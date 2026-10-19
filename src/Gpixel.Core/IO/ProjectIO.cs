using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>저장 결과: 프로젝트 파일 경로와 (켜져 있었으면) 내보내기 결과.</summary>
public sealed record ProjectSaveResult(string ProjectPath, ProjectExportResult? Export);

/// <summary>
/// <c>.gpixel</c> 프로젝트 파일 읽기/쓰기. 형식은 ZIP:
/// <c>project.json</c>(버전 2, 캔버스·레이어(종류·그룹·부위 크기·위치·피벗·테두리)·프레임·태그·Unity·내보내기 설정) +
/// <c>cels/L{레이어 번호}_F{프레임 번호}.png</c>(셀, 부위 셀은 부위 크기). 빈 셀은 생략, 번호는 0부터, 레이어 0 = 맨 아래.
/// <para>
/// 버전 1 애니메이션 파일은 그대로 읽는다. 버전 1 아틀라스 파일(<c>kind: Atlas</c>, <c>atlas/S{스프라이트}_L{레이어}.png</c>)은
/// 그룹 "부위" + 스프라이트마다 부위 하나로 바꿔 읽는다(부위 아틀라스 이름은 예전 내보내기 이름이라 Unity guid·spriteID가 유지됨).
/// </para>
/// </summary>
public static partial class ProjectIO
{
    public const int CurrentVersion = 2;
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
            for (int fi = 0; fi < project.Frames.Count; fi++)
                WriteCel(zip, CelEntryName(li, fi), project.GetCel(li, fi));
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
    /// 저장(Ctrl+S): 프로젝트를 저장하고, 내보내기가 켜져 있으면 프레임 시트·부위 아틀라스 PNG + 사이드카 + (필요하면) Unity 메타도 쓴다.
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
    /// 프로젝트에 등록된 내보내기 이미지 경로(파일 존재 여부와 무관): 프레임 시트(켜져 있으면), 부위 아틀라스(켜져 있고 부위가 있으면).
    /// project.json만 읽어 셀은 풀지 않는다. 읽을 수 없으면 빈 목록.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredExportPaths(string projectPath)
    {
        ProjectDto dto;
        try
        {
            using var zip = ZipFile.OpenRead(projectPath);
            dto = ReadDto(zip);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return [];
        }
        var export = new ExportSettings();
        if (dto.Export is { } e) ApplyExport(export, e);
        var full = Path.GetFullPath(projectPath);
        var paths = new List<string>(2);
        if (export.ExportFrames) paths.Add(ProjectExport.GetExportImagePath(export, full));
        if (export.ExportParts && dto.Layers?.Any(l => l.Kind == LayerKind.Part) == true) paths.Add(ProjectExport.GetPartsImagePath(export, full));
        return paths;
    }

    /// <summary>
    /// Returns the existing, default-named exports (frame sheet <c>{project}.png</c>, parts atlas <c>{project}_parts.png</c>)
    /// and their existing companions. A custom folder or filename is never treated as owned by the project browser.
    /// Read only project.json so browsing a large project does not decode its cels.
    /// </summary>
    public static string[] GetOwnedDefaultExportPaths(string projectPath)
    {
        var owned = new List<string>();
        foreach (var png in GetOwnedDefaultPngs(projectPath))
        {
            owned.Add(png);
            owned.AddRange(DocumentIO.GetCompanionPaths(png).Where(File.Exists));
        }
        return owned.ToArray();
    }

    /// <summary>Existing default-named export PNGs owned by the project: frame sheet first, then parts atlas.</summary>
    private static List<string> GetOwnedDefaultPngs(string projectPath)
    {
        ProjectDto dto;
        using (var zip = ZipFile.OpenRead(projectPath))
            dto = ReadDto(zip);
        var export = dto.Export;
        if (export is not { OnSave: true } || !string.IsNullOrWhiteSpace(export.Folder)
            || !string.IsNullOrWhiteSpace(export.FileName)) return [];

        var full = Path.GetFullPath(projectPath);
        var ext = (Enum.IsDefined(export.ImageFormat) ? export.ImageFormat : ExportImageFormat.Png).Extension();
        var owned = new List<string>(2);
        var frames = Path.ChangeExtension(full, ext);
        if (export.ExportFrames && File.Exists(frames)) owned.Add(frames);
        var parts = DefaultPartsImagePath(full, ext);
        if (export.ExportParts && string.IsNullOrWhiteSpace(export.PartsFileName) && File.Exists(parts)) owned.Add(parts);
        return owned;
    }

    /// <summary>기본 이름 부위 아틀라스 경로(<c>hero.gpixel</c> → <c>hero_parts.png</c>, 확장자는 내보내기 형식).</summary>
    private static string DefaultPartsImagePath(string fullProjectPath, string extension)
        => Path.Combine(Path.GetDirectoryName(fullProjectPath) ?? "", Path.GetFileNameWithoutExtension(fullProjectPath) + "_parts" + extension);

    /// <summary>Renames a project and its owned default exports together, without replacing any destination.</summary>
    public static void RenameWithOwnedDefaultExport(string oldProjectPath, string newProjectPath)
    {
        var oldFull = Path.GetFullPath(oldProjectPath);
        var newFull = Path.GetFullPath(newProjectPath);
        // 소유한 기본 이름 이미지는 어떤 형식이든(확장자만 다름) 이름 짝을 만든다.
        var pngNames = Enum.GetValues<ExportImageFormat>().Select(f => f.Extension()).SelectMany(ext => new (string Old, string New)[]
        {
            (Path.ChangeExtension(oldFull, ext), Path.ChangeExtension(newFull, ext)),
            (DefaultPartsImagePath(oldFull, ext), DefaultPartsImagePath(newFull, ext)),
        }).ToArray();
        var paths = new List<(string Source, string Destination)>();
        var newPngs = new List<string>();
        foreach (var oldPng in GetOwnedDefaultPngs(oldProjectPath))
        {
            var newPng = pngNames.First(n => n.Old == oldPng).New;
            newPngs.Add(newPng);
            paths.Add((oldPng, newPng));
            foreach (var source in DocumentIO.GetCompanionPaths(oldPng).Where(File.Exists))
                paths.Add((source, newPng + source[oldPng.Length..]));
        }
        paths.Add((oldProjectPath, newProjectPath));

        foreach (var (source, destination) in paths)
            if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
                throw new IOException($"이미 같은 이름의 파일이 있습니다: {Path.GetFileName(destination)}");

        // An orphan companion at the new name must not become associated with the moved PNG.
        foreach (var newPng in newPngs)
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
    /// 읽지 못한 셀은 빈 셀이 되고 <paramref name="warnings"/>에 적힌다. 버전 1 아틀라스 파일은 부위 레이어로 바꿔 읽는다.
    /// </summary>
    public static SpriteProject Load(string path, ICollection<string>? warnings = null)
    {
        var full = Path.GetFullPath(path);
        SpriteProject project;
        using (var fs = File.OpenRead(full))
            project = Load(fs, warnings, Path.GetFileNameWithoutExtension(full));
        project.Path = full;
        project.IsDirty = false;
        return project;
    }

    /// <inheritdoc cref="Load(string, ICollection{string}?)"/>
    public static SpriteProject Load(Stream stream, ICollection<string>? warnings = null) => Load(stream, warnings, null);

    /// <param name="baseName">파일 이름(확장자 없이). 버전 1 아틀라스의 예전 내보내기 이름을 정할 때 쓴다(null이면 "sprite").</param>
    private static SpriteProject Load(Stream stream, ICollection<string>? warnings, string? baseName)
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
            var dto = ReadDto(zip);
            if (dto.Width < 1 || dto.Height < 1 || dto.Width > PixelImage.MaxSize || dto.Height > PixelImage.MaxSize)
                throw new InvalidDataException($"캔버스 크기가 잘못되었습니다: {dto.Width}x{dto.Height}");
            if (dto.Kind == V1Kind.Atlas) return ConvertV1Atlas(zip, dto, warnings, baseName ?? "sprite");

            var (project, fileLayers) = FromDto(dto, warnings);
            foreach (var e in zip.Entries)
            {
                if (CelEntry().Match(e.FullName.Replace('\\', '/')) is not { Success: true } m) continue;
                if (!TryIndex(m, 1, fileLayers.Count, out var li) || !TryIndex(m, 2, project.Frames.Count, out var fi)
                    || fileLayers[li].IsGroup)
                {
                    warnings?.Add($"쓰이지 않는 셀 파일을 무시했습니다: {e.FullName}");
                    continue;
                }
                var layer = fileLayers[li];
                if (ReadCel(e, project.CelSize(layer), warnings) is { } img) project.SetCelRaw(layer, project.Frames[fi], img);
            }
            return project;
        }
    }

    /// <summary><c>project.json</c>을 읽고 버전을 확인한다.</summary>
    private static ProjectDto ReadDto(ZipArchive zip)
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
        return dto;
    }

    /// <summary>셀 PNG를 읽어 <paramref name="size"/>로 맞춘다(크기가 다르면 왼쪽 위에 맞추고 알림). 읽지 못하면 null.</summary>
    private static PixelImage? ReadCel(ZipArchiveEntry e, Size size, ICollection<string>? warnings)
    {
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
            return null;
        }
        if (img.Size == size) return img;
        var fitted = new PixelImage(size.Width, size.Height);
        fitted.Blit(img, 0, 0);
        warnings?.Add($"셀 크기가 맞지 않아 왼쪽 위에 맞췄습니다: {e.FullName}");
        return fitted;
    }

    private static bool TryIndex(Match m, int group, int count, out int index)
        => int.TryParse(m.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index < count;

    /// <summary>파일 목록 썸네일: 첫 프레임 합성.</summary>
    public static PixelImage LoadThumbnail(string path) => Load(path).Flatten(0);

    // ───────────── 버전 1 아틀라스 변환 ─────────────

    /// <summary>
    /// 버전 1 아틀라스 → 그룹 "부위" + 스프라이트마다 부위 하나(첫 스프라이트 = 맨 위 부위), 프레임 1개.
    /// 부위 셀 = 그 스프라이트의 보이는 레이어 합성. 모든 스프라이트에 원래 칸이 있으면 캔버스 = 원래 시트 크기, 위치 = 원래 칸,
    /// 아니면 왼쪽부터 1px 간격으로 늘어놓는다. 내보내기: 부위 아틀라스 = 예전 이름, 프레임 시트 = <c>{예전 이름}_frames</c>.
    /// </summary>
    private static SpriteProject ConvertV1Atlas(ZipArchive zip, ProjectDto dto, ICollection<string>? warnings, string baseName)
    {
        // 파일의 스프라이트 번호 → 유효한 스프라이트
        var sprites = new List<(SpriteDto Dto, int FileIndex)>();
        var spriteDtos = dto.Sprites ?? [];
        for (int i = 0; i < spriteDtos.Count; i++)
        {
            var sd = spriteDtos[i];
            if (sd.Width < 1 || sd.Height < 1 || sd.Width > PixelImage.MaxSize || sd.Height > PixelImage.MaxSize) continue;
            sprites.Add((sd, i));
        }
        if (sprites.Count == 0) sprites.Add((new SpriteDto { Name = "sprite", Width = dto.Width, Height = dto.Height }, 0));

        var layerDtos = dto.Layers ?? [];
        int layerCount = Math.Max(1, layerDtos.Count);
        var cels = new Dictionary<(int Sprite, int Layer), PixelImage>();
        foreach (var e in zip.Entries)
        {
            if (AtlasEntry().Match(e.FullName.Replace('\\', '/')) is not { Success: true } m) continue;
            int si = -1;
            if (TryIndex(m, 1, spriteDtos.Count == 0 ? 1 : spriteDtos.Count, out var fileSprite))
                si = sprites.FindIndex(s => s.FileIndex == fileSprite);
            if (si < 0 || !TryIndex(m, 2, layerCount, out var li))
            {
                warnings?.Add($"쓰이지 않는 셀 파일을 무시했습니다: {e.FullName}");
                continue;
            }
            var size = new Size(sprites[si].Dto.Width, sprites[si].Dto.Height);
            if (ReadCel(e, size, warnings) is { } img && !img.IsRegionEmpty(img.Bounds)) cels[(si, li)] = img;
        }
        int layersWithContent = cels.Keys.Select(k => k.Layer).Distinct().Count();
        if (layersWithContent > 1)
            warnings?.Add($"예전 아틀라스의 레이어 {layersWithContent}개를 부위마다 한 장으로 합쳤습니다(보이는 레이어만).");

        // 캔버스·위치
        Rectangle? Source(SpriteDto s) => s.Source is { Length: >= 4 } r && r[2] > 0 && r[3] > 0 ? new Rectangle(r[0], r[1], r[2], r[3]) : null;
        var offsets = new List<Point>(sprites.Count);
        int canvasW, canvasH;
        if (sprites.All(s => Source(s.Dto) is not null))
        {
            offsets.AddRange(sprites.Select(s => Source(s.Dto)!.Value.Location));
            bool hasSheet = dto.AtlasSourceWidth is > 0 && dto.AtlasSourceHeight is > 0;
            canvasW = hasSheet ? dto.AtlasSourceWidth!.Value : sprites.Max(s => Source(s.Dto)!.Value.Right);
            canvasH = hasSheet ? dto.AtlasSourceHeight!.Value : sprites.Max(s => Source(s.Dto)!.Value.Bottom);
        }
        else
        {
            int x = 0;
            foreach (var (sd, _) in sprites)
            {
                offsets.Add(new Point(x, 0));
                x += sd.Width + 1;
            }
            canvasW = x - 1;
            canvasH = sprites.Max(s => s.Dto.Height);
        }
        canvasW = Math.Clamp(canvasW, 1, PixelImage.MaxSize);
        canvasH = Math.Clamp(canvasH, 1, PixelImage.MaxSize);

        var project = SpriteProject.CreateBlank(canvasW, canvasH);
        var frame = new Frame();
        project.AddFrameRaw(frame);
        ApplySettings(project, dto);
        var visible = Enumerable.Range(0, layerCount)
            .Select(i => i < layerDtos.Count ? (layerDtos[i].Visible, (byte)Math.Clamp(layerDtos[i].Opacity, 0, 255)) : (true, (byte)255))
            .ToList();

        var parts = new List<Layer>(sprites.Count);
        for (int si = 0; si < sprites.Count; si++)
        {
            var sd = sprites[si].Dto;
            var part = new Layer(project.UniqueLayerName(string.IsNullOrWhiteSpace(sd.Name) ? "sprite" : sd.Name.Trim()))
            {
                Kind = LayerKind.Part,
                PartSize = new Size(sd.Width, sd.Height),
                PartOffset = offsets[si],
                PartPivot = sd.Pivot?.ToPivot() ?? project.Sprite.DefaultPivot,
                PartBorder = sd.Border is { Length: >= 4 } b ? new SpriteBorder(b[0], b[1], b[2], b[3]) : SpriteBorder.None,
                PartSourceRect = Source(sd),
            };
            parts.Add(part);
            project.AddLayerRaw(part); // 이름 중복 검사용(순서는 아래에서 다시)

            var flat = new PixelImage(sd.Width, sd.Height);
            for (int li = 0; li < layerCount; li++)
                if (visible[li].Item1 && cels.TryGetValue((si, li), out var cel)) flat.Composite(cel, 0, 0, visible[li].Item2);
            if (!flat.IsRegionEmpty(flat.Bounds)) project.SetCelRaw(part, frame, flat);
        }
        var group = new Layer(project.UniqueLayerName("부위")) { Kind = LayerKind.Group };
        foreach (var part in parts) part.Parent = group;
        project.ReplaceLayersRaw([.. Enumerable.Reverse(parts), group]);

        var oldName = project.Export.FileName ?? baseName;
        project.Export.ExportParts = true;
        project.Export.PartsFileName = oldName;
        project.Export.ExportFrames = true;
        project.Export.FileName = oldName + "_frames";
        return project;
    }

    // ───────────── 변환 ─────────────

    private static ProjectDto ToDto(SpriteProject p) => new()
    {
        Version = CurrentVersion,
        Width = p.Width,
        Height = p.Height,
        Layers = p.Layers.Select(l => new LayerDto
        {
            Name = l.Name,
            Visible = l.Visible,
            Locked = l.Locked,
            Opacity = l.Opacity,
            Kind = l.Kind == LayerKind.Normal ? null : l.Kind,
            Parent = l.Parent is { } g ? p.IndexOf(g) : null,
            Collapsed = l.IsGroup && l.Collapsed ? true : null,
            PartWidth = l.IsPart ? l.PartSize.Width : null,
            PartHeight = l.IsPart ? l.PartSize.Height : null,
            PartX = l.IsPart ? l.PartOffset.X : null,
            PartY = l.IsPart ? l.PartOffset.Y : null,
            PartPivot = l.IsPart ? PivotDto.From(l.PartPivot) : null,
            PartBorder = l.IsPart && !l.PartBorder.IsNone ? [l.PartBorder.Left, l.PartBorder.Top, l.PartBorder.Right, l.PartBorder.Bottom] : null,
            PartSource = l.IsPart && l.PartSourceRect is { } r ? [r.X, r.Y, r.Width, r.Height] : null,
        }).ToList(),
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
            Texture = TextureDto.From(p.Sprite),
        },
        Export = new ExportDto
        {
            OnSave = p.Export.ExportOnSave,
            Folder = p.Export.Folder,
            FileName = p.Export.FileName,
            ExportFrames = p.Export.ExportFrames,
            ExportParts = p.Export.ExportParts,
            PartsFileName = p.Export.PartsFileName,
            ImageFormat = p.Export.ImageFormat,
            JpegQuality = p.Export.JpegQuality,
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

    /// <summary>JSON → 프로젝트(셀 제외). 파일 안 레이어 순서 목록도 돌려준다(셀 파일 번호용, 구조를 고치면 순서가 바뀔 수 있음).</summary>
    private static (SpriteProject Project, List<Layer> FileLayers) FromDto(ProjectDto dto, ICollection<string>? warnings)
    {
        var p = SpriteProject.CreateBlank(dto.Width, dto.Height);
        var layerDtos = dto.Layers ?? [];
        var fileLayers = new List<Layer>(layerDtos.Count);
        foreach (var l in layerDtos)
        {
            var kind = l.Kind is { } k && Enum.IsDefined(k) ? k : LayerKind.Normal;
            var layer = new Layer(string.IsNullOrWhiteSpace(l.Name) ? p.NextLayerName() : l.Name)
            {
                Visible = l.Visible,
                Locked = l.Locked,
                Opacity = (byte)Math.Clamp(l.Opacity, 0, 255),
                Kind = kind,
                Collapsed = kind == LayerKind.Group && l.Collapsed == true,
            };
            if (kind == LayerKind.Part)
            {
                layer.PartSize = new Size(Math.Clamp(l.PartWidth ?? 1, 1, PixelImage.MaxSize), Math.Clamp(l.PartHeight ?? 1, 1, PixelImage.MaxSize));
                layer.PartOffset = new Point(l.PartX ?? 0, l.PartY ?? 0);
                layer.PartPivot = l.PartPivot?.ToPivot() ?? Pivot.Center;
                layer.PartBorder = l.PartBorder is { Length: >= 4 } b ? new SpriteBorder(b[0], b[1], b[2], b[3]) : SpriteBorder.None;
                layer.PartSourceRect = l.PartSource is { Length: >= 4 } r && r[2] > 0 && r[3] > 0 ? new Rectangle(r[0], r[1], r[2], r[3]) : null;
            }
            fileLayers.Add(layer);
            p.AddLayerRaw(layer);
        }
        for (int i = 0; i < fileLayers.Count; i++)
            if (fileLayers[i].IsPart && layerDtos[i].Parent is int gi && gi >= 0 && gi < fileLayers.Count && fileLayers[gi].IsGroup)
                fileLayers[i].Parent = fileLayers[gi];
        if (p.NormalizeHierarchy()) warnings?.Add("레이어 그룹 구조가 잘못되어 고쳤습니다.");
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

        ApplySettings(p, dto);
        return (p, fileLayers);
    }

    /// <summary>Unity·내보내기 설정을 적용한다.</summary>
    private static void ApplySettings(SpriteProject p, ProjectDto dto)
    {
        if (dto.Unity is { } u)
        {
            p.Sprite.Mode = u.SpriteMode;
            p.Sprite.PixelsPerUnit = u.PixelsPerUnit;
            p.Sprite.DefaultPivot = u.DefaultPivot?.ToPivot() ?? Pivot.Center;
            p.Sprite.Border = u.Border is { Length: >= 4 } b ? new SpriteBorder(b[0], b[1], b[2], b[3]) : SpriteBorder.None;
            p.Sprite.FullRectMesh = u.FullRect;
            p.Sprite.Readable = u.Readable;
            u.Texture?.ApplyTo(p.Sprite);
        }
        if (dto.Export is { } e) ApplyExport(p.Export, e);
    }

    private static void ApplyExport(ExportSettings x, ExportDto e)
    {
        x.ExportOnSave = e.OnSave;
        x.Folder = string.IsNullOrWhiteSpace(e.Folder) ? null : e.Folder;
        x.FileName = string.IsNullOrWhiteSpace(e.FileName) ? null : e.FileName;
        x.ExportFrames = e.ExportFrames;
        x.ExportParts = e.ExportParts;
        x.PartsFileName = string.IsNullOrWhiteSpace(e.PartsFileName) ? null : e.PartsFileName;
        x.ImageFormat = Enum.IsDefined(e.ImageFormat) ? e.ImageFormat : ExportImageFormat.Png;
        x.JpegQuality = e.JpegQuality;
        x.Layout = e.Layout;
        x.Columns = e.Columns;
        x.Spacing = e.Spacing;
        x.Trim = e.Trim;
        x.SingleFrameIndex = e.SingleFrame;
        x.WriteMeta = e.WriteMeta;
        x.AtlasMaxWidth = e.AtlasMaxWidth;
        x.AtlasPowerOfTwo = e.AtlasPowerOfTwo;
        x.AtlasOrder = e.AtlasOrder;
        x.KeepImportedRects = e.KeepImportedRects;
    }

    // ───────────── JSON 형식 ─────────────

    /// <summary>버전 1 프로젝트 종류(읽기 전용, 아틀라스면 부위 레이어로 변환).</summary>
    private enum V1Kind
    {
        Animation,
        Atlas,
    }

    private sealed class ProjectDto
    {
        public int Version { get; set; } = CurrentVersion;
        /// <summary>버전 1 전용(쓰지 않음).</summary>
        public V1Kind? Kind { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        /// <summary>버전 1 아틀라스 전용: 가져온 원래 시트 크기.</summary>
        public int? AtlasSourceWidth { get; set; }
        public int? AtlasSourceHeight { get; set; }
        /// <summary>버전 1 아틀라스 전용: 스프라이트.</summary>
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
        /// <summary>null = 일반.</summary>
        public LayerKind? Kind { get; set; }
        /// <summary>부위: 그룹 레이어 번호.</summary>
        public int? Parent { get; set; }
        /// <summary>그룹: 타임라인에서 접힘.</summary>
        public bool? Collapsed { get; set; }
        public int? PartWidth { get; set; }
        public int? PartHeight { get; set; }
        public int? PartX { get; set; }
        public int? PartY { get; set; }
        public PivotDto? PartPivot { get; set; }
        /// <summary>부위 테두리 [왼쪽, 위, 오른쪽, 아래].</summary>
        public int[]? PartBorder { get; set; }
        /// <summary>부위의 가져온 원래 칸 [x, y, w, h](왼쪽 위 원점).</summary>
        public int[]? PartSource { get; set; }
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
        /// <summary>Unity 텍스처 가져오기 설정(없으면 기본값 — 예전 파일).</summary>
        public TextureDto? Texture { get; set; }
    }

    /// <summary>Unity 인스펙터 항목(Extrude·Physics·Advanced·Wrap/Filter·Default 플랫폼).</summary>
    private sealed class TextureDto
    {
        public int ExtrudeEdges { get; set; } = 1;
        public bool GeneratePhysicsShape { get; set; } = true;
        public bool SRgb { get; set; } = true;
        public SpriteAlphaSource AlphaSource { get; set; } = SpriteAlphaSource.InputTextureAlpha;
        public bool AlphaIsTransparency { get; set; } = true;
        public bool GenerateMipMaps { get; set; }
        public TextureWrap WrapMode { get; set; } = TextureWrap.Clamp;
        public TextureFilter FilterMode { get; set; } = TextureFilter.Point;
        public int AnisoLevel { get; set; } = 1;
        public int MaxTextureSize { get; set; }
        public TextureResize ResizeAlgorithm { get; set; } = TextureResize.Mitchell;
        public TextureFormatChoice Format { get; set; } = TextureFormatChoice.Automatic;
        public TextureCompressionChoice Compression { get; set; } = TextureCompressionChoice.None;
        public bool CrunchCompression { get; set; }
        public int CompressionQuality { get; set; } = 50;

        public static TextureDto From(SpriteSettings s) => new()
        {
            ExtrudeEdges = s.ExtrudeEdges, GeneratePhysicsShape = s.GeneratePhysicsShape, SRgb = s.SRgb, AlphaSource = s.AlphaSource,
            AlphaIsTransparency = s.AlphaIsTransparency, GenerateMipMaps = s.GenerateMipMaps, WrapMode = s.WrapMode, FilterMode = s.FilterMode,
            AnisoLevel = s.AnisoLevel, MaxTextureSize = s.MaxTextureSize, ResizeAlgorithm = s.ResizeAlgorithm, Format = s.Format,
            Compression = s.Compression, CrunchCompression = s.CrunchCompression, CompressionQuality = s.CompressionQuality,
        };

        public void ApplyTo(SpriteSettings s)
        {
            s.ExtrudeEdges = ExtrudeEdges;
            s.GeneratePhysicsShape = GeneratePhysicsShape;
            s.SRgb = SRgb;
            s.AlphaSource = Enum.IsDefined(AlphaSource) ? AlphaSource : SpriteAlphaSource.InputTextureAlpha;
            s.AlphaIsTransparency = AlphaIsTransparency;
            s.GenerateMipMaps = GenerateMipMaps;
            s.WrapMode = Enum.IsDefined(WrapMode) ? WrapMode : TextureWrap.Clamp;
            s.FilterMode = Enum.IsDefined(FilterMode) ? FilterMode : TextureFilter.Point;
            s.AnisoLevel = AnisoLevel;
            s.MaxTextureSize = MaxTextureSize;
            s.ResizeAlgorithm = Enum.IsDefined(ResizeAlgorithm) ? ResizeAlgorithm : TextureResize.Mitchell;
            s.Format = Enum.IsDefined(Format) ? Format : TextureFormatChoice.Automatic;
            s.Compression = Enum.IsDefined(Compression) ? Compression : TextureCompressionChoice.None;
            s.CrunchCompression = CrunchCompression;
            s.CompressionQuality = CompressionQuality;
        }
    }

    private sealed class ExportDto
    {
        public bool OnSave { get; set; } = true;
        public string? Folder { get; set; }
        public string? FileName { get; set; }
        public bool ExportFrames { get; set; } = true;
        public bool ExportParts { get; set; } = true;
        public ExportImageFormat ImageFormat { get; set; } = ExportImageFormat.Png;
        public int JpegQuality { get; set; } = 90;
        public string? PartsFileName { get; set; }
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

    /// <summary>버전 1 아틀라스 스프라이트(읽기 전용).</summary>
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
