using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>
/// 그리기 도구 알고리즘: 선(브레젠험), 사각형, 원(중점 타원), 브러시, 대칭, 채우기, 색 바꾸기.
/// 모양 함수는 점 목록만 만들고 실제 칠하기는 <see cref="Plot"/>이 한다.
/// </summary>
public static class Raster
{
    /// <summary>두 점을 잇는 브레젠험 선(양 끝 포함).</summary>
    public static List<Point> Line(Point a, Point b)
    {
        var pts = new List<Point>(Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) + 1);
        int x = a.X, y = a.Y;
        int dx = Math.Abs(b.X - a.X), sx = a.X < b.X ? 1 : -1;
        int dy = -Math.Abs(b.Y - a.Y), sy = a.Y < b.Y ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            pts.Add(new Point(x, y));
            if (x == b.X && y == b.Y) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
        return pts;
    }

    /// <summary>
    /// 두 점을 잇는 4방향 선(양 끝 포함): 한 번에 가로나 세로로만 움직여 모든 점이 앞 점과 변으로 붙는다(대각선 대신 ㄱ자 계단).
    /// 이상적인 선이 가로·세로 칸 경계를 지나는 순서대로 걷는다(같으면 세로 먼저).
    /// </summary>
    public static List<Point> Line4(Point a, Point b)
    {
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
        int sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1;
        var pts = new List<Point>(dx + dy + 1) { a };
        int x = a.X, y = a.Y;
        for (int ix = 0, iy = 0; ix < dx || iy < dy;)
        {
            // 다음 세로 경계(가로 한 칸)를 지나는 t = (½ + ix)/dx, 다음 가로 경계는 (½ + iy)/dy — 먼저 오는 쪽으로.
            if ((long)(1 + 2 * ix) * dy < (long)(1 + 2 * iy) * dx) { x += sx; ix++; }
            else { y += sy; iy++; }
            pts.Add(new Point(x, y));
        }
        return pts;
    }

    /// <summary>끝점 <paramref name="b"/>를 시작점 <paramref name="a"/>에서 0°/45°/90° 방향으로 맞춘다(Shift).</summary>
    public static Point SnapAngle(Point a, Point b)
    {
        int dx = b.X - a.X, dy = b.Y - a.Y;
        int adx = Math.Abs(dx), ady = Math.Abs(dy);
        if (adx > ady * 2) return new Point(b.X, a.Y);
        if (ady > adx * 2) return new Point(a.X, b.Y);
        int d = Math.Max(adx, ady);
        return new Point(a.X + Math.Sign(dx) * d, a.Y + Math.Sign(dy) * d);
    }

    /// <summary>
    /// 끝점 <paramref name="b"/>를 시작점 <paramref name="a"/>에서 <paramref name="stepDegrees"/>° 단위 방향으로 맞춘다(길이는 그대로, 끝점은 가장 가까운 픽셀).
    /// </summary>
    public static Point SnapAngle(Point a, Point b, double stepDegrees)
    {
        if (a == b) return b;
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double step = stepDegrees * Math.PI / 180;
        double angle = Math.Round(Math.Atan2(dy, dx) / step) * step;
        return new Point(a.X + (int)Math.Round(len * Math.Cos(angle), MidpointRounding.AwayFromZero),
            a.Y + (int)Math.Round(len * Math.Sin(angle), MidpointRounding.AwayFromZero));
    }

    /// <summary>두 모서리 점(양 끝 포함)을 감싸는 사각형. <paramref name="square"/>면 정사각형으로 맞춘다(Shift).</summary>
    public static Rectangle RectFromPoints(Point a, Point b, bool square = false)
    {
        if (square)
        {
            int side = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            b = new Point(a.X + (b.X >= a.X ? side : -side), a.Y + (b.Y >= a.Y ? side : -side));
        }
        return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
    }

    /// <summary>사각형 테두리 1픽셀.</summary>
    public static List<Point> RectOutline(Rectangle r)
    {
        var pts = new List<Point>();
        if (r.Width <= 0 || r.Height <= 0) return pts;
        int x0 = r.Left, x1 = r.Right - 1, y0 = r.Top, y1 = r.Bottom - 1;
        for (int x = x0; x <= x1; x++)
        {
            pts.Add(new Point(x, y0));
            if (y1 != y0) pts.Add(new Point(x, y1));
        }
        for (int y = y0 + 1; y < y1; y++)
        {
            pts.Add(new Point(x0, y));
            if (x1 != x0) pts.Add(new Point(x1, y));
        }
        return pts;
    }

    /// <summary>채운 사각형.</summary>
    public static List<Point> RectFill(Rectangle r)
    {
        var pts = new List<Point>(Math.Max(0, r.Width * r.Height));
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
                pts.Add(new Point(x, y));
        return pts;
    }

    /// <summary>
    /// 사각형에 내접하는 타원 테두리(중점/브레젠험 방식, 짝수 크기도 정확히 대칭).
    /// </summary>
    public static List<Point> EllipseOutline(Rectangle r)
    {
        if (r.Width <= 0 || r.Height <= 0) return [];
        if (r.Width <= 2 || r.Height <= 2) return RectFill(r);
        var set = new HashSet<Point>();
        var order = new List<Point>();
        void Add(long x, long y)
        {
            var p = new Point((int)x, (int)y);
            if (set.Add(p)) order.Add(p);
        }

        long x0 = r.Left, x1 = r.Right - 1, y0 = r.Top, y1 = r.Bottom - 1;
        long a = x1 - x0, b = y1 - y0, b1 = b & 1;
        double dx = 4 * (1.0 - a) * b * b, dy = 4 * (b1 + 1) * a * a;
        double err = dx + dy + b1 * a * a;
        y0 += (b + 1) / 2;
        y1 = y0 - b1;
        long a8 = 8 * a * a, b8 = 8 * b * b;
        do
        {
            Add(x1, y0);
            Add(x0, y0);
            Add(x0, y1);
            Add(x1, y1);
            double e2 = 2 * err;
            if (e2 <= dy) { y0++; y1--; err += dy += a8; }
            if (e2 >= dx || 2 * err > dy) { x0++; x1--; err += dx += b8; }
        } while (x0 <= x1);
        while (y0 - y1 <= b) // 납작한 타원의 끝 마무리
        {
            Add(x0 - 1, y0);
            Add(x1 + 1, y0++);
            Add(x0 - 1, y1);
            Add(x1 + 1, y1--);
        }
        return order;
    }

    /// <summary>사각형에 내접하는 채운 타원.</summary>
    public static List<Point> EllipseFill(Rectangle r)
    {
        var outline = EllipseOutline(r);
        var spans = new Dictionary<int, (int Min, int Max)>();
        foreach (var p in outline)
            spans[p.Y] = spans.TryGetValue(p.Y, out var s) ? (Math.Min(s.Min, p.X), Math.Max(s.Max, p.X)) : (p.X, p.X);
        var pts = new List<Point>();
        foreach (var (y, (min, max)) in spans.OrderBy(kv => kv.Key))
            for (int x = min; x <= max; x++) pts.Add(new Point(x, y));
        return pts;
    }

    /// <summary>
    /// 브러시 도장: (cx,cy)를 중심으로 한 변 <paramref name="size"/>(1~<see cref="BrushFootprint.MaxSize"/>)인 <paramref name="shape"/> 모양.
    /// 짝수 크기는 중심의 오른쪽·아래로 한 칸 더 넓다(<see cref="BrushFootprint"/>).
    /// </summary>
    public static List<Point> Brush(int cx, int cy, int size, BrushShape shape = BrushShape.Square)
    {
        var offsets = BrushFootprint.Get(shape, size).Offsets;
        var pts = new List<Point>(offsets.Count);
        foreach (var o in offsets) pts.Add(new Point(cx + o.X, cy + o.Y));
        return pts;
    }

    /// <summary>
    /// 순서 디더링(2×2 Bayer): (x,y)에 기본색을 칠할지. 체커는 절반, 25%는 네 칸에 한 칸, 75%는 네 칸에 세 칸이 기본색이다.
    /// </summary>
    public static bool DitherUsesPrimary(int x, int y, DitherPattern pattern)
    {
        // Bayer 2×2: [0 2; 3 1]
        int bayer = (y & 1) == 0 ? ((x & 1) == 0 ? 0 : 2) : ((x & 1) == 0 ? 3 : 1);
        int level = pattern switch
        {
            DitherPattern.Quarter => 1,
            DitherPattern.ThreeQuarters => 3,
            _ => 2,
        };
        return bayer < level;
    }

    /// <summary>
    /// 대칭 그리기: 점과 그 거울 점들(가로축 대칭 = 좌우 반전 <c>x' = W-1-x</c>, 세로축 대칭 = 상하 반전).
    /// </summary>
    public static IEnumerable<Point> Mirror(Point p, int width, int height, bool mirrorX, bool mirrorY)
    {
        yield return p;
        var mx = new Point(width - 1 - p.X, p.Y);
        var my = new Point(p.X, height - 1 - p.Y);
        var mxy = new Point(width - 1 - p.X, height - 1 - p.Y);
        if (mirrorX && mx != p) yield return mx;
        if (mirrorY && my != p) yield return my;
        if (mirrorX && mirrorY && mxy != p && mxy != mx && mxy != my) yield return mxy;
    }

    /// <summary>점 목록 전체에 대칭을 적용(중복 제거).</summary>
    public static List<Point> Mirror(IEnumerable<Point> points, int width, int height, bool mirrorX, bool mirrorY)
    {
        if (!mirrorX && !mirrorY) return points as List<Point> ?? points.ToList();
        var set = new HashSet<Point>();
        var list = new List<Point>();
        foreach (var p in points)
            foreach (var m in Mirror(p, width, height, mirrorX, mirrorY))
                if (set.Add(m)) list.Add(m);
        return list;
    }

    /// <summary>
    /// 점들을 칠한다(이미지 밖·<paramref name="clip"/> 밖은 무시). <paramref name="alphaBlend"/>면 알파 합성.
    /// 실제로 바뀐 픽셀 수를 돌려준다.
    /// </summary>
    public static int Plot(PixelImage image, IEnumerable<Point> points, Color32 color, PixelMask? clip = null,
        bool alphaBlend = false)
    {
        int changed = 0;
        foreach (var p in points)
        {
            if (clip is { } c && !c.Contains(p)) continue;
            if (!image.Contains(p.X, p.Y)) continue;
            var value = alphaBlend ? Color32.BlendOver(image.Get(p.X, p.Y), color) : color;
            if (image.Set(p.X, p.Y, value)) changed++;
        }
        return changed;
    }

    /// <summary>
    /// 채우기. <paramref name="contiguous"/>면 이어진 영역만(<paramref name="diagonal"/>이면 8방향, 아니면 4방향), 아니면 영역 안의 맞는 색 전체를 바꾼다.
    /// 영역은 <see cref="FloodRegion"/>으로 정한다.
    /// <para>
    /// <paramref name="sample"/>을 주면 영역은 그 그림(같은 크기, 예: 보이는 레이어 합성)의 색으로 정하고 칠하기는 <paramref name="image"/>에 한다.
    /// </para>
    /// 바뀐 픽셀 수를 돌려준다.
    /// </summary>
    public static int FloodFill(PixelImage image, int x, int y, Color32 color, bool contiguous = true, PixelMask? clip = null,
        int tolerance = 0, bool diagonal = false, PixelImage? sample = null)
    {
        if (sample is not null && (sample.Width != image.Width || sample.Height != image.Height))
            throw new ArgumentException(Gpixel.Core.Localization.DomainStrings.Fill_SourceSizeMismatch, nameof(sample));
        if (!image.Contains(x, y)) return 0;
        // 같은 그림에서 정확히 같은 색으로 채우면 바뀔 것이 없다.
        if (sample is null && tolerance <= 0 && image.Get(x, y) == color) return 0;
        var region = FloodRegion(sample ?? image, x, y, contiguous, clip, tolerance, diagonal);
        var r = region.Bounds;
        var px = image.Pixels;
        int changed = 0;
        for (int ry = r.Top; ry < r.Bottom; ry++)
            for (int rx = r.Left; rx < r.Right; rx++)
            {
                int i = ry * image.Width + rx;
                if (!region.Contains(rx, ry) || px[i] == color) continue;
                px[i] = color;
                changed++;
            }
        return changed;
    }

    /// <summary>
    /// 채우기·스마트 선택 영역: <paramref name="source"/>에서 (x,y)(씨앗)의 색과 맞는 칸.
    /// <paramref name="contiguous"/>면 씨앗에서 이어진 칸만(<paramref name="diagonal"/>이면 8방향, 아니면 4방향), 아니면 <paramref name="clip"/> 안의 맞는 칸 전체.
    /// <para>
    /// 맞는 색: 씨앗 색과 RGBA 채널마다의 차이 중 가장 큰 값이 <paramref name="tolerance"/>(0~255) 이하인 색(<see cref="Matches"/>).
    /// 0이면 정확히 같은 색만이다. 투명(알파 0) 픽셀은 RGB가 없는 것으로 보아, 한쪽이 투명이면 알파 차이만 잰다(둘 다 투명이면 같은 색).
    /// </para>
    /// 씨앗이 그림이나 <paramref name="clip"/> 밖이면 빈 영역. 돌려주는 영역은 그림 크기를 감싼다(필요하면 <see cref="PixelMask.Trim"/>).
    /// </summary>
    public static PixelMask FloodRegion(PixelImage source, int x, int y, bool contiguous = true, PixelMask? clip = null,
        int tolerance = 0, bool diagonal = false)
    {
        var area = Rectangle.Intersect(clip?.Bounds ?? source.Bounds, source.Bounds);
        if (!area.Contains(x, y) || clip is { } c0 && !c0.Contains(x, y)) return default;
        // 모양 제한이면 칸마다 확인한다(사각형은 area로 충분).
        PixelMask? shape = clip is { IsRectangle: false } ? clip : null;
        tolerance = Math.Clamp(tolerance, 0, 255);
        int w = source.Width;
        var src = source.Pixels;
        var seed = src[y * w + x];
        var bits = new bool[src.Length];
        bool Ok(int cx, int cy) => (shape is not { } s || s.Contains(cx, cy)) && Matches(src[cy * w + cx], seed, tolerance);

        if (!contiguous)
        {
            for (int ry = area.Top; ry < area.Bottom; ry++)
                for (int rx = area.Left; rx < area.Right; rx++)
                    if (Ok(rx, ry)) bits[ry * w + rx] = true;
            return new PixelMask(source.Bounds, bits);
        }

        // 가로 구간 단위로 넓힌다. bits가 이미 넣은 칸을 다시 보지 않게 막는다.
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (sx, sy) = stack.Pop();
            int row = sy * w;
            if (bits[row + sx] || !Ok(sx, sy)) continue;
            int lx = sx, rx = sx;
            while (lx > area.Left && !bits[row + lx - 1] && Ok(lx - 1, sy)) lx--;
            while (rx < area.Right - 1 && !bits[row + rx + 1] && Ok(rx + 1, sy)) rx++;
            for (int cx = lx; cx <= rx; cx++) bits[row + cx] = true;
            int from = diagonal ? Math.Max(area.Left, lx - 1) : lx;
            int to = diagonal ? Math.Min(area.Right - 1, rx + 1) : rx;
            if (sy > area.Top) ScanRow(sy - 1, from, to);
            if (sy < area.Bottom - 1) ScanRow(sy + 1, from, to);
        }
        return new PixelMask(source.Bounds, bits);

        // 이웃 줄에서 맞는 구간마다 시작점 하나를 넣는다.
        void ScanRow(int ry, int from, int to)
        {
            bool inSpan = false;
            for (int cx = from; cx <= to; cx++)
            {
                bool match = !bits[ry * w + cx] && Ok(cx, ry);
                if (match && !inSpan) stack.Push((cx, ry));
                inSpan = match;
            }
        }
    }

    /// <summary>
    /// <see cref="FloodRegion"/>의 색 비교: RGBA 채널 차이의 최댓값 ≤ <paramref name="tolerance"/>. 한쪽이 투명이면 알파 차이만 잰다.
    /// </summary>
    public static bool Matches(Color32 c, Color32 seed, int tolerance)
    {
        if (c.Argb == seed.Argb) return true;
        if (c.A == 0 || seed.A == 0) return Math.Abs(c.A - seed.A) <= tolerance;
        return Math.Abs(c.R - seed.R) <= tolerance && Math.Abs(c.G - seed.G) <= tolerance
            && Math.Abs(c.B - seed.B) <= tolerance && Math.Abs(c.A - seed.A) <= tolerance;
    }

    /// <summary>영역 안의 <paramref name="target"/>(투명은 모두 같은 색) 픽셀을 모두 바꾼다.</summary>
    public static int ReplaceColor(PixelImage image, Color32 target, Color32 replacement, PixelMask? clip = null)
    {
        var area = Rectangle.Intersect(clip?.Bounds ?? image.Bounds, image.Bounds);
        int changed = 0;
        var px = image.Pixels;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                int i = y * image.Width + x;
                if (clip is { IsRectangle: false } c && !c.Contains(x, y)) continue;
                if (!Color32.SameVisible(px[i], target) || px[i] == replacement) continue;
                px[i] = replacement;
                changed++;
            }
        return changed;
    }
}

/// <summary>디더링 브러시 무늬(기본색 비율).</summary>
public enum DitherPattern
{
    /// <summary>체커(50%).</summary>
    Checker,
    /// <summary>네 칸에 한 칸 기본색(25%).</summary>
    Quarter,
    /// <summary>네 칸에 세 칸 기본색(75%).</summary>
    ThreeQuarters,
}
