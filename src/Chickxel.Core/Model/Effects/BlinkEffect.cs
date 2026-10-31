namespace Chickxel.Core.Model;

/// <summary>깜빡이기 간격을 정하는 방식.</summary>
public enum BlinkTiming
{
    /// <summary>보임 N프레임, 숨김 M프레임을 범위 첫 프레임부터 되풀이.</summary>
    Frames,
    /// <summary>범위 안에서 N번 깜빡임(범위의 프레임을 보임·숨김 2N칸으로 고르게 나눔).</summary>
    Count,
}

/// <summary>깜빡이기에서 숨기는 모습.</summary>
public enum BlinkHide
{
    /// <summary>완전 투명.</summary>
    Transparent,
    /// <summary>반투명(알파 × <see cref="BlinkEffect.Opacity"/>%).</summary>
    Translucent,
    /// <summary>디더 50%: 바둑판 칸만 남김(원본 왼쪽 위 기준이라 움직여도 무늬가 그대로).</summary>
    Dither,
}

/// <summary>
/// 깜빡이기: 보였다 숨었다를 되풀이한다(무적 시간 등). 프레임 간격이면 범위 안 몇 번째 프레임 s에서 s mod (보임 + 숨김) ≥ 보임일 때 숨김,
/// 횟수면 범위의 프레임 n개를 보임·숨김 2 × 횟수 칸으로 고르게 나눠 ⌊s × 2 × 횟수 / n⌋이 홀수일 때 숨김(첫 프레임은 보임, 되풀이해도 고르게 이어짐).
/// </summary>
public sealed record BlinkEffect : AnimatedEffect
{
    public const int MaxFrames = 16, MaxCount = 16;

    private readonly int _on = 1, _off = 1, _count = 4, _opacity = 40;

    public BlinkTiming Timing { get; init; } = BlinkTiming.Frames;

    /// <summary>보이는 프레임 수 1~<see cref="MaxFrames"/>(프레임 간격).</summary>
    public int OnFrames { get => _on; init => _on = Math.Clamp(value, 1, MaxFrames); }

    /// <summary>숨는 프레임 수 1~<see cref="MaxFrames"/>(프레임 간격).</summary>
    public int OffFrames { get => _off; init => _off = Math.Clamp(value, 1, MaxFrames); }

    /// <summary>범위 안 깜빡임 횟수 1~<see cref="MaxCount"/>(횟수).</summary>
    public int Count { get => _count; init => _count = Math.Clamp(value, 1, MaxCount); }

    public BlinkHide Hide { get; init; } = BlinkHide.Transparent;

    /// <summary>반투명으로 숨길 때 남는 불투명도 0~100%.</summary>
    public int Opacity { get => _opacity; init => _opacity = Math.Clamp(value, 0, 100); }

    public override string DisplayName => Chickxel.Core.Localization.DomainStrings.Effect_Blink;

    /// <summary>지금 프레임에서 숨는지.</summary>
    public bool Hidden => Timing == BlinkTiming.Frames
        ? FrameStep % (OnFrames + OffFrames) >= OnFrames
        : (FrameStep * 2 * Count / (FrameSpan + 1) & 1) == 1;
}
