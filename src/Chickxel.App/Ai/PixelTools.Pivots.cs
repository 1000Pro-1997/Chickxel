using System.Globalization;
using System.Text.Json.Nodes;
using PointF = System.Drawing.PointF;
using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace Chickxel.App.Ai;

/// <summary>
/// 피벗 도구: 레이어마다의 피벗(set_layer의 pivot_* 인자 — 부위는 부위 기준, 일반 레이어·그룹은 캔버스 기준)과 캔버스 피벗(set_canvas_pivot — 내보내기 피벗,
/// 캔버스 자신의 값 또는 대표 레이어의 피벗), get_document의 피벗 항목. 피벗은 0~1, 아래 왼쪽 원점이다(<see cref="Pivot"/>).
/// 바꾸는 호출은 프로젝트 메서드를 바깥 편집으로 감싸 실행취소 한 단계를 남기고, 바뀌는 것이 없으면 기록하지 않는다.
/// </summary>
public sealed partial class PixelTools
{
    private const string PivotFromFrameNote = "Apply pivot_x/pivot_y from this frame to the last frame (those frames get their own pivot; later frames shift by the same amount) " +
                                              "and, with clear_frame_pivots, clear only from this frame. Omit to change the base pivot (frames without their own pivot).";
    private const string ClearFramePivotsNote = "Remove per-frame pivots (from pivot_from_frame, else in every frame) before applying pivot_x/pivot_y.";

    private static IEnumerable<JsonObject> PivotToolDefinitions() =>
    [
        Tool("set_canvas_pivot",
            "Change the canvas pivot used by exports (sprite sheet / Unity sprite pivot; 0..1 of the canvas, origin bottom-left). pivot_x/pivot_y set the canvas's own pivot; " +
            "representative_layer makes every frame's canvas pivot follow that layer's pivot (then the own pivot is kept but unused). Layer pivots (effects, part export) " +
            "are set with set_layer. get_document `canvasPivot` shows the current values. One undo step.",
            new JsonObject
            {
                ["pivot_x"] = UnitProp("Canvas pivot x (0 = left, 1 = right)."),
                ["pivot_y"] = UnitProp("Canvas pivot y (0 = bottom, 1 = top)."),
                ["pivot_from_frame"] = Int(PivotFromFrameNote, 0),
                ["clear_frame_pivots"] = Bool(ClearFramePivotsNote),
                ["representative_layer"] = Int("Layer index (normal, part or group) whose pivot becomes the canvas pivot of every frame; -1 = none (use the canvas's own pivot).", -1),
            }),
    ];

    /// <summary>set_layer의 피벗 인자 스키마(pivot_x, pivot_y, pivot_from_frame, clear_frame_pivots)를 더한 <paramref name="props"/>.</summary>
    private static JsonObject WithLayerPivotProps(JsonObject props)
    {
        props["pivot_x"] = UnitProp("Layer pivot x (0 = left, 1 = right; part: of the part, other layers: of the canvas).");
        props["pivot_y"] = UnitProp("Layer pivot y (0 = bottom, 1 = top; part: of the part, other layers: of the canvas).");
        props["pivot_from_frame"] = Int(PivotFromFrameNote, 0);
        props["clear_frame_pivots"] = Bool(ClearFramePivotsNote);
        return props;
    }

    private static JsonObject UnitProp(string description) => new() { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1, ["description"] = description };

    /// <summary>피벗 인자: 새 x·y(0~1, 없으면 그대로), "이 프레임부터"(없으면 기본 피벗), 프레임마다의 피벗 지우기.</summary>
    private readonly record struct PivotArgs(double? X, double? Y, int? From, bool Clear);

    /// <summary>피벗 인자를 읽는다. 바꿀 것이 없으면 null(pivot_from_frame만 있으면 오류).</summary>
    private static PivotArgs? ReadPivotArgs(JsonObject? a, int frameCount)
    {
        double? x = OptUnit(a, "pivot_x"), y = OptUnit(a, "pivot_y");
        int? from = OptIndex(a, "pivot_from_frame", frameCount, UiStrings.S_PixelTools_Frame_CAED4A473B);
        bool clear = OptBool(a, "clear_frame_pivots") ?? false;
        if (x is null && y is null && !clear)
            return from is null ? null : throw new ToolError(UiStrings.Ai_Pivot_FromFrameAlone);
        return new PivotArgs(x, y, from, clear);
    }

    /// <summary>
    /// 피벗 바꾸기의 결과(순수 계산): 기본 피벗과 프레임마다 따로 둔 피벗(null = 기본을 따름). 먼저 지우고(<see cref="PivotArgs.Clear"/>),
    /// 그다음 기본 피벗을 바꾸거나 "이 프레임부터" 바꾼다 — 그 프레임이 새 값이 되고 뒤 프레임도 같은 만큼 옮긴다(<see cref="SpriteProject.SetLayerPivotFrom"/>와 같음).
    /// </summary>
    private static (Pivot Base, Pivot?[] Frames) PlanPivot(Pivot basePivot, Pivot?[] frames, PivotArgs args)
    {
        var next = (Pivot?[])frames.Clone();
        if (args.Clear)
            for (int f = args.From ?? 0; f < next.Length; f++) next[f] = null;
        if (args.X is null && args.Y is null) return (basePivot, next);
        if (args.From is not { } start)
            return (new Pivot((float)(args.X ?? basePivot.X), (float)(args.Y ?? basePivot.Y)), next);
        var cur = next[start] ?? basePivot;
        var target = new Pivot((float)(args.X ?? cur.X), (float)(args.Y ?? cur.Y));
        float dx = target.X - cur.X, dy = target.Y - cur.Y;
        for (int f = start; f < next.Length; f++)
        {
            var v = next[f] ?? basePivot;
            next[f] = f == start ? target : new Pivot(v.X + dx, v.Y + dy);
        }
        return (basePivot, next);
    }

    /// <summary>레이어 피벗 바꾸기(바깥 편집 안에서 실행). 바뀌는 것이 없으면 null.</summary>
    private static Action? LayerPivotEdit(SpriteProject p, int index, PivotArgs args)
    {
        var layer = p.Layers[index];
        var frames = new Pivot?[p.Frames.Count];
        for (int f = 0; f < frames.Length; f++)
            if (p.HasLayerFramePivot(layer, f)) frames[f] = p.GetLayerPivot(layer, f);
        var (nextBase, next) = PlanPivot(layer.Pivot, frames, args);
        if (nextBase == layer.Pivot && next.SequenceEqual(frames)) return null;
        return () =>
        {
            if (nextBase != layer.Pivot) p.SetLayerPivot(index, nextBase);
            for (int f = 0; f < next.Length; f++)
                if (next[f] != frames[f]) p.SetLayerFramePivot(index, f, next[f]);
        };
    }

    /// <summary>캔버스 자신의 피벗 바꾸기(기본 피벗 + 프레임 재정의, 바깥 편집 안에서 실행). 바뀌는 것이 없으면 null.</summary>
    private static Action? CanvasPivotEdit(SpriteProject p, PivotArgs args)
    {
        var frames = p.Frames.Select(f => f.Pivot).ToArray();
        var (nextBase, next) = PlanPivot(p.Sprite.DefaultPivot, frames, args);
        if (nextBase == p.Sprite.DefaultPivot && next.SequenceEqual(frames)) return null;
        return () =>
        {
            if (nextBase != p.Sprite.DefaultPivot) p.Sprite.DefaultPivot = nextBase;
            for (int f = 0; f < next.Length; f++)
                if (next[f] != frames[f]) p.SetFramePivot(f, next[f]);
        };
    }

    private JsonObject SetCanvasPivot(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        var args = ReadPivotArgs(a, p.Frames.Count);
        int? rep = OptInt(a, "representative_layer", -1, p.Layers.Count - 1);
        if (args is null && rep is null) throw new ToolError(UiStrings.Ai_Pivot_CanvasNothing);
        var pivotEdit = args is { } pa ? CanvasPivotEdit(p, pa) : null;
        var representative = rep is { } r ? (r < 0 ? null : p.Layers[r]) : p.PivotLayer;
        bool repChanged = !ReferenceEquals(representative, p.PivotLayer);
        int frame = args?.From ?? Ctx.CurrentFrame;
        if (pivotEdit is null && !repChanged)
            return TextResult($"{tool}: {UiStrings.Ai_Pivot_Unchanged} — {CanvasPivotText(p, frame)}");
        p.Edit(Label(tool), ProjectChange.Frames | ProjectChange.Layers | ProjectChange.Settings, () =>
        {
            pivotEdit?.Invoke();
            if (repChanged) p.SetPivotLayer(representative);
        });
        if (args?.From is { } from) Ctx.SetCurrent(Ctx.CurrentLayer, from);
        return TextResult($"{tool}: {CanvasPivotText(p, frame)}");
    }

    // ───────────── 보고(get_document·결과 글) ─────────────

    /// <summary>get_document 레이어 항목에 피벗을 넣는다: 기본 피벗(그 레이어의 공간), 현재 프레임의 캔버스 픽셀, 프레임마다의 피벗(있을 때만, 같은 값이 이어지면 묶음).</summary>
    private static void AddLayerPivotJson(JsonObject o, SpriteProject p, Layer layer, int frame)
    {
        o["pivot"] = PivotJson(layer.Pivot);
        o["pivotPx"] = PixelJson(p.LayerPivotPixel(layer, frame));
        if (FramePivotsJson(OwnPivots(p, layer)) is { } own) o["framePivots"] = own;
    }

    /// <summary>get_document의 캔버스 피벗: 자신의 기본 피벗·프레임마다의 피벗, 대표 레이어, 현재 프레임의 실제(내보내기) 캔버스 피벗.</summary>
    private static JsonObject CanvasPivotJson(SpriteProject p, int frame)
    {
        var o = new JsonObject { ["pivot"] = PivotJson(p.Sprite.DefaultPivot) };
        if (FramePivotsJson(p.Frames.Select(f => f.Pivot).ToArray()) is { } own) o["framePivots"] = own;
        o["representative"] = p.PivotLayer is { } rep ? new JsonObject { ["layer"] = p.IndexOf(rep), ["name"] = rep.Name } : null;
        var effective = PivotJson(p.GetFramePivot(frame));
        effective["frame"] = frame;
        effective["px"] = PixelJson(PivotPixel(p, p.GetFramePivot(frame)));
        o["effective"] = effective;
        return o;
    }

    /// <summary>레이어의 프레임마다 따로 둔 피벗(없는 프레임은 null).</summary>
    private static Pivot?[] OwnPivots(SpriteProject p, Layer layer)
    {
        var own = new Pivot?[p.Frames.Count];
        for (int f = 0; f < own.Length; f++)
            if (p.HasLayerFramePivot(layer, f)) own[f] = p.GetLayerPivot(layer, f);
        return own;
    }

    /// <summary>프레임마다의 피벗을 같은 값이 이어지는 묶음으로: [{from, to, x, y}]. 하나도 없으면 null.</summary>
    private static JsonArray? FramePivotsJson(Pivot?[] own)
    {
        JsonArray? runs = null;
        foreach (var (from, to, pivot) in PivotRuns(own))
        {
            var run = new JsonObject { ["from"] = from, ["to"] = to };
            run["x"] = RoundUnit(pivot.X);
            run["y"] = RoundUnit(pivot.Y);
            (runs ??= []).Add(run);
        }
        return runs;
    }

    private static IEnumerable<(int From, int To, Pivot Pivot)> PivotRuns(Pivot?[] own)
    {
        for (int f = 0; f < own.Length;)
        {
            if (own[f] is not { } v) { f++; continue; }
            int end = f;
            while (end + 1 < own.Length && own[end + 1] == v) end++;
            yield return (f, end, v);
            f = end + 1;
        }
    }

    /// <summary>결과 글의 "따로 둔 프레임" 꼬리(없으면 빈 글): 3-5, 8.</summary>
    private static string OwnFramesText(Pivot?[] own)
    {
        var frames = new List<string>();
        for (int f = 0; f < own.Length;)
        {
            if (own[f] is null) { f++; continue; }
            int end = f;
            while (end + 1 < own.Length && own[end + 1] is not null) end++;
            frames.Add(end == f ? $"{f}" : $"{f}-{end}");
            f = end + 1;
        }
        return frames.Count == 0 ? "" : UiStrings.FormatAi_Pivot_OwnFrames(frames: string.Join(", ", frames));
    }

    /// <summary>레이어 피벗 요약(결과 글): 기본 피벗·공간·따로 둔 프레임, 그 프레임의 피벗과 캔버스 픽셀.</summary>
    private static string LayerPivotText(SpriteProject p, Layer layer, int frame)
        => UiStrings.FormatAi_Pivot_LayerText(
            pivot: PivotText(layer.Pivot),
            space: layer.IsPart ? UiStrings.Ai_Pivot_SpacePart : UiStrings.Ai_Pivot_SpaceCanvas,
            frames: OwnFramesText(OwnPivots(p, layer)),
            frame: frame,
            current: PivotText(p.GetLayerPivot(layer, frame)),
            pixel: PixelText(p.LayerPivotPixel(layer, frame)));

    /// <summary>캔버스 피벗 요약(결과 글): 자신의 피벗·따로 둔 프레임, 대표 레이어, 그 프레임의 실제 캔버스 피벗과 픽셀.</summary>
    private static string CanvasPivotText(SpriteProject p, int frame)
    {
        var effective = p.GetFramePivot(frame);
        return UiStrings.FormatAi_Pivot_CanvasText(
            pivot: PivotText(p.Sprite.DefaultPivot),
            frames: OwnFramesText(p.Frames.Select(f => f.Pivot).ToArray()),
            representative: p.PivotLayer is { } rep ? $"{p.IndexOf(rep)} \"{rep.Name}\"" : UiStrings.Ai_Pivot_NoRepresentative,
            frame: frame,
            effective: PivotText(effective),
            pixel: PixelText(PivotPixel(p, effective)));
    }

    /// <summary>캔버스 기준 피벗의 캔버스 픽셀(왼쪽 위 원점).</summary>
    private static PointF PivotPixel(SpriteProject p, Pivot pivot)
    {
        var (x, y) = pivot.ToLocalPixel(p.Width, p.Height);
        return new PointF(x, y);
    }

    private static double RoundUnit(float v) => Math.Round(v, 4);

    private static JsonObject PivotJson(Pivot v) => new() { ["x"] = RoundUnit(v.X), ["y"] = RoundUnit(v.Y) };

    private static JsonObject PixelJson(PointF pt) => new() { ["x"] = Math.Round(pt.X, 2), ["y"] = Math.Round(pt.Y, 2) };

    private static string PivotText(Pivot v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.###}, {v.Y:0.###})");

    private static string PixelText(PointF pt) => string.Create(CultureInfo.InvariantCulture, $"({pt.X:0.##}, {pt.Y:0.##})");
}
