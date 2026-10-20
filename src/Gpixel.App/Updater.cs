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
            if (manual) _showMessage("업데이트를 확인하는 중입니다…");
            return;
        }
        _checking = true;
        try
        {
            if (manual) _showMessage("업데이트 확인 중…");
            var result = await FetchLatestAsync();
            if (result.Problem is { } problem)
            {
                if (!manual) return;
                _showMessage("");
                if (result.NoReleases) Ui.Info(_owner, problem, "업데이트 확인");
                else Ui.Warn(_owner, problem, "업데이트 확인");
                return;
            }
            var release = result.Release;
            if (!AppUpdate.IsNewer(AppInfo.Version, release))
            {
                SetAvailable(null);
                if (!manual) return;
                _showMessage("");
                Ui.Info(_owner, $"최신 버전을 쓰고 있습니다.\n\n지금 버전: {AppInfo.VersionText}"
                    + (release is null ? "" : $"\n최신 릴리스: {release.Version}"), "업데이트 확인");
                return;
            }
            if (!manual && IsSkipped(release!))
            {
                SetAvailable(null);
                return;
            }
            SetAvailable(release);
            _showMessage($"새 버전이 나왔습니다: Gpixel {release!.Version}");
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
                return new LatestResult(null, NoReleaseText("저장소를 찾지 못했습니다(비공개이거나 주소가 바뀌었을 수 있습니다)."), NoReleases: true);
            if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is not { } location)
                return new LatestResult(null, $"GitHub가 예상과 다르게 답했습니다({(int)response.StatusCode} {response.ReasonPhrase}). 잠시 뒤 다시 확인하세요.", false);
            if (!location.IsAbsoluteUri) location = new Uri(AppUpdate.LatestReleasePageUrl, location);
            if (AppUpdate.ReleaseFromLatestRedirect(location) is not { } release)
                return location.AbsolutePath.TrimEnd('/').EndsWith("/releases", StringComparison.Ordinal)
                    ? new LatestResult(null, NoReleaseText("아직 공개된 릴리스가 없습니다."), NoReleases: true)
                    : new LatestResult(null, "최신 릴리스의 태그가 버전 형식(vX.Y.Z)이 아니어서 비교할 수 없습니다.", false);
            if (AppUpdate.IsNewer(AppInfo.Version, release)) release = await WithNotesAsync(release);
            return new LatestResult(release, null, false);
        }
        catch (OperationCanceledException)
        {
            return new LatestResult(null, $"GitHub가 {ApiTimeout.TotalSeconds:0}초 안에 응답하지 않았습니다. 인터넷 연결을 확인하고 다시 시도하세요.", false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new LatestResult(null, $"GitHub에 연결하지 못했습니다. 인터넷 연결을 확인하세요.\n({ex.Message})", false);
        }
    }

    private static string NoReleaseText(string why)
        => $"업데이트 정보를 찾지 못했습니다. {why}\n\n지금 버전: {AppInfo.VersionText}\n릴리스 페이지: {AppUpdate.ReleasesUrl}";

    /// <summary>
    /// API로 같은 태그의 릴리스 설명·공개일·실제 파일 목록을 얻어 붙인다. 한도 초과·오류·태그가 다르면 웹 주소로 얻은 정보를 그대로 쓴다.
    /// </summary>
    private async Task<ReleaseInfo> WithNotesAsync(ReleaseInfo fromPage)
    {
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
                _showMessage($"Gpixel {release.Version}은(는) 다시 띄우지 않습니다(더 새 버전이 나오면 알림). 도움말 > 업데이트 확인으로 언제든 볼 수 있습니다.");
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
        const string manual = "\n릴리스 페이지에서 받아 직접 바꿔 주세요.";
        if (AppInfo.InstallKind == InstallKind.Development || AppInfo.InstallPath is not { } target)
            return (null, "소스에서 실행한 개발 빌드는 자동으로 설치할 수 없습니다." + manual);
        if (AppInfo.IsDevBuild)
            return (null, $"시험 빌드({AppInfo.VersionText})는 자동으로 설치할 수 없습니다." + manual);
        var os = OperatingSystem.IsWindows() ? UpdateOs.Windows : OperatingSystem.IsMacOS() ? UpdateOs.MacOS : UpdateOs.Other;
        var arch = RuntimeInformation.OSArchitecture;
        if (AppUpdate.AssetName(os, arch) is not { } assetName)
            return (null, "이 운영체제·CPU는 자동 설치를 지원하지 않습니다." + manual);
        if (release.Asset(assetName) is not { } asset)
            return (null, $"이 릴리스에는 이 컴퓨터용 파일({assetName})이 없습니다." + manual);
        if (release.Asset(AppUpdate.ChecksumsAssetName) is not { } sums)
            return (null, $"이 릴리스에는 체크섬 파일({AppUpdate.ChecksumsAssetName})이 없어 내려받은 파일을 검증할 수 없으므로 자동으로 설치하지 않습니다." + manual);
        if (target.Contains("/AppTranslocation/", StringComparison.Ordinal))
            return (null, "macOS가 앱을 임시 위치에서 실행하고 있습니다(App Translocation).\nGpixel.app을 응용 프로그램 폴더 등으로 옮긴 뒤 다시 실행하면 자동 업데이트를 쓸 수 있습니다.");
        var dirs = AppInfo.InstallKind == InstallKind.MacBundle
            ? new[] { Path.GetDirectoryName(target)!, Path.Combine(target, "Contents", "MacOS") }
            : new[] { Path.GetDirectoryName(target)! };
        if (dirs.FirstOrDefault(d => !CanWrite(d)) is { } locked)
            return (null, $"설치 폴더에 쓸 권한이 없어 자동으로 바꿀 수 없습니다.\n{locked}" + manual);
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
            progress.Report(new UpdateProgress("체크섬 받는 중…", 0, 0));
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
                throw new UpdateException($"체크섬 파일에 {plan.Asset.Name}의 값이 없어 검증할 수 없습니다.");

            var zip = Path.Combine(dir, plan.Asset.Name);
            await DownloadFileAsync(plan.Asset, zip, progress, cancellationToken).ConfigureAwait(false);

            progress.Report(new UpdateProgress("검증하는 중…", 0, 0));
            string actual;
            await using (var fs = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, cancellationToken).ConfigureAwait(false));
            if (actual != expected)
                throw new UpdateException("내려받은 파일의 SHA-256이 체크섬과 다릅니다. 파일이 손상되었거나 바뀌었을 수 있어 설치하지 않습니다.");

            progress.Report(new UpdateProgress("압축 푸는 중…", 0, 0));
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
            if (newApp is null) throw new UpdateException($"{plan.Asset.Name} 안에서 새 앱을 찾지 못했습니다.");
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
        const string stage = "내려받는 중…";
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
        using var p = Process.Start(psi) ?? throw new UpdateException("ditto를 실행하지 못했습니다.");
        var error = p.StandardError.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (p.ExitCode != 0) throw new UpdateException($"압축을 풀지 못했습니다.\n{(await error.ConfigureAwait(false)).Trim()}");
    }

    // ───────────── 설치 ─────────────

    /// <summary>저장하지 않은 작업을 물은 뒤 설치 스크립트를 띄우고 앱을 끝낸다. 취소하거나 실패하면 지금 앱은 그대로.</summary>
    private void Install(StagedUpdate staged)
    {
        if (!_confirmExit())
        {
            TryDeleteDirectory(staged.Directory);
            _showMessage("업데이트를 취소했습니다.");
            return;
        }
        try
        {
            LaunchInstaller(staged, Environment.ProcessId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            TryDeleteDirectory(staged.Directory);
            Ui.Error(_owner, $"업데이트를 설치하지 못했습니다. 지금 버전은 그대로입니다.\n{ex.Message}");
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
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("설치 스크립트를 실행하지 못했습니다.");
    }

    private static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    private static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Windows 설치 스크립트(PowerShell): 앱이 끝나기를 기다렸다가(최대 2분) exe를 .old로 옮기고 새 exe를 복사해 실행한 뒤 .old를 지운다.
    /// 복사가 실패하면 .old를 되돌리고 이전 버전을 다시 실행한다.
    /// </summary>
    public static string BuildWindowsScript(int pid, string targetExe, string newExe, string stagingDir, string logPath) => $$"""
        # Gpixel 업데이트: 앱이 끝나면 exe를 바꾸고 다시 실행한다. 실패하면 이전 exe를 되돌린다.
        $ErrorActionPreference = 'Stop'
        $appPid = {{pid}}
        $target = {{PsQuote(targetExe)}}
        $new = {{PsQuote(newExe)}}
        $staging = {{PsQuote(stagingDir)}}
        $log = {{PsQuote(logPath)}}
        $old = $target + '.old'
        function Log([string]$m) { try { Add-Content -LiteralPath $log -Value ((Get-Date -Format 's') + ' ' + $m) -Encoding UTF8 } catch {} }
        function Alert([string]$m) {
            try {
                Add-Type -AssemblyName PresentationFramework
                [System.Windows.MessageBox]::Show("Gpixel 업데이트를 설치하지 못했습니다. 이전 버전을 그대로 둡니다.`n`n$m`n`n기록: $log", 'Gpixel') | Out-Null
            } catch {}
        }
        Log "update to $new"
        for ($i = 0; $i -lt 600 -and (Get-Process -Id $appPid -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 200 }
        if (Get-Process -Id $appPid -ErrorAction SilentlyContinue) { Log 'app did not exit'; Alert 'Gpixel이 끝나지 않았습니다.'; exit 1 }
        try {
            if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
            Move-Item -LiteralPath $target -Destination $old
        } catch {
            Log "move failed: $_"; Alert "$_"
            if (Test-Path -LiteralPath $target) { Start-Process -FilePath $target }
            exit 1
        }
        try {
            Copy-Item -LiteralPath $new -Destination $target
        } catch {
            Log "copy failed: $_"
            Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
            Move-Item -LiteralPath $old -Destination $target
            Alert "$_"
            Start-Process -FilePath $target
            exit 1
        }
        Unblock-File -LiteralPath $target -ErrorAction SilentlyContinue
        Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target)
        Start-Sleep -Seconds 2
        Remove-Item -LiteralPath $old -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        Log 'done'

        """;

    /// <summary>
    /// macOS 설치 스크립트(sh): 앱이 끝나기를 기다렸다가(최대 2분) .app을 .old로 옮기고 새 .app을 ditto로 복사,
    /// 격리 표시(com.apple.quarantine)를 지우고 open으로 다시 실행한 뒤 .old를 지운다. 복사가 실패하면 .old를 되돌리고 이전 버전을 연다.
    /// </summary>
    public static string BuildMacScript(int pid, string targetApp, string newApp, string stagingDir, string logPath) => $$"""
        #!/bin/sh
        # Gpixel 업데이트: 앱이 끝나면 .app을 바꾸고 다시 실행한다. 실패하면 이전 .app을 되돌린다.
        APP_PID={{pid}}
        TARGET={{ShQuote(targetApp)}}
        NEW={{ShQuote(newApp)}}
        STAGING={{ShQuote(stagingDir)}}
        LOG={{ShQuote(logPath)}}
        BACKUP="$TARGET.old"
        exec >>"$LOG" 2>&1
        echo "$(date '+%Y-%m-%dT%H:%M:%S') update to $NEW"
        alert() {
          osascript -e 'display alert "Gpixel 업데이트를 설치하지 못했습니다" message "이전 버전을 그대로 둡니다. 자세한 내용은 임시 폴더의 gpixel-update.log에 있습니다." as warning' >/dev/null 2>&1
        }
        i=0
        while kill -0 "$APP_PID" 2>/dev/null; do
          i=$((i + 1))
          if [ "$i" -gt 600 ]; then echo "app did not exit"; alert; exit 1; fi
          sleep 0.2
        done
        xattr -dr com.apple.quarantine "$NEW" 2>/dev/null
        rm -rf "$BACKUP"
        if ! mv "$TARGET" "$BACKUP"; then echo "move failed"; alert; open "$TARGET"; exit 1; fi
        if ! ditto "$NEW" "$TARGET"; then
          echo "copy failed"
          rm -rf "$TARGET"
          mv "$BACKUP" "$TARGET"
          alert
          open "$TARGET"
          exit 1
        fi
        xattr -dr com.apple.quarantine "$TARGET" 2>/dev/null
        rm -rf "$BACKUP"
        open "$TARGET"
        rm -rf "$STAGING"
        echo "done"

        """;

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
            ClaudeCli.OpenUrl(url);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Ui.Error(owner, $"브라우저를 열지 못했습니다.\n{url}\n{ex.Message}");
        }
    }
}
