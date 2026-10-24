using System.Globalization;
using System.Resources;

namespace Gpixel.Core.Localization;

/// <summary>
/// 화면 문구 찾기. 내장 언어(한국어 = 기본 리소스, 영어 = *.en.resx)는 리소스에서 찾고,
/// CSV로 더한 언어는 <see cref="Use"/>로 넘긴 번역을 먼저 본 뒤 비었거나 없는 문구만 대신할 언어(보통 영어)의 리소스에서 찾는다.
/// 생성된 문구 클래스(<c>Localization/*.resx</c>)의 GetResourceString이 모두 이곳을 거친다.
/// </summary>
public static class Translations
{
    private static IReadOnlyDictionary<(string Table, string Key), string>? s_overrides;
    private static CultureInfo? s_fallback;

    /// <summary>
    /// CSV 번역을 쓴다(앱이 문구를 읽기 전에 한 번). <paramref name="overrides"/>의 키 = (표 이름(예: "UiStrings"), 리소스 키).
    /// null이면 리소스만 쓴다. <paramref name="fallback"/> = 번역에 없는 문구를 찾을 리소스 언어.
    /// </summary>
    public static void Use(IReadOnlyDictionary<(string Table, string Key), string>? overrides, CultureInfo? fallback)
    {
        s_overrides = overrides;
        s_fallback = fallback;
    }

    /// <summary><paramref name="table"/>의 <paramref name="key"/> 문구. 생성된 문구 클래스가 부른다.</summary>
    public static string? Get(string table, string key, ResourceManager resources, CultureInfo? culture)
    {
        if (s_overrides is not { } overrides) return resources.GetString(key, culture);
        return overrides.TryGetValue((table, key), out var value) ? value : resources.GetString(key, s_fallback ?? culture);
    }

    /// <summary>문구의 <c>{이름}</c> 자리 표시자를 <c>{번호}</c>로 바꾼다(생성된 Format… 메서드가 string.Format에 넘기기 전).</summary>
    public static string ReplaceFormatterNames(string value, string[]? formatterNames)
    {
        if (formatterNames is null) return value;
        for (int i = 0; i < formatterNames.Length; i++)
            value = value.Replace("{" + formatterNames[i] + "}", "{" + i.ToString(CultureInfo.InvariantCulture) + "}");
        return value;
    }
}