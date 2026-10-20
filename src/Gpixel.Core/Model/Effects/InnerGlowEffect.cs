namespace Gpixel.Core.Model;

/// <summary>
/// 안쪽 빛: 그림 가장자리(투명한 이웃·이미지 끝과 닿은 픽셀)에서 안쪽으로 <see cref="Size"/>px까지 한 색을 섞는다(알파는 그대로, 색의 알파는 쓰지 않음).
/// 가장자리 1px이 <see cref="Strength"/>%, <see cref="Fade"/>면 안쪽으로 갈수록 한 단계씩 약해진다(계단식, 번짐 없음).
/// 거리는 8방향(모서리 포함)으로 센다.
/// </summary>
public sealed record InnerGlowEffect : LayerEffect
{
    public const int MaxSize = 8;

    private readonly int _size = 2, _strength = 70;

    public Color32 Color { get; init; } = new(255, 240, 160);

    /// <summary>안쪽으로 1~<see cref="MaxSize"/>px.</summary>
    public int Size { get => _size; init => _size = Math.Clamp(value, 1, MaxSize); }

    /// <summary>0~100(%).</summary>
    public int Strength { get => _strength; init => _strength = Math.Clamp(value, 0, 100); }

    /// <summary>true = 안쪽으로 갈수록 약하게, false = 두께 전체 같은 세기.</summary>
    public bool Fade { get; init; } = true;

    public EffectBlend Blend { get; init; } = EffectBlend.Screen;

    public override string DisplayName => "안쪽 빛";
}
