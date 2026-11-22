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

    [Fact]
    public void GradientAt_picks_by_position_regardless_of_shape_and_falls_back_to_the_solid_color()
    {
        // 칸 x = 위치 x/(w-1). 줄 0: 색 "c", 줄 1: 목록 항목 "list[1]", 줄 2: 쌍 "pairs[0].to"(그라데이션 없음 → 단색, 디더 자리 생략)
        var program = Parse(Unique("""
            effect({
              name: "ga",
              settings: [
                { key: "c", type: "color", default: "#336699" },
                { key: "list", type: "colorList", default: ["#ff0000", "#00ff00"] },
                { key: "pairs", type: "colorPairs", default: [{ from: "#101010", to: "#20202080" }] },
              ],
              render(img, ctx, s) {
                const w = img.width, d = img.data;
                for (let x = 0; x < w; x++) {
                  const t = x / (w - 1);
                  const a = ctx.gradientAt("c", t, x, 0), i = x * 4;
                  d[i] = a.r; d[i + 1] = a.g; d[i + 2] = a.b; d[i + 3] = a.a;
                  const b = ctx.gradientAt("list[1]", t, x, 1), j = (w + x) * 4;
                  d[j] = b.r; d[j + 1] = b.g; d[j + 2] = b.b; d[j + 3] = b.a;
                  const c = ctx.gradientAt("pairs[0].to", t), k = (2 * w + x) * 4;
                  d[k] = c.r; d[k + 1] = c.g; d[k + 2] = c.b; d[k + 3] = c.a;
                }
              },
            });
            """));
        const int w = 9;
        var source = new Rectangle(0, 0, w, 3);
        Color32 red = new(255, 0, 0), blue = new(0, 0, 255);
        var radial = new EffectColorGradient
        {
            Stops = [new GradientStop(0, red), new GradientStop(1, blue)],
            Shape = EffectGradientShape.Radial, Angle = 90, Cycles = 3,
        };
        var reversed = radial with { Reverse = true };
        foreach (var gradient in EffectParity.Gradients.Append(radial).Append(reversed))
        {
            var frame = new ScriptFrameContext
            {
                GradientTime = 0.37,
                Gradients = new Dictionary<string, EffectColorGradient> { ["c"] = gradient, ["list[1]"] = gradient },
            };
            var result = RenderBoth(program, new PixelImage(w, 3), source, frame);
            for (int x = 0; x < w; x++)
            {
                double t = x / (double)(w - 1);
                Assert.Equal(EffectGradientSampler.SampleAt(gradient, t, x, 0), result[x, 0]);
                Assert.Equal(EffectGradientSampler.SampleAt(gradient, t, x, 1), result[x, 1]);
                Assert.Equal(new Color32(0x20, 0x20, 0x20, 0x80), result[x, 2]);
            }
            // 모양·각도·반복과 상관없이 위치 0은 첫 멈춤점, 1은 끝 멈춤점(뒤집으면 반대)
            if (ReferenceEquals(gradient, radial)) Assert.Equal((red, blue), (result[0, 0], result[w - 1, 0]));
            if (ReferenceEquals(gradient, reversed)) Assert.Equal((blue, red), (result[0, 0], result[w - 1, 0]));
        }

        // 그라데이션이 없으면 설정 단색, 목록에 없는 항목·모르는 슬롯은 오류
        var plain = RenderBoth(program, new PixelImage(w, 3), source, ScriptFrameContext.Still);
        Assert.Equal(new Color32(0x33, 0x66, 0x99), plain[4, 0]);
        Assert.Equal(new Color32(0, 255, 0), plain[4, 1]);
        foreach (var bad in new[] { "ctx.gradientAt('nope', 0)", "ctx.gradientAt('list', 0)", "ctx.gradientAt('list[2]', 0)", "ctx.gradientAt('pairs[0]', 0)" })
        {
            var broken = Parse(Unique($$"""
                effect({ name: "b", settings: [{ key: "c", type: "color" }, { key: "list", type: "colorList", default: ["#ff0000", "#00ff00"] },
                  { key: "pairs", type: "colorPairs", default: [{ from: "#101010", to: "#202020" }] }],
                  render(img, ctx, s) { img.data[0] = {{bad}}.r; } });
                """, bad));
            Assert.Throws<ScriptEffectException>(() => broken.Render(new PixelImage(1, 1), new Rectangle(0, 0, 1, 1), ScriptFrameContext.Still));
        }
    }

    [Fact]
    public void Declared_gradients_start_new_effects_and_round_trip_through_SetGradients()
    {
        const string Source = """
            effect({
              name: "dg",
              settings: [
                { key: "c", type: "color", default: "#336699" },
                { key: "list", type: "colorList", max: 4, default: ["#ff0000", "#00ff00"] },
              ],
              render(img, ctx, s) { const c = ctx.gradientAt("c", 0.5); img.data[0] = c.r; },
            });
            """;
        var plain = Parse(Unique(Source));
        Assert.Empty(plain.DefaultGradients);
        Assert.Empty(plain.CreateEffect().ColorGradients);

        var fire = new EffectColorGradient
        {
            Stops = [new(0, new Color32(255, 248, 224)), new(0.5, new Color32(249, 138, 42, 128)), new(1, new Color32(192, 32, 32))],
            AlphaStops = [new(0, 255), new(1, 0)],
            Shape = EffectGradientShape.Radial, Angle = 90, Reverse = true, Cycles = 2,
            Interpolation = GradientInterpolation.Steps, ColorSpace = GradientColorSpace.Rgb, Dither = GradientDither.Bayer4,
        };
        var item = new EffectColorGradient { Stops = [new(0, Color32.White)], Shape = EffectGradientShape.Uniform, Easing = EffectEasing.EaseIn };
        var edited = ScriptDeclarationEdit.SetGradients(plain.Source, new Dictionary<string, EffectColorGradient> { ["c"] = fire, ["list[1]"] = item })!;
        var program = Parse(edited);
        Assert.Equal(fire, program.DefaultGradients["c"]);
        Assert.Equal(item, program.DefaultGradients["list[1]"]);
        // 새 효과·에셋 레시피(효과 창·패키지 미리보기가 쓰는 것)가 선언의 그라데이션으로 시작한다
        Assert.Equal(fire, program.CreateEffect().ColorGradients["c"]);
        Assert.Equal(item, Chickxel.Core.IO.ScriptAsset.From("x", program).ToRecipe().Effects[0].ColorGradients["list[1]"]);
        Assert.Empty(Parse(ScriptDeclarationEdit.SetGradients(edited, new Dictionary<string, EffectColorGradient>())!).DefaultGradients);

        foreach (var bad in new[]
                 {
                     "gradients: 5",
                     "gradients: { nope: { stops: [{ position: 0, color: '#ffffff' }] } }",
                     "gradients: { 'list[4]': { stops: [{ position: 0, color: '#ffffff' }] } }",
                     "gradients: { c: { stops: [] } }",
                     "gradients: { c: { alphaStops: [] } }",
                     "gradients: { c: { stops: [{ position: 2, color: '#ffffff' }] } }",
                     "gradients: { c: { stops: [{ position: 0, color: '#ffffff' }], shape: 'radial' } }",
                     "gradients: { c: { stops: [{ position: 0, color: '#ffffff' }], alphaStops: [{ position: 0, alpha: 300 }] } }",
                     "gradients: { c: { stops: [{ position: 0, color: '#ffffff' }], extra: 1 } }",
                 })
        {
            var result = ScriptEffectProgram.Parse(Source.Replace("  render(", "  " + bad + ",\n  render("));
            Assert.False(result.Success, bad);
            Assert.Contains(result.Errors, e => e.Kind == ScriptErrorKind.Declaration);
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
    public void Layer_settings_default_to_no_layer_and_trim_the_declared_name()
    {
        var program = Parse("effect({ name: 'x', settings: [{ key: 'p', type: 'layer' }, { key: 'q', label: 'Q', type: 'layer', default: '  ground  ' }], render(img, ctx, s) {} });");
        Assert.Equal((ScriptSettingType.Layer, ""), (program.Settings[0].Type, program.Settings[0].Default));
        Assert.Equal((ScriptSettingType.Layer, "ground"), (program.Settings[1].Type, program.Settings[1].Default));
        // 값 맞추기: 문서에 없는 이름도 그대로, 앞뒤 공백은 지움, 너무 길거나 글이 아니면 기본값
        var values = program.NormalizeSettings(new Dictionary<string, object> { ["p"] = " nowhere ", ["q"] = new string('a', ScriptSetting.MaxLayerNameLength + 1) });
        Assert.Equal(("nowhere", "ground"), (values["p"], values["q"]));
        Assert.Equal("", program.NormalizeSettings(new Dictionary<string, object> { ["p"] = 3 })["p"]);
        Assert.Equal(new string('a', 64), ScriptSetting.LayerName(" " + new string('a', 64) + " "));
    }

    [Theory]
    [InlineData("{ key: 'p', type: 'layer', min: 0 }", "min")]
    [InlineData("{ key: 'p', type: 'layer', max: 3 }", "max")]
    [InlineData("{ key: 'p', type: 'layer', step: 1 }", "step")]
    [InlineData("{ key: 'p', type: 'layer', choices: ['a'] }", "choices")]
    [InlineData("{ key: 'p', type: 'layer', alpha: false }", "alpha")]
    [InlineData("{ key: 'p', type: 'layer', widget: 'angle' }", "widget")]
    [InlineData("{ key: 'p', type: 'layer', default: 3 }", "default")]
    [InlineData("{ key: 'p', type: 'layer', default: ['ground'] }", "default")]
    [InlineData("{ key: 'p', type: 'layer', default: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }", "default")]
    public void Invalid_layer_setting_declarations_are_reported_at_the_field(string setting, string field)
    {
        string source = $"effect({{ name: 'x', settings: [{setting}], render(img, ctx, s) {{}} }});";
        var result = ScriptEffectProgram.Parse(source);
        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ScriptErrorKind.Declaration, error.Kind);
        Assert.Contains(field, error.Message);
        // 오류 자리는 그 칸(한 줄 원문의 칸 이름이나 값 위치)
        int at = source.IndexOf(field + ":", StringComparison.Ordinal);
        Assert.Equal(1, error.Line);
        Assert.InRange(error.Column, at, source.IndexOf('}', at) + 1);
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
        Assert.Equal(limits.MemoryBytes + (6L << 10) * 1024 * 1024, big.MemoryBytes);
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

    [Fact]
    public void When_ShowsASettingOnlyForMatchingEarlierChoiceOrBool_AndChainsThroughHiddenOnes()
    {
        var program = Parse(Unique("""
            effect({ name: "w", settings: [
              { key: "facing", type: "choice", default: "none", choices: [{ value: "none", label: "-" }, { value: "flip", label: "f" }, { value: "rotate", label: "r" }] },
              { key: "baseAngle", type: "int", min: 0, max: 359, default: 90, when: { facing: "rotate" } },
              { key: "snap", type: "bool", default: false, when: { facing: ["rotate", "flip"] } },
              { key: "step", type: "int", min: 0, max: 90, default: 0, when: { snap: true } },
            ], render() {} });
            """));
        bool Shown(string key, params (string Key, object Value)[] values)
            => program.IsShown(program.Settings.Single(s => s.Key == key), values.ToDictionary(v => v.Key, v => v.Value));

        Assert.False(Shown("baseAngle")); // 기본값 none
        Assert.True(Shown("baseAngle", ("facing", "rotate")));
        Assert.True(Shown("snap", ("facing", "flip")));
        Assert.True(Shown("step", ("facing", "flip"), ("snap", true)));
        // 조건 설정이 숨겨지면 그 설정을 따르는 설정도 숨는다
        Assert.False(Shown("step", ("facing", "none"), ("snap", true)));

        // 앞에 없는 설정·choice/bool이 아닌 설정·없는 값·자기 자신은 선언 오류
        foreach (var bad in new[] { "{ later: true }", "{ n: 1 }", "{ c: \"zzz\" }", "{ x: true }", "[\"c\"]", "{ c: [] }" })
            Assert.Contains(ScriptEffectProgram.Parse("""
                effect({ name: "w", settings: [
                  { key: "c", type: "choice", default: "a", choices: [{ value: "a", label: "a" }] },
                  { key: "n", type: "int", min: 0, max: 1, default: 0 },
                  { key: "x", type: "bool", default: false, when: WHEN },
                  { key: "later", type: "bool", default: false },
                ], render() {} });
                """.Replace("WHEN", bad)).Errors, e => e.Kind == ScriptErrorKind.Declaration);
    }

    [Fact]
    public void Text_settings_keep_newlines_and_use_the_declared_length_in_the_schema_and_json()
    {
        var program = Parse(Unique("""
            effect({ name: "letters", settings: [
              { key: "caption", type: "text", max: 12, default: " A\nB " },
              { key: "empty", type: "text" },
            ], render(img, ctx, s) { img.data[0] = s.caption.length; img.data[1] = s.caption.indexOf("\n"); } });
            """));
        Assert.Equal((ScriptSettingType.Text, 12.0, " A\nB "), (program.Settings[0].Type, program.Settings[0].Max, program.Settings[0].Default));
        Assert.Equal(("", (double)ScriptSetting.MaxTextLength), (program.Settings[1].Default, program.Settings[1].Max));
        Assert.Equal(" A\nB ", program.NormalizeSettings(null)["caption"]);
        Assert.Equal(" A\nB ", program.NormalizeSettings(new Dictionary<string, object> { ["caption"] = 123 })["caption"]);
        Assert.Equal("abc\ndefghijk", program.NormalizeSettings(new Dictionary<string, object> { ["caption"] = "abc\ndefghijklmnopqrstuvwxyz" })["caption"]);

        var effect = program.CreateEffect(new Dictionary<string, object> { ["caption"] = "line1\r\nline2" });
        var parameter = EffectRegistry.ParametersOf(effect, _ => program).Single(p => p.Key == "caption");
        Assert.Equal(EffectParameterKind.Text, parameter.Kind);
        Assert.Equal(12, parameter.MaxLength);
        Assert.True(parameter.Exposed);
        var json = EffectCodec.Write(effect);
        Assert.Equal("line1\nline2", ((JsonObject)json[EffectCodec.SettingsKey]!)["caption"]!.GetValue<string>());
        var hash = ScriptLibrary.Add(program.Source);
        ScriptLibrary.SetTrust(hash, true);
        var decoded = (ScriptEffect)EffectCodec.Read((JsonObject)JsonNode.Parse(json.ToJsonString())!);
        Assert.Equal("line1\nline2", decoded.Settings["caption"]);

        var image = new PixelImage(1, 1);
        program.Render(image, image.Bounds, ScriptFrameContext.Still);
        Assert.Equal(new Color32(5, 2, 0, 0), image[0, 0]);
    }

    [Theory]
    [InlineData("max: 0", "max")]
    [InlineData("max: 1001", "max")]
    [InlineData("max: 3.5", "max")]
    [InlineData("max: '8'", "max")]
    [InlineData("max: 2, default: 'long'", "default")]
    [InlineData("default: false", "default")]
    [InlineData("min: 1", "min")]
    [InlineData("step: 1", "step")]
    [InlineData("choices: ['a']", "choices")]
    public void Invalid_text_setting_declarations_are_rejected(string fields, string field)
    {
        var result = ScriptEffectProgram.Parse($"effect({{ name: 'x', settings: [{{ key: 'caption', type: 'text', {fields} }}], render() {{}} }});");
        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Kind == ScriptErrorKind.Declaration && error.Message.Contains(field, StringComparison.Ordinal));
    }

    [Fact]
    public void Font_settings_keep_the_font_name_and_ctx_text_draws_with_it()
    {
        var program = Parse(Unique("""
            effect({ name: "fonts", settings: [
              { key: "font", type: "font", default: "Galmuri7" },
              { key: "plain", type: "font" },
            ], render(img, ctx, s) { img.data[0] = ctx.text("A", { font: s.font }).height; img.data[1] = ctx.text("A", { font: s.plain }).height; } });
            """));
        Assert.Equal((ScriptSettingType.Font, "Galmuri7"), (program.Settings[0].Type, program.Settings[0].Default));
        Assert.Equal(Chickxel.Core.Text.FontLibrary.DefaultName, program.Settings[1].Default);
        // 앞뒤 공백은 지우고, 이 컴퓨터에 없는 이름은 그대로 두며, 빈 이름·글이 아닌 값은 기본값
        Assert.Equal("Galmuri14", program.NormalizeSettings(new Dictionary<string, object> { ["font"] = " Galmuri14 " })["font"]);
        Assert.Equal("NotInstalled", program.NormalizeSettings(new Dictionary<string, object> { ["font"] = "NotInstalled" })["font"]);
        Assert.Equal("Galmuri7", program.NormalizeSettings(new Dictionary<string, object> { ["font"] = "  " })["font"]);
        Assert.Equal("Galmuri7", program.NormalizeSettings(new Dictionary<string, object> { ["font"] = 3 })["font"]);

        var effect = program.CreateEffect(new Dictionary<string, object> { ["font"] = "Galmuri14" });
        var parameter = EffectRegistry.ParametersOf(effect, _ => program).Single(p => p.Key == "font");
        Assert.Equal(EffectParameterKind.Text, parameter.Kind);
        var json = EffectCodec.Write(effect);
        Assert.Equal("Galmuri14", ((JsonObject)json[EffectCodec.SettingsKey]!)["font"]!.GetValue<string>());
        var hash = ScriptLibrary.Add(program.Source);
        ScriptLibrary.SetTrust(hash, true);
        Assert.Equal("Galmuri14", ((ScriptEffect)EffectCodec.Read((JsonObject)JsonNode.Parse(json.ToJsonString())!)).Settings["font"]);

        // 고른 글꼴로 그린다: Galmuri7은 기본 글꼴(Galmuri11)보다 낮다
        var image = new PixelImage(1, 1);
        program.Render(image, image.Bounds, ScriptFrameContext.Still);
        Assert.True(image[0, 0].R < image[0, 0].G, $"Galmuri7 {image[0, 0].R}px, default {image[0, 0].G}px");
    }

    [Theory]
    [InlineData("max: 10", "max")]
    [InlineData("min: 1", "min")]
    [InlineData("choices: ['a']", "choices")]
    [InlineData("default: ''", "default")]
    [InlineData("default: 3", "default")]
    public void Invalid_font_setting_declarations_are_rejected(string fields, string field)
    {
        var result = ScriptEffectProgram.Parse($"effect({{ name: 'x', settings: [{{ key: 'font', type: 'font', {fields} }}], render() {{}} }});");
        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Kind == ScriptErrorKind.Declaration && error.Message.Contains(field, StringComparison.Ordinal));
    }

    [Fact]
    public void Context_text_defaults_to_a_bounded_transparent_image_and_rejects_oversized_text()
    {
        var program = Parse(Unique("""
            effect({ name: "default text", render(img, ctx, s) {
              const letters = ctx.text("A");
              img.data[0] = letters.width > 0 && letters.width <= 16384 ? 1 : 0;
              img.data[1] = letters.height > 0 && letters.height <= 16384 ? 1 : 0;
              img.data[2] = letters.data.length === letters.width * letters.height * 4 ? 1 : 0;
              img.data[3] = letters.data.some((_, i) => i % 4 === 3 && letters.data[i] !== 0) ? 255 : 0;
            } });
            """));
        var image = new PixelImage(1, 1);
        program.Render(image, image.Bounds, ScriptFrameContext.Still);
        Assert.Equal(new Color32(1, 1, 1, 255), image[0, 0]);

        var oversized = Parse(Unique("""
            effect({ name: "oversized", render(img, ctx, s) { ctx.text("x".repeat(1001)); } });
            """));
        Assert.Throws<ScriptEffectException>(() => oversized.Render(new PixelImage(1, 1), new Rectangle(0, 0, 1, 1), ScriptFrameContext.Still));
    }

    [Fact]
    public void Context_text_images_blend_like_the_builtin_text_effect()
    {
        var program = Parse(Unique("""
            effect({ name: "print", settings: [{ key: "caption", type: "text", default: "Hi\nYou" }],
              render(img, ctx, s) {
                const letters = ctx.text(s.caption, { size: 12, color: { r: 245, g: 65, b: 35, a: 160 },
                  bold: true, italic: false, smooth: true, align: "left", letterSpacing: 1, lineSpacing: 2, width: 32 });
                img.blendOver(letters, 2, 3);
              } });
            """));
        var actual = new PixelImage(64, 48);
        actual[5, 8] = new Color32(25, 70, 180, 255);
        var expected = actual.Clone();
        TextRender.Apply(expected, new TextEffect
        {
            Text = "Hi\nYou", Size = 12, Color = new Color32(245, 65, 35, 160),
            Bold = true, Smooth = true, Align = TextAlign.Left, LetterSpacing = 1, LineSpacing = 2,
            BoxWidth = 32, X = 2, Y = 3,
        }, expected.Bounds);
        program.Render(actual, actual.Bounds, ScriptFrameContext.Still);
        Assert.True(expected.ContentEquals(actual), "ctx.text differs from TextEffect");
        Assert.Contains(actual.Pixels, pixel => pixel.A > 0 && pixel != new Color32(25, 70, 180, 255));
    }
}
