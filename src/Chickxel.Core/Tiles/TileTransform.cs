using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 칸 하나의 뒤집기·회전. 그리는 차례는 시계 방향 90° 회전(<see cref="Rotate90"/>) → 좌우(<see cref="FlipX"/>) → 상하(<see cref="FlipY"/>)이고,
/// 세 비트의 8가지 조합이 모든 뒤집기·회전이다(180° = 좌우 + 상하).
/// </summary>
[Flags]
public enum TileTransform : byte
{
    None = 0,
    FlipX = 1,
    FlipY = 2,
    Rotate90 = 4,
}

/// <summary>뒤집기·회전 계산: 합치기, 방향·이웃 모양·사각형·그림에 입히기.</summary>
public static class TileTransforms
{
    /// <summary>8가지 모두(<see cref="TileTransform.None"/>이 먼저).</summary>
    public static IReadOnlyList<TileTransform> All { get; } = Enumerable.Range(0, 8).Select(i => (TileTransform)i).ToArray();

    // 화면 좌표(아래가 +y)의 2×2 행렬 (a b; c d): x' = a·x + b·y, y' = c·x + d·y
    private readonly record struct Matrix(int A, int B, int C, int D)
    {
        public static Matrix Of(TileTransform t)
        {
            var m = new Matrix(1, 0, 0, 1);
            if ((t & TileTransform.Rotate90) != 0) m = new Matrix(0, -1, 1, 0) * m; // (x, y) → (−y, x): 위 → 오른쪽
            if ((t & TileTransform.FlipX) != 0) m = new Matrix(-1, 0, 0, 1) * m;
            if ((t & TileTransform.FlipY) != 0) m = new Matrix(1, 0, 0, -1) * m;
            return m;
        }

        public static Matrix operator *(Matrix l, Matrix r)
            => new(l.A * r.A + l.B * r.C, l.A * r.B + l.B * r.D, l.C * r.A + l.D * r.C, l.C * r.B + l.D * r.D);

        public (int X, int Y) Apply(int x, int y) => (A * x + B * y, C * x + D * y);
    }

    private static readonly Matrix[] Matrices = Enumerable.Range(0, 8).Select(i => Matrix.Of((TileTransform)i)).ToArray();

    private static TileTransform FromMatrix(Matrix m)
    {
        for (int i = 0; i < 8; i++)
            if (Matrices[i] == m) return (TileTransform)i;
        throw new InvalidOperationException();
    }

    /// <summary><paramref name="first"/>을 입힌 뒤 <paramref name="then"/>을 입힌 것과 같은 하나.</summary>
    public static TileTransform Then(this TileTransform first, TileTransform then)
        => FromMatrix(Matrices[(int)then & 7] * Matrices[(int)first & 7]);

    /// <summary>되돌리는 것(<c>t.Then(t.Inverse()) == None</c>).</summary>
    public static TileTransform Inverse(this TileTransform t)
    {
        foreach (var u in All)
            if (t.Then(u) == TileTransform.None) return u;
        throw new InvalidOperationException();
    }

    /// <summary>가로·세로가 바뀌는지(90° 회전이 든 것).</summary>
    public static bool SwapsAxes(this TileTransform t) => (t & TileTransform.Rotate90) != 0;

    /// <summary>
    /// 허용한 조작(좌우·상하 뒤집기, 90° 회전)을 몇 번이든 이어 붙여 만들 수 있는 것 모두(<see cref="TileTransform.None"/> 포함, 차례는 <see cref="All"/> 순서).
    /// 예: 회전만 허용하면 0°·90°·180°·270°.
    /// </summary>
    public static IReadOnlyList<TileTransform> Reachable(bool flipX, bool flipY, bool rotate)
    {
        var gens = new List<TileTransform>();
        if (flipX) gens.Add(TileTransform.FlipX);
        if (flipY) gens.Add(TileTransform.FlipY);
        if (rotate) gens.Add(TileTransform.Rotate90);
        var found = new HashSet<TileTransform> { TileTransform.None };
        var queue = new Queue<TileTransform>(found);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            foreach (var g in gens)
                if (found.Add(t.Then(g))) queue.Enqueue(t.Then(g));
        }
        return All.Where(found.Contains).ToArray();
    }

    /// <summary>방향(칸 단위 이웃 거리)을 돌리고 뒤집는다.</summary>
    public static (int X, int Y) ApplyToVector(this TileTransform t, int x, int y) => Matrices[(int)t & 7].Apply(x, y);

    /// <summary>
    /// 상자(<paramref name="box"/> 크기, 왼쪽 위 원점) 안의 사각형을 상자째 돌리고 뒤집은 자리와, 돌린 상자 크기.
    /// 차지 칸(상자)에 대한 그림 자리를 옮길 때 쓴다.
    /// </summary>
    public static (Rectangle Rect, Size Box) ApplyToRect(this TileTransform t, Rectangle rect, Size box)
    {
        int l = rect.Left, top = rect.Top, r = rect.Right, b = rect.Bottom;
        int bw = box.Width, bh = box.Height;
        if ((t & TileTransform.Rotate90) != 0)
        {
            // (x, y) → (bh − y, x)
            (l, top, r, b) = (bh - b, l, bh - top, r);
            (bw, bh) = (bh, bw);
        }
        if ((t & TileTransform.FlipX) != 0) (l, r) = (bw - r, bw - l);
        if ((t & TileTransform.FlipY) != 0) (top, b) = (bh - b, bh - top);
        return (Rectangle.FromLTRB(l, top, r, b), new Size(bw, bh));
    }

    /// <summary>그림을 돌리고 뒤집은 새 그림(<see cref="TileTransform.None"/>이면 같은 그림).</summary>
    public static PixelImage ApplyToImage(this TileTransform t, PixelImage image)
    {
        var result = image;
        if ((t & TileTransform.Rotate90) != 0) result = ImageTransform.Rotate90(result, clockwise: true);
        if ((t & TileTransform.FlipX) != 0) result = ImageTransform.FlipHorizontal(result);
        if ((t & TileTransform.FlipY) != 0) result = ImageTransform.FlipVertical(result);
        return result;
    }
}
