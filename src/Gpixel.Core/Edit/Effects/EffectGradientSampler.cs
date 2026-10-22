using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>Prepared, allocation-free per-pixel sampler for one effect color slot.</summary>
internal readonly struct EffectGradientSampler
{
    private readonly EffectColorGradient? _gradient;
    private readonly Color32 _solid;
    private readonly double _dx, _dy, _first, _inverseSpan;
    private readonly int _sourceX, _sourceY;

    private EffectGradientSampler(EffectColorGradient? gradient, Rectangle bounds, Color32 solid, int sourceX, int sourceY)
    {
        _gradient = gradient;
        _solid = solid;
        _sourceX = sourceX;
        _sourceY = sourceY;
        _dx = _dy = _first = _inverseSpan = 0;
        if (gradient is null || bounds.IsEmpty) return;

        double angle = gradient.Angle * Math.PI / 180;
        _dx = Math.Cos(angle);
        _dy = -Math.Sin(angle); // image y grows downward
        double l = bounds.Left + 0.5, r = bounds.Right - 0.5;
        double t = bounds.Top + 0.5, b = bounds.Bottom - 0.5;
        double p0 = l * _dx + t * _dy, p1 = r * _dx + t * _dy;
        double p2 = l * _dx + b * _dy, p3 = r * _dx + b * _dy;
        _first = Math.Min(Math.Min(p0, p1), Math.Min(p2, p3));
        double span = Math.Max(Math.Max(p0, p1), Math.Max(p2, p3)) - _first;
        _inverseSpan = span < 1e-9 ? 0 : 1 / span;
    }

    public static EffectGradientSampler For(LayerEffect effect, string slot, Rectangle bounds, Color32 solid,
        int sourceX = 0, int sourceY = 0)
        => new(effect.ColorGradients.TryGetValue(slot, out var gradient) ? gradient : null,
            bounds, solid, sourceX, sourceY);

    public bool HasGradient => _gradient is not null;

    public Color32 Sample(int x, int y)
    {
        if (_gradient is not { } gradient) return _solid;
        double position = _inverseSpan == 0 ? 0.5
            : Math.Clamp(((x + 0.5) * _dx + (y + 0.5) * _dy - _first) * _inverseSpan, 0, 1);
        if (gradient.StraightRgba && gradient.Interpolation == GradientInterpolation.Smooth && gradient.Dither == GradientDither.None)
            return SampleStraight(gradient, position);
        return Gradient.Sample(gradient.Stops, gradient.AlphaStops, position,
            gradient.Interpolation, gradient.Dither, x - _sourceX, y - _sourceY);
    }

    private static Color32 SampleStraight(EffectColorGradient gradient, double position)
    {
        var stops = gradient.Stops;
        Color32 color;
        if (position <= stops[0].Position) color = stops[0].Color;
        else if (position >= stops[^1].Position) color = stops[^1].Color;
        else
        {
            int i = 0;
            while (i + 2 < stops.Count && position >= stops[i + 1].Position) i++;
            var a = stops[i];
            var b = stops[i + 1];
            double span = b.Position - a.Position;
            double u = span <= 0 ? 1 : (position - a.Position) / span;
            byte Lerp(byte from, byte to)
                => (byte)Math.Round(from + (to - from) * u, MidpointRounding.AwayFromZero);
            color = new Color32(Lerp(a.Color.R, b.Color.R), Lerp(a.Color.G, b.Color.G),
                Lerp(a.Color.B, b.Color.B), Lerp(a.Color.A, b.Color.A));
        }
        int alpha = (color.A * Gradient.AlphaAt(gradient.AlphaStops, position) + 127) / 255;
        return alpha == 0 ? Color32.Transparent : color.WithAlpha((byte)alpha);
    }
}
