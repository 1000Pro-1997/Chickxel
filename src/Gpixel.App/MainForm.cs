using System.Runtime.InteropServices;
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
public sealed class MainForm : Form
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
        (ToolKind.Pencil, "연필", "B", "왼쪽: 기본색 · 오른쪽: 보조색(또는 지우개) · Shift+클릭: 마지막 점에서 직선 · Ctrl/Alt+클릭: 색 집기"),
        (ToolKind.Eraser, "지우개", "E", "투명으로 지웁니다"),
        (ToolKind.Fill, "채우기", "G", "이어진 같은 색 영역(전체색: 그 색 전부)을 채웁니다"),
        (ToolKind.Picker, "스포이드", "I", "왼쪽: 기본색 · 오른쪽: 보조색으로 집기"),
        (ToolKind.Line, "선", "L", "Shift: 45° 단위"),
        (ToolKind.Rectangle, "사각형", "R", "Shift: 정사각형 · U: 외곽/채움"),
        (ToolKind.Ellipse, "원", "O", "Shift: 정원 · U: 외곽/채움"),
        (ToolKind.Select, "사각 선택", "M", "드래그로 선택, 안쪽을 끌어 이동 · Enter: 내려놓기"),
        (ToolKind.Move, "이동", "V", "선택이 있으면 선택 이동, 없으면 셀 전체 밀기(순환 옵션)"),
        (ToolKind.Dodge, "밝게/어둡게", "D", "왼쪽: 밝게 · 오른쪽: 어둡게(한 획에 한 번)"),
        (ToolKind.Dither, "디더링", "T", "체커 무늬로 기본색·보조색을 번갈아 칠합니다"),
        (ToolKind.ReplaceBrush, "색 바꾸기 브러시", "C", "처음 닿은 색만 기본색으로 바꿉니다"),
        (ToolKind.Pivot, "피벗", "K", "끌어서 피벗(Unity 포커스)을 옮깁니다"),
    ];

    private readonly AppSettings _settings;
    private readonly StartupArgs _args;
    private readonly EditorContext _ctx = new();
    private readonly Dictionary<Keys, (KeyScope Scope, Func<bool> Action)> _keys = new();

    private readonly PixelCanvas _canvas;
    private readonly StartPanel _start;
    private readonly FileBrowserPanel _browser;
    private readonly ColorPanel _colors;
    private readonly SpritePanel _sprite;
    private readonly TimelinePanel _timeline;
    private readonly PreviewPanel _preview;
    private readonly SplitContainer _splitFiles, _splitPalette, _splitRight, _splitTimeline, _splitRightDock;

    private readonly Dictionary<ToolKind, ToolStripButton> _toolButtons = new();
    private ToolStripButton _tbFiles = null!;
    private ToolStripLabel _cbToolName = null!, _cbHint = null!, _cbBrushLabel = null!, _cbZoom = null!;
    private ToolStripComboBox _cbBrush = null!;
    private ToolStripButton _cbPixelPerfect = null!, _cbFilled = null!, _cbGlobalFill = null!, _cbRightErase = null!, _cbWrap = null!,
        _cbMirrorX = null!, _cbMirrorY = null!, _cbGrid = null!, _cbTiles = null!, _cbTiled = null!, _cbPivot = null!, _cbOnion = null!;
    private ToolStripSeparator _cbSepMirror = null!;

    private ToolStripMenuItem _miUndo = null!, _miRedo = null!, _miRecentFolders = null!, _miRecentFiles = null!;
    private ToolStripMenuItem _miGrid = null!, _miTiles = null!, _miTiled = null!, _miPivot = null!, _miOnion = null!, _miFiles = null!;
    private ToolStripMenuItem _miPixelPerfect = null!, _miFilled = null!, _miGlobalFill = null!, _miMirrorX = null!, _miMirrorY = null!,
        _miRightErase = null!, _miWrap = null!;
    private readonly List<ToolStripItem> _docItems = new();
    /// <summary>애니메이션 프로젝트에서만 쓰는 명령(아틀라스면 비활성).</summary>
    private readonly List<ToolStripItem> _animItems = new();
    /// <summary>아틀라스 프로젝트에서만 쓰는 명령.</summary>
    private readonly List<ToolStripItem> _atlasItems = new();
    private ToolStripMenuItem _miPlayTest = null!, _miSpriteMenu = null!;
    private readonly Dictionary<ToolKind, ToolStripMenuItem> _toolMenuItems = new();

    private readonly ToolStripStatusLabel _stPos, _stColor, _stSize, _stFrame, _stZoom, _stDirty, _stMessage;

    private PlayTestForm? _play;
    private readonly System.Windows.Forms.Timer _playRefresh = new() { Interval = 400 };
    private DateTime _docStamp;
    private bool _settingsApplied;

    public MainForm(StartupArgs args)
    {
        _args = args;
        _settings = AppSettings.Load();
        Text = "Gpixel";
        Font = Theme.UiFont;
        BackColor = Theme.BackColor;
        ForeColor = Theme.TextColor;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(1000, 600);

        // ── 패널
        _canvas = new PixelCanvas(_ctx) { Dock = DockStyle.Fill };
        _start = new StartPanel { Dock = DockStyle.Fill };
        _browser = new FileBrowserPanel { Dock = DockStyle.Fill };
        _colors = new ColorPanel(_ctx) { Dock = DockStyle.Fill };
        _sprite = new SpritePanel(_ctx) { Dock = DockStyle.Fill };
        _preview = new PreviewPanel(_ctx) { Dock = DockStyle.Fill };
        _timeline = new TimelinePanel(_ctx, _preview) { Dock = DockStyle.Fill };

        var canvasHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BackColor };
        canvasHost.Controls.Add(_canvas);
        canvasHost.Controls.Add(_start);

        _splitRightDock = MakeSplit(Orientation.Horizontal, _preview, _sprite, FixedPanel.Panel1);
        _splitTimeline = MakeSplit(Orientation.Horizontal, canvasHost, _timeline, FixedPanel.Panel2);
        _splitRight = MakeSplit(Orientation.Vertical, _splitTimeline, _splitRightDock, FixedPanel.Panel2);
        _splitPalette = MakeSplit(Orientation.Vertical, _colors, _splitRight, FixedPanel.Panel1);
        var work = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BackColor };
        work.Controls.Add(_splitPalette);
        work.Controls.Add(BuildToolBar());
        _splitFiles = MakeSplit(Orientation.Vertical, _browser, work, FixedPanel.Panel1);

        // ── 상태 표시줄
        _stPos = StatusLabel(80);
        _stColor = StatusLabel(100);
        _stSize = StatusLabel(90);
        _stFrame = StatusLabel(180);
        _stZoom = StatusLabel(60);
        _stDirty = StatusLabel(80);
        _stMessage = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.DimTextColor };
        var status = new StatusStrip { SizingGrip = true, Renderer = Theme.Renderer, BackColor = Theme.PanelColor };
        status.Items.AddRange([_stPos, _stColor, _stSize, _stFrame, _stZoom, _stDirty, _stMessage]);

        var menu = BuildMenu();
        var contextBar = BuildContextBar();
        Controls.Add(_splitFiles);
        Controls.Add(status);
        Controls.Add(contextBar);
        Controls.Add(menu);
        MainMenuStrip = menu;

        WireEvents();
        ApplySettingsBeforeShow();
        Theme.Apply(this);
        UpdateAll();
    }

    // ───────────── 구성 ─────────────

    private static SplitContainer MakeSplit(Orientation orientation, Control a, Control b, FixedPanel fixedPanel)
    {
        var sc = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = orientation,
            SplitterWidth = 3,
            FixedPanel = fixedPanel,
            BackColor = Theme.BorderColor,
        };
        sc.Panel1.BackColor = Theme.PanelColor;
        sc.Panel2.BackColor = Theme.PanelColor;
        sc.Panel1.Controls.Add(a);
        sc.Panel2.Controls.Add(b);
        return sc;
    }

    private static ToolStripStatusLabel StatusLabel(int width) => new()
    {
        AutoSize = false,
        Width = width,
        TextAlign = ContentAlignment.MiddleLeft,
        BorderSides = ToolStripStatusLabelBorderSides.Right,
        BorderStyle = Border3DStyle.Flat,
        ForeColor = Theme.TextColor,
    };

    /// <summary>왼쪽 끝 세로 도구 막대(점 그림 아이콘, 도움말에 단축키).</summary>
    private ToolStrip BuildToolBar()
    {
        var ts = new ToolStrip
        {
            Dock = DockStyle.Left,
            LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow,
            GripStyle = ToolStripGripStyle.Hidden,
            Renderer = Theme.Renderer,
            BackColor = Theme.PanelColor,
            Padding = new Padding(2, 4, 2, 4),
            ImageScalingSize = new Size(Icons.Size, Icons.Size),
            AutoSize = false,
            Width = Icons.Size + 14,
        };
        _tbFiles = new ToolStripButton(Icons.Folder)
        {
            ToolTipText = "파일 브라우저 보이기/숨기기 (F8)",
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Margin = new Padding(1, 1, 1, 6),
        };
        _tbFiles.Click += (_, _) => SetFilesVisible(_splitFiles.Panel1Collapsed);
        ts.Items.Add(_tbFiles);
        foreach (var (tool, name, key, _) in ToolDefs)
        {
            var b = new ToolStripButton(Icons.For(tool))
            {
                ToolTipText = $"{name} ({key})",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Margin = new Padding(1),
            };
            var t = tool;
            b.Click += (_, _) => { _ctx.Tool = t; _canvas.Focus(); };
            _toolButtons[tool] = b;
            ts.Items.Add(b);
        }
        return ts;
    }

    /// <summary>메뉴 아래 도구 옵션 막대(현재 도구에 맞는 옵션만 보임) + 보기 옵션·확대율.</summary>
    private ToolStrip BuildContextBar()
    {
        var ts = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Renderer = Theme.Renderer,
            BackColor = Theme.PanelColor,
            Padding = new Padding(6, 2, 6, 2),
        };
        _cbToolName = new ToolStripLabel("연필") { Font = Theme.BoldFont, AutoSize = false, Width = 110, TextAlign = ContentAlignment.MiddleLeft };
        _cbBrushLabel = new ToolStripLabel("크기");
        _cbBrush = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 44, FlatStyle = FlatStyle.Flat };
        _cbBrush.Items.AddRange(Enumerable.Range(1, 8).Select(i => (object)i.ToString()).ToArray());
        _cbBrush.ComboBox.BackColor = Theme.InputColor;
        _cbBrush.ComboBox.ForeColor = Theme.TextColor;
        _cbBrush.SelectedIndexChanged += (_, _) => { if (_cbBrush.SelectedIndex >= 0) _ctx.BrushSize = _cbBrush.SelectedIndex + 1; };
        _cbBrush.ToolTipText = "브러시 크기 1~8 ([ / ])";
        ts.Items.AddRange([_cbToolName, new ToolStripSeparator(), _cbBrushLabel, _cbBrush]);
        _cbPixelPerfect = Toggle(ts, "픽셀 퍼펙트", "1px 연필의 L자 모서리 제거 (P)", () => _ctx.PixelPerfect = !_ctx.PixelPerfect);
        _cbFilled = Toggle(ts, "채움", "사각형·원 채움 (U)", () => _ctx.ShapeFilled = !_ctx.ShapeFilled);
        _cbGlobalFill = Toggle(ts, "같은 색 전체", "채우기: 이어진 영역 대신 같은 색 전체 (Shift+G)", () => _ctx.FillContiguous = !_ctx.FillContiguous);
        _cbRightErase = Toggle(ts, "오른쪽=지우개", "오른쪽 버튼으로 보조색 대신 지우기", () => _ctx.RightButtonErases = !_ctx.RightButtonErases);
        _cbWrap = Toggle(ts, "순환", "이동: 밖으로 나간 픽셀을 반대편으로", () => _ctx.MoveWrap = !_ctx.MoveWrap);
        _cbSepMirror = new ToolStripSeparator();
        ts.Items.Add(_cbSepMirror);
        _cbMirrorX = Toggle(ts, "대칭 ↔", "좌우 대칭 그리기 (Shift+X)", () => _ctx.MirrorX = !_ctx.MirrorX);
        _cbMirrorY = Toggle(ts, "대칭 ↕", "상하 대칭 그리기 (Shift+Y)", () => _ctx.MirrorY = !_ctx.MirrorY);
        _cbHint = new ToolStripLabel("") { ForeColor = Theme.DimTextColor };
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(_cbHint);

        // 오른쪽 정렬: 보기·확대
        RightButton(ts, "맞추기", "화면에 맞추기 (Ctrl+0)", _canvas.FitToWindow);
        RightButton(ts, "＋", "확대 (Ctrl+=)", () => _canvas.ZoomStep(1));
        _cbZoom = new ToolStripLabel("800%") { Alignment = ToolStripItemAlignment.Right, AutoSize = false, Width = 50 };
        ts.Items.Add(_cbZoom);
        RightButton(ts, "－", "축소 (Ctrl+-)", () => _canvas.ZoomStep(-1));
        ts.Items.Add(new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right });
        _cbOnion = RightToggle(ts, "양파", "양파 껍질: 앞/뒤 프레임 겹쳐 보기 (Ctrl+J)", () => _ctx.OnionSkin = !_ctx.OnionSkin);
        _cbPivot = RightToggle(ts, "피벗", "피벗 표시", () => SetShowPivot(!_canvas.ShowPivot));
        _cbTiled = RightToggle(ts, "타일 모드", "3×3 반복해 보기·이어 그리기 (Ctrl+T)", () => SetTiledMode(!_canvas.TiledMode));
        _cbTiles = RightToggle(ts, "타일 격자", "타일 격자 (Ctrl+Shift+G)", () => SetTileGrid(!_canvas.ShowTileGrid));
        _cbGrid = RightToggle(ts, "격자", "픽셀 격자(6배 이상) (Ctrl+G)", () => SetPixelGrid(!_canvas.ShowPixelGrid));
        foreach (ToolStripItem it in ts.Items) if (it != _cbHint) it.ForeColor = Theme.TextColor;
        return ts;
    }

    private static ToolStripButton Toggle(ToolStrip ts, string text, string tip, Action onClick)
    {
        var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text, Margin = new Padding(1) };
        b.Click += (_, _) => onClick();
        ts.Items.Add(b);
        return b;
    }

    private static ToolStripButton RightToggle(ToolStrip ts, string text, string tip, Action onClick)
    {
        var b = Toggle(ts, text, tip, onClick);
        b.Alignment = ToolStripItemAlignment.Right;
        return b;
    }

    private static ToolStripButton RightButton(ToolStrip ts, string text, string tip, Action onClick)
        => RightToggle(ts, text, tip, onClick);

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Renderer = Theme.Renderer, BackColor = Theme.PanelColor, ForeColor = Theme.TextColor };

        // 파일
        var file = TopMenu("파일(&F)");
        _miRecentFolders = new ToolStripMenuItem("최근 폴더") { ForeColor = Theme.TextColor };
        _miRecentFiles = new ToolStripMenuItem("최근 파일") { ForeColor = Theme.TextColor };
        _miRecentFolders.DropDownOpening += (_, _) => FillRecent(_miRecentFolders, _settings.RecentFolders, SetRootFolder);
        _miRecentFiles.DropDownOpening += (_, _) => FillRecent(_miRecentFiles, _settings.RecentFiles, p => OpenFile(p));
        _miRecentFolders.DropDownItems.Add("(비어 있음)");
        _miRecentFiles.DropDownItems.Add("(비어 있음)");
        file.DropDownItems.AddRange([
            Cmd("새 프로젝트…", () => NewProject(null), "Ctrl+N", KeyScope.Global, Keys.Control | Keys.N),
            Cmd("열기(.gpixel / PNG 가져오기)…", OpenFileDialogCmd, "Ctrl+O", KeyScope.Global, Keys.Control | Keys.O),
            Cmd("폴더 열기…", OpenFolder, "Ctrl+Shift+O", KeyScope.Global, Keys.Control | Keys.Shift | Keys.O),
            Cmd("Unity 프로젝트 열기…", OpenUnityProject, "Ctrl+Shift+U", KeyScope.Global, Keys.Control | Keys.Shift | Keys.U),
            _miRecentFolders,
            _miRecentFiles,
            new ToolStripSeparator(),
            DocCmd("저장(+ 시트 내보내기)", () => Save(), "Ctrl+S", KeyScope.Global, Keys.Control | Keys.S),
            DocCmd("다른 이름으로 저장…", () => SaveAs(), "Ctrl+Shift+S", KeyScope.Global, Keys.Control | Keys.Shift | Keys.S),
            DocCmd("지금 내보내기(PNG 시트 + .meta)", ExportNow, "Ctrl+E", KeyScope.Global, Keys.Control | Keys.E),
            DocCmd("닫기", () => CloseProject(), "Ctrl+W", KeyScope.Global, Keys.Control | Keys.W),
            new ToolStripSeparator(),
            Cmd("시작 화면", ShowStart),
            Cmd("끝내기", Close, "Alt+F4"),
        ]);

        // 편집
        var edit = TopMenu("편집(&E)");
        _miUndo = Cmd("실행취소", Undo, "Ctrl+Z", KeyScope.NotInText, Keys.Control | Keys.Z);
        _miRedo = Cmd("다시 실행", Redo, "Ctrl+Y", KeyScope.NotInText, Keys.Control | Keys.Y);
        Bind(Keys.Control | Keys.Shift | Keys.Z, KeyScope.NotInText, Redo);
        edit.DropDownItems.AddRange([
            _miUndo,
            _miRedo,
            new ToolStripSeparator(),
            DocCmd("잘라내기", Cut, "Ctrl+X", KeyScope.NotInText, Keys.Control | Keys.X),
            DocCmd("복사", Copy, "Ctrl+C", KeyScope.NotInText, Keys.Control | Keys.C),
            DocCmd("붙여넣기", Paste, "Ctrl+V", KeyScope.NotInText, Keys.Control | Keys.V),
            DocCmd("지우기", DeleteSelection, "Delete", KeyScope.Canvas, Keys.Delete),
            new ToolStripSeparator(),
            DocCmd("모두 선택", SelectAll, "Ctrl+A", KeyScope.NotInText, Keys.Control | Keys.A),
            DocCmd("선택 해제", Deselect, "Ctrl+D", KeyScope.NotInText, Keys.Control | Keys.D),
            new ToolStripSeparator(),
            DocCmd("좌우 뒤집기(선택/셀)", () => Flip(true), "Shift+H", KeyScope.NotInText, Keys.Shift | Keys.H),
            DocCmd("상하 뒤집기(선택/셀)", () => Flip(false), "Shift+V", KeyScope.NotInText, Keys.Shift | Keys.V),
            DocCmd("90° 회전(선택/캔버스)", () => Rotate(true), "Ctrl+R", KeyScope.NotInText, Keys.Control | Keys.R),
            DocCmd("-90° 회전(선택/캔버스)", () => Rotate(false), "Ctrl+Shift+R", KeyScope.NotInText, Keys.Control | Keys.Shift | Keys.R),
            new ToolStripSeparator(),
            DocCmd("외곽선 추가(기본색)", () => AddOutline(false), "Ctrl+Alt+O", KeyScope.NotInText, Keys.Control | Keys.Alt | Keys.O),
            DocCmd("외곽선 추가(대각 포함)", () => AddOutline(true)),
            DocCmd("색 바꾸기…", ReplaceColor, "Ctrl+Alt+R", KeyScope.NotInText, Keys.Control | Keys.Alt | Keys.R),
            new ToolStripSeparator(),
            DocCmd("캔버스 크기…(아틀라스: 스프라이트 크기)", ResizeCanvas, "Ctrl+Alt+C", KeyScope.Global, Keys.Control | Keys.Alt | Keys.C),
            AnimCmd("캔버스 전체 좌우 뒤집기(모든 프레임)", () => _ctx.Structural(() => _ctx.Project!.FlipCanvasHorizontal())),
            AnimCmd("캔버스 전체 상하 뒤집기(모든 프레임)", () => _ctx.Structural(() => _ctx.Project!.FlipCanvasVertical())),
            DocCmd("이미지에서 팔레트 만들기", _colors.PaletteFromImage),
        ]);
        Bind(Keys.Enter, KeyScope.Canvas, () => _ctx.CommitFloating());
        Bind(Keys.Escape, KeyScope.Canvas, Deselect);
        foreach (var (k, dx, dy) in new[] { (Keys.Left, -1, 0), (Keys.Right, 1, 0), (Keys.Up, 0, -1), (Keys.Down, 0, 1) })
        {
            _keys[k] = (KeyScope.Canvas, () => _canvas.Nudge(dx, dy));
            _keys[k | Keys.Control | Keys.Shift] = (KeyScope.Canvas, () => _canvas.Nudge(dx, dy));
            _keys[k | Keys.Shift] = (KeyScope.Canvas, () => _canvas.Nudge(dx * 8, dy * 8));
        }

        // 보기
        var view = TopMenu("보기(&V)");
        _miGrid = Cmd("픽셀 격자", () => SetPixelGrid(!_canvas.ShowPixelGrid), "Ctrl+G", KeyScope.Global, Keys.Control | Keys.G);
        _miTiles = Cmd("타일 격자", () => SetTileGrid(!_canvas.ShowTileGrid), "Ctrl+Shift+G", KeyScope.Global, Keys.Control | Keys.Shift | Keys.G);
        _miTiled = Cmd("타일 모드(3×3 반복)", () => SetTiledMode(!_canvas.TiledMode), "Ctrl+T", KeyScope.Global, Keys.Control | Keys.T);
        _miPivot = Cmd("피벗 표시", () => SetShowPivot(!_canvas.ShowPivot));
        _miOnion = AnimCmd("양파 껍질", () => _ctx.OnionSkin = !_ctx.OnionSkin, "Ctrl+J", KeyScope.Global, Keys.Control | Keys.J);
        _miFiles = Cmd("파일 브라우저", () => SetFilesVisible(_splitFiles.Panel1Collapsed), "F8", KeyScope.Global, Keys.F8);
        view.DropDownItems.AddRange([
            Cmd("확대", () => _canvas.ZoomStep(1), "Ctrl+=", KeyScope.NotInText, Keys.Control | Keys.Oemplus, Keys.Control | Keys.Add),
            Cmd("축소", () => _canvas.ZoomStep(-1), "Ctrl+-", KeyScope.NotInText, Keys.Control | Keys.OemMinus, Keys.Control | Keys.Subtract),
            Cmd("화면에 맞추기", _canvas.FitToWindow, "Ctrl+0", KeyScope.Global, Keys.Control | Keys.D0, Keys.Control | Keys.NumPad0),
            Cmd("실제 크기(1배)", () => _canvas.SetZoom(1), "Ctrl+1", KeyScope.Global, Keys.Control | Keys.D1, Keys.Control | Keys.NumPad1),
            new ToolStripSeparator(),
            _miGrid, _miTiles, _miTiled, _miPivot, _miOnion,
            Cmd("격자 설정…", GridSettings),
            new ToolStripSeparator(),
            _miFiles,
        ]);
        // H를 누르고 있는 동안 이전 프레임 보기(애니메이션만)
        _keys[Keys.H] = (KeyScope.NotInText, () => { if (_ctx.HasProject && !_ctx.IsAtlas) _canvas.PeekPrevious = true; return true; });

        // 프레임(아틀라스면 이동 명령은 스프라이트 사이를 움직임)
        var frame = TopMenu("프레임(&R)");
        frame.DropDownItems.AddRange([
            AnimCmd("재생 / 정지", _preview.TogglePlay, "Shift+Enter", KeyScope.Global, Keys.Shift | Keys.Enter),
            DocCmd("이전 프레임/스프라이트", () => _timeline.StepFrame(-1), ",", KeyScope.NotInText, Keys.Oemcomma),
            DocCmd("다음 프레임/스프라이트", () => _timeline.StepFrame(1), ".", KeyScope.NotInText, Keys.OemPeriod),
            DocCmd("첫 프레임/스프라이트", () => _ctx.SetCurrentColumn(0), "Home", KeyScope.Canvas, Keys.Home),
            DocCmd("마지막 프레임/스프라이트", () => _ctx.SetCurrentColumn(_ctx.ColumnCount - 1), "End", KeyScope.Canvas, Keys.End),
            new ToolStripSeparator(),
            AnimCmd("새 빈 프레임", _timeline.NewEmptyFrame, "Alt+N"),
            AnimCmd("프레임 복제", _timeline.DuplicateFrame, "Alt+D"),
            AnimCmd("프레임 삭제", _timeline.DeleteFrame),
            new ToolStripSeparator(),
            DocCmd("새 레이어", _timeline.AddLayer, "Shift+N", KeyScope.NotInText, Keys.Shift | Keys.N),
            AnimCmd("새 태그(애니메이션)…", _timeline.NewTag),
            new ToolStripSeparator(),
            AnimCmd("격자로 나눠 프레임 만들기(시트 → 프레임)…", SplitIntoFrames),
        ]);
        // Alt+N / Alt+D: 애니메이션 = 프레임, 아틀라스 = 스프라이트
        Bind(Keys.Alt | Keys.N, KeyScope.Global, () => { if (_ctx.IsAtlas) _timeline.AddSprite(); else _timeline.NewEmptyFrame(); });
        Bind(Keys.Alt | Keys.D, KeyScope.Global, () => { if (_ctx.IsAtlas) _timeline.DuplicateSprite(); else _timeline.DuplicateFrame(); });

        // 스프라이트(아틀라스)
        _miSpriteMenu = TopMenu("스프라이트(&P)");
        _miSpriteMenu.DropDownItems.AddRange([
            AtlasCmd("새 스프라이트…", _timeline.AddSprite, "Alt+N"),
            AtlasCmd("스프라이트 복제", _timeline.DuplicateSprite, "Alt+D"),
            AtlasCmd("스프라이트 삭제", _timeline.DeleteSprite),
            new ToolStripSeparator(),
            AtlasCmd("이름 바꾸기", _timeline.BeginRenameSprite, "F2", KeyScope.NotInText, Keys.F2),
            AtlasCmd("크기 바꾸기(기준점)…", _timeline.ResizeSprite),
            AtlasCmd("앞으로", () => _timeline.MoveSprite(-1)),
            AtlasCmd("뒤로", () => _timeline.MoveSprite(1)),
        ]);

        // 도구
        var tools = TopMenu("도구(&T)");
        foreach (var (tool, name, key, _) in ToolDefs)
        {
            var t = tool;
            var keyCode = (Keys)Enum.Parse(typeof(Keys), key);
            var mi = Cmd(name, () => _ctx.Tool = t, key, KeyScope.NotInText, keyCode);
            mi.Image = Icons.For(tool);
            _toolMenuItems[tool] = mi;
            tools.DropDownItems.Add(mi);
        }
        _miFilled = Cmd("도형 채움", () => _ctx.ShapeFilled = !_ctx.ShapeFilled, "U", KeyScope.NotInText, Keys.U);
        _miGlobalFill = Cmd("채우기: 같은 색 전체", () => _ctx.FillContiguous = !_ctx.FillContiguous, "Shift+G", KeyScope.NotInText, Keys.Shift | Keys.G);
        _miPixelPerfect = Cmd("픽셀 퍼펙트", () => _ctx.PixelPerfect = !_ctx.PixelPerfect, "P", KeyScope.NotInText, Keys.P);
        _miMirrorX = Cmd("좌우 대칭 그리기", () => _ctx.MirrorX = !_ctx.MirrorX, "Shift+X", KeyScope.NotInText, Keys.Shift | Keys.X);
        _miMirrorY = Cmd("상하 대칭 그리기", () => _ctx.MirrorY = !_ctx.MirrorY, "Shift+Y", KeyScope.NotInText, Keys.Shift | Keys.Y);
        _miRightErase = Cmd("오른쪽 버튼 = 지우개", () => _ctx.RightButtonErases = !_ctx.RightButtonErases);
        _miWrap = Cmd("이동: 순환", () => _ctx.MoveWrap = !_ctx.MoveWrap);
        tools.DropDownItems.AddRange([
            new ToolStripSeparator(),
            _miFilled, _miGlobalFill, _miPixelPerfect, _miMirrorX, _miMirrorY, _miRightErase, _miWrap,
            new ToolStripSeparator(),
            Cmd("브러시 크게", () => _ctx.BrushSize++, "]", KeyScope.NotInText, Keys.OemCloseBrackets),
            Cmd("브러시 작게", () => _ctx.BrushSize--, "[", KeyScope.NotInText, Keys.OemOpenBrackets),
            Cmd("기본색 ↔ 보조색", _ctx.SwapColors, "X", KeyScope.NotInText, Keys.X),
        ]);

        // 테스트
        var test = TopMenu("테스트(&S)");
        _miPlayTest = Cmd("움직임 테스트", ShowPlayTest, "F5", KeyScope.Global, Keys.F5);
        test.DropDownItems.Add(_miPlayTest);

        // 도움말
        var help = TopMenu("도움말(&H)");
        help.DropDownItems.AddRange([
            Cmd("단축키 목록", ShowShortcuts, "F1", KeyScope.Global, Keys.F1),
            Cmd("설정 파일 위치 열기", OpenSettingsFolder),
            Cmd("Gpixel 정보", () => Ui.Info(this,
                "Gpixel — 게임용 픽셀 아트 편집기\n프레임·레이어는 .gpixel 프로젝트로 저장되고, 저장할 때 PNG 시트 + Unity .meta가 내보내집니다.\n\n설정: " + AppSettings.FilePath,
                "Gpixel 정보")),
        ]);

        menu.Items.AddRange([file, edit, view, frame, _miSpriteMenu, tools, test, help]);
        return menu;
    }

    private static ToolStripMenuItem TopMenu(string text) => new(text) { ForeColor = Theme.TextColor };

    private ToolStripMenuItem Cmd(string text, Action action, string? keyText = null, KeyScope scope = KeyScope.Global, params Keys[] keys)
    {
        var mi = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keyText, ForeColor = Theme.TextColor };
        mi.Click += (_, _) => action();
        foreach (var k in keys) Bind(k, scope, action);
        return mi;
    }

    /// <summary>프로젝트가 있어야 쓸 수 있는 명령(없으면 비활성).</summary>
    private ToolStripMenuItem DocCmd(string text, Action action, string? keyText = null, KeyScope scope = KeyScope.Global, params Keys[] keys)
    {
        var mi = Cmd(text, () => { if (_ctx.HasProject) action(); }, keyText, scope, keys);
        _docItems.Add(mi);
        return mi;
    }

    /// <summary>애니메이션 프로젝트에서만 쓰는 명령(아틀라스·프로젝트 없음이면 비활성, 단축키도 무시).</summary>
    private ToolStripMenuItem AnimCmd(string text, Action action, string? keyText = null, KeyScope scope = KeyScope.Global, params Keys[] keys)
    {
        var mi = Cmd(text, () => { if (_ctx.HasProject && !_ctx.IsAtlas) action(); }, keyText, scope, keys);
        _animItems.Add(mi);
        return mi;
    }

    /// <summary>아틀라스 프로젝트에서만 쓰는 명령.</summary>
    private ToolStripMenuItem AtlasCmd(string text, Action action, string? keyText = null, KeyScope scope = KeyScope.Global, params Keys[] keys)
    {
        var mi = Cmd(text, () => { if (_ctx.IsAtlas) action(); }, keyText, scope, keys);
        _atlasItems.Add(mi);
        return mi;
    }

    private void Bind(Keys key, KeyScope scope, Action action) => _keys[key] = (scope, () => { action(); return true; });

    private void FillRecent(ToolStripMenuItem parent, List<string> list, Action<string> open)
    {
        parent.DropDownItems.Clear();
        foreach (var p in list)
        {
            var path = p;
            parent.DropDownItems.Add(new ToolStripMenuItem(p, null, (_, _) => open(path)) { ForeColor = Theme.TextColor });
        }
        if (list.Count == 0) parent.DropDownItems.Add(new ToolStripMenuItem("(비어 있음)") { Enabled = false });
        else
        {
            parent.DropDownItems.Add(new ToolStripSeparator());
            parent.DropDownItems.Add(new ToolStripMenuItem("목록 지우기", null, (_, _) => list.Clear()) { ForeColor = Theme.TextColor });
        }
    }

    // ───────────── 이벤트 연결 ─────────────

    private void WireEvents()
    {
        _ctx.ProjectReplaced += (_, _) =>
        {
            _docStamp = _ctx.Project?.Path is { } p ? SafeStamp(p) : default;
            // 아틀라스는 움직임 테스트를 쓰지 않는다(미리보기만).
            if (_ctx.IsAtlas) _play?.Close();
            _play?.DocumentChanged();
            UpdateAll();
        };
        _ctx.DirtyChanged += (_, _) => { UpdateTitle(); UpdateStatus(); };
        _ctx.ProjectChanged += (_, e) =>
        {
            if ((e.Change & (ProjectChange.Canvas | ProjectChange.Path | ProjectChange.Frames | ProjectChange.Sprites)) != 0) { UpdateTitle(); UpdateStatus(); }
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

        _browser.FileActivated += (_, path) =>
        {
            // 목록 선택 이벤트가 끝난 뒤 연다(저장 확인 대화상자·선택 되돌리기를 안전하게).
            BeginInvoke(() => { if (!OpenFile(path)) _browser.SelectFile(_ctx.Project?.Path); });
        };
        _browser.OpenFolderRequested += (_, _) => OpenFolder();
        _browser.OpenUnityRequested += (_, _) => OpenUnityProject();
        _browser.NewImageRequested += (_, folder) => NewProject(folder);
        _browser.FileRenamed += (_, r) => OnFileRenamed(r.OldPath, r.NewPath);
        _browser.FileDeleted += (_, path) => OnFileDeleted(path);
        _browser.FilesChanged += (_, paths) => OnFilesChanged(paths);

        _start.FolderChosen += (_, folder) => SetRootFolder(folder);
        _start.FileChosen += (_, file) => OpenFile(file);
        _start.OpenFolderRequested += (_, _) => OpenFolder();
        _start.NewImageRequested += (_, _) => NewProject(null);

        _sprite.PpuChanged += (_, v) => _settings.LastPpu = v;
        _sprite.ExportRequested += (_, _) => ExportNow();
        _colors.PaletteChanged += (_, _) => _settings.Palette = _colors.Palette.Select(c => c.ToHex()).ToList();
    }

    // ───────────── 설정 ─────────────

    private void ApplySettingsBeforeShow()
    {
        var s = _settings;
        var bounds = new Rectangle(s.WindowX, s.WindowY, Math.Max(MinimumSize.Width, s.WindowWidth), Math.Max(MinimumSize.Height, s.WindowHeight));
        if (s.WindowX >= 0 && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(bounds)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
        }
        else Size = bounds.Size;
        if (s.Maximized) WindowState = FormWindowState.Maximized;

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
        _ctx.PixelPerfect = s.PixelPerfect;
        _ctx.FillContiguous = s.FillContiguous;
        _ctx.ShapeFilled = s.ShapeFilled;
        _ctx.RightButtonErases = s.RightButtonErases;
        _ctx.MoveWrap = s.MoveWrap;
        _ctx.Primary = ColorParse.ParseOr(s.PrimaryColor, Color32.Black);
        _ctx.Secondary = ColorParse.ParseOr(s.SecondaryColor, Color32.White);
        foreach (var hex in s.RecentColors.AsEnumerable().Reverse())
            if (Color32.TryParseHex(hex, out var c)) _ctx.PushRecentColor(c);
        _colors.SetRecent(_ctx.RecentColors);
        var palette = s.Palette ?? [.. AppSettings.DefaultPalette];
        _colors.SetPalette(palette.Select(h => Color32.TryParseHex(h, out var c) ? c : (Color32?)null).OfType<Color32>());
        _browser.IncludeSubfolders = s.IncludeSubfolders;
        _preview.ZoomSetting = s.PreviewZoom;
        _preview.FpsSetting = s.PreviewFps;
        _preview.BackgroundSetting = s.PreviewBackground;
        _start.SetRecent(s.RecentFolders, s.RecentFiles);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkTitleBar(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var s = _settings;
        SetSplit(_splitFiles, s.LeftWidth);
        SetSplit(_splitPalette, s.PaletteWidth);
        SetSplit(_splitRight, _splitRight.Width - s.RightWidth - _splitRight.SplitterWidth);
        SetSplit(_splitTimeline, _splitTimeline.Height - s.BottomHeight - _splitTimeline.SplitterWidth);
        SetSplit(_splitRightDock, s.PreviewHeight);
        _browser.TreeHeight = s.BrowserTreeHeight;
        SetFilesVisible(s.FilesVisible);
        _settingsApplied = true;
        _start.LoadHub();
        var folder = _args.OpenFolder ?? s.LastFolder;
        if (folder is not null && Directory.Exists(folder)) SetRootFolder(folder);
        else if (_args.OpenFolder is not null) Ui.Error(this, $"폴더를 찾을 수 없습니다:\n{_args.OpenFolder}");
        if (_args.OpenFile is not null) OpenFile(_args.OpenFile);
        _canvas.Focus();
    }

    private static void SetSplit(SplitContainer sc, int distance)
    {
        int size = sc.Orientation == Orientation.Vertical ? sc.Width : sc.Height;
        int max = size - sc.Panel2MinSize - sc.SplitterWidth;
        if (max <= sc.Panel1MinSize) return;
        sc.SplitterDistance = Math.Clamp(distance, sc.Panel1MinSize, max);
    }

    private void SaveSettings()
    {
        var s = _settings;
        s.Maximized = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        (s.WindowX, s.WindowY, s.WindowWidth, s.WindowHeight) = (b.X, b.Y, b.Width, b.Height);
        if (_settingsApplied)
        {
            s.FilesVisible = !_splitFiles.Panel1Collapsed;
            if (s.FilesVisible) s.LeftWidth = _splitFiles.SplitterDistance;
            s.PaletteWidth = _splitPalette.SplitterDistance;
            s.RightWidth = _splitRight.Width - _splitRight.SplitterDistance - _splitRight.SplitterWidth;
            s.BottomHeight = _splitTimeline.Height - _splitTimeline.SplitterDistance - _splitTimeline.SplitterWidth;
            s.PreviewHeight = _splitRightDock.SplitterDistance;
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
        s.PixelPerfect = _ctx.PixelPerfect;
        s.FillContiguous = _ctx.FillContiguous;
        s.ShapeFilled = _ctx.ShapeFilled;
        s.RightButtonErases = _ctx.RightButtonErases;
        s.MoveWrap = _ctx.MoveWrap;
        s.PrimaryColor = _ctx.Primary.ToHex();
        s.SecondaryColor = _ctx.Secondary.ToHex();
        s.RecentColors = _ctx.RecentColors.Select(c => c.ToHex()).ToList();
        s.Palette = _colors.Palette.Select(c => c.ToHex()).ToList();
        s.IncludeSubfolders = _browser.IncludeSubfolders;
        s.LastFolder = _browser.RootFolder ?? s.LastFolder;
        s.PreviewZoom = _preview.ZoomSetting;
        s.PreviewFps = _preview.FpsSetting;
        s.PreviewBackground = _preview.BackgroundSetting;
        s.Save();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        _play?.Close();
        SaveSettings();
    }

    private void OpenSettingsFolder()
    {
        var dir = Path.GetDirectoryName(AppSettings.FilePath)!;
        SaveSettings();
        try { System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\""); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Ui.Error(this, ex.Message);
        }
    }

    // ───────────── 키 처리 ─────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_keys.TryGetValue(keyData, out var entry) && CanRun(entry.Scope) && entry.Action())
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool CanRun(KeyScope scope)
    {
        if (scope == KeyScope.Global) return true;
        var focus = NativeMethods.FocusedControl(out bool native);
        if (scope == KeyScope.Canvas) return focus == _canvas;
        bool textInput = native || focus is TextBoxBase || focus is ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList }
                         || focus is UpDownBase;
        return !textInput;
    }

    // ───────────── 화면 갱신 ─────────────

    private void UpdateAll()
    {
        bool has = _ctx.HasProject;
        _start.Visible = !has;
        if (!has) _start.SetRecent(_settings.RecentFolders, _settings.RecentFiles);
        _canvas.Visible = has;
        foreach (var item in _docItems) item.Enabled = has;
        bool atlas = _ctx.IsAtlas;
        foreach (var item in _animItems) item.Enabled = has && !atlas;
        foreach (var item in _atlasItems) item.Enabled = atlas;
        _miSpriteMenu.Visible = atlas;
        _cbOnion.Visible = !atlas;
        if (atlas) _canvas.PeekPrevious = false;
        _miPlayTest.Text = atlas ? "움직임 테스트 — 아틀라스 프로젝트는 쓸 수 없음(미리보기만)" : "움직임 테스트";
        _miPlayTest.ToolTipText = atlas ? AtlasPlayTestReason : null;
        _miPlayTest.Enabled = !atlas;
        UpdateTitle();
        UpdateStatus();
        UpdateUndoItems();
        UpdateToolUi();
    }

    private void UpdateTitle()
    {
        var p = _ctx.Project;
        Text = p is null
            ? "Gpixel"
            : $"{p.DisplayName}{(p.IsDirty ? " ●" : "")} — Gpixel" + (p.Path is { } path ? $"  ({Path.GetDirectoryName(path)})" : "");
    }

    private void UpdateStatus()
    {
        var p = _ctx.Project;
        _stSize.Text = p is null ? "" : $"{_ctx.CanvasWidth} × {_ctx.CanvasHeight}";
        _stFrame.Text = p is null ? ""
            : _ctx.CurrentAtlasSprite is { } s ? $"스프라이트 {_ctx.CurrentSprite + 1}/{p.Sprites.Count} {s.Name} · L{_ctx.CurrentLayer + 1}"
            : $"프레임 {_ctx.CurrentFrame + 1}/{p.Frames.Count} · L{_ctx.CurrentLayer + 1}";
        _stZoom.Text = $"{_canvas.Zoom * 100}%";
        _cbZoom.Text = $"{_canvas.Zoom * 100}%";
        if (p is null) _stDirty.Text = "";
        else
        {
            _stDirty.Text = p.IsDirty ? "● 수정됨" : "저장됨";
            _stDirty.ForeColor = p.IsDirty ? Theme.WarningColor : Theme.TextColor;
        }
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
        _miUndo.Enabled = h?.CanUndo == true;
        _miRedo.Enabled = h?.CanRedo == true;
        _miUndo.Text = h?.UndoLabel is { } u ? $"실행취소: {u}" : "실행취소";
        _miRedo.Text = h?.RedoLabel is { } r ? $"다시 실행: {r}" : "다시 실행";
    }

    private void UpdateToolUi()
    {
        var tool = _ctx.Tool;
        foreach (var (t, b) in _toolButtons) b.Checked = t == tool;
        foreach (var (t, mi) in _toolMenuItems) mi.Checked = t == tool;
        var def = ToolDefs.First(d => d.Tool == tool);
        _cbToolName.Text = $"{def.Name} ({def.Key})";
        _cbHint.Text = def.Hint;
        bool brush = tool is ToolKind.Pencil or ToolKind.Eraser or ToolKind.Line or ToolKind.Rectangle or ToolKind.Ellipse
            or ToolKind.Dodge or ToolKind.Dither or ToolKind.ReplaceBrush;
        bool paint = brush || tool == ToolKind.Fill;
        _cbBrushLabel.Visible = _cbBrush.Visible = brush;
        _cbPixelPerfect.Visible = tool is ToolKind.Pencil or ToolKind.Eraser;
        _cbFilled.Visible = tool is ToolKind.Rectangle or ToolKind.Ellipse;
        _cbGlobalFill.Visible = tool == ToolKind.Fill;
        _cbRightErase.Visible = tool is ToolKind.Pencil or ToolKind.Line or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Fill;
        _cbWrap.Visible = tool == ToolKind.Move;
        _cbSepMirror.Visible = _cbMirrorX.Visible = _cbMirrorY.Visible = paint;
        _cbBrush.SelectedIndex = _ctx.BrushSize - 1;
        _cbPixelPerfect.Checked = _miPixelPerfect.Checked = _ctx.PixelPerfect;
        _cbFilled.Checked = _miFilled.Checked = _ctx.ShapeFilled;
        _cbGlobalFill.Checked = _miGlobalFill.Checked = !_ctx.FillContiguous;
        _cbRightErase.Checked = _miRightErase.Checked = _ctx.RightButtonErases;
        _cbWrap.Checked = _miWrap.Checked = _ctx.MoveWrap;
        _cbMirrorX.Checked = _miMirrorX.Checked = _ctx.MirrorX;
        _cbMirrorY.Checked = _miMirrorY.Checked = _ctx.MirrorY;
        _cbGrid.Checked = _miGrid.Checked = _canvas.ShowPixelGrid;
        _cbTiles.Checked = _miTiles.Checked = _canvas.ShowTileGrid;
        _cbTiled.Checked = _miTiled.Checked = _canvas.TiledMode;
        _cbPivot.Checked = _miPivot.Checked = _canvas.ShowPivot;
        _cbOnion.Checked = _miOnion.Checked = _ctx.OnionSkin;
        _tbFiles.Checked = _miFiles.Checked = !_splitFiles.Panel1Collapsed;
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

    private void SetFilesVisible(bool visible)
    {
        _splitFiles.Panel1Collapsed = !visible;
        if (visible && _settingsApplied) SetSplit(_splitFiles, _settings.LeftWidth);
        UpdateToolUi();
    }

    private void GridSettings()
    {
        using var dlg = new GridSettingsDialog(_canvas.PixelGridColor.ToColor32(), _canvas.TileSize, _canvas.TileGridColor.ToColor32());
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _canvas.PixelGridColor = dlg.PixelGridColor.ToColor();
        _canvas.TileGridColor = dlg.TileGridColor.ToColor();
        _canvas.TileSize = dlg.TileSize;
        UpdateToolUi();
    }

    private void ShowStart()
    {
        if (!ConfirmDiscard()) return;
        _ctx.SetProject(null);
        _start.LoadHub();
    }

    private void ShowShortcuts()
    {
        using var dlg = new ShortcutHelpDialog();
        dlg.ShowDialog(this);
    }

    private const string AtlasPlayTestReason =
        "아틀라스 프로젝트는 부위별 스프라이트 묶음이라 애니메이션(태그)이 없어 움직임 테스트를 쓸 수 없습니다.\n"
        + "아틀라스는 오른쪽 위 미리보기로 고른 스프라이트를 확인하세요. 움직임 테스트는 애니메이션 프로젝트에서 태그(walk_down, idle, run …)로 합니다.";

    /// <summary>움직임 테스트: 프로젝트의 태그를 시트 문서로 만들어 넘긴다(편집하면 잠시 뒤 다시 읽음). 아틀라스는 이유를 알린다.</summary>
    private void ShowPlayTest()
    {
        if (_ctx.IsAtlas)
        {
            ShowMessage("움직임 테스트: 아틀라스 프로젝트는 쓸 수 없습니다(미리보기만).");
            Ui.Info(this, AtlasPlayTestReason, "움직임 테스트");
            return;
        }
        if (_play is { IsDisposed: false })
        {
            if (_play.WindowState == FormWindowState.Minimized) _play.WindowState = FormWindowState.Normal;
            _play.Activate();
            return;
        }
        _play = new PlayTestForm(() => _ctx.Project is { } p ? ProjectExport.BuildDocument(p, p.BaseName, allFrames: true) : null);
        _play.FormClosed += (_, _) => _play = null;
        _play.Show();
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
        var answer = MessageBox.Show(this, $"\"{p.DisplayName}\"의 변경 내용을 저장할까요?", "Gpixel",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
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
        if (_splitFiles.Panel1Collapsed) SetFilesVisible(true);
        _settings.LastFolder = folder;
        AppSettings.PushRecent(_settings.RecentFolders, folder);
        _start.SetRecent(_settings.RecentFolders, _settings.RecentFiles);
        var unityRoot = UnityProjectLocator.FindProjectRoot(folder);
        ShowMessage(unityRoot is null ? $"폴더: {folder}" : $"Unity 프로젝트 \"{Path.GetFileName(unityRoot)}\" — 저장하면 시트와 .meta를 함께 내보냅니다.");
    }

    private void OpenFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "편집할 그림이 있는 루트 폴더를 고르세요.",
            UseDescriptionForTitle = true,
            InitialDirectory = _browser.RootFolder ?? _settings.LastFolder ?? "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) SetRootFolder(UnityHubDialog.ResolveAssets(dlg.SelectedPath));
    }

    private void OpenUnityProject()
    {
        using var dlg = new UnityHubDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedFolder is { } folder) SetRootFolder(folder);
    }

    private void OpenFileDialogCmd()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Gpixel 프로젝트·PNG (*.gpixel;*.png)|*.gpixel;*.png|Gpixel 프로젝트 (*.gpixel)|*.gpixel|PNG 이미지 (*.png)|*.png",
            InitialDirectory = _browser.CurrentFolder ?? _settings.LastFolder ?? "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenFile(dlg.FileName);
    }

    /// <summary>.gpixel을 열거나 PNG를 가져온다(같은 이름 .gpixel이 있으면 그것). 취소·실패하면 false.</summary>
    private bool OpenFile(string path)
    {
        var full = Path.GetFullPath(path);
        var target = full.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(ProjectImport.GetProjectPathForPng(full))
            ? ProjectImport.GetProjectPathForPng(full)
            : full;
        if (_ctx.Project?.Path is { } cur && string.Equals(cur, target, StringComparison.OrdinalIgnoreCase)) return true;
        if (!File.Exists(full))
        {
            Ui.Error(this, $"파일을 찾을 수 없습니다:\n{full}");
            _settings.RecentFiles.Remove(path);
            return false;
        }
        if (!ConfirmDiscard()) return false;
        ProjectOpenResult result;
        try
        {
            var analysis = ProjectImport.Analyze(full, new SpriteSettings { PixelsPerUnit = _settings.LastPpu, Mode = SpriteMode.Multiple });
            ProjectKind? kind = null;
            if (analysis.NeedsKindChoice)
            {
                var size = analysis.Document!.Slices.Select(s => Rectangle.Intersect(s.Rect, analysis.Document.Image.Bounds).Size)
                    .FirstOrDefault(s => s.Width > 0 && s.Height > 0);
                kind = ImportKindDialog.Ask(this, Path.GetFileName(full), analysis.SliceCount, size);
                if (kind is null) return false;
            }
            result = ProjectImport.Import(analysis, kind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException
                                       or System.Text.Json.JsonException)
        {
            Ui.Error(this, $"열지 못했습니다:\n{full}\n\n{ex.Message}");
            return false;
        }
        _ctx.SetProject(result.Project);
        var p = result.Project;
        AppSettings.PushRecent(_settings.RecentFiles, result.Imported ? full : p.Path ?? full);
        _browser.SelectFile(p.Path is { } pp && File.Exists(pp) ? pp : full);
        var contents = p.IsAtlas
            ? $"아틀라스 · 스프라이트 {p.Sprites.Count}개"
            : $"프레임 {p.Frames.Count}개 · 태그 {p.Tags.Count}개";
        ShowMessage(result.Imported
            ? $"PNG 가져옴: {Path.GetFileName(full)} → {contents} — 저장하면 {Path.GetFileName(p.Path)}이(가) 만들어집니다."
            : $"열기: {p.DisplayName} (레이어 {p.Layers.Count} · {contents})");
        if (result.Warning is { } w) MessageBox.Show(this, w, "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return true;
    }

    private void NewProject(string? folder)
    {
        var defaults = _ctx.HasProject ? (_ctx.CanvasWidth, _ctx.CanvasHeight) : (32, 32);
        if (folder is null && !ConfirmDiscard()) return;
        using var dlg = new NewImageDialog(defaults.Item1, defaults.Item2, folder is null ? null : "new_sprite");
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
        var p = new SpriteProject(dlg.ImageWidth, dlg.ImageHeight, dlg.Kind);
        p.Edit("새 프로젝트", ProjectChange.Settings | ProjectChange.Pixels, () =>
        {
            p.Sprite.PixelsPerUnit = _settings.LastPpu;
            if (!dlg.Transparent && !p.IsAtlas)
            {
                var bg = new PixelImage(p.Width, p.Height);
                bg.Clear(_ctx.Secondary);
                p.SetCel(0, 0, bg);
            }
        });
        if (!dlg.Transparent && p.IsAtlas)
        {
            p.BeginSpriteStroke(0, 0).Clear(_ctx.Secondary);
            p.CommitCelStroke("배경");
        }
        p.History.Clear();
        _ctx.SetProject(p);
        if (path is null)
        {
            p.IsDirty = true;
            ShowMessage("새 프로젝트 — Ctrl+S로 저장할 위치를 고르세요.");
            return;
        }
        if (SaveTo(path)) _browser.RefreshFile(path);
        _browser.SelectFile(path);
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
        using var dlg = new SaveFileDialog
        {
            Filter = "Gpixel 프로젝트 (*.gpixel)|*.gpixel",
            FileName = p.Path is null ? "sprite.gpixel" : Path.GetFileName(p.Path),
            InitialDirectory = p.Path is null ? _browser.CurrentFolder ?? _settings.LastFolder ?? "" : Path.GetDirectoryName(p.Path)!,
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        return SaveTo(dlg.FileName);
    }

    private bool SaveTo(string path)
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
            Ui.Error(this, projectSaved
                ? $"프로젝트는 저장했지만 PNG 시트·.meta 내보내기에 실패했습니다(다른 프로그램이 파일을 쓰고 있는지 확인하세요).\n{path}\n\n{ex.Message}"
                : $"저장하지 못했습니다. 프로젝트는 수정된 상태로 남아 있습니다.\n{path}\n\n{ex.Message}", "저장 실패");
            return false;
        }
        _docStamp = SafeStamp(result.ProjectPath);
        AppSettings.PushRecent(_settings.RecentFiles, result.ProjectPath);
        ShowMessage($"저장: {Path.GetFileName(result.ProjectPath)}" + ExportSummary(result.Export));
        _browser.RefreshFile(result.ProjectPath);
        _browser.SelectFile(result.ProjectPath);
        _play?.DocumentChanged();
        UpdateTitle();
        UpdateStatus();
        return true;
    }

    private static string ExportSummary(ExportResult? e)
        => e is null ? " (내보내기 꺼짐)" : $" → {Path.GetFileName(e.PngPath)} 칸 {e.SpriteCount}개" + (e.MetaWritten ? " + .meta" : "");

    private void ExportNow()
    {
        var p = _ctx.Project!;
        if (p.Path is null && !SaveAs()) return;
        try
        {
            var e = ProjectExport.Export(p);
            ShowMessage("내보내기:" + ExportSummary(e).TrimStart(' ', '→'));
            _browser.RefreshFile(e.PngPath);
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
        if (MessageBox.Show(this, message, "파일 변경", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
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
        PixelImage? clip;
        if (_ctx.Selection.HasSelection) clip = _ctx.Selection.Copy(_ctx.EditImageOrEmpty);
        else
        {
            // 선택이 없으면 현재 프레임(아틀라스면 현재 스프라이트) 합성 그림 전체를 복사한다.
            clip = _ctx.IsAtlas ? _ctx.Project!.FlattenSprite(_ctx.CurrentSprite) : _ctx.Project!.Flatten(_ctx.CurrentFrame);
        }
        if (clip is null) return;
        _ctx.Clipboard = clip;
        PutOnSystemClipboard(clip);
        ShowMessage($"복사: {clip.Width}×{clip.Height}");
    }

    private void Cut()
    {
        if (!_ctx.Selection.HasSelection)
        {
            ShowMessage("잘라낼 선택 영역이 없습니다.");
            return;
        }
        if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 숨김/잠금 상태입니다."); return; }
        _ctx.BeginSelectionEdit();
        var clip = _ctx.Selection.Cut(_ctx.EditImage!)!;
        _ctx.NotifyPixels();
        _ctx.CommitFloating();
        _ctx.Clipboard = clip;
        PutOnSystemClipboard(clip);
        ShowMessage($"잘라내기: {clip.Width}×{clip.Height}");
    }

    private static void PutOnSystemClipboard(PixelImage clip)
    {
        try
        {
            var data = new DataObject();
            using var bmp = ImageUtil.ToBitmap(clip);
            data.SetImage(new Bitmap(bmp));
            var png = new MemoryStream();
            PngIO.Save(clip, png);
            data.SetData("PNG", false, png);
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (ExternalException)
        {
            // 다른 프로그램이 클립보드를 잡고 있으면 앱 내부 클립보드만 쓴다.
        }
    }

    private PixelImage? ReadClipboard()
    {
        try
        {
            if (Clipboard.GetDataObject()?.GetData("PNG") is MemoryStream png)
            {
                png.Position = 0;
                return PngIO.Load(png);
            }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
            {
                using (img) return ImageUtil.FromImage(img);
            }
        }
        catch (Exception ex) when (ex is ExternalException or InvalidDataException or ArgumentException)
        {
        }
        return _ctx.Clipboard;
    }

    private void Paste()
    {
        var clip = ReadClipboard();
        if (clip is null)
        {
            ShowMessage("붙여넣을 그림이 없습니다.");
            return;
        }
        if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 숨김/잠금 상태입니다."); return; }
        _ctx.Tool = ToolKind.Select;
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
        if (_ctx.Tool is not (ToolKind.Select or ToolKind.Move)) _ctx.Tool = ToolKind.Select;
        _ctx.CommitFloating();
        _ctx.Selection.SelectAll(_ctx.EditImageOrEmpty);
    }

    private void Deselect()
    {
        if (!_ctx.HasProject) return;
        _ctx.CommitFloating();
        _ctx.Selection.Deselect(_ctx.EditImageOrEmpty);
    }

    /// <summary>선택이 있으면 선택 내용, 없으면 현재 셀을 뒤집는다.</summary>
    private void Flip(bool horizontal)
    {
        if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 숨김/잠금 상태입니다."); return; }
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
            if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 숨김/잠금 상태입니다."); return; }
            _ctx.BeginSelectionEdit();
            _ctx.Selection.Rotate90(_ctx.EditImage!, clockwise);
            _ctx.NotifyPixels();
            ShowMessage("선택 회전 — Enter로 내려놓기");
            return;
        }
        if (_ctx.IsAtlas)
        {
            ShowMessage("아틀라스: 회전할 영역을 먼저 선택하세요(캔버스 전체 회전은 애니메이션 프로젝트만).");
            return;
        }
        _ctx.Structural(() => _ctx.Project!.RotateCanvas90(clockwise));
        _canvas.FitToWindow();
        ShowMessage("캔버스 회전(모든 프레임)");
    }

    /// <summary>선택 영역(없으면 현재 셀 전체)의 불투명 픽셀 둘레에 기본색으로 1픽셀 외곽선.</summary>
    private void AddOutline(bool diagonal)
    {
        if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 숨김/잠금 상태입니다."); return; }
        _ctx.CommitFloating();
        var region = _ctx.Selection.HasSelection ? _ctx.Selection.Bounds : new Rectangle(0, 0, _ctx.CanvasWidth, _ctx.CanvasHeight);
        _ctx.BeginStroke();
        int n = PixelOps.AddOutline(_ctx.EditImage!, region, _ctx.Primary, diagonal);
        _ctx.NotifyPixels();
        _ctx.CommitStroke("외곽선 추가");
        ShowMessage(n > 0 ? $"외곽선 {n}픽셀" : "외곽선을 그릴 곳이 없습니다(불투명 픽셀이 없음).");
    }

    private void ReplaceColor()
    {
        if (!_ctx.CanEdit) { ShowMessage("현재 레이어가 숨김/잠금 상태입니다."); return; }
        _ctx.CommitFloating();
        using var dlg = new ReplaceColorDialog(_ctx.Primary, _ctx.Secondary, _ctx.Selection.HasSelection);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Rectangle? clip = dlg.SelectionOnly ? _ctx.Selection.Bounds : null;
        _ctx.BeginStroke();
        int n = Raster.ReplaceColor(_ctx.EditImage!, dlg.From, dlg.To, clip);
        _ctx.NotifyPixels();
        _ctx.CommitStroke("색 바꾸기");
        ShowMessage($"색 바꾸기: {n}픽셀");
    }

    /// <summary>현재 프레임 그림(시트)을 격자/자동으로 나눠 칸마다 프레임 하나인 프로젝트로 바꾼다.</summary>
    private void SplitIntoFrames()
    {
        var p = _ctx.Project!;
        _ctx.CommitFloating();
        var image = p.Flatten(_ctx.CurrentFrame);
        using var dlg = new SplitFramesDialog(image, 16, 16);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var rects = dlg.Rects;
        if (p.Frames.Count > 1 && MessageBox.Show(this,
                $"지금 프레임 {p.Frames.Count}개·레이어·태그를 버리고, 현재 프레임 그림을 나눈 {rects.Count}개 프레임으로 바꿉니다. 실행취소할 수 없습니다.",
                "격자로 나누기", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        var doc = new Document(image);
        doc.Sprite.CopyFrom(p.Sprite);
        doc.Sprite.Mode = SpriteMode.Multiple;
        doc.Slices.AddRange(Gpixel.Core.Slicing.NamingRules.CreateSlices(rects,
            new Gpixel.Core.Slicing.NamingOptions { BaseName = p.BaseName }, p.Sprite.DefaultPivot));
        var split = ProjectImport.FromDocument(doc);
        split.Export.CopyFrom(p.Export);
        split.Path = p.Path;
        split.History.Clear();
        split.IsDirty = true;
        _ctx.SetProject(split);
        ShowMessage($"격자로 나눔: 프레임 {split.Frames.Count}개 ({split.Width}×{split.Height}) — 태그를 만들어 애니메이션을 정하세요.");
    }

    /// <summary>캔버스 크기(아틀라스면 현재 스프라이트 크기) 바꾸기.</summary>
    private void ResizeCanvas()
    {
        var p = _ctx.Project!;
        if (p.IsAtlas)
        {
            _timeline.ResizeSprite();
            ShowMessage($"스프라이트 크기: {_ctx.CanvasWidth}×{_ctx.CanvasHeight}");
            return;
        }
        using var dlg = new ResizeDialog(p.Width, p.Height);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.NewWidth == p.Width && dlg.NewHeight == p.Height) return;
        _ctx.Structural(() => p.ResizeCanvas(dlg.NewWidth, dlg.NewHeight, dlg.ImageAnchor));
        _canvas.FitToWindow();
        ShowMessage($"캔버스 크기: {p.Width}×{p.Height}");
    }
}
