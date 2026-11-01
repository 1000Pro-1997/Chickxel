using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Assets;

/// <summary>
/// 패키지 이름·짧은 설명의 언어 하나(manifest와 원격 카탈로그 항목의 <c>translations.&lt;언어 코드&gt;</c>).
/// 둘 중 하나는 있어야 하고, 빠진 것은 기본 이름·설명으로 보인다.
/// </summary>
public sealed record AssetTranslation(string? Name, string? Summary);

/// <summary>
/// 패키지 번역 묶음 <c>{ "en": { "name": "…", "summary": "…" } }</c>의 규칙·읽기·쓰기·고르기(서버와 같은 규칙):
/// 언어 <see cref="MaxLanguages"/>개까지, 키는 BCP-47 모양 언어 코드(대소문자 무시하고 겹치지 않음),
/// <c>name</c>은 <see cref="AssetManifest.IsValidName"/>, <c>summary</c>는 <see cref="RemoteCatalogFormat.IsValidSummary"/>, 둘 중 하나는 있어야 한다.
/// 모르는 안쪽 키는 무시한다(앞으로 늘어날 필드).
/// </summary>
public static class AssetTranslations
{
    /// <summary>번역할 수 있는 언어 수(스크립트 선언 <c>translations</c>와 같음).</summary>
    public const int MaxLanguages = ScriptTranslations.MaxLanguages;

    public static IReadOnlyDictionary<string, AssetTranslation> Empty { get; } = new Dictionary<string, AssetTranslation>(StringComparer.OrdinalIgnoreCase);

    /// <summary>언어 코드·값이 모두 규칙에 맞는지(언어 수, 코드 모양·겹침, 이름·설명 길이).</summary>
    public static bool IsValid(IEnumerable<KeyValuePair<string, AssetTranslation>> translations)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, translation) in translations)
            if (!ScriptTranslations.IsLanguageCode(code) || !seen.Add(code) || !IsValid(translation)) return false;
        return seen.Count <= MaxLanguages;
    }

    /// <summary>값 하나가 맞는지: 이름이나 설명 중 하나는 있고, 있는 것은 길이·글자 규칙에 맞음.</summary>
    public static bool IsValid(AssetTranslation translation)
        => (translation.Name is not null || translation.Summary is not null)
           && (translation.Name is null || AssetManifest.IsValidName(translation.Name))
           && (translation.Summary is null || RemoteCatalogFormat.IsValidSummary(translation.Summary));

    /// <summary>
    /// JSON 값을 읽는다. 없으면(null) 빈 묶음, 규칙에 맞지 않으면 false와 틀린 필드(<c>translations</c>, <c>translations.ja.name</c> 등).
    /// 값은 앞뒤 공백을 뺀다.
    /// </summary>
    public static bool TryRead(JsonNode? node, out IReadOnlyDictionary<string, AssetTranslation> translations, out string? badField)
    {
        translations = Empty;
        badField = null;
        if (node is null) return true;
        if (node is not JsonObject map || map.Count > MaxLanguages)
        {
            badField = "translations";
            return false;
        }
        var result = new Dictionary<string, AssetTranslation>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, value) in map)
        {
            string field = "translations." + code;
            if (!ScriptTranslations.IsLanguageCode(code) || result.ContainsKey(code) || value is not JsonObject entry)
            {
                badField = field;
                return false;
            }
            string? name = AssetJson.String(entry["name"]), summary = AssetJson.String(entry["summary"]);
            if (entry["name"] is not null && (name is null || !AssetManifest.IsValidName(name)))
            {
                badField = field + ".name";
                return false;
            }
            if (entry["summary"] is not null && (summary is null || !RemoteCatalogFormat.IsValidSummary(summary)))
            {
                badField = field + ".summary";
                return false;
            }
            if (name is null && summary is null)
            {
                badField = field;
                return false;
            }
            result[code] = new AssetTranslation(name?.Trim(), summary?.Trim());
        }
        translations = result;
        return true;
    }

    /// <summary>JSON 객체(언어 차례 그대로). 빈 묶음이면 null(필드를 쓰지 않음).</summary>
    public static JsonObject? Write(IReadOnlyDictionary<string, AssetTranslation> translations)
    {
        if (translations.Count == 0) return null;
        var json = new JsonObject();
        foreach (var (code, translation) in translations)
        {
            var entry = new JsonObject();
            if (translation.Name is { } name) entry["name"] = name;
            if (translation.Summary is { } summary) entry["summary"] = summary;
            json[code] = entry;
        }
        return json;
    }

    /// <summary>
    /// <paramref name="culture"/>(보통 화면 언어)에 맞는 값: 그 언어 코드 그대로, 그 위 언어들(<c>zh-Hans-CN</c> → <c>zh-Hans</c> → <c>zh</c>) 차례로
    /// <paramref name="pick"/>이 null이 아닌 첫 값. 없으면 null(기본 값을 씀).
    /// </summary>
    public static string? Find(IReadOnlyDictionary<string, AssetTranslation> translations, CultureInfo culture, Func<AssetTranslation, string?> pick)
    {
        if (translations.Count == 0) return null;
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
            if (translations.TryGetValue(c.Name, out var translation) && pick(translation) is { } value) return value;
        return null;
    }
}
