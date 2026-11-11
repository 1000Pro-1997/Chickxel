using System.Net;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace GuideShots;

/// <summary>화면 구성: 에셋 관리자(설치됨·상점 탭, 온라인에 올리기 창). 온라인 목록은 저장소의 <c>docs/catalog</c>를 가짜 HTTP로 내어 준다.</summary>
internal static class ScreenAssetShots
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>요청 주소를 <c>docs/catalog</c> 아래 파일로 돌려준다(없으면 404).</summary>
    private sealed class CatalogFiles(string folder, Uri root) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string relative = Uri.UnescapeDataString(root.MakeRelativeUri(request.RequestUri!).ToString());
            string path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            var response = File.Exists(path)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(path)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    /// <summary>앱의 온라인 카탈로그 client를 <c>docs/catalog</c>를 읽는 것으로 바꾼다.</summary>
    private static void UseLocalCatalog()
    {
        var root = new Uri("https://catalog.guide.test/catalog/");
        var http = new HttpClient(new CatalogFiles(Path.Combine(ShotKit.Root, "docs", "catalog"), root));
        var client = new RemoteAssetClient(http, new Uri(root, RemoteCatalogFormat.FirstPageName), AppAssets.Store.CacheDirectory, "GuideShots");
        typeof(AppAssets).GetField("_remote", Any)!.SetValue(null, client);
    }

    /// <summary>개인 에셋 셋(레시피 둘·스크립트 하나)과 설치한 공식 패키지 셋.</summary>
    private static void Seed()
    {
        AppAssets.Store.SaveRecipe(EffectRecipe.Capture("", "Gold outline", [new OutlineEffect { Color = new Color32(255, 230, 120, 255) }]));
        AppAssets.Store.SaveRecipe(EffectRecipe.Capture("", "Hurt", [new HitFlashEffect { Color = new Color32(255, 80, 80, 255) }, new ShakeEffect()]));
        ScreenTimelineShots.InstallCatalogPackage("chickxel.effects.shiny-pickup-1.0.1.chickxel-asset");
        ScreenTimelineShots.InstallCatalogPackage("chickxel.effects.hit-reaction-1.0.1.chickxel-asset");
        ScreenTimelineShots.InstallCatalogPackage("chickxel.gradients.sunset-1.0.0.chickxel-asset");
    }

    private static Rect Union(Window w, params Control[] controls)
        => controls.Select(c => ShotKit.Bounds(c, w)).Aggregate((a, b) => a.Union(b));

    private static Rect PadLeft(Rect r, double dx) => new(r.X - dx, r.Y, r.Width + dx, r.Height);

    /// <summary>
    /// 1 = 설치됨 탭(① 탭 ② 거르기·검색 ③ 가져오기·내보내기·올리기 ④ 목록 ⑤ 상세), 2 = 상점 탭(⑥ 거르기·검색·태그·새로 고침·홈페이지에서 보기 ⑦ 목록 ⑧ 상세),
    /// 4 = 설치됨 탭 모양 펜 칸에서 내 세트를 고르고 내보내기 메뉴를 연 모습(⑨ 캔버스에서 새 세트·기본 세트 되돌리기 ⑩ 내 세트 줄 ⑪ 기본 세트 줄 ⑫ 상세 ⑬ 단추 ⑭ 내보내기 메뉴),
    /// 5 = 기본 세트(꽃 1)를 고른 모습(⑮ 상세 ⑯ 단추),
    /// 3 = 온라인에 올리기 창(⑨ 작성자 ⑩ 패키지 ⑪ 라이선스 ⑫ 올릴 에셋 ⑬ 목록 미리보기 ⑭ 올리기 전 검사 — 따로 된 쪽 screen-asset-publish).
    /// </summary>
    [Shot("screen-asset-manager")]
    public static void AssetManager(ShotKit k)
    {
        Seed();
        UseLocalCatalog();
        var main = k.Main();
        k.Settle(300);
        // 모양 펜 칸(그림 4)의 내 세트: 기본 나뭇잎 세트의 모양을 담은 직접 만든 세트
        var shapeCtx = ToolFileHelp.Ctx(main);
        var leaves = shapeCtx.InstalledShapeSets.First(s => s.Id == OfficialShapeSets.Packages.First(p => p.Id == "leaves").AssetId);
        var mySet = new StampSet { Name = "My leaves" };
        mySet.Shapes.AddRange(leaves.Shapes);
        shapeCtx.ShapeSets.Sets.Add(mySet);
        shapeCtx.NotifyShapeSetsEdited();
        k.Settle(200);

        k.Dialog(() => AssetManagerWindow.Open(main), w =>
        {
            // 종류 거르기가 세 칸(효과·그라데이션·모양 펜)이라 도구 막대가 한 줄에 들도록 넓히고, 상세의 단추 줄이 보이게 높인다.
            w.Width = 1120;
            w.Height = 760;
            ShotKit.Pump(500);
            var tabs = ShotKit.Get<TabControl>(w, "_tabs");
            ShotKit.Set(w, "_selectKey", "asset:" + AppAssets.Catalog.FromPackage("chickxel.effects.shiny-pickup")[0].Id);
            ShotKit.Call(w, "Refresh");
            ShotKit.Pump(800);
            var filter = ShotKit.Get<Control>(w, "_filter");
            var search = ShotKit.Get<Control>(w, "_search");
            var toolbar = (Panel)filter.Parent!;
            var buttons = toolbar.Children.Skip(2).Where(c => c.IsVisible).ToArray();
            var list = ShotKit.Get<Control>(w, "_list");
            var detail = (Control)ShotKit.Get<Control>(w, "_detail").Parent!;
            var headers = tabs.Items.OfType<TabItem>().Select(t => ShotKit.Bounds(t, w)).Aggregate((a, b) => a.Union(b));
            k.Save(w, "screen-asset-manager", marks:
            [
                PadLeft(headers, 10),
                PadLeft(Union(w, filter, search), 10),
                Union(w, buttons),
                list,
                detail,
            ]);

            // 2: 상점 — 목록을 받은 뒤 설치하지 않은 프리셋 하나를 고른다
            tabs.SelectedIndex = 1;
            var remoteList = ShotKit.Get<ListBox>(w, "_remoteList");
            ShotKit.Pump(200);
            for (int i = 0; i < 150 && ShotKit.Get<bool>(w, "_remoteLoading"); i++) ShotKit.Pump(100);
            ShotKit.Set(w, "_remoteSelectId", "chickxel.effects.outlined-drop-shadow");
            ShotKit.Call(w, "RefreshRemote");
            ShotKit.Pump(1500);
            var remoteFilter = ShotKit.Get<Control>(w, "_remoteFilter");
            var remoteDetail = (Control)ShotKit.Get<Control>(w, "_remoteDetail").Parent!;
            k.Save(w, "screen-asset-manager-2", marks:
            [
                PadLeft(ShotKit.Bounds((Control)remoteFilter.Parent!, w), 10),
                remoteList,
                remoteDetail,
            ], firstNumber: 6);

            // 4: 설치됨 탭 모양 펜 칸 — 내 세트를 고르고 내보내기 메뉴를 연다
            // 모양 펜 칸은 도구 막대 단추가 둘 늘어 한 줄에 들도록 더 넓힌다.
            w.Width = 1300;
            tabs.SelectedIndex = 0;
            ShotKit.Pump(300);
            ShotKit.Get<TabStrip>(w, "_filter").SelectedIndex = 2;
            ShotKit.Pump(400);
            ShotKit.Set(w, "_selectKey", "shape:" + mySet.Id);
            ShotKit.Call(w, "Refresh");
            ShotKit.Pump(800);
            var listBox = ShotKit.Get<ListBox>(w, "_list");
            var listRect = ShotKit.Bounds(listBox, w);
            var rows = listBox.Items.OfType<ListBoxItem>().ToList();
            var builtInRows = rows.Skip(1).Where(r => r.IsEffectivelyVisible && r.Bounds.Height > 0)
                .Select(r => ShotKit.Bounds(r, w)).Aggregate((a, b) => a.Union(b)).Intersect(listRect.Deflate(2));
            var detailPanel = ShotKit.Get<StackPanel>(w, "_detail");
            var actions = (Control)detailPanel.Children[^1];
            var info = Union(w, [.. detailPanel.Children.Take(detailPanel.Children.Count - 1)]);
            var export = ShotKit.FindText(actions, UiStrings.Assets_ExportSetMenu, ancestorControl: true);
            export.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            ShotKit.Pump(600);
            var menu = ToolFileShotsMenus.OpenMenuRect(w) ?? throw new InvalidOperationException("export menu did not open");
            k.Save(w, "screen-asset-manager-4", marks:
            [
                Union(w, ShotKit.Get<Control>(w, "_newSetFromCanvas"), ShotKit.Get<Control>(w, "_restoreSets")),
                PadLeft(ShotKit.Bounds(rows[0], w), 4),
                PadLeft(builtInRows, 4),
                info,
                actions,
                menu,
            ], firstNumber: 9);
            PopupMenus.CloseAll();
            ShotKit.Pump(300);

            // 5: 기본 세트(꽃 1) — 앱이 기본으로 설치한 상점 에셋의 상세와 단추
            ShotKit.Set(w, "_selectKey", "asset:" + OfficialShapeSets.Packages.First(p => p.Id == "flowers").AssetId);
            ShotKit.Call(w, "Refresh");
            ShotKit.Pump(800);
            var receivedActions = (Control)detailPanel.Children[^1];
            k.Save(w, "screen-asset-manager-5", marks:
            [
                Union(w, [.. detailPanel.Children.Take(detailPanel.Children.Count - 1)]),
                receivedActions,
            ], firstNumber: 15);
        }, settleMs: 700);

        // 3: 온라인에 올리기 창 — 로그인한 계정(가짜 서버)으로 내 스크립트 효과 하나를 연다
        CommunityShots.UseFakeAccount(signedIn: true);
        var script = AppAssets.Store.SaveScript(PulseScript, null, null);
        k.Dialog(() => _ = AssetPublishDialog.RunAsync(main, script.Id), w =>
        {
            ShotKit.Pump(1200);
            Control Group(string title) => ShotKit.All<Border>(w).First(b => b.Child is StackPanel { Children: [TextBlock t, ..] } && t.Text == title);
            var preview = ShotKit.FindText(w, UiStrings.Publish_Preview).FindAncestorOfType<Border>()!;
            k.Save(w, "screen-asset-manager-3", marks:
            [
                Group(UiStrings.Publish_Author),
                Group(UiStrings.Publish_Package),
                Group(UiStrings.Publish_License),
                Group(UiStrings.Publish_Asset),
                preview,
                Group(UiStrings.Publish_Checks),
            ], firstNumber: 9);
        }, settleMs: 900);
    }

    private const string PulseScript = """
        effect({
          name: "Color pulse",
          description: "Pulses the sprite toward a color.",
          language: "en",
          tags: ["color", "flash"],
          animated: true,
          settings: [
            { key: "color", label: "Color", type: "color", default: "#ff6060" },
            { key: "strength", label: "Strength", type: "int", min: 0, max: 100, default: 60 },
          ],
          render(img, ctx, s) {
            const c = ctx.color("color", 0, 0);
            const k = s.strength / 100 * Math.sin(Math.PI * ctx.progress);
            const d = img.data;
            for (let i = 0; i < d.length; i += 4) {
              if (d[i + 3] === 0) continue;
              d[i] += Math.round((c.r - d[i]) * k);
              d[i + 1] += Math.round((c.g - d[i + 1]) * k);
              d[i + 2] += Math.round((c.b - d[i + 2]) * k);
            }
          },
        });
        """;
}
