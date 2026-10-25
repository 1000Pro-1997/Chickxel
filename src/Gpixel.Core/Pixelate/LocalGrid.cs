using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Pixelate;

/// <summary>
/// 칸 경계 지역 보정 결과: 세로 칸 경계(x)는 가로 띠(칸 <see cref="Band"/>줄씩)마다 따로(<see cref="XByBand"/>[띠] — 길이 = 열 수 + 1),
/// 가로 칸 경계(y)는 세로 띠(칸 <see cref="Band"/>칸씩)마다 따로(<see cref="YByBand"/>[띠] — 길이 = 행 수 + 1). 칸 (c, r)은
/// x = <c>XByBand[r / Band][c..c+1]</c>, y = <c>YByBand[c / Band][r..r+1]</c>인 사각형 — 띠 안에서는 곧은 경계, 띠가 바뀌는 자리에서 경계가 조금 꺾인다.
/// </summary>
public sealed record LocalCuts(int Band, int[][] XByBand, int[][] YByBand);

/// <summary>
/// 한 프레임의 칸 배치: 전체 칸 경계(<see cref="XCuts"/>·<see cref="YCuts"/>, 원본 px)와 지역 보정(<see cref="Local"/>, 없으면 곧은 격자).
/// 칸 사각형(<see cref="Cell"/>), 원본 자리 → 칸(<see cref="Locate"/> — 픽셀 하나는 늘 칸 하나), 칸 안 실수 좌표(<see cref="ToCell"/>).
/// </summary>
public sealed class CellLayout
{
    public CellLayout(int[] xCuts, int[] yCuts, LocalCuts? local = null)
    {
        XCuts = xCuts;
        YCuts = yCuts;
        Local = local;
    }

    public int[] XCuts { get; }
    public int[] YCuts { get; }
    public LocalCuts? Local { get; }
    public int Columns => XCuts.Length - 1;
    public int Rows => YCuts.Length - 1;

    /// <summary>행 <paramref name="row"/>의 세로 칸 경계(x).</summary>
    public int[] XCutsFor(int row) => Local is null ? XCuts : Local.XByBand[Math.Clamp(row / Local.Band, 0, Local.XByBand.Length - 1)];

    /// <summary>열 <paramref name="col"/>의 가로 칸 경계(y).</summary>
    public int[] YCutsFor(int col) => Local is null ? YCuts : Local.YByBand[Math.Clamp(col / Local.Band, 0, Local.YByBand.Length - 1)];

    /// <summary>칸 (c, r)의 사각형 [X0, X1) × [Y0, Y1)(원본 px).</summary>
    public (int X0, int Y0, int X1, int Y1) Cell(int c, int r)
    {
        var xs = XCutsFor(r);
        var ys = YCutsFor(c);
        return (xs[c], ys[r], xs[c + 1], ys[r + 1]);
    }

    /// <summary>
    /// 원본 자리 (x, y)가 든 칸(경계 밖이면 가장 가까운 끝 칸, 안이면 true). 지역 보정이면 전체 경계로 열을 어림한 뒤 그 열의 가로 경계로 행,
    /// 그 행의 세로 경계로 열을 두 번 되풀이해 정한다 — 띠가 바뀌는 모서리의 작은 겹침·틈도 칸 하나로 간다.
    /// </summary>
    public bool Locate(double x, double y, out int col, out int row)
    {
        col = Find(XCuts, x);
        row = Find(YCuts, y);
        if (Local is not null)
            for (int k = 0; k < 2; k++)
            {
                row = Find(YCutsFor(col), y);
                col = Find(XCutsFor(row), x);
            }
        var (x0, y0, x1, y1) = Cell(col, row);
        return x >= x0 && x < x1 && y >= y0 && y < y1;
    }

    /// <summary>원본 자리 → 칸 좌표(실수, 칸 (c, r)의 안쪽이면 c ≤ X &lt; c + 1).</summary>
    public (double X, double Y) ToCell(double x, double y)
    {
        Locate(x, y, out int c, out int r);
        var (x0, y0, x1, y1) = Cell(c, r);
        return (c + (x - x0) / Math.Max(1, x1 - x0), r + (y - y0) / Math.Max(1, y1 - y0));
    }

    /// <summary>cuts[c] ≤ p &lt; cuts[c + 1]인 c(밖이면 끝 칸으로 자름).</summary>
    private static int Find(int[] cuts, double p)
    {
        int c = Array.BinarySearch(cuts, (int)Math.Floor(p));
        if (c < 0) c = ~c - 1;
        return Math.Clamp(c, 0, cuts.Length - 2);
    }
}

/// <summary>
/// 칸 경계 지역 보정: 축 전체에 한 줄로 고른 칸 경계(<see cref="GridDetector.Fit"/>)는 그림 한쪽이 반 칸쯤 어긋나 그려진 AI 그림에 맞지 않는다.
/// 그림을 가로 띠(칸 몇 줄)로 나눠 띠마다 그 띠의 뚜렷한 경계(<see cref="GridDetector.BlockEdges"/>)만 세어 세로 칸 경계를 동적 계획법으로 다시 고르고
/// (경계마다 전체 자리에서 칸 크기의 ±최대 이동 안, 칸 너비는 원래 너비의 <see cref="MinWidth"/>~<see cref="MaxWidth"/>배, 경계가 없으면 전체 자리로
/// 끌리고 바로 앞 띠와 다르면 벌점 — 띠 사이가 매끄럽게), 가로 칸 경계도 세로 띠마다 같은 방법으로 고른다(<see cref="LocalCuts"/>).
/// 같은 문제를 푸는 '교차점을 가까운 경계로 끌어당기는 탄성 격자'보다 띠마다 곧은 경계(칸 = 사각형)가 단순하고 결과가 안정적이며, 칸마다 엇갈린
/// 벽돌 무늬(띠 = 1칸)도 그대로 따라간다.
/// </summary>
public static class LocalGrid
{
    /// <summary>칸 너비의 최소·최대 비율(원래 너비 기준).</summary>
    public const double MinWidth = 0.7, MaxWidth = 1.3;
    /// <summary>전체 자리에서 벗어난 만큼의 벌점(칸 크기로 나눈 거리의 제곱 × 이 값) — 경계가 없는 곳은 전체 자리 그대로.</summary>
    private const double Pull = 0.2;
    /// <summary>바로 앞 띠의 자리와 다른 만큼의 벌점(같은 꼴) — 띠 사이 매끄럽게.</summary>
    private const double Smooth = 0.2;
    /// <summary>
    /// 지역 보정을 할 가장 작은 평균 칸 크기(원본 px): 이보다 작으면 최대 이동(±35%)이 1px 남짓이라 흐리게 키운 그림의 경계 자리 흔들림(±1px)과 가릴 수 없다
    /// (표본 물 기계 3.9px 칸: 켜면 충실도 0.026 → 0.038로 나빠짐).
    /// </summary>
    public const double MinCell = 5;

    /// <summary>
    /// 지역 보정한 칸 경계. <paramref name="edges"/> = 이 프레임의 뚜렷한 경계(<see cref="GridDetector.BlockEdgeMap.Rows"/>·<see cref="GridDetector.BlockEdgeMap.Cols"/>),
    /// <paramref name="maxShift"/> = 전체 자리에서 움직일 수 있는 거리(칸 크기 비율), <paramref name="band"/> = 띠 너비(칸). 어느 띠도 바뀌지 않거나
    /// 칸이 <see cref="MinCell"/>보다 작으면 null(곧은 격자).
    /// </summary>
    public static LocalCuts? Correct(GridDetector.BlockEdgeMap edges, int[] xc, int[] yc, double maxShift, int band)
    {
        band = (int)PixelateRanges.LocalBand.Clamp(band);
        maxShift = PixelateRanges.LocalShift.Clamp(maxShift);
        if (xc.Length < 2 || yc.Length < 2 || edges.Rows.Length == 0 || edges.Cols.Length == 0) return null;
        double cell = ((xc[^1] - xc[0]) + (yc[^1] - yc[0])) / (double)(xc.Length - 1 + yc.Length - 1);
        if (cell < MinCell) return null;
        var xb = FitAxis(xc, yc, band, maxShift, edges.Rows, edges.Area.Top);
        var yb = FitAxis(yc, xc, band, maxShift, edges.Cols, edges.Area.Left);
        bool changed = xb.Any(b => !b.AsSpan().SequenceEqual(xc)) || yb.Any(b => !b.AsSpan().SequenceEqual(yc));
        return changed ? new LocalCuts(band, xb, yb) : null;
    }

    /// <summary>
    /// 한 축의 띠별 경계. <paramref name="cuts"/> = 고를 축의 전체 경계, <paramref name="across"/> = 다른 축 경계(띠를 나눔),
    /// <paramref name="lines"/>[k] = 다른 축 자리 <paramref name="origin"/> + k 줄의 경계 자리.
    /// </summary>
    private static int[][] FitAxis(int[] cuts, int[] across, int band, double maxShift, int[][] lines, int origin)
    {
        int n = across.Length - 1;
        int bands = (n + band - 1) / band;
        var result = new int[bands][];
        int reach = (int)Math.Ceiling(maxShift * MaxGap(cuts)) + 2;
        int lo = cuts[0] - reach, hi = cuts[^1] + reach;
        var profile = new double[hi - lo + 1];
        var g = new double[profile.Length];
        int[]? prev = null;
        for (int k = 0; k < bands; k++)
        {
            int a = Math.Max(across[k * band], origin), b = Math.Min(across[Math.Min(n, (k + 1) * band)], origin + lines.Length);
            Array.Clear(profile);
            int count = 0;
            for (int y = a; y < b; y++, count++)
                foreach (int x in lines[y - origin])
                    if (x >= lo && x <= hi) profile[x - lo]++;
            if (count == 0)
            {
                result[k] = prev ?? (int[])cuts.Clone();
                continue;
            }
            // 줄 수로 나누고(그 자리에 경계가 있는 줄 비율) [0.25, 0.5, 0.25]로 부드럽게(번진 경계 ±1px).
            for (int i = 0; i < g.Length; i++)
                g[i] = (0.5 * profile[i] + 0.25 * (i > 0 ? profile[i - 1] : 0) + 0.25 * (i + 1 < g.Length ? profile[i + 1] : 0)) / count;
            result[k] = Fit(cuts, g, lo, maxShift, prev);
            prev = result[k];
        }
        return result;
    }

    private static int MaxGap(int[] cuts)
    {
        int m = 1;
        for (int i = 1; i < cuts.Length; i++) m = Math.Max(m, cuts[i] - cuts[i - 1]);
        return m;
    }

    /// <summary>
    /// 동적 계획법: 경계 i를 전체 자리 ± <paramref name="maxShift"/> × 칸 크기 안에서, 이웃 경계와의 간격이 원래 간격의 <see cref="MinWidth"/>~<see cref="MaxWidth"/>배이면서
    /// (경계 세기 − 전체 자리에서 벗어난 벌점 − 앞 띠 자리와 다른 벌점) 합이 가장 큰 자리로.
    /// </summary>
    private static int[] Fit(int[] cuts, double[] g, int lo, double maxShift, int[]? prev)
    {
        int m = cuts.Length;
        var start = new int[m];
        var score = new double[m][];
        var from = new int[m][];
        for (int i = 0; i < m; i++)
        {
            double gap = Gap(cuts, i);
            int r = (int)Math.Floor(maxShift * gap);
            start[i] = cuts[i] - r;
            score[i] = new double[2 * r + 1];
            from[i] = new int[2 * r + 1];
        }
        double Unary(int i, int x)
        {
            double gap = Gap(cuts, i);
            int p = x - lo;
            double v = p >= 0 && p < g.Length ? g[p] : 0;
            double d = (x - cuts[i]) / gap;
            v -= Pull * d * d;
            if (prev is not null)
            {
                double e = (x - prev[i]) / gap;
                v -= Smooth * e * e;
            }
            return v;
        }
        for (int j = 0; j < score[0].Length; j++) score[0][j] = Unary(0, start[0] + j);
        for (int i = 1; i < m; i++)
        {
            int baseGap = cuts[i] - cuts[i - 1];
            int dmin = Math.Max(1, (int)Math.Ceiling(baseGap * MinWidth)), dmax = Math.Max(dmin, (int)Math.Floor(baseGap * MaxWidth));
            for (int j = 0; j < score[i].Length; j++)
            {
                int x = start[i] + j;
                double best = double.NegativeInfinity;
                int arg = -1;
                for (int q = 0; q < score[i - 1].Length; q++)
                {
                    int d = x - (start[i - 1] + q);
                    if (d < dmin || d > dmax || score[i - 1][q] <= best) continue;
                    best = score[i - 1][q];
                    arg = q;
                }
                score[i][j] = arg < 0 ? double.NegativeInfinity : best + Unary(i, x);
                from[i][j] = arg;
            }
        }
        int end = -1;
        for (int j = 0; j < score[m - 1].Length; j++)
            if (!double.IsNegativeInfinity(score[m - 1][j]) && (end < 0 || score[m - 1][j] > score[m - 1][end])) end = j;
        if (end < 0) return (int[])cuts.Clone();
        var result = new int[m];
        for (int i = m - 1, j = end; i >= 0; j = from[i][j], i--) result[i] = start[i] + j;
        return result;
    }

    /// <summary>경계 i 둘레 칸 크기(양옆 간격 평균).</summary>
    private static double Gap(int[] cuts, int i)
    {
        int a = i > 0 ? cuts[i] - cuts[i - 1] : cuts[1] - cuts[0];
        int b = i + 1 < cuts.Length ? cuts[i + 1] - cuts[i] : cuts[i] - cuts[i - 1];
        return Math.Max(1, 0.5 * (a + b));
    }

    /// <summary>
    /// 지역 보정한 격자가 색 바뀜과 맞는 정도(<see cref="GridDetector.Confidence"/>를 띠마다 그 띠의 색 차이 세기와 그 띠의 경계로 재서 띠 크기로 평균).
    /// </summary>
    public static double Confidence(PixelImage cleaned, int[]? labels, int label, Rectangle bounds, CellLayout layout)
    {
        var local = layout.Local!;
        double sx = 0, wx = 0, sy = 0, wy = 0;
        for (int k = 0; k < local.XByBand.Length; k++)
        {
            int a = Math.Max(bounds.Top, layout.YCuts[Math.Min(layout.Rows, k * local.Band)]);
            int b = Math.Min(bounds.Bottom, layout.YCuts[Math.Min(layout.Rows, (k + 1) * local.Band)]);
            if (b <= a) continue;
            var (ex, _) = GridDetector.Profiles(cleaned, new Rectangle(bounds.Left, a, bounds.Width, b - a), labels, label);
            sx += (b - a) * GridDetector.Confidence(ex, local.XByBand[k], bounds.Left, bounds.Right);
            wx += b - a;
        }
        for (int k = 0; k < local.YByBand.Length; k++)
        {
            int a = Math.Max(bounds.Left, layout.XCuts[Math.Min(layout.Columns, k * local.Band)]);
            int b = Math.Min(bounds.Right, layout.XCuts[Math.Min(layout.Columns, (k + 1) * local.Band)]);
            if (b <= a) continue;
            var (_, ey) = GridDetector.Profiles(cleaned, new Rectangle(a, bounds.Top, b - a, bounds.Height), labels, label);
            sy += (b - a) * GridDetector.Confidence(ey, local.YByBand[k], bounds.Top, bounds.Bottom);
            wy += b - a;
        }
        return 0.5 * ((wx > 0 ? sx / wx : 0) + (wy > 0 ? sy / wy : 0));
    }
}
