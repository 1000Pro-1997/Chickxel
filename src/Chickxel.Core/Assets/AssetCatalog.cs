using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;

namespace Chickxel.Core.Assets;

/// <summary>에셋이 저장소에 들어온 길(<see cref="AssetEntry.Source"/>).</summary>
public static class AssetSources
{
    /// <summary>이 컴퓨터에서 만듦(효과 만들기, 프리셋 저장, 복사본).</summary>
    public const string Local = "local";
    /// <summary>패키지 파일에서 가져옴.</summary>
    public const string File = "file";
    /// <summary>온라인 목록(상점)에서 받음.</summary>
    public const string Remote = "remote";
    /// <summary>앱에 든 공식 효과.</summary>
    public const string Bundled = "bundled";
}

/// <summary>
/// 에셋이 상점(온라인 목록)에서 어떤 정체인지: 받은(또는 올린) 패키지 ID와 그 안의 에셋 ID, 버전, 그때의 내용 해시, 목록 글(이름·설명·번역·필요한 앱 버전).
/// 업데이트는 <see cref="PackageId"/>로 온라인 목록과 비교하고, 지금 내용 해시가 <see cref="ContentHash"/>와 다르면 고친 것이다(<see cref="AssetEntry.Modified"/>).
/// </summary>
public sealed record AssetStoreLink
{
    public string PackageId { get; init; } = "";
    public string AssetId { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    /// <summary>받거나 올렸을 때의 <see cref="AssetContent.Hash"/>.</summary>
    public string ContentHash { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, AssetTranslation> Translations { get; init; } = AssetTranslations.Empty;
    public string MinAppVersion { get; init; } = "0.0.0";

    public SemVersion SemanticVersion => SemVersion.TryParse(Version, out var v) ? v : new SemVersion(0, 0, 0);

    /// <summary>화면 언어의 목록 이름(번역 → <see cref="Name"/>).</summary>
    public string DisplayName => AssetTranslations.Find(Translations, CultureInfo.CurrentUICulture, t => t.Name) ?? Name;

    /// <summary>화면 언어의 목록 설명(번역 → <see cref="Description"/>, 없으면 null).</summary>
    public string? DisplayDescription => AssetTranslations.Find(Translations, CultureInfo.CurrentUICulture, t => t.Summary) ?? Description;

    /// <summary>패키지 <paramref name="manifest"/>에서 받은 에셋 <paramref name="assetId"/>의 연결(내용 해시 <paramref name="contentHash"/>).</summary>
    public static AssetStoreLink FromManifest(AssetManifest manifest, string assetId, string contentHash) => new()
    {
        PackageId = manifest.Id, AssetId = assetId, Version = manifest.Version, ContentHash = contentHash,
        Name = manifest.Name, Description = manifest.Description, Translations = manifest.Translations, MinAppVersion = manifest.MinAppVersion,
    };

    /// <summary>JSON(manifest 에셋 줄의 <c>store</c>, 저장소 파일). 필드 순서 고정.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["id"] = PackageId,
            ["assetId"] = AssetId,
            ["version"] = Version,
            ["sha256"] = ContentHash,
            ["name"] = Name,
        };
        if (Description is { Length: > 0 } description) json["description"] = description;
        if (AssetTranslations.Write(Translations) is { } translations) json["translations"] = translations;
        json["minAppVersion"] = MinAppVersion;
        return json;
    }

    /// <summary>JSON을 읽는다. 맞지 않으면 null과 틀린 필드 이름(<paramref name="bad"/>, <c>store.version</c> 등).</summary>
    public static AssetStoreLink? Read(JsonNode? node, out string? bad)
    {
        bad = null;
        if (node is not JsonObject o)
        {
            bad = "store";
            return null;
        }
        string id = AssetJson.String(o["id"]) ?? "", assetId = AssetJson.String(o["assetId"]) ?? "", version = AssetJson.String(o["version"]) ?? "";
        string hash = AssetJson.String(o["sha256"]) ?? "", name = AssetJson.String(o["name"]) ?? "", minApp = AssetJson.String(o["minAppVersion"]) ?? "0.0.0";
        string? description = AssetJson.String(o["description"]);
        if (!AssetManifest.IsValidId(id)) bad = "store.id";
        else if (!AssetManifest.IsValidAssetId(id, assetId)) bad = "store.assetId";
        else if (!AssetManifest.IsValidVersion(version)) bad = "store.version";
        else if (!AssetContent.IsHash(hash)) bad = "store.sha256";
        else if (!AssetManifest.IsValidName(name)) bad = "store.name";
        else if (o["description"] is not null && (description is null || description.Length > AssetManifest.MaxDescriptionLength)) bad = "store.description";
        else if (!SemVersion.TryParse(minApp, out _) || minApp.Length > 64) bad = "store.minAppVersion";
        if (bad is not null) return null;
        if (!AssetTranslations.TryRead(o["translations"], out var translations, out string? badTranslation))
        {
            bad = "store." + badTranslation;
            return null;
        }
        return new AssetStoreLink
        {
            PackageId = id, AssetId = assetId, Version = version, ContentHash = hash, Name = name.Trim(), Description = description,
            Translations = translations, MinAppVersion = minApp,
        };
    }
}

/// <summary>에셋 내용 해시: 종류의 파일 형식으로 쓴 바이트(ID와 무관)의 SHA-256. 받은 뒤 고쳤는지 볼 때 쓴다.</summary>
public static class AssetContent
{
    private const string HashId = "content.hash";

    public static string Hash(string kind, IAssetData data)
    {
        var definition = AssetKinds.Find(kind) ?? throw new ArgumentException(kind, nameof(kind));
        return Convert.ToHexStringLower(SHA256.HashData(definition.Write(data, HashId).Span));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IAssetData, string> Cache = new();

    /// <summary><see cref="Hash"/>를 값 객체마다 한 번만 계산한다(값은 바뀌지 않는 객체).</summary>
    public static string HashOf(string kind, IAssetData data) => Cache.GetValue(data, value => Hash(kind, value));

    /// <summary>소문자 16진수 64자인지.</summary>
    public static bool IsHash(string? text) => text is { Length: 64 } && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>
/// 카탈로그의 에셋 하나. <see cref="Data"/>는 값 자체이고, 프로젝트에 넣을 때는 값을 복사한다(계획 2.4).
/// 에셋마다 작성자·라이선스·상점 연결을 따로 가진다(패키지는 옮길 때만 쓰는 파일 형식). <see cref="Author"/>가 null이면 이 컴퓨터에서 만든 것이다.
/// 내 것인지는 <see cref="IsOwnedBy"/>(작성자 ID = 계정 핸들), 상점 업데이트는 <see cref="Store"/>로 본다. <see cref="Favorite"/>는 컴퓨터별 즐겨찾기.
/// </summary>
public sealed record AssetEntry(string Id, string Kind, IAssetData Data)
{
    public string Name => Data.Name;

    /// <summary>
    /// 화면에 보일 이름: 스크립트 에셋은 화면 언어(<see cref="CultureInfo.CurrentUICulture"/>)의 번역(<see cref="ScriptAsset.NameFor"/>), 그 밖은 <see cref="Name"/>.
    /// 정체·중복 판정·이름 바꾸기에는 <see cref="Name"/>을 쓴다.
    /// </summary>
    public string DisplayName => Script?.NameFor(CultureInfo.CurrentUICulture) ?? Name;

    /// <summary>작성자(null = 이 컴퓨터에서 만듦).</summary>
    public AssetAuthor? Author { get; init; }
    /// <summary>SPDX 라이선스(이 컴퓨터에서 만든 것은 null).</summary>
    public string? License { get; init; }
    /// <summary>상점 연결(상점에서 받았거나 올린 에셋, 아니면 null).</summary>
    public AssetStoreLink? Store { get; init; }
    /// <summary>들어온 길(<see cref="AssetSources"/>).</summary>
    public string Source { get; init; } = AssetSources.Local;
    /// <summary>저장소에 들어온(만든·받은) 시각.</summary>
    public DateTime AddedUtc { get; init; }
    public bool Favorite { get; init; }
    /// <summary>미리보기 PNG의 디스크 경로(받은 패키지가 미리보기를 넣었을 때만). 보이는 항목만 늦게 decode한다.</summary>
    public string? PreviewPath { get; init; }
    /// <summary>받은 패키지의 라이선스 문서(<c>LICENSE.txt</c>·<c>LICENSE.md</c>) 경로(없으면 null).</summary>
    public string? LicenseFilePath { get; init; }

    /// <summary>지금 내용의 <see cref="AssetContent.Hash"/>(값 객체마다 한 번 계산).</summary>
    public string ContentHash => AssetContent.HashOf(Kind, Data);

    /// <summary>상점에서 받거나 올린 뒤 고쳤는지.</summary>
    public bool Modified => Store is { } store && store.ContentHash != ContentHash;

    /// <summary>
    /// <paramref name="handle"/>(계정 핸들 — 로그아웃이면 마지막으로 로그인한 핸들)의 것인지: 이 컴퓨터에서 만들었거나 작성자 ID가 같음.
    /// 내 것은 그 자리에서 고치고, 아니면 고칠 때 내 사본을 만든다.
    /// </summary>
    public bool IsOwnedBy(string? handle) => Author is null || (handle is { Length: > 0 } && Author.Id == handle);

    public EffectRecipe? Recipe => Data as EffectRecipe;

    public ScriptAsset? Script => Data as ScriptAsset;

    /// <summary>효과로 넣을 레시피: 효과 레시피는 그대로, 스크립트 에셋은 기본 설정 스크립트 효과 하나(<see cref="ScriptAsset.ToRecipe"/>). 그 밖은 null.</summary>
    public EffectRecipe? EffectTemplate => Recipe ?? Script?.ToRecipe();

    /// <summary>이 에셋이 담은 스크립트 원문 해시(레시피의 원문, 스크립트 에셋 자신). 실행 허락 표시에 쓴다.</summary>
    public IReadOnlyCollection<string> ScriptHashes
        => Recipe is { } recipe ? recipe.Scripts.Keys.ToArray() : Script is { } script ? [script.Hash] : Array.Empty<string>();

    /// <summary>그라데이션 값(<see cref="GradientPreset.Favorite"/>는 이 항목의 즐겨찾기).</summary>
    public GradientPreset? Gradient => Data is GradientAsset g ? g.Gradient with { Favorite = Favorite } : null;
}

/// <summary>
/// 카탈로그(계획 4.5): 저장소의 에셋을 모은 읽기 모델. 저장소가 바뀔 때마다 새로 만들어지는 변하지 않는 값이다.
/// 고르는 목록(효과 창, 그라데이션 메뉴, AI 도구)은 <see cref="Available"/>을 쓴다.
/// </summary>
public sealed class AssetCatalog
{
    public AssetCatalog(IReadOnlyList<AssetEntry> all) => All = all;

    public static AssetCatalog Empty { get; } = new([]);

    /// <summary>모든 에셋(저장소 순서 — 들어온 차례).</summary>
    public IReadOnlyList<AssetEntry> All { get; }

    /// <summary><paramref name="kind"/> 에셋(저장소 순서).</summary>
    public IReadOnlyList<AssetEntry> Available(string kind) => All.Where(entry => entry.Kind == kind).ToArray();

    public IReadOnlyList<AssetEntry> Recipes => Available(AssetKinds.EffectRecipe);

    /// <summary>효과 창 종류 칸에 놓을 에셋: 효과 레시피(프리셋 탭)와 스크립트 효과(효과 탭), 저장소 순서.</summary>
    public IReadOnlyList<AssetEntry> EffectTemplates
        => All.Where(entry => entry.Kind is AssetKinds.EffectRecipe or AssetKinds.EffectScript).ToArray();

    /// <summary>
    /// 넣을 수 있는 스크립트 효과(효과 창 효과 탭·AI 도구의 차례): 공식 효과(<see cref="OfficialEffectPackages"/>)를 예전 기본 효과 차례
    /// (<see cref="Scripting.OfficialEffects.Ordered"/>)로 먼저, 받은 에셋(작성자가 있는 것), 이 컴퓨터에서 만든 것 — 같은 무리 안은 저장소 순서.
    /// </summary>
    public IReadOnlyList<AssetEntry> EffectScripts
    {
        get
        {
            var order = Scripting.OfficialEffects.Ordered.ToList();
            int Rank(AssetEntry entry) => OfficialIdOf(entry) is { } official ? order.IndexOf(official)
                : entry.Author is null ? int.MaxValue : int.MaxValue - 1;
            return Available(AssetKinds.EffectScript).Select((entry, index) => (entry, index)).OrderBy(p => Rank(p.entry)).ThenBy(p => p.index)
                .Select(p => p.entry).ToArray();
        }
    }

    /// <summary>스크립트 효과 에셋이 앱에 든 공식 효과(상점 연결이 공식 패키지의 그 효과)면 그 공식 효과 ID(<c>outline</c>), 아니면 null.</summary>
    public static string? OfficialIdOf(AssetEntry entry)
        => entry.Store is { } store && OfficialEffectPackages.IdOfPackage(store.PackageId) is { } official && store.AssetId == OfficialEffectPackages.AssetId(official)
            ? official : null;

    public IReadOnlyList<AssetEntry> Gradients => Available(AssetKinds.Gradient);

    public AssetEntry? Find(string id) => All.FirstOrDefault(entry => entry.Id == id);

    /// <summary>상점 패키지 <paramref name="packageId"/>에서 온(또는 그 ID로 올린) 에셋.</summary>
    public IReadOnlyList<AssetEntry> FromPackage(string packageId) => All.Where(entry => entry.Store?.PackageId == packageId).ToArray();
}
