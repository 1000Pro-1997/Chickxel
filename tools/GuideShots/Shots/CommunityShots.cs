using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    // 레벨 7(동, 75~91 XP 중 85)과 레벨 12(은, 165~186 XP 중 176) — 배지 색 단계가 둘 보이게. 보상은 서버처럼 칸마다 자동(그 레벨까지 가장 높은 것).
    private static readonly CommunityUser Me = new("u1", "pixel-cat", "Pixel Cat", null, "2026-10-01T00:00:00Z", false,
        Level: 7, Xp: 85, LevelStartXp: 75, NextLevelXp: 91, Rewards: Auto(7));
    private static readonly CommunityUser Other = new("u2", "studio-mole", "Mole Studio", null, "2026-10-02T00:00:00Z", false,
        Level: 12, Xp: 176, LevelStartXp: 165, NextLevelXp: 186, Rewards: Auto(12));

    private static readonly JsonSerializerOptions RewardsJson = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static ProfileRewards Auto(int level)
    {
        string? Id(RewardSlot slot) => LevelRewards.Highest(slot, level)?.Id;
        return new(Id(RewardSlot.Title), Id(RewardSlot.Badge), Id(RewardSlot.Frame), Id(RewardSlot.Effect), Id(RewardSlot.Card), Id(RewardSlot.NameStyle));
    }

    /// <summary><c>api/me</c>는 토큰에 맞는 사용자, 나머지는 404(올린 적 없는 새 패키지).</summary>
    private sealed class FakeSite : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath.TrimEnd('/');
            // 레벨 튜토리얼 새싹 보상 받기도 내 정보로 답한다(받은 횟수는 튜토리얼 점검이 봄).
            if (path == "/api/me/tutorial-rewards" && request.Method == HttpMethod.Post) TutorialClaims++;
            if (path is "/api/me" or "/api/me/tutorial-rewards")
            {
                var user = request.Headers.Authorization?.Parameter == "t2" ? Other : Me;
                string json = $$"""{"id":"{{user.Id}}","handle":"{{user.Handle}}","name":"{{user.Name}}","createdAt":"{{user.CreatedAt}}","level":{{user.Level}},"xp":{{user.Xp}},"levelStartXp":{{user.LevelStartXp}},"nextLevelXp":{{user.NextLevelXp}},"rewards":{{JsonSerializer.Serialize(user.Rewards, RewardsJson)}}}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":"not found"}""", Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>가짜 서버가 받은 <c>POST /api/me/tutorial-rewards</c> 수.</summary>
    internal static int TutorialClaims;

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

    /// <summary>1 = 로그인 전(로그인 단추), 2 = 로그인한 뒤(① 계정 ② 레벨·경험치 ③ 레벨 보상 ④ 프로필 ⑤ 단추 ⑥ 다른 계정).</summary>
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
            // 프로필 카드(프로필 배지 + 레벨 줄), 레벨 보상, 프로필 묶음, 단추 두 줄, 다른 계정 묶음
            Rect Part(int i) => ShotKit.Bounds(view.Children[i], w).Inflate(3);
            var head = ((Panel)((Border)view.Children[0]).Child!).Children.OfType<StackPanel>().First();
            Rect Head(int i) => ShotKit.Bounds(head.Children[i], w).Inflate(3);
            k.Save(w, "start-account-2", marks: [Head(0), Head(1), Part(1), Part(2), Part(3).Union(Part(4)), Part(5)]);
        }, settleMs: 700);
    }

    /// <summary>
    /// 갤러리에 올리기 창(타임라인에서 프레임 셋을 고르고 연 것, GIF·태그 idle): ① 올릴 그림 목록 ② GIF 범위 ③ 제목·설명·태그 ④ 계정
    /// ⑤ 미리보기와 공개 안내 ⑥ 갤러리 보기. 이어서 ⑦ 올릴 그림의 순서 ⑧ 더하기 ⑨ 같은 그림 경고.
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
            var form = ShotKit.Get<StackPanel>(w, "_form");
            var listPane = (Control)form.Children[0];
            var settings = (StackPanel)form.Children[1];
            var groups = settings.Children.ToList();
            var range = ShotKit.Get<Control>(w, "_rangePanel");
            var preview = (Control)ShotKit.Get<Control>(w, "_preview").Parent!;
            var openGallery = ShotKit.FindText(w, UiStrings.Gallery_OpenGallery, ancestorControl: true);
            // GIF 범위의 번호가 위 단추를 가리지 않도록 묶음 왼쪽 여백까지 포함한다.
            var r = ShotKit.Bounds(range, w);
            var g = ShotKit.Bounds(groups[0], w);
            var rangeMark = new Rect(g.X + 6, r.Y + 4, r.Right + 2 - (g.X + 6), r.Height - 2);
            k.Save(w, "screen-gallery", marks: [listPane, rangeMark, groups[2], groups[3], preview, openGallery]);

            ShotKit.Call(w, "AddItem", CommunityArtType.Png);
            ShotKit.Call(w, "AddItem", CommunityArtType.Gif);
            ShotKit.Get<RadioButton>(w, "_rangeTag").IsChecked = true;
            ShotKit.Pump(400);
            var add = ShotKit.FindText(w, UiStrings.Gallery_Add, ancestorControl: true);
            var warning = ShotKit.Get<Control>(w, "_listWarning");
            k.Save(w, "screen-gallery-2", crop: ShotKit.Bounds(listPane, w).Inflate(8), marks:
                [ShotKit.Get<Control>(w, "_list"), add, warning], firstNumber: 7);
        }, settleMs: 700);
    }
}
