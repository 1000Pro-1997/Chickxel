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
        AppAssets.Store.SavePersonalRecipe(EffectRecipe.Capture("", "Gold outline", [new OutlineEffect { Color = new Color32(255, 230, 120, 255) }]));
        AppAssets.Store.SavePersonalRecipe(EffectRecipe.Capture("", "Hurt", [new HitFlashEffect { Color = new Color32(255, 80, 80, 255) }, new ShakeEffect()]));
        ScreenTimelineShots.InstallCatalogPackage("chickxel.effects.shiny-pickup-1.0.1.chickxel-asset");
        ScreenTimelineShots.InstallCatalogPackage("chickxel.effects.hit-reaction-1.0.1.chickxel-asset");
        ScreenTimelineShots.InstallCatalogPackage("chickxel.gradients.sunset-1.0.0.chickxel-asset");
    }

    private static Rect Union(Window w, params Control[] controls)
        => controls.Select(c => ShotKit.Bounds(c, w)).Aggregate((a, b) => a.Union(b));

    private static Rect PadLeft(Rect r, double dx) => new(r.X - dx, r.Y, r.Width + dx, r.Height);

    /// <summary>
    /// 1 = 설치됨 탭(① 탭 ② 거르기·검색 ③ 가져오기·내보내기·올리기 ④ 목록 ⑤ 상세), 2 = 상점 탭(⑥ 거르기·검색·태그·새로 고침·홈페이지에서 보기 ⑦ 목록 ⑧ 상세).
    /// </summary>
    [Shot("screen-asset-manager")]
    public static void AssetManager(ShotKit k)
    {
        Seed();
        UseLocalCatalog();
        var main = k.Main();
        k.Settle(300);

        k.Dialog(() => AssetManagerWindow.Open(main), w =>
        {
            ShotKit.Pump(500);
            var tabs = ShotKit.Get<TabControl>(w, "_tabs");
            ShotKit.Set(w, "_selectKey", "pkg:chickxel.effects.shiny-pickup");
            ShotKit.Call(w, "Refresh");
            ShotKit.Pump(800);
            var filter = ShotKit.Get<Control>(w, "_filter");
            var search = ShotKit.Get<Control>(w, "_search");
            var toolbar = (Panel)filter.Parent!;
            var buttons = toolbar.Children.Skip(2).ToArray();
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
        }, settleMs: 700);
    }
}
