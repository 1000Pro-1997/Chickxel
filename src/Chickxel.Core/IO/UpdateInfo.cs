using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Chickxel.Core.IO;

/// <summary>
/// 시맨틱 버전(SemVer 2.0) "주.부.수[-프리릴리스]". 앞의 "v"와 빌드 메타데이터("+커밋")는 읽을 때 버린다.
/// 비교: 숫자 셋을 차례로 보고, 같으면 프리릴리스가 있는 쪽이 낮다. 프리릴리스끼리는 점으로 나눈 식별자를 차례로
/// (둘 다 숫자면 수로, 숫자는 글자보다 낮음, 글자는 서수로) 보고, 앞이 모두 같으면 식별자가 적은 쪽이 낮다.
/// </summary>
public sealed record SemVersion(int Major, int Minor, int Patch, string Prerelease = "") : IComparable<SemVersion>
{
    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>"1.2.3", "v1.2.3-beta.1", "1.2.3+abc", "1.2"(수 = 0)를 읽는다. 틀리면 false.</summary>
    public static bool TryParse(string? text, out SemVersion version)
    {
        version = new SemVersion(0, 0, 0);
        var s = (text ?? "").Trim();
        if (s.Length > 0 && s[0] is 'v' or 'V') s = s[1..];
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        string pre = "";
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
            if (!pre.Split('.').All(id => id.Length > 0 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) return false;
        }
        var parts = s.Split('.');
        if (parts.Length is < 2 or > 3) return false;
        var nums = new int[3];
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length == 0 || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out nums[i])) return false;
        version = new SemVersion(nums[0], nums[1], nums[2], pre);
        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        return c != 0 ? Math.Sign(c) : ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return 1;
        if (b.Length == 0) return -1;
        var x = a.Split('.');
        var y = b.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool xn = x[i].All(char.IsAsciiDigit), yn = y[i].All(char.IsAsciiDigit);
            int c = xn && yn ? CompareDigits(x[i], y[i]) : xn ? -1 : yn ? 1 : string.CompareOrdinal(x[i], y[i]);
            if (c != 0) return Math.Sign(c);
        }
        return x.Length.CompareTo(y.Length);
    }

    /// <summary>숫자 문자열을 수로 비교(길이 제한 없음).</summary>
    private static int CompareDigits(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    }

    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;

    public override string ToString()
        => FormattableString.Invariant($"{Major}.{Minor}.{Patch}") + (IsPrerelease ? "-" + Prerelease : "");
}

/// <summary>릴리스에 올린 파일 하나.</summary>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);

/// <summary>
/// GitHub 릴리스 하나. <see cref="Notes"/>는 릴리스 설명(마크다운 원문), <see cref="PageUrl"/>은 릴리스 웹 페이지.
/// <see cref="IsPrerelease"/>는 GitHub의 "pre-release" 표시나 태그의 프리릴리스(-beta 등) 중 하나라도 있으면 true.
/// </summary>
public sealed record ReleaseInfo(
    string Tag,
    SemVersion Version,
    string Name,
    string Notes,
    Uri PageUrl,
    bool IsPrerelease,
    bool IsDraft,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>이름이 같은 파일(대소문자 무시). 없으면 null.</summary>
    public ReleaseAsset? Asset(string name) => Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>자동 업데이트가 아는 운영체제.</summary>
public enum UpdateOs
{
    Windows,
    MacOS,
    Other,
}

/// <summary>
/// GitHub 릴리스로 하는 업데이트 확인: 주소, 최신 릴리스 JSON 읽기, 새 버전 판단, OS·CPU에 맞는 파일 고르기, SHA256SUMS 읽기.
/// 네트워크·설치는 앱이 한다.
/// </summary>
public static class AppUpdate
{
    public const string Owner = "1000Pro-1997";
    public const string Repository = "Chickxel";
    public const string UserAgent = "Chickxel";
    /// <summary>릴리스마다 올리는 체크섬 파일(<c>shasum -a 256</c> 출력 형식).</summary>
    public const string ChecksumsAssetName = "SHA256SUMS.txt";

    public const string RepoUrl = $"https://github.com/{Owner}/{Repository}";
    public const string ReleasesUrl = RepoUrl + "/releases";
    public const string IssuesUrl = RepoUrl + "/issues";
    public static readonly Uri LatestReleaseApiUrl = new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");
    /// <summary>
    /// 최신 릴리스 웹 주소. API와 달리 요청 한도(시간당 60회)가 없고, 최신 릴리스 태그 페이지(<c>/releases/tag/vX.Y.Z</c>)로 넘겨 준다
    /// (릴리스가 없으면 <c>/releases</c>로). 초안·프리릴리스는 가리키지 않는다.
    /// </summary>
    public static readonly Uri LatestReleasePageUrl = new(ReleasesUrl + "/latest");

    /// <summary>릴리스에 올리는 파일 이름들(<see cref="AssetName"/>이 고르는 것 + 체크섬).</summary>
    public static readonly string[] KnownAssetNames = ["Chickxel-windows-x64.zip", "Chickxel-macos-arm64.zip", "Chickxel-macos-x64.zip", ChecksumsAssetName];

    /// <summary>
    /// <see cref="LatestReleasePageUrl"/>가 넘겨 준 주소(<c>…/releases/tag/vX.Y.Z</c>)에서 릴리스 정보를 만든다. 파일 주소는
    /// <c>…/releases/download/{태그}/{이름}</c> 규칙으로 만들고(<see cref="KnownAssetNames"/>), 설명은 비워 둔다.
    /// 태그 페이지가 아니거나(릴리스 없음) 태그가 버전이 아니면 null.
    /// </summary>
    public static ReleaseInfo? ReleaseFromLatestRedirect(Uri location)
    {
        const string marker = "/releases/tag/";
        string path = location.AbsolutePath;
        int i = path.IndexOf(marker, StringComparison.Ordinal);
        if (location.Scheme != Uri.UriSchemeHttps || i < 0) return null;
        string tag = Uri.UnescapeDataString(path[(i + marker.Length)..].TrimEnd('/'));
        if (tag.Length == 0 || tag.Contains('/') || !SemVersion.TryParse(tag, out var version)) return null;
        var assets = KnownAssetNames
            .Select(n => new ReleaseAsset(n, new Uri($"{ReleasesUrl}/download/{Uri.EscapeDataString(tag)}/{n}"), 0))
            .ToList();
        return new ReleaseInfo(tag, version, tag, "", new Uri($"{ReleasesUrl}/tag/{Uri.EscapeDataString(tag)}"),
            version.IsPrerelease, false, null, assets);
    }

    /// <summary>
    /// GitHub <c>releases/latest</c>(또는 <c>releases/{id}</c>) 응답을 읽는다. 태그가 버전(vX.Y.Z)이 아니거나 페이지 주소가 없으면 null,
    /// JSON 객체가 아니면 <see cref="FormatException"/>. 내려받기 주소가 http(s)가 아닌 파일은 건너뛴다.
    /// </summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException(Chickxel.Core.Localization.DomainStrings.FormatUpdate_ReadFailed(error: ex.Message), ex);
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException(Chickxel.Core.Localization.DomainStrings.Update_InvalidObject);
            string tag = Str(root, "tag_name");
            if (!SemVersion.TryParse(tag, out var version)) return null;
            if (!Uri.TryCreate(Str(root, "html_url"), UriKind.Absolute, out var page) || page.Scheme != Uri.UriSchemeHttps) return null;
            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object) continue;
                    string name = Str(a, "name");
                    if (name.Length == 0 || !Uri.TryCreate(Str(a, "browser_download_url"), UriKind.Absolute, out var url)
                        || url.Scheme != Uri.UriSchemeHttps) continue;
                    long size = a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out var n) ? n : 0;
                    assets.Add(new ReleaseAsset(name, url, size));
                }
            }
            DateTimeOffset? published = DateTimeOffset.TryParse(Str(root, "published_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var p) ? p : null;
            string name0 = Str(root, "name");
            return new ReleaseInfo(
                tag,
                version,
                name0.Length > 0 ? name0 : tag,
                Str(root, "body").Replace("\r\n", "\n"),
                page,
                Bool(root, "prerelease") || version.IsPrerelease,
                Bool(root, "draft"),
                published,
                assets);
        }
    }

    /// <summary>
    /// <paramref name="latest"/>가 지금 버전보다 새로 쓸 릴리스인지. 초안은 무시하고, 프리릴리스는 지금 버전도 프리릴리스일 때만 권한다.
    /// </summary>
    public static bool IsNewer(SemVersion current, ReleaseInfo? latest)
        => latest is { IsDraft: false } && (!latest.IsPrerelease || current.IsPrerelease) && latest.Version > current;


    /// <summary>
    /// 이 OS·CPU에 맞는 릴리스 파일 이름. Windows는 x64 하나(ARM64 Windows도 x64 에뮬레이션으로 실행), macOS는 arm64/x64. 없으면 null.
    /// </summary>
    public static string? AssetName(UpdateOs os, Architecture arch) => os switch
    {
        UpdateOs.Windows when arch is Architecture.X64 or Architecture.Arm64 => "Chickxel-windows-x64.zip",
        UpdateOs.MacOS when arch == Architecture.Arm64 => "Chickxel-macos-arm64.zip",
        UpdateOs.MacOS when arch == Architecture.X64 => "Chickxel-macos-x64.zip",
        _ => null,
    };

    /// <summary>릴리스에서 이 OS·CPU용 파일. 없으면 null.</summary>
    public static ReleaseAsset? SelectAsset(ReleaseInfo release, UpdateOs os, Architecture arch)
        => AssetName(os, arch) is { } name ? release.Asset(name) : null;

    /// <summary>
    /// SHA256SUMS 파일(줄마다 "64자리 16진수␠␠파일 이름", 이진 모드 "*이름"·"./이름"도 됨)을 읽는다.
    /// 파일 이름(대소문자 무시) → 소문자 16진수. 형식이 틀린 줄과 # 주석은 건너뛰고, 같은 이름이 또 나오면 처음 것을 쓴다.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseChecksums(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 66 || line[0] == '#') continue;
            var hash = line[..64];
            if (!hash.All(char.IsAsciiHexDigit) || !char.IsWhiteSpace(line[64])) continue;
            var name = line[65..].TrimStart();
            if (name.StartsWith('*')) name = name[1..];
            if (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
            if (name.Length > 0) map.TryAdd(name, hash.ToLowerInvariant());
        }
        return map;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>
/// 저장소의 바뀐 점에서 버전 절을 고른다: <c>CHANGELOG.md</c>(영어, 원본 — v1.14.23부터)와 <c>CHANGELOG.ko.md</c>(한국어, 모든 버전 — 머리는 영어와 같음).
/// 절 머리는 <c>## v1.2.3 (날짜)</c>이고, 범위로 쓴 절(<c>## v1.10.x</c>, <c>## v1.0.0 ~ v1.5.x</c>)은 머리의 가장 높은 버전으로 본다
/// (<c>x</c> = 그 부 버전의 끝). 두 파일 모두 릴리스마다 함께 올려 업데이트 창이 API 한도 없이 읽는다. 어느 파일을 쓸지는 앱이 정한다.
/// </summary>
public static class ChangelogNotes
{
    /// <summary>릴리스에 함께 올리는 영어 바뀐 점 파일 이름(<see cref="KoreanAssetName"/>이 생기기 전 예전 릴리스에서는 이 이름이 한국어 파일).</summary>
    public const string EnglishAssetName = "CHANGELOG.md";

    /// <summary>릴리스에 함께 올리는 한국어 바뀐 점 파일 이름.</summary>
    public const string KoreanAssetName = "CHANGELOG.ko.md";

    private static readonly System.Text.RegularExpressions.Regex VersionToken =
        new(@"v?(\d+)\.(\d+)(?:\.(\d+|x))?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// <paramref name="current"/>보다 높고 <paramref name="latest"/> 이하인 절들을 마크다운 원문 그대로 이어 붙인다(새 버전부터, 머리는
    /// <c>## v1.2.3 (날짜)</c> 줄 그대로, 절 안의 <c>###</c> 소제목과 <c>-</c> 세부 목록도 그대로 — 업데이트 창이 꾸며 보인다).
    /// <paramref name="fallback"/>을 주면 버전마다 <paramref name="markdown"/>의 절을 먼저 쓰고, 거기 없는 버전만 <paramref name="fallback"/>의
    /// 절로 채운다(영어 절이 없는 옛 버전을 한국어로). 맞는 절이 없거나 글이 비었으면 빈 문자열.
    /// </summary>
    public static string Between(string? markdown, SemVersion current, SemVersion latest, string? fallback = null)
    {
        var picked = Sections(markdown, current, latest);
        if (fallback is not null)
        {
            foreach (var section in Sections(fallback, current, latest))
                if (!picked.Exists(p => p.Version == section.Version)) picked.Add(section);
            picked = picked.OrderByDescending(p => p.Version).ToList();
        }
        return string.Join("\n\n", picked.Select(p => p.Text));
    }

    /// <summary><paramref name="current"/>보다 높고 <paramref name="latest"/> 이하인 절들(글 순서대로, 머리 버전과 글).</summary>
    private static List<(SemVersion Version, string Text)> Sections(string? markdown, SemVersion current, SemVersion latest)
    {
        var picked = new List<(SemVersion, string)>();
        if (string.IsNullOrWhiteSpace(markdown)) return picked;
        SemVersion? version = null;
        List<string>? section = null;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            // "## "만 버전 절 머리다. 절 안의 "### 소제목"은 "##" 뒤가 "#"라 여기 걸리지 않고 절에 그대로 들어간다.
            if (raw.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                version = HeaderVersion(raw[3..]);
                section = version is not null && version > current && version <= latest ? [raw.TrimEnd()] : null;
                continue;
            }
            if (raw.StartsWith("# ", StringComparison.Ordinal)) { Flush(); section = null; continue; }
            section?.Add(raw);
        }
        Flush();
        return picked;

        void Flush()
        {
            if (section is null) return;
            while (section.Count > 1 && string.IsNullOrWhiteSpace(section[^1])) section.RemoveAt(section.Count - 1);
            picked.Add((version!, string.Join("\n", section)));
            section = null;
        }
    }

    /// <summary>절 머리의 가장 높은 버전(<c>x</c>는 그 부 버전의 끝). 버전이 없으면 null.</summary>
    public static SemVersion? HeaderVersion(string header)
    {
        SemVersion? best = null;
        foreach (System.Text.RegularExpressions.Match m in VersionToken.Matches(header))
        {
            int major = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), minor = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int patch = !m.Groups[3].Success ? 0 : m.Groups[3].Value is "x" or "X" ? int.MaxValue : int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            var v = new SemVersion(major, minor, patch);
            if (best is null || v > best) best = v;
        }
        return best;
    }
}
