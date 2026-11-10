using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class FillColorMatchTests
{
    private static bool Match(Color32 seed, Color32 c, int tolerance, FillColorMode mode)
        => new FillColorMatcher(seed, tolerance, mode).Matches(c);

    public static TheoryData<FillColorMode> Modes()
    {
        var data = new TheoryData<FillColorMode>();
        foreach (var m in Enum.GetValues<FillColorMode>()) data.Add(m);
        return data;
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Zero_ExactOnly_Hundred_Everything(FillColorMode mode)
    {
        var black = new Color32(0, 0, 0);
        Assert.True(Match(black, black, 0, mode));
        Assert.False(Match(black, new Color32(0, 0, 1), 0, mode));
        Assert.False(Match(new Color32(40, 40, 40), new Color32(60, 60, 60, 254), 0, mode));
        // 투명끼리는 RGB가 달라도 같은 색
        Assert.True(Match(Color32.Transparent, new Color32(9, 9, 9, 0), 0, mode));

        Assert.True(Match(black, new Color32(255, 255, 255), 255, mode));
        Assert.True(Match(new Color32(200, 50, 50), new Color32(128, 128, 128), 255, mode));
        Assert.True(Match(Color32.Transparent, new Color32(255, 255, 255), 255, mode));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Alpha_TransparentComparesAlphaOnly_VisibleNeedsAlphaWithin(FillColorMode mode)
    {
        Assert.False(Match(Color32.Transparent, new Color32(200, 10, 10, 5), 4, mode));
        Assert.True(Match(Color32.Transparent, new Color32(200, 10, 10, 5), 5, mode));
        // 같은 RGB라도 알파 차이가 허용 오차 밖이면 다른 색
        Assert.False(Match(new Color32(90, 90, 90), new Color32(90, 90, 90, 200), 50, mode));
        Assert.True(Match(new Color32(90, 90, 90), new Color32(90, 90, 90, 200), 55, mode));
    }

    [Fact]
    public void ChannelMax_IsDefault_AndMatchesOldRule()
    {
        // 한 채널 차이 21 — 채널 최대는 21부터, 기본 생성도 같은 방식
        var seed = new Color32(50, 50, 50);
        var c = new Color32(50, 50, 71);
        Assert.False(new FillColorMatcher(seed, 20).Matches(c));
        Assert.True(new FillColorMatcher(seed, 21).Matches(c));
    }

    [Fact]
    public void RgbDistance_UsesEuclideanScaledBySqrt3()
    {
        // (0,200,0)–(0,230,0): 채널 차 30, 직선 거리 30 = 17.3·√3
        var seed = new Color32(0, 200, 0);
        var c = new Color32(0, 230, 0);
        Assert.True(Match(seed, c, 18, FillColorMode.RgbDistance));
        Assert.False(Match(seed, c, 17, FillColorMode.RgbDistance));
        Assert.False(Match(seed, c, 18, FillColorMode.ChannelMax));
    }

    [Fact]
    public void OkLab_SeesDarkStepsAsLargerThanChannels()
    {
        // 검정–(12,12,12): 채널 차는 12뿐이지만 눈에는 뚜렷(OKLab 0.154 ≈ 39/255)
        var black = new Color32(0, 0, 0);
        var dark = new Color32(12, 12, 12);
        Assert.True(Match(black, dark, 12, FillColorMode.ChannelMax));
        Assert.False(Match(black, dark, 12, FillColorMode.OkLab));
        Assert.True(Match(black, dark, 40, FillColorMode.OkLab));
    }

    [Fact]
    public void Ciede2000_SeesBrightGreenStepAsSmall()
    {
        // (0,200,0)–(0,230,0): ΔE00 ≈ 7.2(≈ 15.4/255 × 120), 채널 차 30
        var seed = new Color32(0, 200, 0);
        var c = new Color32(0, 230, 0);
        Assert.True(Match(seed, c, 16, FillColorMode.Ciede2000));
        Assert.False(Match(seed, c, 15, FillColorMode.Ciede2000));
        Assert.False(Match(seed, c, 16, FillColorMode.ChannelMax));
    }

    [Fact]
    public void Hue_IgnoresLightness_GraysGroupTogether()
    {
        var red = new Color32(200, 50, 50);
        Assert.True(Match(red, new Color32(100, 20, 20), 1, FillColorMode.Hue));
        Assert.False(Match(red, new Color32(100, 20, 20), 99, FillColorMode.ChannelMax));
        // 빨강–초록 색상각 약 117도(≈ 166/255 × 180)
        Assert.False(Match(red, new Color32(50, 200, 50), 160, FillColorMode.Hue));
        Assert.True(Match(red, new Color32(50, 200, 50), 170, FillColorMode.Hue));
        // 무채색끼리는 같은 색상, 무채색과 유채색은 가장 멂
        Assert.True(Match(new Color32(40, 40, 40), new Color32(220, 220, 220), 1, FillColorMode.Hue));
        Assert.False(Match(new Color32(128, 128, 128), red, 254, FillColorMode.Hue));
    }

    [Fact]
    public void FloodRegion_UsesMode()
    {
        // 빨강 명암 세 칸 + 파랑: 색상만이면 빨강 명암을 모두, 채널 최대 5%면 씨앗만
        var img = new PixelImage(4, 1);
        img.Set(0, 0, new Color32(200, 50, 50));
        img.Set(1, 0, new Color32(150, 35, 35));
        img.Set(2, 0, new Color32(100, 20, 20));
        img.Set(3, 0, new Color32(50, 50, 200));
        Assert.Equal(3, Raster.FloodRegion(img, 0, 0, tolerance: 13, mode: FillColorMode.Hue).Count);
        Assert.Equal(1, Raster.FloodRegion(img, 0, 0, tolerance: 13).Count);
        Assert.Equal(3, Raster.FloodFill(img.Clone(), 0, 0, new Color32(0, 0, 0), tolerance: 13, mode: FillColorMode.Hue));
    }
}
