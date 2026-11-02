using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;

namespace Chickxel.App;

/// <summary>
/// 명령줄 인수: <c>--open-folder &lt;폴더&gt;</c>, <c>--open &lt;파일&gt;</c>(.chickxel/.png). 경로만 주면 --open과 같다.
/// <c>chickxel://asset/…</c> 주소나 <c>.chickxel-asset</c> 파일은 편집기에서 열지 않고 <see cref="Activation"/>으로 받는다(OS가 넘긴 일).
/// </summary>
public sealed record StartupArgs(string? OpenFolder, string? OpenFile, Activation? Activation = null)
{
    public static StartupArgs Parse(string[] args)
    {
        string? folder = null, file = null;
        Activation? activation = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--open-folder" when i + 1 < args.Length:
                    folder = args[++i];
                    break;
                case "--open" when i + 1 < args.Length:
                    file = args[++i];
                    break;
                default:
                    // macOS는 Finder에서 실행할 때 -psn_… 인수를 붙일 수 있다.
                    if (args[i].StartsWith('-')) break;
                    if (Activation.Parse(args[i]) is { } parsed) activation ??= parsed;
                    // 모르는 chickxel:// 주소(새 홈페이지가 보낸 다른 종류 등)는 편집기에서 열려 하지 않는다.
                    else if (!args[i].StartsWith(Chickxel.Core.Assets.AssetLinks.Scheme + ":", StringComparison.OrdinalIgnoreCase)) file ??= args[i];
                    break;
            }
        }
        if (activation is null && Activation.Parse(file) is { } install)
        {
            activation = install;
            file = null;
        }
        return new StartupArgs(folder, file, activation);
    }
}

/// <summary>Avalonia 앱: 어두운 Fluent 테마(촘촘한 크기) 위에 Chickxel 색을 입힌다.</summary>
public sealed class App : Application
{
    public static StartupArgs Args { get; set; } = new(null, null);
    /// <summary>시작할 때 읽은 설정(화면 언어를 정하려고 창보다 먼저 읽음). 메인 창이 그대로 쓴다.</summary>
    public static AppSettings Settings { get; set; } = new();
    /// <summary>창이 닫힌 뒤 앱을 새로 띄울지(화면 언어를 바꾸고 "지금 다시 시작"). 닫기를 취소하면 다시 false.</summary>
    public static bool RestartOnExit { get; set; }

    public override void Initialize()
    {
        Name = "Chickxel";
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(AppTheme.CreateFluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Chickxel.App/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });
        // 효과 만들기의 코드 편집기(AvaloniaEdit) 기본 모양
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Chickxel.App/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
        AppTheme.Install(this);
        PopupMenus.Install();
        TextFocusRelease.Install();
    }

    /// <summary>SIGTERM을 창 닫기로 바꾸는 등록(살아 있는 동안만 유효해 필드에 둔다).</summary>
    private System.Runtime.InteropServices.PosixSignalRegistration? _sigterm;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var main = new MainForm(Args, Settings);
            // 창이 뜬 뒤에 넣어야 Avalonia의 앱 초기화가 덮어쓰지 않는다.
            if (OperatingSystem.IsMacOS()) main.Opened += (_, _) => Platform.SetMacDockIcon(Icons.AppIconPng());
            // macOS·Linux: SIGTERM(예: 개발용 chickxel stop·restart, kill)은 바로 끝내지 않고 창 닫기와 같이 처리한다
            // (저장하지 않은 문서는 저장할지 묻고, 취소하면 계속 돈다).
            if (!OperatingSystem.IsWindows())
                _sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, ctx =>
                {
                    ctx.Cancel = true;
                    Dispatcher.UIThread.Post(main.Close);
                });
            desktop.MainWindow = main;
            AppActivation.Start(this);
            if (Args.Activation is { } activation) AppActivation.Deliver(activation);
        }
        base.OnFrameworkInitializationCompleted();
    }
}

static class Program
{
    /// <summary>앱 진입점. 처리하지 못한 예외는 <see cref="CrashGuard"/>가 로그를 남기고 알린 뒤 앱을 계속 돌린다.</summary>
    [STAThread]
    static int Main(string[] args)
    {
        App.Args = StartupArgs.Parse(args);
        // 주소·패키지 파일로 띄워졌는데 이미 켜진 Chickxel이 있으면(Windows) 그쪽에 넘기고 끝낸다.
        if (App.Args.Activation is { } activation && AppActivation.TryForward(activation)) return 0;
        Platform.MigrateLegacyFolders();
        // 문구를 읽기 전에(창·정적 필드보다 먼저) 화면 언어를 정한다. 처음 켤 때(또는 고른 언어의 CSV를 지웠으면) 컴퓨터 언어로 고른다.
        App.Settings = AppSettings.Load();
        var languages = Localization.UiLanguage.Available();
        if (App.Settings.Language is not { } language || !languages.Any(l => string.Equals(l.Code, language, StringComparison.OrdinalIgnoreCase)))
            App.Settings.Language = Localization.UiLanguage.Detect(System.Globalization.CultureInfo.CurrentUICulture, languages);
        Localization.UiLanguage.Apply(App.Settings.Language);
        int exitCode = CrashGuard.Run(BuildAvaloniaApp(), args);
        if (App.RestartOnExit) Relaunch();
        return exitCode;
    }

    /// <summary>같은 실행 파일로 새로 띄운다(빌드 출력을 <c>dotnet Chickxel.App.dll</c>로 돌렸으면 dll을 인수로).</summary>
    private static void Relaunch()
    {
        if (Environment.ProcessPath is not { } exe) return;
        var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(typeof(Program).Assembly.Location);
        try
        {
            System.Diagnostics.Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 새로 띄우지 못하면 그냥 끝난다(설정은 이미 저장돼 다음 실행에 적용됨).
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
