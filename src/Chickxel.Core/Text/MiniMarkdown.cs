using System.Text;

namespace Chickxel.Core.Text;

/// <summary>글 조각의 꾸밈(겹칠 수 있음).</summary>
[Flags]
public enum MdStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    Strike = 8,
}

/// <summary>표 칸의 가로 맞춤(구분 줄의 <c>:</c> 자리로 정함).</summary>
public enum MdAlign
{
    Left,
    Center,
    Right,
}

/// <summary>같은 꾸밈의 글 조각. <see cref="Url"/>이 있으면 누르면 여는 링크(http·https만). 줄바꿈은 글 속 '\n'.</summary>
public sealed record MdSpan(string Text, MdStyle Style = MdStyle.None, string? Url = null);

public enum MdBlockKind
{
    Paragraph,
    Heading,
    ListItem,
    Quote,
    Code,
    Table,
    /// <summary>가로줄(<c>---</c>·<c>***</c>·<c>___</c>).</summary>
    Rule,
}

/// <summary>마크다운 블록 하나.</summary>
public sealed class MdBlock
{
    public required MdBlockKind Kind { get; init; }
    /// <summary>제목 단계(1~3, <c>####</c> 이하는 3) 또는 목록 깊이(0부터).</summary>
    public int Level { get; init; }
    /// <summary>번호 목록의 번호 글(예: "1."). 글머리 목록이면 null.</summary>
    public string? Number { get; init; }
    /// <summary>문단·제목·목록 항목의 글.</summary>
    public IReadOnlyList<MdSpan> Spans { get; init; } = [];
    /// <summary>인용(<see cref="MdBlockKind.Quote"/>) 안의 블록.</summary>
    public IReadOnlyList<MdBlock> Children { get; init; } = [];
    /// <summary>코드 블록 내용(원문 그대로, 언어 이름 줄은 뺌).</summary>
    public string Code { get; init; } = "";
    /// <summary>표의 칸별 가로 맞춤. 칸 수 = 머리 줄 칸 수.</summary>
    public IReadOnlyList<MdAlign> Columns { get; init; } = [];
    /// <summary>표의 줄(맨 앞 = 머리 줄). 줄마다 칸 수가 <see cref="Columns"/>와 같게 채우거나 자른다.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<MdSpan>>> Rows { get; init; } = [];
    /// <summary>이 블록을 만든 원문 줄. 스트리밍 중 앞쪽 블록이 그대로인지 알아보는 데 쓴다.</summary>
    public string Source { get; init; } = "";
}

/// <summary>
/// AI 답 글에 쓰는 작은 마크다운 해석기(CommonMark 일부 + GitHub 표·취소선). 문단(줄바꿈은 그대로 줄바꿈), <c>#</c>~<c>######</c> 제목,
/// <c>-</c>/<c>*</c>/<c>+</c>·<c>1.</c> 목록(들여쓰기로 겹침), <c>&gt;</c> 인용, <c>```</c> 코드 블록(닫히지 않으면 끝까지),
/// <c>---</c> 가로줄(밑줄 제목은 없음 — 문단 아래 <c>---</c>도 가로줄), <c>| 머리 |</c> + <c>|---|:--:|</c> 표,
/// 글 안의 <c>**굵게**</c>/<c>__굵게__</c>, <c>*기울임*</c>/<c>_기울임_</c>, <c>~~취소선~~</c>, <c>`코드`</c>, <c>[글](주소)</c>와 맨 주소(http·https만)를 읽는다.
/// 짝이 없거나 모르는 표시는 글자 그대로 둔다. 강조 규칙은 CommonMark의 좌우 경계 규칙을 따르되, 한글처럼 ASCII가 아닌 글자 옆은
/// 공백·문장부호처럼 봐서 <c>**'이름'**은</c>도 굵게 되고, 영문·숫자 사이의 <c>*</c>(<c>2*3*4</c>)·글자 사이의 <c>_</c>(<c>snake_case</c>)는 그대로 둔다.
/// </summary>
public static class MiniMarkdown
{
    public static IReadOnlyList<MdBlock> Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<MdBlock>();
        ParseBlocks(lines, blocks);
        return blocks;
    }

    /// <summary>글 안의 꾸밈만 읽는다(블록 없이).</summary>
    public static IReadOnlyList<MdSpan> ParseInline(string text)
    {
        var spans = new List<MdSpan>();
        var depth = new Depth();
        Flatten(Inline(text, allowLinks: true), MdStyle.None, null, ref depth, spans);
        return spans;
    }

    // ───────────── 블록 ─────────────

    private static void ParseBlocks(string[] lines, List<MdBlock> blocks)
    {
        var listIndents = new List<int>();
        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (IsBlank(line))
            {
                i++;
                continue;
            }
            int indent = Indent(line, out int start);
            var rest = line[start..];

            if (IsFenceOpen(rest, out char fenceChar, out int fenceLen))
            {
                var code = new List<string>();
                int j = i + 1;
                for (; j < lines.Length; j++)
                {
                    if (IsFenceClose(lines[j].TrimStart(' ', '\t'), fenceChar, fenceLen)) break;
                    code.Add(StripIndent(lines[j], indent));
                }
                int end = Math.Min(j + 1, lines.Length);
                blocks.Add(new MdBlock { Kind = MdBlockKind.Code, Code = string.Join("\n", code), Source = Join(lines, i, end) });
                if (indent == 0) listIndents.Clear();
                i = end;
                continue;
            }

            if (IsHeading(rest, out int level, out string heading))
            {
                blocks.Add(new MdBlock { Kind = MdBlockKind.Heading, Level = level, Spans = ParseInline(heading), Source = line });
                listIndents.Clear();
                i++;
                continue;
            }

            if (rest.StartsWith('>'))
            {
                var inner = new List<string>();
                int j = i;
                while (j < lines.Length && lines[j].TrimStart(' ', '\t') is var q && q.StartsWith('>'))
                {
                    q = q[1..];
                    inner.Add(q.StartsWith(' ') ? q[1..] : q);
                    j++;
                }
                var children = new List<MdBlock>();
                ParseBlocks([.. inner], children);
                blocks.Add(new MdBlock { Kind = MdBlockKind.Quote, Children = children, Source = Join(lines, i, j) });
                listIndents.Clear();
                i = j;
                continue;
            }

            if (IsRule(rest))
            {
                blocks.Add(new MdBlock { Kind = MdBlockKind.Rule, Source = line });
                listIndents.Clear();
                i++;
                continue;
            }

            if (IsListItem(rest, out string? number, out string itemText))
            {
                while (listIndents.Count > 0 && listIndents[^1] > indent) listIndents.RemoveAt(listIndents.Count - 1);
                if (listIndents.Count == 0 || listIndents[^1] < indent) listIndents.Add(indent);
                var body = new StringBuilder(itemText.Trim());
                int j = i + 1;
                // 항목보다 더 들여 쓴 이어지는 줄은 같은 항목의 다음 줄.
                while (j < lines.Length && !IsBlank(lines[j]) && Indent(lines[j], out _) > indent && !StartsBlock(lines, j, inParagraph: false))
                {
                    body.Append('\n').Append(lines[j].Trim());
                    j++;
                }
                blocks.Add(new MdBlock
                {
                    Kind = MdBlockKind.ListItem,
                    Level = listIndents.Count - 1,
                    Number = number,
                    Spans = ParseInline(body.ToString()),
                    Source = Join(lines, i, j),
                });
                i = j;
                continue;
            }

            if (IsTableStart(lines, i, out var columns))
            {
                var rows = new List<IReadOnlyList<IReadOnlyList<MdSpan>>> { TableRow(lines[i], columns.Count) };
                int j = i + 2;
                // 표는 빈 줄이나 |가 없는 줄, 다른 블록을 여는 줄에서 끝난다.
                while (j < lines.Length && !IsBlank(lines[j]) && HasPipe(lines[j]) && !StartsLine(lines[j].TrimStart(' ', '\t'), inParagraph: true))
                {
                    rows.Add(TableRow(lines[j], columns.Count));
                    j++;
                }
                blocks.Add(new MdBlock { Kind = MdBlockKind.Table, Columns = columns, Rows = rows, Source = Join(lines, i, j) });
                listIndents.Clear();
                i = j;
                continue;
            }

            var para = new StringBuilder(line.Trim());
            int k = i + 1;
            while (k < lines.Length && !IsBlank(lines[k]) && !StartsBlock(lines, k, inParagraph: true))
            {
                para.Append('\n').Append(lines[k].Trim());
                k++;
            }
            blocks.Add(new MdBlock { Kind = MdBlockKind.Paragraph, Spans = ParseInline(para.ToString()), Source = Join(lines, i, k) });
            listIndents.Clear();
            i = k;
        }
    }

    private static bool IsBlank(string line) => string.IsNullOrWhiteSpace(line);

    private static string Join(string[] lines, int from, int to) => string.Join("\n", lines, from, to - from);

    /// <summary>앞 공백 칸 수(탭 = 다음 4칸 배수)와 첫 글자 자리.</summary>
    private static int Indent(string line, out int start)
    {
        int cols = 0;
        start = 0;
        while (start < line.Length && line[start] is ' ' or '\t')
        {
            cols = line[start] == '\t' ? (cols / 4 + 1) * 4 : cols + 1;
            start++;
        }
        return cols;
    }

    private static string StripIndent(string line, int indent)
    {
        int n = 0;
        while (n < indent && n < line.Length && line[n] == ' ') n++;
        return line[n..];
    }

    /// <summary>
    /// 문단(또는 목록 항목)을 끊고 새 블록을 여는 줄인지(다음 줄이 표 구분 줄이면 표 머리 줄). 문단 안에서는 CommonMark처럼 1로 시작하는 번호만 목록을 연다.
    /// </summary>
    private static bool StartsBlock(string[] lines, int j, bool inParagraph)
        => StartsLine(lines[j].TrimStart(' ', '\t'), inParagraph) || IsTableStart(lines, j, out _);

    /// <summary>그 줄만 보고 새 블록을 여는지(표 빼고). 밑줄 제목은 없으니 문단 아래 <c>---</c>도 가로줄.</summary>
    private static bool StartsLine(string rest, bool inParagraph)
    {
        if (IsFenceOpen(rest, out _, out _) || IsHeading(rest, out _, out _) || rest.StartsWith('>') || IsRule(rest)) return true;
        if (!IsListItem(rest, out string? number, out _)) return false;
        return !inParagraph || number is null or "1." or "1)";
    }

    /// <summary>같은 글자(<c>-</c>·<c>*</c>·<c>_</c>) 셋 이상만 있는 줄(사이 공백 허용). 목록(<c>- - -</c>)보다 먼저 본다.</summary>
    private static bool IsRule(string rest)
    {
        if (rest.Length == 0 || rest[0] is not ('-' or '*' or '_')) return false;
        int n = 0;
        foreach (char c in rest)
        {
            if (c == rest[0]) n++;
            else if (c is not (' ' or '\t')) return false;
        }
        return n >= 3;
    }

    /// <summary>
    /// j줄이 표 머리 줄이고 다음 줄이 칸 수가 같은 구분 줄(<c>|---|:--:|--:|</c>, |가 하나는 있어야 함)인지. 스트리밍 중 구분 줄이 아직 덜 왔으면
    /// 칸 수가 모자라 문단으로 남는다.
    /// </summary>
    private static bool IsTableStart(string[] lines, int j, out List<MdAlign> columns)
    {
        columns = [];
        if (j + 1 >= lines.Length || !HasPipe(lines[j]) || !lines[j + 1].Contains('|')) return false;
        var head = SplitRow(lines[j]);
        var delims = SplitRow(lines[j + 1]);
        if (head.Count != delims.Count) return false;
        foreach (var d in delims)
        {
            bool left = d.StartsWith(':'), right = d.EndsWith(':');
            int from = left ? 1 : 0, to = d.Length - (right && d.Length > 1 ? 1 : 0);
            if (to <= from || d.AsSpan(from, to - from).ContainsAnyExcept('-')) return false;
            columns.Add(left && right ? MdAlign.Center : right ? MdAlign.Right : MdAlign.Left);
        }
        return true;
    }

    /// <summary>표 한 줄의 칸 글(모자라면 빈 칸을 채우고 넘치면 자름).</summary>
    private static IReadOnlyList<IReadOnlyList<MdSpan>> TableRow(string line, int count)
    {
        var cells = SplitRow(line);
        var row = new IReadOnlyList<MdSpan>[count];
        for (int c = 0; c < count; c++) row[c] = c < cells.Count ? ParseInline(cells[c]) : [];
        return row;
    }

    /// <summary>
    /// 표 줄을 칸으로 나눈다. 앞뒤 |는 있어도 없어도 되고, <c>\|</c>는 칸을 나누지 않는 |(줄 안 코드 안에서도)다. 다른 <c>\</c> 표시는 글 안 꾸밈이 읽게 둔다.
    /// </summary>
    private static List<string> SplitRow(string line)
    {
        var s = line.Trim();
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool endedWithPipe = false;
        for (int i = s.StartsWith('|') ? 1 : 0; i < s.Length; i++)
        {
            char c = s[i];
            endedWithPipe = false;
            if (c == '\\' && i + 1 < s.Length)
            {
                if (s[i + 1] == '|') cell.Append('|');
                else cell.Append(c).Append(s[i + 1]);
                i++;
            }
            else if (c == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
                endedWithPipe = true;
            }
            else
            {
                cell.Append(c);
            }
        }
        if (!endedWithPipe) cells.Add(cell.ToString().Trim());
        return cells;
    }

    /// <summary>\로 막지 않은 |가 있는지.</summary>
    private static bool HasPipe(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\') i++;
            else if (line[i] == '|') return true;
        }
        return false;
    }

    private static bool IsFenceOpen(string rest, out char ch, out int len)
    {
        ch = rest.Length > 0 ? rest[0] : '\0';
        len = 0;
        if (ch is not ('`' or '~')) return false;
        while (len < rest.Length && rest[len] == ch) len++;
        // ``` 뒤 언어 이름에는 `가 올 수 없다(그러면 줄 안 코드).
        return len >= 3 && (ch == '~' || rest.IndexOf('`', len) < 0);
    }

    private static bool IsFenceClose(string rest, char ch, int len)
    {
        int n = 0;
        while (n < rest.Length && rest[n] == ch) n++;
        return n >= len && IsBlank(rest[n..]);
    }

    private static bool IsHeading(string rest, out int level, out string text)
    {
        level = 0;
        text = "";
        int n = 0;
        while (n < rest.Length && rest[n] == '#') n++;
        if (n is 0 or > 6 || n < rest.Length && rest[n] is not (' ' or '\t')) return false;
        var body = rest[n..].Trim();
        // 닫는 #들(앞에 공백이 있을 때만)은 뗀다.
        int end = body.Length;
        while (end > 0 && body[end - 1] == '#') end--;
        if (end == 0) body = "";
        else if (end < body.Length && body[end - 1] is ' ' or '\t') body = body[..end].TrimEnd();
        level = Math.Min(n, 3);
        text = body;
        return true;
    }

    /// <summary><c>- 글</c>·<c>* 글</c>·<c>+ 글</c>(number = null) 또는 <c>1. 글</c>·<c>1) 글</c>.</summary>
    private static bool IsListItem(string rest, out string? number, out string text)
    {
        number = null;
        text = "";
        if (rest.Length >= 2 && rest[0] is '-' or '*' or '+' && rest[1] is ' ' or '\t')
        {
            text = rest[2..];
            return !IsBlank(text);
        }
        int n = 0;
        while (n < rest.Length && n < 9 && char.IsAsciiDigit(rest[n])) n++;
        if (n == 0 || n + 1 >= rest.Length || rest[n] is not ('.' or ')') || rest[n + 1] is not (' ' or '\t')) return false;
        text = rest[(n + 2)..];
        if (IsBlank(text)) return false;
        number = rest[..(n + 1)];
        return true;
    }

    // ───────────── 글 안 꾸밈 ─────────────

    private abstract class Node;

    private sealed class TextNode(string text) : Node
    {
        public string Text { get; } = text;
    }

    private sealed class CodeNode(string text) : Node
    {
        public string Text { get; } = text;
    }

    private sealed class LinkNode(List<Node> children, string url) : Node
    {
        public List<Node> Children { get; } = children;
        public string Url { get; } = url;
    }

    /// <summary>
    /// *·_ 또는 ~~ 묶음. 짝을 찾으면 쓴 만큼 Count가 줄고 열거나 닫는 꾸밈 수가 붙는다. 남은 Count는 글자 그대로.
    /// ~는 꼭 두 개(<c>~~</c>)일 때만 묶음이 되고 같은 ~~끼리만 짝짓는다.
    /// </summary>
    private sealed class DelimNode(char ch, int count, bool canOpen, bool canClose) : Node
    {
        public char Ch { get; } = ch;
        public int Original { get; } = count;
        public int Count { get; set; } = count;
        public bool CanOpen { get; set; } = canOpen;
        public bool CanClose { get; set; } = canClose;
        public int OpenBold { get; set; }
        public int OpenItalic { get; set; }
        public int OpenStrike { get; set; }
        public int CloseBold { get; set; }
        public int CloseItalic { get; set; }
        public int CloseStrike { get; set; }
    }

    /// <summary>지금 자리에서 열려 있는 꾸밈 수.</summary>
    private struct Depth
    {
        public int Bold, Italic, Strike;
    }

    private static List<Node> Inline(string s, bool allowLinks)
    {
        var nodes = new List<Node>();
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length == 0) return;
            nodes.Add(new TextNode(text.ToString()));
            text.Clear();
        }

        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length && s[i + 1] is > ' ' and < '\x7f' && !char.IsAsciiLetterOrDigit(s[i + 1]))
            {
                text.Append(s[i + 1]);
                i += 2;
                continue;
            }
            if (c == '`')
            {
                int n = Run(s, i, '`');
                int close = FindBacktickRun(s, i + n, n);
                if (close < 0)
                {
                    text.Append(s, i, n);
                }
                else
                {
                    Flush();
                    nodes.Add(new CodeNode(CodeContent(s[(i + n)..close])));
                    n = close + n - i;
                }
                i += n;
                continue;
            }
            if (c is '*' or '_')
            {
                int n = Run(s, i, c);
                Flush();
                nodes.Add(Delimiter(s, i, n, c));
                i += n;
                continue;
            }
            if (c == '~')
            {
                int n = Run(s, i, c);
                if (n == 2)
                {
                    Flush();
                    nodes.Add(Delimiter(s, i, n, c));
                }
                else
                {
                    text.Append(s, i, n);
                }
                i += n;
                continue;
            }
            if (allowLinks && c == '[' && TryLink(s, i, out int linkEnd, out string label, out string url))
            {
                Flush();
                var children = Inline(label, allowLinks: false);
                ProcessEmphasis(children);
                nodes.Add(new LinkNode(children, url));
                i = linkEnd;
                continue;
            }
            if (allowLinks && c == '<' && TryAngleLink(s, i, out int angleEnd, out string angleUrl))
            {
                Flush();
                nodes.Add(new LinkNode([new TextNode(angleUrl)], angleUrl));
                i = angleEnd;
                continue;
            }
            if (allowLinks && c is 'h' or 'H' && (i == 0 || !char.IsLetterOrDigit(s[i - 1])) && BareUrlLength(s, i) is > 0 and var len)
            {
                Flush();
                var bare = s.Substring(i, len);
                nodes.Add(new LinkNode([new TextNode(bare)], bare));
                i += len;
                continue;
            }
            text.Append(c);
            i++;
        }
        Flush();
        if (allowLinks) ProcessEmphasis(nodes);
        return nodes;
    }

    private static int Run(string s, int i, char c)
    {
        int n = 0;
        while (i + n < s.Length && s[i + n] == c) n++;
        return n;
    }

    /// <summary>from부터 길이가 정확히 n인 ` 묶음의 자리, 없으면 -1.</summary>
    private static int FindBacktickRun(string s, int from, int n)
    {
        int j = from;
        while ((j = s.IndexOf('`', j)) >= 0)
        {
            int m = Run(s, j, '`');
            if (m == n) return j;
            j += m;
        }
        return -1;
    }

    /// <summary>줄 안 코드 내용: 줄바꿈은 공백, 양끝에 공백이 하나씩 있으면 뗀다(CommonMark).</summary>
    private static string CodeContent(string code)
    {
        code = code.Replace('\n', ' ');
        if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && !string.IsNullOrWhiteSpace(code)) code = code[1..^1];
        return code;
    }

    private static DelimNode Delimiter(string s, int i, int n, char c)
    {
        char? prev = i > 0 ? s[i - 1] : null;
        char? next = i + n < s.Length ? s[i + n] : null;
        bool left = !IsSpace(next) && (!IsPunct(next) || IsLoose(prev));
        bool right = !IsSpace(prev) && (!IsPunct(prev) || IsLoose(next));
        // ~~는 글자 사이에서도 짝지을 수 있다(GitHub처럼).
        if (c == '~') return new DelimNode(c, n, left, right);
        if (c == '*')
        {
            // 영문·숫자 사이의 *는 곱셈 따위로 보고 그대로 둔다(2*3*4, a*b).
            if (prev is { } p && next is { } q && char.IsAsciiLetterOrDigit(p) && char.IsAsciiLetterOrDigit(q)) return new DelimNode(c, n, false, false);
            return new DelimNode(c, n, left, right);
        }
        return new DelimNode(c, n, left && (!right || IsPunct(prev)), right && (!left || IsPunct(next)));
    }

    private static bool IsSpace(char? c) => c is not { } ch || char.IsWhiteSpace(ch);
    private static bool IsPunct(char? c) => c is { } ch && (char.IsPunctuation(ch) || char.IsSymbol(ch));
    /// <summary>강조 경계에서 공백·문장부호처럼 보는 글자(ASCII가 아닌 글자 포함 — 한글 조사가 바로 붙어도 되게).</summary>
    private static bool IsLoose(char? c) => IsSpace(c) || IsPunct(c) || c is { } ch && ch > '\x7f' && char.IsLetterOrDigit(ch);

    /// <summary>CommonMark의 "process emphasis": 왼쪽부터 닫는 묶음마다 가장 가까운 여는 묶음을 찾아 짝짓는다.</summary>
    private static void ProcessEmphasis(List<Node> nodes)
    {
        var delims = nodes.OfType<DelimNode>().ToList();
        for (int c = 0; c < delims.Count; c++)
        {
            var closer = delims[c];
            while (closer.CanClose && closer.Count > 0)
            {
                int found = -1;
                for (int o = c - 1; o >= 0; o--)
                {
                    var op = delims[o];
                    if (op.Ch != closer.Ch || !op.CanOpen || op.Count == 0) continue;
                    if ((op.CanClose || closer.CanOpen) && (op.Original + closer.Original) % 3 == 0
                        && !(op.Original % 3 == 0 && closer.Original % 3 == 0)) continue;
                    found = o;
                    break;
                }
                if (found < 0) break;
                var opener = delims[found];
                bool strong = opener.Count >= 2 && closer.Count >= 2;
                int use = strong ? 2 : 1;
                opener.Count -= use;
                closer.Count -= use;
                if (closer.Ch == '~')
                {
                    opener.OpenStrike++;
                    closer.CloseStrike++;
                }
                else if (strong)
                {
                    opener.OpenBold++;
                    closer.CloseBold++;
                }
                else
                {
                    opener.OpenItalic++;
                    closer.CloseItalic++;
                }
                // 사이에 남은 묶음은 짝을 잃고 글자 그대로.
                for (int k = found + 1; k < c; k++) delims[k].CanOpen = delims[k].CanClose = false;
            }
        }
    }

    private static void Flatten(List<Node> nodes, MdStyle outer, string? url, ref Depth depth, List<MdSpan> spans)
    {
        MdStyle Style(in Depth d) => outer | (d.Bold > 0 ? MdStyle.Bold : 0) | (d.Italic > 0 ? MdStyle.Italic : 0) | (d.Strike > 0 ? MdStyle.Strike : 0);
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode t:
                    Add(spans, t.Text, Style(depth), url);
                    break;
                case CodeNode code:
                    Add(spans, code.Text, Style(depth) | MdStyle.Code, url);
                    break;
                case LinkNode link:
                    var linkDepth = new Depth();
                    Flatten(link.Children, Style(depth), link.Url, ref linkDepth, spans);
                    break;
                case DelimNode d:
                    depth.Bold -= d.CloseBold;
                    depth.Italic -= d.CloseItalic;
                    depth.Strike -= d.CloseStrike;
                    if (d.Count > 0) Add(spans, new string(d.Ch, d.Count), Style(depth), url);
                    depth.Bold += d.OpenBold;
                    depth.Italic += d.OpenItalic;
                    depth.Strike += d.OpenStrike;
                    break;
            }
        }
    }

    private static void Add(List<MdSpan> spans, string text, MdStyle style, string? url)
    {
        if (text.Length == 0) return;
        if (spans.Count > 0 && spans[^1] is var last && last.Style == style && last.Url == url)
            spans[^1] = last with { Text = last.Text + text };
        else
            spans.Add(new MdSpan(text, style, url));
    }

    // ───────────── 링크 ─────────────

    private static bool IsWebUrl(string url)
        => (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
           && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0;

    /// <summary><c>[글](http(s)://주소)</c>. 글 안에 대괄호, 주소 안에 공백이 있거나 http·https가 아니면 링크가 아니다.</summary>
    private static bool TryLink(string s, int i, out int end, out string label, out string url)
    {
        end = 0;
        label = url = "";
        int close = s.IndexOf(']', i + 1);
        if (close < 0 || close + 1 >= s.Length || s[close + 1] != '(' || s.IndexOf('[', i + 1, close - i - 1) >= 0) return false;
        int depth = 0, j = close + 2;
        for (; j < s.Length; j++)
        {
            char c = s[j];
            if (char.IsWhiteSpace(c)) return false;
            if (c == '(') depth++;
            else if (c == ')' && depth-- == 0) break;
        }
        if (j >= s.Length) return false;
        label = s[(i + 1)..close];
        url = s[(close + 2)..j];
        if (label.Trim().Length == 0 || !IsWebUrl(url)) return false;
        end = j + 1;
        return true;
    }

    /// <summary><c>&lt;http(s)://주소&gt;</c>.</summary>
    private static bool TryAngleLink(string s, int i, out int end, out string url)
    {
        end = 0;
        url = "";
        int close = s.IndexOf('>', i + 1);
        if (close < 0) return false;
        var inner = s[(i + 1)..close];
        if (inner.Any(char.IsWhiteSpace) || inner.Contains('<') || !IsWebUrl(inner)) return false;
        url = inner;
        end = close + 1;
        return true;
    }

    /// <summary>
    /// i에서 시작하는 맨 주소(http·https)의 길이, 아니면 0. 공백·&lt;·&gt;·"·`·ASCII가 아닌 글자(뒤에 붙은 한글 조사 등)에서 끝나고,
    /// 끝의 문장부호와 짝 없는 닫는 괄호는 뺀다.
    /// </summary>
    private static int BareUrlLength(string s, int i)
    {
        int schemeEnd = s.AsSpan(i).StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? i + 8
            : s.AsSpan(i).StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? i + 7 : -1;
        if (schemeEnd < 0) return 0;
        int end = schemeEnd;
        while (end < s.Length && s[end] is > ' ' and < '\x7f' and not ('<' or '>' or '"' or '`')) end++;
        while (end > schemeEnd)
        {
            char last = s[end - 1];
            if (last is '.' or ',' or ':' or ';' or '!' or '?' or '\'' or '*' or '_' or '~')
            {
                end--;
                continue;
            }
            if (last == ')' && Count(s, i, end, '(') < Count(s, i, end, ')'))
            {
                end--;
                continue;
            }
            break;
        }
        return end > schemeEnd && IsWebUrl(s[i..end]) ? end - i : 0;
    }

    private static int Count(string s, int from, int to, char c)
    {
        int n = 0;
        for (int k = from; k < to; k++) if (s[k] == c) n++;
        return n;
    }
}
