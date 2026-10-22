namespace Gpixel.Core.Model;

/// <summary>
/// 회전: 범위 동안 기준점을 중심으로 각도 θ = ±<see cref="Angle"/> × 진행만큼 돌린다(시계 방향 = 화면에서 오른쪽으로 도는 쪽).
/// 픽셀이 깨지지 않게 RotSprite 방식: 그림을 Scale2x(EPX)로 여러 번(작은 그림 8배, 큰 그림은 덜) 키운 뒤 최근접으로 돌려 원래 크기 픽셀 가운데 점을 집는다.
/// 90°의 배수는 키우지 않고 그대로 옮기며(손실 없음), 360°의 배수(시작·한 바퀴 끝)는 원래 그림 그대로. 여백은 돌린 그림이 닿을 수 있는 만큼.
/// </summary>
public sealed record RotateEffect : AnimatedEffect
{
    public const int MaxAngle = 1440;

    private readonly int _angle = 360;

    /// <summary>범위 동안 도는 전체 각도 0~<see cref="MaxAngle"/>°.</summary>
    public int Angle { get => _angle; init => _angle = Math.Clamp(value, 0, MaxAngle); }

    /// <summary>기준점(이 점은 움직이지 않음).</summary>
    public SquashAnchor Anchor { get; init; } = SquashAnchor.Middle;

    /// <summary>true = 시계 방향, false = 반시계 방향.</summary>
    public bool Clockwise { get; init; } = true;

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_Rotate;

    /// <summary>진행 <paramref name="u"/>의 회전 각(도, 시계 방향 +).</summary>
    public double DegreesAt(double u) => (Clockwise ? 1 : -1) * Angle * Math.Clamp(u, 0, 1);
}
