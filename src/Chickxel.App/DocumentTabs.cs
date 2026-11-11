using Avalonia.VisualTree;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>문서 탭 하나의 겉모습: 제목(파일 이름), 풍선 도움말(전체 경로), 저장 안 한 변경 표시, 미리보기 탭인지(다른 색·재생 아이콘).</summary>
internal readonly record struct DocumentTabInfo(string Title, string Tip, bool Dirty, bool Preview = false);

/// <summary>
/// 캔버스 위 문서 탭 줄(편집기 묶음 하나): 탭마다 제목·저장 안 한 변경 점·닫기 X, 끝에 "+"(새 문서), 오른쪽 끝에 나눠 열기 단추.
/// 왼쪽 클릭 = 고르기, 가운데 클릭·X = 닫기, 우클릭 = <see cref="MenuFor"/> 메뉴. 끌면 순서를 바꾸고(<see cref="DragReorder"/> — 앱의 다른 목록과 같은 모양),
/// 끄는 동안·놓을 때 <see cref="TabDragMoved"/>·<see cref="TabDragEnded"/>로 위치를 알려 캔버스 쪽에 놓아 나누기를 할 수 있다.
/// 포커스된 묶음이면 고른 탭 위에 강조색 선(좌우로 나눴으면 줄 아래 선도).
/// </summary>
internal sealed class DocumentTabStrip : Border
{
    private const double TabHeight = 28;
    /// <summary>닫은 자리에 맞춰 너비를 고정할 때 가장 좁은 탭(제목은 줄임표, 닫기 X는 보임).</summary>
    private const double MinTabWidth = 48;
    private static readonly IBrush Clear = Brushes.Transparent;
    private static readonly IBrush TabHover = AppTheme.Brush(Color.FromArgb(18, 255, 255, 255));
    /// <summary>미리보기 탭 색(문서 탭과 한눈에 구분되게): 글자·아이콘·위 선, 옅은 바탕.</summary>
    private static readonly Color PreviewColor = Color.FromRgb(80, 200, 150);
    private static readonly IImmutableSolidColorBrush PreviewBrush = AppTheme.Brush(PreviewColor);
    private static readonly IBrush PreviewBack = AppTheme.Brush(Color.FromArgb(40, PreviewColor.R, PreviewColor.G, PreviewColor.B));
    private static readonly IBrush PreviewActiveBack = AppTheme.Brush(Color.FromArgb(70, PreviewColor.R, PreviewColor.G, PreviewColor.B));

    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer _scroll;
    private readonly Border _newButton;
    private readonly Border _splitButton;
    private readonly DragReorder _reorder = new(Orientation.Horizontal);
    private readonly List<(Border Box, TextBlock Title, Border Close)> _tabs = new();
    private IReadOnlyList<DocumentTabInfo> _infos = [];
    private int _active = -1, _hover = -1;
    private bool _focused = true, _highlight;
    private string _closeTip = "";

    // 탭 끌기(캔버스 쪽에 놓기): 누른 탭·위치, 문턱을 넘었는지.
    private int _pressIndex = -1;
    private Avalonia.Point _pressAt;
    private bool _dragging;

    // 마우스로 연달아 닫기(Chrome처럼): 닫은 탭 번호(−1 = 없음), 그 탭 오른쪽 끝·줄 전체 너비(줄 좌표). 다시 그린 뒤 그 자리로 온 탭의 너비를 고정해
    // 닫기 X가 같은 곳에 오게 하고, 줄 끝 빈 칸(_spacer)으로 줄 너비를 지켜 넘친 줄의 스크롤 위치가 당겨지지 않게 한다. 마우스가 줄을 나가면 원래 너비로.
    private int _closedIndex = -1;
    private double _closedRight, _closedExtent;
    private readonly Border _spacer = new() { IsHitTestVisible = false };
    private bool _frozen;
    /// <summary>마우스가 탭 줄 안에 있는지(탭을 지우면 Avalonia가 마우스가 그대로여도 나감 알림을 보내 <see cref="InputElement.IsPointerOver"/>를 믿지 않는다).</summary>
    private bool _pointerInside;
    /// <summary>고정한 동안 마우스 움직임을 보는 창(<see cref="WatchPointer"/>)과 그 처리기.</summary>
    private TopLevel? _watchTop;
    private readonly EventHandler<PointerEventArgs> _onTopPointerMoved;

    public DocumentTabStrip()
    {
        Background = AppTheme.PanelBrush;
        BorderBrush = AppTheme.BorderBrush;
        BorderThickness = new Thickness(0, 0, 0, 2);
        _onTopPointerMoved = OnTopPointerMoved;
        _scroll = new ScrollViewer
        {
            Content = _row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        // 휠은 가로로 넘긴다(탭이 넘칠 때).
        _scroll.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (e.Delta.Y == 0) return;
            _scroll.Offset = _scroll.Offset.WithX(Math.Clamp(_scroll.Offset.X - e.Delta.Y * 40, 0, Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width)));
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        _newButton = FlatIcon(VectorIcon.Plus, () => NewRequested?.Invoke());
        _newButton.Margin = new Thickness(3, 0);
        // 탭이 없어도(시작 화면) "+"는 보인다.
        _row.Children.Add(_newButton);
        _row.Children.Add(_spacer);
        _splitButton = FlatIcon(VectorIcon.Columns2, () => SplitRequested?.Invoke());
        _splitButton.Margin = new Thickness(3, 0, 4, 0);
        DockPanel.SetDock(_splitButton, Dock.Right);
        Child = new DockPanel { LastChildFill = true, Height = TabHeight, Children = { _splitButton, _scroll } };
        _reorder.Moved += (from, to) => Reordered?.Invoke(from, to);
        PointerEntered += (_, _) => _pointerInside = true;
        PointerExited += (_, e) =>
        {
            // 마우스 아래 탭이 지워져 생긴 나감 알림(위치는 아직 줄 안)은 무시한다.
            if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
            _pointerInside = false;
            ReleaseFreeze();
        };
    }

    /// <summary>탭을 왼쪽 버튼으로 누름(번호).</summary>
    public event Action<int>? TabPressed;

    /// <summary>탭 닫기(X·가운데 클릭).</summary>
    public event Action<int>? CloseRequested;

    /// <summary>"+": 새 문서.</summary>
    public event Action? NewRequested;

    /// <summary>나눠 열기 단추.</summary>
    public event Action? SplitRequested;

    /// <summary>끌어서 순서를 바꿈(원래 번호, 옮긴 뒤 번호).</summary>
    public event Action<int, int>? Reordered;

    /// <summary>탭을 끄는 중(문턱을 넘은 뒤 이동마다): 번호, 포인터 이벤트(위치는 받는 쪽 좌표로 읽음).</summary>
    public event Action<int, PointerEventArgs>? TabDragMoved;

    /// <summary>끌던 탭을 놓음(Esc·붙잡기를 잃으면 <c>null</c> 이벤트).</summary>
    public event Action<int, PointerEventArgs?>? TabDragEnded;

    /// <summary>탭 우클릭 메뉴 항목(번호 → 항목, null = 메뉴 없음).</summary>
    public Func<int, IEnumerable<object>?>? MenuFor { get; set; }

    /// <summary>오른쪽 끝 다른 쪽으로 옮기기 단추를 보일지(탭이 없으면 — 시작 화면 — 숨김).</summary>
    public bool SplitButtonVisible
    {
        get => _splitButton.IsVisible;
        set => _splitButton.IsVisible = value;
    }

    /// <summary>풍선 도움말: 닫기 X, "+", 나눠 열기 단추.</summary>
    public void SetTips(string close, string newTab, string split)
    {
        _closeTip = close;
        foreach (var t in _tabs) ToolTip.SetTip(t.Close, close);
        ToolTip.SetTip(_newButton, newTab);
        ToolTip.SetTip(_splitButton, split);
    }

    /// <summary>
    /// 포커스 표시: <paramref name="focused"/> = 편집 대상 묶음(고른 탭 위에 강조색 선), <paramref name="highlight"/> = 줄 아래 선도 강조색
    /// (좌우로 나눴을 때 포커스된 묶음을 알아보게).
    /// </summary>
    public void SetFocus(bool focused, bool highlight)
    {
        if (_focused == focused && _highlight == highlight) return;
        _focused = focused;
        _highlight = highlight;
        UpdateVisuals();
    }

    /// <summary>탭들을 다시 만든다(같은 내용이면 겉모습만 맞춘다). <paramref name="active"/> = 고른 탭 번호(−1 = 없음).</summary>
    public void SetTabs(IReadOnlyList<DocumentTabInfo> tabs, int active)
    {
        if (tabs.Count == _infos.Count && tabs.SequenceEqual(_infos))
        {
            if (_active != active)
            {
                _active = active;
                UpdateVisuals();
                BringActiveIntoView();
            }
            return;
        }
        // 마우스로 탭 하나를 닫은 결과인지(닫기를 취소했거나 다른 변경이면 고정하지 않음).
        int closed = _closedIndex >= 0 && tabs.Count == _infos.Count - 1 && tabs.Count > 0 && _pointerInside ? _closedIndex : -1;
        _closedIndex = -1;
        _infos = tabs.ToList();
        _active = active;
        _hover = -1;
        _reorder.Clear();
        _row.Children.Clear();
        _tabs.Clear();
        for (int i = 0; i < tabs.Count; i++)
        {
            var (box, title, close) = MakeTab(i, tabs[i]);
            _tabs.Add((box, title, close));
            _row.Children.Add(box);
            _reorder.Attach(box);
            AttachDrag(i, box);
        }
        _row.Children.Add(_newButton);
        _row.Children.Add(_spacer);
        if (closed >= 0) FreezeAfterClose(closed);
        else ReleaseFreeze();
        UpdateVisuals();
        BringActiveIntoView();
    }

    /// <summary>탭 닫기 요청: 마우스 아래 자리를 기억해 두고(<see cref="FreezeAfterClose"/>) 알린다.</summary>
    private void RequestClose(int index)
    {
        _closedIndex = index;
        _closedRight = _tabs[index].Box.Bounds.Right;
        _closedExtent = _row.Bounds.Width;
        CloseRequested?.Invoke(index);
    }

    /// <summary>
    /// 닫은 탭 자리로 온 탭(뒤 탭, 맨 끝을 닫았으면 앞 탭)의 오른쪽 끝을 닫은 탭의 오른쪽 끝에 맞춘다 — 닫기 X가 마우스 아래 그대로 와서 같은 곳을 계속 눌러 닫을 수 있다.
    /// 줄 너비는 닫기 전만큼 지킨다(넘친 줄이면 스크롤 위치가 그대로).
    /// </summary>
    private void FreezeAfterClose(int closed)
    {
        int target = Math.Min(closed, _tabs.Count - 1);
        var box = _tabs[target].Box;
        // 먼저 넉넉한 빈 칸으로 줄이 줄지 않게 해 두고 배치해 그 탭의 왼쪽 끝을 읽는다.
        _spacer.Width = _closedExtent;
        UpdateLayout();
        box.Width = Math.Max(MinTabWidth, _closedRight - box.Bounds.X);
        UpdateLayout();
        _spacer.Width = Math.Max(0, _closedExtent - (_row.Bounds.Width - _spacer.Width));
        _frozen = true;
        WatchPointer(true);
        // 줄이 바뀌어도 마우스는 그대로라 들어옴 알림이 없을 수 있다: 그 탭에 닫기 X를 바로 보인다.
        _hover = target;
    }

    /// <summary>마우스가 탭 줄을 나감: 고정한 탭 너비와 줄 끝 빈 칸을 풀어 원래 너비로.</summary>
    private void ReleaseFreeze()
    {
        _closedIndex = -1;
        if (!_frozen) return;
        _frozen = false;
        WatchPointer(false);
        foreach (var t in _tabs) t.Box.Width = double.NaN;
        _spacer.Width = 0;
    }

    /// <summary>
    /// 고정한 동안 창 전체의 마우스 움직임을 본다: 탭을 지운 뒤에는 줄의 나감 알림이 오지 않을 수 있어(<see cref="_pointerInside"/>),
    /// 줄 밖에서 움직이면 풀어 준다.
    /// </summary>
    private void WatchPointer(bool on)
    {
        if (on == _watchTop is not null) return;
        if (on)
        {
            _watchTop = TopLevel.GetTopLevel(this);
            _watchTop?.AddHandler(PointerMovedEvent, _onTopPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        else
        {
            _watchTop!.RemoveHandler(PointerMovedEvent, _onTopPointerMoved);
            _watchTop = null;
        }
    }

    private void OnTopPointerMoved(object? sender, PointerEventArgs e)
    {
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
        _pointerInside = false;
        ReleaseFreeze();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ReleaseFreeze();
    }

    private (Border Box, TextBlock Title, Border Close) MakeTab(int index, DocumentTabInfo info)
    {
        var title = new TextBlock
        {
            Text = info.Title,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220,
            Margin = new Thickness(0, 0, 5, 0),
        };
        var dot = new IconView(VectorIcon.Dot, AppTheme.UnsavedBrush) { Width = 8, Height = 8, Filled = true, IsVisible = info.Dirty, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        var close = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            Background = Clear,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new IconView(VectorIcon.X, strokeWidth: 2.4) { Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        ToolTip.SetTip(close, _closeTip);
        close.PointerEntered += (_, _) => close.Background = AppTheme.HoverBrush;
        close.PointerExited += (_, _) => close.Background = Clear;
        close.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(close).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            RequestClose(index);
        };
        // 닫기 X(와 저장 안 한 점)는 오른쪽 끝에 붙인다 — 닫은 자리를 지키려고 탭 너비를 고정하면(FreezeAfterClose) 남는 칸은 제목 뒤, 모자라면 제목이 줄임표로.
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(dot, Dock.Right);
        var content = new DockPanel { LastChildFill = true };
        if (info.Preview)
        {
            var play = new IconView(VectorIcon.Play, PreviewBrush) { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            DockPanel.SetDock(play, Dock.Left);
            content.Children.Add(play);
        }
        content.Children.Add(close);
        content.Children.Add(dot);
        content.Children.Add(title);
        var box = new Border
        {
            Height = TabHeight,
            Padding = new Thickness(10, 0, 4, 0),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Background = Clear,
            BorderBrush = Clear,
            Child = content,
        };
        ToolTip.SetTip(box, info.Tip);
        // 지운 탭(같은 번호의 새 탭이 대신함)에 늦게 오는 알림은 무시한다.
        bool Current() => index < _tabs.Count && ReferenceEquals(_tabs[index].Box, box);
        box.PointerEntered += (_, _) => { if (!Current()) return; _hover = index; UpdateVisuals(); };
        box.PointerExited += (_, _) => { if (!Current()) return; if (_hover == index) _hover = -1; UpdateVisuals(); };
        box.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(box).Properties;
            if (props.IsMiddleButtonPressed)
            {
                e.Handled = true;
                RequestClose(index);
            }
            else if (props.IsLeftButtonPressed)
            {
                e.Handled = true;
                TabPressed?.Invoke(index);
            }
        };
        PopupMenus.Attach(box, () => MenuFor?.Invoke(index));
        return (box, title, close);
    }

    /// <summary>탭 끌기를 지켜본다(<see cref="DragReorder"/>와 함께 — 그쪽이 처리한 이벤트도 본다).</summary>
    private void AttachDrag(int index, Border box)
    {
        box.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
            _pressIndex = index;
            _pressAt = e.GetPosition(this);
            _dragging = false;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        box.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (_pressIndex != index) return;
            if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed)
            {
                EndDrag(null);
                return;
            }
            if (!_dragging && !ReorderCore.Exceeded(_pressAt, e.GetPosition(this))) return;
            _dragging = true;
            TabDragMoved?.Invoke(index, e);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        box.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_pressIndex != index) return;
            EndDrag(e);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        box.PointerCaptureLost += (_, _) =>
        {
            if (_pressIndex == index && _dragging) EndDrag(null);
        };
    }

    private void EndDrag(PointerEventArgs? e)
    {
        int index = _pressIndex;
        bool dragging = _dragging;
        _pressIndex = -1;
        _dragging = false;
        if (dragging) TabDragEnded?.Invoke(index, e);
    }

    private void BringActiveIntoView()
    {
        if (_active < 0 || _active >= _tabs.Count) return;
        var box = _tabs[_active].Box;
        Dispatcher.UIThread.Post(() => box.BringIntoView(), DispatcherPriority.Background);
    }

    private void UpdateVisuals()
    {
        BorderBrush = _highlight ? AppTheme.AccentBrush : AppTheme.BorderBrush;
        for (int i = 0; i < _tabs.Count; i++)
        {
            var (box, title, close) = _tabs[i];
            bool active = i == _active, hover = i == _hover, preview = i < _infos.Count && _infos[i].Preview;
            if (preview)
            {
                // 미리보기 탭: 늘 미리보기 색(고른 탭이면 더 진한 바탕 + 위 선)
                box.Background = active ? PreviewActiveBack : PreviewBack;
                box.BorderBrush = active ? PreviewBrush : Clear;
                title.Foreground = PreviewBrush;
            }
            else
            {
                box.Background = active ? AppTheme.BackBrush : hover ? TabHover : Clear;
                box.BorderBrush = active ? (_focused ? AppTheme.AccentBrush : AppTheme.BorderBrush) : Clear;
                title.Foreground = active || hover ? AppTheme.TextBrush : AppTheme.DimTextBrush;
            }
            // 닫기 X는 고른 탭·마우스를 올린 탭에만(자리는 늘 잡아 두어 탭 너비가 흔들리지 않게).
            bool showClose = active || hover;
            close.Opacity = showClose ? 1 : 0;
            close.IsHitTestVisible = showClose;
            if (close.Child is IconView x) x.Foreground = active || hover ? AppTheme.TextBrush : AppTheme.DimTextBrush;
        }
    }

    /// <summary>줄에 붙는 납작한 아이콘 단추(마우스를 올리면 밝게).</summary>
    private static Border FlatIcon(VectorIcon icon, Action onClick)
    {
        var b = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(3),
            Background = Clear,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new IconView(icon, AppTheme.DimTextBrush) { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        b.PointerEntered += (_, _) => { b.Background = AppTheme.HoverBrush; ((IconView)b.Child!).Foreground = AppTheme.TextBrush; };
        b.PointerExited += (_, _) => { b.Background = Clear; ((IconView)b.Child!).Foreground = AppTheme.DimTextBrush; };
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            onClick();
        };
        return b;
    }
}

/// <summary>
/// 포커스되지 않은 묶음의 그림(나눈 화면의 다른 쪽): 그 묶음이 고른 탭의 문서를 탭의 보기 상태(프레임·확대율·위치)대로 그리기만 한다.
/// 편집은 포커스된 묶음의 캔버스에서 — 누르면 이 묶음에 포커스가 가며 캔버스가 이리 온다. 문서가 바뀌면(다른 쪽에서 편집) 다시 그린다.
/// </summary>
internal sealed class DocumentPaneView : Control
{
    private static readonly IPen BorderPen = AppTheme.Pen(AppTheme.BorderColor);

    private DocumentTab? _tab;
    private SpriteProject? _project;
    private WriteableBitmap? _bitmap;
    private int _bitmapFrame = -1;

    public DocumentPaneView()
    {
        ClipToBounds = true;
    }

    /// <summary>위 막대 "맞추기"가 켜져 있는지(그림 전체가 보이던 탭은 이 칸에 맞춰 그림).</summary>
    public bool AutoFit { get; set; } = true;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateVisual();
    }

    /// <summary>보여 줄 탭(null = 빈 화면).</summary>
    public DocumentTab? Tab
    {
        get => _tab;
        set
        {
            _tab = value;
            Sync();
            InvalidateVisual();
        }
    }

    /// <summary>탭의 문서·보기가 바뀌었을 수 있다: 다시 맞추고 그린다.</summary>
    public void Refresh()
    {
        Sync();
        DropBitmap();
        InvalidateVisual();
    }

    private void Sync()
    {
        var project = IsAttached ? _tab?.Doc?.Project : null;
        if (ReferenceEquals(project, _project)) return;
        if (_project is not null) _project.Changed -= OnProjectChanged;
        _project = project;
        if (_project is not null) _project.Changed += OnProjectChanged;
        DropBitmap();
    }

    private bool IsAttached => this.GetVisualRoot() is not null;

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        DropBitmap();
        InvalidateVisual();
    }

    private void DropBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _bitmapFrame = -1;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_project is not null) _project.Changed -= OnProjectChanged;
        _project = null;
        DropBitmap();
    }

    public override void Render(DrawingContext g)
    {
        g.FillRectangle(AppTheme.BackBrush, new Rect(Bounds.Size));
        if (_tab is null || _project is not { Frames.Count: > 0 } p) return;
        var view = _tab.View;
        int frame = Math.Clamp(view?.Frame ?? 0, 0, p.Frames.Count - 1);
        double w = Bounds.Width, h = Bounds.Height;
        int fitZoom = Math.Clamp((int)Math.Floor(Math.Min((w - 24) / p.Width, (h - 24) / p.Height)), 1, 64);
        int zoom;
        double ox, oy;
        // 캔버스와 같은 규칙(PixelCanvas.ApplyView): 그림 전체가 보이던 보기(처음 보이는 탭 포함)면 이 칸에 맞추고(맞추기가 꺼져 있으면 확대율을 두고 가운데),
        // 아니면 확대율을 두고 가리키던 그림 좌표를 이 칸 가운데에.
        if (view is not { Canvas: var v } || v.WholeVisible)
        {
            zoom = view is { Canvas: var c } && !AutoFit && p.Width * c.Zoom <= w - 24 && p.Height * c.Zoom <= h - 24 ? Math.Clamp(c.Zoom, 1, 64) : fitZoom;
            ox = Math.Floor((w - p.Width * zoom) / 2);
            oy = Math.Floor((h - p.Height * zoom) / 2);
        }
        else
        {
            zoom = Math.Clamp(v.Zoom, 1, 64);
            // 부위 화면이면 캔버스 좌표는 부위 기준 — 프레임 좌표로 옮겨 같은 자리를 가운데에.
            var origin = view.Value.Layer >= 0 && view.Value.Layer < p.Layers.Count && p.Layers[view.Value.Layer] is { IsPart: true } part ? p.GetPartOffset(part, frame) : Point.Empty;
            ox = Math.Round(w / 2 - (v.CenterX + origin.X) * zoom);
            oy = Math.Round(h / 2 - (v.CenterY + origin.Y) * zoom);
        }
        if (_bitmap is null || _bitmapFrame != frame)
        {
            _bitmap?.Dispose();
            _bitmap = ImageUtil.ToBitmap(p.Flatten(frame));
            _bitmapFrame = frame;
        }
        var dest = new Rect(ox, oy, p.Width * zoom, p.Height * zoom);
        var visible = dest.Intersect(new Rect(Bounds.Size));
        if (visible.Width > 0 && visible.Height > 0)
        {
            AppTheme.DrawChecker(g, visible, 8);
            var src = new Rect((visible.X - ox) / zoom, (visible.Y - oy) / zoom, visible.Width / zoom, visible.Height / zoom);
            using (ImageUtil.Pixelated(g))
                g.DrawImage(_bitmap, src, visible);
        }
        g.DrawRectangle(null, BorderPen, new Rect(dest.X - 0.5, dest.Y - 0.5, dest.Width + 1, dest.Height + 1));
    }
}

/// <summary>편집기 묶음 하나의 칸: 위 탭 줄 + 고른 문서의 막대(<see cref="OpenDocument.Bar"/>, 있을 때만) + 아래 그림 칸(포커스된 묶음은 캔버스, 아니면 <see cref="DocumentPaneView"/>).</summary>
internal sealed class DocumentPane : DockPanel
{
    public DocumentPane()
    {
        LastChildFill = true;
        DockPanel.SetDock(Strip, Dock.Top);
        Children.Add(Strip);
        DockPanel.SetDock(Bar, Dock.Top);
        Children.Add(Bar);
        Content.Children.Add(Passive);
        Children.Add(Content);
    }

    public DocumentTabStrip Strip { get; } = new();

    /// <summary>고른 탭 문서의 막대 자리(<see cref="OpenDocument.Bar"/>, 없으면 숨김).</summary>
    public ContentControl Bar { get; } = new() { IsVisible = false };

    /// <summary>그림 칸(캔버스나 <see cref="Passive"/>를 담음).</summary>
    public Grid Content { get; } = new() { Background = AppTheme.BackBrush, ClipToBounds = true };

    public DocumentPaneView Passive { get; } = new();
}
