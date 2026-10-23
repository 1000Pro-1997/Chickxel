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

/// <summary>시간 그라데이션의 적용 방식.</summary>
public enum GradientChangeMode
{
    /// <summary>프레임마다 모든 보이는 픽셀에 같은 색을 적용한다.</summary>
    ColorTransition,
    /// <summary>보이는 그림 중심 둘레의 각도에 따라 색을 고르고 지정한 각도만큼 회전한다.</summary>
    RadialRotation,
}

/// <summary>
/// 시간 그라데이션: <see cref="Mode"/>에 따라 진행 0→1의 한 색을 모든 보이는 픽셀에 섞거나,
/// 보이는 그림 중심 둘레의 각도별 색을 <see cref="Angle"/>만큼 돌린다. 디더와 세기·섞기는 두 방식에 공통으로 적용한다.
/// 픽셀 알파는 그대로 둔다.
/// </summary>
public sealed record GradientOverlayEffect : AnimatedEffect
{
    public const int MaxAngle = 1440;
    private readonly int _strength = 100;
    private readonly int _angle = 360;

    public EffectColorGradient Gradient { get; init; } = new()
    {
        Stops = [new(0, new Color32(255, 230, 120)), new(1, new Color32(200, 60, 40))],
    };

    public GradientChangeMode Mode { get; init; } = GradientChangeMode.ColorTransition;

    /// <summary>방사형 방식의 전체 회전 각도(-1440~1440°). 양수는 반시계, 음수는 시계 방향.</summary>
    public int Angle { get => _angle; init => _angle = Math.Clamp(value, -MaxAngle, MaxAngle); }

    /// <summary>0~100(%).</summary>
    public int Strength { get => _strength; init => _strength = Math.Clamp(value, 0, 100); }

    public EffectBlend Blend { get; init; } = EffectBlend.Normal;

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_GradientChange;
}
