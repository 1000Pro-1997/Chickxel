using System.Drawing;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Jint;
using Jint.Native.Object;

namespace Chickxel.Core.Tests;

/// <summary>자동 완성·AI 문서가 쓰는 API 표가 실제 실행기에 있는 이름과 같은지(빠뜨리거나 없는 이름을 알려 주지 않게).</summary>
public class ScriptApiCatalogTests
{
    private static string[] Names(string scope) => ScriptApiCatalog.In(scope).Select(m => m.Name).Order(StringComparer.Ordinal).ToArray();

    private static string[] Keys(ObjectInstance obj) => obj.GetOwnProperties().Select(p => p.Key.ToString()).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Catalog_matches_the_objects_the_sandbox_hands_to_render()
    {
        var (ctx, source, pivot, image, color, hsv, math, globals) = ScriptSandbox.Run(ScriptLimits.Default, CancellationToken.None, sandbox =>
        {
            var engine = sandbox.Engine;
            var settings = ScriptHostApi.CreateSettings(engine, [], new Dictionary<string, object>());
            var ctx = ScriptHostApi.CreateContext(sandbox, [], settings, new Rectangle(0, 0, 2, 2), ScriptFrameContext.Still);
            var (img, _) = ScriptHostApi.CreateImage(sandbox, new PixelImage(2, 2));
            var color = (ObjectInstance)engine.Evaluate("hsvToRgb(0, 1, 1)");
            var hsv = (ObjectInstance)engine.Evaluate("rgbToHsv(255, 0, 0)");
            var math = (ObjectInstance)engine.Evaluate("Math");
            var globals = ScriptApiCatalog.In(ScriptApiCatalog.Global).Where(m => engine.Evaluate($"typeof {m.Name}").AsString() == "undefined").Select(m => m.Name).ToArray();
            return (Keys(ctx), Keys((ObjectInstance)ctx.Get("source")), Keys((ObjectInstance)ctx.Get("pivot")),
                Keys(img).Concat(Keys(img.Prototype!)).Order(StringComparer.Ordinal).ToArray(), Keys(color), Keys(hsv),
                Keys(math).ToHashSet(StringComparer.Ordinal), globals);
        });

        Assert.Equal(Names(ScriptApiCatalog.Context), ctx);
        Assert.Equal(Names(ScriptApiCatalog.Source), source);
        Assert.Equal(Names(ScriptApiCatalog.Pivot), pivot);
        Assert.Equal(Names(ScriptApiCatalog.Image), image);
        Assert.Equal(Names(ScriptApiCatalog.Color), color);
        Assert.Equal(Names(ScriptApiCatalog.Hsv), hsv);
        Assert.Empty(globals);
        Assert.All(Names(ScriptApiCatalog.MathScope), name => Assert.Contains(name, math));
        Assert.DoesNotContain("random", math);
    }

    [Fact]
    public void Catalog_declaration_keys_are_all_read_and_every_entry_has_both_languages()
    {
        // 표의 선언 키를 모두 쓴 원문이 선언 오류 없이 읽힌다(이름이 틀리면 읽는 쪽이 무시하거나 오류를 낸다).
        var parsed = ScriptEffectProgram.Parse("""
            effect({ name: "x", author: "a", animated: true, area: "local", radius: 2, previewChick: "knight",
              settings: [{ key: "k", label: "K", type: "number", min: 0, max: 1, step: 0.5, default: 0 },
                         { key: "c", label: "C", type: "choice", choices: [{ value: "a", label: "A" }], default: "a" }],
              padding: [1, 1, 1, 1], render(img, ctx, s) {} });
            """);
        Assert.True(parsed.Success, string.Join("\n", parsed.Errors));
        var program = parsed.Program;
        Assert.Equal(("a", true, ScriptEffectArea.Local, 2, "knight"), (program.Author, program.Animated, program.Area, program.Radius, program.PreviewChick));

        foreach (var member in ScriptApiCatalog.All)
        {
            Assert.NotEqual(member.Signature, member.EnglishSummary);
            Assert.NotEqual(member.EnglishSummary, Localization.ScriptApiStrings.ResourceManager.GetString(
                "Api_" + (member.Scope.Length == 0 ? "global" : member.Scope.Replace('.', '_')) + "_" + member.Name,
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
