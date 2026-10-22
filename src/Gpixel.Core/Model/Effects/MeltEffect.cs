namespace Gpixel.Core.Model;

/// <summary>
/// 녹아내리기: 보이는 픽셀이 열마다 아래로 흘러내려 사라진다. 열(원본 기준 x)마다 (시드, 열) 무작위로 늦게 시작 d = 불규칙함 × 0.5 × r₁,
/// 빠르기 s = 1 − 불규칙함 × 0.5 × r₂, 그 열의 진행 q = clamp((u − d) / (1 − d)), 내려감 = 거리 × s × q² (중력처럼 점점 빨리).
/// 늘어짐이면 열 아래쪽 픽셀일수록 더 내려가(위 40% → 아래 100%) 사이를 위 픽셀 색으로 채운 흐른 자국이 생긴다.
/// 사라짐이면 q가 0.65를 넘으면서 알파가 줄어 q = 1(늦어도 범위 끝)에서 투명. 시작(u = 0)은 그림 그대로, 여백은 아래로 거리만큼.
/// </summary>
public sealed record MeltEffect : AnimatedEffect
{
    public const int MaxDistance = 128, MaxSeed = 9999;

    private readonly int _distance = 12, _irregular = 60, _seed = 1;

    public MeltEffect() => Easing = EffectEasing.Linear;

    /// <summary>거리: 끝까지 내려가는 최대 px 1~<see cref="MaxDistance"/>.</summary>
    public int Distance { get => _distance; init => _distance = Math.Clamp(value, 1, MaxDistance); }

    /// <summary>불규칙함 0~100%: 열마다 시작 늦음·빠르기의 무작위.</summary>
    public int Irregularity { get => _irregular; init => _irregular = Math.Clamp(value, 0, 100); }

    /// <summary>늘어짐: 흘러내린 자국(열이 길게 늘어남).</summary>
    public bool Stretch { get; init; } = true;

    /// <summary>사라짐: 다 흘러내리면서 투명해짐.</summary>
    public bool Fade { get; init; } = true;

    public int Seed { get => _seed; init => _seed = Math.Clamp(value, 0, MaxSeed); }

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_Melt;
}
