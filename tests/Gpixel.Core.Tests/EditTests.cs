using System.Drawing;
using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class EditTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    [Fact]
    public void Line_HitsBothEnds_AndIsContinuous()
    {
        var pts = Raster.Line(new Point(0, 0), new Point(7, 3));
        Assert.Equal(new Point(0, 0), pts[0]);
        Assert.Equal(new Point(7, 3), pts[^1]);
        Assert.Equal(8, pts.Count); // x가 긴 축 → x마다 한 점
        for (int i = 1; i < pts.Count; i++)
        {
            Assert.True(Math.Abs(pts[i].X - pts[i - 1].X) <= 1);
            Assert.True(Math.Abs(pts[i].Y - pts[i - 1].Y) <= 1);
        }
        Assert.Equal([new Point(2, 2)], Raster.Line(new Point(2, 2), new Point(2, 2)));
        Assert.Equal(6, Raster.Line(new Point(0, 0), new Point(3, -5)).Count);
    }

    [Fact]
    public void Rectangle_OutlineAndFill()
    {
        var r = Raster.RectFromPoints(new Point(5, 4), new Point(2, 1));
        Assert.Equal(new Rectangle(2, 1, 4, 4), r);
        Assert.Equal(12, Raster.RectOutline(r).Count);
        Assert.Equal(16, Raster.RectFill(r).Count);
        Assert.Equal(new Rectangle(0, 0, 4, 4), Raster.RectFromPoints(new Point(0, 0), new Point(3, 1), square: true));
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(11, 7)]
    [InlineData(16, 5)]
    [InlineData(3, 9)]
    [InlineData(32, 31)]
    public void Ellipse_IsSymmetric_AndInsideBounds(int w, int h)
    {
        var r = new Rectangle(3, 2, w, h);
        foreach (var pts in new[] { Raster.EllipseOutline(r), Raster.EllipseFill(r) })
        {
            var set = pts.ToHashSet();
            Assert.All(pts, p => Assert.True(r.Contains(p)));
            foreach (var p in pts)
            {
                Assert.Contains(new Point(r.Left + r.Right - 1 - p.X, p.Y), set);
                Assert.Contains(new Point(p.X, r.Top + r.Bottom - 1 - p.Y), set);
            }
            // 사각형의 네 변에 닿는다
            Assert.Contains(pts, p => p.X == r.Left);
            Assert.Contains(pts, p => p.X == r.Right - 1);
            Assert.Contains(pts, p => p.Y == r.Top);
            Assert.Contains(pts, p => p.Y == r.Bottom - 1);
        }
        // 테두리는 8방향으로 한 덩어리로 이어져 있다
        var outline = Raster.EllipseOutline(r).ToHashSet();
        var seen = new HashSet<Point> { outline.First() };
        var queue = new Queue<Point>(seen);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var q = new Point(p.X + dx, p.Y + dy);
                    if (outline.Contains(q) && seen.Add(q)) queue.Enqueue(q);
                }
        }
        Assert.Equal(outline.Count, seen.Count);
    }

    [Fact]
    public void FloodFill_Contiguous_StopsAtWalls_AndGlobalReplacesAll()
    {
        var img = new PixelImage(7, 5);
        // 세로 벽으로 왼쪽/오른쪽 분리
        for (int y = 0; y < 5; y++) img.Set(3, y, Blue);
        int changed = Raster.FloodFill(img, 0, 0, Red);
        Assert.Equal(15, changed);
        Assert.Equal(Red, img.Get(2, 4));
        Assert.Equal(Color32.Transparent, img.Get(4, 0));
        Assert.Equal(Blue, img.Get(3, 2));

        // 같은 색 전체
        var img2 = new PixelImage(7, 5);
        for (int y = 0; y < 5; y++) img2.Set(3, y, Blue);
        Assert.Equal(30, Raster.FloodFill(img2, 0, 0, Red, contiguous: false));
        Assert.Equal(Blue, img2.Get(3, 0));

        // 대각선으로는 새지 않는다(4방향)
        var img3 = new PixelImage(3, 3);
        img3.Set(1, 0, Blue);
        img3.Set(0, 1, Blue);
        Assert.Equal(1, Raster.FloodFill(img3, 0, 0, Red));

        // 선택 영역으로 제한
        var img4 = new PixelImage(6, 6);
        Assert.Equal(4, Raster.FloodFill(img4, 1, 1, Red, clip: new Rectangle(0, 0, 2, 2)));
        Assert.Equal(0, Raster.FloodFill(img4, 0, 0, Red, clip: new Rectangle(0, 0, 2, 2)));
    }

    [Fact]
    public void FloodFill_TreatsAllTransparentAsSame()
    {
        var img = new PixelImage(3, 1);
        img.Set(1, 0, new Color32(9, 9, 9, 0));
        Assert.Equal(3, Raster.FloodFill(img, 0, 0, Red));
    }

    [Fact]
    public void Brush_AndMirror()
    {
        Assert.Single(Raster.Brush(5, 5, 1));
        Assert.Equal(9, Raster.Brush(5, 5, 3).Count);
        Assert.Contains(new Point(4, 4), Raster.Brush(5, 5, 3));
        Assert.Equal(4, Raster.Brush(5, 5, 2).Count);
        var round = Raster.Brush(0, 0, 5, round: true);
        Assert.True(round.Count < 25);
        Assert.DoesNotContain(new Point(-2, -2), round);

        var m = Raster.Mirror(new Point(1, 2), 10, 8, mirrorX: true, mirrorY: true).ToList();
        Assert.Equal([new Point(1, 2), new Point(8, 2), new Point(1, 5), new Point(8, 5)], m);
        Assert.Single(Raster.Mirror(new Point(2, 2), 5, 5, true, true)); // 정중앙
    }

    [Fact]
    public void PixelPerfect_RemovesLCorners()
    {
        // 계단: (0,0)→(1,0)→(1,1)→(2,1)→(2,2)
        var path = new[] { new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(2, 1), new Point(2, 2) };
        var filtered = PixelPerfect.Filter(path);
        Assert.Equal([new Point(0, 0), new Point(1, 1), new Point(2, 2)], filtered);

        var stroke = new PixelPerfectStroke();
        Assert.Equal((new Point(0, 0), (Point?)null), stroke.Add(new Point(0, 0)));
        stroke.Add(new Point(1, 0));
        var step = stroke.Add(new Point(1, 1));
        Assert.Equal(new Point(1, 0), step.Restore);
        Assert.Equal((null, null), stroke.Add(new Point(1, 1)));

        // 직선은 그대로
        Assert.Equal(4, PixelPerfect.Filter(Raster.Line(new Point(0, 0), new Point(3, 0))).Count);
    }

    [Fact]
    public void Selection_MoveLiftsAndAnchors()
    {
        var img = new PixelImage(8, 8);
        img.FillRect(new Rectangle(1, 1, 2, 2), Red);
        img.Set(6, 1, Blue);
        var sel = new Selection();
        sel.Select(img, new Rectangle(1, 1, 2, 2));
        sel.MoveBy(img, 4, 0);
        Assert.True(sel.IsFloating);
        Assert.Equal(Color32.Transparent, img.Get(1, 1)); // 원래 자리는 비었다
        sel.MoveBy(img, 0, 1);
        Assert.Equal(new Rectangle(5, 2, 2, 2), sel.Bounds);
        sel.Anchor(img);
        Assert.False(sel.IsFloating);
        Assert.Equal(Red, img.Get(5, 2));
        Assert.Equal(Red, img.Get(6, 3));
        Assert.Equal(Blue, img.Get(6, 1)); // 다른 픽셀은 그대로
        Assert.Equal(Color32.Transparent, img.Get(2, 2));
    }

    [Fact]
    public void Selection_CopyPasteFlipRotateDelete()
    {
        var img = new PixelImage(8, 8);
        img.Set(0, 0, Red);
        img.Set(1, 0, Blue);
        var sel = new Selection();
        sel.Select(img, new Rectangle(0, 0, 2, 1));
        var clip = sel.Copy(img)!;
        Assert.Equal(new Size(2, 1), clip.Size);

        sel.Paste(img, clip, new Point(4, 4));
        sel.FlipHorizontal(img);
        sel.Anchor(img);
        Assert.Equal(Blue, img.Get(4, 4));
        Assert.Equal(Red, img.Get(5, 4));

        sel.Select(img, new Rectangle(4, 4, 2, 1));
        sel.Rotate90(img);
        Assert.Equal(new Size(1, 2), sel.Bounds.Size);
        sel.Anchor(img);
        Assert.Equal(Blue, img.Get(sel.Bounds.X, sel.Bounds.Y));
        Assert.Equal(Red, img.Get(sel.Bounds.X, sel.Bounds.Y + 1));

        var cut = sel.Cut(img);
        Assert.NotNull(cut);
        Assert.True(img.IsRegionEmpty(new Rectangle(4, 3, 2, 3)));
        Assert.True(sel.Contains(sel.Bounds.X, sel.Bounds.Y));
        sel.Deselect(img);
        Assert.True(sel.Contains(0, 7));
    }

    [Fact]
    public void Transforms_FlipRotateResize()
    {
        var img = new PixelImage(3, 2);
        img.Set(0, 0, Red);
        Assert.Equal(Red, ImageTransform.FlipHorizontal(img).Get(2, 0));
        Assert.Equal(Red, ImageTransform.FlipVertical(img).Get(0, 1));
        var cw = ImageTransform.Rotate90(img, clockwise: true);
        Assert.Equal((2, 3), (cw.Width, cw.Height));
        Assert.Equal(Red, cw.Get(1, 0));
        var ccw = ImageTransform.Rotate90(img, clockwise: false);
        Assert.Equal(Red, ccw.Get(0, 2));
        Assert.True(img.ContentEquals(ImageTransform.Rotate90(cw, clockwise: false)));

        var big = img.ResizeCanvas(5, 4, Anchor.BottomRight);
        Assert.Equal(Red, big.Get(2, 2));
        var center = img.ResizeCanvas(5, 4, Anchor.Center);
        Assert.Equal(Red, center.Get(1, 1));
        var small = img.ResizeCanvas(1, 1, Anchor.TopLeft);
        Assert.Equal(Red, small.Get(0, 0));
    }

    [Fact]
    public void PixelImage_BlitClipsAtEdges()
    {
        var src = new PixelImage(4, 4);
        src.Clear(Red);
        var dst = new PixelImage(4, 4);
        dst.Blit(src, -2, -1);
        Assert.Equal(Red, dst.Get(1, 2));
        Assert.Equal(Color32.Transparent, dst.Get(2, 0));
        Assert.Equal(Color32.Transparent, dst.Get(0, 3));
        Assert.Equal(new Rectangle(0, 0, 2, 3), dst.GetOpaqueBounds());
    }
}
