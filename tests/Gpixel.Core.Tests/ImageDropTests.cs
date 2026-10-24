using System.Drawing;
using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class ImageDropTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    private static PixelImage Solid(int w, int h, Color32 c)
    {
        var img = new PixelImage(w, h);
        img.Clear(c);
        return img;
    }

    private static DroppedImage One(string name, PixelImage image) => new(name, [image], [Frame.DefaultDurationMs]);

    [Theory]
    [InlineData(3, 3, 10, 10, 9, 9, false)]  // 홀수: 놓은 픽셀이 정가운데
    [InlineData(4, 2, 10, 10, 8, 9, false)]  // 짝수: 가운데 둘 중 오른쪽·아래
    [InlineData(5, 5, 0, 0, -2, -2, false)]  // 가장자리: 캔버스 밖은 넣을 때 잘림
    [InlineData(40, 8, 3, 3, -4, 12, true)]  // 가로가 캔버스보다 큼: 캔버스 가운데
    [InlineData(5, 5, -10, 40, -2, 29, false)] // 캔버스 밖에 놓음: 가장 가까운 가장자리 픽셀 기준
    [InlineData(33, 33, 3, 3, -1, -1, true)] // 홀수 차이: 왼쪽·위로 내림
    public void Place_CentersOnDropOrCanvas(int w, int h, int dx, int dy, int x, int y, bool oversized)
    {
        var (loc, over) = ImageDrop.Place(new Size(32, 32), new Size(w, h), new Point(dx, dy));
        Assert.Equal(new Point(x, y), loc);
        Assert.Equal(oversized, over);
    }

    [Fact]
    public void Apply_AddsLayerAboveCurrentWithClippedPixels_OneUndoStep()
    {
        var p = new SpriteProject(8, 8);
        p.AddLayer(); // 레이어 2개, 현재 = 0
        int undoBefore = p.History.UndoCount;

        var result = ImageDrop.Apply(p, [One("hero", Solid(3, 3, Red))], currentLayer: 0, currentFrame: 0, new Point(0, 7), "drop");

        Assert.Equal(3, p.Layers.Count);
        var layer = Assert.Single(result.Layers);
        Assert.Equal(1, p.IndexOf(layer));
        Assert.Equal("hero", layer.Name);
        Assert.False(result.Oversized);
        Assert.Equal(0, result.AddedFrames);
        var cel = p.GetCel(1, 0)!;
        // (0,7) 가운데 3×3 → (-1,6)~(1,8): 캔버스 안은 (0,6)~(1,7)
        Assert.Equal(Red, cel.Get(0, 6));
        Assert.Equal(Red, cel.Get(1, 7));
        Assert.Equal(Color32.Transparent, cel.Get(2, 7));
        Assert.Equal(Color32.Transparent, cel.Get(0, 5));
        Assert.Equal(4, cel.Pixels.Count(c => c == Red));

        Assert.Equal(undoBefore + 1, p.History.UndoCount);
        p.Undo();
        Assert.Equal(2, p.Layers.Count);
    }

    [Fact]
    public void Apply_MultipleFilesStackInOrderWithUniqueNames()
    {
        var p = new SpriteProject(4, 4);
        p.Layers[0].Name = "a";
        var result = ImageDrop.Apply(p, [One("a", Solid(1, 1, Red)), One("b", Solid(1, 1, Blue))], 0, 0, new Point(1, 1), "drop");

        Assert.Equal(["a", "a 2", "b"], p.Layers.Select(l => l.Name));
        Assert.Equal(p.Layers[2], result.Layers[^1]);
        Assert.Equal(Blue, p.GetCel(2, 0)!.Get(1, 1));
    }

    [Fact]
    public void Apply_OversizedImageIsCenteredAndReported()
    {
        var p = new SpriteProject(4, 4);
        var big = new PixelImage(6, 2);
        for (int x = 0; x < 6; x++) big.Set(x, 0, new Color32((byte)(x + 1), 0, 0));
        var result = ImageDrop.Apply(p, [One("big", big)], 0, 0, new Point(0, 0), "drop");

        Assert.True(result.Oversized);
        var cel = p.GetCel(1, 0)!;
        // 가로 6 → x -1부터: 캔버스 x0 = 그림 x1, 세로 2 → y 1부터
        Assert.Equal(new Color32(2, 0, 0), cel.Get(0, 1));
        Assert.Equal(new Color32(5, 0, 0), cel.Get(3, 1));
    }

    [Fact]
    public void Apply_AnimationFillsFromCurrentFrameAndAppendsFrames()
    {
        var p = new SpriteProject(4, 4);
        p.AddFrame();
        p.AddFrame(); // 프레임 3개, 현재 = 1
        var gif = new DroppedImage("anim", [Solid(1, 1, Red), Solid(1, 1, Blue), Solid(1, 1, Red), Solid(1, 1, Blue)], [10, 20, 30, 40]);
        int undoBefore = p.History.UndoCount;

        var result = ImageDrop.Apply(p, [gif], 0, 1, new Point(2, 2), "drop");

        Assert.Equal(2, result.AddedFrames);
        Assert.Equal(5, p.Frames.Count);
        Assert.True(p.IsCelEmpty(1, 0));
        Assert.Equal(Red, p.GetCel(1, 1)!.Get(2, 2));
        Assert.Equal(Blue, p.GetCel(1, 2)!.Get(2, 2));
        Assert.Equal(Red, p.GetCel(1, 3)!.Get(2, 2));
        Assert.Equal(Blue, p.GetCel(1, 4)!.Get(2, 2));
        Assert.Equal(30, p.Frames[3].DurationMs);
        Assert.Equal(40, p.Frames[4].DurationMs);
        Assert.Equal(Frame.DefaultDurationMs, p.Frames[1].DurationMs); // 있던 프레임 길이는 그대로

        Assert.Equal(undoBefore + 1, p.History.UndoCount);
        p.Undo();
        Assert.Equal(3, p.Frames.Count);
        Assert.Single(p.Layers);
    }

    [Fact]
    public void Apply_FromPartGoesAboveGroupAtTopLevel()
    {
        var p = new SpriteProject(8, 8);
        var group = p.AddGroup("body");
        var part = p.AddPart(group, "arm", 2, 2);
        p.AddLayer(); // 그룹 위 일반 레이어
        int partIndex = p.IndexOf(part);

        var result = ImageDrop.Apply(p, [One("drop", Solid(1, 1, Red))], partIndex, 0, new Point(4, 4), "drop");

        var layer = result.Layers[0];
        Assert.Null(layer.Parent);
        Assert.Equal(p.IndexOf(group) + 1, p.IndexOf(layer));
        Assert.False(layer.IsPart);
    }
}
