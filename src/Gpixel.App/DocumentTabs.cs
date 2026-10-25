using Avalonia.VisualTree;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>문서 탭 하나의 겉모습: 제목(파일 이름), 풍선 도움말(전체 경로), 저장 안 한 변경 표시.</summary>
internal readonly record struct DocumentTabInfo(string Title, string Tip, bool Dirty);

/// <summary>
/// 캔버스 위 문서 탭 줄(편집기 묶음 하나): 탭마다 제목·저장 안 한 변경 점·닫기 X, 끝에 "+"(새 문서), 오른쪽 끝에 나눠 열기 단추.
/// 왼쪽 클릭 = 고르기, 가운데 클릭·X = 닫기, 우클릭 = <see cref="MenuFor"/> 메뉴. 끌면 순서를 바꾸고(<see cref="DragReorder"/> — 앱의 다른 목록과 같은 모양),
/// 끄는 동안·놓을 때 <see cref="TabDragMoved"/>·<see cref="TabDragEnded"/>로 위치를 알려 캔버스 쪽에 놓아 나누기를 할 수 있다.
/// 포커스된 묶음이면 고른 탭 위에 강조색 선(좌우로 나눴으면 줄 아래 선도).
/// </summary>
internal sealed class DocumentTabStrip : Border
{
    private const double TabHeight = 28;
    private static readonly IBrush Clear = Brushes.Transparent;
    private static readonly IBrush TabHover = AppTheme.Brush(Color.FromArgb(18, 255, 255, 255));

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

    public DocumentTabStrip()
    {
        Background = AppTheme.PanelBrush;
        BorderBrush = AppTheme.BorderBrush;
        BorderThickness = new Thickness(0, 0, 0, 2);
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
        _splitButton = FlatIcon(VectorIcon.Columns2, () => SplitRequested?.Invoke());
        _splitButton.Margin = new Thickness(3, 0, 4, 0);
        DockPanel.SetDock(_splitButton, Dock.Right);
        Child = new DockPanel { LastChildFill = true, Height = TabHeight, Children = { _splitButton, _scroll } };
        _reorder.Moved += (from, to) => Reordered?.Invoke(from, to);
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
        UpdateVisuals();
        BringActiveIntoView();
    }

    private (Border Box, TextBlock Title, Border Close) MakeTab(int index, DocumentTabInfo info)
    {
        var title = new TextBlock
        {
            Text = info.Title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220,
        };
        var dot = new IconView(VectorIcon.Dot, AppTheme.UnsavedBrush) { Width = 8, Height = 8, Filled = true, IsVisible = info.Dirty, VerticalAlignment = VerticalAlignment.Center };
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
            CloseRequested?.Invoke(index);
        };
        var box = new Border
        {
            Height = TabHeight,
            Padding = new Thickness(10, 0, 4, 0),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Background = Clear,
            BorderBrush = Clear,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { title, dot, close } },
        };
        ToolTip.SetTip(box, info.Tip);
        box.PointerEntered += (_, _) => { _hover = index; UpdateVisuals(); };
        box.PointerExited += (_, _) => { if (_hover == index) _hover = -1; UpdateVisuals(); };
        box.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(box).Properties;
            if (props.IsMiddleButtonPressed)
            {
                e.Handled = true;
                CloseRequested?.Invoke(index);
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
            bool active = i == _active, hover = i == _hover;
            box.Background = active ? AppTheme.BackBrush : hover ? TabHover : Clear;
            box.BorderBrush = active ? (_focused ? AppTheme.AccentBrush : AppTheme.BorderBrush) : Clear;
            title.Foreground = active || hover ? AppTheme.TextBrush : AppTheme.DimTextBrush;
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
        var project = IsAttached ? _tab?.Doc.Project : null;
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
        int zoom;
        double ox, oy;
        if (view is { } v)
        {
            zoom = Math.Clamp(v.Zoom, 1, 64);
            // 부위 화면이면 캔버스는 부위를 (0,0)에 그린다 — 프레임 전체를 그 부위 위치만큼 당겨 같은 자리에 보이게.
            var origin = v.Layer >= 0 && v.Layer < p.Layers.Count && p.Layers[v.Layer] is { IsPart: true } part ? part.PartOffset : Point.Empty;
            ox = v.Offset.X - origin.X * zoom;
            oy = v.Offset.Y - origin.Y * zoom;
        }
        else
        {
            zoom = Math.Clamp((int)Math.Floor(Math.Min((Bounds.Width - 24) / p.Width, (Bounds.Height - 24) / p.Height)), 1, 64);
            ox = Math.Floor((Bounds.Width - p.Width * zoom) / 2);
            oy = Math.Floor((Bounds.Height - p.Height * zoom) / 2);
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

/// <summary>편집기 묶음 하나의 칸: 위 탭 줄 + 아래 그림 칸(포커스된 묶음은 캔버스, 아니면 <see cref="DocumentPaneView"/>).</summary>
internal sealed class DocumentPane : DockPanel
{
    public DocumentPane()
    {
        LastChildFill = true;
        DockPanel.SetDock(Strip, Dock.Top);
        Children.Add(Strip);
        Content.Children.Add(Passive);
        Children.Add(Content);
    }

    public DocumentTabStrip Strip { get; } = new();

    /// <summary>그림 칸(캔버스나 <see cref="Passive"/>를 담음).</summary>
    public Grid Content { get; } = new() { Background = AppTheme.BackBrush, ClipToBounds = true };

    public DocumentPaneView Passive { get; } = new();
}
