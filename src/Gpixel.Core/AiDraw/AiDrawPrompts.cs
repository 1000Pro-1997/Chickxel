using System.Text;

namespace Gpixel.Core.AiDraw;

/// <summary>
/// 그릴 것: 대상 설명(영어), 종류(참고 자료 종류 — character·item·tile·effect·ui·other), 동작(없으면 그림 하나), 보는 방향,
/// 배경(사용자가 배경을 말했을 때만, 없으면 투명한 빈 배경), 결과 크기 목표(0 = 자동).
/// </summary>
public sealed record AiDrawRequest
{
    public required string Subject { get; init; }
    public string Kind { get; init; } = "character";
    public string? Action { get; init; }
    public string? View { get; init; }
    public string? Background { get; init; }
    public int SizeTarget { get; init; }

    /// <summary>배경을 말하지 않았으면 투명 배경으로 만든다.</summary>
    public bool Transparent => string.IsNullOrWhiteSpace(Background);

    public bool Animated => !string.IsNullOrWhiteSpace(Action);
}

/// <summary>생성에 함께 보내는 그림의 역할(보내는 순서 = 프롬프트의 Image 1, 2, …).</summary>
public enum AiDrawImageRole
{
    /// <summary>그릴 캐릭터·물건(사용자가 붙인 그림): 디자인·색·옷을 그대로.</summary>
    Subject,
    /// <summary>픽셀 아트 스타일 참고(참고 자료 보관소): 그림체만 따르고 대상은 따르지 않음.</summary>
    Style,
    /// <summary>애니메이션의 첫 프레임: 같은 캐릭터·크기·카메라.</summary>
    FirstFrame,
    /// <summary>바로 앞 프레임: 자세가 이어지게.</summary>
    PreviousFrame,
}

/// <summary>스프라이트 시트의 칸 배치: 가로 칸 수 × 줄 수(읽는 순서 = 왼쪽에서 오른쪽, 위에서 아래).</summary>
public readonly record struct AiDrawSheetLayout(int Columns, int Rows)
{
    /// <summary>한 줄에 둘 최대 프레임 수(넘으면 두 줄 격자).</summary>
    public const int MaxSingleRow = 6;

    /// <summary>프레임 <paramref name="frames"/>개의 배치: <see cref="MaxSingleRow"/>개까지 한 줄, 넘으면 두 줄(위 줄이 같거나 하나 더 많음).</summary>
    public static AiDrawSheetLayout For(int frames)
    {
        frames = Math.Max(1, frames);
        return frames <= MaxSingleRow ? new AiDrawSheetLayout(frames, 1) : new AiDrawSheetLayout((frames + 1) / 2, 2);
    }
}

/// <summary>이미지 생성 프롬프트(모델이 읽으므로 영어). 픽셀 아트처럼 보이고 변환하기 좋은 그림이 나오게 지시를 붙인다.</summary>
public static class AiDrawPrompts
{
    /// <summary>프레임 하나의 생성 프롬프트. <paramref name="images"/> = 함께 보내는 그림의 역할(순서대로 Image 1, 2, …).</summary>
    public static string Frame(AiDrawRequest r, AiDrawFrame f, IReadOnlyList<AiDrawImageRole> images)
    {
        var sb = new StringBuilder();
        AppendOpening(sb, r, sheet: false, HasSubjectImage(images));
        sb.Append("Pixel-art ").Append(What(r)).Append(": ").Append(Subject(r)).Append('.').AppendLine();
        if (r.Animated)
        {
            sb.Append("Animation \"").Append(r.Action!.Trim()).Append("\", frame ").Append(f.Index + 1).Append(" of ").Append(f.Count).Append('.');
            if (f.Pose.Length > 0) sb.Append(" Pose in this frame: ").Append(f.Pose.TrimEnd('.')).Append('.');
            sb.AppendLine();
        }
        else if (f.Pose.Length > 0) sb.Append("Pose: ").Append(f.Pose.TrimEnd('.')).Append('.').AppendLine();

        AppendStyle(sb, r, "the whole sprite", "a small sprite of roughly 32 to 64 pixels tall", HasSubjectImage(images), sheet: false);
        sb.Append("Composition: exactly one ").Append(r.Kind == "character" ? "character, full body" : "subject").Append(", ").Append(View(r, images))
          .Append(", centered, the whole sprite visible with empty margin on every side, nothing cropped");
        if (r.SizeTarget > 0) sb.Append(" within a square ").Append(r.SizeTarget).Append('x').Append(r.SizeTarget).Append(" pixel canvas");
        sb.AppendLine(".");
        if (r.Animated)
        {
            sb.Append("Keep the same subject design, size, scale, camera, lighting and canvas position as the other frames; only the requested motion changes.");
            if (r.Kind == "character") sb.Append(" The feet stay on the same ground line unless the pose leaves the ground.");
            sb.AppendLine();
        }
        AppendBackground(sb, r);
        AppendPixelRules(sb, r, sheet: false);
        AppendImages(sb, images, poseMayChange: r.Animated || f.Pose.Length > 0);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 애니메이션 전체를 한 장에 그리는 스프라이트 시트 프롬프트: 같은 캐릭터의 프레임 <paramref name="frames"/>개를 <paramref name="layout"/> 격자에
    /// 같은 간격·크기·카메라·바닥선으로, 프레임마다 자세 목록(1: …, 2: …), 투명 배경, 글자·번호·칸 선 없이. 가로로 긴 그림을 부탁한다.
    /// </summary>
    public static string Sheet(AiDrawRequest r, IReadOnlyList<AiDrawFrame> frames, AiDrawSheetLayout layout, IReadOnlyList<AiDrawImageRole> images)
    {
        int n = frames.Count;
        var sb = new StringBuilder();
        AppendOpening(sb, r, sheet: true, HasSubjectImage(images));
        sb.Append("Pixel-art ").Append(What(r)).Append(" sprite sheet: a single wide landscape image containing exactly ").Append(n)
          .Append(" frames of the same ").Append(r.Kind == "character" ? "character" : "subject").Append("'s \"").Append(r.Action?.Trim() ?? "animation").Append("\" ")
          .Append(r.Action is { } act && AiDrawActions.Loops(act.Trim().ToLowerInvariant()) ? "cycle" : "animation").Append(". Subject: ").Append(Subject(r)).Append('.').AppendLine();
        if (layout.Rows <= 1)
            sb.Append("Layout: all ").Append(n).AppendLine(" frames in ONE horizontal row, ordered left to right, evenly spaced with the same gap between frames and a clear empty gap so no two frames touch or overlap.");
        else
            sb.Append("Layout: a grid of ").Append(layout.Columns).Append(" columns by ").Append(layout.Rows).Append(" rows, frames ordered left to right then top to bottom (frames 1-")
              .Append(layout.Columns).Append(" in the top row, frames ").Append(layout.Columns + 1).Append('-').Append(n)
              .AppendLine(" in the bottom row), evenly spaced cells with a clear empty gap so no two frames touch or overlap.");
        sb.Append("Every frame shows the identical ").Append(r.Kind == "character" ? "character" : "subject").Append(" design, colors, proportions, pixel size, scale, camera angle and lighting; each frame shows the whole subject, the same size, centered in its cell");
        if (r.SizeTarget > 0) sb.Append(" on a logical ").Append(r.SizeTarget).Append('x').Append(r.SizeTarget).Append(" pixel canvas");
        sb.AppendLine(r.Kind == "character" ? " and on the same baseline unless the pose leaves the ground. Only the pose changes between frames." : ". Only the requested animation changes between frames.");
        sb.AppendLine("Frames:");
        foreach (var f in frames)
            sb.Append(f.Index + 1).Append(": ").Append(f.Pose.Length > 0 ? f.Pose.TrimEnd('.') : "the next step of the motion").AppendLine();
        AppendStyle(sb, r, "each frame", "each frame a small sprite of roughly 32 to 64 pixels tall", HasSubjectImage(images), sheet: true);
        sb.Append("View: ").Append(View(r, images)).AppendLine(" in every frame.");
        AppendBackground(sb, r);
        AppendPixelRules(sb, r, sheet: true);
        sb.AppendLine("No labels, no frame numbers, no text, no grid lines, no borders or boxes around the frames.");
        AppendImages(sb, images, poseMayChange: true);
        return sb.ToString().TrimEnd();
    }

    private static void AppendOpening(StringBuilder sb, AiDrawRequest r, bool sheet, bool hasSubjectImage)
    {
        if (hasSubjectImage) sb.AppendLine("Convert the attached reference image into authentic, hand-crafted pixel art.");
        if (r.SizeTarget <= 0) return;
        sb.Append("Create a true ").Append(r.SizeTarget).Append('x').Append(r.SizeTarget)
          .Append(sheet ? " logical pixel-art sprite for each frame" : " pixel-art sprite")
          .AppendLine(", not a high-resolution image with a pixelation filter.");
    }

    private static void AppendPixelRules(StringBuilder sb, AiDrawRequest r, bool sheet)
    {
        if (r.SizeTarget <= 0) return;
        sb.Append("Important pixel-art rules: Every logical pixel must have the same size and align to a consistent grid. ")
          .Append("Use intentional pixel clusters and pixel-perfect outlines. ");
        if (sheet)
            sb.Append("Keep every frame at the same logical ").Append(r.SizeTarget).Append('x').Append(r.SizeTarget)
              .Append(" pixel resolution; scale the full sheet with nearest-neighbor if the image tool cannot output at that resolution.");
        else
            sb.Append("Output exactly ").Append(r.SizeTarget).Append('x').Append(r.SizeTarget)
              .Append(" pixels if supported; otherwise preserve the appearance of a ").Append(r.SizeTarget).Append('x').Append(r.SizeTarget)
              .Append(" logical sprite with nearest-neighbor scaling.");
        sb.AppendLine();
    }

    private static string What(AiDrawRequest r) => r.Kind switch
    {
        "item" => "game item icon",
        "tile" => "game tile",
        "effect" => "game visual effect sprite",
        "ui" => "game UI element",
        _ => "game character sprite",
    };

    private static bool HasSubjectImage(IReadOnlyList<AiDrawImageRole> images)
    {
        for (int i = 0; i < images.Count; i++)
            if (images[i] == AiDrawImageRole.Subject) return true;
        return false;
    }

    private static string Subject(AiDrawRequest r) => r.Subject.Trim().TrimEnd('.');

    private static string View(AiDrawRequest r, IReadOnlyList<AiDrawImageRole> images) => !string.IsNullOrWhiteSpace(r.View) ? r.View!.Trim().TrimEnd('.')
        : HasSubjectImage(images) ? "the same view as the subject image"
        : r.Animated ? "side view facing right" : r.Kind == "character" ? "front three-quarter view" : "front view";

    private static void AppendStyle(StringBuilder sb, AiDrawRequest r, string sizeOf, string autoSize, bool hasSubjectImage, bool sheet)
    {
        if (hasSubjectImage)
        {
            sb.Append("Style: (masterpiece, best quality), authentic hand-crafted 16-bit JRPG pixel art, a strict uniform grid of large, crisp square pixels; ");
            if (r.SizeTarget > 0)
                sb.Append(sizeOf).Append(" drawn at a logical ").Append(r.SizeTarget).Append('x').Append(r.SizeTarget)
                  .Append(sheet ? " pixel resolution in its square animation cell; " : " pixel resolution on a square canvas; ");
            else
                sb.Append(autoSize).Append("; ");
            sb.AppendLine("intentional chunky pixel clusters for hair, eyes, hands, clothing folds and shadows where applicable; prioritize a readable silhouette over tiny details; limited 24-color palette, clean dark pixel-perfect outline, flat colors and 2-step cel shading with hard pixel edges. No pixelation filter, mixed pixel sizes, anti-aliasing, gradients, blur, smooth lines, dithering, high-resolution anime, vector art, 3D rendering or mosaic effects; no extra fingers or limbs, text, watermark or UI.");
            return;
        }
        sb.Append("Style: authentic retro pixel art with clearly visible square pixels, all pixels the same size");
        if (r.SizeTarget > 0)
            sb.Append(", ").Append(sizeOf).Append(" drawn on a logical ").Append(r.SizeTarget).Append('x').Append(r.SizeTarget).Append(" pixel grid (each pixel a big crisp square block)");
        else sb.Append(", ").Append(autoSize).Append(" (each pixel a big crisp square block)");
        sb.AppendLine(", limited palette of about 12 to 24 colors, flat cel shading with 2 or 3 shades per color, a clean 1-pixel dark outline around the silhouette, no anti-aliasing, no gradients, no blur, no soft glow, no noise or dithering, no text, no watermark, no UI.");
    }

    private static void AppendBackground(StringBuilder sb, AiDrawRequest r)
    {
        if (r.Transparent) sb.AppendLine("Background: plain transparent background, no scenery, no floor, no cast shadow, no frame or border.");
        else sb.Append("Background: ").Append(r.Background!.Trim().TrimEnd('.')).AppendLine(", drawn in the same pixel-art style.");
    }

    private static void AppendImages(StringBuilder sb, IReadOnlyList<AiDrawImageRole> images, bool poseMayChange)
    {
        if (images.Count == 0) return;
        sb.AppendLine("Attached images:");
        for (int i = 0; i < images.Count; i++)
        {
            sb.Append("- Image ").Append(i + 1).Append(": ").AppendLine(images[i] switch
            {
                AiDrawImageRole.Subject => "the subject to draw, not a style reference. Preserve its identity, silhouette, proportions, facial features, colors, clothing and distinguishing details; redraw the actual subject as pixel art, not a generic substitute. "
                    + (poseMayChange ? "Change the original pose only as required by the requested action or frame pose." : "Preserve its original pose unless the request explicitly asks to change it."),
                AiDrawImageRole.Style => "pixel-art style reference only. Match its pixel size, outline, palette treatment and shading; do NOT copy its subject, shape or colors.",
                AiDrawImageRole.FirstFrame => "frame 1 of this animation. Draw the identical subject (same design, colors, proportions, pixel size, scale and position on the canvas); only change the pose as needed for the animation.",
                _ => "the previous frame of this animation. Continue the motion smoothly from it, with the same scale and camera.",
            });
        }
    }

    /// <summary>
    /// Codex에 보내는 지시: 이미지 생성 도구를 이 프롬프트로 정확히 한 번 부르고(투명 배경 여부, <paramref name="landscape"/>면 크기·비율을 고를 수 있을 때
    /// 가로로 긴 1536x1024), 다른 일은 하지 말고 "done"만 답하게.
    /// </summary>
    public static string CodexInstruction(string prompt, bool transparent, int attachedImages, bool landscape = false)
        => "Call your image generation tool exactly once, with transparent_background set to " + (transparent ? "true" : "false")
           + (landscape ? " and, if the tool accepts a size or aspect ratio, a wide landscape size such as 1536x1024" : "")
           + (attachedImages > 0 ? ", using the attached image(s) as described in the prompt" : "")
           + ", with exactly the prompt below (do not shorten or rewrite it). Do not use any other tool, do not edit or save files, and do not ask questions. "
           + "After the image is generated, reply with only the word: done\n\nPrompt:\n" + prompt;
}
