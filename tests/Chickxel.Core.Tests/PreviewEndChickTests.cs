using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 끝 그림 병아리(<c>previewEndChick</c>): 선언 검사, 미리보기 칸(마지막 칸 = 끝 그림 병아리, 같은 크기 캔버스, 다른 프레임 원본), 패키지·장면 미리보기.
/// 신뢰 결정은 프로세스 전체 상태라 시험마다 원문 끝에 주석을 붙여 해시를 따로 쓴다.
/// </summary>
public class PreviewEndChickTests
{
    private const int Steps = 6;

    /// <summary>마지막 칸의 다른 프레임 원본(<c>ctx.frameSource(ctx.span)</c>)을 그대로 img에 옮기는 스크립트.</summary>
    private const string CopyLastSource = """
        effect({ name: "copy last", animated: true, render(img, ctx) {
          const f = ctx.frameSource(ctx.span);
          if (!f) return;
          const d = img.data;
          for (let i = 0; i < d.length; i++) d[i] = f.data[i];
        } });
        """;

    private static PixelImage Start => PreviewChicks.Load(PreviewChicks.Default);

    private static PixelImage End => PreviewChicks.Load("wizard");

    private static ScriptEffectProgram Trusted(string source, string test)
    {
        var program = ScriptEffectProgram.Parse(source + "\n// " + test + "\n").Program!;
        ScriptLibrary.Add(program.Source);
        ScriptLibrary.SetTrust(program.ContentHash, true);
        return program;
    }

    private static ScriptEffect CopyLast([CallerMemberName] string test = "") => Trusted(CopyLastSource, test).CreateEffect();

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
    public void Declaration_is_optional_and_checked_like_preview_chick()
    {
        const string body = "render(img, ctx, s) {} })";
        Assert.Null(ScriptEffectProgram.Parse("effect({ name: \"A\", " + body).Program!.PreviewEndChick);
        var chosen = ScriptEffectProgram.Parse("effect({ name: \"A\", previewChick: \"knight\", previewEndChick: \"wizard\", " + body).Program!;
        Assert.Equal(("knight", "wizard"), (chosen.PreviewChick, chosen.PreviewEndChick));
        Assert.Equal("wizard", ScriptAsset.From("", chosen).PreviewEndChick);
        Assert.Null(ScriptAsset.From("", ScriptEffectProgram.Parse("effect({ name: \"A\", " + body).Program!).PreviewEndChick);

        foreach (var bad in new[] { "3", "\"Wizard\"", "\"\"", "\"-x\"", "\"" + new string('a', 33) + "\"", "null" })
        {
            var wrong = ScriptEffectProgram.Parse("effect({ name: \"A\", previewEndChick: " + bad + ", " + body);
            Assert.Null(wrong.Program);
            var error = Assert.Single(wrong.Errors);
            Assert.Equal(ScriptErrorKind.Declaration, error.Kind);
        }
    }

    [Fact]
    public void Frames_put_both_chicks_bottom_centered_on_one_canvas_and_the_end_chick_last()
    {
        var (first, frames) = AssetPreview.Frame(Start, End, PreviewMotion.None, 0, Steps);
        var last = frames(Steps - 1)!;
        Assert.Equal((first.Width, first.Height), (last.Width, last.Height));
        Assert.Equal(Math.Max(Start.Width, End.Width), first.Width);
        Assert.True(first.ContentEquals(frames(0)!));
        Assert.True(first.ContentEquals(frames(Steps - 2)!));
        Assert.True(last.ContentEquals(AssetPreview.Frame(Start, End, PreviewMotion.None, Steps - 1, Steps).Image));
        Assert.Null(frames(-1));
        Assert.Null(frames(Steps));

        // 불투명 바닥이 같은 줄, 그림은 그대로(불투명 픽셀 수가 같음)
        Assert.Equal(first.GetOpaqueBounds().Bottom, last.GetOpaqueBounds().Bottom);
        Assert.Equal(Start.Pixels.Count(p => p.A > 0), first.Pixels.Count(p => p.A > 0));
        Assert.Equal(End.Pixels.Count(p => p.A > 0), last.Pixels.Count(p => p.A > 0));
        Assert.False(first.ContentEquals(last));
    }

    [Fact]
    public void Without_an_end_chick_or_motion_every_frame_is_the_start_image()
    {
        var start = Start;
        var (image, frames) = AssetPreview.Frame(start, null, PreviewMotion.None, Steps - 1, Steps);
        Assert.Same(start, image);
        Assert.Same(start, frames(0));
        Assert.Null(frames(Steps));
        var (_, context) = AssetPreview.ScriptFrame(start, null, PreviewMotion.None, 2, Steps);
        Assert.Same(start, context.FrameSource!(Steps - 1));
    }

    [Fact]
    public void Render_gives_scripts_the_end_chick_as_the_last_frame_source()
    {
        IReadOnlyList<LayerEffect> effects = [CopyLast()];
        // 끝 그림 병아리: 첫 칸에서 읽은 마지막 칸 원본 = 끝 그림 병아리를 놓은 그림
        var expected = AssetPreview.Frame(Start, End, PreviewMotion.None, Steps - 1, Steps).Image;
        Assert.True(expected.ContentEquals(AssetPreview.Render(effects, Start, 0, Steps, End)));
        Assert.True(expected.ContentEquals(AssetPreview.Original(effects, Start, Steps - 1, Steps, End)));
    }

    [Fact]
    public void Script_frame_context_reads_the_end_chick_at_the_range_end()
    {
        var (image, context) = AssetPreview.ScriptFrame(Start, End, PreviewMotion.None, 1, Steps);
        Assert.NotNull(context.FrameSource);
        Assert.Equal((1, Steps - 1), (context.FrameIndex, context.FrameSpan));
        var last = context.FrameSource(Steps - 1)!;
        Assert.Equal((image.Width, image.Height), (last.Width, last.Height));
        Assert.True(last.ContentEquals(AssetPreview.Frame(Start, End, PreviewMotion.None, Steps - 1, Steps).Image));
    }

    [Fact]
    public void Package_preview_of_a_still_script_animates_to_the_end_chick()
    {
        const string still = "effect({ name: \"still\", tags: [\"a\"], render(img) {} });";
        var plain = ScriptAsset.From("", Trusted(still, nameof(Package_preview_of_a_still_script_animates_to_the_end_chick)));
        Assert.False(IsAnimatedPng(AssetPreview.Package(plain)!.Png));
        var morph = plain with { PreviewEndChick = "wizard" };
        Assert.True(IsAnimatedPng(AssetPreview.Package(morph)!.Png));
    }

    [Fact]
    public void Scene_render_places_the_end_chick_on_the_last_frame_at_the_chick_anchor()
    {
        var scene = PreviewScene.CreateDefault(Start);
        var render = new PreviewSceneRender(scene, [], Start, Steps, End);
        PixelImage At(PixelImage chick)
        {
            var image = new PixelImage(scene.Width, scene.Height);
            var at = scene.ChickOffset(chick);
            image.Blit(chick, at.X, at.Y);
            return image;
        }
        Assert.True(At(Start).ContentEquals(render.Original(0, hideSceneLayers: true)));
        Assert.True(At(Start).ContentEquals(render.Original(Steps - 2, hideSceneLayers: true)));
        Assert.True(At(End).ContentEquals(render.Original(Steps - 1, hideSceneLayers: true)));
    }
}
