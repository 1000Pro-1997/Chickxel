using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// Left-to-right preview of a gradient preset over a checkerboard. The image is rendered once per device-pixel size
/// (colors, opacity, interpolation, color space and Bayer dithering) and drawn without smoothing so it stays crisp.
/// </summary>
public sealed class GradientSwatch : Control
{
    private static readonly IPen OutlinePen = AppTheme.Pen(AppTheme.BorderColor);

    private GradientPreset? _preset;
    private WriteableBitmap? _bitmap;
    private PixelSize _bitmapSize;

    public GradientPreset? Preset
    {
        get => _preset;
        set
        {
            if (ReferenceEquals(_preset, value)) return;
            _preset = value;
            DropBitmap();
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DropBitmap();
    }

    private void DropBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public override void Render(DrawingContext g)
    {
        var rect = new Rect(Bounds.Size);
        if (rect.Width < 1 || rect.Height < 1) return;
        AppTheme.DrawChecker(g, rect, Math.Clamp(Math.Round(rect.Height / 3), 2, 6));
        if (_preset is { Stops.Count: > 0 } preset)
        {
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(rect.Width * scale)), Math.Max(1, (int)Math.Ceiling(rect.Height * scale)));
            if (_bitmap is null || _bitmapSize != size)
            {
                DropBitmap();
                _bitmap = Build(preset, size, scale);
                _bitmapSize = size;
            }
            using (ImageUtil.Pixelated(g))
                g.DrawImage(_bitmap, new Rect(0, 0, size.Width, size.Height), rect);
        }
        g.DrawRectangle(null, OutlinePen, rect.Deflate(0.5));
    }

    private static unsafe WriteableBitmap Build(GradientPreset preset, PixelSize size, double scale)
    {
        var stops = Gradient.Sorted(preset.Stops);
        var alphaStops = Gradient.SortedAlpha(preset.AlphaStops);
        var dither = preset.Interpolation == GradientInterpolation.Smooth ? preset.Dither : GradientDither.None;
        // One dither cell spans about two logical pixels so the ordered pattern stays visible at any scaling.
        int cell = Math.Max(1, (int)Math.Round(2 * scale));
        int period = dither switch
        {
            GradientDither.Bayer2 => 2 * cell,
            GradientDither.Bayer4 => 4 * cell,
            _ => 1,
        };
        int width = size.Width, height = size.Height;
        period = Math.Min(period, height);
        var bitmap = ImageUtil.NewBitmap(width, height);
        using var frame = bitmap.Lock();
        Span<Color32> Row(int y) => new((byte*)frame.Address + (long)y * frame.RowBytes, width);
        // Rows repeat with the dither period, so only one period is sampled.
        for (int y = 0; y < period; y++)
        {
            var row = Row(y);
            for (int x = 0; x < width; x++)
            {
                double t = width <= 1 ? 0 : x / (double)(width - 1);
                row[x] = Gradient.Sample(stops, alphaStops, t, preset.Interpolation, dither, x / cell, y / cell, preset.ColorSpace);
            }
        }
        for (int y = period; y < height; y++) Row(y % period).CopyTo(Row(y));
        return bitmap;
    }
}
