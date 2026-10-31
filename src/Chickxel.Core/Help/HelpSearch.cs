using System.Text;

namespace Chickxel.Core.Help;

/// <summary>검색할 도움말 쪽 하나(쪽 id, 제목, 본문 글자).</summary>
public sealed record HelpDocument(string PageId, string Title, string Text);

/// <summary>검색 결과 한 줄: 쪽 id, 점수(클수록 맞음), 본문에서 찾은 곳 둘레 글.</summary>
public sealed record HelpSearchResult(string PageId, double Score, string Snippet);

/// <summary>
/// 사용 가이드 로컬 검색(모델·네트워크 없음). 낱말을 통째로 + 글자 두 개씩(bigram) 색인해
/// 한국어 조사가 붙은 말("팔레트가")과 영어 낱말 일부("palet")도 찾고, BM25로 점수를 매긴다(제목 낱말 ×3).
/// 질의 낱말이 제목·본문에 그대로 들어 있으면 덤 점수를 더한다.
/// </summary>
public sealed class HelpSearch
{
    private const double K1 = 1.2, B = 0.75, TitleWeight = 3;
    private const double TitleWordBonus = 4, TextWordBonus = 1.5, TitlePhraseBonus = 6, TextPhraseBonus = 3;
    /// <summary>1등 점수의 이 비율보다 낮은 결과는 버린다(글자 두 개만 우연히 겹친 쪽).</summary>
    private const double MinRelativeScore = 0.1;
    private const int SnippetLength = 80, SnippetLead = 25;

    private sealed record Entry(HelpDocument Doc, Dictionary<string, double> Tf, double Length);

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, int> _df = new();
    private readonly double _avgLength;

    public HelpSearch(IEnumerable<HelpDocument> documents)
    {
        foreach (var doc in documents)
        {
            var tf = new Dictionary<string, double>();
            double length = AddTerms(tf, doc.Text, 1) + AddTerms(tf, doc.Title, TitleWeight);
            _entries.Add(new Entry(doc, tf, length));
            foreach (var term in tf.Keys) _df[term] = _df.GetValueOrDefault(term) + 1;
        }
        _avgLength = _entries.Count == 0 ? 1 : Math.Max(1, _entries.Average(e => e.Length));
    }

    public int Count => _entries.Count;

    /// <summary>점수 순 결과(많아야 <paramref name="max"/>개). 빈 질의는 결과 없음.</summary>
    public IReadOnlyList<HelpSearchResult> Search(string? query, int max = 50)
    {
        var words = Words(query ?? "");
        if (words.Count == 0 || _entries.Count == 0) return [];
        var queryTerms = words.SelectMany(Terms).Distinct().ToList();
        var phrase = words.Count > 1 ? Normalize(query!) : null;
        int n = _entries.Count;

        var scored = new List<(Entry Entry, double Score)>();
        foreach (var e in _entries)
        {
            double score = 0;
            foreach (var term in queryTerms)
            {
                if (!e.Tf.TryGetValue(term, out var tf)) continue;
                int df = _df[term];
                double idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
                score += idf * tf * (K1 + 1) / (tf + K1 * (1 - B + B * e.Length / _avgLength));
            }
            foreach (var w in words)
            {
                if (Contains(e.Doc.Title, w)) score += TitleWordBonus;
                if (Contains(e.Doc.Text, w)) score += TextWordBonus;
            }
            if (phrase is not null)
            {
                if (Contains(Normalize(e.Doc.Title), phrase)) score += TitlePhraseBonus;
                if (Contains(Normalize(e.Doc.Text), phrase)) score += TextPhraseBonus;
            }
            if (score > 0) scored.Add((e, score));
        }
        if (scored.Count == 0) return [];
        double top = scored.Max(s => s.Score);
        return scored
            .Where(s => s.Score >= top * MinRelativeScore)
            .OrderByDescending(s => s.Score)
            .Take(max)
            .Select(s => new HelpSearchResult(s.Entry.Doc.PageId, s.Score, Snippet(s.Entry.Doc.Text, words)))
            .ToList();
    }

    /// <summary>노란색으로 칠할 질의 낱말(소문자, 겹침 없음). 대소문자 무시 부분 문자열로 찾는다.</summary>
    public static IReadOnlyList<string> HighlightTerms(string? query) => Words(query ?? "").Distinct().ToList();

    /// <summary>
    /// <paramref name="text"/>에서 실제로 칠할 말. 낱말이 그대로 없으면 앞에서부터 가장 긴 두 글자 이상 앞부분
    /// ("팔레트가" → "팔레트")을 쓰고, 그것도 없으면 뺀다.
    /// </summary>
    public static IReadOnlyList<string> ResolveTerms(string text, IReadOnlyList<string> terms)
    {
        var list = new List<string>();
        foreach (var term in terms)
        {
            for (int len = term.Length; len >= Math.Min(2, term.Length); len--)
            {
                var part = term[..len];
                if (!Contains(text, part)) continue;
                if (!list.Contains(part)) list.Add(part);
                break;
            }
        }
        return list;
    }

    /// <summary><paramref name="text"/> 안의 <paramref name="terms"/> 위치(대소문자 무시, 겹치면 합침, 앞에서부터).</summary>
    public static IReadOnlyList<(int Start, int Length)> Matches(string text, IReadOnlyList<string> terms)
    {
        var hits = new List<(int Start, int End)>();
        foreach (var term in terms)
        {
            if (term.Length == 0) continue;
            for (int i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = text.IndexOf(term, i + term.Length, StringComparison.OrdinalIgnoreCase))
                hits.Add((i, i + term.Length));
        }
        hits.Sort();
        var merged = new List<(int Start, int Length)>();
        int start = -1, end = -1;
        foreach (var (s, e) in hits)
        {
            if (s <= end) { end = Math.Max(end, e); continue; }
            if (start >= 0) merged.Add((start, end - start));
            (start, end) = (s, e);
        }
        if (start >= 0) merged.Add((start, end - start));
        return merged;
    }

    /// <summary>본문에서 질의 낱말이 처음 나오는 곳 둘레 약 80자(없으면 본문 앞부분).</summary>
    private static string Snippet(string text, IReadOnlyList<string> words)
    {
        var flat = Normalize(text);
        var matches = Matches(flat, ResolveTerms(flat, words));
        int at = matches.Count > 0 ? matches[0].Start : 0;
        int start = Math.Max(0, at - SnippetLead);
        int end = Math.Min(flat.Length, start + SnippetLength);
        start = Math.Max(0, Math.Min(start, end - SnippetLength));
        var sb = new StringBuilder();
        if (start > 0) sb.Append('…');
        sb.Append(flat, start, end - start);
        if (end < flat.Length) sb.Append('…');
        return sb.ToString();
    }

    /// <summary>소문자 낱말(글자·숫자가 이어진 것).</summary>
    internal static List<string> Words(string s)
    {
        var words = new List<string>();
        int start = -1;
        for (int i = 0; i <= s.Length; i++)
        {
            bool word = i < s.Length && char.IsLetterOrDigit(s[i]);
            if (word && start < 0) start = i;
            else if (!word && start >= 0)
            {
                words.Add(s[start..i].ToLowerInvariant());
                start = -1;
            }
        }
        return words;
    }

    /// <summary>낱말 하나의 색인 말: 통째(앞에 "=")와 글자 두 개씩. 한 글자 낱말은 그 글자 하나.</summary>
    internal static IEnumerable<string> Terms(string word)
    {
        yield return "=" + word;
        for (int i = 0; i + 2 <= word.Length; i++) yield return word.Substring(i, 2);
    }

    private static double AddTerms(Dictionary<string, double> tf, string text, double weight)
    {
        double length = 0;
        foreach (var word in Words(text))
        {
            foreach (var term in Terms(word))
            {
                tf[term] = tf.GetValueOrDefault(term) + weight;
                length += weight;
            }
        }
        return length;
    }

    private static bool Contains(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);

    /// <summary>공백(줄바꿈 포함)을 빈칸 하나로.</summary>
    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
