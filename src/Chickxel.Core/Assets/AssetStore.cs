using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;

namespace Chickxel.Core.Assets;

/// <summary>가져오는 에셋이 이미 있는 에셋과 겹칠 때 할 일.</summary>
public enum ImportChoice
{
    /// <summary>있는 에셋을 새것으로 바꾼다(ID·즐겨찾기는 그대로).</summary>
    Replace,
    /// <summary>가져오지 않는다.</summary>
    Skip,
    /// <summary>새 에셋으로 하나 더 둔다.</summary>
    KeepBoth,
}

/// <summary>
/// 가져오는 패키지 파일 안의 에셋 하나(<see cref="AssetStore.Install"/>가 겹칠 때 묻는 값): 읽은 값, 이 에셋의 작성자·라이선스·상점 연결,
/// 겹치는 기존 에셋(같은 상점 에셋, 아니면 같은 종류·같은 내용 — 없으면 null), 패키지에 에셋이 이것 하나뿐인지.
/// </summary>
public sealed record AssetImportItem(string Kind, IAssetData Data, AssetAuthor? Author, string? License, AssetStoreLink? Store, AssetEntry? Existing, bool SingleAsset = false)
{
    public string DisplayName => Data is ScriptAsset script ? script.NameFor(CultureInfo.CurrentUICulture) : Data.Name;

    /// <summary>
    /// 기존 에셋과 같은 상점 에셋인지(업데이트나 다시 설치): 같은 패키지 ID·에셋 ID, 에셋이 하나뿐인 패키지(게시 패키지·공식 효과)면 같은 패키지 ID·종류
    /// (예전 게시 창은 이름이 바뀌면 에셋 ID도 바꿨음).
    /// </summary>
    public bool SameStoreAsset => Existing?.Store is { } have && Store is { } incoming && have.PackageId == incoming.PackageId
                                  && (have.AssetId == incoming.AssetId || (SingleAsset && Existing.Kind == Kind));

    /// <summary>기존 에셋과 내용이 같은지.</summary>
    public bool SameContent => Existing is { } existing && existing.ContentHash == AssetContent.HashOf(Kind, Data);
}

/// <summary>
/// 로컬 에셋 저장소(계획 7장). 앱 설정 폴더의 <c>Chickxel/Assets/</c> 아래:
/// <code>
/// library.json          모든 에셋(값, 작성자·라이선스·상점 연결, 들어온 길·시각, 딸린 파일 폴더)과 즐겨찾기
/// Files/&lt;폴더&gt;/        에셋에 딸린 파일(preview.png 받은 미리보기, scene.png 미리보기 장면, LICENSE.txt|md 받은 라이선스 문서)
/// Staging/              가져오는 중 임시 파일(열 때 비움)
/// Cache/                원격 카탈로그 cache(<see cref="RemoteAssetClient"/>)
/// v1-backup/            예전 저장소(설치 패키지 폴더·개인 에셋 파일)를 옮긴 뒤 남긴 원본
/// </code>
/// 에셋 하나하나가 단위다: 패키지 파일(<c>.chickxel-asset</c>)은 옮길 때만 쓰고, 가져오면 안의 에셋을 하나씩 넣는다(<see cref="Install"/>).
/// 딸린 파일은 바꿀 때마다 새 폴더에 쓰고 library.json을 임시 파일 + 바꿔치기로 쓴 뒤 옛 폴더를 지운다 — 어느 단계에서 끊겨도 이전 상태가 그대로이고,
/// 쓰이지 않는 폴더는 다음에 열 때 치운다. 네트워크는 다루지 않는다. 저장소를 바꾸는 메서드는 한 스레드(앱의 UI 스레드)에서 부른다.
/// 앱을 여러 개 띄워 같은 저장소를 쓰면, 바꾸기 전에 잠금 파일을 잡고 다른 앱이 그 사이 쓴 파일을 다시 읽은 뒤 바꾼다.
/// </summary>
public sealed partial class AssetStore
{
    public const string LibraryFileName = "library.json";
    public const int LibrarySchemaVersion = 2;
    /// <summary>이 컴퓨터에서 새로 만든 에셋 ID의 앞부분(뒤는 GUID).</summary>
    public const string LocalIdPrefix = "personal.";
    /// <summary>개인 내보내기 파일의 작성자 ID·패키지 ID 앞부분(<c>personal.</c>) — 가져올 때 작성자·상점 연결로 보지 않는다.</summary>
    public const string PersonalAuthorId = "personal";

    private const string PreviewFile = "preview.png", SceneFile = "scene.png";

    private readonly AssetValidationOptions _options;
    private List<Record> _records = [];
    private HashSet<string> _favorites = new(StringComparer.Ordinal);
    private readonly List<string> _loadErrors = [];
    /// <summary>예전 프리셋 파일(<see cref="AssetMigration"/>)을 이미 옮겼는지(library.json의 <c>legacyPresets</c>).</summary>
    private bool _legacyPresetsDone;
    /// <summary>마지막으로 읽거나 쓴 library.json의 (수정 시각, 길이) — 다른 앱이 바꿨는지 볼 때 쓴다.</summary>
    private (DateTime, long) _libraryStamp;
    private FileStream? _lock;
    private int _lockDepth;

    /// <summary>
    /// library.json의 한 줄. <see cref="Files"/>는 딸린 파일 폴더 이름(<c>Files/</c> 아래, 없으면 null). 읽지 못한 줄(모르는 종류, 미래 형식)은 <see cref="Raw"/>를 그대로 다시 쓴다.
    /// </summary>
    private sealed record Record(string Id, string Kind, DateTime AddedUtc, string Source, IAssetData? Data, AssetAuthor? Author, string? License,
        AssetStoreLink? Store, string? Files, JsonObject? Raw);

    private AssetStore(string root, AssetValidationOptions options)
    {
        Root = root;
        _options = options;
    }

    public string Root { get; }
    public string FilesDirectory => Path.Combine(Root, "Files");
    public string StagingDirectory => Path.Combine(Root, "Staging");
    public string CacheDirectory => Path.Combine(Root, "Cache");
    public string LibraryPath => Path.Combine(Root, LibraryFileName);

    /// <summary>library.json이 있는지.</summary>
    public bool HasLibraryFile => File.Exists(LibraryPath);

    /// <summary>
    /// 저장소를 쓸 수 없는 까닭(파일이 손상됐거나 미래 형식, migration 실패). null이면 쓸 수 있다.
    /// 쓸 수 없으면 바꾸는 메서드가 <see cref="InvalidOperationException"/>을 던진다 — 읽지 못한 파일을 빈 목록으로 덮어쓰지 않는다.
    /// </summary>
    public string? LibraryError { get; private set; }

    /// <summary>열 때 만난 문제(손상된 파일, 옮기지 못한 예전 패키지 등). 앱이 한 번 알린다.</summary>
    public IReadOnlyList<string> LoadErrors => _loadErrors;

    public AssetCatalog Catalog { get; private set; } = AssetCatalog.Empty;

    /// <summary>카탈로그가 바뀌었다(가져오기, 저장, 삭제, 즐겨찾기). 바꾼 스레드에서 부른다.</summary>
    public event Action? Changed;

    /// <summary>테스트용: 단계 이름("staged", "files", "committed")마다 불린다.</summary>
    internal Action<string>? StepHook { get; set; }

    /// <summary>
    /// 저장소를 연다(폴더가 없으면 만든다). 예전 저장소(설치 패키지 폴더·개인 에셋 파일)가 있고 library.json이 없으면 한 번 옮긴다.
    /// 잠금 파일을 바로 잡을 수 있으면 Staging과 쓰이지 않는 딸린 파일 폴더를 치운다(다른 앱이 쓰는 중이면 건너뜀).
    /// 파일이 손상돼도 예외 없이 열고 <see cref="LoadErrors"/>에 남긴다.
    /// </summary>
    public static AssetStore Open(string root, AssetValidationOptions? options = null, DateTime? nowUtc = null)
    {
        var store = new AssetStore(root, options ?? new AssetValidationOptions());
        store.Load(nowUtc ?? DateTime.UtcNow);
        return store;
    }

    private void Load(DateTime nowUtc)
    {
        foreach (var dir in new[] { Root, FilesDirectory, StagingDirectory, CacheDirectory })
            Directory.CreateDirectory(dir);
        if (!HasLibraryFile && HasVersion1Files)
        {
            using var change = BeginChange();
            if (!HasLibraryFile) MigrateVersion1(nowUtc);
        }
        using var cleanup = TryLockNow();
        if (cleanup is not null) TryDeleteContents(StagingDirectory);
        LoadLibrary();
        if (LibraryError is null && cleanup is not null) RemoveUnusedFiles();
        Rebuild(notify: false);
    }

    // 잠금 파일을 기다리지 않고 잡아 본다(다른 쪽이 쥐고 있으면 null)
    private FileStream? TryLockNow()
    {
        try
        {
            return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 다른 앱이나 같은 폴더의 다른 저장소 객체(예: 백그라운드 공식 효과 설치)가 바꾼 파일을 다시 읽는다(바뀌었으면 <see cref="Changed"/>).
    /// 그쪽이 바꾸는 중이면 끝날 때까지 기다린다.
    /// </summary>
    public void Refresh()
    {
        using var change = BeginChange();
    }

    // ───────────── library.json ─────────────

    private void LoadLibrary()
    {
        if (!HasLibraryFile) return;
        try
        {
            var issues = new List<AssetIssue>();
            var node = AssetJson.Parse(File.ReadAllBytes(LibraryPath), LibraryFileName, 64, 1 << 20, issues) as JsonObject
                       ?? throw new InvalidDataException(issues.FirstOrDefault()?.Message ?? LibraryFileName);
            if (!AssetJson.TryInt(node["schemaVersion"], out int schema) || schema < LibrarySchemaVersion) throw new InvalidDataException(LibraryFileName);
            if (schema > LibrarySchemaVersion) throw new InvalidDataException(new AssetIssue(AssetIssueCode.FutureSchema, LibraryFileName, schema.ToString(CultureInfo.InvariantCulture)).Message);
            var records = new List<Record>();
            foreach (var item in node["assets"] as JsonArray ?? [])
                if (item is JsonObject o && ReadRecord(o) is { } record && records.All(r => r.Id != record.Id))
                    records.Add(record);
            _records = records;
            _favorites = new HashSet<string>((node["favorites"] as JsonArray ?? []).Select(AssetJson.String).OfType<string>(), StringComparer.Ordinal);
            _legacyPresetsDone = AssetJson.TryBool(node["legacyPresets"], out bool legacy) && legacy;
            _libraryStamp = StampOf(LibraryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LibraryError = ex.Message;
            _loadErrors.Add($"{LibraryFileName}: {ex.Message}");
        }
    }

    /// <summary>한 줄을 읽는다. 값을 읽지 못하면(모르는 종류·효과) 원본을 그대로 둔 줄, ID가 없으면 null.</summary>
    private Record? ReadRecord(JsonObject o)
    {
        string id = AssetJson.String(o["id"]) ?? "", kind = AssetJson.String(o["kind"]) ?? "";
        if (id.Length == 0) return null;
        DateTime added = DateTime.TryParse(AssetJson.String(o["addedAt"]), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : default;
        string source = AssetJson.String(o["source"]) ?? AssetSources.Local;
        string? files = AssetJson.String(o["files"]);
        if (files is not null && !IsFilesFolder(files)) files = null;
        bool bad = false;
        var author = o["author"] is null ? null : AssetManifest.ReadAuthor(o["author"], "author", _ => bad = true);
        string? license = AssetJson.String(o["license"]);
        if (license is not null && !AssetManifest.IsValidLicense(license)) bad = true;
        var store = o["store"] is null ? null : AssetStoreLink.Read(o["store"], out _);
        if (o["store"] is not null && store is null) bad = true;
        IAssetData? data = null;
        if (!bad && AssetKinds.Find(kind) is { } definition && o["data"] is JsonObject json)
        {
            // 저장소는 앱이 쓴 데이터: 느슨하게 읽어 모르는 효과도 보존한다. 읽지 못한 줄은 원본 그대로 남긴다.
            var context = new AssetReadContext(id, strict: false) { AssetId = id };
            data = definition.ReadPersonal(json, context);
            if (data is not null && data.Id != id) data = WithId(data, id);
            if (data is ScriptAsset script && files is not null) data = WithSceneFile(script, Path.Combine(FilesDirectory, files, SceneFile));
        }
        return data is null
            ? new Record(id, kind, added, source, null, null, null, null, files, (JsonObject)o.DeepClone())
            : new Record(id, kind, added, source, data, author, license, store, files, null);
    }

    private void SaveLibrary(List<Record> records, HashSet<string> favorites)
    {
        if (LibraryError is { } error) throw new InvalidOperationException(error);
        WriteLibraryFile(records, favorites);
        _records = records;
        _favorites = favorites;
        StepHook?.Invoke("committed");
        Rebuild(notify: true);
    }

    private void WriteLibraryFile(List<Record> records, HashSet<string> favorites)
    {
        var assets = new JsonArray();
        foreach (var record in records)
        {
            if (record.Raw is { } raw)
            {
                assets.Add(raw.DeepClone());
                continue;
            }
            var json = new JsonObject
            {
                ["id"] = record.Id,
                ["kind"] = record.Kind,
                ["addedAt"] = record.AddedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["source"] = record.Source,
            };
            if (record.Files is { } files) json["files"] = files;
            if (record.Author is { } author) json["author"] = AssetManifest.AuthorJson(author);
            if (record.License is { } license) json["license"] = license;
            if (record.Store is { } store) json["store"] = store.ToJson();
            json["data"] = AssetKinds.Find(record.Kind)!.WritePersonal(record.Data!, record.Id);
            assets.Add(json);
        }
        var known = records.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var favs = new JsonArray(favorites.Where(known.Contains).Order(StringComparer.Ordinal).Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
        AtomicFile.Write(LibraryPath, AssetJson.ToUtf8(new JsonObject
        {
            ["schemaVersion"] = LibrarySchemaVersion,
            ["assets"] = assets,
            ["favorites"] = favs,
            ["legacyPresets"] = _legacyPresetsDone,
        }));
        _libraryStamp = StampOf(LibraryPath);
    }

    // ───────────── 여러 앱 ─────────────

    private static (DateTime, long) StampOf(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (info.LastWriteTimeUtc, info.Length) : default;
    }

    /// <summary>여러 앱이 저장소를 바꿀 때 차례를 정하는 잠금 파일(바꾸는 동안만 쥔다).</summary>
    internal const string LockFileName = "store.lock";

    private string LockPath => Path.Combine(Root, LockFileName);

    /// <summary>잠금 파일을 잡을 때까지 기다리는 최대 시간(다른 앱이 가져오는 중이면 끝날 때까지).</summary>
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 저장소를 바꾸기 시작: 잠금 파일을 잡고(다른 앱이 쥐고 있으면 기다림) 다른 앱이 쓴 library.json을 다시 읽는다. 돌려받은 것을 닫으면 잠금을 푼다.
    /// 바꾸는 메서드 안에서 다시 불러도 된다.
    /// </summary>
    private IDisposable BeginChange()
    {
        if (_lockDepth++ == 0)
        {
            var deadline = DateTime.UtcNow + LockTimeout;
            while (true)
            {
                try
                {
                    _lock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    break;
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                }
                catch
                {
                    _lockDepth--;
                    throw;
                }
            }
            ReloadIfChanged();
        }
        return new ChangeScope(this);
    }

    private sealed class ChangeScope(AssetStore store) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (--store._lockDepth > 0) return;
            store._lock?.Dispose();
            store._lock = null;
        }
    }

    /// <summary>다른 앱이 library.json을 바꿨으면 다시 읽는다(읽지 못하면 그 까닭을 남겨 덮어쓰지 않는다).</summary>
    private void ReloadIfChanged()
    {
        if (StampOf(LibraryPath) == _libraryStamp) return;
        LibraryError = null;
        _records = [];
        _favorites = new HashSet<string>(StringComparer.Ordinal);
        _legacyPresetsDone = false;
        LoadLibrary();
        _libraryStamp = StampOf(LibraryPath);
        Rebuild(notify: true);
    }

    // ───────────── 카탈로그 ─────────────

    private void Rebuild(bool notify)
    {
        Catalog = new AssetCatalog(_records.Where(r => r.Data is not null).Select(ToEntry).ToArray());
        if (notify) Changed?.Invoke();
    }

    private AssetEntry ToEntry(Record record)
    {
        string? folder = record.Files is { } files ? Path.Combine(FilesDirectory, files) : null;
        return new AssetEntry(record.Id, record.Kind, record.Data!)
        {
            Author = record.Author,
            License = record.License,
            Store = record.Store,
            Source = record.Source,
            AddedUtc = record.AddedUtc,
            Favorite = _favorites.Contains(record.Id),
            PreviewPath = folder is not null && File.Exists(Path.Combine(folder, PreviewFile)) ? Path.Combine(folder, PreviewFile) : null,
            LicenseFilePath = folder is null ? null
                : LicenseDocuments.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists),
        };
    }

    /// <summary>받은 패키지에서 에셋에 딸려 두는 라이선스 문서 이름.</summary>
    private static readonly string[] LicenseDocuments = ["LICENSE.txt", "LICENSE.md"];

    // ───────────── 가져오기 ─────────────

    /// <summary>
    /// 온라인 카탈로그에서 받은 패키지를 설치한다. 목록의 ID·버전과 파일의 manifest가 같아야 한다.
    /// 같은 패키지의 연결된 에셋은 더 높은 버전일 때만 고친 내용까지 교체하고 중복 연결을 하나로 합친다.
    /// 파일에서 직접 가져오는 <see cref="Install"/>의 겹침 선택에는 영향을 주지 않는다.
    /// </summary>
    public IReadOnlyList<AssetEntry> InstallRemote(string packagePath, string packageId, string version,
        Func<AssetImportItem, ImportChoice>? choose = null, CancellationToken cancellation = default)
    {
        if (!AssetManifest.IsValidId(packageId) || !AssetManifest.IsValidVersion(version))
            throw new ArgumentException("Invalid remote package identity.");
        return InstallCore(packagePath, AssetSources.Remote, choose, (packageId, version), cancellation);
    }

    /// <summary>
    /// 패키지 파일(<c>.chickxel-asset</c>)의 에셋을 하나씩 넣는다(계획 7.3). 먼저 파일 전체를 검사하고(크기, 경로, manifest, 앱 호환성, 해시, 모든 에셋 값),
    /// 실패하면 <see cref="AssetPackageException"/>이고 아무것도 바뀌지 않는다. 에셋마다 작성자·라이선스·상점 연결은 그 줄에 적힌 것, 없으면 패키지의 것이다
    /// (개인 내보내기 파일 — 작성자 <see cref="PersonalAuthorId"/>, ID <c>personal.</c> — 의 것은 쓰지 않음).
    /// 기존 에셋과 겹치면(같은 상점 에셋, 아니면 같은 종류·같은 내용) <paramref name="choose"/>에 물어 바꾸거나 건너뛰거나 하나 더 둔다.
    /// 들어간(새로 넣거나 바꾼) 에셋을 패키지 순서로 돌려준다.
    /// </summary>
    public IReadOnlyList<AssetEntry> Install(string packagePath, string source, Func<AssetImportItem, ImportChoice> choose, CancellationToken cancellation = default)
        => InstallCore(packagePath, source, choose, null, cancellation);

    private IReadOnlyList<AssetEntry> InstallCore(string packagePath, string source, Func<AssetImportItem, ImportChoice>? choose,
        (string Id, string Version)? remote, CancellationToken cancellation)
    {
        using var change = BeginChange();
        if (LibraryError is { } error) throw new InvalidOperationException(error);
        string token = Guid.NewGuid().ToString("N");
        string stagedFile = Path.Combine(StagingDirectory, token + AssetPackage.Extension);
        var written = new List<string>();
        bool committed = false;
        try
        {
            var limits = _options.EffectiveLimits;
            if (new FileInfo(packagePath).Length > limits.MaxPackageBytes)
                throw new AssetPackageException(new AssetIssue(AssetIssueCode.PackageTooLarge, null, limits.MaxPackageBytes.ToString(CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(StagingDirectory);
            File.Copy(packagePath, stagedFile);
            StepHook?.Invoke("staged");
            cancellation.ThrowIfCancellationRequested();
            var package = AssetPackageValidator.Read(stagedFile, _options);
            cancellation.ThrowIfCancellationRequested();

            var manifest = package.Manifest;
            if (remote is { } expected && (manifest.Id != expected.Id || manifest.Version != expected.Version))
                throw new InvalidDataException("Downloaded package identity or version does not match the catalog.");
            if (remote is not null && package.Items.Any(item => item.Entry.Store is { } link
                    && (link.PackageId != manifest.Id || link.Version != manifest.Version || link.AssetId != item.Entry.Id)))
                throw new InvalidDataException("Downloaded package contains a mismatched store link.");
            if (remote is not null && manifest.Id.StartsWith(LocalIdPrefix, StringComparison.Ordinal))
                throw new InvalidDataException("A personal export cannot be installed from the remote catalog.");
            bool personal = manifest.Author.Id == PersonalAuthorId;
            bool listed = !manifest.Id.StartsWith(LocalIdPrefix, StringComparison.Ordinal);
            byte[]? licenseDocument = null;
            string? licenseName = LicenseDocuments.FirstOrDefault(package.Files.ContainsKey);
            if (licenseName is not null) licenseDocument = package.Files[licenseName];

            var records = _records.ToList();
            var favorites = new HashSet<string>(_favorites, StringComparer.Ordinal);
            var installed = new List<string>();
            var removedFolders = new List<string>();
            bool single = package.Items.Count == 1;
            bool deduplicated = false;
            void RemoveLinkedDuplicates(Record[] group, string retainedId)
            {
                if (group.Length <= 1) return;
                bool favorite = group.Any(r => favorites.Contains(r.Id));
                foreach (var duplicate in group)
                {
                    if (duplicate.Id == retainedId) continue;
                    records.Remove(duplicate);
                    favorites.Remove(duplicate.Id);
                    if (duplicate.Files is { } old) removedFolders.Add(old);
                    deduplicated = true;
                }
                if (favorite) favorites.Add(retainedId);
            }
            foreach (var (entry, data) in package.Items)
            {
                cancellation.ThrowIfCancellationRequested();
                string hash = AssetContent.HashOf(entry.Kind, data);
                var author = entry.Author ?? (personal ? null : manifest.Author);
                if (author?.Id == PersonalAuthorId) author = null;
                var license = entry.License ?? (personal ? null : manifest.License);
                var store = remote is not null ? AssetStoreLink.FromManifest(manifest, entry.Id, hash)
                    : entry.Store ?? (listed ? AssetStoreLink.FromManifest(manifest, entry.Id, hash) : null);
                var matches = remote is null || store is null ? null : records.Where(r => r.Data is not null && r.Kind == entry.Kind
                    && r.Store is { } link && link.PackageId == store.PackageId
                    && (link.AssetId == store.AssetId || single)).ToArray();
                if (matches is { Length: > 0 } && matches.Any(r => r.Store!.SemanticVersion.CompareTo(manifest.SemanticVersion) >= 0))
                {
                    if (matches.Length > 1)
                    {
                        var newest = matches[0];
                        foreach (var candidate in matches)
                            if (candidate.Store!.SemanticVersion.CompareTo(newest.Store!.SemanticVersion) > 0) newest = candidate;
                        RemoveLinkedDuplicates(matches, newest.Id);
                    }
                    continue;
                }
                var existing = matches is { Length: > 0 }
                    ? matches.FirstOrDefault(r => r.Store!.AssetId == store!.AssetId) ?? matches[0]
                    : remote is null ? FindExisting(records, entry.Kind, hash, store, single)
                    : choose is null ? null : records.FirstOrDefault(r => r.Data is not null && r.Kind == entry.Kind && AssetContent.HashOf(entry.Kind, r.Data) == hash);
                var sameRemote = matches is { Length: > 0 };
                var choice = sameRemote ? ImportChoice.Replace : existing is null ? ImportChoice.KeepBoth
                    : choose!(new AssetImportItem(entry.Kind, data, author, license, store, ToEntry(existing), single));
                if (choice == ImportChoice.Skip) continue;

                bool replace = choice == ImportChoice.Replace && existing is not null;
                string id = replace ? existing!.Id : NewId(records, store?.AssetId);
                byte[]? preview = entry.Preview is { } p && package.Files.TryGetValue(p, out var png) ? png : null;
                package.Files.TryGetValue(AssetPackageBuilder.ScenePath(entry.Path), out var scene);
                if (data is not ScriptAsset { Scene: not null }) scene = null;
                string? folder = WriteFiles(preview, scene, licenseName is null ? null : (licenseName, licenseDocument!));
                if (folder is not null) written.Add(folder);
                var record = new Record(id, entry.Kind, DateTime.UtcNow, source, WithId(data, id), author, license, store, folder, null);
                if (replace)
                {
                    int index = records.FindIndex(r => r.Id == id);
                    if (records[index].Files is { } old) removedFolders.Add(old);
                    records[index] = record;
                }
                else records.Add(record);
                if (matches is { Length: > 1 }) RemoveLinkedDuplicates(matches, id);
                installed.Add(id);
            }
            StepHook?.Invoke("files");
            cancellation.ThrowIfCancellationRequested();
            if (installed.Count > 0 || deduplicated)
            {
                SaveLibrary(records, favorites);
                committed = true;
                foreach (var old in removedFolders) TryDeleteDirectory(Path.Combine(FilesDirectory, old));
            }
            return installed.Select(id => Catalog.Find(id)!).ToArray();
        }
        finally
        {
            TryDeleteFile(stagedFile);
            if (!committed)
                foreach (var folder in written) TryDeleteDirectory(Path.Combine(FilesDirectory, folder));
        }
    }

    /// <summary>
    /// 가져오는 에셋과 겹치는 기존 줄: 같은 상점 에셋(패키지 ID·에셋 ID — 에셋이 하나뿐인 패키지면 같은 패키지 ID·종류), 아니면 같은 종류·같은 내용.
    /// </summary>
    private static Record? FindExisting(List<Record> records, string kind, string hash, AssetStoreLink? store, bool single)
        => (store is null ? null
               : records.FirstOrDefault(r => r.Data is not null && r.Store is { } s && s.PackageId == store.PackageId && s.AssetId == store.AssetId)
                 ?? (single ? records.FirstOrDefault(r => r.Data is not null && r.Kind == kind && r.Store?.PackageId == store.PackageId) : null))
           ?? records.FirstOrDefault(r => r.Data is not null && r.Kind == kind && AssetContent.HashOf(kind, r.Data) == hash);

    // ───────────── 내 에셋 저장 ─────────────

    /// <summary>
    /// 효과 레시피를 저장한다. <see cref="EffectRecipe.Id"/>가 있는 에셋이면 그 자리의 값을 바꾸고(작성자·상점 연결은 그대로 — 고친 것으로 보임), 아니면 이 컴퓨터의 새 에셋으로 끝에 더한다.
    /// 매개변수가 연결·범위 검사(<see cref="Effects.RecipeParameters.Check"/>)를 통과하지 않으면 <see cref="AssetPackageException"/>. 저장된 항목을 돌려준다.
    /// </summary>
    public AssetEntry SaveRecipe(EffectRecipe recipe)
    {
        var issues = new List<AssetIssue>();
        Effects.RecipeParameters.Check(recipe.Effects, recipe.Parameters, (code, detail) => issues.Add(new AssetIssue(code, null, detail)));
        if (issues.Count > 0) throw new AssetPackageException(issues);
        return SaveData(AssetKinds.EffectRecipe, recipe, favorite: null, scene: null, sceneChanges: false);
    }

    /// <summary>그라데이션을 저장한다(<paramref name="id"/>가 그라데이션 에셋이면 바꾸고, 아니면 새로 더함). 즐겨찾기는 <see cref="GradientPreset.Favorite"/>를 따른다.</summary>
    public AssetEntry SaveGradient(string? id, GradientPreset gradient)
    {
        GradientAssetFormat.Validate(gradient);
        return SaveData(AssetKinds.Gradient, new GradientAsset(id ?? "", gradient with { Favorite = false }), gradient.Favorite, scene: null, sceneChanges: false);
    }

    /// <summary>
    /// 스크립트 원문을 스크립트 에셋으로 저장한다(이름·작성자는 원문의 선언). 패키지 가져오기와 같은 검사(<see cref="ScriptAssetFormat.Check"/>)를
    /// 통과하지 못하면 <see cref="AssetPackageException"/>. <paramref name="id"/>가 스크립트 에셋이면 그 자리의 원문과 미리보기 장면을 바꾸고
    /// (<paramref name="scene"/>이 null이면 장면을 지움), 아니면 이 컴퓨터에서 만든(작성자·상점 연결 없는) 같은 원문의 스크립트가 있을 때 새로 더하지 않고
    /// 그 항목을 돌려준다 — <paramref name="scene"/>이 있고 그 항목의 장면과 다르면 그 항목의 장면만 바꾼다(null이면 그대로 둠). 실행 허락은 앱이 정한다.
    /// </summary>
    public AssetEntry SaveScript(string source, string? id, PreviewScene? scene)
    {
        var issues = new List<AssetIssue>();
        var context = new AssetReadContext(AssetKinds.EffectScript, strict: true, _options.EffectiveLimits, issues);
        if (ScriptAssetFormat.Check(source, "source", context) is not { } program) throw new AssetPackageException(issues);
        var asset = ScriptAsset.From("", program) with { Scene = scene };
        if (id is not null && Catalog.Find(id) is { Kind: AssetKinds.EffectScript }) return SaveData(AssetKinds.EffectScript, asset with { Id = id }, null, scene, sceneChanges: true);
        if (Catalog.All.FirstOrDefault(e => e is { Author: null, Store: null } && e.Data is ScriptAsset s && s.Hash == program.ContentHash) is { } existing)
            return scene is null || SameScene(existing.Script!.Scene, scene) ? existing : SaveData(AssetKinds.EffectScript, asset with { Id = existing.Id }, null, scene, sceneChanges: true);
        return SaveData(AssetKinds.EffectScript, asset, null, scene, sceneChanges: true);
    }

    private static bool SameScene(PreviewScene? a, PreviewScene b) => a is not null && a.ToPng().AsSpan().SequenceEqual(b.ToPng());

    /// <summary>
    /// 에셋 값을 저장한다: <paramref name="data"/>의 ID가 같은 종류의 에셋이면 그 자리(작성자·라이선스·상점 연결·들어온 길은 그대로, 받은 미리보기는 지금 값과
    /// 맞지 않으므로 뺌), 아니면 이 컴퓨터의 새 에셋. <paramref name="sceneChanges"/>면 딸린 장면을 <paramref name="scene"/>으로 바꾼다.
    /// </summary>
    private AssetEntry SaveData(string kind, IAssetData data, bool? favorite, PreviewScene? scene, bool sceneChanges)
    {
        using var change = BeginChange();
        if (LibraryError is { } error) throw new InvalidOperationException(error);
        if (!AssetManifest.IsValidName(data.Name)) throw new InvalidDataException(new AssetIssue(AssetIssueCode.InvalidField, null, "name").Message);
        var records = _records.ToList();
        int index = records.FindIndex(r => r.Id == data.Id && r.Kind == kind && r.Data is not null);
        var old = index >= 0 ? records[index] : null;
        string id = old?.Id ?? NewId(records, null);
        string? oldFolder = old?.Files;
        byte[]? keptScene = sceneChanges ? scene?.ToPng() : ReadFile(oldFolder, SceneFile);
        var license = ReadLicense(oldFolder);
        string? folder = keptScene is null && license is null ? null : WriteFiles(null, keptScene, license);
        try
        {
            var record = old is null
                ? new Record(id, kind, DateTime.UtcNow, AssetSources.Local, WithId(data, id), null, null, null, folder, null)
                : old with { Data = WithId(data, id), Files = folder };
            if (old is null) records.Add(record);
            else records[index] = record;
            var favorites = new HashSet<string>(_favorites, StringComparer.Ordinal);
            if (favorite == true) favorites.Add(id);
            else if (favorite == false) favorites.Remove(id);
            SaveLibrary(records, favorites);
        }
        catch
        {
            if (folder is not null) TryDeleteDirectory(Path.Combine(FilesDirectory, folder));
            throw;
        }
        if (oldFolder is not null) TryDeleteDirectory(Path.Combine(FilesDirectory, oldFolder));
        return Catalog.Find(id)!;
    }

    /// <summary>
    /// 올린 뒤 에셋에 작성자·라이선스·상점 연결을 붙인다(게시 창이 올리기에 성공했을 때). 이 연결로 업데이트를 보고, 다른 컴퓨터에서 받은 것도 같은 에셋으로 알아본다.
    /// 게시 패키지는 에셋 하나라 같은 패키지에 연결된 같은 종류의 다른 에셋(받아 둔 지난 버전 등)은 이 에셋이 대신한다: 고치지 않은 것은 지우고(즐겨찾기는 이 에셋으로),
    /// 고친 것은 연결을 끊어 이 컴퓨터의 사본으로 둔다 — 같은 상점 에셋이 둘이면 업데이트가 하나에만 들어가 효과가 둘로 남는다.
    /// </summary>
    public AssetEntry MarkPublished(string id, AssetAuthor author, string license, AssetStoreLink link)
    {
        using var change = BeginChange();
        if (LibraryError is { } error) throw new InvalidOperationException(error);
        var target = _records.FirstOrDefault(r => r.Id == id && r.Data is not null) ?? throw new ArgumentException(id, nameof(id));
        var favorites = new HashSet<string>(_favorites, StringComparer.Ordinal);
        var records = new List<Record>(_records.Count);
        var removedFolders = new List<string>();
        foreach (var record in _records)
        {
            if (record.Id == id) records.Add(record with { Author = author, License = license, Store = link });
            else if (record.Data is null || record.Kind != target.Kind || record.Store is not { } other || other.PackageId != link.PackageId) records.Add(record);
            else if (other.ContentHash != AssetContent.HashOf(record.Kind, record.Data))
                records.Add(record with { Author = null, License = null, Store = null, Source = AssetSources.Local });
            else
            {
                if (favorites.Remove(record.Id)) favorites.Add(id);
                if (record.Files is { } folder) removedFolders.Add(folder);
            }
        }
        SaveLibrary(records, favorites);
        foreach (var folder in removedFolders) TryDeleteDirectory(Path.Combine(FilesDirectory, folder));
        return Catalog.Find(id)!;
    }

    /// <summary>에셋을 지운다(딸린 파일도). 프로젝트에 넣은 효과·그라데이션은 값 사본이라 바뀌지 않는다.</summary>
    public void Remove(string id)
    {
        using var change = BeginChange();
        if (LibraryError is { } error) throw new InvalidOperationException(error);
        var records = _records.ToList();
        var record = records.FirstOrDefault(r => r.Id == id);
        if (record is null) return;
        records.Remove(record);
        var favorites = new HashSet<string>(_favorites, StringComparer.Ordinal);
        favorites.Remove(id);
        SaveLibrary(records, favorites);
        if (record.Files is { } folder) TryDeleteDirectory(Path.Combine(FilesDirectory, folder));
    }

    /// <summary>에셋의 즐겨찾기를 켜거나 끈다(컴퓨터별 상태).</summary>
    public void SetFavorite(string assetId, bool favorite)
    {
        using var change = BeginChange();
        if (LibraryError is { } error) throw new InvalidOperationException(error);
        if (_favorites.Contains(assetId) == favorite) return;
        var favorites = new HashSet<string>(_favorites, StringComparer.Ordinal);
        if (favorite) favorites.Add(assetId);
        else favorites.Remove(assetId);
        SaveLibrary(_records.ToList(), favorites);
    }

    /// <summary>
    /// 기본 그라데이션(<see cref="GradientPresets.BuiltIns"/>)을 처음 값으로 되돌리고 지운 것은 끝에 다시 넣는다(이 컴퓨터에서 만든 그라데이션을 이름으로 찾음, 대소문자 무시).
    /// 받은 그라데이션, 직접 만든 다른 그라데이션과 즐겨찾기는 그대로 둔다.
    /// </summary>
    public void RestoreBuiltInGradients()
    {
        using var change = BeginChange();
        if (LibraryError is { } error) throw new InvalidOperationException(error);
        var records = _records.ToList();
        foreach (var builtIn in GradientPresets.BuiltIns())
        {
            int index = records.FindIndex(r => r is { Kind: AssetKinds.Gradient, Author: null, Store: null } && r.Data is GradientAsset g
                                               && string.Equals(g.Gradient.Name, builtIn.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) records[index] = records[index] with { Data = new GradientAsset(records[index].Id, builtIn) };
            else
            {
                string id = NewId(records, null);
                records.Add(new Record(id, AssetKinds.Gradient, DateTime.UtcNow, AssetSources.Local, new GradientAsset(id, builtIn), null, null, null, null, null));
            }
        }
        SaveLibrary(records, new HashSet<string>(_favorites, StringComparer.Ordinal));
    }

    /// <summary>예전 프리셋 파일을 이미 옮겼는지(옮기지 않았으면 <see cref="AssetMigration"/>이 다음 실행에 다시 시도).</summary>
    internal bool LegacyPresetsMigrated => _legacyPresetsDone;

    /// <summary>
    /// 예전 프리셋을 이 컴퓨터의 에셋으로 앞에 더하고 옮겼다고 적는다(<see cref="AssetMigration"/> 전용). 잠금을 잡은 뒤 이미 옮겼으면(같은 저장소를 쓰는
    /// 다른 앱이 먼저 옮김) 다시 읽고 false. library.json을 읽지 못했으면 덮어쓰지 않고 false. <paramref name="items"/>는 (종류, 값, 즐겨찾기) 순서대로 새 ID를 받는다.
    /// </summary>
    internal bool TryAddLegacyPresets(IEnumerable<(string Kind, IAssetData Data, bool Favorite)> items, DateTime nowUtc)
    {
        using var change = BeginChange();
        if (_legacyPresetsDone || LibraryError is not null) return false;
        var records = _records.ToList();
        var added = new List<Record>();
        var favorites = new HashSet<string>(_favorites, StringComparer.Ordinal);
        foreach (var (kind, data, favorite) in items)
        {
            string id = NewId([.. records, .. added], null);
            added.Add(new Record(id, kind, nowUtc, AssetSources.Local, WithId(data, id), null, null, null, null, null));
            if (favorite) favorites.Add(id);
        }
        _legacyPresetsDone = true;
        try
        {
            SaveLibrary([.. added, .. records], favorites);
        }
        catch
        {
            _legacyPresetsDone = false;
            throw;
        }
        return true;
    }

    // ───────────── 내보내기 ─────────────

    /// <summary>
    /// 에셋을 패키지 파일 하나로 묶는다. 고치지 않은 상점 에셋 하나면 그 상점 패키지 그대로(ID·버전·목록 글·작성자·라이선스, 받은 라이선스 문서) 쓰고,
    /// 그 밖에는 <paramref name="bundleHeader"/>(개인 내보내기 정보) 아래에 에셋마다 작성자·라이선스·상점 연결을 적는다 — 이 컴퓨터에서 만든 에셋의 작성자는
    /// <paramref name="localAuthor"/>(로그인한 계정, 없으면 비움 — 받는 쪽에서 그 사람이 만든 것으로 보임).
    /// <paramref name="preview"/>는 에셋마다 미리보기 PNG를 만드는 함수(null이면 넣지 않음). 만든 패키지는 검사를 다시 통과해야 쓴다 —
    /// 통과하지 못하면 <see cref="AssetPackageException"/>이고 <paramref name="output"/>에는 쓰지 않는다.
    /// </summary>
    public AssetManifest Export(IEnumerable<string> ids, AssetManifest bundleHeader, Stream output, AssetAuthor? localAuthor = null,
        Func<AssetEntry, PackagePreview?>? preview = null)
    {
        var entries = ids.Select(id => Catalog.Find(id) ?? throw new ArgumentException(id, nameof(ids))).ToList();
        if (entries.Count == 0) throw new ArgumentException(null, nameof(ids));
        AssetPackageBuilder builder;
        if (entries is [{ Store: { } store, Author: { } author, License: { } license, Modified: false } single])
        {
            builder = new AssetPackageBuilder(new AssetManifest
            {
                Id = store.PackageId, Version = store.Version, Name = store.Name, Description = store.Description, Translations = store.Translations,
                Author = author, License = license, MinAppVersion = store.MinAppVersion,
            });
            builder.Add(single.Kind, single.Data, preview?.Invoke(single), segment: store.AssetId[(store.PackageId.Length + 1)..]);
            if (single.LicenseFilePath is { } path) builder.AddDocument(Path.GetFileName(path), File.ReadAllText(path, Encoding.UTF8));
        }
        else
        {
            builder = new AssetPackageBuilder(bundleHeader);
            foreach (var entry in entries)
                builder.Add(entry.Kind, entry.Data, preview?.Invoke(entry), author: entry.Author ?? localAuthor, license: entry.License, store: entry.Store);
        }
        using var buffer = new MemoryStream();
        var manifest = builder.Write(buffer);
        buffer.Position = 0;
        AssetPackageValidator.Read(buffer, _options with { AppVersion = null });
        buffer.Position = 0;
        buffer.CopyTo(output);
        return manifest;
    }

    // ───────────── 딸린 파일 ─────────────

    /// <summary>딸린 파일을 새 폴더에 쓰고 그 이름을 돌려준다(쓸 것이 없으면 null).</summary>
    private string? WriteFiles(byte[]? preview, byte[]? scene, (string Name, byte[] Data)? license)
    {
        if (preview is null && scene is null && license is null) return null;
        string name = Guid.NewGuid().ToString("N");
        string folder = Path.Combine(FilesDirectory, name);
        Directory.CreateDirectory(folder);
        if (preview is not null) File.WriteAllBytes(Path.Combine(folder, PreviewFile), preview);
        if (scene is not null) File.WriteAllBytes(Path.Combine(folder, SceneFile), scene);
        if (license is { } doc) File.WriteAllBytes(Path.Combine(folder, doc.Name), doc.Data);
        return name;
    }

    private byte[]? ReadFile(string? folder, string name)
    {
        if (folder is null) return null;
        string path = Path.Combine(FilesDirectory, folder, name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private (string Name, byte[] Data)? ReadLicense(string? folder)
        => LicenseDocuments.Select(name => ReadFile(folder, name) is { } data ? (name, data) : ((string, byte[])?)null).FirstOrDefault(doc => doc is not null);

    /// <summary>딸린 파일 폴더 이름(<see cref="WriteFiles"/>가 만든 32자 16진수)인지.</summary>
    private static bool IsFilesFolder(string name) => name is { Length: 32 } && name.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>스크립트에 장면 파일을 붙인다(없으면 그대로). 읽지 못하면 장면 없이 두고 <see cref="LoadErrors"/>에 남긴다.</summary>
    private ScriptAsset WithSceneFile(ScriptAsset script, string path)
    {
        if (!File.Exists(path)) return script;
        try
        {
            return script with { Scene = PreviewScene.FromPng(File.ReadAllBytes(path)) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _loadErrors.Add($"{path}: {ex.Message}");
            return script;
        }
    }

    /// <summary>library.json이 가리키지 않는 딸린 파일 폴더(쓰다가 끊긴 것, 바꾼 뒤 지우지 못한 옛 폴더)를 지운다.</summary>
    private void RemoveUnusedFiles()
    {
        var used = _records.Select(r => r.Files).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var dir in SafeDirectories(FilesDirectory))
            if (!used.Contains(Path.GetFileName(dir))) TryDeleteDirectory(dir);
    }

    // ───────────── 도구 ─────────────

    /// <summary>새 에셋 ID: <paramref name="preferred"/>(받은 에셋의 원래 ID)가 비어 있으면 그것, 아니면 <see cref="LocalIdPrefix"/> + GUID.</summary>
    private static string NewId(List<Record> records, string? preferred)
    {
        if (preferred is { Length: > 0 } && records.All(r => r.Id != preferred)) return preferred;
        string id;
        do id = LocalIdPrefix + Guid.NewGuid().ToString("N");
        while (records.Any(r => r.Id == id));
        return id;
    }

    private static IAssetData WithId(IAssetData data, string id) => data switch
    {
        EffectRecipe recipe => recipe with { Id = id },
        GradientAsset gradient => gradient with { Id = id },
        ScriptAsset script => script with { Id = id },
        _ => data,
    };

    private static IEnumerable<string> SafeDirectories(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetDirectories(dir) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void TryDeleteContents(string dir)
    {
        foreach (var sub in SafeDirectories(dir)) TryDeleteDirectory(sub);
        try
        {
            foreach (var file in Directory.GetFiles(dir)) TryDeleteFile(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
