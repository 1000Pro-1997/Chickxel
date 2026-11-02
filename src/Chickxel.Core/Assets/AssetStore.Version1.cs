using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;

namespace Chickxel.Core.Assets;

/// <summary>
/// 예전 저장소(설치 패키지 폴더 <c>Packages/&lt;id&gt;/&lt;version&gt;/</c> + <c>installed.json</c>, 개인 에셋 <c>Personal/personal-assets.json</c>,
/// 삭제 보관함 <c>Deleted/</c>)를 library.json으로 한 번 옮긴다. 개인 에셋은 이 컴퓨터에서 만든 에셋으로, 설치 패키지의 에셋은 하나씩 패키지의 작성자·라이선스·
/// 상점 연결을 붙여(<see cref="Install"/>과 같은 규칙) 넣는다. 끈 패키지도 옮긴다(켜기·끄기는 없어짐). 읽지 못한 패키지는 건너뛰고 <see cref="LoadErrors"/>에 남긴다.
/// 상태 파일을 읽지 못하면 아무것도 쓰지 않고 저장소를 막는다(<see cref="LibraryError"/>, 다음 실행에 다시 시도). 옮긴 뒤 예전 파일은 <c>v1-backup/</c>으로 옮긴다.
/// </summary>
public sealed partial class AssetStore
{
    private const string Version1InstalledFile = "installed.json", Version1PersonalFile = "personal-assets.json";

    private string Version1PersonalPath => Path.Combine(Root, "Personal", Version1PersonalFile);

    private bool HasVersion1Files => File.Exists(Path.Combine(Root, Version1InstalledFile)) || File.Exists(Version1PersonalPath);

    private void MigrateVersion1(DateTime nowUtc)
    {
        var records = new List<Record>();
        HashSet<string> favorites;
        bool personalExisted = File.Exists(Version1PersonalPath);
        var written = new List<string>();
        try
        {
            favorites = ReadVersion1Personal(records, written);
            foreach (var state in ReadVersion1Installed())
                ReadVersion1Package(state, records, written);
            _legacyPresetsDone = personalExisted;
            WriteLibraryFile(records, favorites);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            foreach (var folder in written) TryDeleteDirectory(Path.Combine(FilesDirectory, folder));
            _legacyPresetsDone = false;
            LibraryError = ex.Message;
            _loadErrors.Add(ex.Message);
            return;
        }

        // 새 파일이 완성된 뒤에만 예전 파일을 치운다(옮기지 못해도 library.json이 기준 — 예전 파일은 다시 읽지 않음)
        string backup = Path.Combine(Root, "v1-backup", nowUtc.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
        foreach (var name in new[] { Version1InstalledFile, "Packages", "Personal", "Deleted" })
        {
            string path = Path.Combine(Root, name);
            try
            {
                Directory.CreateDirectory(backup);
                if (File.Exists(path)) File.Move(path, Path.Combine(backup, name));
                else if (Directory.Exists(path)) Directory.Move(path, Path.Combine(backup, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _loadErrors.Add($"{name}: {ex.Message}");
            }
        }
    }

    /// <summary>개인 에셋을 줄로 더하고 즐겨찾기를 돌려준다(파일이 없으면 빈 것). 읽지 못한 줄은 원본 그대로 옮긴다.</summary>
    private HashSet<string> ReadVersion1Personal(List<Record> records, List<string> written)
    {
        var favorites = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(Version1PersonalPath)) return favorites;
        var issues = new List<AssetIssue>();
        var node = AssetJson.Parse(File.ReadAllBytes(Version1PersonalPath), Version1PersonalFile, 64, 1 << 20, issues) as JsonObject
                   ?? throw new InvalidDataException(issues.FirstOrDefault()?.Message ?? Version1PersonalFile);
        if (!AssetJson.TryInt(node["schemaVersion"], out int schema) || schema != 1) throw new InvalidDataException(Version1PersonalFile);
        foreach (var item in node["assets"] as JsonArray ?? [])
        {
            if (item is not JsonObject o) continue;
            string id = AssetJson.String(o["id"]) ?? "", kind = AssetJson.String(o["kind"]) ?? "";
            if (id.Length == 0 || records.Any(r => r.Id == id)) continue;
            DateTime created = DateTime.TryParse(AssetJson.String(o["createdAt"]), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : default;
            IAssetData? data = null;
            if (AssetKinds.Find(kind) is { } definition && o["data"] is JsonObject json)
            {
                data = definition.ReadPersonal(json, new AssetReadContext(id, strict: false) { AssetId = id });
                if (data is not null && data.Id != id) data = WithId(data, id);
            }
            string? folder = null;
            string scene = Path.Combine(Root, "Personal", "scenes", id + ".png");
            if (data is ScriptAsset && File.Exists(scene))
            {
                folder = WriteFiles(null, File.ReadAllBytes(scene), null);
                written.Add(folder!);
            }
            if (data is null)
            {
                var raw = (JsonObject)o.DeepClone();
                raw["addedAt"] = raw["createdAt"]?.DeepClone();
                records.Add(new Record(id, kind, created, AssetSources.Local, null, null, null, null, null, raw));
            }
            else records.Add(new Record(id, kind, created, AssetSources.Local, data, null, null, null, folder, null));
        }
        foreach (var id in (node["favorites"] as JsonArray ?? []).Select(AssetJson.String).OfType<string>()) favorites.Add(id);
        return favorites;
    }

    /// <summary>installed.json의 패키지 줄(ID, 버전, 출처, 설치 시각). 켜짐·이전 버전은 옮기지 않는다.</summary>
    private List<(string Id, string Version, string Source, DateTime InstalledUtc)> ReadVersion1Installed()
    {
        var list = new List<(string Id, string Version, string Source, DateTime InstalledUtc)>();
        string path = Path.Combine(Root, Version1InstalledFile);
        if (!File.Exists(path)) return list;
        var issues = new List<AssetIssue>();
        var node = AssetJson.Parse(File.ReadAllBytes(path), Version1InstalledFile, 16, 4096, issues) as JsonObject
                   ?? throw new InvalidDataException(issues.FirstOrDefault()?.Message ?? Version1InstalledFile);
        if (!AssetJson.TryInt(node["schemaVersion"], out int schema) || schema != 1) throw new InvalidDataException(Version1InstalledFile);
        foreach (var item in node["packages"] as JsonArray ?? [])
        {
            if (item is not JsonObject o) continue;
            string id = AssetJson.String(o["id"]) ?? "", version = AssetJson.String(o["version"]) ?? "";
            if (!AssetManifest.IsValidId(id) || !AssetManifest.IsValidVersion(version) || list.Any(p => p.Id == id)) continue;
            DateTime installed = DateTime.TryParse(AssetJson.String(o["installedAt"]), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : default;
            list.Add((id, version, AssetJson.String(o["source"]) ?? AssetSources.File, installed));
        }
        return list;
    }

    /// <summary>설치 폴더 하나를 검사해(manifest, 파일 해시, 값) 에셋마다 줄을 더한다. 읽지 못하면 건너뛰고 까닭을 남긴다.</summary>
    private void ReadVersion1Package((string Id, string Version, string Source, DateTime InstalledUtc) state, List<Record> records, List<string> written)
    {
        string folder = Path.Combine(Root, "Packages", state.Id, state.Version);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (Directory.Exists(folder))
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                files[Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllBytes(file);
        AssetPackage package;
        try
        {
            package = AssetPackageValidator.Validate(files, _options with { AppVersion = null });
        }
        catch (AssetPackageException ex)
        {
            _loadErrors.Add($"{state.Id} {state.Version}: {string.Join("; ", ex.Issues.Take(3).Select(issue => issue.Message))}");
            return;
        }
        var manifest = package.Manifest;
        if (manifest.Id != state.Id)
        {
            _loadErrors.Add($"{state.Id} {state.Version}: {new AssetIssue(AssetIssueCode.InvalidField, AssetManifest.FileName, "id").Message}");
            return;
        }
        bool personal = manifest.Author.Id == PersonalAuthorId;
        bool listed = !manifest.Id.StartsWith(LocalIdPrefix, StringComparison.Ordinal);
        string? licenseName = LicenseDocuments.FirstOrDefault(package.Files.ContainsKey);
        foreach (var (entry, data) in package.Items)
        {
            string hash = AssetContent.HashOf(entry.Kind, data);
            var author = entry.Author ?? (personal ? null : manifest.Author);
            if (author?.Id == PersonalAuthorId) author = null;
            var store = entry.Store ?? (listed ? AssetStoreLink.FromManifest(manifest, entry.Id, hash) : null);
            string id = NewId(records, entry.Id);
            byte[]? preview = entry.Preview is { } p && package.Files.TryGetValue(p, out var png) ? png : null;
            package.Files.TryGetValue(AssetPackageBuilder.ScenePath(entry.Path), out var scene);
            if (data is not ScriptAsset { Scene: not null }) scene = null;
            string? filesFolder = WriteFiles(preview, scene, licenseName is null ? null : (licenseName, package.Files[licenseName]));
            if (filesFolder is not null) written.Add(filesFolder);
            records.Add(new Record(id, entry.Kind, state.InstalledUtc, state.Source, WithId(data, id), author,
                entry.License ?? (personal ? null : manifest.License), store, filesFolder, null));
        }
    }
}
