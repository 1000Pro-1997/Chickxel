using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>A named snapshot of gradient colors, opacity and effect geometry.</summary>
public sealed record GradientPreset
{
    private IReadOnlyList<GradientStop> _stops = Array.AsReadOnly(new[]
    {
        new GradientStop(0, Color32.White), new GradientStop(1, Color32.White),
    });
    private IReadOnlyList<GradientAlphaStop> _alphaStops = Array.AsReadOnly(new[]
    {
        new GradientAlphaStop(0, 255), new GradientAlphaStop(1, 255),
    });

    public string Name { get; init; } = "";
    public IReadOnlyList<GradientStop> Stops
    {
        get => _stops;
        init => _stops = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
    }
    public IReadOnlyList<GradientAlphaStop> AlphaStops
    {
        get => _alphaStops;
        init => _alphaStops = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
    }
    public GradientInterpolation Interpolation { get; init; } = GradientInterpolation.Smooth;
    public GradientDither Dither { get; init; } = GradientDither.None;
    public GradientColorSpace ColorSpace { get; init; } = GradientColorSpace.OkLch;
    public int Angle { get; init; }
    public EffectGradientShape Shape { get; init; } = EffectGradientShape.Linear;
    public bool Animated { get; init; }
    public bool Reverse { get; init; }
    public int Cycles { get; init; } = 1;
    public EffectEasing Easing { get; init; } = EffectEasing.Linear;
    public bool PingPong { get; init; }
    public bool Favorite { get; init; }

    /// <summary>Copy the editable effect settings; an absent alpha curve is an opaque curve.</summary>
    public static GradientPreset Capture(string name, EffectColorGradient gradient) => new()
    {
        Name = name.Trim(), Stops = gradient.Stops,
        AlphaStops = gradient.AlphaStops.Count == 0
            ? [new GradientAlphaStop(0, 255), new GradientAlphaStop(1, 255)] : gradient.AlphaStops,
        Interpolation = gradient.Interpolation, Dither = gradient.Dither, ColorSpace = gradient.ColorSpace,
        Angle = gradient.Angle, Shape = gradient.Shape, Animated = gradient.Animated,
        Reverse = gradient.Reverse, Cycles = gradient.Cycles, Easing = gradient.Easing, PingPong = gradient.PingPong,
    };

    /// <summary>Make a separate effect value; callers applying to the paint tool use only color settings.</summary>
    public EffectColorGradient ToGradient() => new()
    {
        Stops = Stops, AlphaStops = AlphaStops, Interpolation = Interpolation, Dither = Dither,
        ColorSpace = ColorSpace, Angle = Angle, Shape = Shape, Animated = Animated,
        Reverse = Reverse, Cycles = Cycles, Easing = Easing, PingPong = PingPong,
    };
}

/// <summary>Versioned JSON catalog. The last occurrence of a name replaces the first, ignoring case.</summary>
public static class GradientPresets
{
    /// <summary>2 = <see cref="BuiltIns"/>에 Fire·Forest·Grayscale를 더한 판. 1판 파일을 읽으면 빠진 그 기본 그라데이션을 덧붙인다.</summary>
    public const int FileVersion = 2;
    public const string FileName = "gradient-presets.json";

    private static readonly JsonSerializerOptions Options = new(ProjectIO.Options)
    {
        NumberHandling = JsonNumberHandling.Strict,
    };

    private sealed class FileDto
    {
        public int Version { get; set; } = FileVersion;
        public List<PresetDto?>? Presets { get; set; }
    }

    private sealed class PresetDto
    {
        public string? Name { get; set; }
        public List<ColorStopDto?>? Stops { get; set; }
        public List<AlphaStopDto?>? AlphaStops { get; set; }
        public string? Interpolation { get; set; }
        public string? Dither { get; set; }
        public string? ColorSpace { get; set; }
        public int Angle { get; set; }
        public string? Shape { get; set; }
        public bool Animated { get; set; }
        public bool Reverse { get; set; }
        public int Cycles { get; set; } = 1;
        public string? Easing { get; set; }
        public bool PingPong { get; set; }
        public bool Favorite { get; set; }
    }

    private sealed class ColorStopDto
    {
        public double? Position { get; set; }
        public string? Color { get; set; }
    }

    private sealed class AlphaStopDto
    {
        public double? Position { get; set; }
        public int? Alpha { get; set; }
    }

    /// <summary>Serialize the entire editable catalog, with last duplicate name winning in its original slot.</summary>
    public static string Serialize(IEnumerable<GradientPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        var entries = new List<PresetDto?>();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presets)
        {
            if (preset is null) throw new InvalidDataException("Gradient preset cannot be null.");
            Validate(preset);
            string name = preset.Name.Trim();
            var dto = new PresetDto
            {
                Name = name,
                Stops = preset.Stops.Select(stop => (ColorStopDto?)new ColorStopDto
                {
                    Position = stop.Position, Color = stop.Color.ToHex(),
                }).ToList(),
                AlphaStops = preset.AlphaStops.Select(stop => (AlphaStopDto?)new AlphaStopDto
                {
                    Position = stop.Position, Alpha = stop.Alpha,
                }).ToList(),
                Interpolation = preset.Interpolation.ToString(), Dither = preset.Dither.ToString(),
                ColorSpace = preset.ColorSpace.ToString(), Angle = preset.Angle, Shape = preset.Shape.ToString(),
                Animated = preset.Animated, Reverse = preset.Reverse, Cycles = preset.Cycles,
                Easing = preset.Easing.ToString(), PingPong = preset.PingPong, Favorite = preset.Favorite,
            };
            if (indexes.TryGetValue(name, out int index)) entries[index] = dto;
            else { indexes.Add(name, entries.Count); entries.Add(dto); }
        }
        return JsonSerializer.Serialize(new FileDto { Presets = entries }, Options);
    }

    /// <summary>Reject malformed or unsupported data rather than turning a damaged catalog into an empty one.</summary>
    public static List<GradientPreset> Parse(string json) => ParseFile(json).Presets;

    private static (int Version, List<GradientPreset> Presets) ParseFile(string json)
    {
        FileDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(json, Options)
                ?? throw new InvalidDataException("Gradient preset catalog must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid gradient preset JSON.", ex);
        }
        if (dto.Version is < 1 or > FileVersion) throw new InvalidDataException($"Unsupported gradient preset version: {dto.Version}.");
        if (dto.Presets is null) throw new InvalidDataException("Gradient preset catalog is missing presets.");

        var result = new List<GradientPreset>(dto.Presets.Count);
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var saved in dto.Presets)
        {
            if (saved is null) throw new InvalidDataException("Gradient preset cannot be null.");
            if (saved.Stops is null) throw new InvalidDataException("Gradient preset is missing color stops.");
            var stops = new List<GradientStop>(saved.Stops.Count);
            foreach (var stop in saved.Stops)
            {
                if (stop?.Position is not { } position || !Color32.TryParseHex(stop.Color, out var color))
                    throw new InvalidDataException("Invalid gradient color stop.");
                stops.Add(new GradientStop(position, color));
            }
            // Older gradient settings did not store an alpha curve. Preserve their opacity.
            var alphaStops = new List<GradientAlphaStop>();
            if (saved.AlphaStops is null)
            {
                alphaStops.Add(new GradientAlphaStop(0, 255));
                alphaStops.Add(new GradientAlphaStop(1, 255));
            }
            else foreach (var stop in saved.AlphaStops)
            {
                if (stop?.Position is not { } position || stop.Alpha is not (>= 0 and <= 255))
                    throw new InvalidDataException("Invalid gradient alpha stop.");
                alphaStops.Add(new GradientAlphaStop(position, (byte)stop.Alpha.Value));
            }
            var preset = new GradientPreset
            {
                Name = saved.Name ?? "", Stops = stops, AlphaStops = alphaStops,
                Interpolation = ParseEnum(saved.Interpolation, GradientInterpolation.Smooth, "interpolation"),
                Dither = ParseEnum(saved.Dither, GradientDither.None, "dither"),
                ColorSpace = ParseEnum(saved.ColorSpace, GradientColorSpace.Rgb, "color space"),
                Angle = saved.Angle, Shape = ParseEnum(saved.Shape, EffectGradientShape.Linear, "shape"),
                Animated = saved.Animated, Reverse = saved.Reverse, Cycles = saved.Cycles,
                Easing = ParseEnum(saved.Easing, EffectEasing.Linear, "easing"),
                PingPong = saved.PingPong, Favorite = saved.Favorite,
            };
            Validate(preset);
            string name = preset.Name.Trim();
            if (indexes.TryGetValue(name, out int index)) result[index] = preset with { Name = name };
            else { indexes.Add(name, result.Count); result.Add(preset with { Name = name }); }
        }
        return (dto.Version, result);
    }

    /// <summary>
    /// An absent file uses starter gradients; an explicitly saved empty file stays empty. A version-1 file gains the starter gradients
    /// introduced in version 2 that it does not already name (they are written back on the next save, so deleting them later sticks).
    /// </summary>
    public static List<GradientPreset> Load(string path)
    {
        if (!File.Exists(path)) return BuiltIns();
        var (version, presets) = ParseFile(File.ReadAllText(path));
        if (version < 2)
            foreach (var added in BuiltIns().Where(p => AddedInVersion2.Contains(p.Name)))
                if (!presets.Any(p => string.Equals(p.Name, added.Name, StringComparison.OrdinalIgnoreCase))) presets.Add(added);
        return presets;
    }

    private static readonly HashSet<string> AddedInVersion2 = new(StringComparer.OrdinalIgnoreCase) { "Fire", "Forest", "Grayscale" };

    /// <summary>Write a complete catalog to a unique temporary file and atomically replace the target.</summary>
    public static void Save(string path, IEnumerable<GradientPreset> presets)
    {
        string json = Serialize(presets); // Validate before touching the stored catalog.
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static T ParseEnum<T>(string? value, T fallback, string field) where T : struct, Enum
    {
        if (value is null) return fallback;
        foreach (string name in Enum.GetNames<T>())
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<T>(name);
        throw new InvalidDataException($"Invalid gradient {field}: {value}.");
    }

    private static void Validate(GradientPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Any(char.IsControl))
            throw new InvalidDataException("Gradient preset name cannot be blank or contain control characters.");
        if (preset.Stops is null || preset.Stops.Count < 2 || preset.AlphaStops is null || preset.AlphaStops.Count < 2)
            throw new InvalidDataException("Gradient preset needs at least two color and two alpha stops.");
        if (preset.Stops.Any(stop => !ValidPosition(stop.Position)) ||
            preset.AlphaStops.Any(stop => !ValidPosition(stop.Position)))
            throw new InvalidDataException("Gradient stop position must be between zero and one.");
        if (!Enum.IsDefined(preset.Interpolation) || !Enum.IsDefined(preset.Dither) ||
            !Enum.IsDefined(preset.ColorSpace) || !Enum.IsDefined(preset.Shape) || !Enum.IsDefined(preset.Easing))
            throw new InvalidDataException("Gradient preset contains an unsupported setting.");
        if (preset.Angle is < 0 or >= 360 || preset.Cycles is < 1 or > 16)
            throw new InvalidDataException("Gradient preset angle or cycle count is out of range.");
    }

    private static bool ValidPosition(double value) => double.IsFinite(value) && value >= 0 && value <= 1;

    /// <summary>기본 그라데이션(코드에 고정 — 사용자가 지워도 '기본값으로 초기화'로 되돌림).</summary>
    public static List<GradientPreset> BuiltIns()
    {
        var opaque = new[] { new GradientAlphaStop(0, 255), new GradientAlphaStop(1, 255) };
        var rainbow = new[]
        {
            new GradientStop(0, new Color32(255, 0, 0)),
            new GradientStop(0.2, new Color32(255, 140, 0)),
            new GradientStop(0.4, new Color32(255, 255, 0)),
            new GradientStop(0.6, new Color32(0, 190, 50)),
            new GradientStop(0.8, new Color32(0, 80, 255)),
            new GradientStop(1, new Color32(148, 0, 211)),
        };
        return
        [
            new GradientPreset { Name = "Rainbow", Stops = rainbow, AlphaStops = opaque },
            new GradientPreset { Name = "Repeating Rainbow", Stops = [new(0, rainbow[0].Color), new(0.17, rainbow[1].Color), new(0.34, rainbow[2].Color), new(0.51, rainbow[3].Color), new(0.68, rainbow[4].Color), new(0.85, rainbow[5].Color), new(1, rainbow[0].Color)], AlphaStops = opaque },
            new GradientPreset { Name = "Sunset", Stops = [new(0, new Color32(30, 16, 77)), new(0.45, new Color32(219, 54, 97)), new(1, new Color32(255, 204, 90))], AlphaStops = opaque },
            new GradientPreset { Name = "Ocean", Stops = [new(0, new Color32(3, 31, 65)), new(0.5, new Color32(12, 120, 171)), new(1, new Color32(106, 224, 220))], AlphaStops = opaque },
            new GradientPreset { Name = "Transparent Fade", Stops = [new(0, Color32.White), new(1, Color32.White)], AlphaStops = [new(0, 255), new(1, 0)] },
            new GradientPreset { Name = "Fire", Stops = [new(0, new Color32(40, 4, 4)), new(0.35, new Color32(190, 30, 20)), new(0.7, new Color32(255, 140, 20)), new(1, new Color32(255, 240, 150))], AlphaStops = opaque },
            new GradientPreset { Name = "Forest", Stops = [new(0, new Color32(12, 36, 22)), new(0.5, new Color32(46, 120, 60)), new(1, new Color32(180, 220, 110))], AlphaStops = opaque },
            new GradientPreset { Name = "Grayscale", Stops = [new(0, Color32.Black), new(1, Color32.White)], AlphaStops = opaque, ColorSpace = GradientColorSpace.Rgb },
        ];
    }

    /// <summary>
    /// 기본 그라데이션(<see cref="BuiltIns"/>)을 처음 값으로 되돌리고 지운 것은 뒤에 다시 넣는다(이름으로 찾음, 대소문자 무시).
    /// 직접 만든 그라데이션과 즐겨찾기 표시는 그대로 둔다.
    /// </summary>
    public static void RestoreBuiltIns(List<GradientPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        foreach (var builtIn in BuiltIns())
        {
            int index = presets.FindIndex(p => string.Equals(p.Name, builtIn.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) presets.Add(builtIn);
            else presets[index] = builtIn with { Favorite = presets[index].Favorite };
        }
    }
}
