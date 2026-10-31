using Chickxel.Core.Model;

namespace Chickxel.Core.Pixelate;

/// <summary>결과 픽셀 하나의 색을 정한 단계(<see cref="Pixelator"/>가 돌리는 순서).</summary>
public enum PixelTraceStep
{
    /// <summary>칸 색 고르기(<see cref="DownsampleSettings.Mode"/>) — 팔레트 전 원본 칸 색.</summary>
    Cell,
    /// <summary>팔레트로 바꾸기(가장 가까운 색, 명암 계열 우선, 디더).</summary>
    Palette,
    /// <summary>거의 검은 색을 외곽선 색 하나로(<see cref="PaletteSettings.SnapOutline"/>).</summary>
    SnapOutline,
    /// <summary>벡터로 다시 그리기의 외곽선(<see cref="OutlineFit.Vector"/>).</summary>
    VectorOutline,
    /// <summary>외톨이 픽셀 지우기(<see cref="PixelCleanup.RemoveOrphans"/>).</summary>
    Orphans,
    /// <summary>1px 구멍 메우기(<see cref="PixelCleanup.FillHoles"/>).</summary>
    Holes,
    /// <summary>바깥 외곽선 1px로(<see cref="PixelCleanup.ThinOutline"/>).</summary>
    ThinOutline,
    /// <summary>외곽선 통일·닫기(<see cref="CleanupSettings.Outline"/>).</summary>
    Outline,
    /// <summary>외곽선 L자 겹 픽셀 줄이기(<see cref="CleanupSettings.ReduceJaggies"/>).</summary>
    Jaggies,
    /// <summary>벡터로 다시 그린 바깥 둘레의 L자 모서리 빼기(<see cref="PixelCleanup.RemoveRingCorners"/>).</summary>
    RingCorners,
    /// <summary>앞뒤 프레임과 같게(<see cref="ConsistencySettings.TemporalMajority"/>).</summary>
    Temporal,
    /// <summary>고정 색(<see cref="PaletteSettings.PinnedColors"/>).</summary>
    Pinned,
    /// <summary>색 빼기(<see cref="PaletteSettings.ExcludedColors"/>).</summary>
    Excluded,
}

/// <summary>단계 하나가 끝난 뒤의 색(투명이면 알파 0).</summary>
public readonly record struct PixelTraceEntry(PixelTraceStep Step, Color32 Color);

/// <summary>
/// 팔레트 단계에서 칸 색과 가까운 팔레트 색과 그 거리(팔레트의 색 거리 — <see cref="ColorMetric.For"/>), 그리고 칸 색에서 이 색까지의 OKLCh 차:
/// 밝기 <see cref="DeltaL"/>·채도 <see cref="DeltaC"/>(+ = 더 밝음·더 진함), 색상 <see cref="DeltaH"/>(채도를 곱한 색상 차 — <see cref="ColorDistance.OkLchHue"/>의 ΔH, 늘 0 이상).
/// 세 차의 제곱합의 제곱근이 OKLab 거리다.
/// </summary>
public readonly record struct PixelTraceCandidate(Color32 Color, double Distance, double DeltaL, double DeltaC, double DeltaH);

/// <summary>
/// 결과 픽셀 하나의 색 판정 과정(<see cref="Pixelator.TracePixel"/>): 칸 색 → 팔레트 색 → 그 뒤 색을 바꾼 단계만 차례로. 마지막 항목이 결과 색이다.
/// </summary>
public sealed class PixelTrace
{
    /// <summary>프레임 번호와 결과(캔버스) 좌표.</summary>
    public required int Frame { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    /// <summary>단계별 색: 칸 색과 팔레트(원래 색 유지가 아니면)는 늘, 그 뒤는 색이 바뀐 단계만.</summary>
    public required IReadOnlyList<PixelTraceEntry> Steps { get; init; }
    /// <summary>팔레트 단계에서 칸 색에 가까운 팔레트 색(가까운 순 <see cref="MaxCandidates"/>개 — 팔레트로 바꾸지 않았으면 빈 목록).</summary>
    public required IReadOnlyList<PixelTraceCandidate> Candidates { get; init; }
    /// <summary>팔레트 단계의 색 거리.</summary>
    public required ColorMetric Metric { get; init; }
    /// <summary>팔레트 단계가 디더링이었는지(가장 가까운 색 대신 두 색 무늬 중 하나).</summary>
    public required bool Dithered { get; init; }

    /// <summary><see cref="Candidates"/> 개수 상한.</summary>
    public const int MaxCandidates = 3;

    /// <summary>결과 색(마지막 단계).</summary>
    public Color32 Final => Steps[^1].Color;

    /// <summary>
    /// 팔레트 단계가 가장 가까운 색이 아닌 색을 골랐는지: 디더가 아닌데 고른 색이 가까운 순 1위가 아니면 명암 계열 우선(<see cref="ColorMetric.Nearest"/>)이 바꾼 것.
    /// </summary>
    public bool ShadeFamilyChose => !Dithered && Candidates.Count > 0 && Steps.FirstOrDefault(e => e.Step == PixelTraceStep.Palette) is { Step: PixelTraceStep.Palette } p
        && !SameRgb(p.Color, Candidates[0].Color);

    internal static bool SameRgb(Color32 a, Color32 b) => a.A == 0 ? b.A == 0 : b.A != 0 && (a.Argb & 0xFFFFFFu) == (b.Argb & 0xFFFFFFu);
}

/// <summary>
/// 판정 과정을 적을 결과 픽셀: 프레임 <see cref="Frame"/>의 칸 (<see cref="Column"/>, <see cref="Row"/>)(<see cref="PixelateFrameMap.Layout"/> 기준).
/// <see cref="Pixelator.RunOnce"/>가 돌 때마다 새 <see cref="PixelTraceRecorder"/>를 만들어 그 결과에 붙인다.
/// </summary>
internal readonly record struct PixelTraceTarget(int Frame, int Column, int Row);

/// <summary>한 번 돌리는 동안 대상 픽셀의 단계별 색을 모은다(색이 바뀐 단계만 — 칸 색·팔레트는 늘).</summary>
internal sealed class PixelTraceRecorder(PixelTraceTarget target)
{
    private readonly List<PixelTraceEntry> _steps = new(4);
    private List<Color32>? _palette;
    private ColorMetric _metric = ColorMetric.OkLab;
    private bool _dithered;

    public PixelTraceTarget Target => target;

    /// <summary>칸 그림(칸 좌표)에서 대상 픽셀 색을 적는다.</summary>
    public void Cells(PixelTraceStep step, PixelImage cells) => Add(step, cells.Get(target.Column, target.Row));

    /// <summary>
    /// 단계 뒤의 색을 적는다: 칸 색·팔레트는 늘, 그 밖은 앞 색과 다를 때만.
    /// </summary>
    public void Add(PixelTraceStep step, Color32 color)
    {
        bool always = step is PixelTraceStep.Cell or PixelTraceStep.Palette;
        if (!always && _steps.Count > 0 && _steps[^1].Color == color) return;
        _steps.Add(new PixelTraceEntry(step, color));
    }

    /// <summary>대상 프레임이 쓴 팔레트와 색 거리, 디더 여부.</summary>
    public void Palette(List<Color32> palette, ColorMetric metric, bool dithered)
    {
        _palette = palette;
        _metric = metric;
        _dithered = dithered;
    }

    public PixelTrace Build(int x, int y)
    {
        var candidates = new List<PixelTraceCandidate>(PixelTrace.MaxCandidates);
        var cell = _steps.Count > 0 ? _steps[0].Color : Color32.Transparent;
        if (_palette is { Count: > 0 } pal && cell.A != 0)
        {
            var p = _metric.Point(cell);
            var from = OkLab.FromColor(cell);
            double c0 = ColorDistance.Chroma(from);
            candidates.AddRange(pal.DistinctBy(c => c.Argb | 0xFF000000u)
                .Select(c =>
                {
                    var to = OkLab.FromColor(c);
                    double da = to.A - from.A, db = to.B - from.B, dc = ColorDistance.Chroma(to) - c0;
                    return new PixelTraceCandidate(c.WithAlpha(255), _metric.Distance(p, _metric.Point(c)), to.L - from.L, dc, Math.Sqrt(Math.Max(0, da * da + db * db - dc * dc)));
                })
                .OrderBy(c => c.Distance)
                .Take(PixelTrace.MaxCandidates));
        }
        return new PixelTrace
        {
            Frame = target.Frame,
            X = x,
            Y = y,
            Steps = _steps.ToArray(),
            Candidates = candidates,
            Metric = _metric,
            Dithered = _dithered,
        };
    }
}
