using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Chickxel.Core.Assets;

namespace Chickxel.App;

/// <summary>OS가 앱에 넘긴 일: 홈페이지 "Chickxel에서 열기" 주소, 또는 더블클릭한 에셋 패키지 파일.</summary>
public abstract record Activation
{
    /// <summary><c>chickxel://asset/ID</c>: 에셋 관리자 상점에서 패키지를 보인다(설치는 사용자가 누름).</summary>
    public sealed record OpenPackage(string Id) : Activation
    {
        public override string Argument => AssetLinks.Url(Id);
    }

    /// <summary><c>.chickxel-asset</c> 파일: 에셋 관리자에서 설치한다(가져오기와 같음).</summary>
    public sealed record InstallFile(string Path) : Activation
    {
        public override string Argument => Path;
    }

    /// <summary>이 일을 다른 Chickxel에 넘길 때 쓰는 명령줄 인수 하나.</summary>
    public abstract string Argument { get; }

    /// <summary>명령줄 인수나 OS가 준 주소·경로 하나. 앱이 맡을 일이 아니면 null(편집기에서 여는 파일 등).</summary>
    public static Activation? Parse(string? text)
    {
        if (AssetLinks.PackageId(text) is { } id) return new OpenPackage(id);
        if (string.IsNullOrWhiteSpace(text)) return null;
        string path = text.Trim();
        if (!path.EndsWith(AssetPackage.Extension, StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(AssetPackage.LegacyExtension, StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            path = System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }
        return File.Exists(path) ? new InstallFile(path) : null;
    }
}

/// <summary>
/// OS가 넘긴 일(<see cref="Activation"/>)을 메인 창에 전한다. 메인 창이 받을 준비가 되기 전에 온 것은 모아 두었다가 준비되면 차례로 넘긴다.
/// <list type="bullet">
/// <item>macOS: LaunchServices가 실행 중인(없으면 띄운) 앱에 주소·파일을 보낸다 — Avalonia의 <see cref="IActivatableLifetime"/>로 받는다.</item>
/// <item>Windows: 주소·파일마다 새 프로세스가 뜬다. 새 프로세스는 이미 켜진 Chickxel이 있으면 이 사용자만 쓰는 named pipe로 일을 넘기고
/// 바로 끝난다(<see cref="TryForward"/>). 켜진 Chickxel은 pipe로 받는다(<see cref="Listen"/>).</item>
/// </list>
/// </summary>
public static class AppActivation
{
    private static readonly List<Activation> Pending = [];
    private static Action<Activation>? _handler;

    /// <summary>이 사용자의 Chickxel끼리 쓰는 pipe 이름(설정 폴더가 다르면 — 시험용 CHICKXEL_DATA_DIR — 다른 pipe).</summary>
    private static string PipeName
    {
        get
        {
            byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Platform.AppFolder.ToLowerInvariant()));
            return "Chickxel.Activation." + Convert.ToHexString(hash, 0, 8);
        }
    }

    /// <summary>메인 창이 준비됨: 지금까지 모인 일과 앞으로 올 일을 <paramref name="handler"/>로 넘긴다(UI 스레드).</summary>
    public static void SetHandler(Action<Activation> handler)
    {
        _handler = handler;
        var queued = Pending.ToArray();
        Pending.Clear();
        foreach (var activation in queued) handler(activation);
    }

    /// <summary>일 하나를 넘긴다(UI 스레드에서). 메인 창이 아직이면 모아 둔다.</summary>
    public static void Deliver(Activation activation)
    {
        if (_handler is { } handler) handler(activation);
        else Pending.Add(activation);
    }

    /// <summary>
    /// 앱이 뜬 뒤 한 번: macOS는 OS가 보내는 주소·파일을 받기 시작하고, Windows는 다른 Chickxel이 넘기는 일을 pipe로 받기 시작하며
    /// 주소·파일 연결을 지금 실행 파일로 적는다(<see cref="WindowsShellRegistration"/>).
    /// </summary>
    public static void Start(Application app)
    {
        if (app.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime lifetime)
            lifetime.Activated += (_, e) =>
            {
                foreach (var activation in FromOs(e)) Dispatcher.UIThread.Post(() => Deliver(activation));
            };
        if (OperatingSystem.IsWindows())
        {
            Listen();
            _ = Task.Run(WindowsShellRegistration.Update);
        }
    }

    private static IEnumerable<Activation> FromOs(ActivatedEventArgs e)
    {
        if (e is ProtocolActivatedEventArgs { Uri: { } uri } && Activation.Parse(uri.OriginalString) is { } link) yield return link;
        if (e is FileActivatedEventArgs { Files: { } files })
            foreach (var file in files)
                if (Activation.Parse(file.TryGetLocalPath()) is { } install) yield return install;
    }

    // ───────────── Windows: 다른 Chickxel에 넘기기 ─────────────

    /// <summary>
    /// 이미 켜진 Chickxel에 <paramref name="activation"/>을 넘긴다(그쪽이 앞으로 나오도록 허락한 뒤). 넘겼으면 true — 이 프로세스는 끝내면 된다.
    /// 켜진 Chickxel이 없거나 받지 못하면 false.
    /// </summary>
    public static bool TryForward(Activation activation)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(1500);
            // 받는 쪽이 먼저 자기 프로세스 번호를 알린다: 브라우저가 띄운 이 프로세스만 창을 앞으로 가져올 권한이 있어 그쪽에 넘겨 준다.
            Span<byte> pid = stackalloc byte[4];
            pipe.ReadExactly(pid);
            AllowSetForegroundWindow(BitConverter.ToInt32(pid));
            byte[] message = Encoding.UTF8.GetBytes(activation.Argument + "\n");
            pipe.Write(message);
            pipe.Flush();
            // 받았다는 1바이트를 기다린다(받기 전에 끝나 일이 사라지지 않게).
            return pipe.ReadByte() == 1;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// 다른 Chickxel이 넘기는 일을 받는다(백그라운드, 앱이 끝나면 함께 끝남). 같은 pipe를 이미 다른 Chickxel이 받고 있으면
    /// 이 창은 받지 않는다(먼저 켠 쪽이 받음).
    /// </summary>
    private static void Listen()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                NamedPipeServerStream server;
                try
                {
                    server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return;
                }
                await using (server)
                {
                    try
                    {
                        await server.WaitForConnectionAsync().ConfigureAwait(false);
                        await server.WriteAsync(BitConverter.GetBytes(Environment.ProcessId)).ConfigureAwait(false);
                        if (await ReadLineAsync(server).ConfigureAwait(false) is { } line && Activation.Parse(line) is { } activation)
                            Dispatcher.UIThread.Post(() => Deliver(activation));
                        server.WriteByte(1);
                        server.Flush();
                    }
                    catch (IOException)
                    {
                        // 보내는 쪽이 중간에 끊었으면 다음 연결을 기다린다.
                    }
                }
            }
        });
    }

    /// <summary>줄 하나(4 KiB 안, 끝의 '\n' 전까지). 넘으면 null.</summary>
    private static async Task<string?> ReadLineAsync(Stream stream)
    {
        var buffer = new byte[4096];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length, 1)).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer[length] == (byte)'\n') return Encoding.UTF8.GetString(buffer, 0, length);
            length += read;
        }
        return null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
