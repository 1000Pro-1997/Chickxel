using System.Drawing;

namespace Gpixel.Core.Edit;

/// <summary>브러시 모양.</summary>
public enum BrushShape
{
    Square,
    Circle,
    Diamond,
}

/// <summary>
/// 브러시 도장 모양(중심 기준 오프셋). 한 변 <see cref="Size"/> 칸 안에 들어가며, 왼쪽 위 칸이 중심에서 (-<see cref="Start"/>, -<see cref="Start"/>)이다.
/// 짝수 크기는 중심의 오른쪽·아래로 한 칸 더 넓다. 크기 1은 모양과 관계없이 한 점.
/// <list type="bullet">
/// <item>사각: 한 변 전체.</item>
/// <item>원: 칸 가운데와 모양 가운데 거리² ≤ r² − r/2 (r = 크기/2) — 픽셀 아트 원 브러시(3 = 십자 5칸, 4 = 12칸, 5 = 21칸, 7 = 37칸). 2 이하는 사각과 같다.</item>
/// <item>마름모: 칸 가운데에서 모양 가운데까지 맨해튼 거리 ≤ ⌊크기/2⌋.</item>
/// </list>
/// 크기·모양마다 한 번 만들어 재사용한다.
/// </summary>
public sealed class BrushFootprint
{
    public const int MaxSize = 32;

    private static readonly BrushFootprint?[,] Cache = new BrushFootprint?[3, MaxSize + 1];

    private readonly bool[] _mask;

    public BrushShape Shape { get; }
    public int Size { get; }
    /// <summary>왼쪽 위 칸이 중심에서 얼마나 떨어졌는지(<c>(Size - 1) / 2</c>).</summary>
    public int Start => (Size - 1) / 2;
    /// <summary>중심 기준 오프셋(위에서 아래, 왼쪽에서 오른쪽 순).</summary>
    public IReadOnlyList<Point> Offsets { get; }

    private BrushFootprint(BrushShape shape, int size)
    {
        Shape = shape;
        Size = size;
        _mask = new bool[size * size];
        var offsets = new List<Point>(size * size);
        int start = (size - 1) / 2;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (!Inside(shape, size, x, y)) continue;
                _mask[y * size + x] = true;
                offsets.Add(new Point(x - start, y - start));
            }
        Offsets = offsets;
    }

    /// <summary>(모양, 크기)의 도장. 크기는 1~<see cref="MaxSize"/>로 맞춘다.</summary>
    public static BrushFootprint Get(BrushShape shape, int size)
    {
        size = Math.Clamp(size, 1, MaxSize);
        int s = shape is BrushShape.Circle or BrushShape.Diamond ? (int)shape : 0;
        lock (Cache)
            return Cache[s, size] ??= new BrushFootprint((BrushShape)s, size);
    }

    /// <summary>중심 기준 오프셋 (dx, dy) 칸이 도장에 들어가는지.</summary>
    public bool Contains(int dx, int dy)
    {
        int x = dx + Start, y = dy + Start;
        return (uint)x < (uint)Size && (uint)y < (uint)Size && _mask[y * Size + x];
    }

    // 두 배 좌표로 반 칸 계산을 정수로 한다: 칸 가운데 2x+1, 모양 가운데 size.
    private static bool Inside(BrushShape shape, int size, int x, int y)
    {
        int fx = 2 * x + 1 - size, fy = 2 * y + 1 - size;
        return shape switch
        {
            BrushShape.Circle => fx * fx + fy * fy <= size * (size - 1),
            BrushShape.Diamond => Math.Abs(fx) + Math.Abs(fy) <= 2 * (size / 2),
            _ => true,
        };
    }
}
