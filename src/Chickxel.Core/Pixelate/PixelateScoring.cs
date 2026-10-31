namespace Chickxel.Core.Pixelate;

/// <summary>점수 항목.</summary>
public enum ScoreTerm
{
    /// <summary>원본과 닮음: 결과와 원본의 색 차이(<see cref="FidelityJudge"/>대로 칸끼리 또는 주변과 섞어 봄)·투명 어긋남·칸 안 구조 손실.</summary>
    Fidelity,
    /// <summary>외톨이 픽셀 비율.</summary>
    Orphans,
    /// <summary>가장자리 중 외곽선(어두운 색)이 아닌 비율.</summary>
    OutlineGaps,
    /// <summary>색 수가 목표에서 벗어난 정도(점수 전체에 곱하는 감점 — <see cref="PixelateScoring.Score"/>).</summary>
    Colors,
    /// <summary>프레임 사이 색 쓰임이 다른 정도.</summary>
    Consistency,
    /// <summary>격자가 색 바뀜과 맞지 않는 정도.</summary>
    Grid,
    /// <summary>결과의 바깥 외곽선이 2px 이상 겹친 비율(칸 크기가 맞고 정리가 잘되면 외곽선이 1px).</summary>
    OutlineThin,
    /// <summary>넓게 쓰인 원본 색이 통째로 바뀐 정도(<see cref="PixelateMetrics.ColorShift"/>).</summary>
    ColorShift,
    /// <summary>결과 블록 반복도: 결과가 아직 k배 확대 그림인 정도(<see cref="PixelateMetrics.BlockRepetition"/> — 칸이 진짜 칸보다 작음).</summary>
    BlockRepetition,
    /// <summary>칸 나누기 점수의 경계: 원본 경계와 칸 경계가 만나는 정도(<see cref="GridQuality.Edges"/> — 칸 맞추기 먼저의 격자 단계만).</summary>
    GridEdges,
    /// <summary>칸 나누기 점수의 색 고름: 칸마다 비슷한 색만 든 정도(<see cref="GridQuality.Purity"/> — 칸 맞추기 먼저의 격자 단계만).</summary>
    GridPurity,
}

/// <summary>점수 가중치(0 = 그 항목은 보지 않음).</summary>
public sealed record ScoreWeights
{
    public double Fidelity { get; init; } = 4;
    public double Orphans { get; init; } = 1;
    public double OutlineGaps { get; init; } = 0.5;
    public double Colors { get; init; } = 0.5;
    public double Consistency { get; init; } = 1;
    public double Grid { get; init; } = 2;
    public double OutlineThin { get; init; } = 4;
    public double ColorShift { get; init; } = 2;
    public double BlockRepetition { get; init; } = 2;

    public double Of(ScoreTerm t) => t switch
    {
        ScoreTerm.Fidelity => Fidelity,
        ScoreTerm.Orphans => Orphans,
        ScoreTerm.OutlineGaps => OutlineGaps,
        ScoreTerm.Colors => Colors,
        ScoreTerm.Consistency => Consistency,
        ScoreTerm.OutlineThin => OutlineThin,
        ScoreTerm.ColorShift => ColorShift,
        ScoreTerm.BlockRepetition => BlockRepetition,
        ScoreTerm.Grid => Grid,
        _ => 0,
    };
}

/// <summary>'원본과 닮음'의 색 차이를 재는 방식.</summary>
public enum FidelityMode
{
    /// <summary>
    /// 칸끼리: 결과 픽셀마다 그 칸 원본의 대표 색(<see cref="FidelityJudge.Reference"/>)과만 점수용 색 거리로 견준다(이웃과 섞지 않음 — 엉뚱한 색 점·디더 무늬가
    /// 칸마다 그대로 감점).
    /// </summary>
    Cell,
    /// <summary>주변과 섞어 보기: 원본 칸 평균과 결과를 이웃 칸과 흐린 뒤 견준다(S-CIELAB — 멀리서 섞여 보이는 디더 무늬는 맞는 것으로 봄).</summary>
    Blended,
}

/// <summary>칸끼리 견줄 때 원본 칸의 대표 색.</summary>
public enum CellReference
{
    /// <summary>칸 안의 비슷한 색(OKLab <see cref="FidelityJudge.GroupRadius"/> 안)끼리 묶어 무게가 가장 큰 묶음의 평균 — 칸에 걸친 다른 재질·번짐은 빼고 본다.</summary>
    Dominant,
    /// <summary>칸 안 전체의 평균.</summary>
    Mean,
}

/// <summary>
/// '원본과 닮음'의 색 차이 판정(점수 기준 — 프리셋에 저장, 최적화가 바꾸지 않음). 기본은 칸끼리·우세 색 묶음·묶음 범위 0.06·가운데 가중:
/// 도트 그림은 멀리서 섞여 보이는 것보다 한 칸 한 칸이 원본 그 칸과 비슷한 것이 중요하다.
/// </summary>
public sealed record FidelityJudge
{
    public FidelityMode Mode { get; init; } = FidelityMode.Cell;
    public CellReference Reference { get; init; } = CellReference.Dominant;
    /// <summary>우세 색 묶음의 묶음 범위(OKLab 거리, <see cref="PixelateRanges.FidelityGroupRadius"/>).</summary>
    public double GroupRadius { get; init; } = 0.06;
    /// <summary>칸 가운데 픽셀에 더 큰 무게(가장자리로 갈수록 줄어드는 피라미드 — 칸 경계에 번진 이웃 색을 덜 셈).</summary>
    public bool CenterWeighted { get; init; } = true;

    public static FidelityJudge Default { get; } = new();
}

/// <summary>항목 하나: 측정값, 나쁨(0~1), 가중치.</summary>
public sealed record ScorePart(ScoreTerm Term, double Value, double Badness, double Weight);

/// <summary>점수(0~100, 높을수록 좋음)와 항목별 내역.</summary>
public sealed record PixelateScore(double Total, IReadOnlyList<ScorePart> Parts);

/// <summary>
/// AI 없이 후보를 매기는 점수: 항목마다 나쁨(0~1)을 가중 평균해 100 × (1 − 평균). 충실도는 OKLab 평균 차 0.12에서,
/// 외톨이는 불투명 픽셀의 5%에서 가장 나쁨. 색 수는 평균에 넣지 않고 목표가 있을 때만 점수 전체에 곱해 깎는다(아래). 색 바뀜은 팔레트 단계의 색 거리(<see cref="PaletteSettings.ColorDistance"/> — OKLab와 같은 크기,
/// CIEDE2000은 ΔE00/200 — <see cref="ColorMetric.CiedeScale"/>)로 0.02(겨우 보이는 차이)까지는 0, 0.10에서 가장 나쁨.
/// 블록 반복도는 0.65(제대로 줄인 표본 결과 0.17~0.34, 두 칸 두께 외곽선·눈인 걷기 표본의 맞는 크기 0.64)까지는 0, 0.95에서 가장 나쁨(칸을 진짜 칸의
/// ½~⅓로 고른 표본 결과 0.52~1.0, 깨끗한 그림을 키운 것은 1.0) — 칸이 작을수록 충실도는 늘 좋아지므로 이것이 없으면 점수가 그림을 덜 줄이는 쪽으로 끌린다.
/// 실패한 결과(<see cref="PixelateResult.Failed"/> — 비었거나 거의 비었음·한 색·너무 작음, <see cref="Pixelator.CheckFailure"/>)는 항목과 상관없이 0점: 항목은 결과에 있는
/// 픽셀로만 재서, 다 투명한 결과는 충실도(가중치 4/17)·프레임 사이 일관성만 가장 나쁘고 나머지는 나쁠 것이 없어 66점쯤 나왔다(표본 엘프 걷기 시트 1086×362).
/// </summary>
public static class PixelateScoring
{
    /// <summary>
    /// 목표 색 수(<paramref name="targetColors"/>)가 있으면 점수 전체에 (1 − 색 수 가중치 × 목표와의 차이 ÷ 목표)를 곱한다(0 아래면 0). 차이는 목표보다 많은 만큼만 —
    /// <paramref name="strictTarget"/>(목표 색 수 '강제')이면 적은 만큼도.
    /// 예: 가중치 0.5·목표 16이면 20색 −12.5%, 24색 −25%, 48색 이상 0점. 강제면 12색 −12.5%, 8색 −25%도.
    /// 다른 항목과 가중 평균하면 색 수 비중이 작아 크게 벗어난 결과도 거의 깎이지 않는다. 항목의 나쁨 = 깎인 비율.
    /// </summary>
    public static PixelateScore Score(PixelateResult r, ScoreWeights w, int? targetColors, bool strictTarget)
    {
        var m = r.Metrics;
        double orphanRate = m.OpaquePixels == 0 ? 0 : m.Orphans / (double)m.OpaquePixels;
        double off = targetColors is > 0 and var t ? (strictTarget ? Math.Abs(m.Colors - t) : Math.Max(0, m.Colors - t)) / (double)t : 0;
        double colorBad = Math.Clamp(Math.Max(0, w.Colors) * off, 0, 1);
        var parts = new List<ScorePart>
        {
            new(ScoreTerm.Fidelity, m.Fidelity, Math.Min(1, m.Fidelity / 0.12), w.Fidelity),
            new(ScoreTerm.Orphans, m.Orphans, Math.Min(1, orphanRate / 0.05), w.Orphans),
            new(ScoreTerm.OutlineGaps, m.OutlineGaps, Math.Clamp(m.OutlineGaps, 0, 1), w.OutlineGaps),
            new(ScoreTerm.Colors, m.Colors, colorBad, w.Colors),
            new(ScoreTerm.Consistency, m.Consistency, Math.Clamp(1 - m.Consistency, 0, 1), w.Consistency),
            new(ScoreTerm.Grid, m.GridConfidence, Math.Clamp(1 - m.GridConfidence, 0, 1), w.Grid),
            new(ScoreTerm.OutlineThin, m.OutlineThin, Math.Clamp((1 - m.OutlineThin) / 0.5, 0, 1), w.OutlineThin),
            new(ScoreTerm.ColorShift, m.ColorShift, Math.Clamp((m.ColorShift - 0.02) / 0.08, 0, 1), w.ColorShift),
            new(ScoreTerm.BlockRepetition, m.BlockRepetition, Math.Clamp((m.BlockRepetition - 0.65) / 0.3, 0, 1), w.BlockRepetition),
        };
        // 색 수는 평균에 넣지 않고 전체에 곱한다(위).
        double tw = parts.Where(p => p.Term != ScoreTerm.Colors).Sum(p => Math.Max(0, p.Weight));
        double bad = tw <= 0 ? 0 : parts.Where(p => p.Term != ScoreTerm.Colors).Sum(p => Math.Max(0, p.Weight) * p.Badness) / tw;
        return new PixelateScore(r.Failed ? 0 : 100 * (1 - bad) * (1 - colorBad), parts);
    }

    /// <summary>
    /// 칸 나누기 점수(칸 맞추기 먼저의 격자 단계): 100 × (경계 + 색 고름) / 2(<see cref="GridQuality.Total"/>). 실패한 결과는 0점(<see cref="Score"/>와 같음).
    /// 점수 비중(<see cref="ScoreWeights"/>)은 쓰지 않는다 — 두 항목 무게는 1.
    /// </summary>
    public static PixelateScore GridScore(GridQuality q, bool failed) => new(failed ? 0 : q.Total,
    [
        new ScorePart(ScoreTerm.GridEdges, q.Edges, Math.Clamp(1 - q.Edges, 0, 1), 1),
        new ScorePart(ScoreTerm.GridPurity, q.Purity, Math.Clamp(1 - q.Purity, 0, 1), 1),
    ]);
}
