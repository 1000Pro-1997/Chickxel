namespace Chickxel.Core.Model;

/// <summary>색조 보정 방식.</summary>
public enum ColorAdjustMode
{
    /// <summary>흑백(밝기 = 0.299R + 0.587G + 0.114B) 쪽으로 <see cref="ColorAdjustEffect.Amount"/>%.</summary>
    Grayscale,
    /// <summary>세피아(누런 옛 사진) 쪽으로 <see cref="ColorAdjustEffect.Amount"/>%.</summary>
    Sepia,
    /// <summary>색상(HSV의 H)을 <see cref="ColorAdjustEffect.Hue"/>도 돌림.</summary>
    HueShift,
    /// <summary>밝기 <see cref="ColorAdjustEffect.Brightness"/>, 대비 <see cref="ColorAdjustEffect.Contrast"/>.</summary>
    BrightnessContrast,
    /// <summary>채도 <see cref="ColorAdjustEffect.Saturation"/>(−100 = 흑백, +100 = 두 배).</summary>
    Saturation,
}

/// <summary>
/// 색조 보정: 보이는 픽셀의 RGB를 <see cref="Mode"/>에 따라 바꾼다(알파는 그대로). 방식마다 쓰는 값만 쓰고 나머지 값은 저장만 된다(방식을 바꿔도 남음).
/// </summary>
public sealed record ColorAdjustEffect : LayerEffect
{
    public const int MaxHue = 180;

    private readonly int _amount = 100, _hue = 30, _brightness, _contrast, _saturation = 50;

    public ColorAdjustMode Mode { get; init; } = ColorAdjustMode.Grayscale;

    /// <summary>흑백·세피아 세기 0~100(%).</summary>
    public int Amount { get => _amount; init => _amount = Math.Clamp(value, 0, 100); }

    /// <summary>색상 돌리기 −<see cref="MaxHue"/>~<see cref="MaxHue"/>(도).</summary>
    public int Hue { get => _hue; init => _hue = Math.Clamp(value, -MaxHue, MaxHue); }

    /// <summary>밝기 −100~100(%, 255의 몇 %를 더함).</summary>
    public int Brightness { get => _brightness; init => _brightness = Math.Clamp(value, -100, 100); }

    /// <summary>대비 −100~100(%, 가운데 회색 128 기준 배율 1 + 대비/100).</summary>
    public int Contrast { get => _contrast; init => _contrast = Math.Clamp(value, -100, 100); }

    /// <summary>채도 −100~100(%).</summary>
    public int Saturation { get => _saturation; init => _saturation = Math.Clamp(value, -100, 100); }

    public override string DisplayName => Chickxel.Core.Localization.DomainStrings.Effect_ColorAdjust;
}
