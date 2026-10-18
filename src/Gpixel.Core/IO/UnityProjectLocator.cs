namespace Gpixel.Core.IO;

/// <summary>경로가 Unity 프로젝트 안에 있는지 찾는다.</summary>
public static class UnityProjectLocator
{
    /// <summary>
    /// 파일이나 폴더 경로에서 위로 올라가며 <c>Assets</c>와 <c>ProjectSettings</c> 폴더를 모두 가진
    /// 프로젝트 루트를 찾는다. 없으면 null.
    /// </summary>
    public static string? FindProjectRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        var dir = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full).Directory;
        for (; dir is not null; dir = dir.Parent)
        {
            if (IsProjectRoot(dir.FullName)) return dir.FullName;
        }
        return null;
    }

    /// <summary>폴더가 Unity 프로젝트 루트(Assets + ProjectSettings)인지.</summary>
    public static bool IsProjectRoot(string directory)
        => Directory.Exists(Path.Combine(directory, "Assets"))
           && Directory.Exists(Path.Combine(directory, "ProjectSettings"));

    /// <summary>
    /// 경로가 Unity가 가져오는 위치(프로젝트의 <c>Assets</c> 또는 <c>Packages</c> 아래)에 있는지.
    /// 메타 쓰기 기본값 결정에 쓴다.
    /// </summary>
    public static bool IsInUnityAssets(string path)
    {
        var root = FindProjectRoot(path);
        if (root is null) return false;
        var full = Path.GetFullPath(path);
        return IsUnder(full, Path.Combine(root, "Assets")) || IsUnder(full, Path.Combine(root, "Packages"));
    }

    private static bool IsUnder(string path, string folder)
    {
        var f = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return path.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }
}
