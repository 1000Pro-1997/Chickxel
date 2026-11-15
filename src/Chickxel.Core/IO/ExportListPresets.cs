using System.Text.Json;
using System.Text.Json.Serialization;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// 내보내기 목록 프리셋 하나: 프로젝트의 시트(목록 첫 줄 — 시트 설정 프리셋 + 이름·폴더)와 목록의 나머지 줄(시트 줄·애니메이션 줄) 전부.
/// 파일 이름이 프로젝트 이름으로 시작하면 그 부분을 <see cref="NameToken"/>으로 바꿔 담아, 다른 프로젝트에 적용하면 그 프로젝트 이름이 된다.
/// 폴더는 프로젝트 폴더 기준 상대 경로만 담는다(전체 경로는 빼서 프로젝트 폴더).
/// 고른 스킨·착장·뺀 레이어 같은 프로젝트 의존 값은 <see cref="ExportPreset"/>처럼 담지 않는다.
/// </summary>
public sealed class ExportListPreset
{
    /// <summary>담을 때 프로젝트 이름 자리.</summary>
    public const string NameToken = "{name}";

    public string Name { get; set; } = "";

    /// <summary>프로젝트의 시트 설정(목록에 있는지 = 프레임 시트·부위 아틀라스 켬).</summary>
    public ExportPreset Sheet { get; set; } = new();
    public string? FileName { get; set; }
    public string? Folder { get; set; }

    public List<ExportListPresetRow> Rows { get; set; } = new();

    /// <summary>지금 목록을 담는다(<paramref name="projectName"/> = 확장자 뺀 프로젝트 파일 이름).</summary>
    public static ExportListPreset Capture(ExportSettings x, string projectName, string name) => new()
    {
        Name = name,
        Sheet = ExportPreset.Capture(x, name),
        FileName = Tokenize(x.FileName, projectName),
        Folder = RelativeOnly(x.Folder),
        Rows = x.Entries.Select(e => e.Sheet is { } s
            ? new ExportListPresetRow { Sheet = ExportPreset.Capture(s, ""), FileName = Tokenize(s.FileName, projectName), Folder = RelativeOnly(s.Folder) }
            : new ExportListPresetRow { Animation = ExportListPresetAnimation.From(e.Animation!, projectName) }).ToList(),
    };

    /// <summary>
    /// 목록을 이 프리셋으로 바꾼다: 프로젝트의 시트 설정과 이름·폴더, 목록의 줄 전부(실행취소는 부르는 쪽이 묶음).
    /// 시트 줄은 프로젝트의 시트를 바탕으로(고른 스킨 등 프로젝트 의존 값은 그대로) 프리셋 값을 덮는다.
    /// </summary>
    public void ApplyTo(ExportSettings x, string projectName)
    {
        Sheet.ApplyTo(x);
        x.FileName = Untokenize(FileName, projectName);
        x.Folder = Folder;
        var basis = x.CloneSheet();
        x.Entries.Clear();
        foreach (var row in Rows)
        {
            if (row.Sheet is { } preset)
            {
                var sheet = basis.CloneSheet();
                preset.ApplyTo(sheet);
                sheet.FileName = Untokenize(row.FileName, projectName);
                sheet.Folder = row.Folder;
                x.Entries.Add(ExportEntry.ForSheet(sheet));
            }
            else if (row.Animation?.ToExport(projectName) is { } a) x.Entries.Add(ExportEntry.ForAnimation(a));
        }
    }

    private static string? Tokenize(string? name, string projectName)
        => name is null ? null
            : projectName.Length > 0 && name.StartsWith(projectName, StringComparison.Ordinal) ? NameToken + name[projectName.Length..] : name;

    internal static string? Untokenize(string? name, string projectName)
        => name?.StartsWith(NameToken, StringComparison.Ordinal) == true ? projectName + name[NameToken.Length..] : name;

    private static string? RelativeOnly(string? folder) => string.IsNullOrWhiteSpace(folder) || Path.IsPathRooted(folder) ? null : folder;
}

/// <summary>목록 프리셋의 한 줄: 시트(<see cref="Sheet"/> + 이름·폴더) 또는 애니메이션.</summary>
public sealed class ExportListPresetRow
{
    public ExportPreset? Sheet { get; set; }
    public string? FileName { get; set; }
    public string? Folder { get; set; }
    public ExportListPresetAnimation? Animation { get; set; }
}

/// <summary>목록 프리셋의 애니메이션 줄(대상은 프로젝트 폴더 기준 상대 경로, 파일 이름은 프로젝트 이름 자리 표시).</summary>
public sealed class ExportListPresetAnimation
{
    public AnimationExportFormat Format { get; set; }
    public string Path { get; set; } = "";
    public string? Tag { get; set; }
    public int Scale { get; set; } = 1;
    public int Plays { get; set; }
    public string? Background { get; set; }
    public GifTimingMode TimingMode { get; set; }
    public double Speed { get; set; } = 1;
    public int Fps { get; set; } = GifTiming.DefaultFps;
    public bool OnSave { get; set; }

    internal static ExportListPresetAnimation From(AnimationExport a, string projectName)
    {
        // 전체 경로는 파일 이름만(프로젝트 폴더에 둠)
        var path = System.IO.Path.IsPathRooted(a.Path) ? System.IO.Path.GetFileName(a.Path.TrimEnd('/', '\\')) : a.Path;
        var dir = System.IO.Path.GetDirectoryName(path) ?? "";
        var file = System.IO.Path.GetFileName(path);
        if (projectName.Length > 0 && file.StartsWith(projectName, StringComparison.Ordinal)) file = ExportListPreset.NameToken + file[projectName.Length..];
        return new ExportListPresetAnimation
        {
            Format = a.Format,
            Path = dir.Length == 0 ? file : dir.Replace('\\', '/') + "/" + file,
            Tag = a.TagName,
            Scale = a.Scale,
            Plays = a.PlayCount,
            Background = a.Background?.ToHex(),
            TimingMode = a.Timing.Mode,
            Speed = a.Timing.Speed,
            Fps = a.Timing.Fps,
            OnSave = a.OnSave,
        };
    }

    internal AnimationExport? ToExport(string projectName)
    {
        if (!Enum.IsDefined(Format) || string.IsNullOrWhiteSpace(Path)) return null;
        var dir = System.IO.Path.GetDirectoryName(Path) ?? "";
        var file = ExportListPreset.Untokenize(System.IO.Path.GetFileName(Path), projectName)!;
        return new AnimationExport
        {
            Format = Format,
            Path = dir.Length == 0 ? file : System.IO.Path.Combine(dir, file),
            TagName = Tag,
            Scale = Scale,
            PlayCount = Math.Max(0, Plays),
            Background = Background is { } bg && Color32.TryParseHex(bg, out var c) ? c : null,
            Timing = new GifTiming
            {
                Mode = Enum.IsDefined(TimingMode) ? TimingMode : GifTimingMode.Original,
                Speed = Speed > 0 ? Speed : 1,
                Fps = Fps > 0 ? Fps : GifTiming.DefaultFps,
            },
            OnSave = OnSave,
        };
    }
}

/// <summary>내보내기 목록 프리셋 파일: 앱 설정 폴더의 <c>export-list-presets.json</c>(<c>{"version":1,"presets":[…]}</c>).</summary>
public static class ExportListPresets
{
    public const int FileVersion = 1;
    public const string FileName = "export-list-presets.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed class FileDto
    {
        public int Version { get; set; } = FileVersion;
        public List<ExportListPreset>? Presets { get; set; }
    }

    /// <summary>파일에서 프리셋을 읽는다(이름 없는 것은 건너뜀, 이름이 겹치면 뒤의 것). 없으면 빈 목록, 형식이 틀리면 <see cref="InvalidDataException"/>.</summary>
    public static List<ExportListPreset> Load(string path)
    {
        if (!File.Exists(path)) return [];
        FileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Chickxel.Core.Localization.DomainStrings.FormatPreset_ReadFailed(error: ex.Message), ex);
        }
        if (dto is null) return [];
        if (dto.Version > FileVersion) throw new InvalidDataException(Chickxel.Core.Localization.DomainStrings.FormatPreset_NewerVersion(version: dto.Version));
        var list = new List<ExportListPreset>();
        foreach (var p in dto.Presets ?? [])
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            p.Name = p.Name.Trim();
            p.Sheet ??= new ExportPreset();
            p.Sheet.Sheet ??= new PresetSheet();
            p.Rows ??= [];
            foreach (var r in p.Rows)
                if (r.Sheet is { } s) s.Sheet ??= new PresetSheet();
            list.RemoveAll(x => x.Name == p.Name);
            list.Add(p);
        }
        return list;
    }

    /// <summary>프리셋을 파일에 쓴다(임시 파일에 쓴 뒤 바꿔치기).</summary>
    public static void Save(string path, IEnumerable<ExportListPreset> presets)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        AtomicFile.WriteText(path, JsonSerializer.Serialize(new FileDto { Presets = presets.ToList() }, Options));
    }
}
