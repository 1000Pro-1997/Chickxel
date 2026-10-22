using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Input.Platform;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App.Ai;

/// <summary>
/// AI 채팅 창(메인 창에 딸린 도구 창). 사용자 컴퓨터의 AI CLI(Claude Code 또는 Codex, <see cref="AiProvider"/>)와 그 로그인을 쓰고,
/// AI는 편집기 안 MCP 서버(<see cref="McpServer"/>)의 도구로 열린 그림을 고친다.
/// AI마다 탭(<see cref="ChatTab"/>)이 따로 있어 대화·세션·입력·모델이 섞이지 않고, 머리줄 전환 단추는 보이는 탭만 바꾼다
/// (가려진 탭의 차례는 계속 돌고, 도는 동안 탭 이름에 표시). 한 캔버스를 함께 고치지 않도록 차례는 모든 탭을 통틀어 하나만 돈다.
/// 탭을 처음 볼 때 CLI 찾기 → 로그인 → 채팅 순서로 화면이 바뀐다. AI의 한 차례(turn)는 실행취소 한 단계로 묶이고,
/// 차례마다 캔버스 모습과 "이 단계 되돌리기/다시 적용" 단추가 붙는다.
/// </summary>
public sealed class ChatWindow : Window
{
    private static readonly IBrush ErrorBrush = AppTheme.Brush(Color.FromRgb(240, 110, 110));
    private static readonly IBrush UserBubbleBrush = AppTheme.Brush(Color.FromRgb(50, 66, 88));
    private static readonly FontFamily MonoFont = new("Menlo, Consolas, DejaVu Sans Mono, monospace");
    private const int SnapshotMax = 240;
    private const int CanvasAttachSide = 512;
    /// <summary>첨부 이미지의 긴 변 최대(이보다 크면 줄여서 PNG로 보냄).</summary>
    private const int AttachMaxSide = 1568;

    private readonly EditorContext _ctx;
    private readonly McpServer _mcp;
    private readonly AppSettings _settings;

    private readonly TextBlock _status;
    private readonly TabStrip _providerToggle;
    /// <summary>보이는 탭의 모델 칸을 담는 자리.</summary>
    private readonly Border _modelHost;
    private readonly Button _newChat;
    private readonly Border _body;

    /// <summary>AI마다 탭 하나(<see cref="AiProvider.All"/> 순서).</summary>
    private readonly List<ChatTab> _tabs;
    private ChatTab _visible;
    private bool _shutdown, _ready, _opened;

    public ChatWindow(EditorContext ctx, McpServer mcp, AppSettings settings)
    {
        _ctx = ctx;
        _mcp = mcp;
        _settings = settings;

        ShowInTaskbar = false;
        MinWidth = 300;
        MinHeight = 360;
        Width = Math.Clamp(settings.ChatWidth, 300, 3000);
        Height = Math.Clamp(settings.ChatHeight, 360, 3000);
        // 기억한 위치가 지금 어느 화면에도 걸치지 않으면(모니터 분리 등) 메인 창 가운데로.
        var bounds = new PixelRect(settings.ChatX, settings.ChatY, (int)Width, (int)Height);
        if (settings.ChatX >= 0 && settings.ChatY >= 0 && Screens.All.Any(sc => sc.WorkingArea.Intersects(bounds)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(settings.ChatX, settings.ChatY);
        }
        else WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // ── 머리줄: 로그인 상태 · AI(Claude/Codex) 탭 · 모델 · 새 대화
        _status = new TextBlock
        {
            Foreground = AppTheme.DimTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0),
        };
        _tabs = [.. AiProvider.All.Select((p, i) => new ChatTab(this, p, i))];
        _visible = _tabs[AiProvider.All.ToList().IndexOf(AiProvider.FromId(settings.ChatProvider))];
        _providerToggle = new TabStrip(TabStripKind.Segmented)
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0),
        };
        // 글자 뒤 점 = 그 탭이 답을 쓰는 중(SetIconVisible로 켜고 끔)
        foreach (var p in AiProvider.All)
        {
            int i = _providerToggle.Add(p.Name, (g, r, b) => VectorIcons.Draw(g, VectorIcon.Dot, r, 8, b, filled: true),
                string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ChatWindow_02EFD3D4E7,p.Name,p.ProductName,p.AccountText), iconAfter: true);
            _providerToggle.SetIconVisible(i, false);
        }
        _providerToggle.SelectedIndex = _visible.Index;
        _providerToggle.SelectionChanged += (_, _) => OnProviderToggled();
        _modelHost = new Border { VerticalAlignment = VerticalAlignment.Center };
        _newChat = Ui.Button(UiStrings.S_ChatWindow_ChatWindow_ACBC4AE706, (_, _) => _visible.NewConversation());
        _newChat.Tip(UiStrings.S_ChatWindow_ChatWindow_8A59B9F2A0);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(4, 3) };
        header.Children.Add(_status);
        Grid.SetColumn(_providerToggle, 1);
        header.Children.Add(_providerToggle);
        Grid.SetColumn(_modelHost, 2);
        header.Children.Add(_modelHost);
        Grid.SetColumn(_newChat, 3);
        header.Children.Add(_newChat);
        var headerBorder = new Border
        {
            Background = AppTheme.BackBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = header,
        };
        DockPanel.SetDock(headerBorder, Dock.Top);

        _body = new Border { Background = AppTheme.PanelBrush };
        Content = new DockPanel { Children = { headerBorder, _body } };

        _ctx.UndoChanged += OnUndoChanged;
        _ctx.ProjectReplaced += OnProjectReplaced;
        PositionChanged += (_, _) => SaveBounds();
        Opened += (_, _) =>
        {
            _opened = true;
            _visible.Activate();
        };
        ShowTab(_visible);
        _ready = true;
    }

    /// <summary>AI 프로세스를 모두 끝낸다(앱을 닫을 때 메인 창이 부름). 진행 중인 차례는 멈춘다.</summary>
    public void Shutdown()
    {
        if (_shutdown) return;
        SaveBounds();
        foreach (var t in _tabs) t.StopTurn();
        _shutdown = true;
        foreach (var t in _tabs) t.Close();
        _ctx.UndoChanged -= OnUndoChanged;
        _ctx.ProjectReplaced -= OnProjectReplaced;
    }

    protected override void OnClosed(EventArgs e)
    {
        Shutdown();
        base.OnClosed(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty) SaveBounds();
    }

    // ───────────── 창 위치 ─────────────

    private void SaveBounds()
    {
        if (!_ready || _shutdown || WindowState != WindowState.Normal || !IsVisible) return;
        _settings.ChatX = Math.Max(0, Position.X);
        _settings.ChatY = Math.Max(0, Position.Y);
        if (ClientSize.Width > 0) _settings.ChatWidth = (int)Math.Round(ClientSize.Width);
        if (ClientSize.Height > 0) _settings.ChatHeight = (int)Math.Round(ClientSize.Height);
    }

    // ───────────── 탭 ─────────────

    /// <summary>Claude ↔ Codex 단추: 보이는 탭만 바꾼다(어느 탭의 세션·차례도 멈추지 않음).</summary>
    private void OnProviderToggled()
    {
        if (_providerToggle.SelectedIndex < 0) return;
        var tab = _tabs[_providerToggle.SelectedIndex];
        if (ReferenceEquals(tab, _visible)) return;
        ShowTab(tab);
    }

    /// <summary>탭을 보이게 한다: 몸통·모델 칸·머리줄 상태를 그 탭 것으로. 창이 열려 있으면 처음 볼 때 확인을 시작한다.</summary>
    private void ShowTab(ChatTab tab)
    {
        _visible = tab;
        _settings.ChatProvider = tab.Provider.Id;
        Title = tab.Provider.Name;
        _modelHost.Child = tab.Model;
        _body.Child = tab.Root;
        RefreshHeader();
        if (_opened) tab.Activate();
    }

    /// <summary>머리줄 상태 글·새 대화 단추를 보이는 탭에 맞춘다.</summary>
    private void RefreshHeader()
    {
        _status.Text = _visible.StatusText;
        _newChat.IsEnabled = _visible.InChat;
    }

    /// <summary>차례가 도는 탭(없으면 null). 차례는 모든 탭을 통틀어 하나만 돈다.</summary>
    private ChatTab? RunningTab => _tabs.FirstOrDefault(t => t.IsRunning);

    /// <summary>"지금 레이어만"(두 탭 공통 설정): 설정에 넣고 모든 탭의 단추를 맞춘다. 진행 중인 차례의 범위는 그대로(다음 메시지부터).</summary>
    private void SetCurrentLayerOnly(bool on)
    {
        if (_settings.AiCurrentLayerOnly == on) return;
        _settings.AiCurrentLayerOnly = on;
        foreach (var t in _tabs) t.SyncScopeToggle(on);
    }

    /// <summary>어느 탭의 차례가 시작·끝남: 탭 이름의 작업 중 표시와 모든 탭의 보내기·되돌리기 단추를 고친다.</summary>
    private void OnTurnsChanged()
    {
        foreach (var t in _tabs)
        {
            _providerToggle.SetIconVisible(t.Index, t.IsRunning);
            t.UpdateInputState();
            t.RefreshSteps();
        }
    }

    private void OnProjectReplaced(object? sender, EventArgs e)
    {
        foreach (var t in _tabs) t.OnProjectReplaced();
    }

    private void OnUndoChanged(object? sender, EventArgs e)
    {
        foreach (var t in _tabs) t.RefreshSteps();
    }

    private string? ProjectFolder()
        => _ctx.Project?.Path is { } file && Path.GetDirectoryName(file) is { Length: > 0 } dir ? dir : null;

    // ───────────── 메시지 그리기 도우미 ─────────────

    /// <summary>스트리밍 중이던 글을 마무리한다(코드 블록 꾸미기).</summary>
    private static void FinishStream(Turn t)
    {
        if (t.StreamHost is { } host)
        {
            var text = t.Stream.ToString();
            if (text.Length > 0) RenderText(host, text);
        }
        t.StreamHost = null;
        t.StreamText = null;
        t.Stream.Clear();
    }

    private static SelectableTextBlock AnswerText(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = AppTheme.TextBrush,
        Margin = new Thickness(2, 0),
    };

    /// <summary>답 글을 그린다. ``` 로 둘러싼 부분은 고정폭 글꼴 상자.</summary>
    private static void RenderText(StackPanel host, string text)
    {
        host.Children.Clear();
        var parts = text.Split("```");
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (i % 2 == 0)
            {
                part = part.Trim('\n', '\r');
                if (part.Trim().Length > 0) host.Children.Add(AnswerText(part));
                continue;
            }
            // 코드 블록 첫 줄의 언어 이름은 뗀다.
            int nl = part.IndexOf('\n');
            if (nl >= 0 && part[..nl].Trim() is var lang && lang.Length < 20 && !lang.Contains(' ')) part = part[(nl + 1)..];
            host.Children.Add(new Border
            {
                Background = AppTheme.InputBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4),
                Child = new SelectableTextBlock
                {
                    Text = part.TrimEnd('\n', '\r'),
                    FontFamily = MonoFont,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = AppTheme.TextBrush,
                },
            });
        }
    }

    /// <summary>도구 인자 짧은 요약(legend 같은 긴 것은 뺌).</summary>
    private static string Summarize(JsonObject? input)
    {
        if (input is null) return "";
        var parts = new List<string>();
        foreach (var (key, value) in input)
        {
            if (key == "legend" || value is null) continue;
            string text = value switch
            {
                JsonArray a when key == "rows" => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_Summarize_473D2F6697,a.Count),
                JsonArray a => $"[{a.Count}]",
                JsonObject => "{…}",
                _ => Shorten(value.ToString(), 18),
            };
            parts.Add($"{key}={text}");
        }
        return Shorten(string.Join(" ", parts), 80);
    }

    private static string Shorten(string s, int max)
    {
        s = s.ReplaceLineEndings(" ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    // ───────────── 이미지 도우미 ─────────────

    /// <summary>비트맵을 PNG로(긴 변이 너무 크면 줄임).</summary>
    private static byte[] ToPng(Bitmap bmp)
    {
        var size = bmp.PixelSize;
        int longSide = Math.Max(size.Width, size.Height);
        Bitmap src = bmp;
        if (longSide > AttachMaxSide)
        {
            double k = (double)AttachMaxSide / longSide;
            src = bmp.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(size.Width * k)), Math.Max(1, (int)(size.Height * k))),
                BitmapInterpolationMode.HighQuality);
        }
        using var ms = new MemoryStream();
        src.Save(ms);
        if (!ReferenceEquals(src, bmp)) src.Dispose();
        return ms.ToArray();
    }

    private static Control Thumb(Attachment a, int size)
    {
        var px = a.Thumb.PixelSize;
        double k = Math.Min((double)size / Math.Max(1, px.Width), (double)size / Math.Max(1, px.Height));
        if (a.PixelArt && k > 1) k = Math.Floor(k);
        var image = new Image { Source = a.Thumb, Width = Math.Max(1, px.Width * k), Height = Math.Max(1, px.Height * k), Stretch = Stretch.Fill };
        if (a.PixelArt) RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        return new CheckerBackground { Child = image, Margin = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
    }

    /// <summary>지금 프레임의 보이는 레이어 합성(그룹은 부위를 배치한 대로).</summary>
    private PixelImage? Composite(SpriteProject p)
    {
        return p.Frames.Count == 0 ? null : p.Flatten(Math.Clamp(_ctx.CurrentFrame, 0, p.Frames.Count - 1));
    }

    /// <summary>정수 배 최근접 확대.</summary>
    private static PixelImage ScaleUp(PixelImage src, int n)
    {
        var dst = new PixelImage(src.Width * n, src.Height * n);
        for (int y = 0; y < dst.Height; y++)
        {
            int row = (y / n) * src.Width, dstRow = y * dst.Width;
            for (int x = 0; x < dst.Width; x++) dst.Pixels[dstRow + x] = src.Pixels[row + x / n];
        }
        return dst;
    }

    // ───────────── 작은 UI 도우미 ─────────────

    private static TextBlock TitleText(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        FontSize = AppTheme.FontSize + 2,
        Foreground = AppTheme.TextBrush,
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock WrapText(string text, bool dim = false, bool error = false) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = error ? ErrorBrush : dim ? AppTheme.DimTextBrush : AppTheme.TextBrush,
    };

    /// <summary>복사 단추가 달린 명령 한 줄.</summary>
    private Control CommandBox(string title, string command)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new Border
        {
            Background = AppTheme.InputBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new SelectableTextBlock { Text = command, FontFamily = MonoFont, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.TextBrush },
        });
        var copy = Ui.Button(UiStrings.S_ChatWindow_CommandBox_05C2558491, (_, _) => CopyText(command));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        return Ui.Group(title, grid);
    }

    /// <summary>누르면 브라우저로 여는 주소 + 복사 단추.</summary>
    private Control LinkRow(string url)
    {
        var link = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
            Content = new TextBlock
            {
                Text = url,
                Foreground = AppTheme.AccentBrush,
                TextDecorations = TextDecorations.Underline,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        link.Click += (_, _) =>
        {
            try
            {
                AiCli.OpenUrl(url);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
            {
                Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_LinkRow_5865E61E9C,ex.Message));
            }
        };
        var copy = Ui.Button(UiStrings.S_ChatWindow_LinkRow_E4434318F0, (_, _) => CopyText(url));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(link);
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        return grid;
    }

    private void CopyText(string text)
    {
        if (Clipboard is { } clipboard) _ = clipboard.SetTextAsync(text);
    }

    // ───────────── 내부 형식 ─────────────

    /// <summary>보낼 첨부 이미지(PNG 바이트 + 미리보기).</summary>
    private sealed record Attachment(byte[] Png, Bitmap Thumb, bool PixelArt);

    /// <summary>끝난 차례 하나의 실행취소 묶음과 그 단추.</summary>
    private sealed record UndoStep(SpriteProject Project, IUndoCommand Command, Button Button);

    /// <summary>진행 중인 차례.</summary>
    private sealed class Turn(string label)
    {
        public string Label { get; } = label;
        /// <summary>실행취소 묶음을 연 프로젝트.</summary>
        public SpriteProject? Project { get; set; }
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public StackPanel? StreamHost { get; set; }
        public SelectableTextBlock? StreamText { get; set; }
        public StringBuilder Stream { get; } = new();
        public Dictionary<string, TextBlock> Tools { get; } = new();
    }

    /// <summary>투명한 곳이 보이게 체커 무늬 위에 자식을 그리는 틀.</summary>
    private sealed class CheckerBackground : Decorator
    {
        public override void Render(DrawingContext g) => AppTheme.DrawChecker(g, new Rect(Bounds.Size), 6);
    }

    /// <summary>
    /// AI 하나(Claude/Codex)의 탭: CLI 경로·로그인·세션·대화 글·입력란·첨부·모델 칸·실행취소 단계를 따로 가진다.
    /// 가려져 있어도 세션은 살아 있어, 도는 차례의 글은 계속 이 탭에 붙는다. 머리줄 상태는 보일 때만 창에 반영된다.
    /// </summary>
    private sealed class ChatTab
    {
        private readonly ChatWindow _w;

        private readonly DockPanel _chatPanel;
        private readonly ScrollViewer _scroll;
        private readonly StackPanel _messages;
        private readonly TextBlock _thinking;
        private readonly WrapPanel _chips;
        private readonly TextBox _input;
        private readonly Button _sendButton, _stopButton;
        private readonly ToggleButton _scopeToggle;

        /// <summary>"지금 레이어만" 단추를 설정 값에 맞춘다(다른 탭에서 바꿨을 때).</summary>
        public void SyncScopeToggle(bool on)
        {
            if (_scopeToggle.IsChecked != on) _scopeToggle.IsChecked = on;
        }

        private readonly List<Attachment> _attachments = new();
        private readonly List<UndoStep> _steps = new();
        private readonly DispatcherTimer _terminalPoll = new() { Interval = TimeSpan.FromSeconds(2) };
        private readonly DispatcherTimer _installPoll = new() { Interval = TimeSpan.FromSeconds(2) };
        /// <summary>차례 중 "생각 중…" 줄을 진행 상황(단계·받은 양·걸린 시간)으로 고쳐 쓴다.</summary>
        private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private DateTime _turnStart, _phaseStart;
        /// <summary>지금 단계: wait(응답 기다림) / thinking / tool_use(도구 호출 작성) / run(도구 실행) / text(답 쓰기).</summary>
        private string _phase = "wait";
        private string? _phaseTool;
        private int _phaseChars;

        private string? _cliPath;
        private IChatSession? _session;
        private CliLogin? _login;
        /// <summary>모델 목록 칸(<see cref="Model"/>의 항목 순서).</summary>
        private IReadOnlyList<ModelChoice> _modelChoices = [];
        private Turn? _turn;
        private TextBlock? _hint;
        /// <summary><see cref="_started"/>: 처음 보일 때 확인을 시작했음(다시 볼 때는 확인하지 않음).</summary>
        private bool _follow = true, _checking, _polling, _installChecking, _started, _fillingModels;

        public ChatTab(ChatWindow w, AiProvider provider, int index)
        {
            _w = w;
            Provider = provider;
            Index = index;

            Model = new ComboBox { Width = 190, Margin = new Thickness(3), VerticalAlignment = VerticalAlignment.Center };
            Model.SelectionChanged += (_, _) => OnModelChanged();
            FillModels();

            // ── 채팅 화면
            _messages = new StackPanel { Spacing = 6, Margin = new Thickness(10, 8) };
            _scroll = Ui.ScrollHost(_messages);
            _scroll.ScrollChanged += OnScrollChanged;
            _thinking = new TextBlock
            {
                Text = UiStrings.S_ChatWindow_ChatTab_8F1426E012,
                Foreground = AppTheme.DimTextBrush,
                FontStyle = FontStyle.Italic,
                Margin = new Thickness(2, 2),
                TextWrapping = TextWrapping.Wrap,
            };

            _chips = new WrapPanel { IsVisible = false, Margin = new Thickness(0, 0, 0, 4) };
            _input = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 54,
                MaxHeight = 160,
                Watermark = Ui.Shortcut(UiStrings.S_ChatWindow_ChatTab_95FEEA4ADC),
            };
            ScrollViewer.SetVerticalScrollBarVisibility(_input, ScrollBarVisibility.Auto);
            _input.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
            var imageButton = Ui.Button(UiStrings.S_ChatWindow_ChatTab_B428204BA3, (_, _) => AttachFile()).Tip(UiStrings.S_ChatWindow_ChatTab_046C36A571);
            var canvasButton = Ui.Button(UiStrings.S_ChatWindow_ChatTab_F9D4399092, (_, _) => AttachCanvas()).Tip(UiStrings.S_ChatWindow_ChatTab_B9C7852D0E);
            _scopeToggle = new ToggleButton
            {
                Content = UiStrings.S_ChatWindow_ChatTab_498E3C255E,
                IsChecked = _w._settings.AiCurrentLayerOnly,
                Margin = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
            }.Tip(UiStrings.S_ChatWindow_ChatTab_1B00266AAA);
            _scopeToggle.IsCheckedChanged += (_, _) => _w.SetCurrentLayerOnly(_scopeToggle.IsChecked == true);
            _sendButton = Ui.Button(UiStrings.S_ChatWindow_ChatTab_CF5843D1B0, (_, _) => Send()).Tip("Enter");
            _stopButton = Ui.Button(UiStrings.S_ChatWindow_ChatTab_1CB431966C, (_, _) => StopTurn());
            _stopButton.IsVisible = false;
            var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
            buttons.Children.Add(imageButton);
            Grid.SetColumn(canvasButton, 1);
            buttons.Children.Add(canvasButton);
            Grid.SetColumn(_scopeToggle, 2);
            buttons.Children.Add(_scopeToggle);
            Grid.SetColumn(_sendButton, 4);
            buttons.Children.Add(_sendButton);
            Grid.SetColumn(_stopButton, 5);
            buttons.Children.Add(_stopButton);
            var inputArea = new Border
            {
                Background = AppTheme.BackBrush,
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(8, 6),
                Child = new StackPanel { Children = { _chips, _input, buttons } },
            };
            DockPanel.SetDock(inputArea, Dock.Bottom);
            _chatPanel = new DockPanel { Children = { inputArea, _scroll } };

            _terminalPoll.Tick += async (_, _) => await PollLoginAsync();
            _installPoll.Tick += async (_, _) => await PollInstallAsync();
            _progressTimer.Tick += (_, _) => UpdateProgress();
            AddHint();
            SetHeader("", inChat: false);
        }

        public AiProvider Provider { get; }
        /// <summary>전환 단추에서의 번호(<see cref="AiProvider.All"/> 순서).</summary>
        public int Index { get; }
        /// <summary>탭 몸통: 확인 중 / 설치 안내 / 로그인 / 채팅 화면 중 하나를 담는다.</summary>
        public Border Root { get; } = new();
        /// <summary>이 AI의 모델 칸(보이는 동안 머리줄에 놓인다).</summary>
        public ComboBox Model { get; }
        /// <summary>머리줄 상태 글(로그인 계정·확인 중 등).</summary>
        public string StatusText { get; private set; } = "";
        /// <summary>채팅 화면인지(모델·새 대화를 쓸 수 있음).</summary>
        public bool InChat { get; private set; }
        public bool IsRunning => _turn is not null;

        private bool IsShown => ReferenceEquals(_w._visible, this);

        /// <summary>탭이 보이게 됨: 처음이면 확인(설치 → 로그인 → 채팅)을 시작하고, 채팅 중이면 입력란에 포커스.</summary>
        public void Activate()
        {
            if (!_started)
            {
                _started = true;
                Recheck();
            }
            else if (ReferenceEquals(Root.Child, _chatPanel)) FocusInput();
        }

        /// <summary>창을 닫을 때: 로그인·세션을 끝낸다(차례는 먼저 <see cref="StopTurn"/>).</summary>
        public void Close()
        {
            _installPoll.Stop();
            LeaveLogin();
            _session?.Dispose();
            _session = null;
        }

        private void FocusInput() => Dispatcher.UIThread.Post(() => _input.Focus(), DispatcherPriority.Background);

        // ───────────── 상태 확인(설치 → 로그인 → 채팅) ─────────────

        private void SetHeader(string text, bool inChat)
        {
            StatusText = text;
            InChat = inChat;
            Model.IsEnabled = inChat;
            if (IsShown) _w.RefreshHeader();
        }

        private void SetBody(Control content) => Root.Child = content;

        private async Task CheckAsync()
        {
            if (_checking || _w._shutdown) return;
            _checking = true;
            try
            {
                LeaveLogin();
                SetHeader(UiStrings.S_ChatWindow_CheckAsync_F75B2590E0, inChat: false);
                SetBody(new TextBlock
                {
                    Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_CheckAsync_CA460A15B1,Provider.ProductName),
                    Foreground = AppTheme.DimTextBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                var path = Provider.Locate(_w._settings);
                if (path is null)
                {
                    if (!_installPoll.IsEnabled) ShowNoCli();
                    return;
                }
                if (_cliPath != path)
                {
                    if (_turn is not null) StopTurn();
                    _session?.Dispose();
                    _session = null;
                }
                _cliPath = path;
                var status = await Provider.GetAuthStatusAsync(path);
                if (_w._shutdown) return;
                if (status.LoggedIn) ShowChat(status);
                else ShowLogin(status);
            }
            finally
            {
                _checking = false;
            }
        }

        private async void Recheck() => await CheckAsync();
        private async Task PollInstallAsync()
        {
            if (_installChecking || _checking || _w._shutdown) return;
            _installChecking = true;
            try
            {
                var path = Provider.Locate(_w._settings);
                if (path is null) return;
                if (_cliPath != path)
                {
                    await CheckAsync();
                    return;
                }
                var status = await Provider.GetAuthStatusAsync(path);
                if (!_w._shutdown && status.LoggedIn) ShowChat(status);
            }
            finally
            {
                _installChecking = false;
            }
        }

        private void StartInstall()
        {
            var steps = AiInstaller.Preview(Provider);
            if (Ui.Ask(_w, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_StartInstall_8776DA7BEF,Provider.ProductName,steps),
                    UiStrings.S_ChatWindow_StartInstall_BD9FDFD3C7, MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
            if (!AiInstaller.OpenTerminal(Provider, out var error))
            {
                Ui.Error(_w, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_StartInstall_4B456B142A,error));
                return;
            }
            _installPoll.Start();
            ShowNoCli();
        }


        /// <summary>CLI가 없을 때: 설치 안내.</summary>
        private void ShowNoCli()
        {
            var p = Provider;
            SetHeader(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowNoCli_C0F440A767,p.ProductName), inChat: false);
            var stack = new StackPanel { Spacing = 8, Margin = new Thickness(14, 12) };
            stack.Children.Add(TitleText(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowNoCli_EB50F0AD30,p.ProductName)));
            stack.Children.Add(WrapText(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowNoCli_A6A8288124,p.ProductName,p.Command,p.AccountText) +
                                        UiStrings.S_ChatWindow_ShowNoCli_4D48263369));
            var install = Ui.Button(UiStrings.S_ChatWindow_ShowNoCli_ED06427F94, (_, _) => StartInstall());
            install.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(install);
            if (_installPoll.IsEnabled) stack.Children.Add(WrapText(UiStrings.S_ChatWindow_ShowNoCli_37191A6276, dim: true));
            else stack.Children.Add(WrapText(UiStrings.S_ChatWindow_ShowNoCli_3EA0647605, dim: true));
            foreach (var (title, command) in p.InstallCommands) stack.Children.Add(_w.CommandBox(title, command));
            var recheck = Ui.Button(UiStrings.S_ChatWindow_ShowNoCli_2736EF27CC, (_, _) => Recheck());
            recheck.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(recheck);

            var overridePath = p.GetPathOverride(_w._settings);
            var pathBox = new TextBox { Text = overridePath ?? "", Watermark = p.PathExample, Margin = new Thickness(0, 2) };
            var browse = Ui.Button(UiStrings.S_ChatWindow_ShowNoCli_F47FA9147B, (_, _) =>
            {
                if (Ui.OpenFile(_w, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowNoCli_3F2EDB54CC,p.Command), null) is { } picked) pathBox.Text = picked;
            });
            var apply = Ui.Button(UiStrings.S_ChatWindow_ShowNoCli_306897216F, (_, _) =>
            {
                var text = (pathBox.Text ?? "").Trim();
                p.SetPathOverride(_w._settings, text.Length == 0 ? null : text);
                Recheck();
            });
            var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            pathRow.Children.Add(pathBox);
            Grid.SetColumn(browse, 1);
            pathRow.Children.Add(browse);
            Grid.SetColumn(apply, 2);
            pathRow.Children.Add(apply);
            var pathStack = new StackPanel { Spacing = 4 };
            pathStack.Children.Add(WrapText(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowNoCli_1CC476FE7A,p.Command,p.Command,p.Command), dim: true));
            pathStack.Children.Add(pathRow);
            if (!string.IsNullOrWhiteSpace(overridePath))
                pathStack.Children.Add(WrapText(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowNoCli_F8C688F131,p.Command,overridePath), error: true));
            stack.Children.Add(Ui.Group(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowNoCli_09620464D8,p.Command), pathStack));
            SetBody(Ui.ScrollHost(stack));
        }

        /// <summary>로그인 안 됨: 숨은 로그인(주소 표시)·터미널 로그인·다시 확인.</summary>
        private void ShowLogin(AuthStatus status)
        {
            var p = Provider;
            SetHeader(UiStrings.S_ChatWindow_ShowLogin_3B08918AF3, inChat: false);
            var stack = new StackPanel { Spacing = 8, Margin = new Thickness(14, 12) };
            stack.Children.Add(TitleText(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowLogin_9B4E400681,p.Name)));
            stack.Children.Add(WrapText(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowLogin_4B41926EA7,p.AccountText,p.Name) +
                                        string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowLogin_7211786FF1,p.ProductName)));
            if (status.Error is { } err) stack.Children.Add(WrapText(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowLogin_BDF84EA2D9,err), error: true));
            var info = new StackPanel { Spacing = 6 };
            var login = Ui.Button(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowLogin_86A2C2515E,p.Name), (_, _) => StartLogin(info));
            var terminal = Ui.Button(UiStrings.S_ChatWindow_ShowLogin_F93C9FEC46, (_, _) => StartTerminalLogin(info)).Tip(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ShowLogin_B35819C965,p.Command,string.Join(' ', p.LoginArgs)));
            var recheck = Ui.Button(UiStrings.S_ChatWindow_ShowLogin_E9BFEE873E, (_, _) => Recheck());
            stack.Children.Add(new WrapPanel { Children = { login, terminal, recheck } });
            stack.Children.Add(info);
            SetBody(Ui.ScrollHost(stack));
        }

        private void StartLogin(StackPanel info)
        {
            if (_cliPath is not { } path) return;
            LeaveLogin();
            info.Children.Clear();
            info.Children.Add(WrapText(UiStrings.S_ChatWindow_StartLogin_BDD2BF6309, dim: true));
            var urls = new StackPanel { Spacing = 4 };
            var output = WrapText("", dim: true);
            var codeBox = new TextBox { Watermark = UiStrings.S_ChatWindow_StartLogin_B79F003D82, Margin = new Thickness(0, 2) };
            var codeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            codeRow.Children.Add(codeBox);
            var takesCode = Provider.LoginTakesCode;
            var login = new CliLogin(Provider, path);
            var sendCode = Ui.Button(UiStrings.S_ChatWindow_StartLogin_B3F1AA5FE7, (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(codeBox.Text)) return;
                login.SendCode(codeBox.Text);
                codeBox.Text = "";
            });
            Grid.SetColumn(sendCode, 1);
            codeRow.Children.Add(sendCode);
            var codePanel = new StackPanel { Spacing = 2, IsVisible = false };
            codePanel.Children.Add(WrapText(UiStrings.S_ChatWindow_StartLogin_1F1673FEF2, dim: true));
            codePanel.Children.Add(codeRow);
            info.Children.AddRange([urls, codePanel, output]);

            login.UrlFound += url =>
            {
                urls.Children.Add(_w.LinkRow(url));
                codePanel.IsVisible = takesCode;
            };
            login.OutputLine += line => output.Text = line;
            login.Succeeded += st =>
            {
                if (!ReferenceEquals(_login, login)) return;
                _login = null;
                login.Dispose();
                ShowChat(st);
            };
            login.Failed += message =>
            {
                if (!ReferenceEquals(_login, login)) return;
                _login = null;
                login.Dispose();
                codePanel.IsVisible = false;
                info.Children.Add(WrapText(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_StartLogin_6D8D76D688,message), error: true));
            };
            try
            {
                login.Start();
                _login = login;
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                login.Dispose();
                info.Children.Add(WrapText(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_StartLogin_80640A2D7A,Provider.Command,ex.Message), error: true));
            }
        }

        private void StartTerminalLogin(StackPanel info)
        {
            if (_cliPath is not { } path) return;
            LeaveLogin();
            info.Children.Clear();
            var command = $"{Provider.Command} {string.Join(' ', Provider.LoginArgs)}";
            if (!AiCli.OpenLoginTerminal(path, Provider.LoginArgs, out var error))
            {
                info.Children.Add(WrapText(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_StartTerminalLogin_B0C0F4D01D,error,command), error: true));
                return;
            }
            info.Children.Add(WrapText(UiStrings.S_ChatWindow_StartTerminalLogin_067B1B838D, dim: true));
            _terminalPoll.Start();
        }

        /// <summary>터미널 로그인 중: 2초마다 로그인 상태를 본다.</summary>
        private async Task PollLoginAsync()
        {
            if (_polling || _cliPath is not { } path) return;
            _polling = true;
            try
            {
                var status = await Provider.GetAuthStatusAsync(path);
                if (_terminalPoll.IsEnabled && status.LoggedIn && !_w._shutdown)
                {
                    _terminalPoll.Stop();
                    ShowChat(status);
                }
            }
            finally
            {
                _polling = false;
            }
        }

        /// <summary>로그인 화면을 떠남: 숨은 로그인 프로세스·상태 확인 타이머를 끝낸다.</summary>
        private void LeaveLogin()
        {
            _terminalPoll.Stop();
            _login?.Dispose();
            _login = null;
        }

        private void ShowChat(AuthStatus status)
        {
            _installPoll.Stop();
            LeaveLogin();
            if (_cliPath is not { } path) return;
            var who = status.Describe();
            SetHeader(who.Length > 0 ? who : UiStrings.S_ChatWindow_ShowChat_B46AD2AE0A, inChat: true);
            if (_session is null)
            {
                _session = Provider.CreateSession(path, _w._mcp);
                _session.Model = CurrentModel();
                HookSession(_session);
            }
            SetBody(_chatPanel);
            UpdateInputState();
            if (IsShown) FocusInput();
        }

        // ───────────── 세션 이벤트(가려져 있어도 이 탭에 쓴다) ─────────────

        private void HookSession(IChatSession s)
        {
            s.TextStarted += () =>
            {
                if (_turn is { } t) BeginText(t);
            };
            s.TextDelta += delta =>
            {
                if (_turn is not { } t) return;
                if (t.StreamText is null) BeginText(t);
                t.Stream.Append(delta);
                t.StreamText!.Text = t.Stream.ToString();
            };
            s.AssistantText += text =>
            {
                if (_turn is not { } t || text.Length == 0) return;
                if (t.StreamHost is { } host)
                {
                    RenderText(host, text);
                    t.StreamHost = null;
                    t.StreamText = null;
                    t.Stream.Clear();
                }
                else
                {
                    var block = new StackPanel { Spacing = 4 };
                    RenderText(block, text);
                    AddItem(block);
                }
            };
            s.ToolUse += info =>
            {
                if (_turn is not { } t) return;
                SetPhase("run", info.Name);
                FinishStream(t);
                var line = new TextBlock
                {
                    Text = $"• {info.Name}" + (Summarize(info.Input) is { Length: > 0 } sum ? $" · {sum}" : ""),
                    Foreground = AppTheme.DimTextBrush,
                    FontSize = AppTheme.SmallFontSize,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 0),
                };
                if (info.Id.Length > 0) t.Tools[info.Id] = line;
                AddItem(line);
            };
            s.BlockStarted += (kind, name) => { if (_turn is not null) SetPhase(kind, name); };
            s.BlockProgress += chars => _phaseChars += chars;
            s.ToolResult += info => { if (_turn is not null && info.ToolUseId.Length > 0) SetPhase("wait", null); };
            s.ToolResult += info =>
            {
                if (_turn is not { } t || !info.IsError || !t.Tools.TryGetValue(info.ToolUseId, out var line)) return;
                line.Foreground = ErrorBrush;
                if (info.Text.Length > 0) line.Text += $" — {info.Text}";
            };
            s.TurnCompleted += r =>
            {
                string? error = r.IsError ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_HookSession_E1F3DD4697,(string.IsNullOrWhiteSpace(r.Text) ? r.Subtype ?? UiStrings.S_ChatWindow_HookSession_365CF8E859 : r.Text)) : null;
                if (_turn is not null) EndTurn(error, r, cancelled: false);
                else if (error is not null) AddSystemLine(error, error: true);
                // 로그인이 풀렸으면 로그인 화면으로(대화 내용은 남아 있다가 다시 로그인하면 보인다).
                if (r.NeedsLogin) Recheck();
            };
            s.Exited += info =>
            {
                var msg = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_HookSession_5674519D81,Provider.Name,info.ExitCode);
                if (info.StderrTail.Length > 0) msg += "\n" + info.StderrTail;
                if (_turn is not null) EndTurn(msg, null, cancelled: false);
                else AddSystemLine(msg, error: true);
            };
            s.Notice += text => AddSystemLine(text, error: false);
            s.McpProblem += text => AddSystemLine(text, error: true);
            s.ModelResolved += model =>
            {
                if (ReferenceEquals(s, _session) && Provider.RememberResolved(_w._settings, s.Model, model)) FillModels();
            };
        }

        // ───────────── 보내기·차례 ─────────────

        private string? CurrentModel()
            => _modelChoices.Count == 0 ? null : _modelChoices[Math.Clamp(Model.SelectedIndex, 0, _modelChoices.Count - 1)].Value;

        /// <summary>모델 목록을 이 AI의 칸으로 채우고 설정에 저장된 모델을 고른다(이름에 실제 버전이 붙으면 다시 부름).</summary>
        private void FillModels()
        {
            _fillingModels = true;
            try
            {
                _modelChoices = Provider.Models(_w._settings);
                Model.Items.Clear();
                foreach (var c in _modelChoices)
                {
                    var item = new ComboBoxItem { Content = c.Label };
                    if (c.Tip is { Length: > 0 } tip) item.Tip(tip);
                    Model.Items.Add(item);
                }
                var saved = Provider.GetModel(_w._settings);
                Model.SelectedIndex = Math.Max(0, _modelChoices.ToList().FindIndex(c => c.Value == saved));
                Model.Tip(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_FillModels_B806D71CEE,Provider.Name,Provider.ProductName));
            }
            finally
            {
                _fillingModels = false;
            }
        }

        private void OnModelChanged()
        {
            if (_fillingModels || Model.SelectedIndex < 0) return;
            var model = CurrentModel();
            if (Provider.GetModel(_w._settings) == model) return;
            Provider.SetModel(_w._settings, model);
            if (_session is null) return;
            if (_turn is not null) StopTurn();
            _session.Model = model;
            _session.Restart();
            AddSystemLine(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_OnModelChanged_C640BCC05A,_modelChoices[Model.SelectedIndex].Label), error: false);
        }

        public void NewConversation()
        {
            if (_turn is not null) StopTurn();
            _session?.Reset();
            _messages.Children.Clear();
            _steps.Clear();
            AddHint();
            _input.Focus();
        }

        private void Send()
        {
            if (_session is not { } session || _turn is not null || _w._shutdown) return;
            var text = (_input.Text ?? "").Trim();
            if (text.Length == 0 && _attachments.Count == 0) return;
            // 실행취소 묶음은 하나만 열 수 있으므로 다른 탭의 차례가 끝날 때까지 보내지 않는다.
            if (_w.RunningTab is { } other)
            {
                AddSystemLine(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_Send_2A1D03CC20,other.Provider.Name), error: false);
                return;
            }
            var attachments = _attachments.ToList();
            session.WorkingDirectory = _w.ProjectFolder();
            session.Model = CurrentModel();
            // "지금 레이어만": 보내는 순간의 문서·현재 레이어로 편집 범위를 정하고(도구가 막음), AI에게도 알린다.
            var ctx = _w._ctx;
            var scope = _w._settings.AiCurrentLayerOnly && ctx.Project is { } sp && ctx.CurrentLayer >= 0 && ctx.CurrentLayer < sp.Layers.Count
                ? new EditScope(sp, sp.Layers[ctx.CurrentLayer])
                : null;
            string scopeText = scope is null ? "" : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_Send_F50CA3E886,scope.Layer.Name,(scope.Layer.IsGroup ? UiStrings.S_ChatWindow_Send_D27CE54072 : ""));
            _w._mcp.Tools.Scope = scope;
            try
            {
                session.Send(scope is null ? text : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_Send_7CDB2DE090,scopeText,text),
                    attachments.Select(a => a.Png).ToList());
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _w._mcp.Tools.Scope = null;
                AddSystemLine(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_Send_E1ACACFBF7,Provider.Command,ex.Message), error: true);
                return;
            }
            _input.Text = "";
            ClearAttachments();
            RemoveHint();
            _follow = true;
            AddUserBubble(text, attachments);
            if (scope is not null) AddSystemLine(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_Send_7C61278ECA,scopeText), error: false);

            var label = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_Send_9DB69B8394,Provider.Name,Shorten(text.Length > 0 ? text : UiStrings.S_ChatWindow_Send_A2D9AB8F67, 24));
            var turn = new Turn(label);
            if (_w._ctx.Project is { } p)
            {
                // 사용자가 하던 떠 있는 선택 편집은 이 차례에 섞이지 않게 먼저 확정한다.
                _w._ctx.CommitFloating();
                p.History.BeginGroup(label);
                turn.Project = p;
            }
            _turn = turn;
            _messages.Children.Add(_thinking);
            _turnStart = DateTime.UtcNow;
            SetPhase("wait", null);
            _progressTimer.Start();
            _w.OnTurnsChanged();
        }

        public void StopTurn()
        {
            if (_turn is null) return;
            _session?.Cancel();
            EndTurn(null, null, cancelled: true);
        }

        /// <summary>
        /// 차례를 마무리한다: 실행취소 묶음을 닫고, 바뀐 것이 있으면 캔버스 모습과 되돌리기 단추, 끝에 비용·시간.
        /// </summary>
        private void EndTurn(string? error, TurnResult? result, bool cancelled)
        {
            if (_turn is not { } turn) return;
            _turn = null;
            _w._mcp.Tools.Scope = null;
            FinishStream(turn);
            _messages.Children.Remove(_thinking);
            _progressTimer.Stop();

            IUndoCommand? group = null;
            SpriteProject? project = turn.Project;
            if (project is not null && ReferenceEquals(project, _w._ctx.Project)) group = project.History.EndGroup();

            if (error is not null) AddSystemLine(error, error: true);
            if (group is not null && project is not null) AddSnapshot(project, group);

            var parts = new List<string>();
            if (cancelled) parts.Add(UiStrings.S_ChatWindow_EndTurn_D19A601695);
            if (result?.CostUsd is { } cost && cost > 0) parts.Add("$" + cost.ToString("0.000", CultureInfo.InvariantCulture));
            double ms = result?.DurationMs ?? turn.Clock.Elapsed.TotalMilliseconds;
            parts.Add((ms / 1000).ToString("0.0", CultureInfo.InvariantCulture) + UiStrings.S_ChatWindow_EndTurn_7EEAC40074);
            AddItem(new TextBlock
            {
                Text = string.Join(" · ", parts),
                Foreground = AppTheme.DimTextBrush,
                FontSize = AppTheme.SmallFontSize - 1,
                Margin = new Thickness(2, 0, 0, 6),
            });
            _w.OnTurnsChanged();
        }

        /// <summary>차례 중에 다른 프로젝트가 열리면(new_project 등) 묶음을 새 프로젝트로 옮긴다.</summary>
        public void OnProjectReplaced()
        {
            if (_turn is { } turn)
            {
                turn.Project?.History.EndGroup();
                turn.Project = null;
                if (_w._ctx.Project is { } p)
                {
                    p.History.BeginGroup(turn.Label);
                    turn.Project = p;
                }
            }
            RefreshSteps();
        }

        /// <summary>보내기/정지 단추. 다른 탭의 차례가 돌면 보내기 도움말로 알린다.</summary>
        public void UpdateInputState()
        {
            bool running = _turn is not null;
            _sendButton.IsVisible = !running;
            _stopButton.IsVisible = running;
            var other = _w.RunningTab;
            _sendButton.Tip(other is null || ReferenceEquals(other, this)
                ? "Enter"
                : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_UpdateInputState_25BD68A0C8,other.Provider.Name));
        }

        private void OnInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0) return; // 줄바꿈(TextBox가 처리)
                e.Handled = true;
                // 한글 조합 중이던 글자가 먼저 들어가도록 한 박자 늦게 보낸다.
                Dispatcher.UIThread.Post(Send, DispatcherPriority.Background);
            }
            else if (e.Key == Key.V && Ui.HasCommand(e.KeyModifiers))
            {
                _ = PasteImageAsync();
            }
        }

        // ───────────── 메시지 목록 ─────────────

        /// <summary>진행 단계를 바꾼다(받은 양·단계 시간은 0부터).</summary>
        private void SetPhase(string phase, string? tool)
        {
            _phase = phase;
            _phaseTool = tool;
            _phaseChars = 0;
            _phaseStart = DateTime.UtcNow;
            UpdateProgress();
        }

        /// <summary>
        /// "생각 중…" 줄: 지금 하는 일(응답 기다림·생각·도구 호출 작성(받은 양)·도구 실행·답 쓰기) + 그 단계 시간 + 전체 시간.
        /// 큰 그림을 그리는 도구 호출은 작성에 오래 걸리므로 받은 양이 늘어나는 것으로 살아 있음을 보인다.
        /// </summary>
        private void UpdateProgress()
        {
            if (_turn is null) return;
            var now = DateTime.UtcNow;
            static string Time(TimeSpan t) => t.TotalMinutes >= 1 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_UpdateProgress_A70F7D76B8,(int)t.TotalMinutes,t.Seconds) : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_UpdateProgress_ACBE1DF02D,t.Seconds);
            string Size(int chars) => chars >= 1024 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_UpdateProgress_60CB2685BB,chars / 1024.0) : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_UpdateProgress_97F1F78A01,chars);
            string what = _phase switch
            {
                "thinking" => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_UpdateProgress_B750FB67DF,Size(_phaseChars)),
                "tool_use" => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_UpdateProgress_4C23FA2A5B,_phaseTool ?? "?",Size(_phaseChars)),
                "run" => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_UpdateProgress_07B7C8BA45,_phaseTool ?? "?"),
                "text" => UiStrings.S_ChatWindow_UpdateProgress_5CDDDE0C69,
                _ => UiStrings.S_ChatWindow_UpdateProgress_46A1800CB1,
            };
            _thinking.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_UpdateProgress_A4C73F4DE5,what,Time(now - _phaseStart),Time(now - _turnStart));
        }

        /// <summary>메시지 목록 끝("생각 중…" 앞)에 붙인다.</summary>
        private void AddItem(Control c)
        {
            int idx = _messages.Children.IndexOf(_thinking);
            if (idx >= 0) _messages.Children.Insert(idx, c);
            else _messages.Children.Add(c);
        }

        private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
            {
                if (_follow) Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);
            }
            else if (e.OffsetDelta.Y != 0)
            {
                _follow = _scroll.Offset.Y + _scroll.Viewport.Height >= _scroll.Extent.Height - 24;
            }
        }

        private void AddHint()
        {
            _hint = WrapText(UiStrings.S_ChatWindow_AddHint_99795D658D, dim: true);
            _hint.Margin = new Thickness(2, 6);
            _messages.Children.Add(_hint);
        }

        private void RemoveHint()
        {
            if (_hint is null) return;
            _messages.Children.Remove(_hint);
            _hint = null;
        }

        private void AddUserBubble(string text, IReadOnlyList<Attachment> attachments)
        {
            var stack = new StackPanel { Spacing = 4 };
            if (attachments.Count > 0)
            {
                var thumbs = new WrapPanel();
                foreach (var a in attachments) thumbs.Children.Add(Thumb(a, 72));
                stack.Children.Add(thumbs);
            }
            if (text.Length > 0)
            {
                stack.Children.Add(new SelectableTextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = AppTheme.TextBrush,
                });
            }
            AddItem(new Border
            {
                Background = UserBubbleBrush,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6),
                Margin = new Thickness(40, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = stack,
            });
        }

        private void AddSystemLine(string text, bool error)
        {
            RemoveHint();
            var block = new SelectableTextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = error ? ErrorBrush : AppTheme.DimTextBrush,
                FontSize = AppTheme.SmallFontSize,
                Margin = new Thickness(2, 0),
            };
            AddItem(block);
        }

        /// <summary>스트리밍 답 글 블록을 새로 연다(앞 블록은 그대로 둠).</summary>
        private void BeginText(Turn t)
        {
            FinishStream(t);
            var host = new StackPanel { Spacing = 4 };
            var text = AnswerText("");
            host.Children.Add(text);
            t.StreamHost = host;
            t.StreamText = text;
            AddItem(host);
        }

        // ───────────── 실행취소 단계 ─────────────

        /// <summary>차례가 끝난 뒤 캔버스 모습과 "이 단계 되돌리기/다시 적용" 단추.</summary>
        private void AddSnapshot(SpriteProject project, IUndoCommand group)
        {
            var panel = new StackPanel { Spacing = 4, Margin = new Thickness(2, 2, 0, 0) };
            if (_w.Composite(project) is { } img)
            {
                double scale = Math.Max(img.Width, img.Height) <= SnapshotMax
                    ? Math.Max(1, SnapshotMax / Math.Max(img.Width, img.Height))
                    : (double)SnapshotMax / Math.Max(img.Width, img.Height);
                var image = new Image
                {
                    Source = ImageUtil.ToBitmap(img),
                    Width = img.Width * scale,
                    Height = img.Height * scale,
                    Stretch = Stretch.Fill,
                };
                RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
                panel.Children.Add(new Border
                {
                    BorderBrush = AppTheme.BorderBrush,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new CheckerBackground { Child = image },
                });
            }
            var button = Ui.Button(UiStrings.S_ChatWindow_AddSnapshot_C5F0084B6D);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            var step = new UndoStep(project, group, button);
            button.Click += (_, _) => ToggleStep(step);
            _steps.Add(step);
            panel.Children.Add(button);
            AddItem(panel);
            RefreshStep(step);
        }

        private void ToggleStep(UndoStep step)
        {
            // 어느 탭이든 차례가 돌면 실행취소 묶음이 열려 있으므로 건드리지 않는다.
            if (_w.RunningTab is not null || !ReferenceEquals(_w._ctx.Project, step.Project)) return;
            var history = step.Project.History;
            int depth = history.UndoDepthOf(step.Command);
            if (depth == 1) _w._ctx.UndoStep();
            else if (depth > 1)
            {
                if (Ui.Ask(_w, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_ToggleStep_3758F11139,depth - 1), UiStrings.S_ChatWindow_ToggleStep_579568A52F, MessageButtons.OKCancel, MessageIcon.Question)
                    != DialogResult.OK) return;
                for (int i = 0; i < depth; i++)
                {
                    if (!_w._ctx.UndoStep()) break;
                }
            }
            else if (history.RedoDepthOf(step.Command) == 1) _w._ctx.RedoStep();
            RefreshSteps();
        }

        public void RefreshSteps()
        {
            foreach (var s in _steps) RefreshStep(s);
        }

        private void RefreshStep(UndoStep step)
        {
            bool same = ReferenceEquals(_w._ctx.Project, step.Project);
            bool idle = _w.RunningTab is null;
            var history = step.Project.History;
            int undo = same ? history.UndoDepthOf(step.Command) : -1;
            int redo = same ? history.RedoDepthOf(step.Command) : -1;
            var b = step.Button;
            if (undo > 0)
            {
                b.Content = UiStrings.S_ChatWindow_RefreshStep_B7986869B0;
                b.IsEnabled = idle;
                b.Tip(undo > 1 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_RefreshStep_8F1EF61EAD,undo - 1) : null);
            }
            else if (redo > 0)
            {
                b.Content = UiStrings.S_ChatWindow_RefreshStep_E659C76CB0;
                b.IsEnabled = idle && redo == 1;
                b.Tip(redo > 1 ? UiStrings.S_ChatWindow_RefreshStep_B9AA5F8B5A : null);
            }
            else
            {
                b.Content = UiStrings.S_ChatWindow_RefreshStep_B7986869B0;
                b.IsEnabled = false;
                b.Tip(same ? UiStrings.S_ChatWindow_RefreshStep_1CA90CA5FE : UiStrings.S_ChatWindow_RefreshStep_842B2F53BC);
            }
        }

        // ───────────── 첨부 ─────────────

        private void AttachFile()
        {
            var path = Ui.OpenFile(_w, UiStrings.S_ChatWindow_AttachFile_A024695555, null, new FileFilter(UiStrings.S_ChatWindow_AttachFile_1565F6E888, "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"));
            if (path is null) return;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Ui.Error(_w, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ChatWindow_AttachFile_FB523C7B58,ex.Message));
                return;
            }
            Bitmap bmp;
            try
            {
                using var ms = new MemoryStream(bytes);
                bmp = new Bitmap(ms);
            }
            catch (Exception)
            {
                // 디코더가 던지는 예외 형식은 플랫폼마다 다르다.
                Ui.Error(_w, UiStrings.S_ChatWindow_AttachFile_0469DC1AFF);
                return;
            }
            bool isPng = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';
            bool small = Math.Max(bmp.PixelSize.Width, bmp.PixelSize.Height) <= AttachMaxSide;
            AddAttachment(isPng && small ? bytes : ToPng(bmp), bmp, pixelArt: false);
        }

        private void AttachCanvas()
        {
            if (_w._ctx.Project is not { } p || _w.Composite(p) is not { } img)
            {
                Ui.Warn(_w, UiStrings.S_ChatWindow_AttachCanvas_F7C294FC0C);
                return;
            }
            int n = Math.Max(1, CanvasAttachSide / Math.Max(img.Width, img.Height));
            using var ms = new MemoryStream();
            PngIO.Save(n > 1 ? ScaleUp(img, n) : img, ms);
            AddAttachment(ms.ToArray(), ImageUtil.ToBitmap(img), pixelArt: true);
        }

        /// <summary>⌘/Ctrl+V: 클립보드에 그림이 있으면 첨부(글자 붙여넣기는 입력란이 그대로 함).</summary>
        private async Task PasteImageAsync()
        {
            if (_w.Clipboard is not { } clipboard) return;
            Bitmap? bmp;
            try
            {
                bmp = await clipboard.TryGetBitmapAsync();
            }
            catch (Exception)
            {
                // 플랫폼 클립보드가 그림 형식을 못 주면 무시(글자 붙여넣기만).
                return;
            }
            if (bmp is null || _w._shutdown) return;
            AddAttachment(ToPng(bmp), bmp, pixelArt: false);
        }

        private void AddAttachment(byte[] png, Bitmap thumb, bool pixelArt)
        {
            var a = new Attachment(png, thumb, pixelArt);
            _attachments.Add(a);
            var remove = new Button
            {
                Content = new IconView(VectorIcon.X) { Width = 10, Height = 10, Margin = new Thickness(0, 2) },
                Padding = new Thickness(3, 0),
                MinHeight = 0,
                FontSize = AppTheme.SmallFontSize,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
            }.Tip(UiStrings.S_ChatWindow_AddAttachment_1F581E4392);
            var chip = new Border
            {
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 4, 4),
                Child = new Grid { Children = { Thumb(a, 48), remove } },
            };
            remove.Click += (_, _) =>
            {
                _attachments.Remove(a);
                _chips.Children.Remove(chip);
                _chips.IsVisible = _attachments.Count > 0;
            };
            _chips.Children.Add(chip);
            _chips.IsVisible = true;
        }

        private void ClearAttachments()
        {
            _attachments.Clear();
            _chips.Children.Clear();
            _chips.IsVisible = false;
        }
    }
}
