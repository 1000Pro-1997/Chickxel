using Gpixel.Core.Edit;

namespace Gpixel.Core.Model;

/// <summary>Spatial gradient geometry for effect color slots.</summary>
public enum EffectGradientShape { Linear, Radial, Square, Angular }

/// <summary>
/// Spatial color stops for an effect. Shape selects the geometry inside the opaque bounds;
/// animated gradients move through full cycles over the effect's frame range.
/// Stops are sorted and constrained once when assigned.
/// </summary>

public sealed record EffectColorGradient
{
    private readonly IReadOnlyList<GradientStop> _stops =
        Array.AsReadOnly(new[] { new GradientStop(0, Color32.White), new GradientStop(1, Color32.White) });
    private readonly IReadOnlyList<GradientAlphaStop> _alphaStops = [];
    private readonly int _angle;
    private readonly int _cycles = 1;

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

    /// <summary>Degrees, 0 right and 90 up: linear direction, square orientation, radial color phase, or angular starting offset.</summary>
    public int Angle { get => _angle; init => _angle = ((value % 360) + 360) % 360; }

    public EffectGradientShape Shape { get; init; }
    public bool Animated { get; init; }
    /// <summary>Reverse spatial direction (radial and square inward, angular clockwise).</summary>
    public bool Reverse { get; init; }
    /// <summary>Number of spatial repeats and, if animated, turns over the frame range (1–16).</summary>
    public int Cycles { get => _cycles; init => _cycles = Math.Clamp(value, 1, 16); }

    public GradientInterpolation Interpolation { get; init; } = GradientInterpolation.Smooth;
    public GradientDither Dither { get; init; } = GradientDither.None;

    /// <summary>
    /// 예전 A/B 공간 그라데이션 호환: RGB와 알파를 각각 직선 보간하고 .5는 위로 반올림한다.
    /// 새 그라데이션은 false이며, 예전 파일을 불러와 다시 저장할 때만 유지된다.
    /// </summary>
    public bool StraightRgba { get; init; }

    public bool Equals(EffectColorGradient? other)
        => other is not null && _angle == other._angle && Shape == other.Shape &&
           Animated == other.Animated && Reverse == other.Reverse && Cycles == other.Cycles &&
           Interpolation == other.Interpolation && Dither == other.Dither && StraightRgba == other.StraightRgba &&
           _stops.SequenceEqual(other._stops) && _alphaStops.SequenceEqual(other._alphaStops);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_angle);
        hash.Add(Shape);
        hash.Add(Animated);
        hash.Add(Reverse);
        hash.Add(Cycles);
        hash.Add(Interpolation);
        hash.Add(Dither);
        hash.Add(StraightRgba);
        foreach (var stop in _stops) hash.Add(stop);
        foreach (var stop in _alphaStops) hash.Add(stop);
        return hash.ToHashCode();
    }
}
