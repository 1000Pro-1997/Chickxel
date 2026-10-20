using Avalonia.Input.Platform;
using Gpixel.App.PlayTest;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// Gpixel 메인 창(Aseprite식 배치): 메뉴, 도구 옵션 막대(위), 세로 도구 막대(왼쪽 끝), 팔레트·색 열,
/// 캔버스(가운데), 타임라인(아래), 미리보기·Unity/내보내기 독(오른쪽), 접을 수 있는 파일 브라우저(맨 왼쪽), 상태 표시줄.
/// .gpixel 프로젝트 열기/저장(+시트 내보내기), 단축키, 설정 저장을 맡는다.
/// </summary>
public sealed class MainForm : Window, Ai.IAiHost
{
    private enum KeyScope
    {
        /// <summary>어디에 포커스가 있어도 동작.</summary>
        Global,
        /// <summary>글자 입력란에 포커스가 있으면 그 입력란이 키를 받는다.</summary>
        NotInText,
        /// <summary>캔버스에 포커스가 있을 때만.</summary>
        Canvas,
    }

    private static readonly (ToolKind Tool, string Name, string Key, string Hint)[] ToolDefs =
    [
        (ToolKind.Pencil, "연필", "B", "왼쪽: 기본색 · 오른쪽: 보조색(또는 지우개) · Shift+클릭: 마지막 점에서 직선 · Alt+클릭: 색 집기"),
        (ToolKind.Eraser, "지우개", "E", "투명으로 지웁니다"),
        (ToolKind.Fill, "채우기", "G", "클릭한 곳의 색(허용 오차 안의 비슷한 색 포함) 영역을 채웁니다"),
        (ToolKind.Gradient, "그라데이션", "F", "끌어서 시작점 → 끝점 방향으로 칠합니다(선택 영역이 있으면 그 안만) · Shift: 45° 단위 · 색 정지점·보간·디더링은 도구 설정"),
        (ToolKind.Picker, "스포이드", "I", "왼쪽: 기본색 · 오른쪽: 보조색으로 집기"),
        (ToolKind.Line, "선", "L", "Shift: 45° 단위"),
        (ToolKind.Rectangle, "사각형", "R", "Shift: 정사각형 · U: 외곽/채움"),
        (ToolKind.Ellipse, "원", "O", "Shift: 정원 · U: 외곽/채움"),
        (ToolKind.Select, "사각 선택", "", "드래그로 선택(Shift: 정사각형), 안쪽을 끌어 이동 · Enter: 내려놓기 · M: 다음 선택 도구"),
        (ToolKind.EllipseSelect, "원 선택", "", "드래그로 원 선택(Shift: 정원), 안쪽을 끌어 이동 · Enter: 내려놓기 · M: 다음 선택 도구"),
        (ToolKind.Lasso, "자유 선택", "Q", "끌어서 테두리를 그리면 놓을 때 닫아 선택, 안쪽을 끌어 이동 · Enter: 내려놓기"),
        (ToolKind.MagicWand, "스마트 선택", "W", "클릭한 곳과 비슷한 색 영역 선택(허용 오차·이어진 영역·기준은 채우기와 같은 설정) · 옮기기: V/방향키"),
        (ToolKind.Move, "이동", "V", "선택이 있으면 선택 이동, 없으면 셀 전체 밀기(순환 옵션) · 다른 도구에서도 Ctrl+드래그(누르는 동안 이동 도구)"),
        (ToolKind.Dodge, "밝게/어둡게", "D", "왼쪽: 밝게 · 오른쪽: 어둡게(한 획에 한 번)"),
        (ToolKind.Dither, "디더링", "T", "무늬(체커·25%·75%)대로 기본색·보조색을 섞어 칠합니다"),
        (ToolKind.ReplaceBrush, "색 바꾸기 브러시", "C", "처음 닿은 색만 기본색으로 바꿉니다"),
        (ToolKind.Pivot, "피벗", "K", "끌어서 피벗(Unity 포커스)을 옮깁니다"),
        (ToolKind.Slice, "9-slice", "S", "초록 선을 끌어 늘려도 안 늘어나는 테두리(Unity Sprite Border)를 정합니다 — UI 버튼·패널용"),
    ];

    /// <summary>
    /// 도구 막대 칸(위에서부터). 한 칸의 도구는 서로 바꿔 쓰는 변형이다: 채우기(채우기·그라데이션), 도형(선·사각형·원), 선택(사각·원·자유·스마트).
    /// 칸은 마지막으로 고른 도구를 보이고, 길게 누르거나 오른쪽 클릭하면 옆에 펼쳐 바꾼다(<see cref="ToolSlotButton"/>).
    /// </summary>
    private static readonly ToolKind[][] ToolSlots =
    [
        [ToolKind.Pencil], [ToolKind.Eraser], [ToolKind.Fill, ToolKind.Gradient], [ToolKind.Picker],
        [ToolKind.Line, ToolKind.Rectangle, ToolKind.Ellipse],
        [ToolKind.Select, ToolKind.EllipseSelect, ToolKind.Lasso, ToolKind.MagicWand],
        [ToolKind.Move], [ToolKind.Dodge], [ToolKind.Dither], [ToolKind.ReplaceBrush], [ToolKind.Pivot], [ToolKind.Slice],
    ];

    /// <summary>도구 이름(예: "연필").</summary>
    internal static string ToolName(ToolKind tool) => ToolDefs.First(d => d.Tool == tool).Name;

    /// <summary>단축키를 바꿀 수 있는 명령들(메뉴를 만들 때 등록, <see cref="ApplyShortcuts"/>가 키 표를 만든다).</summary>
    private readonly List<CommandEntry> _commands = new();
    /// <summary>지금 만드는 위 메뉴 이름(명령 분류).</summary>
    private string _cmdCategory = "";
    private readonly Dictionary<(Key Key, KeyModifiers Mods), (KeyScope Scope, Func<bool> Action)> _keys = new();

    /// <summary>단축키의 "Ctrl" 자리(macOS = ⌘).</summary>
    private static readonly KeyModifiers Ctrl = Platform.CommandModifier;
    private const KeyModifiers Shift = KeyModifiers.Shift;
    private const KeyModifiers Alt = KeyModifiers.Alt;
    private const KeyModifiers NoMods = KeyModifiers.None;

    /// <summary>시스템 클립보드의 PNG 형식(Windows "PNG", macOS "public.png", 그 밖 "image/png").</summary>
    private static readonly DataFormat<byte[]> PngClipboardFormat = DataFormat.CreateBytesPlatformFormat(
        OperatingSystem.IsWindows() ? "PNG" : Platform.IsMac ? "public.png" : "image/png");

    private const int SplitterSize = 3;
    private const int MinPanelSize = 25;

    private readonly AppSettings _settings;
    private readonly StartupArgs _args;
    private readonly EditorContext _ctx = new();

    private readonly PixelCanvas _canvas;
    private readonly StartPanel _start;
    private readonly FileBrowserPanel _browser;
    private readonly ColorPanel _colors;
    private readonly ToolOptionsPanel _toolOptions;
    private readonly TimelinePanel _timeline;
    private readonly PreviewPanel _preview;

    // 나눔 막대(Grid + GridSplitter): 고정되는 쪽의 열/행만 픽셀 크기, 나머지는 *.
    private readonly Grid _splitFiles, _splitPalette, _splitRight, _splitTimeline, _splitRightDock;
    private readonly ColumnDefinition _colFiles, _colPalette, _colRight;
    private readonly RowDefinition _rowBottom, _rowPreview;
    private readonly GridSplitter _filesSplitter;

    private readonly List<ToolSlotButton> _toolSlots = new();
    private ToggleButton _tbFiles = null!;
    private TextBlock _cbToolName = null!, _cbHint = null!, _cbZoom = null!;
    private ToggleButton _cbGrid = null!, _cbTiles = null!, _cbTiled = null!, _cbPivot = null!, _cbOnion = null!, _cbFit = null!;
    /// <summary>새 버전이 있을 때만 보이는 위 막대 단추(누르면 업데이트 창).</summary>
    private Button _cbUpdate = null!;

    private MenuItem _miUndo = null!, _miRedo = null!, _miRecentFolders = null!, _miRecentFiles = null!;
    private MenuItem _miGrid = null!, _miTiles = null!, _miTiled = null!, _miPivot = null!, _miOnion = null!, _miFiles = null!;
    private MenuItem _miPixelPerfect = null!, _miFilled = null!, _miGlobalFill = null!, _miMirrorX = null!, _miMirrorY = null!,
        _miRightErase = null!, _miWrap = null!;
    private readonly List<MenuItem> _docItems = new();
    private MenuItem _miPlayTest = null!;
    private readonly Dictionary<ToolKind, MenuItem> _toolMenuItems = new();

    private readonly TextBlock _stPos, _stColor, _stSize, _stFrame, _stZoom, _stDirty, _stMessage;

    private PlayTestForm? _play;
    private readonly DispatcherTimer _playRefresh = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private DateTime _docStamp;
    private bool _settingsApplied;
    private bool _closeConfirmed, _closePending;
    private Ai.McpServer? _mcp;
    private Ai.ChatWindow? _chat;
    private readonly Updater _updater;
    /// <summary>최대화 전 일반 창 위치·크기(설정 저장용).</summary>
    private PixelPoint _normalPosition;
    private Avalonia.Size _normalSize;

    public MainForm(StartupArgs args)
    {
        _args = args;
        _settings = AppSettings.Load();
        _updater = new Updater(_settings, this, ShowMessage, ShowUpdateButton, ConfirmDiscard, ExitForUpdate);
        Title = "Gpixel";
        Background = AppTheme.BackBrush;
        Foreground = AppTheme.TextBrush;
        FontFamily = AppTheme.FontFamily;
        FontSize = AppTheme.FontSize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 1280;
        Height = 800;
        MinWidth = 1000;
        MinHeight = 600;

        // ── 패널
        _canvas = new PixelCanvas(_ctx);
        _start = new StartPanel();
        _browser = new FileBrowserPanel();
        _colors = new ColorPanel(_ctx);
        _toolOptions = new ToolOptionsPanel(_ctx);
        _preview = new PreviewPanel(_ctx);
        _timeline = new TimelinePanel(_ctx, _preview);

        var canvasHost = new Panel { Background = AppTheme.BackBrush };
        canvasHost.Children.Add(_canvas);
        canvasHost.Children.Add(_start);

        var s0 = _settings;
        (_splitRightDock, _rowPreview, _) = MakeRowSplit(_preview, _toolOptions, fixedFirst: true, s0.PreviewHeight);
        (_splitTimeline, _rowBottom, _) = MakeRowSplit(canvasHost, _timeline, fixedFirst: false, s0.BottomHeight);
        (_splitRight, _colRight, _) = MakeColumnSplit(_splitTimeline, _splitRightDock, fixedFirst: false, s0.RightWidth);
        (_splitPalette, _colPalette, _) = MakeColumnSplit(_colors, _splitRight, fixedFirst: true, s0.PaletteWidth);
        var work = new DockPanel { Background = AppTheme.BackBrush };
        var toolBar = BuildToolBar();
        DockPanel.SetDock(toolBar, Dock.Left);
        work.Children.Add(toolBar);
        work.Children.Add(_splitPalette);
        (_splitFiles, _colFiles, _filesSplitter) = MakeColumnSplit(_browser, work, fixedFirst: true, s0.LeftWidth);

        // ── 상태 표시줄
        var statusBar = new DockPanel { Height = 22, LastChildFill = true };
        _stPos = StatusLabel(statusBar, 80);
        _stColor = StatusLabel(statusBar, 100);
        _stSize = StatusLabel(statusBar, 90);
        _stFrame = StatusLabel(statusBar, 180);
        _stZoom = StatusLabel(statusBar, 60);
        _stDirty = StatusLabel(statusBar, 80);
        _stMessage = new TextBlock
        {
            Foreground = AppTheme.DimTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 0, 6, 0),
        };
        statusBar.Children.Add(_stMessage);
        var status = new Border
        {
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = statusBar,
        };

        var menu = BuildMenu();
        var contextBar = BuildContextBar();
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(menu, Dock.Top);
        DockPanel.SetDock(contextBar, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(menu);
        root.Children.Add(contextBar);
        root.Children.Add(status);
        root.Children.Add(_splitFiles);
        Content = root;

        KeyState.Attach(this);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => OnShown();
        Closing += OnWindowClosing;
        // Windows는 최대화할 때 WindowState가 바뀌기 전에 위치·크기 이벤트가 오므로 상태가 정해진 뒤에 기록한다.
        PositionChanged += (_, _) => Dispatcher.UIThread.Post(RememberNormalBounds);
        SizeChanged += (_, _) => Dispatcher.UIThread.Post(RememberNormalBounds);

        WireEvents();
        ApplySettingsBeforeShow();
        UpdateAll();
    }

    // ───────────── 구성 ─────────────

    private static GridSplitter Splitter(GridResizeDirection direction)
    {
        var sp = new GridSplitter
        {
            ResizeDirection = direction,
            Background = AppTheme.BorderBrush,
            Focusable = false,
        };
        if (direction == GridResizeDirection.Columns) { sp.Width = SplitterSize; sp.MinWidth = SplitterSize; }
        else { sp.Height = SplitterSize; sp.MinHeight = SplitterSize; }
        return sp;
    }

    /// <summary>좌우로 나눈 칸. <paramref name="fixedFirst"/>면 왼쪽, 아니면 오른쪽이 고정 너비(창 크기가 바뀌어도 그대로).</summary>
    private static (Grid Grid, ColumnDefinition Fixed, GridSplitter Splitter) MakeColumnSplit(Control a, Control b, bool fixedFirst, int size)
    {
        var fixedCol = new ColumnDefinition(new GridLength(size)) { MinWidth = MinPanelSize };
        var starCol = new ColumnDefinition(1, GridUnitType.Star) { MinWidth = MinPanelSize };
        var grid = new Grid { Background = AppTheme.PanelBrush };
        grid.ColumnDefinitions.Add(fixedFirst ? fixedCol : starCol);
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(fixedFirst ? starCol : fixedCol);
        var sp = Splitter(GridResizeDirection.Columns);
        Grid.SetColumn(a, 0);
        Grid.SetColumn(sp, 1);
        Grid.SetColumn(b, 2);
        grid.Children.Add(a);
        grid.Children.Add(sp);
        grid.Children.Add(b);
        return (grid, fixedCol, sp);
    }

    /// <summary>위아래로 나눈 칸. <paramref name="fixedFirst"/>면 위, 아니면 아래가 고정 높이.</summary>
    private static (Grid Grid, RowDefinition Fixed, GridSplitter Splitter) MakeRowSplit(Control a, Control b, bool fixedFirst, int size)
    {
        var fixedRow = new RowDefinition(new GridLength(size)) { MinHeight = MinPanelSize };
        var starRow = new RowDefinition(1, GridUnitType.Star) { MinHeight = MinPanelSize };
        var grid = new Grid { Background = AppTheme.PanelBrush };
        grid.RowDefinitions.Add(fixedFirst ? fixedRow : starRow);
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(fixedFirst ? starRow : fixedRow);
        var sp = Splitter(GridResizeDirection.Rows);
        Grid.SetRow(a, 0);
        Grid.SetRow(sp, 1);
        Grid.SetRow(b, 2);
        grid.Children.Add(a);
        grid.Children.Add(sp);
        grid.Children.Add(b);
        return (grid, fixedRow, sp);
    }

    /// <summary>고정 칸 크기를 나눔 전체 크기 안으로 맞춰 정한다(아직 배치 전이면 그대로).</summary>
    private static double ClampSplit(double value, double total)
    {
        double max = total - MinPanelSize - SplitterSize;
        return max <= MinPanelSize ? Math.Max(MinPanelSize, value) : Math.Clamp(value, MinPanelSize, max);
    }

    private static void SetSplit(ColumnDefinition col, Grid grid, int width)
        => col.Width = new GridLength(ClampSplit(width, grid.Bounds.Width));

    private static void SetSplit(RowDefinition row, Grid grid, int height)
        => row.Height = new GridLength(ClampSplit(height, grid.Bounds.Height));

    private static TextBlock StatusLabel(DockPanel host, int width)
    {
        var text = new TextBlock
        {
            Foreground = AppTheme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var cell = new Border
        {
            Width = width,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(4, 0, 4, 0),
            Child = text,
        };
        DockPanel.SetDock(cell, Dock.Left);
        host.Children.Add(cell);
        return text;
    }

    internal static Image IconImage(Bitmap bitmap, double size)
    {
        var img = new Image { Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform };
        // 원래 크기 이상은 점 그대로, 메뉴처럼 줄일 때는 부드럽게(점이 빠지지 않게).
        RenderOptions.SetBitmapInterpolationMode(img, size >= bitmap.PixelSize.Width ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        return img;
    }

    /// <summary>왼쪽 끝 세로 도구 막대(점 그림 아이콘, 도움말에 단축키).</summary>
    private Control BuildToolBar()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1 };
        _tbFiles = ToolBarButton(Icons.Folder, "파일 브라우저 보이기/숨기기 (F8)");
        _tbFiles.Margin = new Thickness(1, 1, 1, 6);
        _tbFiles.Click += (_, _) => SetFilesVisible(!_filesShown, animate: true);
        stack.Children.Add(_tbFiles);
        foreach (var slotTools in ToolSlots)
        {
            var slot = new ToolSlotButton(slotTools.Select(t => ToolDefs.First(d => d.Tool == t)).Select(d => (d.Tool, d.Name)).ToList(), ToolKeyText);
            slot.ToolPicked += t => { _ctx.Tool = t; UpdateToolUi(); _canvas.Focus(); };
            _toolSlots.Add(slot);
            stack.Children.Add(slot);
        }
        return new Border
        {
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(2, 4, 2, 4),
            Width = Icons.Size + 14,
            // 32px 아이콘 칸 12개(도형·선택은 묶음) — 창이 낮으면 스크롤된다.
            Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
    }

    private static ToggleButton ToolBarButton(Bitmap icon, string tip) => new ToggleButton
    {
        Content = IconImage(icon, Icons.Size),
        Padding = new Thickness(2),
        Margin = new Thickness(1),
        MinWidth = 0,
        MinHeight = 0,
        HorizontalAlignment = HorizontalAlignment.Center,
        Focusable = false,
    }.Tip(tip);

    /// <summary>메뉴 아래 막대: 현재 도구 이름·도움말(세부 설정은 오른쪽 독 "도구 설정") + 보기 옵션·확대율.</summary>
    private Control BuildContextBar()
    {
        var bar = new DockPanel { LastChildFill = true };

        // 오른쪽 정렬: (새 버전 알림) 보기·확대
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        DockPanel.SetDock(right, Dock.Right);
        _cbUpdate = BarButton(right, "업데이트", "새 버전이 나왔습니다 — 눌러서 바뀐 점 보기·설치", () =>
        {
            if (_updater.Available is { } release) _updater.ShowUpdateDialog(release);
        });
        _cbUpdate.Background = AppTheme.AccentBrush;
        _cbUpdate.Foreground = Brushes.White;
        _cbUpdate.FontWeight = FontWeight.Bold;
        _cbUpdate.Margin = new Thickness(1, 1, 6, 1);
        _cbUpdate.IsVisible = false;
        _cbGrid = Toggle(right, "격자", "픽셀 격자(6배 이상) (Ctrl+G)", () => SetPixelGrid(!_canvas.ShowPixelGrid));
        _cbTiles = Toggle(right, "타일 격자", "타일 격자 (Ctrl+Shift+G)", () => SetTileGrid(!_canvas.ShowTileGrid));
        _cbTiled = Toggle(right, "타일 모드", "반복해 보기(크지 않으면 5×5, 128px 넘으면 3×3)·이어 그리기 (Ctrl+Shift+T)", () => SetTiledMode(!_canvas.TiledMode));
        _cbPivot = Toggle(right, "피벗", "피벗 표시", () => SetShowPivot(!_canvas.ShowPivot));
        _cbOnion = Toggle(right, "양파", "양파 껍질: 앞/뒤 프레임 겹쳐 보기 (Ctrl+J)", () => _ctx.OnionSkin = !_ctx.OnionSkin);
        right.Children.Add(BarSeparator());
        BarButton(right, "－", "축소 (Ctrl+-)", () => _canvas.ZoomStep(-1));
        _cbZoom = new TextBlock { Text = "800%", TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(ZoomPicker());
        BarButton(right, "＋", "확대 (Ctrl+=)", () => _canvas.ZoomStep(1));
        _cbFit = Toggle(right, "맞추기", "켜면 이미지를 열 때마다 화면에 맞춥니다(켜는 순간에도 한 번 맞춤) · Ctrl+0: 지금 맞추기", () =>
        {
            _canvas.AutoFit = !_canvas.AutoFit;
            if (_canvas.AutoFit) _canvas.FitToWindow();
        });
        right.Children.Add(BarSeparator());
        var claude = BarButton(right, "", "AI 채팅(Claude·Codex): 말로 그림 만들기·고치기 (Ctrl+L)", ToggleChat);
        claude.Content = IconImage(Icons.Claude, 16);
        claude.Padding = new Thickness(5, 3);
        bar.Children.Add(right);

        _cbToolName = new TextBlock
        {
            Text = "연필",
            FontWeight = FontWeight.Bold,
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        AddLeft(bar, _cbToolName);
        AddLeft(bar, BarSeparator());
        _cbHint = new TextBlock
        {
            Foreground = AppTheme.DimTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 6, 0),
        };
        bar.Children.Add(_cbHint);

        return new Border
        {
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 2, 6, 2),
            Child = bar,
        };
    }

    /// <summary>확대율 글자: 누르면 대표 배율(100%~6400%)과 "화면에 맞추기"가 든 목록.</summary>
    private Button ZoomPicker()
    {
        List<Control> Items()
        {
            var fit = new MenuItem { Header = "화면에 맞추기", InputGesture = new KeyGesture(Key.D0, Ctrl) };
            fit.Click += (_, _) => _canvas.FitToWindow();
            var items = new List<Control> { fit, new Separator() };
            foreach (int z in new[] { 1, 2, 4, 8, 16, 32, 64 })
            {
                var item = new MenuItem { Header = $"{z * 100}%", ToggleType = MenuItemToggleType.Radio, IsChecked = _canvas.Zoom == z };
                item.Click += (_, _) => _canvas.SetZoom(z);
                items.Add(item);
            }
            return items;
        }
        var button = new Button
        {
            Content = _cbZoom,
            Width = 62,
            Padding = new Thickness(2),
            Margin = new Thickness(1),
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        }.Tip("확대율 — 눌러서 배율 고르기·화면에 맞추기");
        button.Click += (_, _) => PopupMenus.Show(button, Items(), PlacementMode.BottomEdgeAlignedLeft);
        return button;
    }

    private static void AddLeft(DockPanel bar, Control c)
    {
        DockPanel.SetDock(c, Dock.Left);
        c.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(c);
    }

    private static Border BarSeparator() => new()
    {
        Width = 1,
        Background = AppTheme.BorderBrush,
        Margin = new Thickness(5, 3, 5, 3),
    };

    /// <summary>켜고 끄는 글자 단추. 눌린 상태는 <see cref="UpdateToolUi"/>가 실제 값으로 맞춘다.</summary>
    private ToggleButton Toggle(Panel host, string text, string tip, Action onClick)
    {
        var b = new ToggleButton
        {
            Content = text,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(1),
            MinHeight = 0,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        }.Tip(tip);
        b.Click += (_, _) => { onClick(); UpdateToolUi(); };
        host.Children.Add(b);
        return b;
    }

    private static Button BarButton(Panel host, string text, string tip, Action onClick)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(1),
            MinHeight = 0,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        }.Tip(tip);
        b.Click += (_, _) => onClick();
        host.Children.Add(b);
        return b;
    }

    private Menu BuildMenu()
    {
        var menu = new Menu { Background = AppTheme.PanelBrush };

        // 파일
        var file = TopMenu("파일(_F)");
        _miRecentFolders = new MenuItem { Header = MenuHeader("최근 폴더", null) };
        _miRecentFiles = new MenuItem { Header = MenuHeader("최근 파일", null) };
        _miRecentFolders.SubmenuOpened += (_, _) => FillRecent(_miRecentFolders, _settings.RecentFolders, SetRootFolder);
        _miRecentFiles.SubmenuOpened += (_, _) => FillRecent(_miRecentFiles, _settings.RecentFiles, p => OpenFile(p));
        _miRecentFolders.Items.Add(EmptyItem());
        _miRecentFiles.Items.Add(EmptyItem());
        AddItems(file,
            Cmd("새 프로젝트…", () => NewProject(null), "Ctrl+N", KeyScope.Global, (Key.N, Ctrl)),
            Cmd("열기(.gpixel / PNG 가져오기)…", OpenFileDialogCmd, "Ctrl+O", KeyScope.Global, (Key.O, Ctrl)),
            Cmd("폴더 열기…", OpenFolder, "Ctrl+Shift+O", KeyScope.Global, (Key.O, Ctrl | Shift)),
            Cmd("Unity 프로젝트 열기…", OpenUnityProject, "Ctrl+Shift+U", KeyScope.Global, (Key.U, Ctrl | Shift)),
            _miRecentFolders,
            _miRecentFiles,
            new Separator(),
            DocCmd("저장(+ 시트 내보내기)", () => Save(), "Ctrl+S", KeyScope.Global, (Key.S, Ctrl)),
            DocCmd("다른 이름으로 저장…", () => SaveAs(), "Ctrl+Shift+S", KeyScope.Global, (Key.S, Ctrl | Shift)),
            DocCmd("내보내기…", OpenExportDialog, "Ctrl+E", KeyScope.Global, (Key.E, Ctrl)),
            DocCmd("GIF 애니메이션으로 내보내기…", ExportGif),
            DocCmd("닫기", () => CloseProject(), "Ctrl+W", KeyScope.Global, (Key.W, Ctrl)),
            new Separator(),
            Cmd("시작 화면", ShowStart),
            // 끝내기 단축키는 OS가 처리한다(Windows Alt+F4, macOS 앱 메뉴의 ⌘Q).
            Cmd("끝내기", Close, Platform.IsMac ? "Ctrl+Q" : "Alt+F4"));

        // 편집
        var edit = TopMenu("편집(_E)");
        _miUndo = Cmd("실행취소", Undo, "Ctrl+Z", KeyScope.NotInText, (Key.Z, Ctrl));
        _miRedo = Cmd("다시 실행", Redo, "Ctrl+Y", KeyScope.NotInText, (Key.Y, Ctrl), (Key.Z, Ctrl | Shift));
        (Key, KeyModifiers)[] deleteKeys = Platform.IsMac ? [(Key.Delete, NoMods), (Key.Back, NoMods)] : [(Key.Delete, NoMods)];
        AddItems(edit,
            _miUndo,
            _miRedo,
            new Separator(),
            DocCmd("잘라내기", Cut, "Ctrl+X", KeyScope.NotInText, (Key.X, Ctrl)),
            DocCmd("복사", Copy, "Ctrl+C", KeyScope.NotInText, (Key.C, Ctrl)),
            DocCmd("붙여넣기", Paste, "Ctrl+V", KeyScope.NotInText, (Key.V, Ctrl)),
            DocCmd("지우기", DeleteSelection, "Delete", KeyScope.Canvas, deleteKeys),
            new Separator(),
            DocCmd("모두 선택", SelectAll, "Ctrl+A", KeyScope.NotInText, (Key.A, Ctrl)),
            DocCmd("선택 해제", Deselect, "Ctrl+D", KeyScope.NotInText, (Key.D, Ctrl)),
            new Separator(),
            DocCmd("좌우 뒤집기(선택/셀)", () => Flip(true), "Shift+H", KeyScope.NotInText, (Key.H, Shift)),
            DocCmd("상하 뒤집기(선택/셀)", () => Flip(false), "Shift+V", KeyScope.NotInText, (Key.V, Shift)),
            DocCmd("90° 회전(선택/캔버스)", () => Rotate(true), "Ctrl+R", KeyScope.NotInText, (Key.R, Ctrl)),
            DocCmd("-90° 회전(선택/캔버스)", () => Rotate(false), "Ctrl+Shift+R", KeyScope.NotInText, (Key.R, Ctrl | Shift)),
            DocCmd("변형(회전·크기)…", BeginTransform, "Ctrl+T", KeyScope.NotInText, (Key.T, Ctrl)),
            new Separator(),
            DocCmd("외곽선 추가(기본색)", () => AddOutline(false), "Ctrl+Alt+O", KeyScope.NotInText, (Key.O, Ctrl | Alt)),
            DocCmd("외곽선 추가(대각 포함)", () => AddOutline(true)),
            DocCmd("색 바꾸기…", ReplaceColor, "Ctrl+Alt+R", KeyScope.NotInText, (Key.R, Ctrl | Alt)),
            new Separator(),
            DocCmd("캔버스 크기…", ResizeCanvas, "Ctrl+Alt+C", KeyScope.Global, (Key.C, Ctrl | Alt)),
            DocCmd("캔버스 전체 좌우 뒤집기(모든 프레임)", () => _ctx.Structural(() => _ctx.Project!.FlipCanvasHorizontal())),
            DocCmd("캔버스 전체 상하 뒤집기(모든 프레임)", () => _ctx.Structural(() => _ctx.Project!.FlipCanvasVertical())),
            DocCmd("이미지에서 팔레트 만들기", _colors.PaletteFromImage),
            DocCmd("팔레트 색으로 변환…", _colors.ConvertImageToActivePalette),
            new Separator(),
            Cmd("환경 설정…", ShowPreferences, "Ctrl+,", KeyScope.Global, (Key.OemComma, Ctrl)));
        Bind("편집", "선택 내려놓기", KeyScope.Canvas, () =>
        {
            bool transforming = _ctx.IsTransforming;
            _ctx.CommitFloating();
            if (transforming) ShowMessage("변형 적용");
            return true;
        }, (Key.Enter, NoMods));
        Bind("편집", "선택 해제(캔버스)", KeyScope.Canvas, () =>
        {
            if (_ctx.IsTransforming)
            {
                _ctx.CancelTransform();
                ShowMessage("변형 취소");
            }
            else Deselect();
            return true;
        }, (Key.Escape, NoMods));
        foreach (var (k, dx, dy, dir) in new[] { (Key.Left, -1, 0, "왼쪽으로"), (Key.Right, 1, 0, "오른쪽으로"), (Key.Up, 0, -1, "위로"), (Key.Down, 0, 1, "아래로") })
        {
            Bind("편집", $"1px {dir} 옮기기", KeyScope.Canvas, () => _canvas.Nudge(dx, dy), (k, NoMods), (k, Ctrl | Shift));
            Bind("편집", $"8px {dir} 옮기기", KeyScope.Canvas, () => _canvas.Nudge(dx * 8, dy * 8), (k, Shift));
        }

        // 보기
        var view = TopMenu("보기(_V)");
        _miGrid = Checkable(Cmd("픽셀 격자", () => SetPixelGrid(!_canvas.ShowPixelGrid), "Ctrl+G", KeyScope.Global, (Key.G, Ctrl)));
        _miTiles = Checkable(Cmd("타일 격자", () => SetTileGrid(!_canvas.ShowTileGrid), "Ctrl+Shift+G", KeyScope.Global, (Key.G, Ctrl | Shift)));
        _miTiled = Checkable(Cmd("타일 모드(반복해 보기)", () => SetTiledMode(!_canvas.TiledMode), "Ctrl+Shift+T", KeyScope.Global, (Key.T, Ctrl | Shift)));
        _miPivot = Checkable(Cmd("피벗 표시", () => SetShowPivot(!_canvas.ShowPivot)));
        _miOnion = Checkable(DocCmd("양파 껍질", () => _ctx.OnionSkin = !_ctx.OnionSkin, "Ctrl+J", KeyScope.Global, (Key.J, Ctrl)));
        _miFiles = Checkable(Cmd("파일 브라우저", () => SetFilesVisible(!_filesShown, animate: true), "F8", KeyScope.Global, (Key.F8, NoMods)));
        AddItems(view,
            Cmd("확대", () => _canvas.ZoomStep(1), "Ctrl+=", KeyScope.NotInText, (Key.OemPlus, Ctrl), (Key.Add, Ctrl)),
            Cmd("축소", () => _canvas.ZoomStep(-1), "Ctrl+-", KeyScope.NotInText, (Key.OemMinus, Ctrl), (Key.Subtract, Ctrl)),
            Cmd("화면에 맞추기", _canvas.FitToWindow, "Ctrl+0", KeyScope.Global, (Key.D0, Ctrl), (Key.NumPad0, Ctrl)),
            Cmd("실제 크기(1배)", () => _canvas.SetZoom(1), "Ctrl+1", KeyScope.Global, (Key.D1, Ctrl), (Key.NumPad1, Ctrl)),
            new Separator(),
            _miGrid, _miTiles, _miTiled, _miPivot, _miOnion,
            Cmd("격자 설정…", GridSettings),
            new Separator(),
            _miFiles);
        // 누르고 있는 동안 이전 프레임 보기(키를 떼면 캔버스가 끈다 — PixelCanvas.PeekKey)
        Bind("보기", "이전 프레임 보기(누르고 있기)", KeyScope.NotInText, () => { if (_ctx.HasProject) _canvas.PeekPrevious = true; return true; }, (Key.H, NoMods));

        // 프레임·레이어
        var frame = TopMenu("프레임·레이어(_R)");
        AddItems(frame,
            DocCmd("재생 / 정지", _preview.TogglePlay, "Shift+Enter", KeyScope.Global, (Key.Enter, Shift)),
            DocCmd("이전 프레임", () => _timeline.StepFrame(-1), ",", KeyScope.NotInText, (Key.OemComma, NoMods)),
            DocCmd("다음 프레임", () => _timeline.StepFrame(1), ".", KeyScope.NotInText, (Key.OemPeriod, NoMods)),
            DocCmd("첫 프레임", () => _ctx.SetCurrentFrame(0), "Home", KeyScope.Canvas, (Key.Home, NoMods)),
            DocCmd("마지막 프레임", () => _ctx.SetCurrentFrame(_ctx.FrameCount - 1), "End", KeyScope.Canvas, (Key.End, NoMods)),
            new Separator(),
            DocCmd("새 빈 프레임", _timeline.NewEmptyFrame, "Alt+N", KeyScope.Global, (Key.N, Alt)),
            DocCmd("프레임 복제", _timeline.DuplicateFrame, "Alt+D", KeyScope.Global, (Key.D, Alt)),
            DocCmd("프레임 삭제", _timeline.DeleteFrame),
            new Separator(),
            DocCmd("새 레이어", _timeline.AddLayer, "Shift+N", KeyScope.NotInText, (Key.N, Shift)),
            DocCmd("새 그룹", _timeline.AddGroup, "Ctrl+Shift+N", KeyScope.Global, (Key.N, Ctrl | Shift)),
            DocCmd("새 부위…", _timeline.AddPart, "Shift+P", KeyScope.NotInText, (Key.P, Shift)),
            DocCmd("부위 크기…", _timeline.ResizePart),
            DocCmd("레이어 이름 바꾸기", _timeline.BeginRenameLayer, "F2", KeyScope.NotInText, (Key.F2, NoMods)),
            DocCmd("이전 스킨(현재 그룹)", () => StepSkin(-1), "Alt+[", KeyScope.NotInText, (Key.OemOpenBrackets, Alt)),
            DocCmd("다음 스킨(현재 그룹)", () => StepSkin(1), "Alt+]", KeyScope.NotInText, (Key.OemCloseBrackets, Alt)),
            DocCmd("스킨 관리…", () => _timeline.OpenSkinManager(null)),
            new Separator(),
            DocCmd("새 태그(애니메이션)…", _timeline.NewTag),
            new Separator(),
            DocCmd("격자로 나눠 프레임 만들기(시트 → 프레임)…", SplitIntoFrames));

        // 도구
        var tools = TopMenu("도구(_T)");
        foreach (var (tool, name, key, _) in ToolDefs)
        {
            var t = tool;
            // 이미 고른 도구를 다시 눌러도 메뉴 체크가 풀린 채로 남지 않게 UI를 다시 맞춘다.
            (Key, KeyModifiers)[] keys = key.Length > 0 ? [(Enum.Parse<Key>(key), NoMods)] : [];
            var mi = Checkable(Cmd(name, () => { _ctx.Tool = t; UpdateToolUi(); }, null, KeyScope.NotInText, keys));
            mi.Icon = IconImage(Icons.For(tool), 16);
            _toolMenuItems[tool] = mi;
            tools.Items.Add(mi);
        }
        // M = 선택 칸: 칸의 지금 선택 도구, 이미 선택 도구면 다음 선택 도구.
        Bind("도구", SelectCycleName, KeyScope.NotInText, () => { CycleSelectionTool(); return true; }, (Key.M, NoMods));
        _miFilled = Checkable(Cmd("도형 채움", () => _ctx.ShapeFilled = !_ctx.ShapeFilled, "U", KeyScope.NotInText, (Key.U, NoMods)));
        _miGlobalFill = Checkable(Cmd("채우기: 같은 색 전체", () => _ctx.FillContiguous = !_ctx.FillContiguous, "Shift+G", KeyScope.NotInText, (Key.G, Shift)));
        _miPixelPerfect = Checkable(Cmd("픽셀 퍼펙트", () => _ctx.PixelPerfect = !_ctx.PixelPerfect, "P", KeyScope.NotInText, (Key.P, NoMods)));
        _miMirrorX = Checkable(Cmd("좌우 대칭 그리기", () => _ctx.MirrorX = !_ctx.MirrorX, "Shift+X", KeyScope.NotInText, (Key.X, Shift)));
        _miMirrorY = Checkable(Cmd("상하 대칭 그리기", () => _ctx.MirrorY = !_ctx.MirrorY, "Shift+Y", KeyScope.NotInText, (Key.Y, Shift)));
        _miRightErase = Checkable(Cmd("오른쪽 버튼 = 지우개", () => _ctx.RightButtonErases = !_ctx.RightButtonErases));
        _miWrap = Checkable(Cmd("이동: 순환", () => _ctx.MoveWrap = !_ctx.MoveWrap));
        AddItems(tools,
            new Separator(),
            _miFilled, _miGlobalFill, _miPixelPerfect, _miMirrorX, _miMirrorY, _miRightErase, _miWrap,
            new Separator(),
            Cmd("브러시 크게", () => _ctx.BrushSize++, "]", KeyScope.NotInText, (Key.OemCloseBrackets, NoMods)),
            Cmd("브러시 작게", () => _ctx.BrushSize--, "[", KeyScope.NotInText, (Key.OemOpenBrackets, NoMods)),
            Cmd("기본색 ↔ 보조색", _ctx.SwapColors, "X", KeyScope.NotInText, (Key.X, NoMods)));

        // 테스트
        var test = TopMenu("테스트(_S)");
        _miPlayTest = Cmd("움직임 테스트", ShowPlayTest, "F5", KeyScope.Global, (Key.F5, NoMods));
        test.Items.Add(_miPlayTest);

        // AI
        var ai = TopMenu("AI(_A)");
        AddItems(ai, Cmd("AI 채팅", ToggleChat, "Ctrl+L", KeyScope.Global, (Key.L, Ctrl)));

        // 도움말
        var help = TopMenu("도움말(_H)");
        AddItems(help,
            Cmd("단축키 목록", ShowShortcuts, "F1", KeyScope.Global, (Key.F1, NoMods)),
            Cmd("설정 파일 위치 열기", OpenSettingsFolder),
            new Separator(),
            Cmd("업데이트 확인…", () => _ = _updater.CheckAsync(manual: true)),
            Cmd("Gpixel 정보…", () => new AboutDialog().ShowDialog(this)));

        foreach (var top in new[] { file, edit, view, frame, tools, test, ai, help }) menu.Items.Add(top);
        return menu;
    }

    /// <summary>위 메뉴. 이어서 만드는 명령의 단축키 분류(예: "편집")가 된다.</summary>
    private MenuItem TopMenu(string text)
    {
        _cmdCategory = text[..(text.IndexOf('(') is var i and > 0 ? i : text.Length)];
        return new MenuItem { Header = text };
    }

    private static void AddItems(MenuItem parent, params Control[] items)
    {
        foreach (var it in items) parent.Items.Add(it);
    }

    /// <summary>메뉴 글자 + 오른쪽 단축키 표기(OS 표기로 바꿈). 밑줄(_)이 접근 키로 바뀌지 않게 TextBlock으로 그린다.</summary>
    private static Grid MenuHeader(string text, string? keyText)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        if (keyText is not null)
        {
            var k = new TextBlock
            {
                Text = Ui.Shortcut(keyText),
                Foreground = AppTheme.DimTextBrush,
                Margin = new Thickness(24, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(k, 1);
            g.Children.Add(k);
        }
        return g;
    }

    private static void SetMenuText(MenuItem mi, string text)
    {
        if (mi.Header is Grid g && g.Children.Count > 0 && g.Children[0] is TextBlock t) t.Text = text;
        else mi.Header = text;
    }

    private static MenuItem Checkable(MenuItem mi)
    {
        mi.ToggleType = MenuItemToggleType.CheckBox;
        return mi;
    }

    private static MenuItem EmptyItem() => new() { Header = MenuHeader("(비어 있음)", null), IsEnabled = false };

    /// <summary>
    /// 메뉴 명령 + 단축키 등록(<see cref="Register"/>). 단축키 표기는 지금 단축키(사용자 설정)로 메뉴에 보이고,
    /// <paramref name="keyText"/>는 등록된 키가 없을 때만 보인다(OS가 처리하는 끝내기 등).
    /// </summary>
    private MenuItem Cmd(string text, Action action, string? keyText = null, KeyScope scope = KeyScope.Global, params (Key, KeyModifiers)[] keys)
    {
        var mi = new MenuItem { Header = MenuHeader(text, keyText) };
        // 메뉴가 닫힌 뒤 실행한다(대화상자를 띄워도 메뉴가 남지 않게).
        mi.Click += (_, _) => Dispatcher.UIThread.Post(action);
        Register(_cmdCategory, text, scope, () => { action(); return true; }, keys, mi, keys.Length == 0 ? keyText : null);
        return mi;
    }

    /// <summary>프로젝트가 있어야 쓸 수 있는 명령(없으면 비활성).</summary>
    private MenuItem DocCmd(string text, Action action, string? keyText = null, KeyScope scope = KeyScope.Global, params (Key, KeyModifiers)[] keys)
    {
        var mi = Cmd(text, () => { if (_ctx.HasProject) action(); }, keyText, scope, keys);
        _docItems.Add(mi);
        return mi;
    }

    /// <summary>메뉴 없는 단축키 명령. <paramref name="action"/>이 false면 키를 처리하지 않은 것으로 본다.</summary>
    private void Bind(string category, string name, KeyScope scope, Func<bool> action, params (Key, KeyModifiers)[] keys)
        => Register(category, name, scope, action, keys, null, null);

    private void Register(string category, string name, KeyScope scope, Func<bool> action, (Key, KeyModifiers)[] keys, MenuItem? item, string? fixedKeyText)
    {
        var id = $"{category}/{name}";
        if (_commands.Any(c => c.Info.Id == id)) throw new InvalidOperationException($"단축키 명령 이름이 겹칩니다: {id}");
        var defaults = keys.Select(k => new KeyChord(k.Item1, k.Item2)).ToArray();
        _commands.Add(new CommandEntry(new ShortcutCommand(id, category, name, defaults), scope, action, item, fixedKeyText) { Keys = defaults.ToList() });
    }

    /// <summary>단축키를 바꿀 수 있는 명령(등록 순서 = 메뉴 순서).</summary>
    private sealed class CommandEntry(ShortcutCommand info, KeyScope scope, Func<bool> action, MenuItem? item, string? fixedKeyText)
    {
        public ShortcutCommand Info { get; } = info;
        public KeyScope Scope { get; } = scope;
        public Func<bool> Action { get; } = action;
        public MenuItem? Item { get; } = item;
        public string? FixedKeyText { get; } = fixedKeyText;
        public List<KeyChord> Keys { get; set; } = new();
    }

    /// <summary>
    /// 설정의 사용자 단축키(<see cref="AppSettings.Shortcuts"/>, 기본값과 다른 것만)를 적용한다: 키 표를 다시 만들고
    /// 메뉴·도구 막대·이전 프레임 보기 키를 맞춘다. 같은 키가 여럿이면 나중 명령이 이긴다(환경 설정 창은 겹치지 않게 함).
    /// </summary>
    private void ApplyShortcuts()
    {
        _keys.Clear();
        foreach (var c in _commands)
        {
            c.Keys = _settings.Shortcuts is { } map && map.TryGetValue(c.Info.Id, out var custom)
                ? custom.Select(Shortcuts.Parse).OfType<KeyChord>().Distinct().ToList()
                : c.Info.Defaults.ToList();
            foreach (var k in c.Keys) _keys[(k.Key, k.Mods)] = (c.Scope, c.Action);
            if (c.Item is { } mi)
                mi.Header = MenuHeader(MenuText(mi), c.Keys.Count > 0 ? Shortcuts.Format(c.Keys[0]) : c.FixedKeyText);
        }
        _canvas.PeekKey = Command("보기/이전 프레임 보기(누르고 있기)").Keys.Select(k => (Key?)k.Key).FirstOrDefault();
        foreach (var slot in _toolSlots) slot.RefreshTip();
        if (_tbFiles is not null) _tbFiles.Tip(KeyHint("파일 브라우저 보이기/숨기기", "보기/파일 브라우저"));
        UpdateToolUi();
    }

    private CommandEntry Command(string id) => _commands.First(c => c.Info.Id == id);

    /// <summary>메뉴 항목의 글자(단축키 표기 제외).</summary>
    private static string MenuText(MenuItem mi) => mi.Header is Grid g && g.Children.Count > 0 && g.Children[0] is TextBlock t ? t.Text ?? "" : mi.Header?.ToString() ?? "";

    /// <summary>명령의 첫 단축키 표기(없으면 빈 문자열).</summary>
    private string KeyText(string id) => _commands.FirstOrDefault(c => c.Info.Id == id)?.Keys is { Count: > 0 } k ? Shortcuts.Format(k[0]) : "";

    /// <summary>"설명 (단축키)" — 단축키가 없으면 설명만.</summary>
    private string KeyHint(string text, string id) => KeyText(id) is { Length: > 0 } k ? $"{text} ({k})" : text;

    private const string SelectCycleName = "선택 칸(다음 선택 도구)";

    /// <summary>도구 단축키 표기. 자기 키가 없는 선택 도구(사각·원)는 선택 칸 키(M).</summary>
    private string ToolKeyText(ToolKind tool)
    {
        var own = KeyText($"도구/{ToolName(tool)}");
        if (own.Length > 0 || !EditorContext.IsSelectionTool(tool)) return own;
        return KeyText($"도구/{SelectCycleName}");
    }

    private void FillRecent(MenuItem parent, List<string> list, Action<string> open)
    {
        parent.Items.Clear();
        foreach (var p in list)
        {
            var path = p;
            var mi = new MenuItem { Header = MenuHeader(p, null) };
            mi.Click += (_, _) => Dispatcher.UIThread.Post(() => open(path));
            parent.Items.Add(mi);
        }
        if (list.Count == 0) parent.Items.Add(EmptyItem());
        else
        {
            parent.Items.Add(new Separator());
            var clear = new MenuItem { Header = MenuHeader("목록 지우기", null) };
            clear.Click += (_, _) => list.Clear();
            parent.Items.Add(clear);
        }
    }

    // ───────────── 이벤트 연결 ─────────────

    private void WireEvents()
    {
        _ctx.ProjectReplaced += (_, _) =>
        {
            _docStamp = _ctx.Project?.Path is { } p ? SafeStamp(p) : default;
            _play?.DocumentChanged();
            UpdateAll();
        };
        _ctx.DirtyChanged += (_, _) => { UpdateTitle(); UpdateStatus(); };
        _ctx.ProjectChanged += (_, e) =>
        {
            if ((e.Change & (ProjectChange.Canvas | ProjectChange.Path | ProjectChange.Frames | ProjectChange.Layers)) != 0) { UpdateTitle(); UpdateStatus(); }
            // 움직임 테스트 창은 편집이 잠시 멈추면 다시 읽는다(스트로크 중에는 미룸).
            if (_play is not null && !_ctx.Project!.IsStrokeActive)
            {
                _playRefresh.Stop();
                _playRefresh.Start();
            }
        };
        _playRefresh.Tick += (_, _) => { _playRefresh.Stop(); _play?.DocumentChanged(); };
        _ctx.CurrentChanged += (_, _) => { UpdateStatus(); UpdateToolUi(); };
        _ctx.UndoChanged += (_, _) => UpdateUndoItems();
        _ctx.ToolChanged += (_, _) => UpdateToolUi();
        _ctx.ToolOptionsChanged += (_, _) => UpdateToolUi();
        _ctx.OnionSkinChanged += (_, _) => UpdateToolUi();

        _canvas.ZoomChanged += (_, _) => { _settings.Zoom = _canvas.Zoom; UpdateStatus(); };
        _canvas.HoverChanged += (_, p) => UpdateHover(p);
        _canvas.Message += (_, m) => ShowMessage(m);
        _timeline.Message += (_, m) => ShowMessage(m);

        _browser.SpriteDefaults = () => new SpriteSettings { PixelsPerUnit = _settings.LastPpu, Mode = SpriteMode.Multiple };
        _browser.TrashPolicy = () => _settings.TrashPolicy;
        _browser.TrashPolicyChanged += policy => _settings.TrashPolicy = policy;
        _browser.Message += (_, m) => ShowMessage(m);
        // 보관 기간이 지났거나 용량을 넘친 휴지통 항목은 시작할 때 영구 삭제한다.
        _browser.Trash.Purge(_settings.TrashPolicy);
        _browser.FileActivated += (_, path) =>
        {
            // 목록 선택 이벤트가 끝난 뒤 연다(저장 확인 대화상자·선택 되돌리기를 안전하게).
            Dispatcher.UIThread.Post(() => { if (!OpenFile(path)) _browser.SelectFile(_ctx.Project?.Path); });
        };
        _browser.NewImageRequested += (_, folder) => NewProject(folder);
        _browser.FileRenamed += (_, r) => OnFileRenamed(r.OldPath, r.NewPath);
        _browser.FileDeleted += (_, path) => OnFileDeleted(path);
        _browser.FilesChanged += (_, paths) => OnFilesChanged(paths);

        _start.FolderChosen += (_, folder) => SetRootFolder(folder);
        _start.FileChosen += (_, file) => OpenFile(file);
        _start.OpenFolderRequested += (_, _) => OpenFolder();
        _start.NewImageRequested += (_, _) => NewProject(null);

        _colors.PalettesChanged += (_, _) => _settings.Palettes = _colors.Palettes.Select(PaletteSetting.From).ToList();
        _colors.Message += (_, m) => ShowMessage(m);

        // 모든 색 고르기 창: 팔레트·최근 색·스포이드(현재 프레임 합성).
        ColorPickDialog.Sources = () => new ColorPickSources(_colors.Palettes, _ctx.RecentColors.ToList(),
            () => _ctx.Project is { } p ? p.Flatten(_ctx.CurrentFrame) : null);

        // 숫자 상자를 끄는 동안의 값 변화(프로젝트 설정 등)는 실행취소 한 단계로 묶는다. Claude 차례처럼 이미 열린 묶음이 있으면 그 안에 둔다.
        SpriteProject? scrubProject = null;
        NumberScrub.Started += (_, _) =>
        {
            if (_ctx.Project is not { } p || p.History.InGroup) return;
            p.History.BeginGroup(null);
            scrubProject = p;
        };
        NumberScrub.Ended += (_, _) =>
        {
            if (scrubProject is not { } p) return;
            scrubProject = null;
            p.History.EndGroup();
        };
    }

    // ───────────── 설정 ─────────────

    private void ApplySettingsBeforeShow()
    {
        var s = _settings;
        _ctx.UndoCapacity = s.UndoSteps;
        ApplyShortcuts();
        double winW = Math.Max(MinWidth, s.WindowWidth), winH = Math.Max(MinHeight, s.WindowHeight);
        Width = winW;
        Height = winH;
        _normalSize = new Avalonia.Size(winW, winH);
        var bounds = new PixelRect(s.WindowX, s.WindowY, (int)winW, (int)winH);
        if (s.WindowX >= 0 && Screens.All.Any(sc => sc.WorkingArea.Intersects(bounds)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(s.WindowX, s.WindowY);
            _normalPosition = Position;
        }
        if (s.Maximized) WindowState = WindowState.Maximized;

        _canvas.InitZoom(s.Zoom);
        _canvas.ShowPixelGrid = s.ShowPixelGrid;
        _canvas.ShowPivot = s.ShowPivot;
        _canvas.ShowTileGrid = s.ShowTileGrid;
        _canvas.TiledMode = s.TiledMode;
        _canvas.TileSize = new Size(s.TileWidth, s.TileHeight);
        _canvas.PixelGridColor = ColorParse.ParseOr(s.PixelGridColor, new Color32(255, 255, 255, 48)).ToColor();
        _canvas.TileGridColor = ColorParse.ParseOr(s.TileGridColor, new Color32(90, 200, 255, 150)).ToColor();
        _ctx.OnionSkin = s.OnionSkin;
        _ctx.BrushSize = s.BrushSize;
        _ctx.BrushShape = s.BrushShape;
        _ctx.FillTolerance = s.FillTolerance;
        _ctx.FillDiagonal = s.FillDiagonal;
        _ctx.FillSampleVisible = s.FillSampleVisible;
        _ctx.PickerSampleVisible = s.PickerSampleVisible;
        _ctx.DodgeStrength = s.DodgeStrength;
        _ctx.DitherPattern = s.DitherPattern;
        _ctx.PixelPerfect = s.PixelPerfect;
        _ctx.FillContiguous = s.FillContiguous;
        _ctx.ShapeFilled = s.ShapeFilled;
        _ctx.RightButtonErases = s.RightButtonErases;
        _ctx.MoveWrap = s.MoveWrap;
        _ctx.TransformResample = s.TransformResample;
        if (ParseGradientStops(s.GradientStops) is { } stops) _ctx.GradientStops = stops;
        if (ParseGradientAlphaStops(s.GradientAlphaStops) is { } alphaStops) _ctx.GradientAlphaStops = alphaStops;
        _ctx.GradientInterpolation = s.GradientInterpolation;
        _ctx.GradientDither = s.GradientDither;
        _ctx.Primary = ColorParse.ParseOr(s.PrimaryColor, Color32.Black);
        _ctx.Secondary = ColorParse.ParseOr(s.SecondaryColor, Color32.Transparent);
        foreach (var hex in s.RecentColors.AsEnumerable().Reverse())
            if (Color32.TryParseHex(hex, out var c)) _ctx.PushRecentColor(c);
        _colors.SetRecent(_ctx.RecentColors);
        var palettes = s.Palettes ?? [new PaletteSetting { Name = AppSettings.DefaultPaletteName, Colors = [.. AppSettings.DefaultPalette] }];
        _colors.SetPalettes(palettes.Select(p => p.ToPalette()));
        _colors.PickerMode = s.ColorPickerMode;
        _browser.IncludeSubfolders = s.IncludeSubfolders;
        _canvas.AutoFit = s.AutoFit;
        _preview.ZoomSetting = s.PreviewZoom;
        _preview.FpsSetting = s.PreviewFps;
        _preview.BackgroundSetting = s.PreviewBackground;
        _start.SetRecent(s.RecentFolders, s.RecentFiles);
    }

    /// <summary>일반(최대화·최소화 아닌) 상태의 창 위치·크기를 기억한다(WinForms RestoreBounds 대신).</summary>
    private void RememberNormalBounds()
    {
        if (WindowState != WindowState.Normal || !IsVisible) return;
        _normalPosition = Position;
        _normalSize = ClientSize;
    }

    private void OnShown()
    {
        var s = _settings;
        SetSplit(_colFiles, _splitFiles, s.LeftWidth);
        SetSplit(_colPalette, _splitPalette, s.PaletteWidth);
        SetSplit(_colRight, _splitRight, s.RightWidth);
        SetSplit(_rowBottom, _splitTimeline, s.BottomHeight);
        SetSplit(_rowPreview, _splitRightDock, s.PreviewHeight);
        _browser.TreeHeight = s.BrowserTreeHeight;
        SetFilesVisible(s.FilesVisible);
        _settingsApplied = true;
        RememberNormalBounds();
        _start.LoadHub();
        var folder = _args.OpenFolder ?? s.LastFolder;
        if (folder is not null && Directory.Exists(folder)) SetRootFolder(folder);
        else if (_args.OpenFolder is not null) Ui.Error(this, $"폴더를 찾을 수 없습니다:\n{_args.OpenFolder}");
        if (_args.OpenFile is not null) OpenFile(_args.OpenFile);
        if (s.ChatOpen) Dispatcher.UIThread.Post(ToggleChat);
        _canvas.Focus();
        _updater.OnStartup();
    }

    /// <summary>설정의 그라데이션 정지점("위치:#색")을 읽는다. 없거나 2개보다 적게 읽히면 null(기본값 유지).</summary>
    private static List<GradientStop>? ParseGradientStops(List<string>? texts)
    {
        if (texts is null) return null;
        var stops = new List<GradientStop>();
        foreach (var text in texts)
        {
            int colon = text.IndexOf(':');
            if (colon <= 0) continue;
            if (double.TryParse(text[..colon], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pos)
                && Color32.TryParseHex(text[(colon + 1)..].Trim(), out var color))
                stops.Add(new GradientStop(Math.Clamp(pos, 0, 1), color));
        }
        return stops.Count >= 2 ? stops : null;
    }

    /// <summary>설정의 불투명도 정지점("위치:0~255")을 읽는다. 없거나 2개보다 적게 읽히면 null(기본값 유지).</summary>
    private static List<GradientAlphaStop>? ParseGradientAlphaStops(List<string>? texts)
    {
        if (texts is null) return null;
        var stops = new List<GradientAlphaStop>();
        foreach (var text in texts)
        {
            int colon = text.IndexOf(':');
            if (colon <= 0) continue;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (double.TryParse(text[..colon], System.Globalization.NumberStyles.Float, inv, out var pos)
                && int.TryParse(text[(colon + 1)..].Trim(), System.Globalization.NumberStyles.Integer, inv, out var alpha))
                stops.Add(new GradientAlphaStop(Math.Clamp(pos, 0, 1), (byte)Math.Clamp(alpha, 0, 255)));
        }
        return stops.Count >= 2 ? stops : null;
    }

    private void SaveSettings()
    {
        var s = _settings;
        s.Maximized = WindowState == WindowState.Maximized;
        RememberNormalBounds();
        (s.WindowX, s.WindowY, s.WindowWidth, s.WindowHeight) =
            (_normalPosition.X, _normalPosition.Y, (int)Math.Round(_normalSize.Width), (int)Math.Round(_normalSize.Height));
        if (_settingsApplied)
        {
            s.FilesVisible = _filesShown;
            if (s.FilesVisible && _filesAnim is null) s.LeftWidth = (int)Math.Round(_colFiles.ActualWidth);
            s.PaletteWidth = (int)Math.Round(_colPalette.ActualWidth);
            s.RightWidth = (int)Math.Round(_colRight.ActualWidth);
            s.BottomHeight = (int)Math.Round(_rowBottom.ActualHeight);
            s.PreviewHeight = (int)Math.Round(_rowPreview.ActualHeight);
            s.BrowserTreeHeight = _browser.TreeHeight;
        }
        s.Zoom = _canvas.Zoom;
        s.ShowPixelGrid = _canvas.ShowPixelGrid;
        s.ShowPivot = _canvas.ShowPivot;
        s.ShowTileGrid = _canvas.ShowTileGrid;
        s.TiledMode = _canvas.TiledMode;
        s.TileWidth = _canvas.TileSize.Width;
        s.TileHeight = _canvas.TileSize.Height;
        s.PixelGridColor = _canvas.PixelGridColor.ToColor32().ToHex();
        s.TileGridColor = _canvas.TileGridColor.ToColor32().ToHex();
        s.OnionSkin = _ctx.OnionSkin;
        s.BrushSize = _ctx.BrushSize;
        s.BrushShape = _ctx.BrushShape;
        s.FillTolerance = _ctx.FillTolerance;
        s.FillDiagonal = _ctx.FillDiagonal;
        s.FillSampleVisible = _ctx.FillSampleVisible;
        s.PickerSampleVisible = _ctx.PickerSampleVisible;
        s.DodgeStrength = _ctx.DodgeStrength;
        s.DitherPattern = _ctx.DitherPattern;
        s.PixelPerfect = _ctx.PixelPerfect;
        s.FillContiguous = _ctx.FillContiguous;
        s.ShapeFilled = _ctx.ShapeFilled;
        s.RightButtonErases = _ctx.RightButtonErases;
        s.MoveWrap = _ctx.MoveWrap;
        s.TransformResample = _ctx.TransformResample;
        s.GradientStops = _ctx.GradientStops.Select(g => FormattableString.Invariant($"{g.Position:0.####}:{g.Color.ToHex()}")).ToList();
        s.GradientAlphaStops = _ctx.GradientAlphaStops.Select(g => FormattableString.Invariant($"{g.Position:0.####}:{g.Alpha}")).ToList();
        s.GradientInterpolation = _ctx.GradientInterpolation;
        s.GradientDither = _ctx.GradientDither;
        s.PrimaryColor = _ctx.Primary.ToHex();
        s.SecondaryColor = _ctx.Secondary.ToHex();
        s.RecentColors = _ctx.RecentColors.Select(c => c.ToHex()).ToList();
        s.Palettes = _colors.Palettes.Select(PaletteSetting.From).ToList();
        s.ColorPickerMode = _colors.PickerMode;
        s.IncludeSubfolders = _browser.IncludeSubfolders;
        s.LastFolder = _browser.RootFolder ?? s.LastFolder;
        s.AutoFit = _canvas.AutoFit;
        s.PreviewZoom = _preview.ZoomSetting;
        s.PreviewFps = _preview.FpsSetting;
        s.PreviewBackground = _preview.BackgroundSetting;
        s.Save();
    }

    /// <summary>
    /// 창 닫기: 저장 확인 대화상자는 Closing 처리 중에 띄울 수 없으므로, 수정된 프로젝트가 있으면 닫기를 일단 취소하고
    /// 처리가 끝난 뒤 물어서 계속해도 되면 다시 닫는다.
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed) return;
        _ctx.CommitFloating();
        if (_ctx.Project is not { IsDirty: true })
        {
            _play?.Close();
            ShutdownChat();
            SaveSettings();
            return;
        }
        e.Cancel = true;
        if (_closePending) return;
        _closePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _closePending = false;
            if (!ConfirmDiscard()) return;
            _closeConfirmed = true;
            _play?.Close();
            ShutdownChat();
            SaveSettings();
            Close();
        });
    }

    /// <summary>위 막대 "업데이트" 단추: 알릴 새 버전이 있을 때만 보인다.</summary>
    private void ShowUpdateButton(ReleaseInfo? release)
    {
        _cbUpdate.IsVisible = release is not null;
        if (release is not null) _cbUpdate.Content = $"업데이트 {release.Version}";
    }

    /// <summary>업데이트 설치 스크립트를 띄운 뒤 앱을 끝낸다(저장 여부는 <see cref="ConfirmDiscard"/>로 이미 물었다).</summary>
    private void ExitForUpdate()
    {
        _closeConfirmed = true;
        _play?.Close();
        ShutdownChat();
        SaveSettings();
        Close();
    }

    private void OpenSettingsFolder()
    {
        var dir = Path.GetDirectoryName(AppSettings.FilePath)!;
        SaveSettings();
        try { Platform.OpenFolder(dir); }
        catch (IOException ex)
        {
            Ui.Error(this, ex.Message);
        }
    }

    // ───────────── 키 처리 ─────────────

    /// <summary>창 전체 키 미리 받기(터널): 포커스된 컨트롤보다 먼저 단축키 표를 본다.</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl(⌘)을 누르는 동안의 임시 이동 도구는 단축키(⌘T·⌘V 등)를 누르는 순간 먼저 원래 도구로 되돌린다 —
        // 단축키가 시작한 변형·붙여넣기가 ⌘를 뗄 때의 도구 되돌리기에 적용(취소)되지 않게.
        _canvas.EndTempMoveForShortcut(e.Key);
        if (e.Handled) return;
        var mods = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta);
        if (_keys.TryGetValue((e.Key, mods), out var entry) && CanRun(entry.Scope) && entry.Action())
            e.Handled = true;
    }

    private bool CanRun(KeyScope scope)
    {
        if (scope == KeyScope.Global) return true;
        var focus = FocusManager?.GetFocusedElement();
        if (scope == KeyScope.Canvas) return ReferenceEquals(focus, _canvas);
        // 글자 입력란(숫자 상자·편집 가능한 콤보 상자 안쪽 입력란 포함)이면 그 입력란이 키를 받는다.
        return focus is not TextBox;
    }

    // ───────────── 화면 갱신 ─────────────

    private void UpdateAll()
    {
        bool has = _ctx.HasProject;
        _start.IsVisible = !has;
        if (!has) _start.SetRecent(_settings.RecentFolders, _settings.RecentFiles);
        _canvas.IsVisible = has;
        foreach (var item in _docItems) item.IsEnabled = has;
        _miPlayTest.IsEnabled = has;
        UpdateTitle();
        UpdateStatus();
        UpdateUndoItems();
        UpdateToolUi();
    }

    private void UpdateTitle()
    {
        var p = _ctx.Project;
        Title = p is null
            ? "Gpixel"
            : $"{p.DisplayName}{(p.IsDirty ? " ●" : "")} — Gpixel" + (p.Path is { } path ? $"  ({Path.GetDirectoryName(path)})" : "");
    }

    private void UpdateStatus()
    {
        var p = _ctx.Project;
        _stSize.Text = p is null ? "" : $"{_ctx.CanvasWidth} × {_ctx.CanvasHeight}";
        _stFrame.Text = p is null ? "" : StatusFrameText(p);
        _stZoom.Text = $"{_canvas.Zoom * 100}%";
        _cbZoom.Text = $"{_canvas.Zoom * 100}%";
        if (p is null) _stDirty.Text = "";
        else
        {
            _stDirty.Text = p.IsDirty ? "● 수정됨" : "저장됨";
            _stDirty.Foreground = p.IsDirty ? AppTheme.WarningBrush : AppTheme.TextBrush;
        }
    }

    /// <summary>상태 표시줄의 "프레임 i/n · L k 이름" (부위면 크기도).</summary>
    private string StatusFrameText(SpriteProject p)
    {
        var text = $"프레임 {_ctx.CurrentFrame + 1}/{p.Frames.Count} · L{_ctx.CurrentLayer + 1}";
        if (_ctx.CurrentLayerObject is { } layer)
        {
            text += $" {layer.Name}";
            if (layer.IsPart) text += $" ({layer.PartSize.Width}×{layer.PartSize.Height})";
            else if (layer.IsGroup) text += " (그룹)";
        }
        return text;
    }

    private void UpdateHover(Point? pt)
    {
        if (!_ctx.HasProject || pt is not { } p)
        {
            _stPos.Text = "";
            _stColor.Text = "";
            return;
        }
        _stPos.Text = $"{p.X}, {p.Y}";
        var c = _ctx.CompositeColorAt(p.X, p.Y);
        _stColor.Text = c.A == 0 ? "투명" : c.ToHex();
    }

    private void UpdateUndoItems()
    {
        var h = _ctx.Project?.History;
        _miUndo.IsEnabled = h?.CanUndo == true;
        _miRedo.IsEnabled = h?.CanRedo == true;
        SetMenuText(_miUndo, h?.UndoLabel is { } u ? $"실행취소: {u}" : "실행취소");
        SetMenuText(_miRedo, h?.RedoLabel is { } r ? $"다시 실행: {r}" : "다시 실행");
    }

    /// <summary>M을 이만큼 안에 다시 눌러야 선택 칸의 다음 도구로 넘어간다.</summary>
    private static readonly TimeSpan SelectCycleWindow = TimeSpan.FromSeconds(10);
    private DateTime _lastSelectCycleKey = DateTime.MinValue;

    /// <summary>
    /// M: 선택 칸의 도구(마지막에 고른 선택 도구)를 고른다. 이미 선택 도구를 쓰는 중이고 직전 M이 10초 안이면 칸 안의 다음 도구로(사각 → 원 → 자유 → 스마트).
    /// 10초가 지났으면 바로 바꾸지 않고, 10초 안에 한 번 더 누르면 바꾼다. 다른 도구를 쓰는 중이면 선택 칸 도구로만 바꾼다.
    /// </summary>
    private void CycleSelectionTool()
    {
        var slot = _toolSlots.First(s => s.Contains(ToolKind.Select));
        var now = DateTime.UtcNow;
        bool recent = now - _lastSelectCycleKey <= SelectCycleWindow;
        _lastSelectCycleKey = now;
        if (!slot.Contains(_ctx.Tool)) _ctx.Tool = slot.Current;
        else if (recent) _ctx.Tool = slot.Next(_ctx.Tool);
        else ShowMessage(Ui.Shortcut($"{ToolDefs.First(d => d.Tool == _ctx.Tool).Name} — 10초 안에 {KeyText($"도구/{SelectCycleName}")}를 한 번 더 누르면 다음 선택 도구로 바뀝니다."));
        UpdateToolUi();
    }

    private void UpdateToolUi()
    {
        var tool = _ctx.Tool;
        // 단축키·메뉴로 고른 묶음 도구도 그 칸에 보이게 바꾼다.
        foreach (var slot in _toolSlots)
        {
            slot.SetCurrent(tool);
            slot.IsChecked = slot.Current == tool;
        }
        foreach (var (t, mi) in _toolMenuItems) mi.IsChecked = t == tool;
        var def = ToolDefs.First(d => d.Tool == tool);
        _cbToolName.Text = Ui.Shortcut(ToolKeyText(tool) is { Length: > 0 } key ? $"{def.Name} ({key})" : def.Name);
        _cbHint.Text = Ui.Shortcut(def.Hint);
        _miPixelPerfect.IsChecked = _ctx.PixelPerfect;
        _miFilled.IsChecked = _ctx.ShapeFilled;
        _miGlobalFill.IsChecked = !_ctx.FillContiguous;
        _miRightErase.IsChecked = _ctx.RightButtonErases;
        _miWrap.IsChecked = _ctx.MoveWrap;
        _miMirrorX.IsChecked = _ctx.MirrorX;
        _miMirrorY.IsChecked = _ctx.MirrorY;
        _cbGrid.IsChecked = _miGrid.IsChecked = _canvas.ShowPixelGrid;
        _cbTiles.IsChecked = _miTiles.IsChecked = _canvas.ShowTileGrid;
        _cbTiled.IsChecked = _miTiled.IsChecked = _canvas.TiledMode;
        _cbPivot.IsChecked = _miPivot.IsChecked = _canvas.ShowPivot;
        _cbOnion.IsChecked = _miOnion.IsChecked = _ctx.OnionSkin;
        _cbFit.IsChecked = _canvas.AutoFit;
        _tbFiles.IsChecked = _miFiles.IsChecked = _filesShown;
    }

    private void ShowMessage(string text) => _stMessage.Text = text;

    private void SetPixelGrid(bool on) { _canvas.ShowPixelGrid = on; UpdateToolUi(); }
    private void SetTileGrid(bool on) { _canvas.ShowTileGrid = on; UpdateToolUi(); }
    private void SetShowPivot(bool on) { _canvas.ShowPivot = on; UpdateToolUi(); }

    private void SetTiledMode(bool on)
    {
        _canvas.TiledMode = on;
        UpdateToolUi();
        ShowMessage(on ? "타일 모드: 가장자리를 넘는 그리기가 반대편으로 이어집니다." : "타일 모드 끔");
    }

    private bool _filesShown = true;
    private DispatcherTimer? _filesAnim;

    /// <summary>
    /// 파일 브라우저 칸 보이기/숨기기(숨기면 칸과 나눔 막대를 접는다). <paramref name="animate"/>면 폭이 부드럽게 늘고 준다(약 0.18초);
    /// 도중에 다시 누르면 지금 폭에서 반대로 간다.
    /// </summary>
    private void SetFilesVisible(bool visible, bool animate = false)
    {
        if (!visible && _filesShown && _browser.IsVisible && _settingsApplied && _filesAnim is null)
            _settings.LeftWidth = (int)Math.Round(_colFiles.ActualWidth);
        _filesShown = visible;
        _filesAnim?.Stop();
        _filesAnim = null;
        double target = visible ? ClampSplit(_settings.LeftWidth, _splitFiles.Bounds.Width) : 0;
        void Finish()
        {
            _browser.IsVisible = visible;
            _filesSplitter.IsVisible = visible;
            _colFiles.MinWidth = visible ? MinPanelSize : 0;
            if (!visible) _colFiles.Width = new GridLength(0);
            else if (_settingsApplied || _colFiles.Width.Value == 0) SetSplit(_colFiles, _splitFiles, _settings.LeftWidth);
            UpdateToolUi();
        }
        if (!animate || _splitFiles.Bounds.Width <= 0)
        {
            Finish();
            return;
        }
        // 움직이는 동안은 칸을 보이게 두고 최소 폭을 풀어 0까지 줄 수 있게 한다.
        _browser.IsVisible = true;
        _browser.ClipToBounds = true;
        _filesSplitter.IsVisible = true;
        _colFiles.MinWidth = 0;
        double from = _colFiles.ActualWidth;
        var start = DateTime.UtcNow;
        const double DurationMs = 180;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / DurationMs);
            double eased = 1 - Math.Pow(1 - t, 3);
            _colFiles.Width = new GridLength(Math.Max(0, from + (target - from) * eased));
            if (t < 1) return;
            timer.Stop();
            if (ReferenceEquals(_filesAnim, timer)) _filesAnim = null;
            Finish();
        };
        _filesAnim = timer;
        timer.Start();
        UpdateToolUi();
    }

    private void GridSettings()
    {
        var dlg = new GridSettingsDialog(_canvas.PixelGridColor.ToColor32(), _canvas.TileSize, _canvas.TileGridColor.ToColor32());
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _canvas.PixelGridColor = dlg.PixelGridColor.ToColor();
        _canvas.TileGridColor = dlg.TileGridColor.ToColor();
        _canvas.TileSize = dlg.TileSize;
        UpdateToolUi();
    }

    /// <summary>환경 설정(실행취소 횟수·앱 휴지통 보관 기간·업데이트 자동 확인·단축키).</summary>
    private void ShowPreferences()
    {
        var dlg = new PreferencesDialog(_settings.UndoSteps, _settings.TrashPolicy, _settings.UpdateCheck,
            _commands.Select(c => c.Info).ToList(), _commands.ToDictionary(c => c.Info.Id, c => c.Keys.ToList()));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _settings.UndoSteps = dlg.UndoSteps;
        _settings.TrashPolicy = dlg.TrashPolicy;
        _settings.UpdateCheck = dlg.UpdateCheck;
        _ctx.UndoCapacity = dlg.UndoSteps;
        _browser.Trash.Purge(dlg.TrashPolicy);
        // 기본값과 다른 명령만 저장한다(나중에 기본 단축키가 바뀌어도 고치지 않은 명령은 따라감).
        var custom = _commands
            .Where(c => dlg.Keys.TryGetValue(c.Info.Id, out var k) && !k.SequenceEqual(c.Info.Defaults))
            .ToDictionary(c => c.Info.Id, c => dlg.Keys[c.Info.Id].Select(Shortcuts.Format).ToList());
        _settings.Shortcuts = custom.Count > 0 ? custom : null;
        ApplyShortcuts();
        _settings.Save();
    }

    private void ShowStart()
    {
        if (!ConfirmDiscard()) return;
        _ctx.SetProject(null);
        _start.LoadHub();
    }

    private void ShowShortcuts()
    {
        var dlg = new ShortcutHelpDialog();
        dlg.ShowDialog(this);
    }

    /// <summary>움직임 테스트: 프로젝트의 태그를 시트 문서로 만들어 넘긴다(편집하면 잠시 뒤 다시 읽음).</summary>
    private void ShowPlayTest()
    {
        if (_play is { IsClosed: false })
        {
            if (_play.WindowState == WindowState.Minimized) _play.WindowState = WindowState.Normal;
            _play.Activate();
            return;
        }
        var play = new PlayTestForm(() => _ctx.Project is { } p ? ProjectExport.BuildDocument(p, p.BaseName, allFrames: true) : null);
        play.Closed += (_, _) => { if (ReferenceEquals(_play, play)) _play = null; };
        _play = play;
        play.Show();
    }

    // ───────────── 파일 ─────────────

    private static DateTime SafeStamp(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return default; }
    }

    /// <summary>수정된 프로젝트가 있으면 저장 여부를 묻는다. 계속해도 되면 true.</summary>
    private bool ConfirmDiscard()
    {
        var p = _ctx.Project;
        if (p is null) return true;
        _ctx.CommitFloating();
        if (!p.IsDirty) return true;
        var answer = Ui.Ask(this, $"\"{p.DisplayName}\"의 변경 내용을 저장할까요?", "Gpixel",
            MessageButtons.YesNoCancel, MessageIcon.Question);
        return answer switch
        {
            DialogResult.Yes => Save(),
            DialogResult.No => true,
            _ => false,
        };
    }

    private void SetRootFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Ui.Error(this, $"폴더를 찾을 수 없습니다:\n{folder}");
            _settings.RecentFolders.Remove(folder);
            _start.SetRecent(_settings.RecentFolders, _settings.RecentFiles);
            return;
        }
        folder = Path.GetFullPath(folder);
        _browser.SetRoot(folder);
        if (!_filesShown) SetFilesVisible(true, animate: true);
        _settings.LastFolder = folder;
        AppSettings.PushRecent(_settings.RecentFolders, folder);
        _start.SetRecent(_settings.RecentFolders, _settings.RecentFiles);
        var unityRoot = UnityProjectLocator.FindProjectRoot(folder);
        ShowMessage(unityRoot is null ? $"폴더: {folder}" : $"Unity 프로젝트 \"{Path.GetFileName(unityRoot)}\" — 저장하면 시트와 .meta를 함께 내보냅니다.");
    }

    private void OpenFolder()
    {
        var path = Ui.PickFolder(this, "편집할 그림이 있는 루트 폴더를 고르세요.", _browser.RootFolder ?? _settings.LastFolder);
        if (path is not null) SetRootFolder(UnityHubDialog.ResolveAssets(path));
    }

    private void OpenUnityProject()
    {
        var dlg = new UnityHubDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedFolder is { } folder) SetRootFolder(folder);
    }

    private void OpenFileDialogCmd()
    {
        var path = Ui.OpenFile(this, "열기", _browser.CurrentFolder ?? _settings.LastFolder,
            new FileFilter("Gpixel 프로젝트·PNG", "*.gpixel", "*.png"),
            new FileFilter("Gpixel 프로젝트", "*.gpixel"),
            new FileFilter("PNG 이미지", "*.png"));
        if (path is not null) OpenFile(path);
    }

    /// <summary>.gpixel을 열거나 PNG를 가져온다(같은 이름 .gpixel이 있으면 그것). 취소·실패하면 false.</summary>
    private bool OpenFile(string path) => OpenFile(path, interactive: true, out _);

    /// <summary>
    /// 파일 열기. <paramref name="interactive"/>가 false면(Claude 도구) 대화상자를 띄우지 않는다:
    /// 저장 안 된 수정이 있으면 실패하고, 칸 크기가 모두 같은 시트는 애니메이션으로 가져오며, 결과·오류는 <paramref name="message"/>로 돌려준다.
    /// </summary>
    private bool OpenFile(string path, bool interactive, out string message)
    {
        message = "";
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            message = $"잘못된 경로입니다: {path}";
            if (interactive) Ui.Error(this, message);
            return false;
        }
        var target = !full.EndsWith(SpriteProject.FileExtension, StringComparison.OrdinalIgnoreCase) && ProjectImport.FindProjectForImage(full) is { } owner
            ? owner
            : full;
        if (_ctx.Project?.Path is { } cur && string.Equals(cur, target, StringComparison.OrdinalIgnoreCase))
        {
            message = $"이미 열려 있습니다: {Path.GetFileName(cur)}";
            return true;
        }
        if (!File.Exists(full))
        {
            message = $"파일을 찾을 수 없습니다:\n{full}";
            if (interactive) Ui.Error(this, message);
            _settings.RecentFiles.Remove(path);
            return false;
        }
        if (interactive ? !ConfirmDiscard() : !CanReplaceSilently(out message)) return false;
        ProjectOpenResult result;
        try
        {
            var analysis = ProjectImport.Analyze(full, new SpriteSettings { PixelsPerUnit = _settings.LastPpu, Mode = SpriteMode.Multiple });
            ImportMode? mode = null;
            if (analysis.NeedsModeChoice)
            {
                if (interactive)
                {
                    var size = analysis.Document!.Slices.Select(s => Rectangle.Intersect(s.Rect, analysis.Document.Image.Bounds).Size)
                        .FirstOrDefault(s => s.Width > 0 && s.Height > 0);
                    mode = ImportKindDialog.Ask(this, Path.GetFileName(full), analysis.SliceCount, size);
                    if (mode is null) return false;
                }
                else mode = ImportMode.Frames;
            }
            result = ProjectImport.Import(analysis, mode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException
                                       or System.Text.Json.JsonException)
        {
            message = $"열지 못했습니다:\n{full}\n\n{ex.Message}";
            if (interactive) Ui.Error(this, message);
            return false;
        }
        _ctx.SetProject(result.Project);
        var p = result.Project;
        AppSettings.PushRecent(_settings.RecentFiles, result.Imported ? full : p.Path ?? full);
        _browser.SelectFile(p.Path is { } pp && File.Exists(pp) ? pp : full);
        int parts = p.Layers.Count(l => l.IsPart);
        var contents = $"프레임 {p.Frames.Count}개 · 태그 {p.Tags.Count}개" + (parts > 0 ? $" · 부위 {parts}개" : "");
        message = result.Imported
            ? $"PNG 가져옴: {Path.GetFileName(full)} → {contents} — 저장하면 {Path.GetFileName(p.Path)}이(가) 만들어집니다."
            : $"열기: {p.DisplayName} (레이어 {p.Layers.Count} · {contents})";
        ShowMessage(message);
        if (result.Warning is { } w)
        {
            if (interactive) Ui.Warn(this, w);
            else message += "\n" + w;
        }
        return true;
    }

    private void NewProject(string? folder)
    {
        var defaults = _ctx.HasProject ? (_ctx.CanvasWidth, _ctx.CanvasHeight) : (32, 32);
        if (folder is null && !ConfirmDiscard()) return;
        var dlg = new NewImageDialog(defaults.Item1, defaults.Item2, folder is null ? null : "new_sprite");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string? path = null;
        if (folder is not null)
        {
            var name = dlg.FileName.EndsWith(SpriteProject.FileExtension, StringComparison.OrdinalIgnoreCase) ? dlg.FileName : dlg.FileName + SpriteProject.FileExtension;
            path = Path.Combine(folder, name);
            if (File.Exists(path))
            {
                Ui.Error(this, $"이미 같은 이름의 파일이 있습니다:\n{path}");
                return;
            }
            if (!ConfirmDiscard()) return;
        }
        var p = BuildProject(dlg.ImageWidth, dlg.ImageHeight, dlg.Transparent ? null : _ctx.Secondary,
            dlg.StartWithParts, dlg.PartName, dlg.PartWidth, dlg.PartHeight);
        _ctx.SetProject(p);
        SelectFirstPart(p);
        if (path is null)
        {
            p.IsDirty = true;
            ShowMessage(Ui.Shortcut("새 프로젝트 — Ctrl+S로 저장할 위치를 고르세요."));
            return;
        }
        if (SaveTo(path)) _browser.RefreshFile(path);
        _browser.SelectFile(path);
    }

    /// <summary>
    /// 새 프로젝트(기록 없음). <paramref name="background"/>가 있으면 첫 레이어 셀을 그 색으로 채운다.
    /// <paramref name="startWithParts"/>면 그룹 + 첫 부위를 만들고 그 부위를 현재 레이어로 고른다(<see cref="SelectFirstPart"/>).
    /// </summary>
    private SpriteProject BuildProject(int width, int height, Color32? background,
        bool startWithParts = false, string? partName = null, int partWidth = 0, int partHeight = 0)
    {
        var p = new SpriteProject(width, height);
        p.Edit("새 프로젝트", ProjectChange.Settings | ProjectChange.Pixels, () =>
        {
            p.Sprite.PixelsPerUnit = _settings.LastPpu;
            if (background is { } c)
            {
                var bg = new PixelImage(p.Width, p.Height);
                bg.Clear(c);
                p.SetCel(0, 0, bg);
            }
        });
        if (startWithParts)
        {
            var group = p.AddGroup();
            p.AddPart(group, string.IsNullOrWhiteSpace(partName) ? null : partName.Trim(),
                Math.Clamp(partWidth, 1, width), Math.Clamp(partHeight, 1, height));
        }
        p.History.Clear();
        return p;
    }

    /// <summary>새로 연 프로젝트에 부위가 있으면 맨 위 부위를 현재 레이어로 고른다.</summary>
    private void SelectFirstPart(SpriteProject p)
    {
        for (int i = p.Layers.Count - 1; i >= 0; i--)
            if (p.Layers[i].IsPart) { _ctx.SetCurrentLayer(i); return; }
    }

    private void CloseProject()
    {
        if (!ConfirmDiscard()) return;
        _ctx.SetProject(null);
        _browser.SelectFile(null);
    }

    /// <summary>Ctrl+S: .gpixel 저장 + (켜져 있으면) 시트·메타 내보내기. 저장했으면 true.</summary>
    private bool Save()
    {
        var p = _ctx.Project;
        if (p is null) return false;
        if (p.Path is null) return SaveAs();
        // PNG를 가져온 프로젝트는 아직 .gpixel 파일이 없다 — 제안된 경로에 그대로 저장한다.
        return SaveTo(p.Path);
    }

    private bool SaveAs()
    {
        var p = _ctx.Project;
        if (p is null) return false;
        var path = Ui.SaveFile(this, "다른 이름으로 저장",
            p.Path is null ? _browser.CurrentFolder ?? _settings.LastFolder : Path.GetDirectoryName(p.Path),
            p.Path is null ? "sprite.gpixel" : Path.GetFileName(p.Path),
            SpriteProject.FileExtension,
            new FileFilter("Gpixel 프로젝트", "*.gpixel"));
        if (path is null) return false;
        return SaveTo(path);
    }

    private bool SaveTo(string path) => SaveTo(path, interactive: true, out _);

    /// <summary>저장(+내보내기). <paramref name="interactive"/>가 false면 실패를 대화상자 대신 <paramref name="message"/>로 돌려준다.</summary>
    private bool SaveTo(string path, bool interactive, out string message)
    {
        var p = _ctx.Project!;
        _ctx.CommitFloating();
        ProjectSaveResult result;
        var stampBefore = SafeStamp(path);
        try
        {
            result = ProjectIO.SaveWithExport(p, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                       or InvalidOperationException or System.Security.SecurityException)
        {
            // .gpixel은 저장했고 내보내기(PNG·사이드카·메타)만 실패했으면: 파일 감시가 자기 저장을 "밖에서 바뀜"으로 보지 않게 하고 그대로 알린다.
            bool projectSaved = !p.IsDirty && SafeStamp(path) is var stamp && stamp != default && stamp != stampBefore;
            if (projectSaved)
            {
                _docStamp = SafeStamp(p.Path!);
                _browser.RefreshFile(p.Path!);
                UpdateTitle();
                UpdateStatus();
            }
            message = projectSaved
                ? $"프로젝트는 저장했지만 PNG 시트·.meta 내보내기에 실패했습니다(다른 프로그램이 파일을 쓰고 있는지 확인하세요).\n{path}\n\n{ex.Message}"
                : $"저장하지 못했습니다. 프로젝트는 수정된 상태로 남아 있습니다.\n{path}\n\n{ex.Message}";
            if (interactive) Ui.Error(this, message, "저장 실패");
            return false;
        }
        _docStamp = SafeStamp(result.ProjectPath);
        AppSettings.PushRecent(_settings.RecentFiles, result.ProjectPath);
        message = $"저장: {Path.GetFileName(result.ProjectPath)}" + ExportSummary(result.Export) + TrashStale(result.Export);
        ShowMessage(message);
        _browser.RefreshFile(result.ProjectPath);
        _browser.SelectFile(result.ProjectPath);
        _play?.DocumentChanged();
        UpdateTitle();
        UpdateStatus();
        return true;
    }

    /// <summary>저장/내보내기 결과 요약: 프레임 시트·부위 아틀라스·Sprite Library 각각(파일이 여러 개면 파일 수와 칸·라벨 합계).</summary>
    private static string ExportSummary(ProjectExportResult? e)
    {
        if (e is null || e.IsEmpty) return " (내보내기 꺼짐)";
        static string One(string label, IReadOnlyList<ExportResult> files, string unit = "칸")
        {
            var meta = files.Any(r => r.MetaWritten) ? " + .meta" : "";
            return files.Count == 1
                ? $"{label} {Path.GetFileName(files[0].ImagePath)} {unit} {files[0].SpriteCount}개{meta}"
                : $"{label} {files.Count}개 파일 {unit} {files.Sum(r => r.SpriteCount)}개{meta}";
        }
        var parts = new List<string>(3);
        if (e.Frames.Count > 0) parts.Add(One("프레임", e.Frames));
        if (e.Parts.Count > 0) parts.Add(One("부위", e.Parts));
        if (e.Libraries.Count > 0) parts.Add(One("Sprite Library", e.Libraries, "라벨"));
        return " → " + string.Join(" · ", parts);
    }

    /// <summary>
    /// 지난번에 이 프로젝트가 내보냈지만 이번에 만들지 않은 옛 결과(이미지·.meta)를 앱 휴지통으로 옮기고 요약 문구를 돌려준다.
    /// 옮기지 못해도 내보내기는 성공으로 두고 알리기만 한다.
    /// </summary>
    private string TrashStale(ProjectExportResult? e)
    {
        if (e is null || e.Stale.Count == 0) return "";
        var first = Path.GetFileName(e.Stale[0]);
        var label = $"내보내기 정리: {(e.Stale.Count == 1 ? first : $"{first} 외 {e.Stale.Count - 1}개")}";
        string text;
        try
        {
            var entry = _browser.Trash.Add(e.Stale, label);
            text = $" · 옛 결과 {entry?.Files.Count ?? 0}개 정리(앱 휴지통)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text = $" · 옛 결과 정리 실패({ex.Message})";
        }
        return text;
    }

    /// <summary>Ctrl+E: 내보내기 설정 창. 파란 "내보내기"를 누르면 창을 닫고 지금 설정대로 내보낸다.</summary>
    private void OpenExportDialog()
    {
        if (!_ctx.HasProject) return;
        var dlg = new UnityExportDialog(_ctx, _settings, v => _settings.LastPpu = v);
        if (dlg.ShowDialog(this) == DialogResult.OK) ExportNow();
    }

    /// <summary>파일 > GIF 애니메이션으로 내보내기: 범위·배율·반복·배경을 고르는 창(프로젝트는 바꾸지 않음).</summary>
    private void ExportGif()
    {
        if (GifExportDialog.Run(_ctx, _settings, this) is not var (message, path)) return;
        ShowMessage(message);
        _browser.RefreshFile(path);
    }

    private void ExportNow()
    {
        var p = _ctx.Project!;
        if (p.Path is null && !SaveAs()) return;
        try
        {
            var e = ProjectExport.Export(p);
            ShowMessage("내보내기:" + ExportSummary(e).TrimStart(' ', '→') + TrashStale(e));
            foreach (var r in e.All) _browser.RefreshFile(r.ImagePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Ui.Error(this, $"내보내지 못했습니다.\n{ex.Message}", "내보내기 실패");
        }
    }

    private void OnFileRenamed(string oldPath, string newPath)
    {
        int i = _settings.RecentFiles.FindIndex(p => string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) _settings.RecentFiles[i] = newPath;
        if (_ctx.Project is { Path: { } p } proj && string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            proj.Path = newPath;
            proj.ExportedFiles = ProjectIO.RemapExportRecord(proj.ExportedFiles, oldPath, newPath);
            _docStamp = SafeStamp(newPath);
            ShowMessage($"이름 바꿈: {Path.GetFileName(newPath)}");
        }
    }

    private void OnFileDeleted(string path)
    {
        _settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (_ctx.Project?.Path is { } p && string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
        {
            _ctx.Project.IsDirty = true;
            ShowMessage($"열린 프로젝트 파일을 휴지통으로 보냈습니다: {Path.GetFileName(path)} — 저장하면 다시 만듭니다.");
        }
    }

    /// <summary>파일 감시: 열린 프로젝트가 밖에서 바뀌었으면 다시 불러올지 묻는다.</summary>
    private void OnFilesChanged(IReadOnlyCollection<string> paths)
    {
        var p = _ctx.Project;
        if (p?.Path is not { } path || !paths.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase))) return;
        if (!File.Exists(path)) return;
        var stamp = SafeStamp(path);
        if (stamp == _docStamp) return;
        _docStamp = stamp;
        var message = p.IsDirty
            ? $"\"{p.DisplayName}\" 파일이 밖에서 바뀌었습니다.\n다시 불러오면 지금 수정한 내용은 사라집니다. 다시 불러올까요?"
            : $"\"{p.DisplayName}\" 파일이 밖에서 바뀌었습니다. 다시 불러올까요?";
        if (Ui.Ask(this, message, "파일 변경", MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes)
        {
            ShowMessage("밖에서 바뀐 파일을 다시 불러오지 않았습니다. 저장하면 덮어씁니다.");
            return;
        }
        p.IsDirty = false;
        _ctx.SetProject(null);
        OpenFile(path);
    }

    // ───────────── 편집 ─────────────

    private void Undo()
    {
        // 파일 브라우저에 포커스가 있으면 마지막 파일 삭제를 되살린다(앱 휴지통).
        if (_browser.IsKeyboardFocusWithin)
        {
            _browser.RestoreLastDeleted();
            return;
        }
        // 변형 중이면 변형 전으로 되돌린다(Esc와 같음).
        if (_ctx.IsTransforming)
        {
            _ctx.CancelTransform();
            ShowMessage("변형 취소");
            return;
        }
        var label = _ctx.Project?.History.UndoLabel;
        if (_ctx.UndoStep()) ShowMessage($"실행취소: {label}");
    }

    private void Redo()
    {
        var label = _ctx.Project?.History.RedoLabel;
        if (_ctx.RedoStep()) ShowMessage($"다시 실행: {label}");
    }

    private void Copy()
    {
        // 타임라인에 포커스가 있으면 셀/프레임 대상
        if (_timeline.IsGridFocused) { _timeline.CopySelection(false); ShowMessage("타임라인: 복사"); return; }
        PixelImage? clip;
        if (_ctx.Selection.HasSelection) clip = _ctx.Selection.Copy(_ctx.EditImageOrEmpty);
        else
        {
            // 선택이 없으면 부위는 그 부위 셀, 그 밖에는 현재 프레임 합성 그림 전체를 복사한다.
            clip = _ctx.CurrentPart is not null ? _ctx.EditImageOrEmpty.Clone() : _ctx.Project!.Flatten(_ctx.CurrentFrame);
        }
        if (clip is null) return;
        _ctx.Clipboard = clip;
        PutOnSystemClipboard(clip);
        ShowMessage($"복사: {clip.Width}×{clip.Height}");
    }

    private void Cut()
    {
        // 타임라인에 포커스가 있으면 셀/프레임 대상
        if (_timeline.IsGridFocused) { _timeline.CopySelection(true); ShowMessage("타임라인: 잘라내기"); return; }
        if (!_ctx.Selection.HasSelection)
        {
            ShowMessage("잘라낼 선택 영역이 없습니다.");
            return;
        }
        if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 잠겨 있습니다."); return; }
        _ctx.BeginSelectionEdit();
        var clip = _ctx.Selection.Cut(_ctx.EditImage!)!;
        _ctx.NotifyPixels();
        _ctx.CommitFloating();
        _ctx.Clipboard = clip;
        PutOnSystemClipboard(clip);
        ShowMessage($"잘라내기: {clip.Width}×{clip.Height}");
    }

    /// <summary>시스템 클립보드에 PNG로 올린다(다른 그림 프로그램에 붙여넣기용). 실패하면 앱 내부 클립보드만 쓴다.</summary>
    private void PutOnSystemClipboard(PixelImage clip)
    {
        if (Clipboard is not { } cb) return;
        try
        {
            using var png = new MemoryStream();
            PngIO.Save(clip, png);
            var item = new DataTransferItem();
            item.Set(PngClipboardFormat, png.ToArray());
            // Windows 프로그램 대부분은 PNG 대신 비트맵(DIB)을 읽는다.
            if (OperatingSystem.IsWindows()) item.SetBitmap(ImageUtil.ToBitmap(clip));
            var data = new DataTransfer();
            data.Add(item);
            Ui.Wait(cb.SetDataAsync(data));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 다른 프로그램이 클립보드를 잡고 있으면 앱 내부 클립보드만 쓴다.
        }
    }

    private PixelImage? ReadClipboard()
    {
        if (Clipboard is { } cb)
        {
            try
            {
                if (Ui.Wait(cb.TryGetValueAsync(PngClipboardFormat)) is { Length: > 0 } bytes)
                {
                    using var png = new MemoryStream(bytes, writable: false);
                    return PngIO.Load(png);
                }
                using var bmp = Ui.Wait(cb.TryGetBitmapAsync());
                if (bmp is not null)
                {
                    using var ms = new MemoryStream();
                    bmp.Save(ms);
                    ms.Position = 0;
                    return PngIO.Load(ms);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // 읽을 수 없는 클립보드 내용이면 앱 내부 클립보드를 쓴다.
            }
        }
        return _ctx.Clipboard;
    }

    private void Paste()
    {
        // 타임라인에 포커스가 있으면 셀/프레임 대상
        if (_timeline.IsGridFocused && _timeline.PasteSelection()) { ShowMessage("타임라인: 붙여넣기"); return; }
        var clip = ReadClipboard();
        if (clip is null)
        {
            ShowMessage("붙여넣을 그림이 없습니다.");
            return;
        }
        if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 잠겨 있습니다."); return; }
        // 끌어 옮길 수 있는 선택 도구(사각·원·자유)는 그대로 두고, 그 밖이면 사각 선택으로.
        if (_ctx.Tool is not (ToolKind.Select or ToolKind.EllipseSelect or ToolKind.Lasso)) _ctx.Tool = ToolKind.Select;
        _ctx.CommitFloating();
        _ctx.BeginSelectionEdit();
        _ctx.Selection.Paste(_ctx.EditImage!, clip, _canvas.PasteLocation());
        _ctx.NotifyPixels();
        ShowMessage($"붙여넣기: {clip.Width}×{clip.Height} — 끌어서 옮기고 Enter로 내려놓기");
        _canvas.Focus();
    }

    private void DeleteSelection()
    {
        if (!_ctx.Selection.HasSelection || !_ctx.CanEdit) return;
        _ctx.BeginSelectionEdit();
        _ctx.Selection.Delete(_ctx.EditImage!);
        _ctx.NotifyPixels();
        _ctx.CommitFloating();
    }

    private void SelectAll()
    {
        if (!EditorContext.IsSelectionTool(_ctx.Tool) && _ctx.Tool != ToolKind.Move) _ctx.Tool = ToolKind.Select;
        _ctx.CommitFloating();
        _ctx.Selection.SelectAll(_ctx.EditImageOrEmpty);
    }

    private void Deselect()
    {
        if (!_ctx.HasProject || _canvas.ClearPartSelection()) return;
        _ctx.CommitFloating();
        _ctx.Selection.Deselect(_ctx.EditImageOrEmpty);
    }

    /// <summary>선택이 있으면 선택 내용, 없으면 현재 셀을 뒤집는다.</summary>
    private void Flip(bool horizontal)
    {
        if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 잠겨 있습니다."); return; }
        string label = horizontal ? "좌우 뒤집기" : "상하 뒤집기";
        if (_ctx.Selection.HasSelection)
        {
            _ctx.BeginSelectionEdit();
            if (horizontal) _ctx.Selection.FlipHorizontal(_ctx.EditImage!);
            else _ctx.Selection.FlipVertical(_ctx.EditImage!);
            _ctx.NotifyPixels();
            ShowMessage($"{label}(선택) — Enter로 내려놓기");
            return;
        }
        _ctx.CommitFloating();
        _ctx.BeginStroke();
        var img = _ctx.EditImage!;
        img.CopyFrom(horizontal ? ImageTransform.FlipHorizontal(img) : ImageTransform.FlipVertical(img));
        _ctx.NotifyPixels();
        _ctx.CommitStroke(label);
        ShowMessage($"{label}(현재 셀)");
    }

    /// <summary>선택이 있으면 선택 내용, 없으면 캔버스 전체(모든 프레임)를 90° 회전.</summary>
    private void Rotate(bool clockwise)
    {
        if (_ctx.Selection.HasSelection)
        {
            if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 잠겨 있습니다."); return; }
            _ctx.BeginSelectionEdit();
            _ctx.Selection.Rotate90(_ctx.EditImage!, clockwise);
            _ctx.NotifyPixels();
            ShowMessage("선택 회전 — Enter로 내려놓기");
            return;
        }
        _ctx.Structural(() => _ctx.Project!.RotateCanvas90(clockwise));
        _canvas.FitToWindow();
        ShowMessage("캔버스 회전(모든 프레임)");
    }

    /// <summary>
    /// 변형 모드(Ctrl+T): 선택 영역(없으면 현재 셀의 칠한 픽셀 전체)을 손잡이로 회전·크기·옮기기. Enter 적용, Esc·Ctrl+Z 취소.
    /// </summary>
    private void BeginTransform()
    {
        if (_ctx.BeginTransform() is { } error)
        {
            ShowMessage(error);
            return;
        }
        ShowMessage("변형: 안쪽 = 옮기기, 손잡이 = 크기, 바깥 = 회전 — Enter 적용, Esc 취소");
        _canvas.Focus();
    }

    /// <summary>선택 영역(없으면 현재 셀 전체)의 불투명 픽셀 둘레에 기본색으로 1픽셀 외곽선.</summary>
    private void AddOutline(bool diagonal)
    {
        if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 잠겨 있습니다."); return; }
        _ctx.CommitFloating();
        PixelMask region = _ctx.Selection.HasSelection ? _ctx.Selection.Region : new Rectangle(0, 0, _ctx.CanvasWidth, _ctx.CanvasHeight);
        _ctx.BeginStroke();
        int n = PixelOps.AddOutline(_ctx.EditImage!, region, _ctx.Primary, diagonal);
        _ctx.NotifyPixels();
        _ctx.CommitStroke("외곽선 추가");
        ShowMessage(n > 0 ? $"외곽선 {n}픽셀" : "외곽선을 그릴 곳이 없습니다(불투명 픽셀이 없음).");
    }

    private void ReplaceColor()
    {
        if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 잠겨 있습니다."); return; }
        _ctx.CommitFloating();
        var dlg = new ReplaceColorDialog(_ctx.Primary, _ctx.Secondary, _ctx.Selection.HasSelection);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        PixelMask? clip = dlg.SelectionOnly ? _ctx.Selection.Region : null;
        _ctx.BeginStroke();
        int n = Raster.ReplaceColor(_ctx.EditImage!, dlg.From, dlg.To, clip);
        _ctx.NotifyPixels();
        _ctx.CommitStroke("색 바꾸기");
        ShowMessage($"색 바꾸기: {n}픽셀");
    }

    /// <summary>현재 그룹(부위면 그 그룹)의 스킨을 앞뒤로 바꾼다(착장 전환, 실행취소 기록 없음).</summary>
    private void StepSkin(int delta)
    {
        if (_ctx.CurrentGroup is not { } group)
        {
            ShowMessage("그룹이나 부위를 고르면 그 그룹의 스킨을 바꿀 수 있습니다.");
            return;
        }
        if (_ctx.StepSkin(group, delta)) ShowMessage($"\"{group.Name}\" 스킨: {group.ActiveSkin!.Name}");
        else ShowMessage($"\"{group.Name}\" 그룹은 스킨이 하나뿐입니다 — 타임라인 이름 칸의 스킨 칩을 눌러 스킨 관리 창에서 스킨을 더하세요.");
    }

    /// <summary>현재 프레임 그림(시트)을 격자/자동으로 나눠 칸마다 프레임 하나인 프로젝트로 바꾼다.</summary>
    private void SplitIntoFrames()
    {
        var p = _ctx.Project!;
        _ctx.CommitFloating();
        var image = p.Flatten(_ctx.CurrentFrame);
        var dlg = new SplitFramesDialog(image, 16, 16);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var rects = dlg.Rects;
        if (p.Frames.Count > 1 && Ui.Ask(this,
                $"지금 프레임 {p.Frames.Count}개·레이어·태그를 버리고, 현재 프레임 그림을 나눈 {rects.Count}개 프레임으로 바꿉니다. 실행취소할 수 없습니다.",
                "격자로 나누기", MessageButtons.OKCancel, MessageIcon.Warning) != DialogResult.OK) return;
        var doc = new Document(image);
        doc.Sprite.CopyFrom(p.Sprite);
        doc.Sprite.Mode = SpriteMode.Multiple;
        doc.Slices.AddRange(Gpixel.Core.Slicing.NamingRules.CreateSlices(rects,
            new Gpixel.Core.Slicing.NamingOptions { BaseName = p.BaseName }, p.Sprite.DefaultPivot));
        var split = ProjectImport.FromDocument(doc);
        split.Export.CopyFrom(p.Export);
        split.ExportedFiles = p.ExportedFiles;
        split.Path = p.Path;
        split.History.Clear();
        split.IsDirty = true;
        _ctx.SetProject(split);
        ShowMessage($"격자로 나눔: 프레임 {split.Frames.Count}개 ({split.Width}×{split.Height}) — 태그를 만들어 애니메이션을 정하세요.");
    }

    /// <summary>캔버스(프레임) 크기 바꾸기. 부위 크기는 "부위 크기…"로 바꾼다.</summary>
    private void ResizeCanvas()
    {
        var p = _ctx.Project!;
        var dlg = new ResizeDialog(p.Width, p.Height);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.NewWidth == p.Width && dlg.NewHeight == p.Height) return;
        _ctx.Structural(() => p.ResizeCanvas(dlg.NewWidth, dlg.NewHeight, dlg.ImageAnchor));
        _canvas.FitToWindow();
        ShowMessage($"캔버스 크기: {p.Width}×{p.Height}");
    }

    // ───────────── AI 채팅(Claude·Codex, AI 도구 호스트) ─────────────

    /// <summary>AI 채팅 창을 연다(이미 열려 있으면 앞으로). 처음 열 때 편집기 안 MCP 서버를 띄운다.</summary>
    private void ToggleChat()
    {
        if (_chat is not null)
        {
            if (_chat.WindowState == WindowState.Minimized) _chat.WindowState = WindowState.Normal;
            _chat.Activate();
            return;
        }
        if (_mcp is null)
        {
            var server = new Ai.McpServer(new Ai.PixelTools(this));
            try { server.Start(); }
            catch (Exception ex) when (ex is System.Net.HttpListenerException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                server.Dispose();
                Ui.Error(this, $"AI 연결용 내부 서버(MCP)를 시작하지 못했습니다.\n{ex.Message}");
                return;
            }
            _mcp = server;
        }
        var chat = new Ai.ChatWindow(_ctx, _mcp, _settings);
        chat.Closed += (_, _) => { if (ReferenceEquals(_chat, chat)) _chat = null; };
        _chat = chat;
        chat.Show(this);
    }

    /// <summary>앱을 닫을 때: 채팅이 열려 있었는지 기억하고 AI 프로세스·MCP 서버를 끝낸다.</summary>
    private void ShutdownChat()
    {
        _settings.ChatOpen = _chat is not null;
        if (_chat is { } chat)
        {
            _chat = null;
            chat.Shutdown();
            chat.Close();
        }
        _mcp?.Dispose();
        _mcp = null;
    }

    /// <summary>Claude 도구가 지금 프로젝트를 바꿔도 되는지(저장 안 된 수정이 있으면 안 됨 — 대화상자 없이).</summary>
    private bool CanReplaceSilently(out string message)
    {
        _ctx.CommitFloating();
        if (_ctx.Project is { IsDirty: true } p)
        {
            message = $"\"{p.DisplayName}\"에 저장 안 된 수정이 있습니다. 먼저 save_project로 저장하거나 사용자에게 저장·닫기를 부탁하세요.";
            return false;
        }
        message = "";
        return true;
    }

    EditorContext Ai.IAiHost.Context => _ctx;

    string? Ai.IAiHost.CurrentFolder => _browser.CurrentFolder ?? _browser.RootFolder;

    IReadOnlyList<Color32> Ai.IAiHost.Palette => _colors.ActivePalette;

    void Ai.IAiHost.SetPalette(IReadOnlyList<Color32> colors) => _colors.SetActivePalette(colors);

    bool Ai.IAiHost.CreateProject(int width, int height, string? fileName, Color32? background, out string message)
    {
        if (width < 1 || height < 1 || width > PixelImage.MaxSize || height > PixelImage.MaxSize)
        {
            message = $"크기는 1~{PixelImage.MaxSize} 사이여야 합니다: {width}×{height}";
            return false;
        }
        string? path = null;
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var folder = _browser.CurrentFolder ?? _browser.RootFolder;
            if (folder is null)
            {
                message = "열린 폴더가 없어 파일로 저장할 수 없습니다. file_name 없이 만들거나 사용자에게 폴더를 열어 달라고 하세요.";
                return false;
            }
            var name = Path.GetFileName(fileName.Trim());
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                message = $"쓸 수 없는 파일 이름입니다: {fileName}";
                return false;
            }
            if (!name.EndsWith(SpriteProject.FileExtension, StringComparison.OrdinalIgnoreCase)) name += SpriteProject.FileExtension;
            path = Path.Combine(folder, name);
            if (File.Exists(path))
            {
                message = $"이미 같은 이름의 파일이 있습니다: {path}";
                return false;
            }
        }
        if (!CanReplaceSilently(out message)) return false;
        var p = BuildProject(width, height, background);
        _ctx.SetProject(p);
        if (path is null)
        {
            p.IsDirty = true;
            message = $"새 프로젝트 {width}×{height} — 아직 저장 위치 없음";
            ShowMessage(message);
            return true;
        }
        if (!SaveTo(path, interactive: false, out var saveMessage))
        {
            message = $"새 프로젝트를 만들었지만 저장하지 못했습니다: {saveMessage}";
            return true;
        }
        _browser.RefreshFile(path);
        _browser.SelectFile(path);
        message = $"새 프로젝트 {width}×{height} → {path}";
        return true;
    }

    bool Ai.IAiHost.SaveProject(out string message)
    {
        if (_ctx.Project is not { } p)
        {
            message = "열린 프로젝트가 없습니다.";
            return false;
        }
        if (p.Path is null)
        {
            message = "아직 저장 위치가 없는 프로젝트입니다. 사용자에게 Ctrl+S(⌘S)로 저장 위치를 고르도록 안내하세요.";
            return false;
        }
        return SaveTo(p.Path, interactive: false, out message);
    }

    bool Ai.IAiHost.OpenFile(string path, out string message) => OpenFile(path, interactive: false, out message);
}
