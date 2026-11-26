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
    /// 셀 링크(튜토리얼 '레이어와 프레임' <c>frames.link</c>): 몸 레이어 1~2프레임 칸을 끌어 골라 우클릭 → 셀 링크(파란 띠), 이어 4~6프레임을 골라 또 셀 링크(주황 띠) —
    /// 링크는 묶음이 여럿이고 묶음마다 띠 색이 다르다. 메뉴가 뜨는 자리까지 넉넉히 자른다.
    /// </summary>
    [Shot("clip-tutorial-frames-link")]
    public static void FramesLink(ShotKit k)
    {
        // 셀 우클릭 메뉴는 길어(아래로 펼쳐짐) 타임라인을 크게 하고 창을 세로로 넉넉히.
        App.Settings.BottomHeight = 600;
        App.Settings.PaletteWidth = 170;
        App.Settings.RightWidth = 250;
        var m = ScreenDemo.Open(k, width: 1280, height: 1000);
        ScreenDemo.Build(k, m, group: false);
        k.Settle(500);
        int body = Body(m);
        var c0 = Cel(m, body, 0);
        var c5 = Cel(m, body, 5);
        Console.WriteLine($"  cels {c0} .. {c5}");
        var crop = Fit(new Rect(c0.X - 260, c0.Y - 46, c5.Right - c0.X + 520, 520), m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        void Link(int from, int to)
        {
            var a = Cel(m, body, from);
            var b = Cel(m, body, to);
            rec.Drag(a.Center, b.Center, 800);
            rec.Hold(300);
            rec.Click(b.Center, 300, MouseButton.Right, pressMs: 100, afterMs: 500);
            var item = ShotKit.OpenPopups.Where(pop => pop.Child is not null).SelectMany(pop => ShotKit.All<MenuItem>(pop.Child!))
                .First(i => i.IsEffectivelyVisible && (i.Header as string ?? (i.Header as TextBlock)?.Text) == UiStrings.Timeline_LinkCels);
            var at = rec.At(item);
            rec.Move(at, 500);
            rec.Hold(250);
            rec.Click(at, 0, pressMs: 100, afterMs: 1300);
        }
        rec.Place(new Point(c0.Center.X - 20, c0.Bottom + 30));
        rec.Hold(500);
        Link(0, 1);
        Link(3, 5);
        rec.Move(new Point(c5.Right + 60, c5.Bottom + 40), 500);
        rec.Hold(1500);
        var proj = ScreenDemo.Project(m);
        if (!proj.AreCelsLinked(body, 0, 1) || !proj.AreCelsLinked(body, 3, 5)) throw new InvalidOperationException("cels not linked");
        Save(rec, "frames-link");
    }

    /// <summary>
    /// 링크된 칸(튜토리얼 '레이어와 프레임' <c>frames.linked</c>): 예시 늑대 4프레임(1~2프레임 링크)에서 1프레임에 연필로 빨간 줄을 그으면 2프레임 칸 그림도 같이 바뀌고,
    /// 2프레임을 눌러 보면 같은 줄이, 링크 안 된 3프레임에는 없다. 캔버스와 타임라인 칸 두 곳만 잘라 붙인다.
    /// </summary>
    [Shot("clip-tutorial-frames-linked")]
    public static void FramesLinked(ShotKit k)
    {
        var path = Path.Combine(k.WorkDir, "wolf.png");
        File.Copy(ShotKit.SamplePng, path, overwrite: true);
        App.Settings.ShowPivot = false;
        var m = k.Main(path);
        k.Settle(400);
        var ctx = ScreenDemo.Ctx(m);
        var proj = ScreenDemo.Project(m);
        var timeline = ShotKit.Get<Control>(m, "_timeline");
        ShotKit.Call(timeline, "AppendFrames", true, 3, 0);
        ShotKit.Call(Grid(m), "SelectRect", 0, 0, null, 1);
        ShotKit.Call(timeline, "LinkSelectedCels");
        ctx.SetCurrent(0, 0);
        ctx.Primary = new Color32(235, 60, 60);
        k.Settle(400);
        var canvas = ShotKit.Get<Control>(m, "_canvas");
        Point P(double x, double y) => canvas.TranslatePoint((Point)ShotKit.Call(canvas, "ImageToScreen", x, y)!, m)!.Value;
        var image = Fit(new Rect(P(0, 0), P(proj.Width, proj.Height)).Inflate(12), m);
        var c0 = Cel(m, 0, 0);
        var c2 = Cel(m, 0, 2);
        var cels = Fit(new Rect(c0.X - 30, c0.Y - 46, c2.Right - c0.X + 60, c0.Height + 46 + 16), m);
        using var rec = new ClipRecorder(k, m, image.Union(cels), frameMs: 40) { Regions = [image, cels] };
        var line = Enumerable.Range(0, 9).Select(i => P(8.5 + i * 4, 52.5 + (i % 2 == 0 ? 0 : 3))).ToList();
        rec.Place(line[0] + new Vector(-20, 30));
        rec.Hold(600);
        rec.Move(line[0], 400);
        rec.Down();
        rec.Trace(line, 1400);
        rec.Up();
        rec.Hold(900);
        if (!proj.AreCelsLinked(0, 0, 1)) throw new InvalidOperationException("cels not linked");
        // 칸을 누르는 모습에 맞춰 지금 프레임을 바꾼다(2프레임 = 링크됨 → 같은 줄, 3프레임 = 링크 안 됨 → 없음).
        void Show(int f, int afterMs)
        {
            rec.Click(Cel(m, 0, f).Center, 650, pressMs: 100, afterMs: 0);
            ctx.SetCurrent(0, f);
            rec.Hold(afterMs);
        }
        Show(1, 1300);
        Show(2, 1300);
        Show(0, 1000);
        Save(rec, "frames-linked");
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

    /// <summary>
    /// 3D 모델 원본 색 바꾸기(튜토리얼 '3D를 픽셀 아트로' 4단계 <c>model.colors</c>의 큰 움짤): 원본 모델 색 첫 칸을 눌러 쓰인 곳을 보이고 → 우클릭 → '색 바꾸기…'
    /// → 색 고르기 창에서 색상 막대를 끌면 두 칸이 따라 바뀜 → 확인 → 칸을 다시 눌러 표시를 끄고 바뀐 모델을 보임. 색 고르기 창은 미리보기 오른쪽에 둔다.
    /// </summary>
    [Shot("clip-tutorial-model-colors")]
    public static void ModelColors(ShotKit k)
    {
        var m = ScreenDemo.Open(k, width: 1280, height: 900);
        k.Settle(400);
        var path = ModelSample.Chick(k.WorkDir);
        var scene = Chickxel.Core.Scene3D.ModelLoader.Load(path);
        k.Dialog(() => new ModelImportDialog(path, scene).ShowDialog(m), w =>
        {
            ShotKit.Pump(1500);
            // 대화상자는 메인 창 가운데에 그려진다(ClipRecorder.DialogOrigin과 같은 셈).
            var size = w.ClientSize;
            var origin = new Point(Math.Round((m.ClientSize.Width - size.Width) / 2),
                Math.Round(Math.Max(0, (m.ClientSize.Height - size.Height - ShotKit.TitleHeight) / 2)) + ShotKit.TitleHeight);
            Rect At(Control c) => ShotKit.Bounds(c, w).Translate((Vector)origin);
            var pb = At(ShotKit.Get<Control>(w, "_preview"));
            var swatches = ShotKit.Get<Control>(w, "_sourceSwatches");
            var sb = At(swatches);
            // 미리보기 두 칸 + 색 줄 + 오른쪽에 색 고르기 창 자리.
            const double PickerW = 500, PickerH = 600;
            var crop = Fit(new Rect(pb.X - 8, pb.Y - 24, pb.Width + 16 + PickerW + 8, Math.Max(sb.Bottom + 16 - (pb.Y - 24), PickerH + 16)), m);
            using var rec = new ClipRecorder(k, m, crop, frameMs: 100);
            // 첫 색 칸(가장 많이 쓴 색 — 병아리 몸) 가운데(ColorSwatches: 14px 칸, 2px 간격).
            var cell = new Point(sb.X + 2 + 7, sb.Y + 2 + 7);
            rec.Place(new Point(cell.X + 90, cell.Y + 40));
            rec.Hold(700);
            rec.Click(cell, 600, pressMs: 100, afterMs: 1100);
            rec.Click(cell, 300, MouseButton.Right, pressMs: 100, afterMs: 600);
            var item = ShotKit.OpenPopups.Where(p => p.Child is not null).SelectMany(p => ShotKit.All<MenuItem>(p.Child!))
                .First(i => i.IsEffectivelyVisible && (i.Header as string ?? (i.Header as TextBlock)?.Text) == UiStrings.Model_SourceColorChange);
            var itemAt = rec.At(item);
            rec.Move(itemAt, 500);
            rec.Hold(300);
            k.Dialog(() => rec.PressAndRelease(itemAt, 0, pressMs: 100), picker =>
            {
                Console.WriteLine($"  color picker {picker.ClientSize}");
                // 색 고르기 창을 미리보기 오른쪽에 그리고 누른다.
                ShotKit.Get<Dictionary<Window, Point>>(rec, "_dialogs")[picker] = new Point(pb.Right + 16, crop.Y + 8);
                rec.Hold(600);
                var editor = ShotKit.Get<Control>(ShotKit.Get<Control>(picker, "_editor"), "_editor");
                var hue = ShotKit.Get<Control>(editor, "_hue");
                if (hue.IsEffectivelyVisible)
                {
                    var hb = rec.BoundsOf(hue);
                    rec.Drag(new Point(hb.Center.X, hb.Y + hb.Height * 0.15), new Point(hb.Center.X, hb.Y + hb.Height * 0.55), 2000);
                    rec.Hold(500);
                    rec.Drag(new Point(hb.Center.X, hb.Y + hb.Height * 0.55), new Point(hb.Center.X, hb.Y + hb.Height * 0.8), 1200);
                }
                else
                {
                    // 다른 고르기 모양이면 색을 차례로 고른다.
                    foreach (var h in new[] { 60f, 120f, 180f, 240f, 300f })
                    {
                        ShotKit.Call(editor, "Pick", Color32.FromHsv(h, 0.7f, 0.9f));
                        rec.Hold(400);
                    }
                }
                rec.Hold(900);
                rec.Click(rec.At(ShotKit.Get<Control>(picker, "OkButton")), 700, pressMs: 100, afterMs: 0);
            }, settleMs: 300);
            rec.Hold(700);
            // 칸을 다시 눌러 쓰인 곳 표시를 끄고 바뀐 모델을 본다.
            rec.Click(cell, 600, pressMs: 100, afterMs: 1800);
            if (ShotKit.Get<System.Collections.ICollection>(w, "_colorMap").Count == 0) throw new InvalidOperationException("model color not changed");
            rec.Save(Out + "model-colors", ClipFormat.WebP, webpLossless: false, webpQuality: 75, webpWidth: 860);
        }, settleMs: 1200);
    }

    // ───────────── 레이어 묶기와 스킨 ─────────────

    /// <summary>
    /// Shift 끌어 놓기로 자식 만들기(튜토리얼 '레이어 묶기와 스킨' <c>skins.child</c>): Shadow 레이어 이름을 끌다가 Shift를 누른 채 Body 행 위에 놓으면
    /// Shadow가 Body의 자식이 된다(들여쓰기). 타임라인 레이어 이름 칸 둘레만 자른다.
    /// </summary>
    [Shot("clip-tutorial-skins-shift-drag")]
    public static void SkinsShiftDrag(ShotKit k)
    {
        App.Settings.BottomHeight = 300;
        var m = ScreenDemo.Open(k, width: 1280, height: 800);
        ScreenDemo.Build(k, m, group: false);
        k.Settle(500);
        var proj = ScreenDemo.Project(m);
        int body = Body(m), shadow = proj.IndexOf(proj.Layers.First(l => l.Name == "Shadow"));
        Rect Name(int layer)
        {
            var g = Grid(m);
            var r = (Rect)ShotKit.Call(g, "LayerNameRect", layer)!;
            return new Rect(g.TranslatePoint(r.Position, m)!.Value, r.Size);
        }
        var a = Name(shadow);
        var b = Name(body);
        var top = Math.Min(a.Y, b.Y);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        var crop = Fit(new Rect(a.X, top - 60, a.Width + 210, bottom - top + 110), m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        // 눈·자물쇠 단추(x 0~48)를 피해 이름 글자 쪽을 잡는다.
        var from = new Point(a.X + a.Width * 0.6, a.Center.Y);
        var to = new Point(b.X + a.Width * 0.6 + 10, b.Center.Y);
        rec.Place(from + new Vector(60, 30));
        rec.Hold(600);
        rec.Move(from, 500);
        rec.Hold(200);
        // 고른 이름을 눌러 끌어야 순서 바꾸기(안 고른 이름을 끌면 사각형 고르기): 먼저 한 번 눌러 고르고,
        // 두 번 누르기(이름 바꾸기)로 잡히지 않게 조금 떨어진 자리를 다시 누른다.
        rec.Click(from, 0, afterMs: 600);
        from += new Vector(-30, 0);
        rec.Move(from, 300);
        rec.Down();
        rec.Move(from + new Vector(10, (to.Y - from.Y) / 3), 300);
        rec.KeyDown(Key.LeftShift);
        rec.Modifiers = RawInputModifiers.Shift;
        rec.Hold(200);
        rec.Move(to, 900);
        rec.Hold(500);
        rec.Up();
        rec.Modifiers = RawInputModifiers.None;
        rec.KeyUp(Key.LeftShift);
        // 이름 칸 위에 머물면 그 행이 흐려지고 아래 '새 레이어' 줄에선 풍선이 떠서, 잘라 낸 칸 밖 오른쪽으로 비킨다.
        rec.Move(rec.Pointer + new Vector(360, -40), 500);
        rec.Hold(1600);
        if (ScreenDemo.Project(m).Layers.First(l => l.Name == "Shadow").Parent?.Name != "Body") throw new InvalidOperationException("Shadow not nested under Body");
        Save(rec, "skins-shift-drag");
    }

    /// <summary>
    /// 스킨 갈아 끼우기(튜토리얼 '레이어 묶기와 스킨' <c>skins.switch</c>): 늑대 레이어에 옷 색을 바꾼 스킨 둘(Red · Green)을 더해 두고,
    /// 이름 줄의 스킨 칩을 눌러 목록에서 Red → Green → 처음 스킨으로 고르면 캔버스 그림이 통째로 바뀐다. 캔버스와 이름 줄(+ 펼친 목록) 두 곳만 잘라 붙인다.
    /// </summary>
    [Shot("clip-tutorial-skins-switch")]
    public static void SkinsSwitch(ShotKit k)
    {
        var path = Path.Combine(k.WorkDir, "wolf.png");
        File.Copy(ShotKit.SamplePng, path, overwrite: true);
        App.Settings.ShowPivot = false;
        var m = k.Main(path);
        k.Settle(400);
        var proj = ScreenDemo.Project(m);
        var wolf = proj.Layers[0];
        var first = wolf.ActiveSkin!;
        var original = proj.GetCel(0, 0)!;
        proj.AddSkin(wolf, "Red", first);
        proj.SetCel(0, 0, TintClothes(original, 2.4, 0.5, 0.5));
        proj.AddSkin(wolf, "Green", first);
        proj.SetCel(0, 0, TintClothes(original, 0.5, 2.2, 0.7));
        proj.SetActiveSkin(wolf, first);
        k.Settle(400);
        var canvas = ShotKit.Get<Control>(m, "_canvas");
        Point P(double x, double y) => canvas.TranslatePoint((Point)ShotKit.Call(canvas, "ImageToScreen", x, y)!, m)!.Value;
        var image = Fit(new Rect(P(0, 0), P(proj.Width, proj.Height)).Inflate(14), m);
        var g = Grid(m);
        var nameRow = (Rect)ShotKit.Call(g, "LayerNameRect", 0)!;
        var (chipX, chipW) = ((int, int))ShotKit.Call(g, "SkinChip", wolf)!;
        var chip = new Rect(g.TranslatePoint(new Point(nameRow.X + chipX, nameRow.Y), m)!.Value, new Size(chipW, nameRow.Height));
        var row = new Rect(g.TranslatePoint(nameRow.Position, m)!.Value, nameRow.Size);
        // 칩 아래로 펼쳐지는 스킨 목록까지.
        var rowArea = Fit(new Rect(row.X, row.Y - 30, row.Width + 140, row.Height + 30 + 150), m);
        using var rec = new ClipRecorder(k, m, image.Union(rowArea), frameMs: 40) { Regions = [image, rowArea] };
        rec.Cursor = ClipCursor.Arrow;
        // 쉬는 자리: 레이어 이름 글자 위(아래 '새 레이어' 줄·칸 쪽 '복사'·위 캔버스 줄은 타임라인이 직접 풍선을 띄운다).
        var rest = chip.Center + new Vector(-130, 4);
        rec.Place(rest);
        rec.Hold(700);
        // 첫 스킨은 목록에서 "이름 (기본)"처럼 덧붙여 보인다.
        void Pick(Skin skin)
        {
            string header = ReferenceEquals(skin, wolf.Skins[0])
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_TimelinePanel_SkinMenuItems_38CB630C12, skin.Name) : skin.Name;
            rec.Click(chip.Center, 600, pressMs: 100, afterMs: 500);
            var item = ShotKit.OpenPopups.Where(pop => pop.Child is not null).SelectMany(pop => ShotKit.All<MenuItem>(pop.Child!))
                .First(i => i.IsEffectivelyVisible && (i.Header as string ?? (i.Header as TextBlock)?.Text) == header);
            var at = rec.At(item);
            rec.Move(at, 450);
            rec.Hold(200);
            rec.Click(at, 0, pressMs: 100, afterMs: 200);
            // 목록 자리(타임라인 칸 단추 위)에 머물면 단추 풍선이 떠서 쉬는 자리로 비킨다.
            rec.Move(rest, 400);
            rec.Hold(900);
            if (!ReferenceEquals(wolf.ActiveSkin, skin)) throw new InvalidOperationException($"skin {skin.Name} not picked");
        }
        Pick(wolf.Skins[1]);
        Pick(wolf.Skins[2]);
        Pick(first);
        rec.Hold(400);
        Save(rec, "skins-switch");
    }

    /// <summary>
    /// 예시 늑대의 옷(머리 아래 40줄부터)에서 어두운 남색 픽셀만 밝기를 살려 (<paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>) 쪽으로 물들인 사본
    /// — 옷 색이 확 다른 스킨용(머리카락 그림자 선은 그대로).
    /// </summary>
    private static PixelImage TintClothes(PixelImage original, double r, double g, double b)
    {
        var img = original.Clone();
        for (int i = img.Width * 40; i < img.Pixels.Length; i++)
        {
            var c = img.Pixels[i];
            double lum = 0.3 * c.R + 0.59 * c.G + 0.11 * c.B;
            if (c.IsTransparent || c.B <= c.R + 15 || lum >= 110) continue;
            byte Ch(double k) => (byte)Math.Clamp(30 + lum * k, 0, 255);
            img.Pixels[i] = new Color32(Ch(r), Ch(g), Ch(b), c.A);
        }
        return img;
    }

    /// <summary>늑대 크기의 그림 가운데 아래쪽에 (<paramref name="fill"/>, 어두운 테두리) 둥근 방패.</summary>
    private static PixelImage Shield(int w, int h, Color32 fill)
    {
        var img = new PixelImage(w, h);
        var rim = new Color32((byte)(fill.R / 2), (byte)(fill.G / 2), (byte)(fill.B / 2));
        double cx = 24, cy = 62, rx = 13, ry = 15;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double d = (x + 0.5 - cx) * (x + 0.5 - cx) / (rx * rx) + (y + 0.5 - cy) * (y + 0.5 - cy) / (ry * ry);
                if (d <= 1) img.Pixels[y * w + x] = d > 0.72 ? rim : fill;
            }
        return img;
    }

    /// <summary>예시 늑대(Wolf, 아래) 위에 방패 레이어(Shield, 위)를 겹친 메인 창 — 겹쳐 쌓기 움짤용.</summary>
    private static (MainForm M, SpriteProject P, Layer Wolf, Layer Shield) StackMain(ShotKit k)
    {
        var path = Path.Combine(k.WorkDir, "wolf.png");
        File.Copy(ShotKit.SamplePng, path, overwrite: true);
        App.Settings.ShowPivot = false;
        var m = k.Main(path);
        k.Settle(400);
        var proj = ScreenDemo.Project(m);
        var wolf = proj.Layers[0];
        proj.SetLayerProperties(0, name: "Wolf");
        var shield = proj.AddLayer(null, "Shield");
        proj.SetCel(proj.IndexOf(shield), 0, Shield(proj.Width, proj.Height, new Color32(240, 150, 40)));
        ScreenDemo.Ctx(m).SetCurrent(proj.IndexOf(shield), 0);
        k.Settle(400);
        return (m, proj, wolf, shield);
    }

    /// <summary>캔버스 그림 둘레(메인 창 좌표).</summary>
    private static Rect CanvasImageArea(MainForm m, SpriteProject proj)
    {
        var canvas = ShotKit.Get<Control>(m, "_canvas");
        Point P(double x, double y) => canvas.TranslatePoint((Point)ShotKit.Call(canvas, "ImageToScreen", x, y)!, m)!.Value;
        return Fit(new Rect(P(0, 0), P(proj.Width, proj.Height)).Inflate(14), m);
    }

    /// <summary>레이어 이름 칸(메인 창 좌표).</summary>
    private static Rect LayerName(MainForm m, int layer)
    {
        var g = Grid(m);
        var r = (Rect)ShotKit.Call(g, "LayerNameRect", layer)!;
        return new Rect(g.TranslatePoint(r.Position, m)!.Value, r.Size);
    }

    /// <summary>
    /// 겹쳐 쌓기 순서(튜토리얼 '레이어 묶기와 스킨' <c>skins.stack</c>): 방패(위)가 늑대를 가리다가, Wolf 이름을 Shield 위로 끌어 올리면 늑대가 방패를 가린다.
    /// 캔버스와 레이어 이름 칸 두 곳만 잘라 붙인다.
    /// </summary>
    [Shot("clip-tutorial-skins-stack-order")]
    public static void SkinsStackOrder(ShotKit k)
    {
        var (m, proj, wolf, shield) = StackMain(k);
        var image = CanvasImageArea(m, proj);
        var w = LayerName(m, proj.IndexOf(wolf));
        var s = LayerName(m, proj.IndexOf(shield));
        var rows = Fit(new Rect(s.X, s.Y - 30, s.Width, w.Bottom - s.Y + 70), m);
        using var rec = new ClipRecorder(k, m, image.Union(rows), frameMs: 40) { Regions = [image, rows] };
        rec.Cursor = ClipCursor.Arrow;
        // 눈·자물쇠 단추(x 0~48)를 피해 이름 글자 쪽을 잡는다.
        var from = new Point(w.X + w.Width * 0.6, w.Center.Y);
        rec.Place(from + new Vector(40, 50));
        rec.Hold(900);
        rec.Move(from, 500);
        // 고른 이름을 눌러 끌어야 순서 바꾸기: 먼저 눌러 고르고, 두 번 누르기(이름 바꾸기)가 되지 않게 조금 떨어진 자리를 다시 누른다.
        rec.Click(from, 0, afterMs: 500);
        from += new Vector(-30, 0);
        rec.Move(from, 300);
        rec.Down();
        rec.Move(new Point(from.X + 6, s.Y + 4), 900);
        rec.Hold(400);
        rec.Up();
        // 이름 칸 위에 머물면 그 행이 흐려지고 아래 '새 레이어' 줄에선 풍선이 떠서, 잘라 낸 칸 밖 오른쪽으로 비킨다.
        rec.Move(rec.Pointer + new Vector(360, 0), 500);
        rec.Hold(1800);
        if (proj.IndexOf(wolf) < proj.IndexOf(shield)) throw new InvalidOperationException("wolf not moved above shield");
        Save(rec, "skins-stack-order");
    }

    /// <summary>
    /// 스킨 프리셋(튜토리얼 '레이어 묶기와 스킨' <c>skins.stack</c>): 늑대(기본 · Red 옷)와 방패(주황 · 파랑) 레이어의 스킨 조합을 프리셋 A·B로 저장해 두고,
    /// 미리보기 위 스킨 줄의 프리셋 상자에서 B → A를 고르면 두 레이어가 한꺼번에 바뀐다. 캔버스와 프리셋 상자(+ 펼친 목록) 두 곳만 잘라 붙인다.
    /// </summary>
    [Shot("clip-tutorial-skins-stack-preset")]
    public static void SkinsStackPreset(ShotKit k)
    {
        var (m, proj, wolf, shield) = StackMain(k);
        var original = proj.GetCel(proj.IndexOf(wolf), 0)!;
        var wolfBase = wolf.ActiveSkin!;
        var shieldBase = shield.ActiveSkin!;
        var wolfRed = proj.AddSkin(wolf, "Red", wolfBase);
        proj.SetCel(proj.IndexOf(wolf), 0, TintClothes(original, 2.4, 0.5, 0.5));
        var shieldBlue = proj.AddSkin(shield, "Blue", shieldBase);
        proj.SetCel(proj.IndexOf(shield), 0, Shield(proj.Width, proj.Height, new Color32(60, 120, 235)));
        proj.SaveSkinPreset("A", [new(wolf, wolfBase), new(shield, shieldBase)]);
        proj.SaveSkinPreset("B", [new(wolf, wolfRed), new(shield, shieldBlue)]);
        proj.ApplySkinPreset(proj.SkinPresets[0]);
        k.Settle(600);
        var combo = ShotKit.All<ComboBox>(ShotKit.Get<Control>(m, "_preview")).First(c => c.IsEffectivelyVisible && c.PlaceholderText == UiStrings.Preview_PresetPlaceholder);
        var box = ShotKit.Bounds(combo, m);
        var image = CanvasImageArea(m, proj);
        // 'Skins' 글자부터 오른쪽 레이어별 스킨 상자(Shield)까지, 아래로 펼쳐지는 목록까지.
        var boxArea = Fit(new Rect(box.X - 46, box.Y - 16, box.Width + 46 + 120, box.Height + 16 + 90), m);
        using var rec = new ClipRecorder(k, m, image.Union(boxArea), frameMs: 40) { Regions = [image, boxArea] };
        rec.Cursor = ClipCursor.Arrow;
        var rest = new Point(box.X - 40, box.Bottom + 50);
        rec.Place(rest);
        rec.Hold(900);
        void Pick(string name)
        {
            rec.Click(box.Center, 600, pressMs: 100, afterMs: 500);
            var item = ShotKit.OpenPopups.Where(pop => pop.Child is not null).SelectMany(pop => ShotKit.All<ComboBoxItem>(pop.Child!))
                .First(i => i.IsEffectivelyVisible && (i.Content as string) == name);
            var at = rec.At(item);
            rec.Move(at, 400);
            rec.Hold(200);
            rec.Click(at, 0, pressMs: 100, afterMs: 200);
            rec.Move(rest, 400);
            rec.Hold(1300);
            if (!ReferenceEquals(wolf.ActiveSkin, name == "B" ? wolfRed : wolfBase) || !ReferenceEquals(shield.ActiveSkin, name == "B" ? shieldBlue : shieldBase))
                throw new InvalidOperationException($"preset {name} not applied");
        }
        Pick("B");
        Pick("A");
        Save(rec, "skins-stack-preset");
    }

    // ───────────── 캔버스 다루기 ─────────────

    /// <summary>예시 늑대(49×98)를 3배로 연 메인 창과 그림 둘레를 넉넉히 둔 자르기 칸(오른쪽으로 <paramref name="right"/>px 더).</summary>
    private static (MainForm M, Rect Crop) WolfCanvas(ShotKit k, double right = 60)
    {
        var path = Path.Combine(k.WorkDir, "wolf.png");
        File.Copy(ShotKit.SamplePng, path, overwrite: true);
        App.Settings.ShowPivot = true;
        var m = k.Main(path);
        ToolFileHelp.Canvas(m).SetZoom(3);
        k.Settle(400);
        var a = ToolFileHelp.At(m, 0, 0);
        var b = ToolFileHelp.At(m, 49, 98);
        // 위는 Ctrl 끌기로 그림이 올라가는 만큼 넉넉히.
        return (m, Fit(new Rect(a, b).Inflate(new Thickness(40, 48, 40 + right, 24)), m));
    }

    /// <summary>
    /// Ctrl 끌기 비교(튜토리얼 '캔버스 다루기' <c>canvas.ctrl</c>): <paramref name="button"/> 왼쪽 = 그림과 피벗이 같이 옮겨짐, 오른쪽 = 피벗은 제자리에 둔 채 그림만.
    /// </summary>
    private static void CtrlDrag(ShotKit k, MouseButton button, string name)
    {
        var (m, crop) = WolfCanvas(k);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        // 피벗(가슴께)을 커서가 가리지 않게 다리 쪽을 잡는다.
        var from = ToolFileHelp.At(m, 24, 80);
        rec.Place(from + new Vector(30, 40));
        rec.Hold(600);
        rec.Move(from, 500);
        rec.KeyDown(Key.LeftCtrl);
        rec.Modifiers = RawInputModifiers.Control;
        rec.Hold(300);
        rec.Down(button);
        // 오른쪽 위로: 옮겨진 피벗이 제자리 캔버스 피벗의 이름표(오른쪽 아래에 붙음) 밑으로 숨지 않게.
        rec.Move(from + new Vector(14 * 3, -8 * 3), 1100);
        rec.Hold(300);
        rec.Up();
        rec.Modifiers = RawInputModifiers.None;
        rec.KeyUp(Key.LeftCtrl);
        rec.Move(rec.Pointer + new Vector(30, 20), 400);
        rec.Hold(1500);
        if (button == MouseButton.Left == (ToolFileHelp.Ctx(m).PivotPixel.X == 24.5f)) throw new InvalidOperationException($"pivot {ToolFileHelp.Ctx(m).PivotPixel}");
        Save(rec, name);
    }

    [Shot("clip-tutorial-canvas-ctrl-left")]
    public static void CanvasCtrlLeft(ShotKit k) => CtrlDrag(k, MouseButton.Left, "canvas-ctrl-left");

    [Shot("clip-tutorial-canvas-ctrl-right")]
    public static void CanvasCtrlRight(ShotKit k) => CtrlDrag(k, MouseButton.Right, "canvas-ctrl-right");

    /// <summary>
    /// 레이어 전체 변형(튜토리얼 '캔버스 다루기' <c>canvas.layer.edit</c>): 늑대 4프레임에서 Alt+Shift+C → 오른쪽 아래 손잡이를 안으로 끌어 줄이고 안쪽을 끌어 옮긴 뒤
    /// Enter로 적용하면 타임라인 네 칸 그림이 한꺼번에 바뀐다. 2~4프레임 칸을 눌러 보며 모두 같이 바뀐 것을 보인다. 캔버스와 타임라인 칸 두 곳만 잘라 붙인다.
    /// </summary>
    [Shot("clip-tutorial-canvas-layer-frames")]
    public static void CanvasLayerFrames(ShotKit k)
    {
        var path = Path.Combine(k.WorkDir, "wolf.png");
        File.Copy(ShotKit.SamplePng, path, overwrite: true);
        App.Settings.ShowPivot = false;
        var m = k.Main(path);
        k.Settle(400);
        var ctx = ScreenDemo.Ctx(m);
        var proj = ScreenDemo.Project(m);
        ShotKit.Call(ShotKit.Get<Control>(m, "_timeline"), "AppendFrames", true, 3, 0);
        ctx.SetCurrent(0, 0);
        k.Settle(400);
        var canvas = ShotKit.Get<Control>(m, "_canvas");
        Point P(double x, double y) => canvas.TranslatePoint((Point)ShotKit.Call(canvas, "ImageToScreen", x, y)!, m)!.Value;
        var image = Fit(new Rect(P(0, 0), P(proj.Width, proj.Height)).Inflate(14), m);
        var c0 = Cel(m, 0, 0);
        // 새로 붙인 칸은 폭이 늘어나는 중이라(처음엔 0) 첫 칸 폭으로 네 칸 자리를 센다.
        var cels = Fit(new Rect(c0.X - 30, c0.Y - 46, c0.Width * 4 + 60, c0.Height + 46 + 16), m);
        using var rec = new ClipRecorder(k, m, image.Union(cels), frameMs: 40) { Regions = [image, cels] };
        rec.Cursor = ClipCursor.Arrow;
        rec.Place(P(proj.Width, proj.Height) + new Vector(10, -30));
        rec.Hold(500);
        k.Command("편집/레이어 크기…");
        k.Settle(200);
        if (!ctx.IsLayerTransform) throw new InvalidOperationException("layer transform not started");
        rec.Hold(700);
        (double X, double Y)[] Corners() => ctx.TransformBox!.Value.Corners(ctx.TransformParams);
        // 오른쪽 아래 손잡이를 안쪽으로 = 줄이기.
        var corner = P(Corners()[2].X, Corners()[2].Y);
        rec.Drag(corner, corner + new Vector(-16 * ZoomOf(canvas), -32 * ZoomOf(canvas)), 1000);
        rec.Hold(400);
        // 안쪽(가운데 피벗 손잡이를 피해 아래쪽)을 끌어 옮기기.
        var c = Corners();
        var inside = P((c[0].X + c[2].X) / 2, c[0].Y + (c[2].Y - c[0].Y) * 0.75);
        rec.Drag(inside, inside + new Vector(8 * ZoomOf(canvas), 18 * ZoomOf(canvas)), 900);
        rec.Hold(500);
        rec.Key(Key.Enter, afterMs: 600);
        if (ctx.IsLayerTransform) throw new InvalidOperationException("layer transform not applied");
        // 칸을 누르는 모습에 맞춰 지금 프레임을 바꾼다(모든 프레임이 같이 줄고 옮겨짐).
        foreach (var f in new[] { 1, 2, 3, 0 })
        {
            rec.Click(Cel(m, 0, f).Center, 550, pressMs: 100, afterMs: 0);
            ctx.SetCurrent(0, f);
            rec.Hold(f == 0 ? 1400 : 700);
        }
        Save(rec, "canvas-layer-frames");
    }

    private static double ZoomOf(Control canvas) => ((PixelCanvas)canvas).Zoom;

    /// <summary>
    /// 캔버스 크기 손잡이 끌기 비교(튜토리얼 '캔버스 다루기' <c>canvas.resize.edit</c>): 오른쪽 손잡이를 <paramref name="button"/> 왼쪽으로 끌면 그림도 함께 늘고,
    /// 오른쪽으로 끌면 캔버스만 넓어져 여백이 생긴다. 적용하지 않고 끝.
    /// </summary>
    private static void ResizeDrag(ShotKit k, MouseButton button, string name)
    {
        var (m, crop) = WolfCanvas(k, right: 80);
        var ctx = ToolFileHelp.Ctx(m);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 40);
        k.Command("편집/캔버스 크기…");
        k.Settle(200);
        var cv = ctx.CanvasResizeCanvas;
        var handle = ToolFileHelp.At(m, cv.Right, cv.Y + cv.Height / 2.0);
        rec.Place(handle + new Vector(30, 50));
        rec.Hold(700);
        rec.Move(handle, 500);
        rec.Hold(200);
        rec.Down(button);
        rec.Move(handle + new Vector(20 * 3, 0), 1100);
        rec.Hold(300);
        rec.Up();
        rec.Move(rec.Pointer + new Vector(20, 40), 400);
        rec.Hold(1600);
        ctx.CancelCanvasResize();
        Save(rec, name);
    }

    [Shot("clip-tutorial-canvas-resize-left")]
    public static void CanvasResizeLeft(ShotKit k) => ResizeDrag(k, MouseButton.Left, "canvas-resize-left");

    [Shot("clip-tutorial-canvas-resize-right")]
    public static void CanvasResizeRight(ShotKit k) => ResizeDrag(k, MouseButton.Right, "canvas-resize-right");

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
