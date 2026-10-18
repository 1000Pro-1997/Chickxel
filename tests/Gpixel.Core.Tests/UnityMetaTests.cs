using System.Drawing;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.Core.Slicing;

namespace Gpixel.Core.Tests;

public class UnityMetaTests
{
    [Fact]
    public void Read_MultipleFixture_ConvertsToTopLeftAndKeepsIds()
    {
        var info = UnityMeta.Read(TestUtil.Fixture("player@2x.png.meta"));
        Assert.Equal("bb378709875f2244094864a5e8989909", info.Guid);
        Assert.Equal(SpriteMode.Multiple, info.SpriteMode);
        Assert.Equal(16f, info.PixelsPerUnit);
        Assert.Equal(8, info.TextureType);
        Assert.Equal(["Head", "Body", "Arm", "lag"], info.Sprites.Select(s => s.Name));
        Assert.Equal(4, info.InternalIdToName.Count);
        Assert.Equal((3382395990116336189L, "player@2x_0"), info.InternalIdToName[0]);
        Assert.Equal("c533e4961b1e06a419d8d7aaadd1b699", info.SheetSpriteId);

        var slices = info.ToSlices(32);
        // Head: Unity y=20,h=10 → 위 원점 y = 32-20-10 = 2
        Assert.Equal(new Rectangle(2, 2, 16, 10), slices[0].Rect);
        Assert.Equal(new Rectangle(4, 14, 12, 16), slices[1].Rect);
        Assert.Equal(new Rectangle(22, 4, 6, 14), slices[2].Rect);
        Assert.Equal(new Rectangle(22, 20, 6, 6), slices[3].Rect);
        Assert.Equal(Pivot.BottomCenter, slices[0].Pivot);
        Assert.Equal(new Pivot(0.5f, 0.9285714f), slices[2].Pivot);
        Assert.Equal(PivotPreset.Custom, slices[2].Alignment);
        Assert.Equal(new Pivot(0.5f, 1f), slices[3].Pivot);

        var arm = info.Sprites[2];
        Assert.Equal("8e018344a614c904fae1ba4d27455a85", arm.SpriteId);
        Assert.Equal(1404073571L, arm.InternalId);
        Assert.Equal(-622449481L, info.Sprites[1].InternalId);
    }

    [Fact]
    public void Read_SingleFixture()
    {
        var info = UnityMeta.Read(TestUtil.Fixture("fantasy-8x.png.meta"));
        Assert.Equal("6bc9eec90c9c7604f8b0853525f5756e", info.Guid);
        Assert.Equal(SpriteMode.Single, info.SpriteMode);
        Assert.Equal(8f, info.PixelsPerUnit);
        Assert.Equal(Pivot.Center, info.SpritePivot);
        Assert.Equal(-6583847082394195339L, info.Sprites[0].InternalId);
    }

    [Fact]
    public void Resave_FixtureCopy_PreservesGuidIdsRectsAndPivots()
    {
        using var dir = new TempDir();
        var png = dir.File("player@2x.png");
        File.Copy(TestUtil.Fixture("player@2x.png"), png);
        File.Copy(TestUtil.Fixture("player@2x.png.meta"), png + ".meta");
        var original = UnityMeta.Read(png + ".meta");

        var load = DocumentIO.Load(png);
        Assert.Equal(DocumentInfoSource.UnityMeta, load.Source);
        var doc = load.Document;
        Assert.Equal(4, doc.Slices.Count);
        Assert.False(doc.IsDirty);

        DocumentIO.Save(doc, null, writeMeta: true);
        var saved = UnityMeta.Read(png + ".meta");

        Assert.Equal(original.Guid, saved.Guid);
        Assert.Equal(original.SheetSpriteId, saved.SheetSpriteId);
        Assert.Equal(SpriteMode.Multiple, saved.SpriteMode);
        Assert.Equal(16f, saved.PixelsPerUnit);
        Assert.Equal(original.Sprites.Count, saved.Sprites.Count);
        for (int i = 0; i < original.Sprites.Count; i++)
        {
            var o = original.Sprites[i];
            var s = saved.Sprites[i];
            Assert.Equal(o.Name, s.Name);
            Assert.Equal(o.UnityRect, s.UnityRect);
            Assert.Equal(o.Alignment, s.Alignment);
            Assert.Equal(o.Pivot, s.Pivot);
            Assert.Equal(o.SpriteId, s.SpriteId);
            Assert.Equal(o.InternalId, s.InternalId);
        }
        // 바뀐 것이 없으므로 Unity가 쓴 메타와 글자 하나까지 같다(internalIDToNameTable의 지난 항목 포함).
        Assert.Equal(original.InternalIdToName, saved.InternalIdToName);
        Assert.Equal(File.ReadAllText(TestUtil.Fixture("player@2x.png.meta")), File.ReadAllText(png + ".meta"));

        var text = File.ReadAllText(png + ".meta");
        Assert.Contains("    filterMode: 0\n", text);
        Assert.Contains("  textureType: 8\n", text);
        Assert.Contains("  alphaIsTransparency: 1\n", text);
        Assert.Contains("    enableMipMap: 0\n", text);
        Assert.Contains("    textureCompression: 0\n", text);
        Assert.Contains("    nameFileIdTable:\n      Arm: 1404073571\n      Body: -622449481\n      Head: 5499198\n      lag: 302612106\n", text);
        Assert.True(File.Exists(png + ".gpixel.json"));
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void Generated_Meta_HasSameFieldStructureAsUnityFixture()
    {
        var fixtureText = File.ReadAllText(TestUtil.Fixture("player@2x.png.meta"));
        var info = UnityMeta.Parse(fixtureText);
        var doc = new Document(new PixelImage(32, 32), "player@2x.png");
        info.ApplyTo(doc);
        var generated = UnityMeta.Generate(doc, info);
        Assert.Equal(KeySequence(fixtureText), KeySequence(generated));

        var single = File.ReadAllText(TestUtil.Fixture("fantasy-8x.png.meta"));
        var singleInfo = UnityMeta.Parse(single);
        var singleDoc = new Document(new PixelImage(256, 8), "fantasy-8x.png");
        singleInfo.ApplyTo(singleDoc);
        var singleGenerated = UnityMeta.Generate(singleDoc, singleInfo);
        // Single은 칸 목록을 쓰지 않으므로 sprites 블록을 뺀 나머지 구조가 같아야 한다.
        Assert.Equal(KeySequence(single, skipSprites: true), KeySequence(singleGenerated, skipSprites: true));
    }

    /// <summary>internalIDToNameTable 내용을 뺀 줄별 "들여쓰기+키" 목록.</summary>
    private static List<string> KeySequence(string yaml, bool skipSprites = false)
    {
        var keys = new List<string>();
        bool inTable = false, inSprites = false;
        foreach (var line in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0) continue;
            if (line.StartsWith("  internalIDToNameTable")) { inTable = true; keys.Add("  internalIDToNameTable"); continue; }
            if (inTable && (line.StartsWith("  - ") || line.StartsWith("      ") || line.StartsWith("    second"))) continue;
            inTable = false;
            if (skipSprites && line.StartsWith("    sprites:")) { inSprites = true; keys.Add("    sprites"); continue; }
            if (skipSprites && line.StartsWith("    nameFileIdTable")) { keys.Add("    nameFileIdTable"); inSprites = true; continue; }
            if (inSprites && (line.StartsWith("    - ") || line.StartsWith("      "))) continue;
            inSprites = false;
            int colon = line.IndexOf(':');
            keys.Add(colon >= 0 ? line[..colon] : line);
        }
        return keys;
    }

    [Fact]
    public void NewMeta_RoundTrip_MultipleWithCustomPivotsAndBorders()
    {
        var doc = new Document(new PixelImage(48, 32), @"C:\x\hero.png");
        doc.Sprite.Mode = SpriteMode.Multiple;
        doc.Sprite.PixelsPerUnit = 32;
        doc.Slices.Add(new Slice("a", new Rectangle(0, 0, 16, 16), Pivot.BottomCenter, new SpriteBorder(1, 2, 3, 4)));
        doc.Slices.Add(new Slice("b", new Rectangle(16, 8, 16, 24), new Pivot(0.3f, 0.7f)));
        doc.Slices.Add(new Slice("odd: name #1", new Rectangle(32, 0, 16, 32), Pivot.Center));

        var text = UnityMeta.Generate(doc, null);
        var info = UnityMeta.Parse(text);
        Assert.Matches("^[0-9a-f]{32}$", info.Guid);
        Assert.Equal(SpriteMode.Multiple, info.SpriteMode);
        Assert.Equal(32f, info.PixelsPerUnit);
        var slices = info.ToSlices(32);
        Assert.Equal(doc.Slices.Select(s => s.Name), slices.Select(s => s.Name));
        Assert.Equal(doc.Slices.Select(s => s.Rect), slices.Select(s => s.Rect));
        Assert.Equal(doc.Slices.Select(s => s.Pivot), slices.Select(s => s.Pivot));
        Assert.Equal(new SpriteBorder(1, 2, 3, 4), slices[0].Border);
        Assert.Equal(16, info.Sprites[0].UnityRect.Y); // 32 - 0 - 16
        Assert.Equal(7, info.Sprites[0].Alignment);
        Assert.Equal(9, info.Sprites[1].Alignment);
        Assert.Equal(3, info.Sprites.Select(s => s.InternalId).Distinct().Count());
        Assert.All(info.Sprites, s => Assert.True(s.InternalId > 0));
        Assert.All(info.Sprites, s => Assert.Matches("^[0-9a-f]{32}$", s.SpriteId));
        Assert.Contains("spriteMeshType: 0", text);
        Assert.Equal(info.Sprites.Select(s => (s.InternalId, s.Name)), info.InternalIdToName);
    }

    [Fact]
    public void Rewrite_KeepsIdsByName_AndRenamedSliceKeepsIdByRect()
    {
        var doc = new Document(new PixelImage(32, 16), @"C:\x\s.png");
        doc.Sprite.Mode = SpriteMode.Multiple;
        doc.Slices.Add(new Slice("head", new Rectangle(0, 0, 16, 16), Pivot.Center));
        doc.Slices.Add(new Slice("body", new Rectangle(16, 0, 16, 16), Pivot.Center));
        var first = UnityMeta.Parse(UnityMeta.Generate(doc, null));

        // 순서를 바꾸고, 한 칸은 이름 변경, 새 칸 추가
        doc.Slices.Reverse();
        doc.Slices[1].Name = "hat";
        doc.Slices.Add(new Slice("extra", new Rectangle(0, 0, 8, 8), Pivot.Center));
        var second = UnityMeta.Parse(UnityMeta.Generate(doc, first));

        Assert.Equal(first.Guid, second.Guid);
        var f = first.Sprites.ToDictionary(s => s.Name);
        var s2 = second.Sprites.ToDictionary(s => s.Name);
        Assert.Equal(f["body"].InternalId, s2["body"].InternalId);
        Assert.Equal(f["body"].SpriteId, s2["body"].SpriteId);
        Assert.Equal(f["head"].InternalId, s2["hat"].InternalId);
        Assert.Equal(f["head"].SpriteId, s2["hat"].SpriteId);
        Assert.DoesNotContain(s2["extra"].InternalId, first.Sprites.Select(s => s.InternalId));
    }

    [Fact]
    public void SingleMode_Meta_HasNoRects_AndKeepsGuid()
    {
        using var dir = new TempDir();
        var png = dir.File("fantasy-8x.png");
        File.Copy(TestUtil.Fixture("fantasy-8x.png"), png);
        File.Copy(TestUtil.Fixture("fantasy-8x.png.meta"), png + ".meta");

        var doc = DocumentIO.Load(png).Document;
        Assert.Equal(SpriteMode.Single, doc.Sprite.Mode);
        Assert.Equal(8f, doc.Sprite.PixelsPerUnit);
        Assert.Empty(doc.Slices);
        doc.Sprite.DefaultPivot = Pivot.BottomCenter;
        DocumentIO.Save(doc, null, writeMeta: true);

        var text = File.ReadAllText(png + ".meta");
        Assert.Contains("guid: 6bc9eec90c9c7604f8b0853525f5756e\n", text);
        Assert.Contains("  spriteMode: 1\n", text);
        Assert.Contains("    sprites: []\n", text);
        Assert.Contains("    nameFileIdTable: {}\n", text);
        Assert.Contains("  alignment: 7\n", text);
        Assert.Contains("  spritePivot: {x: 0.5, y: 0}\n", text);
        Assert.Contains("  spritePixelsToUnits: 8\n", text);
        Assert.Contains("    spriteID: 5e97eb03825dee720800000000000000\n", text);
        var info = UnityMeta.Parse(text);
        Assert.Equal(SpriteMode.Single, info.SpriteMode);
        Assert.Equal(Pivot.BottomCenter, info.SpritePivot);
    }

    [Fact]
    public void NewSingleMeta_WithoutExisting()
    {
        var doc = new Document(new PixelImage(4096, 16), @"C:\x\big.png");
        var text = UnityMeta.Generate(doc, null);
        Assert.Contains("  internalIDToNameTable: []\n", text);
        Assert.Contains("  maxTextureSize: 4096\n", text);
        Assert.Contains("  spriteMode: 1\n", text);
        Assert.Equal(SpriteMode.Single, UnityMeta.Parse(text).SpriteMode);
    }

    [Fact]
    public void Acceptance_4x4Sheet_SlicedNamedSaved_ResaveKeepsIds()
    {
        using var dir = new TempDir();
        var root = dir.Path;
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Art"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        var png = Path.Combine(root, "Assets", "Art", "hero.png");

        var doc = new Document(TestUtil.RandomImage(64, 128), null);
        doc.Sprite.Mode = SpriteMode.Multiple;
        doc.Sprite.DefaultPivot = Pivot.BottomCenter;
        doc.Grid = new GridSettings { CellW = 16, CellH = 32 };
        var rects = GridSlicer.Slice(doc.Image, doc.Grid);
        Assert.Equal(16, rects.Count);
        doc.Slices.AddRange(NamingRules.CreateSlices(rects,
            new NamingOptions { Template = NamingRules.RowNamesTemplate, BaseName = "hero", RowNames = ["walk_down", "walk_left", "walk_right", "walk_up"] },
            doc.Sprite.DefaultPivot));
        Assert.True(UnityProjectLocator.IsInUnityAssets(png));
        DocumentIO.Save(doc, png, writeMeta: UnityProjectLocator.IsInUnityAssets(png));

        var first = UnityMeta.Read(png + ".meta");
        Assert.Equal(16, first.Sprites.Count);
        Assert.Equal("walk_down_0", first.Sprites[0].Name);
        Assert.Equal("walk_up_3", first.Sprites[15].Name);
        Assert.Equal(new Rectangle(0, 96, 16, 32), first.Sprites[0].UnityRect); // 첫 행이 Unity 좌표에선 맨 위
        Assert.All(first.Sprites, s => Assert.Equal(new Pivot(0.5f, 0f), s.Pivot));

        var reopened = DocumentIO.Load(png);
        Assert.Equal(DocumentInfoSource.Sidecar, reopened.Source);
        reopened.Document.Image.Set(0, 0, Color32.White);
        DocumentIO.Save(reopened.Document, null, writeMeta: true);
        var second = UnityMeta.Read(png + ".meta");
        Assert.Equal(first.Guid, second.Guid);
        Assert.Equal(first.Sprites.Select(s => (s.Name, s.SpriteId, s.InternalId)),
            second.Sprites.Select(s => (s.Name, s.SpriteId, s.InternalId)));
    }

    [Fact]
    public void ProjectLocator_FindsRoot()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "Assets", "A", "B"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "ProjectSettings"));
        var file = Path.Combine(dir.Path, "Assets", "A", "B", "x.png");
        Assert.Equal(Path.GetFullPath(dir.Path), UnityProjectLocator.FindProjectRoot(file));
        Assert.True(UnityProjectLocator.IsInUnityAssets(file));
        Assert.False(UnityProjectLocator.IsInUnityAssets(Path.Combine(dir.Path, "ProjectSettings", "x.png")));
        Assert.Null(UnityProjectLocator.FindProjectRoot(Path.GetTempPath()));
    }

    [Fact]
    public void RenameWithCompanions_MovesMetaAndSidecar()
    {
        using var dir = new TempDir();
        var a = dir.File("a.png");
        var doc = new Document(new PixelImage(2, 2));
        DocumentIO.Save(doc, a, writeMeta: true);
        var guid = UnityMeta.Read(a + ".meta").Guid;
        var b = dir.File("b.png");
        DocumentIO.RenameWithCompanions(a, b);
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(b + ".gpixel.json"));
        Assert.Equal(guid, UnityMeta.Read(b + ".meta").Guid);
    }
}
