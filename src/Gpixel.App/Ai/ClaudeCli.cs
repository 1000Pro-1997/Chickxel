using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Gpixel.App.Localization;

namespace Gpixel.App.Ai;

/// <summary>
/// Claude Code CLI(<c>claude</c>): 로그인 상태는 <c>claude auth status --json</c>, 대화는 <see cref="ClaudeSession"/>.
/// 모델 목록은 Claude Code가 받아 둔 모델 목록(<see cref="ClaudeCatalog"/>)의 주요 모델을 실제 ID와 이름("Opus 5.5")으로 보인다.
/// 목록 파일이 없으면 별칭(opus·sonnet·haiku)을 보이고, 세션 시작 때 Claude Code가 알려 주는 실제 모델 ID를
/// <see cref="AppSettings.ClaudeResolvedModels"/>에 기억해 버전까지 보인다.
/// </summary>
public sealed partial class ClaudeProvider : AiProvider
{
    /// <summary>기본값(모델을 넘기지 않음)의 기억 열쇠 접두어. 뒤에 Claude Code 설정의 모델 값을 붙여, 설정을 바꾸면 따로 기억한다.</summary>
    private const string DefaultKeyPrefix = "default:";

    private static readonly (string Alias, string Hint, string Tip)[] Aliases =
    [
        ("opus", UiStrings.S_ClaudeCli_Aliases_FB8E7B5244, UiStrings.S_ClaudeCli_Aliases_E680EAD01B),
        ("sonnet", UiStrings.S_ClaudeCli_Aliases_52F235016A, UiStrings.S_ClaudeCli_Aliases_42281164DA),
        ("haiku", UiStrings.S_ClaudeCli_Aliases_32AA2E3322, UiStrings.S_ClaudeCli_Aliases_7FC8C1B69A),
    ];

    private static bool IsAlias(string model) => Aliases.Any(a => string.Equals(a.Alias, BaseModel(model), StringComparison.OrdinalIgnoreCase));

    /// <summary>"[1m]" 같은 문맥 길이 꼬리표를 뗀 모델 값.</summary>
    private static string BaseModel(string model) => model.Trim() is var m && m.IndexOf('[') is var i and > 0 ? m[..i] : model.Trim();

    public override string Id => "claude";
    public override string Name => "Claude";
    public override string ProductName => "Claude Code";
    public override string Command => "claude";
    public override string AccountText => UiStrings.S_ClaudeCli_AccountText_B2D662EB6E;
    public override IReadOnlyList<string> LoginArgs { get; } = ["auth", "login"];
    public override bool LoginTakesCode => true;
    public override string PathExample => UiStrings.S_ClaudeCli_PathExample_1B14AE488B;

    public override IReadOnlyList<(string Title, string Command)> InstallCommands
    {
        get
        {
            (string, string) unix = (UiStrings.S_ClaudeCli_InstallCommands_70BD2CD707, "curl -fsSL https://claude.ai/install.sh | bash");
            (string, string) win = ("Windows (PowerShell)", "irm https://claude.ai/install.ps1 | iex");
            return OperatingSystem.IsWindows() ? [win, unix] : [unix, win];
        }
    }

    public override string? GetPathOverride(AppSettings settings) => settings.ClaudePath;
    public override void SetPathOverride(AppSettings settings, string? path) => settings.ClaudePath = path;
    public override string? GetModel(AppSettings settings) => settings.AiModel;
    public override void SetModel(AppSettings settings, string? model) => settings.AiModel = model;

    public override async Task<AuthStatus> GetAuthStatusAsync(string path, CancellationToken cancellationToken = default)
    {
        var run = await AiCli.RunAsync(path, ["auth", "status", "--json"], cancellationToken).ConfigureAwait(false);
        if (run.Error is { } error) return new AuthStatus(false, null, null, null, error);
        return ParseAuthStatus(run.Stdout) ?? new AuthStatus(false, null, null, null, AiCli.Tail((run.Stderr + "\n" + run.Stdout).Trim(), 600));
    }

    private static AuthStatus? ParseAuthStatus(string stdout)
    {
        int start = stdout.IndexOf('{'), end = stdout.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            if (JsonNode.Parse(stdout[start..(end + 1)]) is not JsonObject o) return null;
            return new AuthStatus(AiCli.Bool(o["loggedIn"]) ?? false, AiCli.Str(o["email"]), AiCli.Str(o["subscriptionType"]), AiCli.Str(o["authMethod"]), null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public override IReadOnlyList<ModelChoice> Models(AppSettings settings)
    {
        var resolved = settings.ClaudeResolvedModels ?? [];
        var catalog = ClaudeCatalog.Load(ConfigDir());
        var configured = ConfiguredModel();
        string? Learned(string key) => resolved.TryGetValue(key, out var id) ? Name(id) : null;
        // 모델 값 → 이름: 목록의 이름(별칭은 가리키는 모델) → 기억한 실제 모델 → ID 모양으로 짐작.
        string Name(string model)
        {
            var trimmed = model.Trim();
            var id = BaseModel(trimmed);
            var ctx = trimmed.Length > id.Length ? $" ({trimmed[(id.Length + 1)..].TrimEnd(']').ToUpperInvariant()})" : "";
            if (catalog?.AliasTargets.TryGetValue(id.ToLowerInvariant(), out var target) == true) id = target;
            return (catalog?.Names.TryGetValue(id, out var name) == true ? name : PrettyName(id)) + ctx;
        }

        var defaultName = Learned(DefaultKeyPrefix + (configured ?? ""))
                          ?? (configured is not null ? Name(configured) : catalog?.DefaultModel is { } d ? Name(d) : null);
        var list = new List<ModelChoice>
        {
            new(null, defaultName is not null ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ClaudeCli_Models_C32F36E3D8,defaultName) : UiStrings.S_ClaudeCli_Models_497C1B2930,
                configured is not null
                    ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ClaudeCli_Models_576D372047,configured)
                    : UiStrings.S_ClaudeCli_Models_A14F4A45A8),
        };
        if (catalog is { Main.Count: > 0 })
        {
            foreach (var m in catalog.Main)
            {
                var alias = Aliases.FirstOrDefault(a => a.Alias == m.Family);
                list.Add(new ModelChoice(m.Id, alias.Hint is { } hint ? $"{m.Name} · {hint}" : m.Name,
                    alias.Tip is { } tip ? $"{tip} ({m.Id})" : $"{m.Description} ({m.Id})".Trim()));
            }
        }
        else
        {
            foreach (var (alias, hint, tip) in Aliases)
                list.Add(new ModelChoice(alias, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ClaudeCli_Models_BAF4B07362,Learned(alias) ?? PrettyName(alias) + UiStrings.S_ClaudeCli_Models_4B9F38E324,hint), tip));
        }
        if (settings.AiModel is { Length: > 0 } custom && list.All(c => c.Value != custom))
        {
            var name = Learned(custom) ?? Name(custom);
            list.Add(new ModelChoice(custom, IsAlias(custom) ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ClaudeCli_Models_24B7B5B99B,name,BaseModel(custom)) : name,
                IsAlias(custom) ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ClaudeCli_Models_2569D19437,custom) : UiStrings.S_ClaudeCli_Models_529BDF05AB));
        }
        return list;
    }

    public override bool RememberResolved(AppSettings settings, string? requested, string actual)
    {
        var key = requested ?? DefaultKeyPrefix + (ConfiguredModel() ?? "");
        settings.ClaudeResolvedModels ??= [];
        if (settings.ClaudeResolvedModels.TryGetValue(key, out var old) && old == actual) return false;
        settings.ClaudeResolvedModels[key] = actual;
        return true;
    }

    public override IChatSession CreateSession(string path, McpServer mcp) => new ClaudeSession(path, mcp);

    /// <summary>Claude Code 설정 폴더(<c>$CLAUDE_CONFIG_DIR</c> 또는 <c>~/.claude</c>).</summary>
    private static string ConfigDir()
        => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } custom ? AiCli.ExpandHome(custom) : Path.Combine(AiCli.Home, ".claude");

    /// <summary>
    /// 모델을 넘기지 않을 때 Claude Code가 쓰는 설정 값: 환경 변수 <c>ANTHROPIC_MODEL</c> → 사용자 설정
    /// (<c>$CLAUDE_CONFIG_DIR/settings.json</c> 또는 <c>~/.claude/settings.json</c>)의 <c>model</c>. 없으면 null(요금제 기본값).
    /// </summary>
    private static string? ConfiguredModel()
    {
        if (Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") is { Length: > 0 } env) return env.Trim();
        try
        {
            var file = Path.Combine(ConfigDir(), "settings.json");
            if (!File.Exists(file)) return null;
            return JsonNode.Parse(File.ReadAllText(file)) is JsonObject o && AiCli.Str(o["model"]) is { Length: > 0 } model ? model.Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 모델 ID·별칭을 사람이 읽는 이름으로: <c>claude-opus-5-5-20260901</c> → "Opus 5.5", <c>claude-3-5-sonnet-20241022</c> → "Sonnet 3.5",
    /// <c>opus[1m]</c> → "Opus (1M)". 모르는 형식은 그대로.
    /// </summary>
    public static string PrettyName(string model)
    {
        var m = ModelIdPattern().Match(model.Trim());
        if (!m.Success) return model;
        var family = m.Groups["fam"].Value;
        var name = char.ToUpperInvariant(family[0]) + family[1..];
        if (m.Groups["maj"].Success)
            name += " " + m.Groups["maj"].Value + (m.Groups["min"].Success ? "." + m.Groups["min"].Value : "");
        if (m.Groups["ctx"].Success) name += $" ({m.Groups["ctx"].Value.ToUpper(CultureInfo.InvariantCulture)})";
        return name;
    }

    [GeneratedRegex(@"^(?:claude-(?:(?<fam>opus|sonnet|haiku)-(?<maj>\d+)(?:-(?<min>\d{1,2}))?|(?<maj>\d+)(?:-(?<min>\d{1,2}))?-(?<fam>opus|sonnet|haiku))(?:-\d{8})?|(?<fam>opus|sonnet|haiku))(?:\[(?<ctx>\d+[km])\])?$",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex ModelIdPattern();
}

/// <summary>
/// Claude Code가 받아 두는 모델 목록(<c>~/.claude/cache/model-catalog/*.json</c>, 가장 최근 것): 주요 모델(<c>section = "main"</c>),
/// 모든 모델의 ID → 이름, 별칭(opus·sonnet·haiku)이 가리키는 모델, 설정이 없을 때의 기본 모델.
/// Claude Code 내부 파일이라 모양이 바뀌거나 없으면 null(별칭 목록으로 돌아감).
/// </summary>
public sealed record ClaudeCatalog(
    IReadOnlyList<(string Id, string Name, string Family, string Description)> Main,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyDictionary<string, string> AliasTargets,
    string? DefaultModel)
{
    public static ClaudeCatalog? Load(string configDir)
    {
        try
        {
            var dir = Path.Combine(configDir, "cache", "model-catalog");
            if (!Directory.Exists(dir)) return null;
            ClaudeCatalog? best = null;
            double bestAt = double.MinValue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject root || root["catalog"] is not JsonObject cat) continue;
                double at = root["fetchedAt"] is JsonValue v && v.TryGetValue(out double d) ? d : 0;
                if (at < bestAt || Parse(cat) is not { } parsed) continue;
                best = parsed;
                bestAt = at;
            }
            return best;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ClaudeCatalog? Parse(JsonObject cat)
    {
        if (cat["config"] is not JsonObject config || config["models"] is not JsonArray models) return null;
        var main = new List<(string, string, string, string)>();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in models)
        {
            if (node is not JsonObject m || AiCli.Str(m["id"]) is not { Length: > 0 } id || AiCli.Str(m["name"]) is not { Length: > 0 } name) continue;
            names[id] = name;
            if (AiCli.Str(m["section"]) == "main")
                main.Add((id, name, AiCli.Str(m["runtime"]?["family"]) ?? "", AiCli.Str(m["description"]) ?? ""));
        }
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (config["provider_alias_targets"] is JsonObject targets)
            foreach (var (alias, node) in targets)
                if (AiCli.Str(node?["default"]) is { Length: > 0 } target) aliases[alias] = target;
        return names.Count == 0 ? null : new ClaudeCatalog(main, names, aliases, AiCli.Str(cat["state"]?["model"]));
    }
}

/// <summary>
/// 오래 살아 있는 <c>claude -p</c> stream-json 프로세스 하나(대화 하나).
/// 첫 <see cref="Send"/> 때 띄우고, <see cref="Cancel"/>·<see cref="Restart"/>로 끝내면 다음 <see cref="Send"/>가
/// <c>--resume &lt;세션&gt;</c>으로 같은 대화를 이어서 다시 띄운다. 이어 가기에 실패하면 새 대화로 같은 메시지를 다시 보낸다.
/// 출력은 별도 스레드에서 읽고, 이벤트는 모두 UI 스레드에서 발생한다(끝낸 프로세스의 이벤트는 버림).
/// </summary>
public sealed class ClaudeSession : IChatSession
{
    private const string ToolPrefix = "mcp__" + McpServer.ServerName + "__";

    private readonly string _claudePath;
    private readonly McpServer _mcp;
    private Process? _process;
    private StreamWriter? _stdin;
    private Task _writes = Task.CompletedTask;
    /// <summary>프로세스 세대. 끝낸 프로세스에서 늦게 온 이벤트를 걸러 낸다.</summary>
    private int _generation;
    private string? _sessionCwd, _pendingLine;
    private bool _resuming, _sawInit;
    /// <summary>이 프로세스를 띄울 때의 MCP 요청 수와, 연결 문제를 이미 알렸거나 연결을 확인했는지.</summary>
    private McpCounters _mcpSince;
    private bool _mcpChecked;
    /// <summary>Claude Code가 init에서 알려 준 gpixel 서버 상태("connected"·"pending"·"failed" 등, 목록에 없으면 "목록에 없음").</summary>
    private string? _mcpStatus;

    public ClaudeSession(string claudePath, McpServer mcp)
    {
        _claudePath = claudePath;
        _mcp = mcp;
    }

    /// <summary>모델 별칭(null = Claude Code 기본값). 다음에 프로세스를 띄울 때 적용된다.</summary>
    public string? Model { get; set; }

    /// <summary>새 대화를 시작할 때의 작업 폴더(없으면 홈). 이어 가는 대화는 처음 폴더를 그대로 쓴다(세션 기록 위치).</summary>
    public string? WorkingDirectory { get; set; }

    public string? SessionId { get; private set; }

    /// <summary>세션을 시작한 작업 폴더(Claude Code의 세션 기록이 그 폴더에 묶임 — 이어 갈 때 같은 폴더여야 함).</summary>
    public string? SessionDirectory => SessionId is null ? null : _sessionCwd;

    /// <summary>답을 기다리는 중.</summary>
    public bool IsTurnActive { get; private set; }

    /// <summary>답 글 블록이 새로 시작됨(스트리밍).</summary>
    public event Action? TextStarted;
    /// <summary>스트리밍 중인 답 글 조각.</summary>
    public event Action<string>? TextDelta;
    /// <summary>
    /// 응답 블록이 시작됨(진행 표시용): 종류("thinking" 생각 / "tool_use" 도구 호출 작성 / "text" 답 글)와 도구 이름(도구일 때, 접두어 뺌).
    /// </summary>
    public event Action<string, string?>? BlockStarted;
    /// <summary>지금 블록에 받은 글자 수(생각·도구 입력 JSON·답 글 조각마다, 진행 표시용).</summary>
    public event Action<int>? BlockProgress;
    /// <summary>다 받은 답 글 블록.</summary>
    public event Action<string>? AssistantText;
    public event Action<ToolUseInfo>? ToolUse;
    public event Action<ToolResultInfo>? ToolResult;
    public event Action<TurnResult>? TurnCompleted;
    public event Action<string>? ModelResolved;
    /// <summary>프로세스가 뜻밖에 끝남(차례 중이었으면 그 차례도 끝남).</summary>
    public event Action<ExitInfo>? Exited;
    /// <summary>사용자에게 알릴 짧은 안내(이어 가기 실패 등).</summary>
    public event Action<string>? Notice;
    public event Action<string>? McpProblem;

    /// <summary>
    /// 메시지(글 + PNG 이미지)를 보낸다. 프로세스가 없으면 띄운다(실행 실패는 예외: Win32Exception 등).
    /// 쓰기는 백그라운드에서 한다.
    /// </summary>
    public void Send(string text, IReadOnlyList<byte[]> pngImages)
    {
        var content = new JsonArray();
        foreach (var png in pngImages)
        {
            content.Add(new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = Convert.ToBase64String(png) },
            });
        }
        if (text.Length > 0 || content.Count == 0) content.Add(new JsonObject { ["type"] = "text", ["text"] = text.Length > 0 ? text : UiStrings.S_ClaudeCli_Send_B72FA88EEA });
        var line = new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = content },
        }.ToJsonString();
        if (_process is null) StartProcess();
        _pendingLine = line;
        IsTurnActive = true;
        Write(line);
    }

    /// <summary>지금 차례를 멈춘다(프로세스를 끝냄). 다음 <see cref="Send"/>는 같은 대화를 이어서 다시 띄운다.</summary>
    public void Cancel()
    {
        KillProcess();
        IsTurnActive = false;
        _pendingLine = null;
    }

    /// <summary>설정(모델 등)을 바꾼 뒤: 프로세스를 끝내고 다음 <see cref="Send"/>에서 같은 대화를 이어 다시 띄운다.</summary>
    public void Restart() => Cancel();

    /// <summary>새 대화: 프로세스를 끝내고 세션을 잊는다.</summary>
    public void Reset()
    {
        Cancel();
        SessionId = null;
        _sessionCwd = null;
    }

    public void Resume(string sessionId, string? directory)
    {
        Cancel();
        SessionId = sessionId;
        _sessionCwd = directory;
    }

    public void Dispose() => Cancel();

    private void StartProcess()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cwd = (SessionId is not null ? _sessionCwd : null) ?? WorkingDirectory ?? home;
        if (!Directory.Exists(cwd)) cwd = home;
        var args = new List<string>
        {
            "-p",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--disable-slash-commands",
            "--setting-sources", "user",
            "--system-prompt-file", WriteSystemPromptFile(),
            "--strict-mcp-config",
            "--mcp-config", _mcp.WriteMcpConfigFile(),
            "--allowedTools", "mcp__" + McpServer.ServerName,
            "--tools", "",
        };
        if (!string.IsNullOrEmpty(Model)) args.AddRange(["--model", Model]);
        if (SessionId is not null) args.AddRange(["--resume", SessionId]);

        var psi = AiCli.CreateStartInfo(_claudePath, args, cwd, true);
        var p = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr)
            {
                stderr.AppendLine(e.Data);
                if (stderr.Length > 8000) stderr.Remove(0, stderr.Length - 4000);
            }
        };
        _mcpSince = _mcp.Counters;
        _mcpChecked = false;
        _mcpStatus = null;
        p.Start();
        p.BeginErrorReadLine();
        int gen = ++_generation;
        _process = p;
        _stdin = p.StandardInput;
        _resuming = SessionId is not null;
        _sawInit = false;
        if (SessionId is null) _sessionCwd = cwd;
        var reader = new Thread(() => ReadLoop(p, gen, stderr)) { IsBackground = true, Name = "claude-stdout" };
        reader.Start();
    }

    private static string WriteSystemPromptFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gpixel-claude");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "system-prompt.txt");
        File.WriteAllText(file, AiCli.SystemPrompt, new UTF8Encoding(false));
        return file;
    }

    private void Write(string line)
    {
        if (_stdin is not { } w) return;
        _writes = _writes.ContinueWith(_ =>
        {
            try
            {
                w.Write(line);
                w.Write('\n');
                w.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // 프로세스가 죽었으면 읽기 스레드가 Exited로 알린다.
            }
        }, TaskScheduler.Default);
    }

    private void KillProcess()
    {
        _generation++;
        var p = _process;
        _process = null;
        _stdin = null;
        if (p is not null) AiCli.Kill(p);
    }

    /// <summary>표준 출력 JSONL을 읽는 스레드. 한 줄씩 파싱해 UI 스레드로 넘긴다.</summary>
    private void ReadLoop(Process p, int gen, StringBuilder stderr)
    {
        try
        {
            var stdout = p.StandardOutput;
            string? line;
            while ((line = stdout.ReadLine()) is not null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                JsonObject? obj;
                try
                {
                    obj = JsonNode.Parse(line) as JsonObject;
                }
                catch (JsonException)
                {
                    continue;
                }
                if (obj is not null) Dispatcher.UIThread.Post(() => Handle(gen, obj));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
        int code;
        try
        {
            p.WaitForExit();
            code = p.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            code = -1;
        }
        string tail;
        lock (stderr) tail = AiCli.Tail(stderr.ToString().Trim(), 1500);
        Dispatcher.UIThread.Post(() =>
        {
            p.Dispose();
            OnExit(gen, code, tail);
        });
    }

    private void OnExit(int gen, int code, string stderrTail)
    {
        if (gen != _generation) return;
        _process = null;
        _stdin = null;
        if (_resuming && !_sawInit && _pendingLine is not null && IsTurnActive)
        {
            RestartFresh();
            return;
        }
        IsTurnActive = false;
        _pendingLine = null;
        Exited?.Invoke(new ExitInfo(code, stderrTail));
    }

    /// <summary>이전 대화를 이어 가지 못함(세션 기록 없음 등): 새 대화로 같은 메시지를 다시 보낸다.</summary>
    private void RestartFresh()
    {
        var line = _pendingLine!;
        KillProcess();
        SessionId = null;
        _sessionCwd = null;
        Notice?.Invoke(UiStrings.S_ClaudeCli_RestartFresh_821977AA3D);
        try
        {
            StartProcess();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            IsTurnActive = false;
            _pendingLine = null;
            Exited?.Invoke(new ExitInfo(-1, ex.Message));
            return;
        }
        Write(line);
    }

    private void Handle(int gen, JsonObject o)
    {
        if (gen != _generation) return;
        switch (AiCli.Str(o["type"]))
        {
            case "system":
                if (AiCli.Str(o["subtype"]) == "init")
                {
                    _sawInit = true;
                    SetSessionId(AiCli.Str(o["session_id"]));
                    if (AiCli.Str(o["model"]) is { Length: > 0 } model) ModelResolved?.Invoke(model);
                    CheckMcpStatus(o["mcp_servers"]);
                }
                break;
            case "stream_event":
                if (o["parent_tool_use_id"] is JsonValue) break; // 하위 에이전트 출력은 보이지 않는다.
                if (o["event"] is not JsonObject ev) break;
                switch (AiCli.Str(ev["type"]))
                {
                    case "content_block_start" when ev["content_block"] is JsonObject block:
                        var kind = AiCli.Str(block["type"]) ?? "";
                        var blockName = AiCli.Str(block["name"]);
                        if (blockName is not null && blockName.StartsWith(ToolPrefix, StringComparison.Ordinal)) blockName = blockName[ToolPrefix.Length..];
                        BlockStarted?.Invoke(kind, blockName);
                        if (kind == "text") TextStarted?.Invoke();
                        break;
                    case "content_block_delta" when ev["delta"] is JsonObject delta:
                        switch (AiCli.Str(delta["type"]))
                        {
                            case "text_delta":
                                if (AiCli.Str(delta["text"]) is { Length: > 0 } t)
                                {
                                    BlockProgress?.Invoke(t.Length);
                                    TextDelta?.Invoke(t);
                                }
                                break;
                            case "thinking_delta":
                                BlockProgress?.Invoke(AiCli.Str(delta["thinking"])?.Length ?? 0);
                                break;
                            case "input_json_delta":
                                BlockProgress?.Invoke(AiCli.Str(delta["partial_json"])?.Length ?? 0);
                                break;
                        }
                        break;
                }
                break;
            case "assistant":
                if (o["parent_tool_use_id"] is JsonValue) break;
                if (o["message"]?["content"] is not JsonArray parts) break;
                foreach (var part in parts.OfType<JsonObject>())
                {
                    switch (AiCli.Str(part["type"]))
                    {
                        case "text":
                            if (AiCli.Str(part["text"]) is { } text) AssistantText?.Invoke(text);
                            break;
                        case "tool_use":
                            var name = AiCli.Str(part["name"]) ?? "?";
                            if (name.StartsWith(ToolPrefix, StringComparison.Ordinal)) name = name[ToolPrefix.Length..];
                            ToolUse?.Invoke(new ToolUseInfo(AiCli.Str(part["id"]) ?? "", name, part["input"] as JsonObject));
                            break;
                    }
                }
                break;
            case "user":
                if (o["message"]?["content"] is not JsonArray results) break;
                foreach (var r in results.OfType<JsonObject>())
                {
                    if (AiCli.Str(r["type"]) != "tool_result") continue;
                    ToolResult?.Invoke(new ToolResultInfo(AiCli.Str(r["tool_use_id"]) ?? "", AiCli.Bool(r["is_error"]) ?? false,
                        ResultText(r["content"])));
                }
                break;
            case "result":
                bool isError = AiCli.Bool(o["is_error"]) ?? false;
                if (_resuming && !_sawInit && isError && _pendingLine is not null)
                {
                    RestartFresh();
                    break;
                }
                if (!_mcpChecked)
                {
                    _mcpChecked = true;
                    if (!_mcp.ToolsListedSince(_mcpSince)) McpProblem?.Invoke(_mcp.DescribeProblem(_mcpSince, _mcpStatus));
                }
                SetSessionId(AiCli.Str(o["session_id"]));
                IsTurnActive = false;
                _pendingLine = null;
                var resultText = AiCli.Str(o["result"]);
                // 로그인이 풀렸으면 로그인 화면으로(대화 내용은 남아 있다가 다시 로그인하면 보인다).
                bool needsLogin = isError && resultText is { } rt && (rt.Contains("/login", StringComparison.OrdinalIgnoreCase)
                                                                      || rt.Contains("Invalid API key", StringComparison.OrdinalIgnoreCase));
                TurnCompleted?.Invoke(new TurnResult(isError, AiCli.Str(o["subtype"]), resultText,
                    AiCli.Num(o["total_cost_usd"]), AiCli.Num(o["duration_ms"]), needsLogin));
                break;
        }
    }

    /// <summary>init의 MCP 서버 목록에서 gpixel 상태를 읽는다. 확실히 실패했으면 바로 알리고, 연결 중이면 차례 끝에 다시 본다.</summary>
    private void CheckMcpStatus(JsonNode? servers)
    {
        if (servers is not JsonArray list) return;
        var gpixel = list.OfType<JsonObject>().FirstOrDefault(s => AiCli.Str(s["name"]) == McpServer.ServerName);
        _mcpStatus = gpixel is null ? UiStrings.S_ClaudeCli_CheckMcpStatus_B9CFC13832 : AiCli.Str(gpixel["status"]);
        if (_mcpChecked || _mcpStatus is "connected" or "pending" or null) return;
        _mcpChecked = true;
        McpProblem?.Invoke(_mcp.DescribeProblem(_mcpSince, _mcpStatus));
    }

    private void SetSessionId(string? id)
    {
        if (!string.IsNullOrEmpty(id)) SessionId = id;
    }

    /// <summary>도구 결과 내용(문자열 또는 블록 배열)의 첫 글(짧게).</summary>
    private static string ResultText(JsonNode? content)
    {
        string? text = content switch
        {
            JsonValue v => AiCli.Str(v),
            JsonArray a => a.OfType<JsonObject>().Where(b => AiCli.Str(b["type"]) == "text").Select(b => AiCli.Str(b["text"])).FirstOrDefault(s => !string.IsNullOrEmpty(s))
                           ?? (a.OfType<JsonObject>().Any(b => AiCli.Str(b["type"]) == "image") ? UiStrings.S_ClaudeCli_ResultText_5FC8FABEA3 : null),
            _ => null,
        };
        text = (text ?? "").Trim();
        return text.Length > 300 ? text[..300] + "…" : text;
    }
}
