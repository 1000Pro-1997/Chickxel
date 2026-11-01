using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Assets;

/// <summary>
/// 공식 효과(<see cref="OfficialEffects"/>)마다 패키지 하나: ID <c>chickxel.effects.&lt;효과 ID&gt;</c>(예: <c>chickxel.effects.outline</c>), 에셋 ID
/// <c>chickxel.effects.outline.outline</c>, 작성자 <c>chickxel</c>, MIT, 이름·설명은 원문 선언의 영어 번역(없으면 선언 그대로 — 앱 화면에는 에셋의 번역 이름이 보임),
/// 미리보기는 <see cref="AssetPreview.Package"/>(선언의 병아리에 입힌 APNG + 효과 없는 원본). 같은 원문·버전이면 같은 바이트다.
/// <para>
/// 앱은 이 패키지를 저장소에 기본 설치한다(<see cref="Sync"/> — 출처 <see cref="BundledSource"/>): 처음 실행에 모두 설치하고, 앱에 든 버전이 더 새로우면
/// 켜짐 상태를 그대로 둔 채 업데이트한다. 사용자가 지운 패키지는 다시 설치하지 않고(한 번 설치한 패키지 ID를 <see cref="StateFileName"/>에 기억),
/// "기본 효과 되돌리기"(<c>restore</c>)만 지운 것을 다시 설치하고 끈 것을 켠다. 정적 카탈로그 원본(<c>catalog/</c>)의 같은 패키지는 <c>tools/CatalogBuilder</c>가 이 코드로 만든다.
/// </para>
/// </summary>
public static class OfficialEffectPackages
{
    public const string PackagePrefix = "chickxel.effects.";

    /// <summary>앱에 든 공식 패키지를 설치한 출처 값(installed.json).</summary>
    public const string BundledSource = "bundled";

    /// <summary>한 번 설치한 공식 패키지 ID를 기억하는 파일(저장소 뿌리).</summary>
    public const string StateFileName = "bundled-effects.json";

    /// <summary>공식 효과 패키지가 요구하는 앱 버전(공식 효과의 스크립트 API가 모두 든 첫 배포).</summary>
    public const string MinAppVersion = "1.14.63";

    public const string FirstVersion = "1.0.0", License = "MIT";

    public static AssetAuthor Author { get; } = new("chickxel", "Chickxel", "https://1000pro-1997.github.io/Chickxel/");

    /// <summary>
    /// 처음 버전(<see cref="FirstVersion"/>)이 아닌 공식 효과의 패키지 버전. 원문(<c>Scripting/Official/&lt;ID&gt;.js</c>)을 고치면 그 효과의 버전을 올린다 —
    /// <c>tools/CatalogBuilder --check</c>가 카탈로그의 같은 버전 패키지 원문과 다르면 실패한다.
    /// </summary>
    private static readonly Dictionary<string, string> Versions = new(StringComparer.Ordinal)
    {
        // 1.0.1: 선언에 language: "ko"를 더함(원문의 이름·설명·설정 이름이 한국어임을 밝힘)
        ["afterimage"] = "1.0.1", ["appear"] = "1.0.1", ["blink"] = "1.0.1", ["bob"] = "1.0.1", ["coloradjust"] = "1.0.1", ["colorcycle"] = "1.0.1",
        ["coloroverlay"] = "1.0.1", ["explode"] = "1.0.1", ["fade"] = "1.0.1", ["hitflash"] = "1.0.1", ["hologram"] = "1.0.1", ["innerglow"] = "1.0.1",
        ["melt"] = "1.0.1", ["outline"] = "1.0.1", ["paletteswap"] = "1.0.1", ["reflection"] = "1.0.1", ["rimlight"] = "1.0.1", ["rotate"] = "1.0.1",
        ["shadow"] = "1.0.1", ["shake"] = "1.0.1", ["shine"] = "1.0.1", ["shockwave"] = "1.0.1", ["squash"] = "1.0.1",
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
        Author = Author, License = License, MinAppVersion = MinAppVersion,
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

    /// <summary>기본 설치 결과: 새로 설치·업데이트·다시 켠 패키지 ID와 실패(패키지 ID, 까닭).</summary>
    public sealed record SyncResult(IReadOnlyList<string> Installed, IReadOnlyList<string> Updated, IReadOnlyList<string> Enabled,
        IReadOnlyList<(string PackageId, string Error)> Errors)
    {
        public bool Changed => Installed.Count + Updated.Count + Enabled.Count > 0;
    }

    /// <summary>
    /// 공식 효과 패키지를 저장소에 맞춘다. 패키지를 만들고 설치하므로 처음 실행에는 수 초가 걸린다 — 앱은 UI 스레드가 아닌 곳에서 같은 폴더의 다른 저장소 객체로 부르고
    /// (<see cref="NeedsSync"/>로 할 일이 있을 때만), 끝나면 앱의 저장소를 <see cref="AssetStore.Refresh"/>한다.
    /// 없는 패키지는 처음이면 설치하고, 예전에 설치했던 것(사용자가 지움)은 <paramref name="restore"/>일 때만 설치한다.
    /// 설치된 버전이 앱에 든 버전보다 낮으면 업데이트한다(켜짐 그대로 — <paramref name="restore"/>면 켬). 더 높은 버전(온라인에서 받음)은 그대로 둔다.
    /// <paramref name="restore"/>면 끈 공식 패키지도 켠다. installed.json을 쓸 수 없으면 아무것도 하지 않는다.
    /// </summary>
    public static SyncResult Sync(AssetStore store, bool restore = false)
    {
        List<string> installed = [], updated = [], enabled = [];
        List<(string, string)> errors = [];
        if (store.PackagesError is not null) return new SyncResult(installed, updated, enabled, errors);
        var offered = ReadState(store);
        int offeredBefore = offered.Count;
        foreach (var id in OfficialEffects.Ordered)
        {
            string packageId = PackageId(id);
            try
            {
                switch (StepOf(store, offered, id, restore))
                {
                    case Step.Install:
                        Install(store, id, keepEnabled: false);
                        installed.Add(packageId);
                        break;
                    case Step.Update:
                        Install(store, id, keepEnabled: !restore);
                        updated.Add(packageId);
                        break;
                    case Step.Enable:
                        store.SetEnabled(packageId, true);
                        enabled.Add(packageId);
                        break;
                    case Step.None when store.Catalog.FindPackage(packageId) is null:
                        continue; // 사용자가 지운 패키지
                }
                offered.Add(packageId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AssetPackageException or InvalidOperationException)
            {
                errors.Add((packageId, ex.Message));
            }
        }
        if (offered.Count != offeredBefore)
        {
            try
            {
                WriteState(store, offered);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add((StateFileName, ex.Message));
            }
        }
        return new SyncResult(installed, updated, enabled, errors);
    }

    /// <summary>
    /// <see cref="Sync"/>가 할 일이 있는지(설치·업데이트·켜기, 처음 보는 공식 패키지 기억하기). 패키지를 만들지 않고 상태만 보므로 UI 스레드에서 불러도 된다.
    /// </summary>
    public static bool NeedsSync(AssetStore store, bool restore = false)
    {
        if (store.PackagesError is not null) return false;
        var offered = ReadState(store);
        return OfficialEffects.Ordered.Any(id => StepOf(store, offered, id, restore) != Step.None);
    }

    private enum Step { None, Install, Update, Enable, Remember }

    // 공식 효과 하나에 할 일(Remember = 이미 있는 패키지를 처음 봄 — 설치 기록에만 더함)
    private static Step StepOf(AssetStore store, HashSet<string> offered, string id, bool restore)
    {
        string packageId = PackageId(id);
        var current = store.Catalog.FindPackage(packageId);
        if (current is null) return offered.Contains(packageId) && !restore ? Step.None : Step.Install;
        if (SemVersion.TryParse(current.Version, out var have) && SemVersion.TryParse(VersionOf(id), out var bundled) && have.CompareTo(bundled) < 0)
            return Step.Update;
        if (restore && !current.Enabled) return Step.Enable;
        return offered.Contains(packageId) ? Step.None : Step.Remember;
    }

    /// <summary>설치된 패키지가 앱에 든 공식 효과 패키지인지(ID로).</summary>
    public static bool IsOfficial(InstalledPackage package) => IdOfPackage(package.Id) is not null;

    private static void Install(AssetStore store, string id, bool keepEnabled)
    {
        string file = Path.Combine(store.CacheDirectory, "bundled-" + id + AssetPackage.Extension);
        Directory.CreateDirectory(store.CacheDirectory);
        File.WriteAllBytes(file, Build(id));
        try
        {
            store.Install(file, BundledSource, keepEnabled: keepEnabled);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 다음 설치가 덮어씀
            }
        }
    }

    private static HashSet<string> ReadState(AssetStore store)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        string path = Path.Combine(store.Root, StateFileName);
        try
        {
            if (!File.Exists(path)) return set;
            if (JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) is JsonObject o && o["offered"] is JsonArray list)
                foreach (var item in list)
                    if (AssetJson.String(item) is { } packageId && AssetManifest.IsValidId(packageId)) set.Add(packageId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // 읽지 못하면 처음처럼 — 없는 패키지를 설치할 뿐(지운 공식 효과가 한 번 돌아올 수 있음)
        }
        return set;
    }

    private static void WriteState(AssetStore store, HashSet<string> offered)
    {
        var json = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["offered"] = new JsonArray(offered.Order(StringComparer.Ordinal).Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
        };
        AtomicFile.Write(Path.Combine(store.Root, StateFileName), AssetJson.ToUtf8(json));
    }

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
