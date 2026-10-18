using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>
/// 편집기 사이드카 <c>&lt;이미지&gt;.png.gpixel.json</c> 읽기/쓰기(스프라이트 설정·칸·애니메이션·격자).
/// 좌표는 왼쪽 위 원점, 피벗은 Unity와 같은 아래 왼쪽 원점 0~1, 테두리 배열은 [왼쪽, 위, 오른쪽, 아래].
/// </summary>
public static class SidecarIO
{
    public const int CurrentVersion = 1;
    public const string Extension = ".gpixel.json";

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

    public static string GetPath(string pngPath) => pngPath + Extension;

    /// <summary>문서 내용을 JSON 문자열로.</summary>
    public static string Serialize(Document doc)
    {
        var s = doc.Sprite;
        var dto = new SidecarDto
        {
            Version = CurrentVersion,
            SpriteMode = s.Mode,
            PixelsPerUnit = s.PixelsPerUnit,
            DefaultPivot = PivotDto.From(s.DefaultPivot),
            Border = s.Border.IsNone ? null : BorderArray(s.Border),
            FullRect = s.FullRectMesh,
            Readable = s.Readable,
            Slices = doc.Slices.Select(sl => new SliceDto
            {
                Name = sl.Name,
                X = sl.Rect.X, Y = sl.Rect.Y, W = sl.Rect.Width, H = sl.Rect.Height,
                Pivot = PivotDto.From(sl.Pivot),
                Border = BorderArray(sl.Border),
            }).ToList(),
            Animations = doc.Animations.Select(a => new AnimationDto
            {
                Name = a.Name,
                Loop = a.Loop,
                Fps = a.Fps,
                Frames = a.Frames.Select(f => new FrameDto { Slice = f.Slice, Ms = f.Ms }).ToList(),
            }).ToList(),
            Grid = new GridDto
            {
                CellW = doc.Grid.CellW, CellH = doc.Grid.CellH,
                OffsetX = doc.Grid.OffsetX, OffsetY = doc.Grid.OffsetY,
                PadX = doc.Grid.PadX, PadY = doc.Grid.PadY,
            },
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>
    /// JSON을 읽어 문서의 스프라이트 설정·칸·애니메이션·격자를 바꾼다(이미지·수정 여부는 그대로).
    /// JSON이 잘못되면 <see cref="JsonException"/>.
    /// </summary>
    public static void Deserialize(string json, Document doc)
    {
        var dto = JsonSerializer.Deserialize<SidecarDto>(json, Options)
                  ?? throw new JsonException("사이드카 내용이 비어 있습니다.");
        var s = doc.Sprite;
        s.Mode = dto.SpriteMode;
        s.PixelsPerUnit = dto.PixelsPerUnit;
        s.DefaultPivot = dto.DefaultPivot?.ToPivot() ?? Pivot.Center;
        s.Border = ParseBorder(dto.Border);
        s.FullRectMesh = dto.FullRect;
        s.Readable = dto.Readable;

        doc.Slices.Clear();
        foreach (var sl in dto.Slices ?? [])
        {
            if (sl.W <= 0 || sl.H <= 0) continue;
            var name = string.IsNullOrWhiteSpace(sl.Name) ? doc.UniqueSliceName(doc.BaseName) : sl.Name;
            doc.Slices.Add(new Slice(name, new Rectangle(sl.X, sl.Y, sl.W, sl.H),
                sl.Pivot?.ToPivot() ?? s.DefaultPivot, ParseBorder(sl.Border)));
        }

        doc.Animations.Clear();
        foreach (var a in dto.Animations ?? [])
        {
            if (string.IsNullOrWhiteSpace(a.Name)) continue;
            var clip = new AnimationClip(a.Name) { Loop = a.Loop, Fps = a.Fps ?? AnimationClip.DefaultFps };
            foreach (var f in a.Frames ?? [])
                if (!string.IsNullOrEmpty(f.Slice)) clip.Frames.Add(new AnimationFrame(f.Slice, f.Ms));
            doc.Animations.Add(clip);
        }

        if (dto.Grid is { } g)
        {
            doc.Grid = new GridSettings
            {
                CellW = Math.Max(1, g.CellW), CellH = Math.Max(1, g.CellH),
                OffsetX = Math.Max(0, g.OffsetX), OffsetY = Math.Max(0, g.OffsetY),
                PadX = Math.Max(0, g.PadX), PadY = Math.Max(0, g.PadY),
            };
        }
    }

    /// <summary>사이드카 파일을 쓴다(임시 파일 후 바꿔치기).</summary>
    public static void Save(Document doc, string pngPath)
    {
        var path = GetPath(pngPath);
        var temp = path + ".gpixel-tmp";
        try
        {
            File.WriteAllText(temp, Serialize(doc), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>사이드카 파일이 있으면 읽어 문서에 적용하고 true. 없으면 false.</summary>
    public static bool TryLoad(string pngPath, Document doc)
    {
        var path = GetPath(pngPath);
        if (!File.Exists(path)) return false;
        Deserialize(File.ReadAllText(path), doc);
        return true;
    }

    private static int[] BorderArray(SpriteBorder b) => [b.Left, b.Top, b.Right, b.Bottom];

    private static SpriteBorder ParseBorder(int[]? a)
        => a is { Length: >= 4 } ? new SpriteBorder(a[0], a[1], a[2], a[3]) : SpriteBorder.None;

    // ───────────── JSON 형식 ─────────────

    private sealed class SidecarDto
    {
        public int Version { get; set; } = CurrentVersion;
        public SpriteMode SpriteMode { get; set; } = SpriteMode.Single;
        public float PixelsPerUnit { get; set; } = SpriteSettings.DefaultPixelsPerUnit;
        public PivotDto? DefaultPivot { get; set; }
        /// <summary>Single 모드 테두리 [왼쪽, 위, 오른쪽, 아래](없으면 생략).</summary>
        public int[]? Border { get; set; }
        public bool FullRect { get; set; } = true;
        public bool Readable { get; set; }
        public List<SliceDto>? Slices { get; set; }
        public List<AnimationDto>? Animations { get; set; }
        public GridDto? Grid { get; set; }
    }

    private sealed class PivotDto
    {
        public float X { get; set; } = 0.5f;
        public float Y { get; set; } = 0.5f;
        public static PivotDto From(Pivot p) => new() { X = p.X, Y = p.Y };
        public Pivot ToPivot() => float.IsFinite(X) && float.IsFinite(Y) ? new Pivot(X, Y) : Pivot.Center;
    }

    private sealed class SliceDto
    {
        public string Name { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public PivotDto? Pivot { get; set; }
        public int[]? Border { get; set; }
    }

    private sealed class AnimationDto
    {
        public string Name { get; set; } = "";
        public bool Loop { get; set; } = true;
        public int? Fps { get; set; }
        public List<FrameDto>? Frames { get; set; }
    }

    private sealed class FrameDto
    {
        public string Slice { get; set; } = "";
        public int Ms { get; set; } = 100;
    }

    private sealed class GridDto
    {
        public int CellW { get; set; } = 16;
        public int CellH { get; set; } = 16;
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public int PadX { get; set; }
        public int PadY { get; set; }
    }
}
