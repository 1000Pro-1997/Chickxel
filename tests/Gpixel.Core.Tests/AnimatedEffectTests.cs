using System.Drawing;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

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

    // ───────────── 시간 그라데이션 ─────────────

    [Fact]
    public void GradientChange_SamplesStopsFromFirstToLastFrame()
    {
        var blue = new Color32(0, 0, 255);
        var effect = new GradientOverlayEffect
        {
            Gradient = new EffectColorGradient { Stops = [new(0, Red), new(1, blue)] },
            Easing = EffectEasing.Linear,
        };
        var src = Filled(2, 2, Black.WithAlpha(77));

        Assert.All(At(src, effect, 0, 4).Image.Pixels, pixel => Assert.Equal(Red.WithAlpha(77), pixel));
        Assert.All(At(src, effect, 2, 4).Image.Pixels, pixel => Assert.Equal(new Color32(128, 0, 128, 77), pixel));
        Assert.All(At(src, effect, 4, 4).Image.Pixels, pixel => Assert.Equal(blue.WithAlpha(77), pixel));
    }

    [Fact]
    public void GradientChange_DitherUsesOnlyAdjacentStopColorsDuringTransition()
    {
        var blue = new Color32(0, 0, 255);
        var effect = new GradientOverlayEffect
        {
            Gradient = new EffectColorGradient
            {
                Stops = [new(0, Red), new(1, blue)],
                Dither = GradientDither.Bayer2,
            },
        };
        var pixels = At(Filled(2, 2, Color32.White), effect, 1, 2).Image.Pixels;

        Assert.All(pixels, pixel => Assert.True(pixel == Red || pixel == blue, pixel.ToHex()));
        Assert.Equal(2, pixels.Count(pixel => pixel == Red));
        Assert.Equal(2, pixels.Count(pixel => pixel == blue));
    }

    [Fact]
    public void GradientChange_RadialRotatesAroundOpaqueBoundsAndLoopsWithoutTouchingTransparentPixels()
    {
        var blue = new Color32(0, 0, 255);
        var src = new PixelImage(9, 7);
        for (int y = 1; y <= 3; y++)
            for (int x = 4; x <= 6; x++)
                src[x, y] = Black.WithAlpha(77);
        var transparent = new Color32(40, 50, 60, 0);
        src[0, 0] = transparent;
        var effect = new GradientOverlayEffect
        {
            Mode = GradientChangeMode.RadialRotation,
            Gradient = new EffectColorGradient { Stops = [new(0, Red), new(1, blue)] },
        };

        var first = At(src, effect, 0, 4).Image;
        var quarter = At(src, effect, 1, 4).Image;
        var last = At(src, effect, 4, 4).Image;
        Assert.Equal(Red.WithAlpha(77), first[6, 2]); // right of opaque center (5.5, 2.5)
        Assert.Equal(new Color32(191, 0, 64, 77), first[5, 1]); // above center
        Assert.Equal(new Color32(128, 0, 128, 77), first[4, 2]); // left of center
        Assert.Equal(new Color32(64, 0, 191, 77), first[5, 3]); // below center
        Assert.Equal(first[6, 2], quarter[5, 1]); // one quarter-turn counterclockwise
        Assert.NotEqual(first[6, 2], quarter[6, 2]);
        Assert.True(first.ContentEquals(last));
        Assert.Equal(transparent, first[0, 0]);
        Assert.Equal(transparent, quarter[0, 0]);
        Assert.Equal(transparent, last[0, 0]);
    }

    [Theory]
    [InlineData(180, 2, 1)]
    [InlineData(-180, 2, 3)]
    public void GradientChange_RadialSignedAngleControlsDirectionAndPartialTurn(int angle, int targetX, int targetY)
    {
        var source = new PixelImage(5, 5);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++) source[x, y] = Color32.White;
        var effect = new GradientOverlayEffect
        {
            Mode = GradientChangeMode.RadialRotation,
            Angle = angle,
            Gradient = new EffectColorGradient { Stops = [new(0, Red), new(1, Color32.Black)] },
        };
        var start = At(source, effect, 0, 4).Image;
        var middle = At(source, effect, 2, 4).Image;
        var end = At(source, effect, 4, 4).Image;
        Assert.Equal(start[3, 2], middle[targetX, targetY]);
        Assert.NotEqual(start[3, 2], end[3, 2]);
        Assert.True(start.ContentEquals(At(source, effect with { Angle = 0 }, 4, 4).Image));
        Assert.True(start.ContentEquals(At(source, effect with { Angle = 720 }, 4, 4).Image));
    }

    // ───────────── 터지기 ─────────────

    private static readonly ExplodeEffect Burst = new() { Distance = 8, Spread = 0, Gravity = 0, Fade = false, Easing = EffectEasing.Linear };

    [Fact]
    public void Explode_FirstFrameIsOriginal()
    {
        var src = Filled(6, 6, Red);
        Assert.True(SameAsSource(src, At(src, new ExplodeEffect { Spread = 100, Gravity = 9, PieceSize = 2 }, 0, 5)));
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
        var (mid, _) = At(src, new ExplodeEffect { Fade = true, FadeStart = 0, Easing = EffectEasing.Linear, Spread = 0 }, 3, 6);
        Assert.All(mid.Pixels.Where(p => p.A != 0), p => Assert.InRange((int)p.A, 1, 254));
    }

    [Fact]
    public void Explode_IsDeterministicPerSeed()
    {
        var src = Filled(8, 8, Red);
        var fx = new ExplodeEffect { Spread = 100, Seed = 7 };
        Assert.True(At(src, fx, 2, 5).Image.ContentEquals(At(src, fx, 2, 5).Image));
        Assert.False(At(src, fx, 2, 5).Image.ContentEquals(At(src, fx with { Seed = 8 }, 2, 5).Image));
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
        p.SetLayerEffects(p.IndexOf(part), [new ExplodeEffect { Distance = 5, Gravity = 3 }]);
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
                Distance = 30, Center = ExplodeCenter.Custom, CenterX = 3, CenterY = -2, Spread = 77, Gravity = -5, PieceSize = 3, Fade = false, FadeStart = 10,
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
        Assert.Equal(fx, ProjectIO.Load(stream).Layers[0].Effects);

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
        Assert.Equal([new ExplodeEffect(), new ShineEffect(), new SquashEffect()], ProjectIO.Load(stream).Layers[0].Effects);

        // 이전 프로젝트의 공통 세기는 두 축에 동일하게 적용한다.
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            System.Text.Json.Nodes.JsonNode root;
            using (var r = entry.Open()) root = System.Text.Json.Nodes.JsonNode.Parse(r)!;
            root["layers"]!.AsArray()[0]!["effects"]!.AsArray()[2] = new System.Text.Json.Nodes.JsonObject { ["type"] = "squash", ["amount"] = 35 };
            entry.Delete();
            using var w = zip.CreateEntry(ProjectIO.ProjectEntryName).Open();
            using var writer = new System.Text.Json.Utf8JsonWriter(w);
            root.WriteTo(writer);
        }
        stream.Position = 0;
        var legacy = (SquashEffect)ProjectIO.Load(stream).Layers[0].Effects[2];
        Assert.Equal((35, 35), (legacy.WidthAmount, legacy.HeightAmount));
    }

    [Theory]
    [InlineData(-180)]
    [InlineData(0)]
    public void SaveLoad_GradientChangeMode_RoundTripsAndMissingModeUsesColorTransition(int angle)
    {
        var p = new SpriteProject(4, 4);
        var radial = new GradientOverlayEffect
        {
            Mode = GradientChangeMode.RadialRotation,
            Angle = angle,
            Gradient = new EffectColorGradient
            {
                Stops = [new(0, Red), new(0.5, new Color32(0, 255, 0)), new(1, Color32.Black)],
                Dither = GradientDither.Bayer4,
            },
            Strength = 65,
            Blend = EffectBlend.Screen,
        };
        p.SetLayerEffects(0, [radial]);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        Assert.Equal(radial, ProjectIO.Load(stream).Layers[0].Effects[0]);

        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            System.Text.Json.Nodes.JsonNode root;
            using (var reader = entry.Open()) root = System.Text.Json.Nodes.JsonNode.Parse(reader)!;
            var saved = root["layers"]!.AsArray()[0]!["effects"]!.AsArray()[0]!.AsObject();
            Assert.Equal("RadialRotation", saved["mode"]!.GetValue<string>());
            Assert.Equal(angle, saved["angle"]!.GetValue<int>());
            saved.Remove("mode");
            saved.Remove("angle");
            entry.Delete();
            using var writer = new System.Text.Json.Utf8JsonWriter(zip.CreateEntry(ProjectIO.ProjectEntryName).Open());
            root.WriteTo(writer);
        }
        stream.Position = 0;
        Assert.Equal(radial with { Mode = GradientChangeMode.ColorTransition, Angle = 360 }, ProjectIO.Load(stream).Layers[0].Effects[0]);
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
