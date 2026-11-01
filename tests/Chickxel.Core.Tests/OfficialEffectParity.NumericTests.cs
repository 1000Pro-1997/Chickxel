using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 숫자·움직임 묶음(색조 보정, 피격 번쩍임, 깜빡이기, 둥실둥실, 흔들기)의 공식 스크립트 효과가 옮긴 기본 효과와 효과 파이프라인에서 바이트까지 같은지
/// (<see cref="EffectParity"/>, 빠른 경로·Jint).
/// </summary>
public class OfficialEffectParityNumericTests
{
    [Fact]
    public void Numeric_ports_are_listed_on_the_fast_path_with_built_in_names()
    {
        var ko = System.Globalization.CultureInfo.GetCultureInfo("ko");
        var en = System.Globalization.CultureInfo.GetCultureInfo("en");
        foreach (var (id, key) in new[]
                 {
                     ("coloradjust", "Effect_ColorAdjust"), ("hitflash", "Effect_HitFlash"), ("blink", "Effect_Blink"), ("bob", "Effect_Bob"), ("shake", "Effect_Shake"),
                 })
        {
            Assert.Contains(id, OfficialEffects.Ids);
            var program = EffectParity.Official(id);
            Assert.True(program.ExecutionPath.Compiled, $"{id}: {program.ExecutionPath.Reason}");
            // 깜빡이기만 빠르기 곡선·반복을 쓰지 않는다(기본 효과 패널의 AddMotionEditors(timing: false)와 같음)
            Assert.Equal(id != "blink", program.Motion);
            Assert.Equal(Localization.DomainStrings.ResourceManager.GetString(key, ko), program.NameFor(ko));
            Assert.Equal(Localization.DomainStrings.ResourceManager.GetString(key, en), program.NameFor(en));
        }
    }

    [Theory]
    [InlineData(ColorAdjustMode.Grayscale, 100, 30, 0, 0, 50)]
    [InlineData(ColorAdjustMode.Grayscale, 37, 30, 0, 0, 50)]
    [InlineData(ColorAdjustMode.Grayscale, 1, 30, 0, 0, 50)]
    [InlineData(ColorAdjustMode.Grayscale, 0, 30, 0, 0, 50)]
    [InlineData(ColorAdjustMode.Sepia, 100, 30, 0, 0, 50)]
    [InlineData(ColorAdjustMode.Sepia, 64, -90, 40, 40, -40)]
    [InlineData(ColorAdjustMode.Sepia, 0, 30, 0, 0, 50)]
    [InlineData(ColorAdjustMode.HueShift, 100, 30, 0, 0, 50)]
    [InlineData(ColorAdjustMode.HueShift, 0, -180, 0, 0, 50)]
    [InlineData(ColorAdjustMode.HueShift, 0, 180, 0, 0, 50)]
    [InlineData(ColorAdjustMode.HueShift, 0, 1, 0, 0, 50)]
    [InlineData(ColorAdjustMode.HueShift, 0, -97, 0, 0, 50)]
    [InlineData(ColorAdjustMode.HueShift, 0, 0, 0, 0, 50)]
    [InlineData(ColorAdjustMode.BrightnessContrast, 100, 30, 0, 0, 50)]
    [InlineData(ColorAdjustMode.BrightnessContrast, 100, 30, 100, 0, 50)]
    [InlineData(ColorAdjustMode.BrightnessContrast, 100, 30, -100, 100, 50)]
    [InlineData(ColorAdjustMode.BrightnessContrast, 100, 30, 35, -60, 50)]
    [InlineData(ColorAdjustMode.BrightnessContrast, 100, 30, -20, 45, 50)]
    [InlineData(ColorAdjustMode.BrightnessContrast, 100, 30, 0, -100, 50)]
    [InlineData(ColorAdjustMode.BrightnessContrast, 100, 30, 1, 1, 50)]
    [InlineData(ColorAdjustMode.Saturation, 100, 30, 0, 0, 50)]
    [InlineData(ColorAdjustMode.Saturation, 100, 30, 0, 0, -100)]
    [InlineData(ColorAdjustMode.Saturation, 100, 30, 0, 0, 100)]
    [InlineData(ColorAdjustMode.Saturation, 100, 30, 0, 0, -33)]
    [InlineData(ColorAdjustMode.Saturation, 100, 30, 0, 0, 0)]
    public void Color_adjust_port_matches(ColorAdjustMode mode, int amount, int hue, int brightness, int contrast, int saturation)
        => EffectParity.AssertParity(new ColorAdjustEffect
        {
            Mode = mode, Amount = amount, Hue = hue, Brightness = brightness, Contrast = contrast, Saturation = saturation,
        }, EffectParity.Official("coloradjust"));

    [Theory]
    [InlineData(100, 2, HitFlashShape.Solid, 0xFFFFFFFFu)]
    [InlineData(100, 2, HitFlashShape.Fade, 0xFFFFFFFFu)]
    [InlineData(63, 1, HitFlashShape.Solid, 0x40E02030u)]
    [InlineData(63, 3, HitFlashShape.Fade, 0xFFE02030u)]
    [InlineData(1, 8, HitFlashShape.Fade, 0xFF102040u)]
    [InlineData(100, 8, HitFlashShape.Solid, 0x00000000u)]
    [InlineData(0, 2, HitFlashShape.Solid, 0xFFFFFFFFu)]
    [InlineData(0, 5, HitFlashShape.Fade, 0xFF80FF00u)]
    public void Hit_flash_port_matches(int strength, int count, HitFlashShape shape, uint argb)
        => EffectParity.AssertParity(new HitFlashEffect { Strength = strength, Count = count, Shape = shape, Color = new Color32(argb) },
            EffectParity.Official("hitflash"), frameCount: 9);

    [Theory]
    [InlineData(HitFlashShape.Solid)]
    [InlineData(HitFlashShape.Fade)]
    public void Hit_flash_port_matches_with_gradients(HitFlashShape shape)
        => EffectParity.AssertParity(new HitFlashEffect { Strength = 85, Count = 2, Shape = shape }, EffectParity.Official("hitflash"),
            gradientSlot: nameof(HitFlashEffect.Color));

    [Theory]
    [InlineData(BlinkTiming.Frames, 1, 1, 4, BlinkHide.Transparent, 40)]
    [InlineData(BlinkTiming.Frames, 2, 3, 4, BlinkHide.Translucent, 40)]
    [InlineData(BlinkTiming.Frames, 3, 1, 4, BlinkHide.Dither, 40)]
    [InlineData(BlinkTiming.Frames, 1, 16, 4, BlinkHide.Translucent, 0)]
    [InlineData(BlinkTiming.Frames, 16, 16, 4, BlinkHide.Transparent, 40)]
    [InlineData(BlinkTiming.Frames, 1, 2, 4, BlinkHide.Translucent, 100)]
    [InlineData(BlinkTiming.Frames, 1, 1, 4, BlinkHide.Translucent, 73)]
    [InlineData(BlinkTiming.Count, 1, 1, 1, BlinkHide.Transparent, 40)]
    [InlineData(BlinkTiming.Count, 1, 1, 4, BlinkHide.Dither, 40)]
    [InlineData(BlinkTiming.Count, 1, 1, 16, BlinkHide.Translucent, 13)]
    [InlineData(BlinkTiming.Count, 1, 1, 3, BlinkHide.Translucent, 40)]
    public void Blink_port_matches(BlinkTiming timing, int on, int off, int count, BlinkHide hide, int opacity)
        => EffectParity.AssertParity(new BlinkEffect { Timing = timing, OnFrames = on, OffFrames = off, Count = count, Hide = hide, Opacity = opacity },
            EffectParity.Official("blink"), frameCount: 10);

    [Theory]
    [InlineData(2, 1, EffectAxis.Vertical)]
    [InlineData(2, 1, EffectAxis.Horizontal)]
    [InlineData(1, 3, EffectAxis.Vertical)]
    [InlineData(5, 2, EffectAxis.Horizontal)]
    [InlineData(32, 8, EffectAxis.Vertical)]
    [InlineData(32, 1, EffectAxis.Horizontal)]
    public void Bob_port_matches(int height, int cycles, EffectAxis axis)
        => EffectParity.AssertParity(new BobEffect { Height = height, Cycles = cycles, Axis = axis }, EffectParity.Official("bob"), frameCount: 9);

    [Theory]
    [InlineData(EffectAxis.Horizontal, 2, 4, true, false, 1)]
    [InlineData(EffectAxis.Vertical, 2, 4, true, false, 1)]
    [InlineData(EffectAxis.Both, 2, 4, true, false, 1)]
    [InlineData(EffectAxis.Horizontal, 5, 3, false, true, 1)]
    [InlineData(EffectAxis.Vertical, 7, 2, true, true, 0)]
    [InlineData(EffectAxis.Both, 4, 2, false, true, 1)]
    [InlineData(EffectAxis.Both, 32, 16, false, false, 9999)]
    [InlineData(EffectAxis.Horizontal, 1, 1, true, false, 4321)]
    [InlineData(EffectAxis.Vertical, 32, 16, false, true, 1)]
    [InlineData(EffectAxis.Both, 12, 5, true, false, 77)]
    public void Shake_port_matches(EffectAxis axis, int amount, int count, bool decay, bool regular, int seed)
        => EffectParity.AssertParity(new ShakeEffect { Axis = axis, Amount = amount, Count = count, Decay = decay, Regular = regular, Seed = seed },
            EffectParity.Official("shake"), frameCount: 9);

    // 옮기는 효과가 앞 효과의 여백과 원본 위치를 이어받는지: 둥실둥실 → 흔들기 → 디더 깜빡이기(원본 기준 무늬) → 번쩍임 그라데이션(원본 기준 범위)
    [Fact]
    public void Motion_ports_chain_like_the_built_in_effects()
    {
        var gradient = EffectParity.Gradients[2];
        LayerEffect[] builtIn =
        [
            new BobEffect { Height = 3, Cycles = 2, Axis = EffectAxis.Horizontal },
            new ShakeEffect { Axis = EffectAxis.Both, Amount = 4, Count = 3, Decay = false },
            new BlinkEffect { Timing = BlinkTiming.Frames, OnFrames = 1, OffFrames = 1, Hide = BlinkHide.Dither },
            new HitFlashEffect { Count = 3, Shape = HitFlashShape.Fade, ClipToPrevious = true }
                with { ColorGradients = new Dictionary<string, EffectColorGradient> { [nameof(HitFlashEffect.Color)] = gradient } },
            new ColorAdjustEffect { Mode = ColorAdjustMode.HueShift, Hue = 120 },
        ];
        var programs = new Dictionary<Type, ScriptEffectProgram>
        {
            [typeof(BobEffect)] = EffectParity.Official("bob"),
            [typeof(ShakeEffect)] = EffectParity.Official("shake"),
            [typeof(BlinkEffect)] = EffectParity.Official("blink"),
            [typeof(HitFlashEffect)] = EffectParity.Official("hitflash"),
            [typeof(ColorAdjustEffect)] = EffectParity.Official("coloradjust"),
        };
        EffectParity.AssertSameLists(builtIn, [.. builtIn.Select(e => EffectParity.ToScript(e, programs[e.GetType()]))], frameCount: 8, label: "motion chain");
    }
}
