using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Input.Platform;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App.Ai;

/// <summary>
/// Claude 채팅 창(메인 창에 딸린 도구 창). 사용자 컴퓨터의 Claude Code CLI와 그 로그인을 쓰고,
/// Claude는 편집기 안 MCP 서버(<see cref="McpServer"/>)의 도구로 열린 그림을 고친다.
/// 처음에는 CLI 찾기 → 로그인 → 채팅 순서로 화면이 바뀐다. Claude의 한 차례(turn)는 실행취소 한 단계로 묶이고,
/// 차례마다 캔버스 모습과 "이 단계 되돌리기/다시 적용" 단추가 붙는다.
/// </summary>
public sealed class ChatWindow : Window
{
    private static readonly IBrush ErrorBrush = AppTheme.Brush(Color.FromRgb(240, 110, 110));
    private static readonly IBrush UserBubbleBrush = AppTheme.Brush(Color.FromRgb(50, 66, 88));
    private static readonly FontFamily MonoFont = new("Menlo, Consolas, DejaVu Sans Mono, monospace");
    private static readonly string?[] ModelValues = [null, "sonnet", "opus", "haiku"];
    private static readonly string[] ModelLabels = ["기본", "sonnet", "opus", "haiku"];
    private const int SnapshotMax = 240;
    private const int CanvasAttachSide = 512;
    /// <summary>첨부 이미지의 긴 변 최대(이보다 크면 줄여서 PNG로 보냄).</summary>
    private const int AttachMaxSide = 1568;

    private readonly EditorContext _ctx;
    private readonly McpServer _mcp;
    private readonly AppSettings _settings;

    private readonly TextBlock _status;
    private readonly ComboBox _model;
    private readonly Button _newChat;
    private readonly Border _body;

    private readonly DockPanel _chatPanel;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _messages;
    private readonly TextBlock _thinking;
    private readonly WrapPanel _chips;
    private readonly TextBox _input;
    private readonly Button _sendButton, _stopButton;

    private readonly List<Attachment> _attachments = new();
    private readonly List<UndoStep> _steps = new();
    private readonly DispatcherTimer _terminalPoll = new() { Interval = TimeSpan.FromSeconds(2) };
    /// <summary>차례 중 "생각 중…" 줄을 진행 상황(단계·받은 양·걸린 시간)으로 고쳐 쓴다.</summary>
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DateTime _turnStart, _phaseStart;
    /// <summary>지금 단계: wait(응답 기다림) / thinking / tool_use(도구 호출 작성) / run(도구 실행) / text(답 쓰기).</summary>
    private string _phase = "wait";
    private string? _phaseTool;
    private int _phaseChars;

    private string? _claudePath;
    private ClaudeSession? _session;
    private ClaudeLogin? _login;
    private Turn? _turn;
    private bool _follow = true, _checking, _polling, _shutdown, _ready;

    public ChatWindow(EditorContext ctx, McpServer mcp, AppSettings settings)
    {
        _ctx = ctx;
        _mcp = mcp;
        _settings = settings;

        Title = "Claude";
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

        // ── 머리줄: 로그인 상태 · 모델 · 새 대화
        _status = new TextBlock
        {
            Foreground = AppTheme.DimTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0),
        };
        _model = Ui.Combo(90, ModelLabels);
        _model.SelectedIndex = Math.Max(0, Array.IndexOf(ModelValues, settings.AiModel));
        _model.SelectionChanged += (_, _) => OnModelChanged();
        _model.Tip("Claude 모델(기본 = Claude Code 설정값). 바꾸면 다음 메시지부터 적용");
        _newChat = Ui.Button("새 대화", (_, _) => NewConversation());
        _newChat.Tip("대화를 비우고 새로 시작");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(4, 3) };
        header.Children.Add(_status);
        Grid.SetColumn(_model, 1);
        header.Children.Add(_model);
        Grid.SetColumn(_newChat, 2);
        header.Children.Add(_newChat);
        var headerBorder = new Border
        {
            Background = AppTheme.BackBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = header,
        };
        DockPanel.SetDock(headerBorder, Dock.Top);

        // ── 채팅 화면
        _messages = new StackPanel { Spacing = 6, Margin = new Thickness(10, 8) };
        _scroll = Ui.ScrollHost(_messages);
        _scroll.ScrollChanged += OnScrollChanged;
        _thinking = new TextBlock
        {
            Text = "생각 중…",
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
            Watermark = Ui.Shortcut("메시지 (Enter 보내기, Shift+Enter 줄바꿈)"),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_input, ScrollBarVisibility.Auto);
        _input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        var imageButton = Ui.Button("이미지…", (_, _) => AttachFile()).Tip("PNG·JPG·GIF·WebP 이미지를 첨부");
        var canvasButton = Ui.Button("캔버스 첨부", (_, _) => AttachCanvas()).Tip("지금 프레임 모습을 확대해 첨부");
        _sendButton = Ui.Button("보내기", (_, _) => Send()).Tip("Enter");
        _stopButton = Ui.Button("정지", (_, _) => StopTurn());
        _stopButton.IsVisible = false;
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(imageButton);
        Grid.SetColumn(canvasButton, 1);
        buttons.Children.Add(canvasButton);
        Grid.SetColumn(_sendButton, 3);
        buttons.Children.Add(_sendButton);
        Grid.SetColumn(_stopButton, 4);
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

        _body = new Border { Background = AppTheme.PanelBrush };
        Content = new DockPanel { Children = { headerBorder, _body } };

        _terminalPoll.Tick += async (_, _) => await PollLoginAsync();
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _ctx.UndoChanged += OnUndoChanged;
        _ctx.ProjectReplaced += OnProjectReplaced;
        PositionChanged += (_, _) => SaveBounds();
        Opened += async (_, _) => await CheckAsync();
        AddHint();
        SetHeader("", inChat: false);
        _ready = true;
    }

    /// <summary>Claude 프로세스를 끝낸다(앱을 닫을 때 메인 창이 부름). 진행 중인 차례는 멈춘다.</summary>
    public void Shutdown()
    {
        if (_shutdown) return;
        SaveBounds();
        if (_turn is not null)
        {
            _session?.Cancel();
            EndTurn(null, null, cancelled: true);
        }
        _shutdown = true;
        LeaveLogin();
        _session?.Dispose();
        _session = null;
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

    // ───────────── 상태 확인(설치 → 로그인 → 채팅) ─────────────

    private void SetHeader(string text, bool inChat)
    {
        _status.Text = text;
        _model.IsEnabled = inChat;
        _newChat.IsEnabled = inChat;
    }

    private void SetBody(Control content) => _body.Child = content;

    private async Task CheckAsync()
    {
        if (_checking || _shutdown) return;
        _checking = true;
        try
        {
            LeaveLogin();
            SetHeader("확인 중…", inChat: false);
            SetBody(new TextBlock
            {
                Text = "Claude Code 확인 중…",
                Foreground = AppTheme.DimTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var path = ClaudeCli.Locate(_settings.ClaudePath);
            if (path is null)
            {
                ShowNoCli();
                return;
            }
            if (_claudePath != path)
            {
                if (_turn is not null) StopTurn();
                _session?.Dispose();
                _session = null;
            }
            _claudePath = path;
            var status = await ClaudeCli.GetAuthStatusAsync(path);
            if (_shutdown) return;
            if (status.LoggedIn) ShowChat(status);
            else ShowLogin(status);
        }
        finally
        {
            _checking = false;
        }
    }

    private async void Recheck() => await CheckAsync();

    /// <summary>CLI가 없을 때: 설치 안내.</summary>
    private void ShowNoCli()
    {
        SetHeader("Claude Code 없음", inChat: false);
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(14, 12) };
        stack.Children.Add(TitleText("Claude Code를 찾지 못했습니다"));
        stack.Children.Add(WrapText("이 창은 컴퓨터에 설치된 Claude Code(claude 명령)와 사용자의 Claude 계정(Pro·Max 구독 등)으로 동작합니다. " +
                                    "터미널에서 아래 명령으로 설치한 뒤 \"다시 확인\"을 누르세요."));
        var unix = CommandBox("macOS · Linux (터미널)", "curl -fsSL https://claude.ai/install.sh | bash");
        var win = CommandBox("Windows (PowerShell)", "irm https://claude.ai/install.ps1 | iex");
        if (OperatingSystem.IsWindows()) stack.Children.AddRange([win, unix]);
        else stack.Children.AddRange([unix, win]);
        var recheck = Ui.Button("다시 확인", (_, _) => Recheck());
        recheck.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(recheck);

        var pathBox = new TextBox { Text = _settings.ClaudePath ?? "", Watermark = "예: /opt/homebrew/bin/claude", Margin = new Thickness(0, 2) };
        var browse = Ui.Button("찾아보기…", (_, _) =>
        {
            if (Ui.OpenFile(this, "claude 실행 파일", null) is { } picked) pathBox.Text = picked;
        });
        var apply = Ui.Button("적용", (_, _) =>
        {
            var text = (pathBox.Text ?? "").Trim();
            _settings.ClaudePath = text.Length == 0 ? null : text;
            Recheck();
        });
        var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        pathRow.Children.Add(pathBox);
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(browse);
        Grid.SetColumn(apply, 2);
        pathRow.Children.Add(apply);
        var pathStack = new StackPanel { Spacing = 4 };
        pathStack.Children.Add(WrapText("이미 설치했는데 못 찾으면 claude 실행 파일 경로를 직접 정하세요(터미널에서 \"which claude\" 또는 \"where claude\").", dim: true));
        pathStack.Children.Add(pathRow);
        if (!string.IsNullOrWhiteSpace(_settings.ClaudePath))
            pathStack.Children.Add(WrapText($"지정한 경로에 claude가 없습니다: {_settings.ClaudePath}", error: true));
        stack.Children.Add(Ui.Group("claude 경로 직접 지정", pathStack));
        SetBody(Ui.ScrollHost(stack));
    }

    /// <summary>로그인 안 됨: 숨은 로그인(주소 표시)·터미널 로그인·다시 확인.</summary>
    private void ShowLogin(AuthStatus status)
    {
        SetHeader("로그인 필요", inChat: false);
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(14, 12) };
        stack.Children.Add(TitleText("Claude 계정으로 로그인하세요"));
        stack.Children.Add(WrapText("Claude 구독(Pro·Max 등) 계정으로 로그인하면 Claude가 이 편집기에서 그림을 보고 고칠 수 있습니다. " +
                                    "로그인 정보는 Claude Code가 보관하며 Gpixel은 읽지 않습니다."));
        if (status.Error is { } err) stack.Children.Add(WrapText($"로그인 상태를 확인하지 못했습니다: {err}", error: true));
        var info = new StackPanel { Spacing = 6 };
        var login = Ui.Button("Claude로 로그인", (_, _) => StartLogin(info));
        var terminal = Ui.Button("터미널에서 로그인", (_, _) => StartTerminalLogin(info)).Tip("터미널 창에서 claude auth login 실행");
        var recheck = Ui.Button("다시 확인", (_, _) => Recheck());
        stack.Children.Add(new WrapPanel { Children = { login, terminal, recheck } });
        stack.Children.Add(info);
        SetBody(Ui.ScrollHost(stack));
    }

    private void StartLogin(StackPanel info)
    {
        if (_claudePath is not { } path) return;
        LeaveLogin();
        info.Children.Clear();
        info.Children.Add(WrapText("브라우저가 열리면 로그인을 마치세요. 열리지 않으면 아래 주소를 누르세요.", dim: true));
        var urls = new StackPanel { Spacing = 4 };
        var output = WrapText("", dim: true);
        var codeBox = new TextBox { Watermark = "인증 코드", Margin = new Thickness(0, 2) };
        var codeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        codeRow.Children.Add(codeBox);
        var login = new ClaudeLogin(path);
        var sendCode = Ui.Button("코드 보내기", (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(codeBox.Text)) return;
            login.SendCode(codeBox.Text);
            codeBox.Text = "";
        });
        Grid.SetColumn(sendCode, 1);
        codeRow.Children.Add(sendCode);
        var codePanel = new StackPanel { Spacing = 2, IsVisible = false };
        codePanel.Children.Add(WrapText("브라우저가 인증 코드를 보여 주면 여기에 붙여 넣으세요:", dim: true));
        codePanel.Children.Add(codeRow);
        info.Children.AddRange([urls, codePanel, output]);

        login.UrlFound += url =>
        {
            urls.Children.Add(LinkRow(url));
            codePanel.IsVisible = true;
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
            info.Children.Add(WrapText($"로그인하지 못했습니다. \"터미널에서 로그인\"을 써 보세요.\n{message}", error: true));
        };
        try
        {
            login.Start();
            _login = login;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            login.Dispose();
            info.Children.Add(WrapText($"claude를 실행하지 못했습니다: {ex.Message}", error: true));
        }
    }

    private void StartTerminalLogin(StackPanel info)
    {
        if (_claudePath is not { } path) return;
        LeaveLogin();
        info.Children.Clear();
        if (!ClaudeCli.OpenLoginTerminal(path, out var error))
        {
            info.Children.Add(WrapText($"터미널을 열지 못했습니다: {error}\n터미널에서 직접 \"{path}\" auth login 을 실행한 뒤 \"다시 확인\"을 누르세요.", error: true));
            return;
        }
        info.Children.Add(WrapText("터미널 창에서 로그인을 마치세요. 로그인되면 저절로 채팅으로 넘어갑니다(안 되면 \"다시 확인\").", dim: true));
        _terminalPoll.Start();
    }

    /// <summary>터미널 로그인 중: 2초마다 로그인 상태를 본다.</summary>
    private async Task PollLoginAsync()
    {
        if (_polling || _claudePath is not { } path) return;
        _polling = true;
        try
        {
            var status = await ClaudeCli.GetAuthStatusAsync(path);
            if (_terminalPoll.IsEnabled && status.LoggedIn && !_shutdown)
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
        LeaveLogin();
        if (_claudePath is not { } path) return;
        var who = status.Describe();
        SetHeader(who.Length > 0 ? who : "로그인됨", inChat: true);
        if (_session is null)
        {
            _session = new ClaudeSession(path, _mcp) { Model = CurrentModel() };
            HookSession(_session);
        }
        SetBody(_chatPanel);
        UpdateInputState();
        Dispatcher.UIThread.Post(() => _input.Focus(), DispatcherPriority.Background);
    }

    // ───────────── 세션 이벤트 ─────────────

    private void HookSession(ClaudeSession s)
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
            string? error = r.IsError ? $"오류: {(string.IsNullOrWhiteSpace(r.Text) ? r.Subtype ?? "알 수 없는 오류" : r.Text)}" : null;
            if (_turn is not null) EndTurn(error, r, cancelled: false);
            else if (error is not null) AddSystemLine(error, error: true);
            // 로그인이 풀렸으면 로그인 화면으로(대화 내용은 남아 있다가 다시 로그인하면 보인다).
            if (r.IsError && r.Text is { } text && (text.Contains("/login", StringComparison.OrdinalIgnoreCase)
                                                     || text.Contains("Invalid API key", StringComparison.OrdinalIgnoreCase)))
                Recheck();
        };
        s.Exited += info =>
        {
            var msg = $"Claude 프로세스가 끝났습니다(코드 {info.ExitCode}).";
            if (info.StderrTail.Length > 0) msg += "\n" + info.StderrTail;
            if (_turn is not null) EndTurn(msg, null, cancelled: false);
            else AddSystemLine(msg, error: true);
        };
        s.Notice += text => AddSystemLine(text, error: false);
    }

    // ───────────── 보내기·차례 ─────────────

    private string? CurrentModel() => ModelValues[Math.Clamp(_model.SelectedIndex, 0, ModelValues.Length - 1)];

    private void OnModelChanged()
    {
        var model = CurrentModel();
        if (_settings.AiModel == model) return;
        _settings.AiModel = model;
        if (_session is null) return;
        if (_turn is not null) StopTurn();
        _session.Model = model;
        _session.Restart();
        AddSystemLine($"모델: {ModelLabels[Math.Max(0, _model.SelectedIndex)]} — 다음 메시지부터 적용됩니다.", error: false);
    }

    private void NewConversation()
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
        if (_session is not { } session || _turn is not null || _shutdown) return;
        var text = (_input.Text ?? "").Trim();
        if (text.Length == 0 && _attachments.Count == 0) return;
        var attachments = _attachments.ToList();
        session.WorkingDirectory = ProjectFolder();
        session.Model = CurrentModel();
        try
        {
            session.Send(text, attachments.Select(a => a.Png).ToList());
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            AddSystemLine($"claude를 실행하지 못했습니다: {ex.Message}", error: true);
            return;
        }
        _input.Text = "";
        ClearAttachments();
        RemoveHint();
        _follow = true;
        AddUserBubble(text, attachments);

        var label = $"Claude: {Shorten(text.Length > 0 ? text : "(이미지)", 24)}";
        var turn = new Turn(label);
        if (_ctx.Project is { } p)
        {
            // 사용자가 하던 떠 있는 선택 편집은 이 차례에 섞이지 않게 먼저 확정한다.
            _ctx.CommitFloating();
            p.History.BeginGroup(label);
            turn.Project = p;
        }
        _turn = turn;
        _messages.Children.Add(_thinking);
        _turnStart = DateTime.UtcNow;
        SetPhase("wait", null);
        _progressTimer.Start();
        UpdateInputState();
        RefreshSteps();
    }

    private void StopTurn()
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
        FinishStream(turn);
        _messages.Children.Remove(_thinking);
        _progressTimer.Stop();

        IUndoCommand? group = null;
        SpriteProject? project = turn.Project;
        if (project is not null && ReferenceEquals(project, _ctx.Project)) group = project.History.EndGroup();

        if (error is not null) AddSystemLine(error, error: true);
        if (group is not null && project is not null) AddSnapshot(project, group);

        var parts = new List<string>();
        if (cancelled) parts.Add("정지됨");
        if (result?.CostUsd is { } cost && cost > 0) parts.Add("$" + cost.ToString("0.000", CultureInfo.InvariantCulture));
        double ms = result?.DurationMs ?? turn.Clock.Elapsed.TotalMilliseconds;
        parts.Add((ms / 1000).ToString("0.0", CultureInfo.InvariantCulture) + "초");
        AddItem(new TextBlock
        {
            Text = string.Join(" · ", parts),
            Foreground = AppTheme.DimTextBrush,
            FontSize = AppTheme.SmallFontSize - 1,
            Margin = new Thickness(2, 0, 0, 6),
        });
        UpdateInputState();
        RefreshSteps();
    }

    /// <summary>차례 중에 다른 프로젝트가 열리면(new_project 등) 묶음을 새 프로젝트로 옮긴다.</summary>
    private void OnProjectReplaced(object? sender, EventArgs e)
    {
        if (_turn is { } turn)
        {
            turn.Project?.History.EndGroup();
            turn.Project = null;
            if (_ctx.Project is { } p)
            {
                p.History.BeginGroup(turn.Label);
                turn.Project = p;
            }
        }
        RefreshSteps();
    }

    private void UpdateInputState()
    {
        bool running = _turn is not null;
        _sendButton.IsVisible = !running;
        _stopButton.IsVisible = running;
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

    private string? ProjectFolder()
        => _ctx.Project?.Path is { } file && Path.GetDirectoryName(file) is { Length: > 0 } dir ? dir : null;

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
        static string Time(TimeSpan t) => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}분 {t.Seconds}초" : $"{t.Seconds}초";
        string Size(int chars) => chars >= 1024 ? $"{chars / 1024.0:0.0}K자" : $"{chars}자";
        string what = _phase switch
        {
            "thinking" => $"생각 중… ({Size(_phaseChars)})",
            "tool_use" => $"도구 호출 작성 중: {_phaseTool ?? "?"} ({Size(_phaseChars)})",
            "run" => $"편집기에서 실행 중: {_phaseTool ?? "?"}",
            "text" => "답 쓰는 중…",
            _ => "응답 기다리는 중…",
        };
        _thinking.Text = $"{what} · {Time(now - _phaseStart)} (전체 {Time(now - _turnStart)})";
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

    private TextBlock? _hint;

    private void AddHint()
    {
        _hint = WrapText("그림에 대해 무엇이든 부탁하세요.\n예: \"16×16 슬라임 캐릭터를 그려 줘\", \"외곽선을 더 어둡게 해 줘\", \"걷기 애니메이션 4프레임 만들어 줘\"", dim: true);
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
                JsonArray a when key == "rows" => $"{a.Count}줄",
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

    // ───────────── 실행취소 단계 ─────────────

    /// <summary>차례가 끝난 뒤 캔버스 모습과 "이 단계 되돌리기/다시 적용" 단추.</summary>
    private void AddSnapshot(SpriteProject project, IUndoCommand group)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(2, 2, 0, 0) };
        if (Composite(project) is { } img)
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
        var button = Ui.Button("이 단계 되돌리기");
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
        if (_turn is not null || !ReferenceEquals(_ctx.Project, step.Project)) return;
        var history = step.Project.History;
        int depth = history.UndoDepthOf(step.Command);
        if (depth == 1) _ctx.UndoStep();
        else if (depth > 1)
        {
            if (Ui.Ask(this, $"이후 편집 {depth - 1}단계도 함께 되돌립니다. 계속할까요?", "되돌리기", MessageButtons.OKCancel, MessageIcon.Question)
                != DialogResult.OK) return;
            for (int i = 0; i < depth; i++)
            {
                if (!_ctx.UndoStep()) break;
            }
        }
        else if (history.RedoDepthOf(step.Command) == 1) _ctx.RedoStep();
        RefreshSteps();
    }

    private void OnUndoChanged(object? sender, EventArgs e) => RefreshSteps();

    private void RefreshSteps()
    {
        foreach (var s in _steps) RefreshStep(s);
    }

    private void RefreshStep(UndoStep step)
    {
        bool same = ReferenceEquals(_ctx.Project, step.Project);
        bool idle = _turn is null;
        var history = step.Project.History;
        int undo = same ? history.UndoDepthOf(step.Command) : -1;
        int redo = same ? history.RedoDepthOf(step.Command) : -1;
        var b = step.Button;
        if (undo > 0)
        {
            b.Content = "이 단계 되돌리기";
            b.IsEnabled = idle;
            b.Tip(undo > 1 ? $"이후 편집 {undo - 1}단계도 함께 되돌립니다" : null);
        }
        else if (redo > 0)
        {
            b.Content = "다시 적용";
            b.IsEnabled = idle && redo == 1;
            b.Tip(redo > 1 ? "먼저 그 뒤 단계들을 다시 실행해야 합니다" : null);
        }
        else
        {
            b.Content = "이 단계 되돌리기";
            b.IsEnabled = false;
            b.Tip(same ? "실행취소 기록에 없습니다" : "다른 프로젝트의 단계입니다");
        }
    }

    // ───────────── 첨부 ─────────────

    private void AttachFile()
    {
        var path = Ui.OpenFile(this, "이미지 첨부", null, new FileFilter("이미지", "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"));
        if (path is null) return;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, $"파일을 읽지 못했습니다.\n{ex.Message}");
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
            Ui.Error(this, "이 이미지를 읽을 수 없습니다(지원하지 않는 형식이거나 깨진 파일).");
            return;
        }
        bool isPng = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';
        bool small = Math.Max(bmp.PixelSize.Width, bmp.PixelSize.Height) <= AttachMaxSide;
        AddAttachment(isPng && small ? bytes : ToPng(bmp), bmp, pixelArt: false);
    }

    private void AttachCanvas()
    {
        if (_ctx.Project is not { } p || Composite(p) is not { } img)
        {
            Ui.Warn(this, "열린 그림이 없습니다.");
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
        if (Clipboard is not { } clipboard) return;
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
        if (bmp is null || _shutdown) return;
        AddAttachment(ToPng(bmp), bmp, pixelArt: false);
    }

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

    private void AddAttachment(byte[] png, Bitmap thumb, bool pixelArt)
    {
        var a = new Attachment(png, thumb, pixelArt);
        _attachments.Add(a);
        var remove = new Button
        {
            Content = "✕",
            Padding = new Thickness(3, 0),
            MinHeight = 0,
            FontSize = AppTheme.SmallFontSize,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        }.Tip("첨부 빼기");
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

    private static Control Thumb(Attachment a, int size)
    {
        var px = a.Thumb.PixelSize;
        double k = Math.Min((double)size / Math.Max(1, px.Width), (double)size / Math.Max(1, px.Height));
        if (a.PixelArt && k > 1) k = Math.Floor(k);
        var image = new Image { Source = a.Thumb, Width = Math.Max(1, px.Width * k), Height = Math.Max(1, px.Height * k), Stretch = Stretch.Fill };
        if (a.PixelArt) RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        return new CheckerBackground { Child = image, Margin = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left };
    }

    // ───────────── 이미지 도우미 ─────────────

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
        var copy = Ui.Button("복사", (_, _) => CopyText(command));
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
                ClaudeCli.OpenUrl(url);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
            {
                Ui.Error(this, $"브라우저를 열지 못했습니다. 주소를 복사해 브라우저에 붙여 넣으세요.\n{ex.Message}");
            }
        };
        var copy = Ui.Button("복사", (_, _) => CopyText(url));
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
}
