using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Chickxel.Core.Community;

/// <summary>
/// Discord 활동 상태(Rich Presence의 <c>activity</c>): "앱 이름 하는 중"과 <see cref="Start"/>부터 지난 시간, 큰 그림(그림 주소 또는 Developer Portal에
/// 올린 그림 키)과 그 풍선 글(128자까지 — 길면 잘림). 앱 이름은 Discord 애플리케이션 이름에서 온다.
/// </summary>
public sealed record DiscordActivity(DateTimeOffset Start, string? LargeImage = null, string? LargeText = null)
{
    /// <summary>Discord <c>SET_ACTIVITY</c>의 <c>activity</c> 객체.</summary>
    internal JsonObject ToJson()
    {
        var o = new JsonObject { ["timestamps"] = new JsonObject { ["start"] = Start.ToUnixTimeSeconds() } };
        var assets = new JsonObject();
        if (!string.IsNullOrWhiteSpace(LargeImage)) assets["large_image"] = LargeImage;
        // Discord는 2~128자만 받는다.
        if (LargeText?.Trim() is { Length: >= 2 } text) assets["large_text"] = text.Length <= 128 ? text : text[..127] + "…";
        if (assets.Count > 0) o["assets"] = assets;
        return o;
    }
}

/// <summary>
/// 이 컴퓨터에서 실행 중인 Discord 앱과의 로컬 RPC 연결 하나(Rich Presence). 인터넷이나 계정 정보는 쓰지 않고, Discord가 연
/// 로컬 통로(Windows <c>\\.\pipe\discord-ipc-N</c>, 그 밖은 임시 폴더의 <c>discord-ipc-N</c> 유닉스 소켓, N = 0~9)에 앱 ID로 인사한 뒤
/// 활동을 보낸다. 틀: 작은 끝 정수 둘(종류, 길이) + UTF-8 JSON. 연결을 닫으면 Discord가 그 활동을 지운다.
/// 한 번에 한 요청만(부르는 쪽이 차례로 부름).
/// </summary>
public sealed class DiscordPresenceClient : IAsyncDisposable
{
    private const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;
    private const int MaxFrameBytes = 64 * 1024;
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);

    private readonly Stream _stream;
    private readonly int _pid;

    private DiscordPresenceClient(Stream stream, int pid)
    {
        _stream = stream;
        _pid = pid;
    }

    /// <summary>
    /// 실행 중인 Discord에 연결하고 <paramref name="clientId"/>(Developer Portal의 애플리케이션 ID)로 인사해 준비(<c>READY</c>)를 받는다.
    /// Discord가 없거나 받아 주지 않으면 null.
    /// </summary>
    public static async Task<DiscordPresenceClient?> ConnectAsync(string clientId, CancellationToken cancellationToken = default)
    {
        foreach (var open in Endpoints())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = await open(cancellationToken).ConfigureAwait(false);
            if (stream is null) continue;
            if (await HandshakeAsync(stream, clientId, Environment.ProcessId, cancellationToken).ConfigureAwait(false) is { } client) return client;
        }
        return null;
    }

    /// <summary>열린 통로 <paramref name="stream"/>에서 인사한다(시험용 통로도 받음). 받아 주지 않으면 통로를 닫고 null.</summary>
    internal static async Task<DiscordPresenceClient?> HandshakeAsync(Stream stream, string clientId, int pid, CancellationToken cancellationToken)
    {
        var client = new DiscordPresenceClient(stream, pid);
        try
        {
            await client.WriteAsync(OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = clientId }, cancellationToken).ConfigureAwait(false);
            var ready = await client.ReadReplyAsync(reply => reply["evt"]?.GetValue<string>() == "READY", cancellationToken).ConfigureAwait(false);
            if (ready is not null) return client;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException or TimeoutException or InvalidOperationException or FormatException)
        {
        }
        await client.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// 활동을 바꾼다(null = 지움). Discord의 답을 기다린다. 연결이 끊겼거나 Discord가 거절하면 <see cref="IOException"/>
    /// (부르는 쪽은 이 연결을 버리고 나중에 다시 연결한다).
    /// </summary>
    public async Task SetActivityAsync(DiscordActivity? activity, CancellationToken cancellationToken = default)
    {
        string nonce = Guid.NewGuid().ToString("N");
        var args = new JsonObject { ["pid"] = _pid };
        if (activity is not null) args["activity"] = activity.ToJson();
        try
        {
            await WriteAsync(OpFrame, new JsonObject { ["cmd"] = "SET_ACTIVITY", ["args"] = args, ["nonce"] = nonce }, cancellationToken).ConfigureAwait(false);
            var reply = await ReadReplyAsync(r => r["nonce"]?.GetValue<string>() == nonce, cancellationToken).ConfigureAwait(false)
                        ?? throw new IOException("Discord closed the connection.");
            if (reply["evt"]?.GetValue<string>() == "ERROR")
                throw new IOException("Discord refused the activity: " + reply["data"]?["message"]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is InvalidDataException or TimeoutException or InvalidOperationException or FormatException)
        {
            throw new IOException("Discord connection failed.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// 받은 틀을 읽어 <paramref name="match"/>에 맞는 답을 돌려준다(그 사이 핑에는 퐁으로 답하고 다른 틀은 넘김). Discord가 닫으면 null.
    /// <see cref="ReplyTimeout"/> 안에 오지 않으면 <see cref="TimeoutException"/>.
    /// </summary>
    private async Task<JsonObject?> ReadReplyAsync(Func<JsonObject, bool> match, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReplyTimeout);
        try
        {
            while (true)
            {
                var (op, json) = await ReadAsync(timeout.Token).ConfigureAwait(false);
                switch (op)
                {
                    case OpPing:
                        await WriteAsync(OpPong, json, timeout.Token).ConfigureAwait(false);
                        break;
                    case OpClose:
                        return null;
                    case OpFrame when json is JsonObject frame && match(frame):
                        return frame;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Discord did not answer.");
        }
    }

    private async Task WriteAsync(int op, JsonNode? json, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(json?.ToJsonString() ?? "{}");
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<(int Op, JsonNode? Json)> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        int op = BinaryPrimitives.ReadInt32LittleEndian(header);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length < 0 || length > MaxFrameBytes) throw new InvalidDataException("Bad Discord frame length.");
        var body = new byte[length];
        await _stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return (op, length == 0 ? null : JsonNode.Parse(body));
    }

    /// <summary>Discord가 열어 두는 통로 후보를 차례로 여는 함수들(열리지 않으면 null).</summary>
    private static IEnumerable<Func<CancellationToken, Task<Stream?>>> Endpoints()
    {
        if (OperatingSystem.IsWindows())
        {
            for (int i = 0; i < 10; i++)
            {
                string name = $"discord-ipc-{i}";
                yield return ct => OpenPipeAsync(name, ct);
            }
            yield break;
        }
        // 유닉스: 임시 폴더(XDG_RUNTIME_DIR·TMPDIR 등) 아래, Flatpak·Snap Discord는 그 하위 폴더.
        var roots = new[] { "XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP" }
            .Select(Environment.GetEnvironmentVariable).OfType<string>().Where(d => d.Length > 0).Append("/tmp").Distinct();
        foreach (var root in roots)
            foreach (var sub in new[] { "", "app/com.discordapp.Discord", "snap.discord" })
                for (int i = 0; i < 10; i++)
                {
                    string path = Path.Combine(root, sub, $"discord-ipc-{i}");
                    if (File.Exists(path)) yield return ct => OpenSocketAsync(path, ct);
                }
    }

    private static async Task<Stream?> OpenPipeAsync(string name, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(200, cancellationToken).ConfigureAwait(false);
            return pipe;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    private static async Task<Stream?> OpenSocketAsync(string path, CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
    }
}
