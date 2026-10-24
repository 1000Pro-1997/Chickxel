using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class EffectPresetTests
{
    [Fact]
    public void CaptureSerializeParseInstantiate_PreservesStackAndResetsFrames()
    {
        var preset = EffectPreset.Capture("그라데이션 외곽선",
        [
            new OutlineEffect { Color = new Color32(10, 20, 30), Thickness = 2, Frames = new FrameRange(1, 3) },
            new ColorOverlayEffect { Color = new Color32(40, 50, 60), ClipToPrevious = true, Amount = 75, Frames = new FrameRange(2, 4) },
        ]);

        Assert.All(preset.Effects, effect => Assert.Null(effect.Frames));
        var parsed = Assert.Single(EffectPresets.Parse(EffectPresets.Serialize([preset])));
        Assert.Equal(preset.Name, parsed.Name);
        Assert.Equal(preset.Effects, parsed.Effects);

        var range = new FrameRange(4, 7);
        var instance = parsed.Instantiate(range);
        Assert.All(instance, effect => Assert.Equal(range, effect.Frames));
        Assert.False(instance[0].ClipToPrevious);
        Assert.True(instance[1].ClipToPrevious);
    }

    [Fact]
    public void Parse_SkipsUnknownEffectsAndDuplicateNameUsesLastPreset()
    {
        const string json = """
            {
              "version": 1,
              "presets": [
                { "name": "효과", "effects": [ { "type": "outline", "thickness": 1 } ] },
                { "name": "효과", "effects": [ { "type": "future" }, { "type": "shadow", "x": 3 } ] }
              ]
            }
            """;

        var preset = Assert.Single(EffectPresets.Parse(json));
        var shadow = Assert.IsType<ShadowEffect>(Assert.Single(preset.Effects));
        Assert.Equal(3, shadow.OffsetX);
    }

    [Fact]
    public void OutlineHideOriginal_RoundTrips_WrittenOnlyWhenOn_OldJsonLoadsOff()
    {
        var on = EffectPreset.Capture("선만", [new OutlineEffect { Color = new Color32(10, 20, 30), Inside = true, HideOriginal = true }]);
        var json = EffectPresets.Serialize([on]);
        Assert.Contains("\"hideOriginal\": true", json);
        Assert.Equal(on.Effects, Assert.Single(EffectPresets.Parse(json)).Effects);

        var off = EffectPresets.Serialize([EffectPreset.Capture("기본", [new OutlineEffect()])]);
        Assert.DoesNotContain("hideOriginal", off);

        const string old = """
            { "version": 1, "presets": [ { "name": "예전", "effects": [ { "type": "outline", "thickness": 2, "inside": false } ] } ] }
            """;
        var outline = Assert.IsType<OutlineEffect>(Assert.Single(Assert.Single(EffectPresets.Parse(old)).Effects));
        Assert.False(outline.HideOriginal);
        Assert.Equal(2, outline.Thickness);
    }
}
