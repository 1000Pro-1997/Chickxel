using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chickxel.Core.Community;

/// <summary>커뮤니티 client 설정: 요청 시간 한도(보통·올리기), 브라우저 로그인 기다림 한도.</summary>
public sealed record CommunityOptions
{
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan UploadTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Chickxel 커뮤니티 서비스(<c>https://chickxel.com</c>) client: 앱 로그인(브라우저 + 127.0.0.1 되돌아오기 + PKCE), 내 정보 보기·고치기,
/// 로그아웃, 갤러리에 그림 올리기, 에셋 패키지 올리기. <see cref="HttpClient"/>는 부르는 쪽이 준다(test는 가짜 handler).
/// - 로그인한 요청은 <c>Authorization: Bearer</c> 세션 토큰을 보내고, 토큰은 <see cref="ICommunityTokenStore"/>에 둔다(기록에 남기지 않음).
/// - 실패는 <see cref="CommunityException"/>(서버의 <c>{ error, message, detail }</c>를 읽음). 401이면 저장한 토큰을 지운다.
/// - 올리기는 다시 시도하지 않는다(같은 그림이 두 번 올라가지 않게). 부르는 쪽의 취소는 <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class CommunityClient
{
    public static readonly Uri DefaultBaseUrl = new("https://chickxel.com/");
    /// <summary>JSON 응답 최대 바이트.</summary>
    public const int MaxResponseBytes = 1 << 20;

    private readonly HttpClient _http;
    private readonly ICommunityTokenStore _tokens;
    private readonly CommunityOptions _options;
    private readonly string _userAgent;
    private string? _token;
    private bool _loaded;

    /// <param name="baseUrl">사이트 주소. HTTPS만(이 컴퓨터의 개발 서버 <c>http://localhost</c>·<c>127.0.0.1</c>는 됨).</param>
    public CommunityClient(HttpClient http, Uri baseUrl, string userAgent, ICommunityTokenStore tokens, CommunityOptions? options = null)
    {
        if (!baseUrl.IsAbsoluteUri || !(baseUrl.Scheme == Uri.UriSchemeHttps || (baseUrl.Scheme == Uri.UriSchemeHttp && baseUrl.IsLoopback)))
            throw new ArgumentException(baseUrl.ToString(), nameof(baseUrl));
        _http = http;
        BaseUrl = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
        _userAgent = userAgent;
        _tokens = tokens;
        _options = options ?? new CommunityOptions();
    }

    public Uri BaseUrl { get; }

    /// <summary>저장한 세션 토큰이 있는지(서버에서 끝났을 수 있음 — <see cref="GetMeAsync"/>로 확인).</summary>
    public bool IsSignedIn => Token is not null;

    /// <summary>마지막으로 받은 내 정보(로그인·<see cref="GetMeAsync"/>·<see cref="UpdateMeAsync"/>). 모르면 null.</summary>
    public CommunityUser? User { get; private set; }

    private string? Token
    {
        get
        {
            if (!_loaded)
            {
                _token = _tokens.Load();
                _loaded = true;
            }
            return _token;
        }
    }

    // ───────────── 주소 ─────────────

    /// <summary>앱 로그인 쪽: <c>/login?app=1&amp;port=&amp;challenge=&amp;state=</c>, 제공자를 골랐으면 <c>&amp;provider=</c>.</summary>
    public Uri LoginUrl(int port, string challenge, string state, string? provider = null)
    {
        string query = string.Create(CultureInfo.InvariantCulture, $"app=1&port={port}&challenge={Uri.EscapeDataString(challenge)}&state={Uri.EscapeDataString(state)}");
        if (provider is { Length: > 0 }) query += "&provider=" + Uri.EscapeDataString(provider);
        return new Uri(BaseUrl, "login?" + query);
    }

    public Uri ArtPageUrl(string id) => new(BaseUrl, "gallery/" + Uri.EscapeDataString(id));

    public Uri PackagePageUrl(string id) => new(BaseUrl, "packages/" + Uri.EscapeDataString(id));

    /// <summary>에셋 목록 쪽 <c>/packages</c>. 사이트의 종류 탭(<c>effect</c>·<c>gradient</c>)을 주면 <c>?kind=</c>로 그 탭을 연다(null = 사이트 기본 탭).</summary>
    public Uri PackagesPageUrl(string? siteKind = null)
        => new(BaseUrl, siteKind is { Length: > 0 } ? "packages?kind=" + Uri.EscapeDataString(siteKind) : "packages");

    public Uri UserPageUrl(string handle) => new(BaseUrl, "gallery/u/" + Uri.EscapeDataString(handle));

    /// <summary>사이트 기준 경로(<c>/files/…</c>)나 절대 주소를 절대 주소로. HTTPS(또는 개발 서버와 같은 사이트)가 아니면 null.</summary>
    public Uri? Resolve(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(BaseUrl, url, out var resolved)) return null;
        return resolved.Scheme == Uri.UriSchemeHttps || resolved.Authority == BaseUrl.Authority ? resolved : null;
    }

    // ───────────── 로그인 ─────────────

    /// <summary>
    /// 브라우저 로그인: 127.0.0.1 빈 포트에서 기다리고, <paramref name="openBrowser"/>로 로그인 쪽을 연 뒤 되돌아온 code를 PKCE verifier와 함께
    /// 토큰으로 바꿔 저장한다. <paramref name="provider"/>(google·github·discord)는 로그인 쪽이 그 단추로 바로 가게 하는 힌트다.
    /// </summary>
    public async Task<CommunityUser> SignInAsync(string? provider, Action<Uri> openBrowser, CancellationToken cancellationToken = default)
    {
        string verifier = Pkce.CreateVerifier(), state = Pkce.CreateState();
        using var receiver = LoopbackReceiver.Start();
        openBrowser(LoginUrl(receiver.Port, Pkce.Challenge(verifier), state, provider));
        string code = await receiver.WaitForCodeAsync(state, _options.LoginTimeout, cancellationToken).ConfigureAwait(false);
        return await ExchangeCodeAsync(code, verifier, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>POST /api/auth/app/token</c> <c>{ code, verifier }</c> → 토큰을 저장하고 내 정보를 돌려준다.</summary>
    public async Task<CommunityUser> ExchangeCodeAsync(string code, string verifier, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["code"] = code, ["verifier"] = verifier };
        var json = await SendAsync(HttpMethod.Post, "api/auth/app/token", Json(body), auth: false, _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
        string token = CommunityJson.String(json?["token"]) is { Length: > 0 and <= 512 } t && t.All(c => c is > ' ' and < (char)127)
            ? t : throw CommunityJson.Bad("token");
        var user = CommunityUser.Read(json!["user"]);
        _tokens.Save(token);
        _token = token;
        _loaded = true;
        User = user;
        return user;
    }

    /// <summary><c>GET /api/me</c>. 로그인하지 않았거나 세션이 끝났으면(401, 토큰을 지움) null.</summary>
    public async Task<CommunityUser?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn) return null;
        try
        {
            var json = await SendAsync(HttpMethod.Get, "api/me", null, auth: true, _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
            return User = CommunityUser.Read(json);
        }
        catch (CommunityException ex) when (ex.Code == CommunityErrorCode.Unauthorized)
        {
            return null;
        }
    }

    /// <summary><c>PATCH /api/me</c> <c>{ name?, handle? }</c>(null인 칸은 보내지 않음) → 바뀐 내 정보.</summary>
    public async Task<CommunityUser> UpdateMeAsync(string? name, string? handle, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (name is not null) body["name"] = name.Trim();
        if (handle is not null) body["handle"] = handle.Trim();
        var json = await SendAsync(HttpMethod.Patch, "api/me", Json(body), auth: true, _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
        return User = CommunityUser.Read(json);
    }

    /// <summary>
    /// 로그아웃: 서버 세션을 지우고(<c>POST /api/auth/logout</c> — 연결 문제나 이미 끝난 세션이어도 계속) 저장한 토큰을 지운다.
    /// </summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (IsSignedIn) await SendAsync(HttpMethod.Post, "api/auth/logout", null, auth: true, _options.RequestTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (CommunityException) { }
        finally
        {
            ForgetToken();
        }
    }

    private void ForgetToken()
    {
        _tokens.Clear();
        _token = null;
        _loaded = true;
        User = null;
    }

    // ───────────── 올리기 ─────────────

    /// <summary>
    /// 갤러리에 그림을 올린다(<c>POST /api/art</c> multipart: <c>file</c>, <c>title</c>, <c>description</c>, <c>tags</c> 쉼표 목록).
    /// <paramref name="progress"/>는 보낸 비율 0~1.
    /// </summary>
    public async Task<CommunityArt> UploadArtAsync(ArtUpload upload, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string extension = upload.Type == CommunityArtType.Gif ? "gif" : "png";
        var form = Form(
            FilePart("file", "art." + extension, "image/" + extension, upload.Data),
            TextPart("title", upload.Title.Trim()),
            TextPart("description", upload.Description.Trim()),
            TextPart("tags", string.Join(",", upload.Tags)));
        var json = await SendAsync(HttpMethod.Post, "api/art", new ProgressContent(form, progress), auth: true, _options.UploadTimeout, cancellationToken).ConfigureAwait(false);
        return CommunityArt.Read(json);
    }

    /// <summary>
    /// 에셋 패키지(<c>.chickxel-asset</c>)를 올린다(<c>POST /api/assets</c> multipart: <c>package</c>, <c>summary</c>, <c>tags</c>).
    /// 서버가 패키지 검사에서 거절하면 <see cref="CommunityErrorCode.Invalid"/>와 <see cref="CommunityException.Issues"/>.
    /// </summary>
    public async Task<CommunityPackage> UploadPackageAsync(byte[] package, string fileName, string summary, IReadOnlyList<string> tags,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var form = Form(
            FilePart("package", fileName, "application/zip", package),
            TextPart("summary", summary.Trim()),
            TextPart("tags", string.Join(",", tags)));
        var json = await SendAsync(HttpMethod.Post, "api/assets", new ProgressContent(form, progress), auth: true, _options.UploadTimeout, cancellationToken).ConfigureAwait(false);
        return CommunityPackage.Read(json);
    }

    /// <summary>
    /// <c>GET /api/assets/{id}</c>: 올린 패키지의 최신 버전(게시 창의 "새 패키지 / 업데이트" 판단). 없으면(404) null.
    /// 공개 조회라 토큰을 붙이지 않는다(끝난 세션이 여기서 지워지지 않게). 연결 실패 등은 <see cref="CommunityException"/>.
    /// </summary>
    public async Task<CommunityPackage?> GetPackageAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await SendAsync(HttpMethod.Get, "api/assets/" + Uri.EscapeDataString(id), null, auth: false, _options.RequestTimeout, cancellationToken)
                .ConfigureAwait(false);
            return CommunityPackage.Read(json);
        }
        catch (CommunityException ex) when (ex.Code == CommunityErrorCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// multipart 본문. 경계 값은 영문·숫자만 써서 머리에 따옴표 없이 적는다(<c>boundary=…</c> — 따옴표를 다르게 읽는 서버가 없게).
    /// </summary>
    private static MultipartFormDataContent Form(params HttpContent[] parts)
    {
        string boundary = "chickxel" + Guid.NewGuid().ToString("N");
        var form = new MultipartFormDataContent(boundary);
        form.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=" + boundary);
        foreach (var part in parts) form.Add(part);
        return form;
    }

    /// <summary>파일 칸: 이름을 <c>filename*</c> 없이 따옴표로 적는다(ASCII 이름만 씀).</summary>
    private static HttpContent FilePart(string name, string fileName, string mediaType, byte[] data)
    {
        // 64 KiB씩 보내 진행 비율이 고르게 오른다.
        var content = new StreamContent(new MemoryStream(data, writable: false), 64 * 1024);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = Quote(name), FileName = Quote(AsciiFileName(fileName)) };
        return content;
    }

    private static HttpContent TextPart(string name, string value)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(value));
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = Quote(name) };
        return content;
    }

    private static string Quote(string value) => "\"" + value + "\"";

    private static string AsciiFileName(string name)
    {
        var chars = name.Select(c => c is >= ' ' and < (char)127 and not ('"' or '\\' or '/') ? c : '_').ToArray();
        return chars.Length == 0 ? "file" : new string(chars);
    }

    private static ByteArrayContent Json(JsonObject body)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body.ToJsonString()));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    // ───────────── HTTP ─────────────

    /// <summary>요청 하나(다시 시도 없음). 성공이면 JSON 객체(본문이 없으면 null), 아니면 <see cref="CommunityException"/>.</summary>
    private async Task<JsonObject?> SendAsync(HttpMethod method, string path, HttpContent? content, bool auth, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var owned = content;
        string? token = null;
        if (auth && (token = Token) is null) throw new CommunityException(CommunityErrorCode.Unauthorized);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(BaseUrl, path)) { Content = content };
            request.Headers.UserAgent.ParseAdd(_userAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token).ConfigureAwait(false);
            byte[] body = await ReadLimitedAsync(response.Content, attempt.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = Error(response, body);
                if (auth && error.Code == CommunityErrorCode.Unauthorized) ForgetToken();
                throw error;
            }
            if (body.Length == 0 || response.StatusCode == HttpStatusCode.NoContent) return null;
            return JsonNode.Parse(body) as JsonObject ?? throw CommunityJson.Bad("body");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CommunityException(CommunityErrorCode.Timeout);
        }
        catch (HttpRequestException ex)
        {
            throw new CommunityException(CommunityErrorCode.Network, serverMessage: ex.Message, inner: ex);
        }
        catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CommunityException(CommunityErrorCode.Network, serverMessage: ex.Message, inner: ex);
        }
        catch (JsonException ex)
        {
            throw new CommunityException(CommunityErrorCode.BadResponse, inner: ex);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxResponseBytes) throw new CommunityException(CommunityErrorCode.BadResponse, serverMessage: "size");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxResponseBytes) throw new CommunityException(CommunityErrorCode.BadResponse, serverMessage: "size");
        }
        return buffer.ToArray();
    }

    /// <summary>실패 응답 → 예외: 본문 <c>{ error, message, detail }</c>(없거나 JSON이 아니면 HTTP 상태만), <c>Retry-After</c>.</summary>
    private static CommunityException Error(HttpResponseMessage response, byte[] body)
    {
        int status = (int)response.StatusCode;
        string? error = null, message = null;
        IReadOnlyList<string> issues = [];
        try
        {
            if (body.Length > 0 && JsonNode.Parse(body) is JsonObject o)
            {
                error = CommunityJson.String(o["error"]);
                message = CommunityJson.String(o["message"]);
                issues = CommunityException.IssuesOf(o["detail"]);
            }
        }
        catch (JsonException) { }
        TimeSpan? retry = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero,
            _ => null,
        };
        return new CommunityException(CommunityException.CodeOf(error, status), status, error, message, issues, retry);
    }

    /// <summary>안쪽 내용을 그대로 보내며 보낸 비율을 알린다(<see cref="MultipartFormDataContent"/>의 머리·길이를 이어받음).</summary>
    private sealed class ProgressContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly IProgress<double>? _progress;

        public ProgressContent(HttpContent inner, IProgress<double>? progress)
        {
            _inner = inner;
            _progress = progress;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            long total = _inner.Headers.ContentLength ?? 0;
            await using var counting = new CountingStream(stream, sent => _progress?.Report(total > 0 ? Math.Min(1, (double)sent / total) : 0));
            await _inner.CopyToAsync(counting, cancellationToken).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _inner.Headers.ContentLength ?? -1;
            return length >= 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>쓰기만 하는 감싸개: 쓴 바이트 수를 알린다(안쪽 스트림은 닫지 않음).</summary>
    private sealed class CountingStream(Stream inner, Action<long> report) : Stream
    {
        private long _written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _written; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            report(_written += buffer.Length);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            report(_written += buffer.Length);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
