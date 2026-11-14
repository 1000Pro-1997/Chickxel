using Chickxel.Core.AiDraw;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

// 그리기 설정 칸(머리줄 "?" 옆 슬라이더 단추): 도움말 칸과 같은 오른쪽 자리·움직임을 쓰고, 둘 중 하나만 열린다.
// 자동(Codex: "지금 그림만"이면 직접 찍기, 아니면 그림 생성 먼저·안 되면 직접 찍기) / 그림 생성 → 픽셀 변환 / 직접 찍기.
// 자동 애니메이션은 GIF 프레임을 만들어 변환하므로 수동 생성 방식 선택은 그림 생성 모드에서만 쓴다.
public sealed partial class ChatWindow
{
    /// <summary>결과 크기 칸의 고정 값(마지막 칸은 직접 입력).</summary>
    private static readonly int[] SizeChoices = [32, 48, 64];

    private RadioButton? _modeAuto, _modeGenerate, _modeDirect;
    private Border? _modeWarning;
    private TextBlock? _codexStatus;
    private Control? _generateOptions;
    private ComboBox? _sizeCombo, _styleCombo, _styleRefCombo, _insertCombo, _animCombo;
    /// <summary>"프레임마다 따로"를 고르면 보이는 일관성 경고 아이콘.</summary>
    private Control? _animWarning;
    private NumericUpDown? _sizeCustom;
    private CheckBox? _removeBackground, _autoOptimize, _paletteOnly, _resizeResult, _autoPrompt;
    /// <summary>"생성 요청 다듬기"(자동 프롬프트)를 끄면 쓰이지 않는 칸(스타일 참고, 애니메이션 생성 방식, 기본 프레임 수).</summary>
    private readonly List<Control> _autoPromptRows = [];
    /// <summary>결과 크기 칸(결과 크기 보정이 켜졌을 때만 쓸 수 있음).</summary>
    private Control? _sizeRow;
    /// <summary>팔레트 색만 사용 줄(직접 찍기를 골랐을 때만 보임).</summary>
    private Control? _paletteRow;
    private readonly Dictionary<string, NumericUpDown> _frameBoxes = new();
    /// <summary>스타일 참고 "지정" 목록의 자료 id(목록 순서).</summary>
    private List<string> _styleRefIds = [];
    /// <summary>설정 칸 값을 채우는 중(바뀜 이벤트로 설정을 고치지 않게).</summary>
    private bool _drawFilling;
    private bool _codexChecking;
    /// <summary>그리기 프롬프트·도구 묶음 켜고 끄기(<see cref="AppSettings.AiChatAssist"/>).</summary>
    private AiAssistView? _assistView;
    /// <summary>"생성 요청 다듬기"를 끈 Codex 그림 생성이라 AI 그리기 요령이 쓰이지 않는다는 안내.</summary>
    private TextBlock? _guidesUnused;

    /// <summary>그림 생성 진행·결과는 차례가 도는 탭에 붙인다(차례가 없으면 버림 — 멈춘 뒤 늦게 온 것).</summary>
    private void OnAiDrawProgress(string text) => RunningTab?.OnAiDrawProgress(text);
    private void OnAiDrawPromptReady(string instruction, int index, int count) => RunningTab?.OnAiDrawPromptReady(instruction, index, count);
    private void OnAiDrawPreview(PixelImage frame) => RunningTab?.OnAiDrawPreview(frame);

    private void OnAiDrawCompleted(AiDrawOutcome outcome) => RunningTab?.OnAiDrawCompleted(outcome);

    /// <summary>도움말·설정 단추가 켜지고 꺼짐: 하나를 켜면 다른 하나를 끄고 칸 내용만 바꾼다(열려 있으면 그대로, 닫혀 있으면 연다). 켜진 단추를 끄면 닫는다.</summary>
    private void OnSidePanelToggled(ToggleButton source)
    {
        if (_panelSyncing) return;
        var other = ReferenceEquals(source, _help) ? _drawSettings : _help;
        if (source.IsChecked == true)
        {
            if (other.IsChecked == true)
            {
                _panelSyncing = true;
                other.IsChecked = false;
                _panelSyncing = false;
            }
            bool help = ReferenceEquals(source, _help);
            _helpPanel.IsVisible = help;
            _drawPanel.IsVisible = !help;
            if (!help) RefreshDrawSettings(checkCodex: true);
            AnimateHelp(true);
        }
        else if (other.IsChecked != true) AnimateHelp(false);
        UpdateDrawSettingsButton();
    }

    /// <summary>설정 칸이 열려 있는 동안은 빨간 X(정리 창의 설정 단추처럼), 닫히면 슬라이더 아이콘.</summary>
    private void UpdateDrawSettingsButton()
    {
        bool open = _drawSettings.IsChecked == true;
        _drawSettingsIcon.Icon = open ? VectorIcon.X : VectorIcon.SlidersHorizontal;
        if (open) _drawSettingsIcon.Foreground = AppTheme.UnsavedBrush;
        else _drawSettingsIcon.ClearValue(IconView.ForegroundProperty);
        _drawSettings.Tip(open ? UiStrings.AiDraw_Settings_Close : UiStrings.AiDraw_Settings_Tip);
    }

    /// <summary>설정 칸: 머리줄(제목 + 닫기) 아래 내용 스크롤(도움말 칸과 같은 너비).</summary>
    private Control BuildDrawSettingsPanel()
    {
        var title = new TextBlock
        {
            Text = UiStrings.AiDraw_Settings_Title,
            FontWeight = FontWeight.Bold,
            Foreground = AppTheme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 0),
        };
        var close = Ui.IconButton(VectorIcon.X, UiStrings.AiDraw_Settings_Close, (_, _) => _drawSettings.IsChecked = false, 28, 12);
        close.Margin = new Thickness(0, 0, 2, 0);
        Grid.SetColumn(close, 1);
        var header = new Border
        {
            Background = AppTheme.BackBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid { Margin = new Thickness(4, 3), ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { title, close } },
        };
        DockPanel.SetDock(header, Dock.Top);

        // 대상 도움말(F1): 칸 전체는 "그리기 설정 칸" 절, 줄마다 그것을 설명하는 목록 항목·생성 옵션 표의 줄로(정보 아이콘은 InfoIcon이 정함).
        const string page = "ai-generate", modeAnchor = "Drawing mode";
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(12, 10) };
        stack.Children.Add(TitleText(UiStrings.AiDraw_Mode_Title).HelpAt(page, anchor: modeAnchor));
        _modeAuto = new RadioButton { Content = UiStrings.AiDraw_Mode_Auto, GroupName = "aidraw-mode", Foreground = AppTheme.TextBrush };
        _modeGenerate = new RadioButton { Content = UiStrings.AiDraw_Mode_Generate, GroupName = "aidraw-mode", Foreground = AppTheme.TextBrush };
        _modeDirect = new RadioButton { Content = UiStrings.AiDraw_Mode_Direct, GroupName = "aidraw-mode", Foreground = AppTheme.TextBrush };
        _modeWarning = AlertIcon(UiStrings.AiDraw_Mode_ClaudeWarning).HelpAt(page, anchor: "In the Claude tab");
        _modeAuto.IsCheckedChanged += (_, _) => OnDrawModeChanged();
        _modeGenerate.IsCheckedChanged += (_, _) => OnDrawModeChanged();
        _modeDirect.IsCheckedChanged += (_, _) => OnDrawModeChanged();
        stack.Children.Add(WithInfo(_modeAuto, UiStrings.AiDraw_Mode_Auto_Text).HelpAt(page, anchor: modeAnchor));
        stack.Children.Add(WithInfo(_modeGenerate, UiStrings.AiDraw_Mode_Generate_Text, _modeWarning).HelpAt(page, anchor: modeAnchor));
        stack.Children.Add(WithInfo(_modeDirect, UiStrings.AiDraw_Mode_Direct_Text).HelpAt(page, anchor: modeAnchor));
        // 직접 찍기를 골랐을 때만: 팔레트 색만 사용.
        _paletteOnly = Ui.Check(UiStrings.AiDraw_PaletteOnly);
        _paletteOnly.IsCheckedChanged += (_, _) => OnDrawOptionChanged();
        _paletteRow = WithInfo(_paletteOnly, UiStrings.AiDraw_PaletteOnly_Text).HelpAt(page, anchor: "Use palette colors only");
        _paletteRow.Margin = new Thickness(22, 0, 0, 0);
        stack.Children.Add(_paletteRow);
        // AI 그리기 요령: 대충 말해도 잘 나오게 시스템 프롬프트에 붙이는 작업별 지시(켜고 끄기·글 고치기). 모든 방식에 쓰되,
        // Codex 그림 생성에서 "생성 요청 다듬기"를 끄면 내 말이 그대로 생성에 가서 쓰이지 않으므로 흐리게 하고 까닭을 보인다.
        _assistView = new AiAssistView(() => _settings.AiChatAssist, () => _settings.AiGuideTexts, McpScope.Full, t => InfoIcon(t), OnAssistChanged);
        stack.Children.Add(new Border { Height = 1, Background = AppTheme.BorderBrush, Margin = new Thickness(0, 4) });
        stack.Children.Add(WithInfo(TitleText(UiStrings.AiGuide_Title), UiStrings.AiGuide_Title_Tip).HelpAt(page, anchor: "The drawing settings panel"));
        _guidesUnused = WrapText(UiStrings.AiGuide_UnusedByGenerate, dim: true);
        stack.Children.Add(_guidesUnused);
        stack.Children.Add(_assistView.Guides.HelpAt(page, anchor: "The drawing settings panel"));
        _codexStatus = WrapText("", dim: true).HelpAt(page, anchor: "Drawing settings");
        // 그림 생성을 골랐을 때만: 생성 옵션.
        var options = new StackPanel { Spacing = 6 };
        options.Children.Add(new Border { Height = 1, Background = AppTheme.BorderBrush, Margin = new Thickness(0, 4) });
        options.Children.Add(TitleText(UiStrings.AiDraw_Options_Title).HelpAt(page, anchor: "Generation options"));

        _autoPrompt = Ui.Check(UiStrings.AiDraw_AutoPrompt);
        _autoPrompt.IsCheckedChanged += (_, _) => OnDrawOptionChanged();
        options.Children.Add(WithInfo(_autoPrompt, UiStrings.AiDraw_AutoPrompt_Text).HelpAt(page, anchor: "Automatic prompt"));

        _resizeResult = Ui.Check(UiStrings.AiDraw_ResizeResult);
        _resizeResult.IsCheckedChanged += (_, _) => OnDrawOptionChanged();
        options.Children.Add(WithInfo(_resizeResult, UiStrings.AiDraw_ResizeResult_Text).HelpAt(page, anchor: "Result-size correction"));
        _sizeCombo = Ui.Combo(120, "32", "48", "64", UiStrings.AiDraw_Size_Custom);
        _sizeCustom = Ui.Num(AiDrawSettings.MinSize, AiDrawSettings.MaxSize, 32, 64);
        _sizeCombo.SelectionChanged += (_, _) => OnDrawOptionChanged();
        _sizeCustom.ValueChanged += (_, _) => OnDrawOptionChanged();
        _sizeRow = OptionRow(UiStrings.AiDraw_Size, UiStrings.AiDraw_Size_Tip, Ui.Row(_sizeCombo, _sizeCustom)).HelpAt(page, anchor: "Result size");
        _sizeRow.Margin = new Thickness(22, 0, 0, 0);
        options.Children.Add(_sizeRow);

        _removeBackground = Ui.Check(UiStrings.AiDraw_RemoveBackground);
        _removeBackground.IsCheckedChanged += (_, _) => OnDrawOptionChanged();
        options.Children.Add(WithInfo(_removeBackground, UiStrings.AiDraw_RemoveBackground_Tip).HelpAt(page, anchor: "Remove background"));
        _autoOptimize = Ui.Check(UiStrings.AiDraw_AutoOptimize);
        _autoOptimize.IsCheckedChanged += (_, _) => OnDrawOptionChanged();
        options.Children.Add(WithInfo(_autoOptimize, UiStrings.AiDraw_AutoOptimize_Tip).HelpAt(page, anchor: "Auto-optimize the conversion"));

        _insertCombo = Ui.Combo(150, UiStrings.AiDraw_Insert_Layer, UiStrings.AiDraw_Insert_Document);
        _insertCombo.SelectionChanged += (_, _) => OnDrawOptionChanged();
        options.Children.Add(OptionRow(UiStrings.AiDraw_Insert, null, _insertCombo).HelpAt(page, anchor: "Insert as"));

        _styleCombo = Ui.Combo(150, UiStrings.AiDraw_Style_Auto, UiStrings.AiDraw_Style_None, UiStrings.AiDraw_Style_Specific);
        _styleCombo.SelectionChanged += (_, _) => OnDrawOptionChanged();
        var styleRow = OptionRow(UiStrings.AiDraw_Style, UiStrings.AiDraw_Style_Text, _styleCombo).HelpAt(page, anchor: "Style reference");
        options.Children.Add(styleRow);
        _styleRefCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(3) }.HelpAt(page, anchor: "Style reference");
        _styleRefCombo.SelectionChanged += (_, _) => OnDrawOptionChanged();
        options.Children.Add(_styleRefCombo);

        var framesTitle = new TextBlock { Text = UiStrings.AiDraw_Frames, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center };
        var framesHeader = WithInfo(framesTitle, UiStrings.AiDraw_Frames_Text).HelpAt(page, anchor: "Default animation frames");
        framesHeader.Margin = new Thickness(0, 4, 0, 0);
        options.Children.Add(framesHeader);
        _animCombo = Ui.Combo(170, UiStrings.AiDraw_Anim_Sheet, UiStrings.AiDraw_Anim_PerFrame);
        _animCombo.SelectionChanged += (_, _) => OnDrawOptionChanged();
        // "프레임마다 따로"를 고르면 콤보 옆에 빨간 경고 아이콘(마우스를 올리면 까닭).
        _animWarning = AlertIcon(UiStrings.AiDraw_Anim_PerFrame_Warning);
        var animRow = OptionRow(UiStrings.AiDraw_Anim, UiStrings.AiDraw_Anim_Tip, Ui.Row(_animCombo, _animWarning)).HelpAt(page, anchor: "Generation");
        options.Children.Add(animRow);
        var frames = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto") }.HelpAt(page, anchor: "Default animation frames");
        int row = 0, col = 0;
        foreach (var action in AiDrawActions.All)
        {
            if (col == 0) frames.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = ActionName(action), Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            var box = Ui.Num(AiDrawSettings.MinFrames, AiDrawSettings.MaxFrames, AiDrawActions.DefaultFrames(action), 56);
            box.ValueChanged += (_, _) => OnDrawOptionChanged();
            _frameBoxes[action] = box;
            Grid.SetRow(label, row);
            Grid.SetColumn(label, col * 2);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, col * 2 + 1);
            frames.Children.Add(label);
            frames.Children.Add(box);
            if (++col == 2) { col = 0; row++; }
        }
        options.Children.Add(frames);
        _autoPromptRows.AddRange([styleRow, _styleRefCombo, framesHeader, animRow, frames]);
        _generateOptions = options;
        stack.Children.Add(options);
        // 쓸 수 있는 도구: 끈 묶음은 MCP 서버가 목록에서 빼고 호출도 거부한다.
        stack.Children.Add(new Border { Height = 1, Background = AppTheme.BorderBrush, Margin = new Thickness(0, 4) });
        stack.Children.Add(WithInfo(TitleText(UiStrings.AiTools_Title), UiStrings.AiTools_Title_Tip).HelpAt(page, anchor: "The drawing settings panel"));
        stack.Children.Add(_assistView.Tools.HelpAt(page, anchor: "The drawing settings panel"));

        var scroll = new ScrollViewer
        {
            Background = AppTheme.PanelBrush,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack,
        };
        return new DockPanel
        {
            Width = HelpPanelWidth - 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsVisible = false,
            Children = { header, scroll },
        }.HelpAt(page, anchor: "The drawing settings panel");
    }

    /// <summary>
    /// 선택지 한 줄: 컨트롤, 덧붙일 것(경고 아이콘 등), 흐린 정보 아이콘. 설명(<paramref name="tip"/>)은 정보 아이콘과 컨트롤 모두의 풍선 — 패널에는 한 줄 이름만 보인다.
    /// </summary>
    private static StackPanel WithInfo(Control control, string tip, params Control[] extra)
    {
        control.Tip(tip);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(control);
        row.Children.Add(InfoIcon(tip));
        foreach (var c in extra) row.Children.Add(c);
        return row;
    }

    /// <summary>설명 풍선을 띄우는 흐린 정보 아이콘(Lucide info).</summary>
    private static Border InfoIcon(string tip) => new Border
    {
        Width = 18,
        Height = 18,
        Margin = new Thickness(4, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Cursor = new Cursor(StandardCursorType.Help),
        Background = Brushes.Transparent,
        Child = new IconView(VectorIcon.Info, AppTheme.DimTextBrush, 2) { Width = 14, Height = 14 },
    }.Tip(tip).HelpAt("ai-generate", anchor: "Hover over an option");

    /// <summary>경고 풍선을 띄우는 빨간 경고 아이콘(Lucide circle-alert).</summary>
    private static Border AlertIcon(string tip) => new Border
    {
        Width = 18,
        Height = 18,
        Margin = new Thickness(4, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Cursor = new Cursor(StandardCursorType.Help),
        Background = Brushes.Transparent,
        Child = new IconView(VectorIcon.CircleAlert, AppTheme.UnsavedBrush, 2.2) { Width = 16, Height = 16 },
    }.Tip(tip);

    /// <summary>이름 + 컨트롤 한 줄. 설명이 있으면 이름 옆에 정보 아이콘, 이름에도 같은 풍선.</summary>
    private static Control OptionRow(string name, string? tip, Control control)
    {
        var label = new TextBlock { Text = name, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center };
        var head = tip is null ? (Control)label : WithInfo(label, tip);
        head.MinWidth = 100;
        head.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { head, control } };
    }

    private static string ActionName(string action) => action switch
    {
        AiDrawActions.Idle => UiStrings.AiDraw_Action_Idle,
        AiDrawActions.Walk => UiStrings.AiDraw_Action_Walk,
        AiDrawActions.Run => UiStrings.AiDraw_Action_Run,
        AiDrawActions.Attack => UiStrings.AiDraw_Action_Attack,
        AiDrawActions.Jump => UiStrings.AiDraw_Action_Jump,
        AiDrawActions.Hurt => UiStrings.AiDraw_Action_Hurt,
        AiDrawActions.Death => UiStrings.AiDraw_Action_Death,
        _ => action,
    };

    /// <summary>
    /// 설정 칸을 설정 값과 보이는 탭에 맞춘다(Claude 탭이면 생성 선택지 옆 경고). <paramref name="checkCodex"/>면 Codex 설치·로그인 상태를 다시 본다.
    /// </summary>
    private void RefreshDrawSettings(bool checkCodex = false)
    {
        if (_modeAuto is null || _drawPanel?.IsVisible != true) return;
        var o = _settings.AiDraw;
        _drawFilling = true;
        try
        {
            _modeAuto.IsChecked = o.Mode == AiDrawMode.Auto;
            _modeGenerate!.IsChecked = o.Mode == AiDrawMode.Generate;
            _modeDirect!.IsChecked = o.Mode == AiDrawMode.Direct;
            _modeWarning!.IsVisible = _visible.Provider.Id != AiDrawSettings.GeneratingProvider;
            int size = o.SizeTargetClamped;
            int index = Array.IndexOf(SizeChoices, size);
            _sizeCombo!.SelectedIndex = index >= 0 ? index : SizeChoices.Length;
            _sizeCustom!.Value = size;
            _resizeResult!.IsChecked = o.ResizeResult;
            _autoPrompt!.IsChecked = o.AutoPrompt;
            _removeBackground!.IsChecked = o.ForceRemoveBackground;
            _paletteOnly!.IsChecked = o.PaletteOnly;
            _autoOptimize!.IsChecked = o.AutoOptimize;
            _insertCombo!.SelectedIndex = o.Insert == AiDrawInsert.NewDocument ? 1 : 0;
            _styleCombo!.SelectedIndex = (int)o.StyleReference;
            _animCombo!.SelectedIndex = o.AnimationMethod == AiDrawAnimationMethod.PerFrame ? 1 : 0;
            FillStyleReferences(o.StyleReferenceId);
            foreach (var (action, box) in _frameBoxes) box.Value = o.FramesFor(action);
        }
        finally
        {
            _drawFilling = false;
        }
        UpdateDrawOptionsEnabled();
        _assistView?.Refresh();
        if (checkCodex) _ = CheckCodexAsync();
    }

    /// <summary>그리기 프롬프트·도구 묶음을 바꿈: 채팅의 도구 허락을 바로 바꾸고, 같이 쓰는 프롬프트 글이 바뀌었을 수 있어 외부 연결 안내도 맞춘다(대화는 다음에 보낼 때 다시 띄움).</summary>
    private void OnAssistChanged()
    {
        _mcp.SetChatTools(McpToolAccess.From(_settings.AiChatAssist));
        _mcp.UpdateExternalInstructions(AiCli.ExternalInstructions(_settings));
    }

    /// <summary>"지정" 목록: 참고 자료 보관소의 그림(사용자 자료 다음 기본 예시). 보관소를 열 수 없으면 비움.</summary>
    private void FillStyleReferences(string? selectedId)
    {
        _styleRefCombo!.Items.Clear();
        _styleRefIds = [];
        try
        {
            foreach (var item in _mcp.Tools.Host.References.AllItems.Where(i => i.IsImage))
            {
                _styleRefIds.Add(item.Id);
                _styleRefCombo.Items.Add($"{(item.Title.Length > 0 ? item.Title : item.Id)} ({item.Width}×{item.Height})");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 보관소를 열 수 없으면 "지정"은 고를 것이 없다.
        }
        _styleRefCombo.SelectedIndex = selectedId is null ? -1 : _styleRefIds.IndexOf(selectedId);
    }

    private void UpdateDrawOptionsEnabled()
    {
        if (_generateOptions is null) return;
        // 자동은 두 방식 모두 쓸 수 있으므로 두 옵션을 함께 보인다.
        var mode = _settings.AiDraw.Mode;
        _generateOptions.IsVisible = mode != AiDrawMode.Direct;
        _paletteRow!.IsVisible = mode != AiDrawMode.Generate;
        _styleRefCombo!.IsVisible = _styleCombo!.SelectedIndex == (int)AiDrawStyleReference.Specific;
        _sizeCustom!.IsVisible = _sizeCombo!.SelectedIndex == SizeChoices.Length;
        _sizeRow!.IsEnabled = _resizeResult!.On();
        _animCombo!.IsEnabled = mode == AiDrawMode.Generate;
        _animWarning!.IsVisible = mode == AiDrawMode.Generate && _animCombo.SelectedIndex == 1;
        bool autoPrompt = _autoPrompt!.On();
        foreach (var row in _autoPromptRows) row.IsEnabled = autoPrompt;
        bool guidesUnused = mode == AiDrawMode.Generate && !autoPrompt && _visible.Provider.Id == AiDrawSettings.GeneratingProvider;
        _assistView!.Guides.IsEnabled = !guidesUnused;
        _guidesUnused!.IsVisible = guidesUnused;
    }

    private void OnDrawModeChanged()
    {
        if (_drawFilling || _modeAuto is null) return;
        _settings.AiDraw.Mode = _modeAuto.IsChecked == true ? AiDrawMode.Auto
            : _modeGenerate!.IsChecked == true ? AiDrawMode.Generate : AiDrawMode.Direct;
        UpdateDrawOptionsEnabled();
    }

    private void OnDrawOptionChanged()
    {
        if (_drawFilling || _autoPrompt is null) return;
        var o = _settings.AiDraw;
        o.AutoPrompt = _autoPrompt.On();
        int si = _sizeCombo!.SelectedIndex;
        o.SizeTarget = si >= 0 && si < SizeChoices.Length ? SizeChoices[si] : (int)_sizeCustom!.NumValue();
        o.ResizeResult = _resizeResult!.On();
        o.ForceRemoveBackground = _removeBackground!.On();
        o.PaletteOnly = _paletteOnly!.On();
        o.AutoOptimize = _autoOptimize!.On();
        o.Insert = _insertCombo!.SelectedIndex == 1 ? AiDrawInsert.NewDocument : AiDrawInsert.NewLayer;
        o.StyleReference = (AiDrawStyleReference)Math.Max(0, _styleCombo!.SelectedIndex);
        o.AnimationMethod = _animCombo!.SelectedIndex == 1 ? AiDrawAnimationMethod.PerFrame : AiDrawAnimationMethod.SingleSheet;
        if (_styleRefCombo!.SelectedIndex is int ri && ri >= 0 && ri < _styleRefIds.Count) o.StyleReferenceId = _styleRefIds[ri];
        foreach (var (action, box) in _frameBoxes) o.FrameCounts[action] = (int)box.NumValue();
        UpdateDrawOptionsEnabled();
    }

    /// <summary>그림 생성에 쓰는 Codex CLI의 설치·로그인 상태를 설정 칸에 보인다.</summary>
    private async Task CheckCodexAsync()
    {
        if (_codexChecking || _codexStatus is null) return;
        _codexChecking = true;
        _codexStatus.Text = UiStrings.AiDraw_Codex_Checking;
        string text;
        try
        {
            var path = AiProvider.Codex.Locate(_settings);
            if (path is null) text = UiStrings.AiDraw_Codex_NotInstalled;
            else
            {
                var status = await AiProvider.Codex.GetAuthStatusAsync(path);
                text = status.LoggedIn ? UiStrings.AiDraw_Codex_Ready
                    : status.Error is null ? UiStrings.AiDraw_Codex_NotLoggedIn
                    : UiStrings.FormatAiDraw_Codex_Unknown(message: status.Error);
            }
        }
        finally
        {
            _codexChecking = false;
        }
        if (!_shutdown) _codexStatus.Text = text;
    }

    /// <summary>"변환 설정 보기": 생성 기록의 원본과 변환 설정으로 정리 창을 연다(기록이 지워졌으면 알림).</summary>
    private void OpenAiDrawJob(string jobId)
    {
        if (_mcp.Tools.Jobs.Load(jobId) is not { } loaded)
        {
            Ui.Error(this, UiStrings.AiDraw_Job_Missing);
            return;
        }
        var (job, source) = loaded;
        _mcp.Tools.Host.ShowPixelate(new PixelateInput(source, job.Name, null), job.ReadSettings());
    }

    /// <summary>보낸 질문 한 줄을 눌러 Codex 이미지 생성에 전달한 프롬프트 전문을 보는 카드.</summary>
    private Control AiDrawPromptCard(string summary, string instruction)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MinWidth = 0 };
        header.Children.Add(new TextBlock
        {
            Text = UiStrings.AiDraw_Prompt_Label,
            Foreground = AppTheme.AccentBrush,
            FontSize = AppTheme.SmallFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        var question = new TextBlock
        {
            Text = summary,
            Foreground = AppTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
        };
        Grid.SetColumn(question, 1);
        header.Children.Add(question);
        var full = new SelectableTextBlock
        {
            Text = instruction,
            FontFamily = MonoFont,
            FontSize = AppTheme.SmallFontSize,
            Foreground = AppTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(10, 6),
            Background = Brushes.Transparent,
        };
        var expander = new Expander
        {
            Header = header,
            Content = new ScrollViewer
            {
                Content = full,
                MaxHeight = 320,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        }.Tip(UiStrings.AiDraw_Prompt_Tip);
        return new Border
        {
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(2, 4, 2, 4),
            Child = expander,
        }.HelpAt("ai-generate", anchor: "Generation prompt card");
    }

    /// <summary>AI 그리기 결과 칸: 결과 그림, 결과 글, "변환 설정 보기"(기록이 있을 때)·"새 레이어로 넣기"(그림이 있을 때) 단추.</summary>
    private Control AiDrawCard(Bitmap? image, string text, string? jobId)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(2, 2, 0, 0) }.HelpAt("ai-generate", anchor: "Result card");
        if (image is not null) panel.Children.Add(SnapshotView(image));
        if (text.Length > 0) panel.Children.Add(new SelectableTextBlock { Text = text, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap });
        var buttons = new List<Control>();
        if (jobId is not null)
            buttons.Add(Ui.Button(VectorIcon.SlidersHorizontal, UiStrings.AiDraw_ViewSettings, (_, _) => OpenAiDrawJob(jobId)).Tip(UiStrings.AiDraw_ViewSettings_Tip));
        if (image is not null) buttons.Add(InsertImageButton(image));
        if (buttons.Count > 0) panel.Children.Add(ButtonRow([.. buttons]));
        return panel;
    }
}
