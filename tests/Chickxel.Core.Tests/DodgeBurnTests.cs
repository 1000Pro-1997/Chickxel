using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.Tests;

public class DodgeBurnTests
{
    private static readonly Color32 Green = new(60, 150, 70);
    private static readonly Color32 Gray = new(128, 128, 128);

    private static float Hue(Color32 c)
    {
        var lab = OkLab.FromColor(c);
        return MathF.Atan2(lab.B, lab.A) * 180f / MathF.PI;
    }

    private static float Chroma(Color32 c)
    {
        var lab = OkLab.FromColor(c);
        return MathF.Sqrt(lab.A * lab.A + lab.B * lab.B);
    }

    private static float HueDistance(float a, float b) => MathF.Abs(MathF.IEEERemainder(a - b, 360f));

    [Theory]
    [InlineData(DodgeMode.HueShift)]
    [InlineData(DodgeMode.Perceptual)]
    [InlineData(DodgeMode.Value)]
    [InlineData(DodgeMode.TintShade)]
    [InlineData(DodgeMode.Saturation)]
    public void TransparentStaysAndAlphaIsKept(DodgeMode mode)
    {
        Assert.Equal(Color32.Transparent, DodgeBurn.Apply(Color32.Transparent, true, 0.2f, mode));
        var half = Green.WithAlpha(128);
        Assert.Equal(128, DodgeBurn.Apply(half, true, 0.2f, mode).A);
        Assert.Equal(128, DodgeBurn.Apply(half, false, 0.2f, mode).A);
    }

    [Theory]
    [InlineData(DodgeMode.HueShift)]
    [InlineData(DodgeMode.Perceptual)]
    [InlineData(DodgeMode.Value)]
    [InlineData(DodgeMode.TintShade)]
    public void LightenRaisesAndDarkenLowersPerceivedLightness(DodgeMode mode)
    {
        float l = OkLab.FromColor(Green).L;
        Assert.True(OkLab.FromColor(DodgeBurn.Apply(Green, true, 0.2f, mode)).L > l + 0.02f);
        Assert.True(OkLab.FromColor(DodgeBurn.Apply(Green, false, 0.2f, mode)).L < l - 0.02f);
    }

    [Fact]
    public void HueShiftTurnsHighlightsWarmAndShadowsCool()
    {
        const float warm = 85f, cool = 275f;
        float hue = Hue(Green);
        Assert.True(HueDistance(Hue(DodgeBurn.Apply(Green, true, 0.2f, DodgeMode.HueShift)), warm) < HueDistance(hue, warm) - 3);
        Assert.True(HueDistance(Hue(DodgeBurn.Apply(Green, false, 0.2f, DodgeMode.HueShift)), cool) < HueDistance(hue, cool) - 3);
    }

    [Fact]
    public void HueShiftLeavesGrayNeutral()
    {
        foreach (bool lighten in new[] { true, false })
        {
            var c = DodgeBurn.Apply(Gray, lighten, 0.3f, DodgeMode.HueShift);
            Assert.True(Math.Abs(c.R - c.G) <= 1 && Math.Abs(c.G - c.B) <= 1, $"{c}");
            Assert.NotEqual(Gray, c);
        }
    }

    [Fact]
    public void PerceptualKeepsHue()
    {
        float hue = Hue(Green);
        Assert.True(HueDistance(Hue(DodgeBurn.Apply(Green, true, 0.2f, DodgeMode.Perceptual)), hue) < 2);
        Assert.True(HueDistance(Hue(DodgeBurn.Apply(Green, false, 0.2f, DodgeMode.Perceptual)), hue) < 2);
    }

    [Fact]
    public void SaturationChangesChromaButNotLightness()
    {
        var muted = new Color32(110, 140, 115);
        float l = OkLab.FromColor(muted).L, c = Chroma(muted);
        var vivid = DodgeBurn.Apply(muted, true, 0.3f, DodgeMode.Saturation);
        var dull = DodgeBurn.Apply(muted, false, 0.3f, DodgeMode.Saturation);
        Assert.True(Chroma(vivid) > c * 1.15f);
        Assert.True(Chroma(dull) < c * 0.85f);
        Assert.True(MathF.Abs(OkLab.FromColor(vivid).L - l) < 0.01f);
        Assert.True(MathF.Abs(OkLab.FromColor(dull).L - l) < 0.01f);
        Assert.Equal(Gray, DodgeBurn.Apply(Gray, true, 0.3f, DodgeMode.Saturation));
    }

    [Fact]
    public void RepeatedLighteningReachesWhiteAndDarkeningReachesBlack()
    {
        foreach (var mode in new[] { DodgeMode.HueShift, DodgeMode.Perceptual, DodgeMode.TintShade })
        {
            Color32 up = Green, down = Green;
            for (int i = 0; i < 80; i++)
            {
                up = DodgeBurn.Apply(up, true, 0.2f, mode);
                down = DodgeBurn.Apply(down, false, 0.2f, mode);
            }
            Assert.True(up.R >= 250 && up.G >= 250 && up.B >= 250, $"{mode} {up}");
            Assert.True(down.R <= 5 && down.G <= 5 && down.B <= 5, $"{mode} {down}");
        }
    }
}
