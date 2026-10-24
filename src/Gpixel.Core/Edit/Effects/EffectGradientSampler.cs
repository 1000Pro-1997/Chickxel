using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>Prepared, allocation-free per-pixel sampler for one effect color slot.</summary>
internal readonly struct EffectGradientSampler
{
    private readonly EffectColorGradient? _gradient;
    private readonly Color32 _solid;
    private readonly double _dx, _dy, _first, _inverseSpan;
    private readonly double _centerX, _centerY, _cos, _sin, _phase, _time;
    private readonly int _sourceX, _sourceY;

    private EffectGradientSampler(EffectColorGradient? gradient, Rectangle bounds, Color32 solid,
        int sourceX, int sourceY, double time)
    {
        _gradient = gradient;
        _solid = solid;
        _sourceX = sourceX;
        _sourceY = sourceY;
        _dx = _dy = _first = _inverseSpan = _centerX = _centerY = _cos = _sin = _phase = _time = 0;
        if (gradient is null) return;
        if (gradient.Shape == EffectGradientShape.Uniform)
        {
            // One color for every pixel: the time position itself, not wrapped, so a single pass ends on the last stop.
            double progress = Progress(gradient, time);
            _time = gradient.Reverse ? 1 - progress : progress;
            return;
        }
        if (bounds.IsEmpty) return;

        // Linear easing without ping-pong keeps the original wrap so existing animated gradients render unchanged.
        _time = !gradient.Animated ? 0
            : gradient.Easing == EffectEasing.Linear && !gradient.PingPong ? Wrap(time * gradient.Cycles)
            : Progress(gradient, time);
        double angle = gradient.Angle * Math.PI / 180;
        _cos = Math.Cos(angle);
        _sin = Math.Sin(angle);
        _centerX = (bounds.Left + bounds.Right) * 0.5;
        _centerY = (bounds.Top + bounds.Bottom) * 0.5;
        if (gradient.Shape == EffectGradientShape.Radial)
        {
            double halfWidth = (bounds.Width - 1) * 0.5, halfHeight = (bounds.Height - 1) * 0.5;
            double radius = Math.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);
            _inverseSpan = radius < 1e-9 ? 0 : 1 / radius;
            _phase = gradient.Angle / 360.0;
            return;
        }
        if (gradient.Shape == EffectGradientShape.Square)
        {
            double halfWidth = (bounds.Width - 1) * 0.5, halfHeight = (bounds.Height - 1) * 0.5;
            double extentX = halfWidth * Math.Abs(_cos) + halfHeight * Math.Abs(_sin);
            double extentY = halfWidth * Math.Abs(_sin) + halfHeight * Math.Abs(_cos);
            double extent = Math.Max(extentX, extentY);
            _inverseSpan = extent < 1e-9 ? 0 : 1 / extent;
            return;
        }
        if (gradient.Shape == EffectGradientShape.Angular) return;

        // Keep the original linear projection and rounding intact for old gradient files.
        _dx = _cos;
        _dy = -_sin; // image y grows downward
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
            bounds, solid, sourceX, sourceY,
            effect is AnimatedEffect animated ? animated.Time : effect.GradientTime);

    public bool HasGradient => _gradient is not null;

    public Color32 Sample(int x, int y)
    {
        if (_gradient is not { } gradient) return _solid;
        double position;
        if (gradient.Shape == EffectGradientShape.Uniform)
            position = _time;
        else if (gradient.Shape == EffectGradientShape.Radial)
        {
            double xOffset = x + 0.5 - _centerX, yOffset = y + 0.5 - _centerY;
            position = Math.Clamp(Math.Sqrt(xOffset * xOffset + yOffset * yOffset) * _inverseSpan, 0, 1);
        }
        else if (gradient.Shape == EffectGradientShape.Square)
        {
            double xOffset = x + 0.5 - _centerX, yOffset = _centerY - y - 0.5;
            position = Math.Clamp(Math.Max(Math.Abs(xOffset * _cos + yOffset * _sin),
                Math.Abs(yOffset * _cos - xOffset * _sin)) * _inverseSpan, 0, 1);
        }
        else if (gradient.Shape == EffectGradientShape.Angular)
        {
            double angle = Math.Atan2(_centerY - y - 0.5, x + 0.5 - _centerX) / (2 * Math.PI);
            position = Wrap(angle - gradient.Angle / 360.0);
        }
        else
        {
            position = _inverseSpan == 0 ? 0.5
                : Math.Clamp(((x + 0.5) * _dx + (y + 0.5) * _dy - _first) * _inverseSpan, 0, 1);
        }
        if (gradient.Shape != EffectGradientShape.Uniform)
        {
            if (gradient.Reverse) position = gradient.Shape == EffectGradientShape.Angular ? Wrap(-position) : 1 - position;
            if (gradient.Animated)
                position = Wrap(position * gradient.Cycles + _phase - _time);
            else if (gradient.Cycles != 1 || _phase != 0)
            {
                double repeated = position * gradient.Cycles + _phase;
                position = repeated == gradient.Cycles && _phase == 0 ? 1 : Wrap(repeated);
            }
        }
        if (gradient.StraightRgba && gradient.Interpolation == GradientInterpolation.Smooth && gradient.Dither == GradientDither.None)
            return SampleStraight(gradient, position);
        return Gradient.Sample(gradient.Stops, gradient.AlphaStops, position,
            gradient.Interpolation, gradient.Dither, x - _sourceX, y - _sourceY);
    }

    private static double Wrap(double value) => value - Math.Floor(value);

    /// <summary>Eased, repeated time position; a single forward pass is the plain clamped time (same as a one-shot animated effect).</summary>
    private static double Progress(EffectColorGradient gradient, double time)
        => AnimatedEffect.Ease(AnimatedEffect.Cycle(time,
            gradient.PingPong ? EffectRepeat.PingPong : gradient.Cycles == 1 ? EffectRepeat.Once : EffectRepeat.Loop,
            gradient.Cycles), gradient.Easing);

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
