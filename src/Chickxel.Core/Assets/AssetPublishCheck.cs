using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Assets;

/// <summary>게시 검사 문제의 무게: 오류는 올리기를 막고(서버·카탈로그 CI도 거부), 경고는 알리기만 한다.</summary>
public enum PublishSeverity
{
    Error,
    Warning,
}

/// <summary>앱 게시 창의 입력 칸 문제(문구는 <c>DomainStrings</c>의 <c>Publish_{종류}</c>). CI 규칙 문제는 <see cref="CatalogBuildIssueCode"/>.</summary>
public enum PublishIssueCode
{
    AuthorId,
    AuthorName,
    PackageId,
    Version,
    Name,
    Summary,
    /// <summary>언어별 이름·짧은 설명이 규칙(<see cref="AssetTranslations"/>)에 맞지 않음.</summary>
    Translations,
    Tags,
    /// <summary>경고: 태그가 없음(상점에서 찾기 어려움).</summary>
    NoTags,
    /// <summary>버전이 이미 올린 가장 높은 버전(온라인 목록·이 컴퓨터의 기록)보다 높지 않음. 올린 버전은 바꿀 수 없으므로 같은 버전도 안 된다.</summary>
    VersionNotNewer,
    /// <summary>효과 레시피(기본 효과를 합친 것)임. 지금은 효과로 스크립트 효과만 올린다(서버도 받지 않음).</summary>
    RecipeNotPublishable,
    /// <summary>다른 레이어를 읽는 스크립트(<see cref="ScriptAsset.ReadsOtherLayers"/>)인데 태그에 <see cref="ScriptAsset.LayerReferenceTag"/>가 없음.</summary>
    LayerReferenceTag,
    /// <summary>다른 레이어를 읽는 스크립트인데 <see cref="ScriptAsset.LayerReferenceTag"/>를 더하면 태그가 <see cref="RemoteCatalogFormat.MaxTags"/>개를 넘음(태그를 하나 빼야 함).</summary>
    LayerReferenceTagFull,
}

/// <summary>
/// 게시 검사 문제 하나: 무게, 지금 UI 언어 문구, 입력 칸 문제(<see cref="Code"/>)나 CI와 같은 카탈로그 규칙 문제(<see cref="CatalogCode"/>).
/// </summary>
public sealed record PublishIssue(PublishSeverity Severity, string Message)
{
    public PublishIssueCode? Code { get; init; }
    public CatalogBuildIssueCode? CatalogCode { get; init; }
    public bool IsError => Severity == PublishSeverity.Error;

    public static PublishIssue Field(PublishSeverity severity, PublishIssueCode code, string? detail = null)
        => new(severity, (DomainStrings.GetResourceString("Publish_" + code) ?? code.ToString()).Replace("{detail}", detail ?? "", StringComparison.Ordinal))
        {
            Code = code,
        };

    public static PublishIssue Catalog(CatalogBuildIssue issue) => new(PublishSeverity.Error, issue.Text) { CatalogCode = issue.Code };

    public override string ToString() => Message;
}

/// <summary>게시할 에셋: 종류, 값, 미리보기(효과면 원본 포함, 없으면 null).</summary>
public sealed record PublishAsset(string Kind, IAssetData Data, PackagePreview? Preview);

/// <summary>이 컴퓨터에서 올린 패키지(다음 버전은 이 기록과 온라인 목록 중 가장 높은 버전보다 높아야 함, 다음 버전 제안).</summary>
public sealed record PublishedVersion(string Id, string Version, string Sha256);

/// <summary>패키지 ID를 어디서 정했는지(<see cref="AssetPublishCheck.ResolveTarget"/>).</summary>
public enum PublishIdSource
{
    /// <summary>내가 만든 설치 패키지의 에셋 — 그 패키지 ID.</summary>
    OwnPackage,
    /// <summary>내 설치 패키지 에셋의 개인 사본(원문 해시나 이름이 같음) — 그 패키지 ID.</summary>
    OwnPackageCopy,
    /// <summary>핸들과 이름에서 만든 ID.</summary>
    Derived,
}

/// <summary>게시할 패키지 ID와 그 출처, 그 ID의 내 설치 패키지(없으면 null).</summary>
public sealed record PublishTarget(string Id, PublishIdSource Source, InstalledPackage? Package);

/// <summary>
/// 게시 창 입력: 작성자(계정 핸들·표시 이름), 패키지 ID·버전·이름, 짧은 설명(manifest 설명과 목록 요약), 언어별 이름·짧은 설명, 태그,
/// 올릴 에셋 하나, 목록 미리보기(직접 고른 PNG, 없으면 에셋 미리보기), 최소 앱 버전. 라이선스는 고정(<see cref="AssetPublishCheck.License"/>).
/// </summary>
public sealed record PublishRequest
{
    public AssetAuthor Author { get; init; } = new("", "");
    public string Id { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    public string Name { get; init; } = "";
    public string Summary { get; init; } = "";
    public IReadOnlyDictionary<string, AssetTranslation> Translations { get; init; } = AssetTranslations.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public required PublishAsset Asset { get; init; }
    public byte[]? CustomPreview { get; init; }
    public string MinAppVersion { get; init; } = "0.0.0";
}

/// <summary>
/// 게시 검사 결과: 문제 목록, 만든 패키지 바이트와 manifest(입력 칸 오류나 패키지 검사 실패면 null), 목록 태그.
/// <see cref="Ok"/>일 때만 올린다.
/// </summary>
public sealed record PublishResult(IReadOnlyList<PublishIssue> Issues, byte[]? Package, AssetManifest? Manifest)
{
    public bool Ok => Package is not null && Issues.All(issue => !issue.IsError);

    /// <summary>목록 태그(올릴 때 같이 보냄).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public int ErrorCount => Issues.Count(issue => issue.IsError);

    public int WarningCount => Issues.Count(issue => !issue.IsError);

    /// <summary>패키지 SHA-256(소문자 16진수). 패키지가 없으면 null.</summary>
    public string? Sha256 => Package is null ? null : AssetPackageWriter.Hash(Package);

    /// <summary>패키지 파일 이름 <c>&lt;ID&gt;-&lt;버전&gt;.chickxel-asset</c>.</summary>
    public string? PackageFileName => Manifest is null ? null : $"{Manifest.Id}-{Manifest.Version}{AssetPackage.Extension}";
}

/// <summary>
/// 공개 게시 규칙(계획 9.1·9.3, 14장 5단계). 예전 정적 카탈로그 만들기(<see cref="RemoteCatalogBuilder"/>, CI)와 앱의 게시 창이 같은 함수를 쓴다:
/// 개인 작성자·<c>personal.</c> ID 금지, ID 첫 조각 = 작성자 ID, 공개 라이선스와 LICENSE 파일, <c>official</c> ↔ 작성자 <c>chickxel</c>,
/// 목록 미리보기, 짧은 설명·태그, 이미 게시한 버전의 내용 변경·버전 내림 금지.
/// <see cref="Check"/>는 게시 창 입력으로 패키지를 만들어 설치와 같은 엄격 검사와 위 규칙을 모두 돌린다. 앱은 오류가 없을 때만 커뮤니티 서비스에 올린다
/// (작성자 ID·패키지 ID 첫 조각 = 계정 핸들은 서버가 다시 본다).
/// </summary>
public static class AssetPublishCheck
{
    /// <summary>
    /// 게시 패키지의 고정 라이선스(지금은 누구나 조건 없이 복사·변경·사용할 수 있는 CC0 1.0). 바꿀 때는 이 값과
    /// <c>Assets/Licenses/&lt;ID&gt;.txt</c> 원문, 게시 창의 설명 문구(<c>Publish_LicenseFixed</c>)를 같이 바꾼다.
    /// </summary>
    public const string License = "CC0-1.0";

    public const string LicenseFileName = "LICENSE.txt";

    private static string? _licenseText;

    /// <summary><see cref="License"/>의 법률 원문(패키지 <c>LICENSE.txt</c>). 해시가 컴퓨터마다 같도록 줄 끝은 LF로 맞춘다. 파일 내용이라 번역하지 않는다.</summary>
    public static string LicenseText => _licenseText ??= LoadLicenseText();

    private static string LoadLicenseText()
    {
        using var stream = typeof(AssetPublishCheck).Assembly.GetManifestResourceStream($"Chickxel.Licenses.{License}.txt")
                           ?? throw new InvalidOperationException(License);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    // ───────────── CI와 같이 쓰는 공개 규칙 ─────────────

    /// <summary>
    /// 패키지만으로 보는 공개 규칙: 개인 작성자(<c>personal</c>)·<c>personal.</c> ID 금지, ID 첫 조각 = 작성자 ID,
    /// <c>LicenseRef-Personal</c> 금지와 <c>LICENSE.txt|md</c> 필수. <paramref name="where"/>는 문제에 붙일 자리(제출 폴더 이름).
    /// </summary>
    public static IEnumerable<CatalogBuildIssue> PackageRules(AssetPackage package, string where)
    {
        var manifest = package.Manifest;
        if (manifest.Author.Id == "personal" || manifest.Id.StartsWith(AssetStore.PersonalIdPrefix, StringComparison.Ordinal))
            yield return new CatalogBuildIssue(CatalogBuildIssueCode.MissingAuthor, where);
        else if (manifest.Id.Split('.')[0] != manifest.Author.Id)
            yield return new CatalogBuildIssue(CatalogBuildIssueCode.AuthorMismatch, where, manifest.Author.Id);
        if (manifest.License.StartsWith("LicenseRef-Personal", StringComparison.OrdinalIgnoreCase)
            || !package.Files.Keys.Any(path => path.StartsWith("LICENSE", StringComparison.Ordinal)))
            yield return new CatalogBuildIssue(CatalogBuildIssueCode.MissingLicense, where, manifest.License);
    }

    /// <summary>
    /// 검토 정보와 같이 보는 공개 규칙: <c>official</c>은 작성자 <c>chickxel</c>만(반대도), 목록 미리보기(<paramref name="reviewPreview"/>나
    /// 에셋 미리보기)가 있어야 한다.
    /// </summary>
    public static IEnumerable<CatalogBuildIssue> ReviewRules(AssetPackage package, string status, string? reviewPreview, string where)
    {
        if ((status == RemoteReview.Official) != (package.Manifest.Author.Id == RemoteCatalogBuilder.OfficialAuthor))
            yield return new CatalogBuildIssue(CatalogBuildIssueCode.OfficialMismatch, where, status);
        if (ListPreview(package.Manifest, reviewPreview) is null)
            yield return new CatalogBuildIssue(CatalogBuildIssueCode.MissingPreview, where);
    }

    /// <summary>목록 미리보기 PNG의 패키지 안 경로: 검토 정보의 <c>preview</c>, 없으면 미리보기가 있는 첫 에셋의 것.</summary>
    public static string? ListPreview(AssetManifest manifest, string? reviewPreview)
        => reviewPreview ?? manifest.Assets.Select(asset => asset.Preview).FirstOrDefault(path => path is not null);

    /// <summary>이미 게시한 버전과 비교: 같은 버전인데 패키지 SHA-256이 다르거나 버전이 낮으면 문제.</summary>
    public static CatalogBuildIssue? PublishedRule(AssetManifest manifest, string sha256, string publishedVersion, string publishedSha256, string where)
    {
        if (publishedVersion == manifest.Version && publishedSha256 != sha256)
            return new CatalogBuildIssue(CatalogBuildIssueCode.PublishedChanged, where, manifest.Version);
        if (SemVersion.TryParse(publishedVersion, out var old) && manifest.SemanticVersion < old)
            return new CatalogBuildIssue(CatalogBuildIssueCode.VersionDowngrade, where, publishedVersion);
        return null;
    }

    /// <summary>목록 태그: <see cref="RemoteCatalogFormat.MaxTags"/>개 이하, 각각 소문자·숫자·하이픈, 중복 없음.</summary>
    public static bool IsValidTags(IReadOnlyList<string?> tags)
        => tags.Count <= RemoteCatalogFormat.MaxTags && tags.All(RemoteCatalogFormat.IsValidTag)
                                                      && tags.Distinct(StringComparer.Ordinal).Count() == tags.Count;

    // ───────────── 게시 창 ─────────────

    /// <summary>
    /// 입력한 태그 글을 태그 목록으로: 쉼표·공백·<c>#</c>으로 나누고 소문자로 바꾸며 빈 것과 중복을 뺀다(<c>"#Neon #hit"</c> → neon, hit).
    /// 형식 검사는 <see cref="Check"/>가 한다.
    /// </summary>
    public static IReadOnlyList<string> ParseTags(string? text)
        => (text ?? "").Split([',', ' ', '\t', '\n', '\r', '#'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// 처음 제안하는 패키지 ID <c>&lt;작성자 ID&gt;.&lt;이름 조각&gt;</c>: <paramref name="names"/> 중 ASCII 글자·숫자가 있는 첫 이름의 조각
    /// (<see cref="AssetPackageBuilder.Slug"/>), 모두 없으면(한국어 이름 등) 첫 이름 SHA-256의 앞 8자리.
    /// </summary>
    public static string DefaultId(string authorId, params IEnumerable<string?> names)
    {
        var list = names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!.Trim()).ToList();
        string segment = list.Select(name => AssetPackageBuilder.Slug(name, "")).FirstOrDefault(slug => slug.Length > 0)
                         ?? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(list.FirstOrDefault() ?? "")))[..8];
        return authorId.Length > 0 ? authorId + "." + segment : segment;
    }

    /// <summary>
    /// 게시할 패키지 ID를 정한다(사용자 모델: 같은 핸들 + 같은 패키지 = 업데이트).
    /// <list type="number">
    /// <item>에셋이 내가 만든(작성자 ID = <paramref name="handle"/>) 설치 패키지의 것이면 그 패키지 ID(<see cref="PublishIdSource.OwnPackage"/>).</item>
    /// <item>개인 에셋이 내 설치 패키지 에셋의 사본이면(스크립트 원문 해시가 같은 것, 없으면 같은 종류·같은 이름) 그 패키지 ID
    /// (<see cref="PublishIdSource.OwnPackageCopy"/>) — 이름·번역을 바꿔도 같은 패키지를 업데이트한다.</item>
    /// <item>그 밖(다른 사람의 패키지 에셋 포함)은 <see cref="DefaultId"/>(<paramref name="handle"/>.이름 조각, <see cref="PublishIdSource.Derived"/>).
    /// 그 ID가 내 설치 패키지면 <see cref="PublishTarget.Package"/>에 둔다(이름이 같아 업데이트가 됨 — 창이 알림).</item>
    /// </list>
    /// 에셋이 여럿인 예전 패키지는 알아보지 않는다(한 에셋으로 올리면 나머지가 빠지므로).
    /// </summary>
    public static PublishTarget ResolveTarget(string handle, AssetEntry asset, AssetCatalog catalog, params IEnumerable<string?> names)
    {
        bool Mine(InstalledPackage package) => IsOwnPackage(package, handle);
        if (asset.Origin == AssetOrigin.Package && asset.PackageId is { } packageId && catalog.FindPackage(packageId) is { } owner && Mine(owner))
            return new PublishTarget(owner.Id, PublishIdSource.OwnPackage, owner);
        if (asset.Origin == AssetOrigin.Personal)
        {
            var mine = catalog.Packages.Where(Mine).ToList();
            string? hash = asset.Script?.Hash;
            var copied = (hash is null ? null : mine.FirstOrDefault(p => p.Entries.Any(e => e.Script?.Hash == hash)))
                         ?? mine.FirstOrDefault(p => p.Entries.Any(e => e.Kind == asset.Kind
                                                                     && string.Equals(e.Name.Trim(), asset.Name.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (copied is not null) return new PublishTarget(copied.Id, PublishIdSource.OwnPackageCopy, copied);
        }
        string id = DefaultId(handle, names);
        return new PublishTarget(id, PublishIdSource.Derived, catalog.FindPackage(id) is { } same && same.Manifest?.Author.Id == handle ? same : null);
    }

    /// <summary>
    /// <paramref name="handle"/>이 만든(작성자 ID가 같은), 읽을 수 있는 에셋 하나짜리 설치 패키지인지 — 게시 창이 그 ID로 새 버전을 올리는 패키지.
    /// </summary>
    public static bool IsOwnPackage(InstalledPackage package, string? handle)
        => handle is { Length: > 0 } && package.Manifest?.Author.Id == handle && !package.IsBroken && package.Entries.Count == 1;

    /// <summary>
    /// 다음 버전 제안: 전에 게시한 버전이 없으면 1.0.0, 프리릴리스(1.2.0-beta)면 그 정식 버전(1.2.0), 아니면 수 버전을 올린다(1.2.3 → 1.2.4).
    /// </summary>
    public static string SuggestVersion(string? previous)
    {
        if (!SemVersion.TryParse(previous, out var v)) return "1.0.0";
        return v.IsPrerelease
            ? string.Create(CultureInfo.InvariantCulture, $"{v.Major}.{v.Minor}.{v.Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{v.Major}.{v.Minor}.{v.Patch + 1}");
    }

    /// <summary><paramref name="id"/>의 가장 높은 게시 버전(온라인 목록과 이 컴퓨터의 저장 기록 중). 없으면 null.</summary>
    public static string? LatestVersion(string id, IEnumerable<RemoteCatalogEntry> catalog, IEnumerable<PublishedVersion> saved)
        => catalog.Where(entry => entry.Id == id).Select(entry => entry.Version)
            .Concat(saved.Where(record => record.Id == id).Select(record => record.Version))
            .Select(text => SemVersion.TryParse(text, out var v) ? (Text: text, Version: v) : default)
            .Where(pair => pair.Text is not null)
            .OrderByDescending(pair => pair.Version)
            .Select(pair => pair.Text)
            .FirstOrDefault();

    /// <summary>
    /// 게시 창 입력을 검사한다. 입력 칸이 맞으면 에셋 하나로 패키지를 만들어(라이선스 <see cref="License"/>, <c>LICENSE.txt</c>는 그 원문)
    /// 설치와 같은 엄격 검사, 공개 규칙(<see cref="PackageRules"/>,
    /// <see cref="ReviewRules"/>, 짧은 설명·태그)과 이름 겹침을 보고, 버전은 온라인 목록(<paramref name="catalog"/>, 마지막으로 받은 목록)과
    /// 이 컴퓨터에서 올린 기록(<paramref name="saved"/>) 중 가장 높은 버전보다 높아야 한다. 검토 상태는 서버와 같이 작성자 <c>chickxel</c>이면
    /// <c>official</c>, 아니면 <c>community</c>다.
    /// </summary>
    public static PublishResult Check(PublishRequest request, IReadOnlyList<RemoteCatalogEntry> catalog, IEnumerable<PublishedVersion> saved)
    {
        var issues = new List<PublishIssue>();
        void Error(PublishIssueCode code) => issues.Add(PublishIssue.Field(PublishSeverity.Error, code));
        void Warn(PublishIssueCode code, string? detail = null) => issues.Add(PublishIssue.Field(PublishSeverity.Warning, code, detail));

        if (request.Asset.Kind == AssetKinds.EffectRecipe) Error(PublishIssueCode.RecipeNotPublishable);
        if (!AssetManifest.IsValidSegment(request.Author.Id)) Error(PublishIssueCode.AuthorId);
        if (!AssetManifest.IsValidName(request.Author.Name)) Error(PublishIssueCode.AuthorName);
        if (!AssetManifest.IsValidId(request.Id)) Error(PublishIssueCode.PackageId);
        if (!AssetManifest.IsValidVersion(request.Version)) Error(PublishIssueCode.Version);
        if (!AssetManifest.IsValidName(request.Name)) Error(PublishIssueCode.Name);
        if (!RemoteCatalogFormat.IsValidSummary(request.Summary)) Error(PublishIssueCode.Summary);
        if (!AssetTranslations.IsValid(request.Translations)) Error(PublishIssueCode.Translations);
        if (!IsValidTags(request.Tags)) Error(PublishIssueCode.Tags);
        if (request.Tags.Count == 0) Warn(PublishIssueCode.NoTags);
        // 다른 레이어를 읽는 효과는 layer-reference 태그가 꼭 있어야 한다(상점에서 이런 효과를 따로 찾게)
        if (request.Asset.Data is ScriptAsset { ReadsOtherLayers: true } && !request.Tags.Contains(ScriptAsset.LayerReferenceTag))
            Error(request.Tags.Count >= RemoteCatalogFormat.MaxTags ? PublishIssueCode.LayerReferenceTagFull : PublishIssueCode.LayerReferenceTag);
        if (issues.Any(issue => issue.IsError)) return new PublishResult(issues, null, null);

        var translations = new Dictionary<string, AssetTranslation>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, translation) in request.Translations) translations[code] = new AssetTranslation(translation.Name?.Trim(), translation.Summary?.Trim());
        var builder = new AssetPackageBuilder(new AssetManifest
        {
            Id = request.Id,
            Version = request.Version,
            Name = request.Name.Trim(),
            Description = request.Summary.Trim(),
            Translations = translations,
            Author = new AssetAuthor(request.Author.Id, request.Author.Name.Trim()),
            License = License,
            MinAppVersion = request.MinAppVersion,
        });
        // 직접 고른 PNG는 효과 없는 원본과 짝이 맞지 않으므로 원본 없이 넣는다(서버는 첫 에셋의 미리보기를 목록 미리보기로 씀).
        var preview = request.CustomPreview is { Length: > 0 } custom ? new PackagePreview(custom, null) : request.Asset.Preview;
        builder.Add(request.Asset.Kind, request.Asset.Data, preview);
        builder.AddDocument(LicenseFileName, LicenseText);
        byte[] bytes;
        AssetManifest manifest;
        using (var buffer = new MemoryStream())
        {
            manifest = builder.Write(buffer);
            bytes = buffer.ToArray();
        }

        // 설치와 같은 엄격 검사(최소 앱 버전은 카탈로그가 앱마다 보이므로 보지 않음).
        AssetPackage package;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            package = AssetPackageValidator.Read(stream, new AssetValidationOptions());
        }
        catch (AssetPackageException ex)
        {
            issues.AddRange(ex.Issues.Select(issue => PublishIssue.Catalog(new CatalogBuildIssue(CatalogBuildIssueCode.InvalidPackage, manifest.Id, issue.Message))));
            return new PublishResult(issues, null, null);
        }

        string where = manifest.Id;
        string status = manifest.Author.Id == RemoteCatalogBuilder.OfficialAuthor ? RemoteReview.Official : RemoteReview.Community;
        issues.AddRange(PackageRules(package, where).Select(PublishIssue.Catalog));
        issues.AddRange(ReviewRules(package, status, null, where).Select(PublishIssue.Catalog));
        // 올린 버전은 바꿀 수 없다: 온라인 목록과 이 컴퓨터의 기록 중 가장 높은 버전보다 높아야 한다(서버도 같은 비교로 거부).
        if (LatestVersion(manifest.Id, catalog, saved) is { } latest && SemVersion.TryParse(latest, out var top) && manifest.SemanticVersion <= top)
            issues.Add(PublishIssue.Field(PublishSeverity.Error, PublishIssueCode.VersionNotNewer, latest));
        if (catalog.FirstOrDefault(entry => entry.Id != manifest.Id && string.Equals(entry.Name.Trim(), manifest.Name, StringComparison.OrdinalIgnoreCase)) is { } same)
            issues.Add(PublishIssue.Catalog(new CatalogBuildIssue(CatalogBuildIssueCode.Duplicate, where, same.Id)));
        return new PublishResult(issues, bytes, manifest) { Tags = request.Tags };
    }
}
