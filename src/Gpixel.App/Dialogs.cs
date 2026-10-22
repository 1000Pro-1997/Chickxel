using Avalonia.VisualTree;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>
/// 새 프로젝트: 캔버스 가로·세로, 시작 방식(일반 레이어 1 / 부위로 시작 = 그룹 + 첫 부위의 이름·크기), 배경(투명/보조색),
/// 파일 브라우저에서 만들 때는 파일 이름도.
/// </summary>
public sealed class NewImageDialog : DialogBase
{
    private readonly NumericUpDown _w, _h, _partW, _partH;
    private readonly CheckBox _transparent;
    private readonly TextBox? _name;
    private readonly TextBox _partName;
    private readonly RadioButton _normal, _parts;
    private readonly StackPanel _partRow;

    public NewImageDialog(int width, int height, string? fileName = null) : base(UiStrings.S_Dialogs_NewImageDialog_7A2F4AE34E, UiStrings.S_Dialogs_NewImageDialog_7D347172F9)
    {
        _w = Ui.Num(1, PixelImage.MaxSize, width, 80).Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        _h = Ui.Num(1, PixelImage.MaxSize, height, 80).Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        _transparent = Ui.Check(UiStrings.S_Dialogs_NewImageDialog_1BD6059500, true);
        _normal = new RadioButton { GroupName = "start", Content = UiStrings.S_Dialogs_NewImageDialog_6F947592B8, IsChecked = true, Foreground = AppTheme.TextBrush };
        _parts = new RadioButton { GroupName = "start", Content = UiStrings.S_Dialogs_NewImageDialog_0F2FF932AA, Foreground = AppTheme.TextBrush };
        _partName = Ui.Text(UiStrings.S_Dialogs_NewImageDialog_AB87E30975, 100);
        _partW = Ui.Num(1, PixelImage.MaxSize, Math.Min(8, width), 70).Presets(Ui.PartSizes).Unit("px").RecentKey("part.size");
        _partH = Ui.Num(1, PixelImage.MaxSize, Math.Min(8, height), 70).Presets(Ui.PartSizes).Unit("px").RecentKey("part.size");
        _partRow = Ui.Row(Ui.Label(UiStrings.S_Dialogs_NewImageDialog_7C07F3BD59), _partName, Ui.Label(UiStrings.S_Dialogs_NewImageDialog_D02D24EC7E), _partW, Ui.Label(UiStrings.S_Dialogs_NewImageDialog_03DEAAFD72), _partH, Ui.Label("px"));
        _partRow.IsEnabled = false;
        _parts.IsCheckedChanged += (_, _) => _partRow.IsEnabled = _parts.On();
        if (fileName is not null)
        {
            _name = Ui.Text(fileName, 220);
            Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_NewImageDialog_BF763338DC), _name, Ui.Label(SpriteProject.FileExtension)));
        }
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_NewImageDialog_3231BB27CA), Ui.Label(UiStrings.S_Dialogs_NewImageDialog_D02D24EC7E), _w, Ui.Label(UiStrings.S_Dialogs_NewImageDialog_03DEAAFD72), _h, Ui.Label("px")));
        var presets = Ui.Row();
        foreach (var (pw, ph) in new[] { (16, 16), (32, 32), (16, 32), (64, 64), (128, 128), (256, 256) })
            presets.Children.Add(Ui.Button($"{pw}×{ph}", (_, _) => { _w.Value = pw; _h.Value = ph; }));
        Body.Children.Add(presets);
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_NewImageDialog_DA31ACDA8F, bold: true));
        Body.Children.Add(_normal);
        Body.Children.Add(_parts);
        Body.Children.Add(_partRow);
        Body.Children.Add(_transparent);
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_NewImageDialog_8376337E30));
        OkButton.Click += (_, _) =>
        {
            if (_name is not null && (FileName.Length == 0 || FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                Ui.Error(this, UiStrings.S_Dialogs_NewImageDialog_E71C7CF793);
                DialogResult = DialogResult.None;
                return;
            }
            if (StartWithParts && PartName.Length == 0)
            {
                Ui.Error(this, UiStrings.S_Dialogs_NewImageDialog_DFFA42AA00);
                DialogResult = DialogResult.None;
            }
        };
    }

    public int ImageWidth => (int)_w.NumValue();
    public int ImageHeight => (int)_h.NumValue();
    public bool Transparent => _transparent.On();
    public string FileName => _name?.Text?.Trim() ?? "";
    /// <summary>그룹 + 첫 부위로 시작(아니면 일반 레이어 1).</summary>
    public bool StartWithParts => _parts.On();
    public string PartName => _partName.Text?.Trim() ?? "";
    public int PartWidth => (int)_partW.NumValue();
    public int PartHeight => (int)_partH.NumValue();
}

/// <summary>부위 추가: 이름 + 크기(부위 캔버스).</summary>
public sealed class NewPartDialog : DialogBase
{
    private readonly TextBox _name;
    private readonly NumericUpDown _w, _h;

    public NewPartDialog(string name, int width, int height) : base(UiStrings.S_Dialogs_NewPartDialog_2F37A51A37, UiStrings.S_Dialogs_NewPartDialog_961BAAAD27)
    {
        _name = Ui.Text(name, 200);
        _w = Ui.Num(1, PixelImage.MaxSize, width, 80).Presets(Ui.PartSizes).Unit("px").RecentKey("part.size");
        _h = Ui.Num(1, PixelImage.MaxSize, height, 80).Presets(Ui.PartSizes).Unit("px").RecentKey("part.size");
        var names = Ui.Row(Ui.Label(UiStrings.S_Dialogs_NewPartDialog_A37D1FAE04), _name);
        foreach (var n in new[] { UiStrings.S_Dialogs_NewPartDialog_2C2DA45F93, UiStrings.S_Dialogs_NewPartDialog_24E2B88EB7, UiStrings.S_Dialogs_NewPartDialog_EA5F33CDE6, UiStrings.S_Dialogs_NewPartDialog_FE7511726E })
            names.Children.Add(Ui.Button(n, (_, _) => _name.Text = n));
        Body.Children.Add(names);
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_NewPartDialog_0DFD005474), _w, Ui.Label(UiStrings.S_Dialogs_NewPartDialog_F6B6C03DD0), _h, Ui.Label("px")));
        var presets = Ui.Row();
        foreach (var (pw, ph) in new[] { (8, 8), (8, 5), (16, 16), (16, 32), (32, 32) })
            presets.Children.Add(Ui.Button($"{pw}×{ph}", (_, _) => { _w.Value = pw; _h.Value = ph; }));
        Body.Children.Add(presets);
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_NewPartDialog_A409FD6D21));
        Opened += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    public string PartName => _name.Text?.Trim() ?? "";
    public int PartWidth => (int)_w.NumValue();
    public int PartHeight => (int)_h.NumValue();
}

/// <summary>부위 위치: 프레임 캔버스 안 왼쪽 위 X·Y(밖으로 나가도 됨).</summary>
public sealed class PartOffsetDialog : DialogBase
{
    private readonly NumericUpDown _x, _y;

    public PartOffsetDialog(string partName, Point offset) : base(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Dialogs_PartOffsetDialog_24B8E24EE4,partName))
    {
        // 프레임 캔버스 안의 왼쪽 위 위치: 흔한 칸 경계 위주, 조금 밖으로 내민 음수도
        decimal[] positions = [-8, -4, 0, 2, 4, 8, 12, 16, 24, 32, 48, 64];
        _x = Ui.Num(-PixelImage.MaxSize, PixelImage.MaxSize, offset.X, 80).Presets(positions).Unit("px").RecentKey("part.position");
        _y = Ui.Num(-PixelImage.MaxSize, PixelImage.MaxSize, offset.Y, 80).Presets(positions).Unit("px").RecentKey("part.position");
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_PartOffsetDialog_0C8997B97B));
        Body.Children.Add(Ui.Row(Ui.Label("X"), _x, Ui.Label("Y"), _y, Ui.Label("px")));
    }

    public Point Offset => new((int)_x.NumValue(), (int)_y.NumValue());
}

/// <summary>PNG 가져오기: 칸이 모두 같은 크기일 때 부위 / 애니메이션 프레임 중 고르기.</summary>
public sealed class ImportKindDialog : DialogBase
{
    public ImportKindDialog(string fileName, int sliceCount, Size sliceSize) : base(UiStrings.S_Dialogs_ImportKindDialog_E1ED0FF7D0, UiStrings.S_Dialogs_ImportKindDialog_5EAAD1D0AB)
    {
        Body.Children.Add(Ui.Label(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Dialogs_ImportKindDialog_F7F2CAA9B5,fileName,sliceCount,sliceSize.Width,sliceSize.Height), bold: true));
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_ImportKindDialog_0162B199AF));
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_ImportKindDialog_6C0B0B1FB9));
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_ImportKindDialog_C65D508A8F));
        SetButtonResult(OkButton, DialogResult.Yes);
        OkButton.Width = 110;
        var anim = AddButton(UiStrings.S_Dialogs_ImportKindDialog_F16B548693, DialogResult.No);
        anim.Width = 150;
    }

    /// <summary>고른 가져오기 방식(취소하면 null).</summary>
    public static ImportMode? Ask(Visual? owner, string fileName, int sliceCount, Size sliceSize)
    {
        var dlg = new ImportKindDialog(fileName, sliceCount, sliceSize);
        return dlg.ShowDialog(owner) switch
        {
            DialogResult.Yes => ImportMode.Parts,
            DialogResult.No => ImportMode.Frames,
            _ => null,
        };
    }
}

/// <summary>부위 크기 바꾸기: 새 크기 + 기존 그림 기준점(9방향). 캔버스 크기는 캔버스 크기 모드(Ctrl+Alt+C)로 바꾼다.</summary>
public sealed class ResizeDialog : DialogBase
{
    private readonly NumericUpDown _w, _h;
    private readonly AnchorPicker _anchor = new();

    public ResizeDialog(int width, int height, string title) : base(title)
    {
        _w = Ui.Num(1, PixelImage.MaxSize, width, 80).Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        _h = Ui.Num(1, PixelImage.MaxSize, height, 80).Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        Body.Children.Add(Ui.Label(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Dialogs_ResizeDialog_EA62E1C6CB,width,height)));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_ResizeDialog_C4A4F3494A), _w, Ui.Label(UiStrings.S_Dialogs_ResizeDialog_F0673D9964), _h, Ui.Label("px")));
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_ResizeDialog_497FE1221E));
        Body.Children.Add(_anchor);
    }

    public int NewWidth => (int)_w.NumValue();
    public int NewHeight => (int)_h.NumValue();
    public Gpixel.Core.Model.Anchor ImageAnchor => _anchor.Value;
}

/// <summary>기준점 9방향 단추(라디오처럼 하나만 눌림).</summary>
public sealed class AnchorPicker : Grid
{
    private readonly ToggleButton[] _buttons = new ToggleButton[9];

    public AnchorPicker(Gpixel.Core.Model.Anchor value = Gpixel.Core.Model.Anchor.Center)
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto");
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
        Background = AppTheme.PanelBrush;
        HorizontalAlignment = HorizontalAlignment.Left;
        Value = value;
        VectorIcon[] arrows =
        [
            VectorIcon.ArrowUpLeft, VectorIcon.ArrowUp, VectorIcon.ArrowUpRight,
            VectorIcon.ArrowLeft, VectorIcon.Dot, VectorIcon.ArrowRight,
            VectorIcon.ArrowDownLeft, VectorIcon.ArrowDown, VectorIcon.ArrowDownRight,
        ];
        for (int i = 0; i < 9; i++)
        {
            int index = i;
            var b = new ToggleButton
            {
                Content = i == 4 ? Ui.Icon(VectorIcon.Dot, 14, filled: true) : Ui.Icon(arrows[i], 14),
                Width = 32,
                Height = 28,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1),
                IsChecked = i == (int)value,
            };
            // 라디오처럼: 하나만 눌려 있고, 눌린 걸 다시 눌러도 꺼지지 않는다.
            b.Click += (_, _) =>
            {
                Value = (Gpixel.Core.Model.Anchor)index;
                for (int k = 0; k < 9; k++) _buttons[k].IsChecked = k == index;
                Changed?.Invoke(this, EventArgs.Empty);
            };
            Grid.SetColumn(b, i % 3);
            Grid.SetRow(b, i / 3);
            _buttons[i] = b;
            Children.Add(b);
        }
    }

    public Gpixel.Core.Model.Anchor Value { get; private set; }

    /// <summary>눌린 단추만 바꾼다(<see cref="Changed"/> 없음). null = 아무것도 눌리지 않음(<see cref="Value"/>는 그대로).</summary>
    public void Show(Gpixel.Core.Model.Anchor? value)
    {
        if (value is { } v) Value = v;
        for (int k = 0; k < 9; k++) _buttons[k].IsChecked = k == (int?)value;
    }

    public event EventHandler? Changed;
}

/// <summary>시트 한 장을 격자(칸 크기/칸 수) 또는 자동(덩어리)으로 나눠 프레임으로 만드는 설정.</summary>
public sealed class SplitFramesDialog : DialogBase
{
    private readonly ComboBox _method;
    private readonly NumericUpDown _cellW, _cellH, _offX, _offY, _padX, _padY, _cols, _rows, _minSize;
    private readonly CheckBox _skipEmpty;
    private readonly TextBlock _result;
    private readonly Gpixel.Core.Model.PixelImage _image;

    public SplitFramesDialog(Gpixel.Core.Model.PixelImage image, int cellW, int cellH) : base(UiStrings.S_Dialogs_SplitFramesDialog_AA00952270, UiStrings.S_Dialogs_SplitFramesDialog_CC55D4341C)
    {
        _image = image;
        _method = Ui.Combo(170, UiStrings.S_Dialogs_SplitFramesDialog_E531B3BF12, UiStrings.S_Dialogs_SplitFramesDialog_6D54D43F1D, UiStrings.S_Dialogs_SplitFramesDialog_08AC8DBD4F);
        if (_method.SelectedIndex < 0) _method.SelectedIndex = 0;
        decimal[] counts = [1, 2, 3, 4, 5, 6, 8, 10, 12, 16];
        _cellW = Ui.Num(1, image.Width, Math.Min(cellW, image.Width), 60).Presets(Ui.SpriteSizes).Unit("px").RecentKey("cell.size");
        _cellH = Ui.Num(1, image.Height, Math.Min(cellH, image.Height), 60).Presets(Ui.SpriteSizes).Unit("px").RecentKey("cell.size");
        _offX = Ui.Num(0, image.Width - 1, 0, 60).Presets(Ui.Spacings).Unit("px").RecentKey("sheet.margin");
        _offY = Ui.Num(0, image.Height - 1, 0, 60).Presets(Ui.Spacings).Unit("px").RecentKey("sheet.margin");
        _padX = Ui.Num(0, image.Width, 0, 60).Presets(Ui.Spacings).Unit("px").RecentKey("sheet.spacing");
        _padY = Ui.Num(0, image.Height, 0, 60).Presets(Ui.Spacings).Unit("px").RecentKey("sheet.spacing");
        _cols = Ui.Num(1, image.Width, Math.Max(1, image.Width / Math.Max(1, cellW)), 60).Presets(counts).Unit(UiStrings.S_Dialogs_SplitFramesDialog_C156C2AE58).RecentKey("sheet.count");
        _rows = Ui.Num(1, image.Height, Math.Max(1, image.Height / Math.Max(1, cellH)), 60).Presets(counts).Unit(UiStrings.S_Dialogs_SplitFramesDialog_C156C2AE58).RecentKey("sheet.count");
        _minSize = Ui.Num(1, 1024, 2, 60).Presets(1, 2, 3, 4, 6, 8, 12, 16, 24, 32).Unit("px").RecentKey("autoslice.min");
        _skipEmpty = Ui.Check(UiStrings.S_Dialogs_SplitFramesDialog_B6C30C0EF4, true);
        _result = Ui.Label("");
        Body.Children.Add(Ui.Label(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Dialogs_SplitFramesDialog_B7C2E8311F,image.Width,image.Height)));
        Body.Children.Add(_method);
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_SplitFramesDialog_84FD67B968), _cellW, Ui.Label(UiStrings.S_Dialogs_SplitFramesDialog_5473AAAF60), _cellH));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_SplitFramesDialog_9C87DD306F), _offX, Ui.Label("Y"), _offY));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_SplitFramesDialog_DC3CD0F4DA), _padX, Ui.Label("Y"), _padY));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_SplitFramesDialog_D213350F0D), _cols, Ui.Label(UiStrings.S_Dialogs_SplitFramesDialog_7F8BCA446D), _rows));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_SplitFramesDialog_36ABCCA237), _minSize));
        Body.Children.Add(_skipEmpty);
        Body.Children.Add(_result);
        foreach (var n in new[] { _cellW, _cellH, _offX, _offY, _padX, _padY, _cols, _rows, _minSize })
            n.ValueChanged += (_, _) => UpdateResult();
        _method.SelectionChanged += (_, _) => UpdateResult();
        _skipEmpty.IsCheckedChanged += (_, _) => UpdateResult();
        UpdateResult();
        OkButton.Click += (_, _) =>
        {
            if (Rects.Count > 0) return;
            Ui.Error(this, UiStrings.S_Dialogs_SplitFramesDialog_7AE557EFFF);
            DialogResult = DialogResult.None;
        };
    }

    /// <summary>나눌 칸들(위→아래, 왼쪽→오른쪽).</summary>
    public List<Rectangle> Rects
    {
        get
        {
            switch (_method.SelectedIndex)
            {
                case 1:
                    return Gpixel.Core.Slicing.CountSlicer.Slice(_image, (int)_cols.NumValue(), (int)_rows.NumValue(), _skipEmpty.On());
                case 2:
                    return Gpixel.Core.Slicing.AutoSlicer.Slice(_image, (int)_minSize.NumValue());
                default:
                    var grid = new Gpixel.Core.Model.GridSettings
                    {
                        CellW = (int)_cellW.NumValue(), CellH = (int)_cellH.NumValue(),
                        OffsetX = (int)_offX.NumValue(), OffsetY = (int)_offY.NumValue(),
                        PadX = (int)_padX.NumValue(), PadY = (int)_padY.NumValue(),
                    };
                    return Gpixel.Core.Slicing.GridSlicer.Slice(_image, grid, _skipEmpty.On());
            }
        }
    }

    private void UpdateResult()
    {
        var rects = Rects;
        _result.Text = rects.Count == 0
            ? UiStrings.S_Dialogs_UpdateResult_0CE7149528
            : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Dialogs_UpdateResult_7A4F61BFA8,rects.Count,rects.Max(r => r.Width),rects.Max(r => r.Height));
    }
}

/// <summary>색 칸 + HEX 입력. 색 칸을 누르면 공용 색 편집기와 메인 팔레트가 팝업으로 열린다.</summary>
public sealed class ColorField : StackPanel
{
    private readonly SwatchBox _swatch;
    private readonly TextBox _hex;
    private Color32 _color;

    /// <param name="hexWidth">HEX 입력 칸 너비(좁은 줄에 여럿 놓을 때 줄임).</param>
    public ColorField(Color32 color, int hexWidth = 90)
    {
        Orientation = Orientation.Horizontal;
        Margin = new Thickness(0);
        Background = Brushes.Transparent;
        _swatch = new SwatchBox(this) { Width = 28, Height = 22, Margin = new Thickness(3), Cursor = new Cursor(StandardCursorType.Hand) };
        _swatch.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_swatch).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            var editor = new ColorPickerEditor(Color);
            editor.ColorChanged += (_, _) => Color = editor.Color;
            var flyout = new Flyout
            {
                Content = new Border { Width = 466, Padding = new Thickness(8), Child = editor },
            };
            flyout.FlyoutPresenterClasses.Add("color-picker");
            PopupMenus.Show(_swatch, flyout);
        };
        _hex = Ui.Text("", hexWidth);
        _hex.LostFocus += (_, _) => { if (Color32.TryParseHex((_hex.Text ?? "").Trim(), out var c)) Color = c; else _hex.Text = _color.ToHex(); };
        Children.Add(_swatch);
        Children.Add(_hex);
        Color = color;
    }

    /// <summary>팝업, HEX 입력 또는 코드에서 색이 바뀌었다.</summary>
    public event EventHandler? ColorChanged;

    public Color32 Color
    {
        get => Color32.TryParseHex((_hex.Text ?? "").Trim(), out var c) ? c : _color;
        set
        {
            bool changed = _color != value;
            _color = value;
            _hex.Text = value.ToHex();
            _swatch.InvalidateVisual();
            if (changed) ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>테두리 있는 색 칸(투명 부분은 체커).</summary>
    private sealed class SwatchBox(ColorField owner) : Control
    {
        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            ImageUtil.DrawSwatch(g, r, owner._color);
            g.DrawRectangle(null, AppTheme.Pen(AppTheme.BorderColor), r.Deflate(0.5));
        }
    }
}

/// <summary>색 바꾸기: 한 색을 다른 색으로(선택 영역 / 현재 셀).</summary>
public sealed class ReplaceColorDialog : DialogBase
{
    private readonly ColorField _from, _to;
    private readonly CheckBox _selectionOnly;

    public ReplaceColorDialog(Color32 from, Color32 to, bool hasSelection) : base(UiStrings.S_Dialogs_ReplaceColorDialog_D512F65023, UiStrings.S_Dialogs_ReplaceColorDialog_EDA3D827F9)
    {
        _from = new ColorField(from);
        _to = new ColorField(to);
        _selectionOnly = Ui.Check(UiStrings.S_Dialogs_ReplaceColorDialog_0B7DF97295, hasSelection);
        _selectionOnly.IsEnabled = hasSelection;
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_ReplaceColorDialog_B456BDF62D), _from));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_ReplaceColorDialog_D361ED4C0A), _to));
        Body.Children.Add(_selectionOnly);
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_ReplaceColorDialog_8D2F0F6870));
    }

    public Color32 From => _from.Color;
    public Color32 To => _to.Color;
    public bool SelectionOnly => _selectionOnly.On();
}

/// <summary>격자 설정: 픽셀 격자 색, 타일 격자 크기·색.</summary>
public sealed class GridSettingsDialog : DialogBase
{
    private readonly ColorField _pixelColor, _tileColor;
    private readonly NumericUpDown _tileW, _tileH;

    public GridSettingsDialog(Color32 pixelColor, Size tile, Color32 tileColor) : base(UiStrings.S_Dialogs_GridSettingsDialog_100AEB4ECD)
    {
        _pixelColor = new ColorField(pixelColor);
        _tileColor = new ColorField(tileColor);
        _tileW = Ui.Num(1, 4096, tile.Width, 64).Presets(Ui.TileSizes).Unit("px").RecentKey("tile.size");
        _tileH = Ui.Num(1, 4096, tile.Height, 64).Presets(Ui.TileSizes).Unit("px").RecentKey("tile.size");
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_GridSettingsDialog_32C718E6B0, bold: true));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_GridSettingsDialog_0F749AB890), _pixelColor));
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_GridSettingsDialog_1E51753746, bold: true));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_GridSettingsDialog_8997F6CAB3), _tileW, Ui.Label(UiStrings.S_Dialogs_GridSettingsDialog_934A32A181), _tileH, Ui.Label("px")));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.S_Dialogs_GridSettingsDialog_0F749AB890), _tileColor));
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_GridSettingsDialog_356D2B7E9E));
    }

    public Color32 PixelGridColor => _pixelColor.Color;
    public Color32 TileGridColor => _tileColor.Color;
    public Size TileSize => new((int)_tileW.NumValue(), (int)_tileH.NumValue());
}

/// <summary>한 줄 입력(선택 목록 제안 포함 가능).</summary>
public sealed class InputDialog : DialogBase
{
    private readonly AutoCompleteBox _box;

    public InputDialog(string title, string prompt, string value, IEnumerable<string>? suggestions = null) : base(title)
    {
        _box = new AutoCompleteBox
        {
            Width = 280,
            MinimumPrefixLength = 0,
            FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
            ItemsSource = suggestions?.ToList(),
            Text = value,
        };
        Body.Children.Add(Ui.Label(prompt));
        Body.Children.Add(_box);
        Opened += (_, _) =>
        {
            _box.Focus();
            // 안쪽 TextBox의 글자를 모두 고른다.
            if (_box.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } tb) { tb.Focus(); tb.SelectAll(); }
        };
    }

    public string Value => _box.Text?.Trim() ?? "";

    /// <summary>입력을 받는다. 취소하거나 비었으면 null.</summary>
    public static string? Ask(Visual? owner, string title, string prompt, string value, IEnumerable<string>? suggestions = null)
    {
        var dlg = new InputDialog(title, prompt, value, suggestions);
        return dlg.ShowDialog(owner) == DialogResult.OK && dlg.Value.Length > 0 ? dlg.Value : null;
    }
}

/// <summary>단축키 목록(F1).</summary>
public sealed class ShortcutHelpDialog : DialogBase
{
    public static readonly (string Group, string Key, string Action)[] Shortcuts =
    [
        (UiStrings.S_Dialogs_Shortcuts_7D7F9ACF6D, "Ctrl+N", UiStrings.S_Dialogs_Shortcuts_EFEB5D2162),
        (UiStrings.S_Dialogs_Shortcuts_7D7F9ACF6D, "Ctrl+O", UiStrings.S_Dialogs_Shortcuts_6760ED067E),
        (UiStrings.S_Dialogs_Shortcuts_7D7F9ACF6D, "Ctrl+Shift+O", UiStrings.S_Dialogs_Shortcuts_2B9CAB397B),
        (UiStrings.S_Dialogs_Shortcuts_7D7F9ACF6D, "Ctrl+Shift+U", UiStrings.S_Dialogs_Shortcuts_DB4E346999),
        (UiStrings.S_Dialogs_Shortcuts_7D7F9ACF6D, "Ctrl+S", UiStrings.S_Dialogs_Shortcuts_45F476C257),
        (UiStrings.S_Dialogs_Shortcuts_7D7F9ACF6D, "Ctrl+Shift+S", UiStrings.S_Dialogs_Shortcuts_8B17B51BE3),
        (UiStrings.S_Dialogs_Shortcuts_7D7F9ACF6D, "Ctrl+E", UiStrings.S_Dialogs_Shortcuts_0B183E4E8D),
        (UiStrings.S_Dialogs_Shortcuts_7D7F9ACF6D, "Ctrl+W", UiStrings.S_Dialogs_Shortcuts_8980D9A859),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Ctrl+Z", UiStrings.S_Dialogs_Shortcuts_91284AEEDC),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Ctrl+Y / Ctrl+Shift+Z", UiStrings.S_Dialogs_Shortcuts_E4A0BFBB0C),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Ctrl+,", UiStrings.S_Dialogs_Shortcuts_1565A446F4),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Ctrl+X / Ctrl+C / Ctrl+V", UiStrings.S_Dialogs_Shortcuts_F5FA573A5A),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, Platform.IsMac ? "Delete / ⌫" : "Delete", UiStrings.S_Dialogs_Shortcuts_7E84FD5492),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Ctrl+A / Ctrl+D", UiStrings.S_Dialogs_Shortcuts_5FC87736AC),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Enter / Esc", UiStrings.S_Dialogs_Shortcuts_38B69F2A2F),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Shift+H / Shift+V", UiStrings.S_Dialogs_Shortcuts_B672785A4E),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Ctrl+R / Ctrl+Shift+R", UiStrings.S_Dialogs_Shortcuts_662DA22B3A),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Ctrl+T", UiStrings.S_Dialogs_Shortcuts_E1937C2A7E),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, UiStrings.S_Dialogs_Shortcuts_E1C05D9B32, UiStrings.S_Dialogs_Shortcuts_52A2CB03ED),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, UiStrings.S_Dialogs_Shortcuts_277551CED2, UiStrings.S_Dialogs_Shortcuts_FAE04D0081),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, UiStrings.S_Dialogs_Shortcuts_BD86C3C490, UiStrings.S_Dialogs_Shortcuts_E014D4B8B3),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Ctrl+Alt+O", UiStrings.S_Dialogs_Shortcuts_669BA7D949),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Ctrl+Alt+R", UiStrings.S_Dialogs_Shortcuts_5F294C19DF),
        (UiStrings.S_Dialogs_Shortcuts_145FCAB3DD, "Ctrl+Alt+C", UiStrings.S_Dialogs_Shortcuts_F869736C79),
        (UiStrings.S_Dialogs_Shortcuts_A069FAB880, "Shift+Enter", UiStrings.S_Dialogs_Shortcuts_E5B5E47196),
        (UiStrings.S_Dialogs_Shortcuts_A069FAB880, ", / .", UiStrings.S_Dialogs_Shortcuts_864C7AA661),
        (UiStrings.S_Dialogs_Shortcuts_A069FAB880, "Home / End", UiStrings.S_Dialogs_Shortcuts_D49FC130EF),
        (UiStrings.S_Dialogs_Shortcuts_A069FAB880, "Alt+N / Alt+D", UiStrings.S_Dialogs_Shortcuts_1B6FD3C420),
        (UiStrings.S_Dialogs_Shortcuts_0DA3C79665, "Shift+N", UiStrings.S_Dialogs_Shortcuts_F141C62B2F),
        (UiStrings.S_Dialogs_Shortcuts_0DA3C79665, "Ctrl+Shift+N", UiStrings.S_Dialogs_Shortcuts_E485ABF61D),
        (UiStrings.S_Dialogs_Shortcuts_0DA3C79665, "Shift+P", UiStrings.S_Dialogs_Shortcuts_CD3EC97621),
        (UiStrings.S_Dialogs_Shortcuts_0DA3C79665, UiStrings.S_Dialogs_Shortcuts_C171BDC125, UiStrings.S_Dialogs_Shortcuts_B509609DFC),
        (UiStrings.S_Dialogs_Shortcuts_0DA3C79665, UiStrings.S_Dialogs_Shortcuts_1C35C1C5A5, UiStrings.S_Dialogs_Shortcuts_846D4758F6),
        (UiStrings.S_Dialogs_Shortcuts_5BC0EB1C9D, UiStrings.S_Dialogs_Shortcuts_1801ED1758, UiStrings.S_Dialogs_Shortcuts_57366E3F77),
        (UiStrings.S_Dialogs_Shortcuts_5BC0EB1C9D, UiStrings.S_Dialogs_Shortcuts_D9CD891666, UiStrings.S_Dialogs_Shortcuts_7CE7A346C0),
        (UiStrings.S_Dialogs_Shortcuts_5BC0EB1C9D, UiStrings.S_Dialogs_Shortcuts_1D4D69FEBB, UiStrings.S_Dialogs_Shortcuts_76C0AB65F2),
        (UiStrings.S_Dialogs_Shortcuts_5BC0EB1C9D, UiStrings.S_Dialogs_Shortcuts_744D097883, UiStrings.S_Dialogs_Shortcuts_778F677E82),
        (UiStrings.S_Dialogs_Shortcuts_5BC0EB1C9D, "Ctrl+C / Ctrl+X / Ctrl+V", UiStrings.S_Dialogs_Shortcuts_DD21ABD0E2),
        (UiStrings.S_Dialogs_Shortcuts_5BC0EB1C9D, UiStrings.S_Dialogs_Shortcuts_551C1C1477, UiStrings.S_Dialogs_Shortcuts_4433121F4C),
        (UiStrings.S_Dialogs_Shortcuts_5BC0EB1C9D, UiStrings.S_Dialogs_Shortcuts_6FA30CF6A4, UiStrings.S_Dialogs_Shortcuts_3DBB46587D),
        (UiStrings.S_Dialogs_Shortcuts_BF8A231C98, UiStrings.S_Dialogs_Shortcuts_BA96D2C3EB, UiStrings.S_Dialogs_Shortcuts_699BE4B36E),
        (UiStrings.S_Dialogs_Shortcuts_BF8A231C98, UiStrings.S_Dialogs_Shortcuts_6AF34AD7B6, UiStrings.S_Dialogs_Shortcuts_1C2B1A1E5E),
        (UiStrings.S_Dialogs_Shortcuts_A069FAB880, UiStrings.S_Dialogs_Shortcuts_89EBA10751, UiStrings.S_Dialogs_Shortcuts_F50A780EEA),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "B", UiStrings.S_Dialogs_Shortcuts_8DDFCADA9D),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "E", UiStrings.S_Dialogs_Shortcuts_25E99AD68A),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "G", UiStrings.S_Dialogs_Shortcuts_946C7D8FC7),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "F", UiStrings.S_Dialogs_Shortcuts_0432F72DFF),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, UiStrings.S_Dialogs_Shortcuts_38E4E2F3CE, UiStrings.S_Dialogs_Shortcuts_F8F7C54D54),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "L", UiStrings.S_Dialogs_Shortcuts_63ADB1FB8F),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "R", UiStrings.S_Dialogs_Shortcuts_DAEB4ED541),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "O", UiStrings.S_Dialogs_Shortcuts_495263DD23),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "U", UiStrings.S_Dialogs_Shortcuts_094D63C9FA),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "M", UiStrings.S_Dialogs_Shortcuts_7E24CFF354),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "Q", UiStrings.S_Dialogs_Shortcuts_2C42C899BE),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "W", UiStrings.S_Dialogs_Shortcuts_EDD59A4672),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, UiStrings.S_Dialogs_Shortcuts_FCF789E683, UiStrings.S_Dialogs_Shortcuts_90C65F584D),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, UiStrings.S_Dialogs_Shortcuts_50CAA43D24, UiStrings.S_Dialogs_Shortcuts_4A05EA78E2),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "D", UiStrings.S_Dialogs_Shortcuts_BFE4A309E1),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "T", UiStrings.S_Dialogs_Shortcuts_A171E1ED26),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "C", UiStrings.S_Dialogs_Shortcuts_F2DB2601F2),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "K", UiStrings.S_Dialogs_Shortcuts_F892032BCA),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "[ / ]", UiStrings.S_Dialogs_Shortcuts_A2ACEFDBE7),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "P", UiStrings.S_Dialogs_Shortcuts_113C7D14A1),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "X", UiStrings.S_Dialogs_Shortcuts_6759557E42),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, "Shift+X / Shift+Y", UiStrings.S_Dialogs_Shortcuts_0963C5F4ED),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, UiStrings.S_Dialogs_Shortcuts_8E216B788D, UiStrings.S_Dialogs_Shortcuts_1F0C6D3C83),
        (UiStrings.S_Dialogs_Shortcuts_CBA6A95A08, UiStrings.S_Dialogs_Shortcuts_6DB7F163BF, UiStrings.S_Dialogs_Shortcuts_45DD7D9C62),
        (UiStrings.S_Dialogs_Shortcuts_6657ADE48E, UiStrings.S_Dialogs_Shortcuts_FEE4BB43A7, UiStrings.S_Dialogs_Shortcuts_9A0AAD2E25),
        (UiStrings.S_Dialogs_Shortcuts_CBA6A95A08, UiStrings.S_Dialogs_Shortcuts_033B73C2A4, UiStrings.S_Dialogs_Shortcuts_D663A4CCA6),
        (UiStrings.S_Dialogs_Shortcuts_CBA6A95A08, UiStrings.S_Dialogs_Shortcuts_D1CD5B98A5, UiStrings.S_Dialogs_Shortcuts_30B9A84541),
        (UiStrings.S_Dialogs_Shortcuts_CBA6A95A08, "Ctrl+0 / Ctrl+1", UiStrings.S_Dialogs_Shortcuts_5DB5D4EBD4),
        (UiStrings.S_Dialogs_Shortcuts_CBA6A95A08, "Ctrl+= / Ctrl+-", UiStrings.S_Dialogs_Shortcuts_48AD2BB98E),
        (UiStrings.S_Dialogs_Shortcuts_CBA6A95A08, "Ctrl+G / Ctrl+Shift+G", UiStrings.S_Dialogs_Shortcuts_BC827F884D),
        (UiStrings.S_Dialogs_Shortcuts_CBA6A95A08, "Ctrl+Shift+T", UiStrings.S_Dialogs_Shortcuts_42916C914B),
        (UiStrings.S_Dialogs_Shortcuts_CBA6A95A08, "Ctrl+J", UiStrings.S_Dialogs_Shortcuts_821AC00B5A),
        (UiStrings.S_Dialogs_Shortcuts_CBA6A95A08, "F8", UiStrings.S_Dialogs_Shortcuts_BC85BDFD60),
        (UiStrings.S_Dialogs_Shortcuts_249FBBC3DD, "F5", UiStrings.S_Dialogs_Shortcuts_39DA072B09),
        ("AI", "Ctrl+L", UiStrings.S_Dialogs_Shortcuts_D8744C6D64),
        (UiStrings.S_Dialogs_Shortcuts_8FC1214E44, "F1", UiStrings.S_Dialogs_Shortcuts_6A1571C5A3),
        (UiStrings.S_Dialogs_Shortcuts_3577B85684, UiStrings.S_Dialogs_Shortcuts_F0B0D29737, UiStrings.S_Dialogs_Shortcuts_C65D718314),
    ];

    public ShortcutHelpDialog() : base(UiStrings.S_Dialogs_ShortcutHelpDialog_40F6EB91B0)
    {
        CloseOnly();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,200,270") };
        int row = 0;
        void AddRow(string a, string b, string c, bool bold, IBrush? back)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (back is not null)
            {
                var bg = new Border { Background = back };
                Grid.SetRow(bg, row);
                Grid.SetColumnSpan(bg, 3);
                grid.Children.Add(bg);
            }
            string[] texts = [a, b, c];
            for (int col = 0; col < 3; col++)
            {
                var t = Ui.Label(texts[col], bold);
                t.Margin = new Thickness(4, 2);
                t.TextWrapping = TextWrapping.Wrap;
                Grid.SetRow(t, row);
                Grid.SetColumn(t, col);
                grid.Children.Add(t);
            }
            row++;
        }
        Body.Children.Add(Ui.Label(UiStrings.S_Dialogs_ShortcutHelpDialog_66FDB77042));
        AddRow(UiStrings.S_Dialogs_ShortcutHelpDialog_12C51EAE62, UiStrings.S_Dialogs_ShortcutHelpDialog_378916AAF8, UiStrings.S_Dialogs_ShortcutHelpDialog_DF90340663, true, AppTheme.PanelBrush);
        foreach (var (group, key, action) in Shortcuts)
            AddRow(group, Ui.Shortcut(key), Ui.Shortcut(action), false, null);
        Body.Children.Add(new ScrollViewer
        {
            Content = grid,
            Width = 560,
            Height = 520,
            Background = AppTheme.InputBrush,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        });
    }
}
