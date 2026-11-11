using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>그라데이션 에셋 JSON 왕복, 예전 그라데이션 프리셋 파일 읽기, 기본 그라데이션과 기본값 되돌리기.</summary>
public class GradientPresetsTests
{
    private static GradientPreset Example(string name = "Evening") => new()
    {
        Name = name,
        Stops = [new(0, new Color32(190, 20, 90, 127)), new(0.4, new Color32(250, 130, 10)), new(1, new Color32(16, 30, 170))],
        AlphaStops = [new(0, 10), new(0.7, 240), new(1, 100)],
        Interpolation = GradientInterpolation.Smooth, Dither = GradientDither.Bayer4,
        ColorSpace = GradientColorSpace.Hsl, Angle = 83, Shape = EffectGradientShape.Angular,
        Animated = true, Reverse = true, Cycles = 4, Easing = EffectEasing.EaseInOut,
        PingPong = true, Favorite = true,
    };

    /// <summary>그라데이션 에셋을 JSON 글자로 썼다가 엄격하게(패키지처럼) 다시 읽는다.</summary>
    private static GradientPreset RoundTrip(GradientPreset gradient, string id = "author.pack.g")
    {
        var json = JsonNode.Parse(GradientAssetFormat.Write(id, gradient).ToJsonString());
        var context = new AssetReadContext("assets/gradients/g.json", strict: true);
        var read = GradientAssetFormat.Read(json, context);
        Assert.Empty(context.Issues);
        Assert.Equal(id, read!.Id);
        return read.Gradient;
    }

    /// <summary>예전 사용자 데이터가 없는 새 저장소(기본 그라데이션만 있음).</summary>
    private static AssetStore SeededStore(TempDir dir)
    {
        var store = AssetStore.Open(Path.Combine(dir.Path, "Assets"));
        AssetMigration.Run(store, dir.Path);
        return store;
    }

    [Fact]
    public void RoundTrip_PreservesEverySettingAndIndependentStopLists_FavoriteIsNotWritten()
    {
        var originalStops = new List<GradientStop>(Example().Stops);
        var originalAlpha = new List<GradientAlphaStop>(Example().AlphaStops);
        var preset = Example() with { Stops = originalStops, AlphaStops = originalAlpha };
        originalStops[0] = new GradientStop(0, Color32.Black);
        originalAlpha[0] = new GradientAlphaStop(0, 0);

        // 즐겨찾기는 컴퓨터별 상태라 에셋 파일에 쓰지 않는다
        Assert.Null(GradientAssetFormat.Write("author.pack.g", preset)["favorite"]);
        var parsed = RoundTrip(preset);
        Assert.Equal(preset.Name, parsed.Name);
        Assert.Equal(preset.Stops, parsed.Stops);
        Assert.Equal(preset.AlphaStops, parsed.AlphaStops);
        Assert.Equal((preset.Interpolation, preset.Dither, preset.ColorSpace, preset.Angle, preset.Shape,
                preset.Animated, preset.Reverse, preset.Cycles, preset.Easing, preset.PingPong),
            (parsed.Interpolation, parsed.Dither, parsed.ColorSpace, parsed.Angle, parsed.Shape,
                parsed.Animated, parsed.Reverse, parsed.Cycles, parsed.Easing, parsed.PingPong));
        Assert.False(parsed.Favorite);
        Assert.NotSame(preset.Stops, parsed.Stops);
        Assert.NotSame(preset.AlphaStops, parsed.AlphaStops);
        Assert.Equal(new Color32(190, 20, 90, 127), parsed.Stops[0].Color);
        Assert.Equal((byte)10, parsed.AlphaStops[0].Alpha);

        var effect = parsed.ToGradient();
        Assert.Equal(parsed.Stops, effect.Stops);
        Assert.Equal(parsed.AlphaStops, effect.AlphaStops);
        Assert.Equal(parsed.ColorSpace, effect.ColorSpace);
        Assert.Equal(parsed.Shape, effect.Shape);
        Assert.Equal(parsed.Easing, effect.Easing);
        var recaptured = GradientPreset.Capture("Copy", effect);
        Assert.Equal(parsed.Stops, recaptured.Stops);
        Assert.Equal(parsed.AlphaStops, recaptured.AlphaStops);
        Assert.NotSame(effect.Stops, recaptured.Stops);
        Assert.NotSame(effect.AlphaStops, recaptured.AlphaStops);
    }

    [Fact]
    public void Snapshot_RetainsRenderedColorsForGradientToolAndEffect()
    {
        var source = new EffectColorGradient
        {
            Stops = [new(0, new Color32(233, 30, 11, 100)), new(1, new Color32(13, 81, 201, 233))],
            AlphaStops = [new(0, 255), new(1, 80)],
            ColorSpace = GradientColorSpace.OkLab, Interpolation = GradientInterpolation.Steps,
            Dither = GradientDither.Bayer2, Shape = EffectGradientShape.Square,
            Angle = 72, Reverse = true, Cycles = 3, Animated = true,
            Easing = EffectEasing.EaseOut, PingPong = true,
        };
        var restored = RoundTrip(GradientPreset.Capture("Colors", source)).ToGradient();
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                double t = (x + y) / 6.0;
                Assert.Equal(Gradient.Sample(source.Stops, source.AlphaStops, t, source.Interpolation,
                        source.Dither, x, y, source.ColorSpace),
                    Gradient.Sample(restored.Stops, restored.AlphaStops, t, restored.Interpolation,
                        restored.Dither, x, y, restored.ColorSpace));
            }
        Assert.Equal(source.Angle, restored.Angle);
        Assert.Equal(source.Shape, restored.Shape);
        Assert.Equal(source.Reverse, restored.Reverse);
        Assert.Equal(source.Cycles, restored.Cycles);
        Assert.Equal(source.Animated, restored.Animated);
        Assert.Equal(source.Easing, restored.Easing);
        Assert.Equal(source.PingPong, restored.PingPong);
    }

    [Fact]
    public void Capture_NoAlphaStops_MakesOpaqueCurveWithoutChangingSampling()
    {
        var effect = new EffectColorGradient
        {
            Stops = [new(0, new Color32(100, 40, 20, 128)), new(1, new Color32(20, 100, 40, 240))],
        };
        var preset = GradientPreset.Capture("Opaque", effect);
        Assert.Equal([new GradientAlphaStop(0, 255), new GradientAlphaStop(1, 255)], preset.AlphaStops);
        var loaded = RoundTrip(preset);
        Assert.Equal(Gradient.Sample(effect.Stops, effect.AlphaStops, 0.3, colorSpace: effect.ColorSpace),
            Gradient.Sample(loaded.Stops, loaded.AlphaStops, 0.3, colorSpace: loaded.ColorSpace));
    }

    [Fact]
    public void LegacyParse_EmptyCatalogAndDuplicateNames_LastCaseInsensitiveEntryWinsInFirstSlot()
    {
        Assert.Empty(AssetMigration.ReadLegacyGradients("""{"version":2,"presets":[]}"""));
        const string onDisk = """
            {"version":2,"presets":[{"name":"  SKY  ","favorite":true,"colorSpace":"Hsl","stops":[{"position":0,"color":"#FF0000"},{"position":1,"color":"#0000FF"}]},
            {"name":"Moon","stops":[{"position":0,"color":"#FFFFFF"},{"position":1,"color":"#000000"}]},
            {"name":"sky","stops":[{"position":0,"color":"#00FF00"},{"position":1,"color":"#0000FF"}]},
            {"name":"Sun","stops":[{"position":0,"color":"#FF0000"},{"position":1,"color":"#0000FF"}]},
            {"name":"sun","favorite":true,"stops":[{"position":0,"color":"#00FF00"},{"position":1,"color":"#0000FF"}]}]}
            """;
        var deduplicated = AssetMigration.ReadLegacyGradients(onDisk);
        Assert.Equal(["sky", "Moon", "sun"], deduplicated.Select(p => p.Name).ToArray());
        Assert.False(deduplicated[0].Favorite);
        Assert.Equal(new Color32(0, 255, 0), deduplicated[0].Stops[0].Color);
        Assert.Equal(new Color32(0, 255, 0), deduplicated[2].Stops[0].Color);
        Assert.True(deduplicated[2].Favorite);
    }

    [Fact]
    public void LegacyParse_OlderMissingFields_DefaultsToRgbAndOpaqueAlpha()
    {
        const string json = """
            {"version":1,"presets":[{"name":"Older","stops":[
              {"position":0,"color":"#FF000080"},{"position":1,"color":"#0000FF"}
            ]}]}
            """;
        var old = AssetMigration.ReadLegacyGradients(json)[0];
        Assert.Equal("Older", old.Name);
        Assert.Equal(GradientColorSpace.Rgb, old.ColorSpace);
        Assert.Equal(GradientDither.None, old.Dither);
        Assert.Equal(GradientInterpolation.Smooth, old.Interpolation);
        Assert.Equal(EffectGradientShape.Linear, old.Shape);
        Assert.Equal(1, old.Cycles);
        Assert.Equal([new GradientAlphaStop(0, 255), new GradientAlphaStop(1, 255)], old.AlphaStops);
        Assert.False(old.Favorite);
        Assert.Equal(new Color32(255, 0, 0, 128), old.Stops[0].Color);
        Assert.Equal(Gradient.Sample(old.Stops, [], 0.5, colorSpace: GradientColorSpace.Rgb),
            Gradient.Sample(old.Stops, old.AlphaStops, 0.5, colorSpace: old.ColorSpace));
        // 새 형식으로 옮겨 써도 RGB로 남는다(새 기본값 OkLch로 바뀌지 않음)
        Assert.Equal(GradientColorSpace.Rgb, RoundTrip(old).ColorSpace);
    }

    /// <summary>1판 파일은 2판에서 더한 기본 그라데이션(Fire, Forest, Grayscale) 중 없는 이름만 받는다(대소문자 무시).</summary>
    [Fact]
    public void LegacyVersionOne_GainsNewBuiltInsMissingByName()
    {
        var upgraded = AssetMigration.ReadLegacyGradients("""
            {"version":1,"presets":[{"name":"Mine","stops":[{"position":0,"color":"#FF0000"},{"position":1,"color":"#0000FF"}]},
            {"name":"fire","stops":[{"position":0,"color":"#000000"},{"position":1,"color":"#FFFFFF"}]}]}
            """);
        Assert.Equal(["Mine", "fire", "Forest", "Grayscale"], upgraded.Select(p => p.Name).ToArray());
        Assert.Equal(Color32.Black, upgraded[1].Stops[0].Color);
        var builtIns = GradientPresets.BuiltIns();
        Assert.Equal(builtIns.Single(p => p.Name == "Grayscale").Stops, upgraded[3].Stops);

        // 2판 파일은 사용자가 지운 기본 그라데이션을 되살리지 않는다
        var second = AssetMigration.ReadLegacyGradients("""
            {"version":2,"presets":[{"name":"Mine","stops":[{"position":0,"color":"#FF0000"},{"position":1,"color":"#0000FF"}]}]}
            """);
        Assert.Equal(["Mine"], second.Select(p => p.Name).ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"version\":3,\"presets\":[]}")]
    [InlineData("{\"version\":0,\"presets\":[]}")]
    [InlineData("{\"version\":1,\"presets\":[null]}")]
    [InlineData("{\"version\":1,\"presets\":[{\"name\":\"Blank\",\"stops\":[]}]}")]
    [InlineData("{\"version\":1,\"presets\":[{\"name\":\"Broken\",\"stops\":[{\"position\":0,\"color\":\"#123456\"},{\"position\":1,\"color\":\"not-a-color\"}]}]}")]
    public void LegacyParse_DoesNotTreatMalformedCatalogAsEmpty(string json)
        => Assert.Throws<InvalidDataException>(() => AssetMigration.ReadLegacyGradients(json));

    [Theory]
    [InlineData("\"alphaStops\":[{\"position\":0,\"alpha\":-1},{\"position\":1,\"alpha\":255}]")]
    [InlineData("\"alphaStops\":[{\"position\":0,\"alpha\":256},{\"position\":1,\"alpha\":255}]")]
    [InlineData("\"alphaStops\":[]")]
    [InlineData("\"interpolation\":\"Future\"")]
    [InlineData("\"colorSpace\":\"999\"")]
    [InlineData("\"angle\":360")]
    [InlineData("\"cycles\":0")]
    [InlineData("\"stops\":[{\"position\":-0.01,\"color\":\"#FFFFFF\"},{\"position\":1,\"color\":\"#000000\"}]")]
    public void InvalidValues_AreRejectedByLegacyParseAndAssetFormat(string field)
    {
        string stops = field.StartsWith("\"stops\"", StringComparison.Ordinal) ? "" :
            "\"stops\":[{\"position\":0,\"color\":\"#FFFFFF\"},{\"position\":1,\"color\":\"#000000\"}],";
        var legacy = "{\"version\":1,\"presets\":[{\"name\":\"Broken\"," + stops + field + "}]}";
        Assert.Throws<InvalidDataException>(() => AssetMigration.ReadLegacyGradients(legacy));

        var asset = JsonNode.Parse("{\"schemaVersion\":1,\"id\":\"author.pack.broken\",\"name\":\"Broken\"," + stops + field + "}");
        foreach (bool strict in new[] { true, false })
        {
            var context = new AssetReadContext("assets/gradients/broken.json", strict);
            Assert.Null(GradientAssetFormat.Read(asset, context));
            Assert.Contains(context.Issues, issue => issue.Code == AssetIssueCode.InvalidField);
        }
    }

    [Fact]
    public void BuiltIns_AreFreshEditableCopiesWithExpectedColors()
    {
        var first = GradientPresets.BuiltIns();
        var second = GradientPresets.BuiltIns();
        var rainbow = Assert.Single(first, p => p.Name == "Rainbow");
        Assert.Equal([new Color32(255, 0, 0), new Color32(255, 140, 0), new Color32(255, 255, 0),
            new Color32(0, 190, 50), new Color32(0, 80, 255), new Color32(148, 0, 211)],
            rainbow.Stops.Select(s => s.Color).ToArray());
        var repeated = Assert.Single(first, p => p.Name == "Repeating Rainbow");
        Assert.Equal(repeated.Stops[0].Color, repeated.Stops[^1].Color);
        Assert.Equal(0, repeated.Stops[0].Position);
        Assert.Equal(1, repeated.Stops[^1].Position);
        Assert.Contains(first, p => p.Name == "Sunset");
        Assert.Contains(first, p => p.Name == "Ocean");
        Assert.Contains(first, p => p.Name == "Transparent Fade");
        Assert.NotSame(first[0].Stops, second[0].Stops);
        Assert.NotSame(first[0].AlphaStops, second[0].AlphaStops);
        // 모든 기본 그라데이션은 개인 에셋으로 저장할 수 있는 값이다
        Assert.All(first, GradientAssetFormat.Validate);
    }

    [Fact]
    public void FailedSave_LeavesStoredGradientsIntact()
    {
        using var dir = new TempDir();
        var store = SeededStore(dir);
        var saved = store.SaveGradient(null, Example());
        byte[] before = File.ReadAllBytes(store.LibraryPath);

        Assert.Throws<InvalidDataException>(() => store.SaveGradient(saved.Id, Example() with { Cycles = 17 }));
        Assert.Throws<InvalidDataException>(() => store.SaveGradient(null, Example() with { Name = "  " }));
        Assert.Equal(before, File.ReadAllBytes(store.LibraryPath));
        var reopened = AssetStore.Open(store.Root);
        var kept = Assert.Single(reopened.Catalog.Gradients, e => e.Id == saved.Id).Gradient!;
        Assert.Equal((4, "Evening", true), (kept.Cycles, kept.Name, kept.Favorite));
    }

    /// <summary>
    /// 기본 그라데이션은 공식 패키지로 설치된다: 예전 판이 이 컴퓨터의 그라데이션으로 넣어 둔 고치지 않은 사본은 지우고(즐겨찾기는 공식 것으로), 고친 사본·직접 만든 것은 남는다.
    /// 지운 공식 그라데이션은 그라데이션 되돌리기로만 다시 들어온다.
    /// </summary>
    [Fact]
    public void OfficialGradients_ReplaceUneditedLocalCopies_KeepEditedAndCustom_RestoreBringsDeletedBack()
    {
        using var dir = new TempDir();
        var store = SeededStore(dir);
        var builtIns = GradientPresets.BuiltIns();
        foreach (var builtIn in builtIns) store.SaveGradient(null, builtIn with { Favorite = builtIn.Name == "Ocean" });
        var sunset = store.Catalog.Gradients.Single(e => e.Name == "Sunset");
        var edited = store.SaveGradient(sunset.Id, sunset.Gradient! with { Stops = [new(0, Color32.Black), new(1, Color32.Black)] });
        var mine = store.SaveGradient(null, Example("Mine"));

        Assert.Empty(BundledPackages.Sync(store).Errors);

        var official = store.Catalog.Gradients.Where(e => e.Source == AssetSources.Bundled).ToList();
        Assert.Equal(OfficialGradientPackages.Ids.Select(OfficialGradientPackages.PackageId), official.Select(e => e.Store!.PackageId));
        Assert.All(official, e => Assert.Equal(("chickxel", false), (e.Author!.Id, e.Modified)));
        Assert.Equal(builtIns.Select(p => p.Name), official.Select(e => e.Name));
        Assert.True(official.Single(e => e.Name == "Ocean").Favorite);
        // 남은 이 컴퓨터의 그라데이션: 고친 노을과 직접 만든 것뿐
        Assert.Equal(new[] { edited.Id, mine.Id }.Order(), store.Catalog.Gradients.Where(e => e.Source == AssetSources.Local).Select(e => e.Id).Order());

        store.Remove(official.Single(e => e.Name == "Fire").Id);
        Assert.False(BundledPackages.Sync(store).Changed);
        var restored = BundledPackages.Sync(store, AssetKinds.Gradient);
        Assert.Equal([OfficialGradientPackages.PackageId("fire")], restored.Installed);
        Assert.Equal(builtIns.Count + 2, store.Catalog.Gradients.Count);
    }
}
