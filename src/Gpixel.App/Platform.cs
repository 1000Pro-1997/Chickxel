using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Gpixel.App;

/// <summary>운영체제마다 다른 일: 설정 폴더, 파일 관리자 열기, 휴지통, 단축키 수정자.</summary>
public static class Platform
{
    public static bool IsMac => OperatingSystem.IsMacOS();

    /// <summary>설정 바탕 폴더를 바꾸는 환경 변수(시험용) — 설정·휴지통·기록·오류 로그가 모두 그 아래 Gpixel 폴더로 간다.</summary>
    public const string DataDirVariable = "GPIXEL_DATA_DIR";

    /// <summary>앱 설정 바탕 폴더(Windows: %AppData%, macOS: ~/Library/Application Support, Linux: ~/.config, <see cref="DataDirVariable"/>가 있으면 그것).</summary>
    public static string AppDataDirectory => Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } dir
        ? dir
        : Environment.GetFolderPath(IsMac ? Environment.SpecialFolder.LocalApplicationData : Environment.SpecialFolder.ApplicationData);

    /// <summary>메뉴에 쓰는 파일 관리자 이름.</summary>
    public static string FileManagerName => OperatingSystem.IsWindows() ? "탐색기" : IsMac ? "Finder" : "파일 관리자";

    /// <summary>단축키의 "Ctrl" 자리(macOS는 ⌘).</summary>
    public static KeyModifiers CommandModifier => IsMac ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>파일 관리자에서 파일을 선택해 보여 준다(파일이 없으면 폴더를 연다).</summary>
    public static void RevealInFileManager(string path)
    {
        if (OperatingSystem.IsWindows()) Start("explorer.exe", $"/select,\"{path}\"");
        else if (IsMac) Start("open", "-R", path);
        else Start("xdg-open", Path.GetDirectoryName(path) ?? path);
    }

    /// <summary>파일 관리자로 폴더를 연다.</summary>
    public static void OpenFolder(string folder)
    {
        if (OperatingSystem.IsWindows()) Start("explorer.exe", $"\"{folder}\"");
        else if (IsMac) Start("open", folder);
        else Start("xdg-open", folder);
    }

    private static void Start(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        if (OperatingSystem.IsWindows() && args.Length == 1) psi.Arguments = args[0];
        else foreach (var a in args) psi.ArgumentList.Add(a);
        try { Process.Start(psi)?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new IOException(ex.Message, ex);
        }
    }

    // ───────────── macOS Dock 아이콘 ─────────────

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    /// <summary>
    /// macOS Dock 아이콘을 바꾼다. <c>.app</c> 묶음으로 실행하면 Info.plist의 icns가 쓰이지만,
    /// <c>dotnet run</c>·<c>run.sh</c>처럼 묶음 없이 실행하면 기본 아이콘이 보이므로 실행 중에 직접 넣는다. UI 스레드에서 부른다.
    /// </summary>
    public static unsafe void SetMacDockIcon(byte[] png)
    {
        if (!IsMac || png.Length == 0) return;
        fixed (byte* bytes = png)
        {
            var data = Send(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"), (IntPtr)bytes, (nuint)png.Length);
            var image = Send(Send(objc_getClass("NSImage"), sel_registerName("alloc")), sel_registerName("initWithData:"), data);
            if (image == IntPtr.Zero) return;
            var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            Send(app, sel_registerName("setApplicationIconImage:"), image);
        }
    }

    // ───────────── 커서 옮기기(숫자 상자 끌기에서 화면 끝 넘기기) ─────────────

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint(double x, double y)
    {
        public double X = x, Y = y;
    }

    [DllImport(CoreGraphics)]
    private static extern int CGWarpMouseCursorPosition(CGPoint point);

    [DllImport(CoreGraphics)]
    private static extern int CGAssociateMouseAndMouseCursorPosition(int connected);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    /// <summary>
    /// 마우스 커서를 화면 좌표 <paramref name="screen"/>(Avalonia 화면 픽셀)로 옮긴다. 옮겼으면 true(지원하지 않는 OS·실패면 false).
    /// macOS는 포인트 단위라 <paramref name="scaling"/>으로 나눈다.
    /// </summary>
    public static bool WarpCursor(PixelPoint screen, double scaling)
    {
        try
        {
            if (IsMac)
            {
                double s = scaling > 0 ? scaling : 1;
                if (CGWarpMouseCursorPosition(new CGPoint(screen.X / s, screen.Y / s)) != 0) return false;
                // 옮긴 직후 잠깐 마우스 이동이 무시되는 것을 없앤다.
                CGAssociateMouseAndMouseCursorPosition(1);
                return true;
            }
            if (OperatingSystem.IsWindows()) return SetCursorPos(screen.X, screen.Y);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return false;
    }
}
