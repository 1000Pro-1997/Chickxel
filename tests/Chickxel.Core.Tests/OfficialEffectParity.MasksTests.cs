using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>
/// 모양(마스크)·색 목록 효과의 공식 스크립트(안쪽 빛·가장자리 빛·그늘·색 바꾸기·색 순환)가 옮긴 기본 효과와 효과 파이프라인에서 바이트까지 같은지
/// (<see cref="EffectParity"/>, 빠른 경로·Jint).
/// </summary>
public class OfficialEffectParityMasksTests
{
    /// <summary>시험 입력에 실제로 쓰인 색(입력마다 많이 쓰인 순 몇 개, 겹치지 않게) — 색 바꾸기·색 순환이 맞는 픽셀을 찾도록.</summary>
    private static readonly Color32[] Used = EffectParity.Inputs
        .SelectMany(input => PaletteRemap.UsedColors(input.Frames[0], 5))
        .DistinctBy(c => c.Argb & 0x00FFFFFFu)
        .ToArray();

    [Fact]
    public void Names_match_the_built_in_effect_names()
    {
        var ko = System.Globalization.CultureInfo.GetCultureInfo("ko");
        var en = System.Globalization.CultureInfo.GetCultureInfo("en");
        foreach (var (id, key) in new[] { ("innerglow", "Effect_InnerGlow"), ("rimlight", "Effect_EdgeLightShade"), ("paletteswap", "Effect_PaletteSwap"), ("colorcycle", "Effect_ColorCycle") })
        {
            var program = EffectParity.Official(id);
            Assert.True(program.ExecutionPath.Compiled, $"{id}: {program.ExecutionPath.Reason}");
            Assert.Equal(Localization.DomainStrings.ResourceManager.GetString(key, ko), program.NameFor(ko));
            Assert.Equal(Localization.DomainStrings.ResourceManager.GetString(key, en), program.NameFor(en));
            foreach (var setting in program.Settings)
                Assert.True(program.Translations["en"].Labels.ContainsKey(setting.Key), $"{id}.{setting.Key}");
        }
        Assert.True(Used.Length >= 8, $"only {Used.Length} used colors");
    }

    // ───────────── 안쪽 빛 ─────────────

    [Theory]
    [InlineData(2, 70, true, EffectBlend.Screen, 0xFFFFF0A0u)]
    [InlineData(1, 100, false, EffectBlend.Normal, 0xFFFF2000u)]
    [InlineData(8, 100, true, EffectBlend.Multiply, 0x8040A0FFu)]
    [InlineData(5, 35, true, EffectBlend.Normal, 0x00FFFFFFu)]
    [InlineData(3, 0, true, EffectBlend.Screen, 0xFFFFFFFFu)]
    [InlineData(8, 1, false, EffectBlend.Screen, 0xFF00FF00u)]
    [InlineData(4, 60, false, EffectBlend.Multiply, 0xFF102030u)]
    public void Inner_glow_port_matches(int size, int strength, bool fade, EffectBlend blend, uint argb)
        => EffectParity.AssertParity(new InnerGlowEffect { Size = size, Strength = strength, Fade = fade, Blend = blend, Color = new Color32(argb) },
            EffectParity.Official("innerglow"));

    [Theory]
    [InlineData(3, true, EffectBlend.Normal)]
    [InlineData(2, false, EffectBlend.Multiply)]
    [InlineData(6, true, EffectBlend.Screen)]
    public void Inner_glow_port_matches_with_gradients(int size, bool fade, EffectBlend blend)
        => EffectParity.AssertParity(new InnerGlowEffect { Size = size, Strength = 90, Fade = fade, Blend = blend }, EffectParity.Official("innerglow"),
            gradientSlot: nameof(InnerGlowEffect.Color));

    // ───────────── 가장자리 빛·그늘 ─────────────

    [Theory]
    [InlineData(LightDirection.TopLeft, 1, 0xA0FFFFFFu, 0x60000000u)]
    [InlineData(LightDirection.Top, 2, 0xFFFFE080u, 0xFF201040u)]
    [InlineData(LightDirection.TopRight, 16, 0x80FFFFFFu, 0x80000000u)]
    [InlineData(LightDirection.Left, 3, 0x00FFFFFFu, 0xC0102030u)]
    [InlineData(LightDirection.Right, 1, 0xFF80FF80u, 0x00000000u)]
    [InlineData(LightDirection.BottomLeft, 4, 0x01FFFFFFu, 0xFE000000u)]
    [InlineData(LightDirection.Bottom, 8, 0x00FFFFFFu, 0x00000000u)]
    [InlineData(LightDirection.BottomRight, 5, 0xA0FFFFFFu, 0x60000000u)]
    public void Rim_light_port_matches(LightDirection direction, int size, uint light, uint shade)
        => EffectParity.AssertParity(new RimLightEffect { Direction = direction, Size = size, Light = new Color32(light), Shade = new Color32(shade) },
            EffectParity.Official("rimlight"));

    [Theory]
    [InlineData(nameof(RimLightEffect.Light), LightDirection.TopLeft, 0xA0FFFFFFu, 0x60000000u)]
    [InlineData(nameof(RimLightEffect.Shade), LightDirection.Bottom, 0xA0FFFFFFu, 0x60000000u)]
    [InlineData(nameof(RimLightEffect.Light), LightDirection.Right, 0x00FFFFFFu, 0xFF000000u)]
    [InlineData(nameof(RimLightEffect.Shade), LightDirection.TopRight, 0xFFFFFFFFu, 0x00000000u)]
    public void Rim_light_port_matches_with_gradients(string slot, LightDirection direction, uint light, uint shade)
        => EffectParity.AssertParity(new RimLightEffect { Direction = direction, Size = 3, Light = new Color32(light), Shade = new Color32(shade) },
            EffectParity.Official("rimlight"), gradientSlot: slot);

    // ───────────── 색 바꾸기 ─────────────

    [Fact]
    public void Palette_swap_port_matches_without_pairs()
        => EffectParity.AssertParity(new PaletteSwapEffect { Tolerance = 30 }, EffectParity.Official("paletteswap"));

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(60)]
    [InlineData(255)]
    public void Palette_swap_port_matches(int tolerance)
        => EffectParity.AssertParity(new PaletteSwapEffect { Pairs = SwapPairs(), Tolerance = tolerance }, EffectParity.Official("paletteswap"));

    [Theory]
    [InlineData("Pairs[0].From", 40)]
    [InlineData("Pairs[0].To", 0)]
    [InlineData("Pairs[1].To", 25)]
    [InlineData("Pairs[2].From", 90)]
    [InlineData("Pairs[5].To", 0)]
    public void Palette_swap_port_matches_with_gradients(string slot, int tolerance)
        => EffectParity.AssertParity(new PaletteSwapEffect { Pairs = SwapPairs(), Tolerance = tolerance }, EffectParity.Official("paletteswap"), gradientSlot: slot);

    /// <summary>
    /// 쌍 묶음: 맞바꾸기(A↔B), 반투명·투명(지우기) 대상, 알파 0인 단색 원본(그래도 RGB로 맞음), 같은 원본 두 번(앞의 것), 외곽선 색, 그림에 없는 색.
    /// </summary>
    private static ColorSwap[] SwapPairs() =>
    [
        new(Used[0], Used[1]),
        new(Used[1], Used[0]),
        new(Used[2], new Color32(255, 0, 128, 140)),
        new(Used[3], Used[3].WithAlpha(0)),
        new(Used[4].WithAlpha(0), new Color32(20, 200, 60)),
        new(Used[5], new Color32(0, 0, 255, 200)),
        new(Used[5], new Color32(255, 255, 0)),
        new(new Color32(250, 250, 250), new Color32(255, 120, 0)),
        new(Used[6], new Color32(9, 9, 9, 1)),
        new(new Color32(1, 2, 3), new Color32(200, 100, 50)),
        new(Used[7], new Color32(90, 30, 160, 255)),
    ];

    // ───────────── 색 순환 ─────────────

    [Fact]
    public void Color_cycle_port_matches_defaults()
        => EffectParity.AssertParity(new ColorCycleEffect(), EffectParity.Official("colorcycle"));

    [Theory]
    [InlineData(2, 1, false)]
    [InlineData(3, 3, false)]
    [InlineData(3, 7, true)]
    [InlineData(4, 64, false)]
    [InlineData(8, 5, true)]
    [InlineData(8, 16, false)]
    public void Color_cycle_port_matches(int count, int steps, bool reverse)
        => EffectParity.AssertParity(new ColorCycleEffect { Colors = CycleColors(count), Steps = steps, Reverse = reverse }, EffectParity.Official("colorcycle"),
            frameCount: 8);

    [Fact]
    public void Color_cycle_port_matches_with_repeated_colors()
        => EffectParity.AssertParity(new ColorCycleEffect { Colors = [Used[0], Used[1], Used[0], Used[2]], Steps = 5 }, EffectParity.Official("colorcycle"));

    [Theory]
    [InlineData("Colors[0]", 3, 4)]
    [InlineData("Colors[1]", 3, 2)]
    [InlineData("Colors[2]", 3, 5)]
    [InlineData("Colors[7]", 8, 9)]
    public void Color_cycle_port_matches_with_gradients(string slot, int count, int steps)
        => EffectParity.AssertParity(new ColorCycleEffect { Colors = CycleColors(count), Steps = steps }, EffectParity.Official("colorcycle"), gradientSlot: slot);

    /// <summary>순환할 색: 그림에 쓰인 색 앞에서부터(8개면 외곽선 색 하나 포함).</summary>
    private static Color32[] CycleColors(int count)
        => count < 8 ? Used[..count] : [.. Used[..7], new Color32(250, 250, 250)];
}
