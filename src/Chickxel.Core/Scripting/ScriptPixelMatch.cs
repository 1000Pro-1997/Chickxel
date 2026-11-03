using Chickxel.Core.Model;

namespace Chickxel.Core.Scripting;

/// <summary>그림 하나를 읽는 쪽(<see cref="ScriptPixelMatch"/> — Jint 배열·빠른 경로 숫자 배열). 칸 값은 <see cref="ScriptHostApi.ToByte"/>로 읽는다.</summary>
internal interface IScriptPixels
{
    int Width { get; }
    int Height { get; }

    /// <summary>픽셀 번호(y × 너비 + x)의 알파.</summary>
    byte Alpha(int index);

    /// <summary>픽셀 번호의 색.</summary>
    Color32 Color(int index);
}

/// <summary>
/// <c>ctx.matchPixels(a, b, mode)</c>(Jint와 빠른 경로가 같이 씀): 두 그림의 불투명 픽셀(알파 &gt; 0)을 짝짓는다.
/// 쪽마다 불투명 픽셀을 자기 불투명 범위를 같은 정사각 격자(두 범위의 긴 변 이상인 2의 거듭제곱)로 늘린 힐베르트 곡선 차례로 세운다
/// (자리와 크기가 달라도 부위끼리 이어짐). 색 모드(<see cref="ColorMode"/>)는 밝기(0.299r + 0.587g + 0.114b) 차례가 먼저고 힐베르트 차례는 동점 가르기다.
/// 짝 k(0 ≤ k &lt; count, count = max(nA, nB), 한쪽이 0이면 0)는 A[⌊k·nA/count⌋]와 B[⌊k·nB/count⌋] — 많은 쪽은 한 번씩, 적은 쪽은 되풀이(모이거나 갈라짐).
/// </summary>
internal static class ScriptPixelMatch
{
    /// <summary>밝기 차례로 짝짓는 모드 이름(그 밖의 값은 자리 차례).</summary>
    public const string ColorMode = "color";

    internal readonly record struct Entry(double Luma, long Key, int Index);

    /// <summary>짝지은 결과: 짝 <see cref="Count"/>개, 짝 k의 (A의 x, y, B의 x, y).</summary>
    public readonly struct Pairs
    {
        private readonly Entry[] _a, _b;
        private readonly int _aw, _bw;

        internal Pairs(Entry[] a, int aw, Entry[] b, int bw)
        {
            _a = a;
            _b = b;
            _aw = aw;
            _bw = bw;
            Count = a.Length == 0 || b.Length == 0 ? 0 : Math.Max(a.Length, b.Length);
        }

        public int Count { get; }

        public (int AX, int AY, int BX, int BY) this[int k]
        {
            get
            {
                int ia = _a[(int)((long)k * _a.Length / Count)].Index, ib = _b[(int)((long)k * _b.Length / Count)].Index;
                return (ia % _aw, ia / _aw, ib % _bw, ib / _bw);
            }
        }
    }

    /// <summary><paramref name="a"/>와 <paramref name="b"/>의 불투명 픽셀을 짝짓는다. <paramref name="byColor"/>면 밝기 차례가 먼저. O(n log n).</summary>
    public static Pairs Match<TA, TB>(TA a, TB b, bool byColor) where TA : IScriptPixels where TB : IScriptPixels
    {
        var boxA = Bounds(a);
        var boxB = Bounds(b);
        int side = Math.Max(Math.Max(boxA.W, boxA.H), Math.Max(boxB.W, boxB.H));
        int grid = 1;
        while (grid < side) grid <<= 1;
        return new Pairs(Order(a, boxA, grid, byColor), a.Width, Order(b, boxB, grid, byColor), b.Width);
    }

    // 불투명 픽셀 수와 범위(없으면 수 0, 크기 0)
    private static (int Count, int X, int Y, int W, int H) Bounds<T>(T image) where T : IScriptPixels
    {
        int w = image.Width, h = image.Height, count = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0, i = 0; y < h; y++)
            for (int x = 0; x < w; x++, i++)
            {
                if (image.Alpha(i) == 0) continue;
                count++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        return count == 0 ? (0, 0, 0, 0, 0) : (count, minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // 불투명 픽셀을 차례대로: 범위를 grid × grid로 늘린 힐베르트 번호(격자가 범위보다 작지 않아 픽셀마다 다름), 색 모드면 밝기가 먼저
    private static Entry[] Order<T>(T image, (int Count, int X, int Y, int W, int H) box, int grid, bool byColor) where T : IScriptPixels
    {
        var entries = new Entry[box.Count];
        if (box.Count == 0) return entries;
        int w = image.Width, n = 0;
        for (int y = box.Y; y < box.Y + box.H; y++)
            for (int x = box.X; x < box.X + box.W; x++)
            {
                int i = y * w + x;
                if (image.Alpha(i) == 0) continue;
                int gx = (int)((long)(x - box.X) * grid / box.W), gy = (int)((long)(y - box.Y) * grid / box.H);
                double luma = 0;
                if (byColor)
                {
                    var c = image.Color(i);
                    luma = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                }
                entries[n++] = new Entry(luma, Hilbert(grid, gx, gy), i);
            }
        entries.AsSpan().Sort(static (p, q) =>
        {
            int c = p.Luma.CompareTo(q.Luma);
            if (c == 0) c = p.Key.CompareTo(q.Key);
            return c != 0 ? c : p.Index.CompareTo(q.Index);
        });
        return entries;
    }

    // (x, y)의 힐베르트 곡선 번호(n × n 격자, n은 2의 거듭제곱)
    private static long Hilbert(int n, int x, int y)
    {
        long d = 0;
        for (int s = n / 2; s > 0; s /= 2)
        {
            int rx = (x & s) != 0 ? 1 : 0, ry = (y & s) != 0 ? 1 : 0;
            d += (long)s * s * ((3 * rx) ^ ry);
            if (ry == 0)
            {
                if (rx == 1)
                {
                    x = n - 1 - x;
                    y = n - 1 - y;
                }
                (x, y) = (y, x);
            }
        }
        return d;
    }
}
