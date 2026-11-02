namespace Chickxel.Core.Assets;

/// <summary>
/// 홈페이지의 "Chickxel에서 열기" 주소: <c>chickxel://asset/&lt;패키지 ID&gt;</c>. 앱은 이 주소를 받으면 에셋 관리자 상점에서 그 패키지를 보인다
/// (설치는 사용자가 누를 때만, 패키지는 늘 온라인 목록에서 받음 — 주소에 든 것은 ID뿐).
/// </summary>
public static class AssetLinks
{
    public const string Scheme = "chickxel";
    private const string Host = "asset";

    /// <summary>패키지 <paramref name="packageId"/>를 여는 주소.</summary>
    public static string Url(string packageId) => $"{Scheme}://{Host}/{packageId}";

    /// <summary>
    /// <c>chickxel://asset/&lt;ID&gt;</c>(scheme·host 대소문자 무시, 끝의 '/'와 query·fragment는 무시)의 패키지 ID.
    /// 다른 주소이거나 ID 형식(<see cref="AssetManifest.IsValidId"/>)이 아니면 null.
    /// </summary>
    public static string? PackageId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 300 || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)) return null;
        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) || !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)) return null;
        string path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
        return AssetManifest.IsValidId(path) ? path : null;
    }
}
