using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>
/// 외부 AI 연결 창(AI 메뉴): 다른 AI 앱이 <c>Chickxel --mcp</c>로 켜져 있는 이 앱의 도구를 쓰게 할지(<see cref="AppSettings.AiExternalMcp"/>),
/// 쓸 수 있는 도구 묶음과 그 앱에 줄 그리기 프롬프트(<see cref="AppSettings.AiExternalAssist"/> — 켰을 때만), 그 앱에 붙여 넣을 연결 설정 복사.
/// 확인을 누르면 부른 쪽이 설정에 넣고 바로 적용한다(프롬프트 글 고침도 확인할 때 남음).
/// "연결 방법"을 펼치면 연결 구조·실행 명령·앱별 등록 곳을 적은 안내(<see cref="McpBridge.ConnectionGuide"/>)가 보이고, 통째로 복사해 AI 앱에 붙여 넣을 수 있다.
/// </summary>
internal sealed class ExternalAiDialog : DialogBase
{
    private readonly CheckBox _external;
    private readonly AiAssistSettings _assist;
    private readonly Dictionary<string, string> _texts;
    /// <summary>도구 묶음(왼쪽)·그리기 프롬프트(오른쪽) 칸 — 연결을 허락했을 때만 쓸 수 있음.</summary>
    private readonly Grid _options;

    /// <summary>연결 설정 칸과 연결 안내의 폭(DIP).</summary>
    private const double PanelWidth = 560;

    internal ExternalAiDialog(AppSettings settings) : base(UiStrings.ExternalMcp_Title)
    {
        this.HelpAt("ai-external", shot: "ai-external");
        _external = Ui.Check(UiStrings.ExternalMcp_Allow, settings.AiExternalMcp);
        _assist = settings.AiExternalAssist.Clone();
        _texts = new Dictionary<string, string>(settings.AiGuideTexts);
        var assist = new AiAssistView(() => _assist, () => _texts, McpScope.External, Ui.InfoBadge, () => { });
        Control Column(string title, string tip, Control content) => new StackPanel
        {
            Children = { Ui.Row(Ui.Label(title, bold: true), Ui.InfoBadge(tip)), content },
        };
        var tools = Column(UiStrings.AiTools_Title, UiStrings.AiTools_Title_Tip, assist.Tools);
        var guides = Column(UiStrings.AiGuide_Title, UiStrings.AiGuide_Title_Tip, assist.Guides);
        guides.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(guides, 1);
        var options = _options = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            Margin = new Thickness(22, 4, 0, 4),
            Children = { tools, guides },
        };
        void SyncExternal() => options.IsEnabled = _external.On();
        _external.IsCheckedChanged += (_, _) => SyncExternal();
        SyncExternal();
        // 연결 설정: 고칠 수 없는 입력칸에 붙여 넣을 JSON을 보이고, 오른쪽 아이콘 단추로 복사한다(복사하면 잠깐 체크 표시).
        var configBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Text = McpBridge.ClientConfigJson(),
            FontSize = AppTheme.SmallFontSize,
            MaxHeight = 200,
            Margin = new Thickness(2),
        };
        var copyConfig = Ui.IconButton(VectorIcon.Copy, UiStrings.ExternalMcp_CopyConfig + "\n" + UiStrings.ExternalMcp_CopyConfigTip);
        copyConfig.VerticalAlignment = VerticalAlignment.Top;
        copyConfig.Click += async (_, _) =>
        {
            if (Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(configBox.Text ?? "");
            var icon = (IconView)copyConfig.Content!;
            icon.Icon = VectorIcon.Check;
            DispatcherTimer.RunOnce(() => icon.Icon = VectorIcon.Copy, TimeSpan.FromSeconds(1.5));
        };
        DockPanel.SetDock(copyConfig, Dock.Right);
        var config = new DockPanel { Width = PanelWidth, Children = { copyConfig, configBox } };
        var guideArrow = new IconView(VectorIcon.ChevronRight, AppTheme.TextBrush) { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
        var guideToggle = new Button
        {
            Margin = new Thickness(2, 6, 2, 2),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { guideArrow, new TextBlock { Text = UiStrings.ExternalMcp_GuideShow, VerticalAlignment = VerticalAlignment.Center } },
            },
        }.Tip(UiStrings.ExternalMcp_GuideShowTip);
        var guideCopied = Ui.Label("");
        guideCopied.Foreground = AppTheme.DimTextBrush;
        var guideText = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.TextBrush, FontSize = AppTheme.SmallFontSize, Margin = new Thickness(0, 0, 14, 0) };
        var guideCopy = Ui.Button(VectorIcon.Copy, UiStrings.ExternalMcp_GuideCopy, async (_, _) =>
        {
            if (Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(guideText.Text ?? "");
            guideCopied.Text = UiStrings.ExternalMcp_Copied;
        }).Tip(UiStrings.ExternalMcp_GuideCopyTip);
        var guide = new StackPanel
        {
            IsVisible = false,
            Width = PanelWidth,
            Margin = new Thickness(0, 2, 0, 0),
            Spacing = 4,
            Children =
            {
                Ui.Row(guideCopy, guideCopied),
                new Border
                {
                    BorderBrush = AppTheme.BorderBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 6),
                    Child = new ScrollViewer { Content = guideText, MaxHeight = 420, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                },
            },
        };
        guideToggle.Click += (_, _) =>
        {
            // 처음 펼칠 때 채운다(실행 파일 경로·잠금 파일 위치는 지금 값).
            if (string.IsNullOrEmpty(guideText.Text)) guideText.Text = McpBridge.ConnectionGuide();
            guide.IsVisible = !guide.IsVisible;
            guideArrow.Icon = guide.IsVisible ? VectorIcon.ChevronDown : VectorIcon.ChevronRight;
        };
        Body.Children.Add(Ui.Row(_external, Ui.InfoBadge(UiStrings.ExternalMcp_Info)));
        Body.Children.Add(options);
        var configLabel = Ui.Label(UiStrings.ExternalMcp_ConfigLabel);
        configLabel.Margin = new Thickness(2, 8, 2, 0);
        Body.Children.Add(configLabel);
        Body.Children.Add(config);
        Body.Children.Add(guideToggle);
        Body.Children.Add(guide);
    }

    /// <summary>외부 AI 연결을 허락할지(<see cref="AppSettings.AiExternalMcp"/>).</summary>
    public bool ExternalMcp => _external.On();

    /// <summary>외부 AI 연결에서 끈 도구 묶음·그리기 프롬프트.</summary>
    public AiAssistSettings Assist => _assist;

    /// <summary>고친 그리기 프롬프트 글(채팅과 같이 씀).</summary>
    public Dictionary<string, string> GuideTexts => _texts;
}
