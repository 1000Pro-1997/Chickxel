using Chickxel.Core.Model;

namespace Chickxel.Core.Pixelate;

/// <summary>
/// 색 줄이기용 팔레트 만들기: OKLab k-평균(가장 많이 쓰인 색에서 시작해 가장 먼 색을 더하는 정해진 초기값 — 같은 입력이면 늘 같은 결과)과
/// RGB 중앙 분할. 색 수 자동은 k = 2~32의 k-평균 오차 곡선에서 팔꿈치(정규화한 곡선이 대각선에서 가장 먼 점).
/// 묶음 색은 평균(<see cref="PaletteColorSource.Mean"/>) 또는 묶음 안 실제 칸 색 중 가장 많이 쓰인 색(<see cref="PaletteColorSource.Original"/>,
/// <see cref="WeightedHistogram"/>의 칸 색을 넘기면).
/// </summary>
public static class PaletteBuilder
{
    public const int MaxColors = 64;
    public const int AutoMaxColors = 32;

    /// <summary>색 → 쓰인 수(RGB 5비트로 묶은 평균 색 — 비슷한 색이 많아도 빠름).</summary>
    public static List<(Color32 Color, double Count)> Histogram(IEnumerable<PixelImage> images)
    {
        var bins = new Dictionary<int, (long R, long G, long B, int N)>();
        foreach (var img in images)
            foreach (var c in img.Pixels)
            {
                if (c.A == 0) continue;
                int key = BinKey(c);
                var b = bins.GetValueOrDefault(key);
                bins[key] = (b.R + c.R, b.G + c.G, b.B + c.B, b.N + 1);
            }
        return bins.OrderBy(kv => kv.Key)
            .Select(kv => (new Color32((byte)(kv.Value.R / kv.Value.N), (byte)(kv.Value.G / kv.Value.N), (byte)(kv.Value.B / kv.Value.N)), (double)kv.Value.N))
            .ToList();
    }

    private static int BinKey(Color32 c) => (c.R >> 3) << 10 | (c.G >> 3) << 5 | c.B >> 3;

    /// <summary>섞인 칸: 마주 보는 두 이웃 색을 잇는 선분에서 이만큼(OKLab) 안이고</summary>
    private const float BlendNear = 0.03f;
    /// <summary>두 이웃 어느 쪽과도 이만큼 이상 떨어진 칸.</summary>
    private const float BlendFar = 0.05f;
    /// <summary>실제 칸 색 고르기: 묶음 안에서 이 거리(OKLab) 안의 색이 가장 많이 모인 곳.</summary>
    private const float ModeRadius = 0.04f;

    /// <summary>
    /// 원본 색용 히스토그램: <see cref="Histogram"/>처럼 RGB 5비트로 묶되 칸마다 무게를 준다 — 외곽선(밝기 ≤ <paramref name="darkness"/>) 칸은 1,
    /// 그보다 밝은 칸 중 가장자리(상하좌우에 투명)·섞인 칸(좌우 또는 상하 이웃 색 사이의 중간 색 — 외곽선과 칠 사이에 번진 칸도)은
    /// <paramref name="edgeWeight"/>, 나머지는 1. 외곽선과 맞닿기만 한 칸은 줄이지 않는다(작은 스프라이트는 칸 대부분이 외곽선 옆이라
    /// 외곽선 안쪽의 진짜 그늘 색까지 묻힘). 묶음마다 그 안의 실제 칸 색과 무게(같은 색은 합침)도 돌려준다. 무게가 모두 0이면 무게 없이(모두 1).
    /// </summary>
    public static (List<(Color32 Color, double Count)> Bins, List<(Color32 Color, double Weight)>[] Members) WeightedHistogram(
        IEnumerable<PixelImage> images, double edgeWeight, double darkness)
    {
        var list = images as IReadOnlyList<PixelImage> ?? images.ToList();
        var result = Weighted(list, Math.Clamp(edgeWeight, 0, 1), darkness);
        return result.Bins.Count > 0 || edgeWeight >= 1 ? result : Weighted(list, 1, darkness);
    }

    private static (List<(Color32 Color, double Count)> Bins, List<(Color32 Color, double Weight)>[] Members) Weighted(
        IReadOnlyList<PixelImage> images, double edgeWeight, double darkness)
    {
        var bins = new SortedDictionary<int, Dictionary<uint, double>>();
        foreach (var img in images)
        {
            int w = img.Width, h = img.Height;
            var px = img.Pixels;
            var lab = new Lab[px.Length];
            var dark = new bool[px.Length];
            for (int i = 0; i < px.Length; i++)
                if (px[i].A != 0)
                {
                    lab[i] = OkLab.FromColor(px[i]);
                    dark[i] = lab[i].L <= darkness;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (px[i].A == 0) continue;
                    double weight = 1;
                    if (!dark[i] && edgeWeight < 1)
                    {
                        int l = x > 0 ? i - 1 : -1, r = x < w - 1 ? i + 1 : -1, u = y > 0 ? i - w : -1, d = y < h - 1 ? i + w : -1;
                        bool down = false;
                        foreach (int n in (ReadOnlySpan<int>)[l, r, u, d])
                            if (n < 0 || px[n].A == 0) { down = true; break; }
                        if (!down) down = IsBlend(lab[i], lab[l], lab[r]) || IsBlend(lab[i], lab[u], lab[d]);
                        if (down) weight = edgeWeight;
                    }
                    if (weight <= 0) continue;
                    var c = px[i].WithAlpha(255);
                    int key = BinKey(c);
                    if (!bins.TryGetValue(key, out var members)) bins[key] = members = [];
                    members[c.Argb] = members.GetValueOrDefault(c.Argb) + weight;
                }
        }
        var hist = new List<(Color32 Color, double Count)>(bins.Count);
        var exact = new List<(Color32 Color, double Weight)>[bins.Count];
        int at = 0;
        foreach (var members in bins.Values)
        {
            var sorted = members.OrderBy(kv => kv.Key).Select(kv => (new Color32(kv.Key), kv.Value)).ToList();
            double r = 0, g = 0, b = 0, n = 0;
            foreach (var (c, wt) in sorted) { r += c.R * wt; g += c.G * wt; b += c.B * wt; n += wt; }
            hist.Add((new Color32((byte)Math.Round(r / n), (byte)Math.Round(g / n), (byte)Math.Round(b / n)), n));
            exact[at++] = sorted;
        }
        return (hist, exact);
    }

    /// <summary>
    /// <paramref name="p"/>가 두 이웃 <paramref name="a"/>·<paramref name="b"/>의 중간 색인지: 선분 a–b에서 <see cref="BlendNear"/> 안이고
    /// 양쪽과 <see cref="BlendFar"/> 이상 떨어짐(번진 경계에 생긴 색).
    /// </summary>
    private static bool IsBlend(in Lab p, in Lab a, in Lab b)
    {
        float da = Lab.Distance(p, a), db = Lab.Distance(p, b);
        if (Math.Min(da, db) < BlendFar) return false;
        float abl = b.L - a.L, aba = b.A - a.A, abb = b.B - a.B;
        float len2 = abl * abl + aba * aba + abb * abb;
        if (len2 <= 1e-9f) return false;
        float t = ((p.L - a.L) * abl + (p.A - a.A) * aba + (p.B - a.B) * abb) / len2;
        if (t <= 0 || t >= 1) return false;
        var q = new Lab(a.L + t * abl, a.A + t * aba, a.B + t * abb);
        return Lab.Distance(p, q) <= BlendNear;
    }

    /// <summary>
    /// 두드러진 색 지키기: 자동 색 수는 쓰인 픽셀의 이 비율 이상이 가장 가까운 팔레트 색에서 <see cref="TailLimit"/> 안에 들 때까지 늘린다
    /// (넓은 면만 보면 작은 빨간 목도리·초록 물약 같은 색이 묻히므로).
    /// </summary>
    private const double TailShare = 0.98;
    private const double TailLimit = 0.04;

    /// <summary>
    /// k-평균 팔레트와 가중 평균 오차(OKLab 거리). <paramref name="members"/>(<see cref="WeightedHistogram"/>의 칸 색)를 주면
    /// 묶음마다 평균 대신 실제 칸 색(<see cref="Representative"/>).
    /// </summary>
    public static (List<Color32> Palette, double Error) KMeans(IReadOnlyList<(Color32 Color, double Count)> hist, int k,
        IReadOnlyList<List<(Color32 Color, double Weight)>>? members = null, ColorMetric? metric = null)
    {
        var pts = hist.Select(h => OkLab.FromColor(h.Color)).ToArray();
        var (centers, assign) = KMeansLab(pts, ClusterWeights(hist), k);
        var (mean, _) = Errors(pts, hist, centers, assign);
        if (members is null) return (centers.Select(c => OkLab.ToColor(c)).Distinct().ToList(), mean);
        var groups = Enumerable.Range(0, centers.Length).Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < assign.Length; i++) groups[assign[i]].Add(i);
        return (groups.Where(g => g.Count > 0).Select(g => Representative(g, pts, hist, members, metric)).Distinct().ToList(), mean);
    }

    /// <summary>넓게 쓰인 색: 둘레(<see cref="ModeRadius"/>) 무게가 전체의 이 비율 이상인 색 무리.</summary>
    private const double MajorShare = 0.01;
    /// <summary>넓게 쓰인 색이 가장 가까운 팔레트 색과 이보다 멀면(OKLab — 겨우 보이는 차이) 그 색을 더한다.</summary>
    private const float MajorLimit = 0.02f;

    /// <summary>
    /// 넓게 쓰인 색 지키기: 히스토그램에서 둘레 무게가 <see cref="MajorShare"/> 이상인 색 무리의 봉우리마다(무게 큰 것부터) 그 색(실제 칸 색 —
    /// <see cref="Representative"/>, 칸 색이 없으면 둘레 평균)이 팔레트의 가장 가까운 색과 <see cref="MajorLimit"/>보다 멀면 팔레트에 더한다(최대 <paramref name="max"/>색).
    /// k-평균은 묶음 무게를 제곱근으로 줄이고 가장 먼 색부터 시작하므로, 신발의 밝은 갈색처럼 넓지만 다른 색(금색 장식) 사이에 있는 색이
    /// 한 묶음으로 섞여 중간 색(올리브빛)이 되는 일이 있다.
    /// </summary>
    public static List<Color32> KeepMajorColors(List<Color32> palette, IReadOnlyList<(Color32 Color, double Count)> hist,
        IReadOnlyList<List<(Color32 Color, double Weight)>>? members, int max = MaxColors, ColorMetric? metric = null)
    {
        if (hist.Count == 0 || palette.Count >= max) return palette;
        var pts = hist.Select(h => OkLab.FromColor(h.Color)).ToArray();
        double total = hist.Sum(h => h.Count);
        const float r2 = ModeRadius * ModeRadius;
        var density = new double[pts.Length];
        for (int i = 0; i < pts.Length; i++)
            for (int j = 0; j < pts.Length; j++)
                if (Lab.DistanceSquared(pts[i], pts[j]) <= r2) density[i] += hist[j].Count;
        var peaks = Enumerable.Range(0, pts.Length)
            .Where(i => density[i] >= total * MajorShare && Enumerable.Range(0, pts.Length).All(j => j == i || Lab.DistanceSquared(pts[i], pts[j]) > r2
                || density[j] < density[i] || (density[j] == density[i] && j > i)))
            .OrderByDescending(i => density[i]).ThenBy(i => i).ToList();
        var result = new List<Color32>(palette);
        var labs = palette.Select(OkLab.FromColor).ToList();
        foreach (int p in peaks)
        {
            if (result.Count >= max) break;
            var near = Enumerable.Range(0, pts.Length).Where(j => Lab.DistanceSquared(pts[p], pts[j]) <= r2).ToList();
            Color32 c;
            if (members is not null) c = Representative(near, pts, hist, members, metric);
            else
            {
                double sl = 0, sa = 0, sb = 0, sw = 0;
                foreach (int j in near) { sl += pts[j].L * hist[j].Count; sa += pts[j].A * hist[j].Count; sb += pts[j].B * hist[j].Count; sw += hist[j].Count; }
                c = OkLab.ToColor(new Lab((float)(sl / sw), (float)(sa / sw), (float)(sb / sw)));
            }
            var lab = OkLab.FromColor(c);
            if (labs.Count > 0 && labs.Min(l => Lab.Distance(l, lab)) <= MajorLimit) continue;
            result.Add(c);
            labs.Add(lab);
        }
        return result;
    }

    /// <summary>
    /// 묶음(<paramref name="group"/> = 히스토그램 번호)의 실제 칸 색: 묶음 안에서 <see cref="ModeRadius"/>(OKLab) 안의 무게가 가장 큰 곳(색이 가장 많이 모인 곳)을 찾고,
    /// 그 둘레의 칸 색 중 (무게 × 둘레 평균에 가까운 정도 — 가까움은 <paramref name="metric"/>, 없으면 OKLab)가 가장 큰 색 — 같은 색이 많이 쓰였으면 그 색, 모두 한 번씩이면 둘레 평균에 가장 가까운 색.
    /// 평균과 달리 번진 중간 색에 끌리지 않고 늘 실제로 쓰인 색.
    /// </summary>
    private static Color32 Representative(List<int> group, Lab[] pts, IReadOnlyList<(Color32 Color, double Count)> hist,
        IReadOnlyList<List<(Color32 Color, double Weight)>> members, ColorMetric? metric = null)
    {
        metric ??= ColorMetric.OkLab;
        const float r2 = ModeRadius * ModeRadius;
        int peak = group[0];
        double peakDensity = -1;
        foreach (int i in group)
        {
            double density = 0;
            foreach (int j in group)
                if (Lab.DistanceSquared(pts[i], pts[j]) <= r2) density += hist[j].Count;
            if (density > peakDensity + 1e-9 || (Math.Abs(density - peakDensity) <= 1e-9 && hist[i].Count > hist[peak].Count))
                (peak, peakDensity) = (i, density);
        }
        var near = group.Where(j => Lab.DistanceSquared(pts[peak], pts[j]) <= r2).ToList();
        double sl = 0, sa = 0, sb = 0, sw = 0;
        foreach (int j in near)
            foreach (var (c, w) in members[j])
            {
                var lab = OkLab.FromColor(c);
                sl += lab.L * w; sa += lab.A * w; sb += lab.B * w; sw += w;
            }
        var center = metric.Point(new Lab((float)(sl / sw), (float)(sa / sw), (float)(sb / sw)));
        Color32 best = members[peak][0].Color;
        double bestScore = -1, bestD = double.MaxValue;
        foreach (int j in near)
            foreach (var (c, w) in members[j])
            {
                double dist = metric.Distance(metric.Point(c), center);
                double d = dist * dist;
                double score = w * Math.Exp(-d / r2);
                if (score > bestScore + 1e-12 || (Math.Abs(score - bestScore) <= 1e-12 && d < bestD)) (best, bestScore, bestD) = (c, score, d);
            }
        return best;
    }

    /// <summary>다른 색상 지키기: 한 색 무리로 모을 거리(OKLab) — 씨앗 색에서 이 안이고 색상 차가 문턱의 반 안인 색.</summary>
    private const float HueClusterRadius = 0.08f;
    /// <summary>다른 색상 지키기에서 더할 최대 색 수(바꾸기는 팔레트의 ¼까지, 적어도 1색 — 작은 팔레트를 통째로 바꾸지 않게).</summary>
    private const int MaxDistinctHues = 8;

    /// <summary>
    /// 다른 색상 지키기: 히스토그램 색 중 채도 ≥ <paramref name="minChroma"/>이고, <paramref name="metric"/>로 가장 가까운 팔레트 색과 색상이
    /// <paramref name="minHue"/>도 이상 다른(그 팔레트 색이 거의 무채색이면 다른 것으로 봄) 색들을 무리(무게 큰 색을 씨앗으로 <see cref="HueClusterRadius"/>·색상 차 ½ 문턱 안)로 묶어,
    /// 무리 무게 ≥ <paramref name="minArea"/>이면 그 색(실제 칸 색 — <see cref="Representative"/>, 칸 색이 없으면 무게 평균)을 넣는다.
    /// <paramref name="replace"/>가 거짓이면 더하고(최대 <paramref name="max"/>색), 참이면(고른 색 수) 이 단계에서 넣은 색을 빼고
    /// 가장 덜 중요한 색 — 그 색으로 가는 색들이 다음으로 가까운 색으로 갈 때 늘어나는 (무게 × 거리) 합이 가장 작은 색 — 과 바꾼다(팔레트의 ¼까지, 적어도 1색).
    /// </summary>
    public static List<Color32> KeepDistinctHues(List<Color32> palette, IReadOnlyList<(Color32 Color, double Count)> hist,
        IReadOnlyList<List<(Color32 Color, double Weight)>>? members, bool replace, ColorMetric metric,
        double minChroma, double minHue, double minArea, int max = MaxColors)
    {
        if (hist.Count == 0 || palette.Count == 0) return palette;
        var pts = hist.Select(h => OkLab.FromColor(h.Color)).ToArray();
        var cps = pts.Select(p => metric.Point(p)).ToArray();
        var result = new List<Color32>(palette);
        var locked = new HashSet<int>();
        int rounds = replace ? Math.Max(1, palette.Count / 4) : MaxDistinctHues;
        for (int round = 0; round < rounds; round++)
        {
            var pal = result.Select(metric.Point).ToArray();
            var nearest = new int[pts.Length];
            var candidates = new List<int>();
            for (int i = 0; i < pts.Length; i++)
            {
                nearest[i] = metric.Nearest(cps[i], pal);
                if (ColorDistance.Chroma(pts[i]) < minChroma) continue;
                var p = pal[nearest[i]].Ok;
                if ((ColorDistance.Chroma(p) < minChroma / 2 || ColorDistance.HueDifference(pts[i], p) >= minHue) && !IsPaletteBlend(pts[i], pal, nearest[i]))
                    candidates.Add(i);
            }
            // 무리: 후보마다 씨앗으로 둘레를 모아 무게가 가장 큰 무리(같으면 앞 번호).
            List<int>? cluster = null;
            double clusterArea = minArea - 1e-9;
            foreach (int seed in candidates)
            {
                var near = candidates.Where(j => Lab.Distance(pts[seed], pts[j]) <= HueClusterRadius && ColorDistance.HueDifference(pts[seed], pts[j]) < minHue / 2).ToList();
                double area = near.Sum(j => hist[j].Count);
                if (area > clusterArea) (cluster, clusterArea) = (near, area);
            }
            if (cluster is null) break;
            Color32 add;
            if (members is not null) add = Representative(cluster, pts, hist, members, metric);
            else
            {
                double sl = 0, sa = 0, sb = 0, sw = 0;
                foreach (int j in cluster) { sl += pts[j].L * hist[j].Count; sa += pts[j].A * hist[j].Count; sb += pts[j].B * hist[j].Count; sw += hist[j].Count; }
                add = OkLab.ToColor(new Lab((float)(sl / sw), (float)(sa / sw), (float)(sb / sw)));
            }
            if (result.Contains(add)) break;
            if (!replace)
            {
                if (result.Count >= max) break;
                result.Add(add);
                locked.Add(result.Count - 1);
                continue;
            }
            // 지울 색: 넣을 색을 더한 팔레트에서, 그 색으로 가는 칸들이 다음으로 가까운 색으로 갈 때 늘어나는 거리 × 무게의 합이 가장 작은 색.
            // 지우면 다른 색상이 없어지는(그 색으로 가는 유채색이 다음 색과 색상 문턱 이상 다름 — 넓이 ≥ 문턱) 색은 되도록 지우지 않는다.
            var ext = pal.Append(metric.Point(add)).ToArray();
            var cost = new double[pal.Length];
            var hueLoss = new double[pal.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                int n1 = -1, n2 = -1;
                double d1 = double.MaxValue, d2 = double.MaxValue;
                for (int q = 0; q < ext.Length; q++)
                {
                    double d = metric.Distance(cps[i], ext[q]);
                    if (d < d1) { (n2, d2) = (n1, d1); (n1, d1) = (q, d); }
                    else if (d < d2) (n2, d2) = (q, d);
                }
                if (n1 >= pal.Length || n2 < 0) continue;
                cost[n1] += hist[i].Count * (d2 - d1);
                if (ColorDistance.Chroma(pts[i]) >= minChroma && ColorDistance.HueDifference(pts[i], ext[n2].Ok) >= minHue
                    && ColorDistance.HueDifference(pts[i], ext[n1].Ok) < minHue)
                    hueLoss[n1] += hist[i].Count;
            }
            int drop = -1;
            for (int pass = 0; pass < 2 && drop < 0; pass++)
                for (int q = 0; q < pal.Length; q++)
                    if (!locked.Contains(q) && (pass == 1 || hueLoss[q] < minArea) && (drop < 0 || cost[q] < cost[drop])) drop = q;
            if (drop < 0) break;
            result[drop] = add;
            locked.Add(drop);
        }
        return result;
    }

    /// <summary>다른 색상 지키기: 두 팔레트 색을 잇는 선분(OKLab)에서 이만큼 안이면 두 색이 섞인(번진) 색으로 보고 넣지 않는다.</summary>
    private const float PaletteBlendNear = 0.02f;

    /// <summary>
    /// <paramref name="c"/>가 바뀔 팔레트 색(<paramref name="near"/>)과 다른 팔레트 색 사이의 중간 색인지: 선분 위 10~90% 자리에서 <see cref="PaletteBlendNear"/> 안
    /// (흰 옷과 보라 단 사이의 번진 연보라가 흰색으로 가는 것처럼 — 두 색 사이에 번진 색이 한쪽으로 가는 것이라 빠진 색상이 아님).
    /// 바뀔 색이 끝점이 아닌 쌍은 보지 않는다(살구색 그늘이 우연히 살색–빨강 사이에 놓여도 금색으로 가면 빠진 색상).
    /// </summary>
    private static bool IsPaletteBlend(in Lab c, ColorPoint[] pal, int near)
    {
        var a = pal[near].Ok;
        for (int j = 0; j < pal.Length; j++)
        {
            if (j == near) continue;
            var b = pal[j].Ok;
            float dl = b.L - a.L, da = b.A - a.A, db = b.B - a.B;
            float len2 = dl * dl + da * da + db * db;
            if (len2 <= 1e-9f) continue;
            float t = ((c.L - a.L) * dl + (c.A - a.A) * da + (c.B - a.B) * db) / len2;
            if (t < 0.1f || t > 0.9f) continue;
            if (Lab.Distance(c, new Lab(a.L + t * dl, a.A + t * da, a.B + t * db)) <= PaletteBlendNear) return true;
        }
        return false;
    }

    /// <summary>묶음 무게 = 쓰인 수의 제곱근(넓은 면이 작은 면의 색을 덮지 않게).</summary>
    private static double[] ClusterWeights(IReadOnlyList<(Color32 Color, double Count)> hist) => hist.Select(h => Math.Sqrt(h.Count)).ToArray();

    /// <summary>쓰인 수로 가중한 평균 거리와 <see cref="TailShare"/> 분위 거리.</summary>
    private static (double Mean, double Tail) Errors(Lab[] pts, IReadOnlyList<(Color32 Color, double Count)> hist, Lab[] centers, int[] assign)
    {
        var d = new (double D, double N)[pts.Length];
        double sum = 0, total = 0;
        for (int i = 0; i < pts.Length; i++)
        {
            double v = Math.Sqrt(Lab.DistanceSquared(pts[i], centers[assign[i]]));
            d[i] = (v, hist[i].Count);
            sum += v * hist[i].Count;
            total += hist[i].Count;
        }
        if (total <= 0) return (0, 0);
        Array.Sort(d, (a, b) => a.D.CompareTo(b.D));
        double acc = 0, tail = 0;
        foreach (var (v, n) in d)
        {
            acc += n;
            tail = v;
            if (acc >= total * TailShare) break;
        }
        return (sum / total, tail);
    }

    private static (Lab[] Centers, int[] Assign) KMeansLab(Lab[] pts, double[] wts, int k)
    {
        int n = pts.Length;
        if (n == 0) return ([], []);
        k = Math.Clamp(k, 1, n);
        var centers = new Lab[k];
        var d2 = new double[n];
        int first = 0;
        for (int i = 1; i < n; i++) if (wts[i] > wts[first]) first = i;
        centers[0] = pts[first];
        for (int i = 0; i < n; i++) d2[i] = Lab.DistanceSquared(pts[i], centers[0]);
        for (int c = 1; c < k; c++)
        {
            int far = 0;
            double farV = -1;
            for (int i = 0; i < n; i++)
            {
                double v = d2[i] * Math.Sqrt(wts[i]);
                if (v > farV) { farV = v; far = i; }
            }
            centers[c] = pts[far];
            for (int i = 0; i < n; i++) d2[i] = Math.Min(d2[i], Lab.DistanceSquared(pts[i], centers[c]));
        }

        var assign = new int[n];
        for (int iter = 0; iter < 24; iter++)
        {
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                int best = 0;
                float bd = float.MaxValue;
                for (int c = 0; c < k; c++)
                {
                    float d = Lab.DistanceSquared(pts[i], centers[c]);
                    if (d < bd) { bd = d; best = c; }
                }
                if (assign[i] != best || iter == 0) { changed |= assign[i] != best; assign[i] = best; }
            }
            var sl = new double[k];
            var sa = new double[k];
            var sb = new double[k];
            var sw = new double[k];
            for (int i = 0; i < n; i++)
            {
                int c = assign[i];
                sl[c] += pts[i].L * wts[i];
                sa[c] += pts[i].A * wts[i];
                sb[c] += pts[i].B * wts[i];
                sw[c] += wts[i];
            }
            for (int c = 0; c < k; c++)
                if (sw[c] > 0) centers[c] = new Lab((float)(sl[c] / sw[c]), (float)(sa[c] / sw[c]), (float)(sb[c] / sw[c]));
            if (!changed && iter > 0) break;
        }
        return (centers, assign);
    }

    /// <summary>
    /// 자동 색 수(k = 2~<see cref="AutoMaxColors"/>): 평균 오차 곡선의 팔꿈치(정규화한 곡선이 대각선에서 가장 먼 점)와
    /// 두드러진 색을 지키는 가장 작은 k(<see cref="TailLimit"/>) 중 큰 쪽.
    /// </summary>
    public static int AutoCount(IReadOnlyList<(Color32 Color, double Count)> hist)
    {
        int kmax = Math.Min(AutoMaxColors, hist.Count);
        if (kmax <= 2) return Math.Max(1, kmax);
        var pts = hist.Select(h => OkLab.FromColor(h.Color)).ToArray();
        var wts = ClusterWeights(hist);
        var err = new double[kmax + 1];
        int tailK = kmax;
        for (int k = 2; k <= kmax; k++)
        {
            var (centers, assign) = KMeansLab(pts, wts, k);
            var (mean, tail) = Errors(pts, hist, centers, assign);
            err[k] = mean;
            if (tail <= TailLimit && tailK == kmax) tailK = k;
        }
        int last = kmax;
        double e0 = err[2], e1 = err[last];
        int elbow = 2;
        if (e0 - e1 > 1e-9)
        {
            double bestD = double.NegativeInfinity;
            for (int k = 2; k <= last; k++)
            {
                double x = (k - 2) / (double)(last - 2);
                double y = (err[k] - e1) / (e0 - e1);
                double d = 1 - x - y;
                if (d > bestD) { bestD = d; elbow = k; }
            }
        }
        return Math.Max(elbow, tailK);
    }

    /// <summary>
    /// RGB 중앙 분할(가장 넓은 채널을 가중 중앙값에서 자르기를 반복). 상자 색은 가중 평균, <paramref name="members"/>를 주면
    /// 실제 칸 색(<see cref="Representative"/>).
    /// </summary>
    public static List<Color32> MedianCut(IReadOnlyList<(Color32 Color, double Count)> hist, int k,
        IReadOnlyList<List<(Color32 Color, double Weight)>>? members = null, ColorMetric? metric = null)
    {
        var boxes = new List<List<int>> { Enumerable.Range(0, hist.Count).ToList() };
        while (boxes.Count < k)
        {
            int pick = -1, ch = 0, range = 0;
            for (int b = 0; b < boxes.Count; b++)
            {
                if (boxes[b].Count < 2) continue;
                for (int c = 0; c < 3; c++)
                {
                    int lo = 255, hi = 0;
                    foreach (int i in boxes[b])
                    {
                        int v = Channel(hist[i].Color, c);
                        lo = Math.Min(lo, v);
                        hi = Math.Max(hi, v);
                    }
                    if (hi - lo > range) { range = hi - lo; pick = b; ch = c; }
                }
            }
            if (pick < 0) break;
            var sorted = boxes[pick].OrderBy(i => Channel(hist[i].Color, ch)).ToList();
            double total = sorted.Sum(i => hist[i].Count), acc = 0;
            int cut = 1;
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                acc += hist[sorted[i]].Count;
                cut = i + 1;
                if (acc * 2 >= total) break;
            }
            boxes[pick] = sorted.Take(cut).ToList();
            boxes.Add(sorted.Skip(cut).ToList());
        }
        if (members is not null)
        {
            var pts = hist.Select(h => OkLab.FromColor(h.Color)).ToArray();
            return boxes.Where(b => b.Count > 0).Select(b => Representative(b, pts, hist, members, metric)).Distinct().ToList();
        }
        return boxes.Where(b => b.Count > 0).Select(b =>
        {
            double r = 0, g = 0, bl = 0, w = 0;
            foreach (int i in b) { var (c, n) = hist[i]; r += c.R * n; g += c.G * n; bl += c.B * n; w += n; }
            return new Color32((byte)Math.Round(r / w), (byte)Math.Round(g / w), (byte)Math.Round(bl / w));
        }).Distinct().ToList();
    }

    /// <summary>OKLCh 묶기에서 색상을 볼 최소 채도(OKLCh C) — 이보다 흐린 색은 색상각이 흔들리는 회색이라 색상 차를 보지 않는다.</summary>
    public const double OkLchAchromatic = 0.02;

    /// <summary>
    /// OKLCh 문턱 묶기(<see cref="PaletteMethod.OkLch"/>): 두 색의 밝기 차 ≤ <paramref name="maxL"/>, 채도 차 ≤ <paramref name="maxC"/>, 색상 차 ≤ <paramref name="maxH"/>도
    /// (둘 다 채도 ≥ <see cref="OkLchAchromatic"/>일 때만)면 같은 색으로 묶을 수 있다. 아직 묶이지 않은 색 중 문턱 안에 드는 쓰인 수의 합이 가장 큰 색(같으면 쓰인 수가 큰 색,
    /// 그다음 앞 번호)을 묶음 중심으로 뽑고 그 이웃을 묶음에서 빼기를 모든 색이 묶일 때까지 되풀이한다 — 가장 많이 묶는 중심부터. 그 뒤 색마다 문턱 안의 중심 중
    /// 가장 가까운(각 문턱으로 나눈 차의 제곱합) 중심으로 다시 나눈다. 묶음 색은 <paramref name="members"/>가 있으면 실제 칸 색(<see cref="Representative"/>), 없으면 OKLab 무게 평균.
    /// 색 수는 문턱이 정한다(같은 입력이면 늘 같은 결과).
    /// </summary>
    public static List<Color32> OkLchGroups(IReadOnlyList<(Color32 Color, double Count)> hist, double maxL, double maxC, double maxH,
        IReadOnlyList<List<(Color32 Color, double Weight)>>? members = null, ColorMetric? metric = null)
    {
        int n = hist.Count;
        if (n == 0) return [];
        var pts = hist.Select(h => OkLab.FromColor(h.Color)).ToArray();
        var l = new double[n];
        var c = new double[n];
        var h = new double[n];
        for (int i = 0; i < n; i++)
        {
            l[i] = pts[i].L;
            c[i] = ColorDistance.Chroma(pts[i]);
            double deg = Math.Atan2(pts[i].B, pts[i].A) * 180 / Math.PI;
            h[i] = deg < 0 ? deg + 360 : deg;
        }
        double HueGap(int a, int b)
        {
            double d = Math.Abs(h[a] - h[b]);
            return d > 180 ? 360 - d : d;
        }
        bool Within(int a, int b)
            => Math.Abs(l[a] - l[b]) <= maxL && Math.Abs(c[a] - c[b]) <= maxC
               && (c[a] < OkLchAchromatic || c[b] < OkLchAchromatic || HueGap(a, b) <= maxH);

        var near = Neighbors(n, l, maxL, Within);
        return CoverGroups(hist, pts, near, (i, j) =>
        {
            double dl = (l[i] - l[j]) / maxL, dc = (c[i] - c[j]) / maxC;
            double dh = c[i] < OkLchAchromatic || c[j] < OkLchAchromatic ? 0 : HueGap(i, j) / maxH;
            return dl * dl + dc * dc + dh * dh;
        }, members, metric);
    }

    /// <summary>
    /// 색 거리로 묶기(<see cref="PaletteMethod.DistanceGroups"/>): 두 색의 <paramref name="metric"/> 거리(칸이 팔레트 색을 고를 때와 같은 거리)가 <paramref name="radius"/> 이하면
    /// 같은 색으로 묶을 수 있다. 묶음 중심 고르기·나누기·묶음 색은 <see cref="OkLchGroups"/>와 같고(가장 많이 묶는 중심부터, 색마다 반경 안의 가장 가까운 중심으로),
    /// 색 수는 반경이 정한다. 견줄 짝은 밝기 열쇠 창(<see cref="ColorMetric.LightnessWindow"/>) 안만 — 반경 안인 짝을 놓치지 않는다.
    /// </summary>
    public static List<Color32> DistanceGroups(IReadOnlyList<(Color32 Color, double Count)> hist, double radius, ColorMetric metric,
        IReadOnlyList<List<(Color32 Color, double Weight)>>? members = null)
    {
        int n = hist.Count;
        if (n == 0) return [];
        var pts = hist.Select(h => OkLab.FromColor(h.Color)).ToArray();
        var cps = pts.Select(p => metric.Point(p)).ToArray();
        var key = cps.Select(p => metric.LightnessKey(p)).ToArray();
        var near = Neighbors(n, key, metric.LightnessWindow(radius), (a, b) => metric.Distance(cps[a], cps[b]) <= radius);
        return CoverGroups(hist, pts, near, (i, j) => metric.Distance(cps[i], cps[j]), members, metric);
    }

    /// <summary>이웃 목록(자기 포함): 밝기 열쇠 순으로 늘어놓고 열쇠 차가 <paramref name="window"/> 안인 짝만 <paramref name="within"/>으로 견준다.</summary>
    private static List<int>[] Neighbors(int n, double[] key, double window, Func<int, int, bool> within)
    {
        var byKey = Enumerable.Range(0, n).OrderBy(i => key[i]).ThenBy(i => i).ToArray();
        var near = new List<int>[n];
        for (int i = 0; i < n; i++) near[i] = [i];
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n && key[byKey[b]] - key[byKey[a]] <= window; b++)
                if (within(byKey[a], byKey[b]))
                {
                    near[byKey[a]].Add(byKey[b]);
                    near[byKey[b]].Add(byKey[a]);
                }
        return near;
    }

    /// <summary>
    /// 문턱 묶기의 공통 부분: 아직 묶이지 않은 색 중 이웃(<paramref name="near"/>)의 쓰인 수 합이 가장 큰 색(같으면 쓰인 수가 큰 색, 그다음 앞 번호)을 묶음 중심으로 뽑고
    /// 그 이웃을 묶음에서 빼기를 모든 색이 묶일 때까지 되풀이한 뒤, 색마다 이웃 중심 중 <paramref name="distance"/>가 가장 작은 중심(같으면 먼저 뽑힌 중심)으로 나눈다.
    /// 묶음 색은 <paramref name="members"/>가 있으면 실제 칸 색(<see cref="Representative"/>), 없으면 OKLab 무게 평균.
    /// </summary>
    private static List<Color32> CoverGroups(IReadOnlyList<(Color32 Color, double Count)> hist, Lab[] pts, List<int>[] near, Func<int, int, double> distance,
        IReadOnlyList<List<(Color32 Color, double Weight)>>? members, ColorMetric? metric)
    {
        int n = hist.Count;
        // 가장 많이 묶는 중심부터: 남은 이웃의 쓰인 수 합(묶이면 이웃들의 합에서 뺀다).
        var cover = new double[n];
        for (int i = 0; i < n; i++) foreach (int j in near[i]) cover[i] += hist[j].Count;
        var taken = new bool[n];
        var centers = new List<int>();
        for (int left = n; left > 0;)
        {
            int best = -1;
            for (int i = 0; i < n; i++)
                if (!taken[i] && (best < 0 || cover[i] > cover[best] + 1e-9
                                  || (Math.Abs(cover[i] - cover[best]) <= 1e-9 && hist[i].Count > hist[best].Count)))
                    best = i;
            centers.Add(best);
            foreach (int j in near[best])
            {
                if (taken[j]) continue;
                taken[j] = true;
                left--;
                foreach (int k in near[j]) cover[k] -= hist[j].Count;
            }
        }

        // 색마다 이웃 중 가장 가까운 중심(자기 묶음의 중심은 늘 이웃).
        var isCenter = new int[n];
        Array.Fill(isCenter, -1);
        for (int g = 0; g < centers.Count; g++) isCenter[centers[g]] = g;
        var groups = Enumerable.Range(0, centers.Count).Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < n; i++)
        {
            int pick = -1;
            double pickD = double.MaxValue;
            foreach (int j in near[i])
            {
                if (isCenter[j] < 0) continue;
                double d = distance(i, j);
                if (d < pickD - 1e-12 || (Math.Abs(d - pickD) <= 1e-12 && isCenter[j] < pick)) (pick, pickD) = (isCenter[j], d);
            }
            groups[pick].Add(i);
        }

        var result = new List<Color32>(groups.Length);
        foreach (var g in groups)
        {
            if (g.Count == 0) continue;
            if (members is not null) { result.Add(Representative(g, pts, hist, members, metric)); continue; }
            double sl = 0, sa = 0, sb = 0, sw = 0;
            foreach (int j in g) { sl += pts[j].L * hist[j].Count; sa += pts[j].A * hist[j].Count; sb += pts[j].B * hist[j].Count; sw += hist[j].Count; }
            result.Add(OkLab.ToColor(new Lab((float)(sl / sw), (float)(sa / sw), (float)(sb / sw))));
        }
        return result.Distinct().ToList();
    }

    private static int Channel(Color32 c, int ch) => ch == 0 ? c.R : ch == 1 ? c.G : c.B;
}
