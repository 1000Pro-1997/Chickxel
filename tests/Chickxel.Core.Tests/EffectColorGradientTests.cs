using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

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
            ColorSpace = GradientColorSpace.Rgb,
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
    public void ColorOverlayGradient_UsesSelectedColorSpaceForPreviewAndRender()
    {
        var gradient = Horizontal(Red, Green);
        Assert.Equal(GradientColorSpace.OkLch, gradient.ColorSpace);
        var perceptual = Sample(gradient, 3, 1);
        var rgb = Sample(gradient with { ColorSpace = GradientColorSpace.Rgb }, 3, 1);
        Assert.Equal(new Color32(128, 128, 0), rgb[1, 0]);
        Assert.NotEqual(rgb[1, 0], perceptual[1, 0]);
        Assert.Equal(Gradient.ColorAt(gradient.Stops, 0.5), perceptual[1, 0]);
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
            ColorSpace = GradientColorSpace.Hsl,
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
        // 열면 색 덮기의 공식 스크립트 — 그라데이션 슬롯은 스크립트 이름(Color → color)
        var stored = Assert.IsType<ScriptEffect>(loaded.Layers[0].Effects[0]).ColorGradients[LegacyEffects.ScriptSlot(nameof(ColorOverlayEffect.Color))];
        Assert.Equal(GradientColorSpace.Hsl, stored.ColorSpace);
        Assert.Equal(gradient, stored);
        Assert.True(project.Flatten(0).ContentEquals(loaded.Flatten(0)));
    }

    [Fact]
    public void SpatialModesRoundTripAndMissingModeFieldsKeepOldLinearGradient()
    {
        const string slot = nameof(ColorOverlayEffect.Color);
        // 열면 색 덮기의 공식 스크립트 — 그라데이션 슬롯은 스크립트 이름
        string scriptSlot = LegacyEffects.ScriptSlot(slot);
        var gradients = Enum.GetValues<EffectGradientShape>()
            .SelectMany(shape => new[] { false, true }.Select(animated =>
                Horizontal(Red, Blue) with { Shape = shape, Animated = animated, Reverse = true, Cycles = 3,
                    Angle = 45, ColorSpace = animated ? GradientColorSpace.OkLab : GradientColorSpace.OkLch }))
            .ToArray();
        var project = new SpriteProject(5, 5);
        project.SetLayerEffects(0, gradients.Select(gradient => new ColorOverlayEffect
        {
            ColorGradients = new Dictionary<string, EffectColorGradient> { [slot] = gradient },
        }).ToArray());
        using var stream = new MemoryStream();
        ProjectIO.Save(project, stream);
        stream.Position = 0;
        Assert.Equal(gradients, ProjectIO.Load(stream).Layers[0].Effects
            .Select(effect => effect.ColorGradients[scriptSlot]));

        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            System.Text.Json.Nodes.JsonNode root;
            using (var reader = entry.Open()) root = System.Text.Json.Nodes.JsonNode.Parse(reader)!;
            var first = root["layers"]!.AsArray()[0]!["effects"]!.AsArray()[0]!["colorGradients"]![slot]!.AsObject();
            first.Remove("shape");
            first.Remove("animated");
            first.Remove("reverse");
            first.Remove("cycles");
            first.Remove("colorSpace");
            entry.Delete();
            using var writer = new System.Text.Json.Utf8JsonWriter(zip.CreateEntry(ProjectIO.ProjectEntryName).Open());
            root.WriteTo(writer);
        }
        stream.Position = 0;
        var legacyProject = ProjectIO.Load(stream);
        var legacy = legacyProject.Layers[0].Effects[0].ColorGradients[scriptSlot];
        Assert.Equal(gradients[0] with { Shape = EffectGradientShape.Linear, Animated = false, Reverse = false, Cycles = 1,
            ColorSpace = GradientColorSpace.Rgb }, legacy);
        var legacyMiddle = Sample(legacy, 3, 1)[1, 0];
        Assert.InRange(legacyMiddle.R, 127, 128);
        Assert.Equal((byte)0, legacyMiddle.G);
        Assert.InRange(legacyMiddle.B, 127, 128);
        using var savedAgain = new MemoryStream();
        ProjectIO.Save(legacyProject, savedAgain);
        savedAgain.Position = 0;
        Assert.Equal(GradientColorSpace.Rgb,
            ProjectIO.Load(savedAgain).Layers[0].Effects[0].ColorGradients[scriptSlot].ColorSpace);
    }

    private static PixelImage Sample(EffectColorGradient gradient, int width = 5, int height = 5,
        bool reverse = false)
    {
        var image = new PixelImage(width, height);
        image.Clear(Color32.Black);
        return LayerEffects.RenderClipped(image, [new ColorOverlayEffect
        {
            ColorGradients = new Dictionary<string, EffectColorGradient>
            {
                [nameof(ColorOverlayEffect.Color)] = reverse ? gradient with { Reverse = true } : gradient,
            },
        }]);
    }

    [Fact]
    public void DefaultsPreserveOldLinearEndpointsAndAvoidNonanimatedCopies()
    {
        var gradient = Horizontal(Red, Blue);
        Assert.Equal(EffectGradientShape.Linear, gradient.Shape);
        Assert.False(gradient.Animated);
        Assert.False(gradient.Reverse);
        Assert.Equal(1, gradient.Cycles);
        var image = Sample(gradient, 5, 1);
        Assert.Equal(Red, image[0, 0]);
        Assert.Equal(Blue, image[4, 0]);
        Assert.Equal(image[0, 0], Sample(gradient, 5, 1, reverse: true)[4, 0]);
        var effects = new LayerEffect[] { new ColorOverlayEffect
        {
            ColorGradients = new Dictionary<string, EffectColorGradient> { [nameof(ColorOverlayEffect.Color)] = gradient },
        } };
        Assert.Same(effects, LayerEffects.At(effects, 2, 5));
        Assert.Same(effects, LayerEffects.Animate(effects, 2, 5));
    }

    [Fact]
    public void ShapesUseDistinctGeometryAndAngle()
    {
        var gradient = Horizontal(Red, Blue);
        var radial = Sample(gradient with { Shape = EffectGradientShape.Radial });
        var square = Sample(gradient with { Shape = EffectGradientShape.Square });
        var angular = Sample(gradient with { Shape = EffectGradientShape.Angular });
        Assert.Equal(Red, radial[2, 2]);
        Assert.Equal(Blue, radial[0, 0]);
        Assert.NotEqual(Blue, radial[4, 2]);
        Assert.Equal(Red, square[2, 2]);
        Assert.Equal(Blue, square[4, 2]);
        Assert.Equal(Red, angular[4, 2]);
        Assert.NotEqual(angular[2, 0], angular[2, 4]);
        Assert.NotEqual(square[4, 2], Sample(gradient with { Shape = EffectGradientShape.Square, Angle = 45 })[4, 2]);
        Assert.NotEqual(radial[2, 2], Sample(gradient with { Shape = EffectGradientShape.Radial, Angle = 90 })[2, 2]);
        Assert.Equal(Red, Sample(gradient with { Shape = EffectGradientShape.Angular, Angle = 90 })[2, 0]);
    }

    [Theory]
    [InlineData(EffectGradientShape.Linear, 0, 2)]
    [InlineData(EffectGradientShape.Radial, 2, 2)]
    [InlineData(EffectGradientShape.Square, 2, 2)]
    [InlineData(EffectGradientShape.Angular, 4, 2)]
    public void StaticCyclesRepeatAndReverseDirection(EffectGradientShape shape, int x, int y)
    {
        var gradient = Horizontal(Red, Blue) with { Shape = shape, Cycles = 2 };
        var image = Sample(gradient);
        var reversed = Sample(gradient, reverse: true);
        Assert.Equal(Red, image[x, y]);
        Assert.Equal(shape == EffectGradientShape.Angular ? Red : Blue, reversed[x, y]);
        if (shape == EffectGradientShape.Linear)
            Assert.Equal(Blue, image[4, 2]); // Static linear keeps its final endpoint.
        else if (shape == EffectGradientShape.Radial || shape == EffectGradientShape.Square)
        {
            Assert.Equal(Blue, image[0, 0]);
            Assert.Equal(Blue, reversed[2, 2]);
        }
    }

    [Fact]
    public void AngularReverseTraversesClockwise()
    {
        var gradient = Horizontal(Red, Blue) with { Shape = EffectGradientShape.Angular };
        var forward = Sample(gradient);
        var reverse = Sample(gradient, reverse: true);
        Assert.Equal(forward[2, 0], reverse[2, 4]);
        Assert.Equal(forward[2, 4], reverse[2, 0]);
        Assert.Equal(Red, forward[4, 2]);
    }

    [Theory]
    [InlineData(EffectGradientShape.Linear, 0, 2)]
    [InlineData(EffectGradientShape.Radial, 2, 2)]
    [InlineData(EffectGradientShape.Square, 2, 2)]
    [InlineData(EffectGradientShape.Angular, 4, 2)]
    public void AnimatedColorsMoveAndReturnAtFrameRangeEndpoints(EffectGradientShape shape, int x, int y)
    {
        var gradient = Horizontal(Red, Blue) with { Shape = shape, Animated = true, Cycles = 3 };
        var effect = new ColorOverlayEffect
        {
            Frames = new FrameRange(2, 6),
            ColorGradients = new Dictionary<string, EffectColorGradient> { [nameof(ColorOverlayEffect.Color)] = gradient },
        };
        LayerEffect[] effects = [effect];
        PixelImage Render(int frame) => LayerEffects.RenderClipped(FilledBlack(),
            LayerEffects.At(effects, frame, 9));
        var first = Render(2);
        var middle = Render(3);
        var last = Render(6);
        Assert.True(first.ContentEquals(last));
        Assert.NotEqual(first[x, y], middle[x, y]);
        Assert.Empty(LayerEffects.At(effects, 1, 9));
        Assert.True(first.ContentEquals(LayerEffects.RenderClipped(FilledBlack(),
            LayerEffects.Animate(effects, 0, 9))));
        Assert.True(last.ContentEquals(LayerEffects.RenderClipped(FilledBlack(),
            LayerEffects.Animate(effects, 8, 9))));

        static PixelImage FilledBlack()
        {
            var image = new PixelImage(5, 5);
            image.Clear(Color32.Black);
            return image;
        }
    }

    [Fact]
    public void AnimatedColorsWithoutFrameRangeUseWholeProjectAndClampCycleCount()
    {
        var gradient = Horizontal(Red, Blue) with { Animated = true, Cycles = 40 };
        Assert.Equal(16, gradient.Cycles);
        Assert.Equal(1, (gradient with { Cycles = -3 }).Cycles);
        var effect = new ColorOverlayEffect
        {
            ColorGradients = new Dictionary<string, EffectColorGradient> { [nameof(ColorOverlayEffect.Color)] = gradient },
        };
        LayerEffect[] effects = [effect];
        Assert.NotSame(effects, LayerEffects.At(effects, 1, 6));
        var image = new PixelImage(5, 1);
        image.Clear(Color32.Black);
        PixelImage At(int frame) => LayerEffects.RenderClipped(image, LayerEffects.At(effects, frame, 6));
        Assert.True(At(0).ContentEquals(At(5)));
        Assert.False(At(0).ContentEquals(At(1)));
    }

    [Fact]
    public void ConcentricGradientsUseOpaqueBoundsRatherThanCanvasBounds()
    {
        var image = new PixelImage(9, 9);
        for (int y = 2; y <= 6; y++)
            for (int x = 2; x <= 6; x++)
                image[x, y] = Color32.Black;
        foreach (var shape in new[] { EffectGradientShape.Radial, EffectGradientShape.Square })
        {
            var effect = new ColorOverlayEffect
            {
                ColorGradients = new Dictionary<string, EffectColorGradient>
                {
                    [nameof(ColorOverlayEffect.Color)] = Horizontal(Red, Blue) with { Shape = shape },
                },
            };
            var result = LayerEffects.RenderClipped(image, [effect]);
            Assert.Equal(Red, result[4, 4]);
            Assert.Equal(Blue, result[2, 2]);
            Assert.Equal(Color32.Transparent, result[0, 0]);
        }
    }

    [Fact]
    public void AnimatedAngularDirectionMovesCounterclockwiseOrClockwise()
    {
        var source = new PixelImage(5, 5);
        source.Clear(Color32.Black);
        var gradient = Horizontal(Red, Blue) with { Shape = EffectGradientShape.Angular, Animated = true };
        PixelImage At(int frame, bool reverse)
        {
            LayerEffect[] effects = [new ColorOverlayEffect
            {
                ColorGradients = new Dictionary<string, EffectColorGradient>
                {
                    [nameof(ColorOverlayEffect.Color)] = gradient with { Reverse = reverse },
                },
            }];
            return LayerEffects.RenderClipped(source, LayerEffects.At(effects, frame, 5));
        }
        Assert.Equal(Red, At(0, false)[4, 2]);
        Assert.Equal(Red, At(1, false)[2, 0]);
        Assert.Equal(Red, At(1, true)[2, 4]);
        Assert.True(At(0, false).ContentEquals(At(4, false)));
        Assert.True(At(0, true).ContentEquals(At(4, true)));
    }

}
