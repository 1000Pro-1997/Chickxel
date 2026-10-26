using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Gpixel.App;
using Gpixel.Core.Model;
using static GuideShots.ScreenDemo;

namespace GuideShots;

/// <summary>화면 구성: 타임라인, 고르기, 편집, 태그, 레이어 효과.</summary>
internal static class ScreenTimelineShots
{
    // TimelinePanel.TimelineGrid의 배치(이름 칸 폭, 태그 띠·머리글 높이, 셀 크기, 효과 줄 높이).
    private const int NameW = 270, TagH = 20, HeadH = 30, CellH = 36, CellW = 42, FxH = 16;

    private static Control Grid(MainForm main) => Named(ShotKit.Get<Control>(main, "_timeline"), "TimelineGrid");

    /// <summary>격자 좌표 → 창 좌표.</summary>
    private static Point At(MainForm main, double x, double y)
        => Grid(main).TranslatePoint(new Point(x, y), main) ?? throw new InvalidOperationException("grid not attached");

    private static Point FrameHead(MainForm main, int f) => At(main, NameW + f * CellW + CellW / 2.0, TagH + HeadH / 2.0);
    private static Point Cell(MainForm main, int row, int f, double extraY = 0) => At(main, NameW + f * CellW + CellW / 2.0, TagH + HeadH + row * CellH + CellH / 2.0 + extraY);
    private static Point LayerName(MainForm main, int row, double extraY = 0) => At(main, 140, TagH + HeadH + row * CellH + CellH / 2.0 + extraY);
    private static Point Tag(MainForm main, int from, int to) => At(main, NameW + (from + to + 1) * CellW / 2.0, TagH / 2.0);

    private static Rect GridRect(MainForm main, double x, double y, double w, double h)
    {
        var p = At(main, x, y);
        return new Rect(p.X, p.Y, w, h);
    }

    private static Rect TimelineRect(MainForm main) => ShotKit.Bounds(ShotKit.Get<Control>(main, "_timeline"), main);

    /// <summary>타임라인 전체(프레임 6개까지 보이게 오른쪽을 줄임).</summary>
    private static Rect TimelineCrop(MainForm main, double width = 640)
    {
        var t = TimelineRect(main);
        return new Rect(t.X, t.Y, width, t.Height);
    }

    /// <summary>타임라인 칸을 높여(레이어 추가 줄·효과 줄까지 보이게) 연 메인 창.</summary>
    private static MainForm OpenTall(ShotKit k, int bottom = 330)
    {
        App.Settings.BottomHeight = bottom;
        return Open(k);
    }

    [Shot("screen-timeline")]
    public static void Timeline(ShotKit k)
    {
        var main = OpenTall(k);
        Build(k, main);
        var crop = TimelineCrop(main);
        k.Save(main, crop: crop, markRects:
        [
            GridRect(main, NameW, 0, 6 * CellW, TagH),            // 태그 띠
            GridRect(main, NameW, TagH, 6 * CellW, HeadH),        // 프레임 머리글
            GridRect(main, 0, TagH + HeadH, NameW, 5 * CellH),    // 레이어 행
            GridRect(main, NameW + 6 * CellW, TagH, CellW, HeadH + CellH),        // 프레임 추가·복사
            GridRect(main, 0, TagH + HeadH + 5 * CellH, NameW + 6 * CellW, CellH), // 레이어 추가·복사 줄
        ]);

        // 2: 프레임 머리글 우클릭
        k.Click(main, FrameHead(main, 1), MouseButton.Left);
        k.Click(main, FrameHead(main, 1), MouseButton.Right, settleMs: 400);
        k.Save(main, "screen-timeline-2", crop: new Rect(crop.X, crop.Y - 330, 820, crop.Height + 330));
        k.Key(main, Key.Escape);

        // 3: 레이어 이름 우클릭
        k.Click(main, LayerName(main, 3), MouseButton.Left);
        k.Click(main, LayerName(main, 3), MouseButton.Right, settleMs: 400);
        k.Save(main, "screen-timeline-3", crop: new Rect(crop.X, 120, 700, crop.Bottom - 120));
    }

    [Shot("screen-timeline-select")]
    public static void Select(ShotKit k)
    {
        var main = OpenTall(k);
        Build(k, main);
        k.Click(main, FrameHead(main, 1));
        k.Click(main, FrameHead(main, 3), mods: RawInputModifiers.Shift);
        k.Settle(300);
        var t = TimelineRect(main);
        var msg = ShotKit.Get<Control>(main, "_stMessage");
        var m = ShotKit.Bounds(msg, main);
        k.Save(main, crop: new Rect(t.X, t.Y, t.Width, m.Bottom - t.Y + 2),
            markRects: [GridRect(main, NameW + CellW, TagH, 3 * CellW, HeadH + 5 * CellH)], marks: [msg]);

        // 2: 셀 묶음(몸·그림자 행 × 2~3 프레임)
        k.Click(main, Cell(main, 3, 1));
        k.Click(main, Cell(main, 4, 2), mods: RawInputModifiers.Shift);
        k.Settle(300);
        k.Save(main, "screen-timeline-select-2", crop: new Rect(t.X, t.Y, t.Width, m.Bottom - t.Y + 2), marks: [msg]);
    }

    [Shot("screen-timeline-edit")]
    public static void Edit(ShotKit k)
    {
        var main = OpenTall(k);
        Build(k, main);
        // 빈 셀(그림자 3번 프레임)을 만들어 "이전 프레임 그림 복사"가 켜지게
        var p = Project(main);
        Ctx(main).Structural(() => p.SetCel(p.IndexOf(p.Layers.First(l => l.Name == L("그림자", "Shadow"))), 2, null));
        k.Settle(200);
        k.Click(main, Cell(main, 4, 2));
        k.Click(main, Cell(main, 4, 2), MouseButton.Right, settleMs: 400);
        var crop = TimelineCrop(main);
        k.Save(main, crop: new Rect(crop.X, crop.Y - 300, 820, crop.Height + 300));
    }

    [Shot("screen-tags")]
    public static void Tags(ShotKit k)
    {
        var main = Open(k);
        Build(k, main);
        var t = TimelineRect(main);
        var tagStrip = GridRect(main, NameW, 0, 6 * CellW, TagH);
        k.Save(main, crop: new Rect(t.X, t.Y, 620, 150), markRects: [tagStrip]);

        // 2: 태그 우클릭 메뉴
        k.Click(main, Tag(main, 0, 3));
        k.Click(main, Tag(main, 0, 3), MouseButton.Right, settleMs: 400);
        k.Save(main, "screen-tags-2", crop: new Rect(t.X, t.Y - 200, 720, 400));
    }

    [Shot("screen-layer-effects")]
    public static void LayerEffects(ShotKit k)
    {
        var main = OpenTall(k, 370);
        Build(k, main, effects: true);
        var p = Project(main);
        int body = p.IndexOf(p.Layers.First(l => l.Name == L("몸", "Body")));
        Ctx(main).SetCurrentFrame(4);
        k.Settle(300);
        // 1: 타임라인의 효과 줄(몸 레이어 아래 효과 2줄)
        var t = TimelineRect(main);
        k.Save(main, crop: new Rect(t.X, t.Y, 640, t.Height),
            markRects: [GridRect(main, 0, TagH + HeadH + 4 * CellH, NameW + 6 * CellW, 2 * FxH)]);

        // 2: 효과 창
        var timeline = ShotKit.Get<Control>(main, "_timeline");
        k.Dialog(() => ShotKit.Call(timeline, "EditLayerEffects", body, -1, null, null), w =>
        {
            ShotKit.Pump(600);
            k.Save(w, "screen-layer-effects-2");
        }, settleMs: 900);
    }
}
