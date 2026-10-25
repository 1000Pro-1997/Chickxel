using Gpixel.Core.Edit;

namespace Gpixel.Core.Pixelate;

/// <summary>후보를 만들 때 바꿔 볼 설정.</summary>
[Flags]
public enum CandidateVary
{
    None = 0,
    /// <summary>칸 크기 ±1(자동이면 찾은 값에 더함).</summary>
    Scale = 1,
    /// <summary>색 수 ±.</summary>
    PaletteSize = 2,
    /// <summary>칸 색 고르기 방식.</summary>
    DownsampleMode = 4,
    /// <summary>정리 켜기/끄기.</summary>
    Cleanup = 8,
    /// <summary>디더링 켜기/끄기.</summary>
    Dither = 16,
    /// <summary>외곽선 맞추기 방식(크기를 줄여서 ↔ 외곽선만 얇게).</summary>
    OutlineFit = 32,
    Default = Scale | PaletteSize | DownsampleMode | Cleanup | OutlineFit,
}

/// <summary>후보 하나: 번호(0 = 지금 설정), 설정, 결과, 점수.</summary>
public sealed record PixelateCandidate(int Index, PixelateSettings Settings, PixelateResult Result, PixelateScore Score);

/// <summary>
/// 후보 만들기: 지금 설정(첫 후보)에서 고른 항목을 하나씩 바꾼 설정, 모자라면 둘씩 바꾼 설정을 <c>count</c>개까지(같은 설정은 한 번).
/// 실행은 지금 설정을 먼저 돌려 자동 색 수·칸 크기를 안 뒤 나머지를 여러 스레드로 — 끝나는 대로 알린다.
/// </summary>
public static class PixelateCandidates
{
    /// <summary>
    /// <paramref name="baseResult"/>(지금 설정 결과 — 자동 색 수를 앎)에서 바꿔 볼 설정들(지금 설정 제외, 최대 <paramref name="count"/> − 1개).
    /// </summary>
    public static List<PixelateSettings> Variants(PixelateSettings b, PixelateResult baseResult, CandidateVary vary, int count)
    {
        var axes = new List<List<Func<PixelateSettings, PixelateSettings>>>();
        if (vary.HasFlag(CandidateVary.Scale)) axes.Add(ScaleVariants(b, baseResult));
        if (vary.HasFlag(CandidateVary.PaletteSize)) axes.Add(PaletteVariants(b, baseResult));
        if (vary.HasFlag(CandidateVary.DownsampleMode))
            axes.Add(Enum.GetValues<DownsampleMode>().Where(m => m != b.Downsample.Mode)
                .Select(m => (Func<PixelateSettings, PixelateSettings>)(s => s with { Downsample = s.Downsample with { Mode = m } })).ToList());
        if (vary.HasFlag(CandidateVary.Cleanup)) axes.Add([CleanupToggle(b)]);
        if (vary.HasFlag(CandidateVary.OutlineFit) && b.Cleanup.OutlineFit != OutlineFit.Off)
            axes.Add([s => s with { Cleanup = s.Cleanup with { OutlineFit = s.Cleanup.OutlineFit == OutlineFit.Shrink ? OutlineFit.Thin : OutlineFit.Shrink } }]);
        if (vary.HasFlag(CandidateVary.Dither))
            axes.Add([s => s with { Palette = s.Palette with { Dither = s.Palette.Dither == PaletteDither.None ? PaletteDither.Bayer4 : PaletteDither.None } }]);
        axes.RemoveAll(a => a.Count == 0);

        var seen = new HashSet<string> { b.Key() };
        var result = new List<PixelateSettings>();
        void Add(PixelateSettings s)
        {
            if (result.Count < count - 1 && seen.Add(s.Key())) result.Add(s);
        }
        // 하나씩: 축마다 돌아가며(앞 값부터).
        int depth = axes.Count == 0 ? 0 : axes.Max(a => a.Count);
        for (int i = 0; i < depth; i++)
            foreach (var axis in axes)
                if (i < axis.Count) Add(axis[i](b));
        // 둘씩.
        for (int a = 0; a < axes.Count; a++)
            for (int c = a + 1; c < axes.Count; c++)
                foreach (var fa in axes[a])
                    foreach (var fc in axes[c])
                        Add(fc(fa(b)));
        return result;
    }

    private static List<Func<PixelateSettings, PixelateSettings>> ScaleVariants(PixelateSettings b, PixelateResult r)
    {
        switch (b.Size.Mode)
        {
            case OutputSizeMode.FixedSize:
                return [];
            case OutputSizeMode.ScaleFactor:
            {
                var factor = PixelateRanges.Factor;
                double sc = 1 / factor.Clamp(b.Size.Factor);
                return [.. new[] { sc - 1, sc + 1 }.Where(v => v >= 1 && factor.Contains(1 / v))
                    .Select(v => (Func<PixelateSettings, PixelateSettings>)(s => s with { Size = s.Size with { Factor = 1 / v } }))];
            }
        }
        if (b.Grid.Mode == GridMode.Manual)
            return [.. new[] { b.Grid.Scale - 1, b.Grid.Scale + 1 }.Where(v => v >= 2 && PixelateRanges.Scale.Contains(v))
                .Select(v => (Func<PixelateSettings, PixelateSettings>)(s => s with { Grid = s.Grid with { Scale = v } }))];
        return [.. new[] { -1.0, 1.0 }.Where(d => r.DetectedScale + b.Grid.ScaleAdjust + d >= 2 && PixelateRanges.ScaleAdjust.Contains(b.Grid.ScaleAdjust + d))
            .Select(d => (Func<PixelateSettings, PixelateSettings>)(s => s with { Grid = s.Grid with { ScaleAdjust = b.Grid.ScaleAdjust + d } }))];
    }

    private static List<Func<PixelateSettings, PixelateSettings>> PaletteVariants(PixelateSettings b, PixelateResult r)
    {
        if (b.Palette.Method == PaletteMethod.Existing) return [];
        if (b.Palette.Method == PaletteMethod.KeepOriginal)
            return [s => s with { Palette = s.Palette with { Method = PaletteMethod.KMeans, Colors = 0 } }];
        int k = b.Palette.Colors > 0 ? b.Palette.Colors : r.ResolvedColors;
        int step = Math.Max(2, (int)Math.Round(k * 0.25));
        return [.. new[] { k - step, k + step }.Where(v => v >= 2 && v <= PaletteBuilder.MaxColors)
            .Select(v => (Func<PixelateSettings, PixelateSettings>)(s => s with { Palette = s.Palette with { Colors = v } }))];
    }

    private static Func<PixelateSettings, PixelateSettings> CleanupToggle(PixelateSettings b)
    {
        var c = b.Cleanup;
        bool anyOn = c.RemoveOrphans || c.FillHoles || c.Outline != OutlineMode.None || c.ReduceJaggies;
        var to = anyOn
            ? c with { RemoveOrphans = false, FillHoles = false, Outline = OutlineMode.None, ReduceJaggies = false }
            : c with { RemoveOrphans = true, FillHoles = true, Outline = OutlineMode.UnifyDarkest };
        return s => s with { Cleanup = to };
    }

    /// <summary>
    /// 후보를 모두 돌린다: 지금 설정을 먼저(번호 0), 그다음 바꾼 설정들을 여러 스레드로. 하나 끝날 때마다 <paramref name="onResult"/>(아무 스레드).
    /// 점수의 목표 색 수는 지금 설정의 색 수(자동이면 찾은 수). 실패한 결과(<see cref="PixelateResult.Failed"/>)는 맨 뒤. 취소하면 <see cref="OperationCanceledException"/>.
    /// </summary>
    public static List<PixelateCandidate> Run(PixelateSource src, PixelateSettings b, CandidateVary vary, int count, ScoreWeights weights,
        Action<PixelateCandidate>? onResult = null, CancellationToken ct = default)
    {
        var first = Pixelator.Run(src, b, ct);
        int? target = b.Palette.Method == PaletteMethod.KeepOriginal ? null : first.ResolvedColors;
        var all = new List<PixelateCandidate> { new(0, b, first, PixelateScoring.Score(first, weights, target)) };
        onResult?.Invoke(all[0]);
        var variants = count > 1 ? Variants(b, first, vary, count) : [];
        var done = new PixelateCandidate?[variants.Count];
        Parallel.For(0, variants.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
        {
            var r = Pixelator.Run(src, variants[i], ct);
            var cand = new PixelateCandidate(i + 1, variants[i], r, PixelateScoring.Score(r, weights, target));
            done[i] = cand;
            onResult?.Invoke(cand);
        });
        all.AddRange(done.OfType<PixelateCandidate>());
        return [.. all.OrderBy(c => c.Result.Failed).ThenByDescending(c => c.Score.Total).ThenBy(c => c.Index)];
    }
}
