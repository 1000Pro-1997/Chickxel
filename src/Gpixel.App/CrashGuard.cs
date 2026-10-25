using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>
/// 처리하지 못한 예외를 붙잡는다: 오류 로그(<see cref="LogDirectory"/>의 <c>crash-YYYYMMDD-HHmmss.log</c>, 최근 <see cref="KeepLogs"/>개)를 남기고,
/// UI 스레드 예외면 알림 창(<see cref="CrashDialog"/>)을 띄운 뒤 앱을 계속 돌린다.
/// <list type="bullet">
/// <item>UI 스레드 작업(입력 처리·타이머·Post 등)의 예외: <see cref="Dispatcher.UnhandledException"/>에서 처리함으로 표시.
/// 처리기가 없으면 Avalonia는 다시 던지고, macOS(Avalonia.Native)에서는 그 예외가 실행 루프를 끝내 앱이 죽었다(v1.14.0 효과 창 끌기).</item>
/// <item>디스패처를 거치지 않는 창 이벤트(macOS 창 활성화·크기 등)의 예외는 실행 루프 밖으로 던져진다 —
/// <see cref="Run"/>(메인 루프)과 <see cref="Ui.Wait{T}(Task{T})"/>(대화상자 루프)가 받아 <see cref="Recover"/>한 뒤 루프를 다시 돈다.</item>
/// <item>다른 스레드의 예외(앱이 끝남): 로그를 남기고 수정된 문서를 <see cref="RecoveryDirectory"/>에 비상 저장한다.</item>
/// <item>관찰되지 않은 Task 예외: 로그만(관찰함으로 표시).</item>
/// <item>2초 안에 3번 넘게 생기거나 알림 창이 떠 있는 동안·알림 창 자체가 예외를 내면 로그만 남긴다.</item>
/// </list>
/// </summary>
public static class CrashGuard
{
    /// <summary>남겨 둘 오류 로그 수.</summary>
    public const int KeepLogs = 20;

    /// <summary>이 시간 안에 <see cref="BurstLimit"/>번 넘게 생기면 알림 창 없이 로그만.</summary>
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(2);
    private const int BurstLimit = 3;

    /// <summary>로그 파일 하나의 최대 크기(같은 초에 쏟아지는 예외로 커지지 않게).</summary>
    private const long MaxLogBytes = 1 << 20;

    /// <summary>오류 로그 폴더(<c>&lt;설정 폴더&gt;/Gpixel/Logs</c>).</summary>
    public static string LogDirectory => Path.Combine(Platform.AppDataDirectory, "Gpixel", "Logs");

    /// <summary>비상 저장 폴더(<c>&lt;설정 폴더&gt;/Gpixel/Recovery</c>).</summary>
    public static string RecoveryDirectory => Path.Combine(Platform.AppDataDirectory, "Gpixel", "Recovery");

    /// <summary>앱이 끝나기 전에 비상 저장할 수정된 문서(지금 문서 + 메모리에 남겨 둔 문서). MainForm이 연결한다.</summary>
    public static Func<IEnumerable<SpriteProject>>? UnsavedProjects { get; set; }

    private static readonly object Lock = new();
    private static readonly Queue<DateTime> Recent = new();
    private static bool _installed, _shuttingDown, _dialogOpen, _dialogBroken;
    private static Exception? _lastLogged;
    private static string? _lastPath;

    /// <summary>
    /// 데스크톱 앱 실행: 처리기들을 걸고(UI 스레드 처리기는 Avalonia 준비가 끝난 뒤) <c>StartWithClassicDesktopLifetime</c>으로 돌린다(Program.Main).
    /// 메인 루프 밖으로 예외가 던져지면(macOS 창 이벤트) 알린 뒤 앱이 끝날 때까지 메인 루프를 다시 돈다. 종료 코드.
    /// </summary>
    public static int Run(AppBuilder builder, string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => OnFatal(e.ExceptionObject as Exception, e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log(e.Exception, UiStrings.S_CrashGuard_Run_9A96A3E2F9);
            e.SetObserved();
        };
        builder.AfterSetup(_ => InstallDispatcher());
        try
        {
            return builder.StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e) when (CanResume() && Recover(e, UiStrings.S_CrashGuard_Run_19B16BF9A9))
        {
            return ResumeMainLoop();
        }
    }

    /// <summary>UI 스레드 디스패처 처리기(디스패처는 플랫폼 준비 뒤에 만들어야 하므로 AfterSetup에서).</summary>
    private static void InstallDispatcher()
    {
        if (_installed) return;
        _installed = true;
        Dispatcher.UIThread.UnhandledException += (_, e) => e.Handled = Recover(e.Exception, UiStrings.S_CrashGuard_InstallDispatcher_C20B6EAA3B);
        Dispatcher.UIThread.ShutdownStarted += (_, _) => _shuttingDown = true;
    }

    /// <summary>메인 창이 떠 있고 끝나는 중이 아니면 메인 루프를 이어 돌 수 있다.</summary>
    private static bool CanResume()
        => _installed && !_shuttingDown
           && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.IsVisible: true };

    private static int ResumeMainLoop()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        int exitCode = 0;
        using var exit = new CancellationTokenSource();
        desktop.Exit += (_, e) =>
        {
            exitCode = e.ApplicationExitCode;
            exit.Cancel();
        };
        while (true)
        {
            try
            {
                Dispatcher.UIThread.MainLoop(exit.Token);
                Environment.ExitCode = exitCode;
                return exitCode;
            }
            catch (Exception e) when (CanResume() && Recover(e, UiStrings.S_CrashGuard_ResumeMainLoop_F498C55218))
            {
                // 알렸으니 이어서 돈다.
            }
        }
    }

    /// <summary>
    /// UI 스레드에서 처리하지 못한 예외: 로그를 남기고(실패해도 계속) 알림 창을 곧 띄운다(짧은 사이에 여러 번·창이 떠 있으면 로그만).
    /// 앱을 계속 돌려도 되면 true(설치 전·끝나는 중이면 false — 그대로 던진다).
    /// </summary>
    public static bool Recover(Exception e, string source)
    {
        if (!_installed || _shuttingDown) return false;
        var path = Log(e, source + UiStrings.S_CrashGuard_Recover_C9E2C1EC9E);
        if (Burst() || _dialogOpen || _dialogBroken) return true;
        _dialogOpen = true; // 띄우기를 예약한 때부터(그 사이 예외는 로그만)
        try
        {
            Dispatcher.UIThread.Post(() => ShowDialog(e, path));
        }
        catch (InvalidOperationException)
        {
            // 디스패처가 막 끝나는 중 — 로그만
            _dialogOpen = false;
        }
        return true;
    }

    /// <summary>최근 <see cref="BurstWindow"/> 안에 <see cref="BurstLimit"/>번 넘게 생겼는지(이번 것 포함).</summary>
    private static bool Burst()
    {
        lock (Lock)
        {
            var now = DateTime.UtcNow;
            Recent.Enqueue(now);
            while (Recent.Count > 0 && now - Recent.Peek() > BurstWindow) Recent.Dequeue();
            return Recent.Count > BurstLimit;
        }
    }

    private static void ShowDialog(Exception e, string? logPath)
    {
        try
        {
            new CrashDialog(Summary(e, logPath)).ShowDialog(Ui.Owner(null));
        }
        catch (Exception ex)
        {
            // 알림 창도 못 띄우면 이제부터 로그만
            _dialogBroken = true;
            Log(ex, UiStrings.S_CrashGuard_ShowDialog_2163AA43BD);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private static string Summary(Exception e, string? logPath)
    {
        var message = e.Message.Length > 300 ? e.Message[..300] + "…" : e.Message;
        return UiStrings.S_CrashGuard_Summary_756D71A877
               + $"{e.GetType().Name}: {message}\n\n"
               + (logPath is not null ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_CrashGuard_Summary_9CA92BBE5E,Path.GetFileName(logPath)) : "")
               + UiStrings.S_CrashGuard_Summary_35DAD57FC4;
    }

    /// <summary>오류 로그 폴더를 파일 관리자로 연다(없으면 만든다).</summary>
    public static void OpenLogFolder()
    {
        Directory.CreateDirectory(LogDirectory);
        Platform.OpenFolder(LogDirectory);
    }

    /// <summary>다른 스레드(또는 메인 루프 밖)의 처리하지 못한 예외: 로그, 앱이 끝나면 수정된 문서 비상 저장.</summary>
    private static void OnFatal(Exception? e, bool terminating)
    {
        if (e is not null) Log(e, terminating ? UiStrings.S_CrashGuard_OnFatal_B962FB416E : UiStrings.S_CrashGuard_OnFatal_55B42030F8);
        if (terminating) EmergencySave();
    }

    /// <summary>
    /// 수정된 문서를 <see cref="RecoveryDirectory"/>에 따로 저장한다(원래 파일·수정 표시는 그대로). 프로젝트는 UI 스레드에서만 고치므로 UI 스레드에서
    /// 쓴다(다른 스레드면 UI 스레드가 지금 일을 마칠 때까지 잠깐 기다림). 실패하면 로그만.
    /// </summary>
    private static void EmergencySave()
    {
        if (UnsavedProjects is not { } source) return;
        void Save()
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            int n = 0;
            foreach (var p in source())
            {
                try
                {
                    Directory.CreateDirectory(RecoveryDirectory);
                    var name = string.Concat(Path.GetFileNameWithoutExtension(p.DisplayName).Split(Path.GetInvalidFileNameChars()));
                    var path = Path.Combine(RecoveryDirectory, $"{name}-{stamp}-{++n}{SpriteProject.FileExtension}");
                    using (var stream = File.Create(path)) ProjectIO.Save(p, stream);
                    Log(null, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_CrashGuard_EmergencySave_3206D81AC2,path));
                }
                catch (Exception ex)
                {
                    Log(ex, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_CrashGuard_EmergencySave_3B295813C9,p.DisplayName));
                }
            }
        }
        try
        {
            if (Dispatcher.UIThread.CheckAccess()) Save();
            else Dispatcher.UIThread.Invoke(Save, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Log(ex, UiStrings.S_CrashGuard_EmergencySave_F5D4306C56);
        }
    }

    /// <summary>
    /// 예외(또는 <paramref name="e"/>가 null이면 알림 한 줄)를 로그 파일에 덧붙인다(같은 초면 같은 파일). 오래된 로그는 최근 <see cref="KeepLogs"/>개만 남긴다.
    /// 같은 예외는 한 번만. 로그 경로(못 쓰면 null) — 로그를 못 남겨도 앱은 계속한다.
    /// </summary>
    internal static string? Log(Exception? e, string source)
    {
        lock (Lock)
        {
            if (e is not null && ReferenceEquals(e, _lastLogged)) return _lastPath;
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var now = DateTimeOffset.Now;
                var path = Path.Combine(LogDirectory, $"crash-{now:yyyyMMdd-HHmmss}.log");
                if (!File.Exists(path) || new FileInfo(path).Length < MaxLogBytes)
                    File.AppendAllText(path, Describe(e, source, now), Encoding.UTF8);
                Prune();
                if (e is not null) (_lastLogged, _lastPath) = (e, path);
                return path;
            }
            catch (Exception)
            {
                // 로그 폴더를 못 쓰면(권한·디스크) 포기 — 예외 처리기에서 다시 던지지 않는다.
                return null;
            }
        }
    }

    private static string Describe(Exception? e, string source, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"===== {now:yyyy-MM-dd HH:mm:ss.fff zzz} — {source}");
        sb.AppendLine($"Gpixel {AppInfo.VersionText} ({AppInfo.InstallKind})");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture} · {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_CrashGuard_Describe_732C4602C8,Environment.CurrentManagedThreadId,(Dispatcher.UIThread.CheckAccess() ? " (UI)" : "")));
        sb.AppendLine(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_CrashGuard_Describe_60D73F9563,ActiveWindowTitle()));
        if (e is not null) sb.AppendLine(e.ToString());
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>활성 창 제목(없으면 메인 창). UI 스레드가 아니면 읽지 않는다.</summary>
    private static string ActiveWindowTitle()
    {
        if (!Dispatcher.UIThread.CheckAccess()) return UiStrings.S_CrashGuard_ActiveWindowTitle_DD13D01EBB;
        try
        {
            return Ui.Owner(null)?.Title ?? UiStrings.S_CrashGuard_ActiveWindowTitle_DC1E8FED9D;
        }
        catch (Exception)
        {
            return UiStrings.S_CrashGuard_ActiveWindowTitle_076C9E002D;
        }
    }

    private static void Prune()
    {
        var logs = new DirectoryInfo(LogDirectory).GetFiles("crash-*.log").OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(KeepLogs);
        foreach (var old in logs)
        {
            try { old.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>처리하지 못한 오류 알림: 무슨 오류인지와 로그 파일, Discord 제보 안내·단추, "로그 폴더 열기"(창은 그대로)·"확인".</summary>
internal sealed class CrashDialog : DialogBase
{
    public CrashDialog(string message) : base(UiStrings.S_CrashGuard_CrashDialog_E9BDC482E3)
    {
        Body.Children.Add(MessageDialog.MessageRow(message, MessageIcon.Error));
        var report = Ui.DiscordButton(UiStrings.Crash_ReportOnDiscord, (_, _) => Updater.OpenUrl(this, AppInfo.DiscordUrl));
        report.HorizontalAlignment = HorizontalAlignment.Left;
        var feedback = new StackPanel { Spacing = 4, Margin = new Thickness(42, 12, 0, 0) }; // 메시지 글과 왼쪽을 맞춘다(아이콘 30 + 간격 12).
        feedback.Children.Add(new TextBlock { Text = UiStrings.Crash_ReportOnDiscord_Hint, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left, Foreground = AppTheme.DimTextBrush });
        feedback.Children.Add(report);
        Body.Children.Add(feedback);
        Body.MinWidth = 280;
        OkButton.Content = UiStrings.S_CrashGuard_CrashDialog_AA2995CAF6;
        OkButton.Width = double.NaN; // 기본 폭 90에 "로그 폴더 열기"가 잘리므로 글에 맞춘다.
        OkButton.MinWidth = 90;
        OkButton.IsDefault = false;
        OkButton.Click += (_, _) =>
        {
            try { CrashGuard.OpenLogFolder(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Ui.Error(this, $"{ex.Message}\n{CrashGuard.LogDirectory}");
            }
            DialogResult = DialogResult.None; // 닫지 않음
        };
        CancelButtonControl.Content = UiStrings.S_CrashGuard_CrashDialog_8FDCF832FD;
        CancelButtonControl.IsDefault = true;
        Opened += (_, _) => CancelButtonControl.Focus();
    }
}
