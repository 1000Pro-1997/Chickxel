namespace Chickxel.Core.Model;

/// <summary>나타나기·사라지기 방식.</summary>
public enum AppearStyle
{
    /// <summary>투명도(알파 × 보임).</summary>
    Fade,
    /// <summary>4×4 Bayer 순서 디더로 픽셀이 차례로 켜짐/꺼짐.</summary>
    Dither,
    /// <summary>한쪽에서 쓸기(경계에 디더 가장자리 0~2px).</summary>
    Wipe,
    /// <summary>시드로 정한 무작위 픽셀 흩어짐.</summary>
    Scatter,
}

/// <summary>쓸기가 시작되는 쪽.</summary>
public enum WipeDirection
{
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>
/// 나타나기 / 사라지기: 보임 v = 진행 u(나타나기) 또는 1 − u(사라지기). 나타나기는 범위 첫 프레임이 비어 있고 마지막 프레임이 그림 그대로, 사라지기는 그 반대.
/// 투명도 = 알파 × v, 디더 = Bayer 4×4 문턱 (b + ½)/16 &lt; v인 픽셀만, 흩어짐 = (시드, 픽셀 자리) 무작위 r &lt; v인 픽셀만,
/// 쓸기 = 보이는 그림 범위의 <see cref="From"/> 쪽에서 선이 지나가며(사라지기는 그 쪽부터 지워짐) 선 바로 앞 <see cref="Edge"/>px는 바둑판 디더.
/// 무늬·무작위는 원본 왼쪽 위 기준 자리라 여백과 관계없이 같다.
/// </summary>
public sealed record AppearEffect : AnimatedEffect
{
    public const int MaxEdge = 2, MaxSeed = 9999;

    private readonly int _edge = 1, _seed = 1;

    /// <summary>false = 나타나기, true = 사라지기.</summary>
    public bool Disappear { get; init; }

    public AppearStyle Style { get; init; } = AppearStyle.Dither;

    /// <summary>쓸기가 시작되는 쪽.</summary>
    public WipeDirection From { get; init; } = WipeDirection.Top;

    /// <summary>쓸기 경계의 디더 가장자리 0~<see cref="MaxEdge"/>px.</summary>
    public int Edge { get => _edge; init => _edge = Math.Clamp(value, 0, MaxEdge); }

    /// <summary>흩어짐 시드 0~<see cref="MaxSeed"/>.</summary>
    public int Seed { get => _seed; init => _seed = Math.Clamp(value, 0, MaxSeed); }

    public override string DisplayName => Disappear ? Chickxel.Core.Localization.DomainStrings.Effect_Disappear : Chickxel.Core.Localization.DomainStrings.Effect_Appear;

    /// <summary>진행 <paramref name="u"/>의 보임 0~1.</summary>
    public double Visibility(double u) => Math.Clamp(Disappear ? 1 - u : u, 0, 1);
}
