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

    /// <summary>예시 그림을 작업 폴더에 복사한다(영어 그림이면 탭·제목에 보일 파일 이름도 영어로).</summary>
    public static string Sample(ShotKit k)
        => ShotKit.Language == "ko" ? k.CopySample() : k.CopySample("wolf adventurer.png");

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
    [Shot("tools-list")]
    public static void List(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.Tool(k, "연필");
        k.Save(m, markRects: [ToolFileHelp.ToolBarRect(m), ToolFileHelp.OptionsRect(m, 2)]);
    }

    [Shot("tools-basics")]
    public static void Basics(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        ToolFileHelp.Tool(k, "연필");
        var hint = ShotKit.Get<TextBlock>(m, "_cbHint");
        var colors = ShotKit.Get<ColorPanel>(m, "_colors");
        var primary = ShotKit.Get<Control>(colors, "_primarySlot");
        var secondary = ShotKit.Get<Control>(colors, "_secondarySlot");
        var slots = ShotKit.Bounds(primary, m).Union(ShotKit.Bounds(secondary, m));
        var hintBox = ShotKit.Bounds(hint, m);
        var hintRect = new Rect(hintBox.X, hintBox.Y, Math.Min(hintBox.Width, hint.DesiredSize.Width), hintBox.Height);
        var crop = new Rect(0, 0, Math.Max(hintRect.Right, slots.Right) + 24, slots.Bottom + 8);
        k.Save(m, crop: crop, markRects: [hintRect.Inflate(2), slots.Inflate(2)]);
    }

    [Shot("tools-groups")]
    public static void Groups(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.HideFiles(k, m);
        ToolFileHelp.Tool(k, "사각 선택");
        var slots = ShotKit.Get<List<ToolSlotButton>>(m, "_toolSlots");
        var select = slots.First(s => s.Contains(ToolKind.Select));
        ShotKit.Call(select, "ShowGroup");
        k.Settle(400);
        var slot = ShotKit.Bounds(select, m);
        var crop = new Rect(0, slot.Y - 110, 250, 300);
        k.Save(m, crop: crop, markRects: [slot.Inflate(1)]);
    }

    [Shot("tools-brush")]
    public static void Brush(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.Tool(k, "연필");
        var ctx = ToolFileHelp.Ctx(m);
        ctx.BrushShape = BrushShape.Circle;
        ctx.BrushSize = 7;
        k.Settle(200);
        var brushBox = ShotKit.Get<Control>(ToolFileHelp.Options(m), "_brushBox");
        k.Save(m, crop: ToolFileHelp.OptionsRect(m), markRects: [ShotKit.Bounds(brushBox, m)]);

        ToolFileHelp.Tool(k, "디더링");
        var options = ToolFileHelp.Options(m);
        k.Save(m, name: "tools-brush-2", crop: ToolFileHelp.OptionsRect(m),
            markRects: [ShotKit.Bounds(ShotKit.Get<Control>(options, "_ditherBox"), m)]);
        ToolFileHelp.Tool(k, "밝게/어둡게");
        k.Save(m, name: "tools-brush-3", crop: ToolFileHelp.OptionsRect(m),
            markRects: [ShotKit.Bounds(ShotKit.Get<Control>(options, "_dodgeBox"), m)]);
    }

    [Shot("tools-fill")]
    public static void Fill(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.Tool(k, "채우기");
        var fillBox = ShotKit.Get<Control>(ToolFileHelp.Options(m), "_fillBox");
        k.Save(m, crop: ToolFileHelp.OptionsRect(m), markRects: [ShotKit.Bounds(fillBox, m)]);
    }

    [Shot("tools-select")]
    public static void Select(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k), timeline: 110);
        ShotKit.Call(m, "SetPixelGrid", false);
        // 사각 선택: 머리를 끌어 선택(점선 테두리).
        ToolFileHelp.Tool(k, "사각 선택");
        k.Drag(m, ToolFileHelp.At(m, 7, 3), ToolFileHelp.At(m, 42, 36));
        k.Settle(300);
        var area = ToolFileHelp.SpriteRect(m).Inflate(40);
        k.Save(m, crop: area);

        // 자유 선택: 끄는 중(선택될 영역이 파란 점선) — 마우스를 떼기 전에 찍는다.
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
        k.Save(m, name: "tools-select-2", crop: area);
        m.MouseUp(ToolFileHelp.At(m, path[^1].X, path[^1].Y), MouseButton.Left);
        k.Settle(200);

        // 스마트 선택: 머리카락을 누르면 비슷한 색 영역이 선택된다.
        k.Command("편집/선택 해제");
        ToolFileHelp.Tool(k, "스마트 선택");
        ToolFileHelp.Ctx(m).FillTolerance = 25;
        k.Click(m, ToolFileHelp.At(m, 22.5, 18.5));
        k.Settle(300);
        k.Save(m, name: "tools-select-3", crop: area);
    }

    [Shot("tools-gradient")]
    public static void Gradient(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k));
        ToolFileHelp.Tool(k, "그라데이션");
        var ctx = ToolFileHelp.Ctx(m);
        ctx.GradientStops =
        [
            new GradientStop(0, new Color32(0x1D, 0x2B, 0x53)),
            new GradientStop(0.45, new Color32(0x29, 0xAD, 0xFF)),
            new GradientStop(1, new Color32(0xFF, 0xF1, 0xE8)),
        ];
        ctx.GradientAlphaStops = [new GradientAlphaStop(0, 255), new GradientAlphaStop(0.7, 255), new GradientAlphaStop(1, 96)];
        k.Settle(300);
        var options = ToolFileHelp.Options(m);
        var editor = ShotKit.Get<Control>(options, "_gradientEditor");
        var bar = ShotKit.Get<Control>(editor, "_bar");
        k.Save(m, crop: ToolFileHelp.OptionsRect(m), markRects: [ShotKit.Bounds(bar, m).Inflate(2)]);

        // 그라데이션 바 우클릭 = 프리셋 메뉴(이름 아래 색 띠).
        var p = ShotKit.Bounds(bar, m).Center;
        k.Click(m, p, MouseButton.Right, settleMs: 500);
        var menu = ToolFileShotsMenus.OpenMenuRect(m);
        var opt = ToolFileHelp.OptionsRect(m);
        var crop = menu is { } r ? opt.Union(r.Inflate(10)) : opt;
        k.Save(m, name: "tools-gradient-2", crop: ToolFileHelp.Clamp(crop, m));
    }

    [Shot("tools-transform")]
    public static void Transform(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k), timeline: 110);
        ShotKit.Call(m, "SetPixelGrid", false);
        ToolFileHelp.Tool(k, "사각 선택");
        k.Drag(m, ToolFileHelp.At(m, 7, 3), ToolFileHelp.At(m, 42, 36));
        k.Command("편집/변형(회전·크기)");
        k.Settle(300);
        var ctx = ToolFileHelp.Ctx(m);
        ctx.SetTransformParams(ctx.RotatedAboutPivot(ctx.TransformParams, 18));
        k.Settle(400);
        var box = ShotKit.Get<Control>(ToolFileHelp.Options(m), "_transformBox");
        k.Save(m, crop: ToolFileHelp.CanvasAndOptions(m), markRects: [ShotKit.Bounds(box, m)]);
    }

    [Shot("tools-canvas-size")]
    public static void CanvasSize(ShotKit k)
    {
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k), timeline: 110);
        ShotKit.Call(m, "SetPixelGrid", false);
        k.Command("편집/캔버스 크기");
        k.Settle(300);
        var ctx = ToolFileHelp.Ctx(m);
        // 양옆으로 넓히고 아래(발)를 잘라 낸 새 캔버스: 잘릴 부분이 흐리게 보인다.
        ctx.SetCanvasResize(new System.Drawing.Rectangle(-10, 0, 69, 88), new System.Drawing.Rectangle(10, 0, 49, 98));
        k.Settle(400);
        var box = ShotKit.Get<Control>(ToolFileHelp.Options(m), "_canvasResizeBox");
        k.Save(m, crop: ToolFileHelp.CanvasAndOptions(m, 90), markRects: [ShotKit.Bounds(box, m)]);
    }

    [Shot("tools-nine-slice")]
    public static void NineSlice(ShotKit k)
    {
        var png = ToolFileHelp.ButtonPng(Path.Combine(k.WorkDir, "button.png"));
        var m = ToolFileHelp.Open(k, png, timeline: 110);
        ShotKit.Call(m, "SetPixelGrid", false);
        ToolFileHelp.Canvas(m).SetZoom(14);
        ToolFileHelp.Tool(k, "9-slice");
        var ctx = ToolFileHelp.Ctx(m);
        ctx.SetSliceBorder(new SpriteBorder(5, 5, 5, 6));
        k.Settle(400);
        var box = ShotKit.Get<Control>(ToolFileHelp.Options(m), "_sliceBox");
        k.Save(m, crop: ToolFileHelp.CanvasAndOptions(m, 60), markRects: [ShotKit.Bounds(box, m)]);
    }
}

/// <summary>열린 팝업(우클릭 메뉴·펼친 목록)의 창 좌표.</summary>
internal static class ToolFileShotsMenus
{
    private const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    /// <summary>열린 팝업들을 모두 감싸는 사각형(없으면 null). 창 안 겹침 층(OverlayPopupHost)이든 따로 뜬 팝업 창이든.</summary>
    public static Rect? OpenMenuRect(Window w)
    {
        var popups = (List<Avalonia.Controls.Primitives.Popup>)typeof(ShotKit).GetField("s_popups", Any)!.GetValue(null)!;
        Rect? all = null;
        foreach (var p in popups.Where(p => p.IsOpen && p.Host is not null))
        {
            Rect r;
            if (p.Host is TopLevel host)
            {
                var pos = host.PlatformImpl?.GetType().GetProperty("Position", Any)?.GetValue(host.PlatformImpl) is PixelPoint pp ? pp : default;
                r = new Rect(pos.X - w.Position.X, pos.Y - w.Position.Y, host.ClientSize.Width, host.ClientSize.Height);
            }
            else if (p.Host is Control overlay && overlay.TranslatePoint(default, w) is { } at)
                r = new Rect(at, overlay.Bounds.Size);
            else continue;
            all = all is { } a ? a.Union(r) : r;
        }
        return all;
    }
}
