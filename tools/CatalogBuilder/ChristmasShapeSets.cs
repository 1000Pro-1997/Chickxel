using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 크리스마스 트리 장식 모양 펜 세트 세 가지(로컬 세트 파일 <see cref="LocalShapeSets"/>의 그림): 전구, 종·리본, 장식.
/// 손으로 그린 글자 그림(글자마다 한 픽셀, 팔레트 0xAARRGGBB)에 색 변형·빛 번짐·겹쳐 놓기를 더한다. 초록 트리 위에서 보이게 테두리는 어둡게, 빛은 왼쪽 위에서.
/// 모양은 모두 투명한 둘레를 잘라 담는다. 같은 코드면 늘 같은 모양이다.
/// </summary>
internal static class ChristmasShapeSets
{
    // ───────────── 전구 ─────────────

    private static readonly Dictionary<char, uint> Socket = new() { ['c'] = 0xFF3E4650, ['C'] = 0xFF8C99A3 };

    // o 테두리, m 그늘, l 바탕, H 밝은 곳
    private static readonly Dictionary<char, uint>[] BulbColors =
    [
        new() { ['o'] = 0xFF6E0F14, ['m'] = 0xFFB01E26, ['l'] = 0xFFE8343A, ['H'] = 0xFFFF9A90 }, // 빨강
        new() { ['o'] = 0xFF7A4A08, ['m'] = 0xFFD08A10, ['l'] = 0xFFFFC72E, ['H'] = 0xFFFFF0A0 }, // 금색
        new() { ['o'] = 0xFF0B4A2A, ['m'] = 0xFF1E9A4A, ['l'] = 0xFF3EDB6A, ['H'] = 0xFFB8FFC8 }, // 초록
        new() { ['o'] = 0xFF102A70, ['m'] = 0xFF2456C8, ['l'] = 0xFF4A8CFF, ['H'] = 0xFFB8D8FF }, // 파랑
        new() { ['o'] = 0xFF7A1A4A, ['m'] = 0xFFD0407E, ['l'] = 0xFFFF78B0, ['H'] = 0xFFFFD0E4 }, // 분홍
    ];

    private const int Red = 0, Gold = 1, Green = 2, Blue = 3, Pink = 4;

    private static readonly string[] Bulb =
    [
        "..cCc..",
        "..cCc..",
        ".oHlmo.",
        "oHHllmo",
        "oHlllmo",
        "oHllmmo",
        "olllmmo",
        ".ollmo.",
        ".olmmo.",
        "..omo..",
        "...o...",
    ];

    private static readonly string[] MiniBulb =
    [
        ".cCc.",
        ".cCc.",
        "oHlmo",
        "oHlmo",
        "ollmo",
        ".olo.",
        "..o..",
    ];

    private static readonly Dictionary<char, uint> WireColor = new() { ['w'] = 0xFF1C2A20 };

    private static readonly string[] Wire =
    [
        "w..................w",
        ".w................w.",
        "..ww............ww..",
        "....www......www....",
        ".......wwwwww.......",
    ];

    /// <summary>전구: 다섯 색 전구, 그 빛나는 전구(한 단계 밝은 색 + 둘레의 빛 번짐), 전구 셋을 단 짧은 전선 두 가지.</summary>
    public static StampSet Lights()
    {
        var shapes = new List<PixelImage>();
        foreach (var color in BulbColors) shapes.Add(Art(Bulb, Socket, color));
        foreach (var color in BulbColors) shapes.Add(Glow(Art(Bulb, Socket, Lit(color)), color['l'], fromRow: 2));
        shapes.Add(LightString(Red, Gold, Blue));
        shapes.Add(LightString(Green, Pink, Gold));
        return Set("Christmas Lights", shapes);
    }

    // 켠 전구: 바탕은 밝은 곳 색, 밝은 곳은 흰색, 테두리는 그늘 색
    private static Dictionary<char, uint> Lit(Dictionary<char, uint> color)
        => new() { ['o'] = color['m'], ['m'] = color['l'], ['l'] = color['H'], ['H'] = 0xFFFFFFFF };

    // 늘어진 전선에 작은 전구 셋(전선이 처지는 자리마다 꼭지가 전선 바로 아래)
    private static PixelImage LightString(int left, int middle, int right)
    {
        (int X, int Y)[] at = [(1, 3), (7, 5), (14, 3)];
        int[] colors = [left, middle, right];
        var parts = new List<(PixelImage, int, int)> { (Art(Wire, WireColor), 0, 0) };
        for (int i = 0; i < at.Length; i++) parts.Add((Art(MiniBulb, Socket, BulbColors[colors[i]]), at[i].X, at[i].Y));
        return Compose(20, 12, parts);
    }

    // ───────────── 종·리본 ─────────────

    // o 테두리, d 그늘, y 바탕, H 밝은 곳, k 추
    private static readonly Dictionary<char, uint> GoldBell = new() { ['o'] = 0xFF5E3A06, ['d'] = 0xFFB0761A, ['y'] = 0xFFF0BA38, ['H'] = 0xFFFFF2B0, ['k'] = 0xFF4A2E06 };
    private static readonly Dictionary<char, uint> SilverBell = new() { ['o'] = 0xFF323A48, ['d'] = 0xFF8090A4, ['y'] = 0xFFC4CEDC, ['H'] = 0xFFFFFFFF, ['k'] = 0xFF3A4252 };

    private static readonly string[] Bell =
    [
        "....ooo....",
        "....o.o....",
        "...ooooo...",
        "..oHyyyyo..",
        "..oHyyydo..",
        "..oHyyydo..",
        "..oHyyydo..",
        ".oHyyyyydo.",
        "oHyyyyyyddo",
        "oyyyyyyyddo",
        ".ooooooooo.",
        "....oko....",
        ".....o.....",
    ];

    private static readonly string[] SmallBell =
    [
        "..ooo..",
        ".oHyyo.",
        ".oHyyo.",
        "oHyyydo",
        "oyyyddo",
        ".ooooo.",
        "..oko..",
    ];

    // o 테두리, d 그늘, r 바탕, R 밝은 곳, k·K 매듭
    private static readonly Dictionary<char, uint> RedRibbon = new()
        { ['o'] = 0xFF4E0810, ['d'] = 0xFF9A1424, ['r'] = 0xFFD42234, ['R'] = 0xFFFF6A6E, ['k'] = 0xFFD42234, ['K'] = 0xFFFF6A6E };
    private static readonly Dictionary<char, uint> GoldRibbon = new()
        { ['o'] = 0xFF5E3A06, ['d'] = 0xFFB0761A, ['r'] = 0xFFF0BA38, ['R'] = 0xFFFFF2B0, ['k'] = 0xFFF0BA38, ['K'] = 0xFFFFF2B0 };

    // 왼쪽 반(마지막 열이 가운데) — Symmetric으로 오른쪽을 채운다
    private static readonly string[] Bow = Symmetric(
    [
        ".ooo....",
        "oRrroo..",
        "oRrrrooo",
        "oRrrrokK",
        "oRrrdokK",
        "oddddokK",
        ".ooooooo",
        "....orro",
        "...ordo.",
        "..ordo..",
        ".ordo...",
        ".ooo....",
    ]);

    // 쌍종 위에 다는 꼬리 짧은 리본
    private static readonly string[] ShortBow = Symmetric(
    [
        ".ooo....",
        "oRrroo..",
        "oRrrrooo",
        "oRrrrokK",
        "oRrrdokK",
        "oddddokK",
        ".ooooooo",
        "....orro",
        "...ordo.",
        "...ooo..",
    ]);

    private static readonly string[] SmallBow = Symmetric(
    [
        ".oo..",
        "oRroo",
        "oRroK",
        "oRdok",
        "oddoo",
        ".ooro",
        "..odo",
        ".odo.",
        ".oo..",
    ]);

    /// <summary>종·리본: 금·은 종(큰 것, 작은 것), 리본 단 쌍종 두 가지, 리본 단 금종, 빨간·금색 리본.</summary>
    public static StampSet BellsAndBows()
    {
        PixelImage gold = Art(Bell, GoldBell), silver = Art(Bell, SilverBell), shortBow = Art(ShortBow, RedRibbon);
        PixelImage DoubleBell(PixelImage left, PixelImage right) => Compose(20, 18, [(right, 9, 5), (left, 0, 5), (shortBow, 2, 0)]);
        return Set("Christmas Bells & Bows",
        [
            gold,
            silver,
            Art(SmallBell, GoldBell),
            Art(SmallBell, SilverBell),
            DoubleBell(gold, gold),
            DoubleBell(silver, gold),
            Compose(11, 17, [(gold, 0, 4), (Art(SmallBow, RedRibbon), 1, 0)]),
            Art(Bow, RedRibbon),
            Art(Bow, GoldRibbon),
        ]);
    }

    // ───────────── 장식 ─────────────

    // c·C 금색 마개, h 은색 고리
    private static readonly Dictionary<char, uint> Cap = new() { ['c'] = 0xFF8A6A1E, ['C'] = 0xFFE8C860, ['h'] = 0xFFB8BCC4 };

    // o 테두리, m 그늘, l 바탕, H 밝은 곳
    private static readonly Dictionary<char, uint>[] BaubleColors =
    [
        new() { ['o'] = 0xFF5A0A12, ['m'] = 0xFFA81828, ['l'] = 0xFFE0303E, ['H'] = 0xFFFFB0B0 }, // 빨강
        new() { ['o'] = 0xFF0E2260, ['m'] = 0xFF1E48B0, ['l'] = 0xFF3A78F0, ['H'] = 0xFFC0DAFF }, // 파랑
        new() { ['o'] = 0xFF5E3A06, ['m'] = 0xFFC08A18, ['l'] = 0xFFF2C440, ['H'] = 0xFFFFF6C8 }, // 금색
        new() { ['o'] = 0xFF3A1258, ['m'] = 0xFF7A30A8, ['l'] = 0xFFB060E0, ['H'] = 0xFFF0D0FF }, // 보라
    ];

    // 흰 띠 두른 빨간 방울의 띠 색
    private static readonly Dictionary<char, uint> Band = new() { ['W'] = 0xFFF8F4F0, ['w'] = 0xFFC8C0C0 };

    private static readonly string[] Bauble =
    [
        "...hhh...",
        "...h.h...",
        "...cCc...",
        "..ooooo..",
        ".oHHllmo.",
        "oHHllllmo",
        "oHlllllmo",
        "olllllmmo",
        "ollllmmmo",
        ".ommmmmo.",
        "..ooooo..",
    ];

    private static readonly string[] BandedBauble =
    [
        "...hhh...",
        "...h.h...",
        "...cCc...",
        "..ooooo..",
        ".oHHllmo.",
        "oHHllllmo",
        "oWWWWWWwo",
        "olllllmmo",
        "ollllmmmo",
        ".ommmmmo.",
        "..ooooo..",
    ];

    private static readonly Dictionary<char, uint> StarGold = new() { ['o'] = 0xFF6A4006, ['d'] = 0xFFC88A14, ['y'] = 0xFFFFD43A, ['H'] = 0xFFFFFAD0 };

    private static readonly string[] TreeTopStar =
    [
        ".....o.....",
        "....oHo....",
        "....oHo....",
        "...oHyyo...",
        "ooooHyyoooo",
        "oHHHyyyyydo",
        ".oHyyyyydo.",
        "..oyyyyyo..",
        "..oyyoyyo..",
        ".oydo.odyo.",
        ".odo...odo.",
        ".oo.....oo.",
    ];

    private static readonly Dictionary<char, uint> CaneColors = new() { ['o'] = 0xFF5A0A12, ['R'] = 0xFFE0303E, ['W'] = 0xFFF8F4F0 };

    private static readonly string[] CandyCane =
    [
        "..oooo..",
        ".oWRRWo.",
        "oRWooRWo",
        "oWo..oRo",
        ".o...oWo",
        ".....oRo",
        ".....oWo",
        ".....oRo",
        ".....oWo",
        ".....oRo",
        "......o.",
    ];

    // o 테두리, B 바탕, d 그늘, D 깊은 그늘, Y 끈
    private static readonly Dictionary<char, uint> RedGift = new() { ['o'] = 0xFF4A0810, ['B'] = 0xFFD42234, ['d'] = 0xFF9A1424, ['D'] = 0xFF6A0C18, ['Y'] = 0xFFF2C440 };
    private static readonly Dictionary<char, uint> BlueGift = new() { ['o'] = 0xFF0E2260, ['B'] = 0xFF3A78F0, ['d'] = 0xFF1E48B0, ['D'] = 0xFF14307A, ['Y'] = 0xFFF8F4F0 };

    private static readonly string[] Gift =
    [
        "..oo.oo..",
        ".oYYoYYo.",
        "..ooYoo..",
        "ooooYoooo",
        "oBBBYBBdo",
        "oBBBYBBdo",
        "oYYYYYYYo",
        "oBBBYBddo",
        "oddddddDo",
        "ooooooooo",
    ];

    private static readonly Dictionary<char, uint> ConeColors = new() { ['s'] = 0xFF3A2410, ['o'] = 0xFF3A2410, ['B'] = 0xFFB07A44, ['b'] = 0xFF7A4A24, ['d'] = 0xFF5A3418 };

    private static readonly string[] PineCone =
    [
        "...s...",
        "..oso..",
        ".oBbBo.",
        "oBbBbdo",
        "obBbBdo",
        "oBbBbdo",
        ".obBdo.",
        ".oBdbo.",
        "..odo..",
        "...o...",
    ];

    private static readonly Dictionary<char, uint> FlakeColors = new() { ['w'] = 0xFFF4FAFF, ['b'] = 0xFF9ED0FF };

    private static readonly string[] Snowflake =
    [
        "...w...",
        ".w.w.w.",
        "..www..",
        "wwwbwww",
        "..www..",
        ".w.w.w.",
        "...w...",
    ];

    /// <summary>장식: 네 색 방울과 띠 두른 방울, 트리 꼭대기 별, 지팡이 사탕, 빨강·파랑 선물 상자, 솔방울, 눈송이.</summary>
    public static StampSet Ornaments() => Set("Christmas Ornaments",
    [
        .. BaubleColors.Select(color => Art(Bauble, Cap, color)),
        Art(BandedBauble, Cap, BaubleColors[0], Band),
        Art(TreeTopStar, StarGold),
        Art(CandyCane, CaneColors),
        Art(Gift, RedGift),
        Art(Gift, BlueGift),
        Art(PineCone, ConeColors),
        Art(Snowflake, FlakeColors),
    ]);
}
