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
}
