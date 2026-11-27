using Chickxel.Core.IO;

namespace Chickxel.Core.Tests;

public class EngineProjectsTests
{
    private static string P(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    [Fact]
    public void GodotProjectsList_ReadsSectionsAndFavorites()
    {
        const string cfg = "[/home/me/games/slime]\n\nfavorite=true\n\n[/home/me/games/tiles]\n\nfavorite=false\n";
        var list = EngineProjects.ParseGodotProjects(cfg);
        Assert.Equal([(P("/home/me/games/slime"), true), (P("/home/me/games/tiles"), false)], list);
    }

    [Fact]
    public void GodotProjectFile_ReadsNameAndVersion()
    {
        const string text = "config_version=5\n\n[application]\n\nconfig/name=\"Slime Farm\"\nconfig/features=PackedStringArray(\"4.3\", \"GL Compatibility\")\n";
        Assert.Equal(("Slime Farm", "4.3"), EngineProjects.ParseGodotProject(text));
        Assert.Equal((null, ""), EngineProjects.ParseGodotProject(""));
    }

    [Fact]
    public void UnrealRecent_ReadsNewAndOldForms()
    {
        const string ini = "[/Script/UnrealEd.EditorSettings]\n"
            + "RecentlyOpenedProjectFiles=(ProjectName=\"D:/Unreal/Dungeon/Dungeon.uproject\",LastOpenTime=2026.08.14-13.30.00)\n"
            + "RecentlyOpenedProjectFiles=D:/Unreal/Old/Old.uproject\n"
            + "RecentlyOpenedProjectFiles=(ProjectName=\"\",LastOpenTime=0001.01.01-00.00.00)\n";
        var list = EngineProjects.ParseUnrealRecent(ini);
        Assert.Equal(2, list.Count);
        Assert.Equal(P("D:/Unreal/Dungeon/Dungeon.uproject"), list[0].UProject);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 13, 30, 0, TimeSpan.Zero), list[0].Opened);
        Assert.Equal(P("D:/Unreal/Old/Old.uproject"), list[1].UProject);
        Assert.Null(list[1].Opened);
    }

    [Fact]
    public void AsepriteFolders_MergeFilesAndPathsAndKeepPinned()
    {
        var a = P("/art/pixel");
        var b = P("/art/other");
        string ini = $"[RecentFiles]\n0 = {a}/slime.aseprite\n1 = {b}/x.aseprite\n[RecentPaths]\n0 = {a}\n[PinnedPaths]\n0 = {b}\n";
        Assert.Equal([(a, false), (b, true)], EngineProjects.ParseAsepriteFolders(ini));
    }

    [Fact]
    public void BestArtFolder_PrefersSpritesThenDescendsOnce()
    {
        var tree = new Dictionary<string, string[]>
        {
            [P("/g")] = [P("/g/addons"), P("/g/assets")],
            [P("/g/assets")] = [P("/g/assets/audio"), P("/g/assets/Sprites")],
            [P("/u/Assets")] = [P("/u/Assets/Scripts"), P("/u/Assets/Textures"), P("/u/Assets/Art")],
            [P("/plain")] = [P("/plain/src")],
        };
        IEnumerable<string> Dirs(string d) => tree.GetValueOrDefault(d) ?? [];
        Assert.Equal(P("/g/assets/Sprites"), EngineProjects.BestArtFolder(P("/g"), Dirs));
        // Art가 Textures보다 앞(이름 차례), 그 아래에는 더 없음.
        Assert.Equal(P("/u/Assets/Art"), EngineProjects.BestArtFolder(P("/u/Assets"), Dirs));
        Assert.Equal(P("/plain"), EngineProjects.BestArtFolder(P("/plain"), Dirs));
    }

    [Fact]
    public void Sort_FavoritesFirstThenRecentAndDropsSameFolder()
    {
        EngineProject E(string title, string folder, bool fav, int day) =>
            new(GameEngine.Godot, title, folder, folder, "", new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero), fav, true);
        var sorted = EngineProjects.Sort([E("old", "/a", false, 1), E("new", "/b", false, 5), E("fav", "/c", true, 2), E("dup", "/b", false, 3)]);
        Assert.Equal(["fav", "new", "old"], sorted.Select(p => p.Title));
    }
}
