using System.Drawing;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.Edit;

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

/// <summary>팔레트 색으로 바꿀 때 색 고르는 규칙(<see cref="PaletteRemap(IEnumerable{Color32}, PaletteMatchRule, PaletteMatchDistance, IEnumerable{Color32}?)"/>).</summary>
public enum PaletteMatchRule
{
    /// <summary>가장 가까운 색(<see cref="PaletteMatchDistance"/>로 잼).</summary>
    Nearest,
    /// <summary>
    /// 밝기 순서: 바꿀 그림의 서로 다른 색을 OKLab 밝기 순으로 줄 세우고, 그 순위 비율대로 밝기 순으로 늘어놓은 팔레트 색에 짝짓는다
    /// (가장 어두운 색 → 가장 어두운 팔레트 색, 가장 밝은 색 → 가장 밝은 팔레트 색). 색상이 아주 달라도 명암 구조가 남는다.
    /// </summary>
    BrightnessOrder,
    /// <summary>팔레트 순서: <see cref="BrightnessOrder"/>와 같은 순위를 팔레트에 놓인 차례(첫 색 = 가장 어두운 색 자리)에 짝짓는다 — 직접 늘어놓은 명암 줄.</summary>
    PaletteOrder,
    /// <summary>색상 먼저: 색상각이 가장 가까운 유채색 팔레트 색들 중에서 밝기가 가장 가까운 색. 무채색(흐린 색)은 무채색 팔레트 색 중 밝기로.</summary>
    HueFirst,
    /// <summary>밝기만: 색상·채도는 보지 않고 OKLab 밝기가 가장 가까운 색(흑백·단색 팔레트).</summary>
    LightnessOnly,
}

/// <summary>가장 가까운 색 규칙(<see cref="PaletteMatchRule.Nearest"/>)의 색 거리.</summary>
public enum PaletteMatchDistance
{
    /// <summary>가중 RGB 'redmean'(빠르고 예전과 같음).</summary>
    Rgb,
    /// <summary>OKLab 직선 거리(<see cref="ColorMetric.OkLab"/>).</summary>
    OkLab,
    /// <summary>CIEDE2000(<see cref="ColorDistanceMode.Ciede2000"/>).</summary>
    Ciede2000,
}

/// <summary>
/// 이미지 색을 팔레트 색으로 바꾼다. 투명이 아닌 픽셀마다 RGB를 팔레트의 투명이 아닌 색 중 고른 색의 RGB로 바꾸고 알파는 그대로 둔다
/// (완전 투명 픽셀은 건드리지 않음). 기본 규칙은 가장 가까운 색이고 거리는 가중 RGB 'redmean'(빨강이 많을수록 빨강 차이를, 적을수록 파랑 차이를 더 무겁게 보는
/// 사람 눈에 가까운 근사)이며, 같은 거리면 팔레트 앞쪽 색. 다른 규칙은 <see cref="PaletteMatchRule"/>. 색마다 결과를 기억해 같은 색은 한 번만 찾는다.
/// 가장 가까운 색 규칙이면 이미 팔레트에 있는 RGB는 디더링을 켜도 그대로다.
/// </summary>
public sealed class PaletteRemap
{
    private static readonly int[] Bayer4x4 = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    private readonly int[] _rgb;
    /// <summary>팔레트 색(<see cref="_rgb"/>와 같은 차례)의 OKLab.</summary>
    private readonly Lab[] _lab;
    private readonly HashSet<int> _exact;
    private readonly Dictionary<int, int> _cache = new();
    private readonly Dictionary<int, (int A, int B, float T)> _mixCache = new();
    private readonly Func<Color32, Color32>? _nearest;
    private readonly Func<Color32, Color32, bool>? _canMix;
    private readonly PaletteMatchRule _rule;
    /// <summary>가장 가까운 색 규칙의 OKLab·CIEDE2000 거리(null = redmean RGB).</summary>
    private readonly ColorMetric? _metric;
    private readonly ColorPoint[] _points = [];
    /// <summary>팔레트 번호를 밝기 순(같으면 팔레트 차례)으로.</summary>
    private readonly int[] _byLightness;
    /// <summary>순위 규칙이 짝지을 팔레트 번호 차례(밝기 순서 = <see cref="_byLightness"/>, 팔레트 순서 = 놓인 차례).</summary>
    private readonly int[] _order = [];
    /// <summary>순위 규칙: 바꿀 그림의 색(RGB) → 순위 비율 0~1(가장 어두운 색 0, 가장 밝은 색 1, 밝기가 같은 색은 같은 순위).</summary>
    private readonly Dictionary<int, float> _rank = new();
    /// <summary>순위 규칙: 바꿀 그림의 색 밝기(오름차순)와 그 순위 비율 — 목록에 없는 색은 밝기로 사이를 메운다.</summary>
    private readonly float[] _sourceL = [], _sourceRank = [];

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
        if (list.Count == 0) throw new ArgumentException(Chickxel.Core.Localization.DomainStrings.Palette_NoOpaqueColors, nameof(palette));
        _rgb = [.. list];
        _exact = seen;
        _lab = [.. list.Select(LabOf)];
        _byLightness = [.. Enumerable.Range(0, _rgb.Length).OrderBy(i => _lab[i].L).ThenBy(i => i)];
    }

    /// <summary>
    /// 고른 규칙으로 바꾸는 팔레트 색 고르기. 디더링은 규칙마다 가장 알맞은 두 색을 섞는다 — 가장 가까운 색은 원래 색을 가장 잘 만드는 두 색,
    /// 밝기만·색상 먼저는 (같은 색상 무리 안에서) 원래 밝기를 사이에 두는 어두운·밝은 두 색(빛의 세기로 섞는 비율), 순위 규칙은 순위 자리를 사이에 두는 두 팔레트 색.
    /// </summary>
    /// <param name="distance">가장 가까운 색 규칙의 색 거리(다른 규칙은 무시).</param>
    /// <param name="source">
    /// 순위 규칙(<see cref="RanksSource"/>)이 줄 세울 바꿀 그림의 색들(투명은 무시, 알파는 보지 않음 — <see cref="AddOpaqueColors"/>로 모음).
    /// 목록에 없는 색은 목록 색 밝기 사이를 메운 순위이고, 목록 색이 두 개 미만이면 팔레트 밝기 범위 안의 자리로 짝짓는다.
    /// </param>
    public PaletteRemap(IEnumerable<Color32> palette, PaletteMatchRule rule, PaletteMatchDistance distance = PaletteMatchDistance.Rgb, IEnumerable<Color32>? source = null)
        : this(palette)
    {
        _rule = rule;
        if (rule == PaletteMatchRule.Nearest && distance != PaletteMatchDistance.Rgb)
        {
            _metric = distance == PaletteMatchDistance.Ciede2000 ? new ColorMetric(ColorDistanceMode.Ciede2000, 1) : ColorMetric.OkLab;
            _points = [.. _rgb.Select(p => _metric.Point(new Color32(0xFF000000u | (uint)p)))];
        }
        if (!RanksSource(rule)) return;
        _order = rule == PaletteMatchRule.BrightnessOrder ? _byLightness : Enumerable.Range(0, _rgb.Length).ToArray();
        var colors = new HashSet<int>();
        foreach (var c in source ?? Enumerable.Empty<Color32>())
            if (c.A != 0) colors.Add((int)(c.Argb & 0xFFFFFF));
        var sorted = colors.Select(rgb => (Rgb: rgb, LabOf(rgb).L)).OrderBy(s => s.L).ThenBy(s => s.Rgb).ToArray();
        if (sorted.Length < 2) return;
        // 밝기가 같은 색들은 그 자리들의 가운데 순위를 함께 쓴다.
        var ls = new List<float>();
        var ranks = new List<float>();
        for (int i = 0; i < sorted.Length;)
        {
            int j = i;
            while (j + 1 < sorted.Length && sorted[j + 1].L - sorted[i].L <= SameLightness) j++;
            float rank = (i + j) / 2f / (sorted.Length - 1);
            for (int k = i; k <= j; k++) _rank[sorted[k].Rgb] = rank;
            ls.Add(sorted[i].L);
            ranks.Add(rank);
            i = j + 1;
        }
        if (ls.Count < 2)
        {
            _rank.Clear();
            return;
        }
        _sourceL = [.. ls];
        _sourceRank = [.. ranks];
    }

    /// <summary>바꿀 그림의 색들을 보고 짝짓는 규칙(밝기 순서·팔레트 순서) — 이때만 <c>source</c>가 쓰인다.</summary>
    public static bool RanksSource(PaletteMatchRule rule) => rule is PaletteMatchRule.BrightnessOrder or PaletteMatchRule.PaletteOrder;

    /// <summary>투명이 아닌 픽셀 색(불투명으로 모음)을 <paramref name="into"/>에 더한다(<paramref name="clip"/>이 있으면 그 안만) — 순위 규칙의 <c>source</c>.</summary>
    public static void AddOpaqueColors(PixelImage image, ISet<Color32> into, PixelMask? clip = null)
    {
        var area = Rectangle.Intersect(clip?.Bounds ?? image.Bounds, image.Bounds);
        var px = image.Pixels;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                if (clip is { IsRectangle: false } m && !m.Contains(x, y)) continue;
                var c = px[y * image.Width + x];
                if (c.A != 0) into.Add(new Color32(c.Argb | 0xFF000000u));
            }
    }

    /// <summary>쓰는 팔레트 색 수(투명·겹치는 색 제외).</summary>
    public int Count => _rgb.Length;

    /// <summary>고른 규칙의 팔레트 색(디더 없음, 알파는 <paramref name="color"/>의 알파). 완전 투명은 그대로.</summary>
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
                int to = dither == PaletteDither.None ? NearestRgb(rgb)
                    : _rule == PaletteMatchRule.Nearest && _exact.Contains(rgb) ? rgb
                    : Dithered(rgb, x, y, dither);
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

    /// <summary>디더에서 섞을 두 팔레트 색(RGB)과 B 비율 — 가장 가까운 색 규칙은 <see cref="NearestMix"/>, 다른 규칙은 <see cref="Pick"/>의 두 색.</summary>
    private (int A, int B, float T) Mix(int rgb)
    {
        if (_mixCache.TryGetValue(rgb, out var hit)) return hit;
        if (_rule == PaletteMatchRule.Nearest) return _mixCache[rgb] = NearestMix(rgb);
        var (lo, hi, t, _) = Pick(rgb);
        return _mixCache[rgb] = (_rgb[lo], _rgb[hi], t);
    }

    /// <summary>
    /// 색을 가장 잘 만드는 두 팔레트 색과 비율: A = 가장 가까운 색, B = OKLab로 가까운 몇 색(<see cref="MixCandidates"/>) 중 하나.
    /// 사람 눈은 이웃 픽셀을 빛의 세기(선형광)로 섞어 보므로, 비율 t마다 A·B를 선형광으로 섞은 색을 OKLab로 원래 색과 견준다.
    /// 거기에 무늬가 눈에 띄는 정도(<see cref="PatternWeight"/> × 2√(t(1−t)) × 밝기 차와 그보다 덜 보이는 색조·채도 차)를 더한 값이
    /// A 하나만 쓸 때의 오차보다 작을 때만 섞는다. A가 원래 색과 겨우 알아볼 차이(<see cref="JustNoticeable"/>) 안이면 섞어도 더 비슷해 보이지 않으므로
    /// 무늬를 만들지 않는다 — 거의 같은 색이 팔레트에 있으면 먼 색 점을 찍지 않는다.
    /// </summary>
    private (int A, int B, float T) NearestMix(int rgb)
    {
        int a = NearestRgb(rgb);
        var src = OkLab.FromRgb(rgb >> 16 & 0xFF, rgb >> 8 & 0xFF, rgb & 0xFF);
        var (ar, ag, ab) = LinearOf(a);
        var aLab = OkLab.FromLinear(ar, ag, ab);
        float bestCost = Lab.Distance(src, aLab);
        int best = a;
        float bestT = 0;
        if (bestCost <= JustNoticeable) return (a, a, 0);
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
        return (a, best, bestT);
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

    private static Lab LabOf(int rgb) => OkLab.FromRgb(rgb >> 16 & 0xFF, rgb >> 8 & 0xFF, rgb & 0xFF);

    private static Color32 Opaque(int rgb) => new(0xFF000000u | (uint)rgb);

    /// <summary>순위를 같게 볼 OKLab 밝기 차(눈으로 가를 수 없는 차이).</summary>
    private const float SameLightness = 1e-4f;
    /// <summary>색상 먼저: 가장 가까운 색상각보다 이만큼(도) 더 떨어진 색까지 같은 무리로 보고 그 안에서 밝기로 고른다.</summary>
    private const double HueTolerance = ColorDistance.ShadeMaxHue;

    /// <summary>고른 규칙의 팔레트 색(RGB, 디더 없음).</summary>
    private int NearestRgb(int rgb)
    {
        if (_cache.TryGetValue(rgb, out int hit)) return hit;
        int best = _nearest is not null ? (int)(_nearest(Opaque(rgb)).Argb & 0xFFFFFF)
            : _metric is not null ? _rgb[_metric.Nearest(_metric.Point(Opaque(rgb)), _points)]
            : _rule == PaletteMatchRule.Nearest ? NearestRedmean(rgb)
            : _rgb[Pick(rgb).Nearest];
        _cache[rgb] = best;
        return best;
    }

    private int NearestRedmean(int rgb)
    {
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
        return best;
    }

    /// <summary>
    /// 가장 가까운 색 밖의 규칙이 고른 두 팔레트 번호(밝기 규칙은 어두운 쪽 Lo·밝은 쪽 Hi, 순위 규칙은 차례의 앞·뒤), 디더에서 Hi를 쓸 비율 T, 디더 없이 쓸 번호.
    /// 순위 규칙: 자리 p = 순위 비율 × (팔레트 색 수 − 1), Lo·Hi = p의 내림·올림, T = p의 소수 부분, 디더 없으면 반올림.
    /// 밝기만·색상 먼저: <see cref="Bracket"/>.
    /// </summary>
    private (int Lo, int Hi, float T, int Nearest) Pick(int rgb)
    {
        var lab = LabOf(rgb);
        switch (_rule)
        {
            case PaletteMatchRule.BrightnessOrder or PaletteMatchRule.PaletteOrder:
                float p = RankOf(rgb, lab.L) * (_order.Length - 1);
                int lo = Math.Min((int)p, _order.Length - 1), hi = Math.Min(lo + 1, _order.Length - 1);
                float f = p - lo;
                return (_order[lo], _order[hi], f, _order[f >= 0.5f ? hi : lo]);
            case PaletteMatchRule.HueFirst:
                return Bracket(HueGroup(lab), lab.L);
            default:
                return Bracket(_byLightness, lab.L);
        }
    }

    /// <summary>순위 비율(0~1): 바꿀 그림의 색이면 그 순위, 아니면 그림 색 밝기 사이를 메운 값, 그림 색이 두 개 미만이면 팔레트 밝기 범위 안의 자리.</summary>
    private float RankOf(int rgb, float l)
    {
        if (_rank.TryGetValue(rgb, out float rank)) return rank;
        if (_sourceL.Length >= 2)
        {
            if (l <= _sourceL[0]) return _sourceRank[0];
            for (int i = 1; i < _sourceL.Length; i++)
                if (l <= _sourceL[i])
                    return _sourceRank[i - 1] + (_sourceRank[i] - _sourceRank[i - 1]) * (l - _sourceL[i - 1]) / (_sourceL[i] - _sourceL[i - 1]);
            return _sourceRank[^1];
        }
        float min = _lab[_byLightness[0]].L, max = _lab[_byLightness[^1]].L;
        return max - min > SameLightness ? Math.Clamp((l - min) / (max - min), 0f, 1f) : 0f;
    }

    /// <summary>
    /// 밝기 순 팔레트 번호 <paramref name="sorted"/>에서 밝기 <paramref name="l"/>을 사이에 두는 어두운 색 Lo와 밝은 색 Hi(밝기가 같은 색이 여럿이면 팔레트 앞쪽),
    /// 디더 비율 T = 빛의 세기(OKLab L³)로 Lo·Hi를 섞어 원래 밝기가 되는 Hi 비율, 디더 없으면 밝기가 더 가까운 쪽(같으면 어두운 쪽). 범위 밖이면 끝 색 하나.
    /// </summary>
    private (int Lo, int Hi, float T, int Nearest) Bracket(IReadOnlyList<int> sorted, float l)
    {
        int lo = -1, hi = -1;
        foreach (int i in sorted)
        {
            if (_lab[i].L > l)
            {
                hi = i;
                break;
            }
            if (lo < 0 || _lab[i].L > _lab[lo].L) lo = i;
        }
        if (lo < 0) return (hi, hi, 0, hi);
        if (hi < 0 || _lab[lo].L == l) return (lo, lo, 0, lo);
        float yl = Cube(_lab[lo].L), yh = Cube(_lab[hi].L);
        float t = Math.Clamp((Cube(l) - yl) / (yh - yl), 0f, 1f);
        return (lo, hi, t, l - _lab[lo].L <= _lab[hi].L - l ? lo : hi);

        static float Cube(float v) => v * v * v;
    }

    /// <summary>
    /// 색상 먼저의 후보(밝기 순): 유채색(OKLCh 채도 ≥ <see cref="ColorDistance.ShadeMinChroma"/>)이면 색상각이 가장 가까운 유채색 팔레트 색과
    /// 그보다 <see cref="HueTolerance"/>도 안쪽으로 더 떨어진 유채색들, 무채색이면 무채색 팔레트 색들. 후보가 없으면(유채색·무채색 팔레트 색이 없음) 모든 색.
    /// </summary>
    private IReadOnlyList<int> HueGroup(in Lab lab)
    {
        bool gray = ColorDistance.Chroma(lab) < ColorDistance.ShadeMinChroma;
        double closest = double.MaxValue;
        if (!gray)
            foreach (int i in _byLightness)
                if (!IsGray(i)) closest = Math.Min(closest, ColorDistance.HueDifference(lab, _lab[i]));
        var group = new List<int>();
        foreach (int i in _byLightness)
            if (gray ? IsGray(i) : !IsGray(i) && ColorDistance.HueDifference(lab, _lab[i]) <= closest + HueTolerance)
                group.Add(i);
        return group.Count > 0 ? group : _byLightness;
    }

    private bool IsGray(int i) => ColorDistance.Chroma(_lab[i]) < ColorDistance.ShadeMinChroma;

    /// <summary>redmean 가중 RGB 거리의 제곱(×256).</summary>
    private static long Distance(int r1, int g1, int b1, int r2, int g2, int b2)
    {
        int rm = (r1 + r2) >> 1;
        int dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
        return (long)(512 + rm) * dr * dr + 1024L * dg * dg + (long)(767 - rm) * db * db;
    }
}
