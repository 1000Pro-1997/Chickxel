using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Chickxel.App;

/// <summary>
/// 풍선 도움말의 단축키 표시: 글 풍선(<see cref="ToolTip"/>의 내용이 문자열)에서 그 대상의 단축키 — 첫 문장 안에서 괄호로 적고 바로 뒤가
/// 글 끝·구분(띄어 쓴 · — –, 쌍점, 마침표, 줄바꿈)인 것, 예: "양파 껍질: … 보기 (Ctrl+J) · 우클릭 …" — 를 빼서 맨 위에 초록 코드 칸으로 보인다.
/// "(S) 먼저"처럼 문장 속에서 다른 도구를 가리키는 키, "밝기(L)·채도"처럼 붙여 쓴 글자, "(X, Y)"처럼 글자만 늘어놓은 것은 그대로 둔다.
/// </summary>
public static partial class ShortcutTip
{
    private const string Key = @"[⌘⇧⌥]*(?:Ctrl|Shift|Alt|Cmd|Tab|Esc|Enter|Space|Delete|Del|Home|End|PgUp|PgDn|Backspace|Insert|F\d{1,2}|[A-Z0-9]|[`\[\]\\;',./=\-]|←|→|↑|↓)";
    private const string Combo = Key + @"(?:\+" + Key + @")*(?:/" + Key + @")*";

    [GeneratedRegex(@"\s?\((?<keys>" + Combo + @"(?:\s*(?:,|또는|or)\s*" + Combo + @")*)\)(?=$|\s+[·—–]\s|\s*:|\.(?:\s|$)|\r?\n)")]
    private static partial Regex ShortcutParen();

    [GeneratedRegex(@"\s*(?:,|또는|or)\s*")]
    private static partial Regex ListSeparator();

    [GeneratedRegex(@"[.。!?](?:\s|$)|\r?\n")]
    private static partial Regex SentenceEnd();

    /// <summary>풍선 글 <paramref name="text"/>에서 단축키를 뺀다: (단축키들, 남은 글). 단축키가 없으면 null.</summary>
    public static (string[] Keys, string Text)? Split(string text)
    {
        var m = ShortcutParen().Match(text);
        if (!m.Success) return null;
        // 첫 문장 안의 괄호만(뒤 문장의 "… 도구 (T)." 같은 안내는 그대로)
        var end = SentenceEnd().Match(text);
        if (end.Success && end.Index < m.Index) return null;
        var keys = ListSeparator().Split(m.Groups["keys"].Value);
        if (keys.Length > 1 && keys.All(k => k.Length == 1 && char.IsLetter(k[0]))) return null;
        string rest = (text[..m.Index] + text[(m.Index + m.Length)..]).Trim();
        return (keys, rest);
    }

    private static readonly IImmutableSolidColorBrush ChipBack = new ImmutableSolidColorBrush(Color.FromRgb(28, 52, 36));
    private static readonly IImmutableSolidColorBrush ChipBorder = new ImmutableSolidColorBrush(Color.FromRgb(58, 118, 76));
    private static readonly IImmutableSolidColorBrush ChipText = new ImmutableSolidColorBrush(Color.FromRgb(126, 216, 143));
    private static readonly FontFamily MonoFont = new("Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>
    /// 단축키가 든 글 풍선의 내용 틀(단축키 없는 글은 평소대로). 앱 스타일에서 모든 <see cref="ToolTip"/>에 건다.
    /// 내용 틀이 있으면 ContentPresenter는 컨트롤 내용도 틀로 만들므로(맞는 틀이 없으면 이름 글자로 보임) 컨트롤 내용은 그대로 돌려준다.
    /// </summary>
    public static readonly IDataTemplate Template = new FuncDataTemplate<object>(
        data => data is Control || data is string s && Split(s) is not null,
        (data, _) => data as Control ?? Build((string)data));

    private static Control Build(string text)
    {
        var (keys, rest) = Split(text)!.Value;
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var key in keys)
        {
            var chip = Chip(key);
            chip.Margin = new Thickness(0, 0, 4, 0);
            chips.Children.Add(chip);
        }
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(chips);
        if (rest.Length > 0) panel.Children.Add(new TextBlock { Text = rest, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    /// <summary>
    /// 초록 코드 칸 하나(단축키 · 튜토리얼에서 누를 것 — <see cref="TutorialText"/>). <paramref name="inline"/>면 글 줄 안에 들어가게 낮게(작은 글자, 위아래 여백 없음).
    /// </summary>
    public static Border Chip(string text, bool inline = false) => new()
    {
        Background = ChipBack,
        BorderBrush = ChipBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        Padding = inline ? new Thickness(4, 0) : new Thickness(5, 1),
        Child = inline
            ? new TextBlock { Text = text, FontFamily = MonoFont, Foreground = ChipText, FontSize = AppTheme.SmallFontSize, LineHeight = double.NaN }
            : new TextBlock { Text = text, FontFamily = MonoFont, Foreground = ChipText },
    };
}
