using System.Globalization;
using System.Text.RegularExpressions;

namespace Chickxel.Core.Scripting;

/// <summary>
/// 선언 <c>translations</c>의 언어 하나: 그 언어의 제목·설명과 설정 이름(<see cref="Labels"/> — 키는 설정 키, 고르기 항목은 <c>"설정키.항목값"</c>).
/// 빠진 것은 다음 언어(중립 언어)나 선언에 적은 값으로 보인다(<see cref="ScriptTranslations"/>).
/// </summary>
public sealed record ScriptTranslation(string? Name, string? Description, IReadOnlyDictionary<string, string> Labels)
{
    /// <summary>고르기 항목 이름의 <see cref="Labels"/> 키.</summary>
    public static string ChoiceKey(string settingKey, string choiceValue) => settingKey + "." + choiceValue;

    /// <summary>값이 같은지(이름·설명·설정 이름 모두 — 설정 이름은 차례와 상관없음).</summary>
    public bool SameAs(ScriptTranslation other)
        => Name == other.Name && Description == other.Description && Labels.Count == other.Labels.Count
           && Labels.All(pair => other.Labels.TryGetValue(pair.Key, out var label) && label == pair.Value);
}

/// <summary>
/// 스크립트 효과의 여러 언어 이름: 화면 언어(<see cref="CultureInfo"/>)에 맞는 번역을 고른다. 찾는 차례는 그 언어 코드 그대로(<c>zh-Hans</c>),
/// 그 위 언어들(<c>zh-Hans-CN</c> → <c>zh-Hans</c> → <c>zh</c>, <c>en-US</c> → <c>en</c>), 그다음 선언에 적은 값이다. 항목(이름·설명·설정 이름)마다 따로 찾는다.
/// 선언의 <c>language</c>(선언에 적은 값의 언어)를 주면, 찾는 차례에서 그 언어에 먼저 닿을 때 번역 대신 선언 값을 쓴다
/// (같은 단계에서는 선언 값이 먼저 — 그 언어의 원본은 선언이다).
/// </summary>
public static partial class ScriptTranslations
{
    /// <summary>번역 이름 한 줄(제목·설정 이름)의 최대 길이.</summary>
    public const int MaxLabelLength = 100;

    /// <summary>번역할 수 있는 언어 수.</summary>
    public const int MaxLanguages = 64;

    public static IReadOnlyDictionary<string, ScriptTranslation> Empty { get; } = new Dictionary<string, ScriptTranslation>(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8})*$")]
    private static partial Regex LanguageCode();

    /// <summary>BCP-47 모양의 언어 코드인지(<c>en</c>, <c>ja</c>, <c>zh-Hans</c>, <c>pt-BR</c> 등, 35자까지).</summary>
    public static bool IsLanguageCode(string code) => code.Length <= 35 && LanguageCode().IsMatch(code);

    /// <summary>
    /// <paramref name="culture"/>에 맞는 번역 값(<paramref name="pick"/>이 null이 아닌 첫 번역). 없거나, 그보다 먼저 <paramref name="baseLanguage"/>
    /// (선언 값의 언어)에 닿으면 null(선언 값을 씀).
    /// </summary>
    public static string? Find(IReadOnlyDictionary<string, ScriptTranslation> translations, CultureInfo culture, Func<ScriptTranslation, string?> pick,
        string? baseLanguage = null)
    {
        if (translations.Count == 0) return null;
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            if (baseLanguage is not null && string.Equals(c.Name, baseLanguage, StringComparison.OrdinalIgnoreCase)) return null;
            if (translations.TryGetValue(c.Name, out var translation) && pick(translation) is { } value) return value;
        }
        return null;
    }

    /// <summary>화면 언어 <paramref name="culture"/>가 선언 값의 언어 <paramref name="baseLanguage"/>(또는 그 아래 언어)인지: 찾는 차례에 그 언어가 있는지.</summary>
    public static bool Matches(string? baseLanguage, CultureInfo culture)
    {
        if (baseLanguage is null) return false;
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
            if (string.Equals(c.Name, baseLanguage, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// 번역 키로 쓸 <paramref name="culture"/>의 언어 코드: 맨 위 중립 언어(<c>en-US</c> → <c>en</c>, <c>ko-KR</c> → <c>ko</c>). 중국어는 글자 체계까지
    /// (<c>zh-CN</c> → <c>zh-Hans</c>, <c>zh-TW</c> → <c>zh-Hant</c> — <c>zh</c> 하나로는 간체·번체를 가를 수 없음). 고정 문화권이면 null.
    /// </summary>
    public static string? LanguageOf(CultureInfo culture)
    {
        string? top = null, below = null;
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            below = top;
            top = c.Name;
        }
        return top is "zh" && below is not null ? below : top;
    }

    /// <summary>
    /// 화면 언어 <paramref name="culture"/>에서 정보 칸(이름·설명)이 고칠 곳: 선언 값이면 null(<paramref name="baseLanguage"/>가 없거나 화면 언어가 그 언어일 때),
    /// 아니면 고칠 번역의 언어 코드(<see cref="LanguageOf"/>). 고정 문화권이면 null.
    /// </summary>
    public static string? EditLanguage(string? baseLanguage, CultureInfo culture)
        => baseLanguage is null || Matches(baseLanguage, culture) ? null : LanguageOf(culture);

    public static string NameFor(IReadOnlyDictionary<string, ScriptTranslation> translations, string declared, CultureInfo culture, string? baseLanguage = null)
        => Find(translations, culture, t => t.Name, baseLanguage) ?? declared;

    public static string? DescriptionFor(IReadOnlyDictionary<string, ScriptTranslation> translations, string? declared, CultureInfo culture, string? baseLanguage = null)
        => Find(translations, culture, t => t.Description, baseLanguage) ?? declared;

    public static string LabelFor(IReadOnlyDictionary<string, ScriptTranslation> translations, ScriptSetting setting, CultureInfo culture, string? baseLanguage = null)
        => Find(translations, culture, t => t.Labels.GetValueOrDefault(setting.Key), baseLanguage) ?? setting.Label;

    public static string ChoiceLabelFor(IReadOnlyDictionary<string, ScriptTranslation> translations, ScriptSetting setting, ScriptChoice choice, CultureInfo culture,
        string? baseLanguage = null)
        => Find(translations, culture, t => t.Labels.GetValueOrDefault(ScriptTranslation.ChoiceKey(setting.Key, choice.Value)), baseLanguage) ?? choice.Label;
}
