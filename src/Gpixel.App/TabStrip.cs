using Avalonia.Media.Immutable;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary><see cref="TabStrip"/> 모양(크기·쓰임새). 색·마우스·키보드 동작은 모두 같다.</summary>
public enum TabStripKind
{
    /// <summary>대화상자 페이지 탭(<see cref="TabView"/>): 큰 글자, 줄 아래 구분선. 고른 탭 = 밝은 바탕 + 강조색 밑줄.</summary>
    Page,
    /// <summary>패널 머리글의 작은 탭(그림만 둬도 됨): 고른 탭 = 페이지 탭과 같은 밝은 바탕 + 강조색 밑줄. 클릭해도 포커스를 가져가지 않는다.</summary>
    Compact,
    /// <summary>값 고르기(칸을 붙인 둥근 묶음, 칸 사이 구분선): 고른 칸 = 강조색 바탕 + 흰 글자.</summary>
    Segmented,
}

/// <summary>탭 그림 그리기: <paramref name="area"/> 안에 <paramref name="brush"/> 색으로(고름·마우스 올림·끔에 따라 색이 바뀐다).</summary>
public delegate void TabIconPainter(DrawingContext g, Rect area, IImmutableSolidColorBrush brush);

/// <summary>
/// 앱 공통 탭 줄: 대화상자 페이지 탭·패널 머리글 탭·값 고르기 묶음이 모두 이것을 쓴다(<see cref="TabStripKind"/>로 크기·모양만 다름).
/// 탭마다 글자 그리고/또는 그림(<see cref="Add"/>), 글자 바꾸기(<see cref="SetText"/>), 도움말(<see cref="SetTip"/>), 숨기기(<see cref="SetTabVisible"/>).
/// 마우스 올리면 밝게, 클릭하면 고른다. 키보드: 포커스가 있으면 ←/→·Home/End, Ctrl+Tab / Ctrl+Shift+Tab(돌아가며).
/// 줄 오른쪽 끝에 다른 컨트롤을 둘 수 있다(<see cref="Trailing"/>, 페이지·작은 탭).
/// <see cref="SelectedIndex"/>가 바뀌면(코드로 바꿔도) <see cref="SelectionChanged"/> — 콤보 상자와 같다.
/// </summary>
public sealed class TabStrip : Border
{
    private static readonly IBrush Clear = Brushes.Transparent;
    private static readonly IImmutableSolidColorBrush White = AppTheme.Brush(Colors.White);
    /// <summary>페이지·작은 탭에 마우스를 올렸을 때 바탕(고른 탭의 바탕보다 옅게 — 어느 바탕 위에서도).</summary>
    private static readonly IBrush TabHover = AppTheme.Brush(Color.FromArgb(18, 255, 255, 255));
    /// <summary>값 고르기 묶음의 바깥 모서리 반지름(앱의 다른 단추와 같은 둥근 사각형).</summary>
    private const double SegmentRadius = 4;

    private sealed class Tab(Border box, TextBlock text, TextBlock? reserve, TabIcon? icon)
    {
        public readonly Border Box = box;
        public readonly TextBlock Text = text;
        /// <summary>굵은 글자 너비를 잡아 두는 보이지 않는 글자(글자 없는 탭은 null).</summary>
        public readonly TextBlock? Reserve = reserve;
        public readonly TabIcon? Icon = icon;
    }

    /// <summary>탭 그림(<see cref="TabIconPainter"/>를 지금 글자색으로 그림).</summary>
    private sealed class TabIcon(TabIconPainter paint) : Control
    {
        public IImmutableSolidColorBrush Brush = AppTheme.TextBrush;
        public override void Render(DrawingContext g) => paint(g, new Rect(Bounds.Size), Brush);
    }

    private readonly List<Tab> _tabs = new();
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly DockPanel _dock = new() { LastChildFill = true };
    private Control? _trailing;
    private int _selected = -1;
    private int _hover = -1;
    private bool _focusRing;

    /// <param name="kind">모양(크기).</param>
    /// <param name="labels">처음 탭 글자들(나중에 <see cref="Add"/>로 더 넣어도 된다).</param>
    public TabStrip(TabStripKind kind, params string[] labels)
    {
        Kind = kind;
        _dock.Children.Add(_row);
        Child = _dock;
        Background = Clear;
        switch (kind)
        {
            case TabStripKind.Page:
                BorderThickness = new Thickness(0, 0, 0, 1);
                HorizontalAlignment = HorizontalAlignment.Stretch;
                Focusable = true;
                break;
            case TabStripKind.Compact:
                VerticalAlignment = VerticalAlignment.Center;
                break;
            default:
                BorderThickness = new Thickness(1);
                CornerRadius = new CornerRadius(SegmentRadius);
                Margin = new Thickness(3);
                HorizontalAlignment = HorizontalAlignment.Left;
                VerticalAlignment = VerticalAlignment.Center;
                Focusable = true;
                break;
        }
        IsTabStop = Focusable;
        // 포커스 표시는 직접 그린다(강조색 테두리/밑선) — 기본 흰 포커스 사각형은 끈다.
        FocusAdorner = null;
        Cursor = new Cursor(StandardCursorType.Hand);
        foreach (var label in labels) Add(label);
        UpdateVisuals();
    }

    public TabStripKind Kind { get; }

    /// <summary>탭 수(숨긴 탭 포함).</summary>
    public int Count => _tabs.Count;

    /// <summary>고른 탭 번호(−1 = 아무것도 고르지 않음). 바뀌면 <see cref="SelectionChanged"/>.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int v = Math.Clamp(value, -1, _tabs.Count - 1);
            if (v == _selected) return;
            _selected = v;
            UpdateVisuals();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>고른 탭이 바뀜(사용자 조작이든 코드든).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>탭 줄 오른쪽 끝에 붙는 컨트롤(예: 내보내기 설정 창의 "프리셋 ▾"). null = 없음.</summary>
    public Control? Trailing
    {
        get => _trailing;
        set
        {
            if (_trailing is not null) _dock.Children.Remove(_trailing);
            _trailing = value;
            if (value is null) return;
            DockPanel.SetDock(value, Dock.Right);
            value.VerticalAlignment = VerticalAlignment.Center;
            _dock.Children.Insert(0, value);
        }
    }

    /// <summary>
    /// 탭 하나를 끝에 더한다(글자·그림 중 하나는 있어야 함). 돌려주는 값 = 탭 번호.
    /// <paramref name="iconAfter"/>면 그림이 글자 뒤의 작은 표시(예: 작업 중 점, <see cref="SetIconVisible"/>로 켜고 끔).
    /// </summary>
    public int Add(string? text, TabIconPainter? icon = null, string? tip = null, bool iconAfter = false)
    {
        if (text is null && icon is null) throw new ArgumentException(UiStrings.S_TabStrip_Add_0D472C8D06, nameof(text));
        int index = _tabs.Count;
        var (fontSize, height, padding, minWidth) = Kind switch
        {
            TabStripKind.Page => (13.0, 28.0, new Thickness(14, 0), 60.0),
            TabStripKind.Compact => (AppTheme.SmallFontSize, 20.0, new Thickness(text is null ? 4 : 6, 0), 26.0),
            _ => (AppTheme.FontSize, 20.0, new Thickness(10, 0), 40.0),
        };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        TabIcon? iconView = null;
        if (icon is not null)
        {
            // 글자 옆 그림은 좁게(글자 기호 한 자 폭), 그림만 있는 탭은 넓게
            iconView = new TabIcon(icon) { Width = iconAfter ? 10 : text is null ? 16 : 12, Height = 14, VerticalAlignment = VerticalAlignment.Center };
            if (!iconAfter) content.Children.Add(iconView);
        }
        var label = new TextBlock { Text = text, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center };
        TextBlock? reserve = null;
        if (text is not null)
        {
            // 고른 탭은 굵게 — 굵은 글자 너비를 미리 잡아 두어 고를 때 탭 너비가 흔들리지 않게 한다.
            reserve = new TextBlock { Text = text, FontSize = fontSize, FontWeight = FontWeight.SemiBold, Opacity = 0, IsHitTestVisible = false };
            content.Children.Add(new Panel { Children = { reserve, label }, VerticalAlignment = VerticalAlignment.Center });
        }
        if (iconAfter && iconView is not null) content.Children.Add(iconView);
        var box = new Border
        {
            Child = content,
            Height = height,
            MinWidth = minWidth,
            Padding = padding,
            Background = Clear,
            BorderBrush = Kind == TabStripKind.Segmented ? AppTheme.BorderBrush : Clear,
            // 페이지 탭 밑줄이 탭 줄 아래 구분선 위에 겹치게 한 칸 내린다.
            Margin = Kind == TabStripKind.Page ? new Thickness(0, 0, 2, -1) : default,
            CornerRadius = Kind == TabStripKind.Segmented ? default : new CornerRadius(3, 3, 0, 0),
        };
        if (Kind != TabStripKind.Segmented) box.BorderThickness = new Thickness(0, 0, 0, 2);
        box.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
            if (Focusable) Focus();
            SelectedIndex = index;
            e.Handled = true;
        };
        box.PointerEntered += (_, _) => { _hover = index; UpdateVisuals(); };
        box.PointerExited += (_, _) => { if (_hover == index) _hover = -1; UpdateVisuals(); };
        box.Tip(tip);
        _tabs.Add(new Tab(box, label, reserve, iconView));
        _row.Children.Add(box);
        UpdateVisuals();
        return index;
    }

    /// <summary>탭 하나의 도움말 풍선(null = 탭 줄 전체 도움말을 씀).</summary>
    public TabStrip SetTip(int index, string? tip)
    {
        _tabs[index].Box.Tip(tip);
        return this;
    }

    /// <summary>글자가 있는 탭의 글자를 바꾼다(예: 작업 중 표시).</summary>
    public TabStrip SetText(int index, string text)
    {
        var tab = _tabs[index];
        if (tab.Reserve is not { } reserve) throw new InvalidOperationException(UiStrings.S_TabStrip_SetText_73932E4743);
        reserve.Text = text;
        tab.Text.Text = text;
        return this;
    }

    /// <summary>그림이 있는 탭의 그림을 보이거나 감춘다(예: 글자 뒤 작업 중 점).</summary>
    public TabStrip SetIconVisible(int index, bool visible)
    {
        if (_tabs[index].Icon is not { } icon) throw new InvalidOperationException(UiStrings.S_TabStrip_SetIconVisible_5D94F67133);
        icon.IsVisible = visible;
        return this;
    }

    public bool IsTabVisible(int index) => _tabs[index].Box.IsVisible;

    /// <summary>탭을 숨기거나 보인다. 고른 탭을 숨기면 보이는 첫 탭을 고른다.</summary>
    public void SetTabVisible(int index, bool visible)
    {
        var box = _tabs[index].Box;
        if (box.IsVisible == visible) return;
        box.IsVisible = visible;
        if (!visible && _selected == index) SelectedIndex = _tabs.FindIndex(t => t.Box.IsVisible);
        UpdateVisuals();
    }

    /// <summary>보이는 탭을 <paramref name="delta"/>만큼 돌아가며 고른다(끝 다음은 처음). 고를 탭이 없으면 false.</summary>
    public bool Cycle(int delta)
    {
        int n = _tabs.Count;
        if (n == 0) return false;
        int i = _selected < 0 ? (delta > 0 ? -1 : 0) : _selected;
        for (int step = 0; step < n; step++)
        {
            i = ((i + Math.Sign(delta)) % n + n) % n;
            if (!_tabs[i].Box.IsVisible) continue;
            SelectedIndex = i;
            return true;
        }
        return false;
    }

    /// <summary>보이는 탭 중 <paramref name="from"/>에서 <paramref name="dir"/> 방향으로 가장 가까운 탭(끝에서 멈춤, 없으면 −1).</summary>
    private int NextVisible(int from, int dir)
    {
        for (int i = from; i >= 0 && i < _tabs.Count; i += dir)
            if (_tabs[i].Box.IsVisible) return i;
        return -1;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.Control)
        {
            Cycle(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None) return;
        int next = e.Key switch
        {
            Key.Left => NextVisible(_selected < 0 ? 0 : _selected - 1, -1),
            Key.Right => NextVisible(_selected < 0 ? 0 : _selected + 1, 1),
            Key.Home => NextVisible(0, 1),
            Key.End => NextVisible(_tabs.Count - 1, -1),
            _ => -2,
        };
        if (next == -2) return;
        if (next >= 0) SelectedIndex = next;
        e.Handled = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        // 키보드로 들어왔을 때만 포커스 테두리(클릭할 때마다 테두리가 번쩍이지 않게)
        _focusRing = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        UpdateVisuals();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _focusRing = false;
        UpdateVisuals();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        bool enabled = IsEffectivelyEnabled;
        BorderBrush = _focusRing && enabled ? AppTheme.AccentBrush : AppTheme.BorderBrush;
        int first = NextVisible(0, 1), last = NextVisible(_tabs.Count - 1, -1);
        const double r = SegmentRadius - 1;
        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            bool selected = i == _selected, hover = enabled && i == _hover;
            IImmutableSolidColorBrush fg;
            if (Kind == TabStripKind.Segmented)
            {
                // 끈 상태는 흐린 색으로(줄째 흐리게 하는 Opacity와 겹쳐 너무 어두워지지 않게 투명도는 쓰지 않음)
                tab.Box.Background = selected ? (enabled ? AppTheme.AccentBrush : AppTheme.HoverBrush) : hover ? AppTheme.HoverBrush : Clear;
                fg = !enabled ? AppTheme.DimTextBrush : selected ? White : AppTheme.TextBrush;
                bool isFirst = i == first, isLast = i == last;
                tab.Box.BorderThickness = new Thickness(isFirst ? 0 : 1, 0, 0, 0);
                // 안쪽 칸 모서리 = 바깥 테두리 안쪽 반지름(바탕색이 둥근 모서리 밖으로 나가지 않게)
                tab.Box.CornerRadius = new CornerRadius(isFirst ? r : 0, isLast ? r : 0, isLast ? r : 0, isFirst ? r : 0);
            }
            else
            {
                tab.Box.Background = selected ? AppTheme.InputBrush : hover ? TabHover : Clear;
                tab.Box.BorderBrush = selected ? (enabled ? AppTheme.AccentBrush : AppTheme.DimTextBrush) : Clear;
                fg = enabled && (selected || hover) ? AppTheme.TextBrush : AppTheme.DimTextBrush;
            }
            tab.Text.Foreground = fg;
            tab.Text.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
            if (tab.Icon is { } icon && !ReferenceEquals(icon.Brush, fg))
            {
                icon.Brush = fg;
                icon.InvalidateVisual();
            }
        }
    }
}

/// <summary>
/// 페이지 탭 상자: 위에 <see cref="TabStrip"/>(<see cref="TabStripKind.Page"/>), 아래에 고른 페이지 하나.
/// 창 안 어디에 포커스가 있든 Ctrl+Tab / Ctrl+Shift+Tab으로 페이지를 넘긴다(보이고 켜져 있을 때).
/// </summary>
public sealed class TabView : DockPanel
{
    private readonly Panel _pages = new() { Margin = new Thickness(0, 6, 0, 0) };
    private TopLevel? _top;

    public TabView()
    {
        LastChildFill = true;
        DockPanel.SetDock(Strip, Dock.Top);
        Children.Add(Strip);
        Children.Add(_pages);
        // 사용자가 단 SelectionChanged보다 먼저 페이지를 바꾼다(여기서 먼저 구독).
        Strip.SelectionChanged += (_, _) =>
        {
            for (int i = 0; i < _pages.Children.Count; i++) _pages.Children[i].IsVisible = i == Strip.SelectedIndex;
        };
    }

    /// <summary>탭 줄(오른쪽 끝 컨트롤은 <see cref="TabStrip.Trailing"/>).</summary>
    public TabStrip Strip { get; } = new(TabStripKind.Page);

    /// <summary>페이지를 끝에 더한다. 첫 페이지는 바로 고른다. 돌려주는 값 = 페이지 번호.</summary>
    public int AddPage(string header, Control content, string? tip = null)
    {
        content.IsVisible = false;
        _pages.Children.Add(content);
        int index = Strip.Add(header, tip: tip);
        if (Strip.SelectedIndex < 0) Strip.SelectedIndex = index;
        return index;
    }

    /// <summary>고른 페이지 번호. 바뀌면 <see cref="SelectionChanged"/>.</summary>
    public int SelectedIndex
    {
        get => Strip.SelectedIndex;
        set => Strip.SelectedIndex = value;
    }

    /// <summary>고른 페이지가 바뀜(새 페이지가 이미 보이는 상태에서 부른다).</summary>
    public event EventHandler? SelectionChanged
    {
        add => Strip.SelectionChanged += value;
        remove => Strip.SelectionChanged -= value;
    }

    /// <summary>페이지(탭)를 숨기거나 보인다. 보던 페이지를 숨기면 보이는 첫 페이지로.</summary>
    public void SetPageVisible(int index, bool visible) => Strip.SetTabVisible(index, visible);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        _top?.AddHandler(KeyDownEvent, OnTopKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _top?.RemoveHandler(KeyDownEvent, OnTopKeyDown);
        _top = null;
    }

    private void OnTopKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Tab || (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.Control) return;
        if (!IsEffectivelyVisible || !IsEffectivelyEnabled) return;
        Strip.Cycle(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
        e.Handled = true;
    }
}
