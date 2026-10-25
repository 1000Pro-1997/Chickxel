using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Pixelate;

/// <summary>
/// 찾은 픽셀 격자: 칸 경계(원본 px, 오름차순 — 칸 i = [Cuts[i], Cuts[i+1])), 평균 칸 크기, 분석으로 찾은 칸 크기(수동이면 고른 값),
/// 경계가 실제 색 바뀜과 맞는 정도(0~1).
/// </summary>
public sealed record GridFit(int[] XCuts, int[] YCuts, double Scale, double DetectedScale, double Confidence)
{
    public int Columns => XCuts.Length - 1;
    public int Rows => YCuts.Length - 1;

    /// <summary>시작 위치(첫 경계를 칸 크기로 나눈 나머지).</summary>
    public (double X, double Y) Offset => (Mod(XCuts[0], Scale), Mod(YCuts[0], Scale));

    private static double Mod(double v, double m) => m <= 0 ? 0 : ((v % m) + m) % m;
}

/// <summary>
/// 칸 크기 어림: 쓴 크기(원본 px, 0 = 못 찾음), 깨끗한 그림에서 정확히 잰 정수 배인지(그러면 흔들림 없이 고른 간격),
/// 잰 바깥 외곽선 두께와 색 경계 방식 값(0 = 못 잼). 나머지(init): 덩어리 경계 방식 값과 간격이 모이는 정도, 세 값마다 그 크기로 고른 경계열이 원본의
/// 뚜렷한 경계와 맞는 정도(우연 수준을 뺀 일치 <see cref="GridDetector.AlignmentMatch"/>, 0~1), 실제로 쓴 방식(자동이면 고른 쪽).
/// </summary>
public readonly record struct ScaleEstimate(double Scale, bool Exact, double Outline = 0, double Edges = 0)
{
    public double Blocks { get; init; }
    /// <summary>덩어리 경계 간격이 모이는 정도(<see cref="GridDetector.BlockScale"/>의 Fit, 0~1).</summary>
    public double BlocksFit { get; init; }
    /// <summary>자동에서 덩어리 경계 간격이 뚜렷해(<see cref="GridDetector.ClearBlockFit"/> 이상) 그 크기를 바로 골랐는지.</summary>
    public bool ClearBlocks { get; init; }
    /// <summary>외곽선 방식인데 외곽선이 칸보다 가늘어(더 큰 값이 확실히 더 잘 맞음) 그 큰 값을 골랐는지.</summary>
    public bool ThinOutline { get; init; }
    public double OutlineMatch { get; init; }
    public double EdgesMatch { get; init; }
    public double BlocksMatch { get; init; }
    /// <summary>쓴 방식(<see cref="ScaleSource.Auto"/> = 정확한 정수 배이거나 못 찾음).</summary>
    public ScaleSource Used { get; init; }
    /// <summary>
    /// 이번 어림의 칸 크기 상한(원본 px, <see cref="GridDetector.MaxCellFor"/> — 스프라이트 짧은 변의 1/<see cref="GridDetector.MinSpriteCells"/>):
    /// 세 방식 값·정확한 배수 모두 이 안에서만 잰다(0 = 어림 없음).
    /// </summary>
    public double MaxScale { get; init; }
    /// <summary>쓴 크기의 경계 일치(0~1).</summary>
    public double Match => Used switch
    {
        ScaleSource.Outline => OutlineMatch,
        ScaleSource.ColorEdges => EdgesMatch,
        ScaleSource.EdgeLines => BlocksMatch,
        _ => 0,
    };
    /// <summary>경계열을 뚜렷한 경계 세기(<see cref="GridDetector.BlockEdges"/>)로 고른다 — 덩어리 경계 값을 쓸 때(아니면 <see cref="GridDetector.Profiles"/>의 색 차이 세기).</summary>
    public bool BlockCuts => Used == ScaleSource.EdgeLines && !Exact;
}

/// <summary>자동 격자의 칸 크기를 무엇으로 정할지.</summary>
public enum ScaleSource
{
    /// <summary>
    /// 세 방식(색 경계·외곽선 두께·덩어리 경계)으로 잰 크기마다 경계열을 골라, 원본의 뚜렷한 경계와 가장 잘 맞는 쪽(<see cref="GridDetector.Alignment"/>).
    /// </summary>
    Auto,
    /// <summary>바깥 외곽선 두께 — 줄였을 때 외곽선이 1px이 되는 크기.</summary>
    Outline,
    /// <summary>색이 바뀌는 자리 사이 길이.</summary>
    ColorEdges,
    /// <summary>
    /// 덩어리 경계: 종이 결 같은 약한 무늬를 뺀 뚜렷한 색 경계가 열·행마다 놓인 자리를 칸 크기로 접어(나머지) 한 자리에 모이는 간격
    /// (<see cref="GridDetector.BlockScale"/>) — 경계열도 이 뚜렷한 경계 세기로 고른다.
    /// </summary>
    EdgeLines,
}

/// <summary>
/// 격자 찾기.
/// (1) 칸 크기 — 세 방식: 색 경계(가로·세로 줄마다 색이 크게 바뀌는 자리 사이 길이가 칸 크기의 정수배에 맞는 정도, <see cref="EdgeScale"/>),
///     바깥 외곽선 두께(<see cref="OutlineThickness"/>), 덩어리 경계(종이 결 같은 약한 무늬를 뺀 뚜렷한 경계 자리가 한 줄 안에서 칸 크기 간격으로
///     모이는 정도, <see cref="BlockScale"/>). 자동은 값마다 고른 경계열이 뚜렷한 경계와 가장 잘 맞는 쪽(<see cref="EstimateScale"/>).
/// (2) 경계열: 열·행마다 경계 세기(뚜렷한 경계 수 <see cref="BlockEdges"/>, 색 경계를 직접 고르면 이웃 색 차이 합 <see cref="Profiles"/>)에서,
///     동적 계획법으로 간격이 칸 크기 ± 흔들림 안이면서 경계 세기가 큰 자리를 지나는 경계열을 고른다 — 그래서 칸 크기가 7~9px로 흔들려도 따라간다.
///     흔들림이 0이거나 수동이면 고른 간격(실수)의 격자다.
/// </summary>
public static class GridDetector
{
    /// <summary>이보다 작은 이웃 차이(채널 최대 차)는 잡음으로 본다.</summary>
    private const int NoiseFloor = 14;

    /// <summary>경계 세기(가로: x 열 왼쪽 경계, 세로: y 행 위 경계). <paramref name="labels"/>가 있으면 그 프레임 픽셀만 본다.</summary>
    public static (double[] X, double[] Y) Profiles(PixelImage cleaned, Rectangle area, int[]? labels = null, int label = -1)
    {
        int w = cleaned.Width;
        var px = cleaned.Pixels;
        var ex = new double[w + 1];
        var ey = new double[cleaned.Height + 1];
        bool In(int i) => px[i].A != 0 && (labels is null || labels[i] == label);
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                int i = y * w + x;
                bool a = In(i);
                if (x > area.Left) ex[x] += Diff(a, px[i], In(i - 1), px[i - 1]);
                if (y > area.Top) ey[y] += Diff(a, px[i], In(i - w), px[i - w]);
            }
        return (ex, ey);
    }

    /// <summary>
    /// 한 범위의 경계 세기 누적(<see cref="Sums"/>): 가로 경계 세기(<see cref="Profiles"/>의 X)를 행마다 위에서부터, 세로 경계 세기(Y)를 열마다 왼쪽에서부터
    /// 더해 둔 표 — 행 띠·열 띠마다의 경계 세기를 픽셀을 다시 보지 않고 바로 낸다(값은 그 띠로 <see cref="Profiles"/>를 잰 것과 같음).
    /// </summary>
    public sealed class EdgeSums
    {
        // _x[r * W + (x − Left)] = 행 Top..Top+r−1의 x 경계 세기 합, _y[c * H + (y − Top)] = 열 Left..Left+c−1의 y 경계 세기 합(세기는 정수).
        private readonly int[] _x, _y;

        internal EdgeSums(Rectangle area, int[] x, int[] y)
        {
            Area = area;
            _x = x;
            _y = y;
        }

        public Rectangle Area { get; }

        /// <summary>행 [<paramref name="top"/>, <paramref name="bottom"/>)(범위 안으로 자름)의 가로 경계 세기: <paramref name="dst"/>[x − (Left − 1)] (길이 = 범위 너비 + 2, 양 끝 0).</summary>
        public void X(int top, int bottom, double[] dst) => Band(_x, Area.Width, top - Area.Top, bottom - Area.Top, Area.Height, dst);

        /// <summary>열 [<paramref name="left"/>, <paramref name="right"/>)의 세로 경계 세기: <paramref name="dst"/>[y − (Top − 1)] (길이 = 범위 높이 + 2, 양 끝 0).</summary>
        public void Y(int left, int right, double[] dst) => Band(_y, Area.Height, left - Area.Left, right - Area.Left, Area.Width, dst);

        private static void Band(int[] sums, int n, int a, int b, int count, double[] dst)
        {
            a = Math.Clamp(a, 0, count);
            b = Math.Clamp(b, a, count);
            dst[0] = 0;
            dst[n + 1] = 0;
            for (int i = 0; i < n; i++) dst[i + 1] = sums[b * n + i] - sums[a * n + i];
        }
    }

    /// <summary>범위 <paramref name="area"/>의 경계 세기 누적(<see cref="EdgeSums"/> — <see cref="Profiles"/>와 같은 세기·같은 픽셀 규칙).</summary>
    public static EdgeSums Sums(PixelImage cleaned, Rectangle area, int[]? labels = null, int label = -1)
    {
        int w = cleaned.Width, aw = Math.Max(0, area.Width), ah = Math.Max(0, area.Height);
        var px = cleaned.Pixels;
        var xs = new int[(ah + 1) * aw];
        var ys = new int[(aw + 1) * ah];
        bool In(int i) => px[i].A != 0 && (labels is null || labels[i] == label);
        for (int r = 0; r < ah; r++)
        {
            int y = area.Top + r;
            for (int c = 0; c < aw; c++)
            {
                int x = area.Left + c, i = y * w + x;
                bool a = In(i);
                int dx = c > 0 ? (int)Diff(a, px[i], In(i - 1), px[i - 1]) : 0;
                int dy = r > 0 ? (int)Diff(a, px[i], In(i - w), px[i - w]) : 0;
                xs[(r + 1) * aw + c] = xs[r * aw + c] + dx;
                ys[(c + 1) * ah + r] = ys[c * ah + r] + dy;
            }
        }
        return new EdgeSums(area, xs, ys);
    }

    private static double Diff(bool a, Color32 ca, bool b, Color32 cb)
    {
        if (!a && !b) return 0;
        if (a != b) return 255 - NoiseFloor;
        int d = Math.Max(Math.Abs(ca.R - cb.R), Math.Max(Math.Abs(ca.G - cb.G), Math.Abs(ca.B - cb.B)));
        return Math.Max(0, d - NoiseFloor);
    }

    /// <summary>뚜렷한 경계로 볼 최소 OKLab 거리(잡음이 거의 없는 그림에서 문턱이 0으로 떨어지지 않게 — 겨우 보이는 차이 0.02의 2배).</summary>
    private const double MinBlockEdge = 0.04;
    /// <summary>뚜렷한 경계 문턱: 스프라이트 안 이웃 차이의 중앙값 + 이 수 × 잡음 크기(MAD × 1.4826).</summary>
    private const double BlockEdgeSigmas = 5;

    /// <summary>그림 전체의 OKLab 색(투명은 0).</summary>
    public static Lab[] LabOf(PixelImage img)
    {
        var px = img.Pixels;
        var lab = new Lab[px.Length];
        var cache = new Dictionary<uint, Lab>();
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].A == 0) continue;
            if (!cache.TryGetValue(px[i].Argb, out var v)) cache[px[i].Argb] = v = OkLab.FromColor(px[i]);
            lab[i] = v;
        }
        return lab;
    }

    /// <summary>
    /// 뚜렷한 경계(<see cref="GridDetector.BlockEdges"/>): 열·행 경계마다 센 수(가로: x 열 왼쪽 경계, 세로: y 행 위 경계 — <see cref="Profiles"/>와 같은 자리)와
    /// 한 줄 안 두 경계 사이 거리(1~<see cref="MaxPair"/>px)의 막대그림(가로·세로 합). <see cref="Rows"/>[y − 범위 위] = 그 행의 경계 x 자리(오름차순),
    /// <see cref="Cols"/>[x − 범위 왼쪽] = 그 열의 경계 y 자리 — 칸 경계 지역 보정(<see cref="LocalGrid.Correct"/>)이 띠마다 경계 세기를 다시 셀 때 쓴다.
    /// </summary>
    public sealed record BlockEdgeMap(double[] X, double[] Y, double[] Pairs)
    {
        public Rectangle Area { get; init; }
        public int[][] Rows { get; init; } = [];
        public int[][] Cols { get; init; } = [];
    }

    /// <summary>덩어리 경계 간격을 잴 때 볼 한 줄 안 두 경계의 최대 거리(가장 큰 후보 칸 크기의 2.5배).</summary>
    private const int MaxPair = MaxSpan;

    /// <summary>
    /// 뚜렷한 경계 찾기: 줄마다 이웃 픽셀의 OKLab 거리(번진 경계도 잡게 한 칸 건너 거리의 0.7배와 큰 쪽)가 문턱을 넘고 양옆보다 큰 자리(번진 경계는 하나로)를
    /// 경계 하나로 센다. 전경·투명이 바뀌는 자리도 경계. 문턱 = 스프라이트 안 이웃 거리의 중앙값 + <see cref="BlockEdgeSigmas"/> × 잡음 크기(MAD × 1.4826),
    /// 적어도 <see cref="MinBlockEdge"/> — 종이 결·잔 잡음은 세지 않는다.
    /// </summary>
    public static BlockEdgeMap BlockEdges(PixelImage cleaned, Lab[] lab, Rectangle area, int[]? labels = null, int label = -1)
    {
        int w = cleaned.Width;
        var px = cleaned.Pixels;
        var ex = new double[w + 1];
        var ey = new double[cleaned.Height + 1];
        var pairs = new double[MaxPair + 1];
        if (area.Width < 2 || area.Height < 2) return new BlockEdgeMap(ex, ey, pairs) { Area = area };
        bool On(int i) => px[i].A != 0 && (labels is null || labels[i] == label);
        // 경계 t(픽셀 t−1과 t 사이)의 세기: 둘 다 투명이면 0, 하나만 투명이면 +∞.
        float Strength(int start, int step, int count, int t)
        {
            int i = start + t * step, j = i - step;
            bool a = On(i), b = On(j);
            if (a != b) return float.PositiveInfinity;
            if (!a) return 0;
            float d = Lab.Distance(lab[i], lab[j]);
            if (t >= 2 && t + 1 < count && On(i + step) && On(j - step)) d = Math.Max(d, 0.7f * Lab.Distance(lab[i + step], lab[j - step]));
            return d;
        }

        // 잡음 크기: 전경 안 이웃 거리(0.001 단위 막대그림)의 중앙값과 MAD.
        const int bins = 2048;
        var hist = new long[bins];
        long total = 0;
        void Sample(int start, int step, int count)
        {
            for (int t = 1; t < count; t++)
            {
                if (!On(start + t * step)) continue;
                float d = Strength(start, step, count, t);
                if (float.IsPositiveInfinity(d)) continue;
                hist[Math.Min(bins - 1, (int)(d * 1000))]++;
                total++;
            }
        }
        for (int y = area.Top; y < area.Bottom; y += 2) Sample(y * w + area.Left, 1, area.Width);
        for (int x = area.Left; x < area.Right; x += 2) Sample(area.Top * w + x, w, area.Height);
        double threshold = MinBlockEdge;
        if (total > 0)
        {
            int median = Quantile(hist, total, 0.5);
            var dev = new long[bins];
            for (int k = 0; k < bins; k++) dev[Math.Abs(k - median)] += hist[k];
            int mad = Quantile(dev, total, 0.5);
            threshold = Math.Max(MinBlockEdge, (median + BlockEdgeSigmas * 1.4826 * mad) / 1000.0);
        }

        var line = new float[Math.Max(area.Width, area.Height) + 1];
        var found = new List<int>();
        var rows = new int[area.Height][];
        var cols = new int[area.Width][];
        int[] Line(int start, int step, int count, double[] profile, int origin)
        {
            for (int t = 1; t < count; t++) line[t] = Strength(start, step, count, t);
            found.Clear();
            for (int t = 1; t < count; t++)
            {
                float d = line[t];
                if (d <= threshold) continue;
                if (t > 1 && line[t - 1] > d) continue;
                if (t + 1 < count && line[t + 1] >= d && !float.IsPositiveInfinity(d)) continue;
                profile[origin + t] += 1;
                found.Add(t);
            }
            for (int a = 0; a < found.Count; a++)
                for (int b = a + 1; b < found.Count && found[b] - found[a] <= MaxPair; b++)
                    pairs[found[b] - found[a]]++;
            var at = new int[found.Count];
            for (int k = 0; k < at.Length; k++) at[k] = origin + found[k];
            return at;
        }
        for (int y = area.Top; y < area.Bottom; y++) rows[y - area.Top] = Line(y * w + area.Left, 1, area.Width, ex, area.Left);
        for (int x = area.Left; x < area.Right; x++) cols[x - area.Left] = Line(area.Top * w + x, w, area.Height, ey, area.Top);
        return new BlockEdgeMap(ex, ey, pairs) { Area = area, Rows = rows, Cols = cols };
    }

    private static int Quantile(long[] hist, long total, double q)
    {
        long target = (long)Math.Ceiling(total * q), seen = 0;
        for (int k = 0; k < hist.Length; k++)
        {
            seen += hist[k];
            if (seen >= target) return k;
        }
        return hist.Length - 1;
    }

    /// <summary>
    /// 덩어리 경계 간격마다 경계 자리가 모이는 정도(−1~1, 0 = 우연 수준). 한 줄의 경계 자리 xᵢ를 간격 s로 접은(나머지) 원 위 평균 벡터 길이의 제곱은
    /// 두 경계 사이 거리 d의 cos(2πd/s) 평균이므로, 줄마다 2.5칸 안 경계 쌍 거리 막대그림(<see cref="BlockEdgeMap.Pairs"/>)에서 cos 평균을 잰다
    /// — 줄마다·가까운 쌍만 보므로 줄마다 덩어리가 조금씩 어긋나거나 칸 크기가 흔들려 위치가 밀려도(누적) 모인다. 우연히 모이는 정도(같은 막대그림을
    /// 칸 크기 폭으로 고르게 편 것의 cos 평균)를 빼고 나눈다(<see cref="ScaleFits"/>와 같은 보정). 후보는 <see cref="CandidateScales"/>(3~<see cref="MaxScale"/>px, 범위의 ¼까지,
    /// <paramref name="maxScale"/> 이하 — <see cref="MaxCellFor"/>).
    /// </summary>
    public static List<(double Scale, double Fit)> BlockScaleFits(BlockEdgeMap edges, Rectangle bounds, double maxScale = MaxScale)
    {
        var h = edges.Pairs;
        var result = new List<(double, double)>();
        double max = Math.Min(maxScale, Math.Min(MaxScale, Math.Max(bounds.Width, bounds.Height) / 4.0));
        var prefix = new double[h.Length + 1];
        for (int d = 0; d < h.Length; d++) prefix[d + 1] = prefix[d] + h[d];
        foreach (double s in CandidateScales(max))
        {
            int reach = Math.Min(h.Length - 1, (int)(2.5 * s));
            int half = Math.Max(1, (int)Math.Round(s / 2));
            double fit = 0, total = 0, chance = 0, flat = 0;
            for (int d = 1; d <= reach; d++)
            {
                double c = Math.Cos(2 * Math.PI * d / s);
                fit += h[d] * c;
                total += h[d];
                int a = Math.Max(1, d - half), b = Math.Min(reach, d + half);
                double smooth = (prefix[b + 1] - prefix[a]) / (b - a + 1);
                chance += smooth * c;
                flat += smooth;
            }
            if (total <= 0 || flat <= 0) { result.Add((s, 0)); continue; }
            fit /= total;
            chance /= flat;
            result.Add((s, chance >= 1 ? 0 : (fit - chance) / (1 - chance)));
        }
        return result;
    }

    /// <summary>
    /// 덩어리 경계 방식 칸 크기(<see cref="BlockEdges"/>의 뚜렷한 경계에서): <see cref="BlockScaleFits"/>의 가장 높은 봉우리 —
    /// 경계가 s 간격이면 s/2·s/3 간격에도 모두 모이고(칸 하나 = 반 칸 둘), 반 칸 덩어리가 섞이면 s/2가 더 높을 수 있으므로,
    /// 2배·3배 자리 봉우리가 가장 높은 값에서 0.15 안이면 그쪽으로(되풀이 — 색 경계 방식의 '절반도 거의 그만큼이면 절반'과 같은 꼴, 방향만 반대).
    /// 진짜 칸 크기의 2배는 한 칸 거리 쌍이 반 바퀴(cos = −1)로 접혀 크게 떨어지므로 배수로는 가지 않는다. 경계가 없으면 (0, 0). 후보는 <paramref name="maxScale"/> 이하.
    /// </summary>
    public static (double Scale, double Fit) BlockScale(BlockEdgeMap edges, Rectangle bounds, double maxScale = MaxScale)
    {
        var fits = BlockScaleFits(edges, bounds, maxScale);
        var peaks = new List<int>();
        for (int i = 0; i < fits.Count; i++)
            if ((i == 0 || fits[i].Fit >= fits[i - 1].Fit) && (i + 1 == fits.Count || fits[i].Fit >= fits[i + 1].Fit) && fits[i].Fit > 0) peaks.Add(i);
        if (peaks.Count == 0) return (0, 0);
        int top = peaks.MaxBy(i => fits[i].Fit);
        double bestFit = fits[top].Fit;
        while (true)
        {
            int next = -1;
            foreach (int k in new[] { 2, 3 })
            {
                double want = fits[top].Scale * k, tol = Math.Max(0.75, want * 0.06);
                foreach (int p in peaks)
                    if (Math.Abs(fits[p].Scale - want) <= tol && fits[p].Fit >= bestFit - 0.15 && (next < 0 || fits[p].Fit > fits[next].Fit))
                        next = p;
                if (next >= 0) break;
            }
            if (next < 0) break;
            top = next;
        }
        return (fits[top].Scale, fits[top].Fit);
    }

    /// <summary>
    /// 경계열이 뚜렷한 경계(<paramref name="e"/> — 보통 <see cref="BlockEdges"/>의 X·Y)와 맞는 정도(−1~1): lo~hi 자리마다 '경계 ±1px 안인지'와 경계 세기의
    /// 상관계수(점-이연 상관). 경계를 모두 지나면서(놓친 경계가 없음) 경계 없는 자리에 칸 경계를 덜 둘수록(빈 칸 경계가 적음) 크다 —
    /// 진짜 칸의 절반(빈 칸 경계가 반)이나 두 배(경계를 반 놓침)는 낮다.
    /// </summary>
    public static double Alignment(double[] e, int[] cuts, int lo, int hi)
    {
        lo = Math.Max(0, lo);
        hi = Math.Min(e.Length - 1, hi);
        int n = hi - lo + 1;
        if (n <= 2 || cuts.Length == 0) return 0;
        var near = new bool[n];
        foreach (int c in cuts)
            for (int d = -1; d <= 1; d++)
                if (c + d >= lo && c + d <= hi) near[c + d - lo] = true;
        double s1 = 0, s0 = 0, sum = 0, sq = 0;
        int n1 = 0;
        for (int k = 0; k < n; k++)
        {
            double v = e[lo + k];
            sum += v;
            sq += v * v;
            if (near[k]) { s1 += v; n1++; } else s0 += v;
        }
        int n0 = n - n1;
        double mean = sum / n, var = sq / n - mean * mean;
        if (n1 == 0 || n0 == 0 || var <= 1e-12) return 0;
        double p = n1 / (double)n;
        return (s1 / n1 - s0 / n0) * Math.Sqrt(p * (1 - p)) / Math.Sqrt(var);
    }

    /// <summary>우연 수준을 잴 때 섞어 볼 횟수.</summary>
    private const int ChanceShuffles = 4;

    /// <summary>
    /// 우연 수준을 뺀 경계 일치(−1~1, 0 = 우연): 칸 크기 s·흔들림으로 고른 경계열의 <see cref="Alignment"/>에서, 같은 경계 세기를 자리만 섞은 것
    /// (<see cref="ChanceShuffles"/>번, 늘 같은 씨앗)에 같은 방법으로 고른 경계열의 평균 일치 c를 빼고 (1 − c)로 나눈다. 경계열 고르기는 경계 세기가 큰 자리를
    /// 골라 지나므로 경계가 없어도 일치가 0보다 크고, 그 크기는 칸 크기마다 다르다(칸이 클수록 경계 하나를 고를 자리가 넓음) — 그래서 크기가 다른 값끼리
    /// 견주려면 빼야 한다.
    /// </summary>
    public sealed class AlignmentMatch
    {
        private readonly BlockEdgeMap _edges;
        private readonly Rectangle _bounds;
        private readonly double _jitter;
        private readonly double[][] _sx, _sy;

        public AlignmentMatch(BlockEdgeMap edges, Rectangle bounds, double jitter)
        {
            _edges = edges;
            _bounds = bounds;
            _jitter = jitter;
            _sx = Shuffles(edges.X, bounds.Left, bounds.Right, 17);
            _sy = Shuffles(edges.Y, bounds.Top, bounds.Bottom, 29);
        }

        private static double[][] Shuffles(double[] e, int lo, int hi, int seed)
        {
            var rng = new Random(seed);
            lo = Math.Max(0, lo);
            hi = Math.Min(e.Length - 1, hi);
            var r = new double[ChanceShuffles][];
            for (int k = 0; k < r.Length; k++)
            {
                var c = (double[])e.Clone();
                for (int i = hi; i > lo; i--)
                {
                    int j = lo + rng.Next(i - lo + 1);
                    (c[i], c[j]) = (c[j], c[i]);
                }
                r[k] = c;
            }
            return r;
        }

        private double Axis(double[] e, double[][] shuffled, int lo, int hi, double s)
        {
            double a = Alignment(e, FitCuts(e, lo, hi, s, _jitter), lo, hi);
            double c = 0;
            foreach (var r in shuffled) c += Alignment(r, FitCuts(r, lo, hi, s, _jitter), lo, hi);
            c /= shuffled.Length;
            return c >= 1 ? 0 : (a - c) / (1 - c);
        }

        /// <summary>칸 크기 <paramref name="s"/>의 가로·세로 평균(0 아래는 0, s ≤ 0이면 0).</summary>
        public double Of(double s) => s <= 0 ? 0 : Math.Max(0, 0.5 * (
            Axis(_edges.X, _sx, _bounds.Left, _bounds.Right, s) + Axis(_edges.Y, _sy, _bounds.Top, _bounds.Bottom, s)));
    }

    /// <summary>구간 길이를 잴 때 경계로 볼 색 차이(채널 최대 차).</summary>
    private const int RunEdge = 40;
    /// <summary>
    /// 칸 크기를 잴 때 기본으로 볼 구간 길이(원본 px) — 이보다 긴 구간은 그 길이의 ⅖보다 큰 후보(<see cref="Reach"/>)에서만 센다.
    /// 외곽선 두께도 이보다 길면 세지 않는다.
    /// </summary>
    private const int MaxRun = 96;
    /// <summary>가장 큰 칸 크기 후보(원본 px, 수동 칸 크기 최댓값과 같음) — 100px 넘는 덩어리로 키운 그림도 잴 수 있게.</summary>
    private const int MaxScale = 256;
    /// <summary>세는 가장 긴 구간·경계 쌍 거리(가장 큰 후보의 2.5배).</summary>
    private const int MaxSpan = MaxScale * 5 / 2;

    /// <summary>칸 크기 <paramref name="s"/>에서 볼 가장 긴 구간: <see cref="MaxRun"/>과 2.5칸 중 큰 쪽(≤ 38px 후보는 예전처럼 96px까지).</summary>
    private static double Reach(double s) => Math.Max(MaxRun, 2.5 * s);

    /// <summary>칸 크기 후보(작은 것부터): 3~12는 0.25 간격, 12~48은 0.5 간격, 그 위는 1 간격, <paramref name="max"/>까지.</summary>
    private static List<double> CandidateScales(double max)
    {
        var list = new List<double>();
        for (double s = 3; s <= max + 1e-9; s += s < 12 ? 0.25 : s < 48 ? 0.5 : 1) list.Add(s);
        return list;
    }

    /// <summary>
    /// 구간 길이 세기(0.5px 단위 — 칸 k = 길이 k/2): 두 줄마다 가로·세로로, 전경 안에서 색이 크게 바뀌거나 전경·배경이 바뀌는 자리를 경계로
    /// (2px 안 경계는 하나로 — 번진 경계) 이웃 경계 사이 길이를 센다. 전경에서 시작하는 구간만.
    /// </summary>
    public static int[] RunLengths(PixelImage cleaned, int[]? labels, int label, Rectangle area)
    {
        var hist = new int[MaxSpan * 2 + 1];
        int w = cleaned.Width;
        var px = cleaned.Pixels;
        bool On(int i) => px[i].A != 0 && (labels is null || labels[i] == label);
        var edges = new List<double>();
        void Line(int start, int step, int count)
        {
            edges.Clear();
            for (int t = 1; t < count; t++)
            {
                int i = start + t * step, j = i - step;
                bool a = On(i), b = On(j);
                bool edge = a != b;
                if (!edge && a)
                {
                    // 색 바뀜이 양쪽으로 1px 더 이어져야 경계(종이 결·잡티 같은 1px 점은 경계가 아님): i↔j와 i+1↔j−1이 모두 크게 다름.
                    edge = Diff(px[i], px[j]) > RunEdge
                        && (t + 1 >= count || t - 2 < 0 || !On(i + step) || !On(j - step) || Diff(px[i + step], px[j - step]) > RunEdge);
                }
                if (!edge) continue;
                if (edges.Count > 0 && t - edges[^1] <= 2) edges[^1] = (edges[^1] + t) / 2;
                else edges.Add(t);
            }
            for (int e = 0; e + 1 < edges.Count; e++)
            {
                double len = edges[e + 1] - edges[e];
                if (len > MaxSpan || !On(start + (int)Math.Ceiling(edges[e]) * step)) continue;
                hist[(int)Math.Round(len * 2)]++;
            }
        }

        static int Diff(Color32 ca, Color32 cb) => Math.Max(Math.Abs(ca.R - cb.R), Math.Max(Math.Abs(ca.G - cb.G), Math.Abs(ca.B - cb.B)));
        for (int y = area.Top; y < area.Bottom; y += 2) Line(y * w + area.Left, 1, area.Width);
        for (int x = area.Left; x < area.Right; x += 2) Line(area.Top * w + x, w, area.Height);
        return hist;
    }

    /// <summary>
    /// 칸 크기마다 맞는 정도(−1~1, 0 = 우연 수준): 구간 길이 r을 가장 가까운 n칸(n ≥ 1)과 견준 차이를 흔들림 σ·s·√n(최소 1px)으로 나눠
    /// 가우스 가중 평균하고, 같은 계산을 고르게 퍼진 길이로 한 값(우연히 맞는 정도)을 뺀다. 후보마다 <see cref="Reach"/>까지의 구간만 본다.
    /// 후보는 3px부터 <paramref name="max"/>까지(<see cref="CandidateScales"/>).
    /// </summary>
    public static List<(double Scale, double Fit)> ScaleFits(int[] hist, double jitter, double max = MaxRun / 2.0)
    {
        double sigma = Math.Max(0.06, jitter * 0.8);
        var result = new List<(double, double)>();
        var count = new double[hist.Length + 1];
        for (int k = 0; k < hist.Length; k++) count[k + 1] = count[k] + hist[k];
        if (count[^1] == 0) return result;
        static double Kernel(double r, double s, double sigma)
        {
            double n = Math.Max(1, Math.Round(r / s));
            double e = Math.Abs(r - n * s) / Math.Max(1, sigma * s * Math.Sqrt(n));
            return Math.Exp(-e * e / 2);
        }
        foreach (double s in CandidateScales(max))
        {
            double reach = Reach(s);
            int top = Math.Min(hist.Length - 1, (int)(reach * 2));
            double total = count[top + 1] - count[1];
            if (total <= 0) { result.Add((s, 0)); continue; }
            double fit = 0;
            for (int k = 1; k <= top; k++)
                if (hist[k] > 0) fit += hist[k] * Kernel(k / 2.0, s, sigma);
            fit /= total;
            double chance = 0;
            int m = 0;
            for (double r = s / 2; r <= reach; r += 0.25, m++) chance += Kernel(r, s, sigma);
            chance /= Math.Max(1, m);
            result.Add((s, chance >= 1 ? 0 : (fit - chance) / (1 - chance)));
        }
        return result;
    }

    /// <summary>이 수 이하의 색만 쓴 그림은 이미 깨끗한 픽셀 아트로 보고 정확한 구간 길이로 칸 크기를 잰다(<see cref="ExactScale"/>).</summary>
    private const int CleanColorLimit = 256;

    /// <summary>외곽선 두께로 칸 크기를 정하려면 잰 단면이 이만큼 있어야 한다.</summary>
    private const int MinOutlineSamples = 30;

    /// <summary>외곽선 맞추기 '크기를 줄여서'(외곽선 방식)에서 다른 방식 값을 외곽선 두께와 같은 칸으로 볼 비율 차(±12%).</summary>
    private const double SameCellTolerance = 0.12;
    /// <summary>경계 일치가 이만큼 안이면 같은 것으로 보고 먼저 고를 쪽(외곽선 → 덩어리 경계 → 색 경계)을 고른다.</summary>
    private const double MatchTie = 0.01;
    /// <summary>
    /// 고른 값의 정수배(2배·3배…, ±8%)인 다른 값이 경계 일치가 이만큼 안이면(거의 같으면) 큰 쪽을 고른다 — 진짜 칸의 1/k 격자도 진짜 경계를 모두 지나므로
    /// 일치가 비슷하게 나올 수 있는데, 그러면 결과가 k배 커지고 k×k 같은 색 덩어리로 채워진다(<see cref="PixelateMetrics.BlockRepetition"/>).
    /// </summary>
    private const double DivisorTie = 0.03;
    /// <summary>
    /// 외곽선 방식에서 외곽선 두께보다 큰(같은 칸 ±<see cref="SameCellTolerance"/> 밖) 다른 값의 경계 일치가 외곽선 쪽보다 이만큼 넘게 높으면 그 값을 쓴다:
    /// 외곽선이 칸보다 가늘면(번진 1px 테두리, 가는 선) 외곽선 두께로 줄인 결과는 칸 하나가 여러 픽셀로 늘어난다 — 큰 칸으로 줄여도 외곽선은 1px 이하.
    /// </summary>
    private const double ThinOutlineMargin = 0.05;
    /// <summary>
    /// 자동에서 덩어리 경계 간격이 이만큼 모이면(<see cref="BlockScale"/>의 Fit) 그 크기를 바로 쓴다. 경계 일치(<see cref="Alignment"/>)는 흔들림을 따라가는
    /// 경계열로 재므로 넓은 칠이 많은 그림에서는 진짜 칸의 2배도 거의 모든 경계를 지나 더 높게 나올 수 있는데, 간격이 뚜렷하면 그것이 더 믿을 만하다.
    /// AI 그림처럼 덩어리 크기가 들쭉날쭉해 간격이 흐리면(표본: 0.1~0.16) 경계 일치로 고른다.
    /// </summary>
    public const double ClearBlockFit = 0.3;

    /// <summary>
    /// 칸 크기 상한을 정할 스프라이트 짧은 변의 칸 수: 자동 칸 크기는 스프라이트(프레임 범위) 짧은 변의 1/이 수를 넘지 않는다(<see cref="MaxCellFor"/>).
    /// 큰 덩어리 시험 그림(8×6칸, 칸 64~200px)은 짧은 변이 6칸이라 1칸 여유(흔들림·반올림으로 어림이 칸보다 조금 클 때)를 두고 5.
    /// 이보다 큰 값은 칸이 아니라 프레임 간격·덩어리 크기다(표본 엘프 걷기 시트 1086×362: 그림 전체 덩어리 경계 183px = 프레임 간격 181px →
    /// 결과 3×3 투명).
    /// </summary>
    public const int MinSpriteCells = 5;

    /// <summary>스프라이트 범위 <paramref name="sprite"/>의 칸 크기 상한: 짧은 변 / <see cref="MinSpriteCells"/>(<see cref="MaxScale"/> 이하, 1 이상).</summary>
    public static double MaxCellFor(Rectangle sprite)
        => Math.Clamp(Math.Min(sprite.Width, sprite.Height) / (double)MinSpriteCells, 1, MaxScale);

    /// <summary>
    /// 칸 크기 어림. 세 값(바깥 외곽선 두께 <see cref="OutlineThickness"/>, 색 경계 <see cref="EdgeScale"/>, 덩어리 경계 <see cref="BlockScale"/>)과
    /// 값마다 경계 일치(그 크기·흔들림으로 뚜렷한 경계 세기에서 고른 경계열의 우연 수준을 뺀 일치, <see cref="AlignmentMatch"/>)를 늘 같이 재서 돌려준다(화면 표시·검증).
    /// 색 경계를 직접 고른 게 아니면, 색이 적은(이미 깨끗한) 그림은 <see cref="ExactScale"/>(정수 배 확대 그대로면 그 배수, 1배면 1 — <c>Exact</c>).
    /// 아니면 <paramref name="source"/>대로:
    /// 색 경계 = 색 경계 값(경계열은 예전처럼 색 차이 세기로). 덩어리 경계 = 덩어리 경계 값(못 재면 자동처럼).
    /// 외곽선 = 바깥 외곽선 두께 — 줄였을 때 외곽선이 1px이 되는 크기라는 뜻은 지키고, 다른 값이 외곽선 두께와 같은 칸(±<see cref="SameCellTolerance"/>)이면
    /// 그중 경계 일치가 가장 높은 값(같은 칸을 더 정확히 잰 값). 반 칸(외곽선 두께의 ½) 같은 값은 외곽선이 2px이 되므로 고르지 않는다. 외곽선을 못 재면 자동처럼.
    /// 외곽선 두께보다 큰 값이 <see cref="ThinOutlineMargin"/> 넘게 더 잘 맞으면(외곽선이 칸보다 가늚 — <see cref="ScaleEstimate.ThinOutline"/>) 그 값들 중에서.
    /// 자동 = 덩어리 경계 간격이 뚜렷하면(<see cref="ClearBlockFit"/>) 덩어리 경계 값, 아니면 잰 값 중 경계 일치가 가장 높은 값.
    /// 둘 다 경계 일치가 <see cref="MatchTie"/> 안이면 외곽선 → 덩어리 경계 → 색 경계 순으로(외곽선 두께가 맞으면 정리에서 외곽선이 1px).
    /// 고른 값의 정수배인 값이 거의 같게 맞으면(<see cref="DivisorTie"/>) 큰 쪽.
    /// 덩어리 경계 값을 쓸 때만 경계열을 뚜렷한 경계 세기로 고른다(<see cref="ScaleEstimate.BlockCuts"/> — 다른 값은 예전처럼 색 차이 세기로:
    /// 표본에서 그쪽이 충실도·격자 맞음이 조금 더 좋았다).
    /// <paramref name="blocks"/>(<see cref="BlockEdges"/>)가 없으면 여기서 찾는다. 모든 값(정확한 배수 포함)은 <paramref name="maxScale"/>(<see cref="MaxCellFor"/>,
    /// <see cref="ScaleEstimate.MaxScale"/>) 이하에서만 잰다 — 넘는 외곽선 두께는 못 잰 것(0)으로.
    /// </summary>
    public static ScaleEstimate EstimateScale(PixelImage cleaned, int[]? labels, int label, Rectangle bounds, double jitter,
        ScaleSource source = ScaleSource.Auto, double darkness = 0.24, BlockEdgeMap? blocks = null, double maxScale = MaxScale)
    {
        maxScale = Math.Clamp(maxScale, 1, MaxScale);
        var (outline, samples) = OutlineThickness(cleaned, labels, label, bounds, darkness);
        if (samples < MinOutlineSamples || outline > maxScale) outline = 0;
        double edges = EdgeScale(cleaned, labels, label, bounds, jitter, maxScale);
        blocks ??= BlockEdges(cleaned, LabOf(cleaned), bounds, labels, label);
        var (block, blockFit) = BlockScale(blocks, bounds, maxScale);
        var match = new AlignmentMatch(blocks, bounds, jitter);
        var est = new ScaleEstimate(0, false, outline, edges)
        {
            Blocks = block,
            BlocksFit = blockFit,
            OutlineMatch = match.Of(outline),
            EdgesMatch = match.Of(edges),
            BlocksMatch = match.Of(block),
            MaxScale = maxScale,
        };
        if (source != ScaleSource.ColorEdges && ExactScale(cleaned, labels, label, bounds, maxScale) is { } exact)
            return est with { Scale = exact, Exact = true };

        // 순서 = 비슷하면 먼저 고를 쪽.
        var all = new[] { (ScaleSource.Outline, outline, est.OutlineMatch), (ScaleSource.EdgeLines, block, est.BlocksMatch), (ScaleSource.ColorEdges, edges, est.EdgesMatch) };
        (ScaleSource Source, double Scale, double Match)? Pick(IEnumerable<(ScaleSource Source, double Scale, double Match)> options)
        {
            var valid = options.Where(o => o.Scale > 0).ToList();
            if (valid.Count == 0) return null;
            double top = valid.Max(o => o.Match);
            var pick = valid.First(o => o.Match >= top - MatchTie);
            // 정수배인 값이 거의 같게 맞으면 큰 쪽(가장 큰 것).
            foreach (var o in valid.OrderByDescending(o => o.Scale))
            {
                double ratio = o.Scale / pick.Scale;
                int k = (int)Math.Round(ratio);
                if (k >= 2 && Math.Abs(ratio - k) <= 0.08 * k && o.Match >= pick.Match - DivisorTie) return o;
            }
            return pick;
        }
        ScaleEstimate Use((ScaleSource Source, double Scale, double Match)? p) => p is { } v ? est with { Scale = v.Scale, Used = v.Source } : est;
        ScaleEstimate OutlineFirst()
        {
            var same = Pick(all.Where(o => Math.Abs(o.Item2 / outline - 1) <= SameCellTolerance))!.Value;
            var larger = all.Where(o => o.Item2 > outline * (1 + SameCellTolerance) && o.Item3 > same.Match + ThinOutlineMargin).ToList();
            return larger.Count == 0 ? Use(same) : Use(Pick(larger)) with { ThinOutline = true };
        }
        return source switch
        {
            ScaleSource.ColorEdges => edges > 0 ? est with { Scale = edges, Used = ScaleSource.ColorEdges } : est,
            ScaleSource.EdgeLines when block > 0 => est with { Scale = block, Used = ScaleSource.EdgeLines },
            ScaleSource.Outline when outline > 0 => OutlineFirst(),
            _ when block > 0 && blockFit >= ClearBlockFit => est with { Scale = block, Used = ScaleSource.EdgeLines, ClearBlocks = true },
            _ => Use(Pick(all)),
        };
    }

    /// <summary>
    /// 바깥 외곽선 두께(원본 px, 실수)와 잰 단면 수: 가로·세로 줄마다 투명 → 그림으로 들어서는 자리(양쪽 방향)에서 어두운 픽셀(OKLab L ≤ <paramref name="darkness"/>)이
    /// 이어지는 길이를 잰다(안쪽 밝은 색에서 끝난 단면만 — 투명에서 끝난 가는 조각은 뺌). 안쪽 번진 경계 픽셀은 외곽선 색이 섞인 비율만큼 더한다
    /// (RGB에서 안쪽 색 → 외곽선 색 쪽으로 얼마나 갔는지). 중앙값 둘레(0.6~1.4배) 값들의 평균.
    /// </summary>
    public static (double Thickness, int Samples) OutlineThickness(PixelImage cleaned, int[]? labels, int label, Rectangle bounds, double darkness)
    {
        var px = cleaned.Pixels;
        int w = cleaned.Width;
        var darkCache = new Dictionary<uint, bool>();
        bool On(int i) => px[i].A != 0 && (labels is null || labels[i] == label);
        bool Dark(int i)
        {
            var c = px[i];
            if (!darkCache.TryGetValue(c.Argb, out bool d)) darkCache[c.Argb] = d = OkLab.FromColor(c).L <= darkness;
            return d;
        }
        var runs = new List<double>();
        void Measure(int start, int step, int count, int t, int dir)
        {
            int k = 0;
            for (int u = t; u >= 0 && u < count; u += dir)
            {
                int i = start + u * step;
                if (!On(i)) return; // 투명에서 끝남(가는 조각)
                if (!Dark(i)) break;
                if (++k > MaxRun) return;
            }
            int end = t + dir * k;
            if (k == 0 || end < 0 || end >= count) return;
            double run = k;
            int far = end + dir * 3;
            if (far >= 0 && far < count && On(start + far * step))
            {
                var d = px[start + (t + dir * (k / 2)) * step];
                var f = px[start + far * step];
                double dr = d.R - f.R, dg = d.G - f.G, db = d.B - f.B, len = dr * dr + dg * dg + db * db;
                if (len >= 40 * 40)
                    for (int u = end; u != far; u += dir)
                    {
                        var p = px[start + u * step];
                        run += Math.Clamp(((p.R - f.R) * dr + (p.G - f.G) * dg + (p.B - f.B) * db) / len, 0, 1);
                    }
            }
            runs.Add(run);
        }
        void Line(int start, int step, int count)
        {
            for (int t = 0; t < count; t++)
            {
                int i = start + t * step;
                if (!On(i)) continue;
                if (t == 0 || !On(i - step)) Measure(start, step, count, t, +1);
                if (t == count - 1 || !On(i + step)) Measure(start, step, count, t, -1);
            }
        }
        for (int y = bounds.Top; y < bounds.Bottom; y++) Line(y * w + bounds.Left, 1, bounds.Width);
        for (int x = bounds.Left; x < bounds.Right; x++) Line(bounds.Top * w + x, w, bounds.Height);
        if (runs.Count == 0) return (0, 0);
        runs.Sort();
        double median = runs[runs.Count / 2];
        var near = runs.Where(r => r >= median * 0.6 && r <= median * 1.4).ToList();
        return (near.Count > 0 ? near.Average() : median, runs.Count);
    }

    /// <summary>
    /// 색 경계 방식 칸 크기: <see cref="ScaleFits"/>의 가장 높은 봉우리 — 그 절반 자리 봉우리도 가장 높은 값에서 0.15 안이면 절반으로(되풀이). 구간이 없으면 0.
    /// 후보는 <paramref name="maxScale"/>(<see cref="MaxCellFor"/>) 이하.
    /// </summary>
    public static double EdgeScale(PixelImage cleaned, int[]? labels, int label, Rectangle bounds, double jitter, double maxScale = MaxScale)
    {
        double max = Math.Min(maxScale, Math.Max(MaxRun / 2.0, Math.Min(MaxScale, Math.Max(bounds.Width, bounds.Height) / 4.0)));
        var fits = ScaleFits(RunLengths(cleaned, labels, label, bounds), jitter, max);
        var peaks = new List<int>();
        for (int i = 1; i + 1 < fits.Count; i++)
            if (fits[i].Fit >= fits[i - 1].Fit && fits[i].Fit >= fits[i + 1].Fit) peaks.Add(i);
        if (peaks.Count == 0) return 0;
        int top = peaks.MaxBy(i => fits[i].Fit);
        double bestFit = fits[top].Fit;
        while (true)
        {
            double half = fits[top].Scale / 2, tol = Math.Max(0.75, half * 0.1);
            int next = -1;
            foreach (int p in peaks)
                if (Math.Abs(fits[p].Scale - half) <= tol && fits[p].Scale >= 4 && fits[p].Fit >= bestFit - 0.15 && (next < 0 || fits[p].Fit > fits[next].Fit))
                    next = p;
            if (next < 0) break;
            top = next;
        }
        return fits[top].Scale;
    }

    /// <summary>
    /// 색이 <see cref="CleanColorLimit"/>개 이하일 때: 색이 조금이라도 바뀌는 자리 사이 길이(두 줄마다, <see cref="MaxSpan"/>px까지)의 90% 이상을 나누는
    /// 가장 큰 정수(2~<see cref="MaxScale"/>, <paramref name="maxScale"/> 이하) — 정수 배 확대 그대로(100px 넘는 칸도).
    /// 그런 정수가 없으면 1-px 단위 그림일 때만 1, 아니면 null — 색만 적은 AI 그림처럼 칸이 2px 이상이지만 위치가 흔들리면(정수배가 아님)
    /// 흔들림을 따라가는 일반 어림(색 경계·외곽선 두께)으로. 색 수만으로는 1배로 정하지 않는다. 1-px 단위 그림 = 1~2px 구간이 픽셀의
    /// <see cref="FinePixelShare"/> 이상이거나, 넓은 칠 사이 1px 외곽선처럼 1px 구간이 구간 수의 <see cref="FineRunShare"/> 이상이면서 그중 두 이웃 색의
    /// 중간 색(번진 경계 — 색을 줄인 AI 그림)이 <see cref="BlendRunShare"/> 이하.
    /// </summary>
    private static double? ExactScale(PixelImage img, int[]? labels, int label, Rectangle bounds, double maxScale)
    {
        var px = img.Pixels;
        int w = img.Width;
        bool On(int i) => px[i].A != 0 && (labels is null || labels[i] == label);
        var colors = new HashSet<uint>();
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                int i = y * w + x;
                if (On(i) && colors.Add(px[i].Argb) && colors.Count > CleanColorLimit) return null;
            }
        var hist = new int[MaxSpan + 1];
        int ones = 0, blends = 0;
        void Line(int start, int step, int count)
        {
            int last = -1;
            for (int t = 1; t < count; t++)
            {
                int i = start + t * step, j = i - step;
                bool a = On(i), b = On(j);
                if (a == b && (!a || px[i] == px[j])) continue;
                if (last >= 0 && On(start + last * step) && t - last <= MaxSpan)
                {
                    hist[t - last]++;
                    // 1px 구간 j의 양옆(j−1, i)이 모두 그림이면 두 색의 중간 색인지(번진 경계).
                    if (t - last == 1 && last >= 1 && a && On(j - step))
                    {
                        ones++;
                        if (Between(px[j - step], px[j], px[i])) blends++;
                    }
                }
                last = t;
            }
        }
        // 범위 바깥 1px(그림 안이면)까지 읽어 범위 가장자리에 닿은 구간(바깥 외곽선 1px 등)도 센다.
        int x0 = Math.Max(0, bounds.Left - 1), x1 = Math.Min(w, bounds.Right + 1);
        int y0 = Math.Max(0, bounds.Top - 1), y1 = Math.Min(img.Height, bounds.Bottom + 1);
        for (int y = bounds.Top; y < bounds.Bottom; y += 2) Line(y * w + x0, 1, x1 - x0);
        for (int x = bounds.Left; x < bounds.Right; x += 2) Line(y0 * w + x, w, y1 - y0);
        double total = hist.Sum();
        if (total == 0) return null;
        for (int s = (int)Math.Min(MaxScale, maxScale); s >= 2; s--)
        {
            double on = 0;
            for (int r = s; r <= MaxSpan; r += s) on += hist[r];
            if (on >= total * 0.9) return s;
        }
        double pixels = 0;
        for (int r = 1; r <= MaxSpan; r++) pixels += (double)r * hist[r];
        if (hist[1] + 2.0 * hist[2] >= pixels * FinePixelShare) return 1;
        return hist[1] >= total * FineRunShare && blends <= ones * BlendRunShare ? 1 : null;

        // c가 a와 b 사이(RGB 선분 근처 — a→c→b 길이가 a→b의 1.2배 + 1 이하).
        static bool Between(Color32 a, Color32 c, Color32 b)
        {
            static double D(Color32 p, Color32 q) => Math.Sqrt((p.R - q.R) * (p.R - q.R) + (p.G - q.G) * (p.G - q.G) + (p.B - q.B) * (p.B - q.B));
            return D(a, c) + D(c, b) <= 1.2 * D(a, b) + 1;
        }
    }

    /// <summary>1배 그림으로 볼 1~2px 구간 픽셀 비율(번진 경계 한 줄이 끼어든 큰 칸 그림은 칸 크기의 몇 분의 1뿐).</summary>
    private const double FinePixelShare = 0.25;
    /// <summary>넓은 칠이 많은 1배 그림으로 볼 1px 구간 수 비율(표본: 1px 외곽선 그림 0.33~0.51, 색을 줄인 AI 그림 0.46 — 그래서 <see cref="BlendRunShare"/>도 봄).</summary>
    private const double FineRunShare = 0.25;
    /// <summary>1px 구간 중 두 이웃 색의 중간 색 비율이 이 이하여야 1배(1배 그림 0~0.27, 색을 줄인 AI 그림의 번진 경계 0.61).</summary>
    private const double BlendRunShare = 0.4;

    /// <summary>원본 좌표(lo~hi) → 칸 번호.</summary>
    internal static int[] Lookup(int[] cuts, int lo, int hi)
    {
        var r = new int[Math.Max(0, hi - lo)];
        int c = 0;
        for (int p = lo; p < hi; p++)
        {
            while (c < cuts.Length - 2 && cuts[c + 1] <= p) c++;
            r[p - lo] = c;
        }
        return r;
    }

    /// <summary>
    /// 경계열 고르기. <paramref name="lo"/>~<paramref name="hi"/>(스프라이트 범위)를 덮는 경계열 중
    /// 간격이 <paramref name="scale"/> × (1 ± <paramref name="jitter"/>) 안이고 (경계 세기 − 평균) 합에서 간격 벗어남 벌점을 뺀 값이 가장 큰 것.
    /// 흔들림이 0이면 고른 간격 격자 중 경계 세기 합이 가장 큰 시작 위치.
    /// </summary>
    public static int[] FitCuts(double[] e, int lo, int hi, double scale, double jitter)
    {
        int len = e.Length - 1;
        scale = Math.Max(1, scale);
        var g = Smooth(e);
        if (jitter <= 0.001) return UniformCuts(lo, hi, scale, BestPhase(g, lo, hi, scale));

        double mean = 0;
        for (int i = lo; i <= hi; i++) mean += g[i];
        mean /= Math.Max(1, hi - lo + 1);
        int dmin = Math.Max(1, (int)Math.Round(scale * (1 - jitter)));
        int dmax = Math.Max(dmin + 1, (int)Math.Round(scale * (1 + jitter)));
        double lambda = 16 * Math.Max(mean, 1e-6);
        int reach = dmax + 1;
        int a = lo - reach, b = hi + reach;
        int n = b - a + 1;
        var best = new double[n];
        var prev = new int[n];
        for (int k = 0; k < n; k++)
        {
            int p = a + k;
            double gain = (p >= 0 && p <= len ? g[p] : 0) - mean;
            best[k] = double.NegativeInfinity;
            prev[k] = -1;
            if (p <= lo && p > lo - reach) best[k] = gain;
            for (int d = dmin; d <= dmax; d++)
            {
                int q = k - d;
                if (q < 0 || double.IsNegativeInfinity(best[q])) continue;
                double dev = (d - scale) / scale;
                double v = best[q] + gain - lambda * dev * dev;
                if (v > best[k]) { best[k] = v; prev[k] = q; }
            }
        }
        int end = -1;
        for (int k = hi - a; k < n; k++)
            if (!double.IsNegativeInfinity(best[k]) && (end < 0 || best[k] > best[end])) end = k;
        if (end < 0) return UniformCuts(lo, hi, scale, BestPhase(g, lo, hi, scale));
        var cuts = new List<int>();
        for (int k = end; k >= 0; k = prev[k]) cuts.Add(a + k);
        cuts.Reverse();
        return [.. cuts];
    }

    /// <summary>[0.25, 0.5, 0.25]로 부드럽게(번진 경계의 봉우리를 한 자리로).</summary>
    private static double[] Smooth(double[] e)
    {
        var g = new double[e.Length];
        for (int i = 0; i < e.Length; i++)
            g[i] = 0.5 * e[i] + 0.25 * (i > 0 ? e[i - 1] : 0) + 0.25 * (i < e.Length - 1 ? e[i + 1] : 0);
        return g;
    }

    private static double BestPhase(double[] g, int lo, int hi, double scale)
    {
        double bestPhase = 0, bestSum = double.NegativeInfinity;
        for (double phase = 0; phase < scale; phase += 0.25)
        {
            double sum = 0;
            foreach (int c in UniformCuts(lo, hi, scale, phase))
                if (c >= 0 && c < g.Length) sum += g[c];
            if (sum > bestSum) { bestSum = sum; bestPhase = phase; }
        }
        return bestPhase;
    }

    /// <summary>시작 위치 <paramref name="phase"/>(원본 px)부터 <paramref name="scale"/> 간격 경계 중 lo~hi를 덮는 것.</summary>
    public static int[] UniformCuts(int lo, int hi, double scale, double phase)
    {
        scale = Math.Max(1, scale);
        double k0 = Math.Floor((lo - phase) / scale + 1e-9);
        var cuts = new List<int>();
        for (double k = k0; ; k++)
        {
            int c = (int)Math.Round(phase + k * scale);
            if (cuts.Count > 0 && c <= cuts[^1]) continue;
            cuts.Add(c);
            if (c >= hi) break;
        }
        return [.. cuts];
    }

    /// <summary>경계가 실제 색 바뀜에 놓인 정도: (경계 자리 평균 세기 − 전체 평균) / 경계 자리 평균(0~1).</summary>
    public static double Confidence(double[] e, int[] cuts, int lo, int hi) => Confidence(e, 0, cuts, lo, hi);

    /// <inheritdoc cref="Confidence(double[], int[], int, int)"/>
    /// <param name="origin"><paramref name="e"/>[0]의 자리(<paramref name="e"/>[i] = 자리 origin + i의 세기, 밖은 0).</param>
    public static double Confidence(double[] e, int origin, int[] cuts, int lo, int hi)
    {
        var g = Smooth(e);
        double all = 0;
        int n = 0;
        for (int i = Math.Max(lo, origin); i <= hi && i - origin < g.Length; i++) { all += g[i - origin]; n++; }
        if (n == 0) return 0;
        all /= n;
        double at = 0;
        int m = 0;
        foreach (int c in cuts)
        {
            if (c < lo || c > hi || c < origin || c - origin >= g.Length) continue;
            at += g[c - origin];
            m++;
        }
        if (m == 0 || at <= 0) return 0;
        at /= m;
        return Math.Clamp((at - all) / at, 0, 1);
    }

    /// <summary>
    /// 한 영역(프레임 범위 또는 그림 전체)의 격자. 자동이면 <paramref name="estimate"/>(<see cref="EstimateScale"/> — 0이면 수동 칸 크기)에
    /// 조정값을 더한 칸 크기로 경계열을 고르고(깨끗한 그림에서 잰 정확한 크기면 흔들림 없이 고른 간격), 수동이면 고른 칸 크기·시작 위치의 고른 간격.
    /// 경계열은 <see cref="ScaleEstimate.BlockCuts"/>이고 <paramref name="blocks"/>가 있으면 뚜렷한 경계 세기로, 아니면 <paramref name="ex"/>·<paramref name="ey"/>로 고른다.
    /// 맞는 정도(<see cref="GridFit.Confidence"/>)는 늘 <paramref name="ex"/>·<paramref name="ey"/>(색 차이 세기)로 잰다.
    /// </summary>
    public static GridFit Fit(double[] ex, double[] ey, Rectangle bounds, PixelGridSettings s, ScaleEstimate estimate, BlockEdgeMap? blocks = null)
    {
        double detected;
        int[] xc, yc;
        if (s.Mode == GridMode.Manual)
        {
            detected = Math.Max(1, s.Scale);
            xc = UniformCuts(bounds.Left, bounds.Right, detected, s.OffsetX);
            yc = UniformCuts(bounds.Top, bounds.Bottom, detected, s.OffsetY);
        }
        else
        {
            double jitter = estimate.Exact ? 0 : Math.Clamp(s.Jitter, 0, 0.45);
            detected = estimate.Scale > 0 ? estimate.Scale : Math.Max(1, s.Scale);
            double prior = Math.Max(1, detected + s.ScaleAdjust);
            var (cx, cy) = estimate.BlockCuts && blocks is not null ? (blocks.X, blocks.Y) : (ex, ey);
            xc = FitCuts(cx, bounds.Left, bounds.Right, prior, jitter);
            yc = FitCuts(cy, bounds.Top, bounds.Bottom, prior, jitter);
        }
        double scale = ((xc[^1] - xc[0]) + (yc[^1] - yc[0])) / (double)Math.Max(1, xc.Length - 1 + yc.Length - 1);
        double conf = 0.5 * (Confidence(ex, xc, bounds.Left, bounds.Right) + Confidence(ey, yc, bounds.Top, bounds.Bottom));
        return new GridFit(xc, yc, scale, detected, conf);
    }
}
