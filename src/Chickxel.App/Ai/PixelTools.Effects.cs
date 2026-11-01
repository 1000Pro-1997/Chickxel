using System.Text.Json;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>
/// 레이어 효과 도구: 종류·설정 보기, 넣기·고치기·빼기, 영구 적용, 효과에 맞춰 캔버스 늘리기. 설정은 프로젝트 파일과 같은 JSON(<see cref="EffectSettings"/>)이라
/// 모든 효과 종류를 같은 방식으로 다룬다. 바꾸는 호출은 효과 창과 같은 프로젝트 메서드를 바깥 편집으로 감싸 실행취소 한 단계("AI: 도구이름")를 남긴다.
/// 잠긴 레이어(잠긴 그룹의 부위 포함)의 효과는 고치지 않는다.
/// </summary>
public sealed partial class PixelTools
{
    private const string FrameRangeNote = "Omit from_frame and to_frame to cover the whole animation (also frames added later).";

    private static IEnumerable<JsonObject> EffectToolDefinitions()
    {
        yield return Tool("list_effect_types",
            "List the layer effects you can add with add_effect. Layer effects are non-destructive: they never change pixels, are drawn on top of a " +
            "layer's (or a group's composite) image in stack order, and can be changed, disabled or removed at any time. The effects are the script effects " +
            "installed in the user's asset catalog: the official Chickxel effects (installed by default; ids like \"outline\", \"shadow\", \"rimlight\", " +
            "\"coloroverlay\", \"innerglow\", \"paletteswap\", \"coloradjust\", \"reflection\", \"explode\", \"shine\", \"squash\", \"hitflash\", \"blink\", " +
            "\"shake\", \"bob\", \"appear\", \"fade\", \"melt\", \"rotate\", \"afterimage\", \"colorcycle\", \"shockwave\", \"hologram\"), effects from other " +
            "enabled packages and the user's own script effects (ids = asset ids), plus app effects without a script (e.g. text). " +
            "Returns every effect id with its display name and description (user's UI language), its source, whether it animates over its frame range, " +
            "whether the user allowed its script to run, its settings with default values (keys and value formats of the .chickxel file), allowed values of " +
            "choice settings, [min, max] of clamped numbers, and the user's effect recipes (presets: the user's personal recipes and recipes of enabled installed " +
            "asset packages; each is a stack of effects with order and clipping). Works without an open project.",
            new JsonObject());
        yield return Tool("get_effects",
            "List the effects of a layer or group in stack order (index 0 is applied first): index, type, name, enabled, frames (\"all\" or {from,to} " +
            "frame indices, inclusive), clipToAbove (applies only to the pixels changed by the nearest non-clipped effect above it, i.e. with a lower index) " +
            "and the full settings.",
            new JsonObject { ["layer"] = Int("Layer index (normal, part or group). Default: current.", 0) });
        yield return Tool("add_effect",
            "Add a layer effect (or an effect recipe stack) to a normal, part or group layer. Non-destructive; one undo step. " +
            "Pass either `type` (an effect id from list_effect_types; the old built-in type ids such as \"chickxel.effect.outline\" also work and add the " +
            "official script effect) with optional partial `settings` (only the keys you want to change; the rest keep the effect's defaults from list_effect_types), " +
            "or `preset` (an effect recipe name or id from list_effect_types; its effect values are copied into the layer). Animated effects play across their frame range, so choose from_frame/to_frame " +
            "for the moment it should happen (e.g. a hit flash on frames 3-4). " + FrameRangeNote + " Returns the new effect indices and the layer's effects.",
            new JsonObject
            {
                ["layer"] = Int("Layer index (normal, part or group). Default: current.", 0),
                ["type"] = Str("Effect id from list_effect_types (e.g. \"outline\")."),
                ["preset"] = Str("Effect recipe name or id from list_effect_types (instead of type)."),
                ["settings"] = EffectSettingsProp(),
                ["from_frame"] = Int("First frame index of the effect's range (default 0).", 0),
                ["to_frame"] = Int("Last frame index of the effect's range, inclusive (default: last frame).", 0),
                ["enabled"] = Bool("Draw the effect (default true)."),
                ["clip_to_above"] = Bool("Apply only to the pixels changed by the nearest non-clipped effect above (lower index); not allowed at index 0 (default false)."),
                ["index"] = Int("Stack position to insert at (0 = applied first; default: end).", 0),
            });
        yield return Tool("set_effect",
            "Change one effect of a layer: partial `settings` (only the keys to change; null resets a key to its default), frame range, enabled, " +
            "clip_to_above, and/or its stack position (move_to). One undo step.",
            new JsonObject
            {
                ["layer"] = Int("Layer index. Default: current.", 0),
                ["effect"] = Int("Effect index (see get_effects).", 0),
                ["settings"] = EffectSettingsProp(),
                ["from_frame"] = Int("New first frame index of the range (default: keep).", 0),
                ["to_frame"] = Int("New last frame index of the range, inclusive (default: keep).", 0),
                ["all_frames"] = Bool("true = apply to the whole animation (also frames added later); do not combine with from_frame/to_frame."),
                ["enabled"] = Bool("Turn the effect on or off (off keeps it in the stack without drawing)."),
                ["clip_to_above"] = Bool("Clip to the nearest non-clipped effect above (lower index)."),
                ["move_to"] = Int("New stack index (counted after taking the effect out). Effects that were clipped to it, or to the effect now at the target, stop clipping.", 0),
            },
            "effect");
        yield return Tool("remove_effect",
            "Remove one effect from a layer (effects clipped to it stop clipping). One undo step.",
            new JsonObject
            {
                ["layer"] = Int("Layer index. Default: current.", 0),
                ["effect"] = Int("Effect index (see get_effects).", 0),
            },
            "effect");
        yield return Tool("apply_effects",
            "Bake effects permanently into the pixels of a normal or part layer (destructive, like the editor's \"apply effects permanently\"): every stored " +
            "cel in the effects' frame range is replaced by its look with the effects, and the baked effects are removed from the stack. Enabled effects above " +
            "(lower index than) a chosen effect are baked too, and clip groups are baked whole. A part grows to fit what spills outside it. Groups cannot be baked. " +
            "Only do this when the user asks for it; effects are normally kept non-destructive. One undo step.",
            new JsonObject
            {
                ["layer"] = Int("Normal or part layer index. Default: current.", 0),
                ["effects"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 },
                    ["description"] = "Effect indices to bake (default: every enabled effect).",
                },
            });
        yield return Tool("expand_canvas_for_effects",
            "Enlarge the canvas so effect results that spill outside it (outer outlines, shadows, reflections, shake, explode, ...) are not cut off in any frame, " +
            "like the effects dialog's \"expand canvas to fit effects\". The drawing keeps its size; only the canvas grows. Refused while any layer is locked. One undo step.",
            new JsonObject
            {
                ["layer"] = Int("Only fit this layer's effects (default: every visible layer with effects).", 0),
                ["mode"] = Enum("How to grow (default keep_center): minimum = only the needed sides; keep_ratio = keep the aspect ratio; keep_center = same margin on every side.",
                    "minimum", "keep_ratio", "keep_center"),
            });
    }

    private static JsonObject EffectSettingsProp() => new()
    {
        ["type"] = "object",
        ["description"] = "Partial effect settings: keys and value formats as listed by list_effect_types (e.g. {\"color\": \"#FFFFFF\", \"count\": 2}). " +
                          "Colors are \"#RRGGBB\" or \"#RRGGBBAA\"; choice settings take one of the listed names.",
    };

    // ───────────── 읽기 ─────────────

    /// <summary>
    /// AI가 넣을 수 있는 효과 하나: 도구 ID(공식 효과는 짧은 ID <c>outline</c>, 다른 스크립트는 에셋 ID, 앱 효과는 종류 ID), 화면 이름·설명, 출처,
    /// 기본 설정 효과, 넣을 때 스크립트 원문을 함께 복사할 레시피(앱 효과는 null), 움직이는지, 실행을 허락했는지.
    /// </summary>
    private sealed record EffectChoice(string Id, string Name, string? Description, string Source, LayerEffect Effect, EffectRecipe? Template,
        bool Animated, bool Allowed);

    /// <summary>넣을 수 있는 효과(효과 창 효과 탭과 같은 차례): 켜진 공식 효과, 고를 수 있는 앱 효과(글씨), 다른 패키지·개인 스크립트 효과.</summary>
    private static List<EffectChoice> EffectChoices()
    {
        var culture = global::System.Globalization.CultureInfo.CurrentUICulture;
        var result = new List<EffectChoice>();
        var scripts = AppAssets.Catalog.EffectScripts;
        int officialCount = scripts.TakeWhile(entry => AssetCatalog.OfficialIdOf(entry) is not null).Count();
        void AddScript(AssetEntry entry)
        {
            var asset = entry.Script!;
            var template = asset.ToRecipe();
            var official = AssetCatalog.OfficialIdOf(entry);
            result.Add(new EffectChoice(official ?? entry.Id, entry.DisplayName, asset.DescriptionFor(culture), official is not null ? "official" : entry.PackageId ?? "personal",
                template.Effects[0], template, asset.Animated, ScriptLibrary.TrustOf(asset.Hash) == ScriptTrust.Trusted));
        }
        foreach (var entry in scripts.Take(officialCount)) AddScript(entry);
        foreach (var definition in EffectRegistry.Selectable)
        {
            var ui = LayerEffectsDialog.UiOf(definition);
            result.Add(new EffectChoice(definition.TypeId, ui.Name, ui.Description, "app", definition.Create(), null, definition.Animated, true));
        }
        foreach (var entry in scripts.Skip(officialCount)) AddScript(entry);
        return result;
    }

    /// <summary>
    /// 도구 인자의 효과: 목록 ID(대소문자 무시), 공식 효과의 다른 이름(예전 기본 효과 종류 ID·짧은 ID, 공식 패키지·에셋 ID — 공식 패키지를 껐거나 지웠으면
    /// 앱에 든 원문으로), 화면 이름 차례로 찾는다. 없으면 null.
    /// </summary>
    private static EffectChoice? ResolveEffect(IReadOnlyList<EffectChoice> choices, string name)
    {
        var key = name.Trim();
        if (choices.FirstOrDefault(c => c.Id.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } exact) return exact;
        if (OfficialEffects.Resolve(key) is { } official)
        {
            if (choices.FirstOrDefault(c => c.Id == official) is { } installed) return installed;
            var program = OfficialEffects.Program(official);
            var template = ScriptAsset.From(OfficialEffectPackages.AssetId(official), program).ToRecipe();
            return new EffectChoice(official, program.NameFor(global::System.Globalization.CultureInfo.CurrentUICulture),
                program.DescriptionFor(global::System.Globalization.CultureInfo.CurrentUICulture), "official", template.Effects[0], template, program.Animated, true);
        }
        return choices.FirstOrDefault(c => c.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 도구 결과에 보일 효과 ID: 공식 스크립트 효과는 짧은 ID, 카탈로그에 있는 스크립트는 그 에셋 ID, 다른 스크립트·C# 효과는 종류 ID(모르는 효과는 원본의 ID).
    /// </summary>
    private static string EffectIdOf(LayerEffect effect)
        => effect is ScriptEffect script
            ? OfficialEffects.IdOfHash(script.ScriptHash)
              ?? AppAssets.Catalog.EffectScripts.FirstOrDefault(entry => entry.Script!.Hash == script.ScriptHash)?.Id
              ?? EffectRegistry.TypeIdOf(effect)
            : EffectRegistry.TypeIdOf(effect);

    private JsonObject ListEffectTypes()
    {
        var types = new JsonArray();
        foreach (var choice in EffectChoices())
        {
            var settings = new JsonObject();
            var choices = new JsonObject();
            var ranges = new JsonObject();
            var parameters = EffectRegistry.ParametersOf(choice.Effect, null, out bool complete);
            if (complete)
                foreach (var p in parameters)
                {
                    settings[p.Key] = p.DefaultValue;
                    if (p.Choices is { } names) choices[p.Key] = Strings(names.ToArray());
                    if (p.Min is not null || p.Max is not null) ranges[p.Key] = new JsonArray(JsonValue.Create(p.Min), JsonValue.Create(p.Max));
                }
            else if (choice.Effect is ScriptEffect script)
                settings = script.Settings.ToJson(); // 실행을 허락하지 않은 스크립트: 선언의 기본값만
            var o = new JsonObject
            {
                ["type"] = choice.Id,
                ["name"] = choice.Name,
                ["source"] = choice.Source,
                ["animated"] = choice.Animated,
                ["settings"] = settings,
            };
            if (choice.Description is { Length: > 0 } description) o["description"] = description;
            if (!choice.Allowed) o["allowed"] = false;
            if (choices.Count > 0) o["choices"] = choices;
            if (ranges.Count > 0) o["ranges"] = ranges;
            types.Add(o);
        }
        var presets = new JsonArray();
        var saved = LoadEffectRecipes(out var error);
        foreach (var entry in saved)
        {
            var recipe = entry.Recipe!;
            presets.Add(new JsonObject
            {
                ["name"] = recipe.Name,
                ["id"] = entry.Id,
                ["source"] = entry.PackageId ?? "personal",
                ["frameRange"] = recipe.FrameRange == RecipeFrameRange.AllFrames ? "allFrames" : "addRange",
                ["effects"] = Strings(recipe.Effects.Select(EffectIdOf).ToArray()),
            });
        }
        var doc = new JsonObject
        {
            ["types"] = types,
            ["presets"] = presets,
            ["notes"] = "Effects render in stack order (index 0 first) and never change pixels. Pass only the settings you want to change (null resets one). " +
                        "Color list settings = [\"#RRGGBB\", ...]; color pair settings = [[\"#from\", \"#to\"], ...]. Every effect also accepts colorGradients " +
                        "(advanced: per color key gradient objects in the .chickxel format). Animated effects run once over their frame range; easing, repeat and repeatCount change that. " +
                        "Effects with \"allowed\": false are scripts the user has not allowed to run yet: they can be added but are not drawn until the user allows them " +
                        "in the effects window, and only their declared default settings are known.",
        };
        if (error is not null) doc["presetsError"] = error;
        return TextResult(doc.ToJsonString(JsonOptions));
    }

    private JsonObject GetEffects(JsonObject? a)
    {
        var p = RequireProject();
        return TextResult(EffectsJson(p, LayerIndex(a)).ToJsonString(JsonOptions));
    }

    /// <summary>레이어의 효과 목록 JSON(get_effects와 바꾸는 도구의 결과).</summary>
    private static JsonObject EffectsJson(SpriteProject p, int index)
    {
        var layer = p.Layers[index];
        var effects = new JsonArray();
        for (int i = 0; i < layer.Effects.Count; i++)
        {
            var e = layer.Effects[i];
            effects.Add(new JsonObject
            {
                ["index"] = i,
                ["type"] = EffectIdOf(e),
                ["name"] = e.DisplayName,
                ["enabled"] = e.Enabled,
                ["frames"] = e.Frames is { } r ? new JsonObject { ["from"] = r.From, ["to"] = r.To } : (JsonNode)"all",
                ["clipToAbove"] = e.ClipToPrevious,
                ["settings"] = EffectSettings.ToJson(e),
            });
        }
        return new JsonObject
        {
            ["layer"] = index, ["name"] = layer.Name, ["kind"] = KindName(layer.Kind), ["locked"] = p.IsLayerLocked(index),
            ["frameCount"] = p.Frames.Count, ["effects"] = effects,
        };
    }

    // ───────────── 바꾸기 ─────────────

    private JsonObject AddEffect(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int index = LayerIndex(a);
        var layer = p.Layers[index];
        RequireUnlocked(p, index);
        var type = OptString(a, "type")?.Trim();
        var presetName = OptString(a, "preset")?.Trim();
        if (string.IsNullOrEmpty(type) == string.IsNullOrEmpty(presetName)) throw new ToolError(UiStrings.Ai_Effect_TypeOrPreset);
        var settings = SettingsArg(a);
        var range = NewRange(a, p);
        bool enabled = OptBool(a, "enabled") ?? true;
        bool clip = OptBool(a, "clip_to_above") ?? false;
        int at = OptInt(a, "index", 0, layer.Effects.Count) ?? layer.Effects.Count;
        if (clip && at == 0) throw new ToolError(UiStrings.Ai_Effect_ClipFirst);

        IReadOnlyList<LayerEffect> added;
        IReadOnlyList<string> clamped = [];
        if (!string.IsNullOrEmpty(type))
        {
            var choices = EffectChoices();
            var choice = ResolveEffect(choices, type)
                         ?? throw new ToolError(UiStrings.FormatAi_Effect_UnknownType(type: type, types: string.Join(", ", choices.Select(c => c.Id))));
            // 스크립트 효과는 레시피로 넣어 원문을 프로젝트 스크립트 표에 복사한다(효과 창 상자와 같음)
            var created = choice.Template is { } template ? template.Instantiate(p, range)[0] : choice.Effect;
            var (effect, adjusted) = MergeSettings(created, settings);
            clamped = adjusted;
            added = [effect with { Frames = range, Enabled = enabled, ClipToPrevious = clip }];
        }
        else
        {
            if (settings is not null) throw new ToolError(UiStrings.Ai_Effect_PresetSettings);
            var presets = LoadEffectRecipes(out var error);
            var preset = (presets.FirstOrDefault(x => x.Id == presetName)
                          ?? presets.FirstOrDefault(x => x.Name == presetName)
                          ?? presets.FirstOrDefault(x => x.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase)))?.Recipe;
            if (preset is null && error is not null) throw new ToolError(UiStrings.FormatAi_Effect_PresetLoad(error: error));
            if (preset is null)
                throw new ToolError(presets.Count == 0
                    ? UiStrings.Ai_Effect_NoPresets
                    : UiStrings.FormatAi_Effect_UnknownPreset(name: presetName, presets: string.Join(", ", presets.Select(x => $"\"{x.Name}\""))));
            added = preset.Instantiate(p, range).Select((effect, i) => effect with
            {
                Enabled = enabled,
                ClipToPrevious = i == 0 ? clip : effect.ClipToPrevious,
            }).ToArray();
        }

        var list = layer.Effects.ToList();
        list.InsertRange(at, added);
        p.Edit(Label(tool), ProjectChange.Layers, () => p.SetLayerEffects(index, list));
        Ctx.SetCurrentLayer(index);
        var indices = added.Count == 1 ? $"{at}" : $"{at}-{at + added.Count - 1}";
        var what = string.Join(", ", added.Select(EffectIdOf));
        return EffectResult(UiStrings.FormatAi_Effect_Added(tool: tool, effects: what, layer: index, name: layer.Name, indices: indices) + ClampedNote(clamped), p, index);
    }

    private JsonObject SetEffect(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int index = LayerIndex(a);
        var layer = p.Layers[index];
        int e = EffectIndex(a, p, index);
        var settings = SettingsArg(a);
        bool? enabled = OptBool(a, "enabled");
        bool? clip = OptBool(a, "clip_to_above");
        int? moveTo = OptInt(a, "move_to", 0, layer.Effects.Count - 1);
        var effect = layer.Effects[e];
        var (rangeGiven, range) = ChangedRange(a, p, effect.Frames);
        if (settings is null && !rangeGiven && enabled is null && clip is null && moveTo is null)
            throw new ToolError(UiStrings.Ai_Effect_NothingToChange);
        RequireUnlocked(p, index);

        var (changed, clamped) = MergeSettings(effect, settings);
        if (rangeGiven) changed = changed with { Frames = range };
        if (enabled is { } on) changed = changed with { Enabled = on };
        var list = layer.Effects.ToList();
        list[e] = changed;
        int position = e;
        if (moveTo is { } to && to != e)
        {
            list = EffectStack.Move(list, e, to);
            position = to;
        }
        if (clip is { } c)
        {
            if (c && position == 0) throw new ToolError(UiStrings.Ai_Effect_ClipFirst);
            list[position] = list[position] with { ClipToPrevious = c };
        }
        if (list.SequenceEqual(layer.Effects))
        {
            Ctx.SetCurrentLayer(index);
            return EffectResult(UiStrings.FormatAi_Effect_Unchanged(tool: tool, effect: e, layer: index) + ClampedNote(clamped), p, index);
        }
        p.Edit(Label(tool), ProjectChange.Layers, () => p.SetLayerEffects(index, list));
        Ctx.SetCurrentLayer(index);
        return EffectResult(UiStrings.FormatAi_Effect_Changed(tool: tool, effect: position, type: EffectIdOf(changed), layer: index, name: layer.Name)
                            + ClampedNote(clamped), p, index);
    }

    private JsonObject RemoveEffect(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int index = LayerIndex(a);
        var layer = p.Layers[index];
        int e = EffectIndex(a, p, index);
        RequireUnlocked(p, index);
        var removed = layer.Effects[e];
        var list = EffectStack.Remove(layer.Effects, new HashSet<int> { e });
        p.Edit(Label(tool), ProjectChange.Layers, () => p.SetLayerEffects(index, list));
        Ctx.SetCurrentLayer(index);
        return EffectResult(UiStrings.FormatAi_Effect_Removed(tool: tool, effect: e, type: EffectIdOf(removed), layer: index, name: layer.Name), p, index);
    }

    private JsonObject ApplyEffects(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int index = LayerIndex(a);
        var layer = p.Layers[index];
        RequireUnlocked(p, index);
        if (LayerEffectsDialog.CanBake(p, layer) is { Ok: false, Why: { } why }) throw new ToolError(why);
        if (layer.Effects.Count == 0) throw new ToolError(UiStrings.FormatAi_Effect_NoEffects(layer: index, name: layer.Name));
        List<int> chosen;
        if (Arg(a, "effects") is { } node)
        {
            var nodes = node as JsonArray ?? throw new ToolError(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_PixelTools_ReqArray_C2A791FDD4, "effects"));
            chosen = [];
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is not { } n || !TryInt(n, out int v)) throw new ToolError(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_PixelTools_OptIndex_D858DEC2E1, $"effects[{i}]", nodes[i] is { } shown ? Show(shown) : "null"));
                if (v < 0 || v >= layer.Effects.Count)
                    throw new ToolError(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_PixelTools_OptIndex_4F2BADBB8B, $"effects[{i}]", v, UiStrings.Ai_Effect_What, layer.Effects.Count - 1));
                chosen.Add(v);
            }
        }
        else chosen = Enumerable.Range(0, layer.Effects.Count).Where(i => layer.Effects[i].Enabled).ToList();
        if (chosen.Count == 0) throw new ToolError(UiStrings.FormatAi_Effect_NoneToBake(layer: index, name: layer.Name));
        // 부위가 넓어지면 자동 배치 그룹은 다시 배치된다
        if (layer.IsAutoLaidOut) RequireLayoutKeepsLocked(p, layer.Parent!);
        var result = p.Edit(Label(tool), ProjectChange.Layers | ProjectChange.Pixels, () => p.BakeLayerEffects(index, chosen));
        Ctx.SetCurrentLayer(p.IndexOf(layer));
        return EffectResult($"{tool}: " + LayerEffectsDialog.BakeSummary(result, layer), p, p.IndexOf(layer));
    }

    private JsonObject ExpandCanvasForEffects(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int? only = OptLayer(a);
        var mode = OptEnum(a, "mode", "minimum", "keep_ratio", "keep_center") switch
        {
            "minimum" => EffectCanvasExpandMode.Minimum,
            "keep_ratio" => EffectCanvasExpandMode.KeepRatio,
            _ => EffectCanvasExpandMode.KeepCenter,
        };
        List<Layer> targets = only is { } l ? [p.Layers[l]] : p.Layers.Where(x => x.Visible && x.Effects.Count > 0).ToList();
        int width = p.Width, height = p.Height;
        var bounds = new Rectangle(0, 0, width, height);
        bool any = false;
        foreach (var layer in targets)
        {
            int ox = layer.IsPart ? layer.PartOffset.X : 0, oy = layer.IsPart ? layer.PartOffset.Y : 0;
            for (int frame = 0; frame < p.Frames.Count; frame++)
            {
                if (p.EffectSource(layer, frame) is not { } source) continue;
                var effects = p.EffectsAt(layer, frame);
                if (!LayerEffects.AnyEnabled(effects)) continue;
                any = true;
                var (rendered, dx, dy) = LayerEffects.Render(source, effects);
                var opaque = rendered.GetOpaqueBounds();
                if (opaque.IsEmpty) continue;
                opaque.Offset(ox + dx, oy + dy);
                bounds = Rectangle.Union(bounds, opaque);
            }
        }
        if (!any) return TextResult(UiStrings.FormatAi_Effect_ExpandNoEffects(tool: tool));
        var plan = EffectCanvasExpansion.FromBounds(width, height, bounds, mode);
        if (plan.IsNone(width, height)) return TextResult(UiStrings.FormatAi_Effect_ExpandNone(tool: tool, width: width, height: height));
        var locked = p.Layers.Where((_, i) => p.IsLayerLocked(i)).ToList();
        if (locked.Count > 0) throw new ToolError(UiStrings.FormatAi_Locked_ResizeCanvas(layers: LayerList(p, locked)));
        p.Edit(Label(tool), ProjectChange.Canvas | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Layers | ProjectChange.Settings,
            () => p.ResizeCanvasAndScale(plan.Width, plan.Height, new Rectangle(plan.OffsetX, plan.OffsetY, width, height)));
        return TextResult(UiStrings.FormatAi_Effect_Expanded(tool: tool, oldWidth: width, oldHeight: height, width: plan.Width, height: plan.Height,
            x: plan.OffsetX, y: plan.OffsetY));
    }

    // ───────────── 인자·결과 ─────────────

    /// <summary>
    /// 활성 카탈로그의 효과 레시피(효과 창 프리셋 탭과 같은 목록 — 개인 에셋과 켜진 패키지). 개인 에셋을 읽지 못했으면 그 까닭도 준다
    /// (설치 패키지의 레시피는 그대로 쓸 수 있음).
    /// </summary>
    private static IReadOnlyList<Chickxel.Core.Assets.AssetEntry> LoadEffectRecipes(out string? error)
    {
        var store = AppAssets.Store;
        error = store.PersonalError;
        return store.Catalog.Recipes;
    }

    /// <summary>효과 번호 인자(<c>effect</c>, 필수). 효과가 없는 레이어면 도구 오류.</summary>
    private static int EffectIndex(JsonObject? a, SpriteProject p, int layer)
    {
        int count = p.Layers[layer].Effects.Count;
        if (count == 0) throw new ToolError(UiStrings.FormatAi_Effect_NoEffects(layer: layer, name: p.Layers[layer].Name));
        return ReqIndex(a, "effect", count, UiStrings.Ai_Effect_What);
    }

    /// <summary><c>settings</c> 인자: 객체(모델이 JSON 글로 보낸 객체도 받음). 없으면 null.</summary>
    private static JsonObject? SettingsArg(JsonObject? a)
    {
        switch (Arg(a, "settings"))
        {
            case null:
                return null;
            case JsonObject o:
                return o;
            case JsonValue v when v.TryGetValue(out string? text):
                try
                {
                    if (JsonNode.Parse(text) is JsonObject parsed) return parsed;
                }
                catch (JsonException)
                {
                }
                break;
        }
        throw new ToolError(UiStrings.Ai_Effect_SettingsObject);
    }

    /// <summary>새 효과의 프레임 범위: from_frame·to_frame(빠진 쪽은 처음·끝), 둘 다 없거나 전체를 덮으면 null(모든 프레임).</summary>
    private static FrameRange? NewRange(JsonObject? a, SpriteProject p)
    {
        int n = p.Frames.Count;
        int? from = OptIndex(a, "from_frame", n, UiStrings.S_PixelTools_Frame_CAED4A473B);
        int? to = OptIndex(a, "to_frame", n, UiStrings.S_PixelTools_Frame_CAED4A473B);
        if (from is null && to is null) return null;
        return CheckedRange(from ?? 0, to ?? n - 1, n);
    }

    /// <summary>고칠 프레임 범위: all_frames 또는 from_frame·to_frame(빠진 쪽은 지금 범위). 주지 않았으면 (false, 지금 범위).</summary>
    private static (bool Given, FrameRange? Range) ChangedRange(JsonObject? a, SpriteProject p, FrameRange? current)
    {
        int n = p.Frames.Count;
        bool all = OptBool(a, "all_frames") ?? false;
        int? from = OptIndex(a, "from_frame", n, UiStrings.S_PixelTools_Frame_CAED4A473B);
        int? to = OptIndex(a, "to_frame", n, UiStrings.S_PixelTools_Frame_CAED4A473B);
        if (all && (from is not null || to is not null)) throw new ToolError(UiStrings.Ai_Effect_RangeConflict);
        if (all) return (true, null);
        if (from is null && to is null) return (false, current);
        return (true, CheckedRange(from ?? current?.From ?? 0, to ?? current?.To ?? n - 1, n));
    }

    private static FrameRange? CheckedRange(int from, int to, int frameCount)
    {
        if (from > to) throw new ToolError(UiStrings.FormatAi_Effect_FrameOrder(from: from, to: to));
        return FrameRange.Of(from, to, frameCount);
    }

    /// <summary>설정 덮기(<see cref="EffectSettings.Merge"/>) — 잘못된 키·값은 화면 언어 도구 오류로.</summary>
    private static (LayerEffect Effect, IReadOnlyList<string> Clamped) MergeSettings(LayerEffect effect, JsonObject? settings)
    {
        try
        {
            return EffectSettings.Merge(effect, settings);
        }
        catch (EffectSettingException e)
        {
            var key = e.Key;
            throw new ToolError(e.Error switch
            {
                EffectSettingError.UnknownKey => UiStrings.FormatAi_Effect_SettingUnknown(key: key, type: EffectIdOf(effect), keys: e.Detail),
                EffectSettingError.ReservedKey => UiStrings.FormatAi_Effect_SettingReserved(key: key),
                EffectSettingError.WrongType => UiStrings.FormatAi_Effect_SettingWrongType(key: key, example: SettingExample(effect, key)),
                EffectSettingError.NotAChoice => UiStrings.FormatAi_Effect_SettingChoice(key: key, choices: e.Detail),
                EffectSettingError.InvalidColor => UiStrings.FormatAi_Effect_SettingColor(key: key, value: e.Detail),
                EffectSettingError.InvalidEntries => UiStrings.FormatAi_Effect_SettingEntries(key: key, count: e.Detail),
                _ => UiStrings.FormatAi_Effect_SettingInvalid(key: key, reason: e.Detail),
            });
        }
    }

    /// <summary>오류 문구에 보일 설정 값 예(그 종류의 기본값).</summary>
    private static string SettingExample(LayerEffect effect, string key)
    {
        var setting = EffectSettings.Describe(effect).FirstOrDefault(s => s.Key == key);
        return setting?.DefaultValue.ToJsonString(JsonOptions) ?? "{}";
    }

    private static string ClampedNote(IReadOnlyList<string> clamped)
        => clamped.Count == 0 ? "" : UiStrings.FormatAi_Effect_Clamped(values: string.Join(", ", clamped));

    /// <summary>결과 한 줄 + 그 레이어의 효과 목록 JSON.</summary>
    private static JsonObject EffectResult(string text, SpriteProject p, int layer)
        => TextResult(text + "\n" + EffectsJson(p, layer).ToJsonString(JsonOptions));
}
