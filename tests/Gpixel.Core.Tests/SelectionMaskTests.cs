using System.Drawing;
using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

/// <summary>원·자유·스마트 선택(픽셀 모양 선택)과 영역 제한.</summary>
public class SelectionMaskTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    [Fact]
    public void Polygon_FillsInsideAndEdges()
    {
        // 직각삼각형: 빗변 위 칸은 선으로, 안쪽은 짝홀 규칙으로 들어간다.
        var m = PixelMask.Polygon([new Point(0, 0), new Point(6, 0), new Point(0, 6)]);
        Assert.Equal(new Rectangle(0, 0, 7, 7), m.Bounds);
        Assert.True(m.Contains(0, 0));
        Assert.True(m.Contains(1, 1));
        Assert.True(m.Contains(3, 3));   // 빗변 위
        Assert.True(m.Contains(0, 6));
        Assert.False(m.Contains(4, 4));
        Assert.False(m.Contains(6, 6));
        Assert.Equal(28, m.Count);       // 1+2+…+7

        // 정사각형 꼭짓점이면 꽉 찬 사각형이 되고, 다듬으면 비트 없는 사각형
        var square = PixelMask.Polygon([new Point(2, 2), new Point(5, 2), new Point(5, 5), new Point(2, 5)]);
        Assert.Equal(16, square.Count);
        Assert.True(square.Trim(new Rectangle(0, 0, 10, 10)).IsRectangle);

        // 가는 올가미(한 줄로 왔다 갔다)도 지나간 칸은 들어간다.
        var thin = PixelMask.Polygon([new Point(1, 1), new Point(4, 1)]);
        Assert.Equal(4, thin.Count);
    }

    [Fact]
    public void Ellipse_MatchesEllipseFill()
    {
        var r = new Rectangle(0, 0, 7, 5);
        var m = PixelMask.Ellipse(r);
        var fill = Raster.EllipseFill(r);
        Assert.Equal(fill.Count, m.Count);
        Assert.All(fill, p => Assert.True(m.Contains(p)));
        Assert.False(m.Contains(0, 0));
        Assert.True(m.Contains(3, 2));
    }

    [Fact]
    public void Trim_ClipsToImageAndShrinks()
    {
        var m = PixelMask.FromPoints([new Point(-2, 1), new Point(3, 3), new Point(9, 9)]);
        var t = m.Trim(new Rectangle(0, 0, 8, 8));
        Assert.Equal(new Rectangle(3, 3, 1, 1), t.Bounds);
        Assert.True(t.IsRectangle);
        Assert.True(PixelMask.FromPoints([new Point(-1, -1)]).Trim(new Rectangle(0, 0, 4, 4)).IsEmpty);
        Assert.True(default(PixelMask).IsEmpty);
    }

    [Fact]
    public void FloodRegion_IsMagicWandRegion()
    {
        // 왼쪽 3열 빨강(가운데 줄은 파랑으로 끊김), 오른쪽 투명
        var img = new PixelImage(5, 3);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++) img.Set(x, y, Red);
        img.Set(1, 1, Blue);
        var region = Raster.FloodRegion(img, 0, 0);
        Assert.Equal(8, region.Count);
        Assert.False(region.Contains(1, 1));
        Assert.False(region.Contains(3, 0));

        // 맞는 색 전체(이어지지 않아도)
        img.Set(4, 2, Red);
        Assert.Equal(9, Raster.FloodRegion(img, 0, 0, contiguous: false).Count);
        Assert.Equal(8, Raster.FloodRegion(img, 0, 0).Count);

        // 허용 오차
        img.Set(2, 2, new Color32(250, 0, 0));
        Assert.Equal(7, Raster.FloodRegion(img, 0, 0).Count);
        Assert.Equal(8, Raster.FloodRegion(img, 0, 0, tolerance: 5).Count);

        // 모양 제한(위 줄만) 밖은 들어가지 않고, 씨앗이 밖이면 빈 영역
        var topRow = PixelMask.FromPoints([new Point(0, 0), new Point(1, 0), new Point(2, 0), new Point(0, 2)]);
        Assert.Equal(3, Raster.FloodRegion(img, 0, 0, clip: topRow).Count);
        Assert.True(Raster.FloodRegion(img, 1, 2, clip: topRow).IsEmpty);
        Assert.True(Raster.FloodRegion(img, 9, 9).IsEmpty);
    }

    [Fact]
    public void MaskSelection_CopyDeleteHonorMask()
    {
        var img = new PixelImage(4, 4);
        img.FillRect(img.Bounds, Red);
        var sel = new Selection();
        // 대각선 두 칸만
        sel.Select(img, PixelMask.FromPoints([new Point(1, 1), new Point(2, 2)]));
        Assert.Equal(new Rectangle(1, 1, 2, 2), sel.Bounds);
        Assert.False(sel.IsRectangle);
        Assert.True(sel.Contains(1, 1));
        Assert.False(sel.Contains(2, 1));
        Assert.False(sel.IsInside(new Point(2, 1)));
        Assert.False(sel.Contains(0, 0));

        var copy = sel.Copy(img)!;
        Assert.Equal(Red, copy.Get(0, 0));
        Assert.Equal(Color32.Transparent, copy.Get(1, 0));
        Assert.Equal(Red, copy.Get(1, 1));

        sel.Delete(img);
        Assert.Equal(Color32.Transparent, img.Get(1, 1));
        Assert.Equal(Color32.Transparent, img.Get(2, 2));
        Assert.Equal(Red, img.Get(2, 1));
        Assert.Equal(Red, img.Get(1, 2));

        // 그리기 제한도 모양을 따른다.
        Assert.Equal(1, Raster.Plot(img, [new Point(1, 1), new Point(2, 1)], Blue, sel.Clip));
        Assert.Equal(Blue, img.Get(1, 1));
        Assert.Equal(Red, img.Get(2, 1));
    }

    [Fact]
    public void MaskSelection_MoveKeepsUnselectedPixels()
    {
        var img = new PixelImage(6, 3);
        img.FillRect(new Rectangle(0, 0, 2, 2), Red);
        var sel = new Selection();
        sel.Select(img, PixelMask.FromPoints([new Point(0, 0), new Point(1, 1)]));
        sel.MoveBy(img, 3, 0);
        Assert.True(sel.IsFloating);
        // 모양 밖 칸은 그대로 남는다.
        Assert.Equal(Red, img.Get(1, 0));
        Assert.Equal(Red, img.Get(0, 1));
        Assert.Equal(Color32.Transparent, img.Get(0, 0));
        sel.Anchor(img);
        Assert.Equal(Red, img.Get(3, 0));
        Assert.Equal(Red, img.Get(4, 1));
        Assert.Equal(Color32.Transparent, img.Get(4, 0));
        Assert.True(sel.Contains(3, 0));
        Assert.False(sel.Contains(4, 0));
    }

    [Fact]
    public void MaskSelection_FlipTransformsShape()
    {
        var img = new PixelImage(5, 5);
        // L자: (1,1) (1,2) (2,2)
        foreach (var p in new[] { new Point(1, 1), new Point(1, 2), new Point(2, 2) }) img.Set(p.X, p.Y, Red);
        var sel = new Selection();
        sel.Select(img, PixelMask.FromPoints([new Point(1, 1), new Point(1, 2), new Point(2, 2)]));
        sel.FlipHorizontal(img);
        sel.Anchor(img);
        // 좌우 뒤집으면 (2,1) (2,2) (1,2)
        Assert.True(sel.Contains(2, 1));
        Assert.False(sel.Contains(1, 1));
        Assert.Equal(Red, img.Get(2, 1));
        Assert.Equal(Color32.Transparent, img.Get(1, 1));
        Assert.Equal(3, sel.Region.Count);
    }

    [Fact]
    public void ReplaceColor_HonorsMask()
    {
        var img = new PixelImage(3, 1);
        img.FillRect(img.Bounds, Red);
        var clip = PixelMask.FromPoints([new Point(0, 0), new Point(2, 0)]);
        Assert.Equal(2, Raster.ReplaceColor(img, Red, Blue, clip));
        Assert.Equal(Red, img.Get(1, 0));
    }
}
