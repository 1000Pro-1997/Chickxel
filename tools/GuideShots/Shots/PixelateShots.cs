using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Gpixel.App;
using Gpixel.App.Localization;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace GuideShots;

/// <summary>AI 그림 픽셀로 정리 창 샷의 준비: 짧은 최적화 예산, "AI가 만든 듯한" 원본(예시 그림을 고르지 않게 키우고 번지게 한 JPG), 후보·최적화 기다리기.</summary>
internal static class PixelateKit
{
    /// <summary>예시 문서 파일 이름(화면 언어대로 — 탭·파일 브라우저에 보임).</summary>
    public static string SampleFile => ShotKit.Language == "ko" ? "늑대귀 모험가.png" : "Wolf-ear adventurer.png";

    public static string SourceName => ShotKit.Language == "ko" ? "늑대귀 모험가 AI" : "Wolf-ear adventurer AI";

    /// <summary>창 옵션을 새로: 불러오면 자동 최적화 끔(필요하면 켬), 설정 칸 펼침 여부, 최적화 프리셋은 앞의 두 개만 짧은 예산으로.</summary>
    public static PixelateOptions Options(bool expanded = false)
    {
        var o = new PixelateOptions();
        o.Migrate();
        o.AutoOptimizeOnLoad = false;
        o.SettingsExpanded = expanded;
        o.Width = 1400;
        o.Height = 860;
        for (int i = 0; i < o.OptimizePresets.Count; i++)
        {
            var p = o.OptimizePresets[i];
            p.Enabled = i < 2;
            p.Trials = 120;
            p.Seconds = 20;
        }
        App.Settings.Pixelate = o;
        return o;
    }

    /// <summary>
    /// 예시 그림(49×98)을 칸마다 7~8px로 들쭉날쭉하게 키우고, 밝은 단색 배경 위에 살짝 번지게 한 뒤 JPG로 저장한다(AI가 만든 "픽셀 느낌" 그림 흉내).
    /// </summary>
    public static string MakeAiImage(ShotKit k)
    {
        var src = PixelateDialog.LoadImage(ShotKit.SamplePng) ?? throw new InvalidOperationException("sample unreadable");
        const double scale = 7.4;
        const int margin = 44;
        int[] MapAxis(int cells, out int size)
        {
            size = (int)Math.Round(cells * scale) + margin * 2;
            var map = new int[size];
            int cell = 0;
            for (int p = 0; p < size; p++)
            {
                double q = (p - margin) / scale;
                double edge = Math.Sin(cell * 1.7) * 0.18;
                int c = (int)Math.Floor(q + edge);
                map[p] = q < 0 || c >= cells ? -1 : Math.Clamp(c, 0, cells - 1);
                if (map[p] >= 0) cell = map[p];
            }
            return map;
        }
        var mx = MapAxis(src.Width, out int w);
        var my = MapAxis(src.Height, out int h);
        var bg = new Color32(238, 235, 228);
        var sharp = new double[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = mx[x] < 0 || my[y] < 0 ? Color32.Transparent : src[mx[x], my[y]];
                double a = c.A / 255.0;
                int i = (y * w + x) * 3;
                sharp[i] = c.R * a + bg.R * (1 - a);
                sharp[i + 1] = c.G * a + bg.G * (1 - a);
                sharp[i + 2] = c.B * a + bg.B * (1 - a);
            }
        var rnd = new Random(7);
        var img = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double r = 0, g = 0, b = 0, sum = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = Math.Clamp(x + dx, 0, w - 1), yy = Math.Clamp(y + dy, 0, h - 1);
                        double wt = (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1);
                        int i = (yy * w + xx) * 3;
                        r += sharp[i] * wt; g += sharp[i + 1] * wt; b += sharp[i + 2] * wt; sum += wt;
                    }
                double n = rnd.NextDouble() * 6 - 3;
                img[x, y] = new Color32(Byte(r / sum + n), Byte(g / sum + n), Byte(b / sum + n));
            }
        var path = Path.Combine(k.WorkDir, SourceName + ".jpg");
        ImageWriter.Save(img, path, ExportImageFormat.Jpg, 80);
        return path;
    }

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>메인 창(예시 문서 열림)을 띄우고 AI 그림을 원본으로 정리 창을 연 뒤 첫 후보가 끝날 때까지 기다린다.</summary>
    public static PixelateDialog Open(ShotKit k, bool expanded = false)
    {
        Options(expanded);
        var main = k.Main(k.CopySample(SampleFile));
        var path = MakeAiImage(k);
        ShotKit.Call(main, "PixelateFile", path);
        k.Settle(400);
        var dlg = k.Windows.OfType<PixelateDialog>().Last();
        WaitCandidates(k, dlg);
        return dlg;
    }

    public static void WaitCandidates(ShotKit k, PixelateDialog dlg, int timeoutMs = 120000)
    {
        var running = UiStrings.Pixelate_Status_Running;
        running = running[..running.IndexOf('{')];
        var status = ShotKit.Get<TextBlock>(dlg, "_status");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            k.Settle(200);
            if (ShotKit.Get<object?>(dlg, "_selected") is not null && !(status.Text ?? "").StartsWith(running, StringComparison.Ordinal)) break;
        }
        k.Settle(600);
    }

    public static void WaitOptimize(ShotKit k, PixelateDialog dlg, int timeoutMs = 240000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        k.Settle(300);
        while (sw.ElapsedMilliseconds < timeoutMs && ShotKit.Get<object?>(dlg, "_optCts") is not null) k.Settle(250);
        k.Settle(800);
    }

    /// <summary>설정 칸의 단계 접기 상자(머리글 글자로).</summary>
    public static Expander Section(PixelateDialog dlg, string title)
        => ShotKit.All<Expander>(dlg).First(e => e.Header is TextBlock t && t.Text == title);

    /// <summary>설정 칸에서 <paramref name="open"/> 단계만 펼치고 나머지는 접는다.</summary>
    public static void OnlyExpand(ShotKit k, PixelateDialog dlg, params string[] open)
    {
        foreach (var e in ShotKit.All<Expander>(dlg).Where(e => e.Header is TextBlock))
            e.IsExpanded = open.Contains(((TextBlock)e.Header!).Text);
        k.Settle(600);
    }

    /// <summary>팝업 안 컨트롤의 <paramref name="top"/> 창 좌표 사각형(팝업 위치는 플랫폼 구현의 Position — 공개 API가 아님).</summary>
    public static Rect PopupBounds(Control c, Window top)
    {
        var host = TopLevel.GetTopLevel(c) ?? throw new InvalidOperationException("detached");
        var local = ShotKit.Bounds(c, host);
        if (ReferenceEquals(host, top)) return local;
        var pos = host.PlatformImpl?.GetType().GetProperty("Position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(host.PlatformImpl) is PixelPoint p ? p : default;
        var origin = top.Position;
        return local.Translate(new Vector(pos.X - origin.X, pos.Y - origin.Y));
    }
}

internal static class PixelateShots
{
    // 여는 방법: 원본 없이 연 빈 창(가운데 안내·파일 찾기) + AI 메뉴의 명령.
    [Shot("pixelate-open")]
    public static void Open(ShotKit k)
    {
        PixelateKit.Options();
        var main = k.Main(k.CopySample(PixelateKit.SampleFile));
        var menu = ShotKit.Find<Menu>(main);
        var ai = menu.Items.OfType<MenuItem>().ElementAt(5);
        k.Click(ai, settleMs: 500);
        var item = ShotKit.All<MenuItem>(main).FirstOrDefault(m => m.Header is not null && m.IsVisible && ShotKit.All<TextBlock>(m).Any(t => t.Text == MainStrings.Menu_Ai_Pixelate))
                   ?? throw new InvalidOperationException("no pixelate menu item");
        var r = PixelateKit.PopupBounds(item, main);
        var aiBounds = ShotKit.Bounds(ai, main);
        var crop = new Rect(Math.Max(0, aiBounds.X - 120), 0, 560, Math.Min(900, r.Bottom + 140));
        k.Save(main, "pixelate-open-2", crop: crop, markRects: [r]);
        k.Key(main, Avalonia.Input.Key.Escape);
        k.Settle(300);

        k.Command("AI/AI 그림 픽셀로 정리…");
        k.Settle(800);
        var dlg = k.Windows.OfType<PixelateDialog>().Last();
        k.Settle(500);
        var browse = ShotKit.FindText(dlg, UiStrings.Pixelate_Browse, ancestorControl: true);
        k.Save(dlg, marks: [browse]);
    }

    // 여는 방법: 파일 브라우저에서 그림 파일 우클릭 > AI 그림 픽셀로 정리….
    [Shot("pixelate-open-3")]
    public static void OpenFromBrowser(ShotKit k)
    {
        PixelateKit.Options();
        var sample = k.CopySample(PixelateKit.SampleFile);
        var jpg = PixelateKit.MakeAiImage(k);
        var main = k.Main(sample, folder: k.WorkDir);
        k.Settle(800);
        var browser = ShotKit.Get<FileBrowserPanel>(main, "_browser");
        var list = ShotKit.Get<ListBox>(browser, "_list");
        var container = ShotKit.All<ListBoxItem>(list).First(c => ShotKit.Get<string>(c.Tag!, "Path") == jpg);
        ShotKit.Call(browser, "SetSelection", (Func<object, bool>)(o => ReferenceEquals(o, container)));
        k.Settle(300);
        k.Click(container, Avalonia.Input.MouseButton.Right, settleMs: 600);
        var item = ShotKit.All<MenuItem>(main).FirstOrDefault(m => m.IsVisible && m.Header as string == UiStrings.Pixelate_MenuItem)
                   ?? throw new InvalidOperationException("no browser pixelate item");
        var r = PixelateKit.PopupBounds(item, main);
        var b = ShotKit.Bounds(container, main);
        var menuRect = PixelateKit.PopupBounds((Control)item.Parent!, main);
        var top = Math.Max(0, b.Y - 70);
        var area = new Rect(0, top, menuRect.Right + 16, menuRect.Bottom + 16 - top);
        k.Save(main, crop: area, marks: [container], markRects: [r]);
    }

    // 설정 단계: 펼친 설정 칸(1단계만 펼치고 나머지 단계 머리글이 보이게) + 접기 막대 강조.
    [Shot("pixelate-settings")]
    public static void Settings(ShotKit k)
    {
        var dlg = PixelateKit.Open(k, expanded: true);
        PixelateKit.OnlyExpand(k, dlg, UiStrings.Pixelate_Section_Background, UiStrings.Pixelate_Section_Grid);
        var toggle = ShotKit.Get<Button>(dlg, "_settingsToggle");
        var b = ShotKit.Bounds(toggle, dlg);
        k.Save(dlg, crop: new Rect(0, 0, b.Right + 330, dlg.Bounds.Height - 50), marks: [toggle]);
    }

    // 후보와 점수: 고른 후보 줄·점수 내역·후보 칸.
    [Shot("pixelate-candidates")]
    public static void Candidates(ShotKit k)
    {
        var dlg = PixelateKit.Open(k);
        var info = ShotKit.Get<TextBlock>(dlg, "_selectedInfo");
        var breakdown = ShotKit.Get<TextBlock>(dlg, "_breakdown");
        var tiles = ShotKit.Get<WrapPanel>(dlg, "_tiles");
        var scroll = tiles.FindAncestorOfType<ScrollViewer>()!;
        var tile = tiles.Children.OfType<Control>().First();
        k.SaveArea([info, breakdown, scroll], pad: 6, marks: [breakdown, tile]);
    }

    // 자동 최적화: 끝난 뒤의 막대(그래프·한 줄 결과·되돌리기)와 최적화 후보 + 최적화 설정 창(프리셋 목록).
    [Shot("pixelate-optimize")]
    public static void Optimize(ShotKit k)
    {
        var dlg = PixelateKit.Open(k);
        ShotKit.Call(dlg, "StartOptimize");
        PixelateKit.WaitOptimize(k, dlg);
        var run = ShotKit.Get<Button>(dlg, "_optRun");
        var bar = (Control)run.Parent!;
        var tiles = ShotKit.Get<WrapPanel>(dlg, "_tiles");
        var scroll = tiles.FindAncestorOfType<ScrollViewer>()!;
        var settingsButton = ShotKit.All<Button>(bar).First(b => !ReferenceEquals(b, run) && b.IsVisible);
        ShotKit.Get<TextBlock>(dlg, "_breakdown").Opacity = 0; // 바로 위 점수 내역 줄 끝이 잘려 보이지 않게.
        k.Settle(200);
        var area = ShotKit.Bounds(bar, dlg).Union(ShotKit.Bounds(scroll, dlg));
        k.Save(dlg, crop: new Rect(area.X - 6, area.Y - 4, area.Width + 12, area.Height + 10), marks: [run, settingsButton]);

        ShotKit.Call(dlg, "OpenOptimizeSettings");
        k.Settle(800);
        var win = ShotKit.Get<Window>(dlg, "_optSettingsWindow");
        k.Settle(400);
        k.Save(win, "pixelate-optimize-2");
    }

    // 원본과 미리보기: 전체 창(원본·격자·큰 미리보기·색 견본·프레임) + 크게 보기 + 색 견본 줄.
    [Shot("pixelate-view")]
    public static void View(ShotKit k)
    {
        var dlg = PixelateKit.Open(k);
        ShotKit.Call(dlg, "StopPlayback");
        k.Settle(300);
        k.Save(dlg);

        var colors = ShotKit.Get<Control>(dlg, "_colors");
        var showGrid = ShotKit.Get<CheckBox>(dlg, "_showGrid");
        var showMask = ShotKit.Get<CheckBox>(dlg, "_showMask");
        var barAuto = ShotKit.Get<CheckBox>(dlg, "_barAuto");
        var aspect = ShotKit.Get<ToggleButton>(dlg, "_barAspect");
        var toolbar = (Control)showGrid.Parent!;
        k.SaveControl(toolbar, "pixelate-view-3", pad: 8, marks: [showGrid, showMask, barAuto, aspect]);

        ShotKit.Call(dlg, "OpenZoom");
        k.Settle(800);
        k.Save(dlg, "pixelate-view-2");
        ShotKit.Call(dlg, "CloseZoom");
        k.Settle(300);

        // 색 하나를 빼 둔 모습(우클릭과 같음): 뺀 견본은 끝에 흐리게, "색 빼기 1개 적용 중 · 모두 되살리기".
        var selected = ShotKit.Get<object>(dlg, "_selected");
        var palette = ShotKit.Get<IEnumerable<Color32>>(ShotKit.Get<object>(selected, "Result"), "Palette").ToList();
        ShotKit.Call(dlg, "ToggleColor", palette[Math.Min(6, palette.Count - 1)]);
        k.Settle(500);
        var strip = ShotKit.Get<Control>(dlg, "_strip").FindAncestorOfType<ScrollViewer>()!;
        var colorsPlus = ShotKit.Get<Control>(colors, "_add");
        k.SaveArea([colors, strip], "pixelate-view-4", pad: 10, marks: [colorsPlus]);
    }

    // 넣기: 넣기 단계 + 창 아래 단추(새 탭에 추가·적용).
    [Shot("pixelate-output")]
    public static void Output(ShotKit k)
    {
        var dlg = PixelateKit.Open(k, expanded: true);
        PixelateKit.OnlyExpand(k, dlg, UiStrings.Pixelate_Section_Output);
        var section = PixelateKit.Section(dlg, UiStrings.Pixelate_Section_Output);
        var newTab = ShotKit.Get<Button>(dlg, "_newTab");
        var ok = ShotKit.Get<Button>(dlg, "OkButton");
        k.SaveControl(section, pad: 8);
        var row = (Control)newTab.Parent!;
        var rb = ShotKit.Bounds(row, dlg);
        k.Save(dlg, "pixelate-output-2", crop: new Rect(dlg.Bounds.Width - 520, rb.Y - 40, 520, rb.Height + 52), marks: [newTab, ok]);
    }

    // AI로 다듬기: AI 단계 펼침.
    [Shot("pixelate-ai")]
    public static void Ai(ShotKit k)
    {
        var dlg = PixelateKit.Open(k, expanded: true);
        PixelateKit.OnlyExpand(k, dlg, UiStrings.Pixelate_Section_Ai);
        var section = PixelateKit.Section(dlg, UiStrings.Pixelate_Section_Ai);
        var touch = ShotKit.Get<CheckBox>(dlg, "_aiTouch");
        touch.IsChecked = true;
        k.Settle(300);
        section.BringIntoView();
        k.Settle(500);
        var rank = ShotKit.FindText(section, UiStrings.Pixelate_Ai_Rank, ancestorControl: true);
        k.SaveControl(section, pad: 8, marks: [touch, rank]);
    }

    // 프리셋: 프리셋 목록을 펼친 모습.
    [Shot("pixelate-presets")]
    public static void Presets(ShotKit k)
    {
        var dlg = PixelateKit.Open(k, expanded: true);
        PixelateKit.OnlyExpand(k, dlg);
        var combo = ShotKit.Get<ComboBox>(dlg, "_preset");
        combo.IsDropDownOpen = true;
        k.Settle(500);
        var row = (Control)combo.Parent!;
        var b = ShotKit.Bounds(row, dlg);
        k.Save(dlg, crop: new Rect(0, Math.Max(0, b.Y - 8), b.Right + 40, 260), marks: [row]);
    }
}
