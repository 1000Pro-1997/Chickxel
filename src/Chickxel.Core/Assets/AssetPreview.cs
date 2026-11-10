using System.Drawing;
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
    /// 칸은 <see cref="Frame"/>을 따른다: 끝 그림 병아리 <paramref name="end"/>가 있으면 마지막 칸이 그 병아리, <paramref name="motion"/>(null이면 묶음에서 정함 —
    /// <see cref="MotionOf"/>)이 있으면 칸마다 병아리가 그만큼 옮겨 가고, <paramref name="placement"/>가 끝 그림 병아리 자리와 칸마다 피벗을 옮긴다.
    /// 모두 없으면 <paramref name="source"/> 그대로이고, 다른 프레임 원본은 다른 프레임을 쓰는 효과(<see cref="LayerEffects.UsesOtherFrames"/>)에만 준다(칸마다 같은 그림).
    /// </summary>
    public static PixelImage Render(IReadOnlyList<LayerEffect> template, PixelImage source, int step, int steps = Steps, PixelImage? end = null,
        PreviewMotion? motion = null, PreviewPlacement? placement = null)
    {
        var stage = Source(template, source, end, motion, placement, step, steps);
        var src = stage.Image;
        var animated = LayerEffects.Animate(template, step, steps, stage.Pivot?.Invoke(step), stage.Frames, stage.Pivot);
        var pad = LayerEffects.Padding(template, src.Width, src.Height);
        var (image, x, y) = LayerEffects.Render(src, animated, pad);
        // 미리보기 칸은 모두 같은 크기: 옮겨 간 만큼 넓혀 그렸으면 정한 여백 자리로 자른다
        if (x == -pad.Left && y == -pad.Top) return image;
        var fixedSize = new PixelImage(src.Width + pad.Left + pad.Right, src.Height + pad.Top + pad.Bottom);
        fixedSize.Blit(image, pad.Left + x, pad.Top + y);
        return fixedSize;
    }

    /// <summary><see cref="Render"/>의 <paramref name="step"/>칸 원본 자리에 효과 없이 놓은 견본(효과를 입힌 그림과 같은 크기).</summary>
    public static PixelImage Original(IReadOnlyList<LayerEffect> template, PixelImage source, int step, int steps = Steps, PixelImage? end = null,
        PreviewMotion? motion = null, PreviewPlacement? placement = null)
    {
        var src = Source(template, source, end, motion, placement, step, steps).Image;
        var padding = LayerEffects.Padding(template, src.Width, src.Height);
        var image = new PixelImage(src.Width + padding.Left + padding.Right, src.Height + padding.Top + padding.Bottom);
        image.Blit(src, padding.Left, padding.Top);
        return image;
    }

    /// <summary>
    /// <see cref="Render"/>·<see cref="Original"/> 그림 안에서 원본 캔버스(효과를 입히기 전 견본 칸)의 자리 — 효과 여백을 뺀 자리이고 모든 칸이 같다.
    /// 레이어에 넣으면 이 밖은 캔버스에 잘린다(캔버스를 늘리기 전까지).
    /// </summary>
    public static Rectangle Canvas(IReadOnlyList<LayerEffect> template, PixelImage source, int steps = Steps, PixelImage? end = null,
        PreviewMotion? motion = null, PreviewPlacement? placement = null)
    {
        var src = Source(template, source, end, motion, placement, 0, steps).Image;
        var padding = LayerEffects.Padding(template, src.Width, src.Height);
        return new Rectangle(padding.Left, padding.Top, src.Width, src.Height);
    }

    /// <summary>
    /// 효과 묶음의 미리보기 움직임: 켜진 스크립트 효과 중 실행할 수 있는 원문이 움직임(<see cref="ScriptEffectProgram.PreviewMotion"/>)을 선언한 첫 효과의 것.
    /// 없으면 <see cref="PreviewMotion.None"/>. 효과 창 상자·에셋 미리보기·레시피 패키지 미리보기처럼 선언을 따로 넘기지 않는 곳이 쓴다.
    /// </summary>
    public static PreviewMotion MotionOf(IReadOnlyList<LayerEffect> template)
    {
        foreach (var effect in template)
            if (effect is ScriptEffect { Enabled: true } script && ScriptLibrary.Runnable(script.ScriptHash) is { PreviewMotion.IsNone: false } program)
                return program.PreviewMotion;
        return PreviewMotion.None;
    }

    /// <summary>
    /// 미리보기의 <paramref name="step"/>칸 원본과 다른 프레임 원본(<c>ctx.frameSource</c>, 0..steps-1 밖은 null) — 미리보기·효과 만들기 진단·검증·그라데이션 검사·AI 도구가
    /// 같이 쓴다. 칸 k는 <paramref name="start"/>(<paramref name="end"/>가 있으면 마지막 칸은 <paramref name="end"/>)를 제자리 + <paramref name="motion"/>의 k칸 자리에 놓은 것이다.
    /// 제자리: 두 병아리를 가로 가운데·불투명 바닥 같은 줄로 맞춘 자리(폭은 큰 쪽, 높이는 바닥을 맞춘 만큼). 캔버스는 모든 칸의 자리를 합친 크기라 칸마다 크기가 같다.
    /// <paramref name="placement"/>(<see cref="PreviewPlacement"/>)가 있으면 끝 그림 병아리는 제자리에서 그만큼 더 옮기고, 캔버스는 옮긴 끝 그림 병아리와 피벗 자리까지 넓힌다
    /// (피벗은 <see cref="ScriptFrame"/>·<see cref="Render"/>가 준다).
    /// <paramref name="end"/>도 움직임도 자리·피벗도 없으면 <paramref name="start"/> 그대로이고 다른 프레임 원본도 칸마다 <paramref name="start"/>다.
    /// </summary>
    public static (PixelImage Image, Func<int, PixelImage?> Frames) Frame(PixelImage start, PixelImage? end, PreviewMotion motion, int step, int steps,
        PreviewPlacement? placement = null)
    {
        var stage = Stage(start, end, motion, placement, step, steps);
        return (stage.Image, stage.Frames!);
    }

    /// <summary><see cref="Frame"/>의 칸 그림·다른 프레임 원본과 칸마다 피벗(효과 원본 왼쪽 위 기준 px, null이면 모든 칸이 원본 가운데).</summary>
    private readonly record struct Staged(PixelImage Image, Func<int, PixelImage?>? Frames, Func<int, PointF?>? Pivot);

    private static Staged Stage(PixelImage start, PixelImage? end, PreviewMotion motion, PreviewPlacement? placement, int step, int steps)
    {
        int last = Math.Max(1, steps) - 1;
        var place = (placement ?? PreviewPlacement.None).For(end is not null);
        if (end is null && motion.IsNone && place.IsNone) return new(start, frame => frame >= 0 && frame <= last ? start : null, null);
        int startBottom = OpaqueBottom(start), endBottom = end is null ? startBottom : OpaqueBottom(end);
        // 바닥 줄 위(바닥 줄 포함)와 아래 높이를 따로 맞춘다
        int above = Math.Max(startBottom, endBottom) + 1;
        int below = Math.Max(start.Height - startBottom - 1, end is null ? 0 : end.Height - endBottom - 1);
        int baseWidth = Math.Max(start.Width, end?.Width ?? 0);
        int minX = 0, maxX = 0, minY = 0, maxY = 0;
        for (int k = 0; k <= last; k++)
        {
            var o = motion.OffsetAt(k, steps);
            (minX, maxX, minY, maxY) = (Math.Min(minX, o.X), Math.Max(maxX, o.X), Math.Min(minY, o.Y), Math.Max(maxY, o.Y));
        }
        // 자리·피벗을 옮기기 전 캔버스(모든 칸의 제자리 + 움직임). 기본 피벗은 그 가운데
        int baseCanvasWidth = baseWidth + maxX - minX, baseCanvasHeight = above + below + maxY - minY;
        bool IsEnd(int frame) => end is not null && frame >= last;
        Point At(int frame)
        {
            bool isEnd = IsEnd(frame);
            var image = isEnd ? end! : start;
            var o = motion.OffsetAt(frame, steps);
            var shift = isEnd ? place.EndOffset : Point.Empty;
            return new((baseWidth - image.Width) / 2 + o.X - minX + shift.X, above - 1 - (isEnd ? endBottom : startBottom) + o.Y - minY + shift.Y);
        }
        PointF PivotAt(int frame)
        {
            var o = place.PivotAt(frame, steps, end is not null);
            return new(baseCanvasWidth / 2f + o.X, baseCanvasHeight / 2f + o.Y);
        }
        // 캔버스: 옮기기 전 캔버스에 옮긴 끝 그림 병아리와 피벗 자리까지 넣은 범위
        int left = 0, top = 0, right = baseCanvasWidth, bottom = baseCanvasHeight;
        if (end is not null && !place.EndOffset.IsEmpty)
        {
            var p = At(last);
            (left, top, right, bottom) = (Math.Min(left, p.X), Math.Min(top, p.Y), Math.Max(right, p.X + end.Width), Math.Max(bottom, p.Y + end.Height));
        }
        if (place.MovesPivot)
            foreach (int frame in (int[])[0, last])
            {
                var p = PivotAt(frame);
                (left, top) = (Math.Min(left, (int)MathF.Floor(p.X)), Math.Min(top, (int)MathF.Floor(p.Y)));
                (right, bottom) = (Math.Max(right, (int)MathF.Ceiling(p.X)), Math.Max(bottom, (int)MathF.Ceiling(p.Y)));
            }
        int width = right - left, height = bottom - top;
        PixelImage Placed(int frame)
        {
            var p = At(frame);
            var canvas = new PixelImage(width, height);
            canvas.Blit(IsEnd(frame) ? end! : start, p.X - left, p.Y - top);
            return canvas;
        }
        Func<int, PointF?>? pivot = place.IsNone ? null : frame =>
        {
            var p = PivotAt(Math.Clamp(frame, 0, last));
            return new PointF(p.X - left, p.Y - top);
        };
        return new(Placed(Math.Clamp(step, 0, last)), frame => frame >= 0 && frame <= last ? Placed(frame) : null, pivot);
    }

    /// <summary>불투명 픽셀이 있는 가장 아래 줄(모두 투명이면 맨 아래 줄).</summary>
    private static int OpaqueBottom(PixelImage image)
        => image.GetOpaqueBounds() is { IsEmpty: false } b ? b.Bottom - 1 : image.Height - 1;

    /// <summary>
    /// 스크립트를 직접 돌리는 진단(효과 만들기 진단·검증·그라데이션 검사·AI 도구)의 <paramref name="step"/>칸 원본과 ctx:
    /// <see cref="Frame"/>의 원본과, <see cref="ScriptFrameContext.PreviewStep"/>에 그 다른 프레임 원본과 그 칸의 피벗(<paramref name="placement"/>)을 넣은 것.
    /// </summary>
    public static (PixelImage Image, ScriptFrameContext Context) ScriptFrame(PixelImage start, PixelImage? end, PreviewMotion motion, int step, int steps,
        PreviewPlacement? placement = null)
    {
        var stage = Stage(start, end, motion, placement, step, steps);
        return (stage.Image, ScriptFrameContext.PreviewStep(step, steps) with { FrameSource = stage.Frames, Pivot = stage.Pivot?.Invoke(step) });
    }

    private static Staged Source(IReadOnlyList<LayerEffect> template, PixelImage source, PixelImage? end, PreviewMotion? motion, PreviewPlacement? placement,
        int step, int steps)
    {
        motion ??= MotionOf(template);
        if (end is null && motion.IsNone && (placement ?? PreviewPlacement.None).For(false).IsNone && !LayerEffects.UsesOtherFrames(template))
            return new(source, null, null);
        return Stage(source, end, motion, placement, step, steps);
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
    /// 끝 그림 병아리(<see cref="ScriptAsset.PreviewEndChick"/>)가 있는 스크립트는 마지막 칸이 그 병아리고, 움직임(<see cref="ScriptAsset.PreviewMotion"/>)이 있으면
    /// 병아리가 칸마다 옮겨 가며, 자리·피벗(<see cref="ScriptAsset.PreviewPlacement"/>)이 끝 그림 병아리 자리와 칸마다 피벗을 옮긴다(<see cref="Frame"/>).
    /// 레시피에는 끝 그림 병아리·자리·피벗이 없고 움직임은 효과 묶음에서 정한다(<see cref="MotionOf"/>).
    /// 스크립트는 실행을 허락한 원문만 그린다. 모르는 종류면 null.
    /// </summary>
    public static PackagePreview? Package(IAssetData data) => data switch
    {
        EffectRecipe recipe => EffectPreview(recipe.Effects, PreviewChicks.Load(recipe.PreviewChick), PackageFrames, null, null, null),
        ScriptAsset { Scene: { } scene } script => ScenePreview(scene, script.ToRecipe().Effects, PreviewChicks.Load(script.PreviewChick), script.PreviewFrames,
            EndChick(script.PreviewEndChick), script.PreviewMotion, script.PreviewPlacement),
        ScriptAsset script => EffectPreview(script.ToRecipe().Effects, PreviewChicks.Load(script.PreviewChick), script.PreviewFrames, EndChick(script.PreviewEndChick),
            script.PreviewMotion, script.PreviewPlacement),
        GradientAsset gradient => new PackagePreview(Encode([new PngFrame(RenderGradient(gradient.Gradient, PngMaxSize, 16), PackageFrameMs)]), null),
        _ => null,
    };

    /// <summary>선언의 끝 그림 병아리 ID(<c>previewEndChick</c>)의 그림. 없으면 null.</summary>
    public static PixelImage? EndChick(string? id) => id is null ? null : PreviewChicks.Load(id);

    private static PackagePreview EffectPreview(IReadOnlyList<LayerEffect> effects, PixelImage chick, int steps, PixelImage? end, PreviewMotion? motion,
        PreviewPlacement? placement)
    {
        motion ??= MotionOf(effects);
        return new(Animation(step => Render(effects, chick, step, steps, end, motion, placement), steps),
            Still(Original(effects, chick, 0, steps, end, motion, placement)));
    }

    /// <summary>
    /// 장면 위 미리보기. 장면 레이어가 있으면 그 레이어를 숨긴 그림(효과는 장면 레이어를 그대로 읽음)과 원본도 같은 칸 수·같은 배율로 그린다
    /// (캔버스가 같아 크기도 같음). 칸 줄이기는 따로 하고, 그래도 미리보기 크기 한도를 넘는 것이 있으면 숨긴 그림은 둘 다 뺀다.
    /// </summary>
    private static PackagePreview ScenePreview(PreviewScene scene, IReadOnlyList<LayerEffect> effects, PixelImage chick, int steps, PixelImage? end, PreviewMotion motion,
        PreviewPlacement placement)
    {
        var render = new PreviewSceneRender(scene, effects, chick, steps, end, motion, placement);
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
