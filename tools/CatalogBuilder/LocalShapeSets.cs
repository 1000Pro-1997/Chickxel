using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace CatalogBuilder;

/// <summary>
/// 상점에 올리는 공식 모양 펜 세트(<see cref="OfficialShapeSetPackage"/>)를 로컬 파일로 쓴다(<c>--shape-sets &lt;폴더&gt;</c>): 앱에 든 기본 세트 다섯 가지
/// (<see cref="OfficialShapeSets"/>)와 앱에 넣지 않는 세트(예전 기본 세트 <see cref="FormerBuiltInShapeSets"/>, 크리스마스 트리 장식 <see cref="ChristmasShapeSets"/>,
/// <c>ShapeSets/*.cs</c>의 테마 세트). 세트마다 패키지(<c>chickxel.shapes.&lt;ID&gt;-&lt;버전&gt;.chickxel-asset</c>)와 바로 가져올 세트 파일
/// (<c>chickxel.shapes.&lt;ID&gt;.chickxel-shapes</c>), 그리고 서버의 공식 패키지 올리기(<c>Chickxel-server/scripts/import-official.mjs --catalog &lt;폴더&gt;</c>)가 읽는
/// 목록 <c>index.json</c>(정적 카탈로그 쪽과 같은 모양 — ID·버전·요약·꼬리표·패키지 파일)을 쓴다. 공식 카탈로그(<c>catalog/</c>)에는 넣지 않는다.
/// 쓴 뒤 앱의 패키지 검사(엄격)와 세트 파일 읽기로 다시 읽어 같은지 본다. 기본 세트 다섯 가지의 패키지는 앱이 설치하는 바이트와 같다.
/// </summary>
internal static class LocalShapeSets
{
    private static OfficialShapeSetPackage P(string id, string description, string nameKo, string summaryKo, Func<StampSet> create, params string[] tags)
        => new(id, "1.0.0", description, nameKo, summaryKo, create, [.. tags, "official"]);

    /// <summary>올리는 세트 모두(기본 세트 다섯 가지가 먼저).</summary>
    public static IReadOnlyList<OfficialShapeSetPackage> Packages { get; } =
    [
        .. OfficialShapeSets.Packages,
        P("pine-needles", "Pine needle sprigs and twigs in dark and light green, facing both ways.",
            "솔잎", "진한 초록·초록 솔잎 가지와 잔가지(좌우로 뒤집은 것까지).", FormerBuiltInShapeSets.Pine, "nature", "forest", "leaves"),
        P("grass", "Grass tufts with light blade tips, facing both ways, for meadows and ground edges.",
            "풀", "끝이 밝은 풀 포기(좌우로 뒤집은 것까지) — 풀밭, 땅 가장자리.", FormerBuiltInShapeSets.Grass, "nature", "grass", "ground"),
        P("petals", "Tiny pink petals with semi-transparent edges for falling blossoms.",
            "꽃잎", "흩날리는 꽃잎을 그리는 작은 분홍 꽃잎(가장자리가 반투명).", FormerBuiltInShapeSets.Petals, "nature", "spring", "flowers"),
        P("stars", "Gold and pale blue five-pointed stars and small twinkles in three sizes.",
            "별", "세 가지 크기의 금색·옅은 파랑 오각 별과 작은 반짝임.", FormerBuiltInShapeSets.Stars, "sky", "night", "stars"),
        P("dots", "Dark dots from 1 to 6 pixels, stamped in order from small to large.",
            "점", "1~6px 어두운 점, 작은 것부터 차례로 찍습니다.", FormerBuiltInShapeSets.Dots, "texture", "dots"),
        P("clouds", "Fluffy white clouds with a blue-gray outline and a shaded bottom.",
            "구름", "푸른 회색 테두리와 그늘진 밑면의 흰 구름.", FormerBuiltInShapeSets.Clouds, "sky", "weather"),
        P("pebbles", "Gray pebbles in eight sizes, lit from the top left.",
            "돌멩이", "왼쪽 위에서 빛을 받은 여덟 가지 크기의 회색 돌멩이.", FormerBuiltInShapeSets.Pebbles, "nature", "ground", "rocks"),
        P("raindrops", "Semi-transparent light blue raindrops and slanted rain streaks.",
            "빗방울", "반투명한 옅은 파랑 빗방울과 비스듬한 빗줄기.", FormerBuiltInShapeSets.Raindrops, "weather", "rain", "water"),
        P("snowflakes", "White and pale blue snowflakes and soft snow specks.",
            "눈송이", "흰색·옅은 파랑 눈송이와 작은 눈 알갱이.", FormerBuiltInShapeSets.Snowflakes, "weather", "winter", "snow"),
        P("hearts", "Red and pink hearts in four sizes.",
            "하트", "네 가지 크기의 빨강·분홍 하트.", FormerBuiltInShapeSets.Hearts, "love", "decoration"),
        P("smoke-puffs", "Soft semi-transparent gray puffs of smoke and dust.",
            "연기·먼지", "반투명한 회색 연기·먼지 뭉치.", FormerBuiltInShapeSets.SmokePuffs, "smoke", "dust", "effects"),
        P("wood-grain", "Wavy dark and light wood grain lines and knots for planks.",
            "나뭇결", "널빤지에 찍는 물결치는 나뭇결 줄과 옹이.", FormerBuiltInShapeSets.WoodGrain, "texture", "wood"),
        P("cracks", "Branching dark cracks with a light lip for walls and stone.",
            "금", "벽·돌에 찍는 갈라지는 어두운 금(밝은 가장자리).", FormerBuiltInShapeSets.Cracks, "texture", "damage", "stone"),
        P("coins-and-gems", "Gold coins in three sizes and red, green and blue gems.",
            "동전·보석", "세 가지 크기의 금화와 빨강·초록·파랑 보석.", FormerBuiltInShapeSets.CoinsAndGems, "treasure", "items", "game"),
        P("christmas-lights", "String-light bulbs in five colors, glowing bulbs and short light strings for decorating a Christmas tree.",
            "크리스마스 전구", "트리를 꾸미는 다섯 가지 색 전구, 빛나는 전구, 짧은 전구 줄.", ChristmasShapeSets.Lights, "christmas", "decoration", "lights"),
        P("christmas-bells", "Gold and silver bells, double bells with a bow, and red and gold ribbon bows for a Christmas tree.",
            "크리스마스 종·리본", "트리에 다는 금색·은색 종, 리본을 단 쌍종, 빨간색·금색 리본.", ChristmasShapeSets.BellsAndBows, "christmas", "decoration"),
        P("christmas-ornaments", "Shiny baubles, a tree-top star, a candy cane, gift boxes, a pine cone and a snowflake for a Christmas tree.",
            "크리스마스 장식", "트리에 다는 반짝이는 방울, 꼭대기 별, 지팡이 사탕, 선물 상자, 솔방울, 눈송이.", ChristmasShapeSets.Ornaments, "christmas", "decoration"),
        .. ThemeShapeSets.Packages,
    ];

    /// <summary><paramref name="directory"/>에 세트마다 패키지와 세트 파일, 올리기 목록(<c>index.json</c>)을 쓰고(있으면 덮어씀) 다시 읽어 본다. 문제가 있으면 문제마다 한 줄을 남기고 1.</summary>
    public static int Write(string directory)
    {
        Directory.CreateDirectory(directory);
        int problems = 0;
        var listed = new JsonArray();
        foreach (var dup in Packages.GroupBy(p => p.Id).Where(g => g.Count() > 1))
        {
            Console.Error.WriteLine($"{dup.Key}: listed {dup.Count()} times");
            problems++;
        }
        foreach (var package in Packages)
        {
            var set = package.Create();
            ShapeSetAssetFormat.Validate(set);
            string packageFile = $"{package.PackageId}-{package.Version}{AssetPackage.Extension}";
            string packagePath = Path.Combine(directory, packageFile);
            string setPath = Path.Combine(directory, package.PackageId + StampLibrary.SetFileExtension);
            File.WriteAllBytes(packagePath, package.Build(set));
            StampLibrary.SaveSet(set, setPath);
            if (Check(package, set, packagePath, setPath) is { } problem)
            {
                Console.Error.WriteLine($"{package.PackageId}: {problem}");
                problems++;
                continue;
            }
            listed.Add(new JsonObject
            {
                ["id"] = package.PackageId, ["version"] = package.Version, ["name"] = set.DisplayName, ["summary"] = package.Description,
                ["tags"] = new JsonArray(package.Tags.Take(8).Select(tag => (JsonNode)JsonValue.Create(tag)!).ToArray()),
                ["kinds"] = new JsonObject { [AssetKinds.ShapeSet] = 1 },
                ["package"] = new JsonObject { ["url"] = packageFile },
            });
            Console.WriteLine($"wrote: {packageFile}, {Path.GetFileName(setPath)} ({set.Shapes.Count} shapes)");
        }
        var index = new JsonObject
        {
            ["schemaVersion"] = 1, ["page"] = 1, ["pageCount"] = 1, ["totalCount"] = listed.Count, ["next"] = null, ["packages"] = listed,
        };
        File.WriteAllText(Path.Combine(directory, "index.json"), index.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Packages.Count - problems} of {Packages.Count} shape set(s) written to {directory} (index.json lists them for upload).");
        return problems == 0 ? 0 : 1;
    }

    // 쓴 파일을 앱처럼 읽어 같은 세트인지(다르면 까닭)
    private static string? Check(OfficialShapeSetPackage package, StampSet set, string packagePath, string setPath)
    {
        AssetPackage read;
        try { read = AssetPackageValidator.Read(packagePath); }
        catch (AssetPackageException ex) { return "package rejected: " + string.Join("; ", ex.Issues.Select(issue => issue.Message)); }
        var manifest = read.Manifest;
        if (manifest.Id != package.PackageId || manifest.Version != package.Version || manifest.MinAppVersion != AssetKinds.ShapeSetMinAppVersion
            || !manifest.Translations.TryGetValue("ko", out var ko) || ko != new AssetTranslation(package.NameKo, package.SummaryKo))
            return "unexpected manifest";
        if (read.Items is not [{ Data: ShapeSetAsset asset }] || asset.Id != package.AssetId) return "unexpected assets";
        if (!Same(set, asset.Set)) return "package shape set differs";
        if (StampLibrary.ParseSet(File.ReadAllText(setPath)) is not { } file || !Same(set, file)) return "set file differs";
        if (package.Description.Length > 200 || package.SummaryKo.Length > 200) return "summary longer than 200 characters";
        return null;
    }

    private static bool Same(StampSet a, StampSet b)
        => a.DisplayName == b.DisplayName && a.Order == b.Order && a.Shapes.Count == b.Shapes.Count
           && a.Shapes.Zip(b.Shapes).All(pair => pair.First.ContentEquals(pair.Second));
}
