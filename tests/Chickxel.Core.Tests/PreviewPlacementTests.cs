using System.Drawing;
using System.Runtime.CompilerServices;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 미리보기 자리·피벗(<c>previewEndOffset</c>·<c>previewPivot</c>·<c>previewEndPivot</c>): 선언 검사·고치기, 미리보기 칸(끝 그림 병아리만 옮기고 기본 피벗은
/// 시작 병아리 자리 그대로, 칸마다 피벗), 장면 미리보기. 신뢰 결정은 프로세스 전체 상태라 시험마다 원문 끝에 주석을 붙여 해시를 따로 쓴다.
/// </summary>
public class PreviewPlacementTests
{
    private const int Steps = 6;
    private const string Body = "render(img, ctx, s) {} });";

    /// <summary>img를 비우고 ctx.pivot 자리 한 픽셀만 칠하는 스크립트.</summary>
    private const string MarkPivotSource = """
        effect({ name: "mark pivot", animated: true, render(img, ctx) {
          const d = img.data;
          for (let i = 0; i < d.length; i++) d[i] = 0;
          const x = Math.floor(ctx.pivot.x), y = Math.floor(ctx.pivot.y);
          if (x < 0 || y < 0 || x >= img.width || y >= img.height) return;
          const i = (y * img.width + x) * 4;
          d[i] = 255; d[i + 3] = 255;
        } });
        """;

    private static PixelImage Start => PreviewChicks.Load(PreviewChicks.Default);

    private static PixelImage End => PreviewChicks.Load("wizard");

    private static ScriptEffectProgram Trusted(string source, [CallerMemberName] string test = "")
    {
        var program = ScriptEffectProgram.Parse(source + "\n// " + test + "\n").Program!;
        ScriptLibrary.Add(program.Source);
        ScriptLibrary.SetTrust(program.ContentHash, true);
        return program;
    }

    private static ScriptEffectProgram Parse(string keys) => ScriptEffectProgram.Parse("effect({ name: \"A\", " + keys + Body).Program!;

    /// <summary>칸 그림의 불투명 범위 왼쪽 위.</summary>
    private static Point TopLeft(PixelImage image) => image.GetOpaqueBounds().Location;

    /// <summary>ctx.pivot(효과 원본 기준 px): 따로 주지 않았으면 원본 가운데.</summary>
    private static PointF PivotOf(PixelImage image, ScriptFrameContext context) => context.Pivot ?? new PointF(image.Width / 2f, image.Height / 2f);

    [Fact]
    public void Declaration_reads_whole_number_pairs_and_rejects_others()
    {
        Assert.Same(PreviewPlacement.None, Parse("").PreviewPlacement);
        var program = Parse("previewEndChick: \"wizard\", previewEndOffset: [12, -3], previewPivot: [0, 4], previewEndPivot: [-64, 64], ");
        Assert.Equal(new PreviewPlacement(new(12, -3), new(0, 4), new(-64, 64)), program.PreviewPlacement);
        Assert.Equal(program.PreviewPlacement, ScriptAsset.From("", program).PreviewPlacement);
        Assert.Same(PreviewPlacement.None, Parse("previewPivot: [0, 0], ").PreviewPlacement);

        foreach (var key in new[] { PreviewPlacement.EndOffsetKey, PreviewPlacement.PivotKey, PreviewPlacement.EndPivotKey })
            foreach (var bad in new[] { "3", "\"1, 2\"", "null", "[]", "[1]", "[1, 2, 3]", "[1.5, 0]", "[65, 0]", "[0, -65]", "[[1, 2]]" })
            {
                var wrong = ScriptEffectProgram.Parse("effect({ name: \"A\", " + key + ": " + bad + ", " + Body);
                Assert.Null(wrong.Program);
                var error = Assert.Single(wrong.Errors);
                Assert.Equal(ScriptErrorKind.Declaration, error.Kind);
                Assert.Contains(key, error.Message);
            }
    }

    [Fact]
    public void SetPreviewPlacement_writes_nonzero_values_next_to_the_chicks_and_removes_zeros()
    {
        var placement = new PreviewPlacement(new(12, -3), new(0, 4), new(-5, 1));
        Assert.Equal("effect({ name: 'x', previewChick: \"knight\", previewPivot: [0, 4], previewEndChick: \"wizard\", previewEndOffset: [12, -3], previewEndPivot: [-5, 1], render() {} });",
            ScriptDeclarationEdit.SetPreviewPlacement("effect({ name: 'x', previewChick: \"knight\", previewEndChick: \"wizard\", render() {} });", placement));
        Assert.Equal("effect({\n  name: \"x\",\n  previewPivot: [3, 0],\n  render() {},\n});",
            ScriptDeclarationEdit.SetPreviewPlacement("effect({\n  name: \"x\",\n  previewEndOffset: [1, 1],\n  render() {},\n});",
                PreviewPlacement.None with { Pivot = new(3, 0) }));
        Assert.Equal("effect({ name: 'x', render() {} });",
            ScriptDeclarationEdit.SetPreviewPlacement("effect({ name: 'x', previewPivot: [1, 2], previewEndPivot: [3, 4], render() {} });", PreviewPlacement.None));
        Assert.Null(ScriptDeclarationEdit.SetPreviewPlacement("effect({ name: 'x', render( {} });", placement));

        var edited = ScriptDeclarationEdit.SetPreviewPlacement(OfficialEffects.Source("shine"), placement)!;
        Assert.Equal(placement, ScriptEffectProgram.Parse(edited).Program!.PreviewPlacement);
    }

    [Fact]
    public void End_offset_moves_only_the_end_chick_and_the_pivot_stays_on_the_start_chick()
    {
        var offset = new PreviewPlacement(new(10, -4), Point.Empty, Point.Empty);
        var (baseFirst, baseFrames) = AssetPreview.Frame(Start, End, PreviewMotion.None, 0, Steps);
        var (first, frames) = AssetPreview.Frame(Start, End, PreviewMotion.None, 0, Steps, offset);
        var last = frames(Steps - 1)!;
        Assert.Equal((first.Width, first.Height), (last.Width, last.Height));

        // 시작 병아리끼리 견준 끝 그림 병아리 자리가 정확히 옮긴 만큼 다르다
        Point Gap(PixelImage a, PixelImage b) => new(TopLeft(b).X - TopLeft(a).X, TopLeft(b).Y - TopLeft(a).Y);
        var baseGap = Gap(baseFirst, baseFrames(Steps - 1)!);
        Assert.Equal(new Point(baseGap.X + 10, baseGap.Y - 4), Gap(first, last));
        Assert.True(first.GetOpaqueBounds().Size == baseFirst.GetOpaqueBounds().Size);

        // 피벗은 칸마다 옮기기 전 자리(시작 병아리 기준) 그대로
        var (baseImage, baseContext) = AssetPreview.ScriptFrame(Start, End, PreviewMotion.None, Steps - 1, Steps);
        var (image, context) = AssetPreview.ScriptFrame(Start, End, PreviewMotion.None, Steps - 1, Steps, offset);
        var basePivot = PivotOf(baseImage, baseContext);
        var pivot = PivotOf(image, context);
        Assert.Equal(new PointF(basePivot.X - TopLeft(baseFirst).X, basePivot.Y - TopLeft(baseFirst).Y), new PointF(pivot.X - TopLeft(first).X, pivot.Y - TopLeft(first).Y));
    }

    [Fact]
    public void Pivot_values_move_ctx_pivot_of_the_start_frames_and_the_end_frame()
    {
        var placement = new PreviewPlacement(Point.Empty, new(3, 2), new(-5, 1));
        var (baseImage, baseContext) = AssetPreview.ScriptFrame(Start, End, PreviewMotion.None, 0, Steps);
        var center = PivotOf(baseImage, baseContext);
        foreach (var (step, shift) in new[] { (0, new Point(3, 2)), (2, new Point(3, 2)), (Steps - 1, new Point(-5, 1)) })
        {
            var (image, context) = AssetPreview.ScriptFrame(Start, End, PreviewMotion.None, step, Steps, placement);
            // 피벗을 캔버스 밖으로 옮기지 않았으므로 캔버스는 그대로
            Assert.Equal((baseImage.Width, baseImage.Height), (image.Width, image.Height));
            Assert.Equal(new PointF(center.X + shift.X, center.Y + shift.Y), context.Pivot);
        }

        // 끝 그림 병아리가 없으면 끝 값은 쓰지 않고 모든 칸이 병아리 피벗
        var (alone, aloneContext) = AssetPreview.ScriptFrame(Start, null, PreviewMotion.None, Steps - 1, Steps, placement);
        Assert.Equal(new PointF(alone.Width / 2f + 3, alone.Height / 2f + 2), aloneContext.Pivot);

        // 미리보기 그리기도 같은 피벗을 효과에 준다
        IReadOnlyList<LayerEffect> effects = [Trusted(MarkPivotSource).CreateEffect()];
        var drawn = AssetPreview.Render(effects, Start, Steps - 1, Steps, End, PreviewMotion.None, placement);
        var mark = drawn.GetOpaqueBounds();
        Assert.Equal(new Rectangle((int)MathF.Floor(center.X - 5), (int)MathF.Floor(center.Y + 1), 1, 1), mark);
    }

    [Fact]
    public void Pivot_moved_past_the_chick_widens_the_canvas_to_hold_it()
    {
        var (baseImage, _) = AssetPreview.ScriptFrame(Start, null, PreviewMotion.None, 0, Steps);
        var far = PreviewPlacement.None with { Pivot = new(-PreviewPlacement.MaxOffset, 0) };
        var (image, context) = AssetPreview.ScriptFrame(Start, null, PreviewMotion.None, 0, Steps, far);
        var pivot = context.Pivot!.Value;
        Assert.InRange(pivot.X, 0, image.Width);
        Assert.Equal(baseImage.Height, image.Height);
        // 병아리는 캔버스가 왼쪽으로 넓어진 만큼 옮겨 가고, 피벗은 병아리 가운데에서 64px 왼쪽
        Assert.Equal(TopLeft(image).X - TopLeft(baseImage).X, image.Width - baseImage.Width);
        Assert.Equal(TopLeft(image).X + Start.Width / 2f - PreviewPlacement.MaxOffset - TopLeft(Start).X, pivot.X);
    }

    [Fact]
    public void Scene_render_moves_the_end_chick_and_the_frame_pivots()
    {
        var scene = PreviewScene.CreateDefault(Start);
        var placement = new PreviewPlacement(new(6, -2), new(2, 0), new(-3, 1));
        var render = new PreviewSceneRender(scene, [], Start, Steps, End, PreviewMotion.None, placement);
        var at = scene.ChickOffset(Start);
        var endAt = scene.ChickOffset(End);
        var expectedStart = new PixelImage(scene.Width, scene.Height);
        expectedStart.Blit(Start, at.X, at.Y);
        var expectedEnd = new PixelImage(scene.Width, scene.Height);
        expectedEnd.Blit(End, endAt.X + 6, endAt.Y - 2);
        Assert.True(expectedStart.ContentEquals(render.Original(0, hideSceneLayers: true)));
        Assert.True(expectedEnd.ContentEquals(render.Original(Steps - 1, hideSceneLayers: true)));

        float centerX = at.X + Start.Width / 2f, centerY = at.Y + Start.Height / 2f;
        Assert.Equal(Pivot.FromLocalPixel(centerX + 2, centerY, scene.Width, scene.Height), render.Project.GetCanvasOwnPivot(0));
        Assert.Equal(Pivot.FromLocalPixel(centerX + 2, centerY, scene.Width, scene.Height), render.Project.GetCanvasOwnPivot(Steps - 2));
        Assert.Equal(Pivot.FromLocalPixel(centerX - 3, centerY + 1, scene.Width, scene.Height), render.Project.GetCanvasOwnPivot(Steps - 1));

        // 끝 그림 병아리가 없으면 끝 값은 쓰지 않는다
        var alone = new PreviewSceneRender(scene, [], Start, Steps, null, PreviewMotion.None, placement);
        Assert.True(expectedStart.ContentEquals(alone.Original(Steps - 1, hideSceneLayers: true)));
        Assert.Equal(Pivot.FromLocalPixel(centerX + 2, centerY, scene.Width, scene.Height), alone.Project.GetCanvasOwnPivot(Steps - 1));
    }
}
