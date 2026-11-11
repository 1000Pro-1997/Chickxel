using Chickxel.Core.Edit;   // StampOrder
using Chickxel.Core.IO;     // StampSet, ShapeArt
using Chickxel.Core.Model;  // PixelImage, Color32
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 자연·생물 모양 펜 세트: 나비·벌레, 나는 새, 물고기, 산호·해초, 선인장, 침엽수, 둥근 나무·덤불.
/// 글자 그림(글자마다 한 픽셀, 팔레트 0xAARRGGBB)과 씨앗 고정 난수로 그린다. 빛은 왼쪽 위에서, 테두리는 바탕보다 어두운 색.
/// </summary>
internal static class NatureLifeShapeSets
{
    // ───────────── 공통 ─────────────

    // 재질 네 톤: 테두리, 그늘, 바탕, 밝은 곳
    private readonly record struct Mat(uint Out, uint Dark, uint Base, uint Light);

    /// <summary>
    /// 글자 그림을 재질별 자동 명암으로 칠한다: 투명과 맞닿은 칸은 테두리, 테두리 바로 아래·오른쪽 칸은 밝게, 위·왼쪽 칸은 어둡게.
    /// <paramref name="direct"/>의 글자는 그 색 그대로(모양 안쪽으로 친다).
    /// <paramref name="outer"/>면 테두리를 모양 바깥 1px에 두르고(그림이 둘레로 1px씩 커짐) 가장자리 칸은 밝은 곳·그늘로 칠한다 — 가는 줄기용.
    /// </summary>
    private static PixelImage Toon(string[] rows, Dictionary<char, Mat> mats, Dictionary<char, uint>? direct = null, bool outer = false)
    {
        int w = rows.Max(r => r.Length), h = rows.Length, pad = outer ? 1 : 0;
        char At(int x, int y) => y < 0 || y >= h || x < 0 || x >= rows[y].Length ? '.' : rows[y][x];
        bool In(int x, int y)
        {
            char c = At(x, y);
            return mats.ContainsKey(c) || (direct?.ContainsKey(c) ?? false);
        }
        bool Rim(int x, int y) => In(x, y) && Edge(In, x, y);
        var image = new PixelImage(w + pad * 2, h + pad * 2);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                char c = rows[y][x];
                if (direct is not null && direct.TryGetValue(c, out var argb)) { image.Set(x + pad, y + pad, new Color32(argb)); continue; }
                if (!mats.TryGetValue(c, out var m)) continue;
                uint color;
                if (outer)
                {
                    bool lit = !In(x, y - 1) || !In(x - 1, y);
                    bool dark = !In(x, y + 1) || !In(x + 1, y);
                    color = lit && !dark ? m.Light : dark && !lit ? m.Dark : m.Base;
                }
                else if (Edge(In, x, y)) color = m.Out;
                else
                {
                    bool lit = Rim(x, y - 1) || Rim(x - 1, y);
                    bool dark = Rim(x, y + 1) || Rim(x + 1, y);
                    color = lit && !dark ? m.Light : dark && !lit ? m.Dark : m.Base;
                }
                image.Set(x + pad, y + pad, new Color32(color));
            }
        if (outer)
            for (int y = -1; y <= h; y++)
                for (int x = -1; x <= w; x++)
                {
                    if (In(x, y)) continue;
                    foreach (var (nx, ny) in new[] { (x, y + 1), (x + 1, y), (x - 1, y), (x, y - 1) })
                        if (mats.TryGetValue(At(nx, ny), out var m)) { image.Set(x + 1, y + 1, new Color32(m.Out)); break; }
                }
        return image;
    }

    /// <summary>글자 판: 원·붓질로 재질 글자를 찍고 <see cref="Rows"/>로 글자 그림을 낸다.</summary>
    private sealed class Canvas(int w, int h)
    {
        public readonly int W = w, H = h;
        private readonly char[] _cells = Enumerable.Repeat('.', w * h).ToArray();

        public char this[int x, int y]
        {
            get => x >= 0 && y >= 0 && x < W && y < H ? _cells[y * W + x] : '.';
            set { if (x >= 0 && y >= 0 && x < W && y < H) _cells[y * W + x] = value; }
        }

        public void Disc(double cx, double cy, double r, char c)
        {
            for (int y = (int)Math.Floor(cy - r); y <= (int)Math.Ceiling(cy + r); y++)
                for (int x = (int)Math.Floor(cx - r); x <= (int)Math.Ceiling(cx + r); x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) this[x, y] = c;
        }

        public void Stroke(double x0, double y0, double x1, double y1, double r, char c)
        {
            int n = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 2) + 1;
            for (int i = 0; i <= n; i++) Disc(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, r, c);
        }

        public string[] Rows() => [.. Enumerable.Range(0, H).Select(y => new string(_cells, y * W, W))];
    }

    // ───────────── 나비·벌레 ─────────────

    // o 테두리, m 그늘, l 바탕, H 밝은 곳, w 무늬, k 몸통, a 더듬이
    private static readonly Dictionary<char, uint>[] WingColors =
    [
        new() { ['o'] = 0xFF2A160C, ['m'] = 0xFFC8560E, ['l'] = 0xFFF08A1C, ['H'] = 0xFFFFC060, ['w'] = 0xFFFFF4E0 }, // 제왕나비
        new() { ['o'] = 0xFF0C1A48, ['m'] = 0xFF1E5AC8, ['l'] = 0xFF2E9CF0, ['H'] = 0xFF9EE6FF, ['w'] = 0xFF2E9CF0 }, // 모르포
        new() { ['o'] = 0xFF5A4410, ['m'] = 0xFFD8B420, ['l'] = 0xFFFFE04A, ['H'] = 0xFFFFF6B0, ['w'] = 0xFFF08A1C }, // 노랑
        new() { ['o'] = 0xFF5C6270, ['m'] = 0xFFCCD2DC, ['l'] = 0xFFF0F2F6, ['H'] = 0xFFFFFFFF, ['w'] = 0xFF6A707C }, // 흰나비
        new() { ['o'] = 0xFF6A1440, ['m'] = 0xFFD8508C, ['l'] = 0xFFFF86B8, ['H'] = 0xFFFFD4E6, ['w'] = 0xFFFFF0F6 }, // 분홍
    ];

    private static readonly Dictionary<char, uint> BugBody = new() { ['k'] = 0xFF2A2020, ['a'] = 0xFF3A2C28 };

    private static readonly string[] OpenWings = Symmetric(
    [
        "oooo.a..",
        "oHHlo.a.",
        "oHlllllk",
        ".oHlwlmk",
        ".olllmmk",
        "..oooomk",
        "..oHllmk",
        ".oHlwlmk",
        ".ollmmok",
        "..oooo..",
    ]);

    private static readonly string[] SmallOpenWings = Symmetric(
    [
        "oo.a.",
        "oHllk",
        "oHwlk",
        ".oomk",
        ".olmk",
        "..oo.",
    ]);

    // 옆에서 본 접은 날개(왼쪽을 봄): 앞날개 끝이 위, 뒷날개가 뒤로 둥글게
    private static readonly string[] SideWings =
    [
        ".oo.........",
        ".oHoo.......",
        ".oHllo......",
        "..oHllo.....",
        "..oHlwlooo..",
        "a..oHllooHlo",
        ".a.ollmoHllo",
        "..aollmolmmo",
        "...okkkomwmo",
        "....kk..ooo.",
    ];

    // t 날개 테두리, W 날개(반투명), y 노랑, k 검정, o 테두리
    private static readonly Dictionary<char, uint> BeeColors = new()
    {
        ['t'] = 0xC07890A8, ['W'] = 0x90E8F4FF, ['y'] = 0xFFFFC82A, ['Y'] = 0xFFFFEC90, ['d'] = 0xFFD89A10,
        ['k'] = 0xFF2A2018, ['o'] = 0xFF4A3008,
    };

    private static readonly string[] Bee =
    [
        "....ttt.....",
        "...tWWWt.tt.",
        "...tWWWttWWt",
        "..ootWtWWWt.",
        ".okYYkYYko..",
        "okkyykyykkok",
        ".okddkddko..",
        "..oooooooo..",
    ];

    // r 빨강, R 밝은 빨강, m 그늘, k 검정 점, o 테두리
    private static readonly Dictionary<char, uint> LadybugColors = new()
    {
        ['o'] = 0xFF3A0A0A, ['r'] = 0xFFE02828, ['R'] = 0xFFFF8A7A, ['m'] = 0xFFA81818, ['k'] = 0xFF1E1414, ['w'] = 0xFFF0F0F0,
    };

    private static readonly string[] Ladybug =
    [
        "..ooo..",
        ".owkwo.",
        "oRrkrro",
        "orkkrko",
        "okrkmro",
        "ormkkmo",
        ".ooooo.",
    ];

    private static readonly string[] SmallLadybug =
    [
        ".ooo.",
        "oRkro",
        "okrko",
        "ormmo",
        ".ooo.",
    ];

    /// <summary>나비: 날개를 편 큰 나비 다섯 색(작은 나비·옆모습·벌·무당벌레는 <see cref="Insects"/> — 무작위로 섞여도 비슷한 것끼리).</summary>
    public static StampSet Butterflies() => Set("Butterflies", WingColors.Select(c => Art(OpenWings, BugBody, c)));

    /// <summary>곤충: 날개를 편 작은 나비 네 색, 날개를 접은 옆모습 나비, 꿀벌(좌우로 뒤집은 것도), 무당벌레 둘.</summary>
    public static StampSet Insects()
    {
        var shapes = new List<PixelImage>();
        foreach (var c in new[] { WingColors[0], WingColors[1], WingColors[3], WingColors[4] }) shapes.Add(Art(SmallOpenWings, BugBody, c));
        var side = new List<PixelImage>();
        foreach (var c in new[] { WingColors[0], WingColors[2], WingColors[4] }) side.Add(Art(SideWings, BugBody, c));
        side.Add(Art(Bee, BeeColors));
        shapes.AddRange(Mirrored(side));
        shapes.Add(Art(Ladybug, LadybugColors));
        shapes.Add(Art(SmallLadybug, LadybugColors));
        return Set("Insects", shapes);
    }

    // ───────────── 나는 새 ─────────────

    private static readonly Dictionary<char, uint> Silhouette = new() { ['k'] = 0xFF262A34, ['K'] = 0xFF464C5A };

    private static readonly string[][] FarBirds =
    [
        [
            "kk...kk",
            "..k.k..",
            "...k...",
        ],
        [
            ".kk.....kk.",
            "k..kk.kk..k",
            ".....k.....",
        ],
        [
            "kkk...kkk",
            "...kkk...",
        ],
        [
            "...k...",
            ".kk.kk.",
            "k.....k",
        ],
        [
            "kk.........kk",
            ".kKk.....kKk.",
            "...kkk.kkk...",
            ".....kKk.....",
            "......k......",
        ],
        [
            ".kkk.......kkk.",
            "kK..kk...kk..Kk",
            "......kKk......",
            ".......k.......",
        ],
        // 기운 새(좌우 뒤집어 쓴다)
        [
            "kk.......",
            "..kk...kk",
            "....kkk..",
            "....k....",
        ],
    ];

    // o 테두리, g 회색 날개, G 밝은 회색, W 흰 몸, w 흰 그늘, k 날개 끝, y 부리
    private static readonly Dictionary<char, uint> GullColors = new()
    {
        ['o'] = 0xFF4E5666, ['g'] = 0xFF9EA8B8, ['G'] = 0xFFC8D0DC, ['W'] = 0xFFFFFFFF, ['w'] = 0xFFD8DEE8,
        ['k'] = 0xFF1E222A, ['y'] = 0xFFF0B020, ['e'] = 0xFF1E222A,
    };

    private static readonly string[] GullUp = Symmetric(
    [
        "kk.......",
        "okk......",
        ".oGgo....",
        "..oGgo...",
        "...oGgo.o",
        "....oggoW",
        ".....ooow",
        ".......oo",
    ]);

    private static readonly string[] GullFlat = Symmetric(
    [
        "...........o",
        ".......ooooW",
        "kkooooGGGGoW",
        ".kkgggggggow",
        "...oooooooow",
        "..........oo",
    ]);

    // 옆에서 본 갈매기(왼쪽을 봄, 날개 위로)
    private static readonly string[] GullSide =
    [
        "...........kk.",
        "..........kkGo",
        ".........oGGgo",
        "........oGGgo.",
        "..oo...oGGgo..",
        ".oWWo.oGGggo..",
        "yoeWWoGggoooo.",
        ".ooWWWWWWWWWwo",
        "...oowwwwwwoo.",
        ".....ooooo....",
    ];

    // 옆에서 본 갈매기(날개 아래로)
    private static readonly string[] GullSideDown =
    [
        "..oo..........",
        ".oWWo.........",
        "yoeWWoooooooo.",
        ".ooWWWGGGGWWwo",
        "...ooGGggggwoo",
        ".....oGggooo..",
        "......oggo....",
        ".......okk....",
        "........kk....",
    ];

    // b 갈색, B 밝은 갈색, d 짙은 갈색, c 배, o 테두리, y 부리
    private static readonly Dictionary<char, uint> SparrowColors = new()
    {
        ['o'] = 0xFF3A2414, ['b'] = 0xFF9A6234, ['B'] = 0xFFC8905A, ['d'] = 0xFF6A4020, ['c'] = 0xFFEADCC0, ['C'] = 0xFFC8B898,
        ['y'] = 0xFF3A2C20, ['e'] = 0xFF101010,
    };

    private static readonly string[] SparrowUp =
    [
        "......ooo..",
        ".....oBbdo.",
        "..oo.oBbdo.",
        ".oBbooBbdo.",
        "yoebbbbbdooo",
        ".occccbbdddo",
        "..oCCCCoooo.",
        "...oooo.....",
    ];

    private static readonly string[] SparrowDown =
    [
        "..oo........",
        ".oBbo.......",
        "yoebbbbbooo.",
        ".occcbbbbddo",
        "..oCCdbbdoo.",
        "...ooodbo...",
        "......odo...",
        ".......o....",
    ];

    /// <summary>먼 새 실루엣(크기·날갯짓 여러 가지), 갈매기(날개 위·편 것·옆모습), 참새. 좌우로 뒤집은 것도.</summary>
    public static StampSet Birds()
    {
        var shapes = new List<PixelImage>();
        foreach (var bird in FarBirds) shapes.Add(Art(bird, Silhouette));
        shapes.Add(Art(GullUp, GullColors));
        shapes.Add(Art(GullFlat, GullColors));
        shapes.Add(Art(GullSide, GullColors));
        shapes.Add(Art(GullSideDown, GullColors));
        shapes.Add(Art(SparrowUp, SparrowColors));
        shapes.Add(Art(SparrowDown, SparrowColors));
        return Set("Flying Birds", Mirrored(shapes));
    }

    // ───────────── 물고기 ─────────────

    private static readonly Dictionary<char, uint> FishEye = new() { ['e'] = 0xFF101418, ['E'] = 0xFFFFFFFF };

    private static readonly Dictionary<char, Mat> ClownMats = new()
    {
        ['b'] = new(0xFF6A1E04, 0xFFD04A08, 0xFFFF7A1A, 0xFFFFB060),
        ['w'] = new(0xFF2A2A2A, 0xFFD8DCE0, 0xFFFFFFFF, 0xFFFFFFFF),
        ['f'] = new(0xFF6A1E04, 0xFFD04A08, 0xFFFF7A1A, 0xFFFFB060),
    };

    private static readonly Dictionary<char, uint> ClownBands = new() { ['k'] = 0xFF1E1A1A };

    // 왼쪽을 봄
    private static readonly string[] Clownfish =
    [
        ".....fffk......",
        "...bbwwkbbbwk..ff",
        "..bbbwwkbbbwkbfff",
        ".beEbwwkbbbwkbbff",
        "bbeebwwkbbbwkbbf.",
        ".bbbbwwkbbbwkbbff",
        "..bbbwwkbbbwkbfff",
        "...bbwwkbbbwk..ff",
        ".....fffk........",
    ];

    private static readonly Dictionary<char, Mat> TangMats = new()
    {
        ['b'] = new(0xFF0A1E5A, 0xFF1A50C0, 0xFF2A78F0, 0xFF80BCFF),
        ['n'] = new(0xFF06102E, 0xFF0E1A40, 0xFF14244E, 0xFF20386A),
        ['y'] = new(0xFF6A5008, 0xFFE0B010, 0xFFFFD82A, 0xFFFFF0A0),
    };

    private static readonly string[] BlueTang =
    [
        "....bbbbbbb.......",
        "..bbbbnnnnnbbb..yy",
        ".beEbbbbbnnnnbbyyy",
        "bbeebbnnnbbbnnbyyy",
        ".bbbbbbnnnnnbbbyyy",
        "..bbbbbbbbbbbb..yy",
        "....bbbbbbbb......",
    ];

    private static readonly Dictionary<char, Mat> GoldMats = new()
    {
        ['b'] = new(0xFF7A2808, 0xFFE0620E, 0xFFFF9A20, 0xFFFFD070),
        ['f'] = new(0xFFA8400C, 0xD0F07020, 0xB0FFA040, 0xA0FFD090),
    };

    private static readonly string[] Goldfish =
    [
        "....ff......",
        "...bbbb..ff.",
        ".bbbbbbbfff.",
        "beEbbbbbbfff",
        "beebbbbbbff.",
        ".bbbbbbbfff.",
        "..bbbbb..fff",
        "...ff.....f.",
    ];

    private static readonly Dictionary<char, Mat> SilverMats = new()
    {
        ['b'] = new(0xFF2E3C50, 0xFF7890A8, 0xFFB4C4D4, 0xFFF0F6FF),
        ['s'] = new(0xFF2E3C50, 0xFF2E5A88, 0xFF3E72A8, 0xFF6C9CCC),
    };

    private static readonly string[] Sardine =
    [
        "...ssssss.....",
        ".sssssssssss.s",
        "beEbbbbbbbbbss",
        ".bbbbbbbbbbb.s",
        "...bbbbbb.....",
    ];

    private static readonly Dictionary<char, Mat> PufferMats = new()
    {
        ['b'] = new(0xFF5A4410, 0xFFC8A040, 0xFFE8CC6A, 0xFFFFF0B0),
        ['c'] = new(0xFF5A4410, 0xFFD8D0B8, 0xFFF4EEDC, 0xFFFFFFFF),
        ['f'] = new(0xFF5A4410, 0xFFC8A040, 0xFFE8CC6A, 0xFFFFF0B0),
    };

    private static readonly Dictionary<char, uint> PufferSpots = new() { ['p'] = 0xFF8A6A20, ['s'] = 0xFF5A4410 };

    private static readonly string[] Puffer =
    [
        "...s..s..s....",
        "s...bbbbbb..s.",
        "..bbbpbbpbb...",
        ".bbpbbbbbpbb.ff",
        "sbeEbbpbbbbbfff",
        ".bbebbbbbpbbbff",
        "bbbbbbbbbbbb.ff",
        ".bcccccccccb..",
        "s.bcccccccb..s",
        "...cccccc.....",
        "..s..s..s.....",
    ];

    private static readonly Dictionary<char, Mat> AngelMats = new()
    {
        ['b'] = new(0xFF3A3018, 0xFFC8BC8A, 0xFFF0E6B8, 0xFFFFFAE0),
        ['n'] = new(0xFF101010, 0xFF181818, 0xFF262626, 0xFF3A3A3A),
        ['y'] = new(0xFF5A4810, 0xFFD8B020, 0xFFFFD84A, 0xFFFFF0A0),
    };

    private static readonly string[] Angelfish =
    [
        ".........nn....",
        "........nbn....",
        ".......nbbn....",
        "......bnbbn....",
        ".....bbnbbnb...",
        "...bbbbnbbnbb..",
        "..beEbbnbbnbbyy",
        ".bbeebbnbbnbyyy",
        "..bbbbbnbbnbbyy",
        "...bbbbnbbnbb..",
        ".....bbnbbnb...",
        "......bnbbn....",
        ".......nbbn....",
        "........nbn....",
        ".........nn....",
    ];

    private static readonly Dictionary<char, uint> BubbleColors = new() { ['q'] = 0xC0D8F4FF, ['Q'] = 0x60D8F4FF, ['h'] = 0xE0FFFFFF };

    private static PixelImage School(int count)
    {
        var one = Toon(Sardine, SilverMats, FishEye);
        (int X, int Y)[] at = count == 2 ? [(0, 0), (7, 6)] : [(0, 4), (11, 0), (9, 8)];
        return Compose(26, 14, at.Take(count).Select(p => (one, p.X, p.Y)));
    }

    private static PixelImage WithBubbles(PixelImage fish)
    {
        string[] bubbles =
        [
            ".qq.",
            "qhQq",
            "qQQq",
            ".qq.",
            "....",
            "..qq",
            "..qq",
            "....",
            "...q",
        ];
        var b = Art(bubbles, BubbleColors);
        return Compose(fish.Width + 2, fish.Height + 9, [(b, 0, 0), (fish, 2, 9)]);
    }

    /// <summary>옆에서 본 물고기: 흰동가리, 블루탱, 금붕어, 정어리 떼(둘·셋), 복어, 엔젤피시. 양쪽을 보게 뒤집은 것도.</summary>
    public static StampSet Fish()
    {
        var shapes = new List<PixelImage>
        {
            Toon(Clownfish, ClownMats, Merge(FishEye, ClownBands)),
            Toon(BlueTang, TangMats, FishEye),
            Toon(Goldfish, GoldMats, FishEye),
            WithBubbles(Toon(Goldfish, GoldMats, FishEye)),
            School(2),
            School(3),
            Toon(Puffer, PufferMats, Merge(FishEye, PufferSpots)),
            Toon(Angelfish, AngelMats, FishEye),
        };
        return Set("Fish", Mirrored(shapes));
    }

    private static Dictionary<char, uint> Merge(params Dictionary<char, uint>[] palettes)
    {
        var all = new Dictionary<char, uint>();
        foreach (var p in palettes) foreach (var (k, v) in p) all.TryAdd(k, v);
        return all;
    }

    // ───────────── 산호·해초 ─────────────

    private static readonly Mat PinkCoral = new(0xFF6A1838, 0xFFC8406E, 0xFFF06E96, 0xFFFFB4C8);
    private static readonly Mat OrangeCoral = new(0xFF6A2808, 0xFFD0581A, 0xFFF5883A, 0xFFFFC488);
    private static readonly Mat PurpleCoral = new(0xFF2E1450, 0xFF6A3AA8, 0xFF9060D0, 0xFFC8A4F4);
    private static readonly Mat RedCoral = new(0xFF5A0E18, 0xFFB0283A, 0xFFE04454, 0xFFFF8C90);
    private static readonly Mat BrainTan = new(0xFF4A4018, 0xFFA89848, 0xFFD4C474, 0xFFF4EAB0);
    private static readonly Mat BrainGreen = new(0xFF2A4018, 0xFF6A8C34, 0xFF92B44C, 0xFFC4E07C);
    private static readonly Mat SpongeYellow = new(0xFF5A4006, 0xFFC8960E, 0xFFF4C428, 0xFFFFEC86);
    private static readonly Mat SpongePurple = new(0xFF2E1450, 0xFF7040A8, 0xFF9A62D4, 0xFFCCA6F4);
    private static readonly Mat KelpOlive = new(0xFF283A0E, 0xFF587018, 0xFF7C9428, 0xFFB0C454);
    private static readonly Mat SeaweedGreen = new(0xFF0C3A1E, 0xFF1C7A3A, 0xFF2EAA50, 0xFF78DA84);
    private static readonly Mat AnemoneColumn = new(0xFF5A2A10, 0xFFB06030, 0xFFD88A50, 0xFFF4BC88);
    private static readonly Mat AnemonePink = new(0xFF6A1440, 0xFFC84888, 0xFFF07AB4, 0xFFFFC0DC);
    private static readonly Mat AnemoneGreen = new(0xFF1A4A2A, 0xFF3A9A5A, 0xFF5AC87A, 0xFFA8F0B8);

    private static PixelImage BranchCoral(int height, int depth, Mat mat, uint seed)
    {
        var g = new Canvas(height * 2 + 8, height + 4);
        var rng = new Lcg(seed);
        double x = g.W / 2 + 0.5, y = g.H - 1, up = height * 0.22;
        g.Stroke(x, y, x, y - up, 1.6, 'c');
        Candelabra(g, x, y - up, height * 0.28, depth, ref rng);
        return Toon(g.Rows(), new() { ['c'] = mat }, outer: true);
    }

    // 촛대 모양 가지: 양옆으로 비스듬히 나갔다가 위로(가끔 가운데도 곧게), 줄기에 짧은 혹, 끝은 둥글게
    private static void Candelabra(Canvas g, double x, double y, double up, int depth, ref Lcg rng)
    {
        if (depth == 0)
        {
            g.Stroke(x, y, x, y - up * 0.7, 0.6, 'c');
            g.Disc(x, y - up * 0.7, 0.9, 'c');
            return;
        }
        double s = 2 + depth * 2;
        if (depth > 1 && rng.Next(2) == 0) Candelabra(g, x, y, up * 0.8, 0, ref rng);
        foreach (int side in new[] { -1, 1 })
        {
            double nx = x + side * (s + rng.Next(2)), ny = y - 1 - rng.Next(3);
            g.Stroke(x, y, nx, ny, 0.6, 'c');
            double rise = up * (0.4 + rng.Next(40) / 100.0);
            g.Stroke(nx, ny, nx, ny - rise, 0.6, 'c');
            if (rise > 4 && rng.Next(2) == 0) g.Stroke(nx, ny - rise / 2, nx + side * 2, ny - rise / 2 - 2, 0.6, 'c');
            Candelabra(g, nx, ny - rise, up * 0.8, rng.Next(5) == 0 ? 0 : depth - 1, ref rng);
        }
    }

    // 뇌산호: 반 타원 돔에 구불구불한 가로 홈(테두리 안쪽에만)
    private static PixelImage BrainCoral(int w, int h, Mat mat)
    {
        bool Dome(int x, int y) => y >= 1 && y < h + 1 && InEllipse(w, h * 2, x, y - 1) && (y > 1 || Math.Abs(x - (w - 1) / 2.0) < w / 4.0);
        var g = new Canvas(w, h + 1);
        for (int y = 0; y <= h; y++)
            for (int x = 0; x < w; x++)
                if (Dome(x, y))
                    g[x, y] = !Edge(Dome, x, y) && y < h && Math.Sin(y * 1.7 + Math.Sin(x * 0.75) * 1.9) > 0.55 ? 'v' : 'b';
        return Toon(g.Rows(), new() { ['b'] = mat }, new() { ['v'] = Mix(mat.Dark, mat.Out) });
    }

    // 두 색의 가운데
    private static uint Mix(uint a, uint b)
    {
        uint Ch(int shift) => (((a >> shift) & 0xFF) + ((b >> shift) & 0xFF)) / 2 << shift;
        return 0xFF000000 | Ch(16) | Ch(8) | Ch(0);
    }

    // 부채산호: 그물 부채와 짧은 줄기. o 테두리, l 밝은 곳, b 바탕, d 그늘, '.' 그물 구멍
    private static readonly string[] BigFan =
    [
        "....oooooo.....",
        "..oolblblbdoo..",
        ".olb.b.b.b.bdo.",
        "olblbbbdbdbdbdo",
        "ob.b.b.b.d.d.do",
        "olbbblbbdbddbdo",
        ".ob.b.b.d.d.do.",
        ".olbblbdbddbdo.",
        "..ob.b.d.d.do..",
        "...obbbdbddo...",
        "....ob.d.do....",
        ".....obddo.....",
        "......odo......",
        "......odo......",
        ".....ooooo.....",
    ];

    private static readonly string[] SmallFan =
    [
        "...ooooo...",
        ".oolblbdoo.",
        "olb.b.d.ddo",
        "olbblbdbddo",
        "ob.b.d.d.do",
        ".obblbdbdo.",
        "..ob.d.do..",
        "...obddo...",
        "....odo....",
        "....odo....",
        "...ooooo...",
    ];

    private static PixelImage FanCoral(string[] art, Mat mat)
        => Art(art, new Dictionary<char, uint> { ['o'] = mat.Out, ['l'] = mat.Light, ['b'] = mat.Base, ['d'] = mat.Dark });

    // 관해면: 높이가 다른 관(키 큰 것이 뒤), 위에 어두운 구멍, 몸에 작은 구멍 점
    private static PixelImage TubeSponge(Mat mat, params (int X, int W, int H)[] tubes)
    {
        int w = tubes.Max(t => t.X + t.W) + 2, h = tubes.Max(t => t.H);
        var parts = new List<(PixelImage, int, int)>();
        var bits = new Dictionary<char, uint> { ['H'] = 0xFF1A0E20, ['p'] = mat.Dark };
        foreach (var t in tubes.OrderByDescending(t => t.H))
        {
            var g = new Canvas(w, h);
            int top = h - t.H;
            for (int y = top; y < h; y++)
            {
                int left = t.X + 1, right = t.X + t.W;
                for (int x = left; x <= right; x++)
                {
                    if (y == top && (x == left || x == right)) continue;
                    bool pore = y - top > 3 && x > left + 1 && x < right - 1 && y < h - 2 && (x * 5 + y * 3) % 7 == 0;
                    g[x, y] = y == top + 1 && x > left && x < right ? 'H' : pore ? 'p' : 's';
                }
            }
            parts.Add((Toon(g.Rows(), new() { ['s'] = mat }, bits), 0, 0));
        }
        return Compose(w, h, parts);
    }

    // 해초 줄기: 아래는 붙박이, 위로 갈수록 크게 흔들리고 양쪽으로 잎
    private static PixelImage Strands(Mat mat, double bladeLen, params (int X, int H, double Amp, double Phase)[] strands)
    {
        int h = strands.Max(s => s.H) + 1, w = strands.Max(s => s.X) + 12;
        var g = new Canvas(w, h);
        foreach (var (x0, sh, amp, phase) in strands)
        {
            double X(double y) => x0 + amp * Math.Sin(y * 0.32 + phase) * ((h - y) / (double)sh);
            int top = h - sh;
            for (int y = h - 1; y > top; y--) g.Stroke(X(y), y, X(y - 1), y - 1, 0.55, 'k');
            g.Disc(X(top), top + 0.5, 0.8, 'k');
            if (bladeLen <= 0) continue;
            int side = (x0 & 1) == 0 ? 1 : -1;
            for (int y = h - 4; y > top + 2; y -= 4, side = -side)
            {
                double bx = X(y);
                g.Stroke(bx, y, bx + side * bladeLen, y - bladeLen * 0.8, 0.75, 'k');
            }
        }
        return Toon(g.Rows(), new() { ['k'] = mat }, outer: true);
    }

    // 말미잘: 짧은 기둥 위로 끝이 밝은 촉수가 부채처럼
    private static PixelImage Anemone(Mat tentacle, uint tip, int count)
    {
        var g = new Canvas(21, 15);
        for (int y = 11; y < 15; y++)
            for (int x = 7; x <= 13; x++)
                if (!(y == 11 && (x == 7 || x == 13))) g[x, y] = 'a';
        for (int k = 0; k < count; k++)
        {
            double f = count == 1 ? 0 : k / (count - 1.0) * 2 - 1; // -1..1
            double bx = 10 + f * 3, ang = -90 + f * 55, len = 7 - Math.Abs(f) * 2 + (k % 2);
            double x = bx, y = 11;
            for (int i = 0; i < len; i++)
            {
                double a = (ang + f * 8 * i + (k % 2 == 0 ? 6 : -6) * Math.Sin(i)) * Math.PI / 180;
                double nx = x + Math.Cos(a), ny = y + Math.Sin(a);
                g.Stroke(x, y, nx, ny, 0.5, i >= len - 1.5 ? 't' : 'n');
                x = nx; y = ny;
            }
        }
        return Toon(g.Rows(), new() { ['a'] = AnemoneColumn, ['n'] = tentacle, ['t'] = tentacle with { Base = tip, Light = tip, Dark = tip } }, outer: true);
    }

    /// <summary>바닥에 선 산호·해초: 가지산호(분홍·주황·보라), 뇌산호, 부채산호, 관해면, 다시마·해초, 말미잘.</summary>
    public static StampSet CoralAndSeaweed()
    {
        var shapes = new List<PixelImage>
        {
            BranchCoral(22, 2, PinkCoral, 3),
            BranchCoral(16, 2, OrangeCoral, 11),
            BranchCoral(26, 2, PurpleCoral, 9),
            BranchCoral(12, 1, PinkCoral, 21),
            BranchCoral(14, 1, PurpleCoral, 5),
            BrainCoral(16, 9, BrainTan),
            BrainCoral(10, 6, BrainGreen),
            FanCoral(BigFan, PurpleCoral),
            FanCoral(SmallFan, RedCoral),
            TubeSponge(SpongeYellow, (0, 6, 14), (5, 5, 10), (9, 5, 7)),
            TubeSponge(SpongePurple, (0, 5, 8), (4, 6, 12)),
            Strands(KelpOlive, 3.5, (6, 34, 2.2, 0.0)),
            Strands(KelpOlive, 3, (6, 22, 1.8, 1.7)),
            Strands(SeaweedGreen, 2.5, (5, 16, 1.5, 0.5)),
            Strands(SeaweedGreen, 0, (4, 12, 1.4, 0.3), (7, 16, 1.6, 2.0), (10, 10, 1.2, 4.0), (13, 14, 1.5, 1.0)),
            Anemone(AnemonePink, 0xFFFFE0F0, 9),
            Anemone(AnemoneGreen, 0xFFFFC8F0, 7),
        };
        return Set("Coral & Seaweed", Mirrored(shapes));
    }

    // ───────────── 선인장·사막 ─────────────

    private static readonly Mat CactusGreen = new(0xFF173A1E, 0xFF2C6A36, 0xFF44904C, 0xFF72B868);
    private static readonly Mat CactusRib = new(0xFF173A1E, 0xFF24582C, 0xFF347A3E, 0xFF52A058);
    private static readonly Mat BarrelGreen = new(0xFF24401A, 0xFF46782A, 0xFF62A03A, 0xFF96C858);
    private static readonly Mat BarrelRib = new(0xFF24401A, 0xFF3A6824, 0xFF4E8A30, 0xFF78B048);
    private static readonly Mat PadGreen = new(0xFF2A4A20, 0xFF4E8A3A, 0xFF6CAE4E, 0xFFA0D27A);
    private static readonly Mat PearFruit = new(0xFF4A0A28, 0xFFA01E50, 0xFFD03A72, 0xFFF08AAE);
    private static readonly Mat AgaveBlue = new(0xFF1A3A3C, 0xFF3A706E, 0xFF5A9A92, 0xFF90C8BC);
    private static readonly Mat AloeGreen = new(0xFF1E4020, 0xFF3E7A3A, 0xFF5EA050, 0xFF92C878);
    private static readonly Mat Sandstone = new(0xFF4A2814, 0xFF9A5A34, 0xFFC88250, 0xFFE8B080);
    private static readonly Mat GrayRock = new(0xFF34363E, 0xFF74768A, 0xFF9C9EAC, 0xFFCACCD6);

    private static readonly Dictionary<char, uint> DesertBits = new()
    {
        ['y'] = 0xFFF4EEC8, // 가시
        ['f'] = 0xFFFFFFFF, ['c'] = 0xFFF0C030, // 흰 꽃, 꽃술
        ['p'] = 0xFFFF5A9A, ['P'] = 0xFFFFB0D0, // 분홍 꽃
        ['q'] = 0xFFFFD02A, ['Q'] = 0xFFFFF29A, // 노랑 꽃
    };

    // 사와로: 둥근 머리의 굵은 줄기, 팔은 옆으로 나갔다가 위로(Side -1 왼쪽, +1 오른쪽)
    private static PixelImage Saguaro(int h, bool big, bool flowers, params (int Side, int Attach, int Out, int Up)[] arms)
    {
        double tr = big ? 3.2 : 2.3, ar = big ? 2.1 : 1.5;
        int reach = arms.Length == 0 ? 0 : arms.Max(a => a.Out);
        int w = (reach + 4) * 2 + 9, cx = w / 2;
        var g = new Canvas(w, h);
        g.Stroke(cx, tr, cx, h + 4, tr, 'g');
        foreach (var (side, attach, outLen, up) in arms)
        {
            int ay = h - 1 - attach, ex = cx + side * outLen;
            g.Stroke(cx, ay, ex, ay, ar, 'g');
            g.Stroke(ex, ay, ex, ay - up, ar, 'g');
            if (big)
                for (int y = ay - up; y < ay - 1; y++)
                    if (g[ex, y] == 'g' && g[ex, y - 1] != '.') g[ex, y] = 'r';
        }
        for (int y = (int)tr + 1; y < h; y++)
            foreach (int dx in big ? new[] { -1, 1 } : [0])
                if (g[cx + dx, y] == 'g') g[cx + dx, y] = 'r';
        if (flowers)
        {
            g[cx - 1, 0] = 'f'; g[cx, 0] = 'c'; g[cx + 1, 0] = 'f'; g[cx, 1] = 'f';
            foreach (var (side, attach, outLen, up) in arms)
                g[cx + side * outLen, h - 1 - attach - up - (int)ar] = 'f';
        }
        return Toon(g.Rows(), new() { ['g'] = CactusGreen, ['r'] = CactusRib }, DesertBits);
    }

    // 둥근 통선인장: 위아래로 모이는 세로 골, 둘레에 가시, 머리에 꽃
    private static PixelImage Barrel(int w, int h, char flower, char flowerLight, uint seed)
    {
        var g = new Canvas(w + 2, h + 2);
        var rng = new Lcg(seed);
        int eh = h + 3;
        double cx = (w - 1) / 2.0, cy = (eh - 1) / 2.0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!InEllipse(w, eh, x, y)) continue;
                double v = (y - cy) / (eh / 2.0);
                double u = (x - cx) / Math.Sqrt(Math.Max(0.05, 1 - v * v));
                g[x + 1, y + 2] = Math.Abs(u - Math.Round(u / 2.6) * 2.6) < 0.5 ? 'r' : 'g';
            }
        for (int y = 3; y < h + 1; y += 2)
        {
            int left = 0, right = w + 1;
            while (left < w + 1 && g[left, y] == '.') left++;
            while (right > 0 && g[right, y] == '.') right--;
            if (rng.Next(3) > 0) g[left - 1, y] = 'y';
            if (rng.Next(3) > 0) g[right + 1, y] = 'y';
        }
        int c = (w + 1) / 2;
        g[c, 1] = 'c'; g[c - 1, 1] = flowerLight; g[c + 1, 1] = flower; g[c, 0] = flowerLight; g[c - 1, 0] = flower; g[c + 1, 0] = flower;
        g[c - 2, 1] = flower; g[c + 2, 1] = flower;
        return Toon(g.Rows(), new() { ['g'] = BarrelGreen, ['r'] = BarrelRib }, DesertBits);
    }

    // 부채선인장: 납작한 타원 잎 여러 장(위 잎이 앞), 잎마다 가시 점, 위에 붉은 열매
    private static PixelImage PricklyPear(int fruit, params (int X, int Y, int W, int H)[] pads)
    {
        int w = pads.Max(p => p.X + p.W), h = pads.Max(p => p.Y + p.H);
        var parts = new List<(PixelImage, int, int)>();
        foreach (var (px, py, pw, ph) in pads)
        {
            var g = new Canvas(pw, ph);
            for (int y = 0; y < ph; y++)
                for (int x = 0; x < pw; x++)
                    if (InEllipse(pw, ph, x, y)) g[x, y] = (x * 7 + y * 13 + px * 5) % 9 == 0 && y > 1 && y < ph - 2 && x > 1 && x < pw - 2 ? 'y' : 'n';
            parts.Add((Toon(g.Rows(), new() { ['n'] = PadGreen }, DesertBits), px, py));
        }
        string[] fruitArt = [".o.", "oLo", "oDo", ".o."];
        var top = pads.OrderBy(p => p.Y).First();
        for (int i = 0; i < fruit; i++)
        {
            var f = Art(fruitArt, new Dictionary<char, uint> { ['o'] = PearFruit.Out, ['L'] = PearFruit.Light, ['D'] = PearFruit.Base });
            parts.Add((f, top.X + top.W / 2 - 3 + i * 3, top.Y - 2));
        }
        int lift = fruit > 0 ? 2 : 0;
        return Compose(w, h + lift, parts.Select(p => (p.Item1, p.Item2, p.Item3 + lift)));
    }

    // 로제트(용설란·알로에): 가운데 잎부터 바깥 잎 순서로 겹쳐 놓은 뾰족한 잎
    private static PixelImage Rosette(int size, Mat mat, bool spots)
    {
        int w = size * 2 + 6, h = size + 2;
        double bx = w / 2.0, by = h - 1;
        double[] angles = [-90, -107, -73, -124, -56, -142, -38];
        double[] lengths = [1.0, 0.97, 0.95, 0.88, 0.86, 0.72, 0.7];
        var parts = new List<(PixelImage, int, int)>();
        for (int i = 0; i < angles.Length; i++)
        {
            var g = new Canvas(w, h);
            double len = size * lengths[i], width = size / 12.0 + 0.55;
            double x = bx, y = by;
            for (double t = 0; t < len; t += 0.5)
            {
                double a = (angles[i] + (angles[i] + 90) * 0.4 * t / len) * Math.PI / 180;
                x += Math.Cos(a) * 0.5; y += Math.Sin(a) * 0.5;
                g.Disc(x, y, width * (1 - t / len) + 0.4, 'a');
            }
            if (spots)
                for (int sy = 1; sy < h - 1; sy++)
                    for (int sx = 1; sx < w - 1; sx++)
                        if (g[sx, sy] == 'a' && g[sx - 1, sy] == 'a' && g[sx + 1, sy] == 'a' && g[sx, sy - 1] == 'a' && g[sx, sy + 1] == 'a' && (sx * 5 + sy * 3) % 7 == 0)
                            g[sx, sy] = 'y';
            parts.Add((Toon(g.Rows(), new() { ['a'] = mat }, DesertBits, outer: true), 0, 0));
        }
        return Compose(w + 2, h + 2, parts);
    }

    // 회전초: 마른 잔가지 호를 둥글게 엉켜 놓은 공
    private static PixelImage Tumbleweed(int d, uint seed)
    {
        var rng = new Lcg(seed);
        var img = new PixelImage(d, d);
        double c = (d - 1) / 2.0, r = d / 2.0 - 0.5;
        uint[] tones = [0xFFE6CC90, 0xFFC8A060, 0xFF9A7440, 0xFF6A4C28];
        for (int k = 0; k < d * 3 / 2; k++)
        {
            double rr = r * (0.35 + rng.Next(66) / 100.0), ox = (rng.Next(7) - 3) * r / 10, oy = (rng.Next(7) - 3) * r / 10;
            double a0 = rng.Next(360) * Math.PI / 180, span = (50 + rng.Next(110)) * Math.PI / 180;
            for (double a = a0; a < a0 + span; a += 0.5 / Math.Max(1, rr))
            {
                double x = c + ox + Math.Cos(a) * rr, y = c + oy + Math.Sin(a) * rr;
                int ix = (int)Math.Round(x), iy = (int)Math.Round(y);
                if ((ix - c) * (ix - c) + (iy - c) * (iy - c) > r * r) continue;
                double s = ((ix - c) + (iy - c)) / r;
                int tone = s < -0.6 ? 0 : s < 0.1 ? 1 : s < 0.8 ? 2 : 3;
                if (img.Contains(ix, iy)) img.Set(ix, iy, new Color32(tones[tone]));
            }
        }
        return img;
    }

    // 바위: o 테두리, H 밝은 곳, l 바탕, m 그늘
    private static readonly string[] BigRock =
    [
        "....oooo......",
        "..ooHHllo.....",
        ".oHHlllmooooo.",
        ".oHllllmoHHllo",
        "oHllllmmolllmo",
        "oHlllllmmllmmo",
        "olllllmmmmmmmo",
        "oooooooooooooo",
    ];

    private static readonly string[] SmallRock =
    [
        "..oooo...",
        ".oHHllo..",
        "oHlllmmo.",
        "olllmmmmo",
        "ooooooooo",
    ];

    private static PixelImage Rock(string[] art, Mat mat)
        => Art(art, new Dictionary<char, uint> { ['o'] = mat.Out, ['H'] = mat.Light, ['l'] = mat.Base, ['m'] = mat.Dark });

    /// <summary>선인장과 사막 식물: 사와로(팔 0~3개, 크기 여러 가지), 통선인장, 부채선인장, 꽃 핀 작은 선인장, 용설란·알로에, 회전초, 바위. 좌우로 뒤집은 것도.</summary>
    public static StampSet Cacti()
    {
        var shapes = new List<PixelImage>
        {
            Saguaro(34, true, false, (-1, 11, 8, 12), (1, 16, 7, 10), (1, 6, 5, 4)),
            Saguaro(26, true, true, (-1, 8, 7, 9), (1, 12, 7, 8)),
            Saguaro(22, false, false, (-1, 7, 5, 6), (1, 10, 5, 7)),
            Saguaro(18, false, false, (1, 6, 5, 7)),
            Saguaro(12, false, false),
            Barrel(11, 12, 'p', 'P', 5),
            Barrel(9, 8, 'q', 'Q', 9),
            Compose(16, 10, [(Barrel(7, 6, 'p', 'P', 2), 0, 2), (Barrel(9, 8, 'p', 'P', 4), 6, 0)]),
            Barrel(7, 6, 'q', 'Q', 7),
            PricklyPear(2, (0, 2, 7, 9), (8, 0, 7, 10), (3, 8, 9, 12)),
            PricklyPear(0, (5, 0, 7, 8), (0, 6, 8, 10)),
            PricklyPear(1, (6, 0, 6, 8), (0, 5, 8, 11)),
            Rosette(12, AgaveBlue, false),
            Rosette(9, AloeGreen, true),
            Tumbleweed(12, 3),
            Tumbleweed(9, 8),
            Rock(BigRock, Sandstone),
            Rock(SmallRock, GrayRock),
            Rock(SmallRock, Sandstone),
        };
        return Set("Cacti & Desert Plants", Mirrored(shapes));
    }

    // ───────────── 나무 공통 ─────────────

    private static readonly Mat Bark = new(0xFF2A180C, 0xFF4E2E16, 0xFF6E4424, 0xFF906038);

    // 줄기 칸: 테두리·잎 바로 아래는 어둡게, 가운데 왼쪽은 밝게
    private static uint BarkColor(Func<int, int, bool> trunk, Func<int, int, bool> crown, int x, int y, int cx)
        => Edge(trunk, x, y) || crown(x, y - 1) ? Bark.Out : x < cx ? Bark.Light : x == cx ? Bark.Base : Bark.Dark;

    // ───────────── 침엽수 ─────────────

    private static readonly Mat PineDark = new(0xFF0C2818, 0xFF174A2A, 0xFF22663A, 0xFF3A8A4C);
    private static readonly Mat PineBlue = new(0xFF0C2830, 0xFF1A4C54, 0xFF286C6E, 0xFF48928A);
    private static readonly Mat PineYellow = new(0xFF22380E, 0xFF40681C, 0xFF5E8E28, 0xFF8CB848);
    private const uint Snow = 0xFFF4F8FF, SnowShade = 0xFFC4D4EA, SnowEdge = 0xFF8CA2C0;

    /// <summary>
    /// 층층이 쌓인 침엽수: 위층이 아래층 윗부분을 덮고, 층 아랫단은 톱니, 층 바로 밑은 그늘. 왼쪽이 밝다.
    /// <paramref name="snow"/>면 층마다 위로 드러난 면에 눈.
    /// </summary>
    private static PixelImage Pine(int height, double widthScale, Mat leaf, bool snow, uint seed)
    {
        int trunkH = Math.Max(2, height / 7), crownH = height - trunkH;
        int tiers = Math.Clamp((crownH + 4) / 7, 2, 5);
        double maxHalf = Math.Max(3.0, crownH * 0.3 * widthScale);
        int w = (int)Math.Ceiling(maxHalf) * 2 + 3, cx = w / 2;
        var top = new int[tiers];
        var bot = new int[tiers];
        var halfT = new double[tiers];
        var halfB = new double[tiers];
        for (int i = 0; i < tiers; i++)
        {
            bot[i] = (int)Math.Round(crownH * (i + 1.0) / tiers) - 1;
            top[i] = i == 0 ? 0 : (int)Math.Round(bot[i - 1] - (bot[i - 1] - top[i - 1]) * 0.5);
            halfB[i] = maxHalf * (0.4 + 0.6 * (i + 1.0) / tiers);
            halfT[i] = i == 0 ? 0 : halfB[i - 1] * 0.3;
        }
        double Half(int i, int y) => halfT[i] + (halfB[i] - halfT[i]) * (y - top[i]) / Math.Max(1.0, bot[i] - top[i]);
        int Tier(int x, int y)
        {
            for (int i = 0; i < tiers; i++)
            {
                if (y < top[i] || y > bot[i]) continue;
                int dx = Math.Abs(x - cx);
                if (dx > Half(i, y) + 0.3) continue;
                if (y == bot[i] && dx > 0 && (x + i) % 3 == 0) continue;
                return i;
            }
            return -1;
        }
        int tw = height >= 22 ? 1 : 0;
        bool Trunk(int x, int y) => y >= crownH - 2 && y < height && Math.Abs(x - cx) <= tw;
        bool Crown(int x, int y) => Tier(x, y) >= 0;
        bool In(int x, int y) => Crown(x, y) || Trunk(x, y);
        bool SnowTop(int x, int y) => snow && Crown(x, y) && !In(x, y - 1);
        var rng = new Lcg(seed);
        var img = new PixelImage(w, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < w; x++)
            {
                int t = Tier(x, y), fleck = rng.Next(8);
                if (t < 0)
                {
                    if (Trunk(x, y)) img.Set(x, y, new Color32(BarkColor(Trunk, Crown, x, y, cx)));
                    continue;
                }
                double u = (x - cx) / Math.Max(1.0, Half(t, y));
                int above = Tier(x, y - 1), above2 = Tier(x, y - 2);
                uint c;
                if (SnowTop(x, y)) c = SnowEdge;
                else if (Edge(Crown, x, y)) c = leaf.Out;
                else if (SnowTop(x, y - 1)) c = u > 0.35 ? SnowShade : Snow;
                else if (SnowTop(x, y - 2) && u < 0 && fleck < 5) c = Snow;
                else if (above >= 0 && above < t) c = leaf.Out;
                else if (above2 >= 0 && above2 < t) c = leaf.Dark;
                else if (u < -0.3) c = fleck == 0 ? leaf.Base : leaf.Light;
                else if (u > 0.35) c = fleck == 0 ? leaf.Base : leaf.Dark;
                else c = fleck == 0 ? (u < 0 ? leaf.Light : leaf.Dark) : leaf.Base;
                img.Set(x, y, new Color32(c));
            }
        return img;
    }

    /// <summary>옆에서 본 침엽수: 높이 다섯 가지 남짓, 짙은 초록·청록·연두, 눈 덮인 것도.</summary>
    public static StampSet PineTrees()
    {
        var shapes = new List<PixelImage>
        {
            Pine(40, 1.0, PineDark, false, 1),
            Pine(32, 0.9, PineBlue, false, 2),
            Pine(26, 1.0, PineYellow, false, 3),
            Pine(20, 1.1, PineDark, false, 4),
            Pine(16, 1.0, PineBlue, false, 5),
            Pine(12, 1.1, PineYellow, false, 6),
            Pine(36, 0.85, PineBlue, false, 7),
            Pine(22, 0.95, PineYellow, false, 8),
            Pine(34, 1.0, PineDark, true, 9),
            Pine(24, 1.0, PineBlue, true, 10),
            Pine(16, 1.0, PineDark, true, 11),
            Pine(28, 0.9, PineBlue, true, 12),
        };
        return Set("Pine Trees", shapes);
    }

    // ───────────── 둥근 나무·덤불 ─────────────

    private static readonly Mat LeafLight = new(0xFF24501A, 0xFF3E8228, 0xFF5CA834, 0xFF8CD04E);
    private static readonly Mat LeafMid = new(0xFF1A4418, 0xFF2E6E2A, 0xFF46923A, 0xFF70B852);
    private static readonly Mat LeafDark = new(0xFF0E3018, 0xFF1C5A28, 0xFF2C7A34, 0xFF4A9E44);
    private static readonly Mat LeafAutumn = new(0xFF5A2008, 0xFFB0480E, 0xFFE0781A, 0xFFFFB040);
    private static readonly Mat LeafBlossom = new(0xFF7A2E54, 0xFFD06A98, 0xFFF4A0C0, 0xFFFFD8E6);

    /// <summary>
    /// 잎 뭉치(원) 여러 개로 된 둥근 수관: 뭉치마다 왼쪽 위가 밝고, 앞 뭉치 오른쪽 아래 틈은 그늘. 나무는 아래에 줄기,
    /// 덤불(<paramref name="bush"/>)은 줄기 없이 납작하게 땅에 붙는다. <paramref name="dots"/>는 꽃·단풍 점.
    /// </summary>
    private static PixelImage RoundTree(int d, int trunkH, Mat leaf, uint seed, uint[]? dots = null, bool bush = false)
    {
        var rng = new Lcg(seed);
        double rx = d / 2.0, ry = bush ? d * 0.42 : d / 2.0;
        int w = d + 6, cx = w / 2;
        int h = bush ? (int)Math.Ceiling(ry * 1.6) + 1 : d + 3 + trunkH;
        double cy = bush ? h - 1 - ry * 0.6 : rx + 2;
        var back = new List<(double X, double Y, double R)>
        {
            (cx - rx * 0.4, cy + ry * 0.3, Math.Min(rx, ry) * 0.6 + 0.5),
            (cx + rx * 0.4, cy + ry * 0.3, Math.Min(rx, ry) * 0.6 + 0.5),
            (cx, cy, Math.Min(rx, ry) * 0.85),
        };
        int n = 5 + rng.Next(3);
        var ring = new List<(double X, double Y, double R)>();
        for (int k = 0; k < n; k++)
        {
            double a = Math.PI * (0.92 + 1.16 * (k + 0.5) / n) + (rng.Next(21) - 10) / 100.0;
            double size = Math.Min(rx, ry) * (0.42 + rng.Next(14) / 100.0) + 0.5;
            ring.Add((cx + Math.Cos(a) * (rx - size) * 0.95, cy + Math.Sin(a) * (ry - size) * 0.95, size));
        }
        var clumps = back.Concat(ring.OrderByDescending(c => c.Y)).ToList();
        int Clump(int x, int y)
        {
            if (y < 0 || y >= h) return -1;
            int found = -1;
            for (int i = 0; i < clumps.Count; i++)
            {
                var (px, py, pr) = clumps[i];
                if ((x - px) * (x - px) + (y - py) * (y - py) <= pr * pr) found = i;
            }
            return found;
        }
        int tw = d >= 26 ? 2 : d >= 14 ? 1 : 0;
        bool Crown(int x, int y) => Clump(x, y) >= 0;
        bool Trunk(int x, int y) => !bush && y >= cy && y < h && Math.Abs(x - cx) <= tw + (y == h - 1 && tw > 0 ? 1 : 0);
        var img = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = Clump(x, y), fleck = rng.Next(9), pick = rng.Next(8);
                if (i < 0)
                {
                    if (Trunk(x, y)) img.Set(x, y, new Color32(BarkColor(Trunk, Crown, x, y, cx)));
                    continue;
                }
                var (px, py, pr) = clumps[i];
                uint c;
                if (Edge(Crown, x, y)) c = leaf.Out;
                else if (Clump(x - 1, y) > i || Clump(x, y - 1) > i) c = leaf.Dark;
                else if (dots is not null && pick == 0 && fleck < 6) c = dots[fleck % dots.Length];
                else
                {
                    double l = -((x - px) + (y - py)) / (pr * 1.41) * 0.75 - ((x - cx) / rx + (y - cy) / ry) * 0.25;
                    int tone = l > 0.32 ? 3 : l < -0.28 ? 1 : 2;
                    if (fleck == 0) tone += l > 0 ? -1 : 1;
                    c = Math.Clamp(tone, 1, 3) switch { 3 => leaf.Light, 1 => leaf.Dark, _ => leaf.Base };
                }
                img.Set(x, y, new Color32(c));
            }
        return img;
    }

    /// <summary>옆에서 본 활엽수(둥근 수관, 크기·초록 여러 가지, 단풍, 꽃나무)와 줄기 없는 덤불.</summary>
    public static StampSet RoundTrees()
    {
        uint[] autumn = [0xFFC8301A, 0xFFFFD050];
        uint[] blossom = [0xFFFFFFFF, 0xFFE0609A];
        uint[] flowers = [0xFFE83848, 0xFFFFFFFF, 0xFFFFD040];
        var shapes = new List<PixelImage>
        {
            RoundTree(26, 8, LeafLight, 1),
            RoundTree(16, 5, LeafLight, 2),
            RoundTree(30, 9, LeafDark, 3),
            RoundTree(20, 6, LeafDark, 4),
            RoundTree(11, 4, LeafMid, 5),
            RoundTree(18, 6, LeafMid, 8),
            RoundTree(22, 7, LeafAutumn, 6, autumn),
            RoundTree(22, 6, LeafBlossom, 7, blossom),
            RoundTree(14, 0, LeafLight, 11, bush: true),
            RoundTree(20, 0, LeafDark, 12, bush: true),
            RoundTree(10, 0, LeafMid, 13, bush: true),
            RoundTree(16, 0, LeafDark, 14, flowers, bush: true),
            RoundTree(15, 0, LeafAutumn, 17, autumn, bush: true),
        };
        return Set("Round Trees & Bushes", shapes);
    }
}
