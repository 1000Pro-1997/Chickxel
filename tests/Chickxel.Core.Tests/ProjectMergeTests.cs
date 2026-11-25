using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class ProjectMergeTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);
    private static readonly Color32 White = new(255, 255, 255);

    private static MergeSource Solid(string name, int w, int h, Color32 c, int frames = 1)
    {
        var images = Enumerable.Range(0, frames).Select(_ => Fill(w, h, c)).ToArray();
        return new MergeSource(name, images, Enumerable.Repeat(80, frames).ToArray());
    }

    private static PixelImage Fill(int w, int h, Color32 c)
    {
        var img = new PixelImage(w, h);
        img.Clear(c);
        return img;
    }

    private static Layer Named(SpriteProject p, string name) => p.Layers.Single(l => l.Name == name);

    private static string Base => Layer.DefaultSkinName;

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
    public void Parent_EmptyCanvasSizeParent_OneChildPerFile_KeepingFileSizeAtAnchor()
    {
        var p = ProjectMerge.Merge([Solid("head", 2, 1, Red), Solid("body", 3, 3, Blue)],
            new MergeOptions(MergeMode.Parent, 5, 5, Anchor.TopLeft, "player"));
        Assert.Equal(["body", "head", "player"], p.Layers.Select(l => l.Name));
        var parent = p.Layers[2];
        Assert.Equal((LayerKind.Part, new Size(5, 5), Point.Empty, (Layer?)null), (parent.Kind, parent.PartSize, parent.PartOffset, parent.Parent));
        Assert.Null(p.GetCel(2, 0)); // 빈 부모(셀 없음)
        Assert.Equal(["body", "head"], p.ChildrenOf(parent).Select(l => l.Name));
        var head = p.Layers[1];
        Assert.True(head.IsPart);
        Assert.Same(parent, head.Parent);
        Assert.Equal(new Size(2, 1), head.PartSize);
        Assert.Equal(new Point(0, 0), head.PartOffset);
        Assert.Equal(new Size(2, 1), p.GetCel(1, 0)!.Size);
        Assert.Equal(Red, p.Flatten(0)[1, 0]);
        Assert.Equal(Blue, p.Flatten(0)[2, 2]);
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
    public void Skins_OneLayerWithSkinPerFile_MissingFramesStayEmpty()
    {
        var p = ProjectMerge.Merge([Solid("red", 2, 2, Red, frames: 2), Solid("blue", 1, 1, Blue), Solid("red", 2, 2, Blue)],
            new MergeOptions(MergeMode.Skins, 4, 4, Anchor.BottomRight, "ore"));
        var layer = Assert.Single(p.Layers);
        Assert.Equal("ore", layer.Name);
        Assert.Equal((LayerKind.Part, (Layer?)null), (layer.Kind, layer.Parent)); // 크기가 달라 자리 있는 레이어(가장 큰 그림 크기)
        Assert.Equal(new Size(2, 2), layer.PartSize);
        Assert.Equal(new Point(2, 2), layer.PartOffset);
        Assert.Equal(["red", "blue", "red 2"], layer.Skins.Select(s => s.Name));
        Assert.Same(layer.Skins[0], layer.ActiveSkin);
        Assert.Equal(2, p.Frames.Count);

        var blue = layer.Skins[1];
        var cel = p.GetOwnCel(layer, p.Frames[0], blue)!;
        Assert.Equal(Blue, cel[1, 1]);              // 1×1이 2×2 레이어 안 오른쪽 아래
        Assert.Equal(default, cel[0, 0]);
        Assert.Null(p.GetOwnCel(layer, p.Frames[1], blue)); // 프레임 2는 없어서 빈 칸(첫 스킨을 빌리지 않음)
        Assert.Equal(Red, p.Flatten(0)[2, 2]);
    }

    [Fact]
    public void Skins_AllFilesCanvasSize_OneNormalLayer()
    {
        var p = ProjectMerge.Merge([Solid("day", 4, 4, Red), Solid("night", 4, 4, Blue)],
            new MergeOptions(MergeMode.Skins, 4, 4, Anchor.Center, "sky"));
        var layer = Assert.Single(p.Layers);
        Assert.Equal(("sky", LayerKind.Normal), (layer.Name, layer.Kind));
        Assert.Equal(["day", "night"], layer.Skins.Select(s => s.Name));
        Assert.Equal(Blue, p.GetOwnCel(layer, p.Frames[0], layer.Skins[1])![3, 3]);
        Assert.Equal(Red, p.Flatten(0)[0, 0]);
    }

    [Fact]
    public void BlankCover_Parent_HidesChildrenSoParentIsEmpty_ButChildrenKeepPixelsAndExport()
    {
        var sources = new[] { Solid("hat", 2, 1, Red), Solid("helmet", 3, 3, Blue, frames: 2) };
        var g = ProjectMerge.Merge(sources, new MergeOptions(MergeMode.Parent, 5, 5, Anchor.TopLeft, "head", BlankCover: true));
        Assert.Equal(["helmet", "hat", "head"], g.Layers.Select(l => l.Name)); // 빈 자식을 더 넣지 않는다
        var parent = g.Layers[2];
        Assert.All(g.ChildrenOf(parent), child => Assert.False(child.Visible));
        Assert.True(parent.Visible);
        Assert.True(g.FlattenSubtree(parent, 0).IsRegionEmpty(new Rectangle(0, 0, 5, 5))); // 접은 부모 줄 썸네일 = 빈 그림
        Assert.True(g.Flatten(0).IsRegionEmpty(new Rectangle(0, 0, 5, 5)));
        Assert.Equal(Red, g.GetCel(1, 0)![0, 0]); // 자식 그림은 그대로
        Assert.Equal(3, ProjectExport.BuildPartsSheet(g, "head_parts").Entries.Count); // 숨겨도 아틀라스엔 모든 자식 칸(hat 1 + helmet 2), 셀 없는 부모는 없음

        var shown = ProjectMerge.Merge(sources, new MergeOptions(MergeMode.Parent, 5, 5, Anchor.TopLeft, "head"));
        Assert.All(shown.ChildrenOf(shown.Layers[2]), child => Assert.True(child.Visible));
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
            new MergeOptions(MergeMode.Parent, 5, 5, Anchor.Center, "player"));
        var path = dir.File("player.chickxel");
        ProjectIO.Save(p, path);
        var loaded = ProjectIO.Load(path);
        Assert.Equal(p.Layers.Select(l => (l.Name, l.Kind, l.PartSize, l.PartOffset, l.Parent?.Name)),
            loaded.Layers.Select(l => (l.Name, l.Kind, l.PartSize, l.PartOffset, l.Parent?.Name)));
        Assert.Equal(p.Flatten(0).Pixels, loaded.Flatten(0).Pixels);
    }

    /// <summary>
    /// .chickxel 같은 원본(8×8): 빈 부모 "ore"(캔버스 크기) 아래 "rock"(4×4, (2,2)) + "shine"(2×2, (0,0)).
    /// rock 스킨 "기본"(빨강)·"gold"(파랑), shine 스킨 "기본"(빨강)·"gold"(빈 칸)·"sparkle"(초록). 지금 스킨 rock = gold, shine = sparkle.
    /// 스킨 프리셋 "look"(rock = gold, shine = sparkle). 일반 레이어 없음.
    /// </summary>
    private static SpriteProject OreProject(int frames = 1)
    {
        var p = new SpriteProject(8, 8);
        var parent = p.AddEmptyParent("ore");
        var rock = p.AddPart(parent, "rock", 4, 4, new Point(2, 2));
        var shine = p.AddPart(parent, "shine", 2, 2, new Point(0, 0));
        p.DeleteLayer(0);
        for (int f = 1; f < frames; f++) p.AddFrame();
        var rockGold = p.AddSkin(rock, "gold");
        p.AddSkin(shine, "gold");
        var sparkle = p.AddSkin(shine, "sparkle");
        foreach (var frame in p.Frames)
        {
            p.SetCelRaw(rock, frame, Fill(4, 4, Red));
            p.SetCelRaw(rock, frame, Fill(4, 4, Blue), rockGold);
            p.SetCelRaw(shine, frame, Fill(2, 2, Red));
            p.SetCelRaw(shine, frame, Fill(2, 2, Green), sparkle);
        }
        p.SaveSkinPreset("look", [new SkinPresetEntry(rock, rockGold), new SkinPresetEntry(shine, sparkle)]);
        return p;
    }

    [Fact]
    public void Skins_ProjectKeepsAllSkins_ImageBecomesNewSkinOnSameSizeLayer()
    {
        var ore = OreProject();
        var p = ProjectMerge.Merge([new MergeSource("cobalt_ore_2", ore), Solid("stone", 4, 4, White)],
            new MergeOptions(MergeMode.Skins, 8, 8, Anchor.Center, "ignored"));

        Assert.Equal(["rock", "shine", "ore"], p.Layers.Select(l => l.Name)); // 원래 나무 그대로(새 레이어 없음)
        var (parent, rock, shine) = (Named(p, "ore"), Named(p, "rock"), Named(p, "shine"));
        Assert.Equal([rock, shine], p.ChildrenOf(parent));
        Assert.Equal([Base, "gold", "stone"], rock.Skins.Select(s => s.Name));
        Assert.Equal([Base, "gold", "sparkle"], shine.Skins.Select(s => s.Name));
        Assert.Equal(("gold", "sparkle"), (rock.ActiveSkin!.Name, shine.ActiveSkin!.Name));

        var f0 = p.Frames[0];
        Assert.Equal(Red, p.GetOwnCel(rock, f0, rock.Skins[0])![0, 0]);
        Assert.Equal(Blue, p.GetOwnCel(rock, f0, rock.Skins[1])![0, 0]);
        Assert.Equal(White, p.GetOwnCel(rock, f0, rock.Skins[2])![0, 0]); // 4×4 그림 → 크기가 같은 rock
        Assert.Equal(new Size(4, 4), rock.PartSize);
        Assert.Equal(Green, p.GetOwnCel(shine, f0, shine.Skins[2])![0, 0]);

        var look = Assert.Single(p.SkinPresets);
        Assert.Equal([new SkinPresetEntry(rock, rock.Skins[1]), new SkinPresetEntry(shine, shine.Skins[2])], look.Entries); // 복사본을 가리킨다
        Assert.Equal(3, ore.Layers.Count); // 원본은 그대로
        Assert.Equal(2, ore.Layers[0].Skins.Count);
    }

    [Fact]
    public void Skins_ImageLargerThanEveryLayer_GrowsLargestLayerKeepingPixelsInPlace()
    {
        var p = ProjectMerge.Merge([new MergeSource("ore", OreProject()), Solid("boulder", 6, 6, White)],
            new MergeOptions(MergeMode.Skins, 8, 8, Anchor.Center, "x"));
        var rock = Named(p, "rock");
        Assert.Equal(new Size(6, 6), rock.PartSize);
        Assert.Equal(new Point(1, 1), rock.PartOffset);
        var red = p.GetOwnCel(rock, p.Frames[0], rock.Skins[0])!;
        Assert.Equal(default, red[0, 0]);
        Assert.Equal(Red, red[1, 1]); // 캔버스 (2,2) 그대로
        Assert.Equal(White, p.GetOwnCel(rock, p.Frames[0], rock.Skins[2])![0, 0]);
        Assert.Equal(new Point(0, 0), Named(p, "shine").PartOffset); // 다른 레이어는 그대로
    }

    [Fact]
    public void Parent_ProjectTreeKeepsChildrenAndSkins_ImagesBecomeNewChildren()
    {
        var p = ProjectMerge.Merge([new MergeSource("ore", OreProject()), Solid("hat", 3, 3, White), Solid("cape", 2, 5, White)],
            new MergeOptions(MergeMode.Parent, 8, 8, Anchor.TopLeft, "ignored"));
        var parent = Named(p, "ore");
        Assert.Equal(["cape", "hat", "rock", "shine"], p.ChildrenOf(parent).Select(l => l.Name)); // 뒤 파일은 원래 자식 아래, 첫 파일이 위
        Assert.Equal(5, p.Layers.Count);
        Assert.Equal([Base, "gold", "sparkle"], Named(p, "shine").Skins.Select(s => s.Name));
        var hat = Named(p, "hat");
        Assert.Equal((new Size(3, 3), new Point(0, 0)), (hat.PartSize, hat.PartOffset));
        Assert.Equal(White, p.GetOwnCel(hat, p.Frames[0], hat.Skins[0])![0, 0]);
        var rock = Named(p, "rock");
        Assert.Equal(Blue, p.GetOwnCel(rock, p.Frames[0], rock.Skins[1])![0, 0]);
    }

    [Fact]
    public void Layers_ProjectKeepsTreeEffectsTagsAndFrames()
    {
        var ore = OreProject(frames: 2);
        ore.SetLayerEffects(2, [new OutlineEffect { Color = Green, Frames = new FrameRange(1, 1), IncludeChildren = true }]);
        ore.AddTag("idle", 0, 1);
        ore.SetFrameDuration(1, 250);
        var p = ProjectMerge.Merge([new MergeSource("ore", ore), Solid("bg", 8, 8, White)],
            new MergeOptions(MergeMode.Layers, 8, 8, Anchor.Center, "x"));

        Assert.Equal(["bg", "rock", "shine", "ore"], p.Layers.Select(l => l.Name)); // 첫 파일(나무)이 맨 위
        var parent = Named(p, "ore");
        Assert.Equal(ore.Layers[2].Effects, parent.Effects);
        Assert.True(parent.Effects[0].IncludeChildren);
        Assert.Equal([Base, "gold", "sparkle"], Named(p, "shine").Skins.Select(s => s.Name));
        Assert.Equal(new Point(2, 2), Named(p, "rock").PartOffset);
        Assert.Equal([100, 250], p.Frames.Select(f => f.DurationMs));
        var tag = Assert.Single(p.Tags);
        Assert.Equal(("idle", 0, 1), (tag.Name, tag.From, tag.To));
        Assert.Equal(Blue, p.Flatten(0)[2, 2]); // 지금 스킨(rock = gold) 그대로
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
        var parent = Named(p, "ore");
        Assert.Equal(["rock", "shine"], p.ChildrenOf(parent).Select(l => l.Name));
        Assert.Equal([Base, "gold", "sparkle"], Named(p, "shine").Skins.Select(s => s.Name));
        var rock = Named(p, "rock");
        Assert.Equal(Blue, p.GetOwnCel(rock, p.Frames[1], rock.Skins[1])![0, 0]);
        Assert.Null(p.GetOwnCel(rock, p.Frames[2], rock.Skins[0]));
        var stoneLayer = Assert.Single(p.Layers, l => l.Kind == LayerKind.Normal);
        Assert.Equal(White, p.GetCel(stoneLayer, p.Frames[2])![0, 0]);
        Assert.Null(p.GetCel(stoneLayer, p.Frames[0]));
    }

    [Fact]
    public void Frames_SameStructureTwice_ContinuesOneTree_TagsAndEffectRangesShifted()
    {
        var a = OreProject(frames: 2);
        a.AddTag("idle", 0, 1);
        var b = OreProject(frames: 2);
        b.AddTag("idle", 0, 1);
        b.SetLayerEffects(0, [new OutlineEffect { Frames = new FrameRange(1, 1) }]); // b의 rock만 효과 → 그 덩어리는 따로
        var p = ProjectMerge.Merge([new MergeSource("a", a), new MergeSource("b", b)],
            new MergeOptions(MergeMode.Frames, 8, 8, Anchor.Center, "x"));

        Assert.Equal(4, p.Frames.Count);
        Assert.Equal([("idle", 0, 1), ("idle_1", 2, 3)], p.Tags.Select(t => (t.Name, t.From, t.To)));
        Assert.Equal(2, p.Layers.Count(p.HasChildren)); // 효과가 달라 이어 붙이지 않고 따로(지우지 않음)
        var bRock = p.Layers.Single(l => l.IsPart && l.Effects.Count > 0);
        Assert.Equal(new FrameRange(3, 3), bRock.Effects[0].Frames);

        var same = ProjectMerge.Merge([new MergeSource("a", OreProject(2)), new MergeSource("b", OreProject(2))],
            new MergeOptions(MergeMode.Frames, 8, 8, Anchor.Center, "x"));
        Assert.Single(same.Layers, same.HasChildren);
        Assert.Equal(3, same.Layers.Count);
        var shine = Named(same, "shine");
        Assert.Equal(3, shine.Skins.Count); // 스킨은 이름으로 합침
        Assert.Equal(Green, same.GetOwnCel(shine, same.Frames[3], shine.Skins[2])![0, 0]);
        Assert.Single(same.SkinPresets); // 같은 프리셋은 한 번만
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
        Assert.Equal(new Point(2, 2), Named(p, "ore").PartOffset); // 빈 부모도 같이 옮겨진다
        Assert.Equal((4.0, 4.0), Px(p.Frames[0].Pivot!.Value));
        Assert.Equal((6.0, 6.0), Px(p.Sprite.DefaultPivot)); // 가운데 (4,4) → (6,6)
        Assert.Equal(Blue, p.Flatten(0)[4, 4]);
    }

    [Fact]
    public void Skins_SecondProjectWithSameTreePathsAppendsItsSkinsPerLayer_BaseOnlyWhenDifferent()
    {
        var b = OreProject();
        b.SetCelRaw(b.Layers[0], b.Frames[0], Fill(4, 4, White)); // b의 rock 기본 = 흰색(shine 기본은 a와 같음)
        var notes = new List<string>();
        var p = ProjectMerge.Merge([new MergeSource("a", OreProject()), new MergeSource("b", b)],
            new MergeOptions(MergeMode.Skins, 8, 8, Anchor.Center, "x"), notes: notes);

        Assert.Equal(["rock", "shine", "ore"], p.Layers.Select(l => l.Name)); // b는 따로 넣지 않는다
        var (parent, rock, shine, f0) = (Named(p, "ore"), Named(p, "rock"), Named(p, "shine"), p.Frames[0]);
        Assert.Equal([Base], parent.Skins.Select(s => s.Name)); // 빈 부모의 같은(빈) 기본 스킨은 더하지 않음
        Assert.Equal([Base, "gold", Base + " 2", "gold 2"], rock.Skins.Select(s => s.Name));
        Assert.Equal([Base, "gold", "sparkle", "gold 2", "sparkle 2"], shine.Skins.Select(s => s.Name));
        Assert.Equal(White, p.GetOwnCel(rock, f0, rock.Skins[2])![0, 0]);
        Assert.Equal(Blue, p.GetOwnCel(rock, f0, rock.Skins[3])![0, 0]);
        Assert.Null(p.GetOwnCel(shine, f0, shine.Skins[3])); // b의 gold shine은 빈 칸 그대로(기본 그림으로 채우지 않음)
        Assert.Contains(DomainStrings.FormatMerge_AppendSkins(name: "b" + SpriteProject.FileExtension, source: "ore", count: 4, target: "a" + SpriteProject.FileExtension), notes);

        Assert.Equal(["look", "look 2"], p.SkinPresets.Select(x => x.Name));
        Assert.Equal([new SkinPresetEntry(rock, rock.Skins[3]), new SkinPresetEntry(shine, shine.Skins[4])], p.SkinPresets[1].Entries);
    }

    [Fact]
    public void Skins_DifferentTreePath_GoesAside()
    {
        var b = OreProject();
        b.SetLayerProperties(1, name: "glow"); // shine → glow: 나무 경로가 달라 맞지 않음
        var notes = new List<string>();
        var p = ProjectMerge.Merge([new MergeSource("a", OreProject()), new MergeSource("b", b)],
            new MergeOptions(MergeMode.Skins, 8, 8, Anchor.Center, "x"), notes: notes);

        Assert.Equal(6, p.Layers.Count);
        Assert.Equal(2, p.Layers.Count(p.HasChildren));
        Assert.Equal([Base, "gold"], Named(p, "rock").Skins.Select(s => s.Name));
        Assert.Contains(DomainStrings.FormatMerge_LayerAside(name: "b" + SpriteProject.FileExtension, source: "ore", target: "a" + SpriteProject.FileExtension), notes);
    }
}
