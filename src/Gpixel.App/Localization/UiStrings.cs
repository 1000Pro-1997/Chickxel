using Gpixel.Core.Localization;

namespace Gpixel.App.Localization;

// 생성된 문구 클래스의 찾기 부분(OmitGetResourceString): CSV로 더한 언어도 찾도록 Translations를 거친다.
public static partial class UiStrings
{
    public static global::System.Globalization.CultureInfo? Culture { get; set; }

    internal static string? GetResourceString(string resourceKey, string? defaultValue = null)
        => Translations.Get(nameof(UiStrings), resourceKey, ResourceManager, Culture) ?? defaultValue;

    private static string GetResourceString(string resourceKey, string[]? formatterNames)
        => Translations.ReplaceFormatterNames(GetResourceString(resourceKey) ?? "", formatterNames);
}
