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
