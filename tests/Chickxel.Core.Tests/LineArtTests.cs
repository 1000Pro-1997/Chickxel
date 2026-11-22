using System.Drawing;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.Tests;

/// <summary>깨끗한 픽셀 선(<see cref="CleanLines"/>)과 그림의 선 정리(<see cref="LineArt"/>).</summary>
public class LineArtTests
{
    private static readonly Color32 Ink = new(0x28, 0x18, 0x2D);
    private static readonly Color32 Fill = new(0xF0, 0xB0, 0x60);
    private static readonly Color32 Leaf = new(150, 210, 140);
    private static readonly Color32 Pink = new(235, 120, 160);

    private static double L(Color32 c) => OkLab.FromColor(c).L;

    private static List<Point> Cells(PixelImage img, Color32 c)
    {
        var cells = new List<Point>();
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (img[x, y] == c) cells.Add(new Point(x, y));
        return cells;
    }

    /// <summary>칸들이 8방향으로 한 덩어리인지.</summary>
    private static bool Connected(IReadOnlyCollection<Point> cells)
    {
        if (cells.Count == 0) return true;
        var set = cells.ToHashSet();
        var seen = new HashSet<Point> { cells.First() };
        var stack = new Stack<Point>(seen);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var q = new Point(p.X + dx, p.Y + dy);
                    if (set.Contains(q) && seen.Add(q)) stack.Push(q);
                }
        }
        return seen.Count == set.Count;
    }

    private static void AssertStepwise(IReadOnlyList<Point> path, bool closed)
    {
        for (int i = 1; i < path.Count; i++)
            Assert.Equal(1, Math.Max(Math.Abs(path[i].X - path[i - 1].X), Math.Abs(path[i].Y - path[i - 1].Y)));
        if (closed && path.Count > 2)
            Assert.Equal(1, Math.Max(Math.Abs(path[0].X - path[^1].X), Math.Abs(path[0].Y - path[^1].Y)));
    }

    /// <summary>채운 면 위에 손으로 그린 듯 들쭉날쭉한 2px 두께 1:2 대각선.</summary>
    private static PixelImage JaggyDiagonal()
    {
        var img = new PixelImage(34, 24);
        img.FillRect(new Rectangle(1, 1, 32, 22), Fill);
        for (int x = 4; x <= 29; x++)
        {
            int y = 4 + x / 2 + ((x * 7) % 5 == 0 ? 1 : 0);
            img[x, y] = Ink;
            img[x, y + 1] = Ink;
        }
        return img;
    }

    /// <summary>울퉁불퉁한 2px 두께 원 외곽선(가운데 (20, 20), 반지름 약 13)과 그 안의 채운 면 — 바깥은 투명.</summary>
    private static PixelImage RoughRing()
    {
        var img = new PixelImage(40, 40);
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 40; x++)
            {
                double dx = x + 0.5 - 20, dy = y + 0.5 - 20, d = Math.Sqrt(dx * dx + dy * dy);
                double r = 13 + 0.4 * Math.Sin(5 * Math.Atan2(dy, dx));
                if (Math.Abs(d - r) <= 1) img[x, y] = Ink;
                else if (d < r) img[x, y] = Fill;
            }
        return img;
    }

    /// <summary>열마다 그 색 픽셀 수.</summary>
    private static int Column(PixelImage img, int x, Color32 c)
        => Enumerable.Range(0, img.Height).Count(y => img[x, y] == c);

    [Fact]
    public void JaggyThickDiagonalBecomesEvenOnePixelLineOverRestoredFill()
    {
        var img = JaggyDiagonal();
        var before = img.Clone();
        var o = new LineArtOptions();
        var trace = LineArt.Trace(img, o);
        Assert.NotEmpty(trace.Strokes);
        var result = LineArt.Apply(img, trace, o);
        Assert.True(before.ContentEquals(img));

        var ink = Cells(result, Ink);
        Assert.True(Connected(ink));
        for (int x = 6; x <= 27; x++) Assert.Equal(1, Column(result, x, Ink));
        for (int i = 0; i < img.Pixels.Length; i++)
            if (before.Pixels[i] == Ink) Assert.True(result.Pixels[i] == Ink || result.Pixels[i] == Fill);
            else Assert.True(result.Pixels[i] == before.Pixels[i] || result.Pixels[i] == Ink);
        var runs = ink.GroupBy(p => p.Y).OrderBy(g => g.Key).Select(g => g.Count()).ToList();
        var inner = runs.Skip(1).SkipLast(1).ToList();
        Assert.True(inner.Max() - inner.Min() <= 1, string.Join(",", runs));
        Assert.InRange(inner.Min(), 1, 3);
    }

    [Fact]
    public void RoughRingBecomesMonotoneLoopAndOutsideOfOldRingClears()
    {
        var img = RoughRing();
        var o = new LineArtOptions();
        var trace = LineArt.Trace(img, o);
        Assert.Contains(trace.Strokes, s => s.Closed);
        var result = LineArt.Apply(img, trace, o);

        var ink = Cells(result, Ink).ToHashSet();
        Assert.True(Connected(ink));
        Assert.All(ink, p =>
        {
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx | dy) != 0 && ink.Contains(new Point(p.X + dx, p.Y + dy))) n++;
            Assert.Equal(2, n);
        });

        // 바깥(둘레에서 새 선을 넘지 않고 닿는 곳)은 모두 투명, 안은 모두 채운 면.
        var outside = new HashSet<Point>();
        var queue = new Queue<Point>();
        for (int i = 0; i < 40; i++)
            foreach (var p in new[] { new Point(i, 0), new Point(i, 39), new Point(0, i), new Point(39, i) })
                if (outside.Add(p)) queue.Enqueue(p);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var q in new[] { new Point(p.X - 1, p.Y), new Point(p.X + 1, p.Y), new Point(p.X, p.Y - 1), new Point(p.X, p.Y + 1) })
                if (result.Contains(q.X, q.Y) && !ink.Contains(q) && outside.Add(q)) queue.Enqueue(q);
        }
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 40; x++)
            {
                var p = new Point(x, y);
                if (ink.Contains(p)) continue;
                Assert.Equal(outside.Contains(p) ? Color32.Transparent : Fill, result[x, y]);
            }

        // 사분면마다 가로 계단(위·아래 끝 줄부터)과 세로 계단(옆 끝 열부터)의 길이가 가운데로 갈수록 줄기만 한다.
        foreach (int sx in new[] { -1, 1 })
            foreach (int sy in new[] { -1, 1 })
            {
                bool Side(Point p) => (p.X >= 20) == (sx > 0) && (p.Y >= 20) == (sy > 0);
                var rows = ink.Where(Side).GroupBy(p => p.Y).OrderBy(g => sy * -g.Key).Select(g => g.Count()).ToList();
                var columns = ink.Where(Side).GroupBy(p => p.X).OrderBy(g => sx * -g.Key).Select(g => g.Count()).ToList();
                foreach (var runs in new[] { rows, columns })
                    for (int i = 2; i < runs.Count; i++)
                        Assert.True(runs[i] <= runs[i - 1], $"({sx}, {sy}): {string.Join(",", runs)}");
            }
    }

    [Fact]
    public void PickedLightLineIsCleanedWhileDarkBlobAndEyeStay()
    {
        var img = new PixelImage(30, 22);
        img.FillRect(new Rectangle(0, 0, 30, 22), Leaf);
        for (int x = 3; x <= 26; x++)
        {
            int y = 4 + ((x * 3) % 4 == 0 ? 1 : 0);
            img[x, y] = Pink;
            img[x, y + 1] = Pink;
        }
        img.FillRect(new Rectangle(4, 10, 7, 7), Ink);
        img.FillRect(new Rectangle(20, 12, 2, 2), Ink);
        var o = new LineArtOptions { Colors = [Pink] };
        var trace = LineArt.Trace(img, o);
        var result = LineArt.Apply(img, trace, o);

        for (int i = 0; i < img.Pixels.Length; i++)
            if (img.Pixels[i] == Ink) Assert.Equal(Ink, result.Pixels[i]);
        Assert.All(trace.Strokes, s => Assert.All(s.Pixels, p => Assert.Equal(Pink, img[p.X, p.Y])));
        for (int x = 5; x <= 24; x++) Assert.Equal(1, Column(result, x, Pink));
        Assert.True(Connected(Cells(result, Pink)));
        Assert.Single(Cells(result, Pink).Select(p => p.Y).Where(y => y != 4 && y != 5).DefaultIfEmpty(4).Distinct());
    }

    [Fact]
    public void ExcludedStrokesStayUntouched()
    {
        var img = new PixelImage(40, 24);
        img.FillRect(new Rectangle(0, 0, 40, 24), Fill);
        for (int y = 3; y <= 20; y++)
        {
            int left = 4 + y / 3 + (y % 4 == 0 ? 1 : 0), right = 24 + y / 3 + (y % 5 == 0 ? 1 : 0);
            img[left, y] = img[left + 1, y] = Ink;
            img[right, y] = img[right + 1, y] = Ink;
        }
        var o = new LineArtOptions();
        var trace = LineArt.Trace(img, o);
        var excluded = trace.Strokes.Where(s => s.Pixels.All(p => p.X < 20)).Select(s => s.Id).ToHashSet();
        Assert.NotEmpty(excluded);
        Assert.Contains(trace.Strokes, s => !excluded.Contains(s.Id));
        var result = LineArt.Apply(img, trace, o, excluded);

        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 20; x++)
                Assert.Equal(img[x, y], result[x, y]);
        for (int y = 5; y <= 18; y++)
            Assert.Equal(1, Enumerable.Range(20, 20).Count(x => result[x, y] == Ink));
    }

    [Fact]
    public void DarkenModePaintsLineWithDarkenedFill()
    {
        var img = RoughRing();
        var o = new LineArtOptions { ColorMode = LineColorMode.Darken, Darken = 0.35 };
        var result = LineArt.Apply(img, LineArt.Trace(img, o), o);
        var expected = OkLab.ShiftLightness(Fill, -0.35f);
        Assert.True(L(expected) < L(Fill) - 0.1);
        var fill = OkLab.FromColor(Fill);
        var line = OkLab.FromColor(expected);
        Assert.InRange(Math.Abs(Math.Atan2(line.B, line.A) - Math.Atan2(fill.B, fill.A)), 0, 0.3);
        Assert.Empty(Cells(result, Ink));
        var lineCells = Cells(result, expected);
        Assert.True(lineCells.Count > 60);
        Assert.True(Connected(lineCells));
        Assert.All(result.Pixels, c => Assert.True(c.A == 0 || c == Fill || c == expected));
    }

    [Fact]
    public void AreaMaskLimitsChanges()
    {
        var img = JaggyDiagonal();
        var area = new bool[img.Width * img.Height];
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < 16; x++) area[y * img.Width + x] = true;
        var o = new LineArtOptions();
        var trace = LineArt.Trace(img, o, area);
        Assert.All(trace.Strokes, s => Assert.All(s.Pixels, p => Assert.True(p.X < 16)));
        var result = LineArt.Apply(img, trace, o);
        bool changed = false;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (x >= 16) Assert.Equal(img[x, y], result[x, y]);
                else changed |= img[x, y] != result[x, y];
        Assert.True(changed);
    }

    [Fact]
    public void EmptyImageHasNoStrokesAndStaysEqual()
    {
        var img = new PixelImage(16, 16);
        var o = new LineArtOptions();
        var trace = LineArt.Trace(img, o);
        Assert.Empty(trace.Strokes);
        Assert.DoesNotContain(true, trace.LineMask);
        Assert.True(LineArt.Apply(img, trace, o).ContentEquals(img));
    }

    [Fact]
    public void CleanLinesDrawEqualsRasterizeOfFitOnRandomChains()
    {
        var random = new Random(1234);
        for (int trial = 0; trial < 60; trial++)
        {
            bool closed = trial % 3 == 0;
            var pts = new List<(double X, double Y)>();
            if (closed)
            {
                double r = 4 + random.NextDouble() * 12;
                int count = 20 + random.Next(60);
                for (int i = 0; i < count; i++)
                {
                    double a = 2 * Math.PI * i / count;
                    pts.Add((30 + r * Math.Cos(a) + random.NextDouble() - 0.5, 30 + r * Math.Sin(a) + random.NextDouble() - 0.5));
                }
            }
            else
            {
                double x = 30, y = 30, heading = random.NextDouble() * 2 * Math.PI;
                int count = 2 + random.Next(80);
                for (int i = 0; i < count; i++)
                {
                    pts.Add((x, y));
                    heading += (random.NextDouble() - 0.5) * (random.Next(10) == 0 ? 2.5 : 0.3);
                    x += Math.Cos(heading);
                    y += Math.Sin(heading);
                }
            }
            var corners = new List<Point>();
            var drawn = CleanLines.Draw(pts, 0.6, 40, closed, corners);
            var shapes = CleanLines.Fit(pts, 0.6, 40, closed);
            var fitCorners = new List<Point>();
            Assert.Equal(CleanLines.Rasterize(shapes, closed, fitCorners), drawn);
            Assert.Equal(fitCorners, corners);
            AssertStepwise(drawn, closed);
            if (closed) Assert.Equal(shapes[^1].End, shapes[0].Start);
            if (!closed)
            {
                Assert.Equal(pts[0], shapes[0].Start);
                Assert.Equal(pts[^1], shapes[^1].End);
            }
            for (int k = 1; k < shapes.Count; k++) Assert.Equal(shapes[k - 1].End, shapes[k].Start);
        }
    }

    [Fact]
    public void CleanLinesFitsPixelStaircaseAsOneEvenStraight()
    {
        var pts = Enumerable.Range(0, 21).Select(x => (x + 0.5, x / 2 + 0.5)).ToList();
        var shapes = CleanLines.Fit(pts, 0.6, 40, false);
        Assert.Single(shapes);
        Assert.Equal(LineShapeKind.Straight, shapes[0].Kind);
        var path = CleanLines.Rasterize(shapes, false);
        AssertStepwise(path, false);
        var runs = path.GroupBy(p => p.Y).Select(g => g.Count()).ToList();
        Assert.All(runs.Skip(1).SkipLast(1), r => Assert.Equal(2, r));
    }

    [Fact]
    public void CleanLinesFitsJitteredCurvesWithFewArcs()
    {
        var random = new Random(7);
        double Jitter() => (random.NextDouble() * 2 - 1) * 0.45;
        var quarter = Enumerable.Range(0, 61).Select(i =>
        {
            double t = Math.PI / 2 * i / 60;
            return (X: 5 + 16 * Math.Cos(t) + Jitter(), Y: 5 + 10 * Math.Sin(t) + Jitter());
        }).ToList();
        var shapes = CleanLines.Fit(quarter, 0.6, 40, false);
        Assert.InRange(shapes.Count, 1, 3);
        Assert.All(shapes, s => Assert.Equal(LineShapeKind.Arc, s.Kind));
        AssertStepwise(CleanLines.Rasterize(shapes, false), false);

        var ring = Enumerable.Range(0, 80).Select(i =>
        {
            double t = 2 * Math.PI * i / 80;
            return (X: 20 + 10 * Math.Cos(t) + Jitter(), Y: 20 + 10 * Math.Sin(t) + Jitter());
        }).ToList();
        var loop = CleanLines.Fit(ring, 0.6, 40, true);
        Assert.InRange(loop.Count, 2, 4);
        Assert.All(loop, s => Assert.Equal(LineShapeKind.Arc, s.Kind));
        Assert.InRange(Math.Abs(Math.Sqrt(Math.Pow(loop[0].Start.X - 20, 2) + Math.Pow(loop[0].Start.Y - 20, 2)) - 10), 0, 0.3);
        var cells = CleanLines.Rasterize(loop, true);
        AssertStepwise(cells, true);
        var set = cells.ToHashSet();
        Assert.Equal(cells.Count, set.Count);
        Assert.All(cells, p => Assert.Equal(2, Enumerable.Range(0, 9).Count(k => k != 4 && set.Contains(new Point(p.X + k % 3 - 1, p.Y + k / 3 - 1)))));
    }

    [Fact]
    public void CleanLinesReportsArcToStraightCorner()
    {
        var pts = new List<(double X, double Y)>();
        for (int i = 0; i <= 40; i++)
        {
            double t = Math.PI + Math.PI / 2 * i / 40;
            pts.Add((10 + 8 * Math.Cos(t), 10 + 8 * Math.Sin(t)));
        }
        double turn = 70 * Math.PI / 180;
        for (int i = 1; i <= 16; i++) pts.Add((10 + 0.5 * i * Math.Cos(turn), 2 + 0.5 * i * Math.Sin(turn)));
        var shapes = CleanLines.Fit(pts, 0.6, 40, false);
        Assert.Contains(shapes, s => s.Kind == LineShapeKind.Arc);
        Assert.Equal(LineShapeKind.Straight, shapes[^1].Kind);
        var corners = new List<Point>();
        var path = CleanLines.Rasterize(shapes, false, corners);
        Assert.Contains(corners, p => Math.Abs(p.X - 10) <= 1 && Math.Abs(p.Y - 2) <= 1);
        Assert.All(corners, p => Assert.Contains(p, path));
    }

    [Fact]
    public void CleanVeeKeepsBothArmsAndBottomCorner()
    {
        var img = new PixelImage(40, 22);
        img.FillRect(new Rectangle(0, 0, 40, 22), Fill);
        for (int i = 0; i <= 6; i++)
        {
            img[26 + i / 2, 7 + i] = Ink;
            img[33 - i / 2, 7 + i] = Ink;
        }
        var o = new LineArtOptions();
        var trace = LineArt.Trace(img, o);
        Assert.NotEmpty(trace.Strokes);
        Assert.DoesNotContain(trace.Strokes, s => s.Closed);
        var result = LineArt.Apply(img, trace, o);
        for (int y = 7; y <= 12; y++)
            Assert.Equal(2, Enumerable.Range(0, 40).Count(x => result[x, y] == Ink));
        Assert.True(Connected(Cells(result, Ink)));
        Assert.Contains(Enumerable.Range(28, 4), x => result[x, 13] == Ink);
        Assert.DoesNotContain(Enumerable.Range(0, 40), x => result[x, 14] == Ink);
    }

    [Fact]
    public void CleanLinesKeepsSharpPixelCorner()
    {
        var pts = Enumerable.Range(0, 10).Select(x => (x + 0.5, 2.5)).Concat(Enumerable.Range(1, 9).Select(y => (9.5, y + 2.5))).ToList();
        var corners = new List<Point>();
        var path = CleanLines.Draw(pts, 0.6, 40, false, corners);
        Assert.Contains(new Point(9, 2), path);
        Assert.Contains(new Point(9, 2), corners);
        AssertStepwise(path, false);
    }

    [Fact]
    public void CleanLinesFitsArcWithMonotoneRuns()
    {
        // 반지름 12 사분원의 픽셀 중심(1px 계단).
        var cells = new List<Point>();
        for (int i = 0; i <= 200; i++)
        {
            double a = Math.PI / 2 * i / 200;
            var p = new Point((int)Math.Round(12 * Math.Cos(a)), (int)Math.Round(12 * Math.Sin(a)));
            if (cells.Count == 0 || cells[^1] != p) cells.Add(p);
        }
        var pts = cells.Select(p => (p.X + 0.5, p.Y + 0.5)).ToList();
        var shapes = CleanLines.Fit(pts, 0.6, 40, false);
        Assert.Contains(shapes, s => s.Kind == LineShapeKind.Arc);
        var path = CleanLines.Rasterize(shapes, false);
        AssertStepwise(path, false);
        // 가로 계단은 아래(y 큼)로 갈수록, 세로 계단은 오른쪽으로 갈수록 길어지기만 한다(끝 줄은 잘린 계단이라 뺌).
        var rows = path.GroupBy(p => p.Y).OrderBy(g => g.Key).Select(g => g.Count()).ToList();
        var columns = path.GroupBy(p => p.X).OrderBy(g => g.Key).Select(g => g.Count()).ToList();
        foreach (var runs in new[] { rows, columns })
            for (int i = 1; i + 2 < runs.Count; i++)
                Assert.True(runs[i] <= runs[i + 1], string.Join(",", runs));
        var sample = shapes[0].Sample(0.5);
        Assert.Equal(shapes[0].Start, sample[0]);
        Assert.Equal(shapes[0].End, sample[^1]);
    }
}
