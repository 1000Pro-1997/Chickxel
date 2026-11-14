using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>
/// 열린 폴더 그림 도구: 파일 브라우저의 맨 위 폴더(<see cref="IAiHost.RootFolder"/>) 아래 프로젝트·그림을 찾아보고, 열지 않고 본다(사용자 그림과 그림체를 맞추려고).
/// 파일을 바꾸지 않으며 맨 위 폴더 밖의 경로는 읽지 않는다.
/// </summary>
public sealed partial class PixelTools
{
    private const string ListFolderTool = "list_folder_images";
    private const string GetFolderImageTool = "get_folder_image";
    /// <summary>폴더에서 살펴볼 파일 수 상한(아주 큰 폴더에서 오래 걸리지 않게).</summary>
    private const int MaxFolderScan = 20000;
    /// <summary>get_folder_image가 푸는 최대 프레임 수.</summary>
    private const int MaxFolderFrames = 64;

    private static IEnumerable<JsonObject> FolderToolDefinitions()
    {
        yield return Tool(ListFolderTool,
            "List the user's own drawings in the folder open in Chickxel's file browser (the top folder and all its subfolders): .chickxel projects and images " +
            "(PNG, GIF, WebP, JPG, ...). Use it to find existing art and keep new drawings consistent with it (scale, outline, palette, shading, proportions). " +
            "Returns JSON: root, files [{path (relative to root, use it with get_folder_image), kind (project/image), bytes, modified}], newest first, or best matches " +
            "first when `query` is given (words matched against folder and file names). Read-only.",
            new JsonObject
            {
                ["query"] = Str("Words to look for in folder and file names (any language, e.g. \"slime\", \"hero walk\", \"나무\"). Omit to list the newest drawings."),
                ["folder"] = Str("Only this subfolder (relative to the root)."),
                ["limit"] = Int("Maximum number of files (default 30).", 1, 200),
            });
        yield return Tool(GetFolderImageTool,
            "Look at one drawing from list_folder_images without opening it in the editor. as=\"image\" (default) returns it enlarged with nearest neighbor over a " +
            "light checkerboard that is NOT part of the image; with several frames and no `frame`, the frames are drawn side by side in one strip. as=\"pixels\" " +
            "returns the exact pixels of one frame in the get_pixels/draw_pixels text format (at most 128x128). Projects show the visible composite. Read-only.",
            new JsonObject
            {
                ["path"] = Str("File path relative to the root (from list_folder_images)."),
                ["frame"] = Int("Frame index (default: all frames as a strip for as=image, frame 0 for as=pixels).", 0),
                ["as"] = Enum("Output form (default image).", "image", "pixels"),
                ["scale"] = Int("Integer zoom for as=image. Default: the largest factor <= 16 that keeps the long side <= 512 px.", 1, 16),
            },
            "path");
    }

    /// <summary>파일 브라우저의 맨 위 폴더(없거나 사라졌으면 도구 오류).</summary>
    private string FolderRoot()
        => _host.RootFolder is { } root && Directory.Exists(root) ? Path.GetFullPath(root) : throw new ToolError(UiStrings.AiFolder_NoRoot);

    /// <summary>맨 위 폴더 기준 상대 경로를 전체 경로로(맨 위 폴더 밖이면 도구 오류).</summary>
    private static string InsideRoot(string root, string relative)
    {
        string full;
        try { full = Path.GetFullPath(Path.Combine(root, relative.Trim())); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { throw new ToolError(UiStrings.FormatAiFolder_Outside(path: relative)); }
        var rel = Path.GetRelativePath(root, full);
        if (rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(rel))
            throw new ToolError(UiStrings.FormatAiFolder_Outside(path: relative));
        return full;
    }

    private static string RelativeId(string root, string full) => Path.GetRelativePath(root, full).Replace('\\', '/');

    /// <summary>폴더 그림으로 보는 파일: 프로젝트와 ffmpeg 없이 푸는 그림.</summary>
    private static string? FolderKind(string path)
        => SpriteProject.IsProjectFile(path) ? "project" : MediaFormats.IsRaster(path) ? "image" : null;

    private JsonObject ListFolderImages(JsonObject? a)
    {
        var root = FolderRoot();
        var start = OptString(a, "folder") is { Length: > 0 } sub ? InsideRoot(root, sub) : root;
        if (!Directory.Exists(start)) throw new ToolError(UiStrings.FormatAiFolder_NotFound(path: OptString(a, "folder") ?? ""));
        int limit = OptInt(a, "limit", 1, 200) ?? 30;
        var words = (OptString(a, "query") ?? "").ToLowerInvariant()
            .Split([' ', ',', '_', '-', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var found = new List<(string Path, string Kind, long Bytes, DateTime Modified, int Score)>();
        int scanned = 0;
        bool truncated = false;
        try
        {
            foreach (var path in Directory.EnumerateFiles(start, "*", options))
            {
                if (++scanned > MaxFolderScan)
                {
                    truncated = true;
                    break;
                }
                if (FolderKind(path) is not { } kind) continue;
                var id = RelativeId(root, path);
                if (id.Split('/').Any(part => part.StartsWith('.'))) continue;
                int score = 0;
                if (words.Length > 0)
                {
                    var lower = id.ToLowerInvariant();
                    score = words.Count(w => lower.Contains(w, StringComparison.Ordinal));
                    if (score == 0) continue;
                }
                var info = new FileInfo(path);
                found.Add((id, kind, info.Length, info.LastWriteTimeUtc, score));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ToolError(UiStrings.FormatAiFolder_ReadFailed(path: RelativeId(root, start), message: e.Message));
        }
        var files = new JsonArray();
        foreach (var f in found.OrderByDescending(f => f.Score).ThenByDescending(f => f.Modified).Take(limit))
            files.Add(new JsonObject
            {
                ["path"] = f.Path, ["kind"] = f.Kind, ["bytes"] = f.Bytes,
                ["modified"] = f.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            });
        var result = new JsonObject { ["root"] = root, ["files"] = files, ["matched"] = found.Count };
        if (truncated) result["note"] = $"The folder is large; only the first {MaxFolderScan} files were searched. Narrow it with `folder`.";
        else if (found.Count == 0) result["note"] = words.Length > 0 ? "Nothing matched. Try other words or omit `query`." : "No drawings in this folder.";
        return TextResult(result.ToJsonString(JsonOptions));
    }

    private JsonObject GetFolderImage(JsonObject? a)
    {
        var root = FolderRoot();
        var relative = ReqString(a, "path");
        var path = InsideRoot(root, relative);
        if (!File.Exists(path) || FolderKind(path) is not { } kind) throw new ToolError(UiStrings.FormatAiFolder_NotFound(path: relative));
        IReadOnlyList<PixelImage> images;
        IReadOnlyList<int> durations;
        try
        {
            if (kind == "project")
            {
                var p = ProjectIO.Load(path);
                int count = Math.Min(p.Frames.Count, MaxFolderFrames);
                images = [.. Enumerable.Range(0, count).Select(i => p.Flatten(i))];
                durations = [.. p.Frames.Take(count).Select(f => f.DurationMs)];
            }
            else
            {
                var decoded = ImageUtil.DecodeFrames(path, MaxFolderFrames) ?? throw new InvalidDataException(Path.GetFileName(path));
                (images, durations) = decoded;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException
                                      or NotSupportedException or ArgumentException or InvalidOperationException or FormatException)
        {
            throw new ToolError(UiStrings.FormatAiFolder_ReadFailed(path: relative, message: e.Message));
        }
        if (images.Count == 0) throw new ToolError(UiStrings.FormatAiFolder_Empty(path: relative));
        var info = new JsonObject { ["path"] = RelativeId(root, path), ["kind"] = kind, ["width"] = images[0].Width, ["height"] = images[0].Height };
        return FramesView(images, durations, info, a);
    }
}
