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

    /// <summary><paramref name="culture"/>에 맞는 번역 값(<paramref name="pick"/>이 null이 아닌 첫 번역). 없으면 null(선언 값을 씀).</summary>
    public static string? Find(IReadOnlyDictionary<string, ScriptTranslation> translations, CultureInfo culture, Func<ScriptTranslation, string?> pick)
    {
        if (translations.Count == 0) return null;
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
            if (translations.TryGetValue(c.Name, out var translation) && pick(translation) is { } value) return value;
        return null;
    }

    public static string NameFor(IReadOnlyDictionary<string, ScriptTranslation> translations, string declared, CultureInfo culture)
        => Find(translations, culture, t => t.Name) ?? declared;

    public static string? DescriptionFor(IReadOnlyDictionary<string, ScriptTranslation> translations, string? declared, CultureInfo culture)
        => Find(translations, culture, t => t.Description) ?? declared;

    public static string LabelFor(IReadOnlyDictionary<string, ScriptTranslation> translations, ScriptSetting setting, CultureInfo culture)
        => Find(translations, culture, t => t.Labels.GetValueOrDefault(setting.Key)) ?? setting.Label;

    public static string ChoiceLabelFor(IReadOnlyDictionary<string, ScriptTranslation> translations, ScriptSetting setting, ScriptChoice choice, CultureInfo culture)
        => Find(translations, culture, t => t.Labels.GetValueOrDefault(ScriptTranslation.ChoiceKey(setting.Key, choice.Value))) ?? choice.Label;
}
