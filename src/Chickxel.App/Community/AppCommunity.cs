using Chickxel.App.Localization;
using Chickxel.Core.Community;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>
/// 앱 하나의 커뮤니티 client(<see cref="AppInfo.WebsiteUrl"/>, 로그인한 계정들은 설정 폴더의 <see cref="AccountsFileName"/>)와
/// 로그인이 필요한 곳(갤러리 올리기, 에셋 올리기)이 쓰는 "로그인 확인" 흐름, 계정 창·계정 단추가 함께 쓰는 프로필 그림.
/// 개발 서버로 시험할 때는 <see cref="UrlVariable"/>로 사이트 주소를 바꾼다(<c>http://localhost:8787</c> 등).
/// </summary>
public static class AppCommunity
{
    public const string UrlVariable = "CHICKXEL_COMMUNITY_URL";
    /// <summary>계정 파일(예전 한 계정 세션 파일과 같은 이름 — 예전 파일은 그 계정 하나로 읽힘).</summary>
    public const string AccountsFileName = "community-session.bin";

    private const int MaxAvatarBytes = 2 << 20;

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly HttpClient AvatarHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    /// <summary>받은 프로필 그림(주소 → 그림). UI 스레드에서만 쓴다.</summary>
    private static readonly Dictionary<Uri, Task<Bitmap?>> Avatars = [];
    private static CommunityClient? _client;

    public static CommunityClient Client
        => _client ??= new CommunityClient(Http, BaseUrl, $"{AppUpdate.UserAgent}/{AppInfo.VersionText}",
            new CommunityAccountStore(Path.Combine(Platform.AppFolder, AccountsFileName)));

    /// <summary>사이트 주소: <see cref="UrlVariable"/>(HTTPS나 이 컴퓨터의 http)가 있으면 그것, 아니면 홈페이지.</summary>
    private static Uri BaseUrl
        => Environment.GetEnvironmentVariable(UrlVariable) is { Length: > 0 } custom && Uri.TryCreate(custom, UriKind.Absolute, out var url)
           && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback))
            ? url
            : new Uri(AppInfo.WebsiteUrl);

    /// <summary>
    /// 로그인한 내 정보. 저장한 세션이 있으면 서버에 확인하고(끝났으면 계정 창), 없으면 <paramref name="reason"/>을 보이며 계정 창을 연다.
    /// 로그인하지 않고 닫았거나 확인하지 못했으면(오류 상자를 보임) null.
    /// </summary>
    public static async Task<CommunityUser?> RequireUserAsync(Window owner, string reason)
    {
        var client = Client;
        if (client.IsSignedIn && client.User is null)
        {
            try
            {
                await client.GetMeAsync();
            }
            catch (CommunityException ex)
            {
                Ui.Error(owner, ex.DisplayText, UiStrings.Account_Title);
                return null;
            }
        }
        if (client.User is { } user) return user;
        AccountDialog.Show(owner, reason);
        return client.User;
    }

    /// <summary>
    /// 지금 계정을 서버에 조용히 확인한다(저장한 이름·프로필 그림을 새것으로, 끝난 세션이면 목록에서 빠짐). 연결 문제는 넘긴다.
    /// 확인했으면 true, 세션이 끝났으면 false, 확인하지 못했거나 로그인하지 않았으면 null.
    /// </summary>
    public static async Task<bool?> VerifyQuietlyAsync()
    {
        if (!Client.IsSignedIn) return null;
        try
        {
            return await Client.GetMeAsync() is not null;
        }
        catch (CommunityException)
        {
            return null;
        }
    }

    /// <summary>계정 이름(정보를 아직 모르면 "확인하지 않은 계정").</summary>
    public static string DisplayName(CommunityUser? user) => user?.Name ?? UiStrings.Account_Unverified;

    /// <summary>둥근 프로필 그림 칸: 처음엔 사람 아이콘, 그림을 받으면 바꾼다(HTTPS·2 MiB 이하, 못 받으면 아이콘 그대로).</summary>
    public static Border Avatar(CommunityUser? user, double size)
    {
        double icon = Math.Round(size * 0.72);
        var host = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), ClipToBounds = true,
            Child = new IconView(VectorIcon.CircleUser, AppTheme.DimTextBrush) { Width = icon, Height = icon },
            VerticalAlignment = VerticalAlignment.Center,
        };
        LoadAvatar(host, user?.AvatarUrl);
        return host;
    }

    private static async void LoadAvatar(Border host, string? url)
    {
        if (Client.Resolve(url) is not { } uri) return;
        if (!Avatars.TryGetValue(uri, out var download)) Avatars[uri] = download = DownloadAvatarAsync(uri);
        if (await download is not { } bitmap)
        {
            // 다음에 다시 받아 본다(잠깐의 연결 문제로 이번 실행 내내 비지 않게).
            Avatars.Remove(uri);
            return;
        }
        host.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill, Width = host.Width, Height = host.Height };
    }

    private static async Task<Bitmap?> DownloadAvatarAsync(Uri uri)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await AvatarHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxAvatarBytes) return null;
            byte[] data = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (data.Length > MaxAvatarBytes) return null;
            using var stream = new MemoryStream(data, writable: false);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or ArgumentException or InvalidOperationException
                                       or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>주소를 기본 브라우저로 연다.</summary>
    public static void Open(Visual? owner, Uri url) => Updater.OpenUrl(owner, url.AbsoluteUri);
}
