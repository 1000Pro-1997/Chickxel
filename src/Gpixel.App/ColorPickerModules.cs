using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>색 고르기에 함께 보일 앱 팔레트·최근 색·현재 프레임과 공용 색 고르기 방식.</summary>
public sealed record ColorPickSources(
    IReadOnlyList<NamedPalette> Palettes,
    IReadOnlyList<Color32> Recent,
    Func<PixelImage?> Image,
    ColorPickerMode Mode = ColorPickerMode.Square,
    Action<ColorPickerMode>? SetMode = null);

/// <summary>메인과 똑같은 4방식 색 편집기 왼쪽, 팔레트·스포이드 오른쪽을 조합한 팝업 내용.</summary>
internal sealed class ColorPickerEditor : StackPanel
{
    /// <summary>MainForm이 연결하는 현재 팔레트·최근 색·현재 프레임 공급자.</summary>
    public static Func<ColorPickSources?>? Sources { get; set; }

    private readonly ColorEditor _editor;
    private readonly ColorSourcePicker? _sources;

    public ColorPickerEditor(Color32 initial)
    {
        Orientation = Orientation.Horizontal;
        var source = Sources?.Invoke();
        _editor = new ColorEditor(initial, source?.Recent) { Width = 210 };
        if (source is not null) _editor.Mode = source.Mode;
        _editor.ColorChanged += (_, _) =>
        {
            _sources?.SetSelected(Color);
            ColorChanged?.Invoke(this, EventArgs.Empty);
        };
        _editor.RecentColorClicked += (_, e) => _editor.Pick(e.Color);
        if (source?.SetMode is { } setMode)
            _editor.ModeChanged += (_, _) => setMode(_editor.Mode);
        Children.Add(_editor);

        if (source is not null)
        {
            _sources = new ColorSourcePicker(source, initial, _editor.Pick);
            Children.Add(_sources);
        }
    }

    public event EventHandler? ColorChanged;

    public Color32 Color
    {
        get => _editor.Color;
        set => _editor.Color = value;
    }
}

/// <summary>색 편집기 오른쪽에서 앱 팔레트와 현재 프레임 스포이드를 제공하는 재사용 모듈.</summary>
internal sealed class ColorSourcePicker : ScrollViewer
{
    private readonly List<PaletteSection> _palettes = new();

    public ColorSourcePicker(ColorPickSources sources, Color32 initial, Action<Color32> pick)
    {
        MaxHeight = 460;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

        var body = new StackPanel { Spacing = 4, Width = 230, Margin = new Thickness(10, 0, 0, 0) };
        for (int i = 0; i < sources.Palettes.Count; i++)
        {
            var source = sources.Palettes[i];
            var palette = new PaletteSection(source.Name, source.Colors, source.Collapsed, editable: false);
            palette.Grid.LeadingTransparent = i == 0;
            palette.Grid.SetMarks(initial, initial);
            palette.Grid.ColorClicked += (_, e) => pick(e.Color);
            palette.ArrowButton.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(palette.ArrowButton).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                palette.Collapsed = !palette.Collapsed;
                palette.Refresh(false);
            };
            _palettes.Add(palette);
            body.Children.Add(palette);
        }


        if (sources.Image() is { } image)
        {
            var picker = new ImagePicker(image, pick) { IsVisible = false };
            var dropper = new ToggleButton
            {
                Content = UiStrings.S_ColorPickerModules_ColorSourcePicker_B498EFF5EB,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            }.Tip(UiStrings.S_ColorPickerModules_ColorSourcePicker_72F60739D7);
            dropper.IsCheckedChanged += (_, _) => picker.IsVisible = dropper.IsChecked == true;
            body.Children.Add(dropper);
            body.Children.Add(picker);
        }

        Content = body;
    }

    public void SetSelected(Color32 color)
    {
        foreach (var palette in _palettes) palette.Grid.SetMarks(color, color);
    }

    /// <summary>그림에 쓰인 색을 작게 나열하고 누른 색을 전달한다.</summary>
    public static WrapPanel Swatches(IEnumerable<Color32> colors, Action<Color32> pick)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var color in colors.Distinct())
        {
            var button = new Border
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(1),
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new SwatchCell(color),
            };
            ToolTip.SetTip(button, color.A == 0 ? UiStrings.S_ColorPickerModules_Swatches_3FF5B6C54B : color.ToHex());
            button.PointerPressed += (_, e) => { pick(color); e.Handled = true; };
            wrap.Children.Add(button);
        }
        return wrap;
    }

    private sealed class SwatchCell(Color32 color) : Control
    {
        public override void Render(DrawingContext context) => ImageUtil.DrawSwatch(context, new Rect(Bounds.Size), color);
    }

    private sealed class ImagePicker : Control
    {
        private const double Box = 220;
        private readonly PixelImage _image;
        private readonly WriteableBitmap _bitmap;
        private readonly Action<Color32> _pick;

        public ImagePicker(PixelImage image, Action<Color32> pick)
        {
            _image = image;
            _bitmap = ImageUtil.ToBitmap(image);
            _pick = pick;
            Width = Box;
            Height = Box;
            Cursor = new Cursor(StandardCursorType.Cross);
            ClipToBounds = true;
        }

        private Rect ImageRect
        {
            get
            {
                double fit = Math.Min(Box / _image.Width, Box / _image.Height);
                double scale = fit >= 1 ? Math.Floor(fit) : fit;
                double width = _image.Width * scale, height = _image.Height * scale;
                return new Rect((Box - width) / 2, (Box - height) / 2, width, height);
            }
        }

        public override void Render(DrawingContext context)
        {
            var rect = ImageRect;
            context.FillRectangle(AppTheme.InputBrush, new Rect(Bounds.Size));
            AppTheme.DrawChecker(context, rect, 6);
            using (ImageUtil.Pixelated(context))
                context.DrawImage(_bitmap, new Rect(0, 0, _image.Width, _image.Height), rect);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            e.Pointer.Capture(this);
            PickAt(e.GetPosition(this));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) PickAt(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Pointer.Capture(null);
        }

        private void PickAt(Avalonia.Point point)
        {
            var rect = ImageRect;
            int x = (int)Math.Floor((point.X - rect.X) / rect.Width * _image.Width);
            int y = (int)Math.Floor((point.Y - rect.Y) / rect.Height * _image.Height);
            if (!_image.Contains(x, y)) return;
            var color = _image.Get(x, y);
            if (color.A > 0) _pick(color);
        }
    }
}
