using System.Diagnostics;
using System.Drawing;
using Gpixel.Core.AiDraw;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.Core.Pixelate;

namespace Gpixel.Core.Tests;

public class PixelateTests
{
    private static readonly Color32 Outline = new(30, 20, 12);

    /// <summary>24×24 스프라이트: 모서리를 깎은 사각형, 1px 외곽선 + 5색 무늬(<paramref name="detailed"/>면 1~2px 잔무늬), 1×2 눈 두 개(모두 6색).</summary>
    private static PixelImage Sprite(bool detailed = false)
    {
        var fills = new[] { new Color32(200, 40, 40), new Color32(60, 160, 70), new Color32(60, 80, 200), new Color32(230, 200, 60), new Color32(240, 190, 150) };
        var img = new PixelImage(24, 24);
        static bool Inside(int x, int y) => x >= 0 && y >= 0 && x < 24 && y < 24 && Math.Min(x, 23 - x) + Math.Min(y, 23 - y) >= 3;
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                if (!Inside(x, y)) continue;
                bool edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                img[x, y] = edge ? Outline : fills[(detailed ? x * 3 + y * 5 + x * y % 3 : x / 5 + y / 6) % fills.Length];
            }
        img[8, 9] = img[8, 10] = img[15, 9] = img[15, 10] = Outline;
        return img;
    }

    /// <summary>
    /// AI 그림 흉내: 칸마다 7~9px로 키우고(열·행 크기 무작위), 3×3 흐림과 ±<paramref name="noise"/> 잡음, 검은 바탕(둘레 <paramref name="margin"/>칸).
    /// 여러 그림을 <paramref name="gapCells"/>칸 사이를 두고 가로로 잇는다.
    /// </summary>
    private static PixelImage Render(IReadOnlyList<PixelImage> sprites, int seed = 7, int margin = 4, double noise = 6, int gapCells = 6)
    {
        int cw = sprites.Sum(s => s.Width) + gapCells * (sprites.Count - 1) + 2 * margin;
        int ch = sprites.Max(s => s.Height) + 2 * margin;
        var logical = new PixelImage(cw, ch);
        int at = margin;
        foreach (var s in sprites)
        {
            logical.Blit(s, at, margin);
            at += s.Width + gapCells;
        }
        var rng = new Random(seed);
        var xs = new int[cw + 1];
        var ys = new int[ch + 1];
        for (int i = 1; i <= cw; i++) xs[i] = xs[i - 1] + 7 + rng.Next(3);
        for (int i = 1; i <= ch; i++) ys[i] = ys[i - 1] + 7 + rng.Next(3);
        var big = new PixelImage(xs[cw], ys[ch]);
        for (int cy = 0; cy < ch; cy++)
            for (int cx = 0; cx < cw; cx++)
            {
                var c = logical[cx, cy];
                big.FillRect(Rectangle.FromLTRB(xs[cx], ys[cy], xs[cx + 1], ys[cy + 1]), c.A == 0 ? Color32.Black : c);
            }
        var result = new PixelImage(big.Width, big.Height);
        for (int y = 0; y < big.Height; y++)
            for (int x = 0; x < big.Width; x++)
            {
                int r = 0, g = 0, b = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!big.Contains(x + dx, y + dy)) continue;
                        var c = big[x + dx, y + dy];
                        r += c.R; g += c.G; b += c.B; n++;
                    }
                byte Noisy(int v) => (byte)Math.Clamp((int)Math.Round(v / (double)n + (rng.NextDouble() * 2 - 1) * noise), 0, 255);
                result[x, y] = new Color32(Noisy(r), Noisy(g), Noisy(b));
            }
        return result;
    }

    private static PixelateSettings SixColors => new() { Palette = new PaletteSettings { Colors = 6 } };

    private static readonly Color32 RobeWhite = new(0xF4, 0xF4, 0xF8);
    private static readonly Color32 RobePurple = new(140, 80, 180);
    private static readonly Color32 RobeGrey = new(150, 150, 160);

    /// <summary>
    /// 24×24 "흰 옷": 모서리를 깎은 사각형, 1px 외곽선, 넓은 흰 면(#F4F4F8)에 아래·왼쪽 2칸 보라 단, 오른쪽 3칸 회색 그늘, 가운데 보라 띠.
    /// AI 그림처럼 흰 면이 보라·회색과 만나는 곳에는 한 칸 폭의 번진 중간 색(흰색 70% + 이웃 색 30% — 흰색에 더 가까움)이 있다.
    /// </summary>
    private static PixelImage RobeSprite()
    {
        var img = new PixelImage(24, 24);
        static bool Inside(int x, int y) => x >= 0 && y >= 0 && x < 24 && y < 24 && Math.Min(x, 23 - x) + Math.Min(y, 23 - y) >= 3;
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                if (!Inside(x, y)) continue;
                bool edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                img[x, y] = edge ? Outline
                    : y >= 20 || x <= 2 || (y is 11 or 12 && x is >= 6 and <= 17) ? RobePurple
                    : x >= 19 ? RobeGrey
                    : RobeWhite;
            }
        var blurred = img.Clone();
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                if (img[x, y] != RobeWhite) continue;
                foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    var n = img.Get(x + dx, y + dy);
                    if (n != RobePurple && n != RobeGrey) continue;
                    static byte Mix(byte w, byte c) => (byte)Math.Round(w * 0.7 + c * 0.3);
                    blurred[x, y] = new Color32(Mix(RobeWhite.R, n.R), Mix(RobeWhite.G, n.G), Mix(RobeWhite.B, n.B));
                    break;
                }
            }
        return blurred;
    }

    private static readonly Color32 Skin = new(0xFC, 0xDD, 0xC1);
    private static readonly Color32 Apricot = new(0xE1, 0x92, 0x69);
    private static readonly Color32 Gold = new(0xD5, 0xA8, 0x55);
    private static readonly Color32 Hair = new(0xC0, 0xBD, 0xD3);
    private static readonly Color32 HairShade = new(0x91, 0x83, 0xA7);

    /// <summary>
    /// 24×24 "얼굴": 1px 외곽선, 넓은 살색 면, 외곽선 옆 2×3칸 살구색 그늘(귀), 금색 장식 3×4칸, 위쪽 넓은 연보라 머리와 보라 그늘.
    /// </summary>
    private static PixelImage FaceSprite()
    {
        var img = new PixelImage(24, 24);
        static bool Inside(int x, int y) => x >= 0 && y >= 0 && x < 24 && y < 24 && Math.Min(x, 23 - x) + Math.Min(y, 23 - y) >= 3;
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                if (!Inside(x, y)) continue;
                bool edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                img[x, y] = edge ? Outline
                    : y <= 7 ? (x % 5 == 0 ? HairShade : Hair)
                    : x is >= 1 and <= 2 && y is >= 11 and <= 13 ? Apricot
                    : x is >= 16 and <= 18 && y is >= 15 and <= 18 ? Gold
                    : Skin;
            }
        return img;
    }

    /// <summary>
    /// 작은 살구색 그늘(귀)은 고른 색 수가 적어도 금색으로 바뀌지 않는다(기본: OKLCh 색상 가중 + 다른 색상 지키기). 예전 방식(OKLab, 지키기 끔)은 금색이 된다 —
    /// 살구색이 k-평균에서 금색 묶음에 섞이고(OKLab 0.07 — 살색까지는 0.20) 팔레트에 살구색이 없음.
    /// </summary>
    [Fact]
    public void SmallApricotShadingDoesNotTurnGold()
    {
        var source = Render([FaceSprite()]);
        var s = new PixelateSettings { Palette = new PaletteSettings { Colors = 5 } };
        var src = PixelateSource.Prepare(source, s);
        var apricot = OkLab.FromColor(Apricot);
        var gold = OkLab.FromColor(Gold);

        // 원본 칸 색이 살구색인 칸의 결과 색 — 금색보다 살구색에 가까운(색상 차) 칸의 비율.
        double ApricotKept(PixelateResult r)
        {
            int n = 0, kept = 0;
            for (int f = 0; f < r.Frames.Count; f++)
                for (int i = 0; i < r.Frames[f].Pixels.Length; i++)
                {
                    var from = r.SourceColors[f].Pixels[i];
                    if (from.A == 0 || Lab.Distance(OkLab.FromColor(from), apricot) > 0.03) continue;
                    n++;
                    var to = OkLab.FromColor(r.Frames[f].Pixels[i]);
                    if (ColorDistance.HueDifference(to, apricot) < ColorDistance.HueDifference(to, gold) && ColorDistance.Chroma(to) >= 0.05) kept++;
                }
            Assert.True(n >= 3, $"apricot cells {n}");
            return kept / (double)n;
        }

        var now = Pixelator.Run(src, s);
        Assert.Equal(ColorDistanceMode.OkLchHue, s.Palette.ColorDistance);
        Assert.True(s.Palette.KeepDistinctHues);
        Assert.True(ApricotKept(now) >= 0.8, ApricotKept(now).ToString());
        var old = Pixelator.Run(src, s with { Palette = s.Palette with { ColorDistance = ColorDistanceMode.OkLab, KeepDistinctHues = false } });
        Assert.True(ApricotKept(old) <= 0.2, ApricotKept(old).ToString());
        // 금색 장식은 그대로 금색 쪽.
        Assert.Contains(now.Palette, c => Lab.Distance(OkLab.FromColor(c), gold) <= 0.05);
    }

    /// <summary>
    /// 섞인 칸: 10px 블록 그림(외곽선 고리 + 살색 + 외곽선 옆 살구색 2×2 블록)을 블록과 6px 어긋난 수동 격자(칸 10px)로 줄이면 외곽선 옆 칸은
    /// 검은 외곽선 ⅓ + 살구색 ⅔. 평균이면 갈색빛 주황(#AA7C64 쯤)이 되어 색상 가중 거리로는 금색에 더 가깝지만, 명암 계열 우선(기본)이면 세 방식 모두
    /// 살구색 계열을 지킨다(팔레트: 검정·살색·살구색·금색).
    /// </summary>
    [Fact]
    public void MixedApricotOutlineCellStaysApricot()
    {
        var black = new Color32(0x1E, 0x12, 0x11);
        var apricot = new Color32(0xF0, 0xB0, 0x90);
        var gold = new Color32(0xD4, 0xAB, 0x55);
        var logical = new PixelImage(10, 10);
        for (int y = 1; y <= 8; y++)
            for (int x = 1; x <= 8; x++)
                logical[x, y] = x is 1 or 8 || y is 1 or 8 ? black : x is 2 or 3 && y is >= 3 and <= 6 ? apricot : Skin;
        var rng = new Random(3);
        var image = new PixelImage(100, 100);
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 100; x++)
            {
                var c = logical[x / 10, y / 10];
                if (c.A == 0) continue;
                byte N(byte v) => (byte)Math.Clamp(v + rng.Next(-4, 5), 0, 255);
                image[x, y] = new Color32(N(c.R), N(c.G), N(c.B));
            }
        var baseSettings = new PixelateSettings
        {
            Background = new BackgroundSettings { Mode = BackgroundMode.None },
            Grid = new PixelGridSettings { Mode = GridMode.Manual, Scale = 10, OffsetX = 6, LocalCorrection = false },
            Palette = new PaletteSettings { Method = PaletteMethod.Existing, ExistingColors = [black.ToHex(), Skin.ToHex(), apricot.ToHex(), gold.ToHex()], SnapOutline = false },
            Cleanup = new CleanupSettings { RemoveOrphans = false, FillHoles = false, OutlineFit = OutlineFit.Off },
        };
        Assert.True(baseSettings.Palette.PreferShadeFamily);
        var src = PixelateSource.Prepare(image, baseSettings);
        foreach (var mode in new[] { DownsampleMode.Mode, DownsampleMode.Median, DownsampleMode.Mean })
        {
            var s = baseSettings with { Downsample = baseSettings.Downsample with { Mode = mode } };
            var r = Pixelator.Run(src, s);
            var f = r.Frames[0];
            var layout = r.Maps[0].Layout();
            // 칸 [16, 26) × 행 3~6: 안쪽(둘레 2px 뺌) 18~23px = 외곽선 2px + 살구색 4px.
            int checkedCells = 0;
            for (int row = 0; row < layout.Rows; row++)
                for (int col = 0; col < layout.Columns; col++)
                {
                    var (x0, y0, x1, _) = layout.Cell(col, row);
                    if (x0 != 16 || x1 != 26 || y0 < 30 || y0 >= 70) continue;
                    var got = f[col + r.Maps[0].OffsetX, row + r.Maps[0].OffsetY];
                    Assert.True(got == apricot, $"{mode} cell ({col},{row}) raw {r.SourceColors[0][col + r.Maps[0].OffsetX, row + r.Maps[0].OffsetY].ToHex()} → {got.ToHex()}");
                    checkedCells++;
                }
            Assert.Equal(4, checkedCells);
        }
        // 끄면(예전) 평균 칸은 금색 — 위 검사가 실제로 섞인 칸을 본다는 뜻.
        var off = Pixelator.Run(src, baseSettings with { Downsample = baseSettings.Downsample with { Mode = DownsampleMode.Mean }, Palette = baseSettings.Palette with { PreferShadeFamily = false } });
        Assert.Contains(off.Frames[0].Pixels, c => c == gold);
    }

    /// <summary>
    /// 명암 계열 우선: 진한 살구색(#C88060 — 팔레트 살구색보다 어두움)은 색 거리로는 밝기가 비슷한 금색이 가장 가깝지만, 같은 계열(색상·채도 비슷)의 살구색으로.
    /// 금색 계열 색·무채색은 그대로. 디더(Bayer 2×2)도 명암 계열 우선이면 살구색 그늘(#E69973 — RGB로는 금색이 가장 가까움)에 금색 점을 섞지 않는다.
    /// </summary>
    [Fact]
    public void ShadeFamilyPrefersSameHueShade()
    {
        var apricot = new Color32(0xF1, 0xB0, 0x8E);
        var gold = new Color32(0xD5, 0xA8, 0x55);
        var black = new Color32(0x1E, 0x12, 0x11);
        var grey = new Color32(0x90, 0x90, 0x90);
        Color32[] palette = [black, apricot, gold];
        Color32[] withGrey = [black, grey, apricot, gold];
        static Color32 Pick(ColorMetric m, Color32[] pal, Color32 c) => pal[m.Nearest(m.Point(c), pal.Select(m.Point).ToArray())];
        var deep = new Color32(0xC8, 0x80, 0x60);
        Assert.Equal(gold, Pick(ColorMetric.OkLab, palette, deep));
        foreach (var mode in new[] { ColorDistanceMode.OkLab, ColorDistanceMode.OkLchHue, ColorDistanceMode.Ciede2000 })
        {
            var shade = new ColorMetric(mode, 2, shadeFamily: true);
            Assert.Equal(apricot, Pick(shade, palette, deep));
            Assert.Equal(gold, Pick(shade, withGrey, new Color32(0xB8, 0x8A, 0x40)));
            Assert.Equal(grey, Pick(shade, withGrey, new Color32(0x88, 0x86, 0x84)));
        }

        var shading = new PixelImage(4, 4);
        shading.FillRect(new Rectangle(0, 0, 4, 4), new Color32(0xE6, 0x99, 0x73));
        var old = shading.Clone();
        new Gpixel.Core.Edit.PaletteRemap(palette).Apply(old, Gpixel.Core.Edit.PaletteDither.Bayer2);
        Assert.Contains(old.Pixels, c => c == gold);
        var metric = new ColorMetric(ColorDistanceMode.OkLchHue, 2, shadeFamily: true);
        var pts = palette.Select(metric.Point).ToArray();
        new Gpixel.Core.Edit.PaletteRemap(palette, c => palette[metric.Nearest(metric.Point(c), pts)],
            (a, b) => ColorDistance.CanDither(OkLab.FromColor(a), OkLab.FromColor(b))).Apply(shading, Gpixel.Core.Edit.PaletteDither.Bayer2);
        Assert.All(shading.Pixels, c => Assert.Equal(apricot, c));
    }

    /// <summary>CIEDE2000: Sharma·Wu·Dalal(2005) 시험 자료의 쌍 1·7·17·25(CIELAB 입력).</summary>
    [Theory]
    [InlineData(50, 2.6772, -79.7751, 50, 0, -82.7485, 2.0425)]
    [InlineData(50, 0, 0, 50, -1, 2, 2.3669)]
    [InlineData(50, 2.5, 0, 73, 25, -18, 27.1492)]
    [InlineData(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
    public void Ciede2000MatchesSharmaTestData(double l1, double a1, double b1, double l2, double a2, double b2, double expected)
    {
        Assert.Equal(expected, ColorDistance.Ciede2000(new CieLab(l1, a1, b1), new CieLab(l2, a2, b2)), 4);
        Assert.Equal(expected, ColorDistance.Ciede2000(new CieLab(l2, a2, b2), new CieLab(l1, a1, b1)), 4);
    }

    /// <summary>색 거리 도움: sRGB → CIELAB(D65 흰색 = L 100, a·b 0), OKLCh 색상 가중 1 = OKLab, 색상 무게가 크면 색상 차만 커짐.</summary>
    [Fact]
    public void ColorMetricBasics()
    {
        var white = ColorDistance.ToCieLab(new Color32(255, 255, 255));
        Assert.Equal(100, white.L, 2);
        Assert.Equal(0, white.A, 2);
        Assert.Equal(0, white.B, 2);
        var a = OkLab.FromColor(Apricot);
        var g = OkLab.FromColor(Gold);
        Assert.Equal(Lab.Distance(a, g), ColorDistance.OkLchHue(a, g, 1), 4);
        Assert.True(ColorDistance.OkLchHue(a, g, 2) > Lab.Distance(a, g) * 1.5);
        var light = OkLab.FromColor(new Color32(0xF0, 0xF0, 0xF0));
        var dark = OkLab.FromColor(new Color32(0x80, 0x80, 0x80));
        Assert.Equal(ColorDistance.OkLchHue(light, dark, 1), ColorDistance.OkLchHue(light, dark, 3), 3);
    }

    [Fact]
    public void RecoversScaleSizeOutlineAndColors()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        Assert.False(src.Background.UsedAlpha);
        Assert.Single(src.Split.Frames);

        var r = Pixelator.Run(src, SixColors);
        Assert.InRange(r.DetectedScale, 7.5, 8.5);
        Assert.InRange(r.Scale, 7.5, 8.5);
        var frame = Assert.Single(r.Frames);
        var bounds = frame.GetOpaqueBounds();
        Assert.InRange(bounds.Width, 23, 25);
        Assert.InRange(bounds.Height, 23, 25);
        Assert.True(r.Palette.Count <= 6);

        // 외곽선이 배경에 닿아 있어도 남는다(테두리와 이어진 배경만 지움): 가장자리 픽셀은 거의 모두 외곽선 색(가장 어두운 색).
        var dark = r.Palette[0];
        int edge = 0, outline = 0;
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                if (frame[x, y].A == 0) continue;
                if (frame.Get(x - 1, y).A != 0 && frame.Get(x + 1, y).A != 0 && frame.Get(x, y - 1).A != 0 && frame.Get(x, y + 1).A != 0) continue;
                edge++;
                if (frame[x, y] == dark) outline++;
            }
        Assert.True(edge > 60);
        Assert.True(outline >= edge * 0.95, $"outline {outline}/{edge}");
        Assert.True(dark.R < 60 && dark.G < 60 && dark.B < 60);
        Assert.Equal(0, PixelCleanup.CountOrphans(frame));
    }

    [Fact]
    public void OutlineThicknessGivesScaleAndOnePixelOutline()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        var (thickness, samples) = GridDetector.OutlineThickness(src.Cleaned, null, -1, src.ContentBounds, 0.24);
        Assert.True(samples > 100);
        Assert.InRange(thickness, 7.5, 8.5);
        var est = src.EstimatedScale(-1, 0.15, ScaleSource.Outline, 0.24);
        Assert.InRange(est.Scale, 7.5, 8.5);

        var frame = Assert.Single(Pixelator.Run(src, SixColors).Frames);
        var (edge, thick) = PixelCleanup.OutlineThickness(frame, 0.24);
        Assert.True(edge > 60);
        Assert.True(1 - thick / (double)edge >= 0.95, $"{thick}/{edge}");
    }

    private static double NearestError(IEnumerable<Color32> palette, Color32 c)
    {
        var lab = OkLab.FromColor(c);
        return palette.Min(p => (double)Lab.Distance(lab, OkLab.FromColor(p)));
    }

    /// <summary>
    /// 넓은 흰 옷 + 번진 회보라 가장자리, 색 4개(외곽선·흰색·보라·회색): 원본 색은 흰색을 그대로 지키고(가장 많이 쓰인 칠 색도 그 색),
    /// 평균 색은 번진 칸이 흰 묶음을 끌어 회색 쪽으로 밀린다.
    /// </summary>
    [Fact]
    public void OriginalPaletteColorsKeepWhiteRobe()
    {
        var source = Render([RobeSprite()]);
        var original = new PixelateSettings { Palette = new PaletteSettings { Colors = 4 } };
        Assert.Equal(PaletteColorSource.Original, original.Palette.ColorSource);
        var mean = original with { Palette = original.Palette with { ColorSource = PaletteColorSource.Mean } };
        var src = PixelateSource.Prepare(source, original);
        var ro = Pixelator.Run(src, original);
        var rm = Pixelator.Run(src, mean);

        double whiteO = NearestError(ro.Palette, RobeWhite), whiteM = NearestError(rm.Palette, RobeWhite);
        Assert.True(whiteO <= 0.02, $"white {whiteO:0.0000}");
        foreach (var fill in new[] { RobePurple, RobeGrey, Outline })
            Assert.True(NearestError(ro.Palette, fill) <= 0.02, $"{fill.ToHex()} {NearestError(ro.Palette, fill):0.0000}");
        Assert.True(whiteO < whiteM, $"original {whiteO:0.0000} vs mean {whiteM:0.0000}");
        Assert.True(whiteM > 0.03, $"mean {whiteM:0.0000}");

        // 가장 많이 쓰인 칠 색 = 팔레트의 흰색.
        var white = ro.Palette.OrderBy(p => Lab.Distance(OkLab.FromColor(p), OkLab.FromColor(RobeWhite))).First();
        var dominant = Assert.Single(ro.Frames).Pixels.Where(c => c.A != 0).GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        Assert.Equal(white, dominant);

        // 같은 입력이면 늘 같은 결과.
        Assert.Equal(ro.Palette, Pixelator.Run(src, original).Palette);
    }

    /// <summary>원본 색 팔레트(k-평균·중앙 분할)는 모두 실제로 쓰인 색이고, 가장자리 무게 0이어도(모두 가장자리) 팔레트가 나온다.</summary>
    [Fact]
    public void OriginalPaletteUsesActualCellColors()
    {
        var img = Render([RobeSprite()], noise: 10);
        var used = img.Pixels.Select(c => c.WithAlpha(255)).ToHashSet();
        var (hist, members) = PaletteBuilder.WeightedHistogram([img], 0.25, 0.24);
        foreach (int k in new[] { 3, 6, 12 })
        {
            Assert.All(PaletteBuilder.KMeans(hist, k, members).Palette, c => Assert.Contains(c, used));
            Assert.All(PaletteBuilder.MedianCut(hist, k, members), c => Assert.Contains(c, used));
        }
        var tiny = new PixelImage(2, 1);
        tiny[0, 0] = RobeWhite;
        tiny[1, 0] = RobePurple;
        var (th, tm) = PaletteBuilder.WeightedHistogram([tiny], 0, 0.24);
        Assert.Equal([RobePurple, RobeWhite], PaletteBuilder.KMeans(th, 2, tm).Palette.OrderBy(c => c.R));
    }

    /// <summary><see cref="Sprite"/>와 같고 외곽선만 2px 두께(바깥 둘레에서 2칸 안).</summary>
    private static PixelImage ThickOutlineSprite()
    {
        var img = Sprite();
        var thick = img.Clone();
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                if (img[x, y].A == 0) continue;
                bool near = false;
                for (int d = 1; d <= 2 && !near; d++)
                    near = img.Get(x - d, y).A == 0 || img.Get(x + d, y).A == 0 || img.Get(x, y - d).A == 0 || img.Get(x, y + d).A == 0;
                if (near) thick[x, y] = Outline;
            }
        return thick;
    }

    private static double OnePixelShare(IEnumerable<PixelImage> frames)
    {
        int edge = 0, thick = 0;
        foreach (var f in frames)
        {
            var (e, t) = PixelCleanup.OutlineThickness(f, 0.24);
            edge += e;
            thick += t;
        }
        return edge == 0 ? 1 : 1 - thick / (double)edge;
    }

    [Fact]
    public void OutlineFitShrinkScalesToOutlineAndThinKeepsEdgeScale()
    {
        var source = Render([ThickOutlineSprite()]);
        var shrink = Pixelator.Run(source, SixColors);
        Assert.Equal(OutlineFit.Shrink, SixColors.Cleanup.OutlineFit);
        Assert.InRange(shrink.DetectedScale, 14, 18); // 외곽선 두께(2칸 × 8px)
        Assert.True(OnePixelShare(shrink.Frames) >= 0.9, OnePixelShare(shrink.Frames).ToString());

        var thinSettings = SixColors with { Cleanup = SixColors.Cleanup with { OutlineFit = OutlineFit.Thin } };
        var thin = Pixelator.Run(source, thinSettings);
        Assert.InRange(thin.DetectedScale, 7.5, 8.5); // 색 경계
        Assert.True(OnePixelShare(thin.Frames) >= 0.9, OnePixelShare(thin.Frames).ToString());
        var off = Pixelator.Run(source, SixColors with { Cleanup = SixColors.Cleanup with { OutlineFit = OutlineFit.Off } });
        Assert.InRange(off.DetectedScale, 7.5, 8.5);
        Assert.True(OnePixelShare(off.Frames) < 0.5);

        // 수동 격자는 외곽선 맞추기보다 이긴다.
        var manual = Pixelator.Run(source, SixColors with { Grid = SixColors.Grid with { Mode = GridMode.Manual, Scale = 8 } });
        Assert.Equal(8, manual.DetectedScale);
    }

    [Fact]
    public void CandidatesIncludeBothOutlineFitModes()
    {
        var source = Render([ThickOutlineSprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        var all = PixelateCandidates.Run(src, SixColors, CandidateVary.OutlineFit, 4, new ScoreWeights());
        Assert.Contains(all, c => c.Settings.Cleanup.OutlineFit == OutlineFit.Shrink);
        Assert.Contains(all, c => c.Settings.Cleanup.OutlineFit == OutlineFit.Thin);
    }

    /// <summary>
    /// 종이 결 흉내: 24px 덩어리(투명 바탕, 시작 x = 7·y = 5, 4색 + 1칸 외곽선, 20×20칸)에 2~3px 알갱이(3px 칸마다 같은 값)의 채널마다 ±10 잡음.
    /// </summary>
    private static PixelImage TexturedBlocks(out int originX, out int originY)
    {
        const int block = 24, cells = 20;
        originX = 7;
        originY = 5;
        var colors = new[] { new Color32(70, 130, 140), new Color32(200, 150, 90), new Color32(240, 200, 170), new Color32(110, 80, 150) };
        var rng = new Random(11);
        static bool Inside(int x, int y) => x >= 0 && y >= 0 && x < cells && y < cells && Math.Min(x, cells - 1 - x) + Math.Min(y, cells - 1 - y) >= 3;
        var logical = new PixelImage(cells, cells);
        for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
            {
                if (!Inside(x, y)) continue;
                bool edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                logical[x, y] = edge ? Outline : colors[rng.Next(colors.Length)];
            }
        var img = new PixelImage(originX + cells * block + 11, originY + cells * block + 9);
        var grain = new int[(img.Width / 3 + 1) * (img.Height / 3 + 1) * 3];
        for (int i = 0; i < grain.Length; i++) grain[i] = rng.Next(-10, 11);
        int gw = img.Width / 3 + 1;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int cx = Math.DivRem(x - originX, block, out int rx), cy = Math.DivRem(y - originY, block, out int ry);
                if (x < originX || y < originY || cx >= cells || cy >= cells) continue;
                var c = logical[cx, cy];
                if (c.A == 0) continue;
                int g = ((y / 3) * gw + x / 3) * 3;
                static byte Add(byte v, int d) => (byte)Math.Clamp(v + d, 0, 255);
                img[x, y] = new Color32(Add(c.R, grain[g]), Add(c.G, grain[g + 1]), Add(c.B, grain[g + 2]));
            }
        return img;
    }

    [Fact]
    public void BlockEdgesFindTexturedBlockSizeAndPhase()
    {
        var image = TexturedBlocks(out int ox, out int oy);
        var s = new PixelateSettings
        {
            Grid = new PixelGridSettings { Source = ScaleSource.EdgeLines },
            Cleanup = new CleanupSettings { OutlineFit = OutlineFit.Thin },
        };
        var src = PixelateSource.Prepare(image, s);
        var est = src.EstimatedScale(-1, s.Grid.Jitter, ScaleSource.EdgeLines, 0.24);
        Assert.InRange(est.Blocks, 23, 25);
        Assert.Equal(ScaleSource.EdgeLines, est.Used);
        // 자동도 덩어리 간격이 뚜렷하므로 덩어리 경계를 고른다(색 경계는 종이 결에 속아 작게 나올 수 있음).
        var auto = src.EstimatedScale(-1, s.Grid.Jitter, ScaleSource.Auto, 0.24);
        Assert.Equal(ScaleSource.EdgeLines, auto.Used);

        var r = Pixelator.Run(src, s);
        Assert.InRange(r.Scale, 23, 25);
        var b = src.ContentBounds;
        var m = r.Maps[0];
        foreach (var (cuts, origin, lo, hi) in new[] { (m.XCuts, ox, b.Left, b.Right), (m.YCuts, oy, b.Top, b.Bottom) })
            foreach (int c in cuts.Where(c => c > lo && c < hi))
            {
                int off = Math.Abs(((c - origin) % 24 + 24) % 24);
                Assert.True(Math.Min(off, 24 - off) <= 2, $"cut {c} is {Math.Min(off, 24 - off)}px off a block edge");
            }
    }

    [Fact]
    public void VectorOutlineRedrawsThickOutlineAsOnePixelLine()
    {
        var source = Render([ThickOutlineSprite()]);
        var s = SixColors with { Cleanup = SixColors.Cleanup with { OutlineFit = OutlineFit.Vector } };
        var r = Pixelator.Run(source, s);
        Assert.InRange(r.DetectedScale, 7.5, 8.5); // 칸 크기는 격자 설정(자동)대로 — 외곽선 두께(16px)가 아님
        var f = Assert.Single(r.Frames);
        bool Dark(Color32 c) => c.A != 0 && OkLab.FromColor(c).L <= 0.24;
        bool Edge(int x, int y) => f.Get(x - 1, y).A == 0 || f.Get(x + 1, y).A == 0 || f.Get(x, y - 1).A == 0 || f.Get(x, y + 1).A == 0;
        Assert.True(OnePixelShare(r.Frames) >= 0.95, OnePixelShare(r.Frames).ToString());

        // 바깥 둘레는 모두 외곽선(빈틈 없음)이고 8방향으로 한 줄.
        var ring = new HashSet<Point>();
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                if (f[x, y].A == 0 || !Edge(x, y)) continue;
                Assert.True(Dark(f[x, y]), $"gap at ({x},{y})");
                ring.Add(new Point(x, y));
            }
        var seen = new HashSet<Point> { ring.First() };
        var stack = new Stack<Point>(seen);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var q = new Point(p.X + dx, p.Y + dy);
                    if (ring.Contains(q) && seen.Add(q)) stack.Push(q);
                }
        }
        Assert.Equal(ring.Count, seen.Count);

        // L자 겹 픽셀 없음: 둘레 픽셀 중 둘레 이웃이 가로 하나·세로 하나뿐인(그 둘이 대각선으로 이어져 빼도 끊기지 않는) 픽셀.
        int corners = 0;
        foreach (var p in ring)
        {
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx | dy) != 0 && ring.Contains(new Point(p.X + dx, p.Y + dy))) n++;
            bool h = ring.Contains(new Point(p.X - 1, p.Y)) ^ ring.Contains(new Point(p.X + 1, p.Y));
            bool v = ring.Contains(new Point(p.X, p.Y - 1)) ^ ring.Contains(new Point(p.X, p.Y + 1));
            if (n == 2 && h && v) corners++;
        }
        Assert.Equal(0, corners);

        // 눈(바깥 외곽선과 닿지 않은 작은 어두운 덩어리)은 남는다: 둘레와 이어지지 않은 어두운 픽셀이 양쪽 눈에.
        var inner = new List<Point>();
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
                if (Dark(f[x, y]) && !ring.Contains(new Point(x, y)))
                {
                    bool touches = false;
                    for (int dy = -1; dy <= 1 && !touches; dy++)
                        for (int dx = -1; dx <= 1 && !touches; dx++)
                            touches = ring.Contains(new Point(x + dx, y + dy));
                    if (!touches) inner.Add(new Point(x, y));
                }
        var bounds = f.GetOpaqueBounds();
        int mid = bounds.Left + bounds.Width / 2;
        Assert.Contains(inner, p => p.X < mid);
        Assert.Contains(inner, p => p.X >= mid);
    }

    [Fact]
    public void ThinOutlineMakesTwoPixelOutlineOnePixelWithoutBreaks()
    {
        var fill = new Color32(60, 160, 70);
        var img = new PixelImage(16, 16);
        img.FillRect(new Rectangle(2, 2, 12, 12), Outline);
        img.FillRect(new Rectangle(4, 4, 8, 8), fill);
        img[7, 7] = img[8, 7] = Outline; // 안쪽 어두운 덩어리(눈) — 그대로
        Assert.Equal(40, PixelCleanup.OutlineThickness(img, 0.24).Thick); // 모서리 4칸 말고는 모두 2px

        Assert.True(PixelCleanup.ThinOutline(img, 0.24) > 0);
        var (edge, thick) = PixelCleanup.OutlineThickness(img, 0.24);
        Assert.Equal(0, thick);
        Assert.Equal(44, edge);
        Assert.Equal(Outline, img[7, 7]);
        Assert.Equal(Outline, img[8, 7]);
        for (int i = 2; i < 14; i++)
        {
            Assert.Equal(Outline, img[i, 2]);
            Assert.Equal(Outline, img[i, 13]);
            Assert.Equal(Outline, img[2, i]);
            Assert.Equal(Outline, img[13, i]);
        }
        Assert.Equal(fill, img[3, 3]);
        Assert.Equal(fill, img[3, 8]);
    }

    [Fact]
    public void BackgroundRemovalKeepsOutlineTouchingBlackBackground()
    {
        var source = Render([Sprite()]);
        var connected = BackgroundRemoval.Run(source, new BackgroundSettings());
        var everywhere = BackgroundRemoval.Run(source, new BackgroundSettings { ConnectedOnly = false, AutoTolerance = false, Tolerance = 45 });
        Assert.NotNull(connected.Color);
        Assert.True(connected.Color!.Value.R < 10);
        // 이어진 것만 지우면 안쪽 눈(외곽선 색)은 남고, 넉넉한 허용 거리로 어디서나 지우면 외곽선·눈까지 지워져 전경이 줄어든다.
        Assert.True(connected.ForegroundCount > everywhere.ForegroundCount);
    }

    [Fact]
    public void RemoveOrphansKeepsHighContrastDetails()
    {
        var skin = new Color32(240, 190, 150);
        var img = new PixelImage(7, 7);
        img.Clear(skin);
        img[2, 2] = new Color32(232, 184, 146); // 잡티(대비 작음)
        img[4, 4] = Outline;                     // 눈동자(대비 큼)
        Assert.Equal(2, PixelCleanup.CountOrphans(img));
        PixelCleanup.RemoveOrphans(img, 0.18);
        Assert.Equal(skin, img[2, 2]);
        Assert.Equal(Outline, img[4, 4]);

        var speck = new PixelImage(5, 5);
        speck[2, 2] = skin;
        PixelCleanup.RemoveOrphans(speck, 0.18);
        Assert.Equal(0, speck[2, 2].A);
    }

    [Fact]
    public void FillHolesAndOutlineClosing()
    {
        var c = new Color32(60, 160, 70);
        var img = new PixelImage(5, 5);
        img.FillRect(new Rectangle(1, 1, 3, 3), c);
        img[2, 2] = Color32.Transparent;
        Assert.Equal(1, PixelCleanup.FillHoles(img));
        Assert.Equal(c, img[2, 2]);
        PixelCleanup.Outline(img, OutlineMode.EnsureClosed, Outline, 0.24);
        Assert.Equal(Outline, img[0, 2]);
        Assert.Equal(Outline, img[2, 0]);
        Assert.Equal(0, img[0, 0].A);
    }

    [Fact]
    public void SplitsSheetIntoFramesAndMergesDetachedFragment()
    {
        var a = Sprite();
        var b = Sprite();
        var c = Sprite();
        // 떨어진 작은 조각(효과): 세 번째 그림 오른쪽 위에 2×2.
        var withSpark = new PixelImage(28, 24);
        withSpark.Blit(c, 0, 0);
        withSpark.FillRect(new Rectangle(26, 0, 2, 2), new Color32(230, 200, 60));
        var source = Render([a, b, withSpark]);
        var src = PixelateSource.Prepare(source, new PixelateSettings());
        Assert.Equal(3, src.Split.Frames.Count);
        Assert.True(src.Split.Frames[0].Bounds.Right < src.Split.Frames[1].Bounds.Left);

        var r = Pixelator.Run(src, new PixelateSettings());
        Assert.Equal(3, r.Frames.Count);
        Assert.All(r.Frames, f => Assert.Equal(r.Frames[0].Size, f.Size));
    }

    [Fact]
    public void GridSplitUsesCells()
    {
        var source = Render([Sprite(), Sprite()], gapCells: 2);
        var s = new PixelateSettings { Frames = new FrameSettings { Mode = FrameSplitMode.Grid, Columns = 2, Rows = 1 } };
        var src = PixelateSource.Prepare(source, s);
        Assert.Equal(2, src.Split.Frames.Count);
    }

    /// <summary>
    /// 4칸(각 40px) 시트 마스크: 0번은 팔이 칸 선(40)을 넘고 떨어진 발끝(3×8)이 다음 칸에 더 많이 걸침, 1번은 몸통만, 2번은 팔 끝 1px이 3번 몸통에 닿음.
    /// </summary>
    private static (bool[] Mask, int W, int H) CrossingSheet()
    {
        const int w = 160, h = 40;
        var m = new bool[w * h];
        void Fill(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) m[y * w + x] = true;
        }
        Fill(14, 5, 34, 35); // 0: 몸통
        Fill(34, 15, 46, 18); // 0: 팔(칸 선 40을 6px 넘음)
        Fill(37, 36, 45, 39); // 0: 떨어진 발끝(1칸에 5열, 0칸에 3열)
        Fill(54, 5, 74, 35); // 1
        Fill(94, 5, 114, 35); // 2: 몸통
        Fill(114, 18, 126, 23); // 2: 팔(칸 선 120을 넘음)
        Fill(126, 20, 127, 21); // 2: 손끝 1px — 3번 몸통(127~)에 닿음
        Fill(127, 5, 147, 35); // 3
        return (m, w, h);
    }

    /// <summary>
    /// 프레임마다 그 라벨 픽셀의 8방향 덩어리 중 가장 큰 것이 아니면서 작고(가장 큰 것의 ¼ 미만) 프레임 범위 왼쪽·오른쪽 끝에 닿은 것의 수 —
    /// 칸 선을 넘어온 이웃 스프라이트 조각.
    /// </summary>
    private static int StrayEdgePieces(FrameSplit split, int w, int h)
    {
        int stray = 0;
        for (int f = 0; f < split.Frames.Count; f++)
        {
            var b = split.Frames[f].Bounds;
            var seen = new bool[w * h];
            var pieces = new List<(int Count, int Left, int Right)>();
            for (int y = b.Top; y < b.Bottom; y++)
                for (int x = b.Left; x < b.Right; x++)
                {
                    if (seen[y * w + x] || split.Labels[y * w + x] != f) continue;
                    int n = 0, left = x, right = x;
                    var stack = new Stack<int>();
                    stack.Push(y * w + x);
                    seen[y * w + x] = true;
                    while (stack.TryPop(out int i))
                    {
                        n++;
                        int px = i % w, py = i / w;
                        left = Math.Min(left, px);
                        right = Math.Max(right, px);
                        for (int ny = Math.Max(0, py - 1); ny <= Math.Min(h - 1, py + 1); ny++)
                            for (int nx = Math.Max(0, px - 1); nx <= Math.Min(w - 1, px + 1); nx++)
                                if (!seen[ny * w + nx] && split.Labels[ny * w + nx] == f) { seen[ny * w + nx] = true; stack.Push(ny * w + nx); }
                    }
                    pieces.Add((n, left, right));
                }
            int largest = pieces.Max(p => p.Count);
            stray += pieces.Count(p => p.Count < largest / 4 && (p.Left == b.Left || p.Right == b.Right - 1));
        }
        return stray;
    }

    /// <summary>
    /// 격자 나누기는 덩어리째: 칸 선을 넘은 팔·떨어진 발끝은 제 스프라이트(0번)에, 손끝으로 맞닿은 2·3번은 가장 얇은 열(손끝)에서 갈린다.
    /// 자리는 칸 그대로, 범위는 칸 밖으로 나갈 수 있다.
    /// </summary>
    [Fact]
    public void GridSplitKeepsCrossingPiecesWithTheirSprite()
    {
        var (m, w, h) = CrossingSheet();
        var split = FrameSplitter.Run(m, w, h, new FrameSettings { Mode = FrameSplitMode.Grid, Columns = 4, Rows = 1 });
        Assert.Equal(4, split.Frames.Count);
        Assert.Equal(new Rectangle(0, 0, 40, 40), split.Frames[0].Slot);
        Assert.Equal(Rectangle.FromLTRB(14, 5, 46, 39), split.Frames[0].Bounds);
        Assert.Equal(Rectangle.FromLTRB(54, 5, 74, 35), split.Frames[1].Bounds);
        Assert.Equal(Rectangle.FromLTRB(94, 5, 126, 35), split.Frames[2].Bounds);
        Assert.Equal(Rectangle.FromLTRB(126, 5, 147, 35), split.Frames[3].Bounds);
        Assert.Equal(0, split.Labels[16 * w + 45]); // 팔 끝
        Assert.Equal(0, split.Labels[37 * w + 44]); // 발끝
        Assert.Equal(2, split.Labels[20 * w + 125]);
        Assert.Equal(0, StrayEdgePieces(split, w, h));
    }

    /// <summary>
    /// 자동 나누기: 맞닿은 2·3번은 빈 열이 없어 한 조각 → 기대 프레임 수(4)가 있으면, 없으면 다른 조각 너비 중앙값의 1.8배 규칙으로 손끝 열에서 가른다.
    /// 기대 수만큼 이미 나왔거나(3) 넓은 조각이 한 스프라이트 몸통이면(얇은 열 없음) 가르지 않는다.
    /// </summary>
    [Fact]
    public void AutoSplitSeparatesTouchingSprites()
    {
        var (m, w, h) = CrossingSheet();
        foreach (var expected in new[] { 0, 4 })
        {
            var split = FrameSplitter.Run(m, w, h, new FrameSettings { Mode = FrameSplitMode.Auto, ExpectedFrames = expected });
            Assert.Equal(4, split.Frames.Count);
            Assert.Equal(Rectangle.FromLTRB(14, 5, 46, 39), split.Frames[0].Bounds);
            Assert.Equal(Rectangle.FromLTRB(94, 5, 126, 35), split.Frames[2].Bounds);
            Assert.Equal(Rectangle.FromLTRB(126, 5, 147, 35), split.Frames[3].Bounds);
            Assert.Equal(2, split.Labels[20 * w + 125]);
            Assert.Equal(3, split.Labels[20 * w + 127]);
            Assert.Equal(0, StrayEdgePieces(split, w, h));
        }
        Assert.Equal(3, FrameSplitter.Run(m, w, h, new FrameSettings { Mode = FrameSplitMode.Auto, ExpectedFrames = 3 }).Frames.Count);

        // 넓은 스프라이트 하나(60px 통몸통)는 중앙값(20px)의 3배여도 얇은 열이 없어 그대로.
        const int ww = 160;
        var solid = new bool[ww * h];
        foreach (var (x0, x1) in new[] { (5, 25), (35, 55), (70, 130) })
            for (int y = 5; y < 35; y++)
                for (int x = x0; x < x1; x++) solid[y * ww + x] = true;
        Assert.Equal(3, FrameSplitter.Run(solid, ww, h, new FrameSettings { Mode = FrameSplitMode.Auto }).Frames.Count);
    }

    [Fact]
    public void ScoringPrefersDetectedScaleOverPlusMinusOne()
    {
        var source = Render([Sprite()]);
        var s = SixColors;
        var src = PixelateSource.Prepare(source, s);
        var weights = new ScoreWeights();
        double Score(double adjust)
        {
            var settings = s with { Grid = s.Grid with { ScaleAdjust = adjust } };
            return PixelateScoring.Score(Pixelator.Run(src, settings), weights, 6).Total;
        }
        double right = Score(0), smaller = Score(-1), larger = Score(1);
        Assert.True(right > smaller, $"{right:0.00} vs {smaller:0.00} (−1)");
        Assert.True(right > larger, $"{right:0.00} vs {larger:0.00} (+1)");
    }

    [Fact]
    public void CandidatesAreSortedAndIncludeCurrentSettings()
    {
        var source = Render([Sprite()]);
        var s = SixColors;
        var src = PixelateSource.Prepare(source, s);
        int seen = 0;
        var all = PixelateCandidates.Run(src, s, CandidateVary.Default, 6, new ScoreWeights(), _ => Interlocked.Increment(ref seen));
        Assert.Equal(6, all.Count);
        Assert.Equal(6, seen);
        Assert.Contains(all, c => c.Index == 0);
        Assert.Equal(all.Count, all.Select(c => c.Settings.Key()).Distinct().Count());
        for (int i = 1; i < all.Count; i++) Assert.True(all[i - 1].Score.Total >= all[i].Score.Total);
    }

    [Fact]
    public void CandidatesHonorCancellation()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, new PixelateSettings());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => PixelateCandidates.Run(src, new PixelateSettings(), CandidateVary.Default, 8, new ScoreWeights(), null, cts.Token));
    }

    [Fact]
    public void FixedSizeFitsSpriteInCanvas()
    {
        var source = Render([Sprite()]);
        var s = new PixelateSettings { Size = new SizeSettings { Mode = OutputSizeMode.FixedSize, Width = 16, Height = 16 } };
        var r = Pixelator.Run(source, s);
        var frame = Assert.Single(r.Frames);
        Assert.Equal(new Size(16, 16), frame.Size);
        var b = frame.GetOpaqueBounds();
        Assert.True(b.Width <= 14 && b.Height <= 14 && b.Width >= 10);
    }

    [Fact]
    public void ExistingPaletteAndKeepOriginal()
    {
        var source = Render([Sprite()]);
        var palette = new[] { "#1E140C", "#C82828", "#3CA046", "#3C50C8", "#E6C83C", "#F0BE96" };
        var r = Pixelator.Run(source, new PixelateSettings { Palette = new PaletteSettings { Method = PaletteMethod.Existing, ExistingColors = palette, SnapOutline = false } });
        Assert.All(r.Palette, c => Assert.Contains(c.ToHex(), palette));
        var keep = Pixelator.Run(source, PixelatePresets.BuiltIn(PixelateBuiltIn.KeepColors));
        Assert.True(keep.Palette.Count > 6);
    }

    [Fact]
    public void CleanPixelArtKeepsItsScale()
    {
        var sprite = Sprite(detailed: true);
        var same = Pixelator.Run(sprite, new PixelateSettings());
        Assert.Equal(1, same.DetectedScale);
        Assert.Equal(new Size(24, 24), Assert.Single(same.Frames).GetOpaqueBounds().Size);

        var x4 = new PixelImage(96, 96);
        for (int y = 0; y < 96; y++)
            for (int x = 0; x < 96; x++)
                x4[x, y] = sprite[x / 4, y / 4];
        var r = Pixelator.Run(x4, new PixelateSettings());
        Assert.Equal(4, r.DetectedScale);
        Assert.Equal(new Size(24, 24), Assert.Single(r.Frames).GetOpaqueBounds().Size);
    }

    private static PixelImage Upscale(PixelImage art, int k)
    {
        var big = new PixelImage(art.Width * k, art.Height * k);
        for (int y = 0; y < big.Height; y++)
            for (int x = 0; x < big.Width; x++)
                big[x, y] = art[x / k, y / k];
        return big;
    }

    /// <summary>JPEG 흉내: 투명을 검게, 3×3 흐림, 채널마다 ±<paramref name="noise"/> 잡음.</summary>
    private static PixelImage BlurNoise(PixelImage img, int noise = 4, int seed = 9)
    {
        var rng = new Random(seed);
        var r = new PixelImage(img.Width, img.Height);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int R = 0, G = 0, B = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!img.Contains(x + dx, y + dy)) continue;
                        var c = img[x + dx, y + dy];
                        if (c.A == 0) c = Color32.Black;
                        R += c.R; G += c.G; B += c.B; n++;
                    }
                byte N(int v) => (byte)Math.Clamp(v / n + rng.Next(-noise, noise + 1), 0, 255);
                r[x, y] = new Color32(N(R), N(G), N(B));
            }
        return r;
    }

    private static PixelateSettings AutoThin => new()
    {
        Grid = new PixelGridSettings { Source = ScaleSource.Auto },
        Cleanup = new CleanupSettings { OutlineFit = OutlineFit.Thin },
    };

    private static Size[] OpaqueSizes(PixelateResult r) => [.. r.Frames.Select(f => f.GetOpaqueBounds().Size)];

    /// <summary>
    /// 깨끗한 그림(1px 무늬 스프라이트, 넓은 칠에 1px 외곽선인 Unity 표본 player@2x)을 최근접으로 k배 키우면 칸 크기는 정확히 k이고 결과는 1배 그림을
    /// 그대로 넣은 것과 같은 크기 — 외곽선 방식(기본)·자동 모두. 1배 그림은 칸 크기 1, 프레임 크기 그대로(넓은 칠 1배 그림이 5.75px로 줄던 회귀).
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void NearestUpscaledCleanArtGetsExactScale(int k)
    {
        foreach (var art in new[] { Sprite(detailed: true), PngIO.Load(TestUtil.Fixture("player@2x.png")) })
            foreach (var s in new[] { new PixelateSettings(), AutoThin })
            {
                var at1 = Pixelator.Run(art, s);
                Assert.Equal(1, at1.DetectedScale);
                var src1 = PixelateSource.Prepare(art, s);
                Assert.Equal(src1.Split.Frames.Select(f => f.Bounds.Size), OpaqueSizes(at1));
                var r = Pixelator.Run(Upscale(art, k), s);
                Assert.Equal(k, r.DetectedScale);
                Assert.Equal(OpaqueSizes(at1), OpaqueSizes(r));
            }
    }

    /// <summary>우리 결과(1배 픽셀 그림)를 다시 넣으면 칸 크기 1, 크기 그대로.</summary>
    [Fact]
    public void OwnResultFedBackKeepsScaleAndSize()
    {
        var first = Pixelator.Run(PngIO.Load(TestUtil.Fixture("ai-girl-bg.png")), new PixelateSettings());
        var result = Assert.Single(first.Frames);
        foreach (var s in new[] { new PixelateSettings(), AutoThin })
        {
            var again = Pixelator.Run(result, s);
            Assert.Equal(1, again.DetectedScale);
            Assert.Equal(result.GetOpaqueBounds().Size, Assert.Single(again.Frames).GetOpaqueBounds().Size);
        }
    }

    /// <summary>
    /// 우리 결과를 16배로 키우고 흐림·잡음을 더하면(외곽선이 검은 바탕에 붙어 1px 번진 테두리만 외곽선으로 잡힘) 외곽선 방식도 칸 크기 ~16 —
    /// 외곽선 두께 1px(경계 일치 0)로 줄여 결과가 16배 커지던 회귀.
    /// </summary>
    [Fact]
    public void ThinOutlineDoesNotShrinkUpscaledResultToHairline()
    {
        var result = Assert.Single(Pixelator.Run(PngIO.Load(TestUtil.Fixture("ai-girl-bg.png")), new PixelateSettings()).Frames);
        var image = BlurNoise(Upscale(result, 16));
        var src = PixelateSource.Prepare(image, new PixelateSettings());
        var est = src.EstimatedScale(-1, 0.15, ScaleSource.Outline, 0.24);
        Assert.True(est.ThinOutline, $"outline {est.Outline} ({est.OutlineMatch:0.00}) used {est.Used} {est.Scale}");
        var r = Pixelator.Run(src, new PixelateSettings());
        Assert.InRange(r.Scale, 15, 17);
        Assert.Equal(result.GetOpaqueBounds().Size, Assert.Single(r.Frames).GetOpaqueBounds().Size);
    }

    /// <summary>큰 단색 덩어리(8×6칸, 6색) + 칸 안 작은 점(2~6px) + 채널마다 ±6 잡음.</summary>
    private static PixelImage FlatBlocks(int block, int seed = 3)
    {
        var rng = new Random(seed);
        var colors = new[] { new Color32(70, 130, 140), new Color32(200, 150, 90), new Color32(240, 200, 170), new Color32(110, 80, 150), new Color32(40, 50, 60), new Color32(220, 90, 70) };
        var img = new PixelImage(8 * block, 6 * block);
        for (int cy = 0; cy < 6; cy++)
            for (int cx = 0; cx < 8; cx++)
                img.FillRect(new Rectangle(cx * block, cy * block, block, block), colors[rng.Next(colors.Length)]);
        for (int k = 0; k < 96; k++)
        {
            int x = rng.Next(img.Width - 8), y = rng.Next(img.Height - 8);
            var c = img[x, y];
            img.FillRect(new Rectangle(x, y, 2 + rng.Next(5), 2 + rng.Next(5)), new Color32((byte)Math.Min(255, c.R + 40), (byte)Math.Min(255, c.G + 30), (byte)Math.Min(255, c.B + 20)));
        }
        for (int i = 0; i < img.Pixels.Length; i++)
        {
            var c = img.Pixels[i];
            byte N(int v) => (byte)Math.Clamp(v + rng.Next(-6, 7), 0, 255);
            img.Pixels[i] = new Color32(N(c.R), N(c.G), N(c.B));
        }
        return img;
    }

    /// <summary>64~128px 큰 덩어리에 잡음·작은 점이 있어도 칸 크기는 덩어리 크기(±10%) — 후보가 48px까지라 4~8px로 잘게 자르던 회귀.</summary>
    [Theory]
    [InlineData(64)]
    [InlineData(100)]
    [InlineData(128)]
    public void LargeFlatBlocksWithNoiseKeepBlockSize(int block)
    {
        var image = FlatBlocks(block);
        foreach (var s in new[] { new PixelateSettings(), AutoThin })
        {
            var r = Pixelator.Run(image, s);
            Assert.InRange(r.Scale, block * 0.9, block * 1.1);
            Assert.Equal(new Size(8, 6), Assert.Single(r.Frames).GetOpaqueBounds().Size);
        }
    }

    /// <summary>'현재 레이어 픽셀로 정리'처럼 프레임(칸) 넷을 가로로 이어 격자로 나눠도 1배·8배 그림의 칸 크기와 프레임 크기가 그대로.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void LayerFramesJoinedWithGridSplitKeepScale(int k)
    {
        var art = Upscale(Sprite(detailed: true), k);
        int cel = art.Width + 4 * k;
        var image = new PixelImage(cel * 4, art.Height + 2 * k);
        for (int i = 0; i < 4; i++) image.Blit(art, i * cel + k * (1 + i % 2), k);
        var s = new PixelateSettings { Frames = new FrameSettings { Mode = FrameSplitMode.Grid, Columns = 4, Rows = 1 } };
        var r = Pixelator.Run(image, s);
        Assert.Equal(k, r.DetectedScale);
        Assert.All(OpaqueSizes(r), size => Assert.Equal(new Size(24, 24), size));
    }

    /// <summary>
    /// 결과 블록 반복도: 맞는 칸 크기의 결과는 낮고(가장자리 칸 대부분이 1칸 계단·외곽선), 칸을 진짜 칸의 ½로 고른 결과는 1에 가깝다 —
    /// 디더링을 켜도(결과 색이 아니라 칸마다 원본 평균 색으로 재므로) 그대로이고, 점수는 맞는 크기를 더 높게 본다.
    /// </summary>
    [Fact]
    public void BlockRepetitionFlagsTooSmallCells()
    {
        var s = new PixelateSettings();
        var src = PixelateSource.Prepare(PngIO.Load(TestUtil.Fixture("ai-sword-boy.png")), s);
        var right = Pixelator.Run(src, s);
        Assert.InRange(right.Metrics.BlockRepetition, 0, 0.4);
        var half = s with { Grid = s.Grid with { ScaleAdjust = -right.DetectedScale / 2 } };
        foreach (var dither in new[] { Gpixel.Core.Edit.PaletteDither.None, Gpixel.Core.Edit.PaletteDither.Bayer2 })
        {
            var r = Pixelator.Run(src, half with { Palette = half.Palette with { Dither = dither } });
            Assert.InRange(r.Metrics.BlockRepetition, 0.9, 1);
        }
        var w = new ScoreWeights();
        var score = PixelateScoring.Score(Pixelator.Run(src, half), w, right.ResolvedColors);
        Assert.True(PixelateScoring.Score(right, w, right.ResolvedColors).Total > score.Total);
        Assert.InRange(score.Parts.Single(p => p.Term == ScoreTerm.BlockRepetition).Badness, 0.8, 1);
    }

    /// <summary>
    /// 어림이 진짜 칸의 ½이어도(색 경계 방식은 16배로 키운 결과에서 8px — 2칸 구간도 꼭 맞으므로 절반을 고름) 자동 최적화는 2배 후보에서 시작해
    /// 진짜 크기를 찾는다 — 예전에는 시작 크기 ±25% 안에 갇혔고, 점수도 칸이 작은 쪽을 조금 더 높게 봤다(결과 블록 반복도가 없을 때).
    /// </summary>
    [Fact]
    public void OptimizerEscapesHalfScaleEstimate()
    {
        var result = Assert.Single(Pixelator.Run(PngIO.Load(TestUtil.Fixture("ai-girl-bg.png")), new PixelateSettings()).Frames);
        var image = BlurNoise(Upscale(result, 16));
        var start = new PixelateSettings
        {
            Grid = new PixelGridSettings { Source = ScaleSource.ColorEdges },
            Cleanup = new CleanupSettings { OutlineFit = OutlineFit.Thin },
        };
        var src = PixelateSource.Prepare(image, start);
        var r = PixelateOptimizer.Run(src, start, new PixelateOptimizeOptions { Params = OptimizeParam.Scale, MaxTrials = 40, MaxTime = TimeSpan.FromSeconds(60) });
        Assert.InRange(r.Start.Result.Scale, 7, 9);
        Assert.InRange(r.Best.Result.Scale, 15, 17);
        Assert.Equal(result.GetOpaqueBounds().Size, r.Best.Result.Frames[0].GetOpaqueBounds().Size);
    }

    [Fact]
    public void PresetsRoundTrip()
    {
        var presets = new List<PixelatePreset>
        {
            new("small", PixelatePresets.BuiltIn(PixelateBuiltIn.Small32)),
            new("custom", new PixelateSettings
            {
                Background = new BackgroundSettings { Mode = BackgroundMode.PickColor, Color = "#102030", Tolerance = 33, AutoTolerance = false },
                Grid = new PixelGridSettings { Mode = GridMode.Manual, Scale = 7.5, OffsetX = 2, OffsetY = 3 },
                Palette = new PaletteSettings { Method = PaletteMethod.Existing, ExistingColors = ["#000000", "#FFFFFF"], ExistingName = "BW", Dither = Gpixel.Core.Edit.PaletteDither.Bayer2 },
                Cleanup = new CleanupSettings { Outline = OutlineMode.EnsureClosed, ReduceJaggies = true },
            }),
        };
        using var dir = new TempDir();
        var path = dir.File(PixelatePresets.FileName);
        PixelatePresets.Save(path, presets);
        var loaded = PixelatePresets.Load(path);
        Assert.Equal(presets.Select(p => p.Name), loaded.Select(p => p.Name));
        for (int i = 0; i < presets.Count; i++) Assert.Equal(presets[i].Settings.Key(), loaded[i].Settings.Key());

        Assert.Empty(PixelatePresets.Parse("not json"));
        var partial = PixelatePresets.ParseSettings("{\"grid\":{\"scale\":5}}");
        Assert.NotNull(partial);
        Assert.Equal(5, partial!.Grid.Scale);
        Assert.Equal(new PaletteSettings().Method, partial.Palette.Method);
    }

    [Fact]
    public void SampleSheetFindsFourFramesQuickly()
    {
        var image = PngIO.Load(TestUtil.Fixture("ai-walk-sample.png"));
        var sw = Stopwatch.StartNew();
        var r = Pixelator.Run(image, new PixelateSettings());
        sw.Stop();
        Assert.Equal(4, r.Frames.Count);
        // 파일은 2172×724(보이는 크기 1568×523의 약 1.39배)다. 색 경계는 8px 안팎이지만 외곽선이 2칸 두께(약 16px)라,
        // 기본(외곽선 1px 맞추기 = 크기를 줄여서)은 칸 크기를 외곽선 두께에 맞춰 줄인다.
        Assert.InRange(r.Scale, 13, 19);
        int edge = 0, thick = 0;
        foreach (var f in r.Frames)
        {
            var (e, t) = PixelCleanup.OutlineThickness(f, 0.24);
            edge += e;
            thick += t;
        }
        Assert.True(1 - thick / (double)edge >= 0.9, $"{thick}/{edge}");
        Assert.All(r.Frames, f => Assert.Equal(r.Frames[0].Size, f.Size));
        Assert.InRange(r.Palette.Count, 8, 32);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
    }

    /// <summary>
    /// 일부러 틀린 시작(자동 격자에 칸 크기 −2px, 색 4)에서 칸 크기·색 수를 맞게 찾는다. 자동 격자는 7~9px로 들쭉날쭉한 경계를 따라가므로
    /// 맞는 답(칸 약 8px, 6색)이 하나로 정해진다 — 수동(고른 간격) 격자는 이 그림의 어떤 칸 크기로도 경계와 맞지 않아 정답이 없다.
    /// </summary>
    [Fact]
    public void OptimizerFixesWrongScaleAndColors()
    {
        var source = Render([Sprite()]);
        var start = new PixelateSettings
        {
            Grid = new PixelGridSettings { ScaleAdjust = -2 },
            Palette = new PaletteSettings { Colors = 4, ColorSource = PaletteColorSource.Mean },
        };
        var src = PixelateSource.Prepare(source, start);
        int progress = 0;
        var r = PixelateOptimizer.Run(src, start,
            new PixelateOptimizeOptions { Params = OptimizeParam.Scale | OptimizeParam.Colors, MaxTrials = 120, MaxTime = TimeSpan.FromSeconds(30) },
            _ => Interlocked.Increment(ref progress));
        Assert.Equal(r.Trials.Count, progress);
        Assert.True(r.Best.Score.Total > r.Start.Score.Total, $"{r.Start.Score.Total} → {r.Best.Score.Total}");
        Assert.InRange(r.Best.Result.Scale, 7, 9);
        Assert.InRange(r.Best.Result.Palette.Count, 5, 7);
        Assert.Equal(r.Best.Settings.Key(), r.Top(1)[0].Settings.Key());
        Assert.Equal(GridMode.Auto, r.Best.Settings.Grid.Mode);
        Assert.Equal(OutlineFit.Shrink, r.Best.Settings.Cleanup.OutlineFit); // 고르지 않은 항목은 그대로
    }

    [Fact]
    public void OptimizerRespectsTrialAndTimeBudgets()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        var few = PixelateOptimizer.Run(src, SixColors, new PixelateOptimizeOptions { MaxTrials = 10 });
        Assert.True(few.Trials.Count <= 10, few.Trials.Count.ToString());
        Assert.Equal(OptimizeStop.Trials, few.StoppedReason);

        // 작은 합성 그림은 빠른 컴퓨터에서 금방 끝나므로 시간 예산을 아주 짧게(시작 설정 평가 뒤 바로 멈춤).
        var quick = PixelateOptimizer.Run(src, SixColors, new PixelateOptimizeOptions { Params = OptimizeParam.All, MaxTrials = 10000, MaxTime = TimeSpan.FromMilliseconds(1) });
        Assert.Equal(OptimizeStop.Time, quick.StoppedReason);
        Assert.True(quick.Elapsed < TimeSpan.FromSeconds(3), quick.Elapsed.ToString());
        Assert.NotEmpty(quick.Trials);
    }

    [Fact]
    public void OptimizerCancellation()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        using (var pre = new CancellationTokenSource())
        {
            pre.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => PixelateOptimizer.Run(src, SixColors, new PixelateOptimizeOptions(), null, pre.Token));
        }
        // 시작 설정을 돌린 뒤 취소하면 그때까지의 결과를 돌려준다.
        using var cts = new CancellationTokenSource();
        var r = PixelateOptimizer.Run(src, SixColors, new PixelateOptimizeOptions { Params = OptimizeParam.All }, _ => cts.Cancel(), cts.Token);
        Assert.Equal(OptimizeStop.Cancelled, r.StoppedReason);
        Assert.NotEmpty(r.Trials);
        Assert.True(r.Trials.Count < 40, r.Trials.Count.ToString());
    }

    [Fact]
    public void OptimizerNeverRunsTheSameSettingsTwice()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        // 칸 색 방식만: 시작 + 나머지 3방식 = 4번. 1단계의 시작 값과 다듬기의 이웃은 모두 이미 돌린 설정.
        var r = PixelateOptimizer.Run(src, SixColors, new PixelateOptimizeOptions { Params = OptimizeParam.DownsampleMode });
        Assert.Equal(4, r.Trials.Count);
        Assert.Equal(4, r.Trials.Select(t => t.Settings.Key()).Distinct().Count());
        Assert.True(r.CacheHits > 0);
        Assert.Equal(OptimizeStop.Done, r.StoppedReason);
        Assert.Equal(4, r.Top(10).Count);
        Assert.InRange(r.Top(10, distinctOutput: true).Count, 1, 4);
        Assert.Equal(r.Top(1)[0].Settings.Key(), r.Top(1, distinctOutput: true)[0].Settings.Key());
    }

    private static readonly PixelateSettings WrongStart = new()
    {
        Grid = new PixelGridSettings { ScaleAdjust = -2 },
        Palette = new PaletteSettings { Colors = 4, ColorSource = PaletteColorSource.Mean },
    };

    /// <summary>
    /// 다시 돌리기: 첫 돌림이 '더 나아지지 않음'으로 끝나면 1위에서 다시 돈다. 같은 예산에서 한 번만 돈 결과 이상이고, 첫 돌림은 한 번만 돈 실행과 같으며,
    /// 돌림은 앞 돌림의 끝 점수에서 시작하고, 돌림별 시도 수의 합 = 전체 시도 수. MinPassGain 미만 나아진 돌림은 StalePasses번 넘게 이어지지 않고, 마지막까지
    /// StalePasses + 1번 이어져 멈추거나 새 시도가 없는 돌림에서 멈춘다. 진행 알림의 돌림 번호는 1부터 돌림 수까지.
    /// </summary>
    [Fact]
    public void OptimizerRestartsFromBestAndBeatsSinglePass()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, WrongStart);
        var options = new PixelateOptimizeOptions { Params = OptimizeParam.Scale | OptimizeParam.Colors | OptimizeParam.DownsampleMode, MaxTrials = 1000, MaxTime = TimeSpan.FromMinutes(5) };
        var single = PixelateOptimizer.Run(src, WrongStart, options with { MaxPasses = 1 });
        var passes = new System.Collections.Concurrent.ConcurrentBag<int>();
        var multi = PixelateOptimizer.Run(src, WrongStart, options, p => passes.Add(p.Pass));

        Assert.Single(single.Passes);
        Assert.Equal(OptimizeStop.Done, single.StoppedReason);
        Assert.Equal(OptimizeStop.Done, multi.StoppedReason);
        Assert.True(multi.Passes.Count >= 2, multi.Passes.Count.ToString());
        Assert.True(multi.Best.Score.Total >= single.Best.Score.Total, $"{single.Best.Score.Total} → {multi.Best.Score.Total}");
        Assert.Equal(single.Passes[0], multi.Passes[0]);
        Assert.Equal(multi.Start.Score.Total, multi.Passes[0].StartScore);
        int stale = 0;
        for (int i = 0; i < multi.Passes.Count; i++)
        {
            Assert.Equal(i + 1, multi.Passes[i].Number);
            if (i > 0) Assert.Equal(multi.Passes[i - 1].EndScore, multi.Passes[i].StartScore);
            stale = multi.Passes[i].EndScore - multi.Passes[i].StartScore < PixelateOptimizer.MinPassGain ? stale + 1 : 0;
            if (i < multi.Passes.Count - 1) Assert.True(stale <= PixelateOptimizer.StalePasses, $"pass {i + 1}: {stale} stale");
        }
        Assert.True(stale == PixelateOptimizer.StalePasses + 1 || (stale > 0 && multi.Passes[^1].Trials == 0), $"{stale} stale, last {multi.Passes[^1].Trials} trials");
        Assert.Equal(multi.Best.Score.Total, multi.Passes[^1].EndScore);
        Assert.Equal(multi.Trials.Count, multi.Passes.Sum(p => p.Trials));
        Assert.Equal(multi.Trials.Count, passes.Count);
        Assert.Contains(1, passes);
        Assert.All(passes, p => Assert.InRange(p, 1, multi.Passes.Count));
    }

    /// <summary>
    /// 이미 최적화 1위인 설정에서 다시 돌리면 첫 돌림은 나아지지 않는다(지역 최적). 그래도 바로 멈추지 않고 다른 씨앗(아직 씨앗이 아니었던 상위 시도, 더 크게 흔든 1위)으로
    /// 돌림을 더 돌아 첫 돌림보다 많은 시도를 쓴다(예전: 첫 돌림에서 멈춤 — 마법사 꼬마 시트 2000번 예산에 216번). 같은 입력이면 스레드 수와 상관없이 같은 돌림·시도.
    /// </summary>
    [Fact]
    public void OptimizerKeepsSearchingWhenFirstPassGainsNothing()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, WrongStart);
        var options = new PixelateOptimizeOptions
        {
            Params = OptimizeParam.Scale | OptimizeParam.Colors | OptimizeParam.DownsampleMode | OptimizeParam.BorderFraction | OptimizeParam.AlphaThreshold,
            MaxTrials = 2000,
            MaxTime = TimeSpan.FromMinutes(5),
        };
        var best = PixelateOptimizer.Run(src, WrongStart, options).Best.Settings;
        var again = PixelateOptimizer.Run(src, best, options);
        Assert.Equal(OptimizeStop.Done, again.StoppedReason);
        Assert.True(again.Passes[0].EndScore - again.Passes[0].StartScore < PixelateOptimizer.MinPassGain, $"{again.Passes[0].StartScore} → {again.Passes[0].EndScore}");
        Assert.True(again.Passes.Count >= 2, again.Passes.Count.ToString());
        Assert.True(again.Trials.Count > again.Passes[0].Trials, $"{again.Trials.Count} / first pass {again.Passes[0].Trials}");

        var serial = PixelateOptimizer.Run(PixelateSource.Prepare(source, WrongStart), best, options with { MaxDegreeOfParallelism = 1 });
        Assert.Equal(again.Passes, serial.Passes);
        Assert.Equal(again.Trials.Select(t => (t.Candidate.Index, t.Settings.Key(), t.Score.Total)), serial.Trials.Select(t => (t.Candidate.Index, t.Settings.Key(), t.Score.Total)));
    }

    /// <summary>시도 수 예산은 모든 돌림을 합친 것: 한 번만 돈 시도 수 + 5로 묶으면 둘째 돌림에서 정확히 예산만큼 쓰고 멈춘다.</summary>
    [Fact]
    public void OptimizerTrialBudgetCoversAllPasses()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, WrongStart);
        var options = new PixelateOptimizeOptions { Params = OptimizeParam.Scale | OptimizeParam.Colors | OptimizeParam.DownsampleMode, MaxTrials = 1000, MaxTime = TimeSpan.FromMinutes(5) };
        int first = PixelateOptimizer.Run(src, WrongStart, options with { MaxPasses = 1 }).Trials.Count;
        var r = PixelateOptimizer.Run(src, WrongStart, options with { MaxTrials = first + 5 });
        Assert.Equal(OptimizeStop.Trials, r.StoppedReason);
        Assert.Equal(first + 5, r.Trials.Count);
        Assert.Equal(2, r.Passes.Count);
        Assert.Equal(first, r.Passes[0].Trials);
        Assert.Equal(5, r.Passes[1].Trials);
    }

    /// <summary>
    /// 결정성: 같은 입력·설정이면 스레드 수(1 또는 여러)와 실행 순서에 상관없이 시도 목록(순서·번호·점수)·1위·돌림이 같다.
    /// 시도 수 예산으로 끝나게 하고 시간은 넉넉히(시간 예산으로 끝나면 끊기는 곳이 정해지지 않음).
    /// </summary>
    [Fact]
    public void OptimizerIsDeterministic()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, WrongStart);
        var options = new PixelateOptimizeOptions { Params = OptimizeParam.SelectAll, MaxTrials = 160, MaxTime = TimeSpan.FromMinutes(5) };
        var a = PixelateOptimizer.Run(src, WrongStart, options);
        var b = PixelateOptimizer.Run(src, WrongStart, options);
        var c = PixelateOptimizer.Run(PixelateSource.Prepare(source, WrongStart), WrongStart, options with { MaxDegreeOfParallelism = 1 });
        Assert.NotEqual(OptimizeStop.Time, a.StoppedReason);
        foreach (var r in new[] { b, c })
        {
            Assert.Equal(a.StoppedReason, r.StoppedReason);
            Assert.Equal(a.Best.Settings.Key(), r.Best.Settings.Key());
            Assert.Equal(a.Best.Score.Total, r.Best.Score.Total);
            Assert.Equal(a.Passes, r.Passes);
            Assert.Equal(a.Trials.Select(t => (t.Candidate.Index, t.Settings.Key(), t.Score.Total)), r.Trials.Select(t => (t.Candidate.Index, t.Settings.Key(), t.Score.Total)));
            Assert.Equal(a.Top(10, distinctOutput: true).Select(t => t.Settings.Key()), r.Top(10, distinctOutput: true).Select(t => t.Settings.Key()));
        }
    }

    /// <summary>팔레트 색·팔레트 방식·가장자리 칸 가중·외곽선 색 모으기를 바꿔 본다(팔레트 색은 기본으로 켜짐, 기존 팔레트·원래 색 유지면 팔레트 항목은 건너뜀).</summary>
    [Fact]
    public void OptimizerSearchesPaletteColorSourceMethodEdgeWeightAndSnap()
    {
        Assert.True(OptimizeParam.Default.HasFlag(OptimizeParam.PaletteColorSource));
        Assert.False(OptimizeParam.Default.HasFlag(OptimizeParam.SnapOutline));
        Assert.False(OptimizeParam.Default.HasFlag(OptimizeParam.PaletteMethod));
        Assert.False(OptimizeParam.Default.HasFlag(OptimizeParam.EdgeWeight));

        var source = Render([RobeSprite()]);
        var start = new PixelateSettings { Palette = new PaletteSettings { Colors = 4 } };
        var src = PixelateSource.Prepare(source, start);
        var p = OptimizeParam.PaletteColorSource | OptimizeParam.PaletteMethod | OptimizeParam.EdgeWeight | OptimizeParam.SnapOutline;
        var r = PixelateOptimizer.Run(src, start, new PixelateOptimizeOptions { Params = p, MaxTrials = 60 });
        var tried = r.Trials.Select(t => t.Settings.Palette).ToList();
        Assert.Contains(tried, s => s.ColorSource == PaletteColorSource.Mean);
        Assert.Contains(tried, s => s.Method == PaletteMethod.MedianCut);
        Assert.Contains(tried, s => !s.SnapOutline);
        Assert.True(tried.Select(s => s.EdgeWeight).Distinct().Count() >= 3);
        Assert.All(tried, s => Assert.InRange(s.EdgeWeight, 0, 1));
        Assert.Contains("palette ", PixelateOptimizer.Summary(r.Best.Settings, r.Best.Result, p));
        // 평균 색이 흰 옷을 회색으로 끌므로 고른 것은 원본 색.
        Assert.Equal(PaletteColorSource.Original, r.Best.Settings.Palette.ColorSource);

        foreach (var method in new[] { PaletteMethod.KeepOriginal, PaletteMethod.Existing })
        {
            var other = start with { Palette = start.Palette with { Method = method, ExistingColors = ["#000000", "#FFFFFF"] } };
            var none = PixelateOptimizer.Run(src, other, new PixelateOptimizeOptions { Params = p & ~OptimizeParam.SnapOutline });
            Assert.Single(none.Trials);
        }
    }

    private static readonly Color32 SpriteRed = new(200, 40, 40);

    private static Color32 Nearest(IEnumerable<Color32> colors, Color32 to, ColorMetric? metric = null)
        => colors.OrderBy(c => (metric ?? ColorMetric.OkLab).Distance(c, to)).First();

    /// <summary>뺀 결과가 색 빼기 전 결과의 뒤처리인지: 뺀 색 픽셀만 원본 칸 색에 (팔레트의 색 거리로) 가장 가까운 남은 색으로, 다른 픽셀·측정값은 그대로, 새 색 없음.</summary>
    private static void AssertPostProcessed(PixelateResult baseline, PixelateResult r, IReadOnlyCollection<Color32> removed)
    {
        Assert.Equal(baseline.Palette.Count - removed.Count, r.Palette.Count);
        Assert.All(r.Palette, c => Assert.Contains(c, baseline.Palette));
        Assert.All(removed, c => Assert.DoesNotContain(c, r.Palette));
        Assert.Equal(baseline.Metrics, r.Metrics);
        var kept = r.Palette.ToList();
        var metric = ColorMetric.For(r.Settings.Palette);
        for (int f = 0; f < r.Frames.Count; f++)
        {
            var before = baseline.Frames[f].Pixels;
            var after = r.Frames[f].Pixels;
            var source = r.SourceColors[f].Pixels;
            Assert.Equal(before.Length, after.Length);
            for (int i = 0; i < before.Length; i++)
            {
                if (!removed.Contains(before[i])) { Assert.Equal(before[i], after[i]); continue; }
                var from = source[i].A != 0 ? source[i] : before[i];
                Assert.Equal(Nearest(kept, from, metric), after[i]);
            }
        }
    }

    [Fact]
    public void ExcludedColorIsReplacedByNearestRemainingToSourceCell()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        var baseline = Pixelator.Run(src, SixColors);
        var red = Nearest(baseline.Palette, SpriteRed);
        Assert.True(PaletteEdits.Matches(red, SpriteRed), red.ToHex());

        var s = SixColors with { Palette = SixColors.Palette with { ExcludedColors = PaletteEdits.Toggle(SixColors.Palette.ExcludedColors, red) } };
        Assert.Equal([red.ToHex()], s.Palette.ExcludedColors);
        var r = Pixelator.Run(src, s);
        AssertPostProcessed(baseline, r, [red]);
        // 다시 돌리지 않고 뺀 것과 같다.
        Assert.Equal(r.Frames.Select(f => f.Pixels), Pixelator.WithExclusions(baseline, s.Palette.ExcludedColors).Frames.Select(f => f.Pixels));

        // 다시 누르면(살짝 다른 색이어도 같은 색으로 맞음) 되살아나 처음 결과 그대로.
        var restored = s with { Palette = s.Palette with { ExcludedColors = PaletteEdits.Toggle(s.Palette.ExcludedColors, new Color32((byte)(red.R + 2), (byte)(red.G - 1), red.B)) } };
        Assert.Empty(restored.Palette.ExcludedColors);
        Assert.Equal(SixColors.Key(), restored.Key());
        Assert.Equal(Assert.Single(baseline.Frames).Pixels, Assert.Single(Pixelator.Run(src, restored).Frames).Pixels);
        var back = Pixelator.WithExclusions(r, []);
        Assert.Equal(Assert.Single(baseline.Frames).Pixels, Assert.Single(back.Frames).Pixels);
        Assert.Equal(baseline.Palette, back.Palette);
        Assert.Empty(back.ExcludedFrames);
    }

    [Fact]
    public void ExcludingColorsDropsCountByExactlyThatManyOnAiImage()
    {
        // 자동 색 수(팔꿈치·넓게 쓰인 색 지키기)여도 색 빼기는 뒤처리라 다른 색이 생기지 않는다: 1개 → −1, 2개 → −2, 되살리면 처음 그대로.
        var image = PngIO.Load(TestUtil.Fixture("ai-mage.png"));
        var s = new PixelateSettings();
        var src = PixelateSource.Prepare(image, s);
        var baseline = Pixelator.Run(src, s);
        var byUse = baseline.Palette.OrderByDescending(c => baseline.Frames.Sum(f => f.Pixels.Count(p => p == c))).ToList();
        Assert.True(byUse.Count > 6, byUse.Count.ToString());
        var one = Pixelator.Run(src, s with { Palette = s.Palette with { ExcludedColors = [byUse[2].ToHex()] } });
        AssertPostProcessed(baseline, one, [byUse[2]]);
        var two = Pixelator.Run(src, s with { Palette = s.Palette with { ExcludedColors = [byUse[2].ToHex(), byUse[5].ToHex()] } });
        AssertPostProcessed(baseline, two, [byUse[2], byUse[5]]);
        var restored = Pixelator.WithExclusions(two, []);
        Assert.Equal(baseline.Frames.Select(f => f.Pixels), restored.Frames.Select(f => f.Pixels));
        Assert.Equal(baseline.Palette, restored.Palette);
    }

    [Fact]
    public void ExclusionSurvivesSlightlyDifferentPalettes()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        var red = Nearest(Pixelator.Run(src, SixColors).Palette, SpriteRed);
        // 평균 색이면 빨강이 조금 달라진다 — 그래도 거리로 맞춰 뺀다.
        var mean = SixColors with { Palette = SixColors.Palette with { ColorSource = PaletteColorSource.Mean } };
        var meanRed = Nearest(Pixelator.Run(src, mean).Palette, SpriteRed);
        Assert.True(PaletteEdits.Matches(meanRed, red), $"{meanRed.ToHex()} {red.ToHex()}");
        var excluded = new[] { red.ToHex() };
        foreach (var s in new[] { mean, SixColors with { Palette = SixColors.Palette with { Colors = 7 } } })
        {
            var r = Pixelator.Run(src, s with { Palette = s.Palette with { ExcludedColors = excluded } });
            Assert.DoesNotContain(r.Palette, c => PaletteEdits.Matches(c, red));
            Assert.NotEmpty(r.Palette);
        }

        // 자동 맞추기는 빼기를 바꾸지 않는다(모든 시도·가장 좋은 결과에 그대로).
        var start = SixColors with { Palette = SixColors.Palette with { ExcludedColors = excluded } };
        var opt = PixelateOptimizer.Run(src, start, new PixelateOptimizeOptions { MaxTrials = 12 });
        Assert.All(opt.Trials, t => Assert.Equal(excluded, t.Settings.Palette.ExcludedColors));
        Assert.DoesNotContain(opt.Best.Result.Palette, c => PaletteEdits.Matches(c, red));
    }

    [Fact]
    public void ExclusionAlwaysKeepsOneColor()
    {
        Color32 a = new(200, 40, 40), b = new(40, 160, 60), c = new(40, 60, 200);
        // 모두 빼면 마지막 빼기(c)를 무시한다.
        Assert.Equal(new[] { a.Argb, b.Argb }.Order(), PaletteEdits.Removed([a, b, c], [a.ToHex(), b.ToHex(), c.ToHex()], []).Order());
        Assert.Empty(PaletteEdits.Removed([a, b], [], []));
        Assert.Equal([a.Argb], PaletteEdits.Removed([a, b, c], [a.ToHex()], []));
        // 고정 색은 빠지지 않는다.
        Assert.Empty(PaletteEdits.Removed([a, b, c], [a.ToHex()], [a.ToHex()]));

        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        var all = Pixelator.Run(src, SixColors).Palette.Select(x => x.ToHex()).ToList();
        var r = Pixelator.Run(src, SixColors with { Palette = SixColors.Palette with { ExcludedColors = all } });
        var only = Assert.Single(r.Palette);
        Assert.Equal(all[^1], only.ToHex());
    }

    [Fact]
    public void ExclusionRecordsReplacedPixels()
    {
        // 바꾼 픽셀에만 원래 색(알파 포함), 나머지·바뀐 것이 없는 그림은 비어 있음. 원본 칸 색이 없으면 그 픽셀 색에 가장 가까운 남은 색.
        Color32 a = new(200, 40, 40), b = new(40, 160, 60), c = new(40, 60, 200);
        var one = new PixelImage(3, 2);
        one[0, 0] = a;
        one[1, 0] = b;
        one[2, 0] = a.WithAlpha(128);
        one[0, 1] = c;
        var two = new PixelImage(2, 1);
        two[0, 0] = b;
        var replaced = new PixelImage?[2];
        Assert.Equal(2, PaletteEdits.Apply([one, two], null, [a.ToHex()], [], replaced));
        var mask = Assert.IsType<PixelImage>(replaced[0]);
        Assert.Equal(one.Size, mask.Size);
        Assert.Equal([a, default, a.WithAlpha(128), default, default, default], mask.Pixels);
        Assert.Null(replaced[1]);
        Assert.Equal(Nearest([b, c], a), one[0, 0]);
        // 원본 칸 색이 있으면 뺀 색이 아니라 그 색에 가장 가까운 남은 색으로(파랑 칸 → 파랑).
        var three = new PixelImage(1, 1);
        three[0, 0] = a;
        var source = new PixelImage(1, 1);
        source[0, 0] = new Color32(60, 70, 190);
        var others = new PixelImage(2, 1);
        others[0, 0] = b;
        others[1, 0] = c;
        Assert.Equal(1, PaletteEdits.Apply([three, others], [source, new PixelImage(2, 1)], [a.ToHex()], []));
        Assert.Equal(c, three[0, 0]);

        // 정리 결과에서는 결과 프레임과 같은 자리: 뺀 색이 있던 자리가 처음 결과의 빨강 자리와 같다.
        var src = PixelateSource.Prepare(Render([Sprite()]), SixColors);
        var baseline = Pixelator.Run(src, SixColors);
        Assert.Empty(baseline.ExcludedFrames);
        var red = Nearest(baseline.Palette, SpriteRed);
        var r = Pixelator.Run(src, SixColors with { Palette = SixColors.Palette with { ExcludedColors = [red.ToHex()] } });
        var before = Assert.Single(baseline.Frames);
        var excluded = Assert.Single(r.ExcludedFrames);
        Assert.Equal(Assert.Single(r.Frames).Size, excluded.Size);
        int wasRed = 0;
        for (int i = 0; i < excluded.Pixels.Length; i++)
        {
            if (before.Pixels[i] == red) wasRed++;
            Assert.Equal(before.Pixels[i] == red ? red : default, excluded.Pixels[i]);
        }
        Assert.True(wasRed > 20, wasRed.ToString());
    }

    [Fact]
    public void ExcludedAndPinnedColorsRoundTripJson()
    {
        var s = SixColors with { Palette = SixColors.Palette with { ExcludedColors = ["#C82828", "#3CA046"], PinnedColors = ["#F0BE96"] } };
        Assert.NotEqual(SixColors.Key(), s.Key());
        var back = PixelatePresets.ParseSettings(PixelatePresets.SerializeSettings(s));
        Assert.NotNull(back);
        Assert.Equal(s.Palette.ExcludedColors, back!.Palette.ExcludedColors);
        Assert.Equal(s.Palette.PinnedColors, back.Palette.PinnedColors);
        Assert.Equal(s.Key(), back.Key());
        var nulls = PixelatePresets.ParseSettings("{\"palette\":{\"colors\":6,\"excludedColors\":null,\"pinnedColors\":null}}");
        Assert.NotNull(nulls);
        Assert.Empty(nulls!.Palette.ExcludedColors);
        Assert.Empty(nulls.Palette.PinnedColors);
        Assert.Empty(PixelatePresets.ParseSettings("{}")!.Palette.ExcludedColors);
        Assert.Empty(PixelatePresets.ParseSettings("{}")!.Palette.PinnedColors);
    }

    private static readonly Color32 SpriteSkin = new(240, 190, 150);

    /// <summary>결과에서 원본 칸 색이 <paramref name="to"/>에 가장 가까운 칸(프레임, 열, 행)과 그 색.</summary>
    private static (int Frame, int Column, int Row, Color32 Color) SourceCellNearest(PixelateResult r, Color32 to)
    {
        var best = (Frame: -1, Column: 0, Row: 0, Color: default(Color32));
        float bestD = float.MaxValue;
        for (int f = 0; f < r.SourceColors.Count; f++)
        {
            var img = r.SourceColors[f];
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                {
                    var c = img[x, y];
                    if (c.A == 0) continue;
                    float d = Lab.Distance(OkLab.FromColor(c), OkLab.FromColor(to));
                    if (d < bestD) { bestD = d; best = (f, x - r.Maps[f].OffsetX, y - r.Maps[f].OffsetY, c); }
                }
        }
        return best;
    }

    [Fact]
    public void PinnedColorIsFixedPaletteEntryAndTakesNearestCells()
    {
        // 4색이면 살색이 노랑·빨강과 묶여 사라진다(다른 색상 지키기를 끄면 — 켜면 살색을 스스로 넣음) — 원본 칸에서 살색을 집어 고정하면 그 색이 그대로 들어가고, 색 수(4)에 든다.
        var noCleanup = new CleanupSettings { RemoveOrphans = false, FillHoles = false };
        var four = new PixelateSettings { Palette = new PaletteSettings { Colors = 4, KeepDistinctHues = false }, Cleanup = noCleanup };
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, four);
        var baseline = Pixelator.Run(src, four);
        var cell = SourceCellNearest(baseline, SpriteSkin);
        var skin = Pixelator.CellColor(src, cell.Frame, baseline.Maps[cell.Frame].Layout(), cell.Column, cell.Row, four.Downsample);
        Assert.Equal(cell.Color, skin);
        Assert.True(PaletteEdits.Matches(skin, SpriteSkin), skin.ToHex());
        Assert.DoesNotContain(baseline.Palette, c => PaletteEdits.Matches(c, skin));
        Assert.Equal(Color32.Transparent, Pixelator.CellColor(src, 0, baseline.Maps[0].Layout(), -1, 0, four.Downsample));

        var pinned = four with { Palette = four.Palette with { PinnedColors = [skin.ToHex()] } };
        var r = Pixelator.Run(src, pinned);
        Assert.Contains(skin, r.Palette);
        Assert.InRange(r.Palette.Count, 2, 4);
        Assert.Equal(4, r.ResolvedColors);
        // 원본 칸 색이 다른 결과 색보다 고정 색에 가까운 칸은 모두 고정 색.
        int taken = 0;
        for (int f = 0; f < r.Frames.Count; f++)
            for (int i = 0; i < r.Frames[f].Pixels.Length; i++)
            {
                var from = r.SourceColors[f].Pixels[i];
                if (from.A == 0 || r.Frames[f].Pixels[i].A == 0 || Nearest(r.Palette, from, ColorMetric.For(r.Settings.Palette)) != skin) continue;
                Assert.Equal(skin, r.Frames[f].Pixels[i]);
                taken++;
            }
        Assert.True(taken > 10, taken.ToString());

        // 두 고정 색도 색 수 안에.
        var two = four with { Palette = four.Palette with { PinnedColors = [skin.ToHex(), "#3CA046"] } };
        var r2 = Pixelator.Run(src, two);
        Assert.Contains(skin, r2.Palette);
        Assert.Contains(new Color32(0x3C, 0xA0, 0x46), r2.Palette);
        Assert.True(r2.Palette.Count <= 4, r2.Palette.Count.ToString());
    }

    [Fact]
    public void PinnedColorSurvivesOtherSettingsAndExclusion()
    {
        var source = Render([Sprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        var skin = SourceCellNearest(Pixelator.Run(src, SixColors with { Palette = SixColors.Palette with { Colors = 3 } }), SpriteSkin).Color;
        var pins = new[] { skin.ToHex() };
        PixelateSettings Pin(PixelateSettings s) => s with { Palette = s.Palette with { PinnedColors = pins } };
        foreach (var s in new[]
        {
            SixColors with { Palette = SixColors.Palette with { Colors = 3 } },
            SixColors with { Palette = SixColors.Palette with { Colors = 0 } },
            SixColors with { Palette = SixColors.Palette with { Colors = 4, Method = PaletteMethod.MedianCut, ColorSource = PaletteColorSource.Mean } },
            SixColors with { Palette = SixColors.Palette with { Shared = false, Colors = 3 } },
            SixColors with { Palette = SixColors.Palette with { Method = PaletteMethod.Existing, ExistingColors = ["#000000", "#FFFFFF"] } },
            SixColors with { Palette = SixColors.Palette with { Method = PaletteMethod.KeepOriginal } },
        })
        {
            var r = Pixelator.Run(src, Pin(s));
            Assert.Contains(skin, r.Palette);
            if (s.Palette.Method == PaletteMethod.Existing) Assert.All(r.Palette, c => Assert.Contains(c.ToHex(), new[] { "#000000", "#FFFFFF", skin.ToHex() }));
        }

        // 고정 색은 빠지지 않고(빼기를 무시), 다른 색은 그대로 빠진다.
        var pinned = Pin(SixColors);
        var baseline = Pixelator.Run(src, pinned);
        var red = Nearest(baseline.Palette, SpriteRed);
        var r2 = Pixelator.Run(src, pinned with { Palette = pinned.Palette with { ExcludedColors = [skin.ToHex(), red.ToHex()] } });
        Assert.Contains(skin, r2.Palette);
        AssertPostProcessed(baseline, r2, [red]);

        // 자동 맞추기는 고정 색을 바꾸지 않는다.
        var opt = PixelateOptimizer.Run(src, pinned, new PixelateOptimizeOptions { MaxTrials = 8 });
        Assert.All(opt.Trials, t => Assert.Equal(pins, t.Settings.Palette.PinnedColors));
        Assert.Contains(skin, opt.Best.Result.Palette);
    }

    /// <summary>
    /// 색이 적은(16색) AI 픽셀 그림 흉내: 30×30칸 스프라이트(1칸 외곽선, 안은 4칸 덩어리 무늬)를 12px 칸으로 키우되 칸 경계를 ±1px 흔든다(흐림·잡음 없음), 바깥은 투명.
    /// </summary>
    private static PixelImage JitteredFewColors(int block = 12, int cells = 30, int seed = 3)
    {
        var rng = new Random(seed);
        var colors = Enumerable.Range(0, 15).Select(i => new Color32((byte)(60 + i * 12), (byte)(200 - i * 9), (byte)(90 + (i * 37) % 150))).ToArray();
        var logical = new PixelImage(cells, cells);
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < cells && y < cells && Math.Min(x, cells - 1 - x) + Math.Min(y, cells - 1 - y) >= 4;
        for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
            {
                if (!Inside(x, y)) continue;
                bool edge = !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1);
                logical[x, y] = edge ? Outline : colors[(x / 4 * 7 + y / 4 * 3) % colors.Length];
            }
        int[] Cuts() => [.. Enumerable.Range(0, cells + 1).Select(i => i * block + (i == 0 || i == cells ? 0 : rng.Next(-1, 2)))];
        var xs = Cuts();
        var ys = Cuts();
        var img = new PixelImage(xs[^1] + 2 * block, ys[^1] + 2 * block);
        for (int cy = 0; cy < cells; cy++)
            for (int cx = 0; cx < cells; cx++)
                if (logical[cx, cy].A != 0)
                    img.FillRect(Rectangle.FromLTRB(xs[cx] + block, ys[cy] + block, xs[cx + 1] + block, ys[cy + 1] + block), logical[cx, cy]);
        return img;
    }

    [Fact]
    public void FewColorJitteredArtIsNotTreatedAsOnePixelArt()
    {
        var image = JitteredFewColors();
        foreach (var s in new[]
        {
            new PixelateSettings(),
            new PixelateSettings { Grid = new PixelGridSettings { Source = ScaleSource.ColorEdges }, Cleanup = new CleanupSettings { OutlineFit = OutlineFit.Thin } },
        })
        {
            var r = Pixelator.Run(image, s);
            Assert.InRange(r.DetectedScale, 11, 13);
            var size = Assert.Single(r.Frames).GetOpaqueBounds().Size;
            Assert.InRange(size.Width, 28, 32);
            Assert.InRange(size.Height, 28, 32);
        }
    }

    [Fact]
    public void QuantizedSampleKeepsNormalScale()
    {
        var image = PngIO.Load(TestUtil.Fixture("ai-walk-sample.png"));
        // 색이 적은 AI 그림 흉내: 알파를 0/255로, 불투명 픽셀을 48색 k-평균 팔레트로(가장 가까운 색).
        for (int i = 0; i < image.Pixels.Length; i++)
            image.Pixels[i] = image.Pixels[i].A >= 128 ? image.Pixels[i].WithAlpha(255) : Color32.Transparent;
        var hist = PaletteBuilder.Histogram([image]);
        new Gpixel.Core.Edit.PaletteRemap(PaletteBuilder.KMeans(hist, 48).Palette).Apply(image, Gpixel.Core.Edit.PaletteDither.None);
        Assert.True(Gpixel.Core.Edit.PaletteRemap.CountColors(image) <= 64);
        var shrink = Pixelator.Run(image, new PixelateSettings());
        Assert.InRange(shrink.Scale, 13, 19);
        var edges = Pixelator.Run(image, new PixelateSettings { Grid = new PixelGridSettings { Source = ScaleSource.ColorEdges }, Cleanup = new CleanupSettings { OutlineFit = OutlineFit.Thin } });
        Assert.InRange(edges.Scale, 6.5, 10);
    }

    [Fact]
    public void OptimizerStaysInsideSharedRanges()
    {
        static void InRanges(PixelateSettings s)
        {
            Assert.True(PixelateRanges.Scale.Contains(s.Grid.Scale), $"scale {s.Grid.Scale}");
            Assert.True(PixelateRanges.ScaleAdjust.Contains(s.Grid.ScaleAdjust), $"adjust {s.Grid.ScaleAdjust}");
            Assert.True(PixelateRanges.Offset.Contains(s.Grid.OffsetX) && PixelateRanges.Offset.Contains(s.Grid.OffsetY), "offset");
            Assert.True(PixelateRanges.Factor.Contains(s.Size.Factor), $"factor {s.Size.Factor}");
            Assert.True(s.Palette.Colors == 0 || PixelateRanges.Colors.Contains(s.Palette.Colors), $"colors {s.Palette.Colors}");
            Assert.True(PixelateRanges.BorderFraction.Contains(s.Downsample.BorderFraction), $"border {s.Downsample.BorderFraction}");
            Assert.True(PixelateRanges.AlphaThreshold.Contains(s.Downsample.AlphaThreshold), $"alpha {s.Downsample.AlphaThreshold}");
            Assert.True(PixelateRanges.SnapStep.Contains(s.Downsample.SnapStep), $"snap {s.Downsample.SnapStep}");
            Assert.True(PixelateRanges.OutlineDarkness.Contains(s.Palette.OutlineDarkness), $"dark {s.Palette.OutlineDarkness}");
            Assert.True(PixelateRanges.EdgeWeight.Contains(s.Palette.EdgeWeight), $"edge {s.Palette.EdgeWeight}");
            Assert.True(PixelateRanges.VectorTolerance.Contains(s.Cleanup.VectorTolerance), $"vector tolerance {s.Cleanup.VectorTolerance}");
            Assert.True(PixelateRanges.VectorBlobCells.Contains(s.Cleanup.VectorBlobCells), $"vector blob {s.Cleanup.VectorBlobCells}");
        }
        // 깨끗한 1배 그림(찾은 칸 크기 1 — 예전에는 칸 크기 찾기 범위가 [1.5, 1.25]가 되어 "'0.5' cannot be greater than 0.25"로 멈춤)과 끝 값들.
        var clean = Sprite(detailed: true);
        var extremes = new[]
        {
            new PixelateSettings(),
            new PixelateSettings
            {
                Downsample = new DownsampleSettings { BorderFraction = 0.45, AlphaThreshold = 1, SnapStep = 128 },
                Palette = new PaletteSettings { Colors = 64, OutlineDarkness = 1, EdgeWeight = 1 },
            },
            new PixelateSettings { Grid = new PixelGridSettings { Mode = GridMode.Manual, Scale = 1, OffsetX = 256, OffsetY = -256 } },
            new PixelateSettings { Size = new SizeSettings { Mode = OutputSizeMode.ScaleFactor, Factor = 1 } },
            new PixelateSettings { Size = new SizeSettings { Mode = OutputSizeMode.ScaleFactor, Factor = 0.01 } },
            new PixelateSettings { Grid = new PixelGridSettings { ScaleAdjust = -64 } },
        };
        foreach (var start in extremes)
        {
            var src = PixelateSource.Prepare(clean, start);
            var r = PixelateOptimizer.Run(src, start, new PixelateOptimizeOptions { Params = OptimizeParam.All, MaxTrials = 40 });
            Assert.All(r.Trials, t => InRanges(t.Settings));
        }
    }

    [Fact]
    public void ApplicableParamsFollowSettings()
    {
        var d = new PixelateSettings();
        Assert.False(PixelateOptimizer.Applicable(d).HasFlag(OptimizeParam.OffsetX));
        Assert.True(PixelateOptimizer.Applicable(d with { Grid = d.Grid with { Mode = GridMode.Manual } }).HasFlag(OptimizeParam.OffsetY));
        var keep = d with { Palette = d.Palette with { Method = PaletteMethod.KeepOriginal } };
        Assert.Equal(OptimizeParam.None, PixelateOptimizer.Applicable(keep) & (OptimizeParam.Colors | OptimizeParam.Dither | OptimizeParam.PaletteMethod | OptimizeParam.PaletteColorSource | OptimizeParam.EdgeWeight));
        var mean = d with { Palette = d.Palette with { ColorSource = PaletteColorSource.Mean } };
        Assert.False(PixelateOptimizer.Applicable(mean).HasFlag(OptimizeParam.EdgeWeight));
        Assert.True(PixelateOptimizer.Applicable(mean, OptimizeParam.PaletteColorSource).HasFlag(OptimizeParam.EdgeWeight));
        Assert.False(PixelateOptimizer.Applicable(d with { Size = d.Size with { Mode = OutputSizeMode.FixedSize } }).HasFlag(OptimizeParam.Scale));
        // 벡터 설정은 벡터로 다시 그리기이거나 외곽선 맞추기도 바꿔 볼 때만.
        Assert.False(PixelateOptimizer.Applicable(d).HasFlag(OptimizeParam.VectorTolerance));
        Assert.True(PixelateOptimizer.Applicable(d, OptimizeParam.OutlineFit).HasFlag(OptimizeParam.VectorBlob));
        Assert.True(PixelateOptimizer.Applicable(d with { Cleanup = d.Cleanup with { OutlineFit = OutlineFit.Vector } }).HasFlag(OptimizeParam.VectorTolerance));
    }

    /// <summary>결과에서 원본 픽셀 (x, y)가 들어간 칸의 색.</summary>
    private static Color32 OutputAt(PixelateResult r, int x, int y)
    {
        var m = r.Maps[0];
        int col = Math.Max(0, Array.FindLastIndex(m.XCuts, c => c <= x)), row = Math.Max(0, Array.FindLastIndex(m.YCuts, c => c <= y));
        return r.Frames[0].Get(col + m.OffsetX, row + m.OffsetY);
    }

    /// <summary>
    /// 신발 영역(원본 540~860 × 1000~1210)에서 원본 색이 <paramref name="color"/>(±20)인 덩어리 안쪽 픽셀(±3px 네 모서리도 같은 색)들의 평균 색과,
    /// 그 자리 결과 색 평균의 OKLab 거리 — 덩어리 경계(외곽선 칸으로 가는 것이 맞는 자리)는 빼고, 갈색이 통째로 금색·올리브 쪽으로 밀린 것만 잰다.
    /// </summary>
    private static double BootShift(PixelateSource src, PixelateResult r, Color32 color)
    {
        bool Near(int x, int y)
        {
            var p = src.Cleaned[x, y];
            return p.A != 0 && Math.Max(Math.Abs(p.R - color.R), Math.Max(Math.Abs(p.G - color.G), Math.Abs(p.B - color.B))) <= 20;
        }
        double sl = 0, sa = 0, sb = 0, ol = 0, oa = 0, ob = 0;
        int n = 0;
        for (int y = 1000; y < 1210; y += 2)
            for (int x = 540; x < 860; x += 2)
            {
                if (!Near(x, y) || !Near(x - 3, y - 3) || !Near(x + 3, y - 3) || !Near(x - 3, y + 3) || !Near(x + 3, y + 3)) continue;
                var c = src.Cleaned[x, y];
                var o = OutputAt(r, x, y);
                if (o.A == 0) continue;
                var a = OkLab.FromColor(c);
                var b = OkLab.FromColor(o);
                sl += a.L; sa += a.A; sb += a.B; ol += b.L; oa += b.A; ob += b.B;
                n++;
            }
        Assert.True(n > 500, n.ToString());
        return Lab.Distance(new Lab((float)(sl / n), (float)(sa / n), (float)(sb / n)), new Lab((float)(ol / n), (float)(oa / n), (float)(ob / n)));
    }

    /// <summary>
    /// 마법사 그림(1254², 이미 투명한 배경): 칸 크기(블록을 재 보면 약 14px — 외곽선 두께 어림과 색 경계 어림이 같음), 테두리 투명,
    /// 신발의 밝은·중간 갈색이 기본과 모두 켠 자동 최적화(작은 예산)에서 통째로 바뀌지 않음. 문턱 0.05 = 겨우 보이는 차이(0.02)의 2.5배 —
    /// 같은 갈색 계열로 보이는 정도이고, 밝은 갈색이 금색 장식과 한 묶음이 되어 #AE8040 쪽으로 가면 0.06을 넘는다.
    /// </summary>
    [Fact]
    public void MageKeepsScaleBackgroundAndBootColors()
    {
        var image = PngIO.Load(TestUtil.Fixture("ai-mage.png"));
        var s = new PixelateSettings();
        var src = PixelateSource.Prepare(image, s);
        var r = Pixelator.Run(src, s);
        Assert.InRange(r.Scale, 12, 17);
        var f = Assert.Single(r.Frames);
        for (int x = 0; x < f.Width; x++) { Assert.Equal(0, f[x, 0].A); Assert.Equal(0, f[x, f.Height - 1].A); }
        for (int y = 0; y < f.Height; y++) { Assert.Equal(0, f[0, y].A); Assert.Equal(0, f[f.Width - 1, y].A); }
        Assert.Equal(0, src.Cleaned[2, 2].A);

        var light = new Color32(165, 105, 60);
        var mid = new Color32(132, 84, 44);
        var opt = PixelateOptimizer.Run(src, s, new PixelateOptimizeOptions { Params = OptimizeParam.SelectAll, MaxTrials = 60 });
        foreach (var res in new[] { r, opt.Best.Result })
            foreach (var c in new[] { light, mid })
            {
                double d = BootShift(src, res, c);
                Assert.True(d <= 0.05, $"{c.ToHex()}: {d:0.000}");
            }
    }

    /// <summary>
    /// 표본 그림마다 '크기를 줄여서'(기본) + '모두 선택' 자동 최적화(기본 예산)가 70점 이상이고, 칸 크기가 원본 외곽선 두께 ±15% 안
    /// (외곽선 띠 = 한 칸). 몇 초 걸리므로 느린 시험으로 표시한다.
    /// </summary>
    [Theory]
    [Trait("Category", "Slow")]
    [InlineData("ai-walk-sample.png")]
    [InlineData("ai-mage.png")]
    [InlineData("ai-sword-boy.png")]
    [InlineData("ai-spear-boy-sticker.png")]
    [InlineData("ai-girl-bg.png")]
    [InlineData("ai-water-machine-bg.png")]
    public void SampleOptimizesToAtLeast70(string file)
    {
        var image = PngIO.Load(TestUtil.Fixture(file));
        var s = new PixelateSettings();
        var src = PixelateSource.Prepare(image, s);
        var r = PixelateOptimizer.Run(src, s, new PixelateOptimizeOptions { Params = OptimizeParam.SelectAll, MaxTime = TimeSpan.FromMinutes(2) });
        Assert.True(r.Best.Score.Total >= 70, $"{file}: {r.Best.Score.Total:0.0}");
        Assert.Equal(OutlineFit.Shrink, r.Best.Settings.Cleanup.OutlineFit);
        double outline = src.EstimatedScale(-1, s.Grid.Jitter, ScaleSource.Outline, s.Palette.OutlineDarkness).Outline;
        Assert.InRange(r.Best.Result.Scale, outline * 0.85 * 0.98, outline * 1.15 * 1.02);
    }

    /// <summary>
    /// 2칸 두께 외곽선(<see cref="ThickOutlineSprite"/>, 칸 7~9px → 띠 약 16px): '크기를 줄여서' + 외곽선 맞추기만 뺀 '모두 선택' 최적화에서 칸 크기가 외곽선 두께
    /// (±15%)이고 결과 외곽선이 1px(95% 이상). 칸이 두께의 ½(8px)이면 외곽선이 2칸이 된다 — 그런 크기는 '크기를 줄여서'에서 고르지 않는다.
    /// 외곽선 밝기를 0.15로 낮춘 시도도 외곽선 1px 점수를 거저 얻지 못한다(검은 외곽선은 기본 밝기로 잼).
    /// </summary>
    [Fact]
    public void ShrinkOptimizerKeepsCellAtOutlineThickness()
    {
        var source = Render([ThickOutlineSprite()]);
        var src = PixelateSource.Prepare(source, SixColors);
        var est = src.EstimatedScale(-1, SixColors.Grid.Jitter, ScaleSource.Outline, SixColors.Palette.OutlineDarkness);
        Assert.InRange(est.Outline, 14, 18);
        var r = PixelateOptimizer.Run(src, SixColors, new PixelateOptimizeOptions { Params = OptimizeParam.SelectAll, MaxTrials = 150 });
        Assert.Equal(OutlineFit.Shrink, r.Best.Settings.Cleanup.OutlineFit);
        Assert.InRange(r.Best.Result.Scale, est.Outline * 0.85 * 0.98, est.Outline * 1.15 * 1.02);
        Assert.True(OnePixelShare(r.Best.Result.Frames) >= 0.95, OnePixelShare(r.Best.Result.Frames).ToString());
        // 모든 '크기를 줄여서' 시도 중 고를 수 있는 것은 모두 외곽선 두께 둘레.
        Assert.All(r.Trials.Where(t => t.InScale && t.Candidate.Index > 0), t => Assert.InRange(t.Result.Scale, est.Outline * 0.83, est.Outline * 1.18));

        var dim = Pixelator.Run(src, SixColors with { Palette = SixColors.Palette with { OutlineDarkness = 0.15 } });
        Assert.Equal(OnePixelShare(dim.Frames), dim.Metrics.OutlineThin, 3);
    }

    /// <summary>
    /// AI 엘프 걷기 시트(2172×724, 투명 배경, 6프레임)를 AI 그리기 도구와 같은 길로(6프레임 시트 나누기). AI 그리기 길은 이 시트를 줄이지 않는다
    /// (<see cref="AiDrawConversion.MaxSheetSide"/>) — 작게 생성된 시트와 같은 ×½(1086×362, 예전 AI 그리기 길)도 같이 본다:
    /// 예전에는 그림 전체 덩어리 경계가 프레임 간격(×½에서 183px)을 칸으로 잡아 '크기를 줄여서'의 '더 큰 값이 확실히 더 잘 맞음' 규칙(일치 0.083 > 외곽선 0 + 0.05)으로
    /// 그 값을 골라 결과가 3×3 투명이었다(점수 66). 칸 크기 상한(프레임 짧은 변의 1/5 — ×½ 32px, 원래 크기 61px)으로 막는다. 원본 칸은 눈으로 보면 약 3.5px
    /// (외곽선 두께 3.5px)이라 ×½에서 약 1.75~2px. 원래 크기의 기본 변환 점수는 ×½보다 낮지 않다(55.5 / 54.3).
    /// </summary>
    [Fact]
    public void ElfSheetOnAiDrawPathIsNotEmpty()
    {
        var image = PngIO.Load(TestUtil.Fixture("ai-elf-walk-sheet.png"));
        Assert.Same(image, AiDrawConversion.Downscale(image, AiDrawConversion.MaxSheetSide));
        var half = AiDrawConversion.Downscale(image, 1600);
        Assert.Equal(new Size(1086, 362), half.Size);
        var settings = AiDrawConversion.Settings(new PixelateSettings(), 6, 0, true, false);
        var scores = new List<double>();
        foreach (var (sheet, cellLo, cellHi, capLo, capHi) in new[] { (half, 1.5, 3.0, 25.0, 40.0), (image, 3.0, 4.5, 50.0, 80.0) })
        {
            var (src, split) = AiDrawConversion.PrepareSheet(sheet, settings, 6, AiDrawSheetLayout.For(6), false);
            // 2·3번(×½ 시트 207px에서 맞닿음)과 4·5번(가로로 겹침)도 기대 프레임 수(6)로 가른 자동 나누기 — 칸 선을 넘은 손끝·발끝 조각 없음.
            Assert.Equal(FrameSplitMode.Auto, split.Frames.Mode);
            Assert.Equal(6, src.Split.Frames.Count);
            Assert.Equal(0, StrayEdgePieces(src.Split, sheet.Width, sheet.Height));
            var est = src.EstimatedScale(-1, split.Grid.Jitter, ScaleSource.Outline, split.Palette.OutlineDarkness);
            Assert.InRange(est.MaxScale, capLo, capHi);
            Assert.True(est.Blocks <= est.MaxScale && est.Edges <= est.MaxScale, $"blocks {est.Blocks} edges {est.Edges} cap {est.MaxScale}");

            var r = Pixelator.Run(src, split);
            Assert.False(r.Failed, r.Failure.ToString());
            Assert.Equal(6, r.Frames.Count);
            Assert.InRange(r.Scale, cellLo, cellHi);
            Assert.All(r.Frames, f => Assert.True(f.GetOpaqueBounds().Height >= 80, f.GetOpaqueBounds().ToString()));
            Assert.True(r.Palette.Count > 4, r.Palette.Count.ToString());
            Assert.True(OnePixelShare(r.Frames) >= 0.9, OnePixelShare(r.Frames).ToString());
            scores.Add(PixelateScoring.Score(r, new ScoreWeights(), r.ResolvedColors).Total);
        }
        Assert.True(scores[0] > 0 && scores[1] >= scores[0], $"half {scores[0]:0.0}, full {scores[1]:0.0}");
    }

    /// <summary>같은 시트를 원래 크기(2172×724)로 6칸 격자로 나누면 칸 약 3.5px(외곽선 두께), 외곽선 1px, 여러 색.</summary>
    [Fact]
    public void ElfSheetAtFullSizeKeepsOutlineCell()
    {
        var image = PngIO.Load(TestUtil.Fixture("ai-elf-walk-sheet.png"));
        var s = new PixelateSettings { Frames = new FrameSettings { Mode = FrameSplitMode.Grid, Columns = 6, Rows = 1 } };
        var r = Pixelator.Run(image, s);
        Assert.False(r.Failed, r.Failure.ToString());
        Assert.Equal(6, r.Frames.Count);
        Assert.InRange(r.Scale, 3, 4.5);
        Assert.True(r.Palette.Count > 4, r.Palette.Count.ToString());
        Assert.True(OnePixelShare(r.Frames) >= 0.9, OnePixelShare(r.Frames).ToString());
    }

    /// <summary>
    /// 원래 크기 엘프 시트를 6칸 격자·자동(기대 수 없이 중앙값 규칙, 기대 수 6)으로 나누면 모두 스프라이트 6개 통째: 0번의 앞손(칸 선 362를 52px 넘음)과
    /// 3번의 앞발(1448을 넘음)이 제 프레임에 남고, 4·5번(가로로 겹쳐 빈 열이 없음)이 갈리며, 프레임 가장자리에 이웃 조각이 없다.
    /// </summary>
    [Fact]
    public void ElfSheetSplitsIntoWholeSprites()
    {
        var image = PngIO.Load(TestUtil.Fixture("ai-elf-walk-sheet.png"));
        foreach (var frames in new[]
                 {
                     new FrameSettings { Mode = FrameSplitMode.Grid, Columns = 6, Rows = 1 },
                     new FrameSettings { Mode = FrameSplitMode.Auto },
                     new FrameSettings { Mode = FrameSplitMode.Auto, ExpectedFrames = 6 },
                 })
        {
            var split = PixelateSource.Prepare(image, new PixelateSettings { Frames = frames }).Split;
            Assert.Equal(6, split.Frames.Count);
            Assert.Equal(0, StrayEdgePieces(split, image.Width, image.Height));
            Assert.All(split.Frames, f => Assert.InRange(f.Bounds.Width, 280, 380));
            Assert.True(split.Frames[0].Bounds.Right > 400, split.Frames[0].Bounds.ToString());
            Assert.True(split.Frames[3].Bounds.Right > 1490, split.Frames[3].Bounds.ToString());
        }

        // ×½ 시트(작게 생성된 시트 — 예전 AI 그리기 길)의 격자 6칸(자동이 프레임 수를 못 맞출 때 쓰는 나누기)도 같다: 0번 앞손이 칸 선 181을 넘어 207까지.
        var sheet = AiDrawConversion.Downscale(image, 1600);
        var settings = AiDrawConversion.Settings(new PixelateSettings(), 6, 0, true, false);
        var half = PixelateSource.Prepare(sheet, settings).Split;
        Assert.Equal(6, half.Frames.Count);
        Assert.Equal(0, StrayEdgePieces(half, sheet.Width, sheet.Height));
        Assert.Equal(new Rectangle(0, 0, 181, 362), half.Frames[0].Slot);
        Assert.True(half.Frames[0].Bounds.Right > 200, half.Frames[0].Bounds.ToString());
    }

    /// <summary>
    /// 원래 위치 유지 + 격자: 1번 칸의 팔이 칸 선(40)을 왼쪽으로 4px 넘어 범위가 자리 밖이면 모든 프레임을 그만큼(2칸) 밀어 팔이 잘리지 않는다 — 서로 위치는 그대로.
    /// </summary>
    [Fact]
    public void KeepOffsetsShiftsFramesWhoseBoundsLeaveTheirCell()
    {
        var image = new PixelImage(80, 40);
        image.FillRect(Rectangle.FromLTRB(10, 6, 30, 34), new Color32(200, 40, 40));
        image.FillRect(Rectangle.FromLTRB(50, 6, 70, 34), new Color32(60, 80, 200));
        image.FillRect(Rectangle.FromLTRB(36, 16, 50, 20), new Color32(60, 80, 200));
        var s = new PixelateSettings
        {
            Frames = new FrameSettings { Mode = FrameSplitMode.Grid, Columns = 2, Rows = 1, Align = FrameAlign.KeepOffsets },
            Grid = new PixelGridSettings { Mode = GridMode.Manual, Scale = 2, LocalCorrection = false },
            Cleanup = new CleanupSettings { RemoveOrphans = false, FillHoles = false, OutlineFit = OutlineFit.Off },
        };
        var r = Pixelator.Run(image, s);
        Assert.Equal(2, r.Frames.Count);
        Assert.Equal(Rectangle.FromLTRB(36, 6, 70, 34), r.Maps[1].SourceBounds);
        var a = r.Frames[0].GetOpaqueBounds();
        var b = r.Frames[1].GetOpaqueBounds();
        Assert.Equal(17, b.Width);
        Assert.Equal(10, a.Width);
        Assert.Equal(5 + 2, a.Left - b.Left);
    }

    /// <summary>
    /// AI 그리기 도구의 변환 전체(<see cref="AiDrawConversion.Convert"/> — 정리 창 기본 자동 최적화 항목: 외곽선 맞추기 방식만 빼고 모두)로 엘프 시트(원래 크기 —
    /// AI 그리기 길은 줄이지 않음)를 바꾸면 1위는 실패하지 않은 6프레임 결과(칸 약 3.5px = 외곽선 두께)이고 진행(돌림 번호 포함)이 알려진다.
    /// 시험 시간을 줄이려고 예산은 700번 — 첫 돌림이 끝나 둘째 돌림이 시작된다.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void ElfSheetAiDrawConversionWithDefaultOptimizer()
    {
        var sheet = AiDrawConversion.Downscale(PngIO.Load(TestUtil.Fixture("ai-elf-walk-sheet.png")), AiDrawConversion.MaxSheetSide);
        var settings = AiDrawConversion.Settings(new PixelateSettings(), 6, 0, true, false);
        int reports = 0, maxPass = 0;
        // 시간이 아닌 시도 수 예산으로 끝나게(원래 크기 시트는 Debug에서 첫 돌림 약 670번에 100초 남짓).
        var options = new PixelateOptimizeOptions { Params = OptimizeParam.SelectAll, MaxTrials = 700, MaxTime = TimeSpan.FromMinutes(5) };
        var (r, used) = AiDrawConversion.Convert(sheet, settings, 6, sheet: true, keepOffsets: false, optimize: true, options, p =>
        {
            Interlocked.Increment(ref reports);
            int seen;
            while ((seen = Volatile.Read(ref maxPass)) < p.Pass && Interlocked.CompareExchange(ref maxPass, p.Pass, seen) != seen) { }
        });
        Assert.True(maxPass >= 2, maxPass.ToString());
        Assert.False(r.Failed, r.Failure.ToString());
        Assert.Equal(6, r.CommonFrames().Count);
        Assert.Equal(OutlineFit.Shrink, used.Cleanup.OutlineFit);
        Assert.InRange(r.Scale, 3, 4.5);
        Assert.True(r.Palette.Count > 4, r.Palette.Count.ToString());
        Assert.True(OnePixelShare(r.Frames) >= 0.9, OnePixelShare(r.Frames).ToString());
        Assert.True(reports > 1, reports.ToString());
    }

    /// <summary>
    /// AI 마법사 꼬마 시트(2172×724, 투명 배경, 6프레임, 덩어리 약 7px — 외곽선 두께 6.9px): AI 그리기 길은 이 시트를 줄이지 않는다(<see cref="AiDrawConversion.MaxSheetSide"/>).
    /// 예전처럼 ×½(1086×362)로 줄이면 칸이 3.6px이라 지역 보정(<see cref="LocalGrid.MinCell"/> 5px 이상)이 꺼져 칸 선이 곧은 격자로 덩어리를 비껴갔다(점수 57.4).
    /// 원래 크기는 칸 약 7px, 모든 프레임에 지역 보정이 켜지고, 뚜렷한 경계에서 가장 가까운 칸 경계까지 거리(칸 크기 비율)가 곧은 격자보다 짧으며(0.215 → 0.144),
    /// 외곽선은 1px, 점수는 ×½보다 5점 넘게 높다(66.5).
    /// </summary>
    [Fact]
    public void ChibiSheetOnAiDrawPathKeepsFullSizeAndLocalGrid()
    {
        var image = PngIO.Load(TestUtil.Fixture("ai-mage-chibi-sheet.png"));
        var sheet = AiDrawConversion.Downscale(image, AiDrawConversion.MaxSheetSide);
        Assert.Same(image, sheet);
        var settings = AiDrawConversion.Settings(new PixelateSettings(), 6, 0, true, false);
        var (src, split) = AiDrawConversion.PrepareSheet(sheet, settings, 6, AiDrawSheetLayout.For(6), false);
        Assert.Equal(6, src.Split.Frames.Count);
        var r = Pixelator.Run(src, split);
        Assert.False(r.Failed, r.Failure.ToString());
        Assert.InRange(r.Scale, 6, 8);
        Assert.All(r.Maps, m => Assert.NotNull(m.Local));
        double global = Misalignment(src, r, local: false), local = Misalignment(src, r, local: true);
        Assert.True(local < global - 0.04, $"global {global:0.000} → local {local:0.000}");
        Assert.True(OnePixelShare(r.Frames) >= 0.95, OnePixelShare(r.Frames).ToString());

        var half = AiDrawConversion.Downscale(image, 1600);
        var (hsrc, hsplit) = AiDrawConversion.PrepareSheet(half, settings, 6, AiDrawSheetLayout.For(6), false);
        var hr = Pixelator.Run(hsrc, hsplit);
        Assert.True(hr.Scale < LocalGrid.MinCell, hr.Scale.ToString());
        Assert.All(hr.Maps, m => Assert.Null(m.Local));
        var w = new ScoreWeights();
        double full = PixelateScoring.Score(r, w, r.ResolvedColors).Total, small = PixelateScoring.Score(hr, w, hr.ResolvedColors).Total;
        Assert.True(full > small + 5, $"half {small:0.0} → full {full:0.0}");
    }

    /// <summary>
    /// 뚜렷한 경계(<see cref="PixelateSource.BlockEdges"/>)마다 그 줄(지역 보정이면 그 띠)의 가장 가까운 칸 경계까지 거리 ÷ 칸 크기의 평균 —
    /// 칸 선이 덩어리 경계를 따르면 0, 아무렇게나 놓이면 약 0.25.
    /// </summary>
    private static double Misalignment(PixelateSource src, PixelateResult r, bool local)
    {
        double sum = 0;
        long all = 0;
        for (int f = 0; f < r.Maps.Count; f++)
        {
            var m = r.Maps[f];
            var l = local ? m.Layout() : new CellLayout(m.XCuts, m.YCuts);
            var e = src.BlockEdges(f);
            int cx = (m.XCuts[0] + m.XCuts[^1]) / 2, cy = (m.YCuts[0] + m.YCuts[^1]) / 2;
            for (int k = 0; k < e.Rows.Length; k++)
            {
                l.Locate(cx, e.Area.Top + k, out _, out int row);
                var cuts = l.XCutsFor(row);
                foreach (int x in e.Rows[k]) { all++; sum += cuts.Min(c => Math.Abs(c - x)) / r.Scale; }
            }
            for (int k = 0; k < e.Cols.Length; k++)
            {
                l.Locate(e.Area.Left + k, cy, out int col, out _);
                var cuts = l.YCutsFor(col);
                foreach (int y in e.Cols[k]) { all++; sum += cuts.Min(c => Math.Abs(c - y)) / r.Scale; }
            }
        }
        return all == 0 ? 0 : sum / all;
    }

    /// <summary>
    /// '크기를 줄여서'의 칸 크기 띠(외곽선 두께 ±15%)는 시작 설정의 외곽선 밝기와 상관없이 기본 밝기로 잰 두께에 둔다: ×½ 마법사 꼬마 시트는 기본 밝기로 3.48px,
    /// 최적화 1위가 고르곤 하는 밝기 0.35로 재면 3.75px(어두운 명암까지 외곽선으로 셈) — 그 1위에서 다시 최적화하면 예전에는 띠가 3.19~4.31px로 밀렸다.
    /// </summary>
    [Fact]
    public void ShrinkBandIgnoresStartOutlineDarkness()
    {
        var half = AiDrawConversion.Downscale(PngIO.Load(TestUtil.Fixture("ai-mage-chibi-sheet.png")), 1600);
        var settings = AiDrawConversion.Settings(new PixelateSettings(), 6, 0, true, false);
        var (src, split) = AiDrawConversion.PrepareSheet(half, settings, 6, AiDrawSheetLayout.For(6), false);
        double outline = src.EstimatedScale(-1, split.Grid.Jitter, ScaleSource.Outline, PaletteSettings.DefaultOutlineDarkness).Outline;
        double dark = src.EstimatedScale(-1, split.Grid.Jitter, ScaleSource.Outline, 0.35).Outline;
        Assert.True(dark > outline * 1.05, $"{outline:0.00} / {dark:0.00}");
        var start = split with { Palette = split.Palette with { OutlineDarkness = 0.35 } };
        var r = PixelateOptimizer.Run(src, start, new PixelateOptimizeOptions { Params = OptimizeParam.Scale, MaxTrials = 40, MaxTime = TimeSpan.FromMinutes(2) });
        var tried = r.Trials.Where(t => t.InScale && t.Candidate.Index > 0).ToList();
        Assert.NotEmpty(tried);
        Assert.All(tried, t => Assert.InRange(t.Result.Scale, outline * 0.85 * 0.98, outline * 1.15 * 1.02));
    }

    /// <summary>
    /// 실패한 결과 막기: 예전 엘프 시트 어림(그림 전체 183px = 프레임 간격)을 억지로 쓰면 결과가 모두 투명 → 실패(점수 0, 항목 점수로는 66점이었음),
    /// 다시 돌리기(<see cref="Pixelator.Retry"/>)는 다른 어림(외곽선 두께 등)으로 실패하지 않은 결과를 내고 처음 크기를 기록한다.
    /// 비었거나 한 색·너무 작은 프레임도 실패(<see cref="Pixelator.CheckFailure"/>).
    /// </summary>
    [Fact]
    public void FailedResultScoresZeroAndRetries()
    {
        // ×½ 시트(예전 AI 그리기 길): 프레임 간격 183px.
        var sheet = AiDrawConversion.Downscale(PngIO.Load(TestUtil.Fixture("ai-elf-walk-sheet.png")), 1600);
        var settings = AiDrawConversion.Settings(new PixelateSettings(), 6, 0, true, false);
        var (src, split) = AiDrawConversion.PrepareSheet(sheet, settings, 6, AiDrawSheetLayout.For(6), false);
        var est = src.EstimatedScale(-1, split.Grid.Jitter, ScaleSource.Outline, split.Palette.OutlineDarkness);
        var bad = Pixelator.RunOnce(src, split, null, est with { Scale = 183, Used = ScaleSource.EdgeLines }, 0, default);
        Assert.Equal(PixelateFailure.Empty, bad.Failure);
        Assert.Empty(bad.Palette);
        var score = PixelateScoring.Score(bad, new ScoreWeights(), null);
        Assert.Equal(0, score.Total);
        // 항목만으로 매기면 빈 결과도 60점이 넘는다(그래서 점수가 아니라 실패 검사로 거름).
        Assert.True(100 * (1 - score.Parts.Sum(p => p.Weight * p.Badness) / score.Parts.Sum(p => p.Weight)) > 60);

        var retried = Pixelator.Retry(src, split, null, bad, default);
        Assert.False(retried.Failed, retried.Failure.ToString());
        Assert.Equal(183, retried.RetriedFrom, 1);
        Assert.True(retried.Scale <= src.MaxCell(-1), retried.Scale.ToString());

        // 결과 그림만으로 정하는 실패: 비었음·한 색·모든 프레임이 8×8 미만.
        var blank = new PixelImage(20, 20);
        Assert.Equal(PixelateFailure.Empty, Pixelator.CheckFailure(src, [blank], [2.0], 0));
        var one = new PixelImage(60, 140);
        one.FillRect(new Rectangle(0, 0, 60, 140), new Color32(200, 40, 40));
        Assert.Equal(PixelateFailure.OneColor, Pixelator.CheckFailure(src, [one], [2.0], 1));
        var tiny = new PixelImage(7, 7);
        tiny.FillRect(new Rectangle(0, 0, 7, 7), new Color32(200, 40, 40));
        tiny[3, 3] = Outline;
        Assert.Equal(PixelateFailure.TooSmall, Pixelator.CheckFailure(src, [tiny], [60.0], 2));
        Assert.Equal(PixelateFailure.NearlyEmpty, Pixelator.CheckFailure(src, [tiny], [2.0], 2));
    }

    /// <summary>
    /// 벽돌 무늬: 10px 덩어리(무작위 5색, 불투명), 줄마다 세로 경계가 반 칸(5px)씩 엇갈림 — 축 전체에 한 줄인 세로 칸 경계는 줄의 반만 맞는다.
    /// 칸 경계 지역 보정(띠 1칸, 최대 이동 0.5칸)은 줄마다 경계를 덩어리 경계로 옮겨 맞는 비율과 충실도가 나아지고, 원본 자리 → 칸(<see cref="CellLayout.Locate"/>)은
    /// 칸 사각형과 어긋나지 않는다(원본 보기에서 마우스를 올린 칸 = 결과 픽셀).
    /// </summary>
    [Fact]
    public void LocalGridFollowsBrickOffsets()
    {
        const int cell = 10, cols = 16, rows = 12;
        var colors = new[] { new Color32(200, 60, 60), new Color32(60, 160, 80), new Color32(70, 90, 210), new Color32(230, 200, 70), new Color32(240, 190, 150) };
        var rng = new Random(5);
        var img = new PixelImage(cols * cell + cell, rows * cell);
        for (int r = 0; r < rows; r++)
        {
            int shift = r % 2 == 0 ? 0 : cell / 2;
            for (int b = -1; b <= cols; b++)
            {
                var c = colors[rng.Next(colors.Length)];
                for (int y = r * cell; y < (r + 1) * cell; y++)
                    for (int x = Math.Max(0, b * cell + shift); x < Math.Min(img.Width, (b + 1) * cell + shift); x++)
                        img[x, y] = c;
            }
        }
        int[] xc = [.. Enumerable.Range(0, cols + 2).Select(i => i * cell)];
        int[] yc = [.. Enumerable.Range(0, rows + 1).Select(i => i * cell)];
        var edges = GridDetector.BlockEdges(img, GridDetector.LabOf(img), img.Bounds);
        var local = LocalGrid.Correct(edges, xc, yc, 0.5, 1);
        Assert.NotNull(local);
        var global = new CellLayout(xc, yc);
        var layout = new CellLayout(xc, yc, local);

        // 줄마다 뚜렷한 세로 경계 중 그 줄의 칸 경계(±1px) 위에 놓인 비율.
        double Aligned(CellLayout l)
        {
            int hit = 0, all = 0;
            for (int y = 0; y < img.Height; y++)
            {
                l.Locate(img.Width / 2, y, out _, out int row);
                var cuts = l.XCutsFor(row);
                foreach (int x in edges.Rows[y])
                {
                    all++;
                    if (cuts.Any(c => Math.Abs(c - x) <= 1)) hit++;
                }
            }
            return hit / (double)all;
        }
        double a0 = Aligned(global), a1 = Aligned(layout);
        Assert.True(a1 > a0 + 0.3 && a1 > 0.9, $"global {a0:0.00} → local {a1:0.00}");

        // 원본 자리 → 칸이 칸 사각형과 같음.
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var (x0, y0, x1, y1) = layout.Cell(c, r);
                Assert.True(layout.Locate((x0 + x1) / 2.0, (y0 + y1) / 2.0, out int lc, out int lr) && lc == c && lr == r, $"cell {c},{r}");
            }

        // 정리 결과: 지역 보정이면 엇갈린 줄도 덩어리 하나가 칸 하나라 충실도가 나아진다.
        var s = new PixelateSettings
        {
            Background = new BackgroundSettings { Mode = BackgroundMode.None },
            Frames = new FrameSettings { Mode = FrameSplitMode.Single },
            Grid = new PixelGridSettings { Mode = GridMode.Manual, Scale = cell, LocalCorrection = false, LocalShift = 0.5, LocalBand = 1 },
            Palette = new PaletteSettings { Method = PaletteMethod.KeepOriginal, SnapOutline = false },
            Cleanup = new CleanupSettings { RemoveOrphans = false, FillHoles = false, OutlineFit = OutlineFit.Off },
        };
        var off = Pixelator.Run(img, s);
        var on = Pixelator.Run(img, s with { Grid = s.Grid with { LocalCorrection = true } });
        Assert.NotNull(on.Maps[0].Local);
        Assert.True(on.Metrics.Fidelity < off.Metrics.Fidelity * 0.5, $"{off.Metrics.Fidelity:0.000} → {on.Metrics.Fidelity:0.000}");
    }

    [Fact]
    public void StickerBorderIsRemovedAndBackgroundCanBeKept()
    {
        var sticker = PngIO.Load(TestUtil.Fixture("ai-spear-boy-sticker.png"));
        var on = PixelateSource.Prepare(sticker, new PixelateSettings());
        var off = PixelateSource.Prepare(sticker, new PixelateSettings { Background = new BackgroundSettings { RemoveStickerBorder = false } });
        Assert.True(on.Background.ForegroundCount < off.Background.ForegroundCount * 0.9, $"{on.Background.ForegroundCount} / {off.Background.ForegroundCount}");
        // 흰 띠를 지우면 바깥 외곽선 두께로 칸 크기를 잴 수 있다(띠가 있으면 외곽선이 투명과 닿지 않아 0).
        Assert.InRange(on.EstimatedScale(-1, 0.15, ScaleSource.Outline, 0.24).Outline, 20, 30);

        var girl = PngIO.Load(TestUtil.Fixture("ai-girl-bg.png"));
        var kept = Pixelator.Run(girl, new PixelateSettings { Background = new BackgroundSettings { KeepBackground = true } });
        // 배경을 남기면 그림 안쪽은 모두 불투명(바깥 한 칸은 정리용 여백 칸).
        var kf = Assert.Single(kept.Frames);
        for (int y = 1; y < kf.Height - 1; y++)
            for (int x = 1; x < kf.Width - 1; x++)
                Assert.Equal(255, kf[x, y].A);
        var removed = Assert.Single(Pixelator.Run(girl, new PixelateSettings()).Frames);
        Assert.Equal(0, removed[0, 0].A);
    }
}
