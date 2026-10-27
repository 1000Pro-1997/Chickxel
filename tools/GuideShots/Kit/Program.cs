using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Gpixel.App;

namespace GuideShots;

/// <summary>
/// 진입점: <c>dotnet run --project tools/GuideShots -- [id 앞부분 …] [--list]</c>. 인수가 없으면 모든 샷을 찍는다.
/// <c>--shortcuts &lt;파일&gt;</c>은 샷 대신 앱의 기본 단축키(설정 창 "내보내기(JSON)"와 같은 모양)를 써서 홈페이지 가이드가 쓰게 한다
/// (<c>docs/i18n/shortcuts.json</c>, <c>tools/build-docs.py</c>의 <c>{{key:명령}}</c>).
/// 샷마다 설정 폴더를 새로 만들어(GPIXEL_DATA_DIR) 사용자 설정을 건드리지 않고, 결과는 docs/assets/guide/&lt;이름&gt;.png.
/// 샷은 하나씩 따로 된 프로세스(<c>--only &lt;id&gt;</c>)에서 찍는다 — 앞 샷이 남긴 창·타이머·폴더 감시가 다음 샷을 멈추게 하지 않게.
/// </summary>
public static class Program
{
    /// <summary>샷 하나가 이보다 오래 걸리면 멈춘 것으로 보고 끝낸다.</summary>
    private static readonly TimeSpan ShotTimeout = TimeSpan.FromMinutes(4);

    public static int Main(string[] args)
    {
        var shots = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(m => (Method: m, Attr: m.GetCustomAttribute<ShotAttribute>()))
            .Where(s => s.Attr is not null)
            .OrderBy(s => s.Attr!.Id, StringComparer.Ordinal)
            .ToList();
        if (args.Contains("--list"))
        {
            foreach (var s in shots) Console.WriteLine(s.Attr!.Id);
            return 0;
        }
        if (Array.IndexOf(args, "--shortcuts") is var si and >= 0 && si + 1 < args.Length)
            return ExportShortcuts(args[si + 1]);
        if (Array.IndexOf(args, "--only") is var oi and >= 0 && oi + 1 < args.Length)
        {
            var shot = shots.FirstOrDefault(s => s.Attr!.Id == args[oi + 1]);
            if (shot.Method is null)
            {
                Console.WriteLine($"FAIL {args[oi + 1]}: no such shot");
                return 1;
            }
            return RunHere(shot.Method, shot.Attr!.Id) ? 0 : 1;
        }

        var filters = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        int failed = 0, ran = 0;
        foreach (var (_, attr) in shots)
        {
            if (filters.Length > 0 && !filters.Any(f => attr!.Id.StartsWith(f, StringComparison.Ordinal))) continue;
            ran++;
            if (!RunChild(attr!.Id)) failed++;
        }
        Console.WriteLine($"{ran} shot(s), {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>이 프로세스에서 샷 하나를 찍는다.</summary>
    private static bool RunHere(MethodInfo method, string id)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        ShotKit.InstallTracking();
        ShotKit.UseWindowsLook();
        var kit = new ShotKit(id);
        try
        {
            method.Invoke(null, [kit]);
            foreach (var file in kit.Saved) Console.WriteLine($"OK   {id} -> {file}");
            if (kit.Saved.Count == 0) Console.WriteLine($"WARN {id}: nothing saved");
            return true;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
            Console.WriteLine($"FAIL {id}: {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
            return false;
        }
        finally
        {
            kit.CloseAll();
        }
    }

    /// <summary>메인 창을 화면 없이 만들어 등록된 모든 명령의 기본 단축키를 JSON으로 쓴다(바뀌었을 때만).</summary>
    private static int ExportShortcuts(string path)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        ShotKit.UseWindowsLook();
        var kit = new ShotKit("shortcuts");
        try
        {
            var main = kit.Main();
            var commands = ShotKit.Get<System.Collections.IEnumerable>(main, "_commands").Cast<object>()
                .Select(c => ShotKit.Get<ShortcutCommand>(c, "Info"));
            var json = Shortcuts.ToJson(commands, new Dictionary<string, List<KeyChord>>()) + "\n";
            var full = Path.GetFullPath(path);
            if (File.Exists(full) && File.ReadAllText(full) == json)
            {
                Console.WriteLine($"unchanged {path}");
                return 0;
            }
            File.WriteAllText(full, json);
            Console.WriteLine($"wrote {path}");
            return 0;
        }
        finally
        {
            kit.CloseAll();
        }
    }

    /// <summary>같은 프로그램을 <c>--only &lt;id&gt;</c>로 띄워 샷 하나를 찍고 출력을 그대로 옮긴다.</summary>
    private static bool RunChild(string id)
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        var psi = new ProcessStartInfo(self) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // `dotnet GuideShots.dll`로 돌고 있으면 dll을 다시 넘긴다(`dotnet run`·실행 파일이면 그대로).
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(typeof(Program).Assembly.Location);
        psi.ArgumentList.Add("--only");
        psi.ArgumentList.Add(id);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start " + self);
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(ShotTimeout))
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit();
            Console.WriteLine($"FAIL {id}: timed out after {ShotTimeout.TotalMinutes:0} min");
            return false;
        }
        p.WaitForExit();
        return p.ExitCode == 0;
    }
}

/// <summary>샷 하나: <see cref="Id"/> = 기본 파일 이름(가이드 쪽 id, 여러 장이면 -2·-3…). 메서드는 <c>static void X(ShotKit k)</c>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ShotAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}
