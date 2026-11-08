using System.Drawing;

namespace Chickxel.Core.Edit;

/// <summary>
/// 변형 상자: 원본 사각형 <see cref="Bounds"/>와 <see cref="TransformParams"/>가 정하는 대응(원본 중심이 (CenterX, CenterY)로, 원본 중심 기준 배율 → 회전).
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
        var (sx0, sy0) = Center;
        double lx = (x - sx0) * p.ScaleX, ly = (y - sy0) * p.ScaleY;
        var (sin, cos) = p.SinCos();
        return (p.CenterX + lx * cos - ly * sin, p.CenterY + lx * sin + ly * cos);
    }

    /// <summary>변형한 좌표 → 원본 좌표(연속, <see cref="Map"/>의 반대). 배율이 0이면 NaN.</summary>
    public (double X, double Y) Unmap(TransformParams p, double x, double y)
    {
        var (sx0, sy0) = Center;
        double dx = x - p.CenterX, dy = y - p.CenterY;
        var (sin, cos) = p.SinCos();
        double lx = dx * cos + dy * sin, ly = -dx * sin + dy * cos;
        return (sx0 + lx / p.ScaleX, sy0 + ly / p.ScaleY);
    }

    /// <summary>
    /// 다른 상자 <paramref name="other"/>를 이 상자의 값 <paramref name="p"/>와 똑같은 대응(같은 각도·배율, 같은 자리로 옮김)으로 그리는 값.
    /// <paramref name="other"/>의 좌표 + (<paramref name="dx"/>, <paramref name="dy"/>) = 이 상자의 좌표(다른 레이어·부위의 셀을 기준 공간에 맞출 때).
    /// 원본을 넓히거나 줄일 때(여백)도 이것으로 그림이 제자리에 남는 값을 얻는다.
    /// </summary>
    public TransformParams ParamsFor(TransformBox other, TransformParams p, double dx = 0, double dy = 0)
    {
        var (ox, oy) = other.Center;
        var (cx, cy) = Map(p, ox + dx, oy + dy);
        return p with { CenterX = cx - dx, CenterY = cy - dy };
    }

    /// <summary>변형한 원본 사각형의 네 꼭짓점: 원본의 왼쪽 위, 오른쪽 위, 오른쪽 아래, 왼쪽 아래 순.</summary>
    public (double X, double Y)[] Corners(TransformParams p)
    {
        var r = Bounds;
        return [Map(p, r.Left, r.Top), Map(p, r.Right, r.Top), Map(p, r.Right, r.Bottom), Map(p, r.Left, r.Bottom)];
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

    /// <summary>원본 벡터(<paramref name="vx"/>, <paramref name="vy"/>)에 배율·회전만 입힌 벡터(옮김 없음).</summary>
    public (double X, double Y) Linear(TransformParams p, double vx, double vy)
    {
        var (cx, cy) = Center;
        var (x, y) = Map(p, cx + vx, cy + vy);
        return (x - p.CenterX, y - p.CenterY);
    }
}
