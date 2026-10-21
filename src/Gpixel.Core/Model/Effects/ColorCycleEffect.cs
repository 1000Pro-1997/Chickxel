namespace Gpixel.Core.Model;

/// <summary>
/// 색 순환: 보이는 픽셀 중 RGB가 목록의 i번째 색과 같은 픽셀을 (i + k) mod n번째 색으로 바꾼다(알파는 그대로, 목록에 없는 색은 그대로).
/// k = ⌊진행 × <see cref="Steps"/>⌋(거꾸로면 −k). 단계 수가 색 개수의 배수면 범위 끝이 시작과 같아 되풀이해도 이어진다(물·불·네온).
/// 같은 색이 목록에 두 번 있으면 앞의 것만 찾는다. 값 비교는 색 목록 내용으로 한다.
/// 색 그라데이션 슬롯은 각 위치에서 샘플한 RGB로 목록의 원본 색을 찾고, 순환한 목표색을
/// 같은 위치에서 샘플한다(목표 그라데이션의 알파는 교체 세기, 원본 픽셀 알파는 유지).
/// </summary>
public sealed record ColorCycleEffect : AnimatedEffect
{
    public const int MinColors = 2, MaxColors = 8, MaxSteps = 64;

    private readonly IReadOnlyList<Color32> _colors = [new(40, 80, 200), new(70, 150, 240), new(170, 230, 255)];
    private readonly int _steps = 3;

    /// <summary>순환할 색(<see cref="MinColors"/>~<see cref="MaxColors"/>개, 넘치면 앞에서부터, 알파는 무시).</summary>
    public IReadOnlyList<Color32> Colors
    {
        get => _colors;
        init => _colors = value is null ? [] : value.Take(MaxColors).Select(c => c.WithAlpha(255)).ToArray();
    }

    /// <summary>범위 동안 한 칸씩 넘어가는 횟수 1~<see cref="MaxSteps"/>.</summary>
    public int Steps { get => _steps; init => _steps = Math.Clamp(value, 1, MaxSteps); }

    /// <summary>거꾸로(i → i − k).</summary>
    public bool Reverse { get; init; }

    public override string DisplayName => "색 순환";

    /// <summary>진행 <paramref name="u"/>에서 넘어간 칸 수 k(0 ≤ k &lt; 색 개수).</summary>
    public int Shift(double u)
    {
        int n = _colors.Count;
        if (n < MinColors) return 0;
        int k = (int)Math.Floor(Math.Clamp(u, 0, 1) * Steps + 1e-9) % n;
        return Reverse ? (n - k) % n : k;
    }

    public bool Equals(ColorCycleEffect? other)
        => other is not null && base.Equals(other) && _steps == other._steps && Reverse == other.Reverse && _colors.SequenceEqual(other._colors);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(base.GetHashCode());
        h.Add(_steps);
        h.Add(Reverse);
        foreach (var c in _colors) h.Add(c);
        return h.ToHashCode();
    }
}
