using Avalonia.Input.Platform;
using Chickxel.App.Localization;
using Chickxel.Core.Scripting;

namespace Chickxel.App.Ai;

/// <summary>
/// 효과 만들기의 "규칙" 창(모달이 아님): 스크립트 효과를 만들 때 지킬 규칙과 API(<see cref="ScriptEffectGuide"/>)를 읽기 좋게 보이고,
/// 복사 단추로 클립보드에 올린다 — 다른 AI(웹의 ChatGPT 등)에 붙여 넣어 효과를 부탁하고, 받은 코드 블록을 편집기에 붙여 넣는 데 쓴다.
/// "지금 원문 포함"이 켜져 있으면 편집기의 지금 원문을 끝에 붙인다(보이는 글 = 복사하는 글). 원문은 창을 앞으로 가져올 때·복사할 때 다시 읽는다.
/// </summary>
internal sealed class ScriptGuideWindow : Window
{
    private static readonly FontFamily MonoFont = new("Menlo, Consolas, DejaVu Sans Mono, monospace");

    private readonly Func<string?> _source;
    private readonly MarkdownView _view = new(MonoFont, AiCli.OpenUrl);
    private readonly CheckBox _includeSource;
    private readonly TextBlock _copied = new() { Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    /// <param name="currentSource">효과 만들기 편집기의 지금 원문(열린 스크립트가 없으면 null).</param>
    public ScriptGuideWindow(Func<string?> currentSource)
    {
        _source = currentSource;
        Title = UiStrings.EffectRules_Title;
        Width = 760;
        Height = 760;
        MinWidth = 480;
        MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var hint = new TextBlock { Text = UiStrings.EffectRules_Hint, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush, Margin = new Thickness(2, 0, 2, 8) };
        DockPanel.SetDock(hint, Dock.Top);
        var scroll = Ui.ScrollHost(new Border { Padding = new Thickness(10, 8), Child = _view });
        var body = new Border { BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), Child = scroll };

        _includeSource = Ui.Check(UiStrings.EffectRules_IncludeSource, true).Tip(UiStrings.EffectRules_IncludeSourceTip);
        _includeSource.IsCheckedChanged += (_, _) => Refresh();
        var copy = Ui.Button(VectorIcon.Copy, UiStrings.EffectRules_Copy, async (_, _) => await CopyAsync());
        copy.Background = AppTheme.AccentBrush;
        copy.Foreground = Brushes.White;
        var close = Ui.Button(UiStrings.S_Ui_CloseOnly_11312F9A81, (_, _) => Close(), 90);
        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var right = Ui.Row(copy, close);
        DockPanel.SetDock(right, Dock.Right);
        bottom.Children.Add(right);
        bottom.Children.Add(Ui.Row(_includeSource, _copied));
        DockPanel.SetDock(bottom, Dock.Bottom);

        Content = new DockPanel { Margin = new Thickness(10), Children = { hint, bottom, body } };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            _copied.Text = "";
        };
        Activated += (_, _) => Refresh();
        Closed += (_, _) => _copiedTimer.Stop();
        Refresh();
    }

    /// <summary>복사할 글: 안내와(켜져 있고 열린 스크립트가 있으면) 지금 원문.</summary>
    internal string GuideText() => ScriptEffectGuide.ToMarkdown(_includeSource.On() ? _source() : null);

    /// <summary>지금 원문이 있는지에 맞춰 "원문 포함"을 켜고 끌 수 있게 하고, 보이는 글을 다시 만든다.</summary>
    private void Refresh()
    {
        _includeSource.IsEnabled = _source() is not null;
        _view.SetText(GuideText());
    }

    /// <summary>안내(와 원문)를 클립보드에 올리고 잠깐 "복사했습니다"를 보인다.</summary>
    internal async Task CopyAsync()
    {
        Refresh();
        if (Clipboard is not { } clipboard) return;
        bool withSource = _includeSource.On() && _source() is not null;
        await clipboard.SetTextAsync(GuideText());
        _copied.Text = withSource ? UiStrings.EffectRules_CopiedWithSource : UiStrings.EffectRules_Copied;
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }
}
