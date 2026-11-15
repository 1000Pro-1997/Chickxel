using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// 내보내기 목록의 애니메이션 줄(<see cref="AnimationExport"/>) 쓰기: 프레임 시트와 같은 합성(<see cref="GifWriter.ProjectFrames"/>)을 그 범위·배율·속도·반복·배경으로
/// GIF·APNG·프레임마다 PNG로 쓴다. 내보내기 창(그때그때 내보내기)과 저장(<see cref="AnimationExport.OnSave"/>)이 같이 쓴다.
/// </summary>
public static class AnimationExportWriter
{
    /// <summary>APNG 한 프레임의 가장 짧은 길이(ms) — 미리보기·GIF와 같은 최소.</summary>
    public const int MinFrameMs = GifWriter.MinDelayCs * 10;

    /// <summary>대상 전체 경로(상대 경로면 프로젝트 파일 폴더 기준).</summary>
    public static string ResolvePath(AnimationExport export, string projectPath)
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "", export.Path));

    /// <summary>목록에 적을 경로: 프로젝트 파일 폴더 안이면 상대 경로, 밖이거나 프로젝트 경로가 없으면 전체 경로.</summary>
    public static string StoredPath(string fullPath, string? projectPath)
    {
        if (projectPath is null) return fullPath;
        var dir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "";
        var rel = Path.GetRelativePath(dir, fullPath);
        return rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel) ? fullPath : rel;
    }

    /// <summary>범위 태그(없으면 모든 프레임). 이름이 있는데 그 태그가 없어졌으면 false(내보내지 않음).</summary>
    public static bool TryRange(SpriteProject project, AnimationExport export, out AnimationTag? tag)
    {
        tag = export.TagName is { } name ? project.Tags.FirstOrDefault(t => t.Name == name) : null;
        return export.TagName is null || tag is not null;
    }

    /// <summary>프레임마다 PNG의 이름 앞부분: <c>{프로젝트}</c> 또는 <c>{프로젝트}_{태그}</c>.</summary>
    public static string FramesBaseName(SpriteProject project, AnimationTag? tag, string? projectPath)
    {
        var name = projectPath is not null ? Path.GetFileNameWithoutExtension(projectPath) : ProjectExport.CleanFileName(project.DisplayName);
        return tag is not null ? $"{name}_{ProjectExport.CleanFileName(tag.Name)}" : name;
    }

    /// <summary>프레임마다 PNG의 파일 이름: <c>{이름}_{번호:000}.png</c>(재생 순서로 1부터).</summary>
    public static string FrameFileName(string baseName, int index) => $"{baseName}_{index + 1:000}.png";

    /// <summary>
    /// 만들어질 파일들(존재와 무관): GIF·APNG = 그 파일, 프레임마다 PNG = 폴더 안 재생 순서 수만큼. 범위 태그가 없어졌으면 빈 목록.
    /// </summary>
    public static IReadOnlyList<string> OutputPaths(SpriteProject project, AnimationExport export, string projectPath)
    {
        if (!TryRange(project, export, out var tag)) return [];
        var target = ResolvePath(export, projectPath);
        if (export.Format != AnimationExportFormat.FramePngs) return [target];
        int count = tag?.GetPlaybackOrder().Count ?? project.Frames.Count;
        var baseName = FramesBaseName(project, tag, projectPath);
        return Enumerable.Range(0, count).Select(i => Path.Combine(target, FrameFileName(baseName, i))).ToList();
    }

    /// <summary>프레임 그림이 이 형식으로 쓰일 그림(배율 전): GIF = 알파 문턱·배경 합성, APNG·PNG = 배경색이 있을 때만 합성.</summary>
    public static PixelImage Prepared(PixelImage image, AnimationExportFormat format, Color32? background)
        => format == AnimationExportFormat.Gif || background is not null ? GifWriter.Prepare(image, background) : image;

    /// <summary>APNG 프레임 길이(ms): 고른 속도로 바꾼 길이(최소 <see cref="MinFrameMs"/>).</summary>
    public static int ApngFrameMs(GifTiming timing, int durationMs) => Math.Max(MinFrameMs, timing.DurationMs(durationMs));

    /// <summary>프레임 그림마다(같은 그림은 한 번) 쓸 그림: 배경 합성 후 배율만큼 최근접으로 키움.</summary>
    public static Dictionary<PixelImage, PixelImage> FinalImages(IReadOnlyList<GifFrame> frames, AnimationExportFormat format, Color32? background, int scale)
    {
        var images = new Dictionary<PixelImage, PixelImage>(ReferenceEqualityComparer.Instance);
        foreach (var f in frames)
        {
            if (images.ContainsKey(f.Image)) continue;
            var img = Prepared(f.Image, format, background);
            images[f.Image] = scale == 1 ? img : ImageTransform.ScaleNearest(img, img.Width * scale, img.Height * scale);
        }
        return images;
    }

    /// <summary>
    /// 이미 합성한 프레임(재생 순서)을 <paramref name="export"/>의 형식·설정으로 <paramref name="target"/>(파일 또는 폴더)에 쓰고 쓴 파일들을 돌려준다.
    /// 프레임마다 PNG는 <paramref name="framesBaseName"/>로 이름을 짓는다.
    /// </summary>
    public static IReadOnlyList<string> Write(IReadOnlyList<GifFrame> frames, AnimationExport export, string target, string framesBaseName, WorkProgress? progress = null)
    {
        if (frames.Count == 0) return [];
        var dir = export.Format == AnimationExportFormat.FramePngs ? target : Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        switch (export.Format)
        {
            case AnimationExportFormat.Gif:
                GifWriter.Save(frames, target, new GifOptions { Scale = export.Scale, PlayCount = export.PlayCount, Background = export.Background, Timing = export.Timing }, progress);
                return [target];
            case AnimationExportFormat.Apng:
            {
                var images = FinalImages(frames, export.Format, export.Background, export.Scale);
                var pngFrames = frames.Select(f => new PngFrame(images[f.Image], ApngFrameMs(export.Timing, f.DurationMs))).ToList();
                using var stage = progress?.Begin(pngFrames.Count);
                PngIO.SaveAnimated(pngFrames, target, export.PlayCount, stage);
                return [target];
            }
            default:
            {
                var images = FinalImages(frames, export.Format, export.Background, export.Scale);
                var paths = Enumerable.Range(0, frames.Count).Select(i => Path.Combine(target, FrameFileName(framesBaseName, i))).ToList();
                using var stage = progress?.Begin(frames.Count);
                for (int i = 0; i < frames.Count; i++)
                {
                    PngIO.Save(images[frames[i].Image], paths[i]);
                    stage?.Step(1);
                }
                return paths;
            }
        }
    }

    /// <summary>프로젝트를 합성해 한 줄을 내보낸다(저장할 때). 범위 태그가 없어졌거나 프레임이 없으면 빈 목록.</summary>
    public static IReadOnlyList<string> Export(SpriteProject project, AnimationExport export, string projectPath)
    {
        if (!TryRange(project, export, out var tag) || project.Frames.Count == 0) return [];
        var frames = GifWriter.ProjectFrames(project, tag);
        return Write(frames, export, ResolvePath(export, projectPath), FramesBaseName(project, tag, projectPath));
    }
}
