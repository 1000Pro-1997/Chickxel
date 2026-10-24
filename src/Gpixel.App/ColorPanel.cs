using System.Globalization;
using Avalonia.Media.Immutable;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

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
    private readonly ColorEditor _editor;
    private readonly ScrollViewer _paletteHost;
    private readonly StackPanel _sectionsPanel;
    private readonly Button _addPalette;
    private readonly List<PaletteSection> _sections = new();
    private PaletteSection? _active;
    private bool _editSecondary;
    private LospecPaletteDialog? _lospec;
    /// <summary>열린 Lospec 창이 색을 넣을 팔레트(그 창을 마지막으로 연 팔레트).</summary>
    private PaletteSection? _lospecTarget;

    /// <summary>팔레트 목록·이름·색·접힘·순서가 바뀜(설정 저장용).</summary>
    public event EventHandler? PalettesChanged;

    /// <summary>팔레트 내용(이름·색·순서·개수)이 바뀜: 바꾸기 전과 후(실행취소용). 접기만 바꾼 것은 알리지 않는다.</summary>
    public event Action<IReadOnlyList<NamedPalette>, IReadOnlyList<NamedPalette>>? PaletteEdited;

    /// <summary>마지막으로 알린(또는 들여온) 팔레트 목록.</summary>
    private IReadOnlyList<NamedPalette> _committed = [];

    /// <summary>상태 표시줄에 보일 알림.</summary>
    public event EventHandler<string>? Message;

    public ColorPanel(EditorContext ctx)
    {
        _ctx = ctx;
        Background = AppTheme.PanelBrush;

        _primarySlot = new ColorSlot(UiStrings.S_ColorPanel_ColorPanel_195645F007) { Selected = true };
        _secondarySlot = new ColorSlot(UiStrings.S_ColorPanel_ColorPanel_0B93355BEE);
        _primarySlot.Click += (_, _) => SetEditTarget(false);
        _secondarySlot.Click += (_, _) => SetEditTarget(true);
        var swap = Ui.IconButton(VectorIcon.Swap, UiStrings.S_ColorPanel_ColorPanel_B61A799223, (_, _) => _ctx.SwapColors(), 26);

        _editor = new ColorEditor(_ctx.Primary, _ctx.RecentColors);
        _editor.ColorChanged += (_, _) => SetCurrent(_editor.Color);
        _editor.RecentColorClicked += OnSwatchClicked;

        // ── 위: 팔레트 제목 + 기본/보조색
        var header = Ui.Header(UiStrings.S_ColorPanel_ColorPanel_B96AEE0468);

        var slots = Ui.Row(_primarySlot, _secondarySlot, swap);
        slots.Height = 48;
        slots.Margin = new Thickness(2, 2, 0, 0);
        swap.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(slots, Dock.Top);

        // ── 가운데: 팔레트 칸들(머리글 + 색 칸) + 새 팔레트 단추(스크롤)
        _addPalette = Ui.Button(VectorIcon.Plus, UiStrings.S_ColorPanel_ColorPanel_C7B5B33FE3, (_, _) => AddPalette()).Tip(UiStrings.S_ColorPanel_ColorPanel_197998B972);
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

        // ── 아래: 메인과 팝업이 똑같이 공유하는 색 편집기
        DockPanel.SetDock(_editor, Dock.Bottom);

        var root = new DockPanel();
        root.Children.Add(header);
        root.Children.Add(slots);
        root.Children.Add(_editor);
        root.Children.Add(_paletteHost);
        Content = root;

        ctx.ColorsChanged += (_, _) => SyncFromContext();
        ctx.RecentColorsChanged += (_, _) => _editor.SetRecent(_ctx.RecentColors);
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

    /// <summary>색 고르기 방식(사각형·벌집·색상환·슬라이더). 팝업과 같은 공용 편집기를 쓴다.</summary>
    public ColorPickerMode PickerMode
    {
        get => _editor.Mode;
        set => _editor.Mode = value;
    }

    private void SyncFromContext()
    {
        _primarySlot.Color = _ctx.Primary;
        _secondarySlot.Color = _ctx.Secondary;
        foreach (var section in _sections) section.Grid.SetMarks(_ctx.Primary, _ctx.Secondary);
        _editor.Color = CurrentColor;
    }

    private void OnSwatchClicked(object? sender, (Color32 Color, MouseButton Button) e)
    {
        if (e.Button == MouseButton.Right) _ctx.Secondary = e.Color;
        else _ctx.Primary = e.Color;
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
        _committed = Palettes;
    }

    /// <summary>
    /// 실행취소·다시 실행: 팔레트 목록을 <paramref name="palettes"/>로 되돌린다. 접힘은 지금 상태(같은 자리의 팔레트)를 따르고,
    /// 활성 팔레트는 같은 자리를 유지한다. 설정 저장 알림(<see cref="PalettesChanged"/>)만 내고 실행취소 기록은 남기지 않는다.
    /// </summary>
    public void RestorePalettes(IReadOnlyList<NamedPalette> palettes)
    {
        int active = Math.Max(0, _sections.IndexOf(Active));
        var collapsed = _sections.Select(s => s.Collapsed).ToArray();
        SetPalettes(palettes.Select((p, i) => p with { Collapsed = i < collapsed.Length ? collapsed[i] : p.Collapsed }));
        _active = _sections[Math.Min(active, _sections.Count - 1)];
        RefreshSections();
        PalettesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameContent(IReadOnlyList<NamedPalette> a, IReadOnlyList<NamedPalette> b)
        => a.Count == b.Count && a.Zip(b).All(p => p.First.Name == p.Second.Name && p.First.Colors.SequenceEqual(p.Second.Colors));

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

    public void SetRecent(IEnumerable<Color32> colors) => _editor.SetRecent(colors);

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
            Item(UiStrings.S_ColorPanel_SectionMenu_6B5F562A33, () => AddToPalette(s, CurrentColor)),
            Item(UiStrings.S_ColorPanel_SectionMenu_E11BC26CCA, () => RemoveFromPalette(s)),
            Item(UiStrings.S_ColorPanel_SectionMenu_99C40336CD, () => SortPalette(s)),
            new Separator(),
            Item(UiStrings.S_ColorPanel_SectionMenu_F614342A09, () => LoadPalette(s)),
            Item(UiStrings.S_ColorPanel_SectionMenu_8C285D8080, () => OpenLospec(s)),
            Item(UiStrings.S_ColorPanel_SectionMenu_39B0AAF262, () => SavePalette(s)),
            Item(UiStrings.S_ColorPanel_SectionMenu_7B7D7BEB97, () => PaletteFromImage(s)),
            Item(UiStrings.S_ColorPanel_SectionMenu_65C4F22AE2, () => ConvertImage(s), _ctx.HasProject),
            new Separator(),
            Item(UiStrings.S_ColorPanel_SectionMenu_64E969B02E, () => RenamePalette(s)),
            Item(UiStrings.S_ColorPanel_SectionMenu_4C7EB2DA21, () => MovePalette(s, -1), index > 0),
            Item(UiStrings.S_ColorPanel_SectionMenu_299E405790, () => MovePalette(s, 1), index < _sections.Count - 1),
            Item(UiStrings.S_ColorPanel_SectionMenu_8DA4CD616C, () => { s.Grid.SetColors([]); s.Grid.Commit(); }, s.Grid.Colors.Count > 0),
            Item(UiStrings.S_ColorPanel_SectionMenu_F324D27E74, () => DeletePalette(s), _sections.Count > 1),
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

    private void RaisePalettesChanged()
    {
        var now = Palettes;
        var before = _committed;
        _committed = now;
        if (!SameContent(before, now)) PaletteEdited?.Invoke(before, now);
        PalettesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>쓰이지 않은 "팔레트 N"(N은 팔레트 수 + 1부터).</summary>
    private string NextPaletteName()
    {
        for (int i = _sections.Count + 1; ; i++)
        {
            var name = string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_ColorPanel_NextPaletteName_497ECFC04A, i);
            if (_sections.All(s => s.PaletteName != name)) return name;
        }
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
        if (!_sections.Contains(s) || InputDialog.Ask(this, UiStrings.S_ColorPanel_RenamePalette_427E33B59C, UiStrings.S_ColorPanel_RenamePalette_068D403D46, s.PaletteName) is not { } name || name == s.PaletteName) return;
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
        if (s.Grid.Colors.Count > 0 && Ui.Ask(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ColorPanel_DeletePalette_0A673C4D6C,s.PaletteName,s.Grid.Colors.Count), UiStrings.S_ColorPanel_DeletePalette_C07F74F11A,
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
        var path = Ui.OpenFile(this, UiStrings.S_ColorPanel_LoadPalette_3310F46ECE, null,
            new FileFilter(UiStrings.S_ColorPanel_LoadPalette_9D3D5672C7, "*.hex", "*.gpl"), new FileFilter(UiStrings.S_ColorPanel_LoadPalette_8142E478F5, "*.*"));
        if (path is null) return;
        try
        {
            var colors = PaletteIO.Load(path);
            if (colors.Count == 0)
            {
                Ui.Error(this, UiStrings.S_ColorPanel_LoadPalette_7D6D623706);
                return;
            }
            s.Grid.SetColors(colors);
            s.Grid.Commit();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ColorPanel_LoadPalette_A14CF681D1,ex.Message));
        }
    }

    private void SavePalette(PaletteSection s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string fileName = new string([.. s.PaletteName.Select(ch => invalid.Contains(ch) ? '_' : ch)]).Trim();
        var path = Ui.SaveFile(this, UiStrings.S_ColorPanel_SavePalette_025163AA2F, null, (fileName.Length > 0 ? fileName : "palette") + ".hex", "hex",
            new FileFilter(UiStrings.S_ColorPanel_SavePalette_836AB9D8EA, "*.hex"));
        if (path is null) return;
        try
        {
            PaletteIO.SaveHex(path, s.Grid.Colors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ColorPanel_SavePalette_86BA21EC2E,ex.Message));
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
            Ui.Info(this, UiStrings.S_ColorPanel_PaletteFromImage_5F29209EAB);
            return;
        }
        if (s.Grid.Colors.Count > 0 && Ui.Ask(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ColorPanel_PaletteFromImage_5BF333D17D,colors.Count,s.PaletteName),
                UiStrings.S_ColorPanel_PaletteFromImage_3B7B772DC0, MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
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
            AppTheme.DrawTextIn(g, UiStrings.S_ColorPanel_Render_7ACAF89E7C, bounds, AppTheme.DimTextBrush, AppTheme.SmallFontSize, center: true);
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
        ToolTip.SetTip(this, i < 0 ? null : shown[i].A == 0 ? UiStrings.S_ColorPanel_OnPointerMoved_D44392062E : shown[i].ToHex());
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
