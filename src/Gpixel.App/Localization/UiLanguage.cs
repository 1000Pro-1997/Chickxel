using System.Globalization;

namespace Gpixel.App.Localization;

/// <summary>
/// 화면 언어. 문구는 Localization/*.resx(기본 = 한국어, 다른 언어 = *.&lt;언어&gt;.resx)에 있고, 앱이 뜨기 전에 한 번 정한다
/// (실행 중에 바꾸지 않음 — 정적 필드에 담긴 문구도 있으므로 바꾸면 다시 시작).
/// </summary>
public static class UiLanguage
{
    /// <summary>
    /// <paramref name="language"/>(예: "en", 설정 값)를 화면 문구 언어로 정한다. null·빈 값·모르는 이름이면 OS 언어 그대로.
    /// 숫자·날짜 형식(CurrentCulture)은 바꾸지 않는다.
    /// </summary>
    public static void Apply(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return;
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(language.Trim());
        }
        catch (CultureNotFoundException)
        {
            return;
        }
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
