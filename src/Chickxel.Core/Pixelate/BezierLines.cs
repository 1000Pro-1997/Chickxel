using System.Drawing;
using V = (double X, double Y);

namespace Chickxel.Core.Pixelate;

/// <summary>
/// 베지어 경로의 고정점 하나(칸 좌표 — 칸 (x, y)는 [x, x+1)×[y, y+1)): 자리 <paramref name="Point"/>, 들어오는 손잡이 <paramref name="In"/>,
/// 나가는 손잡이 <paramref name="Out"/>(모두 절대 위치 — 손잡이가 자리와 같으면 접힌 손잡이). <paramref name="Corner"/>면 두 손잡이가 따로 움직이는 모서리,
/// 아니면 두 손잡이가 한 줄에 놓이는 매끈한 점.
/// </summary>
public sealed record BezierNode((double X, double Y) Point, (double X, double Y) In, (double X, double Y) Out, bool Corner)
{
    /// <summary>손잡이를 접은 점.</summary>
    public static BezierNode At((double X, double Y) p, bool corner = true) => new(p, p, p, corner);

    /// <summary>자리와 두 손잡이를 함께 옮긴 점.</summary>
    public BezierNode Offset(double dx, double dy)
        => this with { Point = (Point.X + dx, Point.Y + dy), In = (In.X + dx, In.Y + dy), Out = (Out.X + dx, Out.Y + dy) };

    /// <summary>들어오는 손잡이가 접혔는지.</summary>
    public bool InRetracted => BezierLines.Near(In, Point);

    /// <summary>나가는 손잡이가 접혔는지.</summary>
    public bool OutRetracted => BezierLines.Near(Out, Point);
}

/// <summary>
/// 포토샵 펜 같은 3차 베지어 경로: 점 목록(붓 자취)을 모서리에서 나눈 뒤 슈나이더의 최소제곱 맞춤으로 적은 고정점에 맞추고(<see cref="Fit"/>),
/// 경로를 끊기지 않는 1px 픽셀 퍼펙트 선으로 칠한다(<see cref="Rasterize"/> — 곧은 조각은 고른 계단, 휜 조각은 변곡점 사이마다 계단 길이가 한쪽으로만 바뀜).
/// 조각 i는 점 i(Point·Out)에서 점 i+1(In·Point)로 가고, 닫힌 경로는 마지막 점에서 첫 점으로 한 조각 더 있다. 점 하나를 고쳐도 그 점 양옆 두 조각만 바뀐다.
/// </summary>
public static class BezierLines
{
    /// <summary>휜 조각을 칠할 때 곡선 위 표본 간격(칸, 이보다 촘촘).</summary>
    private const double SampleStep = 0.2;
    /// <summary>두 손잡이가 양 끝을 잇는 선에서 이만큼(칸) 안이면 곧은 조각으로 칠한다.</summary>
    private const double StraightSnap = 0.05;
    /// <summary>맞춤에서 뉴턴 다시 매기기를 되풀이하는 최대 횟수.</summary>
    private const int Reparameterize = 6;
    /// <summary>한 조각의 손잡이 길이가 양 끝 거리의 이 배를 넘으면 맞추지 않은 것으로 본다(고리·꼬임 막기).</summary>
    private const double MaxHandleRatio = 2;
    /// <summary>매끈한 점의 두 손잡이 방향이 이 각도(라디안)보다 어긋나면 모서리로 표시한다.</summary>
    private const double SmoothSlack = 2 * Math.PI / 180;
    /// <summary>두 직선이 만나는 모서리 점을 직선끼리 만나는 자리로 옮길 최대 거리(칸).</summary>
    private const double CornerSnap = 1.5;
    /// <summary>직선 사이에 낀 이보다 짧은(칸) 직선 조각은 모서리 하나로 합친다.</summary>
    private const double ShortLine = 1.5;

    internal static bool Near(V a, V b) => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;

    /// <summary>점 <paramref name="nodes"/>개 경로의 조각 수(닫힌 경로는 점 2개부터 점 수만큼).</summary>
    public static int SegmentCount(int nodes, bool closed) => nodes < 2 ? 0 : closed ? nodes : nodes - 1;

    /// <summary>조각 <paramref name="segment"/>의 네 조절점(시작, 시작 손잡이, 끝 손잡이, 끝).</summary>
    public static (V P0, V P1, V P2, V P3) Segment(IReadOnlyList<BezierNode> nodes, int segment)
    {
        var a = nodes[segment];
        var b = nodes[(segment + 1) % nodes.Count];
        return (a.Point, a.Out, b.In, b.Point);
    }

    /// <summary>조각 <paramref name="segment"/>의 <paramref name="t"/>(0~1) 자리.</summary>
    public static V PointAt(IReadOnlyList<BezierNode> nodes, int segment, double t) => Cubic.Of(nodes, segment).At(t);

    // ───────────── 맞추기 ─────────────

    /// <summary>
    /// 점 목록을 베지어 경로로 맞춘다: 점을 0.8칸 이상 간격으로 추리고, 곡선 정리(<see cref="CleanLines"/>)와 같은 규칙으로 <paramref name="cornerAngle"/>도 이상
    /// 꺾인 자리에서 나눈 뒤 모서리 사이를 고르게 하고, 앞에서부터 가장 멀리 가는 조각 — 직선(벗어남 ≤ <paramref name="tolerance"/>, 같으면 직선) 또는
    /// 3차 베지어(슈나이더: 호 길이 매기기, 양 끝 접선 고정 최소제곱, 뉴턴 다시 매기기, 모든 점의 오차 ≤ <paramref name="tolerance"/>) — 으로 잇는다.
    /// 모서리 사이의 이음점은 앞뒤 조각이 같은 접선을 써서 손잡이가 한 줄이고(매끈), 모서리 점은 손잡이가 따로다. 직선 조각의 손잡이는 양 끝을 잇는 선의 1/3·2/3 자리.
    /// <paramref name="closed"/>면 고리로 보고(끝 점이 첫 점과 같아도 됨) 가장 꺾인 모서리에서, 모서리가 없으면 고른 점 중 가장 위 점에서 시작한다(마지막 조각이 첫 점으로 돌아옴).
    /// </summary>
    public static List<BezierNode> Fit(IReadOnlyList<(double X, double Y)> pts, double tolerance, double cornerAngle, bool closed)
    {
        var nodes = new List<BezierNode>();
        if (pts.Count == 0) return nodes;
        double limit = PixelateRanges.VectorCornerAngle.Clamp(cornerAngle) * Math.PI / 180;
        bool seamCorner = false;
        var source = pts;
        if (closed) source = CleanLines.RotateLoop(pts, limit, out seamCorner);
        var p = CleanLines.Thin(source);
        if (closed && p.Count < 4) closed = false;
        if (p.Count < 3)
        {
            nodes.Add(BezierNode.At(p[0]));
            for (int i = 1; i < p.Count; i++)
            {
                var (a, b) = (p[i - 1], p[i]);
                nodes[^1] = nodes[^1] with { Out = Lerp(a, b, 1 / 3.0) };
                nodes.Add(new BezierNode(b, Lerp(a, b, 2 / 3.0), b, true));
            }
            return nodes;
        }
        var breaks = CleanLines.Corners(p, limit);
        CleanLines.Smooth(p, breaks);
        var fitter = new Fitter(p, Math.Max(0.1, tolerance), closed && !seamCorner && breaks.Count == 2);
        var segments = new List<FitSegment>();
        for (int k = 1; k < breaks.Count; k++) fitter.FitPiece(breaks[k - 1], breaks[k], segments);
        SnapCorners(segments, p, closed);

        var corner = new bool[segments.Count + 1];
        for (int k = 0; k < segments.Count; k++)
        {
            if (segments[k].From == breaks[0] || breaks.Contains(segments[k].From)) corner[k] = true;
            if (k > 0 && segments[k - 1].Line && segments[k].Line) corner[k] = true;
        }
        // 열린 경로의 양 끝은 한쪽 손잡이뿐이라 매끈한 점으로 둔다.
        if (!closed)
        {
            corner[0] = false;
            corner[segments.Count] = false;
        }
        else corner[0] = seamCorner || breaks.Count > 2;

        int count = closed ? segments.Count : segments.Count + 1;
        for (int k = 0; k < count; k++)
        {
            var point = k < segments.Count ? segments[k].C.P0 : segments[^1].C.P3;
            var outHandle = k < segments.Count ? segments[k].C.P1 : point;
            var inHandle = k > 0 ? segments[k - 1].C.P2 : closed ? segments[^1].C.P2 : point;
            nodes.Add(new BezierNode(point, inHandle, outHandle, corner[k]));
        }
        // 접선이 어긋난 매끈한 점(직선 뒤 곡선을 다시 맞추지 못한 자리 등)은 모서리로 표시한다.
        for (int k = 0; k < nodes.Count; k++)
            if (!nodes[k].Corner && !Collinear(nodes[k])) nodes[k] = nodes[k] with { Corner = true };
        return nodes;
    }

    /// <summary>두 손잡이가 모두 펼쳐졌고 한 줄에서 어긋났는지의 반대(하나라도 접혔으면 한 줄로 봄).</summary>
    private static bool Collinear(BezierNode n)
    {
        if (n.InRetracted || n.OutRetracted) return true;
        V u = Sub(n.Point, n.In), v = Sub(n.Out, n.Point);
        return Math.Abs(Math.Atan2(Cross(u, v), Dot(u, v))) <= SmoothSlack;
    }

    /// <summary>
    /// 직선 둘이 만나는 모서리: 점을 두 직선(조각의 점들에 최소제곱으로 맞춘 선)이 만나는 자리로 옮기고(<see cref="CornerSnap"/>칸 안일 때),
    /// 직선 사이에 낀 <see cref="ShortLine"/>칸보다 짧은 직선 조각(V자 바닥의 두 모서리)은 양옆 직선이 만나는 점 하나로 합친다.
    /// </summary>
    private static void SnapCorners(List<FitSegment> segs, List<V> p, bool closed)
    {
        for (int k = 0; k < segs.Count && segs.Count > (closed ? 3 : 2);)
        {
            int prev = closed ? (k - 1 + segs.Count) % segs.Count : k - 1, next = closed ? (k + 1) % segs.Count : k + 1;
            if (prev < 0 || next >= segs.Count || !segs[k].Line || !segs[prev].Line || !segs[next].Line
                || CleanLines.Distance(segs[k].C.P0, segs[k].C.P3) >= ShortLine
                || Intersect(LineOf(p, segs[prev]), LineOf(p, segs[next])) is not { } x
                || CleanLines.Distance(x, Lerp(segs[k].C.P0, segs[k].C.P3, 0.5)) > CornerSnap)
            {
                k++;
                continue;
            }
            segs[prev] = WithEnds(segs[prev], segs[prev].C.P0, x);
            segs[next] = WithEnds(segs[next], x, segs[next].C.P3);
            segs.RemoveAt(k);
        }
        for (int k = closed ? 0 : 1; k < segs.Count; k++)
        {
            int prev = (k - 1 + segs.Count) % segs.Count;
            if (prev == k || !segs[k].Line || !segs[prev].Line) continue;
            if (Intersect(LineOf(p, segs[prev]), LineOf(p, segs[k])) is not { } x || CleanLines.Distance(x, segs[k].C.P0) > CornerSnap) continue;
            segs[prev] = WithEnds(segs[prev], segs[prev].C.P0, x);
            segs[k] = WithEnds(segs[k], x, segs[k].C.P3);
        }
    }

    /// <summary>양 끝을 바꾼 직선 조각(손잡이는 1/3·2/3 자리).</summary>
    private static FitSegment WithEnds(FitSegment s, V a, V b) => s with { C = new Cubic(a, Lerp(a, b, 1 / 3.0), Lerp(a, b, 2 / 3.0), b) };

    /// <summary>조각의 점들에 맞춘 직선(무게중심, 단위 방향 — 수직 거리 최소제곱).</summary>
    private static (V Center, V Direction) LineOf(List<V> p, FitSegment s)
    {
        double cx = 0, cy = 0;
        int n = s.To - s.From + 1;
        for (int i = s.From; i <= s.To; i++) { cx += p[i].X; cy += p[i].Y; }
        cx /= n;
        cy /= n;
        double sxx = 0, sxy = 0, syy = 0;
        for (int i = s.From; i <= s.To; i++)
        {
            double dx = p[i].X - cx, dy = p[i].Y - cy;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }
        double angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        return ((cx, cy), (Math.Cos(angle), Math.Sin(angle)));
    }

    /// <summary>두 직선이 만나는 점(15°보다 덜 꺾이면 null).</summary>
    private static V? Intersect((V Center, V Direction) a, (V Center, V Direction) b)
    {
        double denominator = Cross(a.Direction, b.Direction);
        if (Math.Abs(denominator) < Math.Sin(15 * Math.PI / 180)) return null;
        double t = Cross(Sub(b.Center, a.Center), b.Direction) / denominator;
        return Add(a.Center, Mul(a.Direction, t));
    }

    private readonly record struct FitSegment(Cubic C, int From, int To, bool Line);

    /// <summary>모서리 사이 점들을 조각으로 맞추는 도우미(점·누적 호 길이·허용 오차²).</summary>
    private sealed class Fitter
    {
        private readonly List<V> _p;
        private readonly double[] _s;
        private readonly double _tol, _tol2;
        /// <summary>모서리 없는 고리: 이음매(첫 점 = 끝 점)의 접선을 고리째 잰다.</summary>
        private readonly bool _loop;

        public Fitter(List<V> p, double tol, bool loop)
        {
            _p = p;
            _tol = tol;
            _tol2 = tol * tol;
            _loop = loop;
            _s = new double[p.Count];
            for (int i = 1; i < p.Count; i++) _s[i] = _s[i - 1] + CleanLines.Distance(p[i - 1], p[i]);
        }

        /// <summary>점 <paramref name="a"/>~<paramref name="b"/>를 앞에서부터 가장 멀리 가는 직선·베지어 조각으로 잇는다.</summary>
        public void FitPiece(int a, int b, List<FitSegment> output)
        {
            int first = output.Count;
            V? forced = null;
            for (int i = a; i < b;)
            {
                int lineEnd = i + 1;
                while (lineEnd < b && CleanLines.LineError(_p, i, lineEnd + 1) <= _tol) lineEnd++;
                var t1 = forced ?? Tangent(i, a, b, true);
                // 가장 멀리 맞는 끝을 반씩 좁혀 찾는다(점이 늘면 오차도 대개 늘어남).
                int lo = i + 1, hi = b;
                Cubic best = Heuristic(i, lo, t1, Neg(Tangent(lo, a, b, false)));
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (TryFit(i, mid, t1, Neg(Tangent(mid, a, b, false)), out var c)) { lo = mid; best = c; }
                    else hi = mid - 1;
                }
                if (lo > lineEnd)
                {
                    output.Add(new FitSegment(best, i, lo, false));
                    forced = null;
                    i = lo;
                    continue;
                }
                var line = new Cubic(_p[i], Lerp(_p[i], _p[lineEnd], 1 / 3.0), Lerp(_p[i], _p[lineEnd], 2 / 3.0), _p[lineEnd]);
                // 곡선 뒤 직선: 곡선 끝 접선을 직선 방향으로 다시 맞춰 매끈하게 잇는다(안 맞으면 모서리로 남음).
                if (output.Count > first && !output[^1].Line && output[^1].To == i && Normalize(Sub(_p[lineEnd], _p[i])) is { } dir)
                {
                    var prev = output[^1];
                    if (TryFit(prev.From, prev.To, Normalize(Sub(prev.C.P1, prev.C.P0)) ?? Tangent(prev.From, a, b, true), Neg(dir), out var refit))
                        output[^1] = prev with { C = refit };
                }
                output.Add(new FitSegment(line, i, lineEnd, true));
                forced = Normalize(Sub(_p[lineEnd], _p[i]));
                i = lineEnd;
            }
        }

        /// <summary>점 <paramref name="first"/>~<paramref name="last"/>를 양 끝 접선을 고정한 베지어 하나로 맞춘다. 모든 점의 오차가 허용치 안이면 true.</summary>
        private bool TryFit(int first, int last, V t1, V t2, out Cubic c)
        {
            c = default;
            if (last - first < 2)
            {
                c = Heuristic(first, last, t1, t2);
                return true;
            }
            var u = ChordLength(first, last);
            c = Generate(first, last, u, t1, t2);
            double error = MaxError(c, first, last, u);
            for (int n = 0; n < Reparameterize && error > _tol2; n++)
            {
                var u2 = Reparam(c, first, last, u);
                var c2 = Generate(first, last, u2, t1, t2);
                double e2 = MaxError(c2, first, last, u2);
                if (e2 >= error) break;
                (c, u, error) = (c2, u2, e2);
            }
            return error <= _tol2;
        }

        /// <summary>점 <paramref name="first"/>~<paramref name="last"/>의 호 길이 비율 매개값(0~1).</summary>
        public double[] ChordLength(int first, int last)
        {
            var u = new double[last - first + 1];
            double total = _s[last] - _s[first];
            for (int i = first; i <= last; i++) u[i - first] = total > 1e-12 ? (_s[i] - _s[first]) / total : (i - first) / (double)(last - first);
            return u;
        }

        /// <summary>손잡이 길이를 양 끝 거리의 1/3로 둔 베지어.</summary>
        private Cubic Heuristic(int first, int last, V t1, V t2)
        {
            V a = _p[first], b = _p[last];
            double d = CleanLines.Distance(a, b) / 3;
            return new Cubic(a, Add(a, Mul(t1, d)), Add(b, Mul(t2, d)), b);
        }

        /// <summary>슈나이더: 접선 방향을 고정하고 손잡이 길이 둘을 최소제곱으로 푼다(못 풀거나 음수면 1/3 길이).</summary>
        public Cubic Generate(int first, int last, double[] u, V t1, V t2)
        {
            V p0 = _p[first], p3 = _p[last];
            double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
            for (int i = 0; i < u.Length; i++)
            {
                double t = u[i], mt = 1 - t;
                double b0 = mt * mt * mt, b1 = 3 * t * mt * mt, b2 = 3 * t * t * mt, b3 = t * t * t;
                V a0 = Mul(t1, b1), a1 = Mul(t2, b2);
                c00 += Dot(a0, a0);
                c01 += Dot(a0, a1);
                c11 += Dot(a1, a1);
                var d = _p[first + i];
                V tmp = (d.X - (p0.X * (b0 + b1) + p3.X * (b2 + b3)), d.Y - (p0.Y * (b0 + b1) + p3.Y * (b2 + b3)));
                x0 += Dot(a0, tmp);
                x1 += Dot(a1, tmp);
            }
            double det = c00 * c11 - c01 * c01;
            double chord = CleanLines.Distance(p0, p3);
            double alphaL = Math.Abs(det) < 1e-12 ? 0 : (x0 * c11 - x1 * c01) / det;
            double alphaR = Math.Abs(det) < 1e-12 ? 0 : (c00 * x1 - c01 * x0) / det;
            double epsilon = 1e-6 * chord;
            if (alphaL < epsilon || alphaR < epsilon) alphaL = alphaR = chord / 3;
            return new Cubic(p0, Add(p0, Mul(t1, alphaL)), Add(p3, Mul(t2, alphaR)), p3);
        }

        /// <summary>매긴 값 자리와 점 사이 거리² 중 가장 큰 것(손잡이가 너무 길면 무한대).</summary>
        public double MaxError(Cubic c, int first, int last, double[] u)
        {
            double chord = CleanLines.Distance(c.P0, c.P3);
            if (CleanLines.Distance(c.P0, c.P1) > MaxHandleRatio * chord + 1 || CleanLines.Distance(c.P3, c.P2) > MaxHandleRatio * chord + 1)
                return double.PositiveInfinity;
            double worst = 0;
            for (int i = first + 1; i < last; i++)
            {
                var q = c.At(u[i - first]);
                double dx = q.X - _p[i].X, dy = q.Y - _p[i].Y;
                worst = Math.Max(worst, dx * dx + dy * dy);
            }
            return worst;
        }

        /// <summary>뉴턴–랩슨으로 점마다 곡선 위 가장 가까운 매개값에 다가간다.</summary>
        public double[] Reparam(Cubic c, int first, int last, double[] u)
        {
            var result = new double[u.Length];
            result[0] = 0;
            result[^1] = 1;
            for (int i = 1; i < u.Length - 1; i++)
            {
                double t = u[i];
                V q = c.At(t), d1 = c.D1(t), d2 = c.D2(t), diff = Sub(q, _p[first + i]);
                double numerator = Dot(diff, d1), denominator = Dot(d1, d1) + Dot(diff, d2);
                result[i] = Math.Abs(denominator) < 1e-12 ? t : Math.Clamp(t - numerator / denominator, 0, 1);
            }
            return result;
        }

        /// <summary>
        /// 점 <paramref name="j"/>의 나아가는 방향(단위 벡터): 가운데 점은 앞뒤 2칸을 잇고, 조각 끝은 앞(뒤) h·2h칸 점으로 2차까지 맞춘 방향.
        /// <paramref name="start"/>면 조각 시작에서 앞으로, 아니면 끝에서 앞으로(<see cref="Neg"/>로 뒤집어 씀).
        /// </summary>
        private V Tangent(int j, int a, int b, bool start)
        {
            double sj = _s[j], sa = _s[a], sb = _s[b], reach = CleanLines.CornerReach;
            bool seam = _loop && (j == a || j == b);
            if (seam)
            {
                double h = Math.Min(reach, (sb - sa) / 4);
                return Normalize(Sub(PointAt(sa + h), PointAt(sb - h))) ?? (1, 0);
            }
            if (j == a || j == b)
            {
                double h = Math.Min(reach, (sb - sa) / 3);
                double dir = j == a ? 1 : -1;
                V p0 = _p[j], p1 = PointAt(sj + dir * h), p2 = PointAt(sj + dir * 2 * h);
                V v = (4 * p1.X - p2.X - 3 * p0.X, 4 * p1.Y - p2.Y - 3 * p0.Y);
                var t = Normalize(v) ?? Normalize(Sub(p1, p0)) ?? (1, 0);
                return j == a ? t : Neg(t);
            }
            double reachHere = Math.Min(reach, Math.Min(sj - sa, sb - sj));
            return Normalize(Sub(PointAt(sj + reachHere), PointAt(sj - reachHere))) ?? Normalize(Sub(_p[j + 1], _p[j - 1])) ?? (1, 0);
        }

        /// <summary>누적 호 길이 <paramref name="d"/>의 자리(고리면 감아서).</summary>
        private V PointAt(double d)
        {
            double total = _s[^1];
            if (_loop && total > 0) d = ((d % total) + total) % total;
            d = Math.Clamp(d, 0, total);
            int hi = Array.BinarySearch(_s, d);
            if (hi >= 0) return _p[hi];
            hi = ~hi;
            if (hi >= _s.Length) return _p[^1];
            int lo = hi - 1;
            double span = _s[hi] - _s[lo];
            return span < 1e-12 ? _p[lo] : Lerp(_p[lo], _p[hi], (d - _s[lo]) / span);
        }
    }

    // ───────────── 칠하기 ─────────────

    /// <summary>
    /// 경로를 8방향으로 끊기지 않는 1px 선 칸으로 칠한다. 곧은 조각(두 손잡이가 양 끝을 잇는 선에서 0.05칸 안)은 칸 사이 브레즌햄(고른 계단),
    /// 휜 조각은 0.2칸보다 촘촘한 표본이 든 칸을 잇고(빈틈은 브레즌햄으로) 변곡점마다 나눈 덩어리마다 L자 겹 칸을 뺀 뒤 계단 길이를 한쪽으로만 바뀌게 줄 세운다
    /// (한 칸 튀어나옴은 곧게 폄 — 조각마다 따로라 점 하나를 고쳐도 다른 조각의 칸은 그대로). 마지막에 L자 겹 칸을 뺀다 — 30° 이상 꺾인 조각 이음 칸(모서리)은
    /// 빼지 않고 <paramref name="corners"/>에 담는다. <paramref name="closed"/>면 마지막 점에서 첫 점으로 한 조각 더 잇고 첫 칸을 되풀이하지 않는다.
    /// </summary>
    public static List<Point> Rasterize(IReadOnlyList<BezierNode> nodes, bool closed, List<Point>? corners = null)
    {
        var output = new List<Point>();
        if (nodes.Count == 0) return output;
        int count = SegmentCount(nodes.Count, closed);
        if (count == 0)
        {
            var p = nodes[0].Point;
            output.Add(new Point((int)Math.Floor(p.X), (int)Math.Floor(p.Y)));
            corners?.Add(output[0]);
            return output;
        }
        var keep = new HashSet<Point>();
        var curves = new List<(int From, int To)>();
        for (int k = 0; k < count; k++)
        {
            var c = Cubic.Of(nodes, k);
            int joint = Math.Max(0, output.Count - 1);
            if (c.IsStraight) CleanLines.Append(output, CleanLines.RasterPath([c.P0, c.P3]));
            else
            {
                double from = 0;
                foreach (double to in c.Inflections().Append(1))
                {
                    int start = Math.Max(0, output.Count - 1);
                    CleanLines.Append(output, CleanLines.RasterPath(c.Sample(from, to, SampleStep)));
                    curves.Add((start, output.Count - 1));
                    from = to;
                }
            }
            if ((k > 0 || closed) && IsCorner(Cubic.Of(nodes, (k - 1 + count) % count), c)) keep.Add(output[joint]);
        }
        if (closed && output.Count > 1 && output[^1] != output[0]) CleanLines.AppendLine(output, output[0]);
        for (int r = curves.Count - 1; r >= 0; r--)
        {
            var (from, to) = curves[r];
            if (to - from < 3) continue;
            var part = CleanLines.Monotone(CleanLines.PixelPerfect(output.GetRange(from, to - from + 1), keep, false));
            output.RemoveRange(from, to - from + 1);
            output.InsertRange(from, part);
        }
        var result = CleanLines.PixelPerfect(output, keep, closed);
        if (corners is not null)
            foreach (var p in result)
                if (keep.Contains(p)) corners.Add(p);
        return result;
    }

    private static bool IsCorner(Cubic previous, Cubic next)
    {
        if (previous.EndTangent() is not { } u || next.StartTangent() is not { } v) return false;
        return Math.Abs(Math.Atan2(Cross(u, v), Dot(u, v))) >= CleanLines.CornerTurn;
    }

    // ───────────── 고치기 ─────────────

    /// <summary>
    /// 조각 <paramref name="segment"/>를 <paramref name="t"/>(0~1)에서 드 카스텔조로 나눠 점을 하나 더한다(모양은 그대로, 새 점은 매끈한 점).
    /// 새 목록과 새 점의 번호를 돌려준다.
    /// </summary>
    public static (List<BezierNode> Nodes, int Index) Split(IReadOnlyList<BezierNode> nodes, int segment, double t, bool closed)
    {
        var list = nodes.ToList();
        int count = SegmentCount(nodes.Count, closed);
        if (segment < 0 || segment >= count) return (list, -1);
        t = Math.Clamp(t, 0, 1);
        var c = Cubic.Of(nodes, segment);
        V p01 = Lerp(c.P0, c.P1, t), p12 = Lerp(c.P1, c.P2, t), p23 = Lerp(c.P2, c.P3, t);
        V p012 = Lerp(p01, p12, t), p123 = Lerp(p12, p23, t), mid = Lerp(p012, p123, t);
        int next = (segment + 1) % nodes.Count;
        list[segment] = list[segment] with { Out = p01 };
        list[next] = list[next] with { In = p23 };
        var node = new BezierNode(mid, p012, p123, false);
        list.Insert(segment + 1, node);
        return (list, segment + 1);
    }

    /// <summary>
    /// 점 <paramref name="index"/>를 뺀다. 가운데 점이면 양옆 두 조각을 그 모양에 가장 가까운 베지어 하나로 합친다(이웃 점의 자리와 손잡이 방향은 그대로,
    /// 손잡이 길이만 맞춤 — 두 조각이 모두 곧으면 곧은 조각). 열린 경로의 끝 점이면 새 끝 점의 바깥 손잡이를 접는다.
    /// </summary>
    public static List<BezierNode> Remove(IReadOnlyList<BezierNode> nodes, int index, bool closed)
    {
        var list = nodes.ToList();
        if (index < 0 || index >= list.Count) return list;
        int n = list.Count;
        if (!closed && (index == 0 || index == n - 1) || n <= 2)
        {
            list.RemoveAt(index);
            if (list.Count > 0 && !closed)
            {
                if (index == 0) list[0] = list[0] with { In = list[0].Point };
                else list[^1] = list[^1] with { Out = list[^1].Point };
            }
            return list;
        }
        int prev = (index - 1 + n) % n, next = (index + 1) % n;
        var a = Cubic.Of(nodes, prev);
        var b = Cubic.Of(nodes, index);
        var start = nodes[prev];
        var end = nodes[next];
        (V Out, V In) handles;
        if (a.IsStraight && b.IsStraight)
            handles = (start.OutRetracted ? start.Point : Lerp(start.Point, end.Point, 1 / 3.0),
                end.InRetracted ? end.Point : Lerp(start.Point, end.Point, 2 / 3.0));
        else handles = Merge(a, b);
        list[prev] = start with { Out = handles.Out };
        list[next] = end with { In = handles.In };
        list.RemoveAt(index);
        return list;
    }

    /// <summary>이어진 두 조각을 표본으로 찍어 바깥 두 접선을 고정한 베지어 하나로 맞춘 손잡이 둘.</summary>
    private static (V Out, V In) Merge(Cubic a, Cubic b)
    {
        var pts = new List<V>();
        pts.AddRange(a.Sample(0, 1, 0.5));
        var second = b.Sample(0, 1, 0.5);
        pts.AddRange(second.Skip(1));
        var t1 = a.StartTangent() ?? Normalize(Sub(a.P3, a.P0)) ?? (1, 0);
        var t2 = b.EndTangent() is { } e ? Neg(e) : Normalize(Sub(b.P0, b.P3)) ?? (-1, 0);
        var fitter = new Fitter(pts, 0.1, false);
        var u = fitter.ChordLength(0, pts.Count - 1);
        Cubic best = default;
        double bestError = double.PositiveInfinity;
        for (int n = 0; n <= 10; n++)
        {
            var c = fitter.Generate(0, pts.Count - 1, u, t1, t2);
            double error = fitter.MaxError(c, 0, pts.Count - 1, u);
            if (error < bestError) (best, bestError) = (c, error);
            u = fitter.Reparam(c, 0, pts.Count - 1, u);
        }
        if (!double.IsFinite(bestError))
        {
            double d = CleanLines.Distance(a.P0, b.P3) / 3;
            return (Add(a.P0, Mul(t1, d)), Add(b.P3, Mul(t2, d)));
        }
        return (best.P1, best.P2);
    }

    /// <summary>
    /// 점 <paramref name="index"/>를 모서리(손잡이 접음) ↔ 매끈한 점(손잡이를 이웃 점 방향으로 — 앞 이웃에서 뒤 이웃으로 가는 방향, 길이는 각 이웃까지 거리의 1/3)으로 바꾼다.
    /// </summary>
    public static List<BezierNode> ToggleCorner(IReadOnlyList<BezierNode> nodes, int index, bool closed)
    {
        var list = nodes.ToList();
        if (index < 0 || index >= list.Count) return list;
        var node = list[index];
        bool hasHandles = !node.InRetracted || !node.OutRetracted;
        if (node.Corner && !hasHandles || !node.Corner && !hasHandles && list.Count > 1)
        {
            int n = list.Count;
            V? before = index > 0 || closed && n > 2 ? list[(index - 1 + n) % n].Point : null;
            V? after = index < n - 1 || closed && n > 2 ? list[(index + 1) % n].Point : null;
            if (before is null && after is null) return list;
            var dir = Normalize(Sub(after ?? node.Point, before ?? node.Point)) ?? (1, 0);
            double back = before is { } b ? CleanLines.Distance(b, node.Point) / 3 : 0;
            double ahead = after is { } a ? CleanLines.Distance(node.Point, a) / 3 : 0;
            list[index] = new BezierNode(node.Point, Add(node.Point, Mul(dir, -back)), Add(node.Point, Mul(dir, ahead)), false);
        }
        else list[index] = BezierNode.At(node.Point);
        return list;
    }

    /// <summary>
    /// 손잡이 하나(<paramref name="outHandle"/>면 나가는 쪽)를 <paramref name="to"/>로 옮긴 점. 매끈한 점이면 반대 손잡이를 제 길이 그대로 한 줄로 돌리고,
    /// <paramref name="breakCorner"/>면 모서리로 바꿔 반대 손잡이는 그대로 둔다.
    /// </summary>
    public static BezierNode DragHandle(BezierNode node, bool outHandle, (double X, double Y) to, bool breakCorner)
    {
        if (breakCorner) node = node with { Corner = true };
        var moved = outHandle ? node with { Out = to } : node with { In = to };
        if (moved.Corner) return moved;
        var other = outHandle ? node.In : node.Out;
        double length = CleanLines.Distance(other, node.Point);
        if (length < 1e-9 || Normalize(Sub(to, node.Point)) is not { } dir) return moved;
        var opposite = Add(node.Point, Mul(dir, -length));
        return outHandle ? moved with { In = opposite } : moved with { Out = opposite };
    }

    /// <summary>
    /// 경로에서 <paramref name="point"/>에 가장 가까운 자리: 조각 번호, 매개값, 거리. 조각이 없으면(점 하나) 조각 −1과 첫 점까지 거리.
    /// </summary>
    public static (int Segment, double T, double Distance) Nearest(IReadOnlyList<BezierNode> nodes, bool closed, (double X, double Y) point)
    {
        int count = SegmentCount(nodes.Count, closed);
        if (count == 0) return (-1, 0, nodes.Count == 0 ? double.PositiveInfinity : CleanLines.Distance(nodes[0].Point, point));
        (int, double, double) best = (-1, 0, double.PositiveInfinity);
        for (int k = 0; k < count; k++)
        {
            var c = Cubic.Of(nodes, k);
            int steps = Math.Max(8, (int)Math.Ceiling(c.MaxSpeed));
            double bestT = 0, bestD = double.PositiveInfinity;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps, d = Dist2(c.At(t), point);
                if (d < bestD) (bestT, bestD) = (t, d);
            }
            // 둘레를 반씩 좁혀 다듬는다.
            for (double h = 1.0 / steps; h > 1e-6; h /= 2)
                for (int side = -1; side <= 1; side += 2)
                {
                    double t = bestT + side * h;
                    if (t < 0 || t > 1) continue;
                    double d = Dist2(c.At(t), point);
                    if (d < bestD) (bestT, bestD) = (t, d);
                }
            double distance = Math.Sqrt(bestD);
            if (distance < best.Item3) best = (k, bestT, distance);
        }
        return best;
    }

    // ───────────── 베지어 조각 ─────────────

    private readonly record struct Cubic(V P0, V P1, V P2, V P3)
    {
        public static Cubic Of(IReadOnlyList<BezierNode> nodes, int segment)
        {
            var (p0, p1, p2, p3) = Segment(nodes, segment);
            return new Cubic(p0, p1, p2, p3);
        }

        public V At(double t)
        {
            double mt = 1 - t, a = mt * mt * mt, b = 3 * mt * mt * t, c = 3 * mt * t * t, d = t * t * t;
            return (a * P0.X + b * P1.X + c * P2.X + d * P3.X, a * P0.Y + b * P1.Y + c * P2.Y + d * P3.Y);
        }

        public V D1(double t)
        {
            double mt = 1 - t, a = 3 * mt * mt, b = 6 * mt * t, c = 3 * t * t;
            return (a * (P1.X - P0.X) + b * (P2.X - P1.X) + c * (P3.X - P2.X), a * (P1.Y - P0.Y) + b * (P2.Y - P1.Y) + c * (P3.Y - P2.Y));
        }

        public V D2(double t)
        {
            double mt = 1 - t;
            return (6 * (mt * (P2.X - 2 * P1.X + P0.X) + t * (P3.X - 2 * P2.X + P1.X)), 6 * (mt * (P2.Y - 2 * P1.Y + P0.Y) + t * (P3.Y - 2 * P2.Y + P1.Y)));
        }

        /// <summary>|B'(t)|의 위 한계(조절점 다리 중 가장 긴 것 × 3).</summary>
        public double MaxSpeed => 3 * Math.Max(CleanLines.Distance(P0, P1), Math.Max(CleanLines.Distance(P1, P2), CleanLines.Distance(P2, P3)));

        /// <summary>두 손잡이가 양 끝을 잇는 선분에서 <see cref="StraightSnap"/> 안인지.</summary>
        public bool IsStraight => SegmentDistance(P1, P0, P3) <= StraightSnap && SegmentDistance(P2, P0, P3) <= StraightSnap;

        /// <summary>t <paramref name="from"/>~<paramref name="to"/>를 곡선 위 <paramref name="step"/>보다 촘촘하게 찍은 점(양 끝 포함).</summary>
        public List<V> Sample(double from, double to, double step)
        {
            int count = Math.Max(1, (int)Math.Ceiling(MaxSpeed * (to - from) / step));
            var pts = new List<V>(count + 1);
            for (int i = 0; i <= count; i++) pts.Add(At(from + (to - from) * i / count));
            return pts;
        }

        /// <summary>굽은 방향이 바뀌는 매개값(0~1 사이, 오름차순).</summary>
        public IEnumerable<double> Inflections()
        {
            // B'×B''는 t의 2차식: 세 자리 값으로 계수를 구한다.
            double f0 = Cross(D1(0), D2(0)), fh = Cross(D1(0.5), D2(0.5)), f1 = Cross(D1(1), D2(1));
            double c = f0, a = 2 * f1 + 2 * f0 - 4 * fh, b = f1 - f0 - a;
            double scale = Math.Max(Math.Abs(f0), Math.Max(Math.Abs(fh), Math.Abs(f1)));
            if (scale < 1e-12) return [];
            var roots = new List<double>();
            if (Math.Abs(a) < 1e-9 * scale)
            {
                if (Math.Abs(b) > 1e-12) roots.Add(-c / b);
            }
            else
            {
                double disc = b * b - 4 * a * c;
                if (disc >= 0)
                {
                    double sq = Math.Sqrt(disc);
                    roots.Add((-b - sq) / (2 * a));
                    roots.Add((-b + sq) / (2 * a));
                }
            }
            return roots.Where(t => t > 0.02 && t < 0.98).Order().ToList();
        }

        public V? StartTangent()
            => Normalize(Sub(P1, P0)) ?? Normalize(Sub(P2, P0)) ?? Normalize(Sub(P3, P0));

        public V? EndTangent()
            => Normalize(Sub(P3, P2)) ?? Normalize(Sub(P3, P1)) ?? Normalize(Sub(P3, P0));
    }

    // ───────────── 벡터 도우미 ─────────────

    private static V Add(V a, V b) => (a.X + b.X, a.Y + b.Y);
    private static V Sub(V a, V b) => (a.X - b.X, a.Y - b.Y);
    private static V Mul(V a, double k) => (a.X * k, a.Y * k);
    private static V Neg(V a) => (-a.X, -a.Y);
    private static double Dot(V a, V b) => a.X * b.X + a.Y * b.Y;
    private static double Cross(V a, V b) => a.X * b.Y - a.Y * b.X;
    private static double Dist2(V a, V b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
    private static V Lerp(V a, V b, double t) => (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static V? Normalize(V v)
    {
        double length = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        return length < 1e-9 ? null : (v.X / length, v.Y / length);
    }

    /// <summary>점 <paramref name="p"/>와 선분 a–b 사이 거리.</summary>
    private static double SegmentDistance(V p, V a, V b)
    {
        V ab = Sub(b, a);
        double len2 = Dot(ab, ab);
        double t = len2 < 1e-18 ? 0 : Math.Clamp(Dot(Sub(p, a), ab) / len2, 0, 1);
        return Math.Sqrt(Dist2(p, Add(a, Mul(ab, t))));
    }
}
