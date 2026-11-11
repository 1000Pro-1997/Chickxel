namespace Chickxel.Core.Assets;

/// <summary>
/// 기본 그라데이션 여덟 가지의 공식 패키지(<c>chickxel.gradients.&lt;ID&gt;</c>, 작성자 <c>chickxel</c>): 정적 카탈로그 원본(<c>catalog/chickxel.gradients.*/</c>)의
/// 패키지 파일을 그대로 앱에 넣어(내장 리소스 — 상점의 것과 같은 바이트) 기본으로 설치한다(<see cref="BundledPackages"/>). 버전은 파일 이름
/// (<c>&lt;패키지 ID&gt;-&lt;버전&gt;.chickxel-asset</c>)에서 읽는다 — 고친 그라데이션은 새 버전 패키지를 그 폴더에 넣으면(예전 파일은 지움) 다음 판에 업데이트된다.
/// 처음 넣을 때 예전 판이 이 컴퓨터의 그라데이션으로 넣어 둔 같은 내용의 기본 그라데이션은 지운다(즐겨찾기는 옮김).
/// </summary>
public static class OfficialGradientPackages
{
    public const string PackagePrefix = "chickxel.gradients.";
    private const string ResourcePrefix = "Chickxel.Gradients.";

    /// <summary>넣는 차례(예전 기본 그라데이션 차례 — <see cref="IO.GradientPresets.BuiltIns"/>).</summary>
    public static IReadOnlyList<string> Ids { get; } = ["rainbow", "repeating-rainbow", "sunset", "ocean", "transparent-fade", "fire", "forest", "grayscale"];

    /// <summary>패키지 ID → (리소스 이름, 버전).</summary>
    private static readonly Lazy<Dictionary<string, (string Resource, string Version)>> Resources = new(() =>
    {
        var found = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var name in typeof(OfficialGradientPackages).Assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix + PackagePrefix, StringComparison.Ordinal) || !name.EndsWith(AssetPackage.Extension, StringComparison.Ordinal)) continue;
            string stem = name[ResourcePrefix.Length..^AssetPackage.Extension.Length];
            int dash = stem.LastIndexOf('-');
            if (dash > 0 && AssetManifest.IsValidVersion(stem[(dash + 1)..])) found[stem[..dash]] = (name, stem[(dash + 1)..]);
        }
        return found;
    });

    public static string PackageId(string id) => PackagePrefix + id;

    /// <summary>앱에 든 기본 그라데이션 패키지(<see cref="Ids"/> 차례, 리소스가 없는 것은 뺌).</summary>
    public static IEnumerable<BundledPackage> Bundled
        => Ids.Where(id => Resources.Value.ContainsKey(PackageId(id))).Select(id =>
        {
            var (resource, version) = Resources.Value[PackageId(id)];
            return new BundledPackage(AssetKinds.Gradient, PackageId(id), version, () => Read(resource), entry => entry.Kind == AssetKinds.Gradient,
                ReplacesLocalCopies: true);
        });

    private static byte[] Read(string resource)
    {
        using var stream = typeof(OfficialGradientPackages).Assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException(resource);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
