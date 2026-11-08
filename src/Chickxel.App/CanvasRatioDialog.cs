using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 캔버스 크기 모드의 특수 비율(도구 설정 '그림 비율 유지' 옆 단추): 비율(빠른 고르기·직접 넣기), 기준(가로·세로 유지), 방식(캔버스만·그림 늘리기)을 고르고
/// 미리보기로 확인한다. 확인하면 계산한 값(<see cref="CanvasRatio.Plan"/>)을 돌려줄 뿐이고, 모드에 넣는 것은 부르는 쪽 — 적용·취소는 그 모드가 한다.
/// </summary>
internal sealed class CanvasRatioDialog : DialogBase
{
    // 고른 값은 앱을 켜 둔 동안 기억한다.
    private static int s_ratioWidth = 16, s_ratioHeight = 9;
    private static CanvasRatioBasis s_basis = CanvasRatioBasis.KeepWidth;
    private static CanvasRatioFit s_fit = CanvasRatioFit.Canvas;

    private readonly Rectangle _canvas, _image;
    private readonly Anchor? _anchor;
    private readonly TabStrip _presets = new(TabStripKind.Segmented);
    private readonly NumericUpDown _ratioWidth, _ratioHeight;
    private readonly TabStrip _basis = new(TabStripKind.Segmented, UiStrings.CanvasRatio_KeepWidth, UiStrings.CanvasRatio_KeepHeight);
    private readonly TabStrip _fit = new(TabStripKind.Segmented, UiStrings.CanvasRatio_FitCanvas, UiStrings.CanvasRatio_FitStretch);
    private readonly RatioPreview _preview;
    private readonly TextBlock _sizes, _imageSize, _error;
    private bool _updating;

    /// <summary>확인했을 때 넣을 새 캔버스(원래 프레임 좌표)와 그림 사각형(새 캔버스 좌표). 크기를 정할 수 없으면 null.</summary>
    public (Rectangle Canvas, Rectangle Image)? Plan { get; private set; }

    /// <summary>고른 방식.</summary>
    public CanvasRatioFit Fit => s_fit;

    /// <summary>
    /// 캔버스 크기 모드의 지금 값으로 창을 띄운다. 확인하면 넣을 값과 방식, 취소했거나 모드가 아니면 null.
    /// <paramref name="anchor"/>는 도구 설정에 눌려 보이는 기준점(그림이 9방향 어디에도 맞지 않으면 null — 가운데로 늘거나 줆).
    /// </summary>
    public static (Rectangle Canvas, Rectangle Image, CanvasRatioFit Fit)? Run(Visual owner, EditorContext ctx, Anchor? anchor)
    {
        if (!ctx.IsCanvasResizing || ctx.CanvasResizeSourceImage is not { } source) return null;
        var dialog = new CanvasRatioDialog(source, ctx.CanvasResizeCanvas, ctx.CanvasResizeImage, anchor);
        if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.Plan is not { } plan) return null;
        return (plan.Canvas, plan.Image, dialog.Fit);
    }

    public CanvasRatioDialog(PixelImage source, Rectangle canvas, Rectangle image, Anchor? anchor)
        : base(UiStrings.CanvasRatio_Title)
    {
        _canvas = canvas;
        _image = image;
        _anchor = anchor;
        var intro = Ui.Label(UiStrings.CanvasRatio_Intro);
        intro.TextWrapping = TextWrapping.Wrap;
        intro.MaxWidth = 340;
        intro.Foreground = AppTheme.DimTextBrush;

        foreach (var (w, h) in CanvasRatio.Presets) _presets.Add($"{w}:{h}");
        _presets.Tip(UiStrings.CanvasRatio_RatioTip);
        _ratioWidth = RatioNum(s_ratioWidth);
        _ratioHeight = RatioNum(s_ratioHeight);
        _basis.SelectedIndex = (int)s_basis;
        _basis.Tip(UiStrings.CanvasRatio_BasisTip);
        _fit.SelectedIndex = (int)s_fit;
        _fit.Tip(UiStrings.CanvasRatio_FitTip);
        _preview = new RatioPreview(source) { Margin = new Thickness(0, 6, 0, 4) };
        _preview.Tip(UiStrings.CanvasRatio_PreviewTip);
        _sizes = Ui.Label("");
        _imageSize = Ui.Label("");
        _imageSize.Foreground = AppTheme.DimTextBrush;
        _error = Ui.Label("");
        _error.Foreground = AppTheme.WarningBrush;
        _error.IsVisible = false;

        _presets.SelectionChanged += (_, _) =>
        {
            if (_updating || _presets.SelectedIndex < 0) return;
            var (w, h) = CanvasRatio.Presets[_presets.SelectedIndex];
            _updating = true;
            _ratioWidth.Value = w;
            _ratioHeight.Value = h;
            _updating = false;
            Refresh();
        };
        _ratioWidth.ValueChanged += (_, _) => { if (!_updating) Refresh(); };
        _ratioHeight.ValueChanged += (_, _) => { if (!_updating) Refresh(); };
        _basis.SelectionChanged += (_, _) => Refresh();
        _fit.SelectionChanged += (_, _) => Refresh();

        Body.Children.Add(intro);
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.CanvasRatio_Ratio), _presets).HelpAt("tools-canvas-size", anchor: "Ratio:"));
        Body.Children.Add(Ui.Row(_ratioWidth, Ui.Label(":"), _ratioHeight).HelpAt("tools-canvas-size", anchor: "Ratio:"));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.CanvasRatio_Basis), _basis).HelpAt("tools-canvas-size", anchor: "Keep:"));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.CanvasRatio_Fit), _fit).HelpAt("tools-canvas-size", anchor: "Method:"));
        Body.Children.Add(new Border { Child = _preview, BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left }
            .HelpAt("tools-canvas-size", anchor: "The preview shows"));
        Body.Children.Add(_sizes.HelpAt("tools-canvas-size", anchor: "Below it you see"));
        Body.Children.Add(_imageSize.HelpAt("tools-canvas-size", anchor: "Below it you see"));
        Body.Children.Add(_error.HelpAt("tools-canvas-size", anchor: "Below it you see"));
        // 대상 도움말(F1)
        this.HelpAt("tools-canvas-size", anchor: "Aspect ratio");
        Refresh();
    }

    /// <summary>비율 한 항 칸(1~100 정수).</summary>
    private NumericUpDown RatioNum(int value)
    {
        var n = Ui.Num(1, CanvasRatio.MaxTerm, value, 56);
        n.Tip(UiStrings.CanvasRatio_RatioTip);
        return n;
    }

    /// <summary>고른 값으로 다시 계산해 미리보기·크기 글을 맞춘다. 크기를 정할 수 없으면 확인을 끈다.</summary>
    private void Refresh()
    {
        s_ratioWidth = (int)_ratioWidth.NumValue();
        s_ratioHeight = (int)_ratioHeight.NumValue();
        if (_basis.SelectedIndex >= 0) s_basis = (CanvasRatioBasis)_basis.SelectedIndex;
        if (_fit.SelectedIndex >= 0) s_fit = (CanvasRatioFit)_fit.SelectedIndex;
        // 직접 넣은 값이 빠른 고르기 비율과 같으면 그 단추를 눌린 것으로 보인다.
        _updating = true;
        _presets.SelectedIndex = CanvasRatio.Presets.ToList().IndexOf((s_ratioWidth, s_ratioHeight));
        _updating = false;

        Plan = CanvasRatio.Plan(_canvas, _image, s_ratioWidth, s_ratioHeight, s_basis, s_fit, _anchor);
        string from = $"{_canvas.Width}×{_canvas.Height}";
        if (Plan is { } plan)
        {
            _sizes.Text = UiStrings.FormatCanvasRatio_CanvasSize(from: from, to: $"{plan.Canvas.Width}×{plan.Canvas.Height}");
            _imageSize.Text = UiStrings.FormatCanvasRatio_ImageSize(size: $"{plan.Image.Width}×{plan.Image.Height}");
            _error.IsVisible = false;
        }
        else
        {
            _sizes.Text = UiStrings.FormatCanvasRatio_CanvasSize(from: from, to: "-");
            _imageSize.Text = UiStrings.FormatCanvasRatio_ImageSize(size: $"{_image.Width}×{_image.Height}");
            _error.Text = UiStrings.FormatCanvasRatio_OutOfRange(max: PixelImage.MaxSize.ToString(CultureInfo.CurrentCulture));
            _error.IsVisible = true;
        }
        _preview.Show(_canvas, Plan);
        OkButton.IsEnabled = Plan is not null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _preview.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// 작은 미리보기: 지금 캔버스(점선), 새 캔버스(체커 + 강조색 테두리), 그림이 놓일 자리(새 캔버스 밖으로 나가는 부분은 흐리게).
    /// 그림은 원래 프레임 합성을 최근접으로 늘여 그린다(9-slice는 어림 — 정확한 모습은 모드 화면).
    /// </summary>
    private sealed class RatioPreview : Control, IDisposable
    {
        private const double PreviewWidth = 300, PreviewHeight = 180, Padding = 10;
        /// <summary>큰 그림은 이 크기 안으로 줄여 올린다(미리보기 칸보다 넉넉히).</summary>
        private const int ThumbnailMax = 512;
        private static readonly IPen OldPen = new Pen(AppTheme.DimTextBrush, 1, new DashStyle([3, 3], 0));
        private static readonly IPen NewPen = new Pen(AppTheme.AccentBrush, 2);

        private readonly Bitmap _thumbnail;
        private Rectangle _old;
        private (Rectangle Canvas, Rectangle Image)? _plan;

        public RatioPreview(PixelImage source)
        {
            Width = PreviewWidth;
            Height = PreviewHeight;
            ClipToBounds = true;
            _thumbnail = ImageUtil.ToBitmap(Shrink(source));
        }

        public void Dispose() => _thumbnail.Dispose();

        public void Show(Rectangle oldCanvas, (Rectangle Canvas, Rectangle Image)? plan)
        {
            _old = oldCanvas;
            _plan = plan;
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            g.FillRectangle(AppTheme.PanelBrush, new Rect(Bounds.Size));
            // 원래 프레임 좌표: 새 캔버스 원점 + 그림 자리 = 그림이 놓일 곳
            var bounds = _old;
            Rectangle? canvas = null, picture = null;
            if (_plan is { } p)
            {
                canvas = p.Canvas;
                picture = new Rectangle(p.Canvas.X + p.Image.X, p.Canvas.Y + p.Image.Y, p.Image.Width, p.Image.Height);
                bounds = Rectangle.Union(Rectangle.Union(bounds, p.Canvas), picture.Value);
            }
            double scale = Math.Min((Bounds.Width - 2 * Padding) / bounds.Width, (Bounds.Height - 2 * Padding) / bounds.Height);
            if (scale <= 0) return;
            double ox = (Bounds.Width - bounds.Width * scale) / 2 - bounds.X * scale;
            double oy = (Bounds.Height - bounds.Height * scale) / 2 - bounds.Y * scale;
            Rect ToScreen(Rectangle r) => new(ox + r.X * scale, oy + r.Y * scale, r.Width * scale, r.Height * scale);

            var source = new Rect(0, 0, _thumbnail.PixelSize.Width, _thumbnail.PixelSize.Height);
            if (canvas is { } c && picture is { } pic)
            {
                var canvasRect = ToScreen(c);
                var pictureRect = ToScreen(pic);
                AppTheme.DrawChecker(g, canvasRect, Math.Clamp(scale * 2, 4, 8));
                using (ImageUtil.Pixelated(g))
                {
                    // 새 캔버스 밖으로 나가는 부분(잘려 캔버스 밖 그림으로 남음)은 흐리게, 안은 그대로
                    using (g.PushOpacity(0.35)) g.DrawImage(_thumbnail, source, pictureRect);
                    using (g.PushClip(canvasRect)) g.DrawImage(_thumbnail, source, pictureRect);
                }
                g.DrawRectangle(null, OldPen, ToScreen(_old).Deflate(0.5));
                g.DrawRectangle(null, NewPen, canvasRect.Deflate(1));
            }
            else g.DrawRectangle(null, OldPen, ToScreen(_old).Deflate(0.5));
        }

        /// <summary>긴 변이 <see cref="ThumbnailMax"/>보다 크면 최근접으로 줄인다(미리보기에 큰 비트맵을 올리지 않게).</summary>
        private static PixelImage Shrink(PixelImage image)
        {
            double fit = Math.Min(1, (double)ThumbnailMax / Math.Max(image.Width, image.Height));
            if (fit >= 1) return image;
            int width = Math.Max(1, (int)(image.Width * fit)), height = Math.Max(1, (int)(image.Height * fit));
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = (int)((long)y * image.Height / height) * image.Width;
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = image.Pixels[row + (int)((long)x * image.Width / width)];
            }
            return new PixelImage(width, height, pixels);
        }
    }
}
