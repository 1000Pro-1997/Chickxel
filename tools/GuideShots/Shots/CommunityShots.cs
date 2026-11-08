using System.Net;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.Community;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>Chickxel 계정 창, 갤러리에 올리기 창, 온라인에 올리기 창. 서버는 가짜 HTTP(내 정보만 답함)로 바꾼다.</summary>
internal static class CommunityShots
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // 레벨 7(동, 90~112 XP 중 103)과 레벨 12(은, 220~252 XP 중 236) — 칩 색 단계가 둘 보이게
    private static readonly CommunityUser Me = new("u1", "pixel-cat", "Pixel Cat", null, "2026-10-01T00:00:00Z", false,
        Level: 7, Xp: 103, LevelStartXp: 90, NextLevelXp: 112);
    private static readonly CommunityUser Other = new("u2", "studio-mole", "Mole Studio", null, "2026-10-02T00:00:00Z", false,
        Level: 12, Xp: 236, LevelStartXp: 220, NextLevelXp: 252);

    /// <summary><c>api/me</c>는 토큰에 맞는 사용자, 나머지는 404(올린 적 없는 새 패키지).</summary>
    private sealed class FakeSite : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.TrimEnd('/') == "/api/me")
            {
                var user = request.Headers.Authorization?.Parameter == "t2" ? Other : Me;
                string json = $$"""{"id":"{{user.Id}}","handle":"{{user.Handle}}","name":"{{user.Name}}","createdAt":"{{user.CreatedAt}}","level":{{user.Level}},"xp":{{user.Xp}},"levelStartXp":{{user.LevelStartXp}},"nextLevelXp":{{user.NextLevelXp}}}""";
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

    /// <summary>1 = 로그인 전(로그인 단추), 2 = 로그인한 뒤(① 계정 ② 레벨·경험치 ③ 프로필 ④ 단추 ⑤ 다른 계정).</summary>
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
            // 계정 줄(프로필 배지 + 레벨 줄), 프로필 묶음, 단추 두 줄, 다른 계정 묶음
            Rect Part(int i) => ShotKit.Bounds(view.Children[i], w).Inflate(3);
            var head = (StackPanel)view.Children[0];
            Rect Head(int i) => ShotKit.Bounds(head.Children[i], w).Inflate(3);
            k.Save(w, "start-account-2", marks: [Head(0), Head(1), Part(1), Part(2).Union(Part(3)), Part(4)]);
        }, settleMs: 700);
    }

    /// <summary>
    /// 갤러리에 올리기 창(타임라인에서 프레임 셋을 고르고 연 것, GIF·태그 idle): ① 올릴 그림·배율 ② GIF 범위 ③ 제목·설명·태그 ④ 계정
    /// ⑤ 미리보기와 공개 안내 ⑥ 갤러리 보기.
    /// </summary>
    [Shot("screen-gallery")]
    public static void Gallery(ShotKit k)
    {
        UseFakeAccount(signedIn: true);
        var main = Open(k);
        Build(k, main);
        k.Dialog(() => GalleryUploadDialog.Run(Ctx(main), main, [1, 2, 4]), w =>
        {
            ShotKit.Pump(500);
            ShotKit.Get<RadioButton>(w, "_gif").IsChecked = true;
            ShotKit.Get<RadioButton>(w, "_rangeTag").IsChecked = true;
            ShotKit.Pump(400);
            var settings = ShotKit.Get<Control>(w, "_form");
            var groups = ((StackPanel)settings).Children.ToList();
            var range = ShotKit.Get<Control>(w, "_rangePanel");
            var preview = (Control)ShotKit.Get<Control>(w, "_preview").Parent!;
            var openGallery = ShotKit.FindText(w, UiStrings.Gallery_OpenGallery, ancestorControl: true);
            // ② 범위 목록: 번호가 위 "애니메이션(GIF)" 단추를 가리지 않게 묶음 왼쪽 여백까지 넓히고 위는 조금 줄인다
            var r = ShotKit.Bounds(range, w);
            var g = ShotKit.Bounds(groups[0], w);
            var rangeMark = new Rect(g.X + 6, r.Y + 4, r.Right + 2 - (g.X + 6), r.Height - 2);
            k.Save(w, "screen-gallery", marks: [groups[0], rangeMark, groups[1], groups[2], preview, openGallery]);
        }, settleMs: 700);
    }
}
