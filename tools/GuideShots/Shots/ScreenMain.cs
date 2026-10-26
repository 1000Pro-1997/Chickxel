using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Gpixel.App;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>화면 구성: 전체 창, 위 막대, 숫자 칸, 도구 막대, 캔버스, 도구 설정, 상태 표시줄.</summary>
internal static class ScreenMainShots
{
    /// <summary>화면 구성 장 첫 그림: 전체 창(애니메이션 프로젝트, 파일 브라우저 폴더 열림).</summary>
    [Shot("screen-overview")]
    public static void Overview(ShotKit k)
    {
        var main = OpenFolder(k);
        Build(k, main);
        CleanTitle(main);
        k.Settle(300);
        k.Save(main);
    }

    [Shot("screen-top-bar")]
    public static void TopBar(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var views = new Control[]
        {
            ShotKit.Get<ToggleButton>(main, "_cbGrid"), ShotKit.Get<ToggleButton>(main, "_cbTiles"), ShotKit.Get<ToggleButton>(main, "_cbTiled"),
            ShotKit.Get<ToggleButton>(main, "_cbPivot"), ShotKit.Get<ToggleButton>(main, "_cbOnion"),
        };
        var zoom = ShotKit.Get<Control>(main, "_cbZoom");
        var fit = ShotKit.Get<Control>(main, "_cbFit");
        var tool = ShotKit.Get<Control>(main, "_cbToolName");
        var hint = ShotKit.Get<Control>(main, "_cbHint");
        double barBottom = ShotKit.Bounds(zoom, main).Bottom + 8;
        k.Save(main, crop: new Rect(0, 0, main.ClientSize.Width, barBottom),
            markRects:
            [
                views.Select(v => ShotKit.Bounds(v, main)).Aggregate((a, b) => a.Union(b)),
                ShotKit.Bounds(zoom, main).Union(ShotKit.Bounds(fit, main)),
                ShotKit.Bounds(tool, main).Union(ShotKit.Bounds(hint, main)),
            ]);

        // 2: 격자 단추 우클릭 설정 팝업
        k.Click(views[0], MouseButton.Right, settleMs: 400);
        var b = ShotKit.Bounds(views[0], main);
        k.Save(main, "screen-top-bar-2", crop: new Rect(b.X - 420, 0, 640, 200), marks: [views[0]]);
        k.Key(main, Key.Escape);

        // 3: 확대율 상자 우클릭 목록
        k.Click(zoom, MouseButton.Right, settleMs: 400);
        var z = ShotKit.Bounds(zoom, main);
        k.Save(main, "screen-top-bar-3", crop: new Rect(z.X - 360, 0, 560, 300), marks: [zoom]);
    }

    /// <summary>숫자 칸 우클릭 = 자주 쓰는 값 목록(도구 설정의 브러시 크기).</summary>
    [Shot("screen-common-controls")]
    public static void CommonControls(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var options = ShotKit.Get<Control>(main, "_toolOptions");
        var size = ShotKit.All<NumericUpDown>(options).First(n => n.IsEffectivelyVisible);
        k.Click(size, MouseButton.Right, settleMs: 400);
        var o = ShotKit.Bounds(options, main);
        k.Save(main, crop: new Rect(o.X - 4, o.Y - 4, o.Width + 8, 470).Intersect(new Rect(main.ClientSize)), marks: [size]);
    }

    /// <summary>도구 막대 + 묶음 칸 우클릭으로 펼친 도구 목록.</summary>
    [Shot("screen-toolbar")]
    public static void Toolbar(ShotKit k)
    {
        var main = Open(k, height: 760);
        Build(k, main);
        var slots = ShotKit.Get<List<ToolSlotButton>>(main, "_toolSlots");
        var shapes = slots.First(s => s.IsGroup && s.Contains(ToolKind.Rectangle));
        var files = ShotKit.Get<Control>(main, "_tbFiles");
        var save = ShotKit.Get<Control>(main, "_tbSave");
        var revert = ShotKit.Get<Control>(main, "_tbRevert");
        // 저장 단추가 켜지게 한 번 고친다(수정됨 상태).
        Ctx(main).Structural(() => Project(main).SetFrameDuration(0, 110));
        k.Settle(200);
        k.Click(shapes, MouseButton.Right, settleMs: 400);
        var bar = ShotKit.Bounds(files, main).Union(ShotKit.Bounds(revert, main));
        k.Save(main, crop: new Rect(bar.X - 8, bar.Y - 8, 300, bar.Height + 16),
            marks: [files, shapes],
            markRects: [ShotKit.Bounds(save, main).Union(ShotKit.Bounds(revert, main))]);
    }

    /// <summary>캔버스: 800%에서 픽셀 격자·피벗, 그리고 부위 레이어를 골랐을 때(부위만 자기 크기, 나머지는 반투명).</summary>
    [Shot("screen-canvas")]
    public static void Canvas(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var zoom = ShotKit.Get<NumericUpDown>(main, "_cbZoom");
        zoom.Value = 800;
        k.Settle(400);
        var canvas = ShotKit.Get<Control>(main, "_canvas");
        var tabs = ShotKit.All<Control>(main).First(c => c.GetType().Name == "DocumentTabStrip" && c.IsEffectivelyVisible);
        k.Save(main, crop: Around(main, 0, canvas, tabs));

        // 2: 부위(보석) 레이어를 고른 화면
        var p = Project(main);
        Ctx(main).SetCurrentLayer(p.IndexOf(p.Layers.First(l => l.Name == L("보석", "Gem"))));
        zoom.Value = 600;
        k.Settle(400);
        var timeline = ShotKit.Get<Control>(main, "_timeline");
        k.Save(main, "screen-canvas-2", crop: Around(main, 0, canvas, tabs, timeline));
    }

    /// <summary>도구 설정: 연필, 그리고 피벗 도구.</summary>
    [Shot("screen-tool-settings")]
    public static void ToolSettings(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var options = ShotKit.Get<Control>(main, "_toolOptions");
        var o = ShotKit.Bounds(options, main);
        k.Save(main, crop: new Rect(o.X, o.Y, o.Width, 350));
        k.Command("도구/피벗");
        k.Settle(300);
        k.Save(main, "screen-tool-settings-2", crop: new Rect(o.X, o.Y, o.Width, 330));
    }

    /// <summary>상태 표시줄: 캔버스 위에 마우스를 올린 상태(좌표·색), 프레임 범위를 고른 안내.</summary>
    [Shot("screen-status-bar")]
    public static void StatusBar(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var canvas = ShotKit.Get<Control>(main, "_canvas");
        var c = ShotKit.Bounds(canvas, main);
        k.Hover(main, new Point(c.Center.X, c.Center.Y - 40), 300);
        var pos = ShotKit.Get<Control>(main, "_stPos");
        var msg = ShotKit.Get<Control>(main, "_stMessage");
        var r = ShotKit.Bounds(pos, main).Union(ShotKit.Bounds(msg, main));
        k.Save(main, crop: new Rect(0, r.Y - 8, 1100, r.Height + 14),
            marks: [pos, ShotKit.Get<Control>(main, "_stColor"), ShotKit.Get<Control>(main, "_stSize"),
                ShotKit.Get<Control>(main, "_stFrame"), ShotKit.Get<Control>(main, "_stZoom"), ShotKit.Get<Control>(main, "_stDirty")]);
    }
}
