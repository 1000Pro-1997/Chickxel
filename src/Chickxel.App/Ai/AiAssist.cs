using System.Text;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>
/// 사용자가 켜고 끄는 MCP 도구 묶음. 묶음에 들지 않는 도구(문서 읽기·픽셀 그리기·현재 프레임 고르기)는 늘 켜져 있다(<see cref="AiToolGroups.Of"/>).
/// 설정에는 이름(<see cref="Enum.ToString()"/>)으로 남는다.
/// </summary>
public enum AiToolGroup
{
    Frames,
    Layers,
    Skins,
    Effects,
    ScriptEffects,
    References,
    FolderImages,
    Canvas,
    Palette,
    Documents,
    OpenFile,
    Generate,
    Ask,
}

/// <summary>도구 묶음의 도구·이름·적용 범위.</summary>
public static class AiToolGroups
{
    public static readonly IReadOnlyList<AiToolGroup> All = Enum.GetValues<AiToolGroup>();

    private static readonly Dictionary<string, AiToolGroup> ToolGroup = Build();

    private static Dictionary<string, AiToolGroup> Build()
    {
        var map = new Dictionary<string, AiToolGroup>(StringComparer.Ordinal);
        foreach (var g in All)
            foreach (var tool in ToolsOf(g))
                map.Add(tool, g);
        return map;
    }

    /// <summary>묶음의 도구 이름(MCP 도구 목록과 같은 이름).</summary>
    public static IReadOnlyList<string> ToolsOf(AiToolGroup g) => g switch
    {
        AiToolGroup.Frames => ["add_frame", "delete_frame", "set_frame_duration", "add_tag", "link_cels", "unlink_cels", "set_link_color"],
        AiToolGroup.Layers => ["add_layer", "set_layer", "add_group", "add_part", "set_part"],
        AiToolGroup.Skins => ["list_skins", "add_skin", "set_skin", "edit_skin", "list_outfits", "save_outfit", "apply_outfit"],
        AiToolGroup.Effects => ["list_effect_types", "get_effects", "add_effect", "set_effect", "remove_effect", "apply_effects", "expand_canvas_for_effects", "preview_effects"],
        AiToolGroup.ScriptEffects => ["get_script_api", "write_script_effect", "save_script_effect", "list_script_effects", "get_script_effect_source",
            "get_effect_maker_source", "set_effect_maker_source", "check_effect_maker"],
        AiToolGroup.References => ["search_references", "get_reference", "get_style_rules", "add_reference", "set_reference_info"],
        AiToolGroup.FolderImages => ["list_folder_images", "get_folder_image"],
        AiToolGroup.Canvas => ["resize_canvas", "set_canvas_pivot"],
        AiToolGroup.Palette => ["set_palette", "set_colors"],
        AiToolGroup.Documents => ["new_project", "save_project", "set_document_title"],
        AiToolGroup.OpenFile => ["open_file"],
        AiToolGroup.Generate => ["generate_pixel_art"],
        _ => ["ask_user"],
    };

    /// <summary>도구가 든 묶음(늘 켜진 도구면 null).</summary>
    public static AiToolGroup? Of(string tool) => ToolGroup.TryGetValue(tool, out var g) ? g : null;

    /// <summary>
    /// 이 범위에서 켜고 끌 수 있는 묶음인지. 채팅의 그림 생성은 그리는 방식이 정하고, 외부 연결에는 사용자에게 묻기가 없다(채팅 창이 없음).
    /// </summary>
    public static bool AppliesTo(AiToolGroup g, McpScope scope) => scope switch
    {
        McpScope.External => g != AiToolGroup.Ask,
        _ => g != AiToolGroup.Generate,
    };

    public static string Name(AiToolGroup g) => g switch
    {
        AiToolGroup.Frames => UiStrings.AiTools_Frames,
        AiToolGroup.Layers => UiStrings.AiTools_Layers,
        AiToolGroup.Skins => UiStrings.AiTools_Skins,
        AiToolGroup.Effects => UiStrings.AiTools_Effects,
        AiToolGroup.ScriptEffects => UiStrings.AiTools_ScriptEffects,
        AiToolGroup.References => UiStrings.AiTools_References,
        AiToolGroup.FolderImages => UiStrings.AiTools_FolderImages,
        AiToolGroup.Canvas => UiStrings.AiTools_Canvas,
        AiToolGroup.Palette => UiStrings.AiTools_Palette,
        AiToolGroup.Documents => UiStrings.AiTools_Documents,
        AiToolGroup.OpenFile => UiStrings.AiTools_OpenFile,
        AiToolGroup.Generate => UiStrings.AiTools_Generate,
        _ => UiStrings.AiTools_Ask,
    };

    /// <summary>풍선 글: 묶음 설명 + 도구 이름.</summary>
    public static string Tip(AiToolGroup g)
    {
        var text = g switch
        {
            AiToolGroup.Frames => UiStrings.AiTools_Frames_Tip,
            AiToolGroup.Layers => UiStrings.AiTools_Layers_Tip,
            AiToolGroup.Skins => UiStrings.AiTools_Skins_Tip,
            AiToolGroup.Effects => UiStrings.AiTools_Effects_Tip,
            AiToolGroup.ScriptEffects => UiStrings.AiTools_ScriptEffects_Tip,
            AiToolGroup.References => UiStrings.AiTools_References_Tip,
            AiToolGroup.FolderImages => UiStrings.AiTools_FolderImages_Tip,
            AiToolGroup.Canvas => UiStrings.AiTools_Canvas_Tip,
            AiToolGroup.Palette => UiStrings.AiTools_Palette_Tip,
            AiToolGroup.Documents => UiStrings.AiTools_Documents_Tip,
            AiToolGroup.OpenFile => UiStrings.AiTools_OpenFile_Tip,
            AiToolGroup.Generate => UiStrings.AiTools_Generate_Tip,
            _ => UiStrings.AiTools_Ask_Tip,
        };
        return text + "\n" + UiStrings.FormatAiTools_ToolList(tools: string.Join(", ", ToolsOf(g)));
    }

    /// <summary>모델에게 알리는 묶음 이름(영어).</summary>
    internal static string ModelName(AiToolGroup g) => g switch
    {
        AiToolGroup.Frames => "frames, tags and linked cels",
        AiToolGroup.Layers => "layers, groups and parts",
        AiToolGroup.Skins => "skins and outfits",
        AiToolGroup.Effects => "layer effects",
        AiToolGroup.ScriptEffects => "writing script effects",
        AiToolGroup.References => "the reference library",
        AiToolGroup.FolderImages => "reading drawings in the open folder",
        AiToolGroup.Canvas => "canvas size and pivot",
        AiToolGroup.Palette => "changing the palette and drawing colors",
        AiToolGroup.Documents => "new documents, saving and titles",
        AiToolGroup.OpenFile => "opening files",
        AiToolGroup.Generate => "image generation",
        _ => "asking the user",
    };
}

/// <summary>
/// 그리기 프롬프트: 대충 말해도 픽셀 아트가 잘 나오게 시스템 프롬프트(외부 연결은 MCP 안내)에 붙이는 작업별 지시. 사용자가 하나씩 켜고 끄고 글을 고칠 수 있다.
/// 설정에는 이름으로 남는다.
/// </summary>
public enum AiGuide
{
    Basics,
    Character,
    Object,
    Background,
    Motion,
    Inbetween,
    Cleanup,
    Folder,
}

/// <summary>그리기 프롬프트의 이름·기본 글(모델이 읽으므로 영어)·시스템 프롬프트에 넣을 묶음.</summary>
public static class AiGuides
{
    public static readonly IReadOnlyList<AiGuide> All = Enum.GetValues<AiGuide>();

    public static string Name(AiGuide g) => g switch
    {
        AiGuide.Basics => UiStrings.AiGuide_Basics,
        AiGuide.Character => UiStrings.AiGuide_Character,
        AiGuide.Object => UiStrings.AiGuide_Object,
        AiGuide.Background => UiStrings.AiGuide_Background,
        AiGuide.Motion => UiStrings.AiGuide_Motion,
        AiGuide.Inbetween => UiStrings.AiGuide_Inbetween,
        AiGuide.Cleanup => UiStrings.AiGuide_Cleanup,
        _ => UiStrings.AiGuide_Folder,
    };

    public static string Tip(AiGuide g) => g switch
    {
        AiGuide.Basics => UiStrings.AiGuide_Basics_Tip,
        AiGuide.Character => UiStrings.AiGuide_Character_Tip,
        AiGuide.Object => UiStrings.AiGuide_Object_Tip,
        AiGuide.Background => UiStrings.AiGuide_Background_Tip,
        AiGuide.Motion => UiStrings.AiGuide_Motion_Tip,
        AiGuide.Inbetween => UiStrings.AiGuide_Inbetween_Tip,
        AiGuide.Cleanup => UiStrings.AiGuide_Cleanup_Tip,
        _ => UiStrings.AiGuide_Folder_Tip,
    };

    /// <summary>켜야 쓸 수 있는 도구 묶음(폴더 그림 참고는 폴더 그림 읽기 도구가 있어야 함).</summary>
    public static AiToolGroup? NeedsTools(AiGuide g) => g == AiGuide.Folder ? AiToolGroup.FolderImages : null;

    /// <summary>모델에게 보이는 제목(영어).</summary>
    private static string Heading(AiGuide g) => g switch
    {
        AiGuide.Basics => "Drawing process",
        AiGuide.Character => "Characters and people",
        AiGuide.Object => "Objects and items",
        AiGuide.Background => "Backgrounds and tiles",
        AiGuide.Motion => "Drawing a motion (animation)",
        AiGuide.Inbetween => "In-betweening (adding frames between existing frames)",
        AiGuide.Cleanup => "Making the current image cleaner pixel art",
        _ => "Consistency with the user's own art",
    };

    /// <summary>기본 글(사용자가 고치지 않았거나 초기화하면 이것).</summary>
    public static string DefaultText(AiGuide g) => g switch
    {
        AiGuide.Basics =>
            """
            - Before drawing, turn the user's words AND any attached images into concrete visual criteria: requested subject, pose, full-body silhouette, head-to-body and limb proportions, part positions and overlap, colors, style, canvas size and any negative constraints. Images are optional; text-only criteria count just as much. When the request is short or vague, fill in the missing decisions yourself (size, view, palette, mood) with sensible game-sprite defaults instead of asking, and mention the main choices in your reply.
            - Plan the whole composition and shared canvas coordinates before splitting a character into part layers; use the same baseline and check the composite with get_image WITHOUT `layer` after arranging the parts. A part image by itself cannot verify the whole character.
            - Before drawing a new sprite or animation, call get_style_rules once per conversation and search_references for similar items (subject words, kind, size; similar_to_canvas when improving existing art). When a reference fits, look at it with get_reference (as="image" for overall appearance, as="pixels" for exact proportions) and follow its proportions, outline and palette style.
            - After each substantial drawing pass, call get_image WITHOUT `layer`, compare the full composite against every applicable criterion (including the user's wording, not just references), and fix problems. Score visual fidelity out of 10 for silhouette/proportions, part placement, and requested colors/style; never claim a score without seeing the composite. Repeat review and correction until acceptable or no further useful improvement, then state the actual score and remaining defects.
            """,
        AiGuide.Character =>
            """
            - Vague requests ("draw a knight", "a cute girl"): pick a clear archetype silhouette and 2-4 main colors yourself. Default to a 32x32 sprite (or the open canvas) in a front three-quarter view, full body, centered with 1-2 px margin and the feet on one baseline.
            - Proportions: small sprites (up to about 48 px tall) are chibi, 2-3 heads tall with a big readable head, eyes 1-2 px, hands 2-3 px; larger sprites 4-6 heads tall. Exaggerate the defining feature (helmet plume, ears, hat, weapon).
            - Work in passes: flat silhouette block-in with base colors → 1 px outline in a darker hue of the neighbouring fill (not pure black everywhere; lighter inner lines between parts) → 2-3 shade steps per color from one light source at the top-left, shadows hue-shifted cooler, highlights warmer → small details last.
            - Avoid pillow shading, banding, orphan single pixels, doubled outline corners and jaggies: curves use consistent step lengths (1-1-2-3, not 1-3-1-2).
            """,
        AiGuide.Object =>
            """
            - Items and objects should fill the canvas and read at 1x: icons are 16x16 or 32x32 with 1 px margin; tilt long items (swords, staffs) diagonally to use the space.
            - Use a strong recognizable silhouette and exaggerate the defining feature (blade, gem, handle, label).
            - Light from the top-left; 3-4 value ramp per material: metal = high contrast with a sharp 1-2 px specular, wood = warm mid tones with a few grain lines, glass and gems = dark rim with a bright inner reflection, cloth = soft 2-step shading.
            - 1 px outline darker than the local color; anti-alias only long curves with one in-between color. Keep the perspective consistent (top-down 3/4 for RPG items, straight side view for platformer props).
            """,
        AiGuide.Background =>
            """
            - Decide the purpose first: a full scene, a parallax layer or a tileable tile. Tiles are 16x16 or 32x32 and must repeat seamlessly: whatever touches the left edge continues at the right edge, top continues at bottom.
            - Use a limited palette (8-16 colors) with lower contrast and saturation than the characters so sprites stay readable on top; atmospheric perspective: farther = lighter, bluer, less detail.
            - Block in large shapes and light first, then add texture as deliberate clusters (grass tufts, bricks, leaves), never random single-pixel noise; vary cluster shapes to avoid visible repetition.
            - No outlines on background elements unless the art style uses them; keep a clear ground line where characters will stand.
            """,
        AiGuide.Motion =>
            """
            - Choose the frame count by motion unless the user gave one: idle 4, walk 6-8, run 6-8, attack 4-6 (anticipation → strike → impact → recovery), jump 4-6, hurt 2-3, death 5-8.
            - Draw the key poses (contact/extreme poses) first, then the breakdowns. Each frame goes in its own frame: add_frame with duplicate=true from the previous pose, then redraw only what moves. Use link_cels for layers that do not change.
            - Keep the character's volume, palette, outline and pixel size identical in every frame. Move body parts in whole pixels along arcs with a clear line of action; add a 1 px up/down bob for walk and idle; secondary motion (hair, cape, tail) follows one frame later.
            - Set durations (e.g. 80-120 ms per walk frame, longer holds on anticipation and impact) and name the range with add_tag. Check each frame with get_image and that the last frame loops back into the first.
            """,
        AiGuide.Inbetween =>
            """
            - Read the two neighbouring key frames first (get_image and get_pixels of the changing area). Insert the new frame(s) right after the first key with add_frame (after=<first key>, duplicate=true).
            - Move every changing part partway along its arc (not a straight line) between the two keys; keep the exact palette, outline and part sizes of the keys and do not invent new details.
            - For easing, place in-betweens closer to the slow end of the motion (ease in/out favours the key frames). Redistribute durations so the total timing stays about the same unless the user asks for slower or smoother motion.
            - Afterwards look at every frame of the sequence in order with get_image and fix popping or volume changes.
            """,
        AiGuide.Cleanup =>
            """
            - For "make it more pixel-art", "clean it up" or "도트답게": inspect the image first (get_image, then get_pixels per region). Keep the subject, pose, size and composition; do not redraw from scratch.
            - Reduce the colors to a coherent palette: merge near-duplicate colors with replace_color (prefer the document palette), 2-4 shades per hue.
            - Remove anti-aliasing blur, gradients, dithering noise and stray single pixels; replace them with 2-3 flat shade bands lit from one direction.
            - Make the outline 1 px and continuous, remove doubled corners, and fix jaggies so line steps are consistent. Sharpen the silhouette. Work one region at a time and verify the whole composite after each pass.
            """,
        _ =>
            """
            - The user keeps their art in the folder open in Chickxel's file browser. Before drawing something new, call list_folder_images (with a query of subject words, e.g. a character name or "tree") and look at 1-3 related or recent drawings with get_folder_image.
            - Match their scale and pixel size, outline color and thickness, palette, shading style and proportions so the new art fits the set. If nothing related exists, match the overall style of the most recent drawings.
            - These files are only references: never change them.
            """,
    };

    /// <summary>사용자가 고친 글이 있으면 그것, 없으면 기본 글.</summary>
    public static string Text(AiGuide g, IReadOnlyDictionary<string, string> custom)
        => custom.TryGetValue(g.ToString(), out var text) && !string.IsNullOrWhiteSpace(text) ? text : DefaultText(g);

    /// <summary>
    /// 시스템 프롬프트에 붙일 부분: 끈 도구 묶음 안내와 켠 그리기 프롬프트(쓸 도구가 꺼진 것은 뺌). 모두 켜져 있고 프롬프트가 없으면 빈 문자열.
    /// </summary>
    public static string Compose(AiAssistSettings settings, IReadOnlyDictionary<string, string> custom, McpScope scope)
    {
        var sb = new StringBuilder();
        var off = AiToolGroups.All.Where(g => AiToolGroups.AppliesTo(g, scope) && !settings.ToolOn(g)).ToList();
        if (off.Count > 0)
            sb.Append("- The user turned off these tools in Chickxel: ")
              .Append(string.Join("; ", off.Select(g => AiToolGroups.ModelName(g) + " (" + string.Join(", ", AiToolGroups.ToolsOf(g)) + ")")))
              .AppendLine(". Calls to them are refused; do not plan around them, and tell the user briefly if the request needs one.");
        var guides = All.Where(g => settings.GuideOn(g) && (NeedsTools(g) is not { } need || settings.ToolOn(need))).ToList();
        if (guides.Count == 0) return sb.ToString().TrimEnd();
        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine("Drawing guides (chosen by the user). Apply every guide that matches the request; several can apply at once. They describe how to work with the pixel tools; when you use generate_pixel_art instead, use them to write a better subject, poses and frame count.");
        foreach (var g in guides)
            sb.AppendLine().Append("## ").AppendLine(Heading(g)).AppendLine(Text(g, custom).Trim());
        return sb.ToString().TrimEnd();
    }
}

/// <summary>AI 도구 묶음·그리기 프롬프트 켜고 끄기(채팅과 외부 AI 연결이 따로 가짐). 끈 것만 이름으로 남겨 새로 생긴 것은 켜진 채로 시작한다.</summary>
public sealed class AiAssistSettings
{
    /// <summary>끈 도구 묶음(<see cref="AiToolGroup"/> 이름).</summary>
    public List<string> ToolsOff { get; set; } = [];

    /// <summary>끈 그리기 프롬프트(<see cref="AiGuide"/> 이름).</summary>
    public List<string> GuidesOff { get; set; } = [];

    /// <summary>외부 AI 연결의 기본값: 파일 열기·그림 생성은 사용자가 켜야 한다.</summary>
    public static AiAssistSettings ExternalDefault() => new() { ToolsOff = [nameof(AiToolGroup.OpenFile), nameof(AiToolGroup.Generate)] };

    public bool ToolOn(AiToolGroup g) => !ToolsOff.Contains(g.ToString());

    public bool GuideOn(AiGuide g) => !GuidesOff.Contains(g.ToString());

    public void SetTool(AiToolGroup g, bool on) => Set(ToolsOff, g.ToString(), on);

    public void SetGuide(AiGuide g, bool on) => Set(GuidesOff, g.ToString(), on);

    private static void Set(List<string> off, string name, bool on)
    {
        off.Remove(name);
        if (!on) off.Add(name);
    }

    public AiAssistSettings Clone() => new() { ToolsOff = [.. ToolsOff], GuidesOff = [.. GuidesOff] };
}

/// <summary>MCP 서버가 쓰는 도구 허락(설정을 바꿀 때마다 새로 만든 고정 값 — 요청을 받는 작업 스레드에서 읽음).</summary>
public sealed record McpToolAccess(IReadOnlySet<AiToolGroup> Off)
{
    public static readonly McpToolAccess AllOn = new(new HashSet<AiToolGroup>());

    public static McpToolAccess From(AiAssistSettings s) => new(AiToolGroups.All.Where(g => !s.ToolOn(g)).ToHashSet());

    /// <summary><paramref name="scope"/>에서 <paramref name="tool"/>을 쓸 수 있는지(켜고 끄지 않는 묶음은 범위 규칙대로: 외부 연결에 묻기는 없음).</summary>
    public bool Allows(string tool, McpScope scope)
    {
        if (AiToolGroups.Of(tool) is not { } g) return true;
        if (!AiToolGroups.AppliesTo(g, scope)) return scope != McpScope.External;
        return !Off.Contains(g);
    }
}
