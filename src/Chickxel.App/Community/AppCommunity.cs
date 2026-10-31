using Chickxel.App.Localization;
using Chickxel.Core.Community;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>
/// 앱 하나의 커뮤니티 client(<see cref="AppInfo.WebsiteUrl"/>, 세션 토큰은 설정 폴더의 <see cref="TokenFileName"/>)와
/// 로그인이 필요한 곳(갤러리 올리기, 에셋 올리기)이 쓰는 "로그인 확인" 흐름.
/// 개발 서버로 시험할 때는 <see cref="UrlVariable"/>로 사이트 주소를 바꾼다(<c>http://localhost:8787</c> 등).
/// </summary>
public static class AppCommunity
{
    public const string UrlVariable = "CHICKXEL_COMMUNITY_URL";
    public const string TokenFileName = "community-session.bin";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static CommunityClient? _client;

    public static CommunityClient Client
        => _client ??= new CommunityClient(Http, BaseUrl, $"{AppUpdate.UserAgent}/{AppInfo.VersionText}",
            new CommunityTokenStore(Path.Combine(Platform.AppFolder, TokenFileName)));

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

    /// <summary>주소를 기본 브라우저로 연다.</summary>
    public static void Open(Visual? owner, Uri url) => Updater.OpenUrl(owner, url.AbsoluteUri);
}
