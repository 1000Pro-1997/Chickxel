using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Gpixel.App;
using Gpixel.Core.Model;
using static GuideShots.ScreenDemo;
using static GuideShots.ScreenShot;

namespace GuideShots;

/// <summary>화면 구성: 전체 창, 위 막대, 숫자 칸, 도구 막대, 캔버스, 도구 설정, 상태 표시줄.</summary>
internal static class ScreenMainShots
{
    /// <summary>화면 구성 장 첫 그림: 전체 창(애니메이션 프로젝트, 파일 브라우저 폴더 열림)과 영역마다 번호.</summary>
    [Shot("screen-overview")]
    public static void Overview(ShotKit k)
    {
        var main = OpenFolder(k);
        Build(k, main);
        k.Settle(300);
        var tool = ShotKit.Get<Control>(main, "_cbToolName");
        var bar = tool.GetVisualAncestors().OfType<Border>().First();
        var menu = ShotKit.Find<Menu>(main);
        var toolbar = ToolbarColumn(main);
        var colors = ShotKit.Get<Control>(main, "_colors");
        var editor = ShotKit.Get<Control>(colors, "_editor");
        var c = ShotKit.Bounds(colors, main);
        var palette = new Rect(c.X, c.Y, c.Width, ShotKit.Bounds(editor, main).Y - c.Y);
        var tabs = ShotKit.All<Control>(main).First(x => x.GetType().Name == "DocumentTabStrip" && x.IsEffectivelyVisible);
        var status = StatusBar(main);
        Shot(k, main, null, new Rect(main.ClientSize), marks:
        [
            U(main, menu, bar).Deflate(3),
            toolbar,
            ShotKit.Get<Control>(main, "_browser"),
            palette.Deflate(3),
            editor,
            tabs,
            ShotKit.Get<Control>(main, "_canvas"),
            ShotKit.Get<Control>(main, "_timeline"),
            ShotKit.Get<Control>(main, "_preview"),
            ShotKit.Get<Control>(main, "_toolOptions"),
            status,
        ]);
    }

    [Shot("screen-top-bar")]
    public static void TopBar(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var grid = ShotKit.Get<ToggleButton>(main, "_cbGrid");
        var tiles = ShotKit.Get<ToggleButton>(main, "_cbTiles");
        var tiled = ShotKit.Get<ToggleButton>(main, "_cbTiled");
        var pivot = ShotKit.Get<ToggleButton>(main, "_cbPivot");
        var onion = ShotKit.Get<ToggleButton>(main, "_cbOnion");
        var zoom = ShotKit.Get<Control>(main, "_cbZoom");
        var fit = ShotKit.Get<Control>(main, "_cbFit");
        var tool = ShotKit.Get<TextBlock>(main, "_cbToolName");
        var hint = ShotKit.Get<TextBlock>(main, "_cbHint");
        var ai = ((Panel)fit.Parent!).Children.OfType<Button>().Last(b => b.IsVisible);
        var bar = tool.GetVisualAncestors().OfType<Border>().First();
        var views = U(main, grid, tiles, tiled, pivot, onion);
        var strip = ShotKit.Bounds(bar, main);
        // 1: 위 막대 — ① 도구 이름 ② 사용법 ③ 보기 옵션 ④ 확대율 상자 ⑤ 맞추기 ⑥ AI 채팅(번호는 막대 위 여백에)
        Shot(k, main, null, strip, [Above(TextRect(tool, main)), Above(TextRect(hint, main)), Above(views), Above(ShotKit.Bounds(zoom, main)), Above(ShotKit.Bounds(fit, main)), Above(ShotKit.Bounds(ai, main))],
            only: [strip]);

        // 2: 확대율 상자 우클릭 목록 ⑦(상자 왼쪽 아래를 눌러 목록이 상자를 가리지 않게) — 단추 줄과 목록만
        var row = U(main, grid, ai).Inflate(4);
        var z = ShotKit.Bounds(zoom, main);
        k.Click(main, new Point(z.X + 3, z.Bottom - 2), MouseButton.Right, settleMs: 500);
        var list = PopupArea(k, main) ?? throw new InvalidOperationException("no zoom list");
        Shot(k, main, "screen-top-bar-2", row.Union(list).Inflate(6), [Pop(list)], 7, only: [row, list]);
        PopupMenus.CloseAll();
        k.Settle(200);

        // 3~6: 보기 단추 우클릭 설정 팝업(격자 ⑧, 타일 격자 ⑨, 타일 모드 ⑩, 양파 ⑪)
        int n = 8, file = 3;
        foreach (var button in new Control[] { grid, tiles, tiled, onion })
        {
            k.Click(button, MouseButton.Right, settleMs: 500);
            var pop = PopupArea(k, main) ?? throw new InvalidOperationException("no view popup");
            // 팝업이 단추 줄 바로 아래 붙어 뜨므로 위로는 넓히지 않는다(테두리가 단추 글자를 긋지 않게).
            Shot(k, main, $"screen-top-bar-{file++}", row.Union(pop).Inflate(6), [pop.Inflate(new Thickness(8, 0, 1, 1))], n++, only: [row, pop]);
            PopupMenus.CloseAll();
            k.Settle(200);
        }
    }

    /// <summary>숫자 칸(도구 설정의 브러시 크기) ①과 우클릭 자주 쓰는 값 목록 ②.</summary>
    [Shot("screen-common-controls")]
    public static void CommonControls(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var options = ShotKit.Get<Control>(main, "_toolOptions");
        var size = ShotKit.Get<NumericUpDown>(options, "_sizeNum");
        Ctx(main).BrushSize = 4;
        k.Settle(200);
        // 칸 오른쪽 끝을 우클릭해 목록이 칸을 가리지 않게.
        var s = ShotKit.Bounds(size, main);
        k.Click(main, new Point(s.Right - 3, s.Bottom - 2), MouseButton.Right, settleMs: 500);
        var list = PopupArea(k, main) ?? throw new InvalidOperationException("no preset list");
        var brush = ShotKit.Get<Control>(options, "_brushBox");
        var o = ShotKit.Bounds(options, main);
        var panel = new Rect(o.X, o.Y, o.Width, ShotKit.Bounds(brush, main).Bottom + 4 - o.Y);
        var field = ShotKit.FindText(options, "Size").GetVisualAncestors().OfType<Control>().First(a => ShotKit.Bounds(a, main).Contains(s.Center));
        Shot(k, main, null, panel.Union(list).Inflate(6), [Room(field, main), Pop(list)], only: [panel, list]);
    }

    /// <summary>도구 막대: ① 폴더 단추 ② 묶음 칸 ③ 펼친 목록 ④ 저장 ⑤ 되돌리기.</summary>
    [Shot("screen-toolbar")]
    public static void Toolbar(ShotKit k)
    {
        var main = Open(k, height: 760);
        Build(k, main);
        var slots = ShotKit.Get<List<ToolSlotButton>>(main, "_toolSlots");
        var group = slots.First(s => s.IsGroup && s.Contains(ToolKind.Fill));
        var files = ShotKit.Get<Control>(main, "_tbFiles");
        var save = ShotKit.Get<Control>(main, "_tbSave");
        var revert = ShotKit.Get<Control>(main, "_tbRevert");
        // 저장·되돌리기 단추가 켜지게 한 번 고친다(수정됨 상태).
        Ctx(main).Structural(() => Project(main).SetFrameDuration(0, 110));
        k.Settle(200);
        k.Click(group, MouseButton.Right, settleMs: 500);
        var list = PopupArea(k, main) ?? throw new InvalidOperationException("no tool list");
        var column = ToolbarColumn(main);
        static Rect Corner(Rect r) => r.Inflate(new Thickness(8, 8, 1, 1));
        Shot(k, main, null, column.Union(list).Inflate(8),
            [Corner(ShotKit.Bounds(files, main)), Corner(ShotKit.Bounds(group, main)), Pop(list), Corner(ShotKit.Bounds(save, main)), Corner(ShotKit.Bounds(revert, main))],
            only: [column, list]);
    }

    /// <summary>
    /// 캔버스: 1 = 부위(보석) 레이어를 골랐을 때(① 부위 ② 반투명한 나머지), 2 = 보기 기능(③ 체커 ④ 픽셀 격자 ⑤ 타일 격자 ⑥ 대칭 안내선
    /// ⑦ 피벗 ⑧ 선택 영역 ⑨ 양파 껍질), 3 = 타일 모드 ⑩.
    /// </summary>
    [Shot("screen-canvas")]
    public static void Canvas(ShotKit k)
    {
        var main = Open(k, height: 1100);
        Build(k, main);
        var canvas = ShotKit.Get<PixelCanvas>(main, "_canvas");
        var ctx = Ctx(main);
        var p = Project(main);
        var tabs = ShotKit.All<Control>(main).First(c => c.GetType().Name == "DocumentTabStrip" && c.IsEffectivelyVisible);
        var area = Around(main, 0, canvas, tabs);

        // 1: 부위 레이어
        var gem = p.Layers.First(l => l.Name == "Gem");
        ctx.SetCurrentLayer(p.IndexOf(gem));
        canvas.SetZoom(7);
        k.Settle(400);
        var o = gem.PartOffset;
        Shot(k, main, null, area, [Px(main, canvas, 0, 0, gem.PartSize.Width, gem.PartSize.Height).Inflate(6), Px(main, canvas, -o.X, -o.Y, p.Width, p.Height)]);

        // 2: 보기 기능 — 몸 레이어 5번 프레임(4번과 6번이 양파 껍질로 비침)
        ctx.SetCurrent(p.IndexOf(p.Layers.First(l => l.Name == "Body")), 4);
        ShotKit.Call(main, "SetPixelGrid", true);
        ShotKit.Call(main, "SetTileGrid", true);
        ctx.MirrorX = true;
        ctx.OnionSkin = true;
        ctx.Selection.Select(new PixelImage(p.Width, p.Height), new System.Drawing.Rectangle(31, 58, 10, 9));
        canvas.SetZoom(7);
        k.Settle(500);
        var (px, py) = ctx.Pivot.ToLocalPixel(p.Width, p.Height);
        Shot(k, main, "screen-canvas-2", area,
        [
            Px(main, canvas, 0, 0, 5, 5),
            Px(main, canvas, 19, 70, 4, 4),
            Px(main, canvas, 31, 31, 2, 2).Inflate(4),
            Px(main, canvas, 24.5, 88, 0, 7).Inflate(new Thickness(5, 0)),
            Px(main, canvas, px, py, 0, 0).Inflate(10),
            Px(main, canvas, 31, 58, 10, 9),
            Px(main, canvas, 8, 10, 6, 8),
        ], 3);

        // 3: 타일 모드
        ctx.Selection.Deselect(new PixelImage(p.Width, p.Height));
        ctx.MirrorX = false;
        ctx.OnionSkin = false;
        ShotKit.Call(main, "SetPixelGrid", false);
        ShotKit.Call(main, "SetTileGrid", false);
        ShotKit.Call(main, "SetTiledMode", true);
        canvas.SetZoom(3);
        k.Settle(500);
        Shot(k, main, "screen-canvas-3", area, [Px(main, canvas, 0, 0, p.Width, p.Height)], 10);
    }

    /// <summary>도구 설정: 연필(① 제목 줄 ② 그 도구의 설정), 피벗 도구(③ 대상 ④ 5×5 단추 ⑤ X·Y ⑥ 현재 프레임만 따로).</summary>
    [Shot("screen-tool-settings")]
    public static void ToolSettings(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var options = ShotKit.Get<Control>(main, "_toolOptions");
        var title = ShotKit.Get<TextBlock>(options, "_title");
        Rect Body() => ShotKit.All<Border>(options)
            .Where(b => b.IsEffectivelyVisible && b.Parent is StackPanel s && s.Parent is not null && b.Bounds.Height > 30 && b.Bounds.Width > 150)
            .Select(b => ShotKit.Bounds(b, main)).Aggregate((a, b) => a.Union(b));
        var o = ShotKit.Bounds(options, main);
        var body = Body();
        var crop1 = new Rect(o.X, o.Y, o.Width, body.Bottom - o.Y + 8);
        Shot(k, main, null, crop1, [Room(TextRect(title, main)), body], only: [crop1]);

        k.Command("도구/피벗");
        k.Settle(300);
        var presets = ShotKit.Get<Button[]>(options, "_presetButtons");
        var x = ShotKit.Get<Control>(options, "_pivotX");
        var y = ShotKit.Get<Control>(options, "_pivotY");
        var target = ShotKit.Get<TextBlock>(options, "_pivotTarget");
        var perFrame = ShotKit.Get<Control>(options, "_framePivot");
        var pivotBox = ShotKit.Get<Control>(options, "_pivotBox");
        var b2 = ShotKit.Bounds(pivotBox, main);
        var crop2 = new Rect(o.X, o.Y, o.Width, b2.Bottom - o.Y + 8);
        Shot(k, main, "screen-tool-settings-2", crop2,
            [TextRect(target, main).Inflate(new Thickness(14, 2, 2, 2)), U(main, presets), U(main, x, y), perFrame], 3, only: [crop2]);
    }

    /// <summary>상태 표시줄: ① 좌표 ② 색 ③ 크기 ④ 프레임/레이어 ⑤ 확대율 ⑥ 저장 상태 ⑦ 안내.</summary>
    [Shot("screen-status-bar")]
    public static void StatusBar(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var canvas = ShotKit.Get<Control>(main, "_canvas");
        var c = ShotKit.Bounds(canvas, main);
        k.Hover(main, new Point(c.Center.X, c.Center.Y - 40), 300);
        var pos = ShotKit.Get<TextBlock>(main, "_stPos");
        var msg = ShotKit.Get<TextBlock>(main, "_stMessage");
        var bar = StatusBar(main);
        var end = TextRect(msg, main);
        // 칸이 붙어 있어 번호를 칸 위(어두운 여백)에 둔다.
        static Rect Up(Rect r) => r.Inflate(new Thickness(3, 18, 1, 1));
        var crop = new Rect(0, bar.Y, Math.Min(main.ClientSize.Width, end.Right + 30), bar.Height);
        Shot(k, main, null, crop,
        [
            Up(ShotKit.Bounds(pos, main)), Up(U(main, ShotKit.Get<Control>(main, "_stColor"))), Up(U(main, ShotKit.Get<Control>(main, "_stSize"))),
            Up(U(main, ShotKit.Get<Control>(main, "_stFrame"))), Up(U(main, ShotKit.Get<Control>(main, "_stZoom"))),
            Up(U(main, ShotKit.Get<Control>(main, "_stDirtyDot"), ShotKit.Get<Control>(main, "_stDirty"))), Up(end),
        ], only: [crop]);
    }

    /// <summary>왼쪽 세로 도구 막대(폴더 단추부터 되돌리기 단추까지 감싼 세로 칸).</summary>
    private static Rect ToolbarColumn(MainForm main)
    {
        var files = ShotKit.Get<Control>(main, "_tbFiles");
        var revert = ShotKit.Get<Control>(main, "_tbRevert");
        var column = files.GetVisualAncestors().OfType<Control>().FirstOrDefault(a => a.Bounds.Height >= ShotKit.Bounds(revert, main).Bottom - ShotKit.Bounds(files, main).Y && a.Bounds.Width < 80);
        return column is not null ? ShotKit.Bounds(column, main) : U(main, files, revert);
    }

    /// <summary>맨 아래 상태 표시줄(창 너비).</summary>
    private static Rect StatusBar(MainForm main)
    {
        var pos = ShotKit.Get<Control>(main, "_stPos");
        var bar = pos.GetVisualAncestors().OfType<Control>().First(a => a.Bounds.Width > main.ClientSize.Width - 20);
        return ShotKit.Bounds(bar, main);
    }

    /// <summary>캔버스 픽셀 사각형(지금 보이는 대상 기준 — 부위 화면이면 부위의 (0,0)) → 창 좌표.</summary>
    private static Rect Px(MainForm main, PixelCanvas canvas, double x, double y, double w, double h)
    {
        var off = ShotKit.Get<System.Drawing.Point>(canvas, "_offset");
        int z = canvas.Zoom;
        var c = ShotKit.Bounds(canvas, main);
        return new Rect(c.X + off.X + x * z, c.Y + off.Y + y * z, w * z, h * z);
    }
}

/// <summary>
/// 화면 구성 샷의 찍기 도우미: 번호 동그라미가 그림 밖으로 밀려 표시한 것을 가리지 않게 자르는 영역을 넓혀 찍고(창 밖이면 어두운 여백),
/// 열린 팝업(설정 팝업·메뉴·목록)의 자리를 찾는다.
/// </summary>
internal static class ScreenShot
{
    /// <summary>번호 동그라미(반지름 12, 테두리 바깥 3px 모서리에 놓임)가 그림 안에 다 들어가는 여백.</summary>
    private const double BadgeRoom = 17;
    private static readonly IBrush Margin = new SolidColorBrush(Color.FromRgb(24, 24, 27));

    /// <summary>컨트롤들을 감싼 창 좌표 사각형.</summary>
    public static Rect U(TopLevel top, params Control[] controls)
        => controls.Select(c => ShotKit.Bounds(c, top)).Aggregate((a, b) => a.Union(b));

    /// <summary>컨트롤 사각형을 <paramref name="d"/>만큼 넓힌 것(작은 단추의 번호가 단추를 덜 가리게).</summary>
    public static Rect Grow(Control c, TopLevel top, double d) => ShotKit.Bounds(c, top).Inflate(d);

    /// <summary>작은 표시(글자·단추)용: 왼쪽 위로 넓혀 번호 동그라미가 표시한 것 바깥에 놓이게.</summary>
    public static Rect Room(Rect r) => r.Inflate(new Thickness(10, 12, 2, 2));

    public static Rect Room(Control c, TopLevel top) => Room(ShotKit.Bounds(c, top));

    /// <summary>가로로 붙은 작은 표시용: 위로 넓혀 번호 동그라미가 표시한 것 위에 놓이게.</summary>
    public static Rect Above(Rect r) => r.Inflate(new Thickness(2, 16, 2, 2));

    /// <summary>팝업(메뉴·설정 팝업)용: 번호 동그라미가 첫 항목 글자를 가리지 않게 왼쪽 위로 조금 넓힌다.</summary>
    public static Rect Pop(Rect r) => r.Inflate(new Thickness(8, 8, 1, 1));

    /// <summary>왼쪽 끝 줄(이름표가 왼쪽 끝에 붙은 줄)용: 번호 동그라미가 왼쪽 여백에 놓이게.</summary>
    public static Rect Left(Rect r) => r.Inflate(new Thickness(16, 1, 1, 1));

    /// <summary>글자 칸에서 글자가 차지한 부분만(채우기 칸이면 남는 너비 빼고).</summary>
    public static Rect TextRect(TextBlock t, TopLevel top)
    {
        var b = ShotKit.Bounds(t, top);
        return new Rect(b.X, b.Y, Math.Min(b.Width, t.DesiredSize.Width), b.Height);
    }

    /// <summary>
    /// 열린 팝업(<paramref name="tips"/>가 아니면 툴팁 제외 — 따로 뜬 팝업 창이든 창 안 겹침 층이든)의 보이는 판(배경이 칠해진 첫 테두리 — 그림자 여백 빼고)을
    /// 감싼 창 좌표 사각형. 없으면 null.
    /// </summary>
    public static Rect? PopupArea(ShotKit k, TopLevel top, bool tips = false)
    {
        var popups = (List<Popup>)typeof(ShotKit).GetField("s_popups", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var origin = (PixelPoint)ShotKit.Call(k, "Position", top)!;
        Rect? area = null;
        foreach (var p in popups.Where(p => p.IsOpen && p.Child is not null && (tips || p.Child is not ToolTip)))
        {
            var panel = VisiblePanel(p.Child!);
            Rect r;
            if (p.Host is TopLevel host)
            {
                var pos = (PixelPoint)ShotKit.Call(k, "Position", host)!;
                r = ShotKit.Bounds(panel, host).Translate(new Vector(pos.X - origin.X, pos.Y - origin.Y));
            }
            else if (TopLevel.GetTopLevel(p.Child) == top) r = ShotKit.Bounds(panel, top);
            else continue;
            area = area is { } a ? a.Union(r) : r;
        }
        return area;
    }

    /// <summary>팝업 내용 중 배경이 칠해진 첫 테두리(그림자용 바깥 여백을 뺀 실제 판). 없으면 내용 그대로.</summary>
    private static Control VisiblePanel(Control child)
        => child.GetSelfAndVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Background is not null && b.Bounds.Width > 0) ?? child;

    /// <summary>
    /// <paramref name="crop"/>(창 좌표)을 찍어 저장한다. 표시가 다 들어가고 표시마다 왼쪽 위 번호 동그라미가 들어갈 자리만큼 넓히며,
    /// 창 밖은 어두운 여백. <paramref name="only"/>를 주면 그 사각형들만 화면을 보이고 나머지는 어두운 여백(팝업과 그 단추만 보일 때 등).
    /// 번호는 <paramref name="first"/>부터. <paramref name="scale"/>배로 키워 그리면(작은 단추가 붙어 있을 때) 번호 동그라미 크기는 그대로라 덜 붙는다.
    /// </summary>
    public static string Shot(ShotKit k, TopLevel top, string? name, Rect crop, IEnumerable<Mark>? marks = null, int first = 1, IEnumerable<Rect>? only = null, int scale = 1)
    {
        var frame = k.Capture(top);
        var window = new Rect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height);
        var rects = (marks ?? []).Select(m => m.In(top)).ToList();
        double room = BadgeRoom / scale;
        double left = crop.X, upper = crop.Y, right = crop.Right, lower = crop.Bottom;
        foreach (var r in rects)
        {
            left = Math.Min(left, r.X - room);
            upper = Math.Min(upper, r.Y - room);
            right = Math.Max(right, r.Right + 5.0 / scale);
            lower = Math.Max(lower, r.Bottom + 5.0 / scale);
        }
        var src = new Rect(Math.Floor(left), Math.Floor(upper), Math.Ceiling(right - Math.Floor(left)), Math.Ceiling(lower - Math.Floor(upper)));
        var shown = (only ?? [src]).Select(r => r.Intersect(window).Intersect(src)).Where(r => r.Width > 0 && r.Height > 0).ToList();
        var size = new PixelSize((int)src.Width * scale, (int)src.Height * scale);
        Rect ToImage(Rect r) => new((r.X - src.X) * scale, (r.Y - src.Y) * scale, r.Width * scale, r.Height * scale);
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            if (shown.Count != 1 || shown[0] != src) ctx.FillRectangle(Margin, new Rect(0, 0, size.Width, size.Height));
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                foreach (var r in shown) ctx.DrawImage(frame, r, ToImage(r));
            ShotKit.DrawMarks(ctx, rects.Select(ToImage), first, new Rect(0, 0, size.Width, size.Height));
        }
        return k.Write(target, name ?? k.Id);
    }
}
