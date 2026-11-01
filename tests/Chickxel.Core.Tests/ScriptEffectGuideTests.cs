using System.Globalization;
using Chickxel.Core.Localization;
using Chickxel.Core.Scripting;
using Chickxel.Core.Text;

namespace Chickxel.Core.Tests;

/// <summary>효과 만들기 안내(규칙 창 복사·AI 도구)가 API 목록 전체와 지금 원문을 깨지지 않게 담는지.</summary>
public class ScriptEffectGuideTests
{
    [Theory]
    [InlineData("ko")]
    [InlineData("en")]
    public void Guide_holds_the_whole_api_and_the_current_source_as_the_last_code_block(string culture)
    {
        var saved = ScriptGuideStrings.Culture;
        ScriptGuideStrings.Culture = CultureInfo.GetCultureInfo(culture);
        try
        {
            // 원문 안의 ``` 때문에 코드 블록이 끊기면 다른 AI에 붙여 넣은 원문이 잘린다
            const string source = "// 설명 ```js 조각```\r\neffect({\n  name: \"x\",\n  render(img, ctx, s) {},\n});\n";
            string guide = ScriptEffectGuide.ToMarkdown(source);

            Assert.All(ScriptApiCatalog.All, member => Assert.Contains(member.Signature, guide, StringComparison.Ordinal));
            Assert.DoesNotMatch(@"\{[A-Za-z]+\}", guide); // 채우지 않은 자리 표시자
            var blocks = MiniMarkdown.Parse(guide);
            Assert.Equal(ScriptEffectProgram.NormalizeSource(source).TrimEnd('\n'), blocks[^1].Code);
            Assert.Equal(MdBlockKind.Code, blocks[^1].Kind);

            var withoutSource = ScriptEffectGuide.ToMarkdown(null);
            Assert.DoesNotContain("name: \"x\"", withoutSource, StringComparison.Ordinal);
            Assert.StartsWith(withoutSource.TrimEnd(), guide, StringComparison.Ordinal);
        }
        finally
        {
            ScriptGuideStrings.Culture = saved;
        }
    }

    [Theory]
    [InlineData("ko")]
    [InlineData("en")]
    public void Guide_rules_ask_for_description_and_tags_and_the_example_declares_tags(string culture)
    {
        var saved = ScriptGuideStrings.Culture;
        ScriptGuideStrings.Culture = CultureInfo.GetCultureInfo(culture);
        try
        {
            string guide = ScriptEffectGuide.ToMarkdown(null);
            // 선언 규칙과 바꿀 때 규칙 둘 다 설명·태그를 말한다
            foreach (var rule in new[] { ScriptGuideStrings.Guide_RuleDeclaration, ScriptGuideStrings.Guide_RuleLanguage })
            {
                Assert.Contains("`description`", rule, StringComparison.Ordinal);
                Assert.Contains("`tags`", rule, StringComparison.Ordinal);
            }
            Assert.Contains("`language`", ScriptGuideStrings.Guide_RuleLanguage, StringComparison.Ordinal);
            Assert.Contains("`tags`", guide, StringComparison.Ordinal);
            // 예제(공식 빛 지나가기)에 language·description·tags가 들어 있고 그대로 해석된다
            var example = MiniMarkdown.Parse(guide).Last(b => b.Kind == MdBlockKind.Code).Code;
            var program = ScriptEffectProgram.Parse(example).Program!;
            Assert.InRange(program.Tags.Count, 3, 6);
            Assert.NotNull(program.Description);
            Assert.Equal("ko", program.Language);
        }
        finally
        {
            ScriptGuideStrings.Culture = saved;
        }
    }

    [Theory]
    [InlineData("ko")]
    [InlineData("en")]
    public void Guide_explains_layer_reading_with_the_forced_tag_and_the_preview_scene_names(string culture)
    {
        var saved = ScriptGuideStrings.Culture;
        ScriptGuideStrings.Culture = CultureInfo.GetCultureInfo(culture);
        try
        {
            string guide = ScriptEffectGuide.ToMarkdown(null);
            string rule = ScriptGuideStrings.FormatGuide_RuleLayers(tag: Chickxel.Core.IO.ScriptAsset.LayerReferenceTag);
            Assert.Contains("- " + rule, guide, StringComparison.Ordinal);
            Assert.Contains("`layer-reference`", rule, StringComparison.Ordinal);
            Assert.Contains("ctx.layerByName(\"ground\")", rule, StringComparison.Ordinal);
            Assert.Contains("null", rule, StringComparison.Ordinal);
        }
        finally
        {
            ScriptGuideStrings.Culture = saved;
        }
    }
}
