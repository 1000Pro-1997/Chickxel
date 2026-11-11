using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class StampFramesTests
{
    private static readonly Color32 Red = new(255, 0, 0);

    /// <summary><paramref name="width"/>×<paramref name="height"/> 캔버스의 (<paramref name="x"/>, <paramref name="y"/>)부터 <paramref name="w"/>×<paramref name="h"/>만 빨강.</summary>
    private static PixelImage Frame(int width, int height, int x, int y, int w, int h)
    {
        var image = new PixelImage(width, height);
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++) image.Set(xx, yy, Red);
        return image;
    }

    [Fact]
    public void ToShapes_TrimsEachFrameAndSkipsEmptyOnes()
    {
        var result = StampFrames.ToShapes([Frame(10, 10, 2, 3, 4, 2), new PixelImage(10, 10), Frame(10, 10, 0, 0, 1, 5)]);

        Assert.True(result.IsValid);
        Assert.Equal(1, result.EmptyFrames);
        Assert.Equal([(4, 2), (1, 5)], result.Shapes.Select(s => (s.Width, s.Height)));
    }

    [Fact]
    public void ToShapes_ReportsTooBigFramesByIndexAndIsInvalid()
    {
        int big = StampLibrary.MaxShapeSize + 1;
        var result = StampFrames.ToShapes([Frame(300, 300, 0, 0, 2, 2), Frame(300, 300, 0, 0, big, 3)]);

        Assert.False(result.IsValid);
        Assert.Equal([new StampFrames.TooBigFrame(1, big, 3)], result.TooBig);
        Assert.Single(result.Shapes);
    }

    [Fact]
    public void ToShapes_MoreThanTheLimitIsTooMany_TheLimitItselfIsFine()
    {
        var atLimit = StampFrames.ToShapes(Enumerable.Range(0, StampFrames.MaxShapes).Select(_ => Frame(4, 4, 1, 1, 1, 1)));
        var over = StampFrames.ToShapes(Enumerable.Range(0, StampFrames.MaxShapes + 1).Select(_ => Frame(4, 4, 1, 1, 1, 1)));

        Assert.True(atLimit.IsValid);
        Assert.True(over.TooMany);
        Assert.False(over.IsValid);
    }

    [Fact]
    public void ToProject_CentersEachShapeOnTheLargestSizeAndRoundTrips()
    {
        var wide = Frame(6, 2, 0, 0, 6, 2);
        var tall = Frame(1, 5, 0, 0, 1, 5);
        var project = StampFrames.ToProject([wide, tall]);

        Assert.Equal((6, 5), (project.Width, project.Height));
        Assert.Single(project.Layers);
        Assert.Equal(2, project.Frames.Count);
        Assert.False(project.IsDirty);
        // 6×2 → 위아래 3칸 남음: 위 1, 아래 2. 1×5 → 좌우 5칸 남음: 왼쪽 2.
        Assert.Equal(new System.Drawing.Rectangle(0, 1, 6, 2), project.Flatten(0).GetOpaqueBounds());
        Assert.Equal(new System.Drawing.Rectangle(2, 0, 1, 5), project.Flatten(1).GetOpaqueBounds());

        var back = StampFrames.ToShapes(Enumerable.Range(0, project.Frames.Count).Select(f => project.Flatten(f)));
        Assert.Equal([wide.Pixels, tall.Pixels], back.Shapes.Select(s => s.Pixels));
    }

    [Fact]
    public void ToProject_EmptySetGetsOneEmptyFrame()
    {
        var project = StampFrames.ToProject([]);

        Assert.Equal((StampFrames.EmptyCanvasSize, StampFrames.EmptyCanvasSize), (project.Width, project.Height));
        Assert.Single(project.Frames);
        Assert.Empty(StampFrames.ToShapes([project.Flatten(0)]).Shapes);
    }
}
