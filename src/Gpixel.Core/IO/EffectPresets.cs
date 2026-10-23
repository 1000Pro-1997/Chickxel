using System.Text.Json;
using System.Text.Json.Serialization;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>여러 레이어 효과와 클리핑 관계를 함께 저장해 다시 넣는 사용자 정의 효과.</summary>
public sealed class EffectPreset
{
    public string Name { get; init; } = "";
    public IReadOnlyList<LayerEffect> Effects { get; init; } = [];

    /// <summary>프로젝트 프레임 번호에 묶이지 않은 프리셋 사본.</summary>
    public static EffectPreset Capture(string name, IEnumerable<LayerEffect> effects)
    {
        var captured = effects.Select((effect, index) => effect with
        {
            Frames = null,
            ClipToPrevious = index > 0 && effect.ClipToPrevious,
        }).ToArray();
        return new EffectPreset { Name = name.Trim(), Effects = captured };
    }

    /// <summary>지정한 프레임 범위에 넣을 독립 효과 목록.</summary>
    public IReadOnlyList<LayerEffect> Instantiate(FrameRange? frames)
        => Effects.Select((effect, index) => effect with
        {
            Frames = frames,
            ClipToPrevious = index > 0 && effect.ClipToPrevious,
        }).ToArray();
}

/// <summary>
/// 사용자 정의 효과 JSON. 앱 설정 폴더의 <c>effect-presets.json</c>에 저장하며,
/// 프로젝트 효과와 같은 직렬화 형식을 써 모든 기본 효과 설정을 보존한다.
/// </summary>
public static class EffectPresets
{
    public const int FileVersion = 1;
    public const string FileName = "effect-presets.json";

    private static readonly JsonSerializerOptions Options = new(ProjectIO.Options)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class FileDto
    {
        public int Version { get; set; } = FileVersion;
        public List<PresetDto>? Presets { get; set; }
    }

    private sealed class PresetDto
    {
        public string? Name { get; set; }
        public List<ProjectIO.EffectDto?>? Effects { get; set; }
    }

    public static string Serialize(IEnumerable<EffectPreset> presets)
        => JsonSerializer.Serialize(new FileDto
        {
            Presets = presets.Where(p => !string.IsNullOrWhiteSpace(p.Name) && p.Effects.Count > 0)
                .Select(p => new PresetDto
                {
                    Name = p.Name.Trim(),
                    Effects = p.Effects.Select(effect => (ProjectIO.EffectDto?)ProjectIO.EffectDto.From(effect)).ToList(),
                }).ToList(),
        }, Options);

    public static List<EffectPreset> Parse(string json)
    {
        FileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
        if (dto is null) return [];
        if (dto.Version > FileVersion) throw new InvalidDataException($"Unsupported effect preset version: {dto.Version}");

        var result = new List<EffectPreset>();
        foreach (var saved in dto.Presets ?? [])
        {
            string name = saved.Name?.Trim() ?? "";
            if (name.Length == 0) continue;
            var effects = (saved.Effects ?? []).Where(effect => effect is not null)
                .Select(effect => effect!.ToEffect()).Where(effect => effect is not null).Cast<LayerEffect>().ToArray();
            if (effects.Length == 0) continue;
            var preset = EffectPreset.Capture(name, effects);
            int duplicate = result.FindIndex(item => item.Name == name);
            if (duplicate >= 0) result[duplicate] = preset;
            else result.Add(preset);
        }
        return result;
    }

    public static List<EffectPreset> Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : [];

    public static void Save(string path, IEnumerable<EffectPreset> presets)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, Serialize(presets), new System.Text.UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
