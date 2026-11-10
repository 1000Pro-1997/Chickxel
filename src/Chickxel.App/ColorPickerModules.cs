using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

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

    /// <summary>알파를 고르지 않는 색(<see cref="ColorEditor.Opaque"/>).</summary>
    public bool Opaque
    {
        get => _editor.Opaque;
        set => _editor.Opaque = value;
    }
}

/// <summary>색 편집기 오른쪽에서 앱 팔레트와 스포이드(현재 프레임 창·화면 전체 — <see cref="ImagePickWindow"/>·<see cref="ScreenColorPicker"/>)를 제공하는 재사용 모듈.</summary>
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

        // 스포이드: 그림에서 집기(현재 프레임을 따로 창에 크게) · 화면에서 집기(다른 앱을 포함한 화면 전체).
        if (sources.Image() is { } image)
            body.Children.Add(Wide(Ui.Button(VectorIcon.Pipette, UiStrings.ColorPick_FromImage,
                (s, _) => ImagePickWindow.Open(s as Visual, image, pick)).Tip(UiStrings.ColorPick_FromImageTip)));
        var fromScreen = Wide(Ui.Button(VectorIcon.Monitor, UiStrings.ColorPick_FromScreen, (s, _) => ScreenColorPicker.Start(s as Visual, pick)));
        if (ScreenColorPicker.UnavailableReason is { } reason)
        {
            fromScreen.IsEnabled = false;
            ToolTip.SetShowOnDisabled(fromScreen, true);
            fromScreen.Tip(reason);
        }
        else fromScreen.Tip(UiStrings.ColorPick_FromScreenTip);
        body.Children.Add(fromScreen);

        Content = body;
    }

    /// <summary>본문 폭을 채우고 내용을 가운데에 둔 단추.</summary>
    private static Button Wide(Button button)
    {
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        return button;
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
}
