using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Chickxel.Core.Scripting.Fast;
using Jint;
using Jint.Constraints;
using Jint.Native;

namespace Chickxel.Core.Tests;

/// <summary>
/// 스크립트 효과 빠른 경로(설계 8장): 컴파일한 render가 Jint와 같은 값(배열 칸의 double 비트까지), 같은 픽셀, 같은 문장 수 한도를 내는지.
/// </summary>
public class ScriptFastPathTests
{
    private static readonly ScriptLimits Limits = new() { Timeout = TimeSpan.FromSeconds(30), MaxStatements = 5_000_000, MemoryBytes = 256L << 20, MaxRecursion = 64 };

    private const string SettingsDecl = """
        { key: "n", type: "int", min: 0, max: 100, default: 7 },
          { key: "f", type: "number", min: 0, max: 1, default: 0.25 },
          { key: "on", type: "bool", default: true },
          { key: "tint", type: "color", default: "#3080ff" },
          { key: "mode", type: "choice", choices: ["add", "mul", "xor"], default: "mul" },
        """;

    private static readonly IReadOnlyDictionary<string, object>[] SettingsCases =
    [
        new Dictionary<string, object>(),
        new Dictionary<string, object> { ["n"] = 33, ["f"] = 0.8, ["on"] = false, ["tint"] = new Color32(200, 10, 90, 128), ["mode"] = "xor" },
        new Dictionary<string, object> { ["n"] = 100, ["f"] = 0.0, ["on"] = true, ["tint"] = new Color32(0, 255, 0), ["mode"] = "add" },
    ];

    private static readonly ScriptFrameContext Frame = new()
    {
        Time = 0.3, Progress = 0.55, FrameIndex = 4, FrameStep = 2, FrameSpan = 9, Pivot = new PointF(1.5f, 2.25f),
    };

    private static readonly Rectangle Source = new(1, 0, 3, 3);

    private static string Script(string body, string top = "")
        => top + "\neffect({\n  name: 't',\n  settings: [\n  " + SettingsDecl + "\n  ],\n  render(img, ctx, s) {\n" + body + "\n  },\n});\n";

    // 머리말이 없을 때 render 몸통 첫 줄의 원문 줄 번호
    private static readonly int BodyLine = Script("").Split('\n').ToList().FindIndex(l => l.Contains("render(img")) + 2;

    private static ScriptEffectProgram Program(string source, ScriptLimits? limits = null)
    {
        var result = ScriptEffectProgram.Parse(source, limits ?? Limits);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return result.Program;
    }

    private static ScriptEffectProgram Compiled(string source, ScriptLimits? limits = null)
    {
        var program = Program(source, limits);
        Assert.True(program.ExecutionPath.Compiled, program.ExecutionPath.ToString());
        return program;
    }

    /// <summary>작은 그림: 색과 알파가 칸마다 다르고 투명 칸이 섞임.</summary>
    private static PixelImage Small(int w = 5, int h = 4)
    {
        var image = new PixelImage(w, h);
        for (int i = 0; i < image.Pixels.Length; i++)
            image.Pixels[i] = i % 5 == 3 ? default : new Color32((byte)(i * 37 + 11), (byte)(i * 91 + 3), (byte)(255 - i * 13), (byte)(i % 4 == 1 ? 140 : 255));
        return image;
    }

    private static double[] Data(PixelImage image)
    {
        var data = new double[image.Pixels.Length * 4];
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            var c = image.Pixels[i];
            (data[i * 4], data[i * 4 + 1], data[i * 4 + 2], data[i * 4 + 3]) = (c.R, c.G, c.B, c.A);
        }
        return data;
    }

    /// <summary>한 경로로 render 한 번 돌린 결과: 끝난 data 배열, 또는 오류, 또는 Jint로 넘김.</summary>
    private sealed record Outcome(double[]? Data, ScriptError? Error, bool Deopt, long Statements = -1);

    private static Outcome Fast(ScriptEffectProgram program, PixelImage image, IReadOnlyDictionary<string, object>? settings, int? maxStatements = null)
    {
        var fast = program.Fast!;
        if (maxStatements is { } max) fast = fast.WithLimits(program.Limits with { MaxStatements = max });
        var data = Data(image);
        try
        {
            long statements = fast.Run(data, image.Width, image.Height, Source, Frame, program.NormalizeSettings(settings), CancellationToken.None,
                new ScriptColors(program.Settings, program.NormalizeSettings(settings), Frame, image, Source));
            return new(data, null, false, statements);
        }
        catch (ScriptEffectException ex)
        {
            return new(null, ex.Error, false);
        }
        catch (FastDeoptException)
        {
            return new(null, null, true);
        }
    }

    // Jint 길을 그대로 밟되 되쓰기 전의 배열 값을 꺼낸다. 문장 한도는 실행기의 계수 제약을 이 호출 동안만 바꾼다(선언 실행 뒤, render 직전)
    private static Outcome Jint(ScriptEffectProgram program, PixelImage image, IReadOnlyDictionary<string, object>? settings, int? maxStatements = null)
    {
        var values = program.NormalizeSettings(settings);
        try
        {
            var data = ScriptSandbox.Run(program.Limits, CancellationToken.None, sandbox =>
            {
                var declaration = sandbox.Declare(program.PreparedScript);
                var s = ScriptHostApi.CreateSettings(sandbox.Engine, program.Settings, values);
                var ctx = ScriptHostApi.CreateContext(sandbox, program.Settings, s, Source, Frame, new System.Drawing.Size(image.Width, image.Height),
                    new ScriptColors(program.Settings, values, Frame, image, Source));
                var (img, array) = ScriptHostApi.CreateImage(sandbox, image);
                var constraint = sandbox.Engine.Constraints.Find<MaxStatementsConstraint>()!;
                int saved = constraint.MaxStatements;
                constraint.MaxStatements = maxStatements ?? saved;
                try
                {
                    sandbox.Engine.Invoke(declaration.Get("render"), declaration, [img, ctx, s]);
                }
                finally
                {
                    constraint.MaxStatements = saved;
                }
                var result = new double[ScriptHostApi.Length(array)];
                for (uint i = 0; i < result.Length; i++)
                    result[i] = array[i] is JsNumber n ? n.AsNumber() : throw new Xunit.Sdk.XunitException($"data[{i}] is not a number");
                return result;
            });
            return new(data, null, false);
        }
        catch (ScriptEffectException ex)
        {
            return new(null, ex.Error, false);
        }
    }

    private static void AssertSameValues(double[] expected, double[] actual, string label)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            bool same = double.IsNaN(expected[i]) ? double.IsNaN(actual[i]) : BitConverter.DoubleToInt64Bits(expected[i]) == BitConverter.DoubleToInt64Bits(actual[i]);
            Assert.True(same, $"{label}: data[{i}] Jint {expected[i]:R} fast {actual[i]:R}");
        }
    }

    private static void AssertSame(Outcome jint, Outcome fast, string label)
    {
        Assert.False(fast.Deopt, label + ": fast path deopted");
        if (jint.Error is { } error)
        {
            Assert.NotNull(fast.Error);
            Assert.Equal((error.Kind, error.Line, error.Column), (fast.Error!.Kind, fast.Error.Line, fast.Error.Column));
            return;
        }
        Assert.True(fast.Error is null, $"{label}: fast failed with {fast.Error}");
        AssertSameValues(jint.Data!, fast.Data!, label);
    }

    // ───────────── 본보기(빛 지나가기) ─────────────

    /// <summary>여백 안의 울퉁불퉁한 원본: 투명 구멍, 반투명 픽셀, 가장자리에 붙은 경우.</summary>
    private static (PixelImage Image, Rectangle Source) Sprite(Random random, int width, int height, int margin)
    {
        var image = new PixelImage(width + margin * 2, height + margin * 2);
        double cx = width / 2.0, cy = height / 2.0, rx = width * (0.3 + random.NextDouble() * 0.25), ry = height * (0.3 + random.NextDouble() * 0.25);
        int hole = random.Next(5, 19);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double dx = (x + 0.5 - cx) / rx, dy = (y + 0.5 - cy) / ry;
                if (dx * dx + dy * dy > 1 + 0.3 * Math.Sin(x * 0.7 + y) || (x * 7 + y * 3) % hole == 0) continue;
                byte alpha = (x + y) % 9 == 0 ? (byte)random.Next(1, 255) : (byte)255;
                image.Pixels[(y + margin) * image.Width + x + margin] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha);
            }
        return (image, new Rectangle(margin, margin, width, height));
    }

    [Fact]
    public void Shine_template_runs_on_the_fast_path_and_matches_Jint_and_the_built_in_renderer()
    {
        var program = Program(OfficialEffects.Source("shine"));
        Assert.Equal(new ScriptExecutionPath(true, null, 0, 0), program.ExecutionPath);
        var random = new Random(20261101);
        (int W, int H, int Margin)[] sizes = [(16, 16, 0), (48, 32, 3), (40, 40, 0), (9, 23, 2), (8, 8, 1), (1, 1, 0), (64, 64, 4)];
        int cases = 0, changed = 0;
        foreach (var (w, h, margin) in sizes)
        {
            for (int n = 0; n < 70; n++)
            {
                var (sprite, source) = n == 0 && w == 8 ? (new PixelImage(w, h), new Rectangle(0, 0, w, h)) : Sprite(random, w, h, margin);
                int frames = random.Next(2, 13);
                var shine = new ShineEffect
                {
                    Angle = random.Next(360), Width = random.Next(1, 33), Strength = random.Next(101), Softness = random.Next(3),
                    Color = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
                    Repeat = (EffectRepeat)random.Next(3), RepeatCount = random.Next(1, 4), Easing = (EffectEasing)random.Next(4),
                };
                var at = (ShineEffect)shine.AtFrame(random.Next(frames), frames, null);
                var settings = new Dictionary<string, object>
                {
                    ["angle"] = at.Angle, ["width"] = at.Width, ["color"] = at.Color, ["strength"] = at.Strength, ["softness"] = at.Softness,
                };
                var context = ScriptFrameContext.From(at);
                var expected = sprite.Clone();
                ShineRender.Apply(expected, at, source);
                var jint = sprite.Clone();
                program.RenderOnJint(jint, source, context, program.NormalizeSettings(settings), CancellationToken.None);
                var fast = sprite.Clone();
                Assert.True(program.Fast!.TryRender(fast, source, context, program.NormalizeSettings(settings), CancellationToken.None));
                Assert.True(expected.ContentEquals(jint), $"Jint {w}x{h} case {n}");
                Assert.True(expected.ContentEquals(fast), $"fast {w}x{h} case {n}");
                cases++;
                if (!expected.ContentEquals(sprite)) changed++;
            }
        }
        Assert.Equal(sizes.Length * 70, cases);
        Assert.True(changed > cases / 3, $"only {changed} of {cases} cases changed pixels");
    }

    [Fact]
    public void Shine_template_matches_Jint_value_for_value_at_128()
    {
        var program = Program(OfficialEffects.Source("shine"));
        var random = new Random(7);
        for (int n = 0; n < 4; n++)
        {
            var (sprite, source) = Sprite(random, 128, 128, 0);
            var settings = new Dictionary<string, object>
            {
                ["angle"] = random.Next(360), ["width"] = random.Next(1, 33), ["color"] = new Color32((byte)random.Next(256), 200, 90),
                ["strength"] = random.Next(1, 101), ["softness"] = random.Next(3),
            };
            var jint = Jint(program, sprite, settings);
            var fast = Fast(program, sprite, settings);
            AssertSame(jint, fast, $"128 case {n}");
        }
    }

    // ───────────── 구문 묶음 ─────────────

    // 가장자리 값(±0, NaN, ±Infinity, .5, 큰 수)을 칸마다 돌려 가며 만든다. 리터럴 return과 아닌 return이 섞여 문장 수도 시험한다
    private const string EdgeTop = """
        function edge(i) {
          const k = i % 13;
          if (k === 0) return -0;
          if (k === 1) return NaN;
          if (k === 2) return Infinity;
          if (k === 3) return -Infinity;
          if (k === 4) return 0.5;
          if (k === 5) return -2.5;
          if (k === 6) { return 4503599627370497; }
          if (k === 7) return 1e21;
          if (k === 8) return -0.49999999999999994;
          return (i - 40) / 3;
        }
        """;

    public static TheoryData<string> EdgeExpressions =>
    [
        "Math.round(v)", "Math.floor(v) + Math.ceil(v)", "Math.trunc(v) * 2", "Math.sign(v)", "Math.abs(v)", "Math.sqrt(v)",
        "Math.sin(v) + Math.cos(v)", "Math.tan(v)", "Math.atan2(v, i - 50)", "Math.atan2(-0, v)", "Math.atan2(v, -0)", "Math.atan2(v, v)",
        "Math.pow(v, 2) + Math.pow(2, v)", "Math.pow(v, 0.5)", "Math.pow(-v, i % 3 - 1)", "v ** 3", "(-v) ** 0.5", "v ** -1", "v ** v",
        "Math.exp(v) + Math.log(v)", "Math.log2(v) + Math.log10(v)", "Math.min(v, 3, i)", "Math.max(v, -0, 0)", "Math.min(0, v, -0)",
        "Math.min() - Math.max()", "Math.max(v)", "Math.hypot(v, 3)", "Math.hypot(v)", "Math.hypot()", "Math.hypot(v, -0, i)",
        "Math.asin(v / 20) + Math.acos(v / 20) + Math.atan(v)", "Math.sinh(v / 4) + Math.cosh(v / 4) + Math.tanh(v)",
        "Math.fround(v / 3)", "Math.imul(i * 1234567, v)", "Math.clz32(v * 99999)",
        "v % 3", "i % -4 - v % 2.5", "-v % 1", "v % v", "v / 0", "-v / -0", "0 / v",
        "(v | 0) + (v >>> 0) + (v >> 1) + (v << 3) + (~v) + (v ^ 5) + (v & 6)", "(i * 1e9) | 0", "(v * 4294967296 + 7) >>> 3", "-v >> 33",
        "Math.round(v * 1e10) / 1e10", "lerp(v, i, 0.25)", "lerp(i, v)", "v === v ? 1 : 2", "(v > 0) + (v < 0) * 2 + (v == 0) * 4 + (v >= 0) * 8 + (v <= 0) * 16",
        "true + v", "!v + !!i", "(v && i) + (v || i)", "-v", "+v - -v", "v !== v ? 3 : 4",
        "Math.round(Math.PI * Math.E * Math.LN2 * Math.LN10 * Math.LOG2E * Math.LOG10E * Math.SQRT2 * Math.SQRT1_2 * 1e6) + v",
        "2 ** 53 + i + v", "1e21 | v", "-1e21 >>> 0", "0x7fffffff + 1 | 0", "1.7976931348623157e308 * 10 - v",
    ];

    [Theory]
    [MemberData(nameof(EdgeExpressions))]
    public void Number_semantics_match_Jint_bit_for_bit(string expression)
    {
        var program = Compiled(Script($$"""
            const d = img.data;
            for (let i = 0; i < d.length; i++) {
              const v = edge(i);
              d[i] = {{expression}};
            }
            """, EdgeTop));
        var image = Small(8, 5);
        AssertSame(Jint(program, image, null), Fast(program, image, null), expression);
        AssertStopsAtTheSameStatement(program, image, null, sweep: false);
    }

    public static TheoryData<string, string, string> Constructs => new()
    {
        {
            "loops and control flow", "", """
            const d = img.data;
            let acc = 0;
            for (let y = 0; y < img.height; y++) {
              for (let x = 0; x < img.width; x++) {
                const i = (y * img.width + x) * 4;
                if (d[i + 3] === 0) continue;
                if (x > 3) break;
                acc += d[i] * 0.5 - d[i + 1];
                d[i] = (acc % 256 + 256) % 256;
              }
            }
            let k = 0;
            while (k < 10) { k++; if (k === 7) break; }
            do { k--; } while (k > 3);
            do k += 5; while (k < 12);
            for (; k < 20;) k += 2;
            for (k = 0; k < 3; k++);
            {}
            ;
            while (k > 0) { k--; continue; }
            d[1] = k + acc;
            """
        },
        {
            "functions inlined at the call site", "", """
            const d = img.data;
            const sq = (v) => v * v;
            function clamp(v, lo, hi) { if (v < lo) return lo; if (v > hi) { return hi; } return v; }
            const pick = function (a, b) { 'use strict'; for (let i = 0; i < 5; i++) { if (i === a) return b; } return -1; };
            const noop = (v) => { if (v > 2) return; d[2] = v; };
            noop(1); noop(5);
            d[0] = clamp(sq(d[0] / 16), 0, 255);
            d[4] = pick(3, 99) + pick(9, 1);
            const hidden = 3;
            d[5] = later(4);
            function later(x) { return x * hidden; }
            const named = function self(x) { return x + 1; };
            d[6] = named(d[6]);
            const twice = (fn, x) => fn(fn(x));
            d[7] = twice(sq, 3);
            const flag = (x) => x > 100;
            d[8] = flag(d[8]) ? 1 : 0;
            """
        },
        {
            "host API", "", """
            const d = img.data, c = ctx.color('tint', 1, 2);
            const h = hsvToRgb(ctx.progress * 360, s.f, 0.9);
            const h2 = hsvToRgb(120, 1, 1, 77);
            const v = rgbToHsv(d[0], d[1], d[2]);
            for (let i = 0; i < d.length; i += 4) {
              const r = ctx.hash(i, i >> 2, s.n);
              d[i] = c.r * r + h.g + ctx.color('tint').a;
              d[i + 1] = s.on ? h2.a + ctx.random(i) * 10 : v.h;
              d[i + 2] = s.mode === 'xor' ? (d[i + 2] ^ c.b) : s.mode !== 'add' ? d[i + 2] * v.s : d[i + 2] + v.v * 100;
              d[i + 3] = ctx.source.x + ctx.source.y * 2 + ctx.source.w * 3 + ctx.source.h * 4 + ctx.pivot.x + ctx.pivot.y + ctx.t + ctx.frame + ctx.step + ctx.span + img.width;
            }
            d[0] = 'mul' == s.mode ? hsvToRgb(d[1] / 255, 0.5).b : rgbToHsv().v + hsvToRgb().r;
            d[1] = s['n'] + img['height'] + d.length;
            """
        },
        {
            "assignments and updates", "", """
            const d = img.data;
            let a = 5, b = 2.5, t = true;
            d[0] += 3; d[1] -= a; d[2] *= b; d[3] /= 4; d[4] %= 7; d[5] **= 0.5; d[6] <<= 2; d[7] >>= 1; d[8] >>>= 1;
            d[9] &= 0x0f; d[10] |= 0x30; d[11] ^= 0xff;
            d[12] = d[13]++ + ++d[14] - d[15]-- + --d[16];
            a += b *= 2; a -= 1; a **= 2; a %= 7; a |= 0; a <<= 1; a >>= 1; a >>>= 0; a &= 255; a ^= 3; a /= 3;
            t = !t; t = a > 3 && t;
            const x = (a++, a--, ++a, --a, a);
            d[17] = x + (t ? 1 : 0) + (t ? b : a);
            d[18] = (b && a) || 9;
            d[19] = (0 || b) + (NaN && 1) + (Infinity - Infinity);
            d[20] = (d[21] = 7) + (a = 2);
            """
        },
        {
            "block scoping and shadowing", "", """
            const d = img.data;
            const v = 1;
            { const v = 2; d[0] = v; { let v = 3; d[1] = v; } d[2] = v; }
            d[3] = v;
            let w = 0;
            for (let i = 0; i < 3; i++) { let w2 = i * 2; w += w2; }
            if (w > 1) { let only = w; d[4] = only; }
            const f = (n) => { const v = n * 10; return v; };
            d[5] = f(v) + v;
            for (let i = 0; i < 2; i++) for (let i = 5; i < 7; i++) d[6] += i;
            """
        },
        {
            "literal returns and blocks", "", """
            const d = img.data;
            const a = (x) => { return 1; };
            const b = (x) => { if (x) return 2; return 3; };
            const c = (x) => { if (x) { return 4; } return 5; };
            const e = (x) => { { return 6; } };
            const g = (x) => { { x++; return 7; } };
            const k = (x) => x ? 8 : 9;
            d[0] = a(0) + b(0) + b(1) + c(0) + c(1) + e(0) + g(0) + k(0) + k(1);
            const z = (x) => { return -1; };
            d[1] = z(0);
            const m = () => { return true; };
            d[2] = m() ? 10 : 20;
            function declared() {}
            declared();
            """
        },
        {
            "conditions with mixed kinds", "", """
            const d = img.data;
            const on = s.on;
            let n = 0;
            if (on && d[0]) n++;
            if (!d[1] || on) n += 2;
            while (n < 10 && (on || d[2])) n += 3;
            d[0] = n;
            d[1] = (n > 3) == 1 ? 5 : 6;
            d[2] = (n === true) ? 1 : 0;
            d[3] = (true == 1) + (false != 0) * 2 + (true === 1) * 4 + (on !== 1) * 8;
            d[4] = true + true - -false;
            d[5] = -false;
            d[6] = s.on * 3 + (s.on + s.n);
            on && d[7]++;
            on || d[8]++;
            n > 5 ? d[9]++ : d[10]++;
            """
        },
        {
            "top-level constants and functions", """
            'use strict';
            const TAU = Math.PI * 2, HALF = 0.5, ON = true;
            const LIST = [1, 2, { a: 'b' }];
            const NAME = 'shape';
            function wave(x) { return Math.sin(x * TAU) * HALF + HALF; }
            const tri = (x) => Math.abs((x % 1 + 1) % 1 - 0.5) * 2;
            """, """
            const d = img.data;
            for (let i = 0; i < d.length; i++) d[i] = ON ? wave(i / 7) * 255 : tri(i / 5) * 255;
            """
        },
        {
            "nested functions, lerp and extra arguments", "", """
            const d = img.data;
            const add = (a, b) => a + b;
            const mul = function m(a, b) { return a * b; };
            const both = (a, b) => add(mul(a, b), add(a, b));
            function outer(x) { const inner = (y) => both(x, y) + x; return inner(2) + inner(3); }
            d[0] = outer(d[0] / 32);
            d[1] = lerp(d[1], d[2], 0.25) + lerp(1, 2) * 0;
            const f = (a) => a * 2;
            let side = 0;
            d[2] = f(3, side++, side++) + side;
            d[3] = Math.abs(-4, side++) + side;
            d[4] = Math.max() === -Infinity ? 1 : 0;
            d[5] = Math.min(side++, side++, side++) + side;
            d[6] = hsvToRgb(10, 1, 1, 200, side++).a + side;
            d[7] = ctx.hash(1, 2, 3, side++) + side + ctx.random(9, side++) + side;
            d[8] = lerp(1, 2, 3, side++) + side;
            """
        },
    };

    [Theory]
    [MemberData(nameof(Constructs))]
    public void Supported_constructs_match_Jint_bit_for_bit(string name, string top, string body)
    {
        var program = Compiled(Script(body, top));
        foreach (var settings in SettingsCases)
        {
            var image = Small();
            AssertSame(Jint(program, image, settings), Fast(program, image, settings), name);
        }
    }

    // ───────────── 문장 수 한도 ─────────────

    /// <summary>
    /// 빠른 경로가 센 문장 수 N에서: 한도 N + 1(바로 아래)과 N(꼭 맞음)이면 두 길 모두 끝까지, N − 1(바로 위)이면 두 길 모두 같은 줄·칸에서
    /// 문장 한도로 멈춘다. <paramref name="sweep"/>면 그 사이 여러 한도(1부터 차례로 + 무작위)에서도 멈추는 자리가 같다.
    /// </summary>
    private static void AssertStopsAtTheSameStatement(ScriptEffectProgram program, PixelImage image, IReadOnlyDictionary<string, object>? settings, bool sweep)
    {
        long counted = Fast(program, image, settings).Statements;
        Assert.InRange(counted, 1, 1_000_000);
        int n = (int)counted;
        var limits = new SortedSet<int> { n - 1, n, n + 1 };
        if (sweep)
        {
            for (int m = 1; m <= Math.Min(n, 150); m++) limits.Add(m);
            var random = new Random(n);
            for (int r = 0; r < 40; r++) limits.Add(random.Next(1, n + 1));
        }
        foreach (int max in limits)
        {
            if (max < 1) continue;
            var jint = Jint(program, image, settings, max);
            var fast = Fast(program, image, settings, max);
            Assert.Equal(max >= n, jint.Error is null);
            if (fast.Deopt)
            {
                // 식 몸통 화살표 함수에서 걸림: 그 호출은 Jint가 그리므로 Jint도 바로 이 한도에서 멈춰야 한다
                Assert.Equal(ScriptErrorKind.Statements, jint.Error?.Kind);
                continue;
            }
            AssertSame(jint, fast, $"limit {max} of {n}");
            if (fast.Error is { } error)
            {
                Assert.Equal(ScriptErrorKind.Statements, error.Kind);
                Assert.Equal(DomainStrings.FormatScript_StatementLimit(count: max), error.Message);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Constructs))]
    public void Constructs_stop_at_the_same_statement_as_Jint(string name, string top, string body)
    {
        _ = name;
        var program = Compiled(Script(body, top));
        AssertStopsAtTheSameStatement(program, Small(), SettingsCases[1], sweep: true);
    }

    [Fact]
    public void Shine_template_stops_at_the_same_statement_as_Jint()
    {
        var program = Program(OfficialEffects.Source("shine"));
        var (sprite, _) = Sprite(new Random(3), 6, 5, 1);
        var settings = new Dictionary<string, object> { ["angle"] = 30, ["width"] = 3, ["strength"] = 90, ["softness"] = 2 };
        AssertStopsAtTheSameStatement(program, sprite, settings, sweep: true);
    }

    [Theory]
    [InlineData("let i = 0;\nwhile (true) { i++; }")]
    [InlineData("for (;;) {}")]
    [InlineData("let i = 0;\nfor (;;) { i++; if (i < 0) break; }")]
    [InlineData("const f = (x) => x + 1;\nlet i = 0;\nwhile (true) i = f(i);")]
    [InlineData("const f = (x) => { return x + 1; };\nlet i = 0;\ndo { i = f(i); } while (true);")]
    public void Infinite_loops_stop_at_the_same_statement_and_position_on_both_paths(string body)
    {
        var limits = Limits with { MaxStatements = 300_000, StatementsPerPixel = 0 }; // 실행기를 만들 때 잠금 스크립트도 이 한도 안에서 돈다(그림 크기 몫 없이)
        var program = Compiled(Script(body), limits);
        var image = Small();
        var jint = Jint(program, image, null);
        Assert.Equal(ScriptErrorKind.Statements, jint.Error?.Kind);
        var fast = Fast(program, image, null);
        if (!fast.Deopt) AssertSame(jint, fast, body);
        // 공개 Render는 어느 길이든 같은 오류
        var error = Assert.Throws<ScriptEffectException>(() => program.Render(image, Source, Frame)).Error;
        Assert.Equal((jint.Error!.Kind, jint.Error.Line, jint.Error.Column, jint.Error.Message), (error.Kind, error.Line, error.Column, error.Message));
    }

    // ───────────── 실행 중 Jint로 넘기기 ─────────────

    [Theory]
    [InlineData("d[0] = d[d.length] === 0 ? 1 : 2;")]
    [InlineData("d[0] = d[0.5] === 0 ? 1 : 2;")]
    [InlineData("d[-1] = 5; d[0] = 9;")]
    [InlineData("d[NaN] = 3; d[1] = 4;")]
    [InlineData("d[d.length] = 1;")]
    [InlineData("const f = (x) => { if (x > 0) return 1; };\nd[0] = f(-1);")]
    [InlineData("const f = (x) => { if (x > 0) return 1; return; };\nd[0] = f(-1);")]
    [InlineData("for (let i = 0; i <= d.length; i++) d[i] = 255 - d[i];")]
    public void Spots_where_JS_could_differ_rerun_that_call_on_Jint(string body)
    {
        var program = Compiled(Script("const d = img.data;\n" + body));
        var image = Small();
        var before = image.Clone();
        Assert.False(program.Fast!.TryRender(image, Source, Frame, program.NormalizeSettings(null), CancellationToken.None));
        Assert.True(before.ContentEquals(image));

        var expected = image.Clone();
        var expectedError = Record.Exception(() => program.RenderOnJint(expected, Source, Frame, program.NormalizeSettings(null), CancellationToken.None));
        var actual = image.Clone();
        var actualError = Record.Exception(() => program.Render(actual, Source, Frame));
        Assert.Equal((expectedError as ScriptEffectException)?.Error, (actualError as ScriptEffectException)?.Error);
        Assert.Equal(expectedError is null, actualError is null);
        Assert.True(expected.ContentEquals(actual));
    }

    // ───────────── 실행 경로 ─────────────

    [Theory]
    [InlineData("const d = img.data;\ntry { d[0] = 1; } catch (e) {}", 2, 1)]
    [InlineData("const d = img.data;\n  const label = 'x';", 2, 17)]
    [InlineData("const d = img.data;\nd[0] = later;\nconst later = 1;", 2, 8)]
    [InlineData("const d = img.data;\nd[0] = ctx.frameSource(0);", 2, 8)]
    [InlineData("const f = (n) => n < 1 ? 0 : f(n - 1);\nimg.data[0] = f(3);", 1, 30)]
    public void Unsupported_script_reports_why_and_where(string body, int line, int column)
    {
        var program = Program(Script(body));
        var path = program.ExecutionPath;
        Assert.False(path.Compiled);
        Assert.False(string.IsNullOrWhiteSpace(path.Reason));
        Assert.Equal((line + BodyLine - 1, column), (path.Line, path.Column));
        Assert.Null(program.Fast);
    }

    [Fact]
    public void Execution_path_reasons_name_the_construct()
    {
        Assert.Equal(DomainStrings.FormatScript_FastSyntax(construct: "TryStatement"),
            Program(Script("try {} catch (e) {}")).ExecutionPath.Reason);
        Assert.Equal(DomainStrings.FormatScript_FastName(name: "ctx.frameSource"),
            Program(Script("ctx.frameSource(0);")).ExecutionPath.Reason);
        Assert.Equal(DomainStrings.FormatScript_FastBeforeDeclaration(name: "later"),
            Program(Script("img.data[0] = later;\nconst later = 1;")).ExecutionPath.Reason);
    }

    [Fact]
    public void Compiled_render_gives_the_same_result_on_other_threads()
    {
        var program = Program(OfficialEffects.Source("shine"));
        var (sprite, source) = Sprite(new Random(11), 40, 30, 2);
        var settings = new Dictionary<string, object> { ["angle"] = 200, ["width"] = 7, ["softness"] = 1 };
        var context = new ScriptFrameContext { Progress = 0.4, Time = 0.4 };
        var expected = sprite.Clone();
        program.Render(expected, source, context, settings);
        var results = new PixelImage[8];
        Parallel.For(0, results.Length, i =>
        {
            results[i] = sprite.Clone();
            program.Render(results[i], source, context, settings);
        });
        Assert.All(results, r => Assert.True(expected.ContentEquals(r)));
    }

    // ───────────── 숫자 배열(typed array) ─────────────

    private const string TypedBody = """
            const d = img.data, n = d.length;
            const f = new Float64Array(n), i32 = new Int32Array(8), u8 = new Uint8Array(img.data), copy = img.data.slice();
            f.fill(0.5);
            i32.fill(-3.7);
            for (let i = 0; i < n; i++) { f[i] += d[i] * 1.5; u8[i] = d[i] * 3 - 200; }
            for (let k = 0; k < 8; k++) { i32[k] = k * 1e9 + 0.5; i32[k]++; }
            let t = 0;
            for (let i = 0; i < n; i += 4) {
              d[i] = u8[i]; d[i + 1] = f[i + 1] / 2; d[i + 2] = i32[(i / 4) % 8] & 255; t += copy[i + 3]; ++u8[i];
              copy[i] = -1;
            }
            d[3] = t % 256; d[7] = i32.length + u8.length % 7 + f.length % 5 + copy[0];
        """;

    [Fact]
    public void Typed_arrays_compile_and_match_Jint_values_and_statement_counts()
    {
        var program = Compiled(Script(TypedBody));
        var image = Small(6, 5);
        AssertSame(Jint(program, image, null), Fast(program, image, null), "typed arrays");
        AssertStopsAtTheSameStatement(program, image, null, sweep: true);
    }

    [Theory]
    [InlineData("const a = new Float64Array(4);\nimg.data[0] = 1;\nimg.data[1] = 2;")]
    [InlineData("const c = img.data.slice();\nimg.data[0] = 1;\nimg.data[1] = 2;")]
    [InlineData("const u = new Uint8Array(img.data);\nimg.data[0] = 1;\nimg.data[1] = 2;")]
    [InlineData("const a = new Float64Array(4);\na.fill(2);\nimg.data[0] = 1;\nimg.data[1] = 2;")]
    public void Typed_array_construction_and_fill_count_statements_like_Jint(string body)
    {
        var program = Compiled(Script(body));
        AssertStopsAtTheSameStatement(program, Small(), null, sweep: true);
    }

    [Theory]
    [InlineData("const a = new Float64Array(4); img.data[0] = a[4] === undefined ? 1 : 2;")] // 밖 읽기 → undefined
    [InlineData("const a = new Uint8Array(4); a[4] = 1; img.data[0] = a.length;")] // 밖 쓰기는 무시
    [InlineData("const a = new Int32Array(4); img.data[0] = a[1.5] === undefined ? 1 : 2;")]
    public void Typed_array_edge_cases_hand_the_call_to_Jint_with_the_same_result(string body)
    {
        var program = Program(Script(body));
        var image = Small();
        var jint = image.Clone();
        program.RenderOnJint(jint, Source, Frame, program.NormalizeSettings(null), CancellationToken.None);
        var rendered = image.Clone();
        program.Render(rendered, Source, Frame);
        Assert.True(jint.ContentEquals(rendered));
    }

    [Theory]
    [InlineData("new Float64Array(-1)")]
    [InlineData("new Uint8Array(2.5)")]
    [InlineData("new Int32Array(1e12)")]
    [InlineData("new (new Float64Array(1).constructor)(1e12)")]
    [InlineData("new Float64Array(1).buffer.byteLength")]
    [InlineData("new Uint16Array(4)")]
    public void Typed_array_sizes_are_checked_and_buffers_are_unreachable(string expression)
    {
        var program = Program(Script($"img.data[0] = ({expression}).length;"));
        var image = Small();
        Assert.Throws<ScriptEffectException>(() => program.Render(image, Source, Frame));
    }
}
