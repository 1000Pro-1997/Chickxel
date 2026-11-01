using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Scripting;

/// <summary>앱에 들어 있는 스크립트 효과 틀(<c>Scripting/Templates/*.js</c>). 기본 효과를 옮긴 공식 원문은 <see cref="OfficialEffects"/>.</summary>
public static class ScriptTemplates
{
    private static readonly string StarterSource = Read("starter.js");
    private static readonly JsonSerializerOptions LiteralOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>효과 만들기 "새 스크립트"의 처음 원문(지금 화면 언어 — <see cref="StarterFor"/>).</summary>
    public static string Starter => StarterFor(DomainStrings.Culture ?? CultureInfo.CurrentUICulture);

    /// <summary>
    /// "새 스크립트"의 처음 원문: 설정 하나와 픽셀마다 도는 반복문만 든 짧은 뼈대. 이름·설정 이름과 선언의 <c>language</c>는 <paramref name="culture"/>의
    /// <see cref="DomainStrings"/> 문구(앱이 그 언어 문구를 갖지 않으면 문구가 나온 언어 — <c>language</c>도 그 문구의 언어)로 쓴다. 틀 파일(<c>starter.js</c>)은 한국어다.
    /// </summary>
    public static string StarterFor(CultureInfo culture)
    {
        string Text(string key) => Translations.Get(nameof(DomainStrings), key, DomainStrings.ResourceManager, culture) ?? throw new InvalidOperationException(key);
        return StarterSource
            .Replace("name: \"새 효과\"", "name: " + Literal(Text(nameof(DomainStrings.Script_StarterName))), StringComparison.Ordinal)
            .Replace("language: \"ko\"", "language: " + Literal(Text(nameof(DomainStrings.Script_StarterLanguage))), StringComparison.Ordinal)
            .Replace("label: \"세기\"", "label: " + Literal(Text(nameof(DomainStrings.Script_StarterStrength))), StringComparison.Ordinal);
    }

    private static string Literal(string text) => JsonSerializer.Serialize(text, LiteralOptions);

    private static string Read(string name)
    {
        using var stream = typeof(ScriptTemplates).Assembly.GetManifestResourceStream("Chickxel.Scripting." + name)
                           ?? throw new InvalidOperationException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
