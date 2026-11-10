using System.Drawing;
using System.Runtime.CompilerServices;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 선언의 <c>expandCanvas</c>: 새로 넣는 효과의 "확인할 때 캔버스 늘리기"(<see cref="LayerEffect.ExpandCanvas"/>) 처음 값 — 읽기·검사, 새 효과(효과 창 상자의 레시피 포함),
/// 효과 만들기 "기본값으로"의 선언 쓰기·지우기, 미리보기에서 잘릴 캔버스 자리.
/// </summary>
public class ScriptExpandCanvasDeclarationTests
{
    private const string Body = "render(img, ctx, s) {} });";

    private static string Unique(string source, [CallerMemberName] string test = "") => source + "\n// " + test + "\n";

    private static ScriptEffectProgram Program(string declaration, [CallerMemberName] string test = "")
        => ScriptEffectProgram.Parse(Unique("effect({ name: 'x', " + declaration + Body, test)).Program!;

    [Fact]
    public void Declaration_DefaultsToTrue_AndReadsTrueFalse()
    {
        Assert.True(Program("").ExpandCanvas);
        Assert.True(Program("expandCanvas: true, ").ExpandCanvas);
        Assert.False(Program("expandCanvas: false, ").ExpandCanvas);
    }

    [Theory]
    [InlineData("expandCanvas: 0")]
    [InlineData("expandCanvas: 'false'")]
    [InlineData("expandCanvas: null")]
    public void InvalidDeclaration_IsReported(string declaration)
    {
        var result = ScriptEffectProgram.Parse("effect({ name: 'x', " + declaration + ", " + Body);
        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ScriptErrorKind.Declaration, error.Kind);
        Assert.Contains("expandCanvas", error.Message);
    }

    [Fact]
    public void NewEffects_StartWithDeclaredValue()
    {
        var off = Program("expandCanvas: false, ");
        Assert.False(off.CreateEffect().ExpandCanvas);
        Assert.True(Program("animated: true, ").CreateEffect().ExpandCanvas);

        // 효과 창 상자·AI가 넣는 길: 스크립트 에셋 레시피를 프로젝트에 넣은 효과
        var added = ScriptAsset.From("personal.fire", off).ToRecipe().Instantiate(new SpriteProject(4, 4), null);
        Assert.False(Assert.Single(added).ExpandCanvas);
        Assert.True(Assert.Single(ScriptAsset.From("personal.plain", Program("")).ToRecipe().Instantiate(new SpriteProject(4, 4), null)).ExpandCanvas);
    }

    [Fact]
    public void SetEffectDefaults_WritesFalse_AndRemovesTrue()
    {
        Assert.Equal("effect({ name: 'x', movePivot: false, expandCanvas: false, render() {} });",
            ScriptDeclarationEdit.SetEffectDefaults("effect({ name: 'x', movePivot: false, render() {} });", expandCanvas: false));
        Assert.Equal("effect({ name: 'x', render() {} });",
            ScriptDeclarationEdit.SetEffectDefaults("effect({ name: 'x', expandCanvas: false, render() {} });", expandCanvas: true));
        Assert.Equal("effect({\n  name: 'x',\n  expandCanvas: false,\n  render() {},\n});",
            ScriptDeclarationEdit.SetEffectDefaults("effect({\n  name: 'x',\n  render() {},\n});", expandCanvas: false));
        // 값을 주지 않으면 그대로
        Assert.Equal("effect({ name: 'x', expandCanvas: false, render() {} });",
            ScriptDeclarationEdit.SetEffectDefaults("effect({ name: 'x', expandCanvas: false, render() {} });", movePivot: true));
        Assert.Null(ScriptDeclarationEdit.SetEffectDefaults("effect({ name: 'x', render() {", expandCanvas: false));
        // 쓴 원문은 다시 읽으면 그 값
        Assert.False(ScriptEffectProgram.Parse(Unique(ScriptDeclarationEdit.SetEffectDefaults("effect({ name: 'x', " + Body, expandCanvas: false)!)).Program!.ExpandCanvas);
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
