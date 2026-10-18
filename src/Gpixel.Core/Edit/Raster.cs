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
    /// 브러시 도장: (cx,cy)를 중심으로 한 변 <paramref name="size"/>(1~8 권장) 정사각형 또는 원.
    /// 짝수 크기는 중심의 오른쪽·아래로 한 칸 더 넓다.
    /// </summary>
    public static List<Point> Brush(int cx, int cy, int size, bool round = false)
    {
        size = Math.Max(1, size);
        int start = (size - 1) / 2;
        var pts = new List<Point>(size * size);
        float r = size / 2f;
        for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
            {
                if (round && size > 2)
                {
                    float fx = dx + 0.5f - r, fy = dy + 0.5f - r;
                    if (fx * fx + fy * fy > r * r) continue;
                }
                pts.Add(new Point(cx - start + dx, cy - start + dy));
            }
        return pts;
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
    public static int Plot(PixelImage image, IEnumerable<Point> points, Color32 color, Rectangle? clip = null,
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
    /// 채우기. <paramref name="contiguous"/>면 4방향으로 이어진 같은 색 영역만, 아니면 같은 색 전체를 바꾼다.
    /// 투명 픽셀은 RGB와 관계없이 같은 색으로 본다. 바뀐 픽셀 수를 돌려준다.
    /// </summary>
    public static int FloodFill(PixelImage image, int x, int y, Color32 color, bool contiguous = true, Rectangle? clip = null)
    {
        var area = Rectangle.Intersect(clip ?? image.Bounds, image.Bounds);
        if (!area.Contains(x, y)) return 0;
        var target = image.Get(x, y);
        if (target == color) return 0;
        if (!contiguous) return ReplaceColor(image, target, color, area);

        int w = image.Width;
        var px = image.Pixels;
        var visited = new bool[px.Length];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        int changed = 0;
        while (stack.Count > 0)
        {
            var (sx, sy) = stack.Pop();
            // 왼쪽 끝 찾기
            int lx = sx;
            while (lx > area.Left && !visited[sy * w + lx - 1] && Color32.SameVisible(px[sy * w + lx - 1], target)) lx--;
            bool spanUp = false, spanDown = false;
            for (int cx = lx; cx < area.Right; cx++)
            {
                int i = sy * w + cx;
                if (visited[i] || !Color32.SameVisible(px[i], target)) break;
                visited[i] = true;
                if (px[i] != color) { px[i] = color; changed++; }
                if (sy > area.Top) CheckRow(sy - 1, cx, ref spanUp);
                if (sy < area.Bottom - 1) CheckRow(sy + 1, cx, ref spanDown);
            }
        }
        return changed;

        void CheckRow(int ry, int cx, ref bool inSpan)
        {
            int j = ry * w + cx;
            bool match = !visited[j] && Color32.SameVisible(px[j], target);
            if (match && !inSpan) stack.Push((cx, ry));
            inSpan = match;
        }
    }

    /// <summary>영역 안의 <paramref name="target"/>(투명은 모두 같은 색) 픽셀을 모두 바꾼다.</summary>
    public static int ReplaceColor(PixelImage image, Color32 target, Color32 replacement, Rectangle? clip = null)
    {
        var area = Rectangle.Intersect(clip ?? image.Bounds, image.Bounds);
        int changed = 0;
        var px = image.Pixels;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                int i = y * image.Width + x;
                if (!Color32.SameVisible(px[i], target) || px[i] == replacement) continue;
                px[i] = replacement;
                changed++;
            }
        return changed;
    }
}
