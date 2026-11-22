using System.Drawing;

namespace Chickxel.Core.Pixelate;

/// <summary>선 조각 모양: 직선 또는 원호.</summary>
public enum LineShapeKind { Straight, Arc }

/// <summary>
/// 선 조각 하나의 3점 표현(칸 좌표 — 칸 (x, y)는 [x, x+1)×[y, y+1)): 직선은 <paramref name="Mid"/> = 가운데,
/// 원호는 <paramref name="Start"/>·<paramref name="Mid"/>·<paramref name="End"/>를 지나는 원의 Start→End 호(Mid 쪽).
/// </summary>
public sealed record LineShape(LineShapeKind Kind, (double X, double Y) Start, (double X, double Y) Mid, (double X, double Y) End)
{
    /// <summary>모양을 따라 <paramref name="step"/> 간격으로 찍은 점(처음·끝 포함) — 화면에 겹쳐 그리기용.</summary>
    public List<(double X, double Y)> Sample(double step)
    {
        step = double.IsFinite(step) ? Math.Max(1e-3, step) : 1;
        var points = new List<(double X, double Y)> { Start };
        if (Kind == LineShapeKind.Arc && CleanLines.Circle(Start, Mid, End) is { } arc)
        {
            int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(arc.Sweep) * arc.Radius / step));
            for (int n = 1; n < count; n++) points.Add(arc.At(n / (double)count));
        }
        else if (Kind == LineShapeKind.Arc)
        {
            AddLine(points, Start, Mid, step);
            points.Add(Mid);
            AddLine(points, Mid, End, step);
        }
        else AddLine(points, Start, End, step);
        if (points[^1] != End || points.Count == 1) points.Add(End);
        return points;
    }

    /// <summary>a→b 사이 점들(a·b는 빼고).</summary>
    private static void AddLine(List<(double X, double Y)> points, (double X, double Y) a, (double X, double Y) b, double step)
    {
        double length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        int count = Math.Max(1, (int)Math.Ceiling(length / step));
        for (int n = 1; n < count; n++)
            points.Add((a.X + (b.X - a.X) * n / count, a.Y + (b.Y - a.Y) * n / count));
    }
}

/// <summary>
/// 깨끗한 픽셀 선: 점 목록(뼈대·붓 자취)을 모서리에서 나눠 직선·원호 조각으로 맞추고(<see cref="Fit"/>),
/// 조각들을 끊기지 않는 1px 픽셀 퍼펙트 선으로 칠한다(<see cref="Rasterize"/> — 직선은 고른 계단, 원호는 계단 길이가 한쪽으로만 바뀜, 날카로운 모서리 칸은 남김).
/// </summary>
public static class CleanLines
{
    /// <summary>표본 점 사이 최소 거리²(칸).</summary>
    private const double MinSpacing2 = 0.64;
    /// <summary>원호를 칠할 때 원 위 표본 간격(칸).</summary>
    private const double ArcStep = 0.35;
    /// <summary>모서리를 잴 때 앞뒤로 보는 길이(칸) — 1px 계단의 들쭉날쭉함을 모서리로 보지 않는다.</summary>
    internal const double CornerReach = 2;
    /// <summary>이어진 두 조각의 방향이 이 각도 이상 꺾이면 모서리 칸으로 남긴다.</summary>
    internal const double CornerTurn = 30 * Math.PI / 180;
    /// <summary>원호 계단 길이를 한쪽으로 줄 세울 때 칸이 옮겨 가도 되는 최대 거리(칸).</summary>
    private const int MonotoneShift = 2;
    /// <summary>원호 하나로 맞춰 볼 최대 점 수.</summary>
    private const int ArcSpan = 160;

    internal readonly record struct Arc(double X, double Y, double Radius, double Start, double Sweep)
    {
        /// <summary>호 위 점(t 0 = 시작, 1 = 끝).</summary>
        public (double X, double Y) At(double t)
        {
            double angle = Start + Sweep * t;
            return (X + Radius * Math.Cos(angle), Y + Radius * Math.Sin(angle));
        }
    }

    private readonly record struct Piece(LineShape Shape, Arc Arc, bool IsArc);

    /// <summary>
    /// 점 목록을 직선·원호 조각으로 맞춘다: 점을 0.8칸 이상 간격으로 추리고, [1, 2, 1]로 고른 점에서 앞뒤 2칸을 이은 방향이 <paramref name="cornerAngle"/>도
    /// 이상 꺾이는 자리(그 둘레에서 가장 꺾인 점)에서 나눈 뒤 모서리 사이를 고르게 하고, 앞에서부터 더 멀리 가는 쪽 — 원호(두 끝을 지나는 최소제곱 원,
    /// 반지름 오차 ≤ <paramref name="straightTolerance"/>) 또는 직선(벗어남 ≤ <paramref name="straightTolerance"/>, 같으면 직선) — 으로 잇고,
    /// 이웃한 두 조각이 하나로 맞으면 합친다. 열린 선의 첫 점·끝 점은 그대로 남는다(갈림점에서 만난 선이 그대로 만남).
    /// <paramref name="closed"/>면 고리로 보고(끝 점이 첫 점과 같아도 됨) 가장 꺾인 모서리에서, 모서리가 없으면 고른 점 중 가장 위 점에서 시작한다.
    /// </summary>
    public static IReadOnlyList<LineShape> Fit(IReadOnlyList<(double X, double Y)> pts, double straightTolerance, double cornerAngle, bool closed)
        => FitPieces(pts, straightTolerance, cornerAngle, closed, robust: true).ConvertAll(p => p.Shape);

    /// <summary><see cref="Fit"/> 후 <see cref="Rasterize"/>.</summary>
    public static List<Point> Draw(IReadOnlyList<(double X, double Y)> pts, double straightTolerance, double cornerAngle, bool closed,
        List<Point>? corners = null)
        => Rasterize(Fit(pts, straightTolerance, cornerAngle, closed), closed, corners);

    /// <summary>
    /// 조각들을 이어 8방향으로 끊기지 않는 1px 선 칸으로 칠한다: 직선은 칸 사이 브레즌햄(고른 계단), 원호는 원 위 표본을 이은 뒤 이어진 원호마다
    /// 계단 길이를 한쪽으로만 바뀌게 줄 세우고(칸이 최대 2칸까지 옮겨 갈 때만), 마지막에 L자 겹 칸을 뺀다 — <see cref="CornerTurn"/> 이상 꺾인
    /// 조각 이음 칸(모서리)은 빼지 않고 <paramref name="corners"/>에 담는다. <paramref name="closed"/>면 끝을 첫 칸에 잇고 첫 칸을 되풀이하지 않는다.
    /// </summary>
    public static List<Point> Rasterize(IReadOnlyList<LineShape> shapes, bool closed, List<Point>? corners = null)
    {
        var output = new List<Point>();
        if (shapes.Count == 0) return output;
        var keep = new HashSet<Point>();
        var curves = new List<(int From, int To)>();
        bool previousArc = false;
        for (int k = 0; k < shapes.Count; k++)
        {
            var shape = shapes[k];
            var arc = shape.Kind == LineShapeKind.Arc ? Circle(shape.Start, shape.Mid, shape.End) : null;
            var piece = arc is { } a ? RasterArc(shape.Start, a, shape.End)
                : shape.Kind == LineShapeKind.Arc ? RasterPath([shape.Start, shape.Mid, shape.End])
                : RasterPath([shape.Start, shape.End]);
            int joint = Math.Max(0, output.Count - 1);
            Append(output, piece);
            bool corner = k > 0 && IsCorner(shapes[k - 1], shape);
            if (corner) keep.Add(output[joint]);
            if (arc is not null)
            {
                if (previousArc && !corner) curves[^1] = (curves[^1].From, output.Count - 1);
                else curves.Add((joint, output.Count - 1));
            }
            previousArc = arc is not null;
        }
        if (closed && output.Count > 1)
        {
            if (shapes.Count > 1 && IsCorner(shapes[^1], shapes[0])) keep.Add(output[0]);
            if (output[^1] != output[0]) AppendLine(output, output[0]);
        }
        for (int c = curves.Count - 1; c >= 0; c--)
        {
            var (from, to) = curves[c];
            if (to - from < 3) continue;
            var part = Monotone(PixelPerfect(output.GetRange(from, to - from + 1), keep, false));
            output.RemoveRange(from, to - from + 1);
            output.InsertRange(from, part);
        }
        var result = PixelPerfect(output, keep, closed);
        if (corners is not null)
            foreach (var p in result)
                if (keep.Contains(p)) corners.Add(p);
        return result;
    }

    /// <summary>
    /// 벡터로 다시 그리기(<see cref="VectorOutline"/>)의 선: 이웃 점끼리 꺾임만 보고 나눈 직선·원호를 L자 정리 없이 잇는다
    /// (그쪽은 칸 좌표로 줄인 뼈대라 촘촘한 1px 계단이 없고, 정리는 부르는 쪽이 한다).
    /// </summary>
    internal static List<Point> VectorPath(IReadOnlyList<(double X, double Y)> source, double tolerance, double cornerAngle)
    {
        var output = new List<Point>();
        foreach (var piece in FitPieces(source, tolerance, cornerAngle, false, robust: false))
            Append(output, piece.IsArc ? RasterArc(piece.Shape.Start, piece.Arc, piece.Shape.End)
                : RasterPath([piece.Shape.Start, piece.Shape.End]));
        return output;
    }

    private static List<Piece> FitPieces(IReadOnlyList<(double X, double Y)> source, double tolerance, double cornerAngle, bool closed, bool robust)
    {
        var pieces = new List<Piece>();
        if (source.Count == 0) return pieces;
        double limit = PixelateRanges.VectorCornerAngle.Clamp(cornerAngle) * Math.PI / 180;
        if (closed) source = RotateLoop(source, limit, out _);
        var pts = Thin(source);
        if (pts.Count < 3)
        {
            if (pts.Count == 1) pieces.Add(StraightPiece(pts[0], pts[0]));
            for (int i = 1; i < pts.Count; i++) pieces.Add(StraightPiece(pts[i - 1], pts[i]));
            return pieces;
        }

        double tol = Math.Max(0.25, tolerance);
        if (robust)
        {
            var corners = Corners(pts, limit);
            Smooth(pts, corners);
            for (int k = 1; k < corners.Count; k++) FitSegment(pts, corners[k - 1], corners[k], tol, pieces);
            return pieces;
        }
        var breaks = NeighbourCorners(pts, limit);
        for (int k = 1; k < breaks.Count; k++)
            for (int from = breaks[k - 1], end = breaks[k]; from < end;)
            {
                Arc best = default;
                int arcEnd = -1;
                for (int to = from + 4; to <= Math.Min(end, from + 64); to++)
                    if (TryArc(pts, from, to, tol, out var candidate))
                    {
                        best = candidate;
                        arcEnd = to;
                    }
                if (arcEnd > from)
                {
                    pieces.Add(new Piece(new LineShape(LineShapeKind.Arc, pts[from], best.At(0.5), pts[arcEnd]), best, true));
                    from = arcEnd;
                }
                else
                {
                    int lineEnd = Math.Min(end, from + 1);
                    while (lineEnd < end && LineError(pts, from, lineEnd + 1) <= tol) lineEnd++;
                    pieces.Add(StraightPiece(pts[from], pts[lineEnd]));
                    from = lineEnd;
                }
            }
        return pieces;
    }

    /// <summary>
    /// 모서리 사이 점 <paramref name="start"/>~<paramref name="end"/>를 조각으로: 앞에서부터 가장 길게 맞는 원호(<see cref="ArcSpan"/>점까지) 또는 직선을 고른 뒤,
    /// 이웃한 두 조각이 직선 하나나 원호 하나로 맞으면 합친다(흔들린 점 때문에 잘게 나뉜 곡선을 한 조각으로).
    /// </summary>
    private static void FitSegment(List<(double X, double Y)> pts, int start, int end, double tol, List<Piece> pieces)
    {
        var ranges = new List<(int From, int To, bool IsArc, Arc Arc)>();
        for (int from = start; from < end;)
        {
            Arc best = default;
            int arcEnd = -1;
            for (int to = from + 4; to <= Math.Min(end, from + ArcSpan); to++)
                if (TryArcThrough(pts, from, to, tol, out var candidate))
                {
                    best = candidate;
                    arcEnd = to;
                }
            int lineEnd = Math.Min(end, from + 1);
            while (lineEnd < end && LineError(pts, from, lineEnd + 1) <= tol) lineEnd++;
            if (arcEnd > lineEnd)
            {
                ranges.Add((from, arcEnd, true, best));
                from = arcEnd;
                continue;
            }
            ranges.Add((from, lineEnd, false, default));
            from = lineEnd;
        }
        for (int i = 0; i + 1 < ranges.Count;)
        {
            int a = ranges[i].From, c = ranges[i + 1].To;
            if (LineError(pts, a, c) <= tol) ranges[i] = (a, c, false, default);
            else if (TryArcThrough(pts, a, c, tol, out var arc)) ranges[i] = (a, c, true, arc);
            else { i++; continue; }
            ranges.RemoveAt(i + 1);
            if (i > 0) i--;
        }
        foreach (var r in ranges)
            pieces.Add(r.IsArc
                ? new Piece(new LineShape(LineShapeKind.Arc, pts[r.From], r.Arc.At(0.5), pts[r.To]), r.Arc, true)
                : StraightPiece(pts[r.From], pts[r.To]));
    }

    /// <summary>
    /// 두 끝 점을 지나는 원 중 사이 점들에 가장 잘 맞는 것(대수 최소제곱)으로 원호를 맞춘다: 모든 점의 반지름 오차 ≤ <paramref name="tolerance"/>,
    /// 한 방향으로만 돌고 0.4~π+0.2 라디안, 반지름 1.2~10000.
    /// </summary>
    private static bool TryArcThrough(List<(double X, double Y)> pts, int from, int to, double tolerance, out Arc arc)
    {
        arc = default;
        if (to - from < 4 || LineError(pts, from, to) < 0.35) return false;
        var s = pts[from];
        double ex = pts[to].X - s.X, ey = pts[to].Y - s.Y, length = Math.Sqrt(ex * ex + ey * ey);
        if (length < 1e-9) return false;
        double mx = ex / 2, my = ey / 2, nx = -ey / length, ny = ex / length, ab = 0, bb = 0;
        for (int i = from + 1; i < to; i++)
        {
            double px = pts[i].X - s.X, py = pts[i].Y - s.Y;
            double a = px * px + py * py - 2 * (mx * px + my * py), b = 2 * (nx * px + ny * py);
            ab += a * b;
            bb += b * b;
        }
        if (bb < 1e-12) return false;
        double t = ab / bb, cx = s.X + mx + t * nx, cy = s.Y + my + t * ny;
        double radius = Math.Sqrt((s.X - cx) * (s.X - cx) + (s.Y - cy) * (s.Y - cy));
        if (radius < 1.2 || radius > 10000) return false;
        double begin = Math.Atan2(s.Y - cy, s.X - cx), previous = begin, sweep = 0;
        for (int i = from + 1; i <= to; i++)
        {
            double x = pts[i].X - cx, y = pts[i].Y - cy;
            if (Math.Abs(Math.Sqrt(x * x + y * y) - radius) > tolerance) return false;
            double angle = Math.Atan2(y, x);
            double step = Math.Atan2(Math.Sin(angle - previous), Math.Cos(angle - previous));
            if (sweep != 0 && step * sweep < -0.01) return false;
            sweep += step;
            if (Math.Abs(sweep) > Math.PI + 0.2) return false;
            previous = angle;
        }
        if (Math.Abs(sweep) < 0.4) return false;
        arc = new Arc(cx, cy, radius, begin, sweep);
        return true;
    }

    private static Piece StraightPiece((double X, double Y) a, (double X, double Y) b)
        => new(new LineShape(LineShapeKind.Straight, a, ((a.X + b.X) / 2, (a.Y + b.Y) / 2), b), default, false);

    /// <summary>이웃 점끼리 이은 방향이 꺾인 자리(벡터로 다시 그리기 — 칸 좌표로 줄인 뼈대).</summary>
    private static List<int> NeighbourCorners(List<(double X, double Y)> pts, double limit)
    {
        var breaks = new List<int> { 0 };
        for (int i = 1; i < pts.Count - 1; i++)
        {
            var a = pts[i - 1]; var b = pts[i]; var c = pts[i + 1];
            double ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - b.X, vy = c.Y - b.Y;
            if (ux * ux + uy * uy < 0.25 || vx * vx + vy * vy < 0.25) continue;
            if (i - breaks[^1] >= 2 && pts.Count - i >= 2
                && Math.Abs(Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy)) >= limit)
                breaks.Add(i);
        }
        breaks.Add(pts.Count - 1);
        return breaks;
    }

    /// <summary>
    /// 고른 점(<see cref="Smooth"/> — 1px 튀어나옴·계단이 모서리로 잡히지 않게)에서 앞뒤 <see cref="CornerReach"/>칸 떨어진 점을 이은 방향이
    /// <paramref name="limit"/> 이상 꺾이고 그 둘레에서 가장 꺾인 자리. 옆 점도 거의 같게 꺾이면(V자의 납작한 바닥 두 칸) 두 점 모두에서 나눠
    /// 두 팔이 바닥 칸에서 곧게 끝나게 한다.
    /// </summary>
    internal static List<int> Corners(List<(double X, double Y)> source, double limit)
    {
        int n = source.Count;
        var pts = new List<(double X, double Y)>(source);
        Smooth(pts, [0, n - 1]);
        var s = new double[n];
        for (int i = 1; i < n; i++) s[i] = s[i - 1] + Distance(pts[i - 1], pts[i]);
        var turn = new double[n];
        for (int i = 1; i < n - 1; i++)
        {
            int a = i, c = i;
            while (a > 0 && s[i] - s[a] < CornerReach) a--;
            while (c < n - 1 && s[c] - s[i] < CornerReach) c++;
            if (s[i] - s[a] < CornerReach * 0.75 || s[c] - s[i] < CornerReach * 0.75) continue;
            turn[i] = Turn(pts[a], pts[i], pts[c]);
        }
        var breaks = new List<int> { 0 };
        for (int i = 1; i < n - 1; i++)
        {
            if (turn[i] < limit || i - breaks[^1] < 2 || n - 1 - i < 2) continue;
            bool peak = true;
            for (int j = i - 1; j >= 0 && s[i] - s[j] <= CornerReach && peak; j--) peak = turn[j] <= turn[i];
            for (int j = i + 1; j < n && s[j] - s[i] <= CornerReach && peak; j++) peak = turn[j] < turn[i];
            if (!peak) continue;
            if (turn[i - 1] >= 0.9 * turn[i] && i - 1 - breaks[^1] >= 2) breaks.Add(i - 1);
            breaks.Add(i);
            if (turn[i + 1] >= 0.9 * turn[i] && n - 2 - i >= 2) breaks.Add(++i);
        }
        breaks.Add(n - 1);
        return breaks;
    }

    /// <summary>점을 <see cref="MinSpacing2"/> 이상 간격으로 추린다(첫 점·끝 점은 남김).</summary>
    internal static List<(double X, double Y)> Thin(IReadOnlyList<(double X, double Y)> source)
    {
        var pts = new List<(double X, double Y)>();
        if (source.Count == 0) return pts;
        pts.Add(source[0]);
        for (int i = 1; i < source.Count - 1; i++)
        {
            double dx = source[i].X - pts[^1].X, dy = source[i].Y - pts[^1].Y;
            if (dx * dx + dy * dy >= MinSpacing2) pts.Add(source[i]);
        }
        if (source.Count > 1 && pts[^1] != source[^1]) pts.Add(source[^1]);
        return pts;
    }

    /// <summary>모서리 사이 점을 [1, 2, 1] 평균으로 두 번 고른다(모서리·끝 점은 그대로) — 1px 계단이 직선·원 위에 가깝게.</summary>
    internal static void Smooth(List<(double X, double Y)> pts, List<int> breaks)
    {
        var copy = new (double X, double Y)[pts.Count];
        for (int pass = 0; pass < 2; pass++)
        {
            pts.CopyTo(copy);
            for (int k = 1; k < breaks.Count; k++)
                for (int i = breaks[k - 1] + 1; i < breaks[k]; i++)
                    pts[i] = ((copy[i - 1].X + 2 * copy[i].X + copy[i + 1].X) / 4, (copy[i - 1].Y + 2 * copy[i].Y + copy[i + 1].Y) / 4);
        }
    }

    /// <summary>
    /// 고리 점들을 가장 꺾인 모서리(없으면 가장 위·왼쪽 점 — 원호의 단조 구간 경계)에서 시작해 그 점으로 끝나게 돌린다.
    /// <paramref name="corner"/>는 시작점이 모서리인지.
    /// </summary>
    internal static List<(double X, double Y)> RotateLoop(IReadOnlyList<(double X, double Y)> source, double limit, out bool corner)
    {
        corner = false;
        var loop = source.ToList();
        if (loop.Count > 1 && loop[0] == loop[^1]) loop.RemoveAt(loop.Count - 1);
        int n = loop.Count;
        if (n < 3)
        {
            loop.Add(loop[0]);
            return loop;
        }
        // 꺾임은 고리째 [1, 2, 1]로 두 번 고른 점에서 잰다.
        var smooth = loop.ToArray();
        var copy = new (double X, double Y)[n];
        for (int pass = 0; pass < 2; pass++)
        {
            smooth.CopyTo(copy, 0);
            for (int i = 0; i < n; i++)
            {
                var p = copy[(i - 1 + n) % n]; var q = copy[(i + 1) % n];
                smooth[i] = ((p.X + 2 * copy[i].X + q.X) / 4, (p.Y + 2 * copy[i].Y + q.Y) / 4);
            }
        }
        int start = -1;
        double sharpest = limit;
        for (int i = 0; i < n; i++)
        {
            int a = i, c = i;
            double back = 0, ahead = 0;
            for (int step = 0; step < n && back < CornerReach; step++)
            {
                int prev = (a - 1 + n) % n;
                back += Distance(smooth[prev], smooth[a]);
                a = prev;
            }
            for (int step = 0; step < n && ahead < CornerReach; step++)
            {
                int next = (c + 1) % n;
                ahead += Distance(smooth[c], smooth[next]);
                c = next;
            }
            double t = Turn(smooth[a], smooth[i], smooth[c]);
            if (t >= sharpest) { sharpest = t; start = i; }
        }
        corner = start >= 0;
        // 모서리가 없는 고리는 이음매도 고른 점에서 — 흔들린 첫 점에 매이지 않게.
        var basis = loop;
        if (start < 0)
        {
            basis = [.. smooth];
            start = 0;
            for (int i = 1; i < n; i++)
                if (basis[i].Y < basis[start].Y || (basis[i].Y == basis[start].Y && basis[i].X < basis[start].X)) start = i;
        }
        var rotated = new List<(double X, double Y)>(n + 1);
        for (int i = 0; i <= n; i++) rotated.Add(basis[(start + i) % n]);
        return rotated;
    }

    internal static double Distance((double X, double Y) a, (double X, double Y) b)
        => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    /// <summary>a→b와 b→c 방향 사이 각(라디안, 0~π).</summary>
    internal static double Turn((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        double ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - b.X, vy = c.Y - b.Y;
        if (ux * ux + uy * uy < 1e-12 || vx * vx + vy * vy < 1e-12) return 0;
        return Math.Abs(Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy));
    }

    /// <summary>세 점을 지나는 원의 a→c 호(b 쪽). 세 점이 한 줄이면 null.</summary>
    internal static Arc? Circle((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y, acx = c.X - a.X, acy = c.Y - a.Y;
        double d = 2 * (abx * acy - aby * acx);
        if (Math.Abs(d) < 1e-9) return null;
        double ab = abx * abx + aby * aby, ac = acx * acx + acy * acy;
        double cx = a.X + (ab * acy - ac * aby) / d;
        double cy = a.Y + (ac * abx - ab * acx) / d;
        double radius = Math.Sqrt((a.X - cx) * (a.X - cx) + (a.Y - cy) * (a.Y - cy));
        if (!double.IsFinite(radius) || radius > 1e6) return null;
        double start = Math.Atan2(a.Y - cy, a.X - cx), mid = Math.Atan2(b.Y - cy, b.X - cx), end = Math.Atan2(c.Y - cy, c.X - cx);
        static double Wrap(double v) => Math.Atan2(Math.Sin(v), Math.Cos(v));
        return new Arc(cx, cy, radius, start, Wrap(mid - start) + Wrap(end - mid));
    }

    /// <summary>조각이 시작(<paramref name="atEnd"/> false) 또는 끝에서 나아가는 방향. 길이가 없으면 null.</summary>
    private static (double X, double Y)? Tangent(LineShape shape, bool atEnd)
    {
        (double X, double Y) v;
        if (shape.Kind == LineShapeKind.Arc && Circle(shape.Start, shape.Mid, shape.End) is { } arc)
        {
            double angle = arc.Start + (atEnd ? arc.Sweep : 0), sign = Math.Sign(arc.Sweep);
            v = (-Math.Sin(angle) * sign, Math.Cos(angle) * sign);
        }
        else if (shape.Kind == LineShapeKind.Arc)
            v = atEnd ? (shape.End.X - shape.Mid.X, shape.End.Y - shape.Mid.Y) : (shape.Mid.X - shape.Start.X, shape.Mid.Y - shape.Start.Y);
        else v = (shape.End.X - shape.Start.X, shape.End.Y - shape.Start.Y);
        return v.X * v.X + v.Y * v.Y < 1e-12 ? null : v;
    }

    private static bool IsCorner(LineShape previous, LineShape next)
    {
        if (Tangent(previous, true) is not { } u || Tangent(next, false) is not { } v) return false;
        return Math.Abs(Math.Atan2(u.X * v.Y - u.Y * v.X, u.X * v.X + u.Y * v.Y)) >= CornerTurn;
    }

    private static List<Point> RasterArc((double X, double Y) from, Arc arc, (double X, double Y) to)
    {
        int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(arc.Sweep) * arc.Radius / ArcStep));
        var points = new List<(double X, double Y)>(count + 1) { from };
        for (int n = 1; n < count; n++)
        {
            double angle = arc.Start + arc.Sweep * n / count;
            points.Add((arc.X + arc.Radius * Math.Cos(angle), arc.Y + arc.Radius * Math.Sin(angle)));
        }
        points.Add(to);
        return RasterPath(points);
    }

    /// <summary>점들이 든 칸을 브레즌햄 선으로 잇는다.</summary>
    internal static List<Point> RasterPath(IReadOnlyList<(double X, double Y)> pts)
    {
        var path = new List<Point>();
        foreach (var (x, y) in pts) AppendLine(path, new Point((int)Math.Floor(x), (int)Math.Floor(y)));
        return path;
    }

    internal static void AppendLine(List<Point> path, Point to)
    {
        if (path.Count == 0) { path.Add(to); return; }
        int x = path[^1].X, y = path[^1].Y;
        int dx = Math.Abs(to.X - x), sx = x < to.X ? 1 : -1;
        int dy = -Math.Abs(to.Y - y), sy = y < to.Y ? 1 : -1;
        int error = dx + dy;
        while (x != to.X || y != to.Y)
        {
            int twice = 2 * error;
            if (twice >= dy) { error += dy; x += sx; }
            if (twice <= dx) { error += dx; y += sy; }
            path.Add(new Point(x, y));
        }
    }

    internal static void Append(List<Point> output, List<Point> next)
    {
        if (output.Count > 0 && next.Count > 0 && output[^1] != next[0])
            AppendLine(output, next[0]);
        for (int i = output.Count > 0 && next.Count > 0 && output[^1] == next[0] ? 1 : 0; i < next.Count; i++)
            output.Add(next[i]);
    }

    internal static double LineError(IReadOnlyList<(double X, double Y)> pts, int from, int to)
    {
        var a = pts[from]; var b = pts[to];
        double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-8) return double.PositiveInfinity;
        double worst = 0;
        for (int i = from + 1; i < to; i++)
            worst = Math.Max(worst, Math.Abs(dx * (pts[i].Y - a.Y) - dy * (pts[i].X - a.X)) / length);
        return worst;
    }

    private static bool TryArc(IReadOnlyList<(double X, double Y)> pts, int from, int to, double tolerance, out Arc arc)
    {
        arc = default;
        if (to - from < 4 || LineError(pts, from, to) < 0.35) return false;
        var a = pts[from]; var b = pts[(from + to) / 2]; var c = pts[to];
        double abx = b.X - a.X, aby = b.Y - a.Y, acx = c.X - a.X, acy = c.Y - a.Y;
        double d = 2 * (abx * acy - aby * acx);
        if (Math.Abs(d) < 1e-6) return false;
        double ab = abx * abx + aby * aby, ac = acx * acx + acy * acy;
        double cx = a.X + (ab * acy - ac * aby) / d;
        double cy = a.Y + (ac * abx - ab * acx) / d;
        double radius = Math.Sqrt((a.X - cx) * (a.X - cx) + (a.Y - cy) * (a.Y - cy));
        if (radius < 1.2 || radius > 10000) return false;
        double start = Math.Atan2(a.Y - cy, a.X - cx), previous = start, sweep = 0;
        for (int i = from + 1; i <= to; i++)
        {
            double x = pts[i].X - cx, y = pts[i].Y - cy;
            if (Math.Abs(Math.Sqrt(x * x + y * y) - radius) > tolerance) return false;
            double angle = Math.Atan2(y, x);
            double step = Math.Atan2(Math.Sin(angle - previous), Math.Cos(angle - previous));
            if (sweep != 0 && step * sweep < -0.01) return false;
            sweep += step;
            if (Math.Abs(sweep) > Math.PI + 0.2) return false;
            previous = angle;
        }
        if (Math.Abs(sweep) < 0.4) return false;
        arc = new Arc(cx, cy, radius, start, sweep);
        return true;
    }

    private static bool IsL(Point a, Point b, Point c)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1 && Math.Abs(b.X - c.X) + Math.Abs(b.Y - c.Y) == 1
            && Math.Abs(a.X - c.X) == 1 && Math.Abs(a.Y - c.Y) == 1;

    /// <summary>
    /// 연속한 같은 칸을 합치고 L자 겹 칸(앞뒤 칸과 상하좌우로 붙고 앞뒤 칸끼리 대각선)을 뺀다 — 앞에서 이미 지난 칸과 <paramref name="keep"/> 칸은 남김
    /// (<see cref="Chickxel.Core.Edit.PixelPerfect"/>와 같은 규칙). <paramref name="closed"/>면 마지막 칸이 첫 칸과 같을 때 빼고 이음매도 본다.
    /// </summary>
    internal static List<Point> PixelPerfect(List<Point> path, HashSet<Point> keep, bool closed)
    {
        var result = new List<Point>(path.Count);
        var seen = new Dictionary<Point, int>();
        foreach (var p in path)
        {
            if (result.Count > 0 && result[^1] == p) continue;
            result.Add(p);
            seen[p] = seen.GetValueOrDefault(p) + 1;
            if (result.Count >= 3 && IsL(result[^3], result[^2], result[^1]) && !keep.Contains(result[^2]) && seen[result[^2]] == 1)
            {
                seen[result[^2]]--;
                result.RemoveAt(result.Count - 2);
            }
        }
        if (!closed) return result;
        if (result.Count > 1 && result[^1] == result[0])
        {
            seen[result[0]]--;
            result.RemoveAt(result.Count - 1);
        }
        if (result.Count >= 4 && IsL(result[^2], result[^1], result[0]) && !keep.Contains(result[^1]) && seen[result[^1]] == 1)
            result.RemoveAt(result.Count - 1);
        if (result.Count >= 4 && IsL(result[^1], result[0], result[1]) && !keep.Contains(result[0]) && seen[result[0]] == 1)
            result.RemoveAt(0);
        return result;
    }

    /// <summary>
    /// 원호 칸 줄의 계단 길이를 한쪽으로만 바뀌게 줄 세운다: 꼭짓점에서 한 칸만 튀어나온 자리(대각선으로 나갔다 바로 대각선으로 돌아옴)는 곧게 펴고,
    /// x·y 방향이 뒤집히지 않는 구간마다, 그 안에서 가로 이동과 세로 이동이 섞이지 않는 덩어리마다 대각선 이동 사이 곧은 이동 수
    /// (첫·끝 것은 잘린 계단이라 뺌)를 오름·내림 중 칸이 덜 옮겨 가는 쪽으로 정렬한다(끝 칸은 그대로).
    /// </summary>
    internal static List<Point> Monotone(List<Point> cells)
    {
        int n = cells.Count - 1;
        if (n < 3) return cells;
        var moves = new (int X, int Y)[n];
        for (int i = 0; i < n; i++)
        {
            moves[i] = (cells[i + 1].X - cells[i].X, cells[i + 1].Y - cells[i].Y);
            if (Math.Max(Math.Abs(moves[i].X), Math.Abs(moves[i].Y)) != 1) return cells;
        }
        for (int i = 0; i + 1 < n; i++)
        {
            var (a, b) = (moves[i], moves[i + 1]);
            if (a.X == 0 || a.Y == 0 || b.X == 0 || b.Y == 0) continue;
            if (a.X == -b.X && a.Y == b.Y) moves[i] = moves[i + 1] = (0, a.Y);
            else if (a.Y == -b.Y && a.X == b.X) moves[i] = moves[i + 1] = (a.X, 0);
        }
        for (int s = 0; s < n;)
        {
            int sx = 0, sy = 0, e = s;
            for (; e < n; e++)
            {
                var m = moves[e];
                if ((m.X != 0 && sx != 0 && m.X != sx) || (m.Y != 0 && sy != 0 && m.Y != sy)) break;
                if (m.X != 0) sx = m.X;
                if (m.Y != 0) sy = m.Y;
            }
            for (int c = s; c < e;)
            {
                bool? horizontal = null;
                int d = c;
                for (; d < e; d++)
                {
                    var m = moves[d];
                    if (m.X != 0 && m.Y != 0) continue;
                    if (horizontal is null) horizontal = m.Y == 0;
                    else if (horizontal != (m.Y == 0)) break;
                }
                SortRuns(moves, c, d);
                c = d;
            }
            s = e;
        }
        var result = new List<Point>(cells.Count) { cells[0] };
        foreach (var m in moves) result.Add(new Point(result[^1].X + m.X, result[^1].Y + m.Y));
        return result;
    }

    private static void SortRuns((int X, int Y)[] moves, int from, int to)
    {
        var runs = new List<int> { 0 };
        (int X, int Y) diagonal = default, straight = default;
        for (int i = from; i < to; i++)
            if (moves[i].X != 0 && moves[i].Y != 0)
            {
                diagonal = moves[i];
                runs.Add(0);
            }
            else
            {
                straight = moves[i];
                runs[^1]++;
            }
        int k = runs.Count - 1;
        if (k < 3) return;
        var raw = runs.GetRange(1, k - 1);
        var ascending = raw.Order().ToList();
        var descending = raw.OrderDescending().ToList();
        int Shift(List<int> sorted)
        {
            int a = 0, b = 0, worst = 0;
            for (int j = 0; j < raw.Count; j++)
            {
                a += raw[j];
                b += sorted[j];
                worst = Math.Max(worst, Math.Abs(a - b));
            }
            return worst;
        }
        int up = Shift(ascending), down = Shift(descending);
        int shift = Math.Min(up, down);
        if (shift == 0 || shift > MonotoneShift) return;
        var best = up <= down ? ascending : descending;
        for (int j = 0; j < best.Count; j++) runs[j + 1] = best[j];
        int at = from;
        for (int j = 0; j <= k; j++)
        {
            if (j > 0) moves[at++] = diagonal;
            for (int r = 0; r < runs[j]; r++) moves[at++] = straight;
        }
    }
}
