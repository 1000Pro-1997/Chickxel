using System.Drawing;
using System.IO.Compression;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.Core.Slicing;

namespace Gpixel.Core.Tests;

public class AtlasTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    private static string CopyPlayerFixture(TempDir dir)
    {
        var png = dir.File("player@2x.png");
        File.Copy(TestUtil.Fixture("player@2x.png"), png);
        File.Copy(TestUtil.Fixture("player@2x.png.meta"), png + ".meta");
        return png;
    }

    // ───────────── 가져오기 ─────────────

    [Fact]
    public void Import_PlayerFixture_BecomesAtlasWithExactParts()
    {
        using var dir = new TempDir();
        var png = CopyPlayerFixture(dir);

        var analysis = ProjectImport.Analyze(png);
        Assert.Equal(ProjectKind.Atlas, analysis.SuggestedKind);
        Assert.Equal(4, analysis.SliceCount);
        Assert.False(analysis.SlicesSameSize);
        Assert.False(analysis.NeedsKindChoice);

        var result = ProjectImport.Import(analysis);
        var p = result.Project;
        Assert.True(result.Imported);
        Assert.True(p.IsAtlas);
        Assert.Equal(dir.File("player@2x.gpixel"), p.Path);
        Assert.False(p.IsDirty);
        Assert.Equal(new Size(32, 32), p.AtlasSourceSize);
        Assert.True(p.Export.KeepImportedRects);
        Assert.Equal(16f, p.Sprite.PixelsPerUnit);
        Assert.Equal(["Head", "Body", "Arm", "lag"], p.Sprites.Select(s => s.Name));
        Assert.Equal([new Size(16, 10), new Size(12, 16), new Size(6, 14), new Size(6, 6)], p.Sprites.Select(s => s.Size));
        Assert.Equal([new Pivot(0.5f, 0f), new Pivot(0.5f, 0f), new Pivot(0.5f, 0.9285714f), new Pivot(0.5f, 1f)],
            p.Sprites.Select(s => s.Pivot));
        Assert.Equal([new Rectangle(2, 2, 16, 10), new Rectangle(4, 14, 12, 16), new Rectangle(22, 4, 6, 14), new Rectangle(22, 20, 6, 6)],
            p.Sprites.Select(s => s.SourceRect!.Value));

        var image = PngIO.Load(png);
        for (int i = 0; i < 4; i++)
            Assert.True(p.FlattenSprite(i).ContentEquals(image.Crop(p.Sprites[i].SourceRect!.Value)));

        // 애니메이션으로 강제하면 예전 동작(4프레임)
        Assert.Equal(4, ProjectImport.Open(png, kind: ProjectKind.Animation).Project.Frames.Count);
    }

    [Fact]
    public void Import_SameSizeSlices_NeedsChoice_AndAtlasChoiceWorks()
    {
        using var dir = new TempDir();
        var png = dir.File("tiles.png");
        var doc = new Document(TestUtil.RandomImage(32, 16, opaque: true), png);
        doc.Sprite.Mode = SpriteMode.Multiple;
        doc.Slices.Add(new Slice("a", new Rectangle(0, 0, 16, 16), Pivot.Center, new SpriteBorder(1, 2, 3, 4)));
        doc.Slices.Add(new Slice("b", new Rectangle(16, 0, 16, 16), Pivot.BottomCenter));
        DocumentIO.Save(doc, png, writeMeta: false);

        var analysis = ProjectImport.Analyze(png);
        Assert.True(analysis.NeedsKindChoice);
        Assert.Equal(ProjectKind.Animation, analysis.SuggestedKind);
        var atlas = ProjectImport.Import(analysis, ProjectKind.Atlas).Project;
        Assert.True(atlas.IsAtlas);
        Assert.Equal(new SpriteBorder(1, 2, 3, 4), atlas.Sprites[0].Border);
        Assert.Equal(Pivot.BottomCenter, atlas.Sprites[1].Pivot);

        // 칸 없는 PNG를 아틀라스로 → 이미지 전체 1개
        var plain = dir.File("plain.png");
        PngIO.Save(TestUtil.RandomImage(5, 7), plain);
        var single = ProjectImport.Open(plain, kind: ProjectKind.Atlas).Project;
        var sprite = Assert.Single(single.Sprites);
        Assert.Equal(("plain", new Size(5, 7)), (sprite.Name, sprite.Size));

        // 이미 .gpixel이 있으면 묻지 않는다
        ProjectIO.Save(atlas, ProjectImport.GetProjectPathForPng(png));
        var again = ProjectImport.Analyze(png);
        Assert.False(again.NeedsKindChoice);
        Assert.True(ProjectImport.Import(again).Project.IsAtlas);
    }

    // ───────────── 내보내기 ─────────────

    [Fact]
    public void Export_KeepImportedRects_ReproducesFixtureMeta()
    {
        using var dir = new TempDir();
        var png = CopyPlayerFixture(dir);
        var original = UnityMeta.Read(png + ".meta");
        var originalImage = PngIO.Load(png);
        var p = ProjectImport.Open(png).Project;

        var saved = ProjectIO.SaveWithExport(p);
        Assert.NotNull(saved.Export);
        Assert.True(saved.Export!.MetaWritten);
        var meta = UnityMeta.Read(png + ".meta");
        Assert.Equal(original.Guid, meta.Guid);
        Assert.Equal(SpriteMode.Multiple, meta.SpriteMode);
        Assert.Equal(original.Sprites.Select(s => (s.Name, s.UnityRect, s.Alignment, s.Pivot, s.Border, s.SpriteId, s.InternalId)),
            meta.Sprites.Select(s => (s.Name, s.UnityRect, s.Alignment, s.Pivot, s.Border, s.SpriteId, s.InternalId)));
        var sheet = PngIO.Load(png);
        Assert.Equal(new Size(32, 32), sheet.Size);
        foreach (var s in p.Sprites)
            Assert.True(sheet.Crop(s.SourceRect!.Value).ContentEquals(originalImage.Crop(s.SourceRect!.Value)));

        // 머리가 커져서 원래 칸에 안 들어가면 빈 곳으로 옮겨지고, 이름 기준 ID는 그대로
        p.ResizeSprite(0, 18, 10, Anchor.MiddleLeft);
        ProjectIO.SaveWithExport(p);
        var moved = UnityMeta.Read(png + ".meta");
        Assert.Equal(original.Guid, moved.Guid);
        var head = moved.Sprites.Single(s => s.Name == "Head");
        Assert.Equal(new Size(18, 10), head.UnityRect.Size);
        Assert.Equal(original.Sprites[0].InternalId, head.InternalId);
        Assert.Equal(original.Sprites[0].SpriteId, head.SpriteId);
        foreach (var s in moved.Sprites.Where(s => s.Name != "Head"))
            Assert.Equal(original.Sprites.Single(o => o.Name == s.Name).UnityRect, s.UnityRect);
        var size = PngIO.ReadSize(png);
        var rects = moved.Sprites.Select(s => s.ToTopLeftRect(size.Height)).ToList();
        AssertNoOverlap(rects, 0);
    }

    [Fact]
    public void Export_PackedAtlas_WithoutKeepRects()
    {
        var p = new SpriteProject(8, 8, ProjectKind.Atlas);
        p.AddSprite("wide", 20, 4);
        p.AddSprite("tall", 3, 12);
        p.SetSpriteBorder(1, new SpriteBorder(1, 1, 1, 1));
        var cel = p.BeginSpriteStroke(0, 1);
        cel.Clear(Red);
        p.CommitCelStroke("칠");
        p.Edit("배치", ProjectChange.Settings, () =>
        {
            p.Export.Spacing = 2;
            p.Export.AtlasPowerOfTwo = true;
            p.Export.AtlasOrder = AtlasPackOrder.LargestFirst;
        });

        var sheet = ProjectExport.BuildSheet(p, "parts");
        Assert.Equal(["sprite", "wide", "tall"], sheet.Entries.Select(e => e.Name));
        Assert.True(IsPow2(sheet.Image.Width) && IsPow2(sheet.Image.Height));
        AssertNoOverlap(sheet.Entries.Select(e => e.Rect).ToList(), 2);
        Assert.Equal(new Point(0, 0), sheet.Entries[1].Rect.Location); // 가장 큰 것부터
        var wide = sheet.Entries[1].Rect;
        Assert.Equal(Red, sheet.Image.Get(wide.X + 19, wide.Y + 3));
        Assert.Equal(new SpriteBorder(1, 1, 1, 1), sheet.Entries[1].Border);

        var doc = ProjectExport.BuildDocument(p, "parts");
        Assert.Equal(SpriteMode.Multiple, doc.Sprite.Mode);
        Assert.Equal(3, doc.Slices.Count);
        Assert.Empty(doc.Animations);
        Assert.Equal(new SpriteBorder(1, 1, 1, 1), doc.FindSlice("wide")!.Border);

        // 메타 왕복: 테두리·피벗
        var meta = UnityMeta.Parse(UnityMeta.Generate(doc, null));
        Assert.Equal(new SpriteBorder(1, 1, 1, 1), meta.Sprites[1].Border);
    }

    // ───────────── 배치기 ─────────────

    [Fact]
    public void Packer_NoOverlap_SpacingMaxWidthAndPowerOfTwo()
    {
        var rng = new Random(3);
        var sizes = Enumerable.Range(0, 40).Select(_ => new Size(rng.Next(1, 30), rng.Next(1, 30))).ToList();
        foreach (var order in new[] { AtlasPackOrder.KeepOrder, AtlasPackOrder.LargestFirst })
            foreach (var spacing in new[] { 0, 1, 3 })
            {
                var r = AtlasPacker.Pack(sizes, new AtlasPackOptions(MaxWidth: 100, Spacing: spacing, PowerOfTwo: true, Order: order));
                Assert.Equal(sizes, r.Rects.Select(x => x.Size));
                AssertNoOverlap(r.Rects.ToList(), spacing);
                Assert.All(r.Rects, x => Assert.True(x.Right <= 100 && x.X >= 0 && x.Y >= 0));
                Assert.All(r.Rects, x => Assert.True(x.Right <= r.Size.Width && x.Bottom <= r.Size.Height));
                Assert.True(IsPow2(r.Size.Width) && IsPow2(r.Size.Height));
            }

        // 같은 높이 + 순서 유지 = 행 채우기
        var rows = AtlasPacker.Pack(Enumerable.Repeat(new Size(10, 10), 5).ToList(), new AtlasPackOptions(MaxWidth: 32, Spacing: 1));
        Assert.Equal([new Point(0, 0), new Point(11, 0), new Point(22, 0), new Point(0, 11), new Point(11, 11)],
            rows.Rects.Select(x => x.Location));
        Assert.Equal(new Size(32, 21), rows.Size);

        // 자동 너비: 가장 넓은 스프라이트 이상
        var auto = AtlasPacker.Pack([new Size(50, 2), new Size(3, 3)], new AtlasPackOptions());
        Assert.True(auto.Size.Width >= 50);
    }

    [Fact]
    public void Packer_FixedRects_KeptWhenFitting_OthersPlacedAround()
    {
        Rectangle?[] fixedRects = [new Rectangle(0, 0, 10, 10), new Rectangle(5, 5, 10, 10), null, new Rectangle(20, 0, 4, 4)];
        Size[] sizes = [new(10, 10), new(10, 10), new(6, 6), new(5, 5)];
        var r = AtlasPacker.Pack(sizes, new AtlasPackOptions(MaxWidth: 30, MinSize: new Size(40, 12)), fixedRects);
        Assert.Equal(new Rectangle(0, 0, 10, 10), r.Rects[0]);      // 고정
        Assert.NotEqual(new Point(5, 5), r.Rects[1].Location);       // 고정 칸과 겹쳐서 새로 배치
        Assert.NotEqual(new Point(20, 0), r.Rects[3].Location);      // 커져서 원래 칸에 안 들어감
        AssertNoOverlap(r.Rects.ToList(), 0);
        Assert.True(r.Size.Width >= 40 && r.Size.Height >= 12);
    }

    // ───────────── 편집·실행취소 ─────────────

    [Fact]
    public void AtlasOps_AreUndoable()
    {
        var p = new SpriteProject(4, 4, ProjectKind.Atlas);
        Assert.Single(p.Sprites);
        var cel = p.BeginSpriteStroke(0, 0);
        cel.Set(0, 3, Red);
        p.CommitCelStroke("점");

        var arm = p.AddSprite("arm", 2, 6);
        Assert.Equal(1, p.IndexOf(arm));
        var dup = p.DuplicateSprite(0);
        Assert.Equal("sprite_1", dup.Name);
        Assert.Equal(Red, p.GetSpriteCel(0, 1)!.Get(0, 3));
        Assert.NotSame(p.GetSpriteCel(0, 0), p.GetSpriteCel(0, 1));

        p.RenameSprite(2, "leg");
        p.RenameSprite(2, "leg"); // 같은 이름 그대로는 아무 일 없음
        Assert.Equal(4, p.History.UndoCount);
        Assert.Throws<ArgumentException>(() => p.RenameSprite(1, "sprite"));
        Assert.Throws<ArgumentException>(() => p.RenameSprite(1, " "));
        Assert.Equal("leg_1", p.UniqueSpriteName("leg"));

        p.SetSpritePivot(0, Pivot.BottomCenter); // 픽셀 (2,4)
        p.SetSpriteBorder(0, new SpriteBorder(1, 0, 1, 0));
        p.ResizeSprite(0, 6, 5, Anchor.TopRight);  // 오프셋 (2,0)
        var s0 = p.Sprites[0];
        Assert.Equal(new Size(6, 5), s0.Size);
        Assert.Equal(Red, p.GetSpriteCel(0, 0)!.Get(2, 3));
        var (px, py) = s0.Pivot.ToLocalPixel(6, 5);
        Assert.Equal((4f, 4f), (px, py));

        p.MoveSprite(0, 2);
        Assert.Same(s0, p.Sprites[2]);
        p.DeleteSprite(1);
        Assert.Equal(2, p.Sprites.Count);

        // 전부 되돌리기
        while (p.History.UndoCount > 1) p.Undo(); // 첫 스트로크만 남김
        Assert.Single(p.Sprites);
        Assert.Same(s0, p.Sprites[0]);
        Assert.Equal(("sprite", new Size(4, 4), Pivot.Center, SpriteBorder.None), (s0.Name, s0.Size, s0.Pivot, s0.Border));
        Assert.Equal(Red, p.GetSpriteCel(0, 0)!.Get(0, 3));
        p.Undo();
        Assert.Null(p.GetSpriteCel(0, 0));
        while (p.Redo()) { }
        Assert.Equal(2, p.Sprites.Count);
        Assert.Equal(new Size(6, 5), p.Sprites[1].Size);
        Assert.Equal("sprite_1", p.Sprites[0].Name); // [dup, leg, s0] → leg 삭제 → [dup, s0]

        Assert.Throws<InvalidOperationException>(() =>
        {
            var q = new SpriteProject(2, 2, ProjectKind.Atlas);
            q.DeleteSprite(0);
        });
    }

    [Fact]
    public void LayerOps_IncludeSpriteCels()
    {
        var p = new SpriteProject(3, 3, ProjectKind.Atlas);
        p.AddSprite("b", 2, 5);
        p.AddLayer();
        var c = p.BeginSpriteStroke(0, 1);
        c.Set(0, 0, Red);
        p.CommitCelStroke("아래");
        c = p.BeginSpriteStroke(1, 1);
        c.Set(1, 4, Blue);
        p.CommitCelStroke("위");

        p.DuplicateLayer(1);
        Assert.Equal(Blue, p.GetSpriteCel(2, 1)!.Get(1, 4));
        p.Undo();
        p.MergeLayerDown(1);
        var merged = p.GetSpriteCel(0, 1)!;
        Assert.Equal(new Size(2, 5), merged.Size);
        Assert.Equal(Red, merged.Get(0, 0));
        Assert.Equal(Blue, merged.Get(1, 4));
        p.SetLayerProperties(0, visible: false);
        Assert.True(p.FlattenSprite(1).IsRegionEmpty(new Rectangle(0, 0, 2, 5)));
        Assert.Equal(Blue, p.FlattenSprite(1, includeHidden: true).Get(1, 4));
        p.AddLayer();
        p.DeleteLayer(0);
        Assert.Null(p.GetSpriteCel(0, 1));
    }

    // ───────────── 파일 ─────────────

    [Fact]
    public void ProjectIO_AtlasRoundTrip()
    {
        using var dir = new TempDir();
        var png = CopyPlayerFixture(dir);
        var p = ProjectImport.Open(png).Project;
        p.AddLayer(name: "그림자");
        var c = p.BeginSpriteStroke(1, 3);
        c.Set(1, 1, Blue);
        p.CommitCelStroke("점");
        p.SetSpriteBorder(1, new SpriteBorder(1, 2, 3, 4));
        p.Edit("배치", ProjectChange.Settings, () =>
        {
            p.Export.AtlasMaxWidth = 64;
            p.Export.AtlasPowerOfTwo = true;
            p.Export.AtlasOrder = AtlasPackOrder.LargestFirst;
            p.Export.KeepImportedRects = false;
        });
        var path = dir.File("parts.gpixel");
        ProjectIO.Save(p, path);

        using (var zip = ZipFile.OpenRead(path))
        {
            var names = zip.Entries.Select(e => e.FullName).Where(n => n.StartsWith("atlas/")).OrderBy(n => n).ToList();
            Assert.Equal(["atlas/S0_L0.png", "atlas/S1_L0.png", "atlas/S2_L0.png", "atlas/S3_L0.png", "atlas/S3_L1.png"], names);
        }

        var back = ProjectIO.Load(path);
        Assert.True(back.IsAtlas);
        Assert.Equal(new Size(32, 32), back.AtlasSourceSize);
        Assert.Equal(p.Sprites.Select(s => (s.Name, s.Size, s.Pivot, s.Border, s.SourceRect)),
            back.Sprites.Select(s => (s.Name, s.Size, s.Pivot, s.Border, s.SourceRect)));
        Assert.Equal((64, true, AtlasPackOrder.LargestFirst, false),
            (back.Export.AtlasMaxWidth, back.Export.AtlasPowerOfTwo, back.Export.AtlasOrder, back.Export.KeepImportedRects));
        for (int s = 0; s < 4; s++)
            Assert.True(p.FlattenSprite(s, includeHidden: true).ContentEquals(back.FlattenSprite(s, includeHidden: true)));
        Assert.Equal(Blue, back.GetSpriteCel(1, 3)!.Get(1, 1));
        Assert.Equal(ProjectExport.BuildSheet(back, "x").Image.Size, ProjectIO.LoadThumbnail(path).Size);

        // 예전(종류 없는) 파일은 애니메이션
        var anim = new SpriteProject(2, 2);
        var animPath = dir.File("anim.gpixel");
        ProjectIO.Save(anim, animPath);
        Assert.Equal(ProjectKind.Animation, ProjectIO.Load(animPath).Kind);
    }

    // ───────────── 도우미 ─────────────

    private static bool IsPow2(int v) => v > 0 && (v & (v - 1)) == 0;

    /// <summary>모든 쌍이 겹치지 않고 간격 이상 떨어져 있는지.</summary>
    private static void AssertNoOverlap(IReadOnlyList<Rectangle> rects, int spacing)
    {
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
                Assert.False(Rectangle.Inflate(rects[i], spacing, spacing).IntersectsWith(rects[j]),
                    $"{rects[i]}와 {rects[j]}가 간격 {spacing} 안에서 겹칩니다.");
    }
}
