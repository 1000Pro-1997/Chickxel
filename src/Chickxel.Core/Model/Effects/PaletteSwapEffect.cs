namespace Chickxel.Core.Model;

/// <summary>색 바꾸기 한 쌍: <see cref="From"/>(RGB로 찾음) → <see cref="To"/>.</summary>
public readonly record struct ColorSwap(Color32 From, Color32 To);

/// <summary>
/// 색 바꾸기(팔레트 스왑): 보이는 픽셀의 RGB가 쌍의 <see cref="ColorSwap.From"/>과 같으면(채널마다 차이 ≤ <see cref="Tolerance"/>) RGB를
/// <see cref="ColorSwap.To"/>로 바꾼다. 알파 = 픽셀 알파 × To 알파 / 255(To 알파 0 = 그 색 지우기). 위의 쌍부터 보고 처음 맞는 쌍 하나만 쓴다
/// (바꾼 색을 다시 바꾸지 않음 — A↔B 맞바꾸기도 됨). 값 비교는 쌍 목록 내용으로 한다.
/// 소스 색의 그라데이션은 픽셀 위치에서 샘플한 RGB(알파 0이면 일치하지 않음)로 비교하고,
/// 대상 색의 그라데이션은 같은 위치에서 샘플한 색·알파로 바꾼다.
/// </summary>
public sealed record PaletteSwapEffect : LayerEffect
{
    public const int MaxTolerance = 255;

    private readonly IReadOnlyList<ColorSwap> _pairs = [];
    private readonly int _tolerance;

    /// <summary>바꿀 색 쌍(위에서부터 먼저 맞는 쌍).</summary>
    public IReadOnlyList<ColorSwap> Pairs { get => _pairs; init => _pairs = value?.ToArray() ?? []; }

    /// <summary>채널(R·G·B)마다 허용하는 차이 0~<see cref="MaxTolerance"/>(0 = 정확히 같은 색만).</summary>
    public int Tolerance { get => _tolerance; init => _tolerance = Math.Clamp(value, 0, MaxTolerance); }

    public override string DisplayName => Chickxel.Core.Localization.DomainStrings.Effect_PaletteSwap;

    public bool Equals(PaletteSwapEffect? other)
        => other is not null && base.Equals(other) && _tolerance == other._tolerance && _pairs.SequenceEqual(other._pairs);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(base.GetHashCode());
        h.Add(_tolerance);
        foreach (var p in _pairs) h.Add(p);
        return h.ToHashCode();
    }
}
