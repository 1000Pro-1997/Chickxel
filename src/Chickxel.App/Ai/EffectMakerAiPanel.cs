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
/// 대화는 차례가 끝날 때마다 AI별로 저장되고(<see cref="ChatHistoryStore"/> — AI 채팅과 따로 <c>EffectMakerChats</c> 폴더), 칸을 열면 마지막으로 보던 대화를 이어 가며,
/// "새 대화" 화면은 안내 아래에 지난 대화 목록(누르면 이어 감)을 보인다. CLI 설치·로그인은 AI 채팅 창에 맡긴다(이 칸은 안내와 "AI 채팅 열기"·"다시 확인"만).
/// </summary>
internal sealed class EffectMakerAiPanel : Border
{
    private static readonly IBrush ErrorBrush = AppTheme.Brush(Color.FromRgb(240, 110, 110));
    private static readonly IBrush UserBubbleBrush = AppTheme.Brush(Color.FromRgb(50, 66, 88));
    private static readonly FontFamily MonoFont = new("Menlo, Consolas, DejaVu Sans Mono, monospace");
    /// <summary>이 칸의 대화 보관소(AI 채팅의 <c>Chats</c>와 따로).</summary>
    private static readonly ChatHistoryStore s_store = new(Path.Combine(Platform.AppFolder, "EffectMakerChats"));

    private readonly McpServer _mcp;
    private readonly AppSettings _settings;
    private readonly Action _openChat;
    private readonly Func<PixelImage?> _canvasImage, _previewFrame;
    private readonly TabStrip _providers;
    /// <summary>모델 칸(보내기 왼쪽). AI 채팅 창과 같은 설정 값(<see cref="AiProvider.GetModel"/>)을 읽고 쓴다.</summary>
    private readonly ComboBox _model;
    /// <summary>모델 칸(<see cref="_model"/>)의 목록·고르기·설정 저장.</summary>
    private readonly ChatModelSelector _models;
    private readonly TextBlock _status;
    private readonly Button _newChat;
    private readonly Border _body = new();
    private readonly DockPanel _chatPanel;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _messages = new() { Spacing = 6, Margin = new Thickness(8, 8) };
    /// <summary>새 대화 화면의 지난 대화 목록(안내 바로 아래, 대화를 시작하거나 열면 뺌).</summary>
    private readonly StackPanel _history = new() { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _thinking;
    private readonly ChatImageInput _imageInput;
    /// <summary>차례가 도는 동안 보낸 메시지(입력란 위 대기 줄) — 차례가 끝나면 차례로 보낸다.</summary>
    private readonly PendingMessages _pending;
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
    /// <summary>도는 도구 호출 id → 도구 줄·저장할 줄·시작 시각.</summary>
    private readonly Dictionary<string, (TextBlock Line, ChatItem Item, long Started)> _tools = new();
    /// <summary>지금 대화(저장 전 새 대화면 null — 첫 메시지를 보낼 때 만든다)와 저장할 줄.</summary>
    private SavedChat? _chat;
    private List<ChatItem> _items = new();

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
        // 대상 도움말(F1): 칸 = "효과 만들기"의 AI 칸 절, 머리줄 = 그 항목, 대화·입력 = ㉖, 첨부 단추 = ㉗, 모델·보내기 = ㉘, 대기 줄 = 그 항목
        const string page = "screen-effect-maker-ai";
        this.HelpAt(page, anchor: "AI panel");

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
        }.HelpAt(page, anchor: "The header has the Claude/Codex switch");
        DockPanel.SetDock(header, Dock.Top);

        // ── 채팅 화면: 대화(안내 글로 시작) + 그림 칩 입력란·첨부 단추·보내기/멈춤
        _scroll = Ui.ScrollHost(_messages).HelpAt(page, anchor: "Send the effect you want");
        _thinking = new TextBlock { Foreground = AppTheme.DimTextBrush, FontStyle = FontStyle.Italic, Margin = new Thickness(2), TextWrapping = TextWrapping.Wrap };
        _imageInput = new ChatImageInput(UiStrings.EffectMakerAi_Watermark).HelpAt(page, anchor: "Send the effect you want");
        _imageInput.Input.AddHandler(InputElement.KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        _pending = new PendingMessages(_imageInput).HelpAt(page, anchor: "Pressing Enter or Send while the AI's turn is running");
        // 지금 보내기: 도는 차례를 멈추면 차례 끝에서 맨 앞 메시지가 나간다. 도는 차례가 없으면 바로.
        _pending.SendNowRequested += () =>
        {
            if (_running) StopTurn();
            else SendQueued();
        };
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
        _models = new ChatModelSelector(_model, settings, () => _provider, OnModelChanged);
        var sendButtons = Ui.Row(_send, _stop).HelpAt(page, anchor: "The model list left of Send");
        var attachButtons = Ui.Row(attachFile, attachCanvas, attachPreview).HelpAt(page, anchor: "The three buttons under the input attach");
        // 모델 칸은 남는 폭만 쓴다(좁으면 이름 말줄임 — 차례 중에 보내기·멈춤이 함께 보여도 단추가 밀리지 않게).
        _model.HorizontalAlignment = HorizontalAlignment.Right;
        _model.HelpAt(page, anchor: "The model list left of Send");
        var buttons = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(sendButtons, Dock.Right);
        DockPanel.SetDock(attachButtons, Dock.Left);
        buttons.Children.Add(sendButtons);
        buttons.Children.Add(attachButtons);
        buttons.Children.Add(_model);
        var inputArea = new Border
        {
            Background = AppTheme.BackBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(6),
            Child = new StackPanel { Children = { _pending, _imageInput, buttons } },
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
            if (_session is null)
            {
                AttachSession(provider.CreateSession(path, _mcp, () => AiCli.EffectMakerSystemPrompt, McpScope.EffectMaker));
                // 열어 둔 대화가 있으면 그 CLI 세션을 이어 가고, 없으면 이 AI에서 마지막으로 보던 대화를 연다
                if (_chat is not null) ResumeChat(_chat);
                else if (_settings.EffectMakerChatLastOpen?.GetValueOrDefault(provider.Id) is { } last && s_store.Load(provider.Id, last) is { } saved) OpenChat(saved);
            }
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
        _models.Fill();
        _chat = null;
        _items = new List<ChatItem>();
        ClearTranscript();
        _started = true;
        _ = CheckAsync();
    }

    /// <summary>모델을 바꿈(설정에는 이미 남음): 세션이 있으면 같은 대화를 이어 다음 메시지부터 새 모델로 띄운다.</summary>
    private void OnModelChanged(string? model, string notice)
    {
        if (_session is null) return;
        StopTurn();
        _session.Model = model;
        _session.Restart();
        AddLine(notice, error: false, record: false);
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
            Record(new ChatItem { Kind = ChatItemKind.Assistant, Text = text });
        };
        s.ToolUse += info =>
        {
            if (!_running || !ReferenceEquals(s, _session)) return;
            FinishStream();
            var line = new TextBlock { Text = $"{info.Name}", Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextWrapping = TextWrapping.Wrap };
            var item = new ChatItem { Kind = ChatItemKind.Tool, Text = info.Name };
            Record(item);
            if (info.Id.Length > 0) _tools[info.Id] = (line, item, Stopwatch.GetTimestamp());
            AddItem(ToolRow(line));
        };
        s.ToolResult += info =>
        {
            if (!ReferenceEquals(s, _session) || !_tools.Remove(info.ToolUseId, out var tool)) return;
            if (info.IsError)
            {
                tool.Line.Foreground = ErrorBrush;
                if (info.Text.Length > 0) tool.Line.Text += $" - {info.Text}";
            }
            tool.Line.Text += $" ({ChatTurnFooter.Seconds(Stopwatch.GetElapsedTime(tool.Started).TotalSeconds)})";
            tool.Item.Text = tool.Line.Text;
            tool.Item.Error = info.IsError;
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
            if (ReferenceEquals(s, _session) && _provider.RememberResolved(_settings, s.Model, model)) _models.Fill();
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

    /// <summary>입력란의 메시지를 보낸다. 차례가 도는 중이면 대기 줄에 넣고 차례가 끝나면 보낸다.</summary>
    private void Send()
    {
        if (_session is null || _shutdown) return;
        if (_running)
        {
            _pending.AddFromInput();
            return;
        }
        var text = _imageInput.StoredText();
        var attachments = _imageInput.Attachments.ToList();
        if (text.Length == 0 && attachments.Count == 0) return;
        if (SendMessage(text, attachments)) _imageInput.Clear();
    }

    /// <summary>차례가 끝났으면 대기 줄의 다음 메시지를 보낸다(보내지 못하면 입력란이 비어 있을 때 그리로 되돌림).</summary>
    private void SendQueued()
    {
        if (_running || _session is null || _shutdown || _pending.TakeNext() is not { } next) return;
        if (!SendMessage(next.Text, next.Attachments) && _imageInput.StoredText().Length == 0 && _imageInput.Attachments.Count == 0)
            _imageInput.Restore(next.Text, next.Attachments);
    }

    /// <summary>메시지 하나로 새 차례를 연다. 보냈으면 true.</summary>
    private bool SendMessage(string text, IReadOnlyList<ChatAttachment> attachments)
    {
        if (_session is not { } session) return false;
        // 설정에서 CLI 경로를 바꿨으면 다시 확인한다(보내지 않음 — AI 채팅처럼 알리고, 확인 뒤 다시 보냄).
        if (_cliPath is not null && _cliPath != _provider.Locate(_settings))
        {
            AddLine(UiStrings.Chat_CliPathChanged, error: false);
            _ = CheckAsync();
            return false;
        }
        session.Model = _models.Current;
        var modelText = ChatImageTokens.ToModel(text);
        var message = _primed ? modelText : AiCli.EffectMakerGuidePrefix() + modelText;
        try
        {
            session.Send(message, attachments.Select(a => a.Png).ToList());
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            AddLine(string.Format(CultureInfo.CurrentCulture, UiStrings.S_ChatWindow_Send_E1ACACFBF7, _provider.Command, ex.Message), error: true);
            return false;
        }
        _primed = true;
        if (_chat is null)
        {
            var now = DateTime.UtcNow;
            _chat = new SavedChat
            {
                Id = ChatHistoryStore.NewId(),
                Provider = _provider.Id,
                Title = ChatImageTokens.WithoutTokens(text).Length > 0 ? ChatHistoryStore.MakeTitle(ChatImageTokens.WithoutTokens(text)) : UiStrings.Chat_History_ImageTitle,
                CreatedUtc = now,
                UpdatedUtc = now,
            };
            RememberOpen(_chat.Id);
        }
        _messages.Children.Remove(_history);
        AddItem(UserBubble(text, [.. attachments.Select(a => (Bitmap?)a.Thumb)]));
        Record(new ChatItem
        {
            Kind = ChatItemKind.User,
            Text = text,
            ImageCount = attachments.Count,
            // 칩 번호와 맞도록 미리보기를 만들지 못한 그림은 빈 글로 자리를 지킨다
            Images = attachments.Count == 0 ? null : [.. attachments.Select(a => ChatHistoryViews.SavedThumb(a) ?? "")],
        });
        _running = true;
        _tools.Clear();
        _turnClock.Restart();
        _messages.Children.Add(_thinking);
        UpdateThinking();
        _clock.Start();
        UpdateInputState();
        return true;
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
        var footer = ChatTurnFooter.Text(cancelled, result?.CostUsd ?? 0, _turnClock.Elapsed.TotalSeconds);
        AddItem(FooterLine(footer));
        Record(new ChatItem { Kind = ChatItemKind.Footer, Text = footer });
        SaveChat();
        UpdateInputState();
        // 기다리던 메시지는 이 차례를 다 그린 뒤에 보낸다(멈춤으로 끝났어도 — 방향을 바꾸려고 멈춘 경우가 많음).
        if (_pending.Count > 0) Dispatcher.UIThread.Post(SendQueued);
    }

    /// <summary>
    /// 새 대화: 도는 차례를 멈추고 세션의 대화를 잊는다(다음 메시지에 규칙·API를 다시 붙임). 지금 대화는 저장된 채로 두고 지난 대화 목록을 보인다. 편집기 원문은 그대로.
    /// </summary>
    private void NewConversation()
    {
        _pending.ClearAll();
        StopTurn();
        _session?.Reset();
        _primed = false;
        _chat = null;
        _items = new List<ChatItem>();
        RememberOpen(null);
        ClearTranscript();
        FocusInput();
    }

    // ───────────── 저장한 대화 ─────────────

    private void Record(ChatItem item) => _items.Add(item);

    /// <summary>지금 대화를 저장한다(세션 ID·작업 폴더·모델·고친 시각 포함). 넘친 옛 대화는 보관소가 지운다.</summary>
    private void SaveChat()
    {
        if (_chat is not { } chat) return;
        if (_session is { } s)
        {
            chat.Model = s.Model;
            chat.SessionId = s.SessionId;
            chat.SessionDirectory = s.SessionDirectory;
        }
        chat.UpdatedUtc = DateTime.UtcNow;
        chat.Items = _items;
        try
        {
            s_store.Save(chat);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AddLine(UiStrings.FormatChat_History_SaveFailed(message: ex.Message), error: true, record: false);
        }
    }

    /// <summary>이 AI에서 마지막으로 보던 대화를 설정에 적는다(null = 새 대화).</summary>
    private void RememberOpen(string? id)
    {
        var map = _settings.EffectMakerChatLastOpen ??= new Dictionary<string, string>();
        if (id is null) map.Remove(_provider.Id);
        else map[_provider.Id] = id;
        _settings.Save();
    }

    /// <summary>
    /// 저장한 대화를 연다: 줄을 다시 그리고, 모델을 그 대화 것으로 고르고, 세션이 있으면 그 CLI 세션을 같은 작업 폴더에서 이어 가게 한다
    /// (세션 ID가 없으면 다음 메시지는 규칙·API를 붙인 새 대화).
    /// </summary>
    private void OpenChat(SavedChat chat)
    {
        _pending.ClearAll();
        StopTurn();
        _chat = chat;
        _items = chat.Items;
        _stream = null;
        _streamText.Clear();
        _tools.Clear();
        _messages.Children.Clear();
        AddIntro(history: false);
        foreach (var item in chat.Items) RenderSaved(item);
        _models.Select(chat.Model);
        ResumeChat(chat);
        AddLine(chat.SessionId is null ? UiStrings.Chat_History_NoSession : UiStrings.EffectMakerAi_HistoryOpened, error: false, record: false);
        RememberOpen(chat.Id);
        FocusInput();
    }

    /// <summary>세션이 <paramref name="chat"/>의 CLI 세션을 이어 가게 한다(세션 ID가 없으면 새 대화 — 다음 메시지에 규칙·API를 붙임).</summary>
    private void ResumeChat(SavedChat chat)
    {
        if (_session is not { } s) return;
        s.Model = _models.Current;
        if (chat.SessionId is { } id) s.Resume(id, chat.SessionDirectory);
        else s.Reset();
        _primed = chat.SessionId is not null;
    }

    /// <summary>저장한 줄 하나를 그린다(이 칸이 남기는 종류만).</summary>
    private void RenderSaved(ChatItem item)
    {
        switch (item.Kind)
        {
            case ChatItemKind.User:
                AddItem(UserBubble(item.Text, [.. (item.Images ?? []).Select(data => data.Length > 0 ? ChatHistoryViews.DecodeImage(data) : null)]));
                break;
            case ChatItemKind.Assistant:
                AddItem(Answer(item.Text));
                break;
            case ChatItemKind.Tool:
                AddItem(ToolRow(new TextBlock
                {
                    Text = item.Text, Foreground = item.Error ? ErrorBrush : AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextWrapping = TextWrapping.Wrap,
                }));
                break;
            case ChatItemKind.Notice:
            case ChatItemKind.Error:
                AddLine(item.Text, item.Kind == ChatItemKind.Error, record: false);
                break;
            case ChatItemKind.Footer:
                AddItem(FooterLine(item.Text));
                break;
        }
    }

    /// <summary>새 대화 화면의 지난 대화 목록을 다시 그린다(즐겨찾기 먼저, 그다음 최근에 고친 순 — 없으면 비움).</summary>
    private void RefreshHistory()
    {
        _history.Children.Clear();
        List<ChatSummary> chats;
        try
        {
            chats = s_store.List(_provider.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            chats = [];
        }
        if (chats.Count == 0) return;
        _history.Children.Add(new TextBlock { Text = UiStrings.Chat_History_Title, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, Margin = new Thickness(2, 0, 0, 2) });
        foreach (var c in chats)
            _history.Children.Add(ChatHistoryViews.Row(c, ModelLabel(c.Model), () => OpenSaved(c.Id), () => ToggleFavorite(c), () => DeleteSaved(c), helpPage: null));
    }

    private void OpenSaved(string id)
    {
        if (s_store.Load(_provider.Id, id) is { } chat) OpenChat(chat);
        else RefreshHistory();
    }

    private void ToggleFavorite(ChatSummary c)
    {
        try
        {
            s_store.SetFavorite(_provider.Id, c.Id, !c.Favorite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, UiStrings.FormatChat_History_SaveFailed(message: ex.Message));
        }
        RefreshHistory();
    }

    private void DeleteSaved(ChatSummary c)
    {
        var title = c.Title.Length > 0 ? c.Title : UiStrings.Chat_History_ImageTitle;
        if (Ui.Ask(this, UiStrings.FormatChat_History_DeleteAsk(title: title), UiStrings.Chat_History_DeleteTitle, MessageButtons.YesNo, MessageIcon.Question)
            != DialogResult.Yes) return;
        s_store.Delete(_provider.Id, c.Id);
        RefreshHistory();
    }

    /// <summary>모델 값(null = 기본값)의 목록 이름.</summary>
    private string ModelLabel(string? model)
        => _models.Choices.FirstOrDefault(m => m.Value == model)?.Label ?? model ?? _models.Choices.FirstOrDefault()?.Label ?? "";

    private void UpdateInputState()
    {
        bool ready = _session is not null && !_shutdown;
        // 도는 동안에도 보내기를 둔다(대기 줄에 넣음 — 좁은 칸이라 아이콘만).
        _stop.IsVisible = _running;
        _send.Content = _running ? Ui.Icon(VectorIcon.ArrowUp) : Ui.IconLabel(VectorIcon.ArrowUp, UiStrings.S_ChatWindow_ChatTab_CF5843D1B0);
        _send.Tip(_running ? UiStrings.Chat_SendQueuesTip : "Enter");
        _send.IsEnabled = ready;
        _newChat.IsEnabled = ready;
    }

    private void UpdateThinking()
        => _thinking.Text = UiStrings.FormatEffectMakerAi_Working(seconds: ((int)_turnClock.Elapsed.TotalSeconds).ToString(CultureInfo.CurrentCulture));

    private void FocusInput() => _imageInput.FocusInput();

    // ───────────── 대화 그리기 ─────────────

    /// <summary>머리 안내 글. <paramref name="history"/>면 그 아래에 지난 대화 목록(새 대화 화면).</summary>
    private void AddIntro(bool history = true)
    {
        _messages.Children.Add(new TextBlock
        {
            Text = UiStrings.EffectMakerAi_Intro,
            TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.DimTextBrush,
            Margin = new Thickness(2, 0, 2, 4),
        });
        if (!history) return;
        RefreshHistory();
        _messages.Children.Add(_history);
    }

    /// <summary>대화 화면을 새 대화 화면(안내 + 지난 대화 목록)으로 비운다(지금 대화·세션은 부르는 쪽이 정함).</summary>
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

    private void AddLine(string text, bool error, bool record = true)
    {
        AddItem(new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = error ? ErrorBrush : AppTheme.DimTextBrush,
            FontSize = AppTheme.SmallFontSize,
        });
        if (record) Record(new ChatItem { Kind = error ? ChatItemKind.Error : ChatItemKind.Notice, Text = text });
    }

    private static Control ToolRow(TextBlock line) => Ui.Row(Ui.Icon(VectorIcon.Code, 11), line);

    private static TextBlock FooterLine(string text)
        => new() { Text = text, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, HorizontalAlignment = HorizontalAlignment.Right };

    /// <summary>사용자 말풍선: 글 사이 그림 칩(작은 미리보기 + 번호, 누르면 크게 보기)은 AI 채팅 창과 같은 모양(저장한 대화는 미리보기를 못 읽은 그림이 null).</summary>
    private static Control UserBubble(string text, IReadOnlyList<Bitmap?> images) => new Border
    {
        Background = UserBubbleBrush,
        CornerRadius = new CornerRadius(6),
        Margin = new Thickness(24, 2, 0, 2),
        HorizontalAlignment = HorizontalAlignment.Right,
        Child = ChatImages.BubbleText(text, images),
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
}
