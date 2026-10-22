namespace Gpixel.Core.Model;

/// <summary>
/// 둥실둥실: 그림 전체를 사인으로 부드럽게 오르내린다(떠 있는 아이템·유령). 옮김 = round(높이 × sin(2π × 횟수 × u))px(위아래면 위로 먼저, 좌우면 오른쪽 먼저),
/// u = 진행. 시작(u = 0)과 끝(u = 1)이 같아서 범위를 되풀이해도 끊김 없이 이어진다. 여백은 그 축으로 높이만큼.
/// </summary>
public sealed record BobEffect : AnimatedEffect
{
    public const int MaxHeight = 32, MaxCycles = 8;

    private readonly int _height = 2, _cycles = 1;
    private readonly EffectAxis _axis = EffectAxis.Vertical;

    /// <summary>높이: 가장 많이 옮기는 px 1~<see cref="MaxHeight"/>(가운데에서 위·아래로 각각).</summary>
    public int Height { get => _height; init => _height = Math.Clamp(value, 1, MaxHeight); }

    /// <summary>범위 안에서 오르내리는 횟수 1~<see cref="MaxCycles"/>.</summary>
    public int Cycles { get => _cycles; init => _cycles = Math.Clamp(value, 1, MaxCycles); }

    /// <summary>위아래(<see cref="EffectAxis.Vertical"/>) 또는 좌우(<see cref="EffectAxis.Horizontal"/>).</summary>
    public EffectAxis Axis { get => _axis; init => _axis = value == EffectAxis.Horizontal ? EffectAxis.Horizontal : EffectAxis.Vertical; }

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_Bob;

    /// <summary>진행 <paramref name="u"/>의 옮김(px, 정수, |옮김| ≤ 높이).</summary>
    public (int X, int Y) Offset(double u)
    {
        double s = u <= 0 || u >= 1 ? 0 : Math.Sin(2 * Math.PI * Cycles * u);
        int d = Math.Clamp((int)Math.Round(Height * s, MidpointRounding.AwayFromZero), -Height, Height);
        return Axis == EffectAxis.Horizontal ? (d, 0) : (0, -d);
    }
}
