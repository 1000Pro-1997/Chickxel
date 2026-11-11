using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace CatalogBuilder;

/// <summary>
/// 앱에 넣지 않는 모양 펜 세트(예전 기본 세트 열다섯 가지 <see cref="FormerBuiltInShapeSets"/>와 크리스마스 세트 셋 <see cref="ChristmasShapeSets"/>)를
/// 로컬 파일로 쓴다(<c>--shape-sets &lt;폴더&gt;</c>): 세트마다 상점에 올릴 수 있는 패키지(<c>chickxel.shapes.&lt;ID&gt;-&lt;버전&gt;.chickxel-asset</c>, MIT,
/// 영어·한국어 이름과 요약, 모양 격자 미리보기, 최소 앱 버전은 모양 펜 세트 종류가 정함)와 바로 가져올 세트 파일(<c>chickxel.shapes.&lt;ID&gt;.chickxel-shapes</c>).
/// 공식 카탈로그(<c>catalog/</c>)에는 넣지 않는다 — 올릴지는 사람이 정한다. 쓴 뒤 앱의 패키지 검사(엄격)와 세트 파일 읽기로 다시 읽어 같은지 본다.
/// </summary>
internal static class LocalShapeSets
{
    public const string PackagePrefix = "chickxel.shapes.";
    public const string Version = "1.0.0";

    /// <summary>세트 하나: ID(패키지 ID의 끝 조각, 에셋 조각), 영어 설명(목록 요약), 한국어 이름·요약, 세트 만들기(영어 이름 = 세트 이름).</summary>
    private sealed record Package(string Id, string Description, string NameKo, string SummaryKo, Func<StampSet> Create)
    {
        public string PackageId => PackagePrefix + Id;

        public AssetManifest Header(StampSet set) => new()
        {
            Id = PackageId, Version = Version, Name = set.DisplayName, Description = Description,
            Translations = new Dictionary<string, AssetTranslation>(StringComparer.OrdinalIgnoreCase) { ["ko"] = new(NameKo, SummaryKo) },
            Author = OfficialEffectPackages.Author, License = OfficialEffectPackages.License,
        };
    }

    private static readonly Package[] Packages =
    [
        new("pine-needles", "Pine needle sprigs and twigs in dark and light green, facing both ways.",
            "솔잎", "진한 초록·초록 솔잎 가지와 잔가지(좌우로 뒤집은 것까지).", FormerBuiltInShapeSets.Pine),
        new("flowers", "Small pink, blue and white flowers with yellow centers, some on a stem.",
            "꽃", "노란 가운데의 분홍·파랑·흰색 작은 꽃(줄기 달린 것 포함).", FormerBuiltInShapeSets.Flowers),
        new("petals", "Tiny pink petals with semi-transparent edges for falling blossoms.",
            "꽃잎", "흩날리는 꽃잎을 그리는 작은 분홍 꽃잎(가장자리가 반투명).", FormerBuiltInShapeSets.Petals),
        new("sparkles", "White and soft yellow twinkles and four-pointed glints with a semi-transparent glow.",
            "반짝이", "흰색·옅은 노랑의 반짝임과 네 갈래 빛(반투명하게 번짐).", FormerBuiltInShapeSets.Sparkles),
        new("dots", "Dark dots from 1 to 6 pixels, stamped in order from small to large.",
            "점", "1~6px 어두운 점, 작은 것부터 차례로 찍습니다.", FormerBuiltInShapeSets.Dots),
        new("clouds", "Fluffy white clouds with a blue-gray outline and a shaded bottom.",
            "구름", "푸른 회색 테두리와 그늘진 밑면의 흰 구름.", FormerBuiltInShapeSets.Clouds),
        new("pebbles", "Gray pebbles in eight sizes, lit from the top left.",
            "돌멩이", "왼쪽 위에서 빛을 받은 여덟 가지 크기의 회색 돌멩이.", FormerBuiltInShapeSets.Pebbles),
        new("bricks", "Red bricks in five sizes and a staggered pair.",
            "벽돌", "다섯 가지 크기의 붉은 벽돌과 엇갈려 쌓은 두 장.", FormerBuiltInShapeSets.Bricks),
        new("raindrops", "Semi-transparent light blue raindrops and slanted rain streaks.",
            "빗방울", "반투명한 옅은 파랑 빗방울과 비스듬한 빗줄기.", FormerBuiltInShapeSets.Raindrops),
        new("snowflakes", "White and pale blue snowflakes and soft snow specks.",
            "눈송이", "흰색·옅은 파랑 눈송이와 작은 눈 알갱이.", FormerBuiltInShapeSets.Snowflakes),
        new("hearts", "Red and pink hearts in four sizes.",
            "하트", "네 가지 크기의 빨강·분홍 하트.", FormerBuiltInShapeSets.Hearts),
        new("smoke-puffs", "Soft semi-transparent gray puffs of smoke and dust.",
            "연기·먼지", "반투명한 회색 연기·먼지 뭉치.", FormerBuiltInShapeSets.SmokePuffs),
        new("wood-grain", "Wavy dark and light wood grain lines and knots for planks.",
            "나뭇결", "널빤지에 찍는 물결치는 나뭇결 줄과 옹이.", FormerBuiltInShapeSets.WoodGrain),
        new("cracks", "Branching dark cracks with a light lip for walls and stone.",
            "금", "벽·돌에 찍는 갈라지는 어두운 금(밝은 가장자리).", FormerBuiltInShapeSets.Cracks),
        new("coins-and-gems", "Gold coins in three sizes and red, green and blue gems.",
            "동전·보석", "세 가지 크기의 금화와 빨강·초록·파랑 보석.", FormerBuiltInShapeSets.CoinsAndGems),
        new("christmas-lights", "String-light bulbs in five colors, glowing bulbs and short light strings for decorating a Christmas tree.",
            "크리스마스 전구", "트리를 꾸미는 다섯 가지 색 전구, 빛나는 전구, 짧은 전구 줄.", ChristmasShapeSets.Lights),
        new("christmas-bells", "Gold and silver bells, double bells with a bow, and red and gold ribbon bows for a Christmas tree.",
            "크리스마스 종·리본", "트리에 다는 금색·은색 종, 리본을 단 쌍종, 빨간색·금색 리본.", ChristmasShapeSets.BellsAndBows),
        new("christmas-ornaments", "Shiny baubles, a tree-top star, a candy cane, gift boxes, a pine cone and a snowflake for a Christmas tree.",
            "크리스마스 장식", "트리에 다는 반짝이는 방울, 꼭대기 별, 지팡이 사탕, 선물 상자, 솔방울, 눈송이.", ChristmasShapeSets.Ornaments),
    ];

    /// <summary><paramref name="directory"/>에 세트마다 패키지와 세트 파일을 쓰고(있으면 덮어씀) 다시 읽어 본다. 문제가 있으면 문제마다 한 줄을 남기고 1.</summary>
    public static int Write(string directory)
    {
        Directory.CreateDirectory(directory);
        int problems = 0;
        foreach (var package in Packages)
        {
            var set = package.Create();
            ShapeSetAssetFormat.Validate(set);
            string packagePath = Path.Combine(directory, $"{package.PackageId}-{Version}{AssetPackage.Extension}");
            string setPath = Path.Combine(directory, package.PackageId + StampLibrary.SetFileExtension);
            File.WriteAllBytes(packagePath, Build(package, set));
            StampLibrary.SaveSet(set, setPath);
            if (Check(package, set, packagePath, setPath) is { } problem)
            {
                Console.Error.WriteLine($"{package.PackageId}: {problem}");
                problems++;
            }
            else Console.WriteLine($"wrote: {Path.GetFileName(packagePath)}, {Path.GetFileName(setPath)} ({set.Shapes.Count} shapes)");
        }
        Console.WriteLine($"{Packages.Length - problems} of {Packages.Length} shape set(s) written to {directory}.");
        return problems == 0 ? 0 : 1;
    }

    // 패키지 바이트(미리보기는 모양 격자, 최소 앱 버전은 모양 펜 세트 종류가 올림)
    private static byte[] Build(Package package, StampSet set)
    {
        var builder = new AssetPackageBuilder(package.Header(set));
        builder.Add(set, segment: package.Id);
        builder.AddDocument("LICENSE.txt", OfficialEffectPackages.LicenseText);
        builder.AddDocument("README.md", $"# {set.DisplayName}\n\n{package.Description} Chickxel shape pen set.\n");
        using var output = new MemoryStream();
        builder.Write(output);
        return output.ToArray();
    }

    // 쓴 파일을 앱처럼 읽어 같은 세트인지(다르면 까닭)
    private static string? Check(Package package, StampSet set, string packagePath, string setPath)
    {
        AssetPackage read;
        try { read = AssetPackageValidator.Read(packagePath); }
        catch (AssetPackageException ex) { return "package rejected: " + string.Join("; ", ex.Issues.Select(issue => issue.Message)); }
        var manifest = read.Manifest;
        if (manifest.Id != package.PackageId || manifest.Version != Version || manifest.MinAppVersion != AssetKinds.ShapeSetMinAppVersion
            || !manifest.Translations.TryGetValue("ko", out var ko) || ko != new AssetTranslation(package.NameKo, package.SummaryKo))
            return "unexpected manifest";
        if (read.Items is not [{ Data: ShapeSetAsset asset }] || asset.Id != $"{package.PackageId}.{package.Id}") return "unexpected assets";
        if (!Same(set, asset.Set)) return "package shape set differs";
        if (StampLibrary.ParseSet(File.ReadAllText(setPath)) is not { } file || !Same(set, file)) return "set file differs";
        return null;
    }

    private static bool Same(StampSet a, StampSet b)
        => a.DisplayName == b.DisplayName && a.Order == b.Order && a.Shapes.Count == b.Shapes.Count
           && a.Shapes.Zip(b.Shapes).All(pair => pair.First.ContentEquals(pair.Second));
}
