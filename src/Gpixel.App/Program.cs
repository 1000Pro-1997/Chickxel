using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;

namespace Gpixel.App;

/// <summary>명령줄 인수: <c>--open-folder &lt;폴더&gt;</c>, <c>--open &lt;파일&gt;</c>(.gpixel/.png). 경로만 주면 --open과 같다.</summary>
public sealed record StartupArgs(string? OpenFolder, string? OpenFile)
{
    public static StartupArgs Parse(string[] args)
    {
        string? folder = null, file = null;
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
                    if (!args[i].StartsWith('-')) file ??= args[i];
                    break;
            }
        }
        return new StartupArgs(folder, file);
    }
}

/// <summary>Avalonia 앱: 어두운 Fluent 테마(촘촘한 크기) 위에 Gpixel 색을 입힌다.</summary>
public sealed class App : Application
{
    public static StartupArgs Args { get; set; } = new(null, null);
    /// <summary>시작할 때 읽은 설정(화면 언어를 정하려고 창보다 먼저 읽음). 메인 창이 그대로 쓴다.</summary>
    public static AppSettings Settings { get; set; } = new();

    public override void Initialize()
    {
        Name = "Gpixel";
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(AppTheme.CreateFluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Gpixel.App/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });
        AppTheme.Install(this);
        PopupMenus.Install();
        TextFocusRelease.Install();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var main = new MainForm(Args, Settings);
            // 창이 뜬 뒤에 넣어야 Avalonia의 앱 초기화가 덮어쓰지 않는다.
            if (OperatingSystem.IsMacOS()) main.Opened += (_, _) => Platform.SetMacDockIcon(Icons.AppIconPng());
            desktop.MainWindow = main;
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
        // 문구를 읽기 전에(창·정적 필드보다 먼저) 화면 언어를 정한다.
        App.Settings = AppSettings.Load();
        Localization.UiLanguage.Apply(App.Settings.Language);
        return CrashGuard.Run(BuildAvaloniaApp(), args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
