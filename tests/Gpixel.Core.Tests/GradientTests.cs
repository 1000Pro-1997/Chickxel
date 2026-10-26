using System.Drawing;
using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class GradientTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);

    [Fact]
    public void Project_ClampsAlongDirection()
    {
        var a = new Point(2, 0);
        var b = new Point(6, 0);
        Assert.Equal(0, Gradient.Project(a, b, 0, 5));
        Assert.Equal(0.5, Gradient.Project(a, b, 4, 3));
        Assert.Equal(1, Gradient.Project(a, b, 9, 0));
        // 대각선: 수직 방향으로는 같은 값
        Assert.Equal(Gradient.Project(new Point(0, 0), new Point(4, 4), 4, 0), Gradient.Project(new Point(0, 0), new Point(4, 4), 0, 4));
        Assert.Equal(0, Gradient.Project(a, a, 5, 5));
    }

    [Fact]
    public void ColorAt_StopBoundaries()
    {
        var stops = Gradient.Sorted([new GradientStop(1, Blue), new GradientStop(0.25, Red), new GradientStop(0.5, Green)]);
        Assert.Equal([0.25, 0.5, 1.0], stops.Select(s => s.Position));
        Assert.Equal(Red, Gradient.ColorAt(stops, 0));          // 첫 정지점 앞
        Assert.Equal(Red, Gradient.ColorAt(stops, 0.25));
        Assert.Equal(Green, Gradient.ColorAt(stops, 0.5));       // 정지점 위는 그 색
        Assert.Equal(Blue, Gradient.ColorAt(stops, 1));
        Assert.Equal(new Color32(128, 128, 0), Gradient.ColorAt(stops, 0.375, colorSpace: GradientColorSpace.Rgb)); // 빨강↔초록 가운데
        // 같은 위치 두 정지점은 딱 끊긴다
        var hard = Gradient.Sorted([new GradientStop(0, Red), new GradientStop(0.5, Red), new GradientStop(0.5, Blue), new GradientStop(1, Blue)]);
        Assert.Equal(Red, Gradient.ColorAt(hard, 0.49));
        Assert.Equal(Blue, Gradient.ColorAt(hard, 0.5));
    }

    [Fact]
    public void OpacityStops_MultiplyColorAlpha_IndependentOfColorStops()
    {
        var colors = Gradient.Sorted([new GradientStop(0, Red), new GradientStop(1, Blue)]);
        var alpha = Gradient.SortedAlpha([new GradientAlphaStop(1, 0), new GradientAlphaStop(0, 255)]);
        Assert.Equal(Red, Gradient.Sample(colors, alpha, 0));
        Assert.Equal(Color32.Transparent, Gradient.Sample(colors, alpha, 1));
        // 가운데: 색은 빨강↔파랑 가운데, 불투명도 50%
        var mid = Gradient.Sample(colors, alpha, 0.5, colorSpace: GradientColorSpace.Rgb);
        Assert.Equal((128, 0, 128, 128), (mid.R, mid.G, mid.B, mid.A));
        // 반투명 색 정지점 × 불투명도 정지점
        var halfRed = new Color32(255, 0, 0, 128);
        Assert.Equal(64, Gradient.Sample([new GradientStop(0, halfRed)], [new GradientAlphaStop(0, 128)], 0.3).A);
        // 불투명도 정지점이 없으면 색 그대로
        Assert.Equal(Red, Gradient.Sample(colors, [], 0));
        // 단계 모드·디더링도 불투명도에 같은 규칙
        Assert.Equal(0, Gradient.AlphaAt(alpha, 0.6, GradientInterpolation.Steps));
        int opaque = 0;
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                byte a = Gradient.AlphaAt(alpha, 0.25, GradientInterpolation.Smooth, GradientDither.Bayer4, x, y);
                Assert.True(a is 0 or 255);
                if (a == 255) opaque++;
            }
        Assert.Equal(12, opaque); // 75% 불투명
    }

    [Fact]
    public void Apply_UsesOpacityStops()
    {
        var img = new PixelImage(4, 1);
        img.Clear(Green);
        Gradient.Apply(img, new Point(0, 0), new Point(3, 0), [new GradientStop(0, Red), new GradientStop(1, Red)],
            alphaStops: [new GradientAlphaStop(0, 255), new GradientAlphaStop(1, 0)]);
        Assert.Equal(Red, img[0, 0]);
        Assert.Equal(Color32.Transparent, img[3, 0]);
        Assert.InRange(img[1, 0].A, 1, 254);
    }

    [Fact]
    public void ColorAt_AlphaInterpolatedWithoutDarkening()
    {
        var stops = Gradient.Sorted([new GradientStop(0, Red), new GradientStop(1, Color32.Transparent)]);
        var mid = Gradient.ColorAt(stops, 0.5);
        Assert.Equal(128, mid.A);
        Assert.Equal(255, mid.R);   // 투명(검정 RGB) 쪽으로 가도 어두워지지 않는다
        Assert.Equal(Color32.Transparent, Gradient.ColorAt(stops, 1));
    }

    [Fact]
    public void ColorSpaces_InterpolateHueOnShortestArcAndBorrowAchromaticHue()
    {
        GradientStop[] stops = [new(0, Red), new(1, Green)];
        Assert.Equal(GradientColorSpace.Rgb, (GradientColorSpace)0);
        Assert.Equal(new Color32(128, 128, 0), Gradient.ColorAt(stops, 0.5, colorSpace: GradientColorSpace.Rgb));
        Assert.Equal(new Color32(255, 255, 0), Gradient.ColorAt(stops, 0.5, colorSpace: GradientColorSpace.Hsl));
        var perceptual = Gradient.ColorAt(stops, 0.5);
        Assert.Equal(perceptual, Gradient.ColorAt(stops, 0.5, colorSpace: GradientColorSpace.OkLch));
        Assert.True(perceptual.R > 120 && perceptual.G > 100 && perceptual.B < 80);
        var lab = Gradient.ColorAt(stops, 0.5, colorSpace: GradientColorSpace.OkLab);
        Assert.NotEqual(perceptual, lab);

        GradientStop[] wrap = [new(0, Red), new(1, Blue)];
        Assert.Equal(new Color32(255, 0, 255), Gradient.ColorAt(wrap, 0.5, colorSpace: GradientColorSpace.Hsl));
        GradientStop[] achromatic = [new(0, Color32.White), new(1, Blue)];
        foreach (var space in new[] { GradientColorSpace.Hsl, GradientColorSpace.OkLch })
        {
            var tinted = Gradient.ColorAt(achromatic, 0.5, colorSpace: space);
            Assert.True(tinted.B > tinted.R && tinted.B > tinted.G);
        }
    }

    [Fact]
    public void ColorSpaces_MapOutOfGamutChromaWithoutChangingHueOrLightness()
    {
        GradientStop[] stops = [new(0, Red), new(1, Green)];
        var a = Gpixel.Core.Pixelate.OkLab.FromColor(Red);
        var b = Gpixel.Core.Pixelate.OkLab.FromColor(Green);
        double firstHue = Math.Atan2(a.B, a.A), lastHue = Math.Atan2(b.B, b.A);
        double wantedHue = (firstHue + lastHue) / 2;
        double wantedChroma = (Math.Sqrt(a.A * a.A + a.B * a.B) + Math.Sqrt(b.A * b.A + b.B * b.B)) / 2;
        var unmapped = new Gpixel.Core.Pixelate.Lab((a.L + b.L) / 2,
            (float)(wantedChroma * Math.Cos(wantedHue)), (float)(wantedChroma * Math.Sin(wantedHue)));
        Assert.False(Gpixel.Core.Pixelate.OkLab.InGamut(unmapped));
        var mapped = Gpixel.Core.Pixelate.OkLab.FromColor(Gradient.ColorAt(stops, 0.5));
        Assert.InRange(Math.Abs(mapped.L - unmapped.L), 0, 0.01);
        Assert.InRange(Math.Abs(Math.Atan2(mapped.B, mapped.A) - wantedHue), 0, 0.035);
        Assert.True(Math.Sqrt(mapped.A * mapped.A + mapped.B * mapped.B) < wantedChroma);
    }

    [Theory]
    [InlineData(GradientColorSpace.Rgb)]
    [InlineData(GradientColorSpace.Hsl)]
    [InlineData(GradientColorSpace.OkLab)]
    [InlineData(GradientColorSpace.OkLch)]
    public void ColorSpaces_WeightByAlphaAndKeepStepsAndDitherIndependent(GradientColorSpace space)
    {
        GradientStop[] stops = [new(0, Red), new(1, Blue.WithAlpha(0))];
        var mid = Gradient.ColorAt(stops, 0.5, colorSpace: space);
        Assert.Equal(Red.WithAlpha(128), mid);
        var weighted = Gradient.ColorAt([new(0, Red.WithAlpha(64)), new(1, Blue)], 0.5, colorSpace: space);
        var balanced = Gradient.ColorAt([new(0, Red), new(1, Blue)], 0.5, colorSpace: space);
        Assert.Equal((byte)160, weighted.A);
        Assert.True(weighted.R < balanced.R, $"Expected transparent red to contribute less in {space}");
        Assert.Equal(Color32.Transparent, Gradient.Sample(stops, [new(0, 0)], 0.5, colorSpace: space));
        Assert.Equal(Blue.WithAlpha(0), Gradient.ColorAt(stops, 0.5,
            GradientInterpolation.Steps, colorSpace: space));
        Assert.Equal(Red, Gradient.ColorAt(stops, 0.5,
            dither: GradientDither.Bayer2, x: 1, y: 0, colorSpace: space));
        Assert.Equal(Color32.Transparent, Gradient.ColorAt(
            [new(0, Red.WithAlpha(0)), new(1, Blue.WithAlpha(0))], 0.5, colorSpace: space));
    }

    [Fact]
    public void Apply_UsesSelectedColorSpace()
    {
        var image = new PixelImage(3, 1);
        Gradient.Apply(image, new Point(0, 0), new Point(2, 0),
            [new(0, Red), new(1, Green)], colorSpace: GradientColorSpace.Hsl);
        Assert.Equal(new Color32(255, 255, 0), image[1, 0]);
    }

    [Fact]
    public void StepsMode_UsesNearestStopOnly()
    {
        var stops = Gradient.Sorted([new GradientStop(0, Red), new GradientStop(0.5, Green), new GradientStop(1, Blue)]);
        Assert.Equal(Red, Gradient.ColorAt(stops, 0.2, GradientInterpolation.Steps));
        Assert.Equal(Green, Gradient.ColorAt(stops, 0.25, GradientInterpolation.Steps));
        Assert.Equal(Green, Gradient.ColorAt(stops, 0.7, GradientInterpolation.Steps));
        Assert.Equal(Blue, Gradient.ColorAt(stops, 0.75, GradientInterpolation.Steps));

        var img = new PixelImage(8, 1);
        Gradient.Apply(img, new Point(0, 0), new Point(7, 0), stops, GradientInterpolation.Steps);
        Assert.All(img.Pixels, c => Assert.Contains(c, new[] { Red, Green, Blue }));
    }

    [Fact]
    public void Dither_UsesOnlyStopColorsWithMatchingRatio()
    {
        var stops = Gradient.Sorted([new GradientStop(0, Red), new GradientStop(1, Blue)]);
        // 4×4 칸에서 u = 0.5면 절반이 파랑
        int blue = 0;
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                var c = Gradient.ColorAt(stops, 0.5, GradientInterpolation.Smooth, GradientDither.Bayer4, x, y);
                Assert.True(c == Red || c == Blue);
                if (c == Blue) blue++;
            }
        Assert.Equal(8, blue);
        int blue2 = 0;
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                if (Gradient.ColorAt(stops, 0.3, GradientInterpolation.Smooth, GradientDither.Bayer2, x, y) == Blue) blue2++;
        Assert.Equal(1, blue2);
    }

    [Fact]
    public void Apply_HonorsMaskAndIgnoresZeroLength()
    {
        var img = new PixelImage(4, 4);
        img.FillRect(img.Bounds, Green);
        var stops = new[] { new GradientStop(0, Red), new GradientStop(1, Blue) };
        Assert.Equal(0, Gradient.Apply(img, new Point(1, 1), new Point(1, 1), stops));

        var clip = PixelMask.FromPoints([new Point(0, 0), new Point(3, 0), new Point(3, 3)]);
        int n = Gradient.Apply(img, new Point(0, 0), new Point(3, 0), stops, clip: clip);
        Assert.Equal(3, n);
        Assert.Equal(Red, img.Get(0, 0));
        Assert.Equal(Blue, img.Get(3, 0));
        Assert.Equal(Blue, img.Get(3, 3));
        Assert.Equal(Green, img.Get(1, 0));
        Assert.Equal(Green, img.Get(0, 3));

        // 사각형 제한
        var img2 = new PixelImage(4, 1);
        Assert.Equal(2, Gradient.Apply(img2, new Point(0, 0), new Point(3, 0), stops, clip: new Rectangle(1, 0, 2, 1)));
        Assert.Equal(Color32.Transparent, img2.Get(0, 0));
    }
}
