using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scene3D;

/// <summary>3D 모델에서 뽑을 프레임.</summary>
public enum ModelFrameMode
{
    /// <summary>지금 자세 한 장.</summary>
    Still,
    /// <summary>한 바퀴 돌려 보기: 가로 각도를 프레임마다 360°/프레임 수씩 더한다(8방향 스프라이트 등).</summary>
    Turntable,
    /// <summary>모델에 든 애니메이션 하나를 초당 프레임 수대로.</summary>
    Animation,
}

/// <summary>프레임 뽑기 설정(<see cref="ModelFrames"/>).</summary>
public sealed record ModelFrameOptions
{
    public ModelFrameMode Mode { get; init; } = ModelFrameMode.Still;
    /// <summary>돌려 보기 프레임 수(방향 수).</summary>
    public int TurntableFrames { get; init; } = 8;
    /// <summary>애니메이션 번호(<see cref="ModelScene.Animations"/>).</summary>
    public int Animation { get; init; }
    public double Fps { get; init; } = 12;
    /// <summary>애니메이션에서 뽑을 최대 프레임 수(길면 앞쪽만).</summary>
    public int MaxFrames { get; init; } = 120;
    /// <summary>한 장·돌려 보기의 프레임 길이(ms).</summary>
    public int StillDurationMs { get; init; } = 100;

    public const int MaxTurntableFrames = 64;
    public const int MaxFramesLimit = 1000;
    public const double MinFps = 1, MaxFps = 60;
}

/// <summary>프레임 하나: 애니메이션 번호(−1 = 쉬는 자세)·시각(초)·더할 가로 각도(도).</summary>
public readonly record struct ModelFrame(int Animation, float Time, float YawOffset);

/// <summary>
/// 3D 모델을 프레임 그림들로: 설정에서 프레임 목록을 만들고(<see cref="Plan"/>), 모든 프레임 자세를 합친 범위에 카메라를 맞춰(프레임마다 크기·자리가 흔들리지 않게) 그린다.
/// </summary>
public static class ModelFrames
{
    /// <summary>설정대로 뽑을 프레임들(애니메이션이 없거나 번호가 틀리면 쉬는 자세 한 장).</summary>
    public static IReadOnlyList<ModelFrame> Plan(ModelScene scene, ModelFrameOptions o)
    {
        switch (o.Mode)
        {
            case ModelFrameMode.Turntable:
            {
                int n = Math.Clamp(o.TurntableFrames, 1, ModelFrameOptions.MaxTurntableFrames);
                var frames = new ModelFrame[n];
                for (int i = 0; i < n; i++) frames[i] = new ModelFrame(-1, 0, 360f * i / n);
                return frames;
            }
            case ModelFrameMode.Animation when o.Animation >= 0 && o.Animation < scene.Animations.Count:
            {
                double fps = Math.Clamp(o.Fps, ModelFrameOptions.MinFps, ModelFrameOptions.MaxFps);
                float duration = scene.Animations[o.Animation].Duration;
                // 끝 시각은 첫 시각과 같은 자세인 경우가 많아(되풀이 애니메이션) 넣지 않는다 — 길이 0이면 한 장.
                int n = Math.Max(1, (int)Math.Ceiling(duration * fps - 1e-6));
                n = Math.Min(n, Math.Clamp(o.MaxFrames, 1, ModelFrameOptions.MaxFramesLimit));
                var frames = new ModelFrame[n];
                for (int i = 0; i < n; i++) frames[i] = new ModelFrame(o.Animation, (float)(i / fps), 0);
                return frames;
            }
            default:
                return [new ModelFrame(-1, 0, 0)];
        }
    }

    /// <summary>프레임 하나의 길이(ms).</summary>
    public static int DurationMs(ModelFrameOptions o) => o.Mode == ModelFrameMode.Animation
        ? Math.Max(1, (int)Math.Round(1000 / Math.Clamp(o.Fps, ModelFrameOptions.MinFps, ModelFrameOptions.MaxFps)))
        : Math.Max(1, o.StillDurationMs);

    /// <summary>프레임들의 자세(같은 애니메이션·시각은 한 번만 계산 — 돌려 보기는 쉬는 자세 하나).</summary>
    public static PosedMesh[] Pose(ModelScene scene, IReadOnlyList<ModelFrame> frames, CancellationToken ct = default)
    {
        var poses = new PosedMesh[frames.Count];
        var cache = new Dictionary<(int, float), PosedMesh>();
        for (int i = 0; i < frames.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var key = (frames[i].Animation, frames[i].Time);
            if (!cache.TryGetValue(key, out var pose)) cache[key] = pose = ModelPose.Pose(scene, frames[i].Animation, frames[i].Time);
            poses[i] = pose;
        }
        return poses;
    }

    /// <summary>자세들을 모두 담는 범위.</summary>
    public static ModelBounds Fit(IEnumerable<PosedMesh> poses)
    {
        var fit = ModelBounds.Empty;
        foreach (var p in poses) fit = fit.Union(ModelRenderer.Bounds(p));
        return fit;
    }

    /// <summary>
    /// 프레임을 모두 그린다(<paramref name="progress"/> = 끝낸 프레임 수). 돌려 보기는 프레임마다 <see cref="ModelRenderSettings.Yaw"/>에 그 프레임의 각도를 더한다.
    /// 다듬기는 모두 그린 뒤: 반투명 없애기 → 색 줄이기(모든 프레임의 픽셀로 만든 팔레트 하나를 함께 씀 — 프레임마다 색이 흔들리지 않게) → 외곽선
    /// (자리는 프레임마다 그릴 때 찾아 두고 깊이·법선 층은 버려, 프레임이 많아도 메모리는 그림 수만큼).
    /// </summary>
    public static DecodedAnimation Render(ModelScene scene, ModelRenderSettings settings, ModelFrameOptions options, IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var frames = Plan(scene, options);
        var poses = Pose(scene, frames, ct);
        var fit = Fit(poses);
        var images = new PixelImage[frames.Count];
        var outlines = settings.HasOutline ? new int[frames.Count][] : null;
        for (int i = 0; i < frames.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var layers = ModelRenderer.RenderLayers(poses[i], settings with { Yaw = settings.Yaw + frames[i].YawOffset }, fit, ct);
            images[i] = layers.Image;
            if (outlines != null) outlines[i] = ModelPixelPass.FindOutline(layers, settings);
            progress?.Report(i + 1);
        }
        if (settings.HardAlpha)
            foreach (var image in images)
            {
                ct.ThrowIfCancellationRequested();
                ModelPixelPass.HardAlpha(image);
            }
        if (settings.Colors > 0)
        {
            ct.ThrowIfCancellationRequested();
            var palette = ModelPixelPass.Palette(images, settings.Colors);
            foreach (var image in images)
            {
                ct.ThrowIfCancellationRequested();
                ModelPixelPass.Remap(image, palette);
            }
        }
        if (outlines != null)
            for (int i = 0; i < images.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                ModelPixelPass.PaintOutline(images[i], outlines[i], settings);
            }
        int ms = DurationMs(options);
        return new DecodedAnimation(images[0].Width, images[0].Height, images, [.. Enumerable.Repeat(ms, images.Length)]);
    }
}
