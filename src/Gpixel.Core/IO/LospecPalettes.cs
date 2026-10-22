using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>Lospec 팔레트 목록 정렬.</summary>
public enum LospecSort
{
    /// <summary>Lospec 기본(인기) 순.</summary>
    Default,
    Downloads,
    Newest,
    Alphabetical,
}

/// <summary>색 수 거르기 방식.</summary>
public enum LospecColorFilter
{
    Any,
    Max,
    Min,
    Exact,
}

/// <summary>목록 한 쪽 요청 조건. <see cref="Page"/>는 0부터.</summary>
public sealed record LospecQuery(
    LospecSort Sort = LospecSort.Default,
    LospecColorFilter ColorFilter = LospecColorFilter.Any,
    int ColorCount = 8,
    string Tag = "",
    int Page = 0);

/// <summary>
/// Lospec 팔레트 하나. <see cref="Author"/>는 작성자가 없으면 빈 문자열. <see cref="Examples"/>는 그 팔레트로 그린 예시 그림 주소(없으면 비어 있음).
/// </summary>
public sealed record LospecPalette(
    string Title,
    string Slug,
    string Author,
    IReadOnlyList<Color32> Colors,
    int Likes,
    int Downloads,
    IReadOnlyList<string> Tags,
    IReadOnlyList<Uri> Examples)
{
    /// <summary>Lospec 웹 페이지 주소.</summary>
    public string WebUrl => LospecPalettes.WebUrl(Slug);
}

/// <summary>목록 한 쪽과 조건에 맞는 전체 개수.</summary>
public sealed record LospecPage(IReadOnlyList<LospecPalette> Palettes, int TotalCount);

/// <summary>
/// lospec.com 팔레트 목록 읽기: 목록 주소 만들기, JSON 해석, 비동기 내려받기(<see cref="HttpClient"/>는 호출하는 쪽이 준다).
/// </summary>
public static class LospecPalettes
{
    public const string BaseUrl = "https://lospec.com/palette-list";
    /// <summary>예시 그림(<c>examples[].image</c>, 상대 경로)이 있는 곳.</summary>
    public const string CdnUrl = "https://cdn.lospec.com/";
    public const string UserAgent = "Gpixel";

    /// <summary>
    /// 목록 한 쪽 주소. Lospec은 매개변수가 하나라도 빠지거나 두 번 오면 제대로 답하지 않으므로 다섯 개를 모두 한 번씩 넣는다.
    /// </summary>
    public static Uri ListUrl(LospecQuery q)
    {
        string filter = q.ColorFilter switch
        {
            LospecColorFilter.Max => "max",
            LospecColorFilter.Min => "min",
            LospecColorFilter.Exact => "exact",
            _ => "any",
        };
        string sort = q.Sort switch
        {
            LospecSort.Downloads => "downloads",
            LospecSort.Newest => "newest",
            LospecSort.Alphabetical => "alphabetical",
            _ => "default",
        };
        int count = Math.Clamp(q.ColorCount, 1, 256);
        var sb = new StringBuilder(BaseUrl).Append("/load");
        sb.Append("?colorNumberFilterType=").Append(filter);
        sb.Append("&colorNumber=").Append(count.ToString(CultureInfo.InvariantCulture));
        sb.Append("&page=").Append(Math.Max(0, q.Page).ToString(CultureInfo.InvariantCulture));
        // Lospec 태그는 모두 소문자이고 대소문자를 가린다(GameBoy → 0건).
        sb.Append("&tag=").Append(Uri.EscapeDataString((q.Tag ?? "").Trim().ToLowerInvariant()));
        sb.Append("&sortingType=").Append(sort);
        return new Uri(sb.ToString());
    }

    /// <summary>팔레트 하나의 JSON 주소(<c>/palette-list/&lt;slug&gt;.json</c>).</summary>
    public static Uri PaletteJsonUrl(string slug) => new($"{BaseUrl}/{Uri.EscapeDataString(slug)}.json");

    /// <summary>팔레트 웹 페이지 주소.</summary>
    public static string WebUrl(string slug) => $"{BaseUrl}/{Uri.EscapeDataString(slug)}";

    /// <summary>입력한 이름을 Lospec 슬러그 모양으로(소문자, 공백 → '-', 연속 공백은 하나로).</summary>
    public static string ToSlug(string text)
        => string.Join('-', (text ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// <c>/palette-list/load</c> 응답을 읽는다. 객체가 아니거나 쓸 수 있는 색이 하나도 없는 팔레트, 16진수가 아닌 색은 건너뛴다.
    /// 형식이 아예 틀리면 <see cref="FormatException"/>.
    /// </summary>
    public static LospecPage ParsePage(string json)
    {
        using var doc = Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException(Gpixel.Core.Localization.DomainStrings.Lospec_InvalidObject);
        var list = new List<LospecPalette>();
        if (root.TryGetProperty("palettes", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var colors = ParseColors(e);
                if (colors.Count == 0) continue;
                string title = Str(e, "title");
                string slug = Str(e, "slug");
                if (slug.Length == 0) slug = ToSlug(title);
                if (slug.Length == 0) continue;
                if (title.Length == 0) title = slug;
                string author = e.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object ? Str(user, "name") : "";
                var tags = new List<string>();
                if (e.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array)
                    foreach (var tag in t.EnumerateArray())
                        if (tag.ValueKind == JsonValueKind.String && tag.GetString() is { Length: > 0 } s) tags.Add(s);
                var examples = new List<Uri>();
                if (e.TryGetProperty("examples", out var ex) && ex.ValueKind == JsonValueKind.Array)
                    foreach (var item in ex.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.Object && ExampleUrl(Str(item, "image")) is { } url) examples.Add(url);
                list.Add(new LospecPalette(title, slug, author, colors, Int(e, "likes"), Int(e, "downloads"), tags, examples));
            }
        }
        int total = Int(root, "totalCount");
        return new LospecPage(list, Math.Max(total, list.Count));
    }

    /// <summary>
    /// <c>/palette-list/&lt;slug&gt;.json</c> 응답(<c>{"name","author","colors"}</c>)을 읽는다. 색이 없거나 형식이 틀리면 null.
    /// </summary>
    public static LospecPalette? ParseSingle(string json, string slug)
    {
        try
        {
            using var doc = Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var colors = ParseColors(root);
            if (colors.Count == 0) return null;
            string name = Str(root, "name");
            return new LospecPalette(name.Length > 0 ? name : slug, slug, Str(root, "author"), colors, 0, 0, [], []);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>목록 한 쪽을 내려받는다. 네트워크 오류는 <see cref="HttpRequestException"/>, 취소·시간 초과는 <see cref="OperationCanceledException"/>.</summary>
    public static async Task<LospecPage> FetchPageAsync(HttpClient http, LospecQuery query, CancellationToken cancellationToken = default)
    {
        using var request = Request(ListUrl(query));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParsePage(json);
    }

    /// <summary>슬러그로 팔레트 하나를 내려받는다. 없는 슬러그(404·JSON 아님)면 null.</summary>
    public static async Task<LospecPalette?> FetchBySlugAsync(HttpClient http, string slug, CancellationToken cancellationToken = default)
    {
        slug = ToSlug(slug);
        if (slug.Length == 0) return null;
        using var request = Request(PaletteJsonUrl(slug));
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        // 내려받기용이라 Content-Type이 application/octet-stream으로 온다. JSON이 아니면 ParseSingle이 null을 준다.
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseSingle(json, slug);
    }

    /// <summary>예시 그림 주소: 절대 주소(http/https)는 그대로, 상대 경로는 <see cref="CdnUrl"/> 아래. 비었거나 이상하면 null.</summary>
    public static Uri? ExampleUrl(string image)
    {
        image = image.Trim();
        if (image.Length == 0) return null;
        if (Uri.TryCreate(image, UriKind.Absolute, out var abs))
            return abs.Scheme is "http" or "https" ? abs : null;
        return Uri.TryCreate(CdnUrl + image.TrimStart('/'), UriKind.Absolute, out var rel) ? rel : null;
    }

    /// <summary>예시 그림(PNG 등) 바이트를 내려받는다. 네트워크 오류는 <see cref="HttpRequestException"/>.</summary>
    public static async Task<byte[]> FetchImageAsync(HttpClient http, Uri url, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage Request(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException(Gpixel.Core.Localization.DomainStrings.FormatLospec_ReadFailed(error: ex.Message), ex);
        }
    }

    private static List<Color32> ParseColors(JsonElement e)
    {
        var colors = new List<Color32>();
        if (!e.TryGetProperty("colors", out var arr) || arr.ValueKind != JsonValueKind.Array) return colors;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var hex = (item.GetString() ?? "").Trim().TrimStart('#');
            if (hex.Length is 6 or 8 && Color32.TryParseHex(hex, out var c)) colors.Add(c);
        }
        return colors;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    /// <summary>숫자 또는 숫자 문자열("1,234" 포함). 못 읽으면 0.</summary>
    private static int Int(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt32(out var n) ? n : 0;
        if (v.ValueKind != JsonValueKind.String) return 0;
        var digits = new string((v.GetString() ?? "").Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : 0;
    }
}
