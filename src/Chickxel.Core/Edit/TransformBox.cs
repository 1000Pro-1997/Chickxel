using System.Drawing;

namespace Chickxel.Core.Edit;

/// <summary>
/// 변형 상자: 원본 사각형 <see cref="Bounds"/>와 <see cref="TransformParams"/>가 정하는 대응(원본 중심이 (CenterX, CenterY)로, 원본 중심 기준 배율 → 회전).
/// 찌그러뜨리면(<see cref="TransformParams.Distort"/>) 배율·회전 전에 상자 네 꼭짓점을 옮긴 원근 대응(호모그래피)을 먼저 입힌다.
/// 손잡이·숫자의 기준이며 픽셀은 갖지 않는다 — 픽셀은 <see cref="FreeTransform"/>·<see cref="Model.SpriteProject.LayerTransform"/>가 같은 대응으로 그린다.
/// </summary>
public readonly record struct TransformBox(Rectangle Bounds)
{
    /// <summary>원본 중심(연속 좌표).</summary>
    public (double X, double Y) Center => (Bounds.X + Bounds.Width / 2.0, Bounds.Y + Bounds.Height / 2.0);

    /// <summary>원본 그대로 두는 값.</summary>
    public TransformParams Identity => TransformParams.Identity(Bounds);

    /// <summary>원본 좌표(연속) → 변형한 좌표.</summary>
    public (double X, double Y) Map(TransformParams p, double x, double y)
    {
        if (p.IsDistorted)
        {
            var h = Homography.Of(p.Distort);
            double w = Bounds.Width, ht = Bounds.Height;
            var (u, v) = h.Apply((x - Bounds.X) / w, (y - Bounds.Y) / ht);
            x = Bounds.X + u * w;
            y = Bounds.Y + v * ht;
        }
        return AffineMap(p, x, y);
    }

    /// <summary>
    /// 변형한 좌표 → 원본 좌표(연속, <see cref="Map"/>의 반대). 배율이 0이거나, 찌그러뜨린 대응에서 원본이 없는 점(원근의 지평선 너머)이면 NaN.
    /// </summary>
    public (double X, double Y) Unmap(TransformParams p, double x, double y)
    {
        var (sx0, sy0) = Center;
        double dx = x - p.CenterX, dy = y - p.CenterY;
        var (sin, cos) = p.SinCos();
        double lx = dx * cos + dy * sin, ly = -dx * sin + dy * cos;
        double ux = sx0 + lx / p.ScaleX, uy = sy0 + ly / p.ScaleY;
        if (!p.IsDistorted) return (ux, uy);
        double w = Bounds.Width, ht = Bounds.Height;
        var inv = Homography.Of(p.Distort).Inverse();
        var (u, v, hw) = inv.ApplyHomogeneous((ux - Bounds.X) / w, (uy - Bounds.Y) / ht);
        if (!(hw > 0)) return (double.NaN, double.NaN);
        return (Bounds.X + u / hw * w, Bounds.Y + v / hw * ht);
    }

    /// <summary>원본 좌표(연속)의 점이 원근의 지평선 이쪽(<see cref="Map"/>으로 옮긴 곳이 참으로 있음)인지. 찌그러뜨리지 않았으면 늘 true.</summary>
    public bool InFront(TransformParams p, double x, double y)
        => !p.IsDistorted || Homography.Of(p.Distort).ApplyHomogeneous((x - Bounds.X) / Bounds.Width, (y - Bounds.Y) / Bounds.Height).W > 0;

    /// <summary>
    /// 다른 상자 <paramref name="other"/>를 이 상자의 값 <paramref name="p"/>와 똑같은 대응(같은 각도·배율·찌그러뜨림, 같은 자리로 옮김)으로 그리는 값.
    /// <paramref name="other"/>의 좌표 + (<paramref name="dx"/>, <paramref name="dy"/>) = 이 상자의 좌표(다른 레이어·부위의 셀을 기준 공간에 맞출 때).
    /// 원본을 넓히거나 줄일 때(여백)도 이것으로 그림이 제자리에 남는 값을 얻는다. 찌그러뜨린 값이면 <paramref name="other"/>의 꼭짓점이
    /// 같은 대응으로 가는 곳에 맞춘 찌그러뜨림을 쓴다(넓힌 상자가 원근의 지평선을 넘어 볼록하지 않으면 같은 찌그러뜨림 비율을 그대로 씀).
    /// </summary>
    public TransformParams ParamsFor(TransformBox other, TransformParams p, double dx = 0, double dy = 0)
    {
        var (ox, oy) = other.Center;
        var (cx, cy) = AffineMap(p, ox + dx, oy + dy);
        var basis = p with { CenterX = cx - dx, CenterY = cy - dy };
        if (!p.IsDistorted) return basis;
        var r = other.Bounds;
        Span<(double X, double Y)> corners = stackalloc (double X, double Y)[4];
        for (int i = 0; i < 4; i++)
        {
            var (u, v) = UnitCorners[i];
            var (mx, my) = Map(p, (u == 0 ? r.Left : r.Right) + dx, (v == 0 ? r.Top : r.Bottom) + dy);
            corners[i] = (mx - dx, my - dy);
        }
        return other.WithCorners(basis, corners) ?? basis;
    }

    /// <summary>
    /// 꼭짓점을 <paramref name="corners"/>(왼쪽 위, 오른쪽 위, 오른쪽 아래, 왼쪽 아래 순, 변형한 좌표)로 옮긴 값: 각도·배율·중심은 <paramref name="basis"/> 그대로,
    /// 찌그러뜨림만 맞춘다. 꼭짓점이 유한하지 않거나 사각형이 볼록하지 않으면(꼬이거나 접히거나 한 변이 일직선) null.
    /// </summary>
    public TransformParams? WithCorners(TransformParams basis, ReadOnlySpan<(double X, double Y)> corners)
    {
        if (corners.Length != 4) return null;
        foreach (var (x, y) in corners)
            if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
        int sign = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            var c = corners[(i + 2) % 4];
            double cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            int s = Math.Sign(cross);
            if (s == 0 || (sign != 0 && s != sign)) return null;
            sign = s;
        }
        var affine = basis with { Distort = default };
        double w = Bounds.Width, h = Bounds.Height;
        Span<double> d = stackalloc double[8];
        int k = 0;
        foreach (var (cu, cv) in UnitCorners)
        {
            var (x, y) = Unmap(affine, corners[k / 2].X, corners[k / 2].Y);
            d[k++] = Snap((x - Bounds.X) / w - cu);
            d[k++] = Snap((y - Bounds.Y) / h - cv);
        }
        foreach (var v in d)
            if (!double.IsFinite(v)) return null;
        return basis with { Distort = new CornerOffsets(d[0], d[1], d[2], d[3], d[4], d[5], d[6], d[7]) };

        static double Snap(double v) => Math.Abs(v) < 1e-9 ? 0 : v;
    }

    /// <summary>변형한 원본 사각형의 네 꼭짓점: 원본의 왼쪽 위, 오른쪽 위, 오른쪽 아래, 왼쪽 아래 순.</summary>
    public (double X, double Y)[] Corners(TransformParams p)
    {
        var r = Bounds;
        if (!p.IsDistorted)
            return [AffineMap(p, r.Left, r.Top), AffineMap(p, r.Right, r.Top), AffineMap(p, r.Right, r.Bottom), AffineMap(p, r.Left, r.Bottom)];
        // 단위 꼭짓점 + 옮긴 양을 상자로 되돌려 배율·회전(호모그래피를 거치지 않아 꼭 맞음).
        var o = p.Distort;
        double w = r.Width, h = r.Height;
        return
        [
            AffineMap(p, r.X + o.X0 * w, r.Y + o.Y0 * h),
            AffineMap(p, r.X + (1 + o.X1) * w, r.Y + o.Y1 * h),
            AffineMap(p, r.X + (1 + o.X2) * w, r.Y + (1 + o.Y2) * h),
            AffineMap(p, r.X + o.X3 * w, r.Y + (1 + o.Y3) * h),
        ];
    }

    /// <summary>변형한 원본 사각형을 감싸는 정수 사각형(꼭짓점을 바깥으로 맞춤, 소수 오차 1e-6은 버림).</summary>
    public Rectangle MappedBounds(TransformParams p)
    {
        var c = Corners(p);
        const double eps = 1e-6;
        int left = (int)Math.Floor(c.Min(v => v.X) + eps), top = (int)Math.Floor(c.Min(v => v.Y) + eps);
        int right = (int)Math.Ceiling(c.Max(v => v.X) - eps), bottom = (int)Math.Ceiling(c.Max(v => v.Y) - eps);
        return Rectangle.FromLTRB(left, top, Math.Max(right, left + 1), Math.Max(bottom, top + 1));
    }

    /// <summary>원본 벡터(<paramref name="vx"/>, <paramref name="vy"/>)에 배율·회전만 입힌 벡터(옮김·찌그러뜨림 없음).</summary>
    public (double X, double Y) Linear(TransformParams p, double vx, double vy)
    {
        var (cx, cy) = Center;
        var (x, y) = AffineMap(p, cx + vx, cy + vy);
        return (x - p.CenterX, y - p.CenterY);
    }

    /// <summary>찌그러뜨림 없이 배율 → 회전 → 중심으로 옮김만.</summary>
    private (double X, double Y) AffineMap(TransformParams p, double x, double y)
    {
        var (sx0, sy0) = Center;
        double lx = (x - sx0) * p.ScaleX, ly = (y - sy0) * p.ScaleY;
        var (sin, cos) = p.SinCos();
        return (p.CenterX + lx * cos - ly * sin, p.CenterY + lx * sin + ly * cos);
    }

    /// <summary>단위 상자의 꼭짓점: 왼쪽 위, 오른쪽 위, 오른쪽 아래, 왼쪽 아래.</summary>
    private static readonly (double U, double V)[] UnitCorners = [(0, 0), (1, 0), (1, 1), (0, 1)];
}

/// <summary>
/// 3×3 원근 대응(호모그래피) 행렬: (u, v, 1) → (M00 u + M01 v + M02, M10 u + M11 v + M12, M20 u + M21 v + M22), 마지막 성분으로 나눈다.
/// </summary>
internal readonly record struct Homography(double M00, double M01, double M02, double M10, double M11, double M12, double M20, double M21, double M22)
{
    /// <summary>단위 상자 꼭짓점 (0,0), (1,0), (1,1), (0,1)을 각 꼭짓점 + <paramref name="o"/>로 보내는 대응(Heckbert의 정사각형 → 사각형).</summary>
    public static Homography Of(CornerOffsets o)
    {
        double x0 = o.X0, y0 = o.Y0, x1 = 1 + o.X1, y1 = o.Y1, x2 = 1 + o.X2, y2 = 1 + o.Y2, x3 = o.X3, y3 = 1 + o.Y3;
        double dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3;
        double dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3;
        double den = dx1 * dy2 - dx2 * dy1;
        double g = (dx3 * dy2 - dx2 * dy3) / den, h = (dx1 * dy3 - dx3 * dy1) / den;
        return new(x1 - x0 + g * x1, x3 - x0 + h * x3, x0, y1 - y0 + g * y1, y3 - y0 + h * y3, y0, g, h, 1);
    }

    /// <summary>점 (<paramref name="u"/>, <paramref name="v"/>)를 옮긴 곳(마지막 성분으로 나눔).</summary>
    public (double X, double Y) Apply(double u, double v)
    {
        var (x, y, w) = ApplyHomogeneous(u, v);
        return (x / w, y / w);
    }

    /// <summary>나누기 전의 동차 좌표 (x, y, w).</summary>
    public (double X, double Y, double W) ApplyHomogeneous(double u, double v)
        => (M00 * u + M01 * v + M02, M10 * u + M11 * v + M12, M20 * u + M21 * v + M22);

    /// <summary>
    /// 역행렬. 원래 대응의 마지막 성분이 양수인 점(원본 쪽)이 역행렬로 되돌리면 마지막 성분도 양수가 되도록 행렬식으로 나눈 진짜 역행렬이다.
    /// </summary>
    public Homography Inverse()
    {
        double c00 = M11 * M22 - M12 * M21, c01 = M02 * M21 - M01 * M22, c02 = M01 * M12 - M02 * M11;
        double c10 = M12 * M20 - M10 * M22, c11 = M00 * M22 - M02 * M20, c12 = M02 * M10 - M00 * M12;
        double c20 = M10 * M21 - M11 * M20, c21 = M01 * M20 - M00 * M21, c22 = M00 * M11 - M01 * M10;
        double det = M00 * c00 + M01 * c10 + M02 * c20;
        double k = 1 / det;
        return new(c00 * k, c01 * k, c02 * k, c10 * k, c11 * k, c12 * k, c20 * k, c21 * k, c22 * k);
    }
}
