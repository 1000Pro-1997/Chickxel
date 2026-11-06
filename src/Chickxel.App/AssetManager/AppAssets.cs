using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>
/// 앱이 쓰는 로컬 에셋 저장소 하나(<c>&lt;설정 폴더&gt;/Chickxel/Assets</c>). 처음 열 때 예전 프리셋 파일을 내 에셋으로 한 번 옮기고(예전 게시 기록은 그 에셋의
/// 상점 연결로 한 번 잇는다), 앱에 든 공식 효과를 백그라운드에서 설치·업데이트한다(<see cref="SyncBundledAsync"/> — 처음 실행과 앱에 든 버전이 더 새로울 때만.
/// 지운 공식 효과는 다시 넣지 않음 — 에셋 관리자의 "기본 효과 되돌리기"만 다시 넣는다).
/// 효과 창, 그라데이션 메뉴·프리셋 창, 에셋 관리자, AI 도구가 같은 저장소와 카탈로그를 쓰고, 저장소를 바꾸면 <see cref="Changed"/>로 목록을 다시 만든다.
/// 저장소를 바꾸는 호출은 UI 스레드에서 한다. 온라인 카탈로그 client(<see cref="Remote"/>)는 저장소와 따로이고 cache만 저장소 폴더에 둔다.
/// 내 에셋(<see cref="IsMine"/>)은 이 컴퓨터에서 만들었거나 작성자 ID가 내 계정 핸들(<see cref="MyHandle"/>)인 것이다.
/// </summary>
public static class AppAssets
{
    /// <summary>커뮤니티 서비스가 내는 온라인 카탈로그 첫 쪽(예전 홈페이지의 정적 <c>catalog/</c>와 같은 모양, 패키지·미리보기는 같은 사이트의 <c>/files/</c>).</summary>
    public static readonly Uri CatalogIndexUrl = new(AppInfo.WebsiteUrl + "api/catalog/" + RemoteCatalogFormat.FirstPageName);

    private static readonly HttpClient RemoteHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static RemoteAssetClient? _remote;
    private static readonly Lock Gate = new();
    private static AssetStore? _store;
    private static string? _notice;
    private static readonly Lock BundledGate = new();
    private static Task<OfficialEffectPackages.SyncResult>? _bundled;

    /// <summary>예전 프리셋 파일이 있던 앱 설정 폴더.</summary>
    public static string SettingsDirectory => Platform.AppFolder;

    public static string Root => Path.Combine(SettingsDirectory, "Assets");

    /// <summary>패키지 검사 설정: 개발 빌드(버전 0.0.0)는 최소 앱 버전을 보지 않는다.</summary>
    public static AssetValidationOptions Options => new(AppVersion: AppInfo.IsDevBuild ? null : AppInfo.Version);

    public static AssetStore Store
    {
        get
        {
            if (_store is { } store) return store;
            lock (Gate)
            {
                if (_store is null)
                {
                    var opened = AssetStore.Open(Root, Options);
                    var migration = AssetMigration.Run(opened, SettingsDirectory);
                    var problems = opened.LoadErrors.ToList();
                    if (migration.Error is { } error)
                        problems.Add(string.Format(CultureInfo.CurrentCulture, UiStrings.Assets_MigrationError, error));
                    _notice = problems.Count > 0 ? string.Join(Environment.NewLine, problems) : null;
                    LinkPublishRecords(opened);
                    _store = opened;
                    // 계정이 바뀌면 핸들을 기억한다(로그아웃한 뒤에도 내가 올린 에셋을 내 것으로 보게)
                    AppCommunity.Client.AccountsChanged += () => Dispatcher.UIThread.Post(() => _ = MyHandle);
                    // 공식 효과: 할 일이 있을 때만(처음 실행·앱 업데이트) 백그라운드에서 — 끝나면 Changed로 효과 탭이 다시 그려진다
                    if (OfficialEffectPackages.NeedsSync(opened)) StartBundledSync();
                    // 설치하면 바로 믿기 전에 받아 둔 에셋의 스크립트도 믿는다(설정 파일을 쓰므로 UI 스레드에서)
                    Dispatcher.UIThread.Post(() => ScriptTrustStore.TrustStored(opened.Catalog.All));
                }
                return _store;
            }
        }
    }

    /// <summary>
    /// 예전 게시 기록(<see cref="AssetPublishSettings.Saved"/>, 최근 것부터)을 그 에셋의 상점 연결로 한 번 잇는다: 기록의 에셋이 아직 이 컴퓨터에서 만든 것
    /// (작성자·상점 연결 없음)이고 그 상점 ID에 연결된 에셋이 없을 때만 — 여러 번 불러도 같다. 그 상점 ID의 에셋이 이미 있고(예전 저장소에서 받아 둔 것을 옮김)
    /// 내용까지 같으면 기록의 에셋은 겹친 사본이라 지운다(즐겨찾기는 받은 쪽으로). 맞지 않는 기록은 건너뛰고, 쓰지 못하면 다음 실행에 다시 한다.
    /// </summary>
    private static void LinkPublishRecords(AssetStore store)
    {
        if (store.LibraryError is not null) return;
        var settings = App.Settings.AssetPublish;
        foreach (var record in settings.Saved.ToList())
        {
            if (!AssetManifest.IsValidId(record.Id) || !AssetManifest.IsValidVersion(record.Version) || !AssetManifest.IsValidName(record.Name)) continue;
            int dot = record.Id.IndexOf('.');
            if (dot <= 0) continue;
            string handle = record.Id[..dot];
            if (!AssetManifest.IsValidSegment(handle)) continue;
            string authorName = AssetManifest.IsValidName(settings.AuthorName) ? settings.AuthorName.Trim() : handle;
            var translations = AssetTranslations.IsValid(record.Translations) ? record.Translations : AssetTranslations.Empty;
            string? description = string.IsNullOrWhiteSpace(record.Summary) || record.Summary.Length > AssetManifest.MaxDescriptionLength ? null : record.Summary;
            foreach (var id in record.Assets)
            {
                if (store.Catalog.Find(id) is not { Author: null, Store: null } entry) continue;
                var linked = store.Catalog.FromPackage(record.Id);
                AssetEntry? same = null;
                string assetId = record.Id + "." + AssetPackageBuilder.Slug(entry.Name, entry.Kind);
                if (linked.Count > 0)
                {
                    same = linked.FirstOrDefault(e => e.Kind == entry.Kind && e.ContentHash == entry.ContentHash);
                    if (same is null) continue;
                }
                else if (!AssetManifest.IsValidAssetId(record.Id, assetId)) continue;
                try
                {
                    if (same is not null)
                    {
                        if (entry.Favorite) store.SetFavorite(same.Id, true);
                        store.Remove(id);
                    }
                    else
                        store.MarkPublished(id, new AssetAuthor(handle, authorName), AssetPublishCheck.License, new AssetStoreLink
                        {
                            PackageId = record.Id, AssetId = assetId, Version = record.Version, ContentHash = entry.ContentHash,
                            Name = record.Name.Trim(), Description = description, Translations = translations, MinAppVersion = "0.0.0",
                        });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return; }
            }
        }
    }

    /// <summary>
    /// 내 계정 핸들: 로그인한 계정(이번 실행에 확인한 정보, 아니면 저장한 계정 정보), 로그아웃이면 마지막으로 알던 핸들(설정에 기억). 모르면 null.
    /// 계정을 알 때마다 설정에 기억한다.
    /// </summary>
    public static string? MyHandle
    {
        get
        {
            var client = AppCommunity.Client;
            if ((client.User?.Handle ?? client.Saved.Current?.User?.Handle) is { Length: > 0 } live)
            {
                RememberHandle(live);
                return live;
            }
            return App.Settings.AssetPublish.LastHandle is { Length: > 0 } last ? last : null;
        }
    }

    private static void RememberHandle(string handle)
    {
        if (App.Settings.AssetPublish.LastHandle == handle) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RememberHandle(handle));
            return;
        }
        App.Settings.AssetPublish.LastHandle = handle;
        App.Settings.Save();
    }

    /// <summary>내 에셋인지(이 컴퓨터에서 만들었거나 작성자 ID가 <see cref="MyHandle"/>) — 내 것은 그 자리에서 고치고, 아니면 고칠 때 사본을 만든다.</summary>
    public static bool IsMine(AssetEntry entry) => entry.IsOwnedBy(MyHandle);

    /// <summary>들어온 길(<see cref="AssetEntry.Source"/>)의 화면 글.</summary>
    public static string SourceText(string source) => source switch
    {
        AssetSources.Local => UiStrings.Assets_SourceLocal,
        AssetSources.File => UiStrings.Assets_SourceFile,
        AssetSources.Remote => UiStrings.Assets_SourceRemote,
        AssetSources.Bundled => UiStrings.Assets_SourceBundled,
        _ => source,
    };

    /// <summary>처음 열 때 시작한 공식 효과 패키지 맞추기(할 일이 없었으면 끝난 작업). 화면 그림 도구가 기다린다.</summary>
    public static Task BundledReady
    {
        get
        {
            _ = Store;
            lock (BundledGate) return _bundled ?? Task.CompletedTask;
        }
    }

    /// <summary>
    /// 공식 효과 패키지를 맞춘다(<see cref="OfficialEffectPackages.Sync"/>): 패키지를 만들고 설치하는 일(처음 실행에 수 초)은 백그라운드에서 같은 폴더의
    /// 다른 저장소 객체로 하고, 끝나면 UI 스레드에서 앱 저장소를 다시 읽는다(<see cref="AssetStore.Refresh"/> → <see cref="Changed"/>).
    /// 앞서 시작한 맞추기가 끝난 뒤에 시작한다(설치 기록 파일을 함께 쓰지 않게).
    /// </summary>
    public static Task<OfficialEffectPackages.SyncResult> SyncBundledAsync(bool restore)
    {
        lock (BundledGate)
        {
            var task = Run(_bundled, restore);
            _bundled = task;
            return task;
        }

        static async Task<OfficialEffectPackages.SyncResult> Run(Task? previous, bool restore)
        {
            // 앞의 맞추기가 실패했어도 기다리기만 한다(실패는 그쪽에서 알림)
            if (previous is not null) await previous.ContinueWith(_ => { }, TaskScheduler.Default).ConfigureAwait(false);
            var result = await Task.Run(() => OfficialEffectPackages.Sync(AssetStore.Open(Root, Options), restore)).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => Store.Refresh());
            return result;
        }
    }

    // 처음 열 때의 맞추기: 실패는 다음 ShowProblems에서 알린다
    private static void StartBundledSync()
        => SyncBundledAsync(restore: false).ContinueWith(task =>
        {
            string? errors = task.IsFaulted ? task.Exception!.GetBaseException().Message
                : task.Result.Errors.Count > 0 ? string.Join(Environment.NewLine, task.Result.Errors.Select(e => $"{e.PackageId}: {e.Error}"))
                : null;
            if (errors is null) return;
            var message = string.Format(CultureInfo.CurrentCulture, UiStrings.Assets_BundledError, errors);
            string? current;
            do current = Volatile.Read(ref _notice);
            while (Interlocked.CompareExchange(ref _notice, current is null ? message : current + Environment.NewLine + message, current) != current);
        }, TaskScheduler.Default);

    public static AssetCatalog Catalog => Store.Catalog;

    /// <summary>온라인 카탈로그 client(요청 시간 한도·재시도는 client 기본값, cache = 저장소의 <c>Cache/</c>).</summary>
    public static RemoteAssetClient Remote
        => _remote ??= new RemoteAssetClient(RemoteHttp, CatalogIndexUrl, Store.CacheDirectory, $"{AppUpdate.UserAgent}/{AppInfo.VersionText}");

    /// <summary>카탈로그가 바뀌었다(가져오기, 저장, 삭제, 즐겨찾기).</summary>
    public static event Action? Changed
    {
        add => Store.Changed += value;
        remove => Store.Changed -= value;
    }

    /// <summary>저장소를 열 때 만난 문제(손상된 상태 파일, migration 실패)를 한 번 알린다.</summary>
    public static void ShowProblems(Visual? owner)
    {
        _ = Store;
        if (Interlocked.Exchange(ref _notice, null) is { } notice) Ui.Warn(owner, notice, UiStrings.Assets_Title);
    }

    /// <summary>
    /// 저장소를 바꾸는 동작을 하고, 실패하면 <paramref name="errorFormat"/>({0} = 까닭)으로 알린다. 성공하면 true.
    /// 패키지 검사 실패는 찾은 문제를 줄마다 보인다.
    /// </summary>
    public static bool Try(Visual? owner, Action action, string errorFormat)
    {
        try
        {
            action();
            return true;
        }
        catch (AssetPackageException ex)
        {
            Ui.Error(owner, string.Format(CultureInfo.CurrentCulture, errorFormat, IssueText(ex.Issues)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            Ui.Error(owner, string.Format(CultureInfo.CurrentCulture, errorFormat, ex.Message));
        }
        return false;
    }

    /// <summary>
    /// 패키지 파일(<c>.chickxel-asset</c>)의 에셋을 하나씩 더한다(<see cref="AssetSources.File"/>). 이미 있는 에셋과 겹치면 하나마다 묻는다
    /// (<see cref="AssetImportPrompt"/>: 바꾸기·건너뛰기·둘 다 두기, "남은 항목에도 적용"이면 이 파일의 나머지에 같은 답).
    /// 들어간 에셋(패키지 차례)을 돌려주고 그 스크립트는 바로 믿는다(<see cref="ScriptTrustStore.TrustInstalled"/>). 실패하면 알리고 null(아무것도 바뀌지 않음).
    /// </summary>
    public static IReadOnlyList<AssetEntry>? InstallFile(Visual owner, string path)
    {
        var ask = Prompter(owner);
        IReadOnlyList<AssetEntry>? installed = null;
        if (!Try(owner, () => installed = Store.Install(path, AssetSources.File, ask), UiStrings.Assets_ImportError) || installed is null) return null;
        ScriptTrustStore.TrustInstalled(installed);
        return installed;
    }

    /// <summary>
    /// 온라인 목록의 패키지를 내려받아(크기·SHA-256 확인) 에셋을 저장소에 넣는다(<see cref="AssetSources.Remote"/>).
    /// 목록과 패키지의 ID·버전을 확인하고, 연결된 에셋의 새 버전은 고친 내용도 묻지 않고 교체한다(다른 내용 겹침은 묻는다).
    /// 실패하면 <paramref name="owner"/>에 알리고 null, 취소하면 아무것도 바꾸지 않고 null. 받은 임시 파일은 지운다.
    /// </summary>
    public static async Task<IReadOnlyList<AssetEntry>?> InstallRemoteAsync(Visual owner, RemoteCatalogEntry entry, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string? file = null;
        try
        {
            file = await Remote.DownloadPackageAsync(entry, progress, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return null;
            var ask = Prompter(owner);
            IReadOnlyList<AssetEntry>? installed = null;
            string path = file;
            if (!Try(owner, () => installed = Store.InstallRemote(path, entry.Id, entry.Version, ask, cancellationToken), UiStrings.Assets_InstallRemoteError) || installed is null) return null;
            ScriptTrustStore.TrustInstalled(installed);
            return installed;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is RemoteAssetException or IOException or UnauthorizedAccessException)
        {
            Ui.Error(owner, string.Format(CultureInfo.CurrentCulture, UiStrings.Assets_DownloadError, ex.Message));
            return null;
        }
        finally
        {
            if (file is not null)
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>파일 하나를 가져오는 동안 겹침마다 묻는 함수("남은 항목에도 적용"을 고르면 그 뒤로는 묻지 않고 같은 답).</summary>
    private static Func<AssetImportItem, ImportChoice> Prompter(Visual owner)
    {
        ImportChoice? rest = null;
        return item =>
        {
            if (rest is { } same) return same;
            var (choice, applyToRest) = AssetImportPrompt.Ask(owner, item);
            if (applyToRest) rest = choice;
            return choice;
        };
    }

    /// <summary>
    /// 효과 프리셋·그라데이션·스크립트 효과를 내 에셋(작성자·상점 연결 없음)으로 복사한다 — 받은 에셋을 고칠 때 쓰는 사본. 원본은 그대로.
    /// 만든(같은 원문의 내 스크립트가 있으면 그) 에셋을 돌려주고, 실패하면 알리고 null.
    /// </summary>
    public static AssetEntry? CopyAsMine(Visual owner, AssetEntry entry)
    {
        AssetEntry? saved = null;
        return Try(owner, () => saved = entry switch
        {
            { Recipe: { } recipe } => Store.SaveRecipe(recipe with { Id = "" }),
            { Gradient: { } gradient } => Store.SaveGradient(null, gradient with { Favorite = false }),
            { Script: { } script } => Store.SaveScript(script.Source, null, script.Scene),
            _ => null,
        }, UiStrings.Assets_ActionError) ? saved : null;
    }

    /// <summary>문제 목록 글(처음 12개, 넘으면 "외 N개").</summary>
    public static string IssueText(IReadOnlyList<AssetIssue> issues)
    {
        var lines = issues.Take(12).Select(issue => "- " + issue.Message).ToList();
        if (issues.Count > 12) lines.Add(string.Format(CultureInfo.CurrentCulture, UiStrings.Assets_MoreIssues, issues.Count - 12));
        return Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}
