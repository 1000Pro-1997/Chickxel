using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.Scripting;

namespace CatalogBuilder;

/// <summary>
/// 공식 효과 패키지(<see cref="OfficialEffectPackages"/>)를 <c>catalog/chickxel.effects.&lt;ID&gt;/</c>에 둔다. 지금 버전의 패키지가 없으면 Core 코드로 만들어 쓰고
/// (예전 버전 파일은 지움, <c>review.json</c>의 <c>published</c>는 처음 날짜 그대로), 있으면 바꾸지 않는다 — 이미 게시한 버전의 바이트는 그대로여야 한다.
/// 검사: 있는 패키지의 스크립트 원문·이름·설명이 지금 공식 효과와 같아야 한다. 원문을 고치고 버전을 올리지 않았으면 실패한다
/// (미리보기는 압축 바이트가 기계마다 다를 수 있어 비교하지 않음).
/// </summary>
internal static class OfficialEffectCatalog
{
    private static readonly JsonSerializerOptions ReviewJson = new()
    {
        WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>문제(사람이 읽는 한 줄)와 쓸 파일(catalog/ 기준 경로 → 바이트, null = 지움).</summary>
    public static (List<string> Problems, Dictionary<string, byte[]?> Writes) Plan(string catalogDirectory)
    {
        var problems = new List<string>();
        var writes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var id in OfficialEffects.Ordered)
        {
            string packageId = OfficialEffectPackages.PackageId(id), version = OfficialEffectPackages.VersionOf(id);
            string folder = Path.Combine(catalogDirectory, packageId);
            string fileName = $"{packageId}-{version}{AssetPackage.Extension}";
            string path = Path.Combine(folder, fileName);
            var program = OfficialEffects.Program(id);
            if (File.Exists(path))
            {
                if (Compare(path, id, program) is { } problem)
                    problems.Add($"{packageId}/{fileName}: {problem} — raise its version in OfficialEffectPackages and run CatalogBuilder");
                continue;
            }
            // 새 버전: 패키지를 만들고 예전 버전 파일을 지운다
            writes[$"{packageId}/{fileName}"] = OfficialEffectPackages.Build(id);
            if (Directory.Exists(folder))
                foreach (var old in Directory.GetFiles(folder, "*" + AssetPackage.Extension))
                    writes[$"{packageId}/{Path.GetFileName(old)}"] = null;
            writes[$"{packageId}/review.json"] = Review(Path.Combine(folder, "review.json"), program);
        }
        return (problems, writes);
    }

    public static void Apply(string catalogDirectory, Dictionary<string, byte[]?> writes)
    {
        foreach (var (relative, data) in writes.OrderBy(pair => pair.Value is null ? 0 : 1))
        {
            string path = Path.Combine(catalogDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (data is null)
            {
                if (File.Exists(path)) File.Delete(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }
    }

    // 있는 패키지가 지금 공식 효과와 같은지(다르면 까닭)
    private static string? Compare(string path, string id, ScriptEffectProgram program)
    {
        using var zip = ZipFile.OpenRead(path);
        string Text(string name)
        {
            using var reader = new StreamReader(zip.GetEntry(name)?.Open() ?? throw new InvalidDataException(name), Encoding.UTF8);
            return reader.ReadToEnd();
        }
        var issues = new List<AssetIssue>();
        var manifest = AssetManifest.Read(JsonNode.Parse(Text(AssetManifest.FileName)), issues);
        if (manifest is null) return "unreadable manifest";
        var header = OfficialEffectPackages.Header(id, program);
        if (manifest.Name != header.Name || manifest.Description != header.Description || manifest.MinAppVersion != header.MinAppVersion)
            return "name, description or minAppVersion changed";
        if (manifest.Assets is not [{ Kind: AssetKinds.EffectScript } asset] || asset.Id != OfficialEffectPackages.AssetId(id))
            return "unexpected assets";
        return ScriptEffectProgram.NormalizeSource(Text(asset.Path)) == OfficialEffects.Source(id) ? null : "script source changed";
    }

    // 검토 정보: 요약·꼬리표는 지금 선언에서, 처음 게시한 날짜는 있던 값 그대로
    private static byte[] Review(string existing, ScriptEffectProgram program)
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string published = today;
        if (File.Exists(existing) && JsonNode.Parse(File.ReadAllText(existing))?["published"]?.GetValue<string>() is { Length: > 0 } first) published = first;
        var review = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["summary"] = OfficialEffectPackages.SummaryOf(program),
            ["tags"] = new JsonArray(OfficialEffectPackages.Tags(program).Select(tag => (JsonNode)JsonValue.Create(tag)!).ToArray()),
            ["review"] = "official",
            ["published"] = published,
            ["updated"] = today,
        };
        return Encoding.UTF8.GetBytes(review.ToJsonString(ReviewJson) + "\n");
    }

    /// <summary>
    /// 공식 원문 해시 목록(<see cref="OfficialEffects.ShippedFileName"/>)에 빠진 해시(해시 → 효과 ID, 어디서 나왔는지): 지금 공식 원문과, 주어진 폴더들에 있는
    /// 공식 효과 패키지(<c>chickxel.effects.&lt;ID&gt;</c>, 모든 버전)의 원문. 예전 버전 패키지를 지우기 전에 불러야 그 해시도 남는다.
    /// 목록 글의 모양이 틀렸으면 <see cref="FormatException"/>.
    /// </summary>
    public static (Dictionary<string, string> Listed, List<(string Hash, string Id, string From)> Missing) PlanShipped(string listPath, IEnumerable<string> packageDirectories)
    {
        var listed = File.Exists(listPath) ? OfficialEffects.ParseShipped(File.ReadAllText(listPath, Encoding.UTF8)) : new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<(string, string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Need(string hash, string id, string from)
        {
            if (listed.TryGetValue(hash, out var listedId) && listedId == id) return;
            if (seen.Add(hash)) missing.Add((hash, id, from));
        }
        foreach (var id in OfficialEffects.Ordered) Need(OfficialEffects.HashOf(id), id, $"Scripting/Official/{id}.js");
        foreach (var directory in packageDirectories.Where(Directory.Exists))
            foreach (var file in Directory.GetFiles(directory, OfficialEffectPackages.PackagePrefix + "*" + AssetPackage.Extension, SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
                if (OfficialScript(file) is { } script)
                    Need(ScriptEffectProgram.HashOf(script.Source), script.Id, Path.GetRelativePath(Path.GetDirectoryName(directory)!, file).Replace('\\', '/'));
        return (listed, missing);
    }

    // 공식 효과 패키지(에셋 하나 = chickxel.effects.<ID>.<ID> 효과 스크립트)면 효과 ID와 정리한 원문(레시피 묶음 등 다른 패키지는 null)
    private static (string Id, string Source)? OfficialScript(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        string? Text(string name)
        {
            if (zip.GetEntry(name) is not { } entry) return null;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }
        if (Text(AssetManifest.FileName) is not { } manifestText) return null;
        var manifest = AssetManifest.Read(JsonNode.Parse(manifestText), new List<AssetIssue>());
        if (manifest is null || !manifest.Id.StartsWith(OfficialEffectPackages.PackagePrefix, StringComparison.Ordinal)) return null;
        string id = manifest.Id[OfficialEffectPackages.PackagePrefix.Length..];
        if (manifest.Assets is not [{ Kind: AssetKinds.EffectScript } asset] || asset.Id != manifest.Id + "." + id) return null;
        return Text(asset.Path) is { } source ? (id, ScriptEffectProgram.NormalizeSource(source)) : null;
    }
}
