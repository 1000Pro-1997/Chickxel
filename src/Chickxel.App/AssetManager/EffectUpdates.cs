using Chickxel.Core.Assets;
using Chickxel.App.Localization;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>
/// 효과 창의 효과 업데이트: 상점에서 받은 효과 에셋의 새 버전 찾기(<see cref="AssetUpdates"/>)와, 목록에 넣은 스크립트 효과가 어느 상점 스크립트 에셋에서 왔는지·
/// 더 새 원문이 있는지 찾기(<see cref="For"/>). 온라인 목록은 마지막으로 받은 것(처음엔 저장소 cache)을 앱을 켜 둔 동안 기억한다(<see cref="FetchAsync"/>가 바꿈).
/// </summary>
internal static class EffectUpdates
{
    private static RemoteCatalogSnapshot? s_snapshot;

    /// <summary>넣은 효과의 업데이트: 원본 에셋과, 그 에셋을 먼저 바꿀 온라인 새 버전(에셋이 이미 새 원문이면 null).</summary>
    public sealed record EffectUpdate(AssetEntry Asset, RemoteCatalogEntry? Remote);

    /// <summary>알고 있는 온라인 목록(아직 받지 않았으면 저장소 cache, 그것도 없으면 빈 목록).</summary>
    public static IReadOnlyList<RemoteCatalogEntry> Packages => (s_snapshot ??= AppAssets.Remote.LoadCache() ?? RemoteCatalogSnapshot.Empty(null)).Packages;

    /// <summary>온라인 목록을 새로 받아 기억한다(받지 못하면 cache 목록과 <see cref="RemoteCatalogSnapshot.Error"/>). 부르는 쪽의 취소만 예외다.</summary>
    public static async Task<RemoteCatalogSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        var snapshot = await AppAssets.Remote.FetchCatalogAsync(cancellationToken);
        s_snapshot = snapshot;
        return snapshot;
    }

    /// <summary>상점에서 받은 효과 에셋(스크립트 효과·효과 프리셋) 중 온라인 목록에 더 새 버전이 있는 것, 상점 패키지마다 하나.</summary>
    public static IReadOnlyList<RemoteUpdate> AssetUpdates()
        => RemoteAssetClient.FindUpdates(AppAssets.Catalog, Packages)
            .Where(update => update.Installed.Kind is AssetKinds.EffectScript or AssetKinds.EffectRecipe).ToList();

    /// <summary>이 앱 버전에서 설치할 수 없는 새 버전인지(필요한 앱 버전이 더 높음 등).</summary>
    public static bool Installable(RemoteCatalogEntry remote) => remote.IncompatibleReason(AppAssets.Options.AppVersion) is null;

    /// <summary>
    /// 원문 <paramref name="hash"/>로 넣은 스크립트 효과의 업데이트: 원본 상점 에셋(<see cref="SourceAsset"/>)에 설치할 수 있는 온라인 새 버전이 있거나,
    /// 에셋의 원문이 이미 넣은 원문과 다르면(에셋을 먼저 업데이트함) 그 업데이트. 없으면 null.
    /// </summary>
    public static EffectUpdate? For(string hash)
    {
        if (SourceAsset(hash) is not { Script: { } script, Store: { } store } asset) return null;
        var remote = Packages.FirstOrDefault(p => p.Id == store.PackageId) is { } newer && newer.SemanticVersion > store.SemanticVersion && Installable(newer)
            ? newer : null;
        return remote is not null || script.Hash != hash ? new EffectUpdate(asset, remote) : null;
    }

    /// <summary>저장할 때 프로젝트에 적을 상점 패키지 ID: 같은 원문의 상점 스크립트 에셋(공식 효과는 앱에 들어 있어 뺌). 없으면 null. 아무 스레드에서나 불림.</summary>
    public static string? StorePackageOf(string hash)
        => OfficialEffects.IsOfficialHash(hash) ? null
            : AppAssets.Catalog.Available(AssetKinds.EffectScript).FirstOrDefault(entry => entry.Script!.Hash == hash && entry.Store is not null)?.Store!.PackageId;

    /// <summary>
    /// 라이브러리에 없는 효과를 받을 상점 패키지 ID: 공식 효과가 아니고, 같은 원문의 에셋도 원본 상점 에셋(<see cref="SourceAsset"/>)도 라이브러리에 없으며,
    /// 프로젝트가 그 원문이 온 패키지를 알 때(<see cref="Chickxel.Core.Model.SpriteProject.ScriptStores"/>). 아니면 null.
    /// </summary>
    public static string? MissingStorePackage(Chickxel.Core.Model.SpriteProject project, string hash)
        => OfficialEffects.IsOfficialHash(hash) || AppAssets.Catalog.Available(AssetKinds.EffectScript).Any(entry => entry.Script!.Hash == hash)
           || SourceAsset(hash) is not null
            ? null
            : project.ScriptStores.GetValueOrDefault(hash);

    /// <summary>
    /// 상점 패키지 <paramref name="packageId"/>의 최신 버전을 받아 라이브러리에 넣는다(온라인 목록을 새로 받음). 알릴 글을 돌려준다 —
    /// 설치 실패는 <see cref="AppAssets.InstallRemoteAsync"/>가 이미 알렸으므로 null.
    /// </summary>
    public static async Task<string?> DownloadAsync(Visual owner, string packageId)
    {
        var snapshot = await FetchAsync(CancellationToken.None);
        if (snapshot.Find(packageId) is not { } remote)
            return snapshot.Error is { } error ? F(UiStrings.EffectUpdates_FetchFailed, error.Message) : UiStrings.StoreDownload_NotFound;
        if (remote.IncompatibleReason(AppAssets.Options.AppVersion) is { } reason) return reason;
        return await AppAssets.InstallRemoteAsync(owner, remote, null, CancellationToken.None) is not null
            ? F(UiStrings.StoreDownload_Done, remote.DisplayName, remote.Version)
            : null;
    }

    private static string F(string format, params object[] args) => string.Format(System.Globalization.CultureInfo.CurrentCulture, format, args);

    /// <summary>
    /// 원문 <paramref name="hash"/>가 온 상점 스크립트 에셋: 같은 원문의 에셋(상점 연결이 없는 내 에셋뿐이면 null), 예전 앱이 넣은 공식 효과 원문이면 그 공식 효과,
    /// 아니면 선언의 이름·작성자가 같은 상점 스크립트 에셋이 하나뿐일 때 그것(에셋을 업데이트해 넣은 원문이 예전 것이 된 효과).
    /// </summary>
    public static AssetEntry? SourceAsset(string hash)
    {
        var scripts = AppAssets.Catalog.Available(AssetKinds.EffectScript);
        var same = scripts.Where(entry => entry.Script!.Hash == hash).ToList();
        if (same.Count > 0) return same.FirstOrDefault(entry => entry.Store is not null);
        var linked = scripts.Where(entry => entry.Store is not null).ToList();
        if (OfficialEffects.IdOfShippedHash(hash) is { } official) return linked.FirstOrDefault(entry => AssetCatalog.OfficialIdOf(entry) == official);
        if (ScriptLibrary.InfoOf(hash) is not { Name: { } declared } info || declared.Trim() is not { Length: > 0 } name) return null;
        string author = info.Author?.Trim() ?? "";
        var named = linked.Where(entry => entry.Script!.Name == name && (entry.Script.Author?.Trim() ?? "") == author).Take(2).ToList();
        return named.Count == 1 ? named[0] : null;
    }
}
