using System.Text.Json.Nodes;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class EffectSettingsTests
{
    private static IEnumerable<LayerEffect> AllDefaults()
        => typeof(LayerEffect).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(LayerEffect)))
            .Select(t => (LayerEffect)Activator.CreateInstance(t)!);

    [Fact]
    public void EveryEffectType_RoundTripsThroughTypeIdDefaultsAndSettings()
    {
        foreach (var effect in AllDefaults())
        {
            var id = EffectSettings.TypeId(effect);
            Assert.IsType(effect.GetType(), EffectSettings.Create(id));
            var settings = EffectSettings.ToJson(effect);
            foreach (var reserved in EffectSettings.ReservedKeys) Assert.False(settings.ContainsKey(reserved), $"{id}.{reserved}");
            var keys = EffectSettings.Describe(effect).Select(s => s.Key).ToHashSet();
            Assert.Contains(EffectSettings.GradientsKey, keys);
            Assert.All(settings.Select(entry => entry.Key), key => Assert.Contains(key, keys));
            // 기본값을 그대로 덮으면 같은 효과
            var (merged, clamped) = EffectSettings.Merge(effect, settings);
            Assert.Equal(effect, merged);
            Assert.Empty(clamped);
        }
    }

    [Fact]
    public void Create_RejectsUnknownAndLegacyTypeIds()
    {
        Assert.Null(EffectSettings.Create("nope"));
        Assert.Null(EffectSettings.Create("gradient")); // 예전 파일에서만 읽힘(색 덮기로 바뀜)
        Assert.IsType<HitFlashEffect>(EffectSettings.Create(" HitFlash "));
    }

    [Fact]
    public void Merge_ChangesOnlyGivenKeys_AndKeepsRangeEnabledAndClip()
    {
        var start = new HitFlashEffect { Frames = new FrameRange(2, 3), Enabled = false, ClipToPrevious = true };
        var (merged, clamped) = EffectSettings.Merge(start, new JsonObject { ["color"] = "#FFFFFF", ["count"] = 2, ["repeat_count"] = 3 });
        var hit = Assert.IsType<HitFlashEffect>(merged);
        Assert.Equal(new Color32(255, 255, 255), hit.Color);
        Assert.Equal(2, hit.Count);
        Assert.Equal(3, hit.RepeatCount);
        Assert.Equal(new HitFlashEffect().Strength, hit.Strength);
        Assert.Equal(new FrameRange(2, 3), hit.Frames);
        Assert.False(hit.Enabled);
        Assert.True(hit.ClipToPrevious);
        Assert.Empty(clamped);
    }

    [Fact]
    public void Merge_ReportsClampedNumbers_AndDescribeKnowsTheRange()
    {
        var (merged, clamped) = EffectSettings.Merge(new OutlineEffect(), new JsonObject { ["thickness"] = 99 });
        Assert.Equal(OutlineEffect.MaxThickness, ((OutlineEffect)merged).Thickness);
        Assert.Equal([$"thickness = {OutlineEffect.MaxThickness}"], clamped);
        var thickness = EffectSettings.Describe(new OutlineEffect()).Single(s => s.Key == "thickness");
        Assert.Equal(EffectSettingKind.Number, thickness.Kind);
        Assert.Equal((1, OutlineEffect.MaxThickness), (thickness.Min, thickness.Max));
    }

    [Fact]
    public void Describe_FindsSettingsOmittedAtTheirDefault_AndChoiceNames()
    {
        var dither = EffectSettings.Describe(new ShadowEffect()).Single(s => s.Key == "dither");
        Assert.Equal(EffectSettingKind.Choice, dither.Kind);
        Assert.Equal("None", dither.Default!.GetValue<string>());
        Assert.Equal(Enum.GetNames<ShadowDither>(), dither.Choices);

        var (merged, _) = EffectSettings.Merge(new ShadowEffect(), new JsonObject { ["Dither"] = "half50" });
        Assert.Equal(ShadowDither.Half50, ((ShadowEffect)merged).Dither);
        var color = EffectSettings.Describe(new ShadowEffect()).Single(s => s.Key == "color");
        Assert.Equal(EffectSettingKind.Color, color.Kind);
    }

    [Fact]
    public void Describe_OutlineShowsShadeModeAndAmount_AndMergeSetsThem()
    {
        var info = EffectSettings.Describe(new OutlineEffect());
        var mode = info.Single(s => s.Key == "mode");
        Assert.Equal(EffectSettingKind.Choice, mode.Kind);
        Assert.Equal("Fixed", mode.Default!.GetValue<string>());
        Assert.Equal(Enum.GetNames<OutlineColorMode>(), mode.Choices);
        var amount = info.Single(s => s.Key == "amount");
        Assert.Equal(EffectSettingKind.Number, amount.Kind);
        Assert.Equal(OutlineEffect.DefaultShadeAmount, amount.Default!.GetValue<int>());
        Assert.Equal((-100, 100), (amount.Min, amount.Max));

        var (merged, clamped) = EffectSettings.Merge(new OutlineEffect(), new JsonObject { ["mode"] = "shade", ["amount"] = 150 });
        var outline = Assert.IsType<OutlineEffect>(merged);
        Assert.Equal((OutlineColorMode.Shade, 100), (outline.ColorMode, outline.ShadeAmount));
        Assert.Equal(["amount = 100"], clamped);
    }

    [Fact]
    public void Merge_NullResetsAKeyToTheTypeDefault()
    {
        var (moved, _) = EffectSettings.Merge(new ShadowEffect(), new JsonObject { ["x"] = 5 });
        Assert.Equal(5, ((ShadowEffect)moved).OffsetX);
        var (reset, _) = EffectSettings.Merge(moved, new JsonObject { ["x"] = null });
        Assert.Equal(new ShadowEffect().OffsetX, ((ShadowEffect)reset).OffsetX);
    }

    [Theory]
    [InlineData("""{"nope": 1}""", EffectSettingError.UnknownKey)]
    [InlineData("""{"frames": [0, 1]}""", EffectSettingError.ReservedKey)]
    [InlineData("""{"clip_to_above": true}""", EffectSettingError.ReservedKey)]
    [InlineData("""{"count": "many"}""", EffectSettingError.WrongType)]
    [InlineData("""{"count": 1.5}""", EffectSettingError.WrongType)]
    [InlineData("""{"mode": "sparkle"}""", EffectSettingError.NotAChoice)]
    [InlineData("""{"color": "red"}""", EffectSettingError.InvalidColor)]
    [InlineData("""{"colorGradients": 3}""", EffectSettingError.WrongType)]
    public void Merge_RejectsBadKeysAndValues(string json, EffectSettingError error)
    {
        var e = Assert.Throws<EffectSettingException>(() => EffectSettings.Merge(new HitFlashEffect(), (JsonObject)JsonNode.Parse(json)!));
        Assert.Equal(error, e.Error);
    }

    [Fact]
    public void Merge_RejectsListsWithDroppedEntries()
    {
        var e = Assert.Throws<EffectSettingException>(() =>
            EffectSettings.Merge(new ColorCycleEffect(), new JsonObject { ["colors"] = new JsonArray("#FF0000", "not a color", "#0000FF") }));
        Assert.Equal(EffectSettingError.InvalidEntries, e.Error);
        Assert.Equal("colors", e.Key);
        var (swap, _) = EffectSettings.Merge(new PaletteSwapEffect(),
            (JsonObject)JsonNode.Parse("""{"pairs": [["#FF0000", "#00FF00"]]}""")!);
        Assert.Equal(new ColorSwap(new Color32(255, 0, 0), new Color32(0, 255, 0)), Assert.Single(((PaletteSwapEffect)swap).Pairs));
    }

    [Fact]
    public void EffectStack_RemoveUnclipsOrphans_MoveReanchors()
    {
        LayerEffect a = new OutlineEffect(), b = new ColorOverlayEffect { ClipToPrevious = true }, c = new ShadowEffect(), d = new InnerGlowEffect { ClipToPrevious = true };
        // a 지우기: a에 붙은 b는 클리핑이 풀림
        var removed = EffectStack.Remove([a, b, c, d], new HashSet<int> { 0 });
        Assert.Equal([b with { ClipToPrevious = false }, c, d], removed);
        // c를 맨 앞으로: d의 기준이 사라져 클리핑이 풀리고, 자리에 있던 a는 그대로
        var moved = EffectStack.Move([a, b, c, d], 2, 0);
        Assert.Equal([c, a, b, d with { ClipToPrevious = false }], moved);
        // 클리핑 효과 b를 c 뒤로: b는 새 기준 효과가 되고 그 자리의 d는 풀림
        var block = EffectStack.Move([a, b, c, d], 1, 2);
        Assert.Equal([a, c, b with { ClipToPrevious = false }, d with { ClipToPrevious = false }], block);
    }
}
