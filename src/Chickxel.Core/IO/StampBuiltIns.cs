using Chickxel.Core.Edit;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// 모양 펜 기본 세트 스무 가지(코드에 고정 — 지워도 <see cref="StampLibrary.RestoreDefaults"/>로 되돌림). 손으로 그린 글자 그림과,
/// 원·타원·무작위 걸음으로 만든 그림(씨앗 고정이라 늘 같은 모양)을 섞는다. 모양은 모두 투명한 둘레를 잘라 담는다.
/// </summary>
public static class StampBuiltIns
{
    public static readonly IReadOnlyList<string> Ids =
    [
        "leaves", "autumn", "pine", "grass", "flowers", "petals", "sparkles", "stars", "dots", "bubbles",
        "clouds", "rocks", "bricks", "rain", "snow", "hearts", "puffs", "wood", "cracks", "treasure",
    ];

    /// <summary>처음 판의 기본 세트 — 알던 기본 세트를 적지 않은 예전 파일은 이것만 알던 것으로 본다(나머지는 새로 넣음).</summary>
    public static readonly IReadOnlyList<string> FirstIds = ["leaves", "grass", "flowers", "sparkles", "dots"];

    /// <summary>기본 세트 이름(지금 언어). 기본 세트가 아니면 null.</summary>
    public static string? Name(string id) => id switch
    {
        "leaves" => DomainStrings.StampSet_Leaves,
        "autumn" => DomainStrings.StampSet_AutumnLeaves,
        "pine" => DomainStrings.StampSet_PineNeedles,
        "grass" => DomainStrings.StampSet_Grass,
        "flowers" => DomainStrings.StampSet_Flowers,
        "petals" => DomainStrings.StampSet_Petals,
        "sparkles" => DomainStrings.StampSet_Sparkles,
        "stars" => DomainStrings.StampSet_Stars,
        "dots" => DomainStrings.StampSet_Dots,
        "bubbles" => DomainStrings.StampSet_Bubbles,
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

    /// <summary>기본 세트를 새로 만든다(<see cref="Ids"/> 차례). 점은 차례로, 나머지는 무작위.</summary>
    public static List<StampSet> Sets() =>
    [
        Set("leaves", Mirrored(Leaves.Select(rows => Art(rows, LeafGreen)))),
        Set("autumn", LeafAutumn.SelectMany(palette => Leaves.Take(3).Select(rows => Art(rows, palette)))),
        Set("pine", Mirrored(Pine.Select(rows => Art(rows, PineGreen)))),
        Set("grass", Mirrored(Grass.Select(rows => Art(rows, LeafGreen)))),
        Set("flowers", FlowerColors.SelectMany(palette => Flowers.Select(rows => Art(rows, palette)))),
        Set("petals", Mirrored(Petals.Select(rows => Art(rows, PetalPink)))),
        Set("sparkles", Sparkles()),
        Set("stars", StarColors.SelectMany(palette => Stars.Select(rows => Art(rows, palette)))),
        Set("dots", Dots(), StampOrder.Sequential),
        Set("bubbles", Bubbles()),
        Set("clouds", Clouds()),
        Set("rocks", Rocks()),
        Set("bricks", Bricks()),
        Set("rain", Rain.Select(rows => Art(rows, RainBlue))),
        Set("snow", Snow.Select(rows => Art(rows, SnowWhite))),
        Set("hearts", HeartColors.SelectMany(palette => Hearts.Select(rows => Art(rows, palette)))),
        Set("puffs", Puffs()),
        Set("wood", Wood()),
        Set("cracks", Cracks()),
        Set("treasure", [.. Coins(), .. Gems.Select(palette => Art(Gem, palette))]),
    ];

    private static StampSet Set(string id, IEnumerable<PixelImage> shapes, StampOrder order = StampOrder.Random)
    {
        var set = new StampSet { Id = id, Order = order };
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

    private static readonly Dictionary<char, uint> PineGreen = new() { ['n'] = 0xFF1F4D3A, ['m'] = 0xFF3E7D4F, ['s'] = 0xFF5A3A22 };

    private static readonly string[][] Pine =
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

    private static readonly Dictionary<char, uint>[] FlowerColors =
    [
        new() { ['p'] = 0xFFE86A92, ['q'] = 0xFFF8B4C8, ['y'] = 0xFFF5D545, ['g'] = 0xFF4C9A3A },
        new() { ['p'] = 0xFF5B6EE1, ['q'] = 0xFFA8B8FF, ['y'] = 0xFFFFF1A8, ['g'] = 0xFF4C9A3A },
        new() { ['p'] = 0xFFD8D8E4, ['q'] = 0xFFFFFFFF, ['y'] = 0xFFF5B83A, ['g'] = 0xFF4C9A3A },
    ];

    private static readonly string[][] Flowers =
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

    private static readonly Dictionary<char, uint> PetalPink = new() { ['p'] = 0xFFE86A92, ['q'] = 0xFFF8B4C8, ['Q'] = 0x90F8B4C8 };

    private static readonly string[][] Petals =
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

    private static readonly Dictionary<char, uint> RainBlue = new() { ['a'] = 0x50A8C8FF, ['b'] = 0x90A8C8FF, ['c'] = 0xD0C8DEFF };

    private static readonly string[][] Rain =
    [
        ["a", "b", "c"],
        ["a", "a", "b", "b", "c"],
        ["a", "b", "b", "c", "c", "c"],
        ["..a", ".b.", "c.."],
        ["....a", "...a.", "..b..", ".b...", "c...."],
        ["b...b", ".c.c."],
    ];

    private static readonly Dictionary<char, uint> SnowWhite = new() { ['w'] = 0xFFFFFFFF, ['b'] = 0xFFCDE6FF, ['W'] = 0xB0FFFFFF };

    private static readonly string[][] Snow =
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

    private static readonly string[][] Hearts =
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

    /// <summary>씨앗이 같으면 늘 같은 수를 내는 작은 난수(.NET 버전에 따라 바뀌지 않게 직접).</summary>
    private struct Lcg(uint seed)
    {
        private uint _state = seed * 2654435761u + 1;

        public int Next(int max)
        {
            _state = _state * 1664525u + 1013904223u;
            return (int)((_state >> 8) % (uint)max);
        }
    }

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

    /// <summary>타원 안(가로 <paramref name="w"/>·세로 <paramref name="h"/> 칸에 꼭 맞게).</summary>
    private static bool InEllipse(int w, int h, int x, int y)
    {
        double rx = w / 2.0, ry = h / 2.0, dx = (x - (w - 1) / 2.0) / rx, dy = (y - (h - 1) / 2.0) / ry;
        return dx * dx + dy * dy <= 1.0 - 0.5 / Math.Max(rx, ry);
    }

    /// <summary>안쪽 칸 중 위·아래·왼쪽·오른쪽 이웃이 바깥인 칸(테두리).</summary>
    private static bool Edge(Func<int, int, bool> inside, int x, int y)
        => !inside(x - 1, y) || !inside(x + 1, y) || !inside(x, y - 1) || !inside(x, y + 1);

    private static IEnumerable<PixelImage> Sparkles()
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

    private static IEnumerable<PixelImage> Dots()
    {
        var dark = new Color32(0x3A, 0x3A, 0x4A);
        yield return Paint(1, 1, (_, _) => dark);
        yield return Art(["Dd", "dD"], new Dictionary<char, uint> { ['d'] = 0xFF3A3A4A, ['D'] = 0x803A3A4A });
        foreach (int size in new[] { 3, 4, 5, 6 })
            yield return Paint(size, size, (x, y) => !InCircle(size, x, y) ? Color32.Transparent
                : Edge((a, b) => a >= 0 && b >= 0 && a < size && b < size && InCircle(size, a, b), x, y) && size >= 5 ? dark.WithAlpha(140) : dark);
    }

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

    private static IEnumerable<PixelImage> Clouds()
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

    private static IEnumerable<PixelImage> Rocks()
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

    private static IEnumerable<PixelImage> Bricks()
    {
        var top = new Color32(0xD2, 0x74, 0x52);
        var face = new Color32(0xB0, 0x50, 0x38);
        var bottom = new Color32(0x7A, 0x34, 0x28);
        PixelImage Brick(int w, int h) => Paint(w, h, (x, y) => y == 0 && x < w - 1 ? top : y == h - 1 || x == w - 1 ? bottom : face);
        foreach (var (w, h) in new[] { (6, 3), (8, 3), (8, 4), (10, 4), (12, 5) })
            yield return Brick(w, h);
        // 엇갈려 쌓은 두 장(사이 줄눈은 투명)
        var a = Brick(8, 4);
        var pair = new PixelImage(13, 9);
        pair.Blit(a, 0, 0);
        pair.Blit(a, 5, 5);
        yield return pair;
    }

    private static IEnumerable<PixelImage> Puffs()
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

    private static IEnumerable<PixelImage> Wood()
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

    private static IEnumerable<PixelImage> Cracks()
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

    private static IEnumerable<PixelImage> Coins()
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
