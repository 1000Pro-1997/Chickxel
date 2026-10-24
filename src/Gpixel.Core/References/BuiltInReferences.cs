using Gpixel.Core.Localization;
using Gpixel.Core.Model;

namespace Gpixel.Core.References;

/// <summary>앱에 들어 있는 기본 예시 그림 하나: 색인 항목(읽기 전용)과 프레임.</summary>
public sealed record BuiltInReference(ReferenceItem Item, ReferenceFrames Frames);

/// <summary>
/// 기본 자료: 예시 그림(<see cref="BuiltInExampleArt"/>, 제목·설명·태그는 <see cref="CoreStrings"/>)과
/// 기본 그림 규칙·사용자 규칙 틀(내장 리소스 <c>Gpixel.References.rules.&lt;언어&gt;.md</c>·<c>rules-template.&lt;언어&gt;.md</c>).
/// </summary>
public static class BuiltInReferences
{
    /// <summary>기본 자료 id 앞부분(<c>builtin/slime</c>).</summary>
    public const string IdPrefix = "builtin/";
    /// <summary>기본 규칙의 id.</summary>
    public const string RulesId = "builtin/rules";

    /// <summary>예시 그림(지금 화면 언어의 제목·설명·태그).</summary>
    public static IReadOnlyList<BuiltInReference> Examples()
    {
        var list = new List<BuiltInReference>();
        foreach (var art in BuiltInExampleArt.All)
        {
            var frames = art.Frames.Select(rows => Render(art, rows)).ToList();
            var item = new ReferenceItem
            {
                Id = IdPrefix + art.Key,
                Type = ReferenceTypes.Image,
                Title = Text($"Refs_Example_{art.Key}_Title"),
                Description = Text($"Refs_Example_{art.Key}_Description"),
                Tags = SplitTags(Text($"Refs_Example_{art.Key}_Tags")),
                Kind = ReferenceKinds.Parse(art.Kind) ?? ReferenceKinds.Other,
                CaptionedBy = CaptionSources.User,
                BuiltIn = true,
                // 화면 언어와 다른 말로 찾아도(영어 화면에서 "슬라임") 나오게 한국어·영어 제목·태그를 검색 낱말에 더한다.
                Keywords = string.Join(' ', [art.Keywords, .. BothLanguages($"Refs_Example_{art.Key}_Title"), .. BothLanguages($"Refs_Example_{art.Key}_Tags")]),
            };
            item.ApplyStats(ReferenceAnalysis.Analyze(frames));
            list.Add(new BuiltInReference(item, new ReferenceFrames(frames, [.. frames.Select(_ => art.DurationMs)])));
        }
        return list;
    }

    /// <summary>기본 규칙 항목(제목 = "Gpixel 기본 규칙", 본문 = <see cref="RulesText"/>).</summary>
    public static ReferenceItem Rules() => new()
    {
        Id = RulesId,
        Type = ReferenceTypes.Rules,
        Title = CoreStrings.Refs_BuiltInRules_Title,
        BuiltIn = true,
        CaptionedBy = CaptionSources.User,
    };

    /// <summary>기본 규칙 마크다운.</summary>
    public static string RulesText(bool korean) => Resource(korean ? "rules.ko.md" : "rules.en.md");

    /// <summary>사용자 rules.md를 처음 만들 때 넣는 틀.</summary>
    public static string RulesTemplate(bool korean) => Resource(korean ? "rules-template.ko.md" : "rules-template.en.md");

    /// <summary>"a, b ,c" → ["a","b","c"](빈 것·겹친 것 뺌, 대소문자 무시).</summary>
    public static List<string> SplitTags(string? text)
        => NormalizeTags((text ?? "").Split([',', '\n', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    /// <summary>태그 정리: 앞뒤 공백·'#' 떼고, 빈 것·겹친 것(대소문자 무시) 빼고, 많아야 24개·각 40자.</summary>
    public static List<string> NormalizeTags(IEnumerable<string> tags)
    {
        var list = new List<string>();
        foreach (var raw in tags)
        {
            var t = raw.Trim().TrimStart('#').Trim();
            if (t.Length == 0) continue;
            if (t.Length > 40) t = t[..40];
            if (list.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(t);
            if (list.Count == 24) break;
        }
        return list;
    }

    private static string Text(string key) => CoreStrings.GetResourceString(key) ?? "";

    private static PixelImage Render(BuiltInExampleArt.Art art, string[] rows)
    {
        var colors = art.Legend.ToDictionary(kv => kv.Key, kv => Color32.TryParseHex(kv.Value, out var c) ? c : Color32.Transparent);
        var image = new PixelImage(art.Width, art.Height);
        for (int y = 0; y < Math.Min(art.Height, rows.Length); y++)
        {
            var row = rows[y];
            for (int x = 0; x < Math.Min(art.Width, row.Length); x++)
                if (colors.TryGetValue(row[x], out var c)) image.Pixels[y * art.Width + x] = c;
        }
        return image;
    }

    /// <summary>기본(한국어)·영어 문구(검색 낱말용).</summary>
    private static IEnumerable<string> BothLanguages(string key)
    {
        foreach (var culture in new[] { System.Globalization.CultureInfo.InvariantCulture, System.Globalization.CultureInfo.GetCultureInfo("en") })
            if (CoreStrings.ResourceManager.GetString(key, culture) is { } s) yield return s;
    }

    private static string Resource(string name)
    {
        var full = "Gpixel.References." + name;
        using var stream = typeof(BuiltInReferences).Assembly.GetManifestResourceStream(full)
            ?? throw new InvalidOperationException(full);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
