using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Gpixel.App.Ai;

/// <summary>AI 질문과 차례 완료를 운영체제 알림으로 표시한다. 앱 안 채팅 기록과는 별개다.</summary>
internal static class AiSystemNotifications
{
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const uint NifIcon = 2, NifTip = 4, NifInfo = 16;
    private const uint TrayId = 0x4750;
    private static IntPtr _trayWindow;
    private static readonly uint DataSize = (uint)Marshal.SizeOf<NotifyIconData>();
    private static CancellationTokenSource? _expiry;

    public static void Show(Window owner, string title, string message)
    {
        if (OperatingSystem.IsWindows()) ShowWindows(owner, title, message);
        else if (OperatingSystem.IsMacOS()) _ = ShowMacAsync(title, message);
    }

    public static void Clear(Window owner)
    {
        if (OperatingSystem.IsWindows() && owner.TryGetPlatformHandle()?.Handle == _trayWindow)
            DeleteTrayIcon();
    }

    private static async Task ShowMacAsync(string title, string message)
    {
        try
        {
            var start = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("on run argv\n  display notification (item 2 of argv) with title (item 1 of argv)\nend run");
            start.ArgumentList.Add(title);
            start.ArgumentList.Add(message);
            using var process = Process.Start(start);
            if (process is not null) await process.WaitForExitAsync();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            // 알림이 불가능해도 채팅과 편집은 계속한다.
        }
    }

    private static void ShowWindows(Window owner, string title, string message)
    {
        var window = owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (window == IntPtr.Zero) return;
        if (_trayWindow != IntPtr.Zero && _trayWindow != window) DeleteTrayIcon();

        var data = NewData(window);
        if (_trayWindow == IntPtr.Zero)
        {
            data.uFlags = NifIcon | NifTip;
            data.hIcon = SendMessage(window, 0x007F, (IntPtr)2, IntPtr.Zero); // WM_GETICON, ICON_SMALL2
            if (data.hIcon == IntPtr.Zero) data.hIcon = LoadIcon(IntPtr.Zero, (IntPtr)0x7F00);
            data.szTip = "Gpixel";
            if (!Shell_NotifyIcon(NimAdd, ref data)) return;
            _trayWindow = window;
        }

        data.uFlags = NifInfo;
        data.szInfoTitle = Trim(title, 63);
        data.szInfo = Trim(message, 255);
        data.dwInfoFlags = 1; // NIIF_INFO
        if (!Shell_NotifyIcon(NimModify, ref data))
        {
            DeleteTrayIcon();
            return;
        }

        _expiry?.Cancel();
        _expiry?.Dispose();
        var expiry = new CancellationTokenSource();
        _expiry = expiry;
        _ = ExpireAsync(expiry);
    }

    private static async Task ExpireAsync(CancellationTokenSource expiry)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), expiry.Token); }
        catch (OperationCanceledException) { return; }
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_expiry, expiry)) DeleteTrayIcon();
        });
    }

    private static void DeleteTrayIcon()
    {
        _expiry?.Cancel();
        _expiry?.Dispose();
        _expiry = null;
        if (_trayWindow == IntPtr.Zero) return;
        var data = NewData(_trayWindow);
        Shell_NotifyIcon(NimDelete, ref data);
        _trayWindow = IntPtr.Zero;
    }

    private static string Trim(string value, int length) => value.Length <= length ? value : value[..length];

    private static NotifyIconData NewData(IntPtr window) => new()
    {
        cbSize = DataSize, hWnd = window, uID = TrayId,
        szTip = "", szInfo = "", szInfoTitle = "",
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
}
