using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class PaletteRemapTests
{
    private static readonly Color32 Black = new(0, 0, 0);
    private static readonly Color32 White = new(255, 255, 255);
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Green = new(0, 255, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    [Fact]
    public void PaletteColors_StayExactly_EvenWithDither()
    {
        var palette = new[] { Black, White, Red, new Color32(40, 90, 200) };
        var img = new PixelImage(4, 4);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = palette[i % palette.Length];
        var before = img.Clone();
        var remap = new PaletteRemap(palette);
        Assert.Equal(0, remap.Apply(img));
        Assert.Equal(0, remap.Apply(img, PaletteDither.Bayer4));
        Assert.True(img.ContentEquals(before));
    }

    [Theory]
    [InlineData(250, 10, 10, 255, 0, 0)]
    [InlineData(30, 30, 30, 0, 0, 0)]
    [InlineData(200, 200, 210, 255, 255, 255)]
    [InlineData(20, 220, 40, 0, 255, 0)]
    [InlineData(10, 20, 180, 0, 0, 255)]
    public void Nearest_PicksClosestColor(byte r, byte g, byte b, byte er, byte eg, byte eb)
    {
        var remap = new PaletteRemap([Black, White, Red, Green, Blue]);
        Assert.Equal(new Color32(er, eg, eb), remap.Nearest(new Color32(r, g, b)));
    }

    [Fact]
    public void Nearest_WeighsGreenMoreThanBlue_TiesGoToFirst()
    {
        // 순수 RGB 거리로는 같지만(둘 다 한 채널만 60 차이), redmean은 초록 차이를 파랑 차이보다 크게 본다.
        var a = new Color32(100, 160, 100);
        var b = new Color32(100, 100, 160);
        Assert.Equal(b, new PaletteRemap([a, b]).Nearest(new Color32(100, 100, 100)));
        // 초록 30 + 파랑 30 차이씩이면 두 색까지 거리가 같다 → 팔레트 앞쪽 색.
        Assert.Equal(a, new PaletteRemap([a, b]).Nearest(new Color32(100, 130, 130)));
        Assert.Equal(b, new PaletteRemap([b, a]).Nearest(new Color32(100, 130, 130)));
    }

    [Fact]
    public void Apply_KeepsAlpha_AndLeavesTransparentUntouched()
    {
        var img = new PixelImage(3, 1);
        img.Pixels[0] = new Color32(240, 20, 20, 128);
        img.Pixels[1] = new Color32(12, 34, 56, 0); // 완전 투명(RGB 값이 있어도 그대로)
        img.Pixels[2] = new Color32(10, 10, 10, 255);
        int n = new PaletteRemap([Black, Red]).Apply(img);
        Assert.Equal(2, n);
        Assert.Equal(new Color32(255, 0, 0, 128), img.Pixels[0]);
        Assert.Equal(new Color32(12, 34, 56, 0), img.Pixels[1]);
        Assert.Equal(Black, img.Pixels[2]);
    }

    [Fact]
    public void Palette_IgnoresTransparentEntries_AndRejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new PaletteRemap([]));
        Assert.Throws<ArgumentException>(() => new PaletteRemap([Color32.Transparent, new Color32(255, 0, 0, 0)]));
        var remap = new PaletteRemap([Color32.Transparent, Blue, Blue]);
        Assert.Equal(1, remap.Count);
        Assert.Equal(Blue, remap.Nearest(White));
    }

    [Fact]
    public void Apply_RespectsClip()
    {
        var img = new PixelImage(4, 1);
        img.Clear(new Color32(200, 30, 30));
        int n = new PaletteRemap([Red]).Apply(img, clip: new System.Drawing.Rectangle(1, 0, 2, 1));
        Assert.Equal(2, n);
        Assert.Equal(new Color32(200, 30, 30), img.Pixels[0]);
        Assert.Equal(Red, img.Pixels[1]);
        Assert.Equal(Red, img.Pixels[2]);
        Assert.Equal(new Color32(200, 30, 30), img.Pixels[3]);
    }

    [Fact]
    public void Bayer_MixesTwoColorsForMidTone_OnlyPaletteColorsOut()
    {
        var img = new PixelImage(4, 4);
        img.Clear(new Color32(128, 128, 128));
        new PaletteRemap([Black, White]).Apply(img, PaletteDither.Bayer4);
        Assert.All(img.Pixels, c => Assert.True(c == Black || c == White));
        Assert.Contains(Black, img.Pixels);
        Assert.Contains(White, img.Pixels);
    }

    [Fact]
    public void Bayer_MixesInLinearLight_SoHalfLinearGrayIsHalfBlackHalfWhite()
    {
        // sRGB 188 ≈ 선형광 0.5: 눈에는 검정·흰색 반반 무늬가 이 회색으로 보인다(sRGB 값으로 섞으면 흰색이 3/4쯤 됨).
        var img = new PixelImage(4, 4);
        img.Clear(new Color32(188, 188, 188));
        new PaletteRemap([Black, White]).Apply(img, PaletteDither.Bayer4);
        Assert.Equal(8, img.Pixels.Count(c => c == White));
        Assert.Equal(8, img.Pixels.Count(c => c == Black));
    }

    [Fact]
    public void Bayer_DoesNotScatterCoolPixelsIntoNearlyExactSkinColor()
    {
        var source = new Color32(253, 231, 200);
        var skin = new Color32(253, 230, 197);
        var blueGray = new Color32(217, 216, 226);
        var img = new PixelImage(4, 4);
        img.Clear(source);

        new PaletteRemap([skin, blueGray]).Apply(img, PaletteDither.Bayer4);

        Assert.All(img.Pixels, c => Assert.Equal(skin, c));
    }

    [Fact]
    public void Bayer_FlatColorUsesAtMostTwoPaletteColors_RepeatingEvery4px()
    {
        // 팔레트에 없는 한 가지 색(크림)은 많은 팔레트 색 중에서도 두 색의 고정 무늬로만 바뀐다.
        Color32[] palette = [Black, White, Red, new(0xEE, 0xC3, 0x9A), new(0xCB, 0xDB, 0xFC), new(0x59, 0x56, 0x52), new(0x22, 0x20, 0x34)];
        var img = new PixelImage(8, 8);
        img.Clear(new Color32(0xED, 0xE4, 0xDA));
        new PaletteRemap(palette).Apply(img, PaletteDither.Bayer4);
        Assert.InRange(img.Pixels.Distinct().Count(), 1, 2);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(img[x, y], img[x + 4, y + 4]);
    }

    [Fact]
    public void MapCels_IsOneUndoStep_SkipsLocked()
    {
        var p = new SpriteProject(2, 2);
        p.AddLayer();
        p.AddFrame();
        var off = new Color32(250, 5, 5);
        p.SetCel(0, 0, Filled(off));
        p.SetCel(0, 1, Filled(off));
        p.SetCel(1, 0, Filled(off));
        p.SetLayerProperties(1, locked: true);
        var remap = new PaletteRemap([Red]);
        int depth = p.History.UndoCount;

        int cels = p.MapCels("팔레트 색으로", (_, _) => true, img => remap.Remapped(img));

        Assert.Equal(2, cels);
        Assert.Equal(depth + 1, p.History.UndoCount);
        Assert.Equal(Red, p.GetCel(0, 0)!.Pixels[0]);
        Assert.Equal(Red, p.GetCel(0, 1)!.Pixels[0]);
        Assert.Equal(off, p.GetCel(1, 0)!.Pixels[0]);
        Assert.True(p.Undo());
        Assert.Equal(off, p.GetCel(0, 0)!.Pixels[0]);
        Assert.Equal(off, p.GetCel(0, 1)!.Pixels[0]);
        Assert.Equal(0, p.MapCels("팔레트 색으로", (_, _) => true, _ => null));
        Assert.Equal(depth, p.History.UndoCount);
    }

    [Fact]
    public void MapCels_ReachesHiddenSkinCels()
    {
        var p = new SpriteProject(4, 4);
        var g = p.AddGroup("body");
        var head = p.AddPart(g, "Head", 2, 2, new System.Drawing.Point(0, 0));
        var off = new Color32(250, 5, 5);
        p.BeginCelStroke(p.IndexOf(head), 0).Set(0, 0, off);
        p.CommitCelStroke("점");
        var knight = p.AddSkin(g, "knight");
        p.BeginCelStroke(p.IndexOf(head), 0).Set(1, 1, new Color32(5, 5, 250));
        p.CommitCelStroke("점");
        p.SetActiveSkin(g, g.BaseSkin!);

        var remap = new PaletteRemap([Red, Blue]);
        Assert.Equal(2, p.MapCels("팔레트 색으로", (_, _) => true, img => remap.Remapped(img)));

        Assert.Equal(Red, p.GetSkinCel(head, p.Frames[0], g.BaseSkin!)![0, 0]);
        var own = p.GetOwnCel(head, p.Frames[0], knight)!;
        Assert.Equal((Red, Blue), (own[0, 0], own[1, 1]));
    }

    private static PixelImage Filled(Color32 c)
    {
        var img = new PixelImage(2, 2);
        img.Clear(c);
        return img;
    }
}
