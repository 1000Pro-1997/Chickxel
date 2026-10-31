using System.Drawing;
using Chickxel.Core.Edit;

namespace Chickxel.Core.Tests;

public class ShapeTests
{
    private static Rectangle Bounds(IEnumerable<Point> pts)
    {
        var list = pts.ToList();
        return Rectangle.FromLTRB(list.Min(p => p.X), list.Min(p => p.Y), list.Max(p => p.X) + 1, list.Max(p => p.Y) + 1);
    }

    private static bool Connected(IReadOnlyCollection<Point> pts)
    {
        var set = pts.ToHashSet();
        var seen = new HashSet<Point> { pts.First() };
        var queue = new Queue<Point>(seen);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var q = new Point(p.X + dx, p.Y + dy);
                    if (set.Contains(q) && seen.Add(q)) queue.Enqueue(q);
                }
        }
        return seen.Count == set.Count;
    }

    [Fact]
    public void Line_runs_from_press_to_release_snaps_to_15_degrees_with_shift_and_has_no_fill()
    {
        var style = new ShapeStyle(ShapeKind.Line);
        var free = Shapes.Outline(style, new Point(1, 1), new Point(9, 4), false);
        Assert.Equal(new Point(1, 1), free[0]);
        Assert.Equal(new Point(9, 4), free[^1]);
        Assert.Equal(free, Shapes.Fill(style, new Point(1, 1), new Point(9, 4), false));
        Assert.False(Shapes.CanFill(ShapeKind.Line));

        // Shift: 15° 단위 방향(길이는 그대로, 끝점은 가장 가까운 픽셀).
        var start = new Point(0, 0);
        Assert.All(Shapes.Outline(style, start, new Point(30, 3), true), p => Assert.Equal(0, p.Y));   // 5.7° → 0°
        Assert.Equal(new Point(21, 21), Shapes.Outline(style, start, new Point(20, 22), true)[^1]);    // 47.7° → 45°
        Assert.Equal(new Point(26, 15), Shapes.Outline(style, start, new Point(25, 16), true)[^1]);    // 32.6° → 30°
        Assert.Equal(new Point(30, 8), Shapes.Outline(style, start, new Point(30, 7), true)[^1]);      // 13.1° → 15°
        Assert.Equal(new Point(-8, -29), Shapes.Outline(style, start, new Point(-6, -29), true)[^1]);  // −101.7° → −105°
    }

    [Fact]
    public void Curve_runs_between_the_ends_and_bends_through_the_pointer()
    {
        var a = new Point(0, 10);
        var b = new Point(20, 10);
        var bent = Shapes.Curve(a, b, new Point(10, 2));
        Assert.Equal(a, bent[0]);
        Assert.Equal(b, bent[^1]);
        Assert.Contains(new Point(10, 2), bent);
        Assert.True(Connected(bent));
        Assert.Equal(2, bent.Min(p => p.Y));
        // 좌우 대칭(가운데를 지나게 휘면)
        var set = bent.ToHashSet();
        Assert.All(bent, p => Assert.Contains(new Point(20 - p.X, p.Y), set));
        // 픽셀 퍼펙트: 계단 모서리 L자 겹 픽셀이 없다(가로·세로로 붙은 이웃이 셋 이상인 점 없음).
        Assert.All(bent, p => Assert.True(new[] { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) }
            .Count(set.Contains) <= 2));
        // 가운데를 지나면 직선.
        Assert.Equal(Raster.Line(a, b), Shapes.Curve(a, b, new Point(10, 10)));
    }

    private static bool FourConnected(IReadOnlyCollection<Point> pts)
    {
        var set = pts.ToHashSet();
        var seen = new HashSet<Point> { pts.First() };
        var queue = new Queue<Point>(seen);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var q in new[] { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) })
                if (set.Contains(q) && seen.Add(q)) queue.Enqueue(q);
        }
        return seen.Count == set.Count;
    }

    [Theory]
    [InlineData(0, 0, 7, 3)]
    [InlineData(5, 5, -4, 9)]
    [InlineData(0, 0, 6, 6)]
    [InlineData(2, 2, 2, -5)]
    public void Orthogonal_line_steps_only_sideways_or_up_down_and_stays_on_the_line(int ax, int ay, int bx, int by)
    {
        var a = new Point(ax, ay);
        var b = new Point(bx, by);
        var pts = Shapes.Outline(new ShapeStyle(ShapeKind.Line, Orthogonal: true), a, b, false);
        Assert.Equal(a, pts[0]);
        Assert.Equal(b, pts[^1]);
        Assert.Equal(Math.Abs(bx - ax) + Math.Abs(by - ay) + 1, pts.Count);
        for (int i = 1; i < pts.Count; i++)
            Assert.Equal(1, Math.Abs(pts[i].X - pts[i - 1].X) + Math.Abs(pts[i].Y - pts[i - 1].Y));
        // 이상적인 선에서 한 칸 안쪽(대각선 칸 하나만큼)
        double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        Assert.All(pts, p => Assert.True(Math.Abs((p.X - ax) * (by - ay) - (p.Y - ay) * (bx - ax)) / Math.Max(1, len) <= 0.75));
        // 기본(대각선)은 그대로 브레젠험.
        Assert.Equal(Raster.Line(a, b), Shapes.Outline(new ShapeStyle(ShapeKind.Line), a, b, false));
    }

    [Fact]
    public void Orthogonal_curve_has_every_pixel_touching_by_an_edge()
    {
        var pts = Shapes.Curve(new Point(0, 12), new Point(24, 12), new Point(12, 2), orthogonal: true);
        Assert.True(FourConnected(pts));
        Assert.Contains(new Point(12, 2), pts);
        var set = pts.ToHashSet();
        Assert.All(pts, p => Assert.Contains(new Point(24 - p.X, p.Y), set));
        Assert.False(FourConnected(Shapes.Curve(new Point(0, 12), new Point(24, 12), new Point(12, 2))));
    }

    [Fact]
    public void Shift_curve_is_a_circular_arc_through_both_ends_and_the_pointer()
    {
        // (0,10)·(20,10)·(10,0) → 중심 (10,10) 반지름 10의 위쪽 반원.
        var arc = Shapes.Curve(new Point(0, 10), new Point(20, 10), new Point(10, 0), arc: true);
        Assert.Equal(new Point(0, 10), arc[0]);
        Assert.Equal(new Point(20, 10), arc[^1]);
        Assert.Contains(new Point(10, 0), arc);
        Assert.True(Connected(arc));
        Assert.All(arc, p =>
        {
            Assert.InRange(Math.Sqrt((p.X - 10) * (p.X - 10) + (p.Y - 10) * (p.Y - 10)), 9.3, 10.7);
            Assert.True(p.Y <= 10);
        });
        // 베지어보다 둥글다: 끝 근처에서 더 높이 올라감(원은 끝에서 수직으로 선다).
        var bezier = Shapes.Curve(new Point(0, 10), new Point(20, 10), new Point(10, 0));
        Assert.True(arc.Where(p => p.X == 1).Min(p => p.Y) < bezier.Where(p => p.X == 1).Min(p => p.Y));
        // 포인터가 반대쪽이면 아래로 도는 호.
        Assert.All(Shapes.Curve(new Point(0, 10), new Point(20, 10), new Point(10, 16), arc: true), p => Assert.True(p.Y >= 10));
        // 세 점이 한 줄이면 곧은 선.
        Assert.Equal(Raster.Line(new Point(0, 10), new Point(20, 10)), Shapes.Curve(new Point(0, 10), new Point(20, 10), new Point(10, 10), arc: true));
    }

    [Fact]
    public void Equilateral_triangle_uses_the_drag_as_one_side_and_stands_on_its_left()
    {
        var style = new ShapeStyle(ShapeKind.Triangle);
        var up = Shapes.Outline(style, new Point(0, 10), new Point(10, 10), false);
        Assert.Contains(new Point(0, 10), up);
        Assert.Contains(new Point(10, 10), up);
        Assert.Contains(new Point(5, 1), up); // 높이 10·√3/2 ≈ 8.66
        Assert.All(up, p => Assert.InRange(p.Y, 1, 10));

        // 반대로 끌면 아래로 선다.
        var down = Shapes.Outline(style, new Point(10, 10), new Point(0, 10), false);
        Assert.All(down, p => Assert.InRange(p.Y, 10, 19));

        // 세로로 끌면 그 선이 한 변(아래로 끌면 오른쪽에 선다).
        var side = Shapes.Outline(style, new Point(0, 0), new Point(0, 10), false);
        Assert.Equal(new Rectangle(0, 0, 10, 11), Bounds(side));
    }

    [Theory]
    [InlineData(9, 7)]
    [InlineData(10, 7)]
    public void Isosceles_triangle_fills_the_dragged_box_and_points_away_from_the_drag(int w, int h)
    {
        var style = new ShapeStyle(ShapeKind.IsoscelesTriangle);
        var start = new Point(2, 3);
        var end = new Point(2 + w - 1, 3 + h - 1);
        var box = new Rectangle(2, 3, w, h);
        foreach (var filled in new[] { false, true })
        {
            var down = filled ? Shapes.Fill(style, start, end, false) : Shapes.Outline(style, start, end, false);
            Assert.Equal(box, Bounds(down));
            // 아래로 끌었으면 꼭짓점이 위: 맨 윗줄은 가운데 1픽셀(짝수 너비는 2픽셀), 맨 아랫줄은 밑변 전체.
            Assert.Equal(w % 2 == 0 ? 2 : 1, down.Count(p => p.Y == box.Top));
            Assert.Equal(w, down.Count(p => p.Y == box.Bottom - 1));

            // 위로 끌면 뒤집힌다.
            var upStart = new Point(2, 3 + h - 1);
            var upEnd = new Point(2 + w - 1, 3);
            var up = filled ? Shapes.Fill(style, upStart, upEnd, false) : Shapes.Outline(style, upStart, upEnd, false);
            Assert.Equal(box, Bounds(up));
            Assert.Equal(w, up.Count(p => p.Y == box.Top));
            Assert.Equal(w % 2 == 0 ? 2 : 1, up.Count(p => p.Y == box.Bottom - 1));
        }
    }

    [Theory]
    [InlineData(ShapeKind.IsoscelesTriangle, 0, 13, 11)]
    [InlineData(ShapeKind.IsoscelesTriangle, 0, 14, 11)]
    [InlineData(ShapeKind.Polygon, 5, 15, 15)]
    [InlineData(ShapeKind.Polygon, 5, 16, 15)]
    [InlineData(ShapeKind.Polygon, 6, 20, 17)]
    [InlineData(ShapeKind.Polygon, 7, 31, 30)]
    [InlineData(ShapeKind.Polygon, 4, 12, 12)]
    [InlineData(ShapeKind.Star, 5, 21, 20)]
    [InlineData(ShapeKind.Star, 6, 24, 24)]
    [InlineData(ShapeKind.Cube, 0, 17, 18)]
    [InlineData(ShapeKind.Cube, 0, 16, 17)]
    [InlineData(ShapeKind.Cylinder, 0, 12, 20)]
    [InlineData(ShapeKind.Ellipse, 0, 12, 9)]
    public void Fitted_shapes_fill_the_box_and_are_mirror_symmetric(ShapeKind kind, int count, int w, int h)
    {
        var style = new ShapeStyle(kind, Sides: count, StarPoints: count);
        var box = new Rectangle(-3, 4, w, h);
        var start = box.Location;
        var end = new Point(box.Right - 1, box.Bottom - 1);
        var outline = Shapes.Outline(style, start, end, false);
        var fill = Shapes.Fill(style, start, end, false);
        Assert.Equal(box, Bounds(outline));
        Assert.Equal(box, Bounds(fill));
        Assert.True(Connected(outline));
        Assert.Equal(outline.Count, outline.Distinct().Count());
        var fillSet = fill.ToHashSet();
        Assert.All(outline, p => Assert.Contains(p, fillSet));
        Assert.True(fill.Count > outline.Count);
        foreach (var pts in new[] { outline, fill })
        {
            var set = pts.ToHashSet();
            Assert.All(pts, p => Assert.Contains(new Point(box.Left + box.Right - 1 - p.X, p.Y), set));
        }
        // 위아래로도 대칭인 모양(짝수 꼭짓점 정다각형·짝수 뿔 별)
        if (kind is ShapeKind.Polygon or ShapeKind.Star && count % 2 == 0)
        {
            var set = outline.ToHashSet();
            Assert.All(outline, p => Assert.Contains(new Point(p.X, box.Top + box.Bottom - 1 - p.Y), set));
        }
    }

    [Fact]
    public void Shift_keeps_regular_polygons_and_stars_undistorted()
    {
        // 뾰족한 위의 정육각형: 픽셀 중심 사이 가로:세로 = √3/2.
        var hex = Shapes.Outline(new ShapeStyle(ShapeKind.Polygon, Sides: 6), new Point(0, 0), new Point(40, 2), true);
        var b = Bounds(hex);
        Assert.Equal(41, b.Width);
        Assert.Equal((int)Math.Round(40 / (Math.Sqrt(3) / 2)) + 1, b.Height);

        // 시작점에서 끈 쪽으로 커진다(왼쪽 위로 끌기).
        var star = Shapes.Outline(new ShapeStyle(ShapeKind.Star, StarPoints: 5), new Point(50, 50), new Point(49, 20), true);
        var s = Bounds(star);
        Assert.Equal(50, s.Right - 1);
        Assert.Equal(50, s.Bottom - 1);
        Assert.Equal(20, s.Top);
        Assert.True(s.Width > 2);

        // 사각형·원·원기둥·주사위·나선은 정사각형.
        foreach (var kind in new[] { ShapeKind.Rectangle, ShapeKind.Ellipse, ShapeKind.Cylinder, ShapeKind.Cube, ShapeKind.Spiral })
        {
            var r = Bounds(Shapes.Outline(new ShapeStyle(kind), new Point(0, 0), new Point(20, 5), true));
            Assert.True(r.Width <= 21 && r.Height <= 21 && Math.Max(r.Width, r.Height) >= 20, $"{kind}: {r}");
        }
    }

    [Fact]
    public void Spiral_fills_the_box_as_one_unbroken_line_ending_at_the_side_it_was_dragged_toward_and_has_no_fill()
    {
        var style = new ShapeStyle(ShapeKind.Spiral, SpiralTurns: 3);
        var right = Shapes.Outline(style, new Point(0, 0), new Point(30, 20), false);
        Assert.True(Connected(right));
        Assert.Equal(new Rectangle(0, 0, 31, 21), Bounds(right));
        Assert.Equal(30, right[^1].X);
        Assert.Equal(right, Shapes.Fill(style, new Point(0, 0), new Point(30, 20), false));
        Assert.False(Shapes.CanFill(ShapeKind.Spiral));

        // 왼쪽으로 끌면 거울 모양(반대로 감김).
        var left = Shapes.Outline(style, new Point(30, 0), new Point(0, 20), false);
        var leftSet = left.ToHashSet();
        Assert.All(right, p => Assert.Contains(new Point(30 - p.X, p.Y), leftSet));
        Assert.Equal(0, left[^1].X);
    }

    [Fact]
    public void Cube_has_three_front_edges_meeting_in_the_middle()
    {
        // 17 × 18: 윗면 높이 = 너비의 절반(2:1), 가운데 세로 모서리.
        var cube = Shapes.Outline(new ShapeStyle(ShapeKind.Cube), new Point(0, 0), new Point(16, 17), false).ToHashSet();
        Assert.Contains(new Point(8, 0), cube);   // 위
        Assert.Contains(new Point(0, 4), cube);   // 왼쪽 위
        Assert.Contains(new Point(8, 8), cube);   // 가운데(앞 모서리가 만나는 곳)
        Assert.Contains(new Point(4, 6), cube);   // 가운데 → 왼쪽 위 모서리 중간
        for (int y = 8; y <= 17; y++) Assert.Contains(new Point(8, y), cube);
        Assert.DoesNotContain(new Point(8, 4), cube); // 윗면 안은 비어 있다
    }

    [Fact]
    public void Single_point_drag_draws_one_pixel_for_every_shape()
    {
        foreach (var kind in Enum.GetValues<ShapeKind>())
        {
            var style = new ShapeStyle(kind);
            Assert.Equal([new Point(4, 4)], Shapes.Outline(style, new Point(4, 4), new Point(4, 4), false));
            Assert.Equal([new Point(4, 4)], Shapes.Fill(style, new Point(4, 4), new Point(4, 4), true));
        }
    }
}
