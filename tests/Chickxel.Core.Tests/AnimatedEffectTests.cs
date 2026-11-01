using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class AnimatedEffectTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Black = Color32.Black;

    private static PixelImage Filled(int w, int h, Color32 c)
    {
        var img = new PixelImage(w, h);
        img.Clear(c);
        return img;
    }

    private static int Opaque(PixelImage img) => img.Pixels.Count(p => p.A != 0);

    /// <summary>효과를 범위 0~<paramref name="span"/>의 <paramref name="step"/>번째 프레임으로 그린 넓힌 그림과 원본 자리.</summary>
    private static (PixelImage Image, Rectangle Source) At(PixelImage src, AnimatedEffect effect, int step, int span)
    {
        var e = effect with { Frames = new FrameRange(0, span) };
        var (img, x, y) = LayerEffects.Render(src, LayerEffects.At([e], step, span + 1));
        return (img, new Rectangle(-x, -y, src.Width, src.Height));
    }

    private static bool SameAsSource(PixelImage src, (PixelImage Image, Rectangle Source) r)
        => r.Image.Crop(r.Source).ContentEquals(src) && Opaque(r.Image) == Opaque(src);

    // ───────────── 진행 ─────────────

    [Theory]
    [InlineData(EffectRepeat.Once, 1, 0.5, 0.5)]
    [InlineData(EffectRepeat.Loop, 2, 0.5, 1.0)] // 첫 바퀴 끝
    [InlineData(EffectRepeat.Loop, 2, 0.75, 0.5)]
    [InlineData(EffectRepeat.PingPong, 1, 0.5, 1.0)]
    [InlineData(EffectRepeat.PingPong, 1, 1.0, 0.0)] // 왕복은 원래대로 끝남
    [InlineData(EffectRepeat.Loop, 3, 0.0, 0.0)]
    public void Cycle_RepeatsWithinRange(EffectRepeat repeat, int count, double t, double expected)
        => Assert.Equal(expected, AnimatedEffect.Cycle(t, repeat, count), 9);

    [Fact]
    public void Time_IsFrameOffsetOverRangeLength_NullRangeUsesWholeAnimation()
    {
        var ranged = new ShineEffect { Frames = new FrameRange(2, 6) };
        Assert.Equal(0.5, ((AnimatedEffect)LayerEffects.At([ranged], 4, 10)[0]).Time);
        Assert.Empty(LayerEffects.At([ranged], 7, 10)); // 범위 밖은 빠짐
        Assert.Equal(1.0, ((AnimatedEffect)LayerEffects.At([new ShineEffect()], 4, 5)[0]).Time);
        Assert.Equal(0.0, ((AnimatedEffect)LayerEffects.At([new ShineEffect { Frames = new FrameRange(3, 3) }], 3, 5)[0]).Time);
    }

    // ───────────── 색 덮기 그라데이션: 전체 한 색(시간) ─────────────

    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);

    /// <summary>정지 효과를 범위 0~<paramref name="span"/>의 <paramref name="step"/>번째 프레임으로 그린 그림.</summary>
    private static PixelImage OverlayAt(PixelImage src, ColorOverlayEffect effect, int step, int span)
        => LayerEffects.RenderClipped(src, LayerEffects.At([effect with { Frames = new FrameRange(0, span) }], step, span + 1));

    private static ColorOverlayEffect TimeOverlay(EffectColorGradient gradient) => new()
    {
        ColorGradients = new Dictionary<string, EffectColorGradient> { [nameof(ColorOverlayEffect.Color)] = gradient },
    };

    [Fact]
    public void ColorOverlayUniform_SamplesStopsFromFirstToLastFrame()
    {
        var effect = TimeOverlay(new EffectColorGradient { Stops = [new(0, Red), new(1, Blue)],
            Shape = EffectGradientShape.Uniform, ColorSpace = GradientColorSpace.Rgb });
        var src = Filled(2, 2, Black.WithAlpha(77));

        Assert.True(effect.ColorGradients[nameof(ColorOverlayEffect.Color)].Animated);
        Assert.All(OverlayAt(src, effect, 0, 4).Pixels, pixel => Assert.Equal(Red.WithAlpha(77), pixel));
        Assert.All(OverlayAt(src, effect, 2, 4).Pixels, pixel => Assert.Equal(new Color32(128, 0, 128, 77), pixel));
        Assert.All(OverlayAt(src, effect, 4, 4).Pixels, pixel => Assert.Equal(Blue.WithAlpha(77), pixel));
    }

    [Theory]
    [InlineData(1, false, EffectEasing.Linear, false, 4)] // 한 번: 마지막 정지점에서 끝남
    [InlineData(1, false, EffectEasing.Linear, false, 3)]
    [InlineData(2, false, EffectEasing.Linear, false, 4)] // 반복: 바퀴 경계는 끝 색
    [InlineData(2, false, EffectEasing.Linear, false, 6)]
    [InlineData(1, true, EffectEasing.Linear, false, 4)] // 왕복: 가운데가 끝 색
    [InlineData(1, true, EffectEasing.Linear, false, 8)] // 왕복: 처음 색으로 끝남
    [InlineData(3, true, EffectEasing.EaseInOut, false, 5)]
    [InlineData(1, false, EffectEasing.EaseIn, false, 4)]
    [InlineData(1, false, EffectEasing.EaseOut, false, 2)]
    [InlineData(1, false, EffectEasing.Linear, true, 2)] // 역방향
    [InlineData(2, false, EffectEasing.EaseIn, true, 3)]
    public void ColorOverlayUniform_OneColorPerFrameAtEasedRepeatedProgress(int cycles, bool pingPong, EffectEasing easing, bool reverse, int step)
    {
        const int span = 8;
        var gradient = new EffectColorGradient
        {
            Stops = [new(0, Red), new(0.4, Green), new(1, Blue)],
            Shape = EffectGradientShape.Uniform, Cycles = cycles, PingPong = pingPong, Easing = easing, Reverse = reverse,
        };
        var repeat = pingPong ? EffectRepeat.PingPong : cycles == 1 ? EffectRepeat.Once : EffectRepeat.Loop;
        double progress = AnimatedEffect.Ease(AnimatedEffect.Cycle((double)step / span, repeat, cycles), easing);
        var expected = Gradient.Sample(gradient.Stops, gradient.AlphaStops, reverse ? 1 - progress : progress,
            gradient.Interpolation, colorSpace: gradient.ColorSpace);

        var pixels = OverlayAt(Filled(3, 2, Color32.White), TimeOverlay(gradient), step, span).Pixels;

        Assert.All(pixels, pixel => Assert.Equal(expected, pixel));
        if (!pingPong && cycles == 1 && step == span && !reverse) Assert.Equal(Blue, expected);
    }

    [Fact]
    public void ColorOverlayUniform_DitherUsesOnlyAdjacentStopColorsDuringTransition()
    {
        var effect = TimeOverlay(new EffectColorGradient
        {
            Stops = [new(0, Red), new(1, Blue)],
            Dither = GradientDither.Bayer2,
            Shape = EffectGradientShape.Uniform,
        });
        var pixels = OverlayAt(Filled(2, 2, Color32.White), effect, 1, 2).Pixels;

        Assert.All(pixels, pixel => Assert.True(pixel == Red || pixel == Blue, pixel.ToHex()));
        Assert.Equal(2, pixels.Count(pixel => pixel == Red));
        Assert.Equal(2, pixels.Count(pixel => pixel == Blue));
    }

    [Fact]
    public void ColorOverlayAngular_PingPongAndEasingReshapeTimeOnly()
    {
        var src = new PixelImage(5, 5);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++) src[x, y] = Color32.White;
        var angular = new EffectColorGradient
        {
            Stops = [new(0, Red), new(0.5, Green), new(1, Blue)], Shape = EffectGradientShape.Angular, Animated = true,
        };

        // Linear without ping-pong keeps the original rotation (half a turn at the middle frame).
        Assert.True(OverlayAt(src, TimeOverlay(angular), 2, 4).ContentEquals(OverlayAt(src, TimeOverlay(angular), 1, 2)));
        Assert.True(OverlayAt(src, TimeOverlay(angular), 0, 4).ContentEquals(OverlayAt(src, TimeOverlay(angular), 4, 4)));
        // Ping-pong reaches the full turn at the middle and comes back.
        var pingPong = angular with { PingPong = true };
        Assert.True(OverlayAt(src, TimeOverlay(pingPong), 1, 4).ContentEquals(OverlayAt(src, TimeOverlay(angular), 2, 4)));
        Assert.True(OverlayAt(src, TimeOverlay(pingPong), 3, 4).ContentEquals(OverlayAt(src, TimeOverlay(angular), 2, 4)));
        // Ease-in at the middle frame is a quarter turn.
        Assert.True(OverlayAt(src, TimeOverlay(angular with { Easing = EffectEasing.EaseIn }), 2, 4)
            .ContentEquals(OverlayAt(src, TimeOverlay(angular), 1, 4)));
    }

    [Fact]
    public void Load_OldGradientMap_MigratesToColorOverlayAndRendersSamePixels()
    {
        var p = new SpriteProject(3, 2);
        for (int i = 0; i < 4; i++) p.DuplicateFrame(0);
        p.SetLayerEffects(0, [new ShadowEffect()]);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        const string stops = """[{ "position": 0, "color": "#FF0000" }, { "position": 0.3, "color": "#00FF00" }, { "position": 1, "color": "#0000FF80" }]""";
        string[] oldJson =
        [
            $$"""
            { "type": "gradient", "enabled": true, "frames": [1, 4], "amount": 65, "blend": "Screen",
              "easing": "EaseIn", "repeat": "Loop", "repeatCount": 2, "mode": "ColorTransition",
              "gradient": { "interpolation": "Smooth", "dither": "None", "stops": {{stops}} } }
            """,
            $$"""
            { "type": "gradient", "enabled": false, "clipToPrevious": true, "amount": 40, "blend": "Multiply",
              "easing": "EaseOut", "repeat": "PingPong", "repeatCount": 3, "mode": "RadialRotation", "angle": -720,
              "gradient": { "dither": "Bayer2", "stops": {{stops}} } }
            """,
        ];
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            System.Text.Json.Nodes.JsonNode root;
            using (var reader = entry.Open()) root = System.Text.Json.Nodes.JsonNode.Parse(reader)!;
            var effects = root["layers"]!.AsArray()[0]!["effects"]!.AsArray();
            effects.Clear();
            foreach (var json in oldJson) effects.Add(System.Text.Json.Nodes.JsonNode.Parse(json));
            entry.Delete();
            using var writer = new System.Text.Json.Utf8JsonWriter(zip.CreateEntry(ProjectIO.ProjectEntryName).Open());
            root.WriteTo(writer);
        }
        stream.Position = 0;
        var loaded = ProjectIO.Load(stream).Layers[0].Effects;
        // 예전 그라데이션 맵을 C# 모델로 읽으면 색 덮기가 되고, 프로젝트로 열면 그 색 덮기의 공식 스크립트가 된다
        var stored = oldJson.Select(json => EffectCodec.ReadStored((System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(json)!)).ToArray();
        Assert.Equal(TestUtil.ConvertedJson(stored), TestUtil.EffectJson(loaded));

        var transition = Assert.IsType<ColorOverlayEffect>(stored[0]);
        Assert.Equal((true, new FrameRange(1, 4), 65, EffectBlend.Screen, Red),
            (transition.Enabled, transition.Frames!.Value, transition.Amount, transition.Blend, transition.Color));
        var uniform = Assert.Single(transition.ColorGradients).Value;
        Assert.Equal((EffectGradientShape.Uniform, true, 2, EffectEasing.EaseIn, false, false),
            (uniform.Shape, uniform.Animated, uniform.Cycles, uniform.Easing, uniform.PingPong, uniform.Reverse));

        var src = new PixelImage(3, 2);
        src.Clear(new Color32(30, 120, 200));
        src[1, 0] = new Color32(90, 10, 60, 77);
        src[2, 1] = new Color32(40, 50, 60, 0);
        int amount = (65 * 255 + 50) / 100;
        for (int frame = 1; frame <= 4; frame++)
        {
            // Old gradient map: progress = Ease(Cycle(Time, Repeat, RepeatCount), Easing), one color for every visible pixel.
            double progress = AnimatedEffect.Ease(AnimatedEffect.Cycle((frame - 1) / 3.0, EffectRepeat.Loop, 2), EffectEasing.EaseIn);
            var sampled = Gradient.Sample(uniform.Stops, uniform.AlphaStops, progress,
                GradientInterpolation.Smooth, colorSpace: uniform.ColorSpace);
            int strength = (amount * sampled.A + 127) / 255;
            var rendered = LayerEffects.RenderClipped(src, LayerEffects.At([transition], frame, 5));
            for (int i = 0; i < src.Pixels.Length; i++)
            {
                var expected = src.Pixels[i].A == 0 ? src.Pixels[i] : LayerEffects.Blend(src.Pixels[i], sampled, strength, EffectBlend.Screen);
                Assert.Equal(expected, rendered.Pixels[i]);
            }
            // 열린 스크립트 효과도 같은 그림
            Assert.True(LayerEffects.RenderClipped(src, LayerEffects.At([loaded[0]], frame, 5)).ContentEquals(rendered), $"frame {frame}");
        }

        var radial = Assert.IsType<ColorOverlayEffect>(stored[1]);
        Assert.Equal((false, true, (FrameRange?)null, 40, EffectBlend.Multiply),
            (radial.Enabled, radial.ClipToPrevious, radial.Frames, radial.Amount, radial.Blend));
        var angular = Assert.Single(radial.ColorGradients).Value;
        Assert.Equal((EffectGradientShape.Angular, true, 0, true, 1, EffectEasing.EaseOut, true, GradientDither.Bayer2),
            (angular.Shape, angular.Animated, angular.Angle, angular.Reverse, angular.Cycles, angular.Easing, angular.PingPong, angular.Dither));
        Assert.Equal(3, angular.Stops.Count);
    }

    [Fact]
    public void SaveLoad_ColorGradientEasingAndPingPong_OmittedWhenDefault()
    {
        var p = new SpriteProject(4, 4);
        var gradient = new EffectColorGradient { Stops = [new(0, Red), new(1, Blue)], Shape = EffectGradientShape.Uniform };
        p.SetLayerEffects(0, [TimeOverlay(gradient), TimeOverlay(gradient with { Easing = EffectEasing.EaseInOut, PingPong = true })]);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        var loaded = ProjectIO.Load(stream).Layers[0].Effects;
        // 열면 공식 스크립트 효과 — 그라데이션 슬롯은 스크립트 이름(Color → color)
        Assert.All(loaded, e => Assert.IsType<ScriptEffect>(e));
        Assert.Equal(gradient, loaded[0].ColorGradients[LegacyEffects.ScriptSlot(nameof(ColorOverlayEffect.Color))]);
        Assert.Equal(gradient with { Easing = EffectEasing.EaseInOut, PingPong = true }, loaded[1].ColorGradients[LegacyEffects.ScriptSlot(nameof(ColorOverlayEffect.Color))]);

        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        using var reader = zip.GetEntry(ProjectIO.ProjectEntryName)!.Open();
        var effects = System.Text.Json.Nodes.JsonNode.Parse(reader)!["layers"]!.AsArray()[0]!["effects"]!.AsArray();
        var plain = effects[0]!["colorGradients"]!["Color"]!.AsObject();
        Assert.False(plain.ContainsKey("easing"));
        Assert.False(plain.ContainsKey("pingPong"));
        Assert.Equal("Uniform", plain["shape"]!.GetValue<string>());
        var timed = effects[1]!["colorGradients"]!["Color"]!.AsObject();
        Assert.Equal("EaseInOut", timed["easing"]!.GetValue<string>());
        Assert.True(timed["pingPong"]!.GetValue<bool>());
    }

    // ───────────── 터지기 ─────────────

    private static readonly ExplodeEffect Burst = new() { DistanceX = 8, DistanceY = 8, SpreadX = 0, SpreadY = 0, Gravity = 0, Fade = false, Easing = EffectEasing.Linear };

    [Fact]
    public void Explode_FirstFrameIsOriginal()
    {
        var src = Filled(6, 6, Red);
        Assert.True(SameAsSource(src, At(src, new ExplodeEffect { SpreadX = 100, SpreadY = 100, Gravity = 9, PieceSize = 2 }, 0, 5)));
    }

    [Fact]
    public void Explode_MidRange_PiecesMoveOutwardKeepingCount()
    {
        var src = Filled(6, 6, Red);
        var (img, rect) = At(src, Burst, 3, 6);
        int count = Opaque(img);
        Assert.InRange(count, 30, 36); // 겹친 조각만큼 조금 줄 수 있음, 흐려지지 않음
        Assert.All(img.Pixels.Where(p => p.A != 0), p => Assert.Equal(Red, p));
        double cx = rect.X + 3, cy = rect.Y + 3;
        double Spread(PixelImage im, Rectangle? within = null)
        {
            var d = new List<double>();
            for (int y = 0; y < im.Height; y++)
                for (int x = 0; x < im.Width; x++)
                    if (im.Get(x, y).A != 0) d.Add(Math.Sqrt(Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2)));
            return d.Average();
        }
        var original = new PixelImage(img.Width, img.Height);
        original.Blit(src, rect.X, rect.Y);
        Assert.True(Spread(img) > Spread(original) + 2);
        // 모서리 조각은 원래 그림 밖으로 나갔다
        Assert.Contains(Enumerable.Range(0, img.Width * img.Height), i => img.Pixels[i].A != 0 && !rect.Contains(i % img.Width, i / img.Width));
    }

    [Fact]
    public void Explode_EndWithFadeIsTransparent()
    {
        var src = Filled(6, 6, Red);
        var (img, _) = At(src, new ExplodeEffect { Fade = true, FadeStart = 50 }, 6, 6);
        Assert.Equal(0, Opaque(img));
        var (mid, _) = At(src, new ExplodeEffect { Fade = true, FadeStart = 0, Easing = EffectEasing.Linear, SpreadX = 0, SpreadY = 0 }, 3, 6);
        Assert.All(mid.Pixels.Where(p => p.A != 0), p => Assert.InRange((int)p.A, 1, 254));
    }

    [Fact]
    public void Explode_IsDeterministicPerSeed()
    {
        var src = Filled(8, 8, Red);
        var fx = new ExplodeEffect { SpreadX = 100, SpreadY = 100, Seed = 7 };
        Assert.True(At(src, fx, 2, 5).Image.ContentEquals(At(src, fx, 2, 5).Image));
        Assert.False(At(src, fx, 2, 5).Image.ContentEquals(At(src, fx with { Seed = 8 }, 2, 5).Image));
    }

    [Fact]
    public void Explode_SpreadAxesAffectOnlyTheirOwnDisplacement()
    {
        var src = Filled(1, 1, Red);
        bool changedX = false, changedY = false;
        for (int seed = 1; seed <= 32; seed++)
        {
            (int X, int Y) Position(int horizontal, int vertical)
            {
                var image = At(src, new ExplodeEffect
                {
                    DistanceX = 32, DistanceY = 32, SpreadX = horizontal, SpreadY = vertical, Seed = seed,
                    Gravity = 0, Fade = false, Easing = EffectEasing.Linear,
                }, 2, 2).Image;
                int index = Array.FindIndex(image.Pixels, pixel => pixel.A != 0);
                Assert.True(index >= 0);
                return (index % image.Width, index / image.Width);
            }
            var baseline = Position(0, 0);
            var horizontal = Position(100, 0);
            var vertical = Position(0, 100);
            Assert.Equal(baseline.Y, horizontal.Y);
            Assert.Equal(baseline.X, vertical.X);
            changedX |= baseline.X != horizontal.X;
            changedY |= baseline.Y != vertical.Y;
        }
        Assert.True(changedX && changedY);
    }

    [Fact]
    public void Explode_ZeroDistanceAxisStaysPut_AndGravityRemainsIndependent()
    {
        var src = Filled(1, 1, Red);
        bool movedX = false, movedY = false;
        for (int seed = 1; seed <= 32; seed++)
        {
            foreach (bool horizontal in new[] { true, false })
            {
                var effect = new ExplodeEffect
                {
                    DistanceX = horizontal ? 32 : 0, DistanceY = horizontal ? 0 : 32,
                    SpreadX = 100, SpreadY = 100, Gravity = 0, Fade = false,
                    Seed = seed, Easing = EffectEasing.Linear,
                };
                var (image, source) = At(src, effect, 2, 2);
                int index = Array.FindIndex(image.Pixels, pixel => pixel.A != 0);
                Assert.True(index >= 0);
                int x = index % image.Width, y = index / image.Width;
                if (horizontal)
                {
                    Assert.Equal(source.Y, y);
                    movedX |= x != source.X;
                    Assert.Equal(new EffectPadding(32, 0, 32, 0), LayerEffects.Padding([effect], 1, 1));
                }
                else
                {
                    Assert.Equal(source.X, x);
                    movedY |= y != source.Y;
                    Assert.Equal(new EffectPadding(0, 32, 0, 32), LayerEffects.Padding([effect], 1, 1));
                }
            }
        }
        Assert.True(movedX && movedY);
        var gravityOnly = new ExplodeEffect { DistanceX = 0, DistanceY = 0, Gravity = 6, Fade = false, Easing = EffectEasing.Linear };
        Assert.Equal(new EffectPadding(0, 0, 0, 6), LayerEffects.Padding([gravityOnly], 1, 1));
        var (falling, start) = At(src, gravityOnly, 2, 2);
        Assert.Equal(Red, falling.Get(start.X, start.Y + 6));
        var still = gravityOnly with { Gravity = 0 };
        Assert.Equal(new EffectPadding(0, 0, 0, 0), LayerEffects.Padding([still], 1, 1));
        Assert.True(SameAsSource(src, At(src, still, 2, 2)));
    }

    [Fact]
    public void Explode_KeepOriginalLeavesPictureInPlace()
    {
        var src = Filled(6, 6, Red);
        var (img, rect) = At(src, Burst with { KeepOriginal = true }, 3, 6);
        Assert.True(img.Crop(rect).ContentEquals(src));
        Assert.True(Opaque(img) > 36);
        var (gone, r2) = At(src, Burst with { Fade = true }, 6, 6);
        Assert.Equal(0, Opaque(gone));
        var (kept, r3) = At(src, Burst with { Fade = true, KeepOriginal = true }, 6, 6);
        Assert.True(kept.Crop(r3).ContentEquals(src));
    }

    [Fact]
    public void Explode_PartsAtlasGrowsByDistanceAndGravity_CanvasClips()
    {
        var p = new SpriteProject(8, 8);
        var g = p.AddGroup("몸");
        var part = p.AddPart(g, "머리", 2, 2, new Point(3, 3));
        p.SetCel(p.IndexOf(part), 0, Filled(2, 2, Red));
        p.SetLayerEffects(p.IndexOf(part), [new ExplodeEffect { DistanceX = 5, DistanceY = 5, Gravity = 3 }]);
        Assert.Equal(new EffectPadding(5, 5, 5, 8), LayerEffects.Padding(part.Effects, 2, 2));
        var parts = ProjectExport.BuildPartsSheet(p, "p");
        Assert.Equal(new Size(12, 15), parts.Entries.Single(e => ReferenceEquals(e.Part, part)).Rect.Size);
        Assert.Equal(new Size(8, 8), p.Flatten(0).Size);
    }

    // ───────────── 빛 지나가기 ─────────────

    [Fact]
    public void Shine_NoChangeOutsideBand_OnlyOpaquePixelsChange()
    {
        var src = new PixelImage(8, 8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                if ((x + y) % 3 != 0) src[x, y] = new Color32(40, 60, 80);
        var shine = new ShineEffect { Angle = 45, Width = 2, Softness = 1 };
        Assert.True(SameAsSource(src, At(src, shine, 0, 8)));
        Assert.True(SameAsSource(src, At(src, shine, 8, 8)));
        var (mid, _) = At(src, shine, 4, 8);
        int changed = 0;
        for (int i = 0; i < src.Pixels.Length; i++)
        {
            Assert.Equal(src.Pixels[i].A, mid.Pixels[i].A);
            if (src.Pixels[i].A == 0) Assert.Equal(Color32.Transparent, mid.Pixels[i]);
            else if (mid.Pixels[i] != src.Pixels[i]) changed++;
        }
        Assert.InRange(changed, 1, 63);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(180)]
    public void Shine_BandMovesAlongAngle(int angle)
    {
        var src = Filled(10, 10, new Color32(20, 20, 20));
        var shine = new ShineEffect { Angle = angle, Width = 2 };
        double a = angle * Math.PI / 180, dx = Math.Cos(a), dy = -Math.Sin(a);
        double Position(int step)
        {
            var (img, _) = At(src, shine, step, 10);
            var hit = Enumerable.Range(0, 100).Where(i => img.Pixels[i] != src.Pixels[i]).ToList();
            Assert.NotEmpty(hit);
            return hit.Average(i => (i % 10 + 0.5) * dx + (i / 10 + 0.5) * dy);
        }
        double p3 = Position(3), p5 = Position(5), p7 = Position(7);
        Assert.True(p3 < p5 && p5 < p7);
    }

    // ───────────── 늘었다 줄기 ─────────────

    [Fact]
    public void Squash_StartAndEndAreOriginal()
    {
        var src = Filled(6, 8, Red);
        var squash = new SquashEffect { WidthAmount = 30, HeightAmount = 30, Bounces = 3 };
        Assert.True(SameAsSource(src, At(src, squash, 0, 7)));
        Assert.True(SameAsSource(src, At(src, squash, 7, 7)));
    }

    [Fact]
    public void Squash_MidSquashIsWiderAndShorter_BottomRowStays()
    {
        var src = Filled(6, 10, Red);
        var (img, r) = At(src, new SquashEffect { WidthAmount = 20, HeightAmount = 20, Bounces = 1 }, 2, 10); // 첫 봉우리 근처 = 옆으로 넓게
        int Row(int y) => Enumerable.Range(0, img.Width).Count(x => img.Get(x, y).A != 0);
        int bottom = r.Bottom - 1;
        Assert.True(Row(bottom) > 6);
        Assert.Equal(0, Row(bottom + 1)); // 발은 그 자리
        Assert.Equal(0, Row(r.Top)); // 낮아짐
        // 위로 먼저면 반대: 좁고 높게
        var (tall, r2) = At(src, new SquashEffect { WidthAmount = 20, HeightAmount = 20, Bounces = 1, StretchFirst = true }, 2, 10);
        Assert.True(Enumerable.Range(0, tall.Width).Count(x => tall.Get(x, r2.Bottom - 1).A != 0) < 6);
        Assert.True(Enumerable.Range(0, tall.Width).Count(x => tall.Get(x, r2.Top - 1).A != 0) > 0);
    }

    [Fact]
    public void Squash_SeparateAxisAmountsKeepZeroAxisFixed()
    {
        var src = Filled(10, 10, Red);
        var (wide, wideSource) = At(src, new SquashEffect { WidthAmount = 40, HeightAmount = 0, Bounces = 1 }, 2, 10);
        var (tall, tallSource) = At(src, new SquashEffect { WidthAmount = 0, HeightAmount = 40, Bounces = 1, StretchFirst = true }, 2, 10);
        Assert.True(wide.GetOpaqueBounds().Width > src.Width);
        Assert.Equal(src.Height, wide.GetOpaqueBounds().Height);
        Assert.Equal(wideSource.Y, wide.GetOpaqueBounds().Y);
        Assert.Equal(src.Width, tall.GetOpaqueBounds().Width);
        Assert.True(tall.GetOpaqueBounds().Height > src.Height);
        Assert.Equal(tallSource.X, tall.GetOpaqueBounds().X);
    }

    // ───────────── 저장 ─────────────

    [Fact]
    public void SaveLoad_RoundTripsAnimatedEffects_MissingFieldsAreDefaults()
    {
        var p = new SpriteProject(8, 8);
        p.DuplicateFrame(0);
        LayerEffect[] fx =
        [
            new ExplodeEffect
            {
                DistanceX = 30, DistanceY = 0, Center = ExplodeCenter.Custom, CenterX = 3, CenterY = -2, SpreadX = 77, SpreadY = 12, Gravity = -5, PieceSize = 3, Fade = false, FadeStart = 10,
                Shrink = true, Seed = 1234, KeepOriginal = true, FlashFrames = 2, FlashColor = new Color32(1, 2, 3), Easing = EffectEasing.EaseInOut,
                Repeat = EffectRepeat.PingPong, RepeatCount = 3, Frames = new FrameRange(0, 0),
            },
            new ShineEffect { Angle = 300, Width = 5, Color = new Color32(255, 240, 0), Strength = 55, Softness = 2, Repeat = EffectRepeat.Loop, RepeatCount = 2, Enabled = false },
            new SquashEffect { WidthAmount = 35, HeightAmount = 15, Anchor = SquashAnchor.Pivot, StretchFirst = true, Bounces = 3, Easing = EffectEasing.EaseOut },
        ];
        p.SetLayerEffects(0, fx);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        // 열면 같은 설정의 공식 스크립트 효과(그라데이션 사전은 참조로 비교되므로 저장 JSON으로)
        Assert.Equal(TestUtil.ConvertedJson(fx), TestUtil.EffectJson(ProjectIO.Load(stream).Layers[0].Effects));

        // 설정이 빠진 효과 = 그 종류의 기본값
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            System.Text.Json.Nodes.JsonNode root;
            using (var r = entry.Open()) root = System.Text.Json.Nodes.JsonNode.Parse(r)!;
            var effects = root["layers"]!.AsArray()[0]!["effects"]!.AsArray();
            effects[0] = new System.Text.Json.Nodes.JsonObject { ["type"] = "explode", ["center"] = "nowhere" };
            effects[1] = new System.Text.Json.Nodes.JsonObject { ["type"] = "shine" };
            effects[2] = new System.Text.Json.Nodes.JsonObject { ["type"] = "squash" };
            entry.Delete();
            using var w = zip.CreateEntry(ProjectIO.ProjectEntryName).Open();
            using var writer = new System.Text.Json.Utf8JsonWriter(w);
            root.WriteTo(writer);
        }
        stream.Position = 0;
        Assert.Equal(TestUtil.ConvertedJson([new ExplodeEffect(), new ShineEffect(), new SquashEffect()]), TestUtil.EffectJson(ProjectIO.Load(stream).Layers[0].Effects));

        // 이전 프로젝트의 공통 세기는 두 축에 동일하게 적용한다.
        var oldSquash = new System.Text.Json.Nodes.JsonObject { ["type"] = "squash", ["amount"] = 35 };
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            System.Text.Json.Nodes.JsonNode root;
            using (var r = entry.Open()) root = System.Text.Json.Nodes.JsonNode.Parse(r)!;
            root["layers"]!.AsArray()[0]!["effects"]!.AsArray()[2] = oldSquash.DeepClone();
            entry.Delete();
            using var w = zip.CreateEntry(ProjectIO.ProjectEntryName).Open();
            using var writer = new System.Text.Json.Utf8JsonWriter(w);
            root.WriteTo(writer);
        }
        stream.Position = 0;
        var legacy = Assert.IsType<SquashEffect>(EffectCodec.ReadStored(oldSquash));
        Assert.Equal((35, 35), (legacy.WidthAmount, legacy.HeightAmount));
        Assert.Equal(TestUtil.ConvertedJson([legacy]), TestUtil.EffectJson([ProjectIO.Load(stream).Layers[0].Effects[2]]));
    }

    [Fact]
    public void Explode_LegacyCommonDistanceAndSpreadLoadIntoBothAxes()
    {
        var p = new SpriteProject(8, 8);
        var expected = new ExplodeEffect { DistanceX = 27, DistanceY = 27, SpreadX = 73, SpreadY = 73, Fade = false, Seed = 9 };
        p.SetLayerEffects(0, [expected]);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        System.Text.Json.Nodes.JsonObject oldExplode;
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            System.Text.Json.Nodes.JsonNode root;
            using (var r = entry.Open()) root = System.Text.Json.Nodes.JsonNode.Parse(r)!;
            var explode = root["layers"]!.AsArray()[0]!["effects"]!.AsArray()[0]!.AsObject();
            oldExplode = explode;
            Assert.False(explode.ContainsKey("distance"));
            explode.Remove("distanceX");
            explode.Remove("distanceY");
            explode["distance"] = 27;
            explode.Remove("spreadX");
            explode.Remove("spreadY");
            explode["spread"] = 73;
            entry.Delete();
            using var w = zip.CreateEntry(ProjectIO.ProjectEntryName).Open();
            using var writer = new System.Text.Json.Utf8JsonWriter(w);
            root.WriteTo(writer);
        }
        stream.Position = 0;
        // 예전 공통 거리·퍼짐은 C# 모델로 읽을 때 두 축에 같게 들어가고, 프로젝트로 열면 그 효과의 공식 스크립트(같은 그림)
        var stored = Assert.IsType<ExplodeEffect>(EffectCodec.ReadStored(oldExplode));
        Assert.Equal(expected, stored);
        var loaded = Assert.IsType<ScriptEffect>(ProjectIO.Load(stream).Layers[0].Effects[0]);
        Assert.Equal(TestUtil.ConvertedJson([expected]), TestUtil.EffectJson([loaded]));
        var src = Filled(6, 6, Red);
        var want = At(src, expected, 2, 4).Image;
        Assert.True(want.ContentEquals(At(src, stored, 2, 4).Image));
        var e = loaded with { Frames = new FrameRange(0, 4) };
        Assert.True(want.ContentEquals(LayerEffects.Render(src, LayerEffects.At([e], 2, 5)).Image));
    }

    // ───────────── 영구 적용 ─────────────

    [Fact]
    public void Bake_Outline_EqualsRenderedCel_RemovesEffect_UndoRestoresBoth()
    {
        var p = new SpriteProject(6, 6);
        var cel = new PixelImage(6, 6);
        cel[2, 2] = Red;
        cel[3, 2] = Red;
        p.SetCel(0, 0, cel);
        var outline = new OutlineEffect { Color = Black, Diagonal = true };
        p.SetLayerEffects(0, [outline]);
        int steps = p.History.UndoCount;
        var expected = LayerEffects.RenderClipped(cel, [outline]);

        var (baked, frames, count, grown) = p.BakeLayerEffects(0, [0]);
        Assert.Equal([outline], baked);
        Assert.Null(frames);
        Assert.Equal((1, false), (count, grown));
        Assert.True(p.GetCel(0, 0)!.ContentEquals(expected));
        Assert.Empty(p.Layers[0].Effects);
        Assert.Equal(steps + 1, p.History.UndoCount);

        Assert.True(p.Undo());
        Assert.True(p.GetCel(0, 0)!.ContentEquals(cel));
        Assert.Equal([outline], p.Layers[0].Effects);
    }

    [Fact]
    public void Bake_RangeTouchesOnlyItsFrames_IncludesEffectsAbove_KeepsBelow()
    {
        var p = new SpriteProject(6, 6);
        var cel = new PixelImage(6, 6);
        cel[2, 2] = Red;
        p.SetCel(0, 0, cel);
        for (int i = 0; i < 3; i++) p.DuplicateFrame(i);
        var overlay = new ColorOverlayEffect { Color = Color32.White, Amount = 100 };
        var outline = new OutlineEffect { Color = Black, Frames = new FrameRange(1, 2) };
        var shadow = new ShadowEffect { Color = Black };
        p.SetLayerEffects(0, [overlay, outline, shadow]);
        Assert.Equal([0, 1], LayerEffects.BakeSet(p.Layers[0].Effects, [1]));
        var before = Enumerable.Range(0, 4).Select(f => p.Flatten(f)).ToList();

        var (_, frames, count, _) = p.BakeLayerEffects(0, [1]);
        Assert.Null(frames); // 위의 색 덮기(모든 프레임)까지 구움
        Assert.Equal(4, count);
        Assert.Equal([shadow], p.Layers[0].Effects);
        for (int f = 0; f < 4; f++) Assert.True(p.Flatten(f).ContentEquals(before[f])); // 보이던 그대로

        // 범위 효과 하나만: 범위 밖 프레임 셀은 그대로(같은 참조)
        p.Undo();
        p.SetLayerEffects(0, [outline]);
        var untouched = new[] { p.GetCel(0, 0), p.GetCel(0, 3) };
        var (_, range, n, _) = p.BakeLayerEffects(0, [0]);
        Assert.Equal((new FrameRange(1, 2), 2), (range!.Value, n));
        Assert.Same(untouched[0], p.GetCel(0, 0));
        Assert.Equal(4, p.GetCel(0, 1)!.Pixels.Count(c => c == Black));
    }

    [Fact]
    public void Bake_PartGrowsToFitBleed_KeepsCanvasPositionAndPivot()
    {
        var p = new SpriteProject(10, 10);
        var g = p.AddGroup("몸");
        var part = p.AddPart(g, "머리", 2, 2, new Point(4, 4));
        int pi = p.IndexOf(part);
        p.SetCel(pi, 0, Filled(2, 2, Red));
        p.SetLayerEffects(pi, [new ShadowEffect { Color = Black, OffsetX = 1, OffsetY = 1 }]);
        var before = p.Flatten(0);
        var (px, py) = part.PartPivot.ToLocalPixel(2, 2);

        var (_, _, _, grown) = p.BakeLayerEffects(pi, [0]);
        Assert.True(grown);
        Assert.Equal(new Rectangle(4, 4, 3, 3), part.PartRect);
        Assert.True(p.Flatten(0).ContentEquals(before));
        var (qx, qy) = part.PartPivot.ToLocalPixel(3, 3);
        Assert.Equal(px, qx, 4);
        Assert.Equal(py, qy, 4);
        Assert.Throws<InvalidOperationException>(() => p.BakeLayerEffects(p.IndexOf(g), [0]));
    }
}
