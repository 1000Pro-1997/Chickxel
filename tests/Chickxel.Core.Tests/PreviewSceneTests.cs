using System.Drawing;
using System.Runtime.CompilerServices;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 효과 만들기의 미리보기 장면: 저장 PNG 왕복과 한도, 편집 문서 왕복, 장면 위 렌더(병아리만 둔 장면 = <see cref="AssetPreview.Render"/>, 장면 레이어를 읽는 효과),
/// 진단 실행, 패키지·개인 저장소에서의 보관, 다른 레이어를 읽는 스크립트의 강제 태그.
/// 신뢰 결정은 프로세스 전체 상태라 시험마다 원문 끝에 주석을 붙여 해시를 따로 쓴다.
/// </summary>
public class PreviewSceneTests
{
    private static readonly Color32 Green = new(40, 200, 60), Magenta = new(255, 0, 255);

    private static string Trusted(string source, [CallerMemberName] string test = "")
    {
        var hash = ScriptLibrary.Add(source + "\n// " + test + "\n");
        ScriptLibrary.SetTrust(hash, true);
        return hash;
    }

    /// <summary>불투명 픽셀의 빨강을 진행(0~255)으로 바꾸는 움직이는 스크립트(크기·피벗을 쓰지 않음).</summary>
    private static ScriptEffect Tint([CallerMemberName] string test = "") => new()
    {
        ScriptHash = Trusted("""
            effect({ name: "tint", animated: true, render(img, ctx) {
              const d = img.data; const v = Math.round(ctx.progress * 255);
              for (let i = 0; i < d.length; i += 4) if (d[i + 3] > 0) d[i] = v;
            } });
            """, test),
    };

    /// <summary>레이어 고르기 설정(기본값 = 장면의 "ground")으로 고른 레이어가 불투명한 자리를 자홍으로 칠하는 스크립트.</summary>
    private static ScriptEffect MarkGround([CallerMemberName] string test = "") => new()
    {
        ScriptHash = Trusted("""
            effect({ name: "mark ground", settings: [{ key: "ground", label: "Ground", type: "layer", default: "ground" }], render(img, ctx) {
              const o = ctx.layer("ground");
              if (!o) return;
              const d = img.data;
              for (let i = 0; i < d.length; i += 4)
                if (o.data[i + 3] > 0) { d[i] = 255; d[i + 1] = 0; d[i + 2] = 255; d[i + 3] = 255; }
            } });
            """, test),
    };

    private static PixelImage Chick => PreviewChicks.Load(PreviewChicks.Default);

    /// <summary>병아리 크기에 맞춘 캔버스(폭 3배, 높이 2배)와 발 자리(가운데, 바닥에서 4px 위).</summary>
    private static (int W, int H, Point Anchor) Canvas(PixelImage chick)
    {
        int w = Math.Min(PreviewScene.MaxSize, chick.Width * 3), h = Math.Min(PreviewScene.MaxSize, chick.Height * 2);
        return (w, h, new Point(w / 2, h - 4));
    }

    /// <summary>두 프레임, 세 레이어(이름·불투명도·보이기가 모두 다름), 병아리는 1번 자리.</summary>
    private static PreviewScene SampleScene(int width, int height, Point anchor) => new(width, height, 2,
    [
        new PreviewSceneLayer("ground", [TestUtil.RandomImage(width, height, 1), TestUtil.RandomImage(width, height, 2)]),
        new PreviewSceneLayer("길 \"path\" <1>", [TestUtil.RandomImage(width, height, 3), new PixelImage(width, height)], 128, Visible: false),
        new PreviewSceneLayer("sky", [new PixelImage(width, height), TestUtil.RandomImage(width, height, 4, opaque: true)], 7),
    ], 1, anchor);

    private static void AssertSameScene(PreviewScene expected, PreviewScene actual)
    {
        Assert.Equal((expected.Width, expected.Height, expected.FrameCount, expected.ChickIndex, expected.ChickAnchor),
            (actual.Width, actual.Height, actual.FrameCount, actual.ChickIndex, actual.ChickAnchor));
        Assert.Equal(expected.Layers.Select(l => (l.Name, l.Opacity, l.Visible)), actual.Layers.Select(l => (l.Name, l.Opacity, l.Visible)));
        for (int l = 0; l < expected.Layers.Count; l++)
            for (int f = 0; f < expected.FrameCount; f++)
                Assert.True(expected.Layers[l].Frames[f].ContentEquals(actual.Layers[l].Frames[f]), $"layer {l} frame {f}");
    }

    // ───────────── 저장 PNG ─────────────

    [Fact]
    public void Png_round_trips_pixels_names_opacity_visibility_anchor_and_frames()
    {
        var scene = SampleScene(20, 12, new Point(9, 11));
        byte[] png = scene.ToPng();
        Assert.Equal(png, scene.ToPng());

        var read = PreviewScene.FromPng(png);
        AssertSameScene(scene, read);
        Assert.Equal(png, read.ToPng());

        // 예전 앱에는 평범한 미리보기 PNG(크기 한도 안, 디코드됨) — 셀 6개가 한 줄에
        var issues = new List<AssetIssue>();
        AssetPackageValidator.CheckPreview("previews/x.scene.png", png, AssetPackageLimits.Default, issues);
        Assert.Empty(issues);
        var sheet = PngIO.Load(new MemoryStream(png));
        Assert.Equal((6 * 20, 12), (sheet.Width, sheet.Height));

        // 레이어가 없는 장면(병아리만)도 왕복한다
        var bare = new PreviewScene(30, 30, 3, [], 0, new Point(15, 30));
        AssertSameScene(bare, PreviewScene.FromPng(bare.ToPng()));
    }

    [Fact]
    public void Large_cells_wrap_into_a_grid_within_the_sheet_limit()
    {
        // 200×100 셀 8개 → 한 줄 2칸 × 4줄 = 400×400
        var images = Enumerable.Range(0, 4).Select(f => TestUtil.RandomImage(200, 100, f)).ToArray();
        var scene = new PreviewScene(200, 100, 4, [new("a", images), new("b", Enumerable.Reverse(images).ToArray())], 2, new Point(100, 100));
        byte[] png = scene.ToPng();
        var sheet = PngIO.Load(new MemoryStream(png));
        Assert.Equal((400, 400), (sheet.Width, sheet.Height));
        AssertSameScene(scene, PreviewScene.FromPng(png));
    }

    [Fact]
    public void Constructor_rejects_values_over_the_limits()
    {
        PixelImage Img(int w = 16, int h = 16) => new(w, h);
        var ok = new Point(8, 16);
        Assert.Throws<ArgumentException>(() => new PreviewScene(PreviewScene.MaxSize + 1, 16, 1, [], 0, ok));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 0, 1, [], 0, ok));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 16, PreviewScene.MaxFrames + 1, [], 0, ok));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 16, 1,
            [.. Enumerable.Range(0, PreviewScene.MaxLayers + 1).Select(i => new PreviewSceneLayer("l" + i, [Img()]))], 0, ok));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 16, 1, [new("a", [Img()])], 2, ok));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 16, 1, [], 0, new Point(17, 3)));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 16, 1, [new(new string('x', PreviewScene.MaxNameLength + 1), [Img()])], 0, ok));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 16, 1, [new(" ground", [Img()])], 0, ok));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 16, 1, [new("", [Img()])], 0, ok));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 16, 1, [new("a\nb", [Img()])], 0, ok));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 16, 2, [new("a", [Img()])], 0, ok));
        Assert.Throws<ArgumentException>(() => new PreviewScene(16, 16, 1, [new("a", [Img(16, 15)])], 0, ok));
        // 256×256 셀은 512×512에 4개까지: 2레이어 × 3프레임은 들어가지 않는다
        Assert.Equal(4, PreviewScene.MaxCells(256, 256));
        var big = Img(256, 256);
        Assert.Throws<ArgumentException>(() => new PreviewScene(256, 256, 3, [new("a", [big, big, big]), new("b", [big, big, big])], 0, new Point(1, 1)));
        Assert.Equal(64, PreviewScene.MaxCells(64, 64));
    }

    /// <summary>메타데이터 글 덩어리를 마음대로 넣은 PNG.</summary>
    private static byte[] Crafted(PixelImage sheet, string? json)
    {
        using var stream = new MemoryStream();
        if (json is null) PngIO.Save(sheet, stream);
        else PngIO.SaveWithText(sheet, stream, PreviewScene.TextKeyword, json);
        return stream.ToArray();
    }

    [Fact]
    public void FromPng_rejects_garbage_and_bad_metadata()
    {
        const string Good = """{"schemaVersion":1,"width":4,"height":4,"frames":1,"layers":[{"name":"a","opacity":255,"visible":true}],"chickIndex":0,"chickAnchor":{"x":2,"y":4}}""";
        var sheet = new PixelImage(4, 4);
        Assert.Single(PreviewScene.FromPng(Crafted(sheet, Good)).Layers);

        var random = new byte[300];
        new Random(5).NextBytes(random);
        var bad = new List<byte[]>
        {
            random,
            Array.Empty<byte>(),
            Crafted(sheet, null), // 메타데이터 없음
            Crafted(sheet, "not json"),
            Crafted(sheet, Good.Replace("\"frames\":1", "\"frames\":1,\"extra\":2", StringComparison.Ordinal)),
            Crafted(sheet, Good.Replace("\"visible\":true", "\"visible\":true,\"blend\":1", StringComparison.Ordinal)),
            Crafted(sheet, Good.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)),
            Crafted(sheet, Good.Replace("\"width\":4", "\"width\":5", StringComparison.Ordinal)), // 그림 크기와 다름
            Crafted(sheet, Good.Replace("\"width\":4", "\"width\":300", StringComparison.Ordinal)),
            Crafted(sheet, Good.Replace("\"opacity\":255", "\"opacity\":256", StringComparison.Ordinal)),
            Crafted(sheet, Good.Replace("\"chickIndex\":0", "\"chickIndex\":2", StringComparison.Ordinal)),
            Crafted(sheet, Good.Replace("\"y\":4", "\"y\":9", StringComparison.Ordinal)),
            Crafted(sheet, Good.Replace("\"name\":\"a\"", "\"name\":\"" + new string('n', 65) + "\"", StringComparison.Ordinal)),
            Crafted(sheet, Good.Replace("\"name\":\"a\"", "\"name\":7", StringComparison.Ordinal)),
        };
        // CRC가 틀린 덩어리
        var corrupt = Crafted(sheet, Good);
        corrupt[40] ^= 0xFF;
        bad.Add(corrupt);
        foreach (var png in bad)
            Assert.Throws<InvalidDataException>(() => PreviewScene.FromPng(png));
    }

    // ───────────── 편집 문서 ─────────────

    [Fact]
    public void ToProject_and_FromProject_round_trip_and_follow_a_moved_chick()
    {
        var chick = Chick;
        var (w, h, anchor) = Canvas(chick);
        var scene = SampleScene(w, h, anchor);
        var (project, chickLayer) = scene.ToProject(chick, "chick");

        Assert.Equal(["ground", "chick", "길 \"path\" <1>", "sky"], project.Layers.Select(l => l.Name));
        Assert.Same(project.Layers[scene.ChickIndex], chickLayer);
        Assert.Equal(2, project.Frames.Count);
        Assert.Equal(((byte)128, false), (project.Layers[2].Opacity, project.Layers[2].Visible));
        Assert.False(project.IsDirty);
        // 병아리의 불투명 영역 아래 가운데가 발 자리
        var b = project.GetCel(chickLayer, project.Frames[1])!.GetOpaqueBounds();
        Assert.Equal(anchor, new Point(b.X + b.Width / 2, b.Bottom));

        AssertSameScene(scene, PreviewScene.FromProject(project, chickLayer));

        // 그룹은 무시하고, 병아리를 옮기면 발 자리도 옮겨진다
        project.AddGroup("group");
        var moved = new PixelImage(w, h);
        var at = scene.ChickOffset(chick);
        moved.Blit(chick, at.X + 3, at.Y - 2);
        project.SetCel(project.IndexOf(chickLayer), 0, moved);
        var back = PreviewScene.FromProject(project, chickLayer);
        Assert.Equal(new Point(anchor.X + 3, anchor.Y - 2), back.ChickAnchor);
        Assert.Equal(3, back.Layers.Count);

        // 병아리 레이어가 비면 장면을 만들 수 없다
        project.SetCel(project.IndexOf(chickLayer), 0, null);
        Assert.Throws<InvalidOperationException>(() => PreviewScene.FromProject(project, chickLayer));
        Assert.Throws<InvalidOperationException>(() => PreviewScene.FromProject(project, new Layer("other")));
    }

    [Fact]
    public void Default_scene_has_an_empty_ground_below_the_chick()
    {
        var chick = Chick;
        var scene = PreviewScene.CreateDefault(chick);
        Assert.Equal((1, 1, PreviewScene.DefaultGroundName), (scene.FrameCount, scene.ChickIndex, Assert.Single(scene.Layers).Name));
        Assert.True(scene.Layers[0].Frames[0].IsRegionEmpty(scene.Layers[0].Frames[0].Bounds));
        var at = scene.ChickOffset(chick);
        Assert.True(at.X >= 0 && at.Y >= 0 && at.X + chick.Width <= scene.Width && at.Y + chick.Height <= scene.Height);
        AssertSameScene(scene, PreviewScene.FromPng(scene.ToPng()));
    }

    // ───────────── 장면 위 렌더 ─────────────

    /// <summary>병아리만 둔 장면의 칸 = 같은 칸의 <see cref="AssetPreview.Render"/>를 병아리 자리(움직이면 움직임 범위의 왼쪽)에 놓은 캔버스.</summary>
    private static void AssertSameAsAssetPreview(IReadOnlyList<LayerEffect> effects, bool moves)
    {
        var chick = Chick;
        const int Steps = 6;
        var (w, h, anchor) = Canvas(chick);
        var scene = new PreviewScene(w, h, 1, [], 0, anchor);
        var render = new PreviewSceneRender(scene, effects, chick, Steps);
        var at = scene.ChickOffset(chick);
        int left = at.X - (moves ? (Steps - 1) / 2 : 0);
        var padding = LayerEffects.Padding(effects, chick.Width + (moves ? Steps : 0), chick.Height);
        for (int step = 0; step < Steps; step++)
        {
            var expected = new PixelImage(w, h);
            expected.Blit(AssetPreview.Render(effects, chick, step, Steps), left - padding.Left, at.Y - padding.Top);
            Assert.True(expected.ContentEquals(render.Render(step)), $"step {step}");
            var original = new PixelImage(w, h);
            original.Blit(AssetPreview.Original(effects, chick, step, Steps), left - padding.Left, at.Y - padding.Top);
            Assert.True(original.ContentEquals(render.Original(step)), $"original {step}");
        }
    }

    [Fact]
    public void Chick_only_scene_renders_the_same_pixels_as_the_chick_preview()
    {
        AssertSameAsAssetPreview([Tint(), new OutlineEffect { Thickness = 1 }], moves: false);
    }

    [Fact]
    public void Chick_only_scene_moves_the_chick_like_the_chick_preview_for_afterimages()
    {
        AssertSameAsAssetPreview([Tint(), new AfterimageEffect { Count = 2 }], moves: true);
    }

    /// <summary>바닥 4줄이 초록인 1프레임 장면(병아리는 바닥 위).</summary>
    private static PreviewScene GroundScene(PixelImage chick)
    {
        var (w, h, anchor) = Canvas(chick);
        var ground = new PixelImage(w, h);
        ground.FillRect(new Rectangle(0, h - 4, w, 4), Green);
        return new PreviewScene(w, h, 1, [new PreviewSceneLayer("ground", [ground])], 1, anchor);
    }

    [Fact]
    public void Scene_layers_are_visible_to_layer_reading_scripts()
    {
        var chick = Chick;
        var scene = GroundScene(chick);
        var effects = new LayerEffect[] { MarkGround() };
        var render = new PreviewSceneRender(scene, effects, chick, 4);
        int bottom = scene.Height - 1;
        Assert.Equal(Magenta, render.Render(0)[1, bottom]);
        Assert.Equal(Green, render.Original(0)[1, bottom]);
        Assert.Equal(default, render.Render(0)[1, 0]);
        // 장면 프레임은 칸마다 되풀이(1프레임 장면이면 모든 칸이 같은 바닥)
        Assert.Equal(Magenta, render.Render(3)[1, bottom]);

        // 장면 레이어를 숨기면 바닥은 그리지 않지만 효과는 여전히 바닥을 읽는다(같은 캔버스 크기)
        var solo = render.Render(0, hideSceneLayers: true);
        Assert.Equal(scene.Width, solo.Width);
        Assert.Equal(scene.Height, solo.Height);
        Assert.Equal(Magenta, solo[1, bottom]);
        Assert.Equal(default, render.Original(0, hideSceneLayers: true)[1, bottom]);

        // 병아리만 그리는 미리보기에는 다른 레이어가 없어 표시가 없다
        var alone = AssetPreview.Render(effects, chick, 0, 4);
        Assert.Equal(-1, Array.IndexOf(alone.Pixels, Magenta));
    }

    [Fact]
    public void Probe_runs_the_script_with_the_scene_layers_and_reports_errors()
    {
        var chick = Chick;
        var hash = Trusted("""
            effect({ name: 'needs ground', settings: [{ key: 'ground', type: 'layer', default: 'ground' }],
              render(img, ctx) { if (!ctx.layer('ground')) throw new Error('no ground'); } });
            """);
        var program = ScriptLibrary.Runnable(hash)!;
        LayerEffect[] effects = [new ScriptEffect { ScriptHash = hash }];

        var grounded = new PreviewSceneRender(GroundScene(chick), effects, chick, 4).Probe(program, null, 1, CancellationToken.None);
        Assert.Null(grounded.Error);
        Assert.True(grounded.Elapsed >= TimeSpan.Zero);

        var (w, h, anchor) = Canvas(chick);
        var bare = new PreviewSceneRender(new PreviewScene(w, h, 1, [], 0, anchor), effects, chick, 4).Probe(program, null, 1, CancellationToken.None);
        Assert.IsType<ScriptEffectException>(bare.Error);

        // 장면에 고른 이름의 레이어가 없으면 null → 오류, 설정 값으로 있는 레이어를 고르면 다시 괜찮다
        var sky = new PreviewScene(w, h, 1, [new PreviewSceneLayer("sky", [Dot(w, h)])], 1, anchor);
        var skyRender = new PreviewSceneRender(sky, effects, chick, 4);
        Assert.IsType<ScriptEffectException>(skyRender.Probe(program, null, 1, CancellationToken.None).Error);
        Assert.Null(skyRender.Probe(program, new Dictionary<string, object> { ["ground"] = "sky" }, 1, CancellationToken.None).Error);
    }

    private static PixelImage Dot(int width, int height)
    {
        var image = new PixelImage(width, height);
        image[0, 0] = Green;
        return image;
    }

    // ───────────── 패키지 ─────────────

    private const string GroundSource = """
        effect({ name: "Ground Mark", tags: ["ground"], settings: [{ key: "ground", label: "Ground", type: "layer", default: "ground" }], render(img, ctx) {
          const o = ctx.layer("ground");
          if (!o) return;
          for (let i = 0; i < img.data.length; i += 4) if (o.data[i + 3] > 0) img.data[i] = 255;
        } });
        """;

    private static ScriptAsset GroundScript([CallerMemberName] string test = "") => Script(GroundSource, test);

    /// <summary>레이어 고르기 설정(기본값 = 장면의 "ground")으로 고른 레이어가 불투명한 자리 중 캔버스 왼쪽 반만 자홍으로 칠하는 스크립트.</summary>
    private const string HalfMarkSource = """
        effect({ name: "Half Mark", tags: ["ground"], settings: [{ key: "target", label: "Target", type: "layer", default: "ground" }], render(img, ctx) {
          const o = ctx.layer("target");
          if (!o) return;
          const d = img.data, w = img.width;
          for (let i = 0; i < d.length; i += 4)
            if (o.data[i + 3] > 0 && (i / 4) % w < w / 2) { d[i] = 255; d[i + 1] = 0; d[i + 2] = 255; d[i + 3] = 255; }
        } });
        """;

    private static ScriptAsset HalfMarkScript([CallerMemberName] string test = "") => Script(HalfMarkSource, test);

    private static ScriptAsset Script(string source, string test)
    {
        var program = ScriptEffectProgram.Parse(source + "\n// " + test + "\n").Program!;
        ScriptLibrary.Add(program.Source);
        ScriptLibrary.SetTrust(program.ContentHash, true);
        return ScriptAsset.From("", program);
    }

    private static (byte[] Bytes, AssetManifest Manifest) Package(ScriptAsset script)
    {
        var builder = new AssetPackageBuilder(AssetSamples.Header());
        builder.Add(AssetKinds.EffectScript, script, AssetPreview.Package(script));
        builder.AddDocument("LICENSE.txt", "CC0");
        using var stream = new MemoryStream();
        var manifest = builder.Write(stream);
        return (stream.ToArray(), manifest);
    }

    private static AssetPackage Read(byte[] bytes) => AssetPackageValidator.Read(new MemoryStream(bytes, writable: false));

    [Fact]
    public void Package_carries_the_scene_through_export_install_and_reexport()
    {
        var scene = GroundScene(Chick);
        var script = GroundScript() with { Scene = scene };
        var (bytes, manifest) = Package(script);

        // 장면 파일은 manifest files에 있지만 에셋 줄은 가리키지 않는다
        const string ScenePath = "previews/ground-mark.scene.png";
        Assert.Equal(ScenePath, AssetPackageBuilder.ScenePath("assets/scripts/ground-mark.js"));
        Assert.Contains(ScenePath, manifest.Files.Keys);
        Assert.DoesNotContain(manifest.Assets, a => a.Preview == ScenePath);

        var package = Read(bytes);
        Assert.Empty(package.Warnings);
        AssertSameScene(scene, ((ScriptAsset)Assert.Single(package.Items).Data).Scene!);

        using var dir = new TempDir();
        var store = AssetStore.Open(dir.File("Assets"));
        var installed = store.Install(AssetSamples.Save(dir.Path, bytes));
        Assert.False(installed.IsBroken);
        AssertSameScene(scene, Assert.Single(installed.Entries).Script!.Scene!);
        AssertSameScene(scene, Assert.Single(AssetStore.Open(store.Root).Catalog.Packages).Entries[0].Script!.Scene!);

        using var exported = new MemoryStream();
        store.ExportPackage(installed.Id, exported);
        AssertSameScene(scene, ((ScriptAsset)Read(exported.ToArray()).Items[0].Data).Scene!);

        // 장면이 있는 스크립트의 패키지 미리보기는 장면 캔버스 전체
        var preview = PngIO.Load(new MemoryStream(AssetPreview.Package(script)!.OriginalPng!));
        Assert.Equal(AssetPreview.Upscale(new PixelImage(scene.Width, scene.Height), AssetPreview.PngMaxSize).Size, preview.Size);
    }

    private static PixelImage LoadPng(byte[]? png) => PngIO.Load(new MemoryStream(png!));

    [Fact]
    public void Scene_preview_also_draws_the_effect_without_the_scene_layers_at_the_same_size()
    {
        var scene = GroundScene(Chick);
        var preview = AssetPreview.Package(HalfMarkScript() with { Scene = scene })!;
        var (image, original, solo, soloOriginal) = (LoadPng(preview.Png), LoadPng(preview.OriginalPng), LoadPng(preview.SoloPng), LoadPng(preview.SoloOriginalPng));
        Assert.All([original, solo, soloOriginal], other => Assert.Equal(image.Size, other.Size));

        int scale = image.Width / scene.Width, bottom = (scene.Height - 1) * scale, left = scale, right = (scene.Width - 2) * scale;
        Assert.Equal(AssetPreview.Upscale(new PixelImage(scene.Width, scene.Height), AssetPreview.PngMaxSize).Size, image.Size);
        // 효과는 숨긴 바닥도 읽는다: 왼쪽 반의 바닥 자리는 둘 다 자홍
        Assert.Equal(Magenta, image[left, bottom]);
        Assert.Equal(Magenta, solo[left, bottom]);
        // 칠하지 않은 오른쪽 반: 장면 그림은 바닥(초록), 숨긴 그림은 비었다
        Assert.Equal(Green, image[right, bottom]);
        Assert.Equal(0, solo[right, bottom].A);
        Assert.Equal(Green, original[left, bottom]);
        Assert.Equal(0, soloOriginal[left, bottom].A);
        // 숨긴 원본 = 병아리 레이어만 그린 장면 원본(같은 배율)
        Assert.True(AssetPreview.Upscale(new PreviewSceneRender(scene, [], Chick, 1).Original(0, hideSceneLayers: true), AssetPreview.PngMaxSize).ContentEquals(soloOriginal));
    }

    private static PublishResult Publish(ScriptAsset script, byte[]? customPreview = null) => AssetPublishCheck.Check(new PublishRequest
    {
        Author = new AssetAuthor("pixel-cat", "Pixel Cat"),
        Id = "pixel-cat.half-mark",
        Name = "Half Mark",
        Summary = "바닥 왼쪽 반을 칠하는 효과",
        Tags = AssetPublishCheck.ParseTags("#ground #effect #" + ScriptAsset.LayerReferenceTag),
        Asset = new PublishAsset(AssetKinds.EffectScript, script, AssetPreview.Package(script)),
        CustomPreview = customPreview,
        MinAppVersion = "1.14.55",
    }, [], []);

    [Fact]
    public void Packages_carry_the_solo_previews_and_pass_the_validator()
    {
        var script = HalfMarkScript() with { Scene = GroundScene(Chick) };
        var expected = AssetPreview.Package(script)!;
        var (bytes, manifest) = Package(script);
        var package = Read(bytes);
        Assert.Empty(package.Warnings);
        string preview = Assert.Single(manifest.Assets).Preview!;
        Assert.Equal(("previews/half-mark.solo.png", "previews/half-mark.solo.original.png"),
            (AssetPackageBuilder.SoloPreviewPath(preview), AssetPackageBuilder.SoloOriginalPreviewPath(preview)));
        Assert.Equal(expected.SoloPng, package.Files[AssetPackageBuilder.SoloPreviewPath(preview)]);
        Assert.Equal(expected.SoloOriginalPng, package.Files[AssetPackageBuilder.SoloOriginalPreviewPath(preview)]);
        Assert.Equal(preview, Assert.Single(package.Manifest.Assets).Preview);

        // 게시 패키지
        var published = Publish(script);
        Assert.True(published.Ok, string.Join("\n", published.Issues));
        var publishedFiles = Read(published.Package!).Files;
        Assert.Equal(expected.SoloPng, publishedFiles[AssetPackageBuilder.SoloPreviewPath(Assert.Single(published.Manifest!.Assets).Preview!)]);

        // 개인 내보내기
        using var dir = new TempDir();
        var store = AssetStore.Open(dir.File("Assets"));
        var saved = store.SavePersonalScript(HalfMarkSource + "\n// " + nameof(Packages_carry_the_solo_previews_and_pass_the_validator) + "\n", null, script.Scene);
        using var exported = new MemoryStream();
        var exportedManifest = store.ExportPersonal([saved.Id], AssetSamples.Header(), exported, entry => AssetPreview.Package(entry.Data));
        var exportedFiles = Read(exported.ToArray()).Files;
        string exportedPreview = Assert.Single(exportedManifest.Assets).Preview!;
        Assert.Contains(AssetPackageBuilder.SoloPreviewPath(exportedPreview), exportedFiles.Keys);
        Assert.Contains(AssetPackageBuilder.SoloOriginalPreviewPath(exportedPreview), exportedFiles.Keys);
    }

    [Fact]
    public void No_solo_previews_without_scene_layers_or_with_a_custom_preview()
    {
        var script = HalfMarkScript();
        var (w, h, anchor) = Canvas(Chick);
        foreach (var asset in new[] { script, script with { Scene = new PreviewScene(w, h, 1, [], 0, anchor) } })
        {
            var preview = AssetPreview.Package(asset)!;
            Assert.NotNull(preview.OriginalPng);
            Assert.Null(preview.SoloPng);
            Assert.Null(preview.SoloOriginalPng);
            Assert.DoesNotContain(Read(Package(asset).Bytes).Files.Keys, path => path.Contains(".solo.", StringComparison.Ordinal));
        }

        // 직접 고른 미리보기는 원본처럼 숨긴 그림도 없다
        byte[] custom = AssetPreview.Package(new GradientAsset("", AssetSamples.Gradient("Cover")))!.Png;
        var result = Publish(script with { Scene = GroundScene(Chick) }, custom);
        Assert.True(result.Ok, string.Join("\n", result.Issues));
        Assert.DoesNotContain(Read(result.Package!).Files.Keys, path => path.Contains(".solo.", StringComparison.Ordinal) || path.EndsWith(".original.png", StringComparison.Ordinal));
    }

    [Fact]
    public void Broken_scene_is_skipped_with_a_warning_and_old_packages_load_without_a_scene()
    {
        var script = GroundScript() with { Scene = GroundScene(Chick) };
        var (bytes, manifest) = Package(script);
        // 그림은 멀쩡하지만 장면 메타데이터가 없는 PNG로 바꾼 패키지(해시는 다시 계산)
        var files = AssetSamples.Unzip(bytes).Where(e => e.Name != AssetManifest.FileName).ToDictionary(e => e.Name, e => e.Data);
        using (var plain = new MemoryStream())
        {
            PngIO.Save(new PixelImage(8, 8), plain);
            files["previews/ground-mark.scene.png"] = plain.ToArray();
        }
        using var broken = new MemoryStream();
        AssetPackageWriter.Write(broken, manifest, files);

        var package = Read(broken.ToArray());
        Assert.Null(((ScriptAsset)Assert.Single(package.Items).Data).Scene);
        Assert.Equal(AssetIssueCode.InvalidPreviewScene, Assert.Single(package.Warnings).Code);

        using var dir = new TempDir();
        var store = AssetStore.Open(dir.File("Assets"));
        var installed = store.Install(AssetSamples.Save(dir.Path, broken.ToArray()));
        Assert.False(installed.IsBroken);
        Assert.Null(Assert.Single(installed.Entries).Script!.Scene);
        Assert.Equal(AssetIssueCode.InvalidPreviewScene, Assert.Single(installed.Warnings).Code);

        // 장면이 없던 예전 패키지
        var (old, _) = Package(GroundScript("old package"));
        var oldPackage = Read(old);
        Assert.Empty(oldPackage.Warnings);
        Assert.Null(((ScriptAsset)Assert.Single(oldPackage.Items).Data).Scene);
        Assert.DoesNotContain(oldPackage.Files.Keys, path => path.EndsWith(".scene.png", StringComparison.Ordinal));
    }

    // ───────────── 개인 저장소 ─────────────

    [Fact]
    public void Personal_scripts_save_load_replace_export_and_delete_their_scene()
    {
        var scene = GroundScene(Chick);
        var source = GroundSource + "\n// " + nameof(Personal_scripts_save_load_replace_export_and_delete_their_scene) + "\n";
        using var dir = new TempDir();
        var store = AssetStore.Open(dir.File("Assets"));

        var saved = store.SavePersonalScript(source, null, scene);
        string file = Path.Combine(store.ScenesDirectory, saved.Id + ".png");
        Assert.True(File.Exists(file));
        AssertSameScene(scene, saved.Script!.Scene!);
        AssertSameScene(scene, AssetStore.Open(store.Root).Catalog.Find(saved.Id)!.Script!.Scene!);

        // 장면 없이 같은 원문을 다시 저장하면(효과 창의 "에셋으로 저장") 그 항목 그대로 — 장면도 그대로
        var same = store.SavePersonalScript(source, null, null);
        Assert.Equal(saved.Id, same.Id);
        Assert.NotNull(same.Script!.Scene);
        Assert.True(File.Exists(file));

        // 개인 내보내기 패키지에도 장면이 들어간다
        using (var exported = new MemoryStream())
        {
            store.ExportPersonal([saved.Id], AssetSamples.Header(), exported);
            AssertSameScene(scene, ((ScriptAsset)Read(exported.ToArray()).Items[0].Data).Scene!);
        }

        // 효과 만들기에서 장면을 지우고 저장 → 파일도 지움
        var cleared = store.SavePersonalScript(source, saved.Id, null);
        Assert.Equal(saved.Id, cleared.Id);
        Assert.Null(cleared.Script!.Scene);
        Assert.False(File.Exists(file));
        Assert.Null(AssetStore.Open(store.Root).Catalog.Find(saved.Id)!.Script!.Scene);

        // 같은 원문에 새 장면 → 그 항목의 장면만 바뀜, 지우면 파일도 지움
        var again = store.SavePersonalScript(source, null, scene);
        Assert.Equal(saved.Id, again.Id);
        Assert.True(File.Exists(file));
        store.RemovePersonal(saved.Id);
        Assert.False(File.Exists(file));
        Assert.Empty(store.LoadErrors);
    }

    // ───────────── 강제 태그 ─────────────

    [Fact]
    public void Layer_reading_scripts_get_the_layer_reference_tag()
    {
        var reads = GroundScript();
        Assert.True(reads.ReadsOtherLayers);
        Assert.Equal(["ground", ScriptAsset.LayerReferenceTag], reads.Tags);

        var declared = ScriptEffectProgram.Parse("effect({ name: 'x', tags: ['layer-reference', 'a'], settings: [{ key: 'k', type: 'layer' }], render(img, ctx) { ctx.layerEffects('k'); } });").Program!;
        Assert.Equal(["layer-reference", "a"], ScriptAsset.From("", declared).Tags);

        var plain = ScriptEffectProgram.Parse("effect({ name: 'x', tags: ['a'], render(img, ctx) { ctx.frameSource(0); } });").Program!;
        var asset = ScriptAsset.From("", plain);
        Assert.False(asset.ReadsOtherLayers);
        Assert.Equal(["a"], asset.Tags);
    }
}
