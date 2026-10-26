using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Gpixel.App;

namespace GuideShots;

/// <summary>
/// 진입점: <c>dotnet run --project tools/GuideShots -- [id 앞부분 …] [--list]</c>. 인수가 없으면 모든 샷을 찍는다.
/// 샷마다 설정 폴더를 새로 만들어(GPIXEL_DATA_DIR) 사용자 설정을 건드리지 않고, 결과는 docs/assets/guide/&lt;이름&gt;.png.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        ShotKit.InstallTracking();
        if (Array.IndexOf(args, "--lang") is var li and >= 0 && li + 1 < args.Length)
        {
            ShotKit.Language = args[li + 1];
            args = [.. args.Where((_, i) => i != li && i != li + 1)];
        }
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
        var filters = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        int failed = 0, ran = 0;
        foreach (var (method, attr) in shots)
        {
            if (filters.Length > 0 && !filters.Any(f => attr!.Id.StartsWith(f, StringComparison.Ordinal))) continue;
            ran++;
            var kit = new ShotKit(attr!.Id);
            try
            {
                method.Invoke(null, [kit]);
                foreach (var file in kit.Saved) Console.WriteLine($"OK   {attr.Id} -> {file}");
                if (kit.Saved.Count == 0) Console.WriteLine($"WARN {attr.Id}: nothing saved");
            }
            catch (Exception ex)
            {
                failed++;
                var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
                Console.WriteLine($"FAIL {attr.Id}: {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
            }
            finally
            {
                kit.CloseAll();
            }
        }
        Console.WriteLine($"{ran} shot(s), {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}

/// <summary>샷 하나: <see cref="Id"/> = 기본 파일 이름(가이드 쪽 id, 여러 장이면 -2·-3…). 메서드는 <c>static void X(ShotKit k)</c>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ShotAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}
