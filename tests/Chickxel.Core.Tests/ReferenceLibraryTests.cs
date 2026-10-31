using System.Drawing;
using System.Text.Json;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.References;

namespace Chickxel.Core.Tests;

public class ReferenceLibraryTests : IDisposable
{
    private readonly TempDir _dir = new();
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Dispose() => _dir.Dispose();

    private ReferenceLibraryOptions Options(IReadOnlyList<BuiltInReference>? builtIns = null) => new()
    {
        Clock = () => _now,
        BuiltIns = builtIns ?? [],
        Korean = true,
    };

    private ReferenceLibrary Open(IReadOnlyList<BuiltInReference>? builtIns = null) => ReferenceLibrary.Open(_dir.Path, Options(builtIns));

    private static readonly Color32 Red = new(200, 30, 30), Dark = new(40, 10, 10), Green = new(30, 180, 60), Blue = new(30, 60, 200);

    /// <summary>가운데 원(빨강, 테두리 어두운 색).</summary>
    private static PixelImage Blob(int size, Color32 fill, Color32 outline)
    {
        var img = new PixelImage(size, size);
        double c = (size - 1) / 2.0, r = size * 0.4;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double d = Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                if (d <= r - 1) img.Pixels[y * size + x] = fill;
                else if (d <= r) img.Pixels[y * size + x] = outline;
            }
        return img;
    }

    /// <summary>세로 막대(칼 모양).</summary>
    private static PixelImage Bar(int w, int h, Color32 color)
    {
        var img = new PixelImage(w, h);
        for (int y = 1; y < h - 1; y++)
            for (int x = w / 2 - 1; x <= w / 2; x++)
                img.Pixels[y * w + x] = color;
        return img;
    }

    private void Png(string name, PixelImage img) => PngIO.Save(img, _dir.File(name));

    [Fact]
    public void Analyze_counts_colors_orders_palette_by_use_and_builds_silhouette()
    {
        var img = new PixelImage(8, 4);
        for (int i = 0; i < 6; i++) img.Pixels[i] = Red;
        img.Pixels[8] = Blue;
        img.Pixels[9] = Blue;
        img.Pixels[16] = new Color32(1, 2, 3, 128);
        var s = ReferenceAnalysis.Analyze([img]);
        Assert.Equal(3, s.Colors);
        Assert.Equal([Red, Blue, new Color32(1, 2, 3, 128)], s.Palette);
        Assert.Equal((8, 4, 1), (s.Width, s.Height, s.Frames));

        // 왼쪽 위 2×2 불투명 사각형만 있는 그림: 실루엣은 경계 상자를 꽉 채운다.
        var square = new PixelImage(10, 10);
        square.FillRect(new Rectangle(0, 0, 2, 2), Red);
        var sil = ReferenceAnalysis.SilhouetteOf(square);
        Assert.Equal(Silhouette.Bits, Enumerable.Range(0, 256).Count(i => sil.Get(i % 16, i / 16)));
        Assert.Equal(1, sil.Aspect);

        // 가로로 긴 막대는 가로/세로 비가 크다. 빈 그림은 빈 실루엣.
        var wide = new PixelImage(20, 4);
        wide.FillRect(new Rectangle(0, 1, 20, 2), Red);
        Assert.Equal(10, ReferenceAnalysis.SilhouetteOf(wide).Aspect);
        Assert.True(ReferenceAnalysis.SilhouetteOf(new PixelImage(4, 4)).IsEmpty);
    }

    [Fact]
    public void Palette_is_capped_but_color_count_is_not()
    {
        var img = new PixelImage(10, 10);
        for (int i = 0; i < 100; i++) img.Pixels[i] = new Color32((byte)i, 0, 0);
        var s = ReferenceAnalysis.Analyze([img]);
        Assert.Equal(100, s.Colors);
        Assert.Equal(ReferenceAnalysis.PaletteCap, s.Palette.Count);
    }

    [Fact]
    public void Silhouette_hex_round_trips()
    {
        var sil = ReferenceAnalysis.SilhouetteOf(Blob(16, Red, Dark));
        var back = Silhouette.Parse(sil.ToHex(), sil.Aspect);
        Assert.Equal(sil, back);
        Assert.Equal(0, sil.Hamming(back));
    }

    [Fact]
    public void Open_indexes_files_dropped_into_the_folder_with_auto_metadata()
    {
        Png("red_slime.png", Blob(16, Red, Dark));
        File.WriteAllText(_dir.File("shading.md"), "# 명암 메모\n빛은 왼쪽 위에서.\n");
        File.WriteAllText(_dir.File("ignored.bmp"), "x");
        var lib = Open();

        Assert.Equal(2, lib.Items.Count);
        var slime = lib.Find("red_slime.png")!;
        Assert.Equal(ReferenceTypes.Image, slime.Type);
        Assert.Equal("red slime", slime.Title);
        Assert.Equal(ReferenceKinds.Character, slime.Kind);
        Assert.Equal((16, 16, 1, 2), (slime.Width, slime.Height, slime.Frames, slime.Colors));
        Assert.Equal(16, slime.Hash.Length);
        Assert.Equal(CaptionSources.None, slime.CaptionedBy);
        Assert.True(slime.NeedsCaption);
        Assert.Equal(1, lib.UncaptionedCount);
        Assert.Equal(_now, slime.AddedUtc);

        var note = lib.Find("shading.md")!;
        Assert.Equal(ReferenceTypes.Note, note.Type);
        Assert.Equal("명암 메모", note.Title);
        Assert.Contains("왼쪽 위", lib.ReadText(note));
        Assert.False(note.NeedsCaption);

        Assert.True(File.Exists(lib.IndexPath));
    }

    [Fact]
    public void Rescan_adds_new_drops_missing_and_reanalyzes_changed_files()
    {
        Png("a.png", Blob(16, Red, Dark));
        Png("b.png", Bar(16, 16, Blue));
        var lib = Open();
        lib.Update("a.png", "My slime", "round red blob", ["slime"], "character", CaptionSources.User);

        _now = _now.AddHours(1);
        Png("c.png", Bar(8, 8, Green));
        File.Delete(_dir.File("b.png"));
        Png("a.png", Blob(32, Green, Dark));
        File.SetLastWriteTimeUtc(_dir.File("a.png"), DateTime.UtcNow.AddMinutes(5));
        var result = lib.Rescan();

        Assert.Equal(new ReferenceRescanResult(1, 1, 1), result);
        Assert.Null(lib.Find("b.png"));
        Assert.NotNull(lib.Find("c.png"));
        var a = lib.Find("a.png")!;
        Assert.Equal(32, a.Width);
        Assert.Equal("My slime", a.Title);
        Assert.Equal("round red blob", a.Description);
        Assert.Contains(Green.ToHex(), a.Palette);

        // 아무것도 안 바뀌면 다시 살펴봐도 그대로.
        Assert.False(lib.Rescan().Any);
    }

    [Fact]
    public void Moved_or_restored_file_keeps_its_caption()
    {
        Png("hero.png", Blob(16, Red, Dark));
        var lib = Open();
        lib.Update("hero.png", "Hero", "front view", ["hero"], null, CaptionSources.Ai);

        Directory.CreateDirectory(_dir.File("chars"));
        File.Move(_dir.File("hero.png"), _dir.File("chars/hero2.png"));
        lib.Rescan();
        var moved = lib.Find("chars/hero2.png")!;
        Assert.Equal(("Hero", "front view", CaptionSources.Ai), (moved.Title, moved.Description, moved.CaptionedBy));

        // 지웠다가(휴지통) 다시 넣어도 정보가 돌아온다(색인을 다시 열어도).
        var bytes = File.ReadAllBytes(_dir.File("chars/hero2.png"));
        File.Delete(_dir.File("chars/hero2.png"));
        lib.Rescan();
        Assert.Empty(lib.Items);
        File.WriteAllBytes(_dir.File("back.png"), bytes);
        var reopened = Open();
        Assert.Equal("Hero", reopened.Find("back.png")!.Title);
    }

    [Fact]
    public void Index_round_trips_all_metadata()
    {
        Png("sword.png", Bar(16, 16, Blue));
        var lib = Open();
        lib.Update("sword.png", "칼", "푸른 칼", ["칼", "무기", "칼"], "item", CaptionSources.Ai);

        var again = Open();
        var a = lib.Find("sword.png")!;
        var b = again.Find("sword.png")!;
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
        Assert.Equal(["칼", "무기"], b.Tags);
        Assert.Equal(ReferenceKinds.Item, b.Kind);
        Assert.Equal(CaptionSources.Ai, b.CaptionedBy);
        Assert.False(again.IndexWasCorrupt);

        var json = File.ReadAllText(again.IndexPath);
        Assert.Contains("\"captionedBy\": \"ai\"", json);
        Assert.Contains("\"silhouette\"", json);
    }

    [Fact]
    public void Corrupt_index_is_rebuilt_from_files()
    {
        Png("a.png", Blob(16, Red, Dark));
        File.WriteAllText(_dir.File(ReferenceLibrary.IndexFileName), "{ not json");
        var lib = Open();
        Assert.True(lib.IndexWasCorrupt);
        Assert.Single(lib.Items);
        Assert.Equal("a", lib.Find("a.png")!.Title);
        // 다시 쓴 색인은 읽을 수 있다.
        Assert.False(Open().IndexWasCorrupt);

        // 밖으로 새는 id는 버린다.
        File.WriteAllText(_dir.File(ReferenceLibrary.IndexFileName), """{"version":1,"items":[{"id":"../evil.png","title":"x"}]}""");
        Assert.Null(Open().Find("../evil.png"));
    }

    [Fact]
    public void Text_search_ranks_title_and_tag_matches_in_korean_and_english()
    {
        Png("slime_green.png", Blob(16, Green, Dark));
        Png("bat.png", Blob(16, Blue, Dark));
        Png("sword.png", Bar(16, 16, Blue));
        var lib = Open();
        lib.Update("slime_green.png", "초록 슬라임", "통통 튀는 젤리 몬스터", ["슬라임", "몬스터", "slime"], "character", CaptionSources.User);
        lib.Update("bat.png", "박쥐", "날개를 편 박쥐 적", ["박쥐", "bat"], "character", CaptionSources.User);
        lib.Update("sword.png", "Steel sword", "A short steel sword icon", ["sword", "weapon"], "item", CaptionSources.User);

        var ko = lib.Search(new ReferenceQuery { Text = "슬라임처럼", IncludeRules = false });
        Assert.Equal("slime_green.png", ko[0].Item.Id);
        Assert.Equal(1, ko[0].Score);

        var en = lib.Search(new ReferenceQuery { Text = "weapon sword", IncludeRules = false });
        Assert.Equal("sword.png", en[0].Item.Id);
        Assert.DoesNotContain(en, h => h.Item.Id == "bat.png");

        // 파일 이름·영어 태그로도 찾는다.
        Assert.Equal("bat.png", lib.Search(new ReferenceQuery { Text = "bat" })[0].Item.Id);
    }

    [Fact]
    public void Search_finds_notes_and_rules_with_snippets()
    {
        File.WriteAllText(_dir.File("walk.md"), "# 걷기 메모\n걷기 사이클은 접지 포즈에서 몸이 가장 낮다. 6프레임으로 그린다.");
        var lib = Open();
        lib.EnsureUserRules();
        Assert.Contains("내 그림 규칙", lib.ReadUserRules());

        var hits = lib.Search(new ReferenceQuery { Text = "걷기 접지", Limit = 10 });
        var note = Assert.Single(hits, h => h.Item.Id == "walk.md");
        Assert.Contains("접지", note.Snippet);
        Assert.Contains(hits, h => h.Item.Id == BuiltInReferences.RulesId);

        var outline = lib.Search(new ReferenceQuery { Text = "outline 외곽선", Limit = 10 });
        Assert.Contains(outline, h => h.Item.Id == BuiltInReferences.RulesId && h.Snippet!.Contains("외곽선"));
        Assert.Contains("1px", lib.ReadText(lib.Find(BuiltInReferences.RulesId)!));
        Assert.Equal(ReferenceTypes.Rules, lib.Find(ReferenceLibrary.RulesFileName)!.Type);
        // rules.md는 목록 항목이 아니다.
        Assert.DoesNotContain(lib.Items, i => i.Id == ReferenceLibrary.RulesFileName);
    }

    [Fact]
    public void Filters_apply_to_images_only_and_drop_notes()
    {
        Png("s16.png", Blob(16, Red, Dark));
        Png("s32.png", Blob(32, Red, Dark));
        Png("t16.png", Bar(16, 16, Green));
        File.WriteAllText(_dir.File("n.md"), "note");
        var p = SpriteProject.FromImage(Blob(16, Red, Dark));
        p.AddFrame();
        p.SetCel(0, 1, Blob(16, Blue, Dark));
        ProjectIO.Save(p, _dir.File("anim.chickxel"));
        var lib = Open();
        lib.Update("t16.png", null, null, null, "tile", CaptionSources.User);

        static string[] Ids(IReadOnlyList<ReferenceHit> hits) => [.. hits.Select(h => h.Item.Id).Order()];
        Assert.Equal(["anim.chickxel", "s16.png", "t16.png"], Ids(lib.Search(new ReferenceQuery { Width = 16, Height = 16, Limit = 0 })));
        Assert.Equal(["t16.png"], Ids(lib.Search(new ReferenceQuery { Kind = "tile", Limit = 0 })));
        Assert.Equal(["anim.chickxel"], Ids(lib.Search(new ReferenceQuery { MinFrames = 2, Limit = 0 })));
        Assert.Equal(["s32.png"], Ids(lib.Search(new ReferenceQuery { MinSide = 20, Limit = 0 })));
        Assert.Equal(["s16.png", "s32.png", "t16.png"], Ids(lib.Search(new ReferenceQuery { MaxColors = 2, MaxFrames = 1, Limit = 0 })));
        Assert.Equal(5, lib.Search(new ReferenceQuery { Limit = 0, IncludeRules = false }).Count);

        var anim = lib.Find("anim.chickxel")!;
        Assert.Equal(2, anim.Frames);
        Assert.Equal(3, anim.Colors);
        Assert.Equal(2, lib.LoadFrames(anim).Images.Count);
    }

    [Fact]
    public void Similarity_orders_by_palette_and_silhouette()
    {
        Png("same.png", Blob(16, Red, Dark));
        Png("other_color.png", Blob(16, Green, new Color32(0, 60, 0)));
        Png("other_shape.png", Bar(16, 16, Red));
        var lib = Open();

        var canvas = ReferenceSignature.Of([Blob(24, Red, Dark)]);
        var hits = lib.Search(new ReferenceQuery { SimilarTo = canvas, Limit = 0 });
        Assert.Equal("same.png", hits[0].Item.Id);
        Assert.True(hits[0].Score > hits[1].Score);
        Assert.Equal("other_shape.png", hits[2].Item.Id);

        // 글과 섞어도 글이 맞는 것만 남는다.
        lib.Update("other_color.png", "green blob", null, null, null, CaptionSources.User);
        var mixed = lib.Search(new ReferenceQuery { Text = "blob", SimilarTo = canvas, Limit = 0 });
        Assert.Equal(["other_color.png"], mixed.Select(h => h.Item.Id));
    }

    [Fact]
    public void AddImage_writes_png_or_project_and_keeps_metadata()
    {
        var lib = Open();
        var item = lib.AddImage(ReferenceFrames.Single(Blob(16, Red, Dark)), "빨간 공", null, "둥근 공", ["공"], "item", CaptionSources.Ai);
        Assert.Equal("빨간 공.png", item.Id);
        Assert.Equal(("빨간 공", "둥근 공", ReferenceKinds.Item, CaptionSources.Ai), (item.Title, item.Description, item.Kind, item.CaptionedBy));
        Assert.True(File.Exists(_dir.File("빨간 공.png")));

        var again = lib.AddImage(ReferenceFrames.Single(Blob(16, Red, Dark)), "빨간 공", null, null, null, null, CaptionSources.Ai);
        Assert.Equal("빨간 공-2.png", again.Id);
        Assert.Equal(("빨간 공", CaptionSources.None), (again.Title, again.CaptionedBy));
        Assert.True(again.NeedsCaption);

        var anim = lib.AddImage(new ReferenceFrames([Blob(16, Red, Dark), Blob(16, Blue, Dark)], [80, 160]), "bounce", "Bounce", null, null, null, CaptionSources.User);
        Assert.Equal("bounce.chickxel", anim.Id);
        Assert.Equal(2, anim.Frames);
        Assert.Equal([80, 160], lib.LoadFrames(anim).DurationsMs);
    }

    [Fact]
    public void ImportFiles_copies_supported_files_with_unique_names()
    {
        using var src = new TempDir();
        PngIO.Save(Blob(16, Red, Dark), src.File("a.png"));
        File.WriteAllText(src.File("rules.md"), "# imported");
        File.WriteAllText(src.File("x.bmp"), "x");
        Png("a.png", Bar(8, 8, Blue));
        var lib = Open();
        var added = lib.ImportFiles([src.File("a.png"), src.File("rules.md"), src.File("x.bmp")]);
        Assert.Equal(["a-2.png", "rules-2.md"], added.Select(i => i.Id).Order());
        Assert.False(File.Exists(lib.UserRulesPath));
    }

    [Fact]
    public void Built_in_examples_are_clean_read_only_and_searchable()
    {
        var builtIns = BuiltInReferences.Examples();
        Assert.Equal(["builtin/slime", "builtin/sword", "builtin/hero", "builtin/slime_bounce", "builtin/grass"], builtIns.Select(b => b.Item.Id));
        foreach (var b in builtIns)
        {
            Assert.True(b.Item.BuiltIn);
            Assert.InRange(b.Item.Colors, 3, 16);
            Assert.NotEmpty(b.Item.Title);
            Assert.NotEmpty(b.Item.Description);
            Assert.NotEmpty(b.Item.Tags);
            Assert.False(b.Item.NeedsCaption);
            Assert.Equal(b.Item.Frames, b.Frames.Images.Count);
        }
        Assert.Equal(4, builtIns.Single(b => b.Item.Id == "builtin/slime_bounce").Item.Frames);
        Assert.Equal((32, 32), builtIns.Single(b => b.Item.Id == "builtin/hero").Item is var h ? (h.Width, h.Height) : default);

        var lib = Open(builtIns);
        Assert.Equal(5, lib.AllItems.Count());
        Assert.Contains(lib.Search(new ReferenceQuery { Text = "slime", Limit = 3 }), x => x.Item.Id == "builtin/slime");
        Assert.Throws<InvalidOperationException>(() => lib.Update("builtin/slime", "x", null, null, null, CaptionSources.User));
        lib.ShowBuiltIns = false;
        Assert.Empty(lib.AllItems);
        Assert.Empty(lib.Search(new ReferenceQuery { Text = "slime" }));
        Assert.NotNull(lib.Find("builtin/slime"));
    }
}
