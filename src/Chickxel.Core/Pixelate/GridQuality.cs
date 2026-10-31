using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Pixelate;

/// <summary>
/// 칸 나누기 점수(칸 맞추기 먼저의 격자 단계 — <see cref="PixelateOptimizer.RunGrid"/>): 원본과 칸 경계만 보고 잰다(팔레트·정리 결과는 보지 않음).
/// <list type="bullet">
/// <item>경계(<see cref="Edges"/>): 원본의 뚜렷한 경계(<see cref="GridDetector.BlockEdges"/> — 종이 결 같은 약한 무늬는 빼고, 그림·투명이 바뀌는 자리는 넣음)와 칸 경계가
/// 줄마다 ±1px 안에서 만나는 정도. <see cref="Recall"/> = 원본 경계 중 칸 경계가 잡은 비율(칸이 진짜보다 크면 낮음), <see cref="Precision"/> = 그림을 지나는 칸 경계 중
/// 원본 경계 위에 놓인 비율(칸이 진짜보다 작으면 빈 자리를 잘라 낮음)의 조화 평균. 둘 다 우연 수준을 뺀 값이다(우연 수준 c → (값 − c) / (1 − c), 0 아래는 0):
/// 잡은 비율의 우연 수준 = 그림을 지나는 줄의 자리 중 칸 경계 ±1px 안인 비율(칸이 작을수록 아무 데나 잡힘 — 3px 칸이면 거의 다),
/// 놓인 비율의 우연 수준 = 같은 자리 중 원본 경계 ±1px 안인 비율. 빼지 않으면 칸 경계를 조금 더 촘촘히 두는 쪽(진짜보다 작은 칸)이 이겼다.</item>
/// <item>색 고름(<see cref="Purity"/>): 칸마다 안쪽(둘레 칸 크기의 1/8, 적어도 1px — 작은 칸은 1/4까지 — 는 번지거나 흔들린 자리로 보고 뺌) 그림 픽셀 중 우세 색 묶음
/// (OKLab 거리가 묶음 범위 안인 것끼리 묶은 가장 큰 묶음)에 드는 비율(픽셀 수 무게) — 칸이 두 색에 걸치면 낮다. 안쪽만 보므로 칸을 진짜보다 잘게 나눠도 별로 오르지 않는다.</item>
/// </list>
/// </summary>
public readonly record struct GridQuality(double Recall, double Precision, double Purity)
{
    public double Edges => Recall + Precision <= 0 ? 0 : 2 * Recall * Precision / (Recall + Precision);

    /// <summary>0~100: 경계와 색 고름의 평균.</summary>
    public double Total => 50 * (Edges + Purity);

    /// <summary>결과 <paramref name="r"/>의 칸 나누기(<see cref="PixelateResult.Maps"/> — 지역 보정 포함)를 준비한 원본 <paramref name="src"/>에서 잰다.</summary>
    /// <param name="radius">같은 색으로 볼 OKLab 거리(<see cref="FidelityJudge.GroupRadius"/>).</param>
    public static GridQuality Measure(PixelateSource src, PixelateResult r, double radius)
    {
        var meter = new Meter(src, radius);
        for (int f = 0; f < src.Split.Frames.Count && f < r.Maps.Count; f++) meter.Frame(f, r.Maps[f].Layout());
        return meter.Result();
    }

    /// <summary>프레임마다 센 수를 모은다.</summary>
    private sealed class Meter(PixelateSource src, double radius)
    {
        /// <summary>한 칸에서 따로 세는 색 묶음 수 상한(잡음이 심한 칸에서 계산이 늘지 않게 — 넘는 픽셀은 우세 묶음에 들지 않은 것으로 셈).</summary>
        private const int MaxGroups = 32;

        private readonly Color32[] _px = src.Cleaned.Pixels;
        private readonly int[] _labels = src.Split.Labels;
        private readonly Lab[] _lab = src.Lab;
        private readonly int _w = src.Cleaned.Width;
        private readonly float _r2 = (float)(radius * radius);
        private readonly List<(Lab Seed, int N)> _groups = new(MaxGroups);
        private Rectangle _b;
        private int _f;
        // 경계 잡음·놓임 수, 칸 안쪽 그림 픽셀·우세 묶음 픽셀 수, 우연 수준을 잴 자리 수(그림을 지나는 줄의 자리)와 그중 칸 경계·원본 경계 ±1px 안인 수.
        private long _edges, _caught, _crossings, _onEdge, _painted, _dominant, _positions, _nearCut, _nearEdge;

        public GridQuality Result() => new(
            BeyondChance(Ratio(_caught, _edges), Ratio(_nearCut, _positions)),
            BeyondChance(Ratio(_onEdge, _crossings), Ratio(_nearEdge, _positions)),
            Ratio(_dominant, _painted));

        private static double Ratio(long a, long b) => b == 0 ? 0 : a / (double)b;

        /// <summary>우연 수준 <paramref name="chance"/>를 뺀 비율: (v − c) / (1 − c), 0 아래는 0.</summary>
        private static double BeyondChance(double v, double chance) => chance >= 1 ? 0 : Math.Max(0, (v - chance) / (1 - chance));

        private bool On(int x, int y)
        {
            if (x < _b.Left || x >= _b.Right || y < _b.Top || y >= _b.Bottom) return false;
            int i = y * _w + x;
            return _px[i].A != 0 && _labels[i] == _f;
        }

        public void Frame(int f, CellLayout layout)
        {
            _f = f;
            _b = src.Split.Frames[f].Bounds;
            var map = src.BlockEdges(f);
            // 가로 줄마다 세로 칸 경계(x)를, 세로 줄마다 가로 칸 경계(y)를 그 줄의 원본 경계와 견준다(지역 보정이면 그 줄이 든 띠의 경계).
            if (map.Rows.Length == _b.Height && map.Cols.Length == _b.Width)
            {
                for (int y = _b.Top; y < _b.Bottom; y++)
                {
                    var cuts = layout.XCutsFor(Find(layout.YCuts, y));
                    Count(map.Rows[y - _b.Top], cuts);
                    for (int p = _b.Left + 1; p < _b.Right; p++)
                        if (On(p - 1, y) || On(p, y)) { _positions++; if (Near(cuts, p)) _nearCut++; if (Near(map.Rows[y - _b.Top], p)) _nearEdge++; }
                    foreach (int c in cuts)
                        if (c > _b.Left && c < _b.Right && (On(c - 1, y) || On(c, y))) Crossing(map.Rows[y - _b.Top], c);
                }
                for (int x = _b.Left; x < _b.Right; x++)
                {
                    var cuts = layout.YCutsFor(Find(layout.XCuts, x));
                    Count(map.Cols[x - _b.Left], cuts);
                    for (int p = _b.Top + 1; p < _b.Bottom; p++)
                        if (On(x, p - 1) || On(x, p)) { _positions++; if (Near(cuts, p)) _nearCut++; if (Near(map.Cols[x - _b.Left], p)) _nearEdge++; }
                    foreach (int c in cuts)
                        if (c > _b.Top && c < _b.Bottom && (On(x, c - 1) || On(x, c))) Crossing(map.Cols[x - _b.Left], c);
                }
            }
            for (int row = 0; row < layout.Rows; row++)
                for (int col = 0; col < layout.Columns; col++)
                {
                    var (x0, y0, x1, y1) = layout.Cell(col, row);
                    int qx = Ring(x1 - x0), qy = Ring(y1 - y0);
                    Cell(Rectangle.Intersect(Rectangle.FromLTRB(x0 + qx, y0 + qy, x1 - qx, y1 - qy), _b));
                }
        }

        /// <summary>칸 둘레에서 뺄 폭: 칸 크기의 1/8(적어도 1px), 작은 칸은 1/4까지(<see cref="Pixelator"/>의 칸 안 구조 손실과 같은 규칙).</summary>
        private static int Ring(int size) => Math.Min(size / 4, Math.Max(1, (int)Math.Round(size / 8.0)));

        /// <summary>한 줄의 원본 경계(오름차순)마다 ±1px 안에 칸 경계가 있는지.</summary>
        private void Count(int[] found, int[] cuts)
        {
            _edges += found.Length;
            foreach (int e in found)
                if (Near(cuts, e)) _caught++;
        }

        /// <summary>그림을 지나는 칸 경계 하나: ±1px 안에 원본 경계가 있는지.</summary>
        private void Crossing(int[] found, int cut)
        {
            _crossings++;
            if (Near(found, cut)) _onEdge++;
        }

        /// <summary>칸 하나의 그림 픽셀을 비슷한 색끼리 묶어(씨앗과의 거리가 묶음 범위 안인 가장 가까운 묶음, 없으면 새 묶음) 가장 큰 묶음의 픽셀 수를 센다.</summary>
        private void Cell(Rectangle cell)
        {
            _groups.Clear();
            int n = 0;
            for (int y = cell.Top; y < cell.Bottom; y++)
                for (int x = cell.Left; x < cell.Right; x++)
                {
                    if (!On(x, y)) continue;
                    n++;
                    var c = _lab[y * _w + x];
                    int best = -1;
                    float bestD = _r2;
                    for (int g = 0; g < _groups.Count; g++)
                    {
                        float d = Lab.DistanceSquared(c, _groups[g].Seed);
                        if (d <= bestD) { bestD = d; best = g; }
                    }
                    if (best >= 0) _groups[best] = (_groups[best].Seed, _groups[best].N + 1);
                    else if (_groups.Count < MaxGroups) _groups.Add((c, 1));
                }
            if (n == 0) return;
            int top = 0;
            foreach (var g in _groups) top = Math.Max(top, g.N);
            _painted += n;
            _dominant += top;
        }
    }

    /// <summary>오름차순 <paramref name="sorted"/>에 <paramref name="p"/> ±1 안의 값이 있는지.</summary>
    private static bool Near(int[] sorted, int p)
    {
        int i = Array.BinarySearch(sorted, p - 1);
        if (i < 0) i = ~i;
        return i < sorted.Length && sorted[i] <= p + 1;
    }

    /// <summary>cuts[c] ≤ p &lt; cuts[c + 1]인 c(밖이면 끝 칸으로 자름).</summary>
    private static int Find(int[] cuts, int p)
    {
        int c = Array.BinarySearch(cuts, p);
        if (c < 0) c = ~c - 1;
        return Math.Clamp(c, 0, cuts.Length - 2);
    }
}
