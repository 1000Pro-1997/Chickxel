using System.Runtime.CompilerServices;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.VisualTree;

namespace Chickxel.App;

/// <summary>
/// 앱 전체의 우클릭 메뉴·단추 메뉴·목록 Flyout을 한곳에서 연다: 언제나 하나만 열리고(다른 창 것 포함),
/// 바깥 누르기(어느 Chickxel 창이든)·바깥 휠·Esc·창 비활성화/이동/크기 바꿈·기준 컨트롤 떼어짐에 닫힌다.
/// </summary>
/// <remarks>
/// Avalonia 메뉴를 그대로 열면 macOS(팝업이 따로 창)에서는 바깥 누르기를 창 공용 LightDismissOverlayLayer로만 닫는데,
/// 그 누르기를 넘겨받은 컨트롤이 새 메뉴를 열면 이전 메뉴가 뒤늦게 닫히며 공용 층을 감춰, 새 메뉴는 바깥 누르기에 안 닫히고 쌓였다.
/// 여기서는 새 메뉴를 열기 전에 이전 메뉴를 닫고, 각 창의 터널 처리기로 바깥 누르기를 따로 잡는다.
/// 메뉴 안에서 연 메뉴(예: 프리셋 Flyout 속 "불러오기 ▾")는 바깥 것을 닫지 않는다.
/// </remarks>
public static class PopupMenus
{
    private sealed record Entry(object Popup, Control Anchor, Func<Visual?> Root, Action Close);

    private static readonly List<Entry> OpenMenus = [];
    private static readonly ConditionalWeakTable<Control, object> Tracked = new();
    private static readonly object Marker = new();
    /// <summary>이번 왼쪽 누르기가 닫은 메뉴의 기준 단추: 그 단추의 Click이 메뉴를 다시 열지 않게(단추 = 열기/닫기 전환).</summary>
    private static Control? _toggledOff;
    /// <summary>바깥 누르기 층이 받은 누르기와 그때 열려 있던 메뉴(같은 누르기가 아래 컨트롤로 다시 올 때 씀).</summary>
    private static (PointerPressedEventArgs? Press, Entry[] Open) _overlayPress = (null, []);

    /// <summary>관리하는 메뉴가 하나라도 열려 있는가.</summary>
    public static bool IsOpen => OpenMenus.Count > 0;

    /// <summary>앱 시작 때 한 번: 앞으로 열리는 모든 창에 바깥 누르기·Esc 처리기를 단다.</summary>
    public static void Install() => Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => Track(w));

    /// <summary>열린 메뉴를 모두 닫는다.</summary>
    public static void CloseAll() => CloseWhere(_ => true);

    /// <summary>
    /// <paramref name="anchor"/>에 메뉴를 연다. <paramref name="at"/>(anchor 좌표)이 있으면 그 점에서,
    /// 없으면 <paramref name="placement"/>대로(기본: 마지막 포인터 위치). 다른 메뉴는 먼저 닫힌다.
    /// </summary>
    /// <returns>연 메뉴. 같은 단추를 눌러 방금 그 메뉴를 닫았으면(전환) null.</returns>
    public static ContextMenu? Show(Control anchor, IEnumerable<object> items, PlacementMode placement = PlacementMode.Pointer, Avalonia.Point? at = null)
    {
        if (ToggledOff(anchor)) return null;
        var menu = new ContextMenu { ItemsSource = items.ToList(), Placement = placement };
        if (at is { } p)
        {
            menu.Placement = PlacementMode.AnchorAndGravity;
            menu.PlacementRect = new Rect(p, new Avalonia.Size(1, 1));
            menu.PlacementAnchor = PopupAnchor.TopLeft;
            menu.PlacementGravity = PopupGravity.BottomRight;
        }
        var entry = new Entry(menu, anchor, () => menu, menu.Close);
        Begin(entry);
        menu.Opened += (_, _) => Track(PopupHost(menu));
        menu.Closed += (_, _) => End(entry);
        menu.Open(anchor);
        Opened(entry, menu.IsOpen);
        return menu;
    }

    /// <summary>
    /// 우클릭·메뉴 키 이벤트(<paramref name="trigger"/>)로 메뉴를 연다: 이벤트를 처리됨으로 표시해 부모가 또 메뉴를 열지 않게 하고,
    /// 포인터 이벤트면 그 점에서, 키보드면 anchor 아래에 연다.
    /// </summary>
    public static ContextMenu? Show(Control anchor, IEnumerable<object> items, RoutedEventArgs trigger)
    {
        trigger.Handled = true;
        Avalonia.Point? at = trigger switch
        {
            PointerEventArgs pe => pe.GetPosition(anchor),
            ContextRequestedEventArgs ce when ce.TryGetPosition(anchor, out var p) => p,
            _ => null,
        };
        return Show(anchor, items, at is null ? PlacementMode.BottomEdgeAlignedLeft : PlacementMode.Pointer, at);
    }

    /// <summary>
    /// 메뉴가 아닌 내용이 든 Flyout을 관리 아래 연다(다른 메뉴와 서로 닫힌다).
    /// 메뉴처럼 바깥 누르기가 닫으면서 그 자리 컨트롤에도 전해진다.
    /// </summary>
    public static void Show(Control anchor, Flyout flyout)
    {
        if (flyout.IsOpen || ToggledOff(anchor)) return;
        flyout.OverlayDismissEventPassThrough = true;
        var entry = new Entry(flyout, anchor, () => flyout.Content is Control c ? c.FindAncestorOfType<FlyoutPresenter>() ?? c : null, flyout.Hide);
        Begin(entry);
        EventHandler? opened = null, closed = null;
        opened = (_, _) =>
        {
            flyout.Opened -= opened;
            Track(flyout.Content is Control c ? PopupHost(c) : null);
        };
        closed = (_, _) =>
        {
            flyout.Closed -= closed;
            End(entry);
        };
        flyout.Opened += opened;
        flyout.Closed += closed;
        flyout.ShowAt(anchor);
        Opened(entry, flyout.IsOpen);
    }

    /// <summary>
    /// <paramref name="control"/>의 우클릭(·메뉴 키)에 <paramref name="build"/>가 만든 메뉴를 단다. null이면 메뉴 없이 부모에게 넘긴다.
    /// 메뉴를 열 때마다 새로 만들어 항목 상태가 늘 지금 것이다.
    /// </summary>
    public static void Attach(Control control, Func<IEnumerable<object>?> build)
    {
        control.ContextRequested += (_, e) =>
        {
            if (e.Handled || build() is not { } items) return;
            Show(control, items, e);
        };
    }

    private static void Begin(Entry entry)
    {
        // 새 메뉴를 품은 메뉴(메뉴 속 메뉴)만 남기고 닫는다.
        CloseWhere(x => !Contains(x, entry.Anchor));
        OpenMenus.Add(entry);
        Track(TopLevel.GetTopLevel(entry.Anchor));
        entry.Anchor.DetachedFromVisualTree += OnAnchorDetached;
    }

    private static void Opened(Entry entry, bool open)
    {
        if (!open)
        {
            End(entry);
            return;
        }
        // 창 안에 그려지는 팝업이면 메뉴가 창에 단 바깥 누르기 처리기(터널은 나중에 단 것이 먼저)보다 먼저 보도록 다시 단다.
        if (TopLevel.GetTopLevel(entry.Anchor) is { } top)
        {
            top.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            top.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    private static void End(Entry entry)
    {
        entry.Anchor.DetachedFromVisualTree -= OnAnchorDetached;
        OpenMenus.Remove(entry);
    }

    private static void OnAnchorDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        CloseWhere(x => ReferenceEquals(x.Anchor, sender));
    }

    /// <summary>
    /// 팝업 안 이벤트가 끝나는 곳: 팝업 창(PopupRoot), 창 안에 그려지는 팝업이면 OverlayPopupHost
    /// (그 안의 이벤트는 창까지 올라가지 않고 팝업에서 끝난다).
    /// </summary>
    private static Control? PopupHost(Control content) =>
        content.GetVisualRoot() as PopupRoot ?? (Control?)content.FindAncestorOfType<OverlayPopupHost>();

    /// <summary>창·팝업마다 한 번: 바깥 누르기·휠, Esc, 창 비활성화·이동·크기 바꿈에 메뉴를 닫는다.</summary>
    private static void Track(Control? top)
    {
        if (top is null || !Tracked.TryAdd(top, Marker)) return;
        top.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) => CloseOutside(e.Source), RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        if (top is not Window window) return;
        window.Deactivated += (_, _) => CloseIn(window);
        window.PositionChanged += (_, _) => CloseIn(window);
        window.Resized += (_, e) => { if (e.Reason != WindowResizeReason.Layout) CloseIn(window); };
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 창 공용 바깥 누르기 층: Avalonia가 같은 이벤트를 아래 컨트롤로 넘겨 다시 보내므로(그때 여기서 닫는다)
        // 지금은 두어 그 누르기가 사라지지 않게 한다. 그 사이 Avalonia가 먼저 닫을 수 있어 열린 메뉴를 적어 둔다.
        if (e.Source is LightDismissOverlayLayer)
        {
            _overlayPress = (e, [.. OpenMenus]);
            return;
        }
        var before = ReferenceEquals(_overlayPress.Press, e) ? _overlayPress.Open : [.. OpenMenus];
        _overlayPress = (null, []);
        CloseOutside(e.Source);
        _toggledOff = e.GetCurrentPoint(null).Properties.IsLeftButtonPressed
            ? before.LastOrDefault(x => !OpenMenus.Contains(x) && Inside(x.Anchor, e.Source))?.Anchor
            : null;
    }

    /// <summary>놓기 뒤 단추 Click이 끝나면 전환 표시를 지운다.</summary>
    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _overlayPress = (null, []);
        if (_toggledOff is not null) Dispatcher.UIThread.Post(() => _toggledOff = null, DispatcherPriority.Background);
    }

    private static bool ToggledOff(Control anchor)
    {
        if (!ReferenceEquals(anchor, _toggledOff)) return false;
        _toggledOff = null;
        return true;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || OpenMenus.Count == 0) return;
        var top = OpenMenus[^1];
        // 메뉴 안의 Esc는 메뉴가 직접 처리한다(하위 메뉴만 닫기 등).
        if (top.Popup is ContextMenu && Contains(top, e.Source)) return;
        top.Close();
        e.Handled = true;
    }

    private static void CloseOutside(object? source) => CloseWhere(x => !Contains(x, source));

    /// <summary>안쪽(나중에 연) 메뉴부터 닫는다. 닫힘이 다른 메뉴를 함께 닫을 수 있어 아직 열린 것만.</summary>
    private static void CloseWhere(Func<Entry, bool> match)
    {
        for (int i = OpenMenus.Count - 1; i >= 0; i--)
        {
            if (i >= OpenMenus.Count) continue;
            var entry = OpenMenus[i];
            if (!match(entry)) continue;
            entry.Close();
            End(entry);
        }
    }

    private static void CloseIn(Window window)
    {
        if (OpenMenus.Any(x => ReferenceEquals(TopLevel.GetTopLevel(x.Anchor), window))) CloseAll();
    }

    /// <summary>
    /// <paramref name="source"/>가 메뉴 안(하위 메뉴·메뉴 속 메뉴의 팝업 창 포함)인가.
    /// 팝업 창은 시각 트리가 끊기므로 논리 부모·템플릿 부모도 따라 올라간다.
    /// </summary>
    private static bool Contains(Entry entry, object? source) => Inside(entry.Root(), source);

    private static bool Inside(Visual? root, object? source)
    {
        if (root is null || source is not StyledElement start) return false;
        var seen = new HashSet<StyledElement>();
        var pending = new Stack<StyledElement>();
        pending.Push(start);
        while (pending.TryPop(out var node))
        {
            if (ReferenceEquals(node, root)) return true;
            if (!seen.Add(node)) continue;
            if (node.Parent is StyledElement logical) pending.Push(logical);
            if (node.TemplatedParent is StyledElement templated) pending.Push(templated);
            if (node is Visual v && v.GetVisualParent() is StyledElement visual) pending.Push(visual);
        }
        return false;
    }
}
