using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>
/// 앱이 쓰는 로컬 에셋 저장소 하나(<c>&lt;설정 폴더&gt;/Chickxel/Assets</c>). 처음 열 때 예전 프리셋 파일을 개인 에셋으로 한 번 옮기고,
/// 앱에 든 공식 효과 패키지를 백그라운드에서 설치·업데이트한다(<see cref="SyncBundledAsync"/> — 처음 실행과 앱에 든 버전이 더 새로울 때만 패키지를 만든다.
/// 끈 패키지는 켜짐 그대로, 지운 패키지는 다시 설치하지 않음 — 에셋 관리자의 "기본 효과 되돌리기"만 다시 넣는다).
/// 효과 창, 그라데이션 메뉴·프리셋 창, 에셋 관리자, AI 도구가 같은 저장소와 카탈로그를 쓰고, 저장소를 바꾸면 <see cref="Changed"/>로 목록을 다시 만든다.
/// 저장소를 바꾸는 호출은 UI 스레드에서 한다. 온라인 카탈로그 client(<see cref="Remote"/>)는 저장소와 따로이고 cache만 저장소 폴더에 둔다.
/// </summary>
public static class AppAssets
{
    /// <summary>원격에서 설치한 패키지의 출처 값(installed.json).</summary>
    public const string RemoteSource = "remote";

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
                    _store = opened;
                    // 공식 효과 패키지: 할 일이 있을 때만(처음 실행·앱 업데이트) 백그라운드에서 — 끝나면 Changed로 효과 탭이 다시 그려진다
                    if (OfficialEffectPackages.NeedsSync(opened)) StartBundledSync();
                }
                return _store;
            }
        }
    }

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

    /// <summary>카탈로그가 바뀌었다(설치, 켜기·끄기, 삭제, 개인 에셋 저장).</summary>
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

    /// <summary>문제 목록 글(처음 12개, 넘으면 "외 N개").</summary>
    public static string IssueText(IReadOnlyList<AssetIssue> issues)
    {
        var lines = issues.Take(12).Select(issue => "- " + issue.Message).ToList();
        if (issues.Count > 12) lines.Add(string.Format(CultureInfo.CurrentCulture, UiStrings.Assets_MoreIssues, issues.Count - 12));
        return Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}
