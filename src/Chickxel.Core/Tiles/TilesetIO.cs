using System.Collections.Immutable;
using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 타일셋 파일(<see cref="Tileset.FileExtension"/>) 읽기·쓰기. 형식은 ZIP: <c>tileset.json</c>(버전, 이름표, 이름, 칸 크기, 층, 타일 속성, 자동 타일 세트) +
/// <c>tiles/&lt;타일 Id&gt;.png</c>(타일 그림). 프로젝트(<c>.chickxel</c>)도 같은 내용을 <c>tilesets/&lt;이름표&gt;.chickxel-tileset</c>로 담는다.
/// </summary>
public static class TilesetIO
{
    public const int CurrentVersion = 1;
    public const string JsonEntryName = "tileset.json";
    /// <summary>타일 그림 한 변 최대(px).</summary>
    public const int MaxTileImageSize = 4096;

    private sealed class TilesetDto
    {
        public int Version { get; set; }
        public string? Key { get; set; }
        public string? Name { get; set; }
        public int CellWidth { get; set; }
        public int CellHeight { get; set; }
        public List<FloorDto>? Floors { get; set; }
        public List<TileDto>? Tiles { get; set; }
        public List<AutoTileDto>? AutoTiles { get; set; }
    }

    private sealed class FloorDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class TileDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Width { get; set; } = 1;
        public int Height { get; set; } = 1;
        public int[]? DrawOffset { get; set; }
        public int Floor { get; set; }
        public int[]? Palette { get; set; }
        /// <summary>움직이는 타일의 그림 수(2 이상일 때만, 그림은 tiles/&lt;Id&gt;_&lt;차례&gt;.png).</summary>
        public int? Frames { get; set; }
        public int? FrameMs { get; set; }
    }

    private sealed class AutoTileDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Floor { get; set; }
        public string? Group { get; set; }
        public List<RuleDto>? Rules { get; set; }
    }

    private sealed class RuleDto
    {
        /// <summary>같은 무리가 있어야 하는 이웃 비트.</summary>
        public int Same { get; set; }
        /// <summary>같은 무리가 없어야 하는 이웃 비트.</summary>
        public int Other { get; set; }
        public bool FlipX { get; set; }
        public bool FlipY { get; set; }
        public bool Rotate { get; set; }
        /// <summary>[타일 Id, 비중] 목록.</summary>
        public List<int[]>? Tiles { get; set; }
    }

    // ───────────── 쓰기 ─────────────

    private static string TileEntryName(int id, int frame = 0)
        => frame == 0 ? string.Create(CultureInfo.InvariantCulture, $"tiles/{id}.png") : string.Create(CultureInfo.InvariantCulture, $"tiles/{id}_{frame}.png");

    // 같은 타일셋은 같은 바이트가 되게(프로젝트 파일 안 사본·실행취소 기록 조각) ZIP 항목 시각을 고정한다.
    private static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>파일에 쓴다(임시 파일에 쓴 뒤 바꿔치기).</summary>
    public static void Save(Tileset tileset, string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = full + ".chickxel-tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                Write(tileset, fs);
            FileReplace.Commit(temp, full);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>ZIP 내용을 스트림에 쓴다.</summary>
    public static void Write(Tileset tileset, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var dto = new TilesetDto
        {
            Version = CurrentVersion,
            Key = tileset.Key,
            Name = tileset.Name,
            CellWidth = tileset.CellWidth,
            CellHeight = tileset.CellHeight,
            Floors = tileset.Floors.Select(f => new FloorDto { Id = f.Id, Name = f.Name }).ToList(),
            Tiles = tileset.Tiles.Select(t => new TileDto
            {
                Id = t.Id,
                Name = t.Name.Length == 0 ? null : t.Name,
                Width = t.Width,
                Height = t.Height,
                DrawOffset = t.DrawOffset.IsEmpty ? null : [t.DrawOffset.X, t.DrawOffset.Y],
                Floor = t.Floor,
                Palette = [t.PaletteX, t.PaletteY],
                Frames = t.IsAnimated ? t.Frames.Length : null,
                FrameMs = t.IsAnimated ? t.FrameMs : null,
            }).ToList(),
            AutoTiles = tileset.AutoTiles.Length == 0 ? null : tileset.AutoTiles.Select(a => new AutoTileDto
            {
                Id = a.Id,
                Name = a.Name.Length == 0 ? null : a.Name,
                Floor = a.Floor,
                Group = a.Group.Length == 0 ? null : a.Group,
                Rules = a.Rules.Select(r => new RuleDto
                {
                    Same = r.Same,
                    Other = r.Other & ~r.Same,
                    FlipX = r.AllowFlipX,
                    FlipY = r.AllowFlipY,
                    Rotate = r.AllowRotate,
                    Tiles = r.Tiles.IsDefaultOrEmpty ? null : r.Tiles.Select(c => new[] { c.Tile, c.Weight }).ToList(),
                }).ToList(),
            }).ToList(),
        };
        var json = JsonSerializer.Serialize(dto, ProjectIO.Options);
        var entry = zip.CreateEntry(JsonEntryName, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTime;
        using (var w = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false)))
            w.Write(json);
        foreach (var t in tileset.Tiles)
            for (int f = 0; f < t.FrameCount; f++)
            {
                // PNG는 이미 압축되어 있으므로 ZIP 압축은 하지 않는다.
                var png = zip.CreateEntry(TileEntryName(t.Id, f), CompressionLevel.NoCompression);
                png.LastWriteTime = FixedTime;
                using var s = png.Open();
                PngIO.Save(t.FrameImage(f), s);
            }
    }

    /// <summary>바이트로(프로젝트 안에 담을 때).</summary>
    public static byte[] ToBytes(Tileset tileset)
    {
        using var ms = new MemoryStream();
        Write(tileset, ms);
        return ms.ToArray();
    }

    // ───────────── 읽기 ─────────────

    /// <summary>파일에서 읽는다(<see cref="Tileset.SourcePath"/> = 그 파일). 이름표가 없으면 새로 만든다.</summary>
    public static Tileset Load(string path)
    {
        var full = Path.GetFullPath(path);
        using var fs = File.OpenRead(full);
        return Read(fs) with { SourcePath = full };
    }

    /// <summary>ZIP 스트림에서 읽는다(<see cref="Tileset.SourcePath"/>는 비움). 형식이 틀리면 <see cref="InvalidDataException"/>.</summary>
    public static Tileset Read(Stream stream)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException(DomainStrings.Tile_FileInvalid, ex);
        }
        using (zip)
        {
            var jsonEntry = zip.GetEntry(JsonEntryName) ?? throw new InvalidDataException(DomainStrings.Tile_FileInvalid);
            TilesetDto dto;
            try
            {
                using var r = new StreamReader(jsonEntry.Open());
                dto = JsonSerializer.Deserialize<TilesetDto>(r.ReadToEnd(), ProjectIO.Options) ?? throw new InvalidDataException(DomainStrings.Tile_FileInvalid);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(DomainStrings.Tile_FileInvalid, ex);
            }
            if (dto.Version > CurrentVersion) throw new InvalidDataException(DomainStrings.Tile_FileTooNew);
            return FromDto(dto, (id, frame) =>
            {
                var e = zip.GetEntry(TileEntryName(id, frame)) ?? throw new InvalidDataException(DomainStrings.Tile_FileInvalid);
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                ms.Position = 0;
                return PngIO.Load(ms);
            });
        }
    }

    /// <summary>바이트에서(프로젝트 안에 담긴 것).</summary>
    public static Tileset FromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        return Read(ms);
    }

    private static Tileset FromDto(TilesetDto dto, Func<int, int, PixelImage> image)
    {
        try
        {
            Tileset.ValidateCell(dto.CellWidth, dto.CellHeight);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
        var floors = (dto.Floors ?? []).Select(f => new TileFloor(f.Id, f.Name ?? "")).ToList();
        if (floors.Select(f => f.Id).Distinct().Count() != floors.Count) throw new InvalidDataException(DomainStrings.Tile_FileInvalid);
        var tiles = new List<TileDef>();
        var ids = new HashSet<int>();
        foreach (var t in dto.Tiles ?? [])
        {
            if (t.Id < 1 || !ids.Add(t.Id) || t.Width < 1 || t.Height < 1 || t.Width > Tileset.MaxFootprint || t.Height > Tileset.MaxFootprint)
                throw new InvalidDataException(DomainStrings.Tile_FileInvalid);
            var img = image(t.Id, 0);
            if (img.Width > MaxTileImageSize || img.Height > MaxTileImageSize) throw new InvalidDataException(DomainStrings.Tile_FileInvalid);
            int frames = t.Frames ?? 1;
            if (frames < 1 || frames > TileDef.MaxFrames) throw new InvalidDataException(DomainStrings.Tile_FileInvalid);
            var frameImages = new List<PixelImage> { img };
            for (int f = 1; f < frames; f++)
            {
                var fi = image(t.Id, f);
                if (fi.Width != img.Width || fi.Height != img.Height) throw new InvalidDataException(DomainStrings.Tile_FileInvalid);
                frameImages.Add(fi);
            }
            tiles.Add(new TileDef
            {
                Id = t.Id,
                Name = t.Name ?? "",
                Image = img,
                Width = t.Width,
                Height = t.Height,
                DrawOffset = t.DrawOffset is [var dx, var dy] ? new Point(dx, dy) : Point.Empty,
                Floor = t.Floor,
                PaletteX = t.Palette is [var px, _] ? px : 0,
                PaletteY = t.Palette is [_, var py] ? py : 0,
                Frames = frames > 1 ? [.. frameImages] : [],
                FrameMs = Math.Max(TileDef.MinFrameMs, t.FrameMs ?? TileDef.DefaultFrameMs),
            });
        }
        var autoTiles = (dto.AutoTiles ?? []).Select(a => new AutoTileSet
        {
            Id = a.Id,
            Name = a.Name ?? "",
            Floor = a.Floor,
            Group = a.Group?.Trim() ?? "",
            Rules = [.. (a.Rules ?? []).Select(r => new AutoTileRule
            {
                Same = (byte)r.Same,
                Other = (byte)(r.Other & ~r.Same),
                AllowFlipX = r.FlipX,
                AllowFlipY = r.FlipY,
                AllowRotate = r.Rotate,
                Tiles = [.. (r.Tiles ?? []).Where(c => c is { Length: >= 1 } && c[0] > 0).Select(c => new AutoTileChoice(c[0], c.Length > 1 ? Math.Max(1, c[1]) : 1))],
            })],
        }).ToList();
        return new Tileset
        {
            Key = string.IsNullOrWhiteSpace(dto.Key) ? Tileset.NewKey() : dto.Key,
            Name = dto.Name ?? "",
            CellWidth = dto.CellWidth,
            CellHeight = dto.CellHeight,
            Floors = [.. floors],
            Tiles = [.. tiles],
            AutoTiles = [.. autoTiles],
        };
    }
}
