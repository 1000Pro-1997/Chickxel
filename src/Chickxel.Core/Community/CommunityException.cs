using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Community;

/// <summary>커뮤니티 서비스 요청 실패의 종류(문구는 <c>DomainStrings</c>의 <c>Community_Error_{종류}</c>).</summary>
public enum CommunityErrorCode
{
    /// <summary>로그인하지 않았거나 세션이 끝남(401 <c>unauthorized</c>).</summary>
    Unauthorized,
    /// <summary>권한 없음(403 <c>forbidden</c>).</summary>
    Forbidden,
    /// <summary>계정이 막힘(403 <c>banned</c>).</summary>
    Banned,
    /// <summary>없는 항목(404 <c>not_found</c>).</summary>
    NotFound,
    /// <summary>입력이나 파일이 규칙에 맞지 않음(400 <c>invalid</c> — <see cref="CommunityException.Issues"/>에 까닭).</summary>
    Invalid,
    /// <summary>이미 있는 값과 겹침(409 <c>conflict</c> — 쓰는 핸들, 같은 패키지 버전 …).</summary>
    Conflict,
    /// <summary>파일이 너무 큼(413 <c>too_large</c>).</summary>
    TooLarge,
    /// <summary>요청이 너무 잦음(429 <c>rate_limited</c> — <see cref="CommunityException.RetryAfter"/>).</summary>
    RateLimited,
    /// <summary>서버 문제나 앱이 모르는 오류.</summary>
    Server,
    /// <summary>연결 실패.</summary>
    Network,
    /// <summary>요청 시간 초과.</summary>
    Timeout,
    /// <summary>응답 모양이 틀림.</summary>
    BadResponse,
    /// <summary>브라우저 로그인이 시간 안에 끝나지 않음.</summary>
    LoginTimeout,
    /// <summary>브라우저 로그인이 거절되거나 실패함(<see cref="CommunityException.ServerMessage"/> = 받은 오류 값).</summary>
    LoginFailed,
}

/// <summary>
/// 커뮤니티 서비스 요청 실패: 종류, HTTP 상태(요청을 보내지 못했으면 0), 서버의 오류 값·영어 설명(<c>{ error, message, detail }</c>),
/// <c>detail</c>에서 읽은 문제 줄(입력 칸·패키지 검사), 다시 시도할 때까지 기다릴 시간. <see cref="Exception.Message"/>는 지금 UI 언어 문구다.
/// </summary>
public sealed class CommunityException(CommunityErrorCode code, int status = 0, string? error = null, string? serverMessage = null,
    IReadOnlyList<string>? issues = null, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception(MessageFor(code, retryAfter), inner)
{
    public CommunityErrorCode Code { get; } = code;
    public int Status { get; } = status;
    /// <summary>서버가 보낸 <c>error</c> 값(없으면 null).</summary>
    public string? Error { get; } = error;
    /// <summary>서버가 보낸 <c>message</c>(영어, 없으면 null).</summary>
    public string? ServerMessage { get; } = serverMessage;
    /// <summary><c>detail</c>의 문제 줄(입력 칸 <c>field: code</c>, 패키지 검사 문제). 없으면 빈 목록.</summary>
    public IReadOnlyList<string> Issues { get; } = issues ?? [];
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>화면에 보일 글: 지금 UI 언어 문구, 서버 설명, 문제 줄(12개까지).</summary>
    public string DisplayText
    {
        get
        {
            var lines = new List<string> { Message };
            if (!string.IsNullOrWhiteSpace(ServerMessage) && Code is not (CommunityErrorCode.Network or CommunityErrorCode.Timeout)) lines.Add(ServerMessage.Trim());
            lines.AddRange(Issues.Take(12).Select(issue => "- " + issue));
            if (Issues.Count > 12) lines.Add("- …");
            return string.Join("\n", lines);
        }
    }

    private static string MessageFor(CommunityErrorCode code, TimeSpan? retryAfter)
    {
        // 기다릴 시간을 알면 "N분 뒤에 다시" 문구(Community_Error_RateLimitedWait).
        string key = code == CommunityErrorCode.RateLimited && retryAfter is not null ? "RateLimitedWait" : code.ToString();
        string text = DomainStrings.GetResourceString("Community_Error_" + key) ?? code.ToString();
        int minutes = retryAfter is { } wait ? Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes)) : 0;
        return text.Replace("{detail}", minutes.ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
    }

    /// <summary>서버 <c>error</c> 값 → 종류(모르는 값은 HTTP 상태로).</summary>
    public static CommunityErrorCode CodeOf(string? error, int status) => error switch
    {
        "unauthorized" => CommunityErrorCode.Unauthorized,
        "forbidden" => CommunityErrorCode.Forbidden,
        "banned" => CommunityErrorCode.Banned,
        "not_found" => CommunityErrorCode.NotFound,
        "invalid" => CommunityErrorCode.Invalid,
        "conflict" => CommunityErrorCode.Conflict,
        "too_large" => CommunityErrorCode.TooLarge,
        "rate_limited" => CommunityErrorCode.RateLimited,
        _ => status switch
        {
            401 => CommunityErrorCode.Unauthorized,
            403 => CommunityErrorCode.Forbidden,
            404 => CommunityErrorCode.NotFound,
            400 or 422 => CommunityErrorCode.Invalid,
            409 => CommunityErrorCode.Conflict,
            413 => CommunityErrorCode.TooLarge,
            429 => CommunityErrorCode.RateLimited,
            _ => CommunityErrorCode.Server,
        },
    };

    /// <summary>
    /// <c>detail</c>을 문제 줄로 읽는다: 배열(또는 <c>issues</c> 배열을 가진 객체)의 항목마다 글이면 그대로,
    /// 객체면 <c>field</c>·<c>path</c>와 <c>message</c>·<c>code</c>를 "자리: 까닭"으로. 그 밖의 모양은 빈 목록.
    /// </summary>
    public static IReadOnlyList<string> IssuesOf(JsonNode? detail)
    {
        if (detail is JsonObject wrapper && wrapper["issues"] is JsonArray inner) detail = inner;
        if (detail is not JsonArray items) return [];
        var lines = new List<string>();
        foreach (var item in items.Take(200))
        {
            switch (item)
            {
                case JsonValue value when value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text):
                    lines.Add(text.Trim());
                    break;
                case JsonObject o:
                    string? where = Text(o["field"]) ?? Text(o["path"]);
                    string? why = Text(o["message"]) ?? Text(o["code"]);
                    if (why is null && where is null) break;
                    lines.Add(where is null ? why! : why is null ? where : $"{where}: {why}");
                    break;
            }
        }
        return lines;
    }

    private static string? Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
