using System.Globalization;
using System.Text.RegularExpressions;

namespace Chickxel.Core.IO;

/// <summary>프로젝트 목록을 읽어 올 게임 엔진·그림 프로그램.</summary>
public enum GameEngine
{
    Unity,
    Godot,
    Unreal,
    Aseprite,
}

/// <summary>
/// 엔진·그림 프로그램에 등록된(최근에 연) 프로젝트 하나와 그 안에서 그림을 다루기 가장 알맞은 폴더(<see cref="ArtFolder"/>).
/// </summary>
/// <param name="Engine">어디서 읽었는지.</param>
/// <param name="Title">표시 이름(없으면 폴더 이름).</param>
/// <param name="Root">프로젝트 루트(Aseprite는 최근 파일이 든 폴더).</param>
/// <param name="ArtFolder">열 폴더: Unity = Assets, Godot = 루트, Unreal = Content 아래에서 그림 폴더(Sprites · Art · Textures …)가 있으면 그 폴더.</param>
/// <param name="Version">엔진 버전(모르면 빈 문자열).</param>
/// <param name="LastModified">마지막으로 연·고친 때(모르면 null).</param>
/// <param name="IsFavorite">그 프로그램에서 즐겨찾기한 프로젝트인지.</param>
/// <param name="Exists">열 폴더가 아직 있는지(없으면 흐리게).</param>
public sealed record EngineProject(
    GameEngine Engine, string Title, string Root, string ArtFolder, string Version, DateTimeOffset? LastModified, bool IsFavorite, bool Exists);

/// <summary>
/// 컴퓨터에 깔린 게임 엔진·그림 프로그램의 최근 프로젝트 목록을 한데 모은다(시작 화면 오른쪽 표):
/// Unity Hub(<see cref="UnityHubProjects"/>), Godot 프로젝트 관리자(<c>projects.cfg</c>), Unreal 편집기(<c>EditorSettings.ini</c>의 최근 프로젝트),
/// Aseprite(<c>aseprite.ini</c>의 최근 파일·폴더). 읽지 못한 것은 빼고, 즐겨찾기 먼저 그다음 최근 순. 예외를 던지지 않는다.
/// </summary>
public static class EngineProjects
{
    /// <summary>그림 폴더로 보는 이름(앞일수록 먼저 — 대소문자 무시). 한 단계 들어간 뒤 한 번 더 찾는다(Godot <c>assets/sprites</c>).</summary>
    private static readonly string[] ArtNames = ["Sprites", "Sprite", "PixelArt", "Pixel", "Art", "Arts", "Graphics", "Textures", "Images", "Assets"];

    /// <summary>모든 프로그램의 목록(기본 위치).</summary>
    public static IReadOnlyList<EngineProject> Load()
    {
        var all = new List<EngineProject>();
        all.AddRange(FromUnity(UnityHubProjects.Load()));
        all.AddRange(Safe(() => LoadGodot(GodotConfigPaths())));
        all.AddRange(Safe(() => LoadUnreal(UnrealSettingsFiles())));
        all.AddRange(Safe(() => LoadAseprite(AsepriteConfigPaths())));
        return Sort(all);
    }

    private static IEnumerable<EngineProject> Safe(Func<IEnumerable<EngineProject>> read)
    {
        try
        {
            return read().ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return [];
        }
    }

    public static List<EngineProject> Sort(IEnumerable<EngineProject> list)
        => list
            .GroupBy(p => Path.TrimEndingDirectorySeparator(p.ArtFolder), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(p => p.IsFavorite)
            .ThenByDescending(p => p.LastModified ?? DateTimeOffset.MinValue)
            .ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// <paramref name="start"/>(Unity Assets · Godot 루트 · Unreal Content) 안에서 그림 폴더를 고른다: 바로 아래에 <see cref="ArtNames"/> 이름 폴더가 있으면
    /// 그리로 들어가 한 번 더 찾고(예: <c>assets/sprites</c>), 없으면 <paramref name="start"/> 그대로.
    /// </summary>
    public static string BestArtFolder(string start, Func<string, IEnumerable<string>> subdirectories)
    {
        var current = start;
        for (int depth = 0; depth < 2; depth++)
        {
            List<string> children;
            try
            {
                children = subdirectories(current).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                break;
            }
            string? pick = null;
            foreach (var name in ArtNames)
            {
                pick = children.FirstOrDefault(c => string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(c)), name, StringComparison.OrdinalIgnoreCase));
                if (pick is not null) break;
            }
            if (pick is null) break;
            current = pick;
        }
        return current;
    }

    private static IEnumerable<string> Subdirectories(string dir) => Directory.Exists(dir) ? Directory.EnumerateDirectories(dir) : [];

    // ───────────── Unity ─────────────

    private static IEnumerable<EngineProject> FromUnity(UnityHubProjectList hub)
        => hub.Projects.Select(p => new EngineProject(GameEngine.Unity, p.Title, p.Path,
            p.Exists ? BestArtFolder(p.AssetsPath, Subdirectories) : p.AssetsPath, p.Version, p.LastModified, p.IsFavorite, p.Exists && Directory.Exists(p.AssetsPath)));

    // ───────────── Godot ─────────────

    /// <summary>Godot 4 프로젝트 관리자 목록: Windows <c>%APPDATA%\Godot</c>, macOS <c>~/Library/Application Support/Godot</c>, Linux <c>~/.local/share/godot</c>.</summary>
    public static IReadOnlyList<string> GodotConfigPaths()
    {
        if (OperatingSystem.IsWindows()) return [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "projects.cfg")];
        if (OperatingSystem.IsMacOS()) return [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Godot", "projects.cfg")];
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return [Path.Combine(data, "godot", "projects.cfg")];
    }

    private static IEnumerable<EngineProject> LoadGodot(IEnumerable<string> configs)
    {
        foreach (var cfg in configs.Where(File.Exists))
            foreach (var (root, favorite) in ParseGodotProjects(File.ReadAllText(cfg)))
            {
                var file = Path.Combine(root, "project.godot");
                bool exists = File.Exists(file);
                string text = exists ? File.ReadAllText(file) : "";
                var (name, version) = ParseGodotProject(text);
                yield return new EngineProject(GameEngine.Godot, name ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(root)), root,
                    exists ? BestArtFolder(root, Subdirectories) : root, version, exists ? File.GetLastWriteTimeUtc(file) : null, favorite, exists);
            }
    }

    /// <summary><c>projects.cfg</c>: 프로젝트마다 <c>[경로]</c> 절과 그 아래 <c>favorite=true|false</c>.</summary>
    public static List<(string Root, bool Favorite)> ParseGodotProjects(string text)
    {
        var list = new List<(string, bool)>();
        string? section = null;
        bool favorite = false;
        void Flush()
        {
            if (section is { Length: > 0 }) list.Add((section.Replace('/', Path.DirectorySeparatorChar), favorite));
        }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                section = line[1..^1];
                favorite = false;
            }
            else if (line.StartsWith("favorite", StringComparison.Ordinal) && line.Split('=', 2) is [_, var v])
                favorite = v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        Flush();
        return list;
    }

    /// <summary><c>project.godot</c>의 <c>config/name="…"</c>과 <c>config/features=PackedStringArray("4.3", …)</c>의 버전(없으면 null · 빈 문자열).</summary>
    public static (string? Name, string Version) ParseGodotProject(string text)
    {
        var name = Regex.Match(text, "^config/name=\"(.*)\"\\s*$", RegexOptions.Multiline);
        var features = Regex.Match(text, "^config/features=PackedStringArray\\(\"(\\d+(?:\\.\\d+)*)\"", RegexOptions.Multiline);
        return (name.Success ? name.Groups[1].Value : null, features.Success ? features.Groups[1].Value : "");
    }

    // ───────────── Unreal ─────────────

    /// <summary>Unreal 편집기 설정(버전마다): Windows <c>%LOCALAPPDATA%\UnrealEngine\*</c>, macOS <c>~/Library/Application Support/Epic/UnrealEngine/*</c>, Linux <c>~/.config/Epic/UnrealEngine/*</c>의 <c>Saved/Config/*/EditorSettings.ini</c>.</summary>
    public static IReadOnlyList<(string File, string Version)> UnrealSettingsFiles()
    {
        string root = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnrealEngine")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Epic", "UnrealEngine")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Epic", "UnrealEngine");
        if (!Directory.Exists(root)) return [];
        var list = new List<(string, string)>();
        foreach (var versionDir in Directory.EnumerateDirectories(root))
        {
            var config = Path.Combine(versionDir, "Saved", "Config");
            if (!Directory.Exists(config)) continue;
            foreach (var platform in Directory.EnumerateDirectories(config))
            {
                var ini = Path.Combine(platform, "EditorSettings.ini");
                if (File.Exists(ini)) list.Add((ini, Path.GetFileName(versionDir)));
            }
        }
        return list;
    }

    private static IEnumerable<EngineProject> LoadUnreal(IEnumerable<(string File, string Version)> files)
    {
        foreach (var (ini, version) in files)
            foreach (var (uproject, opened) in ParseUnrealRecent(File.ReadAllText(ini)))
            {
                var root = Path.GetDirectoryName(uproject) ?? uproject;
                var content = Path.Combine(root, "Content");
                bool exists = Directory.Exists(content);
                yield return new EngineProject(GameEngine.Unreal, Path.GetFileNameWithoutExtension(uproject), root,
                    exists ? BestArtFolder(content, Subdirectories) : content, version,
                    opened ?? (File.Exists(uproject) ? File.GetLastWriteTimeUtc(uproject) : null), false, exists);
            }
    }

    /// <summary>
    /// <c>EditorSettings.ini</c>의 <c>RecentlyOpenedProjectFiles=</c> 줄들: 새 형식 <c>(ProjectName="…/X.uproject",LastOpenTime=2024.05.01-10.20.30)</c>,
    /// 예전 형식은 <c>.uproject</c> 경로만.
    /// </summary>
    public static List<(string UProject, DateTimeOffset? Opened)> ParseUnrealRecent(string text)
    {
        var list = new List<(string, DateTimeOffset?)>();
        foreach (Match m in Regex.Matches(text, "^RecentlyOpenedProjectFiles=(.+)$", RegexOptions.Multiline))
        {
            var value = m.Groups[1].Value.Trim();
            string? path;
            DateTimeOffset? opened = null;
            var named = Regex.Match(value, "ProjectName=\"([^\"]+)\"");
            if (named.Success)
            {
                path = named.Groups[1].Value;
                var time = Regex.Match(value, @"LastOpenTime=(\d{4}\.\d{2}\.\d{2}-\d{2}\.\d{2}\.\d{2})");
                if (time.Success && DateTime.TryParseExact(time.Groups[1].Value, "yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
                    opened = new DateTimeOffset(t, TimeSpan.Zero);
            }
            else path = value.Trim('"');
            if (path.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase)) list.Add((path.Replace('/', Path.DirectorySeparatorChar), opened));
        }
        return list;
    }

    // ───────────── Aseprite ─────────────

    /// <summary>Aseprite 설정: Windows <c>%APPDATA%\Aseprite</c>, macOS <c>~/Library/Application Support/Aseprite</c>, Linux <c>~/.config/aseprite</c>의 <c>aseprite.ini</c>.</summary>
    public static IReadOnlyList<string> AsepriteConfigPaths()
    {
        if (OperatingSystem.IsWindows()) return [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aseprite", "aseprite.ini")];
        if (OperatingSystem.IsMacOS()) return [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aseprite", "aseprite.ini")];
        return [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "aseprite", "aseprite.ini")];
    }

    private static IEnumerable<EngineProject> LoadAseprite(IEnumerable<string> configs)
    {
        foreach (var ini in configs.Where(File.Exists))
            foreach (var (folder, pinned) in ParseAsepriteFolders(File.ReadAllText(ini)))
            {
                bool exists = Directory.Exists(folder);
                yield return new EngineProject(GameEngine.Aseprite, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), folder, folder, "",
                    exists ? Directory.GetLastWriteTimeUtc(folder) : null, pinned, exists);
            }
    }

    /// <summary>
    /// <c>aseprite.ini</c>의 최근·고정 폴더: <c>[PinnedPaths]</c>·<c>[RecentPaths]</c>의 폴더와 <c>[PinnedFiles]</c>·<c>[RecentFiles]</c> 파일이 든 폴더(줄은 <c>0 = 경로</c>).
    /// 고정한 것은 즐겨찾기로. 같은 폴더는 한 번만(처음 나온 차례).
    /// </summary>
    public static List<(string Folder, bool Pinned)> ParseAsepriteFolders(string text)
    {
        var list = new List<(string, bool)>();
        var at = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? section = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }
            if (section is not ("PinnedPaths" or "RecentPaths" or "PinnedFiles" or "RecentFiles") || line.Split('=', 2) is not [_, var v]) continue;
            var path = v.Trim().Trim('"');
            if (path.Length == 0) continue;
            var folder = section.EndsWith("Files", StringComparison.Ordinal) ? Path.GetDirectoryName(path) : path;
            if (string.IsNullOrEmpty(folder)) continue;
            folder = Path.TrimEndingDirectorySeparator(folder);
            bool pinned = section.StartsWith("Pinned", StringComparison.Ordinal);
            if (at.TryGetValue(folder, out int i))
            {
                if (pinned) list[i] = (list[i].Item1, true);
            }
            else
            {
                at[folder] = list.Count;
                list.Add((folder, pinned));
            }
        }
        return list;
    }
}
