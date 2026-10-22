using Gpixel.Core.Edit;

namespace Gpixel.Core.Model;

/// <summary>
/// 효과의 색·불투명도 정지점. 공간 그라데이션은 <see cref="Angle"/> 방향의 픽셀 위치로,
/// 시간 그라데이션은 효과 범위의 진행으로 샘플링한다. 정지점은 지정할 때 한 번 정렬·제한된다.
/// </summary>
public sealed record EffectColorGradient
{
    private readonly IReadOnlyList<GradientStop> _stops =
        Array.AsReadOnly(new[] { new GradientStop(0, Color32.White), new GradientStop(1, Color32.White) });
    private readonly IReadOnlyList<GradientAlphaStop> _alphaStops = [];
    private readonly int _angle;

    public IReadOnlyList<GradientStop> Stops
    {
        get => _stops;
        init => _stops = value is { Count: > 0 } ? Gradient.Sorted(value).AsReadOnly()
            : Array.AsReadOnly(new[] { new GradientStop(0, Color32.White), new GradientStop(1, Color32.White) });
    }

    /// <summary>An empty list leaves the opacity of color stops unchanged.</summary>
    public IReadOnlyList<GradientAlphaStop> AlphaStops
    {
        get => _alphaStops;
        init => _alphaStops = Gradient.SortedAlpha(value ?? []).AsReadOnly();
    }

    /// <summary>Direction in degrees: 0 goes right, 90 goes up.</summary>
    public int Angle { get => _angle; init => _angle = ((value % 360) + 360) % 360; }

    public GradientInterpolation Interpolation { get; init; } = GradientInterpolation.Smooth;
    public GradientDither Dither { get; init; } = GradientDither.None;

    /// <summary>
    /// 예전 A/B 공간 그라데이션 호환: RGB와 알파를 각각 직선 보간하고 .5는 위로 반올림한다.
    /// 새 그라데이션은 false이며, 예전 파일을 불러와 다시 저장할 때만 유지된다.
    /// </summary>
    public bool StraightRgba { get; init; }

    public bool Equals(EffectColorGradient? other)
        => other is not null && _angle == other._angle &&
           Interpolation == other.Interpolation && Dither == other.Dither && StraightRgba == other.StraightRgba &&
           _stops.SequenceEqual(other._stops) && _alphaStops.SequenceEqual(other._alphaStops);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_angle);
        hash.Add(Interpolation);
        hash.Add(Dither);
        hash.Add(StraightRgba);
        foreach (var stop in _stops) hash.Add(stop);
        foreach (var stop in _alphaStops) hash.Add(stop);
        return hash.ToHashCode();
    }
}
