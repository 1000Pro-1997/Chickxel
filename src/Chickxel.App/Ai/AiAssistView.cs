using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>
/// 도구 묶음 켜고 끄기(<see cref="Tools"/>)와 그리기 프롬프트 켜고 끄기·글 고치기(<see cref="Guides"/>) 칸. 채팅 창의 그리기 설정 칸과 외부 AI 연결 창이 같이 쓴다.
/// 바꾸면 <paramref name="settings"/>·<paramref name="texts"/>가 돌려주는 객체를 바로 고치고 <c>changed</c>를 부른다(창은 사본을, 채팅 칸은 설정 그대로를 넘김).
/// </summary>
internal sealed class AiAssistView
{
    private readonly Func<AiAssistSettings> _settings;
    private readonly Func<Dictionary<string, string>> _texts;
    private readonly McpScope _scope;
    private readonly Action _changed;
    private readonly Dictionary<AiToolGroup, CheckBox> _tools = new();
    private readonly Dictionary<AiGuide, (CheckBox Check, TextBlock Edited)> _guides = new();
    private bool _filling;

    /// <param name="info">정보 아이콘을 만드는 함수(놓이는 곳의 모양을 따름).</param>
    public AiAssistView(Func<AiAssistSettings> settings, Func<Dictionary<string, string>> texts, McpScope scope, Func<string, Control> info, Action changed)
    {
        _settings = settings;
        _texts = texts;
        _scope = scope;
        _changed = changed;

        var tools = new StackPanel();
        foreach (var g in AiToolGroups.All.Where(g => AiToolGroups.AppliesTo(g, scope)))
        {
            var check = Ui.Check(AiToolGroups.Name(g)).Tip(AiToolGroups.Tip(g));
            check.IsCheckedChanged += (_, _) =>
            {
                if (_filling) return;
                _settings().SetTool(g, check.On());
                Refresh();
                _changed();
            };
            _tools[g] = check;
            tools.Children.Add(Ui.Row(check, info(AiToolGroups.Tip(g))));
        }
        Tools = tools;

        var guides = new StackPanel();
        foreach (var g in AiGuides.All)
        {
            // 좁은 칸(채팅의 설정 칸)에서는 이름이 줄바꿈되고 정보·고침·연필은 오른쪽에 붙는다.
            var check = Ui.Check("").Tip(AiGuides.Tip(g));
            check.Content = new TextBlock { Text = AiGuides.Name(g), TextWrapping = TextWrapping.Wrap };
            check.IsCheckedChanged += (_, _) =>
            {
                if (_filling) return;
                _settings().SetGuide(g, check.On());
                _changed();
            };
            var edit = Ui.IconButton(VectorIcon.Pencil, UiStrings.AiGuide_EditTip, (s, _) => EditGuide(g, (Control)s!), 24, 12);
            edit.VerticalAlignment = VerticalAlignment.Center;
            var edited = new TextBlock
            {
                Text = UiStrings.AiGuide_Edited,
                Foreground = AppTheme.DimTextBrush,
                FontSize = AppTheme.SmallFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
            }.Tip(UiStrings.AiGuide_EditedTip);
            _guides[g] = (check, edited);
            var row = new DockPanel();
            foreach (var c in new[] { edit, edited, info(AiGuides.Tip(g)) })
            {
                DockPanel.SetDock(c, Dock.Right);
                row.Children.Add(c);
            }
            row.Children.Add(check);
            guides.Children.Add(row);
        }
        Guides = guides;
        Refresh();
    }

    /// <summary>도구 묶음 체크 상자들.</summary>
    public Control Tools { get; }

    /// <summary>그리기 프롬프트 줄들(체크 상자 + 정보 + 고치기 단추 + "고침" 표시).</summary>
    public Control Guides { get; }

    /// <summary>체크 상자를 지금 설정에 맞춘다(쓸 도구가 꺼진 프롬프트는 흐리게).</summary>
    public void Refresh()
    {
        var s = _settings();
        var texts = _texts();
        _filling = true;
        try
        {
            foreach (var (g, check) in _tools) check.IsChecked = s.ToolOn(g);
            foreach (var (g, (check, edited)) in _guides)
            {
                check.IsChecked = s.GuideOn(g);
                check.IsEnabled = AiGuides.NeedsTools(g) is not { } need || s.ToolOn(need);
                edited.IsVisible = texts.ContainsKey(g.ToString());
            }
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>프롬프트 글 고치기 창: 저장하면 고친 글을 남기고(기본 글과 같거나 비우면 지움) 바로 알린다.</summary>
    private void EditGuide(AiGuide g, Control owner)
    {
        var texts = _texts();
        var dlg = new AiGuideDialog(g, AiGuides.Text(g, texts));
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;
        var text = dlg.Text.Trim();
        if (text.Length == 0 || text == AiGuides.DefaultText(g).Trim()) texts.Remove(g.ToString());
        else texts[g.ToString()] = text;
        Refresh();
        _changed();
    }
}

/// <summary>그리기 프롬프트 글 하나를 고치는 창: 여러 줄 글 칸, "기본값으로"(칸을 기본 글로 되돌림), 저장·취소.</summary>
internal sealed class AiGuideDialog : DialogBase
{
    private readonly TextBox _text;

    public AiGuideDialog(AiGuide guide, string text) : base(UiStrings.FormatAiGuide_EditTitle(name: AiGuides.Name(guide)), UiStrings.AiGuide_Save)
    {
        this.HelpAt("ai-generate");
        var about = Ui.Label(AiGuides.Tip(guide));
        about.TextWrapping = TextWrapping.Wrap;
        about.MaxWidth = 600;
        var note = Ui.Label(UiStrings.AiGuide_EditNote);
        note.TextWrapping = TextWrapping.Wrap;
        note.MaxWidth = 600;
        note.Foreground = AppTheme.DimTextBrush;
        note.Margin = new Thickness(2, 2, 2, 6);
        _text = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Width = 600,
            Height = 340,
            Margin = new Thickness(2),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_text, ScrollBarVisibility.Auto);
        Body.Children.Add(about);
        Body.Children.Add(note);
        Body.Children.Add(_text);
        AddFooterLeft(Ui.Button(VectorIcon.Undo2, UiStrings.AiGuide_Reset, (_, _) => _text.Text = AiGuides.DefaultText(guide)).Tip(UiStrings.AiGuide_ResetTip));
    }

    /// <summary>칸의 글.</summary>
    public string Text => _text.Text ?? "";
}
