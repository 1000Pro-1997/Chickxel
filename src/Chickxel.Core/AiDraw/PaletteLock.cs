using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.AiDraw;

/// <summary>
/// "팔레트 색만 사용"(AI 직접 찍기): AI가 칠하려는 색을 팔레트의 가장 가까운 색으로 바꾼다. 거리는 픽셀 정리 기본(<see cref="PaletteSettings"/> — OKLCh 색상 가중,
/// 명암 계열 우선)과 같다. 팔레트 색은 불투명으로 보고(투명한 팔레트 칸은 뺌), 완전 투명(지우기)만 그대로 두며, 반투명 색은 가장 가까운 불투명 팔레트 색이 된다.
/// 바꾼 색은 <see cref="Changes"/>에 처음 바꾼 순서로 한 번씩 남는다.
/// </summary>
public sealed class PaletteLock
{
    /// <summary>모델에게 알리는 팔레트 색 수 상한.</summary>
    public const int MaxListed = 64;

    private readonly ColorMetric _metric = ColorMetric.For(new PaletteSettings());
    private readonly Color32[] _colors;
    private readonly ColorPoint[] _points;
    private readonly Dictionary<Color32, Color32> _cache = new();
    private readonly List<(Color32 From, Color32 To)> _changes = new();

    public PaletteLock(IEnumerable<Color32> palette)
    {
        _colors = [.. palette.Where(c => c.A > 0).Select(c => c.WithAlpha(255)).Distinct()];
        _points = [.. _colors.Select(_metric.Point)];
    }

    /// <summary>쓸 팔레트 색(불투명, 겹친 색은 한 번).</summary>
    public IReadOnlyList<Color32> Colors => _colors;

    /// <summary>쓸 색이 없음(제한하지 않음).</summary>
    public bool IsEmpty => _colors.Length == 0;

    /// <summary>바꾼 색(원래 색 → 팔레트 색, 처음 바꾼 순서, 한 번씩).</summary>
    public IReadOnlyList<(Color32 From, Color32 To)> Changes => _changes;

    /// <summary>칠할 색: 완전 투명이면 그대로, 팔레트가 비었으면 그대로, 아니면 가장 가까운 팔레트 색(불투명).</summary>
    public Color32 Map(Color32 c)
    {
        if (c.A == 0 || IsEmpty) return c;
        if (_cache.TryGetValue(c, out var mapped)) return mapped;
        var opaque = c.WithAlpha(255);
        mapped = _colors[_metric.Nearest(_metric.Point(opaque), _points)];
        _cache[c] = mapped;
        if (mapped != c) _changes.Add((c, mapped));
        return mapped;
    }

    /// <summary>바꾼 색 요약: "#RRGGBB→#RRGGBB, …"(반투명이던 색은 #RRGGBBAA). 바꾼 것이 없으면 빈 글.</summary>
    public string DescribeChanges(int max = 12)
    {
        var parts = _changes.Take(max).Select(c => $"{c.From.ToHex()}→{c.To.ToHex()}").ToList();
        if (_changes.Count > max) parts.Add($"+{_changes.Count - max}");
        return string.Join(", ", parts);
    }

    /// <summary>모델에게 알릴 팔레트 색 목록("#RRGGBB, …", 많아야 <see cref="MaxListed"/>개).</summary>
    public string ListColors() => string.Join(", ", _colors.Take(MaxListed).Select(c => c.ToHex()));
}
