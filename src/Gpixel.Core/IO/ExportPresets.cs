using System.Text.Json;
using System.Text.Json.Serialization;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>
/// 내보내기 설정 프리셋 하나: 프로젝트에 매이지 않는 내보내기 설정(내보낼 결과, 시트 설정 — 스킨 방식·담기·배치·정렬·줄바꿈·칸 수·따로 파일·칸 이름·간격·
/// 잘라내기·2의 거듭제곱 —, 시트별 다르게, Sprite Library, 파일 형식·JPG 품질, .meta 방식, 저장할 때 내보내기).
/// 프로젝트마다 다른 값(파일 이름·부위 아틀라스 이름·폴더, 고른 스킨·착장·뺀 레이어 목록, Single 프레임 번호, 원래 위치 유지)은 담지 않고,
/// 적용할 때 프로젝트의 지금 값을 그대로 둔다.
/// </summary>
public sealed class ExportPreset
{
    public string Name { get; set; } = "";

    /// <summary>기본 제공 프리셋(이름 바꾸기·지우기·덮어쓰기 불가).</summary>
    [JsonIgnore]
    public bool BuiltIn { get; init; }

    public bool ExportOnSave { get; set; } = true;
    public bool ExportFrames { get; set; } = true;
    public bool ExportParts { get; set; }
    public ExportImageFormat ImageFormat { get; set; } = ExportImageFormat.Png;
    public int JpegQuality { get; set; } = 90;
    public bool? WriteMeta { get; set; }
    public bool WriteSpriteLibrary { get; set; }
    public PresetSheet Sheet { get; set; } = new();
    /// <summary>부위 아틀라스만 다른 시트 설정(없으면 공통).</summary>
    public PresetSheet? PartsSheet { get; set; }

    /// <summary>프로젝트 내보내기 설정에서 프리셋을 만든다(프로젝트 의존 값은 빼고).</summary>
    public static ExportPreset Capture(ExportSettings x, string name) => new()
    {
        Name = name,
        ExportOnSave = x.ExportOnSave,
        ExportFrames = x.ExportFrames,
        ExportParts = x.ExportParts,
        ImageFormat = x.ImageFormat,
        JpegQuality = x.JpegQuality,
        WriteMeta = x.WriteMeta,
        WriteSpriteLibrary = x.WriteSpriteLibrary,
        Sheet = PresetSheet.From(x.Common),
        PartsSheet = x.PartsOverride is { } po ? PresetSheet.From(po) : null,
    };

    /// <summary>
    /// 프로젝트 내보내기 설정에 적용한다(실행취소는 부르는 쪽이 <see cref="SpriteProject.Edit(string, ProjectChange, Action)"/>로 묶음).
    /// 프로젝트 의존 값(이름·폴더·고른 스킨·착장·뺀 레이어·Single 프레임·원래 위치 유지)은 그대로 — "시트별 다르게"를 새로 켜면 부위 아틀라스 쪽 목록은 공통 목록을 복사한다.
    /// </summary>
    public void ApplyTo(ExportSettings x)
    {
        x.ExportOnSave = ExportOnSave;
        x.ExportFrames = ExportFrames;
        x.ExportParts = ExportParts;
        x.ImageFormat = ImageFormat;
        x.JpegQuality = JpegQuality;
        x.WriteMeta = WriteMeta;
        x.WriteSpriteLibrary = WriteSpriteLibrary;
        Sheet.ApplyTo(x.Common);
        if (PartsSheet is null) x.PartsOverride = null;
        else
        {
            var o = x.PartsOverride ?? x.Common.Clone();
            PartsSheet.ApplyTo(o);
            x.PartsOverride = o;
        }
    }

    public ExportPreset Clone(string? name = null) => new()
    {
        Name = name ?? Name, ExportOnSave = ExportOnSave, ExportFrames = ExportFrames, ExportParts = ExportParts,
        ImageFormat = ImageFormat, JpegQuality = JpegQuality, WriteMeta = WriteMeta, WriteSpriteLibrary = WriteSpriteLibrary,
        Sheet = Sheet.Clone(), PartsSheet = PartsSheet?.Clone(),
    };
}

/// <summary>프리셋에 담는 시트 설정(<see cref="SheetOptions"/>에서 프로젝트 의존 목록을 뺀 것).</summary>
public sealed class PresetSheet
{
    public SheetLayout Layout { get; set; } = SheetLayout.Grid;
    public List<GridKey> Order { get; set; } = [GridKey.Part, GridKey.Skin, GridKey.Frame];
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
    public bool BakeInheritedSkins { get; set; }

    public static PresetSheet From(SheetOptions o) => new()
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
        BakeInheritedSkins = o.BakeInheritedSkins,
    };

    /// <summary>시트 설정에 적용한다(고른 스킨·착장·뺀 레이어 목록은 그대로).</summary>
    public void ApplyTo(SheetOptions o)
    {
        o.Layout = Enum.IsDefined(Layout) ? Layout : SheetLayout.Grid;
        o.Order = new GridOrder(Order.Where(Enum.IsDefined).ToList(), RowBreak);
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
        o.BakeInheritedSkins = BakeInheritedSkins;
    }

    public PresetSheet Clone()
    {
        var c = (PresetSheet)MemberwiseClone();
        c.Order = [.. Order];
        return c;
    }
}

/// <summary>
/// 내보내기 설정 프리셋 목록: 기본 제공 프리셋 + 사용자 프리셋. 사용자 프리셋은 앱 설정 폴더의 <c>export-presets.json</c>
/// (<c>{"version":1,"presets":[…]}</c>)에 저장하고, 같은 형식으로 JSON 내보내기/가져오기를 한다(가져오면 같은 이름은 덮어씀).
/// </summary>
public static class ExportPresets
{
    public const int FileVersion = 1;
    public const string FileName = "export-presets.json";

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
        public List<ExportPreset>? Presets { get; set; }
    }

    /// <summary>기본 제공 프리셋: 프레임 시트 기본 / 부위 아틀라스 + Sprite Library / 스킨마다 따로 파일.</summary>
    public static IReadOnlyList<ExportPreset> BuiltIns { get; } =
    [
        new ExportPreset { Name = "프레임 시트 기본", BuiltIn = true },
        new ExportPreset
        {
            Name = "부위 아틀라스 + Sprite Library", BuiltIn = true, ExportFrames = false, ExportParts = true, WriteSpriteLibrary = true,
            Sheet = new PresetSheet { Layout = SheetLayout.Packed, Skins = SkinSource.Selected },
        },
        new ExportPreset
        {
            Name = "스킨마다 따로 파일", BuiltIn = true, ExportParts = true,
            Sheet = new PresetSheet { Skins = SkinSource.Selected, SplitSkins = true },
        },
    ];

    /// <summary>JSON 텍스트 → 프리셋(이름 없는 것은 건너뜀, 이름이 겹치면 뒤의 것). 형식이 틀리면 <see cref="InvalidDataException"/>.</summary>
    public static List<ExportPreset> Parse(string json)
    {
        FileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"내보내기 설정 프리셋 파일을 읽지 못했습니다: {ex.Message}", ex);
        }
        if (dto is null) return [];
        if (dto.Version > FileVersion) throw new InvalidDataException($"더 새로운 Gpixel의 프리셋 파일입니다(형식 버전 {dto.Version}).");
        var list = new List<ExportPreset>();
        foreach (var p in dto.Presets ?? [])
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            p.Name = p.Name.Trim();
            p.Sheet ??= new PresetSheet();
            list.RemoveAll(x => x.Name == p.Name);
            list.Add(p);
        }
        return list;
    }

    /// <summary>프리셋 → JSON 텍스트(기본 제공 프리셋은 빼고).</summary>
    public static string Serialize(IEnumerable<ExportPreset> presets)
        => JsonSerializer.Serialize(new FileDto { Presets = presets.Where(p => !p.BuiltIn).ToList() }, Options);

    /// <summary>파일에서 사용자 프리셋을 읽는다. 없으면 빈 목록, 읽지 못하면 <see cref="InvalidDataException"/>.</summary>
    public static List<ExportPreset> Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : [];

    /// <summary>사용자 프리셋을 파일에 쓴다(임시 파일에 쓴 뒤 바꿔치기).</summary>
    public static void Save(string path, IEnumerable<ExportPreset> presets)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(presets), new System.Text.UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>가져온 프리셋을 목록에 합친다: 같은 이름은 덮어쓰고 나머지는 끝에. 기본 제공 프리셋 이름이면 " (가져옴)"을 붙인다.</summary>
    public static List<ExportPreset> Merge(IEnumerable<ExportPreset> current, IEnumerable<ExportPreset> imported)
    {
        var list = current.Where(p => !p.BuiltIn).ToList();
        foreach (var p in imported)
        {
            var name = BuiltIns.Any(b => b.Name == p.Name) ? p.Name + " (가져옴)" : p.Name;
            var copy = p.Clone(name);
            int i = list.FindIndex(x => x.Name == name);
            if (i >= 0) list[i] = copy;
            else list.Add(copy);
        }
        return list;
    }
}
