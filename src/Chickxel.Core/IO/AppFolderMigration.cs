namespace Chickxel.Core.IO;

/// <summary>
/// 앱 이름이 Gpixel에서 Chickxel로 바뀌며 설정 폴더 이름도 바뀌었다(<c>…/Gpixel</c> → <c>…/Chickxel</c>).
/// 앱을 켤 때 설정·에셋·기록·휴지통이 든 예전 폴더를 새 이름으로 옮긴다.
/// </summary>
public static class AppFolderMigration
{
    public const string LegacyName = "Gpixel";
    public const string CurrentName = "Chickxel";

    public enum Result
    {
        /// <summary>예전 폴더가 없다.</summary>
        None,
        /// <summary>새 폴더가 이미 있고 비어 있지 않아 건드리지 않았다.</summary>
        Skipped,
        /// <summary>예전 폴더를 새 이름으로 바꿨다.</summary>
        Moved,
        /// <summary>통째로 옮기지 못해(예전 앱이 켜져 있는 등) 복사했다. 예전 폴더는 남아 있다.</summary>
        Copied,
        /// <summary>복사도 실패했다. 새 폴더는 만들지 않았다.</summary>
        Failed,
    }

    /// <summary>
    /// <paramref name="baseDirectory"/> 아래 <see cref="LegacyName"/> 폴더를 <see cref="CurrentName"/>으로 옮긴다.
    /// 새 폴더가 비어 있으면 지우고 옮기고, 비어 있지 않으면 이미 옮겼거나 새로 쓰기 시작한 것이라 그대로 둔다.
    /// 이름 바꾸기가 안 되면 옆의 임시 폴더에 모두 복사한 뒤 새 이름으로 바꾼다(중간에 실패하면 임시 폴더를 지워 반쪽 폴더를 남기지 않음).
    /// </summary>
    public static Result Run(string baseDirectory)
    {
        var legacy = Path.Combine(baseDirectory, LegacyName);
        var current = Path.Combine(baseDirectory, CurrentName);
        try
        {
            if (!Directory.Exists(legacy)) return Result.None;
            if (Directory.Exists(current))
            {
                if (Directory.EnumerateFileSystemEntries(current).Any()) return Result.Skipped;
                Directory.Delete(current);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failed;
        }
        try
        {
            Directory.Move(legacy, current);
            return Result.Moved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        var staging = current + ".migrating";
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            CopyTree(legacy, staging);
            Directory.Move(staging, current);
            return Result.Copied;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
            return Result.Failed;
        }
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }
}
