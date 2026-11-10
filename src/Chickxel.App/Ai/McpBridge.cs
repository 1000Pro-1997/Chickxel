using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>
/// <c>Chickxel --mcp</c>: 다른 AI 앱(Claude Desktop·Cursor·Claude Code 등)이 띄우는 stdio MCP 서버. 창 없이 표준 입력의 JSON-RPC 메시지(한 줄에 하나)를
/// 켜져 있는 Chickxel의 MCP 서버(<see cref="McpScope.External"/>)로 넘기고, 답(SSE면 진행 알림과 결과 각각)을 표준 출력에 한 줄씩 쓴다.
/// 주소·토큰은 메시지마다 잠금 파일(<see cref="McpServer.ExternalLockPath"/>)에서 읽으므로 Chickxel을 다시 켜도 다리를 다시 띄울 필요가 없다.
/// Chickxel이 꺼져 있거나 외부 연결이 꺼져 있으면 id가 있는 요청에 JSON-RPC 오류로 답한다. 메시지는 동시에 넘긴다(오래 걸리는 그림 생성 중에도 다른 요청이 감).
/// </summary>
internal static class McpBridge
{
    /// <summary>Chickxel에 닿지 않을 때의 JSON-RPC 오류 코드(서버 정의 범위).</summary>
    private const int UnavailableCode = -32000;

    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly object WriteGate = new();
    private static Stream _stdout = Stream.Null;
    private static volatile string? _sessionId;

    /// <summary>
    /// 다른 AI 앱에 붙여 넣을 MCP 설정(<c>{ "mcpServers": { "chickxel": { "command", "args" } } }</c> — Claude Desktop·Cursor·Claude Code의 <c>.mcp.json</c> 형식):
    /// 지금 실행 파일에 <c>--mcp</c>(빌드 출력을 <c>dotnet Chickxel.App.dll</c>로 돌렸으면 dll 다음에).
    /// </summary>
    public static string ClientConfigJson()
    {
        var exe = Environment.ProcessPath ?? "Chickxel";
        var args = new JsonArray();
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase)) args.Add(typeof(McpBridge).Assembly.Location);
        args.Add("--mcp");
        var config = new JsonObject
        {
            ["mcpServers"] = new JsonObject { [McpServer.ServerName] = new JsonObject { ["command"] = exe, ["args"] = args } },
        };
        return config.ToJsonString(new JsonSerializerOptions(PixelTools.JsonOptions) { WriteIndented = true });
    }

    /// <summary>
    /// 표준 입력이 닫힐 때까지 메시지를 넘긴다. 메시지마다 답의 머리가 올 때까지(보통 도구가 끝날 때까지) 기다린 뒤 다음 메시지를 넘겨 받은 차례대로 실행되게 하고,
    /// 오래 걸리는 도구는 SSE 머리가 바로 오므로 그동안에도 다음 메시지가 간다. 넘기던 답을 다 쓰면 0을 돌려준다.
    /// </summary>
    public static int Run()
    {
        _stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var pending = new List<Task>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            pending.RemoveAll(task => task.IsCompleted);
            pending.Add(ForwardAsync(line).GetAwaiter().GetResult());
        }
        Task.WaitAll(pending);
        return 0;
    }

    /// <summary>메시지 하나를 보내고 답의 머리를 기다린다. 돌려준 작업은 답의 본문(SSE면 이벤트들)을 표준 출력에 쓴다.</summary>
    private static async Task<Task> ForwardAsync(string line)
    {
        JsonNode? message;
        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            Write(Error(null, -32700, "Parse error"));
            return Task.CompletedTask;
        }
        // 답을 받아야 하는 요청(id와 method가 있음)만 실패를 알린다 — 알림·클라이언트 응답은 버린다.
        JsonNode? id = null;
        bool request = message is JsonObject o && o.ContainsKey("method") && o.TryGetPropertyValue("id", out id);
        if (ReadLock() is not { } target)
        {
            if (request) Write(Error(id, UnavailableCode, UiStrings.McpBridge_Unavailable));
            return Task.CompletedTask;
        }
        HttpResponseMessage response;
        try
        {
            using var post = new HttpRequestMessage(HttpMethod.Post, target.Url) { Content = new StringContent(line, new UTF8Encoding(false), "application/json") };
            post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            post.Headers.Accept.ParseAdd("application/json, text/event-stream");
            if (_sessionId is { } session) post.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            response = await Http.SendAsync(post, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
        {
            // 잠금 파일이 남았는데 Chickxel이 끝났으면(강제 종료) 연결이 거부된다.
            if (request) Write(Error(id, UnavailableCode, e is HttpRequestException ? UiStrings.McpBridge_Unavailable : UiStrings.FormatMcpBridge_Failed(message: e.Message)));
            return Task.CompletedTask;
        }
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessions)) _sessionId = sessions.FirstOrDefault();
        return Task.Run(() => RelayAsync(response, id, request));
    }

    /// <summary>답 하나를 표준 출력에 쓴다: 401(외부 연결이 꺼짐)은 안내 오류, 202는 없음, SSE는 이벤트마다, JSON은 그대로.</summary>
    private static async Task RelayAsync(HttpResponseMessage response, JsonNode? id, bool request)
    {
        using (response)
        {
            try
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (request) Write(Error(id, UnavailableCode, UiStrings.McpBridge_Unavailable));
                    return;
                }
                if (response.StatusCode == HttpStatusCode.Accepted) return;
                if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
                {
                    await RelayEventsAsync(response).ConfigureAwait(false);
                    return;
                }
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (body.Length > 0 && JsonNode.Parse(body) is { } reply) Write(reply);
                else if (request) Write(Error(id, -32603, $"HTTP {(int)response.StatusCode}"));
            }
            catch (Exception e) when (e is HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
                if (request) Write(Error(id, UnavailableCode, UiStrings.FormatMcpBridge_Failed(message: e.Message)));
            }
        }
    }

    /// <summary>SSE 답에서 이벤트마다 <c>data:</c> 줄을 모아 메시지 하나로 쓴다(<c>:</c>로 시작하는 신호 줄은 버림).</summary>
    private static async Task RelayEventsAsync(HttpResponseMessage response)
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), new UTF8Encoding(false));
        var data = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0 && JsonNode.Parse(data.ToString()) is { } message) Write(message);
                data.Clear();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart(' '));
            }
        }
        if (data.Length > 0 && JsonNode.Parse(data.ToString()) is { } last) Write(last);
    }

    /// <summary>잠금 파일의 주소·토큰(없거나 읽을 수 없으면 null = Chickxel이 꺼져 있거나 외부 연결이 꺼짐).</summary>
    private static (Uri Url, string Token)? ReadLock()
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(McpServer.ExternalLockPath));
            if (json?["url"]?.GetValue<string>() is { } url && json["token"]?.GetValue<string>() is { Length: > 0 } token
                && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback)
                return (uri, token);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
        }
        return null;
    }

    /// <summary>메시지 하나를 한 줄로 쓴다(여러 요청의 답이 섞이지 않게 한 번에 하나씩).</summary>
    private static void Write(JsonNode message)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString(PixelTools.JsonOptions) + "\n");
        lock (WriteGate)
        {
            _stdout.Write(bytes);
            _stdout.Flush();
        }
    }

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}
