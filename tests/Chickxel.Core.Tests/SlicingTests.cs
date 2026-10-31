using System.Drawing;
using Chickxel.Core.Model;
using Chickxel.Core.Slicing;

namespace Chickxel.Core.Tests;

public class SlicingTests
{
    private static PixelImage WithBlocks(int w, int h, params Rectangle[] blocks)
    {
        var img = new PixelImage(w, h);
        foreach (var b in blocks) img.FillRect(b, Color32.White);
        return img;
    }

    [Fact]
    public void Grid_CellSize_OffsetPadding_SkipEmpty()
    {
        var img = WithBlocks(40, 20, new Rectangle(1, 1, 2, 2), new Rectangle(13, 2, 1, 1));
        var grid = new GridSettings { CellW = 8, CellH = 8, OffsetX = 1, OffsetY = 1, PadX = 4, PadY = 2 };
        var all = GridSlicer.Slice(img, grid, skipEmpty: false);
        // x: 1, 13, 25 (다음 37은 37+8 > 40) / y: 1, 11 (다음 21은 21+8 > 20)
        Assert.Equal(
            [new(1, 1, 8, 8), new(13, 1, 8, 8), new(25, 1, 8, 8), new(1, 11, 8, 8), new(13, 11, 8, 8), new(25, 11, 8, 8)],
            all);
        var nonEmpty = GridSlicer.Slice(img, grid, skipEmpty: true);
        Assert.Equal([new Rectangle(1, 1, 8, 8), new Rectangle(13, 1, 8, 8)], nonEmpty);
    }

    [Fact]
    public void Count_ColumnsRows()
    {
        var img = new PixelImage(64, 128);
        var rects = CountSlicer.Slice(img, 4, 4);
        Assert.Equal(16, rects.Count);
        Assert.All(rects, r => Assert.Equal(new Size(16, 32), r.Size));
        Assert.Equal(new Rectangle(48, 96, 16, 32), rects[^1]);
    }

    [Fact]
    public void Auto_FindsEightConnectedBlobs_SortedRowsThenColumns()
    {
        var img = new PixelImage(30, 30);
        // 오른쪽 위 덩어리(대각선으로 이어진 두 픽셀 = 하나)
        img.Set(20, 2, Color32.White);
        img.Set(21, 3, Color32.White);
        // 왼쪽 위 덩어리(조금 더 아래에서 시작해도 같은 행)
        img.FillRect(new Rectangle(2, 3, 4, 4), Color32.White);
        // 아래 덩어리
        img.FillRect(new Rectangle(5, 20, 3, 5), Color32.White);
        // 티끌
        img.Set(28, 28, Color32.Black);

        var rects = AutoSlicer.Slice(img, minSize: 2);
        Assert.Equal([new Rectangle(2, 3, 4, 4), new Rectangle(20, 2, 2, 2), new Rectangle(5, 20, 3, 5)], rects);
        Assert.Equal(4, AutoSlicer.Slice(img, minSize: 1).Count);
    }

    [Fact]
    public void Auto_MergesOverlappingBoxes()
    {
        var img = new PixelImage(20, 20);
        img.FillRect(new Rectangle(0, 0, 10, 2), Color32.White);   // 위 막대
        img.FillRect(new Rectangle(0, 2, 1, 8), Color32.White);    // 붙은 기둥
        img.Set(5, 5, Color32.White);                              // 감싸는 상자 안 떨어진 점
        Assert.Equal([new Rectangle(0, 0, 10, 10)], AutoSlicer.Slice(img));
        Assert.Equal(2, AutoSlicer.Slice(img, mergeOverlapping: false).Count);
    }

    [Fact]
    public void Naming_IndexRowColPrefixAndRowNames()
    {
        var rects = GridSlicer.Slice(32, 32, new GridSettings { CellW = 16, CellH = 16 });
        Assert.Equal(["hero_0", "hero_1", "hero_2", "hero_3"],
            NamingRules.GenerateNames(rects, new NamingOptions { BaseName = "hero" }));
        Assert.Equal(["hero_0_0", "hero_0_1", "hero_1_0", "hero_1_1"],
            NamingRules.GenerateNames(rects, new NamingOptions { BaseName = "hero", Template = NamingRules.RowColTemplate }));
        Assert.Equal(["f01", "f02", "f03", "f04"],
            NamingRules.GenerateNames(rects, new NamingOptions { Template = NamingRules.PrefixTemplate("f", 2), Start = 1 }));
        Assert.Equal(["walk_down_0", "walk_down_1", "hero_1_0", "hero_1_1"],
            NamingRules.GenerateNames(rects, new NamingOptions
            {
                BaseName = "hero", Template = NamingRules.RowNamesTemplate, RowNames = ["walk_down"],
            }));
        Assert.Equal(["x", "x_1", "x_2", "x_3"],
            NamingRules.GenerateNames(rects, new NamingOptions { Template = "x" }));
    }

    [Fact]
    public void Naming_RenameUpdatesAnimationFrames()
    {
        var doc = Document.CreateNew(32, 16);
        doc.Slices.Add(new Slice("a", new Rectangle(0, 0, 16, 16), Pivot.Center));
        doc.Slices.Add(new Slice("b", new Rectangle(16, 0, 16, 16), Pivot.Center));
        var clip = new AnimationClip("idle");
        clip.AddFrame("b");
        clip.AddFrame("a");
        doc.Animations.Add(clip);

        NamingRules.Rename(doc, null, new NamingOptions { BaseName = "p" });
        Assert.Equal(["p_0", "p_1"], doc.Slices.Select(s => s.Name));
        Assert.Equal(["p_1", "p_0"], clip.Frames.Select(f => f.Slice));
        Assert.True(doc.IsDirty);

        Assert.True(doc.RenameSlice(doc.Slices[0], "head"));
        Assert.Equal("head", clip.Frames[1].Slice);
        Assert.False(doc.RenameSlice(doc.Slices[0], "p_1"));
    }
}
