using Gpixel.Core.Edit;

namespace Gpixel.Core.Model;

/// <summary>
/// A spatial color for one effect color slot. Color and opacity stops are independent;
/// positions are sorted and clamped once when assigned, never in the pixel loop.
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

    public bool Equals(EffectColorGradient? other)
        => other is not null && _angle == other._angle &&
           Interpolation == other.Interpolation && Dither == other.Dither &&
           _stops.SequenceEqual(other._stops) && _alphaStops.SequenceEqual(other._alphaStops);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_angle);
        hash.Add(Interpolation);
        hash.Add(Dither);
        foreach (var stop in _stops) hash.Add(stop);
        foreach (var stop in _alphaStops) hash.Add(stop);
        return hash.ToHashCode();
    }
}
