using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace CatalogBuilder;

/// <summary>
/// 공식 모양 펜 세트 패키지(<c>catalog/chickxel.shapes.&lt;ID&gt;/</c>, 그림은 <see cref="ChristmasShapeSets"/>). 공식 효과 패키지(<see cref="OfficialEffectCatalog"/>)와 같은 규칙:
/// 지금 버전의 패키지가 없으면 만들어 쓰고(예전 버전 파일은 지움, <c>review.json</c>의 <c>published</c>는 처음 날짜 그대로), 있으면 바꾸지 않는다.
/// 있는 패키지의 이름·설명·번역·세트 내용이 지금 선언과 다르면(그림을 고치고 버전을 올리지 않음) 실패한다. 미리보기는 압축 바이트가 기계마다 다를 수 있어 비교하지 않는다.
/// </summary>
internal static class OfficialShapeSets
{
    public const string PackagePrefix = "chickxel.shapes.";

    /// <summary>공식 세트 하나: ID(패키지 ID의 끝 조각, 에셋 조각), 버전, 영어 이름·설명(목록 요약), 한국어 이름·요약, 꼬리표, 세트 만들기.</summary>
    private sealed record Package(string Id, string Version, string Name, string Description, string NameKo, string SummaryKo, string[] Tags, Func<StampSet> Create)
    {
        public string PackageId => PackagePrefix + Id;

        public string AssetId => PackageId + "." + Id;

        public AssetManifest Header => new()
        {
            Id = PackageId, Version = Version, Name = Name, Description = Description,
            Translations = new Dictionary<string, AssetTranslation>(StringComparer.OrdinalIgnoreCase) { ["ko"] = new(NameKo, SummaryKo) },
            Author = OfficialEffectPackages.Author, License = OfficialEffectPackages.License, MinAppVersion = AssetKinds.ShapeSetMinAppVersion,
        };
    }

    private static readonly Package[] Packages =
    [
        new("christmas-lights", "1.0.0", "Christmas Lights",
            "String-light bulbs in five colors, glowing bulbs and short light strings for decorating a Christmas tree.",
            "크리스마스 전구", "트리를 꾸미는 다섯 가지 색 전구, 빛나는 전구, 짧은 전구 줄.",
            ["shape-set", "christmas", "official"], ChristmasShapeSets.Lights),
        new("christmas-bells", "1.0.0", "Christmas Bells & Bows",
            "Gold and silver bells, double bells with a bow, and red and gold ribbon bows for a Christmas tree.",
            "크리스마스 종·리본", "트리에 다는 금색·은색 종, 리본을 단 쌍종, 빨간색·금색 리본.",
            ["shape-set", "christmas", "official"], ChristmasShapeSets.BellsAndBows),
        new("christmas-ornaments", "1.0.0", "Christmas Ornaments",
            "Shiny baubles, a tree-top star, a candy cane, gift boxes, a pine cone and a snowflake for a Christmas tree.",
            "크리스마스 장식", "트리에 다는 반짝이는 방울, 꼭대기 별, 지팡이 사탕, 선물 상자, 솔방울, 눈송이.",
            ["shape-set", "christmas", "official"], ChristmasShapeSets.Ornaments),
    ];

    /// <summary>문제(사람이 읽는 한 줄)와 쓸 파일(catalog/ 기준 경로 → 바이트, null = 지움). 쓰기는 <see cref="OfficialEffectCatalog.Apply"/>.</summary>
    public static (List<string> Problems, Dictionary<string, byte[]?> Writes) Plan(string catalogDirectory)
    {
        var problems = new List<string>();
        var writes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var package in Packages)
        {
            string folder = Path.Combine(catalogDirectory, package.PackageId);
            string fileName = $"{package.PackageId}-{package.Version}{AssetPackage.Extension}";
            string path = Path.Combine(folder, fileName);
            var set = package.Create();
            if (File.Exists(path))
            {
                if (Compare(path, package, set) is { } problem)
                    problems.Add($"{package.PackageId}/{fileName}: {problem} — raise its version in OfficialShapeSets and run CatalogBuilder");
                continue;
            }
            // 새 버전: 패키지를 만들고 예전 버전 파일을 지운다
            writes[$"{package.PackageId}/{fileName}"] = Build(package, set);
            if (Directory.Exists(folder))
                foreach (var old in Directory.GetFiles(folder, "*" + AssetPackage.Extension))
                    writes[$"{package.PackageId}/{Path.GetFileName(old)}"] = null;
            writes[$"{package.PackageId}/review.json"] = OfficialEffectCatalog.Review(Path.Combine(folder, "review.json"), package.Description, package.Tags);
        }
        return (problems, writes);
    }

    // 패키지 바이트(미리보기는 모양 격자)
    private static byte[] Build(Package package, StampSet set)
    {
        ShapeSetAssetFormat.Validate(set);
        var builder = new AssetPackageBuilder(package.Header);
        builder.Add(set, segment: package.Id);
        builder.AddDocument("LICENSE.txt", OfficialEffectPackages.LicenseText);
        builder.AddDocument("README.md", $"# {package.Name}\n\n{package.Description} Official Chickxel shape pen set.\n");
        using var output = new MemoryStream();
        builder.Write(output);
        return output.ToArray();
    }

    // 있는 패키지가 지금 선언과 같은지(다르면 까닭)
    private static string? Compare(string path, Package package, StampSet set)
    {
        using var zip = ZipFile.OpenRead(path);
        string Text(string name)
        {
            using var reader = new StreamReader(zip.GetEntry(name)?.Open() ?? throw new InvalidDataException(name), Encoding.UTF8);
            return reader.ReadToEnd();
        }
        var issues = new List<AssetIssue>();
        var manifest = AssetManifest.Read(JsonNode.Parse(Text(AssetManifest.FileName)), issues);
        if (manifest is null) return "unreadable manifest";
        var header = package.Header;
        if (manifest.Name != header.Name || manifest.Description != header.Description || manifest.MinAppVersion != header.MinAppVersion
            || manifest.Translations.Count != header.Translations.Count
            || header.Translations.Any(pair => !manifest.Translations.TryGetValue(pair.Key, out var t) || t != pair.Value))
            return "name, description, translations or minAppVersion changed";
        if (manifest.Assets is not [{ Kind: AssetKinds.ShapeSet } asset] || asset.Id != package.AssetId)
            return "unexpected assets";
        var published = StampLibrary.ParseSet(Text(asset.Path));
        if (published is null) return "unreadable shape set";
        bool same = published.Name == set.Name && published.Order == set.Order && published.Shapes.Count == set.Shapes.Count
                    && published.Shapes.Zip(set.Shapes).All(pair => pair.First.ContentEquals(pair.Second));
        return same ? null : "shape set changed";
    }
}
