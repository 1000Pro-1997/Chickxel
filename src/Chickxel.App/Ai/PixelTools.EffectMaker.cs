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
/// 효과 만들기 도구(설계 7장): 스크립트 API 문서, 스크립트 효과 쓰기(해석 오류는 줄·칸으로 돌려주고 아무것도 바꾸지 않음), 효과 미리보기 그림,
/// 레이어 효과 목록을 개인 레시피로 저장. 올리기 도구는 두지 않는다 — 올리기는 사람이 효과 만들기 창에서 누른다(설계 원칙 4).
/// AI가 쓴 원문은 이 컴퓨터에서 쓴 원문으로 보고 실행을 허락한다(실행은 다른 스크립트처럼 Jint 샌드박스·한도 안).
/// </summary>
public sealed partial class PixelTools
{
    /// <summary>미리보기 그림에 넣는 칸 수(효과 범위를 고르게 나눠서).</summary>
    private const int PreviewStripFrames = 5;

    private static IEnumerable<JsonObject> EffectMakerToolDefinitions()
    {
        yield return Tool("get_script_api",
            "Read the script effect API (JavaScript): the effect({...}) declaration, img/ctx/s objects, helpers, limits and an example. " +
            "Read it before writing a script with write_script_effect. Built-in effects (list_effect_types) cover most needs; write a script only for looks they can't make.",
            new JsonObject());
        yield return Tool("write_script_effect",
            "Write a script effect (JavaScript source calling effect({...}) once) and add it to a layer, or replace the source of an existing script effect. " +
            "If the source has syntax or declaration errors nothing changes and the errors come back with line and column; fix and call again. " +
            "On success the result shows the effect on the preview chick (several frames for animated effects) and any run error. One undo step. " +
            FrameRangeNote,
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
        yield return Tool("save_effect_asset",
            "Save a layer's effect list (built-in and script effects) as a personal effect recipe with a name, description and preview chick, and open it in the Effect Maker " +
            "window. Publishing online is done by the user from that window (there is no upload tool).",
            new JsonObject
            {
                ["layer"] = Int("Layer index whose effects to save. Default: current.", 0),
                ["name"] = Str("Recipe name."),
                ["description"] = Str("Short description (optional)."),
                ["chick"] = Str("Preview chick id for the published preview (optional, default \"original\")."),
                ["all_frames"] = Bool("Insert over all frames instead of the effects window's add range (default false)."),
            },
            "name");
    }

    private static JsonObject GetScriptApi() => TextResult(ScriptApiCatalog.ToMarkdown());

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
            list.Add(new ScriptEffect { ScriptHash = hash, Settings = ScriptSettingValues.Normalized(program, given), Frames = NewRange(a, p) });
            position = list.Count - 1;
        }
        p.Edit(Label(tool), ProjectChange.Layers, () => p.SetLayerEffects(index, list));
        Ctx.SetCurrentLayer(index);

        var effect = (ScriptEffect)p.Layers[index].Effects[position];
        string text = UiStrings.FormatAi_Script_Written(tool: tool, name: program.Name, effect: position, layer: index);
        if (RunError(program, effect, program.PreviewChick) is { } error) text += "\n" + UiStrings.FormatAi_Script_RunError(error: ErrorLine(error));
        text += "\n" + EffectsJson(p, index).ToJsonString(JsonOptions);
        return ImageResult(PreviewStrip([effect], PreviewChicks.Load(program.PreviewChick)), text);
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

    private JsonObject SaveEffectAsset(JsonObject? a)
    {
        var p = RequireProject();
        int index = LayerIndex(a);
        var layer = p.Layers[index];
        if (layer.Effects.Count == 0) throw new ToolError(UiStrings.FormatAi_Effect_NoEffects(layer: index, name: layer.Name));
        string name = OptString(a, "name")?.Trim() ?? "";
        if (!AssetManifest.IsValidName(name)) throw new ToolError(UiStrings.Ai_Asset_Name);
        string chick = OptString(a, "chick")?.Trim() is { Length: > 0 } c && PreviewChicks.IsKnown(c) ? c : PreviewChicks.Default;
        var recipe = EffectRecipe.Capture("", name, layer.Effects, p) with
        {
            Description = OptString(a, "description")?.Trim() is { Length: > 0 } d ? d : null,
            PreviewChick = chick,
            FrameRange = OptBool(a, "all_frames") == true ? RecipeFrameRange.AllFrames : RecipeFrameRange.AddRange,
        };
        AssetEntry saved;
        try
        {
            saved = AppAssets.Store.SavePersonalRecipe(recipe);
        }
        catch (Exception e) when (e is AssetPackageException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw new ToolError(string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_SaveError,
                e is AssetPackageException package ? AppAssets.IssueText(package.Issues) : e.Message));
        }
        foreach (var source in recipe.Scripts.Values) ScriptTrustStore.TrustLocal(ScriptLibrary.Add(source));
        // 효과 만들기는 편집기(메인 창)에 딸린 창으로 연다
        if (_host is Window owner) Dispatcher.UIThread.Post(() => EffectMakerWindow.Open(owner, Ctx, saved.Id));
        return TextResult(UiStrings.FormatAi_Asset_Saved(name: name, id: saved.Id));
    }

    // ───────────── 그림 ─────────────

    /// <summary>효과를 병아리에 입힌 칸들(<see cref="AssetPreview.PackageFrames"/>칸 중 고르게 몇 칸)을 2px 사이를 두고 가로로 이어 키운 PNG(base64).</summary>
    private static string PreviewStrip(IReadOnlyList<LayerEffect> effects, PixelImage chick)
    {
        bool animated = effects.Any(e => e is AnimatedEffect || e.ColorGradients.Values.Any(g => g.Animated));
        int count = animated ? PreviewStripFrames : 1, steps = AssetPreview.PackageFrames;
        var frames = Enumerable.Range(0, count)
            .Select(i => AssetPreview.Render(effects, chick, count == 1 ? 0 : (int)Math.Round(i * (steps - 1) / (double)(count - 1)), steps))
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

    /// <summary>병아리에 진행 0.5로 직접 한 번 돌려 본 실행 오류(없으면 null).</summary>
    private static ScriptError? RunError(ScriptEffectProgram program, ScriptEffect effect, string chickId)
    {
        var image = PreviewChicks.Load(chickId);
        try
        {
            program.Render(image, new Rectangle(0, 0, image.Width, image.Height),
                new ScriptFrameContext { Time = 0.5, Progress = 0.5, FrameIndex = 5, FrameStep = 5, FrameSpan = 9 }, effect.Settings);
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
