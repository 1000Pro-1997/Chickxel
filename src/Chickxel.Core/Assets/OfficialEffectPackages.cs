using System.Globalization;
using Chickxel.Core.IO;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Assets;

/// <summary>
/// 공식 효과(<see cref="OfficialEffects"/>)마다 패키지 하나: ID <c>chickxel.effects.&lt;효과 ID&gt;</c>(예: <c>chickxel.effects.outline</c>), 에셋 ID
/// <c>chickxel.effects.outline.outline</c>, 작성자 <c>chickxel</c>, MIT, 이름·설명은 원문 선언의 영어 번역(없으면 선언 그대로 — 앱 화면에는 에셋의 번역 이름이 보임),
/// 미리보기는 <see cref="AssetPreview.Package"/>(선언의 병아리에 입힌 APNG + 효과 없는 원본). 같은 원문·버전이면 같은 바이트다.
/// <para>
/// 앱은 이 효과를 저장소에 기본으로 넣는다(<see cref="Bundled"/> — <see cref="BundledPackages.Sync"/>). 정적 카탈로그 원본(<c>catalog/</c>)의 같은 패키지는
/// <c>tools/CatalogBuilder</c>가 이 코드로 만든다.
/// </para>
/// </summary>
public static class OfficialEffectPackages
{
    public const string PackagePrefix = "chickxel.effects.";

    /// <summary>공식 효과 패키지가 요구하는 앱 버전(공식 효과의 스크립트 API가 모두 든 첫 배포). 더 새 API를 부르는 효과는 <see cref="MinAppVersionOf"/>.</summary>
    public const string MinAppVersion = "1.14.63";

    /// <summary>
    /// <see cref="MinAppVersion"/>보다 새 앱이 있어야 하는 공식 효과(그 뒤에 생긴 스크립트 API를 부름 — 예전 앱은 그 효과를 그리지 못함)와 그 앱 버전.
    /// </summary>
    private static readonly Dictionary<string, string> MinAppVersions = new(StringComparer.Ordinal)
    {
        // ctx.movePivot(뒤 효과의 피벗 같이 옮기기)을 부름
        ["bob"] = "1.14.92", ["shake"] = "1.14.92",
    };

    /// <summary>공식 효과 <paramref name="id"/>의 패키지가 요구하는 앱 버전.</summary>
    public static string MinAppVersionOf(string id) => MinAppVersions.GetValueOrDefault(id, MinAppVersion);

    public const string FirstVersion = "1.0.0", License = "MIT";

    public static AssetAuthor Author { get; } = new("chickxel", "Chickxel", "https://1000pro-1997.github.io/Chickxel/");

    /// <summary>
    /// 처음 버전(<see cref="FirstVersion"/>)이 아닌 공식 효과의 패키지 버전. 원문(<c>Scripting/Official/&lt;ID&gt;.js</c>)을 고치면 그 효과의 버전을 올린다 —
    /// <c>tools/CatalogBuilder --check</c>가 카탈로그의 같은 버전 패키지 원문과 다르면 실패한다.
    /// </summary>
    private static readonly Dictionary<string, string> Versions = new(StringComparer.Ordinal)
    {
        // 1.0.1: 선언에 language: "ko"를 더함(원문의 이름·설명·설정 이름이 한국어임을 밝힘)
        // afterimage 1.0.2: 선언에 previewMotion: "right"를 더함(미리보기 병아리가 움직여야 잔상이 보임)
        // bob·shake 1.0.2: 옮긴 거리를 ctx.movePivot으로 알림(뒤 효과의 피벗이 따라감, 그림은 그대로)
        ["afterimage"] = "1.0.2", ["appear"] = "1.0.1", ["blink"] = "1.0.1", ["bob"] = "1.0.2", ["coloradjust"] = "1.0.1", ["colorcycle"] = "1.0.1",
        ["coloroverlay"] = "1.0.1", ["explode"] = "1.0.1", ["fade"] = "1.0.1", ["hitflash"] = "1.0.1", ["hologram"] = "1.0.1", ["innerglow"] = "1.0.1",
        ["melt"] = "1.0.1", ["outline"] = "1.0.1", ["paletteswap"] = "1.0.1", ["reflection"] = "1.0.1", ["rimlight"] = "1.0.1", ["rotate"] = "1.0.1",
        ["shadow"] = "1.0.1", ["shake"] = "1.0.2", ["shine"] = "1.0.1", ["shockwave"] = "1.0.1", ["squash"] = "1.0.1",
    };

    /// <summary>공식 효과 <paramref name="id"/>의 패키지 버전.</summary>
    public static string VersionOf(string id) => Versions.GetValueOrDefault(id, FirstVersion);

    public static string PackageId(string id) => PackagePrefix + id;

    public static string AssetId(string id) => PackageId(id) + "." + id;

    /// <summary>공식 효과 패키지 ID(<c>chickxel.effects.outline</c>)나 그 에셋 ID(<c>chickxel.effects.outline.outline</c>)의 효과 ID(아니면 null).</summary>
    public static string? IdOfPackage(string? id)
    {
        if (id is null || !id.StartsWith(PackagePrefix, StringComparison.Ordinal)) return null;
        var rest = id[PackagePrefix.Length..];
        int dot = rest.IndexOf('.');
        if (dot >= 0)
        {
            if (rest[(dot + 1)..] != rest[..dot]) return null;
            rest = rest[..dot];
        }
        return OfficialEffects.Ids.Contains(rest, StringComparer.Ordinal) ? rest : null;
    }

    /// <summary>카탈로그 꼬리표: <c>effect</c>, <c>official</c>, <c>script</c>, 움직이면 <c>animated</c>.</summary>
    public static IReadOnlyList<string> Tags(ScriptEffectProgram program)
        => program.Animated ? ["effect", "official", "script", "animated"] : ["effect", "official", "script"];

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    /// <summary>패키지 이름(선언의 영어 번역, 없으면 선언의 이름).</summary>
    public static string NameOf(ScriptEffectProgram program) => program.NameFor(English).Trim();

    /// <summary>패키지 설명(선언의 영어 번역, 없으면 선언의 설명).</summary>
    public static string DescriptionOf(ScriptEffectProgram program) => (program.DescriptionFor(English) ?? NameOf(program)).Trim();

    /// <summary>카탈로그 목록의 짧은 설명(200자 이하 — 넘으면 마지막 낱말 앞에서 자르고 줄임표).</summary>
    public static string SummaryOf(ScriptEffectProgram program)
    {
        var text = DescriptionOf(program);
        if (text.Length <= 200) return text;
        int cut = text.LastIndexOf(' ', 198);
        return text[..(cut > 0 ? cut : 198)].TrimEnd(',', ';', ' ') + "…";
    }

    /// <summary>패키지 정보(에셋·파일 목록 없음).</summary>
    public static AssetManifest Header(string id, ScriptEffectProgram program) => new()
    {
        Id = PackageId(id), Version = VersionOf(id), Name = NameOf(program), Description = DescriptionOf(program),
        Author = Author, License = License, MinAppVersion = MinAppVersionOf(id),
    };

    /// <summary>공식 효과 <paramref name="id"/>의 패키지 바이트(<c>.chickxel-asset</c>). 미리보기를 그리므로 효과마다 수십~수백 ms.</summary>
    public static byte[] Build(string id)
    {
        var program = OfficialEffects.Program(id);
        var asset = ScriptAsset.From(AssetId(id), program);
        var builder = new AssetPackageBuilder(Header(id, program));
        builder.Add(AssetKinds.EffectScript, asset, AssetPreview.Package(asset), segment: id);
        builder.AddDocument("LICENSE.txt", LicenseText);
        builder.AddDocument("README.md", $"# {NameOf(program)}\n\n{DescriptionOf(program)} Official Chickxel effect.\n");
        using var output = new MemoryStream();
        builder.Write(output);
        return output.ToArray();
    }

    /// <summary>앱에 든 공식 효과 패키지(효과 탭 차례). 패키지의 스크립트 효과가 그 공식 효과다(에셋 ID는 보지 않음 — <see cref="AssetCatalog.OfficialIdOf"/>).</summary>
    public static IEnumerable<BundledPackage> Bundled
        => OfficialEffects.Ordered.Select(id => new BundledPackage(AssetKinds.EffectScript, PackageId(id), VersionOf(id), () => Build(id),
            entry => AssetCatalog.OfficialIdOf(entry) == id));

    /// <summary>에셋이 앱에 든 공식 효과인지(상점 연결로).</summary>
    public static bool IsOfficial(AssetEntry entry) => AssetCatalog.OfficialIdOf(entry) is not null;

    /// <summary>공식 패키지의 LICENSE.txt(다른 공식 패키지와 같은 MIT 전문).</summary>
    public static string LicenseText { get; } = """
        MIT License

        Copyright (c) 2026 천성훈 (1000Pro-1997)

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.

        """.ReplaceLineEndings("\n");
}
