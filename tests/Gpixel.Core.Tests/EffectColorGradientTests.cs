using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class EffectColorGradientTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);

    private static EffectColorGradient Horizontal(Color32 left, Color32 right,
        GradientInterpolation interpolation = GradientInterpolation.Smooth)
        => new()
        {
            Angle = 0,
            Stops = [new(1, right), new(0, left)], // unsorted input is normalized on assignment
            Interpolation = interpolation,
        };

    [Fact]
    public void ColorOverlayGradient_InterpolatesStopsAndIndependentOpacity_PreservesPixelAlpha()
    {
        var image = new PixelImage(3, 1);
        image.Clear(new Color32(0, 0, 0, 77));
        var gradient = Horizontal(Red, Blue) with
        {
            AlphaStops = [new(1, 255), new(0, 0)],
        };
        var effect = new ColorOverlayEffect
        {
            ColorGradients = new Dictionary<string, EffectColorGradient> { [nameof(ColorOverlayEffect.Color)] = gradient },
        };

        var result = LayerEffects.RenderClipped(image, [effect]);
        Assert.Equal(image[0, 0], result[0, 0]);
        Assert.Equal((byte)77, result[1, 0].A);
        Assert.InRange(result[1, 0].R, (byte)60, (byte)68);
        Assert.InRange(result[1, 0].B, (byte)60, (byte)68);
        Assert.Equal(Blue.WithAlpha(77), result[2, 0]);
    }

    [Fact]
    public void ColorOverlayGradient_ColorAlphaControlsIntensity_NotPixelAlpha()
    {
        var image = new PixelImage(2, 1);
        image.Clear(new Color32(0, 0, 0, 91));
        var effect = new ColorOverlayEffect
        {
            ColorGradients = new Dictionary<string, EffectColorGradient>
            {
                [nameof(ColorOverlayEffect.Color)] = Horizontal(Red.WithAlpha(0), Blue),
            },
        };
        var result = LayerEffects.RenderClipped(image, [effect]);
        Assert.Equal(image[0, 0], result[0, 0]);
        Assert.Equal(Blue.WithAlpha(91), result[1, 0]);
    }

    [Fact]
    public void HitFlash_UsesSpatialStopsAndAlphaWithoutChangingShape()
    {
        var image = new PixelImage(3, 1);
        image.Clear(new Color32(0, 0, 0, 77));
        var effect = new HitFlashEffect
        {
            Color = Color32.White,
            ColorGradients = new Dictionary<string, EffectColorGradient>
            {
                [nameof(HitFlashEffect.Color)] = Horizontal(Red.WithAlpha(0), Blue),
            },
        };
        var result = LayerEffects.RenderClipped(image, [effect]);
        Assert.Equal(image[0, 0], result[0, 0]);
        Assert.Equal(Blue.WithAlpha(77), result[2, 0]);
    }

    [Fact]
    public void PaletteSwap_GradientMatchesSourcePerPosition_AndColorsTargetPerPosition()
    {
        var image = new PixelImage(3, 1);
        image[0, 0] = Red;
        image[1, 0] = Blue;
        image[2, 0] = Blue.WithAlpha(99);
        var effect = new PaletteSwapEffect
        {
            Pairs = [new ColorSwap(Red, Green)],
            ColorGradients = new Dictionary<string, EffectColorGradient>
            {
                ["Pairs[0].From"] = Horizontal(Red, Blue, GradientInterpolation.Steps),
                ["Pairs[0].To"] = Horizontal(Green.WithAlpha(0), Green),
            },
        };
        var result = LayerEffects.RenderClipped(image, [effect]);
        Assert.Equal(Red, result[0, 0]);
        Assert.Equal(Green.WithAlpha(99), result[2, 0]);
    }

    [Fact]
    public void ColorCycle_GradientSourceAndDestination_PreserveOriginalOpacity()
    {
        var image = new PixelImage(3, 1);
        image[0, 0] = Red;
        image[1, 0] = Blue;
        image[2, 0] = Blue.WithAlpha(99);
        var effect = new ColorCycleEffect
        {
            Colors = [Red, Green], Steps = 2,
            ColorGradients = new Dictionary<string, EffectColorGradient>
            {
                ["Colors[0]"] = Horizontal(Red, Blue, GradientInterpolation.Steps),
                ["Colors[1]"] = Horizontal(Green, Red),
            },
            Frames = new FrameRange(0, 2),
        };
        var active = LayerEffects.At([effect], 1, 3);
        var result = LayerEffects.RenderClipped(image, active);
        Assert.Equal(Green, result[0, 0]);
        Assert.Equal(Red.WithAlpha(99), result[2, 0]);
    }

    [Fact]
    public void Shockwave_SamplesRingAtRenderedPosition()
    {
        var image = new PixelImage(7, 7);
        var effect = new ShockwaveEffect
        {
            Center = ExplodeCenter.Custom, CenterX = 3, CenterY = 3,
            Radius = 2, FrameStep = 1, FrameSpan = 1, Fade = false,
            ColorGradients = new Dictionary<string, EffectColorGradient>
            {
                [nameof(ShockwaveEffect.Color)] = Horizontal(Red, Blue),
            },
        };
        var (result, ox, oy) = LayerEffects.Render(image, [effect]);
        int cx = 3 - ox, cy = 3 - oy;
        Assert.Equal(Red, result[cx - 2, cy]);
        Assert.Equal(Blue, result[cx + 2, cy]);
        Assert.Equal(Color32.Transparent, result[cx, cy]);
    }

    [Fact]
    public void GradientStopsAndAlphaSurviveProjectSaveAndReload()
    {
        var project = new SpriteProject(3, 1);
        var cel = new PixelImage(3, 1);
        cel.Clear(Red.WithAlpha(77));
        project.SetCel(0, 0, cel);
        var gradient = Horizontal(Red, Blue) with
        {
            Angle = 180,
            Dither = GradientDither.Bayer4,
            AlphaStops = [new(0, 255), new(1, 0)],
        };
        project.SetLayerEffects(0, [new ColorOverlayEffect
        {
            Amount = 75,
            ColorGradients = new Dictionary<string, EffectColorGradient> { [nameof(ColorOverlayEffect.Color)] = gradient },
        }]);
        using var stream = new MemoryStream();
        ProjectIO.Save(project, stream);
        stream.Position = 0;
        var loaded = ProjectIO.Load(stream);
        var stored = loaded.Layers[0].Effects[0].ColorGradients[nameof(ColorOverlayEffect.Color)];
        Assert.Equal(gradient, stored);
        Assert.True(project.Flatten(0).ContentEquals(loaded.Flatten(0)));
    }

}
