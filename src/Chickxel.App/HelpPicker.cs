using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 대상 도움말(F1)이 열 가이드 자리: 쪽 id + 그 쪽의 화면 그림(<see cref="Shot"/>) 또는 자리 글(<see cref="Anchor"/>).
/// 자리 글은 소제목·표 줄 첫 칸·목록 항목·문단을 찾는 글이다(<see cref="HelpWindow"/>가 찾음). 가이드가 앱 문자열을 그대로 쓰는 곳은
/// 그 앱 문자열(지금 언어)을, 아니면 영어 가이드 글을 적는다(한국어 화면은 영어 쪽에서 찾은 같은 자리로 간다). 못 찾으면 쪽 맨 위.
/// </summary>
internal readonly record struct HelpTarget(string PageId, string? Shot = null, string? Anchor = null)
{
    /// <summary>도구마다 자세히 설명하는 가이드 쪽.</summary>
    public static string ToolPage(ToolKind tool) => tool switch
    {
        ToolKind.Pencil => "tools-pencil",
        ToolKind.GradientPencil => "tools-gradient-pencil",
        ToolKind.Eraser => "tools-eraser",
        ToolKind.Fill => "tools-fill",
        ToolKind.Gradient => "tools-gradient",
        ToolKind.Picker => "tools-picker",
        ToolKind.Shape => "tools-shape",
        ToolKind.Text => "tools-text",
        ToolKind.Select or ToolKind.EllipseSelect or ToolKind.Lasso or ToolKind.MagicWand => "tools-select",
        ToolKind.Move => "tools-move",
        ToolKind.Dodge => "tools-dodge",
        ToolKind.Dither => "tools-dither",
        ToolKind.ReplaceBrush => "tools-replace-brush",
        ToolKind.Pivot => "tools-pivot",
        ToolKind.Slice => "tools-nine-slice",
        _ => throw new ArgumentOutOfRangeException(nameof(tool)),
    };
}

/// <summary>
/// 점에 따라 달라지는 대상 도움말 자리(격자의 칸, 지금 도구에 따라 바뀌는 설정 등). 이것을 구현한 컨트롤·패널·창은 그 안쪽(자신 포함)의
/// 점 아래 컨트롤마다 가장 안쪽부터 물음을 받는다 — 그 컨트롤에 붙인 <see cref="HelpTargets.Target"/>보다 먼저.
/// </summary>
internal interface IHelpTargetSource
{
    /// <summary>컨트롤 <paramref name="c"/>와 그 안의 점 <paramref name="local"/>(c 좌표)의 가이드 자리와 노란 상자로 두를 범위(c 좌표). 모르면 null.</summary>
    (HelpTarget Target, Rect Area)? HelpTargetFor(Control c, Avalonia.Point local);
}

/// <summary>대상 도움말을 스스로 다루는 창(메인 창): 고르기 키는 창의 단축키 표가 부르고, 안내 글은 창이 직접(상태 표시줄) 보인다.</summary>
internal interface IHelpPickerHost
{
    /// <summary>고르기를 시작할 때: 안내 글(<see cref="UiStrings.Help_SelectHint"/>)을 보인다.</summary>
    void HelpPickerStarted();

    /// <summary>고르기를 마칠 때: 안내를 걷는다. <paramref name="noTarget"/>면 누른 곳에 연결된 도움말이 없다고 알린다.</summary>
    void HelpPickerStopped(bool noTarget);
}

/// <summary>
/// 컨트롤에 붙이는 대상 도움말 자리(<see cref="HelpAt"/>). 창에 붙이면 그 창 어디를 눌러도 열리는 쪽이 된다.
/// 이름 글과 값 칸처럼 한 설정을 이루는 컨트롤들은 묶음(<see cref="HelpGroup"/>)으로 붙여 어느 것을 가리켜도 모두 함께 두르고,
/// 고르기 상자는 고른 항목마다 자리(<see cref="HelpOptions"/>)를 줄 수 있다 — 상자를 가리키면 지금 고른 항목의 설명, 이름 글을 가리키면 설정의 설명.
/// </summary>
internal static class HelpTargets
{
    public static readonly AttachedProperty<HelpTarget?> TargetProperty =
        AvaloniaProperty.RegisterAttached<Control, HelpTarget?>("Target", typeof(HelpTargets));

    /// <summary>같은 자리를 함께 두르는 묶음(<see cref="HelpGroup"/> — 이름 글과 값 칸들).</summary>
    public static readonly AttachedProperty<Control[]?> GroupProperty =
        AvaloniaProperty.RegisterAttached<Control, Control[]?>("Group", typeof(HelpTargets));

    /// <summary>고르기 상자의 항목 번호마다 자리(null 항목 = 상자에 붙인 자리).</summary>
    public static readonly AttachedProperty<HelpTarget?[]?> OptionsProperty =
        AvaloniaProperty.RegisterAttached<Control, HelpTarget?[]?>("Options", typeof(HelpTargets));

    public static HelpTarget? Get(Control control) => control.GetValue(TargetProperty);

    /// <summary>대상 도움말(F1)로 이 컨트롤을 누르면 열 가이드 쪽 <paramref name="page"/>(그 쪽의 그림 <paramref name="shot"/> 또는 자리 글 <paramref name="anchor"/>).</summary>
    public static T HelpAt<T>(this T control, string page, string? shot = null, string? anchor = null) where T : Control
    {
        control.SetValue(TargetProperty, new HelpTarget(page, shot, anchor));
        return control;
    }

    /// <summary>
    /// 한 설정을 이루는 컨트롤들(이름 글 + 값 칸·체크 등)에 같은 자리(쪽 <paramref name="page"/>의 자리 글 <paramref name="anchor"/>)를 붙이고 묶는다:
    /// 어느 것을 가리켜도 노란 상자가 보이는 묶음 전체를 두른다. 묶음 안 고르기 상자에 <see cref="HelpOptions"/>가 있으면 상자만은 고른 항목 자리.
    /// </summary>
    public static void HelpGroup(string page, string? anchor, params Control[] members)
    {
        var target = new HelpTarget(page, Anchor: anchor);
        foreach (var member in members)
        {
            member.SetValue(TargetProperty, target);
            member.SetValue(GroupProperty, members);
        }
    }

    /// <summary>
    /// 고르기 상자를 가리키면 지금 고른 항목(번호 = <paramref name="anchors"/> 차례)을 설명하는 쪽 <paramref name="page"/>의 자리 글로 간다.
    /// null 자리 글이나 범위 밖 항목은 상자에 붙인 자리(<see cref="HelpAt"/>)를 쓴다. 노란 상자는 고르기 상자만 두른다.
    /// </summary>
    public static ComboBox HelpOptions(this ComboBox combo, string page, params string?[] anchors)
    {
        combo.SetValue(OptionsProperty, [.. anchors.Select(a => a is null ? (HelpTarget?)null : new HelpTarget(page, Anchor: a))]);
        return combo;
    }

    /// <summary>고르기 상자의 지금 고른 항목 자리(<see cref="HelpOptions"/>로 정한 것). 없으면 null.</summary>
    public static HelpTarget? Option(Control control)
        => control is ComboBox { SelectedIndex: >= 0 } combo && combo.GetValue(OptionsProperty) is { } options && combo.SelectedIndex < options.Length
            ? options[combo.SelectedIndex] : null;

    /// <summary>
    /// 컨트롤 <paramref name="c"/>에 붙인 자리와 두를 범위(c 좌표): 고르기 상자면 고른 항목 자리와 상자, 아니면 붙인 자리와 컨트롤(묶음이면 보이는 묶음 전체).
    /// 붙인 자리가 없으면 null.
    /// </summary>
    public static (HelpTarget Target, Rect Area)? Of(Control c)
    {
        var area = new Rect(c.Bounds.Size);
        if (Option(c) is { } option) return (option, area);
        if (Get(c) is not { } target) return null;
        if (c.GetValue(GroupProperty) is { } group)
            foreach (var member in group)
                if (!ReferenceEquals(member, c) && member.IsEffectivelyVisible && member.TranslatePoint(default, c) is { } at)
                    area = area.Union(new Rect(at, member.Bounds.Size));
        return (target, area);
    }

    /// <summary>컨트롤 전체를 두르는 자리(<paramref name="target"/>이 null이면 null) — 점과 상관없는 <see cref="IHelpTargetSource"/> 구현용.</summary>
    public static (HelpTarget Target, Rect Area)? Whole(Control c, HelpTarget? target) => target is { } t ? (t, new Rect(c.Bounds.Size)) : null;
}

/// <summary>
/// 대상 도움말(F1) 고르기: 창 위에 투명한 겹판(창의 OverlayLayer)을 덮고, 도움말 커서와 점 아래 대상을 두르는 노란 상자를 보이다가
/// 왼쪽 누르기 → 사용 가이드를 그 자리로 연다. 오른쪽 누르기·Esc·고르기 키 다시 누르기·창 비활성화 → 취소. 한 번에 한 창만.
/// 모든 창에서 명령 <see cref="CommandId"/>의 지금 키(메인 창이 <see cref="HelpWindow.Shortcuts"/>로 알려 줌, 입력란 안에서도)로 켠다.
/// 대상은 점 아래 맨 위 컨트롤부터 바깥으로 올라가며 정한다(<see cref="TargetAt"/>).
/// </summary>
public static class HelpPicker
{
    /// <summary>단축키 표의 대상 도움말 명령 id.</summary>
    public const string CommandId = "도움말/대상 도움말";

    private static readonly KeyChord[] DefaultKeys = [new(Key.F1, KeyModifiers.None)];
    private static Cursor? _cursor;
    private static Session? _session;
    /// <summary>마지막으로 활성화된 창(키보드 초점이 다른 창에 남아 있어도 앞에 있는 창을 알기 위해).</summary>
    private static Window? s_lastActivated;

    /// <summary>앱 시작 때 한 번: 모든 창(대화상자·도구 창 포함)에서 고르기 키를 받는다.</summary>
    public static void Install()
    {
        InputElement.KeyDownEvent.AddClassHandler<Window>(OnKeyDown, RoutingStrategies.Tunnel);
        InputElement.PointerPressedEvent.AddClassHandler<Window>(OnPointerPressed, RoutingStrategies.Tunnel);
        WindowBase.IsActiveProperty.Changed.AddClassHandler<Window>((w, e) =>
        {
            if (w.IsActive) s_lastActivated = w;
            else if (ReferenceEquals(s_lastActivated, w)) s_lastActivated = null;
        });
    }

    /// <summary><paramref name="window"/>에서 고르는 중인가.</summary>
    public static bool IsActive(TopLevel window) => _session is { } s && ReferenceEquals(s.Window, window);

    /// <summary><paramref name="window"/>에서 고르기를 켜고, 이미 그 창에서 고르는 중이면 끈다(다른 창의 고르기는 먼저 끝남).</summary>
    public static void Toggle(TopLevel window)
    {
        // 키보드 초점이 다른 창(예: 메인 캔버스)에 남은 채 키가 오면 지금 앞에 있는 창에서 고른다.
        window = ActiveOr(window);
        bool same = IsActive(window);
        Stop();
        if (!same) Start(window);
    }

    /// <summary>마지막으로 활성화된 창이 아직 활성이면 그 창, 아니면 <paramref name="fallback"/>.</summary>
    private static TopLevel ActiveOr(TopLevel fallback)
        => s_lastActivated is { IsActive: true } last ? last : fallback;

    /// <summary>고르기를 끝낸다(고르는 중이 아니면 아무것도 안 함).</summary>
    public static void Stop() => Stop(noTarget: false);

    /// <summary>명령의 지금 키(사용자가 바꾼 것, 다 지웠으면 없음). 메인 창이 아직 없으면 기본 F1.</summary>
    private static IReadOnlyList<KeyChord> Keys => HelpWindow.Shortcuts?.Invoke(CommandId) ?? DefaultKeys;

    private static void OnKeyDown(Window window, KeyEventArgs e)
    {
        if (WorkOverlay.IsActive) return;
        var chord = new KeyChord(e.Key, Shortcuts.ChordMods(e.KeyModifiers));
        // 고르는 창이 앞에 있으면 초점이 남은 다른 창으로 온 키도 고르기 몫이다.
        if (_session is { } s && (ReferenceEquals(s.Window, window) || ReferenceEquals(s.Window, ActiveOr(window))))
        {
            // 고르는 동안 키는 아래 컨트롤에 가지 않는다.
            if (e.Key == Key.Escape || Keys.Contains(chord)) Stop();
            e.Handled = true;
            return;
        }
        if (window is IHelpPickerHost || !Keys.Contains(chord)) return;
        e.Handled = true;
        Toggle(window);
    }

    /// <summary>마우스 옆 단추에 고르기를 정했으면 그 단추로도 켠다.</summary>
    private static void OnPointerPressed(Window window, PointerPressedEventArgs e)
    {
        if (WorkOverlay.IsActive || IsActive(window) || window is IHelpPickerHost) return;
        var button = Shortcuts.BindableMouse(e.GetCurrentPoint(window).Properties.PointerUpdateKind);
        if (button == MouseButton.None || !Keys.Contains(new KeyChord(Key.None, Shortcuts.ChordMods(e.KeyModifiers), button))) return;
        e.Handled = true;
        Toggle(window);
    }

    private static void Start(TopLevel window)
    {
        if (OverlayLayer.GetOverlayLayer(window) is not { } layer) return;
        CloseMenus(window);
        _session = new Session(window, layer);
    }

    private static void Stop(bool noTarget)
    {
        if (_session is not { } s) return;
        _session = null;
        s.Dispose(noTarget);
    }

    /// <summary>창에 열린 메뉴·드롭다운·Flyout을 닫는다(겹판이 덮지 못하는 따로 된 팝업 창).</summary>
    private static void CloseMenus(TopLevel window)
    {
        PopupMenus.CloseAll();
        foreach (var v in window.GetVisualDescendants())
        {
            if (v is Menu { IsOpen: true } menu) menu.Close();
            else if (v is Popup { IsOpen: true } popup) popup.IsOpen = false;
        }
    }

    /// <summary>가이드 창의 주인: 맨 위 주인 창(대화상자를 닫아도 가이드가 남고, 모달 대화상자 뒤에서도 쓸 수 있게).</summary>
    private static Window? GuideOwner(TopLevel window)
    {
        var w = window as Window;
        while (w?.Owner is Window owner) w = owner;
        return w ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    /// <summary>
    /// 점(<paramref name="window"/> 좌표) 아래의 대상 도움말 자리와 그 대상의 범위(<paramref name="surface"/> 좌표). 상자와 누른 결과가 모두 이것으로 정해진다.
    /// 겹판(<paramref name="surface"/>) 아래에서 실제로 그려진 맨 위 컨트롤부터 창까지 올라가며, 컨트롤마다 (1) 그것을 품은 <see cref="IHelpTargetSource"/>
    /// (가장 가까운 것부터, 자신 포함)가 자리를 주면 그것, (2) 아니면 그 컨트롤에 붙인 자리(<see cref="HelpTargets.Of"/> — 고른 항목·짝 포함)를 쓴다(가장 안쪽 대상이 이김).
    /// 아무것도 없고 창에도 쪽이 없으면(메시지·입력·진행 같은 공용 대화상자) 주인 창을 따라 올라가 처음 만나는 창 쪽을 쓴다(상자는 창 전체).
    /// </summary>
    internal static (HelpTarget Target, Rect Bounds)? TargetAt(TopLevel window, Visual surface, Avalonia.Point at)
    {
        var path = new List<Control>();
        for (var v = window.GetVisualAt(at, x => !ReferenceEquals(x, surface)); v is not null; v = v.GetVisualParent())
            if (v is Control c) path.Add(c);
        for (int i = 0; i < path.Count; i++)
        {
            var c = path[i];
            Avalonia.Point? local = null;
            for (int j = i; j < path.Count; j++)
            {
                if (path[j] is not IHelpTargetSource source) continue;
                local ??= window.TranslatePoint(at, c) ?? default;
                if (source.HelpTargetFor(c, local.Value) is { } found)
                    return Clip(c, found.Area, window, surface) is { } area ? (found.Target, area) : null;
            }
            if (HelpTargets.Of(c) is { } own)
                return Clip(c, own.Area, window, surface) is { } bounds ? (own.Target, bounds) : null;
        }
        // 여기까지 왔으면 창(경로의 끝)에도 쪽이 없다.
        if (path.Count == 0 || OwnerTarget(window) is not { } owned) return null;
        return window.TranslatePoint(default, surface) is { } origin ? (owned, new Rect(origin, window.Bounds.Size)) : null;
    }

    /// <summary>주인 창을 따라 올라가며 처음 만나는 창 쪽(창에 붙인 <see cref="HelpTargets.Target"/>). 없으면 null.</summary>
    private static HelpTarget? OwnerTarget(TopLevel window)
    {
        for (var w = (window as Window)?.Owner as Window; w is not null; w = w.Owner as Window)
            if (HelpTargets.Get(w) is { } target) return target;
        return null;
    }

    /// <summary><paramref name="area"/>(<paramref name="visual"/> 좌표)를 <paramref name="surface"/> 좌표로 옮기고, 잘라 그리는 부모(스크롤 등) 밖은 뺀다.</summary>
    private static Rect? Clip(Visual visual, Rect area, TopLevel window, Visual surface)
    {
        if (visual.TranslatePoint(area.Position, surface) is not { } at) return null;
        var rect = new Rect(at, area.Size);
        for (var p = visual.GetVisualParent(); p is not null && !ReferenceEquals(p, window); p = p.GetVisualParent())
        {
            if (p.ClipToBounds && p.TranslatePoint(default, surface) is { } origin)
                rect = rect.Intersect(new Rect(origin, p.Bounds.Size));
        }
        return rect;
    }

    /// <summary>한 창에서 고르는 동안의 겹판(투명 판 + 노란 상자 + 안내 띠)과 그 창의 처리기.</summary>
    private sealed class Session
    {
        public readonly TopLevel Window;
        private readonly OverlayLayer _layer;
        private readonly Border _root;
        private readonly Border _box = new()
        {
            BorderBrush = HelpWindow.Yellow,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            IsHitTestVisible = false,
            IsVisible = false,
        };
        private readonly TextBlock? _hint;
        private readonly Border? _banner;
        private readonly IInputElement? _previousFocus;
        private HelpTarget? _picked;
        private bool _clickPending;

        public Session(TopLevel window, OverlayLayer layer)
        {
            Window = window;
            _layer = layer;
            var surface = new Panel { Children = { new Canvas { Children = { _box } } } };
            if (window is not IHelpPickerHost)
            {
                // 상태 표시줄이 없는 창: 위쪽 가운데 안내 띠(누르기는 아래로 통과, 점이 띠 위에 오면 아래 끝으로 옮김).
                _hint = new TextBlock { Text = UiStrings.Help_SelectHint, Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap };
                _banner = new Border
                {
                    Child = _hint,
                    Background = AppTheme.PanelBrush,
                    BorderBrush = HelpWindow.Yellow,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 5),
                    Margin = new Thickness(12, 8),
                    MaxWidth = 520,
                    Opacity = 0.94,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible = false,
                };
                surface.Children.Add(_banner);
            }
            _root = new Border
            {
                Background = Brushes.Transparent,
                Cursor = _cursor ??= new Cursor(StandardCursorType.Help),
                Focusable = true,
                Child = surface,
                // 내용에 맞춰 크기가 정해지는 창(대화상자)에서 덮개 층이 더 작게 재어도 가운데로 밀리지 않게 왼쪽 위에 둔다.
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            _root.PointerPressed += OnPressed;
            _root.PointerReleased += OnReleased;
            _root.PointerMoved += (_, e) =>
            {
                AvoidBanner(e.GetPosition(_root));
                ShowBox(TargetAt(Window, _root, e.GetPosition(Window))?.Bounds);
            };
            _root.PointerExited += (_, _) => ShowBox(null);
            Fit();
            _layer.PropertyChanged += OnLayerChanged;
            _layer.Children.Add(_root);
            if (window is WindowBase w) w.Deactivated += OnDeactivated;
            if (window is Window closable) closable.Closed += OnDeactivated;
            _previousFocus = window.FocusManager?.GetFocusedElement();
            _root.Focus();
            (window as IHelpPickerHost)?.HelpPickerStarted();
        }

        public void Dispose(bool noTarget)
        {
            _layer.PropertyChanged -= OnLayerChanged;
            _layer.Children.Remove(_root);
            if (Window is WindowBase w) w.Deactivated -= OnDeactivated;
            if (Window is Window closable) closable.Closed -= OnDeactivated;
            if (_previousFocus is InputElement focus && TopLevel.GetTopLevel(focus) is not null) focus.Focus();
            (Window as IHelpPickerHost)?.HelpPickerStopped(noTarget);
        }

        private static void OnDeactivated(object? sender, EventArgs e) => Stop();

        private void Fit()
        {
            _root.Width = _layer.Bounds.Width;
            _root.Height = _layer.Bounds.Height;
        }

        private void OnLayerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.BoundsProperty) Fit();
        }

        /// <summary>점(겹판 좌표)이 안내 띠 위에 오면 띠를 반대쪽(위 ↔ 아래 끝)으로 옮겨 그 아래 컨트롤이 보이게.</summary>
        private void AvoidBanner(Avalonia.Point p)
        {
            if (_banner is null || !_banner.Bounds.Inflate(8).Contains(p)) return;
            _banner.VerticalAlignment = _banner.VerticalAlignment == VerticalAlignment.Top ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        }

        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
            var point = e.GetCurrentPoint(_root);
            if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed) return;
            _picked = point.Properties.IsLeftButtonPressed ? TargetAt(Window, _root, e.GetPosition(Window))?.Target : null;
            _clickPending = true;
            e.Pointer.Capture(_root);
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs e)
        {
            e.Handled = true;
            if (!_clickPending) return;
            _clickPending = false;
            e.Pointer.Capture(null);
            bool cancelled = e.InitialPressMouseButton == MouseButton.Right;
            if (!cancelled && _picked is null && _hint is not null)
            {
                // 안내 띠가 있는 창은 고르기를 이어 가며 띠에 알린다(메인 창은 끝내고 상태 표시줄에).
                _hint.Text = UiStrings.Help_NoTarget;
                return;
            }
            var target = _picked;
            Stop(noTarget: !cancelled && target is null);
            if (!cancelled && target is { } t) HelpWindow.Open(GuideOwner(Window)).ShowPage(t);
        }

        /// <summary>노란 상자를 <paramref name="bounds"/>(겹판 좌표)에 보인다. null이면 숨긴다.</summary>
        private void ShowBox(Rect? bounds)
        {
            if (bounds is not { Width: > 0, Height: > 0 } r)
            {
                _box.IsVisible = false;
                return;
            }
            // 대상 바로 바깥을 두르되 창 밖으로 나가지 않게.
            r = r.Inflate(1).Intersect(new Rect(_root.Bounds.Size));
            Canvas.SetLeft(_box, r.X);
            Canvas.SetTop(_box, r.Y);
            _box.Width = r.Width;
            _box.Height = r.Height;
            _box.IsVisible = true;
        }
    }
}
