using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Assets;

/// <summary>
/// 패키지에 넣을 미리보기: 효과를 입힌 그림 PNG(움직이면 APNG)와, 효과 패키지면 같은 크기·같은 자리의 효과 없는 견본 PNG(웹 비교용, 그라데이션은 null).
/// 장면 레이어가 있는 미리보기 장면의 스크립트는 장면 레이어를 숨긴 같은 크기의 효과 그림(<paramref name="SoloPng"/>)과 원본(<paramref name="SoloOriginalPng"/>)도 둘 다 있다(웹의 장면 레이어 숨기기용, 없으면 둘 다 null).
/// </summary>
public sealed record PackagePreview(byte[] Png, byte[]? OriginalPng, byte[]? SoloPng = null, byte[]? SoloOriginalPng = null);

/// <summary>
/// 에셋 미리보기 그림: 효과 묶음을 견본(또는 지금 그림)에 입힌 모습, 그라데이션 띠. 효과 창 상자, 에셋 관리자 상세, 패키지 미리보기가 같이 쓴다.
/// </summary>
public static class AssetPreview
{
    /// <summary>움직이는 효과 미리보기의 칸 수(효과 창 상자와 같음).</summary>
    public const int Steps = 12;

    /// <summary>패키지 미리보기의 긴 변(견본 × 정수 배율, 효과 바깥 여백 포함 후 맞춤).</summary>
    public const int PngMaxSize = 128;

    /// <summary>패키지 미리보기에서 효과를 돌리는 프레임 수와 프레임 하나의 시간(ms). 같은 그림이 이어지면 한 프레임으로 합친다.</summary>
    public const int PackageFrames = 10, PackageFrameMs = 100;

    /// <summary>16×16 공 모양 견본(밝은 면·몸·그늘 세 색).</summary>
    public static PixelImage SampleSprite()
    {
        var img = new PixelImage(16, 16);
        var body = new Color32(90, 140, 230);
        var shade = new Color32(50, 80, 170);
        var light = new Color32(175, 205, 255);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                double dx = x - 7.5, dy = y - 7.5, d2 = dx * dx + dy * dy;
                if (d2 > 56) continue;
                img[x, y] = dx + dy > 5 ? shade : dx + dy < -6 ? light : body;
            }
        return img;
    }

    /// <summary>
    /// 효과 묶음을 <paramref name="source"/>에 입힌 그림 — 움직이는 효과는 <paramref name="steps"/>칸 중 <paramref name="step"/>번째.
    /// 다른 프레임을 쓰는 효과(잔상)는 앞 프레임과 달라야 보이므로 원본이 한 칸씩 오른쪽으로 가는 것처럼 만든다.
    /// 끝 그림 병아리 <paramref name="end"/>가 있으면 칸은 <see cref="Frame"/>을 따른다(마지막 칸 = <paramref name="end"/>, 늘 다른 프레임 원본을 줌).
    /// </summary>
    public static PixelImage Render(IReadOnlyList<LayerEffect> template, PixelImage source, int step, int steps = Steps, PixelImage? end = null)
    {
        var (src, frames) = Source(template, source, end, step, steps);
        var animated = LayerEffects.Animate(template, step, steps, null, frames);
        return LayerEffects.Render(src, animated, LayerEffects.Padding(template, src.Width, src.Height)).Image;
    }

    /// <summary><see cref="Render"/>의 <paramref name="step"/>칸 원본 자리에 효과 없이 놓은 견본(효과를 입힌 그림과 같은 크기).</summary>
    public static PixelImage Original(IReadOnlyList<LayerEffect> template, PixelImage source, int step, int steps = Steps, PixelImage? end = null)
    {
        var (src, _) = Source(template, source, end, step, steps);
        var padding = LayerEffects.Padding(template, src.Width, src.Height);
        var image = new PixelImage(src.Width + padding.Left + padding.Right, src.Height + padding.Top + padding.Bottom);
        image.Blit(src, padding.Left, padding.Top);
        return image;
    }

    /// <summary>
    /// 끝 그림 병아리가 있는 미리보기의 <paramref name="step"/>칸 원본과 다른 프레임 원본(<c>ctx.frameSource</c>) — 미리보기·효과 만들기 진단·검증·그라데이션 검사·AI 도구가
    /// 같이 쓴다. <paramref name="end"/>가 있으면 0..steps-2칸은 <paramref name="start"/>, 마지막 칸은 <paramref name="end"/>이고, 둘 다 같은 캔버스(폭은 큰 쪽,
    /// 높이는 불투명 바닥을 맞춘 만큼)에 가로 가운데·불투명 바닥 같은 줄로 놓아 크기가 같다. 다른 프레임 원본은 늘 주고 0..steps-1 밖은 null.
    /// <paramref name="end"/>가 없으면 <paramref name="start"/> 그대로이고 다른 프레임 원본은 없다(null).
    /// </summary>
    public static (PixelImage Image, Func<int, PixelImage?>? Frames) Frame(PixelImage start, PixelImage? end, int step, int steps)
    {
        if (end is null) return (start, null);
        int last = Math.Max(1, steps) - 1;
        int startBottom = OpaqueBottom(start), endBottom = OpaqueBottom(end);
        // 바닥 줄 위(바닥 줄 포함)와 아래 높이를 따로 맞춘다
        int above = Math.Max(startBottom, endBottom) + 1;
        int below = Math.Max(start.Height - startBottom - 1, end.Height - endBottom - 1);
        int width = Math.Max(start.Width, end.Width), height = above + below;
        PixelImage Placed(int frame)
        {
            var image = frame >= last ? end : start;
            int bottom = frame >= last ? endBottom : startBottom;
            var canvas = new PixelImage(width, height);
            canvas.Blit(image, (width - image.Width) / 2, above - 1 - bottom);
            return canvas;
        }
        return (Placed(Math.Clamp(step, 0, last)), frame => frame >= 0 && frame <= last ? Placed(frame) : null);
    }

    /// <summary>불투명 픽셀이 있는 가장 아래 줄(모두 투명이면 맨 아래 줄).</summary>
    private static int OpaqueBottom(PixelImage image)
        => image.GetOpaqueBounds() is { IsEmpty: false } b ? b.Bottom - 1 : image.Height - 1;

    /// <summary>
    /// 스크립트를 직접 돌리는 진단(효과 만들기 진단·검증·그라데이션 검사·AI 도구)의 <paramref name="step"/>칸 원본과 ctx:
    /// <see cref="Frame"/>의 원본과, <see cref="ScriptFrameContext.PreviewStep"/>에 그 다른 프레임 원본을 넣은 것(끝 그림 병아리가 없으면 다른 프레임 원본 없음).
    /// </summary>
    public static (PixelImage Image, ScriptFrameContext Context) ScriptFrame(PixelImage start, PixelImage? end, int step, int steps)
    {
        var (image, frames) = Frame(start, end, step, steps);
        return (image, ScriptFrameContext.PreviewStep(step, steps) with { FrameSource = frames });
    }

    private static (PixelImage Source, Func<int, PixelImage?>? Frames) Source(IReadOnlyList<LayerEffect> template, PixelImage source, PixelImage? end, int step, int steps)
    {
        if (end is not null) return Frame(source, end, step, steps);
        if (!template.Any(effect => EffectRegistry.Of(effect) is { } definition && definition.UsesOtherFrames(effect))) return (source, null);
        return (Shifted(source, step, steps), frame => Shifted(source, frame, steps));
    }

    private static PixelImage Shifted(PixelImage src, int frame, int steps)
    {
        var img = new PixelImage(src.Width + steps, src.Height);
        img.Blit(src, Math.Clamp(frame, 0, steps - 1), 0);
        return img;
    }

    /// <summary>그라데이션 띠(가로 위치 0→1, 디더는 칸 좌표로).</summary>
    public static PixelImage RenderGradient(GradientPreset gradient, int width = 128, int height = 16)
    {
        var img = new PixelImage(width, height);
        var stops = Gradient.Sorted(gradient.Stops);
        var alpha = Gradient.SortedAlpha(gradient.AlphaStops);
        for (int x = 0; x < width; x++)
        {
            double t = width == 1 ? 0 : x / (double)(width - 1);
            for (int y = 0; y < height; y++)
                img[x, y] = Gradient.Sample(stops, alpha, t, gradient.Interpolation, gradient.Dither, x, y, gradient.ColorSpace);
        }
        return img;
    }

    /// <summary>
    /// 패키지에 넣을 미리보기. 레시피·스크립트 에셋(기본 설정 효과 하나)은 고른 병아리(<see cref="PreviewChicks"/>)에 효과를
    /// 칸 수(레시피는 <see cref="PackageFrames"/>, 스크립트는 선언의 <see cref="ScriptAsset.PreviewFrames"/>)만큼 돌려 입힌 그림을 정수 배로 키운 APNG
    /// — 앞 칸과 같은 칸은 그 칸의 시간에 더해 빼므로 움직이지 않는 효과는 한 장짜리 PNG가 된다 — 와 첫 칸 자리의 원본이고, 그라데이션은 띠 하나다.
    /// 미리보기 장면(<see cref="ScriptAsset.Scene"/>)이 있는 스크립트는 장면 캔버스 전체(<see cref="PreviewSceneRender"/>)를 같은 규칙으로 그리고,
    /// 장면 레이어가 있으면 그 레이어를 숨긴 그림·원본(<see cref="PackagePreview.SoloPng"/>)도 같은 규칙·같은 크기로 그린다.
    /// 끝 그림 병아리(<see cref="ScriptAsset.PreviewEndChick"/>)가 있는 스크립트는 마지막 칸이 그 병아리다(<see cref="Frame"/>). 레시피에는 끝 그림 병아리가 없다.
    /// 스크립트는 실행을 허락한 원문만 그린다. 모르는 종류면 null.
    /// </summary>
    public static PackagePreview? Package(IAssetData data) => data switch
    {
        EffectRecipe recipe => EffectPreview(recipe.Effects, PreviewChicks.Load(recipe.PreviewChick), PackageFrames, null),
        ScriptAsset { Scene: { } scene } script => ScenePreview(scene, script.ToRecipe().Effects, PreviewChicks.Load(script.PreviewChick), script.PreviewFrames,
            EndChick(script.PreviewEndChick)),
        ScriptAsset script => EffectPreview(script.ToRecipe().Effects, PreviewChicks.Load(script.PreviewChick), script.PreviewFrames, EndChick(script.PreviewEndChick)),
        GradientAsset gradient => new PackagePreview(Encode([new PngFrame(RenderGradient(gradient.Gradient, PngMaxSize, 16), PackageFrameMs)]), null),
        _ => null,
    };

    /// <summary>선언의 끝 그림 병아리 ID(<c>previewEndChick</c>)의 그림. 없으면 null.</summary>
    public static PixelImage? EndChick(string? id) => id is null ? null : PreviewChicks.Load(id);

    private static PackagePreview EffectPreview(IReadOnlyList<LayerEffect> effects, PixelImage chick, int steps, PixelImage? end)
        => new(Animation(step => Render(effects, chick, step, steps, end), steps), Still(Original(effects, chick, 0, steps, end)));

    /// <summary>
    /// 장면 위 미리보기. 장면 레이어가 있으면 그 레이어를 숨긴 그림(효과는 장면 레이어를 그대로 읽음)과 원본도 같은 칸 수·같은 배율로 그린다
    /// (캔버스가 같아 크기도 같음). 칸 줄이기는 따로 하고, 그래도 미리보기 크기 한도를 넘는 것이 있으면 숨긴 그림은 둘 다 뺀다.
    /// </summary>
    private static PackagePreview ScenePreview(PreviewScene scene, IReadOnlyList<LayerEffect> effects, PixelImage chick, int steps, PixelImage? end)
    {
        var render = new PreviewSceneRender(scene, effects, chick, steps, end);
        byte[] png = Animation(step => render.Render(step), steps), original = Still(render.Original(0));
        if (scene.Layers.Count == 0) return new PackagePreview(png, original);
        byte[] solo = Animation(step => render.Render(step, hideSceneLayers: true), steps), soloOriginal = Still(render.Original(0, hideSceneLayers: true));
        long max = AssetPackageLimits.Default.MaxPreviewBytes;
        return solo.Length <= max && soloOriginal.Length <= max ? new PackagePreview(png, original, solo, soloOriginal) : new PackagePreview(png, original);
    }

    /// <summary>
    /// 효과 미리보기 APNG(<paramref name="frame"/> = 칸마다 그림). 칸마다 무늬가 달라 잘 줄지 않는 효과(칸마다 다른 잡음 등)는
    /// 칸이 많으면 패키지의 미리보기 크기 한도(<see cref="AssetPackageLimits.MaxPreviewBytes"/>)를 넘을 수 있으므로, 넘으면 두 칸씩 합쳐(앞 칸을 두 칸 시간만큼)
    /// 한도 안에 들 때까지 칸을 줄인다 — 한 바퀴 시간은 그대로다.
    /// </summary>
    private static byte[] Animation(Func<int, PixelImage> frame, int steps)
    {
        var frames = new List<PngFrame>();
        for (int step = 0; step < steps; step++)
        {
            var image = Upscale(frame(step), PngMaxSize);
            if (frames.Count > 0 && frames[^1].Image.ContentEquals(image))
                frames[^1] = frames[^1] with { DurationMs = frames[^1].DurationMs + PackageFrameMs };
            else frames.Add(new PngFrame(image, PackageFrameMs));
        }
        var png = Encode(frames);
        while (png.Length > AssetPackageLimits.Default.MaxPreviewBytes && frames.Count > 1)
        {
            frames = Enumerable.Range(0, (frames.Count + 1) / 2)
                .Select(i => 2 * i + 1 < frames.Count ? frames[2 * i] with { DurationMs = frames[2 * i].DurationMs + frames[2 * i + 1].DurationMs } : frames[2 * i])
                .ToList();
            png = Encode(frames);
        }
        return png;
    }

    /// <summary>효과 없는 원본 한 장(<see cref="Animation"/>과 같은 배율).</summary>
    private static byte[] Still(PixelImage original) => Encode([new PngFrame(Upscale(original, PngMaxSize), PackageFrameMs)]);

    private static byte[] Encode(IReadOnlyList<PngFrame> frames)
    {
        using var stream = new MemoryStream();
        PngIO.SaveAnimated(frames, stream);
        return stream.ToArray();
    }

    /// <summary>긴 변이 <paramref name="max"/>를 넘지 않는 가장 큰 정수 배로 키운다(가장 가까운 칸).</summary>
    public static PixelImage Upscale(PixelImage src, int max)
    {
        int scale = Math.Max(1, max / Math.Max(src.Width, src.Height));
        if (scale == 1) return src;
        var img = new PixelImage(src.Width * scale, src.Height * scale);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                img[x, y] = src[x / scale, y / scale];
        return img;
    }
}
