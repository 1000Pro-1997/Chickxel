using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class StaticEffectTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Green = new(0, 255, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Black = Color32.Black;

    private static PixelImage Filled(int w, int h, Color32 c)
    {
        var img = new PixelImage(w, h);
        img.Clear(c);
        return img;
    }

    // ───────────── 색 덮기 공간 그라데이션 ─────────────

    [Fact]
    public void ColorOverlayGradient_OnlyOpaquePixels_TopIsFirstStop_BottomIsLastStop_AlphaKept()
    {
        var src = new PixelImage(5, 6);
        for (int y = 1; y < 6; y++)
            for (int x = 1; x < 4; x++)
                src[x, y] = Color32.White;
        src[2, 3] = Color32.White.WithAlpha(100);
        var g = new ColorOverlayEffect
        {
            ColorGradients = new Dictionary<string, EffectColorGradient>
            {
                [nameof(ColorOverlayEffect.Color)] = new() { Stops = [new(0, Red), new(1, Blue)], Angle = 270 },
            },
        };

        var (img, ox, oy) = LayerEffects.Render(src, [g]);
        Assert.Equal((0, 0, src.Size), (ox, oy, img.Size));
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 5; x++)
                Assert.Equal(src[x, y].A, img[x, y].A); // 투명한 곳은 그대로 투명, 알파는 그대로
        Assert.Equal(Red, img[1, 1]); // 보이는 영역 맨 위 = A
        Assert.Equal(Blue, img[3, 5]); // 맨 아래 = B
        Assert.True(img[2, 2].R > img[2, 4].R && img[2, 2].B < img[2, 4].B); // 위 → 아래로 A에서 B로
        Assert.Equal(Color32.Transparent, img[0, 0]);
        Assert.Equal(Color32.Transparent, img[4, 5]);
    }

    [Theory]
    [InlineData(GradientDither.Bayer2)]
    [InlineData(GradientDither.Bayer4)]
    public void ColorOverlayGradient_BayerDither_UsesOnlyTheTwoColors(GradientDither dither)
    {
        var src = Filled(8, 8, new Color32(10, 200, 30));
        var effect = new ColorOverlayEffect
        {
            ColorGradients = new Dictionary<string, EffectColorGradient>
            {
                [nameof(ColorOverlayEffect.Color)] = new() { Stops = [new(0, Red), new(1, Blue)], Angle = 270, Dither = dither },
            },
        };
        var img = LayerEffects.RenderClipped(src, [effect]);
        Assert.All(img.Pixels, pixel => Assert.True(pixel == Red || pixel == Blue, pixel.ToHex()));
        Assert.All(Enumerable.Range(0, 8), x => Assert.Equal(Red, img[x, 0])); // 첫 줄은 모두 A
        Assert.All(Enumerable.Range(0, 8), x => Assert.Equal(Blue, img[x, 7])); // 끝 줄은 모두 B
        int Blues(int y0, int y1) => Enumerable.Range(y0, y1 - y0).Sum(y => Enumerable.Range(0, 8).Count(x => img[x, y] == Blue));
        Assert.True(Blues(0, 4) < Blues(4, 8)); // 아래쪽 절반에 B가 더 많다
    }

    // ───────────── 안쪽 빛 ─────────────

    [Fact]
    public void InnerGlow_PaintsEdgeBandOnly_FadesInward()
    {
        var src = Filled(7, 7, Black);
        var img = LayerEffects.RenderClipped(src, [new InnerGlowEffect { Color = Color32.White, Size = 2, Strength = 100, Blend = EffectBlend.Normal }]);
        Assert.Equal(Color32.White, img[0, 3]); // 가장자리 1px = 100%
        Assert.Equal(new Color32(127, 127, 127), img[1, 3]); // 2px째 = 절반(127/255)
        Assert.Equal(Black, img[3, 3]); // 그보다 안쪽은 그대로
    }

    // ───────────── 색 바꾸기 ─────────────

    [Fact]
    public void PaletteSwap_MapsExactColors_LeavesOthers_NoChaining()
    {
        var src = new PixelImage(5, 1);
        src[0, 0] = Red.WithAlpha(128);
        src[1, 0] = Blue;
        src[2, 0] = new Color32(254, 0, 0);
        src[3, 0] = Green;
        var swap = new PaletteSwapEffect { Pairs = [new ColorSwap(Red, Blue), new ColorSwap(Blue, Red)] };

        var img = LayerEffects.RenderClipped(src, [swap]);
        Assert.Equal(Blue.WithAlpha(128), img[0, 0]); // 알파는 그대로
        Assert.Equal(Red, img[1, 0]); // 맞바꾸기(바뀐 색은 다시 바뀌지 않음)
        Assert.Equal(new Color32(254, 0, 0), img[2, 0]); // 정확히 같지 않으면 그대로
        Assert.Equal(Green, img[3, 0]);
        Assert.Equal(Color32.Transparent, img[4, 0]);

        var loose = LayerEffects.RenderClipped(src, [swap with { Tolerance = 1 }]);
        Assert.Equal(Blue, loose[2, 0]);
        Assert.Equal(Green, loose[3, 0]);
    }

    // ───────────── 색조 보정 ─────────────

    [Fact]
    public void ColorAdjust_Grayscale_GivesEqualChannels_KeepsAlpha()
    {
        var src = new PixelImage(3, 1);
        src[0, 0] = new Color32(200, 40, 90, 77);
        src[1, 0] = new Color32(12, 250, 130);
        var img = LayerEffects.RenderClipped(src, [new ColorAdjustEffect { Mode = ColorAdjustMode.Grayscale, Amount = 100 }]);
        for (int x = 0; x < 2; x++)
        {
            var c = img[x, 0];
            Assert.True(c.R == c.G && c.G == c.B, c.ToHex());
            Assert.Equal(src[x, 0].A, c.A);
        }
        Assert.Equal(Color32.Transparent, img[2, 0]);
    }

    [Fact]
    public void ColorAdjust_HueShiftZero_IsIdentity_120TurnsRedGreen()
    {
        var rng = new Random(7);
        var src = new PixelImage(16, 16);
        for (int i = 0; i < src.Pixels.Length; i++) src.Pixels[i] = new Color32((uint)rng.Next() | 0xFF000000u);
        var same = LayerEffects.RenderClipped(src, [new ColorAdjustEffect { Mode = ColorAdjustMode.HueShift, Hue = 0 }]);
        Assert.True(same.ContentEquals(src));

        var red = Filled(1, 1, Red);
        Assert.Equal(Green, LayerEffects.RenderClipped(red, [new ColorAdjustEffect { Mode = ColorAdjustMode.HueShift, Hue = 120 }])[0, 0]);
    }

    // ───────────── 디더 그림자 ─────────────

    [Theory]
    [InlineData(ShadowDither.Light25, 4)]
    [InlineData(ShadowDither.Half50, 8)]
    [InlineData(ShadowDither.Dense75, 12)]
    public void DitherShadow_IsOpaqueColorOrTransparent_InBayerPattern_PadsByOffset(ShadowDither dither, int cellsOf16)
    {
        var shadow = new ShadowEffect { Color = Black.WithAlpha(128), OffsetX = 4, OffsetY = 4, Dither = dither };
        Assert.Equal(new EffectPadding(0, 0, 4, 4), LayerEffects.Padding([shadow], 4, 4));

        var (img, ox, oy) = LayerEffects.Render(Filled(4, 4, Red), [shadow]);
        Assert.Equal((0, 0, 8, 8), (ox, oy, img.Width, img.Height));
        // 그림과 겹치지 않는 그림자 4×4 칸: 불투명 검정 또는 투명뿐, 밀도만큼
        int on = 0;
        for (int y = 4; y < 8; y++)
            for (int x = 4; x < 8; x++)
            {
                var c = img[x, y];
                Assert.True(c == Black || c == Color32.Transparent, c.ToHex());
                if (c == Black) on++;
            }
        Assert.Equal(cellsOf16, on);
        if (dither == ShadowDither.Half50)
            for (int y = 4; y < 8; y++)
                for (int x = 4; x < 8; x++)
                    Assert.Equal((x + y) % 2 == 0, img[x, y] == Black); // 바둑판
        Assert.All(Enumerable.Range(0, 16), i => Assert.Equal(Red, img[i % 4, i / 4])); // 그림은 그대로 위
    }

    // ───────────── 반사 ─────────────

    [Fact]
    public void Reflection_MirroredBelowAfterGap_OpacityDecreases_PadsGapPlusHeight()
    {
        var src = new PixelImage(3, 4);
        Color32[] rows = [Red, Green, Blue, new Color32(200, 200, 0)];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 3; x++)
                src[x, y] = rows[y];
        var m = new ReflectionEffect { Gap = 2, Height = 100, OpacityStart = 100, OpacityEnd = 25 };
        Assert.Equal(new EffectPadding(0, 0, 0, 6), LayerEffects.Padding([m], 3, 4));

        var (img, _, _) = LayerEffects.Render(src, [m]);
        Assert.Equal(10, img.Height);
        for (int y = 4; y < 6; y++)
            Assert.All(Enumerable.Range(0, 3), x => Assert.Equal(Color32.Transparent, img[x, y])); // 간격
        int lastAlpha = 256;
        for (int j = 0; j < 4; j++)
        {
            var c = img[1, 6 + j];
            var mirrored = rows[3 - j]; // 그림 쪽 줄부터 거꾸로
            Assert.Equal((mirrored.R, mirrored.G, mirrored.B), (c.R, c.G, c.B));
            Assert.True(c.A < lastAlpha, $"row {j}: {c.A}");
            lastAlpha = c.A;
        }
        Assert.Equal(255, img[1, 6].A);
        Assert.Equal(64, img[1, 9].A);
        Assert.Equal(rows[0], img[0, 0]); // 원래 그림은 그대로
    }

    // ───────────── 저장 ─────────────

    [Fact]
    public void SaveLoad_RoundTripsEffects_AndMigratesOldSpatialGradient()
    {
        var p = new SpriteProject(8, 8);
        p.DuplicateFrame(0); // 범위(프레임 1만)가 "모든 프레임"으로 바뀌지 않게
        LayerEffect[] fx =
        [
            new GradientOverlayEffect
            {
                Gradient = new EffectColorGradient
                {
                    Stops = [new(0, new Color32(1, 2, 3)), new(1, new Color32(4, 5, 6, 7))],
                    Dither = GradientDither.Bayer2,
                },
                Strength = 60, Blend = EffectBlend.Multiply, Easing = EffectEasing.EaseIn,
            },
            new InnerGlowEffect { Color = new(9, 8, 7), Size = 5, Strength = 33, Fade = false, Blend = EffectBlend.Normal, Enabled = false },
            new PaletteSwapEffect { Pairs = [new(Red, Blue), new(Green, Color32.Transparent)], Tolerance = 12, Frames = new FrameRange(0, 0) },
            new ColorAdjustEffect { Mode = ColorAdjustMode.BrightnessContrast, Amount = 40, Hue = -90, Brightness = 20, Contrast = -30, Saturation = 70 },
            new ReflectionEffect { Gap = 3, Height = 80, OpacityStart = 90, OpacityEnd = 10, DitherFade = true, Tint = new(0, 100, 200, 90), Wave = 2, Above = true },
            new ShadowEffect { Color = new(10, 20, 30), OffsetX = -2, OffsetY = 3, Dither = ShadowDither.Dense75 },
        ];
        p.SetLayerEffects(0, fx);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        Assert.Equal(fx, ProjectIO.Load(stream).Layers[0].Effects);

        // 예전 파일: 디더 칸이 없는 그림자 = 반투명 그림자, 다른 새 효과도 설정이 빠지면 기본값
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            System.Text.Json.Nodes.JsonNode root;
            using (var r = entry.Open()) root = System.Text.Json.Nodes.JsonNode.Parse(r)!;
            var effects = root["layers"]!.AsArray()[0]!["effects"]!.AsArray();
            effects.Clear();
            effects.Add(new System.Text.Json.Nodes.JsonObject { ["type"] = "shadow", ["enabled"] = true, ["color"] = "#00000080", ["x"] = 2, ["y"] = 1 });
            effects.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "gradient", ["color"] = "#010203", ["color2"] = "#04050607",
                ["angle"] = 45, ["amount"] = 60, ["blend"] = "Multiply", ["dither"] = "Bayer2",
            });
            effects.Add(System.Text.Json.Nodes.JsonNode.Parse("""
                {
                  "type": "gradient",
                  "amount": 40,
                  "colorGradients": {
                    "Gradient": {
                      "angle": 90,
                      "interpolation": "Steps",
                      "dither": "Bayer4",
                      "stops": [
                        { "position": 0, "color": "#00FF00" },
                        { "position": 1, "color": "#0000FF" }
                      ],
                      "alphaStops": [
                        { "position": 0, "alpha": 255 },
                        { "position": 1, "alpha": 64 }
                      ]
                    }
                  }
                }
                """));
            foreach (var type in new[] { "innerglow", "swap", "adjust", "reflection" })
                effects.Add(new System.Text.Json.Nodes.JsonObject { ["type"] = type });
            entry.Delete();
            using var w = zip.CreateEntry(ProjectIO.ProjectEntryName).Open();
            using var writer = new System.Text.Json.Utf8JsonWriter(w);
            root.WriteTo(writer);
        }
        stream.Position = 0;
        var loaded = ProjectIO.Load(stream).Layers[0].Effects;
        Assert.Equal(new ShadowEffect { Color = Black.WithAlpha(128), OffsetX = 2, OffsetY = 1 }, loaded[0]);
        var migrated = Assert.IsType<ColorOverlayEffect>(loaded[1]);
        Assert.Equal((new Color32(1, 2, 3), 60, EffectBlend.Multiply), (migrated.Color, migrated.Amount, migrated.Blend));
        var spatial = Assert.Single(migrated.ColorGradients).Value;
        Assert.Equal(45, spatial.Angle);
        Assert.Equal(GradientDither.Bayer2, spatial.Dither);
        Assert.True(spatial.StraightRgba);
        Assert.Equal([new GradientStop(0, new Color32(1, 2, 3)), new GradientStop(1, new Color32(4, 5, 6, 7))], spatial.Stops);
        Assert.Empty(spatial.AlphaStops);
        var migrationProbe = migrated with
        {
            Amount = 100,
            Blend = EffectBlend.Normal,
            ColorGradients = new Dictionary<string, EffectColorGradient>
            {
                [nameof(ColorOverlayEffect.Color)] = spatial with { Angle = 0, Dither = GradientDither.None },
            },
        };
        Assert.Equal(new Color32(2, 2, 3), LayerEffects.RenderClipped(Filled(3, 1, Black), [migrationProbe])[1, 0]);
        var custom = Assert.IsType<ColorOverlayEffect>(loaded[2]);
        Assert.Equal(40, custom.Amount);
        var customSpatial = Assert.Single(custom.ColorGradients).Value;
        Assert.Equal((90, GradientInterpolation.Steps, GradientDither.Bayer4),
            (customSpatial.Angle, customSpatial.Interpolation, customSpatial.Dither));
        Assert.Equal([new GradientStop(0, Green), new GradientStop(1, Blue)], customSpatial.Stops);
        Assert.Equal([new GradientAlphaStop(0, 255), new GradientAlphaStop(1, 64)], customSpatial.AlphaStops);
        Assert.Equal(
            [new InnerGlowEffect(), new PaletteSwapEffect(), new ColorAdjustEffect(), new ReflectionEffect()],
            loaded.Skip(3));
    }
}
