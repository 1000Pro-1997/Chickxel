using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Chickxel.App;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace GuideShots;

/// <summary>
/// 움짤·기능 영상(<see cref="ClipRecorder"/>): 실제 앱에 마우스·키를 보내며 찍는다. 샷 id는 <c>clip-</c> + 결과 파일 이름
/// (예: <c>clip-tools-pencil</c> → <c>docs/assets/guide/tools-pencil.webp</c>).
/// </summary>
internal static class ClipShots
{
    // ───────────── 도구 장 캔버스 움짤(378×185, 12배) ─────────────

    private const int Zoom = 12;
    private static readonly Color32 Green = new(46, 160, 96);
    private static readonly Color32 Purple = new(112, 96, 222);

    /// <summary>
    /// <paramref name="art"/>를 PNG로 열어 12배로 키운 메인 창과, 그림 가운데 <paramref name="w"/>×<paramref name="h"/>를 찍는 녹화기(십자 커서 — 앱의 그리기 도구 커서).
    /// </summary>
    private static (MainForm M, ClipRecorder Rec) CanvasClip(ShotKit k, PixelImage art, int w = 378, int h = 185)
    {
        var path = Path.Combine(k.WorkDir, "clip.png");
        PngIO.Save(art, path);
        // 그림만 보이게 피벗 표시는 끈다(도구 설명 움짤).
        App.Settings.ShowPivot = false;
        var m = k.Main(path);
        ToolFileHelp.Canvas(m).SetZoom(Zoom);
        k.Settle(300);
        var s = ToolFileHelp.SpriteRect(m);
        var crop = new Rect(Math.Round(s.Center.X - w / 2.0), Math.Round(s.Center.Y - h / 2.0), w, h);
        return (m, new ClipRecorder(k, m, crop, frameMs: 40) { Cursor = ClipCursor.Cross });
    }

    /// <summary>그림 픽셀 (<paramref name="x"/>, <paramref name="y"/>) 가운데의 창 좌표(소수 = 픽셀 안 자리).</summary>
    private static Point P(MainForm m, double x, double y) => ToolFileHelp.At(m, x + 0.5, y + 0.5);

    /// <summary><paramref name="x0"/>~<paramref name="x1"/>를 지나는 물결(가운데 <paramref name="y"/>, 높이 <paramref name="amp"/>, <paramref name="waves"/>번 출렁임).</summary>
    private static List<Point> Wave(MainForm m, double x0, double x1, double y, double amp, double waves, double phase = 0)
    {
        var list = new List<Point>();
        for (int i = 0; i <= 60; i++)
        {
            double t = i / 60.0;
            list.Add(P(m, x0 + (x1 - x0) * t, y - amp * Math.Sin(phase + t * waves * 2 * Math.PI)));
        }
        return list;
    }

    private static PixelImage Fill(int w, int h, Func<int, int, Color32> color)
    {
        var img = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img.Pixels[y * w + x] = color(x, y);
        return img;
    }

    /// <summary>연필: 왼쪽 버튼으로 기본색(초록), 오른쪽 버튼으로 보조색(보라) 물결 — 기본 픽셀 잇기(ㄱ자)라 계단이 변으로 붙는다.</summary>
    [Shot("clip-tools-pencil")]
    public static void Pencil(ShotKit k)
    {
        var (m, rec) = CanvasClip(k, new PixelImage(32, 16));
        using (rec)
        {
            var ctx = ToolFileHelp.Ctx(m);
            ctx.Primary = Green;
            ctx.Secondary = Purple;
            var top = Wave(m, 2, 28, 4.5, 2, 1.25, 0.6);
            rec.Place(top[0] + new Vector(-30, 24));
            rec.Hold(500);
            rec.Move(top[0], 450);
            rec.Down();
            rec.Trace(top, 1600);
            rec.Up();
            rec.Hold(350);
            var bottom = Wave(m, 4, 29, 11, 2, 1.25, 3.4);
            rec.Move(bottom[0], 500);
            rec.Down(MouseButton.Right);
            rec.Trace(bottom, 1600);
            rec.Up();
            rec.Hold(250);
            rec.Move(bottom[^1] + new Vector(14, 22), 400);
            rec.Hold(1500);
            rec.Save("docs/assets/guide/tools-pencil", ClipFormat.WebP);
        }
    }

    /// <summary>연필 직선: 짧게 그린 뒤 Shift를 누른 채(그어질 직선이 반투명하게 미리 보임) 클릭해 잇고, 마지막에는 클릭한 채 끌어 이어 그린다.</summary>
    [Shot("clip-tools-pencil-line")]
    public static void PencilLine(ShotKit k)
    {
        var (m, rec) = CanvasClip(k, new PixelImage(32, 16));
        using (rec)
        {
            var ctx = ToolFileHelp.Ctx(m);
            ctx.Primary = Green;
            rec.Place(P(m, 2, 5) + new Vector(-20, 30));
            rec.Hold(400);
            rec.Move(P(m, 2, 4), 400);
            rec.Down();
            rec.Trace([P(m, 3, 3), P(m, 5, 2), P(m, 7, 2.2), P(m, 9, 3)], 600);
            rec.Up();
            rec.Hold(400);
            rec.KeyDown(Key.LeftShift);
            rec.Modifiers = RawInputModifiers.Shift;
            foreach (var (x, y) in new[] { (28, 3), (28, 12), (8, 12) })
            {
                rec.Move(P(m, x, y), 650);
                rec.Hold(450);
                rec.Down();
                rec.Hold(150);
                rec.Up();
                rec.Hold(350);
            }
            // 마지막 Shift+클릭은 누른 채 끌어 직선 끝에서 자유롭게 이어 그린다.
            rec.Move(P(m, 12, 7), 600);
            rec.Hold(400);
            rec.Down();
            rec.Hold(120);
            rec.Modifiers = RawInputModifiers.None;
            rec.KeyUp(Key.LeftShift);
            rec.Trace([P(m, 14, 6), P(m, 16, 6.5), P(m, 18, 8), P(m, 20, 8.5), P(m, 22, 7.5)], 900);
            rec.Up();
            rec.Hold(250);
            rec.Move(P(m, 24, 10) + new Vector(10, 16), 400);
            rec.Hold(1500);
            rec.Save("docs/assets/guide/tools-pencil-line", ClipFormat.WebP);
        }
    }

    /// <summary>픽셀 퍼펙트: 같은 곡선을 끄고(위 — 기본 ㄱ자 잇기, 꺾이는 곳마다 겹 픽셀) 켜고(아래 — 대각선으로 한 픽셀 두께) 그린다.</summary>
    [Shot("clip-tools-pencil-perfect")]
    public static void PencilPerfect(ShotKit k)
    {
        var (m, rec) = CanvasClip(k, new PixelImage(32, 16));
        using (rec)
        {
            var ctx = ToolFileHelp.Ctx(m);
            ctx.Primary = Purple;
            ctx.PixelPerfect = false;
            var top = Wave(m, 2, 29, 4, 2.2, 1.5, 0.3);
            rec.Place(top[0] + new Vector(-20, 30));
            rec.Hold(500);
            rec.Move(top[0], 400);
            rec.Down();
            rec.Trace(top, 1700);
            rec.Up();
            rec.Hold(500);
            ctx.PixelPerfect = true;
            var bottom = Wave(m, 2, 29, 11.5, 2.2, 1.5, 0.3);
            rec.Move(bottom[0], 500);
            rec.Down();
            rec.Trace(bottom, 1700);
            rec.Up();
            rec.Hold(250);
            rec.Move(bottom[^1] + new Vector(10, 18), 400);
            rec.Hold(1600);
            rec.Save("docs/assets/guide/tools-pencil-perfect", ClipFormat.WebP);
        }
    }

    /// <summary>지우개: 회색 그라데이션을 원 브러시(3)로 문질러 지우고, Shift를 누른 채 클릭해 직선으로 지운다.</summary>
    [Shot("clip-tools-eraser")]
    public static void Eraser(ShotKit k)
    {
        App.Settings.EraserBrushSize = 3;
        App.Settings.EraserBrushShape = BrushShape.Circle;
        var gradient = Fill(32, 16, (x, _) => { byte v = (byte)Math.Round(x * 255 / 31.0); return new Color32(v, v, v); });
        var (m, rec) = CanvasClip(k, gradient);
        using (rec)
        {
            ToolFileHelp.Tool(k, "지우개");
            rec.Place(P(m, 2, 3) + new Vector(-20, 30));
            rec.Hold(500);
            rec.Move(P(m, 2, 3), 400);
            rec.Down();
            rec.Trace([P(m, 8, 3), P(m, 14, 2.6), P(m, 20, 3.4), P(m, 26, 3), P(m, 28, 5), P(m, 28.4, 8), P(m, 27, 11)], 1900);
            rec.Up();
            rec.Hold(400);
            rec.KeyDown(Key.LeftShift);
            rec.Modifiers = RawInputModifiers.Shift;
            foreach (var (x, y) in new[] { (18, 12), (6, 8) })
            {
                rec.Move(P(m, x, y), 650);
                rec.Hold(450);
                rec.Down();
                rec.Hold(150);
                rec.Up();
                rec.Hold(350);
            }
            rec.Modifiers = RawInputModifiers.None;
            rec.KeyUp(Key.LeftShift);
            rec.Move(P(m, 3, 12) + new Vector(0, 10), 400);
            rec.Hold(1500);
            rec.Save("docs/assets/guide/tools-eraser", ClipFormat.WebP);
        }
    }

    /// <summary>지우개 채널 R만: 빨강·초록·파랑 면을 문지르면 빨강은 검정이 되고 초록은 그대로, 파랑은 섞인 빨강만 빠진다(투명해지지 않음).</summary>
    [Shot("clip-tools-eraser-channels")]
    public static void EraserChannels(ShotKit k)
    {
        App.Settings.EraserBrushSize = 3;
        App.Settings.EraserBrushShape = BrushShape.Circle;
        App.Settings.EraserRed = true;
        App.Settings.EraserGreen = false;
        App.Settings.EraserBlue = false;
        var red = new Color32(230, 0, 0);
        var green = new Color32(0, 112, 0);
        var blue = new Color32(70, 40, 250);
        var art = Fill(32, 16, (x, y) => y < 5 ? green : x < 16 ? red : blue);
        var (m, rec) = CanvasClip(k, art, 376, 186);
        using (rec)
        {
            ToolFileHelp.Tool(k, "지우개");
            rec.Place(P(m, 4, 12) + new Vector(-20, 20));
            rec.Hold(500);
            rec.Move(P(m, 4, 12), 400);
            rec.Down();
            rec.Trace([P(m, 7, 9), P(m, 10, 6), P(m, 12, 2.5), P(m, 14, 6.5), P(m, 15, 10), P(m, 18, 12), P(m, 21, 9), P(m, 23, 3), P(m, 26, 7.5), P(m, 28, 11)], 2600);
            rec.Up();
            rec.Hold(300);
            rec.Move(P(m, 3, 7), 450);
            rec.Down();
            rec.Trace([P(m, 6, 13), P(m, 9, 9), P(m, 12, 13)], 1000);
            rec.Up();
            rec.Hold(250);
            rec.Move(P(m, 13, 14) + new Vector(12, 14), 400);
            rec.Hold(1600);
            rec.Save("docs/assets/guide/tools-eraser-channels", ClipFormat.WebP);
        }
    }

    // ───────────── 숫자 칸 ─────────────

    /// <summary>숫자 칸 우클릭 목록에서 지금 값이 아닌 예시 값 하나(<paramref name="label"/>로 시작하는 항목, 없으면 지금 값 다음 항목).</summary>
    private static MenuItem PresetItem(string? label)
    {
        var items = ShotKit.OpenPopups.Where(p => p.Child is not null)
            .SelectMany(p => ShotKit.All<MenuItem>(p.Child!))
            .Where(i => i.IsEffectivelyVisible && i.ToggleType == MenuItemToggleType.Radio).ToList();
        if (items.Count == 0) throw new InvalidOperationException("no preset list");
        static string Text(MenuItem i) => (i.Header as TextBlock)?.Text ?? i.Header?.ToString() ?? "";
        if (label is not null && items.FirstOrDefault(i => Text(i) == label || Text(i).StartsWith(label + " ", StringComparison.Ordinal)) is { } exact) return exact;
        int current = items.FindIndex(i => i.IsChecked);
        return items[(current + 1) % items.Count];
    }

    /// <summary>
    /// 숫자 칸 다루기: <paramref name="a"/>의 오른쪽 끝 위 단추 두 번·아래 단추 한 번(한 칸씩), 단추 위에서 위로 끌었다 내리기,
    /// 우클릭 목록에서 <paramref name="preset"/> 고르기, 그다음 <paramref name="b"/>의 숫자를 눌러 <paramref name="typed"/> 입력 + Enter.
    /// </summary>
    private static void NumberScene(ClipRecorder rec, NumericUpDown a, NumericUpDown b, string? preset, string typed)
    {
        var spin = rec.BoundsOf((Control)a.InnerRightContent!);
        var up = new Point(spin.Center.X, spin.Y + spin.Height * 0.27);
        var down = new Point(spin.Center.X, spin.Y + spin.Height * 0.75);
        rec.Cursor = ClipCursor.Arrow;
        rec.Hold(700);
        // 위 단추 두 번, 아래 단추 한 번.
        rec.Move(up, 650);
        rec.Cursor = ClipCursor.SizeNS;
        rec.Hold(400);
        rec.Click(up, 0, pressMs: 200, afterMs: 500);
        rec.Click(up, 0, pressMs: 200, afterMs: 500);
        rec.Move(down, 250);
        rec.Hold(300);
        rec.Click(down, 0, pressMs: 200, afterMs: 700);
        // 단추 위에서 위로 끌면 커지고 다시 내리면 작아진다(3px마다 한 칸).
        rec.Move(spin.Center, 200);
        rec.Down();
        rec.Hold(250);
        rec.Move(spin.Center - new Vector(0, 36), 1300, ease: false);
        rec.Hold(350);
        rec.Move(spin.Center - new Vector(0, 15), 800, ease: false);
        rec.Hold(300);
        rec.Up();
        rec.Hold(700);
        // 우클릭 목록에서 고르기.
        var field = rec.BoundsOf(a);
        var text = new Point(field.X + Math.Min(22, field.Width / 3), field.Center.Y);
        rec.Cursor = ClipCursor.Arrow;
        rec.Move(text, 550);
        rec.Cursor = ClipCursor.IBeam;
        rec.Hold(300);
        rec.Click(text, 0, MouseButton.Right, pressMs: 150, afterMs: 700);
        var item = rec.At(PresetItem(preset));
        rec.Cursor = ClipCursor.Arrow;
        rec.Move(item, 600);
        rec.Hold(400);
        rec.Click(item, 0, pressMs: 150, afterMs: 900);
        // 숫자를 눌러 입력(전체 선택된 채로 치면 바뀜) + Enter.
        var other = rec.BoundsOf(b);
        var typeAt = new Point(other.X + Math.Min(22, other.Width / 3), other.Center.Y);
        rec.Move(typeAt, 600);
        rec.Cursor = ClipCursor.IBeam;
        rec.Hold(300);
        rec.Click(typeAt, 0, pressMs: 150, afterMs: 700);
        rec.Type(typed, 220);
        rec.Hold(400);
        rec.Key(Key.Enter, afterMs: 900);
        rec.Cursor = ClipCursor.Arrow;
        rec.Move(typeAt + new Vector(70, 60), 500);
        rec.Hold(1500);
    }

    /// <summary>앱 도움말 '숫자 칸과 메뉴 다루기' 움짤(한국어 화면, 432×442): 도구 설정 · 연필의 크기 칸.</summary>
    [Shot("clip-screen-common-controls")]
    public static void CommonControls(ShotKit k)
    {
        ShotKit.UseLanguage(Chickxel.App.Localization.UiLanguage.Korean);
        App.Settings.RightWidth = 330;
        App.Settings.BrushSize = 4;
        var m = k.Main(ToolFileHelp.Sample(k));
        ToolFileHelp.Tool(k, "연필");
        var options = ToolFileHelp.Options(m);
        var size = ShotKit.Get<NumericUpDown>(options, "_sizeNum");
        var title = ShotKit.Bounds(ShotKit.Get<Control>(options, "_title"), m);
        var dock = ShotKit.Bounds(options, m);
        var crop = new Rect(dock.Right + 2 - 432, title.Y - 46, 432, 442);
        using var rec = new ClipRecorder(k, m, crop, frameMs: 50);
        rec.Place(new Point(crop.X + 300, crop.Y + 300));
        NumberScene(rec, size, size, "8", "3");
        rec.Save("docs/assets/guide/screen-common-controls", ClipFormat.WebP);
    }

    /// <summary>홈페이지 '숫자 칸' 움짤(영어 화면, 512×1031 — 창 오른쪽 세로 띠, 제목 줄 포함): 피벗 도구의 X·Y 칸.</summary>
    [Shot("clip-feature-number-field")]
    public static void NumberField(ShotKit k)
    {
        var m = ScreenDemo.Open(k, width: 1400, height: 1031 - ShotKit.TitleHeight);
        ScreenDemo.Build(k, m, group: false);
        ToolFileHelp.Tool(k, "피벗");
        var options = ToolFileHelp.Options(m);
        var x = ShotKit.Get<NumericUpDown>(options, "_pivotX");
        var y = ShotKit.Get<NumericUpDown>(options, "_pivotY");
        var crop = new Rect(m.ClientSize.Width - 512, 0, 512, m.ClientSize.Height);
        using var rec = new ClipRecorder(k, m, crop, titleBar: true, frameMs: 60) { TitleFilter = PrettyTitle(k) };
        rec.Place(new Point(crop.X + 380, ShotKit.Bounds(x, m).Y + 160));
        NumberScene(rec, x, y, null, "90");
        rec.Save("docs/assets/feature-number-field", ClipFormat.Gif);
    }

    /// <summary>창 제목의 샷 작업 폴더 경로를 그림에 쓰는 예시 경로(<see cref="ScreenDemo.ScrubPaths"/>와 같음)로.</summary>
    private static Func<string, string> PrettyTitle(ShotKit k)
    {
        var real = Path.Combine(k.WorkDir, ScreenDemo.FolderName);
        return t => t.Replace(real, @"D:\MyGame\Assets\" + ScreenDemo.FolderName, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────── 홈페이지 기능 영상(1280×688 = 1280×656 창 + 제목 줄, 영어 화면) ─────────────

    private static Control TimelineGrid(MainForm m) => ShotKit.Get<Control>(ShotKit.Get<Control>(m, "_timeline"), "_grid");

    /// <summary>타임라인 레이어 이름 칸(이름 글자 쪽)의 창 좌표.</summary>
    private static Point LayerName(MainForm m, string name)
    {
        var p = ScreenDemo.Project(m);
        int y = (int)ShotKit.Call(TimelineGrid(m), "LayerY", p.IndexOf(p.Layers.First(l => l.Name == name)))!;
        return TimelineGrid(m).TranslatePoint(new Point(110, y + 18), m)!.Value;
    }

    /// <summary>열린 메뉴에서 글자가 <paramref name="text"/>인 항목.</summary>
    private static MenuItem MenuItemNamed(string text)
        => ShotKit.OpenPopups.Where(p => p.Child is not null).SelectMany(p => ShotKit.All<MenuItem>(p.Child!))
               .FirstOrDefault(i => i.IsEffectivelyVisible && ((i.Header as TextBlock)?.Text ?? i.Header as string) == text)
           ?? throw new InvalidOperationException("no menu item " + text);

    /// <summary>
    /// 피벗 연동: 피벗 도구로 몸 레이어 피벗을 발밑으로 끌고, 다른 레이어(그림자)·그룹(부위마다 파란 점)을 골라 레이어마다 따로인 피벗을 보이고,
    /// 타임라인 캔버스 칩 글자로 캔버스 피벗(내보내기)을 고친 뒤, 몸 레이어를 캔버스 피벗 대표 레이어(이름 앞 왕관)로 정한다.
    /// </summary>
    [Shot("clip-feature-pivot")]
    public static void Pivot(ShotKit k)
    {
        // 낮은 창(1280×656)에서 그림이 크게 보이게: 그룹을 접어 타임라인을 줄이고 3배로.
        App.Settings.BottomHeight = 218;
        var m = ScreenDemo.OpenFolder(k, width: 1280, height: 656);
        ScreenDemo.Build(k, m);
        var ctx = ScreenDemo.Ctx(m);
        var p = ScreenDemo.Project(m);
        p.SetLayerCollapsed(p.IndexOf(p.Layers.First(l => l.Name == "Props")), true);
        k.Settle(200);
        ToolFileHelp.Canvas(m).SetZoom(3);
        var options = ToolFileHelp.Options(m);
        // 낮은 창이라 도구 막대가 스크롤된다: 피벗 칸(아래쪽 묶음)이 보이게 미리 내려 둔다.
        var slot = ShotKit.Get<List<ToolSlotButton>>(m, "_toolSlots").First(s => s.Contains(ToolKind.Pivot));
        slot.SetCurrent(ToolKind.Pivot);
        slot.BringIntoView();
        k.Settle(300);
        using var rec = new ClipRecorder(k, m, new Rect(0, 0, 1280, 656), titleBar: true, frameMs: 42) { TitleFilter = PrettyTitle(k) };
        rec.Place(new Point(700, 300));
        rec.Hold(600);

        // 피벗 도구(도구 막대 칸).
        rec.Click(rec.At(slot), 700, pressMs: 0, afterMs: 600);
        if (ctx.Tool != ToolKind.Pivot) throw new InvalidOperationException("pivot tool not selected: " + ctx.Tool);

        // 몸 레이어(지금 레이어)의 피벗을 발밑으로 끈다.
        var (bx, by) = ctx.Pivot.ToLocalPixel(p.Width, p.Height);
        rec.Move(ToolFileHelp.At(m, bx, by), 700);
        rec.Hold(300);
        rec.Down();
        rec.Hold(200);
        rec.Move(ToolFileHelp.At(m, p.Width / 2.0, p.Height - 3), 1000);
        rec.Hold(250);
        rec.Up();
        rec.Hold(1000);

        // 다른 레이어는 피벗이 따로: 그림자 → 5×5 단추로 아래 가운데, 그룹 → 부위마다 파란 점.
        rec.Click(LayerName(m, "Shadow"), 800, afterMs: 900);
        var presets = ShotKit.Get<Button[]>(options, "_presetButtons");
        rec.Click(rec.At(presets[4 * 5 + 2]), 800, afterMs: 1100);
        rec.Click(LayerName(m, "Props"), 800, afterMs: 1500);
        rec.Click(LayerName(m, "Body"), 700, afterMs: 900);

        // 캔버스 칩 글자 = 캔버스 피벗(시트·Unity 내보내기).
        var chip = TimelineGrid(m).TranslatePoint(new Point(60, 35), m)!.Value;
        rec.Cursor = ClipCursor.Arrow;
        rec.Move(chip, 800);
        rec.Cursor = ClipCursor.Hand;
        rec.Hold(300);
        rec.Click(chip, 0, afterMs: 1000);
        if (!ctx.IsPivotCanvasTarget) throw new InvalidOperationException("canvas pivot not targeted");
        rec.Cursor = ClipCursor.Arrow;
        var (cx, cy) = ctx.Pivot.ToLocalPixel(p.Width, p.Height);
        rec.Move(ToolFileHelp.At(m, cx, cy), 700);
        rec.Down();
        rec.Hold(150);
        rec.Move(ToolFileHelp.At(m, p.Width / 2.0, p.Height), 900);
        rec.Hold(200);
        rec.Up();
        rec.Hold(900);

        // 몸 레이어를 캔버스 피벗 대표 레이어로(이름 앞 왕관, 캔버스 피벗이 그 레이어 피벗을 따름).
        var body = LayerName(m, "Body");
        rec.Move(body, 800);
        rec.Hold(250);
        rec.Click(body, 0, MouseButton.Right, afterMs: 700);
        var item = rec.At(MenuItemNamed(Chickxel.App.Localization.UiStrings.Timeline_PivotLayerSet));
        rec.Move(item, 800);
        rec.Hold(500);
        rec.Click(item, 0, afterMs: 400);
        if (p.PivotLayer?.Name != "Body") throw new InvalidOperationException("representative layer not set");
        rec.Move(body + new Vector(160, -150), 700);
        rec.MarkPoster();
        rec.Hold(2600);
        rec.Save("docs/assets/feature-pivot", ClipFormat.Mp4);
    }

    /// <summary>
    /// 팔레트로 색 바꾸기: 차가운 색 팔레트의 메뉴 단추 → 팔레트 색으로 변환… → 창에서 대상, 색 고르는 규칙(가장 가까운 색 + 색 거리 OKLab,
    /// 밝기 순서, 색상 먼저), 디더링을 바꿔 가며 전·후 미리보기를 보고 변환한다.
    /// </summary>
    [Shot("clip-feature-convert")]
    public static void Convert(ShotKit k)
    {
        App.Settings.BottomHeight = 218;
        var m = ScreenDemo.OpenFolder(k, width: 1280, height: 656);
        ScreenDemo.Build(k, m, group: false);
        ToolFileHelp.Canvas(m).SetZoom(3);
        var colors = ShotKit.Get<ColorPanel>(m, "_colors");
        Color32 Hex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        var frost = new[] { 0x0B132Bu, 0x1C2541u, 0x3A506Bu, 0x0F4C75u, 0x3282B8u, 0x5BC0BEu, 0x6FFFE9u, 0xBBE1FAu, 0xF0F8FFu, 0x4A4E69u, 0x9A8C98u, 0xC9ADA7u }.Select(Hex).ToList();
        // 쌓인 팔레트는 접어 두고 맨 아래에 바꿀 팔레트를 둔다(낮은 창에서도 머리글이 보이게).
        colors.SetPalettes([.. colors.Palettes.Select(pp => pp with { Collapsed = true }), new NamedPalette("Frost", frost)]);
        k.Settle(300);
        var sections = ShotKit.Get<System.Collections.IList>(colors, "_sections").Cast<object>().ToList();
        ShotKit.Call(colors, "SetActive", sections[^1]);
        k.Settle(300);
        var menuButton = ShotKit.Get<Control>(sections[^1], "MenuButton");

        using var rec = new ClipRecorder(k, m, new Rect(0, 0, 1280, 656), titleBar: true, frameMs: 42) { TitleFilter = PrettyTitle(k) };
        rec.Place(new Point(760, 300));
        rec.Hold(700);
        rec.Click(rec.At(menuButton), 800, pressMs: 0, afterMs: 600);
        var item = rec.At(MenuItemNamed(Chickxel.App.Localization.UiStrings.S_ColorPanel_SectionMenu_65C4F22AE2));
        rec.Move(item, 700);
        rec.Hold(450);
        k.Dialog(() => rec.PressAndRelease(item, 0, pressMs: 0), w =>
        {
            // 화면 높이에 맞춰 줄어드는 창처럼(낮은 창 안에 들어오게) 높이를 막고 본문은 스크롤.
            w.MaxHeight = 656 - ShotKit.TitleHeight - 8;
            ShotKit.Pump(300);
            rec.Hold(900);
            RadioButton[] rules = ShotKit.Get<RadioButton[]>(w, "_rules");
            void Pick(Control c, int after = 1300) => rec.Click(rec.At(c), 650, pressMs: 120, afterMs: after);
            Pick(ShotKit.Get<Control>(w, "_layer"), 700);
            // 본문을 내려 전·후 미리보기와 색 수가 다 보이게(휠로 내리듯 몇 프레임에 걸쳐).
            var scroll = ShotKit.Get<ScrollViewer>(w, "_bodyScroll");
            double max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            for (int i = 1; i <= 8; i++)
            {
                scroll.Offset = new Vector(0, max * i / 8);
                rec.Hold(42);
            }
            rec.Hold(500);
            // 색 고르는 규칙: 가장 가까운 색의 색 거리 → OKLab, 밝기 순서, 색상 먼저.
            var distance = ShotKit.Get<ComboBox>(w, "_distance");
            Pick(distance, 600);
            var oklab = ShotKit.OpenPopups.Where(p => p.Child is not null).SelectMany(p => ShotKit.All<ComboBoxItem>(p.Child!)).ElementAt(1);
            Pick(oklab, 1200);
            Pick(rules[1]);
            Pick(rules[3]);
            Pick(ShotKit.Get<Control>(w, "_bayer"));
            Pick(ShotKit.Get<Control>(w, "_noDither"), 700);
            Pick(rules[1], 1200);
            rec.PressAndRelease(rec.At(ShotKit.Get<Button>(w, "OkButton")), 700, pressMs: 150);
        }, settleMs: 600);
        rec.Hold(500);
        rec.Move(new Point(880, 250), 700);
        rec.MarkPoster();
        rec.Hold(2600);
        rec.Save("docs/assets/feature-convert", ClipFormat.Mp4);
    }
}
