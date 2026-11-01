using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>움직이는 효과(피격 번쩍임 ~ 충격파 고리): 범위 양 끝, 시드, 옮김 한계, 다른 프레임, 여백, 저장.</summary>
public class MotionEffectTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    /// <summary>가운데에 왼쪽 위가 빨강·나머지 파랑인 비대칭 4×3 그림(10×10).</summary>
    private static PixelImage Sprite()
    {
        var img = new PixelImage(10, 10);
        for (int y = 3; y < 6; y++)
            for (int x = 3; x < 7; x++)
                img[x, y] = Blue;
        img[3, 3] = Red;
        img[4, 3] = Red;
        return img;
    }

    private static int Opaque(PixelImage img) => img.Pixels.Count(p => p.A != 0);

    /// <summary>범위 0~<paramref name="span"/>의 <paramref name="step"/>번째 프레임(여백 포함)과 원본 자리.</summary>
    private static (PixelImage Image, Rectangle Source) At(PixelImage src, AnimatedEffect effect, int step, int span)
    {
        var e = effect with { Frames = new FrameRange(0, span) };
        var (img, x, y) = LayerEffects.Render(src, LayerEffects.At([e], step, span + 1));
        return (img, new Rectangle(-x, -y, src.Width, src.Height));
    }

    /// <summary>원본 자리로 자른 그림(밖으로 번진 픽셀이 없을 때 원본과 비교).</summary>
    private static PixelImage Cropped(PixelImage src, AnimatedEffect effect, int step, int span)
    {
        var (img, rect) = At(src, effect, step, span);
        return img.Crop(rect);
    }

    private static bool Same(PixelImage src, AnimatedEffect effect, int step, int span)
    {
        var (img, rect) = At(src, effect, step, span);
        return img.Crop(rect).ContentEquals(src) && Opaque(img) == Opaque(src);
    }

    // ───────────── 피격 번쩍임 · 깜빡이기 ─────────────

    [Fact]
    public void HitFlash_FlashesOnFirstFrame_NormalAtRangeEnd()
    {
        var src = Sprite();
        var flash = new HitFlashEffect { Color = Color32.White, Count = 2 };
        var first = Cropped(src, flash, 0, 4);
        Assert.All(first.Pixels.Where(p => p.A != 0), p => Assert.Equal(Color32.White, p));
        Assert.Equal(Opaque(src), Opaque(first));
        Assert.True(Same(src, flash, 1, 4)); // 2번 → 한 번 = 2프레임: 켜짐, 꺼짐
        Assert.False(Same(src, flash, 2, 4));
        Assert.True(Same(src, flash, 4, 4));
        Assert.True(Same(src, flash with { Shape = HitFlashShape.Fade }, 4, 4));
    }

    [Fact]
    public void Blink_FramesPattern_AndCountModeSplitsRangeEvenly()
    {
        var src = Sprite();
        var blink = new BlinkEffect { OnFrames = 2, OffFrames = 1 };
        bool[] hidden = [.. Enumerable.Range(0, 6).Select(s => Opaque(Cropped(src, blink, s, 5)) == 0)];
        Assert.Equal([false, false, true, false, false, true], hidden);
        // 횟수: 8프레임을 보임·숨김 4칸으로 고르게(2번 숨음)
        var count = new BlinkEffect { Timing = BlinkTiming.Count, Count = 2, Hide = BlinkHide.Dither };
        bool[] counted = [.. Enumerable.Range(0, 8).Select(s => !Same(src, count, s, 7))];
        Assert.Equal([false, false, true, true, false, false, true, true], counted);
        Assert.Equal(Opaque(src) / 2, Opaque(Cropped(src, count, 2, 7))); // 4×3 바둑판 = 절반
    }

    // ───────────── 흔들기 · 둥실둥실 ─────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shake_OffsetsAreIntegersBoundedBySettings_StillAtEnds(bool regular)
    {
        var shake = new ShakeEffect { Axis = EffectAxis.Both, Amount = 3, Count = 5, Decay = false, Regular = regular, Seed = 42 };
        var offsets = Enumerable.Range(0, 101).Select(i => shake.Offset(i / 100.0)).ToList();
        Assert.All(offsets, o => Assert.True(Math.Abs(o.X) <= 3 && Math.Abs(o.Y) <= 3));
        Assert.Equal((0, 0), offsets[0]);
        Assert.Equal((0, 0), offsets[100]);
        Assert.Contains(offsets, o => Math.Abs(o.X) == 3 || Math.Abs(o.Y) == 3);
        Assert.All(Enumerable.Range(1, 99).Select(i => (shake with { Axis = EffectAxis.Horizontal }).Offset(i / 100.0)), o => Assert.Equal(0, o.Y));
    }

    [Fact]
    public void Shake_RandomIsDeterministicBySeed_AndDecayShrinks()
    {
        var a = new ShakeEffect { Amount = 4, Count = 8, Decay = false, Seed = 7 };
        var steps = Enumerable.Range(1, 15).Select(i => i / 16.0).ToList();
        Assert.Equal(steps.Select(a.Offset), steps.Select((a with { }).Offset));
        Assert.NotEqual(steps.Select(a.Offset), steps.Select((a with { Seed = 8 }).Offset));
        var decayed = a with { Decay = true };
        Assert.All(steps, u => Assert.True(Math.Abs(decayed.Offset(u).X) <= Math.Ceiling(4 * (1 - u))));
    }

    [Fact]
    public void Bob_LoopsSeamlessly_BoundedByHeight()
    {
        var src = Sprite();
        var bob = new BobEffect { Height = 2, Cycles = 2 };
        Assert.True(Same(src, bob, 0, 8));
        Assert.True(Same(src, bob, 8, 8));
        var offsets = Enumerable.Range(0, 9).Select(s => bob.Offset(s / 8.0)).ToList();
        Assert.All(offsets, o => Assert.True(o.X == 0 && Math.Abs(o.Y) <= 2));
        Assert.Equal((0, -2), offsets[1]); // 2번 = 4프레임에 한 번, 위로 먼저
        Assert.Equal((0, 2), offsets[3]);
        // 위로 2px: 원본 자리에서 위로 옮긴 그림
        var (img, rect) = At(src, bob, 1, 8);
        Assert.Equal(Red, img[rect.X + 3, rect.Y + 1]);
    }

    // ───────────── 나타나기 / 사라지기 ─────────────

    [Theory]
    [InlineData(false, 0, 51, 101)]
    [InlineData(true, 101, 51, 0)]
    public void Fade_ChangesAlphaAcrossRangeWithoutChangingSource(bool fadeOut, int start, int middle, int end)
    {
        var src = new PixelImage(2, 1);
        src[0, 0] = new Color32(10, 20, 30, 101);
        var fade = new FadeEffect { FadeOut = fadeOut };

        Assert.Equal(start, Cropped(src, fade, 0, 4)[0, 0].A);
        var mid = Cropped(src, fade, 2, 4);
        Assert.Equal(middle, mid[0, 0].A);
        Assert.Equal((10, 20, 30), (mid[0, 0].R, mid[0, 0].G, mid[0, 0].B));
        Assert.Equal(Color32.Transparent, mid[1, 0]);
        Assert.Equal(end, Cropped(src, fade, 4, 4)[0, 0].A);
        Assert.Equal(101, src[0, 0].A);
    }

    [Fact]
    public void Fade_RespectsRangeAndEasing()
    {
        var src = new PixelImage(1, 1);
        src[0, 0] = Blue;
        var fade = new FadeEffect { Frames = new FrameRange(1, 3), Easing = EffectEasing.EaseIn };
        Assert.Empty(LayerEffects.At([fade], 0, 5));
        Assert.Equal(64, LayerEffects.RenderClipped(src, LayerEffects.At([fade], 2, 5))[0, 0].A);
        Assert.Empty(LayerEffects.At([fade], 4, 5));
    }

    [Theory]
    [InlineData(AppearStyle.Fade)]
    [InlineData(AppearStyle.Dither)]
    [InlineData(AppearStyle.Wipe)]
    [InlineData(AppearStyle.Scatter)]
    public void Appear_StartsEmptyEndsFull_DisappearReverse(AppearStyle style)
    {
        var src = Sprite();
        var appear = new AppearEffect { Style = style, Edge = 2 };
        Assert.Equal(0, Opaque(Cropped(src, appear, 0, 6)));
        Assert.True(Same(src, appear, 6, 6));
        var disappear = appear with { Disappear = true };
        Assert.True(Same(src, disappear, 0, 6));
        Assert.Equal(0, Opaque(Cropped(src, disappear, 6, 6)));
        // 중간: 일부만 보이거나(켜고 끄기) 옅음(투명도), 픽셀 색은 그대로
        var mid = Cropped(src, appear, 3, 6);
        if (style == AppearStyle.Fade) Assert.All(mid.Pixels.Where(p => p.A != 0), p => Assert.InRange(p.A, 1, 254));
        else Assert.InRange(Opaque(mid), 1, Opaque(src) - 1);
    }

    [Fact]
    public void AppearScatter_DeterministicBySeed_GrowsMonotonically()
    {
        var src = new PixelImage(12, 12);
        src.Clear(Blue);
        var a = new AppearEffect { Style = AppearStyle.Scatter, Seed = 5 };
        Assert.True(Cropped(src, a, 3, 10).ContentEquals(Cropped(src, a with { }, 3, 10)));
        Assert.False(Cropped(src, a, 3, 10).ContentEquals(Cropped(src, a with { Seed = 6 }, 3, 10)));
        var earlier = Cropped(src, a, 3, 10);
        var later = Cropped(src, a, 6, 10);
        Assert.All(Enumerable.Range(0, earlier.Pixels.Length), i => Assert.True(earlier.Pixels[i].A == 0 || later.Pixels[i].A != 0));
    }

    [Fact]
    public void AppearWipe_FromTopRevealsTopRowsFirst()
    {
        var src = new PixelImage(6, 8);
        src.Clear(Blue);
        var mid = Cropped(src, new AppearEffect { Style = AppearStyle.Wipe, From = WipeDirection.Top, Edge = 0 }, 1, 2);
        Assert.All(Enumerable.Range(0, 6), x => Assert.Equal(Blue, mid[x, 0]));
        Assert.All(Enumerable.Range(0, 6), x => Assert.Equal(0, mid[x, 7].A));
    }

    // ───────────── 녹아내리기 ─────────────

    [Fact]
    public void Melt_StartsIntact_DripsDownOnly_GoneAtEnd_Deterministic()
    {
        var src = Sprite();
        var melt = new MeltEffect { Distance = 6, Irregularity = 80, Seed = 3 };
        Assert.True(Same(src, melt, 0, 6));
        var (img, rect) = At(src, melt, 3, 6);
        Assert.Equal(src.Height + 6, img.Height); // 아래로 거리만큼 여백
        var bounds = img.GetOpaqueBounds();
        Assert.True(bounds.Top >= rect.Y + 3 && bounds.Bottom > rect.Y + 6); // 위로는 안 올라가고 아래로 흘러내림
        Assert.True(img.ContentEquals(At(src, melt with { }, 3, 6).Image));
        Assert.False(img.ContentEquals(At(src, melt with { Seed = 4 }, 3, 6).Image));
        Assert.Equal(0, Opaque(At(src, melt, 6, 6).Image));
        Assert.True(Opaque(At(src, melt with { Fade = false }, 6, 6).Image) > 0);
    }

    // ───────────── 회전 ─────────────

    [Fact]
    public void Rotate_FullTurnEndsIdentical_QuarterTurnIsLossless()
    {
        var src = Sprite();
        var spin = new RotateEffect { Angle = 360 };
        Assert.True(Same(src, spin, 0, 4));
        Assert.True(Same(src, spin, 4, 4));
        // 90°(4프레임 중 1) = 손실 없는 자리 바꿈: 픽셀 수·색 그대로, 4×3 → 3×4
        var (quarter, _) = At(src, spin, 1, 4);
        Assert.Equal(Opaque(src), Opaque(quarter));
        Assert.Equal(2, quarter.Pixels.Count(p => p == Red));
        var b = quarter.GetOpaqueBounds();
        Assert.Equal((3, 4), (b.Width, b.Height));
    }

    [Fact]
    public void Rotate_OddAngleAddsNoNewColours()
    {
        var src = Sprite();
        var (img, _) = At(src, new RotateEffect { Angle = -30 }, 1, 1);
        Assert.All(img.Pixels.Where(p => p.A != 0), p => Assert.True(p == Red || p == Blue));
        Assert.InRange(Opaque(img), Opaque(src) - 4, Opaque(src) + 4);
    }

    [Fact]
    public void Rotate_SignedAngleUsesPivotByDefaultAndPadsBothDirections()
    {
        var clockwise = new RotateEffect { Angle = 90 };
        var counterclockwise = clockwise with { Angle = -90 };

        Assert.Equal(SquashAnchor.Pivot, clockwise.Anchor);
        Assert.Equal(45, clockwise.DegreesAt(0.5));
        Assert.Equal(-45, counterclockwise.DegreesAt(0.5));
        Assert.Equal(LayerEffects.Padding([clockwise], 10, 6), LayerEffects.Padding([counterclockwise], 10, 6));
        Assert.False(LayerEffects.Padding([counterclockwise], 10, 6).IsNone);
    }

    // ───────────── 잔상 ─────────────

    [Fact]
    public void Afterimage_DrawsPreviousFramesOfRangeBehind()
    {
        var p = new SpriteProject(8, 3);
        for (int i = 0; i < 3; i++) p.DuplicateFrame(0);
        for (int f = 0; f < 4; f++)
        {
            var cel = new PixelImage(8, 3);
            cel[f * 2, 1] = Red; // 프레임마다 2px씩 오른쪽으로
            p.SetCel(0, f, cel);
        }
        p.SetLayerEffects(0, [new AfterimageEffect { Count = 2, Interval = 1, Opacity = 100, Frames = new FrameRange(1, 3) }]);
        var f3 = p.Flatten(3);
        Assert.Equal(Red, f3[6, 1]);
        Assert.Equal(255, f3[4, 1].A); // 1번째 잔상(프레임 2) 100%
        Assert.InRange((int)f3[2, 1].A, 120, 135); // 2번째 잔상(프레임 1) 50%
        Assert.Equal(0, f3[0, 1].A); // 2개까지만
        var f1 = p.Flatten(1);
        Assert.Equal(0, f1[0, 1].A); // 범위 앞(프레임 0)은 쓰지 않음
        Assert.Equal(1, Opaque(f1));
        Assert.Equal(1, Opaque(p.Flatten(0))); // 범위 밖은 효과 없음
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Afterimage_FollowsHistoricalMotionRegardlessOfEffectOrder(bool afterimageFirst)
    {
        var p = new SpriteProject(5, 7);
        for (int i = 0; i < 4; i++) p.DuplicateFrame(0);
        for (int f = 0; f < 5; f++)
        {
            var cel = new PixelImage(5, 7);
            cel[2, 3] = Red;
            p.SetCel(0, f, cel);
        }
        var bob = new BobEffect { Height = 2 };
        var after = new AfterimageEffect { Count = 1, Opacity = 50 };
        p.SetLayerEffects(0, afterimageFirst ? [after, bob] : [bob, after]);

        var frame = p.Flatten(2);
        Assert.Equal(Red, frame[2, 3]);
        Assert.InRange((int)frame[2, 1].A, 120, 135);
        Assert.Equal(0, frame[2, 2].A);
        Assert.Equal(0, frame[2, 5].A);
    }

    [Fact]
    public void Afterimage_ClippedToOutlineUsesOnlyHistoricalOutlinePixels()
    {
        var p = new SpriteProject(8, 3);
        p.DuplicateFrame(0);
        for (int f = 0; f < 2; f++)
        {
            var cel = new PixelImage(8, 3);
            cel[1 + f * 3, 1] = Red;
            p.SetCel(0, f, cel);
        }
        p.SetLayerEffects(0,
        [
            new OutlineEffect { Color = Blue },
            new AfterimageEffect { ClipToPrevious = true, Count = 1, Opacity = 100 },
        ]);

        var frame = p.Flatten(1);
        Assert.Equal(Blue, frame[0, 1]);
        Assert.Equal(Color32.Transparent, frame[1, 1]);
        Assert.Equal(Blue, frame[2, 1]);
        Assert.Equal(Red, frame[4, 1]);
        Assert.Equal(Blue, frame[5, 1]);
    }

    [Fact]
    public void Afterimage_ClippedToInactiveHistoricalAnchorDoesNotAlterSource()
    {
        var p = new SpriteProject(5, 3);
        p.DuplicateFrame(0);
        var previous = new PixelImage(5, 3);
        previous[0, 1] = Red;
        p.SetCel(0, 0, previous);
        var current = new PixelImage(5, 3);
        current[4, 1] = Red;
        p.SetCel(0, 1, current);
        p.SetLayerEffects(0,
        [
            new ColorOverlayEffect { Color = Blue, Frames = new FrameRange(1, 1) },
            new AfterimageEffect { ClipToPrevious = true, Count = 1, Opacity = 100 },
        ]);

        Assert.Equal(Color32.Transparent, p.Flatten(1)[0, 1]);
        Assert.Equal(Red, previous[0, 1]);
        Assert.Equal(Red, p.Flatten(0)[0, 1]);
    }

    [Fact]
    public void Afterimage_BakeKeepsLook()
    {
        var p = new SpriteProject(8, 3);
        for (int i = 0; i < 2; i++) p.DuplicateFrame(0);
        for (int f = 0; f < 3; f++)
        {
            var cel = new PixelImage(8, 3);
            cel[f * 3, 1] = Red;
            p.SetCel(0, f, cel);
        }
        p.SetLayerEffects(0, [new AfterimageEffect { Count = 2, Opacity = 60, Tint = true, TintColor = Blue }]);
        var before = Enumerable.Range(0, 3).Select(f => p.Flatten(f)).ToList();
        p.BakeLayerEffects(0, [0]);
        Assert.Empty(p.Layers[0].Effects);
        Assert.All(Enumerable.Range(0, 3), f => Assert.True(before[f].ContentEquals(p.Flatten(f))));
    }

    [Fact]
    public void Afterimage_BakingWithLaterMotionKeepsHistoricalPositions()
    {
        var p = new SpriteProject(5, 7);
        for (int i = 0; i < 4; i++) p.DuplicateFrame(0);
        var cel = new PixelImage(5, 7);
        cel[2, 3] = Red;
        for (int f = 0; f < 5; f++) p.SetCel(0, f, cel);
        p.SetLayerEffects(0, [new AfterimageEffect { Count = 1, Opacity = 50 }, new BobEffect { Height = 2 }]);
        var before = Enumerable.Range(0, 5).Select(f => p.Flatten(f)).ToList();
        p.BakeLayerEffects(0, [0]);
        Assert.Empty(p.Layers[0].Effects);
        for (int f = 0; f < 5; f++) Assert.True(before[f].ContentEquals(p.Flatten(f)), $"frame {f}");
    }

    [Fact]
    public void Afterimage_RetainsPaddingForMotionThatEndedBeforeCurrentFrame()
    {
        var p = new SpriteProject(5, 7);
        for (int i = 0; i < 4; i++) p.DuplicateFrame(0);
        var cel = new PixelImage(5, 7);
        cel[2, 2] = Red;
        for (int f = 0; f < 5; f++) p.SetCel(0, f, cel);
        p.SetLayerEffects(0,
        [
            new BobEffect { Height = 2, Frames = new FrameRange(0, 3) },
            new AfterimageEffect { Count = 1, Interval = 3, Opacity = 50, Frames = new FrameRange(1, 4) },
        ]);
        var frame = p.Flatten(4);
        Assert.Equal(Red, frame[2, 2]);
        Assert.InRange((int)frame[2, 0].A, 120, 135);
    }

    // ───────────── 색 순환 ─────────────

    [Fact]
    public void ColorCycle_MapsListedColours_KeepsOthersAndAlpha()
    {
        Color32 a = new(10, 20, 30), b = new(40, 50, 60), c = new(70, 80, 90), other = new(1, 2, 3);
        var src = new PixelImage(4, 1);
        src[0, 0] = a;
        src[1, 0] = b.WithAlpha(128);
        src[2, 0] = c;
        src[3, 0] = other;
        var cycle = new ColorCycleEffect { Colors = [a, b, c], Steps = 3 };
        var one = Cropped(src, cycle, 1, 3);
        Assert.Equal([b, c.WithAlpha(128), a, other], one.Pixels);
        Assert.Equal([c, a.WithAlpha(128), b, other], Cropped(src, cycle with { Reverse = true }, 1, 3).Pixels);
        Assert.True(Same(src, cycle, 3, 3)); // 칸 수 = 색 개수 → 끝이 처음과 같음
    }

    // ───────────── 충격파 고리 ─────────────

    [Fact]
    public void Shockwave_RingRadiusGrows_MidpointCircle()
    {
        var src = new PixelImage(9, 9);
        src[4, 4] = Red;
        var wave = new ShockwaveEffect { Radius = 8, Fade = false, Easing = EffectEasing.Linear, Color = Blue };
        Assert.True(Same(src, wave, 0, 4));
        double prev = 0;
        for (int s = 1; s <= 4; s++)
        {
            var (img, rect) = At(src, wave, s, 4);
            int cx = rect.X + 4, cy = rect.Y + 4, r = 2 * s;
            var ring = Enumerable.Range(0, img.Height).SelectMany(y => Enumerable.Range(0, img.Width).Where(x => img[x, y] == Blue).Select(x => Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)))).ToList();
            Assert.All(ring, d => Assert.InRange(d, r - 0.75, r + 0.75));
            Assert.True(ring.Max() > prev);
            prev = ring.Max();
            Assert.Equal(Red, img[cx, cy]); // 그림은 그대로
        }
    }

    // ───────────── 여백 · 저장 ─────────────

    [Fact]
    public void Padding_OfMotionEffects()
    {
        Assert.Equal(new EffectPadding(3, 0, 3, 0), LayerEffects.Padding([new ShakeEffect { Amount = 3 }], 10, 10));
        Assert.Equal(new EffectPadding(2, 2, 2, 2), LayerEffects.Padding([new ShakeEffect { Amount = 2, Axis = EffectAxis.Both }], 10, 10));
        Assert.Equal(new EffectPadding(0, 4, 0, 4), LayerEffects.Padding([new BobEffect { Height = 4 }], 10, 10));
        Assert.Equal(new EffectPadding(0, 0, 0, 9), LayerEffects.Padding([new MeltEffect { Distance = 9 }], 10, 10));
        Assert.Equal(new EffectPadding(12, 12, 12, 12), LayerEffects.Padding([new ShockwaveEffect { Radius = 12 }], 10, 10));
        Assert.Equal(new EffectPadding(4, 6, 4, 6), LayerEffects.Padding([new RotateEffect { Anchor = SquashAnchor.Middle }], 10, 6));
        Assert.True(LayerEffects.Padding([new HitFlashEffect(), new BlinkEffect(), new AppearEffect(), new FadeEffect(), new AfterimageEffect(), new ColorCycleEffect()], 10, 10).IsNone);
        // 돌린 그림은 늘 여백 안
        var src = new PixelImage(10, 6);
        src.Clear(Blue);
        foreach (var anchor in new[] { SquashAnchor.Middle, SquashAnchor.BottomCenter, SquashAnchor.Pivot })
            for (int s = 1; s < 12; s++)
            {
                var (img, _) = At(src, new RotateEffect { Anchor = anchor }, s, 12);
                var b = img.GetOpaqueBounds();
                Assert.True(b.Left > 0 && b.Top > 0 && b.Right < img.Width && b.Bottom < img.Height, $"{anchor} {s}");
            }
    }

    [Fact]
    public void SaveLoad_RoundTripsAllMotionKinds()
    {
        var p = new SpriteProject(8, 8);
        for (int i = 0; i < 2; i++) p.DuplicateFrame(0); // 3프레임: 범위 0~1은 "모든 프레임"이 아님
        LayerEffect[] fx =
        [
            new HitFlashEffect { Color = new Color32(255, 40, 40), Strength = 70, Count = 3, Shape = HitFlashShape.Fade, Frames = new FrameRange(0, 1) },
            new BlinkEffect { Timing = BlinkTiming.Count, OnFrames = 3, OffFrames = 2, Count = 5, Hide = BlinkHide.Translucent, Opacity = 25, Enabled = false },
            new ShakeEffect { Axis = EffectAxis.Both, Amount = 5, Count = 7, Decay = false, Regular = true, Seed = 77, Easing = EffectEasing.EaseIn },
            new BobEffect { Height = 3, Cycles = 4, Axis = EffectAxis.Horizontal, Repeat = EffectRepeat.Loop, RepeatCount = 2 },
            new AppearEffect { Disappear = true, Style = AppearStyle.Wipe, From = WipeDirection.Right, Edge = 2, Seed = 12 },
            new FadeEffect { FadeOut = true, Easing = EffectEasing.EaseInOut, Frames = new FrameRange(0, 1) },
            new MeltEffect { Distance = 20, Irregularity = 30, Stretch = false, Fade = false, Seed = 99 },
            new RotateEffect { Angle = -720, Anchor = SquashAnchor.Pivot },
            new AfterimageEffect { Count = 5, Interval = 2, Opacity = 80, Tint = true, TintColor = new Color32(10, 20, 30, 128) },
            new ColorCycleEffect { Colors = [Red, Blue, new Color32(0, 255, 0), Color32.White], Steps = 8, Reverse = true },
            new ShockwaveEffect { Center = ExplodeCenter.Custom, CenterX = -3, CenterY = 5, Radius = 30, Thickness = 3, Color = Blue, Fade = false, Behind = true },
        ];
        p.SetLayerEffects(0, fx);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        // 열면 같은 설정의 공식 스크립트 효과
        var loaded = ProjectIO.Load(stream).Layers[0].Effects;
        Assert.All(loaded, e => Assert.IsType<ScriptEffect>(e));
        Assert.Equal(TestUtil.ConvertedJson(fx), TestUtil.EffectJson(loaded));
    }
}
