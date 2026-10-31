using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>홀로그램: 보이는 픽셀만(+ 테두리 빛), 주사선, 명암 순서, 시드, 글리치 여백, 저장, 영구 적용.</summary>
public class HologramEffectTests
{
    /// <summary>모든 부분을 끈 홀로그램(고칠 부분만 켜서 씀).</summary>
    private static readonly HologramEffect Plain = new()
    {
        TintStrength = 0, ScanStrength = 0, Band = false, Opacity = 100, FlickerRate = 0, GlitchRate = 0, EdgeGlow = false, RgbSplit = false,
    };

    /// <summary>가운데 4×3에 서로 다른 색(10×10).</summary>
    private static PixelImage Sprite()
    {
        var img = new PixelImage(10, 10);
        for (int y = 3; y < 6; y++)
            for (int x = 3; x < 7; x++)
                img[x, y] = new Color32((byte)(x * 30), (byte)(y * 40), (byte)(255 - x * 20));
        return img;
    }

    /// <summary>범위 0~<paramref name="span"/>의 <paramref name="step"/>번째 프레임(여백 포함)과 원본 자리.</summary>
    private static (PixelImage Image, Rectangle Source) At(PixelImage src, HologramEffect effect, int step, int span)
    {
        var e = effect with { Frames = new FrameRange(0, span) };
        var (img, x, y) = LayerEffects.Render(src, LayerEffects.At([e], step, span + 1));
        return (img, new Rectangle(-x, -y, src.Width, src.Height));
    }

    private static double Lum(Color32 c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    [Fact]
    public void ChangesOnlyOpaquePixels_EdgeGlowAddsOnlyTheOnePixelRing()
    {
        var src = Sprite();
        var holo = new HologramEffect { GlitchRate = 0, EdgeGlow = false };
        for (int s = 0; s <= 7; s++)
        {
            var (img, rect) = At(src, holo, s, 7);
            Assert.Equal(src.Width, img.Width); // 여백 없음
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    if (src[x, y].A == 0) Assert.Equal(0, img[x, y].A);
                    else Assert.NotEqual(src[x, y], img[x, y]);
                }
        }
        var (glow, r) = At(src, holo with { EdgeGlow = true, EdgeOpacity = 100, Opacity = 100, FlickerRate = 0 }, 0, 7);
        for (int y = 0; y < glow.Height; y++)
            for (int x = 0; x < glow.Width; x++)
            {
                int sx = x - r.X, sy = y - r.Y;
                bool solid = (uint)sx < 10 && (uint)sy < 10 && src[sx, sy].A != 0;
                bool ring = !solid && sx >= 2 && sx <= 7 && sy >= 2 && sy <= 6 && (sx is >= 3 and <= 6 || sy is >= 3 and <= 5); // 4방향 이웃
                if (ring) Assert.Equal(HologramEffect.DefaultColor, glow[x, y]);
                else if (!solid) Assert.Equal(0, glow[x, y].A);
            }
    }

    [Fact]
    public void ScanlineRows_DifferFromNeighboursByStrength_AndScroll()
    {
        var src = new PixelImage(6, 9);
        var grey = new Color32(200, 100, 50);
        src.Clear(grey);
        var scan = Plain with { ScanGap = 3, ScanStrength = 40, ScanScroll = false };
        var (img, _) = At(src, scan, 2, 4);
        for (int y = 0; y < 9; y++)
            Assert.Equal(y % 3 == 0 ? new Color32(120, 60, 30) : grey, img[2, y]);
        var (faded, _) = At(src, scan with { ScanStyle = HologramScanStyle.Fade }, 2, 4);
        for (int y = 0; y < 9; y++)
            Assert.Equal(y % 3 == 0 ? grey.WithAlpha(153) : grey, faded[2, y]);
        // 흐르기: 진행 0 → 1에 한 간격 아래로(끝 = 처음, 이음매 없음)
        var flowing = scan with { ScanScroll = true };
        int Row(int step) => Enumerable.Range(0, 3).Single(y => At(src, flowing, step, 6).Image[0, y] != grey);
        Assert.Equal([0, 0, 1, 1, 2, 2, 0], Enumerable.Range(0, 7).Select(Row));
    }

    [Fact]
    public void Tint_PreservesLuminanceOrder()
    {
        Color32[] colours =
        [
            new(0, 0, 0), new(40, 10, 90), new(200, 0, 0), new(0, 120, 0), new(90, 90, 90), new(30, 200, 240),
            new(250, 200, 20), new(230, 230, 230), new(255, 255, 255), new(0, 0, 255), new(140, 60, 160),
        ];
        var src = new PixelImage(colours.Length, 1);
        for (int i = 0; i < colours.Length; i++) src[i, 0] = colours[i];
        foreach (int strength in new[] { 50, 85, 100 })
        {
            var (img, _) = At(src, Plain with { TintStrength = strength }, 0, 1);
            var order = Enumerable.Range(0, colours.Length).OrderBy(i => Lum(colours[i])).ToList();
            for (int k = 1; k < order.Count; k++)
                Assert.True(Lum(img[order[k], 0]) >= Lum(img[order[k - 1], 0]) - 0.5, $"{strength}%: {colours[order[k - 1]]} → {colours[order[k]]}");
            if (strength == 100) // 모두 홀로그램 색 계열(채널 비율이 색과 같은 방향)
                Assert.All(img.Pixels, p => Assert.True(p.B >= p.G && p.G >= p.R));
        }
    }

    [Fact]
    public void Glitch_DeterministicBySeed_DifferentSeedDiffers_NeverClipped()
    {
        var src = new PixelImage(8, 12);
        src.Clear(new Color32(90, 160, 200)); // 원본 끝까지 차 있어도 밀린 조각이 여백 안
        var holo = new HologramEffect { GlitchRate = 50, GlitchShift = 4, Seed = 3 };
        var frames = Enumerable.Range(0, 8).Select(s => At(src, holo, s, 7).Image).ToList();
        Assert.All(Enumerable.Range(0, 8), s => Assert.True(frames[s].ContentEquals(At(src, holo with { }, s, 7).Image)));
        Assert.Contains(Enumerable.Range(0, 8), s => !frames[s].ContentEquals(At(src, holo with { Seed = 4 }, s, 7).Image));
        // 글리치 프레임마다 조각 밀림 ≤ 최대 밀림, 밀어도 픽셀이 잘리지 않음
        var glitchOnly = Plain with { GlitchRate = 100, GlitchShift = 4, Seed = 9 };
        Assert.All(Enumerable.Range(0, 20), s =>
        {
            var slices = glitchOnly.GlitchSlices(s, 12);
            Assert.InRange(slices.Count, 1, 3);
            Assert.All(slices, c => Assert.True(Math.Abs(c.Shift) is >= 1 and <= 4 && c.Height is >= 1 and <= 3));
            var (img, _) = At(src, glitchOnly, s, 19);
            Assert.Equal(src.Pixels.Length, img.Pixels.Count(p => p.A != 0));
        });
    }

    [Fact]
    public void Padding_IsGlitchShiftSideways_PlusEdgeGlow()
    {
        Assert.Equal(new EffectPadding(5, 0, 5, 0), LayerEffects.Padding([new HologramEffect { GlitchShift = 5, EdgeGlow = false }], 10, 10));
        Assert.Equal(new EffectPadding(3, 1, 3, 1), LayerEffects.Padding([new HologramEffect { GlitchShift = 2 }], 10, 10));
        Assert.True(LayerEffects.Padding([new HologramEffect { GlitchRate = 0, EdgeGlow = false }], 10, 10).IsNone);
    }

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        var p = new SpriteProject(8, 8);
        for (int i = 0; i < 2; i++) p.DuplicateFrame(0);
        LayerEffect[] fx =
        [
            new HologramEffect
            {
                Color = new Color32(255, 80, 200), TintStrength = 60, ScanGap = 4, ScanStrength = 70, ScanStyle = HologramScanStyle.Fade, ScanScroll = false,
                Band = false, BandSize = 5, BandStrength = 30, Opacity = 55, Dither = true, FlickerRate = 33, FlickerStrength = 80, GlitchRate = 45, GlitchShift = 7,
                RgbSplit = false, EdgeGlow = false, EdgeOpacity = 20, Seed = 4321, Easing = EffectEasing.EaseInOut, Repeat = EffectRepeat.Loop, RepeatCount = 3,
                Frames = new FrameRange(0, 1),
            },
            new HologramEffect { Enabled = false },
        ];
        p.SetLayerEffects(0, fx);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        Assert.Equal(fx, ProjectIO.Load(stream).Layers[0].Effects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bake_EqualsRenderedOutput(bool dither)
    {
        var p = new SpriteProject(16, 16);
        for (int i = 0; i < 7; i++) p.DuplicateFrame(0);
        for (int f = 0; f < 8; f++)
        {
            var cel = new PixelImage(16, 16);
            for (int y = 4; y < 12; y++)
                for (int x = 4 + f % 2; x < 11; x++)
                    cel[x, y] = new Color32((byte)(x * 20), (byte)(y * 15), 120);
            p.SetCel(0, f, cel);
        }
        p.SetLayerEffects(0, [new HologramEffect { GlitchRate = 50, FlickerRate = 50, Dither = dither, ScanGap = 3 }]);
        var before = Enumerable.Range(0, 8).Select(f => p.Flatten(f)).ToList();
        p.BakeLayerEffects(0, [0]);
        Assert.Empty(p.Layers[0].Effects);
        Assert.All(Enumerable.Range(0, 8), f => Assert.True(before[f].ContentEquals(p.Flatten(f)), $"frame {f}"));
    }
}
