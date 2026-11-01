using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App.Ai;

/// <summary>
/// 효과 만들기 창 오른쪽에 여는 AI 칸: 사용자가 말로 부탁하면 AI가 효과 규칙(<see cref="Chickxel.Core.Scripting.ScriptEffectGuide"/>)에 맞는 원문을
/// 효과 만들기 편집기에 쓴다. AI 채팅 창과 같은 AI CLI(Claude Code·Codex, <see cref="AiProvider"/>)·로그인·모델과 편집기 MCP 서버(<see cref="McpServer"/>)를 쓰되,
/// 세션은 따로(시스템 프롬프트 <see cref="AiCli.EffectMakerSystemPrompt"/>)이고 대화의 첫 메시지 앞에 규칙·API를 붙인다(<see cref="AiCli.EffectMakerGuidePrefix"/>).
/// AI는 MCP 서버의 효과 만들기 범위(<see cref="McpScope.EffectMaker"/> — 읽기 도구와 효과 만들기 도구만, 서버가 나머지를 막음)로 열린 문서를 보기만 하고
/// 편집기 원문만 고친다 — 문서를 고치지 않으므로 실행취소 묶음·캔버스 모습은 없고, 바뀐 원문은 편집기의 실행취소로 되돌린다.
/// 입력란은 AI 채팅 창과 같은 그림 칩 입력란(<see cref="ChatImageInput"/>): 그림 파일·붙여넣기·끌어 놓기·캔버스·지금 미리보기 칸을 첨부한다.
/// 가벼운 칸이라 대화를 저장하지 않고, CLI 설치·로그인은 AI 채팅 창에 맡긴다(이 칸은 안내와 "AI 채팅 열기"·"다시 확인"만).
/// </summary>
internal sealed class EffectMakerAiPanel : Border
{
    private static readonly IBrush ErrorBrush = AppTheme.Brush(Color.FromRgb(240, 110, 110));
    private static readonly IBrush UserBubbleBrush = AppTheme.Brush(Color.FromRgb(50, 66, 88));
    private static readonly FontFamily MonoFont = new("Menlo, Consolas, DejaVu Sans Mono, monospace");

    private readonly McpServer _mcp;
    private readonly AppSettings _settings;
    private readonly Action _openChat;
    private readonly Func<PixelImage?> _canvasImage, _previewFrame;
    private readonly TabStrip _providers;
    /// <summary>모델 칸(보내기 왼쪽, <see cref="_modelChoices"/> 순서). AI 채팅 창과 같은 설정 값(<see cref="AiProvider.GetModel"/>)을 읽고 쓴다.</summary>
    private readonly ComboBox _model;
    private IReadOnlyList<ModelChoice> _modelChoices = [];
    private bool _fillingModels;
    private readonly TextBlock _status;
    private readonly Button _newChat;
    private readonly Border _body = new();
    private readonly DockPanel _chatPanel;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _messages = new() { Spacing = 6, Margin = new Thickness(8, 8) };
    private readonly TextBlock _thinking;
    private readonly ChatImageInput _imageInput;
    private readonly Button _send, _stop;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private AiProvider _provider;
    private string? _cliPath;
    private IChatSession? _session;
    /// <summary>이 대화에 규칙·API를 이미 보냈는지(새 대화·AI 바꾸기면 다시 붙임).</summary>
    private bool _primed;
    private bool _started, _checking, _shutdown, _running, _renderPending;
    private readonly Stopwatch _turnClock = new();
    /// <summary>스트리밍 중인 답 글 보기와 그 글(Claude만 조각으로 옴).</summary>
    private MarkdownView? _stream;
    private readonly StringBuilder _streamText = new();
    /// <summary>도는 도구 호출 id → 도구 줄·시작 시각.</summary>
    private readonly Dictionary<string, (TextBlock Line, long Started)> _tools = new();

    /// <param name="openChat">AI 채팅 창을 연다(CLI 설치·로그인 안내).</param>
    /// <param name="close">칸을 닫는다(머리줄의 닫기 단추).</param>
    /// <param name="canvasImage">열린 문서의 지금 프레임 합성(없으면 null) — "캔버스 첨부".</param>
    /// <param name="previewFrame">효과 만들기 미리보기에 보이는 칸(없으면 null) — "미리보기 첨부".</param>
    public EffectMakerAiPanel(McpServer mcp, AppSettings settings, Action openChat, Action close, Func<PixelImage?> canvasImage, Func<PixelImage?> previewFrame)
    {
        _mcp = mcp;
        _settings = settings;
        _openChat = openChat;
        _canvasImage = canvasImage;
        _previewFrame = previewFrame;
        _provider = AiProvider.FromId(settings.ChatProvider);

        Background = AppTheme.PanelBrush;
        BorderBrush = AppTheme.BorderBrush;
        BorderThickness = new Thickness(1);

        // ── 머리줄: 제목 · AI(Claude/Codex) · 새 대화 · 닫기, 아래 상태 글
        _providers = new TabStrip(TabStripKind.Segmented) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0) };
        foreach (var p in AiProvider.All) _providers.Add(p.Name, tip: p.ProductName);
        _providers.SelectedIndex = AiProvider.All.ToList().IndexOf(_provider);
        _providers.SelectionChanged += (_, _) => OnProviderChanged();
        _newChat = Ui.Button(UiStrings.S_ChatWindow_ChatWindow_ACBC4AE706, (_, _) => NewConversation()).Tip(UiStrings.EffectMakerAi_NewChatTip);
        var closeButton = Ui.IconButton(VectorIcon.X, UiStrings.EffectMakerAi_CloseTip, (_, _) => close(), size: 12);
        var title = Ui.IconLabel(VectorIcon.Sparkles, UiStrings.EffectMakerAi_Title);
        title.Children.OfType<TextBlock>().First().FontWeight = FontWeight.Bold;
        title.VerticalAlignment = VerticalAlignment.Center;
        var headerRow = new DockPanel { LastChildFill = true };
        var headerButtons = Ui.Row(_providers, _newChat, closeButton);
        DockPanel.SetDock(headerButtons, Dock.Right);
        headerRow.Children.Add(headerButtons);
        headerRow.Children.Add(title);
        _status = new TextBlock { Foreground = AppTheme.DimTextBrush, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 2, 2, 0) };
        var header = new Border
        {
            Background = AppTheme.BackBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 4),
            Child = new StackPanel { Children = { headerRow, _status } },
        };
        DockPanel.SetDock(header, Dock.Top);

        // ── 채팅 화면: 대화(안내 글로 시작) + 그림 칩 입력란·첨부 단추·보내기/멈춤
        _scroll = Ui.ScrollHost(_messages);
        _thinking = new TextBlock { Foreground = AppTheme.DimTextBrush, FontStyle = FontStyle.Italic, Margin = new Thickness(2), TextWrapping = TextWrapping.Wrap };
        _imageInput = new ChatImageInput(UiStrings.EffectMakerAi_Watermark);
        _imageInput.Input.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        var attachFile = Ui.IconButton(VectorIcon.FolderOpen, UiStrings.S_ChatWindow_ChatTab_046C36A571, (_, _) => _imageInput.AttachFile());
        var attachCanvas = Ui.IconButton(VectorIcon.Image, UiStrings.EffectMakerAi_AttachCanvasTip, (_, _) => AttachCanvas());
        var attachPreview = Ui.IconButton(VectorIcon.Film, UiStrings.EffectMakerAi_AttachPreviewTip, (_, _) => AttachPreview());
        _send = Ui.Button(VectorIcon.ArrowUp, UiStrings.S_ChatWindow_ChatTab_CF5843D1B0, (_, _) => Send()).Tip("Enter");
        _stop = Ui.Button(VectorIcon.Square, UiStrings.S_ChatWindow_ChatTab_1CB431966C, (_, _) => StopTurn());
        _stop.IsVisible = false;
        _model = new ComboBox
        {
            MaxWidth = 180,
            VerticalAlignment = VerticalAlignment.Center,
            // 좁으면 고른 모델 이름은 말줄임(보내기 단추가 밀려 잘리지 않게).
            SelectionBoxItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>(
                (label, _) => new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis }, supportsRecycling: false),
        };
        _model.SelectionChanged += (_, _) => OnModelChanged();
        FillModels();
        var sendButtons = Ui.Row(_model, _send, _stop);
        var buttons = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(sendButtons, Dock.Right);
        buttons.Children.Add(sendButtons);
        buttons.Children.Add(Ui.Row(attachFile, attachCanvas, attachPreview));
        var inputArea = new Border
        {
            Background = AppTheme.BackBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(6),
            Child = new StackPanel { Children = { _imageInput, buttons } },
        };
        DockPanel.SetDock(inputArea, Dock.Bottom);
        _chatPanel = new DockPanel { Children = { inputArea, _scroll } };
        AddIntro();

        Child = new DockPanel { Children = { header, _body } };
        _clock.Tick += (_, _) => UpdateThinking();
        UpdateInputState();
    }

    /// <summary>칸이 보이게 됨: 처음이면 CLI·로그인 확인을 시작하고, 채팅 중이면 입력란에 포커스.</summary>
    public void Activate()
    {
        if (_shutdown) return;
        if (!_started)
        {
            _started = true;
            _ = CheckAsync();
        }
        else if (ReferenceEquals(_body.Child, _chatPanel)) FocusInput();
    }

    /// <summary>창을 닫을 때: 도는 차례를 멈추고 AI 프로세스를 끝낸다.</summary>
    public void Shutdown()
    {
        if (_shutdown) return;
        StopTurn();
        _shutdown = true;
        _clock.Stop();
        _session?.Dispose();
        _session = null;
    }

    /// <summary>시험용(화면 점검 하네스): CLI 확인·로그인 없이 주어진 세션으로 채팅 화면을 연다.</summary>
    internal void ShowChatWithoutCli(IChatSession session)
    {
        _started = true;
        AttachSession(session);
        _status.Text = _provider.Name;
        _body.Child = _chatPanel;
        UpdateInputState();
    }

    // ───────────── 확인(설치 → 로그인 → 채팅) ─────────────

    private async Task CheckAsync()
    {
        if (_checking || _shutdown) return;
        _checking = true;
        try
        {
            _status.Text = UiStrings.S_ChatWindow_CheckAsync_F75B2590E0;
            _body.Child = CenterText(string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_CheckAsync_CA460A15B1, _provider.ProductName));
            var provider = _provider;
            var path = provider.Locate(_settings);
            if (path is null)
            {
                DropSession();
                ShowSetup(UiStrings.FormatEffectMakerAi_NoCli(product: provider.ProductName));
                return;
            }
            var status = await provider.GetAuthStatusAsync(path);
            if (_shutdown || !ReferenceEquals(provider, _provider)) return;
            if (!status.LoggedIn)
            {
                ShowSetup(UiStrings.FormatEffectMakerAi_NeedLogin(product: provider.ProductName));
                return;
            }
            if (_cliPath != path) DropSession();
            _cliPath = path;
            var who = status.Describe();
            _status.Text = who.Length > 0 ? $"{provider.Name} · {who}" : provider.Name;
            if (_session is null) AttachSession(provider.CreateSession(path, _mcp, AiCli.EffectMakerSystemPrompt, McpScope.EffectMaker));
            _body.Child = _chatPanel;
            UpdateInputState();
            FocusInput();
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>CLI가 없거나 로그인이 필요함: 안내와 "AI 채팅 열기"(설치·로그인은 채팅 창에서)·"다시 확인".</summary>
    private void ShowSetup(string message)
    {
        _status.Text = _provider.ProductName;
        var open = Ui.Button(VectorIcon.Sparkles, UiStrings.EffectMakerAi_OpenChat, (_, _) => _openChat());
        var recheck = Ui.Button(VectorIcon.Refresh, UiStrings.EffectMakerAi_Recheck, async (_, _) => await CheckAsync());
        _body.Child = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.TextBrush },
                Ui.Row(open, recheck),
            },
        };
    }

    private static TextBlock CenterText(string text) => new()
    {
        Text = text,
        Foreground = AppTheme.DimTextBrush,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12),
    };

    /// <summary>Claude ↔ Codex: 도는 차례를 멈추고 세션을 버린 뒤 새 AI로 다시 확인한다(새 대화). 고른 AI는 설정에 바로 남긴다(다음에 열 때 그 AI).</summary>
    private void OnProviderChanged()
    {
        if (_providers.SelectedIndex < 0 || _shutdown) return;
        var provider = AiProvider.All[_providers.SelectedIndex];
        if (ReferenceEquals(provider, _provider)) return;
        StopTurn();
        DropSession();
        _provider = provider;
        _settings.ChatProvider = provider.Id;
        _settings.Save();
        FillModels();
        ClearTranscript();
        _started = true;
        _ = CheckAsync();
    }

    private string? CurrentModel()
        => _modelChoices.Count == 0 ? null : _modelChoices[Math.Clamp(_model.SelectedIndex, 0, _modelChoices.Count - 1)].Value;

    /// <summary>모델 칸을 지금 AI의 목록으로 채우고 설정에 남은 모델을 고른다(이름에 실제 버전이 붙으면 다시 부름).</summary>
    private void FillModels()
    {
        _fillingModels = true;
        try
        {
            _modelChoices = _provider.Models(_settings);
            _model.Items.Clear();
            foreach (var c in _modelChoices)
            {
                var item = new ComboBoxItem { Content = c.Label };
                if (c.Tip is { Length: > 0 } tip) item.Tip(tip);
                _model.Items.Add(item);
            }
            var saved = _provider.GetModel(_settings);
            _model.SelectedIndex = Math.Max(0, _modelChoices.ToList().FindIndex(c => c.Value == saved));
            _model.Tip(string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_FillModels_B806D71CEE, _provider.Name, _provider.ProductName));
        }
        finally
        {
            _fillingModels = false;
        }
    }

    /// <summary>모델을 바꿈: 설정에 바로 남기고, 세션이 있으면 같은 대화를 이어 다음 메시지부터 새 모델로 띄운다.</summary>
    private void OnModelChanged()
    {
        if (_fillingModels || _model.SelectedIndex < 0) return;
        var model = CurrentModel();
        if (_provider.GetModel(_settings) == model) return;
        _provider.SetModel(_settings, model);
        _settings.Save();
        if (_session is null) return;
        StopTurn();
        _session.Model = model;
        _session.Restart();
        AddLine(string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_OnModelChanged_C640BCC05A, _modelChoices[_model.SelectedIndex].Label), error: false);
    }

    private void DropSession()
    {
        StopTurn();
        _session?.Dispose();
        _session = null;
        _cliPath = null;
        _primed = false;
        UpdateInputState();
    }

    // ───────────── 세션 ─────────────

    private void AttachSession(IChatSession s)
    {
        _session?.Dispose();
        _session = s;
        _primed = false;
        s.TextStarted += () => { if (_running && ReferenceEquals(s, _session)) BeginStream(); };
        s.TextDelta += delta =>
        {
            if (!_running || !ReferenceEquals(s, _session)) return;
            if (_stream is null) BeginStream();
            _streamText.Append(delta);
            ScheduleStreamRender();
        };
        s.AssistantText += text =>
        {
            if (!ReferenceEquals(s, _session) || text.Length == 0) return;
            if (_stream is { } view)
            {
                view.SetText(text);
                _stream = null;
                _streamText.Clear();
            }
            else AddItem(Answer(text));
        };
        s.ToolUse += info =>
        {
            if (!_running || !ReferenceEquals(s, _session)) return;
            FinishStream();
            var line = new TextBlock { Text = $"{info.Name}", Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextWrapping = TextWrapping.Wrap };
            if (info.Id.Length > 0) _tools[info.Id] = (line, Stopwatch.GetTimestamp());
            AddItem(Ui.Row(Ui.Icon(VectorIcon.Code, 11), line));
        };
        s.ToolResult += info =>
        {
            if (!ReferenceEquals(s, _session) || !_tools.Remove(info.ToolUseId, out var tool)) return;
            if (info.IsError)
            {
                tool.Line.Foreground = ErrorBrush;
                if (info.Text.Length > 0) tool.Line.Text += $" - {info.Text}";
            }
            tool.Line.Text += $" ({Seconds(Stopwatch.GetElapsedTime(tool.Started))})";
        };
        s.TurnCompleted += r =>
        {
            if (!ReferenceEquals(s, _session)) return;
            string? error = r.IsError
                ? string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_HookSession_E1F3DD4697, string.IsNullOrWhiteSpace(r.Text) ? r.Subtype ?? UiStrings.S_ChatWindow_HookSession_365CF8E859 : r.Text)
                : null;
            if (_running) EndTurn(error, r, cancelled: false);
            else if (error is not null) AddLine(error, error: true);
            if (r.NeedsLogin) _ = CheckAsync();
        };
        s.Exited += info =>
        {
            if (!ReferenceEquals(s, _session)) return;
            var message = string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_HookSession_5674519D81, _provider.Name, info.ExitCode);
            if (info.StderrTail.Length > 0) message += "\n" + info.StderrTail;
            if (_running) EndTurn(message, null, cancelled: false);
            else AddLine(message, error: true);
        };
        s.Notice += text => { if (ReferenceEquals(s, _session)) AddLine(text, error: false); };
        s.McpProblem += text =>
        {
            if (ReferenceEquals(s, _session))
                AddLine(text + "\n" + UiStrings.FormatChat_McpCliPath(path: _cliPath ?? UiStrings.AiStatus_Unknown), error: true);
        };
        s.ModelResolved += model =>
        {
            if (ReferenceEquals(s, _session) && _provider.RememberResolved(_settings, s.Model, model)) FillModels();
        };
    }

    // ───────────── 보내기·차례 ─────────────

    /// <summary>Enter = 보내기(칩 자판·그림 붙여넣기는 <see cref="ChatImageInput"/>이 먼저 처리, Shift+Enter = 줄바꿈).</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        // 한글 조합 중이던 글자가 먼저 들어가도록 한 박자 늦게 보낸다.
        Dispatcher.UIThread.Post(Send, DispatcherPriority.Background);
    }

    /// <summary>열린 문서의 지금 프레임을 칩으로 첨부(픽셀 아트 — 정수 배로 키워 보냄).</summary>
    private void AttachCanvas()
    {
        if (_canvasImage() is not { } image)
        {
            Ui.Warn(this, UiStrings.S_ChatWindow_AttachCanvas_F7C294FC0C);
            return;
        }
        _imageInput.AttachImage(image, pixelArt: true);
    }

    /// <summary>효과 만들기 미리보기에 보이는 칸(효과를 입힌 병아리)을 칩으로 첨부.</summary>
    private void AttachPreview()
    {
        if (_previewFrame() is not { } image)
        {
            Ui.Warn(this, UiStrings.EffectMakerAi_NoPreview);
            return;
        }
        _imageInput.AttachImage(image, pixelArt: true);
    }

    private void Send()
    {
        if (_session is not { } session || _running || _shutdown) return;
        var text = _imageInput.StoredText();
        var attachments = _imageInput.Attachments.ToList();
        if (text.Length == 0 && attachments.Count == 0) return;
        // 설정에서 CLI 경로를 바꿨으면 다시 확인한다(보내지 않음 — 확인 뒤 다시 보냄).
        if (_cliPath is not null && _cliPath != _provider.Locate(_settings))
        {
            _ = CheckAsync();
            return;
        }
        session.Model = CurrentModel();
        var modelText = ChatImageTokens.ToModel(text);
        var message = _primed ? modelText : AiCli.EffectMakerGuidePrefix() + modelText;
        try
        {
            session.Send(message, attachments.Select(a => a.Png).ToList());
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            AddLine(string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_Send_E1ACACFBF7, _provider.Command, ex.Message), error: true);
            return;
        }
        _primed = true;
        _imageInput.Clear();
        AddItem(UserBubble(text, attachments));
        _running = true;
        _tools.Clear();
        _turnClock.Restart();
        _messages.Children.Add(_thinking);
        UpdateThinking();
        _clock.Start();
        UpdateInputState();
    }

    private void StopTurn()
    {
        if (!_running) return;
        _session?.Cancel();
        EndTurn(null, null, cancelled: true);
    }

    /// <summary>차례를 마무리한다: 스트리밍 글을 닫고, 오류가 있으면 보이고, 끝에 멈춤·비용·시간 줄.</summary>
    private void EndTurn(string? error, TurnResult? result, bool cancelled)
    {
        if (!_running) return;
        _running = false;
        _clock.Stop();
        FinishStream();
        _messages.Children.Remove(_thinking);
        if (error is not null) AddLine(error, error: true);
        var parts = new List<string>();
        if (cancelled) parts.Add(UiStrings.S_ChatWindow_EndTurn_D19A601695);
        if (result?.CostUsd is double cost and > 0) parts.Add("$" + cost.ToString("0.000", CultureInfo.InvariantCulture));
        parts.Add(Seconds(_turnClock.Elapsed));
        AddItem(new TextBlock { Text = string.Join(" · ", parts), Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, HorizontalAlignment = HorizontalAlignment.Right });
        UpdateInputState();
    }

    /// <summary>새 대화: 도는 차례를 멈추고 세션의 대화를 잊는다(다음 메시지에 규칙·API를 다시 붙임). 편집기 원문은 그대로.</summary>
    private void NewConversation()
    {
        StopTurn();
        _session?.Reset();
        _primed = false;
        ClearTranscript();
        FocusInput();
    }

    private void UpdateInputState()
    {
        bool ready = _session is not null && !_shutdown;
        _send.IsVisible = !_running;
        _stop.IsVisible = _running;
        _send.IsEnabled = ready;
        _newChat.IsEnabled = ready;
    }

    private void UpdateThinking()
        => _thinking.Text = UiStrings.FormatEffectMakerAi_Working(seconds: ((int)_turnClock.Elapsed.TotalSeconds).ToString(CultureInfo.CurrentCulture));

    private void FocusInput() => _imageInput.FocusInput();

    // ───────────── 대화 그리기 ─────────────

    private void AddIntro() => _messages.Children.Add(new TextBlock
    {
        Text = UiStrings.EffectMakerAi_Intro,
        TextWrapping = TextWrapping.Wrap,
        Foreground = AppTheme.DimTextBrush,
        Margin = new Thickness(2, 0, 2, 4),
    });

    private void ClearTranscript()
    {
        _stream = null;
        _streamText.Clear();
        _tools.Clear();
        _messages.Children.Clear();
        AddIntro();
    }

    /// <summary>대화 끝("생각 중…" 앞)에 붙이고 끝까지 내린다.</summary>
    private void AddItem(Control c)
    {
        int at = _messages.Children.IndexOf(_thinking);
        if (at >= 0) _messages.Children.Insert(at, c);
        else _messages.Children.Add(c);
        Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void AddLine(string text, bool error) => AddItem(new SelectableTextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = error ? ErrorBrush : AppTheme.DimTextBrush,
        FontSize = AppTheme.SmallFontSize,
    });

    /// <summary>사용자 말풍선: 글 사이 그림 칩(작은 미리보기 + 번호, 누르면 크게 보기)은 AI 채팅 창과 같은 모양.</summary>
    private static Control UserBubble(string text, IReadOnlyList<ChatAttachment> attachments) => new Border
    {
        Background = UserBubbleBrush,
        CornerRadius = new CornerRadius(6),
        Margin = new Thickness(24, 2, 0, 2),
        HorizontalAlignment = HorizontalAlignment.Right,
        Child = ChatImages.BubbleText(text, [.. attachments.Select(a => (Bitmap?)a.Thumb)]),
    };

    private static MarkdownView Answer(string text)
    {
        var view = new MarkdownView(MonoFont, OpenLink);
        view.SetText(text);
        return view;
    }

    private void BeginStream()
    {
        FinishStream();
        _stream = new MarkdownView(MonoFont, OpenLink);
        _streamText.Clear();
        AddItem(_stream);
    }

    /// <summary>스트리밍 글을 모아 한 번에 그린다(조각마다 그리면 긴 답에서 느려짐).</summary>
    private void ScheduleStreamRender()
    {
        if (_renderPending) return;
        _renderPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _renderPending = false;
            _stream?.SetText(_streamText.ToString());
        }, DispatcherPriority.Background);
    }

    private void FinishStream()
    {
        if (_stream is { } view && _streamText.Length > 0) view.SetText(_streamText.ToString());
        _stream = null;
        _streamText.Clear();
    }

    private static void OpenLink(string url) => AiCli.OpenUrl(url);

    private static string Seconds(TimeSpan elapsed)
        => elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + UiStrings.S_ChatWindow_EndTurn_7EEAC40074;
}
