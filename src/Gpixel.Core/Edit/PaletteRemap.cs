using System.Drawing;
using Gpixel.Core.Model;
using Gpixel.Core.Pixelate;

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
    /// <summary>순서 디더링(Bayer 2×2): <see cref="Bayer4"/>와 같고 무늬만 2×2(5단계).</summary>
    Bayer2,
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
    private readonly Func<Color32, Color32>? _nearest;
    private readonly Func<Color32, Color32, bool>? _canMix;

    /// <param name="palette">팔레트(투명 색은 무시, 같은 RGB는 한 번만). 투명이 아닌 색이 없으면 <see cref="ArgumentException"/>.</param>
    /// <param name="nearest">가장 가까운 색 고르기(불투명 색 → 팔레트 색 — 예: 정리의 색 거리·명암 계열 우선). null이면 redmean RGB 거리.</param>
    /// <param name="canMix">디더 짝 거르기(원래 색, 짝 후보 → 섞어도 되는지 — 예: 명암 계열 우선은 색상이 다른 유채색과 섞지 않음). null이면 모두.</param>
    public PaletteRemap(IEnumerable<Color32> palette, Func<Color32, Color32>? nearest = null, Func<Color32, Color32, bool>? canMix = null)
    {
        _nearest = nearest;
        _canMix = canMix;
        var seen = new HashSet<int>();
        var list = new List<int>();
        foreach (var c in palette)
        {
            if (c.A == 0) continue;
            int rgb = (int)(c.Argb & 0xFFFFFF);
            if (seen.Add(rgb)) list.Add(rgb);
        }
        if (list.Count == 0) throw new ArgumentException(Gpixel.Core.Localization.DomainStrings.Palette_NoOpaqueColors, nameof(palette));
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
                int to = dither != PaletteDither.None && !_exact.Contains(rgb) ? Dithered(rgb, x, y, dither) : NearestRgb(rgb);
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
    private int Dithered(int rgb, int x, int y, PaletteDither dither)
    {
        var (a, b, t) = Mix(rgb);
        float threshold = dither == PaletteDither.Bayer2
            ? (Bayer.Threshold2(x, y) + 0.5f) / 4f
            : (Bayer4x4[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
        return t > threshold ? b : a;
    }

    /// <summary>
    /// 색을 가장 잘 만드는 두 팔레트 색과 비율: A = 가장 가까운 색, B = OKLab로 가까운 몇 색(<see cref="MixCandidates"/>) 중 하나.
    /// 사람 눈은 이웃 픽셀을 빛의 세기(선형광)로 섞어 보므로, 비율 t마다 A·B를 선형광으로 섞은 색을 OKLab로 원래 색과 견준다.
    /// 거기에 무늬가 눈에 띄는 정도(<see cref="PatternWeight"/> × 2√(t(1−t)) × 밝기 차와 그보다 덜 보이는 색조·채도 차)를 더한 값이
    /// A 하나만 쓸 때의 오차보다 작을 때만 섞는다. A가 원래 색과 겨우 알아볼 차이(<see cref="JustNoticeable"/>) 안이면 섞어도 더 비슷해 보이지 않으므로
    /// 무늬를 만들지 않는다 — 거의 같은 색이 팔레트에 있으면 먼 색 점을 찍지 않는다.
    /// </summary>
    private (int A, int B, float T) Mix(int rgb)
    {
        if (_mixCache.TryGetValue(rgb, out var hit)) return hit;
        int a = NearestRgb(rgb);
        var src = OkLab.FromRgb(rgb >> 16 & 0xFF, rgb >> 8 & 0xFF, rgb & 0xFF);
        var (ar, ag, ab) = LinearOf(a);
        var aLab = OkLab.FromLinear(ar, ag, ab);
        float bestCost = Lab.Distance(src, aLab);
        int best = a;
        float bestT = 0;
        if (bestCost <= JustNoticeable) return _mixCache[rgb] = (a, a, 0);
        var near = _rgb.OrderBy(p => Lab.DistanceSquared(src, OkLab.FromRgb(p >> 16 & 0xFF, p >> 8 & 0xFF, p & 0xFF))).Take(MixCandidates);
        foreach (int p in near)
        {
            if (p == a || (_canMix is not null && !_canMix(new Color32(0xFF000000u | (uint)rgb), new Color32(0xFF000000u | (uint)p)))) continue;
            var (pr, pg, pb) = LinearOf(p);
            var pLab = OkLab.FromLinear(pr, pg, pb);
            float dl = aLab.L - pLab.L, da = aLab.A - pLab.A, db = aLab.B - pLab.B;
            float pattern = PatternWeight * MathF.Sqrt(dl * dl + ChromaVisibility * ChromaVisibility * (da * da + db * db));
            for (int k = 1; k < MixSteps; k++)
            {
                float t = k / (float)MixSteps;
                var mixed = OkLab.FromLinear(ar + (pr - ar) * t, ag + (pg - ag) * t, ab + (pb - ab) * t);
                float cost = Lab.Distance(src, mixed) + 2 * MathF.Sqrt(t * (1 - t)) * pattern;
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = p;
                bestT = t;
            }
        }
        return _mixCache[rgb] = (a, best, bestT);
    }

    /// <summary>섞을 짝으로 볼 OKLab로 가까운 색 수(A 포함).</summary>
    private const int MixCandidates = 6;
    /// <summary>섞는 비율을 볼 단계(Bayer 4×4의 16단계 — 2×2는 그중 가까운 단계로 찍힌다).</summary>
    private const int MixSteps = 16;
    /// <summary>원래 색과 겨우 알아볼 OKLab 차이 — 이 안이면 디더로 더 맞출 것이 없다(점수의 큰 면 색 바뀜과 같은 기준).</summary>
    private const float JustNoticeable = 0.02f;
    /// <summary>디더 무늬가 눈에 띄는 정도의 벌점 무게(무늬 두 색의 OKLab 차에 곱함).</summary>
    private const float PatternWeight = 0.2f;
    /// <summary>
    /// 무늬에서 색조·채도 차가 밝기 차보다 덜 보이는 정도 — 사람 눈은 색 변화를 밝기 변화보다 거칠게 본다(S-CIELAB, Zhang &amp; Wandell 1996).
    /// </summary>
    private const float ChromaVisibility = 0.5f;

    private static (float R, float G, float B) LinearOf(int rgb)
        => (OkLab.ToLinear(rgb >> 16 & 0xFF), OkLab.ToLinear(rgb >> 8 & 0xFF), OkLab.ToLinear(rgb & 0xFF));

    private int NearestRgb(int rgb)
    {
        if (_cache.TryGetValue(rgb, out int hit)) return hit;
        if (_nearest is not null) return _cache[rgb] = (int)(_nearest(new Color32(0xFF000000u | (uint)rgb)).Argb & 0xFFFFFF);
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
