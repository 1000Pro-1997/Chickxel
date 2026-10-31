using Chickxel.Core.IO;

namespace Chickxel.Core.Tests;

/// <summary>앱 이름 변경(Gpixel → Chickxel)으로 바뀐 설정 폴더 옮기기.</summary>
public class AppFolderMigrationTests
{
    private static string Legacy(TempDir dir) => dir.File(AppFolderMigration.LegacyName);
    private static string Current(TempDir dir) => dir.File(AppFolderMigration.CurrentName);

    private static void WriteLegacySettings(TempDir dir)
    {
        Directory.CreateDirectory(Path.Combine(Legacy(dir), "Assets", "packages"));
        File.WriteAllText(Path.Combine(Legacy(dir), "settings.json"), "{\"language\":\"ko\"}");
        File.WriteAllText(Path.Combine(Legacy(dir), "Assets", "packages", "a.json"), "{}");
    }

    [Fact]
    public void LegacyFolder_IsMovedWithEverythingInside()
    {
        using var dir = new TempDir();
        WriteLegacySettings(dir);

        Assert.Equal(AppFolderMigration.Result.Moved, AppFolderMigration.Run(dir.Path));

        Assert.False(Directory.Exists(Legacy(dir)));
        Assert.Equal("{\"language\":\"ko\"}", File.ReadAllText(Path.Combine(Current(dir), "settings.json")));
        Assert.True(File.Exists(Path.Combine(Current(dir), "Assets", "packages", "a.json")));
        Assert.Equal(AppFolderMigration.Result.None, AppFolderMigration.Run(dir.Path));
    }

    [Fact]
    public void EmptyCurrentFolder_IsReplacedByTheLegacyOne()
    {
        using var dir = new TempDir();
        WriteLegacySettings(dir);
        Directory.CreateDirectory(Current(dir));

        Assert.Equal(AppFolderMigration.Result.Moved, AppFolderMigration.Run(dir.Path));
        Assert.True(File.Exists(Path.Combine(Current(dir), "settings.json")));
    }

    [Fact]
    public void UsedCurrentFolder_IsNeverOverwritten()
    {
        using var dir = new TempDir();
        WriteLegacySettings(dir);
        Directory.CreateDirectory(Current(dir));
        File.WriteAllText(Path.Combine(Current(dir), "settings.json"), "{\"language\":\"en\"}");

        Assert.Equal(AppFolderMigration.Result.Skipped, AppFolderMigration.Run(dir.Path));
        Assert.Equal("{\"language\":\"en\"}", File.ReadAllText(Path.Combine(Current(dir), "settings.json")));
        Assert.True(File.Exists(Path.Combine(Legacy(dir), "settings.json")));
    }

    [Fact]
    public void LockedLegacyFile_IsCopiedAndTheLegacyFolderKept()
    {
        if (!OperatingSystem.IsWindows()) return; // 열린 파일이 폴더 이름 바꾸기를 막는 것은 Windows뿐
        using var dir = new TempDir();
        WriteLegacySettings(dir);
        using (new FileStream(Path.Combine(Legacy(dir), "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(AppFolderMigration.Result.Copied, AppFolderMigration.Run(dir.Path));

        Assert.Equal("{\"language\":\"ko\"}", File.ReadAllText(Path.Combine(Current(dir), "settings.json")));
        Assert.True(File.Exists(Path.Combine(Current(dir), "Assets", "packages", "a.json")));
        Assert.True(File.Exists(Path.Combine(Legacy(dir), "settings.json")));
        Assert.False(Directory.Exists(Current(dir) + ".migrating"));
    }
}
