using Chickxel.Core.IO;
using Chickxel.Core.Model;
using static Chickxel.Core.IO.ShapeArt;

namespace Chickxel.Core.Assets;

/// <summary>
/// 공식 모양 펜 세트 패키지 하나(<c>chickxel.shapes.&lt;ID&gt;</c>, 작성자 <c>chickxel</c>, MIT): ID(패키지 ID의 끝 조각 = 에셋 조각), 버전, 영어 설명(목록 요약),
/// 한국어 이름·요약, 세트 만들기(영어 이름 = 세트 이름), 목록 꼬리표. 앱에 든 기본 세트(<see cref="OfficialShapeSets"/>)와 <c>tools/CatalogBuilder</c>가 상점에 올리는
/// 다른 공식 세트가 같이 쓴다. 같은 코드·버전이면 같은 바이트다.
/// </summary>
public sealed record OfficialShapeSetPackage(string Id, string Version, string Description, string NameKo, string SummaryKo, Func<StampSet> Create, IReadOnlyList<string> Tags)
{
    public const string Prefix = "chickxel.shapes.";

    public string PackageId => Prefix + Id;

    public string AssetId => PackageId + "." + Id;

    /// <summary>패키지 정보(에셋·파일 목록 없음 — 최소 앱 버전은 모양 펜 세트 종류가 올림).</summary>
    public AssetManifest Header(StampSet set) => new()
    {
        Id = PackageId, Version = Version, Name = set.DisplayName, Description = Description,
        Translations = new Dictionary<string, AssetTranslation>(StringComparer.OrdinalIgnoreCase) { ["ko"] = new(NameKo, SummaryKo) },
        Author = OfficialEffectPackages.Author, License = OfficialEffectPackages.License,
    };

    /// <summary>패키지 바이트(<c>.chickxel-asset</c>, 미리보기는 모양 격자).</summary>
    public byte[] Build() => Build(Create());

    /// <summary><paramref name="set"/>(<see cref="Create"/>로 만든 세트)의 패키지 바이트.</summary>
    public byte[] Build(StampSet set)
    {
        var builder = new AssetPackageBuilder(Header(set));
        builder.Add(set, segment: Id);
        builder.AddDocument("LICENSE.txt", OfficialEffectPackages.LicenseText);
        builder.AddDocument("README.md", $"# {set.DisplayName}\n\n{Description} Official Chickxel shape pen set.\n");
        using var output = new MemoryStream();
        builder.Write(output);
        return output.ToArray();
    }
}

/// <summary>
/// 모양 펜 기본 세트 다섯 가지: 꽃·반짝이·비눗방울·단풍잎·나뭇잎. 공식 패키지(<see cref="OfficialShapeSetPackage"/>)로 상점에도 있고, 앱은 처음 실행에 받은 세트로
/// 넣는다(<see cref="BundledPackages"/> — 지우면 "기본 세트 되돌리기"로 다시 넣음). 세트 모음 파일(<see cref="StampLibrary"/>)에는 들지 않는다.
/// 예전 판이 세트 모음에 넣어 둔 기본 세트(<see cref="StampBuiltIns.RetiredIds"/>)는 고치지 않았으면 이 세트와 같아 읽을 때 빼고(<see cref="IsUnchangedCopy"/>),
/// 그 이름표를 고른 설정은 이 세트의 에셋 ID로 잇는다(<see cref="AssetIdOfRetired"/>).
/// </summary>
public static class OfficialShapeSets
{
    /// <summary>기본 세트(넣는 차례 = 받은 세트 목록 차례). 버전을 올리면 고치지 않은 설치본이 업데이트된다.</summary>
    public static IReadOnlyList<OfficialShapeSetPackage> Packages { get; } =
    [
        new("flowers", "1.0.0", "Small pink, blue and white flowers with yellow centers, some on a stem.",
            "꽃", "노란 가운데의 분홍·파랑·흰색 작은 꽃(줄기 달린 것 포함).", Flowers, ["flowers", "nature", "spring", "official"]),
        new("sparkles", "1.0.0", "White and soft yellow twinkles and four-pointed glints with a semi-transparent glow.",
            "반짝이", "흰색·옅은 노랑의 반짝임과 네 갈래 빛(반투명하게 번짐).", Sparkles, ["sparkles", "light", "magic", "official"]),
        new("bubbles", "1.0.0", "Soap bubbles in six sizes: a light blue rim, a clear inside and a white glint.",
            "비눗방울", "여섯 가지 크기의 비눗방울: 옅은 파랑 테두리, 맑은 속, 흰 반짝임.", Bubbles, ["bubbles", "water", "official"]),
        new("autumn-leaves", "1.0.0", "Orange, red and yellow autumn leaves for fallen-leaf ground and trees.",
            "단풍잎", "주황·빨강·노랑 단풍잎(낙엽 깔린 땅, 가을 나무).", AutumnLeaves, ["leaves", "autumn", "nature", "official"]),
        new("leaves", "1.0.0", "Green leaves in four shapes, facing both ways, for trees, bushes and ground.",
            "나뭇잎", "좌우로 뒤집은 것까지 네 가지 모양의 초록 나뭇잎(나무, 덤불, 땅).", Leaves, ["leaves", "nature", "official"]),
    ];

    /// <summary>앱에 든 기본 세트 패키지(<see cref="Packages"/> 차례).</summary>
    public static IEnumerable<BundledPackage> Bundled
        => Packages.Select(p => new BundledPackage(AssetKinds.ShapeSet, p.PackageId, p.Version, p.Build, entry => entry.Kind == AssetKinds.ShapeSet));

    /// <summary>받은 세트 목록에서의 차례(기본 세트가 먼저 — 아니면 <see cref="int.MaxValue"/>).</summary>
    public static int OrderOf(string assetId)
    {
        for (int i = 0; i < Packages.Count; i++)
            if (Packages[i].AssetId == assetId) return i;
        return int.MaxValue;
    }

    // 예전 판의 기본 세트 이름표 → 같은 그림의 공식 세트
    private static OfficialShapeSetPackage? OfRetired(string id) => id switch
    {
        "flowers" => Packages[0],
        "sparkles" => Packages[1],
        "bubbles" => Packages[2],
        "autumn" => Packages[3],
        "leaves" => Packages[4],
        _ => null,
    };

    /// <summary>예전 판의 기본 세트 이름표(설정에 남은 고른 세트)를 같은 그림의 공식 세트 에셋 ID로(아니면 null).</summary>
    public static string? AssetIdOfRetired(string? id) => id is null ? null : OfRetired(id)?.AssetId;

    /// <summary>예전 판이 세트 모음에 넣어 둔 기본 세트가 고치지 않은 채 공식 세트와 같은지(이름 없음, 같은 순서·모양).</summary>
    public static bool IsUnchangedCopy(StampSet set)
    {
        if (set.Name is not null || OfRetired(set.Id) is not { } official) return false;
        var original = official.Create();
        return set.Order == original.Order && set.Shapes.Count == original.Shapes.Count
               && set.Shapes.Zip(original.Shapes).All(pair => pair.First.ContentEquals(pair.Second));
    }

    // ───────────── 그림 ─────────────

    private static StampSet Flowers() => Set("Flowers", FlowerColors.SelectMany(palette => FlowerShapes.Select(rows => Art(rows, palette))));

    private static StampSet Sparkles() => Set("Sparkles", SparkleShapes());

    private static StampSet Bubbles() => Set("Bubbles", BubbleShapes());

    private static StampSet AutumnLeaves() => Set("Autumn Leaves", LeafAutumn.SelectMany(palette => LeafShapes.Take(3).Select(rows => Art(rows, palette))));

    private static StampSet Leaves() => Set("Leaves", Mirrored(LeafShapes.Select(rows => Art(rows, LeafGreen))));

    private static readonly Dictionary<char, uint>[] FlowerColors =
    [
        new() { ['p'] = 0xFFE86A92, ['q'] = 0xFFF8B4C8, ['y'] = 0xFFF5D545, ['g'] = 0xFF4C9A3A },
        new() { ['p'] = 0xFF5B6EE1, ['q'] = 0xFFA8B8FF, ['y'] = 0xFFFFF1A8, ['g'] = 0xFF4C9A3A },
        new() { ['p'] = 0xFFD8D8E4, ['q'] = 0xFFFFFFFF, ['y'] = 0xFFF5B83A, ['g'] = 0xFF4C9A3A },
    ];

    private static readonly string[][] FlowerShapes =
    [
        [
            "..q..",
            ".qpq.",
            "qpypq",
            ".qpq.",
            "..g..",
            ".gg..",
            "..g..",
        ],
        [
            ".pp.pp.",
            "pqqpqqp",
            "pqqyqqp",
            ".pyyyp.",
            "pqqyqqp",
            "pqqpqqp",
            ".pp.pp.",
        ],
        [
            ".y.",
            "yqy",
            ".y.",
        ],
    ];

    private static IEnumerable<PixelImage> SparkleShapes()
    {
        var hand = new Dictionary<char, uint> { ['w'] = 0xFFFFFFFF, ['S'] = 0xFFFFF6C8, ['h'] = 0xA0FFE060, ['H'] = 0x46FFE060 };
        yield return Art(["...H...", "...h...", "..hwh..", "Hhwwwhh", "..hwh..", "...h...", "...H..."], hand);
        yield return Art(["..H..", ".HwH.", "HwSwH", ".HwH.", "..H.."], hand);
        // 네 갈래 반짝이: 가운데 흰색, 팔은 끝으로 갈수록 옅은 노랑
        foreach (int arm in new[] { 1, 2, 3, 4 })
        {
            int size = arm * 2 + 1;
            yield return Paint(size, size, (x, y) =>
            {
                int dx = Math.Abs(x - arm), dy = Math.Abs(y - arm);
                if (dx == 0 && dy == 0) return Color32.White;
                if (dx != 0 && dy != 0) return dx == 1 && dy == 1 && arm >= 3 ? new Color32(255, 224, 96, 60) : Color32.Transparent;
                int d = dx + dy;
                return d == 1 && arm >= 2 ? new Color32(255, 246, 200) : new Color32(255, 224, 96, (byte)(220 - 150 * (d - 1) / Math.Max(1, arm - 1)));
            });
        }
    }

    private static IEnumerable<PixelImage> BubbleShapes()
    {
        foreach (int size in new[] { 3, 5, 6, 7, 9, 11 })
        {
            bool Inside(int x, int y) => x >= 0 && y >= 0 && x < size && y < size && InCircle(size, x, y);
            int hx = size / 4 + (size >= 6 ? 1 : 0), hy = hx;
            yield return Paint(size, size, (x, y) =>
            {
                if (!Inside(x, y)) return Color32.Transparent;
                if (Edge(Inside, x, y)) return new Color32(0xB8, 0xE4, 0xFF, 210);
                if (x == hx && y == hy || size >= 9 && x == hx + 1 && y == hy) return new Color32(255, 255, 255, 235);
                return new Color32(0xC8, 0xEC, 0xFF, 50);
            });
        }
    }

    private static readonly Dictionary<char, uint> LeafGreen = new() { ['o'] = 0xFF2B5329, ['g'] = 0xFF4C9A3A, ['l'] = 0xFF8BCB5A, ['s'] = 0xFF6B4A2A };

    private static readonly Dictionary<char, uint>[] LeafAutumn =
    [
        new() { ['o'] = 0xFF8A3A12, ['g'] = 0xFFE07A2A, ['l'] = 0xFFF6B24A, ['s'] = 0xFF6B4A2A },
        new() { ['o'] = 0xFF6E1A1A, ['g'] = 0xFFC0392B, ['l'] = 0xFFE8704A, ['s'] = 0xFF6B4A2A },
        new() { ['o'] = 0xFF8A6A12, ['g'] = 0xFFE8C33A, ['l'] = 0xFFFFE88A, ['s'] = 0xFF6B4A2A },
    ];

    private static readonly string[][] LeafShapes =
    [
        [
            "....oooo",
            "..oogglo",
            ".oggglgo",
            ".ogglggo",
            ".oglggo.",
            ".olggo..",
            ".sooo...",
            "s.......",
        ],
        [
            "..o..",
            ".olo.",
            "oglgo",
            "oglgo",
            "oglgo",
            ".olo.",
            "..o..",
            "..s..",
        ],
        [
            "oooo....",
            "olggoo..",
            "oglgggo.",
            "oggglggo",
            ".ooggglo",
            "...oooos",
            ".......s",
        ],
        [
            ".oo.oo.",
            "oggoggo",
            "oglglgo",
            ".oglgo.",
            "..olo..",
            "...s...",
            "...s...",
        ],
    ];
}
