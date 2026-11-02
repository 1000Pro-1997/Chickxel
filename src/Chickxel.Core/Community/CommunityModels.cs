using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Chickxel.Core.Assets;

namespace Chickxel.Core.Community;

/// <summary>
/// 커뮤니티 계정 하나(<c>User</c> JSON): ID, 핸들(에셋 패키지 작성자 ID와 같음), 표시 이름, 프로필 그림 주소(없으면 null),
/// 만든 때(ISO 8601), 관리자인지·핸들이 잠겼는지(에셋 패키지를 올린 뒤 — 서버가 핸들 바꾸기를 거절함)(둘 다 내 정보일 때만 옴).
/// </summary>
public sealed record CommunityUser(string Id, string Handle, string Name, string? AvatarUrl, string CreatedAt, bool IsAdmin, bool HandleLocked = false)
{
    internal static CommunityUser Read(JsonNode? node)
    {
        if (node is not JsonObject o) throw CommunityJson.Bad("user");
        return new CommunityUser(
            CommunityJson.Required(o, "id"), CommunityJson.Required(o, "handle"), CommunityJson.Required(o, "name"),
            CommunityJson.String(o["avatarUrl"]), CommunityJson.String(o["createdAt"]) ?? "",
            Flag(o["isAdmin"]), Flag(o["handleLocked"]));
    }

    private static bool Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue(out bool on) && on;

    /// <summary><see cref="Read"/>가 다시 읽는 모양(저장한 계정 목록에 씀).</summary>
    internal JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["id"] = Id, ["handle"] = Handle, ["name"] = Name, ["createdAt"] = CreatedAt, ["isAdmin"] = IsAdmin, ["handleLocked"] = HandleLocked,
        };
        if (AvatarUrl is not null) o["avatarUrl"] = AvatarUrl;
        return o;
    }
}

/// <summary>올린 그림 하나(<c>Art</c> JSON 중 앱이 쓰는 것): ID, 제목, 그림 주소(사이트 기준 경로), 크기, 프레임 수, 형식(png·gif).</summary>
public sealed record CommunityArt(string Id, string Title, string ImageUrl, int Width, int Height, int Frames, string Type)
{
    internal static CommunityArt Read(JsonNode? node)
    {
        if (node is not JsonObject o) throw CommunityJson.Bad("art");
        var image = o["image"] as JsonObject;
        return new CommunityArt(CommunityJson.Required(o, "id"), CommunityJson.String(o["title"]) ?? "",
            CommunityJson.String(image?["url"]) ?? "", CommunityJson.Int(image?["width"]), CommunityJson.Int(image?["height"]),
            CommunityJson.Int(image?["frames"]), CommunityJson.String(image?["type"]) ?? "");
    }
}

/// <summary>올린 에셋 패키지(<c>AssetDetail</c> JSON 중 앱이 쓰는 것): 패키지 ID, 버전, 이름, 카탈로그 검토 값.</summary>
public sealed record CommunityPackage(string Id, string Version, string Name, string Review)
{
    internal static CommunityPackage Read(JsonNode? node)
    {
        if (node is not JsonObject o) throw CommunityJson.Bad("asset");
        return new CommunityPackage(CommunityJson.Required(o, "id"), CommunityJson.String(o["version"]) ?? "",
            CommunityJson.String(o["name"]) ?? "", CommunityJson.String(o["review"]) ?? "");
    }
}

/// <summary>갤러리에 올릴 그림: 파일 바이트(PNG나 GIF), 형식, 제목, 설명, 태그.</summary>
public sealed record ArtUpload(byte[] Data, CommunityArtType Type, string Title, string Description, IReadOnlyList<string> Tags);

public enum CommunityArtType
{
    Png,
    Gif,
}

/// <summary>서버와 같은 입력 규칙(올리기 전에 앱이 먼저 본다).</summary>
public static partial class CommunityRules
{
    public const int MinHandleLength = 3, MaxHandleLength = 32, MaxNameLength = 100;
    public const int MaxTitleLength = 100, MaxDescriptionLength = 2000, MaxTags = 8;
    public const int MaxArtBytes = 4 << 20, MaxArtSide = 2048, MaxArtFrames = 1000;
    public const int MaxPackageBytes = 8 << 20;

    /// <summary>로그인 제공자 값(<c>/login?provider=</c>, <c>/api/auth/{provider}/start</c>).</summary>
    public static readonly IReadOnlyList<string> Providers = ["google", "github", "discord"];

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex HandlePattern();

    public static bool IsValidHandle(string? handle)
        => handle is { Length: >= MinHandleLength and <= MaxHandleLength } && HandlePattern().IsMatch(handle);

    /// <summary>표시 이름: 앞뒤 공백을 뺀 1~100자, 제어 문자 없음.</summary>
    public static bool IsValidName(string? name)
        => name is not null && name.Trim().Length is > 0 and <= MaxNameLength && !name.Any(char.IsControl);

    public static bool IsValidTitle(string? title)
        => title is not null && title.Trim().Length is > 0 and <= MaxTitleLength && !title.Any(char.IsControl);

    /// <summary>설명: 2000자 이하(줄바꿈은 됨, 그 밖의 제어 문자는 안 됨).</summary>
    public static bool IsValidDescription(string? description)
        => description is null || (description.Length <= MaxDescriptionLength && !description.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')));

    /// <summary>태그: 8개 이하, 각각 소문자·숫자·하이픈 32자 이하, 중복 없음(에셋 목록 태그와 같은 규칙).</summary>
    public static bool IsValidTags(IReadOnlyList<string> tags) => AssetPublishCheck.IsValidTags(tags);
}

internal static class CommunityJson
{
    public static string? String(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    public static int Int(JsonNode? node) => node is JsonValue value && value.TryGetValue(out double number) && number is >= 0 and <= int.MaxValue ? (int)number : 0;

    public static string Required(JsonObject o, string key) => String(o[key]) is { Length: > 0 } text ? text : throw Bad(key);

    public static CommunityException Bad(string field) => new(CommunityErrorCode.BadResponse, serverMessage: field);
}
