using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Chickxel.Core.Community;

/// <summary>
/// 앱 로그인의 되돌아오기 받는 곳: <c>127.0.0.1</c>의 빈 포트에서 <see cref="HttpListener"/>로 기다리다 서버가 브라우저를
/// <c>/callback?code=…&amp;state=…</c>로 보내면 code를 돌려준다. 브라우저에는 "창을 닫아도 됩니다" 쪽을 보인다.
/// state가 다른 요청(다른 쪽이 보낸 것)은 400으로 거절하고 계속 기다리며, 다른 주소는 404다.
/// 이 컴퓨터 안에서만 열리고(<c>127.0.0.1</c>) 받은 code는 PKCE verifier가 있어야 쓸 수 있다.
/// </summary>
public sealed class LoopbackReceiver : IDisposable
{
    public const string CallbackPath = "/callback";

    private readonly HttpListener _listener;

    private LoopbackReceiver(HttpListener listener, int port)
    {
        _listener = listener;
        Port = port;
    }

    public int Port { get; }

    /// <summary>되돌아오기 주소(<c>http://127.0.0.1:{포트}/callback</c>).</summary>
    public Uri CallbackUrl => new($"http://127.0.0.1:{Port}{CallbackPath}");

    /// <summary>빈 포트(1024 이상)를 골라 기다리기 시작한다. 몇 번 해도 열지 못하면 <see cref="HttpListenerException"/>.</summary>
    public static LoopbackReceiver Start()
    {
        for (int attempt = 1; ; attempt++)
        {
            int port = FreePort();
            if (port < 1024) continue;
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                return new LoopbackReceiver(listener, port);
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                // 고른 포트를 그새 다른 프로그램이 잡았다 — 다른 포트로.
                listener.Close();
            }
        }
    }

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
    /// <paramref name="state"/>가 맞는 되돌아오기를 기다려 code를 돌려준다. 받은 <c>error</c>(사용자가 거절 등)나 code 없음은
    /// <see cref="CommunityErrorCode.LoginFailed"/>, 시간이 넘으면 <see cref="CommunityErrorCode.LoginTimeout"/>,
    /// 부르는 쪽의 취소는 <see cref="OperationCanceledException"/>. 끝나면 더 받지 않는다(다시 부르지 않음).
    /// </summary>
    public async Task<string> WaitForCodeAsync(string state, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(timeout);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = wait.Token.Register(() => stopped.TrySetResult());
        try
        {
            while (true)
            {
                var next = _listener.GetContextAsync();
                if (await Task.WhenAny(next, stopped.Task).ConfigureAwait(false) != next)
                {
                    _ = next.ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new CommunityException(CommunityErrorCode.LoginTimeout);
                }
                if (Handle(await next.ConfigureAwait(false), state) is { } code) return code;
            }
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>요청 하나에 답한다. 맞는 되돌아오기면 code, 아니면 null(계속 기다림). 로그인 실패는 예외.</summary>
    private static string? Handle(HttpListenerContext context, string state)
    {
        var request = context.Request;
        if (request.HttpMethod != "GET" || request.Url?.AbsolutePath != CallbackPath)
        {
            Reply(context.Response, 404, Page(false));
            return null;
        }
        var query = request.QueryString;
        if (query["state"] != state)
        {
            Reply(context.Response, 400, Page(false));
            return null;
        }
        string? code = query["code"];
        if (query["error"] is { Length: > 0 } error || string.IsNullOrEmpty(code) || code.Length > 512)
        {
            Reply(context.Response, 400, Page(false));
            throw new CommunityException(CommunityErrorCode.LoginFailed, serverMessage: query["error"]);
        }
        Reply(context.Response, 200, Page(true));
        return code;
    }

    private static void Reply(HttpListenerResponse response, int status, string html)
    {
        try
        {
            byte[] body = Encoding.UTF8.GetBytes(html);
            response.StatusCode = status;
            response.ContentType = "text/html; charset=utf-8";
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.ContentLength64 = body.Length;
            response.OutputStream.Write(body);
            response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
        {
            // 브라우저가 먼저 끊음 — 받은 값은 그대로 쓴다.
        }
    }

    /// <summary>브라우저에 보이는 작은 쪽(한국어와 영어 두 줄). 화면 글자 외에 아무것도 불러오지 않는다.</summary>
    private static string Page(bool ok)
    {
        string ko = ok ? "Chickxel에 로그인했습니다. 창을 닫아도 됩니다." : "로그인하지 못했습니다. Chickxel로 돌아가 다시 시도하세요.";
        string en = ok ? "You are signed in to Chickxel. You can close this window." : "Sign-in failed. Return to Chickxel and try again.";
        return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Chickxel</title></head>"
               + "<body style=\"font-family:system-ui,sans-serif;background:#1e1f22;color:#e6e6e6;display:flex;align-items:center;justify-content:center;min-height:90vh;margin:0\">"
               + $"<main style=\"text-align:center\"><h1 style=\"font-size:20px\">Chickxel</h1><p>{ko}</p><p>{en}</p></main></body></html>";
    }

    public void Dispose()
    {
        try
        {
            _listener.Close();
        }
        catch (ObjectDisposedException) { }
    }
}
