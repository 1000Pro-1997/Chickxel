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

    [Fact]
    public void ScaleLayer_ScalesEveryFrameOfOneLayer_KeepsCanvasAndOtherLayers_PushedOffPixelsStay()
    {
        var p = new SpriteProject(6, 4);
        var green = new Color32(0, 255, 0);
        var blue = new Color32(0, 0, 255);
        Draw(p, 0, 0, green);
        p.AddLayer();
        p.AddFrame();
        Draw(p, 2, 1, Red, layer: 1);
        Draw(p, 3, 1, blue, layer: 1);
        Draw(p, 2, 2, Red, layer: 1, frame: 1);
        Assert.Equal(new Rectangle(2, 1, 2, 2), p.LayerBounds(1));
        int steps = p.History.UndoCount;

        p.ScaleLayer(1, new Rectangle(2, 1, 2, 2), new Rectangle(2, 1, 4, 4));

        Assert.Equal(steps + 1, p.History.UndoCount);
        Assert.Equal(new Size(6, 4), p.Size);
        Assert.Equal(green, p.GetCel(0, 0)![0, 0]);
        foreach (var (x, y) in new[] { (2, 1), (3, 2) }) Assert.Equal(Red, FullAt(p, x, y, layer: 1));
        foreach (var (x, y) in new[] { (4, 1), (5, 2) }) Assert.Equal(blue, FullAt(p, x, y, layer: 1));
        Assert.Equal(default, FullAt(p, 2, 3, layer: 1));
        // 프레임 1: (2,2) → (2..3, 3..4) — 아래 줄은 캔버스 밖 그림으로 남는다.
        Assert.Equal(Red, FullAt(p, 3, 3, layer: 1, frame: 1));
        Assert.Equal(Red, FullAt(p, 2, 4, layer: 1, frame: 1));
        Assert.NotNull(p.GetCelOverflow(1, 1));
        Assert.Equal(new Rectangle(2, 1, 4, 4), p.LayerBounds(1));

        p.Undo();
        Assert.Equal(Red, FullAt(p, 2, 1, layer: 1));
        Assert.Equal(blue, FullAt(p, 3, 1, layer: 1));
        Assert.Equal(default, FullAt(p, 3, 2, layer: 1));
        Assert.Null(p.GetCelOverflow(1, 1));
        Assert.Throws<ArgumentException>(() => p.ScaleLayer(p.IndexOf(p.AddGroup()), new Rectangle(0, 0, 1, 1), new Rectangle(0, 0, 2, 2)));
    }

    [Fact]
    public void ResizeLayer_NormalLayer_CropsPixelsOutsideTheArea_InEveryFrameAndOffCanvas()
    {
        var p = new SpriteProject(6, 4);
        var blue = new Color32(0, 0, 255);
        p.AddLayer();
        p.AddFrame();
        Draw(p, 1, 1, Red, layer: 1);
        Draw(p, 4, 2, blue, layer: 1);
        Draw(p, 4, 1, blue, layer: 1, frame: 1);
        p.ShiftCels("옮기기", [(1, 1)], 2, 0); // 프레임 1의 파랑 (6,1) — 캔버스 밖
        var from = p.LayerBounds(1);
        int steps = p.History.UndoCount;

        // 자리를 왼쪽 반(0..2)으로: 오른쪽 그림은 모든 프레임에서 잘림
        p.ResizeLayer(1, from, from, new Rectangle(0, 0, 3, 4));

        Assert.Equal(steps + 1, p.History.UndoCount);
        Assert.Equal(Red, FullAt(p, 1, 1, layer: 1));
        Assert.Equal(default, FullAt(p, 4, 2, layer: 1));
        Assert.Equal(default, FullAt(p, 6, 1, layer: 1, frame: 1));
        Assert.Null(p.GetCelOverflow(1, 1));
        Assert.Equal(new Rectangle(1, 1, 1, 1), p.LayerBounds(1));

        p.Undo();
        Assert.Equal(blue, FullAt(p, 4, 2, layer: 1));
        Assert.Equal(blue, FullAt(p, 6, 1, layer: 1, frame: 1));

        // 자리를 넓히기만 하면 그림은 그대로(기록 없음이 아니라 같은 그림)
        p.ResizeLayer(1, p.LayerBounds(1), p.LayerBounds(1), new Rectangle(-2, -2, 12, 8));
        Assert.Equal(blue, FullAt(p, 4, 2, layer: 1));
        Assert.Equal(Red, FullAt(p, 1, 1, layer: 1));
    }

    [Fact]
    public void ResizeLayer_NormalLayer_MovingOrScalingTheImage_MovesFramePivotsAlong()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        Draw(p, 2, 2, Red);
        p.Sprite.DefaultPivot = Pivot.FromLocalPixel(2, 2, 8, 8);
        p.SetFramePivot(1, Pivot.FromLocalPixel(3, 3, 8, 8));
        var from = new Rectangle(2, 2, 1, 1);

        // (2,2) → (5,4)로 옮기기: 기본·프레임 피벗이 같은 거리만큼
        p.ResizeLayer(0, from, new Rectangle(5, 4, 1, 1), new Rectangle(5, 4, 1, 1));
        Assert.Equal((5f, 4f), p.GetFramePivot(0).ToLocalPixel(8, 8));
        Assert.Equal((6f, 5f), p.GetFramePivot(1).ToLocalPixel(8, 8));
        p.Undo();
        Assert.Equal((2f, 2f), p.GetFramePivot(0).ToLocalPixel(8, 8));

        // 자리만 바꾸면(그림 그대로) 피벗도 그대로
        p.ResizeLayer(0, from, from, new Rectangle(0, 0, 4, 4));
        Assert.Equal((3f, 3f), p.GetFramePivot(1).ToLocalPixel(8, 8));
    }
}
