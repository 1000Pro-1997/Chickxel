using System.Globalization;
using Avalonia.Media.Immutable;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// Aseprite식 왼쪽 열: 위에 기본색/보조색, 가운데 팔레트 여러 개(위아래로 쌓이고 넘치면 스크롤), 아래에 색 고르기
/// (채도·명도 상자 + 색상 막대 + 알파 막대, RGBA·HSV·HEX 입력, 최근 색 16개).
/// 팔레트마다 머리글(접기 ▾/▸, 이름 — 두 번 눌러 바꾸기, ≡ 메뉴)이 있고, 메뉴의 동작(색 추가·삭제·정렬, .hex 저장, .hex/.gpl 불러오기,
/// 이미지에서 만들기, Lospec 웹, 팔레트 색으로 변환, 이름 바꾸기·위아래로·비우기·삭제)은 그 팔레트에 한다.
/// 마지막으로 누르거나 고친 팔레트가 "활성 팔레트"(머리글 아래 파란 줄)이고, 편집 메뉴의 팔레트 명령과 Claude 도구는 활성 팔레트를 쓴다.
/// 투명 칸은 첫 팔레트 맨 앞에만 있다.
/// </summary>
public sealed class ColorPanel : UserControl
{
    private readonly EditorContext _ctx;
    private readonly ColorSlot _primarySlot, _secondarySlot;
    private readonly SvBox _sv;
    private readonly HueBar _hue;
    private readonly AlphaBar _alpha;
    private readonly StackPanel _squarePicker;
    private readonly HoneycombPicker _honeycomb;
    private readonly WheelPicker _wheel;
    private readonly SliderPicker _sliders;
    /// <summary>색 고르기 방식 탭(<see cref="ColorPickerModes.All"/> 순서).</summary>
    private readonly TabStrip _modeTabs = new(TabStripKind.Compact) { Margin = new Thickness(0, 0, 2, 0) };
    private readonly NumericUpDown _r, _g, _b, _a, _hNum, _sNum, _vNum;
    private readonly TextBox _hex;
    private readonly SwatchGrid _recent;
    private readonly ScrollViewer _paletteHost;
    private readonly StackPanel _sectionsPanel;
    private readonly Button _addPalette;
    private readonly List<PaletteSection> _sections = new();
    private PaletteSection? _active;
    private bool _editSecondary;
    private LospecPaletteDialog? _lospec;
    /// <summary>열린 Lospec 창이 색을 넣을 팔레트(그 창을 마지막으로 연 팔레트).</summary>
    private PaletteSection? _lospecTarget;
    private bool _updating;
    private float _h, _s, _v;

    /// <summary>팔레트 목록·이름·색·접힘·순서가 바뀜(설정 저장용).</summary>
    public event EventHandler? PalettesChanged;

    /// <summary>상태 표시줄에 보일 알림.</summary>
    public event EventHandler<string>? Message;

    public ColorPanel(EditorContext ctx)
    {
        _ctx = ctx;
        Background = AppTheme.PanelBrush;

        _primarySlot = new ColorSlot("기본") { Selected = true };
        _secondarySlot = new ColorSlot("보조");
        _primarySlot.Click += (_, _) => SetEditTarget(false);
        _secondarySlot.Click += (_, _) => SetEditTarget(true);
        var swap = Ui.IconButton(VectorIcon.Swap, "기본색 ↔ 보조색 (X)", (_, _) => _ctx.SwapColors(), 26);

        _sv = new SvBox { Width = 170, Height = 110, Margin = new Thickness(3) };
        _hue = new HueBar { Width = 16, Height = 110, Margin = new Thickness(3) };
        _alpha = new AlphaBar { Width = 192, Height = 12, Margin = new Thickness(3) };
        _sv.ValueChanged += (_, _) => { _s = _sv.S; _v = _sv.V; ApplyHsv(); };
        _hue.ValueChanged += (_, _) => { _h = _hue.Hue; _sv.Hue = _h; ApplyHsv(); };
        _alpha.ValueChanged += (_, _) => { if (!_updating) _a!.Value = _alpha.Alpha; };
        _squarePicker = Ui.Row(_sv, _hue);
        _honeycomb = new HoneycombPicker { Margin = new Thickness(3) };
        _honeycomb.ColorPicked += (_, c) => SetCurrent(c.WithAlpha((byte)_a!.NumValue()));
        _wheel = new WheelPicker { Margin = new Thickness(3) };
        _wheel.ValueChanged += (_, _) => { _h = _wheel.Hue; _s = _wheel.S; _v = _wheel.V; ApplyHsv(); };
        _sliders = new SliderPicker { Margin = new Thickness(3) };
        _sliders.RgbPicked += (_, c) => SetCurrent(c.WithAlpha((byte)_a!.NumValue()));
        _sliders.HsvPicked += (_, hsv) => { (_h, _s, _v) = hsv; ApplyHsv(); };
        foreach (var mode in ColorPickerModes.All)
            _modeTabs.Add(null, ColorPickerModes.Icon(mode), $"색 고르기: {ColorPickerModes.Name(mode)}");
        _modeTabs.SelectionChanged += (_, _) => ShowPicker();

        decimal[] channel = [0, 32, 64, 96, 128, 160, 192, 224, 255];
        _r = Ui.Num(0, 255, 0, 44).Presets(channel).RecentKey("color.rgb");
        _g = Ui.Num(0, 255, 0, 44).Presets(channel).RecentKey("color.rgb");
        _b = Ui.Num(0, 255, 0, 44).Presets(channel).RecentKey("color.rgb");
        _a = Ui.Num(0, 255, 255, 44).Presets(channel).RecentKey("color.alpha");
        // 색상환 30° 간격: 빨강 0 · 노랑 60 · 초록 120 · 청록 180 · 파랑 240 · 자홍 300
        _hNum = Ui.Num(0, 360, 0, 44).Presets(0, 30, 60, 90, 120, 150, 180, 210, 240, 270, 300, 330).Unit("°").RecentKey("color.hue");
        _sNum = Ui.Num(0, 100, 0, 44).Presets(Ui.Percents).Unit("%").RecentKey("color.saturation");
        _vNum = Ui.Num(0, 100, 0, 44).Presets(Ui.Percents).Unit("%").RecentKey("color.value");
        foreach (var n in new[] { _r, _g, _b, _a }) n.ValueChanged += (_, _) => ApplyRgba();
        foreach (var n in new[] { _hNum, _sNum, _vNum })
            n.ValueChanged += (_, _) =>
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

        _recent = new SwatchGrid { Width = 196, Height = 30, Cell = 10 };
        _recent.ColorClicked += OnSwatchClicked;

        // ── 위: 팔레트 제목 + 기본/보조색
        var header = Ui.Header("팔레트");

        var slots = Ui.Row(_primarySlot, _secondarySlot, swap);
        slots.Height = 48;
        slots.Margin = new Thickness(2, 2, 0, 0);
        swap.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(slots, Dock.Top);

        // ── 가운데: 팔레트 칸들(머리글 + 색 칸) + 새 팔레트 단추(스크롤)
        _addPalette = Ui.Button(VectorIcon.Plus, "새 팔레트", (_, _) => AddPalette()).Tip("빈 팔레트를 맨 아래에 추가");
        _addPalette.HorizontalAlignment = HorizontalAlignment.Stretch;
        _addPalette.HorizontalContentAlignment = HorizontalAlignment.Center;
        _addPalette.Margin = new Thickness(4, 6, 4, 8);
        _sectionsPanel = new StackPanel { Orientation = Orientation.Vertical };
        _paletteHost = new ScrollViewer
        {
            Content = _sectionsPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = AppTheme.InputBrush,
        };
        SetPalettes([]);

        // ── 아래: 색 고르기(머리글 오른쪽 탭으로 방식 고르기)
        var pickerArea = new Panel { Children = { _squarePicker, _honeycomb, _wheel, _sliders } };
        var picker = Ui.Stack(
            pickerArea,
            _alpha,
            NumberGrid(),
            Ui.Label("최근 색"),
            _recent);
        var colorHeader = Ui.Header("색");
        var colorTitle = colorHeader.Child!;
        colorHeader.Child = null;
        var colorHeaderRow = new DockPanel();
        DockPanel.SetDock(_modeTabs, Dock.Right);
        colorHeaderRow.Children.Add(_modeTabs);
        colorHeaderRow.Children.Add(colorTitle);
        colorHeader.Child = colorHeaderRow;
        PickerMode = ColorPickerMode.Square;
        var pickerHost = new DockPanel { Background = AppTheme.PanelBrush };
        pickerHost.Children.Add(colorHeader);
        pickerHost.Children.Add(picker);
        DockPanel.SetDock(pickerHost, Dock.Bottom);

        var root = new DockPanel();
        root.Children.Add(header);
        root.Children.Add(slots);
        root.Children.Add(pickerHost);
        root.Children.Add(_paletteHost);
        Content = root;

        ctx.ColorsChanged += (_, _) => SyncFromContext();
        ctx.RecentColorsChanged += (_, _) => { _recent.SetColors(_ctx.RecentColors); };
        SyncFromContext();
    }

    private static MenuItem Item(string text, Action action, bool enabled = true)
    {
        var mi = new MenuItem { Header = text, IsEnabled = enabled };
        // 메뉴가 닫힌 뒤에 한다(파일 대화상자·질문 상자가 메뉴 위에 뜨지 않게).
        mi.Click += (_, _) => Dispatcher.UIThread.Post(action);
        return mi;
    }

    private Color32 CurrentColor => _editSecondary ? _ctx.Secondary : _ctx.Primary;

    private void SetCurrent(Color32 c)
    {
        if (_editSecondary) _ctx.Secondary = c;
        else _ctx.Primary = c;
    }

    private void SetEditTarget(bool secondary)
    {
        _editSecondary = secondary;
        _primarySlot.Selected = !secondary;
        _secondarySlot.Selected = secondary;
        SyncFromContext();
    }

    /// <summary>색 고르기 방식(사각형·벌집·색상환·슬라이더). 알파 막대·숫자 칸은 방식과 관계없이 그대로다.</summary>
    public ColorPickerMode PickerMode
    {
        get => _modeTabs.SelectedIndex < 0 ? ColorPickerMode.Square : ColorPickerModes.All[_modeTabs.SelectedIndex];
        set
        {
            _modeTabs.SelectedIndex = Array.IndexOf(ColorPickerModes.All, value);
            ShowPicker();
        }
    }

    private void ShowPicker()
    {
        var mode = PickerMode;
        _squarePicker.IsVisible = mode == ColorPickerMode.Square;
        _honeycomb.IsVisible = mode == ColorPickerMode.Honeycomb;
        _wheel.IsVisible = mode == ColorPickerMode.Wheel;
        _sliders.IsVisible = mode == ColorPickerMode.Sliders;
    }

    private void SyncFromContext()
    {
        if (_updating) return;
        _primarySlot.Color = _ctx.Primary;
        _secondarySlot.Color = _ctx.Secondary;
        foreach (var section in _sections) section.Grid.SetMarks(_ctx.Primary, _ctx.Secondary);
        var c = CurrentColor;
        var (h, s, v) = c.ToHsv();
        // 무채색이면 색상(H)을 유지해 상자가 튀지 않게 한다.
        if (s > 0 && v > 0) _h = h;
        if (v > 0) _s = s;
        _v = v;
        UpdateEditors(c, fromHsv: false);
    }

    private void UpdateEditors(Color32 c, bool fromHsv)
    {
        _updating = true;
        _r.Value = c.R;
        _g.Value = c.G;
        _b.Value = c.B;
        _a.Value = c.A;
        _alpha.Alpha = c.A;
        _alpha.BaseColor = c;
        _hNum.Value = (decimal)Math.Clamp(MathF.Round(_h), 0, 360);
        _sNum.Value = (decimal)Math.Clamp(MathF.Round(_s * 100), 0, 100);
        _vNum.Value = (decimal)Math.Clamp(MathF.Round(_v * 100), 0, 100);
        if (!_hex.IsFocused) _hex.Text = c.ToHex();
        _hue.Hue = _h;
        _sv.Hue = _h;
        if (!fromHsv || !_sv.Dragging) _sv.SetSv(_s, _v);
        _honeycomb.Current = c;
        _wheel.SetHsv(_h, _s, _v);
        _sliders.SetColor(c, _h, _s, _v);
        _updating = false;
    }

    private void ApplyHsv()
    {
        if (_updating) return;
        var c = Color32.FromHsv(_h, _s, _v, (byte)_a.NumValue());
        _updating = true;
        SetCurrent(c);
        _updating = false;
        _primarySlot.Color = _ctx.Primary;
        _secondarySlot.Color = _ctx.Secondary;
        UpdateEditors(c, fromHsv: true);
    }

    private void ApplyRgba()
    {
        if (_updating) return;
        SetCurrent(new Color32((byte)_r.NumValue(), (byte)_g.NumValue(), (byte)_b.NumValue(), (byte)_a.NumValue()));
    }

    private void ApplyHex()
    {
        if (Color32.TryParseHex((_hex.Text ?? "").Trim(), out var c)) SetCurrent(c);
        else _hex.Text = CurrentColor.ToHex();
    }

    private void OnSwatchClicked(object? sender, (Color32 Color, MouseButton Button) e)
    {
        if (e.Button == MouseButton.Right) _ctx.Secondary = e.Color;
        else _ctx.Primary = e.Color;
    }

    /// <summary>R·G·B / H·S·V / A·# 세 줄. 숫자 상자는 패널 폭을 나눠 늘어나 좁아도 잘리지 않는다.</summary>
    private Grid NumberGrid()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
        };
        void Put(Control c, int row, int col, int span = 1)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            Grid.SetColumnSpan(c, span);
            grid.Children.Add(c);
        }
        void Cell(string label, Control box, int row, int col, int span = 1)
        {
            var l = Ui.Label(label);
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(col == 0 ? 2 : 6, 0, 0, 0);
            box.Width = double.NaN;
            box.MinWidth = 0;
            box.HorizontalAlignment = HorizontalAlignment.Stretch;
            box.Margin = new Thickness(3, 2);
            Put(l, row, col);
            Put(box, row, col + 1, span);
        }
        Cell("R", _r, 0, 0); Cell("G", _g, 0, 2); Cell("B", _b, 0, 4);
        Cell("H", _hNum, 1, 0); Cell("S", _sNum, 1, 2); Cell("V", _vNum, 1, 4);
        Cell("A", _a, 2, 0); Cell("#", _hex, 2, 2, span: 3);
        return grid;
    }

    // ───────────── 팔레트 ─────────────
    /// <summary>모든 팔레트(위 → 아래).</summary>
    public IReadOnlyList<NamedPalette> Palettes
        => [.. _sections.Select(s => new NamedPalette(s.PaletteName, [.. s.Grid.Colors], s.Collapsed))];

    /// <summary>팔레트 목록을 통째로 바꾼다(비었으면 빈 "팔레트 1" 하나). 첫 팔레트가 활성 팔레트가 된다. 변경 알림은 내지 않는다.</summary>
    public void SetPalettes(IEnumerable<NamedPalette> palettes)
    {
        _sections.Clear();
        foreach (var p in palettes) _sections.Add(CreateSection(p.Name, p.Colors, p.Collapsed));
        if (_sections.Count == 0) _sections.Add(CreateSection(NextPaletteName(), [], false));
        _active = _sections[0];
        _lospecTarget = null;
        LayoutSections();
    }

    /// <summary>활성 팔레트(마지막으로 누르거나 고친 팔레트)의 색.</summary>
    public IReadOnlyList<Color32> ActivePalette => Active.Grid.Colors;

    /// <summary>활성 팔레트의 색을 바꾼다(변경 알림을 냄).</summary>
    public void SetActivePalette(IEnumerable<Color32> colors)
    {
        var s = Active;
        s.Grid.SetColors(colors);
        s.Grid.Commit();
    }

    /// <summary>이미지에 쓰인 색으로 활성 팔레트를 만든다.</summary>
    public void PaletteFromImage() => PaletteFromImage(Active);

    /// <summary>현재 이미지를 활성 팔레트 색으로 바꾼다(대화상자).</summary>
    public void ConvertImageToActivePalette() => ConvertImage(Active);

    public void SetRecent(IEnumerable<Color32> colors) => _recent.SetColors(colors);

    private PaletteSection Active => _active is { } a && _sections.Contains(a) ? a : _sections[0];

    private PaletteSection CreateSection(string name, IEnumerable<Color32> colors, bool collapsed)
    {
        var s = new PaletteSection(name, colors, collapsed, editable: true);
        var grid = s.Grid;
        var arrow = s.ArrowButton;
        var header = s.Header;
        var title = s.Title;
        var menuButton = s.MenuButton;
        grid.SetMarks(_ctx.Primary, _ctx.Secondary);

        grid.ColorClicked += (sender, e) =>
        {
            SetActive(s);
            OnSwatchClicked(sender, e);
        };
        grid.Colors.CollectionChangedByUser += (_, _) =>
        {
            _active = s;
            RefreshSections();
            RaisePalettesChanged();
        };
        arrow.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(arrow).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            s.Collapsed = !s.Collapsed;
            _active = s;
            RefreshSections();
            RaisePalettesChanged();
        };
        header.PointerPressed += (_, _) => SetActive(s);
        header.DoubleTapped += (_, e) =>
        {
            if (ReferenceEquals(e.Source, arrow)) return;
            e.Handled = true;
            Dispatcher.UIThread.Post(() => RenamePalette(s));
        };
        PopupMenus.Attach(header, () =>
        {
            SetActive(s);
            return SectionMenu(s);
        });
        menuButton.Click += (_, _) =>
        {
            SetActive(s);
            PopupMenus.Show(menuButton, SectionMenu(s), PlacementMode.BottomEdgeAlignedLeft);
        };
        return s;
    }

    private Control[] SectionMenu(PaletteSection s)
    {
        int index = _sections.IndexOf(s);
        return
        [
            Item("현재 색 추가", () => AddToPalette(s, CurrentColor)),
            Item("기본색을 팔레트에서 삭제", () => RemoveFromPalette(s)),
            Item("색상 순으로 정렬", () => SortPalette(s)),
            new Separator(),
            Item("팔레트 불러오기(.hex/.gpl)…", () => LoadPalette(s)),
            Item("팔레트 웹(Lospec)…", () => OpenLospec(s)),
            Item("팔레트 저장(.hex)…", () => SavePalette(s)),
            Item("이미지에서 팔레트 만들기", () => PaletteFromImage(s)),
            Item("팔레트 색으로 변환…", () => ConvertImage(s), _ctx.HasProject),
            new Separator(),
            Item("이름 바꾸기…", () => RenamePalette(s)),
            Item("위로", () => MovePalette(s, -1), index > 0),
            Item("아래로", () => MovePalette(s, 1), index < _sections.Count - 1),
            Item("팔레트 비우기", () => { s.Grid.SetColors([]); s.Grid.Commit(); }, s.Grid.Colors.Count > 0),
            Item("팔레트 삭제", () => DeletePalette(s), _sections.Count > 1),
        ];
    }

    /// <summary>칸을 순서대로 다시 넣는다(투명 칸은 첫 팔레트에만) + 새 팔레트 단추.</summary>
    private void LayoutSections()
    {
        _sectionsPanel.Children.Clear();
        for (int i = 0; i < _sections.Count; i++)
        {
            var s = _sections[i];
            s.Grid.LeadingTransparent = i == 0;
            s.Grid.InvalidateMeasure();
            s.Grid.InvalidateVisual();
            _sectionsPanel.Children.Add(s.Root);
        }
        _sectionsPanel.Children.Add(_addPalette);
        RefreshSections();
    }

    /// <summary>머리글(이름·색 수·접힘·활성 줄)을 지금 상태로.</summary>
    private void RefreshSections()
    {
        var active = Active;
        foreach (var s in _sections)
            s.Refresh(ReferenceEquals(s, active));
    }

    private void SetActive(PaletteSection s)
    {
        if (ReferenceEquals(_active, s)) return;
        _active = s;
        RefreshSections();
    }

    private void RaisePalettesChanged() => PalettesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>쓰이지 않은 "팔레트 N"(N은 팔레트 수 + 1부터).</summary>
    private string NextPaletteName()
    {
        for (int i = _sections.Count + 1; ; i++)
            if (_sections.All(s => s.PaletteName != $"팔레트 {i}")) return $"팔레트 {i}";
    }

    private void AddPalette()
    {
        var s = CreateSection(NextPaletteName(), [], false);
        _sections.Add(s);
        _active = s;
        LayoutSections();
        RaisePalettesChanged();
        Dispatcher.UIThread.Post(() => s.Root.BringIntoView(), DispatcherPriority.Background);
    }

    private void RenamePalette(PaletteSection s)
    {
        if (!_sections.Contains(s) || InputDialog.Ask(this, "팔레트 이름 바꾸기", "이름", s.PaletteName) is not { } name || name == s.PaletteName) return;
        s.PaletteName = name;
        _active = s;
        RefreshSections();
        RaisePalettesChanged();
    }

    private void MovePalette(PaletteSection s, int delta)
    {
        int from = _sections.IndexOf(s), to = from + delta;
        if (from < 0 || to < 0 || to >= _sections.Count) return;
        _sections.RemoveAt(from);
        _sections.Insert(to, s);
        _active = s;
        LayoutSections();
        RaisePalettesChanged();
    }

    private void DeletePalette(PaletteSection s)
    {
        int index = _sections.IndexOf(s);
        if (index < 0 || _sections.Count <= 1) return;
        if (s.Grid.Colors.Count > 0 && Ui.Ask(this, $"팔레트 \"{s.PaletteName}\"({s.Grid.Colors.Count}색)을 삭제할까요?", "팔레트 삭제",
                MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        _sections.RemoveAt(index);
        if (ReferenceEquals(_active, s)) _active = _sections[Math.Min(index, _sections.Count - 1)];
        LayoutSections();
        RaisePalettesChanged();
    }

    private static void AddToPalette(PaletteSection s, Color32 c)
    {
        // 투명은 첫 팔레트 맨 앞에 늘 있다.
        if (c.A == 0 || s.Grid.Colors.Contains(c)) return;
        s.Grid.Colors.Add(c);
        s.Grid.Commit();
    }

    private void RemoveFromPalette(PaletteSection s)
    {
        // 표시(테두리)되는 기본색 칸을 지운다. 맨 앞 투명은 지우지 않는다.
        int i = s.Grid.Colors.IndexOf(_ctx.Primary);
        if (_ctx.Primary.A == 0 || i < 0) return;
        s.Grid.Colors.RemoveAt(i);
        s.Grid.Commit();
    }

    private static void SortPalette(PaletteSection s)
    {
        var sorted = s.Grid.Colors
            .OrderBy(c => c.ToHsv().S < 0.08f ? 1 : 0)
            .ThenBy(c => c.ToHsv().S < 0.08f ? 0 : MathF.Round(c.ToHsv().H / 15f))
            .ThenBy(c => c.ToHsv().V)
            .ToList();
        s.Grid.Colors.Clear();
        s.Grid.Colors.AddRange(sorted);
        s.Grid.Commit();
    }

    private void LoadPalette(PaletteSection s)
    {
        var path = Ui.OpenFile(this, "팔레트 불러오기", null,
            new FileFilter("팔레트", "*.hex", "*.gpl"), new FileFilter("모든 파일", "*.*"));
        if (path is null) return;
        try
        {
            var colors = PaletteIO.Load(path);
            if (colors.Count == 0)
            {
                Ui.Error(this, "파일에서 색을 찾지 못했습니다.");
                return;
            }
            s.Grid.SetColors(colors);
            s.Grid.Commit();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, $"팔레트를 읽지 못했습니다.\n{ex.Message}");
        }
    }

    private void SavePalette(PaletteSection s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string fileName = new string([.. s.PaletteName.Select(ch => invalid.Contains(ch) ? '_' : ch)]).Trim();
        var path = Ui.SaveFile(this, "팔레트 저장", null, (fileName.Length > 0 ? fileName : "palette") + ".hex", "hex",
            new FileFilter("HEX 팔레트", "*.hex"));
        if (path is null) return;
        try
        {
            PaletteIO.SaveHex(path, s.Grid.Colors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, $"팔레트를 저장하지 못했습니다.\n{ex.Message}");
        }
    }

    /// <summary>
    /// Lospec 팔레트 둘러보기 창(모달 아님, 하나만). 이미 열려 있으면 앞으로 가져온다. 창의 "팔레트로 쓰기"·"추가"는
    /// 그 창을 마지막으로 연 팔레트(지워졌으면 활성 팔레트)에 넣는다.
    /// </summary>
    private void OpenLospec(PaletteSection s)
    {
        _lospecTarget = s;
        if (_lospec is { } open)
        {
            open.Activate();
            return;
        }
        PaletteSection Target() => _lospecTarget is { } t && _sections.Contains(t) ? t : Active;
        var dlg = new LospecPaletteDialog(() => Target().Grid.Colors,
            colors =>
            {
                var t = Target();
                t.Grid.SetColors(colors);
                t.Grid.Commit();
            },
            colors =>
            {
                var t = Target();
                int before = t.Grid.Colors.Count;
                foreach (var c in colors)
                    if (!t.Grid.Colors.Contains(c)) t.Grid.Colors.Add(c);
                int added = t.Grid.Colors.Count - before;
                if (added > 0) t.Grid.Commit();
                return added;
            });
        dlg.Closed += (_, _) => _lospec = null;
        _lospec = dlg;
        if (Ui.Owner(this) is { } owner) dlg.Show(owner);
        else dlg.Show();
    }

    /// <summary>프로젝트의 모든 프레임(보이는 레이어 합성)과 부위 셀(캔버스 밖으로 나간 부분 포함)에 쓰인 색으로 팔레트를 만든다.</summary>
    private void PaletteFromImage(PaletteSection s)
    {
        if (_ctx.Project is not { } p) return;
        var colors = new List<Color32>();
        var seen = new HashSet<Color32>();
        void Add(PixelImage image)
        {
            foreach (var c in PaletteIO.ExtractFromImage(image))
                if (colors.Count < 256 && seen.Add(c)) colors.Add(c);
        }
        var parts = p.Layers.Where(l => l.IsPart).ToList();
        for (int i = 0; i < p.Frames.Count && colors.Count < 256; i++)
        {
            Add(p.Flatten(i));
            foreach (var part in parts)
                if (p.GetCel(part, p.Frames[i]) is { } cel) Add(cel);
        }
        if (colors.Count == 0)
        {
            Ui.Info(this, "이미지에 투명이 아닌 색이 없습니다.");
            return;
        }
        if (s.Grid.Colors.Count > 0 && Ui.Ask(this, $"이미지에서 {colors.Count}색을 찾았습니다. 팔레트 \"{s.PaletteName}\"을 바꿀까요?",
                "이미지에서 팔레트 만들기", MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        s.Grid.SetColors(colors);
        s.Grid.Commit();
    }

    private void ConvertImage(PaletteSection s)
    {
        _active = s;
        RefreshSections();
        if (PaletteConvertDialog.Run(_ctx, this, [.. s.Grid.Colors], s.PaletteName) is { } message) Message?.Invoke(this, message);
    }

    // ───────────── 내부 컨트롤 ─────────────

    /// <summary>기본/보조 색 칸.</summary>
    private sealed class ColorSlot : Control
    {
        private readonly string _caption;
        private Color32 _color;
        private bool _selected;

        public ColorSlot(string caption)
        {
            _caption = caption;
            Width = 84;
            Height = 40;
            Margin = new Thickness(3);
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public event EventHandler? Click;

        public Color32 Color { get => _color; set { _color = value; InvalidateVisual(); } }
        public bool Selected { get => _selected; set { _selected = value; InvalidateVisual(); } }

        public override void Render(DrawingContext g)
        {
            double w = Bounds.Width, h = Bounds.Height;
            g.FillRectangle(AppTheme.PanelBrush, new Rect(Bounds.Size));
            var sw = new Rect(2, 2, 34, Math.Max(0, h - 4));
            ImageUtil.DrawSwatch(g, sw, _color);
            g.DrawRectangle(null, _selected ? AppTheme.Pen(AppTheme.AccentColor, 2) : AppTheme.Pen(AppTheme.BorderColor), sw.Deflate(_selected ? 1 : 0.5));
            AppTheme.DrawTextIn(g, _caption, new Rect(38, 2, Math.Max(0, w - 38), h / 2), AppTheme.TextBrush, AppTheme.SmallFontSize);
            AppTheme.DrawTextIn(g, _color.ToHex(), new Rect(38, h / 2, Math.Max(0, w - 38), h / 2), AppTheme.DimTextBrush, AppTheme.SmallFontSize);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Click?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>채도(가로)·명도(세로) 상자.</summary>
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

        public override void Render(DrawingContext g) => PickerPaint.DrawSvSquare(g, new Rect(Bounds.Size), _hue, _s, _v);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            if (!pt.Properties.IsLeftButtonPressed) return;
            Dragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            Pick(pt.Position);
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

        private void Pick(Avalonia.Point p)
        {
            _s = (float)Math.Clamp(p.X / Math.Max(1, Bounds.Width - 1), 0, 1);
            _v = 1 - (float)Math.Clamp(p.Y / Math.Max(1, Bounds.Height - 1), 0, 1);
            InvalidateVisual();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>가로 알파 막대(체커 위에 현재 색의 투명→불투명).</summary>
    private sealed class AlphaBar : Control
    {
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

        /// <summary>양 끝 여백: 0·255에서도 표시가 잘리지 않게.</summary>
        private const double Pad = 4;

        private Rect BarRect => new(Pad, 0, Math.Max(1, Bounds.Width - 2 * Pad), Bounds.Height);

        public override void Render(DrawingContext g)
        {
            var rect = BarRect;
            AppTheme.DrawChecker(g, rect, 4);
            _gradient ??= new ImmutableLinearGradientBrush(
                [new ImmutableGradientStop(0, _base.WithAlpha(0).ToColor()), new ImmutableGradientStop(1, _base.WithAlpha(255).ToColor())],
                startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(1, 0, RelativeUnit.Relative));
            g.FillRectangle(_gradient, rect);
            double x = rect.X + _alpha / 255.0 * (rect.Width - 1);
            g.DrawRectangle(null, PickerPaint.WhiteMarker, new Rect(x - 2, 1, 4, Math.Max(0, rect.Height - 3)));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            if (!pt.Properties.IsLeftButtonPressed) return;
            _drag = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            Pick(pt.Position.X);
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

    /// <summary>세로 색상(H) 막대.</summary>
    private sealed class HueBar : Control
    {
        private static readonly IBrush Spectrum = new ImmutableLinearGradientBrush(
            Enumerable.Range(0, 7)
                .Select(i => new ImmutableGradientStop(i / 6.0, i == 6 ? Colors.Red : Color32.FromHsv(i * 60 % 360, 1, 1).ToColor()))
                .ToArray(),
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

        /// <summary>위아래 여백: 0°·360°에서도 표시가 잘리지 않게.</summary>
        private const double Pad = 4;

        private Rect BarRect => new(0, Pad, Bounds.Width, Math.Max(1, Bounds.Height - 2 * Pad));

        public override void Render(DrawingContext g)
        {
            var rect = BarRect;
            g.FillRectangle(Spectrum, rect);
            double y = rect.Y + _hue / 360.0 * (rect.Height - 1);
            g.DrawRectangle(null, PickerPaint.WhiteMarker, new Rect(1, y - 2, Math.Max(0, rect.Width - 3), 4));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            if (!pt.Properties.IsLeftButtonPressed) return;
            _drag = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            Pick(pt.Position.Y);
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

/// <summary>이름 있는 팔레트(색 목록 + 팔레트 열에서 접혔는지).</summary>
public sealed record NamedPalette(string Name, IReadOnlyList<Color32> Colors, bool Collapsed = false);

/// <summary>색 목록(사용자가 바꾸면 이벤트를 내는 List).</summary>
public sealed class ColorList : List<Color32>
{
    public event EventHandler? CollectionChangedByUser;
    public void RaiseChanged() => CollectionChangedByUser?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 색 칸 격자(최근 색·팔레트). 왼쪽 클릭 = 기본색, 오른쪽 = 보조색.
/// <see cref="ShowMarks"/>면 기본색과 같은 칸은 흰 테두리, 보조색과 같은 칸은 파란 테두리, 둘 다 같은 칸은 왼쪽 흰·오른쪽 파랑 반반 테두리
/// (색이 같은 칸이 없으면 표시도 없음). <see cref="LeadingTransparent"/>면 투명 칸이 늘 맨 앞에 있다(목록에는 저장하지 않음).
/// </summary>
public sealed class SwatchGrid : Control
{
    private static readonly IPen PrimaryPen = new ImmutablePen(new ImmutableSolidColorBrush(Avalonia.Media.Colors.White), 2);
    private static readonly IPen SecondaryPen = new ImmutablePen(new ImmutableSolidColorBrush(AppTheme.AccentColor), 2);
    private static readonly IPen OutlinePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(20, 20, 24)), 1);
    private int _hoverIndex = -1;
    private Color32? _primaryMark, _secondaryMark;

    public SwatchGrid()
    {
        Margin = new Thickness(3);
    }

    public ColorList Colors { get; } = new();
    public int Cell { get; set; } = 16;
    /// <summary>기본색·보조색과 같은 칸에 테두리를 그린다.</summary>
    public bool ShowMarks { get; set; }
    /// <summary>투명 칸을 늘 맨 앞에 보인다(<see cref="Colors"/>에 든 투명은 따로 보이지 않음).</summary>
    public bool LeadingTransparent { get; set; }

    /// <summary>
    /// 참이면 (고정 크기가 아닐 때) 너비는 주어진 공간에 맞추고 높이는 모든 칸이 보이는 만큼 잡는다
    /// (스크롤 영역 안 팔레트용).
    /// </summary>
    public bool FillHeight { get; set; }

    public event EventHandler<(Color32 Color, MouseButton Button)>? ColorClicked;

    /// <summary>화면에 보이는 칸들(맨 앞 투명 포함).</summary>
    private IReadOnlyList<Color32> Shown
        => LeadingTransparent ? [Color32.Transparent, .. Colors.Where(c => c.A > 0)] : Colors;

    public void SetColors(IEnumerable<Color32> colors)
    {
        Colors.Clear();
        Colors.AddRange(colors);
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>기본색·보조색 표시를 바꾼다.</summary>
    public void SetMarks(Color32 primary, Color32 secondary)
    {
        if (_primaryMark == primary && _secondaryMark == secondary) return;
        _primaryMark = primary;
        _secondaryMark = secondary;
        InvalidateVisual();
    }

    /// <summary>사용자 변경을 알리고 다시 그린다.</summary>
    public void Commit()
    {
        Colors.RaiseChanged();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private int Columns => Math.Max(1, ((int)Bounds.Width - 2) / (Cell + 2));

    /// <summary>주어진 너비에서 모든 칸을 보이는 데 필요한 높이.</summary>
    public int RequiredHeight(int width)
    {
        int cols = Math.Max(1, (width - 2) / (Cell + 2));
        return 2 + (Shown.Count + cols - 1) / cols * (Cell + 2);
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        if (!FillHeight) return base.MeasureOverride(availableSize);
        int w = double.IsInfinity(availableSize.Width) ? 40 : Math.Max(40, (int)availableSize.Width);
        double h = RequiredHeight(w);
        // 스크롤 영역(세로 무한)이 아니면 남는 높이도 채운다.
        if (!double.IsInfinity(availableSize.Height)) h = Math.Max(h, availableSize.Height);
        return new Avalonia.Size(w, h);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (FillHeight && e.WidthChanged) InvalidateMeasure();
    }

    private Rect CellRect(int i)
    {
        int cols = Columns;
        return new Rect(1 + i % cols * (Cell + 2), 1 + i / cols * (Cell + 2), Cell, Cell);
    }

    private int IndexAt(Avalonia.Point p, int count)
    {
        for (int i = 0; i < count; i++)
            if (CellRect(i).Inflate(1).Contains(p)) return i;
        return -1;
    }

    /// <summary>표시용 색 비교: 완전 투명끼리는 RGB와 관계없이 같다.</summary>
    private static bool Same(Color32 a, Color32? b) => b is { } c && (a == c || (a.A == 0 && c.A == 0));

    public override void Render(DrawingContext g)
    {
        var bounds = new Rect(Bounds.Size);
        g.FillRectangle(AppTheme.InputBrush, bounds);
        var shown = Shown;
        if (shown.Count == 0)
        {
            AppTheme.DrawTextIn(g, "비어 있음", bounds, AppTheme.DimTextBrush, AppTheme.SmallFontSize, center: true);
            return;
        }
        // 같은 색이 여러 칸이면 첫 칸에만 표시한다.
        int primary = -1, secondary = -1;
        if (ShowMarks)
            for (int i = 0; i < shown.Count; i++)
            {
                if (primary < 0 && Same(shown[i], _primaryMark)) primary = i;
                if (secondary < 0 && Same(shown[i], _secondaryMark)) secondary = i;
            }
        using (g.PushClip(bounds))
        {
            for (int i = 0; i < shown.Count; i++)
            {
                var r = CellRect(i);
                if (r.Top > bounds.Height) break;
                ImageUtil.DrawSwatch(g, r, shown[i]);
                if (i == _hoverIndex && i != primary && i != secondary)
                    g.DrawRectangle(null, AppTheme.Pen(AppTheme.AccentColor), r.Inflate(0.5));
            }
            if (primary >= 0 && primary == secondary) DrawHalfMark(g, CellRect(primary).Inflate(1));
            else
            {
                if (primary >= 0) DrawMark(g, CellRect(primary).Inflate(1), PrimaryPen);
                if (secondary >= 0) DrawMark(g, CellRect(secondary).Inflate(1), SecondaryPen);
            }
        }
    }

    private static void DrawMark(DrawingContext g, Rect r, IPen pen)
    {
        g.DrawRectangle(null, OutlinePen, r.Inflate(1.5));
        g.DrawRectangle(null, pen, r);
    }

    /// <summary>기본색 = 보조색인 칸: 왼쪽 절반 흰 테두리, 오른쪽 절반 파란 테두리.</summary>
    private static void DrawHalfMark(DrawingContext g, Rect r)
    {
        g.DrawRectangle(null, OutlinePen, r.Inflate(1.5));
        double mid = r.X + r.Width / 2;
        void Path(IPen pen, double x0, double x1, double edgeX)
        {
            g.DrawLine(pen, new Avalonia.Point(x0, r.Top), new Avalonia.Point(x1, r.Top));
            g.DrawLine(pen, new Avalonia.Point(x0, r.Bottom), new Avalonia.Point(x1, r.Bottom));
            g.DrawLine(pen, new Avalonia.Point(edgeX, r.Top), new Avalonia.Point(edgeX, r.Bottom));
        }
        Path(PrimaryPen, r.Left - 1, mid, r.Left);
        Path(SecondaryPen, mid, r.Right + 1, r.Right);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var shown = Shown;
        int i = IndexAt(e.GetPosition(this), shown.Count);
        if (i == _hoverIndex) return;
        _hoverIndex = i;
        ToolTip.SetTip(this, i < 0 ? null : shown[i].A == 0 ? "투명" : shown[i].ToHex());
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverIndex = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        var shown = Shown;
        int i = IndexAt(pt.Position, shown.Count);
        if (i < 0) return;
        var button = pt.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.RightButtonPressed => MouseButton.Right,
            PointerUpdateKind.MiddleButtonPressed => MouseButton.Middle,
            _ => MouseButton.Left,
        };
        e.Handled = true;
        ColorClicked?.Invoke(this, (shown[i], button));
    }
}

internal static class ColorParse
{
    public static Color32 ParseOr(string? hex, Color32 fallback)
        => Color32.TryParseHex(hex, out var c) ? c : fallback;

    public static string Format(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
