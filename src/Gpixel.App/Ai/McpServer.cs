using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gpixel.App.Ai;

/// <summary>
/// 앱 안에서 도는 최소 MCP 서버(Streamable HTTP, <see cref="HttpListener"/>). 127.0.0.1의 빈 포트에서
/// <c>POST /mcp</c>로 JSON-RPC 2.0 메시지(하나 또는 묶음)를 받고, Bearer 토큰이 맞아야 응답한다.
/// <para>
/// 요청마다 백그라운드 작업에서 처리하고, <c>tools/call</c>만 UI 스레드로 넘겨 <see cref="PixelTools.Call"/>을 부른다.
/// 어떤 예외도 받기 루프 밖으로 나가지 않는다. 응답이 필요 없는 메시지(알림·응답)는 202, GET은 405, DELETE는 200.
/// </para>
/// </summary>
public sealed class McpServer : IDisposable
{
    public const string ServerName = "gpixel";

    private const string DefaultProtocolVersion = "2025-06-18";
    private const int MaxBodyBytes = 16 * 1024 * 1024;

    private readonly PixelTools _tools;
    private readonly object _gate = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private HttpListener? _listener;
    private string _host = "127.0.0.1";
    private int _port;
    private string? _configPath;
    private volatile bool _disposed;

    public McpServer(PixelTools tools)
    {
        _tools = tools;
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }

    /// <summary>"http://127.0.0.1:포트/mcp"(시작 전이면 빈 문자열).</summary>
    public string Url => _port == 0 ? "" : $"http://{_host}:{_port}/mcp";

    /// <summary>요청 헤더 <c>Authorization: Bearer …</c>에 넣어야 하는 무작위 토큰(32바이트 16진수).</summary>
    public string Token { get; }

    /// <summary>빈 포트를 찾아 듣기 시작한다. 이미 시작했으면 아무것도 안 한다.</summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_listener is not null) return;
            Exception? last = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int port = FreePort();
                foreach (var host in Hosts())
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://{host}:{port}/");
                    try
                    {
                        listener.Start();
                    }
                    catch (Exception e) when (e is HttpListenerException or SocketException)
                    {
                        last = e;
                        try { listener.Close(); } catch { /* 이미 닫힘 */ }
                        continue;
                    }
                    _listener = listener;
                    _host = host;
                    _port = port;
                    _ = Task.Run(() => AcceptLoopAsync(listener));
                    return;
                }
            }
            throw new InvalidOperationException($"MCP 서버를 시작하지 못했습니다: {last?.Message}", last);
        }
    }

    /// <summary>
    /// 들을 주소 후보. Windows(http.sys)는 관리자 권한 없이 IP 주소 접두사를 등록할 수 없는 경우가 있어
    /// 실패하면 localhost로 다시 시도한다.
    /// </summary>
    private static string[] Hosts() => OperatingSystem.IsWindows() ? ["127.0.0.1", "localhost"] : ["127.0.0.1"];

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    /// <summary>
    /// Claude Code의 <c>--mcp-config</c> 파일을 임시 폴더에 쓰고 경로를 돌려준다(서버가 꺼져 있으면 시작).
    /// 토큰이 들어 있으므로 macOS·Linux에서는 본인만 읽을 수 있게 만든다.
    /// </summary>
    public string WriteMcpConfigFile()
    {
        Start();
        var config = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [ServerName] = new JsonObject
                {
                    ["type"] = "http",
                    ["url"] = Url,
                    ["headers"] = new JsonObject { ["Authorization"] = "Bearer " + Token },
                },
            },
        };
        var path = Path.Combine(Path.GetTempPath(), $"gpixel-mcp-{Environment.ProcessId}.json");
        var bytes = Encoding.UTF8.GetBytes(config.ToJsonString(PixelTools.JsonOptions));
        File.Delete(path);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var fs = new FileStream(path, options)) fs.Write(bytes);
        _configPath = path;
        return path;
    }

    public void Dispose()
    {
        HttpListener? listener;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            listener = _listener;
            _listener = null;
        }
        try { listener?.Close(); } catch { /* 종료 중 오류는 무시 */ }
        if (_configPath is { } path)
        {
            try { File.Delete(path); } catch { /* 지우지 못해도 임시 폴더라 괜찮다 */ }
        }
    }

    // ───────────── HTTP ─────────────

    private async Task AcceptLoopAsync(HttpListener listener)
    {
        while (!_disposed)
        {
            HttpListenerContext context;
            try
            {
                if (!listener.IsListening) return;
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (_disposed || !SafeIsListening(listener)) return;
                try { await Task.Delay(50).ConfigureAwait(false); } catch { /* 무시 */ }
                continue;
            }
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private static bool SafeIsListening(HttpListener listener)
    {
        try { return listener.IsListening; }
        catch { return false; }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            var request = context.Request;
            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (path != "/mcp")
            {
                response.StatusCode = 404;
                return;
            }
            if (!Authorized(request))
            {
                response.StatusCode = 401;
                response.AddHeader("WWW-Authenticate", "Bearer");
                return;
            }
            switch (request.HttpMethod)
            {
                case "POST":
                    await HandlePostAsync(request, response).ConfigureAwait(false);
                    break;
                case "DELETE":
                    response.StatusCode = 200;
                    break;
                default:
                    response.StatusCode = 405;
                    response.AddHeader("Allow", "POST, DELETE");
                    break;
            }
        }
        catch (Exception)
        {
            try { response.StatusCode = 500; } catch { /* 이미 헤더를 보냄 */ }
        }
        finally
        {
            try { response.Close(); } catch { /* 연결이 끊김 */ }
        }
    }

    private bool Authorized(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"]?.Trim();
        const string scheme = "Bearer ";
        if (header is null || !header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return false;
        var given = Encoding.UTF8.GetBytes(header[scheme.Length..].Trim());
        var expected = Encoding.UTF8.GetBytes(Token);
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }

    private async Task HandlePostAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.ContentLength64 > MaxBodyBytes)
        {
            response.StatusCode = 413;
            return;
        }
        byte[] body;
        using (var ms = new MemoryStream())
        {
            var buffer = new byte[16384];
            int n;
            while ((n = await request.InputStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                if (ms.Length + n > MaxBodyBytes)
                {
                    response.StatusCode = 413;
                    return;
                }
                ms.Write(buffer, 0, n);
            }
            body = ms.ToArray();
        }

        JsonNode? root;
        try
        {
            root = ParseBody(body);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            await WriteJsonAsync(response, 400, ErrorResponse(null, -32700, "Parse error")).ConfigureAwait(false);
            return;
        }

        if (root is JsonArray batch)
        {
            if (batch.Count == 0)
            {
                await WriteJsonAsync(response, 400, ErrorResponse(null, -32600, "Invalid Request: empty batch")).ConfigureAwait(false);
                return;
            }
            var replies = new JsonArray();
            bool initialize = false;
            foreach (var item in batch.ToList())
            {
                initialize |= IsInitialize(item);
                if (await ProcessAsync(item).ConfigureAwait(false) is { } reply) replies.Add(reply);
            }
            if (initialize) response.AddHeader("Mcp-Session-Id", _sessionId);
            if (replies.Count == 0) response.StatusCode = 202;
            else await WriteJsonAsync(response, 200, replies).ConfigureAwait(false);
        }
        else
        {
            if (IsInitialize(root)) response.AddHeader("Mcp-Session-Id", _sessionId);
            if (await ProcessAsync(root).ConfigureAwait(false) is { } reply) await WriteJsonAsync(response, 200, reply).ConfigureAwait(false);
            else response.StatusCode = 202;
        }
    }

    private static bool IsInitialize(JsonNode? node)
        => node is JsonObject o && AsString(o["method"]) == "initialize" && o.ContainsKey("id");

    /// <summary>본문 JSON 읽기(UTF-8 BOM 허용). 잘못된 JSON이면 <see cref="JsonException"/>.</summary>
    private static JsonNode? ParseBody(byte[] body)
    {
        ReadOnlySpan<byte> span = body;
        if (span.StartsWith("\uFEFF"u8)) span = span[3..];
        return JsonNode.Parse(span);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, JsonNode body)
    {
        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString(PixelTools.JsonOptions));
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

    // ───────────── JSON-RPC ─────────────

    /// <summary>메시지 하나를 처리한다. 응답이 필요 없으면(알림·클라이언트 응답) null.</summary>
    private async Task<JsonObject?> ProcessAsync(JsonNode? node)
    {
        if (node is not JsonObject message) return ErrorResponse(null, -32600, "Invalid Request");
        bool hasId = message.TryGetPropertyValue("id", out var id);
        var method = AsString(message["method"]);
        if (method is null)
        {
            // 클라이언트가 보낸 응답(result/error)은 받기만 한다.
            if (message.ContainsKey("result") || message.ContainsKey("error")) return null;
            return ErrorResponse(hasId ? id : null, -32600, "Invalid Request");
        }
        if (!hasId) return null; // 알림

        var parameters = message["params"] as JsonObject;
        try
        {
            switch (method)
            {
                case "initialize":
                    return Result(id, new JsonObject
                    {
                        ["protocolVersion"] = AsString(parameters?["protocolVersion"]) ?? DefaultProtocolVersion,
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                        ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = "1.0" },
                    });
                case "ping":
                    return Result(id, new JsonObject());
                case "tools/list":
                    return Result(id, new JsonObject { ["tools"] = _tools.ListTools() });
                case "tools/call":
                    var name = AsString(parameters?["name"]);
                    if (string.IsNullOrEmpty(name)) return ErrorResponse(id, -32602, "Invalid params: tool name is required");
                    if (parameters!["arguments"] is { } argNode && argNode is not JsonObject)
                        return ErrorResponse(id, -32602, "Invalid params: arguments must be an object");
                    var arguments = parameters["arguments"] as JsonObject;
                    return Result(id, await CallToolAsync(name, arguments).ConfigureAwait(false));
                default:
                    return ErrorResponse(id, -32601, $"Method not found: {method}");
            }
        }
        catch (Exception e)
        {
            return ErrorResponse(id, -32603, "Internal error: " + e.Message);
        }
    }

    /// <summary>도구를 UI 스레드에서 실행한다. 예외는 isError 결과로 바꾼다.</summary>
    private async Task<JsonObject> CallToolAsync(string name, JsonObject? arguments)
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync<JsonObject>(() => _tools.Call(name, arguments));
        }
        catch (Exception e)
        {
            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"{name} 실패: {e.Message}" }),
                ["isError"] = true,
            };
        }
    }

    private static JsonObject Result(JsonNode? id, JsonNode result)
        => new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };

    private static JsonObject ErrorResponse(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private static string? AsString(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
