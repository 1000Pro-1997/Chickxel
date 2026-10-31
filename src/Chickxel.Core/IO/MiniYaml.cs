using System.Text;

namespace Chickxel.Core.IO;

/// <summary>YAML 노드: 매핑, 시퀀스, 스칼라.</summary>
internal abstract class YamlNode
{
    /// <summary>매핑이면 키의 값, 아니면 null.</summary>
    public YamlNode? this[string key] => this is YamlMapping m ? m.Get(key) : null;

    /// <summary>스칼라 문자열 값(스칼라가 아니면 null).</summary>
    public string? Text => (this as YamlScalar)?.Value;

    public IReadOnlyList<YamlNode> Items => this is YamlSequence s ? s.Items : [];
}

internal sealed class YamlScalar(string value) : YamlNode
{
    public string Value { get; } = value;
    public override string ToString() => Value;
}

internal sealed class YamlSequence : YamlNode
{
    public new List<YamlNode> Items { get; } = new();
}

internal sealed class YamlMapping : YamlNode
{
    public List<KeyValuePair<string, YamlNode>> Entries { get; } = new();

    public YamlNode? Get(string key)
    {
        foreach (var e in Entries)
            if (e.Key == key) return e.Value;
        return null;
    }
}

/// <summary>
/// Unity 메타/에셋 파일에 쓰이는 YAML 부분 집합 파서: 들여쓰기 블록 매핑·시퀀스(키와 같은 들여쓰기의 "- " 포함),
/// 흐름 매핑/시퀀스(<c>{x: 0, y: 1}</c>, <c>[]</c>), 작은따옴표·큰따옴표 문자열, 일반 스칼라.
/// 앵커·태그·여러 줄 블록 스칼라는 다루지 않는다(문서 머리 <c>---</c>, <c>%</c> 지시어는 건너뜀).
/// </summary>
internal static class MiniYaml
{
    private sealed class Line(int indent, string text)
    {
        public int Indent = indent;
        public string Text = text;
    }

    public static YamlNode Parse(string text)
    {
        var lines = new List<Line>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmedEnd = raw.TrimEnd();
            if (trimmedEnd.Length == 0) continue;
            int indent = 0;
            while (indent < trimmedEnd.Length && trimmedEnd[indent] == ' ') indent++;
            var body = trimmedEnd[indent..];
            if (body.StartsWith('#') || body.StartsWith('%') || body.StartsWith("---")) continue;
            lines.Add(new Line(indent, body));
        }
        if (lines.Count == 0) return new YamlMapping();
        int pos = 0;
        return ParseBlock(lines, ref pos, lines[0].Indent);
    }

    private static bool IsSequenceItem(string text) => text == "-" || text.StartsWith("- ");

    private static YamlNode ParseBlock(List<Line> lines, ref int pos, int indent)
        => IsSequenceItem(lines[pos].Text) ? ParseSequence(lines, ref pos, indent) : ParseMapping(lines, ref pos, indent);

    private static YamlSequence ParseSequence(List<Line> lines, ref int pos, int indent)
    {
        var seq = new YamlSequence();
        while (pos < lines.Count && lines[pos].Indent == indent && IsSequenceItem(lines[pos].Text))
        {
            var line = lines[pos];
            var rest = line.Text.Length > 1 ? line.Text[2..].TrimStart() : "";
            if (rest.Length == 0)
            {
                pos++;
                seq.Items.Add(pos < lines.Count && lines[pos].Indent > indent
                    ? ParseBlock(lines, ref pos, lines[pos].Indent)
                    : new YamlScalar(""));
                continue;
            }
            if (IsSequenceItem(rest) || FindKeySeparator(rest) >= 0)
            {
                // "- key: value" → 항목 내용을 "- " 뒤 들여쓰기로 옮겨 블록으로 다시 읽는다.
                line.Indent = indent + (line.Text.Length - rest.Length);
                line.Text = rest;
                seq.Items.Add(ParseBlock(lines, ref pos, line.Indent));
                continue;
            }
            pos++;
            seq.Items.Add(ParseInline(rest));
        }
        return seq;
    }

    private static YamlMapping ParseMapping(List<Line> lines, ref int pos, int indent)
    {
        var map = new YamlMapping();
        while (pos < lines.Count && lines[pos].Indent == indent && !IsSequenceItem(lines[pos].Text))
        {
            var text = lines[pos].Text;
            int sep = FindKeySeparator(text);
            if (sep < 0)
            {
                // 형식에 맞지 않는 줄은 건너뛴다(관대한 읽기).
                pos++;
                continue;
            }
            var key = Unquote(text[..sep].Trim());
            var valueText = text[(sep + 1)..].Trim();
            pos++;
            YamlNode value;
            if (valueText.Length > 0)
                value = ParseInline(valueText);
            else if (pos < lines.Count && lines[pos].Indent > indent)
                value = ParseBlock(lines, ref pos, lines[pos].Indent);
            else if (pos < lines.Count && lines[pos].Indent == indent && IsSequenceItem(lines[pos].Text))
                value = ParseSequence(lines, ref pos, indent);
            else
                value = new YamlScalar("");
            map.Entries.Add(new(key, value));
        }
        return map;
    }

    /// <summary>"key: value" 또는 "key:"의 ':' 위치. 키가 아니면 -1.</summary>
    private static int FindKeySeparator(string text)
    {
        if (text.Length == 0 || text[0] is '{' or '[') return -1;
        int start = 0;
        if (text[0] is '\'' or '"')
        {
            int end = FindQuoteEnd(text, 0);
            if (end < 0) return -1;
            start = end + 1;
        }
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] != ':') continue;
            if (i == text.Length - 1 || text[i + 1] == ' ') return i;
        }
        return -1;
    }

    /// <summary>시작 따옴표에 대응하는 닫는 따옴표 위치.</summary>
    private static int FindQuoteEnd(string text, int start)
    {
        char q = text[start];
        for (int i = start + 1; i < text.Length; i++)
        {
            if (q == '\'' && text[i] == '\'')
            {
                if (i + 1 < text.Length && text[i + 1] == '\'') { i++; continue; }
                return i;
            }
            if (q == '"')
            {
                if (text[i] == '\\') { i++; continue; }
                if (text[i] == '"') return i;
            }
        }
        return -1;
    }

    private static YamlNode ParseInline(string text)
    {
        if (text[0] is '{' or '[')
        {
            int pos = 0;
            return ParseFlow(text, ref pos);
        }
        return new YamlScalar(Unquote(text));
    }

    private static YamlNode ParseFlow(string text, ref int pos)
    {
        SkipSpaces(text, ref pos);
        if (pos >= text.Length) return new YamlScalar("");
        char c = text[pos];
        if (c == '{')
        {
            pos++;
            var map = new YamlMapping();
            while (true)
            {
                SkipSpaces(text, ref pos);
                if (pos >= text.Length) break;
                if (text[pos] == '}') { pos++; break; }
                if (text[pos] == ',') { pos++; continue; }
                var key = ReadFlowScalar(text, ref pos, isKey: true);
                SkipSpaces(text, ref pos);
                YamlNode value = new YamlScalar("");
                if (pos < text.Length && text[pos] == ':')
                {
                    pos++;
                    value = ParseFlow(text, ref pos);
                }
                map.Entries.Add(new(key, value));
            }
            return map;
        }
        if (c == '[')
        {
            pos++;
            var seq = new YamlSequence();
            while (true)
            {
                SkipSpaces(text, ref pos);
                if (pos >= text.Length) break;
                if (text[pos] == ']') { pos++; break; }
                if (text[pos] == ',') { pos++; continue; }
                seq.Items.Add(ParseFlow(text, ref pos));
            }
            return seq;
        }
        return new YamlScalar(ReadFlowScalar(text, ref pos, isKey: false));
    }

    private static string ReadFlowScalar(string text, ref int pos, bool isKey)
    {
        if (text[pos] is '\'' or '"')
        {
            int end = FindQuoteEnd(text, pos);
            if (end < 0) end = text.Length - 1;
            var quoted = text[pos..(end + 1)];
            pos = end + 1;
            return Unquote(quoted);
        }
        int start = pos;
        while (pos < text.Length && text[pos] is not (',' or '}' or ']'))
        {
            if (isKey && text[pos] == ':' && (pos + 1 >= text.Length || text[pos + 1] == ' ')) break;
            pos++;
        }
        return text[start..pos].Trim();
    }

    private static void SkipSpaces(string text, ref int pos)
    {
        while (pos < text.Length && text[pos] == ' ') pos++;
    }

    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
            return text[1..^1].Replace("''", "'");
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            var sb = new StringBuilder();
            for (int i = 1; i < text.Length - 1; i++)
            {
                char ch = text[i];
                if (ch != '\\' || i + 1 >= text.Length - 1) { sb.Append(ch); continue; }
                char n = text[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break;
                    case 'u' when i + 4 <= text.Length - 2
                                  && int.TryParse(text.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var code):
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
        // 일반 스칼라 뒤 주석 제거
        int hash = text.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0 ? text[..hash].TrimEnd() : text;
    }

    /// <summary>
    /// YAML 일반 스칼라로 쓰기 안전하면 그대로, 아니면 작은따옴표로 감싼다(Unity 직렬화와 같은 방식).
    /// </summary>
    public static string QuoteIfNeeded(string value)
    {
        if (value.Length == 0) return "''";
        bool needs = value[0] is ' ' or '-' or '?' or ':' or ',' or '[' or ']' or '{' or '}' or '#' or '&' or '*'
                         or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`'
                     || value[^1] == ' '
                     || value.Contains(": ", StringComparison.Ordinal)
                     || value.EndsWith(':')
                     || value.Contains(" #", StringComparison.Ordinal)
                     || value.Any(ch => ch < ' ');
        if (!needs) return value;
        if (value.Any(ch => ch < ' '))
        {
            var sb = new StringBuilder("\"");
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (ch < ' ') sb.Append($"\\u{(int)ch:X4}");
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
        return "'" + value.Replace("'", "''") + "'";
    }
}
