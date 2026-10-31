using System.Text.Json.Serialization;
using Chickxel.Core.Model;

namespace Chickxel.Core.References;

/// <summary>참고 자료 종류(AI 검색 거르기·목록 거르기). 색인 파일에는 소문자 이름으로 적는다.</summary>
public static class ReferenceKinds
{
    public const string Character = "character", Item = "item", Tile = "tile", Effect = "effect", Ui = "ui", Other = "other";

    public static IReadOnlyList<string> All { get; } = [Character, Item, Tile, Effect, Ui, Other];

    /// <summary>알려진 종류면 소문자 이름, 아니면 null.</summary>
    public static string? Parse(string? text)
    {
        var s = (text ?? "").Trim().ToLowerInvariant();
        return All.Contains(s) ? s : null;
    }
}

/// <summary>설명을 누가 붙였는지(<see cref="ReferenceItem.CaptionedBy"/>).</summary>
public static class CaptionSources
{
    public const string None = "none", User = "user", Ai = "ai";

    public static string Normalize(string? text) => text is User or Ai ? text : None;
}

/// <summary>참고 자료 한 항목의 형식.</summary>
public static class ReferenceTypes
{
    /// <summary>그림(.png·.gif·.chickxel, 기본 예시).</summary>
    public const string Image = "image";
    /// <summary>글 메모(.md·.txt) — 그림 규칙·기법 메모.</summary>
    public const string Note = "note";
    /// <summary>그림 규칙(기본 규칙·사용자 rules.md). 목록에는 보이지 않고 검색·get_style_rules로만 쓴다.</summary>
    public const string Rules = "rules";
}

/// <summary>
/// 참고 자료 하나(<c>index.json</c>에 담는 모양). <see cref="Id"/> = 자료 폴더 기준 상대 경로('/' 구분, 기본 자료는 <c>builtin/…</c>),
/// <see cref="Hash"/> = 파일 내용 SHA-256 앞 16자(바뀌었는지 보는 데 씀). 그림 정보(크기·프레임·색·실루엣)는 파일에서 저절로 채우고,
/// 제목·설명·태그·종류는 사용자나 AI가 고친다.
/// </summary>
public sealed class ReferenceItem
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = ReferenceTypes.Image;
    public string Hash { get; set; } = "";
    /// <summary>마지막으로 읽은 파일 크기·수정 시각(같으면 내용을 다시 읽지 않음).</summary>
    public long FileSize { get; set; }
    public DateTime FileTimeUtc { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public string Kind { get; set; } = ReferenceKinds.Other;
    public int Width { get; set; }
    public int Height { get; set; }
    public int Frames { get; set; }
    /// <summary>서로 다른 색 수(투명 빼고, 상한 없음).</summary>
    public int Colors { get; set; }
    /// <summary>많이 쓴 색부터 "#RRGGBB"(반투명은 "#RRGGBBAA"), 많아야 <see cref="ReferenceAnalysis.PaletteCap"/>개.</summary>
    public List<string> Palette { get; set; } = new();
    /// <summary>실루엣: 불투명 영역(첫 프레임)을 16×16 칸으로 줄인 비트(64자 16진수, 왼쪽 위부터 줄마다).</summary>
    public string Silhouette { get; set; } = "";
    /// <summary>불투명 영역 가로/세로 비.</summary>
    public double Aspect { get; set; } = 1;
    public DateTime AddedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    /// <summary>설명을 붙인 쪽: <see cref="CaptionSources"/>.</summary>
    public string CaptionedBy { get; set; } = CaptionSources.None;

    /// <summary>앱에 들어 있는 읽기 전용 자료(기본 예시·기본 규칙). 색인에 쓰지 않는다.</summary>
    [JsonIgnore]
    public bool BuiltIn { get; set; }

    /// <summary>기본 예시의 검색용 영어 낱말(보이지 않음).</summary>
    [JsonIgnore]
    public string Keywords { get; set; } = "";

    [JsonIgnore]
    public bool IsImage => Type == ReferenceTypes.Image;

    /// <summary>설명이 아직 없는 그림(AI로 설명 붙이기 대상).</summary>
    [JsonIgnore]
    public bool NeedsCaption => IsImage && !BuiltIn && CaptionedBy == CaptionSources.None && Description.Trim().Length == 0;

    public ReferenceItem Clone()
    {
        var copy = (ReferenceItem)MemberwiseClone();
        copy.Tags = [.. Tags];
        copy.Palette = [.. Palette];
        return copy;
    }

    /// <summary>파일에서 읽은 그림 정보를 넣는다(제목·설명·태그·종류는 그대로).</summary>
    internal void ApplyStats(ReferenceStats s)
    {
        Width = s.Width;
        Height = s.Height;
        Frames = s.Frames;
        Colors = s.Colors;
        Palette = [.. s.Palette.Select(c => c.ToHex())];
        Silhouette = s.Silhouette.ToHex();
        Aspect = s.Silhouette.Aspect;
    }

    /// <summary>색인의 팔레트 글자를 색으로(읽을 수 없는 값은 건너뜀).</summary>
    public List<Color32> PaletteColors()
    {
        var list = new List<Color32>(Palette.Count);
        foreach (var hex in Palette)
            if (Color32.TryParseHex(hex, out var c)) list.Add(c);
        return list;
    }
}

/// <summary>참고 그림의 프레임과 프레임 길이(ms).</summary>
public sealed record ReferenceFrames(IReadOnlyList<PixelImage> Images, IReadOnlyList<int> DurationsMs)
{
    public static ReferenceFrames Single(PixelImage image) => new([image], [Frame.DefaultDurationMs]);
}
