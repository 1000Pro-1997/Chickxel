using System.Text.Json;
using System.Text.Json.Serialization;
using Gpixel.Core.IO;

namespace Gpixel.Core.Pixelate;

/// <summary>이름 붙인 설정.</summary>
public sealed record PixelatePreset(string Name, PixelateSettings Settings);

/// <summary>
/// 예전 프리셋 파일(<c>presets.json</c> — 버전과 이름·설정 목록)과 설정 JSON 읽기·쓰기. 읽을 수 없는 파일은 빈 목록.
/// </summary>
public static class PixelatePresets
{
    public const int FileVersion = 1;
    public const string FileName = "presets.json";

    private static readonly JsonSerializerOptions Options = new(ProjectIO.Options)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };

    private sealed class FileDto
    {
        public int Version { get; set; } = FileVersion;
        public List<PixelatePreset>? Presets { get; set; }
    }

    public static string SerializeSettings(PixelateSettings s, bool indented = true)
        => JsonSerializer.Serialize(s, indented ? Options : Compact);

    /// <summary>설정 JSON을 읽는다(빠진 값은 기본값). 읽을 수 없으면 null.</summary>
    public static PixelateSettings? ParseSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return Normalize(JsonSerializer.Deserialize<PixelateSettings>(json, Options));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(IEnumerable<PixelatePreset> presets)
        => JsonSerializer.Serialize(new FileDto { Presets = presets.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList() }, Options);

    public static List<PixelatePreset> Parse(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<FileDto>(json, Options);
            return dto?.Presets?.Where(p => !string.IsNullOrWhiteSpace(p?.Name))
                .Select(p => p with { Settings = Normalize(p.Settings) ?? new() }).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static List<PixelatePreset> Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : [];

    public static void Save(string path, IEnumerable<PixelatePreset> presets)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, Serialize(presets));
    }

    /// <summary>JSON에서 빠진(null) 묶음을 기본값으로.</summary>
    private static PixelateSettings? Normalize(PixelateSettings? s) => s is null ? null : s with
    {
        Background = s.Background ?? new(),
        Frames = s.Frames ?? new(),
        Grid = s.Grid ?? new(),
        Size = s.Size ?? new(),
        Downsample = s.Downsample ?? new(),
        Palette = (s.Palette ?? new()) with
        {
            ExistingColors = s.Palette?.ExistingColors ?? [],
            ExcludedColors = s.Palette?.ExcludedColors ?? [],
            PinnedColors = s.Palette?.PinnedColors ?? [],
        },
        Cleanup = s.Cleanup ?? new(),
        Consistency = s.Consistency ?? new(),
    };
}
