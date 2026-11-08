using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Microsoft.Win32;

namespace Chickxel.App;

/// <summary>
/// Windows: zip으로 배포해 설치 프로그램이 없으므로, 앱이 켤 때마다 지금 실행 파일을 이 사용자의 레지스트리(HKCU, 관리자 권한 없음)에 적는다 —
/// <c>chickxel://</c> 주소와 <c>.chickxel-asset</c>·<c>.chickxel</c> 파일을 이 실행 파일로 연다.
/// exe를 옮기면 다음에 켤 때 새 경로로 바뀐다. <c>dotnet Chickxel.App.dll</c>로 돌린 개발 빌드는 적지 않는다.
/// exe에 탐색기 썸네일 처리기(<c>src/Chickxel.ThumbnailProvider</c>, 배포 빌드만 묶음)가 들어 있으면 그것도 풀어 등록한다(<see cref="WriteThumbnailProvider"/>).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsShellRegistration
{
    private const string FileProgId = "Chickxel.AssetPackage";
    private const string ProjectProgId = "Chickxel.Project";
    private const int ShcneAssocChanged = 0x08000000;
    /// <summary>썸네일 처리기 COM 클래스(<c>thumbnail.c</c>의 <c>kClsidProvider</c>와 같아야 함).</summary>
    private const string ThumbnailClsid = "{C38796F8-4011-498E-A702-A102FB10300F}";
    /// <summary>IThumbnailProvider: 확장자의 <c>ShellEx</c> 아래 이 이름의 키가 썸네일 처리기를 가리킨다.</summary>
    private const string ThumbnailHandlerKey = "{E357FCCD-A995-4576-B01F-234630154E96}";
    private const string ThumbnailResource = "Chickxel.ThumbnailProvider.dll";

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
            changed |= Write(classes, ProjectProgId, UiStrings.Project_FileType, exe, command, urlProtocol: false);
            using (var extension = classes.CreateSubKey(AssetPackage.Extension))
            {
                if (extension.GetValue(null) as string != FileProgId)
                {
                    extension.SetValue(null, FileProgId);
                    changed = true;
                }
            }
            using (var extension = classes.CreateSubKey(".chickxel"))
            {
                if (extension.GetValue(null) as string != ProjectProgId)
                {
                    extension.SetValue(null, ProjectProgId);
                    changed = true;
                }
            }
            changed |= WriteThumbnailProvider(classes);
            if (changed) SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // 레지스트리를 쓸 수 없으면 OS 파일·주소 연결만 안 된다(앱의 파일 메뉴는 그대로).
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

    /// <summary>
    /// 탐색기 썸네일: exe에 묶은 처리기 DLL을 로컬 앱 데이터의 <c>Chickxel/ShellThumbnails</c>에 내용 해시가 든 이름으로 풀고(탐색기가 쥐고 있는 옛 판은
    /// 덮어쓰지 않고, 놓으면 다음 실행에 지움) <c>.chickxel</c>에 등록한다. 처리기가 병아리 배지를 직접 그리므로 탐색기의 기본 앱 아이콘 겹침
    /// (<c>TypeOverlay</c>)과 그림자·테두리(<c>Treatment</c>)는 끈다. 처리기를 묶지 않은 빌드면 아무것도 하지 않는다. 바뀐 값이 있으면 true.
    /// </summary>
    private static bool WriteThumbnailProvider(RegistryKey classes)
    {
        using var resource = typeof(WindowsShellRegistration).Assembly.GetManifestResourceStream(ThumbnailResource);
        if (resource is null) return false;
        var bytes = new byte[resource.Length];
        resource.ReadExactly(bytes);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chickxel", "ShellThumbnails");
        var dll = Path.Combine(folder, $"ChickxelThumbnails-{Convert.ToHexStringLower(SHA256.HashData(bytes).AsSpan(0, 6))}.dll");
        if (!File.Exists(dll))
        {
            Directory.CreateDirectory(folder);
            var temp = dll + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, dll, overwrite: true);
        }
        foreach (var old in Directory.EnumerateFiles(folder, "ChickxelThumbnails-*"))
        {
            if (string.Equals(old, dll, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        bool changed = SetValue(classes, $@"CLSID\{ThumbnailClsid}", null, "Chickxel Thumbnail Provider");
        changed |= SetValue(classes, $@"CLSID\{ThumbnailClsid}\InprocServer32", null, dll);
        changed |= SetValue(classes, $@"CLSID\{ThumbnailClsid}\InprocServer32", "ThreadingModel", "Apartment");
        changed |= SetValue(classes, $@".chickxel\ShellEx\{ThumbnailHandlerKey}", null, ThumbnailClsid);
        changed |= SetValue(classes, ProjectProgId, "TypeOverlay", "");
        changed |= SetValue(classes, ProjectProgId, "Treatment", 0);
        return changed;
    }

    /// <summary>키 <paramref name="path"/>의 값 하나(문자열 또는 DWORD)를 맞춘다. 바뀌었으면 true.</summary>
    private static bool SetValue(RegistryKey classes, string path, string? name, object value)
    {
        using var key = classes.CreateSubKey(path);
        if (Equals(key.GetValue(name), value)) return false;
        key.SetValue(name, value, value is int ? RegistryValueKind.DWord : RegistryValueKind.String);
        return true;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
