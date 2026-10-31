using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>파일 > 열기·끌어 놓기·파일 브라우저가 여는 파일의 종류.</summary>
public enum MediaKind
{
    /// <summary>열 수 없는 파일.</summary>
    None,
    /// <summary><c>.chickxel</c>(예전 <c>.gpixel</c>) 프로젝트.</summary>
    Project,
    /// <summary>그림(정지·움직이는 것 — PNG·JPG·BMP·GIF·WebP·APNG·ICO·WBMP, ffmpeg로 푸는 AVIF·HEIC).</summary>
    Image,
    /// <summary>영상(ffmpeg로 프레임을 뽑음).</summary>
    Video,
}

/// <summary>열 수 있는 파일 확장자(소문자, 점 포함)와 종류 판단.</summary>
public static class MediaFormats
{
    /// <summary>SkiaSharp가 푸는 그림(움직이는 GIF·WebP 포함). APNG는 <see cref="ApngReader"/>가 푼다.</summary>
    public static readonly IReadOnlyList<string> RasterExtensions = [".png", ".apng", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".webp", ".ico", ".wbmp"];

    /// <summary>SkiaSharp에 코덱이 없어 ffmpeg로 푸는 그림(AVIF·HEIF — 움직이는 AVIF는 영상처럼).</summary>
    public static readonly IReadOnlyList<string> FfmpegImageExtensions = [".avif", ".heic", ".heif"];

    /// <summary>움직일 수 있는 그림(열기 대화상자의 "애니메이션" 필터 — APNG는 보통 .png).</summary>
    public static readonly IReadOnlyList<string> AnimationExtensions = [".gif", ".webp", ".apng", ".png", ".avif"];

    /// <summary>영상.</summary>
    public static readonly IReadOnlyList<string> VideoExtensions = [".mp4", ".m4v", ".webm", ".mov", ".mkv", ".avi", ".wmv", ".flv", ".mpg", ".mpeg", ".ts"];

    /// <summary>모든 그림 확장자.</summary>
    public static IEnumerable<string> ImageExtensions => RasterExtensions.Concat(FfmpegImageExtensions);

    /// <summary>열 수 있는 모든 확장자(프로젝트·그림·영상).</summary>
    public static IEnumerable<string> AllExtensions => SpriteProject.FileExtensions.Concat(ImageExtensions).Concat(VideoExtensions);

    public static MediaKind KindOf(string path)
    {
        if (SpriteProject.IsProjectFile(path)) return MediaKind.Project;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (RasterExtensions.Contains(ext) || FfmpegImageExtensions.Contains(ext)) return MediaKind.Image;
        return VideoExtensions.Contains(ext) ? MediaKind.Video : MediaKind.None;
    }

    /// <summary>ffmpeg가 있어야 열 수 있는지(영상, AVIF·HEIF).</summary>
    public static bool NeedsFfmpeg(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return VideoExtensions.Contains(ext) || FfmpegImageExtensions.Contains(ext);
    }

    /// <summary>SkiaSharp·순수 C# 읽기로 푸는 그림인지(ffmpeg 없이).</summary>
    public static bool IsRaster(string path) => RasterExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>파일 대화상자 패턴("*.png").</summary>
    public static string[] Patterns(IEnumerable<string> extensions) => [.. extensions.Distinct().Select(e => "*" + e)];
}
