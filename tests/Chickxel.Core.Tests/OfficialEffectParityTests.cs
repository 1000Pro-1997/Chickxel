using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>공식 스크립트 효과(<see cref="OfficialEffects"/>)가 옮긴 기본 효과와 효과 파이프라인에서 바이트까지 같은지(<see cref="EffectParity"/>, 빠른 경로·Jint).</summary>
public class OfficialEffectParityTests
{
    [Fact]
    public void Official_effects_are_listed_and_parse_with_translations()
    {
        Assert.Equal(["coloroverlay", "fade", "shine"], OfficialEffects.Ids.Where(id => id is "coloroverlay" or "fade" or "shine"));
        foreach (var id in OfficialEffects.Ids)
        {
            var program = EffectParity.Official(id);
            if (id is "coloroverlay" or "fade" or "shine") Assert.True(program.ExecutionPath.Compiled, $"{id}: {program.ExecutionPath.Reason}");
            Assert.True(program.Translations.ContainsKey("ko") && program.Translations.ContainsKey("en"), id);
            foreach (var setting in program.Settings)
                Assert.True(program.Translations["en"].Labels.ContainsKey(setting.Key), $"{id}.{setting.Key}");
        }
        Assert.Throws<KeyNotFoundException>(() => OfficialEffects.Source("nope"));
    }

    [Fact]
    public void Official_names_match_the_built_in_effect_names()
    {
        var ko = System.Globalization.CultureInfo.GetCultureInfo("ko");
        var en = System.Globalization.CultureInfo.GetCultureInfo("en");
        foreach (var (id, effect) in new (string, LayerEffect)[] { ("fade", new FadeEffect()), ("coloroverlay", new ColorOverlayEffect()), ("shine", new ShineEffect()) })
        {
            var program = EffectParity.Official(id);
            Assert.Equal(Localization.DomainStrings.ResourceManager.GetString(DisplayKey(effect), ko), program.NameFor(ko));
            Assert.Equal(Localization.DomainStrings.ResourceManager.GetString(DisplayKey(effect), en), program.NameFor(en));
        }

        static string DisplayKey(LayerEffect e) => e switch
        {
            FadeEffect => "Effect_Fade",
            ColorOverlayEffect => "Effect_ColorOverlay",
            _ => "Effect_LightSweep",
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fade_port_matches(bool fadeOut)
        => EffectParity.AssertParity(new FadeEffect { FadeOut = fadeOut }, EffectParity.Official("fade"));

    [Theory]
    [InlineData(100, EffectBlend.Normal, 0xFFFFFFFFu)]
    [InlineData(60, EffectBlend.Normal, 0x80FF4020u)]
    [InlineData(45, EffectBlend.Multiply, 0xFF30A0F0u)]
    [InlineData(80, EffectBlend.Screen, 0xC8205080u)]
    [InlineData(0, EffectBlend.Normal, 0xFF000000u)]
    public void Color_overlay_port_matches(int amount, EffectBlend blend, uint argb)
        => EffectParity.AssertParity(new ColorOverlayEffect { Amount = amount, Blend = blend, Color = new Color32(argb) }, EffectParity.Official("coloroverlay"));

    [Theory]
    [InlineData(EffectBlend.Normal)]
    [InlineData(EffectBlend.Multiply)]
    [InlineData(EffectBlend.Screen)]
    public void Color_overlay_port_matches_with_gradients(EffectBlend blend)
        => EffectParity.AssertParity(new ColorOverlayEffect { Amount = 85, Blend = blend }, EffectParity.Official("coloroverlay"), gradientSlot: nameof(ColorOverlayEffect.Color));

    [Theory]
    [InlineData(45, 3, 80, 0, 0xFFFFFFFFu)]
    [InlineData(200, 7, 100, 2, 0x40FFE060u)]
    [InlineData(90, 1, 35, 1, 0xFF80C0FFu)]
    [InlineData(0, 32, 0, 0, 0xFFFFFFFFu)]
    public void Shine_port_matches(int angle, int width, int strength, int softness, uint argb)
        => EffectParity.AssertParity(new ShineEffect { Angle = angle, Width = width, Strength = strength, Softness = softness, Color = new Color32(argb) },
            EffectParity.Official("shine"));

    [Fact]
    public void Shine_port_matches_with_gradients()
        => EffectParity.AssertParity(new ShineEffect { Angle = 120, Width = 5, Softness = 1, Strength = 90 }, EffectParity.Official("shine"),
            gradientSlot: nameof(ShineEffect.Color));
}
