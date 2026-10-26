using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

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

    [Fact]
    public void RoundTrip_PreservesEverySettingAndIndependentStopLists()
    {
        var originalStops = new List<GradientStop>(Example().Stops);
        var originalAlpha = new List<GradientAlphaStop>(Example().AlphaStops);
        var preset = Example() with { Stops = originalStops, AlphaStops = originalAlpha };
        originalStops[0] = new GradientStop(0, Color32.Black);
        originalAlpha[0] = new GradientAlphaStop(0, 0);

        var json = GradientPresets.Serialize([preset]);
        Assert.Contains("\"colorSpace\": \"Hsl\"", json);
        Assert.Contains("\"favorite\": true", json);
        var parsed = Assert.Single(GradientPresets.Parse(json));
        Assert.Equal(preset.Name, parsed.Name);
        Assert.Equal(preset.Stops, parsed.Stops);
        Assert.Equal(preset.AlphaStops, parsed.AlphaStops);
        Assert.Equal((preset.Interpolation, preset.Dither, preset.ColorSpace, preset.Angle, preset.Shape,
                preset.Animated, preset.Reverse, preset.Cycles, preset.Easing, preset.PingPong, preset.Favorite),
            (parsed.Interpolation, parsed.Dither, parsed.ColorSpace, parsed.Angle, parsed.Shape,
                parsed.Animated, parsed.Reverse, parsed.Cycles, parsed.Easing, parsed.PingPong, parsed.Favorite));
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
        var restored = Assert.Single(GradientPresets.Parse(GradientPresets.Serialize(
            [GradientPreset.Capture("Colors", source)]))).ToGradient();
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
        var loaded = Assert.Single(GradientPresets.Parse(GradientPresets.Serialize([preset])));
        Assert.Equal(Gradient.Sample(effect.Stops, effect.AlphaStops, 0.3, colorSpace: effect.ColorSpace),
            Gradient.Sample(loaded.Stops, loaded.AlphaStops, 0.3, colorSpace: loaded.ColorSpace));
    }

    [Fact]
    public void Parse_EmptyCatalogAndDuplicateNames_LastCaseInsensitiveEntryWinsInFirstSlot()
    {
        Assert.Empty(GradientPresets.Parse("""{"version":1,"presets":[]}"""));
        var json = GradientPresets.Serialize([Example("  SKY  "), Example("Other"), Example("sky") with { Favorite = false }]);
        var parsed = GradientPresets.Parse(json);
        Assert.Equal(["sky", "Other"], parsed.Select(item => item.Name).ToArray());
        Assert.False(parsed[0].Favorite);
        Assert.Equal(2, parsed.Count);
        const string onDisk = """
            {"version":1,"presets":[{"name":"Sun","stops":[{"position":0,"color":"#FF0000"},{"position":1,"color":"#0000FF"}]},
            {"name":"Moon","stops":[{"position":0,"color":"#FFFFFF"},{"position":1,"color":"#000000"}]},
            {"name":"sun","favorite":true,"stops":[{"position":0,"color":"#00FF00"},{"position":1,"color":"#0000FF"}]}]}
            """;
        var deduplicated = GradientPresets.Parse(onDisk);
        Assert.Equal(["sun", "Moon"], deduplicated.Select(p => p.Name).ToArray());
        Assert.Equal(new Color32(0, 255, 0), deduplicated[0].Stops[0].Color);
        Assert.True(deduplicated[0].Favorite);
    }

    [Fact]
    public void Parse_OlderMissingFields_DefaultsToRgbAndOpaqueAlpha()
    {
        const string json = """
            {"version":1,"presets":[{"name":"Older","stops":[
              {"position":0,"color":"#FF000080"},{"position":1,"color":"#0000FF"}
            ]}]}
            """;
        var old = Assert.Single(GradientPresets.Parse(json));
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
        Assert.Equal(GradientColorSpace.Rgb, Assert.Single(GradientPresets.Parse(GradientPresets.Serialize([old]))).ColorSpace);
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
    public void Parse_DoesNotTreatMalformedCatalogAsEmpty(string json)
        => Assert.Throws<InvalidDataException>(() => GradientPresets.Parse(json));

    [Theory]
    [InlineData("\"alphaStops\":[{\"position\":0,\"alpha\":-1},{\"position\":1,\"alpha\":255}]")]
    [InlineData("\"alphaStops\":[{\"position\":0,\"alpha\":256},{\"position\":1,\"alpha\":255}]")]
    [InlineData("\"alphaStops\":[]")]
    [InlineData("\"interpolation\":\"Future\"")]
    [InlineData("\"colorSpace\":\"999\"")]
    [InlineData("\"angle\":360")]
    [InlineData("\"cycles\":0")]
    [InlineData("\"stops\":[{\"position\":-0.01,\"color\":\"#FFFFFF\"},{\"position\":1,\"color\":\"#000000\"}]")]
    public void Parse_RejectsInvalidValues(string field)
    {
        var json = "{\"version\":1,\"presets\":[{\"name\":\"Broken\"," +
            (field.StartsWith("\"stops\"", StringComparison.Ordinal) ? "" :
                "\"stops\":[{\"position\":0,\"color\":\"#FFFFFF\"},{\"position\":1,\"color\":\"#000000\"}],") +
            field + "}]}";
        Assert.Throws<InvalidDataException>(() => GradientPresets.Parse(json));
    }

    [Fact]
    public void MissingFile_ProvidesEditableBuiltIns_ExplicitEmptyCatalogDoesNotResurrectThem()
    {
        string folder = Path.Combine(Path.GetTempPath(), "gpixel-gradient-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, GradientPresets.FileName);
        try
        {
            var first = GradientPresets.Load(path);
            var second = GradientPresets.Load(path);
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
            first[0] = first[0] with { Name = "Custom Rainbow", Favorite = true };
            GradientPresets.Save(path, first);
            var customized = GradientPresets.Load(path);
            Assert.True(Assert.Single(customized, p => p.Name == "Custom Rainbow").Favorite);
            Assert.DoesNotContain(customized, p => p.Name == "Rainbow");
            GradientPresets.Save(path, [.. customized.Where(p => p.Name != "Ocean")]);
            Assert.DoesNotContain(GradientPresets.Load(path), p => p.Name == "Ocean");
            GradientPresets.Save(path, []);
            Assert.Empty(GradientPresets.Load(path));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void FailedSave_LeavesStoredCatalogIntact()
    {
        string folder = Path.Combine(Path.GetTempPath(), "gpixel-gradient-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, GradientPresets.FileName);
        try
        {
            GradientPresets.Save(path, [Example()]);
            string before = File.ReadAllText(path);
            Assert.Throws<InvalidDataException>(() => GradientPresets.Save(path,
                [Example() with { Cycles = 17 }]));
            Assert.Equal(before, File.ReadAllText(path));
            Assert.Equal("Evening", Assert.Single(GradientPresets.Load(path)).Name);
            File.WriteAllText(path, "{corrupt");
            Assert.Throws<InvalidDataException>(() => GradientPresets.Load(path));
            Assert.Equal("{corrupt", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>1판 카탈로그는 2판에서 더한 기본 그라데이션을 한 번 받고, 저장 뒤 지운 것은 되살아나지 않는다.</summary>
    [Fact]
    public void VersionOneCatalog_GainsNewBuiltInsOnce()
    {
        string folder = Path.Combine(Path.GetTempPath(), "gpixel-gradient-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, GradientPresets.FileName);
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, """
                {"version":1,"presets":[{"name":"Mine","stops":[{"position":0,"color":"#FF0000"},{"position":1,"color":"#0000FF"}]},
                {"name":"fire","stops":[{"position":0,"color":"#000000"},{"position":1,"color":"#FFFFFF"}]}]}
                """);
            var upgraded = GradientPresets.Load(path);
            Assert.Equal(["Mine", "fire", "Forest", "Grayscale"], upgraded.Select(p => p.Name).ToArray());
            Assert.Equal(Color32.Black, upgraded[1].Stops[0].Color);
            GradientPresets.Save(path, [.. upgraded.Where(p => p.Name != "Forest")]);
            Assert.DoesNotContain(GradientPresets.Load(path), p => p.Name == "Forest");
            Assert.Equal(GradientPresets.BuiltIns().Select(p => p.Name), GradientPresets.Load(Path.Combine(folder, "missing.json")).Select(p => p.Name));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>기본값 되돌리기: 고친 기본 그라데이션은 처음 값(즐겨찾기는 유지), 지운 것은 다시 들어오고 직접 만든 것은 남는다.</summary>
    [Fact]
    public void RestoreBuiltIns_ResetsEditedAndReaddsDeletedKeepingCustom()
    {
        var list = GradientPresets.BuiltIns().Where(p => p.Name != "Ocean").ToList();
        int sunset = list.FindIndex(p => p.Name == "Sunset");
        list[sunset] = list[sunset] with { Name = "sunset", Stops = [new(0, Color32.Black), new(1, Color32.Black)], Favorite = true };
        list.Add(Example("Mine"));
        GradientPresets.RestoreBuiltIns(list);
        var original = GradientPresets.BuiltIns();
        Assert.Equal(original.Single(p => p.Name == "Sunset").Stops, list[sunset].Stops);
        Assert.True(list[sunset].Favorite);
        Assert.Contains(list, p => p.Name == "Ocean");
        Assert.Contains(list, p => p.Name == "Mine");
        Assert.Equal(original.Count + 1, list.Count);
    }
}
