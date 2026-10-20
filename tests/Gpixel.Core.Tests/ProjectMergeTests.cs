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
    public void Skins_OnePartWithSkinPerFile_MissingFramesInheritFirstSkin()
    {
        var p = ProjectMerge.Merge([Solid("red", 2, 2, Red, frames: 2), Solid("blue", 1, 1, Blue), Solid("red", 2, 2, Blue)],
            new MergeOptions(MergeMode.Skins, 4, 4, Anchor.BottomRight, "ore"));
        Assert.Equal(["부위", "ore"], p.Layers.Select(l => l.Name));
        var (part, group) = (p.Layers[0], p.Layers[1]);
        Assert.Same(group, part.Parent);
        Assert.Equal(new Size(2, 2), part.PartSize);
        Assert.Equal(new Point(2, 2), part.PartOffset);
        Assert.Equal(["red", "blue", "red 2"], group.Skins.Select(s => s.Name));
        Assert.Same(group.Skins[0], group.ActiveSkin);
        Assert.Equal(2, p.Frames.Count);

        var blue = group.Skins[1];
        var cel = p.GetSkinCel(part, p.Frames[0], blue)!;
        Assert.Equal(Blue, cel[1, 1]);              // 1×1이 2×2 부위 안 오른쪽 아래
        Assert.Equal(default, cel[0, 0]);
        Assert.Equal(Red, p.GetSkinCel(part, p.Frames[1], blue)![0, 0]); // 프레임 2는 없어서 첫 스킨을 물려받음
        Assert.Equal(Red, p.Flatten(0)[2, 2]);
    }

    [Fact]
    public void EmptyFirst_Group_EmptyTopPart_Skins_EmptyBaseSkin()
    {
        var sources = new[] { Solid("hat", 2, 1, Red), Solid("helmet", 3, 3, Blue, frames: 2) };
        var g = ProjectMerge.Merge(sources, new MergeOptions(MergeMode.Group, 5, 5, Anchor.TopLeft, "head", EmptyFirst: true));
        // 맨 위(마지막 레이어 앞) = 빈 부위 "없음", 그 아래 파일 부위들
        Assert.Equal(["helmet", "hat", ProjectMerge.EmptyName, "head"], g.Layers.Select(l => l.Name));
        var empty = g.Layers[2];
        Assert.True(empty.IsPart);
        Assert.Same(g.Layers[3], empty.Parent);
        Assert.Equal(new Size(3, 3), empty.PartSize);
        Assert.All(Enumerable.Range(0, g.Frames.Count), f => Assert.Null(g.GetCel(2, f)));
        Assert.Equal(Red, g.Flatten(0)[0, 0]); // 빈 부위는 아무것도 가리지 않음

        var s = ProjectMerge.Merge(sources, new MergeOptions(MergeMode.Skins, 5, 5, Anchor.TopLeft, "head", EmptyFirst: true));
        var (part, group) = (s.Layers[0], s.Layers[1]);
        Assert.Equal([ProjectMerge.EmptyName, "hat", "helmet"], group.Skins.Select(k => k.Name));
        Assert.Same(group.Skins[0], group.ActiveSkin);
        Assert.Null(s.GetSkinCel(part, s.Frames[0], group.Skins[0]));
        Assert.Equal(default, s.Flatten(0)[0, 0]); // 기본 스킨이 비었으니 그림 없음
        Assert.Equal(Red, s.GetSkinCel(part, s.Frames[0], group.Skins[1])![0, 0]);
        Assert.Null(s.GetSkinCel(part, s.Frames[1], group.Skins[1])); // 모자란 프레임은 빈 기본 스킨을 물려받아 비어 보임
        Assert.Equal(Blue, s.GetSkinCel(part, s.Frames[1], group.Skins[2])![2, 2]);
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
