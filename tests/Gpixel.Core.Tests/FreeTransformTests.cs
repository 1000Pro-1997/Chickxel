using System.Drawing;
using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

/// <summary>
/// 자유 변형(회전·크기·옮기기)과 RotSprite.
/// <para>
/// 중심 규칙: 원본 사각형 (X, Y, W, H)의 중심은 연속 좌표 (X + W/2, Y + H/2)이고 변형은 그 중심을 기준으로 한다.
/// 90° 회전한 사각형의 왼쪽 위는 X + W/2 − H/2라서 W·H가 둘 다 짝수이거나 둘 다 홀수일 때만 픽셀 격자에 맞고,
/// 그때 <see cref="Selection.Rotate90"/>(정수 중심 X + ⌊W/2⌋ − ⌊H/2⌋)과 위치까지 같다. 그래서 90° 비교는 그런 크기로만 한다.
/// </para>
/// </summary>
public class FreeTransformTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 200, 0);
    private static readonly Color32 Yellow = new(255, 230, 0);

    private static readonly ResampleMode[] Modes = [ResampleMode.RotSprite, ResampleMode.Nearest];

    private static FreeTransform RectSource(PixelImage image, int x, int y) => new(image, new Rectangle(x, y, image.Width, image.Height));

    [Fact]
    public void Identity_IsExact()
    {
        var img = TestUtil.RandomImage(5, 4, seed: 3);
        var t = RectSource(img, 3, 2);
        foreach (var mode in Modes)
        {
            var r = t.Render(t.Identity, mode);
            Assert.Equal(new Rectangle(3, 2, 5, 4), r.Region.Bounds);
            Assert.True(r.Region.IsRectangle);
            Assert.True(r.Image.ContentEquals(img));
        }
    }

    [Fact]
    public void Identity_KeepsShapeExactly()
    {
        var region = PixelMask.Ellipse(new Rectangle(2, 1, 7, 6));
        var img = TestUtil.RandomImage(region.Bounds.Width, region.Bounds.Height, seed: 5, opaque: true);
        var t = new FreeTransform(img, region);
        foreach (var mode in Modes)
        {
            var r = t.Render(t.Identity, mode);
            Assert.Equal(region.Bounds, r.Region.Bounds);
            for (int y = region.Bounds.Top; y < region.Bounds.Bottom; y++)
                for (int x = region.Bounds.Left; x < region.Bounds.Right; x++)
                {
                    Assert.Equal(region.Contains(x, y), r.Region.Contains(x, y));
                    var got = r.Image.Get(x - region.Bounds.X, y - region.Bounds.Y);
                    Assert.Equal(region.Contains(x, y) ? img.Get(x - region.Bounds.X, y - region.Bounds.Y) : Color32.Transparent, got);
                }
        }
    }

    [Fact]
    public void IntegerMove_IsExact()
    {
        var img = TestUtil.RandomImage(6, 3, seed: 7);
        var t = RectSource(img, 4, 4);
        foreach (var mode in Modes)
        {
            var p = t.Identity with { CenterX = t.Identity.CenterX + 7, CenterY = t.Identity.CenterY - 3 };
            var r = t.Render(p, mode);
            Assert.Equal(new Rectangle(11, 1, 6, 3), r.Region.Bounds);
            Assert.True(r.Image.ContentEquals(img));
        }
    }

    [Theory]
    [InlineData(6, 4, true)]
    [InlineData(6, 4, false)]
    [InlineData(5, 3, true)]
    [InlineData(5, 3, false)]
    [InlineData(3, 7, true)]
    public void Rotate90_MatchesSelectionRotate90(int w, int h, bool clockwise)
    {
        var canvas = TestUtil.RandomImage(24, 24, seed: w * 31 + h, opaque: true);
        var rect = new Rectangle(8, 9, w, h);

        // 기존 90° 회전(선택)
        var sel = new Selection();
        var work = canvas.Clone();
        sel.Select(work, rect);
        sel.Rotate90(work, clockwise);
        var expectedImage = sel.Floating!;
        var expectedBounds = sel.Bounds;
        Assert.True(expectedImage.ContentEquals(ImageTransform.Rotate90(canvas.Crop(rect), clockwise)));

        var t = RectSource(canvas.Crop(rect), rect.X, rect.Y);
        foreach (var mode in Modes)
        {
            var r = t.Render(t.Identity with { Angle = clockwise ? 90 : -90 }, mode);
            Assert.Equal(expectedBounds, r.Region.Bounds);
            Assert.True(r.Region.IsRectangle);
            Assert.True(r.Image.ContentEquals(expectedImage), $"{mode} {w}×{h} {(clockwise ? "cw" : "ccw")}");
        }
    }

    [Fact]
    public void Rotate180_MatchesImageTransform()
    {
        var img = TestUtil.RandomImage(5, 4, seed: 11);
        var t = RectSource(img, 0, 0);
        foreach (var mode in Modes)
        {
            var r = t.Render(t.Identity with { Angle = 180 }, mode);
            Assert.Equal(new Rectangle(0, 0, 5, 4), r.Region.Bounds);
            Assert.True(r.Image.ContentEquals(ImageTransform.Rotate180(img)));
        }
    }

    [Fact]
    public void Scale2_Nearest_DuplicatesPixels()
    {
        // 짝수 크기라 2배 사각형도 격자에 맞는다: 왼쪽 위 = 중심 − W.
        var img = TestUtil.RandomImage(4, 2, seed: 13);
        var t = RectSource(img, 10, 6);
        var r = t.Render(t.Identity with { ScaleX = 2, ScaleY = 2 }, ResampleMode.Nearest);
        Assert.Equal(new Rectangle(8, 5, 8, 4), r.Region.Bounds);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(img.Get(x / 2, y / 2), r.Image.Get(x, y));
        // 축에 맞는 확대는 RotSprite도 최근접과 같다(번지지 않음).
        Assert.True(t.Render(t.Identity with { ScaleX = 2, ScaleY = 2 }, ResampleMode.RotSprite).Image.ContentEquals(r.Image));
    }

    [Fact]
    public void NegativeScale_Flips()
    {
        var img = TestUtil.RandomImage(5, 3, seed: 17);
        var t = RectSource(img, 2, 2);
        foreach (var mode in Modes)
        {
            var h = t.Render(t.Identity with { ScaleX = -1 }, mode);
            Assert.Equal(new Rectangle(2, 2, 5, 3), h.Region.Bounds);
            Assert.True(h.Image.ContentEquals(ImageTransform.FlipHorizontal(img)));
            var v = t.Render(t.Identity with { ScaleY = -1 }, mode);
            Assert.Equal(new Rectangle(2, 2, 5, 3), v.Region.Bounds);
            Assert.True(v.Image.ContentEquals(ImageTransform.FlipVertical(img)));
        }
    }

    [Theory]
    [InlineData(ResampleMode.RotSprite)]
    [InlineData(ResampleMode.Nearest)]
    public void OutsideMask_StaysTransparent(ResampleMode mode)
    {
        // 모양(원) 밖 칸은 빨강으로 칠해 두어도 결과에 나오지 않고, 결과 영역 밖은 모두 투명.
        var region = PixelMask.Ellipse(new Rectangle(0, 0, 9, 9));
        var img = new PixelImage(9, 9);
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 9; x++)
                img.Set(x, y, region.Contains(x, y) ? ((x + y) % 2 == 0 ? Blue : Green) : Red);
        var t = new FreeTransform(img, region);
        var r = t.Render(t.Identity with { Angle = 33, ScaleX = 1.4, ScaleY = 0.8 }, mode);
        var b = r.Region.Bounds;
        Assert.False(r.Region.IsRectangle);
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
            {
                var c = r.Image.Get(x, y);
                Assert.NotEqual(Red, c);
                if (!r.Region.Contains(b.X + x, b.Y + y)) Assert.Equal(Color32.Transparent, c);
            }
    }

    [Fact]
    public void RotSprite_IntroducesNoNewColors()
    {
        Color32[] colors = [Red, Blue, Green, Yellow, Color32.Transparent];
        var rng = new Random(23);
        var img = new PixelImage(12, 9);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = colors[rng.Next(colors.Length)];
        var t = RectSource(img, 0, 0);
        foreach (var angle in new[] { 17.0, 45, -63.5, 120 })
        {
            var r = t.Render(t.Identity with { Angle = angle, ScaleX = 1.3, ScaleY = -0.9 }, ResampleMode.RotSprite);
            Assert.All(r.Image.Pixels, c => Assert.Contains(c, colors));
        }
        Assert.All(FreeTransform.Scale2x(img).Pixels, c => Assert.Contains(c, colors));
    }

    [Fact]
    public void Scale2x_RoundsCorners()
    {
        // 빨강 한 칸 + 투명 셋: 빨강의 오른쪽 아래 칸만 투명 이웃 쪽으로 깎인다.
        var img = new PixelImage(2, 2);
        img.Set(0, 0, Red);
        var up = FreeTransform.Scale2x(img);
        Assert.Equal(4, up.Width);
        Assert.Equal(Red, up.Get(0, 0));
        Assert.Equal(Red, up.Get(1, 0));
        Assert.Equal(Red, up.Get(0, 1));
        Assert.Equal(Color32.Transparent, up.Get(1, 1));
        Assert.Equal(3, up.Pixels.Count(c => c == Red));
    }

    [Fact]
    public void MapAndUnmap_AreInverse()
    {
        var t = RectSource(new PixelImage(7, 5), 3, 4);
        var p = t.Identity with { CenterX = 20.25, CenterY = -3.5, Angle = 37, ScaleX = -1.7, ScaleY = 0.6 };
        var (x, y) = t.Map(p, 4.5, 8.25);
        var (u, v) = t.Unmap(p, x, y);
        Assert.Equal(4.5, u, 9);
        Assert.Equal(8.25, v, 9);
        // 꼭짓점: 원본 사각형 네 모서리를 옮긴 것
        var corners = t.Corners(t.Identity);
        Assert.Equal((3.0, 4.0), corners[0]);
        Assert.Equal((10.0, 9.0), corners[2]);
    }

    [Fact]
    public void NormalizeAngle_WrapsIntoHalfOpenRange()
    {
        Assert.Equal(180, TransformParams.NormalizeAngle(-180));
        Assert.Equal(-90, TransformParams.NormalizeAngle(270));
        Assert.Equal(10, TransformParams.NormalizeAngle(730));
    }
}
