using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>캔버스 원근 변형(<see cref="SpriteProject.WarpCanvas"/>, <see cref="ImageTransform.WarpWithCanvas"/>): 꼭짓점을 자유롭게 옮긴 사각형으로 프로젝트 전체를 옮기기.</summary>
public class CanvasWarpTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    /// <summary>8×8 캔버스의 오른쪽 위를 위·오른쪽으로 끌고 왼쪽 위를 내린 볼록 사각형(새 캔버스 12×12).</summary>
    private static readonly (double X, double Y)[] Skew = [(0, 4), (11, 0), (8, 12), (0, 12)];

    private static void Paint(SpriteProject p, int layer, int frame, int x, int y, Color32 c)
    {
        p.BeginCelStroke(layer, frame).Set(x, y, c);
        p.CommitCelStroke("점");
    }

    /// <summary>캔버스 좌표 (x, y)의 셀 전체(캔버스 안 + 밖) 색.</summary>
    private static Color32 FullAt(SpriteProject p, int x, int y)
    {
        var (image, origin) = p.GetFullCel(0, 0);
        int ix = x - origin.X, iy = y - origin.Y;
        return ix < 0 || iy < 0 || ix >= image.Width || iy >= image.Height ? default : image[ix, iy];
    }

    [Theory]
    [InlineData(ResampleMode.Nearest)]
    [InlineData(ResampleMode.RotSprite)]
    public void WarpCanvas_RectangleCorners_KeepPixelsExactly(ResampleMode mode)
    {
        var p = new SpriteProject(8, 8);
        var original = TestUtil.RandomImage(8, 8, opaque: true);
        p.SetCel(0, 0, original);
        int steps = p.History.UndoCount;

        // 같은 크기·원래 꼭짓점이면 아무것도 하지 않는다.
        p.WarpCanvas(8, 8, [(0, 0), (8, 0), (8, 8), (0, 8)], mode);
        Assert.Equal(steps, p.History.UndoCount);
        Assert.Equal(original.Pixels, p.GetCel(0, 0)!.Pixels);

        // 꼭짓점이 축에 맞는 같은 크기 사각형이면 옮기기만(무손실), 남는 자리는 투명
        p.WarpCanvas(11, 10, [(2, 1), (10, 1), (10, 9), (2, 9)], mode);
        Assert.Equal(steps + 1, p.History.UndoCount);
        var cel = p.GetCel(0, 0)!;
        Assert.Equal(new Size(11, 10), cel.Size);
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 11; x++)
            {
                bool inside = x >= 2 && x < 10 && y >= 1 && y < 9;
                Assert.True((inside ? original[x - 2, y - 1] : default) == cel[x, y], $"({x}, {y})");
            }
        Assert.Null(p.GetCelOverflow(0, 0));
    }

    [Theory]
    [InlineData(ResampleMode.Nearest)]
    [InlineData(ResampleMode.RotSprite)]
    public void WarpCanvas_SkewedQuad_StretchesIntoCorner_LeavesMarginTransparent_AndUndoes(ResampleMode mode)
    {
        var p = new SpriteProject(8, 8);
        var red = new PixelImage(8, 8);
        red.Clear(Red);
        p.SetCel(0, 0, red);
        p.Edit("피벗", ProjectChange.Settings, () => p.Sprite.DefaultPivot = new Pivot(1, 1)); // 오른쪽 위 꼭짓점 (8,0)
        int steps = p.History.UndoCount;

        p.WarpCanvas(12, 12, Skew, mode);
        Assert.Equal((12, 12), (p.Width, p.Height));
        Assert.Equal(steps + 1, p.History.UndoCount);
        var cel = p.GetCel(0, 0)!;
        Assert.Equal(new Size(12, 12), cel.Size);
        Assert.Equal(Red, cel[9, 2]);          // 끌어 올린 오른쪽 위 자리(원래 캔버스 밖)
        Assert.Equal(Red, cel[4, 8]);
        Assert.Equal(Color32.Transparent, cel[1, 1]);    // 내린 왼쪽 위 위의 여백
        Assert.Equal(Color32.Transparent, cel[11, 11]);  // 오른쪽 아래 밖 여백
        Assert.Null(p.GetCelOverflow(0, 0));
        // 캔버스 피벗은 옮긴 꼭짓점 (11,0)으로
        var (px, py) = p.Sprite.DefaultPivot.ToLocalPixel(12, 12);
        Assert.Equal(11, px, 3);
        Assert.Equal(0, py, 3);

        p.Undo();
        Assert.Equal((8, 8), (p.Width, p.Height));
        Assert.Equal(red.Pixels, p.GetCel(0, 0)!.Pixels);
        Assert.Equal(new Pivot(1, 1), p.Sprite.DefaultPivot);
        p.Redo();
        Assert.Equal((12, 12), (p.Width, p.Height));
        Assert.Equal(Red, p.GetCel(0, 0)![9, 2]);
    }

    [Fact]
    public void WarpCanvas_PixelsOutsideNewCanvas_StayAsOverflow()
    {
        var p = new SpriteProject(4, 4);
        var original = TestUtil.RandomImage(4, 4, opaque: true);
        p.SetCel(0, 0, original);

        // 왼쪽 위를 캔버스 밖 (−3,−1)로: 왼쪽 그림이 캔버스 밖으로 나간다.
        p.WarpCanvas(4, 4, [(-3, -1), (4, 0), (4, 4), (0, 4)], ResampleMode.Nearest);
        Assert.NotNull(p.GetCelOverflow(0, 0));
        var outside = FullAt(p, -2, 0);
        Assert.Equal(255, outside.A);
        Assert.Contains(outside, original.Pixels);

        // 실행취소하면 캔버스 밖 그림도 없어지고 원래 셀로 돌아온다.
        p.Undo();
        Assert.Null(p.GetCelOverflow(0, 0));
        Assert.Equal(original.Pixels, p.GetCel(0, 0)!.Pixels);
    }

    [Fact]
    public void WarpCanvas_Part_EnclosesMappedRect_KeepsPivotPoint_AllSkins_AndShifts()
    {
        var p = new SpriteProject(8, 8);
        var g = p.AddGroup("몸");
        var part = p.AddPart(g, "팔", 2, 3, new Point(1, 2));
        int pi = p.IndexOf(part);
        p.AddFrame();
        var red = new PixelImage(2, 3);
        red.Clear(Red);
        p.SetCel(pi, 0, red);
        var baseSkin = g.BaseSkin!;
        var knight = p.AddSkin(g, "knight");
        p.SetActiveSkin(g, knight);
        Paint(p, pi, 0, 0, 2, Blue);
        p.SetActiveSkin(g, baseSkin);
        p.SetPartBorder(pi, new SpriteBorder(1, 1, 0, 1));
        p.SetLayerPivot(pi, new Pivot(0.25f, 0f)); // 캔버스 (1.5, 5)
        p.MovePartFrom(pi, 1, 2, 1);
        var (box, h) = ImageTransform.CanvasWarp(new Size(8, 8), Skew)!.Value;

        p.WarpCanvas(12, 12, Skew, ResampleMode.Nearest);

        // 새 영역 = 옮긴 네 꼭짓점을 감싸는 정수 사각형
        var mapped = new[] { (1, 2), (3, 2), (3, 5), (1, 5) }.Select(c => box.Map(h, c.Item1, c.Item2)).ToArray();
        var expected = Rectangle.FromLTRB(
            (int)Math.Floor(mapped.Min(m => m.X)), (int)Math.Floor(mapped.Min(m => m.Y)),
            (int)Math.Ceiling(mapped.Max(m => m.X)), (int)Math.Ceiling(mapped.Max(m => m.Y)));
        Assert.Equal(expected, part.PartRect);
        Assert.All(mapped, m => Assert.True(part.PartRect.Left <= m.X && m.X <= part.PartRect.Right && part.PartRect.Top <= m.Y && m.Y <= part.PartRect.Bottom));
        Assert.Equal(SpriteBorder.None, part.PartBorder);

        // 피벗은 같은 그림 지점(옮긴 (1.5, 5))을 가리킨다
        var target = box.Map(h, 1.5, 5);
        var (lx, ly) = part.Pivot.ToLocalPixel(part.PartSize.Width, part.PartSize.Height);
        Assert.InRange(part.PartOffset.X + lx - target.X, -1, 1);
        Assert.InRange(part.PartOffset.Y + ly - target.Y, -1, 1);

        // 모든 스킨의 셀이 새 부위 크기로 다시 그려진다
        var baseCel = p.GetOwnCel(part, p.Frames[0], baseSkin)!;
        var knightCel = p.GetOwnCel(part, p.Frames[0], knight)!;
        Assert.Equal(part.PartSize, baseCel.Size);
        Assert.Equal(part.PartSize, knightCel.Size);
        Assert.Contains(Red, baseCel.Pixels);
        Assert.Contains(Blue, knightCel.Pixels);
        // 합성에서도 옮긴 그림 지점에 있다(부위 가운데 (2, 3.5)를 옮긴 곳)
        var center = box.Map(h, 2, 3.5);
        Assert.Equal(Red, p.Flatten(0).Get((int)center.X, (int)center.Y));

        // 프레임 1에서 옮긴 거리: 옮긴 왼쪽 위끼리의 차이
        var baseAt = box.Map(h, 1, 2);
        var shifted = box.Map(h, 3, 3);
        var shift = new Point(
            (int)Math.Floor(shifted.X + 0.5) - (int)Math.Floor(baseAt.X + 0.5),
            (int)Math.Floor(shifted.Y + 0.5) - (int)Math.Floor(baseAt.Y + 0.5));
        Assert.Equal(new Point(part.PartOffset.X + shift.X, part.PartOffset.Y + shift.Y), p.GetPartOffset(part, 1));

        p.Undo();
        Assert.Equal(new Rectangle(1, 2, 2, 3), part.PartRect);
        Assert.Equal(new SpriteBorder(1, 1, 0, 1), part.PartBorder);
        Assert.Equal(new Point(3, 3), p.GetPartOffset(part, 1));
        Assert.Equal(red.Pixels, p.GetOwnCel(part, p.Frames[0], baseSkin)!.Pixels);
    }

    [Theory]
    [InlineData(ResampleMode.Nearest)]
    [InlineData(ResampleMode.RotSprite)]
    public void WarpWithCanvas_MatchesWarpCanvasInsideCanvas(ResampleMode mode)
    {
        var original = TestUtil.RandomImage(8, 8, seed: 3, opaque: true);
        var p = new SpriteProject(8, 8);
        p.SetCel(0, 0, original);
        p.WarpCanvas(12, 12, Skew, mode);

        var moved = ImageTransform.WarpWithCanvas(original, Point.Empty, new Size(8, 8), Skew, new Rectangle(0, 0, 12, 12), mode);
        Assert.NotNull(moved);
        var expected = new PixelImage(12, 12);
        expected.Blit(moved.Value.Image, moved.Value.Origin.X, moved.Value.Origin.Y);
        Assert.Equal(expected.Pixels, p.GetCel(0, 0)!.Pixels);
    }

    [Fact]
    public void WarpWithCanvas_FarSideOfHorizon_KeepsNearPixels()
    {
        // 위 변을 반으로 줄인 사다리꼴: 원근의 지평선이 원래 캔버스 아래 32px(y = 64)에 있다.
        // 그 너머까지 든 그림도 지평선 이쪽 칸은 같은 대응으로 옮겨지고 캔버스 안 그림은 그대로 남는다.
        var full = new PixelImage(32, 80);
        full.Clear(Red);
        (double X, double Y)[] corners = [(8, 0), (24, 0), (32, 32), (0, 32)];
        var moved = ImageTransform.WarpWithCanvas(full, Point.Empty, new Size(32, 32), corners, new Rectangle(-64, -64, 160, 160), ResampleMode.Nearest);
        Assert.NotNull(moved);
        var (image, origin) = moved.Value;
        Color32 At(int x, int y) => new Rectangle(origin, image.Size).Contains(x, y) ? image[x - origin.X, y - origin.Y] : default;
        Assert.Equal(Red, At(16, 1));
        Assert.Equal(Red, At(16, 31));
        Assert.Equal(Red, At(16, 60));   // 원래 캔버스 아래 그림이 늘어나 이어진다
        Assert.Equal(Color32.Transparent, At(2, 1));  // 줄인 위 변 바깥은 비어 있다

        // 넓은 clip은 띠로 나눠 계산해도 같은 그림(지평선 가까이 끝없이 늘어난 그림도 clip 안은 남음)
        var wide = ImageTransform.WarpWithCanvas(full, Point.Empty, new Size(32, 32), corners, new Rectangle(-2048, -2048, 4096, 4096), ResampleMode.Nearest);
        Assert.NotNull(wide);
        var (wideImage, wideOrigin) = wide.Value;
        Assert.True(wideImage.Height > 160);
        for (int y = -64; y < 96; y++)
            for (int x = -64; x < 96; x++)
            {
                bool inWide = new Rectangle(wideOrigin, wideImage.Size).Contains(x, y);
                Assert.True(At(x, y) == (inWide ? wideImage[x - wideOrigin.X, y - wideOrigin.Y] : default), $"({x}, {y})");
            }
    }

    [Fact]
    public void WarpCanvas_NonConvexCorners_Throws()
    {
        var p = new SpriteProject(4, 4);
        Assert.Throws<ArgumentException>(() => p.WarpCanvas(4, 4, [(0, 0), (4, 4), (4, 0), (0, 4)], ResampleMode.Nearest));
        Assert.Throws<ArgumentException>(() => p.WarpCanvas(4, 4, [(0, 0), (4, 0), (4, 4)], ResampleMode.Nearest));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.WarpCanvas(0, 4, [(0, 0), (4, 0), (4, 4), (0, 4)], ResampleMode.Nearest));
        Assert.Null(ImageTransform.WarpWithCanvas(new PixelImage(4, 4), Point.Empty, new Size(4, 4), [(0, 0), (4, 0), (2, 2), (0, 4)], new Rectangle(0, 0, 4, 4), ResampleMode.Nearest));
    }
}
