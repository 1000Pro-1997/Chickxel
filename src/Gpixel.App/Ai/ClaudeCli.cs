using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Gpixel.App.Ai;

/// <summary>
/// <c>claude auth status --json</c> 결과. <see cref="Error"/>가 있으면 상태를 알아내지 못한 것(실행 실패·응답 이상).
/// </summary>
public sealed record AuthStatus(bool LoggedIn, string? Email, string? Plan, string? AuthMethod, string? Error)
{
    /// <summary>머리줄에 보일 문구: "email · Pro".</summary>
    public string Describe()
    {
        var plan = string.IsNullOrEmpty(Plan) ? AuthMethod : char.ToUpperInvariant(Plan[0]) + Plan[1..];
        return string.Join(" · ", new[] { Email, plan }.Where(s => !string.IsNullOrEmpty(s)));
    }
}

/// <summary>
/// 사용자가 설치한 Claude Code CLI(<c>claude</c>) 찾기·로그인 상태·로그인 터미널.
/// GUI 앱은 셸의 PATH를 물려받지 못하므로 흔한 설치 위치를 직접 살핀다.
/// 로그인 정보는 Claude Code가 보관하며 여기서는 읽지 않는다.
/// </summary>
public static class ClaudeCli
{
    /// <summary>채팅 세션의 시스템 프롬프트(<c>--system-prompt-file</c>로 넘김).</summary>
    public const string SystemPrompt =
        """
        You are the pixel-art assistant built into Gpixel, a pixel-art and sprite editor. The user sees the open image in the editor while chatting with you. You change it only through the gpixel MCP tools (mcp__gpixel__*); you have no file system or shell access.

        - Reply in the user's language (often Korean). Keep answers short: one or two sentences about what you did or found, unless the user asks for more.
        - Before editing, always call get_document to learn the canvas size, frames, layers (including groups and parts), palette and the current frame/layer. When changing existing art, look at it first with get_image (overall look) and get_pixels (exact colors) for the area you will touch.
        - Coordinates: (0,0) is the top-left pixel, x grows to the right, y grows downward. All coordinates are inclusive pixel indices (a 16×16 canvas spans 0..15).
        - Frames are animation frames. Character parts (head, body, arms, legs) are "part" layers nested under a "group" layer: each part has its own size and its own drawing per frame, and one position (offset) inside the frame shared by all frames. When the target layer is a part, pixel coordinates are part-local ((0,0) = the part's top-left). You cannot draw on a group layer; pick one of its parts. Use add_group, add_part and set_part to create, move and resize parts.
        - For sprites and detailed art prefer draw_pixels: rows of characters plus a legend mapping each character to a color; a space (or a character missing from the legend) leaves that pixel unchanged. Use draw_shape, fill, set_pixels and clear for simple shapes and touch-ups.
        - Colors are "#RRGGBB", "#RRGGBBAA" or "transparent". Prefer colors from the document palette unless the user asks otherwise.
        - After editing, verify the result with get_image and fix mistakes before you reply.
        - Your whole turn becomes a single undo step for the user, so several tool calls per request are fine.
        - Don't save (save_project) unless the user asks.
        - To start a new image use new_project. It fails when the open project has unsaved changes; then tell the user and ask them to save or close it first. Never save on your own to get around it.
        - Images the user attaches are references; the canvas itself changes only through the tools.
        - If a tool refuses (hidden or locked layer, no open project, ...), tell the user instead of working around it.
        """;

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// claude 실행 파일을 찾는다. <paramref name="overridePath"/>(설정에서 직접 정한 경로)가 있으면 먼저 쓰고,
    /// 없으면 PATH와 흔한 설치 위치를 살핀다. 못 찾으면 null.
    /// </summary>
    public static string? Locate(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var p = ExpandHome(overridePath.Trim().Trim('"'));
            if (File.Exists(p)) return p;
        }
        foreach (var c in Candidates())
        {
            try
            {
                if (File.Exists(c)) return c;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
        return null;
    }

    /// <summary>"~/"로 시작하면 홈 폴더로 바꾼다.</summary>
    public static string ExpandHome(string path)
        => path == "~" ? Home : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Home, path[2..]) : path;

    private static IEnumerable<string> PathEntries()
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim('"'));

    private static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Home, ".local", "bin", "claude.exe");
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (appData.Length > 0) yield return Path.Combine(appData, "npm", "claude.cmd");
            foreach (var dir in PathEntries())
            {
                yield return Path.Combine(dir, "claude.exe");
                yield return Path.Combine(dir, "claude.cmd");
            }
            yield break;
        }
        foreach (var dir in PathEntries()) yield return Path.Combine(dir, "claude");
        foreach (var dir in UnixProbeDirectories()) yield return Path.Combine(dir, "claude");
    }

    /// <summary>macOS·Linux에서 claude(와 npm 설치면 node)가 흔히 있는 폴더.</summary>
    private static IEnumerable<string> UnixProbeDirectories()
    {
        yield return Path.Combine(Home, ".local", "bin");
        yield return Path.Combine(Home, ".claude", "local");
        yield return "/opt/homebrew/bin";
        yield return "/usr/local/bin";
        yield return Path.Combine(Home, ".npm-global", "bin");
        yield return Path.Combine(Home, ".bun", "bin");
        var nvm = Path.Combine(Home, ".nvm", "versions", "node");
        string[] nodes;
        try
        {
            nodes = Directory.Exists(nvm) ? Directory.GetDirectories(nvm) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            nodes = [];
        }
        foreach (var n in nodes.OrderByDescending(x => x, StringComparer.Ordinal)) yield return Path.Combine(n, "bin");
        yield return "/usr/bin";
        yield return "/bin";
    }

    /// <summary>자식 프로세스에 줄 PATH: claude가 있는 폴더 + 흔한 설치 폴더 + 지금 PATH.</summary>
    public static string AugmentedPath(string claudePath)
    {
        var dirs = new List<string>();
        if (Path.GetDirectoryName(claudePath) is { Length: > 0 } own) dirs.Add(own);
        if (OperatingSystem.IsWindows())
        {
            dirs.Add(Path.Combine(Home, ".local", "bin"));
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (appData.Length > 0) dirs.Add(Path.Combine(appData, "npm"));
        }
        else dirs.AddRange(UnixProbeDirectories());
        dirs.AddRange(PathEntries());
        var cmp = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return string.Join(Path.PathSeparator, dirs.Distinct(cmp));
    }

    /// <summary>claude를 창 없이 띄우는 시작 정보(표준 출력·오류는 UTF-8로 받음, PATH 보강).</summary>
    public static ProcessStartInfo CreateStartInfo(string claudePath, IEnumerable<string> args, string? workingDirectory, bool redirectInput)
    {
        var psi = new ProcessStartInfo(claudePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (redirectInput) psi.StandardInputEncoding = new UTF8Encoding(false);
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (workingDirectory is not null && Directory.Exists(workingDirectory)) psi.WorkingDirectory = workingDirectory;
        psi.Environment["PATH"] = AugmentedPath(claudePath);
        // Claude Code 안에서 Gpixel을 띄웠어도 자식 claude가 "중첩 실행"으로 여기지 않게.
        psi.Environment.Remove("CLAUDECODE");
        return psi;
    }

    /// <summary>로그인 상태(<c>claude auth status --json</c>). 실행 실패·시간 초과는 <see cref="AuthStatus.Error"/>에 담는다.</summary>
    public static async Task<AuthStatus> GetAuthStatusAsync(string path, CancellationToken cancellationToken = default)
    {
        Process? p = null;
        try
        {
            p = Process.Start(CreateStartInfo(path, ["auth", "status", "--json"], Home, false));
            if (p is null) return new AuthStatus(false, null, null, null, "claude를 실행하지 못했습니다.");
            var outTask = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errTask = p.StandardError.ReadToEndAsync(CancellationToken.None);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(p);
                return new AuthStatus(false, null, null, null, cancellationToken.IsCancellationRequested ? "취소됨" : "claude가 응답하지 않습니다(20초).");
            }
            string stdout = await outTask.ConfigureAwait(false), stderr = await errTask.ConfigureAwait(false);
            return ParseAuthStatus(stdout) ?? new AuthStatus(false, null, null, null, Tail((stderr + "\n" + stdout).Trim(), 600));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new AuthStatus(false, null, null, null, $"claude를 실행하지 못했습니다: {ex.Message}");
        }
        finally
        {
            p?.Dispose();
        }
    }

    private static AuthStatus? ParseAuthStatus(string stdout)
    {
        int start = stdout.IndexOf('{'), end = stdout.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            if (JsonNode.Parse(stdout[start..(end + 1)]) is not JsonObject o) return null;
            return new AuthStatus(Bool(o["loggedIn"]) ?? false, Str(o["email"]), Str(o["subscriptionType"]), Str(o["authMethod"]), null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>주소를 기본 브라우저로 연다.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            if (OperatingSystem.IsMacOS()) StartDetached("open", url);
            else if (OperatingSystem.IsLinux()) StartDetached("xdg-open", url);
            else throw;
        }
    }

    /// <summary>
    /// 보이는 터미널 창에서 <c>claude auth login</c>을 실행한다(숨은 로그인이 안 될 때 대신 쓰는 길).
    /// 실패하면 false와 이유.
    /// </summary>
    public static bool OpenLoginTerminal(string path, out string error)
    {
        error = "";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"Claude\" cmd /k \"{path}\" auth login")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return true;
            }
            if (OperatingSystem.IsMacOS()) return OpenMacTerminal(path, out error);
            foreach (var (term, pre) in new[]
                     {
                         ("x-terminal-emulator", "-e"), ("gnome-terminal", "--"), ("konsole", "-e"), ("xfce4-terminal", "-x"), ("xterm", "-e"),
                     })
            {
                try
                {
                    StartDetached(term, pre, path, "auth", "login");
                    return true;
                }
                catch (Win32Exception)
                {
                }
            }
            error = "터미널 프로그램을 찾지 못했습니다. 터미널에서 직접 \"claude auth login\"을 실행하세요.";
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// macOS: 실행 권한을 준 .command 파일을 터미널로 연다(자동화 권한 요청이 필요 없음).
    /// 그게 안 되면 osascript로 Terminal에 <c>do script</c>.
    /// </summary>
    private static bool OpenMacTerminal(string path, out string error)
    {
        error = "";
        var quoted = "'" + path.Replace("'", "'\\''") + "'";
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "gpixel-claude");
            Directory.CreateDirectory(dir);
            var script = Path.Combine(dir, "claude-login.command");
            var pathEnv = "'" + AugmentedPath(path).Replace("'", "'\\''") + "'";
            File.WriteAllText(script,
                $"#!/bin/sh\nexport PATH={pathEnv}\n{quoted} auth login\necho\necho \"로그인이 끝나면 이 창을 닫고 Gpixel에서 '다시 확인'을 누르세요.\"\n",
                new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var p = Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-a", "Terminal", script }, UseShellExecute = false, CreateNoWindow = true });
            if (p is not null && p.WaitForExit(5000) && p.ExitCode == 0) return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
        }
        var apple = (quoted + " auth login").Replace("\\", "\\\\").Replace("\"", "\\\"");
        try
        {
            using var p = Process.Start(new ProcessStartInfo("osascript")
            {
                ArgumentList = { "-e", "tell application \"Terminal\"", "-e", "activate", "-e", $"do script \"{apple}\"", "-e", "end tell" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });
            if (p is null) return false;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            if (p.HasExited && p.ExitCode == 0) return true;
            error = string.IsNullOrWhiteSpace(err) ? "터미널을 열지 못했습니다." : err.Trim();
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void StartDetached(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }

    /// <summary>프로세스(와 자식)를 끝낸다. 이미 끝났으면 무시.</summary>
    internal static void Kill(Process p)
    {
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    // ───────────── JSON·문자열 도우미 ─────────────

    internal static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    internal static bool? Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    internal static double? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    /// <summary>끝부분 <paramref name="max"/>자.</summary>
    internal static string Tail(string s, int max) => s.Length <= max ? s : "…" + s[^max..];

    private static readonly Regex Ansi = new(@"\x1B(\[[0-9;?]*[ -/]*[@-~]|\][^\x07\x1B]*(\x07|\x1B\\)|[@-Z\\-_])", RegexOptions.Compiled);

    /// <summary>터미널 제어 문자(색·링크 등)를 지운다.</summary>
    internal static string StripAnsi(string s) => Ansi.Replace(s, "");
}

/// <summary>
/// 숨은 <c>claude auth login</c> 프로세스로 로그인한다. 출력에 나온 https 주소를 알려 주고(<see cref="UrlFound"/>),
/// 2초마다 로그인 상태를 확인해 로그인되면 <see cref="Succeeded"/>, 로그인 없이 프로세스가 끝나면 <see cref="Failed"/>.
/// 이벤트는 모두 UI 스레드에서 발생한다.
/// </summary>
public sealed class ClaudeLogin : IDisposable
{
    private static readonly Regex UrlPattern = new(@"https://[^\s""'<>`]+", RegexOptions.Compiled);

    private readonly string _path;
    private readonly StringBuilder _output = new();
    private readonly HashSet<string> _urls = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _urlTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private Process? _process;
    private bool _polling, _done;

    public ClaudeLogin(string claudePath)
    {
        _path = claudePath;
        _pollTimer.Tick += async (_, _) => await PollAsync(final: false);
        _urlTimer.Tick += (_, _) =>
        {
            _urlTimer.Stop();
            ScanUrls(includeTrailing: true);
        };
    }

    /// <summary>로그인 주소(브라우저가 저절로 안 열렸을 때 사용자가 누를 주소).</summary>
    public event Action<string>? UrlFound;

    /// <summary>출력의 마지막 내용 있는 줄(안내 문구 표시용).</summary>
    public event Action<string>? OutputLine;

    public event Action<AuthStatus>? Succeeded;

    /// <summary>로그인 없이 끝남(출력 끝부분).</summary>
    public event Action<string>? Failed;

    public bool IsRunning => _process is not null && !_done;

    /// <summary>로그인 프로세스를 시작한다. 실행하지 못하면 예외(Win32Exception 등).</summary>
    public void Start()
    {
        if (_process is not null) return;
        var psi = ClaudeCli.CreateStartInfo(_path, ["auth", "login"], Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), true);
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Exited += (_, _) => Dispatcher.UIThread.Post(OnExited);
        p.Start();
        _process = p;
        _ = Task.Run(() => Pump(p.StandardOutput));
        _ = Task.Run(() => Pump(p.StandardError));
        _pollTimer.Start();
    }

    private async Task Pump(StreamReader reader)
    {
        var buf = new char[1024];
        try
        {
            int n;
            while ((n = await reader.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
            {
                var chunk = new string(buf, 0, n);
                Dispatcher.UIThread.Post(() => OnChunk(chunk));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private void OnChunk(string chunk)
    {
        if (_done) return;
        _output.Append(ClaudeCli.StripAnsi(chunk));
        ScanUrls(includeTrailing: false);
        _urlTimer.Stop();
        _urlTimer.Start();
        var lines = _output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.LastOrDefault(l => l.Length > 0) is { } last) OutputLine?.Invoke(last);
    }

    /// <summary>
    /// 출력에서 새 주소를 찾는다. 끝에 붙은 주소는 아직 덜 왔을 수 있으므로 출력이 잠시 멈춘 뒤에만 낸다.
    /// </summary>
    private void ScanUrls(bool includeTrailing)
    {
        var text = _output.ToString();
        foreach (Match m in UrlPattern.Matches(text))
        {
            if (!includeTrailing && m.Index + m.Length >= text.Length) continue;
            var url = m.Value.TrimEnd('.', ',', ')', ']', ';', ':');
            if (_urls.Add(url)) UrlFound?.Invoke(url);
        }
    }

    /// <summary>브라우저가 보여 준 인증 코드를 로그인 프로세스에 넣는다.</summary>
    public void SendCode(string code)
    {
        if (_process is not { } p || _done) return;
        var w = p.StandardInput;
        var line = code.Trim() + "\n";
        _ = Task.Run(() =>
        {
            try
            {
                w.Write(line);
                w.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
        });
    }

    private async Task PollAsync(bool final)
    {
        if (_done || (_polling && !final)) return;
        _polling = true;
        try
        {
            var status = await ClaudeCli.GetAuthStatusAsync(_path);
            if (_done) return;
            if (status.LoggedIn)
            {
                Finish();
                Succeeded?.Invoke(status);
            }
            else if (final)
            {
                Finish();
                var tail = ClaudeCli.Tail(_output.ToString().Trim(), 600);
                Failed?.Invoke(tail.Length > 0 ? tail : status.Error ?? "로그인이 끝나지 않았습니다.");
            }
        }
        finally
        {
            _polling = false;
        }
    }

    private async void OnExited()
    {
        if (_done) return;
        _pollTimer.Stop();
        ScanUrls(includeTrailing: true);
        await PollAsync(final: true);
    }

    private void Finish()
    {
        _done = true;
        _pollTimer.Stop();
        _urlTimer.Stop();
        if (_process is { } p) ClaudeCli.Kill(p);
    }

    /// <summary>로그인을 그만둔다(이벤트 없음).</summary>
    public void Cancel() => Finish();

    public void Dispose()
    {
        Finish();
        _process?.Dispose();
        _process = null;
    }
}

/// <summary>Claude가 부른 도구(이름은 <c>mcp__gpixel__</c> 접두어를 뗀 것).</summary>
public sealed record ToolUseInfo(string Id, string Name, JsonObject? Input);

/// <summary>도구 결과: 오류인지와 짧은 글.</summary>
public sealed record ToolResultInfo(string ToolUseId, bool IsError, string Text);

/// <summary>한 차례(turn)의 끝.</summary>
public sealed record TurnResult(bool IsError, string? Subtype, string? Text, double? CostUsd, double? DurationMs);

/// <summary>claude 프로세스가 뜻밖에 끝남.</summary>
public sealed record ExitInfo(int ExitCode, string StderrTail);

/// <summary>
/// 오래 살아 있는 <c>claude -p</c> stream-json 프로세스 하나(대화 하나).
/// 첫 <see cref="Send"/> 때 띄우고, <see cref="Cancel"/>·<see cref="Restart"/>로 끝내면 다음 <see cref="Send"/>가
/// <c>--resume &lt;세션&gt;</c>으로 같은 대화를 이어서 다시 띄운다. 이어 가기에 실패하면 새 대화로 같은 메시지를 다시 보낸다.
/// 출력은 별도 스레드에서 읽고, 이벤트는 모두 UI 스레드에서 발생한다(끝낸 프로세스의 이벤트는 버림).
/// </summary>
public sealed class ClaudeSession : IDisposable
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

    /// <summary>답을 기다리는 중.</summary>
    public bool IsTurnActive { get; private set; }

    /// <summary>답 글 블록이 새로 시작됨(스트리밍).</summary>
    public event Action? TextStarted;
    /// <summary>스트리밍 중인 답 글 조각.</summary>
    public event Action<string>? TextDelta;
    /// <summary>다 받은 답 글 블록.</summary>
    public event Action<string>? AssistantText;
    public event Action<ToolUseInfo>? ToolUse;
    public event Action<ToolResultInfo>? ToolResult;
    public event Action<TurnResult>? TurnCompleted;
    public event Action<string>? SessionIdChanged;
    /// <summary>프로세스가 뜻밖에 끝남(차례 중이었으면 그 차례도 끝남).</summary>
    public event Action<ExitInfo>? Exited;
    /// <summary>사용자에게 알릴 짧은 안내(이어 가기 실패 등).</summary>
    public event Action<string>? Notice;

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
        if (text.Length > 0 || content.Count == 0) content.Add(new JsonObject { ["type"] = "text", ["text"] = text.Length > 0 ? text : "(이미지)" });
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

        var psi = ClaudeCli.CreateStartInfo(_claudePath, args, cwd, true);
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
        File.WriteAllText(file, ClaudeCli.SystemPrompt, new UTF8Encoding(false));
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
        if (p is not null) ClaudeCli.Kill(p);
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
        lock (stderr) tail = ClaudeCli.Tail(stderr.ToString().Trim(), 1500);
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
        Notice?.Invoke("이전 대화를 이어 가지 못해 새 대화로 보냈습니다.");
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
        switch (ClaudeCli.Str(o["type"]))
        {
            case "system":
                if (ClaudeCli.Str(o["subtype"]) == "init")
                {
                    _sawInit = true;
                    SetSessionId(ClaudeCli.Str(o["session_id"]));
                }
                break;
            case "stream_event":
                if (o["parent_tool_use_id"] is JsonValue) break; // 하위 에이전트 출력은 보이지 않는다.
                if (o["event"] is not JsonObject ev) break;
                switch (ClaudeCli.Str(ev["type"]))
                {
                    case "content_block_start" when ev["content_block"] is JsonObject block && ClaudeCli.Str(block["type"]) == "text":
                        TextStarted?.Invoke();
                        break;
                    case "content_block_delta" when ev["delta"] is JsonObject delta && ClaudeCli.Str(delta["type"]) == "text_delta":
                        if (ClaudeCli.Str(delta["text"]) is { Length: > 0 } t) TextDelta?.Invoke(t);
                        break;
                }
                break;
            case "assistant":
                if (o["parent_tool_use_id"] is JsonValue) break;
                if (o["message"]?["content"] is not JsonArray parts) break;
                foreach (var part in parts.OfType<JsonObject>())
                {
                    switch (ClaudeCli.Str(part["type"]))
                    {
                        case "text":
                            if (ClaudeCli.Str(part["text"]) is { } text) AssistantText?.Invoke(text);
                            break;
                        case "tool_use":
                            var name = ClaudeCli.Str(part["name"]) ?? "?";
                            if (name.StartsWith(ToolPrefix, StringComparison.Ordinal)) name = name[ToolPrefix.Length..];
                            ToolUse?.Invoke(new ToolUseInfo(ClaudeCli.Str(part["id"]) ?? "", name, part["input"] as JsonObject));
                            break;
                    }
                }
                break;
            case "user":
                if (o["message"]?["content"] is not JsonArray results) break;
                foreach (var r in results.OfType<JsonObject>())
                {
                    if (ClaudeCli.Str(r["type"]) != "tool_result") continue;
                    ToolResult?.Invoke(new ToolResultInfo(ClaudeCli.Str(r["tool_use_id"]) ?? "", ClaudeCli.Bool(r["is_error"]) ?? false,
                        ResultText(r["content"])));
                }
                break;
            case "result":
                bool isError = ClaudeCli.Bool(o["is_error"]) ?? false;
                if (_resuming && !_sawInit && isError && _pendingLine is not null)
                {
                    RestartFresh();
                    break;
                }
                SetSessionId(ClaudeCli.Str(o["session_id"]));
                IsTurnActive = false;
                _pendingLine = null;
                TurnCompleted?.Invoke(new TurnResult(isError, ClaudeCli.Str(o["subtype"]), ClaudeCli.Str(o["result"]),
                    ClaudeCli.Num(o["total_cost_usd"]), ClaudeCli.Num(o["duration_ms"])));
                break;
        }
    }

    private void SetSessionId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id == SessionId) return;
        SessionId = id;
        SessionIdChanged?.Invoke(id);
    }

    /// <summary>도구 결과 내용(문자열 또는 블록 배열)의 첫 글(짧게).</summary>
    private static string ResultText(JsonNode? content)
    {
        string? text = content switch
        {
            JsonValue v => ClaudeCli.Str(v),
            JsonArray a => a.OfType<JsonObject>().Where(b => ClaudeCli.Str(b["type"]) == "text").Select(b => ClaudeCli.Str(b["text"])).FirstOrDefault(s => !string.IsNullOrEmpty(s))
                           ?? (a.OfType<JsonObject>().Any(b => ClaudeCli.Str(b["type"]) == "image") ? "(이미지)" : null),
            _ => null,
        };
        text = (text ?? "").Trim();
        return text.Length > 300 ? text[..300] + "…" : text;
    }
}
