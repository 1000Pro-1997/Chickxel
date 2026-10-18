using System.Text.Json;

namespace Gpixel.Core.IO;

/// <summary>Unity Hub에 등록된 프로젝트 하나.</summary>
/// <param name="Title">표시 이름(없으면 폴더 이름).</param>
/// <param name="Path">프로젝트 루트 경로.</param>
/// <param name="Version">Unity 에디터 버전(모르면 빈 문자열).</param>
/// <param name="LastModified">마지막 수정 시각(모르면 null).</param>
/// <param name="IsFavorite">Hub 즐겨찾기 여부.</param>
/// <param name="Exists">경로가 아직 존재하는지(없으면 회색 표시).</param>
public sealed record UnityHubProject(
    string Title, string Path, string Version, DateTimeOffset? LastModified, bool IsFavorite, bool Exists)
{
    /// <summary>프로젝트의 Assets 폴더 경로.</summary>
    public string AssetsPath => System.IO.Path.Combine(Path, "Assets");
}

/// <summary>Hub 목록 읽기 결과. <see cref="Error"/>가 있으면 "폴더 직접 선택"으로 안내한다.</summary>
public sealed record UnityHubProjectList(IReadOnlyList<UnityHubProject> Projects, string? Error)
{
    public bool Success => Error is null;
}

/// <summary>
/// Unity Hub 프로젝트 목록(<c>%APPDATA%\UnityHub\projects-v1.json</c>, 예전 Hub의 <c>projects.json</c>)을 읽는다.
/// 즐겨찾기 먼저, 그다음 최근 수정 순으로 정렬한다. 파일이 없거나 깨져도 예외 없이 오류 문구를 돌려준다.
/// </summary>
public static class UnityHubProjects
{
    /// <summary>찾아볼 Hub 목록 파일 경로(우선순위 순).</summary>
    public static IReadOnlyList<string> GetDefaultPaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var hub = Path.Combine(appData, "UnityHub");
        return [Path.Combine(hub, "projects-v1.json"), Path.Combine(hub, "projects.json")];
    }

    /// <summary>기본 위치의 Hub 목록을 읽는다.</summary>
    public static UnityHubProjectList Load() => Load(GetDefaultPaths(), Directory.Exists);

    /// <summary>주어진 후보 파일 중 처음으로 읽히는 목록을 돌려준다.</summary>
    public static UnityHubProjectList Load(IEnumerable<string> candidatePaths, Func<string, bool> directoryExists)
    {
        string? lastError = null;
        foreach (var path in candidatePaths)
        {
            if (!File.Exists(path)) continue;
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = $"Unity Hub 목록을 읽을 수 없습니다: {ex.Message}";
                continue;
            }
            var result = Parse(json, directoryExists);
            if (result.Success) return result;
            lastError = result.Error;
        }
        return new UnityHubProjectList([], lastError ?? "Unity Hub 프로젝트 목록 파일을 찾을 수 없습니다.");
    }

    /// <summary>
    /// Hub JSON을 해석한다. 지원 형식:
    /// <c>{"schema_version":"v1","data":{"경로":{title,path,version,lastModified,isFavorite}}}</c>,
    /// <c>data</c> 없이 경로 객체만 있는 형식, 경로 문자열 배열.
    /// </summary>
    public static UnityHubProjectList Parse(string json, Func<string, bool> directoryExists)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            return new UnityHubProjectList([], $"Unity Hub 목록 파일이 손상되었습니다: {ex.Message}");
        }

        using (doc)
        {
            var list = new List<UnityHubProject>();
            var root = doc.RootElement;
            var data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) ? d : root;

            if (data.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in data.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                        AddEntry(list, prop.Value, prop.Name, directoryExists);
                    else if (prop.Value.ValueKind == JsonValueKind.String && LooksLikePath(prop.Value.GetString()))
                        AddPathOnly(list, prop.Value.GetString()!, directoryExists);
                }
            }
            else if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object) AddEntry(list, item, null, directoryExists);
                    else if (item.ValueKind == JsonValueKind.String && LooksLikePath(item.GetString()))
                        AddPathOnly(list, item.GetString()!, directoryExists);
                }
            }
            else
            {
                return new UnityHubProjectList([], "Unity Hub 목록 형식을 알 수 없습니다.");
            }

            var sorted = list
                .GroupBy(p => NormalizePath(p.Path), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(p => p.IsFavorite)
                .ThenByDescending(p => p.LastModified ?? DateTimeOffset.MinValue)
                .ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return new UnityHubProjectList(sorted, null);
        }
    }

    private static void AddEntry(List<UnityHubProject> list, JsonElement e, string? key, Func<string, bool> exists)
    {
        var path = GetString(e, "path") ?? key;
        if (!LooksLikePath(path)) return;
        path = NormalizePath(path!);
        var title = GetString(e, "title") ?? GetString(e, "name");
        if (string.IsNullOrWhiteSpace(title)) title = Path.GetFileName(path);
        var version = GetString(e, "version") ?? "";
        DateTimeOffset? modified = null;
        if (e.TryGetProperty("lastModified", out var lm))
        {
            if (lm.ValueKind == JsonValueKind.Number && lm.TryGetInt64(out var ms) && ms > 0)
                modified = DateTimeOffset.FromUnixTimeMilliseconds(Math.Min(ms, 253402300799999));
            else if (lm.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(lm.GetString(), out var dto))
                modified = dto;
        }
        bool favorite = e.TryGetProperty("isFavorite", out var fav) && fav.ValueKind == JsonValueKind.True;
        list.Add(new UnityHubProject(title!, path, version, modified, favorite, SafeExists(exists, path)));
    }

    private static void AddPathOnly(List<UnityHubProject> list, string path, Func<string, bool> exists)
    {
        path = NormalizePath(path);
        list.Add(new UnityHubProject(Path.GetFileName(path), path, "", null, false, SafeExists(exists, path)));
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool LooksLikePath(string? s) => !string.IsNullOrWhiteSpace(s) && (s.Contains('/') || s.Contains('\\'));

    /// <summary>Hub는 '/' 구분자를 쓰므로 OS 구분자로 바꾸고 끝 구분자를 뗀다.</summary>
    private static string NormalizePath(string path)
        => Path.TrimEndingDirectorySeparator(path.Replace('/', Path.DirectorySeparatorChar));

    private static bool SafeExists(Func<string, bool> exists, string path)
    {
        try
        {
            return exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
