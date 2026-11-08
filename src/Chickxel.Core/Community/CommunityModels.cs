using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Chickxel.Core.Assets;

namespace Chickxel.Core.Community;

/// <summary>
/// 커뮤니티 계정 하나(<c>User</c> JSON): ID, 핸들(에셋 패키지 작성자 ID와 같음), 표시 이름, 프로필 그림 주소(없으면 null),
/// 만든 때(ISO 8601), 관리자인지·핸들이 잠겼는지(에셋 패키지를 올린 뒤 — 서버가 핸들 바꾸기를 거절함)(둘 다 내 정보일 때만 옴),
/// 공유로 쌓은 레벨(<c>level</c>, 0 = 모름 — 예전 서버)과 내 정보(<c>GET /api/me</c>)일 때만 오는 경험치·지금 레벨 시작 XP·다음 레벨 XP
/// (예전 서버면 null, <see cref="CommunityLevel"/>).
/// </summary>
public sealed record CommunityUser(string Id, string Handle, string Name, string? AvatarUrl, string CreatedAt, bool IsAdmin, bool HandleLocked = false,
    int Level = 0, int Xp = 0, int LevelStartXp = 0, int? NextLevelXp = null)
{
    /// <summary>경험치 정보가 있다(내 정보 — 레벨과 다음 레벨 XP가 옴). 레벨만 온 다른 사람 정보면 false.</summary>
    public bool HasXp => Level > 0 && NextLevelXp is not null;

    internal static CommunityUser Read(JsonNode? node)
    {
        if (node is not JsonObject o) throw CommunityJson.Bad("user");
        return new CommunityUser(
            CommunityJson.Required(o, "id"), CommunityJson.Required(o, "handle"), CommunityJson.Required(o, "name"),
            CommunityJson.String(o["avatarUrl"]), CommunityJson.String(o["createdAt"]) ?? "",
            Flag(o["isAdmin"]), Flag(o["handleLocked"]))
            .WithLevel(o);
    }

    /// <summary>
    /// JSON의 레벨 칸(<c>level</c>·<c>xp</c>·<c>levelStartXp</c>·<c>nextLevelXp</c>)으로 바꾼 사본. <c>level</c>이 없거나 0 이하면 레벨을 모름(나머지 칸도 0).
    /// </summary>
    internal CommunityUser WithLevel(JsonObject o)
    {
        int level = CommunityJson.Int(o["level"]);
        return level > 0
            ? this with { Level = level, Xp = CommunityJson.Int(o["xp"]), LevelStartXp = CommunityJson.Int(o["levelStartXp"]), NextLevelXp = CommunityJson.IntOrNull(o["nextLevelXp"]) }
            : this with { Level = 0, Xp = 0, LevelStartXp = 0, NextLevelXp = null };
    }

    private static bool Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue(out bool on) && on;

    /// <summary><see cref="Read"/>가 다시 읽는 모양(저장한 계정 목록에 씀). 레벨을 모르면 레벨 칸은 쓰지 않는다.</summary>
    internal JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["id"] = Id, ["handle"] = Handle, ["name"] = Name, ["createdAt"] = CreatedAt, ["isAdmin"] = IsAdmin, ["handleLocked"] = HandleLocked,
        };
        if (AvatarUrl is not null) o["avatarUrl"] = AvatarUrl;
        if (Level > 0)
        {
            o["level"] = Level;
            o["xp"] = Xp;
            o["levelStartXp"] = LevelStartXp;
            if (NextLevelXp is { } next) o["nextLevelXp"] = next;
        }
        return o;
    }
}

/// <summary>
/// 앱을 켜 둔 시간 경험치 응답(<c>POST /api/me/uptime</c>): 이번에 +1 XP를 받았는지, 다음에 받을 수 있을 때까지 남은 밀리초,
/// 지금 레벨·경험치(<see cref="CommunityUser"/>의 같은 칸, 레벨이 없으면 0).
/// </summary>
public sealed record CommunityUptime(bool Granted, long NextInMs, int Level, int Xp, int LevelStartXp, int? NextLevelXp)
{
    internal static CommunityUptime Read(JsonNode? node)
    {
        if (node is not JsonObject o) throw CommunityJson.Bad("uptime");
        bool granted = o["granted"] is JsonValue g && g.TryGetValue(out bool on) && on;
        long next = o["nextInMs"] is JsonValue v && v.TryGetValue(out double ms) && ms > 0 ? (long)Math.Min(ms, long.MaxValue / 2) : 0;
        int level = CommunityJson.Int(o["level"]);
        return level > 0
            ? new CommunityUptime(granted, next, level, CommunityJson.Int(o["xp"]), CommunityJson.Int(o["levelStartXp"]), CommunityJson.IntOrNull(o["nextLevelXp"]))
            : new CommunityUptime(granted, next, 0, 0, 0, null);
    }
}

/// <summary>하루 출석 응답(<c>POST /api/me/checkin</c>): 이번에 경험치를 받았는지와 지금 레벨·경험치(<see cref="CommunityUptime"/>와 같은 칸).</summary>
public sealed record CommunityCheckIn(bool Granted, int Level, int Xp)
{
    internal static CommunityCheckIn Read(JsonNode? node)
    {
        var grant = CommunityUptime.Read(node);
        return new CommunityCheckIn(grant.Granted, grant.Level, grant.Xp);
    }
}

/// <summary>
/// 올린 그림 하나(<c>Art</c> JSON 중 앱이 쓰는 것): ID, 제목, 그림 주소(사이트 기준 경로), 크기, 프레임 수, 형식(png·gif),
/// 같이 올린 <c>.chickxel</c> 파일(없거나 예전 서버면 null).
/// </summary>
public sealed record CommunityArt(string Id, string Title, string ImageUrl, int Width, int Height, int Frames, string Type, CommunityArtProject? Project = null)
{
    internal static CommunityArt Read(JsonNode? node)
    {
        if (node is not JsonObject o) throw CommunityJson.Bad("art");
        var image = o["image"] as JsonObject;
        return new CommunityArt(CommunityJson.Required(o, "id"), CommunityJson.String(o["title"]) ?? "",
            CommunityJson.String(image?["url"]) ?? "", CommunityJson.Int(image?["width"]), CommunityJson.Int(image?["height"]),
            CommunityJson.Int(image?["frames"]), CommunityJson.String(image?["type"]) ?? "", CommunityArtProject.Read(o["project"]));
    }
}

/// <summary>그림과 같이 올린 편집용 <c>.chickxel</c> 파일(<c>Art.project</c> JSON): 받을 주소(사이트 기준 경로), 바이트 수.</summary>
public sealed record CommunityArtProject(string Url, int Bytes)
{
    /// <summary>없거나(<c>null</c>·예전 서버) 주소가 없으면 null.</summary>
    internal static CommunityArtProject? Read(JsonNode? node)
        => node is JsonObject o && CommunityJson.String(o["url"]) is { Length: > 0 } url ? new CommunityArtProject(url, CommunityJson.Int(o["bytes"])) : null;
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

/// <summary>
/// 갤러리에 올릴 그림: 파일 바이트(PNG나 GIF), 형식, 제목, 설명, 태그, 같이 올릴 편집용 <c>.chickxel</c> 바이트
/// (<see cref="Chickxel.Core.IO.ProjectIO.SaveForSharing"/>, 없으면 null), 그림 페이지에서 방문자에게 PNG/GIF 받기 버튼을 보일지
/// (<c>AllowDownload</c>, 끄면 올린 사람과 관리자에게만 보임 — 편집용 파일 받기와는 별개).
/// </summary>
public sealed record ArtUpload(byte[] Data, CommunityArtType Type, string Title, string Description, IReadOnlyList<string> Tags, byte[]? Project = null,
    bool AllowDownload = true);

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
    /// <summary>그림과 같이 올리는 <c>.chickxel</c> 파일 한도(서버와 같음).</summary>
    public const int MaxProjectBytes = 8 * 1024 * 1024;

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

    /// <summary>0 이상 정수, 없거나 <c>null</c>·다른 값이면 null.</summary>
    public static int? IntOrNull(JsonNode? node) => node is JsonValue value && value.TryGetValue(out double number) && number is >= 0 and <= int.MaxValue ? (int)number : null;

    public static string Required(JsonObject o, string key) => String(o[key]) is { Length: > 0 } text ? text : throw Bad(key);

    public static CommunityException Bad(string field) => new(CommunityErrorCode.BadResponse, serverMessage: field);
}
