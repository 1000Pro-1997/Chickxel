using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Chickxel.Core.IO;

namespace Chickxel.Core.Assets;

/// <summary>검사를 통과한 패키지 에셋 하나: manifest 줄과 읽은 값.</summary>
public sealed record AssetPackageItem(AssetManifestEntry Entry, IAssetData Data);

/// <summary>
/// 검사를 통과한 <c>.chickxel-asset</c>(계획 6.1): manifest, manifest를 뺀 파일 내용(패키지 안 경로 → 바이트), 읽은 에셋.
/// 만들기는 <see cref="AssetPackageValidator"/>(읽기)만 하므로 이 객체가 있으면 모든 검사가 끝난 것이다.
/// </summary>
public sealed class AssetPackage
{
    public const string Extension = ".chickxel-asset";
    /// <summary>앱 이름이 Gpixel이던 때의 패키지 확장자. 내용 형식이 같아 가져오기는 되고, 내보내기는 늘 <see cref="Extension"/>.</summary>
    public const string LegacyExtension = ".gpixel-asset";

    internal AssetPackage(AssetManifest manifest, IReadOnlyDictionary<string, byte[]> files, IReadOnlyList<AssetPackageItem> items,
        IReadOnlyList<AssetIssue>? warnings = null)
    {
        Manifest = manifest;
        Files = files;
        Items = items;
        Warnings = warnings ?? [];
    }

    public AssetManifest Manifest { get; }

    /// <summary>manifest를 뺀 파일(경로 순서 무관).</summary>
    public IReadOnlyDictionary<string, byte[]> Files { get; }

    /// <summary>manifest 순서의 에셋.</summary>
    public IReadOnlyList<AssetPackageItem> Items { get; }

    /// <summary>패키지를 막지 않는 문제(읽지 못해 건너뛴 미리보기 장면 — <see cref="AssetIssueCode.InvalidPreviewScene"/>).</summary>
    public IReadOnlyList<AssetIssue> Warnings { get; }

    /// <summary>같은 패키지 파일 집합인지(manifest의 경로·해시가 모두 같음).</summary>
    public bool SameContent(AssetManifest other)
        => Manifest.Files.Count == other.Files.Count
           && Manifest.Files.All(pair => other.Files.TryGetValue(pair.Key, out var hash) && hash == pair.Value);
}

/// <summary>패키지 ZIP 쓰기: 파일 SHA-256을 계산해 manifest의 <c>files</c>를 채우고 manifest.json을 맨 앞에 둔다.</summary>
public static class AssetPackageWriter
{
    // 같은 내용이면 같은 바이트가 나오도록 ZIP 항목 시각을 고정한다.
    private static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>SHA-256 소문자 16진수.</summary>
    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>
    /// <paramref name="manifest"/>(files는 무시하고 다시 계산)와 <paramref name="files"/>로 패키지를 쓴다.
    /// 돌려주는 manifest는 실제로 쓴 것(files 포함)이다.
    /// </summary>
    public static AssetManifest Write(Stream output, AssetManifest manifest, IReadOnlyDictionary<string, byte[]> files)
    {
        var hashes = files.ToDictionary(pair => pair.Key, pair => Hash(pair.Value), StringComparer.Ordinal);
        var written = manifest with { SchemaVersion = AssetManifest.CurrentSchemaVersion, Files = hashes };
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
        Add(zip, AssetManifest.FileName, Encoding.UTF8.GetBytes(written.Serialize()));
        foreach (var (path, data) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal)) Add(zip, path, data);
        return written;
    }

    private static void Add(ZipArchive zip, string path, byte[] data)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTime;
        entry.ExternalAttributes = 0x81A4 << 16; // 일반 파일 rw-r--r--
        using var stream = entry.Open();
        stream.Write(data);
    }
}

/// <summary>
/// 효과 레시피·그라데이션 값으로 새 패키지를 만든다(내보내기, 5단계 게시). 에셋 ID는 "패키지 ID.이름 조각"으로 새로 붙이고,
/// 파일은 종류 폴더(<see cref="AssetKindDefinition.Folder"/>)와 <c>previews/</c>에 둔다.
/// </summary>
public sealed class AssetPackageBuilder(AssetManifest header)
{
    private readonly List<AssetManifestEntry> _entries = [];
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _segments = new(StringComparer.Ordinal);

    /// <summary>패키지 정보(ID, 버전, 이름, 작성자, 라이선스, 최소 앱 버전). assets와 files는 무시한다.</summary>
    public AssetManifest Header { get; } = header;

    public int Count => _entries.Count;

    /// <summary>
    /// 에셋 하나를 더하고 새 에셋 ID를 돌려준다. <paramref name="preview"/>가 있으면 <c>previews/&lt;조각&gt;.png</c>를 미리보기로 넣고,
    /// 효과 없는 원본이 있으면 <see cref="OriginalPreviewPath"/> 자리에 같이 넣는다(manifest는 가리키지 않음 — 웹 비교용).
    /// 미리보기 장면이 있는 스크립트 에셋(<see cref="ScriptAsset.Scene"/>)은 그 저장 PNG를 <see cref="ScenePath"/> 자리에 넣는다(manifest는 가리키지 않음).
    /// <paramref name="segment"/>를 주면 이름 대신 그 조각을 ID·파일 이름에 쓴다(공식 패키지처럼 ID를 정해 둘 때 — 소문자·숫자·하이픈).
    /// </summary>
    public string Add(string kind, IAssetData value, PackagePreview? preview = null, string? segment = null)
    {
        var definition = AssetKinds.Find(kind) ?? throw new ArgumentException(kind, nameof(kind));
        segment = UniqueSegment(segment is null ? Slug(value.Name, kind) : Slug(segment, kind));
        string id = Header.Id + "." + segment;
        string path = definition.Folder + segment + definition.Extensions[0];
        _files[path] = definition.Write(value, id).ToArray();
        string? previewPath = null;
        if (preview is { Png.Length: > 0 })
        {
            previewPath = "previews/" + segment + ".png";
            _files[previewPath] = preview.Png;
            if (preview.OriginalPng is { Length: > 0 } original) _files[OriginalPreviewPath(previewPath)] = original;
        }
        if (value is ScriptAsset { Scene: { } scene }) _files[ScenePath(path)] = scene.ToPng();
        _entries.Add(new AssetManifestEntry(id, kind, path, previewPath));
        return id;
    }

    public string Add(EffectRecipe recipe, PackagePreview? preview = null) => Add(AssetKinds.EffectRecipe, recipe, preview);

    public string Add(GradientPreset gradient, PackagePreview? preview = null) => Add(AssetKinds.Gradient, new GradientAsset("", gradient), preview);

    /// <summary>미리보기 <c>previews/&lt;이름&gt;.png</c>의 효과 없는 원본 자리 <c>previews/&lt;이름&gt;.original.png</c>(서버가 이 이름으로 찾는다).</summary>
    public static string OriginalPreviewPath(string previewPath) => previewPath[..^".png".Length] + ".original.png";

    /// <summary>
    /// 에셋 파일 <paramref name="assetPath"/>(<c>assets/scripts/&lt;조각&gt;.js</c>)의 미리보기 장면 자리 <c>previews/&lt;조각&gt;.scene.png</c>
    /// (<see cref="PreviewScene.ToPng"/>). 예전 앱에는 쓰이지 않는 평범한 미리보기 PNG다.
    /// </summary>
    public static string ScenePath(string assetPath)
    {
        string name = assetPath[(assetPath.LastIndexOf('/') + 1)..];
        int dot = name.LastIndexOf('.');
        return AssetPackageValidator.PreviewFolder + (dot > 0 ? name[..dot] : name) + ".scene.png";
    }

    /// <summary>뿌리 문서(<c>LICENSE.txt</c>, <c>README.md</c>).</summary>
    public void AddDocument(string name, string text) => _files[name] = Encoding.UTF8.GetBytes(text);

    public AssetManifest Write(Stream output)
        => AssetPackageWriter.Write(output, Header with { Assets = _entries.ToArray() }, _files);

    private string UniqueSegment(string slug)
    {
        string segment = slug;
        for (int n = 2; !_segments.Add(segment); n++) segment = $"{slug}-{n}";
        return segment;
    }

    /// <summary>
    /// 이름에서 ID 조각을 만든다: ASCII 글자·숫자는 소문자로, 나머지는 하이픈 하나로. 남는 것이 없으면(한국어 이름 등) 종류 이름.
    /// 번역 표시 이름을 ID로 쓰지 않도록 한 번 정한 ID는 이름을 바꿔도 그대로 둔다.
    /// </summary>
    public static string Slug(string name, string fallback)
    {
        var sb = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            if (sb.Length >= 48) break;
        }
        string slug = sb.ToString().Trim('-');
        return slug.Length > 0 ? slug : fallback;
    }
}
