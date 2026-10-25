using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Gpixel.Core.Edit;

namespace Gpixel.Core.Pixelate;

/// <summary>자동 최적화가 바꿔 볼 설정.</summary>
[Flags]
public enum OptimizeParam
{
    None = 0,
    /// <summary>칸 크기(세 방식 어림·시작 칸 크기와 그 2·3배·½·⅓을 후보로 꼭 돌려 봄 — 아래쪽은 시작·어림의 0.75배까지, 깨끗한 그림의 정확한 배수면 건너뜀). 격자가 자동이면 <see cref="PixelGridSettings.ScaleAdjust"/>, 수동이면 <see cref="PixelGridSettings.Scale"/>, 배율 크기면 배율.</summary>
    Scale = 1,
    /// <summary>격자 가로 시작 위치(수동 격자일 때만 — 자동 격자는 경계를 따라가므로 건너뜀).</summary>
    OffsetX = 2,
    /// <summary>격자 세로 시작 위치(수동 격자일 때만).</summary>
    OffsetY = 4,
    /// <summary>색 수 4~48(k-평균·중앙 분할일 때만).</summary>
    Colors = 8,
    /// <summary>칸 색 고르기 방식(모두).</summary>
    DownsampleMode = 16,
    /// <summary>칸 둘레 무시 비율 0~0.4.</summary>
    BorderFraction = 32,
    /// <summary>칸 투명 기준 0.2~0.8.</summary>
    AlphaThreshold = 64,
    /// <summary>색 묶음 간격 8~40.</summary>
    SnapStep = 128,
    /// <summary>외곽선으로 볼 밝기 0.15~0.35.</summary>
    OutlineDarkness = 256,
    /// <summary>정리 켜기·끄기(외톨이·구멍·계단 각각).</summary>
    Cleanup = 512,
    /// <summary>디더링(없음·Bayer 2×2·Bayer 4×4, 원래 색 유지가 아닐 때).</summary>
    Dither = 1024,
    /// <summary>
    /// 외곽선 맞추기 방식(크기를 줄여서·외곽선만 얇게·맞추지 않음·벡터로 다시 그리기). 기본도 '모두 선택'(<see cref="SelectAll"/>)도 켜지 않는다 — 사용자가 고른 방식을
    /// 그대로 두고, 사용자가 직접 켤 때만 바꿔 본다.
    /// </summary>
    OutlineFit = 2048,
    /// <summary>팔레트 색(원본 색·평균 색 — k-평균·중앙 분할일 때만).</summary>
    PaletteColorSource = 4096,
    /// <summary>외곽선 색 모으기 켜기·끄기.</summary>
    SnapOutline = 8192,
    /// <summary>팔레트 방식(k-평균·중앙 분할 — 기존 팔레트·원래 색 유지면 건너뜀).</summary>
    PaletteMethod = 16384,
    /// <summary>가장자리 칸 가중 0~1(원본 색 팔레트일 때만 뜻이 있음 — k-평균·중앙 분할일 때만).</summary>
    EdgeWeight = 32768,
    /// <summary>스티커 테두리 지우기 켜기·끄기(배경 지우기를 다시 함 — 배경 설정마다 한 번).</summary>
    StickerBorder = 65536,
    /// <summary>벡터로 다시 그리기의 단순화 허용 오차 0.2~2 결과 px(외곽선 맞추기가 벡터로 다시 그리기인 시도에서만).</summary>
    VectorTolerance = 131072,
    /// <summary>벡터로 다시 그리기에서 그대로 둘 작은 안쪽 덩어리 넓이 1~24칸(외곽선 맞추기가 벡터로 다시 그리기인 시도에서만).</summary>
    VectorBlob = 262144,
    /// <summary>칸 경계 지역 보정(<see cref="PixelGridSettings.LocalCorrection"/>) 켜기·끄기와, 켠 시도에서 최대 이동 0.1~0.5칸·띠 너비 1~8칸.</summary>
    LocalGrid = 524288,
    /// <summary>색 거리(OKLab·CIEDE2000·OKLCh 색상 가중)와, OKLCh 색상 가중인 시도에서 색상 무게 1~3. 점수의 '주요 색 변화'는 모든 시도를 시작 설정의 거리로 잰다.</summary>
    ColorDistance = 1048576,
    /// <summary>다른 색상 지키기 켜기·끄기와, 켠 시도에서 최소 색상 차 10~40도(k-평균·중앙 분할일 때만).</summary>
    DistinctHues = 2097152,
    /// <summary>명암 계열 우선(<see cref="PaletteSettings.PreferShadeFamily"/>) 켜기·끄기(원래 색 유지가 아닐 때).</summary>
    ShadeFamily = 4194304,
    Default = Scale | Colors | DownsampleMode | Cleanup | PaletteColorSource,
    All = Scale | OffsetX | OffsetY | Colors | DownsampleMode | BorderFraction | AlphaThreshold | SnapStep | OutlineDarkness | Cleanup | Dither | OutlineFit
        | PaletteColorSource | SnapOutline | PaletteMethod | EdgeWeight | StickerBorder | VectorTolerance | VectorBlob | LocalGrid | ColorDistance | DistinctHues
        | ShadeFamily,
    /// <summary>대화상자의 '모두 선택'이 켜는 항목: 외곽선 맞추기 방식만 빼고 모두(벡터 설정은 외곽선 맞추기가 벡터로 다시 그리기일 때만 쓰임 — <see cref="PixelateOptimizer.Applicable"/>).</summary>
    SelectAll = All & ~OutlineFit,
}

/// <summary>최적화가 멈춘 까닭.</summary>
public enum OptimizeStop
{
    /// <summary>더 나아지는 곳이 없음.</summary>
    Done,
    /// <summary>시도 횟수를 다 씀.</summary>
    Trials,
    /// <summary>시간을 다 씀.</summary>
    Time,
    /// <summary>취소함(그때까지의 결과를 돌려줌).</summary>
    Cancelled,
}

/// <summary>
/// 자동 최적화 설정: 바꿔 볼 항목, 예산(시도 수·시간 중 먼저 닿는 쪽 — 모든 돌림을 합친 예산), 점수 가중치, 돌림 수 상한(1 = 다시 돌리지 않음).
/// </summary>
public sealed record PixelateOptimizeOptions
{
    public OptimizeParam Params { get; init; } = OptimizeParam.Default;
    public int MaxTrials { get; init; } = 200;
    public TimeSpan MaxTime { get; init; } = TimeSpan.FromSeconds(60);
    public ScoreWeights Weights { get; init; } = new();
    public int MaxDegreeOfParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
    /// <summary>찾기 돌림 수 상한(기본 제한 없음 — 돌림이 <see cref="PixelateOptimizer.MinPassGain"/>점 넘게 나아지는 동안 다시 돌림, 1 = 한 번만).</summary>
    public int MaxPasses { get; init; } = int.MaxValue;
}

/// <summary>
/// 시도 하나: 후보(번호 = 실행 순서, 0 = 시작 설정)와 바꾼 값 요약. <see cref="InScale"/> = 실제 칸 크기가 찾는 범위 안인지 — 다른 항목(예: 외곽선 밝기)이
/// 자동 칸 크기 어림을 바꿔 칸 크기가 범위 밖(예: 1px — 줄이지 않은 그림)으로 나간 시도는 기록만 하고 고르지 않는다.
/// </summary>
public sealed record PixelateTrial(PixelateCandidate Candidate, string Summary, bool InScale = true)
{
    public PixelateSettings Settings => Candidate.Settings;
    public PixelateResult Result => Candidate.Result;
    public PixelateScore Score => Candidate.Score;
}

/// <summary>진행: 끝난 시도 수, 예산(시도 수), 지금까지 최고 점수, 지금 돌림 번호(1부터), 방금 끝난 시도.</summary>
public sealed record PixelateOptimizeProgress(int Done, int Budget, double BestScore, int Pass, PixelateTrial Latest);

/// <summary>찾기 돌림 하나: 번호(1부터), 돌림 시작·끝의 최고 점수, 이 돌림에서 새로 돌린 시도 수(첫 돌림은 시작 설정 포함).</summary>
public sealed record PixelateOptimizePass(int Number, double StartScore, double EndScore, int Trials);

/// <summary>최적화 결과.</summary>
public sealed class PixelateOptimizeResult
{
    public required PixelateCandidate Start { get; init; }
    public required PixelateCandidate Best { get; init; }
    /// <summary>실제로 돌린 시도 모두(설정이 같은 시도는 한 번만 — 번호 순: 묶음마다 만든 순서, 스레드 순서와 무관).</summary>
    public required IReadOnlyList<PixelateTrial> Trials { get; init; }
    /// <summary>찾기 돌림마다 시작·끝 점수(첫 돌림은 늘 있음).</summary>
    public required IReadOnlyList<PixelateOptimizePass> Passes { get; init; }
    /// <summary>이미 돌린 설정이라 다시 돌리지 않은 횟수.</summary>
    public required int CacheHits { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required OptimizeStop StoppedReason { get; init; }

    /// <summary>
    /// 점수 순 상위 <paramref name="n"/>개(설정이 다른 것만). <paramref name="distinctOutput"/>면 결과 그림까지 같은 시도(예: 더할 값이 조금 달라도
    /// 같은 칸 경계가 나온 경우)는 점수가 가장 높은 하나만.
    /// </summary>
    public IReadOnlyList<PixelateTrial> Top(int n, bool distinctOutput = false)
    {
        var sorted = Trials.Where(t => t.InScale).DistinctBy(t => t.Settings.Key()).OrderBy(t => t.Result.Failed).ThenByDescending(t => t.Score.Total).ThenBy(t => t.Candidate.Index);
        return [.. (distinctOutput ? sorted.DistinctBy(t => OutputHash(t.Result)) : sorted).Take(n)];
    }

    private static (int Hash, int Pixels) OutputHash(PixelateResult r)
    {
        var h = new HashCode();
        int pixels = 0;
        foreach (var f in r.Frames)
        {
            h.Add(f.Width);
            h.Add(f.Height);
            h.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(f.Pixels.AsSpan()));
            pixels += f.Pixels.Length;
        }
        return (h.ToHashCode(), pixels);
    }
}

/// <summary>
/// 자동 최적화: 지금 설정에서 시작해 고른 항목을 바꿔 가며 점수(<see cref="PixelateScoring"/>, 가중치 = 점수 비중)가 가장 높은 설정을 찾는다.
/// 돌림 하나 = 1단계(거칠게): 기준점에서 항목마다 하나씩, 숫자는 범위를 고르게 7곳, 고르기·켜고 끄기는 모든 값. 2단계(다듬기): 항목별 최고 값을 합친 점과
/// 씨앗 몇 곳에서 좌표 언덕 오르기 — 숫자는 ±걸음, 둘 다 나아지지 않으면 걸음을 반으로(최소 걸음 아래면 멈춤), 고르기는 모든 값.
/// 첫 돌림은 시작 설정이 기준점이고 씨앗은 점수 상위 3곳. 돌림이 '더 나아지지 않음'으로 끝나고 그 돌림이 최고 점수를 <see cref="MinPassGain"/>점 이상 올렸으면
/// 그때의 1위에서 다시 돌린다 — 씨앗은 1위, 상위 <see cref="DiverseTop"/>개 중 설정이 1위와 가장 다른 시도(지역 최적 벗어나기), 1위를 정해진 씨앗으로
/// 조금 흔든 점(<see cref="PerturbFraction"/>). 같은 설정(<see cref="PixelateSettings.Key"/>)은 모든 돌림에 걸쳐 다시 돌리지 않고, 준비 결과(<see cref="PixelateSource"/>)는 모든 시도가 같이 쓴다.
/// 예산(시도 수·시간)은 모든 돌림을 합친 것이며 먼저 닿는 쪽에서 멈추고, 취소하면 그때까지의 결과를 돌려준다(시작 설정을 돌리기 전에 취소하면 <see cref="OperationCanceledException"/>).
/// 결정성: 묶음마다 만든 순서로 번호를 미리 매기고(시도 수 예산은 묶음을 그 순서로 자름), 결과는 묶음이 끝난 뒤 번호 순으로 반영하며, 같은 점수는 번호가 작은 쪽 —
/// 같은 입력·설정이면 스레드 순서와 상관없이 같은 결과(진행 알림 순서는 고르기에 영향 없음). 시간 예산·취소로 끝난 실행은 어디서 끊길지가 정해지지 않으므로
/// 결정성은 시도 수 예산이나 '더 나아지지 않음'으로 끝날 때만 보장된다.
/// 점수의 목표 색 수는 시작 설정 결과의 색 수(원래 색 유지면 없음) — 후보 만들기와 같다.
/// </summary>
public static class PixelateOptimizer
{
    private const int CoarseSamples = 7;
    private const int Seeds = 3;
    private const double Epsilon = 1e-9;
    /// <summary>다시 돌릴 최소 나아짐(점): 한 돌림이 최고 점수를 이만큼도 올리지 못하면 더 돌리지 않는다(점수 표시 단위 0.1점 — 그보다 작은 나아짐은 보이지 않음).</summary>
    public const double MinPassGain = 0.1;
    /// <summary>다시 돌릴 때 '1위와 가장 다른 시도'를 고르는 점수 상위 범위.</summary>
    public const int DiverseTop = 10;
    /// <summary>다시 돌릴 때 1위를 흔드는 폭: 숫자 항목마다 찾기 범위의 ±이 비율 안에서(씨앗 = 1위 설정 키 해시와 돌림 번호 — 정해짐).</summary>
    public const double PerturbFraction = 0.1;
    /// <summary>'크기를 줄여서'에서 칸 크기로 받아들일 외곽선 두께와의 비율 차(±15%, <c>ShrinkBand</c>).</summary>
    public const double ShrinkTolerance = 0.15;

    public static PixelateOptimizeResult Run(PixelateSource src, PixelateSettings start, PixelateOptimizeOptions options,
        Action<PixelateOptimizeProgress>? onProgress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(options);
        return new Session(src, start, options, onProgress, ct).Run();
    }

    /// <summary>
    /// 시작 설정 <paramref name="s"/>에서 바꿔 볼 수 있는 항목: 고정 크기면 칸 크기 빼고, 격자가 수동(배율 크기 아님)일 때만 시작 위치,
    /// 팔레트가 k-평균·중앙 분할일 때만 색 수·팔레트 색·만드는 방법·가장자리 칸 가중(원본 색이거나 팔레트 색도 바꿔 볼 때 — <paramref name="chosen"/>),
    /// 원본 색 유지가 아닐 때만 디더링, 외곽선 맞추기가 벡터로 다시 그리기이거나 외곽선 맞추기도 바꿔 볼 때만 벡터 설정(허용 오차·작은 덩어리).
    /// </summary>
    public static OptimizeParam Applicable(PixelateSettings s, OptimizeParam chosen = OptimizeParam.None)
    {
        var p = OptimizeParam.All;
        if (s.Size.Mode == OutputSizeMode.FixedSize) p &= ~OptimizeParam.Scale;
        if (s.Size.Mode == OutputSizeMode.ScaleFactor || s.Grid.Mode != GridMode.Manual) p &= ~(OptimizeParam.OffsetX | OptimizeParam.OffsetY);
        if (s.Palette.Method is not (PaletteMethod.KMeans or PaletteMethod.MedianCut))
            p &= ~(OptimizeParam.Colors | OptimizeParam.PaletteColorSource | OptimizeParam.PaletteMethod | OptimizeParam.EdgeWeight | OptimizeParam.DistinctHues);
        else if (s.Palette.ColorSource != PaletteColorSource.Original && !chosen.HasFlag(OptimizeParam.PaletteColorSource))
            p &= ~OptimizeParam.EdgeWeight;
        if (s.Palette.Method == PaletteMethod.KeepOriginal) p &= ~(OptimizeParam.Dither | OptimizeParam.ColorDistance | OptimizeParam.ShadeFamily);
        if (s.Background.Mode == BackgroundMode.None) p &= ~OptimizeParam.StickerBorder;
        if (s.Cleanup.OutlineFit != OutlineFit.Vector && !chosen.HasFlag(OptimizeParam.OutlineFit)) p &= ~(OptimizeParam.VectorTolerance | OptimizeParam.VectorBlob);
        return p;
    }

    /// <summary>
    /// 바꿀 값 하나. <see cref="Choices"/> &gt; 0이면 고르기(값 = 0..Choices−1 번호), 아니면 [<see cref="Min"/>, <see cref="Max"/>] 숫자.
    /// <see cref="Active"/>가 있으면 그 설정에서만 바꿔 본다(예: 벡터 설정은 외곽선 맞추기가 벡터로 다시 그리기인 점에서만 — 다른 점에서는 결과가 같으므로 시도를 아낀다).
    /// <see cref="Seeds"/>는 거칠게 볼 때 고른 간격 값에 더해 꼭 돌려 볼 값(예: 칸 크기 어림과 그 2·3배·½·⅓).
    /// </summary>
    private sealed record Axis(OptimizeParam Param, double Min, double Max, double MinStep, int Decimals, int Choices,
        Func<PixelateSettings, double> Get, Func<PixelateSettings, double, PixelateSettings> Set)
    {
        public Func<PixelateSettings, bool>? Active { get; init; }

        public IReadOnlyList<double> Seeds { get; init; } = [];

        public bool IsActive(PixelateSettings s) => Active is null || Active(s);

        public bool IsChoice => Choices > 0;

        public double Normalize(double v)
        {
            v = Math.Clamp(v, Min, Max);
            return Math.Round(v, Decimals);
        }

        public IEnumerable<double> Coarse()
        {
            if (IsChoice) return Enumerable.Range(0, Choices).Select(i => (double)i);
            return Enumerable.Range(0, CoarseSamples).Select(i => Min + (Max - Min) * i / (CoarseSamples - 1)).Concat(Seeds).Select(Normalize).Distinct();
        }
    }

    private sealed class Session
    {
        private readonly PixelateSource _src;
        private readonly PixelateSettings _start;
        private readonly PixelateOptimizeOptions _opt;
        private readonly Action<PixelateOptimizeProgress>? _onProgress;
        private readonly CancellationToken _ct;
        // 캐시·시도 목록·1위는 찾기를 이끄는 스레드에서만 바꾼다(묶음이 끝난 뒤 번호 순으로) — 작업 스레드는 결과 칸과 진행 알림만.
        private readonly Dictionary<string, PixelateTrial> _cache = new();
        private readonly List<PixelateTrial> _trials = new();
        private readonly object _gate = new();
        private readonly Stopwatch _clock = new();
        private CancellationTokenSource _deadline = null!;
        private List<Axis> _axes = [];
        private int? _target;
        private int _reserved, _index, _hits, _completed, _stop = -1;
        private volatile int _pass = 1;
        private PixelateTrial? _best, _shown;
        private (double Lo, double Hi) _band = (0, double.PositiveInfinity);
        private (double Lo, double Hi)? _shrink;

        public Session(PixelateSource src, PixelateSettings start, PixelateOptimizeOptions opt, Action<PixelateOptimizeProgress>? onProgress, CancellationToken ct)
        {
            _src = src;
            _start = start;
            _opt = opt;
            _onProgress = onProgress;
            _ct = ct;
        }

        private bool Stopped => Volatile.Read(ref _stop) >= 0;

        private void StopWith(OptimizeStop reason) => Interlocked.CompareExchange(ref _stop, (int)reason, -1);

        public PixelateOptimizeResult Run()
        {
            _clock.Start();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_ct);
            _deadline = deadline;
            if (_opt.MaxTime > TimeSpan.Zero && _opt.MaxTime < TimeSpan.FromDays(1)) deadline.CancelAfter(_opt.MaxTime);

            // 시작 설정은 꼭 돌린다(취소만 멈춤 — 시간이 지나도 시작점은 있어야 함).
            var first = Pixelator.Run(_src, _start, _ct);
            _target = _start.Palette.Method == PaletteMethod.KeepOriginal ? null : first.ResolvedColors;
            _shrink = ShrinkBand();
            _band = ScaleBand(first);
            _reserved = 1;
            _index = 1;
            var startTrial = Make(0, _start, first);
            Report(startTrial);
            Commit(_start.Key(), startTrial);

            _axes = BuildAxes(first);
            if (_opt.MaxTrials <= 1) StopWith(OptimizeStop.Trials);
            var passes = new List<PixelateOptimizePass>();
            for (int pass = 1; ; pass++)
            {
                var from = _best!;
                int before = pass == 1 ? 0 : _trials.Count;
                _pass = pass;
                if (_axes.Count > 0 && !Stopped) Search(pass);
                passes.Add(new PixelateOptimizePass(pass, from.Score.Total, _best!.Score.Total, _trials.Count - before));
                // 다시 돌림: 이 돌림이 '더 나아지지 않음'으로 끝났고(예산 남음) 최고 점수를 MinPassGain 이상 올렸을 때만.
                double gain = Rank(_best) - Rank(from);
                if (Stopped || pass >= _opt.MaxPasses || !(Rank(_best) > Rank(from) && gain >= MinPassGain)) break;
            }

            _clock.Stop();
            int stop = Volatile.Read(ref _stop);
            return new PixelateOptimizeResult
            {
                Start = startTrial.Candidate,
                Best = _best!.Candidate,
                Trials = [.. _trials],
                Passes = passes,
                CacheHits = _hits,
                Elapsed = _clock.Elapsed,
                StoppedReason = stop < 0 ? OptimizeStop.Done : (OptimizeStop)stop,
            };
        }

        /// <summary>
        /// 돌림 하나: 지금 1위를 기준점으로 항목마다 거칠게 훑고 항목별 최고 값을 합친 점을 돌린 뒤, 씨앗마다 언덕 오르기.
        /// 첫 돌림의 씨앗은 점수 상위 <see cref="Seeds"/>곳, 다시 돌릴 때는 <see cref="EscapeSeeds"/>.
        /// </summary>
        private void Search(int pass)
        {
            var origin = _best!;
            var o = origin.Settings;
            // 1단계: 항목 하나씩 거칠게.
            var coarse = _axes.Where(a => a.IsActive(o)).SelectMany(a => a.Coarse().Select(v => (Axis: a, Value: v, Settings: a.Set(o, v)))).ToList();
            Evaluate(coarse.Select(c => c.Settings));

            // 항목마다 가장 좋은 값을 합친 점.
            var combined = o;
            foreach (var a in _axes)
            {
                double bestValue = 0, bestScore = Rank(origin) + Epsilon;
                bool better = false;
                foreach (var c in coarse.Where(c => c.Axis == a))
                    if (Lookup(c.Settings) is { } t && Rank(t) > bestScore) (bestValue, bestScore, better) = (c.Value, Rank(t), true);
                if (better) combined = a.Set(combined, bestValue);
            }
            Evaluate([combined]);

            // 2단계: 씨앗마다 언덕 오르기.
            List<PixelateTrial> seeds = pass == 1 ? [.. Ranked().Take(Seeds)] : EscapeSeeds(pass);
            foreach (var seed in seeds)
            {
                if (Stopped) break;
                Climb(seed);
            }
        }

        /// <summary>고를 수 있는 시도(칸 크기 범위 안, 실패 아님)를 점수 순으로(같은 점수는 번호 순).</summary>
        private IEnumerable<PixelateTrial> Ranked()
            => _trials.Where(t => !double.IsNegativeInfinity(Rank(t))).OrderByDescending(Rank).ThenBy(t => t.Candidate.Index);

        /// <summary>
        /// 다시 돌릴 때의 씨앗: 1위, 상위 <see cref="DiverseTop"/>개 중 설정이 1위와 가장 다른 시도(<see cref="Distance"/> — 같으면 점수가 높은 쪽),
        /// 1위를 <see cref="Perturb"/>로 조금 흔든 점(고를 수 있는 결과일 때만).
        /// </summary>
        private List<PixelateTrial> EscapeSeeds(int pass)
        {
            var ranked = Ranked().Take(DiverseTop).ToList();
            if (ranked.Count == 0) return [];
            var top = ranked[0];
            var seeds = new List<PixelateTrial> { top };
            PixelateTrial? far = null;
            double farDistance = Epsilon;
            foreach (var t in ranked.Skip(1))
            {
                double d = Distance(top.Settings, t.Settings);
                if (d > farDistance + Epsilon) (far, farDistance) = (t, d);
            }
            if (far is not null) seeds.Add(far);
            var nudged = Perturb(top.Settings, pass);
            Evaluate([nudged]);
            if (Lookup(nudged) is { } n && !double.IsNegativeInfinity(Rank(n)) && seeds.All(s => s.Settings.Key() != n.Settings.Key())) seeds.Add(n);
            return seeds;
        }

        /// <summary>두 설정의 거리: 항목마다 고르기는 다르면 1, 숫자는 찾기 범위로 나눈 차(최대 1)를 더한다(두 설정 모두에서 쉬는 항목은 뺌).</summary>
        private double Distance(PixelateSettings a, PixelateSettings b)
        {
            double d = 0;
            foreach (var axis in _axes)
            {
                if (!axis.IsActive(a) && !axis.IsActive(b)) continue;
                double va = axis.Get(a), vb = axis.Get(b);
                if (axis.IsChoice) d += Math.Round(va) != Math.Round(vb) ? 1 : 0;
                else if (axis.Max > axis.Min) d += Math.Min(1, Math.Abs(va - vb) / (axis.Max - axis.Min));
            }
            return d;
        }

        /// <summary>
        /// <paramref name="s"/>의 숫자 항목(그 점에서 쓰는 것)마다 찾기 범위의 ±<see cref="PerturbFraction"/> 안에서 흔든 설정. 난수 씨앗은 설정 키의 FNV-1a 해시와
        /// 돌림 번호 — 같은 입력이면 늘 같은 점(<see cref="string.GetHashCode()"/>는 실행마다 달라서 쓰지 않음).
        /// </summary>
        private PixelateSettings Perturb(PixelateSettings s, int pass)
        {
            uint h = 2166136261;
            foreach (char c in s.Key()) h = (h ^ c) * 16777619;
            var rng = new Random((int)(h ^ (uint)pass * 0x9E3779B9u));
            var p = s;
            foreach (var a in _axes)
            {
                if (a.IsChoice || !a.IsActive(p) || a.Max <= a.Min) continue;
                double d = (rng.NextDouble() * 2 - 1) * PerturbFraction * (a.Max - a.Min);
                p = a.Set(p, a.Normalize(a.Get(p) + d));
            }
            return p;
        }

        private PixelateTrial? Lookup(PixelateSettings s) => _cache.TryGetValue(s.Key(), out var t) ? t : null;

        private void Climb(PixelateTrial seed)
        {
            var p = seed;
            var steps = _axes.ToDictionary(a => a, a => a.IsChoice ? 0 : Math.Max(a.MinStep, (a.Max - a.Min) / (2 * (CoarseSamples - 1))));
            while (!Stopped)
            {
                var neighbors = new List<(Axis Axis, PixelateSettings Settings)>();
                foreach (var a in _axes.Where(x => x.IsActive(p.Settings)))
                {
                    double v = a.Get(p.Settings);
                    if (a.IsChoice)
                    {
                        for (int i = 0; i < a.Choices; i++)
                            if (i != (int)Math.Round(v)) neighbors.Add((a, a.Set(p.Settings, i)));
                        continue;
                    }
                    if (steps[a] <= 0) continue;
                    foreach (int d in new[] { -1, 1 })
                    {
                        double nv = a.Normalize(v + d * steps[a]);
                        if (Math.Abs(nv - v) > Epsilon) neighbors.Add((a, a.Set(p.Settings, nv)));
                    }
                }
                if (neighbors.Count == 0) return;
                Evaluate(neighbors.Select(n => n.Settings));
                PixelateTrial? next = null;
                var improved = new HashSet<Axis>();
                foreach (var (a, s) in neighbors)
                {
                    if (Lookup(s) is not { } t || Rank(t) <= Rank(p) + Epsilon) continue;
                    improved.Add(a);
                    if (next is null || Rank(t) > Rank(next)) next = t;
                }
                // 숫자 항목: ±걸음 모두 나아지지 않으면 걸음을 반으로(최소 걸음 아래로는 한 번 최소 걸음, 그다음 멈춤). 이 점에서 쉬는 항목은 그대로.
                bool anyOpen = false;
                foreach (var a in _axes.Where(x => !x.IsChoice && x.IsActive(p.Settings)))
                {
                    if (!improved.Contains(a) && steps[a] > 0)
                    {
                        double half = steps[a] / 2;
                        steps[a] = steps[a] <= a.MinStep + Epsilon ? 0 : Math.Max(half, a.MinStep);
                    }
                    anyOpen |= steps[a] > 0;
                }
                if (next is not null) p = next;
                else if (!anyOpen) return;
            }
        }

        /// <summary>
        /// 설정들을 여러 스레드로 돌린다(이미 돌린 설정·같은 설정은 한 번). 번호는 묶음 순서대로 미리 매기고, 시도 수 예산이 모자라면 묶음을 그 순서로 자른다.
        /// 끝난 결과는 묶음이 다 끝난 뒤 번호 순으로 캐시·목록·1위에 반영한다(스레드 순서와 무관). 시간이 다하거나 취소하면 남은 것은 건너뛴다.
        /// </summary>
        private void Evaluate(IEnumerable<PixelateSettings> batch)
        {
            var todo = new List<(string Key, PixelateSettings Settings)>();
            var seen = new HashSet<string>();
            foreach (var s in batch)
            {
                var key = s.Key();
                if (_cache.ContainsKey(key) || !seen.Add(key)) { _hits++; continue; }
                todo.Add((key, s));
            }
            if (todo.Count == 0 || Stopped) return;
            int room = _opt.MaxTrials - _reserved;
            bool cut = todo.Count > room;
            if (cut)
            {
                if (room <= 0)
                {
                    StopWith(OptimizeStop.Trials);
                    return;
                }
                todo.RemoveRange(room, todo.Count - room);
            }
            _reserved += todo.Count;
            int first = _index;
            _index += todo.Count;
            var done = new PixelateTrial?[todo.Count];
            var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _opt.MaxDegreeOfParallelism) };
            Parallel.For(0, todo.Count, po, (i, state) =>
            {
                if (Stopped) { state.Stop(); return; }
                if (_deadline.IsCancellationRequested)
                {
                    StopWith(_ct.IsCancellationRequested ? OptimizeStop.Cancelled : OptimizeStop.Time);
                    state.Stop();
                    return;
                }
                var s = todo[i].Settings;
                PixelateResult r;
                try
                {
                    r = Pixelator.Run(SourceFor(s), s, ColorMetric.For(_start.Palette), _deadline.Token);
                }
                catch (OperationCanceledException) when (_deadline.IsCancellationRequested)
                {
                    StopWith(_ct.IsCancellationRequested ? OptimizeStop.Cancelled : OptimizeStop.Time);
                    state.Stop();
                    return;
                }
                var trial = Make(first + i, s, r);
                done[i] = trial;
                Report(trial);
            });
            for (int i = 0; i < todo.Count; i++)
                if (done[i] is { } t) Commit(todo[i].Key, t);
            if (cut) StopWith(OptimizeStop.Trials);
        }

        /// <summary>결과에 점수를 매겨 시도로 만든다(번호 <paramref name="index"/>, 0 = 시작 설정 — 시작은 늘 칸 크기 범위 안으로 침).</summary>
        private PixelateTrial Make(int index, PixelateSettings s, PixelateResult r)
        {
            var score = PixelateScoring.Score(r, _opt.Weights, _target);
            bool inScale = index == 0 || (r.Scale >= _band.Lo && r.Scale <= _band.Hi
                && !(s.Cleanup.OutlineFit == OutlineFit.Shrink && _shrink is { } sb && (r.Scale < sb.Lo * 0.98 || r.Scale > sb.Hi * 1.02)));
            return new PixelateTrial(new PixelateCandidate(index, s, r, score), Summary(s, r, _opt.Params), inScale);
        }

        /// <summary>진행 알림(끝나는 대로 — 보여 주는 최고 점수만 여기서 셈, 고르기는 <see cref="Commit"/>).</summary>
        private void Report(PixelateTrial trial)
        {
            int done;
            double best;
            lock (_gate)
            {
                done = ++_completed;
                if (_shown is null || Rank(trial) > Rank(_shown)) _shown = trial;
                best = _shown.Score.Total;
            }
            _onProgress?.Invoke(new PixelateOptimizeProgress(done, _opt.MaxTrials, best, _pass, trial));
        }

        /// <summary>시도를 캐시·목록에 넣고 1위를 고친다(번호 순으로 불림 — 같은 점수면 먼저 넣은 쪽, 곧 번호가 작은 쪽이 남음).</summary>
        private void Commit(string key, PixelateTrial trial)
        {
            _cache[key] = trial;
            _trials.Add(trial);
            if (_best is null || Rank(trial) > Rank(_best)) _best = trial;
        }

        /// <summary>고를 때 쓰는 점수: 칸 크기가 범위 밖이거나 실패한 결과(<see cref="PixelateResult.Failed"/>)인 시도는 −∞.</summary>
        private static double Rank(PixelateTrial t) => t.InScale && !t.Result.Failed ? t.Score.Total : double.NegativeInfinity;

        /// <summary>
        /// 깨끗한 그림의 정확한 정수 배(<see cref="ScaleEstimate.Exact"/> — 자동 격자·배율 크기 아님)면 그 칸 크기, 아니면 null.
        /// 그런 그림은 그 크기가 답이므로 칸 크기를 바꿔 보지 않는다(조금만 바꿔도 칸 경계가 픽셀 사이로 밀려 한 줄이 빠지거나 겹친다).
        /// </summary>
        private double? ExactScale()
        {
            var s0 = _start;
            if (s0.Grid.Mode != GridMode.Auto || s0.Size.Mode != OutputSizeMode.FromScale || _src.Split.Frames.Count == 0) return null;
            var est = _src.EstimatedScale(-1, s0.Grid.Jitter, Pixelator.ScaleSourceFor(s0), s0.Palette.OutlineDarkness);
            return est.Exact && est.Scale > 0 ? est.Scale : null;
        }

        /// <summary>
        /// 외곽선 맞추기 '크기를 줄여서'에서 받아들일 칸 크기(원본 px): 원본 외곽선 두께(<see cref="ScaleEstimate.Outline"/>, 외곽선이 칸보다 가늘어 더 큰 값을 고른
        /// 어림이면 <see cref="ScaleEstimate.ThinOutline"/> — 그 값)의 ±<see cref="ShrinkTolerance"/> — 외곽선 띠가 정확히 한 칸(칸이 두께의 ½이면 외곽선이 2칸).
        /// 자동 격자·칸 크기로 결과 크기가 아니면(수동 격자·배율·고정 크기), 깨끗한 그림의 정확한 배수이면, 외곽선을 못 쟀으면 null.
        /// </summary>
        private (double Lo, double Hi)? ShrinkBand()
        {
            var s0 = _start;
            if (s0.Grid.Mode != GridMode.Auto || s0.Size.Mode != OutputSizeMode.FromScale || _src.Split.Frames.Count == 0 || ExactScale() is not null) return null;
            var est = _src.EstimatedScale(-1, s0.Grid.Jitter, ScaleSource.Outline, s0.Palette.OutlineDarkness);
            if (est.Outline <= 0 || est.Exact) return null;
            double center = est.ThinOutline ? est.Scale : est.Outline;
            return (center * (1 - ShrinkTolerance), center * (1 + ShrinkTolerance));
        }

        /// <summary>'크기를 줄여서'로 시작하고 외곽선 맞추기 항목은 바꿔 보지 않음 — 칸 크기는 <see cref="ShrinkBand"/> 안에서만 찾는다.</summary>
        private bool ShrinkOnly => _start.Cleanup.OutlineFit == OutlineFit.Shrink && _shrink is not null && !_opt.Params.HasFlag(OptimizeParam.OutlineFit);

        /// <summary>
        /// 칸 크기 찾기(원본 px): 찾는 범위 [<c>Lo</c>, <c>Hi</c>]와 꼭 돌려 볼 후보(<c>Candidates</c>, 오름차순, 1~<see cref="PixelateRanges.Scale"/> 최댓값).
        /// 바탕 값 = 시작 결과의 칸 크기, 수동이면 수동 칸 크기, 자동 어림(지금 설정이 쓰는 값). 칸 크기나 외곽선 맞추기를 바꿔 볼 때는 세 방식 값(외곽선 두께·
        /// 색 경계·덩어리 경계)과 모든 바탕 값의 2·3배·½·⅓도 후보 — 어림이 진짜 칸의 몇 분의 1로 잘못 나와도 맞는 크기에서 찾기를 시작할 수 있게.
        /// 범위 = 바닥(시작 결과·자동 어림·수동 칸 크기 중 가장 작은 값 — 외곽선 맞추기도 바꿔 볼 때는 다른 방식 값이 어림의 ½·⅓을 따로 쟀으면 그 값까지: 외곽선을
        /// 2~3칸 두께로 그린 그림은 색 경계 크기 + 외곽선만 얇게가 맞는 답일 수 있다)의 0.75배(1.5px 아래로는 내리지 않음)부터 가장 큰 후보의 1.25배까지.
        /// 바닥의 0.75배보다 작은 후보는 버린다: 칸이 작을수록 충실도는 늘 좋아져서, 근거 없이 어림보다 작은 쪽을 열어 두면 점수가 그림을 덜 줄이는 쪽으로 끌린다
        /// (표본 창 소년: 어림의 ⅓까지 열면 24px → 5.6px, 결과 171×175). 결과 블록 반복도(<see cref="ScoreTerm.BlockRepetition"/>)는 칸이 진짜 칸의 ½ 이하인
        /// 결과를 막지만 ⅔ 같은 크기는 가려내지 못한다.
        /// '크기를 줄여서'만 보면(<see cref="ShrinkOnly"/>) 범위는 <see cref="ShrinkBand"/>, 후보는 그 안의 값(가운데 값 포함)만.
        /// 고정 크기면 비어 있고, 깨끗한 그림의 정확한 배수(<see cref="ExactScale"/>)면 그 크기 하나.
        /// </summary>
        private (double Lo, double Hi, List<double> Candidates) ScaleSearch(PixelateResult first)
        {
            var s0 = _start;
            if (s0.Size.Mode == OutputSizeMode.FixedSize || first.Scale <= 0) return (0, 0, []);
            if (ExactScale() is { } exact) return (exact, exact, [exact]);
            var floor = new List<double> { first.Scale };
            var baseValues = new List<double> { first.Scale };
            if (s0.Grid.Mode == GridMode.Manual && s0.Size.Mode != OutputSizeMode.ScaleFactor)
            {
                floor.Add(PixelateRanges.Scale.Clamp(s0.Grid.Scale));
                baseValues.Add(PixelateRanges.Scale.Clamp(s0.Grid.Scale));
            }
            bool wide = (_opt.Params & (OptimizeParam.Scale | OptimizeParam.OutlineFit)) != 0;
            var est = _src.EstimatedScale(-1, s0.Grid.Jitter, Pixelator.ScaleSourceFor(s0), s0.Palette.OutlineDarkness);
            if (est.Scale > 0)
            {
                floor.Add(est.Scale);
                baseValues.Add(est.Scale);
            }
            if (wide) baseValues.AddRange([est.Outline, est.Edges, est.Blocks]);
            var scale = PixelateRanges.Scale;
            List<double> Clean(IEnumerable<double> values, double lo, double hi)
                => [.. values.Where(v => v > 0 && v >= Math.Max(scale.Min, lo) && v <= hi).Select(v => Math.Round(Math.Min(v, scale.Max), 2)).Distinct().Order()];
            if (ShrinkOnly && _shrink is { } band)
            {
                double center = (band.Lo + band.Hi) / 2;
                return (band.Lo, band.Hi, Clean(baseValues.Append(center), band.Lo, band.Hi));
            }
            // 외곽선을 2~3칸 두께로 그린 그림(외곽선 맞추기도 바꿔 볼 때): 다른 방식 값이 어림의 ½·⅓(±15%)을 따로 쟀으면 그 크기까지 받아들인다.
            if (_opt.Params.HasFlag(OptimizeParam.OutlineFit) && est.Scale > 0)
                foreach (double v in new[] { est.Outline, est.Edges, est.Blocks })
                    if (v > 0 && (Math.Abs(v / (est.Scale / 2) - 1) <= 0.15 || Math.Abs(v / (est.Scale / 3) - 1) <= 0.15)) floor.Add(v);
            double low = floor.Min();
            double min = Math.Max(Math.Min(1.5, low), low * 0.75);
            var all = baseValues.Where(v => v > 0).Distinct().ToList();
            // 배수·약수 후보도 칸 크기 상한(스프라이트 짧은 변의 1/5 — PixelateSource.MaxCell) 안에서만.
            double cap = _src.MaxCell(-1);
            if (wide)
                foreach (double v in all.ToList())
                    all.AddRange(new[] { v * 2, v * 3, v / 2, v / 3 }.Where(x => cap <= 0 || x <= cap));
            var candidates = Clean(all, min, double.PositiveInfinity);
            return (min, candidates.Count > 0 ? candidates.Max() * 1.25 : min, candidates);
        }

        /// <summary>
        /// 실제 칸 크기(원본 px)로 받아들일 범위: 칸 크기 찾기 범위(<see cref="ScaleSearch"/>)에 2% 여유. 깨끗한 그림의 정확한 배수면 그 크기 ±2%만. 고정 크기면 제한 없음.
        /// 다른 항목(예: 외곽선 맞추기·외곽선 밝기)이 자동 어림을 바꿔 범위 밖 크기(예: 1px — 줄이지 않은 그림)로 건너뛴 시도는 기록만 하고 고르지 않는다.
        /// 외곽선 맞추기가 '크기를 줄여서'인 시도는 <see cref="ShrinkBand"/> 안이어야 한다(외곽선 맞추기 항목으로 다른 방식을 바꿔 보는 시도는 넓은 범위).
        /// </summary>
        private (double Lo, double Hi) ScaleBand(PixelateResult first)
        {
            var (lo, hi, known) = ScaleSearch(first);
            if (known.Count == 0) return (0, double.PositiveInfinity);
            return (lo * 0.98, hi * 1.02);
        }

        private readonly ConcurrentDictionary<BackgroundSettings, Lazy<PixelateSource>> _sources = new();

        /// <summary>시도의 준비 결과: 배경·나누기 설정이 시작과 같으면 그대로, 다르면(스티커 테두리·배경 남기기) 배경 설정마다 한 번 다시 준비.</summary>
        private PixelateSource SourceFor(PixelateSettings s)
            => _src.Matches(s) ? _src
                : _sources.GetOrAdd(s.Background, _ => new Lazy<PixelateSource>(() => PixelateSource.Prepare(_src.Original, s))).Value;

        private List<Axis> BuildAxes(PixelateResult first)
        {
            var s0 = _start;
            var p = _opt.Params & Applicable(s0, _opt.Params);
            var axes = new List<Axis>();
            if (p.HasFlag(OptimizeParam.Scale) && ScaleAxis(first) is { } scale) axes.Add(scale);
            double half = Math.Max(0.5, s0.Grid.Scale / 2);
            if (p.HasFlag(OptimizeParam.OffsetX))
                axes.Add(Num(OptimizeParam.OffsetX, PixelateRanges.Offset, s0.Grid.OffsetX - half, s0.Grid.OffsetX + half, 0.5, 1,
                    s => s.Grid.OffsetX, (s, v) => s with { Grid = s.Grid with { OffsetX = v } }));
            if (p.HasFlag(OptimizeParam.OffsetY))
                axes.Add(Num(OptimizeParam.OffsetY, PixelateRanges.Offset, s0.Grid.OffsetY - half, s0.Grid.OffsetY + half, 0.5, 1,
                    s => s.Grid.OffsetY, (s, v) => s with { Grid = s.Grid with { OffsetY = v } }));
            if (p.HasFlag(OptimizeParam.Colors))
            {
                int resolved = first.ResolvedColors;
                axes.Add(Num(OptimizeParam.Colors, PixelateRanges.Colors, 4, 48, 1, 0,
                    s => s.Palette.Colors > 0 ? s.Palette.Colors : resolved, (s, v) => s with { Palette = s.Palette with { Colors = (int)v } }));
            }
            if (p.HasFlag(OptimizeParam.DownsampleMode))
            {
                var modes = Enum.GetValues<DownsampleMode>();
                axes.Add(new Axis(OptimizeParam.DownsampleMode, 0, modes.Length - 1, 1, 0, modes.Length,
                    s => Array.IndexOf(modes, s.Downsample.Mode), (s, v) => s with { Downsample = s.Downsample with { Mode = modes[(int)v] } }));
            }
            if (p.HasFlag(OptimizeParam.BorderFraction))
                axes.Add(Num(OptimizeParam.BorderFraction, PixelateRanges.BorderFraction, 0, 0.4, 0.01, 2,
                    s => s.Downsample.BorderFraction, (s, v) => s with { Downsample = s.Downsample with { BorderFraction = v } }));
            if (p.HasFlag(OptimizeParam.AlphaThreshold))
                axes.Add(Num(OptimizeParam.AlphaThreshold, PixelateRanges.AlphaThreshold, 0.2, 0.8, 0.02, 2,
                    s => s.Downsample.AlphaThreshold, (s, v) => s with { Downsample = s.Downsample with { AlphaThreshold = v } }));
            if (p.HasFlag(OptimizeParam.SnapStep))
                axes.Add(Num(OptimizeParam.SnapStep, PixelateRanges.SnapStep, 8, 40, 1, 0,
                    s => s.Downsample.SnapStep, (s, v) => s with { Downsample = s.Downsample with { SnapStep = (int)v } }));
            if (p.HasFlag(OptimizeParam.OutlineDarkness))
                axes.Add(Num(OptimizeParam.OutlineDarkness, PixelateRanges.OutlineDarkness, 0.15, 0.35, 0.01, 2,
                    s => s.Palette.OutlineDarkness, (s, v) => s with { Palette = s.Palette with { OutlineDarkness = v } }));
            if (p.HasFlag(OptimizeParam.Cleanup))
            {
                axes.Add(Toggle(s => s.Cleanup.RemoveOrphans, (s, on) => s with { Cleanup = s.Cleanup with { RemoveOrphans = on } }));
                axes.Add(Toggle(s => s.Cleanup.FillHoles, (s, on) => s with { Cleanup = s.Cleanup with { FillHoles = on } }));
                axes.Add(Toggle(s => s.Cleanup.ReduceJaggies, (s, on) => s with { Cleanup = s.Cleanup with { ReduceJaggies = on } }));
            }
            if (p.HasFlag(OptimizeParam.Dither))
            {
                PaletteDither[] dithers = [PaletteDither.None, PaletteDither.Bayer2, PaletteDither.Bayer4];
                axes.Add(new Axis(OptimizeParam.Dither, 0, 2, 1, 0, dithers.Length,
                    s => Math.Max(0, Array.IndexOf(dithers, s.Palette.Dither)), (s, v) => s with { Palette = s.Palette with { Dither = dithers[(int)v] } }));
            }
            if (p.HasFlag(OptimizeParam.OutlineFit))
            {
                var fits = Enum.GetValues<OutlineFit>();
                axes.Add(new Axis(OptimizeParam.OutlineFit, 0, fits.Length - 1, 1, 0, fits.Length,
                    s => Array.IndexOf(fits, s.Cleanup.OutlineFit), (s, v) => s with { Cleanup = s.Cleanup with { OutlineFit = fits[(int)v] } }));
            }
            if (p.HasFlag(OptimizeParam.PaletteColorSource))
            {
                PaletteColorSource[] sources = [PaletteColorSource.Original, PaletteColorSource.Mean];
                axes.Add(new Axis(OptimizeParam.PaletteColorSource, 0, 1, 1, 0, sources.Length,
                    s => Math.Max(0, Array.IndexOf(sources, s.Palette.ColorSource)), (s, v) => s with { Palette = s.Palette with { ColorSource = sources[(int)v] } }));
            }
            if (p.HasFlag(OptimizeParam.PaletteMethod))
            {
                PaletteMethod[] methods = [PaletteMethod.KMeans, PaletteMethod.MedianCut];
                axes.Add(new Axis(OptimizeParam.PaletteMethod, 0, 1, 1, 0, methods.Length,
                    s => Math.Max(0, Array.IndexOf(methods, s.Palette.Method)), (s, v) => s with { Palette = s.Palette with { Method = methods[(int)v] } }));
            }
            // 가장자리 칸 가중은 원본 색에서만 쓰므로, 평균 색으로 시작하고 팔레트 색을 바꿔 보지 않으면 건너뜀(Applicable).
            if (p.HasFlag(OptimizeParam.EdgeWeight))
                axes.Add(Num(OptimizeParam.EdgeWeight, PixelateRanges.EdgeWeight, 0, 1, 0.05, 2,
                    s => s.Palette.EdgeWeight, (s, v) => s with { Palette = s.Palette with { EdgeWeight = v } }));
            if (p.HasFlag(OptimizeParam.SnapOutline))
                axes.Add(new Axis(OptimizeParam.SnapOutline, 0, 1, 1, 0, 2,
                    s => s.Palette.SnapOutline ? 1 : 0, (s, v) => s with { Palette = s.Palette with { SnapOutline = v >= 0.5 } }));
            if (p.HasFlag(OptimizeParam.StickerBorder))
                axes.Add(new Axis(OptimizeParam.StickerBorder, 0, 1, 1, 0, 2,
                    s => s.Background.RemoveStickerBorder ? 1 : 0, (s, v) => s with { Background = s.Background with { RemoveStickerBorder = v >= 0.5 } }));
            // 칸 경계 지역 보정: 켜기·끄기, 최대 이동·띠 너비는 켠 점에서만(꺼진 점에서는 결과가 같음).
            if (p.HasFlag(OptimizeParam.LocalGrid))
            {
                static bool IsLocal(PixelateSettings s) => s.Grid.LocalCorrection;
                axes.Add(new Axis(OptimizeParam.LocalGrid, 0, 1, 1, 0, 2,
                    s => s.Grid.LocalCorrection ? 1 : 0, (s, v) => s with { Grid = s.Grid with { LocalCorrection = v >= 0.5 } }));
                axes.Add(Num(OptimizeParam.LocalGrid, PixelateRanges.LocalShift, 0.1, 0.5, 0.05, 2,
                    s => s.Grid.LocalShift, (s, v) => s with { Grid = s.Grid with { LocalShift = v } }) with { Active = IsLocal });
                axes.Add(Num(OptimizeParam.LocalGrid, PixelateRanges.LocalBand, 1, 8, 1, 0,
                    s => s.Grid.LocalBand, (s, v) => s with { Grid = s.Grid with { LocalBand = (int)v } }) with { Active = IsLocal });
            }
            if (p.HasFlag(OptimizeParam.ColorDistance))
            {
                var modes = Enum.GetValues<ColorDistanceMode>();
                axes.Add(new Axis(OptimizeParam.ColorDistance, 0, modes.Length - 1, 1, 0, modes.Length,
                    s => Array.IndexOf(modes, s.Palette.ColorDistance), (s, v) => s with { Palette = s.Palette with { ColorDistance = modes[(int)v] } }));
                axes.Add(Num(OptimizeParam.ColorDistance, PixelateRanges.HueWeight, 1, 3, 0.25, 2,
                    s => s.Palette.HueWeight, (s, v) => s with { Palette = s.Palette with { HueWeight = v } })
                    with { Active = s => s.Palette.ColorDistance == ColorDistanceMode.OkLchHue });
            }
            if (p.HasFlag(OptimizeParam.DistinctHues))
            {
                axes.Add(new Axis(OptimizeParam.DistinctHues, 0, 1, 1, 0, 2,
                    s => s.Palette.KeepDistinctHues ? 1 : 0, (s, v) => s with { Palette = s.Palette with { KeepDistinctHues = v >= 0.5 } }));
                axes.Add(Num(OptimizeParam.DistinctHues, PixelateRanges.DistinctMinHue, 10, 40, 1, 0,
                    s => s.Palette.DistinctMinHue, (s, v) => s with { Palette = s.Palette with { DistinctMinHue = v } })
                    with { Active = s => s.Palette.KeepDistinctHues });
            }
            if (p.HasFlag(OptimizeParam.ShadeFamily))
                axes.Add(new Axis(OptimizeParam.ShadeFamily, 0, 1, 1, 0, 2,
                    s => s.Palette.PreferShadeFamily ? 1 : 0, (s, v) => s with { Palette = s.Palette with { PreferShadeFamily = v >= 0.5 } }));
            // 벡터 설정은 벡터로 다시 그리기에서만 뜻이 있으므로 그 점에서만 바꿔 본다(외곽선 맞추기 항목이 벡터로 바꾼 점에서 언덕 오르기로 다듬음).
            static bool IsVector(PixelateSettings s) => s.Cleanup.OutlineFit == OutlineFit.Vector;
            if (p.HasFlag(OptimizeParam.VectorTolerance))
                axes.Add(Num(OptimizeParam.VectorTolerance, PixelateRanges.VectorTolerance, 0.2, 2, 0.05, 2,
                    s => s.Cleanup.VectorTolerance, (s, v) => s with { Cleanup = s.Cleanup with { VectorTolerance = v } }) with { Active = IsVector });
            if (p.HasFlag(OptimizeParam.VectorBlob))
                axes.Add(Num(OptimizeParam.VectorBlob, PixelateRanges.VectorBlobCells, 1, 24, 1, 0,
                    s => s.Cleanup.VectorBlobCells, (s, v) => s with { Cleanup = s.Cleanup with { VectorBlobCells = v } }) with { Active = IsVector });
            return axes;
        }

        private static Axis Toggle(Func<PixelateSettings, bool> get, Func<PixelateSettings, bool, PixelateSettings> set)
            => new(OptimizeParam.Cleanup, 0, 1, 1, 0, 2, s => get(s) ? 1 : 0, (s, v) => set(s, v >= 0.5));

        /// <summary>숫자 값 하나: 찾기 범위 [<paramref name="lo"/>, <paramref name="hi"/>]를 허용 범위(<see cref="PixelateRanges"/>) 안으로 줄인다.</summary>
        private static Axis Num(OptimizeParam param, PixelateRange allowed, double lo, double hi, double step, int decimals,
            Func<PixelateSettings, double> get, Func<PixelateSettings, double, PixelateSettings> set)
        {
            var (min, max) = allowed.Bound(lo, hi);
            return new Axis(param, min, max, step, decimals, 0, get, set);
        }

        /// <summary>
        /// 칸 크기: 찾는 범위(<see cref="ScaleSearch"/> — 바닥의 0.75배부터 가장 큰 후보(세 방식 어림과 그 2·3배)의 1.25배까지, 시작 칸 크기가 밖이면 시작 ±25%까지 넓힘,
        /// <see cref="ScaleBand"/>와 같은 범위)를 고르게 7곳 + 후보 하나하나를 거칠게 돌려 본다 — 시작 칸 크기 ±25%만 보면 잘못 찾은 작은 칸 크기에 갇힌다.
        /// '크기를 줄여서'만 보면(<see cref="ShrinkOnly"/>) 외곽선 두께 ±<see cref="ShrinkTolerance"/> 안만. 고정 크기면 칸 크기가 결과 크기로 정해지므로 건너뜀.
        /// </summary>
        private Axis? ScaleAxis(PixelateResult first)
        {
            var s0 = _start;
            if (s0.Size.Mode == OutputSizeMode.FixedSize || ExactScale() is not null) return null;
            var (searchLo, searchHi, known) = ScaleSearch(first);
            (double Lo, double Hi) Range(double current)
            {
                if (known.Count == 0) return (Math.Max(Math.Min(1.5, current), current * 0.75), current * 1.25);
                if (ShrinkOnly) return (searchLo, searchHi);
                return (Math.Min(searchLo, Math.Max(Math.Min(1.5, current), current * 0.75)), Math.Max(searchHi, current * 1.25));
            }
            if (s0.Size.Mode == OutputSizeMode.ScaleFactor)
            {
                var factor = PixelateRanges.Factor;
                double current = 1 / factor.Clamp(s0.Size.Factor);
                var (lo, hi) = Range(current);
                return Num(OptimizeParam.Scale, new PixelateRange(1 / factor.Max, 1 / factor.Min), lo, hi, 0.1, 2,
                    s => Math.Round(1 / factor.Clamp(s.Size.Factor), 2), (s, v) => s with { Size = s.Size with { Factor = factor.Clamp(Math.Round(1 / v, 3)) } })
                    with { Seeds = known };
            }
            if (s0.Grid.Mode == GridMode.Manual)
            {
                var (lo, hi) = Range(PixelateRanges.Scale.Clamp(s0.Grid.Scale));
                return Num(OptimizeParam.Scale, PixelateRanges.Scale, lo, hi, 0.1, 2,
                    s => s.Grid.Scale, (s, v) => s with { Grid = s.Grid with { Scale = v } }) with { Seeds = known };
            }
            // 자동: 실제 칸 크기 = 찾은 칸 크기 + 더할 값(실제 칸 크기도 수동 칸 크기 범위 안).
            double detected = first.DetectedScale;
            if (detected <= 0) return null;
            var (alo, ahi) = Range(Math.Max(1, detected + s0.Grid.ScaleAdjust));
            var adjust = PixelateRanges.ScaleAdjust;
            var scale = PixelateRanges.Scale;
            return Num(OptimizeParam.Scale, new PixelateRange(Math.Max(adjust.Min, scale.Min - detected), Math.Min(adjust.Max, scale.Max - detected)), alo - detected, ahi - detected, 0.1, 2,
                s => s.Grid.ScaleAdjust, (s, v) => s with { Grid = s.Grid with { ScaleAdjust = v } }) with { Seeds = [.. known.Select(v => v - detected)] };
        }
    }

    /// <summary>시도 요약(고른 항목의 값, 칸 크기는 실제로 쓴 크기): 예) "scale 8.02 · 6 colors · Median · orphans on, holes on, jaggies off".</summary>
    public static string Summary(PixelateSettings s, PixelateResult r, OptimizeParam p)
    {
        var ci = CultureInfo.InvariantCulture;
        var parts = new List<string>();
        if (p.HasFlag(OptimizeParam.Scale)) parts.Add("scale " + r.Scale.ToString("0.##", ci));
        if ((p & (OptimizeParam.OffsetX | OptimizeParam.OffsetY)) != 0 && s.Grid.Mode == GridMode.Manual)
            parts.Add("offset " + s.Grid.OffsetX.ToString("0.##", ci) + "," + s.Grid.OffsetY.ToString("0.##", ci));
        if (p.HasFlag(OptimizeParam.Colors)) parts.Add(r.ResolvedColors.ToString(ci) + " colors");
        if (p.HasFlag(OptimizeParam.DownsampleMode)) parts.Add(s.Downsample.Mode.ToString());
        if (p.HasFlag(OptimizeParam.BorderFraction)) parts.Add("border " + s.Downsample.BorderFraction.ToString("0.##", ci));
        if (p.HasFlag(OptimizeParam.AlphaThreshold)) parts.Add("alpha " + s.Downsample.AlphaThreshold.ToString("0.##", ci));
        if (p.HasFlag(OptimizeParam.SnapStep)) parts.Add("snap " + s.Downsample.SnapStep.ToString(ci));
        if (p.HasFlag(OptimizeParam.OutlineDarkness)) parts.Add("dark " + s.Palette.OutlineDarkness.ToString("0.##", ci));
        if (p.HasFlag(OptimizeParam.Cleanup))
            parts.Add($"orphans {OnOff(s.Cleanup.RemoveOrphans)}, holes {OnOff(s.Cleanup.FillHoles)}, jaggies {OnOff(s.Cleanup.ReduceJaggies)}");
        if (p.HasFlag(OptimizeParam.Dither)) parts.Add("dither " + s.Palette.Dither);
        if (p.HasFlag(OptimizeParam.OutlineFit)) parts.Add("fit " + s.Cleanup.OutlineFit);
        if (p.HasFlag(OptimizeParam.PaletteMethod)) parts.Add(s.Palette.Method.ToString());
        if (p.HasFlag(OptimizeParam.PaletteColorSource)) parts.Add("palette " + s.Palette.ColorSource);
        if (p.HasFlag(OptimizeParam.EdgeWeight)) parts.Add("edge " + s.Palette.EdgeWeight.ToString("0.##", ci));
        if (p.HasFlag(OptimizeParam.SnapOutline)) parts.Add("snap outline " + OnOff(s.Palette.SnapOutline));
        if (p.HasFlag(OptimizeParam.StickerBorder)) parts.Add("sticker border " + OnOff(s.Background.RemoveStickerBorder));
        if (p.HasFlag(OptimizeParam.LocalGrid))
            parts.Add(s.Grid.LocalCorrection
                ? "local grid on " + s.Grid.LocalShift.ToString("0.##", ci) + "/" + s.Grid.LocalBand.ToString(ci)
                : "local grid off");
        if (p.HasFlag(OptimizeParam.ColorDistance))
            parts.Add("distance " + s.Palette.ColorDistance + (s.Palette.ColorDistance == ColorDistanceMode.OkLchHue ? " " + s.Palette.HueWeight.ToString("0.##", ci) : ""));
        if (p.HasFlag(OptimizeParam.DistinctHues))
            parts.Add(s.Palette.KeepDistinctHues ? "distinct hues on " + s.Palette.DistinctMinHue.ToString("0", ci) : "distinct hues off");
        if (p.HasFlag(OptimizeParam.ShadeFamily)) parts.Add("shade family " + OnOff(s.Palette.PreferShadeFamily));
        if (s.Cleanup.OutlineFit == OutlineFit.Vector)
        {
            if (p.HasFlag(OptimizeParam.VectorTolerance)) parts.Add("vector tolerance " + s.Cleanup.VectorTolerance.ToString("0.##", ci));
            if (p.HasFlag(OptimizeParam.VectorBlob)) parts.Add("vector blob " + s.Cleanup.VectorBlobCells.ToString("0", ci));
        }
        return string.Join(" · ", parts);
    }

    private static string OnOff(bool on) => on ? "on" : "off";
}
