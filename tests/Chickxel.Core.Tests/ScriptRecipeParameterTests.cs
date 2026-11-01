using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 스크립트 효과 설정을 기본 효과 schema처럼 다루기: 레시피 매개변수 연결·값, AI 도구의 설정 읽기·덮기(<see cref="EffectSettings"/>),
/// 패키지 레시피의 엄격 검사(색 목록·색 쌍 목록 값, 그라데이션 슬롯). 신뢰 결정은 프로세스 전체 상태라 시험마다 원문 끝에 주석을 붙여 해시를 따로 쓴다.
/// </summary>
public class ScriptRecipeParameterTests
{
    private const string AssetId = AssetSamples.PackageId + ".x";

    private const string Source = """
        effect({
          name: "Params",
          settings: [
            { key: "size", type: "int", min: 1, max: 8, default: 3 },
            { key: "speed", type: "number", min: 0, max: 2, default: 0.5 },
            { key: "glow", type: "bool", default: true },
            { key: "color", type: "color", alpha: false, default: "#FF0000" },
            { key: "mode", type: "choice", choices: ["soft", { value: "hard", label: "Hard" }], default: "soft" },
            { key: "colors", type: "colorList", min: 1, max: 3, default: ["#000000", "#FFFFFF"] },
            { key: "pairs", type: "colorPairs", max: 2, default: [{ from: "#000000", to: "#FFFFFF" }] },
          ],
          render(img, ctx, s) {},
        });
        """;

    private static readonly Color32 Red = new(255, 0, 0), Green = new(0, 255, 0);

    private static string Unique(string source, [CallerMemberName] string test = "") => source + "\n// " + test + "\n";

    private static ScriptEffectProgram TrustedProgram(string source)
    {
        var hash = ScriptLibrary.Add(source);
        ScriptLibrary.SetTrust(hash, true);
        return ScriptLibrary.Runnable(hash)!;
    }

    private static RecipeParameter Parameter(RecipeParameterKind kind, RecipeValue value, params RecipeBinding[] bindings)
        => new() { Id = "p", Name = "P", Kind = kind, Default = value, Bindings = bindings };

    private static List<(AssetIssueCode Code, string Detail)> Issues(IReadOnlyList<LayerEffect> effects, RecipeParameter parameter,
        Func<string, ScriptEffectProgram?>? programs = null)
    {
        var issues = new List<(AssetIssueCode, string)>();
        RecipeParameters.Check(effects, [parameter], (code, detail) => issues.Add((code, detail)), programs);
        return issues;
    }

    // ───────────── 레시피 매개변수 ─────────────

    [Fact]
    public void Recipe_parameters_read_and_write_script_settings_through_the_declared_schema()
    {
        var program = TrustedProgram(Unique(Source));
        var effect = program.CreateEffect();

        var size = RecipeParameters.SettingOf(effect, "size")!;
        Assert.Equal((EffectParameterKind.Int, 1, 8), (size.Kind, size.Min, size.Max));
        Assert.Equal(["soft", "hard"], RecipeParameters.SettingOf(effect, "mode")!.Choices);
        Assert.False(RecipeParameters.CanExpose(RecipeParameters.SettingOf(effect, "speed")!));
        Assert.False(RecipeParameters.CanExpose(RecipeParameters.SettingOf(effect, "colors")!));
        Assert.True(RecipeParameters.CanExpose(RecipeParameters.SettingOf(effect, "easing")!));

        Assert.Equal(RecipeValue.Of(3), RecipeParameters.ValueOf(effect, "size"));
        Assert.Equal(RecipeValue.Of(true), RecipeParameters.ValueOf(effect, "glow"));
        Assert.Equal(RecipeValue.Of(Red), RecipeParameters.ValueOf(effect, "color"));
        Assert.Equal(RecipeValue.OfChoice("soft"), RecipeParameters.ValueOf(effect, "mode"));
        Assert.Null(RecipeParameters.ValueOf(effect, "speed"));
        Assert.Null(RecipeParameters.ValueOf(effect, "pairs"));

        // 넣을 때 스크립트 schema가 맞춘다: 정수는 범위로 자르고, alpha: false 색은 불투명
        var changed = RecipeParameters.With(effect, "size", RecipeValue.Of(20));
        Assert.Equal(RecipeValue.Of(8), RecipeParameters.ValueOf(changed, "size"));
        changed = RecipeParameters.With(changed, "color", RecipeValue.Of(new Color32(0, 255, 0, 128)));
        Assert.Equal(RecipeValue.Of(Green), RecipeParameters.ValueOf(changed, "color"));
        changed = RecipeParameters.With(changed, "glow", RecipeValue.Of(false));
        changed = RecipeParameters.With(changed, "mode", RecipeValue.OfChoice("hard"));
        Assert.Equal((RecipeValue.Of(false), RecipeValue.OfChoice("hard")), (RecipeParameters.ValueOf(changed, "glow"), RecipeParameters.ValueOf(changed, "mode")));
        var script = Assert.IsType<ScriptEffect>(changed);
        Assert.Equal(effect.ScriptHash, script.ScriptHash);
        Assert.Equal(ScriptSettingValues.Normalized(program, script.Settings), script.Settings);

        // 고를 수 없는 값·종류가 다른 값·없는 키는 효과 그대로
        Assert.Same(changed, RecipeParameters.With(changed, "mode", RecipeValue.OfChoice("nope")));
        Assert.Same(changed, RecipeParameters.With(changed, "size", RecipeValue.Of(true)));
        Assert.Same(changed, RecipeParameters.With(changed, "speed", RecipeValue.Of(1)));
        Assert.Same(changed, RecipeParameters.With(changed, "missing", RecipeValue.Of(1)));
    }

    [Fact]
    public void Recipe_parameters_apply_and_intersect_ranges_across_script_and_builtin_settings()
    {
        var program = TrustedProgram(Unique(Source));
        IReadOnlyList<LayerEffect> effects = [program.CreateEffect(), new OutlineEffect { Thickness = 1 }];
        RecipeBinding size = new(0, "size"), thickness = new(1, "thickness");

        Assert.Equal((1, 3), RecipeParameters.IntRange(effects, [size, thickness]));
        Assert.Equal((1, 8), RecipeParameters.IntRange(effects, [size]));
        Assert.Equal(["soft", "hard"], RecipeParameters.CommonChoices(effects, [new RecipeBinding(0, "mode")]));

        var width = Parameter(RecipeParameterKind.Int, RecipeValue.Of(2), size, thickness) with { Min = 1, Max = 3 };
        Assert.Empty(Issues(effects, width));
        var applied = RecipeParameters.Apply(effects, width, RecipeValue.Of(9));
        Assert.Equal(RecipeValue.Of(3), RecipeParameters.ValueOf(applied[0], "size"));
        Assert.Equal(3, Assert.IsType<OutlineEffect>(applied[1]).Thickness);
        var defaults = RecipeParameters.ApplyDefaults(effects, [width]);
        Assert.Equal(RecipeValue.Of(2), RecipeParameters.ValueOf(defaults[0], "size"));
    }

    [Fact]
    public void Recipe_parameter_check_uses_script_ranges_kinds_and_choices()
    {
        var program = TrustedProgram(Unique(Source));
        IReadOnlyList<LayerEffect> effects = [program.CreateEffect()];

        Assert.Empty(Issues(effects, Parameter(RecipeParameterKind.Color, RecipeValue.Of(Red), new RecipeBinding(0, "color"))));
        Assert.Empty(Issues(effects, Parameter(RecipeParameterKind.Bool, RecipeValue.Of(true), new RecipeBinding(0, "glow"))));
        Assert.Empty(Issues(effects, Parameter(RecipeParameterKind.Choice, RecipeValue.OfChoice("hard"), new RecipeBinding(0, "mode")) with { Choices = ["soft", "hard"] }));

        var outOfRange = Parameter(RecipeParameterKind.Int, RecipeValue.Of(2), new RecipeBinding(0, "size")) with { Min = 0, Max = 9 };
        Assert.Equal(
            [
                (AssetIssueCode.RecipeParameterOutOfRange, "parameters[0].min: 0 < effects[0].size 1"),
                (AssetIssueCode.RecipeParameterOutOfRange, "parameters[0].max: 9 > effects[0].size 8"),
            ],
            Issues(effects, outOfRange));
        Assert.Equal([(AssetIssueCode.RecipeBindingKindMismatch, "parameters[0].bindings[0]: effects[0].size int / color")],
            Issues(effects, Parameter(RecipeParameterKind.Color, RecipeValue.Of(Red), new RecipeBinding(0, "size"))));
        Assert.Equal([(AssetIssueCode.RecipeBindingKindMismatch, "parameters[0].bindings[0]: effects[0].speed number / int")],
            Issues(effects, Parameter(RecipeParameterKind.Int, RecipeValue.Of(1), new RecipeBinding(0, "speed")) with { Min = 0, Max = 1 }));
        Assert.Equal([(AssetIssueCode.RecipeBindingKindMismatch, "parameters[0].bindings[0]: effects[0].colors colorlist / color")],
            Issues(effects, Parameter(RecipeParameterKind.Color, RecipeValue.Of(Red), new RecipeBinding(0, "colors"))));
        Assert.Equal([(AssetIssueCode.RecipeBindingUnknownKey, $"parameters[0].bindings[0]: {ScriptDefinition.Id}.nope")],
            Issues(effects, Parameter(RecipeParameterKind.Bool, RecipeValue.Of(true), new RecipeBinding(0, "nope"))));
        Assert.Equal([(AssetIssueCode.RecipeParameterOutOfRange, "parameters[0].choices: wild / effects[0].mode")],
            Issues(effects, Parameter(RecipeParameterKind.Choice, RecipeValue.OfChoice("soft"), new RecipeBinding(0, "mode")) with { Choices = ["soft", "wild"] }));
    }

    [Fact]
    public void Recipe_parameter_check_keeps_bindings_of_unknown_scripts_and_uses_a_given_program()
    {
        // 허락하지 않은(실행할 수 없는) 원문: 스크립트 설정 키가 맞는지 알 수 없으니 문제로 치지 않는다
        var source = Unique(Source);
        var program = ScriptEffectProgram.Parse(source).Program!;
        IReadOnlyList<LayerEffect> effects = [new ScriptEffect { ScriptHash = program.ContentHash }];
        var unknown = Parameter(RecipeParameterKind.Bool, RecipeValue.Of(true), new RecipeBinding(0, "anything"));
        Assert.Empty(Issues(effects, unknown));
        Assert.Null(RecipeParameters.SettingOf(effects[0], "size"));
        // 움직임 설정은 원문 없이도 안다
        Assert.Equal([(AssetIssueCode.RecipeBindingKindMismatch, "parameters[0].bindings[0]: effects[0].easing choice / bool")],
            Issues(effects, Parameter(RecipeParameterKind.Bool, RecipeValue.Of(true), new RecipeBinding(0, "easing"))));

        // 해석 결과를 주면 그 schema로 검사하고 값을 넣는다
        Func<string, ScriptEffectProgram?> programs = hash => hash == program.ContentHash ? program : null;
        Assert.Equal([(AssetIssueCode.RecipeBindingUnknownKey, $"parameters[0].bindings[0]: {ScriptDefinition.Id}.anything")],
            Issues(effects, unknown, programs));
        var applied = RecipeParameters.Apply(effects, Parameter(RecipeParameterKind.Int, RecipeValue.Of(5), new RecipeBinding(0, "size")) with { Min = 1, Max = 8 },
            RecipeValue.Of(5), programs);
        Assert.Equal(RecipeValue.Of(5), RecipeParameters.ValueOf(applied[0], "size", programs));
    }

    // ───────────── AI 도구 설정 ─────────────

    [Fact]
    public void Effect_settings_describe_script_settings_followed_by_motion_settings()
    {
        var program = TrustedProgram(Unique(Source));
        var effect = program.CreateEffect();
        var schema = EffectSettings.Describe(effect);
        Assert.Equal(["size", "speed", "glow", "color", "mode", "colors", "pairs", "easing", "repeat", "repeatCount"], schema.Select(p => p.Key));
        Assert.Equal(
            [
                EffectParameterKind.Int, EffectParameterKind.Number, EffectParameterKind.Bool, EffectParameterKind.Color, EffectParameterKind.Choice,
                EffectParameterKind.ColorList, EffectParameterKind.ColorPairList,
            ],
            schema.Take(7).Select(p => p.Kind));
        Assert.Same(schema, EffectSettings.Describe(program.CreateEffect()));
        Assert.Equal(["color", "colors", "pairs"], schema.Select(p => p.ColorSlot).OfType<string>());
        Assert.Null(schema[1].Min);
        Assert.True(JsonNode.DeepEquals(new JsonArray("#000000", "#FFFFFF"), schema[5].DefaultValue));
        Assert.True(JsonNode.DeepEquals(new JsonArray(new JsonArray("#000000", "#FFFFFF")), schema[6].DefaultValue));
        Assert.Equal(0.5, schema[1].DefaultValue.GetValue<double>());

        var json = EffectSettings.ToJson(effect);
        Assert.Equal("3", json["size"]!.ToJsonString());
        Assert.Equal("soft", json["mode"]!.GetValue<string>());

        // 원문을 실행할 수 없으면 움직임 설정만
        var unknown = new ScriptEffect { ScriptHash = ScriptEffectProgram.HashOf(Unique(Source, "unknown")) };
        Assert.Equal(["easing", "repeat", "repeatCount"], EffectSettings.Describe(unknown).Select(p => p.Key));
    }

    [Fact]
    public void Effect_settings_merge_script_settings_and_report_clamped_numbers()
    {
        var program = TrustedProgram(Unique(Source));
        var effect = program.CreateEffect();
        var (merged, clamped) = EffectSettings.Merge(effect, new JsonObject
        {
            ["Size"] = 99, ["speed"] = "1.5", ["GLOW"] = "false", ["color"] = "#00FF0080", ["MODE"] = "Hard",
            ["colors"] = new JsonArray("#111111", "#222222", "#333333"),
            ["pairs"] = new JsonArray(new JsonArray("#000000", "#FF0000"), new JsonArray("#00FF00", "#0000FF")),
            ["repeat_count"] = 2,
        });
        Assert.Equal(["size = 8"], clamped);
        var script = Assert.IsType<ScriptEffect>(merged);
        var values = program.NormalizeSettings(script.Settings);
        Assert.Equal(8, values["size"]);
        Assert.Equal(1.5, values["speed"]);
        Assert.False((bool)values["glow"]);
        Assert.Equal(Green, values["color"]);
        Assert.Equal("hard", values["mode"]);
        Assert.Equal([new Color32(0x11, 0x11, 0x11), new Color32(0x22, 0x22, 0x22), new Color32(0x33, 0x33, 0x33)], (IReadOnlyList<Color32>)values["colors"]);
        Assert.Equal([new ScriptColorPair(new Color32(0, 0, 0), Red), new ScriptColorPair(Green, new Color32(0, 0, 255))],
            (IReadOnlyList<ScriptColorPair>)values["pairs"]);
        Assert.Equal(2, script.RepeatCount);
        Assert.Equal(new JsonArray("#111111", "#222222", "#333333").ToJsonString(), EffectSettings.ToJson(script)["colors"]!.ToJsonString());

        // 실수도 범위로 잘린다. null은 기본값
        (merged, clamped) = EffectSettings.Merge(script, new JsonObject { ["speed"] = 5, ["size"] = null });
        Assert.Equal(["speed = 2"], clamped);
        Assert.Equal(3, program.NormalizeSettings(((ScriptEffect)merged).Settings)["size"]);

        // 색 슬롯 그라데이션은 설정 키 그대로
        var gradient = JsonNode.Parse("""{ "stops": [ { "position": 0, "color": "#000000" }, { "position": 1, "color": "#FFFFFF" } ] }""")!;
        (merged, _) = EffectSettings.Merge(script, new JsonObject { ["colorGradients"] = new JsonObject { ["color"] = gradient } });
        Assert.Equal(["color"], merged.ColorGradients.Keys);
        Assert.NotNull(EffectSettings.ToJson(merged)["colorGradients"]!["color"]);
    }

    [Theory]
    [InlineData("""{ "colors": ["#111111", "#222222", "#333333", "#444444"] }""", "colors", EffectSettingError.InvalidEntries)]
    [InlineData("""{ "colors": ["#111111", "nope"] }""", "colors", EffectSettingError.InvalidEntries)]
    [InlineData("""{ "pairs": "#000000" }""", "pairs", EffectSettingError.WrongType)]
    [InlineData("""{ "size": "big" }""", "size", EffectSettingError.WrongType)]
    [InlineData("""{ "speed": true }""", "speed", EffectSettingError.WrongType)]
    [InlineData("""{ "mode": "wild" }""", "mode", EffectSettingError.NotAChoice)]
    [InlineData("""{ "color": "red" }""", "color", EffectSettingError.InvalidColor)]
    [InlineData("""{ "Size2": 1 }""", "Size2", EffectSettingError.UnknownKey)]
    public void Effect_settings_merge_rejects_wrong_script_values(string settings, string key, EffectSettingError error)
    {
        var program = TrustedProgram(Unique(Source) + "// " + key + error);
        var e = Assert.Throws<EffectSettingException>(() => EffectSettings.Merge(program.CreateEffect(), JsonNode.Parse(settings)!.AsObject()));
        Assert.Equal((key, error), (e.Key, e.Error));
    }

    // ───────────── 패키지 레시피 엄격 검사 ─────────────

    private static JsonObject RecipeNode(string source, JsonObject settings, JsonObject? gradients = null, JsonArray? parameters = null)
    {
        var hash = ScriptEffectProgram.HashOf(source);
        var effect = new JsonObject { ["type"] = ScriptDefinition.Id, ["settings"] = settings, ["script"] = hash };
        if (gradients is not null) effect["colorGradients"] = gradients;
        var recipe = new JsonObject
        {
            ["schemaVersion"] = 3, ["id"] = AssetId, ["name"] = "X", ["effects"] = new JsonArray(effect),
            ["scripts"] = new JsonObject { [hash] = source },
        };
        if (parameters is not null) recipe["parameters"] = parameters;
        return recipe;
    }

    private static JsonObject Gradients(params string[] slots)
        => new(slots.Select(slot => KeyValuePair.Create(slot,
            JsonNode.Parse("""{ "stops": [ { "position": 0, "color": "#000000" }, { "position": 1, "color": "#FFFFFF" } ] }"""))));

    // 패키지 파일처럼 글에서 읽은 JSON으로(숫자 값이 JSON 원문으로 남게)
    private static (EffectRecipe? Recipe, List<AssetIssue> Issues) ReadStrict(JsonObject node)
    {
        var context = new AssetReadContext("x.json", strict: true);
        return (EffectRecipeFormat.Read(JsonNode.Parse(node.ToJsonString()), context), context.Issues);
    }

    [Fact]
    public void Strict_recipe_accepts_script_lists_and_gradients_on_script_slots()
    {
        // 실행을 허락하지 않은 원문 — 엄격 검사는 레시피에 실은 원문을 해석해 본다
        var source = Unique(Source);
        var settings = new JsonObject
        {
            ["size"] = 4, ["colors"] = new JsonArray("#111111", "#222222"),
            ["pairs"] = new JsonArray(new JsonArray("#000000", "#FF0000"), new JsonArray("#00FF00", "#0000FFAA")),
        };
        var (recipe, issues) = ReadStrict(RecipeNode(source, settings, Gradients("color", "colors[1]", "colors[2]", "pairs[0].to", "pairs[1].from")));
        Assert.Empty(issues);
        var effect = Assert.IsType<ScriptEffect>(Assert.Single(recipe!.Effects));
        Assert.Equal(["color", "colors[1]", "colors[2]", "pairs[0].to", "pairs[1].from"], effect.ColorGradients.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Color")]
    [InlineData("colors")]
    [InlineData("colors[3]")]
    [InlineData("colors[01]")]
    [InlineData("color[0]")]
    [InlineData("pairs[0]")]
    [InlineData("pairs[0].To")]
    [InlineData("pairs[2].to")]
    [InlineData("size")]
    [InlineData("easing")]
    public void Strict_recipe_rejects_gradients_outside_script_color_slots(string slot)
    {
        var (recipe, issues) = ReadStrict(RecipeNode(Unique(Source) + "// " + slot, [], Gradients(slot)));
        Assert.Null(recipe);
        // 거부한 효과가 빠지면 그 원문도 쓰지 않는 원문으로 함께 걸린다
        Assert.Equal([(AssetIssueCode.InvalidEffectSetting, $"effects[0].colorGradients.{slot}")],
            issues.Where(i => i.Code != AssetIssueCode.InvalidField).Select(i => (i.Code, i.Detail)));
    }

    [Theory]
    [InlineData("colors", """[]""")]
    [InlineData("colors", """["#111111", "#222222", "#333333", "#444444"]""")]
    [InlineData("colors", """["#111111", "red"]""")]
    [InlineData("colors", "\"#111111\"")]
    [InlineData("pairs", """[["#000000", "#111111"], ["#000000", "#111111"], ["#000000", "#111111"]]""")]
    [InlineData("pairs", """[["#000000"]]""")]
    [InlineData("pairs", """["#000000", "#111111"]""")]
    [InlineData("mode", "\"Hard\"")]
    [InlineData("size", "9")]
    public void Strict_recipe_rejects_script_values_outside_the_declaration(string key, string value)
    {
        var settings = new JsonObject { [key] = JsonNode.Parse(value) };
        var (recipe, issues) = ReadStrict(RecipeNode(Unique(Source) + "// " + key + value, settings));
        Assert.Null(recipe);
        Assert.Equal([(AssetIssueCode.InvalidEffectSetting, $"effects[0].settings.{key}")],
            issues.Where(i => i.Code != AssetIssueCode.InvalidField).Select(i => (i.Code, i.Detail)));
    }

    [Fact]
    public void Strict_recipe_checks_parameters_against_the_carried_script_and_applies_defaults()
    {
        var source = Unique(Source);
        JsonArray Parameters(int max) => new(JsonNode.Parse($$"""
            { "id": "size", "name": "Size", "kind": "int", "default": 5, "min": 1, "max": {{max}}, "bindings": [ { "effect": 0, "key": "size" } ] }
            """));

        var (recipe, issues) = ReadStrict(RecipeNode(source, [], parameters: Parameters(8)));
        Assert.Empty(issues);
        var effect = Assert.IsType<ScriptEffect>(Assert.Single(recipe!.Effects));
        Assert.Equal(5.0, effect.Settings["size"]);

        (recipe, issues) = ReadStrict(RecipeNode(source, [], parameters: Parameters(20)));
        Assert.Null(recipe);
        var issue = Assert.Single(issues);
        Assert.Equal((AssetIssueCode.RecipeParameterOutOfRange, "parameters[0].max: 20 > effects[0].size 8"), (issue.Code, issue.Detail));
    }
}
