using Chickxel.Core.Edit;   // StampOrder
using Chickxel.Core.IO;     // StampSet, ShapeArt
using Chickxel.Core.Model;  // PixelImage, Color32
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 크리스마스 파티 모양 펜 세트 다섯 가지: 선물 상자, 쿠키·사탕, 눈사람, 호랑가시·화환, 양말·산타 모자.
/// 트리 아래나 바닥에 흩어 찍기 좋은 것들. 손으로 그린 글자 그림과 작은 그리기 함수(상자·지팡이 사탕·잎·화환)를 섞고,
/// 빛은 왼쪽 위에서, 테두리는 바탕보다 어두운 같은 계열 색. 같은 코드면 늘 같은 모양이다.
/// </summary>
internal static class ChristmasPartyShapeSets
{
    // ───────────── 선물 상자 ─────────────

    private enum Pattern { None, Stripes, Dots, Bands, Checks }
    private enum BowKind { None, Small, Big }

    // 포장지: o 테두리, H 밝은 곳, l 바탕, m 그늘, p 무늬, q 그늘 속 무늬
    private static Dictionary<char, uint> Wrap(uint o, uint h, uint l, uint m, uint p, uint q)
        => new() { ['o'] = o, ['H'] = h, ['l'] = l, ['m'] = m, ['p'] = p, ['q'] = q };

    private static readonly Dictionary<char, uint> RedWrap = Wrap(0xFF5A0C14, 0xFFF26A6E, 0xFFD8283A, 0xFFA8182A, 0xFFF4E8D8, 0xFFC8B4B0);
    private static readonly Dictionary<char, uint> GreenWrap = Wrap(0xFF0C3A1C, 0xFF70D07C, 0xFF2CA04A, 0xFF1C7A38, 0xFFF4E8D8, 0xFFB4C8AC);
    private static readonly Dictionary<char, uint> BlueWrap = Wrap(0xFF0E2260, 0xFF90BCFF, 0xFF3A78F0, 0xFF1E48B0, 0xFFF4F8FF, 0xFFB0C0E0);
    private static readonly Dictionary<char, uint> PurpleWrap = Wrap(0xFF3A1258, 0xFFCC98F0, 0xFF9E50D0, 0xFF76309E, 0xFFF2C440, 0xFFC08A18);
    private static readonly Dictionary<char, uint> GoldWrap = Wrap(0xFF6A4006, 0xFFFFF0A0, 0xFFF2C440, 0xFFC08A18, 0xFFD8283A, 0xFFA8182A);
    private static readonly Dictionary<char, uint> CandyWrap = Wrap(0xFF5A5A74, 0xFFFFFFFF, 0xFFF2F0EC, 0xFFC4C4D4, 0xFFD8283A, 0xFFA8182A);
    private static readonly Dictionary<char, uint> PinkWrap = Wrap(0xFF7A1A4A, 0xFFFFC0DC, 0xFFF078B0, 0xFFC0407A, 0xFFFFFFFF, 0xFFE0B0C8);
    private static readonly Dictionary<char, uint> TealWrap = Wrap(0xFF0A4048, 0xFF88E0D8, 0xFF2CB0B0, 0xFF1A8088, 0xFFF4F8FF, 0xFFB0D0D0);

    // 리본: O 테두리, R 밝은 곳, r 바탕, s 그늘
    private static Dictionary<char, uint> Ribbon(uint o, uint hi, uint r, uint s) => new() { ['O'] = o, ['R'] = hi, ['r'] = r, ['s'] = s };

    private static readonly Dictionary<char, uint> GoldRibbon = Ribbon(0xFF6A4006, 0xFFFFF2B0, 0xFFF2C440, 0xFFC08A18);
    private static readonly Dictionary<char, uint> RedRibbon = Ribbon(0xFF5A0C14, 0xFFFF8A8A, 0xFFE0303E, 0xFFA8182A);
    private static readonly Dictionary<char, uint> SilverRibbon = Ribbon(0xFF5A5A74, 0xFFFFFFFF, 0xFFEEF0F4, 0xFFB4B8C8);
    private static readonly Dictionary<char, uint> GreenRibbon = Ribbon(0xFF0C3A1C, 0xFF90E0A0, 0xFF30B050, 0xFF1C7A38);

    private static readonly string[] BigBow =
    [
        ".OOO...OOO.",
        "ORRrO.ORrsO",
        "ORrrrOrrrsO",
        ".OrrsRsrsO.",
        "..OOOOOOO..",
    ];

    private static readonly string[] SmallBow =
    [
        ".OO.OO.",
        "ORrOrsO",
        ".OrRsO.",
        "..OOO..",
    ];
    /// <summary>
    /// 선물 상자 하나: 가로 <paramref name="w"/>(홀수), 세로 <paramref name="h"/>. 십자 리본(뚜껑 상자는 세로 리본만), 무늬, 위에 나비 리본.
    /// </summary>
    private static PixelImage Box(int w, int h, Dictionary<char, uint> wrap, Dictionary<char, uint> ribbon,
        Pattern pattern = Pattern.None, BowKind bow = BowKind.Small, bool lid = false)
    {
        const int lidH = 4;
        bool In(int x, int y) => y >= 0 && y < h && x >= 0 && x < w && (!lid || y < lidH || (x >= 1 && x < w - 1));
        bool Line(int x, int y) => In(x, y) && (Edge(In, x, y) || (lid && y == lidH - 1));
        int cx = w / 2, rw = w >= 11 ? 1 : 0, cy = lid ? -99 : h / 2, rh = h >= 9 ? 1 : 0;
        bool Patterned(int x, int y) => pattern switch
        {
            Pattern.Stripes => (x + y) % 5 < 2,
            Pattern.Dots => (x % 4 == 1 && y % 4 == 1) || (x % 4 == 3 && y % 4 == 3),
            Pattern.Bands => y % 4 == 1,
            Pattern.Checks => x % 4 == 2 || y % 4 == 2,
            _ => false,
        };
        var rows = Rows(w, h, (x, y) =>
        {
            if (!In(x, y)) return '.';
            bool vr = Math.Abs(x - cx) <= rw, hr = Math.Abs(y - cy) <= rh;
            if (Line(x, y)) return vr || hr ? 'O' : 'o';
            if (vr && hr) return 'r';
            if (vr) return rw == 0 ? 'r' : "Rrs"[x - cx + 1];
            if (hr) return rh == 0 ? 'r' : "Rrs"[y - cy + 1];
            char tone = Line(x - 1, y) || Line(x, y - 1) ? 'H' : Line(x + 1, y) || Line(x, y + 1) ? 'm' : 'l';
            return Patterned(x, y) ? (tone == 'm' ? 'q' : 'p') : tone;
        });
        var box = Art(rows, wrap, ribbon);
        if (bow == BowKind.None) return box;
        var knot = Art(bow == BowKind.Big ? BigBow : SmallBow, ribbon);
        return Compose(w, h + knot.Height - 1, [(box, 0, knot.Height - 1), (knot, cx - knot.Width / 2, 0)]);
    }

    /// <summary>상자 쌓기(아래 상자부터, 각 상자는 가운데에서 <c>Dx</c>만큼 비켜 놓음).</summary>
    private static PixelImage Stack(params (PixelImage Box, int Dx)[] boxes)
    {
        int shift = boxes.Max(b => Math.Abs(b.Dx));
        int w = boxes.Max(b => b.Box.Width) + 2 * shift, h = boxes.Sum(b => b.Box.Height) - (boxes.Length - 1);
        var parts = new List<(PixelImage, int, int)>();
        int y = h;
        foreach (var (box, dx) in boxes)
        {
            y -= box.Height;
            parts.Add((box, (w - box.Width) / 2 + dx, y));
            y += 1;
        }
        return Compose(w, h, parts);
    }

    /// <summary>선물 상자: 여덟 가지 포장지(줄무늬·물방울·띠·체크), 정육면체·키 큰·납작한·작은 상자, 뚜껑 상자, 두세 개 쌓은 더미.</summary>
    public static StampSet Presents() => Set("Christmas Presents",
    [
        Box(13, 11, RedWrap, GoldRibbon, Pattern.None, BowKind.Big),
        Box(11, 15, GreenWrap, RedRibbon, Pattern.Dots, BowKind.Big, lid: true),
        Box(19, 7, BlueWrap, SilverRibbon, Pattern.Stripes, BowKind.Small),
        Box(11, 11, PurpleWrap, GoldRibbon, Pattern.Dots, BowKind.Small, lid: true),
        Box(9, 9, CandyWrap, GreenRibbon, Pattern.Stripes, BowKind.Small),
        Box(7, 7, GoldWrap, RedRibbon, Pattern.None, BowKind.Small),
        Box(13, 9, PinkWrap, SilverRibbon, Pattern.Bands, BowKind.Big),
        Box(9, 13, TealWrap, GoldRibbon, Pattern.Checks, BowKind.Small, lid: true),
        Box(7, 6, GreenWrap, GoldRibbon, Pattern.None, BowKind.Small),
        Box(21, 6, RedWrap, GreenRibbon, Pattern.Checks, BowKind.Small),
        Stack((Box(17, 9, BlueWrap, GoldRibbon, Pattern.Dots, BowKind.None), 0), (Box(11, 8, RedWrap, SilverRibbon, Pattern.Stripes, BowKind.Small), 0)),
        Stack((Box(19, 8, GreenWrap, RedRibbon, Pattern.Checks, BowKind.None), 0), (Box(13, 7, PurpleWrap, SilverRibbon, Pattern.None, BowKind.None), 1), (Box(7, 6, GoldWrap, RedRibbon, Pattern.None, BowKind.Small), -1)),
        Stack((Box(15, 10, PinkWrap, GoldRibbon, Pattern.Dots, BowKind.None), 0), (Box(9, 7, CandyWrap, GreenRibbon, Pattern.Stripes, BowKind.Small), -2)),
    ]);

    // ───────────── 쿠키·사탕 ─────────────

    // 생강 쿠키: o 테두리, H·l·d 반죽, e 눈, W 아이싱
    private static readonly Dictionary<char, uint> Ginger = new()
        { ['o'] = 0xFF4A2410, ['H'] = 0xFFD8955A, ['l'] = 0xFFB06A34, ['d'] = 0xFF7E4420, ['e'] = 0xFF2A1408, ['W'] = 0xFFFFFFFF };
    private static readonly Dictionary<char, uint> GumdropButtons = new() { ['1'] = 0xFFE0303E, ['2'] = 0xFF30B050 };
    private static readonly Dictionary<char, uint> IcingButtons = new() { ['1'] = 0xFFFFFFFF, ['2'] = 0xFFFFFFFF };

    // 설탕 쿠키 반죽 테두리
    private static readonly Dictionary<char, uint> Sugar = new() { ['o'] = 0xFF7A4A1E, ['H'] = 0xFFF8DCA8, ['l'] = 0xFFE8B878, ['d'] = 0xFFC08848 };

    // 아이싱: I 밝은 곳, i 바탕, j 그늘
    private static Dictionary<char, uint> Icing(uint hi, uint i, uint j) => new() { ['I'] = hi, ['i'] = i, ['j'] = j };
    private static readonly Dictionary<char, uint> WhiteIcing = Icing(0xFFFFFFFF, 0xFFF0F2F8, 0xFFC4CCDC);
    private static readonly Dictionary<char, uint> GreenIcing = Icing(0xFF8EE09A, 0xFF3CB850, 0xFF228A3A);
    private static readonly Dictionary<char, uint> YellowIcing = Icing(0xFFFFF4A8, 0xFFF8D848, 0xFFD8A828);
    private static readonly Dictionary<char, uint> BlueIcing = Icing(0xFFB8DCFF, 0xFF6AA8F0, 0xFF3A78C8);

    // 스프링클: 1 빨강, 2 흰색, 3 파랑, 4 노랑
    private static readonly Dictionary<char, uint> Sprinkles = new() { ['1'] = 0xFFE0303E, ['2'] = 0xFFFFFFFF, ['3'] = 0xFF3A78F0, ['4'] = 0xFFFFD43A };

    private static readonly string[] GingerMan = Shade(Symmetric(
    [
        "....ooo",
        "...olll",
        "...olel",
        "...olWW",
        "....oll",
        ".ooolll",
        "olWllll",
        "olWlll1",
        ".ooolll",
        "...olll",
        "...oll2",
        "..ollll",
        "..olloo",
        ".olWlo.",
        ".ooooo.",
    ]));

    private static readonly string[] SmallGingerMan = Shade(Symmetric(
    [
        "...oo",
        "..oll",
        "..oel",
        "..olW",
        "...ol",
        ".oool",
        "olll1",
        ".ooll",
        "..oll",
        "..ol2",
        ".olll",
        ".oloo",
        ".oo..",
    ]));

    private static string[] Iced(string[] rows, bool shadeIcing = true) => Shade(shadeIcing ? Shade(rows, "i", "I", "j", ".ol") : rows);

    private static readonly string[] StarCookie = Iced(Symmetric(
    [
        "......o",
        ".....ol",
        ".....ol",
        "....oli",
        "oooooli",
        "ollli1i",
        ".olliii",
        "..oli3i",
        "..oliii",
        ".olliio",
        ".olloo.",
        ".ooo...",
    ]));

    private static readonly string[] TreeCookie = Iced(shadeIcing: false, rows: Symmetric(
    [
        ".....o",
        "....ol",
        "...ol4",
        "...oli",
        "..oli1",
        "...oli",
        "..olii",
        ".oli2i",
        "..olii",
        ".oliii",
        "oli1ii",
        "ooooll",
        "...oll",
        "...ooo",
    ]));

    private static readonly string[] BellCookie = Iced(Symmetric(
    [
        "....oo",
        "...oll",
        "..olii",
        "..oli1",
        "..olii",
        ".oliii",
        ".oli1i",
        "oll222",
        "olllll",
        ".ooooo",
        "....ol",
        "....oo",
    ]));

    private static readonly string[] SnowflakeDecor =
    [
        "...2...",
        ".2.2.2.",
        "..222..",
        "2222222",
        "..222..",
        ".2.2.2.",
        "...2...",
    ];

    /// <summary>둥근 설탕 쿠키: 반죽 테두리 1px, 안은 아이싱, 가운데 그림.</summary>
    private static string[] RoundCookie(int size, string[] decor)
    {
        bool In(int x, int y) => x >= 0 && y >= 0 && x < size && y < size && InCircle(size, x, y);
        bool Inner(int x, int y) => In(x, y) && !Edge(In, x, y);
        int dx = (size - decor[0].Length) / 2, dy = (size - decor.Length) / 2;
        return Iced(Rows(size, size, (x, y) =>
        {
            if (!In(x, y)) return '.';
            if (Edge(In, x, y)) return 'o';
            if (Edge(Inner, x, y)) return 'l';
            int u = x - dx, v = y - dy;
            return v >= 0 && v < decor.Length && u >= 0 && u < decor[v].Length && decor[v][u] != '.' ? decor[v][u] : 'i';
        }));
    }

    // 지팡이 사탕: o 테두리, R·r·s 빨강(밝은 곳·바탕·그늘), W·w·v 흰색
    private static readonly Dictionary<char, uint> RedCane = new()
        { ['o'] = 0xFF6A0C16, ['R'] = 0xFFFF7A80, ['r'] = 0xFFE0303E, ['s'] = 0xFFA8182A, ['W'] = 0xFFFFFFFF, ['w'] = 0xFFF0ECEC, ['v'] = 0xFFC4BCC8 };
    private static readonly Dictionary<char, uint> GreenCane = new()
        { ['o'] = 0xFF0C3A1C, ['R'] = 0xFF7AE08A, ['r'] = 0xFF2CA04A, ['s'] = 0xFF1C7A38, ['W'] = 0xFFFFFFFF, ['w'] = 0xFFF0ECEC, ['v'] = 0xFFC4BCC8 };

    /// <summary>줄무늬 사탕 칠하기: <paramref name="core"/> 안은 줄무늬, 둘레 1px 테두리, 왼쪽 위 밝게·오른쪽 아래 어둡게.</summary>
    private static PixelImage CandyStripes(int w, int h, Func<int, int, bool> core, Func<int, int, bool> red, Dictionary<char, uint> colors)
    {
        bool C(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && core(x, y);
        return Art(Rows(w, h, (x, y) =>
        {
            if (!C(x, y)) return C(x - 1, y) || C(x + 1, y) || C(x, y - 1) || C(x, y + 1) ? 'o' : '.';
            int tone = !C(x - 1, y) || !C(x, y - 1) ? 0 : !C(x + 1, y) || !C(x, y + 1) ? 2 : 1;
            return (red(x, y) ? "Rrs" : "Wwv")[tone];
        }), colors);
    }

    /// <summary>손잡이가 왼쪽으로 굽은 지팡이 사탕(굵기 <paramref name="thick"/>, 고리 구멍 반지름 <paramref name="hole"/>).</summary>
    private static PixelImage HookCane(int thick, double hole, int height, Dictionary<char, uint> colors)
    {
        double outer = hole + thick, ccx = 1 + outer, ccy = 1 + outer;
        int w = (int)Math.Ceiling(2 * outer) + 2;
        bool Core(int x, int y)
        {
            double px = x + 0.5, py = y + 0.5;
            if (py < ccy)
            {
                double d = Math.Sqrt((px - ccx) * (px - ccx) + (py - ccy) * (py - ccy));
                return d >= hole && d < outer;
            }
            if (px >= ccx + hole && px < ccx + outer) return py < height - 1;
            return px >= ccx - outer && px < ccx - hole && py < ccy + thick * 0.7;
        }
        return CandyStripes(w, height, Core, (x, y) => (x + y) % 4 < 2, colors);
    }

    /// <summary>비스듬히 놓인 곧은 막대 사탕.</summary>
    private static PixelImage CandyStick(int len, double thick, Dictionary<char, uint> colors)
    {
        int s = len + 4;
        double ax = 2, ay = len + 1, bx = len + 1, by = 2;
        bool Core(int x, int y)
        {
            double px = x + 0.5, py = y + 0.5, vx = bx - ax, vy = by - ay;
            double t = Math.Clamp(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy), 0, 1);
            double qx = ax + t * vx - px, qy = ay + t * vy - py;
            return qx * qx + qy * qy <= thick * thick / 4;
        }
        return CandyStripes(s, s, Core, (x, y) => x % 4 < 2, colors);
    }

    // 박하사탕: o 테두리, r·s 빨강, w·v 흰색, G 반짝임
    private static readonly Dictionary<char, uint> RedMint = new()
        { ['o'] = 0xFF7A1018, ['r'] = 0xFFE0303E, ['s'] = 0xFFA8182A, ['w'] = 0xFFF8F4F4, ['v'] = 0xFFC8C0CC, ['G'] = 0xFFFFFFFF };
    private static readonly Dictionary<char, uint> GreenMint = new()
        { ['o'] = 0xFF0C3A1C, ['r'] = 0xFF2CA04A, ['s'] = 0xFF1C7A38, ['w'] = 0xFFF8F4F4, ['v'] = 0xFFC8C0CC, ['G'] = 0xFFFFFFFF };

    /// <summary>소용돌이 박하사탕.</summary>
    private static PixelImage Peppermint(int size, Dictionary<char, uint> colors)
    {
        double c = (size - 1) / 2.0, rad = size / 2.0 - 1;
        bool In(int x, int y) => x >= 0 && y >= 0 && x < size && y < size && InCircle(size, x, y);
        int arms = 6, glint = (int)Math.Round(c - rad * 0.5);
        return Art(Rows(size, size, (x, y) =>
        {
            if (!In(x, y)) return '.';
            if (Edge(In, x, y)) return 'o';
            if (x == glint && y == glint) return 'G';
            double dx = x - c, dy = y - c, r = Math.Sqrt(dx * dx + dy * dy) / rad;
            bool red = r > 0.3 && (int)Math.Floor((Math.Atan2(dy, dx) / (2 * Math.PI) + 1) * arms + r * 1.6) % 2 == 0;
            bool shade = (dx + dy) / (rad * 1.414) > 0.3 && r > 0.6;
            return red ? (shade ? 's' : 'r') : (shade ? 'v' : 'w');
        }), colors);
    }

    // 셀로판 꼬임(반투명): C 가장자리, c 안, k 매듭
    private static readonly Dictionary<char, uint> Cellophane = new() { ['C'] = 0xC0B8D0E0, ['c'] = 0x70FFFFFF, ['k'] = 0xD0A0B4C8 };

    private static readonly string[] CellophaneTwist =
    [
        "CC..",
        "CcC.",
        "CccC",
        "Ccck",
        "CccC",
        "CcC.",
        "CC..",
    ];

    /// <summary>셀로판에 싼 박하사탕.</summary>
    private static PixelImage WrappedMint(Dictionary<char, uint> colors)
    {
        var twist = Art(CellophaneTwist, Cellophane);
        return Compose(17, 9, [(twist, 0, 1), (FlipX(twist), 13, 1), (Peppermint(9, colors), 4, 0)]);
    }

    // 포일 사탕: o 테두리, H·l·m 포일, w·d 꼬임, k 매듭
    private static Dictionary<char, uint> Foil(uint o, uint h, uint l, uint m)
        => new() { ['o'] = o, ['H'] = h, ['l'] = l, ['m'] = m, ['w'] = l, ['d'] = m, ['k'] = m };

    private static readonly Dictionary<char, uint> GoldFoil = Foil(0xFF6A4006, 0xFFFFF2B0, 0xFFF2C440, 0xFFC08A18);
    private static readonly Dictionary<char, uint> PurpleFoil = Foil(0xFF3A1258, 0xFFE0B8FF, 0xFFA058D8, 0xFF6E2C9A);
    private static readonly Dictionary<char, uint> GreenFoil = Foil(0xFF0C3A1C, 0xFFA0F0B0, 0xFF34B858, 0xFF1C7A38);

    private static readonly string[] WrappedCandy =
    [
        "oo...ooooo...oo",
        "owo.oHHllmo.owo",
        "owwoHHllllmowwo",
        "owwkHlllllmkwdo",
        "owdolllllmmodwo",
        "odo.olllmmo.odo",
        "oo...ooooo...oo",
    ];

    /// <summary>쿠키·사탕: 생강 쿠키 사람(젤리·아이싱 단추, 작은 것), 별·트리·종·눈송이 쿠키, 지팡이 사탕(양쪽 방향), 막대 사탕, 박하사탕, 포장 사탕.</summary>
    public static StampSet Cookies() => Set("Christmas Cookies & Candy",
    [
        Art(GingerMan, Ginger, GumdropButtons),
        Art(GingerMan, Ginger, IcingButtons),
        Art(SmallGingerMan, Ginger, GumdropButtons),
        Art(StarCookie, Sugar, WhiteIcing, Sprinkles),
        Art(TreeCookie, Sugar, GreenIcing, Sprinkles),
        Art(BellCookie, Sugar, YellowIcing, Sprinkles),
        Art(RoundCookie(13, SnowflakeDecor), Sugar, BlueIcing, Sprinkles),
        .. Mirrored([HookCane(3, 2, 20, RedCane), HookCane(2, 1.5, 14, GreenCane), CandyStick(10, 3.2, RedCane)]),
        Peppermint(11, RedMint),
        Peppermint(9, GreenMint),
        WrappedMint(RedMint),
        Art(WrappedCandy, GoldFoil),
        Art(WrappedCandy, PurpleFoil),
        Art(WrappedCandy, GreenFoil),
    ]);

    // ───────────── 눈사람 ─────────────

    private const uint SnowOutline = 0xFF4E5E80, SnowDeep = 0xFF8A9AC0, SnowShade = 0xFFB4C4E0, SnowBase = 0xFFEAF2FC, SnowWhite = 0xFFFFFFFF;
    private const uint Coal = 0xFF26262E, Carrot = 0xFFF08A24, CarrotTip = 0xFFC85A14;

    /// <summary>눈덩이(<paramref name="mound"/>면 바닥이 평평한 눈 더미): 흰 바탕에 오른쪽 아래로 푸른 회색 그늘.</summary>
    private static PixelImage Snowball(int w, int h, bool mound = false)
    {
        double cx = (w - 1) / 2.0, cy = mound ? h - 0.5 : (h - 1) / 2.0, rx = w / 2.0, ry = mound ? h : h / 2.0;
        bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && (mound ? InEllipse(w, 2 * h, x, y) : InCircle(w, x, y));
        return Paint(w, h, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return new Color32(SnowOutline);
            double nx = (x - cx) / rx, ny = (y - cy) / ry;
            double shade = Math.Sqrt((nx + 0.35) * (nx + 0.35) + (ny + 0.35) * (ny + 0.35));
            double glint = Math.Sqrt((nx + 0.4) * (nx + 0.4) + (ny + 0.4) * (ny + 0.4));
            return new Color32(shade > 1.12 ? SnowDeep : shade > 0.88 ? SnowShade : glint < 0.3 ? SnowWhite : SnowBase);
        });
    }

    private enum Hat { None, Top, Beanie }

    private static readonly Dictionary<char, uint> Twig = new() { ['s'] = 0xFF6A4422 };

    private static readonly string[] StickArm =
    [
        "s.s.....",
        ".ss.....",
        "...ss...",
        ".....sss",
    ];

    // 실크해트: o 테두리, H·k 검정 천, b 띠
    private static readonly Dictionary<char, uint> RedBandHat = new() { ['o'] = 0xFF101018, ['H'] = 0xFF5A5A70, ['k'] = 0xFF2E2E3C, ['b'] = 0xFFC8202C };
    private static readonly Dictionary<char, uint> GreenBandHat = new() { ['o'] = 0xFF101018, ['H'] = 0xFF5A5A70, ['k'] = 0xFF2E2E3C, ['b'] = 0xFF2CA04A };

    private static readonly string[] TopHat =
    [
        "..ooooooo..",
        "..oHkkkko..",
        "..oHkkkko..",
        "..obbbbbo..",
        "oooHkkkkooo",
        "oHkkkkkkkko",
        "ooooooooooo",
    ];

    private static readonly string[] SmallTopHat =
    [
        "..ooooo..",
        "..oHkko..",
        "..obbbo..",
        "oooHkkooo",
        "oHkkkkkko",
        "ooooooooo",
    ];

    // 털모자: o 테두리, H·l·d 뜨개, K·C·c 접단, P·p 방울
    private static Dictionary<char, uint> Knit(uint o, uint h, uint l, uint d)
        => new() { ['o'] = o, ['H'] = h, ['l'] = l, ['d'] = d, ['K'] = 0xFFFFFFFF, ['C'] = 0xFFF4F0EC, ['c'] = 0xFFC8C4D4, ['P'] = 0xFFFFFFFF, ['p'] = 0xFFD0D0DC };

    private static readonly Dictionary<char, uint> RedKnit = Knit(0xFF5A0C14, 0xFFF26A6E, 0xFFD8283A, 0xFFA8182A);
    private static readonly Dictionary<char, uint> GreenKnit = Knit(0xFF0C3A1C, 0xFF70D07C, 0xFF2CA04A, 0xFF1C7A38);
    private static readonly Dictionary<char, uint> BlueKnit = Knit(0xFF0E2260, 0xFF90BCFF, 0xFF3A78F0, 0xFF1E48B0);

    private static readonly string[] Beanie = Shade(
    [
        "...ooo...",
        "..oPPpo..",
        ".olllllo.",
        "olllllllo",
        "oCCCCCCCo",
        "oCCCCCCCo",
        "ooooooooo",
    ], "lC", "HK", "dc");

    private static readonly string[] SmallBeanie = Shade(
    [
        "..ooo..",
        ".oPPpo.",
        ".olllo.",
        "olllllo",
        "oCCCCCo",
        "ooooooo",
    ], "lC", "HK", "dc");

    // 목도리: o 테두리, h·s·m 바탕, T·S·U 줄무늬
    private static Dictionary<char, uint> Scarf(uint o, uint h, uint s, uint m, uint th, uint st, uint su)
        => new() { ['o'] = o, ['h'] = h, ['s'] = s, ['m'] = m, ['T'] = th, ['S'] = st, ['U'] = su };

    private static readonly Dictionary<char, uint> RedScarf = Scarf(0xFF5A0C14, 0xFFF26A6E, 0xFFD8283A, 0xFFA8182A, 0xFFFFFFFF, 0xFFF4F0EC, 0xFFC8C4D4);
    private static readonly Dictionary<char, uint> BlueScarf = Scarf(0xFF0E2260, 0xFF90BCFF, 0xFF3A78F0, 0xFF1E48B0, 0xFFFFFFFF, 0xFFF4F0EC, 0xFFC8C4D4);
    private static readonly Dictionary<char, uint> GreenScarf = Scarf(0xFF0C3A1C, 0xFF70D07C, 0xFF2CA04A, 0xFF1C7A38, 0xFFFF8A8A, 0xFFE0303E, 0xFFA8182A);

    /// <summary>목에 두른 목도리(가로 <paramref name="w"/>, 오른쪽에 늘어진 꼬리).</summary>
    private static string[] ScarfRows(int w)
    {
        int tail = w - 5, length = w >= 11 ? 3 : 2;
        var rows = Rows(w, length + 6, (x, y) =>
        {
            bool inTail = x > tail && x < w - 1;
            if (y == 0) return x == 0 || x == w - 1 ? '.' : 'o';
            if (y <= 2) return x == 0 || x == w - 1 ? 'o' : (x / 2) % 2 == 1 ? 'S' : 's';
            if (y == 3) return x == 0 ? '.' : inTail ? 's' : 'o';
            if (x < tail) return '.';
            if (y <= 3 + length) return x == tail || x == w - 1 ? 'o' : y == 3 + length ? 'S' : 's';
            if (y == 4 + length) return 'o';
            return inTail && (x - tail) % 2 == 1 ? 'm' : '.';
        });
        return Shade(rows, "sS", "hT", "mU");
    }

    private static void Dot(PixelImage image, int x, int y, uint color)
    {
        if (image.Contains(x, y)) image.Set(x, y, new Color32(color));
    }

    /// <summary>
    /// 눈사람: 눈덩이 <paramref name="bottom"/>·<paramref name="middle"/>(0이면 두 덩이)·<paramref name="head"/>(홀수),
    /// 숯 눈·단추·입, 당근 코(오른쪽), 나뭇가지 팔, 모자·목도리. <paramref name="lean"/>은 윗덩이가 옆으로 비켜난 정도.
    /// </summary>
    private static PixelImage Snowman(int bottom, int middle, int head, Hat hat, Dictionary<char, uint>? hatColors,
        Dictionary<char, uint>? scarf, bool arms = true, int lean = 0)
    {
        const int W = 56, H = 64, cx = 28;
        int by = H - bottom, bx = cx - bottom / 2;
        int my = by, mx = bx, ms = bottom;
        if (middle > 0) { my = by - middle + 2; mx = cx - middle / 2 + lean; ms = middle; }
        int hy = my - head + 2, hx = cx - head / 2 + lean * (middle > 0 ? 2 : 1);
        var parts = new List<(PixelImage, int, int)>();
        if (arms)
        {
            var arm = Art(StickArm, Twig);
            int ay = my + ms / 3 - arm.Height + 1;
            parts.Add((arm, mx + 2 - arm.Width, ay));
            parts.Add((FlipX(arm), mx + ms - 2, ay));
        }
        parts.Add((Snowball(bottom, bottom), bx, by));
        if (middle > 0) parts.Add((Snowball(middle, middle), mx, my));
        parts.Add((Snowball(head, head), hx, hy));
        var body = Compose(W, H, parts);

        int hcx = hx + head / 2, eye = hy + (head >= 9 ? 4 : 3), off = head >= 9 ? 2 : 1;
        Dot(body, hcx - off, eye, Coal);
        Dot(body, hcx + off, eye, Coal);
        int nose = head >= 9 ? 3 : 2;
        for (int i = 0; i < nose; i++) Dot(body, hcx + i, eye + 1, i == nose - 1 ? CarrotTip : Carrot);
        if (head >= 9 && scarf == null)
        {
            Dot(body, hcx - 2, eye + 2, Coal);
            Dot(body, hcx - 1, eye + 3, Coal);
            Dot(body, hcx + 1, eye + 3, Coal);
            Dot(body, hcx + 2, eye + 2, Coal);
        }
        int bcx = mx + ms / 2, step = ms >= 11 ? 3 : 2;
        for (int y = my + (middle > 0 ? 4 : 3); y <= my + ms - 3; y += step) Dot(body, bcx, y, Coal);

        var layers = new List<(PixelImage, int, int)> { (body, 0, 0) };
        if (scarf != null) layers.Add((Art(ScarfRows(head + 2), scarf), hcx - (head + 2) / 2, hy + head - 2));
        if (hat != Hat.None && hatColors != null)
        {
            var cap = Art(hat == Hat.Top ? (head >= 9 ? TopHat : SmallTopHat) : (head >= 9 ? Beanie : SmallBeanie), hatColors);
            layers.Add((cap, hcx - cap.Width / 2, eye - 1 - cap.Height));
        }
        return Compose(W, H, layers);
    }

    /// <summary>눈사람: 네 크기(세 덩이 큰·중간, 두 덩이 작은·아주 작은), 실크해트·털모자, 목도리 색, 기운 것, 양쪽 방향, 눈 더미 둘.</summary>
    public static StampSet Snowmen() => Set("Snowmen",
    [
        .. Mirrored(
        [
            Snowman(15, 11, 9, Hat.Top, RedBandHat, RedScarf),
            Snowman(15, 11, 9, Hat.Beanie, GreenKnit, null, lean: 1),
            Snowman(13, 9, 7, Hat.Top, GreenBandHat, BlueScarf),
            Snowman(13, 9, 7, Hat.Beanie, RedKnit, GreenScarf, lean: -1),
            Snowman(13, 0, 9, Hat.Top, RedBandHat, null),
            Snowman(11, 0, 7, Hat.Beanie, BlueKnit, RedScarf, arms: false),
            Snowman(11, 0, 7, Hat.None, null, GreenScarf, arms: false),
        ]),
        Snowball(15, 5, mound: true),
        Snowball(9, 4, mound: true),
    ]);

    // ───────────── 호랑가시·화환 ─────────────

    // 잎: o 테두리, H·l 밝은 쪽, d 어두운 쪽, v 잎맥
    private static readonly Dictionary<char, uint> HollyLeaf = new() { ['o'] = 0xFF0C3418, ['H'] = 0xFF58B866, ['l'] = 0xFF2E8A44, ['d'] = 0xFF1E6A34, ['v'] = 0xFF9AD088 };
    private static readonly Dictionary<char, uint> MistletoeLeaf = new() { ['o'] = 0xFF2E4A16, ['H'] = 0xFFBCD870, ['l'] = 0xFF8EB040, ['d'] = 0xFF6A8A2A, ['v'] = 0xFFD0E890 };
    private static readonly Dictionary<char, uint> RedBract = new() { ['o'] = 0xFF5A0A12, ['H'] = 0xFFF86870, ['l'] = 0xFFD82838, ['d'] = 0xFFA8182A, ['v'] = 0xFF8A1020 };
    private static readonly Dictionary<char, uint> WhiteBract = new() { ['o'] = 0xFF6A6A7A, ['H'] = 0xFFFFFFFF, ['l'] = 0xFFF0EEE8, ['d'] = 0xFFC8C8D4, ['v'] = 0xFFD8D4CC };

    // 열매: o 테두리, H 반짝임, r 바탕, s 그늘
    private static readonly Dictionary<char, uint> RedBerry = new() { ['o'] = 0xFF5A0A12, ['H'] = 0xFFFF9A9A, ['r'] = 0xFFE0303E, ['s'] = 0xFFA8182A };
    private static readonly Dictionary<char, uint> PearlBerry = new() { ['o'] = 0xFF7A8070, ['H'] = 0xFFFFFFFF, ['r'] = 0xFFEEF0E2, ['s'] = 0xFFC8CCB8 };

    private static readonly string[] Berries =
    [
        ".oo.oo.",
        "oHroHro",
        "orsorso",
        ".ooHro.",
        "..orso.",
        "...oo..",
    ];

    // 화환 리본(꼬리 달린 큰 나비 리본)
    private static readonly string[] WreathBow = Symmetric(
    [
        ".ooo...",
        "oRrroo.",
        "oRrrroo",
        "oRrrsoK",
        "orsssok",
        ".oooooo",
        "....ors",
        "...orso",
        "..orso.",
        ".orso..",
        ".ooo...",
    ]);

    private static readonly string[] SmallWreathBow = Symmetric(
    [
        ".oo..",
        "oRroo",
        "orrsK",
        ".oooo",
        "..ors",
        ".oso.",
        ".oo..",
    ]);

    private static readonly Dictionary<char, uint> RedBowColors = new()
        { ['o'] = 0xFF5A0C14, ['R'] = 0xFFFF8A8A, ['r'] = 0xFFE0303E, ['s'] = 0xFFA8182A, ['K'] = 0xFFFF8A8A, ['k'] = 0xFFE0303E };
    private static readonly Dictionary<char, uint> GoldBowColors = new()
        { ['o'] = 0xFF6A4006, ['R'] = 0xFFFFF2B0, ['r'] = 0xFFF2C440, ['s'] = 0xFFC08A18, ['K'] = 0xFFFFF2B0, ['k'] = 0xFFF2C440 };

    // 솔잎: o 테두리, D·d·l·H·h 어두운 것부터 밝은 것
    private static readonly Dictionary<char, uint> Pine = new()
        { ['o'] = 0xFF0A2E16, ['D'] = 0xFF14482A, ['d'] = 0xFF1E6634, ['l'] = 0xFF2C8442, ['H'] = 0xFF4EA85A, ['h'] = 0xFF86CC78 };

    private static char PineTone(double lum) => lum > 0.85 ? 'h' : lum > 0.58 ? 'H' : lum > 0.3 ? 'l' : lum > 0.0 ? 'd' : 'D';

    /// <summary>
    /// 잎 하나: 밑동이 그림 가운데, <paramref name="deg"/> 방향(0 = 오른쪽, 90 = 아래)으로 길이 <paramref name="len"/>.
    /// <paramref name="spiky"/>면 호랑가시처럼 가장자리가 뾰족뾰족. 잎맥 한쪽은 밝게, 다른 쪽은 어둡게.
    /// </summary>
    private static PixelImage Leaf(double len, double width, double deg, bool spiky, Dictionary<char, uint> colors)
    {
        int s = 2 * (int)Math.Ceiling(len) + 5;
        double b = s / 2.0, a = deg * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
        (double U, double V) Local(int x, int y)
        {
            double dx = x + 0.5 - b, dy = y + 0.5 - b;
            return (dx * ca + dy * sa, -dx * sa + dy * ca);
        }
        double Half(double u)
        {
            if (u < 0) return 0.6;
            double hw = width * Math.Pow(Math.Sin(Math.PI * Math.Min(u, len) / len), 0.7) + 0.5;
            if (!spiky) return hw;
            double f = u * 3.5 / len % 1.0;
            return hw * (0.5 + 0.5 * Math.Abs(2 * f - 1));
        }
        bool In(int x, int y)
        {
            var (u, v) = Local(x, y);
            return u >= -0.5 && u <= len && Math.Abs(v) <= Half(u);
        }
        bool Inner(int x, int y) => In(x, y) && !Edge(In, x, y);
        return Art(Rows(s, s, (x, y) =>
        {
            if (!In(x, y)) return '.';
            if (Edge(In, x, y)) return 'o';
            var (u, v) = Local(x, y);
            if (Math.Abs(v) < 0.5 && u > 0.5 && u < len - 1.5) return 'v';
            double lit = v >= 0 ? sa - ca : ca - sa;
            if (lit >= 0) return Edge(Inner, x, y) ? 'H' : 'l';
            return 'd';
        }), colors);
    }

    /// <summary>밑동이 (<paramref name="x"/>, <paramref name="y"/>)에 오게 놓을 잎 자리.</summary>
    private static (PixelImage, int, int) At(PixelImage leaf, int x, int y) => (leaf, x - leaf.Width / 2, y - leaf.Height / 2);

    /// <summary>둥근 솔잎 고리: 바깥·안쪽 가장자리가 들쭉날쭉, 도넛처럼 왼쪽 위 바깥과 오른쪽 아래 안쪽이 밝다.</summary>
    private static PixelImage PineRing(int size, double thick, uint seed)
    {
        double c = (size - 1) / 2.0, outer = size / 2.0, inner = outer - thick, mid = (outer + inner) / 2 - 0.3;
        int spikes = (size * 3 / 2) & ~1;
        bool In(int x, int y)
        {
            double dx = x - c, dy = y - c, d = Math.Sqrt(dx * dx + dy * dy);
            bool tip = (int)Math.Floor((Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI) * spikes) % 2 == 0;
            return d <= outer - (tip ? 0.0 : 1.0) && d >= inner + (tip ? 0.0 : 0.9);
        }
        return Art(Rows(size, size, (x, y) =>
        {
            if (!In(x, y)) return '.';
            if (Edge(In, x, y)) return 'o';
            double dx = x - c, dy = y - c, d = Math.Sqrt(dx * dx + dy * dy);
            double t = Math.Clamp((d - mid) / (thick / 2), -1, 1);
            double lum = -0.5 * dx / d * t - 0.5 * dy / d * t + 0.7 * Math.Sqrt(1 - t * t);
            var rng = new Lcg(seed + (uint)(x * 977 + y * 131));
            return PineTone(lum + (rng.Next(5) - 2) * 0.13);
        }), Pine);
    }

    /// <summary>화환: 솔잎 고리, <paramref name="berryAngles"/> 자리마다 열매 셋, 아래에 리본.</summary>
    private static PixelImage Wreath(int size, double thick, Dictionary<char, uint>? bow, int[] berryAngles, uint seed)
    {
        const int m = 3;
        double c = (size - 1) / 2.0 + m, mid = size / 2.0 - thick / 2 - 0.3;
        var parts = new List<(PixelImage, int, int)> { (PineRing(size, thick, seed), m, m) };
        var berries = Art(Berries, RedBerry);
        foreach (var deg in berryAngles)
        {
            double a = deg * Math.PI / 180;
            parts.Add((berries, (int)Math.Round(c + mid * Math.Cos(a)) - 3, (int)Math.Round(c + mid * Math.Sin(a)) - 3));
        }
        if (bow != null)
        {
            var knot = Art(size >= 21 ? WreathBow : SmallWreathBow, bow);
            parts.Add((knot, (int)Math.Round(c) - knot.Width / 2, (int)Math.Round(c + mid) - (size >= 21 ? 3 : 2)));
        }
        return Compose(size + 2 * m, size + 2 * m + 8, parts);
    }

    /// <summary>호랑가시 가지: <paramref name="angles"/> 방향으로 뾰족한 잎, 밑동에 빨간 열매.</summary>
    private static PixelImage Holly(double len, params double[] angles)
    {
        var parts = angles.Select(a => At(Leaf(len, len * 0.36, a, true, HollyLeaf), 20, 20)).ToList();
        parts.Add((Art(Berries, RedBerry), 17, 16));
        return Compose(40, 40, parts);
    }

    /// <summary>겨우살이: 연두색 잎 두 쌍과 진주색 열매, 위에 작은 빨간 리본.</summary>
    private static PixelImage Mistletoe()
    {
        string[] bow = Symmetric([".oo.", "oRro", "orsK", ".ooo"]);
        return Compose(40, 40,
        [
            At(Leaf(10, 2.0, 150, false, MistletoeLeaf), 19, 15),
            At(Leaf(10, 2.0, 30, false, MistletoeLeaf), 21, 15),
            At(Leaf(10, 2.2, 112, false, MistletoeLeaf), 19, 16),
            At(Leaf(10, 2.2, 68, false, MistletoeLeaf), 21, 16),
            (Art(Berries, PearlBerry), 17, 12),
            (Art(bow, RedBowColors), 17, 9),
        ]);
    }

    /// <summary>포인세티아: 초록 잎 다섯 위에 큰 꽃턱잎 다섯과 작은 꽃턱잎 다섯, 가운데 노란 꽃.</summary>
    private static PixelImage Poinsettia(double size, Dictionary<char, uint> bracts, double rot)
    {
        rot -= 90;
        var parts = new List<(PixelImage, int, int)>();
        for (int i = 0; i < 5; i++) parts.Add(At(Leaf(size * 1.15, size * 0.3, rot + 36 + i * 72, false, HollyLeaf), 24, 24));
        for (int i = 0; i < 5; i++) parts.Add(At(Leaf(size, size * 0.26, rot + i * 72, false, bracts), 24, 24));
        for (int i = 0; i < 5; i++) parts.Add(At(Leaf(size * 0.6, size * 0.2, rot + 36 + i * 72, false, bracts), 24, 24));
        parts.Add((Art([".y.", "ygy", ".y."], new Dictionary<char, uint> { ['y'] = 0xFFFFD43A, ['g'] = 0xFF8EB040 }), 23, 23));
        return Compose(48, 48, parts);
    }

    /// <summary>솔잎 가랜드 토막: 살짝 처진 솔잎 줄, 위아래 끝이 들쭉날쭉.</summary>
    private static PixelImage Garland(int len, double sag, uint seed)
    {
        int h = (int)Math.Ceiling(sag) + 9;
        double Cy(int x) => 4 + sag * Math.Sin(Math.PI * x / (len - 1));
        bool In(int x, int y)
        {
            if (x < 0 || x >= len || y < 0 || y >= h) return false;
            var rng = new Lcg(seed + (uint)x * 31);
            double up = 2.3 + rng.Next(3) * 0.6, down = 2.0 + rng.Next(3) * 0.5, end = Math.Min(x, len - 1 - x);
            if (end < 2) { up -= (2 - end) * 0.9; down -= (2 - end) * 0.9; }
            double dy = y - Cy(x);
            return dy >= -up && dy <= down;
        }
        return Art(Rows(len, h, (x, y) =>
        {
            if (!In(x, y)) return '.';
            if (Edge(In, x, y)) return 'o';
            var rng = new Lcg(seed + (uint)(x * 977 + y * 131));
            return PineTone(0.45 - 0.22 * (y - Cy(x)) + (rng.Next(5) - 2) * 0.14);
        }), Pine);
    }

    /// <summary>호랑가시·화환: 큰·작은 화환(빨강·금색 리본, 열매), 호랑가시 가지(양쪽 방향), 겨우살이, 포인세티아, 솔잎 가랜드 토막.</summary>
    public static StampSet Wreaths() => Set("Holly & Wreaths",
    [
        Wreath(25, 8, RedBowColors, [200, 330], 1),
        Wreath(25, 8, GoldBowColors, [150, 250, 20], 2),
        Wreath(19, 6, RedBowColors, [215, 325], 3),
        Wreath(15, 5, null, [240, 20], 4),
        .. Mirrored(
        [
            Holly(12, 205, 325),
            Holly(10, 215),
            Holly(11, 190, 265, 350),
            Mistletoe(),
        ]),
        Poinsettia(10, RedBract, 0),
        Poinsettia(7, RedBract, 20),
        Poinsettia(9, WhiteBract, 10),
        Garland(22, 2, 5),
        Compose(28, 14, [(Garland(28, 3, 9), 0, 0), (Art(Berries, RedBerry), 11, 6)]),
        Garland(14, 1, 13),
    ]);

    // ───────────── 양말·산타 모자 ─────────────

    // 천: o 테두리, H·l·d 바탕
    private static Dictionary<char, uint> Cloth(uint o, uint h, uint l, uint d) => new() { ['o'] = o, ['H'] = h, ['l'] = l, ['d'] = d };

    private static readonly Dictionary<char, uint> RedCloth = Cloth(0xFF5A0C14, 0xFFF26A6E, 0xFFD8283A, 0xFFA8182A);
    private static readonly Dictionary<char, uint> GreenCloth = Cloth(0xFF0C3A1C, 0xFF70D07C, 0xFF2CA04A, 0xFF1C7A38);
    private static readonly Dictionary<char, uint> BlueCloth = Cloth(0xFF0E2260, 0xFF90BCFF, 0xFF3A78F0, 0xFF1E48B0);

    // 털 테두리·흰 줄무늬·방울·고리: K·C·c 흰 털, T·S·U 흰 줄, W·w 방울, n 고리 끈
    private static readonly Dictionary<char, uint> Fur = new()
    {
        ['K'] = 0xFFFFFFFF, ['C'] = 0xFFF4F0EC, ['c'] = 0xFFC8C4D4, ['T'] = 0xFFFFFFFF, ['S'] = 0xFFF4F0EC, ['U'] = 0xFFC8C4D4,
        ['W'] = 0xFFFFFFFF, ['w'] = 0xFFC8C4D4, ['n'] = 0xFFD8A838,
    };
    private static readonly Dictionary<char, uint> GreenCuff = new() { ['K'] = 0xFF70D07C, ['C'] = 0xFF2CA04A, ['c'] = 0xFF1C7A38 };

    private static readonly string[] StockingRows =
    [
        "............nn.",
        "...........n..n",
        "....ooooooooooo",
        "....oCCCCCCCCCo",
        "....oCCCCCCCCCo",
        "....oCCCCCCCCCo",
        "....oollllllloo",
        ".....olllllllo.",
        ".....olllllllo.",
        ".....olllllllo.",
        ".....olllllllo.",
        "....ollllllllo.",
        "..ollllllllllo.",
        ".oCClllllllCCo.",
        "oCCCllllllllCo.",
        "oCCCllllllllo..",
        ".oCCllllllo....",
        "..ooooooooo....",
    ];

    /// <summary>양말 글자 그림(<paramref name="striped"/>면 몸통에 흰 줄무늬).</summary>
    private static string[] StockingArt(bool striped)
    {
        var rows = striped
            ? StockingRows.Select((row, y) => (y - 6) / 2 % 2 == 1 && y < 13 ? row.Replace('l', 'S') : row).ToArray()
            : StockingRows;
        return Shade(rows, "lCS", "HKT", "dcU");
    }

    private static readonly Dictionary<char, uint> Teddy = new()
        { ['o'] = 0xFF3A2010, ['B'] = 0xFFC89058, ['b'] = 0xFF9A6434, ['d'] = 0xFF6E4422, ['m'] = 0xFFE0C090, ['k'] = 0xFF1C0E06 };

    private static readonly string[] TeddyHead =
    [
        ".oo...oo.",
        "oBbooobbo",
        ".oBbbbbo.",
        "oBbkbkbdo",
        "obbmkmbdo",
        "obbmmmbdo",
        ".odbbbdo.",
        "..ooooo..",
    ];

    /// <summary>양말 위로 장난감이 빼꼼 나온 모양(장난감 아랫부분은 털 테두리 뒤에 숨음).</summary>
    private static PixelImage WithToy(PixelImage stocking, PixelImage toy)
    {
        int rise = toy.Height * 2 / 3 - 1;
        return Compose(stocking.Width, stocking.Height + rise, [(toy, 9 - toy.Width / 2, 0), (stocking, 0, rise)]);
    }

    private static readonly string[] DroopingHat = Shade(
    [
        ".....ooooo........",
        "....olllllooo.....",
        "...olllllllllloo..",
        "...olllllllllllo..",
        "..olllllllooolllo.",
        "..olllllllo.olllo.",
        ".ollllllllo.olllo.",
        ".ollllllllo..olo..",
        ".ollllllllo.ooWoo.",
        "oCCCCCCCCCCooWWwo.",
        "oCCCCCCCCCCooWwwo.",
        "oooooooooooo.ooo..",
    ], "lC", "HK", "dc");

    private static readonly string[] PointedHat = Shade(
    [
        ".........ooo.",
        "........oWWwo",
        "........oWwwo",
        ".......olooo.",
        "......ollo...",
        ".....olllo...",
        ".....ollllo..",
        "....olllllo..",
        "...ollllllo..",
        "oooollllllooo",
        "oCCCCCCCCCCCo",
        "oCCCCCCCCCCCo",
        "ooooooooooooo",
    ], "lC", "HK", "dc");

    private static readonly string[] Mitten = Shade(
    [
        "...oooo.",
        "..ollllo",
        "..olPllo",
        ".oollllo",
        "ololllPo",
        "ollllllo",
        ".olllllo",
        ".oCCCCCo",
        ".oCCCCCo",
        ".ooooooo",
    ], "lC", "HK", "dc");

    /// <summary>끈으로 이은 벙어리장갑 한 켤레(엄지가 안쪽).</summary>
    private static PixelImage MittenPair(Dictionary<char, uint> cloth, uint pattern)
    {
        var one = Art(Mitten, new Dictionary<char, uint> { ['P'] = pattern }, cloth, Fur);
        var pair = Compose(18, 15, [(FlipX(one), 0, 0), (one, 9, 1)]);
        (int X, int Y)[] cord = [(3, 10), (4, 11), (5, 12), (6, 13), (7, 13), (8, 13), (9, 13), (10, 13), (11, 12), (12, 12), (13, 11)];
        foreach (var (x, y) in cord) Dot(pair, x, y, 0xFFE8E0D0);
        return pair;
    }

    /// <summary>뜨개 목도리 한 토막: 세로 줄무늬, 뜨개 결, 양 끝 술.</summary>
    private static PixelImage KnitScarf(int len, double wave, uint o, uint[] main, uint[] stripe)
    {
        const int band = 5, end = 3;
        int h = band + (int)Math.Ceiling(2 * wave);
        int Top(int x) => (int)Math.Round(wave + wave * Math.Sin(2 * Math.PI * (x - end) / (len - 2 * end - 1)));
        bool In(int x, int y) => x >= end && x < len - end && y >= Top(x) && y < Top(x) + band;
        return Paint(len, h, (x, y) =>
        {
            if (x < end || x >= len - end)
                return (y - Top(x < end ? end : len - end - 1)) is 1 or 3 ? new Color32(main[2]) : Color32.Transparent;
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return new Color32(o);
            var tones = ((x - end - 1) / 3) % 2 == 1 ? stripe : main;
            int row = y - Top(x);
            return new Color32(tones[row == 1 ? 0 : row == band - 2 ? 2 : 1]);
        });
    }

    /// <summary>양말·산타 모자: 빨강·초록·줄무늬 양말(장난감이 빼꼼 나온 것도), 처진·뾰족 산타 모자, 벙어리장갑 한 켤레, 뜨개 목도리.</summary>
    public static StampSet Stockings()
    {
        var red = Art(StockingArt(false), RedCloth, Fur);
        var green = Art(StockingArt(false), GreenCloth, Fur);
        var striped = Art(StockingArt(true), GreenCuff, RedCloth, Fur);
        return Set("Stockings & Santa Hats",
        [
            .. Mirrored(
            [
                red,
                green,
                striped,
                WithToy(red, HookCane(2, 1.5, 14, RedCane)),
                WithToy(green, Art(TeddyHead, Teddy)),
                WithToy(striped, Box(7, 6, BlueWrap, GoldRibbon)),
                Art(DroopingHat, RedCloth, Fur),
                Art(PointedHat, RedCloth, Fur),
            ]),
            MittenPair(RedCloth, 0xFFF4F0EC),
            MittenPair(BlueCloth, 0xFFF4F0EC),
            MittenPair(GreenCloth, 0xFFE0303E),
            KnitScarf(28, 1, 0xFF5A0C14, [0xFFF26A6E, 0xFFD8283A, 0xFFA8182A], [0xFFFFFFFF, 0xFFF4F0EC, 0xFFC8C4D4]),
            KnitScarf(22, 1, 0xFF0C3A1C, [0xFF70D07C, 0xFF2CA04A, 0xFF1C7A38], [0xFFFF8A8A, 0xFFE0303E, 0xFFA8182A]),
        ]);
    }

    // ───────────── 도우미 ─────────────

    /// <summary>
    /// 자동 명암: <paramref name="fills"/>의 글자 중 위나 왼쪽이 바깥(<paramref name="outside"/>)인 칸은 <paramref name="his"/>의 같은 자리 글자로,
    /// 아니면서 오른쪽·아래·오른쪽 아래가 바깥인 칸은 <paramref name="los"/>의 글자로 바꾼다.
    /// </summary>
    private static string[] Shade(string[] rows, string fills = "l", string his = "H", string los = "d", string outside = ".o")
    {
        char At(int x, int y) => y < 0 || y >= rows.Length || x < 0 || x >= rows[y].Length ? '.' : rows[y][x];
        bool Out(int x, int y) => outside.Contains(At(x, y));
        var result = new string[rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            var line = rows[y].ToCharArray();
            for (int x = 0; x < line.Length; x++)
            {
                int i = fills.IndexOf(line[x]);
                if (i < 0) continue;
                if (Out(x - 1, y) || Out(x, y - 1)) line[x] = his[i];
                else if (Out(x + 1, y) || Out(x, y + 1) || Out(x + 1, y + 1)) line[x] = los[i];
            }
            result[y] = new string(line);
        }
        return result;
    }

    /// <summary>칸마다 글자를 고르는 함수로 만든 글자 그림.</summary>
    private static string[] Rows(int w, int h, Func<int, int, char> at)
        => [.. Enumerable.Range(0, h).Select(y => new string(Enumerable.Range(0, w).Select(x => at(x, y)).ToArray()))];
}
