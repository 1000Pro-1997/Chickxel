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
    [InlineData("ctx.|", new[] { "t", "progress", "frame", "step", "span", "source", "pivot", "color", "hash", "random", "frameSource" })]
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
    [InlineData("effect({\n  name: \"x\",\n  pre|", new[] { "previewChick" })]
    [InlineData("effect({ name: 'x', settings: [{ key: 'a', ty|", new[] { "type" })]
    [InlineData("effect({ name: 'x', settings: [{ key: 'a', type: \"co|", new[] { "color" })]
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
        var edited = ScriptDeclarationEdit.SetPreviewChick(Chickxel.Core.Scripting.ScriptTemplates.Shine, "viking")!;
        Assert.Equal("viking", ScriptEffectProgram.Parse(edited).Program!.PreviewChick);
        Assert.Null(ScriptDeclarationEdit.SetPreviewChick("effect({ name: 'x', render( {} });", "ninja"));
    }
}
