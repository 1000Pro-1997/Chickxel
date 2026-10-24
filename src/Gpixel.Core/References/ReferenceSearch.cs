using Gpixel.Core.Help;

namespace Gpixel.Core.References;

/// <summary>
/// 참고 자료 찾기 조건. 글(<see cref="Text"/>)은 제목·태그(×3)·설명·파일 이름·종류·메모 본문을 <see cref="HelpSearch"/>(BM25, 낱말 + 두 글자)로 찾는다.
/// 거르기(종류·크기·프레임·색 수)와 <see cref="SimilarTo"/>는 그림에만 해당하고, 하나라도 있으면 메모·규칙은 결과에서 빠진다.
/// </summary>
public sealed class ReferenceQuery
{
    public string? Text { get; init; }
    public string? Kind { get; init; }
    /// <summary>정확한 가로·세로(px).</summary>
    public int? Width { get; init; }
    public int? Height { get; init; }
    /// <summary>긴 변 범위(px).</summary>
    public int? MinSide { get; init; }
    public int? MaxSide { get; init; }
    public int? MinFrames { get; init; }
    public int? MaxFrames { get; init; }
    public int? MaxColors { get; init; }
    /// <summary>이 팔레트·실루엣과 닮은 순으로(글이 있으면 글 점수와 섞음).</summary>
    public ReferenceSignature? SimilarTo { get; init; }
    /// <summary>결과에서 뺄 id(비슷한 것 찾기의 기준 자료 자신).</summary>
    public string? ExcludeId { get; init; }
    public bool IncludeImages { get; init; } = true;
    public bool IncludeNotes { get; init; } = true;
    public bool IncludeRules { get; init; } = true;
    /// <summary>결과 수 상한(0 이하 = 모두).</summary>
    public int Limit { get; init; } = 5;

    /// <summary>그림에만 해당하는 거르기나 닮음 조건이 있는지(있으면 메모·규칙은 빠짐).</summary>
    public bool HasImageFilter
        => Kind is not null || Width is not null || Height is not null || MinSide is not null || MaxSide is not null
           || MinFrames is not null || MaxFrames is not null || MaxColors is not null || SimilarTo is not null;
}

/// <summary>찾은 자료: 점수(0~1, 클수록 맞음)와 글 항목이면 찾은 곳 둘레 글.</summary>
public sealed record ReferenceHit(ReferenceItem Item, double Score, string? Snippet);

public sealed partial class ReferenceLibrary
{
    /// <summary>글 점수와 닮음을 섞는 비율(글 쪽).</summary>
    private const double TextWeight = 0.65;

    /// <summary>
    /// 조건에 맞는 자료를 점수 순으로. 글이 있으면 글이 맞는 것만(점수 = 1등 대비 비율, 닮음이 있으면 65:35로 섞음),
    /// 글 없이 닮음만 있으면 닮은 순, 둘 다 없으면 최근에 바뀐 것부터(기본 예시는 뒤).
    /// </summary>
    public IReadOnlyList<ReferenceHit> Search(ReferenceQuery q)
    {
        var candidates = new List<ReferenceItem>();
        if (q.IncludeImages) candidates.AddRange(AllItems.Where(i => i.IsImage && Passes(i, q)));
        if (!q.HasImageFilter)
        {
            if (q.IncludeNotes) candidates.AddRange(_items.Where(i => i.Type == ReferenceTypes.Note));
            if (q.IncludeRules) candidates.AddRange(RulesItems());
        }
        if (q.ExcludeId is { } exclude) candidates.RemoveAll(i => i.Id == exclude);

        Dictionary<string, HelpSearchResult>? text = null;
        double top = 1;
        if (!string.IsNullOrWhiteSpace(q.Text))
        {
            var search = new HelpSearch(candidates.Select(i => new HelpDocument(i.Id, SearchTitle(i), SearchText(i))));
            var found = search.Search(q.Text, int.MaxValue);
            text = found.ToDictionary(r => r.PageId, StringComparer.Ordinal);
            if (found.Count > 0) top = found[0].Score;
        }

        var hits = new List<ReferenceHit>();
        foreach (var item in candidates)
        {
            double? similarity = q.SimilarTo is { } s && item.IsImage ? ReferenceAnalysis.Similarity(s, ReferenceSignature.Of(item)) : null;
            double score;
            string? snippet = null;
            if (text is not null)
            {
                if (!text.TryGetValue(item.Id, out var t)) continue;
                score = t.Score / top;
                if (similarity is { } sim) score = TextWeight * score + (1 - TextWeight) * sim;
                if (!item.IsImage) snippet = t.Snippet;
            }
            else score = similarity ?? 0;
            hits.Add(new ReferenceHit(item, Math.Round(score, 4), snippet));
        }
        IEnumerable<ReferenceHit> ordered = hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Item.BuiltIn)
            .ThenByDescending(h => h.Item.UpdatedUtc)
            .ThenBy(h => h.Item.Id, StringComparer.Ordinal);
        if (q.Limit > 0) ordered = ordered.Take(q.Limit);
        return [.. ordered];
    }

    private static bool Passes(ReferenceItem i, ReferenceQuery q)
    {
        int side = Math.Max(i.Width, i.Height);
        return (q.Kind is null || i.Kind == q.Kind)
            && (q.Width is null || i.Width == q.Width)
            && (q.Height is null || i.Height == q.Height)
            && (q.MinSide is null || side >= q.MinSide)
            && (q.MaxSide is null || side <= q.MaxSide)
            && (q.MinFrames is null || i.Frames >= q.MinFrames)
            && (q.MaxFrames is null || i.Frames <= q.MaxFrames)
            && (q.MaxColors is null || i.Colors <= q.MaxColors);
    }

    /// <summary>검색 제목: 제목 + 태그(제목 무게).</summary>
    private static string SearchTitle(ReferenceItem i) => i.Tags.Count == 0 ? i.Title : $"{i.Title} {string.Join(' ', i.Tags)}";

    /// <summary>검색 본문: 메모·규칙 본문(앞에 — 찾은 곳 둘레 글이 여기서 나옴), 설명, 파일 이름 낱말, 종류(영어·한국어), 기본 예시의 영어 낱말.</summary>
    private string SearchText(ReferenceItem i)
    {
        var body = ReadText(i);
        var file = i.BuiltIn ? "" : Path.GetFileNameWithoutExtension(i.Id).Replace('_', ' ').Replace('-', ' ');
        var kind = i.IsImage ? KindSearchWords(i.Kind) : "";
        return string.Join('\n', new[] { body, i.Description, file, kind, i.Keywords, i.IsImage ? $"{i.Width}x{i.Height} {i.Width}px" : null }
            .Where(s => !string.IsNullOrEmpty(s)));
    }

    private static string KindSearchWords(string kind) => kind switch
    {
        ReferenceKinds.Character => "character 캐릭터",
        ReferenceKinds.Item => "item 아이템",
        ReferenceKinds.Tile => "tile 타일",
        ReferenceKinds.Effect => "effect 이펙트 효과",
        ReferenceKinds.Ui => "ui 아이콘",
        _ => "",
    };
}
