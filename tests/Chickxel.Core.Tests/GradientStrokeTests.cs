using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class GradientStrokeTests
{
    private static readonly GradientPreset BlackWhite = new()
    {
        Stops = [new GradientStop(0, Color32.Black), new GradientStop(1, Color32.White)],
        AlphaStops = [new GradientAlphaStop(0, 255), new GradientAlphaStop(1, 255)],
        ColorSpace = GradientColorSpace.Rgb,
    };

    /// <summary>한 픽셀 붓으로 꺾은선을 따라 그린다(시작점 포함).</summary>
    private static GradientStroke Draw(Point start, params Point[] corners)
    {
        var stroke = new GradientStroke(start);
        stroke.Record([start], 0);
        foreach (var corner in corners)
            foreach (var (center, distance) in stroke.LineTo(corner))
                stroke.Record([center], distance);
        return stroke;
    }

    [Fact]
    public void WholeStroke_RunsFromStartToEnd_ByLengthAlongCorneredPath()
    {
        var stroke = Draw(new Point(0, 0), new Point(10, 0), new Point(10, 10));
        Assert.Equal(20, stroke.Length, 6);

        var image = new PixelImage(11, 11);
        stroke.Render(image, BlackWhite, GradientStrokeMode.WholeStroke, GradientStroke.DefaultInterval);

        Assert.Equal(Color32.Black, image.Get(0, 0));
        Assert.Equal(Color32.White, image.Get(10, 10));
        // 꺾인 곳은 그린 길이의 절반(곧은 거리로 재면 절반이 아니다)
        Assert.InRange(image.Get(10, 0).R, 126, 129);
        Assert.Equal(image.Get(10, 0).R, image.Get(10, 0).G);
        // 그린 순서대로 밝아진다
        var path = Enumerable.Range(0, 11).Select(x => new Point(x, 0)).Concat(Enumerable.Range(1, 10).Select(y => new Point(10, y))).ToList();
        for (int i = 1; i < path.Count; i++)
            Assert.True(image.Get(path[i].X, path[i].Y).R > image.Get(path[i - 1].X, path[i - 1].Y).R);
        // 칠하지 않은 곳은 그대로
        Assert.Equal(Color32.Transparent, image.Get(5, 5));
    }

    [Fact]
    public void LineTo_MeasuresDiagonalStepsByProjection_NotStepCount()
    {
        var stroke = new GradientStroke(new Point(0, 0));
        var centers = stroke.LineTo(new Point(10, 5));
        Assert.Equal(Math.Sqrt(125), stroke.Length, 6);
        Assert.Equal(10, centers.Count);
        Assert.Equal(new Point(10, 5), centers[^1].Center);
        Assert.Equal(stroke.Length, centers[^1].Distance, 6);
        for (int i = 1; i < centers.Count; i++)
            Assert.True(centers[i].Distance >= centers[i - 1].Distance);
        // 이어 그린 구간은 앞 길이에서 이어진다
        var more = stroke.LineTo(new Point(10, 8));
        Assert.Equal(Math.Sqrt(125) + 1, more[0].Distance, 6);
        Assert.Empty(stroke.LineTo(new Point(10, 8)));
    }

    [Fact]
    public void Repeat_WrapsEveryInterval()
    {
        Assert.Equal(0, GradientStroke.RepeatT(0, 8));
        Assert.Equal(0.5, GradientStroke.RepeatT(4, 8), 9);
        Assert.Equal(0, GradientStroke.RepeatT(8, 8), 9);
        Assert.Equal(0.5, GradientStroke.RepeatT(12, 8), 9);
        Assert.Equal(0.25, GradientStroke.RepeatT(18, 8), 9);

        var stroke = Draw(new Point(0, 0), new Point(20, 0));
        var image = new PixelImage(21, 1);
        stroke.Render(image, BlackWhite, GradientStrokeMode.Repeat, 8);
        Assert.Equal(Color32.Black, image.Get(0, 0));
        Assert.Equal(Color32.Black, image.Get(8, 0));
        Assert.Equal(Color32.Black, image.Get(16, 0));
        Assert.Equal(image.Get(4, 0), image.Get(12, 0));
        Assert.True(image.Get(7, 0).R > image.Get(4, 0).R);
        // 반복은 획 길이와 상관없다: 더 그려도 앞 색은 그대로
        var before = image.Get(5, 0);
        foreach (var (center, distance) in stroke.LineTo(new Point(20, 9))) stroke.Record([center], distance);
        stroke.Render(image, BlackWhite, GradientStrokeMode.Repeat, 8);
        Assert.Equal(before, image.Get(5, 0));
    }

    [Fact]
    public void SingleClick_HasZeroLength_AndTakesFirstColor()
    {
        var redBlue = BlackWhite with { Stops = [new GradientStop(0, new Color32(255, 0, 0)), new GradientStop(1, new Color32(0, 0, 255))] };
        var stroke = Draw(new Point(2, 3));
        Assert.Equal(0, stroke.Length);
        Assert.Equal(0, GradientStroke.WholeT(0, 0));
        var image = new PixelImage(5, 5);
        stroke.Render(image, redBlue, GradientStrokeMode.WholeStroke, GradientStroke.DefaultInterval);
        Assert.Equal(new Color32(255, 0, 0), image.Get(2, 3));
    }

    [Fact]
    public void WideBrush_ColorsEachPixelByTheLastCenterThatCoveredIt()
    {
        var stroke = new GradientStroke(new Point(0, 2));
        stroke.Record(Raster.Brush(0, 2, 3), 0);
        foreach (var (center, distance) in stroke.LineTo(new Point(8, 2)))
            stroke.Record(Raster.Brush(center.X, center.Y, 3), distance);

        // 3×3 도장: 픽셀 x를 마지막으로 덮은 중심은 x+1(끝 중심 8까지)
        for (int x = 0; x <= 7; x++)
            for (int y = 1; y <= 3; y++)
                Assert.Equal(x + 1, stroke.Pixels[new Point(x, y)], 6);
        Assert.Equal(8, stroke.Pixels[new Point(9, 2)], 6);
        Assert.Equal(0, stroke.Pixels[new Point(-1, 2)], 6);

        var image = new PixelImage(10, 5);
        stroke.Render(image, BlackWhite, GradientStrokeMode.WholeStroke, GradientStroke.DefaultInterval);
        // 한 열(같은 중심이 칠한 칸)은 같은 색, 오른쪽으로 갈수록 밝다
        for (int x = 0; x <= 9; x++)
        {
            Assert.Equal(image.Get(x, 1), image.Get(x, 2));
            Assert.Equal(image.Get(x, 3), image.Get(x, 2));
            if (x > 0 && x <= 7) Assert.True(image.Get(x, 2).R > image.Get(x - 1, 2).R);
        }
        Assert.Equal(Color32.White, image.Get(9, 2));
        Assert.Equal(image.Get(8, 2), image.Get(7, 2)); // 끝 중심(8)이 마지막으로 덮은 칸
    }

    [Fact]
    public void Render_ReplacesPixelsWithStopAlpha_LikeThePencil()
    {
        var fade = BlackWhite with
        {
            Stops = [new GradientStop(0, Color32.White), new GradientStop(1, Color32.White)],
            AlphaStops = [new GradientAlphaStop(0, 255), new GradientAlphaStop(1, 0)],
        };
        var image = new PixelImage(5, 1);
        for (int x = 0; x < 5; x++) image.Set(x, 0, new Color32(255, 0, 0));
        var stroke = Draw(new Point(0, 0), new Point(4, 0));
        stroke.Render(image, fade, GradientStrokeMode.WholeStroke, GradientStroke.DefaultInterval);
        Assert.Equal(Color32.White, image.Get(0, 0));
        Assert.Equal(Color32.Transparent, image.Get(4, 0));          // 섞지 않고 바꾼다(아래 빨강이 지워짐)
        Assert.InRange(image.Get(2, 0).A, 126, 129);
        Assert.Equal(255, image.Get(2, 0).R);
    }

    [Fact]
    public void Forget_DropsPixelsFromLaterRenders()
    {
        var stroke = Draw(new Point(0, 0), new Point(3, 0));
        stroke.Forget([new Point(1, 0)]);
        var image = new PixelImage(4, 1);
        stroke.Render(image, BlackWhite, GradientStrokeMode.WholeStroke, GradientStroke.DefaultInterval);
        Assert.Equal(Color32.Transparent, image.Get(1, 0));
        Assert.Equal(Color32.White, image.Get(3, 0));
    }
}
