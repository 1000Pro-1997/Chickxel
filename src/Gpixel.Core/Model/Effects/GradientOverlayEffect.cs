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
/// 그라데이션 덮기: 보이는 픽셀(알파 0이 아닌 픽셀)에만 색 A → 색 B 그라데이션을 <see cref="Strength"/>%만큼 섞는다(알파는 그대로, 색의 알파는 쓰지 않음).
/// 그라데이션은 그 프레임 그림의 보이는 영역 한쪽 끝(A)에서 반대쪽 끝(B)까지 <see cref="Angle"/> 방향으로 간다(0 = 왼쪽 → 오른쪽, 90 = 아래 → 위, 270 = 위 → 아래).
/// 디더를 켜면 픽셀마다 A·B 중 하나만 고른다(Bayer 무늬, 효과 원본 왼쪽 위 기준이라 여백이 달라도 같은 무늬).
/// </summary>
public sealed record GradientOverlayEffect : LayerEffect
{
    private readonly int _angle = 270, _strength = 100;

    public Color32 ColorA { get; init; } = new(255, 230, 120);
    public Color32 ColorB { get; init; } = new(200, 60, 40);

    /// <summary>A에서 B로 가는 방향(도, 0~359, 0 = →, 90 = ↑).</summary>
    public int Angle { get => _angle; init => _angle = ((value % 360) + 360) % 360; }

    /// <summary>0~100(%).</summary>
    public int Strength { get => _strength; init => _strength = Math.Clamp(value, 0, 100); }

    public EffectBlend Blend { get; init; } = EffectBlend.Normal;

    public EffectDither Dither { get; init; } = EffectDither.None;

    public override string DisplayName => "그라데이션 덮기";
}
