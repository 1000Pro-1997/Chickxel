using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Chickxel.App.Ai;

/// <summary>설치된 AI CLI 버전과 npm에 올라온 최신 버전(최신이 더 높을 때만 만듦).</summary>
public sealed record AiCliUpdate(string Installed, string Latest);

/// <summary>
/// AI CLI 업데이트 확인: 설치된 CLI의 <c>--version</c>과 npm 레지스트리의 최신 버전(<see cref="AiProvider.NpmPackage"/>)을 견준다.
/// 실행 파일마다 앱을 켜 둔 동안 한 번만 확인하고, 네트워크·실행 오류는 "업데이트 없음"으로 본다(채팅을 막지 않음).
/// </summary>
public static partial class AiUpdateCheck
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Dictionary<string, Task<AiCliUpdate?>> Checks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>업데이트가 있으면 버전 둘, 없거나 알 수 없으면 null. 같은 실행 파일은 앱을 켜 둔 동안 같은 결과(UI 스레드에서 부름).</summary>
    public static Task<AiCliUpdate?> CheckAsync(AiProvider provider, string path)
    {
        if (!Checks.TryGetValue(path, out var task)) Checks[path] = task = RunAsync(provider, path);
        return task;
    }

    /// <summary>업데이트한 뒤 다시 확인하게 기억을 지운다.</summary>
    public static void Forget(string path) => Checks.Remove(path);

    private static async Task<AiCliUpdate?> RunAsync(AiProvider provider, string path)
    {
        try
        {
            var latestTask = LatestAsync(provider.NpmPackage);
            var run = await AiCli.RunAsync(path, ["--version"], CancellationToken.None, timeoutSeconds: 10).ConfigureAwait(false);
            var latest = await latestTask.ConfigureAwait(false);
            if (run.Error is not null || run.ExitCode != 0 || ParseVersion(run.Stdout) is not { } installed || latest is null) return null;
            return Version.Parse(latest) > Version.Parse(installed) ? new AiCliUpdate(installed, latest) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>npm 레지스트리의 <c>latest</c> 버전("1.2.3"), 읽을 수 없으면 null.</summary>
    private static async Task<string?> LatestAsync(string package)
    {
        var json = await Http.GetStringAsync($"https://registry.npmjs.org/{package}/latest").ConfigureAwait(false);
        return JsonNode.Parse(json)?["version"] is JsonValue v && v.TryGetValue(out string? s) ? ParseVersion(s) : null;
    }

    /// <summary>글 속 첫 "주.부.수" 버전("codex-cli 0.46.0" → "0.46.0", "2.1.3 (Claude Code)" → "2.1.3"). 없으면 null.</summary>
    internal static string? ParseVersion(string text) => VersionPattern().Match(text) is { Success: true } m ? m.Value : null;

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionPattern();
}
