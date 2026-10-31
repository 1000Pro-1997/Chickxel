using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

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
    private const string CodexTokenVariable = "CHICKXEL_MCP_TOKEN";

    /// <summary>
    /// 상태 창 숨김용 지문: 사용할 AI CLI 경로나 직접 지정한 경로가 바뀔 때만 달라진다.
    /// 앱 업데이트나 CLI 자체 업데이트(같은 경로의 파일 교체)로는 다시 띄우지 않는다.
    /// </summary>
    internal static string Fingerprint(AppSettings settings)
    {
        var text = new StringBuilder();
        foreach (var provider in AiProvider.All)
            text.Append('|').Append(provider.Id).Append('|').Append(provider.GetPathOverride(settings)).Append('|').Append(provider.Locate(settings));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    internal static bool ShouldShow(AppSettings settings)
        => !string.Equals(settings.AiStatusDismissedFingerprint, Fingerprint(settings), StringComparison.Ordinal);

    /// <summary>Chickxel의 HTTP MCP 서버 자체가 initialize와 tools/list에 답하는지 확인한다.</summary>
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
                    ["clientInfo"] = new JsonObject { ["name"] = "chickxel-status", ["version"] = AppInfo.VersionText },
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

    private sealed record CandidateHealth(string Path, string? Version, string? CliError, AiHealthLevel McpLevel, string? McpError);

    internal static async Task<AiProviderHealth> CheckProviderAsync(
        AiProvider provider, AppSettings settings, McpServer mcp, CancellationToken cancellationToken)
    {
        CandidateHealth? first = null, firstRunnable = null, selected = null;
        foreach (var path in provider.LocateCandidates(settings))
        {
            var health = await CheckCandidateAsync(provider, path, mcp, cancellationToken).ConfigureAwait(false);
            first ??= health;
            if (health.CliError is not null) continue;
            firstRunnable ??= health;
            if (health.McpLevel == AiHealthLevel.Error) continue;
            selected = health;
            break;
        }
        selected ??= firstRunnable ?? first;
        if (selected is null)
            return new AiProviderHealth(provider, null, null, null, null, AiHealthLevel.Error, null);

        if (selected.CliError is null && selected.McpLevel != AiHealthLevel.Error)
            RememberWorkingPath(provider, settings, selected.Path);
        var auth = await provider.GetAuthStatusAsync(selected.Path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new AiProviderHealth(provider, selected.Path, selected.Version, selected.CliError, auth, selected.McpLevel, selected.McpError);
    }

    private static async Task<CandidateHealth> CheckCandidateAsync(
        AiProvider provider, string path, McpServer mcp, CancellationToken cancellationToken)
    {
        var run = await AiCli.RunAsync(path, ["--version"], cancellationToken, timeoutSeconds: 10).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var version = FirstLine(run.Stdout, run.Stderr);
        var error = run.Error ?? (run.ExitCode == 0 ? null : ExitFailure(run, 300));
        if (version is null && error is null) error = UiStrings.AiStatus_NoVersionOutput;
        if (error is not null)
            return new CandidateHealth(path, version, error, AiHealthLevel.Error, UiStrings.AiStatus_McpUnavailable);
        var (mcpLevel, mcpError) = await CheckCliMcpAsync(provider, path, mcp, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new CandidateHealth(path, version, null, mcpLevel, mcpError);
    }

    /// <summary>직접 지정 경로가 망가졌거나 먼저 찾은 자동 후보가 실행되지 않으면, 확인된 후보를 이후 채팅도 쓰도록 설정을 고친다.</summary>
    private static void RememberWorkingPath(AiProvider provider, AppSettings settings, string path)
    {
        var configured = provider.GetPathOverride(settings);
        if (!string.IsNullOrWhiteSpace(configured) && AiCli.SamePath(AiCli.ExpandHome(configured.Trim().Trim('"')), path)) return;
        var automatic = provider.LocateAutomatically();
        provider.SetPathOverride(settings, AiCli.SamePath(automatic, path) ? null : path);
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
            return await CheckClaudeMcpAsync(path, mcp, cancellationToken).ConfigureAwait(false);

        try { mcp.Start(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return (AiHealthLevel.Error, e.Message);
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

    /// <summary>
    /// 모델 요청이나 메시지를 보내지 않고 실제 Claude 프로세스를 채팅과 같은 stream-json 모드로 띄운다.
    /// 명시한 임시 MCP만 읽게 한 뒤 Chickxel 서버가 tools/list를 받으면 실제 CLI 연결 성공이다.
    /// 빈 입력을 유지하므로 AI 사용량이 들지 않고, 사용자 hook은 상태 점검에서 실행하지 않는다.
    /// </summary>
    private static async Task<(AiHealthLevel Level, string? Error)> CheckClaudeMcpAsync(
        string path, McpServer mcp, CancellationToken cancellationToken)
    {
        string config;
        try { config = mcp.WriteMcpConfigFile(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return (AiHealthLevel.Error, e.Message);
        }

        var since = mcp.Counters;
        var args = new[]
        {
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose",
            "--disable-slash-commands",
            "--setting-sources", "",
            "--strict-mcp-config",
            "--mcp-config", config,
            "--allowedTools", "mcp__" + McpServer.ServerName,
            "--tools", "",
        };
        using var process = new Process { StartInfo = AiCli.CreateStartInfo(path, args, AiCli.Home, true) };
        var stderr = new StringBuilder();
        Task<string>? stdoutDone = null;
        Task? stderrDone = null;
        try
        {
            process.Start();
            stdoutDone = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            stderrDone = AiCli.CollectErrorTail(process, stderr);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                while (!mcp.ToolsListedSince(since) && !process.HasExited)
                    await Task.Delay(50, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 아래 진단에서 요청이 아예 없었는지, 인증이 틀렸는지 구분한다.
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (mcp.ToolsListedSince(since)) return (AiHealthLevel.Ok, null);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return (AiHealthLevel.Error, e.Message);
        }
        finally
        {
            AiCli.Kill(process);
            if (stdoutDone is not null)
                try { await stdoutDone.ConfigureAwait(false); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
            if (stderrDone is not null)
                try { await stderrDone.ConfigureAwait(false); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
            mcp.ReleaseMcpConfigFile(config);
        }
        var problem = mcp.DescribeProblem(since, null);
        var detail = AiCli.Tail(stderr.ToString().Trim(), 400);
        return (AiHealthLevel.Error, detail.Length == 0 ? problem : problem + "\n" + detail);
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
