using System.Drawing;

namespace Gpixel.Core.Edit;

/// <summary>도형 도구가 그리는 모양(끈 시작점·끝점으로 정함).</summary>
public enum ShapeKind
{
    /// <summary>직선: 누른 곳에서 놓은 곳까지(Shift = 15° 단위). 채움 없음.</summary>
    Line,
    /// <summary>곡선: 먼저 끌어 양 끝을 정하고(그동안은 직선), 놓은 뒤 포인터로 휘어 클릭해 확정한다(<see cref="Shapes.Curve"/>). 채움 없음.</summary>
    Curve,
    /// <summary>끈 사각형(Shift = 정사각형).</summary>
    Rectangle,
    /// <summary>끈 사각형에 내접하는 타원(Shift = 정원).</summary>
    Ellipse,
    /// <summary>정삼각형: 끈 선분이 한 변이고, 끈 방향의 왼쪽에 선다(오른쪽으로 끌면 위로). Shift = 30° 단위.</summary>
    Triangle,
    /// <summary>이등변 삼각형: 끈 사각형에 꼭 맞게, 아래로 끌면 꼭짓점이 위·위로 끌면 아래(Shift = 정삼각형 비율).</summary>
    IsoscelesTriangle,
    /// <summary>정다각형(꼭짓점 수, 맨 위가 꼭짓점): 끈 사각형에 꼭 맞게 늘인다(Shift = 찌그러지지 않게).</summary>
    Polygon,
    /// <summary>별(뿔 수): 끈 사각형에 꼭 맞게 늘인다(Shift = 찌그러지지 않게).</summary>
    Star,
    /// <summary>달팽이(아르키메데스 나선): 가운데에서 바깥으로 감고 오른쪽 끝에서 끝난다. 왼쪽으로 끌면 반대로 감는다. 채움 없음.</summary>
    Spiral,
    /// <summary>원기둥: 위 타원 + 양옆 선 + 아래 앞쪽 반 타원.</summary>
    Cylinder,
    /// <summary>주사위(등각 2:1 정육면체): 육각형 외곽 + 안쪽 세 모서리.</summary>
    Cube,
}

/// <summary>
/// 도형 도구 설정: 모양과 모양별 개수(정다각형 꼭짓점·별 뿔·나선 감은 수), 선·곡선의 픽셀 잇기
/// (<paramref name="Orthogonal"/> = ㄱ자 4방향 — 모든 픽셀이 변으로 붙음, false = 대각선으로도 이음).
/// </summary>
public readonly record struct ShapeStyle(ShapeKind Kind, int Sides = 5, int StarPoints = 5, int SpiralTurns = 3, bool Orthogonal = false);

/// <summary>
/// 도형 도구의 점 목록(<see cref="Raster"/>처럼 모양만 만들고 칠하기는 <see cref="Raster.Plot"/>).
/// 외곽은 1픽셀 선(브러시 두께는 부르는 쪽이 입힘), 채움은 외곽까지 포함한다.
/// 끈 사각형에 맞추는 도형은 꼭짓점을 픽셀 중심에 놓고 좌우·상하 대칭이 정확히 맞게 반올림한다
/// (가운데가 두 픽셀 사이면 축 위의 꼭짓점을 두 픽셀로 나눈다 — 짝수 너비 삼각형의 꼭대기는 2픽셀).
/// </summary>
public static class Shapes
{
    public const int MinSides = 3, MaxSides = 32;
    public const int MinTurns = 1, MaxTurns = 10;
    /// <summary>별 안쪽 꼭짓점 반지름(바깥 = 1).</summary>
    public const double StarInnerRatio = 0.5;

    private const double Eps = 1e-9;

    /// <summary>채워 그릴 수 있는 모양(선·곡선·나선은 선뿐).</summary>
    public static bool CanFill(ShapeKind kind) => kind is not (ShapeKind.Line or ShapeKind.Curve or ShapeKind.Spiral);

    /// <summary>1픽셀 외곽선. <paramref name="constrain"/> = Shift(15° 단위 선·정사각형·정원·정다각형 비율·30° 단위 정삼각형).</summary>
    public static List<Point> Outline(ShapeStyle style, Point start, Point end, bool constrain)
    {
        switch (style.Kind)
        {
            case ShapeKind.Line:
            case ShapeKind.Curve: return Connect(start, constrain ? SnapLineEnd(start, end) : end, style.Orthogonal);
            case ShapeKind.Rectangle: return Raster.RectOutline(Raster.RectFromPoints(start, end, constrain));
            case ShapeKind.Ellipse: return Raster.EllipseOutline(Raster.RectFromPoints(start, end, constrain));
            case ShapeKind.Triangle: return PolygonOutline(EquilateralTriangle(start, end, constrain));
            case ShapeKind.Spiral: return Spiral(start, end, constrain, Math.Clamp(style.SpiralTurns, MinTurns, MaxTurns));
            case ShapeKind.Cylinder: return Cylinder(Raster.RectFromPoints(start, end, constrain), filled: false);
            case ShapeKind.Cube:
            {
                var (hull, inner) = Cube(Raster.RectFromPoints(start, end, constrain));
                var pts = PolygonOutline(hull);
                var seen = pts.ToHashSet();
                foreach (var p in inner) if (seen.Add(p)) pts.Add(p);
                return pts;
            }
            default: return PolygonOutline(FittedVertices(style, start, end, constrain));
        }
    }

    /// <summary>채운 모양(외곽 포함). 선·곡선·나선은 채움이 없어 <see cref="Outline"/>과 같다.</summary>
    public static List<Point> Fill(ShapeStyle style, Point start, Point end, bool constrain) => style.Kind switch
    {
        ShapeKind.Rectangle => Raster.RectFill(Raster.RectFromPoints(start, end, constrain)),
        ShapeKind.Ellipse => Raster.EllipseFill(Raster.RectFromPoints(start, end, constrain)),
        ShapeKind.Triangle => PolygonFill(EquilateralTriangle(start, end, constrain)),
        ShapeKind.Line or ShapeKind.Curve or ShapeKind.Spiral => Outline(style, start, end, constrain),
        ShapeKind.Cylinder => Cylinder(Raster.RectFromPoints(start, end, constrain), filled: true),
        ShapeKind.Cube => PolygonFill(Cube(Raster.RectFromPoints(start, end, constrain)).Hull),
        _ => PolygonFill(FittedVertices(style, start, end, constrain)),
    };

    /// <summary>두 점을 잇는 선: <paramref name="orthogonal"/>면 ㄱ자 4방향(<see cref="Raster.Line4"/>), 아니면 대각선도 쓰는 브레젠험.</summary>
    public static List<Point> Connect(Point a, Point b, bool orthogonal) => orthogonal ? Raster.Line4(a, b) : Raster.Line(a, b);

    /// <summary>선·곡선 끝점의 Shift 각도 단위(°).</summary>
    public const double LineSnapDegrees = 15;

    /// <summary>선·곡선 끝점을 시작점에서 <see cref="LineSnapDegrees"/>° 단위 방향으로 맞춘다(Shift).</summary>
    public static Point SnapLineEnd(Point start, Point end) => Raster.SnapAngle(start, end, LineSnapDegrees);

    /// <summary>
    /// 곡선: <paramref name="a"/>에서 <paramref name="b"/>까지 <paramref name="through"/>를 지난다.
    /// 보통은 2차 베지어(가운데 t = ½가 through를 지남), <paramref name="arc"/>면 세 점을 지나는 원의 호(Shift).
    /// 끊김 없는 1픽셀 선이다. <paramref name="orthogonal"/>면 ㄱ자 4방향으로 잇고, 아니면 계단 모서리의 겹 픽셀을 뺀다(픽셀 퍼펙트).
    /// 앞 절반은 a에서, 뒤 절반은 b에서 같은 규칙으로 그려 대칭으로 휜 곡선은 픽셀도 대칭이다.
    /// </summary>
    public static List<Point> Curve(Point a, Point b, Point through, bool arc = false, bool orthogonal = false)
    {
        Func<double, (double X, double Y)> at;
        double length;
        if (arc && ArcThrough(a, b, through) is { } circle)
        {
            var (ux, uy, r, start, sweep) = circle;
            at = t => (ux + r * Math.Cos(start + sweep * t), uy + r * Math.Sin(start + sweep * t));
            length = r * Math.Abs(sweep);
        }
        else
        {
            // 세 점이 한 줄이면 호 대신 베지어(= 곧은 선이거나 접힌 선).
            double cx = 2 * through.X - (a.X + b.X) / 2.0, cy = 2 * through.Y - (a.Y + b.Y) / 2.0;
            at = t =>
            {
                double u = 1 - t;
                return (u * u * a.X + 2 * u * t * cx + t * t * b.X, u * u * a.Y + 2 * u * t * cy + t * t * b.Y);
            };
            length = Math.Sqrt((cx - a.X) * (cx - a.X) + (cy - a.Y) * (cy - a.Y)) + Math.Sqrt((b.X - cx) * (b.X - cx) + (b.Y - cy) * (b.Y - cy));
        }
        int half = (int)Math.Ceiling(Math.Min(length, 1e6)) + 1;
        static int Away(double d) => d < 0 ? -(int)Math.Floor(-d + 0.5) : (int)Math.Floor(d + 0.5);
        // from 끝에서 가운데(t = ½)까지: from 기준으로 반올림하고 이어진 점으로.
        List<Point> Half(Point from, bool reverse)
        {
            var path = new List<Point> { from };
            var prev = from;
            for (int k = 1; k <= half; k++)
            {
                double t = 0.5 * k / half;
                var (x, y) = at(reverse ? 1 - t : t);
                var p = new Point(from.X + Away(x - from.X), from.Y + Away(y - from.Y));
                if (p == prev) continue;
                path.AddRange(Connect(prev, p, orthogonal).Skip(1));
                prev = p;
            }
            return orthogonal ? path : PixelPerfect.Filter(path);
        }
        var first = Half(a, reverse: false);
        var second = Half(b, reverse: true);
        second.Reverse();
        var all = new List<Point>(first);
        if (first[^1] != second[0]) all.AddRange(Connect(first[^1], second[0], orthogonal).Skip(1).SkipLast(1));
        all.AddRange(second);
        var set = new HashSet<Point>();
        return all.Where(set.Add).ToList();
    }

    /// <summary>a·b·p를 지나는 원(중심·반지름)과 a에서 p를 거쳐 b까지 도는 각(시작각·도는 각, 라디안). 세 점이 한 줄이면 null.</summary>
    private static (double X, double Y, double R, double Start, double Sweep)? ArcThrough(Point a, Point b, Point p)
    {
        double d = 2.0 * (a.X * (b.Y - p.Y) + b.X * (p.Y - a.Y) + p.X * (a.Y - b.Y));
        if (Math.Abs(d) < Eps) return null;
        double a2 = (double)a.X * a.X + (double)a.Y * a.Y, b2 = (double)b.X * b.X + (double)b.Y * b.Y, p2 = (double)p.X * p.X + (double)p.Y * p.Y;
        double ux = (a2 * (b.Y - p.Y) + b2 * (p.Y - a.Y) + p2 * (a.Y - b.Y)) / d;
        double uy = (a2 * (p.X - b.X) + b2 * (a.X - p.X) + p2 * (b.X - a.X)) / d;
        double r = Math.Sqrt((a.X - ux) * (a.X - ux) + (a.Y - uy) * (a.Y - uy));
        double ta = Math.Atan2(a.Y - uy, a.X - ux), tb = Math.Atan2(b.Y - uy, b.X - ux), tp = Math.Atan2(p.Y - uy, p.X - ux);
        static double Turn(double v) => (v % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        double toB = Turn(tb - ta), toP = Turn(tp - ta);
        // 각이 늘어나는 쪽으로 b보다 p를 먼저 만나면 그쪽, 아니면 반대쪽으로 돈다.
        return (ux, uy, r, ta, toP <= toB ? toB : toB - 2 * Math.PI);
    }

    // ───────────── 정삼각형 ─────────────

    /// <summary>선분 a→b를 한 변으로, 끈 방향 왼쪽(화면 기준)에 셋째 꼭짓점.</summary>
    private static List<Point> EquilateralTriangle(Point a, Point b, bool constrain)
    {
        if (constrain) b = Raster.SnapAngle(a, b, 30);
        double vx = b.X - a.X, vy = b.Y - a.Y, k = Math.Sqrt(3) / 2;
        var c = new Point((int)Math.Round((a.X + b.X) / 2.0 + vy * k, MidpointRounding.AwayFromZero),
            (int)Math.Round((a.Y + b.Y) / 2.0 - vx * k, MidpointRounding.AwayFromZero));
        return [a, b, c];
    }

    // ───────────── 끈 사각형에 맞추는 다각형 ─────────────

    /// <summary>이등변 삼각형·정다각형·별: 단위 꼭짓점을 끈 사각형(Shift = 원래 비율)에 맞춘 픽셀 꼭짓점.</summary>
    private static List<Point> FittedVertices(ShapeStyle style, Point start, Point end, bool constrain)
    {
        var raw = UnitVertices(style, apexDown: end.Y < start.Y);
        double minX = raw.Min(v => v.X), maxX = raw.Max(v => v.X), minY = raw.Min(v => v.Y), maxY = raw.Max(v => v.Y);
        double midX = (minX + maxX) / 2, midY = (minY + maxY) / 2, halfW = (maxX - minX) / 2, halfH = (maxY - minY) / 2;
        var unit = raw.Select(v => ((v.X - midX) / halfW, (v.Y - midY) / halfH)).ToList();
        var r = constrain ? FitBounds(start, end, halfW / halfH) : Raster.RectFromPoints(start, end);
        return new Placer(r).Polygon(unit);
    }

    /// <summary>모양의 꼭짓점(화면 좌표: y 아래로, 시계 방향).</summary>
    private static List<(double X, double Y)> UnitVertices(ShapeStyle style, bool apexDown)
    {
        var list = new List<(double X, double Y)>();
        switch (style.Kind)
        {
            case ShapeKind.IsoscelesTriangle:
                double s = apexDown ? -1 : 1;
                list.AddRange([(0, -s), (s, s), (-s, s)]);
                break;
            case ShapeKind.Star:
            {
                int n = Math.Clamp(style.StarPoints, MinSides, MaxSides);
                for (int i = 0; i < 2 * n; i++)
                {
                    double radius = i % 2 == 0 ? 1 : StarInnerRatio, a = -Math.PI / 2 + Math.PI * i / n;
                    list.Add((radius * Math.Cos(a), radius * Math.Sin(a)));
                }
                break;
            }
            default:
            {
                int n = Math.Clamp(style.Sides, MinSides, MaxSides);
                for (int i = 0; i < n; i++)
                {
                    double a = -Math.PI / 2 + 2 * Math.PI * i / n;
                    list.Add((Math.Cos(a), Math.Sin(a)));
                }
                break;
            }
        }
        return list;
    }

    /// <summary>시작점에서 끈 쪽으로, 픽셀 중심 사이 거리의 가로:세로가 <paramref name="aspect"/>인 사각형(짧은 쪽을 늘림).</summary>
    private static Rectangle FitBounds(Point a, Point b, double aspect)
    {
        int sw = Math.Abs(b.X - a.X), sh = Math.Abs(b.Y - a.Y);
        if (sw > sh * aspect) sh = (int)Math.Round(sw / aspect);
        else sw = (int)Math.Round(sh * aspect);
        var e = new Point(a.X + (b.X >= a.X ? sw : -sw), a.Y + (b.Y >= a.Y ? sh : -sh));
        return Raster.RectFromPoints(a, e);
    }

    /// <summary>
    /// 단위 좌표(−1~1)를 사각형의 픽셀 중심에 놓는다. 가운데에서 같은 거리면 양쪽이 같은 칸 수만큼 떨어지게 반올림하고,
    /// 가운데가 두 픽셀 사이인데 꼭짓점이 축 위에 있으면 두 픽셀로 나눈다.
    /// </summary>
    private readonly struct Placer(Rectangle r)
    {
        private readonly int _cx2 = 2 * r.Left + r.Width - 1, _cy2 = 2 * r.Top + r.Height - 1;
        private readonly double _hx = (r.Width - 1) / 2.0, _hy = (r.Height - 1) / 2.0;

        /// <summary>가로 좌표 하나 또는 둘(둘이면 <paramref name="prev"/>(앞 꼭짓점) 쪽 먼저).</summary>
        public int[] X(double u, double prev) => Snap(_cx2, u * _hx, prev);
        public int[] Y(double v, double prev) => Snap(_cy2, v * _hy, prev);

        private static int[] Snap(int c2, double d, double prev)
        {
            double m = Math.Abs(d);
            int sign = d < 0 ? -1 : 1;
            if ((c2 & 1) == 0)
                return [c2 / 2 + (m < Eps ? 0 : sign * (int)Math.Floor(m + 0.5 + Eps))];
            if (m < Eps)
            {
                int lo = (c2 - 1) / 2, hi = (c2 + 1) / 2;
                return prev < 0 ? [lo, hi] : [hi, lo];
            }
            return [(c2 + sign * (2 * (int)Math.Floor(m + Eps) + 1)) / 2];
        }

        /// <summary>닫힌 다각형 꼭짓점(나뉜 꼭짓점은 둘레 순서대로).</summary>
        public List<Point> Polygon(IReadOnlyList<(double U, double V)> unit)
        {
            var pts = new List<Point>();
            for (int i = 0; i < unit.Count; i++)
            {
                var (u, v) = unit[i];
                var (pu, pv) = unit[(i + unit.Count - 1) % unit.Count];
                var xs = X(u, pu);
                var ys = Y(v, pv);
                if (xs.Length == 2 && ys.Length == 2) ys = [ys[0]];
                foreach (int x in xs)
                    foreach (int y in ys)
                        pts.Add(new Point(x, y));
            }
            return pts;
        }
    }

    // ───────────── 다각형 외곽·채움 ─────────────

    private static List<Point> PolygonOutline(IReadOnlyList<Point> v)
    {
        var (cx2, cy2) = Center2(v);
        var set = new HashSet<Point>();
        var list = new List<Point>();
        for (int i = 0; i < v.Count; i++)
            foreach (var p in Edge(v[i], v[(i + 1) % v.Count], cx2, cy2))
                if (set.Add(p)) list.Add(p);
        return list;
    }

    /// <summary>꼭짓점을 감싸는 사각형의 가운데(두 배 좌표).</summary>
    private static (int X2, int Y2) Center2(IReadOnlyList<Point> v)
        => (v.Min(p => p.X) + v.Max(p => p.X), v.Min(p => p.Y) + v.Max(p => p.Y));

    /// <summary>
    /// 변 하나(브레젠험). 가운데에서 먼 끝(세로 먼저, 같으면 가로)부터 그려, 대칭인 변끼리 픽셀도 정확히 거울 모양이 된다.
    /// </summary>
    private static List<Point> Edge(Point a, Point b, int cx2, int cy2)
    {
        int ay = Math.Abs(2 * a.Y - cy2), by = Math.Abs(2 * b.Y - cy2);
        int ax = Math.Abs(2 * a.X - cx2), bx = Math.Abs(2 * b.X - cx2);
        bool fromA = ay != by ? ay > by : ax != bx ? ax > bx : a.Y != b.Y ? a.Y < b.Y : a.X <= b.X;
        return fromA ? Raster.Line(a, b) : Raster.Line(b, a);
    }

    /// <summary>외곽 + 중심이 안쪽(짝홀 규칙)인 픽셀.</summary>
    private static List<Point> PolygonFill(IReadOnlyList<Point> v)
    {
        var list = PolygonOutline(v);
        var set = list.ToHashSet();
        int minY = v.Min(p => p.Y), maxY = v.Max(p => p.Y);
        var xs = new List<double>();
        for (int y = minY; y <= maxY; y++)
        {
            // 가로줄 y와 변의 교차점(아래 끝은 포함, 위 끝은 제외: 꼭짓점을 두 번 세지 않는다).
            xs.Clear();
            for (int i = 0; i < v.Count; i++)
            {
                var a = v[i];
                var b = v[(i + 1) % v.Count];
                if (a.Y == b.Y) continue;
                if ((a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y))
                    xs.Add(a.X + (y - a.Y) * (double)(b.X - a.X) / (b.Y - a.Y));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                for (int x = (int)Math.Ceiling(xs[k]); x <= (int)Math.Floor(xs[k + 1]); x++)
                {
                    var p = new Point(x, y);
                    if (set.Add(p)) list.Add(p);
                }
        }
        return list;
    }

    // ───────────── 나선 ─────────────

    /// <summary>
    /// 끈 사각형(Shift = 정사각형)에 꼭 맞춘 나선: 가운데 근처에서 시작해 <paramref name="turns"/>바퀴 감고
    /// 오른쪽 끝(왼쪽으로 끌었으면 왼쪽 끝)에서 끝난다.
    /// </summary>
    private static List<Point> Spiral(Point start, Point end, bool constrain, int turns)
    {
        var r = Raster.RectFromPoints(start, end, constrain);
        // 바깥 바퀴에서도 표본 사이가 반 픽셀 안쪽이게.
        int samples = (int)Math.Ceiling(turns * Math.PI * Math.Max(r.Width, r.Height) * 2) + 8;
        var unit = new (double X, double Y)[samples + 1];
        for (int k = 0; k <= samples; k++)
        {
            double t = (double)k / samples, a = 2 * Math.PI * turns * (t - 1);
            unit[k] = (t * Math.Cos(a), t * Math.Sin(a));
        }
        double minX = unit.Min(u => u.X), maxX = unit.Max(u => u.X), minY = unit.Min(u => u.Y), maxY = unit.Max(u => u.Y);
        double sx = (r.Width - 1) / (maxX - minX), sy = (r.Height - 1) / (maxY - minY);
        bool mirror = end.X < start.X;
        var path = new List<Point>();
        Point? prev = null;
        foreach (var (ux, uy) in unit)
        {
            int x = (int)Math.Floor((ux - minX) * sx + 0.5), y = r.Top + (int)Math.Floor((uy - minY) * sy + 0.5);
            var p = new Point(mirror ? r.Right - 1 - x : r.Left + x, y);
            if (prev is { } q)
            {
                if (q == p) continue;
                path.AddRange(Raster.Line(q, p).Skip(1));
            }
            else path.Add(p);
            prev = p;
        }
        var set = new HashSet<Point>();
        return PixelPerfect.Filter(path).Where(set.Add).ToList();
    }

    // ───────────── 원기둥 ─────────────

    /// <summary>위 타원(너비의 1/3 높이) + 양옆 선 + 아래 앞쪽 반 타원. 채움이면 그 안을 모두.</summary>
    private static List<Point> Cylinder(Rectangle r, bool filled)
    {
        if (r.Width <= 0 || r.Height <= 0) return [];
        int capH = Math.Min(Math.Max(3, (int)Math.Round(r.Width / 3.0)), Math.Max(1, r.Height / 2));
        var top = new Rectangle(r.Left, r.Top, r.Width, capH);
        var bottom = new Rectangle(r.Left, r.Bottom - capH, r.Width, capH);
        var topOutline = Raster.EllipseOutline(top);
        var bottomOutline = Raster.EllipseOutline(bottom);
        // 옆선은 위 타원의 왼쪽 끝 마지막 줄에서 아래 타원의 왼쪽 끝 첫 줄까지.
        int sideTop = topOutline.Where(p => p.X == r.Left).Max(p => p.Y);
        int sideBottom = bottomOutline.Where(p => p.X == r.Left).Min(p => p.Y);
        var set = new HashSet<Point>();
        var list = new List<Point>();
        void Add(Point p) { if (set.Add(p)) list.Add(p); }
        if (filled)
        {
            foreach (var p in Raster.EllipseFill(top)) Add(p);
            foreach (var p in Raster.RectFill(Rectangle.FromLTRB(r.Left, sideTop, r.Right, sideBottom + 1))) Add(p);
            foreach (var p in Raster.EllipseFill(bottom)) Add(p);
            return list;
        }
        foreach (var p in topOutline) Add(p);
        for (int y = sideTop; y <= sideBottom; y++)
        {
            Add(new Point(r.Left, y));
            Add(new Point(r.Right - 1, y));
        }
        foreach (var p in bottomOutline) if (p.Y >= sideBottom) Add(p);
        return list;
    }

    // ───────────── 주사위(정육면체) ─────────────

    /// <summary>
    /// 등각(2:1) 정육면체: 바깥 육각형(위 → 오른쪽 위 → 오른쪽 아래 → 아래 → 왼쪽 아래 → 왼쪽 위)과
    /// 앞 모서리 셋(가운데에서 왼쪽 위·오른쪽 위·아래로). 윗면 높이는 너비의 절반(2:1 기울기).
    /// </summary>
    private static (List<Point> Hull, List<Point> Inner) Cube(Rectangle r)
    {
        var place = new Placer(r);
        int spanY = r.Height - 1;
        int q = Math.Min((int)Math.Round((r.Width - 1) / 4.0, MidpointRounding.AwayFromZero), spanY / 2);
        double hy = spanY / 2.0, vq = hy > 0 ? q / hy : 0;
        double top = -1, upper = -1 + vq, center = -1 + 2 * vq, lower = 1 - vq, bottom = 1;
        if (hy == 0) top = upper = center = lower = bottom = 0;
        var hull = place.Polygon([(0, top), (1, upper), (1, lower), (0, bottom), (-1, lower), (-1, upper)]);
        int[] cxs = place.X(0, -1);
        int left = place.X(-1, 0)[0], right = place.X(1, 0)[0];
        int cy = place.Y(center, -1)[0], uy = place.Y(upper, -1)[0], by = place.Y(bottom, -1)[0];
        var inner = new List<Point>();
        inner.AddRange(Raster.Line(new Point(cxs[0], cy), new Point(left, uy)));
        inner.AddRange(Raster.Line(new Point(cxs[^1], cy), new Point(right, uy)));
        foreach (int x in cxs) inner.AddRange(Raster.Line(new Point(x, cy), new Point(x, by)));
        return (hull, inner);
    }
}
