using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 미리보기 움직임(<c>previewMotion</c>): 선언 검사(이름·목록·마지막 값 유지·한도), 선언 고치기, 미리보기 칸(움직임이 없으면 다른 프레임을 쓰는 효과도 제자리,
/// 움직임만큼 옮긴 같은 크기 캔버스, 다른 프레임 원본, 끝 그림 병아리와 함께), 효과 묶음에서 정하는 움직임, 장면·패키지 미리보기.
/// 신뢰 결정은 프로세스 전체 상태라 시험마다 원문 끝에 주석을 붙여 해시를 따로 쓴다.
/// </summary>
public class PreviewMotionTests
{
    private const int Steps = 6;
    private const string Body = "render(img, ctx, s) {} });";

    /// <summary>첫 칸의 다른 프레임 원본(<c>ctx.frameSource(0)</c>)을 그대로 img에 옮기는 스크립트.</summary>
    private const string CopyFirstSource = """
        effect({ name: "copy first", animated: true, render(img, ctx) {
          const f = ctx.frameSource(0);
          if (!f) return;
          const d = img.data;
          for (let i = 0; i < d.length; i++) d[i] = f.data[i];
        } });
        """;

    private static readonly PreviewMotion Right = PreviewMotion.FromPreset("right")!;

    private static PixelImage Start => PreviewChicks.Load(PreviewChicks.Default);

    private static PixelImage End => PreviewChicks.Load("wizard");

    private static ScriptEffectProgram Trusted(string source, [CallerMemberName] string test = "")
    {
        var program = ScriptEffectProgram.Parse(source + "\n// " + test + "\n").Program!;
        ScriptLibrary.Add(program.Source);
        ScriptLibrary.SetTrust(program.ContentHash, true);
        return program;
    }

    private static PreviewMotion Parse(string value) => ScriptEffectProgram.Parse("effect({ name: \"A\", previewMotion: " + value + ", " + Body).Program!.PreviewMotion;

    /// <summary><paramref name="image"/>를 (<paramref name="x"/>, <paramref name="y"/>)에 놓은 <paramref name="width"/>×<paramref name="height"/> 캔버스.</summary>
    private static PixelImage Placed(PixelImage image, int width, int height, int x, int y)
    {
        var canvas = new PixelImage(width, height);
        canvas.Blit(image, x, y);
        return canvas;
    }

    private static bool IsAnimatedPng(byte[] png)
    {
        for (int at = 8; at < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (Encoding.ASCII.GetString(png, at + 4, 4) == "acTL") return true;
            at += 12 + length;
        }
        return false;
    }

    [Fact]
    public void Declaration_reads_presets_and_offset_lists_and_holds_the_last_offset()
    {
        Assert.Same(PreviewMotion.None, ScriptEffectProgram.Parse("effect({ name: \"A\", " + Body).Program!.PreviewMotion);
        Assert.True(Parse("\"none\"").IsNone);
        Assert.Equal((new Point(3, 0), new Point(-3, 0), new Point(0, -3), new Point(0, 3)),
            (Parse("\"right\"").OffsetAt(3, Steps), Parse("\"left\"").OffsetAt(3, Steps), Parse("\"up\"").OffsetAt(3, Steps), Parse("\"down\"").OffsetAt(3, Steps)));
        // 칸 범위 밖은 끝 칸으로 자름
        Assert.Equal(new Point(Steps - 1, 0), Right.OffsetAt(Steps + 4, Steps));

        var list = Parse("[[0, 0], [2, -1], [-32, 32]]");
        Assert.Null(list.Preset);
        Assert.False(list.IsNone);
        Assert.Equal(new[] { new Point(0, 0), new Point(2, -1), new Point(-32, 32), new Point(-32, 32) }, Enumerable.Range(0, 4).Select(k => list.OffsetAt(k, Steps)));
        // 칸 수보다 긴 목록은 칸 수까지만 쓴다
        var longer = Parse("[[0, 0], [1, 0], [2, 0], [3, 0], [4, 0], [5, 0], [6, 0]]");
        Assert.Equal(new Point(2, 0), longer.OffsetAt(Steps, 3));
        Assert.True(Parse("[[0, 0], [0, 0]]").IsNone);
        Assert.Equal(Parse("[[0, 0], [1, 0], [2, 0]]"), PreviewMotion.FromOffsets([new(0, 0), new(1, 0), new(2, 0)]));

        string twenty = "[" + string.Join(", ", Enumerable.Repeat("[1, 1]", ScriptEffectProgram.MaxPreviewFrames)) + "]";
        Assert.Equal(new Point(1, 1), Parse(twenty).OffsetAt(0, Steps));
        foreach (var bad in new[] { "\"diagonal\"", "\"Right\"", "3", "null", "true", "[]", "[1, 2]", "[[1]]", "[[1, 2, 3]]", "[[1.5, 0]]", "[[33, 0]]", "[[0, -33]]",
                     "[[\"1\", 0]]", "[" + string.Join(", ", Enumerable.Repeat("[1, 1]", ScriptEffectProgram.MaxPreviewFrames + 1)) + "]" })
        {
            var wrong = ScriptEffectProgram.Parse("effect({ name: \"A\", previewMotion: " + bad + ", " + Body);
            Assert.Null(wrong.Program);
            Assert.Equal(ScriptErrorKind.Declaration, Assert.Single(wrong.Errors).Kind);
        }
    }

    [Fact]
    public void Script_asset_and_the_official_afterimage_carry_the_declared_motion()
    {
        var program = ScriptEffectProgram.Parse("effect({ name: \"A\", previewMotion: [[0, 0], [1, 1]], " + Body).Program!;
        Assert.Equal(program.PreviewMotion, ScriptAsset.From("", program).PreviewMotion);
        Assert.Equal("right", ScriptEffectProgram.Parse(OfficialEffects.Source("afterimage")).Program!.PreviewMotion.Preset);
    }

    [Fact]
    public void SetPreviewMotion_inserts_after_preview_frames_replaces_and_removes()
    {
        Assert.Equal("effect({ name: 'x', previewFrames: 8, previewMotion: \"right\", render() {} });",
            ScriptDeclarationEdit.SetPreviewMotion("effect({ name: 'x', previewFrames: 8, render() {} });", Right));
        Assert.Equal("effect({\n  name: \"x\",\n  previewChick: \"knight\",\n  previewMotion: [[0, 0], [1, 0], [2, -1]],\n  render() {},\n});",
            ScriptDeclarationEdit.SetPreviewMotion("effect({\n  name: \"x\",\n  previewChick: \"knight\",\n  render() {},\n});",
                PreviewMotion.FromOffsets([new(0, 0), new(1, 0), new(2, -1)])));
        Assert.Equal("effect({ name: 'x', previewMotion: \"up\", render() {} });",
            ScriptDeclarationEdit.SetPreviewMotion("effect({ name: 'x', previewMotion: [[0, 1]], render() {} });", PreviewMotion.FromPreset("up")));

        // null·"none" = 지움, 없으면 그대로
        Assert.Equal("effect({ name: 'x', render() {} });",
            ScriptDeclarationEdit.SetPreviewMotion("effect({ name: 'x', previewMotion: \"left\", render() {} });", null));
        Assert.Equal("effect({\n  name: \"x\",\n  render() {},\n});",
            ScriptDeclarationEdit.SetPreviewMotion("effect({\n  name: \"x\",\n  previewMotion: \"left\",\n  render() {},\n});", PreviewMotion.None));
        Assert.Equal("effect({ name: 'x', render() {} });", ScriptDeclarationEdit.SetPreviewMotion("effect({ name: 'x', render() {} });", PreviewMotion.None));
        Assert.Null(ScriptDeclarationEdit.SetPreviewMotion("effect({ name: 'x', render( {} });", Right));

        var edited = ScriptDeclarationEdit.SetPreviewMotion(OfficialEffects.Source("shine"), PreviewMotion.FromOffsets([new(0, 0), new(-3, 2)]))!;
        Assert.Equal(new Point(-3, 2), ScriptEffectProgram.Parse(edited).Program!.PreviewMotion.OffsetAt(4, Steps));
    }

    [Fact]
    public void Simplest_writes_a_matching_preset_or_none_instead_of_a_list()
    {
        Assert.Same(PreviewMotion.None, PreviewMotion.Simplest([new(0, 0), new(0, 0), new(0, 0)], 3));
        Assert.Equal(Right, PreviewMotion.Simplest([new(0, 0), new(1, 0), new(2, 0)], 3));
        Assert.Equal(PreviewMotion.FromPreset("down"), PreviewMotion.Simplest([new(0, 0), new(0, 1)], 2));
        var custom = PreviewMotion.Simplest([new(0, 0), new(1, 0), new(1, 0)], 3);
        Assert.Equal("[[0, 0], [1, 0], [1, 0]]", custom.Literal);
        // 칸 수 안에서만 견줌: 두 칸이면 "right"와 같다
        Assert.Equal(Right, PreviewMotion.Simplest([new(0, 0), new(1, 0), new(1, 0)], 2));
    }

    [Fact]
    public void Without_motion_frame_reading_effects_see_the_chick_in_place()
    {
        // 예전처럼 한 칸씩 오른쪽으로 가는 늘린 원본이 아니라 칸마다 같은 병아리
        IReadOnlyList<LayerEffect> effects = [Trusted(CopyFirstSource).CreateEffect()];
        for (int step = 0; step < Steps; step++)
        {
            Assert.True(Start.ContentEquals(AssetPreview.Render(effects, Start, step, Steps)), $"step {step}");
            Assert.True(Start.ContentEquals(AssetPreview.Original(effects, Start, step, Steps)), $"original {step}");
        }
    }

    [Fact]
    public void Motion_moves_the_chick_on_one_canvas_and_frame_sources_match_the_frames()
    {
        var start = Start;
        int width = start.Width + Steps - 1;
        for (int step = 0; step < Steps; step++)
        {
            var (image, frames) = AssetPreview.Frame(start, null, Right, step, Steps);
            Assert.True(Placed(start, width, start.Height, step, 0).ContentEquals(image), $"step {step}");
            for (int i = 0; i < Steps; i++) Assert.True(frames(i)!.ContentEquals(AssetPreview.Frame(start, null, Right, i, Steps).Image));
            Assert.Null(frames(Steps));
        }

        // 왼쪽·위로 가는 목록: 캔버스는 모든 칸 자리를 합친 크기, 제자리는 가장 작은 자리만큼 안쪽
        var list = PreviewMotion.FromOffsets([new(0, 0), new(-2, -3), new(1, 0)])!;
        var (_, listed) = AssetPreview.Frame(start, null, list, 0, Steps);
        Assert.True(Placed(start, start.Width + 3, start.Height + 3, 2, 3).ContentEquals(listed(0)!));
        Assert.True(Placed(start, start.Width + 3, start.Height + 3, 0, 0).ContentEquals(listed(1)!));
        Assert.True(Placed(start, start.Width + 3, start.Height + 3, 3, 3).ContentEquals(listed(Steps - 1)!));

        // 스크립트 진단의 ctx: 칸 i의 원본 = 그 칸의 그림
        var (sample, context) = AssetPreview.ScriptFrame(start, null, Right, 1, Steps);
        Assert.True(sample.ContentEquals(AssetPreview.Frame(start, null, Right, 1, Steps).Image));
        Assert.True(context.FrameSource!(3)!.ContentEquals(AssetPreview.Frame(start, null, Right, 3, Steps).Image));

        // 효과가 읽는 첫 칸 원본 = 제자리(0칸) 병아리, 그림 크기는 칸마다 같음
        IReadOnlyList<LayerEffect> effects = [Trusted(CopyFirstSource).CreateEffect()];
        var first = AssetPreview.Frame(start, null, Right, 0, Steps).Image;
        for (int step = 0; step < Steps; step++)
            Assert.True(first.ContentEquals(AssetPreview.Render(effects, start, step, Steps, motion: Right)), $"render {step}");
    }

    [Fact]
    public void End_chick_and_motion_combine_on_one_canvas()
    {
        var (_, still) = AssetPreview.Frame(Start, End, PreviewMotion.None, 0, Steps);
        var (_, moving) = AssetPreview.Frame(Start, End, Right, 0, Steps);
        var baseFirst = still(0)!;
        var baseLast = still(Steps - 1)!;
        int width = baseFirst.Width + Steps - 1;
        Assert.True(Placed(baseFirst, width, baseFirst.Height, 0, 0).ContentEquals(moving(0)!));
        Assert.True(Placed(baseFirst, width, baseFirst.Height, 2, 0).ContentEquals(moving(2)!));
        Assert.True(Placed(baseLast, width, baseLast.Height, Steps - 1, 0).ContentEquals(moving(Steps - 1)!));
    }

    [Fact]
    public void Template_without_a_motion_argument_follows_the_first_enabled_declaring_script()
    {
        var mover = Trusted("effect({ name: \"mover\", animated: true, previewMotion: \"right\", " + Body);
        var still = Trusted("effect({ name: \"still\", animated: true, " + Body);
        IReadOnlyList<LayerEffect> effects = [still.CreateEffect(), mover.CreateEffect()];
        Assert.Equal(Right, AssetPreview.MotionOf(effects));
        Assert.Same(PreviewMotion.None, AssetPreview.MotionOf([still.CreateEffect(), mover.CreateEffect() with { Enabled = false }]));
        for (int step = 0; step < Steps; step++)
            Assert.True(AssetPreview.Frame(Start, null, Right, step, Steps).Image.ContentEquals(AssetPreview.Render(effects, Start, step, Steps)), $"step {step}");
        // 넘긴 움직임이 먼저
        Assert.True(Start.ContentEquals(AssetPreview.Render(effects, Start, 3, Steps, motion: PreviewMotion.None)));
    }

    [Fact]
    public void Scene_render_places_the_chick_at_the_anchor_plus_the_frame_offset()
    {
        var scene = PreviewScene.CreateDefault(Start);
        var up = PreviewMotion.FromPreset("up")!;
        var render = new PreviewSceneRender(scene, [], Start, Steps, End, up);
        PixelImage At(PixelImage chick, int dy)
        {
            var at = scene.ChickOffset(chick);
            return Placed(chick, scene.Width, scene.Height, at.X, at.Y + dy);
        }
        Assert.True(At(Start, 0).ContentEquals(render.Original(0, hideSceneLayers: true)));
        Assert.True(At(Start, -2).ContentEquals(render.Original(2, hideSceneLayers: true)));
        Assert.True(At(End, -(Steps - 1)).ContentEquals(render.Original(Steps - 1, hideSceneLayers: true)));

        // 넘기지 않으면 효과 묶음에서 정한다
        var mover = Trusted("effect({ name: \"mover\", animated: true, previewMotion: \"up\", " + Body);
        var derived = new PreviewSceneRender(scene, [mover.CreateEffect()], Start, Steps);
        Assert.True(At(Start, -2).ContentEquals(derived.Original(2, hideSceneLayers: true)));
    }

    [Fact]
    public void Package_preview_of_a_still_script_animates_only_with_motion()
    {
        var plain = ScriptAsset.From("", Trusted("effect({ name: \"still\", tags: [\"a\"], render(img) {} });"));
        Assert.False(IsAnimatedPng(AssetPreview.Package(plain)!.Png));
        Assert.True(IsAnimatedPng(AssetPreview.Package(plain with { PreviewMotion = Right })!.Png));
    }
}
