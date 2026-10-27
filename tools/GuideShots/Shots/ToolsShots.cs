using Avalonia.Headless;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Gpixel.App;
using Gpixel.App.Localization;
using Gpixel.Core.Model;
using Gpixel.Core.Edit;

namespace GuideShots;

/// <summary>도구 장(tools-*) 그림에 쓰는 도움 함수(메인 창 비공개 필드·캔버스 좌표).</summary>
internal static class ToolFileHelp
{
    public static PixelCanvas Canvas(MainForm m) => ShotKit.Get<PixelCanvas>(m, "_canvas");
    public static EditorContext Ctx(MainForm m) => ShotKit.Get<EditorContext>(m, "_ctx");
    public static ToolOptionsPanel Options(MainForm m) => ShotKit.Get<ToolOptionsPanel>(m, "_toolOptions");

    /// <summary>
    /// 메인 창을 띄운다. <paramref name="timeline"/>을 주면 타임라인 칸 높이(px)를 그만큼으로 줄여 캔버스를 키운다
    /// (캔버스 위 상태를 크게 보여 줄 때 — 열 때 화면에 맞추기가 다시 맞춘다).
    /// </summary>
    public static MainForm Open(ShotKit k, string? path = null, int? timeline = null)
    {
        if (timeline is { } h) App.Settings.BottomHeight = h;
        return k.Main(path);
    }

    /// <summary>예시 그림을 작업 폴더에 영어 이름으로 복사한다(탭·제목에 보이는 파일 이름).</summary>
    public static string Sample(ShotKit k) => k.CopySample("wolf adventurer.png");

    /// <summary>프레임 픽셀 좌표(<paramref name="px"/>, <paramref name="py"/>, 소수 = 픽셀 안 위치)의 창 좌표.</summary>
    public static Point At(MainForm m, double px, double py)
    {
        var c = Canvas(m);
        var off = ShotKit.Get<System.Drawing.Point>(c, "_offset");
        var local = new Point(off.X + px * c.Zoom, off.Y + py * c.Zoom);
        return c.TranslatePoint(local, m) ?? throw new InvalidOperationException("canvas not attached");
    }

    /// <summary>파일 브라우저 칸을 접는다(도구 막대가 창 왼쪽 끝으로).</summary>
    public static void HideFiles(ShotKit k, MainForm m)
    {
        ShotKit.Call(m, "SetFilesVisible", false, false);
        k.Settle(300);
    }

    /// <summary>도구를 고른다(메뉴 명령 — 도구 막대·도구 설정이 함께 바뀜).</summary>
    public static void Tool(ShotKit k, string command)
    {
        k.Command("도구/" + command);
        k.Settle(250);
    }

    /// <summary>도구 설정 칸에서 보이는 묶음(Border)들.</summary>
    public static Rect OptionsContent(MainForm m)
    {
        var panel = Options(m);
        var shown = ShotKit.All<Control>(panel)
            .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0 && c is TextBlock or Border or CheckBox or ComboBox or NumericUpDown or Button)
            .Select(c => ShotKit.Bounds(c, m))
            .ToList();
        return shown.Aggregate((a, b) => a.Union(b));
    }

    /// <summary>도구 설정 칸 영역(창 좌표, 내용이 있는 높이까지).</summary>
    public static Rect OptionsRect(MainForm m, double pad = 8)
    {
        var panel = ShotKit.Bounds(Options(m), m);
        var content = OptionsContent(m);
        return new Rect(panel.X - pad, panel.Y - pad, panel.Width + pad * 2, content.Bottom - panel.Y + pad * 2);
    }

    /// <summary>도구 막대(폴더 단추 ~ 되돌리기 단추).</summary>
    public static Rect ToolBarRect(MainForm m)
    {
        var top = ShotKit.Get<Control>(m, "_tbFiles");
        var bottom = ShotKit.Get<Control>(m, "_tbRevert");
        return ShotKit.Bounds(top, m).Union(ShotKit.Bounds(bottom, m));
    }

    public static Rect Clamp(Rect r, TopLevel top) => r.Intersect(new Rect(0, 0, top.Bounds.Width, top.Bounds.Height));

    /// <summary>캔버스에 보이는 그림(프레임 0,0 ~ w,h)의 창 좌표 사각형.</summary>
    public static Rect SpriteRect(MainForm m)
    {
        var ctx = Ctx(m);
        return new Rect(At(m, 0, 0), At(m, ctx.CanvasWidth, ctx.CanvasHeight));
    }

    /// <summary>캔버스 컨트롤의 창 좌표 사각형.</summary>
    public static Rect CanvasRect(MainForm m) => ShotKit.Bounds(Canvas(m), m);

    /// <summary>캔버스 그림 둘레 + 오른쪽 도구 설정까지 한 번에(변형·캔버스 크기·9-slice처럼 둘이 함께 움직이는 모드).</summary>
    public static Rect CanvasAndOptions(MainForm m, double margin = 70)
    {
        var sprite = SpriteRect(m).Inflate(margin);
        var canvas = CanvasRect(m);
        var options = OptionsRect(m, 4);
        double left = Math.Max(canvas.X, sprite.X);
        double top = Math.Max(canvas.Y, Math.Min(sprite.Y, options.Y));
        double bottom = Math.Max(Math.Min(canvas.Bottom, sprite.Bottom), options.Bottom);
        return new Rect(left, top, options.Right - left, bottom - top);
    }

    /// <summary>작은 PNG를 만든다: <paramref name="rows"/>의 글자마다 한 픽셀(<paramref name="palette"/>에서 색 0xAARRGGBB, 없는 글자 = 투명).</summary>
    public static string MakePng(string path, string[] rows, IReadOnlyDictionary<char, uint> palette)
    {
        int h = rows.Length, w = rows.Max(r => r.Length);
        using var bmp = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using (var fb = bmp.Lock())
        {
            var data = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    data[y * w + x] = x < rows[y].Length && palette.TryGetValue(rows[y][x], out var c) ? unchecked((int)c) : 0;
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(data, y * w, fb.Address + y * fb.RowBytes, w);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bmp.Save(path);
        return path;
    }

    /// <summary>9-slice 예시용 UI 단추 그림(32×20, 둥근 테두리·윗면 밝은 줄·아랫면 그림자).</summary>
    public static string ButtonPng(string path)
    {
        const int w = 32, h = 20;
        var rows = new string[h];
        for (int y = 0; y < h; y++)
        {
            var row = new char[w];
            for (int x = 0; x < w; x++)
            {
                int dx = Math.Min(x, w - 1 - x), dy = Math.Min(y, h - 1 - y);
                bool top = y < h / 2;
                row[x] = (dx, dy) switch
                {
                    _ when dx + dy < 2 => '.',
                    _ when dx == 0 || dy == 0 || dx + dy == 2 => 'o',
                    _ when top && (dy == 1 || dx + dy == 3) => 'h',
                    _ when !top && (dy == 1 || dy == 2 || dx + dy <= 4) => 's',
                    _ => 'f',
                };
            }
            rows[y] = new string(row);
        }
        return MakePng(path, rows, new Dictionary<char, uint>
        {
            ['o'] = 0xFF1D2B53,
            ['h'] = 0xFF83E5FF,
            ['f'] = 0xFF29ADFF,
            ['s'] = 0xFF065AB5,
        });
    }

    /// <summary>끌어 놓기 예시용 작은 그림(빨간 하트 11×10).</summary>
    public static string HeartPng(string path) => MakePng(path,
    [
        ".ooo...ooo.",
        "orrro.orrro",
        "orwrrorrrro",
        "orrrrrrrrro",
        "orrrrrrrrro",
        ".orrrrrrro.",
        "..orrrrro..",
        "...orrro...",
        "....oro....",
        ".....o.....",
    ], new Dictionary<char, uint>
    {
        ['o'] = 0xFF3B1726,
        ['r'] = 0xFFE43B44,
        ['w'] = 0xFFFFD6D6,
    });
}

internal static class ToolsShots
{
    // TimelinePanel.TimelineGrid 배치(이름 칸 폭, 태그 띠·머리글 높이, 셀 크기).
    private const int NameW = 270, TagH = 20, HeadH = 30, CellH = 36, CellW = 42;

    private static List<ToolSlotButton> Slots(MainForm m) => ShotKit.Get<List<ToolSlotButton>>(m, "_toolSlots");

    private static Rect R(MainForm m, Control c) => ShotKit.Bounds(c, m);

    private static Rect Union(params Rect[] rects) => rects.Aggregate((a, b) => a.Union(b));

    /// <summary>도구 설정 칸의 비공개 컨트롤.</summary>
    private static T Opt<T>(MainForm m, string field) where T : Control => ShotKit.Get<T>(ToolFileHelp.Options(m), field);

    /// <summary>컨트롤이 든 한 줄(이름표 + 칸)의 사각형.</summary>
    private static Rect Row(MainForm m, Control c) => R(m, (Control)c.Parent!);

    /// <summary>줄 끝까지 늘어난 TextBlock을 글자 폭에 맞춘 사각형.</summary>
    private static Rect TextRect(MainForm m, TextBlock t)
    {
        var b = R(m, t);
        return new Rect(b.X, b.Y, Math.Min(b.Width, t.DesiredSize.Width), b.Height);
    }

    /// <summary>캔버스 픽셀 사각형의 창 좌표.</summary>
    private static Rect Px(MainForm m, double x, double y, double w, double h)
        => new(ToolFileHelp.At(m, x, y), ToolFileHelp.At(m, x + w, y + h));

    /// <summary>점 둘레 사각형(반 폭 <paramref name="r"/>): 왼쪽 위 번호 동그라미가 점을 가리지 않을 만큼 넉넉하게.</summary>
    private static Rect Around(Point p, double r = 16) => new(p.X - r, p.Y - r, r * 2, r * 2);

    /// <summary>
    /// 번호 동그라미가 첫 글자·확인란을 가리지 않게 왼쪽으로 <paramref name="left"/>만큼 늘리고,
    /// 붙어 있는 줄끼리 빨간 테두리가 겹치지 않게 위아래를 <paramref name="dy"/>씩 줄인다.
    /// </summary>
    private static Rect Lead(Rect r, double left = 12, double dy = 2) => new(r.X - left, r.Y + dy, r.Width + left, r.Height - dy * 2);

    private static Rect Lead(MainForm m, Control c, double left = 12, double dy = 2) => Lead(R(m, c), left, dy);

    /// <summary>오른쪽 독을 조금 넓혀(기본 300 → 330) 브러시 모양 단추(사각·원·마름모)가 잘리지 않게 메인 창을 연다.</summary>
    private static MainForm Open(ShotKit k, string? path = null, int? timeline = null)
    {
        App.Settings.RightWidth = 330;
        return ToolFileHelp.Open(k, path ?? ToolFileHelp.Sample(k), timeline);
    }

    /// <summary>도구 설정 칸 위(제목)부터 <paramref name="last"/> 아래까지.</summary>
    private static Rect OptionsUpTo(MainForm m, Control last, double pad = 8)
    {
        var o = ToolFileHelp.OptionsRect(m, pad);
        return new Rect(o.X, o.Y, o.Width, R(m, last).Bottom + pad + 2 - o.Y);
    }

    /// <summary>명령 id(단축키 표의 "분류/이름", 이름만도 됨)의 메뉴 항목.</summary>
    private static MenuItem CommandItem(MainForm m, string id)
        => ShotKit.Get<System.Collections.IEnumerable>(m, "_commands").Cast<object>()
            .Where(c => ShotKit.Get<ShortcutCommand>(c, "Info").Id is var cid && (cid == id || cid.EndsWith("/" + id, StringComparison.Ordinal)))
            .Select(c => ShotKit.Get<MenuItem?>(c, "Item")).First(i => i is not null)!;

    [Shot("tools-list")]
    public static void List(ShotKit k)
    {
        var m = Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.Tool(k, "연필");
        var slots = Slots(m);
        var bar = Union([.. slots.Select(s => R(m, s))]);
        // 1: 창 전체 — 도구 막대와 도구 설정.
        k.Save(m, marks: [bar, ToolFileHelp.OptionsRect(m, 2)]);
        // 2: 도구 막대 칸마다 번호(표 순서 = 막대 순서).
        var crop = new Rect(bar.X - 46, bar.Y - 14, bar.Width + 66, bar.Height + 26);
        k.Save(m, "tools-list-2", crop: crop, marks: slots.Select(s => (Mark)Lead(m, s, 22, 5)), firstNumber: 3);
    }

    [Shot("tools-basics")]
    public static void Basics(ShotKit k)
    {
        // 창을 좁혀 위 막대 안내·기본/보조색·도구 설정이 한 그림에 들어오게.
        App.Settings.RightWidth = 330;
        var m = k.Main(ToolFileHelp.Sample(k), width: 1180, height: 720);
        ToolFileHelp.HideFiles(k, m);
        ToolFileHelp.Tool(k, "연필");
        var colors = ShotKit.Get<ColorPanel>(m, "_colors");
        var primary = ShotKit.Get<Control>(colors, "_primarySlot");
        var secondary = ShotKit.Get<Control>(colors, "_secondarySlot");
        var erase = Opt<CheckBox>(m, "_rightErase");
        var hint = ShotKit.Get<TextBlock>(m, "_cbHint");
        var options = ToolFileHelp.OptionsRect(m);
        var crop = new Rect(0, 0, m.ClientSize.Width, Math.Max(options.Bottom, R(m, secondary).Bottom) + 8);
        k.Save(m, crop: crop, marks: [primary, secondary, Lead(m, erase), Lead(TextRect(m, hint), 14, 0)]);

        // 2: 편집 메뉴의 외곽선·색 바꾸기·뒤집기/회전·캔버스 크기.
        var edit = ShotKit.Find<Menu>(m).Items.OfType<MenuItem>().ElementAt(1);
        k.Click(edit, settleMs: 400);
        Rect Item(string id) => Lead(ToolFileShotsMenus.InWindow(m, CommandItem(m, id)), 12, 4);
        var menu = ToolFileShotsMenus.OpenMenuRect(m) ?? new Rect(0, 0, 400, 600);
        k.Save(m, "tools-basics-2", crop: new Rect(0, 0, menu.Right + 16, menu.Bottom + 12), marks:
        [
            Item("편집/외곽선 추가(기본색)").Union(Item("편집/외곽선 추가(대각 포함)")),
            Item("편집/색 바꾸기…"),
            Union(Item("편집/좌우 뒤집기(선택/셀)"), Item("편집/상하 뒤집기(선택/셀)"), Item("편집/90° 회전(선택/캔버스)"), Item("편집/-90° 회전(선택/캔버스)")),
            Item("편집/캔버스 크기…"),
        ], firstNumber: 5);
    }

    [Shot("tools-groups")]
    public static void Groups(ShotKit k)
    {
        var m = Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.Tool(k, "사각 선택");
        var slots = Slots(m);
        var fill = slots.First(s => s.Contains(ToolKind.Fill));
        var shapes = slots.First(s => s.Contains(ToolKind.Rectangle));
        var select = slots.First(s => s.Contains(ToolKind.Select));
        ShotKit.Call(select, "ShowGroup");
        k.Settle(400);
        var list = ToolFileShotsMenus.OpenMenuRect(m) ?? throw new InvalidOperationException("group list did not open");
        var f = R(m, fill);
        var s = R(m, select);
        double left = f.X - 44, top = f.Y - 22;
        var crop = new Rect(left, top, list.Right + 14 - left, Math.Max(s.Bottom, list.Bottom) + 14 - top);
        k.Save(m, crop: crop, marks: [Lead(m, fill, 16, 3), Lead(m, shapes, 16, 3), Lead(m, select, 16, 3), list]);
    }

    [Shot("tools-brush")]
    public static void Brush(ShotKit k)
    {
        var m = Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.Tool(k, "연필");
        var ctx = ToolFileHelp.Ctx(m);
        ctx.BrushShape = BrushShape.Circle;
        ctx.BrushSize = 7;
        k.Settle(200);
        var brushBox = Opt<Control>(m, "_brushBox");
        k.Save(m, crop: OptionsUpTo(m, brushBox),
            marks: [Lead(m, Opt<Control>(m, "_shapes"), 4, 0), Opt<Control>(m, "_brushPreview"), Lead(Row(m, Opt<Control>(m, "_sizeNum")), 4, 0)]);

        // 2: 사각형 도구 — 선 두께·펜 모양(같은 브러시 칸).
        ToolFileHelp.Tool(k, "사각형");
        k.Save(m, "tools-brush-2", crop: OptionsUpTo(m, brushBox), marks: [brushBox], firstNumber: 4);

        // 3: 밝게/어둡게 — 세기.
        ToolFileHelp.Tool(k, "밝게/어둡게");
        var dodge = Opt<Control>(m, "_dodgeNum");
        k.Save(m, "tools-brush-3", crop: OptionsUpTo(m, Opt<Control>(m, "_dodgeBox")), marks: [Lead(Row(m, dodge))], firstNumber: 5);

        // 4: 디더링 — 무늬 목록을 펼친 모습.
        ToolFileHelp.Tool(k, "디더링");
        var pattern = Opt<ComboBox>(m, "_ditherPattern");
        pattern.IsDropDownOpen = true;
        k.Settle(400);
        var crop = OptionsUpTo(m, Opt<Control>(m, "_ditherBox"));
        if (ToolFileShotsMenus.OpenMenuRect(m) is { } list) crop = crop.Union(list.Inflate(8));
        k.Save(m, "tools-brush-4", crop: ToolFileHelp.Clamp(crop, m), marks: [Lead(Row(m, pattern))], firstNumber: 6);
        pattern.IsDropDownOpen = false;
    }

    [Shot("tools-fill")]
    public static void Fill(ShotKit k)
    {
        var m = Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.Tool(k, "채우기");
        var source = Opt<ComboBox>(m, "_fillSource");
        source.IsDropDownOpen = true;
        k.Settle(400);
        var crop = OptionsUpTo(m, Opt<Control>(m, "_fillBox"));
        if (ToolFileShotsMenus.OpenMenuRect(m) is { } list) crop = crop.Union(list.Inflate(8));
        k.Save(m, crop: ToolFileHelp.Clamp(crop, m), marks:
        [
            Lead(Row(m, Opt<Control>(m, "_tolNum"))),
            Lead(m, Opt<Control>(m, "_contiguous")),
            Lead(m, Opt<Control>(m, "_diagonal")),
            Lead(Row(m, source)),
        ]);
        source.IsDropDownOpen = false;
        k.Settle(200);

        // 2: 스포이드도 같은 기준 설정.
        ToolFileHelp.Tool(k, "스포이드");
        k.Save(m, "tools-fill-2", crop: OptionsUpTo(m, Opt<Control>(m, "_pickerBox")),
            marks: [Lead(Row(m, Opt<Control>(m, "_pickSource")))], firstNumber: 5);
    }

    [Shot("tools-select")]
    public static void Select(ShotKit k)
    {
        var m = Open(k, ToolFileHelp.Sample(k), timeline: 110);
        ShotKit.Call(m, "SetPixelGrid", false);
        var ctx = ToolFileHelp.Ctx(m);
        Rect Selected()
        {
            var b = ctx.Selection.Bounds;
            return Px(m, b.X, b.Y, b.Width, b.Height);
        }
        // 1: 사각 선택 — 머리를 끌어 선택(점선 테두리).
        ToolFileHelp.Tool(k, "사각 선택");
        k.Drag(m, ToolFileHelp.At(m, 7, 3), ToolFileHelp.At(m, 42, 36));
        k.Settle(300);
        var area = ToolFileHelp.SpriteRect(m).Inflate(40);
        k.Save(m, crop: area, marks: [Selected()]);

        // 2: 자유 선택 — 끄는 중(선택될 영역이 파란 점선). 마우스를 떼기 전에 찍는다.
        k.Command("편집/선택 해제");
        ToolFileHelp.Tool(k, "자유 선택");
        (double X, double Y)[] path = [(10, 40), (16, 37), (25, 36), (34, 38), (40, 44), (41, 54), (37, 63), (28, 66), (18, 64), (11, 58), (9, 49)];
        var first = ToolFileHelp.At(m, path[0].X, path[0].Y);
        m.MouseMove(first);
        m.MouseDown(first, MouseButton.Left);
        for (int i = 1; i < path.Length; i++)
        {
            var from = ToolFileHelp.At(m, path[i - 1].X, path[i - 1].Y);
            var to = ToolFileHelp.At(m, path[i].X, path[i].Y);
            for (int s = 1; s <= 4; s++)
            {
                m.MouseMove(from + (to - from) * (s / 4.0));
                k.Settle(10);
            }
        }
        k.Settle(300);
        double minX = path.Min(p => p.X), minY = path.Min(p => p.Y);
        var lasso = Px(m, minX, minY, path.Max(p => p.X) - minX, path.Max(p => p.Y) - minY).Inflate(6);
        k.Save(m, "tools-select-2", crop: area, marks: [lasso], firstNumber: 2);
        m.MouseUp(ToolFileHelp.At(m, path[^1].X, path[^1].Y), MouseButton.Left);
        k.Settle(200);

        // 3: 스마트 선택 — 머리카락을 누르면 비슷한 색 영역이 선택되고, 설정은 채우기와 같다.
        k.Command("편집/선택 해제");
        ToolFileHelp.Tool(k, "스마트 선택");
        ctx.FillTolerance = 25;
        k.Click(m, ToolFileHelp.At(m, 22.5, 18.5));
        k.Settle(300);
        k.Save(m, "tools-select-3", crop: ToolFileHelp.CanvasAndOptions(m, 40),
            marks: [Selected(), Opt<Control>(m, "_fillBox")], firstNumber: 3);
    }

    [Shot("tools-gradient")]
    public static void Gradient(ShotKit k)
    {
        var m = Open(k, ToolFileHelp.Sample(k), timeline: 110);
        ShotKit.Call(m, "SetPixelGrid", false);
        var ctx = ToolFileHelp.Ctx(m);
        // 그림 아래에 빈 레이어를 깔고 거기에 칠한다(셀 전체 = 캐릭터 뒤 배경).
        ctx.Structural(() => ctx.Project!.AddLayer(0, "Background"));
        ctx.SetCurrent(0, 0);
        k.Settle(200);
        ToolFileHelp.Tool(k, "그라데이션");
        ctx.GradientStops =
        [
            new GradientStop(0, new Color32(0x1D, 0x2B, 0x53)),
            new GradientStop(0.45, new Color32(0x29, 0xAD, 0xFF)),
            new GradientStop(1, new Color32(0xFF, 0xF1, 0xE8)),
        ];
        ctx.GradientAlphaStops = [new GradientAlphaStop(0, 255), new GradientAlphaStop(0.7, 255), new GradientAlphaStop(1, 96)];
        k.Settle(300);
        var options = ToolFileHelp.Options(m);
        var editor = ShotKit.Get<GradientEditor>(options, "_gradientEditor");
        var bar = ShotKit.Get<Control>(editor, "_bar");

        // 1: 캔버스에서 끄는 중(시작점 → 끝점 안내선) + 그라데이션 설정.
        var start = ToolFileHelp.At(m, 6.5, 6.5);
        var end = ToolFileHelp.At(m, 42.5, 90.5);
        m.MouseMove(start);
        m.MouseDown(start, MouseButton.Left);
        for (int i = 1; i <= 16; i++)
        {
            m.MouseMove(start + (end - start) * (i / 16.0));
            k.Settle(10);
        }
        k.Settle(300);
        k.Save(m, crop: ToolFileHelp.CanvasAndOptions(m, 40), marks:
        [
            Around(start),
            Around(end),
            Lead(m, bar, 14, 0),
            Lead(Row(m, ShotKit.Get<Control>(editor, "_position"))),
            Lead(m, ShotKit.Get<Control>(editor, "_colorRow")),
            Lead(Row(m, ShotKit.Get<Control>(editor, "_interpolation"))),
            Lead(Row(m, ShotKit.Get<Control>(editor, "_colorSpaceEditor"))),
            Lead(Row(m, ShotKit.Get<Control>(editor, "_dither"))),
            Lead(m, ShotKit.Get<Control>(options, "_hint")),
        ]);
        m.MouseUp(end, MouseButton.Left);
        k.Settle(300);

        // 2: 그라데이션 바 우클릭 = 프리셋 메뉴(이름 아래 색 띠).
        k.Click(m, R(m, bar).Center, MouseButton.Right, settleMs: 500);
        var items = ToolFileShotsMenus.InOpenPopups<MenuItem>().Where(i => i.IsVisible).ToList();
        var save = items.First(i => i.Header is string s && s == UiStrings.GradientPreset_SaveCurrent);
        var manage = items.First(i => i.Header is string s && s == UiStrings.GradientPreset_ManageMenu);
        var presets = items.Where(i => i.Header is StackPanel).Select(i => ToolFileShotsMenus.InWindow(m, i)).ToList();
        var menu = ToolFileShotsMenus.OpenMenuRect(m) ?? throw new InvalidOperationException("preset menu did not open");
        var crop = ToolFileHelp.OptionsRect(m).Union(menu.Inflate(10));
        Rect Pick(MenuItem i) => Lead(ToolFileShotsMenus.InWindow(m, i), 14, 1);
        List<Mark> menuMarks = [Pick(save)];
        if (presets.Count > 0) menuMarks.Add(Lead(Union([.. presets]), 14, 1));
        menuMarks.Add(Pick(manage));
        k.Save(m, "tools-gradient-2", crop: ToolFileHelp.Clamp(crop, m), marks: menuMarks, firstNumber: 10);

        // 3: 그라데이션 프리셋 관리 창.
        int next = 10 + menuMarks.Count;
        k.Dialog(() => manage.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)), w =>
        {
            var list = ShotKit.Get<Control>(w, "_list");
            var buttons = Union(ShotKit.Bounds(ShotKit.Get<Control>(w, "_favorite"), w), ShotKit.Bounds(ShotKit.Get<Control>(w, "_rename"), w),
                ShotKit.Bounds(ShotKit.Get<Control>(w, "_delete"), w));
            var detail = Union(ShotKit.Bounds(ShotKit.Get<Control>(w, "_preview"), w), ShotKit.Bounds(ShotKit.Get<Control>(w, "_editor"), w));
            k.Save(w, "tools-gradient-3", marks: [list, buttons, detail, ShotKit.Get<Control>(w, "_save")], firstNumber: next);
        }, settleMs: 700);
    }

    [Shot("tools-transform")]
    public static void Transform(ShotKit k)
    {
        var m = Open(k, ToolFileHelp.Sample(k), timeline: 110);
        ShotKit.Call(m, "SetPixelGrid", false);
        ToolFileHelp.Tool(k, "사각 선택");
        k.Drag(m, ToolFileHelp.At(m, 7, 3), ToolFileHelp.At(m, 42, 36));
        k.Command("편집/변형(회전·크기)…");
        k.Settle(300);
        var ctx = ToolFileHelp.Ctx(m);
        ctx.SetTransformParams(ctx.RotatedAboutPivot(ctx.TransformParams, 18));
        k.Settle(400);
        var t = ctx.Transform ?? throw new InvalidOperationException("not transforming");
        var p = ctx.TransformParams;
        var src = t.SourceBounds;
        Point Mapped(double x, double y)
        {
            var (cx, cy) = t.Map(p, x, y);
            return ToolFileHelp.At(m, cx, cy);
        }
        var center = Mapped(src.X + src.Width / 2.0, src.Y + src.Height / 2.0);
        var inside = Mapped(src.X + src.Width * 0.3, src.Y + src.Height * 0.3);
        var handle = Mapped(src.Right, src.Y + src.Height / 2.0);
        var corner = Mapped(src.Right, src.Top);
        var outward = corner - center;
        double len = Math.Sqrt(outward.X * outward.X + outward.Y * outward.Y);
        var rotate = corner + outward * (16 / len);
        var (pvx, pvy) = ctx.TransformPivot;
        var pivot = ToolFileHelp.At(m, pvx, pvy);
        var box = Opt<Control>(m, "_transformBox");
        var apply = ShotKit.FindText(box, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_8D7A9F13B5, ancestorControl: true);
        var cancel = ShotKit.FindText(box, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_BDB140E2ED, ancestorControl: true);
        k.Save(m, crop: ToolFileHelp.CanvasAndOptions(m), marks:
        [
            Around(inside, 18),
            Around(handle, 14),
            Around(rotate, 12),
            Around(pivot, 14),
            apply,
            cancel,
            Lead(Union(Row(m, Opt<Control>(m, "_tfAngle")), Row(m, Opt<Control>(m, "_tfScaleX")), Row(m, Opt<Control>(m, "_tfX")))),
            Lead(Row(m, Opt<Control>(m, "_tfResample"))),
        ]);
    }

    /// <summary>여러 셀 함께 변형: 프레임 4개를 머리글로 고르고 Ctrl+T(타임라인이 보이게 기본 높이로 연다).</summary>
    [Shot("tools-transform-2")]
    public static void TransformCels(ShotKit k)
    {
        var m = Open(k, ToolFileHelp.Sample(k));
        ShotKit.Call(m, "SetPixelGrid", false);
        var ctx = ToolFileHelp.Ctx(m);
        for (int i = 0; i < 3; i++) k.Command("프레임 복제");
        ctx.SetCurrentFrame(0);
        k.Settle(200);
        var timeline = ShotKit.Get<Control>(m, "_timeline");
        var grid = ShotKit.Get<Control>(timeline, "_grid");
        ShotKit.Call(grid, "SelectFrames", (IEnumerable<int>)[0, 1, 2, 3]);
        k.Settle(200);
        k.Command("편집/변형(회전·크기)…");
        k.Settle(300);
        ctx.SetTransformParams(ctx.RotatedAboutPivot(ctx.TransformParams, -12));
        k.Settle(400);
        var t = ctx.Transform ?? throw new InvalidOperationException("not transforming");
        var corners = t.Corners(ctx.TransformParams).Select(c => ToolFileHelp.At(m, c.X, c.Y)).ToList();
        var handles = new Rect(new Point(corners.Min(c => c.X), corners.Min(c => c.Y)), new Point(corners.Max(c => c.X), corners.Max(c => c.Y)));
        var cels = new Rect(grid.TranslatePoint(new Point(NameW, TagH), m)!.Value, new Size(4 * CellW, HeadH + CellH));
        var message = ShotKit.Get<TextBlock>(m, "_stMessage");
        k.Save(m, "tools-transform-2", marks: [cels, handles.Inflate(8), Lead(TextRect(m, message), 14, 2)], firstNumber: 9);
    }

    [Shot("tools-canvas-size")]
    public static void CanvasSize(ShotKit k)
    {
        var m = Open(k, ToolFileHelp.Sample(k), timeline: 110);
        ShotKit.Call(m, "SetPixelGrid", false);
        k.Command("편집/캔버스 크기…");
        k.Settle(300);
        var ctx = ToolFileHelp.Ctx(m);
        // 양옆으로 넓히고 아래(발)를 잘라 낸 새 캔버스: 잘릴 부분이 흐리게 보인다.
        var canvas = new System.Drawing.Rectangle(-10, 0, 69, 88);
        ctx.SetCanvasResize(canvas, new System.Drawing.Rectangle(10, 0, 49, 98));
        k.Settle(400);
        // 보이는 좌표: 새 캔버스는 원래 프레임 좌표, 그림은 (0,0)~(49,98).
        var border = Px(m, canvas.X, canvas.Y, canvas.Width, canvas.Height);
        var box = Opt<Control>(m, "_canvasResizeBox");
        var ok = ShotKit.FindText(box, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_666F59D00B, ancestorControl: true);
        var cancel = ShotKit.FindText(box, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_BDB140E2ED, ancestorControl: true);
        k.Save(m, crop: ToolFileHelp.CanvasAndOptions(m, 90), marks:
        [
            border.Inflate(10),
            Px(m, 0, canvas.Bottom, 49, 98 - canvas.Bottom),
            Around(ToolFileHelp.At(m, canvas.Right, canvas.Y + canvas.Height / 2.0), 14),
            Around(ToolFileHelp.At(m, 24, 56), 18),
            ok,
            cancel,
            Lead(Row(m, Opt<Control>(m, "_crWidth"))),
            Lead(m, Opt<Control>(m, "_crFromCenter")),
            Lead(Row(m, Opt<Control>(m, "_crSlice"))),
            Lead(Union(Row(m, Opt<Control>(m, "_crImageWidth")), Row(m, Opt<Control>(m, "_crImageHeight")))),
            Lead(m, Opt<Control>(m, "_crKeepRatio")),
            Lead(Row(m, Opt<Control>(m, "_crX"))),
            Lead(Row(m, Opt<Control>(m, "_crAnchor"))),
        ]);
    }

    [Shot("tools-nine-slice")]
    public static void NineSlice(ShotKit k)
    {
        var png = ToolFileHelp.ButtonPng(Path.Combine(k.WorkDir, "button.png"));
        var m = Open(k, png, timeline: 110);
        ShotKit.Call(m, "SetPixelGrid", false);
        ToolFileHelp.Canvas(m).SetZoom(14);
        ToolFileHelp.Tool(k, "9-slice");
        var ctx = ToolFileHelp.Ctx(m);
        ctx.SetSliceBorder(new SpriteBorder(5, 5, 5, 6));
        k.Settle(400);
        var sL = Opt<Control>(m, "_sL");
        var sB = Opt<Control>(m, "_sB");
        k.Save(m, crop: ToolFileHelp.CanvasAndOptions(m, 60), marks:
        [
            ToolFileHelp.SpriteRect(m).Inflate(8),
            Lead(Row(m, sL).Union(Row(m, sB))),
            Lead(m, Opt<Control>(m, "_sliceClear"), 14, 0),
            Lead(TextRect(m, Opt<TextBlock>(m, "_sliceTarget")), 14, 0),
        ]);
    }
}

/// <summary>열린 팝업(우클릭 메뉴·펼친 목록)의 창 좌표.</summary>
internal static class ToolFileShotsMenus
{
    private const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    private static List<Avalonia.Controls.Primitives.Popup> Popups()
        => (List<Avalonia.Controls.Primitives.Popup>)typeof(ShotKit).GetField("s_popups", Any)!.GetValue(null)!;

    private static PixelPoint PositionOf(TopLevel top)
        => top is Window w ? w.Position
            : top.PlatformImpl?.GetType().GetProperty("Position", Any)?.GetValue(top.PlatformImpl) is PixelPoint pp ? pp : default;

    /// <summary>열린 팝업들을 모두 감싸는 사각형(없으면 null). 창 안 겹침 층(OverlayPopupHost)이든 따로 뜬 팝업 창이든.</summary>
    public static Rect? OpenMenuRect(Window w)
    {
        Rect? all = null;
        foreach (var p in Popups().Where(p => p.IsOpen && p.Host is not null))
        {
            Rect r;
            if (p.Host is TopLevel host)
            {
                var pos = PositionOf(host);
                r = new Rect(pos.X - w.Position.X, pos.Y - w.Position.Y, host.ClientSize.Width, host.ClientSize.Height);
            }
            else if (p.Host is Control overlay && overlay.TranslatePoint(default, w) is { } at)
                r = new Rect(at, overlay.Bounds.Size);
            else continue;
            all = all is { } a ? a.Union(r) : r;
        }
        return all;
    }

    /// <summary>열린 팝업 안의 <typeparamref name="T"/> 컨트롤들.</summary>
    public static IEnumerable<T> InOpenPopups<T>() where T : Visual
    {
        foreach (var p in Popups().Where(p => p.IsOpen).ToList())
            if (p.Child is Visual v)
                foreach (var c in v.GetVisualDescendants().OfType<T>().Concat(v is T self ? [self] : []))
                    yield return c;
    }

    /// <summary>컨트롤의 창 <paramref name="w"/> 좌표 사각형(따로 뜬 팝업 창 안의 컨트롤이면 창 위치 차이만큼 옮김).</summary>
    public static Rect InWindow(Window w, Control c)
    {
        if (c.TranslatePoint(default, w) is { } p) return new Rect(p, c.Bounds.Size);
        var top = TopLevel.GetTopLevel(c) ?? throw new InvalidOperationException("control is not shown");
        var at = c.TranslatePoint(default, top) ?? default;
        var pos = PositionOf(top);
        return new Rect(at.X + pos.X - w.Position.X, at.Y + pos.Y - w.Position.Y, c.Bounds.Width, c.Bounds.Height);
    }
}
