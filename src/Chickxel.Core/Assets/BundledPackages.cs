using System.Text;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;

namespace Chickxel.Core.Assets;

/// <summary>
/// 앱에 든 공식 패키지 하나: 종류(되돌리기 단위), 패키지 ID, 앱에 든 버전, 패키지 바이트 만들기, 저장소의 에셋이 이 패키지의 에셋인지.
/// <paramref name="ReplacesLocalCopies"/>면 처음 넣을 때 이 컴퓨터에 있던 같은 내용의 사본(예전 판의 기본값을 내 에셋으로 넣어 둔 것)을 지우고 즐겨찾기를 옮긴다.
/// </summary>
public sealed record BundledPackage(string Kind, string PackageId, string Version, Func<byte[]> Build, Func<AssetEntry, bool> Matches, bool ReplacesLocalCopies = false);

/// <summary>
/// 앱에 든 공식 패키지(<see cref="All"/> — 공식 효과, 기본 그라데이션, 기본 모양 펜 세트)를 저장소에 기본으로 넣는다(출처 <see cref="AssetSources.Bundled"/>, 작성자 <c>chickxel</c>와
/// 버전이 있는 받은 에셋 — 개발자 계정은 그 자리에서 고치고 새 버전으로 올린다). 처음 실행에 모두 넣고, 앱에 든 버전이 더 새로우면 고치지 않은 것만 업데이트한다.
/// 사용자가 지운 패키지는 다시 넣지 않고(한 번 넣은 패키지 ID를 <see cref="StateFileName"/>에 기억) 종류별 되돌리기(<c>restoreKind</c>)만 지운 것을 다시 넣는다.
/// </summary>
public static class BundledPackages
{
    /// <summary>한 번 설치한 공식 패키지 ID를 기억하는 파일(저장소 뿌리 — 처음에는 공식 효과만 적던 이름 그대로).</summary>
    public const string StateFileName = "bundled-effects.json";

    /// <summary>앱에 든 공식 패키지(넣는 차례 = 목록 차례).</summary>
    public static IReadOnlyList<BundledPackage> All => _all ??= [.. OfficialEffectPackages.Bundled, .. OfficialGradientPackages.Bundled, .. OfficialShapeSets.Bundled];

    private static IReadOnlyList<BundledPackage>? _all;

    /// <summary>에셋이 앱에 든 공식 패키지의 것인지(상점 연결로).</summary>
    public static bool IsBundled(AssetEntry entry)
        => entry.Store is { } store && All.Any(package => package.PackageId == store.PackageId && package.Matches(entry));

    /// <summary>기본 설치 결과: 새로 넣은·업데이트한 패키지 ID와 실패(패키지 ID, 까닭).</summary>
    public sealed record SyncResult(IReadOnlyList<string> Installed, IReadOnlyList<string> Updated, IReadOnlyList<(string PackageId, string Error)> Errors)
    {
        public bool Changed => Installed.Count + Updated.Count > 0;
    }

    /// <summary>
    /// 공식 패키지를 저장소에 맞춘다. 패키지를 만들고 가져오므로 처음 실행에는 수 초가 걸린다 — 앱은 UI 스레드가 아닌 곳에서 같은 폴더의 다른 저장소 객체로 부르고
    /// (<see cref="NeedsSync"/>로 할 일이 있을 때만), 끝나면 앱의 저장소를 <see cref="AssetStore.Refresh"/>한다.
    /// 없는 패키지는 처음이면 넣고, 예전에 넣었던 것(사용자가 지움)은 그 종류가 <paramref name="restoreKind"/>일 때만 넣는다.
    /// 저장소의 버전이 앱에 든 버전보다 낮으면 업데이트한다(고친 것은 그대로 둠). 더 높은 버전(온라인에서 받음)은 그대로 둔다. 저장소를 쓸 수 없으면 아무것도 하지 않는다.
    /// </summary>
    public static SyncResult Sync(AssetStore store, string? restoreKind = null)
    {
        List<string> installed = [], updated = [];
        List<(string, string)> errors = [];
        if (store.LibraryError is not null) return new SyncResult(installed, updated, errors);
        var offered = ReadState(store);
        int offeredBefore = offered.Count;
        foreach (var package in All)
        {
            try
            {
                switch (StepOf(store, offered, package, restoreKind))
                {
                    case Step.Install:
                        Install(store, package);
                        installed.Add(package.PackageId);
                        break;
                    case Step.Update:
                        Install(store, package);
                        updated.Add(package.PackageId);
                        break;
                    case Step.None when Current(store, package) is null:
                        continue; // 사용자가 지운 패키지
                }
                offered.Add(package.PackageId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AssetPackageException or InvalidOperationException)
            {
                errors.Add((package.PackageId, ex.Message));
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
        return new SyncResult(installed, updated, errors);
    }

    /// <summary>
    /// <see cref="Sync"/>가 할 일이 있는지(넣기·업데이트, 처음 보는 공식 패키지 기억하기). 패키지를 만들지 않고 상태만 보므로 UI 스레드에서 불러도 된다.
    /// </summary>
    public static bool NeedsSync(AssetStore store, string? restoreKind = null)
    {
        if (store.LibraryError is not null) return false;
        var offered = ReadState(store);
        return All.Any(package => StepOf(store, offered, package, restoreKind) != Step.None);
    }

    private enum Step { None, Install, Update, Remember }

    private static AssetEntry? Current(AssetStore store, BundledPackage package)
        => store.Catalog.FromPackage(package.PackageId).FirstOrDefault(package.Matches);

    // 공식 패키지 하나에 할 일(Remember = 이미 있는 패키지를 처음 봄 — 설치 기록에만 더함)
    private static Step StepOf(AssetStore store, HashSet<string> offered, BundledPackage package, string? restoreKind)
    {
        var current = Current(store, package);
        if (current is null) return offered.Contains(package.PackageId) && package.Kind != restoreKind ? Step.None : Step.Install;
        if (!current.Modified && SemVersion.TryParse(package.Version, out var bundled) && current.Store!.SemanticVersion.CompareTo(bundled) < 0)
            return Step.Update;
        return offered.Contains(package.PackageId) ? Step.None : Step.Remember;
    }

    // 같은 공식 에셋은 새 버전으로 바꾸되 고친 것(작성자가 이 컴퓨터의 계정일 때만 생김)은 그대로 두고, 내용만 같은 다른 에셋(직접 만든 사본)은 건드리지 않는다
    // (ReplacesLocalCopies면 이 컴퓨터에서 만든 같은 내용의 사본만 지움)
    private static void Install(AssetStore store, BundledPackage package)
    {
        string file = Path.Combine(store.CacheDirectory, "bundled-" + package.PackageId + AssetPackage.Extension);
        Directory.CreateDirectory(store.CacheDirectory);
        File.WriteAllBytes(file, package.Build());
        try
        {
            store.Install(file, AssetSources.Bundled, item => !item.SameStoreAsset ? ImportChoice.KeepBoth
                : item.Existing!.Modified ? ImportChoice.Skip : ImportChoice.Replace);
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
        if (package.ReplacesLocalCopies && Current(store, package) is { } official) RemoveLocalCopies(store, official);
    }

    private static void RemoveLocalCopies(AssetStore store, AssetEntry official)
    {
        var copies = store.Catalog.All.Where(entry => entry.Id != official.Id && entry.Kind == official.Kind && entry is { Author: null, Store: null }
                                                      && entry.ContentHash == official.ContentHash).ToList();
        if (copies.Count == 0) return;
        if (copies.Any(copy => copy.Favorite) && !official.Favorite) store.SetFavorite(official.Id, true);
        foreach (var copy in copies) store.Remove(copy.Id);
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
            // 읽지 못하면 처음처럼 — 없는 패키지를 설치할 뿐(지운 공식 패키지가 한 번 돌아올 수 있음)
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
}
