namespace Gpixel.Core.Model;

/// <summary>
/// 충격파 고리: 중심 픽셀에서 반지름 r = round(최대 반지름 × 진행)인 고리가 퍼진다. 두께 1px은 중점 원 그리기(빈틈·겹침 없는 픽셀 원),
/// 두께 T &gt; 1은 픽셀 가운데 거리 d가 r − T + ½ &lt; d ≤ r + ½인 픽셀. 색 알파 × (사라짐이면 1 − 반복을 넣은 시간 — 빠르기 곡선 없이). r = 0(범위 첫 프레임)이면 그리지 않는다.
/// 뒤에 깔기면 그림 아래로(보이는 픽셀이 가림), 아니면 그림 위에. 여백은 사방으로 최대 반지름만큼.
/// </summary>
public sealed record ShockwaveEffect : AnimatedEffect
{
    public const int MaxRadius = 128, MaxThickness = 8;

    private readonly int _radius = 16, _thickness = 1, _cx, _cy;

    public ShockwaveEffect() => Easing = EffectEasing.EaseOut;

    public ExplodeCenter Center { get; init; } = ExplodeCenter.Middle;

    /// <summary>직접 중심: 원본(셀·그룹이면 캔버스) 왼쪽 위 기준 픽셀.</summary>
    public int CenterX { get => _cx; init => _cx = Math.Clamp(value, -PixelImage.MaxSize, PixelImage.MaxSize); }
    public int CenterY { get => _cy; init => _cy = Math.Clamp(value, -PixelImage.MaxSize, PixelImage.MaxSize); }

    /// <summary>끝(진행 1)의 반지름 1~<see cref="MaxRadius"/>px.</summary>
    public int Radius { get => _radius; init => _radius = Math.Clamp(value, 1, MaxRadius); }

    /// <summary>고리 두께 1~<see cref="MaxThickness"/>px(안쪽으로).</summary>
    public int Thickness { get => _thickness; init => _thickness = Math.Clamp(value, 1, MaxThickness); }

    /// <summary>고리 색(알파 = 진하기).</summary>
    public Color32 Color { get; init; } = Color32.White;

    /// <summary>사라짐: 퍼지면서 옅어져 끝에서 투명.</summary>
    public bool Fade { get; init; } = true;

    /// <summary>true = 그림 뒤에 깔기, false = 그림 위에.</summary>
    public bool Behind { get; init; }

    public override string DisplayName => "충격파 고리";

    /// <summary>진행 <paramref name="u"/>의 반지름(px).</summary>
    public int RadiusAt(double u) => (int)Math.Round(Radius * Math.Clamp(u, 0, 1), MidpointRounding.AwayFromZero);
}
