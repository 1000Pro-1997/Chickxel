using Gpixel.Core.Edit;

namespace Gpixel.Core.Model;

/// <summary>색을 그림 픽셀에 섞는 방법(그라데이션 덮기·안쪽 빛).</summary>
public enum EffectBlend
{
    /// <summary>덮기: 그 색 쪽으로.</summary>
    Normal,
    /// <summary>곱하기: 어둡게(흰색 = 그대로).</summary>
    Multiply,
    /// <summary>스크린: 밝게(검정 = 그대로).</summary>
    Screen,
}

/// <summary>순서 디더(Bayer) 무늬 크기. 없음 = 부드러운 단계, 2×2·4×4 = 두 색만 쓰는 픽셀아트 무늬.</summary>
public enum EffectDither
{
    None,
    Bayer2,
    Bayer4,
}

/// <summary>
/// 시간 그라데이션: 효과 범위의 진행 0→1에 따라 <see cref="Gradient"/>의 색을 골라 보이는 픽셀에
/// <see cref="Strength"/>%만큼 섞는다(픽셀 알파는 그대로). 부드러운 보간은 프레임마다 한 색으로 변하고,
/// 디더를 켜면 전환 중인 두 색을 픽셀 무늬로 섞는다.
/// </summary>
public sealed record GradientOverlayEffect : AnimatedEffect
{
    private readonly int _strength = 100;

    public EffectColorGradient Gradient { get; init; } = new()
    {
        Stops = [new(0, new Color32(255, 230, 120)), new(1, new Color32(200, 60, 40))],
    };

    /// <summary>0~100(%).</summary>
    public int Strength { get => _strength; init => _strength = Math.Clamp(value, 0, 100); }

    public EffectBlend Blend { get; init; } = EffectBlend.Normal;

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_GradientChange;
}
