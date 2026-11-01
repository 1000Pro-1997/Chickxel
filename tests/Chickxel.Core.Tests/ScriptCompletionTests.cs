using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>코드 편집기 자동 완성·매개변수 안내·설명 풍선의 문맥 판정(쓰는 중이라 문법이 깨진 원문 포함).</summary>
public class ScriptCompletionTests
{
    private const string Header = """
        effect({
          name: "x",
          settings: [
            { key: "strength", label: "S", type: "int", min: 0, max: 100, default: 50 },
            { key: "tint", label: "T", type: "color", default: "#ff0000" },
          ],
          render(img, ctx, s) {
            const w = img.width;

        """;

    /// <summary><c>|</c> 자리를 커서로 보고 후보 이름을 돌려준다.</summary>
    private static (int Start, string[] Names) At(string withCaret)
    {
        int caret = withCaret.IndexOf('|');
        var result = ScriptCompletion.Suggest(withCaret.Remove(caret, 1), caret);
        return (result.Start, result.Items.Select(i => i.Name).ToArray());
    }

    [Theory]
    [InlineData("ctx.|", new[] { "t", "phase", "progress", "frame", "step", "span", "source", "pivot", "center", "slice", "color", "colorAt", "pairColor",
        "hasGradient", "hash", "random", "bayer2", "bayer4", "frameSource", "renderedFrameSource",
        "layerBelow", "layerAbove", "layerBelowWithEffects", "layerAboveWithEffects", "layerEffectsByName", "layerEffectsAt" })]
    [InlineData("ctx.slice.|", new[] { "left", "top", "right", "bottom" })]
    [InlineData("ctx.pr|", new[] { "progress" })]
    [InlineData("ctx.source.|", new[] { "x", "y", "w", "h" })]
    [InlineData("img.|", new[] { "width", "height", "data", "blank", "blendOver" })]
    [InlineData("s.|", new[] { "strength", "tint" })]
    [InlineData("s.tint.|", new[] { "r", "g", "b", "a" })]
    [InlineData("const c = ctx.color(\"tint\", 0, 0);\n    c.|", new[] { "r", "g", "b", "a" })]
    [InlineData("ctx.color(\"tint\", 0, 0).|", new[] { "r", "g", "b", "a" })]
    [InlineData("const hsv = rgbToHsv(1, 2, 3); hsv.|", new[] { "h", "s", "v" })]
    [InlineData("Math.fl|", new[] { "floor" })]
    [InlineData("ctx.color(\"|", new[] { "tint" })]
    [InlineData("// ctx.|", new string[0])]
    [InlineData("const label = \"ctx.|", new string[0])]
    [InlineData("w.|", new string[0])]
    public void Member_completion_follows_the_object_before_the_dot(string body, string[] expected)
        => Assert.Equal(expected, At(Header + "    " + body).Names);

    [Fact]
    public void Renamed_render_parameters_are_followed()
        => Assert.Contains("progress", At("effect({ name: 'x', render(image, c, opts) {\n  c.|").Names);

    [Fact]
    public void Global_completion_offers_api_locals_and_keywords_for_the_typed_prefix()
    {
        var (start, names) = At(Header + "    const wobble = 1;\n    wob|");
        Assert.Equal(["wobble"], names);
        Assert.Equal(Header.Length + "    const wobble = 1;\n    ".Length, start);
        Assert.Contains("hsvToRgb", At(Header + "    hs|").Names);
        Assert.Contains("const", At(Header + "    co|").Names);
    }

    [Theory]
    [InlineData("effect({ na|", new[] { "name" })]
    [InlineData("effect({\n  name: \"x\",\n  pre|", new[] { "previewChick", "previewFrames" })]
    [InlineData("effect({ name: 'x', settings: [{ key: 'a', ty|", new[] { "type" })]
    [InlineData("effect({ name: 'x', settings: [{ key: 'a', type: \"co|", new[] { "color", "colorList", "colorPairs" })]
    [InlineData("effect({ name: 'x', easing: \"Ease|", new[] { "EaseIn", "EaseOut", "EaseInOut" })]
    [InlineData("effect({ name: 'x', settings: [{ key: 'a', widget: \"|", new[] { "angle", "point" })]
    [InlineData("effect({ name: 'x', area: \"lo|", new[] { "local" })]
    [InlineData("effect({ name: 'x', previewChick: \"kn|", new[] { "knight" })]
    public void Declaration_and_setting_keys_and_values_are_offered_in_place(string text, string[] expected)
        => Assert.Equal(expected, At(text).Names);

    [Theory]
    [InlineData("lerp(1, |", "lerp", 1)]
    [InlineData("ctx.color(\"tint\", |", "color", 1)]
    [InlineData("Math.max(lerp(a, |", "lerp", 1)]
    [InlineData("Math.max(lerp(a, b, c), |", "max", 1)]
    [InlineData("hsvToRgb(|", "hsvToRgb", 0)]
    public void Signature_help_names_the_innermost_call_and_argument(string body, string member, int parameter)
    {
        string text = Header + "    " + body;
        int caret = text.IndexOf('|');
        var help = ScriptCompletion.Signature(text.Remove(caret, 1), caret);
        Assert.NotNull(help);
        Assert.Equal((member, parameter), (help.Member.Name, help.Parameter));
    }

    [Fact]
    public void Signature_help_is_off_outside_known_calls()
    {
        string text = Header + "    foo(1, ";
        Assert.Null(ScriptCompletion.Signature(text, text.Length));
        string closed = Header + "    lerp(1, 2, 3); ";
        Assert.Null(ScriptCompletion.Signature(closed, closed.Length));
    }

    [Fact]
    public void Hover_describes_api_names_and_declaration_keys()
    {
        string text = Header + "    const p = ctx.progress;\n";
        Assert.Equal((ScriptApiCatalog.Context, "progress"), Describe(text, "progress"));
        Assert.Equal((ScriptApiCatalog.Declaration, "settings"), Describe(text, "settings"));
        Assert.Null(ScriptCompletion.Describe(text, text.IndexOf("const p", StringComparison.Ordinal) + 6));

        static (string, string)? Describe(string text, string word)
            => ScriptCompletion.Describe(text, text.IndexOf(word, StringComparison.Ordinal) + 1) is { } m ? (m.Scope, m.Name) : null;
    }
}

/// <summary>효과 만들기의 정보 칸이 원문 선언을 고치는 것(나머지 글자는 그대로).</summary>
public class ScriptDeclarationEditTests
{
    [Fact]
    public void Preview_chick_is_replaced_in_place_or_added_after_name()
    {
        Assert.Equal("effect({ name: 'x', previewChick: \"ninja\", render() {} });",
            ScriptDeclarationEdit.SetPreviewChick("effect({ name: 'x', previewChick: 'knight', render() {} });", "ninja"));
        Assert.Equal("effect({ name: 'x', previewChick: \"ninja\", render() {} });",
            ScriptDeclarationEdit.SetPreviewChick("effect({ name: 'x', render() {} });", "ninja"));
        Assert.Equal("effect({\n  name: \"x\",\n  previewChick: \"ninja\",\n  render() {},\n});",
            ScriptDeclarationEdit.SetPreviewChick("effect({\n  name: \"x\",\n  render() {},\n});", "ninja"));
        Assert.Equal("effect({ render() {}, name: 'x', previewChick: \"ninja\" });",
            ScriptDeclarationEdit.SetPreviewChick("effect({ render() {}, name: 'x' });", "ninja"));

        // 고친 원문은 그대로 해석되고 병아리가 바뀐다
        var edited = ScriptDeclarationEdit.SetPreviewChick(Chickxel.Core.Scripting.OfficialEffects.Source("shine"), "viking")!;
        Assert.Equal("viking", ScriptEffectProgram.Parse(edited).Program!.PreviewChick);
        Assert.Null(ScriptDeclarationEdit.SetPreviewChick("effect({ name: 'x', render( {} });", "ninja"));
    }

    [Fact]
    public void Info_fields_write_escaped_strings_add_after_name_and_remove_cleanly()
    {
        const string multi = "effect({\n  name: \"x\",\n  render() {},\n});";
        // 따옴표·역슬래시는 이스케이프하고 한글은 그대로 둔다
        var named = ScriptDeclarationEdit.SetString(multi, "name", "빛 \"반짝\" \\ 효과")!;
        Assert.Equal("effect({\n  name: \"빛 \\\"반짝\\\" \\\\ 효과\",\n  render() {},\n});", named);
        Assert.Equal("빛 \"반짝\" \\ 효과", ScriptEffectProgram.Parse(named).Program!.Name);

        var described = ScriptDeclarationEdit.SetString(multi, "description", "설명")!;
        Assert.Equal("effect({\n  name: \"x\",\n  description: \"설명\",\n  render() {},\n});", described);
        Assert.Equal("설명", ScriptEffectProgram.Parse(described).Program!.Description);
        // 지우면 그 줄째로 빠져 원래 원문이 된다; 한 줄 선언은 뒤 쉼표까지
        Assert.Equal(multi, ScriptDeclarationEdit.SetString(described, "description", null));
        Assert.Equal("effect({ name: 'x', render() {} });", ScriptDeclarationEdit.SetString("effect({ name: 'x', description: 'd', render() {} });", "description", null));
        Assert.Equal(multi, ScriptDeclarationEdit.SetString(multi, "description", null));

        // name이 없으면 선언 맨 앞에 넣는다(여러 줄이면 첫 속성과 같은 들여쓰기)
        Assert.Equal("effect({\n  name: \"n\",\n  render() {},\n});", ScriptDeclarationEdit.SetString("effect({\n  render() {},\n});", "name", "n"));
        Assert.Equal("effect({ name: \"n\", render() {} });", ScriptDeclarationEdit.SetString("effect({ render() {} });", "name", "n"));
        Assert.True(ScriptDeclarationEdit.HasDeclaration("effect({ name: 1, render() {} });"));
        Assert.False(ScriptDeclarationEdit.HasDeclaration("effect({ name: 'x', render( {} });"));
    }

    [Fact]
    public void Description_must_be_a_short_string()
    {
        Assert.Null(ScriptEffectProgram.Parse("effect({ name: 'x', render() {} });").Program!.Description);
        var wrong = ScriptEffectProgram.Parse("effect({ name: 'x', description: 3, render() {} });");
        Assert.Contains(wrong.Errors, e => e.Kind == ScriptErrorKind.Declaration && e.Line == 1);
        string max = new('a', ScriptEffectProgram.MaxDescriptionLength);
        Assert.True(ScriptEffectProgram.Parse($"effect({{ name: 'x', description: '{max}', render() {{}} }});").Success);
        Assert.False(ScriptEffectProgram.Parse($"effect({{ name: 'x', description: '{max}a', render() {{}} }});").Success);
    }

    private const string Settings = """
        effect({
          name: "x",
          settings: [
            { key: "sizeScale", label: "Z", type: "number", min: 0, max: 2, step: 0.1, default: 1 },
            { key: "size", label: "S", type: "int", min: 0, max: 10, default: 3 },
            { key: "on", label: "O", type: "bool", default: false },
            { key: "tint", label: "T", type: "color", default: "#ff0000" },
            { key: "mode", label: "M", type: "choice", choices: ["a", "b"], default: 'a' },
          ],
          render(img, ctx, s) {},
        });
        """;

    [Fact]
    public void Setting_default_is_replaced_in_place_for_every_type()
    {
        // 이름이 비슷한 키(size·sizeScale)는 헷갈리지 않고, 고친 값 말고는 글자 그대로
        Assert.Equal(Settings.Replace("max: 10, default: 3", "max: 10, default: 7"), ScriptDeclarationEdit.SetSettingDefault(Settings, "size", 7));
        Assert.Equal(Settings.Replace("step: 0.1, default: 1 }", "step: 0.1, default: 1.5 }"), ScriptDeclarationEdit.SetSettingDefault(Settings, "sizeScale", 1.5));
        Assert.Equal(Settings.Replace("default: false", "default: true"), ScriptDeclarationEdit.SetSettingDefault(Settings, "on", true));
        Assert.Equal(Settings.Replace("\"#ff0000\"", "\"#0080ff\""), ScriptDeclarationEdit.SetSettingDefault(Settings, "tint", new Color32(0, 128, 255)));
        // 대문자 16진수는 대문자로, 알파가 있으면 8자리; 작은따옴표 문자열은 작은따옴표로
        Assert.Equal(Settings.Replace("\"#ff0000\"", "\"#0080FF80\""),
            ScriptDeclarationEdit.SetSettingDefault(Settings.Replace("#ff0000", "#FF0000"), "tint", new Color32(0, 128, 255, 128)));
        Assert.Equal(Settings.Replace("default: 'a'", "default: 'b'"), ScriptDeclarationEdit.SetSettingDefault(Settings, "mode", "b"));

        // 여러 키를 한 번에 쓰면 해석한 기본값이 그 값이 된다
        var edited = ScriptDeclarationEdit.SetSettingDefaults(Settings, new Dictionary<string, object>
        {
            ["sizeScale"] = 0.3, ["size"] = 10, ["on"] = true, ["tint"] = new Color32(1, 2, 3), ["mode"] = "b",
        })!;
        var defaults = ScriptEffectProgram.Parse(edited).Program!.Settings.ToDictionary(s => s.Key, s => s.Default);
        Assert.Equal(0.3, defaults["sizeScale"]);
        Assert.Equal(10, defaults["size"]);
        Assert.Equal(true, defaults["on"]);
        Assert.Equal(new Color32(1, 2, 3), defaults["tint"]);
        Assert.Equal("b", defaults["mode"]);
    }

    [Fact]
    public void Missing_setting_default_is_added_after_the_last_property()
    {
        // 한 줄 항목: 같은 줄에
        Assert.Equal("effect({ name: 'x', settings: [{ key: 'a', type: 'int', min: 0, max: 5, default: 2 }], render() {} });",
            ScriptDeclarationEdit.SetSettingDefault("effect({ name: 'x', settings: [{ key: 'a', type: 'int', min: 0, max: 5 }], render() {} });", "a", 2));
        // 여러 줄 항목: 같은 들여쓰기의 새 줄로(뒤 쉼표가 있으면 새 속성 뒤에도)
        const string trailing = "effect({\n  name: 'x',\n  settings: [\n    {\n      key: 'b',\n      type: 'bool',\n    },\n  ],\n  render() {},\n});";
        var added = ScriptDeclarationEdit.SetSettingDefault(trailing, "b", true)!;
        Assert.Equal(trailing.Replace("      type: 'bool',\n", "      type: 'bool',\n      default: true,\n"), added);
        Assert.Equal(true, ScriptEffectProgram.Parse(added).Program!.Settings[0].Default);
        const string bare = "effect({\n  name: 'x',\n  settings: [\n    {\n      key: 'b',\n      type: 'bool'\n    }\n  ],\n  render() {},\n});";
        Assert.Equal(bare.Replace("      type: 'bool'\n", "      type: 'bool',\n      default: true\n"), ScriptDeclarationEdit.SetSettingDefault(bare, "b", true));

        // 그런 항목이 없거나, 원문이 깨졌거나, 쓸 수 없는 값이면 null
        Assert.Null(ScriptDeclarationEdit.SetSettingDefault(Settings, "siz", 1));
        Assert.Null(ScriptDeclarationEdit.SetSettingDefault("effect({ name: 'x', render() {} });", "a", 1));
        Assert.Null(ScriptDeclarationEdit.SetSettingDefault(Settings.Replace("render(img", "render(img("), "size", 1));
        Assert.Null(ScriptDeclarationEdit.SetSettingDefault(Settings, "sizeScale", double.NaN));
        Assert.Null(ScriptDeclarationEdit.SetSettingDefaults(Settings, new Dictionary<string, object> { ["size"] = 1, ["nope"] = 2 }));
    }

    [Fact]
    public void Preview_frames_are_read_checked_and_written_after_the_chick()
    {
        const string body = "render(img, ctx, s) {} });";
        Assert.Equal(AssetPreview.PackageFrames, ScriptEffectProgram.Parse("effect({ name: 'x', " + body).Program!.PreviewFrames);
        var twenty = ScriptEffectProgram.Parse("effect({ name: 'x', previewFrames: 20, " + body).Program!;
        Assert.Equal((20, 20), (twenty.PreviewFrames, ScriptAsset.From("", twenty).PreviewFrames));
        foreach (var wrong in new[] { "1", "21", "2.5", "'10'" })
            Assert.Contains(ScriptEffectProgram.Parse("effect({ name: 'x', previewFrames: " + wrong + ", " + body).Errors,
                e => e.Kind == ScriptErrorKind.Declaration && e.Line == 1);

        Assert.Equal("effect({ name: 'x', previewChick: 'knight', previewFrames: 20, render() {} });",
            ScriptDeclarationEdit.SetPreviewFrames("effect({ name: 'x', previewChick: 'knight', render() {} });", 20));
        Assert.Equal("effect({\n  name: 'x',\n  previewFrames: 12,\n  render() {},\n});",
            ScriptDeclarationEdit.SetPreviewFrames("effect({\n  name: 'x',\n  render() {},\n});", 12));
        Assert.Equal("effect({ name: 'x', previewFrames: 4, render() {} });",
            ScriptDeclarationEdit.SetPreviewFrames("effect({ name: 'x', previewFrames: 20, render() {} });", 4));
        Assert.Equal("effect({ name: 'x', render() {} });", ScriptDeclarationEdit.SetNumber("effect({ name: 'x', previewFrames: 20, render() {} });", "previewFrames", null));
        Assert.Equal("effect({ name: 'x', radius: 2.5, render() {} });", ScriptDeclarationEdit.SetNumber("effect({ name: 'x', render() {} });", "radius", 2.5));
    }
}
