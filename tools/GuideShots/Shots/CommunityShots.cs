using System.Net;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Chickxel.App;
using Chickxel.Core.Community;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>Chickxel 계정 창, 갤러리에 올리기 창, 온라인에 올리기 창. 서버는 가짜 HTTP(내 정보만 답함)로 바꾼다.</summary>
internal static class CommunityShots
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly CommunityUser Me = new("u1", "pixel-cat", "Pixel Cat", null, "2026-10-01T00:00:00Z", false);
    private static readonly CommunityUser Other = new("u2", "studio-mole", "Mole Studio", null, "2026-10-02T00:00:00Z", false);

    /// <summary><c>api/me</c>는 토큰에 맞는 사용자, 나머지는 404(올린 적 없는 새 패키지).</summary>
    private sealed class FakeSite : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.TrimEnd('/') == "/api/me")
            {
                var user = request.Headers.Authorization?.Parameter == "t2" ? Other : Me;
                string json = $$"""{"id":"{{user.Id}}","handle":"{{user.Handle}}","name":"{{user.Name}}","createdAt":"{{user.CreatedAt}}"}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":"not found"}""", Encoding.UTF8, "application/json") });
        }
    }

    private sealed class MemoryAccounts(CommunityAccounts accounts) : ICommunityAccountStore
    {
        private CommunityAccounts _accounts = accounts;
        public CommunityAccounts Load() => _accounts;
        public void Save(CommunityAccounts accounts) => _accounts = accounts;
    }

    /// <summary>앱의 계정 client를 가짜 서버로 바꾼다. <paramref name="signedIn"/>이면 두 계정(지금 = pixel-cat)으로 로그인해 둔다.</summary>
    internal static void UseFakeAccount(bool signedIn)
    {
        var accounts = signedIn ? new CommunityAccounts([new CommunityAccount("t1", Me), new CommunityAccount("t2", Other)], 0) : CommunityAccounts.Empty;
        var client = new CommunityClient(new HttpClient(new FakeSite()), new Uri("https://chickxel.guide.test/"), "GuideShots", new MemoryAccounts(accounts));
        if (signedIn) client.GetMeAsync().GetAwaiter().GetResult();
        typeof(AppCommunity).GetField("_client", Any)!.SetValue(null, client);
    }

    /// <summary>1 = 로그인 전(로그인 단추), 2 = 로그인한 뒤(① 계정 ② 프로필 ③ 단추 ④ 다른 계정).</summary>
    [Shot("start-account")]
    public static void Account(ShotKit k)
    {
        var main = k.Main();
        UseFakeAccount(signedIn: false);
        k.Dialog(() => AccountDialog.Show(main), w => k.Save(w, "start-account"));

        UseFakeAccount(signedIn: true);
        k.Dialog(() => AccountDialog.Show(main), w =>
        {
            ShotKit.Pump(600);
            var view = ShotKit.Get<StackPanel>(w, "_view");
            // 계정 줄, 프로필 묶음, 단추 두 줄, 다른 계정 묶음
            Rect Part(int i) => ShotKit.Bounds(view.Children[i], w).Inflate(3);
            k.Save(w, "start-account-2", marks: [Part(0), Part(1), Part(2).Union(Part(3)), Part(4)]);
        }, settleMs: 700);
    }

    /// <summary>갤러리에 올리기 창: ① 올릴 그림·배율 ② 제목·설명·태그 ③ 계정 ④ 미리보기와 공개 안내.</summary>
    [Shot("screen-gallery")]
    public static void Gallery(ShotKit k)
    {
        UseFakeAccount(signedIn: true);
        var main = Open(k);
        Build(k, main);
        k.Dialog(() => k.Command("파일/갤러리에 올리기…"), w =>
        {
            ShotKit.Pump(500);
            var settings = ShotKit.Get<Control>(w, "_form");
            var groups = ((StackPanel)settings).Children.ToList();
            var preview = (Control)ShotKit.Get<Control>(w, "_preview").Parent!;
            k.Save(w, "screen-gallery", marks: [groups[0], groups[1], groups[2], preview]);
        }, settleMs: 700);
    }
}
