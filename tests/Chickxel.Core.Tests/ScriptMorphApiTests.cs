using System.Drawing;
using System.Runtime.CompilerServices;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 여러 프레임을 잇는 효과(모프)용 <c>ctx</c>: 효과 범위 끝 프레임 번호(<c>ctx.rangeStart</c>·<c>ctx.rangeEnd</c>), 그리는 프레임의 태그(<c>ctx.frameTag</c>),
/// 두 그림의 불투명 픽셀 짝(<c>ctx.matchPixels</c>). 빠른 경로와 Jint를 함께 본다.
/// </summary>
public class ScriptMorphApiTests
{
    private static string Unique(string source, [CallerMemberName] string test = "") => source + "\n// " + test + "\n";

    private static ScriptEffectProgram Parse(string source)
    {
        var result = ScriptEffectProgram.Parse(source);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return result.Program;
    }

    private static string Trusted(string source)
    {
        var hash = ScriptLibrary.Add(source);
        ScriptLibrary.SetTrust(hash, true);
        return hash;
    }

    // 두 길로 그린 결과(같아야 함)
    private static PixelImage RenderBoth(ScriptEffectProgram program, PixelImage image, ScriptFrameContext frame)
    {
        Assert.True(program.ExecutionPath.Compiled, program.ExecutionPath.Reason);
        var fast = image.Clone();
        program.Render(fast, fast.Bounds, frame);
        var jint = image.Clone();
        program.RenderOnJint(jint, jint.Bounds, frame, program.NormalizeSettings(null), CancellationToken.None);
        Assert.True(fast.ContentEquals(jint), "fast != Jint");
        return fast;
    }

    // ───────────── 효과 범위 ─────────────

    [Fact]
    public void Range_bounds_are_the_first_and_last_frame_of_the_effect_range()
    {
        var program = Parse(Unique("""
            effect({ name: "range", render(img, ctx) {
              const d = img.data;
              d[0] = ctx.rangeStart; d[1] = ctx.rangeEnd; d[2] = ctx.frame; d[3] = 255;
            } });
            """));
        Color32 Draw(ScriptFrameContext frame) => RenderBoth(program, new PixelImage(1, 1), frame)[0, 0];

        var ranged = new ScriptEffect { ScriptHash = program.ContentHash, Frames = new FrameRange(2, 5) };
        Assert.Equal(new Color32(2, 5, 3), Draw(ScriptFrameContext.From(ranged.AtFrame(3, 8, null))));
        // 범위 밖 프레임은 가까운 끝으로 그리지만 범위는 그대로
        Assert.Equal(new Color32(2, 5, 5), Draw(ScriptFrameContext.From(ranged.AtFrame(7, 8, null))));
        // 범위가 없으면 애니메이션 전체
        Assert.Equal(new Color32(0, 7, 4), Draw(ScriptFrameContext.From(new ScriptEffect { ScriptHash = program.ContentHash }.AtFrame(4, 8, null))));
        // 효과 만들기 미리보기: 칸 0 ~ 마지막 칸
        Assert.Equal(new Color32(0, 3, 1), Draw(ScriptFrameContext.PreviewStep(1, 4)));
    }

    // ───────────── 태그 ─────────────

    private const string TagScript = """
        effect({ name: "tag", render(img, ctx) {
          const d = img.data;
          const v = ctx.frameTag === "walk" ? 1 : ctx.frameTag === "over" ? 2 : ctx.frameTag === "" ? 3 : 4;
          for (let i = 0; i < d.length; i += 4) { d[i] = v * 50; d[i + 1] = 0; d[i + 2] = 0; d[i + 3] = 255; }
        } });
        """;

    [Fact]
    public void Frame_tag_is_the_first_tag_containing_the_drawn_frame()
    {
        var hash = Trusted(Unique(TagScript));
        var p = new SpriteProject(4, 4);
        for (int i = 1; i < 5; i++) p.DuplicateFrame(0);
        var cel = new PixelImage(4, 4);
        cel.Pixels.AsSpan().Fill(new Color32(9, 9, 9));
        for (int f = 0; f < 5; f++) p.SetCel(0, f, cel.Clone());
        p.AddTag("walk", 1, 2);
        p.AddTag("over", 2, 3);
        p.SetLayerEffects(0, [new ScriptEffect { ScriptHash = hash }]);

        // 태그 밖 "", 겹치면 먼저 만든 태그
        Assert.Equal([150, 50, 50, 100, 150], Enumerable.Range(0, 5).Select(f => (int)p.Flatten(f)[0, 0].R));
        // 태그 이름을 바꾸면 그 태그 안 프레임 결과도 바뀐다(캐시 열쇠에 태그 이름)
        p.SetTag(0, name: "run");
        Assert.Equal([150, 200, 200, 100, 150], Enumerable.Range(0, 5).Select(f => (int)p.Flatten(f)[0, 0].R));
    }

    [Fact]
    public void Frame_tag_is_empty_outside_a_project()
    {
        var program = Parse(Unique(TagScript));
        var image = new PixelImage(1, 1);
        program.Render(image, image.Bounds, ScriptFrameContext.PreviewStep(0, 4));
        Assert.Equal(150, image[0, 0].R);
        program.Render(image, image.Bounds, ScriptFrameContext.Still with { FrameTag = "over" });
        Assert.Equal(100, image[0, 0].R);
    }

    // ───────────── 픽셀 짝 ─────────────

    // ctx.frameSource(0)과 ctx.frameSource(SECOND)의 짝을 img(8×8)에 적는다: 짝 k = 픽셀 k의 (ax, ay, bx, by), 마지막 픽셀 R = 짝 수(결과가 null이면 G = 255)
    private const string MatchScript = """
        effect({ name: "match", render(img, ctx) {
          const d = img.data;
          const m = ctx.matchPixels(ctx.frameSource(0), ctx.frameSource(SECOND)MODE);
          const last = d.length - 4;
          for (let i = 0; i < d.length; i++) d[i] = 0;
          if (!m) { d[last + 1] = 255; return; }
          d[last] = m.count;
          for (let k = 0; k < m.count * 4; k++) d[k] = m.data[k];
        } });
        """;

    private static ScriptEffectProgram MatchProgram(string mode = "", int second = 1, [CallerMemberName] string test = "")
        => Parse(Unique(MatchScript.Replace("SECOND", second.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("MODE", mode, StringComparison.Ordinal), test));

    /// <summary>짝 목록(결과가 null이면 null). 빠른 경로와 Jint 결과가 같은지도 본다.</summary>
    private static List<(int AX, int AY, int BX, int BY)>? Match(ScriptEffectProgram program, PixelImage a, PixelImage b)
    {
        var frame = new ScriptFrameContext { FrameSource = i => i switch { 0 => a, 1 => b, _ => null } };
        var output = RenderBoth(program, new PixelImage(8, 8), frame);
        var last = output.Pixels[^1];
        if (last.G == 255) return null;
        return [.. output.Pixels.Take(last.R).Select(c => ((int)c.R, (int)c.G, (int)c.B, (int)c.A))];
    }

    private static PixelImage Shape(int width, int height, IEnumerable<(int X, int Y)> pixels, Func<int, int, Color32>? color = null)
    {
        var image = new PixelImage(width, height);
        foreach (var (x, y) in pixels) image[x, y] = color?.Invoke(x, y) ?? new Color32(200, 100, 50);
        return image;
    }

    private static IEnumerable<(int X, int Y)> Block(int x0, int y0, int w, int h)
        => from y in Enumerable.Range(y0, h) from x in Enumerable.Range(x0, w) select (x, y);

    [Fact]
    public void Larger_side_is_covered_once_and_smaller_side_repeats()
    {
        var program = MatchProgram();
        var many = Shape(6, 5, Block(1, 1, 4, 3));                      // 12개
        var few = Shape(7, 6, [(2, 2), (5, 3), (3, 5)]);                // 3개
        var faint = Shape(4, 4, [(1, 1)], (_, _) => new Color32(9, 9, 9, 0));

        var split = Match(program, few, many)!;
        Assert.Equal(12, split.Count);
        Assert.Equal(Block(1, 1, 4, 3).Order(), split.Select(p => (p.BX, p.BY)).Order());
        Assert.All(split.GroupBy(p => (p.AX, p.AY)), g => Assert.Equal(4, g.Count()));
        Assert.Equal([(2, 2), (3, 5), (5, 3)], split.Select(p => (p.AX, p.AY)).Distinct().Order());

        var merge = Match(program, many, few)!;
        Assert.Equal(12, merge.Count);
        Assert.Equal(Block(1, 1, 4, 3).Order(), merge.Select(p => (p.AX, p.AY)).Order());
        Assert.All(merge.GroupBy(p => (p.BX, p.BY)), g => Assert.Equal(4, g.Count()));

        // 한쪽이 비면(알파 0만) 짝 없음, 한쪽이 null이면 null
        Assert.Empty(Match(program, many, faint)!);
        Assert.Null(Match(MatchProgram(second: 2), many, few));
    }

    [Fact]
    public void Shapes_in_other_places_and_sizes_map_part_to_part()
    {
        var program = MatchProgram();
        // 같은 모양을 옮기면 같은 자리끼리
        int[,] mask = { { 1, 1, 0 }, { 1, 0, 0 }, { 1, 1, 1 } };
        var cells = (from y in Enumerable.Range(0, 3) from x in Enumerable.Range(0, 3) where mask[y, x] == 1 select (x, y)).ToList();
        var moved = Match(program, Shape(5, 5, cells.Select(c => (c.x + 1, c.y))), Shape(7, 7, cells.Select(c => (c.x + 4, c.y + 3))))!;
        Assert.Equal(cells.Count, moved.Count);
        Assert.All(moved, p => Assert.Equal((p.AX + 3, p.AY + 3), (p.BX, p.BY)));

        // 2×2를 4×4로 키우면 a 픽셀마다 b의 같은 쪽 2×2 구역
        var grown = Match(program, Shape(4, 4, Block(1, 2, 2, 2)), Shape(6, 6, Block(2, 1, 4, 4)))!;
        Assert.Equal(16, grown.Count);
        Assert.All(grown, p => Assert.Equal((p.AX - 1, p.AY - 2), ((p.BX - 2) / 2, (p.BY - 1) / 2)));
    }

    [Fact]
    public void Color_mode_pairs_by_brightness_and_results_repeat()
    {
        Color32 Gray(int v) => new((byte)v, (byte)v, (byte)v);
        var a = Shape(4, 1, [(0, 0), (1, 0), (2, 0)], (x, _) => Gray(x switch { 0 => 200, 1 => 20, _ => 120 }));
        var b = Shape(1, 4, [(0, 0), (0, 2), (0, 3)], (_, y) => Gray(y switch { 0 => 90, 2 => 250, _ => 10 }));

        var byColor = Match(MatchProgram(", \"color\""), a, b)!;
        Assert.Equal([(1, 0, 0, 3), (2, 0, 0, 0), (0, 0, 0, 2)], byColor);
        // 모르는 모드는 자리 차례(기본과 같음), 같은 입력은 늘 같은 짝
        var position = Match(MatchProgram(), a, b)!;
        Assert.Equal(position, Match(MatchProgram(", \"sideways\""), a, b));
        Assert.Equal(position, Match(MatchProgram(), a, b));
        Assert.NotEqual(position, byColor);
    }
}
