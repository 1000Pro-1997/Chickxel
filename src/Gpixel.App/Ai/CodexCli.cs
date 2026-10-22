using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Gpixel.App.Ai;

/// <summary>
/// OpenAI Codex CLI(<c>codex</c>)를 사용자의 ChatGPT 로그인(<c>codex login</c>)으로 쓴다 — API 키를 넣지 않는다.
/// 로그인 상태는 <c>codex login status</c>, 대화는 <see cref="CodexSession"/>. 모델 목록은 Codex가 받아 둔
/// <c>~/.codex/models_cache.json</c>(목록에 보이는 모델), 기본 모델은 <c>~/.codex/config.toml</c>의 <c>model</c>.
/// </summary>
public sealed partial class CodexProvider : AiProvider
{
    /// <summary>실행 파일별 <c>codex features list</c>의 기능 이름(그 버전이 아는 기능만 끄도록).</summary>
    private static readonly ConcurrentDictionary<string, IReadOnlySet<string>> FeaturesByPath = new();

    public override string Id => "codex";
    public override string Name => "Codex";
    public override string ProductName => "Codex CLI";
    public override string Command => "codex";
    public override string AccountText => "ChatGPT 구독(Plus·Pro 등) 계정";
    public override IReadOnlyList<string> LoginArgs { get; } = ["login"];
    public override bool LoginTakesCode => false;
    public override string PathExample => "예: /opt/homebrew/bin/codex";

    public override IReadOnlyList<(string Title, string Command)> InstallCommands
    {
        get
        {
            (string, string) npm = ("npm (macOS · Linux · Windows)", "npm install -g @openai/codex");
            (string, string) brew = ("Homebrew (macOS)", "brew install --cask codex");
            return OperatingSystem.IsMacOS() ? [brew, npm] : [npm];
        }
    }

    public override string? GetPathOverride(AppSettings settings) => settings.CodexPath;
    public override void SetPathOverride(AppSettings settings, string? path) => settings.CodexPath = path;
    public override string? GetModel(AppSettings settings) => settings.CodexModel;
    public override void SetModel(AppSettings settings, string? model) => settings.CodexModel = model;

    /// <summary>ChatGPT 데스크톱 앱(macOS)에 들어 있는 Codex CLI.</summary>
    protected override IEnumerable<string> ExtraCandidates()
    {
        if (OperatingSystem.IsMacOS()) yield return "/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex";
    }

    public override async Task<AuthStatus> GetAuthStatusAsync(string path, CancellationToken cancellationToken = default)
    {
        var featuresTask = LoadFeaturesAsync(path, cancellationToken);
        var run = await AiCli.RunAsync(path, ["login", "status"], cancellationToken).ConfigureAwait(false);
        await featuresTask.ConfigureAwait(false);
        if (run.Error is { } error) return new AuthStatus(false, null, null, null, error);
        // "Logged in using ChatGPT" / "Logged in using an API key …" / "Not logged in" (출력은 stdout·stderr 어느 쪽이든)
        var text = AiCli.StripAnsi((run.Stdout + "\n" + run.Stderr).Trim());
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Logged in", StringComparison.OrdinalIgnoreCase));
        if (run.ExitCode == 0 && line is not null)
        {
            var method = line.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) ? "ChatGPT 계정" : line.Contains("API key", StringComparison.OrdinalIgnoreCase) ? "API 키" : null;
            return new AuthStatus(true, null, null, method, null);
        }
        bool notLoggedIn = text.Contains("Not logged in", StringComparison.OrdinalIgnoreCase);
        return new AuthStatus(false, null, null, null, notLoggedIn ? null : AiCli.Tail(text, 600));
    }

    /// <summary>그 실행 파일이 아는 기능 이름을 읽어 둔다(실패하면 비워 둠 — 세션은 오래된 기본 기능만 끈다).</summary>
    private static async Task LoadFeaturesAsync(string path, CancellationToken cancellationToken)
    {
        if (FeaturesByPath.ContainsKey(path)) return;
        var run = await AiCli.RunAsync(path, ["features", "list"], cancellationToken, timeoutSeconds: 10).ConfigureAwait(false);
        if (run.Error is not null || run.ExitCode != 0) return;
        var names = run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(' ', 2)[0])
            .Where(n => FeatureName().IsMatch(n))
            .ToHashSet(StringComparer.Ordinal);
        if (names.Count > 0) FeaturesByPath[path] = names;
    }

    internal static IReadOnlySet<string>? KnownFeatures(string path) => FeaturesByPath.GetValueOrDefault(path);

    public override IReadOnlyList<ModelChoice> Models(AppSettings settings)
    {
        var models = CachedModels();
        string Display(string slug) => models.FirstOrDefault(m => m.Slug == slug).Name ?? slug;
        var configured = ConfiguredModel();
        var list = new List<ModelChoice>
        {
            new(null, configured is not null ? $"기본 · {Display(configured)}" : "기본 · Codex 기본 모델",
                configured is not null ? $"Codex 설정(config.toml)의 모델({configured})을 씁니다." : "Codex 기본값을 씁니다(config.toml에 model이 없음)."),
        };
        foreach (var (slug, name, description) in models) list.Add(new ModelChoice(slug, name, description));
        if (settings.CodexModel is { Length: > 0 } custom && list.All(c => c.Value != custom))
            list.Add(new ModelChoice(custom, custom, "설정 파일에 적힌 모델"));
        return list;
    }

    public override IChatSession CreateSession(string path, McpServer mcp) => new CodexSession(path, mcp);

    /// <summary>Codex 설정 폴더(<c>$CODEX_HOME</c> 또는 <c>~/.codex</c>).</summary>
    private static string CodexHome
        => Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } custom ? AiCli.ExpandHome(custom) : Path.Combine(AiCli.Home, ".codex");

    /// <summary>
    /// Codex가 받아 둔 모델 목록(<c>models_cache.json</c>, 목록에 보이는 것만, 우선순위 순): 값, 이름, 설명. 없거나 읽지 못하면 빈 목록.
    /// </summary>
    private static List<(string Slug, string Name, string? Description)> CachedModels()
    {
        var list = new List<(string Slug, string Name, string? Description, double Priority)>();
        try
        {
            var file = Path.Combine(CodexHome, "models_cache.json");
            if (!File.Exists(file) || JsonNode.Parse(File.ReadAllText(file))?["models"] is not JsonArray models) return [];
            foreach (var m in models.OfType<JsonObject>())
            {
                if (AiCli.Str(m["slug"]) is not { Length: > 0 } slug || AiCli.Str(m["visibility"]) is { } v && v != "list") continue;
                list.Add((slug, AiCli.Str(m["display_name"]) ?? slug, AiCli.Str(m["description"]), AiCli.Num(m["priority"]) ?? double.MaxValue));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
        return list.OrderBy(m => m.Priority).Select(m => (m.Slug, m.Name, m.Description)).ToList();
    }

    /// <summary><c>config.toml</c> 맨 위(첫 표 머리글 앞)의 <c>model = "…"</c>. 없으면 null.</summary>
    private static string? ConfiguredModel()
    {
        try
        {
            var file = Path.Combine(CodexHome, "config.toml");
            if (!File.Exists(file)) return null;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith('[')) break;
                if (TopLevelModel().Match(line) is { Success: true } m) return m.Groups[1].Value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    [GeneratedRegex(@"^model\s*=\s*[""']([^""']+)[""']")]
    private static partial Regex TopLevelModel();

    [GeneratedRegex(@"^[a-z0-9_.]+$")]
    private static partial Regex FeatureName();
}

/// <summary>
/// Codex 대화 하나. 메시지마다 <c>codex exec --json</c>(이어 가면 <c>codex exec resume &lt;스레드&gt;</c>) 프로세스를 하나 띄워
/// JSONL 이벤트를 읽는다. 편집기 MCP 서버만 쓰게 하고(셸·웹 검색·앱·플러그인 끔, 읽기 전용 샌드박스, MCP 도구는 묻지 않고 허용),
/// 시스템 프롬프트는 <c>developer_instructions</c>로 넘긴다. 사용자의 Codex 설정(로그인·기본 모델)은 그대로 쓴다.
/// 이어 가기에 실패하면 새 대화로 같은 메시지를 다시 보낸다. 이벤트는 모두 UI 스레드에서 발생한다(끝낸 프로세스의 이벤트는 버림).
/// </summary>
public sealed class CodexSession : IChatSession
{
    private const string TokenVariable = "GPIXEL_MCP_TOKEN";

    /// <summary>MCP 말고 다른 일을 하게 하는 기능(그 Codex 버전이 아는 것만 끈다).</summary>
    private static readonly string[] DisabledFeatures =
    [
        "shell_tool", "unified_exec", "apps", "plugins", "browser_use", "browser_use_external", "computer_use",
        "image_generation", "in_app_browser", "view_image", "memories", "standalone_web_search", "js_repl", "code_mode",
    ];

    /// <summary>기능 목록을 못 읽었을 때 끌 기능(오래전부터 있는 것).</summary>
    private static readonly string[] BaselineFeatures = ["shell_tool", "unified_exec"];

    private readonly string _codexPath;
    private readonly McpServer _mcp;
    private readonly HashSet<string> _shownNotices = new(StringComparer.Ordinal);
    private Process? _process;
    /// <summary>프로세스 세대. 끝낸 프로세스에서 늦게 온 이벤트를 걸러 낸다.</summary>
    private int _generation;
    private string? _pendingText, _lastError;
    private List<string> _pendingImages = [];
    private bool _resuming, _sawThread;
    /// <summary>이 프로세스를 띄울 때의 MCP 요청 수와, 이 대화에서 연결 문제를 이미 알렸는지.</summary>
    private McpCounters _mcpSince;
    private bool _mcpProblemShown;

    public CodexSession(string codexPath, McpServer mcp)
    {
        _codexPath = codexPath;
        _mcp = mcp;
    }

    public string? Model { get; set; }

    /// <summary>Codex는 프로젝트 폴더의 AGENTS.md·git 저장소를 읽지 않도록 늘 빈 작업 폴더에서 돈다(이 값은 쓰지 않음).</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Codex 스레드 ID(이어 가기용).</summary>
    public string? SessionId { get; private set; }

    public bool IsTurnActive { get; private set; }

    // Codex exec는 답 글을 조각으로 보내지 않고(다 쓴 글만 옴) 실제 모델 ID도 알려 주지 않는다.
    public event Action? TextStarted { add { } remove { } }
    public event Action<string>? TextDelta { add { } remove { } }
    public event Action<string>? ModelResolved { add { } remove { } }
    public event Action<string, string?>? BlockStarted;
    public event Action<int>? BlockProgress;
    public event Action<string>? AssistantText;
    public event Action<ToolUseInfo>? ToolUse;
    public event Action<ToolResultInfo>? ToolResult;
    public event Action<TurnResult>? TurnCompleted;
    public event Action<ExitInfo>? Exited;
    public event Action<string>? Notice;
    public event Action<string>? McpProblem;

    public void Send(string text, IReadOnlyList<byte[]> pngImages)
    {
        // 앞 차례의 프로세스가 아직 기록을 마무리하는 중일 수 있으므로 끝내지 않고, 다 쓴 첨부만 지운다.
        DeleteImages();
        var dir = Path.Combine(Path.GetTempPath(), "gpixel-codex", "attach");
        Directory.CreateDirectory(dir);
        var images = new List<string>(pngImages.Count);
        foreach (var png in pngImages)
        {
            var file = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(file, png);
            images.Add(file);
        }
        _pendingText = text.Length > 0 ? text : "(이미지)";
        _pendingImages = images;
        IsTurnActive = true;
        try
        {
            StartProcess();
        }
        catch
        {
            IsTurnActive = false;
            DeleteImages();
            throw;
        }
    }

    public void Cancel()
    {
        KillProcess();
        IsTurnActive = false;
        _pendingText = null;
        DeleteImages();
    }

    public void Restart() => Cancel();

    public void Reset()
    {
        Cancel();
        SessionId = null;
        _shownNotices.Clear();
        _mcpProblemShown = false;
    }

    public void Dispose() => Cancel();

    private void StartProcess()
    {
        var work = Path.Combine(Path.GetTempPath(), "gpixel-codex", "work");
        Directory.CreateDirectory(work);
        var args = new List<string> { "exec" };
        if (SessionId is not null) args.AddRange(["resume", SessionId]);
        // 이미지 목록(-i)은 값을 여러 개 받으므로 다른 옵션보다 앞에 둔다.
        foreach (var image in _pendingImages) args.AddRange(["-i", image]);
        args.AddRange(["--json", "--skip-git-repo-check"]);
        if (!string.IsNullOrEmpty(Model)) args.AddRange(["-m", Model]);
        foreach (var setting in ConfigOverrides()) args.AddRange(["-c", setting]);
        args.Add("-"); // 메시지는 표준 입력으로

        var psi = AiCli.CreateStartInfo(_codexPath, args, work, true);
        psi.Environment[TokenVariable] = _mcp.Token;
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
        p.Start();
        p.BeginErrorReadLine();
        int gen = ++_generation;
        _process = p;
        _resuming = SessionId is not null;
        _sawThread = false;
        _lastError = null;
        var stdin = p.StandardInput;
        var prompt = _pendingText ?? "";
        _ = Task.Run(() =>
        {
            try
            {
                stdin.Write(prompt);
                stdin.Close();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // 프로세스가 죽었으면 읽기 스레드가 끝을 알린다.
            }
        });
        var reader = new Thread(() => ReadLoop(p, gen, stderr)) { IsBackground = true, Name = "codex-stdout" };
        reader.Start();
    }

    /// <summary><c>-c</c> 설정: 편집기 MCP 서버만, MCP 도구는 묻지 않고 허용, 읽기 전용, 웹 검색·알림·AGENTS.md 끔, 시스템 프롬프트.</summary>
    private IEnumerable<string> ConfigOverrides()
    {
        _mcp.Start();
        yield return $"mcp_servers={{{McpServer.ServerName}={{url={TomlString(_mcp.Url)},bearer_token_env_var={TomlString(TokenVariable)},default_tools_approval_mode=\"approve\"}}}}";
        yield return "approval_policy=\"never\"";
        yield return "sandbox_mode=\"read-only\"";
        yield return "web_search=\"disabled\"";
        yield return "notify=[]";
        yield return "project_doc_max_bytes=0";
        yield return "developer_instructions=" + TomlString(AiCli.SystemPrompt);
        var known = CodexProvider.KnownFeatures(_codexPath);
        foreach (var feature in known is null ? BaselineFeatures : DisabledFeatures.Where(known.Contains))
            yield return $"features.{feature}=false";
    }

    /// <summary>TOML 기본 문자열(큰따옴표, 제어 문자·따옴표·역슬래시 이스케이프).</summary>
    internal static string TomlString(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("X4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private void KillProcess()
    {
        _generation++;
        var p = _process;
        _process = null;
        if (p is not null) AiCli.Kill(p);
    }

    private void DeleteImages()
    {
        foreach (var f in _pendingImages)
        {
            try
            {
                File.Delete(f);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        _pendingImages = [];
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
        lock (stderr) tail = AiCli.Tail(AiCli.StripAnsi(stderr.ToString()).Trim(), 1500);
        Dispatcher.UIThread.Post(() =>
        {
            p.Dispose();
            OnExit(gen, code, tail);
        });
    }

    /// <summary>프로세스가 끝남: 차례가 끝나지 않았으면 이어 가기 실패(새 대화로 다시) 또는 오류로 마친다.</summary>
    private void OnExit(int gen, int code, string stderrTail)
    {
        if (gen != _generation) return;
        _process = null;
        if (!IsTurnActive)
        {
            DeleteImages();
            return;
        }
        if (_resuming && !_sawThread && _pendingText is not null)
        {
            RestartFresh();
            return;
        }
        IsTurnActive = false;
        _pendingText = null;
        DeleteImages();
        if (_lastError is { } error)
            TurnCompleted?.Invoke(new TurnResult(true, null, error, null, null, IsAuthError(error) || IsAuthError(stderrTail)));
        else
            Exited?.Invoke(new ExitInfo(code, stderrTail));
    }

    /// <summary>이전 대화를 이어 가지 못함(스레드 기록 없음 등): 새 대화로 같은 메시지를 다시 보낸다.</summary>
    private void RestartFresh()
    {
        KillProcess();
        SessionId = null;
        Notice?.Invoke("이전 대화를 이어 가지 못해 새 대화로 보냈습니다.");
        try
        {
            StartProcess();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            IsTurnActive = false;
            _pendingText = null;
            DeleteImages();
            Exited?.Invoke(new ExitInfo(-1, ex.Message));
        }
    }

    private static bool IsAuthError(string text)
        => text.Contains("401", StringComparison.Ordinal) || text.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
           || text.Contains("not logged in", StringComparison.OrdinalIgnoreCase) || text.Contains("codex login", StringComparison.OrdinalIgnoreCase)
           || text.Contains("refresh token", StringComparison.OrdinalIgnoreCase);

    private void Handle(int gen, JsonObject o)
    {
        if (gen != _generation) return;
        switch (AiCli.Str(o["type"]))
        {
            case "thread.started":
                _sawThread = true;
                if (AiCli.Str(o["thread_id"]) is { Length: > 0 } id) SessionId = id;
                break;
            case "item.started":
            case "item.updated":
            case "item.completed":
                if (o["item"] is JsonObject item) HandleItem(AiCli.Str(o["type"])!, item);
                break;
            case "turn.completed":
                if (!_mcpProblemShown && !_mcp.ToolsListedSince(_mcpSince))
                {
                    _mcpProblemShown = true;
                    McpProblem?.Invoke(_mcp.DescribeProblem(_mcpSince, null));
                }
                Finish(new TurnResult(false, null, null, null, null));
                break;
            case "turn.failed":
                var message = AiCli.Str(o["error"]?["message"]) ?? _lastError ?? "알 수 없는 오류";
                Finish(new TurnResult(true, null, message, null, null, IsAuthError(message)));
                break;
            case "error":
                // 다시 연결 중 같은 안내도 오므로 기억만 해 두고(차례가 실패하면 이유로 씀) 한 번씩만 알린다.
                if (AiCli.Str(o["message"]) is { Length: > 0 } err)
                {
                    _lastError = err;
                    ShowNotice(err);
                }
                break;
        }
    }

    private void Finish(TurnResult result)
    {
        if (!IsTurnActive) return;
        IsTurnActive = false;
        _pendingText = null;
        TurnCompleted?.Invoke(result);
    }

    private void HandleItem(string phase, JsonObject item)
    {
        var id = AiCli.Str(item["id"]) ?? "";
        bool started = phase == "item.started", completed = phase == "item.completed";
        switch (AiCli.Str(item["type"]))
        {
            case "agent_message":
                if (started) BlockStarted?.Invoke("text", null);
                if (completed && AiCli.Str(item["text"]) is { Length: > 0 } text)
                {
                    BlockProgress?.Invoke(text.Length);
                    AssistantText?.Invoke(text);
                }
                break;
            case "reasoning":
                if (started) BlockStarted?.Invoke("thinking", null);
                else BlockProgress?.Invoke(AiCli.Str(item["text"])?.Length ?? 0);
                break;
            case "mcp_tool_call":
                var server = AiCli.Str(item["server"]);
                var tool = AiCli.Str(item["tool"]) ?? "?";
                var name = server is null || server == McpServer.ServerName ? tool : $"{server}.{tool}";
                if (started) ToolUse?.Invoke(new ToolUseInfo(id, name, item["arguments"] as JsonObject));
                else if (completed)
                {
                    var error = AiCli.Str(item["error"]?["message"]);
                    bool failed = error is not null || AiCli.Str(item["status"]) == "failed" || AiCli.Bool(item["result"]?["is_error"]) == true;
                    ToolResult?.Invoke(new ToolResultInfo(id, failed, error ?? ResultText(item["result"]?["content"])));
                }
                break;
            case "command_execution":
                // 셸 도구는 꺼 두지만 켜진 버전이면 무엇을 했는지는 보인다.
                if (started) ToolUse?.Invoke(new ToolUseInfo(id, "shell", new JsonObject { ["command"] = AiCli.Str(item["command"]) }));
                else if (completed) ToolResult?.Invoke(new ToolResultInfo(id, AiCli.Str(item["status"]) == "failed", AiCli.Str(item["aggregated_output"]) ?? ""));
                break;
            case "error":
                // 설정 경고 등(차례는 계속됨).
                if (completed && AiCli.Str(item["message"]) is { Length: > 0 } warning) ShowNotice(warning);
                break;
        }
    }

    private void ShowNotice(string text)
    {
        if (_shownNotices.Add(text)) Notice?.Invoke(text);
    }

    /// <summary>MCP 도구 결과 내용 블록의 첫 글(짧게).</summary>
    private static string ResultText(JsonNode? content)
    {
        var text = content is JsonArray a
            ? a.OfType<JsonObject>().Where(b => AiCli.Str(b["type"]) == "text").Select(b => AiCli.Str(b["text"])).FirstOrDefault(s => !string.IsNullOrEmpty(s))
              ?? (a.OfType<JsonObject>().Any(b => AiCli.Str(b["type"]) == "image") ? "(이미지)" : null)
            : null;
        text = (text ?? "").Trim();
        return text.Length > 300 ? text[..300] + "…" : text;
    }
}
