using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>
/// 변형 모드의 픽셀 엔진(<see cref="SpriteProject.LayerTransform"/>): 지금 셀(Ctrl+T)·레이어 전체(Alt+Shift+C), 선택 영역, 여백, 자유 변형, 피벗 따라가기, 부위 영역.
/// </summary>
public class LayerTransformTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Green = new(0, 255, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    private static void Draw(SpriteProject p, int layer, int frame, int x, int y, Color32 color)
    {
        p.BeginCelStroke(layer, frame).Set(x, y, color);
        Assert.True(p.CommitCelStroke("점"));
    }

    /// <summary>캔버스 좌표 (x, y)의 셀 전체(캔버스 안 + 밖) 색.</summary>
    private static Color32 FullAt(SpriteProject p, int x, int y, int layer, int frame)
    {
        var (image, origin) = p.GetFullCel(layer, frame);
        int ix = x - origin.X, iy = y - origin.Y;
        return ix < 0 || iy < 0 || ix >= image.Width || iy >= image.Height ? default : image[ix, iy];
    }

    /// <summary>상자 값 <paramref name="p"/>를 점 (<paramref name="px"/>, <paramref name="py"/>)를 중심으로 시계 방향 90° 돌린 값.</summary>
    private static TransformParams Rotate90About(TransformParams p, double px, double py)
        => p with { Angle = 90, CenterX = px - (p.CenterY - py), CenterY = py + (p.CenterX - px) };

    private static SpriteProject.LayerTransform Whole(SpriteProject p, int layer, int frame = 0, PixelMask? selection = null)
        => SpriteProject.LayerTransform.Create(p, layer, frame, selection, wholeLayer: true)!;

    private static SpriteProject.LayerTransform Cel(SpriteProject p, int layer, int frame, PixelMask? selection = null)
        => SpriteProject.LayerTransform.Create(p, layer, frame, selection, wholeLayer: false)!;

    [Fact]
    public void WholeLayer_Scale_ChangesEveryFrameOfOneLayer_KeepsPushedOffPixels_PivotsFollow_OneUndoStep()
    {
        var p = new SpriteProject(6, 4);
        Draw(p, 0, 0, 0, 0, Green);
        p.AddLayer();
        p.AddFrame();
        Draw(p, 1, 0, 2, 1, Red);
        Draw(p, 1, 0, 3, 1, Blue);
        Draw(p, 1, 1, 2, 2, Red);
        var layer = p.Layers[1];
        p.SetLayerPivot(1, Pivot.FromLocalPixel(3, 2, 6, 4));
        var canvasPivot = p.GetFramePivot(0);
        int steps = p.History.UndoCount;

        var t = Whole(p, 1);
        Assert.Equal(new Rectangle(2, 1, 2, 2), t.Box.Bounds);
        // 왼쪽 위를 제자리에 두고 두 배
        var scaled = t.Box.Identity with { ScaleX = 2, ScaleY = 2, CenterX = 4, CenterY = 3 };
        Assert.True(t.Commit("레이어 변형", scaled, ResampleMode.Nearest, null));

        Assert.Equal(steps + 1, p.History.UndoCount);
        Assert.Equal(Green, p.GetCel(0, 0)![0, 0]);
        foreach (var (x, y) in new[] { (2, 1), (3, 2) }) Assert.Equal(Red, FullAt(p, x, y, 1, 0));
        foreach (var (x, y) in new[] { (4, 1), (5, 2) }) Assert.Equal(Blue, FullAt(p, x, y, 1, 0));
        // 프레임 1: (2,2) → (2..3, 3..4) — 아래 줄은 캔버스 밖 그림으로 남는다.
        Assert.Equal(Red, FullAt(p, 3, 3, 1, 1));
        Assert.Equal(Red, FullAt(p, 2, 4, 1, 1));
        Assert.NotNull(p.GetCelOverflow(1, 1));
        // 피벗(3,2)은 그림을 따라 (4,3)으로, 캔버스 피벗은 그대로
        Assert.Equal(new PointF(4, 3), p.LayerPivotPixel(layer, 0));
        Assert.Equal(new PointF(4, 3), p.LayerPivotPixel(layer, 1));
        Assert.Equal(canvasPivot, p.GetFramePivot(0));

        p.Undo();
        Assert.Equal(Blue, FullAt(p, 3, 1, 1, 0));
        Assert.Equal(default, FullAt(p, 3, 2, 1, 0));
        Assert.Null(p.GetCelOverflow(1, 1));
        Assert.Equal(new PointF(3, 2), p.LayerPivotPixel(layer, 0));
    }

    [Fact]
    public void WholeLayer_Rotate_TurnsEachFrameAroundItsOwnPivot()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        Draw(p, 0, 0, 4, 1, Red);
        Draw(p, 0, 1, 4, 1, Red);
        var layer = p.Layers[0];
        p.SetLayerPivot(0, Pivot.FromLocalPixel(4.5f, 4.5f, 8, 8));
        p.SetLayerFramePivot(0, 1, Pivot.FromLocalPixel(2.5f, 4.5f, 8, 8));

        var t = Whole(p, 0);
        Assert.True(t.Commit("레이어 변형", Rotate90About(t.Box.Identity, 4.5, 4.5), ResampleMode.Nearest, null));

        // 프레임 0: (4.5,1.5)를 (4.5,4.5) 둘레로 → (7.5,4.5), 프레임 1: (2.5,4.5) 둘레로 → (5.5,6.5)
        Assert.Equal(Red, p.GetCel(0, 0)![7, 4]);
        Assert.Equal(Red, p.GetCel(0, 1)![5, 6]);
        Assert.Equal(new Rectangle(7, 4, 1, 1), p.GetCel(0, 0)!.GetOpaqueBounds());
        Assert.Equal(new Rectangle(5, 6, 1, 1), p.GetCel(0, 1)!.GetOpaqueBounds());
        // 돌린 중심이 피벗이라 피벗은 제자리
        Assert.Equal(new PointF(4.5f, 4.5f), p.LayerPivotPixel(layer, 0));
        Assert.Equal(new PointF(2.5f, 4.5f), p.LayerPivotPixel(layer, 1));
    }

    [Fact]
    public void WholeLayer_RebaseToAnotherFrame_GivesTheSameResult()
    {
        static SpriteProject Make()
        {
            var p = new SpriteProject(10, 10);
            p.AddFrame();
            Draw(p, 0, 0, 2, 2, Red);
            Draw(p, 0, 1, 5, 3, Blue);
            p.SetLayerFramePivot(0, 1, Pivot.FromLocalPixel(6, 6, 10, 10));
            return p;
        }
        var a = Make();
        var ta = Whole(a, 0);
        var pa = Rotate90About(ta.Box.Identity, 5, 5);
        ta.Commit("변형", pa, ResampleMode.Nearest, null);

        var b = Make();
        var tb = Whole(b, 0);
        var (pb, _) = tb.Rebase(b.Frames[1], Rotate90About(tb.Box.Identity, 5, 5), (0, 0));
        Assert.Same(b.Frames[1], tb.ReferenceFrame);
        tb.Commit("변형", pb, ResampleMode.Nearest, null);

        for (int f = 0; f < 2; f++) Assert.True(a.Flatten(f).ContentEquals(b.Flatten(f)), $"frame {f}");
    }

    [Fact]
    public void WholeLayer_Margin_CropsPixelsOutsideTheBox_InEveryFrameAndOffCanvas()
    {
        var p = new SpriteProject(6, 4);
        p.AddFrame();
        Draw(p, 0, 0, 1, 1, Red);
        Draw(p, 0, 0, 4, 2, Blue);
        Draw(p, 0, 1, 4, 1, Blue);
        p.ShiftCels("옮기기", [(0, 1)], 2, 0); // 프레임 1의 파랑 (6,1) — 캔버스 밖

        var t = Whole(p, 0);
        Assert.Equal(new Rectangle(1, 1, 6, 2), t.Box.Bounds);
        // 상자를 왼쪽(x 0..2)만 남게 좁힌다: 그림은 제자리, 밖은 잘림
        var q = t.SetBox(new Rectangle(1, 1, 2, 2), t.Box.Identity);
        Assert.Equal(new Rectangle(1, 1, 2, 2), t.Box.Bounds);
        Assert.NotNull(t.CutPixels(t.Targets[0]));
        Assert.True(t.Commit("레이어 변형", q, ResampleMode.Nearest, null));

        Assert.Equal(Red, FullAt(p, 1, 1, 0, 0));
        Assert.Equal(default, FullAt(p, 4, 2, 0, 0));
        Assert.Equal(default, FullAt(p, 6, 1, 0, 1));
        Assert.Null(p.GetCelOverflow(0, 1));
    }

    [Fact]
    public void WholeLayer_Part_Rotate_AreaWrapsTheTurnedBox_BorderRemoved_PivotStaysOnTheImage()
    {
        var p = new SpriteProject(10, 10);
        var g = p.AddGroup();
        var part = p.AddPart(g, "팔", 4, 2, new Point(1, 1));
        int pi = p.IndexOf(part);
        Draw(p, pi, 0, 0, 0, Red);   // 캔버스 (1,1)
        p.SetPartBorder(pi, new SpriteBorder(1, 0, 0, 0));
        p.SetLayerPivot(pi, Pivot.FromLocalPixel(1, 1, 4, 2)); // 캔버스 (2,2)

        var t = Whole(p, pi);
        Assert.Equal(new Rectangle(0, 0, 4, 2), t.Box.Bounds);
        Assert.True(t.Commit("레이어 변형", Rotate90About(t.Box.Identity, 1, 1), ResampleMode.Nearest, null));

        Assert.Equal(new Rectangle(1, 1, 2, 4), part.PartRect);
        Assert.Equal(SpriteBorder.None, part.PartBorder);
        Assert.Equal(Red, p.Flatten(0).Get(2, 1));
        Assert.Equal(new PointF(2, 2), p.LayerPivotPixel(part, 0));
    }

    [Fact]
    public void WholeLayer_Part_StretchWithNineSlice_KeepsTheBorderPixels()
    {
        var p = new SpriteProject(10, 10);
        var g = p.AddGroup();
        var part = p.AddPart(g, "바", 3, 1, new Point(0, 0));
        int pi = p.IndexOf(part);
        Draw(p, pi, 0, 0, 0, Red);
        Draw(p, pi, 0, 1, 0, Green);
        Draw(p, pi, 0, 2, 0, Blue);
        p.SetPartBorder(pi, new SpriteBorder(1, 0, 1, 0));

        var t = Whole(p, pi);
        // 왼쪽을 제자리에 두고 가로 3 → 5
        var wide = t.Box.Identity with { ScaleX = 5 / 3.0, CenterX = 2.5 };
        Assert.True(t.Commit("레이어 변형", wide, ResampleMode.Nearest, NineSliceFill.Stretch));

        Assert.Equal(new Size(5, 1), part.PartSize);
        Assert.Equal(new SpriteBorder(1, 0, 1, 0), part.PartBorder);
        var cel = p.GetCel(pi, 0)!;
        Assert.Equal([Red, Green, Green, Green, Blue], Enumerable.Range(0, 5).Select(x => cel[x, 0]));
    }

    [Fact]
    public void Cel_Part_MoveOnly_MovesThatFramesPartPosition_LeavesOtherFrames()
    {
        var p = new SpriteProject(10, 10);
        var g = p.AddGroup();
        var part = p.AddPart(g, "팔", 2, 2, new Point(1, 1));
        int pi = p.IndexOf(part);
        p.AddFrame();
        Draw(p, pi, 0, 0, 0, Red);
        Draw(p, pi, 1, 0, 0, Red);
        var celBefore = p.GetCel(pi, 1)!.Clone();

        var t = Cel(p, pi, 1);
        var id = t.Box.Identity;
        Assert.True(t.Commit("변형", id with { CenterX = id.CenterX + 3, CenterY = id.CenterY + 2 }, ResampleMode.Nearest, null));

        Assert.Equal(new Point(1, 1), p.GetPartOffset(part, 0));
        Assert.Equal(new Point(4, 3), p.GetPartOffset(part, 1));
        Assert.True(p.HasPartFrameOverride(part, 1));
        Assert.False(p.HasPartFrameOverride(part, 0));
        Assert.True(p.GetCel(pi, 1)!.ContentEquals(celBefore));
    }

    [Fact]
    public void Cel_Part_GrowingPastTheArea_WidensTheAreaForEveryFrame_OtherFramesStayInPlace()
    {
        var p = new SpriteProject(10, 10);
        var g = p.AddGroup();
        var part = p.AddPart(g, "팔", 2, 2, new Point(2, 2));
        int pi = p.IndexOf(part);
        p.AddFrame();
        Draw(p, pi, 0, 0, 0, Red);   // 캔버스 (2,2)
        Draw(p, pi, 1, 1, 1, Blue);  // 캔버스 (3,3)

        var t = Cel(p, pi, 0);
        Assert.Equal(new Rectangle(0, 0, 1, 1), t.Box.Bounds);
        // 왼쪽 위를 제자리에 두고 세 배 → 부위 밖(2×2)으로 나감
        Assert.True(t.Commit("변형", t.Box.Identity with { ScaleX = 3, ScaleY = 3, CenterX = 1.5, CenterY = 1.5 }, ResampleMode.Nearest, null));

        Assert.Equal(new Rectangle(2, 2, 3, 3), part.PartRect);
        var f0 = p.Flatten(0);
        for (int y = 2; y < 5; y++)
            for (int x = 2; x < 5; x++)
                Assert.Equal(Red, f0.Get(x, y));
        Assert.Equal(Blue, p.Flatten(1).Get(3, 3));
    }

    [Fact]
    public void Cel_NoSelection_PivotFollowsTheDrawing_OnlyInThatFrame()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        Draw(p, 0, 0, 2, 2, Red);
        Draw(p, 0, 1, 2, 2, Red);
        var layer = p.Layers[0];
        p.SetLayerPivot(0, Pivot.FromLocalPixel(2, 2, 8, 8));

        var t = Cel(p, 0, 1);
        var id = t.Box.Identity;
        Assert.True(t.Commit("변형", id with { CenterX = id.CenterX + 3 }, ResampleMode.Nearest, null));

        Assert.Equal(Red, p.GetCel(0, 1)![5, 2]);
        Assert.Equal(Red, p.GetCel(0, 0)![2, 2]);
        Assert.Equal(new PointF(5, 2), p.LayerPivotPixel(layer, 1));
        Assert.Equal(new PointF(2, 2), p.LayerPivotPixel(layer, 0));
        Assert.True(p.HasLayerFramePivot(layer, 1));
        Assert.False(p.HasLayerFramePivot(layer, 0));
    }

    [Fact]
    public void Selection_MarginGrowsByRepeatingEdgePixels_PivotStays_SelectionFollows()
    {
        var p = new SpriteProject(8, 8);
        Draw(p, 0, 0, 2, 2, Red);
        Draw(p, 0, 0, 3, 2, Blue);
        var layer = p.Layers[0];
        var pivot = p.LayerPivotPixel(layer, 0);

        var t = Cel(p, 0, 0, new PixelMask(new Rectangle(2, 2, 2, 1)));
        Assert.True(t.HasSelection);
        // 오른쪽 가장자리를 2칸 넓힌다(그림은 제자리)
        var q = t.SetBox(new Rectangle(2, 2, 4, 1), t.Box.Identity);
        Assert.Equal(new PixelMask(new Rectangle(2, 2, 4, 1)).Bounds, t.SelectionAfter(q, ResampleMode.Nearest)!.Value.Bounds);
        Assert.True(t.Commit("변형", q, ResampleMode.Nearest, null));

        var cel = p.GetCel(0, 0)!;
        Assert.Equal([Red, Blue, Blue, Blue], Enumerable.Range(2, 4).Select(x => cel[x, 2]));
        Assert.Equal(pivot, p.LayerPivotPixel(layer, 0));
    }

    [Fact]
    public void Unchanged_CommitsNothing()
    {
        var p = new SpriteProject(4, 4);
        Draw(p, 0, 0, 1, 1, Red);
        int steps = p.History.UndoCount;
        var t = Whole(p, 0);
        Assert.False(t.Commit("변형", t.Box.Identity, ResampleMode.Nearest, null));
        Assert.Equal(steps, p.History.UndoCount);
    }

    /// <summary>오른쪽 아래 꼭짓점만 (<paramref name="dx"/>, <paramref name="dy"/>)만큼 끌어 찌그러뜨린 값.</summary>
    private static TransformParams DragBottomRight(TransformBox box, TransformParams p, double dx, double dy)
    {
        var c = box.Corners(p);
        c[2] = (c[2].X + dx, c[2].Y + dy);
        return box.WithCorners(p, c)!.Value;
    }

    [Fact]
    public void WholeLayer_Distorted_RebaseToAnotherFrame_GivesTheSameResult()
    {
        static SpriteProject Make()
        {
            var p = new SpriteProject(16, 16);
            p.AddFrame();
            for (int y = 2; y < 6; y++)
                for (int x = 2; x < 6; x++)
                {
                    Draw(p, 0, 0, x, y, Red);
                    Draw(p, 0, 1, x + 1, y + 2, Blue);
                }
            p.SetLayerFramePivot(0, 1, Pivot.FromLocalPixel(7, 6, 16, 16));
            return p;
        }
        var a = Make();
        var ta = Whole(a, 0);
        var pa = DragBottomRight(ta.Box, ta.Box.Identity with { Angle = 30 }, 3, 2);
        Assert.True(pa.IsDistorted);
        Assert.True(ta.Commit("변형", pa, ResampleMode.Nearest, null));

        var b = Make();
        var tb = Whole(b, 0);
        var (pb, _) = tb.Rebase(b.Frames[1], DragBottomRight(tb.Box, tb.Box.Identity with { Angle = 30 }, 3, 2), (0, 0));
        Assert.True(tb.Commit("변형", pb, ResampleMode.Nearest, null));

        for (int f = 0; f < 2; f++) Assert.True(a.Flatten(f).ContentEquals(b.Flatten(f)), $"frame {f}");
        Assert.Contains(Red, a.Flatten(0).Pixels);
        Assert.Contains(Blue, a.Flatten(1).Pixels);
    }

    [Fact]
    public void Distorted_SetBox_LeavesTheDrawingInPlace()
    {
        var p = new SpriteProject(16, 16);
        for (int y = 3; y < 9; y++)
            for (int x = 4; x < 9; x++)
                Draw(p, 0, 0, x, y, (x + y) % 2 == 0 ? Red : Blue);
        var t = Whole(p, 0);
        var q = DragBottomRight(t.Box, t.Box.Identity with { Angle = 20, CenterX = 7, CenterY = 6 }, 2.5, 1.5);
        var before = t.Render(t.Targets[0], q, ResampleMode.Nearest, null)!.Value;

        // 상자를 사방으로 넓혀도(선택이 없으니 넓힌 곳은 빈칸) 그림은 그대로
        var r = q;
        r = t.SetBox(new Rectangle(2, 1, 9, 10), r);
        Assert.True(r.IsDistorted);
        var after = t.Render(t.Targets[0], r, ResampleMode.Nearest, null)!.Value;
        Assert.Equal(before.Origin, after.Origin);
        Assert.True(before.Image.ContentEquals(after.Image));
    }

    [Fact]
    public void Distorted_ClearsNineSliceBorder_AndIsNotAPartMove()
    {
        var p = new SpriteProject(10, 10);
        var g = p.AddGroup();
        var part = p.AddPart(g, "바", 4, 4, new Point(1, 1));
        int pi = p.IndexOf(part);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                Draw(p, pi, 0, x, y, Red);
        p.SetPartBorder(pi, new SpriteBorder(1, 1, 1, 1));

        var t = Whole(p, pi);
        Assert.True(t.Commit("변형", DragBottomRight(t.Box, t.Box.Identity, 2, 2), ResampleMode.Nearest, NineSliceFill.Stretch));

        Assert.Equal(SpriteBorder.None, part.PartBorder);
        // 영역이 찌그러뜨린 사각형을 감싼다(오른쪽 아래가 2칸 늘어남).
        Assert.Equal(new Rectangle(1, 1, 6, 6), part.PartRect);
        Assert.Equal(Red, p.Flatten(0).Get(5, 5));
    }

    [Fact]
    public void PivotSlip_ShiftOnly_KeepsThePivotInPlace_NormalLayer()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        Draw(p, 0, 0, 2, 2, Red);
        Draw(p, 0, 1, 2, 2, Red);
        var layer = p.Layers[0];
        p.SetLayerPivot(0, Pivot.FromLocalPixel(2, 2, 8, 8));

        // 지금 셀
        var t = Cel(p, 0, 1);
        var id = t.Box.Identity;
        Assert.True(t.Commit("변형", id with { CenterX = id.CenterX + 3, CenterY = id.CenterY + 1 }, ResampleMode.Nearest, null, pivotSlip: (-3, -1)));
        Assert.Equal(Red, p.GetCel(0, 1)![5, 3]);
        Assert.Equal(new PointF(2, 2), p.LayerPivotPixel(layer, 1));

        // 레이어 전체
        var w = Whole(p, 0);
        var wid = w.Box.Identity;
        Assert.True(w.Commit("변형", wid with { CenterX = wid.CenterX + 1, CenterY = wid.CenterY + 2 }, ResampleMode.Nearest, null, pivotSlip: (-1, -2)));
        Assert.Equal(Red, p.GetCel(0, 0)![3, 4]);
        Assert.Equal(new PointF(2, 2), p.LayerPivotPixel(layer, 0));
        Assert.Equal(new PointF(2, 2), p.LayerPivotPixel(layer, 1));
    }

    [Fact]
    public void PivotSlip_ShiftOnly_KeepsThePivotInPlace_Part()
    {
        var p = new SpriteProject(10, 10);
        var g = p.AddGroup();
        var part = p.AddPart(g, "팔", 2, 2, new Point(1, 1));
        int pi = p.IndexOf(part);
        p.AddFrame();
        Draw(p, pi, 0, 0, 0, Red);
        Draw(p, pi, 1, 0, 0, Red);
        p.SetLayerPivot(pi, Pivot.FromLocalPixel(1, 1, 2, 2)); // 캔버스 (2,2)

        // 지금 셀: 그 프레임의 부위만 옮기고 피벗은 캔버스 제자리
        var t = Cel(p, pi, 1);
        var id = t.Box.Identity;
        Assert.True(t.Commit("변형", id with { CenterX = id.CenterX + 3, CenterY = id.CenterY + 2 }, ResampleMode.Nearest, null, pivotSlip: (-3, -2)));
        Assert.Equal(new Point(4, 3), p.GetPartOffset(part, 1));
        Assert.Equal(new PointF(2, 2), p.LayerPivotPixel(part, 1));
        Assert.Equal(new PointF(2, 2), p.LayerPivotPixel(part, 0));

        // 레이어 전체: 모든 프레임의 부위를 옮겨도 피벗은 제자리
        var w = Whole(p, pi);
        var wid = w.Box.Identity;
        Assert.True(w.Commit("변형", wid with { CenterX = wid.CenterX + 1, CenterY = wid.CenterY - 1 }, ResampleMode.Nearest, null, pivotSlip: (-1, 1)));
        Assert.Equal(new Point(2, 0), p.GetPartOffset(part, 0));
        Assert.Equal(new Point(5, 2), p.GetPartOffset(part, 1));
        Assert.Equal(new PointF(2, 2), p.LayerPivotPixel(part, 0));
        Assert.Equal(new PointF(2, 2), p.LayerPivotPixel(part, 1));
    }

    [Fact]
    public void PivotSlip_Default_PivotStillFollowsTheDrawing_Part()
    {
        var p = new SpriteProject(10, 10);
        var g = p.AddGroup();
        var part = p.AddPart(g, "팔", 2, 2, new Point(1, 1));
        int pi = p.IndexOf(part);
        Draw(p, pi, 0, 0, 0, Red);
        p.SetLayerPivot(pi, Pivot.FromLocalPixel(1, 1, 2, 2));

        var t = Cel(p, pi, 0);
        var id = t.Box.Identity;
        Assert.True(t.Commit("변형", id with { CenterX = id.CenterX + 3, CenterY = id.CenterY + 2 }, ResampleMode.Nearest, null));
        Assert.Equal(new PointF(5, 4), p.LayerPivotPixel(part, 0));
    }

    [Fact]
    public void Clip_FreeMarginShape_CropsPixelsOutsideTheShape_EvenWithoutMoving()
    {
        var p = new SpriteProject(4, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                Draw(p, 0, 0, x, y, Red);
        var t = Cel(p, 0, 0);
        // 오른쪽 위 꼭짓점을 왼쪽으로 끌어 든 사다리꼴: 오른쪽 위 삼각형이 모양 밖
        t.Clip = PixelMask.CentersInside([(0, 0), (2, 0), (4, 4), (0, 4)]);
        Assert.NotNull(t.CutPixels(t.Targets[0]));
        Assert.True(t.Commit("변형", t.Box.Identity, ResampleMode.Nearest, null));

        var cel = p.GetCel(0, 0)!;
        Assert.Equal(Red, cel[0, 0]);
        Assert.Equal(Red, cel[3, 3]);
        Assert.Equal(default, cel[3, 0]);
        Assert.Equal(default, cel[2, 0]);
    }
}
