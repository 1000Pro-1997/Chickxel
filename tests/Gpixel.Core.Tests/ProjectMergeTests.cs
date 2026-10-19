using System.Drawing;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class ProjectMergeTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    private static MergeSource Solid(string name, int w, int h, Color32 c, int frames = 1)
    {
        var images = Enumerable.Range(0, frames).Select(_ =>
        {
            var img = new PixelImage(w, h);
            img.Clear(c);
            return img;
        }).ToArray();
        return new MergeSource(name, images, Enumerable.Repeat(80, frames).ToArray());
    }

    [Theory]
    [InlineData("potato", "potato_flowering", "potato_growing", "potato_mature")]
    [InlineData("down", "idle_down", "walk_down")]
    [InlineData("tree", "tree", "tree_big")]
    [InlineData("wheat_x", "wheat_x", "wardrobe")] // 공통 단어가 없으면 첫 이름
    [InlineData("potato_seedling", "potato_seedling", "wheat_seedlingx")]
    public void SuggestName_UsesCommonWordAffix_ElseFirstName(string expected, params string[] names)
        => Assert.Equal(expected, ProjectMerge.SuggestName(names));

    [Fact]
    public void Layers_FirstSourceOnTop_PlacedByAnchor()
    {
        var p = ProjectMerge.Merge([Solid("a", 2, 2, Red), Solid("b", 4, 4, Blue)],
            new MergeOptions(MergeMode.Layers, 4, 4, Anchor.BottomCenter, "x"));
        Assert.Equal(["b", "a"], p.Layers.Select(l => l.Name));
        var flat = p.Flatten(0);
        Assert.Equal(Red, flat[1, 2]);   // 2×2가 아래 가운데 (1,2)~(2,3)
        Assert.Equal(Red, flat[2, 3]);
        Assert.Equal(Blue, flat[0, 0]);
        Assert.Equal(Blue, flat[1, 1]);
    }

    [Fact]
    public void Group_PartsKeepOwnSize_OffsetByAnchor()
    {
        var p = ProjectMerge.Merge([Solid("head", 2, 1, Red), Solid("body", 3, 3, Blue)],
            new MergeOptions(MergeMode.Group, 5, 5, Anchor.TopLeft, "player"));
        Assert.Equal(["body", "head", "player"], p.Layers.Select(l => l.Name));
        var head = p.Layers[1];
        Assert.True(head.IsPart);
        Assert.Same(p.Layers[2], head.Parent);
        Assert.Equal(new Size(2, 1), head.PartSize);
        Assert.Equal(new Point(0, 0), head.PartOffset);
        Assert.Equal(new Size(2, 1), p.GetCel(1, 0)!.Size);
    }

    [Fact]
    public void Frames_Concatenated_NamesAndTags()
    {
        var walk = Solid("walk", 2, 2, Blue, frames: 3);
        var p = ProjectMerge.Merge([Solid("seed", 1, 1, Red), walk, Solid("ripe", 2, 2, Red)],
            new MergeOptions(MergeMode.Frames, 2, 2, Anchor.Center, "crop"));
        Assert.Equal(5, p.Frames.Count);
        Assert.Single(p.Layers);
        Assert.Equal(["seed", null, null, null, "ripe"], p.Frames.Select(f => f.Name));
        Assert.All(p.Frames, f => Assert.Equal(80, f.DurationMs));
        var tag = Assert.Single(p.Tags);
        Assert.Equal(("walk", 1, 3), (tag.Name, tag.From, tag.To));
        Assert.Equal(Red, p.Flatten(0)[0, 0]); // 1×1 이 2×2 가운데(내림) → (0,0)
        Assert.Equal(Blue, p.Flatten(2)[1, 1]);
    }

    [Fact]
    public void Layers_FrameCountIsMax_ShorterSourcesLeaveEmptyCels()
    {
        var p = ProjectMerge.Merge([Solid("a", 2, 2, Red), Solid("b", 2, 2, Blue, frames: 2)],
            new MergeOptions(MergeMode.Layers, 2, 2, Anchor.Center, "x"));
        Assert.Equal(2, p.Frames.Count);
        Assert.Null(p.GetCel(1, 1)); // a(맨 위)는 프레임 1개뿐
        Assert.Equal(Blue, p.Flatten(1)[0, 0]);
    }

    [Fact]
    public void MergedProject_SavesAndLoads()
    {
        using var dir = new TempDir();
        var p = ProjectMerge.Merge([Solid("head", 2, 1, Red), Solid("body", 3, 3, Blue)],
            new MergeOptions(MergeMode.Group, 5, 5, Anchor.Center, "player"));
        var path = dir.File("player.gpixel");
        ProjectIO.Save(p, path);
        var loaded = ProjectIO.Load(path);
        Assert.Equal(p.Layers.Select(l => (l.Name, l.Kind, l.PartSize, l.PartOffset)), loaded.Layers.Select(l => (l.Name, l.Kind, l.PartSize, l.PartOffset)));
        Assert.Equal(p.Flatten(0).Pixels, loaded.Flatten(0).Pixels);
    }
}
