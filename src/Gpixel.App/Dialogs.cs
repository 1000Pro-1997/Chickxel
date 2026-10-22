using Avalonia.VisualTree;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

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

    public NewImageDialog(int width, int height, string? fileName = null) : base("새 프로젝트", "만들기")
    {
        _w = Ui.Num(1, PixelImage.MaxSize, width, 80).Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        _h = Ui.Num(1, PixelImage.MaxSize, height, 80).Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        _transparent = Ui.Check("배경 투명", true);
        _normal = new RadioButton { GroupName = "start", Content = "일반(레이어 1)", IsChecked = true, Foreground = AppTheme.TextBrush };
        _parts = new RadioButton { GroupName = "start", Content = "부위로 시작(그룹 + 첫 부위 · 예: 머리·몸·팔·다리)", Foreground = AppTheme.TextBrush };
        _partName = Ui.Text("머리", 100);
        _partW = Ui.Num(1, PixelImage.MaxSize, Math.Min(8, width), 70).Presets(Ui.PartSizes).Unit("px").RecentKey("part.size");
        _partH = Ui.Num(1, PixelImage.MaxSize, Math.Min(8, height), 70).Presets(Ui.PartSizes).Unit("px").RecentKey("part.size");
        _partRow = Ui.Row(Ui.Label("   첫 부위"), _partName, Ui.Label("가로"), _partW, Ui.Label("세로"), _partH, Ui.Label("px"));
        _partRow.IsEnabled = false;
        _parts.IsCheckedChanged += (_, _) => _partRow.IsEnabled = _parts.On();
        if (fileName is not null)
        {
            _name = Ui.Text(fileName, 220);
            Body.Children.Add(Ui.Row(Ui.Label("파일 이름"), _name, Ui.Label(SpriteProject.FileExtension)));
        }
        Body.Children.Add(Ui.Row(Ui.Label("캔버스"), Ui.Label("가로"), _w, Ui.Label("세로"), _h, Ui.Label("px")));
        var presets = Ui.Row();
        foreach (var (pw, ph) in new[] { (16, 16), (32, 32), (16, 32), (64, 64), (128, 128), (256, 256) })
            presets.Children.Add(Ui.Button($"{pw}×{ph}", (_, _) => { _w.Value = pw; _h.Value = ph; }));
        Body.Children.Add(presets);
        Body.Children.Add(Ui.Label("시작", bold: true));
        Body.Children.Add(_normal);
        Body.Children.Add(_parts);
        Body.Children.Add(_partRow);
        Body.Children.Add(_transparent);
        Body.Children.Add(Ui.Label("투명이 아니면 보조색으로 채웁니다."));
        OkButton.Click += (_, _) =>
        {
            if (_name is not null && (FileName.Length == 0 || FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                Ui.Error(this, "파일 이름이 비었거나 쓸 수 없는 문자가 있습니다.");
                DialogResult = DialogResult.None;
                return;
            }
            if (StartWithParts && PartName.Length == 0)
            {
                Ui.Error(this, "첫 부위 이름을 입력하세요.");
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

    public NewPartDialog(string name, int width, int height) : base("새 부위", "추가")
    {
        _name = Ui.Text(name, 200);
        _w = Ui.Num(1, PixelImage.MaxSize, width, 80).Presets(Ui.PartSizes).Unit("px").RecentKey("part.size");
        _h = Ui.Num(1, PixelImage.MaxSize, height, 80).Presets(Ui.PartSizes).Unit("px").RecentKey("part.size");
        var names = Ui.Row(Ui.Label("이름"), _name);
        foreach (var n in new[] { "머리", "몸", "팔", "다리" })
            names.Children.Add(Ui.Button(n, (_, _) => _name.Text = n));
        Body.Children.Add(names);
        Body.Children.Add(Ui.Row(Ui.Label("가로"), _w, Ui.Label("세로"), _h, Ui.Label("px")));
        var presets = Ui.Row();
        foreach (var (pw, ph) in new[] { (8, 8), (8, 5), (16, 16), (16, 32), (32, 32) })
            presets.Children.Add(Ui.Button($"{pw}×{ph}", (_, _) => { _w.Value = pw; _h.Value = ph; }));
        Body.Children.Add(presets);
        Body.Children.Add(Ui.Label("이름이 겹치면 _1, _2 …가 붙습니다. 처음 위치는 캔버스 가운데 — 그룹을 골라 끌어 배치하세요."));
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

    public PartOffsetDialog(string partName, Point offset) : base($"부위 위치 — {partName}")
    {
        // 프레임 캔버스 안의 왼쪽 위 위치: 흔한 칸 경계 위주, 조금 밖으로 내민 음수도
        decimal[] positions = [-8, -4, 0, 2, 4, 8, 12, 16, 24, 32, 48, 64];
        _x = Ui.Num(-PixelImage.MaxSize, PixelImage.MaxSize, offset.X, 80).Presets(positions).Unit("px").RecentKey("part.position");
        _y = Ui.Num(-PixelImage.MaxSize, PixelImage.MaxSize, offset.Y, 80).Presets(positions).Unit("px").RecentKey("part.position");
        Body.Children.Add(Ui.Label("프레임 캔버스에서 부위 왼쪽 위 위치(모든 프레임 공통)"));
        Body.Children.Add(Ui.Row(Ui.Label("X"), _x, Ui.Label("Y"), _y, Ui.Label("px")));
    }

    public Point Offset => new((int)_x.NumValue(), (int)_y.NumValue());
}

/// <summary>PNG 가져오기: 칸이 모두 같은 크기일 때 부위 / 애니메이션 프레임 중 고르기.</summary>
public sealed class ImportKindDialog : DialogBase
{
    public ImportKindDialog(string fileName, int sliceCount, Size sliceSize) : base("PNG 가져오기", "부위로")
    {
        Body.Children.Add(Ui.Label($"{fileName}: 칸 {sliceCount}개가 모두 같은 크기({sliceSize.Width}×{sliceSize.Height})입니다.", bold: true));
        Body.Children.Add(Ui.Label("어떻게 가져올까요?"));
        Body.Children.Add(Ui.Label("· 부위로: 그룹 하나에 칸마다 부위 1개(이름·크기·피벗 그대로, 시트의 원래 위치에 배치)"));
        Body.Children.Add(Ui.Label("· 애니메이션 프레임으로: 칸마다 프레임 1개(같은 캔버스, 태그로 애니메이션)"));
        SetButtonResult(OkButton, DialogResult.Yes);
        OkButton.Width = 110;
        var anim = AddButton("애니메이션 프레임으로", DialogResult.No);
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
        Body.Children.Add(Ui.Label($"현재 크기: {width} × {height}"));
        Body.Children.Add(Ui.Row(Ui.Label("가로"), _w, Ui.Label("세로"), _h, Ui.Label("px")));
        Body.Children.Add(Ui.Label("기준점(기존 그림이 놓일 위치)"));
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

    public SplitFramesDialog(Gpixel.Core.Model.PixelImage image, int cellW, int cellH) : base("격자로 나눠 프레임 만들기", "나누기")
    {
        _image = image;
        _method = Ui.Combo(170, "격자 – 칸 크기", "격자 – 칸 수", "자동(덩어리마다)");
        if (_method.SelectedIndex < 0) _method.SelectedIndex = 0;
        decimal[] counts = [1, 2, 3, 4, 5, 6, 8, 10, 12, 16];
        _cellW = Ui.Num(1, image.Width, Math.Min(cellW, image.Width), 60).Presets(Ui.SpriteSizes).Unit("px").RecentKey("cell.size");
        _cellH = Ui.Num(1, image.Height, Math.Min(cellH, image.Height), 60).Presets(Ui.SpriteSizes).Unit("px").RecentKey("cell.size");
        _offX = Ui.Num(0, image.Width - 1, 0, 60).Presets(Ui.Spacings).Unit("px").RecentKey("sheet.margin");
        _offY = Ui.Num(0, image.Height - 1, 0, 60).Presets(Ui.Spacings).Unit("px").RecentKey("sheet.margin");
        _padX = Ui.Num(0, image.Width, 0, 60).Presets(Ui.Spacings).Unit("px").RecentKey("sheet.spacing");
        _padY = Ui.Num(0, image.Height, 0, 60).Presets(Ui.Spacings).Unit("px").RecentKey("sheet.spacing");
        _cols = Ui.Num(1, image.Width, Math.Max(1, image.Width / Math.Max(1, cellW)), 60).Presets(counts).Unit("칸").RecentKey("sheet.count");
        _rows = Ui.Num(1, image.Height, Math.Max(1, image.Height / Math.Max(1, cellH)), 60).Presets(counts).Unit("칸").RecentKey("sheet.count");
        _minSize = Ui.Num(1, 1024, 2, 60).Presets(1, 2, 3, 4, 6, 8, 12, 16, 24, 32).Unit("px").RecentKey("autoslice.min");
        _skipEmpty = Ui.Check("빈 칸 건너뛰기", true);
        _result = Ui.Label("");
        Body.Children.Add(Ui.Label($"현재 프레임 그림({image.Width}×{image.Height})을 칸마다 프레임 하나로 나눕니다."));
        Body.Children.Add(_method);
        Body.Children.Add(Ui.Row(Ui.Label("칸 가로"), _cellW, Ui.Label("세로"), _cellH));
        Body.Children.Add(Ui.Row(Ui.Label("여백 X"), _offX, Ui.Label("Y"), _offY));
        Body.Children.Add(Ui.Row(Ui.Label("간격 X"), _padX, Ui.Label("Y"), _padY));
        Body.Children.Add(Ui.Row(Ui.Label("열"), _cols, Ui.Label("행"), _rows));
        Body.Children.Add(Ui.Row(Ui.Label("자동: 최소 크기"), _minSize));
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
            Ui.Error(this, "만들어질 칸이 없습니다. 설정을 확인하세요.");
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
            ? "만들어질 칸: 없음"
            : $"만들어질 프레임: {rects.Count}개 (캔버스 {rects.Max(r => r.Width)}×{rects.Max(r => r.Height)})";
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
                Content = new Border { Width = 596, Padding = new Thickness(8), Child = editor },
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

    public ReplaceColorDialog(Color32 from, Color32 to, bool hasSelection) : base("색 바꾸기", "바꾸기")
    {
        _from = new ColorField(from);
        _to = new ColorField(to);
        _selectionOnly = Ui.Check("선택 영역 안만", hasSelection);
        _selectionOnly.IsEnabled = hasSelection;
        Body.Children.Add(Ui.Row(Ui.Label("바꿀 색"), _from));
        Body.Children.Add(Ui.Row(Ui.Label("새 색  "), _to));
        Body.Children.Add(_selectionOnly);
        Body.Children.Add(Ui.Label("투명(알파 0)은 모두 같은 색으로 봅니다. 대상: 현재 레이어의 현재 프레임."));
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

    public GridSettingsDialog(Color32 pixelColor, Size tile, Color32 tileColor) : base("격자 설정")
    {
        _pixelColor = new ColorField(pixelColor);
        _tileColor = new ColorField(tileColor);
        _tileW = Ui.Num(1, 4096, tile.Width, 64).Presets(Ui.TileSizes).Unit("px").RecentKey("tile.size");
        _tileH = Ui.Num(1, 4096, tile.Height, 64).Presets(Ui.TileSizes).Unit("px").RecentKey("tile.size");
        Body.Children.Add(Ui.Label("픽셀 격자(6배 이상 확대에서 보임)", bold: true));
        Body.Children.Add(Ui.Row(Ui.Label("색"), _pixelColor));
        Body.Children.Add(Ui.Label("타일 격자", bold: true));
        Body.Children.Add(Ui.Row(Ui.Label("가로"), _tileW, Ui.Label("세로"), _tileH, Ui.Label("px")));
        Body.Children.Add(Ui.Row(Ui.Label("색"), _tileColor));
        Body.Children.Add(Ui.Label("알파(#RRGGBBAA)로 진하기를 정합니다."));
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
        ("파일", "Ctrl+N", "새 프로젝트"),
        ("파일", "Ctrl+O", "열기(.gpixel / PNG 가져오기)"),
        ("파일", "Ctrl+Shift+O", "폴더 열기"),
        ("파일", "Ctrl+Shift+U", "Unity 프로젝트 열기(Unity Hub)"),
        ("파일", "Ctrl+S", "저장(.gpixel) + PNG 시트·.meta 내보내기"),
        ("파일", "Ctrl+Shift+S", "다른 이름으로 저장"),
        ("파일", "Ctrl+E", "내보내기(설정 창 → 파란 내보내기)"),
        ("파일", "Ctrl+W", "닫기"),
        ("편집", "Ctrl+Z", "실행취소(파일 목록에서는 마지막 삭제 되살리기)"),
        ("편집", "Ctrl+Y / Ctrl+Shift+Z", "다시 실행"),
        ("편집", "Ctrl+,", "환경 설정(실행취소 횟수·앱 휴지통 보관 기간)"),
        ("편집", "Ctrl+X / Ctrl+C / Ctrl+V", "잘라내기 / 복사(선택 없으면 프레임 전체) / 붙여넣기"),
        ("편집", Platform.IsMac ? "Delete / ⌫" : "Delete", "선택 내용 지우기"),
        ("편집", "Ctrl+A / Ctrl+D", "모두 선택 / 선택 해제"),
        ("편집", "Enter / Esc", "떠 있는 선택 내려놓기 / 선택 해제"),
        ("편집", "Shift+H / Shift+V", "좌우 / 상하 뒤집기(선택, 없으면 현재 셀)"),
        ("편집", "Ctrl+R / Ctrl+Shift+R", "90° 회전(선택, 없으면 캔버스 전체)"),
        ("편집", "Ctrl+T", "변형 모드(선택, 없으면 현재 셀의 칠한 픽셀): 안쪽 끌기 = 옮기기, 손잡이 = 크기(Shift 비율, Alt 가운데), 바깥 = 회전(Shift 15°)"),
        ("편집", "변형 중 Enter / Esc · Ctrl+Z", "변형 적용(실행취소 한 단계) / 변형 취소"),
        ("편집", "방향키 · Ctrl+Shift+방향키", "선택(이동 도구면 셀 내용) 1픽셀 옮기기"),
        ("편집", "Shift+방향키", "8픽셀 옮기기"),
        ("편집", "Ctrl+Alt+O", "외곽선 추가(기본색)"),
        ("편집", "Ctrl+Alt+R", "색 바꾸기"),
        ("편집", "Ctrl+Alt+C", "캔버스 크기"),
        ("프레임", "Shift+Enter", "재생 / 정지"),
        ("프레임", ", / .", "이전 / 다음 프레임"),
        ("프레임", "Home / End", "첫 / 마지막 프레임(캔버스 포커스)"),
        ("프레임", "Alt+N / Alt+D", "새 빈 프레임 / 프레임 복제"),
        ("레이어", "Shift+N", "새 레이어"),
        ("레이어", "Ctrl+Shift+N", "새 그룹"),
        ("레이어", "Shift+P", "현재 그룹에 새 부위(그룹이 없으면 그룹도)"),
        ("레이어", "F2 / 이름 더블클릭", "레이어 이름 바꾸기"),
        ("레이어", "그룹 이름 앞 삼각형", "그룹 접기 / 펼치기"),
        ("타임라인", "셀 끌기 / Shift+클릭", "여러 칸(행 × 프레임) 고르기"),
        ("타임라인", "머리글 끌기", "프레임 범위 고르기 → 우클릭 \"태그 만들기\""),
        ("타임라인", "태그 상자 안쪽 끝 끌기", "태그 범위 바꾸기"),
        ("타임라인", "고른 칸 끌기 / Alt+끌기", "칸 묶음 옮기기 / 복사"),
        ("타임라인", "Ctrl+C / Ctrl+X / Ctrl+V", "셀·프레임 복사 / 잘라내기 / 붙여넣기(타임라인을 누른 뒤)"),
        ("타임라인", "다음 프레임 칸의 복사 / 더하기 단추", "마지막 프레임 복사해서 추가 / 빈 프레임 추가"),
        ("타임라인", "Delete / 머리글 더블클릭", "고른 프레임(범위) 삭제 / 프레임 길이"),
        ("부위", "그룹 고르고 끌기", "부위 배치(방향키 1px · Shift 8px)"),
        ("부위", "그룹에서 부위 더블클릭", "그 부위 고르기(부위 크기 캔버스로 그리기)"),
        ("프레임", "H (누르고 있기)", "이전 프레임 보기"),
        ("도구", "B", "연필"),
        ("도구", "E", "지우개"),
        ("도구", "G", "채우기 (Shift+G: 같은 색 전체 전환)"),
        ("도구", "F", "그라데이션 (끌어서 시작 → 끝, Shift: 45°, 정지점은 도구 설정)"),
        ("도구", "I (또는 Alt+클릭)", "스포이드"),
        ("도구", "L", "선 (Shift: 45° 단위)"),
        ("도구", "R", "사각형 (Shift: 정사각형)"),
        ("도구", "O", "원 (Shift: 정원)"),
        ("도구", "U", "도형 외곽 / 채움 전환"),
        ("도구", "M", "선택 칸 — 선택 도구를 쓰는 중이면 다음 선택 도구 (사각 → 원 → 자유 → 스마트)"),
        ("도구", "Q", "자유 선택 (끌어서 테두리, 놓으면 닫힘)"),
        ("도구", "W", "스마트 선택 (비슷한 색 영역, 채우기 설정 공유)"),
        ("도구", "도구 칸 길게 누르기 / 오른쪽 클릭", "묶음 도구 펼쳐 바꾸기 (채우기·도형·선택)"),
        ("도구", "V (또는 Ctrl+드래그 — 누르는 동안)", "이동(선택 없으면 셀 전체, 순환 옵션)"),
        ("도구", "D", "밝게(왼쪽) / 어둡게(오른쪽)"),
        ("도구", "T", "디더링(체커 무늬)"),
        ("도구", "C", "색 바꾸기 브러시"),
        ("도구", "K", "피벗 끌기"),
        ("도구", "[ / ]", "브러시 크기 줄이기 / 늘리기"),
        ("도구", "P", "픽셀 퍼펙트 켜기/끄기"),
        ("도구", "X", "기본색 ↔ 보조색 교환"),
        ("도구", "Shift+X / Shift+Y", "좌우 / 상하 대칭 그리기"),
        ("도구", "Shift+클릭(연필류)", "마지막 점에서 직선"),
        ("보기", "휠", "확대 / 축소(1~64배)"),
        ("도구", "Ctrl+휠(캔버스)", "브러시(펜) 크기 늘리기 / 줄이기"),
        ("보기", "휠 / Ctrl+휠(타임라인)", "레이어 위아래 / 프레임 좌우 스크롤"),
        ("보기", "Space+드래그 / 가운데 버튼", "화면 이동"),
        ("보기", "Ctrl+0 / Ctrl+1", "화면에 맞추기 / 실제 크기"),
        ("보기", "Ctrl+= / Ctrl+-", "확대 / 축소"),
        ("보기", "Ctrl+G / Ctrl+Shift+G", "픽셀 격자 / 타일 격자"),
        ("보기", "Ctrl+Shift+T", "타일 모드(5×5 / 큰 그림 3×3 반복)"),
        ("보기", "Ctrl+J", "양파 껍질"),
        ("보기", "F8", "파일 브라우저 보이기/숨기기"),
        ("테스트", "F5", "움직임 테스트"),
        ("AI", "Ctrl+L", "AI 채팅 — Claude·Codex(말로 그리기·고치기)"),
        ("도움말", "F1", "단축키 목록"),
        ("마우스", "왼쪽 / 오른쪽 버튼", "기본색 / 보조색(옵션: 지우개)으로 그리기"),
    ];

    public ShortcutHelpDialog() : base("단축키")
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
        Body.Children.Add(Ui.Label("기본 단축키 목록입니다. 바꾼 단축키는 메뉴 오른쪽에 보이며, 편집 > 환경 설정 > 단축키에서 바꾸고 JSON으로 내보내기·불러오기 합니다."));
        AddRow("분류", "키", "동작", true, AppTheme.PanelBrush);
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
