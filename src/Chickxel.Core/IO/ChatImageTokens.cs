using System.Text;
using System.Text.RegularExpressions;

namespace Chickxel.Core.IO;

/// <summary>채팅 메시지 글의 한 조각: 글(<see cref="Text"/>) 또는 그림 칩(<see cref="Image"/> = 번호, 1부터).</summary>
public readonly record struct ChatSegment(string? Text, int Image)
{
    public bool IsImage => Text is null;
}

/// <summary>글 안의 그림 칩 하나: 시작 위치·길이(표시 글자 포함)와 번호(1부터).</summary>
public readonly record struct ChatImageToken(int Start, int Length, int Number)
{
    public int End => Start + Length;
}

/// <summary>
/// 채팅 입력·메시지 글 안의 그림 칩(<c>[이미지 #1]</c>). 칩은 보이지 않는 표시 글자(<see cref="Mark"/>, U+2060 낱말 잇기) 사이의
/// <c>[이름 #N]</c>이라 사용자가 친 같은 글과 섞이지 않는다. 이름은 화면 언어(입력란) 또는 <see cref="StoredLabel"/>(저장한 대화)이고,
/// 번호는 글 안 순서(1부터) = 첨부 순서다. AI에게는 <see cref="ToModel"/>로 <c>[Image #N]</c>를 보낸다.
/// </summary>
public static partial class ChatImageTokens
{
    public const char Mark = '\u2060';
    /// <summary>저장하는 칩 이름(언어와 관계없음).</summary>
    public const string StoredLabel = "Image";

    [GeneratedRegex("\u2060\\[[^\\]\u2060\\r\\n]*#([0-9]{1,4})\\]\u2060")]
    private static partial Regex TokenPattern();

    /// <summary>칩 글: 표시 글자 + <c>[이름 #N]</c>(이름과 번호 사이는 줄바꿈 없는 공백) + 표시 글자.</summary>
    public static string Token(string label, int number) => $"{Mark}[{label}\u00A0#{number}]{Mark}";

    /// <summary>글 안의 칩(앞에서부터).</summary>
    public static List<ChatImageToken> Find(string? text)
    {
        var list = new List<ChatImageToken>();
        if (string.IsNullOrEmpty(text) || text.IndexOf(Mark) < 0) return list;
        foreach (Match m in TokenPattern().Matches(text))
            list.Add(new ChatImageToken(m.Index, m.Length, int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
        return list;
    }

    public static bool HasTokens(string? text) => Find(text).Count > 0;

    /// <summary>깨진 칩 조각을 찾을 거리(글자).</summary>
    private const int MaxResidue = 32;

    /// <summary>
    /// 칩 번호를 첨부와 맞춘다: <paramref name="count"/>개 첨부 중 칩이 가리키는 번호(1~count)를 글 순서대로 모아 <paramref name="order"/>
    /// (새 번호 순서의 옛 번호 − 1)에 담고, 범위 밖·두 번째로 나온 칩은 지운 뒤, 남은 칩을 1부터 다시 매기고 이름을 <paramref name="label"/>로 맞춘다.
    /// 칩 밖의 떠도는 표시 글자는 깨진 칩 조각(표시 글자 뒤 <c>[…]</c>까지, 또는 앞 <c>[…]</c>부터)과 함께 지운다.
    /// <paramref name="caret"/>(글자 위치)은 바뀐 글에서 같은 자리로 옮긴다.
    /// </summary>
    public static string Normalize(string text, int count, string label, out List<int> order, ref int caret)
    {
        order = [];
        var tokens = Find(text);
        var drop = BrokenResidue(text, tokens);
        var sb = new StringBuilder(text.Length);
        int pos = 0, newCaret = -1, oldCaret = caret;
        var used = new HashSet<int>();
        void Copy(int end)
        {
            for (int i = pos; i < end; i++)
            {
                if (i == oldCaret && newCaret < 0) newCaret = sb.Length;
                if (!drop[i]) sb.Append(text[i]);
            }
            pos = end;
        }
        foreach (var t in tokens)
        {
            Copy(t.Start);
            bool inside = caret > t.Start && caret < t.End;
            if (caret == t.Start && newCaret < 0) newCaret = sb.Length;
            if (t.Number >= 1 && t.Number <= count && used.Add(t.Number))
            {
                order.Add(t.Number - 1);
                sb.Append(Token(label, order.Count));
            }
            if (inside) newCaret = sb.Length;
            pos = t.End;
        }
        Copy(text.Length);
        if (newCaret < 0) newCaret = sb.Length;
        caret = newCaret;
        return sb.ToString();
    }

    /// <summary>
    /// 칩 밖에서 지울 글자: 떠도는 표시 글자와, 그 뒤가 <c>[</c>면 다음 <c>]</c>까지(칩 앞부분), 그 앞이 <c>]</c>면 앞 <c>[</c>부터(칩 뒷부분) —
    /// 줄바꿈·다른 칩·<see cref="MaxResidue"/>자를 넘지 않을 때만.
    /// </summary>
    private static bool[] BrokenResidue(string text, List<ChatImageToken> tokens)
    {
        var drop = new bool[text.Length];
        var inToken = new bool[text.Length];
        foreach (var t in tokens)
            for (int i = t.Start; i < t.End; i++) inToken[i] = true;
        bool Free(int i) => i >= 0 && i < text.Length && !inToken[i] && text[i] is not ('\n' or '\r' or Mark);
        for (int i = 0; i < text.Length; i++)
        {
            if (inToken[i] || text[i] != Mark) continue;
            drop[i] = true;
            if (Free(i + 1) && text[i + 1] == '[')
            {
                for (int j = i + 1; j < text.Length && j <= i + MaxResidue && Free(j); j++)
                {
                    if (text[j] != ']') continue;
                    for (int k = i + 1; k <= j; k++) drop[k] = true;
                    break;
                }
            }
            else if (Free(i - 1) && text[i - 1] == ']')
            {
                for (int j = i - 1; j >= 0 && j >= i - MaxResidue && Free(j); j--)
                {
                    if (text[j] != '[') continue;
                    for (int k = j; k < i; k++) drop[k] = true;
                    break;
                }
            }
        }
        return drop;
    }

    /// <summary>칩을 <paramref name="label"/> 이름으로 바꾼 글(번호 그대로). 표시 글자는 남긴다.</summary>
    public static string Relabel(string text, string label)
    {
        var tokens = Find(text);
        if (tokens.Count == 0) return text;
        var sb = new StringBuilder(text.Length);
        int pos = 0;
        foreach (var t in tokens)
        {
            sb.Append(text, pos, t.Start - pos).Append(Token(label, t.Number));
            pos = t.End;
        }
        return sb.Append(text, pos, text.Length - pos).ToString();
    }

    /// <summary>AI에게 보낼 글: 칩은 <c>[Image #N]</c>, 떠도는 표시 글자는 지움.</summary>
    public static string ToModel(string text) => Plain(text, StoredLabel);

    /// <summary>보이는 글(제목 등): 칩은 <c>[이름 #N]</c>(보통 공백), 표시 글자는 지움.</summary>
    public static string Plain(string text, string label)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var s in Split(text))
            if (s.IsImage) sb.Append('[').Append(label).Append(" #").Append(s.Image).Append(']');
            else sb.Append(s.Text);
        return sb.ToString();
    }

    /// <summary>칩을 모두 지운 글(앞뒤 공백 정리).</summary>
    public static string WithoutTokens(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var s in Split(text))
            if (!s.IsImage) sb.Append(s.Text);
        return sb.ToString().Trim();
    }

    /// <summary>글과 칩 조각(앞에서부터, 빈 글 조각은 빼고, 떠도는 표시 글자는 지움).</summary>
    public static List<ChatSegment> Split(string? text)
    {
        var list = new List<ChatSegment>();
        if (string.IsNullOrEmpty(text)) return list;
        int pos = 0;
        void AddText(int end)
        {
            if (end > pos)
            {
                var s = text.Substring(pos, end - pos).Replace(Mark.ToString(), "", StringComparison.Ordinal);
                if (s.Length > 0) list.Add(new ChatSegment(s, 0));
            }
        }
        foreach (var t in Find(text))
        {
            AddText(t.Start);
            list.Add(new ChatSegment(null, t.Number));
            pos = t.End;
        }
        AddText(text.Length);
        return list;
    }

    /// <summary>
    /// 칩 하나를 지울 범위: 커서 <paramref name="caret"/>가 칩 바로 뒤(<paramref name="backward"/> — Backspace)나 바로 앞(Delete)이거나 칩 안이면
    /// 그 칩(없으면 null).
    /// </summary>
    public static ChatImageToken? At(string? text, int caret, bool backward)
    {
        foreach (var t in Find(text))
        {
            if (caret > t.Start && caret < t.End) return t;
            if (backward ? caret == t.End : caret == t.Start) return t;
        }
        return null;
    }
}
