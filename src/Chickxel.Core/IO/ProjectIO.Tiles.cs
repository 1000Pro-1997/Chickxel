using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.Core.IO;

// 프로젝트 파일 안의 타일맵(버전 6): 타일셋 내용 tilesets/<이름표>.chickxel-tileset, 맵 tilemaps/L<레이어>.bin, 타일셋 파일 경로.
public static partial class ProjectIO
{
    private const string TilesetFolder = "tilesets/";

    private static string TilesetEntryName(string key) => TilesetFolder + key + Tileset.FileExtension;

    private static string TileMapEntryName(int layerIndex) => string.Create(CultureInfo.InvariantCulture, $"tilemaps/L{layerIndex}.bin");

    /// <summary>타일셋 파일 경로를 프로젝트 파일에 적을 값으로: 프로젝트 파일 기준 상대 경로(<c>/</c> 구분), 안 되면 전체 경로. 파일이 없으면 null.</summary>
    private static string? TilesetPathFor(string? source, string? projectPath)
    {
        if (source is null) return null;
        if (projectPath is null || Path.GetDirectoryName(projectPath) is not { Length: > 0 } dir) return source;
        var relative = Path.GetRelativePath(dir, source);
        return Path.IsPathRooted(relative) ? relative : relative.Replace('\\', '/');
    }

    /// <summary>프로젝트 파일에 적힌 타일셋 경로 → 전체 경로(풀 수 없거나 상대 경로인데 프로젝트 경로를 모르면 null).</summary>
    private static string? ResolveTilesetPath(string? stored, string? projectPath)
    {
        var path = LocalPathCipher.UnprotectCurrent(stored);
        if (string.IsNullOrWhiteSpace(path) || LocalPathCipher.IsProtected(path)) return null;
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        if (projectPath is null || Path.GetDirectoryName(projectPath) is not { Length: > 0 } dir) return null;
        return Path.GetFullPath(Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>전체 경로인 타일셋 경로만 암호화한다(상대 경로는 그대로).</summary>
    private static void ProtectTilesetPaths(ProjectDto dto)
    {
        foreach (var t in dto.Tilesets ?? [])
            if (t.Path is { } path && Path.IsPathRooted(path)) t.Path = LocalPathCipher.ProtectCurrent(path);
    }

    /// <summary>타일셋 내용과 맵을 ZIP에 쓴다.</summary>
    private static void WriteTiles(ZipArchive zip, SpriteProject project)
    {
        foreach (var tileset in project.AllTilesets)
        {
            // 안에 든 PNG가 이미 압축되어 있으므로 압축하지 않는다.
            var entry = zip.CreateEntry(TilesetEntryName(tileset.Key), CompressionLevel.NoCompression);
            using var s = entry.Open();
            TilesetIO.Write(tileset, s);
        }
        for (int li = 0; li < project.Layers.Count; li++)
        {
            if (project.Layers[li].Tiles is not { } data || data.Map.IsEmpty) continue;
            var entry = zip.CreateEntry(TileMapEntryName(li), CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(TileMapCodec.Encode(data.Map));
        }
    }

    /// <summary>
    /// 타일셋과 맵을 읽는다(<see cref="FromDto"/> 뒤). 타일셋 파일이 있으면 파일 내용을, 없거나 읽지 못하면 프로젝트 안 사본을 쓴다.
    /// </summary>
    private static void ReadTiles(ZipArchive zip, ProjectDto dto, IReadOnlyList<Layer> fileLayers, SpriteProject project, string? projectPath, ICollection<string>? warnings)
    {
        foreach (var r in dto.Tilesets ?? [])
        {
            if (string.IsNullOrWhiteSpace(r.Key)) continue;
            var source = ResolveTilesetPath(r.Path, projectPath);
            Tileset? tileset = null;
            if (zip.GetEntry(TilesetEntryName(r.Key)) is { } entry)
            {
                try
                {
                    using var s = entry.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    tileset = TilesetIO.FromBytes(ms.ToArray()) with { Key = r.Key, SourcePath = source };
                }
                catch (InvalidDataException ex)
                {
                    warnings?.Add(DomainStrings.FormatTile_EmbeddedUnreadable(error: ex.Message));
                }
            }
            if (source is not null && File.Exists(source))
            {
                try
                {
                    tileset = TilesetIO.Load(source) with { Key = r.Key };
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    warnings?.Add(DomainStrings.FormatTile_FileUnreadable(path: source, error: ex.Message));
                }
            }
            if (tileset is null) continue;
            project.PutTilesetRaw(tileset);
            project.MarkTilesetSynced(tileset);
        }
        for (int li = 0; li < fileLayers.Count; li++)
        {
            var layer = fileLayers[li];
            if (layer.Tiles is not { } data) continue;
            if (data.TilesetKeys.Any(k => !project.Tilesets.ContainsKey(k))) warnings?.Add(DomainStrings.FormatTile_MissingTileset(layer: layer.Name));
            if (zip.GetEntry(TileMapEntryName(li)) is not { } entry) continue;
            try
            {
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                layer.Tiles = data with { Map = TileMapCodec.Decode(ms.ToArray()) };
            }
            catch (InvalidDataException ex)
            {
                warnings?.Add(DomainStrings.FormatTile_MapUnreadable(layer: layer.Name, error: ex.Message));
            }
        }
    }

    /// <summary>레이어 DTO의 타일맵 내용(맵은 비어 있음 — <see cref="ReadTiles"/>가 채움).</summary>
    private static TilemapData TilemapOf(LayerDto l)
        => new(l.TilesetKeys is { Count: > 0 } keys ? [.. keys] : [""], TileMap.Empty, l.TileOrigin is [var x, var y] ? new Point(x, y) : Point.Empty)
        {
            HiddenFloors = [.. (l.TileHidden ?? []).Distinct()],
            LockedFloors = [.. (l.TileLocked ?? []).Distinct()],
        };

    /// <summary>
    /// 프로젝트를 저장한 뒤 바뀐 타일셋을 타일셋 파일에도 쓴다(<see cref="SpriteProject.TilesetsToWrite"/>). 쓰지 못한 것은 경고로 돌려준다(프로젝트 저장은 성공).
    /// </summary>
    public static IReadOnlyList<string> WriteTilesetFiles(SpriteProject project)
    {
        var failed = new List<string>();
        foreach (var tileset in project.TilesetsToWrite)
        {
            try
            {
                TilesetIO.Save(tileset, tileset.SourcePath!);
                project.MarkTilesetSynced(tileset);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(DomainStrings.FormatTile_FileWriteFailed(path: tileset.SourcePath!, error: ex.Message));
            }
        }
        return failed;
    }
}
