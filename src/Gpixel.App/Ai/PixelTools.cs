using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using ImageAnchor = Gpixel.Core.Model.Anchor;

namespace Gpixel.App.Ai;

/// <summary>
/// AI(Claude·Codex)가 MCP로 부르는 편집 도구 모음. 도구 설명·입력 스키마는 모델이 읽으므로 영어, 결과 요약·오류 문구는 한국어다.
/// <para>
/// <see cref="Call"/>은 UI 스레드에서 불린다(호출자가 넘겨 줌). 대화상자는 절대 띄우지 않는다.
/// 바꾸는 도구는 떠 있는 선택을 먼저 내려놓고, 숨겨진·잠긴 레이어와 그룹 레이어는 거부하고, 캔버스 밖은 잘라 내며,
/// 실행취소 기록을 정확히 하나("AI: 도구이름") 남기고, 고친 프레임·레이어를 현재로 만든다.
/// 픽셀은 셀 스트로크로, 구조(프레임·레이어·그룹·부위·태그·캔버스)는 프로젝트 메서드를 바깥 <see cref="SpriteProject.Edit(string, ProjectChange, Action)"/>로 감싸 한 단계로 기록한다.
/// <c>frame</c>은 항상 프레임 번호다. <c>layer</c>가 부위면 좌표는 부위 안 좌표(부위 왼쪽 위 = 0,0)다.
/// </para>
/// </summary>
public sealed class PixelTools
{
    /// <summary>JSON 직렬화 옵션(한글을 \u 이스케이프하지 않음).</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const string NoProject = "열린 프로젝트가 없습니다 — new_project로 만드세요";
    private const string ColorFormat = "\"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"";
    /// <summary>get_pixels 영역 한 변 최대.</summary>
    private const int MaxPixelRegion = 128;
    /// <summary>get_image 결과 긴 변 최대(넘으면 축소).</summary>
    private const int MaxImageSide = 2048;
    /// <summary>좌표 인자 허용 범위(넘침 방지).</summary>
    private const int CoordLimit = PixelImage.MaxSize * 2;
    private const int MaxDurationMs = 60000;
    /// <summary>get_pixels 범례 글자('.' = 투명은 따로).</summary>
    private const string LegendCodes = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private static readonly Color32 CheckerLight = new(0xFF, 0xFF, 0xFF);
    private static readonly Color32 CheckerDark = new(0xE4, 0xE4, 0xE4);

    private readonly IAiHost _host;

    public PixelTools(IAiHost host) => _host = host;

    private EditorContext Ctx => _host.Context;

    // ───────────── 도구 목록 ─────────────

    /// <summary>tools/list용 도구 설명: { name, description, inputSchema }. 부를 때마다 새 배열을 만든다(노드는 부모가 하나뿐).</summary>
    public JsonArray ListTools() => new(
        Tool("get_document",
            "Describe the open document as JSON: file path, canvas (frame) width/height, frames [{index,durationMs,name}], " +
            "layers [{index,name,kind (normal/group/part),parent (group layer index, parts only),visible,locked,opacity," +
            "partWidth,partHeight,partX,partY,skinOverride (parts only; skinOverride = skin this part wears instead of its group's, only when set)," +
            "collapsed,skins,activeSkin (groups only)}] (bottom to top; a group's parts are the layers " +
            "directly below it), tags, current {frame,layer}, primary/secondary color, palette, dirty flag, selection bounds, current folder. " +
            "Call this first. Works without an open project.",
            new JsonObject()),
        Tool("get_image",
            "Render a frame as a PNG so you can look at it. Omit `layer` for the composite of visible layers; pass a normal or " +
            "part layer index to see that layer alone (a part renders at its own size), or a group index for that group's composite. " +
            "Pixels are enlarged with nearest neighbor; transparent areas are drawn over a light gray checkerboard that is NOT part of the image.",
            new JsonObject
            {
                ["frame"] = FrameProp(),
                ["layer"] = Int("Layer index (0 = bottom) to render alone. Omit for the composite of visible layers."),
                ["scale"] = Int("Integer zoom factor. Default: the largest factor <= 16 that keeps the long side <= 512 px.", 1, 16),
            }),
        Tool("get_pixels",
            "Read exact pixel colors of a rectangle (default: whole canvas or whole part; at most 128x128) as text: legend lines `A = #RRGGBB` " +
            "('.' = transparent), then one string per row (one character per pixel). The format matches draw_pixels input. " +
            "Omit `layer` to read the visible composite. With a part layer, coordinates are part-local (0,0 = the part's top-left).",
            new JsonObject
            {
                ["x"] = Int("Left edge (default 0)."),
                ["y"] = Int("Top edge (default 0)."),
                ["width"] = Int("Region width (default: to the right edge).", 1),
                ["height"] = Int("Region height (default: to the bottom edge).", 1),
                ["frame"] = FrameProp(),
                ["layer"] = Int("Layer index (0 = bottom) to read alone (part: part-local coordinates; group: its composite). Omit for the visible composite."),
            }),
        Tool("draw_pixels",
            "Main drawing tool. Paints a block of pixels whose top-left corner is (x, y). Each string in `rows` is one " +
            "row of pixels; each character is looked up in `legend` (single character -> color). A space ' ' or a character that " +
            "is not in the legend leaves that pixel unchanged; map a character to \"transparent\" to erase " +
            "(e.g. \".\": \"transparent\"). Pixels outside the canvas (or outside the part) are ignored. " +
            "Example: x=4, y=2, rows [\" KK \", \"KRRK\", \" KK \"], legend {\"K\": \"#000000\", \"R\": \"#FF0000\"}.",
            new JsonObject
            {
                ["x"] = Int("Canvas x of the first character of each row."),
                ["y"] = Int("Canvas y of the first row."),
                ["rows"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "One string per pixel row, top to bottom.",
                },
                ["legend"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Map of single character -> color (" + ColorFormat + ").",
                },
                ["frame"] = FrameProp(),
                ["layer"] = LayerProp(),
            },
            "x", "y", "rows", "legend"),
        Tool("set_pixels",
            "Set individual pixels to colors. Prefer draw_pixels for anything larger than a few pixels.",
            new JsonObject
            {
                ["pixels"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["x"] = Int("Canvas x."),
                            ["y"] = Int("Canvas y."),
                            ["color"] = ColorProp("Pixel color."),
                        },
                        ["required"] = Strings("x", "y", "color"),
                    },
                    ["description"] = "Pixels to set.",
                },
                ["frame"] = FrameProp(),
                ["layer"] = LayerProp(),
            },
            "pixels"),
        Tool("draw_shape",
            "Draw a 1-pixel line from (x1,y1) to (x2,y2), or a rectangle/ellipse (outline or filled) inside the box whose " +
            "opposite corners are (x1,y1) and (x2,y2), both inclusive.",
            new JsonObject
            {
                ["shape"] = Enum("Shape to draw.", "line", "rect", "ellipse"),
                ["x1"] = Int("First point / corner x."),
                ["y1"] = Int("First point / corner y."),
                ["x2"] = Int("Second point / corner x."),
                ["y2"] = Int("Second point / corner y."),
                ["color"] = ColorProp("Shape color."),
                ["filled"] = Bool("Fill the rectangle/ellipse instead of drawing its outline (default false)."),
                ["frame"] = FrameProp(),
                ["layer"] = LayerProp(),
            },
            "shape", "x1", "y1", "x2", "y2", "color"),
        Tool("fill",
            "Flood fill (paint bucket) starting at (x, y). contiguous=true (default) fills the 4-connected area of the same color; " +
            "false replaces that color everywhere in the cel.",
            new JsonObject
            {
                ["x"] = Int("Start x (inside the canvas)."),
                ["y"] = Int("Start y (inside the canvas)."),
                ["color"] = ColorProp("Fill color."),
                ["contiguous"] = Bool("Fill only the connected area (default true)."),
                ["frame"] = FrameProp(),
                ["layer"] = LayerProp(),
            },
            "x", "y", "color"),
        Tool("clear",
            "Make a rectangle of the cel transparent (default: the whole cel).",
            new JsonObject
            {
                ["x"] = Int("Left edge (default 0)."),
                ["y"] = Int("Top edge (default 0)."),
                ["width"] = Int("Width (default: to the right edge).", 1),
                ["height"] = Int("Height (default: to the bottom edge).", 1),
                ["frame"] = FrameProp(),
                ["layer"] = LayerProp(),
            }),
        Tool("replace_color",
            "Replace every pixel of one exact color with another color in a cel.",
            new JsonObject
            {
                ["from"] = ColorProp("Color to replace."),
                ["to"] = ColorProp("New color."),
                ["frame"] = FrameProp(),
                ["layer"] = LayerProp(),
            },
            "from", "to"),
        Tool("add_frame",
            "Insert a new frame right after frame `after` (default: current frame; -1 = at the start). " +
            "duplicate=true copies the pixels of frame `after`. The new frame becomes current. Returns its index.",
            new JsonObject
            {
                ["after"] = Int("Insert after this frame index (default: current; -1 = at the start).", -1),
                ["duplicate"] = Bool("Copy the `after` frame instead of adding an empty one (default false)."),
                ["duration_ms"] = Int("Frame duration in milliseconds (default: same as the previous frame).", 1, MaxDurationMs),
            }),
        Tool("delete_frame",
            "Delete a frame (all layers and parts of that frame). The last remaining frame cannot be deleted.",
            new JsonObject { ["frame"] = Int("Frame index.", 0) },
            "frame"),
        Tool("set_frame_duration",
            "Set how long a frame is shown, in milliseconds.",
            new JsonObject
            {
                ["frame"] = Int("Frame index.", 0),
                ["duration_ms"] = Int("Duration in milliseconds.", 1, MaxDurationMs),
            },
            "frame", "duration_ms"),
        Tool("add_layer",
            "Add an empty normal layer on top of all layers; it becomes the current layer. Returns its index.",
            new JsonObject { ["name"] = Str("Layer name (default: automatic).") }),
        Tool("set_layer",
            "Change a layer's name, visibility and/or opacity.",
            new JsonObject
            {
                ["layer"] = Int("Layer index (0 = bottom).", 0),
                ["name"] = Str("New name."),
                ["visible"] = Bool("Show or hide the layer."),
                ["opacity"] = Int("Opacity 0-255.", 0, 255),
            },
            "layer"),
        Tool("set_current",
            "Select the current frame and/or layer shown to the user in the editor.",
            new JsonObject
            {
                ["frame"] = FrameProp(),
                ["layer"] = LayerProp(),
            }),
        Tool("add_tag",
            "Add a named animation tag (clip) spanning frames from..to (inclusive).",
            new JsonObject
            {
                ["name"] = Str("Tag name, unique (e.g. idle, walk_left)."),
                ["from"] = Int("First frame index.", 0),
                ["to"] = Int("Last frame index (inclusive).", 0),
                ["direction"] = Enum("Playback direction (default forward).", "forward", "reverse", "pingpong"),
                ["loop"] = Bool("Loop playback (default true)."),
            },
            "name", "from", "to"),
        Tool("add_group",
            "Add an empty group layer on top (a container for character parts such as head, body, arms, legs). " +
            "It becomes the current layer. Returns its index; then use add_part to add parts to it.",
            new JsonObject { ["name"] = Str("Group name (default: automatic).") }),
        Tool("add_part",
            "Add a part layer to a group. A part has its own size and its own drawing in every frame, and one position (x, y = its " +
            "top-left inside the frame canvas) shared by all frames. It becomes the top part of the group and the current layer. " +
            "Drawing tools on a part use part-local coordinates (0,0 = the part's top-left). Returns its index and the group's " +
            "new index: parts are inserted directly below their group in the layer list, so indices of the group and of layers above it grow by 1.",
            new JsonObject
            {
                ["group"] = Int("Index of the group layer (default: the selected group, or the group of the selected part).", 0),
                ["name"] = Str("Part name, e.g. Head (made unique automatically)."),
                ["width"] = Int("Part width in pixels.", 1, PixelImage.MaxSize),
                ["height"] = Int("Part height in pixels.", 1, PixelImage.MaxSize),
                ["x"] = Int("Left edge inside the frame canvas (default: centered)."),
                ["y"] = Int("Top edge inside the frame canvas (default: centered)."),
            },
            "name", "width", "height"),
        Tool("set_part",
            "Change a part layer: its position (x, y inside the frame canvas, shared by all frames), its size (width/height; " +
            "every frame's drawing stays attached to `anchor` and the position is adjusted so pixels stay where they are on the canvas), " +
            "and/or its Unity pivot (0..1, origin bottom-left).",
            new JsonObject
            {
                ["layer"] = Int("Part layer index.", 0),
                ["x"] = Int("New left edge inside the frame canvas."),
                ["y"] = Int("New top edge inside the frame canvas."),
                ["width"] = Int("New part width.", 1, PixelImage.MaxSize),
                ["height"] = Int("New part height.", 1, PixelImage.MaxSize),
                ["anchor"] = Enum("Where the existing drawing is kept when resizing (default center).",
                    "top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right"),
                ["pivot_x"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1, ["description"] = "Pivot x (0 = left, 1 = right)." },
                ["pivot_y"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1, ["description"] = "Pivot y (0 = bottom, 1 = top)." },
            },
            "layer"),
        Tool("list_skins",
            "List every group with its skins (top to bottom like the timeline). A group is a skin slot: its skins are alternative " +
            "drawings of the same parts (e.g. outfits); only the active skin is visible, and groups stack in layer order. The first " +
            "skin is the base: a part's empty cel in another skin shows the base skin's drawing. A part may wear a different skin than " +
            "its group (override, listed under `overrides`). A part may also own part-only skins (listed under `partSkins`, e.g. a hat " +
            "only for Head): no other part or the group list has them, and they are worn only as that part's override (set_skin with part). " +
            "All reading and drawing tools work on the part's shown skin (its override, " +
            "else the group's active skin; drawing on a cel that shows the base drawing starts from a copy of it).",
            new JsonObject()),
        Tool("add_skin",
            "Add a skin to a group; it becomes the group's active skin, so following drawing on its parts goes to the new skin. " +
            "copy_active=true copies the active skin's own drawings; otherwise it starts empty (showing the base skin's drawing until you draw). " +
            "With `part`: add a part-only skin that only that part has (e.g. a hat for Head) and make the part wear it; copy_shown=true copies the part's shown drawing.",
            new JsonObject
            {
                ["group"] = Int("Index of the group layer (default: the selected group, or the group of the selected part; with `part`, narrows the part search).", 0),
                ["name"] = Str("Skin name (default: automatic; made unique within the group, including part-only skins)."),
                ["copy_active"] = Bool("Copy the active skin's drawings (default false; group skins only)."),
                ["part"] = Str("Part layer name: create a part-only skin for this part instead of a group skin."),
                ["copy_shown"] = Bool("With `part`: copy the part's currently shown skin drawings into the new part-only skin (default false)."),
            }),
        Tool("set_skin",
            "Choose which skin of a group is visible and edited (switch outfit). With `part`, only that part wears `skin` (override — a group skin " +
            "or one of that part's part-only skins); follow_group=true (or skin \"그룹\"/empty) makes the part follow its group's skin again. Not recorded in undo history.",
            new JsonObject
            {
                ["group"] = Int("Index of the group layer (default: the selected group, or the group of the selected part; with `part`, the part's group).", 0),
                ["skin"] = Str("Skin name (see list_skins). Required unless part + follow_group. Part-only skins need `part`."),
                ["part"] = Str("Part layer name: set the skin for this part only instead of the whole group."),
                ["follow_group"] = Bool("With `part`: clear the part's own skin so it follows the group again (default false)."),
            }),
        Tool("list_outfits",
            "List outfit presets: named snapshots of every group's skin plus per-part skin overrides. Each entry has its slots " +
            "(group → skin), part overrides, whether it matches what is shown now, and entries that no longer exist (deleted group/part/skin, skipped on apply).",
            new JsonObject()),
        Tool("save_outfit",
            "Save the current outfit (each group's active skin + part overrides) as a new outfit preset (undoable). " +
            "Outfit presets can also be exported as extra frame sheets ({sheet}@{outfit}).",
            new JsonObject
            {
                ["name"] = Str("Outfit name (default \"착장\"; made unique)."),
            }),
        Tool("apply_outfit",
            "Wear an outfit preset: set each saved group's skin and its parts' overrides (see list_outfits). Groups not in the outfit stay as they are. " +
            "Not recorded in undo history (like set_skin).",
            new JsonObject
            {
                ["name"] = Str("Outfit preset name."),
            },
            "name"),
        Tool("resize_canvas",
            "Change the frame canvas size. Normal layers keep their pixels attached to `anchor` (parts that fall outside are cropped); " +
            "part layers keep their size and are shifted with the anchor. To resize a part use set_part.",
            new JsonObject
            {
                ["width"] = Int("New width in pixels.", 1, PixelImage.MaxSize),
                ["height"] = Int("New height in pixels.", 1, PixelImage.MaxSize),
                ["anchor"] = Enum("Where the existing image is kept (default center).",
                    "top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right"),
            },
            "width", "height"),
        Tool("set_palette",
            "Replace the colors of the active palette (the last clicked/edited one) in the editor's color panel.",
            new JsonObject
            {
                ["colors"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = ColorProp("Palette color."),
                    ["description"] = "Palette colors in order (1-256).",
                },
            },
            "colors"),
        Tool("set_colors",
            "Set the editor's primary and/or secondary drawing color.",
            new JsonObject
            {
                ["primary"] = ColorProp("Primary color."),
                ["secondary"] = ColorProp("Secondary color."),
            }),
        Tool("new_project",
            "Create a new empty project and open it (fails if the open project has unsaved changes). With file_name the project " +
            "is saved as a .gpixel file in the current folder (fails if that file exists).",
            new JsonObject
            {
                ["width"] = Int("Canvas width in pixels.", 1, PixelImage.MaxSize),
                ["height"] = Int("Canvas height in pixels.", 1, PixelImage.MaxSize),
                ["file_name"] = Str("File name to save in the current folder (optional)."),
                ["background"] = ColorProp("Background fill color (default transparent)."),
            },
            "width", "height"),
        Tool("save_project",
            "Save the open project to its file (and export the sheet if the project is set up to). Fails if it has never been saved.",
            new JsonObject()),
        Tool("open_file",
            "Open a .gpixel project or a .png image (fails if the open project has unsaved changes). Relative paths are resolved " +
            "against the current folder.",
            new JsonObject { ["path"] = Str("File path.") },
            "path"));

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Length > 0) schema["required"] = Strings(required);
        return new JsonObject { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };
    }

    private static JsonArray Strings(params string[] values) => new(values.Select(v => (JsonNode?)v).ToArray());

    private static JsonObject Int(string description, int? minimum = null, int? maximum = null)
    {
        var o = new JsonObject { ["type"] = "integer", ["description"] = description };
        if (minimum is { } min) o["minimum"] = min;
        if (maximum is { } max) o["maximum"] = max;
        return o;
    }

    private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

    private static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };

    private static JsonObject Enum(string description, params string[] values)
        => new() { ["type"] = "string", ["enum"] = Strings(values), ["description"] = description };

    private static JsonObject ColorProp(string description) => Str(description + " " + ColorFormat + ".");

    private static JsonObject FrameProp() => Int("Frame index. Default: current.", 0);

    private static JsonObject LayerProp() => Int("Layer index (0 = bottom; a part uses part-local coordinates; groups cannot be drawn on). Default: current.", 0);

    // ───────────── 실행 ─────────────

    /// <summary>
    /// 도구 하나를 실행한다(UI 스레드). 결과는 MCP CallToolResult:
    /// { content:[{type:"text",text} | {type:"image",data,mimeType}], isError }. 예외는 밖으로 내보내지 않는다.
    /// </summary>
    public JsonObject Call(string name, JsonObject? arguments)
    {
        try
        {
            var a = arguments;
            return name switch
            {
                "get_document" => GetDocument(),
                "get_image" => GetImage(a),
                "get_pixels" => GetPixels(a),
                "draw_pixels" => DrawPixels(name, a),
                "set_pixels" => SetPixels(name, a),
                "draw_shape" => DrawShape(name, a),
                "fill" => Fill(name, a),
                "clear" => Clear(name, a),
                "replace_color" => ReplaceColor(name, a),
                "add_frame" => AddFrame(name, a),
                "delete_frame" => DeleteFrame(name, a),
                "set_frame_duration" => SetFrameDuration(name, a),
                "add_layer" => AddLayer(name, a),
                "set_layer" => SetLayer(name, a),
                "set_current" => SetCurrent(name, a),
                "add_tag" => AddTag(name, a),
                "add_group" => AddGroup(name, a),
                "add_part" => AddPart(name, a),
                "set_part" => SetPart(name, a),
                "list_skins" => ListSkins(),
                "list_outfits" => ListOutfits(),
                "save_outfit" => SaveOutfit(name, a),
                "apply_outfit" => ApplyOutfit(name, a),
                "add_skin" => AddSkin(name, a),
                "set_skin" => SetSkin(name, a),
                "resize_canvas" => ResizeCanvas(name, a),
                "set_palette" => SetPalette(name, a),
                "set_colors" => SetColors(name, a),
                "new_project" => NewProject(name, a),
                "save_project" => SaveProject(name),
                "open_file" => OpenFile(name, a),
                _ => TextResult($"알 수 없는 도구입니다: {name}", isError: true),
            };
        }
        catch (ToolError e)
        {
            return TextResult($"{name}: {e.Message}", isError: true);
        }
        catch (Exception e)
        {
            return TextResult($"{name} 실패: {CleanMessage(e)}", isError: true);
        }
    }

    /// <summary>인자·상태 오류(사용자에게 그대로 보여 줄 한국어 문구).</summary>
    private sealed class ToolError(string message) : Exception(message);

    /// <summary>ArgumentException의 " (Parameter 'x')" 꼬리를 뗀 메시지.</summary>
    private static string CleanMessage(Exception e)
    {
        var message = e.Message;
        if (e is ArgumentException)
        {
            int i = message.LastIndexOf(" (Parameter '", StringComparison.Ordinal);
            if (i > 0) message = message[..i];
        }
        return message;
    }

    // ───────────── 읽기 도구 ─────────────

    private JsonObject GetDocument()
    {
        var ctx = Ctx;
        var doc = new JsonObject();
        if (ctx.Project is not { } p)
        {
            doc["open"] = false;
            doc["message"] = "열린 프로젝트가 없습니다 — new_project로 만들거나 open_file로 여세요";
        }
        else
        {
            doc["open"] = true;
            doc["path"] = p.Path;
            doc["name"] = p.DisplayName;
            doc["width"] = p.Width;
            doc["height"] = p.Height;
            var frames = new JsonArray();
            for (int i = 0; i < p.Frames.Count; i++)
            {
                var f = p.Frames[i];
                frames.Add(new JsonObject { ["index"] = i, ["durationMs"] = f.DurationMs, ["name"] = f.Name });
            }
            doc["frames"] = frames;
            var layers = new JsonArray();
            for (int i = 0; i < p.Layers.Count; i++)
            {
                var l = p.Layers[i];
                var o = new JsonObject
                {
                    ["index"] = i, ["name"] = l.Name, ["kind"] = KindName(l.Kind),
                    ["visible"] = l.Visible, ["locked"] = l.Locked, ["opacity"] = (int)l.Opacity,
                };
                if (l.IsPart)
                {
                    o["parent"] = l.Parent is { } g ? p.IndexOf(g) : null;
                    o["partWidth"] = l.PartSize.Width;
                    o["partHeight"] = l.PartSize.Height;
                    o["partX"] = l.PartOffset.X;
                    o["partY"] = l.PartOffset.Y;
                    if (l.SkinOverride is { } own) o["skinOverride"] = own.Name;
                    if (l.OwnSkins is { Count: > 0 } partSkins) o["partSkins"] = Strings(partSkins.Select(s => s.Name).ToArray());
                }
                else if (l.IsGroup)
                {
                    o["collapsed"] = l.Collapsed;
                    o["skins"] = Strings(l.Skins.Select(s => s.Name).ToArray());
                    o["activeSkin"] = l.ActiveSkin?.Name;
                }
                layers.Add(o);
            }
            doc["layers"] = layers;
            var tags = new JsonArray();
            foreach (var t in p.Tags)
            {
                tags.Add(new JsonObject
                {
                    ["name"] = t.Name, ["from"] = t.From, ["to"] = t.To, ["direction"] = DirectionName(t.Direction), ["loop"] = t.Loop,
                });
            }
            doc["tags"] = tags;
            doc["current"] = new JsonObject { ["frame"] = ctx.CurrentFrame, ["layer"] = ctx.CurrentLayer };
            doc["dirty"] = p.IsDirty;
            var sel = ctx.Selection;
            doc["selection"] = sel.HasSelection
                ? new JsonObject
                {
                    ["x"] = sel.Bounds.X, ["y"] = sel.Bounds.Y, ["width"] = sel.Bounds.Width, ["height"] = sel.Bounds.Height,
                    // 원·자유·스마트 선택은 사각형 안의 일부 픽셀만 선택된다.
                    ["shape"] = sel.IsRectangle ? "rectangle" : "mask",
                    ["pixels"] = sel.Region.Count,
                }
                : null;
        }
        doc["primary"] = ctx.Primary.ToHex();
        doc["secondary"] = ctx.Secondary.ToHex();
        doc["palette"] = Strings(_host.Palette.Select(c => c.ToHex()).ToArray());
        doc["currentFolder"] = _host.CurrentFolder;
        return TextResult(doc.ToJsonString(JsonOptions));
    }

    private JsonObject GetImage(JsonObject? a)
    {
        var p = RequireProject();
        int frame = Frame(a);
        int? layer = OptLayer(a);
        int? requested = OptInt(a, "scale", 1, 16);
        var (w, h) = SourceSize(p, layer);
        var src = Source(p, frame, layer);

        int longSide = Math.Max(w, h), scale = 1, step = 1;
        if (longSide > MaxImageSide) step = (longSide + 1023) / 1024; // 너무 크면 정수 배 축소(가장 가까운 픽셀)
        else scale = Math.Max(1, Math.Min(requested ?? Math.Clamp(512 / longSide, 1, 16), MaxImageSide / longSide));
        int outW = step > 1 ? (w + step - 1) / step : w * scale;
        int outH = step > 1 ? (h + step - 1) / step : h * scale;

        var output = new PixelImage(outW, outH);
        var dst = output.Pixels;
        for (int oy = 0; oy < outH; oy++)
        {
            int sy = step > 1 ? Math.Min(oy * step, h - 1) : oy / scale;
            for (int ox = 0; ox < outW; ox++)
            {
                int sx = step > 1 ? Math.Min(ox * step, w - 1) : ox / scale;
                var c = src.Pixels[sy * w + sx];
                if (c.A == 255) { dst[oy * outW + ox] = c; continue; }
                var bg = (((ox >> 3) + (oy >> 3)) & 1) == 0 ? CheckerLight : CheckerDark;
                dst[oy * outW + ox] = c.A == 0 ? bg : Color32.BlendOver(bg, c);
            }
        }

        using var ms = new MemoryStream();
        PngIO.Save(output, ms);
        var zoom = step > 1 ? $"×1/{step} (축소)" : $"×{scale}";
        var text = $"{w}x{h} px, {zoom} (frame {frame}, {(layer is { } l ? $"layer {l}" : "합성")})";
        return new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(ms.GetBuffer(), 0, (int)ms.Length), ["mimeType"] = "image/png" },
                new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = false,
        };
    }

    private JsonObject GetPixels(JsonObject? a)
    {
        var p = RequireProject();
        int frame = Frame(a);
        int? layer = OptLayer(a);
        var (w, h) = SourceSize(p, layer);
        var r = Region(a, w, h);
        if (r.Width > MaxPixelRegion || r.Height > MaxPixelRegion)
            throw new ToolError($"영역 {r.Width}x{r.Height}이(가) 너무 큽니다 (최대 {MaxPixelRegion}x{MaxPixelRegion}) — x, y, width, height로 나눠 요청하세요");
        var src = Source(p, frame, layer);

        var codes = new Dictionary<Color32, char>();
        var legend = new List<(char Code, Color32 Color)>();
        bool anyTransparent = false;
        var rows = new string[r.Height];
        var chars = new char[r.Width];
        for (int y = 0; y < r.Height; y++)
        {
            for (int x = 0; x < r.Width; x++)
            {
                var c = src.Get(r.X + x, r.Y + y);
                if (c.A == 0)
                {
                    chars[x] = '.';
                    anyTransparent = true;
                    continue;
                }
                if (!codes.TryGetValue(c, out var code))
                {
                    if (codes.Count >= LegendCodes.Length)
                        throw new ToolError($"영역에 색이 {LegendCodes.Length}가지보다 많습니다 — 더 작은 영역(x, y, width, height)으로 나눠 요청하세요");
                    code = LegendCodes[codes.Count];
                    codes[c] = code;
                    legend.Add((code, c));
                }
                chars[x] = code;
            }
            rows[y] = new string(chars);
        }

        var sb = new StringBuilder();
        sb.Append($"x={r.X} y={r.Y} width={r.Width} height={r.Height} (frame {frame}, {(layer is { } l ? $"layer {l}" : "합성")})\n");
        sb.Append("legend:\n");
        foreach (var (code, color) in legend) sb.Append(code).Append(" = ").Append(color.ToHex()).Append('\n');
        if (anyTransparent) sb.Append(". = transparent\n");
        sb.Append("rows:\n");
        foreach (var row in rows) sb.Append(row).Append('\n');
        return TextResult(sb.ToString().TrimEnd('\n'));
    }

    // ───────────── 픽셀 도구 ─────────────

    private JsonObject DrawPixels(string tool, JsonObject? a)
    {
        var t = EditTarget(a);
        int x0 = ReqInt(a, "x", -CoordLimit, CoordLimit), y0 = ReqInt(a, "y", -CoordLimit, CoordLimit);
        var rowNodes = ReqArray(a, "rows");
        var rows = new string[rowNodes.Count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = AsString(rowNodes[i]) ?? throw new ToolError($"rows[{i}]: 문자열이 필요합니다");
        if (Arg(a, "legend") is not JsonObject legendNode) throw new ToolError("legend: {\"글자\": \"색\"} 객체가 필요합니다");
        var legend = new Dictionary<char, Color32>();
        foreach (var (key, value) in legendNode)
        {
            if (key.Length != 1) throw new ToolError($"legend 키는 한 글자여야 합니다 (받은 키: \"{key}\")");
            if (key[0] == ' ') continue; // 공백은 항상 "그대로 둠"
            var text = AsString(value) ?? throw new ToolError($"legend[\"{key}\"]: 색 문자열이 필요합니다");
            legend[key[0]] = ParseColor(text, $"legend[\"{key}\"]");
        }
        if (legend.Count == 0) throw new ToolError("legend가 비어 있습니다");

        int outside = 0;
        int changed = Stroke(tool, t, img =>
        {
            int n = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                for (int j = 0; j < row.Length; j++)
                {
                    if (!legend.TryGetValue(row[j], out var color)) continue;
                    int px = x0 + j, py = y0 + i;
                    if (!img.Contains(px, py)) { outside++; continue; }
                    if (img.Set(px, py, color)) n++;
                }
            }
            return n;
        });
        return Done(tool, changed, t, outside > 0 ? $" — 캔버스 밖 {outside} px 무시" : "");
    }

    private JsonObject SetPixels(string tool, JsonObject? a)
    {
        var t = EditTarget(a);
        var nodes = ReqArray(a, "pixels");
        if (nodes.Count > 65536) throw new ToolError($"pixels가 너무 많습니다 ({nodes.Count}개, 최대 65536) — draw_pixels를 쓰세요");
        var pixels = new List<(int X, int Y, Color32 Color)>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is not JsonObject o) throw new ToolError($"pixels[{i}]: {{x, y, color}} 객체가 필요합니다");
            try
            {
                pixels.Add((ReqInt(o, "x", -CoordLimit, CoordLimit), ReqInt(o, "y", -CoordLimit, CoordLimit), ReqColor(o, "color")));
            }
            catch (ToolError e)
            {
                throw new ToolError($"pixels[{i}].{e.Message}");
            }
        }

        int outside = 0;
        int changed = Stroke(tool, t, img =>
        {
            int n = 0;
            foreach (var (x, y, color) in pixels)
            {
                if (!img.Contains(x, y)) { outside++; continue; }
                if (img.Set(x, y, color)) n++;
            }
            return n;
        });
        return Done(tool, changed, t, outside > 0 ? $" — 캔버스 밖 {outside} px 무시" : "");
    }

    private JsonObject DrawShape(string tool, JsonObject? a)
    {
        var t = EditTarget(a);
        var shape = ReqEnum(a, "shape", "line", "rect", "ellipse");
        int x1 = Coord(a, "x1", t.Width), y1 = Coord(a, "y1", t.Height);
        int x2 = Coord(a, "x2", t.Width), y2 = Coord(a, "y2", t.Height);
        var color = ReqColor(a, "color");
        bool filled = OptBool(a, "filled") ?? false;

        Point p1 = new(x1, y1), p2 = new(x2, y2);
        var box = Raster.RectFromPoints(p1, p2);
        var points = shape switch
        {
            "line" => Raster.Line(p1, p2),
            "rect" => filled ? Raster.RectFill(Rectangle.Intersect(box, new Rectangle(0, 0, t.Width, t.Height))) : Raster.RectOutline(box),
            _ => filled ? Raster.EllipseFill(box) : Raster.EllipseOutline(box),
        };
        int changed = Stroke(tool, t, img => Raster.Plot(img, points, color));
        return Done(tool, changed, t);
    }

    /// <summary>도형 좌표: 캔버스 주변 여유(한 변의 2배, 최소 64)까지만 허용(거대한 점 목록 방지).</summary>
    private static int Coord(JsonObject? a, string key, int extent)
    {
        int margin = Math.Max(extent * 2, 64);
        return ReqInt(a, key, -margin, extent - 1 + margin);
    }

    private JsonObject Fill(string tool, JsonObject? a)
    {
        var t = EditTarget(a);
        int x = ReqInt(a, "x", 0, t.Width - 1), y = ReqInt(a, "y", 0, t.Height - 1);
        var color = ReqColor(a, "color");
        bool contiguous = OptBool(a, "contiguous") ?? true;
        int changed = Stroke(tool, t, img => Raster.FloodFill(img, x, y, color, contiguous));
        return Done(tool, changed, t);
    }

    private JsonObject Clear(string tool, JsonObject? a)
    {
        var t = EditTarget(a);
        var r = Region(a, t.Width, t.Height);
        int changed = Stroke(tool, t, img =>
        {
            int n = 0;
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                    if (img.Set(x, y, Color32.Transparent)) n++;
            return n;
        });
        return Done(tool, changed, t);
    }

    private JsonObject ReplaceColor(string tool, JsonObject? a)
    {
        var t = EditTarget(a);
        var from = ReqColor(a, "from");
        var to = ReqColor(a, "to");
        int changed = Stroke(tool, t, img => Raster.ReplaceColor(img, from, to));
        return Done(tool, changed, t);
    }

    // ───────────── 구조 도구 ─────────────

    private JsonObject AddFrame(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        var ctx = Ctx;
        bool duplicate = OptBool(a, "duplicate") ?? false;
        int after = OptInt(a, "after", duplicate ? 0 : -1, p.Frames.Count - 1) ?? ctx.CurrentFrame;
        int? duration = OptInt(a, "duration_ms", 1, MaxDurationMs);
        int index = after + 1;
        p.Edit(Label(tool), ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () =>
        {
            if (duplicate) p.DuplicateFrame(after);
            else p.AddFrame(index);
            if (duration is { } d) p.SetFrameDuration(index, d);
        });
        ctx.SetCurrent(ctx.CurrentLayer, index);
        var copied = duplicate ? $" (frame {after} 복제)" : "";
        return TextResult($"{tool}: frame {index} 추가{copied}, {p.Frames[index].DurationMs}ms — 지금 frame {index}, 전체 {p.Frames.Count}프레임");
    }

    private JsonObject DeleteFrame(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        var ctx = Ctx;
        int frame = ReqIndex(a, "frame", p.Frames.Count, "프레임");
        if (p.Frames.Count <= 1) throw new ToolError("마지막 프레임은 삭제할 수 없습니다");
        p.Edit(Label(tool), ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () => p.DeleteFrame(frame));
        int current = Math.Min(frame, p.Frames.Count - 1);
        ctx.SetCurrent(ctx.CurrentLayer, current);
        return TextResult($"{tool}: frame {frame} 삭제 — 남은 {p.Frames.Count}개, 지금 frame {current}");
    }

    private JsonObject SetFrameDuration(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int frame = ReqIndex(a, "frame", p.Frames.Count, "프레임");
        int duration = ReqInt(a, "duration_ms", 1, MaxDurationMs);
        if (p.Frames[frame].DurationMs != duration)
            p.Edit(Label(tool), ProjectChange.Frames, () => p.SetFrameDuration(frame, duration));
        var ctx = Ctx;
        ctx.SetCurrent(ctx.CurrentLayer, frame);
        return TextResult($"{tool}: frame {frame} = {duration}ms");
    }

    private JsonObject AddLayer(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        var name = OptString(a, "name");
        var layer = p.Edit<Layer>(Label(tool), ProjectChange.Layers, () => p.AddLayer(null, name));
        int index = p.IndexOf(layer);
        Ctx.SetCurrentLayer(index);
        return TextResult($"{tool}: layer {index} \"{layer.Name}\" 추가 — 지금 layer {index}");
    }

    private JsonObject SetLayer(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int index = ReqIndex(a, "layer", p.Layers.Count, "레이어");
        var name = OptString(a, "name");
        bool? visible = OptBool(a, "visible");
        int? opacity = OptInt(a, "opacity", 0, 255);
        if (string.IsNullOrWhiteSpace(name)) name = null;
        if (name is null && visible is null && opacity is null)
            throw new ToolError("바꿀 값(name, visible, opacity) 중 하나 이상이 필요합니다");
        var layer = p.Layers[index];
        if ((name is null || name.Trim() == layer.Name) && (visible is null || visible == layer.Visible)
            && (opacity is null || opacity == layer.Opacity))
        {
            Ctx.SetCurrentLayer(index);
            return TextResult($"{tool}: layer {index} \"{layer.Name}\" — 바뀐 것 없음");
        }
        p.Edit(Label(tool), ProjectChange.Layers, () => p.SetLayerProperties(index, name, visible, null, (byte?)opacity));
        Ctx.SetCurrentLayer(index);
        return TextResult($"{tool}: layer {index} \"{layer.Name}\" — {(layer.Visible ? "보임" : "숨김")}, 불투명도 {layer.Opacity}");
    }

    private JsonObject SetCurrent(string tool, JsonObject? a)
    {
        var p = RequireProject();
        var ctx = Ctx;
        int frame = Frame(a), layer = LayerIndex(a);
        ctx.SetCurrent(layer, frame);
        return TextResult($"{tool}: 지금 frame {ctx.CurrentFrame}, layer {ctx.CurrentLayer} \"{p.Layers[ctx.CurrentLayer].Name}\"");
    }

    private JsonObject AddTag(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        var name = (ReqString(a, "name")).Trim();
        if (name.Length == 0) throw new ToolError("name: 태그 이름이 비어 있습니다");
        if (p.HasTag(name)) throw new ToolError($"이미 같은 이름의 태그가 있습니다: {name}");
        int last = p.Frames.Count - 1;
        int from = ReqInt(a, "from", 0, last), to = ReqInt(a, "to", 0, last);
        if (from > to) throw new ToolError($"from({from})이 to({to})보다 클 수 없습니다");
        var direction = OptString(a, "direction")?.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "") switch
        {
            null or "forward" => TagDirection.Forward,
            "reverse" => TagDirection.Reverse,
            "pingpong" => TagDirection.PingPong,
            _ => throw new ToolError("direction: \"forward\", \"reverse\", \"pingpong\" 중 하나여야 합니다"),
        };
        bool loop = OptBool(a, "loop") ?? true;
        var tag = p.Edit<AnimationTag>(Label(tool), ProjectChange.Tags, () => p.AddTag(name, from, to, direction, loop));
        var ctx = Ctx;
        ctx.SetCurrent(ctx.CurrentLayer, from);
        ctx.SelectedTag = tag;
        return TextResult($"{tool}: 태그 \"{tag.Name}\" frame {from}~{to}, {DirectionName(direction)}{(loop ? ", 반복" : ", 1회")}");
    }

    private JsonObject AddGroup(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        var name = OptString(a, "name");
        if (string.IsNullOrWhiteSpace(name)) name = null;
        var group = p.Edit<Layer>(Label(tool), ProjectChange.Layers, () => p.AddGroup(name?.Trim()));
        int index = p.IndexOf(group);
        Ctx.SetCurrentLayer(index);
        return TextResult($"{tool}: group {index} \"{group.Name}\" 추가 — add_part(group={index})로 부위를 더하세요");
    }

    private JsonObject AddPart(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int groupIndex;
        if (a?["group"] is not null) groupIndex = ReqIndex(a, "group", p.Layers.Count, "레이어");
        else if (Ctx.CurrentGroup is { } current) groupIndex = p.IndexOf(current);
        else throw new ToolError("group: 고른 그룹이 없습니다 — group 번호를 주거나 add_group으로 먼저 그룹을 만드세요");
        var group = p.Layers[groupIndex];
        if (!group.IsGroup) throw new ToolError($"layer {groupIndex} \"{group.Name}\"은(는) 그룹이 아닙니다 — add_group으로 먼저 그룹을 만드세요");
        var name = ReqString(a, "name").Trim();
        if (name.Length == 0) throw new ToolError("name: 부위 이름이 비어 있습니다");
        int width = ReqInt(a, "width", 1, PixelImage.MaxSize), height = ReqInt(a, "height", 1, PixelImage.MaxSize);
        int? x = OptInt(a, "x", -CoordLimit, CoordLimit), y = OptInt(a, "y", -CoordLimit, CoordLimit);
        Point? offset = x is null && y is null ? null
            : new Point(x ?? (p.Width - width) / 2, y ?? (p.Height - height) / 2);
        var part = p.Edit<Layer>(Label(tool), ProjectChange.Layers | ProjectChange.Pixels, () => p.AddPart(group, name, width, height, offset));
        int index = p.IndexOf(part);
        Ctx.SetCurrentLayer(index);
        return TextResult($"{tool}: part {index} \"{part.Name}\" {width}x{height} @({part.PartOffset.X},{part.PartOffset.Y}) 추가 — 지금 layer {index} (좌표는 부위 안 기준), " +
                          $"group \"{group.Name}\"은(는) 이제 layer {p.IndexOf(group)}");
    }

    private JsonObject SetPart(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int index = ReqIndex(a, "layer", p.Layers.Count, "레이어");
        var part = p.Layers[index];
        if (!part.IsPart) throw new ToolError($"layer {index} \"{part.Name}\"은(는) 부위 레이어가 아닙니다");
        int? x = OptInt(a, "x", -CoordLimit, CoordLimit), y = OptInt(a, "y", -CoordLimit, CoordLimit);
        int? width = OptInt(a, "width", 1, PixelImage.MaxSize), height = OptInt(a, "height", 1, PixelImage.MaxSize);
        var anchor = ParseAnchor(a);
        double? pivotX = OptUnit(a, "pivot_x"), pivotY = OptUnit(a, "pivot_y");
        if (x is null && y is null && width is null && height is null && pivotX is null && pivotY is null)
            throw new ToolError("바꿀 값(x, y, width, height, pivot_x, pivot_y) 중 하나 이상이 필요합니다");

        var size = part.PartSize;
        int newW = width ?? size.Width, newH = height ?? size.Height;
        bool resize = newW != size.Width || newH != size.Height;
        p.Edit(Label(tool), ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            if (resize) p.ResizePart(index, newW, newH, anchor);
            // 위치는 크기를 바꾼 뒤(기준점으로 조정된 위치) 위에 덮어쓴다.
            if (x is not null || y is not null)
            {
                var o = part.PartOffset;
                var target = new Point(x ?? o.X, y ?? o.Y);
                if (target != o) p.SetPartOffset(index, target);
            }
            if (pivotX is not null || pivotY is not null)
            {
                var cur = part.PartPivot; // 크기를 바꿨으면 조정된 피벗 기준
                var pivot = new Pivot((float)(pivotX ?? cur.X), (float)(pivotY ?? cur.Y));
                if (pivot != cur) p.SetPartPivot(index, pivot);
            }
        });
        Ctx.SetCurrentLayer(index);
        var r = part.PartRect;
        return TextResult($"{tool}: part {index} \"{part.Name}\" {r.Width}x{r.Height} @({r.X},{r.Y}), pivot ({part.PartPivot.X:0.###},{part.PartPivot.Y:0.###})");
    }

    // ───────────── 스킨(그룹 = 스킨 슬롯) ─────────────

    /// <summary>스킨 도구의 그룹: <c>group</c> 인자, 없으면 고른 그룹(부위면 그 그룹).</summary>
    private Layer SkinGroup(SpriteProject p, JsonObject? a)
    {
        int index;
        if (Arg(a, "group") is not null) index = ReqIndex(a, "group", p.Layers.Count, "레이어");
        else if (Ctx.CurrentGroup is { } current) index = p.IndexOf(current);
        else throw new ToolError("group: 고른 그룹이 없습니다 — group 번호를 주세요(list_skins로 확인)");
        var group = p.Layers[index];
        if (!group.IsGroup) throw new ToolError($"layer {index} \"{group.Name}\"은(는) 그룹이 아닙니다 — 스킨은 그룹에만 있습니다");
        return group;
    }

    private static string SkinNames(IEnumerable<Skin> skins) => string.Join(", ", skins.Select(s => s.Name));

    private static string SkinNames(Layer group) => SkinNames(group.Skins);

    private JsonObject ListSkins()
    {
        var p = RequireProject();
        var groups = new JsonArray();
        for (int i = p.Layers.Count - 1; i >= 0; i--)
        {
            var g = p.Layers[i];
            if (!g.IsGroup) continue;
            var o = new JsonObject
            {
                ["group"] = i, ["name"] = g.Name, ["visible"] = g.Visible,
                ["skins"] = Strings(g.Skins.Select(s => s.Name).ToArray()),
                ["active"] = g.ActiveSkin?.Name, ["base"] = g.BaseSkin?.Name,
            };
            var overrides = new JsonArray();
            var partSkins = new JsonArray();
            foreach (var part in p.ChildrenOf(g).Reverse())
            {
                if (part.SkinOverride is { } own)
                    overrides.Add(new JsonObject { ["part"] = p.IndexOf(part), ["name"] = part.Name, ["skin"] = own.Name, ["partOnly"] = own.IsPartOnly });
                if (part.OwnSkins is { Count: > 0 } ownSkins)
                    partSkins.Add(new JsonObject { ["part"] = p.IndexOf(part), ["name"] = part.Name, ["skins"] = Strings(ownSkins.Select(s => s.Name).ToArray()) });
            }
            if (overrides.Count > 0) o["overrides"] = overrides;
            if (partSkins.Count > 0) o["partSkins"] = partSkins;
            groups.Add(o);
        }
        return TextResult(groups.Count == 0 ? "그룹이 없습니다 — add_group으로 만드세요" : groups.ToJsonString(JsonOptions));
    }

    private JsonObject AddSkin(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        var name = OptString(a, "name");
        if (string.IsNullOrWhiteSpace(name)) name = null;
        if (!string.IsNullOrEmpty(OptString(a, "part")?.Trim()))
        {
            var part = FindPart(p, a);
            var owner = part.Parent!;
            var shown = (OptBool(a, "copy_shown") ?? false) ? part.ShownSkin : null;
            var partSkin = p.Edit<Skin>(Label(tool), ProjectChange.Layers | ProjectChange.Pixels, () => p.AddPartSkin(part, name?.Trim(), shown));
            Ctx.SetCurrentLayer(p.IndexOf(part));
            return TextResult($"{tool}: part {p.IndexOf(part)} \"{part.Name}\" 전용 스킨 \"{partSkin.Name}\" 추가{(shown is null ? "" : $"(\"{shown.Name}\" 복제)")} — 이 부위가 입음. " +
                              $"이 부위 전용 스킨: {SkinNames(part.OwnSkins)} (그룹 \"{owner.Name}\" 스킨: {SkinNames(owner)})");
        }
        var group = SkinGroup(p, a);
        bool copy = OptBool(a, "copy_active") ?? false;
        var from = copy ? group.ActiveSkin : null;
        var skin = p.Edit<Skin>(Label(tool), ProjectChange.Layers | ProjectChange.Pixels, () => p.AddSkin(group, name?.Trim(), from));
        if (!ReferenceEquals(Ctx.CurrentGroup, group)) Ctx.SetCurrentLayer(p.IndexOf(group));
        return TextResult($"{tool}: group {p.IndexOf(group)} \"{group.Name}\"에 스킨 \"{skin.Name}\" 추가{(from is null ? "" : $"(\"{from.Name}\" 복제)")} — 지금 스킨. 스킨: {SkinNames(group)}");
    }

    /// <summary>
    /// 스킨을 이름으로(대소문자 무시 대비) 찾는다: <paramref name="part"/>가 있으면 그 부위가 고를 수 있는 스킨(그룹 스킨 + 그 부위 전용), 없으면 그룹 스킨. 없으면 도구 오류.
    /// </summary>
    private static Skin FindSkin(Layer group, string name, Layer? part = null)
    {
        var list = part?.SkinChoices ?? group.Skins;
        if ((list.FirstOrDefault(s => s.Name == name) ?? list.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) is { } found)
            return found;
        if (part is null && group.AllSkins.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) is { Owner: { } owner })
            throw new ToolError($"\"{name}\"은(는) \"{owner.Name}\" 부위 전용 스킨입니다 — part \"{owner.Name}\"와 함께 set_skin하세요");
        throw new ToolError(part is null
            ? $"group \"{group.Name}\"에 \"{name}\" 스킨이 없습니다 — 있는 스킨: {SkinNames(group)}"
            : $"part \"{part.Name}\"가 고를 수 있는 \"{name}\" 스킨이 없습니다 — 고를 수 있는 스킨: {SkinNames(list)}");
    }

    /// <summary>스킨 도구의 <c>part</c>(부위 이름, <c>group</c>이 있으면 그 그룹 안에서만). 없거나 여럿이면 도구 오류.</summary>
    private Layer FindPart(SpriteProject p, JsonObject? a)
    {
        var partName = OptString(a, "part")?.Trim() ?? "";
        var only = Arg(a, "group") is not null ? SkinGroup(p, a) : null;
        var candidates = p.Layers.Where(l => l.IsPart && l.Parent is { IsGroup: true } && (only is null || ReferenceEquals(l.Parent, only))).ToList();
        var matches = candidates.Where(l => l.Name == partName).ToList();
        if (matches.Count == 0) matches = candidates.Where(l => string.Equals(l.Name, partName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
            throw new ToolError($"part: \"{partName}\" 부위가 없습니다{(only is null ? "" : $"(group \"{only.Name}\" 안)")} — get_document로 부위 이름을 확인하세요");
        if (matches.Count > 1) throw new ToolError($"part: \"{partName}\" 부위가 여러 그룹에 있습니다 — group 번호를 함께 주세요");
        return matches[0];
    }

    private JsonObject SetSkin(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        var name = OptString(a, "skin")?.Trim() ?? "";
        bool follow = OptBool(a, "follow_group") ?? false;
        var partName = OptString(a, "part")?.Trim();
        if (string.IsNullOrEmpty(partName))
        {
            if (follow) throw new ToolError("follow_group: part(부위 이름)와 함께 쓰세요");
            if (name.Length == 0) throw new ToolError("skin: 스킨 이름이 필요합니다(list_skins로 확인)");
            var group = SkinGroup(p, a);
            var skin = FindSkin(group, name);
            Ctx.SetActiveSkin(group, skin);
            return TextResult($"{tool}: group {p.IndexOf(group)} \"{group.Name}\" 지금 스킨 = \"{skin.Name}\"");
        }

        var part = FindPart(p, a);
        var owner = part.Parent!;
        // "그룹"은 그 이름의 스킨이 없을 때만 "그룹 따름"으로 본다.
        if (follow || name.Length == 0 || (name == "그룹" && part.SkinChoices.All(s => s.Name != name)))
        {
            Ctx.SetPartSkin(part, null);
            return TextResult($"{tool}: part {p.IndexOf(part)} \"{part.Name}\" 그룹 스킨 따름 (지금 \"{owner.ActiveSkin?.Name}\")");
        }
        var own = FindSkin(owner, name, part);
        Ctx.SetPartSkin(part, own);
        return TextResult($"{tool}: part {p.IndexOf(part)} \"{part.Name}\"만 스킨 \"{own.Name}\"{(own.IsPartOnly ? " (부위 전용)" : "")} (그룹 \"{owner.Name}\" 스킨은 \"{owner.ActiveSkin?.Name}\")");
    }

    private JsonObject ListOutfits()
    {
        var p = RequireProject();
        var outfits = new JsonArray();
        foreach (var outfit in p.Outfits)
        {
            var slots = new JsonArray();
            foreach (var (group, skin) in outfit.Slots)
                if (p.Layers.Contains(group))
                    slots.Add(new JsonObject { ["group"] = p.IndexOf(group), ["name"] = group.Name, ["skin"] = skin.Name });
            var parts = new JsonArray();
            foreach (var (part, skin) in outfit.Parts)
                if (p.Layers.Contains(part))
                    parts.Add(new JsonObject { ["part"] = p.IndexOf(part), ["name"] = part.Name, ["skin"] = skin.Name });
            var o = new JsonObject { ["name"] = outfit.Name, ["slots"] = slots, ["current"] = SkinManagerWindow.IsCurrentOutfit(p, outfit) };
            if (parts.Count > 0) o["overrides"] = parts;
            var missing = p.MissingOutfitEntries(outfit);
            if (missing.Count > 0) o["missing"] = Strings(missing.ToArray());
            outfits.Add(o);
        }
        return TextResult(outfits.Count == 0 ? "착장 프리셋이 없습니다 — save_outfit으로 지금 착장을 저장하세요" : outfits.ToJsonString(JsonOptions));
    }

    private JsonObject SaveOutfit(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        if (!p.Layers.Any(l => l.IsGroup)) throw new ToolError("그룹이 없습니다 — 착장은 그룹(스킨 칸)마다 스킨을 저장합니다");
        var name = OptString(a, "name")?.Trim();
        var outfit = p.Edit<Outfit>(Label(tool), ProjectChange.Layers | ProjectChange.Settings, () => p.SaveOutfit(string.IsNullOrEmpty(name) ? null : name));
        return TextResult($"{tool}: 착장 \"{outfit.Name}\" 저장 (그룹 {outfit.Slots.Count}개, 부위 재정의 {outfit.Parts.Count}개). 착장: {string.Join(", ", p.Outfits.Select(o => o.Name))}");
    }

    private JsonObject ApplyOutfit(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        var name = OptString(a, "name")?.Trim() ?? "";
        if (name.Length == 0) throw new ToolError("name: 착장 이름이 필요합니다(list_outfits로 확인)");
        var outfit = p.Outfits.FirstOrDefault(o => o.Name == name)
                     ?? p.Outfits.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase))
                     ?? throw new ToolError(p.Outfits.Count == 0
                         ? "착장 프리셋이 없습니다 — save_outfit으로 저장하세요"
                         : $"\"{name}\" 착장이 없습니다 — 있는 착장: {string.Join(", ", p.Outfits.Select(o => o.Name))}");
        IReadOnlyList<string> missing = [];
        Ctx.Structural(() => missing = p.ApplyOutfit(outfit));
        return TextResult($"{tool}: 착장 \"{outfit.Name}\" 입힘" + (missing.Count == 0 ? "" : $" — 건너뛴 항목: {string.Join(", ", missing)}"));
    }

    /// <summary>0~1 실수 인자.</summary>
    private static double? OptUnit(JsonObject? a, string key)
    {
        if (Arg(a, key) is not { } n) return null;
        double v;
        if (n is JsonValue jv && jv.TryGetValue(out double d)) v = d;
        else if (n is JsonValue sv && sv.TryGetValue(out string? s) && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) v = d;
        else throw new ToolError($"{key}: 숫자가 필요합니다 (받은 값: {Show(n)})");
        if (double.IsNaN(v) || v < 0 || v > 1) throw new ToolError($"{key}: 0~1 범위여야 합니다 (받은 값: {v})");
        return v;
    }

    private static ImageAnchor ParseAnchor(JsonObject? a) => (OptEnum(a, "anchor", AnchorNames) ?? "center") switch
    {
        "top-left" => ImageAnchor.TopLeft,
        "top" => ImageAnchor.TopCenter,
        "top-right" => ImageAnchor.TopRight,
        "left" => ImageAnchor.MiddleLeft,
        "right" => ImageAnchor.MiddleRight,
        "bottom-left" => ImageAnchor.BottomLeft,
        "bottom" => ImageAnchor.BottomCenter,
        "bottom-right" => ImageAnchor.BottomRight,
        _ => ImageAnchor.Center,
    };

    private JsonObject ResizeCanvas(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int width = ReqInt(a, "width", 1, PixelImage.MaxSize), height = ReqInt(a, "height", 1, PixelImage.MaxSize);
        var anchor = ParseAnchor(a);
        int oldW = p.Width, oldH = p.Height;
        if (oldW == width && oldH == height) return TextResult($"{tool}: 캔버스가 이미 {width}x{height}입니다 — 바뀐 것 없음");
        p.Edit(Label(tool), ProjectChange.Canvas | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Layers | ProjectChange.Settings,
            () => p.ResizeCanvas(width, height, anchor));
        return TextResult($"{tool}: 캔버스 {oldW}x{oldH} → {width}x{height}");
    }

    private static readonly string[] AnchorNames =
        ["top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right"];

    // ───────────── 색·파일 도구 ─────────────

    private JsonObject SetPalette(string tool, JsonObject? a)
    {
        RequireProject();
        var nodes = ReqArray(a, "colors");
        if (nodes.Count is < 1 or > 256) throw new ToolError($"colors: 1~256개여야 합니다 (받은 개수: {nodes.Count})");
        var colors = new List<Color32>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            var text = AsString(nodes[i]) ?? throw new ToolError($"colors[{i}]: 색 문자열이 필요합니다");
            colors.Add(ParseColor(text, $"colors[{i}]"));
        }
        _host.SetPalette(colors);
        return TextResult($"{tool}: 팔레트 {colors.Count}색으로 바꿈");
    }

    private JsonObject SetColors(string tool, JsonObject? a)
    {
        RequireProject();
        var primary = OptColor(a, "primary");
        var secondary = OptColor(a, "secondary");
        if (primary is null && secondary is null) throw new ToolError("primary 또는 secondary 중 하나 이상이 필요합니다");
        var ctx = Ctx;
        if (primary is { } pc) ctx.Primary = pc;
        if (secondary is { } sc) ctx.Secondary = sc;
        return TextResult($"{tool}: 기본색 {ctx.Primary.ToHex()}, 보조색 {ctx.Secondary.ToHex()}");
    }

    private JsonObject NewProject(string tool, JsonObject? a)
    {
        int width = ReqInt(a, "width", 1, PixelImage.MaxSize), height = ReqInt(a, "height", 1, PixelImage.MaxSize);
        var fileName = OptString(a, "file_name");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = null;
        Color32? background = OptColor(a, "background") is { A: > 0 } bg ? bg : null;
        bool ok = _host.CreateProject(width, height, fileName?.Trim(), background, out var message);
        return TextResult($"{tool}: {message}", isError: !ok);
    }

    private JsonObject SaveProject(string tool)
    {
        RequireProject();
        Ctx.CommitFloating();
        bool ok = _host.SaveProject(out var message);
        return TextResult($"{tool}: {message}", isError: !ok);
    }

    private JsonObject OpenFile(string tool, JsonObject? a)
    {
        var path = ReqString(a, "path").Trim();
        if (path.Length == 0) throw new ToolError("path가 비어 있습니다");
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~').TrimStart('/'));
        else if (!Path.IsPathRooted(path) && _host.CurrentFolder is { } folder)
            path = Path.Combine(folder, path);
        bool ok = _host.OpenFile(path, out var message);
        return TextResult($"{tool}: {message}", isError: !ok);
    }

    // ───────────── 편집 대상·기록 ─────────────

    /// <summary>픽셀을 고칠 셀(검증 끝난 프레임·레이어와 그 크기 — 부위면 부위 크기).</summary>
    private readonly record struct Target(SpriteProject Project, int Frame, int Layer, int Width, int Height);

    private static string Label(string tool) => "AI: " + tool;

    private SpriteProject RequireProject() => Ctx.Project ?? throw new ToolError(NoProject);

    /// <summary>바꾸는 도구 시작: 떠 있는 선택을 내려놓는다. 사용자가 그리는 중이면 거부한다.</summary>
    private SpriteProject BeginMutation()
    {
        var p = RequireProject();
        Ctx.CommitFloating();
        if (p.IsStrokeActive) throw new ToolError("사용자가 지금 그리는 중입니다 — 잠시 뒤 다시 시도하세요");
        return p;
    }

    /// <summary>픽셀 도구의 대상 셀을 고르고 그룹·숨김·잠금을 검사한다.</summary>
    private Target EditTarget(JsonObject? a)
    {
        var p = BeginMutation();
        int frame = Frame(a), layer = LayerIndex(a);
        var l = p.Layers[layer];
        if (l.IsGroup) throw new ToolError("그룹 레이어에는 그릴 수 없습니다 — 부위 레이어를 고르세요");
        if (!p.IsLayerVisible(layer))
            throw new ToolError($"layer {layer} \"{l.Name}\"(또는 그 그룹)이(가) 숨겨져 있습니다 — set_layer로 visible=true로 바꾼 뒤 다시 시도하세요");
        if (p.IsLayerLocked(layer))
            throw new ToolError($"layer {layer} \"{l.Name}\"(또는 그 그룹)이(가) 잠겨 있습니다 — 사용자에게 잠금 해제를 부탁하세요");
        var (w, h) = SourceSize(p, layer);
        return new Target(p, frame, layer, w, h);
    }

    /// <summary>
    /// 대상 셀을 현재로 만들고 스트로크 하나로 <paramref name="edit"/>을 실행해 기록한다(바뀐 것이 없으면 기록 없음).
    /// 예외가 나면 셀을 되돌린다. 바뀐 픽셀 수를 돌려준다.
    /// </summary>
    private int Stroke(string tool, Target t, Func<PixelImage, int> edit)
    {
        Ctx.SetCurrent(t.Layer, t.Frame);
        var p = t.Project;
        var image = p.BeginCelStroke(t.Layer, t.Frame);
        int changed;
        try
        {
            changed = edit(image);
        }
        catch
        {
            p.CancelCelStroke();
            throw;
        }
        p.CommitCelStroke(Label(tool));
        return changed;
    }

    private static JsonObject Done(string tool, int changed, Target t, string extra = "")
        => TextResult($"{tool}: {changed} px 바뀜 (frame {t.Frame}, layer {t.Layer}{(t.Project.Layers[t.Layer].IsPart ? ", 부위 안 좌표" : "")}){extra}");

    /// <summary>읽기/쓰기 대상 크기: 부위면 부위 크기, 아니면 캔버스(프레임) 크기.</summary>
    private static (int Width, int Height) SourceSize(SpriteProject p, int? layer)
        => layer is { } l && p.Layers[l].IsPart ? (p.Layers[l].PartSize.Width, p.Layers[l].PartSize.Height) : (p.Width, p.Height);

    /// <summary>읽기 원본: 레이어를 고르면 그 셀(빈 셀이면 투명, 그룹이면 그룹 합성), 아니면 보이는 레이어 합성.</summary>
    private static PixelImage Source(SpriteProject p, int frame, int? layer)
    {
        if (layer is not { } l) return p.Flatten(frame);
        if (p.Layers[l].IsGroup) return p.FlattenGroup(p.Layers[l], frame);
        if (p.GetCel(l, frame) is { } cel) return cel;
        var (w, h) = SourceSize(p, l);
        return new PixelImage(w, h);
    }

    private static string KindName(LayerKind kind) => kind switch
    {
        LayerKind.Group => "group",
        LayerKind.Part => "part",
        _ => "normal",
    };

    private int Frame(JsonObject? a) => OptIndex(a, "frame", Ctx.FrameCount, "프레임") ?? Ctx.CurrentFrame;

    private int LayerIndex(JsonObject? a) => OptLayer(a) ?? Ctx.CurrentLayer;

    private int? OptLayer(JsonObject? a) => OptIndex(a, "layer", Ctx.LayerCount, "레이어");

    /// <summary>영역 인자(x, y, width, height; 기본 = 캔버스 전체)를 캔버스로 자른다. 비면 오류.</summary>
    private static Rectangle Region(JsonObject? a, int w, int h)
    {
        int x = OptInt(a, "x", -CoordLimit, CoordLimit) ?? 0, y = OptInt(a, "y", -CoordLimit, CoordLimit) ?? 0;
        int rw = OptInt(a, "width", 1, CoordLimit) ?? w - x, rh = OptInt(a, "height", 1, CoordLimit) ?? h - y;
        var r = rw <= 0 || rh <= 0 ? Rectangle.Empty : Rectangle.Intersect(new Rectangle(x, y, rw, rh), new Rectangle(0, 0, w, h));
        if (r.Width <= 0 || r.Height <= 0) throw new ToolError($"영역이 캔버스({w}x{h}) 밖입니다");
        return r;
    }

    private static string DirectionName(TagDirection d) => d switch
    {
        TagDirection.Reverse => "reverse",
        TagDirection.PingPong => "pingpong",
        _ => "forward",
    };

    // ───────────── 인자 읽기 ─────────────

    /// <summary>인자 값(없거나 JSON null이면 null).</summary>
    private static JsonNode? Arg(JsonObject? a, string key) => a is not null && a.TryGetPropertyValue(key, out var n) ? n : null;

    private static string Show(JsonNode n)
    {
        var s = n.ToJsonString(JsonOptions);
        return s.Length > 40 ? s[..40] + "…" : s;
    }

    private static string? AsString(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static bool TryInt(JsonNode n, out int value)
    {
        value = 0;
        if (n is not JsonValue v) return false;
        if (v.TryGetValue(out int i)) { value = i; return true; }
        if (v.TryGetValue(out double d) && Math.Abs(d) <= int.MaxValue && d == Math.Floor(d)) { value = (int)d; return true; }
        if (v.TryGetValue(out string? s) && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) { value = i; return true; }
        return false;
    }

    private static int? OptInt(JsonObject? a, string key, int min = int.MinValue, int max = int.MaxValue)
    {
        if (Arg(a, key) is not { } n) return null;
        if (!TryInt(n, out int v)) throw new ToolError($"{key}: 정수가 필요합니다 (받은 값: {Show(n)})");
        if (v < min || v > max) throw new ToolError($"{key}: {min}~{max} 범위여야 합니다 (받은 값: {v})");
        return v;
    }

    private static int ReqInt(JsonObject? a, string key, int min = int.MinValue, int max = int.MaxValue)
        => OptInt(a, key, min, max) ?? throw new ToolError($"{key} 값이 필요합니다");

    /// <summary>번호 인자(0 ~ count-1).</summary>
    private static int? OptIndex(JsonObject? a, string key, int count, string what)
    {
        if (Arg(a, key) is not { } n) return null;
        if (!TryInt(n, out int v)) throw new ToolError($"{key}: 정수가 필요합니다 (받은 값: {Show(n)})");
        if (v < 0 || v >= count) throw new ToolError($"{key} {v}: {what} 번호는 0~{count - 1} 범위여야 합니다");
        return v;
    }

    private static int ReqIndex(JsonObject? a, string key, int count, string what)
        => OptIndex(a, key, count, what) ?? throw new ToolError($"{key} 값이 필요합니다");

    private static bool? OptBool(JsonObject? a, string key)
    {
        if (Arg(a, key) is not { } n) return null;
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out bool b)) return b;
            if (v.TryGetValue(out string? s) && bool.TryParse(s.Trim(), out b)) return b;
        }
        throw new ToolError($"{key}: true 또는 false가 필요합니다 (받은 값: {Show(n)})");
    }

    private static string? OptString(JsonObject? a, string key)
    {
        if (Arg(a, key) is not { } n) return null;
        return AsString(n) ?? throw new ToolError($"{key}: 문자열이 필요합니다 (받은 값: {Show(n)})");
    }

    private static string ReqString(JsonObject? a, string key)
        => OptString(a, key) ?? throw new ToolError($"{key} 값이 필요합니다");

    private static JsonArray ReqArray(JsonObject? a, string key)
        => Arg(a, key) as JsonArray ?? throw new ToolError($"{key}: 배열이 필요합니다");

    private static string? OptEnum(JsonObject? a, string key, params string[] values)
    {
        if (OptString(a, key) is not { } s) return null;
        var v = s.Trim().ToLowerInvariant();
        if (!values.Contains(v))
            throw new ToolError($"{key}: {string.Join(", ", values.Select(x => $"\"{x}\""))} 중 하나여야 합니다 (받은 값: \"{s}\")");
        return v;
    }

    private static string ReqEnum(JsonObject? a, string key, params string[] values)
        => OptEnum(a, key, values) ?? throw new ToolError($"{key} 값이 필요합니다");

    private static Color32? OptColor(JsonObject? a, string key)
        => OptString(a, key) is { } s ? ParseColor(s, key) : null;

    private static Color32 ReqColor(JsonObject? a, string key)
        => OptColor(a, key) ?? throw new ToolError($"{key} 값이 필요합니다");

    /// <summary>"#RRGGBB" / "#RRGGBBAA" / "transparent". 알파 0은 모두 같은 투명으로 맞춘다.</summary>
    private static Color32 ParseColor(string text, string key)
    {
        var s = text.Trim();
        if (s.Equals("transparent", StringComparison.OrdinalIgnoreCase) || s.Equals("none", StringComparison.OrdinalIgnoreCase))
            return Color32.Transparent;
        if (!Color32.TryParseHex(s, out var c))
            throw new ToolError($"{key}: 색은 \"#RRGGBB\", \"#RRGGBBAA\" 또는 \"transparent\" 형식이어야 합니다 (받은 값: \"{text}\")");
        return c.A == 0 ? Color32.Transparent : c;
    }

    // ───────────── 결과 ─────────────

    private static JsonObject TextResult(string text, bool isError = false) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };
}
