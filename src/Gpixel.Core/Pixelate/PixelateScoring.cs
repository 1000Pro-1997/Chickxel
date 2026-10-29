namespace Gpixel.Core.Pixelate;

/// <summary>점수 항목.</summary>
public enum ScoreTerm
{
    /// <summary>다시 키운 결과와 원본의 OKLab 차이.</summary>
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
        _ => Grid,
    };
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
    /// 목표 색 수(<paramref name="targetColors"/>)가 있으면 점수 전체에 (1 − 색 수 가중치 × |결과 색 수 − 목표| ÷ 목표)를 곱한다(0 아래면 0).
    /// 예: 가중치 0.5·목표 16이면 12색과 20색 모두 −12.5%, 8색과 24색 모두 −25%, 48색 이상 0점.
    /// 다른 항목과 가중 평균하면 색 수 비중이 작아 크게 벗어난 결과도 거의 깎이지 않는다. 항목의 나쁨 = 깎인 비율.
    /// </summary>
    public static PixelateScore Score(PixelateResult r, ScoreWeights w, int? targetColors)
    {
        var m = r.Metrics;
        double orphanRate = m.OpaquePixels == 0 ? 0 : m.Orphans / (double)m.OpaquePixels;
        double colorBad = targetColors is > 0 and var t ? Math.Clamp(Math.Max(0, w.Colors) * Math.Abs((double)m.Colors - t) / t, 0, 1) : 0;
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
}
