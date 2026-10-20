namespace Gpixel.Core.Model;

/// <summary>피격 번쩍임 한 번의 모양.</summary>
public enum HitFlashShape
{
    /// <summary>번쩍임 앞쪽 절반 동안 꽉 채운 색, 뒤쪽 절반은 원래 그림.</summary>
    Solid,
    /// <summary>번쩍임마다 꽉 찬 색에서 원래 그림으로 점점 엷어짐.</summary>
    Fade,
}

/// <summary>
/// 피격 번쩍임: 보이는 픽셀을 한 색(기본 흰색)으로 <see cref="Count"/>번 번쩍인다(알파는 그대로). 진행 u(0~1)를 횟수로 나눈 한 번 안의 위치 f = frac(u × 횟수):
/// 꽉 채움이면 f &lt; ½일 때 세기만큼 색, 점점 엷어짐이면 세기 × (1 − f). 범위 첫 프레임에서 바로 번쩍이고, 끝(u = 1)은 늘 원래 그림.
/// </summary>
public sealed record HitFlashEffect : AnimatedEffect
{
    public const int MaxCount = 8;

    private readonly int _strength = 100, _count = 2;

    /// <summary>번쩍이는 색(알파는 쓰지 않음, 기본 흰색 — 빨강이면 피해 느낌).</summary>
    public Color32 Color { get; init; } = Color32.White;

    /// <summary>세기 0~100%(100 = 실루엣).</summary>
    public int Strength { get => _strength; init => _strength = Math.Clamp(value, 0, 100); }

    /// <summary>범위 안에서 번쩍이는 횟수 1~<see cref="MaxCount"/>.</summary>
    public int Count { get => _count; init => _count = Math.Clamp(value, 1, MaxCount); }

    public HitFlashShape Shape { get; init; } = HitFlashShape.Solid;

    public override string DisplayName => "피격 번쩍임";

    /// <summary>진행 <paramref name="u"/>에서 색을 섞는 양 0~1(끝 u ≥ 1은 0).</summary>
    public double Amount(double u)
    {
        if (u >= 1 || Strength == 0) return 0;
        double p = Math.Max(0, u) * Count, f = p - Math.Floor(p);
        double k = Shape == HitFlashShape.Solid ? (f < 0.5 - 1e-9 ? 1 : 0) : 1 - f;
        return k * Strength / 100.0;
    }
}
