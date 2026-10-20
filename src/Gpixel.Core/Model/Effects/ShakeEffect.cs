namespace Gpixel.Core.Model;

/// <summary>흔들기·둥실둥실의 움직임 축.</summary>
public enum EffectAxis
{
    /// <summary>가로(좌우).</summary>
    Horizontal,
    /// <summary>세로(위아래).</summary>
    Vertical,
    /// <summary>가로와 세로 둘 다.</summary>
    Both,
}

/// <summary>
/// 흔들기: 그림 전체를 정수 픽셀만큼 옮겨 떤다(맞았을 때·지진). 진행 u, 세기 A, 줄어듦이면 크기 e = 1 − u(아니면 1).
/// 규칙적이면 가로 = A·e·sin(2π·횟수·u), 세로 = A·e·sin(4π·횟수·u)(둘 다면 8자), 무작위면 한 번에 두 번씩 바뀌는 (시드, 칸 번호) 무작위 ±A·e.
/// 반올림한 정수, |옮김| ≤ A, 시작(u = 0)·끝(u = 1)은 제자리. 여백은 그 축으로 세기만큼.
/// </summary>
public sealed record ShakeEffect : AnimatedEffect
{
    public const int MaxAmount = 32, MaxCount = 16, MaxSeed = 9999;

    private readonly int _amount = 2, _count = 4, _seed = 1;

    public EffectAxis Axis { get; init; } = EffectAxis.Horizontal;

    /// <summary>세기: 가장 많이 옮기는 px 1~<see cref="MaxAmount"/>.</summary>
    public int Amount { get => _amount; init => _amount = Math.Clamp(value, 1, MaxAmount); }

    /// <summary>범위 안에서 흔드는 횟수 1~<see cref="MaxCount"/>.</summary>
    public int Count { get => _count; init => _count = Math.Clamp(value, 1, MaxCount); }

    /// <summary>줄어듦: 끝으로 갈수록 약하게.</summary>
    public bool Decay { get; init; } = true;

    /// <summary>true = 규칙적(사인), false = 무작위(시드).</summary>
    public bool Regular { get; init; }

    /// <summary>무작위 시드 0~<see cref="MaxSeed"/>.</summary>
    public int Seed { get => _seed; init => _seed = Math.Clamp(value, 0, MaxSeed); }

    public override string DisplayName => "흔들기";

    /// <summary>진행 <paramref name="u"/>의 옮김(px, 정수).</summary>
    public (int X, int Y) Offset(double u)
    {
        if (u <= 0 || u >= 1) return (0, 0);
        double a = Amount * (Decay ? 1 - u : 1);
        double x, y;
        if (Regular)
        {
            x = Math.Sin(2 * Math.PI * Count * u);
            y = Axis == EffectAxis.Both ? Math.Sin(4 * Math.PI * Count * u) : x;
        }
        else
        {
            int k = (int)Math.Floor(u * Count * 2);
            x = Edit.LayerEffects.Random(Seed, k, 0, 7) * 2 - 1;
            y = Axis == EffectAxis.Both ? Edit.LayerEffects.Random(Seed, k, 1, 7) * 2 - 1 : x;
        }
        int R(double v) => Math.Clamp((int)Math.Round(v * a, MidpointRounding.AwayFromZero), -Amount, Amount);
        return Axis switch
        {
            EffectAxis.Horizontal => (R(x), 0),
            EffectAxis.Vertical => (0, R(y)),
            _ => (R(x), R(y)),
        };
    }
}
