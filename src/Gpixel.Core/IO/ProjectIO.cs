using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>저장 결과: 프로젝트 파일 경로와 (켜져 있었으면) 내보내기 결과.</summary>
public sealed record ProjectSaveResult(string ProjectPath, ExportResult? Export);

/// <summary>
/// <c>.gpixel</c> 프로젝트 파일 읽기/쓰기. 형식은 ZIP:
/// <c>project.json</c>(버전 1, 종류·캔버스·레이어·프레임·태그·아틀라스 스프라이트·Unity·내보내기 설정) +
/// <c>cels/L{레이어 번호}_F{프레임 번호}.png</c>(애니메이션 셀) + <c>atlas/S{스프라이트 번호}_L{레이어 번호}.png</c>(아틀라스 셀).
/// 빈 셀은 생략, 번호는 0부터, 레이어 0 = 맨 아래. 종류가 없는 예전 파일은 애니메이션.
/// </summary>
public static partial class ProjectIO
{
    public const int CurrentVersion = 1;
    public const string ProjectEntryName = "project.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex(@"^cels/L(\d+)_F(\d+)\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex CelEntry();

    [GeneratedRegex(@"^atlas/S(\d+)_L(\d+)\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex AtlasEntry();

    public static string CelEntryName(int layerIndex, int frameIndex) => $"cels/L{layerIndex}_F{frameIndex}.png";

    public static string AtlasCelEntryName(int spriteIndex, int layerIndex) => $"atlas/S{spriteIndex}_L{layerIndex}.png";

    // ───────────── 저장 ─────────────

    /// <summary>
    /// 프로젝트를 저장한다(임시 파일에 쓴 뒤 바꿔치기). 성공하면 <see cref="SpriteProject.Path"/>를 갱신하고
    /// 수정됨 표시를 끈다. 실패하면 예외를 던지고 상태는 그대로다.
    /// </summary>
    public static void Save(SpriteProject project, string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = full + ".gpixel-tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                Save(project, fs);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        project.Path = full;
        project.IsDirty = false;
    }

    /// <summary>ZIP 내용을 스트림에 쓴다(경로·수정 여부는 바꾸지 않음).</summary>
    public static void Save(SpriteProject project, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var json = JsonSerializer.Serialize(ToDto(project), Options);
        var entry = zip.CreateEntry(ProjectEntryName, CompressionLevel.Optimal);
        using (var w = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false)))
            w.Write(json);

        for (int li = 0; li < project.Layers.Count; li++)
        {
            for (int fi = 0; fi < project.Frames.Count; fi++)
                WriteCel(zip, CelEntryName(li, fi), project.GetCel(li, fi));
            for (int si = 0; si < project.Sprites.Count; si++)
                WriteCel(zip, AtlasCelEntryName(si, li), project.GetSpriteCel(li, si));
        }
    }

    private static void WriteCel(ZipArchive zip, string name, PixelImage? cel)
    {
        if (cel is null || cel.IsRegionEmpty(cel.Bounds)) return;
        // PNG는 이미 압축되어 있으므로 ZIP 압축은 하지 않는다.
        var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
        using var es = entry.Open();
        PngIO.Save(cel, es);
    }

    /// <summary>
    /// 저장(Ctrl+S): 프로젝트를 저장하고, 내보내기가 켜져 있으면 시트 PNG + 사이드카 + (필요하면) Unity 메타도 쓴다.
    /// 프로젝트 저장 뒤 내보내기가 실패하면 예외가 그대로 올라온다(프로젝트 파일은 이미 저장됨).
    /// </summary>
    /// <param name="path">저장 경로. null이면 <see cref="SpriteProject.Path"/>.</param>
    public static ProjectSaveResult SaveWithExport(SpriteProject project, string? path = null)
    {
        path ??= project.Path ?? throw new InvalidOperationException("저장할 경로가 없습니다.");
        Save(project, path);
        var export = project.Export.ExportOnSave ? ProjectExport.Export(project) : null;
        return new ProjectSaveResult(project.Path!, export);
    }

    /// <summary>
    /// Returns the existing, default-named export and its existing companions. A custom folder or
    /// filename is never treated as owned by the project browser. Read only project.json so browsing
    /// a large project does not decode its cels.
    /// </summary>
    public static string[] GetOwnedDefaultExportPaths(string projectPath)
    {
        using var zip = ZipFile.OpenRead(projectPath);
        var entry = zip.GetEntry(ProjectEntryName)
                    ?? throw new InvalidDataException("프로젝트 정보(project.json)가 없습니다.");
        ProjectDto dto;
        try
        {
            using var stream = entry.Open();
            dto = JsonSerializer.Deserialize<ProjectDto>(stream, Options)
                  ?? throw new InvalidDataException("프로젝트 정보가 비어 있습니다.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"프로젝트 정보(project.json)가 손상되었습니다: {ex.Message}", ex);
        }
        if (dto.Version > CurrentVersion)
            throw new InvalidDataException($"이 파일은 더 새로운 Gpixel(형식 버전 {dto.Version})에서 만들어졌습니다.");
        var export = dto.Export;
        if (export is not { OnSave: true } || !string.IsNullOrWhiteSpace(export.Folder)
            || !string.IsNullOrWhiteSpace(export.FileName)) return [];

        var png = Path.ChangeExtension(Path.GetFullPath(projectPath), ".png");
        if (!File.Exists(png)) return [];
        return [png, .. DocumentIO.GetCompanionPaths(png).Where(File.Exists)];
    }

    /// <summary>Renames a project and its owned default export together, without replacing any destination.</summary>
    public static void RenameWithOwnedDefaultExport(string oldProjectPath, string newProjectPath)
    {
        var oldPng = Path.ChangeExtension(Path.GetFullPath(oldProjectPath), ".png");
        var newPng = Path.ChangeExtension(Path.GetFullPath(newProjectPath), ".png");
        var paths = new List<(string Source, string Destination)>();
        foreach (var source in GetOwnedDefaultExportPaths(oldProjectPath))
        {
            var suffix = source[oldPng.Length..];
            paths.Add((source, newPng + suffix));
        }
        paths.Add((oldProjectPath, newProjectPath));

        foreach (var (source, destination) in paths)
            if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
                throw new IOException($"이미 같은 이름의 파일이 있습니다: {Path.GetFileName(destination)}");

        // An orphan companion at the new name must not become associated with the moved PNG.
        if (paths.Count > 1)
            foreach (var destination in DocumentIO.GetCompanionPaths(newPng))
                if (!paths.Any(p => string.Equals(p.Destination, destination, StringComparison.OrdinalIgnoreCase))
                    && File.Exists(destination))
                    throw new IOException($"이미 같은 이름의 파일이 있습니다: {Path.GetFileName(destination)}");

        var moved = new List<(string Source, string Destination)>();
        try
        {
            foreach (var (source, destination) in paths)
            {
                MoveWithoutReplacing(source, destination);
                moved.Add((source, destination));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                for (int i = moved.Count - 1; i >= 0; i--)
                    MoveWithoutReplacing(moved[i].Destination, moved[i].Source);
            }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
            {
                throw new IOException("이름 변경에 실패했고 이전 파일 이름도 복구하지 못했습니다.", new AggregateException(ex, rollback));
            }
            throw;
        }
    }

    private static void MoveWithoutReplacing(string source, string destination)
    {
        if (string.Equals(source, destination, StringComparison.Ordinal)) return;
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            // Windows requires a distinct intermediate name for case-only changes.
            var temp = source + "." + Guid.NewGuid().ToString("N") + ".rename-tmp";
            File.Move(source, temp);
            try { File.Move(temp, destination); }
            catch
            {
                File.Move(temp, source);
                throw;
            }
        }
        else File.Move(source, destination);
    }

    // ───────────── 읽기 ─────────────

    /// <summary>
    /// 프로젝트 파일을 연다(경로 설정, 수정됨 아님). 손상·지원하지 않는 버전이면 <see cref="InvalidDataException"/>.
    /// 읽지 못한 셀은 빈 셀이 되고 <paramref name="warnings"/>에 적힌다.
    /// </summary>
    public static SpriteProject Load(string path, ICollection<string>? warnings = null)
    {
        var full = Path.GetFullPath(path);
        SpriteProject project;
        using (var fs = File.OpenRead(full))
            project = Load(fs, warnings);
        project.Path = full;
        project.IsDirty = false;
        return project;
    }

    public static SpriteProject Load(Stream stream, ICollection<string>? warnings = null)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"Gpixel 프로젝트 파일이 아니거나 손상되었습니다: {ex.Message}", ex);
        }

        using (zip)
        {
            var entry = zip.GetEntry(ProjectEntryName)
                        ?? throw new InvalidDataException("프로젝트 정보(project.json)가 없습니다.");
            ProjectDto dto;
            try
            {
                using var s = entry.Open();
                dto = JsonSerializer.Deserialize<ProjectDto>(s, Options)
                      ?? throw new InvalidDataException("프로젝트 정보가 비어 있습니다.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"프로젝트 정보(project.json)가 손상되었습니다: {ex.Message}", ex);
            }
            if (dto.Version > CurrentVersion)
                throw new InvalidDataException(
                    $"이 파일은 더 새로운 Gpixel(형식 버전 {dto.Version})에서 만들어졌습니다. 이 버전은 {CurrentVersion}까지 읽을 수 있습니다.");
            if (dto.Width < 1 || dto.Height < 1 || dto.Width > PixelImage.MaxSize || dto.Height > PixelImage.MaxSize)
                throw new InvalidDataException($"캔버스 크기가 잘못되었습니다: {dto.Width}x{dto.Height}");

            var project = FromDto(dto);
            foreach (var e in zip.Entries)
            {
                var name = e.FullName.Replace('\\', '/');
                Layer layer;
                object owner;
                Size size;
                if (CelEntry().Match(name) is { Success: true } m)
                {
                    if (!TryIndex(m, 1, project.Layers.Count, out var li) || !TryIndex(m, 2, project.Frames.Count, out var fi))
                    {
                        warnings?.Add($"쓰이지 않는 셀 파일을 무시했습니다: {e.FullName}");
                        continue;
                    }
                    (layer, owner, size) = (project.Layers[li], project.Frames[fi], project.Size);
                }
                else if (AtlasEntry().Match(name) is { Success: true } a)
                {
                    if (!TryIndex(a, 1, project.Sprites.Count, out var si) || !TryIndex(a, 2, project.Layers.Count, out var li))
                    {
                        warnings?.Add($"쓰이지 않는 셀 파일을 무시했습니다: {e.FullName}");
                        continue;
                    }
                    var sprite = project.Sprites[si];
                    (layer, owner, size) = (project.Layers[li], sprite, sprite.Size);
                }
                else continue;

                PixelImage img;
                try
                {
                    using var es = e.Open();
                    using var ms = new MemoryStream();
                    es.CopyTo(ms);
                    ms.Position = 0;
                    img = PngIO.Load(ms);
                }
                catch (InvalidDataException ex)
                {
                    warnings?.Add($"셀을 읽지 못해 비웠습니다({e.FullName}): {ex.Message}");
                    continue;
                }
                if (img.Size != size)
                {
                    var fitted = new PixelImage(size.Width, size.Height);
                    fitted.Blit(img, 0, 0);
                    img = fitted;
                    warnings?.Add($"셀 크기가 맞지 않아 왼쪽 위에 맞췄습니다: {e.FullName}");
                }
                project.SetCelRaw(layer, owner, img);
            }
            return project;
        }
    }

    private static bool TryIndex(Match m, int group, int count, out int index)
        => int.TryParse(m.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index < count;

    /// <summary>파일 목록 썸네일: 애니메이션은 첫 프레임, 아틀라스는 배치된 시트.</summary>
    public static PixelImage LoadThumbnail(string path)
    {
        var project = Load(path);
        return project.IsAtlas ? ProjectExport.BuildSheet(project, project.BaseName).Image : project.Flatten(0);
    }

    // ───────────── 변환 ─────────────

    private static ProjectDto ToDto(SpriteProject p) => new()
    {
        Version = CurrentVersion,
        Kind = p.Kind,
        Width = p.Width,
        Height = p.Height,
        AtlasSourceWidth = p.AtlasSourceSize.IsEmpty ? null : p.AtlasSourceSize.Width,
        AtlasSourceHeight = p.AtlasSourceSize.IsEmpty ? null : p.AtlasSourceSize.Height,
        Sprites = p.IsAtlas
            ? p.Sprites.Select(s => new SpriteDto
            {
                Name = s.Name,
                Width = s.Width,
                Height = s.Height,
                Pivot = PivotDto.From(s.Pivot),
                Border = s.Border.IsNone ? null : [s.Border.Left, s.Border.Top, s.Border.Right, s.Border.Bottom],
                Source = s.SourceRect is { } r ? [r.X, r.Y, r.Width, r.Height] : null,
            }).ToList()
            : null,
        Layers = p.Layers.Select(l => new LayerDto { Name = l.Name, Visible = l.Visible, Locked = l.Locked, Opacity = l.Opacity }).ToList(),
        Frames = p.Frames.Select(f => new FrameDto
        {
            DurationMs = f.DurationMs,
            Pivot = f.Pivot is { } fp ? PivotDto.From(fp) : null,
            Name = f.Name,
        }).ToList(),
        Tags = p.Tags.Select(t => new TagDto
        {
            Name = t.Name, From = t.From, To = t.To, Direction = t.Direction, Loop = t.Loop, Color = t.Color.ToHex(),
        }).ToList(),
        Unity = new UnityDto
        {
            SpriteMode = p.Sprite.Mode,
            PixelsPerUnit = p.Sprite.PixelsPerUnit,
            DefaultPivot = PivotDto.From(p.Sprite.DefaultPivot),
            Border = p.Sprite.Border.IsNone ? null : [p.Sprite.Border.Left, p.Sprite.Border.Top, p.Sprite.Border.Right, p.Sprite.Border.Bottom],
            FullRect = p.Sprite.FullRectMesh,
            Readable = p.Sprite.Readable,
        },
        Export = new ExportDto
        {
            OnSave = p.Export.ExportOnSave,
            Folder = p.Export.Folder,
            FileName = p.Export.FileName,
            Layout = p.Export.Layout,
            Columns = p.Export.Columns,
            Spacing = p.Export.Spacing,
            Trim = p.Export.Trim,
            SingleFrame = p.Export.SingleFrameIndex,
            WriteMeta = p.Export.WriteMeta,
            AtlasMaxWidth = p.Export.AtlasMaxWidth,
            AtlasPowerOfTwo = p.Export.AtlasPowerOfTwo,
            AtlasOrder = p.Export.AtlasOrder,
            KeepImportedRects = p.Export.KeepImportedRects,
        },
    };

    private static SpriteProject FromDto(ProjectDto dto)
    {
        var p = SpriteProject.CreateBlank(dto.Width, dto.Height, dto.Kind);
        foreach (var l in dto.Layers ?? [])
            p.AddLayerRaw(new Layer(string.IsNullOrWhiteSpace(l.Name) ? p.NextLayerName() : l.Name)
            {
                Visible = l.Visible,
                Locked = l.Locked,
                Opacity = (byte)Math.Clamp(l.Opacity, 0, 255),
            });
        if (p.Layers.Count == 0) p.AddLayerRaw(new Layer(p.NextLayerName()));

        foreach (var f in dto.Frames ?? [])
            p.AddFrameRaw(new Frame
            {
                DurationMs = f.DurationMs,
                Pivot = f.Pivot?.ToPivot(),
                Name = string.IsNullOrWhiteSpace(f.Name) ? null : f.Name,
            });
        if (p.Frames.Count == 0) p.AddFrameRaw(new Frame());

        foreach (var t in dto.Tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(t.Name)) continue;
            int from = Math.Clamp(t.From, 0, p.Frames.Count - 1);
            int to = Math.Clamp(t.To, 0, p.Frames.Count - 1);
            if (from > to) continue;
            p.AddTagRaw(new AnimationTag(p.UniqueTagName(t.Name), from, to)
            {
                Direction = t.Direction,
                Loop = t.Loop,
                Color = Color32.TryParseHex(t.Color, out var c) ? c : AnimationTag.DefaultColors[p.Tags.Count % AnimationTag.DefaultColors.Count],
            });
        }

        if (dto.Unity is { } u)
        {
            p.Sprite.Mode = u.SpriteMode;
            p.Sprite.PixelsPerUnit = u.PixelsPerUnit;
            p.Sprite.DefaultPivot = u.DefaultPivot?.ToPivot() ?? Pivot.Center;
            p.Sprite.Border = u.Border is { Length: >= 4 } b ? new SpriteBorder(b[0], b[1], b[2], b[3]) : SpriteBorder.None;
            p.Sprite.FullRectMesh = u.FullRect;
            p.Sprite.Readable = u.Readable;
        }
        if (dto.Export is { } e)
        {
            p.Export.ExportOnSave = e.OnSave;
            p.Export.Folder = string.IsNullOrWhiteSpace(e.Folder) ? null : e.Folder;
            p.Export.FileName = string.IsNullOrWhiteSpace(e.FileName) ? null : e.FileName;
            p.Export.Layout = e.Layout;
            p.Export.Columns = e.Columns;
            p.Export.Spacing = e.Spacing;
            p.Export.Trim = e.Trim;
            p.Export.SingleFrameIndex = e.SingleFrame;
            p.Export.WriteMeta = e.WriteMeta;
            p.Export.AtlasMaxWidth = e.AtlasMaxWidth;
            p.Export.AtlasPowerOfTwo = e.AtlasPowerOfTwo;
            p.Export.AtlasOrder = e.AtlasOrder;
            p.Export.KeepImportedRects = e.KeepImportedRects;
        }

        if (p.IsAtlas)
        {
            if (dto.AtlasSourceWidth is > 0 and var sw && dto.AtlasSourceHeight is > 0 and var sh)
                p.AtlasSourceSize = new Size(sw, sh);
            foreach (var s in dto.Sprites ?? [])
            {
                if (s.Width < 1 || s.Height < 1 || s.Width > PixelImage.MaxSize || s.Height > PixelImage.MaxSize) continue;
                p.AddSpriteRaw(new AtlasSprite(p.UniqueSpriteName(s.Name), s.Width, s.Height)
                {
                    Pivot = s.Pivot?.ToPivot() ?? p.Sprite.DefaultPivot,
                    Border = s.Border is { Length: >= 4 } b ? new SpriteBorder(b[0], b[1], b[2], b[3]) : SpriteBorder.None,
                    SourceRect = s.Source is { Length: >= 4 } r && r[2] > 0 && r[3] > 0 ? new Rectangle(r[0], r[1], r[2], r[3]) : null,
                });
            }
            if (p.Sprites.Count == 0) p.AddSpriteRaw(new AtlasSprite("sprite", p.Width, p.Height) { Pivot = p.Sprite.DefaultPivot });
        }
        return p;
    }

    // ───────────── JSON 형식 ─────────────

    private sealed class ProjectDto
    {
        public int Version { get; set; } = CurrentVersion;
        public ProjectKind Kind { get; set; } = ProjectKind.Animation;
        public int Width { get; set; }
        public int Height { get; set; }
        public int? AtlasSourceWidth { get; set; }
        public int? AtlasSourceHeight { get; set; }
        public List<SpriteDto>? Sprites { get; set; }
        public List<LayerDto>? Layers { get; set; }
        public List<FrameDto>? Frames { get; set; }
        public List<TagDto>? Tags { get; set; }
        public UnityDto? Unity { get; set; }
        public ExportDto? Export { get; set; }
    }

    private sealed class LayerDto
    {
        public string Name { get; set; } = "";
        public bool Visible { get; set; } = true;
        public bool Locked { get; set; }
        public int Opacity { get; set; } = 255;
    }

    private sealed class FrameDto
    {
        public int DurationMs { get; set; } = Frame.DefaultDurationMs;
        public PivotDto? Pivot { get; set; }
        public string? Name { get; set; }
    }

    private sealed class TagDto
    {
        public string Name { get; set; } = "";
        public int From { get; set; }
        public int To { get; set; }
        public TagDirection Direction { get; set; } = TagDirection.Forward;
        public bool Loop { get; set; } = true;
        public string? Color { get; set; }
    }

    private sealed class UnityDto
    {
        public SpriteMode SpriteMode { get; set; } = SpriteMode.Multiple;
        public float PixelsPerUnit { get; set; } = SpriteSettings.DefaultPixelsPerUnit;
        public PivotDto? DefaultPivot { get; set; }
        /// <summary>Single 모드 테두리 [왼쪽, 위, 오른쪽, 아래].</summary>
        public int[]? Border { get; set; }
        public bool FullRect { get; set; } = true;
        public bool Readable { get; set; }
    }

    private sealed class ExportDto
    {
        public bool OnSave { get; set; } = true;
        public string? Folder { get; set; }
        public string? FileName { get; set; }
        public SheetLayout Layout { get; set; } = SheetLayout.Columns;
        public int Columns { get; set; }
        public int Spacing { get; set; }
        public bool Trim { get; set; }
        public int SingleFrame { get; set; }
        public bool? WriteMeta { get; set; }
        public int AtlasMaxWidth { get; set; }
        public bool AtlasPowerOfTwo { get; set; }
        public AtlasPackOrder AtlasOrder { get; set; } = AtlasPackOrder.KeepOrder;
        public bool KeepImportedRects { get; set; } = true;
    }

    private sealed class SpriteDto
    {
        public string Name { get; set; } = "sprite";
        public int Width { get; set; }
        public int Height { get; set; }
        public PivotDto? Pivot { get; set; }
        /// <summary>테두리 [왼쪽, 위, 오른쪽, 아래].</summary>
        public int[]? Border { get; set; }
        /// <summary>가져온 원래 칸 [x, y, w, h](왼쪽 위 원점).</summary>
        public int[]? Source { get; set; }
    }

    private sealed class PivotDto
    {
        public float X { get; set; } = 0.5f;
        public float Y { get; set; } = 0.5f;
        public static PivotDto From(Pivot p) => new() { X = p.X, Y = p.Y };
        public Pivot ToPivot() => float.IsFinite(X) && float.IsFinite(Y) ? new Pivot(X, Y) : Pivot.Center;
    }
}
