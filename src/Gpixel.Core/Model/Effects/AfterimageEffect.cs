namespace Gpixel.Core.Model;

/// <summary>
/// 잔상: 같은 레이어의 앞 프레임 그림(셀 원본 — 효과 없이)을 지금 그림 뒤에 점점 옅게 깐다. k번째 잔상(k = 1~개수) = 프레임 (지금 − k × 간격)의 원본,
/// 불투명도 = <see cref="Opacity"/> × (개수 − k + 1) / 개수. 효과의 프레임 범위 안 프레임만 쓴다(범위 첫 프레임은 잔상 없음 — 어느 프레임부터 그려도 같은 결과).
/// 색 입히기를 켜면 잔상을 그 색 쪽으로 물들인다(색 알파 = 세기).
/// </summary>
public sealed record AfterimageEffect : AnimatedEffect
{
    public const int MaxCount = 8, MaxInterval = 8;

    private readonly int _count = 3, _interval = 1, _opacity = 60;

    /// <summary>잔상 개수 1~<see cref="MaxCount"/>.</summary>
    public int Count { get => _count; init => _count = Math.Clamp(value, 1, MaxCount); }

    /// <summary>잔상 사이 프레임 수 1~<see cref="MaxInterval"/>.</summary>
    public int Interval { get => _interval; init => _interval = Math.Clamp(value, 1, MaxInterval); }

    /// <summary>가장 가까운 잔상의 불투명도 1~100%(멀수록 같은 간격으로 옅어짐).</summary>
    public int Opacity { get => _opacity; init => _opacity = Math.Clamp(value, 1, 100); }

    /// <summary>색 입히기 켜기.</summary>
    public bool Tint { get; init; }

    /// <summary>잔상에 입힐 색(알파 = 세기).</summary>
    public Color32 TintColor { get; init; } = new(80, 150, 255);

    public override string DisplayName => "잔상";

    /// <summary>k번째 잔상(1부터)의 불투명도 0~255.</summary>
    public byte GhostOpacity(int k) => (byte)Math.Clamp((Opacity * 255 * (Count - k + 1) / Count + 50) / 100, 0, 255);
}
