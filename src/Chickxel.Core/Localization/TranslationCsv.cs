using System.Text;

namespace Chickxel.Core.Localization;

/// <summary>번역 CSV의 언어 한 열: 언어 코드(예: "ja"), 표시 이름(예: "日本語"), 문구 키 → 번역(빈 칸은 담지 않음).</summary>
public sealed record TranslationColumn(string Code, string Name, IReadOnlyDictionary<string, string> Values);

/// <summary>읽은 번역 CSV: 문구 키(파일 순서)와 언어 열들.</summary>
public sealed record TranslationSheet(IReadOnlyList<string> Keys, IReadOnlyList<TranslationColumn> Columns);

/// <summary>
/// 번역 CSV. 첫 줄 = <c>key,&lt;언어 코드&gt;,…</c>, 둘째 줄(선택) = <c>#name,&lt;언어 이름&gt;,…</c>, 나머지 = 문구 키와 언어별 번역.
/// 문구 키 = <c>&lt;표&gt;.&lt;리소스 키&gt;</c>(예: <c>UiStrings.S_AboutDialog_…</c>). 쓸 때는 쉼표·UTF-8(BOM, 엑셀이 한글을 바로 읽음)·CRLF,
/// 읽을 때는 첫 줄로 구분자(쉼표·세미콜론·탭)를 고른다(지역 설정에 따라 엑셀이 세미콜론으로 저장함).
/// </summary>
public static class TranslationCsv
{
    public const string KeyHeader = "key";
    public const string NameRow = "#name";

    /// <summary>언어 코드로 쓸 수 있는지(파일 이름이 되므로 영문자·숫자·-·_만, 32자까지).</summary>
    public static bool IsValidCode(string code)
        => code.Length is > 0 and <= 32 && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static void Write(TextWriter writer, IReadOnlyList<string> keys, IReadOnlyList<TranslationColumn> columns)
    {
        var line = new StringBuilder();
        bool first = true;
        void Flush()
        {
            writer.Write(line);
            writer.Write("\r\n");
            line.Clear();
            first = true;
        }
        void Cell(string value)
        {
            if (!first) line.Append(',');
            first = false;
            bool quote = value.Length > 0 && (value.AsSpan().IndexOfAny(",\"\r\n;\t") >= 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));
            if (!quote)
            {
                line.Append(value);
                return;
            }
            line.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        }

        Cell(KeyHeader);
        foreach (var c in columns) Cell(c.Code);
        Flush();
        Cell(NameRow);
        foreach (var c in columns) Cell(c.Name);
        Flush();
        foreach (var key in keys)
        {
            Cell(key);
            foreach (var c in columns) Cell(c.Values.TryGetValue(key, out var v) ? v : "");
            Flush();
        }
    }

    /// <summary>
    /// 읽는다. 첫 칸이 <see cref="KeyHeader"/>가 아니거나 언어 코드가 겹치면 <see cref="InvalidDataException"/>.
    /// 빈 칸은 번역하지 않은 문구로 보고 담지 않으며, 같은 키가 여러 줄이면 나중 줄을 쓴다.
    /// </summary>
    public static TranslationSheet Read(TextReader reader)
    {
        var text = reader.ReadToEnd();
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        var rows = Parse(text, DetectDelimiter(text));
        if (rows.Count == 0 || rows[0].Count == 0 || !string.Equals(rows[0][0].Trim(), KeyHeader, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(CoreStrings.TranslationCsv_NoKeyHeader);

        var header = rows[0];
        var codes = header.Skip(1).Select(c => c.Trim()).ToList();
        var duplicate = codes.Where(c => c.Length > 0).GroupBy(c => c, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException(CoreStrings.FormatTranslationCsv_DuplicateLanguage(duplicate.Key));

        var names = new string[codes.Count];
        var values = codes.Select(_ => new Dictionary<string, string>(StringComparer.Ordinal)).ToArray();
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.Skip(1))
        {
            var key = row.Count > 0 ? row[0].Trim() : "";
            if (key.Length == 0) continue;
            bool isName = string.Equals(key, NameRow, StringComparison.OrdinalIgnoreCase);
            if (!isName && seen.Add(key)) keys.Add(key);
            for (int i = 0; i < codes.Count && i + 1 < row.Count; i++)
            {
                var cell = row[i + 1];
                if (cell.Length == 0) continue;
                if (isName) names[i] = cell.Trim();
                else values[i][key] = cell;
            }
        }

        var columns = new List<TranslationColumn>();
        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i].Length == 0) continue;
            columns.Add(new TranslationColumn(codes[i], string.IsNullOrWhiteSpace(names[i]) ? codes[i] : names[i], values[i]));
        }
        return new TranslationSheet(keys, columns);
    }

    /// <summary>첫 줄(따옴표 밖)에 가장 많이 나온 구분자. 없으면 쉼표.</summary>
    private static char DetectDelimiter(string text)
    {
        int comma = 0, semicolon = 0, tab = 0;
        bool quoted = false;
        foreach (var ch in text)
        {
            if (ch == '"') quoted = !quoted;
            else if (quoted) continue;
            else if (ch is '\r' or '\n') break;
            else if (ch == ',') comma++;
            else if (ch == ';') semicolon++;
            else if (ch == '\t') tab++;
        }
        if (semicolon > comma && semicolon >= tab) return ';';
        if (tab > comma && tab > semicolon) return '\t';
        return ',';
    }

    /// <summary>RFC 4180: 따옴표 칸 안의 구분자·줄바꿈은 글자, <c>""</c>는 따옴표 하나. 빈 줄은 건너뛴다.</summary>
    private static List<List<string>> Parse(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false, any = false;
        void EndCell()
        {
            row.Add(cell.ToString());
            cell.Clear();
        }
        void EndRow()
        {
            EndCell();
            if (any) rows.Add(row);
            row = new List<string>();
            any = false;
        }
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch != '"') cell.Append(ch);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else quoted = false;
                continue;
            }
            if (ch == '"')
            {
                quoted = any = true;
            }
            else if (ch == delimiter)
            {
                any = true;
                EndCell();
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRow();
            }
            else
            {
                any = true;
                cell.Append(ch);
            }
        }
        if (any || cell.Length > 0) EndRow();
        return rows;
    }
}
