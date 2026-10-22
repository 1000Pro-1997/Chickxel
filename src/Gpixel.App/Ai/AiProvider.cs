using System.Text.Json.Nodes;

namespace Gpixel.App.Ai;

/// <summary>AI가 부른 도구(이름은 MCP 서버 접두어를 뗀 것).</summary>
public sealed record ToolUseInfo(string Id, string Name, JsonObject? Input);

/// <summary>도구 결과: 오류인지와 짧은 글.</summary>
public sealed record ToolResultInfo(string ToolUseId, bool IsError, string Text);

/// <summary>한 차례(turn)의 끝. <see cref="NeedsLogin"/>: 로그인이 풀려 실패함(로그인 화면으로 돌아감).</summary>
public sealed record TurnResult(bool IsError, string? Subtype, string? Text, double? CostUsd, double? DurationMs, bool NeedsLogin = false);

/// <summary>CLI 프로세스가 뜻밖에 끝남.</summary>
public sealed record ExitInfo(int ExitCode, string StderrTail);

/// <summary>모델 목록 한 칸: CLI에 넘길 값(null = CLI 기본값), 목록에 보일 이름, 도움말.</summary>
public sealed record ModelChoice(string? Value, string Label, string? Tip);

/// <summary>
/// 대화 하나(CLI 프로세스를 띄워 메시지를 보내고 답·도구 호출을 이벤트로 알림). 이벤트는 모두 UI 스레드에서 발생한다.
/// <see cref="Cancel"/>·<see cref="Restart"/> 뒤의 <see cref="Send"/>는 같은 대화를 이어 가고, <see cref="Reset"/>은 대화를 잊는다.
/// </summary>
public interface IChatSession : IDisposable
{
    /// <summary>모델(null = CLI 기본값). 다음에 프로세스를 띄울 때 적용된다.</summary>
    string? Model { get; set; }

    /// <summary>새 대화를 시작할 때의 작업 폴더(없으면 CLI마다 정한 곳).</summary>
    string? WorkingDirectory { get; set; }

    /// <summary>답을 기다리는 중.</summary>
    bool IsTurnActive { get; }

    /// <summary>답 글 블록이 새로 시작됨(스트리밍).</summary>
    event Action? TextStarted;
    /// <summary>스트리밍 중인 답 글 조각.</summary>
    event Action<string>? TextDelta;
    /// <summary>응답 블록이 시작됨(진행 표시용): 종류("thinking" / "tool_use" / "text")와 도구 이름(도구일 때).</summary>
    event Action<string, string?>? BlockStarted;
    /// <summary>지금 블록에 받은 글자 수(진행 표시용).</summary>
    event Action<int>? BlockProgress;
    /// <summary>다 받은 답 글 블록.</summary>
    event Action<string>? AssistantText;
    event Action<ToolUseInfo>? ToolUse;
    event Action<ToolResultInfo>? ToolResult;
    event Action<TurnResult>? TurnCompleted;
    /// <summary>프로세스가 뜻밖에 끝남(차례 중이었으면 그 차례도 끝남).</summary>
    event Action<ExitInfo>? Exited;
    /// <summary>사용자에게 알릴 짧은 안내(이어 가기 실패·CLI 경고 등).</summary>
    event Action<string>? Notice;
    /// <summary>CLI가 gpixel MCP에 연결하지 못함(세션 프로세스마다 한 번). 글은 짐작한 원인과 확인할 것.</summary>
    event Action<string>? McpProblem;
    /// <summary>CLI가 알려 준 실제 모델 ID(기본값·별칭이 가리킨 것). 알려 주지 않는 CLI는 부르지 않는다.</summary>
    event Action<string>? ModelResolved;

    /// <summary>메시지(글 + PNG 이미지)를 보낸다. 프로세스를 띄우지 못하면 예외(Win32Exception 등).</summary>
    void Send(string text, IReadOnlyList<byte[]> pngImages);

    /// <summary>지금 차례를 멈춘다(프로세스를 끝냄). 다음 <see cref="Send"/>는 같은 대화를 이어 간다.</summary>
    void Cancel();

    /// <summary>설정(모델 등)을 바꾼 뒤: 프로세스를 끝내고 다음 <see cref="Send"/>에서 같은 대화를 이어 다시 띄운다.</summary>
    void Restart();

    /// <summary>새 대화: 프로세스를 끝내고 대화를 잊는다.</summary>
    void Reset();
}

/// <summary>
/// 채팅에 쓰는 AI CLI 하나(Claude Code / Codex): 찾기·설치 안내·로그인·모델 목록·대화 만들기.
/// 로그인 정보는 각 CLI가 보관하며 Gpixel은 읽지 않는다.
/// </summary>
public abstract class AiProvider
{
    public static readonly AiProvider Claude = new ClaudeProvider();
    public static readonly AiProvider Codex = new CodexProvider();

    /// <summary>채팅 창 전환 단추 순서.</summary>
    public static IReadOnlyList<AiProvider> All { get; } = [Claude, Codex];

    /// <summary>설정 값으로 찾는다(모르는 값·null = Claude).</summary>
    public static AiProvider FromId(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Claude;

    /// <summary>설정에 저장하는 값.</summary>
    public abstract string Id { get; }
    /// <summary>창 제목·차례 이름("Claude" / "Codex").</summary>
    public abstract string Name { get; }
    /// <summary>설치할 프로그램 이름("Claude Code" / "Codex CLI").</summary>
    public abstract string ProductName { get; }
    /// <summary>실행 파일 이름(확장자 없음).</summary>
    public abstract string Command { get; }
    /// <summary>로그인 안내에 쓰는 계정 설명.</summary>
    public abstract string AccountText { get; }
    /// <summary>로그인 명령 인자(숨은 로그인·터미널 로그인).</summary>
    public abstract IReadOnlyList<string> LoginArgs { get; }
    /// <summary>숨은 로그인이 브라우저가 보여 준 인증 코드를 입력받는지.</summary>
    public abstract bool LoginTakesCode { get; }
    /// <summary>설치 명령(제목, 명령) — 이 OS에 맞는 것이 먼저.</summary>
    public abstract IReadOnlyList<(string Title, string Command)> InstallCommands { get; }
    /// <summary>경로 직접 지정 칸의 예.</summary>
    public abstract string PathExample { get; }

    public abstract string? GetPathOverride(AppSettings settings);
    public abstract void SetPathOverride(AppSettings settings, string? path);
    public abstract string? GetModel(AppSettings settings);
    public abstract void SetModel(AppSettings settings, string? model);

    /// <summary>실행 파일을 찾는다(설정에서 정한 경로 → PATH·흔한 설치 위치). 못 찾으면 null.</summary>
    public string? Locate(AppSettings settings) => AiCli.Locate(Command, GetPathOverride(settings), ExtraCandidates());

    /// <summary>PATH·흔한 설치 폴더 다음에 살필 곳(앱에 들어 있는 CLI 등).</summary>
    protected virtual IEnumerable<string> ExtraCandidates() => [];

    /// <summary>로그인 상태. 실행 실패·시간 초과는 <see cref="AuthStatus.Error"/>에 담는다.</summary>
    public abstract Task<AuthStatus> GetAuthStatusAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>모델 목록(첫 칸 = CLI 기본값, 무엇을 가리키는지 알면 이름에 붙임).</summary>
    public abstract IReadOnlyList<ModelChoice> Models(AppSettings settings);

    /// <summary>세션이 알려 준 실제 모델을 기억한다(<paramref name="requested"/> = 보낸 값, null = 기본값). 목록 이름이 바뀌면 true.</summary>
    public virtual bool RememberResolved(AppSettings settings, string? requested, string actual) => false;

    public abstract IChatSession CreateSession(string path, McpServer mcp);
}
