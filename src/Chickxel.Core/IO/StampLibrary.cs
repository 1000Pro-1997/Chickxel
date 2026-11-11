using System.Text.Json;
using System.Text.Json.Serialization;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>모양 펜 세트: 이름, 모양 고르는 순서, 모양들(알파 포함 픽셀 그대로).</summary>
public sealed class StampSet
{
    /// <summary>바뀌지 않는 이름표(GUID — 예전 판의 기본 세트에서 남은 사용자 세트는 <see cref="StampBuiltIns.RetiredIds"/>, 받은 세트는 에셋 ID).</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    /// <summary>세트 이름(null = 이름 없음 — 이름표를 보임).</summary>
    public string? Name { get; set; }
    public StampOrder Order { get; set; } = StampOrder.Random;
    /// <summary>모양들(투명하지 않은 픽셀을 꼭 맞게 감싼 그림, 비어 있을 수 있음).</summary>
    public List<PixelImage> Shapes { get; } = [];

    /// <summary>보이는 이름: 붙인 이름, 없으면 이름표.</summary>
    public string DisplayName => Name ?? Id;
}

/// <summary>
/// 모양 펜 세트 모음(<c>&lt;설정 폴더&gt;/ShapePen/shape-sets.json</c>) — 내 세트. 기본 세트는 받은 세트로 설치하는 공식 패키지라 여기 없다(<see cref="Assets.OfficialShapeSets"/>).
/// 파일 하나에 세트마다 이름·순서와 모양 PNG(base64)를 담아 한 번에 바꿔치기로 쓴다. 파일이 없으면 빈 모음, 읽을 수 없으면 그 파일을 <c>.bad</c>로 옮겨 두고 빈 모음으로 시작한다(덮어써 잃지 않게).
/// </summary>
public sealed class StampLibrary
{
    public const int FileVersion = 1;
    public const string FileName = "shape-sets.json";
    /// <summary>모양 한 변의 최대 크기(px) — 선택 영역·PNG에서 더할 때 이보다 크면 받지 않는다.</summary>
    public const int MaxShapeSize = 256;
    /// <summary>세트 하나를 담는 파일(내보내기·가져오기)의 확장자.</summary>
    public const string SetFileExtension = ".chickxel-shapes";
    /// <summary>세트 파일 머리의 형식 표시(다른 JSON을 세트로 잘못 읽지 않게). 상점 에셋 파일(<see cref="ShapeSetAssetFormat"/>)도 같다.</summary>
    internal const string SetFileFormat = "chickxel-shapes";

    public List<StampSet> Sets { get; } = [];

    public StampSet? Find(string? id) => id is null ? null : Sets.Find(s => s.Id == id);

    /// <summary>
    /// 보이는 이름이 겹치지 않는 이름: <paramref name="name"/>이 이미 있으면 " (2)", " (3)" …를 붙인다. <paramref name="except"/>(이름을 붙일 세트 자신)의 이름은 셈하지 않는다.
    /// </summary>
    public string UniqueName(string name, StampSet? except = null)
    {
        var taken = Sets.Where(s => s != except).Select(s => s.DisplayName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        int n = 2;
        while (taken.Contains($"{name} ({n})")) n++;
        return $"{name} ({n})";
    }

    /// <summary>가져온 세트를 끝에 더한다: 이름이 없으면 <paramref name="fallbackName"/>, 겹치면 <see cref="UniqueName"/>(덮어쓰지 않음).</summary>
    public void AddImported(StampSet set, string fallbackName)
    {
        set.Name = UniqueName(set.Name ?? fallbackName);
        Sets.Add(set);
    }

    /// <summary>그림을 투명하지 않은 픽셀에 꼭 맞게 자른 모양. 모두 투명이면 null.</summary>
    public static PixelImage? Trim(PixelImage image)
    {
        var bounds = image.GetOpaqueBounds();
        return bounds.IsEmpty ? null : image.Crop(bounds);
    }

    /// <summary>모양으로 쓰기에 너무 큰지(한 변 &gt; <see cref="MaxShapeSize"/>).</summary>
    public static bool TooBig(PixelImage shape) => shape.Width > MaxShapeSize || shape.Height > MaxShapeSize;

    // ───────────── 파일 ─────────────

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed class FileDto
    {
        public int Version { get; set; } = FileVersion;
        public List<SetDto>? Sets { get; set; }
    }

    private sealed class SetDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StampOrder Order { get; set; }
        /// <summary>모양마다 RGBA PNG(base64).</summary>
        public List<string>? Shapes { get; set; }
    }

    /// <summary>세트 파일: 형식 표시·판 + 세트 하나(이름표 없음 — 가져오면 새 세트).</summary>
    private sealed class SetFileDto
    {
        public string? Format { get; set; }
        public int Version { get; set; } = FileVersion;
        public SetDto? Set { get; set; }
    }

    private static SetDto ToDto(StampSet set, string? id, string? name) => new()
    {
        Id = id,
        Name = name,
        Order = set.Order,
        Shapes = set.Shapes.Select(EncodePng).ToList(),
    };

    /// <summary>DTO → 세트(이름표 <paramref name="id"/>). 모양 PNG를 풀 수 없으면 예외(부르는 쪽이 통째로 거부).</summary>
    private static StampSet FromDto(SetDto dto, string id)
    {
        var set = new StampSet
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name.Trim(),
            Order = Enum.IsDefined(dto.Order) ? dto.Order : StampOrder.Random,
        };
        foreach (var png in dto.Shapes ?? [])
        {
            using var stream = new MemoryStream(Convert.FromBase64String(png));
            set.Shapes.Add(PngIO.Load(stream));
        }
        return set;
    }

    public string Serialize() => JsonSerializer.Serialize(new FileDto
    {
        Sets = Sets.Select(s => ToDto(s, s.Id, s.Name)).ToList(),
    }, Options);

    /// <summary>
    /// JSON을 읽는다. 형식이 틀리거나 모양 PNG를 풀 수 없으면 null(일부만 살려 저장하다 잃지 않게 통째로 거부).
    /// 예전 판의 기본 세트(<see cref="StampBuiltIns.RetiredIds"/>)는 고치지 않은 공식 세트 사본이면 빼고(받은 세트로 설치됨 — <see cref="Assets.OfficialShapeSets.IsUnchangedCopy"/>),
    /// 이름이 없으면 그때 이름(지금 언어)을 붙여 사용자 세트로 남긴다.
    /// </summary>
    public static StampLibrary? Parse(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize<FileDto>(json, Options) is not { Sets: { } sets }) return null;
            var library = new StampLibrary();
            foreach (var dto in sets)
            {
                if (dto is null || string.IsNullOrWhiteSpace(dto.Id) || library.Find(dto.Id) is not null) continue;
                var set = FromDto(dto, dto.Id);
                if (!Assets.OfficialShapeSets.IsUnchangedCopy(set)) library.Sets.Add(set);
            }
            foreach (var set in library.Sets)
                if (set.Name is null && StampBuiltIns.RetiredName(set.Id) is { } retired) set.Name = library.UniqueName(retired, except: set);
            return library;
        }
        catch (Exception ex) when (IsBadContent(ex))
        {
            return null;
        }
    }

    private static bool IsBadContent(Exception ex)
        => ex is JsonException or FormatException or InvalidDataException or ArgumentException or NotSupportedException;

    /// <summary>세트 하나를 세트 파일 글로(내보내기): 이름은 보이는 이름(기본 세트도 지금 언어 이름), 이름표는 넣지 않는다.</summary>
    public static string SerializeSet(StampSet set)
        => JsonSerializer.Serialize(new SetFileDto { Format = SetFileFormat, Set = ToDto(set, null, set.DisplayName) }, Options);

    /// <summary>세트 파일 글을 읽는다 — 새 이름표의 새 세트(이름이 없으면 null). 형식 표시가 없거나 깨졌거나 모양 PNG를 풀 수 없으면 null.</summary>
    public static StampSet? ParseSet(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SetFileDto>(json, Options) is { Format: SetFileFormat, Set: { } dto }
                ? FromDto(dto, Guid.NewGuid().ToString("N"))
                : null;
        }
        catch (Exception ex) when (IsBadContent(ex))
        {
            return null;
        }
    }

    /// <summary>세트 하나를 파일로 쓴다(<see cref="SerializeSet"/>, 한 번에 바꿔치기).</summary>
    public static void SaveSet(StampSet set, string path) => AtomicFile.WriteText(path, SerializeSet(set));

    /// <summary>파일에서 읽는다(<see cref="StampLibrary"/> 설명대로: 없으면 빈 모음, 깨졌으면 <c>.bad</c>로 옮기고 빈 모음).</summary>
    public static StampLibrary Load(string path)
    {
        if (!File.Exists(path)) return new StampLibrary();
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new StampLibrary(); }
        if (Parse(json) is { } library) return library;
        try { File.Move(path, path + ".bad", overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new StampLibrary();
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        AtomicFile.WriteText(path, Serialize());
    }

    private static string EncodePng(PixelImage image)
    {
        using var stream = new MemoryStream();
        PngIO.Save(image, stream);
        return Convert.ToBase64String(stream.ToArray());
    }
}
