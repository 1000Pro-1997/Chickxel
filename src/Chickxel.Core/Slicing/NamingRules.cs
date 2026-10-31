using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Chickxel.Core.Model;

namespace Chickxel.Core.Slicing;

/// <summary>
/// 일괄 이름 규칙. 틀(template)의 자리표시자:
/// <c>{name}</c> 기본 이름(파일 이름), <c>{index}</c> 목록 순번, <c>{row}</c>/<c>{col}</c> 행·열 번호,
/// <c>{rowname}</c> 행 이름(<see cref="RowNames"/>, 없으면 <c>{name}_{row}</c>).
/// 숫자 자리표시자는 서식을 붙일 수 있다(예: <c>{index:00}</c> → 01, 02 …).
/// </summary>
public sealed record NamingOptions
{
    public string Template { get; init; } = NamingRules.IndexTemplate;
    public string BaseName { get; init; } = "sprite";
    /// <summary>번호 시작 값(index·row·col 모두).</summary>
    public int Start { get; init; }
    /// <summary>행 단위 이름(0번째 = 첫 행). 예: walk_down, walk_left …</summary>
    public IReadOnlyList<string>? RowNames { get; init; }
}

public static partial class NamingRules
{
    /// <summary><c>{파일}_{번호}</c> (Unity 기본 규칙).</summary>
    public const string IndexTemplate = "{name}_{index}";
    /// <summary><c>{파일}_{행}_{열}</c>.</summary>
    public const string RowColTemplate = "{name}_{row}_{col}";
    /// <summary>행 이름 + 열 번호(예: walk_down_0).</summary>
    public const string RowNamesTemplate = "{rowname}_{col}";

    /// <summary>접두어 + 번호 틀. <paramref name="digits"/>가 1보다 크면 0으로 채운다.</summary>
    public static string PrefixTemplate(string prefix, int digits = 0)
        => prefix.Replace("{", "{{").Replace("}", "}}") + (digits > 1 ? "{index:" + new string('0', digits) + "}" : "{index}");

    [GeneratedRegex(@"\{\{|\}\}|\{(name|index|row|col|rowname)(?::([0#]+))?\}")]
    private static partial Regex Placeholder();

    /// <summary>
    /// 사각형 목록(이 순서가 index 순서)에 대한 이름들을 만든다. 행·열은 위치로 계산한다.
    /// 같은 이름이 생기면 뒤의 것에 <c>_1</c>, <c>_2</c> …를 붙인다.
    /// </summary>
    public static List<string> GenerateNames(IReadOnlyList<Rectangle> rects, NamingOptions options)
    {
        var rowOf = new int[rects.Count];
        var colOf = new int[rects.Count];
        var rows = SliceOrder.GroupRows(rects);
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < rows[r].Count; c++)
            {
                rowOf[rows[r][c]] = r;
                colOf[rows[r][c]] = c;
            }

        var names = new List<string>(rects.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < rects.Count; i++)
        {
            int row = rowOf[i], col = colOf[i];
            var name = Format(options, i + options.Start, row, col).Trim();
            if (name.Length == 0) name = $"{options.BaseName}_{i + options.Start}";
            var unique = name;
            for (int n = 1; !used.Add(unique); n++) unique = $"{name}_{n}";
            names.Add(unique);
        }
        return names;
    }

    private static string Format(NamingOptions o, int index, int row, int col)
        => Placeholder().Replace(o.Template, m =>
        {
            if (m.Value == "{{") return "{";
            if (m.Value == "}}") return "}";
            string fmt = m.Groups[2].Success ? m.Groups[2].Value : "0";
            string Num(int v) => v.ToString(fmt, CultureInfo.InvariantCulture);
            return m.Groups[1].Value switch
            {
                "name" => o.BaseName,
                "index" => Num(index),
                "row" => Num(row + o.Start),
                "col" => Num(col + o.Start),
                _ => o.RowNames is { } rn && row < rn.Count && !string.IsNullOrWhiteSpace(rn[row])
                    ? rn[row].Trim()
                    : $"{o.BaseName}_{Num(row + o.Start)}",
            };
        });

    /// <summary>사각형들로 새 칸을 만든다(이름 규칙 적용, 모두 같은 피벗).</summary>
    public static List<Slice> CreateSlices(IReadOnlyList<Rectangle> rects, NamingOptions options, Pivot pivot)
    {
        var names = GenerateNames(rects, options);
        return rects.Select((r, i) => new Slice(names[i], r, pivot)).ToList();
    }

    /// <summary>
    /// 문서의 일부(또는 전체) 칸 이름을 규칙대로 바꾼다. 애니메이션 프레임이 가리키는 이름도 함께 바꾸고,
    /// 대상이 아닌 칸과 이름이 겹치면 <c>_1</c> … 을 붙인다. 문서 변경을 알린다.
    /// </summary>
    /// <param name="targets">바꿀 칸들(이 순서가 index 순서). null이면 문서 전체.</param>
    public static void Rename(Document doc, IReadOnlyList<Slice>? targets, NamingOptions options)
    {
        targets ??= doc.Slices.ToList();
        if (targets.Count == 0) return;
        var newNames = GenerateNames(targets.Select(s => s.Rect).ToList(), options);
        var targetSet = targets.ToHashSet();
        var taken = doc.Slices.Where(s => !targetSet.Contains(s)).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < targets.Count; i++)
        {
            var name = newNames[i];
            var unique = name;
            for (int n = 1; taken.Contains(unique) || !assigned.Add(unique); n++) unique = $"{name}_{n}";
            map.TryAdd(targets[i].Name, unique);
            targets[i].Name = unique;
        }
        foreach (var frame in doc.Animations.SelectMany(a => a.Frames))
            if (map.TryGetValue(frame.Slice, out var renamed)) frame.Slice = renamed;
        doc.NotifyChanged(DocumentChange.Slices | DocumentChange.Animations);
    }

    /// <summary>칸 이름으로 쓸 수 없는 문자(제어 문자)를 빼고 앞뒤 공백을 없앤다.</summary>
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            if (!char.IsControl(ch)) sb.Append(ch);
        return sb.ToString().Trim();
    }
}
