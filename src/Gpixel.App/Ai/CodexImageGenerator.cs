using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gpixel.App.Localization;

namespace Gpixel.App.Ai;

/// <summary>그림 생성·변환 실패(사용자·AI에게 그대로 보여 줄 문구).</summary>
public sealed class AiDrawException(string message) : Exception(message);

/// <summary>
/// 사용자의 Codex CLI 로그인으로 그림 한 장을 만든다: 빈 작업 폴더에서 <c>codex exec --json --ephemeral</c>를 띄워(셸·웹·앱·플러그인·MCP 도구는 끄고
/// 이미지 생성만 켬) "이 프롬프트로 이미지 생성 도구를 한 번 불러라"고 시킨다. 함께 보낼 그림은 <c>-i</c>로 붙인다(프롬프트의 Image 1, 2… 순서).
/// <para>
/// <c>codex exec --json</c>은 이미지 생성 항목을 이벤트로 알리지 않으므로(<c>agent_message</c>만 옴), <c>thread.started</c>의 스레드 id로
/// Codex가 그림을 저장하는 <c>$CODEX_HOME/generated_images/&lt;스레드 id&gt;/*.png</c>에서 가장 새 파일을 찾아 복사한다(원본은 Codex 폴더에 남김).
/// </para>
/// </summary>
public sealed class CodexImageGenerator(string codexPath, string? model)
{
    /// <summary>그림 한 장에 기다리는 최대 시간.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(6);

    /// <summary>
    /// 그림 한 장을 만들어 <paramref name="outputFile"/>(PNG)에 복사한다. 취소하면 프로세스를 끝내고 <see cref="OperationCanceledException"/>,
    /// 실패하면 <see cref="AiDrawException"/>(로그인이 풀렸으면 그 안내).
    /// </summary>
    public async Task GenerateAsync(string instruction, IReadOnlyList<string> images, string outputFile, CancellationToken ct)
    {
        await CodexProvider.LoadFeaturesAsync(codexPath, ct).ConfigureAwait(false);
        var work = Path.Combine(Path.GetTempPath(), "gpixel-codex", "imagegen");
        Directory.CreateDirectory(work);
        var args = new List<string> { "exec" };
        // 이미지 목록(-i)은 값을 여러 개 받으므로 다른 옵션보다 앞에 둔다.
        foreach (var image in images) args.AddRange(["-i", image]);
        args.AddRange(["--json", "--skip-git-repo-check", "--ephemeral"]);
        if (!string.IsNullOrEmpty(model)) args.AddRange(["-m", model]);
        foreach (var setting in ConfigOverrides()) args.AddRange(["-c", setting]);
        args.Add("-");

        ProcessStartInfo psi;
        try
        {
            psi = AiCli.CreateStartInfo(codexPath, args, work, true);
        }
        catch (CommandLineTooLongException ex)
        {
            throw new AiDrawException(UiStrings.FormatAiDraw_Error_Start(message: ex.Message));
        }
        using var p = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        try
        {
            p.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new AiDrawException(UiStrings.FormatAiDraw_Error_Start(message: ex.Message));
        }
        var stderrDone = AiCli.CollectErrorTail(p, stderr);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        using var kill = timeout.Token.Register(() => AiCli.Kill(p));

        string? threadId = null, lastMessage = null, error = null;
        try
        {
            await p.StandardInput.WriteAsync(instruction.AsMemory(), timeout.Token).ConfigureAwait(false);
            p.StandardInput.Close();
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                JsonObject? o;
                try { o = JsonNode.Parse(line) as JsonObject; }
                catch (JsonException) { continue; }
                switch (AiCli.Str(o?["type"]))
                {
                    case "thread.started":
                        threadId = AiCli.Str(o!["thread_id"]);
                        break;
                    case "item.completed" when AiCli.Str(o!["item"]?["type"]) == "agent_message":
                        lastMessage = AiCli.Str(o["item"]?["text"]);
                        break;
                    case "turn.failed":
                        error = AiCli.Str(o!["error"]?["message"]) ?? error;
                        break;
                    case "error":
                        error = AiCli.Str(o!["message"]) ?? error;
                        break;
                }
            }
            await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AiCli.Kill(p);
            ct.ThrowIfCancellationRequested();
            throw new AiDrawException(UiStrings.FormatAiDraw_Error_Timeout(minutes: (int)Timeout.TotalMinutes));
        }
        catch (IOException)
        {
            // 프로세스가 죽음 — 아래에서 그림이 있는지 본다.
        }

        var saved = threadId is null ? null : NewestImage(Path.Combine(CodexProvider.CodexHome, "generated_images", threadId));
        if (saved is null)
        {
            await Task.WhenAny(stderrDone, Task.Delay(2000)).ConfigureAwait(false);
            string tail;
            lock (stderr) tail = AiCli.Tail(AiCli.StripAnsi(stderr.ToString()).Trim(), 600);
            var reason = error ?? lastMessage ?? tail;
            if (IsAuthError(reason) || IsAuthError(tail)) throw new AiDrawException(UiStrings.AiDraw_Error_Login);
            throw new AiDrawException(UiStrings.FormatAiDraw_Error_NoImage(reason: string.IsNullOrWhiteSpace(reason) ? "-" : reason.Trim()));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
        File.Copy(saved, outputFile, overwrite: true);
    }

    /// <summary><c>-c</c> 설정: 승인 묻지 않음, 읽기 전용, 웹 검색·알림·AGENTS.md 끔, 생각은 짧게, 이미지 생성 말고는 끔.</summary>
    private IEnumerable<string> ConfigOverrides()
    {
        yield return "approval_policy=\"never\"";
        yield return "sandbox_mode=\"read-only\"";
        yield return "web_search=\"disabled\"";
        yield return "notify=[]";
        yield return "project_doc_max_bytes=0";
        yield return "model_reasoning_effort=\"low\"";
        foreach (var feature in CodexSession.FeatureOverrides(codexPath, imageGeneration: true)) yield return feature;
    }

    private static string? NewestImage(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? new DirectoryInfo(dir).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsAuthError(string text)
        => text.Contains("401", StringComparison.Ordinal) || text.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
           || text.Contains("not logged in", StringComparison.OrdinalIgnoreCase) || text.Contains("codex login", StringComparison.OrdinalIgnoreCase);
}
