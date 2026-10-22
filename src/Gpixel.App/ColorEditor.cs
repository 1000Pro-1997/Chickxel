using Avalonia.Media.Immutable;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 메인 색 패널과 색 팝업이 함께 쓰는 동일한 색 편집기:
/// 사각형·벌집·색상환·슬라이더, 알파 막대, RGBA·HSV·HEX, 최근 색.
/// </summary>
internal sealed class ColorEditor : UserControl
{
    private readonly SvBox _sv;
    private readonly HueBar _hue;
    private readonly AlphaBar _alpha;
    private readonly StackPanel _squarePicker;
    private readonly HoneycombPicker _honeycomb;
    private readonly WheelPicker _wheel;
    private readonly SliderPicker _sliders;
    private readonly TabStrip _modeTabs = new(TabStripKind.Compact) { Margin = new Thickness(0, 0, 2, 0) };
    private readonly NumericUpDown _r, _g, _b, _a, _hNum, _sNum, _vNum;
    private readonly TextBox _hex;
    private readonly SwatchGrid _recent;
    private Color32 _color;
    private bool _updating;
    private float _h, _s, _v;

    public ColorEditor(Color32 initial, IEnumerable<Color32>? recent = null)
    {
        Background = AppTheme.PanelBrush;
        MinWidth = 200;

        _sv = new SvBox { Width = 170, Height = 110, Margin = new Thickness(3) };
        _hue = new HueBar { Width = 16, Height = 110, Margin = new Thickness(3) };
        _alpha = new AlphaBar { Height = 12, Margin = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Stretch };
        _sv.ValueChanged += (_, _) => { _s = _sv.S; _v = _sv.V; ApplyHsv(); };
        _hue.ValueChanged += (_, _) => { _h = _hue.Hue; _sv.Hue = _h; ApplyHsv(); };
        _alpha.ValueChanged += (_, _) => { if (!_updating) _a!.Value = _alpha.Alpha; };
        _squarePicker = Ui.Row(_sv, _hue);
        _squarePicker.HorizontalAlignment = HorizontalAlignment.Center;
        _honeycomb = new HoneycombPicker { Margin = new Thickness(3) };
        _honeycomb.ColorPicked += (_, color) => SetFromRgb(color.WithAlpha((byte)_a!.NumValue()));
        _wheel = new WheelPicker { Margin = new Thickness(3) };
        _wheel.ValueChanged += (_, _) => { _h = _wheel.Hue; _s = _wheel.S; _v = _wheel.V; ApplyHsv(); };
        _sliders = new SliderPicker { Margin = new Thickness(3) };
        _sliders.RgbPicked += (_, color) => SetFromRgb(color.WithAlpha((byte)_a!.NumValue()));
        _sliders.HsvPicked += (_, hsv) => { (_h, _s, _v) = hsv; ApplyHsv(); };
        foreach (var mode in ColorPickerModes.All)
            _modeTabs.Add(null, ColorPickerModes.Icon(mode), $"색 고르기: {ColorPickerModes.Name(mode)}");
        _modeTabs.SelectionChanged += (_, _) =>
        {
            ShowPicker();
            ModeChanged?.Invoke(this, EventArgs.Empty);
        };

        decimal[] channel = [0, 32, 64, 96, 128, 160, 192, 224, 255];
        _r = Ui.Num(0, 255, 0, 44).Presets(channel).RecentKey("color.rgb");
        _g = Ui.Num(0, 255, 0, 44).Presets(channel).RecentKey("color.rgb");
        _b = Ui.Num(0, 255, 0, 44).Presets(channel).RecentKey("color.rgb");
        _a = Ui.Num(0, 255, 255, 44).Presets(channel).RecentKey("color.alpha");
        _hNum = Ui.Num(0, 360, 0, 44).Presets(0, 30, 60, 90, 120, 150, 180, 210, 240, 270, 300, 330).Unit("°").RecentKey("color.hue");
        _sNum = Ui.Num(0, 100, 0, 44).Presets(Ui.Percents).Unit("%").RecentKey("color.saturation");
        _vNum = Ui.Num(0, 100, 0, 44).Presets(Ui.Percents).Unit("%").RecentKey("color.value");
        foreach (var number in new[] { _r, _g, _b, _a }) number.ValueChanged += (_, _) => ApplyRgba();
        foreach (var number in new[] { _hNum, _sNum, _vNum })
            number.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                _h = (float)_hNum.NumValue();
                _s = (float)_sNum.NumValue() / 100f;
                _v = (float)_vNum.NumValue() / 100f;
                ApplyHsv();
            };
        _hex = Ui.Text("", 80);
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyHex(); e.Handled = true; } };
        _hex.LostFocus += (_, _) => ApplyHex();

        _recent = new SwatchGrid { Height = 30, Cell = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        _recent.SetColors(recent ?? []);
        _recent.ColorClicked += (_, e) => RecentColorClicked?.Invoke(this, e);

        var pickerArea = new Panel { Children = { _squarePicker, _honeycomb, _wheel, _sliders } };
        var body = Ui.Stack(pickerArea, _alpha, NumberGrid(), Ui.Label("최근 색"), _recent);
        var header = Ui.Header("색");
        var title = header.Child!;
        header.Child = null;
        var headerRow = new DockPanel();
        DockPanel.SetDock(_modeTabs, Dock.Right);
        headerRow.Children.Add(_modeTabs);
        headerRow.Children.Add(title);
        header.Child = headerRow;

        var root = new DockPanel { Background = AppTheme.PanelBrush };
        root.Children.Add(header);
        root.Children.Add(body);
        Content = root;

        _modeTabs.SelectedIndex = 0;
        SetColor(initial, fromHsv: false, notify: false);
    }

    public event EventHandler? ColorChanged;
    public event EventHandler? ModeChanged;
    public event EventHandler<(Color32 Color, MouseButton Button)>? RecentColorClicked;

    public Color32 Color
    {
        get => _color;
        set { if (value != _color) SetColor(value, fromHsv: false, notify: false); }
    }

    public ColorPickerMode Mode
    {
        get => _modeTabs.SelectedIndex < 0 ? ColorPickerMode.Square : ColorPickerModes.All[_modeTabs.SelectedIndex];
        set
        {
            int index = Array.IndexOf(ColorPickerModes.All, value);
            _modeTabs.SelectedIndex = index < 0 ? 0 : index;
            ShowPicker();
        }
    }

    /// <summary>팔레트·최근 색처럼 사용자가 고른 색을 적용하고 ColorChanged를 낸다.</summary>
    public void Pick(Color32 color) => SetFromRgb(color);

    public void SetRecent(IEnumerable<Color32> colors) => _recent.SetColors(colors);

    private void ShowPicker()
    {
        var mode = Mode;
        _squarePicker.IsVisible = mode == ColorPickerMode.Square;
        _honeycomb.IsVisible = mode == ColorPickerMode.Honeycomb;
        _wheel.IsVisible = mode == ColorPickerMode.Wheel;
        _sliders.IsVisible = mode == ColorPickerMode.Sliders;
    }

    private void SetFromRgb(Color32 color) => SetColor(color, fromHsv: false, notify: true);

    private void SetColor(Color32 color, bool fromHsv, bool notify)
    {
        if (!fromHsv)
        {
            var (h, s, v) = color.ToHsv();
            // 무채색을 오갈 때도 사용자가 보고 있던 색상·채도를 유지한다.
            if (s > 0 && v > 0) _h = h;
            if (v > 0) _s = s;
            _v = v;
        }
        bool changed = _color != color;
        _color = color;
        UpdateEditors(color, fromHsv);
        if (notify && changed) ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEditors(Color32 color, bool fromHsv)
    {
        _updating = true;
        _r.Value = color.R;
        _g.Value = color.G;
        _b.Value = color.B;
        _a.Value = color.A;
        _alpha.Alpha = color.A;
        _alpha.BaseColor = color;
        _hNum.Value = (decimal)Math.Clamp(MathF.Round(_h), 0, 360);
        _sNum.Value = (decimal)Math.Clamp(MathF.Round(_s * 100), 0, 100);
        _vNum.Value = (decimal)Math.Clamp(MathF.Round(_v * 100), 0, 100);
        if (!_hex.IsFocused) _hex.Text = color.ToHex();
        _hue.Hue = _h;
        _sv.Hue = _h;
        if (!fromHsv || !_sv.Dragging) _sv.SetSv(_s, _v);
        _honeycomb.Current = color;
        _wheel.SetHsv(_h, _s, _v);
        _sliders.SetColor(color, _h, _s, _v);
        _updating = false;
    }

    private void ApplyHsv()
    {
        if (_updating) return;
        SetColor(Color32.FromHsv(_h, _s, _v, (byte)_a.NumValue()), fromHsv: true, notify: true);
    }

    private void ApplyRgba()
    {
        if (_updating) return;
        SetFromRgb(new Color32((byte)_r.NumValue(), (byte)_g.NumValue(), (byte)_b.NumValue(), (byte)_a.NumValue()));
    }

    private void ApplyHex()
    {
        if (Color32.TryParseHex((_hex.Text ?? "").Trim(), out var color)) SetFromRgb(color);
        else _hex.Text = _color.ToHex();
    }

    private Grid NumberGrid()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
        };
        void Put(Control control, int row, int column, int span = 1)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            Grid.SetColumnSpan(control, span);
            grid.Children.Add(control);
        }
        void Cell(string label, Control box, int row, int column, int span = 1)
        {
            var text = Ui.Label(label);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Margin = new Thickness(column == 0 ? 2 : 6, 0, 0, 0);
            box.Width = double.NaN;
            box.MinWidth = 0;
            box.HorizontalAlignment = HorizontalAlignment.Stretch;
            box.Margin = new Thickness(3, 2);
            Put(text, row, column);
            Put(box, row, column + 1, span);
        }
        Cell("R", _r, 0, 0); Cell("G", _g, 0, 2); Cell("B", _b, 0, 4);
        Cell("H", _hNum, 1, 0); Cell("S", _sNum, 1, 2); Cell("V", _vNum, 1, 4);
        Cell("A", _a, 2, 0); Cell("#", _hex, 2, 2, span: 3);
        return grid;
    }

    private sealed class SvBox : Control
    {
        private float _hue, _s, _v;

        public SvBox()
        {
            Cursor = new Cursor(StandardCursorType.Cross);
            ClipToBounds = true;
        }

        public event EventHandler? ValueChanged;
        public float S => _s;
        public float V => _v;
        public bool Dragging { get; private set; }
        public float Hue
        {
            get => _hue;
            set { if (Math.Abs(_hue - value) < 0.01f) return; _hue = value; InvalidateVisual(); }
        }

        public void SetSv(float s, float v)
        {
            _s = s;
            _v = v;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context) => PickerPaint.DrawSvSquare(context, new Rect(Bounds.Size), _hue, _s, _v);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;
            Dragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            Pick(point.Position);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (Dragging) Pick(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            Dragging = false;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            Dragging = false;
        }

        private void Pick(Avalonia.Point point)
        {
            _s = (float)Math.Clamp(point.X / Math.Max(1, Bounds.Width - 1), 0, 1);
            _v = 1 - (float)Math.Clamp(point.Y / Math.Max(1, Bounds.Height - 1), 0, 1);
            InvalidateVisual();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class AlphaBar : Control
    {
        private const double Pad = 4;
        private byte _alpha = 255;
        private Color32 _base = Color32.Black;
        private IBrush? _gradient;
        private bool _drag;

        public AlphaBar()
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            ClipToBounds = true;
        }

        public event EventHandler? ValueChanged;
        public byte Alpha { get => _alpha; set { if (_alpha == value) return; _alpha = value; InvalidateVisual(); } }
        public Color32 BaseColor
        {
            get => _base;
            set
            {
                if (_gradient is not null && _base.WithAlpha(255) == value.WithAlpha(255)) { _base = value; return; }
                _base = value;
                _gradient = null;
                InvalidateVisual();
            }
        }

        private Rect BarRect => new(Pad, 0, Math.Max(1, Bounds.Width - 2 * Pad), Bounds.Height);

        public override void Render(DrawingContext context)
        {
            var rect = BarRect;
            AppTheme.DrawChecker(context, rect, 4);
            _gradient ??= new ImmutableLinearGradientBrush(
                [new ImmutableGradientStop(0, _base.WithAlpha(0).ToColor()), new ImmutableGradientStop(1, _base.WithAlpha(255).ToColor())],
                startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(1, 0, RelativeUnit.Relative));
            context.FillRectangle(_gradient, rect);
            double x = rect.X + _alpha / 255.0 * (rect.Width - 1);
            context.DrawRectangle(null, PickerPaint.WhiteMarker, new Rect(x - 2, 1, 4, Math.Max(0, rect.Height - 3)));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;
            _drag = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            Pick(point.Position.X);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_drag) Pick(e.GetPosition(this).X);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _drag = false;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _drag = false;
        }

        private void Pick(double x)
        {
            var rect = BarRect;
            _alpha = (byte)Math.Round(Math.Clamp((x - rect.X) / Math.Max(1, rect.Width - 1), 0, 1) * 255);
            InvalidateVisual();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class HueBar : Control
    {
        private const double Pad = 4;
        private static readonly IBrush Spectrum = new ImmutableLinearGradientBrush(
            Enumerable.Range(0, 7).Select(i => new ImmutableGradientStop(i / 6.0,
                i == 6 ? Colors.Red : Color32.FromHsv(i * 60 % 360, 1, 1).ToColor())).ToArray(),
            startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
        private float _hue;
        private bool _drag;

        public HueBar()
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            ClipToBounds = true;
        }

        public event EventHandler? ValueChanged;
        public float Hue
        {
            get => _hue;
            set { if (Math.Abs(_hue - value) < 0.01f) return; _hue = value; InvalidateVisual(); }
        }

        private Rect BarRect => new(0, Pad, Bounds.Width, Math.Max(1, Bounds.Height - 2 * Pad));

        public override void Render(DrawingContext context)
        {
            var rect = BarRect;
            context.FillRectangle(Spectrum, rect);
            double y = rect.Y + _hue / 360.0 * (rect.Height - 1);
            context.DrawRectangle(null, PickerPaint.WhiteMarker, new Rect(1, y - 2, Math.Max(0, rect.Width - 3), 4));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) return;
            _drag = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            Pick(point.Position.Y);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_drag) Pick(e.GetPosition(this).Y);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _drag = false;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _drag = false;
        }

        private void Pick(double y)
        {
            var rect = BarRect;
            _hue = (float)Math.Clamp((y - rect.Y) / Math.Max(1, rect.Height - 1), 0, 1) * 360f;
            if (_hue >= 360f) _hue = 359.9f;
            InvalidateVisual();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
