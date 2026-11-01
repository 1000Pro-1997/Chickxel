using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Chickxel.Core.Community;

namespace Chickxel.Core.Tests;

/// <summary>
/// 커뮤니티 서비스 client: PKCE(RFC 7636 시험 값), 127.0.0.1 되돌아오기(state 다름 거절·시간 초과·취소·거절 오류),
/// 가짜 HTTP handler로 요청 모양(경로·메서드·Bearer·JSON·multipart)과 오류 응답 읽기, 토큰 저장.
/// </summary>
public class CommunityClientTests
{
    private static readonly Uri Site = new("https://chickxel.com/");

    private sealed record Sent(HttpMethod Method, string Url, string? Authorization, string? ContentType, byte[] Body);

    /// <summary>"메서드 경로" → 응답 함수. 보낸 요청은 본문까지 남긴다. 없는 경로는 404 JSON.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly Dictionary<string, Func<Sent, HttpResponseMessage>> Routes = new(StringComparer.Ordinal);
        public readonly List<Sent> Requests = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var sent = new Sent(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.ToString(), body);
            Requests.Add(sent);
            return Routes.TryGetValue($"{request.Method} {request.RequestUri.AbsolutePath}", out var route)
                ? route(sent)
                : Json(HttpStatusCode.NotFound, """{ "error": "not_found", "message": "Not found" }""");
        }
    }

    private sealed class MemoryTokens : ICommunityTokenStore
    {
        public string? Token;
        public string? Load() => Token;
        public void Save(string token) => Token = token;
        public void Clear() => Token = null;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string UserJson = """{ "id": "abcdefghijklmnop", "handle": "pixel-cat", "name": "Pixel Cat", "avatarUrl": "https://cdn.example/a.png", "createdAt": "2026-11-01T00:00:00Z", "isAdmin": false }""";

    private static (CommunityClient Client, FakeHandler Handler, MemoryTokens Tokens) Make(string? token = "secret-token")
    {
        var handler = new FakeHandler();
        var tokens = new MemoryTokens { Token = token };
        return (new CommunityClient(new HttpClient(handler), Site, "Chickxel-test/1.0", tokens), handler, tokens);
    }

    /// <summary>multipart 본문을 이름 → (파일 이름, 형식, 바이트)로 나눈다.</summary>
    private static Dictionary<string, (string? FileName, string? Type, byte[] Data)> Parts(Sent sent)
    {
        string boundary = sent.ContentType!.Split("boundary=")[1].Trim('"');
        string text = Encoding.Latin1.GetString(sent.Body);
        var parts = new Dictionary<string, (string?, string?, byte[])>(StringComparer.Ordinal);
        foreach (string raw in text.Split("--" + boundary).Skip(1))
        {
            if (raw.StartsWith("--", StringComparison.Ordinal)) break;
            int split = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            string head = raw[..split];
            string data = raw[(split + 4)..^2];
            string Header(string key) => head.Split("\r\n").FirstOrDefault(line => line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) ?? "";
            string disposition = Header("Content-Disposition:");
            string name = disposition.Split("name=\"")[1].Split('"')[0];
            string? file = disposition.Contains("filename=\"", StringComparison.Ordinal) ? disposition.Split("filename=\"")[1].Split('"')[0] : null;
            Assert.DoesNotContain("filename*", disposition);
            string type = Header("Content-Type:");
            parts[name] = (file, type.Length > 0 ? type["Content-Type:".Length..].Trim() : null, Encoding.Latin1.GetBytes(data));
        }
        return parts;
    }

    // ───────────── PKCE ─────────────

    [Fact]
    public void Pkce_MatchesTheRfc7636AppendixBVector()
    {
        byte[] octets = [116, 24, 223, 180, 151, 153, 224, 37, 79, 250, 96, 125, 216, 173, 187, 186, 22, 212, 37, 77, 105, 214, 191, 240, 91, 88, 5, 88, 83, 132, 141, 121];
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        Assert.Equal(verifier, Pkce.Base64Url(octets));
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge(verifier));
    }

    [Fact]
    public void Pkce_NewValuesAreUrlSafeAndUnique()
    {
        string a = Pkce.CreateVerifier(), b = Pkce.CreateVerifier();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);
        Assert.NotEqual(a, b);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", Pkce.Challenge(a));
        Assert.Matches("^[A-Za-z0-9_-]{1,128}$", Pkce.CreateState());
    }

    // ───────────── 되돌아오기 ─────────────

    private static readonly HttpClient Browser = new();

    [Fact]
    public async Task Loopback_RejectsOtherStatesAndPaths_ThenReturnsTheMatchingCode()
    {
        using var receiver = LoopbackReceiver.Start();
        Assert.InRange(receiver.Port, 1024, 65535);
        var wait = receiver.WaitForCodeAsync("good-state", TimeSpan.FromSeconds(20));
        string root = $"http://127.0.0.1:{receiver.Port}";

        using (var wrong = await Browser.GetAsync($"{root}/callback?code=stolen&state=other"))
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using (var other = await Browser.GetAsync($"{root}/favicon.ico"))
            Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.False(wait.IsCompleted);

        using var ok = await Browser.GetAsync($"{root}/callback?code=the-code&state=good-state");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        string page = await ok.Content.ReadAsStringAsync();
        Assert.Contains("창을 닫아도 됩니다", page);
        Assert.Contains("You can close this window", page);
        Assert.Equal("the-code", await wait);

        // 끝나면 더 받지 않는다(연결이 거절되거나 성공이 아닌 답).
        try
        {
            using var again = await Browser.GetAsync($"{root}/callback?code=again&state=good-state");
            Assert.NotEqual(HttpStatusCode.OK, again.StatusCode);
        }
        catch (HttpRequestException)
        {
        }
    }

    [Fact]
    public async Task Loopback_TimesOut_IsCancellable_AndReportsDeniedSignIn()
    {
        using (var receiver = LoopbackReceiver.Start())
        {
            var ex = await Assert.ThrowsAsync<CommunityException>(() => receiver.WaitForCodeAsync("s", TimeSpan.FromMilliseconds(200)));
            Assert.Equal(CommunityErrorCode.LoginTimeout, ex.Code);
        }
        using (var receiver = LoopbackReceiver.Start())
        {
            using var cancel = new CancellationTokenSource();
            var wait = receiver.WaitForCodeAsync("s", TimeSpan.FromMinutes(1), cancel.Token);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        }
        using (var receiver = LoopbackReceiver.Start())
        {
            var wait = receiver.WaitForCodeAsync("s", TimeSpan.FromSeconds(20));
            using var denied = await Browser.GetAsync($"http://127.0.0.1:{receiver.Port}/callback?error=access_denied&state=s");
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            var ex = await Assert.ThrowsAsync<CommunityException>(() => wait);
            Assert.Equal((CommunityErrorCode.LoginFailed, "access_denied"), (ex.Code, ex.ServerMessage));
        }
    }

    [Fact]
    public async Task SignIn_OpensTheLoginPage_ReceivesTheCode_AndExchangesItWithTheVerifier()
    {
        var (client, handler, tokens) = Make(token: null);
        string? challenge = null;
        handler.Routes["POST /api/auth/app/token"] = sent =>
        {
            var body = JsonNode.Parse(sent.Body)!;
            Assert.Equal("one-time-code", body["code"]!.GetValue<string>());
            // 서버처럼 verifier의 S256이 로그인 쪽에 보낸 challenge와 같은지 본다.
            Assert.Equal(challenge, Pkce.Challenge(body["verifier"]!.GetValue<string>()));
            Assert.Null(sent.Authorization);
            return Json(HttpStatusCode.OK, $$"""{ "token": "new-session", "user": {{UserJson.Replace("false", "true")}} }""");
        };
        Uri? opened = null;
        var user = await client.SignInAsync("github", url =>
        {
            opened = url;
            var query = HttpUtility.ParseQueryString(url.Query);
            challenge = query["challenge"];
            // 브라우저 대신: 서버가 로그인 뒤 보내는 되돌아오기.
            _ = Task.Run(() => Browser.GetAsync($"http://127.0.0.1:{query["port"]}/callback?code=one-time-code&state={query["state"]}"));
        });
        Assert.NotNull(opened);
        Assert.Equal("https://chickxel.com/login", opened!.GetLeftPart(UriPartial.Path));
        var q = HttpUtility.ParseQueryString(opened.Query);
        Assert.Equal(("1", "github"), (q["app"], q["provider"]));
        Assert.Matches("^[A-Za-z0-9_-]{43}$", q["challenge"]!);
        Assert.InRange(int.Parse(q["port"]!, System.Globalization.CultureInfo.InvariantCulture), 1024, 65535);
        Assert.Equal(("pixel-cat", true), (user.Handle, user.IsAdmin));
        Assert.Equal("new-session", tokens.Token);
        Assert.Same(user, client.User);
    }

    // ───────────── 요청 모양 ─────────────

    [Fact]
    public async Task Me_SendsTheBearerToken_AndUnauthorizedForgetsIt()
    {
        var (client, handler, tokens) = Make();
        handler.Routes["GET /api/me"] = _ => Json(HttpStatusCode.OK, UserJson);
        var me = await client.GetMeAsync();
        Assert.Equal(new CommunityUser("abcdefghijklmnop", "pixel-cat", "Pixel Cat", "https://cdn.example/a.png", "2026-11-01T00:00:00Z", false), me);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Get, "https://chickxel.com/api/me", "Bearer secret-token"), (sent.Method, sent.Url, sent.Authorization));

        handler.Routes["GET /api/me"] = _ => Json(HttpStatusCode.Unauthorized, """{ "error": "unauthorized", "message": "Sign in" }""");
        Assert.Null(await client.GetMeAsync());
        Assert.Null(tokens.Token);
        Assert.False(client.IsSignedIn);
        Assert.Null(client.User);

        // 로그인하지 않았으면 보내지 않는다.
        Assert.Null(await client.GetMeAsync());
        var upload = await Assert.ThrowsAsync<CommunityException>(() => client.UploadArtAsync(new ArtUpload([1], CommunityArtType.Png, "t", "", [])));
        Assert.Equal(CommunityErrorCode.Unauthorized, upload.Code);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task UpdateMe_PatchesOnlyTheGivenFields()
    {
        var (client, handler, _) = Make();
        handler.Routes["PATCH /api/me"] = _ => Json(HttpStatusCode.OK, UserJson.Replace("pixel-cat", "new-cat"));
        var user = await client.UpdateMeAsync(null, " new-cat ");
        Assert.Equal("new-cat", user.Handle);
        var sent = Assert.Single(handler.Requests);
        Assert.StartsWith("application/json", sent.ContentType);
        Assert.Equal("""{"handle":"new-cat"}""", Encoding.UTF8.GetString(sent.Body));
        Assert.Equal("Bearer secret-token", sent.Authorization);
    }

    [Fact]
    public async Task UploadArt_SendsMultipartFileTitleDescriptionAndTags_WithProgress()
    {
        var (client, handler, _) = Make();
        handler.Routes["POST /api/art"] = _ => Json(HttpStatusCode.Created,
            $$"""
            { "id": "art2345art2345ab", "title": "Idle", "description": "", "tags": ["idle"], "user": {{UserJson}},
              "image": { "url": "/files/art/art2345art2345ab.gif", "width": 32, "height": 16, "frames": 4, "bytes": 300, "type": "gif" },
              "likes": 0, "dislikes": 0, "myVote": 0, "createdAt": "2026-11-01T00:00:00Z" }
            """);
        byte[] gif = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
        var reports = new List<double>();
        var art = await client.UploadArtAsync(new ArtUpload(gif, CommunityArtType.Gif, " Idle ", "첫 줄\n둘째 줄", ["idle", "cat"]), new SyncProgress(reports.Add));

        Assert.Equal(new CommunityArt("art2345art2345ab", "Idle", "/files/art/art2345art2345ab.gif", 32, 16, 4, "gif"), art);
        Assert.Equal("https://chickxel.com/gallery/art2345art2345ab", client.ArtPageUrl(art.Id).AbsoluteUri);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "Bearer secret-token"), (sent.Method, sent.Authorization));
        Assert.Matches("^multipart/form-data; boundary=[A-Za-z0-9]+$", sent.ContentType);
        var parts = Parts(sent);
        Assert.Equal(("art.gif", "image/gif"), (parts["file"].FileName, parts["file"].Type));
        Assert.Equal(gif, parts["file"].Data);
        Assert.Equal("Idle", Encoding.UTF8.GetString(parts["title"].Data));
        Assert.Equal("첫 줄\n둘째 줄", Encoding.UTF8.GetString(parts["description"].Data));
        Assert.Equal("idle,cat", Encoding.UTF8.GetString(parts["tags"].Data));
        Assert.True(reports.Count > 2, reports.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1.0, reports[^1]);
    }

    [Fact]
    public async Task UploadPackage_SendsThePackageSummaryAndTags()
    {
        var (client, handler, _) = Make();
        handler.Routes["POST /api/assets"] = _ => Json(HttpStatusCode.Created,
            """{ "id": "pixel-cat.neon", "version": "1.0.0", "name": "Neon Set", "summary": "s", "review": "community" }""");
        byte[] package = [0x50, 0x4B, 3, 4, 9, 9];
        var result = await client.UploadPackageAsync(package, "pixel-cat.neon-1.0.0.chickxel-asset", "네온 효과", ["neon", "effect"]);
        Assert.Equal(new CommunityPackage("pixel-cat.neon", "1.0.0", "Neon Set", "community"), result);
        Assert.Equal("https://chickxel.com/packages/pixel-cat.neon", client.PackagePageUrl(result.Id).AbsoluteUri);
        var parts = Parts(Assert.Single(handler.Requests));
        Assert.Equal(("pixel-cat.neon-1.0.0.chickxel-asset", "application/zip"), (parts["package"].FileName, parts["package"].Type));
        Assert.Equal(package, parts["package"].Data);
        Assert.Equal("네온 효과", Encoding.UTF8.GetString(parts["summary"].Data));
        Assert.Equal("neon,effect", Encoding.UTF8.GetString(parts["tags"].Data));
    }

    [Fact]
    public async Task SignOut_DeletesTheServerSession_AndForgetsTheTokenEvenIfTheServerFails()
    {
        var (client, handler, tokens) = Make();
        handler.Routes["POST /api/auth/logout"] = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        await client.SignOutAsync();
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(("https://chickxel.com/api/auth/logout", "Bearer secret-token"), (sent.Url, sent.Authorization));
        Assert.Null(tokens.Token);
        Assert.False(client.IsSignedIn);
    }

    // ───────────── 오류 ─────────────

    [Fact]
    public async Task Errors_MapCodeMessageDetailAndRetryAfter()
    {
        var (client, handler, tokens) = Make();
        var upload = new ArtUpload([1, 2, 3], CommunityArtType.Png, "t", "", []);

        handler.Routes["POST /api/art"] = _ => Json(HttpStatusCode.BadRequest,
            """{ "error": "invalid", "message": "Invalid input", "detail": [{ "field": "title", "code": "too_long" }, { "path": "previews/a.png", "message": "bad PNG" }, "plain issue"] }""");
        var invalid = await Assert.ThrowsAsync<CommunityException>(() => client.UploadArtAsync(upload));
        Assert.Equal((CommunityErrorCode.Invalid, 400, "invalid", "Invalid input"), (invalid.Code, invalid.Status, invalid.Error, invalid.ServerMessage));
        Assert.Equal(["title: too_long", "previews/a.png: bad PNG", "plain issue"], invalid.Issues);
        Assert.Contains("Invalid input", invalid.DisplayText);
        Assert.Contains("- title: too_long", invalid.DisplayText);

        handler.Routes["POST /api/art"] = _ =>
        {
            var response = Json((HttpStatusCode)429, """{ "error": "rate_limited", "message": "Slow down" }""");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(150));
            return response;
        };
        var limited = await Assert.ThrowsAsync<CommunityException>(() => client.UploadArtAsync(upload));
        Assert.Equal((CommunityErrorCode.RateLimited, TimeSpan.FromSeconds(150)), (limited.Code, limited.RetryAfter));
        Assert.Contains("3", limited.Message);

        handler.Routes["POST /api/art"] = _ => Json(HttpStatusCode.Forbidden, """{ "error": "banned", "message": "Banned" }""");
        Assert.Equal(CommunityErrorCode.Banned, (await Assert.ThrowsAsync<CommunityException>(() => client.UploadArtAsync(upload))).Code);

        handler.Routes["POST /api/art"] = _ => Json(HttpStatusCode.RequestEntityTooLarge, """{ "error": "too_large", "message": "Too large" }""");
        Assert.Equal(CommunityErrorCode.TooLarge, (await Assert.ThrowsAsync<CommunityException>(() => client.UploadArtAsync(upload))).Code);

        handler.Routes["POST /api/art"] = _ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") };
        var server = await Assert.ThrowsAsync<CommunityException>(() => client.UploadArtAsync(upload));
        Assert.Equal((CommunityErrorCode.Server, 502, (string?)null), (server.Code, server.Status, server.Error));

        handler.Routes["PATCH /api/me"] = _ => Json(HttpStatusCode.Conflict, """{ "error": "conflict", "message": "Handle taken" }""");
        Assert.Equal(CommunityErrorCode.Conflict, (await Assert.ThrowsAsync<CommunityException>(() => client.UpdateMeAsync(null, "taken"))).Code);

        handler.Routes["POST /api/art"] = _ => Json(HttpStatusCode.Created, "[1, 2]");
        Assert.Equal(CommunityErrorCode.BadResponse, (await Assert.ThrowsAsync<CommunityException>(() => client.UploadArtAsync(upload))).Code);

        // 오류 응답에도 토큰은 401일 때만 지운다.
        Assert.Equal("secret-token", tokens.Token);
    }

    [Fact]
    public async Task NetworkFailureAndTimeout_AreMapped_AndCallerCancelStaysCancel()
    {
        var failing = new CommunityClient(new HttpClient(new ThrowingHandler()), Site, "t", new MemoryTokens { Token = "x" });
        Assert.Equal(CommunityErrorCode.Network, (await Assert.ThrowsAsync<CommunityException>(() => failing.GetMeAsync())).Code);

        var slow = new CommunityClient(new HttpClient(new HangingHandler()), Site, "t", new MemoryTokens { Token = "x" },
            new CommunityOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) });
        Assert.Equal(CommunityErrorCode.Timeout, (await Assert.ThrowsAsync<CommunityException>(() => slow.GetMeAsync())).Code);

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var patient = new CommunityClient(new HttpClient(new HangingHandler()), Site, "t", new MemoryTokens { Token = "x" });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => patient.GetMeAsync(cancel.Token));
    }

    [Fact]
    public void BaseUrl_MustBeHttpsExceptLocalDevelopment_AndLoginUrlCarriesTheAppFields()
    {
        Assert.Throws<ArgumentException>(() => new CommunityClient(new HttpClient(), new Uri("http://chickxel.com/"), "t", new MemoryTokens()));
        var local = new CommunityClient(new HttpClient(), new Uri("http://localhost:8787"), "t", new MemoryTokens());
        Assert.Equal("http://localhost:8787/packages/a.b", local.PackagePageUrl("a.b").AbsoluteUri);

        var (client, _, _) = Make();
        Assert.Equal("https://chickxel.com/login?app=1&port=50123&challenge=abc_-&state=st", client.LoginUrl(50123, "abc_-", "st").AbsoluteUri);
        Assert.EndsWith("&provider=discord", client.LoginUrl(50123, "c", "s", "discord").AbsoluteUri);
        Assert.Equal("https://chickxel.com/gallery/u/pixel-cat", client.UserPageUrl("pixel-cat").AbsoluteUri);
        Assert.Equal("https://chickxel.com/packages", client.PackagesPageUrl().AbsoluteUri);
        Assert.Equal("https://chickxel.com/packages?kind=gradient", client.PackagesPageUrl("gradient").AbsoluteUri);
        Assert.Equal("https://chickxel.com/files/avatars/a.png", client.Resolve("/files/avatars/a.png")!.AbsoluteUri);
        Assert.Null(client.Resolve("http://elsewhere.example/a.png"));
    }

    [Fact]
    public void Rules_MatchTheServer()
    {
        Assert.True(CommunityRules.IsValidHandle("pixel-cat"));
        Assert.False(CommunityRules.IsValidHandle("ab"));
        Assert.False(CommunityRules.IsValidHandle("Pixel"));
        Assert.False(CommunityRules.IsValidHandle("-cat"));
        Assert.False(CommunityRules.IsValidHandle(new string('a', 33)));
        Assert.True(CommunityRules.IsValidTitle("Idle"));
        Assert.False(CommunityRules.IsValidTitle("  "));
        Assert.True(CommunityRules.IsValidDescription("a\nb"));
        Assert.False(CommunityRules.IsValidDescription("a\u0001"));
        Assert.False(CommunityRules.IsValidTags(["a", "a"]));
    }

    [Fact]
    public void TokenStore_RoundTrips_AndDoesNotWriteThePlainTokenOnWindows()
    {
        using var dir = new TempDir();
        var store = new CommunityTokenStore(dir.File(Path.Combine("sub", "session.bin")));
        Assert.Null(store.Load());
        store.Save("token-value-123");
        Assert.Equal("token-value-123", store.Load());
        byte[] raw = File.ReadAllBytes(store.Path);
        if (OperatingSystem.IsWindows()) Assert.DoesNotContain("token-value-123", Encoding.Latin1.GetString(raw));
        else Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(store.Path));
        store.Clear();
        Assert.Null(store.Load());
        Assert.False(File.Exists(store.Path));

        // 손상된 파일은 로그인하지 않은 것으로 본다.
        File.WriteAllBytes(store.Path, [1, 2, 3]);
        Assert.Null(store.Load());
    }

    /// <summary>알림을 바로(같은 스레드에서) 받는 진행 표시(<see cref="Progress{T}"/>는 나중에 알림).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
