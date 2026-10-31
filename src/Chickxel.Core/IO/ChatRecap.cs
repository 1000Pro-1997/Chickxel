using System.Text;

namespace Chickxel.Core.IO;

/// <summary>
/// 마지막 차례 지우기·고치기와 그 뒤 대화 이어 가기. CLI 세션 기록에서는 차례 하나만 뺄 수 없으므로, 차례를 지우면 다음 메시지는
/// 새 CLI 세션으로 보내고 남은 대화(사용자·AI 글, 도구는 차례마다 이름만)를 짧게 정리해 앞에 붙인다(<see cref="SavedChat.NeedsRecap"/>).
/// 정리 글은 AI가 읽는 글이라 영어로 쓴다.
/// </summary>
public static class ChatRecap
{
    /// <summary>정리 글의 최대 길이(넘치면 가장 최근 쪽을 남김).</summary>
    public const int MaxChars = 6000;
    public const string Header = "[Earlier conversation in this chat (for context):]";
    public const string NewMessage = "[New message:]";
    public const string Omitted = "(… earlier part omitted)";

    /// <summary>사용자 메시지가 하나라도 있는지(남은 차례가 있는지).</summary>
    public static bool HasTurns(IEnumerable<ChatItem> items) => items.Any(i => i.Kind == ChatItemKind.User);

    /// <summary>
    /// 마지막 차례(마지막 사용자 메시지부터 끝까지: AI 답·도구 줄·캔버스 모습·끝 줄·안내)를 뺀다. 뺀 사용자 메시지, 차례가 없으면 null.
    /// </summary>
    public static ChatItem? RemoveLastTurn(List<ChatItem> items)
    {
        int at = items.FindLastIndex(i => i.Kind == ChatItemKind.User);
        if (at < 0) return null;
        var user = items[at];
        items.RemoveRange(at, items.Count - at);
        return user;
    }

    /// <summary>
    /// 남은 대화 정리: 차례마다 "User: …", "Assistant: …" 문단과 부른 도구 이름 한 줄(안내·오류·캔버스 모습·끝 줄은 뺌).
    /// <paramref name="maxChars"/>를 넘으면 가장 최근 쪽을 남기고 앞을 자른다. 남은 차례가 없으면 null.
    /// </summary>
    public static string? Build(IEnumerable<ChatItem> items, int maxChars = MaxChars)
    {
        var parts = new List<string>();
        var tools = new List<string>();
        void FlushTools()
        {
            if (tools.Count > 0) parts.Add("(Assistant used tools: " + string.Join(", ", tools) + ")");
            tools.Clear();
        }
        bool any = false;
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case ChatItemKind.User:
                    FlushTools();
                    any = true;
                    var text = ChatImageTokens.ToModel(item.Text).Trim();
                    if (item.ImageCount > 0 && !ChatImageTokens.HasTokens(item.Text)) text = (text + $" [{item.ImageCount} image(s) attached]").Trim();
                    parts.Add("User: " + text);
                    break;
                case ChatItemKind.Assistant when item.Text.Trim().Length > 0:
                    FlushTools();
                    parts.Add("Assistant: " + item.Text.Trim());
                    break;
                case ChatItemKind.Tool when ToolName(item.Text) is { Length: > 0 } name:
                    if (!tools.Contains(name)) tools.Add(name);
                    break;
            }
        }
        FlushTools();
        if (!any) return null;

        // 최근 문단부터 넣을 수 있는 만큼(가장 최근 문단 하나가 넘치면 그 끝부분만).
        const string sep = "\n\n";
        var kept = new List<string>();
        int length = 0;
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            int add = parts[i].Length + (kept.Count > 0 ? sep.Length : 0);
            if (length + add > maxChars)
            {
                if (kept.Count == 0) kept.Add("…" + parts[i][^Math.Max(0, maxChars - 1)..]);
                break;
            }
            kept.Add(parts[i]);
            length += add;
        }
        kept.Reverse();
        var sb = new StringBuilder();
        if (kept.Count < parts.Count) sb.Append(Omitted).Append(sep);
        sb.AppendJoin(sep, kept);
        return sb.ToString();
    }

    /// <summary>보낼 메시지: 정리 글 머리 + 정리 + 새 메시지 머리 + 메시지.</summary>
    public static string Compose(string recap, string message) => $"{Header}\n{recap}\n\n{NewMessage}\n{message}";

    /// <summary>저장한 도구 줄("• 이름 · 인자" / 실패면 뒤에 " — 오류")의 도구 이름.</summary>
    private static string ToolName(string line)
    {
        var s = line.TrimStart('•', ' ');
        int i = s.IndexOf(" · ", StringComparison.Ordinal);
        if (i >= 0) s = s[..i];
        i = s.IndexOf(" — ", StringComparison.Ordinal);
        if (i >= 0) s = s[..i];
        return s.Trim();
    }
}
