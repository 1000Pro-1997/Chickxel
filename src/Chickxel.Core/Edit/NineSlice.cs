using Chickxel.Core.Model;

namespace Chickxel.Core.Edit;

/// <summary>9-slice로 크기를 바꿀 때 가운데(와 가장자리의 늘어나는 방향)를 채우는 방법.</summary>
public enum NineSliceFill
{
    /// <summary>늘이기(Unity Image Type = Sliced): 최근접으로 늘이거나 줄인다.</summary>
    Stretch,
    /// <summary>되풀이(Unity Tiled): 원래 무늬를 크기 그대로 반복한다.</summary>
    Tile,
}

/// <summary>
/// 9-slice 크기 바꾸기(Unity Sliced·Tiled와 같은 모양): 테두리 L·T·R·B 바깥의 모서리는 그대로, 가장자리는 한 방향으로만,
/// 가운데는 양쪽으로 늘이거나 되풀이한다. 새 크기가 마주 보는 두 테두리의 합보다 작으면 Unity처럼 두 테두리를 비율대로 줄인다.
/// </summary>
public static class NineSlice
{
    /// <summary>
    /// <paramref name="border"/>로 <paramref name="width"/>×<paramref name="height"/> 그림을 9-slice로 바꿀 수 있는지:
    /// 테두리가 있고(0이 아님), 가로·세로 모두 가운데가 1px 이상 남는다.
    /// </summary>
    public static bool Applies(SpriteBorder border, int width, int height)
        => !border.IsNone && border.Left >= 0 && border.Top >= 0 && border.Right >= 0 && border.Bottom >= 0
           && border.Left + border.Right < width && border.Top + border.Bottom < height;

    /// <summary>
    /// 한 축: 새 길이 <paramref name="dstLength"/>의 칸 <paramref name="dst"/>가 집는 원래 칸(길이 <paramref name="srcLength"/>,
    /// 앞 테두리 <paramref name="lo"/>, 뒤 테두리 <paramref name="hi"/>). 늘이기의 가운데는 <see cref="ImageTransform.ScaleNearest"/>와 같이 내림으로 집는다.
    /// </summary>
    public static int SourceIndex(int dst, int dstLength, int srcLength, int lo, int hi, NineSliceFill fill)
    {
        if (dstLength < lo + hi)
        {
            // 두 테두리도 다 못 들어감: 비율대로 줄인 두 테두리만(가운데 없음).
            int lo2 = (int)((long)lo * dstLength / (lo + hi));
            return dst < lo2
                ? (int)((long)dst * lo / Math.Max(1, lo2))
                : srcLength - hi + (int)((long)(dst - lo2) * hi / Math.Max(1, dstLength - lo2));
        }
        if (dst < lo) return dst;
        if (dst >= dstLength - hi) return srcLength - (dstLength - dst);
        int m = dst - lo, mid = srcLength - lo - hi, midDst = dstLength - lo - hi;
        return lo + (fill == NineSliceFill.Tile ? m % mid : (int)((long)m * mid / midDst));
    }

    /// <summary>
    /// 한 축의 연속 좌표 대응(원래 → 새, 피벗·부위 가장자리 옮기기): 앞 테두리 안은 그대로, 뒤 테두리 안은 끝에서 같은 거리,
    /// 가운데는 비율대로. 새 길이가 두 테두리의 합보다 작으면 전체를 비율대로.
    /// </summary>
    public static double MapCoordinate(double x, int srcLength, int lo, int hi, int dstLength)
    {
        if (dstLength < lo + hi) return x * dstLength / srcLength;
        if (x <= lo) return x;
        if (x >= srcLength - hi) return dstLength - (srcLength - x);
        return lo + (x - lo) * (dstLength - lo - hi) / (srcLength - lo - hi);
    }

    /// <summary>
    /// 새 길이 <paramref name="dstLength"/>의 칸마다 집는 원래 칸. <paramref name="sliced"/>가 아니면 최근접(<see cref="ImageTransform.ScaleNearest"/>와 같음).
    /// </summary>
    public static int[] SourceIndices(int dstLength, int srcLength, int lo, int hi, NineSliceFill fill, bool sliced)
    {
        var map = new int[dstLength];
        for (int i = 0; i < dstLength; i++)
            map[i] = sliced ? SourceIndex(i, dstLength, srcLength, lo, hi, fill) : (int)((long)i * srcLength / dstLength);
        return map;
    }

    /// <summary><paramref name="src"/>를 <paramref name="border"/>로 9-slice해 <paramref name="width"/>×<paramref name="height"/>로 만든 새 그림(테두리를 쓸 수 없으면 최근접).</summary>
    public static PixelImage Scale(PixelImage src, int width, int height, SpriteBorder border, NineSliceFill fill)
    {
        bool sliced = Applies(border, src.Width, src.Height);
        var xs = SourceIndices(width, src.Width, border.Left, border.Right, fill, sliced);
        var ys = SourceIndices(height, src.Height, border.Top, border.Bottom, fill, sliced);
        var dst = new PixelImage(width, height);
        for (int y = 0; y < height; y++)
        {
            int row = ys[y] * src.Width;
            for (int x = 0; x < width; x++)
                dst.Pixels[y * width + x] = src.Pixels[row + xs[x]];
        }
        return dst;
    }
}
