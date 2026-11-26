using Avalonia.Controls.Documents;

namespace Chickxel.App;

/// <summary>
/// 튜토리얼 글(말풍선 · 움짤 설명)의 입력 표시: <c>[[우클릭]]</c>처럼 두 겹 대괄호로 감싼 곳(누를 것 · 고를 메뉴 · 넣을 값)을
/// 풍선 도움말의 단축키 칸과 같은 초록 네모(<see cref="ShortcutTip.Chip"/>)로 글 줄 안에 그린다.
/// </summary>
internal static class TutorialText
{
    /// <summary><paramref name="block"/>에 <paramref name="text"/>를 넣는다(같은 글이면 그대로 — 단계마다 짧은 간격으로 다시 부름).</summary>
    public static void Fill(TextBlock block, string text)
    {
        if (block.Tag is string shown && shown == text) return;
        block.Tag = text;
        var inlines = block.Inlines ??= [];
        inlines.Clear();
        int pos = 0;
        while (pos < text.Length)
        {
            int open = text.IndexOf("[[", pos, StringComparison.Ordinal);
            int close = open < 0 ? -1 : text.IndexOf("]]", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                AddText(inlines, text[pos..]);
                break;
            }
            if (open > pos) AddText(inlines, text[pos..open]);
            var chip = ShortcutTip.Chip(text[(open + 2)..close], inline: true);
            chip.Margin = new Thickness(1, 0);
            inlines.Add(new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center });
            pos = close + 2;
        }
    }

    /// <summary>글 조각을 넣는다: 줄바꿈(<c>\n</c>)은 줄 나눔으로(경험치 목록처럼 여러 줄로 적은 글).</summary>
    private static void AddText(InlineCollection inlines, string text)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());
            if (lines[i].TrimEnd('\r') is { Length: > 0 } line) inlines.Add(new Run(line));
        }
    }

    /// <summary>입력 표시를 뺀 글(점검이 글을 맞춰 볼 때).</summary>
    public static string Plain(string text) => text.Replace("[[", "").Replace("]]", "");
}
