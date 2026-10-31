using System.Reflection;
using Chickxel.Core.IO;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>앱이 놓인 형태. 자동 업데이트는 배포판(Windows 단일 exe, macOS .app 묶음)만 바꿀 수 있다.</summary>
public enum InstallKind
{
    /// <summary>소스에서 빌드·실행(<c>dotnet run</c>, run.sh 등).</summary>
    Development,
    /// <summary>publish로 만든 단일 파일 Chickxel.exe.</summary>
    WindowsExe,
    /// <summary>Chickxel.app 묶음 안에서 실행.</summary>
    MacBundle,
}

/// <summary>앱 이름·버전·설치 형태와 프로젝트 주소.</summary>
public static class AppInfo
{
    public const string Name = "Chickxel";
    public const string ThirdPartyNoticesUrl = AppUpdate.RepoUrl + "/blob/main/THIRD-PARTY-NOTICES.md";
    /// <summary>버그 제보·불편한 점·아이디어를 받는 Discord 서버의 공개 초대 링크.</summary>
    public const string DiscordUrl = "https://discord.gg/Avw7aUVxqr";
    /// <summary>Chickxel 개발 후원 페이지.</summary>
    public const string SupportUrl = "https://buymeacoffee.com/chickxel";
    /// <summary>홈페이지(한국어 쪽; 영어 쪽은 뒤에 "en/").</summary>
    public const string WebsiteUrl = "https://1000pro-1997.github.io/Chickxel/";

    /// <summary>지금 UI 언어의 홈페이지 주소(한국어가 아니면 영어 쪽).</summary>
    public static string LocalizedWebsiteUrl => UiLanguage.Current == UiLanguage.Korean ? WebsiteUrl : WebsiteUrl + "en/";

    /// <summary>
    /// 앱 버전(어셈블리 InformationalVersion에서 "+커밋"을 뺀 것, publish의 <c>-p:Version</c>). 읽지 못하면 0.0.0.
    /// </summary>
    public static SemVersion Version { get; } = ReadVersion();

    public static string VersionText => Version.ToString();

    public static InstallKind InstallKind { get; }

    /// <summary>자동 업데이트가 바꿀 곳: Windows는 실행 중인 exe, macOS는 .app 묶음 폴더. 개발 빌드면 null.</summary>
    public static string? InstallPath { get; }

    /// <summary>
    /// 개발 빌드: 소스에서 실행했거나(배포판 모양이 아님) 버전이 0.0.0(CI 시험 빌드 0.0.0-ci.N)인 것.
    /// 자동 확인·자동 설치를 하지 않는다(수동 확인은 됨).
    /// </summary>
    public static bool IsDevBuild => InstallKind == InstallKind.Development || Version is { Major: 0, Minor: 0, Patch: 0 };

    static AppInfo()
    {
        (InstallKind, InstallPath) = DetectInstall();
    }

    private static SemVersion ReadVersion()
    {
        var info = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return SemVersion.TryParse(info, out var v) ? v : new SemVersion(0, 0, 0);
    }

    private static (InstallKind, string?) DetectInstall()
    {
        var baseDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (OperatingSystem.IsWindows())
        {
            // 단일 파일 publish는 Chickxel.App.dll을 exe 안에 묶는다. dll이 옆에 있으면 빌드 출력(dotnet run/build)이다.
            var exe = Environment.ProcessPath;
            if (exe is not null
                && string.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase)
                && !File.Exists(Path.Combine(baseDir, "Chickxel.App.dll")))
                return (InstallKind.WindowsExe, exe);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // …/Chickxel.app/Contents/MacOS/
            var contents = Path.GetDirectoryName(baseDir);
            var bundle = contents is null ? null : Path.GetDirectoryName(contents);
            if (Path.GetFileName(baseDir) == "MacOS" && Path.GetFileName(contents) == "Contents"
                && bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(contents!, "Info.plist")))
                return (InstallKind.MacBundle, bundle);
        }
        return (InstallKind.Development, null);
    }
}
