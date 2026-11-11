using Chickxel.Core.Edit;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// 모양 펜 기본 세트 다섯 가지(코드에 고정 — 지워도 <see cref="StampLibrary.RestoreDefaults"/>로 되돌림): 나뭇잎·단풍잎·풀·별·거품. 손으로 그린 글자 그림과
/// 원으로 만든 그림을 섞는다. 모양은 모두 투명한 둘레를 잘라 담는다. 예전 판의 다른 기본 세트는 <see cref="RetiredIds"/>(이제 사용자 세트).
/// </summary>
public static class StampBuiltIns
{
    public static readonly IReadOnlyList<string> Ids = ["leaves", "autumn", "grass", "stars", "bubbles"];

    /// <summary>처음 판의 기본 세트 — 알던 기본 세트를 적지 않은 예전 파일은 이것만 알던 것으로 본다(나머지는 새로 넣음).</summary>
    public static readonly IReadOnlyList<string> FirstIds = ["leaves", "grass", "flowers", "sparkles", "dots"];

    /// <summary>
    /// 예전 판(1.14.117~1.14.120)의 기본 세트였다가 빠진 세트 이름표. 예전 세트 파일에 남은 이 세트는 사용자 세트로 남기고, 이름이 없으면
    /// <see cref="RetiredName"/>을 붙인다(<see cref="StampLibrary.Parse"/>). 같은 이름표를 새 기본 세트에 다시 쓰지 않는다.
    /// </summary>
    public static readonly IReadOnlyList<string> RetiredIds =
    [
        "pine", "flowers", "petals", "sparkles", "dots", "clouds", "rocks", "bricks", "rain", "snow", "hearts", "puffs", "wood", "cracks", "treasure",
    ];

    /// <summary>기본 세트 이름(지금 언어). 기본 세트가 아니면 null.</summary>
    public static string? Name(string id) => id switch
    {
        "leaves" => DomainStrings.StampSet_Leaves,
        "autumn" => DomainStrings.StampSet_AutumnLeaves,
        "grass" => DomainStrings.StampSet_Grass,
        "stars" => DomainStrings.StampSet_Stars,
        "bubbles" => DomainStrings.StampSet_Bubbles,
        _ => null,
    };

    /// <summary>빠진 예전 기본 세트(<see cref="RetiredIds"/>)의 이름(지금 언어). 아니면 null.</summary>
    public static string? RetiredName(string id) => id switch
    {
        "pine" => DomainStrings.StampSet_PineNeedles,
        "flowers" => DomainStrings.StampSet_Flowers,
        "petals" => DomainStrings.StampSet_Petals,
        "sparkles" => DomainStrings.StampSet_Sparkles,
        "dots" => DomainStrings.StampSet_Dots,
        "clouds" => DomainStrings.StampSet_Clouds,
        "rocks" => DomainStrings.StampSet_Rocks,
        "bricks" => DomainStrings.StampSet_Bricks,
        "rain" => DomainStrings.StampSet_Rain,
        "snow" => DomainStrings.StampSet_Snowflakes,
        "hearts" => DomainStrings.StampSet_Hearts,
        "puffs" => DomainStrings.StampSet_Puffs,
        "wood" => DomainStrings.StampSet_WoodGrain,
        "cracks" => DomainStrings.StampSet_Cracks,
        "treasure" => DomainStrings.StampSet_Treasure,
        _ => null,
    };

    /// <summary>기본 세트를 새로 만든다(<see cref="Ids"/> 차례). 모두 무작위.</summary>
    public static List<StampSet> Sets() =>
    [
        Set("leaves", Mirrored(Leaves.Select(rows => Art(rows, LeafGreen)))),
        Set("autumn", LeafAutumn.SelectMany(palette => Leaves.Take(3).Select(rows => Art(rows, palette)))),
        Set("grass", Mirrored(Grass.Select(rows => Art(rows, LeafGreen)))),
        Set("stars", StarColors.SelectMany(palette => Stars.Select(rows => Art(rows, palette)))),
        Set("bubbles", Bubbles()),
    ];

    private static StampSet Set(string id, IEnumerable<PixelImage> shapes)
    {
        var set = new StampSet { Id = id, Order = StampOrder.Random };
        foreach (var shape in shapes) set.Shapes.Add(StampLibrary.Trim(shape) ?? throw new InvalidOperationException(id));
        return set;
    }

    // ───────────── 글자 그림 ─────────────

    private static readonly Dictionary<char, uint> LeafGreen = new() { ['o'] = 0xFF2B5329, ['g'] = 0xFF4C9A3A, ['l'] = 0xFF8BCB5A, ['s'] = 0xFF6B4A2A };
    private static readonly Dictionary<char, uint>[] LeafAutumn =
    [
        new() { ['o'] = 0xFF8A3A12, ['g'] = 0xFFE07A2A, ['l'] = 0xFFF6B24A, ['s'] = 0xFF6B4A2A },
        new() { ['o'] = 0xFF6E1A1A, ['g'] = 0xFFC0392B, ['l'] = 0xFFE8704A, ['s'] = 0xFF6B4A2A },
        new() { ['o'] = 0xFF8A6A12, ['g'] = 0xFFE8C33A, ['l'] = 0xFFFFE88A, ['s'] = 0xFF6B4A2A },
    ];

    private static readonly string[][] Leaves =
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

    private static readonly string[][] Grass =
    [
        [
            "...l...",
            "...l..l",
            "l..g..l",
            ".l.g.lg",
            ".g.ggg.",
            "..ggg..",
        ],
        [
            ".l...",
            ".l..l",
            ".g.lg",
            "lg.g.",
            ".ggg.",
        ],
        [
            ".....l",
            "l.l..l",
            ".lg.lg",
            ".gglg.",
            "..ggg.",
        ],
        [
            "..l.",
            "..l.",
            ".lg.",
            ".g.l",
            ".g.g",
            "lggo",
            ".gg.",
        ],
    ];

    private static readonly Dictionary<char, uint>[] StarColors =
    [
        new() { ['o'] = 0xFFB0701A, ['y'] = 0xFFFFD84A, ['w'] = 0xFFFFF6C8 },
        new() { ['o'] = 0xFF7A8AB8, ['y'] = 0xFFDDE6FF, ['w'] = 0xFFFFFFFF },
    ];

    private static readonly string[][] Stars =
    [
        [
            "....o....",
            "...oyo...",
            "...oyo...",
            "ooooywooo",
            "oyyyywyyo",
            ".oyyyyyo.",
            "..oyyyo..",
            ".oyyoyyo.",
            ".oyo.oyo.",
            ".oo...oo.",
        ],
        [
            "..y..",
            ".ywy.",
            "yyyyy",
            ".yyy.",
            ".y.y.",
        ],
        [
            ".y.",
            "ywy",
            ".y.",
        ],
    ];

    /// <summary>글자 그림 → 모양(글자마다 한 픽셀, 팔레트 0xAARRGGBB, 없는 글자 = 투명).</summary>
    private static PixelImage Art(string[] rows, Dictionary<char, uint> palette)
    {
        int w = rows.Max(r => r.Length), h = rows.Length;
        var image = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (palette.TryGetValue(rows[y][x], out var argb)) image.Pixels[y * w + x] = new Color32(argb);
        return image;
    }

    /// <summary>모양들 다음에 좌우로 뒤집은 것들(좌우 대칭이라 같은 것은 빼고).</summary>
    private static IEnumerable<PixelImage> Mirrored(IEnumerable<PixelImage> shapes)
    {
        var list = shapes.ToList();
        return list.Concat(list.Select(FlipX).Where(f => !list.Any(s => s.ContentEquals(f))));
    }

    private static PixelImage FlipX(PixelImage image)
    {
        var result = new PixelImage(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                result.Pixels[y * image.Width + image.Width - 1 - x] = image.Pixels[y * image.Width + x];
        return result;
    }

    // ───────────── 만든 그림 ─────────────

    private static PixelImage Paint(int w, int h, Func<int, int, Color32> color)
    {
        var image = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) image.Pixels[y * w + x] = color(x, y);
        return image;
    }

    /// <summary>픽셀 아트 원(브러시 원과 같은 식: 칸 가운데까지 거리² ≤ r² − r/2, r = 지름/2).</summary>
    private static bool InCircle(int size, double x, double y)
    {
        double r = size / 2.0, c = (size - 1) / 2.0;
        return size <= 2 || (x - c) * (x - c) + (y - c) * (y - c) <= r * r - r / 2;
    }

    /// <summary>안쪽 칸 중 위·아래·왼쪽·오른쪽 이웃이 바깥인 칸(테두리).</summary>
    private static bool Edge(Func<int, int, bool> inside, int x, int y)
        => !inside(x - 1, y) || !inside(x + 1, y) || !inside(x, y - 1) || !inside(x, y + 1);

    private static IEnumerable<PixelImage> Bubbles()
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
}
