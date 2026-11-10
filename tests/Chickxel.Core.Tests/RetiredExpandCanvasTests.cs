using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 없앤 "확인할 때 캔버스 늘리기"의 예전 키(선언 <c>expandCanvas</c>, 효과·레시피 JSON <c>expandCanvas</c>)는 무시하고 그대로 읽는다.
/// 효과 만들기 미리보기에서 잘릴 캔버스 자리.
/// </summary>
public class RetiredExpandCanvasTests
{
    private const string Body = "render(img, ctx, s) {} });";

    private static string Unique(string source, [CallerMemberName] string test = "") => source + "\n// " + test + "\n";

    [Theory]
    [InlineData("expandCanvas: false")]
    [InlineData("expandCanvas: true")]
    [InlineData("expandCanvas: 'false'")]
    [InlineData("expandCanvas: null")]
    public void RetiredDeclaration_IsIgnored(string declaration)
    {
        var result = ScriptEffectProgram.Parse(Unique("effect({ name: 'x', " + declaration + ", " + Body, declaration));
        Assert.True(result.Success);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RetiredRecipeKey_PassesStrictCheck_AndIsNotWrittenAgain()
    {
        var json = EffectRecipeFormat.Write(new EffectRecipe { Id = "personal.a", Name = "Outline", Effects = [new OutlineEffect()] });
        json["effects"]![0]![EffectCodec.RetiredExpandCanvasKey] = false;
        var context = new AssetReadContext("recipe.json", strict: true);
        var recipe = EffectRecipeFormat.Read(JsonNode.Parse(json.ToJsonString()), context);
        Assert.Empty(context.Issues);
        Assert.Single(recipe!.Effects);
        Assert.Null(EffectRecipeFormat.Write(recipe)["effects"]![0]![EffectCodec.RetiredExpandCanvasKey]);
    }

    [Fact]
    public void PreviewCanvas_IsSourceInsidePadding()
    {
        var program = EffectParity.Trusted(Unique("effect({ name: 'up', padding: [1, 3, 2, 0], " + Body));
        IReadOnlyList<LayerEffect> effects = [program.CreateEffect()];
        var chick = new PixelImage(5, 4);
        var canvas = AssetPreview.Canvas(effects, chick);
        Assert.Equal(new Rectangle(1, 3, 5, 4), canvas);
        var rendered = AssetPreview.Render(effects, chick, 0);
        Assert.Equal((8, 7), (rendered.Width, rendered.Height));
    }
}
