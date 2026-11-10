using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.Model;
using static GuideShots.ScreenDemo;
using static GuideShots.ScreenTimelineShots;

namespace GuideShots;

/// <summary>
/// 효과 만들기 창(<see cref="EffectMakerWindow"/>): 내 스크립트 셋을 저장해 두고 창을 열어, 목록·정보·원문·미리보기 설정 값·미리보기·상태·AI 칸을 찍는다.
/// 번호는 한 쪽(screen-effect-maker) 안에서 그림 1~9를 이어 붙인다. 예시 원문은 영어(가이드 그림이 영어 화면이라서).
/// </summary>
internal static class EffectMakerShots
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    /// <summary>올린 효과를 보일 때의 예시 계정 핸들(<see cref="AppAssets.MyHandle"/>).</summary>
    private const string Handle = "pixel-cat";

    /// <summary>1·3·8·9의 열린 효과: 색이 들썩이는 효과(정수 + 색 설정).</summary>
    private const string GlowScript = """
        effect({
          name: "Glow pulse",
          author: "pixel-cat",
          description: "Tints the character with a color that pulses in and out.",
          tags: ["color", "pulse"],
          animated: true,
          previewFrames: 6,
          settings: [
            { key: "color", label: "Glow color", type: "color", default: "#ff70c8" },
            { key: "strength", label: "Strength", type: "int", min: 0, max: 100, default: 70 },
          ],
          render(img, ctx, s) {
            const k = s.strength / 100 * Math.sin(Math.PI * ctx.progress);
            const d = img.data, w = img.width;
            for (let i = 0; i < d.length; i += 4) {
              if (d[i + 3] === 0) continue;
              const p = i / 4, x = p % w, y = Math.floor(p / w);
              const c = ctx.color("color", x, y);
              d[i] += Math.round((c.r - d[i]) * k);
              d[i + 1] += Math.round((c.g - d[i + 1]) * k);
              d[i + 2] += Math.round((c.b - d[i + 2]) * k);
            }
          },
        });
        """;

    private const string WobbleScript = """
        effect({
          name: "Jelly wobble",
          author: "pixel-cat",
          animated: true,
          settings: [
            { key: "amount", label: "Amount", type: "int", min: 0, max: 4, default: 2 },
          ],
          render(img, ctx, s) {
            const d = img.data, w = img.width, h = img.height;
            for (let y = 0; y < h; y++) {
              const dx = Math.round(Math.sin(ctx.progress * Math.PI * 2 + y / 6) * s.amount);
              if (dx === 0) continue;
              for (let n = 0; n < w; n++) {
                const x = dx > 0 ? w - 1 - n : n, sx = x - dx, i = (y * w + x) * 4;
                if (sx < 0 || sx >= w) { d[i + 3] = 0; continue; }
                const j = (y * w + sx) * 4;
                d[i] = d[j]; d[i + 1] = d[j + 1]; d[i + 2] = d[j + 2]; d[i + 3] = d[j + 3];
              }
            }
          },
        });
        """;

    private const string SparkleScript = """
        effect({
          name: "Sparkle trail",
          author: "pixel-cat",
          animated: true,
          settings: [
            { key: "color", label: "Sparkle color", type: "color", default: "#fff6b0" },
            { key: "seed", label: "Seed", type: "int", min: 0, max: 9999, default: 7 },
          ],
          render(img, ctx, s) {
            const d = img.data, w = img.width, h = img.height;
            for (let y = 0; y < h; y++) {
              for (let x = 0; x < w; x++) {
                const i = (y * w + x) * 4;
                if (d[i + 3] === 0 || ctx.hash(x, y, s.seed, ctx.step) < 0.97) continue;
                const c = ctx.color("color", x, y);
                d[i] = c.r; d[i + 1] = c.g; d[i + 2] = c.b;
              }
            }
          },
        });
        """;

    /// <summary>4·10: 범위 첫 그림을 끝 그림(끝 그림 병아리)으로 픽셀마다 옮겨 바꾸는 변신(비교 그림 <see cref="GuideScreenEffectsCompareShots.EndOffset"/>도 씀).</summary>
    internal const string MorphScript = """
        effect({
          name: "Morph",
          author: "pixel-cat",
          description: "Turns the first image of the range into the last one, pixel by pixel.",
          animated: true,
          previewFrames: 6,
          previewEndChick: "wizard",
          render(img, ctx, s) {
            const a = ctx.frameSource(ctx.rangeStart), b = ctx.frameSource(ctx.rangeEnd);
            if (!a || !b) return;
            const m = ctx.matchPixels(a, b);
            if (!m) return;
            const d = img.data, w = img.width, h = img.height, t = ctx.progress;
            for (let i = 0; i < d.length; i++) d[i] = 0;
            for (let k = 0; k < m.count; k++) {
              const ax = m.data[k * 4], ay = m.data[k * 4 + 1], bx = m.data[k * 4 + 2], by = m.data[k * 4 + 3];
              const x = Math.round(lerp(ax, bx, t)), y = Math.round(lerp(ay, by, t));
              if (x < 0 || y < 0 || x >= w || y >= h) continue;
              const ia = (ay * a.width + ax) * 4, ib = (by * b.width + bx) * 4, i = (y * w + x) * 4;
              d[i] = Math.round(lerp(a.data[ia], b.data[ib], t));
              d[i + 1] = Math.round(lerp(a.data[ia + 1], b.data[ib + 1], t));
              d[i + 2] = Math.round(lerp(a.data[ia + 2], b.data[ib + 2], t));
              d[i + 3] = 255;
            }
          },
        });
        """;

    /// <summary>5·6: 레이어 설정으로 고른 바닥 레이어(ctx.layer)를 읽어 발밑 바닥에 그림자를 드리운다.</summary>
    private const string ShadowScript = """
        effect({
          name: "Ground shadow",
          author: "pixel-cat",
          description: "Casts a soft shadow onto the ground layer under the character.",
          animated: true,
          previewFrames: 6,
          settings: [
            { key: "ground", label: "Ground", type: "layer", default: "ground" },
            { key: "size", label: "Size", type: "int", min: 20, max: 200, default: 110 },
          ],
          render(img, ctx, s) {
            const g = ctx.layer("ground");
            if (!g) return;
            const d = img.data, w = img.width, h = img.height;
            let minX = w, maxX = -1, maxY = -1;
            for (let y = 0; y < h; y++) {
              for (let x = 0; x < w; x++) {
                if (d[(y * w + x) * 4 + 3] === 0) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                maxY = y;
              }
            }
            if (maxX < 0) return;
            const cx = (minX + maxX + 1) / 2;
            let top = -1;
            for (let y = maxY; y < h && top < 0; y++) {
              if (g.data[(y * w + Math.floor(cx)) * 4 + 3] > 0) top = y;
            }
            if (top < 0) return;
            const rx = (maxX - minX + 1) / 2 * s.size / 100 * (0.85 + 0.15 * Math.sin(Math.PI * 2 * ctx.progress));
            const ry = Math.max(1.5, rx / 4);
            for (let y = Math.floor(top - ry); y <= Math.ceil(top + ry); y++) {
              for (let x = Math.floor(cx - rx); x <= Math.ceil(cx + rx); x++) {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                const ex = (x + 0.5 - cx) / rx, ey = (y + 0.5 - top) / ry;
                const i = (y * w + x) * 4;
                if (ex * ex + ey * ey > 1 || d[i + 3] > 0 || g.data[i + 3] === 0) continue;
                d[i] = 24; d[i + 1] = 16; d[i + 2] = 36; d[i + 3] = 150;
              }
            }
          },
        });
        """;

    /// <summary>7: 글자열을 써서 Jint로 돌고(빠른 경로 아님), 색을 한 번만 읽어 그라데이션 권장 안내가 나오는 효과.</summary>
    private const string BlinkScript = """
        effect({
          name: "Blink flash",
          author: "pixel-cat",
          animated: true,
          previewFrames: 6,
          settings: [
            { key: "color", label: "Flash color", type: "color", default: "#ffffff" },
          ],
          render(img, ctx, s) {
            const pattern = "x.x.x.";
            if (pattern[ctx.step % pattern.length] !== "x") return;
            const c = s.color, d = img.data;
            for (let i = 0; i < d.length; i += 4) {
              if (d[i + 3] === 0) continue;
              d[i] = c.r; d[i + 1] = c.g; d[i + 2] = c.b;
            }
          },
        });
        """;

    /// <summary>
    /// 효과 만들기: 1 = 창 전체(①~⑬), 2 = 올린 효과가 보이는 목록(⑭ 올림 표시 ⑮ 이 컴퓨터에 없는 올린 효과), 3 = 미리보기 움직임 창(⑯ 움직임 ⑰ 칸마다 자리),
    /// 4 = 변신(⑱ 끝 그림 병아리 ⑲ 칸 띠), 5 = 레이어 설정과 미리보기 장면(⑳ 레이어 설정 ㉑ 장면 줄), 6 = 장면을 메인 편집기에서 고치는 동안의 작은 창(㉒),
    /// 7 = 상태 창(㉓ 상태 단추 ㉔ 자세한 상태), 8 = 규칙 창(㉕ 원문 포함·복사), 9 = AI 칸(㉖ 대화·입력 ㉗ 첨부 단추 ㉘ 모델·보내기),
    /// 10 = 미리보기 자리·피벗 창(번호 없음 — 가이드에서 4와 5 사이에 놓이고 세 줄은 표로 설명).
    /// </summary>
    [Shot("screen-effect-maker")]
    public static void EffectMaker(ShotKit k)
    {
        // 내 스크립트 셋(목록), 그중 Glow pulse를 연다
        var glow = AppAssets.Store.SaveScript(GlowScript, null, null);
        var wobble = AppAssets.Store.SaveScript(WobbleScript, null, null);
        AppAssets.Store.SaveScript(SparkleScript, null, null);
        var main = OpenTall(k, 300);
        Build(k, main);
        EffectMakerWindow.Open(main, Ctx(main));
        k.Settle(300);
        var maker = k.Windows.OfType<EffectMakerWindow>().Single();
        ShotKit.Call(maker, "TryOpen", glow.Id);
        WaitPreview(k, maker);
        // 고칠 수 있는 효과를 열면 AI 칸이 저절로 열린다 — 창 전체 그림(①~⑬)은 AI 칸을 닫고 찍는다(AI 칸은 9에서 대본 세션으로)
        ShotKit.Get<ToggleButton>(maker, "_aiToggle").IsChecked = false;
        k.Settle(300);

        var list = ShotKit.Get<ListBox>(maker, "_list");
        var header = (Control)ShotKit.Get<Control>(maker, "_showAll").Parent!;
        var newRow = ((StackPanel)header.Parent!).Children[1];
        var title = ShotKit.Get<Control>(maker, "_title");
        var code = ShotKit.Get<Control>(maker, "_code");
        var settings = ShotKit.Get<StackPanel>(maker, "_scriptSettings");
        var preview = ShotKit.Get<Control>(maker, "_preview");
        var strip = ShotKit.Get<Control>(maker, "_stripScroll");
        var chick = ShotKit.Get<Control>(maker, "_chick");
        var chickRows = (Grid)chick.Parent!;
        var play = (Control)ShotKit.Get<Control>(maker, "_play").Parent!;
        var aiToggle = ShotKit.Get<Control>(maker, "_aiToggle");
        var status = ShotKit.Get<Control>(maker, "_statusButton");
        var previewTop = (Control)aiToggle.Parent!;

        // 1: 창 전체(재생 중인 미리보기)
        k.Save(maker, marks:
        [
            Union(maker, header, list),
            newRow,
            GroupOf(title, list),
            ShotKit.FindText(maker, UiStrings.EffectMaker_Rules, ancestorControl: true),
            code,
            GroupOf(settings, code),
            aiToggle,
            Union(maker, preview, strip),
            chickRows,
            play,
            Union(maker, ShotKit.Get<Control>(maker, "_save"), ShotKit.Get<Control>(maker, "_insert")),
            status,
            Union(maker, ShotKit.Get<Control>(maker, "_validate"), (Control)ShotKit.Get<Control>(maker, "_upload").Parent!),
        ]);

        // 3: 미리보기 움직임 창 — 오른쪽으로 가며 한 번 뛰는 직접 움직임(표의 Y를 고쳐 "직접"이 됨). 창을 닫으면(취소) 원문은 그대로
        k.Dialog(() => ShotKit.Call(maker, "EditPreviewMotion"), d =>
        {
            var choice = ShotKit.Get<ComboBox>(d, "_choice");
            choice.SelectedIndex = PreviewMotion.Presets.ToList().IndexOf("right");
            ShotKit.Pump(200);
            int[] hop = [0, -3, -5, -5, -3, 0];
            var rows = ShotKit.Get<System.Collections.IList>(d, "_rows");
            for (int i = 0; i < rows.Count && i < hop.Length; i++) (((NumericUpDown X, NumericUpDown Y))rows[i]!).Y.Value = hop[i];
            ShotKit.Pump(300);
            var table = ShotKit.All<ScrollViewer>(d).First(s => s.Content is Grid);
            k.Save(d, "screen-effect-maker-3", marks: [(Control)choice.Parent!, table], firstNumber: 16);
        }, settleMs: 500);

        // 8: 규칙 창(원문 포함·복사)
        ShotKit.Call(maker, "ShowRules");
        k.Settle(800);
        var rules = k.Windows.Last(w => w.GetType().Name == "ScriptGuideWindow");
        k.Save(rules, "screen-effect-maker-8", marks:
            [Union(rules, ShotKit.Get<Control>(rules, "_includeSource"), ShotKit.FindText(rules, UiStrings.EffectRules_Copy, ancestorControl: true))], firstNumber: 25);
        rules.Close();
        k.Settle(200);

        // 2: 올린 효과 — 계정 핸들을 정하고 둘을 올린 것으로(하나는 올린 뒤 고침), 이 컴퓨터에 없는 올린 효과 하나를 온라인 목록인 것처럼 더한다
        App.Settings.AssetPublish.LastHandle = Handle;
        var author = new AssetAuthor(Handle, "Pixel Cat");
        AppAssets.Store.MarkPublished(glow.Id, author, AssetPublishCheck.License, new AssetStoreLink
        {
            PackageId = Handle + ".glow-pulse", AssetId = "glow-pulse", Version = "1.0.0", ContentHash = AppAssets.Catalog.Find(glow.Id)!.ContentHash, Name = "Glow pulse",
        });
        // 올린 뒤 원문을 고친 것: 상점 연결의 내용 해시가 지금 내용과 다르다
        AppAssets.Store.MarkPublished(wobble.Id, author, AssetPublishCheck.License, new AssetStoreLink
        {
            PackageId = Handle + ".jelly-wobble", AssetId = "jelly-wobble", Version = "1.0.0", ContentHash = "0000000000000000", Name = "Jelly wobble",
        });
        k.Settle(300);
        ShotKit.Set(maker, "_uploadsHandle", Handle);
        ShotKit.Set(maker, "_uploads", new List<RemoteCatalogEntry>
        {
            new()
            {
                Id = Handle + ".pixel-rain", Version = "1.0.0", Name = "Pixel rain", Author = author, License = AssetPublishCheck.License,
                Kinds = new Dictionary<string, int> { [AssetKinds.EffectScript] = 1 },
            },
        });
        ShotKit.Call(maker, "RebuildList");
        k.Settle(800);
        var items = list.Items.OfType<ListBoxItem>().ToList();
        // 올린 내 효과: 이름 옆 작성자 묶음(프로필 사진·이름, 작성자가 없으면 "올린 효과" 글)
        var published = items.Where(i => i.Tag is string && i.Content is StackPanel { Children: [StackPanel, _] })
            .Select(i => ((StackPanel)i.Content!).Children[1]).ToArray();
        var remote = items.Single(i => i.Tag is RemoteCatalogEntry);
        var left = Union(maker, header, newRow, remote);
        // 이 컴퓨터에 없는 올린 효과 줄: 테두리를 이름·바로 위 줄 글자보다 오른쪽부터 둔다(번호 동그라미가 위 줄의 아이콘·글자를 가리지 않게)
        var row = InWindow(maker, remote);
        var name = InWindow(maker, ShotKit.All<TextBlock>(remote).First(t => t.Text == "Pixel rain"));
        double above = ShotKit.All<TextBlock>(items[items.IndexOf(remote) - 1]).Max(t => InWindow(maker, t).Right);
        double from = Math.Max(name.Right, above) + 18;
        var download = new Rect(from, row.Y, row.Right - from, row.Height);
        k.Save(maker, "screen-effect-maker-2", crop: new Rect(left.X, left.Y, left.Width, left.Height + 40).Inflate(8),
            marks: [Union(maker, published), download], firstNumber: 14);

        // 4: 변신 — 첫 병아리가 끝 그림 병아리(마법사)로 바뀐다. 가운데 칸에서 멈춰 둔다
        OpenSource(k, maker, MorphScript);
        ShotKit.Call(maker, "ShowStep", 3);
        k.Settle(300);
        var endLabel = chickRows.Children[2];
        var endChick = ShotKit.Get<Control>(maker, "_endChick");
        k.Save(maker, "screen-effect-maker-4", crop: Union(maker, previewTop, play).Inflate(8), marks: [Union(maker, endLabel, endChick), strip], firstNumber: 18);

        // 10: 미리보기 자리·피벗 창 — 끝 그림 병아리를 오른쪽으로 16px 옮긴 값(끝 그림 병아리가 있어 끝 값 두 줄이 켜짐). 창을 닫으면(취소) 원문은 그대로
        k.Dialog(() => ShotKit.Call(maker, "EditPreviewPlacement"), d =>
        {
            ShotKit.Get<(NumericUpDown X, NumericUpDown Y)>(d, "_endOffset").X.Value = 16;
            ShotKit.Pump(200);
            k.Save(d, "screen-effect-maker-10");
        }, settleMs: 500);

        // 5: 레이어 설정과 미리보기 장면(하늘·바닥 레이어, 바닥 위에 병아리)
        OpenSource(k, maker, ShadowScript);
        ShotKit.Set(maker, "_scene", DemoScene());
        ShotKit.Call(maker, "UpdateSceneRow");
        ShotKit.Call(maker, "Rerender");
        WaitPreview(k, maker);
        MarkSaved(maker);
        k.Settle(200);
        var sceneRow = ShotKit.Get<Control>(maker, "_sceneRow");
        var layerRow = settings.Children.First(c => ShotKit.All<ComboBox>(c).Any());
        var settingsBox = GroupOf(settings, code);
        k.Save(maker, "screen-effect-maker-5", crop: Union(maker, settingsBox, previewTop, sceneRow).Inflate(8),
            marks: [PadLeft(InWindow(maker, layerRow), 10), sceneRow], firstNumber: 20);

        // 6: 장면을 메인 편집기에서 고치는 동안 — 메인 창(장면 탭)과 그 오른쪽 위의 작은 미리보기 창
        ShotKit.Call(maker, "EditScene");
        k.Settle(1200);
        var at = new Point(maker.Position.X - main.Position.X, maker.Position.Y - main.Position.Y);
        AiChat.Composite(k, "screen-effect-maker-6", main, [(maker, at)],
            marks: [new Rect(at.X, at.Y - ShotKit.TitleHeight, maker.Bounds.Width, maker.Bounds.Height + ShotKit.TitleHeight)], firstNumber: 22);
        ShotKit.Call(maker, "DetachScene", true);
        ShotKit.Call(maker, "Expand");
        k.Settle(500);

        // 7: 상태 창 — Jint로 도는(글자열) 효과를 검사한 결과(실행 경로·그라데이션 안내 = 노란 경고)
        OpenSource(k, maker, BlinkScript);
        var check = (Task)ShotKit.Call(maker, "CheckAsync")!;
        var sw = Stopwatch.StartNew();
        while (!check.IsCompleted && sw.ElapsedMilliseconds < 60000) k.Settle(50);
        k.Settle(300);
        k.Click(status, settleMs: 600);
        var flyout = PopupArea(maker) ?? throw new InvalidOperationException("no status flyout");
        var bottomRow = Union(maker, ShotKit.Get<Control>(maker, "_save"), status);
        k.Save(maker, "screen-effect-maker-7", crop: bottomRow.Union(flyout).Inflate(10).Intersect(new Rect(maker.ClientSize)), marks: [status, flyout], firstNumber: 23);
        PopupMenus.CloseAll();
        k.Settle(200);

        // 9: AI 칸 — 대본 세션으로 짧은 대화(원문 읽기·바꾸기 도구는 진짜로 실행)
        ShotKit.Call(maker, "TryOpen", glow.Id);
        WaitPreview(k, maker);
        AiPanel(k, main, maker);

        MarkSaved(maker);
    }

    /// <summary>원문을 새 스크립트로 열고(앞 효과는 저장한 것으로 봐 묻지 않음) 미리보기를 그린 뒤 저장한 것으로 둔다(창 제목에 * 없음).</summary>
    private static void OpenSource(ShotKit k, Window maker, string source)
    {
        MarkSaved(maker);
        ShotKit.Call(maker, "NewScript", source);
        WaitPreview(k, maker);
        MarkSaved(maker);
        k.Settle(100);
    }

    /// <summary>지금 원문·장면을 저장한 것으로 둔다(닫거나 다른 효과를 열 때 "저장할까요?"를 묻지 않게).</summary>
    private static void MarkSaved(Window maker)
    {
        ShotKit.Set(maker, "_savedSource", ShotKit.Get<string>(maker, "_source"));
        ShotKit.Set(maker, "_savedScene", ShotKit.Get<PreviewScene?>(maker, "_scene"));
        ShotKit.Set(maker, "_sceneStale", false);
        ShotKit.Call(maker, "UpdateButtons");
    }

    /// <summary>미리보기 그리기(입력 기다림 300ms + 작업 스레드)가 끝날 때까지 기다린다.</summary>
    private static void WaitPreview(ShotKit k, Window maker)
    {
        k.Settle(400);
        var debounce = ShotKit.Get<UiDebouncer>(maker, "_debounce");
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000 && (debounce.IsPending || ShotKit.Get<bool>(maker, "_statusBusy"))) k.Settle(50);
        k.Settle(400);
    }

    /// <summary>미리보기 장면: 아래 하늘, 그 위 바닥(풀·흙 띠 — 병아리 발 높이부터), 맨 위 병아리.</summary>
    private static PreviewScene DemoScene()
    {
        var chick = PreviewChicks.Load(PreviewChicks.Default);
        var basic = PreviewScene.CreateDefault(chick);
        int w = basic.Width, h = basic.Height, feet = basic.ChickAnchor.Y;
        var sky = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            sky.FillRect(new System.Drawing.Rectangle(0, y, w, 1), new Color32((byte)(150 + y * 60 / h), (byte)(200 + y * 30 / h), 240, 255));
        var ground = new PixelImage(w, h);
        ground.FillRect(new System.Drawing.Rectangle(0, feet, w, h - feet), new Color32(120, 84, 56, 255));
        ground.FillRect(new System.Drawing.Rectangle(0, feet, w, 3), new Color32(96, 176, 72, 255));
        for (int x = 1; x < w; x += 5) ground.Set(x, feet - 1, new Color32(96, 176, 72, 255));
        return new PreviewScene(w, h, 1,
            [new PreviewSceneLayer("sky", [sky]), new PreviewSceneLayer(PreviewScene.DefaultGroundName, [ground])], 2, basic.ChickAnchor);
    }

    /// <summary>9: AI 칸을 대본 세션(CLI 없음)으로 열고 짧은 대화 한 차례를 돌려 찍는다.</summary>
    private static void AiPanel(ShotKit k, MainForm main, EffectMakerWindow maker)
    {
        var mcp = (Chickxel.App.Ai.McpServer)ShotKit.Call(main, "EnsureAiServer", maker)!;
        ShotKit.Set(main, "_closeConfirmed", true);
        var panelType = typeof(EffectMakerWindow).Assembly.GetType("Chickxel.App.Ai.EffectMakerAiPanel")!;
        Func<PixelImage?> Source(string method) => typeof(EffectMakerWindow).GetMethod(method, Any)!.CreateDelegate<Func<PixelImage?>>(maker);
        var aiToggle = ShotKit.Get<ToggleButton>(maker, "_aiToggle");
        var panel = (Control)Activator.CreateInstance(panelType, Any, null,
            [mcp, App.Settings, (Action)(() => { }), (Action)(() => aiToggle.IsChecked = false), Source("CanvasImage"), Source("PreviewFrame")], null)!;
        var session = new ScriptSession(mcp.Tools)
        {
            // 도구 줄 끝의 걸린 시간을 그럴듯하게(칸이 도구 시작 시각을 적어 둠)
            ToolTiming = (id, elapsed) =>
            {
                var tools = ShotKit.Get<System.Collections.IDictionary>(panel, "_tools");
                if (!tools.Contains(id)) return;
                object entry = tools[id]!;
                entry.GetType().GetField("Item2")!.SetValue(entry, Stopwatch.GetTimestamp() - (long)(elapsed.TotalSeconds * Stopwatch.Frequency));
                tools[id] = entry;
            },
        };
        ShotKit.Call(panel, "ShowChatWithoutCli", session);
        ShotKit.Set(maker, "_aiPanel", panel);
        ShotKit.Get<Border>(maker, "_aiHost").Child = panel;
        aiToggle.IsChecked = true;
        k.Settle(600);

        // 미리보기 칸을 첨부하고 부탁을 보낸다 → 원문 읽기·바꾸기(진짜 도구) → 답
        string sparkled = GlowScript
            .Replace("""{ key: "strength", label: "Strength", type: "int", min: 0, max: 100, default: 70 },""",
                """{ key: "strength", label: "Strength", type: "int", min: 0, max: 100, default: 70 },""" + "\n    " +
                """{ key: "seed", label: "Seed", type: "int", min: 0, max: 9999, default: 42 },""")
            .Replace("const k = s.strength / 100", "const k0 = s.strength / 100")
            .Replace("const c = ctx.color(\"color\", x, y);", "const c = ctx.color(\"color\", x, y);\n      const k = ctx.hash(x, y, s.seed, ctx.step) > 0.9 ? 1 : k0;");
        session.Next.Enqueue(new Script()
            .Tool("get_effect_maker_source", seconds: 0.4)
            .Tool("set_effect_maker_source", new JsonObject { ["source"] = sparkled }, seconds: 0.6)
            .Text("Done. Random pixels now flash to the full glow color while the rest pulses as before. I added a **Seed** setting — press its dice button for another sparkle pattern.")
            .Done(0.041, 23800));
        // 글을 먼저 넣고 끝에 미리보기 칸을 그림 칩으로 붙인다(칩은 커서 자리에 들어감)
        var input = ShotKit.Get<Control>(panel, "_imageInput");
        var box = (TextBox)input.GetType().GetProperty("Input")!.GetValue(input)!;
        box.Text = "Make random pixels sparkle in the glow color, brighter than this frame: ";
        box.CaretIndex = box.Text.Length;
        ShotKit.Call(panel, "AttachPreview");
        k.Settle(200);
        ShotKit.Call(panel, "Send");
        var sw = Stopwatch.StartNew();
        while (session.Busy && sw.ElapsedMilliseconds < 10000) k.Settle(50);
        WaitPreview(k, maker);
        // 차례 끝 줄(비용 · 걸린 시간): 대본은 곧바로 끝나므로 실제 차례처럼 보이게 시간을 맞춘다
        var messages = ShotKit.Get<StackPanel>(panel, "_messages");
        if (messages.Children.OfType<TextBlock>().LastOrDefault() is { } end) end.Text = "$0.041 · 23.8s";
        k.Settle(300);

        var model = ShotKit.Get<Control>(panel, "_model");
        var send = ShotKit.Get<Control>(panel, "_send");
        // 입력란 아래 줄: 보내기·멈춤(오른쪽), 첨부 단추 셋(왼쪽), 모델 칸 차례로 든 DockPanel
        var inputArea = (StackPanel)ShotKit.Get<Control>(panel, "_imageInput").Parent!;
        var attachRow = ((Panel)inputArea.Children[2]).Children[1];
        var scroll = ShotKit.Get<Control>(panel, "_scroll");
        var right = ShotKit.Get<Control>(maker, "_aiHost");
        var previewTop = (Control)aiToggle.Parent!;
        k.Save(maker, "screen-effect-maker-9", crop: Union(maker, previewTop, right).Inflate(8).Intersect(new Rect(maker.ClientSize)), marks:
        [
            Union(maker, scroll, input),
            attachRow,
            Union(maker, model, send),
        ], firstNumber: 26);
    }
}
