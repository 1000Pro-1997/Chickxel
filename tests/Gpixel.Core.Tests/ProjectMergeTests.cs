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
    public void BlankCover_Group_HidesPartsSoGroupIsEmpty_ButPartsKeepPixelsAndExport()
    {
        var sources = new[] { Solid("hat", 2, 1, Red), Solid("helmet", 3, 3, Blue, frames: 2) };
        var g = ProjectMerge.Merge(sources, new MergeOptions(MergeMode.Group, 5, 5, Anchor.TopLeft, "head", BlankCover: true));
        Assert.Equal(["helmet", "hat", "head"], g.Layers.Select(l => l.Name)); // 빈 부위를 더 넣지 않는다
        var group = g.Layers[2];
        Assert.All(g.ChildrenOf(group), part => Assert.False(part.Visible));
        Assert.True(group.Visible);
        Assert.True(g.FlattenGroup(group, 0).IsRegionEmpty(new System.Drawing.Rectangle(0, 0, 5, 5))); // 그룹 행 썸네일 = 빈 그림
        Assert.True(g.Flatten(0).IsRegionEmpty(new System.Drawing.Rectangle(0, 0, 5, 5)));
        Assert.Equal(Red, g.GetCel(1, 0)![0, 0]); // 부위 그림은 그대로
        Assert.Equal(3, ProjectExport.BuildPartsSheet(g, "head_parts").Entries.Count); // 숨겨도 아틀라스엔 모든 부위 칸(hat 1 + helmet 2)

        var shown = ProjectMerge.Merge(sources, new MergeOptions(MergeMode.Group, 5, 5, Anchor.TopLeft, "head"));
        Assert.All(shown.ChildrenOf(shown.Layers[2]), part => Assert.True(part.Visible));
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

    private static readonly Color32 Green = new(0, 255, 0);
    private static readonly Color32 White = new(255, 255, 255);

    private static PixelImage Fill(int w, int h, Color32 c)
    {
        var img = new PixelImage(w, h);
        img.Clear(c);
        return img;
    }

    /// <summary>
    /// .gpixel 같은 원본(8×8): 그룹 "ore" = 부위 "rock"(4×4, (2,2)) + "shine"(2×2, (0,0)), 전체 스킨 "기본"(빨강)·"gold"(rock 파랑, shine은 물려받음),
    /// shine 전용 스킨 "sparkle"(초록, shine에 입힘), 지금 스킨 gold, 착장 "look"(gold + shine=sparkle). 일반 레이어 없음.
    /// </summary>
    private static SpriteProject OreProject(int frames = 1)
    {
        var p = new SpriteProject(8, 8);
        var group = p.AddGroup("ore");
        var rock = p.AddPart(group, "rock", 4, 4, new Point(2, 2));
        var shine = p.AddPart(group, "shine", 2, 2, new Point(0, 0));
        p.DeleteLayer(0);
        for (int f = 1; f < frames; f++) p.AddFrame();
        var baseSkin = group.BaseSkin!;
        var gold = p.AddSkin(group, "gold");
        var sparkle = p.AddPartSkin(shine, "sparkle");
        foreach (var frame in p.Frames)
        {
            p.SetCelRaw(rock, frame, Fill(4, 4, Red), baseSkin);
            p.SetCelRaw(rock, frame, Fill(4, 4, Blue), gold);
            p.SetCelRaw(shine, frame, Fill(2, 2, Red), baseSkin);
            p.SetCelRaw(shine, frame, Fill(2, 2, Green), sparkle);
        }
        p.SaveOutfit("look");
        return p;
    }

    private static Layer Named(SpriteProject p, string name) => p.Layers.Single(l => l.Name == name);

    [Fact]
    public void Skins_ProjectGroupKeepsAllSkins_ImageBecomesNewSkinOnMatchingPart()
    {
        var ore = OreProject();
        var p = ProjectMerge.Merge([new MergeSource("cobalt_ore_2", ore), Solid("stone", 4, 4, White)],
            new MergeOptions(MergeMode.Skins, 8, 8, Anchor.Center, "ignored"));

        Assert.Equal(["rock", "shine", "ore"], p.Layers.Select(l => l.Name)); // 원래 그룹 그대로(새 그룹·부위 없음)
        var (group, rock, shine) = (Named(p, "ore"), Named(p, "rock"), Named(p, "shine"));
        Assert.Equal(["기본", "gold", "stone"], group.Skins.Select(s => s.Name));
        var sparkle = Assert.Single(group.AllSkins, s => s.IsPartOnly);
        Assert.Equal(("sparkle", shine), (sparkle.Name, sparkle.Owner));
        Assert.Equal("gold", group.ActiveSkin!.Name);
        Assert.Same(sparkle, shine.SkinOverride);

        var f0 = p.Frames[0];
        Assert.Equal(Red, p.GetSkinCel(rock, f0, group.Skins[0])![0, 0]);
        Assert.Equal(Blue, p.GetSkinCel(rock, f0, group.Skins[1])![0, 0]);
        Assert.Equal(White, p.GetSkinCel(rock, f0, group.Skins[2])![0, 0]); // 4×4 그림 → 크기가 같은 rock
        Assert.Equal(new Size(4, 4), rock.PartSize);
        Assert.Equal(Red, p.GetSkinCel(shine, f0, group.Skins[2])![0, 0]);  // 새 스킨의 shine은 첫 스킨을 물려받음
        Assert.Equal(Green, p.GetSkinCel(shine, f0, sparkle)![0, 0]);

        var look = Assert.Single(p.Outfits);
        Assert.Equal((group, group.Skins[1]), (look.Slots[0].Group, look.Slots[0].Skin)); // 착장은 복사본을 가리킨다
        Assert.Equal((shine, sparkle), (look.Parts[0].Part, look.Parts[0].Skin));
        Assert.Equal(3, ore.Layers.Count); // 원본은 그대로
        Assert.Equal(3, ore.Layers[2].AllSkins.Count);
    }

    [Fact]
    public void Skins_ImageLargerThanEveryPart_GrowsLargestPartKeepingPixelsInPlace()
    {
        var p = ProjectMerge.Merge([new MergeSource("ore", OreProject()), Solid("boulder", 6, 6, White)],
            new MergeOptions(MergeMode.Skins, 8, 8, Anchor.Center, "x"));
        var (group, rock) = (Named(p, "ore"), Named(p, "rock"));
        Assert.Equal(new Size(6, 6), rock.PartSize);
        Assert.Equal(new Point(1, 1), rock.PartOffset);
        var red = p.GetSkinCel(rock, p.Frames[0], group.Skins[0])!;
        Assert.Equal(default, red[0, 0]);
        Assert.Equal(Red, red[1, 1]); // 캔버스 (2,2) 그대로
        Assert.Equal(White, p.GetSkinCel(rock, p.Frames[0], group.Skins[2])![0, 0]);
    }

    [Fact]
    public void Group_ProjectGroupKeepsPartsAndSkins_ImagesBecomeNewParts()
    {
        var p = ProjectMerge.Merge([new MergeSource("ore", OreProject()), Solid("hat", 3, 3, White), Solid("cape", 2, 5, White)],
            new MergeOptions(MergeMode.Group, 8, 8, Anchor.TopLeft, "ignored"));
        var group = Named(p, "ore");
        Assert.Equal(["cape", "hat", "rock", "shine"], p.ChildrenOf(group).Select(l => l.Name)); // 뒤 파일은 원래 부위 아래, 첫 파일이 위
        Assert.Equal(4, p.Layers.Count - 1);
        Assert.Equal(["기본", "gold"], group.Skins.Select(s => s.Name));
        Assert.Contains(group.AllSkins, s => s.Name == "sparkle");
        var hat = Named(p, "hat");
        Assert.Equal((new Size(3, 3), new Point(0, 0)), (hat.PartSize, hat.PartOffset));
        Assert.Equal(White, p.GetSkinCel(hat, p.Frames[0], group.Skins[0])![0, 0]);
        Assert.Equal(Blue, p.GetSkinCel(Named(p, "rock"), p.Frames[0], group.Skins[1])![0, 0]);
    }

    [Fact]
    public void Layers_ProjectKeepsGroupEffectsTagsAndFrames()
    {
        var ore = OreProject(frames: 2);
        ore.SetLayerEffects(2, [new OutlineEffect { Color = Green, Frames = new FrameRange(1, 1) }]);
        ore.AddTag("idle", 0, 1);
        ore.SetFrameDuration(1, 250);
        var p = ProjectMerge.Merge([new MergeSource("ore", ore), Solid("bg", 8, 8, White)],
            new MergeOptions(MergeMode.Layers, 8, 8, Anchor.Center, "x"));

        Assert.Equal(["bg", "rock", "shine", "ore"], p.Layers.Select(l => l.Name)); // 첫 파일(그룹)이 맨 위
        var group = Named(p, "ore");
        Assert.Equal(ore.Layers[2].Effects, group.Effects);
        Assert.Equal(["기본", "gold", "sparkle"], group.AllSkins.Select(s => s.Name));
        Assert.Equal(new Point(2, 2), Named(p, "rock").PartOffset);
        Assert.Equal([100, 250], p.Frames.Select(f => f.DurationMs));
        var tag = Assert.Single(p.Tags);
        Assert.Equal(("idle", 0, 1), (tag.Name, tag.From, tag.To));
        Assert.Equal(Blue, p.Flatten(0)[2, 2]); // 지금 스킨(gold) 그대로
        Assert.Equal(White, p.Flatten(0)[7, 7]);
    }

    [Fact]
    public void Frames_ProjectKeepsLayersAndTags_ImageFramesAppendedOnTheirOwnLayer()
    {
        var ore = OreProject(frames: 2);
        ore.AddTag("idle", 0, 1);
        var p = ProjectMerge.Merge([new MergeSource("ore", ore), Solid("stone", 8, 8, White)],
            new MergeOptions(MergeMode.Frames, 8, 8, Anchor.Center, "x"));

        Assert.Equal(3, p.Frames.Count);
        Assert.Equal([null, null, "stone"], p.Frames.Select(f => f.Name));
        var tag = Assert.Single(p.Tags);
        Assert.Equal(("idle", 0, 1), (tag.Name, tag.From, tag.To));
        var group = Named(p, "ore");
        Assert.Equal(["rock", "shine"], p.ChildrenOf(group).Select(l => l.Name));
        Assert.Equal(["기본", "gold", "sparkle"], group.AllSkins.Select(s => s.Name));
        var rock = Named(p, "rock");
        Assert.Equal(Blue, p.GetSkinCel(rock, p.Frames[1], group.Skins[1])![0, 0]);
        Assert.Null(p.GetSkinCel(rock, p.Frames[2], group.Skins[0]));
        var stoneLayer = Assert.Single(p.Layers, l => l.Kind == LayerKind.Normal);
        Assert.Equal(White, p.GetCel(stoneLayer, p.Frames[2])![0, 0]);
        Assert.Null(p.GetCel(stoneLayer, p.Frames[0]));
    }

    [Fact]
    public void Frames_SameStructureTwice_ContinuesOneGroup_TagsAndEffectRangesShifted()
    {
        var a = OreProject(frames: 2);
        a.AddTag("idle", 0, 1);
        var b = OreProject(frames: 2);
        b.AddTag("idle", 0, 1);
        b.SetLayerEffects(0, [new OutlineEffect { Frames = new FrameRange(1, 1) }]); // b의 rock만 효과 → 그 rock은 따로
        var p = ProjectMerge.Merge([new MergeSource("a", a), new MergeSource("b", b)],
            new MergeOptions(MergeMode.Frames, 8, 8, Anchor.Center, "x"));

        Assert.Equal(4, p.Frames.Count);
        Assert.Equal([("idle", 0, 1), ("idle_1", 2, 3)], p.Tags.Select(t => (t.Name, t.From, t.To)));
        Assert.Equal(2, p.Layers.Count(l => l.IsGroup)); // 부위 효과가 달라 이어 붙이지 않고 따로(지우지 않음)
        var bRock = p.Layers.Single(l => l.IsPart && l.Effects.Count > 0);
        Assert.Equal(new FrameRange(3, 3), bRock.Effects[0].Frames);

        var same = ProjectMerge.Merge([new MergeSource("a", OreProject(2)), new MergeSource("b", OreProject(2))],
            new MergeOptions(MergeMode.Frames, 8, 8, Anchor.Center, "x"));
        var group = Assert.Single(same.Layers, l => l.IsGroup);
        Assert.Equal(3, group.AllSkins.Count); // 스킨은 이름으로 합침
        Assert.Equal(Green, same.GetSkinCel(Named(same, "shine"), same.Frames[3], group.AllSkins[2])![0, 0]);
    }

    [Fact]
    public void CanvasGrowth_ShiftsPartOffsetsAndPivotsByAnchor()
    {
        static (double, double) Px(Pivot pv) { var (x, y) = pv.ToLocalPixel(12, 10); return (Math.Round(x, 3), Math.Round(y, 3)); }
        var ore = OreProject();
        ore.SetFramePivot(0, new Pivot(0.25f, 0.75f)); // 8×8에서 (2,2)
        var p = ProjectMerge.Merge([new MergeSource("ore", ore), Solid("big", 12, 10, White)],
            new MergeOptions(MergeMode.Layers, 12, 10, Anchor.BottomCenter, "x"));
        Assert.Equal(new Point(4, 4), Named(p, "rock").PartOffset); // (2,2) + 아래 가운데 (2,2)
        Assert.Equal(new Point(2, 2), Named(p, "shine").PartOffset);
        Assert.Equal((4.0, 4.0), Px(p.Frames[0].Pivot!.Value));
        Assert.Equal((6.0, 6.0), Px(p.Sprite.DefaultPivot)); // 가운데 (4,4) → (6,6)
        Assert.Equal(Blue, p.Flatten(0)[4, 4]);
    }

    [Fact]
    public void Skins_SecondProjectWithSamePartsAppendsItsSkins_BakingInheritedCels()
    {
        var b = OreProject();
        b.SetCelRaw(b.Layers[0], b.Frames[0], Fill(4, 4, White), b.Layers[2].BaseSkin); // b의 기본 rock = 흰색
        var p = ProjectMerge.Merge([new MergeSource("a", OreProject()), new MergeSource("b", b)],
            new MergeOptions(MergeMode.Skins, 8, 8, Anchor.Center, "x"));
        var group = Assert.Single(p.Layers, l => l.IsGroup);
        Assert.Equal(["기본", "gold", "기본 2", "gold 2"], group.Skins.Select(s => s.Name));
        Assert.Equal(["sparkle", "sparkle 2"], group.AllSkins.Where(s => s.IsPartOnly).Select(s => s.Name));
        var (rock, shine, f0) = (Named(p, "rock"), Named(p, "shine"), p.Frames[0]);
        Assert.Equal(White, p.GetOwnCel(rock, f0, group.Skins[2])![0, 0]);
        Assert.Equal(Red, p.GetOwnCel(shine, f0, group.Skins[3])![0, 0]); // b의 gold shine은 b 기본을 물려받던 그림 → 구워 넣음
        Assert.Equal(2, p.Outfits.Count);
    }
}
