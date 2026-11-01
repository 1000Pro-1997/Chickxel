using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Xunit.Sdk;

namespace Chickxel.Core.Tests;

/// <summary>
/// 디더·무작위·옮기기 묶음(나타나기·사라지기, 녹아내리기, 늘었다 줄기, 그림자)의 공식 스크립트 효과가 옮긴 기본 효과와 효과 파이프라인에서 바이트까지 같은지
/// (<see cref="EffectParity"/>, 빠른 경로·Jint).
/// </summary>
public class OfficialEffectParityDitherTests
{
    [Fact]
    public void Dither_ports_are_listed_on_the_fast_path_with_built_in_names()
    {
        var ko = System.Globalization.CultureInfo.GetCultureInfo("ko");
        var en = System.Globalization.CultureInfo.GetCultureInfo("en");
        foreach (var (id, key) in new[] { ("shadow", "Effect_Shadow"), ("squash", "Effect_SquashStretch"), ("melt", "Effect_Melt"), ("appear", "Effect_Appear") })
        {
            Assert.Contains(id, OfficialEffects.Ids);
            var program = EffectParity.Official(id);
            Assert.True(program.ExecutionPath.Compiled, $"{id}: {program.ExecutionPath.Reason}");
            foreach (var culture in new[] { ko, en })
            {
                var name = Localization.DomainStrings.ResourceManager.GetString(key, culture)!;
                if (id == "appear")
                {
                    // 기본 효과는 사라지기면 이름이 바뀐다 — 공식 효과는 둘을 함께 쓴 이름 하나
                    Assert.StartsWith(name + " / ", program.NameFor(culture), StringComparison.Ordinal);
                    Assert.EndsWith(Localization.DomainStrings.ResourceManager.GetString("Effect_Disappear", culture)!, program.NameFor(culture),
                        StringComparison.OrdinalIgnoreCase);
                }
                else Assert.Equal(name, program.NameFor(culture));
            }
        }
    }

    [Theory]
    [InlineData(false, AppearStyle.Fade)]
    [InlineData(true, AppearStyle.Fade)]
    [InlineData(false, AppearStyle.Dither)]
    [InlineData(true, AppearStyle.Dither)]
    public void Appear_port_matches(bool disappear, AppearStyle style)
        => EffectParity.AssertParity(new AppearEffect { Disappear = disappear, Style = style }, EffectParity.Official("appear"), frameCount: 9);

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(true, 9999)]
    [InlineData(false, 4321)]
    public void Appear_scatter_port_matches(bool disappear, int seed)
        => EffectParity.AssertParity(new AppearEffect { Disappear = disappear, Style = AppearStyle.Scatter, Seed = seed }, EffectParity.Official("appear"),
            frameCount: 9);

    [Theory]
    [InlineData(false, WipeDirection.Top, 1)]
    [InlineData(true, WipeDirection.Top, 0)]
    [InlineData(false, WipeDirection.Bottom, 2)]
    [InlineData(true, WipeDirection.Bottom, 1)]
    [InlineData(false, WipeDirection.Left, 0)]
    [InlineData(true, WipeDirection.Left, 2)]
    [InlineData(false, WipeDirection.Right, 1)]
    [InlineData(true, WipeDirection.Right, 2)]
    [InlineData(false, WipeDirection.Right, 0)]
    [InlineData(true, WipeDirection.Top, 2)]
    public void Appear_wipe_port_matches(bool disappear, WipeDirection from, int edge)
        => EffectParity.AssertParity(new AppearEffect { Disappear = disappear, Style = AppearStyle.Wipe, From = from, Edge = edge }, EffectParity.Official("appear"),
            frameCount: 9);

    [Theory]
    [InlineData(12, 60, true, true, 1)]
    [InlineData(12, 60, false, true, 1)]
    [InlineData(12, 60, true, false, 1)]
    [InlineData(5, 0, false, false, 0)]
    [InlineData(1, 100, true, true, 9999)]
    [InlineData(128, 100, true, false, 77)]
    [InlineData(37, 25, false, true, 4321)]
    [InlineData(3, 80, true, true, 2)]
    public void Melt_port_matches(int distance, int irregularity, bool stretch, bool fade, int seed)
        => EffectParity.AssertParity(new MeltEffect { Distance = distance, Irregularity = irregularity, Stretch = stretch, Fade = fade, Seed = seed },
            EffectParity.Official("melt"), frameCount: 9);

    [Theory]
    [InlineData(20, 20, 2, SquashAnchor.BottomCenter, false)]
    [InlineData(20, 20, 2, SquashAnchor.BottomCenter, true)]
    [InlineData(50, 50, 1, SquashAnchor.Middle, false)]
    [InlineData(50, 0, 3, SquashAnchor.Pivot, true)]
    [InlineData(0, 50, 3, SquashAnchor.Middle, true)]
    [InlineData(0, 0, 2, SquashAnchor.BottomCenter, false)]
    [InlineData(7, 33, 1, SquashAnchor.Pivot, false)]
    [InlineData(45, 13, 3, SquashAnchor.BottomCenter, false)]
    public void Squash_port_matches(int widthAmount, int heightAmount, int bounces, SquashAnchor anchor, bool stretchFirst)
        => EffectParity.AssertParity(new SquashEffect { WidthAmount = widthAmount, HeightAmount = heightAmount, Bounces = bounces, Anchor = anchor, StretchFirst = stretchFirst },
            EffectParity.Official("squash"), frameCount: 11);

    // 피벗 기준점: 프로젝트 피벗(원본 기준, 소수·그림 밖 포함)을 넘겨 그린다
    [Theory]
    [InlineData(3.5f, 9f)]
    [InlineData(0f, 0f)]
    [InlineData(-4f, 20.25f)]
    public void Squash_port_matches_with_a_pivot(float pivotX, float pivotY)
    {
        var program = EffectParity.Official("squash");
        foreach (var built in new LayerEffect[]
                 {
                     new SquashEffect { WidthAmount = 40, HeightAmount = 30, Bounces = 2, Anchor = SquashAnchor.Pivot },
                     new SquashEffect { WidthAmount = 50, HeightAmount = 50, Bounces = 1, Anchor = SquashAnchor.Pivot, StretchFirst = true },
                 })
            foreach (var (label, list) in EffectParity.Variants(built, 11))
            {
                var script = list.Select(e => e is SquashEffect ? EffectParity.ToScript(e, program) : e).ToArray();
                AssertSameWithPivot(list, script, new PointF(pivotX, pivotY), 11, label);
            }
    }

    [Theory]
    [InlineData(1, 1, ShadowDither.None, 0x80000000u)]
    [InlineData(-16, -16, ShadowDither.None, 0xFF203040u)]
    [InlineData(16, 16, ShadowDither.Light25, 0x80000000u)]
    [InlineData(5, -3, ShadowDither.Half50, 0x40FF0000u)]
    [InlineData(-1, 0, ShadowDither.Dense75, 0xFF00FF00u)]
    [InlineData(0, 7, ShadowDither.None, 0xC8102030u)]
    [InlineData(0, 0, ShadowDither.None, 0x80000000u)]
    [InlineData(0, 0, ShadowDither.Half50, 0x80000000u)]
    [InlineData(3, 0, ShadowDither.None, 0x01FFFFFFu)]
    [InlineData(-2, 4, ShadowDither.None, 0x00000000u)]
    [InlineData(0, -5, ShadowDither.Light25, 0x00336699u)]
    public void Shadow_port_matches(int offsetX, int offsetY, ShadowDither dither, uint argb)
        => EffectParity.AssertParity(new ShadowEffect { OffsetX = offsetX, OffsetY = offsetY, Dither = dither, Color = new Color32(argb) },
            EffectParity.Official("shadow"));

    [Theory]
    [InlineData(2, 3, ShadowDither.None)]
    [InlineData(-3, -1, ShadowDither.Light25)]
    [InlineData(4, -2, ShadowDither.Half50)]
    [InlineData(-1, 5, ShadowDither.Dense75)]
    public void Shadow_port_matches_with_gradients(int offsetX, int offsetY, ShadowDither dither)
        => EffectParity.AssertParity(new ShadowEffect { OffsetX = offsetX, OffsetY = offsetY, Dither = dither }, EffectParity.Official("shadow"),
            gradientSlot: nameof(ShadowEffect.Color));

    // Color32.BlendOver(단정도·짝수 반올림)를 모든 알파 쌍에서: 한 줄 아래로 옮기면 홀수 줄 2j+1(알파 (x + k) mod 256) 아래에 짝수 줄 2j(알파 x)의 그림자.
    // k 32개씩 나눈 그림 8장(Jint 호출 한도 안)
    [Fact]
    public void Shadow_port_blends_every_alpha_pair_like_the_built_in_effect()
    {
        var inputs = new List<EffectParity.Input>();
        for (int k0 = 0; k0 < 256; k0 += 32)
        {
            var image = new PixelImage(256, 64);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < 256; x++)
                    image[x, y] = new Color32((byte)(x * 7 + y + k0), (byte)(y * 13 + x * 3), (byte)(255 - x), (byte)(y % 2 == 0 ? x : (x + k0 + y / 2) % 256));
            inputs.Add(new($"alpha pairs from {k0}", [image]));
        }
        var program = EffectParity.Official("shadow");
        foreach (var color in new[] { new Color32(250, 3, 128, 255), new Color32(17, 200, 99, 1), new Color32(128, 128, 128, 77) })
        {
            var built = new ShadowEffect { OffsetX = 0, OffsetY = 1, Color = color };
            EffectParity.AssertSameLists([built], [EffectParity.ToScript(built, program)], frameCount: 1, inputs: inputs, label: color.ToHex(), fast: program);
        }
    }

    // 옮기는 효과가 앞 효과의 여백과 원본 위치를 이어받는지: 그림자 → 늘었다 줄기 → 녹아내리기 → 쓸기(클리핑) → 그림자(그라데이션)
    [Fact]
    public void Dither_ports_chain_like_the_built_in_effects()
    {
        LayerEffect[] builtIn =
        [
            new ShadowEffect { OffsetX = -3, OffsetY = 2, Dither = ShadowDither.Half50 },
            new SquashEffect { WidthAmount = 30, HeightAmount = 25, Anchor = SquashAnchor.Middle },
            new MeltEffect { Distance = 9, Irregularity = 70, Seed = 5 },
            new AppearEffect { Style = AppearStyle.Wipe, From = WipeDirection.Left, Edge = 2, ClipToPrevious = true },
            new ShadowEffect { OffsetX = 2, OffsetY = 2 }
                with { ColorGradients = new Dictionary<string, EffectColorGradient> { [nameof(ShadowEffect.Color)] = EffectParity.Gradients[1] } },
            new AppearEffect { Style = AppearStyle.Scatter, Disappear = true, Seed = 12, Frames = new FrameRange(2, 7) },
        ];
        var programs = new Dictionary<Type, ScriptEffectProgram>
        {
            [typeof(ShadowEffect)] = EffectParity.Official("shadow"),
            [typeof(SquashEffect)] = EffectParity.Official("squash"),
            [typeof(MeltEffect)] = EffectParity.Official("melt"),
            [typeof(AppearEffect)] = EffectParity.Official("appear"),
        };
        EffectParity.AssertSameLists(builtIn, [.. builtIn.Select(e => EffectParity.ToScript(e, programs[e.GetType()]))], frameCount: 9, label: "dither chain");
    }

    // 투명(알파 0)인데 RGB가 남은 픽셀: 지우는 자리는 (0, 0, 0, 0), 건드리지 않는 자리는 RGB까지 그대로여야 한다
    [Fact]
    public void Dither_ports_keep_or_clear_colored_transparent_pixels_like_the_built_in_effects()
    {
        var image = new PixelImage(12, 10);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                bool inside = x is > 1 and < 10 && y is > 1 and < 9 && (x + 2 * y) % 7 != 0;
                image[x, y] = new Color32((byte)(x * 21 + 3), (byte)(y * 25 + 1), (byte)(x * y + 9), (byte)(inside ? (x * y) % 5 == 0 ? 90 : 255 : 0));
            }
        EffectParity.Input[] inputs = [new("colored transparent", [image])];
        foreach (var style in Enum.GetValues<AppearStyle>())
            foreach (var disappear in new[] { false, true })
                EffectParity.AssertParity(new AppearEffect { Style = style, Disappear = disappear, From = WipeDirection.Right, Edge = 2 }, EffectParity.Official("appear"),
                    frameCount: 9, inputs: inputs);
        EffectParity.AssertParity(new MeltEffect { Distance = 4, Irregularity = 90 }, EffectParity.Official("melt"), frameCount: 9, inputs: inputs);
        EffectParity.AssertParity(new SquashEffect { WidthAmount = 50, HeightAmount = 40, Anchor = SquashAnchor.Middle }, EffectParity.Official("squash"),
            frameCount: 9, inputs: inputs);
        foreach (var dither in Enum.GetValues<ShadowDither>())
            EffectParity.AssertParity(new ShadowEffect { OffsetX = -2, OffsetY = 1, Dither = dither, Color = new Color32(10, 200, 30, 0) }, EffectParity.Official("shadow"),
                inputs: inputs);
    }

    private static void AssertSameWithPivot(IReadOnlyList<LayerEffect> expected, IReadOnlyList<LayerEffect> actual, PointF pivot, int frameCount, string label)
    {
        foreach (var input in EffectParity.Inputs)
            for (int f = 0; f < frameCount; f++)
            {
                var source = input.Frames[f % input.Frames.Count];
                var want = LayerEffects.Render(source, LayerEffects.At(expected, f, frameCount, pivot));
                var got = LayerEffects.Render(source, LayerEffects.At(actual, f, frameCount, pivot));
                Same(want, got, $"{label} / {input.Name} / frame {f} / fast");
                using (ScriptEffectProgram.UseJintOnly())
                    got = LayerEffects.Render(source, LayerEffects.At(actual, f, frameCount, pivot));
                Same(want, got, $"{label} / {input.Name} / frame {f} / Jint");
            }

        static void Same((PixelImage Image, int OffsetX, int OffsetY) want, (PixelImage Image, int OffsetX, int OffsetY) got, string where)
        {
            if ((want.OffsetX, want.OffsetY, want.Image.Width, want.Image.Height) != (got.OffsetX, got.OffsetY, got.Image.Width, got.Image.Height))
                throw new XunitException($"{where}: size/offset differ");
            if (!want.Image.ContentEquals(got.Image))
                throw new XunitException($"{where}: pixels differ");
        }
    }
}
