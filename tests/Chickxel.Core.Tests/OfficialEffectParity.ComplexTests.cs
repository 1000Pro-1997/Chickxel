using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Xunit.Sdk;

namespace Chickxel.Core.Tests;

/// <summary>
/// 공식 스크립트 효과 중 복잡한 합성·시간 효과(터지기, 충격파 고리, 홀로그램, 잔상)가 옮긴 기본 효과와 효과 파이프라인에서 바이트까지 같은지(<see cref="EffectParity"/>, 빠른 경로·Jint).
/// </summary>
public class OfficialEffectParityComplexTests
{
    [Fact]
    public void Complex_ports_use_the_built_in_names()
    {
        var ko = System.Globalization.CultureInfo.GetCultureInfo("ko");
        var en = System.Globalization.CultureInfo.GetCultureInfo("en");
        foreach (var (id, key) in new[] { ("explode", "Effect_Explode"), ("shockwave", "Effect_Shockwave"), ("hologram", "Effect_Hologram"), ("afterimage", "Effect_Afterimage") })
        {
            var program = EffectParity.Official(id);
            Assert.Equal(Localization.DomainStrings.ResourceManager.GetString(key, ko), program.NameFor(ko));
            Assert.Equal(Localization.DomainStrings.ResourceManager.GetString(key, en), program.NameFor(en));
            foreach (var setting in program.Settings)
                Assert.True(program.Translations["en"].Labels.ContainsKey(setting.Key), $"{id}.{setting.Key}");
        }
        Assert.Equal(EffectEasing.EaseOut, EffectParity.Official("explode").Easing);
        Assert.Equal(EffectEasing.EaseOut, EffectParity.Official("shockwave").Easing);
        Assert.True(EffectParity.Official("afterimage").Deferred);
        Assert.False(EffectParity.Official("afterimage").Motion);
        Assert.True(EffectParity.Official("explode").Motion && EffectParity.Official("shockwave").Motion && EffectParity.Official("hologram").Motion);
        Assert.True(EffectParity.Official("explode").ExecutionPath.Compiled);
        Assert.True(EffectParity.Official("shockwave").ExecutionPath.Compiled);
        Assert.True(EffectParity.Official("hologram").ExecutionPath.Compiled);
    }

    [Fact]
    public void Complex_ports_pad_like_the_built_in_effects()
    {
        LayerEffect[] effects =
        [
            new ExplodeEffect(), new ExplodeEffect { DistanceX = 0, DistanceY = 128, Gravity = -64 }, new ExplodeEffect { DistanceX = 7, DistanceY = 0, Gravity = 64 },
            new ShockwaveEffect(), new ShockwaveEffect { Center = ExplodeCenter.Pivot, Radius = 128 }, new ShockwaveEffect { Center = ExplodeCenter.Custom, Radius = 1 },
            new HologramEffect(), new HologramEffect { GlitchRate = 0 }, new HologramEffect { EdgeGlow = false, GlitchShift = 8 }, new HologramEffect { EdgeGlow = false, GlitchRate = 0 },
            new AfterimageEffect(),
        ];
        foreach (var e in effects)
        {
            var script = EffectParity.ToScript(e, EffectParity.Official(Id(e)));
            Assert.Equal(LayerEffects.Padding([e], 20, 10), LayerEffects.Padding([script], 20, 10));
        }
    }

    // ───────────── 터지기 ─────────────

    // 큰 여백(최대 거리·반지름)은 Jint 길이 큰 그림에서 느리므로 작은 입력만, 그라데이션 7종을 모두 도는 묶음은 대표 입력만 돌려 시험 시간을 줄인다
    private static readonly IReadOnlyList<EffectParity.Input> Tiny = EffectParity.Inputs.Where(i => i.Name is "holes and partial alpha" or "single pixel" or "empty").ToList();
    private static readonly IReadOnlyList<EffectParity.Input> Small = EffectParity.Inputs.Where(i => i.Name is "chick knight" or "holes and partial alpha" or "single pixel").ToList();

    [Theory]
    [InlineData(16, 16, ExplodeCenter.Middle, 0, 0, 50, 50, 6, 1, false, true, 40, 0, false, 1)]
    [InlineData(0, 0, ExplodeCenter.Middle, 0, 0, 50, 50, 12, 1, false, false, 40, 0, false, 7)]
    [InlineData(40, 5, ExplodeCenter.BottomCenter, 0, 0, 0, 100, -20, 1, false, true, 60, 0, false, 3)]
    [InlineData(12, 20, ExplodeCenter.Custom, -3, 20, 30, 70, 0, 3, true, true, 100, 0, false, 42)]
    [InlineData(9, 9, ExplodeCenter.Pivot, 0, 0, 100, 100, 3, 4, true, true, 0, 0, true, 9999)]
    [InlineData(0, 0, ExplodeCenter.Middle, 0, 0, 50, 50, 0, 1, false, true, 40, 3, false, 2)]
    [InlineData(10, 10, ExplodeCenter.Custom, 5, 4, 100, 100, 0, 2, false, false, 40, 2, true, 0)]
    [InlineData(128, 0, ExplodeCenter.Middle, 0, 0, 20, 80, 64, 1, false, true, 80, 8, false, 5)]
    [InlineData(3, 128, ExplodeCenter.Middle, 0, 0, 0, 0, 0, 1, false, false, 40, 0, false, 6)]
    [InlineData(0, 30, ExplodeCenter.BottomCenter, 0, 0, 0, 0, -64, 2, true, false, 0, 1, true, 11)]
    public void Explode_port_matches(int distanceX, int distanceY, ExplodeCenter center, int centerX, int centerY, int spreadX, int spreadY, int gravity,
        int pieceSize, bool shrink, bool fade, int fadeStart, int flashFrames, bool keepOriginal, int seed)
        => EffectParity.AssertParity(new ExplodeEffect
        {
            DistanceX = distanceX, DistanceY = distanceY, Center = center, CenterX = centerX, CenterY = centerY, SpreadX = spreadX, SpreadY = spreadY,
            Gravity = gravity, PieceSize = pieceSize, Shrink = shrink, Fade = fade, FadeStart = fadeStart, FlashFrames = flashFrames,
            FlashColor = new Color32(255, 60, 30, 170), KeepOriginal = keepOriginal, Seed = seed,
        }, EffectParity.Official("explode"), frameCount: distanceX + distanceY > 100 ? 4 : 6, inputs: distanceX + distanceY > 100 ? Tiny : null);

    [Fact]
    public void Explode_port_matches_with_gradients()
        => EffectParity.AssertParity(new ExplodeEffect { FlashFrames = 4, PieceSize = 2, Gravity = -4 }, EffectParity.Official("explode"),
            gradientSlot: nameof(ExplodeEffect.FlashColor), inputs: Small);

    // ───────────── 충격파 고리 ─────────────

    [Theory]
    [InlineData(ExplodeCenter.Middle, 0, 0, 16, 1, 0xFFFFFFFFu, true, false)]
    [InlineData(ExplodeCenter.BottomCenter, 0, 0, 5, 3, 0xC0FF8020u, true, true)]
    [InlineData(ExplodeCenter.Pivot, 0, 0, 1, 1, 0xFF40FF80u, false, false)]
    [InlineData(ExplodeCenter.Custom, -2, 30, 9, 8, 0x80A0C0FFu, false, true)]
    [InlineData(ExplodeCenter.Custom, 4, 3, 128, 2, 0xFF2040FFu, true, false)]
    [InlineData(ExplodeCenter.Middle, 0, 0, 7, 1, 0x00FFFFFFu, true, false)]
    [InlineData(ExplodeCenter.Pivot, 0, 0, 12, 5, 0x40FFFFFFu, true, true)]
    public void Shockwave_port_matches(ExplodeCenter center, int centerX, int centerY, int radius, int thickness, uint argb, bool fade, bool behind)
        => EffectParity.AssertParity(new ShockwaveEffect
        {
            Center = center, CenterX = centerX, CenterY = centerY, Radius = radius, Thickness = thickness, Color = new Color32(argb), Fade = fade, Behind = behind,
        }, EffectParity.Official("shockwave"), frameCount: radius > 64 ? 3 : 6, inputs: radius > 64 ? Tiny : null);

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    public void Shockwave_port_matches_with_gradients(int thickness, bool behind)
        => EffectParity.AssertParity(new ShockwaveEffect { Radius = 9, Thickness = thickness, Behind = behind, Color = new Color32(0, 0, 0, 0) },
            EffectParity.Official("shockwave"), gradientSlot: nameof(ShockwaveEffect.Color), inputs: Small);

    // ───────────── 홀로그램 ─────────────

    public static TheoryData<HologramEffect> Holograms() =>
    [
        new HologramEffect(),
        new HologramEffect { ScanStyle = HologramScanStyle.Fade, ScanGap = 3, ScanStrength = 100, Dither = true, GlitchRate = 100, GlitchShift = 8, Seed = 17 },
        new HologramEffect { GlitchRate = 100, RgbSplit = false, EdgeGlow = false, FlickerRate = 100, FlickerStrength = 100, Seed = 0 },
        new HologramEffect { TintStrength = 0, Band = false, ScanScroll = false, ScanGap = 6, Opacity = 100, FlickerRate = 0, EdgeOpacity = 100, Color = new Color32(255, 0, 128, 40) },
        new HologramEffect { TintStrength = 100, BandSize = 16, BandStrength = 100, ScanStrength = 0, Opacity = 0, Dither = true, GlitchRate = 60, GlitchShift = 1, Seed = 9999 },
        new HologramEffect { Opacity = 100, FlickerRate = 100, FlickerStrength = 50, EdgeOpacity = 0, GlitchRate = 50, Seed = 123, Color = Color32.White },
        new HologramEffect { ScanStyle = HologramScanStyle.Fade, BandStrength = 0, Opacity = 55, Dither = true, EdgeOpacity = 100, Color = Color32.Black, Seed = 4 },
    ];

    [Theory]
    [MemberData(nameof(Holograms))]
    public void Hologram_port_matches(HologramEffect hologram)
        => EffectParity.AssertParity(hologram, EffectParity.Official("hologram"), frameCount: 8);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hologram_port_matches_with_gradients(bool dither)
        => EffectParity.AssertParity(new HologramEffect { GlitchRate = 50, EdgeOpacity = 90, Dither = dither, Seed = 8 }, EffectParity.Official("hologram"),
            gradientSlot: nameof(HologramEffect.Color), inputs: Small);

    // ───────────── 잔상 ─────────────

    private static readonly IReadOnlyList<EffectParity.Input> Moving = EffectParity.Inputs.Where(i => i.Name is "moving frames" or "player" or "holes and partial alpha" or "empty").ToList();
    private static readonly IReadOnlyList<EffectParity.Input> MovingSmall = EffectParity.Inputs.Where(i => i.Name is "moving frames" or "single pixel").ToList();

    [Theory]
    [InlineData(3, 1, 60, false, 0xFF5096FFu)]
    [InlineData(1, 1, 100, true, 0xFFFF0000u)]
    [InlineData(2, 2, 100, true, 0x96FF5028u)]
    [InlineData(8, 1, 1, false, 0xFF5096FFu)]
    [InlineData(4, 3, 35, true, 0x00FFFFFFu)]
    public void Afterimage_port_matches(int count, int interval, int opacity, bool tint, uint argb)
        => EffectParity.AssertParity(new AfterimageEffect { Count = count, Interval = interval, Opacity = opacity, Tint = tint, TintColor = new Color32(argb) },
            EffectParity.Official("afterimage"), frameCount: 8, inputs: Moving);

    [Fact]
    public void Afterimage_port_matches_with_gradients()
        => EffectParity.AssertParity(new AfterimageEffect { Count = 2, Opacity = 90, Tint = true }, EffectParity.Official("afterimage"),
            gradientSlot: nameof(AfterimageEffect.TintColor), inputs: MovingSmall);

    [Fact]
    public void Afterimage_port_is_deferred_and_sees_other_ported_effects()
    {
        // 목록 맨 앞에 두어도 다른 효과 뒤에 그린다. 클리핑된 것은 클리핑되지 않은 미루는 효과 뒤에, 기준이 없으면 그리지 않는다.
        // 다른 효과도 공식 스크립트로 바꾼 목록(잔상이 다른 스크립트 효과를 입힌 앞 프레임을 받는지)도 같다.
        var afterimage = new AfterimageEffect { Count = 3, Interval = 1, Opacity = 80, Tint = true, TintColor = new Color32(255, 80, 40, 150) };
        var shadow = new ShadowEffect { OffsetX = 2, OffsetY = 1 };
        var outline = new OutlineEffect { Color = new Color32(20, 200, 90) };
        var explode = new ExplodeEffect { DistanceX = 6, DistanceY = 4, FlashFrames = 2 };
        var wave = new ShockwaveEffect { Radius = 6 };
        var hologram = new HologramEffect { GlitchRate = 70 };
        IReadOnlyList<LayerEffect>[] lists =
        [
            [afterimage, outline, shadow],
            [outline, afterimage with { ClipToPrevious = true }, shadow, afterimage with { Opacity = 30 }],
            [outline with { Enabled = false }, afterimage with { ClipToPrevious = true }, shadow],
            [shadow, outline with { ClipToPrevious = true }, afterimage with { ClipToPrevious = true }],
            [explode, afterimage],
            [afterimage, wave, hologram],
            [hologram, afterimage with { Frames = new FrameRange(2, 6) }, explode with { ClipToPrevious = true }],
        ];
        foreach (var list in lists)
        {
            var label = string.Join(" + ", list.Select(e => e.GetType().Name));
            EffectParity.AssertSameLists(list, [.. list.Select(e => e is AfterimageEffect ? EffectParity.ToScript(e, EffectParity.Official(Id(e))) : e)],
                frameCount: 8, inputs: MovingSmall, label: label);
            EffectParity.AssertSameLists(list, [.. list.Select(e => e is ShadowEffect or OutlineEffect ? e : EffectParity.ToScript(e, EffectParity.Official(Id(e))))],
                frameCount: 8, inputs: MovingSmall, label: label + " (all ported)");
        }
    }

    // ───────────── 피벗 ─────────────

    [Fact]
    public void Pivot_centered_ports_match_with_a_frame_pivot()
    {
        LayerEffect[] effects =
        [
            new ExplodeEffect { Center = ExplodeCenter.Pivot, PieceSize = 2, Gravity = 9 },
            new ShockwaveEffect { Center = ExplodeCenter.Pivot, Radius = 7, Thickness = 2 },
            new ShockwaveEffect { Center = ExplodeCenter.Pivot, Radius = 4, Behind = true },
        ];
        foreach (var pivot in new PointF[] { new(3.5f, 9f), new(-2.25f, 0.75f), new(30f, 30f) })
            foreach (var e in effects)
            {
                var script = EffectParity.ToScript(e, EffectParity.Official(Id(e)));
                foreach (var input in EffectParity.Inputs)
                    for (int f = 0; f < 6; f++)
                    {
                        var source = input.Frames[f % input.Frames.Count];
                        var want = LayerEffects.Render(source, LayerEffects.At([e], f, 6, pivot));
                        var fast = LayerEffects.Render(source, LayerEffects.At([script], f, 6, pivot));
                        (PixelImage Image, int OffsetX, int OffsetY) jint;
                        using (ScriptEffectProgram.UseJintOnly())
                            jint = LayerEffects.Render(source, LayerEffects.At([script], f, 6, pivot));
                        foreach (var (path, got) in new[] { ("fast", fast), ("Jint", jint) })
                            if ((want.OffsetX, want.OffsetY) != (got.OffsetX, got.OffsetY) || !want.Image.ContentEquals(got.Image))
                                throw new XunitException($"{e.GetType().Name} pivot {pivot} / {input.Name} / frame {f} / {path}: differs");
                    }
            }
    }

    private static string Id(LayerEffect e) => e switch
    {
        ExplodeEffect => "explode",
        ShockwaveEffect => "shockwave",
        HologramEffect => "hologram",
        AfterimageEffect => "afterimage",
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };
}
