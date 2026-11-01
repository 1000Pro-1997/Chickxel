using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.App.Ai;

/// <summary>
/// 효과 만들기 도구(설계 7장): 스크립트 효과 안내(규칙·API), 스크립트 효과 쓰기(해석 오류는 줄·칸으로 돌려주고 아무것도 바꾸지 않음), 효과 미리보기 그림,
/// 레이어의 스크립트 효과를 개인 스크립트 에셋으로 저장. 올리기 도구는 두지 않는다 — 올리기는 사람이 효과 만들기 창에서 누른다(설계 원칙 4).
/// 기본 효과를 합친 레시피는 저장·올리기 대상이 아니다(효과 만들기는 스크립트만).
/// 효과 만들기 창이 열려 있으면 그 편집기의 원문을 읽고(<c>get_effect_maker_source</c>) 바꾸고(<c>set_effect_maker_source</c>) 검증한다(<c>check_effect_maker</c>) —
/// 문서는 건드리지 않으므로 "지금 그림만" 범위와 상관없이 쓸 수 있다(효과 만들기 AI 칸이 주로 쓰고, 채팅 창도 쓸 수 있음).
/// AI가 쓴 원문은 이 컴퓨터에서 쓴 원문으로 보고 실행을 허락한다(실행은 다른 스크립트처럼 Jint 샌드박스·한도 안).
/// </summary>
public sealed partial class PixelTools
{
    /// <summary>미리보기 그림에 넣는 칸 수(효과 범위를 고르게 나눠서).</summary>
    private const int PreviewStripFrames = 5;
    private const string CheckMakerTool = "check_effect_maker";

    private static IEnumerable<JsonObject> EffectMakerToolDefinitions()
    {
        yield return Tool("get_script_api",
            "Read the guide for writing a script effect (JavaScript): the rules every effect must follow (one effect({...}) call, render changes img.data in place, " +
            "deterministic, run limits, setting types, colors that follow gradients, fast path, answer format), then the API (declaration, img/ctx/s objects, helpers) and an example. " +
            "Read it before writing a script with write_script_effect or set_effect_maker_source. The installed effects (list_effect_types: the official effects and any packages) cover most needs; write a script only for looks they can't make.",
            new JsonObject());
        yield return Tool("write_script_effect",
            "Write a script effect (JavaScript source calling effect({...}) once) and add it to a layer, or replace the source of an existing script effect. " +
            "If the source has syntax or declaration errors nothing changes and the errors come back with line and column; fix and call again. " +
            "On success the result shows the effect on the preview chick (several frames for animated effects), any run error and gradient hints. One undo step. " +
            DeclarationNote + GradientNote + FrameRangeNote,
            new JsonObject
            {
                ["source"] = Str("Full JavaScript source."),
                ["layer"] = Int("Layer index (normal, part or group). Default: current.", 0),
                ["effect"] = Int("Index of an existing script effect on that layer to replace (keeps its frame range and matching setting values). Omit to add a new effect.", 0),
                ["settings"] = new JsonObject { ["type"] = "object", ["description"] = "Setting values by key (as declared in settings; colors \"#RRGGBB\"). Omitted keys use the declared defaults." },
                ["from_frame"] = Int("First frame of a new effect (0-based).", 0),
                ["to_frame"] = Int("Last frame of a new effect (inclusive).", 0),
            },
            "source");
        yield return Tool("preview_effects",
            "Look at effects: renders the effect list of a layer (or a saved effect recipe by name) on the preview chick or on the layer's own frames, " +
            $"as up to {PreviewStripFrames} frames side by side spread over the animation. Doesn't change anything.",
            new JsonObject
            {
                ["layer"] = Int("Layer index whose effects to preview. Default: current.", 0),
                ["recipe"] = Str("Saved effect recipe name or id instead of a layer's effects (see list_effect_types presets)."),
                ["on"] = Enum("Draw on the preview chick (default; same as the published preview) or on the layer's own image.", "chick", "layer"),
                ["chick"] = Str("Preview chick id when on = chick (default \"original\"; e.g. knight, wizard, ninja)."),
            });
        yield return Tool("save_script_effect",
            "Save a script effect of a layer as a personal script effect asset (its name, settings and preview chick come from the source's effect({...}) declaration) " +
            "and open it in the Effect Maker window. Publishing online is done by the user from that window (there is no upload tool).",
            new JsonObject
            {
                ["layer"] = Int("Layer index. Default: current.", 0),
                ["effect"] = Int("Index of the script effect on that layer. Default: the last script effect.", 0),
                ["chick"] = Str("Preview chick id to write into the declaration's previewChick before saving (optional; e.g. knight, wizard, ninja)."),
            });
        yield return Tool("get_effect_maker_source",
            "Read the script open in the Effect Maker window: its full source, unsaved flag, parse errors (line and column), the declared name, language, description, tags, previewChick, previewFrames and animated, " +
            "the execution path (fast path, or Jint with the reason and line), the preview setting values and the preview scene: the layers drawn around the preview chick (names bottom to top and which layers the chick sits between), " +
            "which the user can pick in the effect's layer settings (type: \"layer\") and ctx.layer/layerWithEffects/layerEffects read in the preview, Check and the upload preview. Fails if the Effect Maker is closed or has no script open.",
            new JsonObject());
        yield return Tool("set_effect_maker_source",
            "Replace the source in the Effect Maker editor with a complete JavaScript file (one undo step in the editor; the user's document is not changed). " +
            "If no script is open, it opens as a new unsaved script. Returns parse errors with line and column (the source is still put in the editor so the user sees it), " +
            "or the effect on the preview chick (several frames for animated effects), any run error, gradient hints and the execution path. Fails if the Effect Maker is closed. " +
            DeclarationNote + GradientNote,
            new JsonObject { ["source"] = Str("Full JavaScript source calling effect({...}) once.") },
            "source");
        yield return Tool(CheckMakerTool,
            "Run the Effect Maker's Validate check on its current source (same as the Validate button): the install/source check, the name, and every preview chick at every " +
            $"preview frame (the declared previewFrames, default {AssetPreview.PackageFrames}) with the declared default settings. Returns each problem (where, line and column) or the number of runs and the slowest frame, " +
            "plus the execution path and gradient hints: color settings that stay one flat color when the user picks a gradient (it renders each color setting with a sample gradient and compares). " +
            "Gradient hints are recommendations, not problems; follow them unless the effect really needs one flat color. Fails if the Effect Maker is closed or has no script open.",
            new JsonObject());
        yield return Tool("list_script_effects",
            "List the user's script effects: personal script effects and the script effects of installed asset packages (id, name, description, origin personal/package, " +
            "package name, and whether it is the one open in the Effect Maker). Read their code with get_script_effect_source, for example when the user says \"like the X effect but ...\". Doesn't change anything.",
            new JsonObject());
        yield return Tool("get_script_effect_source",
            "Read one of the user's script effects (see list_script_effects) as a reference: its full source, name, language, description, tags, settings schema and execution path. " +
            "Read-only: to change the effect open in the Effect Maker use set_effect_maker_source.",
            new JsonObject { ["effect"] = Str("Script effect id, or its name (case-insensitive).") },
            "effect");
    }

    /// <summary>스크립트를 쓰는 도구 설명에 붙이는 선언 규칙: 언어·설명·태그를 늘 적고 효과와 맞춘다(<see cref="ScriptEffectGuide"/>의 규칙과 같음).</summary>
    private const string DeclarationNote =
        "Always declare language (the language code of the declared name, description and labels, e.g. \"ko\" or \"en\"), description (one short sentence) " +
        "and tags (3-6 lowercase English tags, e.g. tags: [\"neon\", \"glow\"]), and keep them, translations.en and other translations in sync whenever the effect changes. ";

    /// <summary>스크립트를 쓰는 도구 설명에 붙이는 그라데이션 권장(<see cref="ScriptEffectGuide"/>의 규칙과 같음 — 꼭 지킬 규칙이 아니라 권장).</summary>
    private const string GradientNote =
        "The user can switch any color setting to a gradient, so by default draw each color by sampling it at the pixel you write with ctx.color(\"key\", x, y) " +
        "(lists: ctx.colorAt, pairs: ctx.pairColor; pass bounds like a beam's own rectangle when the source's opaque bounds don't fit the shape) instead of reading s.key once; " +
        "keep a flat s.key color only when the effect really needs one solid color. ";

    private static JsonObject GetScriptApi() => TextResult(ScriptEffectGuide.ToMarkdown(null));

    /// <summary>사용자의 스크립트 효과(개인 에셋 다음 설치한 패키지 순, 꺼진 패키지 포함).</summary>
    private static IEnumerable<(AssetEntry Entry, ScriptAsset Script)> ScriptEffects()
        => AppAssets.Catalog.All.Where(e => e.Kind == AssetKinds.EffectScript && e.Data is ScriptAsset).Select(e => (e, (ScriptAsset)e.Data));

    private static JsonObject ListScriptEffects()
    {
        string? open = EffectMakerWindow.Current?.ShownId;
        var list = new JsonArray();
        foreach (var (entry, script) in ScriptEffects())
        {
            var o = new JsonObject { ["id"] = entry.Id, ["name"] = script.Name };
            if (script.Description is { } description) o["description"] = description;
            o["origin"] = entry.Origin == AssetOrigin.Personal ? "personal" : "package";
            if (entry.Origin != AssetOrigin.Personal) o["package"] = entry.PackageName ?? entry.PackageId;
            if (entry.Id == open) o["open_in_effect_maker"] = true;
            list.Add(o);
        }
        return TextResult(new JsonObject { ["script_effects"] = list }.ToJsonString(JsonOptions));
    }

    private static JsonObject GetScriptEffectSource(JsonObject? a)
    {
        string wanted = (OptString(a, "effect") ?? throw new ToolError(string.Format(CultureInfo.CurrentCulture, UiStrings.S_PixelTools_ReqIndex_6802669E97, "effect"))).Trim();
        var effects = ScriptEffects().ToList();
        // ID가 먼저, 없으면 이름(대소문자 무시 — 같은 이름이 여럿이면 목록의 처음 것)
        var found = effects.FirstOrDefault(x => x.Entry.Id == wanted);
        if (found.Entry is null) found = effects.FirstOrDefault(x => x.Script.Name.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase));
        if (found.Entry is null) throw new ToolError(UiStrings.FormatAi_Script_UnknownEffect(effect: wanted));
        var (entry, script) = found;
        var o = new JsonObject { ["id"] = entry.Id, ["name"] = script.Name };
        if (script.Language is { } language) o["language"] = language;
        if (script.Description is { } description) o["description"] = description;
        o["tags"] = new JsonArray([.. script.Tags.Select(t => (JsonNode?)t)]);
        o["origin"] = entry.Origin == AssetOrigin.Personal ? "personal" : "package";
        if (entry.Origin != AssetOrigin.Personal) o["package"] = entry.PackageName ?? entry.PackageId;
        var settings = new JsonArray();
        foreach (var s in script.Settings)
        {
            var setting = new JsonObject { ["key"] = s.Key, ["label"] = s.Label, ["type"] = s.Type.ToString().ToLowerInvariant() };
            if (s.Min is { } min) setting["min"] = min;
            if (s.Max is { } max) setting["max"] = max;
            if (s.Step is { } step) setting["step"] = step;
            setting["default"] = SettingJson(s.Default);
            if (s.Choices.Count > 0) setting["choices"] = new JsonArray([.. s.Choices.Select(c => (JsonNode)new JsonObject { ["value"] = c.Value, ["label"] = c.Label })]);
            settings.Add(setting);
        }
        o["settings"] = settings;
        var parsed = ScriptEffectProgram.Parse(script.Source);
        if (parsed.Success) o["execution_path"] = PathText(parsed.Program.ExecutionPath);
        o["source"] = script.Source;
        return TextResult(o.ToJsonString(JsonOptions));
    }

    // ───────────── 효과 만들기 창 ─────────────

    /// <summary>열린 효과 만들기(<paramref name="script"/>면 열린 스크립트도 있어야 함). 없으면 도구 오류.</summary>
    private static EffectMakerWindow RequireMaker(bool script)
    {
        var maker = EffectMakerWindow.Current ?? throw new ToolError(UiStrings.Ai_Maker_Closed);
        if (script && !maker.HasScript) throw new ToolError(UiStrings.Ai_Maker_NoScript);
        return maker;
    }

    private static JsonObject GetEffectMakerSource()
    {
        var maker = RequireMaker(script: true);
        var parsed = ScriptEffectProgram.Parse(maker.Source);
        var o = new JsonObject { ["source"] = maker.Source, ["unsaved"] = maker.IsDirty };
        if (!parsed.Success)
        {
            o["parse_errors"] = new JsonArray(parsed.Errors.Select(e => (JsonNode)ErrorLine(e)).ToArray());
            return TextResult(o.ToJsonString(JsonOptions));
        }
        var program = parsed.Program;
        o["name"] = program.Name;
        if (program.Language is { } language) o["language"] = language;
        if (program.Description is { } description) o["description"] = description;
        o["tags"] = new JsonArray([.. program.Tags.Select(t => (JsonNode?)t)]);
        o["preview_chick"] = program.PreviewChick;
        o["preview_frames"] = program.PreviewFrames;
        o["animated"] = program.Animated;
        o["execution_path"] = PathText(program.ExecutionPath);
        var values = new JsonObject();
        foreach (var (key, value) in maker.PreviewValues(program)) values[key] = SettingJson(value);
        o["preview_settings"] = values;
        if (maker.Scene is { } scene) o["preview_scene"] = SceneJson(scene);
        else if (ScriptLibrary.InfoOf(ScriptLibrary.Add(maker.Source)) is { ReadsOtherLayers: true })
            o["preview_scene"] = "none: the preview chick is drawn alone, so ctx.layer, ctx.layerWithEffects and ctx.layerEffects return null / [] in the preview. The user can add a scene with \"Edit in main editor\" in the Effect Maker.";
        return TextResult(o.ToJsonString(JsonOptions));
    }

    /// <summary>미리보기 장면(모델이 읽음 — 영어): 크기·프레임 수, 레이어 이름(아래에서 위로, 병아리 자리 표시)과 병아리 위아래 레이어.</summary>
    private static JsonObject SceneJson(PreviewScene scene)
    {
        const string Chick = "<chick: the layer this effect is on>";
        var o = new JsonObject
        {
            ["width"] = scene.Width,
            ["height"] = scene.Height,
            ["frames"] = scene.FrameCount,
            ["layers_bottom_to_top"] = new JsonArray([.. EffectMakerWindow.SceneLayerNames(scene, Chick).Select(n => (JsonNode?)n)]),
        };
        string below = scene.ChickIndex > 0 ? $"\"{scene.Layers[scene.ChickIndex - 1].Name}\"" : "nothing";
        string above = scene.ChickIndex < scene.Layers.Count ? $"\"{scene.Layers[scene.ChickIndex].Name}\"" : "nothing";
        o["chick_position"] = $"The chick layer sits above {below} and below {above}. A layer setting (type: \"layer\") lists these names in the settings panel; give it the scene layer's exact name as default so ctx.layer(key) finds it in the preview.";
        return o;
    }

    private static JsonObject SetEffectMakerSource(JsonObject? a)
    {
        string source = OptString(a, "source") ?? throw new ToolError(string.Format(CultureInfo.CurrentCulture, UiStrings.S_PixelTools_ReqIndex_6802669E97, "source"));
        var maker = RequireMaker(script: false);
        maker.SetSourceFromAi(source);
        var parsed = ScriptEffectProgram.Parse(maker.Source);
        if (!parsed.Success)
            return TextResult(UiStrings.Ai_Maker_SetErrors + "\n" + string.Join("\n", parsed.Errors.Select(ErrorLine)), isError: true);
        var program = parsed.Program;
        if (maker.PreviewEffects() is not [ScriptEffect effect]) throw new ToolError(UiStrings.Ai_Maker_SetErrors);
        // 미리보기 장면이 있으면 장면 위에서(다른 레이어 함수가 장면 레이어를 읽음)
        var scene = maker.Scene;
        string text = UiStrings.FormatAi_Maker_Set(name: program.Name) + "\n" + PathText(program.ExecutionPath);
        if (RunError(program, effect, program.PreviewChick, scene) is { } error) text += "\n" + UiStrings.FormatAi_Script_RunError(error: ErrorLine(error));
        text += GradientHintText(ScriptGradientCheck.Static(program).Select(h => h.Message));
        return ImageResult(PreviewStrip([effect], PreviewChicks.Load(program.PreviewChick), program.PreviewFrames, scene), text);
    }

    /// <summary>그라데이션 안내 줄들(머리 줄 + 안내마다 한 줄, 앞에 줄바꿈). 없으면 빈 글.</summary>
    private static string GradientHintText(IEnumerable<string> hints)
    {
        var list = hints.ToList();
        return list.Count == 0 ? "" : "\n" + UiStrings.Ai_Script_GradientHints + "\n" + string.Join("\n", list.Select(h => "- " + h));
    }

    /// <summary>효과 만들기의 검증(검증 단추와 같음)을 돌려 결과를 글로. 검증은 작업 스레드에서 돌고 결과는 창의 상태 칸에도 보인다.</summary>
    private static async Task<JsonObject> CheckEffectMakerAsync()
    {
        var maker = RequireMaker(script: true);
        var result = await maker.CheckAsync() ?? throw new ToolError(UiStrings.Ai_Maker_CheckInterrupted);
        var lines = new List<string>();
        if (result.Problems.Count == 0)
            lines.Add(UiStrings.FormatAi_Maker_CheckOk(runs: result.Runs, ms: result.SlowestMs.ToString("0.00", CultureInfo.InvariantCulture), chick: result.SlowestChick));
        else
        {
            lines.Add(UiStrings.FormatAi_Maker_CheckFailed(count: result.Problems.Count));
            lines.AddRange(result.Problems.Select(p => p.Where is { } where ? $"{where} {ErrorLine(p.Error)}" : ErrorLine(p.Error)));
        }
        if (result.Path is { } path) lines.Add(PathText(path));
        return TextResult(string.Join("\n", lines) + GradientHintText(result.Hints), isError: result.Problems.Count > 0);
    }

    /// <summary>실행 경로(모델이 읽음 — 영어): 빠른 경로, 또는 Jint로 도는 까닭과 줄.</summary>
    private static string PathText(ScriptExecutionPath path)
        => path.Compiled ? "Execution path: fast path."
            : path.Line > 0 ? string.Create(CultureInfo.InvariantCulture, $"Execution path: Jint (slower), line {path.Line}: {path.Reason}")
            : $"Execution path: Jint (slower): {path.Reason}";

    /// <summary>스크립트 설정 값(C#)의 JSON 모양(색 = "#RRGGBB").</summary>
    private static JsonNode? SettingJson(object value) => value switch
    {
        bool b => JsonValue.Create(b),
        double d => JsonValue.Create(d),
        int i => JsonValue.Create(i),
        Color32 c => JsonValue.Create(c.ToHex()),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    private JsonObject WriteScriptEffect(string tool, JsonObject? a)
    {
        string source = OptString(a, "source") ?? throw new ToolError(string.Format(CultureInfo.CurrentCulture, UiStrings.S_PixelTools_ReqIndex_6802669E97, "source"));
        var parsed = ScriptEffectProgram.Parse(source);
        if (!parsed.Success)
            return TextResult(UiStrings.FormatAi_Script_Errors(tool: tool) + "\n" + string.Join("\n", parsed.Errors.Select(ErrorLine)), isError: true);
        var program = parsed.Program;

        var p = BeginMutation();
        int index = LayerIndex(a);
        var layer = p.Layers[index];
        RequireUnlocked(p, index);
        var given = SettingsArg(a) is { } json ? ScriptSettingArgs(program, json) : null;
        int? target = Arg(a, "effect") is null ? null : EffectIndex(a, p, index);
        if (target is { } t && layer.Effects[t] is not ScriptEffect)
            throw new ToolError(UiStrings.FormatAi_Script_NotScript(effect: t, layer: index));

        var hash = p.AddScript(source);
        ScriptTrustStore.TrustLocal(hash);
        var list = layer.Effects.ToList();
        int position;
        if (target is { } replace)
        {
            var old = (ScriptEffect)list[replace];
            var merged = new Dictionary<string, object>(program.NormalizeSettings(old.Settings), StringComparer.Ordinal);
            foreach (var (key, value) in given ?? []) merged[key] = value;
            list[replace] = old with { ScriptHash = hash, Settings = ScriptSettingValues.Normalized(program, merged) };
            position = replace;
        }
        else
        {
            list.Add(program.CreateEffect(given) with { ScriptHash = hash, Frames = NewRange(a, p) });
            position = list.Count - 1;
        }
        p.Edit(Label(tool), ProjectChange.Layers, () => p.SetLayerEffects(index, list));
        Ctx.SetCurrentLayer(index);

        var effect = (ScriptEffect)p.Layers[index].Effects[position];
        string text = UiStrings.FormatAi_Script_Written(tool: tool, name: program.Name, effect: position, layer: index);
        if (RunError(program, effect, program.PreviewChick) is { } error) text += "\n" + UiStrings.FormatAi_Script_RunError(error: ErrorLine(error));
        text += GradientHintText(ScriptGradientCheck.Static(program).Select(h => h.Message));
        text += "\n" + EffectsJson(p, index).ToJsonString(JsonOptions);
        return ImageResult(PreviewStrip([effect], PreviewChicks.Load(program.PreviewChick), program.PreviewFrames), text);
    }

    /// <summary>
    /// 스크립트 설정 인자: 선언의 설정 종류에 맞춘 C# 값(정수·실수 = 숫자, 켜고 끔 = 참거짓, 색 = "#RRGGBB", 고르기 = 항목 값).
    /// 모르는 키나 모양이 틀린 값은 도구 오류(범위는 <see cref="ScriptSettingValues.Normalized"/>가 자름).
    /// </summary>
    private static Dictionary<string, object> ScriptSettingArgs(ScriptEffectProgram program, JsonObject json)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, node) in json)
        {
            var setting = program.Settings.FirstOrDefault(s => s.Key == key)
                          ?? throw new ToolError(UiStrings.FormatAi_Script_UnknownSetting(key: key, keys: string.Join(", ", program.Settings.Select(s => s.Key))));
            object? value = setting.Type switch
            {
                ScriptSettingType.Int or ScriptSettingType.Number => Number(node),
                ScriptSettingType.Bool => node is JsonValue v && v.TryGetValue(out bool b) ? b : null,
                ScriptSettingType.Color => node is JsonValue v && v.TryGetValue(out string? hex) && Color32.TryParseHex(hex, out var c) ? c : null,
                ScriptSettingType.Layer => node is JsonValue v && v.TryGetValue(out string? layer) ? ScriptSetting.LayerName(layer) : null,
                _ => node is JsonValue v && v.TryGetValue(out string? choice) && setting.Choices.Any(x => x.Value == choice) ? choice : null,
            };
            values[key] = value ?? throw new ToolError(UiStrings.FormatAi_Script_BadSetting(key: key, type: setting.Type.ToString().ToLowerInvariant()));
        }
        return values;
    }

    /// <summary>JSON 숫자(정수·실수 어느 모양이든)나 숫자로 적은 글. 아니면 null.</summary>
    private static object? Number(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue(out double d)) return d;
        if (v.TryGetValue(out long l)) return (double)l;
        if (v.TryGetValue(out int i)) return (double)i;
        if (v.TryGetValue(out decimal m)) return (double)m;
        return v.TryGetValue(out string? s) && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : null;
    }

    private JsonObject PreviewEffects(JsonObject? a)
    {
        string on = OptEnum(a, "on", "chick", "layer") ?? "chick";
        IReadOnlyList<LayerEffect> effects;
        string what;
        if (OptString(a, "recipe")?.Trim() is { Length: > 0 } name)
        {
            var presets = LoadEffectRecipes(out _);
            var recipe = (presets.FirstOrDefault(x => x.Id == name) ?? presets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))?.Recipe
                         ?? throw new ToolError(UiStrings.FormatAi_Effect_UnknownPreset(name: name, presets: string.Join(", ", presets.Select(x => $"\"{x.Name}\""))));
            if (on == "layer") throw new ToolError(UiStrings.Ai_Preview_RecipeOnChick);
            effects = recipe.Effects;
            what = recipe.Name;
        }
        else
        {
            var p = RequireProject();
            int index = LayerIndex(a);
            var layer = p.Layers[index];
            if (layer.Effects.Count == 0) throw new ToolError(UiStrings.FormatAi_Effect_NoEffects(layer: index, name: layer.Name));
            if (on == "layer") return ImageResult(LayerStrip(p, layer), UiStrings.FormatAi_Preview_Layer(layer: index, name: layer.Name));
            effects = layer.Effects;
            what = layer.Name;
        }
        string chick = OptString(a, "chick")?.Trim() is { Length: > 0 } c && PreviewChicks.IsKnown(c) ? c : PreviewChicks.Default;
        return ImageResult(PreviewStrip(effects, PreviewChicks.Load(chick)), UiStrings.FormatAi_Preview_Chick(name: what, chick: chick));
    }

    private JsonObject SaveScriptEffect(JsonObject? a)
    {
        var p = RequireProject();
        int index = LayerIndex(a);
        var layer = p.Layers[index];
        int position = Arg(a, "effect") is null
            ? layer.Effects.Select((e, i) => (e, i)).LastOrDefault(x => x.e is ScriptEffect, (null!, -1)).Item2
            : EffectIndex(a, p, index);
        if (position < 0) throw new ToolError(UiStrings.FormatAi_Script_NoScript(layer: index, name: layer.Name));
        if (layer.Effects[position] is not ScriptEffect effect || !p.Scripts.TryGetValue(effect.ScriptHash, out var source))
            throw new ToolError(UiStrings.FormatAi_Script_NotScript(effect: position, layer: index));
        if (OptString(a, "chick")?.Trim() is { Length: > 0 } chick)
        {
            if (!PreviewChicks.IsKnown(chick)) throw new ToolError(UiStrings.FormatAi_Script_UnknownChick(chick: chick, chicks: string.Join(", ", PreviewChicks.Ids)));
            source = ScriptDeclarationEdit.SetPreviewChick(source, chick) ?? source;
        }
        AssetEntry saved;
        try
        {
            saved = AppAssets.Store.SavePersonalScript(source, null, null);
        }
        catch (Exception e) when (e is AssetPackageException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw new ToolError(string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_SaveError,
                e is AssetPackageException package ? AppAssets.IssueText(package.Issues) : e.Message));
        }
        ScriptTrustStore.TrustLocal(ScriptLibrary.Add(source));
        // 효과 만들기는 편집기(메인 창)에 딸린 창으로 연다
        if (_host is Window owner) Dispatcher.UIThread.Post(() => EffectMakerWindow.Open(owner, Ctx, saved.Id));
        return TextResult(UiStrings.FormatAi_Asset_Saved(name: saved.Name, id: saved.Id));
    }

    // ───────────── 그림 ─────────────

    /// <summary>
    /// 효과를 병아리에 입힌 칸들(<paramref name="steps"/>칸 — 스크립트는 선언의 <c>previewFrames</c>, 그 밖은 <see cref="AssetPreview.PackageFrames"/> — 중
    /// 고르게 몇 칸)을 2px 사이를 두고 가로로 이어 키운 PNG(base64). <paramref name="scene"/>이 있으면 장면 위에서 그린 칸(<see cref="PreviewSceneRender"/>).
    /// </summary>
    private static string PreviewStrip(IReadOnlyList<LayerEffect> effects, PixelImage chick, int steps = AssetPreview.PackageFrames, PreviewScene? scene = null)
    {
        bool animated = effects.Any(e => e is AnimatedEffect || e.ColorGradients.Values.Any(g => g.Animated));
        int count = animated ? Math.Min(PreviewStripFrames, steps) : 1;
        var render = scene is null ? null : new PreviewSceneRender(scene, effects, chick, steps);
        var frames = Enumerable.Range(0, count)
            .Select(i => count == 1 ? 0 : (int)Math.Round(i * (steps - 1) / (double)(count - 1)))
            .Select(step => render?.Render(step) ?? AssetPreview.Render(effects, chick, step, steps))
            .ToList();
        return Enlarge(SideBySide(frames), null).Base64;
    }

    /// <summary>레이어의 효과를 입힌 그 레이어 그림을 프레임 몇 칸 이어 붙인 PNG(base64).</summary>
    private static string LayerStrip(SpriteProject p, Layer layer)
    {
        int n = p.Frames.Count, count = Math.Min(PreviewStripFrames, n);
        var frames = new List<PixelImage>();
        for (int i = 0; i < count; i++)
        {
            int frame = count == 1 ? 0 : (int)Math.Round(i * (n - 1) / (double)(count - 1));
            if (p.EffectSource(layer, frame) is not { } source) continue;
            frames.Add(LayerEffects.Render(source, p.EffectsAt(layer, frame)).Image);
        }
        if (frames.Count == 0) throw new ToolError(UiStrings.Ai_Preview_Empty);
        return Enlarge(SideBySide(frames), null).Base64;
    }

    private static PixelImage SideBySide(IReadOnlyList<PixelImage> frames)
    {
        const int Gap = 2;
        int width = frames.Sum(f => f.Width) + Gap * (frames.Count - 1), height = frames.Max(f => f.Height);
        var strip = new PixelImage(width, height);
        int x = 0;
        foreach (var frame in frames)
        {
            strip.Blit(frame, x, height - frame.Height);
            x += frame.Width + Gap;
        }
        return strip;
    }

    /// <summary>
    /// 병아리에 진행 0.5로(미리보기 칸 수의 가운데 칸) 직접 한 번 돌려 본 실행 오류(없으면 null). <paramref name="scene"/>이 있으면 장면 위의 병아리 레이어에서
    /// (같은 img·ctx — 다른 레이어 함수가 장면 레이어를 읽음).
    /// </summary>
    private static ScriptError? RunError(ScriptEffectProgram program, ScriptEffect effect, string chickId, PreviewScene? scene = null)
    {
        var image = PreviewChicks.Load(chickId);
        int span = program.PreviewFrames - 1, step = span / 2;
        if (scene is not null)
            return EffectMakerWindow.ProbeError(new PreviewSceneRender(scene, [effect], image, program.PreviewFrames).Probe(program, effect.Settings, step, default));
        try
        {
            program.Render(image, new Rectangle(0, 0, image.Width, image.Height),
                new ScriptFrameContext { Time = 0.5, Progress = 0.5, FrameIndex = step, FrameStep = step, FrameSpan = span }, effect.Settings);
            return null;
        }
        catch (ScriptEffectException e)
        {
            return e.Error;
        }
    }

    private static string ErrorLine(ScriptError error)
        => error.Line > 0 ? string.Create(CultureInfo.InvariantCulture, $"line {error.Line}, column {error.Column}: {error.Message}") : error.Message;
}
