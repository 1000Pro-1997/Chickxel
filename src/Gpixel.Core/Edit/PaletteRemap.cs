using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>팔레트 색으로 바꿀 때의 디더링.</summary>
public enum PaletteDither
{
    /// <summary>가장 가까운 색으로만 바꾼다.</summary>
    None,
    /// <summary>
    /// 순서 디더링(Bayer 4×4): 팔레트에 없는 색은 그 색을 가장 잘 만드는 팔레트 색 <b>두 개</b>를 섞는 비율대로 칸 위치마다 둘 중 하나를 고른다.
    /// 그래서 같은 색은 늘 같은 두 색의 같은 무늬가 되고, 팔레트에 있는 색은 그대로다.
    /// </summary>
    Bayer4,
}

/// <summary>
/// 이미지 색을 팔레트 색으로 바꾼다. 투명이 아닌 픽셀마다 RGB를 팔레트의 투명이 아닌 색 중 가장 가까운 색의 RGB로 바꾸고 알파는 그대로 둔다
/// (완전 투명 픽셀은 건드리지 않음). 거리는 가중 RGB 'redmean'(빨강이 많을수록 빨강 차이를, 적을수록 파랑 차이를 더 무겁게 보는
/// 사람 눈에 가까운 근사)이고, 같은 거리면 팔레트 앞쪽 색. 색마다 결과를 기억해 같은 색은 한 번만 찾는다.
/// 이미 팔레트에 있는 RGB는 디더링을 켜도 그대로다.
/// </summary>
public sealed class PaletteRemap
{
    private static readonly int[] Bayer4x4 = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    private readonly int[] _rgb;
    private readonly HashSet<int> _exact;
    private readonly Dictionary<int, int> _cache = new();
    private readonly Dictionary<int, (int A, int B, float T)> _mixCache = new();

    /// <param name="palette">팔레트(투명 색은 무시, 같은 RGB는 한 번만). 투명이 아닌 색이 없으면 <see cref="ArgumentException"/>.</param>
    public PaletteRemap(IEnumerable<Color32> palette)
    {
        var seen = new HashSet<int>();
        var list = new List<int>();
        foreach (var c in palette)
        {
            if (c.A == 0) continue;
            int rgb = (int)(c.Argb & 0xFFFFFF);
            if (seen.Add(rgb)) list.Add(rgb);
        }
        if (list.Count == 0) throw new ArgumentException("팔레트에 투명이 아닌 색이 없습니다.", nameof(palette));
        _rgb = [.. list];
        _exact = seen;
    }

    /// <summary>쓰는 팔레트 색 수(투명·겹치는 색 제외).</summary>
    public int Count => _rgb.Length;

    /// <summary>가장 가까운 팔레트 색(알파는 <paramref name="color"/>의 알파). 완전 투명은 그대로.</summary>
    public Color32 Nearest(Color32 color)
        => color.A == 0 ? color : new Color32((color.Argb & 0xFF000000u) | (uint)NearestRgb((int)(color.Argb & 0xFFFFFF)));

    /// <summary>
    /// 이미지 픽셀을 그 자리에서 바꾼다(<paramref name="clip"/>이 있으면 그 안만). 바뀐 픽셀 수를 돌려준다.
    /// </summary>
    public int Apply(PixelImage image, PaletteDither dither = PaletteDither.None, PixelMask? clip = null)
    {
        var area = Rectangle.Intersect(clip?.Bounds ?? image.Bounds, image.Bounds);
        var px = image.Pixels;
        int changed = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                if (clip is { IsRectangle: false } m && !m.Contains(x, y)) continue;
                int i = y * image.Width + x;
                var c = px[i];
                if (c.A == 0) continue;
                int rgb = (int)(c.Argb & 0xFFFFFF);
                int to = dither == PaletteDither.Bayer4 && !_exact.Contains(rgb) ? Dithered(rgb, x, y) : NearestRgb(rgb);
                if (to == rgb) continue;
                px[i] = new Color32((c.Argb & 0xFF000000u) | (uint)to);
                changed++;
            }
        return changed;
    }

    /// <summary>바꾼 사본(바뀐 픽셀이 없으면 null). 원본은 그대로.</summary>
    public PixelImage? Remapped(PixelImage image, PaletteDither dither = PaletteDither.None)
    {
        var copy = image.Clone();
        return Apply(copy, dither) > 0 ? copy : null;
    }

    /// <summary>투명이 아닌 픽셀에 쓰인 서로 다른 색(RGBA) 수.</summary>
    public static int CountColors(PixelImage image)
    {
        var set = new HashSet<uint>();
        foreach (var c in image.Pixels)
            if (c.A != 0) set.Add(c.Argb);
        return set.Count;
    }

    /// <summary>투명이 아닌 픽셀에 쓰인 색(RGB, 불투명으로 모음)을 많이 쓰인 순서로 최대 <paramref name="max"/>개 — 색 바꾸기의 "그림에서 색 가져오기".</summary>
    public static IReadOnlyList<Color32> UsedColors(PixelImage image, int max = 64)
    {
        var counts = new Dictionary<uint, int>();
        foreach (var c in image.Pixels)
        {
            if (c.A == 0) continue;
            uint rgb = c.Argb | 0xFF000000u;
            counts[rgb] = counts.GetValueOrDefault(rgb) + 1;
        }
        return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(max).Select(kv => new Color32(kv.Key)).ToList();
    }

    /// <summary>두 색 섞기 비율 <c>T</c>(0 = A만, 1 = B만)와 칸의 Bayer 문턱을 견줘 A 또는 B.</summary>
    private int Dithered(int rgb, int x, int y)
    {
        var (a, b, t) = Mix(rgb);
        float threshold = (Bayer4x4[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
        return t > threshold ? b : a;
    }

    /// <summary>
    /// 색을 가장 잘 만드는 두 팔레트 색: A = 가장 가까운 색, B = 원래 색에 가까운 몇 색(<see cref="MixCandidates"/>) 중
    /// A와 섞었을 때(선분 위 가장 가까운 점) 원래 색과 가장 가까워지는 색. 먼 색(예: 크림에 노랑)은 섞지 않는다.
    /// 섞어도 A 하나보다 나아지지 않으면 B = A(디더 없음).
    /// </summary>
    /// <summary>섞을 짝으로 볼 가까운 색 수(A 포함).</summary>
    private const int MixCandidates = 4;

    private (int A, int B, float T) Mix(int rgb)
    {
        if (_mixCache.TryGetValue(rgb, out var hit)) return hit;
        int a = NearestRgb(rgb);
        int r = rgb >> 16 & 0xFF, g = rgb >> 8 & 0xFF, bl = rgb & 0xFF;
        int ar = a >> 16 & 0xFF, ag = a >> 8 & 0xFF, ab = a & 0xFF;
        long bestD = Distance(r, g, bl, ar, ag, ab);
        int best = a;
        float bestT = 0;
        var near = _rgb.OrderBy(p => Distance(r, g, bl, p >> 16 & 0xFF, p >> 8 & 0xFF, p & 0xFF)).Take(MixCandidates);
        foreach (int p in near)
        {
            if (p == a) continue;
            int pr = p >> 16 & 0xFF, pg = p >> 8 & 0xFF, pb = p & 0xFF;
            float dx = pr - ar, dy = pg - ag, dz = pb - ab;
            float len = dx * dx + dy * dy + dz * dz;
            if (len <= 0) continue;
            float t = Math.Clamp(((r - ar) * dx + (g - ag) * dy + (bl - ab) * dz) / len, 0f, 1f);
            if (t <= 0) continue;
            long d = Distance(r, g, bl, (int)MathF.Round(ar + dx * t), (int)MathF.Round(ag + dy * t), (int)MathF.Round(ab + dz * t));
            if (d >= bestD) continue;
            bestD = d;
            best = p;
            bestT = t;
        }
        return _mixCache[rgb] = (a, best, bestT);
    }

    private int NearestRgb(int rgb)
    {
        if (_cache.TryGetValue(rgb, out int hit)) return hit;
        int r = rgb >> 16 & 0xFF, g = rgb >> 8 & 0xFF, b = rgb & 0xFF;
        int best = _rgb[0];
        long bestD = long.MaxValue;
        foreach (int p in _rgb)
        {
            long d = Distance(r, g, b, p >> 16 & 0xFF, p >> 8 & 0xFF, p & 0xFF);
            if (d >= bestD) continue;
            bestD = d;
            best = p;
            if (d == 0) break;
        }
        _cache[rgb] = best;
        return best;
    }

    /// <summary>redmean 가중 RGB 거리의 제곱(×256).</summary>
    private static long Distance(int r1, int g1, int b1, int r2, int g2, int b2)
    {
        int rm = (r1 + r2) >> 1;
        int dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
        return (long)(512 + rm) * dr * dr + 1024L * dg * dg + (long)(767 - rm) * db * db;
    }
}
