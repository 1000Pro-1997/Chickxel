using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace GuideShots;

/// <summary>
/// 튜토리얼 말풍선의 작은 움짤(그 단계에서 할 일만 가까이 잘라 찍은 것): 앱 내장 <c>src/Chickxel.App/Assets/Tutorial/clip-&lt;이름&gt;.webp</c>.
/// 단계 ↔ 움짤은 <c>MainForm.Tutorial.cs</c>의 <c>TutorialStep.Clip</c>과 저장소 루트 <c>튜토리얼.md</c>. 샷 id = <c>clip-tutorial-&lt;이름&gt;</c>.
/// </summary>
internal static class TutorialClipShots
{
    private const string Out = "src/Chickxel.App/Assets/Tutorial/clip-";

    private static void Save(ClipRecorder rec, string name, int? width = null)
        => rec.Save(Out + name, ClipFormat.WebP, webpLossless: false, webpQuality: 82, webpWidth: width);

    // ───────────── 효과 쓰기 ─────────────

    /// <summary>예시 늑대 6프레임 문서 + 맨 왼쪽 칸에 효과 목록(파일 칸 닫음).</summary>
    private static MainForm EffectsMain(ShotKit k)
    {
        // 타임라인 칸이 효과 목록과 가깝게: 팔레트·오른쪽 칸을 좁힌다.
        App.Settings.BottomHeight = 230;
        App.Settings.PaletteWidth = 170;
        App.Settings.RightWidth = 250;
        var m = ScreenDemo.Open(k, width: 1280, height: 640);
        ScreenDemo.Build(k, m, group: false);
        ShotKit.Call(m, "SetLeftPanes", false, true, false);
        k.Settle(500);
        return m;
    }

    private static Control Grid(MainForm m) => ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_timeline"), "_grid");

    private static int Body(MainForm m) => ScreenDemo.Project(m).IndexOf(ScreenDemo.Project(m).Layers.First(l => l.Name == "Body"));

    /// <summary>레이어 × 프레임 칸의 창 좌표.</summary>
    private static Rect Cel(MainForm m, int layer, int frame)
    {
        var g = Grid(m);
        var r = (Rect)ShotKit.Call(g, "CelRect", layer, frame)!;
        return new Rect(g.TranslatePoint(r.Position, m)!.Value, r.Size);
    }

    private static Control OutlineTile(MainForm m)
        => ShotKit.All<Control>(ShotKit.Get<Control>(m, "_effectList")).First(c => c.GetType().Name == "EffectTile" && c.IsEffectivelyVisible
            && ((string)c.GetType().GetProperty("Key")!.GetValue(c)!).Contains("outline", StringComparison.OrdinalIgnoreCase));

    /// <summary>창 안으로 자르고 짝수 크기로(영상 인코더).</summary>
    private static Rect Fit(Rect r, TopLevel top)
    {
        r = r.Intersect(new Rect(top.ClientSize));
        return new Rect(Math.Round(r.X), Math.Round(r.Y), Math.Floor(r.Width / 2) * 2, Math.Floor(r.Height / 2) * 2);
    }

    // ───────────── 내보내기 ─────────────

    /// <summary>내보내기 창을 열어 더하기로 GIF 줄을 만들고 <paramref name="inside"/>를 부른다(창은 그 뒤 닫힘). 줄 앞 체크는 꺼 둔다.</summary>
    private static void ExportWithGif(ShotKit k, MainForm m, Action<Window, CheckBox> inside)
        => k.Dialog(() => ShotKit.Call(m, "OpenExportDialog"), w =>
        {
            k.Click(ShotKit.Get<Control>(w, "_addButton"));
            k.Settle(300);
            ShotKit.Get<ComboBox>(w, "_formatCombo").SelectedIndex = 0;
            k.Settle(300);
            var pane = ShotKit.Get<Control>(w, "TutorialListPane");
            List<CheckBox> Checks() => ShotKit.All<CheckBox>(pane).Where(c => c.IsEffectivelyVisible).ToList();
            // 체크를 바꾸면 목록을 다시 그리므로 하나씩 새로 찾아 끈다.
            for (int i = 0; i < 4 && Checks().FirstOrDefault(c => c.IsChecked == true) is { } on; i++)
            {
                on.IsChecked = false;
                k.Settle(200);
            }
            inside(w, Checks()[^1]);
        });

    /// <summary>같은 파일을 목록 줄과 파일 칸에서 짚는 번호 색: 1 = 시트 PNG(파랑), 2 = GIF(주황).</summary>
    private static readonly Avalonia.Media.Color OnSaveSheet = Avalonia.Media.Color.FromRgb(59, 130, 246), OnSaveGif = Avalonia.Media.Color.FromRgb(245, 140, 20);

    /// <summary>
    /// 저장할 때 내보내기 ①: 내보내기 목록의 1(시트 PNG) 줄 앞 체크 상자를 켜고 그대로 두었다가(오른쪽 움짤에서 저장하는 동안) 다시 끈다 — 되풀이.
    /// 두 줄에 오른쪽 움짤 파일 칸과 같은 번호·색(1 파랑 · 2 주황)을 붙인다.
    /// </summary>
    [Shot("clip-tutorial-export-onsave-check")]
    public static void ExportOnSaveCheck(ShotKit k)
    {
        var m = ScreenDemo.Open(k, width: 1280, height: 720);
        ScreenDemo.Build(k, m, group: false);
        ExportWithGif(k, m, (w, _) =>
        {
            var paneControl = ShotKit.Get<Control>(w, "TutorialListPane");
            CheckBox Row(int i) => ShotKit.All<CheckBox>(paneControl).Where(c => c.IsEffectivelyVisible).ElementAt(i);
            var pane = Probe(k, m, paneControl);
            var sheet = Probe(k, m, Row(0));
            var gif = Probe(k, m, Row(1));
            var crop = Fit(new Rect(pane.X, pane.Y, pane.Width, Math.Min(pane.Height, gif.Bottom - pane.Y + 60)), m);
            using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
            rec.Badge(sheet.TopLeft + new Vector(-1, -1), "1", OnSaveSheet);
            rec.Badge(gif.TopLeft + new Vector(-1, -1), "2", OnSaveGif);
            rec.Place(new Point(sheet.Center.X + 160, gif.Center.Y + 50));
            rec.Hold(600);
            rec.Click(sheet.Center, 700, pressMs: 120, afterMs: 300);
            if (Row(0).IsChecked != true) throw new InvalidOperationException("on-save check not turned on");
            // 켠 채 둔다(저장하면 이 줄이 다시 만들어짐 — 오른쪽 움짤).
            rec.Move(new Point(sheet.Center.X + 150, gif.Center.Y + 45), 600);
            rec.Hold(3200);
            rec.Click(sheet.Center, 700, pressMs: 120, afterMs: 900);
            if (Row(0).IsChecked != false) throw new InvalidOperationException("on-save check not turned off again");
            Save(rec, "export-onsave-check");
        });
    }

    /// <summary>번호 색(목록 네 줄): 1 파랑 · 2 주황 · 3 보라 · 4 청록.</summary>
    private static readonly Avalonia.Media.Color[] RunColors =
        [Avalonia.Media.Color.FromRgb(59, 130, 246), Avalonia.Media.Color.FromRgb(245, 140, 20), Avalonia.Media.Color.FromRgb(168, 85, 247), Avalonia.Media.Color.FromRgb(20, 170, 160)];

    /// <summary>
    /// 지금 내보내기: 네 줄(1 시트 PNG · 2 GIF는 체크, 3 · 4 GIF는 체크 안 함) 중 목록에서 3을 골라 [내보내기]를 누르면 고른 3만 내보내진다
    /// ("내보냄!" — 체크한 1 · 2도, 체크 안 한 4도 "안 내보냄"). GIF 줄은 내보내도 창이 열린 채라 목록 칸과 단추 두 곳만 붙여 찍는다.
    /// </summary>
    [Shot("clip-tutorial-export-run")]
    public static void ExportRun(ShotKit k)
    {
        const int Target = 2;
        var m = ScreenDemo.OpenFolder(k, width: 1280, height: 900);
        ScreenDemo.Build(k, m, group: false);
        var folder = Path.Combine(k.WorkDir, ScreenDemo.FolderName);
        ExportWithGif(k, m, (w, _) =>
        {
            var paneControl = ShotKit.Get<Control>(w, "TutorialListPane");
            var list = ShotKit.Get<StackPanel>(w, "_list");
            Control Row(int i) => (Control)list.Children[i];
            CheckBox Check(int i) => ShotKit.All<CheckBox>(paneControl).Where(c => c.IsEffectivelyVisible).ElementAt(i);
            // GIF 줄을 골라 더하기 두 번(고른 줄을 복사) → 1 시트 · 2 · 3 · 4 GIF.
            k.Click(Row(1));
            k.Settle(300);
            for (int i = 0; i < 2; i++)
            {
                k.Click(ShotKit.Get<Control>(w, "_addButton"));
                k.Settle(300);
            }
            if (list.Children.Count != 4) throw new InvalidOperationException("expected four export rows, got " + list.Children.Count);
            // 1 · 2는 체크, 3 · 4는 체크 안 함(바꾸면 목록을 다시 그려 줄마다 새로 찾음).
            bool[] on = [true, true, false, false];
            for (int i = 0; i < on.Length; i++)
                if (Check(i).IsChecked != on[i])
                {
                    Check(i).IsChecked = on[i];
                    k.Settle(200);
                }
            // 줄마다(시트 · GIF) 오른쪽 설정 칸 크기가 달라 창 크기가 바뀌므로, 두 종류를 골라 본 큰 크기로 고정한다(녹화 중 단추 자리가 그대로).
            k.Click(Row(Target));
            k.Settle(300);
            double width = w.Bounds.Width, height = w.Bounds.Height;
            k.Click(Row(0));
            k.Settle(300);
            w.SizeToContent = SizeToContent.Manual;
            w.MinWidth = w.MaxWidth = w.Width = Math.Max(width, w.Bounds.Width);
            w.MinHeight = w.MaxHeight = w.Height = Math.Max(height, w.Bounds.Height);
            // 단추 줄은 고른 줄 종류에 따라 [내보내기] 자리가 달라진다: 두 자리를 다 담는다.
            k.Click(Row(Target));
            k.Settle(300);
            var buttonGif = Probe(k, m, ShotKit.Get<Control>(w, "TutorialExport"));
            // 처음에는 1(시트) 줄을 고른 상태.
            k.Click(Row(0));
            k.Settle(300);
            var files = Directory.GetFiles(folder, "*.*").Where(f => f.EndsWith(".gif") || f.EndsWith(".png")).ToDictionary(f => f, File.GetLastWriteTimeUtc);
            List<string> Changed() => [.. Directory.GetFiles(folder, "*.*").Where(f => (f.EndsWith(".gif") || f.EndsWith(".png"))
                && (!files.TryGetValue(f, out var t) || File.GetLastWriteTimeUtc(f) != t))];
            var pane = Probe(k, m, paneControl);
            var rows = Enumerable.Range(0, 4).Select(i => Probe(k, m, Row(i))).ToList();
            var checks = Enumerable.Range(0, 4).Select(i => Probe(k, m, Check(i))).ToList();
            var button = Probe(k, m, ShotKit.Get<Control>(w, "TutorialExport"));
            var listArea = Fit(new Rect(pane.X, pane.Y, pane.Width, rows[^1].Bottom - pane.Y + 40), m);
            var buttonArea = Fit(button.Union(buttonGif).Inflate(new Thickness(16, 16, 16, 16)), m);
            using var rec = new ClipRecorder(k, m, listArea.Union(buttonArea), frameMs: 40) { Regions = [listArea, buttonArea] };
            for (int i = 0; i < 4; i++) rec.Badge(checks[i].TopLeft + new Vector(-1, -1), (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), RunColors[i]);
            rec.Place(new Point(rows[Target].Center.X + 40, rows[^1].Bottom + 25));
            rec.Hold(800);
            // 3(체크 안 한 GIF) 줄을 고른다(체크 상자가 아니라 줄 글자를 누름).
            rec.Click(new Point(rows[Target].X + rows[Target].Width * 0.55, rows[Target].Center.Y), 700, pressMs: 120, afterMs: 600);
            // GIF 미리보기가 준비돼 단추가 켜질 때까지(켜지기 전 누르면 아무 일도 없음).
            var ok = ShotKit.Get<Control>(w, "TutorialExport");
            for (int i = 0; i < 60 && !ok.IsEnabled; i++) rec.Hold(100);
            rec.Click(rec.At(ok), 1000, pressMs: 150, afterMs: 200);
            for (int i = 0; i < 80 && Changed().Count == 0; i++) rec.Hold(100);
            rec.Hold(300);
            var changed = Changed();
            if (changed.Count != 1 || !changed[0].EndsWith(".gif")) throw new InvalidOperationException("exporting row 3 should write exactly one GIF, wrote: " + string.Join(", ", changed.Select(Path.GetFileName)));
            // 고른 3만 내보냄, 나머지는(체크했어도) 안 내보냄.
            rec.Note(new Point(rows[Target].Right - 128, rows[Target].Y + 4), UiStrings.Tutorial_Ex_ClipExported, ok: true);
            rec.Hold(500);
            for (int i = 0; i < 4; i++)
                if (i != Target) rec.Note(new Point(rows[i].Right - 128, rows[i].Y + 4), UiStrings.Tutorial_Ex_ClipNotExported, ok: false);
            rec.Hold(3000);
            Save(rec, "export-run");
        });
    }

    /// <summary>내보내기 창을 열어 목록 줄 앞 체크를 <paramref name="on"/>대로 맞춘다(창은 그 뒤 닫힘 — 바꾸면 목록을 다시 그려 줄마다 새로 찾음).</summary>
    private static void SetOnSave(ShotKit k, MainForm m, params bool[] on)
        => k.Dialog(() => ShotKit.Call(m, "OpenExportDialog"), w =>
        {
            var pane = ShotKit.Get<Control>(w, "TutorialListPane");
            for (int i = 0; i < on.Length; i++)
            {
                var check = ShotKit.All<CheckBox>(pane).Where(c => c.IsEffectivelyVisible).ElementAt(i);
                if (check.IsChecked == on[i]) continue;
                check.IsChecked = on[i];
                k.Settle(200);
            }
        });

    /// <summary>
    /// 저장할 때 내보내기 ②: 시트 PNG 줄은 체크, GIF 줄은 체크 안 한 채(둘 다 한 번 내보내 둠) 캔버스 배경을 빨강으로 채우고 저장 단추를 누르면
    /// 파일 칸에서 체크한 PNG만 새 그림으로 바뀌고(갱신!) GIF는 그대로다(갱신 안 됨). 파일 목록·저장 단추 칸과 캔버스 그림 두 곳만 붙여 찍는다.
    /// </summary>
    [Shot("clip-tutorial-export-onsave-save")]
    public static void ExportOnSaveSave(ShotKit k)
    {
        // 피벗 표시(레이어 이름 글)는 이 움짤과 상관없어 끈 채로 연다.
        App.Settings.ShowPivot = false;
        var m = ScreenDemo.OpenFolder(k, width: 1280, height: 720);
        ScreenDemo.Build(k, m, group: false);
        ExportWithGif(k, m, (_, gif) => gif.IsChecked = true);
        SetOnSave(k, m, true, true);
        var folder = Path.Combine(k.WorkDir, ScreenDemo.FolderName);
        k.Key(m, Key.S, RawInputModifiers.Control);
        k.Settle(800);
        var gif = Directory.GetFiles(folder, "*_2.gif").Single();
        var browser = ShotKit.Get<Control>(m, "_browser");
        var items = ShotKit.Get<System.Collections.IDictionary>(browser, "_items");
        ShotKit.Call(browser, "RefreshAll");
        k.Settle(500);
        var project = items.Keys.Cast<string>().First(p => p.EndsWith(".chickxel", StringComparison.OrdinalIgnoreCase));
        var sheet = Path.ChangeExtension(project, ".png");
        if (!File.Exists(sheet)) throw new InvalidOperationException("sheet PNG not exported: " + sheet);
        // 이제 PNG 줄만 체크해 두고, 프로젝트 묶음을 펼쳐 둔다.
        SetOnSave(k, m, true, false);
        ShotKit.Call(browser, "SetGroupExpanded", project, true);
        k.Settle(600);
        ScreenDemo.PrettyRoot(m);
        var sheetTime = File.GetLastWriteTimeUtc(sheet);
        var gifTime = File.GetLastWriteTimeUtc(gif);

        var ctx = ToolFileHelp.Ctx(m);
        ctx.Tool = ToolKind.Fill;
        ctx.Primary = new Color32(229, 57, 53);
        k.Settle(200);
        var p = ScreenDemo.Project(m);
        var list = ShotKit.Bounds(ShotKit.Get<Control>(browser, "_list"), m);
        var save = ShotKit.Bounds(ShotKit.Get<Control>(m, "_tbSave"), m);
        var top = Math.Min(list.Y, save.Bottom - 300);
        var files = Fit(new Rect(0, top, save.Right + 6, Math.Max(save.Bottom + 8, list.Y + 300) - top), m);
        var a = ToolFileHelp.At(m, 0, 0);
        var b = ToolFileHelp.At(m, p.Width, p.Height);
        var canvas = Fit(new Rect(a, b).Inflate(10), m);
        using var rec = new ClipRecorder(k, m, files.Union(canvas), frameMs: 40) { Regions = [files, canvas] };
        // 왼쪽 움짤 목록 줄과 같은 번호·색: 1 = 시트 PNG, 2 = GIF(썸네일 왼쪽 위).
        Control Tile(string path) => (Control)items[path]!.GetType().GetProperty("Container")!.GetValue(items[path])!;
        rec.Badge(ShotKit.Bounds(Tile(sheet), m).TopLeft + new Vector(10, 10), "1", OnSaveSheet);
        rec.Badge(ShotKit.Bounds(Tile(gif), m).TopLeft + new Vector(10, 10), "2", OnSaveGif);
        rec.Place(new Point(canvas.Center.X, canvas.Bottom - 10));
        rec.Hold(700);
        // 원본을 고친다(배경을 빨강으로 채우기).
        rec.Cursor = ClipCursor.Cross;
        rec.Click(ToolFileHelp.At(m, 1.5, 1.5), 700, pressMs: 120, afterMs: 700);
        rec.Cursor = ClipCursor.Arrow;
        // 저장 단추.
        rec.Click(save.Center, 900, pressMs: 150, afterMs: 300);
        for (int i = 0; i < 30 && File.GetLastWriteTimeUtc(sheet) == sheetTime; i++) rec.Hold(100);
        ShotKit.Call(browser, "RefreshAll");
        ScreenDemo.PrettyRoot(m);
        rec.Hold(600);
        if (File.GetLastWriteTimeUtc(sheet) == sheetTime) throw new InvalidOperationException("saving did not re-export the checked sheet");
        if (File.GetLastWriteTimeUtc(gif) != gifTime) throw new InvalidOperationException("saving re-exported the unchecked GIF");
        // 체크한 1(PNG)은 갱신, 체크 안 한 2(GIF)는 그대로(번호 옆에 표).
        var sheetTile = ShotKit.Bounds(Tile(sheet), m);
        var gifTile = ShotKit.Bounds(Tile(gif), m);
        rec.Note(new Point(sheetTile.X + 22, sheetTile.Y + 2), UiStrings.Tutorial_Ex_ClipUpdated, ok: true);
        rec.Hold(500);
        rec.Note(new Point(gifTile.X + 22, gifTile.Y + 2), UiStrings.Tutorial_Ex_ClipNotUpdated, ok: false);
        rec.Hold(2800);
        Save(rec, "export-onsave-save");
    }

    /// <summary>칸 고르기: 몸 레이어 3프레임 칸을 눌러 5프레임 칸까지 끌면 세 칸이 셀 묶음으로 골라진다.</summary>
    [Shot("clip-tutorial-effects-select")]
    public static void EffectsSelect(ShotKit k)
    {
        var m = EffectsMain(k);
        int body = Body(m);
        var first = Cel(m, body, 2);
        var last = Cel(m, body, 4);
        var crop = Fit(new Rect(first.X - 160, first.Y - 40, last.Right - first.X + 220, first.Height + 40 + 30), m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        rec.Place(new Point(first.Center.X - 40, first.Bottom + 20));
        rec.Hold(500);
        rec.Drag(first.Center, last.Center, 900);
        rec.Hold(1600);
        Save(rec, "effects-select");
    }

    /// <summary>
    /// 효과 넣기: 몸 레이어 3~5프레임 칸을 골라 둔 상태에서 외곽선 상자를 그 칸으로 끌어 놓으면 그 세 프레임에만 효과 줄이 생긴다.
    /// 상자와 칸은 멀리 떨어져 있어 두 곳만 잘라 붙여 찍는다(<see cref="ClipRecorder.Regions"/> — 사이를 지나는 동안은 짧게).
    /// </summary>
    [Shot("clip-tutorial-effects-add")]
    public static void EffectsAdd(ShotKit k)
    {
        var m = EffectsMain(k);
        int body = Body(m);
        ShotKit.Call(Grid(m), "SelectRect", body, 2, null, 4);
        k.Settle(300);
        var tile = ShotKit.Bounds(OutlineTile(m), m);
        var first = Cel(m, body, 2);
        var last = Cel(m, body, 4);
        var drop = Cel(m, body, 3);
        var tileArea = Fit(tile.Inflate(8), m);
        var celArea = Fit(new Rect(first.X - 24, first.Y - 40, last.Right - first.X + 48, first.Height + 40 + 30), m);
        using var rec = new ClipRecorder(k, m, tileArea.Union(celArea), frameMs: 40) { Regions = [tileArea, celArea] };
        rec.Place(new Point(tile.Center.X + 40, tile.Bottom + 2));
        rec.Hold(500);
        rec.Move(tile.Center, 500);
        rec.Hold(250);
        rec.Down();
        rec.Hold(150);
        rec.Move(new Point(tileArea.Right + 4, tile.Center.Y), 250);
        rec.Move(new Point(celArea.X - 4, drop.Center.Y), 300);
        rec.Move(drop.Center, 500);
        rec.Hold(250);
        rec.Up();
        rec.Hold(1600);
        var effects = ScreenDemo.Project(m).Layers[body].Effects;
        if (effects.Count == 0 || effects[0].Frames is not { From: 2, To: 4 }) throw new InvalidOperationException("effect not dropped on frames 3-5");
        Save(rec, "effects-add");
    }

    /// <summary>효과 설정: 넣은 외곽선의 첫 숫자 칸(두께) 위 단추를 두 번 누른다.</summary>
    [Shot("clip-tutorial-effects-adjust")]
    public static void EffectsAdjust(ShotKit k)
    {
        var m = EffectsMain(k);
        k.DoubleClick(OutlineTile(m));
        k.Settle(600);
        var section = ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_toolOptions"), "_effectSection");
        section.BringIntoView();
        k.Settle(500);
        var box = ShotKit.Bounds(section, m);
        var num = ShotKit.All<NumericUpDown>(section).First(n => n.IsEffectivelyVisible);
        var nb = ShotKit.Bounds(num, m);
        var crop = Fit(new Rect(box.X, box.Y, box.Width, Math.Min(box.Height, nb.Bottom - box.Y + 90)), m);
        var up = new Point(nb.Right - 9, nb.Y + nb.Height * 0.28);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        rec.Place(new Point(crop.Center.X, crop.Bottom - 20));
        rec.Hold(500);
        rec.Move(up, 700);
        rec.Hold(200);
        rec.Click(up, 0, pressMs: 120, afterMs: 700);
        rec.Click(up, 0, pressMs: 120, afterMs: 1400);
        Save(rec, "effects-adjust");
    }

    /// <summary>효과 줄: 1프레임에만 넣은 외곽선 상자의 오른쪽 끝을 4프레임까지 끌어 범위를 늘린다.</summary>
    [Shot("clip-tutorial-effects-timeline")]
    public static void EffectsTimeline(ShotKit k)
    {
        var m = EffectsMain(k);
        k.DoubleClick(OutlineTile(m));
        k.Settle(600);
        var g = Grid(m);
        int body = Body(m);
        int cellH = ShotKit.Get<int>(g, "CellH");
        const int FxH = 16;
        int layerY = (int)ShotKit.Call(g, "LayerY", body)!;
        int X(int f) => (int)ShotKit.Call(g, "FrameX", f)!;
        int W(int f) => (int)ShotKit.Call(g, "FrameW", f)!;
        Point At(double x, double y) => g.TranslatePoint(new Point(x, y), m)!.Value;
        double rowY = layerY + cellH + FxH / 2.0;
        var from = At(X(0) + W(0) - 2, rowY);
        var to = At(X(3) + W(3) - 3, rowY);
        var topLeft = At(X(0) - 200, layerY - 6);
        var bottomRight = At(X(5) + W(5) + 12, layerY + cellH + FxH + 12);
        var crop = Fit(new Rect(topLeft, bottomRight), m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        rec.Place(new Point(from.X - 60, from.Y + 30));
        rec.Hold(500);
        rec.Move(from, 600);
        rec.Hold(300);
        rec.Down();
        rec.Hold(150);
        rec.Move(to, 1100);
        rec.Hold(150);
        rec.Up();
        rec.Hold(1600);
        var effect = ScreenDemo.Project(m).Layers[body].Effects[0];
        if (effect.Frames is not { To: 3 }) throw new InvalidOperationException("effect range not extended: " + effect.Frames);
        Save(rec, "effects-timeline");
    }

    // ───────────── 그림을 픽셀 아트로 ─────────────

    /// <summary>AI 그림 흉내를 넣은 픽셀로 정리 창(프리셋마다 짧은 예산). <paramref name="run"/>이면 체크한 프리셋을 한 번 돌려 결과까지. <paramref name="image"/>를 주면 그 그림.</summary>
    private static PixelateDialog PixelateOpen(ShotKit k, bool run, string? image = null)
    {
        foreach (var p in PixelateKit.Options().OptimizePresets)
        {
            p.Trials = 40;
            p.Seconds = 3;
        }
        var main = ScreenDemo.Open(k);
        ShotKit.Call(main, "PixelateFile", image ?? PixelateKit.MakeAiImage(k));
        k.Settle(400);
        var dlg = k.Windows.OfType<PixelateDialog>().Last();
        if (run) PixelateKit.WaitCandidates(k, dlg);
        else
            for (int i = 0; i < 600 && ShotKit.Get<bool>(dlg, "TutorialReady") is false; i++) k.Settle(200);
        k.Settle(500);
        return dlg;
    }

    private static MainForm Main(ShotKit k) => k.Windows.OfType<MainForm>().First();


    /// <summary>찍기 전에 대화상자 안 컨트롤의 메인 창 좌표(녹화기가 대화상자를 놓는 자리 기준 — 찍을 영역을 정하려고).</summary>
    private static Rect Probe(ShotKit k, MainForm m, Control c)
    {
        using var probe = new ClipRecorder(k, m, new Rect(0, 0, 2, 2));
        return probe.BoundsOf(c);
    }
    /// <summary>프리셋 하나만 돌리고 멈추기: '같은 색상 계열' 칸 우클릭 → '이 프리셋만 돌리기' → 그 칸이 돌고 자동 최적화 단추가 '취소'로 → 취소를 누르면 멈춘다.</summary>
    [Shot("clip-tutorial-pixelate-rerun")]
    public static void PixelateRerun(ShotKit k)
    {
        var dlg = PixelateOpen(k, run: false);
        var m = Main(k);
        var found = ShotKit.Call(dlg, "TutorialIdleTile", "shade") ?? throw new InvalidOperationException("no idle preset tile");
        var tile = (Control)found.GetType().GetField("Item1")!.GetValue(found)!;
        var button = ShotKit.Get<Control>(dlg, "_optRun");
        var t = Probe(k, m, tile);
        var b = Probe(k, m, button);
        var crop = Fit(new Rect(t.X - 12, t.Y - 12, t.Width + 260, t.Height + 24).Union(b.Inflate(12)), m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        rec.Place(new Point(t.Right + 120, t.Center.Y + 40));
        rec.Hold(500);
        rec.Click(t.Center, 700, MouseButton.Right, pressMs: 120, afterMs: 700);
        var item = ShotKit.OpenPopups.Where(p => p.Child is not null).SelectMany(p => ShotKit.All<MenuItem>(p.Child!))
            .First(i => i.IsEffectivelyVisible && (i.Header as string ?? (i.Header as TextBlock)?.Text) == UiStrings.Pixelate_Opt_RunPreset);
        rec.Move(rec.At(item), 600);
        rec.Hold(300);
        rec.Click(rec.At(item), 0, pressMs: 120, afterMs: 2200);
        rec.Click(rec.At(button), 700, pressMs: 120, afterMs: 1500);
        Save(rec, "pixelate-rerun");
    }

    /// <summary>결과 크기: '자동'을 끈 간편 설정의 가로 칸(48)을 우클릭 → 예시 값 '64'를 고른다(튜토리얼이 권하는 크기).</summary>
    [Shot("clip-tutorial-pixelate-size")]
    public static void PixelateSize(ShotKit k)
    {
        var dlg = PixelateOpen(k, run: false);
        var m = Main(k);
        // 튜토리얼처럼 설정 칸을 펼친 뒤(접혀 있으면 원본 그림이 그 자리를 덮음) 간편 설정의 결과 크기 줄.
        if (!ShotKit.Get<bool>(dlg, "TutorialSettingsOpen")) k.Click(ShotKit.Get<Control>(dlg, "TutorialSettingsToggle"));
        k.Settle(400);
        var row = (Control)ShotKit.Call(dlg, "TutorialQuickSizeRow")!;
        ShotKit.Get<CheckBox>(dlg, "_quickAuto").IsChecked = false;
        var width = ShotKit.Get<NumericUpDown>(ShotKit.Get<object>(dlg, "_quickSize"), "WidthBox");
        width.Value = 48;
        k.Settle(400);
        row.BringIntoView();
        k.Settle(400);
        var r = Probe(k, m, row);
        var w = Probe(k, m, width);
        // 우클릭 메뉴(예시 값 일곱 줄)가 칸 아래로 펼쳐지므로 아래에 자리를 둔다.
        var crop = Fit(new Rect(r.X - 10, r.Y - 10, r.Width + 20, r.Height + 250), m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        rec.Place(new Point(r.Center.X, r.Bottom + 60));
        var at = rec.BoundsOf(width);
        rec.Hold(500);
        rec.Hold(200);
        rec.Click(at.Center, 600, MouseButton.Right, pressMs: 120, afterMs: 600);
        var items = ShotKit.OpenPopups.Where(p => p.Child is not null).SelectMany(p => ShotKit.All<MenuItem>(p.Child!)).ToList();
        var item = items.FirstOrDefault(i => i.IsEffectivelyVisible && (i.Header as TextBlock)?.Text == "64")
            ?? throw new InvalidOperationException($"no 64 item (focus {width.IsKeyboardFocusWithin}): " + string.Join(" | ", items.Select(i => (i.Header as TextBlock)?.Text ?? i.Header?.ToString())));
        rec.Move(rec.At(item), 600);
        rec.Hold(300);
        rec.Click(rec.At(item), 0, pressMs: 120, afterMs: 1600);
        if (ShotKit.Get<int?>(dlg, "TutorialFixedWidth") != 64) throw new InvalidOperationException("width not set to 64");
        Save(rec, "pixelate-size");
    }

    // 색 빼기·더하기 움짤은 차이가 확실히 보이게 일부러 그린 단순한 그림으로 찍는다: 빨강 · 초록 · 파랑 주사위 셋(검은 테두리, 흰 눈)을
    // AI 그림처럼 흐리게 키운 JPG. 색 하나를 빼면 주사위 하나의 색이 통째로 바뀌고, 색 수를 줄여 빠진 주사위 색은 원본에서 더하면 돌아온다.

    private const int DiceSize = 12, DiceGap = 3, DiceScale = 8;
    /// <summary>흐리게 키울 때 둘레 여백(<see cref="PixelateKit.MakeAiImage(ShotKit, PixelImage, double, string)"/>, px).</summary>
    private const double DiceMargin = 44;
    private static readonly Color32[] DiceColors = [new(225, 45, 45), new(40, 175, 75), new(50, 95, 225)];
    private static readonly int[] DiceFaces = [3, 5, 2];

    /// <summary>주사위 셋 그림(픽셀 좌표) 너비·높이.</summary>
    private static (int Width, int Height) DiceCanvas => (DiceGap + DiceColors.Length * (DiceSize + DiceGap), DiceSize + DiceGap * 2);

    /// <summary>주사위 <paramref name="i"/>의 왼쪽 위(픽셀 좌표).</summary>
    private static (int X, int Y) DieAt(int i) => (DiceGap + i * (DiceSize + DiceGap), DiceGap);

    /// <summary>빨강 · 초록 · 파랑 주사위 셋(둥근 검은 테두리, 흰 눈 2×2)을 그려 AI 그림처럼 흐리게 키운 JPG.</summary>
    private static string DiceImage(ShotKit k)
    {
        var (w, h) = DiceCanvas;
        var img = new PixelImage(w, h);
        var black = new Color32(25, 25, 30);
        var white = new Color32(250, 250, 250);
        for (int i = 0; i < DiceColors.Length; i++)
        {
            var (x0, y0) = DieAt(i);
            for (int y = 0; y < DiceSize; y++)
                for (int x = 0; x < DiceSize; x++)
                {
                    bool corner = (x == 0 || x == DiceSize - 1) && (y == 0 || y == DiceSize - 1);
                    if (corner) continue;
                    bool edge = x == 0 || y == 0 || x == DiceSize - 1 || y == DiceSize - 1;
                    img.Pixels[(y0 + y) * w + x0 + x] = edge ? black : DiceColors[i];
                }
            // 눈: 안쪽 3×3 자리(2 · 5 · 8)에 2×2.
            (int, int)[] pips = DiceFaces[i] switch
            {
                1 => [(5, 5)],
                2 => [(2, 2), (8, 8)],
                3 => [(2, 2), (5, 5), (8, 8)],
                _ => [(2, 2), (8, 2), (5, 5), (2, 8), (8, 8)],
            };
            foreach (var (px, py) in pips)
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 2; x++) img.Pixels[(y0 + py + y) * w + x0 + px + x] = white;
        }
        return PixelateKit.MakeAiImage(k, img, DiceScale, "tutorial-dice");
    }

    /// <summary>
    /// 주사위 그림을 튜토리얼과 같은 길로 정리한 픽셀로 정리 창: 프리셋 '기본', 결과 크기 '자동' 끄고 가로 = 그린 픽셀 수 + 둘레 여백 칸(칸이 그대로 맞게).
    /// <paramref name="colors"/>를 주면 팔레트 색 수 '자동'을 끄고 그 수로.
    /// </summary>
    private static PixelateDialog PixelateDice(ShotKit k, int? colors = null)
    {
        var dlg = PixelateOpen(k, run: false, DiceImage(k));
        var box = ShotKit.Get<ComboBox>(dlg, "_preset");
        box.SelectedIndex = box.Items.IndexOf(ShotKit.Get<string?>(dlg, "TutorialDefaultPresetName") ?? throw new InvalidOperationException("no default preset"));
        k.Settle(300);
        ShotKit.Get<CheckBox>(dlg, "_quickAuto").IsChecked = false;
        ShotKit.Get<NumericUpDown>(ShotKit.Get<object>(dlg, "_quickSize"), "WidthBox").Value = DiceCanvas.Width + (int)Math.Round(2 * DiceMargin / DiceScale);
        if (colors is { } n)
        {
            ShotKit.Get<CheckBox>(dlg, "_palAuto").IsChecked = false;
            ShotKit.Get<NumericUpDown>(dlg, "_palColors").Value = n;
        }
        k.Settle(200);
        ShotKit.Call(dlg, "RunCurrent");
        for (int i = 0; i < 600 && (ShotKit.Get<bool>(dlg, "TutorialRunning") || ShotKit.Get<object?>(dlg, "_selected") is null); i++) k.Settle(200);
        k.Settle(800);
        return dlg;
    }

    private static IReadOnlyList<Color32> ResultPalette(PixelateDialog dlg)
        => ShotKit.Get<IReadOnlyList<Color32>>(ShotKit.Get<object>(ShotKit.Get<object>(dlg, "_selected"), "Result"), "Palette");

    private static double Distance(Color32 a, Color32 b) => Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    /// <summary>큰 미리보기 패널(결과 | 원본 그림 + 색 줄)의 메인 창 좌표 — 색을 빼고 더하면 결과가 함께 바뀌는 것이 보이게.</summary>
    private static Rect PreviewArea(ShotKit k, MainForm m, PixelateDialog dlg)
    {
        var preview = Probe(k, m, ShotKit.Get<Control>(dlg, "_preview"));
        var colors = Probe(k, m, ShotKit.Get<Control>(dlg, "_colors"));
        // 알림 줄이 생기면 색 줄 묶음이 자라므로 아래에 자리를 둔다.
        return Fit(preview.Union(colors).Inflate(new Thickness(4, 4, 4, 28)), m);
    }

    /// <summary>색 줄에서 <paramref name="color"/>의 색 칸.</summary>
    private static Control SwatchOf(PixelateDialog dlg, Color32 color)
        => ShotKit.Get<Panel>(ShotKit.Get<Control>(dlg, "_colors"), "_swatches").Children
            .First(c => c.IsVisible && c.GetType().GetProperty("Value")?.GetValue(c) is Color32 v && v.Equals(color));

    /// <summary>색 빼기: 초록 주사위의 색 칸을 우클릭하면 그 색이 빠져 초록 주사위가 통째로 가까운 다른 색으로 바뀌고 "색 빼기 · 모두 되살리기" 줄이 생긴다.</summary>
    [Shot("clip-tutorial-pixelate-colors")]
    public static void PixelateColors(ShotKit k)
    {
        var dlg = PixelateDice(k);
        var m = Main(k);
        var green = ResultPalette(dlg).MinBy(c => Distance(c, DiceColors[1]));
        if (Distance(green, DiceColors[1]) > 90) throw new InvalidOperationException($"no green in the result ({green.ToHex()})");
        var swatch = SwatchOf(dlg, green);
        using var rec = new ClipRecorder(k, m, PreviewArea(k, m, dlg), frameMs: 40);
        var s = rec.BoundsOf(swatch).Center;
        rec.Place(new Point(s.X + 90, s.Y - 50));
        rec.Hold(900);
        rec.Click(s, 700, MouseButton.Right, pressMs: 120, afterMs: 2200);
        rec.Move(new Point(s.X + 90, s.Y - 50), 600);
        rec.Hold(1200);
        if (ShotKit.Get<string>(dlg, "TutorialExcludedText") == "") throw new InvalidOperationException("green not excluded");
        Save(rec, "pixelate-colors");
    }

    /// <summary>
    /// 색 더하기: 팔레트 색 수를 줄여 주사위 하나의 색이 결과에서 빠진 것을, + 칸을 누르고 원본의 그 주사위를 누르면
    /// 그 색이 고정 색으로 더해져 결과 주사위 색이 돌아온다("고정 색 1개 · 모두 해제" 줄이 생김).
    /// </summary>
    [Shot("clip-tutorial-pixelate-pin")]
    public static void PixelatePin(ShotKit k)
    {
        var dlg = PixelateDice(k, colors: 4);
        var m = Main(k);
        var palette = ResultPalette(dlg);
        int lost = Array.FindIndex(DiceColors, d => palette.All(c => Distance(c, d) > 90));
        if (lost < 0) throw new InvalidOperationException("every die color survived the color limit: " + string.Join(",", palette.Select(c => c.ToHex())));
        var preview = ShotKit.Get<Control>(dlg, "_preview");
        var source = ShotKit.Get<Rect>(preview, "_sourceRect");
        var add = ShotKit.Get<Control>(ShotKit.Get<Control>(dlg, "_colors"), "_add");
        using var rec = new ClipRecorder(k, m, PreviewArea(k, m, dlg), frameMs: 40);
        var plus = rec.BoundsOf(add).Center;
        var p = rec.BoundsOf(preview);
        // 잃은 주사위의 눈 없는 자리(왼쪽 아래 칸 2·5 사이): 그린 픽셀 → 흐리게 키운 JPG 비율 → 미리보기 원본 자리.
        var (dx, dy) = DieAt(lost);
        var (cw, ch) = DiceCanvas;
        double jx = DiceMargin + (dx + 4) * DiceScale, jy = DiceMargin + (dy + 9) * DiceScale;
        double fx = jx / (cw * DiceScale + DiceMargin * 2), fy = jy / (ch * DiceScale + DiceMargin * 2);
        var spot = new Point(p.X + source.X + source.Width * fx, p.Y + source.Y + source.Height * fy);
        rec.Place(new Point(plus.X - 80, plus.Y - 50));
        rec.Hold(900);
        rec.Click(plus, 700, pressMs: 120, afterMs: 600);
        rec.Click(spot, 1300, pressMs: 120, afterMs: 2200);
        rec.Move(new Point(spot.X + 50, spot.Y + 50), 600);
        rec.Hold(1000);
        var pinned = ShotKit.Get<IReadOnlyList<string>>(dlg, "_pinned");
        if (pinned.Count == 0 || !Color32.TryParseHex(pinned[0], out var got) || Distance(got, DiceColors[lost]) > 100)
            throw new InvalidOperationException($"die {lost} color was not added: " + string.Join(",", pinned));
        Save(rec, "pixelate-pin");
    }

    // ───────────── 3D를 픽셀 아트로 ─────────────

    /// <summary>
    /// 튜토리얼 창의 '3D를 픽셀 아트로' 카드 움짤(<c>Assets/Tutorial/card-model.webp</c>): 3D 모델 창에서 예시 병아리 모델을 끌어 돌리면
    /// 오른쪽 픽셀 결과가 따라 바뀐다. 원본 · 결과 두 칸을 16:9로 잘라 찍는다.
    /// </summary>
    [Shot("clip-tutorial-model-card")]
    public static void ModelCard(ShotKit k)
    {
        var m = ScreenDemo.Open(k, width: 1280, height: 900);
        k.Settle(400);
        var path = ModelSample.Chick(k.WorkDir);
        var scene = Chickxel.Core.Scene3D.ModelLoader.Load(path);
        k.Dialog(() => new ModelImportDialog(path, scene).ShowDialog(m), w =>
        {
            ShotKit.Pump(1500);
            var preview = ShotKit.Get<Control>(w, "_preview");
            // 미리보기 두 칸(664×330) + 아래 조금 → 16:9. 대화상자는 메인 창 가운데에 그려진다(ClipRecorder.DialogOrigin과 같은 셈).
            var size = w.ClientSize;
            var origin = new Point(Math.Round((m.ClientSize.Width - size.Width) / 2),
                Math.Round(Math.Max(0, (m.ClientSize.Height - size.Height - ShotKit.TitleHeight) / 2)) + ShotKit.TitleHeight);
            var pb = ShotKit.Bounds(preview, w).Translate((Vector)origin);
            var crop = new Rect(pb.X, pb.Y, Math.Floor(pb.Width / 2) * 2, Math.Round(pb.Width * 9 / 16 / 2) * 2);
            // 모델이 돌면 두 칸이 통째로 바뀌어 프레임마다 무거우므로 프레임을 성기게(100ms).
            using var rec = new ClipRecorder(k, m, crop, frameMs: 100);
            // 원본 칸 가운데에서 조금 왼쪽 아래(고리·축이 아닌 곳 — 누르면 모델 돌리기). 가로 1px = 0.5°.
            var left = new Point(pb.X + pb.Width / 4 - 40, pb.Center.Y + 60);
            rec.Place(new Point(left.X + 40, left.Y + 50));
            rec.Hold(600);
            rec.Move(left, 500);
            rec.Hold(200);
            // 가로로 80°쯤, 이어 위로 조금 — 오른쪽 결과가 따라 바뀐다.
            rec.Drag(left, new Point(left.X + 160, left.Y), 1800);
            rec.Hold(700);
            rec.MarkPoster();
            // 다시 같은 자리에서 위로(바깥 고리를 잡으면 화면 회전이 되므로 늘 안쪽에서 누른다).
            rec.Move(left, 500);
            rec.Drag(left, new Point(left.X + 10, left.Y - 50), 1200);
            rec.Hold(1200);
            // 카드(폭 264)에 작게만 보이므로 두 배 폭으로 줄여 쓴다(앱에 넣는 파일 크기).
            rec.Save("src/Chickxel.App/Assets/Tutorial/card-model", ClipFormat.WebP, webpLossless: false, webpQuality: 80, webpWidth: 528);
        }, settleMs: 1200);
    }

    // ───────────── 레벨 ─────────────

    /// <summary>
    /// 갤러리에 올리기(튜토리얼 '레벨'의 큰 움짤 — 실제로 올리지 않고 보이기만): 로그인한 계정으로 메뉴 줄의 갤러리 단추 → 올리기 창에서 제목 적기
    /// → 편집 파일 함께 올리기 켜기 → 올리기(올리는 중 막대) → 올린 뒤 알림(+15 XP). 가짜 서버라 올리기 단추의 실제 동작 대신 창의 올리는 중 표시와
    /// 앱이 올린 뒤 띄우는 알림 글(<c>Gallery_Done</c> + <c>Gallery_XpGained</c>)을 그대로 띄운다.
    /// </summary>
    [Shot("clip-tutorial-level-upload")]
    public static void LevelUpload(ShotKit k)
    {
        CommunityShots.UseFakeAccount(signedIn: true);
        // 올리기 창(약 800 높이)이 아래 단추·진행 막대까지 들어가게 창을 세로로 넉넉히.
        var m = ScreenDemo.Open(k, width: 1120, height: 940);
        ScreenDemo.Build(k, m, group: false);
        k.Settle(500);
        var gallery = ShotKit.Get<Button>(m, "_galleryButton");
        using var rec = new ClipRecorder(k, m, new Rect(0, 0, 1120, 940), titleBar: true, frameMs: 50) { TitleFilter = t => t.Contains("Chickxel") ? "Chickxel" : t };
        rec.Place(new Point(820, 300));
        rec.Hold(600);
        rec.Move(rec.At(gallery), 800);
        rec.Hold(300);
        k.Dialog(() => rec.PressAndRelease(rec.At(gallery), 0, pressMs: 120), w =>
        {
            Console.WriteLine($"  gallery dialog {w.ClientSize}");
            // 제목은 문서 이름으로 미리 차 있다: 비우고 새로 적는 모습으로.
            var title = ShotKit.Get<TextBox>(w, "_title");
            title.Text = "";
            rec.Hold(900);
            const string name = "Wolf idle";
            rec.Click(rec.At(title), 700, pressMs: 100, afterMs: 200);
            rec.Type(name, 90);
            rec.Hold(400);
            var share = ShotKit.Get<CheckBox>(w, "_shareProject");
            var sb = rec.BoundsOf(share);
            rec.Click(new Point(sb.X + 10, sb.Center.Y), 800, pressMs: 100, afterMs: 700);
            // 올리기: 누르는 동안 올리는 중으로 바꿔(단추가 꺼져 실제로 보내지 않음) 진행 막대를 채운다.
            var ok = ShotKit.Get<Button>(w, "OkButton");
            rec.Move(rec.At(ok), 800);
            rec.Hold(200);
            rec.Down();
            ShotKit.Call(w, "SetUploading", true);
            ok.IsEnabled = false;
            rec.Hold(150);
            rec.Up();
            for (int i = 1; i <= 10; i++)
            {
                ShotKit.Call(w, "ReportProgress", i / 10.0);
                rec.Hold(130);
            }
            var level = Chickxel.App.AppCommunity.Client.User?.Level ?? 1;
            string done = string.Format(UiStrings.Gallery_Done, name) + "\n\n" + string.Format(UiStrings.Gallery_XpGained, 15, level);
            k.Dialog(() => Ui.Info(w, done, UiStrings.Gallery_Title), _ =>
            {
                rec.MarkPoster();
                rec.Hold(2600);
            }, settleMs: 300);
        }, settleMs: 600);
        rec.Hold(400);
        // 큰 말풍선(움짤 폭 약 610)에 맞춰 줄여 쓴다(앱에 넣는 파일 크기).
        rec.Save(Out + "level-upload", ClipFormat.WebP, webpLossless: false, webpQuality: 75, webpWidth: 860);
    }
}
