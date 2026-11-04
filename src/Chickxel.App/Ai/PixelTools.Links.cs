using System.Text.Json.Nodes;
using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace Chickxel.App.Ai;

// 셀 링크 도구: 여러 프레임의 셀이 그림 하나를 같이 쓰게 묶기·풀기, 링크 묶음 색 바꾸기(타임라인의 사슬·띠 색).
public sealed partial class PixelTools
{
    private static readonly string LinkColorList = string.Join(", ", SpriteProject.LinkColorNames);

    private static IEnumerable<JsonObject> LinkToolDefinitions() =>
    [
        Tool("link_cels",
            "Link cels of one layer so the given frames share ONE drawing: editing any linked cel (drawing, clear, fill…) changes all of them. " +
            "Frames need not be adjacent. If one of the frames is already linked, the others join that link (and its color); otherwise the first frame that has pixels " +
            "becomes the shared drawing and the other frames' pixels are replaced (empty frames too). New links get the next unused timeline color " +
            "in the order links are made (" + LinkColorList + "); pass `color` to choose it. A group layer links every part of the group: each part gets one new link of " +
            "exactly those frames (its drawing in `frames[0]`, or the first frame showing one), taken out of any other link of those cels — group links win over part links. " +
            "get_document lists each layer's links (a group lists the frames where all its parts are linked together).",
            new JsonObject
            {
                ["layer"] = Int("Layer index (0 = bottom): normal, part or group layer. Default: current.", 0),
                ["frames"] = IntArray("Frame indices to link (at least 2)."),
                ["color"] = Enum("Timeline color of the link (default: keep / next unused).", [.. SpriteProject.LinkColorNames]),
            },
            "frames"),
        Tool("unlink_cels",
            "Unlink cels: each given frame of the layer (of every part, for a group layer) gets its own copy of the drawing so it can be edited alone. Pixels are unchanged, except: a cel that was showing " +
            "an animated effect's held end look only because it was linked to the effect range's last cel (holdEnd) gets that look baked into its drawing exactly as shown " +
            "(enabled effects only, clipped to the cel; not when an enabled effect's range covers that frame — then it keeps the raw copy). " +
            "If unlinking a range-end cel makes other still-linked cels lose the held look, those are baked too.",
            new JsonObject
            {
                ["layer"] = Int("Layer index (0 = bottom): normal, part or group layer. Default: current.", 0),
                ["frames"] = IntArray("Frame indices to unlink."),
            },
            "frames"),
        Tool("set_link_color",
            "Change the timeline color of a link (the chain icon and bar under linked cels). `frame` is any frame of that link. Use when the user asks to recolor a link.",
            new JsonObject
            {
                ["layer"] = Int("Layer index (0 = bottom). Default: current.", 0),
                ["frame"] = Int("Any frame of the link.", 0),
                ["color"] = Enum("New color.", [.. SpriteProject.LinkColorNames]),
            },
            "frame", "color"),
    ];

    private static JsonObject IntArray(string description) => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 },
        ["description"] = description,
    };

    /// <summary>링크 도구의 레이어(그룹이면 그 부위들에 적용): 잠긴 레이어면 도구 오류.</summary>
    private int LinkLayer(SpriteProject p, JsonObject? a)
    {
        int layer = LayerIndex(a);
        RequireUnlocked(p, layer);
        return layer;
    }

    /// <summary>프레임 번호 목록 인자(겹치면 한 번, 오름차순).</summary>
    private static List<int> ReqFrames(JsonObject? a, string key, int frameCount)
    {
        if (Arg(a, key) is not JsonArray array) throw new ToolError(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_PixelTools_ReqInt_27A61716E9, key));
        var frames = new SortedSet<int>();
        foreach (var n in array)
        {
            if (n is null || !TryInt(n, out int f) || f < 0 || f >= frameCount)
                throw new ToolError(UiStrings.FormatAi_Link_BadFrame(value: n is null ? "null" : Show(n), count: frameCount));
            frames.Add(f);
        }
        return [.. frames];
    }

    private static int? OptLinkColor(JsonObject? a)
    {
        if (OptString(a, "color") is not { } name) return null;
        int color = SpriteProject.LinkColorNames.ToList().IndexOf(name.Trim().ToLowerInvariant());
        return color >= 0 ? color : throw new ToolError(UiStrings.FormatAi_Link_BadColor(value: name, colors: LinkColorList));
    }

    /// <summary>레이어의 링크 묶음을 글로(모델이 읽음): "links: [1,2,3] blue; [5,8] orange" 또는 "no links".</summary>
    private static string LinksText(SpriteProject p, int layer)
        => p.LinkGroups(layer) is { Count: > 0 } groups
            ? "links: " + string.Join("; ", groups.Select(g => $"[{string.Join(",", g.Frames)}] {SpriteProject.LinkColorNames[g.Color]}"))
            : "no links";

    /// <summary>get_document 레이어 항목의 링크 묶음(없으면 null).</summary>
    private static JsonArray? LinksJson(SpriteProject p, int layer)
        => p.LinkGroups(layer) is not { Count: > 0 } groups ? null
            : new JsonArray([.. groups.Select(g => (JsonNode)new JsonObject
            {
                ["frames"] = new JsonArray([.. g.Frames.Select(f => (JsonNode)f)]),
                ["color"] = SpriteProject.LinkColorNames[g.Color],
            })]);

    private JsonObject LinkCels(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int layer = LinkLayer(p, a);
        var frames = ReqFrames(a, "frames", p.Frames.Count);
        if (frames.Count < 2) throw new ToolError(UiStrings.Ai_Link_TwoFrames);
        int? color = OptLinkColor(a);
        int linked = 0;
        bool recolored = false;
        // 바뀐 것이 없으면 편집 안에서 오류를 던져 빈 실행취소 단계를 남기지 않는다
        p.Edit(Label(tool), ProjectChange.Pixels, () =>
        {
            linked = p.LinkCels(frames.Select(f => (layer, f)));
            if (color is { } c) recolored = p.SetLinkColor(layer, frames[0], c);
            if (linked == 0 && !recolored) throw new ToolError(UiStrings.Ai_Link_Nothing);
        });
        Ctx.SetCurrent(layer, frames[0]);
        return TextResult($"{tool}: layer {layer} frames {string.Join(",", frames)} linked ({linked} cel(s) newly linked{(recolored ? ", color set" : "")}). {LinksText(p, layer)}");
    }

    private JsonObject UnlinkCels(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int layer = LinkLayer(p, a);
        var frames = ReqFrames(a, "frames", p.Frames.Count);
        UnlinkResult result = default;
        p.Edit(Label(tool), ProjectChange.Pixels, () =>
        {
            result = p.UnlinkCels(frames.Select(f => (layer, f)));
            if (result.Unlinked == 0) throw new ToolError(UiStrings.Ai_Link_NoneLinked);
        });
        Ctx.SetCurrent(layer, frames[0]);
        string baked = result.Baked > 0 ? $", {result.Baked} cel(s) baked with the effect's held end look as shown" : "";
        return TextResult($"{tool}: layer {layer} — {result.Unlinked} cel(s) unlinked{baked}. {LinksText(p, layer)}");
    }

    private JsonObject SetLinkColor(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int layer = LinkLayer(p, a);
        int frame = ReqIndex(a, "frame", p.Frames.Count, UiStrings.S_PixelTools_SetFrameDuration_D6FFD433A4);
        int color = OptLinkColor(a) ?? throw new ToolError(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_PixelTools_ReqInt_27A61716E9, "color"));
        if (!p.IsCelLinked(layer, frame)) throw new ToolError(UiStrings.FormatAi_Link_NotLinked(layer: layer, frame: frame));
        if (p.LinkMarks(layer)[frame].Color != color) p.Edit(Label(tool), ProjectChange.Pixels, () => p.SetLinkColor(layer, frame, color));
        Ctx.SetCurrent(layer, frame);
        return TextResult($"{tool}: layer {layer} frame {frame} link = {SpriteProject.LinkColorNames[color]}. {LinksText(p, layer)}");
    }
}
