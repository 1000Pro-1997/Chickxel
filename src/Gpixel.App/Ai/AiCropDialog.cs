using System.Globalization;
using Gpixel.App.Localization;
using Gpixel.Core.Model;

namespace Gpixel.App.Ai;

/// <summary>Shows the source and lets the user position the canvas-sized region before any frame is cropped.</summary>
internal sealed class AiCropDialog : DialogBase
{
    private readonly CropPreview _preview;

    internal AiCropDialog(PixelImage image, int canvasWidth, int canvasHeight)
        : base(UiStrings.AiCrop_Title, UiStrings.AiCrop_Keep)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);

        var instruction = Ui.Label(UiStrings.AiCrop_Instruction);
        instruction.TextWrapping = TextWrapping.Wrap;
        instruction.Foreground = AppTheme.DimTextBrush;
        instruction.MaxWidth = 560;

        var position = Ui.Label("");
        position.Foreground = AppTheme.DimTextBrush;
        _preview = new CropPreview(image, canvasWidth, canvasHeight, origin =>
            position.Text = string.Format(CultureInfo.CurrentCulture, UiStrings.AiCrop_Position,
                canvasWidth, canvasHeight, origin.X, origin.Y));
        position.Text = string.Format(CultureInfo.CurrentCulture, UiStrings.AiCrop_Position,
            canvasWidth, canvasHeight, 0, 0);

        Body.Children.Add(instruction);
        Body.Children.Add(new Border
        {
            Child = _preview,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 6),
        });
        Body.Children.Add(position);
        Opened += (_, _) => _preview.Focus();
    }

    /// <summary>Top-left of the chosen region, in original source pixels. Only use after OK.</summary>
    public Point CropOrigin => _preview.Origin;

    protected override void OnClosed(EventArgs e)
    {
        _preview.Dispose();
        base.OnClosed(e);
    }

    private sealed class CropPreview : Control, IDisposable
    {
        private const double PreviewWidth = 560;
        private const double PreviewHeight = 420;
        private const double Padding = 12;
        private static readonly IBrush OutsideBrush = AppTheme.Brush(Color.FromArgb(155, 15, 15, 20));
        private static readonly Pen SourcePen = new(AppTheme.DimTextBrush, 1);
        private static readonly Pen SelectionShadow = new(AppTheme.BackBrush, 4);
        private static readonly Pen SelectionPen = new(AppTheme.AccentBrush, 2);
        private static readonly Pen GridPen = new(AppTheme.Brush(Color.FromArgb(65, 15, 15, 20)), 1);

        private readonly int _sourceWidth, _sourceHeight, _canvasWidth, _canvasHeight;
        private readonly Bitmap _thumbnail;
        private readonly Action<Point> _changed;
        private Point _origin;
        private Avalonia.Point _dragStart;
        private Point _dragOrigin;
        private bool _dragging;

        public CropPreview(PixelImage image, int canvasWidth, int canvasHeight, Action<Point> changed)
        {
            _sourceWidth = image.Width;
            _sourceHeight = image.Height;
            _canvasWidth = canvasWidth;
            _canvasHeight = canvasHeight;
            _changed = changed;
            Width = PreviewWidth;
            Height = PreviewHeight;
            Focusable = true;
            ClipToBounds = true;

            // Never upload a huge source bitmap merely to show a bounded preview.
            double fit = Math.Min((PreviewWidth - 2 * Padding) / Math.Max(image.Width, canvasWidth),
                (PreviewHeight - 2 * Padding) / Math.Max(image.Height, canvasHeight));
            int width = Math.Clamp((int)Math.Ceiling(image.Width * Math.Min(1, fit)), 1, (int)PreviewWidth);
            int height = Math.Clamp((int)Math.Ceiling(image.Height * Math.Min(1, fit)), 1, (int)PreviewHeight);
            if (width == image.Width && height == image.Height)
                _thumbnail = ImageUtil.ToBitmap(image);
            else
            {
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                {
                    int row = (int)((long)y * image.Height / height) * image.Width;
                    for (int x = 0; x < width; x++)
                        pixels[y * width + x] = image.Pixels[row + (int)((long)x * image.Width / width)];
                }
                _thumbnail = ImageUtil.ToBitmap(new PixelImage(width, height, pixels));
            }
        }

        public Point Origin => _origin;
        public void Dispose() => _thumbnail.Dispose();

        private (Rect Area, double Scale) Geometry()
        {
            int width = Math.Max(_sourceWidth, _canvasWidth);
            int height = Math.Max(_sourceHeight, _canvasHeight);
            double fit = Math.Min((Bounds.Width - 2 * Padding) / width, (Bounds.Height - 2 * Padding) / height);
            double scale = fit >= 1 ? Math.Floor(fit) : fit;
            return (new Rect((Bounds.Width - width * scale) / 2,
                (Bounds.Height - height * scale) / 2, width * scale, height * scale), scale);
        }

        private void SetOrigin(int x, int y)
        {
            var next = new Point(Math.Clamp(x, 0, Math.Max(0, _sourceWidth - _canvasWidth)),
                Math.Clamp(y, 0, Math.Max(0, _sourceHeight - _canvasHeight)));
            if (next == _origin) return;
            _origin = next;
            _changed(next);
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            var (area, scale) = Geometry();
            if (scale <= 0) return;
            g.FillRectangle(AppTheme.PanelBrush, new Rect(Bounds.Size));
            AppTheme.DrawChecker(g, area, Math.Clamp(scale * 2, 4, 8));
            var source = new Rect(area.X + Math.Max(0, (_canvasWidth - _sourceWidth) / 2) * scale,
                area.Y + Math.Max(0, (_canvasHeight - _sourceHeight) / 2) * scale,
                _sourceWidth * scale, _sourceHeight * scale);
            using (ImageUtil.Pixelated(g))
                g.DrawImage(_thumbnail, new Rect(0, 0, _thumbnail.PixelSize.Width, _thumbnail.PixelSize.Height), source);
            g.DrawRectangle(null, SourcePen, source.Deflate(0.5));

            if (scale >= 8 && _sourceWidth <= 100 && _sourceHeight <= 100)
            {
                for (int x = 1; x < _sourceWidth; x++)
                    g.DrawLine(GridPen, new Avalonia.Point(source.X + x * scale, source.Y),
                        new Avalonia.Point(source.X + x * scale, source.Bottom));
                for (int y = 1; y < _sourceHeight; y++)
                    g.DrawLine(GridPen, new Avalonia.Point(source.X, source.Y + y * scale),
                        new Avalonia.Point(source.Right, source.Y + y * scale));
            }

            var crop = new Rect(area.X + _origin.X * scale, area.Y + _origin.Y * scale,
                _canvasWidth * scale, _canvasHeight * scale);
            using (g.PushClip(area))
            {
                if (crop.Top > area.Top)
                    g.FillRectangle(OutsideBrush, new Rect(area.Left, area.Top, area.Width, crop.Top - area.Top));
                if (crop.Bottom < area.Bottom)
                    g.FillRectangle(OutsideBrush, new Rect(area.Left, crop.Bottom, area.Width, area.Bottom - crop.Bottom));
                if (crop.Left > area.Left)
                    g.FillRectangle(OutsideBrush, new Rect(area.Left, crop.Top, crop.Left - area.Left, crop.Height));
                if (crop.Right < area.Right)
                    g.FillRectangle(OutsideBrush, new Rect(crop.Right, crop.Top, area.Right - crop.Right, crop.Height));
                g.DrawRectangle(null, SelectionShadow, crop);
                g.DrawRectangle(null, SelectionPen, crop);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var (area, scale) = Geometry();
            var at = e.GetPosition(this);
            if (scale <= 0 || !area.Contains(at)) return;
            Focus();
            var crop = new Rect(area.X + _origin.X * scale, area.Y + _origin.Y * scale,
                _canvasWidth * scale, _canvasHeight * scale);
            if (!crop.Contains(at))
                SetOrigin((int)Math.Round((at.X - area.X) / scale - _canvasWidth / 2.0),
                    (int)Math.Round((at.Y - area.Y) / scale - _canvasHeight / 2.0));
            _dragStart = at;
            _dragOrigin = _origin;
            _dragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_dragging) return;
            var (_, scale) = Geometry();
            if (scale <= 0) return;
            var at = e.GetPosition(this);
            SetOrigin((int)Math.Round(_dragOrigin.X + (at.X - _dragStart.X) / scale),
                (int)Math.Round(_dragOrigin.Y + (at.Y - _dragStart.Y) / scale));
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _dragging = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            switch (e.Key)
            {
                case Key.Left: SetOrigin(_origin.X - step, _origin.Y); break;
                case Key.Right: SetOrigin(_origin.X + step, _origin.Y); break;
                case Key.Up: SetOrigin(_origin.X, _origin.Y - step); break;
                case Key.Down: SetOrigin(_origin.X, _origin.Y + step); break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
