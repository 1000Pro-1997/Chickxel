using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Gpixel.Core.IO;
using Gpixel.Core.Localization;

namespace Gpixel.Core.Tests;

/// <summary>
/// 테스트는 기본(한국어) 문구를 기준으로 쓰였으므로 OS 언어와 관계없이 한국어로 돈다
/// (번역이 생긴 뒤 영어 OS의 CI에서도 같은 결과).
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseKorean()
    {
        var ko = CultureInfo.GetCultureInfo("ko");
        CultureInfo.DefaultThreadCurrentUICulture = ko;
        CultureInfo.CurrentUICulture = ko;
    }
}

/// <summary>
/// 화면 문구 리소스의 자리 표시자 규칙. 자동 추출된 보간 문장은 <c>{0}</c> 같은 번호를
/// string.Format에 넘기고, 수동 작성 문장은 <c>{name}</c>을 생성된 Format 메서드에 넘긴다.
/// </summary>
public class LocalizationResourceTests
{
    [Fact]
    public void Neutral_korean_resource_is_used_when_english_is_untranslated()
    {
        var message = CoreStrings.ResourceManager.GetString("Png_InvalidFile", CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal("PNG 파일이 아닙니다.", message);
        using var invalidPng = new MemoryStream(new byte[8]);
        Assert.Equal(message, Assert.Throws<InvalidDataException>(() => PngIO.Load(invalidPng)).Message);
    }

    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "localization");

    /// <summary>중괄호 한 쌍: 겹친 중괄호({{ }})가 아닌 여는 괄호부터 닫는 괄호까지.</summary>
    private static readonly Regex Brace = new(@"(?<!\{)\{(?!\{)([^{}]*)\}", RegexOptions.Compiled);

    private static readonly Regex Name = new("^(?:[a-z][A-Za-z0-9]*|[0-9]+(?:,-?[0-9]+)?(?::[^{}]+)?)$", RegexOptions.Compiled);

    private sealed record ResxFile(string Path, string BaseName, string? Culture, IReadOnlyDictionary<string, string> Values);

    private static List<ResxFile> Files()
    {
        var files = new List<ResxFile>();
        foreach (var path in Directory.EnumerateFiles(Root, "*.resx", SearchOption.AllDirectories))
        {
            var parts = System.IO.Path.GetFileNameWithoutExtension(path).Split('.');
            var values = XDocument.Load(path).Root!.Elements("data")
                .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetRelativePath(Root, path))!;
            files.Add(new ResxFile(path, System.IO.Path.Combine(dir, parts[0]), parts.Length > 1 ? parts[^1] : null, values));
        }
        return files;
    }

    /// <summary>값의 자리 표시자 이름들. 규칙에 맞지 않는 중괄호는 <paramref name="errors"/>에 담는다.</summary>
    private static SortedSet<string> Placeholders(string value, List<string> errors)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Brace.Matches(value))
        {
            var inner = m.Groups[1].Value;
            if (Name.IsMatch(inner)) names.Add(inner);
            else errors.Add($"{{{inner}}}");
        }
        return names;
    }


    [Fact]
    public void Placeholders_are_well_formed_and_braces_are_balanced()
    {
        var problems = new List<string>();
        foreach (var file in Files())
        foreach (var (key, value) in file.Values)
        {
            var errors = new List<string>();
            var names = Placeholders(value, errors);
            // 표시 예시의 {태그}·JSON 중괄호 등은 값 그대로 보여 주며 string.Format을 거치지 않는다.
            if (names.Count == 0) continue;
            var stripped = Brace.Replace(value, "").Replace("{{", "").Replace("}}", "");
            if (stripped.Contains('{') || stripped.Contains('}')) errors.Add("짝이 맞지 않는 중괄호");
            if (errors.Count > 0) problems.Add($"{file.BaseName}{(file.Culture is null ? "" : "." + file.Culture)}:{key}: {string.Join(", ", errors)}");
        }
        Assert.Empty(problems);
    }

}
