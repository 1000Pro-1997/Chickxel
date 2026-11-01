using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 기본 효과를 옮기는 데 필요한 스크립트 엔진 기능: 색 설정 그라데이션(<c>ctx.color</c>·<c>ctx.colorAt</c>·<c>ctx.pairColor</c>·<c>ctx.hasGradient</c>),
/// 용도별 해시, Bayer 문턱값, 색 목록·색 쌍 목록 설정, 위젯 선언, 미루는 효과(<c>deferred</c>)와 <c>ctx.renderedFrameSource</c>. 빠른 경로와 Jint를 함께 본다.
/// </summary>
public class ScriptEngineFeatureTests
{
    private static string Unique(string source, [CallerMemberName] string test = "") => source + "\n// " + test + "\n";

    private static ScriptEffectProgram Parse(string source)
    {
        var result = ScriptEffectProgram.Parse(source);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return result.Program;
    }

    // 두 길로 그린 결과(같아야 함)
    private static PixelImage RenderBoth(ScriptEffectProgram program, PixelImage image, Rectangle source, ScriptFrameContext frame,
        IReadOnlyDictionary<string, object>? settings = null, bool expectFast = true)
    {
        Assert.Equal(expectFast, program.ExecutionPath.Compiled);
        var fast = image.Clone();
        program.Render(fast, source, frame, settings);
        var jint = image.Clone();
        program.RenderOnJint(jint, source, frame, program.NormalizeSettings(settings), CancellationToken.None);
        Assert.True(fast.ContentEquals(jint), "fast != Jint");
        return fast;
    }

    /// <summary>가운데만 불투명한 그림(불투명 범위가 그림 전체와 다르게).</summary>
    private static PixelImage Sprite(int w = 14, int h = 11)
    {
        var image = new PixelImage(w, h);
        for (int y = 2; y < h - 1; y++)
            for (int x = 3; x < w - 2; x++)
                if ((x + y) % 7 != 0) image[x, y] = new Color32((byte)(x * 17), (byte)(y * 21), 90, (byte)(x % 3 == 0 ? 120 : 255));
        return image;
    }

    private static readonly Rectangle Source = new(1, 1, 12, 9);

    // ───────────── 그라데이션 ─────────────

    private const string ColorScript = """
        effect({
          name: "g",
          settings: [
            { key: "c", type: "color", default: "#336699c0" },
            { key: "list", type: "colorList", default: ["#ff0000", "#00ff00", "#0000ff"] },
            { key: "pairs", type: "colorPairs", default: [{ from: "#101010", to: "#202020" }, { from: "#303030", to: "#40404080" }] },
          ],
          render(img, ctx, s) {
            const w = img.width, h = img.height, d = img.data;
            for (let y = 0; y < h; y++) {
              for (let x = 0; x < w; x++) {
                const i = (y * w + x) * 4;
                let r = 0, g = 0, b = 0, a = 0;
                if (MODE === 0) { const c = ctx.color("c", x, y); r = c.r; g = c.g; b = c.b; a = c.a; }
                if (MODE === 1) { const c = ctx.color("c", x, y, { x: 2, y: 1, w: 7, h: 5 }); r = c.r; g = c.g; b = c.b; a = c.a; }
                if (MODE === 2) { const c = ctx.color("c", x, y, ctx.source); r = c.r; g = c.g; b = c.b; a = c.a; }
                if (MODE === 3) { const c = ctx.colorAt("list", s.list.length - 2, x, y); r = c.r; g = c.g; b = c.b; a = c.a; }
                if (MODE === 4) { const c = ctx.pairColor("pairs", 1, "to", x, y); r = c.r; g = c.g; b = c.b; a = c.a; }
                if (MODE === 5) { const c = ctx.pairColor("pairs", 0, "from", x, y, { w: 5, h: 9, x: -3, y: 4 }); r = c.r; g = c.g; b = c.b; a = c.a; }
                d[i] = r; d[i + 1] = g; d[i + 2] = b; d[i + 3] = a;
              }
            }
          },
        });
        """;

    [Theory]
    [InlineData(0, "c", -1)]
    [InlineData(1, "c", -1)]
    [InlineData(2, "c", -1)]
    [InlineData(3, "list[1]", 1)]
    [InlineData(4, "pairs[1].to", 1)]
    [InlineData(5, "pairs[0].from", 0)]
    public void Color_sampling_matches_the_built_in_gradient_sampler(int mode, string slot, int item)
    {
        var program = Parse(Unique(ColorScript.Replace("MODE", mode.ToString(System.Globalization.CultureInfo.InvariantCulture)), $"mode{mode}"));
        var values = program.NormalizeSettings(null);
        var solid = mode switch
        {
            3 => ((IReadOnlyList<Color32>)values["list"])[item],
            4 => ((IReadOnlyList<ScriptColorPair>)values["pairs"])[item].To,
            5 => ((IReadOnlyList<ScriptColorPair>)values["pairs"])[item].From,
            _ => (Color32)values["c"],
        };
        var image = Sprite();
        var bounds = mode switch { 1 => new Rectangle(2, 1, 7, 5), 2 => Source, 5 => new Rectangle(-3, 4, 5, 9), _ => image.GetOpaqueBounds() };
        foreach (var gradient in EffectParity.Gradients.Prepend(null))
            foreach (double time in new[] { 0, 0.37, 1 })
            {
                var frame = new ScriptFrameContext
                {
                    GradientTime = time,
                    Gradients = gradient is null ? new Dictionary<string, EffectColorGradient>() : new Dictionary<string, EffectColorGradient> { [slot] = gradient },
                };
                var result = RenderBoth(program, image, Source, frame);
                var sampler = EffectGradientSampler.For(gradient, bounds, solid, Source.X, Source.Y, time);
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                        Assert.Equal(sampler.Sample(x, y), result[x, y]);
            }
    }

    [Fact]
    public void HasGradient_reads_every_slot_kind_and_rejects_unknown_slots()
    {
        var program = Parse(Unique("""
            effect({
              name: "h",
              settings: [
                { key: "c", type: "color" },
                { key: "list", type: "colorList", default: ["#ff0000", "#00ff00"] },
                { key: "pairs", type: "colorPairs", default: [{ from: "#101010", to: "#202020" }] },
              ],
              render(img, ctx, s) {
                const d = img.data;
                d[0] = ctx.hasGradient("c") ? 1 : 0;
                d[1] = ctx.hasGradient("list[1]") ? 1 : 0;
                d[2] = ctx.hasGradient("pairs[0].to") ? 1 : 0;
                d[3] = ctx.hasGradient("list[5]") ? 1 : 0;
                for (let j = 0; j < 3; j++) d[4 + j] = (ctx.hasGradient("list", j) ? 1 : 0) + (ctx.hasGradient("pairs", j - 1, "to") ? 2 : 0);
                d[7] = ctx.hasGradient("list", 0.5) ? 1 : 0;
              },
            });
            """));
        var g = new EffectColorGradient();
        var frame = new ScriptFrameContext
        {
            Gradients = new Dictionary<string, EffectColorGradient> { ["c"] = g, ["pairs[0].to"] = g, ["list[5]"] = g },
        };
        var result = RenderBoth(program, new PixelImage(2, 1), new Rectangle(0, 0, 2, 1), frame);
        Assert.Equal(new Color32(1, 0, 1, 0), result[0, 0]); // 목록에 없는 항목(list[5])은 그라데이션이 있어도 false
        Assert.Equal(new Color32(0, 2, 0, 0), result[1, 0]); // 번호로: list[0..2]에는 없음, pairs[0].to만 있음, 정수가 아니면 false

        foreach (var bad in new[] { "ctx.hasGradient('nope')", "ctx.hasGradient('list')", "ctx.hasGradient('pairs[0]')", "ctx.colorAt('list', 2, 0, 0)",
                     "ctx.colorAt('c', 0, 0, 0)", "ctx.pairColor('pairs', 0, 'mid', 0, 0)", "ctx.color('c', 0, 0, 5)", "ctx.colorAt('list', 0.5, 0, 0)" })
        {
            var broken = Parse(Unique($$"""
                effect({ name: "b", settings: [{ key: "c", type: "color" }, { key: "list", type: "colorList", default: ["#ff0000", "#00ff00"] },
                  { key: "pairs", type: "colorPairs", default: [{ from: "#101010", to: "#202020" }] }],
                  render(img, ctx, s) { img.data[0] = {{bad}}.r; } });
                """, bad));
            var image = new PixelImage(1, 1);
            Assert.Throws<ScriptEffectException>(() => broken.Render(image, new Rectangle(0, 0, 1, 1), ScriptFrameContext.Still));
        }
    }

    // ───────────── 해시·Bayer ─────────────

    [Fact]
    public void Salted_hash_and_bayer_match_the_built_in_helpers()
    {
        var program = Parse(Unique("""
            effect({
              name: "r",
              settings: [{ key: "salt", type: "int", min: -100, max: 100, default: 0 }],
              render(img, ctx, s) {
                const w = img.width, d = img.data;
                for (let y = 0; y < img.height; y++) {
                  for (let x = 0; x < w; x++) {
                    const i = (y * w + x) * 4;
                    const v = Math.floor((s.salt === 0 ? ctx.hash(x - 4, y - 3, 1234) : ctx.hash(x - 4, y - 3, 1234, s.salt)) * 4294967296);
                    d[i] = v >>> 24; d[i + 1] = (v >>> 16) & 255; d[i + 2] = (v >>> 8) & 255; d[i + 3] = v & 255;
                  }
                }
              },
            });
            """));
        foreach (int salt in new[] { 0, 7, 21, 41, 44, -3 })
        {
            var result = RenderBoth(program, new PixelImage(9, 7), new Rectangle(0, 0, 9, 7), ScriptFrameContext.Still,
                new Dictionary<string, object> { ["salt"] = salt });
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 9; x++)
                {
                    var c = result[x, y];
                    uint bits = (uint)(c.R << 24 | c.G << 16 | c.B << 8 | c.A);
                    Assert.Equal((uint)(LayerEffects.Random(1234, x - 4, y - 3, salt) * 4294967296.0), bits);
                }
        }

        var bayer = Parse(Unique("""
            effect({ name: "b", render(img, ctx, s) {
              const w = img.width, d = img.data;
              for (let y = 0; y < img.height; y++)
                for (let x = 0; x < w; x++) { const i = (y * w + x) * 4; d[i] = ctx.bayer2(x - 5, y - 6); d[i + 1] = ctx.bayer4(x - 5, y - 6); d[i + 3] = 255; }
            } });
            """));
        var dither = RenderBoth(bayer, new PixelImage(11, 9), new Rectangle(0, 0, 11, 9), ScriptFrameContext.Still);
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 11; x++)
                Assert.Equal((Bayer.Threshold2(x - 5, y - 6), Bayer.Threshold4(x - 5, y - 6)), (dither[x, y].R, dither[x, y].G));
    }

    // ───────────── 목록 설정·위젯 ─────────────

    private const string ListDeclaration = """
        effect({
          name: "l",
          easing: "EaseOut",
          settings: [
            { key: "colors", type: "colorList", min: 2, max: 4, alpha: false, default: ["#2850C8", "#4696F080", "#AAE6FF"] },
            { key: "pairs", type: "colorPairs", default: [{ from: "#000000", to: "#ffffff80" }] },
            { key: "angle", type: "int", min: 0, max: 359, widget: "angle" },
            { key: "cx", type: "int", widget: "point", pair: "cy" },
            { key: "cy", type: "int" },
          ],
          render(img, ctx, s) {
            const d = img.data;
            d[0] = s.colors.length; d[1] = s.pairs.length; d[2] = s.colors[1].a; d[3] = s.pairs[0].to.a;
            d[4] = ctx.colorAt("colors", s.colors.length - 1, 0, 0).g; d[5] = ctx.pairColor("pairs", 0, "to", 0, 0).b;
          },
        });
        """;

    [Fact]
    public void List_settings_parse_normalize_reach_render_and_persist()
    {
        var program = Parse(Unique(ListDeclaration));
        Assert.Equal(EffectEasing.EaseOut, program.Easing);
        var colors = program.Settings[0];
        Assert.Equal((ScriptSettingType.ColorList, 2.0, 4.0, false), (colors.Type, colors.Min, colors.Max, colors.Alpha));
        Assert.Equal([new Color32(0x28, 0x50, 0xC8), new Color32(0x46, 0x96, 0xF0), new Color32(0xAA, 0xE6, 0xFF)], (IReadOnlyList<Color32>)colors.Default);
        Assert.Equal((0.0, (double)ScriptSetting.DefaultMaxListCount), (program.Settings[1].Min, program.Settings[1].Max));
        Assert.Equal([new ScriptColorPair(Color32.Black, new Color32(255, 255, 255, 128))], (IReadOnlyList<ScriptColorPair>)program.Settings[1].Default);
        Assert.Equal((ScriptSettingWidget.Angle, (string?)null), (program.Settings[2].Widget, program.Settings[2].Pair));
        Assert.Equal((ScriptSettingWidget.Point, "cy"), (program.Settings[3].Widget, program.Settings[3].Pair));

        // 정규화: 너무 많으면 앞에서부터 max개, 너무 적거나 틀린 항목이 있으면 기본값, alpha: false는 불투명으로
        IReadOnlyList<Color32> Colors(object value) => (IReadOnlyList<Color32>)program.NormalizeSettings(new Dictionary<string, object> { ["colors"] = value })["colors"];
        var five = Enumerable.Range(1, 5).Select(i => new Color32((byte)i, 0, 0, 10)).ToArray();
        Assert.Equal(five[..4].Select(c => c.WithAlpha(255)), Colors(five));
        Assert.Equal((IReadOnlyList<Color32>)colors.Default, Colors(new[] { Color32.White }));
        Assert.Equal((IReadOnlyList<Color32>)colors.Default, Colors(new ScriptRawValue("[\"#ffffff\", \"nope\"]")));
        Assert.Equal([Color32.White, Color32.Black], Colors(new ScriptRawValue("[\"#ffffff00\", \"#000000\"]")));
        var pairs = (IReadOnlyList<ScriptColorPair>)program.NormalizeSettings(new Dictionary<string, object>
            { ["pairs"] = new ScriptRawValue("[[\"#ff0000\", \"#00ff0080\"], { \"from\": \"#0000ff\", \"to\": \"#ffffff\" }]") })["pairs"];
        Assert.Equal([new ScriptColorPair(new Color32(255, 0, 0), new Color32(0, 255, 0, 128)), new ScriptColorPair(new Color32(0, 0, 255), Color32.White)], pairs);

        // render가 받는 값(s.키 배열은 Jint, ctx.colorAt·ctx.pairColor는 빠른 경로도 되지만 s.colors[i]가 있어 통째로 Jint)
        var image = RenderBoth(program, new PixelImage(2, 1), new Rectangle(0, 0, 2, 1), ScriptFrameContext.Still,
            new Dictionary<string, object> { ["colors"] = five[..2], ["pairs"] = new[] { new ScriptColorPair(Color32.Black, new Color32(1, 2, 3, 4)) } }, expectFast: false);
        Assert.Equal(new Color32(2, 1, 255, 4), image[0, 0]);
        Assert.Equal(new Color32(0, 3, 0, 0), image[1, 0]);

        // 저장: 효과 JSON의 설정은 기본 효과의 목록 모양 그대로(["#…"], [["#…", "#…"]]), 다시 읽으면 같은 값
        var hash = ScriptLibrary.Add(program.Source);
        ScriptLibrary.SetTrust(hash, true);
        var effect = program.CreateEffect(new Dictionary<string, object> { ["colors"] = five[..3], ["pairs"] = pairs }) with
        {
            ColorGradients = new Dictionary<string, EffectColorGradient> { ["colors[2]"] = EffectParity.Gradients[1], ["pairs[1].to"] = EffectParity.Gradients[0] },
        };
        Assert.Equal(EffectEasing.EaseOut, effect.Easing);
        var json = EffectCodec.Write(effect);
        var settings = (JsonObject)json[EffectCodec.SettingsKey]!;
        Assert.Equal("[\"#010000\",\"#020000\",\"#030000\"]", settings["colors"]!.ToJsonString());
        Assert.Equal("[[\"#FF0000\",\"#00FF0080\"],[\"#0000FF\",\"#FFFFFF\"]]", settings["pairs"]!.ToJsonString());
        var read = (ScriptEffect)EffectCodec.Read((JsonObject)JsonNode.Parse(json.ToJsonString())!);
        Assert.Equal(effect.Settings, read.Settings);
        Assert.Equal(effect.ColorGradients.OrderBy(e => e.Key), read.ColorGradients.OrderBy(e => e.Key));
        Assert.Equal(five[..3].Select(c => c.WithAlpha(255)), (IReadOnlyList<Color32>)program.NormalizeSettings(read.Settings)["colors"]);
    }

    [Theory]
    [InlineData("{ key: 'l', type: 'colorList', min: 3, max: 2 }", "max")]
    [InlineData("{ key: 'l', type: 'colorList', max: 65 }", "max")]
    [InlineData("{ key: 'l', type: 'colorList', min: 1.5 }", "min")]
    [InlineData("{ key: 'l', type: 'colorList', min: 2, default: ['#ffffff'] }", "default")]
    [InlineData("{ key: 'l', type: 'colorList', default: ['#ffffff', 'red?'] }", "default")]
    [InlineData("{ key: 'p', type: 'colorPairs', default: [['#ffffff', '#000000']] }", "default")]
    [InlineData("{ key: 'n', type: 'int', alpha: false }", "alpha")]
    [InlineData("{ key: 'c', type: 'color', alpha: 1 }", "alpha")]
    [InlineData("{ key: 'n', type: 'number', widget: 'angle' }", "widget")]
    [InlineData("{ key: 'n', type: 'int', widget: 'dial' }", "widget")]
    [InlineData("{ key: 'n', type: 'int', widget: 'point' }", "widget")]
    [InlineData("{ key: 'n', type: 'int', widget: 'point', pair: 'n' }", "widget")]
    [InlineData("{ key: 'n', type: 'int', widget: 'point', pair: 'missing' }", "pair")]
    public void Invalid_list_and_widget_declarations_are_reported(string setting, string field)
    {
        var result = ScriptEffectProgram.Parse($"effect({{ name: 'x', settings: [{setting}], render(img, ctx, s) {{}} }});");
        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ScriptErrorKind.Declaration, error.Kind);
        Assert.Contains(field, error.Message);
    }

    [Fact]
    public void List_defaults_can_be_written_back_to_the_declaration()
    {
        const string source = "effect({ name: 'x', settings: [{ key: 'l', type: 'colorList', default: ['#FF0000'] }, { key: 'p', type: 'colorPairs' }], render() {} });";
        Color32[] colors = [new(1, 2, 3), new(255, 255, 255, 128)];
        ScriptColorPair[] pairs = [new(Color32.Black, Color32.White)];
        var edited = ScriptDeclarationEdit.SetSettingDefaults(source,
            [new KeyValuePair<string, object>("l", colors), new KeyValuePair<string, object>("p", pairs)])!;
        Assert.Contains("default: ['#010203', '#FFFFFF80']", edited);
        var program = Parse(edited);
        Assert.Equal(colors, (IReadOnlyList<Color32>)program.Settings[0].Default);
        Assert.Equal(pairs, (IReadOnlyList<ScriptColorPair>)program.Settings[1].Default);
    }

    [Fact]
    public void Render_limits_grow_with_the_image_but_runaway_loops_still_stop()
    {
        var limits = ScriptLimits.Default;
        var big = limits.ForPixels(1024L * 1024);
        Assert.Equal(limits.MaxStatements + 500L * 1024 * 1024, big.MaxStatements);
        Assert.Equal(limits.MemoryBytes + (16L << 10) * 1024 * 1024, big.MemoryBytes);
        Assert.True(big.Timeout > limits.Timeout);
        // 최대 그림(16384²)에서도 long으로 넘치지 않는다
        Assert.Equal(limits.MaxStatements + 500L * 16384 * 16384, limits.ForPixels(16384L * 16384).MaxStatements);

        // 바탕 한도(5천만 문장)로는 끝나지 않는 큰 그림의 공식 효과도 끝까지 그린다(두 길 같음)
        var program = EffectParity.Official("innerglow");
        var image = new PixelImage(1536, 1536);
        for (int y = 16; y < image.Height - 16; y++)
            for (int x = 16; x < image.Width - 16; x++)
                if ((x * 7 + y * 3) % 11 != 0) image[x, y] = new Color32((byte)x, (byte)y, 90);
        var source = new Rectangle(0, 0, image.Width, image.Height);
        var fast = image.Clone();
        program.Render(fast, source, ScriptFrameContext.Still);
        Assert.False(fast.ContentEquals(image));

        // 무한 반복은 여전히 문장 한도에서 멈춘다(작은 그림이라 빨리)
        var loop = ScriptEffectProgram.Parse(Unique("effect({ name: 'l', render(img, ctx, s) { let i = 0;\nwhile (true) { i++; } } });"),
            ScriptLimits.Default with { MaxStatements = 1_000_000 }).Program!;
        var error = Assert.Throws<ScriptEffectException>(() => loop.Render(new PixelImage(8, 8), new Rectangle(0, 0, 8, 8), ScriptFrameContext.Still));
        Assert.Equal(ScriptErrorKind.Statements, error.Error.Kind);
        using (ScriptEffectProgram.UseJintOnly())
            Assert.Equal(ScriptErrorKind.Statements, Assert.Throws<ScriptEffectException>(()
                => loop.Render(new PixelImage(8, 8), new Rectangle(0, 0, 8, 8), ScriptFrameContext.Still)).Error.Kind);
    }

    [Fact]
    public void Oklab_helpers_and_phase_match_the_app_math_on_both_paths()
    {
        var program = Parse(Unique("""
            effect({ name: "o", animated: true, render(img, ctx, s) {
              const d = img.data;
              for (let i = 0; i < d.length; i += 4) {
                const lab = rgbToOklab(d[i], d[i + 1], d[i + 2]);
                const amount = Math.fround(-0.4);
                const l = Math.min(1, Math.max(0, Math.fround(lab.l * Math.fround(1 + amount))));
                const c = oklabToRgb(l, lab.a, lab.b);
                d[i] = c.r; d[i + 1] = c.g; d[i + 2] = c.b;
              }
              d[3] = Math.round(ctx.phase * 200);
            } });
            """));
        var image = TestUtil.RandomImage(9, 7, seed: 5, opaque: true);
        var frame = new ScriptFrameContext { Phase = 0.35, Progress = 0.5, Time = 0.7 };
        var result = RenderBoth(program, image, new Rectangle(0, 0, 9, 7), frame);
        for (int i = 1; i < image.Pixels.Length; i++)
            Assert.Equal(Pixelate.OkLab.ShiftLightness(image.Pixels[i], -0.4f), result.Pixels[i]);
        Assert.Equal(70, result.Pixels[0].A);
    }

    [Fact]
    public void Deferred_and_easing_declarations_are_checked()
    {
        Assert.False(ScriptEffectProgram.Parse("effect({ name: 'x', deferred: 1, render() {} });").Success);
        Assert.False(ScriptEffectProgram.Parse("effect({ name: 'x', easing: 'easeOut', render() {} });").Success);
        Assert.False(ScriptEffectProgram.Parse("effect({ name: 'x', render() {}, settings: [{ key: 'a', type: 'int', widget: 'point', pair: 'b' }, "
                                               + "{ key: 'b', type: 'int', widget: 'angle' }] });").Success);
        var program = Parse("effect({ name: 'x', deferred: true, easing: 'EaseInOut', render() {} });");
        Assert.Equal((true, EffectEasing.EaseInOut), (program.Deferred, program.Easing));
        Assert.Equal(EffectEasing.EaseInOut, program.CreateEffect().Easing);
    }

    // ───────────── 미루는 효과·다른 프레임에 효과를 입힌 그림 ─────────────

    /// <summary>시험용 잔상(<see cref="AfterimageRender"/>)을 JS로 옮긴 것: <c>deferred</c>·<c>ctx.renderedFrameSource</c>·원본 범위 그라데이션을 쓴다.</summary>
    private const string AfterimageScript = """
        effect({
          name: "afterimage",
          animated: true,
          deferred: true,
          settings: [
            { key: "count", type: "int", min: 1, max: 8, default: 3 },
            { key: "interval", type: "int", min: 1, max: 8, default: 1 },
            { key: "opacity", type: "int", min: 1, max: 100, default: 60 },
            { key: "tint", type: "bool", default: false },
            { key: "tintColor", type: "color", default: "#5096ff" },
          ],
          render(img, ctx, s) {
            const w = img.width, h = img.height, d = img.data, src = ctx.source;
            let layer = null;
            for (let k = s.count; k >= 1; k--) {
              const back = k * s.interval;
              if (back > ctx.step) continue;
              const rendered = ctx.renderedFrameSource(ctx.frame - back);
              if (!rendered) continue;
              const opacity = Math.min(255, Math.trunc((Math.trunc(s.opacity * 255 * (s.count - k + 1) / s.count) + 50) / 100));
              if (opacity === 0) continue;
              if (!layer) layer = img.blank(w, h);
              const g = rendered.image, gw = g.width, gd = g.data;
              const ox = src.x + rendered.offsetX, oy = src.y + rendered.offsetY;
              const piece = img.blank(w, h), pd = piece.data;
              for (let gy = 0; gy < g.height; gy++) {
                const y = gy + oy;
                if (y < 0 || y >= h) continue;
                for (let gx = 0; gx < gw; gx++) {
                  const x = gx + ox;
                  if (x < 0 || x >= w) continue;
                  const i = (gy * gw + gx) * 4;
                  if (gd[i + 3] === 0) continue;
                  let r = gd[i], gg = gd[i + 1], b = gd[i + 2];
                  if (s.tint) {
                    const t = ctx.color("tintColor", x, y, src);
                    if (t.a > 0) {
                      r = Math.trunc((r * (255 - t.a) + t.r * t.a + 127) / 255);
                      gg = Math.trunc((gg * (255 - t.a) + t.g * t.a + 127) / 255);
                      b = Math.trunc((b * (255 - t.a) + t.b * t.a + 127) / 255);
                    }
                  }
                  const a = Math.trunc((gd[i + 3] * opacity + 127) / 255);
                  if (a === 0) continue;
                  const j = (y * w + x) * 4;
                  pd[j] = r; pd[j + 1] = gg; pd[j + 2] = b; pd[j + 3] = a;
                }
              }
              layer.blendOver(piece, 0, 0);
            }
            if (!layer) return;
            const ld = layer.data, under = [];
            for (let p = 0; p < w * h; p++) under.push(ld[p * 4 + 3]);
            layer.blendOver(img, 0, 0); // 지금 그림을 잔상 위에
            for (let p = 0; p < w * h; p++) {
              if (under[p] === 0) continue;
              for (let c = 0; c < 4; c++) d[p * 4 + c] = ld[p * 4 + c];
            }
          },
        });
        """;

    [Theory]
    [InlineData(3, 1, 60, false)]
    [InlineData(2, 2, 100, true)]
    public void Deferred_script_with_rendered_frames_matches_the_afterimage_pipeline(int count, int interval, int opacity, bool tint)
    {
        var program = EffectParity.Trusted(Unique(AfterimageScript, $"afterimage{count}{interval}{opacity}{tint}"));
        Assert.True(program.Deferred);
        var moving = EffectParity.Inputs.Where(i => i.Name is "moving frames" or "player" or "empty").ToList();
        var afterimage = new AfterimageEffect { Count = count, Interval = interval, Opacity = opacity, Tint = tint, TintColor = new Color32(255, 80, 40, 150) };
        EffectParity.AssertParity(afterimage, program, expectFast: false, inputs: moving);
        if (tint) EffectParity.AssertParity(afterimage, program, gradientSlot: nameof(AfterimageEffect.TintColor), expectFast: false, inputs: moving);

        // 목록 맨 앞에 두어도 다른 효과 뒤에 그린다. 클리핑된 것은 클리핑되지 않은 미루는 효과 뒤에, 기준이 없으면 그리지 않는다
        var shadow = new ShadowEffect { OffsetX = 2, OffsetY = 1 };
        var outline = new OutlineEffect { Color = new Color32(20, 200, 90) };
        IReadOnlyList<LayerEffect>[] lists =
        [
            [afterimage, outline, shadow],
            [outline, afterimage with { ClipToPrevious = true }, shadow, afterimage with { Opacity = 30 }],
            [outline with { Enabled = false }, afterimage with { ClipToPrevious = true }, shadow],
            [shadow, outline with { ClipToPrevious = true }, afterimage with { ClipToPrevious = true }],
        ];
        foreach (var list in lists)
            EffectParity.AssertSameLists(list, [.. list.Select(e => e is AfterimageEffect a ? EffectParity.ToScript(a, program) : e)], inputs: moving,
                label: string.Join(" + ", list.Select(e => e.GetType().Name)));
    }

    [Fact]
    public void Deferred_effects_bake_with_everything_and_disable_clipping_anchors()
    {
        var program = EffectParity.Trusted(Unique(AfterimageScript));
        var script = program.CreateEffect();
        IReadOnlyList<LayerEffect> effects = [new OutlineEffect(), script, new ShadowEffect()];
        Assert.True(LayerEffects.IsDeferred(script));
        Assert.False(LayerEffects.IsDeferred(EffectParity.Official("fade").CreateEffect()));
        Assert.Equal([0, 1, 2], LayerEffects.BakeSet(effects, [1]));
        Assert.True(LayerEffects.UsesOtherFrames([script]));

        // 미루지 않는 스크립트는 ctx.renderedFrameSource로 null을 받는다
        var plain = Parse(Unique("""
            effect({ name: "p", render(img, ctx, s) { img.data[0] = ctx.renderedFrameSource(0) === null ? 1 : 2; } });
            """));
        var image = new PixelImage(1, 1);
        plain.Render(image, new Rectangle(0, 0, 1, 1), new ScriptFrameContext { FrameSource = _ => new PixelImage(1, 1) });
        Assert.Equal(1, image[0, 0].R);
    }
}
