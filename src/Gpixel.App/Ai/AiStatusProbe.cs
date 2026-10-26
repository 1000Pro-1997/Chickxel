using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Gpixel.App.Localization;

namespace Gpixel.App.Ai;

internal enum AiHealthLevel
{
    Pending,
    Ok,
    Info,
    Warning,
    Error,
}

internal sealed record AiServerHealth(AiHealthLevel Level, int ToolCount, string? Error);

internal sealed record AiProviderHealth(
    AiProvider Provider,
    string? Path,
    string? Version,
    string? CliError,
    AuthStatus? Auth,
    AiHealthLevel McpLevel,
    string? McpError);

/// <summary>AI 상태 창에서 쓰는 무비용 점검. 모델 요청은 보내지 않고 CLI 버전·로그인과 CLI의 MCP 목록 연결까지만 확인한다.</summary>
internal static class AiStatusProbe
{
    private const string ProtocolVersion = "2025-06-18";
    private const string CodexTokenVariable = "GPIXEL_MCP_TOKEN";

    /// <summary>앱이나 찾은 CLI 파일·직접 지정 경로가 바뀌면 달라지는, 상태 창 숨김용 지문.</summary>
    internal static string Fingerprint(AppSettings settings)
    {
        var text = new StringBuilder(AppInfo.VersionText);
        foreach (var provider in AiProvider.All)
        {
            var path = provider.Locate(settings);
            text.Append('|').Append(provider.Id).Append('|').Append(provider.GetPathOverride(settings)).Append('|').Append(path);
            if (path is null) continue;
            try
            {
                var file = new FileInfo(path);
                text.Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                text.Append("|unreadable");
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    internal static bool ShouldShow(AppSettings settings)
        => !string.Equals(settings.AiStatusDismissedFingerprint, Fingerprint(settings), StringComparison.Ordinal);

    /// <summary>Gpixel의 HTTP MCP 서버 자체가 initialize와 tools/list에 답하는지 확인한다.</summary>
    internal static async Task<AiServerHealth> CheckServerAsync(McpServer mcp, CancellationToken cancellationToken)
    {
        try
        {
            mcp.Start();
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            using var initialize = Request(mcp, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject { ["name"] = "gpixel-status", ["version"] = AppInfo.VersionText },
                },
            });
            using var initialized = await client.SendAsync(initialize, cancellationToken).ConfigureAwait(false);
            var initializeText = await initialized.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!initialized.IsSuccessStatusCode)
                return new AiServerHealth(AiHealthLevel.Error, 0, $"HTTP {(int)initialized.StatusCode}: {AiCli.Tail(initializeText, 300)}");

            var session = initialized.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.FirstOrDefault() : null;
            using var listRequest = Request(mcp, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 2,
                ["method"] = "tools/list",
                ["params"] = new JsonObject(),
            }, session);
            using var listed = await client.SendAsync(listRequest, cancellationToken).ConfigureAwait(false);
            var listText = await listed.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!listed.IsSuccessStatusCode)
                return new AiServerHealth(AiHealthLevel.Error, 0, $"HTTP {(int)listed.StatusCode}: {AiCli.Tail(listText, 300)}");
            var count = JsonNode.Parse(listText)?["result"]?["tools"] is JsonArray tools ? tools.Count : 0;
            return count > 0
                ? new AiServerHealth(AiHealthLevel.Ok, count, null)
                : new AiServerHealth(AiHealthLevel.Error, 0, UiStrings.AiStatus_NoMcpTools);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return new AiServerHealth(AiHealthLevel.Error, 0, e.Message);
        }
    }

    internal static async Task<AiProviderHealth> CheckProviderAsync(
        AiProvider provider, AppSettings settings, McpServer mcp, CancellationToken cancellationToken)
    {
        var path = provider.Locate(settings);
        if (path is null)
            return new AiProviderHealth(provider, null, null, null, null, AiHealthLevel.Error, null);

        var versionRun = await AiCli.RunAsync(path, ["--version"], cancellationToken, timeoutSeconds: 10).ConfigureAwait(false);
        var version = FirstLine(versionRun.Stdout, versionRun.Stderr);
        var cliError = versionRun.Error ?? (versionRun.ExitCode == 0 ? null : ExitFailure(versionRun, 300));
        if (version is null && cliError is null) cliError = UiStrings.AiStatus_NoVersionOutput;

        var auth = await provider.GetAuthStatusAsync(path, cancellationToken).ConfigureAwait(false);
        var (mcpLevel, mcpError) = await CheckCliMcpAsync(provider, path, mcp, cancellationToken).ConfigureAwait(false);
        return new AiProviderHealth(provider, path, version, cliError, auth, mcpLevel, mcpError);
    }

    internal static async Task<string?> LogoutAsync(AiProvider provider, string path, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> args = provider.Id == "claude" ? ["auth", "logout"] : ["logout"];
        var run = await AiCli.RunAsync(path, args, cancellationToken, timeoutSeconds: 30).ConfigureAwait(false);
        if (run.Error is { } error) return error;
        return run.ExitCode == 0 ? null : ExitFailure(run, 500);
    }

    private static async Task<(AiHealthLevel Level, string? Error)> CheckCliMcpAsync(
        AiProvider provider, string path, McpServer mcp, CancellationToken cancellationToken)
    {
        if (provider.Id == "claude")
        {
            // Claude의 관리 명령은 --mcp-config를 적용하지 않고 계정의 원격 MCP를 검사한다. 모델 호출 없이 할 수 있는 범위는
            // 실제 채팅이 쓸 엄격한 임시 설정 생성까지이며, 연결 여부는 첫 메시지의 init과 서버 요청 카운터로 확인한다.
            try { mcp.WriteMcpConfigFile(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return (AiHealthLevel.Error, e.Message);
            }
            return (AiHealthLevel.Info, null);
        }

        var setting = $"mcp_servers={{{McpServer.ServerName}={{url={CodexSession.TomlString(mcp.Url)},bearer_token_env_var={CodexSession.TomlString(CodexTokenVariable)}}}}}";
        var run = await AiCli.RunAsync(path, ["-c", setting, "mcp", "list", "--json"], cancellationToken, timeoutSeconds: 20,
                environment: new Dictionary<string, string> { [CodexTokenVariable] = mcp.Token })
            .ConfigureAwait(false);
        if (run.Error is not null) return (AiHealthLevel.Error, run.Error);
        if (run.ExitCode != 0) return (AiHealthLevel.Error, ExitFailure(run, 500));

        try
        {
            var configured = JsonNode.Parse(run.Stdout) is JsonArray servers
                && servers.OfType<JsonObject>().Any(server =>
                    string.Equals(AiCli.Str(server["name"]), McpServer.ServerName, StringComparison.Ordinal)
                    && string.Equals(AiCli.Str(server["transport"]?["url"]), mcp.Url, StringComparison.Ordinal));
            return configured
                ? (AiHealthLevel.Info, null)
                : (AiHealthLevel.Error, UiStrings.AiStatus_McpConfigMissing);
        }
        catch (System.Text.Json.JsonException)
        {
            return (AiHealthLevel.Error, UiStrings.AiStatus_McpConfigUnreadable);
        }
    }

    private static string ExitFailure(AiCli.RunResult run, int maxLength)
    {
        var detail = AiCli.Tail((run.Stderr + "\n" + run.Stdout).Trim(), maxLength);
        return string.IsNullOrWhiteSpace(detail)
            ? UiStrings.FormatAiStatus_CliExitNoOutput(code: run.ExitCode)
            : UiStrings.FormatAiStatus_CliExit(code: run.ExitCode, detail: detail);
    }

    private static HttpRequestMessage Request(McpServer mcp, JsonObject body, string? session = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, mcp.Url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcp.Token);
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        if (session is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        return request;
    }

    private static string? FirstLine(params string[] values)
        => values.SelectMany(v => AiCli.StripAnsi(v).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .FirstOrDefault();
}
