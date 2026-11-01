using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>선언 <c>tags</c>: 읽기·정리·검사, 에셋으로 넘기기, 원문 고치기(<see cref="ScriptDeclarationEdit.SetTags"/>).</summary>
public class ScriptTagTests
{
    private static ScriptParseResult Parse(string tags) => ScriptEffectProgram.Parse("effect({ name: 'x',\n" + tags + "\nrender() {} });");

    [Fact]
    public void Tags_are_trimmed_lowercased_and_lose_a_leading_hash()
    {
        var program = ScriptEffectProgram.Parse("effect({ name: 'x', tags: [' #Neon ', 'GLOW', 'pixel-art', '#2d'], render() {} });").Program!;
        Assert.Equal(["neon", "glow", "pixel-art", "2d"], program.Tags);
        Assert.Equal(["neon", "glow", "pixel-art", "2d"], ScriptAsset.From("", program).Tags);

        var plain = ScriptEffectProgram.Parse("effect({ name: 'x', render() {} });").Program!;
        Assert.Empty(plain.Tags);
        Assert.Empty(ScriptAsset.From("", plain).Tags);
        Assert.Empty(ScriptEffectProgram.Parse("effect({ name: 'x', tags: [], render() {} });").Program!.Tags);
    }

    [Theory]
    [InlineData("tags: 'neon',")]
    [InlineData("tags: { a: 'neon' },")]
    [InlineData("tags: ['neon', 3],")]
    [InlineData("tags: ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i'],")]
    [InlineData("tags: ['neon glow'],")]
    [InlineData("tags: ['ne_on'],")]
    [InlineData("tags: ['-neon'],")]
    [InlineData("tags: ['neon-'],")]
    [InlineData("tags: ['네온'],")]
    [InlineData("tags: ['#'],")]
    [InlineData("tags: [''],")]
    [InlineData("tags: ['neon', '#NEON'],")]
    public void Bad_tags_are_declaration_errors_at_their_line(string tags)
    {
        var parsed = Parse(tags);
        Assert.False(parsed.Success);
        Assert.Contains(parsed.Errors, e => e.Kind == ScriptErrorKind.Declaration && e.Line == 2);
    }

    [Fact]
    public void Tag_errors_say_what_is_wrong()
    {
        string longTag = new('a', RemoteCatalogFormat.MaxTagLength + 1);
        Assert.True(ScriptEffectProgram.Parse($"effect({{ name: 'x', tags: ['{new string('a', RemoteCatalogFormat.MaxTagLength)}'], render() {{}} }});").Success);

        Assert.Equal(DomainStrings.FormatScript_InvalidTags(max: RemoteCatalogFormat.MaxTags), Assert.Single(Parse("tags: 'neon',").Errors).Message);
        Assert.Equal(DomainStrings.FormatScript_InvalidTags(max: RemoteCatalogFormat.MaxTags), Assert.Single(Parse("tags: ['neon', true],").Errors).Message);
        Assert.Equal(DomainStrings.FormatScript_InvalidTag(tag: longTag, max: RemoteCatalogFormat.MaxTagLength),
            Assert.Single(Parse($"tags: ['{longTag}'],").Errors).Message);
        Assert.Equal(DomainStrings.FormatScript_DuplicateTag(tag: "glow"), Assert.Single(Parse("tags: ['glow', 'neon', ' #Glow'],").Errors).Message);
        // 잘못된 항목마다 그 항목 자리를 가리킨다
        var errors = Parse("tags: [\n'ok',\n'bad tag',\n'ok'],").Errors;
        Assert.Equal([(4, DomainStrings.FormatScript_InvalidTag(tag: "bad tag", max: RemoteCatalogFormat.MaxTagLength)),
                      (5, DomainStrings.FormatScript_DuplicateTag(tag: "ok"))], errors.Select(e => (e.Line, e.Message)));
    }

    [Fact]
    public void SetTags_adds_after_description_replaces_and_removes()
    {
        const string source = "effect({\n  name: \"x\",\n  description: \"d\",\n  animated: true,\n  render(img, ctx, s) {},\n});\n";
        var added = ScriptDeclarationEdit.SetTags(source, ["neon", "glow"])!;
        Assert.Equal("effect({\n  name: \"x\",\n  description: \"d\",\n  tags: [\"neon\", \"glow\"],\n  animated: true,\n  render(img, ctx, s) {},\n});\n", added);
        var program = ScriptEffectProgram.Parse(added).Program!;
        Assert.Equal(["neon", "glow"], program.Tags);
        Assert.Equal(("x", "d", true), (program.Name, program.Description, program.Animated));

        var replaced = ScriptDeclarationEdit.SetTags(added, ["pixel-art"])!;
        Assert.Equal(source.Replace("  animated", "  tags: [\"pixel-art\"],\n  animated", StringComparison.Ordinal), replaced);
        Assert.Equal(["pixel-art"], ScriptEffectProgram.Parse(replaced).Program!.Tags);

        Assert.Equal(source, ScriptDeclarationEdit.SetTags(replaced, []));
        Assert.Equal(source, ScriptDeclarationEdit.SetTags(source, []));

        // description이 없으면 name 다음, 한 줄 선언이면 같은 줄에
        Assert.Equal("effect({ name: 'x', tags: [\"a\"], render() {} });", ScriptDeclarationEdit.SetTags("effect({ name: 'x', render() {} });", ["a"]));
        // 고칠 선언이 없으면 null
        Assert.Null(ScriptDeclarationEdit.SetTags("effect({ name: 'x', render( {} });", ["a"]));
        Assert.Null(ScriptDeclarationEdit.SetTags("const a = 1;", []));
    }
}
