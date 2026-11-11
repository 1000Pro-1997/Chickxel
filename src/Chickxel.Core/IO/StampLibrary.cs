using System.Text.Json;
using System.Text.Json.Serialization;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>모양 펜 세트: 이름, 모양 고르는 순서, 모양들(알파 포함 픽셀 그대로).</summary>
public sealed class StampSet
{
    /// <summary>바뀌지 않는 이름표(기본 세트는 <see cref="StampBuiltIns.Ids"/>, 사용자 세트는 GUID — 예전 기본 세트에서 남은 사용자 세트는 <see cref="StampBuiltIns.RetiredIds"/>).</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    /// <summary>사용자가 붙인 이름. null = 기본 세트 이름(지금 언어로, <see cref="DisplayName"/>).</summary>
    public string? Name { get; set; }
    public StampOrder Order { get; set; } = StampOrder.Random;
    /// <summary>모양들(투명하지 않은 픽셀을 꼭 맞게 감싼 그림, 비어 있을 수 있음).</summary>
    public List<PixelImage> Shapes { get; } = [];

    /// <summary>보이는 이름: 붙인 이름, 없으면 기본 세트 이름.</summary>
    public string DisplayName => Name ?? StampBuiltIns.Name(Id) ?? Id;
}

/// <summary>
/// 모양 펜 세트 모음(<c>&lt;설정 폴더&gt;/ShapePen/shape-sets.json</c>). 파일 하나에 세트마다 이름·순서와 모양 PNG(base64)를 담아 한 번에 바꿔치기로 쓴다.
/// 파일이 없으면 기본 세트, 읽을 수 없으면 그 파일을 <c>.bad</c>로 옮겨 두고 기본 세트로 시작한다(덮어써 잃지 않게).
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

    /// <summary>기본 세트만 든 모음(첫 실행).</summary>
    public static StampLibrary Defaults()
    {
        var library = new StampLibrary();
        library.Sets.AddRange(StampBuiltIns.Sets());
        return library;
    }

    /// <summary>기본 세트를 처음 모양으로 되돌린다: 있는 것은 그 자리에서 바꾸고, 지운 것은 기본 차례 자리에 다시 넣는다. 사용자 세트(예전 기본 세트에서 남은 것도)는 그대로.</summary>
    public void RestoreDefaults()
    {
        var builtIns = StampBuiltIns.Sets();
        for (int i = 0; i < builtIns.Count; i++)
        {
            int at = Sets.FindIndex(s => s.Id == builtIns[i].Id);
            if (at >= 0) Sets[at] = builtIns[i];
            else Sets.Insert(Math.Min(i, Sets.Count), builtIns[i]);
        }
    }

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
        /// <summary>저장할 때 있던 기본 세트 이름표 — 여기 없는 기본 세트는 그 뒤에 새로 생긴 것이라 읽을 때 넣는다(있는데 세트에 없으면 사용자가 지운 것).</summary>
        public List<string>? KnownBuiltIns { get; set; }
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
        KnownBuiltIns = [.. StampBuiltIns.Ids],
        Sets = Sets.Select(s => ToDto(s, s.Id, s.Name)).ToList(),
    }, Options);

    /// <summary>
    /// JSON을 읽는다. 형식이 틀리거나 모양 PNG를 풀 수 없으면 null(일부만 살려 저장하다 잃지 않게 통째로 거부).
    /// 이름 없는 예전 기본 세트(<see cref="StampBuiltIns.RetiredIds"/>)에는 그때 이름(지금 언어)을 붙여 사용자 세트로 남긴다.
    /// </summary>
    public static StampLibrary? Parse(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize<FileDto>(json, Options) is not { Sets: { } sets } file) return null;
            var library = new StampLibrary();
            foreach (var dto in sets)
            {
                if (dto is null || string.IsNullOrWhiteSpace(dto.Id) || library.Find(dto.Id) is not null) continue;
                library.Sets.Add(FromDto(dto, dto.Id));
            }
            foreach (var set in library.Sets)
                if (set.Name is null && StampBuiltIns.RetiredName(set.Id) is { } retired) set.Name = library.UniqueName(retired, except: set);
            library.AddNewBuiltIns(file.KnownBuiltIns ?? StampBuiltIns.FirstIds);
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

    /// <summary>
    /// <paramref name="known"/>(파일을 저장한 판이 알던 기본 세트)에 없는 기본 세트를 기본 차례 자리(앞 기본 세트 바로 뒤)에 넣는다 — 새 판에서 늘어난 기본 세트가
    /// 예전 파일에도 보이게. 알던 기본 세트가 없으면 사용자가 지운 것이라 다시 넣지 않는다(되살리기는 <see cref="RestoreDefaults"/>).
    /// </summary>
    private void AddNewBuiltIns(IReadOnlyCollection<string> known)
    {
        List<StampSet>? builtIns = null;
        for (int i = 0; i < StampBuiltIns.Ids.Count; i++)
        {
            string id = StampBuiltIns.Ids[i];
            if (known.Contains(id) || Find(id) is not null) continue;
            builtIns ??= StampBuiltIns.Sets();
            int at = 0;
            for (int p = i - 1; p >= 0; p--)
                if (Sets.FindIndex(s => s.Id == StampBuiltIns.Ids[p]) is >= 0 and var previous) { at = previous + 1; break; }
            Sets.Insert(at, builtIns[i]);
        }
    }

    /// <summary>파일에서 읽는다(<see cref="StampLibrary"/> 설명대로: 없으면 기본 세트, 깨졌으면 <c>.bad</c>로 옮기고 기본 세트).</summary>
    public static StampLibrary Load(string path)
    {
        if (!File.Exists(path)) return Defaults();
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Defaults(); }
        if (Parse(json) is { } library) return library;
        try { File.Move(path, path + ".bad", overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Defaults();
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
