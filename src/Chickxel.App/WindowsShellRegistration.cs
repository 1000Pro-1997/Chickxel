using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Microsoft.Win32;

namespace Chickxel.App;

/// <summary>
/// Windows: zip으로 배포해 설치 프로그램이 없으므로, 앱이 켤 때마다 지금 실행 파일을 이 사용자의 레지스트리(HKCU, 관리자 권한 없음)에 적는다 —
/// <c>chickxel://</c> 주소(홈페이지 "Chickxel에서 열기")와 <c>.chickxel-asset</c> 파일 더블클릭이 이 실행 파일로 열린다.
/// exe를 옮기면 다음에 켤 때 새 경로로 바뀐다. <c>dotnet Chickxel.App.dll</c>로 돌린 개발 빌드는 적지 않는다.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsShellRegistration
{
    private const string FileProgId = "Chickxel.AssetPackage";
    private const int ShcneAssocChanged = 0x08000000;

    /// <summary>등록을 지금 실행 파일에 맞춘다(이미 맞으면 쓰지 않음). 실패해도 앱은 그대로 쓴다.</summary>
    public static void Update()
    {
        if (Environment.ProcessPath is not { } exe
            || string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase)) return;
        string command = $"\"{exe}\" \"%1\"";
        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            bool changed = Write(classes, AssetLinks.Scheme, "URL:Chickxel", exe, command, urlProtocol: true);
            changed |= Write(classes, FileProgId, UiStrings.Assets_FileType, exe, command, urlProtocol: false);
            using (var extension = classes.CreateSubKey(AssetPackage.Extension))
            {
                if (extension.GetValue(null) as string != FileProgId)
                {
                    extension.SetValue(null, FileProgId);
                    changed = true;
                }
            }
            if (changed) SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // 레지스트리를 쓸 수 없으면 홈페이지에서 열기·더블클릭 설치만 안 된다(다운로드 → 가져오기는 그대로).
        }
    }

    /// <summary>클래스 키 하나(이름·아이콘·여는 명령). 바뀐 값이 있으면 true.</summary>
    private static bool Write(RegistryKey classes, string name, string description, string exe, string command, bool urlProtocol)
    {
        using var key = classes.CreateSubKey(name);
        using var open = key.CreateSubKey(@"shell\open\command");
        if (open.GetValue(null) as string == command && key.GetValue(null) as string == description) return false;
        key.SetValue(null, description);
        if (urlProtocol) key.SetValue("URL Protocol", "");
        using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exe}\",0");
        open.SetValue(null, command);
        return true;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
