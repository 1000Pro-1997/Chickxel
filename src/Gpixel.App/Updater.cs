using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Gpixel.App.Ai;
using Gpixel.Core.IO;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>자동 설치 준비: 내려받을 파일, 체크섬 파일, 바꿀 곳(Windows exe / macOS .app).</summary>
public sealed record InstallPlan(ReleaseInfo Release, ReleaseAsset Asset, ReleaseAsset Checksums, InstallKind Kind, string Target);

/// <summary>검증·압축 해제까지 끝난 새 버전. <see cref="NewApp"/>은 새 Gpixel.exe 또는 Gpixel.app.</summary>
public sealed record StagedUpdate(InstallPlan Plan, string Directory, string NewApp);

/// <summary>내려받기 진행: 단계 글자와 받은/전체 바이트(전체를 모르면 0).</summary>
public readonly record struct UpdateProgress(string Stage, long Done, long Total);

/// <summary>사용자에게 그대로 보여 줄 업데이트 실패 이유.</summary>
public sealed class UpdateException(string message) : Exception(message);

/// <summary>
/// GitHub 릴리스로 업데이트: 시작할 때마다(배포판만, 끌 수 있음 — 새 버전이면 업데이트 창) 또는 메뉴에서 최신 릴리스를 확인하고,
/// 새 버전이면 상태 표시줄·위 막대 단추로 알린다(수동 확인은 바로 업데이트 창).
/// 설치는 이 OS·CPU용 zip을 받아 SHA256SUMS로 검증하고 임시 폴더에 풀어 둔 뒤, 앱이 끝나기를 기다렸다가
/// 바꿔 넣고 다시 실행하는 작은 스크립트(Windows PowerShell / macOS sh)에 맡긴다. 이전 앱은 새 앱이 자리 잡은 뒤에만 지운다.
/// </summary>
public sealed class Updater
{
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(10);
    private static readonly HttpClient SharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    /// <summary>넘겨주기(302)를 따라가지 않는 클라이언트: 최신 릴리스 웹 주소가 넘겨 주는 태그 주소를 읽는다.</summary>
    private static readonly HttpClient NoRedirectHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    /// <summary>임시 폴더 안 준비 폴더 이름 앞부분.</summary>
    private const string StagingPrefix = "gpixel-update-";

    private readonly HttpClient _http, _pageHttp;
    private readonly AppSettings _settings;
    private readonly Window _owner;
    private readonly Action<string> _showMessage;
    private readonly Action<ReleaseInfo?> _availableChanged;
    private readonly Func<bool> _confirmExit;
    private readonly Action _exit;
    private bool _checking;

    /// <param name="availableChanged">알릴 새 버전이 생기거나(릴리스) 없어질 때(null).</param>
    /// <param name="confirmExit">설치하려고 앱을 끝내도 되는지(저장하지 않은 작업 묻기). 계속해도 되면 true.</param>
    /// <param name="exit">설치 스크립트를 띄운 뒤 앱을 끝낸다(다시 묻지 않음).</param>
    /// <param name="http">시험용으로 바꿀 수 있는 HTTP 클라이언트(기본 = 공유 클라이언트).</param>
    public Updater(AppSettings settings, Window owner, Action<string> showMessage, Action<ReleaseInfo?> availableChanged,
        Func<bool> confirmExit, Action exit, HttpClient? http = null)
    {
        _settings = settings;
        _owner = owner;
        _showMessage = showMessage;
        _availableChanged = availableChanged;
        _confirmExit = confirmExit;
        _exit = exit;
        _http = http ?? SharedHttp;
        _pageHttp = http ?? NoRedirectHttp;
    }

    /// <summary>알린 새 버전(없으면 null).</summary>
    public ReleaseInfo? Available { get; private set; }

    /// <summary>지금 자동 확인할지: 설정이 켜져 있고 배포판일 때(시작할 때마다).</summary>
    public bool IsAutoCheckDue => _settings.UpdateCheck && !AppInfo.IsDevBuild;

    /// <summary>창이 뜬 뒤 부른다: 지난 업데이트의 찌꺼기를 치우고, 때가 됐으면 잠시 뒤 조용히 확인한다(시작을 막지 않음).</summary>
    public void OnStartup()
    {
        Task.Run(CleanupPreviousUpdate);
        if (IsAutoCheckDue)
            DispatcherTimer.RunOnce(() => _ = CheckAsync(manual: false), TimeSpan.FromSeconds(3));
    }

    // ───────────── 확인 ─────────────

    /// <summary>최신 릴리스 확인 결과. <see cref="Problem"/>이 있으면 확인하지 못한 것(<see cref="NoReleases"/> = 404).</summary>
    private sealed record LatestResult(ReleaseInfo? Release, string? Problem, bool NoReleases);

    /// <summary>
    /// 최신 릴리스를 확인한다. 새 버전이면(자동 확인은 "다음 업데이트까지 띄우지 않기"로 고른 버전 제외) 위 막대 단추를 보이고 업데이트 창을 띄운다.
    /// 자동 확인은 그 밖의 결과(최신·오류)를 조용히 넘기고, 수동 확인은 알린다.
    /// </summary>
    public async Task CheckAsync(bool manual)
    {
        if (_checking)
        {
            if (manual) _showMessage(UiStrings.S_Updater_CheckAsync_33CBE4EAD6);
            return;
        }
        _checking = true;
        try
        {
            if (manual) _showMessage(UiStrings.S_Updater_CheckAsync_C61279BB18);
            var result = await FetchLatestAsync();
            if (result.Problem is { } problem)
            {
                if (!manual) return;
                _showMessage("");
                if (result.NoReleases) Ui.Info(_owner, problem, UiStrings.S_Updater_CheckAsync_709BB6FDD1);
                else Ui.Warn(_owner, problem, UiStrings.S_Updater_CheckAsync_709BB6FDD1);
                return;
            }
            var release = result.Release;
            if (!AppUpdate.IsNewer(AppInfo.Version, release))
            {
                SetAvailable(null);
                if (!manual) return;
                _showMessage("");
                Ui.Info(_owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_CheckAsync_B80A4C36E4,AppInfo.VersionText)
                    + (release is null ? "" : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_CheckAsync_48998BE659,release.Version)), UiStrings.S_Updater_CheckAsync_709BB6FDD1);
                return;
            }
            if (!manual && IsSkipped(release!))
            {
                SetAvailable(null);
                return;
            }
            SetAvailable(release);
            _showMessage(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_CheckAsync_CFB3246BF9,release!.Version));
            // 다른 창이 떠 있으면(시작할 때 연 대화상자 등) 겹치지 않게, 위 막대 단추로만 알린다.
            if (manual || !_owner.OwnedWindows.Any()) ShowUpdateDialog(release);
        }
        finally
        {
            _checking = false;
        }
    }

    private bool IsSkipped(ReleaseInfo release)
        => SemVersion.TryParse(_settings.SkippedVersion, out var skipped) && skipped == release.Version;

    private void SetAvailable(ReleaseInfo? release)
    {
        Available = release;
        _availableChanged(release);
    }

    /// <summary>
    /// 최신 릴리스를 확인한다(10초 제한). 먼저 웹 주소 <c>/releases/latest</c>가 넘겨 주는 태그로 버전을 알아내고(요청 한도 없음),
    /// 새 버전이면 바뀐 점(릴리스 설명)을 API로 한 번 더 묻는다 — API가 한도에 걸려도 버전 확인·업데이트는 된다.
    /// </summary>
    private async Task<LatestResult> FetchLatestAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(ApiTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Head, AppUpdate.LatestReleasePageUrl);
            AddHeaders(request);
            using var response = await _pageHttp.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new LatestResult(null, NoReleaseText(UiStrings.S_Updater_FetchLatestAsync_057AEC6641), NoReleases: true);
            if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is not { } location)
                return new LatestResult(null, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_FetchLatestAsync_877F1750C7,(int)response.StatusCode,response.ReasonPhrase), false);
            if (!location.IsAbsoluteUri) location = new Uri(AppUpdate.LatestReleasePageUrl, location);
            if (AppUpdate.ReleaseFromLatestRedirect(location) is not { } release)
                return location.AbsolutePath.TrimEnd('/').EndsWith("/releases", StringComparison.Ordinal)
                    ? new LatestResult(null, NoReleaseText(UiStrings.S_Updater_FetchLatestAsync_2FB5F659ED), NoReleases: true)
                    : new LatestResult(null, UiStrings.S_Updater_FetchLatestAsync_05702F1BD5, false);
            if (AppUpdate.IsNewer(AppInfo.Version, release)) release = await WithNotesAsync(release);
            return new LatestResult(release, null, false);
        }
        catch (OperationCanceledException)
        {
            return new LatestResult(null, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_FetchLatestAsync_4E33D29A1C,ApiTimeout.TotalSeconds), false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new LatestResult(null, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_FetchLatestAsync_92FDE0EACD,ex.Message), false);
        }
    }

    private static string NoReleaseText(string why)
        => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_NoReleaseText_EB9E89C82C,why,AppInfo.VersionText,AppUpdate.ReleasesUrl);

    /// <summary>
    /// 바뀐 점을 붙인다. 먼저 릴리스에 함께 올린 바뀐 점 파일(웹 내려받기 주소라 API 한도 없음)에서 지금 버전 다음 ~ 새 버전 절을 고르고
    /// (언어는 <see cref="ChangelogNotesAsync"/>), 없으면 API의 릴리스 설명을 쓴다. 둘 다 못 읽으면 웹 주소로 얻은 정보를 그대로(업데이트 창이 버전별 링크를 보여 줌).
    /// </summary>
    private async Task<ReleaseInfo> WithNotesAsync(ReleaseInfo fromPage)
    {
        if (await ChangelogNotesAsync(fromPage) is { Length: > 0 } notes) return fromPage with { Notes = notes };
        try
        {
            using var timeout = new CancellationTokenSource(ApiTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, AppUpdate.LatestReleaseApiUrl);
            AddHeaders(request);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return fromPage;
            var full = AppUpdate.ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token));
            return full is not null && full.Version == fromPage.Version ? full : fromPage;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException or FormatException)
        {
            return fromPage;
        }
    }

    /// <summary>
    /// 새 버전 릴리스의 바뀐 점 파일에서 지금 버전보다 높은 절들(없거나 못 읽으면 null). 언어 규칙은 "화면 언어가 한국어(ko)인가" 하나:
    /// 한국어 화면은 <c>CHANGELOG.ko.md</c>를 쓰고(그 파일이 없는 예전 릴리스면 그때 한국어였던 <c>CHANGELOG.md</c>), 그 밖의 모든 화면 언어
    /// (영어, CSV로 더한 일본어 등)는 영어 <c>CHANGELOG.md</c>를 쓰며 영어 절이 없는 버전(v1.14.22 이하의 옛 버전)만 <c>CHANGELOG.ko.md</c>의 한국어 절로 채운다.
    /// </summary>
    private async Task<string?> ChangelogNotesAsync(ReleaseInfo release)
    {
        if (UiLanguage.Current == UiLanguage.Korean)
            return (await ChangelogFileAsync(release, ChangelogNotes.KoreanAssetName) ?? await ChangelogFileAsync(release, ChangelogNotes.EnglishAssetName)) is { } korean
                ? ChangelogNotes.Between(korean, AppInfo.Version, release.Version)
                : null;
        var englishTask = ChangelogFileAsync(release, ChangelogNotes.EnglishAssetName);
        var koreanTask = ChangelogFileAsync(release, ChangelogNotes.KoreanAssetName);
        string? english = await englishTask, fallback = await koreanTask;
        if (english is null && fallback is null) return null;
        return ChangelogNotes.Between(english, AppInfo.Version, release.Version, fallback);
    }

    /// <summary>새 버전 릴리스에 함께 올린 파일 <paramref name="assetName"/>의 글(없거나 못 읽으면 null).</summary>
    private async Task<string?> ChangelogFileAsync(ReleaseInfo release, string assetName)
    {
        try
        {
            using var timeout = new CancellationTokenSource(ApiTimeout);
            var url = new Uri($"{AppUpdate.ReleasesUrl}/download/{Uri.EscapeDataString(release.Tag)}/{assetName}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddHeaders(request);
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException)
        {
            return null;
        }
    }

    private static void AddHeaders(HttpRequestMessage request)
        => request.Headers.UserAgent.Add(new ProductInfoHeaderValue(AppUpdate.UserAgent, AppInfo.VersionText));

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    // ───────────── 업데이트 창 ─────────────

    /// <summary>업데이트 창을 띄우고 고른 대로 한다(다음 업데이트까지 띄우지 않기 기억·설치 시작).</summary>
    public void ShowUpdateDialog(ReleaseInfo release)
    {
        var (plan, reason) = PlanInstall(release);
        var dlg = new UpdateDialog(release, plan, reason, this);
        dlg.ShowDialog(_owner);
        switch (dlg.Choice)
        {
            case UpdateChoice.Skip:
                _settings.SkippedVersion = release.Version.ToString();
                _settings.Save();
                SetAvailable(null);
                _showMessage(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_ShowUpdateDialog_A63ECF4271,release.Version));
                break;
            case UpdateChoice.Install when dlg.Staged is { } staged:
                Install(staged);
                break;
        }
    }

    /// <summary>
    /// 이 설치를 자동으로 바꿀 수 있는지 본다. 바꿀 수 있으면 계획, 아니면 이유(릴리스 페이지에서 받으라는 안내).
    /// </summary>
    public static (InstallPlan? Plan, string? Reason) PlanInstall(ReleaseInfo release)
    {
        string manual = UiStrings.Updater_ManualInstall;
        if (AppInfo.InstallKind == InstallKind.Development || AppInfo.InstallPath is not { } target)
            return (null, UiStrings.S_Updater_PlanInstall_3ABC93510F + manual);
        if (AppInfo.IsDevBuild)
            return (null, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_PlanInstall_FA52471F79,AppInfo.VersionText) + manual);
        var os = OperatingSystem.IsWindows() ? UpdateOs.Windows : OperatingSystem.IsMacOS() ? UpdateOs.MacOS : UpdateOs.Other;
        var arch = RuntimeInformation.OSArchitecture;
        if (AppUpdate.AssetName(os, arch) is not { } assetName)
            return (null, UiStrings.S_Updater_PlanInstall_AF27F58056 + manual);
        if (release.Asset(assetName) is not { } asset)
            return (null, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_PlanInstall_EE7CBEAB5D,assetName) + manual);
        if (release.Asset(AppUpdate.ChecksumsAssetName) is not { } sums)
            return (null, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_PlanInstall_583D1BF21E,AppUpdate.ChecksumsAssetName) + manual);
        if (target.Contains("/AppTranslocation/", StringComparison.Ordinal))
            return (null, UiStrings.S_Updater_PlanInstall_4AA3FC6149);
        var dirs = AppInfo.InstallKind == InstallKind.MacBundle
            ? new[] { Path.GetDirectoryName(target)!, Path.Combine(target, "Contents", "MacOS") }
            : new[] { Path.GetDirectoryName(target)! };
        if (dirs.FirstOrDefault(d => !CanWrite(d)) is { } locked)
            return (null, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_PlanInstall_7002837ACB,locked) + manual);
        return (new InstallPlan(release, asset, sums, AppInfo.InstallKind, target), null);
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            using (File.Create(Path.Combine(dir, $".gpixel-write-test-{Guid.NewGuid():N}"), 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ───────────── 내려받기·검증·풀기 ─────────────

    /// <summary>
    /// 체크섬 파일과 zip을 임시 폴더에 받아 SHA-256을 확인하고 푼다. 실패·취소하면 임시 폴더를 지우고
    /// <see cref="UpdateException"/>(이유) / <see cref="OperationCanceledException"/> / <see cref="HttpRequestException"/> / <see cref="IOException"/>.
    /// </summary>
    public async Task<StagedUpdate> DownloadAndStageAsync(InstallPlan plan, IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{StagingPrefix}{plan.Release.Version}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(dir);
        try
        {
            progress.Report(new UpdateProgress(UiStrings.S_Updater_DownloadAndStageAsync_EA757C7E25, 0, 0));
            string sums;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ApiTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, plan.Checksums.DownloadUrl);
                AddHeaders(request);
                using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                sums = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            }
            if (!AppUpdate.ParseChecksums(sums).TryGetValue(plan.Asset.Name, out var expected))
                throw new UpdateException(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_DownloadAndStageAsync_1F7AC300ED,plan.Asset.Name));

            var zip = Path.Combine(dir, plan.Asset.Name);
            await DownloadFileAsync(plan.Asset, zip, progress, cancellationToken).ConfigureAwait(false);

            progress.Report(new UpdateProgress(UiStrings.S_Updater_DownloadAndStageAsync_DEB5CB9937, 0, 0));
            string actual;
            await using (var fs = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, cancellationToken).ConfigureAwait(false));
            if (actual != expected)
                throw new UpdateException(UiStrings.S_Updater_DownloadAndStageAsync_850D382F27);

            progress.Report(new UpdateProgress(UiStrings.S_Updater_DownloadAndStageAsync_8CBFE6F9BA, 0, 0));
            var extracted = Path.Combine(dir, "new");
            await ExtractAsync(zip, extracted, cancellationToken).ConfigureAwait(false);
            File.Delete(zip);
            string? newApp;
            if (plan.Kind == InstallKind.WindowsExe)
                newApp = Directory.EnumerateFiles(extracted, "Gpixel.exe", SearchOption.AllDirectories).FirstOrDefault();
            else
            {
                var app = Path.Combine(extracted, "Gpixel.app");
                bool complete = File.Exists(Path.Combine(app, "Contents", "Info.plist")) && File.Exists(Path.Combine(app, "Contents", "MacOS", "Gpixel.App"));
                newApp = complete ? app : null;
            }
            if (newApp is null) throw new UpdateException(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_DownloadAndStageAsync_26908971A2,plan.Asset.Name));
            return new StagedUpdate(plan, dir, newApp);
        }
        catch
        {
            TryDeleteDirectory(dir);
            throw;
        }
    }

    private async Task DownloadFileAsync(ReleaseAsset asset, string path, IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        string stage = UiStrings.Updater_Downloading;
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        AddHeaders(request);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? asset.Size;
        progress.Report(new UpdateProgress(stage, 0, total));
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        long done = 0;
        var clock = Stopwatch.StartNew();
        int n;
        while ((n = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
            done += n;
            if (clock.ElapsedMilliseconds >= 100)
            {
                clock.Restart();
                progress.Report(new UpdateProgress(stage, done, total));
            }
        }
        progress.Report(new UpdateProgress(stage, done, total));
    }

    /// <summary>zip 풀기. macOS는 ditto로 푼다(.app의 실행 권한·서명 그대로).</summary>
    private static async Task ExtractAsync(string zip, string destination, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, destination), cancellationToken).ConfigureAwait(false);
            return;
        }
        Directory.CreateDirectory(destination);
        var psi = new ProcessStartInfo("/usr/bin/ditto") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in new[] { "-x", "-k", zip, destination }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new UpdateException(UiStrings.S_Updater_ExtractAsync_4841CB6A40);
        var error = p.StandardError.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (p.ExitCode != 0) throw new UpdateException(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_ExtractAsync_A83ED14485,(await error.ConfigureAwait(false)).Trim()));
    }

    // ───────────── 설치 ─────────────

    /// <summary>저장하지 않은 작업을 물은 뒤 설치 스크립트를 띄우고 앱을 끝낸다. 취소하거나 실패하면 지금 앱은 그대로.</summary>
    private void Install(StagedUpdate staged)
    {
        if (!_confirmExit())
        {
            TryDeleteDirectory(staged.Directory);
            _showMessage(UiStrings.S_Updater_Install_D174C68643);
            return;
        }
        try
        {
            LaunchInstaller(staged, Environment.ProcessId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            TryDeleteDirectory(staged.Directory);
            Ui.Error(_owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_Install_5DA8D5A17A,ex.Message));
            return;
        }
        _exit();
    }

    /// <summary>설치 기록 파일(임시 폴더, 스크립트가 덧붙여 씀).</summary>
    public static string LogPath => Path.Combine(Path.GetTempPath(), "gpixel-update.log");

    /// <summary>설치 스크립트를 준비 폴더에 쓰고 띄운다. 스크립트는 <paramref name="pid"/>가 끝나기를 기다린다.</summary>
    private static void LaunchInstaller(StagedUpdate staged, int pid)
    {
        ProcessStartInfo psi;
        if (staged.Plan.Kind == InstallKind.WindowsExe)
        {
            var script = Path.Combine(staged.Directory, "install.ps1");
            // Windows PowerShell 5.1은 BOM이 없으면 시스템 코드 페이지로 읽는다(한글 경로·글자가 깨짐).
            File.WriteAllText(script, BuildWindowsScript(pid, staged.Plan.Target, staged.NewApp, staged.Directory, LogPath), new UTF8Encoding(true));
            var ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            psi = new ProcessStartInfo(File.Exists(ps) ? ps : "powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script }) psi.ArgumentList.Add(a);
        }
        else
        {
            var script = Path.Combine(staged.Directory, "install.sh");
            File.WriteAllText(script, BuildMacScript(pid, staged.Plan.Target, staged.NewApp, staged.Directory, LogPath), new UTF8Encoding(false));
            psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add(script);
        }
        using var p = Process.Start(psi) ?? throw new InvalidOperationException(UiStrings.S_Updater_LaunchInstaller_C514D8BB78);
    }

    private static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    private static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Windows 설치 스크립트(PowerShell): 앱이 끝나기를 기다렸다가(최대 2분) exe를 .old로 옮기고 새 exe를 복사해 실행한 뒤 .old를 지운다.
    /// 복사가 실패하면 .old를 되돌리고 이전 버전을 다시 실행한다.
    /// </summary>
    public static string BuildWindowsScript(int pid, string targetExe, string newExe, string stagingDir, string logPath) => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_BuildWindowsScript_D0380BD988,pid,PsQuote(targetExe),PsQuote(newExe),PsQuote(stagingDir),PsQuote(logPath));

    /// <summary>
    /// macOS 설치 스크립트(sh): 앱이 끝나기를 기다렸다가(최대 2분) .app을 .old로 옮기고 새 .app을 ditto로 복사,
    /// 격리 표시(com.apple.quarantine)를 지우고 open으로 다시 실행한 뒤 .old를 지운다. 복사가 실패하면 .old를 되돌리고 이전 버전을 연다.
    /// </summary>
    public static string BuildMacScript(int pid, string targetApp, string newApp, string stagingDir, string logPath) => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_BuildMacScript_6FF460617F,pid,ShQuote(targetApp),ShQuote(newApp),ShQuote(stagingDir),ShQuote(logPath));

    /// <summary>지난 업데이트가 남긴 것(이전 exe/.app의 .old, 하루 넘은 준비 폴더)을 지운다. 실패는 무시.</summary>
    private static void CleanupPreviousUpdate()
    {
        if (AppInfo.InstallPath is { } target)
        {
            try
            {
                var old = target + ".old";
                if (File.Exists(old)) File.Delete(old);
                else if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Path.GetTempPath(), StagingPrefix + "*"))
                if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1)) TryDeleteDirectory(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ───────────── 링크 ─────────────

    /// <summary>주소를 기본 브라우저로 연다. 실패하면 오류 상자.</summary>
    public static void OpenUrl(Visual? owner, string url)
    {
        try
        {
            AiCli.OpenUrl(url);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Ui.Error(owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_Updater_OpenUrl_3535AE6451,url,ex.Message));
        }
    }
}
