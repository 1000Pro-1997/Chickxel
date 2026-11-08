namespace Chickxel.Core.IO;

/// <summary>GIF 프레임 길이를 정하는 방식.</summary>
public enum GifTimingMode
{
    /// <summary>프레임마다 정한 길이 그대로.</summary>
    Original,
    /// <summary>프레임 길이 ÷ 배속(2 = 두 배 빠르게).</summary>
    Speed,
    /// <summary>모든 프레임을 같은 길이(1000 ÷ FPS ms)로.</summary>
    Fps,
}

/// <summary>
/// GIF 재생 속도: 원래 속도, 배속, 모든 프레임 같은 FPS. GIF 내보내기와 갤러리에 올리기가 같이 쓰고(<see cref="GifOptions.Timing"/>),
/// 미리보기도 <see cref="DurationMs"/>로 같은 길이를 쓴다. 지연은 1/100초 단위라 가장 빠른 것은 <see cref="MaxFps"/>.
/// </summary>
public sealed record GifTiming
{
    public const double MinSpeed = 0.1, MaxSpeed = 10;
    public const int MinFps = 1, MaxFps = 100 / GifWriter.MinDelayCs;
    public const int DefaultFps = 12;

    /// <summary>프레임 길이 그대로.</summary>
    public static GifTiming Original { get; } = new();

    public GifTimingMode Mode { get; init; }
    /// <summary>배속(<see cref="MinSpeed"/>~<see cref="MaxSpeed"/>, <see cref="GifTimingMode.Speed"/>일 때).</summary>
    public double Speed { get; init; } = 1;
    /// <summary>초당 프레임 수(<see cref="MinFps"/>~<see cref="MaxFps"/>, <see cref="GifTimingMode.Fps"/>일 때).</summary>
    public int Fps { get; init; } = DefaultFps;

    /// <summary>프레임 길이 <paramref name="durationMs"/>를 이 속도로 바꾼 길이(ms, 반올림, 1 이상).</summary>
    public int DurationMs(int durationMs) => Mode switch
    {
        GifTimingMode.Speed => Math.Max(1, (int)Math.Round(durationMs / Math.Clamp(Speed, MinSpeed, MaxSpeed))),
        GifTimingMode.Fps => (int)Math.Round(1000.0 / Math.Clamp(Fps, MinFps, MaxFps)),
        _ => durationMs,
    };
}
