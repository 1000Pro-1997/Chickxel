using System.Text.Json;
using System.Text.Json.Serialization;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scene3D;

/// <summary>3D 모델 가져오기 설정 묶음(그리기 + 프레임). 애니메이션 번호는 모델마다 달라 담지 않는다(불러오면 첫 애니메이션).</summary>
public sealed record ModelPreset
{
    public string Name { get; init; } = "";
    /// <summary>기본 제공 프리셋(이름 바꾸기·덮어쓰기·삭제 불가, 파일에 쓰지 않음).</summary>
    [JsonIgnore]
    public bool BuiltIn { get; init; }
    public ModelRenderSettings Render { get; init; } = new();
    public ModelFrameOptions Frames { get; init; } = new();
}

/// <summary>
/// 3D 모델 가져오기 프리셋: 기본 제공(탑다운·바로 위·아이소메트릭·옆모습·아이콘·캐릭터·크게 그려 픽셀로 정리) + 사용자 프리셋.
/// 사용자 프리셋은 앱 설정 폴더의 <see cref="FileName"/>(<c>{"version":1,"presets":[…]}</c>, 색은 "#RRGGBB")에 둔다.
/// </summary>
public static class ModelPresets
{
    public const int FileVersion = 1;
    public const string FileName = "model-presets.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(), new HexColorConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed class FileDto
    {
        public int Version { get; set; } = FileVersion;
        public List<ModelPreset>? Presets { get; set; }
    }

    /// <summary>Color32를 "#RRGGBB(AA)"로.</summary>
    private sealed class HexColorConverter : JsonConverter<Color32>
    {
        public override Color32 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Color32.TryParseHex(reader.GetString(), out var c) ? c : throw new JsonException("color");

        public override void Write(Utf8JsonWriter writer, Color32 value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToHex());
    }

    /// <summary>게임 스프라이트에 흔한 픽셀 그림 다듬기(또렷한 알파·명암 3단계·색 수·겉 외곽선 어둡게).</summary>
    private static ModelRenderSettings Pixel(int size, int colors) => new()
    {
        Width = size,
        Height = size,
        Supersample = 4,
        HardAlpha = true,
        ShadeSteps = 3,
        Colors = colors,
        OutlineOuter = true,
        OutlineColorMode = ModelOutlineColor.Darken,
        OutlineDarken = 0.55f,
        LightYaw = -40,
        LightPitch = 50,
        Ambient = 0.45f,
    };

    /// <summary>기본 제공 프리셋(화면 언어로 이름).</summary>
    public static IReadOnlyList<ModelPreset> BuiltIns =>
    [
        new()
        {
            Name = CoreStrings.ModelPreset_TopDown8, BuiltIn = true,
            Render = Pixel(48, 16) with { Yaw = 0, Pitch = 55 },
            Frames = new ModelFrameOptions { Mode = ModelFrameMode.Turntable, TurntableFrames = 8 },
        },
        new()
        {
            Name = CoreStrings.ModelPreset_Overhead8, BuiltIn = true,
            Render = Pixel(32, 12) with { Yaw = 0, Pitch = 90, OutlineColorMode = ModelOutlineColor.Fixed },
            Frames = new ModelFrameOptions { Mode = ModelFrameMode.Turntable, TurntableFrames = 8 },
        },
        new()
        {
            // 정사영 세로 30°면 바닥의 45° 선이 화면에서 가로 2 : 세로 1(픽셀 아이소메트릭)로 놓인다.
            Name = CoreStrings.ModelPreset_Isometric4, BuiltIn = true,
            Render = Pixel(64, 20) with { Yaw = 45, Pitch = 30 },
            Frames = new ModelFrameOptions { Mode = ModelFrameMode.Turntable, TurntableFrames = 4 },
        },
        new()
        {
            Name = CoreStrings.ModelPreset_Side, BuiltIn = true,
            Render = Pixel(64, 24) with { Yaw = 90, Pitch = 0, LightYaw = -30, LightPitch = 40, OutlineInner = true },
            Frames = new ModelFrameOptions { Mode = ModelFrameMode.Animation, Fps = 12 },
        },
        new()
        {
            Name = CoreStrings.ModelPreset_Icon32, BuiltIn = true,
            Render = Pixel(32, 12) with { Yaw = 30, Pitch = 25, Zoom = 1.15f, OutlineColorMode = ModelOutlineColor.Fixed },
            Frames = new ModelFrameOptions { Mode = ModelFrameMode.Still },
        },
        new()
        {
            Name = CoreStrings.ModelPreset_Character, BuiltIn = true,
            Render = Pixel(64, 24) with { Yaw = 25, Pitch = 12, OutlineInner = true },
            Frames = new ModelFrameOptions { Mode = ModelFrameMode.Animation, Fps = 12 },
        },
        new()
        {
            // 픽셀로 정리의 원본: 크게·부드럽게 그려 칸 맞추기·팔레트·외곽선은 정리 창에 맡긴다.
            Name = CoreStrings.ModelPreset_LargeForPixelate, BuiltIn = true,
            Render = new ModelRenderSettings { Width = 256, Height = 256, Supersample = 4 },
            Frames = new ModelFrameOptions { Mode = ModelFrameMode.Still },
        },
    ];

    /// <summary>JSON 텍스트 → 사용자 프리셋(이름 없는 것은 건너뜀, 이름이 겹치면 뒤의 것). 형식이 틀리면 <see cref="InvalidDataException"/>.</summary>
    public static List<ModelPreset> Parse(string json)
    {
        FileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(CoreStrings.FormatModelPreset_ReadFailed(error: ex.Message), ex);
        }
        if (dto is null) return [];
        if (dto.Version > FileVersion) throw new InvalidDataException(CoreStrings.FormatModelPreset_NewerVersion(version: dto.Version));
        var list = new List<ModelPreset>();
        foreach (var p in dto.Presets ?? [])
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            var preset = p with
            {
                Name = p.Name.Trim(),
                Render = p.Render ?? new ModelRenderSettings(),
                Frames = (p.Frames ?? new ModelFrameOptions()) with { Animation = 0 },
            };
            list.RemoveAll(x => x.Name == preset.Name);
            list.Add(preset);
        }
        return list;
    }

    /// <summary>사용자 프리셋 → JSON 텍스트(기본 제공은 빼고, 애니메이션 번호는 0으로).</summary>
    public static string Serialize(IEnumerable<ModelPreset> presets)
        => JsonSerializer.Serialize(new FileDto { Presets = [.. presets.Where(p => !p.BuiltIn).Select(p => p with { Frames = p.Frames with { Animation = 0 } })] }, Options);

    /// <summary>파일에서 사용자 프리셋을 읽는다. 없으면 빈 목록, 읽지 못하면 <see cref="InvalidDataException"/>.</summary>
    public static List<ModelPreset> Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : [];

    /// <summary>사용자 프리셋을 파일에 쓴다(임시 파일에 쓴 뒤 바꿔치기).</summary>
    public static void Save(string path, IEnumerable<ModelPreset> presets)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        AtomicFile.WriteText(path, Serialize(presets));
    }
}
