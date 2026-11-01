using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Chickxel.Core.IO;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

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
/// AI CLI(Claude Code·Codex) 공통 도우미: 실행 파일 찾기, 창 없이 띄우기, 로그인 터미널, 브라우저 열기, JSON·문자열 도우미.
/// GUI 앱은 셸의 PATH를 물려받지 못하므로 흔한 설치 위치를 직접 살핀다.
/// 로그인 정보는 각 CLI가 보관하며 여기서는 읽지 않는다.
/// </summary>
public static class AiCli
{
    /// <summary>채팅 세션의 시스템 프롬프트(Claude = <c>--system-prompt-file</c>, Codex = <c>developer_instructions</c>).</summary>
    public const string SystemPrompt =
        """
        You are the pixel-art assistant built into Chickxel, a pixel-art and sprite editor. The user sees the open image in the editor while chatting with you. You change it only through the chickxel MCP tools; you have no file system or shell access.

        - Reply in the user's language (often Korean). Keep answers short: one or two sentences about what you did or found, unless the user asks for more.
        - Drawing mode: each user message starts with a line like "[Chickxel drawing mode: ...]" written by the app. In generate-image and generate-first modes, draw new sprites, characters, items, tiles and animations with generate_pixel_art: write `subject` in English with concrete visual details, set `animation` for animations and let the app plan the frames unless the user asks for specific poses or a frame count, pass `subject_image` when the user attached an image of the character or item to turn into pixel art, set `previous_result_as_subject` when animating, re-posing or changing the character you just generated, and pass `background` only when the user asked for a background or scene.
        - Pass `size` only when the user specified a pixel size. Otherwise the app asks Codex in English to choose a logical sprite size from the reference image or subject description before generation (unless a fixed result size is configured), then includes that size prominently in the image-generation prompt. You do not need get_document, search_references or get_style_rules before generating. After generate_pixel_art succeeds, reply with one short sentence and do not redraw or touch up the result unless the user asks.
        - In direct mode, draw with the pixel tools and do not call generate_pixel_art unless the user explicitly asks to have Codex generate the image (then call it; it runs the user's own Codex image generation inside the app). In generate-image and generate-first modes, use the pixel tools only for small edits to existing art or when the user asks to place pixels by hand. In generate-first mode, if generate_pixel_art fails for any reason other than the user stopping it, tell the user briefly and draw the same request directly with the pixel tools. In generate-image mode, do that only when Codex is not installed or not logged in.
        - Before editing, always call get_document to learn the canvas size, frames, layers (including groups and parts), palette and the current frame/layer. When changing existing art, look at it first with get_image (overall look) and get_pixels (exact colors) for the area you will touch.
        - Coordinates: (0,0) is the top-left pixel, x grows to the right, y grows downward. All coordinates are inclusive pixel indices (a 16×16 canvas spans 0..15).
        - Frames are animation frames. Character parts (head, body, arms, legs) are "part" layers nested under a "group" layer: each part has its own size and its own drawing per frame, and one position (offset) inside the frame shared by all frames. When the target layer is a part, pixel coordinates are part-local ((0,0) = the part's top-left). You cannot draw on a group layer; pick one of its parts. Use add_group, add_part and set_part to create, move and resize parts.
        - A group is also a skin slot: its skins (outfits) are alternative drawings of the same parts, and only the active skin is shown and edited. A part's empty cel in a non-base skin shows the base (first) skin's drawing. A single part may wear a different skin than its group (override). A part can also have its own part-only skins (e.g. a hat only for Head) that no other part or the group list shows; they are worn only through that part's override. Use list_skins to see them (part-only skins under partSkins), add_skin to add one (with part = a part-only skin for that part) and set_skin to switch (set_skin with part = only that part, accepting its part-only skins; follow_group = back to the group's skin); check which skin a part shows before drawing on it. Outfit presets save every group's skin + overrides under a name: list_outfits, save_outfit, apply_outfit.
        - For sprites and detailed art prefer draw_pixels: rows of characters plus a legend mapping each character to a color; a space (or a character missing from the legend) leaves that pixel unchanged. Use draw_shape, fill, set_pixels and clear for simple shapes and touch-ups.
        - In direct drawing mode, before drawing turn the user's words AND any attached images into concrete visual criteria: requested subject, pose, full-body silhouette, head-to-body and limb proportions, part positions and overlap, colors, style, canvas size and any negative constraints. Images are optional; text-only criteria count just as much. Plan the whole composition and shared canvas coordinates before splitting a character into part layers; use the same baseline and check the composite with get_image WITHOUT `layer` after arranging the parts. A part image by itself cannot verify the whole character.
        - References (when drawing directly with the pixel tools): before drawing a new sprite or animation, call get_style_rules once per conversation and search_references for similar items (subject words, kind, size; similar_to_canvas when improving existing art). When a reference fits, look at it with get_reference (as="image" for overall appearance, as="pixels" for exact proportions) and follow its proportions, outline and palette style. After each substantial drawing pass, call get_image WITHOUT `layer`, compare the full composite against every applicable criterion (including the user's wording, not just references), and fix problems. Score visual fidelity out of 10 for silhouette/proportions, part placement, and requested colors/style; never claim a score without seeing the composite. Repeat review and correction until acceptable or no further useful improvement, then state the actual score and remaining defects. add_reference and set_reference_info only write to the reference library; use them when the user asks to keep or caption references.
        - Layer effects are non-destructive: they are drawn on top of a layer (or a group's composite) without changing its pixels and can be changed or removed later. Prefer them over painting when the user asks for an outline, drop shadow, rim light, glow, color overlay/tint/palette swap or reflection, or for timed animation effects such as a hit flash, blink, shake, bob, appear/disappear, fade, squash, explode, melt, rotate, afterimage, color cycle, shockwave or shine. These are the official script effects installed by default; the user may also have effects from packages and their own script effects. Call list_effect_types for the effect ids (e.g. "outline"), settings and defaults, then add_effect (only the settings you change; animated effects play over their from_frame..to_frame range), get_effects to inspect, set_effect to tweak, reorder, retime or disable, and remove_effect. Use apply_effects (bakes effects into pixels) only when the user explicitly asks for it, and expand_canvas_for_effects when effects spill outside the canvas and the user wants them kept.
        - Colors are "#RRGGBB", "#RRGGBBAA" or "transparent". Prefer colors from the document palette unless the user asks otherwise.
        - After editing, verify the entire result with get_image WITHOUT `layer` and fix structural mistakes before you reply. An individual part looking correct does not imply that its position in the complete image is correct.
        - Your whole turn becomes a single undo step for the user, so several tool calls per request are fine.
        - If a consequential choice is ambiguous, call ask_user with a specific question and 2-5 distinct options before changing the document. The user can select an option, enter another answer, or cancel. Never guess whether to enlarge a canvas or crop a generated image; the app asks before inserting oversized art and lets the user position a crop visually.
        - Don't save (save_project) unless the user asks.
        - To start a new image use new_project (the previous document stays open in the file list, edits kept). It fails only when the open project is a never-saved new project with changes; then tell the user and ask whether to save it. Never save on your own to get around it.
        - Images the user attaches are references; the canvas itself changes only through the tools. "[Image #N]" in a user message marks where the N-th attached image belongs in the sentence (attachments are in the same order).
        - Locked layers (get_document `locked`; a locked group locks all its parts) are never changed: tools that would draw on, rename, hide, move, resize or re-skin a locked layer, add/change/remove/apply its effects, delete a frame holding a locked layer's drawing, or resize the canvas while any layer is locked are refused.
        - If a tool refuses (locked layer, no open project, ...), tell the user instead of working around it.
        - If get_document shows editScope, the user limited this turn to the current file: change only that open document (adding layers, frames, groups, parts, skins, tags and layer effects inside it is fine) and never open or create other files (open_file and new_project are refused). If another document is open now, everything is read-only. Tell the user instead of working around it.
        """;

    /// <summary>
    /// 효과 만들기 AI 칸(<see cref="EffectMakerAiPanel"/>)의 시스템 프롬프트: 효과 만들기 편집기의 원문을 고치는 순서. 쓸 수 있는 도구는 MCP 서버가
    /// 범위(<see cref="McpScope.EffectMaker"/>)로 정한다(읽기 도구 + 효과 만들기 도구).
    /// 효과 규칙과 API(<see cref="Chickxel.Core.Scripting.ScriptEffectGuide"/>)는 대화의 첫 메시지 앞에 붙인다(<see cref="EffectMakerGuidePrefix"/>).
    /// </summary>
    public const string EffectMakerSystemPrompt =
        """
        You are the script-effect assistant inside the Effect Maker window of Chickxel, a pixel-art and sprite editor. The Effect Maker writes JavaScript layer effects. The user sees the effect's source in the Effect Maker editor and a live preview on a sample chick sprite. You work only through the chickxel MCP tools; you have no file system or shell access. You can look at the open document (get_document, get_image, get_pixels, get_effects, preview_effects, references) but cannot change it; you change only the Effect Maker's source.

        - Reply in the user's language (often Korean). Keep replies short: what you changed and which settings to tune, in one to three sentences. Do not paste the code into your reply; it is already in the editor.
        - The first user message of a conversation starts with the effect-writing guide (rules and API). Follow it exactly; get_script_api returns the same guide.
        - Before changing anything, call get_effect_maker_source to read the current source, its parse errors, the execution path (fast path or Jint and why) and the preview setting values. When the user asks for a change, start from that source; write a new effect only when asked for a new one.
        - When the user refers to another of their effects ("like the X effect but ..."), find it with list_script_effects and read its code with get_script_effect_source. Those effects are read-only references; only the Effect Maker's source changes.
        - Put code into the editor with set_effect_maker_source, always the complete file. It is one undo step in the editor and does not change the user's document. It returns parse errors, a run error if any, and a preview strip of the effect on the preview chick; look at the image and compare it with the request. Fix errors and call it again.
        - Before you finish, call check_effect_maker (the same check as the Check button: every preview chick at every preview frame). Fix every problem it reports. If render runs on Jint, make it fast-path friendly when that is easy.
        - Write the effect's name, description, setting labels and choice labels in the user's language, and always set language to that language's code (language: "ko", "en", "ja", "zh-Hans" ...); if you rewrite the declared name, description or labels in another language, change language too. If the user's language is not English, always also write translations.en with an English name, description and labels for every setting and choice (labels: { key: "…", "key.choiceValue": "…" }). Whenever you change the name, description, settings or choices, update translations.en and every other existing translation in the same edit so they stay in sync.
        - Every effect you write always declares description (one short sentence about what it does) and tags (3-6 lowercase English tags people would search for, e.g. tags: ["neon", "glow", "outline"]); publishing uses them as they are. Whenever you change what the effect does, update description, tags and translations.en in the same edit so they still fit.
        - Ignore the guide's "Answer format" section: your answer is what you put into the editor, not a code block in the chat.
        - Images the user attaches are references (for example a look to imitate or the current preview frame). "[Image #N]" in a user message marks where the N-th attached image belongs in the sentence. If something is unclear, ask in your reply. The user saves, inserts and publishes the effect with the Effect Maker buttons.
        - If a tool says the Effect Maker is closed or has no script open, tell the user instead of working around it.
        """;

    /// <summary>효과 만들기 AI 칸의 첫 메시지 앞에 붙이는 효과 규칙·API(화면에는 보이지 않음).</summary>
    public static string EffectMakerGuidePrefix()
        => "[Chickxel effect-writing guide: follow these rules for every effect you write in this conversation.]\n\n"
           + Chickxel.Core.Scripting.ScriptEffectGuide.ToMarkdown(null)
           + "\n[End of guide. The user's request follows.]\n\n";

    /// <summary>그림 도구 차례가 끝난 뒤 합성 캔버스 그림을 붙여 자동으로 다시 검토시키는 비공개 후속 지시.</summary>
    public const string VisualReviewPrompt =
        """
        [Chickxel automatic visual review: continue the SAME direct-drawing request; this is not a new user request. If an image is attached, it is the CURRENT FULL COMPOSITE of the open frame, not a style reference. Revisit the ORIGINAL user's text and any ORIGINAL attached images. Restate their concrete visual requirements internally, including text-only conditions: subject, pose, proportions, head/body/limb layout, requested colors, style, size and forbidden features. Judge the entire silhouette and part alignment, not just individual layers or pixel colors. If the user asked for a reference style, compare its overall shape and palette too. Give an honest visual fidelity score out of 10 with specific failed criteria, not a numeric score derived from tool output. Fix visible defects with the pixel tools only; after each correction call get_image without `layer` and score again. Make up to three focused correction passes, stopping early if the picture satisfies the criteria or further changes cannot improve it. If you cannot see the image or cannot improve it, say so instead of claiming success. End with the actual final score and any remaining defects in the user's language. Do not save the project.]
        """;

    /// <summary>
    /// 메시지 앞에 붙여 모델에게 알리는 그리는 방식(영어, 화면에는 보이지 않음 — <see cref="Chickxel.Core.AiDraw.AiDrawSettings.EffectiveMode"/>).
    /// <paramref name="fallbackToDirect"/>(자동 모드의 그림 생성)이면 생성이 안 될 때 같은 요청을 픽셀 도구로 직접 그리게 한다.
    /// </summary>
    public static string DrawingModeLine(Chickxel.Core.AiDraw.AiDrawMode mode, bool canGenerate, bool fallbackToDirect = false) => mode == Chickxel.Core.AiDraw.AiDrawMode.Generate
        ? fallbackToDirect
            ? "[Chickxel drawing mode: generate first. Draw the requested art with generate_pixel_art first. If generate_pixel_art returns an error or cannot run, do not stop: draw the same request yourself with the pixel tools. Exception: if the error says image generation was stopped (the user cancelled it), do not draw anything. Use the pixel tools directly only for small edits or when asked to place pixels by hand.]"
            : "[Chickxel drawing mode: generate-image. Draw new art with generate_pixel_art; use the pixel tools only for small edits or when asked to place pixels by hand.]"
        : canGenerate
            ? "[Chickxel drawing mode: direct. Draw with the pixel tools for this turn; do not call generate_pixel_art.]"
            : "[Chickxel drawing mode: direct. You cannot generate images, so draw with the pixel tools. Call generate_pixel_art only if the user explicitly asks to have Codex generate the image.]";

    /// <summary>"팔레트 색만 사용"일 때 메시지 앞에 붙여 모델에게 알리는 팔레트 색(영어, 화면에는 보이지 않음 — <see cref="Chickxel.Core.AiDraw.PaletteLock"/>).</summary>
    public static string PaletteLine(Chickxel.Core.AiDraw.PaletteLock palette)
        => "[Chickxel palette lock: when drawing with the pixel tools, use only these palette colors (any other color is snapped to the nearest one, and only fully transparent is allowed for erasing): "
           + palette.ListColors() + "]";

    internal static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    internal static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static bool SamePath(string? left, string? right)
    {
        if (left is null || right is null) return left == right;
        try { return PathComparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }


    /// <summary>
    /// <paramref name="command"/> 실행 파일을 찾는다. <paramref name="overridePath"/>(설정에서 직접 정한 경로)가 있으면 먼저 쓰고,
    /// 없거나 쓸 수 없으면 PATH와 흔한 설치 위치, 그다음 <paramref name="extraCandidates"/>를 살핀다. 못 찾으면 null.
    /// </summary>
    public static string? Locate(string command, string? overridePath, IEnumerable<string> extraCandidates)
        => LocateCandidates(command, overridePath, extraCandidates).FirstOrDefault();

    /// <summary>직접 지정 경로와 자동 탐색 경로 중 실제 파일이 있는 후보들. 상태 점검은 앞 후보가 망가졌으면 다음 후보까지 실행해 본다.</summary>
    internal static IEnumerable<string> LocateCandidates(string command, string? overridePath, IEnumerable<string> extraCandidates)
    {
        var seen = new HashSet<string>(PathComparer);
        IEnumerable<string> paths = string.IsNullOrWhiteSpace(overridePath)
            ? Candidates(command).Concat(extraCandidates)
            : new[] { ExpandHome(overridePath.Trim().Trim('"')) }.Concat(Candidates(command)).Concat(extraCandidates);
        foreach (var path in paths)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { continue; }
            if (seen.Add(full) && File.Exists(full)) yield return full;
        }
    }

    /// <summary>현재 프로세스 PATH에서 셸이 실행할 CLI(.exe/.cmd)를 찾는다. 앱의 설치 위치 추측과는 별개다.</summary>
    public static string? LocateOnPath(string command)
    {
        foreach (var dir in PathEntries())
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var exe = Path.Combine(dir, command + ".exe");
                    if (File.Exists(exe)) return exe;
                    var cmd = Path.Combine(dir, command + ".cmd");
                    if (File.Exists(cmd)) return cmd;
                }
                else
                {
                    var path = Path.Combine(dir, command);
                    if (File.Exists(path)) return path;
                }
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

    private static IEnumerable<string> Candidates(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Home, ".local", "bin", command + ".exe");
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (appData.Length > 0) yield return Path.Combine(appData, "npm", command + ".cmd");
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (localAppData.Length > 0) yield return Path.Combine(localAppData, "Chickxel", "Node", command + ".cmd");
            foreach (var dir in PathEntries())
            {
                yield return Path.Combine(dir, command + ".exe");
                yield return Path.Combine(dir, command + ".cmd");
            }
            yield break;
        }
        foreach (var dir in PathEntries()) yield return Path.Combine(dir, command);
        foreach (var dir in UnixProbeDirectories()) yield return Path.Combine(dir, command);
    }

    /// <summary>macOS·Linux에서 CLI(와 npm 설치면 node)가 흔히 있는 폴더.</summary>
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

    /// <summary>자식 프로세스에 줄 PATH: CLI가 있는 폴더 + 흔한 설치 폴더 + 지금 PATH.</summary>
    public static string AugmentedPath(string cliPath)
    {
        var dirs = new List<string>();
        if (Path.GetDirectoryName(cliPath) is { Length: > 0 } own) dirs.Add(own);
        if (OperatingSystem.IsWindows())
        {
            dirs.Add(Path.Combine(Home, ".local", "bin"));
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (localAppData.Length > 0) dirs.Add(Path.Combine(localAppData, "Chickxel", "Node"));
            if (appData.Length > 0) dirs.Add(Path.Combine(appData, "npm"));
        }
        else dirs.AddRange(UnixProbeDirectories());
        dirs.AddRange(PathEntries());
        var cmp = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return string.Join(Path.PathSeparator, dirs.Distinct(cmp));
    }

    /// <summary>
    /// CLI를 창 없이 띄우는 시작 정보(표준 출력·오류는 UTF-8로 받음, PATH 보강). npm의 Codex 셰임(<c>codex.cmd</c>)이면 cmd.exe를 거치지 않고 그 안의
    /// 네이티브 <c>codex.exe</c>를 바로 띄운다(<see cref="CliProcess.ResolveCodexShim"/> — cmd.exe 명령줄 8191자 한도와 인자 다시 해석을 피함).
    /// 명령줄이 한도(<see cref="CliProcess.CmdLimit"/>·<see cref="CliProcess.ProcessLimit"/>)를 넘으면 띄우지 않고 <see cref="CommandLineTooLongException"/>.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string cliPath, IEnumerable<string> args, string? workingDirectory, bool redirectInput)
    {
        var target = OperatingSystem.IsWindows()
            ? CliProcess.ResolveCodexShim(cliPath, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture, Locate("node", null, []))
            : null;
        var psi = new ProcessStartInfo(target?.FileName ?? cliPath)
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
        foreach (var a in target?.PrefixArgs ?? []) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (OperatingSystem.IsWindows())
        {
            int limit = CliProcess.IsBatch(psi.FileName) ? CliProcess.CmdLimit : CliProcess.ProcessLimit;
            int length = CliProcess.CommandLineLength(psi.FileName, psi.ArgumentList);
            if (length > limit) throw new CommandLineTooLongException(UiStrings.FormatAi_Error_CommandLineTooLong(length: length, limit: limit));
        }
        if (workingDirectory is not null && Directory.Exists(workingDirectory)) psi.WorkingDirectory = workingDirectory;
        psi.Environment["PATH"] = AugmentedPath(cliPath);
        foreach (var (key, value) in target?.Environment ?? new Dictionary<string, string>()) psi.Environment[key] = value;
        // Claude Code 안에서 Chickxel을 띄웠어도 자식 claude가 "중첩 실행"으로 여기지 않게.
        psi.Environment.Remove("CLAUDECODE");
        return psi;
    }

    /// <summary>
    /// 자식 프로세스의 표준 오류를 줄 단위로 읽는 작업(<see cref="Process.Start()"/> 뒤에 부름 — <c>BeginErrorReadLine</c> 대신). 줄마다
    /// <see cref="CliProcess.Decode"/>로 UTF-8이 아니면 콘솔 코드 페이지로 읽는다(cmd.exe 오류처럼 CP949로 나온 글이 깨지지 않게).
    /// 표준 오류가 닫히면 끝난다.
    /// </summary>
    public static Task ReadErrorLines(Process p, Action<string> onLine)
    {
        var stream = p.StandardError.BaseStream;
        return Task.Factory.StartNew(() =>
        {
            var pending = new List<byte>();
            var buffer = new byte[4096];
            var fallback = ConsoleEncoding;
            try
            {
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                    CliProcess.SplitLines(buffer.AsSpan(0, n), pending, final: false, fallback, onLine);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // 프로세스를 끝냈거나 스트림이 닫힘.
            }
            CliProcess.SplitLines([], pending, final: true, fallback, onLine);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// 자식 프로세스가 UTF-8이 아닌 글을 낼 때 쓰는 인코딩: Windows는 OEM 코드 페이지(콘솔 기본 — cmd.exe 메시지), 다른 OS는 없음(UTF-8).
    /// </summary>
    private static Encoding? ConsoleEncoding
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding((int)GetOEMCP());
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
            {
                return null;
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    /// <summary>표준 오류를 <paramref name="tail"/>에 모은다(끝부분 수천 자만 남김 — 끝났을 때 이유로 보임). <see cref="ReadErrorLines"/>와 같이 Start 뒤에 부른다.</summary>
    public static Task CollectErrorTail(Process p, StringBuilder tail) => ReadErrorLines(p, line =>
    {
        lock (tail)
        {
            tail.AppendLine(line);
            if (tail.Length > 8000) tail.Remove(0, tail.Length - 4000);
        }
    });

    /// <summary>창 없이 한 번 실행한 결과(<see cref="Error"/>: 실행 실패·시간 초과).</summary>
    public sealed record RunResult(int ExitCode, string Stdout, string Stderr, string? Error);

    /// <summary>
    /// CLI를 홈 폴더에서 창 없이 실행해 끝날 때까지 출력을 모은다(<paramref name="timeoutSeconds"/>초가 지나면 끝내고 오류).
    /// <paramref name="environment"/>는 이 실행에만 더할 환경 변수다.
    /// </summary>
    public static async Task<RunResult> RunAsync(string path, IReadOnlyList<string> args, CancellationToken cancellationToken, int timeoutSeconds = 20,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        Process? p = null;
        try
        {
            var start = CreateStartInfo(path, args, Home, false);
            if (environment is not null)
                foreach (var (key, value) in environment) start.Environment[key] = value;
            p = Process.Start(start);
            if (p is null) return new RunResult(-1, "", "", string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_AiCli_RunAsync_F7AA02C025,name));
            var outTask = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errLines = new List<string>();
            var errTask = ReadErrorLines(p, line => { lock (errLines) errLines.Add(line); });
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(p);
                return new RunResult(-1, "", "", cancellationToken.IsCancellationRequested ? UiStrings.S_AiCli_RunAsync_865C58F1B9 : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_AiCli_RunAsync_3D25A30FE7,name,timeoutSeconds));
            }
            await errTask.ConfigureAwait(false);
            string stderr;
            lock (errLines) stderr = string.Join('\n', errLines);
            return new RunResult(p.ExitCode, await outTask.ConfigureAwait(false), stderr, null);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new RunResult(-1, "", "", string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_AiCli_RunAsync_BD859E6CD5,name,ex.Message));
        }
        finally
        {
            p?.Dispose();
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
    /// 보이는 터미널 창에서 로그인 명령(<paramref name="path"/> + <paramref name="args"/>)을 실행한다(숨은 로그인이 안 될 때 대신 쓰는 길).
    /// 실패하면 false와 이유.
    /// </summary>
    public static bool OpenLoginTerminal(string path, IReadOnlyList<string> args, out string error)
    {
        error = "";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var title = Path.GetFileNameWithoutExtension(path);
                using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"{title}\" cmd /k \"{path}\" {string.Join(' ', args)}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return true;
            }
            if (OperatingSystem.IsMacOS()) return OpenMacTerminal(path, args, out error);
            foreach (var (term, pre) in new[]
                     {
                         ("x-terminal-emulator", "-e"), ("gnome-terminal", "--"), ("konsole", "-e"), ("xfce4-terminal", "-x"), ("xterm", "-e"),
                     })
            {
                try
                {
                    StartDetached(term, [pre, path, .. args]);
                    return true;
                }
                catch (Win32Exception)
                {
                }
            }
            error = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_AiCli_OpenLoginTerminal_25DFAA69B9,Path.GetFileNameWithoutExtension(path),string.Join(' ', args));
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
    private static bool OpenMacTerminal(string path, IReadOnlyList<string> args, out string error)
    {
        error = "";
        static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
        var command = string.Join(' ', args.Prepend(path).Select(Quote));
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "chickxel-ai");
            Directory.CreateDirectory(dir);
            var script = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "-login.command");
            var pathEnv = Quote(AugmentedPath(path));
            File.WriteAllText(script,
                $"#!/bin/sh\nexport PATH={pathEnv}\n{command}\necho\nprintf '%s\\n' {Quote(UiStrings.AiCli_LoginDone)}\n",
                new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var p = Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-a", "Terminal", script }, UseShellExecute = false, CreateNoWindow = true });
            if (p is not null && p.WaitForExit(5000) && p.ExitCode == 0) return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
        }
        var apple = command.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
            error = string.IsNullOrWhiteSpace(err) ? UiStrings.S_AiCli_OpenMacTerminal_6A443C19FB : err.Trim();
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void StartDetached(string file, params IEnumerable<string> args)
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
/// 숨은 로그인 프로세스(<see cref="AiProvider.LoginArgs"/>)로 로그인한다. 출력에 나온 https 주소를 알려 주고(<see cref="UrlFound"/>),
/// 2초마다 로그인 상태를 확인해 로그인되면 <see cref="Succeeded"/>, 로그인 없이 프로세스가 끝나면 <see cref="Failed"/>.
/// 이벤트는 모두 UI 스레드에서 발생한다.
/// </summary>
public sealed class CliLogin : IDisposable
{
    private static readonly Regex UrlPattern = new(@"https://[^\s""'<>`]+", RegexOptions.Compiled);

    private readonly AiProvider _provider;
    private readonly string _path;
    private readonly StringBuilder _output = new();
    private readonly HashSet<string> _urls = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _urlTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private Process? _process;
    private bool _polling, _done;

    public CliLogin(AiProvider provider, string cliPath)
    {
        _provider = provider;
        _path = cliPath;
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
        var psi = AiCli.CreateStartInfo(_path, _provider.LoginArgs, AiCli.Home, true);
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
        _output.Append(AiCli.StripAnsi(chunk));
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
            var status = await _provider.GetAuthStatusAsync(_path);
            if (_done) return;
            if (status.LoggedIn)
            {
                Finish();
                Succeeded?.Invoke(status);
            }
            else if (final)
            {
                Finish();
                var tail = AiCli.Tail(_output.ToString().Trim(), 600);
                Failed?.Invoke(tail.Length > 0 ? tail : status.Error ?? UiStrings.S_AiCli_PollAsync_306BEB327B);
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
        if (_process is { } p) AiCli.Kill(p);
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

/// <summary>명령줄이 OS 한도를 넘어 CLI를 띄우지 않음(문구는 사용자에게 그대로 보임).</summary>
public sealed class CommandLineTooLongException(string message) : InvalidOperationException(message);
