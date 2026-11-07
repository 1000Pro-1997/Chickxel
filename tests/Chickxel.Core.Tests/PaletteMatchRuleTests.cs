using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class PaletteMatchRuleTests
{
    private static readonly Color32 Black = new(0, 0, 0);
    private static readonly Color32 White = new(255, 255, 255);
    private static readonly Color32 Red = new(255, 0, 0);

    // OKLab 밝기: 남색 0.20 < 초록 0.58 < 연노랑 0.98, 팔레트는 검정 0 < 빨강 0.63 < 흰색 1.
    private static readonly Color32 DarkBlue = new(0, 0, 80);
    private static readonly Color32 Green = new(0, 150, 0);
    private static readonly Color32 LightYellow = new(255, 255, 150);

    // 게임보이풍 4색(밝기 0.30 / 0.45 / 0.70 / 0.74).
    private static readonly Color32 Gb0 = new(15, 56, 15);
    private static readonly Color32 Gb1 = new(48, 98, 48);
    private static readonly Color32 Gb2 = new(139, 172, 15);
    private static readonly Color32 Gb3 = new(155, 188, 15);

    [Fact]
    public void BrightnessOrder_MapsDarkestToDarkest_AndLightestToLightest_WhateverTheHue()
    {
        var img = Row(DarkBlue, Green, LightYellow);
        new PaletteRemap([White, Red, Black], PaletteMatchRule.BrightnessOrder, source: img.Pixels).Apply(img);
        Assert.Equal(new[] { Black, Red, White }, img.Pixels);
    }

    [Fact]
    public void PaletteOrder_FollowsListedOrder()
    {
        var img = Row(DarkBlue, Green, LightYellow);
        new PaletteRemap([White, Red, Black], PaletteMatchRule.PaletteOrder, source: img.Pixels).Apply(img);
        Assert.Equal(new[] { White, Red, Black }, img.Pixels);
    }

    [Fact]
    public void BrightnessOrder_IsProportionalByRank()
    {
        var grays = Row(G(40), G(90), G(160), G(220));
        new PaletteRemap([Black, White], PaletteMatchRule.BrightnessOrder, source: grays.Pixels).Apply(grays);
        Assert.Equal(new[] { Black, Black, White, White }, grays.Pixels);

        // 그림 색이 팔레트보다 적으면 양 끝으로 벌어진다(가장 어두운 → 가장 어두운, 가장 밝은 → 가장 밝은).
        var two = Row(G(100), G(140));
        new PaletteRemap([Black, G(85), G(170), White], PaletteMatchRule.BrightnessOrder, source: two.Pixels).Apply(two);
        Assert.Equal(new[] { Black, White }, two.Pixels);
    }

    [Fact]
    public void BrightnessOrder_RanksColorsOfAllSourcesTogether()
    {
        var dark = Row(G(40));
        var light = Row(G(200));
        var source = new HashSet<Color32>();
        PaletteRemap.AddOpaqueColors(dark, source);
        PaletteRemap.AddOpaqueColors(light, source);
        var remap = new PaletteRemap([White, G(128), Black], PaletteMatchRule.BrightnessOrder, source: source);
        remap.Apply(dark);
        remap.Apply(light);
        Assert.Equal(Black, dark.Pixels[0]);
        Assert.Equal(White, light.Pixels[0]);
    }

    [Fact]
    public void BrightnessOrder_Dither_MixesTheTwoPaletteColorsAroundTheRank()
    {
        // 가운데 순위(자리 0.5)는 검정·흰색 반반 무늬.
        var img = new PixelImage(4, 4);
        img.Clear(G(128));
        new PaletteRemap([Black, White], PaletteMatchRule.BrightnessOrder, source: [G(20), G(128), G(240)]).Apply(img, PaletteDither.Bayer4);
        Assert.Equal(8, img.Pixels.Count(c => c == Black));
        Assert.Equal(8, img.Pixels.Count(c => c == White));
    }

    [Fact]
    public void HueFirst_KeepsRedOnRedEntry_WhenLightnessDiffers()
    {
        var src = new Color32(230, 40, 40);
        var darkRed = new Color32(100, 0, 0);
        var orange = new Color32(190, 120, 40);
        // 가장 가까운 색(RGB·OKLab 모두)은 밝기가 비슷한 주황이지만, 색상 먼저는 색상이 같은 어두운 빨강.
        Assert.Equal(orange, new PaletteRemap([darkRed, orange]).Nearest(src));
        Assert.Equal(orange, new PaletteRemap([darkRed, orange], PaletteMatchRule.Nearest, PaletteMatchDistance.OkLab).Nearest(src));
        Assert.Equal(darkRed, new PaletteRemap([darkRed, orange], PaletteMatchRule.HueFirst).Nearest(src));
    }

    [Fact]
    public void HueFirst_MapsGraysByLightnessAmongGrayEntries()
    {
        var remap = new PaletteRemap([new Color32(100, 0, 0), new Color32(190, 120, 40), G(100), White], PaletteMatchRule.HueFirst);
        Assert.Equal(G(100), remap.Nearest(G(128)));
        Assert.Equal(White, remap.Nearest(G(240)));
    }

    [Fact]
    public void LightnessOnly_IgnoresHue()
    {
        var remap = new PaletteRemap([Gb0, Gb1, Gb2, Gb3], PaletteMatchRule.LightnessOnly);
        // 빨강(0.63)과 초록(0.58)은 색상이 전혀 달라도 밝기가 비슷해 같은 칸(0.70).
        Assert.Equal(Gb2, remap.Nearest(Red));
        Assert.Equal(Gb2, remap.Nearest(Green));
        Assert.Equal(Gb0, remap.Nearest(DarkBlue));
        Assert.Equal(Gb3, remap.Nearest(LightYellow));
    }

    [Fact]
    public void LightnessOnly_Dither_UsesOnlyTheTwoColorsAroundTheLightness()
    {
        var img = new PixelImage(4, 4);
        img.Clear(G(128)); // 밝기 0.60: Gb1(0.45)과 Gb2(0.70) 사이
        new PaletteRemap([Gb0, Gb1, Gb2, Gb3], PaletteMatchRule.LightnessOnly).Apply(img, PaletteDither.Bayer4);
        Assert.All(img.Pixels, c => Assert.True(c == Gb1 || c == Gb2));
        Assert.Contains(Gb1, img.Pixels);
        Assert.Contains(Gb2, img.Pixels);

        // 선형광 0.5 회색은 검정·흰색 반반(가장 가까운 색 규칙의 디더와 같은 섞기).
        var half = new PixelImage(4, 4);
        half.Clear(G(188));
        new PaletteRemap([Black, White], PaletteMatchRule.LightnessOnly).Apply(half, PaletteDither.Bayer4);
        Assert.Equal(8, half.Pixels.Count(c => c == White));
    }

    [Theory]
    [InlineData(PaletteMatchRule.Nearest, PaletteDither.None)]
    [InlineData(PaletteMatchRule.Nearest, PaletteDither.Bayer4)]
    [InlineData(PaletteMatchRule.BrightnessOrder, PaletteDither.None)]
    [InlineData(PaletteMatchRule.BrightnessOrder, PaletteDither.Bayer4)]
    [InlineData(PaletteMatchRule.PaletteOrder, PaletteDither.None)]
    [InlineData(PaletteMatchRule.PaletteOrder, PaletteDither.Bayer4)]
    [InlineData(PaletteMatchRule.HueFirst, PaletteDither.None)]
    [InlineData(PaletteMatchRule.HueFirst, PaletteDither.Bayer4)]
    [InlineData(PaletteMatchRule.LightnessOnly, PaletteDither.None)]
    [InlineData(PaletteMatchRule.LightnessOnly, PaletteDither.Bayer4)]
    public void EveryRule_KeepsAlpha_AndLeavesTransparentUntouched(PaletteMatchRule rule, PaletteDither dither)
    {
        var img = new PixelImage(3, 1);
        img.Pixels[0] = new Color32(240, 20, 20, 128);
        img.Pixels[1] = new Color32(12, 34, 56, 0);
        img.Pixels[2] = new Color32(10, 10, 10, 255);
        var source = new HashSet<Color32>();
        PaletteRemap.AddOpaqueColors(img, source);
        Assert.Equal(2, source.Count);
        new PaletteRemap([Black, Red, White], rule, PaletteMatchDistance.OkLab, source).Apply(img, dither);
        Assert.Equal(128, img.Pixels[0].A);
        Assert.Contains(img.Pixels[0].WithAlpha(255), new[] { Black, Red, White });
        Assert.Equal(new Color32(12, 34, 56, 0), img.Pixels[1]);
        Assert.Equal(255, img.Pixels[2].A);
        Assert.Contains(img.Pixels[2], new[] { Black, Red, White });
    }

    private static Color32 G(byte v) => new(v, v, v);

    private static PixelImage Row(params Color32[] colors)
    {
        var img = new PixelImage(colors.Length, 1);
        for (int i = 0; i < colors.Length; i++) img.Pixels[i] = colors[i];
        return img;
    }
}
