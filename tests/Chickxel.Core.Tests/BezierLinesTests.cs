using System.Drawing;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.Tests;

/// <summary>베지어 경로 맞추기·칠하기·고치기(<see cref="BezierLines"/>).</summary>
public class BezierLinesTests
{
    /// <summary>열린 경로 위 점들에서 곡선까지 가장 먼 거리.</summary>
    private static double MaxDeviation(IReadOnlyList<BezierNode> nodes, bool closed, IEnumerable<(double X, double Y)> ideal)
        => ideal.Max(p => BezierLines.Nearest(nodes, closed, p).Distance);

    /// <summary>흔들린(±<paramref name="jitter"/>) 점 목록.</summary>
    private static List<(double X, double Y)> Jitter(IEnumerable<(double X, double Y)> pts, double jitter, int seed)
    {
        var random = new Random(seed);
        return pts.Select(p => (p.X + (random.NextDouble() * 2 - 1) * jitter, p.Y + (random.NextDouble() * 2 - 1) * jitter)).ToList();
    }

    private static bool IsL(Point a, Point b, Point c)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1 && Math.Abs(b.X - c.X) + Math.Abs(b.Y - c.Y) == 1
            && Math.Abs(a.X - c.X) == 1 && Math.Abs(a.Y - c.Y) == 1;

    private static void AssertConnected(IReadOnlyList<Point> cells, bool closed)
    {
        for (int i = 1; i < cells.Count; i++)
            Assert.True(Math.Max(Math.Abs(cells[i].X - cells[i - 1].X), Math.Abs(cells[i].Y - cells[i - 1].Y)) == 1, $"끊김 {cells[i - 1]} → {cells[i]}");
        if (closed && cells.Count > 2)
            Assert.True(Math.Max(Math.Abs(cells[0].X - cells[^1].X), Math.Abs(cells[0].Y - cells[^1].Y)) == 1);
    }

    [Fact]
    public void JitteredSCurveFitsWithFewSmoothNodes()
    {
        var ideal = Enumerable.Range(0, 301).Select(i => i * 0.15).Select(x => (X: 4 + x, Y: 24 + 8 * Math.Sin(x / 45 * 2 * Math.PI))).ToList();
        var nodes = BezierLines.Fit(Jitter(ideal, 0.25, 7), 0.8, 40, false);

        Assert.InRange(nodes.Count, 2, 3);
        Assert.True(MaxDeviation(nodes, false, ideal) <= 0.8, $"벗어남 {MaxDeviation(nodes, false, ideal):0.###}");
        // 원호 조각이 아니라 손잡이가 펼쳐진 곡선, 가운데 점은 매끈(손잡이가 한 줄).
        Assert.Contains(nodes, n => !n.OutRetracted && !n.InRetracted || n == nodes[0] && !n.OutRetracted);
        for (int k = 1; k < nodes.Count - 1; k++)
        {
            var n = nodes[k];
            Assert.False(n.Corner);
            double cross = (n.Point.X - n.In.X) * (n.Out.Y - n.Point.Y) - (n.Point.Y - n.In.Y) * (n.Out.X - n.Point.X);
            Assert.True(Math.Abs(cross) < 1e-6 * (1 + Math.Abs(n.Out.X - n.In.X) + Math.Abs(n.Out.Y - n.In.Y)), "매끈한 점의 손잡이가 한 줄이 아님");
        }
        AssertConnected(BezierLines.Rasterize(nodes, false), false);
    }

    [Fact]
    public void QuarterEllipseIsOneOrTwoCubics()
    {
        var ideal = Enumerable.Range(0, 401).Select(i => i / 400.0 * Math.PI / 2).Select(a => (X: 5 + 30 * Math.Cos(a), Y: 5 + 18 * Math.Sin(a))).ToList();
        var nodes = BezierLines.Fit(Jitter(ideal, 0.2, 3), 0.6, 40, false);

        Assert.InRange(nodes.Count, 2, 3);
        Assert.True(MaxDeviation(nodes, false, ideal) <= 0.6, $"벗어남 {MaxDeviation(nodes, false, ideal):0.###}");
    }

    /// <summary>곡선 펜 픽셀 퍼펙트를 끄면 L자 겹 칸이 남고(끊기지 않음), 켜면 없다.</summary>
    [Fact]
    public void RasterizeWithoutPixelPerfectKeepsLCorners()
    {
        var ideal = Enumerable.Range(0, 401).Select(i => i / 400.0 * Math.PI / 2).Select(a => (X: 5 + 30 * Math.Cos(a), Y: 5 + 18 * Math.Sin(a))).ToList();
        var nodes = BezierLines.Fit(ideal, 0.6, 40, false);
        var on = BezierLines.Rasterize(nodes, false);
        var off = BezierLines.Rasterize(nodes, false, pixelPerfect: false);
        static int Ls(List<Point> c) => Enumerable.Range(1, Math.Max(0, c.Count - 2)).Count(i => IsL(c[i - 1], c[i], c[i + 1]));
        AssertConnected(off, false);
        Assert.Equal(0, Ls(on));
        Assert.True(Ls(off) > 0, "끈 결과에 L자 칸이 없음");
        Assert.True(off.Count > on.Count);
    }

    [Fact]
    public void StraightStrokeBecomesOneStraightSegment()
    {
        var ideal = Enumerable.Range(0, 101).Select(i => (X: 2 + i * 0.2, Y: 3 + i * 0.1)).ToList();
        var nodes = BezierLines.Fit(Jitter(ideal, 0.2, 11), 0.6, 40, false);

        Assert.Equal(2, nodes.Count);
        var (p0, p1, p2, p3) = BezierLines.Segment(nodes, 0);
        Assert.Equal(p0.X + (p3.X - p0.X) / 3, p1.X, 6);
        Assert.Equal(p0.Y + (p3.Y - p0.Y) / 3, p1.Y, 6);
        Assert.Equal(p0.X + (p3.X - p0.X) * 2 / 3, p2.X, 6);
        Assert.Equal(p0.Y + (p3.Y - p0.Y) * 2 / 3, p2.Y, 6);
    }

    [Fact]
    public void ClosedCircleStrokeFitsSmoothLoop()
    {
        var ideal = Enumerable.Range(0, 600).Select(i => i / 600.0 * 2 * Math.PI).Select(a => (X: 30 + 14 * Math.Cos(a), Y: 30 + 14 * Math.Sin(a))).ToList();
        var nodes = BezierLines.Fit(Jitter(ideal, 0.2, 5), 0.6, 40, true);

        Assert.InRange(nodes.Count, 2, 6);
        Assert.All(nodes, n => Assert.False(n.Corner));
        Assert.True(MaxDeviation(nodes, true, ideal) <= 0.6, $"벗어남 {MaxDeviation(nodes, true, ideal):0.###}");
        AssertConnected(BezierLines.Rasterize(nodes, true), true);
    }

    [Fact]
    public void SquareStrokeKeepsCorners()
    {
        var pts = new List<(double X, double Y)>();
        (double X, double Y)[] corners = [(4, 4), (24, 4), (24, 24), (4, 24)];
        for (int k = 0; k < 4; k++)
        {
            var (a, b) = (corners[k], corners[(k + 1) % 4]);
            for (int i = 0; i < 100; i++) pts.Add((a.X + (b.X - a.X) * i / 100.0, a.Y + (b.Y - a.Y) * i / 100.0));
        }
        var nodes = BezierLines.Fit(pts, 0.6, 40, true);

        Assert.True(nodes.Count == 4, string.Join(" | ", nodes.Select(n => $"{n.Point.X:0.##},{n.Point.Y:0.##} {(n.Corner ? "C" : "S")}")));
        Assert.All(nodes, n => Assert.True(n.Corner));
        var cells = new List<Point>();
        var raster = BezierLines.Rasterize(nodes, true, cells);
        Assert.Equal(4, cells.Count);
        Assert.Contains(new Point(24, 24), raster);
    }

    /// <summary>점 다섯 개짜리 물결(매끈한 점, 가로 손잡이).</summary>
    private static List<BezierNode> Wave()
    {
        (double X, double Y)[] points = [(2.5, 12.5), (14.5, 4.5), (26.5, 12.5), (38.5, 20.5), (50.5, 12.5)];
        return points.Select(p => new BezierNode(p, (p.X - 4, p.Y), (p.X + 4, p.Y), false)).ToList();
    }

    private static RectangleF Bounds(IReadOnlyList<BezierNode> nodes, int segment)
    {
        var (p0, p1, p2, p3) = BezierLines.Segment(nodes, segment);
        double[] xs = [p0.X, p1.X, p2.X, p3.X], ys = [p0.Y, p1.Y, p2.Y, p3.Y];
        return RectangleF.FromLTRB((float)Math.Floor(xs.Min()) - 1, (float)Math.Floor(ys.Min()) - 1, (float)Math.Floor(xs.Max()) + 2, (float)Math.Floor(ys.Max()) + 2);
    }

    [Fact]
    public void MovingOneNodeChangesOnlyItsTwoSegments()
    {
        var before = Wave();
        var after = before.ToList();
        after[2] = after[2].Offset(1, -5);
        var a = BezierLines.Rasterize(before, false);
        var b = BezierLines.Rasterize(after, false);
        Assert.NotEqual(a, b);

        RectangleF[] local = [Bounds(before, 1), Bounds(before, 2), Bounds(after, 1), Bounds(after, 2)];
        bool Outside(Point p) => !local.Any(r => r.Contains(p.X + 0.5f, p.Y + 0.5f));
        Assert.Equal(a.Where(Outside).ToList(), b.Where(Outside).ToList());
        // 이웃 점은 움직이지 않는다.
        Assert.Equal(before[1], after[1]);
        Assert.Equal(before[3], after[3]);
        AssertConnected(b, false);
    }

    [Fact]
    public void SplitKeepsTheCurve()
    {
        List<BezierNode> nodes = [new((3.5, 20.5), (3.5, 20.5), (10, 2), false), new((40.5, 18.5), (30, 30), (40.5, 18.5), false)];
        var (split, index) = BezierLines.Split(nodes, 0, 0.37, false);

        Assert.Equal(1, index);
        Assert.Equal(3, split.Count);
        for (int i = 0; i <= 100; i++)
        {
            var p = BezierLines.PointAt(nodes, 0, i / 100.0);
            Assert.True(BezierLines.Nearest(split, false, p).Distance < 1e-4);
        }
        var q = BezierLines.PointAt(nodes, 0, 0.37);
        Assert.Equal(q.X, split[1].Point.X, 9);
        Assert.Equal(q.Y, split[1].Point.Y, 9);
        Assert.False(split[1].Corner);

        // 뺀 점을 다시 합치면 거의 같은 곡선.
        var merged = BezierLines.Remove(split, 1, false);
        Assert.Equal(2, merged.Count);
        for (int i = 0; i <= 50; i++)
            Assert.True(BezierLines.Nearest(merged, false, BezierLines.PointAt(nodes, 0, i / 50.0)).Distance < 0.3);
    }

    [Fact]
    public void StraightSegmentAtOneToTwoHasEvenRuns()
    {
        (double X, double Y) a = (0.5, 0.5), b = (20.5, 10.5);
        List<BezierNode> nodes =
        [
            new(a, a, (a.X + (b.X - a.X) / 3, a.Y + (b.Y - a.Y) / 3), false),
            new(b, (a.X + (b.X - a.X) * 2 / 3, a.Y + (b.Y - a.Y) * 2 / 3), b, false),
        ];
        var cells = BezierLines.Rasterize(nodes, false);
        var runs = cells.GroupBy(c => c.Y).OrderBy(g => g.Key).Select(g => g.Count()).ToList();

        Assert.Equal(11, runs.Count);
        Assert.All(runs.Skip(1).SkipLast(1), r => Assert.Equal(2, r));
        AssertConnected(cells, false);
    }

    [Fact]
    public void CircleRastersMonotoneRunsWithoutLCorners()
    {
        const double k = 0.5522847498, r = 12;
        (double X, double Y) c = (20, 20);
        List<BezierNode> nodes =
        [
            new((c.X, c.Y - r), (c.X - r * k, c.Y - r), (c.X + r * k, c.Y - r), false),
            new((c.X + r, c.Y), (c.X + r, c.Y - r * k), (c.X + r, c.Y + r * k), false),
            new((c.X, c.Y + r), (c.X + r * k, c.Y + r), (c.X - r * k, c.Y + r), false),
            new((c.X - r, c.Y), (c.X - r, c.Y + r * k), (c.X - r, c.Y - r * k), false),
        ];
        var cells = BezierLines.Rasterize(nodes, true);
        AssertConnected(cells, true);
        int n = cells.Count;
        for (int i = 0; i < n; i++) Assert.False(IsL(cells[i], cells[(i + 1) % n], cells[(i + 2) % n]), $"L자 {cells[(i + 1) % n]}");

        // 옥타브(대각 방향·곧은 방향이 같은 이동 덩어리)마다 계단 길이가 한쪽으로만 바뀐다(첫·끝 계단은 뺌).
        var moves = Enumerable.Range(0, n).Select(i => (X: cells[(i + 1) % n].X - cells[i].X, Y: cells[(i + 1) % n].Y - cells[i].Y)).ToList();
        var octants = new List<List<(int X, int Y)>>();
        (int X, int Y)? diagonal = null, straight = null;
        foreach (var m in moves)
        {
            bool diag = m.X != 0 && m.Y != 0;
            bool fresh = octants.Count == 0
                || diag && diagonal is { } d && d != m
                || !diag && straight is { } t && t != m
                || diag && straight is { } st && (st.X != 0 && st.X != m.X || st.Y != 0 && st.Y != m.Y)
                || !diag && diagonal is { } dd && (m.X != 0 && m.X != dd.X || m.Y != 0 && m.Y != dd.Y);
            if (fresh)
            {
                octants.Add([]);
                diagonal = straight = null;
            }
            octants[^1].Add(m);
            if (diag) diagonal = m;
            else straight = m;
        }
        foreach (var octant in octants)
        {
            var runs = new List<int> { 0 };
            foreach (var m in octant)
                if (m.X != 0 && m.Y != 0) runs.Add(0);
                else runs[^1]++;
            var inner = runs.Skip(1).SkipLast(1).ToList();
            if (inner.Count < 3) continue;
            bool up = inner.Zip(inner.Skip(1)).All(p => p.First <= p.Second);
            bool down = inner.Zip(inner.Skip(1)).All(p => p.First >= p.Second);
            Assert.True(up || down, "계단 길이가 오르내림: " + string.Join(",", inner));
        }
    }

    [Fact]
    public void CornerCellIsKept()
    {
        List<BezierNode> nodes = [BezierNode.At((0.5, 0.5)), BezierNode.At((10.5, 0.5)), BezierNode.At((10.5, 10.5))];
        var corners = new List<Point>();
        var cells = BezierLines.Rasterize(nodes, false, corners);

        Assert.Contains(new Point(10, 0), cells);
        Assert.Equal([new Point(10, 0)], corners);
        AssertConnected(cells, false);
    }

    [Fact]
    public void DragHandleKeepsSmoothNodeCollinear()
    {
        var node = new BezierNode((10, 10), (6, 10), (13, 10), false);
        var moved = BezierLines.DragHandle(node, true, (10, 4), false);
        Assert.Equal((10, 4), moved.Out);
        Assert.Equal(10, moved.In.X, 9);
        Assert.Equal(14, moved.In.Y, 9);

        var broken = BezierLines.DragHandle(node, true, (10, 4), true);
        Assert.True(broken.Corner);
        Assert.Equal(node.In, broken.In);
    }

    [Fact]
    public void ToggleCornerRetractsAndRestoresHandles()
    {
        List<BezierNode> nodes = [BezierNode.At((0, 0)), new((10, 5), (7, 5), (13, 5), false), BezierNode.At((20, 0))];
        var corner = BezierLines.ToggleCorner(nodes, 1, false);
        Assert.True(corner[1].Corner);
        Assert.True(corner[1].InRetracted && corner[1].OutRetracted);
        Assert.Equal(nodes[0], corner[0]);
        Assert.Equal(nodes[2], corner[2]);

        var smooth = BezierLines.ToggleCorner(corner, 1, false);
        Assert.False(smooth[1].Corner);
        Assert.Equal(smooth[1].In.Y, smooth[1].Point.Y, 9);
        Assert.Equal(smooth[1].Out.Y, smooth[1].Point.Y, 9);
        Assert.True(smooth[1].In.X < 10 && smooth[1].Out.X > 10);
    }
}
