using System.Drawing;
using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.Core.Pixelate;

/// <summary>
/// 배경 지우기·프레임 나누기까지 마친 원본(후보마다 다시 하지 않음 — 이 두 단계 설정이 같으면 함께 쓴다). 여러 스레드에서 읽어도 된다.
/// </summary>
public sealed class PixelateSource
{
    private readonly Lazy<Lab[]> _lab;

    private PixelateSource(PixelImage original, PixelImage cleaned, BackgroundResult bg, FrameSplit split, BackgroundSettings bgs, FrameSettings fs)
    {
        Original = original;
        Cleaned = cleaned;
        Background = bg;
        Split = split;
        BackgroundSettings = bgs;
        FrameSettings = fs;
        ContentBounds = split.Frames.Count == 0 ? Rectangle.Empty : split.Frames.Select(f => f.Bounds).Aggregate(Rectangle.Union);
        (ProfileX, ProfileY) = GridDetector.Profiles(cleaned, ContentBounds.IsEmpty ? cleaned.Bounds : ContentBounds);
        _lab = new Lazy<Lab[]>(() => GridDetector.LabOf(cleaned), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public PixelImage Original { get; }
    /// <summary>배경을 지운 원본(배경 = 완전 투명, 나머지 불투명).</summary>
    public PixelImage Cleaned { get; }
    public BackgroundResult Background { get; }
    public FrameSplit Split { get; }
    public BackgroundSettings BackgroundSettings { get; }
    public FrameSettings FrameSettings { get; }
    /// <summary>모든 프레임 범위를 합친 사각형.</summary>
    public Rectangle ContentBounds { get; }
    public double[] ProfileX { get; }
    public double[] ProfileY { get; }
    internal Lab[] Lab => _lab.Value;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int Frame, double Jitter, ScaleSource Source, double Darkness), Lazy<ScaleEstimate>> _scales = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Lazy<(double[], double[])>> _profiles = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Lazy<GridDetector.BlockEdgeMap>> _blocks = new();

    /// <summary>프레임 하나의 경계 세기(기억해 둠).</summary>
    public (double[] X, double[] Y) FrameProfiles(int frame)
        => _profiles.GetOrAdd(frame, f => new Lazy<(double[], double[])>(
            () => GridDetector.Profiles(Cleaned, Split.Frames[f].Bounds, Split.Labels, f))).Value;

    /// <summary>뚜렷한 경계(<see cref="GridDetector.BlockEdges"/>, <paramref name="frame"/> = −1이면 그림 전체, 기억해 둠).</summary>
    public GridDetector.BlockEdgeMap BlockEdges(int frame)
        => _blocks.GetOrAdd(frame, f => new Lazy<GridDetector.BlockEdgeMap>(() => f < 0
            ? GridDetector.BlockEdges(Cleaned, Lab, ContentBounds.IsEmpty ? Cleaned.Bounds : ContentBounds)
            : GridDetector.BlockEdges(Cleaned, Lab, Split.Frames[f].Bounds, Split.Labels, f))).Value;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int Frame, double Darkness), Lazy<VectorOutline.Tracing>> _vectors = new();

    /// <summary>프레임 하나의 어두운 덩어리와 뼈대 선(<see cref="VectorOutline.Trace"/>, 외곽선 밝기마다 기억해 둠).</summary>
    public VectorOutline.Tracing VectorTrace(int frame, double darkness)
        => _vectors.GetOrAdd((frame, darkness), k => new Lazy<VectorOutline.Tracing>(
            () => VectorOutline.Trace(Cleaned, Lab, Split.Frames[k.Frame].Bounds, Split.Labels, k.Frame, k.Darkness))).Value;

    /// <summary>
    /// 자동 칸 크기(<paramref name="frame"/> = −1이면 그림 전체, 기억해 둠 — 후보마다 다시 재지 않음). 외곽선 두께는 <see cref="PaletteSettings.MeasureDarkness"/>로 잰다
    /// (밝기를 낮춰도 검은 외곽선은 외곽선 — 표본 마법사: 0.2로 재면 번진 가장자리만 세어 5.7px, 진짜 띠는 14px). 칸 크기 상한은 <see cref="MaxCell"/>.
    /// </summary>
    public ScaleEstimate EstimatedScale(int frame, double jitter, ScaleSource source, double darkness)
    {
        jitter = Math.Clamp(jitter, 0, 0.45);
        darkness = PaletteSettings.MeasureDarkness(darkness);
        return _scales.GetOrAdd((frame, jitter, source, darkness), key => new Lazy<ScaleEstimate>(() =>
        {
            if (key.Frame < 0)
                return ContentBounds.IsEmpty ? default : GridDetector.EstimateScale(Cleaned, null, -1, ContentBounds, key.Jitter, key.Source, key.Darkness, BlockEdges(-1), MaxCell(-1));
            return GridDetector.EstimateScale(Cleaned, Split.Labels, key.Frame, Split.Frames[key.Frame].Bounds, key.Jitter, key.Source, key.Darkness, BlockEdges(key.Frame), MaxCell(key.Frame));
        })).Value;
    }

    /// <summary>
    /// 자동 칸 크기 상한(원본 px, <see cref="GridDetector.MaxCellFor"/>): 프레임 하나면 그 스프라이트 범위의 짧은 변 / <see cref="GridDetector.MinSpriteCells"/>,
    /// −1(그림 전체)이면 프레임마다 잰 상한의 중앙값 — 여러 프레임을 한 범위로 재면 프레임 간격이 칸처럼 보이므로 프레임 범위로 정한다(작은 조각 프레임 하나에 끌려가지 않게 중앙값).
    /// 프레임이 없으면 0.
    /// </summary>
    public double MaxCell(int frame)
    {
        var frames = Split.Frames;
        if (frames.Count == 0) return 0;
        if (frame >= 0) return GridDetector.MaxCellFor(frames[frame].Bounds);
        var caps = frames.Select(f => GridDetector.MaxCellFor(f.Bounds)).Order().ToList();
        return caps[caps.Count / 2];
    }

    private int[]? _framePixels;

    /// <summary>프레임마다 배경을 지운 원본의 그림 픽셀 수(기억해 둠) — 결과가 그림을 얼마나 덮는지(<see cref="Pixelator.CheckFailure"/>).</summary>
    public IReadOnlyList<int> FramePixels
    {
        get
        {
            if (_framePixels is { } done) return done;
            var counts = new int[Split.Frames.Count];
            var labels = Split.Labels;
            var px = Cleaned.Pixels;
            for (int i = 0; i < px.Length; i++)
                if (labels[i] >= 0 && labels[i] < counts.Length && px[i].A != 0) counts[labels[i]]++;
            return _framePixels = counts;
        }
    }

    public static PixelateSource Prepare(PixelImage image, PixelateSettings s)
    {
        var bg = BackgroundRemoval.Run(image, s.Background);
        if (s.Background.KeepBackground && bg.Color is { } color)
        {
            // 배경 남기기: 배경을 배경색 한 색으로 고르게 하고 그림 전체를 한 프레임으로.
            var all = new bool[bg.Foreground.Length];
            Array.Fill(all, true);
            var flat = BackgroundRemoval.Flatten(image, bg.Foreground, color);
            return new PixelateSource(image, flat, bg with { Foreground = all }, FrameSplitter.Run(all, image.Width, image.Height, s.Frames with { Mode = FrameSplitMode.Single }), s.Background, s.Frames);
        }
        var cleaned = BackgroundRemoval.Apply(image, bg.Foreground);
        var split = FrameSplitter.Run(bg.Foreground, image.Width, image.Height, s.Frames);
        return new PixelateSource(image, cleaned, bg, split, s.Background, s.Frames);
    }

    /// <summary>이 준비 결과를 <paramref name="s"/>에도 쓸 수 있는지(배경·나누기 설정이 같음 — 맞추기·캔버스는 상관없음).</summary>
    public bool Matches(PixelateSettings s)
        => BackgroundSettings == s.Background && SplitKey(FrameSettings) == SplitKey(s.Frames);

    private static FrameSettings SplitKey(FrameSettings f) => f with { Align = FrameAlign.BottomCenter, CommonCanvas = true };
}

/// <summary>
/// 결과 프레임 하나가 원본 어디서 왔는지: 칸 경계(원본 px)와 캔버스에서 칸 (0,0)의 위치, 원본 범위. <see cref="Local"/> = 칸 경계 지역 보정(없으면 곧은 격자 —
/// 칸 사각형·원본 자리 → 칸은 <see cref="Layout"/>).
/// </summary>
public sealed record PixelateFrameMap(int[] XCuts, int[] YCuts, int OffsetX, int OffsetY, Rectangle SourceBounds)
{
    public LocalCuts? Local { get; init; }

    /// <summary>칸 배치(지역 보정 포함).</summary>
    public CellLayout Layout() => new(XCuts, YCuts, Local);
}

/// <summary>
/// 점수에 쓰는 측정값. <see cref="ColorShift"/> = 넓게 쓰인 원본 색(불투명 픽셀의 <see cref="Pixelator.MajorColorShare"/> 이상인 OKLab 묶음)마다
/// 원본 평균 색과 그 자리 결과 평균 색의 OKLab 거리 중 가장 큰 값(신발 갈색이 금색 쪽으로 밀리는 것처럼 큰 면의 색이 통째로 바뀐 정도).
/// <see cref="BlockRepetition"/> = 결과가 아직 k배 확대 그림인 정도(칸이 진짜 칸보다 작음 — <see cref="Pixelator.CellRepetition"/>, 0~1).
/// </summary>
public sealed record PixelateMetrics(
    double Fidelity,
    int Orphans,
    int OpaquePixels,
    double OutlineGaps,
    int Colors,
    double Consistency,
    double GridConfidence,
    double OutlineThin = 1,
    double ColorShift = 0,
    double BlockRepetition = 0);

/// <summary>
/// 실패한 결과(칸 크기 감지가 잘못됨 — <see cref="Pixelator.CheckFailure"/>): 점수 0(<see cref="PixelateScoring.Score"/>), 후보·최적화가 스스로 고르지 않고,
/// AI 그리기는 문서에 넣지 않는다.
/// </summary>
public enum PixelateFailure
{
    None,
    /// <summary>결과가 모두 투명.</summary>
    Empty,
    /// <summary>결과가 원본 그림의 일부만 덮음(<see cref="Pixelator.MinCoverage"/> 미만).</summary>
    NearlyEmpty,
    /// <summary>쓴 색이 한 가지(실루엣 덩어리).</summary>
    OneColor,
    /// <summary>모든 프레임이 <see cref="Pixelator.MinResultSide"/>×<see cref="Pixelator.MinResultSide"/>보다 작음.</summary>
    TooSmall,
}

/// <summary>정리 결과: 프레임(캔버스 크기 그림), 쓴 색, 격자, 측정값.</summary>
public sealed class PixelateResult
{
    public required PixelateSettings Settings { get; init; }
    /// <summary>결과 프레임(색 빼기까지 한 것).</summary>
    public required IReadOnlyList<PixelImage> Frames { get; init; }
    /// <summary>색 빼기 전 결과 프레임(빼기가 없으면 <see cref="Frames"/>와 같은 목록) — 빼기를 바꿀 때 다시 돌리지 않고 여기서 다시 뺀다(<see cref="Pixelator.WithExclusions"/>).</summary>
    public required IReadOnlyList<PixelImage> BaseFrames { get; init; }
    /// <summary>
    /// 결과 픽셀 자리의 원본 칸 색(팔레트 전, <see cref="Pixelator.CellColor"/>와 같은 색): <see cref="Frames"/>와 같은 크기·배치, 칸이 없거나 투명한 자리는 투명.
    /// 색 빼기가 뺀 픽셀을 이 색에 가장 가까운 남은 색으로 바꾼다.
    /// </summary>
    public required IReadOnlyList<PixelImage> SourceColors { get; init; }
    public required IReadOnlyList<PixelateFrameMap> Maps { get; init; }
    /// <summary>결과에 쓰인 색(밝기 순).</summary>
    public required IReadOnlyList<Color32> Palette { get; init; }
    /// <summary>쓴 평균 칸 크기(원본 px).</summary>
    public required double Scale { get; init; }
    /// <summary>분석으로 찾은 칸 크기(수동이면 고른 값).</summary>
    public required double DetectedScale { get; init; }
    public required (double X, double Y) Offset { get; init; }
    /// <summary>자동 색 수였으면 고른 수(고정 색 포함), 아니면 설정 값.</summary>
    public required int ResolvedColors { get; init; }
    /// <summary>측정값 — 색 빼기 전 결과(<see cref="BaseFrames"/>)로 잰다: 점수·후보 순위가 색 빼기에 흔들리지 않는다.</summary>
    public required PixelateMetrics Metrics { get; init; }
    /// <summary>
    /// 색 빼기 전 색: <see cref="Frames"/>와 같은 크기·배치로, 색 빼기(<see cref="PaletteSettings.ExcludedColors"/>)가 바꾼 픽셀에 원래(뺀) 색, 나머지는 투명.
    /// 바뀐 픽셀이 없으면 빈 목록.
    /// </summary>
    public IReadOnlyList<PixelImage> ExcludedFrames { get; init; } = [];
    /// <summary>실패한 결과인지와 까닭(색 빼기 전 결과로 정함 — <see cref="Pixelator.CheckFailure"/>).</summary>
    public PixelateFailure Failure { get; init; }
    public bool Failed => Failure != PixelateFailure.None;
    /// <summary>처음 자동 칸 크기의 결과가 실패해 다른 어림으로 다시 돌린 결과면 그 처음 칸 크기(원본 px), 아니면 0.</summary>
    public double RetriedFrom { get; init; }

    /// <summary>가장 큰 프레임 크기(같은 캔버스면 모두 이 크기).</summary>
    public Size Size => Frames.Count == 0 ? Size.Empty : new Size(Frames.Max(f => f.Width), Frames.Max(f => f.Height));

    /// <summary>모두 같은 크기로(작은 프레임은 맞추기 기준대로 놓음). 이미 같으면 그대로.</summary>
    public IReadOnlyList<PixelImage> CommonFrames() => Common(Frames);

    /// <summary><see cref="ExcludedFrames"/>를 <see cref="CommonFrames"/>와 같은 자리로(없으면 빈 목록).</summary>
    public IReadOnlyList<PixelImage> CommonExcludedFrames() => Common(ExcludedFrames);

    private IReadOnlyList<PixelImage> Common(IReadOnlyList<PixelImage> frames)
    {
        var size = Size;
        if (frames.All(f => f.Size == size)) return frames;
        var anchor = Settings.Frames.Align == FrameAlign.Center ? Anchor.Center : Anchor.BottomCenter;
        return frames.Select(f => f.Size == size ? f : f.ResizeCanvas(size.Width, size.Height, anchor)).ToList();
    }
}

/// <summary>
/// AI 그림 픽셀로 정리 — 배경 지우기 → 프레임 나누기 → 격자 찾기 → 칸마다 색 고르기 → 팔레트 → 정리 → 프레임 맞추기·떨림 줄이기.
/// </summary>
public static class Pixelator
{
    public static PixelateResult Run(PixelImage image, PixelateSettings s, CancellationToken ct = default)
        => Run(PixelateSource.Prepare(image, s), s, ct);

    public static PixelateResult Run(PixelateSource src, PixelateSettings s, CancellationToken ct = default) => Run(src, s, null, ct);

    /// <summary>
    /// 결과가 실패하면(<see cref="CheckFailure"/> — 칸 크기 감지가 잘못됨) 자동 격자는 다른 어림(<see cref="RetryEstimates"/>)으로 차례로 다시 돌려 처음 실패하지 않은 결과를
    /// 쓴다(<see cref="PixelateResult.RetriedFrom"/>). 모두 실패하면 처음 결과(실패 표시 그대로).
    /// </summary>
    /// <param name="scoreMetric">'주요 색 변화' 측정에 쓸 색 거리(null = 이 설정의 <see cref="PaletteSettings.ColorDistance"/>) — 자동 최적화는 시작 설정의 거리로
    /// 모든 시도를 재서, 색 거리를 바꿔 본 시도가 자기 자로 잰 점수로 이기지 않게 한다.</param>
    internal static PixelateResult Run(PixelateSource src, PixelateSettings s, ColorMetric? scoreMetric, CancellationToken ct)
    {
        var first = RunOnce(src, s, scoreMetric, null, 0, ct);
        return first.Failed ? Retry(src, s, scoreMetric, first, ct) : first;
    }

    /// <summary>다시 돌려 볼 어림 수 상한(실패한 결과에서만).</summary>
    private const int MaxRetries = 4;

    /// <summary>실패한 결과 <paramref name="failed"/>를 다른 어림으로 다시 돌린다(자동 격자·배율 크기 아님일 때만 — 아니면 그대로).</summary>
    internal static PixelateResult Retry(PixelateSource src, PixelateSettings s, ColorMetric? scoreMetric, PixelateResult failed, CancellationToken ct)
    {
        if (s.Grid.Mode != GridMode.Auto || s.Size.Mode == OutputSizeMode.ScaleFactor || src.Split.Frames.Count == 0) return failed;
        foreach (var est in RetryEstimates(src, s, failed.DetectedScale))
        {
            ct.ThrowIfCancellationRequested();
            var r = RunOnce(src, s, scoreMetric, est, failed.DetectedScale, ct);
            if (!r.Failed) return r;
        }
        return failed;
    }

    /// <summary>
    /// 실패한 칸 크기 <paramref name="from"/> 다음으로 해 볼 어림(최대 <see cref="MaxRetries"/>개): 그림 전체 어림의 세 방식 값(외곽선 두께·덩어리 경계·색 경계 —
    /// 경계 일치가 높은 순, 덩어리 경계 값이면 경계열도 뚜렷한 경계로), 그다음 실패한 크기의 ½·⅓. 칸 크기 상한(<see cref="PixelateSource.MaxCell"/>)을 넘거나
    /// 1px 아래거나 이미 해 본 크기(±5%)는 뺀다.
    /// </summary>
    internal static List<ScaleEstimate> RetryEstimates(PixelateSource src, PixelateSettings s, double from)
    {
        var e = src.EstimatedScale(-1, s.Grid.Jitter, ScaleSourceFor(s), s.Palette.OutlineDarkness);
        double cap = src.MaxCell(-1);
        var list = new List<ScaleEstimate>();
        void Add(double v, ScaleSource used, bool exact)
        {
            if (v < 1 || v > cap || (from > 0 && Math.Abs(v / from - 1) <= 0.05) || list.Any(x => Math.Abs(x.Scale / v - 1) <= 0.05)) return;
            list.Add(e with { Scale = v, Used = used, Exact = exact, ClearBlocks = false, ThinOutline = false });
        }
        (double Value, ScaleSource Used, double Match)[] sources = [(e.Outline, ScaleSource.Outline, e.OutlineMatch), (e.Blocks, ScaleSource.EdgeLines, e.BlocksMatch), (e.Edges, ScaleSource.ColorEdges, e.EdgesMatch)];
        foreach (var o in sources.OrderByDescending(o => o.Match)) Add(o.Value, o.Used, false);
        if (from > 0)
            foreach (int k in new[] { 2, 3 }) Add(from / k, e.Used, e.Exact);
        return list.Count > MaxRetries ? list.GetRange(0, MaxRetries) : list;
    }

    /// <summary>
    /// 결과가 원본 그림을 이 비율보다 덜 덮으면(결과 불투명 픽셀 수 ÷ 프레임마다 원본 그림 픽셀 수 / 칸 넓이의 합) 실패(<see cref="PixelateFailure.NearlyEmpty"/>).
    /// 맞는 칸 크기면 0.8~1.1(가는 머리카락처럼 칸의 일부만 덮는 곳이 빠지는 만큼 작음) — 칸이 프레임만 하면 칸마다 그림이 알파 문턱에 못 미쳐 거의 0.
    /// 캔버스 넓이에 대한 비율이 아닌 까닭: 고정 크기·원래 위치 유지 캔버스는 사용자가 정한 크기라 작은 스프라이트도 정상이다.
    /// </summary>
    public const double MinCoverage = 0.25;

    /// <summary>
    /// 모든 결과 프레임의 불투명 범위가 가로·세로 모두 이보다 작으면 실패(<see cref="PixelateFailure.TooSmall"/>) — 그런 결과는 칸이 프레임 간격·덩어리 크기로 잡힌 것.
    /// 큰 덩어리 시험 그림(8×6칸)은 긴 변이 8이라 통과한다.
    /// </summary>
    public const int MinResultSide = 8;

    /// <summary>
    /// 실패한 결과인지(<see cref="PixelateFailure"/>): 불투명 픽셀이 없음, 원본 그림을 <see cref="MinCoverage"/>보다 덜 덮음(<paramref name="scales"/> = 프레임마다 평균 칸 크기),
    /// 쓴 색이 한 가지 이하(<paramref name="colors"/>), 모든 프레임이 <see cref="MinResultSide"/>×<see cref="MinResultSide"/>보다 작음 — 이 순서로 처음 맞는 까닭.
    /// 점수 항목은 결과에 있는 픽셀로만 재므로(외톨이·외곽선·색 바뀜·블록 반복도는 픽셀이 없으면 나쁠 것도 없음) 빈 결과도 충실도 하나만 나빠 점수가 60점대로 나온다 —
    /// 그래서 점수가 아니라 이 검사로 거른다(<see cref="PixelateScoring.Score"/>는 실패한 결과를 0점으로).
    /// </summary>
    public static PixelateFailure CheckFailure(PixelateSource src, IReadOnlyList<PixelImage> frames, IReadOnlyList<double> scales, int colors)
    {
        long opaque = 0;
        bool big = false;
        foreach (var f in frames)
        {
            foreach (var c in f.Pixels) if (c.A != 0) opaque++;
            var b = f.GetOpaqueBounds();
            if (Math.Max(b.Width, b.Height) >= MinResultSide) big = true;
        }
        if (opaque == 0) return PixelateFailure.Empty;
        double expected = 0;
        var pixels = src.FramePixels;
        for (int f = 0; f < frames.Count && f < pixels.Count && f < scales.Count; f++)
            if (scales[f] > 0) expected += pixels[f] / (scales[f] * scales[f]);
        if (expected > 0 && opaque < expected * MinCoverage) return PixelateFailure.NearlyEmpty;
        if (colors <= 1) return PixelateFailure.OneColor;
        return big ? PixelateFailure.None : PixelateFailure.TooSmall;
    }

    /// <param name="forced">자동 칸 크기 대신 쓸 어림(다시 돌릴 때 — <see cref="Retry"/>), null이면 보통대로.</param>
    /// <param name="retriedFrom">다시 돌린 결과면 처음 칸 크기(<see cref="PixelateResult.RetriedFrom"/>).</param>
    internal static PixelateResult RunOnce(PixelateSource src, PixelateSettings s, ColorMetric? scoreMetric, ScaleEstimate? forced, double retriedFrom, CancellationToken ct)
    {
        var split = src.Split;
        int nf = split.Frames.Count;
        var grids = FitGrids(src, s, forced);
        ct.ThrowIfCancellationRequested();

        // 1) 칸마다 색(지역 보정이면 띠마다 다시 고른 칸 경계 안에서).
        var cells = new PixelImage[nf];
        var layouts = new CellLayout[nf];
        for (int f = 0; f < nf; f++)
        {
            var b = split.Frames[f].Bounds;
            var xc = Margin(Slice(grids[f].XCuts, b.Left, b.Right));
            var yc = Margin(Slice(grids[f].YCuts, b.Top, b.Bottom));
            var local = s.Grid.LocalCorrection ? LocalGrid.Correct(src.BlockEdges(f), xc, yc, s.Grid.LocalShift, s.Grid.LocalBand) : null;
            layouts[f] = new CellLayout(xc, yc, local);
            if (local is not null)
                grids[f] = grids[f] with { Confidence = LocalGrid.Confidence(src.Cleaned, split.Labels, f, b, layouts[f]) };
            cells[f] = Downsample(src, f, layouts[f], s.Downsample);
            ct.ThrowIfCancellationRequested();
        }
        // 팔레트 전 칸 색(고정 색 맞추기, 색 빼기의 원본 칸 색 — PixelateResult.SourceColors).
        var raw = cells.Select(c => c.Clone()).ToArray();

        // 2) 팔레트.
        int resolved = ApplyPalette(cells, raw, s.Palette);
        ct.ThrowIfCancellationRequested();

        // 3) 정리(고정 색은 외곽선 색으로 모으지 않음).
        var pins = PaletteEdits.Colors(s.Palette.PinnedColors).Select(c => c.Argb).ToHashSet();
        var outline = OutlineColor(cells, s.Palette.OutlineDarkness);
        for (int f = 0; f < nf; f++)
        {
            var img = cells[f];
            if (s.Palette.SnapOutline) SnapDark(img, outline, s.Palette.OutlineDarkness, pins);
            if (s.Cleanup.OutlineFit == OutlineFit.Vector)
                VectorOutline.Apply(img, src.VectorTrace(f, s.Palette.OutlineDarkness), split.Frames[f].Bounds, src.Cleaned.Width,
                    layouts[f], outline, s.Palette.OutlineDarkness, s.Cleanup);
            PixelCleanup.Run(img, s.Cleanup, outline, s.Palette.OutlineDarkness);
        }
        ct.ThrowIfCancellationRequested();

        // 4) 캔버스에 놓기. 원본 칸 색도 결과와 같은 자리로(칸 (c, r) → 캔버스 (c + OffsetX, r + OffsetY)).
        var (frames, maps) = Arrange(src, s, cells, layouts, grids);
        var sourceColors = new List<PixelImage>(nf);
        for (int f = 0; f < nf; f++)
        {
            var canvas = new PixelImage(frames[f].Width, frames[f].Height);
            canvas.Blit(raw[f], maps[f].OffsetX, maps[f].OffsetY);
            sourceColors.Add(canvas);
        }
        if (s.Consistency.TemporalMajority) PixelCleanup.TemporalMajority(frames, s.Consistency.MaxContrast);

        var palette = UsedColors(frames);
        double scale = grids.Length == 0 ? 0 : grids.Average(g => g.Scale);
        // 외곽선 1px 점수: 맞춘(캔버스에 놓고 떨림까지 줄인) 마지막 결과 픽셀에서, 외곽선 밝기를 낮춰도 검은 외곽선은 세도록(PaletteSettings.MeasureDarkness).
        int outlineEdge = 0, outlineThick = 0;
        double measureDark = PaletteSettings.MeasureDarkness(s.Palette.OutlineDarkness);
        foreach (var f in frames)
        {
            var (edge, thick) = PixelCleanup.OutlineThickness(f, measureDark);
            outlineEdge += edge;
            outlineThick += thick;
        }
        var metrics = Measure(src, frames, maps, grids, s.Palette.OutlineDarkness, palette.Count, scoreMetric ?? ColorMetric.For(s.Palette))
            with { OutlineThin = outlineEdge == 0 ? 1 : 1 - outlineThick / (double)outlineEdge };
        var result = new PixelateResult
        {
            Settings = s,
            Frames = frames,
            BaseFrames = frames,
            SourceColors = sourceColors,
            Maps = maps,
            Palette = palette,
            Scale = scale,
            DetectedScale = grids.Length == 0 ? 0 : grids.Average(g => g.DetectedScale),
            Offset = grids.Length == 0 ? (0, 0) : grids[0].Offset,
            ResolvedColors = resolved,
            Metrics = metrics,
            Failure = CheckFailure(src, frames, [.. grids.Select(g => g.Scale)], palette.Count),
            RetriedFrom = retriedFrom,
        };
        // 5) 색 빼기 — 다 만든 결과의 뒤처리(다른 픽셀·색은 그대로).
        return s.Palette.ExcludedColors.Count == 0 ? result : WithExclusions(result, s.Palette.ExcludedColors);
    }

    /// <summary>
    /// 색 빼기만 바꾼 결과: 다시 돌리지 않고 색 빼기 전 결과(<see cref="PixelateResult.BaseFrames"/>)에서 <paramref name="excluded"/>를 뺀다
    /// (<see cref="PaletteEdits.Apply"/> — 뺀 색 픽셀만 원본 칸 색에 가장 가까운 남은 색으로, 고정 색은 빼지 않음). 측정값·격자·칸 대응은 그대로,
    /// 설정의 <see cref="PaletteSettings.ExcludedColors"/>만 바뀐다. 빈 목록이면 색 빼기 전 결과 그대로.
    /// </summary>
    public static PixelateResult WithExclusions(PixelateResult r, IReadOnlyList<string> excluded)
    {
        var settings = r.Settings with { Palette = r.Settings.Palette with { ExcludedColors = excluded } };
        IReadOnlyList<PixelImage> frames = r.BaseFrames;
        IReadOnlyList<PixelImage> excludedFrames = [];
        if (excluded.Count > 0)
        {
            var copy = r.BaseFrames.Select(f => f.Clone()).ToList();
            var replaced = new PixelImage?[copy.Count];
            if (PaletteEdits.Apply(copy, r.SourceColors, excluded, settings.Palette.PinnedColors, replaced, ColorMetric.For(settings.Palette)) > 0)
            {
                frames = copy;
                excludedFrames = replaced.Select((x, i) => x ?? new PixelImage(copy[i].Width, copy[i].Height)).ToList();
            }
        }
        return new PixelateResult
        {
            Settings = settings,
            Frames = frames,
            BaseFrames = r.BaseFrames,
            SourceColors = r.SourceColors,
            Maps = r.Maps,
            Palette = ReferenceEquals(frames, r.Frames) ? r.Palette : UsedColors(frames),
            Scale = r.Scale,
            DetectedScale = r.DetectedScale,
            Offset = r.Offset,
            ResolvedColors = r.ResolvedColors,
            Metrics = r.Metrics,
            ExcludedFrames = excludedFrames,
            Failure = r.Failure,
            RetriedFrom = r.RetriedFrom,
        };
    }

    /// <summary>
    /// 자동 격자가 실제로 쓸 칸 크기 기준: 외곽선 맞추기가 "크기를 줄여서"면 외곽선 두께(외곽선이 1px이 되는 크기 — 같은 칸을 잰 다른 값이 더 잘 맞으면 그 값,
    /// <see cref="GridDetector.EstimateScale"/>), 아니면(외곽선만 얇게·벡터로 다시 그리기·맞추지 않음) 격자 설정.
    /// </summary>
    public static ScaleSource ScaleSourceFor(PixelateSettings s)
        => s.Cleanup.OutlineFit == OutlineFit.Shrink ? ScaleSource.Outline : s.Grid.Source;

    /// <summary>그림 전체의 격자(프레임마다 찾기여도 전체 기준) — 대화상자에 찾은 값을 보일 때. <paramref name="darkness"/>는 외곽선으로 볼 밝기.</summary>
    public static GridFit DetectGrid(PixelateSource src, PixelGridSettings g, double darkness)
    {
        var bounds = src.ContentBounds.IsEmpty ? src.Cleaned.Bounds : src.ContentBounds;
        var est = g.Mode == GridMode.Auto ? src.EstimatedScale(-1, g.Jitter, g.Source, darkness) : default;
        return GridDetector.Fit(src.ProfileX, src.ProfileY, bounds, g, est, est.BlockCuts ? src.BlockEdges(-1) : null);
    }

    /// <param name="estimate">자동 격자에서 프레임마다·그림 전체 어림 대신 쓸 어림(<see cref="Retry"/>), null이면 보통대로.</param>
    private static GridFit[] FitGrids(PixelateSource src, PixelateSettings s, ScaleEstimate? estimate = null)
    {
        var split = src.Split;
        int nf = split.Frames.Count;
        var result = new GridFit[nf];
        if (nf == 0) return result;
        var size = s.Size;
        double forced = 0;
        if (size.Mode == OutputSizeMode.ScaleFactor) forced = 1 / PixelateRanges.Factor.Clamp(size.Factor);
        GridFit? shared = null;
        if (!s.Grid.PerFrame || forced > 0)
        {
            var bounds = src.ContentBounds;
            shared = forced > 0
                ? new GridFit(GridDetector.UniformCuts(bounds.Left, bounds.Right, forced, 0), GridDetector.UniformCuts(bounds.Top, bounds.Bottom, forced, 0), forced, forced, 0)
                : estimate is { } e && s.Grid.Mode == GridMode.Auto
                    ? GridDetector.Fit(src.ProfileX, src.ProfileY, bounds, s.Grid, e, e.BlockCuts ? src.BlockEdges(-1) : null)
                    : DetectGrid(src, s.Grid with { Source = ScaleSourceFor(s) }, s.Palette.OutlineDarkness);
            if (forced > 0)
                shared = shared with { Confidence = 0.5 * (GridDetector.Confidence(src.ProfileX, shared.XCuts, bounds.Left, bounds.Right) + GridDetector.Confidence(src.ProfileY, shared.YCuts, bounds.Top, bounds.Bottom)) };
        }
        for (int f = 0; f < nf; f++)
        {
            if (shared is not null) { result[f] = shared; continue; }
            var b = split.Frames[f].Bounds;
            var (ex, ey) = src.FrameProfiles(f);
            var est = s.Grid.Mode == GridMode.Auto ? estimate ?? src.EstimatedScale(f, s.Grid.Jitter, ScaleSourceFor(s), s.Palette.OutlineDarkness) : default;
            result[f] = GridDetector.Fit(ex, ey, b, s.Grid, est, est.BlockCuts ? src.BlockEdges(f) : null);
        }

        if (size.Mode == OutputSizeMode.FixedSize)
        {
            int pad = Math.Max(0, size.Padding);
            int maxW = split.Frames.Max(f => f.Bounds.Width), maxH = split.Frames.Max(f => f.Bounds.Height);
            double sx = maxW / (double)Math.Max(1, size.Width - 2 * pad - 2);
            double sy = maxH / (double)Math.Max(1, size.Height - 2 * pad - 2);
            if (size.KeepAspect) sx = sy = Math.Max(sx, sy);
            double current = result.Max(g => g.Scale);
            if (sx > current * 1.02 || sy > current * 1.02)
            {
                for (int f = 0; f < nf; f++)
                {
                    var b = shared is not null ? src.ContentBounds : split.Frames[f].Bounds;
                    var (ex, ey) = shared is not null ? (src.ProfileX, src.ProfileY) : src.FrameProfiles(f);
                    var xc = GridDetector.FitCuts(ex, b.Left, b.Right, Math.Max(sx, 1), 0);
                    var yc = GridDetector.FitCuts(ey, b.Top, b.Bottom, Math.Max(sy, 1), 0);
                    double conf = 0.5 * (GridDetector.Confidence(ex, xc, b.Left, b.Right) + GridDetector.Confidence(ey, yc, b.Top, b.Bottom));
                    result[f] = new GridFit(xc, yc, 0.5 * (sx + sy), result[f].DetectedScale, conf);
                }
            }
        }
        return result;
    }

    /// <summary>[lo, hi)를 덮는 경계만(첫 경계 ≤ lo, 끝 경계 ≥ hi).</summary>
    private static int[] Slice(int[] cuts, int lo, int hi)
    {
        int i0 = 0, i1 = cuts.Length - 1;
        for (int i = 0; i < cuts.Length; i++) if (cuts[i] <= lo) i0 = i;
        for (int i = cuts.Length - 1; i >= 0; i--) if (cuts[i] >= hi) i1 = i;
        if (i1 <= i0) return [cuts[0], Math.Max(cuts[0] + 1, cuts[^1])];
        return cuts[i0..(i1 + 1)];
    }

    /// <summary>양쪽에 칸 하나씩 더한 경계(정리가 둘레로 1px 자랄 자리).</summary>
    private static int[] Margin(int[] cuts)
    {
        int first = cuts.Length > 1 ? cuts[1] - cuts[0] : 1, last = cuts.Length > 1 ? cuts[^1] - cuts[^2] : 1;
        var r = new int[cuts.Length + 2];
        r[0] = cuts[0] - Math.Max(1, first);
        Array.Copy(cuts, 0, r, 1, cuts.Length);
        r[^1] = cuts[^1] + Math.Max(1, last);
        return r;
    }

    /// <summary>칸마다 색: 칸 사각형(지역 보정이면 그 칸의 띠 경계 — <see cref="CellLayout.Cell"/>)에서 둘레 <see cref="DownsampleSettings.BorderFraction"/>를 뺀 안쪽 픽셀로.</summary>
    private static PixelImage Downsample(PixelateSource src, int frame, CellLayout layout, DownsampleSettings s)
    {
        var img = new PixelImage(layout.Columns, layout.Rows);
        var sampler = new CellSampler(src, frame, s);
        for (int cy = 0; cy < img.Height; cy++)
            for (int cx = 0; cx < img.Width; cx++)
                img[cx, cy] = sampler.Sample(layout, cx, cy);
        return img;
    }

    /// <summary>
    /// 원본 칸 하나의 대표 색(정리가 그 칸에 고르는 팔레트 전 색 — <see cref="DownsampleSettings"/>의 방식·둘레 비율·알파 문턱·묶는 간격 그대로).
    /// 칸 (<paramref name="column"/>, <paramref name="row"/>)은 <paramref name="layout"/>(결과의 <see cref="PixelateFrameMap.Layout"/>) 기준, 칸 밖이거나 비어 있으면 투명.
    /// </summary>
    public static Color32 CellColor(PixelateSource src, int frame, CellLayout layout, int column, int row, DownsampleSettings s)
        => column < 0 || row < 0 || column >= layout.Columns || row >= layout.Rows || frame < 0 || frame >= src.Split.Frames.Count
            ? Color32.Transparent
            : new CellSampler(src, frame, s).Sample(layout, column, row);

    /// <summary>칸 색 고르기(한 프레임, 설정 값을 한 번만 맞추고 칸 픽셀 목록을 다시 씀).</summary>
    private sealed class CellSampler(PixelateSource src, int frame, DownsampleSettings s)
    {
        private readonly double _border = PixelateRanges.BorderFraction.Clamp(s.BorderFraction);
        private readonly double _alphaMin = PixelateRanges.AlphaThreshold.Clamp(s.AlphaThreshold);
        private readonly int _step = (int)PixelateRanges.SnapStep.Clamp(s.SnapStep);
        private readonly List<Color32> _members = new(256);

        /// <summary>칸 사각형(지역 보정이면 그 칸의 띠 경계)에서 둘레 <see cref="DownsampleSettings.BorderFraction"/>를 뺀 안쪽 픽셀로 고른 색(비면 투명).</summary>
        public Color32 Sample(CellLayout layout, int cx, int cy)
        {
            var cleaned = src.Cleaned;
            int w = cleaned.Width, h = cleaned.Height;
            var labels = src.Split.Labels;
            var px = cleaned.Pixels;
            var (x0, y0, x1, y1) = layout.Cell(cx, cy);
            int iy = (int)((y1 - y0) * _border);
            if (y1 - y0 - 2 * iy < 1) iy = 0;
            int ya = Math.Max(0, y0 + iy), yb = Math.Min(h, y1 - iy);
            int ix = (int)((x1 - x0) * _border);
            if (x1 - x0 - 2 * ix < 1) ix = 0;
            int xa = Math.Max(0, x0 + ix), xb = Math.Min(w, x1 - ix);
            if (xa >= xb || ya >= yb) return Color32.Transparent;
            var members = _members;
            members.Clear();
            int total = (xb - xa) * (yb - ya);
            for (int y = ya; y < yb; y++)
                for (int x = xa; x < xb; x++)
                {
                    int i = y * w + x;
                    if (labels[i] == frame && px[i].A != 0) members.Add(px[i]);
                }
            if (members.Count == 0 || members.Count < total * _alphaMin) return Color32.Transparent;
            return s.Mode switch
            {
                DownsampleMode.Mean => Mean(members),
                DownsampleMode.Median => Median(members),
                DownsampleMode.Center => CenterSample(cleaned, labels, frame, (x0 + x1) / 2, (y0 + y1) / 2, members),
                _ => ModeColor(members, _step),
            };
        }
    }

    private static Color32 Mean(List<Color32> m)
    {
        long r = 0, g = 0, b = 0;
        foreach (var c in m) { r += c.R; g += c.G; b += c.B; }
        return new Color32((byte)(r / m.Count), (byte)(g / m.Count), (byte)(b / m.Count));
    }

    private static Color32 Median(List<Color32> m)
    {
        var r = m.Select(c => c.R).Order().ToArray();
        var g = m.Select(c => c.G).Order().ToArray();
        var b = m.Select(c => c.B).Order().ToArray();
        int k = m.Count / 2;
        return new Color32(r[k], g[k], b[k]);
    }

    private static Color32 CenterSample(PixelImage img, int[] labels, int frame, int x, int y, List<Color32> fallback)
    {
        if (img.Contains(x, y) && labels[y * img.Width + x] == frame && img[x, y].A != 0) return img[x, y].WithAlpha(255);
        return Median(fallback);
    }

    /// <summary>채널마다 <paramref name="step"/> 간격으로 묶어 가장 큰 묶음(같으면 먼저 나온 것)의 평균.</summary>
    private static Color32 ModeColor(List<Color32> m, int step)
    {
        var bins = new Dictionary<int, (int N, int R, int G, int B)>();
        int bestKey = 0, bestN = 0;
        foreach (var c in m)
        {
            int key = (c.R / step) << 16 | (c.G / step) << 8 | c.B / step;
            var v = bins.GetValueOrDefault(key);
            v = (v.N + 1, v.R + c.R, v.G + c.G, v.B + c.B);
            bins[key] = v;
            if (v.N > bestN) { bestN = v.N; bestKey = key; }
        }
        var best = bins[bestKey];
        return new Color32((byte)(best.R / best.N), (byte)(best.G / best.N), (byte)(best.B / best.N));
    }

    /// <summary>
    /// 팔레트를 만들고 그 색으로 바꾼다. 고른(자동이면 찾은 — 고정 색 포함) 색 수를 돌려준다. <paramref name="raw"/> = 팔레트 전 칸 색(<paramref name="cells"/>와 같은 크기).
    /// 고정 색(<see cref="PaletteSettings.PinnedColors"/>)은 팔레트 앞에 그대로 넣고, 나머지는 (색 수 − 고정 색 수, 적어도 1)개를 만든다(고정 색과 맞는 만든 색은 버림).
    /// 자동 색 수면 넓게 쓰인 색 지키기에서 고정 색도 그 색을 덮는 색으로 본다. 바꾼 뒤 원본 칸 색이 OKLab로 고정 색에 가장 가까운 칸은 그 고정 색으로(디더 없이).
    /// 원본 색 유지(또는 기존 팔레트가 비어 원본 색 그대로)면 고정 색과 맞는(<see cref="PaletteEdits.MatchDistance"/>) 칸만 가장 가까운 고정 색으로.
    /// </summary>
    private static int ApplyPalette(PixelImage[] cells, PixelImage[] raw, PaletteSettings s)
    {
        var pins = PaletteEdits.Colors(s.PinnedColors).DistinctBy(c => c.Argb).ToList();
        var existing = s.ExistingColors.Select(h => PixelateSettings.ParseColor(h, Color32.Transparent)).Where(c => c.A != 0).ToList();
        if (s.Method == PaletteMethod.KeepOriginal || (s.Method == PaletteMethod.Existing && existing.Count == 0))
        {
            SnapToPins(cells, pins);
            return PaletteRemap.CountColors(Combine(cells));
        }
        var metric = ColorMetric.For(s);
        int[][] groups = s.Shared || s.Method == PaletteMethod.Existing
            ? [Enumerable.Range(0, cells.Length).ToArray()]
            : Enumerable.Range(0, cells.Length).Select(i => new[] { i }).ToArray();
        int resolved = 0;
        foreach (var group in groups)
        {
            var images = group.Select(i => cells[i]).ToArray();
            List<Color32> palette;
            if (s.Method == PaletteMethod.Existing)
            {
                palette = [.. pins, .. existing.Where(c => !pins.Any(p => p.Argb == (c.Argb | 0xFF000000u)))];
                resolved = palette.Count;
            }
            else
            {
                // 원본 색: 가장자리·섞인 칸을 덜 세고, 묶음마다 실제 칸 색. 평균: 쓰인 수 그대로 묶음 평균.
                var (hist, members) = s.ColorSource == PaletteColorSource.Original
                    ? PaletteBuilder.WeightedHistogram(images, s.EdgeWeight, s.OutlineDarkness)
                    : (PaletteBuilder.Histogram(images), null);
                if (hist.Count == 0) continue;
                int k = s.Colors > 0 ? Math.Clamp(s.Colors, 2, PaletteBuilder.MaxColors) : PaletteBuilder.AutoCount(hist);
                int generate = Math.Max(1, k - pins.Count);
                var made = s.Method == PaletteMethod.MedianCut ? PaletteBuilder.MedianCut(hist, generate, members, metric) : PaletteBuilder.KMeans(hist, generate, members, metric).Palette;
                palette = [.. pins, .. made.Where(c => !pins.Any(p => PaletteEdits.Matches(p, c)))];
                // 자동 색 수면 넓게 쓰인 색이 다른 색과 섞여 바뀌지 않게 더한다(고른 색 수는 그대로).
                if (s.Colors <= 0) palette = PaletteBuilder.KeepMajorColors(palette, hist, members, metric: metric);
                // 다른 색상 지키기: 작은 면이라도 색상이 뚜렷이 다른 색(귀 그늘의 살구색 등)이 다른 색상(금색)으로 바뀌지 않게.
                if (s.KeepDistinctHues)
                    palette = PaletteBuilder.KeepDistinctHues(palette, hist, members, pins.Count, s.Colors > 0 && palette.Count >= k, metric,
                        PixelateRanges.DistinctMinChroma.Clamp(s.DistinctMinChroma), PixelateRanges.DistinctMinHue.Clamp(s.DistinctMinHue),
                        PixelateRanges.DistinctMinArea.Clamp(s.DistinctMinArea));
                resolved = Math.Max(resolved, Math.Max(k, palette.Count));
            }
            // 디더 없음: 고른 색 거리로 가장 가까운 색(명암 계열 우선이면 같은 계열 먼저 — ColorMetric.Nearest).
            // 디더(Bayer): RGB 거리(PaletteRemap)의 두 색 섞기 — 명암 계열 우선이면 섞을 바탕 색도 고른 색 거리(같은 계열 먼저)로 고르고, 색상이 다른 유채색과는
            // 섞지 않는다(ColorDistance.CanDither — 살구색 그늘에 금색 점이 찍히지 않게).
            if (s.Dither == PaletteDither.None) MapNearest(images, palette, metric);
            else
            {
                PaletteRemap remap;
                if (s.PreferShadeFamily)
                {
                    var pal = palette.Select(metric.Point).ToArray();
                    remap = new PaletteRemap(palette, c => palette[metric.Nearest(metric.Point(c), pal)],
                        (a, b) => ColorDistance.CanDither(OkLab.FromColor(a), OkLab.FromColor(b)));
                }
                else remap = new PaletteRemap(palette);
                foreach (var img in images) remap.Apply(img, s.Dither);
            }
            if (pins.Count > 0) TakePins(images, group.Select(i => raw[i]).ToArray(), palette, pins.Count, metric);
        }
        return resolved;
    }

    /// <summary>칸마다 <paramref name="metric"/>로 가장 가까운 팔레트 색(알파는 그대로, 같은 색은 한 번만 계산).</summary>
    private static void MapNearest(PixelImage[] images, List<Color32> palette, ColorMetric metric)
    {
        var pal = palette.Select(metric.Point).ToArray();
        var cache = new Dictionary<uint, uint>();
        foreach (var img in images)
        {
            var px = img.Pixels;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.A == 0) continue;
                uint rgb = c.Argb | 0xFF000000u;
                if (!cache.TryGetValue(rgb, out uint to))
                    cache[rgb] = to = palette[metric.Nearest(metric.Point(c), pal)].Argb & 0xFFFFFFu;
                px[i] = new Color32((c.Argb & 0xFF000000u) | to);
            }
        }
    }

    /// <summary>원본 칸 색(<paramref name="raw"/>)이 <paramref name="metric"/>로 팔레트 앞 <paramref name="pinCount"/>개(고정 색) 중 하나에 가장 가까운 칸을 그 고정 색으로.</summary>
    private static void TakePins(PixelImage[] images, PixelImage[] raw, List<Color32> palette, int pinCount, ColorMetric metric)
    {
        var labs = palette.Select(metric.Point).ToArray();
        var pick = new Dictionary<uint, int>();
        for (int n = 0; n < images.Length; n++)
        {
            var px = images[n].Pixels;
            var from = raw[n].Pixels;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].A == 0 || from[i].A == 0) continue;
                uint key = from[i].Argb | 0xFF000000u;
                if (!pick.TryGetValue(key, out int best))
                    pick[key] = best = metric.Nearest(metric.Point(from[i]), labs);
                if (best < pinCount) px[i] = palette[best].WithAlpha(px[i].A);
            }
        }
    }

    /// <summary>고정 색과 맞는(<see cref="PaletteEdits.MatchDistance"/>) 칸을 가장 가까운 고정 색으로(원본 색 유지).</summary>
    private static void SnapToPins(PixelImage[] cells, List<Color32> pins)
    {
        if (pins.Count == 0) return;
        var labs = pins.Select(OkLab.FromColor).ToArray();
        var snap = new Dictionary<uint, int>();
        foreach (var img in cells)
        {
            var px = img.Pixels;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].A == 0) continue;
                uint key = px[i].Argb | 0xFF000000u;
                if (!snap.TryGetValue(key, out int best))
                {
                    var lab = OkLab.FromColor(px[i]);
                    best = -1;
                    float bestD = PaletteEdits.MatchDistance;
                    for (int p = 0; p < labs.Length; p++)
                    {
                        float d = Lab.Distance(lab, labs[p]);
                        if (d <= bestD) { bestD = d; best = p; }
                    }
                    snap[key] = best;
                }
                if (best >= 0) px[i] = pins[best].WithAlpha(px[i].A);
            }
        }
    }

    private static PixelImage Combine(PixelImage[] cells)
    {
        int total = cells.Sum(c => c.Pixels.Length);
        var all = new Color32[Math.Max(1, total)];
        int at = 0;
        foreach (var c in cells) { c.Pixels.CopyTo(all, at); at += c.Pixels.Length; }
        return new PixelImage(all.Length, 1, all);
    }

    /// <summary>
    /// 외곽선 색: 밝기(OKLab L)가 <paramref name="darkness"/> 이하인 색 중 가장 많이 쓰인 색(대개 외곽선 본색), 그런 색이 없으면 가장 어두운 색.
    /// 팔레트로 바꾼 뒤의 칸에서 세므로 늘 팔레트 색 — 원본 색이면 실제 칸 색이다.
    /// </summary>
    private static Color32 OutlineColor(IEnumerable<PixelImage> images, double darkness)
    {
        var counts = new Dictionary<uint, int>();
        foreach (var img in images)
            foreach (var c in img.Pixels)
                if (c.A != 0) counts[c.Argb] = counts.GetValueOrDefault(c.Argb) + 1;
        if (counts.Count == 0) return Color32.Black;
        var light = counts.Keys.ToDictionary(k => k, k => OkLab.FromColor(new Color32(k)).L);
        var dark = counts.Where(kv => light[kv.Key] <= darkness).ToList();
        uint pick = dark.Count > 0
            ? dark.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key
            : light.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        return new Color32(pick);
    }

    private static void SnapDark(PixelImage img, Color32 outline, double darkness, HashSet<uint> pinned)
    {
        var px = img.Pixels;
        var cache = new Dictionary<uint, bool>();
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.A == 0 || c == outline) continue;
            if (!cache.TryGetValue(c.Argb, out bool dark)) cache[c.Argb] = dark = !pinned.Contains(c.Argb | 0xFF000000u) && OkLab.FromColor(c).L <= darkness;
            if (dark) px[i] = outline;
        }
    }

    /// <summary>프레임을 캔버스에 놓는다(맞추기 기준·같은 캔버스·고정 크기).</summary>
    private static (List<PixelImage> Frames, List<PixelateFrameMap> Maps) Arrange(
        PixelateSource src, PixelateSettings s, PixelImage[] cells, CellLayout[] layouts, GridFit[] grids)
    {
        int nf = cells.Length;
        int pad = Math.Max(0, s.Size.Padding);
        var trims = new Rectangle[nf];
        for (int f = 0; f < nf; f++)
        {
            trims[f] = cells[f].GetOpaqueBounds();
            if (trims[f].IsEmpty) trims[f] = new Rectangle(0, 0, 1, 1);
        }
        var align = s.Frames.Align;
        // 원래 위치 유지: 자리(띠·칸)의 왼쪽 위에서 몇 칸 떨어졌는지.
        var slotOffset = new Point[nf];
        var slotSize = new Size[nf];
        for (int f = 0; f < nf; f++)
        {
            var slot = src.Split.Frames[f].Slot;
            double sc = Math.Max(1, grids[f].Scale);
            int left = layouts[f].XCuts[trims[f].Left], top = layouts[f].YCuts[trims[f].Top];
            slotOffset[f] = new Point((int)Math.Round((left - slot.Left) / sc), (int)Math.Round((top - slot.Top) / sc));
            slotSize[f] = new Size((int)Math.Ceiling(slot.Width / sc), (int)Math.Ceiling(slot.Height / sc));
        }
        // 범위가 자리 왼쪽·위로 나간 프레임(격자 칸 선을 넘은 손끝 — FrameRegion): 자리째 그만큼 밀어(공통 캔버스면 모든 프레임을 함께) 잘리지 않게.
        var common = nf == 0 ? Point.Empty : new Point(Math.Max(0, -slotOffset.Min(o => o.X)), Math.Max(0, -slotOffset.Min(o => o.Y)));
        for (int f = 0; f < nf; f++)
        {
            var d = s.Frames.CommonCanvas ? common : new Point(Math.Max(0, -slotOffset[f].X), Math.Max(0, -slotOffset[f].Y));
            slotOffset[f].Offset(d);
            slotSize[f] += new Size(d);
        }

        Size Canvas(int f)
        {
            if (s.Size.Mode == OutputSizeMode.FixedSize) return new Size(Math.Max(1, s.Size.Width), Math.Max(1, s.Size.Height));
            if (align == FrameAlign.KeepOffsets)
            {
                var all = s.Frames.CommonCanvas ? Enumerable.Range(0, nf) : [f];
                return new Size(
                    all.Max(i => Math.Max(slotSize[i].Width, slotOffset[i].X + trims[i].Width)) + 2 * pad,
                    all.Max(i => Math.Max(slotSize[i].Height, slotOffset[i].Y + trims[i].Height)) + 2 * pad);
            }
            if (!s.Frames.CommonCanvas) return new Size(trims[f].Width + 2 * pad, trims[f].Height + 2 * pad);
            return new Size(trims.Max(t => t.Width) + 2 * pad, trims.Max(t => t.Height) + 2 * pad);
        }

        var frames = new List<PixelImage>(nf);
        var maps = new List<PixelateFrameMap>(nf);
        for (int f = 0; f < nf; f++)
        {
            var size = Canvas(f);
            var t = trims[f];
            int x, y;
            switch (align)
            {
                case FrameAlign.KeepOffsets:
                    x = pad + slotOffset[f].X;
                    y = pad + slotOffset[f].Y;
                    break;
                case FrameAlign.Center:
                    x = (size.Width - t.Width) / 2;
                    y = (size.Height - t.Height) / 2;
                    break;
                default:
                    x = (size.Width - t.Width) / 2;
                    y = size.Height - pad - t.Height;
                    break;
            }
            var canvas = new PixelImage(size.Width, size.Height);
            canvas.Blit(cells[f], t, x, y);
            frames.Add(canvas);
            maps.Add(new PixelateFrameMap(layouts[f].XCuts, layouts[f].YCuts, x - t.Left, y - t.Top, src.Split.Frames[f].Bounds) { Local = layouts[f].Local });
        }
        return (frames, maps);
    }

    private static List<Color32> UsedColors(IEnumerable<PixelImage> frames)
    {
        var set = new HashSet<uint>();
        foreach (var f in frames)
            foreach (var c in f.Pixels)
                if (c.A != 0) set.Add(c.Argb | 0xFF000000u);
        return set.Select(v => new Color32(v)).OrderBy(c => OkLab.FromColor(c).L).ThenBy(c => c.Argb).ToList();
    }

    private static PixelateMetrics Measure(PixelateSource src, List<PixelImage> frames, List<PixelateFrameMap> maps, GridFit[] grids, double darkness, int colors,
        ColorMetric metric)
    {
        // 충실도: 결과를 칸 크기로 다시 키워 배경 지운 원본과 OKLab로 견준다(투명/불투명이 어긋나면 1). 칸 안쪽(가장자리 1/8씩 뺌)만 보고,
        // 색은 칸마다 안쪽 평균과 견준다 — 칸 경계의 번진 색·반투명 가장자리와 종이 결 같은 잡음은 어떤 픽셀 그림도 맞출 수 없는 것이고,
        // 칸 안에 두 색이 섞였으면(잃은 무늬) 평균도 결과 색에서 그만큼 멀어진다(픽셀마다 잰 평균과 같은 크기).
        var lab = src.Lab;
        var cleaned = src.Cleaned;
        var labels = src.Split.Labels;
        int w = cleaned.Width;
        double errSum = 0;
        long errN = 0;
        var labCache = new Dictionary<uint, Lab>();
        var buckets = new Dictionary<int, double[]>();
        long matched = 0;
        var cells = new Dictionary<long, double[]>();
        for (int f = 0; f < frames.Count; f++)
        {
            var m = maps[f];
            var frame = frames[f];
            var b = m.SourceBounds;
            int stride = Math.Max(1, (int)Math.Round(grids[f].Scale / 6));
            var layout = m.Layout();
            var colOf = m.Local is null ? GridDetector.Lookup(m.XCuts, b.Left, b.Right) : null;
            var rowOf = m.Local is null ? GridDetector.Lookup(m.YCuts, b.Top, b.Bottom) : null;
            for (int y = b.Top; y < b.Bottom; y += stride)
                for (int x = b.Left; x < b.Right; x += stride)
                {
                    int i = y * w + x;
                    bool srcOn = labels[i] == f && cleaned.Pixels[i].A != 0;
                    int col, row;
                    if (colOf is not null) { col = colOf[x - b.Left]; row = rowOf![y - b.Top]; }
                    else layout.Locate(x, y, out col, out row);
                    var (x0, y0, x1, y1) = layout.Cell(col, row);
                    if (!Inner(x, x0, x1) || !Inner(y, y0, y1)) continue;
                    var outC = frame.Get(col + m.OffsetX, row + m.OffsetY);
                    bool outOn = outC.A != 0;
                    if (!srcOn && !outOn) continue;
                    errN++;
                    if (srcOn != outOn) { errSum += 1; continue; }
                    if (!labCache.TryGetValue(outC.Argb, out var ol)) labCache[outC.Argb] = ol = OkLab.FromColor(outC);
                    var sl = lab[i];
                    long ck = ((long)f << 40) | ((long)row << 20) | (uint)col;
                    if (!cells.TryGetValue(ck, out var cell)) cells[ck] = cell = [0, 0, 0, 0, ol.L, ol.A, ol.B];
                    cell[0] += sl.L; cell[1] += sl.A; cell[2] += sl.B; cell[3]++;
                    int key = ((int)MathF.Floor(sl.L / 0.05f) * 64 + (int)MathF.Floor((sl.A + 0.5f) / 0.03f)) * 64 + (int)MathF.Floor((sl.B + 0.5f) / 0.03f);
                    if (!buckets.TryGetValue(key, out var acc)) buckets[key] = acc = new double[7];
                    acc[0] += sl.L; acc[1] += sl.A; acc[2] += sl.B;
                    acc[3] += ol.L; acc[4] += ol.A; acc[5] += ol.B;
                    acc[6]++;
                    matched++;
                }
        }
        foreach (var c in cells.Values)
            errSum += c[3] * Lab.Distance(new Lab((float)(c[0] / c[3]), (float)(c[1] / c[3]), (float)(c[2] / c[3])), new Lab((float)c[4], (float)c[5], (float)c[6]));

        // 칸 경계 둘레(칸 크기의 1/8, 적어도 1px — 작은 칸은 1/4까지)는 번지거나 흔들린 자리로 보고 뺀다.
        static bool Inner(int p, int lo, int hi)
        {
            int size = hi - lo;
            int q = Math.Min(size / 4, Math.Max(1, (int)Math.Round(size / 8.0)));
            return p >= lo + q && p < hi - q;
        }

        int orphans = 0, opaque = 0, edges = 0, gaps = 0;
        var darkCache = new Dictionary<uint, bool>();
        foreach (var frame in frames)
        {
            orphans += PixelCleanup.CountOrphans(frame);
            for (int y = 0; y < frame.Height; y++)
                for (int x = 0; x < frame.Width; x++)
                {
                    var c = frame[x, y];
                    if (c.A == 0) continue;
                    opaque++;
                    if (frame.Get(x - 1, y).A != 0 && frame.Get(x + 1, y).A != 0 && frame.Get(x, y - 1).A != 0 && frame.Get(x, y + 1).A != 0) continue;
                    edges++;
                    if (!darkCache.TryGetValue(c.Argb, out bool dark)) darkCache[c.Argb] = dark = OkLab.FromColor(c).L <= darkness + 0.1;
                    if (!dark) gaps++;
                }
        }

        // 주요 색 변화: 팔레트 단계의 색 거리(PaletteSettings.ColorDistance — 모두 OKLab와 같은 크기)로 잰다.
        double shift = 0;
        foreach (var acc in buckets.Values)
        {
            if (acc[6] < matched * MajorColorShare) continue;
            double n = acc[6];
            var s = new Lab((float)(acc[0] / n), (float)(acc[1] / n), (float)(acc[2] / n));
            var o = new Lab((float)(acc[3] / n), (float)(acc[4] / n), (float)(acc[5] / n));
            shift = Math.Max(shift, metric.Distance(metric.Point(s), metric.Point(o)));
        }

        return new PixelateMetrics(
            errN == 0 ? 1 : errSum / errN,
            orphans,
            opaque,
            edges == 0 ? 0 : gaps / (double)edges,
            colors,
            FrameConsistency(frames),
            grids.Length == 0 ? 0 : grids.Average(g => g.Confidence),
            ColorShift: shift,
            BlockRepetition: CellRepetition(cells));
    }

    /// <summary>블록 반복도에서 두 칸의 원본 평균 색을 같은 색으로 볼 OKLab 거리(AI 그림 칸 안 결·잡음은 넘지 않고 칠 사이 경계는 넘는 크기).</summary>
    private const double RepeatTolerance = 0.06;

    /// <summary>
    /// 결과 블록 반복도(0~1, <see cref="PixelateMetrics.BlockRepetition"/>): 칸마다 원본 안쪽 평균 색(<paramref name="cells"/> — 충실도에서 잰 것)으로 본
    /// 가장자리 칸(4-이웃 중 평균 색이 <see cref="RepeatTolerance"/> 넘게 다르거나 그림이 아닌 칸이 있음) 중, 네 칸의 평균 색이 서로 그 안인
    /// 2×2 묶음(그러니 k×k, k ≥ 2 묶음 모두)에 든 비율. 제대로 줄이면 경계가 1칸 계단·1칸 외곽선·1칸 잔무늬라 가장자리 칸 대부분이 묶음에 들지 않고,
    /// 칸 크기가 진짜 칸의 1/k이면 진짜 칸 하나가 k×k 칸으로 나뉘어 가장자리 칸도 거의 다 묶음 안에 든다. 결과 색이 아니라 원본 평균 색으로 재므로
    /// 디더링·외톨이 남기기·칸 색 고르기 방식으로는 줄일 수 없다(격자 자체가 너무 잘게 나뉜 정도). 묶음 위치는 가리지 않는다(AI 그림은 칸마다 흔들림).
    /// 안쪽 칸은 넓은 칠이면 크기와 상관없이 늘 묶음 안이므로 세지 않는다.
    /// </summary>
    private static double CellRepetition(Dictionary<long, double[]> cells)
    {
        var mean = new Dictionary<long, Lab>(cells.Count);
        foreach (var (k, c) in cells) mean[k] = new Lab((float)(c[0] / c[3]), (float)(c[1] / c[3]), (float)(c[2] / c[3]));
        bool Same(Lab a, long key) => mean.TryGetValue(key, out var b) && Lab.Distance(a, b) <= RepeatTolerance;
        // 키 = 프레임 << 40 | 행 << 20 | 열 — 열·행 ±1은 그대로 더하고 뺀다(0 아래로 가는 이웃은 없음).
        const long Row = 1L << 20;
        var inBlock = new HashSet<long>();
        foreach (var (k, a) in mean)
            if (Same(a, k + 1) && Same(a, k + Row) && Same(a, k + Row + 1)
                && Lab.Distance(mean[k + 1], mean[k + Row]) <= RepeatTolerance && Lab.Distance(mean[k + 1], mean[k + Row + 1]) <= RepeatTolerance
                && Lab.Distance(mean[k + Row], mean[k + Row + 1]) <= RepeatTolerance)
            {
                inBlock.Add(k);
                inBlock.Add(k + 1);
                inBlock.Add(k + Row);
                inBlock.Add(k + Row + 1);
            }
        long edge = 0, repeated = 0;
        foreach (var (k, a) in mean)
        {
            bool border = !Same(a, k + 1) || !Same(a, k + Row) || (k & (Row - 1)) == 0 || !Same(a, k - 1) || ((k >> 20) & (Row - 1)) == 0 || !Same(a, k - Row);
            if (!border) continue;
            edge++;
            if (inBlock.Contains(k)) repeated++;
        }
        return edge == 0 ? 0 : repeated / (double)edge;
    }

    /// <summary>
    /// 넓게 쓰인 색으로 볼 비율: 원본·결과가 함께 불투명한 픽셀 중 이 비율 이상인 OKLab 묶음(밝기 0.05 × a·b 0.03 칸)만 색 바뀜(<see cref="PixelateMetrics.ColorShift"/>)에 센다.
    /// </summary>
    public const double MajorColorShare = 0.01;

    /// <summary>이웃 프레임끼리 색 쓰임(비율) 겹침의 평균(0~1, 1 = 같은 색을 같은 비율로).</summary>
    private static double FrameConsistency(List<PixelImage> frames)
    {
        if (frames.Count < 2) return 1;
        var hists = frames.Select(f =>
        {
            var h = new Dictionary<uint, double>();
            int n = 0;
            foreach (var c in f.Pixels)
            {
                if (c.A == 0) continue;
                h[c.Argb] = h.GetValueOrDefault(c.Argb) + 1;
                n++;
            }
            foreach (var k in h.Keys.ToList()) h[k] /= Math.Max(1, n);
            return h;
        }).ToList();
        double sum = 0;
        for (int i = 0; i + 1 < hists.Count; i++)
        {
            double inter = 0;
            foreach (var (k, v) in hists[i])
                inter += Math.Min(v, hists[i + 1].GetValueOrDefault(k));
            sum += inter;
        }
        return sum / (hists.Count - 1);
    }
}
