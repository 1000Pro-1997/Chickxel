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
/// <c>project.json</c>(버전 3, 캔버스·레이어(종류·그룹·스킨·부위 크기·위치·피벗·테두리)·프레임·태그·Unity·내보내기 설정) +
/// <c>cels/L{레이어 번호}_F{프레임 번호}.png</c>(셀, 부위 셀은 부위 크기 — 부위는 그룹의 첫 스킨 셀) +
/// <c>cels/L{레이어}_F{프레임}_S{스킨 번호}.png</c>(부위의 첫 스킨이 아닌 스킨 셀, 스킨 번호 = 그룹 스킨 목록 번호 1~).
/// 빈 셀은 생략, 번호는 0부터, 레이어 0 = 맨 아래.
/// <para>
/// 버전 2 이하 파일은 그대로 읽는다(그룹마다 스킨 "기본" 하나). 버전 1 아틀라스 파일(<c>kind: Atlas</c>, <c>atlas/S{스프라이트}_L{레이어}.png</c>)은
/// 그룹 "부위" + 스프라이트마다 부위 하나로 바꿔 읽는다(부위 아틀라스 이름은 예전 내보내기 이름이라 Unity guid·spriteID가 유지됨).
/// </para>
/// </summary>
public static partial class ProjectIO
{
    public const int CurrentVersion = 3;
    public const string ProjectEntryName = "project.json";

    internal static readonly JsonSerializerOptions Options = new()
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

    [GeneratedRegex(@"^cels/L(\d+)_F(\d+)(?:_S(\d+))?\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex CelEntry();

    [GeneratedRegex(@"^atlas/S(\d+)_L(\d+)\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex AtlasEntry();

    /// <param name="skinIndex">부위의 스킨 번호(0 = 첫 스킨·일반 레이어).</param>
    public static string CelEntryName(int layerIndex, int frameIndex, int skinIndex = 0)
        => skinIndex == 0 ? $"cels/L{layerIndex}_F{frameIndex}.png" : $"cels/L{layerIndex}_F{frameIndex}_S{skinIndex}.png";

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
                Save(project, fs, full);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        project.Path = full;
        project.IsDirty = false;
    }

    /// <summary>ZIP 내용을 스트림에 쓴다(경로·수정 여부는 바꾸지 않음). 경로를 모르므로 내보내기 기록(<see cref="SpriteProject.ExportedFiles"/>)은 쓰지 않는다.</summary>
    public static void Save(SpriteProject project, Stream stream) => Save(project, stream, null);

    /// <param name="projectPath">저장할 프로젝트 전체 경로(내보내기 기록을 이 파일 기준 상대 경로로 씀). null이면 기록을 쓰지 않는다.</param>
    private static void Save(SpriteProject project, Stream stream, string? projectPath)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WriteProjectJson(zip, ToDto(project, projectPath));

        for (int li = 0; li < project.Layers.Count; li++)
        {
            var layer = project.Layers[li];
            if (layer.IsGroup) continue;
            var skins = layer is { IsPart: true, Parent: { IsGroup: true } g } ? g.AllSkins : [];
            for (int fi = 0; fi < project.Frames.Count; fi++)
            {
                var frame = project.Frames[fi];
                WriteCel(zip, CelEntryName(li, fi), project.GetCelRaw(layer, frame, null));
                for (int si = 1; si < skins.Count; si++)
                    WriteCel(zip, CelEntryName(li, fi, si), project.GetCelRaw(layer, frame, skins[si]));
            }
        }
    }

    private static void WriteProjectJson(ZipArchive zip, ProjectDto dto)
    {
        var json = JsonSerializer.Serialize(dto, Options);
        var entry = zip.CreateEntry(ProjectEntryName, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false));
        w.Write(json);
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
    /// 저장(Ctrl+S): 프로젝트를 저장하고, 내보내기가 켜져 있으면 프레임 시트·부위 아틀라스 이미지 + (필요하면) Unity 메타도 쓴다.
    /// 내보낼 결과 목록(기록)은 저장 전에 정해 <c>.gpixel</c>에 함께 저장한다. 지난 기록에 있고 이번에 만들지 않는 옛 결과는
    /// <see cref="ProjectExportResult.Stale"/>로 돌려준다(지우지 않음). 다른 경로로 저장하면 지난 기록은 원래 프로젝트 것이라 넘기지 않는다.
    /// 프로젝트 저장 뒤 내보내기가 실패하면 예외가 그대로 올라온다(프로젝트 파일은 이미 저장됨).
    /// </summary>
    /// <param name="path">저장 경로. null이면 <see cref="SpriteProject.Path"/>.</param>
    public static ProjectSaveResult SaveWithExport(SpriteProject project, string? path = null)
    {
        path ??= project.Path ?? throw new InvalidOperationException("저장할 경로가 없습니다.");
        var full = Path.GetFullPath(path);
        var previous = project.ExportedFiles;
        if (project.Path is { } current && !string.Equals(Path.GetFullPath(current), full, StringComparison.OrdinalIgnoreCase))
            project.ExportedFiles = null;
        ExportPlan? plan = null;
        Exception? planError = null;
        if (project.Export.ExportOnSave)
        {
            try
            {
                plan = ProjectExport.Plan(project, full);
                project.ExportedFiles = plan.Record;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or NotSupportedException)
            {
                planError = ex; // 프로젝트는 저장하고 내보내기 실패로 알린다
            }
        }
        try
        {
            Save(project, full);
        }
        catch
        {
            project.ExportedFiles = previous;
            throw;
        }
        if (planError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(planError);
        return new ProjectSaveResult(project.Path!, plan is null ? null : ProjectExport.Write(project, plan));
    }

    /// <summary>
    /// 프로젝트에 등록된 내보내기 결과 경로(파일 존재 여부와 무관): 지금 설정으로 만들 결과(프레임 시트 파일들, 부위가 있으면 부위 아틀라스 파일들,
    /// 켜져 있으면 Sprite Library 파일들) + 마지막으로 내보낸 기록의 이미지·<c>.spriteLib</c>. project.json만 읽어 셀은 풀지 않는다
    /// (부위·스킨은 내용과 무관하게 셈). 읽을 수 없으면 빈 목록.
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
        return RegisteredPaths(dto, Path.GetFullPath(projectPath));
    }

    private static List<string> RegisteredPaths(ProjectDto dto, string fullProjectPath)
    {
        var paths = new List<string>();
        if (dto.Width >= 1 && dto.Height >= 1 && dto.Width <= PixelImage.MaxSize && dto.Height <= PixelImage.MaxSize)
        {
            var (project, _) = FromDto(dto, null);
            var (frames, parts) = ProjectExport.GetOutputPaths(project, fullProjectPath, assumePartContent: true);
            paths.AddRange(frames);
            paths.AddRange(parts);
            paths.AddRange(ProjectExport.GetLibraryPaths(project, fullProjectPath, assumePartContent: true));
        }
        var outputs = Enum.GetValues<ExportImageFormat>().Select(f => f.Extension()).Append(ProjectExport.SpriteLibraryExtension).ToArray();
        foreach (var file in ResolveRecord(dto.Exported, fullProjectPath) ?? [])
            if (outputs.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) && !paths.Contains(file, StringComparer.OrdinalIgnoreCase))
                paths.Add(file);
        return paths;
    }

    /// <summary>
    /// 내보내기 기록 → 전체 경로. 기록한 프로젝트 파일 이름(<c>owner</c>)이 지금 이름과 다르면(밖에서 복사·이름 바꿈) 남의 기록이므로 null.
    /// </summary>
    private static List<string>? ResolveRecord(ExportRecordDto? record, string? fullProjectPath)
    {
        if (record?.Files is null || fullProjectPath is null
            || !string.Equals(record.Owner, Path.GetFileName(fullProjectPath), StringComparison.OrdinalIgnoreCase)) return null;
        var dir = Path.GetDirectoryName(fullProjectPath) ?? "";
        var files = new List<string>(record.Files.Count);
        foreach (var f in record.Files)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            try
            {
                files.Add(Path.GetFullPath(Path.Combine(dir, f)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }
        return files;
    }

    private static ExportRecordDto? ToRecordDto(IReadOnlyList<string>? files, string? fullProjectPath)
    {
        if (files is null || fullProjectPath is null) return null;
        var dir = Path.GetDirectoryName(fullProjectPath) ?? "";
        return new ExportRecordDto
        {
            Owner = Path.GetFileName(fullProjectPath),
            Files = files.Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).ToList(),
        };
    }

    /// <summary>
    /// Returns the existing, default-named exports (frame sheet <c>{project}.png</c> and its per-tag files, parts atlas <c>{project}_parts.png</c>
    /// and its per-part files — from the current settings and the last export record) and their existing companions.
    /// A custom folder or filename is never treated as owned by the project browser.
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

    /// <summary>
    /// Existing default-named export images owned by the project (frame sheets first, then parts atlases, then other recorded files):
    /// in the project folder, named <c>{project}.*</c> or <c>{project}_*</c>, never a custom parts atlas name.
    /// </summary>
    private static List<string> GetOwnedDefaultPngs(string projectPath)
    {
        ProjectDto dto;
        using (var zip = ZipFile.OpenRead(projectPath))
            dto = ReadDto(zip);
        var export = dto.Export;
        if (export is not { OnSave: true } || !string.IsNullOrWhiteSpace(export.Folder)
            || !string.IsNullOrWhiteSpace(export.FileName)) return [];

        var full = Path.GetFullPath(projectPath);
        var dir = Path.GetDirectoryName(full) ?? "";
        var stem = Path.GetFileNameWithoutExtension(full);
        var customParts = string.IsNullOrWhiteSpace(export.PartsFileName) ? null : export.PartsFileName.Trim();
        return RegisteredPaths(dto, full)
            .Where(p => IsDefaultNamed(p, dir, stem) && !(customParts is not null && HasStem(Path.GetFileName(p), customParts)) && File.Exists(p))
            .ToList();
    }

    /// <summary>프로젝트 폴더에 있고 이름이 <c>{stem}.…</c> 또는 <c>{stem}_…</c>인지(기본 이름 내보내기·그 .meta).</summary>
    private static bool IsDefaultNamed(string path, string dir, string stem)
        => string.Equals(Path.GetDirectoryName(path), dir, StringComparison.OrdinalIgnoreCase) && HasStem(Path.GetFileName(path), stem);

    /// <summary>이름이 <c>{stem}.…</c>, <c>{stem}_…</c>, <c>{stem}@…</c>(착장·스킨 파일)인지.</summary>
    private static bool HasStem(string fileName, string stem)
        => fileName.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith(stem + ProjectExport.SkinSeparator, StringComparison.OrdinalIgnoreCase);

    /// <summary>기본 이름 파일의 프로젝트 이름 바꾼 뒤 경로(<c>hero_walk.png</c> → <c>knight_walk.png</c>).</summary>
    private static string RenamedDefault(string path, string oldStem, string newDir, string newStem)
        => Path.Combine(newDir, newStem + Path.GetFileName(path)[oldStem.Length..]);

    /// <summary>
    /// 프로젝트 이름을 바꾼 뒤의 내보내기 기록: 프로젝트 폴더의 기본 이름 파일 중 함께 옮겨진 것(옛 자리에 없고 새 이름에 있음)을 새 경로로.
    /// 열린 프로젝트의 메모리 기록에도 쓴다.
    /// </summary>
    public static IReadOnlyList<string>? RemapExportRecord(IReadOnlyList<string>? files, string oldProjectPath, string newProjectPath)
    {
        if (files is null) return null;
        var oldFull = Path.GetFullPath(oldProjectPath);
        var newFull = Path.GetFullPath(newProjectPath);
        var oldDir = Path.GetDirectoryName(oldFull) ?? "";
        var oldStem = Path.GetFileNameWithoutExtension(oldFull);
        var newDir = Path.GetDirectoryName(newFull) ?? "";
        var newStem = Path.GetFileNameWithoutExtension(newFull);
        return files.Select(f =>
        {
            var full = Path.GetFullPath(f);
            if (!IsDefaultNamed(full, oldDir, oldStem)) return full;
            var moved = RenamedDefault(full, oldStem, newDir, newStem);
            return !File.Exists(full) && File.Exists(moved) ? moved : full;
        }).ToList();
    }

    /// <summary>
    /// Renames a project and its owned default exports together, without replacing any destination.
    /// The export record inside the renamed project is updated to the new names.
    /// </summary>
    public static void RenameWithOwnedDefaultExport(string oldProjectPath, string newProjectPath)
    {
        var oldFull = Path.GetFullPath(oldProjectPath);
        var newFull = Path.GetFullPath(newProjectPath);
        var oldStem = Path.GetFileNameWithoutExtension(oldFull);
        var newDir = Path.GetDirectoryName(newFull) ?? "";
        var newStem = Path.GetFileNameWithoutExtension(newFull);
        var paths = new List<(string Source, string Destination)>();
        var newPngs = new List<string>();
        foreach (var oldPng in GetOwnedDefaultPngs(oldProjectPath))
        {
            var newPng = RenamedDefault(oldPng, oldStem, newDir, newStem);
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
        UpdateRecordAfterRename(oldFull, newFull);
    }

    /// <summary>
    /// 이름을 바꾼 프로젝트 파일 안의 내보내기 기록을 새 이름으로 고쳐 쓴다(project.json만 바꾸고 나머지 항목은 그대로 복사).
    /// 기록이 없거나 고칠 수 없으면 그대로 둔다(기록은 정리·숨김에만 쓰이고, 남의 기록으로 보이면 무시되므로 안전).
    /// </summary>
    private static void UpdateRecordAfterRename(string oldFull, string newFull)
    {
        var temp = newFull + ".gpixel-tmp";
        try
        {
            ProjectDto dto;
            using (var src = ZipFile.OpenRead(newFull))
            {
                dto = ReadDto(src);
                if (ResolveRecord(dto.Exported, oldFull) is not { } files) return;
                dto.Exported = ToRecordDto(RemapExportRecord(files, oldFull, newFull), newFull);
                using var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                using var dst = new ZipArchive(fs, ZipArchiveMode.Create);
                WriteProjectJson(dst, dto);
                foreach (var e in src.Entries)
                {
                    if (string.Equals(e.FullName, ProjectEntryName, StringComparison.Ordinal)) continue;
                    var copy = dst.CreateEntry(e.FullName, CompressionLevel.NoCompression);
                    using var from = e.Open();
                    using var to = copy.Open();
                    from.CopyTo(to);
                }
            }
            File.Move(temp, newFull, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
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
            project = Load(fs, warnings, full);
        project.Path = full;
        project.IsDirty = false;
        return project;
    }

    /// <inheritdoc cref="Load(string, ICollection{string}?)"/>
    public static SpriteProject Load(Stream stream, ICollection<string>? warnings = null) => Load(stream, warnings, null);

    /// <param name="fullPath">프로젝트 파일 경로. 버전 1 아틀라스의 예전 내보내기 이름(파일 이름, null이면 "sprite")과 내보내기 기록에 쓴다.</param>
    private static SpriteProject Load(Stream stream, ICollection<string>? warnings, string? fullPath)
    {
        var baseName = fullPath is null ? null : Path.GetFileNameWithoutExtension(fullPath);
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
            project.ExportedFiles = ResolveRecord(dto.Exported, fullPath);
            foreach (var e in zip.Entries)
            {
                if (CelEntry().Match(e.FullName.Replace('\\', '/')) is not { Success: true } m) continue;
                if (!TryIndex(m, 1, fileLayers.Count, out var li) || !TryIndex(m, 2, project.Frames.Count, out var fi)
                    || fileLayers[li].IsGroup || !TrySkin(m, fileLayers[li], out var skin))
                {
                    warnings?.Add($"쓰이지 않는 셀 파일을 무시했습니다: {e.FullName}");
                    continue;
                }
                var layer = fileLayers[li];
                if (ReadCel(e, project.CelSize(layer), warnings) is { } img) project.SetCelRaw(layer, project.Frames[fi], img, skin);
            }
            project.NormalizeSkins();
            return project;
        }
    }

    /// <summary>셀 파일의 스킨 번호(<c>_S{n}</c>): 없으면 첫 스킨(null). 있으면 그룹에 그 번호(1~)의 스킨이 있는 부위일 때만 유효.</summary>
    private static bool TrySkin(Match m, Layer layer, out Skin? skin)
    {
        skin = null;
        if (!m.Groups[3].Success) return true;
        var skins = layer is { IsPart: true, Parent: { IsGroup: true } g } ? g.AllSkins : [];
        if (!TryIndex(m, 3, skins.Count, out var si) || si == 0) return false;
        skin = skins[si];
        return true;
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
        ApplySheets(project, dto.Export ?? new ExportDto(), [], warnings);
        return project;
    }

    // ───────────── 변환 ─────────────

    private static ProjectDto ToDto(SpriteProject p, string? projectPath) => new()
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
            EffectsCollapsed = l.EffectsCollapsed ? true : null,
            PartWidth = l.IsPart ? l.PartSize.Width : null,
            PartHeight = l.IsPart ? l.PartSize.Height : null,
            PartX = l.IsPart ? l.PartOffset.X : null,
            PartY = l.IsPart ? l.PartOffset.Y : null,
            PartPivot = l.IsPart ? PivotDto.From(l.PartPivot) : null,
            PartBorder = l.IsPart && !l.PartBorder.IsNone ? [l.PartBorder.Left, l.PartBorder.Top, l.PartBorder.Right, l.PartBorder.Bottom] : null,
            PartSource = l.IsPart && l.PartSourceRect is { } r ? [r.X, r.Y, r.Width, r.Height] : null,
            Skins = l.IsGroup ? l.AllSkins.Select(s => s.Name).ToList() : null,
            SkinOwners = l.IsGroup && l.AllSkins.Any(s => s.Owner is not null) ? l.AllSkins.Select(s => s.Owner is { } o ? p.IndexOf(o) : -1).ToList() : null,
            ActiveSkin = l.IsGroup && l.SkinList.IndexOf(l.ActiveSkin!) is > 0 and var ai ? ai : null,
            Skin = l is { IsPart: true, SkinOverride: { } so, Parent: { } pg } ? pg.SkinList.IndexOf(so) : null,
            Effects = l.Effects.Count > 0 ? l.Effects.Select(e => (EffectDto?)EffectDto.From(e)).ToList() : null,
            AutoLayout = l is { IsGroup: true, AutoLayout: { } al } ? GroupLayoutDto.From(al) : null,
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
        Unity = UnityDto.From(p.Sprite),
        Export = ExportDto.From(p.Export, p),
        Outfits = p.Outfits.Count == 0 ? null : p.Outfits.Select(o => new OutfitDto
        {
            Name = o.Name,
            Slots = o.Slots.Select(s => SkinRef(p, s.Group, s.Group, s.Skin)).OfType<SkinRefDto>().ToList(),
            Parts = o.Parts.Select(s => SkinRef(p, s.Part, s.Part.Parent, s.Skin)).OfType<SkinRefDto>().ToList(),
        }).ToList(),
        Exported = ToRecordDto(p.ExportedFiles, projectPath),
    };

    private static List<T>? NullIfEmpty<T>(IEnumerable<T> items) => items.ToList() is { Count: > 0 } list ? list : null;

    private static int IndexOf<T>(IReadOnlyList<T> list, T item) where T : class
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }

    /// <summary>(레이어 번호, 그 그룹의 스킨 번호). 레이어가 프로젝트에 없거나 스킨이 그룹에 없으면 null(지운 항목은 저장하지 않음).</summary>
    private static SkinRefDto? SkinRef(SpriteProject p, Layer layer, Layer? group, Skin skin)
        => p.IndexOf(layer) is >= 0 and var li && group is { IsGroup: true } g && p.IndexOf(g) >= 0 && IndexOf(g.AllSkins, skin) is >= 0 and var si
            ? new SkinRefDto { Layer = li, Skin = si } : null;

    /// <summary>스킨이 든 그룹 기준 참조(내보내기에서 뺄 스킨).</summary>
    private static SkinRefDto? GroupSkinRef(SpriteProject p, Skin skin)
        => p.Layers.FirstOrDefault(l => l.IsGroup && IndexOf(l.Skins, skin) >= 0) is { } g ? SkinRef(p, g, g, skin) : null;

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
                EffectsCollapsed = l.EffectsCollapsed == true,
            };
            if (kind == LayerKind.Part)
            {
                layer.PartSize = new Size(Math.Clamp(l.PartWidth ?? 1, 1, PixelImage.MaxSize), Math.Clamp(l.PartHeight ?? 1, 1, PixelImage.MaxSize));
                layer.PartOffset = new Point(l.PartX ?? 0, l.PartY ?? 0);
                layer.PartPivot = l.PartPivot?.ToPivot() ?? Pivot.Center;
                layer.PartBorder = l.PartBorder is { Length: >= 4 } b ? new SpriteBorder(b[0], b[1], b[2], b[3]) : SpriteBorder.None;
                layer.PartSourceRect = l.PartSource is { Length: >= 4 } r && r[2] > 0 && r[3] > 0 ? new Rectangle(r[0], r[1], r[2], r[3]) : null;
            }
            else if (kind == LayerKind.Group && l.Skins is { Count: > 0 } names)
            {
                // 스킨 이름은 그룹 안에서 겹치지 않게(손상된 파일 대비). 스킨이 없던 예전 파일은 "기본" 하나.
                var skins = new List<Skin>(names.Count);
                foreach (var n in names)
                {
                    var name = string.IsNullOrWhiteSpace(n) ? "스킨" : n.Trim();
                    var unique = name;
                    for (int i = 2; skins.Any(s => s.Name == unique); i++) unique = $"{name} {i}";
                    skins.Add(new Skin(unique));
                }
                layer.ReplaceSkinsRaw(skins);
                if (l.ActiveSkin is int ai && ai >= 0 && ai < skins.Count) layer.SetActiveSkinRaw(skins[ai]);
            }
            if (kind == LayerKind.Group && l.AutoLayout is { } layoutDto) layer.AutoLayout = layoutDto.ToLayout();
            if (l.Effects is { Count: > 0 } effects)
            {
                var list = new List<LayerEffect>(effects.Count);
                foreach (var e in effects)
                    if (e?.ToEffect() is { } effect) list.Add(effect);
                    else warnings?.Add($"레이어 \"{layer.Name}\"의 알 수 없는 효과({e?.Type})를 건너뛰었습니다.");
                layer.Effects = list;
            }
            fileLayers.Add(layer);
            p.AddLayerRaw(layer);
        }
        for (int i = 0; i < fileLayers.Count; i++)
            if (fileLayers[i].IsPart && layerDtos[i].Parent is int gi && gi >= 0 && gi < fileLayers.Count && fileLayers[gi].IsGroup)
                fileLayers[i].Parent = fileLayers[gi];
        if (p.NormalizeHierarchy()) warnings?.Add("레이어 그룹 구조가 잘못되어 고쳤습니다.");
        for (int i = 0; i < fileLayers.Count; i++)
            if (fileLayers[i] is { IsPart: true, Parent: { IsGroup: true } pg } part && layerDtos[i].Skin is int si && si >= 0 && si < pg.AllSkins.Count)
                part.SetSkinOverrideRaw(pg.AllSkins[si]);
        // 부위 전용 스킨의 주인(그 그룹의 부위가 아니면 전체 스킨으로 — 순서 정리는 셀을 읽은 뒤 NormalizeSkins)
        for (int i = 0; i < fileLayers.Count; i++)
            if (fileLayers[i].IsGroup && layerDtos[i].SkinOwners is { } owners)
            {
                var all = fileLayers[i].AllSkins;
                for (int k = 1; k < all.Count && k < owners.Count; k++)
                    if (ResolveLayer(fileLayers, owners[k]) is { IsPart: true } owner && ReferenceEquals(owner.Parent, fileLayers[i])) all[k].Owner = owner;
            }
        if (p.Layers.Count == 0) p.AddLayerRaw(new Layer(p.NextLayerName()));

        foreach (var f in dto.Frames ?? [])
            p.AddFrameRaw(new Frame
            {
                DurationMs = f.DurationMs,
                Pivot = f.Pivot?.ToPivot(),
                Name = string.IsNullOrWhiteSpace(f.Name) ? null : f.Name,
            });
        if (p.Frames.Count == 0) p.AddFrameRaw(new Frame());
        // 효과 프레임 범위는 프레임 수에 맞춘다(손상된 파일 대비 — 밖은 안으로, 모든 프레임을 덮으면 모든 프레임).
        foreach (var layer in p.Layers)
            if (layer.Effects.Any(e => e.Frames is not null))
                layer.Effects = layer.Effects.Select(e => e.Frames is { } r ? e with { Frames = FrameRange.Of(r.From, r.To, p.Frames.Count) } : e).ToArray();

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
        ApplyOutfits(p, dto, fileLayers);
        if (dto.Export is { } export) ApplySheets(p, export, fileLayers, warnings);
        return (p, fileLayers);
    }

    /// <summary>파일 안 (레이어 번호, 스킨 번호) → 그룹 스킨. 그룹이 아니거나 번호가 잘못되면 null.</summary>
    private static Skin? ResolveSkin(List<Layer> fileLayers, SkinRefDto r)
        => r.Layer >= 0 && r.Layer < fileLayers.Count && fileLayers[r.Layer] is var l
           && (l.IsGroup ? l : l.IsPart ? l.Parent : null) is { IsGroup: true } g && r.Skin >= 0 && r.Skin < g.AllSkins.Count ? g.AllSkins[r.Skin] : null;

    private static Layer? ResolveLayer(List<Layer> fileLayers, int index) => index >= 0 && index < fileLayers.Count ? fileLayers[index] : null;

    /// <summary>착장 프리셋을 읽는다. 잘못된 항목은 건너뛴다.</summary>
    private static void ApplyOutfits(SpriteProject p, ProjectDto dto, List<Layer> fileLayers)
    {
        foreach (var o in dto.Outfits ?? [])
        {
            var slots = new List<OutfitSlot>();
            foreach (var s in o.Slots ?? [])
                if (ResolveLayer(fileLayers, s.Layer) is { IsGroup: true } g && ResolveSkin(fileLayers, s) is { } skin && !slots.Any(x => x.Group == g))
                    slots.Add(new OutfitSlot(g, skin));
            var parts = new List<OutfitPart>();
            foreach (var s in o.Parts ?? [])
                if (ResolveLayer(fileLayers, s.Layer) is { IsPart: true } part && ResolveSkin(fileLayers, s) is { } skin && !parts.Any(x => x.Part == part))
                    parts.Add(new OutfitPart(part, skin));
            p.AddOutfitRaw(new Outfit(p.UniqueOutfitName(o.Name ?? ""), slots, parts));
        }
    }

    /// <summary>Unity·내보내기 설정(시트 설정 제외 — <see cref="ApplySheets"/>)을 적용한다.</summary>
    private static void ApplySettings(SpriteProject p, ProjectDto dto)
    {
        dto.Unity?.ApplyTo(p.Sprite);
        if (dto.Export is { } e) ApplyExport(p.Export, e);
    }

    internal static void ApplyExport(ExportSettings x, ExportDto e)
    {
        x.ExportOnSave = e.OnSave;
        x.Folder = string.IsNullOrWhiteSpace(e.Folder) ? null : e.Folder;
        x.FileName = string.IsNullOrWhiteSpace(e.FileName) ? null : e.FileName;
        x.ExportFrames = e.ExportFrames;
        x.ExportParts = e.ExportParts;
        x.PartsFileName = string.IsNullOrWhiteSpace(e.PartsFileName) ? null : e.PartsFileName;
        x.ImageFormat = Enum.IsDefined(e.ImageFormat) ? e.ImageFormat : ExportImageFormat.Png;
        x.JpegQuality = e.JpegQuality;
        x.SingleFrameIndex = e.SingleFrame;
        x.WriteMeta = e.WriteMeta;
        x.WriteSpriteLibrary = e.SpriteLibrary == true;
        x.KeepImportedRects = e.KeepImportedRects;
    }

    /// <summary>
    /// 시트 설정: 새 파일은 <c>sheet</c>(공통)·<c>partsSheet</c>(부위 아틀라스 따로). 예전 파일(프레임 시트·부위 아틀라스 설정이 따로 있던 형식)은
    /// 두 설정을 각각 만들어, 부위 아틀라스 결과가 공통(프레임 시트) 설정으로도 같으면(또는 한쪽 결과를 내보내지 않으면) 하나로 합치고,
    /// 다르면 부위 아틀라스를 "따로"(<see cref="ExportSettings.PartsOverride"/>)로 남겨 결과가 조용히 바뀌지 않게 한다.
    /// </summary>
    private static void ApplySheets(SpriteProject p, ExportDto e, List<Layer> fileLayers, ICollection<string>? warnings)
    {
        var x = p.Export;
        if (e.Sheet is { } sheet)
        {
            sheet.ApplyTo(x.Common, p, fileLayers);
            if (e.PartsSheet is { } ps)
            {
                var o = new SheetOptions();
                ps.ApplyTo(o, p, fileLayers);
                x.PartsOverride = o;
            }
            return;
        }

        // ── 예전 형식 → 공통 설정 ──
        var frames = new SheetOptions
        {
            Layout = SheetLayout.Grid,
            Spacing = e.Spacing ?? 0,
            Trim = e.Trim == true,
            MaxWidth = e.AtlasMaxWidth ?? 0,
            PackOrder = e.AtlasOrder ?? AtlasPackOrder.KeepOrder,
            SplitTags = e.SplitFrames == true,
            SplitParts = e.SplitParts == true,
            Naming = SpriteNaming.ByTag,
            ExcludedLayers = (e.FrameExcludedLayers ?? []).Select(i => ResolveLayer(fileLayers, i)).OfType<Layer>().Where(l => !l.IsPart).Distinct().ToArray(),
            BakeInheritedSkins = e.BakeInheritedSkins == true,
        };
        // 예전 프레임 시트: 태그마다 한 줄(줄바꿈 없음, 태그가 없던 파일은 격자와 같았음) / 격자(프레임 순, 칸 수 설정 — 설정이 없던 파일은 열 수 > 0 = 가로 N칸, 0 = 한 줄)
        if (string.Equals(e.Layout, "RowPerTag", StringComparison.OrdinalIgnoreCase) && p.Tags.Count > 0)
        {
            frames.Order = GridOrder.ByPartTag;
            frames.Grid = SheetGrid.Columns;
            frames.GridCount = SheetOptions.MaxGridCount;
        }
        else if (e.FrameGrid is { } fg && Enum.IsDefined(fg))
        {
            frames.Grid = fg;
            frames.GridCount = e.FrameGridCount ?? 1;
        }
        else if (e.Columns is > 0 and var columns)
        {
            frames.Grid = SheetGrid.Columns;
            frames.GridCount = columns;
        }
        else
        {
            frames.Grid = SheetGrid.Rows;
            frames.GridCount = 1;
        }
        var outfits = (e.FrameOutfits ?? []).Where(i => i >= 0 && i < p.Outfits.Count).Select(i => p.Outfits[i]).Distinct().ToArray();
        if (outfits.Length > 0)
        {
            // 예전 "착장마다 따로 파일" → 고른 스킨(착장만) + 스킨마다 따로 파일
            frames.Skins = SkinSource.Selected;
            frames.Outfits = outfits;
            frames.ExcludedSkins = p.Layers.Where(l => l.IsGroup).SelectMany(l => l.Skins).ToArray();
            frames.SplitSkins = true;
            if (e.FrameCurrentOutfit != false)
                warnings?.Add("예전 \"지금 착장 + 착장 프리셋\" 프레임 시트 설정을 \"고른 스킨\"으로 옮겼습니다 — 지금 착장 파일(이름 그대로)은 더 만들지 않습니다. 필요하면 지금 착장을 착장으로 저장해 고르세요.");
        }

        var legacyParts = e.PartsLayout ?? "Packed";
        var parts = new SheetOptions
        {
            Layout = string.Equals(legacyParts, "Packed", StringComparison.OrdinalIgnoreCase) ? SheetLayout.Packed : SheetLayout.Grid,
            Order = legacyParts.ToLowerInvariant() switch
            {
                "rowperframe" => GridOrder.ByFrame,
                "rowperparttag" => GridOrder.ByPartTag,
                _ => GridOrder.ByPart,
            },
            Grid = e.PartsGrid is { } pg && Enum.IsDefined(pg) ? pg : SheetGrid.Auto,
            GridCount = e.PartsGrid is { } pg2 && Enum.IsDefined(pg2) ? e.PartsGridCount ?? 1 : 1,
            Spacing = frames.Spacing,
            PowerOfTwo = e.AtlasPowerOfTwo == true,
            MaxWidth = frames.MaxWidth,
            PackOrder = frames.PackOrder,
            SplitSkins = e.SplitSkins == true,
            SplitParts = e.SplitParts == true,
            // 이름 규칙이 없던 파일은 프레임 번호(이름이 바뀌면 Unity 참조가 끊김).
            Naming = e.PartsNaming is { } pn && Enum.IsDefined(pn) ? pn : SpriteNaming.FrameNumber,
            Skins = SkinSource.Selected,
            ExcludedSkins = (e.ExcludedSkins ?? []).Select(r => ResolveSkin(fileLayers, r)).OfType<Skin>().Distinct().ToArray(),
            Outfits = frames.Outfits,
            BakeInheritedSkins = frames.BakeInheritedSkins,
        };

        bool partsMatter = x.ExportParts && p.Layers.Any(l => l.IsPart);
        if (!x.ExportFrames && partsMatter) x.Common.CopyFrom(parts);
        else
        {
            x.Common.CopyFrom(frames);
            if (partsMatter && !SameForParts(p, frames, parts)) x.PartsOverride = parts;
        }
    }

    /// <summary>부위 아틀라스 결과가 두 설정에서 같은지(이 프로젝트에서 뜻이 있는 항목만 비교 — 예전 파일 이행용).</summary>
    internal static bool SameForParts(SpriteProject p, SheetOptions a, SheetOptions b)
    {
        bool tags = p.Tags.Count > 0;
        bool skins = p.Layers.Any(l => l.IsGroup && l.Skins.Count > 1);
        GridOrder Effective(GridOrder o)
        {
            if (tags || !o.Keys.Contains(GridKey.Tag)) return o;
            int at = o.Keys.ToList().IndexOf(GridKey.Tag);
            return new GridOrder(o.Keys.Where(k => k != GridKey.Tag).ToList(), o.RowBreak - (at < o.RowBreak ? 1 : 0)).Normalized();
        }
        if (a.Layout != b.Layout || a.Spacing != b.Spacing || a.Trim != b.Trim || a.PowerOfTwo != b.PowerOfTwo || a.SplitParts != b.SplitParts) return false;
        if (Effective(a.Order) != Effective(b.Order)) return false;
        if (a.Layout == SheetLayout.Grid && (a.Grid != b.Grid || (a.Grid != SheetGrid.Auto && a.GridCount != b.GridCount))) return false;
        if (a.Layout == SheetLayout.Packed && (a.MaxWidth != b.MaxWidth || a.PackOrder != b.PackOrder)) return false;
        if (tags && (a.SplitTags != b.SplitTags || a.Naming != b.Naming)) return false;
        if (!a.ExcludedLayers.SequenceEqual(b.ExcludedLayers)) return false;
        if (skins)
        {
            if (a.SplitSkins != b.SplitSkins || a.BakeInheritedSkins != b.BakeInheritedSkins || a.Skins != b.Skins) return false;
            if (a.Skins == SkinSource.Selected && !a.ExcludedSkins.ToHashSet().SetEquals(b.ExcludedSkins)) return false;
        }
        return true;
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
        /// <summary>마지막으로 내보낸 결과 파일(없으면 기록 없음 — 예전 파일).</summary>
        public ExportRecordDto? Exported { get; set; }
        /// <summary>착장 프리셋(없으면 없음 — 예전 파일).</summary>
        public List<OutfitDto>? Outfits { get; set; }
    }

    /// <summary>착장 프리셋: 이름, 그룹마다 스킨(레이어 = 그룹 번호), 부위 재정의(레이어 = 부위 번호, 스킨 = 그 그룹의 스킨 번호).</summary>
    private sealed class OutfitDto
    {
        public string? Name { get; set; }
        public List<SkinRefDto>? Slots { get; set; }
        public List<SkinRefDto>? Parts { get; set; }
    }

    /// <summary>(레이어 번호, 그 레이어(부위면 그 그룹)의 스킨 번호).</summary>
    internal sealed class SkinRefDto
    {
        public int Layer { get; set; } = -1;
        public int Skin { get; set; } = -1;
    }

    /// <summary>내보내기 기록: 기록한 프로젝트 파일 이름과 그 파일 기준 상대 경로(<c>/</c> 구분).</summary>
    private sealed class ExportRecordDto
    {
        public string Owner { get; set; } = "";
        public List<string>? Files { get; set; }
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
        /// <summary>타임라인에서 효과 줄을 접음.</summary>
        public bool? EffectsCollapsed { get; set; }
        public int? PartWidth { get; set; }
        public int? PartHeight { get; set; }
        public int? PartX { get; set; }
        public int? PartY { get; set; }
        public PivotDto? PartPivot { get; set; }
        /// <summary>부위 테두리 [왼쪽, 위, 오른쪽, 아래].</summary>
        public int[]? PartBorder { get; set; }
        /// <summary>부위의 가져온 원래 칸 [x, y, w, h](왼쪽 위 원점).</summary>
        public int[]? PartSource { get; set; }
        /// <summary>그룹: 스킨 이름(순서대로, 첫 스킨 = 기본). 없으면(예전 파일) "기본" 하나.</summary>
        public List<string>? Skins { get; set; }
        /// <summary>그룹: 스킨마다 부위 전용 스킨의 주인 부위 번호(−1 = 전체 스킨). 없으면 모두 전체 스킨.</summary>
        public List<int>? SkinOwners { get; set; }
        /// <summary>그룹: 지금 스킨 번호(없으면 0).</summary>
        public int? ActiveSkin { get; set; }
        /// <summary>부위: 그룹 스킨 대신 보일 스킨 번호(재정의, 없으면 그룹을 따름).</summary>
        public int? Skin { get; set; }
        /// <summary>레이어 효과(적용 순서대로). 없으면 효과 없음.</summary>
        public List<EffectDto?>? Effects { get; set; }
        /// <summary>그룹: 자동 배치 설정. 없으면 끔.</summary>
        public GroupLayoutDto? AutoLayout { get; set; }
    }

    /// <summary>그룹 자동 배치(<see cref="GroupLayout"/>). 없는 값은 기본값.</summary>
    internal sealed class GroupLayoutDto
    {
        public GroupLayoutDirection Direction { get; set; } = GroupLayoutDirection.Row;
        public int? Columns { get; set; }
        public LayoutAlign Align { get; set; } = LayoutAlign.Center;
        public LayoutDistribution Distribution { get; set; } = LayoutDistribution.Center;
        public int Gap { get; set; } = 1;
        public int Margin { get; set; }
        public GroupLayoutOrder Order { get; set; } = GroupLayoutOrder.TopFirst;
        public bool IncludeHidden { get; set; } = true;
        /// <summary>배치 영역 [x, y, w, h]. 없으면 캔버스 전체.</summary>
        public int[]? Area { get; set; }

        public static GroupLayoutDto From(GroupLayout l) => new()
        {
            Direction = l.Direction,
            Columns = l.Direction == GroupLayoutDirection.Grid ? l.Columns : null,
            Align = l.Align,
            Distribution = l.Distribution,
            Gap = l.Gap,
            Margin = l.Margin,
            Order = l.Order,
            IncludeHidden = l.IncludeHidden,
            Area = l.Area is { } a ? [a.X, a.Y, a.Width, a.Height] : null,
        };

        public GroupLayout ToLayout() => new GroupLayout
        {
            Direction = Direction,
            Columns = Columns ?? 2,
            Align = Align,
            Distribution = Distribution,
            Gap = Gap,
            Margin = Margin,
            Order = Order,
            IncludeHidden = IncludeHidden,
            Area = Area is { Length: >= 4 } a ? new Rectangle(a[0], a[1], a[2], a[3]) : null,
        }.Normalized();
    }

    /// <summary>
    /// 레이어 효과 하나. <see cref="Type"/>: outline · shadow · rim · overlay · gradient · innerglow · swap · adjust · reflection, 움직이는 효과 explode · shine · squash
    /// (모르는 종류는 읽을 때 건너뜀).
    /// </summary>
    internal sealed class EffectDto
    {
        public string? Type { get; set; }
        public bool Enabled { get; set; } = true;
        /// <summary>외곽선·그림자·색 덮기·빛 지나가기 색, 가장자리 빛의 빛 색("#RRGGBBAA").</summary>
        public string? Color { get; set; }
        /// <summary>가장자리 빛의 그늘 색.</summary>
        public string? Shade { get; set; }
        public int? Thickness { get; set; }
        public bool? Diagonal { get; set; }
        public bool? Inside { get; set; }
        /// <summary>그림자 X·Y, 터지기 직접 중심 X·Y.</summary>
        public int? X { get; set; }
        public int? Y { get; set; }
        /// <summary>가장자리 빛 방향(<see cref="LightDirection"/> 이름, 모르면 기본값 — 글자로 두어 읽기가 실패하지 않게).</summary>
        public string? Direction { get; set; }
        /// <summary>색 덮기 양, 빛 지나가기 세기, 늘었다 줄기 세기(%).</summary>
        public int? Amount { get; set; }
        /// <summary>움직이는 효과: 빠르기 곡선·반복(<see cref="EffectEasing"/>·<see cref="EffectRepeat"/> 이름)과 반복 횟수.</summary>
        public string? Easing { get; set; }
        public string? Repeat { get; set; }
        public int? RepeatCount { get; set; }
        /// <summary>터지기.</summary>
        public int? Distance { get; set; }
        public string? Center { get; set; }
        public int? Spread { get; set; }
        public int? Gravity { get; set; }
        public int? PieceSize { get; set; }
        public bool? Fade { get; set; }
        public int? FadeStart { get; set; }
        public bool? Shrink { get; set; }
        public int? Seed { get; set; }
        public bool? KeepOriginal { get; set; }
        public int? FlashFrames { get; set; }
        public string? FlashColor { get; set; }
        /// <summary>빛 지나가기.</summary>
        public int? Angle { get; set; }
        public int? Width { get; set; }
        public int? Softness { get; set; }
        /// <summary>늘었다 줄기.</summary>
        public string? Anchor { get; set; }
        public bool? StretchFirst { get; set; }
        public int? Bounces { get; set; }
        // 정적 효과(StaticFx)
        /// <summary>그라데이션 덮기의 색 B(색 A = <see cref="Color"/>).</summary>
        public string? Color2 { get; set; }
        /// <summary>그라데이션 덮기·안쪽 빛의 섞기(<see cref="EffectBlend"/> 이름).</summary>
        public string? Blend { get; set; }
        /// <summary>그라데이션 덮기의 디더(<see cref="EffectDither"/> 이름), 그림자의 디더 밀도(<see cref="ShadowDither"/> 이름, 없으면 반투명 — 예전 파일).</summary>
        public string? Dither { get; set; }
        /// <summary>색 바꾸기: [바꿀 색, 바뀐 색] 쌍들과 허용 차이.</summary>
        public List<string[]>? Pairs { get; set; }
        public int? Tolerance { get; set; }
        /// <summary>색조 보정(<see cref="ColorAdjustMode"/> 이름과 방식별 값).</summary>
        public string? Mode { get; set; }
        public int? Hue { get; set; }
        public int? Brightness { get; set; }
        public int? Contrast { get; set; }
        public int? Saturation { get; set; }
        /// <summary>반사(색 입히기 = <see cref="Color"/>).</summary>
        public int? Gap { get; set; }
        public int? Height { get; set; }
        public int? OpacityStart { get; set; }
        public int? OpacityEnd { get; set; }
        public bool? DitherFade { get; set; }
        public int? Wave { get; set; }
        public bool? Above { get; set; }
        /// <summary>적용할 프레임 범위 [시작, 끝](0부터, 양 끝 포함). 없으면 모든 프레임(예전 파일).</summary>
        public int[]? Frames { get; set; }
        // 움직이는 효과(AnimFx): 피격 번쩍임 모양·깜빡이기 간격 방식은 Mode, 둥실둥실 높이는 Height, 쓸기 방향은 Direction, 녹아내리기 불규칙함은 Spread,
        // 충격파 최대 반지름은 Distance를 함께 쓴다.
        /// <summary>피격 번쩍임·깜빡이기·흔들기 횟수, 둥실둥실 오르내림 횟수, 잔상 개수.</summary>
        public int? Count { get; set; }
        /// <summary>깜빡이기 숨김 방식(<see cref="BlinkHide"/>), 나타나기 방식(<see cref="AppearStyle"/>) 이름.</summary>
        public string? Style { get; set; }
        /// <summary>흔들기·둥실둥실 축(<see cref="EffectAxis"/> 이름).</summary>
        public string? Axis { get; set; }
        public bool? Decay { get; set; }
        public bool? Regular { get; set; }
        /// <summary>잔상 간격(프레임).</summary>
        public int? Interval { get; set; }
        /// <summary>깜빡이기 반투명 불투명도, 잔상 불투명도(%).</summary>
        public int? Opacity { get; set; }
        public bool? Tint { get; set; }
        /// <summary>색 순환 색들("#RRGGBBAA").</summary>
        public string[]? Colors { get; set; }
        public int? Steps { get; set; }
        public bool? Reverse { get; set; }
        public bool? Clockwise { get; set; }
        public bool? Behind { get; set; }
        public bool? Stretch { get; set; }
        public int? OnFrames { get; set; }
        public int? OffFrames { get; set; }
        public bool? Disappear { get; set; }
        public int? Edge { get; set; }
        // 홀로그램: 색 = Color, 색 세기 = Amount, 주사선 간격 = Gap·방식 = Style, 띠 두께 = Thickness, 투명도 = Opacity, 디더 = DitherFade, 최대 밀림 = Distance, 시드 = Seed.
        public int? ScanStrength { get; set; }
        public bool? Scroll { get; set; }
        public bool? Band { get; set; }
        public int? BandStrength { get; set; }
        public int? FlickerRate { get; set; }
        public int? FlickerStrength { get; set; }
        public int? GlitchRate { get; set; }
        public bool? RgbSplit { get; set; }
        public bool? EdgeGlow { get; set; }
        public int? EdgeOpacity { get; set; }

        public static EffectDto From(LayerEffect effect)
        {
            var dto = effect switch
            {
                OutlineEffect o => new EffectDto { Type = "outline", Color = o.Color.ToHex(), Thickness = o.Thickness, Diagonal = o.Diagonal, Inside = o.Inside },
                ShadowEffect s => new EffectDto { Type = "shadow", Color = s.Color.ToHex(), X = s.OffsetX, Y = s.OffsetY, Dither = s.Dither == ShadowDither.None ? null : s.Dither.ToString() },
                RimLightEffect r => new EffectDto { Type = "rim", Color = r.Light.ToHex(), Shade = r.Shade.ToHex(), Direction = r.Direction.ToString() },
                ColorOverlayEffect c => new EffectDto { Type = "overlay", Color = c.Color.ToHex(), Amount = c.Amount },
                ExplodeEffect x => new EffectDto
                {
                    Type = "explode", Distance = x.Distance, Center = x.Center.ToString(), X = x.CenterX, Y = x.CenterY, Spread = x.Spread, Gravity = x.Gravity,
                    PieceSize = x.PieceSize, Fade = x.Fade, FadeStart = x.FadeStart, Shrink = x.Shrink, Seed = x.Seed, KeepOriginal = x.KeepOriginal,
                    FlashFrames = x.FlashFrames, FlashColor = x.FlashColor.ToHex(),
                },
                ShineEffect s => new EffectDto { Type = "shine", Angle = s.Angle, Width = s.Width, Color = s.Color.ToHex(), Amount = s.Strength, Softness = s.Softness },
                SquashEffect q => new EffectDto { Type = "squash", Amount = q.Amount, Anchor = q.Anchor.ToString(), StretchFirst = q.StretchFirst, Bounces = q.Bounces },
                GradientOverlayEffect g => new EffectDto
                {
                    Type = "gradient", Color = g.ColorA.ToHex(), Color2 = g.ColorB.ToHex(), Angle = g.Angle, Amount = g.Strength, Blend = g.Blend.ToString(), Dither = g.Dither.ToString(),
                },
                InnerGlowEffect g => new EffectDto { Type = "innerglow", Color = g.Color.ToHex(), Thickness = g.Size, Amount = g.Strength, Fade = g.Fade, Blend = g.Blend.ToString() },
                PaletteSwapEffect p => new EffectDto { Type = "swap", Pairs = p.Pairs.Select(q => new[] { q.From.ToHex(), q.To.ToHex() }).ToList(), Tolerance = p.Tolerance },
                ColorAdjustEffect c => new EffectDto
                {
                    Type = "adjust", Mode = c.Mode.ToString(), Amount = c.Amount, Hue = c.Hue, Brightness = c.Brightness, Contrast = c.Contrast, Saturation = c.Saturation,
                },
                ReflectionEffect m => new EffectDto
                {
                    Type = "reflection", Gap = m.Gap, Height = m.Height, OpacityStart = m.OpacityStart, OpacityEnd = m.OpacityEnd, DitherFade = m.DitherFade,
                    Color = m.Tint.ToHex(), Wave = m.Wave, Above = m.Above,
                },
                HitFlashEffect hit => new EffectDto { Type = "hitflash", Color = hit.Color.ToHex(), Amount = hit.Strength, Count = hit.Count, Mode = hit.Shape.ToString() },
                BlinkEffect blink => new EffectDto
                {
                    Type = "blink", Mode = blink.Timing.ToString(), OnFrames = blink.OnFrames, OffFrames = blink.OffFrames, Count = blink.Count, Style = blink.Hide.ToString(),
                    Opacity = blink.Opacity,
                },
                ShakeEffect shake => new EffectDto
                {
                    Type = "shake", Axis = shake.Axis.ToString(), Amount = shake.Amount, Count = shake.Count, Decay = shake.Decay, Regular = shake.Regular, Seed = shake.Seed,
                },
                BobEffect bob => new EffectDto { Type = "bob", Height = bob.Height, Count = bob.Cycles, Axis = bob.Axis.ToString() },
                AppearEffect appear => new EffectDto
                {
                    Type = "appear", Disappear = appear.Disappear, Style = appear.Style.ToString(), Direction = appear.From.ToString(), Edge = appear.Edge, Seed = appear.Seed,
                },
                MeltEffect melt => new EffectDto { Type = "melt", Distance = melt.Distance, Spread = melt.Irregularity, Stretch = melt.Stretch, Fade = melt.Fade, Seed = melt.Seed },
                RotateEffect rotate => new EffectDto { Type = "rotate", Angle = rotate.Angle, Anchor = rotate.Anchor.ToString(), Clockwise = rotate.Clockwise },
                AfterimageEffect after => new EffectDto
                {
                    Type = "afterimage", Count = after.Count, Interval = after.Interval, Opacity = after.Opacity, Tint = after.Tint, Color = after.TintColor.ToHex(),
                },
                ColorCycleEffect cycle => new EffectDto { Type = "colorcycle", Colors = cycle.Colors.Select(k => k.ToHex()).ToArray(), Steps = cycle.Steps, Reverse = cycle.Reverse },
                ShockwaveEffect wave => new EffectDto
                {
                    Type = "shockwave", Center = wave.Center.ToString(), X = wave.CenterX, Y = wave.CenterY, Distance = wave.Radius, Thickness = wave.Thickness,
                    Color = wave.Color.ToHex(), Fade = wave.Fade, Behind = wave.Behind,
                },
                HologramEffect holo => new EffectDto
                {
                    Type = "hologram", Color = holo.Color.ToHex(), Amount = holo.TintStrength, Gap = holo.ScanGap, ScanStrength = holo.ScanStrength, Style = holo.ScanStyle.ToString(),
                    Scroll = holo.ScanScroll, Band = holo.Band, Thickness = holo.BandSize, BandStrength = holo.BandStrength, Opacity = holo.Opacity, DitherFade = holo.Dither,
                    FlickerRate = holo.FlickerRate, FlickerStrength = holo.FlickerStrength, GlitchRate = holo.GlitchRate, Distance = holo.GlitchShift, RgbSplit = holo.RgbSplit,
                    EdgeGlow = holo.EdgeGlow, EdgeOpacity = holo.EdgeOpacity, Seed = holo.Seed,
                },
                _ => throw new NotSupportedException($"저장할 수 없는 효과: {effect.GetType().Name}"),
            };
            dto.Enabled = effect.Enabled;
            dto.Frames = effect.Frames is { } f ? [f.From, f.To] : null;
            if (effect is AnimatedEffect a)
            {
                dto.Easing = a.Easing.ToString();
                dto.Repeat = a.Repeat.ToString();
                dto.RepeatCount = a.RepeatCount;
            }
            return dto;
        }

        private static T EnumOr<T>(string? name, T fallback) where T : struct, Enum
            => Enum.TryParse<T>(name, true, out var v) && Enum.IsDefined(v) ? v : fallback;

        private static Color32 ColorOr(string? hex, Color32 fallback) => Color32.TryParseHex(hex, out var c) ? c : fallback;

        /// <summary>효과로 바꾼다(모르는 종류면 null). 프레임 범위는 음수만 걸러 두고, 프레임 수에 맞추기는 프레임을 읽은 뒤에 한다.</summary>
        public LayerEffect? ToEffect()
        {
            var effect = ToKind();
            if (effect is null || Frames is not [int from, int to]) return effect;
            return effect with { Frames = new FrameRange(Math.Max(0, Math.Min(from, to)), Math.Max(0, Math.Max(from, to))) };
        }

        private LayerEffect? ToKind()
        {
            switch (Type?.Trim().ToLowerInvariant())
            {
                case "outline":
                    var o = new OutlineEffect();
                    return o with { Enabled = Enabled, Color = ColorOr(Color, o.Color), Thickness = Thickness ?? o.Thickness, Diagonal = Diagonal ?? o.Diagonal, Inside = Inside ?? o.Inside };
                case "shadow":
                    var s = new ShadowEffect();
                    return s with { Enabled = Enabled, Color = ColorOr(Color, s.Color), OffsetX = X ?? s.OffsetX, OffsetY = Y ?? s.OffsetY, Dither = EnumOr(Dither, s.Dither) };
                case "rim":
                    var r = new RimLightEffect();
                    return r with
                    {
                        Enabled = Enabled, Light = ColorOr(Color, r.Light), Shade = ColorOr(Shade, r.Shade),
                        Direction = EnumOr(Direction, r.Direction),
                    };
                case "overlay":
                    var c = new ColorOverlayEffect();
                    return c with { Enabled = Enabled, Color = ColorOr(Color, c.Color), Amount = Amount ?? c.Amount };
                case "explode":
                    var x = new ExplodeEffect();
                    return Animated(x with
                    {
                        Distance = Distance ?? x.Distance, Center = EnumOr(Center, x.Center), CenterX = X ?? x.CenterX, CenterY = Y ?? x.CenterY,
                        Spread = Spread ?? x.Spread, Gravity = Gravity ?? x.Gravity, PieceSize = PieceSize ?? x.PieceSize, Fade = Fade ?? x.Fade,
                        FadeStart = FadeStart ?? x.FadeStart, Shrink = Shrink ?? x.Shrink, Seed = Seed ?? x.Seed, KeepOriginal = KeepOriginal ?? x.KeepOriginal,
                        FlashFrames = FlashFrames ?? x.FlashFrames, FlashColor = ColorOr(FlashColor, x.FlashColor),
                    });
                case "shine":
                    var s2 = new ShineEffect();
                    return Animated(s2 with
                    {
                        Angle = Angle ?? s2.Angle, Width = Width ?? s2.Width, Color = ColorOr(Color, s2.Color), Strength = Amount ?? s2.Strength, Softness = Softness ?? s2.Softness,
                    });
                case "squash":
                    var q = new SquashEffect();
                    return Animated(q with
                    {
                        Amount = Amount ?? q.Amount, Anchor = EnumOr(Anchor, q.Anchor), StretchFirst = StretchFirst ?? q.StretchFirst, Bounces = Bounces ?? q.Bounces,
                    });
                case "gradient":
                    var g = new GradientOverlayEffect();
                    return g with
                    {
                        Enabled = Enabled, ColorA = ColorOr(Color, g.ColorA), ColorB = ColorOr(Color2, g.ColorB), Angle = Angle ?? g.Angle, Strength = Amount ?? g.Strength,
                        Blend = EnumOr(Blend, g.Blend), Dither = EnumOr(Dither, g.Dither),
                    };
                case "innerglow":
                    var ig = new InnerGlowEffect();
                    return ig with
                    {
                        Enabled = Enabled, Color = ColorOr(Color, ig.Color), Size = Thickness ?? ig.Size, Strength = Amount ?? ig.Strength, Fade = Fade ?? ig.Fade,
                        Blend = EnumOr(Blend, ig.Blend),
                    };
                case "swap":
                    var sw = new PaletteSwapEffect();
                    return sw with
                    {
                        Enabled = Enabled, Tolerance = Tolerance ?? sw.Tolerance,
                        Pairs = (Pairs ?? []).Where(q => q is [_, _] && Color32.TryParseHex(q[0], out _) && Color32.TryParseHex(q[1], out _))
                            .Select(q => new ColorSwap(ColorOr(q[0], default), ColorOr(q[1], default))).ToArray(),
                    };
                case "adjust":
                    var ad = new ColorAdjustEffect();
                    return ad with
                    {
                        Enabled = Enabled, Mode = EnumOr(Mode, ad.Mode), Amount = Amount ?? ad.Amount, Hue = Hue ?? ad.Hue, Brightness = Brightness ?? ad.Brightness,
                        Contrast = Contrast ?? ad.Contrast, Saturation = Saturation ?? ad.Saturation,
                    };
                case "reflection":
                    var m = new ReflectionEffect();
                    return m with
                    {
                        Enabled = Enabled, Gap = Gap ?? m.Gap, Height = Height ?? m.Height, OpacityStart = OpacityStart ?? m.OpacityStart, OpacityEnd = OpacityEnd ?? m.OpacityEnd,
                        DitherFade = DitherFade ?? m.DitherFade, Tint = ColorOr(Color, m.Tint), Wave = Wave ?? m.Wave, Above = Above ?? m.Above,
                    };
                case "hitflash":
                    var hf = new HitFlashEffect();
                    return Animated(hf with { Color = ColorOr(Color, hf.Color), Strength = Amount ?? hf.Strength, Count = Count ?? hf.Count, Shape = EnumOr(Mode, hf.Shape) });
                case "blink":
                    var bl = new BlinkEffect();
                    return Animated(bl with
                    {
                        Timing = EnumOr(Mode, bl.Timing), OnFrames = OnFrames ?? bl.OnFrames, OffFrames = OffFrames ?? bl.OffFrames, Count = Count ?? bl.Count,
                        Hide = EnumOr(Style, bl.Hide), Opacity = Opacity ?? bl.Opacity,
                    });
                case "shake":
                    var sk = new ShakeEffect();
                    return Animated(sk with
                    {
                        Axis = EnumOr(Axis, sk.Axis), Amount = Amount ?? sk.Amount, Count = Count ?? sk.Count, Decay = Decay ?? sk.Decay, Regular = Regular ?? sk.Regular,
                        Seed = Seed ?? sk.Seed,
                    });
                case "bob":
                    var bo = new BobEffect();
                    return Animated(bo with { Height = Height ?? bo.Height, Cycles = Count ?? bo.Cycles, Axis = EnumOr(Axis, bo.Axis) });
                case "appear":
                    var ap = new AppearEffect();
                    return Animated(ap with
                    {
                        Disappear = Disappear ?? ap.Disappear, Style = EnumOr(Style, ap.Style), From = EnumOr(Direction, ap.From), Edge = Edge ?? ap.Edge, Seed = Seed ?? ap.Seed,
                    });
                case "melt":
                    var me = new MeltEffect();
                    return Animated(me with
                    {
                        Distance = Distance ?? me.Distance, Irregularity = Spread ?? me.Irregularity, Stretch = Stretch ?? me.Stretch, Fade = Fade ?? me.Fade, Seed = Seed ?? me.Seed,
                    });
                case "rotate":
                    var ro = new RotateEffect();
                    return Animated(ro with { Angle = Angle ?? ro.Angle, Anchor = EnumOr(Anchor, ro.Anchor), Clockwise = Clockwise ?? ro.Clockwise });
                case "afterimage":
                    var ai = new AfterimageEffect();
                    return Animated(ai with
                    {
                        Count = Count ?? ai.Count, Interval = Interval ?? ai.Interval, Opacity = Opacity ?? ai.Opacity, Tint = Tint ?? ai.Tint, TintColor = ColorOr(Color, ai.TintColor),
                    });
                case "colorcycle":
                    var cc = new ColorCycleEffect();
                    return Animated(cc with
                    {
                        Colors = Colors is null ? cc.Colors : Colors.Where(h => Color32.TryParseHex(h, out _)).Select(h => ColorOr(h, default)).ToArray(),
                        Steps = Steps ?? cc.Steps, Reverse = Reverse ?? cc.Reverse,
                    });
                case "shockwave":
                    var sh = new ShockwaveEffect();
                    return Animated(sh with
                    {
                        Center = EnumOr(Center, sh.Center), CenterX = X ?? sh.CenterX, CenterY = Y ?? sh.CenterY, Radius = Distance ?? sh.Radius, Thickness = Thickness ?? sh.Thickness,
                        Color = ColorOr(Color, sh.Color), Fade = Fade ?? sh.Fade, Behind = Behind ?? sh.Behind,
                    });
                case "hologram":
                    var ho = new HologramEffect();
                    return Animated(ho with
                    {
                        Color = ColorOr(Color, ho.Color), TintStrength = Amount ?? ho.TintStrength, ScanGap = Gap ?? ho.ScanGap, ScanStrength = ScanStrength ?? ho.ScanStrength,
                        ScanStyle = EnumOr(Style, ho.ScanStyle), ScanScroll = Scroll ?? ho.ScanScroll, Band = Band ?? ho.Band, BandSize = Thickness ?? ho.BandSize,
                        BandStrength = BandStrength ?? ho.BandStrength, Opacity = Opacity ?? ho.Opacity, Dither = DitherFade ?? ho.Dither, FlickerRate = FlickerRate ?? ho.FlickerRate,
                        FlickerStrength = FlickerStrength ?? ho.FlickerStrength, GlitchRate = GlitchRate ?? ho.GlitchRate, GlitchShift = Distance ?? ho.GlitchShift,
                        RgbSplit = RgbSplit ?? ho.RgbSplit, EdgeGlow = EdgeGlow ?? ho.EdgeGlow, EdgeOpacity = EdgeOpacity ?? ho.EdgeOpacity, Seed = Seed ?? ho.Seed,
                    });
                default:
                    return null;
            }
        }

        /// <summary>움직이는 효과 공통 설정(없으면 그 종류의 기본값).</summary>
        private AnimatedEffect Animated(AnimatedEffect effect)
            => effect with { Enabled = Enabled, Easing = EnumOr(Easing, effect.Easing), Repeat = EnumOr(Repeat, effect.Repeat), RepeatCount = RepeatCount ?? effect.RepeatCount };
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

    internal sealed class UnityDto
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

        public static UnityDto From(SpriteSettings s) => new()
        {
            SpriteMode = s.Mode,
            PixelsPerUnit = s.PixelsPerUnit,
            DefaultPivot = PivotDto.From(s.DefaultPivot),
            Border = s.Border.IsNone ? null : [s.Border.Left, s.Border.Top, s.Border.Right, s.Border.Bottom],
            FullRect = s.FullRectMesh,
            Readable = s.Readable,
            Texture = TextureDto.From(s),
        };

        public void ApplyTo(SpriteSettings s)
        {
            s.Mode = SpriteMode;
            s.PixelsPerUnit = PixelsPerUnit;
            s.DefaultPivot = DefaultPivot?.ToPivot() ?? Pivot.Center;
            s.Border = Border is { Length: >= 4 } b ? new SpriteBorder(b[0], b[1], b[2], b[3]) : SpriteBorder.None;
            s.FullRectMesh = FullRect;
            s.Readable = Readable;
            Texture?.ApplyTo(s);
        }
    }

    /// <summary>Unity 인스펙터 항목(Extrude·Physics·Advanced·Wrap/Filter·Default 플랫폼).</summary>
    internal sealed class TextureDto
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

    internal sealed class ExportDto
    {
        public bool OnSave { get; set; } = true;
        public string? Folder { get; set; }
        public string? FileName { get; set; }
        public bool ExportFrames { get; set; } = true;
        public bool ExportParts { get; set; } = true;
        public ExportImageFormat ImageFormat { get; set; } = ExportImageFormat.Png;
        public int JpegQuality { get; set; } = 90;
        public string? PartsFileName { get; set; }
        public int SingleFrame { get; set; }
        public bool? WriteMeta { get; set; }
        /// <summary>Sprite Library(.spriteLib) 같이 쓰기(없으면 꺼짐).</summary>
        public bool? SpriteLibrary { get; set; }
        public bool KeepImportedRects { get; set; } = true;
        /// <summary>두 결과가 같이 쓰는 시트 설정(없으면 예전 형식 — 아래 읽기 전용 값들에서 옮김).</summary>
        public SheetDto? Sheet { get; set; }
        /// <summary>부위 아틀라스만 다른 시트 설정(없으면 공통).</summary>
        public SheetDto? PartsSheet { get; set; }

        // ── 예전 형식(읽기 전용, 쓰지 않음) ──
        /// <summary>예전 프레임 시트 배치("Columns" / "RowPerTag").</summary>
        public string? Layout { get; set; }
        /// <summary>예전 파일의 프레임 시트 열 수(0 = 한 줄).</summary>
        public int? Columns { get; set; }
        public SheetGrid? FrameGrid { get; set; }
        public int? FrameGridCount { get; set; }
        public bool? SplitFrames { get; set; }
        public int? Spacing { get; set; }
        public bool? Trim { get; set; }
        public int? AtlasMaxWidth { get; set; }
        public bool? AtlasPowerOfTwo { get; set; }
        public AtlasPackOrder? AtlasOrder { get; set; }
        /// <summary>예전 부위 아틀라스 배치("Packed" / "RowPerPart" / "RowPerFrame" / "RowPerPartTag").</summary>
        public string? PartsLayout { get; set; }
        public SheetGrid? PartsGrid { get; set; }
        public int? PartsGridCount { get; set; }
        public bool? SplitParts { get; set; }
        public bool? SplitSkins { get; set; }
        public bool? FrameCurrentOutfit { get; set; }
        public List<int>? FrameOutfits { get; set; }
        public List<int>? FrameExcludedLayers { get; set; }
        public List<SkinRefDto>? ExcludedSkins { get; set; }
        public bool? BakeInheritedSkins { get; set; }
        public SpriteNaming? PartsNaming { get; set; }

        /// <param name="p">시트 설정의 스킨·레이어·착장을 파일 안 번호로 적을 프로젝트. null이면 적지 않는다(실행취소 기록은 따로 적음).</param>
        public static ExportDto From(ExportSettings x, SpriteProject? p) => new()
        {
            OnSave = x.ExportOnSave,
            Folder = x.Folder,
            FileName = x.FileName,
            ExportFrames = x.ExportFrames,
            ExportParts = x.ExportParts,
            PartsFileName = x.PartsFileName,
            ImageFormat = x.ImageFormat,
            JpegQuality = x.JpegQuality,
            SingleFrame = x.SingleFrameIndex,
            WriteMeta = x.WriteMeta,
            SpriteLibrary = x.WriteSpriteLibrary ? true : null,
            KeepImportedRects = x.KeepImportedRects,
            Sheet = SheetDto.From(p, x.Common),
            PartsSheet = x.PartsOverride is { } po ? SheetDto.From(p, po) : null,
        };
    }

    /// <summary>시트 설정 한 벌(<see cref="SheetOptions"/>). 스킨·레이어·착장은 파일 안 번호.</summary>
    internal sealed class SheetDto
    {
        public SheetLayout Layout { get; set; } = SheetLayout.Grid;
        public List<GridKey>? Order { get; set; }
        public int RowBreak { get; set; } = 2;
        public SheetGrid Grid { get; set; } = SheetGrid.Auto;
        public int GridCount { get; set; } = 4;
        public int Spacing { get; set; } = 1;
        public bool Trim { get; set; }
        public bool PowerOfTwo { get; set; }
        public int MaxWidth { get; set; }
        public AtlasPackOrder PackOrder { get; set; } = AtlasPackOrder.KeepOrder;
        public bool SplitTags { get; set; }
        public bool SplitSkins { get; set; }
        public bool SplitParts { get; set; }
        public SpriteNaming Naming { get; set; } = SpriteNaming.ByTag;
        public SkinSource Skins { get; set; } = SkinSource.Current;
        public List<SkinRefDto>? ExcludedSkins { get; set; }
        public List<int>? Outfits { get; set; }
        public List<int>? ExcludedLayers { get; set; }
        public bool? BakeInheritedSkins { get; set; }

        /// <param name="p">스킨·레이어·착장을 파일 안 번호로 적을 프로젝트. null이면 값만 적는다.</param>
        public static SheetDto From(SpriteProject? p, SheetOptions o) => new()
        {
            Layout = o.Layout,
            Order = o.Order.Keys.ToList(),
            RowBreak = o.Order.RowBreak,
            Grid = o.Grid,
            GridCount = o.GridCount,
            Spacing = o.Spacing,
            Trim = o.Trim,
            PowerOfTwo = o.PowerOfTwo,
            MaxWidth = o.MaxWidth,
            PackOrder = o.PackOrder,
            SplitTags = o.SplitTags,
            SplitSkins = o.SplitSkins,
            SplitParts = o.SplitParts,
            Naming = o.Naming,
            Skins = o.Skins,
            ExcludedSkins = p is null ? null : NullIfEmpty(o.ExcludedSkins.Select(s => GroupSkinRef(p, s)).OfType<SkinRefDto>()),
            Outfits = p is null ? null : NullIfEmpty(o.Outfits.Select(x => IndexOf(p.Outfits, x)).Where(i => i >= 0)),
            ExcludedLayers = p is null ? null : NullIfEmpty(o.ExcludedLayers.Select(p.IndexOf).Where(i => i >= 0)),
            BakeInheritedSkins = o.BakeInheritedSkins ? true : null,
        };

        public void ApplyTo(SheetOptions o, SpriteProject p, List<Layer> fileLayers)
        {
            ApplyValues(o);
            o.ExcludedSkins = (ExcludedSkins ?? []).Select(r => ResolveSkin(fileLayers, r)).OfType<Skin>().Distinct().ToArray();
            o.Outfits = (Outfits ?? []).Where(i => i >= 0 && i < p.Outfits.Count).Select(i => p.Outfits[i]).Distinct().ToArray();
            o.ExcludedLayers = (ExcludedLayers ?? []).Select(i => ResolveLayer(fileLayers, i)).OfType<Layer>().Where(l => !l.IsPart).Distinct().ToArray();
        }

        /// <summary>스킨·레이어·착장 목록을 뺀 값만 적용한다.</summary>
        public void ApplyValues(SheetOptions o)
        {
            o.Layout = Enum.IsDefined(Layout) ? Layout : SheetLayout.Grid;
            o.Order = new GridOrder((Order ?? [GridKey.Part, GridKey.Skin, GridKey.Frame]).Where(Enum.IsDefined).ToList(), RowBreak);
            o.Grid = Enum.IsDefined(Grid) ? Grid : SheetGrid.Auto;
            o.GridCount = GridCount;
            o.Spacing = Spacing;
            o.Trim = Trim;
            o.PowerOfTwo = PowerOfTwo;
            o.MaxWidth = MaxWidth;
            o.PackOrder = Enum.IsDefined(PackOrder) ? PackOrder : AtlasPackOrder.KeepOrder;
            o.SplitTags = SplitTags;
            o.SplitSkins = SplitSkins;
            o.SplitParts = SplitParts;
            o.Naming = Enum.IsDefined(Naming) ? Naming : SpriteNaming.ByTag;
            o.Skins = Enum.IsDefined(Skins) ? Skins : SkinSource.Current;
            o.BakeInheritedSkins = BakeInheritedSkins == true;
        }
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

    internal sealed class PivotDto
    {
        public float X { get; set; } = 0.5f;
        public float Y { get; set; } = 0.5f;
        public static PivotDto From(Pivot p) => new() { X = p.X, Y = p.Y };
        public Pivot ToPivot() => float.IsFinite(X) && float.IsFinite(Y) ? new Pivot(X, Y) : Pivot.Center;
    }
}
