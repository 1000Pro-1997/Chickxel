using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>
/// 변형 모드의 픽셀 엔진(<see cref="SpriteProject.LayerTransform"/>): 지금 셀(Ctrl+T)·레이어 전체(Ctrl+Shift+C), 선택 영역, 여백, 피벗 따라가기, 부위 영역.
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
        => SpriteProject.LayerTransform.Create(p, layer, frame, [], selection, wholeLayer: true)!;

    private static SpriteProject.LayerTransform Cel(SpriteProject p, int layer, int frame, PixelMask? selection = null)
        => SpriteProject.LayerTransform.Create(p, layer, frame, [], selection, wholeLayer: false)!;

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
}
