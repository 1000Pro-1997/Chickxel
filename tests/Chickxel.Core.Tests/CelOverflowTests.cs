using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>일반 레이어 셀의 캔버스 밖 그림: 옮기기·내려놓기·캔버스 크기·복제·합치기·저장·실행취소 기록에서 남는지.</summary>
public class CelOverflowTests
{
    private static readonly Color32 Red = new(255, 0, 0);

    private static void Draw(SpriteProject p, int x, int y, Color32 color, int layer = 0, int frame = 0)
    {
        var img = p.BeginCelStroke(layer, frame);
        img.Set(x, y, color);
        Assert.True(p.CommitCelStroke("연필"));
    }

    /// <summary>캔버스 좌표 (x, y)의 셀 전체(캔버스 안 + 밖) 색.</summary>
    private static Color32 FullAt(SpriteProject p, int x, int y, int layer = 0, int frame = 0)
    {
        var (image, origin) = p.GetFullCel(layer, frame);
        int ix = x - origin.X, iy = y - origin.Y;
        return ix < 0 || iy < 0 || ix >= image.Width || iy >= image.Height ? default : image[ix, iy];
    }

    [Fact]
    public void Shift_PixelsLeavingCanvasStay_AndComeBack_WithUndo()
    {
        var p = new SpriteProject(4, 4);
        Draw(p, 3, 1, Red);

        Assert.Equal(1, p.ShiftCels("옮기기", [(0, 0)], 2, 0));
        Assert.True(p.IsCelEmpty(0, 0));
        var (image, origin) = p.GetCelOverflow(0, 0)!.Value;
        Assert.Equal((new Point(5, 1), new Size(1, 1), Red), (origin, image.Size, image[0, 0]));

        Assert.Equal(1, p.ShiftCels("옮기기", [(0, 0)], -2, 0));
        Assert.Equal(Red, p.GetCel(0, 0)![3, 1]);
        Assert.Null(p.GetCelOverflow(0, 0));

        p.Undo();
        Assert.True(p.IsCelEmpty(0, 0));
        Assert.Equal(Red, FullAt(p, 5, 1));
        p.Undo();
        Assert.Equal(Red, p.GetCel(0, 0)![3, 1]);
        Assert.Null(p.GetCelOverflow(0, 0));
        p.Redo();
        Assert.Equal(Red, FullAt(p, 5, 1));
    }

    [Fact]
    public void Anchor_ImageLargerThanCanvas_KeepsWholeImage_InOneUndoStep()
    {
        var p = new SpriteProject(4, 4);
        var big = TestUtil.RandomImage(8, 6, opaque: true);
        var selection = new Selection();
        var cel = p.BeginCelStroke(0, 0);
        selection.AnchoringOutside = (image, floating, at) => p.AddStrokeOverflow(floating, at);
        selection.Paste(cel, big, new Point(-2, -1));
        selection.Deselect(cel);
        int steps = p.History.UndoCount;
        Assert.True(p.CommitCelStroke("붙여넣기"));
        Assert.Equal(steps + 1, p.History.UndoCount);

        var (full, origin) = p.GetFullCel(0, 0);
        Assert.Equal(new Point(-2, -1), origin);
        Assert.Equal(big.Pixels, full.Pixels);

        p.Undo();
        Assert.True(p.IsCelEmpty(0, 0));
        Assert.Null(p.GetCelOverflow(0, 0));
    }

    [Fact]
    public void CancelledStroke_DropsOutsidePixels()
    {
        var p = new SpriteProject(4, 4);
        p.BeginCelStroke(0, 0);
        Assert.True(p.AddStrokeOverflow(TestUtil.RandomImage(2, 2, opaque: true), new Point(5, 5)));
        Assert.NotNull(p.GetCelOverflow(0, 0));
        p.CancelCelStroke();
        Assert.Null(p.GetCelOverflow(0, 0));
    }

    [Fact]
    public void PaintOutside_CreatesOverflow_WithCanvasPixels_InOneUndoStep()
    {
        var p = new SpriteProject(4, 4);
        var blue = new Color32(0, 0, 255);
        int steps = p.History.UndoCount;
        var cel = p.BeginCelStroke(0, 0);
        cel.Set(3, 1, Red);
        Assert.True(p.SetStrokeOutside(4, 1, Red));
        Assert.True(p.SetStrokeOutside(6, -2, blue));
        Assert.False(p.SetStrokeOutside(4, 1, Red)); // 같은 색
        Assert.False(p.SetStrokeOutside(2, 2, Red)); // 캔버스 안은 셀에
        Assert.False(p.SetStrokeOutside(p.OverflowBounds.Left - 1, 0, Red)); // 남기는 범위 밖
        Assert.Equal(Red, p.GetStrokeOutside(4, 1));
        Assert.Equal(default, p.GetStrokeOutside(4, 1, before: true));
        // 확정 전: 칠한 것은 화면용 작업 그림에만
        Assert.Null(p.GetCelOverflow(0, 0));
        var (live, liveOrigin) = p.GetCelOverflowLive(0, 0)!.Value;
        Assert.Equal(blue, live[6 - liveOrigin.X, -2 - liveOrigin.Y]);

        Assert.True(p.CommitCelStroke("연필"));
        Assert.Equal(steps + 1, p.History.UndoCount);
        var (image, origin) = p.GetCelOverflow(0, 0)!.Value;
        Assert.Equal((new Point(4, -2), new Size(3, 4)), (origin, image.Size)); // 칠한 픽셀을 꼭 맞게 감쌈
        Assert.Equal((Red, Red, blue), (FullAt(p, 3, 1), FullAt(p, 4, 1), FullAt(p, 6, -2)));

        p.Undo();
        Assert.True(p.IsCelEmpty(0, 0));
        Assert.Null(p.GetCelOverflow(0, 0));
        p.Redo();
        Assert.Equal((Red, Red, blue), (FullAt(p, 3, 1), FullAt(p, 4, 1), FullAt(p, 6, -2)));
    }

    [Fact]
    public void EraseOutsideToEmpty_RemovesOverflow_UndoBringsItBack()
    {
        var p = new SpriteProject(4, 4);
        Draw(p, 3, 1, Red);
        p.ShiftCels("옮기기", [(0, 0)], 2, 0); // (5, 1)
        var cel = p.GetCel(0, 0);

        p.BeginCelStroke(0, 0);
        Assert.Equal(Red, p.GetStrokeOutside(5, 1));
        Assert.True(p.SetStrokeOutside(5, 1, Color32.Transparent));
        Assert.True(p.CommitCelStroke("지우개"));
        Assert.Null(p.GetCelOverflow(0, 0));
        Assert.Same(cel, p.GetCel(0, 0)); // 캔버스 안은 그대로라 셀 그림도 그대로

        p.Undo();
        Assert.Equal(Red, FullAt(p, 5, 1));
        p.Redo();
        Assert.Null(p.GetCelOverflow(0, 0));
    }

    [Fact]
    public void ResetOrCancelOutside_LeavesNothing()
    {
        var p = new SpriteProject(4, 4);
        int steps = p.History.UndoCount;
        p.BeginCelStroke(0, 0);
        Assert.True(p.SetStrokeOutside(-1, 0, Red));
        p.ResetStrokeOutside(); // 도형을 다시 그리듯 시작 때로
        Assert.Equal(default, p.GetStrokeOutside(-1, 0));
        Assert.False(p.CommitCelStroke("선"));
        Assert.Equal(steps, p.History.UndoCount);
        Assert.Null(p.GetCelOverflow(0, 0));

        p.BeginCelStroke(0, 0);
        Assert.True(p.SetStrokeOutside(-1, 0, Red));
        p.CancelCelStroke();
        Assert.Null(p.GetCelOverflowLive(0, 0));
        Assert.Equal(steps, p.History.UndoCount);
    }

    [Fact]
    public void PaintOutside_LinkedCelsShareResult()
    {
        var p = new SpriteProject(4, 4);
        p.AddFrame();
        Draw(p, 0, 0, Red);
        p.LinkCels([(0, 0), (0, 1)]);

        p.BeginCelStroke(0, 0);
        Assert.True(p.SetStrokeOutside(-2, 0, Red));
        Assert.True(p.CommitCelStroke("연필"));
        Assert.True(p.AreCelsLinked(0, 0, 1));
        Assert.Equal((Red, Red), (FullAt(p, -2, 0, frame: 0), FullAt(p, -2, 0, frame: 1)));

        p.Undo();
        Assert.True(p.AreCelsLinked(0, 0, 1));
        Assert.Null(p.GetCelOverflow(0, 1));
    }

    [Fact]
    public void PaintOutside_OnlyNormalLayers()
    {
        var p = new SpriteProject(8, 8);
        var g = p.AddGroup("G");
        var a = p.AddPart(g, "A", 2, 2, new Point(0, 0));
        p.BeginCelStroke(p.IndexOf(a), 0);
        Assert.False(p.SetStrokeOutside(-1, 0, Red));
        p.CancelCelStroke();
    }

    [Fact]
    public void CanvasShrinkThenGrow_RestoresImage()
    {
        var p = new SpriteProject(4, 4);
        var original = TestUtil.RandomImage(4, 4, opaque: true);
        p.SetCel(0, 0, original);

        p.ResizeCanvas(2, 2, Anchor.Center);
        Assert.Equal(original[1, 1], p.GetCel(0, 0)![0, 0]);
        Assert.NotNull(p.GetCelOverflow(0, 0));

        p.ResizeCanvas(4, 4, Anchor.Center);
        Assert.Equal(original.Pixels, p.GetCel(0, 0)!.Pixels);
        Assert.Null(p.GetCelOverflow(0, 0));
    }

    [Fact]
    public void CanvasScale_ScalesInsideAndOutsideWithOneMapping()
    {
        // 8×3 그림을 3×3 캔버스로: 캔버스 밖 왼쪽 2열, 오른쪽 3열
        var p = new SpriteProject(8, 3);
        var original = TestUtil.RandomImage(8, 3, opaque: true);
        p.SetCel(0, 0, original);
        p.ResizeCanvasAndScale(3, 3, new Rectangle(-2, 0, 8, 3));

        // 3 → 4배율: 캔버스 안·밖 모두 새 칸 d가 원래 칸 floor(d·3/4)를 집는다(밖을 따로 늘여 이음매가 생기지 않음)
        p.ResizeCanvasAndScale(4, 4, new Rectangle(0, 0, 4, 4));
        for (int y = 0; y < 4; y++)
            for (int x = -4; x < 9; x++)
            {
                int sx = (int)Math.Floor(x * 3 / 4.0) + 2, sy = y * 3 / 4;
                var expected = sx >= 0 && sx < original.Width ? original[sx, sy] : default;
                Assert.True(expected == FullAt(p, x, y), $"({x}, {y})");
            }
    }

    [Fact]
    public void CanvasResize_KeepShape_ErasesEverythingOutsideIt_IncludingOffCanvas_OneUndoStep()
    {
        var p = new SpriteProject(4, 4);
        p.SetCel(0, 0, TestUtil.RandomImage(4, 4, opaque: true));
        Draw(p, 0, 0, Red);
        // 캔버스 밖 그림 하나: 오른쪽 위 밖
        p.ResizeCanvasAndScale(3, 4, new Rectangle(0, 0, 4, 4));
        Assert.NotEqual(default, FullAt(p, 3, 0));
        int steps = p.History.UndoCount;

        // 같은 크기, 오른쪽 위 꼭짓점을 왼쪽으로 옮긴 사다리꼴로 자르기: 오른쪽 위 삼각형과 캔버스 밖은 지워짐
        p.ResizeCanvasAndScale(3, 4, new Rectangle(0, 0, 3, 4), keep: PixelMask.CentersInside([(0, 0), (1, 0), (3, 4), (0, 4)]));

        Assert.Equal(steps + 1, p.History.UndoCount);
        Assert.Equal(Red, FullAt(p, 0, 0));
        Assert.Equal(default, FullAt(p, 2, 0));
        Assert.Equal(default, FullAt(p, 3, 0));
        Assert.NotEqual(default, FullAt(p, 2, 3));
        p.Undo();
        Assert.NotEqual(default, FullAt(p, 3, 0));
    }

    [Fact]
    public void DuplicateMergeAndFlip_CarryOutsidePixels()
    {
        var p = new SpriteProject(4, 4);
        Draw(p, 0, 0, Red);
        p.ShiftCels("옮기기", [(0, 0)], -3, 0); // (−3, 0)

        p.DuplicateLayer(0);
        Assert.Equal(Red, FullAt(p, -3, 0, layer: 1));
        p.DuplicateFrame(0);
        Assert.Equal(Red, FullAt(p, -3, 0, frame: 1));

        // 위 레이어(복제본)를 한 칸 옮겨 아래로 합치면 두 픽셀 모두 캔버스 밖에 남는다.
        p.ShiftCels("옮기기", [(1, 0)], 0, -1);
        p.MergeLayerDown(1);
        Assert.Equal((Red, Red), (FullAt(p, -3, 0), FullAt(p, -3, -1)));

        p.FlipCanvasHorizontal(); // x → 3 − x
        Assert.Equal((Red, Red), (FullAt(p, 6, 0), FullAt(p, 6, -1)));
    }

    [Fact]
    public void SaveLoadAndHistory_KeepOutsidePixels()
    {
        using var tmp = new TempDir();
        var file = tmp.File("a.chickxel");
        var p = new SpriteProject(4, 4);
        Draw(p, 1, 1, Red);
        p.ShiftCels("옮기기", [(0, 0)], 10, -2);
        ProjectIO.Save(p, file);
        var dir = tmp.File("history");
        UndoHistoryIO.Write(UndoHistoryIO.Capture(p, 1000)!, dir);

        var q = ProjectIO.Load(file);
        Assert.Equal(Red, FullAt(q, 11, -1));
        Assert.Equal(p.GetCelOverflow(0, 0)!.Value.Origin, q.GetCelOverflow(0, 0)!.Value.Origin);
        Assert.Equal(2, UndoHistoryIO.Read(q, dir));
        q.Undo();
        Assert.Equal(Red, q.GetCel(0, 0)![1, 1]);
        Assert.Null(q.GetCelOverflow(0, 0));
        q.Redo();
        Assert.Equal(Red, FullAt(q, 11, -1));
    }
}
