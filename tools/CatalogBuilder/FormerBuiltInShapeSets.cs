using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 예전 판(1.14.117~1.14.122)에서 앱에 들어 있던 모양 펜 기본 세트 중 지금 기본 세트(<see cref="Chickxel.Core.Assets.OfficialShapeSets"/> — 꽃·반짝이·비눗방울·단풍잎·나뭇잎)가
/// 아닌 열네 가지의 그림: 솔잎·꽃잎·점·구름·돌멩이·빗방울·눈송이·하트·연기·나뭇결·금·동전과 보석·풀·별(낱개 벽돌 세트는 상점에서 내림). 앱에 있던 코드 그대로라 예전 사용자의 세트와 같은 모양이다
/// (상점 패키지는 <see cref="LocalShapeSets"/>). 손으로 그린 글자 그림과, 원·타원·무작위 걸음으로 만든 그림(씨앗 고정이라 늘 같은 모양)을 섞는다.
/// </summary>
internal static class FormerBuiltInShapeSets
{
    public static StampSet Pine() => Set("Pine needles", Mirrored(PineShapes.Select(rows => Art(rows, PineGreen))));
    public static StampSet Grass() => Set("Grass", Mirrored(GrassShapes.Select(rows => Art(rows, GrassGreen))));
    public static StampSet Petals() => Set("Petals", Mirrored(PetalShapes.Select(rows => Art(rows, PetalPink))));
    public static StampSet Stars() => Set("Stars", StarColors.SelectMany(palette => StarShapes.Select(rows => Art(rows, palette))));
    public static StampSet Dots() => Set("Dots", DotShapes(), StampOrder.Sequential);
    public static StampSet Clouds() => Set("Clouds", CloudShapes());
    public static StampSet Pebbles() => Set("Pebbles", RockShapes());
    public static StampSet Raindrops() => Set("Raindrops", RainShapes.Select(rows => Art(rows, RainBlue)));
    public static StampSet Snowflakes() => Set("Snowflakes", SnowShapes.Select(rows => Art(rows, SnowWhite)));
    public static StampSet Hearts() => Set("Hearts", HeartColors.SelectMany(palette => HeartShapes.Select(rows => Art(rows, palette))));
    public static StampSet SmokePuffs() => Set("Smoke puffs", PuffShapes());
    public static StampSet WoodGrain() => Set("Wood grain", WoodShapes());
    public static StampSet Cracks() => Set("Cracks", CrackShapes());
    public static StampSet CoinsAndGems() => Set("Coins & gems", [.. CoinShapes(), .. Gems.Select(palette => Art(Gem, palette))]);

    // ───────────── 글자 그림 ─────────────

    private static readonly Dictionary<char, uint> PineGreen = new() { ['n'] = 0xFF1F4D3A, ['m'] = 0xFF3E7D4F, ['s'] = 0xFF5A3A22 };

    private static readonly string[][] PineShapes =
    [
        [
            "n.m.n.m..",
            ".n.m.n.m.",
            "sssssssss",
            ".m.n.m.n.",
            "m.n.m.n..",
        ],
        [
            "n.....m",
            ".n...m.",
            "..n.m..",
            "...s...",
            "...s...",
        ],
        [
            "......n.",
            "..n..n.m",
            ".m.ns.m.",
            "n..sn...",
            "..s.m...",
            ".s......",
            "s.......",
        ],
    ];

    private static readonly Dictionary<char, uint> PetalPink = new() { ['p'] = 0xFFE86A92, ['q'] = 0xFFF8B4C8, ['Q'] = 0x90F8B4C8 };

    private static readonly string[][] PetalShapes =
    [
        [
            ".Qq",
            "Qqp",
            "qp.",
        ],
        [
            "Qq.",
            "qqp",
            ".pp",
        ],
        [
            ".q.",
            "Qqp",
            "qp.",
            "p..",
        ],
        [
            "Qqq.",
            ".qpp",
        ],
    ];

    private static readonly Dictionary<char, uint> RainBlue = new() { ['a'] = 0x50A8C8FF, ['b'] = 0x90A8C8FF, ['c'] = 0xD0C8DEFF };

    private static readonly string[][] RainShapes =
    [
        ["a", "b", "c"],
        ["a", "a", "b", "b", "c"],
        ["a", "b", "b", "c", "c", "c"],
        ["..a", ".b.", "c.."],
        ["....a", "...a.", "..b..", ".b...", "c...."],
        ["b...b", ".c.c."],
    ];

    private static readonly Dictionary<char, uint> SnowWhite = new() { ['w'] = 0xFFFFFFFF, ['b'] = 0xFFCDE6FF, ['W'] = 0xB0FFFFFF };

    private static readonly string[][] SnowShapes =
    [
        [
            "...w...",
            ".w.w.w.",
            "..wbw..",
            "wwbwbww",
            "..wbw..",
            ".w.w.w.",
            "...w...",
        ],
        [
            "w.w.w",
            ".wbw.",
            "wbwbw",
            ".wbw.",
            "w.w.w",
        ],
        [
            "..w..",
            "w.b.w",
            ".bwb.",
            "w.b.w",
            "..w..",
        ],
        [
            ".w.",
            "wbw",
            ".w.",
        ],
        ["W"],
        ["Ww", "wW"],
    ];

    private static readonly Dictionary<char, uint>[] HeartColors =
    [
        new() { ['o'] = 0xFF8A1E2B, ['r'] = 0xFFE43B44, ['w'] = 0xFFFFD6D6 },
        new() { ['o'] = 0xFFA8406A, ['r'] = 0xFFF58DB4, ['w'] = 0xFFFFE6F0 },
    ];

    private static readonly string[][] HeartShapes =
    [
        [
            "r.r",
            "rrr",
            ".r.",
        ],
        [
            "rr.rr",
            "rwrrr",
            ".rrr.",
            "..r..",
        ],
        [
            ".rr.rr.",
            "rwrrrrr",
            "rrrrrrr",
            ".rrrrr.",
            "..rrr..",
            "...r...",
        ],
        [
            ".oo...oo.",
            "orro.orro",
            "orwrorrro",
            "orrrrrrro",
            ".orrrrro.",
            "..orrro..",
            "...oro...",
            "....o....",
        ],
    ];

    private static readonly Dictionary<char, uint>[] Gems =
    [
        new() { ['o'] = 0xFF6E1020, ['l'] = 0xFFD8304A, ['L'] = 0xFFFF9AAA },
        new() { ['o'] = 0xFF0E4A2A, ['l'] = 0xFF2EB866, ['L'] = 0xFFA8F0C0 },
        new() { ['o'] = 0xFF14306E, ['l'] = 0xFF3A6EE8, ['L'] = 0xFFA8C8FF },
    ];

    private static readonly string[] Gem =
    [
        ".ooooo.",
        "oLLlllo",
        "olllllo",
        ".olllo.",
        "..olo..",
        "...o...",
    ];

    private static readonly Dictionary<char, uint> GrassGreen = new() { ['o'] = 0xFF2B5329, ['g'] = 0xFF4C9A3A, ['l'] = 0xFF8BCB5A };

    private static readonly string[][] GrassShapes =
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

    private static readonly string[][] StarShapes =
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

    // ───────────── 만든 그림 ─────────────

    private static IEnumerable<PixelImage> DotShapes()
    {
        var dark = new Color32(0x3A, 0x3A, 0x4A);
        yield return Paint(1, 1, (_, _) => dark);
        yield return Art(["Dd", "dD"], new Dictionary<char, uint> { ['d'] = 0xFF3A3A4A, ['D'] = 0x803A3A4A });
        foreach (int size in new[] { 3, 4, 5, 6 })
            yield return Paint(size, size, (x, y) => !InCircle(size, x, y) ? Color32.Transparent
                : Edge((a, b) => a >= 0 && b >= 0 && a < size && b < size && InCircle(size, a, b), x, y) && size >= 5 ? dark.WithAlpha(140) : dark);
    }

    private static IEnumerable<PixelImage> CloudShapes()
    {
        var palette = new Dictionary<char, uint> { ['r'] = 0xFFA8BCD8, ['w'] = 0xFFFFFFFF, ['s'] = 0xFFD4E0F0 };
        string[][] clouds =
        [
            ["...rrr....", "..rwwwrrr.", ".rwwwwwwwr", "rwwwwwwwwr", "rsswwwwssr", ".rrrrrrrr."],
            [".....rrr......", "...rrwwwr.....", "..rwwwwwwrrr..", ".rwwwwwwwwwwr.", "rwwwwwwwwwwwwr", "rsswwwwwwwsssr", ".rrrrrrrrrrrr."],
            ["..rrr..", ".rwwwrr", "rwwwwwr", "rsssssr", ".rrrrr."],
            ["..rrr..rrr..", ".rwwwrrwwwr.", "rwwwwwwwwwwr", "rwwwwwwwwwwr", "rsswwwwwwssr", ".rrrrrrrrrr."],
            ["....rrrr.......", "..rrwwwwrrrrr..", "rrwwwwwwwwwwwrr", "rssswwwwwwssssr", ".rrrrrrrrrrrrr."],
            ["....rrr....", "..rrwwwr...", ".rwwwwwwrr.", ".rwwwwwwwwr", "rwwwwwwwwwr", "rsswwwwwssr", ".rrrrrrrrr."],
        ];
        return clouds.Select(rows => Art(rows, palette));
    }

    private static IEnumerable<PixelImage> RockShapes()
    {
        var outline = new Color32(0x3E, 0x3E, 0x46);
        var dark = new Color32(0x5E, 0x5E, 0x68);
        var mid = new Color32(0x80, 0x80, 0x8A);
        var light = new Color32(0xAE, 0xAE, 0xB8);
        (int W, int H)[] sizes = [(4, 3), (5, 3), (5, 4), (6, 4), (7, 5), (8, 5), (9, 6), (11, 7)];
        for (int v = 0; v < sizes.Length; v++)
        {
            var (w, h) = sizes[v];
            var rng = new Lcg((uint)(v + 31));
            // 위쪽 테두리에서 한 칸을 깎아 덜 매끈하게
            int nick = w >= 6 ? 1 + rng.Next(w - 2) : -1;
            bool Inside(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && InEllipse(w, h, x, y) && !(y == 0 && x == nick);
            yield return Paint(w, h, (x, y) =>
            {
                if (!Inside(x, y)) return Color32.Transparent;
                if (Edge(Inside, x, y)) return outline;
                double nx = (x - (w - 1) / 2.0) / (w / 2.0), ny = (y - (h - 1) / 2.0) / (h / 2.0);
                double lit = -(nx * 0.6 + ny);
                return lit > 0.45 ? light : lit < -0.35 ? dark : mid;
            });
        }
    }

    private static IEnumerable<PixelImage> PuffShapes()
    {
        static Color32 Soft(double t) => new(0xDA, 0xDA, 0xDE, (byte)(t > 0.66 ? 170 : t > 0.33 ? 110 : 55));
        foreach (int size in new[] { 3, 5, 7, 9 })
        {
            double c = (size - 1) / 2.0, r = size / 2.0;
            yield return Paint(size, size, (x, y) =>
            {
                if (!InCircle(size, x, y)) return Color32.Transparent;
                double d = Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / r;
                return Soft(1 - d);
            });
        }
        // 두세 덩이가 붙은 연기
        foreach (var blobs in new[] { new[] { (3.0, 4.0, 3.0), (7.0, 3.0, 3.5) }, new[] { (3.0, 5.0, 2.5), (6.5, 3.5, 3.5), (10.0, 5.0, 2.5) } })
        {
            int w = (int)Math.Ceiling(blobs.Max(b => b.Item1 + b.Item3)) + 1, h = (int)Math.Ceiling(blobs.Max(b => b.Item2 + b.Item3)) + 1;
            yield return Paint(w, h, (x, y) =>
            {
                double best = 0;
                foreach (var (bx, by, br) in blobs)
                {
                    double d = Math.Sqrt((x + 0.5 - bx) * (x + 0.5 - bx) + (y + 0.5 - by) * (y + 0.5 - by)) / br;
                    best = Math.Max(best, 1 - d);
                }
                return best <= 0.05 ? Color32.Transparent : Soft(best);
            });
        }
    }

    private static IEnumerable<PixelImage> WoodShapes()
    {
        var dark = new Color32(0x5A, 0x3A, 0x20, 210);
        var light = new Color32(0x9A, 0x6C, 0x40, 150);
        for (int v = 0; v < 6; v++)
        {
            var rng = new Lcg((uint)(v + 51));
            int w = 8 + rng.Next(7), h = 4 + rng.Next(2);
            double phase = rng.Next(10) * 0.6, freq = 0.35 + rng.Next(3) * 0.12;
            bool knot = v % 3 == 2;
            int kx = w / 2, ky = h / 2;
            yield return Paint(w, h, (x, y) =>
            {
                if (knot)
                {
                    double ex = (x - kx) / 1.6, ey = (y - ky) / 1.0;
                    double e = ex * ex + ey * ey;
                    if (e <= 0.6) return dark;
                    if (e <= 1.6) return Color32.Transparent;
                }
                int wave = (int)Math.Round(Math.Sin(x * freq + phase) * (knot && Math.Abs(x - kx) <= 2 ? 1.8 : 0.8));
                if (y == 1 + wave) return dark;
                if (y == h - 2 + wave / 2 && x % 7 != 3) return light;
                return Color32.Transparent;
            });
        }
    }

    private static IEnumerable<PixelImage> CrackShapes()
    {
        var line = new Color32(0x2A, 0x22, 0x28, 235);
        var lip = new Color32(0xFF, 0xFF, 0xFF, 70);
        for (int v = 0; v < 6; v++)
        {
            var rng = new Lcg((uint)(v + 71));
            int size = 9 + v % 3 * 2;
            var cells = new HashSet<(int, int)>();
            void Walk(int x, int y, int length, int lean)
            {
                for (int i = 0; i < length && y < size; i++)
                {
                    cells.Add((x, y));
                    int step = rng.Next(4);
                    x = Math.Clamp(x + (step == 0 ? -1 : step == 3 ? 1 : 0) + (i % 3 == 2 ? lean : 0), 0, size - 1);
                    y++;
                }
            }
            int start = size / 2 - 1 + rng.Next(3);
            Walk(start, 0, size, 0);
            // 가운데쯤에서 옆으로 갈라지는 가지
            var branch = cells.ElementAt(size / 3 + rng.Next(Math.Max(1, size / 3)));
            Walk(branch.Item1, branch.Item2, size / 2, v % 2 == 0 ? 1 : -1);
            yield return Paint(size, size, (x, y) =>
                cells.Contains((x, y)) ? line : cells.Contains((x - 1, y)) && !cells.Contains((x, y - 1)) ? lip : Color32.Transparent);
        }
    }

    private static IEnumerable<PixelImage> CoinShapes()
    {
        var rim = new Color32(0xB0, 0x7A, 0x1E);
        var gold = new Color32(0xF2, 0xC1, 0x4E);
        var shine = new Color32(0xFF, 0xF1, 0xA8);
        foreach (var (w, h) in new[] { (5, 5), (7, 7), (9, 9) })
        {
            bool Inside(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && InCircle(w, x, y);
            yield return Paint(w, h, (x, y) =>
            {
                if (!Inside(x, y)) return Color32.Transparent;
                if (Edge(Inside, x, y)) return rim;
                // 가운데 세로 홈과 왼쪽 위 빛
                if (w >= 7 && x == w / 2 && y >= 2 && y <= h - 3) return rim;
                return x + y <= w / 2 + 1 ? shine : gold;
            });
        }
    }
}
