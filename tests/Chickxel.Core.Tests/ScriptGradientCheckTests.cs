using System.Diagnostics;
using System.Runtime.CompilerServices;
using Chickxel.Core.Scripting;
using Xunit.Abstractions;

namespace Chickxel.Core.Tests;

/// <summary>
/// 그라데이션 안내(<see cref="ScriptGradientCheck"/>): 색 설정을 <c>s.키</c>로 한 번 읽어 그라데이션을 골라도 한 색인 스크립트는 안내하고,
/// <c>ctx.color</c>·<c>ctx.colorAt</c>·<c>ctx.pairColor</c>로 픽셀마다 고르는 스크립트와 쓰지 않는 색 설정, 공식 효과는 안내하지 않는다.
/// </summary>
public class ScriptGradientCheckTests(ITestOutputHelper output)
{
    private static ScriptEffectProgram Parse(string source, [CallerMemberName] string test = "")
    {
        var result = ScriptEffectProgram.Parse(source + "\n// " + test + "\n");
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return result.Program;
    }

    /// <summary>불투명한 픽셀마다 <paramref name="pick"/>(픽셀 반복 안, <c>x</c>·<c>y</c>·<c>i</c>가 있음)로 고른 색 <c>c</c>를 칠하는 효과.</summary>
    private static string Fill(string settings, string before, string pick) => $$"""
        effect({
          name: "fill",
          settings: [{{settings}}],
          render(img, ctx, s) {
            const d = img.data, w = img.width;
            {{before}}
            for (let y = 0; y < img.height; y++) {
              for (let x = 0; x < w; x++) {
                const i = (y * w + x) * 4;
                if (d[i + 3] === 0) continue;
                {{pick}}
                d[i] = c.r; d[i + 1] = c.g; d[i + 2] = c.b;
              }
            }
          },
        });
        """;

    private const string Tint = """{ key: "tint", label: "Tint", type: "color", default: "#ffcc00" }""";
    private const string Colors = """{ key: "colors", label: "Colors", type: "colorList", default: ["#ffcc00", "#00ccff"] }""";
    private const string Pairs = """{ key: "pairs", label: "Pairs", type: "colorPairs", default: [{ from: "#000000", to: "#ffcc00" }] }""";

    [Fact]
    public void Flat_color_read_once_is_hinted()
    {
        var program = Parse(Fill(Tint, "const c = s.tint;", ""));
        var hint = Assert.Single(ScriptGradientCheck.Render(program));
        Assert.Equal(("tint", "tint"), (hint.Setting.Key, hint.Slot));
        Assert.Contains("Tint", hint.Message, StringComparison.Ordinal);
        Assert.Contains("ctx.color(\"tint\", x, y)", hint.Message, StringComparison.Ordinal);
        Assert.Equal("tint", Assert.Single(ScriptGradientCheck.Static(program)).Slot);
    }

    [Fact]
    public void Per_pixel_ctx_color_is_not_hinted()
    {
        var program = Parse(Fill(Tint, "", "const c = ctx.color(\"tint\", x, y);"));
        Assert.True(program.ExecutionPath.Compiled, program.ExecutionPath.Reason);
        Assert.Empty(ScriptGradientCheck.Render(program));
        Assert.Empty(ScriptGradientCheck.Static(program));
    }

    [Fact]
    public void Color_sampled_at_one_fixed_point_is_hinted_by_the_render_check()
    {
        // 키는 ctx.color에 넘기지만 한 점에서만 고르면 그라데이션이 한 색이다 — 원문만 보는 안내는 모르고, 그려 보는 안내가 잡는다
        var program = Parse(Fill(Tint, "const c = ctx.color(\"tint\", 0, 0);", ""));
        Assert.Equal("tint", Assert.Single(ScriptGradientCheck.Render(program)).Slot);
        Assert.Empty(ScriptGradientCheck.Static(program));
    }

    [Fact]
    public void Unused_color_settings_are_not_hinted_by_the_render_check()
    {
        // 선언만 한 색과, 읽기는 하지만 그림에 쓰지 않는 색: 단색을 바꿔도 그림이 같으므로 안내하지 않는다
        var program = Parse(Fill(Tint + ", " + """{ key: "unused", label: "Unused", type: "color", default: "#123456" }""",
            "const unusedColor = s.unused;", "const c = ctx.color(\"tint\", x, y);"));
        Assert.Empty(ScriptGradientCheck.Render(program));
        // 원문만 보는 안내는 s.unused를 읽는 것만 본다
        Assert.Equal("unused", Assert.Single(ScriptGradientCheck.Static(program)).Setting.Key);
    }

    [Fact]
    public void Color_list_first_item_is_checked()
    {
        var flat = Parse(Fill(Colors, "const c = s.colors[0];", ""));
        var hint = Assert.Single(ScriptGradientCheck.Render(flat));
        Assert.Equal("colors[0]", hint.Slot);
        Assert.Contains("ctx.colorAt(\"colors\", i, x, y)", hint.Message, StringComparison.Ordinal);
        Assert.Equal("colors[0]", Assert.Single(ScriptGradientCheck.Static(flat)).Slot);

        var sampled = Parse(Fill(Colors, "", "const c = ctx.colorAt(\"colors\", 0, x, y);"));
        Assert.Empty(ScriptGradientCheck.Render(sampled));
        Assert.Empty(ScriptGradientCheck.Static(sampled));
    }

    [Fact]
    public void Color_pairs_first_to_color_is_checked()
    {
        var flat = Parse(Fill(Pairs, "const c = s.pairs[0].to;", ""));
        var hint = Assert.Single(ScriptGradientCheck.Render(flat));
        Assert.Equal("pairs[0].to", hint.Slot);
        Assert.Contains("ctx.pairColor(\"pairs\", i, \"to\", x, y)", hint.Message, StringComparison.Ordinal);
        Assert.Single(ScriptGradientCheck.Static(flat));

        var sampled = Parse(Fill(Pairs, "", "const c = ctx.pairColor(\"pairs\", 0, \"to\", x, y);"));
        Assert.Empty(ScriptGradientCheck.Render(sampled));
        Assert.Empty(ScriptGradientCheck.Static(sampled));
    }

    [Fact]
    public void Static_hint_follows_the_settings_parameter()
    {
        // 세 번째 인자 이름이 s가 아니어도, 구조 분해여도 읽는 키를 찾는다
        var renamed = Parse(Fill(Tint, "const c = s.tint;", "").Replace("render(img, ctx, s)", "render(img, ctx, opts)").Replace("s.tint", "opts.tint"));
        Assert.Equal("tint", Assert.Single(ScriptGradientCheck.Static(renamed)).Setting.Key);
        var destructured = Parse(Fill(Tint, "const c = tint;", "").Replace("render(img, ctx, s)", "render(img, ctx, { tint })"));
        Assert.Equal("tint", Assert.Single(ScriptGradientCheck.Static(destructured)).Setting.Key);
        // 키를 값으로 넘기는 호출이 있으면 그 종류는 모른다고 보고 넘어간다
        var dynamic = Parse(Fill(Tint, "const key = \"tint\", flat = s.tint;", "const c = ctx.color(key, x, y);"));
        Assert.Empty(ScriptGradientCheck.Static(dynamic));
    }

    [Fact]
    public void No_color_settings_means_no_work()
    {
        var program = Parse("""effect({ name: "plain", settings: [{ key: "n", type: "int", default: 1 }], render(img, ctx, s) {} });""");
        Assert.Empty(ScriptGradientCheck.Render(program));
        Assert.Empty(ScriptGradientCheck.Static(program));
    }

    [Fact]
    public void Official_ports_are_not_hinted()
    {
        // 공식 효과는 기본 효과와 픽셀까지 같고 기본 효과는 모든 색 설정에서 그라데이션을 따른다
        using var generous = ScriptEffectProgram.UseTimeout(TimeSpan.FromMinutes(5));
        var total = Stopwatch.StartNew();
        foreach (var id in OfficialEffects.Ids)
        {
            var program = OfficialEffects.Program(id);
            _ = program.ExecutionPath; // 빠른 경로 컴파일은 시간에서 뺀다
            var watch = Stopwatch.StartNew();
            var hints = ScriptGradientCheck.Render(program);
            output.WriteLine($"{id}: {watch.Elapsed.TotalMilliseconds:0} ms ({program.Settings.Count(s => s.IsColor)} color settings, {program.ExecutionPath.Compiled switch { true => "fast", false => "Jint" }})");
            Assert.True(hints.Count == 0, $"{id}: {string.Join(" / ", hints.Select(h => h.Message))}");
            Assert.Empty(ScriptGradientCheck.Static(program));
        }
        output.WriteLine($"total: {total.Elapsed.TotalMilliseconds:0} ms");
    }
}
