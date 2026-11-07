using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>캔버스 크기 모드의 특수 비율: 새 캔버스·그림 사각형 계산.</summary>
public class CanvasRatioTests
{
    [Fact]
    public void TargetSize_KeepsOneSide_AndRoundsHalfUp()
    {
        Assert.Equal(new Size(32, 18), CanvasRatio.TargetSize(new Size(32, 32), 16, 9, CanvasRatioBasis.KeepWidth));
        Assert.Equal(new Size(48, 32), CanvasRatio.TargetSize(new Size(20, 32), 3, 2, CanvasRatioBasis.KeepHeight));
        // 10 × 9 / 16 = 5.625 → 6, 6 × 2 / 3 = 4, 5 × 2 / 4 = 2.5 → 3(.5는 올림)
        Assert.Equal(new Size(10, 6), CanvasRatio.TargetSize(new Size(10, 10), 16, 9, CanvasRatioBasis.KeepWidth));
        Assert.Equal(new Size(4, 6), CanvasRatio.TargetSize(new Size(9, 6), 2, 3, CanvasRatioBasis.KeepHeight));
        Assert.Equal(new Size(5, 3), CanvasRatio.TargetSize(new Size(5, 1), 4, 2, CanvasRatioBasis.KeepWidth));
    }

    [Fact]
    public void TargetSize_OutOfRange_IsNull()
    {
        // 1 × 1 / 100 = 0.01 → 0: 1px보다 작다
        Assert.Null(CanvasRatio.TargetSize(new Size(1, 1), 100, 1, CanvasRatioBasis.KeepWidth));
        // 최대 크기를 넘는다
        Assert.Null(CanvasRatio.TargetSize(new Size(PixelImage.MaxSize, PixelImage.MaxSize), 1, 2, CanvasRatioBasis.KeepWidth));
        Assert.Equal(new Size(PixelImage.MaxSize, PixelImage.MaxSize), CanvasRatio.TargetSize(new Size(PixelImage.MaxSize, 10), 1, 1, CanvasRatioBasis.KeepWidth));
        // 비율 항은 1~100
        Assert.Null(CanvasRatio.TargetSize(new Size(10, 10), 0, 1, CanvasRatioBasis.KeepWidth));
        Assert.Null(CanvasRatio.TargetSize(new Size(10, 10), 1, 101, CanvasRatioBasis.KeepWidth));
        Assert.Null(CanvasRatio.Plan(new Rectangle(0, 0, 1, 1), new Rectangle(0, 0, 1, 1), 100, 1, CanvasRatioBasis.KeepWidth, CanvasRatioFit.Canvas, Anchor.Center));
    }

    [Fact]
    public void Canvas_ExactRatioAlready_ChangesNothing()
    {
        var canvas = new Rectangle(0, 0, 32, 18);
        var image = new Rectangle(3, -2, 20, 20); // 기준점에 맞지 않는 자리
        Assert.Equal((canvas, image), CanvasRatio.Plan(canvas, image, 16, 9, CanvasRatioBasis.KeepWidth, CanvasRatioFit.Canvas, null));
        var centered = new Rectangle(6, -1, 20, 20);
        Assert.Equal((canvas, centered), CanvasRatio.Plan(canvas, centered, 16, 9, CanvasRatioBasis.KeepHeight, CanvasRatioFit.Canvas, Anchor.Center));
    }

    [Theory]
    // 32×32 → 32×18(14px 잘림): 위 기준이면 아래가, 가운데면 양쪽 7px씩, 아래면 위가 잘린다.
    [InlineData(Anchor.TopLeft, 0, 0)]
    [InlineData(Anchor.Center, -7, 7)]
    [InlineData(Anchor.BottomRight, -14, 14)]
    public void Canvas_Crop_PlacesPictureByAnchor_AndKeepsItOnScreen(Anchor anchor, int imageY, int canvasY)
    {
        var canvas = new Rectangle(0, 0, 32, 32);
        var image = new Rectangle(0, 0, 32, 32);
        var plan = CanvasRatio.Plan(canvas, image, 16, 9, CanvasRatioBasis.KeepWidth, CanvasRatioFit.Canvas, anchor)!.Value;
        Assert.Equal(new Rectangle(0, canvasY, 32, 18), plan.Canvas);
        Assert.Equal(new Rectangle(0, imageY, 32, 32), plan.Image);
        // 그림의 원래 프레임 위치(캔버스 원점 + 그림 자리)는 그대로
        Assert.Equal(0, plan.Canvas.Y + plan.Image.Y);
        // 가로가 같아 같은 줄의 기준점은 모두 맞는다: 그 줄(위·가운데·아래)이 기준점과 같다.
        Assert.Equal((int)anchor / 3, (int)CanvasRatio.MatchAnchor(plan.Canvas.Size, plan.Image)! / 3);
    }

    [Theory]
    // 32×18 → 32×32(14px 늘어남): 빈칸이 기준점 반대쪽에 붙는다.
    [InlineData(Anchor.TopCenter, 0, 0)]
    [InlineData(Anchor.MiddleLeft, 7, -7)]
    [InlineData(Anchor.BottomLeft, 14, -14)]
    public void Canvas_Extend_PlacesPictureByAnchor(Anchor anchor, int imageY, int canvasY)
    {
        var canvas = new Rectangle(0, 0, 32, 18);
        var image = new Rectangle(0, 0, 32, 18);
        var plan = CanvasRatio.Plan(canvas, image, 1, 1, CanvasRatioBasis.KeepWidth, CanvasRatioFit.Canvas, anchor)!.Value;
        Assert.Equal(new Rectangle(0, canvasY, 32, 32), plan.Canvas);
        Assert.Equal(new Rectangle(0, imageY, 32, 18), plan.Image);
    }

    [Fact]
    public void Canvas_KeepHeight_ChangesOnlyWidth()
    {
        var canvas = new Rectangle(0, 0, 16, 16);
        var image = new Rectangle(0, 0, 16, 16);
        var plan = CanvasRatio.Plan(canvas, image, 16, 9, CanvasRatioBasis.KeepHeight, CanvasRatioFit.Canvas, Anchor.TopRight)!.Value;
        // 16 × 16 / 9 = 28.44 → 28: 오른쪽 기준이면 왼쪽에 12px
        Assert.Equal(new Rectangle(-12, 0, 28, 16), plan.Canvas);
        Assert.Equal(new Rectangle(12, 0, 16, 16), plan.Image);
    }

    [Fact]
    public void Canvas_NoAnchor_GrowsBothSidesAndKeepsPictureOffset()
    {
        // 그림이 기준점에 맞지 않으면(옮겨 둔 자리) 캔버스가 양쪽으로 같게 늘고, 그림과 캔버스 위쪽 거리는 늘어난 길이의 반(5px)만큼 늘어난다.
        var canvas = new Rectangle(2, 3, 20, 10);
        var image = new Rectangle(1, 2, 8, 5);
        var plan = CanvasRatio.Plan(canvas, image, 1, 1, CanvasRatioBasis.KeepWidth, CanvasRatioFit.Canvas, null)!.Value;
        Assert.Equal(new Rectangle(2, -2, 20, 20), plan.Canvas);
        Assert.Equal(new Rectangle(1, 7, 8, 5), plan.Image);
        Assert.Equal(canvas.Y + image.Y, plan.Canvas.Y + plan.Image.Y);
    }

    [Fact]
    public void Stretch_FillsNewCanvas_AnchoredCanvasMove()
    {
        var canvas = new Rectangle(0, 0, 32, 32);
        var image = new Rectangle(4, 4, 24, 24);
        var center = CanvasRatio.Plan(canvas, image, 16, 9, CanvasRatioBasis.KeepWidth, CanvasRatioFit.Stretch, null)!.Value;
        Assert.Equal(new Rectangle(0, 7, 32, 18), center.Canvas);
        Assert.Equal(new Rectangle(0, 0, 32, 18), center.Image);
        var top = CanvasRatio.Plan(canvas, image, 16, 9, CanvasRatioBasis.KeepWidth, CanvasRatioFit.Stretch, Anchor.TopLeft)!.Value;
        Assert.Equal(new Rectangle(0, 0, 32, 18), top.Canvas);
        var tall = CanvasRatio.Plan(new Rectangle(0, 0, 30, 20), image, 1, 1, CanvasRatioBasis.KeepHeight, CanvasRatioFit.Stretch, Anchor.MiddleRight)!.Value;
        Assert.Equal(new Rectangle(10, 0, 20, 20), tall.Canvas);
        Assert.Equal(new Rectangle(0, 0, 20, 20), tall.Image);
    }

    [Fact]
    public void MatchAnchor_FindsExactPositionOnly()
    {
        Assert.Equal(Anchor.Center, CanvasRatio.MatchAnchor(new Size(10, 10), new Rectangle(1, 1, 8, 8)));
        Assert.Equal(Anchor.BottomRight, CanvasRatio.MatchAnchor(new Size(10, 10), new Rectangle(2, 2, 8, 8)));
        Assert.Null(CanvasRatio.MatchAnchor(new Size(10, 10), new Rectangle(1, 2, 7, 7)));
        // 크기가 같으면 모두 맞는다: 고른 기준점을 먼저, 없으면 첫 기준점(왼쪽 위)
        Assert.Equal(Anchor.Center, CanvasRatio.MatchAnchor(new Size(10, 10), new Rectangle(0, 0, 10, 10), Anchor.Center));
        Assert.Equal(Anchor.TopLeft, CanvasRatio.MatchAnchor(new Size(10, 10), new Rectangle(0, 0, 10, 10)));
        // 고른 기준점이 맞지 않으면 맞는 다른 기준점
        Assert.Equal(Anchor.Center, CanvasRatio.MatchAnchor(new Size(10, 10), new Rectangle(1, 1, 8, 8), Anchor.TopLeft));
    }
}
