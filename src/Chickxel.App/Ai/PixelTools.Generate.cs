using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.Core.AiDraw;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;
using Chickxel.Core.References;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>그림 생성이 끝남: 기록(정리 창으로 변환할 원본·처음 설정), 생성한 원본 그림(애니메이션이면 프레임을 나란히 이은 것), 사용자에게 보일 결과 글.</summary>
public sealed record AiDrawOutcome(AiDrawJob Job, PixelImage Original, string Message);

/// <summary>
/// 그림 생성 도구(<c>generate_pixel_art</c>): Codex 이미지 생성으로 그림을 만들고, 애니메이션 자동 모드는 프레임을
/// 이어 GIF로 저장한 뒤 다시 풀어 한 원본으로 잇는다. 수동 생성 모드는 설정대로 시트 또는 프레임별로 만든다.
/// 픽셀 변환은 자동으로 하지 않는다: 원본과 처음 변환 설정을 기록(<see cref="AiDrawJobStore"/>)에 남기고, 채팅은 원본과 "변환하기" 단추를 보인다
/// (외부 AI 연결이면 정리 창을 바로 연다) — 사용자가 정리 창에서 직접 변환해 넣는다.
/// </summary>
public sealed partial class PixelTools
{
    private const string GenerateTool = "generate_pixel_art";
    private const string AskTool = "ask_user";
    /// <summary>스타일 참고 그림을 보낼 때 키울 긴 변(px, 정수 배 최근접).</summary>
    private const int StyleImageSide = 512;

    private static readonly string[] AnimationValues = ["none", .. AiDrawActions.All, AiDrawActions.Custom];

    private readonly AiDrawJobStore _jobs = new(Path.Combine(Platform.AppFolder, "AiDraw"));
    private CancellationTokenSource? _aiDrawCts;
    /// <summary>마지막으로 생성한 첫 그림(<c>previous_result_as_subject</c> — 방금 그린 캐릭터를 움직이거나 고칠 때 같은 디자인으로).</summary>
    private string? _lastGenerated;

    /// <summary>생성 기록 보관소.</summary>
    public AiDrawJobStore Jobs => _jobs;

    /// <summary>편집기(채팅 창이 "변환하기"로 정리 창을 열 때 씀).</summary>
    public IAiHost Host => _host;

    /// <summary>지금 차례에 사용자가 붙인 그림(PNG, 픽셀 아트인지) — 보낸 순서. 채팅 창이 차례마다 넣고 끝나면 비운다.</summary>
    public IReadOnlyList<(byte[] Png, bool PixelArt)> TurnImages { get; set; } = [];
    /// <summary>지금 차례에 사용자가 보낸 글(그림 칩은 <c>[Image #N]</c>) — "생성 요청 다듬기"(자동 프롬프트)를 끄면 이 글을 그대로 이미지 생성에 보낸다. 채팅 창이 차례마다 넣고 끝나면 비운다.</summary>
    public string TurnMessage { get; set; } = "";

    /// <summary>그림 생성 진행(사용자에게 보일 짧은 글 — "프레임 2/6 생성 중"). UI 스레드.</summary>
    public event Action<string>? AiDrawProgress;
    /// <summary>Codex 이미지 생성에 실제로 보낼 지시(프레임 번호·전체 생성 횟수 포함). UI 스레드.</summary>
    public event Action<string, int, int>? AiDrawPromptReady;

    /// <summary>채팅 차례의 그림 생성이 끝나 원본을 기록에 남김(채팅이 원본과 "변환하기"를 보임). UI 스레드.</summary>
    public event Action<AiDrawOutcome>? AiDrawCompleted;

    /// <summary>오래 걸리는 도구(MCP 서버가 SSE로 답하며 기다림). 효과 만들기 검증은 느린 원문이면 병아리·칸마다 몇 초씩 걸릴 수 있다.</summary>
    public static bool IsLongRunning(string name) => name is GenerateTool or AskTool or CheckMakerTool;

    /// <summary>도는 그림 생성을 멈춘다(채팅의 멈춤).</summary>
    public void CancelAiDraw() => _aiDrawCts?.Cancel();

    /// <summary>
    /// 도구 하나를 실행한다(UI 스레드). 그림 생성은 기다리며 <paramref name="progress"/>로 진행을 알리고 <paramref name="ct"/>(클라이언트가 끊음)로 멈춘다.
    /// 효과 만들기 검증은 작업 스레드에서 도는 검증을 기다린다. 다른 도구는 <see cref="Call"/>과 같다. 예외는 밖으로 내보내지 않는다.
    /// <paramref name="external"/>: 외부 AI 연결(<see cref="McpScope.External"/>)에서 온 호출 — 채팅 차례의 편집 범위·첨부 그림·보낸 글을 쓰지 않는다.
    /// </summary>
    public async Task<JsonObject> CallAsync(string name, JsonObject? arguments, Action<string>? progress, CancellationToken ct, bool external = false)
    {
        if (name is not (GenerateTool or AskTool or CheckMakerTool)) return Call(name, arguments, external);
        try
        {
            return name switch
            {
                AskTool => await AskUserAsync(arguments, ct),
                CheckMakerTool => await CheckEffectMakerAsync(),
                _ => await GeneratePixelArtAsync(arguments, progress, ct, external),
            };
        }
        catch (ToolError e)
        {
            return TextResult($"{name}: {e.Message}", isError: true);
        }
        catch (AiDrawException e)
        {
            return TextResult($"{name}: {e.Message}", isError: true);
        }
        catch (OperationCanceledException)
        {
            return TextResult($"{name}: {UiStrings.AiDraw_Cancelled}", isError: true);
        }
        catch (Exception e)
        {
            return TextResult(string.Format(CultureInfo.CurrentCulture, UiStrings.S_PixelTools_Call_B686C7AE84, name, CleanMessage(e)), isError: true);
        }
    }

    private static IEnumerable<JsonObject> GenerateToolDefinitions()
    {
        yield return Tool(GenerateTool,
            "Draw new pixel art with Codex image generation (the user's own Codex login). The app does not convert the result automatically: " +
            "the chat shows the generated original with a Convert button, and the user converts it to pixel art in the Pixelate window " +
            "(auto-optimized grid and palette, 1px outline, background removal are chosen there) and inserts it from there. In automatic " +
            "mode, animations are requested as coherent pixel-art GIF frames: the image generator outputs PNG frames, so the app assembles " +
            "them into an animated GIF, decodes its frames, and lines them up as one original for conversion. In manual generation mode the user's animation " +
            "method (sprite sheet or individual frames) applies. The app plans poses and frame counts unless specified. " +
            "Generation takes about 30-90 seconds per image.",
            new JsonObject
            {
                ["subject"] = Str("What to draw, in English, with concrete visual details: who/what it is, body shape and proportions, outfit, colors, " +
                    "held items, expression (e.g. \"a small knight in silver armor with a blue cape, round helmet with a red plume, holding a short sword\"). " +
                    "For an animation describe the character only; the motion goes in `animation`/`poses`."),
                ["kind"] = Enum("What it is (default character).", KindValues),
                ["animation"] = Enum("Animation to make (default none = a single image). custom needs `poses`.", AnimationValues),
                ["frames"] = Int($"Number of animation frames ({AiDrawSettings.MinFrames}-{AiDrawSettings.MaxFrames}). Default: the user's setting for that animation (e.g. walk 6, idle 4), or the number of `poses`.",
                    AiDrawSettings.MinFrames, AiDrawSettings.MaxFrames),
                ["poses"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Optional English pose description per frame, in order (for custom motions or to override the built-in poses of a standard animation).",
                },
                ["view"] = Str("Camera/facing, in English (default: side view facing right for animations, front three-quarter view for a still character)."),
                ["background"] = Str("Only if the user explicitly asked for a background or scene: describe it in English. Omit otherwise; a plain transparent background is used."),
                ["size"] = Int($"Sprite size in pixels (long side, {AiDrawSettings.MinSize}-{AiDrawSettings.MaxSize}) only when the user specifies one (e.g. \"128x128\"). " +
                    "Omit otherwise: before generating, the app chooses a suitable logical pixel size from the attached subject image or the subject description. " +
                    "This is only a hint for image generation; the output size of the pixel art is chosen by the user in the Pixelate window.",
                    AiDrawSettings.MinSize, AiDrawSettings.MaxSize),
                ["subject_image"] = Int("Number N of an image the user attached to this message (written as [Image #N] in the message) that shows the character/item to draw " +
                    "(e.g. \"make this character as pixel art\"). A suitable pixel-art style reference is then added automatically.", 1),
                ["previous_result_as_subject"] = Bool("Attach the image generated by the previous generate_pixel_art call (its first frame or its sprite sheet) as the subject reference, to keep exactly the same " +
                    "character (e.g. animating or re-posing the character you just drew). Ignored when subject_image is given."),
                ["style_reference"] = Str("Reference id (from search_references) to use as the pixel-art style reference. Omit to let the app choose per the user's settings."),
                ["name"] = Str("Name for the new layer/document (default: from the subject)."),
            },
            "subject");
        yield return Tool(AskTool,
            "Ask the user to resolve an ambiguous choice before changing the document. Show the question and 2-5 mutually exclusive options above the chat input; the user can also type an Other answer or cancel. Do not guess or proceed if canceled.",
            new JsonObject
            {
                ["question"] = Str("A short specific question explaining the tradeoff."),
                ["options"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["minItems"] = 2,
                    ["maxItems"] = 5,
                    ["description"] = "Distinct actions the user can choose.",
                },
            },
            "question", "options");
    }

    /// <summary>
    /// 외부 AI 연결의 도구 목록(<see cref="McpScope.External"/>): 채팅의 그림 칩 번호 대신 <c>subject_image</c>로 PNG 그림을 base64로 받는다.
    /// <paramref name="tools"/>는 <see cref="ListTools"/>가 만든 새 배열이라 그 자리에서 고친다.
    /// </summary>
    public static void AdaptForExternal(JsonArray tools)
    {
        foreach (var tool in tools)
            if (tool?["name"]?.GetValue<string>() == GenerateTool && tool["inputSchema"]?["properties"] is JsonObject properties)
                properties["subject_image"] = Str("Base64-encoded PNG image (no data: prefix) of the character/item to draw " +
                    "(e.g. \"make this character as pixel art\"). A suitable pixel-art style reference is then added automatically.");
    }

    /// <summary>외부 AI 연결이 <c>subject_image</c>로 보낸 base64 PNG(없으면 빈 목록). PNG로 읽을 수 없으면 도구 오류.</summary>
    private static IReadOnlyList<(byte[] Png, bool PixelArt)> ExternalSubject(JsonObject? a)
    {
        if (OptString(a, "subject_image")?.Trim() is not { Length: > 0 } text) return [];
        byte[] png;
        try
        {
            png = Convert.FromBase64String(text);
            using var stream = new MemoryStream(png, writable: false);
            PngIO.Load(stream);
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or IOException or ArgumentException or NotSupportedException)
        {
            throw new ToolError(UiStrings.AiDraw_Error_ExternalImage);
        }
        return [(png, false)];
    }

    private async Task<JsonObject> AskUserAsync(JsonObject? arguments, CancellationToken ct)
    {
        var question = ReqString(arguments, "question").Trim();
        var options = OptStringList(arguments, "options");
        if (question.Length == 0 || options.Count is < 2 or > 5 || options.Any(string.IsNullOrWhiteSpace))
            throw new ToolError(UiStrings.AiQuestion_Invalid);
        var answer = await _host.AskUserAsync(question, options, ct);
        if (answer is null) throw new ToolError(UiStrings.AiQuestion_Cancelled);
        return TextResult(options.Contains(answer) ? UiStrings.FormatAiQuestion_Selected(choice: answer)
            : UiStrings.FormatAiQuestion_Custom(answer: answer));
    }

    private async Task<JsonObject> GeneratePixelArtAsync(JsonObject? a, Action<string>? progress, CancellationToken ct, bool external)
    {
        if (_aiDrawCts is not null) throw new ToolError(UiStrings.AiDraw_Error_Busy);
        // 외부 연결은 채팅 차례의 편집 범위·첨부 그림·보낸 글과 관계없다(채팅이 같은 때 돌아도).
        var scope = external ? null : Scope;
        var turnImages = external ? ExternalSubject(a) : TurnImages;
        var turnMessage = external ? "" : TurnMessage;
        var options = _host.Settings.AiDraw;
        var subject = ReqString(a, "subject").Trim();
        if (subject.Length == 0) throw new ToolError(UiStrings.AiDraw_Error_NoSubject);
        // "생성 요청 다듬기"(자동 프롬프트)를 끄면 사용자 글을 그대로 한 번 생성해 한 장으로 변환한다(애니메이션 계획·자세 없음).
        bool autoPrompt = options.AutoPrompt;
        var kind = OptEnum(a, "kind", KindValues) ?? ReferenceKinds.Character;
        var animation = OptEnum(a, "animation", AnimationValues) is { } an && an != "none" && autoPrompt ? an : null;
        var poses = OptStringList(a, "poses");
        if (animation == AiDrawActions.Custom && poses.Count == 0) throw new ToolError(UiStrings.AiDraw_Error_CustomPoses);
        int? frames = OptInt(a, "frames", AiDrawSettings.MinFrames, AiDrawSettings.MaxFrames);
        // 사용자가 크기를 지정했으면 그대로 생성 프롬프트의 크기 힌트로 쓰고, 없으면 생성 전 영어 판정으로 정한다(생성 요청 다듬기를 켰을 때).
        int? requestedSize = OptInt(a, "size", AiDrawSettings.MinSize, AiDrawSettings.MaxSize);
        int size = requestedSize ?? 0;
        int? subjectIndex = external
            ? (turnImages.Count > 0 ? 0 : null)
            : OptInt(a, "subject_image", 1, Math.Max(1, turnImages.Count)) is { } number ? number - 1 : null;
        if (subjectIndex >= turnImages.Count) throw new ToolError(UiStrings.AiDraw_Error_NoImageAttached);
        var styleId = OptString(a, "style_reference")?.Trim();
        var name = OptString(a, "name")?.Trim() is { Length: > 0 } n ? n : ShortName(subject);
        var request = new AiDrawRequest
        {
            Subject = subject,
            Kind = kind,
            Action = animation,
            View = OptString(a, "view"),
            Background = OptString(a, "background"),
            SizeTarget = size,
        };

        // 결과를 넣지 않으므로(사용자가 정리 창에서 변환해 넣음) 문서가 없어도 된다.
        var codexPath = AiProvider.Codex.Locate(_host.Settings) ?? throw new ToolError(UiStrings.AiDraw_Error_NoCodex);

        int count = animation is null ? 1 : frames ?? (poses.Count >= AiDrawSettings.MinFrames ? poses.Count : options.FramesFor(animation));
        var plan = AiDrawPlanner.Plan(animation, count, poses);
        var jobId = _jobs.Create();
        var dir = _jobs.DirectoryOf(jobId);

        // 함께 보낼 그림: 사용자가 붙인 대상 그림, 픽셀 아트 스타일 참고.
        // "생성 요청 다듬기"(자동 프롬프트)를 끄면 사용자 글의 [Image #N]이 N번째 그림을 가리키게 붙인 그림을 모두 순서대로 보낸다.
        var turnFiles = new List<string>();
        if (!autoPrompt)
            for (int i = 0; i < turnImages.Count; i++)
            {
                var file = Path.Combine(dir, $"image-{i + 1}.png");
                await File.WriteAllBytesAsync(file, turnImages[i].Png, ct);
                turnFiles.Add(file);
            }
        string? subjectFile = null;
        bool subjectIsPixelArt = false;
        if (subjectIndex is { } si)
        {
            if (autoPrompt)
            {
                subjectFile = Path.Combine(dir, "subject.png");
                await File.WriteAllBytesAsync(subjectFile, turnImages[si].Png, ct);
            }
            subjectIsPixelArt = turnImages[si].PixelArt;
        }
        else if (OptBool(a, "previous_result_as_subject") == true)
        {
            if (_lastGenerated is not { } previous || !File.Exists(previous)) throw new ToolError(UiStrings.AiDraw_Error_NoPrevious);
            subjectFile = Path.Combine(dir, "subject.png");
            File.Copy(previous, subjectFile, overwrite: true);
            // 이미 픽셀 아트 그림이라 스타일 참고는 붙이지 않는다.
            subjectIsPixelArt = true;
            // "생성 요청 다듬기"(자동 프롬프트)를 끄면 붙인 그림 뒤에 함께 보낸다(사용자 글이 방금 그린 그림을 이어 그리라고 함).
            if (!autoPrompt) turnFiles.Add(subjectFile);
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _aiDrawCts = cts;
        try
        {
            var generator = new CodexImageGenerator(codexPath, _host.Settings.CodexModel);
            if (autoPrompt && requestedSize is null)
            {
                Report(progress, UiStrings.AiDraw_Progress_Size);
                size = await generator.SuggestSizeAsync(subject, subjectFile, cts.Token);
                request = request with { SizeTarget = size };
            }
            var style = autoPrompt ? StyleReference(options, styleId, subject, kind, size, subjectFile is not null && !subjectIsPixelArt, subjectIsPixelArt) : null;
            string? styleFile = null;
            if (style is not null)
            {
                styleFile = Path.Combine(dir, "style.png");
                PngIO.Save(ScaleUpTo(style.Frame, StyleImageSide), styleFile);
            }

            var prompts = new List<string>(count);
            // 처음 그림에 붙일 것: 대상 그림, 스타일 참고.
            var firstRoles = new List<AiDrawImageRole>();
            var firstFiles = new List<string>();
            if (subjectFile is not null) { firstRoles.Add(AiDrawImageRole.Subject); firstFiles.Add(subjectFile); }
            if (styleFile is not null) { firstRoles.Add(AiDrawImageRole.Style); firstFiles.Add(styleFile); }
            bool animatedGif = count > 1 && options.Mode == AiDrawMode.Auto && scope is null;
            bool sheet = count > 1 && !animatedGif && options.AnimationMethod == AiDrawAnimationMethod.SingleSheet;
            PixelImage source;
            string firstImage;
            var layout = AiDrawSheetLayout.For(count);
            if (!autoPrompt)
            {
                Report(progress, UiStrings.AiDraw_Progress_Image);
                var prompt = turnMessage.Trim() is { Length: > 0 } typed ? typed : subject;
                prompts.Add(prompt);
                firstImage = _jobs.FramePath(jobId, 0);
                var instruction = AiDrawPrompts.CodexInstruction(prompt, request.Transparent, turnFiles.Count);
                AiDrawPromptReady?.Invoke(instruction, 1, 1);
                await generator.GenerateAsync(instruction, turnFiles, firstImage, cts.Token);
                source = LoadGenerated(firstImage);
            }
            else if (sheet)
            {
                // 애니메이션 전체를 한 번에 한 장(스프라이트 시트)으로 — 같은 그림 안이라 프레임끼리 캐릭터가 맞는다.
                Report(progress, UiStrings.FormatAiDraw_Progress_Sheet(count: count));
                var prompt = AiDrawPrompts.Sheet(request, plan, layout, firstRoles);
                prompts.Add(prompt);
                firstImage = _jobs.SheetPath(jobId);
                var instruction = AiDrawPrompts.CodexInstruction(prompt, request.Transparent, firstFiles.Count, landscape: true);
                AiDrawPromptReady?.Invoke(instruction, 1, 1);
                await generator.GenerateAsync(instruction, firstFiles, firstImage, cts.Token);
                source = LoadGenerated(firstImage);
            }
            else
            {
                var images = new List<PixelImage>(count);
                foreach (var f in plan)
                {
                    Report(progress, count == 1 ? UiStrings.AiDraw_Progress_Image : UiStrings.FormatAiDraw_Progress_Frame(index: f.Index + 1, count: count));
                    var roles = new List<AiDrawImageRole>();
                    var files = new List<string>();
                    if (f.Index == 0)
                    {
                        roles.AddRange(firstRoles);
                        files.AddRange(firstFiles);
                    }
                    else
                    {
                        roles.Add(AiDrawImageRole.FirstFrame);
                        files.Add(_jobs.FramePath(jobId, 0));
                        if (f.Index > 1) { roles.Add(AiDrawImageRole.PreviousFrame); files.Add(_jobs.FramePath(jobId, f.Index - 1)); }
                    }
                    var prompt = animatedGif ? AiDrawPrompts.GifFrame(request, f, roles) : AiDrawPrompts.Frame(request, f, roles);
                    prompts.Add(prompt);
                    var output = _jobs.FramePath(jobId, f.Index);
                    var instruction = AiDrawPrompts.CodexInstruction(prompt, request.Transparent, files.Count);
                    AiDrawPromptReady?.Invoke(instruction, f.Index + 1, count);
                    await generator.GenerateAsync(instruction, files, output, cts.Token);
                    images.Add(LoadGenerated(output));
                }
                firstImage = _jobs.FramePath(jobId, 0);
                if (animatedGif)
                {
                    // 이미지 생성 도구는 PNG만 반환한다. 프레임을 움짤로 묶어 저장하고, 그 움짤을 다시 풀어
                    // 이미지→픽셀 변환기에 보낸다(프레임 순서·공유 팔레트 유지).
                    var gifPath = Path.Combine(dir, "animation.gif");
                    Report(progress, UiStrings.AiDraw_Progress_Converting);
                    source = await Task.Run(() =>
                    {
                        var gifFrames = images.Select(i => new GifFrame(i, AiDrawActions.DurationMs(animation))).ToArray();
                        cts.Token.ThrowIfCancellationRequested();
                        GifWriter.Save(gifFrames, gifPath, new GifOptions());
                        cts.Token.ThrowIfCancellationRequested();
                        var decoded = GifReader.Load(gifPath, count);
                        if (decoded.Images.Count != count) throw new AiDrawException(UiStrings.AiDraw_Error_Read);
                        return AiDrawConversion.Combine(decoded.Images);
                    }, cts.Token);
                }
                else
                    source = AiDrawConversion.Combine(images);
            }

            // 변환은 하지 않는다: 원본과 그 안의 프레임 격자만 기록에 남기고 사용자가 정리 창에서 변환한다.
            var arranged = sheet ? layout : new AiDrawSheetLayout(count, 1);
            int duration = AiDrawActions.DurationMs(animation);
            var job = new AiDrawJob
            {
                Id = jobId,
                Name = name,
                Frames = count,
                Columns = arranged.Columns,
                Rows = arranged.Rows,
                Action = animation,
                Prompts = prompts,
                CreatedUtc = DateTime.UtcNow,
                FrameDurationMs = duration,
            };
            _jobs.Save(job, source);
            _lastGenerated = firstImage;
            // 외부 AI 연결은 채팅 카드가 없으므로 정리 창을 바로 연다.
            if (external) _host.ShowPixelate(PixelateInputOf(job, source));
            else AiDrawCompleted?.Invoke(new AiDrawOutcome(job, source, UiStrings.FormatAiDraw_Generated(name: name)));

            var (png, zoom) = Enlarge(source, null);
            var summary = (animatedGif ? $"Generated {count} animation frames and assembled an animated GIF" : sheet ? $"Generated one sprite sheet with {count} frames" : $"Generated {count} image(s)") +
                          $" with Codex image generation (\"{name}\", {source.Width}x{source.Height} px original" +
                          (style is not null ? $", pixel-art style reference: {style.Item.Id}" : "") +
                          $"). The image shows the generated original{(count > 1 ? " frames side by side" : "")} at {zoom}. " +
                          "It was NOT converted or inserted: " +
                          (external ? "the Pixelate window has been opened with this original so the user can convert it to pixel art and insert it manually. "
                                    : "the chat shows the original with a \"Convert\" button that opens the Pixelate window, where the user converts it to pixel art and inserts it manually. ") +
                          (autoPrompt ? "" : "The user turned off the automatic prompt: their message was sent to image generation as written, without animation frames. ") +
                          "Reply briefly.";
            return ImageResult(png, summary);
        }
        finally
        {
            _aiDrawCts = null;
        }
    }

    /// <summary>생성 기록의 원본을 정리 창에 넣을 모양: 프레임이 여럿이고 격자가 맞으면 칸마다 한 프레임(정리 창이 그 격자로 나눔), 아니면 한 장.</summary>
    public static PixelateInput PixelateInputOf(AiDrawJob job, PixelImage source)
        => job.Frames > 1 && job.Columns * job.Rows >= job.Frames
            ? new PixelateInput(source, job.Name, null, job.Frames, job.Columns, job.Rows)
            : new PixelateInput(source, job.Name, null);

    private void Report(Action<string>? progress, string text)
    {
        progress?.Invoke(text);
        AiDrawProgress?.Invoke(text);
    }

    /// <summary>
    /// 스타일 참고 그림(없으면 null). 인자로 id를 주면 그것, 설정이 "사용 안 함"이면 없음, "지정"이면 그 자료, "자동"이면 보관소에서 고른다:
    /// 사용자가 픽셀 아트가 아닌 그림을 대상으로 붙였으면 늘, 그림을 붙이지 않았으면 대상 설명과 글이 맞는 자료가 있을 때만, 픽셀 아트를 붙였으면 없음.
    /// </summary>
    private StyleChoice? StyleReference(AiDrawSettings options, string? explicitId, string subject, string kind, int size, bool restyle, bool subjectIsPixelArt)
    {
        var lib = Library();
        ReferenceItem? item;
        if (!string.IsNullOrEmpty(explicitId))
            item = lib.Find(explicitId) is { IsImage: true } found ? found : throw new ToolError(UiStrings.FormatRefs_Tool_NotFound(id: explicitId));
        else
        {
            switch (options.StyleReference)
            {
                case AiDrawStyleReference.None:
                    return null;
                case AiDrawStyleReference.Specific:
                    item = lib.Find(options.StyleReferenceId) is { IsImage: true } chosen ? chosen : null;
                    break;
                default:
                    if (subjectIsPixelArt) return null;
                    var scores = lib.Search(new ReferenceQuery { Text = subject, IncludeNotes = false, IncludeRules = false, Limit = 0 })
                        .ToDictionary(h => h.Item.Id, h => h.Score, StringComparer.Ordinal);
                    item = AiDrawStyle.Pick(lib.AllItems, scores, kind, size, requireText: !restyle);
                    break;
            }
        }
        if (item is null) return null;
        try
        {
            return new StyleChoice(item, lib.LoadFrames(item).Images[0]);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record StyleChoice(ReferenceItem Item, PixelImage Frame);

    /// <summary>정수 배 최근접으로 긴 변이 <paramref name="side"/>에 가깝게(넘지 않게, 적어도 1배) 키운다.</summary>
    private static PixelImage ScaleUpTo(PixelImage src, int side)
    {
        int k = Math.Max(1, side / Math.Max(src.Width, src.Height));
        if (k == 1) return src;
        var dst = new PixelImage(src.Width * k, src.Height * k);
        for (int y = 0; y < dst.Height; y++)
            for (int x = 0; x < dst.Width; x++)
                dst.Pixels[y * dst.Width + x] = src.Pixels[(y / k) * src.Width + x / k];
        return dst;
    }

    /// <summary>생성한 그림 파일(PNG — 못 읽으면 SkiaSharp). 못 읽으면 도구 오류.</summary>
    private static PixelImage LoadGenerated(string path)
    {
        try
        {
            return PngIO.Load(path);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException)
        {
            return ImageUtil.DecodeFrames(path) is { Images.Count: > 0 } f ? f.Images[0] : throw new AiDrawException(UiStrings.AiDraw_Error_Read);
        }
    }

    /// <summary>대상 설명 앞부분으로 만든 이름(레이어·문서).</summary>
    private static string ShortName(string subject)
    {
        var words = subject.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(4);
        var s = string.Join(' ', words).Trim(',', '.', ';');
        return s.Length > 32 ? s[..32] : s.Length == 0 ? "AI" : s;
    }

    private static List<string> OptStringList(JsonObject? a, string key)
    {
        if (Arg(a, key) is not { } n) return [];
        if (n is not JsonArray arr) throw new ToolError(string.Format(CultureInfo.CurrentCulture, UiStrings.S_PixelTools_ReqArray_C2A791FDD4, key));
        return [.. arr.Select(v => AsString(v)?.Trim() ?? "")];
    }
}
