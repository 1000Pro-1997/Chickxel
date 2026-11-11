using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 일상·축제 모양 펜 세트 여섯 가지: 과일, 사탕·과자, 풍선, 색종이 조각, 물감 자국, 통나무·그루터기.
/// 손으로 그린 글자 그림과 코드로 칠한 둥근 덩어리(테두리는 바탕보다 어둡게, 빛은 왼쪽 위에서)를 섞는다. 같은 코드면 늘 같은 모양이다.
/// </summary>
internal static class EverydayShapeSets
{
    // ───────────── 공통 ─────────────

    // o 테두리, d 그늘, b 바탕, l 밝은 곳, H 반짝임
    private static Dictionary<char, uint> Pal(uint o, uint d, uint b, uint l, uint h)
        => new() { ['o'] = o, ['d'] = d, ['b'] = b, ['l'] = l, ['H'] = h };

    private static readonly Dictionary<char, uint> Red = Pal(0xFF6E0A14, 0xFFB0141E, 0xFFE02A2A, 0xFFFF6A5E, 0xFFFFD4CC);
    private static readonly Dictionary<char, uint> Orange = Pal(0xFF7A2E04, 0xFFD0600A, 0xFFFF8A1E, 0xFFFFB866, 0xFFFFEAD0);
    private static readonly Dictionary<char, uint> Yellow = Pal(0xFF8A5E00, 0xFFE0A800, 0xFFFFD21E, 0xFFFFEA78, 0xFFFFFFE8);
    private static readonly Dictionary<char, uint> Green = Pal(0xFF0C4A1C, 0xFF1E8A34, 0xFF34C04C, 0xFF7EE68A, 0xFFE0FFE4);
    private static readonly Dictionary<char, uint> Teal = Pal(0xFF064A54, 0xFF0E9AA6, 0xFF2ED0D8, 0xFF84ECF0, 0xFFE6FFFF);
    private static readonly Dictionary<char, uint> Blue = Pal(0xFF0E2066, 0xFF1E4AB8, 0xFF2E74E8, 0xFF6AA8FF, 0xFFD8ECFF);
    private static readonly Dictionary<char, uint> Purple = Pal(0xFF34105A, 0xFF6424A0, 0xFF8E3ED0, 0xFFBC80F0, 0xFFF0E0FF);
    private static readonly Dictionary<char, uint> Pink = Pal(0xFF7A1048, 0xFFD03A86, 0xFFFF6AB0, 0xFFFFA8D2, 0xFFFFE6F2);
    private static readonly Dictionary<char, uint> White = Pal(0xFF7C8696, 0xFFC4CCD8, 0xFFE8EDF2, 0xFFFFFFFF, 0xFFFFFFFF);
    private static readonly Dictionary<char, uint> Black = Pal(0xFF060608, 0xFF15151B, 0xFF26262F, 0xFF41414D, 0xFFA0A0B0);
    private static readonly Dictionary<char, uint> Gold = Pal(0xFF6A4606, 0xFFC08A1A, 0xFFF0C030, 0xFFFFE070, 0xFFFFFFE6);
    private static readonly Dictionary<char, uint> Silver = Pal(0xFF3C4450, 0xFF8C98A8, 0xFFC0CAD6, 0xFFE6ECF2, 0xFFFFFFFF);

    /// <summary>둥근 덩어리 명암: 테두리 o, 왼쪽 위 가장자리 l, 오른쪽 아래 가장자리 d, 반짝이 자리 H, 나머지 b.</summary>
    private static PixelImage Shade(int w, int h, Func<int, int, bool> inside, Dictionary<char, uint> p, (double X, double Y, double R)? spot = null)
        => Paint(w, h, (x, y) => Tone(inside, x, y, spot) is var t && t == ' ' ? Color32.Transparent : new Color32(p[t]));

    private static char Tone(Func<int, int, bool> inside, int x, int y, (double X, double Y, double R)? spot)
    {
        if (!inside(x, y)) return ' ';
        if (!inside(x - 1, y) && !inside(x + 1, y) && !inside(x, y - 1) && !inside(x, y + 1)) return 'b';
        if (Edge(inside, x, y)) return 'o';
        if (spot is { } s && (x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) <= s.R * s.R) return 'H';
        int a = Run(inside, x, y, -1), b = Run(inside, x, y, 1);
        double t = (a - b) / (double)(a + b + 1);
        return b == 0 || t > 0.4 ? 'd' : a == 0 || t < -0.45 ? 'l' : 'b';
    }

    // (x, y)에서 대각선으로 몇 칸 더 안쪽인가
    private static int Run(Func<int, int, bool> inside, int x, int y, int dir)
    {
        int n = 0;
        while (n < 64 && inside(x + dir * (n + 1), y + dir * (n + 1))) n++;
        return n;
    }

    private static PixelImage Transpose(PixelImage s) => Paint(s.Height, s.Width, (x, y) => s.Get(y, x));

    private static uint Mix(uint a, uint b, double t)
    {
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t) << shift;
        return 0xFF000000 | Ch(16) | Ch(8) | Ch(0);
    }

    private static void Line(PixelImage img, int x0, int y0, int x1, int y1, Color32 c)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            if (img.Contains(x0, y0)) img.Set(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>조각들을 겹치지 않게(<paramref name="gap"/>칸 띄워) 판 위 아무 데나 놓는다. 자리를 못 찾은 조각은 뺀다.</summary>
    private static PixelImage Scatter(int w, int h, IReadOnlyList<PixelImage> parts, uint seed, int gap)
    {
        var rng = new Lcg(seed);
        var canvas = new PixelImage(w, h);
        foreach (var part in parts)
            for (int tries = 0; tries < 300; tries++)
            {
                int ox = rng.Next(Math.Max(1, w - part.Width + 1)), oy = rng.Next(Math.Max(1, h - part.Height + 1));
                if (!Fits(canvas, part, ox, oy, gap)) continue;
                for (int y = 0; y < part.Height; y++)
                    for (int x = 0; x < part.Width; x++)
                        if (part.Get(x, y).A > 0 && canvas.Contains(ox + x, oy + y)) canvas.Set(ox + x, oy + y, part.Get(x, y));
                break;
            }
        return canvas;
    }

    private static bool Fits(PixelImage canvas, PixelImage part, int ox, int oy, int gap)
    {
        for (int y = 0; y < part.Height; y++)
            for (int x = 0; x < part.Width; x++)
            {
                if (part.Get(x, y).A == 0) continue;
                if (!canvas.Contains(ox + x, oy + y)) return false;
                for (int dy = -gap; dy <= gap; dy++)
                    for (int dx = -gap; dx <= gap; dx++)
                        if (canvas.Contains(ox + x + dx, oy + y + dy) && canvas.Get(ox + x + dx, oy + y + dy).A > 0) return false;
            }
        return true;
    }

    // ───────────── 과일 ─────────────

    private static readonly Dictionary<char, uint> Leaf = new() { ['q'] = 0xFF1E5418, ['g'] = 0xFF3E9A2E, ['G'] = 0xFF86CC52 };
    private static readonly Dictionary<char, uint> Stem = new() { ['S'] = 0xFF5A3A1C };
    private static readonly Dictionary<char, uint> CherryStem = new() { ['S'] = 0xFF56702A };
    private static readonly Dictionary<char, uint> RedApple = Pal(0xFF5C0D12, 0xFFA3171F, 0xFFD92B2B, 0xFFF0605A, 0xFFFFD0C8);
    private static readonly Dictionary<char, uint> GreenApple = Pal(0xFF2E4A0E, 0xFF5E9A1C, 0xFF8CC63A, 0xFFBEE36A, 0xFFF2FFD0);
    private static readonly Dictionary<char, uint> YellowApple = Pal(0xFF6E5410, 0xFFD0A02A, 0xFFF2CE4E, 0xFFFFE890, 0xFFFFFCE6);
    private static readonly Dictionary<char, uint> OrangeSkin = Pal(0xFF7A3A05, 0xFFD06A0A, 0xFFF59A1E, 0xFFFFC05A, 0xFFFFF0C8);
    private static readonly Dictionary<char, uint> PearSkin = Pal(0xFF4E5410, 0xFF9AA52A, 0xFFC8D24A, 0xFFE4EC8A, 0xFFFAFFE0);
    private static readonly Dictionary<char, uint> BoscPear = Pal(0xFF5A2810, 0xFFA2521E, 0xFFC97E36, 0xFFE6A866, 0xFFFFE6C8);
    private static readonly Dictionary<char, uint> LemonSkin = Pal(0xFF7A6205, 0xFFD9B414, 0xFFFBE34A, 0xFFFFF39A, 0xFFFFFFF0);
    private static readonly Dictionary<char, uint> PlumSkin = Pal(0xFF2A0E33, 0xFF5A2470, 0xFF7E3A9A, 0xFFA866C0, 0xFFE0C8F0);
    private static readonly Dictionary<char, uint> CherryRed = Pal(0xFF4A0610, 0xFF8C0D1E, 0xFFC8162E, 0xFFF04A5A, 0xFFFFD0D8);

    private static readonly Dictionary<char, uint> BananaSkin = new()
        { ['o'] = 0xFF6E520A, ['d'] = 0xFFCFA01E, ['b'] = 0xFFF5D547, ['l'] = 0xFFFFEC8F, ['H'] = 0xFFFFFAD8, ['k'] = 0xFF4A3A1A };

    private static readonly Dictionary<char, uint> StrawberrySkin = new()
        { ['o'] = 0xFF5C0A14, ['d'] = 0xFFA8121F, ['b'] = 0xFFE0242E, ['l'] = 0xFFFF6460, ['H'] = 0xFFFFD0CC, ['y'] = 0xFFFFE27A };

    private static readonly string[] Apple =
    [
        ".....SqGgq.",
        ".....S.qq..",
        "..oooSooo..",
        ".oHllobbbo.",
        "oHllbbbbbdo",
        "oHlbbbbbbdo",
        "olbbbbbbbdo",
        "obbbbbbbddo",
        "obbbbbbbddo",
        ".obbbbbbdo.",
        "..oddoddo..",
        "...oo.oo...",
    ];

    private static readonly string[] SmallApple =
    [
        "....S....",
        "..ooSoo..",
        ".oHlobbo.",
        "oHlbbbbdo",
        "olbbbbbdo",
        "obbbbbbdo",
        "obbbbbddo",
        ".obbbddo.",
        "..oo.oo..",
    ];

    private static readonly string[] OrangeFruit =
    [
        "....qgq...",
        "..ooqqoo..",
        ".oHlbbbbo.",
        "oHlbbbbbdo",
        "oHbbbbbbdo",
        "olbbbbbbdo",
        "obbbbbbddo",
        "obbbbbbddo",
        ".obbbbddo.",
        "..oooooo..",
    ];

    private static readonly string[] Pear =
    [
        "....S.qq..",
        "....SqGgq.",
        "...oSoqq..",
        "..oHlbo...",
        "..oHlbo...",
        "..oHbbdo..",
        ".oHlbbbdo.",
        "oHlbbbbbdo",
        "oHbbbbbbdo",
        "olbbbbbbdo",
        "obbbbbbddo",
        ".obbbbddo.",
        "..oooooo..",
    ];

    private static readonly string[] Lemon =
    [
        "....oooooo....",
        "..ooHHlbbbdoo.",
        ".oHlbbbbbbbddo",
        "olbbbbbbbbbbdo",
        "obbbbbbbbbbddo",
        ".obbbbbbbbddo.",
        "..oodddddddoo.",
        "....oooooo....",
    ];

    private static readonly string[] Plum =
    [
        "....S....",
        "..ooSoo..",
        ".oHlbdbo.",
        "oHlbbdbdo",
        "olbbbdbdo",
        "obbbbdbdo",
        "obbbbbddo",
        ".obbbddo.",
        "..ooooo..",
    ];

    private static readonly string[] CherryStems =
    [
        ".......qq....",
        "......SqGgq..",
        ".....S.Sqq...",
        ".....S..S....",
        "....S...S....",
        "....S....S...",
        "...S.....S...",
        "...S......S..",
    ];

    private static readonly string[] Cherry = [".oooo.", "oHlbbo", "olbbdo", "obbbdo", "obbddo", ".oooo."];

    private static readonly string[] Banana =
    [
        "..............k",
        ".............kk",
        "............olo",
        "k..........olbo",
        "oooo.....oolbdo",
        ".oHlooooolbbbdo",
        "..oHllllbbbddo.",
        "...oobbbbddoo..",
        ".....oooooo....",
    ];

    private static readonly string[] Strawberry =
    [
        "....q....",
        ".qqgGgqq.",
        "oqgGqGgqo",
        "oHlbybbdo",
        "olybbbydo",
        "obbbybbdo",
        ".obybbdo.",
        ".obbbydo.",
        "..obddo..",
        "...ooo...",
    ];

    /// <summary>과일: 사과(빨강·초록·노랑), 배 두 가지, 바나나, 체리 한 쌍, 오렌지, 레몬, 자두, 딸기. 잎 달린 것은 좌우로 뒤집은 것도.</summary>
    public static StampSet Fruit()
    {
        var cherries = Compose(13, 14, [(Art(CherryStems, Leaf, CherryStem), 0, 0), (Art(Cherry, CherryRed), 0, 8), (Art(Cherry, CherryRed), 7, 8)]);
        var shapes = new List<PixelImage>();
        shapes.AddRange(Mirrored([
            Art(Apple, Leaf, Stem, RedApple), Art(Apple, Leaf, Stem, GreenApple), Art(Pear, Leaf, Stem, PearSkin),
            Art(Banana, BananaSkin), cherries,
        ]));
        shapes.Add(Art(Pear, Leaf, Stem, BoscPear));
        shapes.Add(Art(SmallApple, Stem, RedApple));
        shapes.Add(Art(SmallApple, Stem, YellowApple));
        shapes.Add(Art(OrangeFruit, Leaf, OrangeSkin));
        shapes.Add(Art(Lemon, LemonSkin));
        shapes.Add(Art(Plum, Stem, PlumSkin));
        shapes.Add(Art(Strawberry, Leaf, StrawberrySkin));
        return Set("Fruit", shapes);
    }

    // ───────────── 사탕·과자 ─────────────

    private static readonly string[] WrapLeft = ["oo..", "olo.", "olbo", "olbd", "obdo", "odo.", "oo.."];
    private static readonly string[] WrapRight = ["..oo", ".obo", "obdo", "dbdo", "obdo", ".odo", "..oo"];
    private static readonly string[] CandyBody = [".ooooo.", "oHlbbdo", "olbbbdo", "obbbddo", ".ooooo."];

    // 양 끝을 비튼 포장 사탕(가로 15×7). 줄무늬면 몸통에 흰 사선
    private static PixelImage WrappedCandy(Dictionary<char, uint> p, bool striped)
    {
        var body = Art(CandyBody, p);
        if (striped)
            body = Paint(7, 5, (x, y) => CandyBody[y][x] is 'l' or 'b' or 'd' && (x + y) % 3 == 0
                ? new Color32(CandyBody[y][x] == 'd' ? 0xFFDCDCE6 : 0xFFFFFFFF) : body.Get(x, y));
        return Compose(15, 7, [(Art(WrapLeft, p), 0, 0), (Art(WrapRight, p), 11, 0), (body, 4, 1)]);
    }

    // 소용돌이 막대사탕: 두 색 나선 + 흰 막대
    private static PixelImage Lollipop(uint a, uint b, uint outline, int size, int stick)
    {
        double c = (size - 1) / 2.0;
        bool In(int x, int y) => x >= 0 && y >= 0 && x < size && y < size && InCircle(size, x, y);
        var disc = Paint(size, size, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return new Color32(outline);
            double dx = x - c, dy = y - c, r = Math.Sqrt(dx * dx + dy * dy);
            if (Math.Abs(dx + size * 0.22) < 0.8 && Math.Abs(dy + size * 0.22) < 0.8) return new Color32(0xFFFFFFFF);
            double k = Math.Atan2(dy, dx) / (2 * Math.PI) + r / 3.2;
            uint col = ((int)Math.Floor(k * 2) & 1) == 0 ? a : b;
            double light = (dx + dy) / (size * 0.9);
            return new Color32(light > 0.25 ? Mix(col, outline, 0.3) : light < -0.35 ? Mix(col, 0xFFFFFFFF, 0.3) : col);
        });
        int sx = (int)Math.Floor(c);
        var stickImg = Paint(2, stick, (x, _) => new Color32(x == 0 ? 0xFFF6F2EA : 0xFFC4BAA8));
        return Compose(size, size + stick - 2, [(stickImg, sx, size - 2), (disc, 0, 0)]);
    }

    // 설탕 묻은 젤리(둥근 지붕, 바닥 평평, H 설탕 알갱이)
    private static readonly string[] BigGumdrop =
    [
        "...ooo...",
        "..oHlHo..",
        ".oHlblbo.",
        ".olbbHdo.",
        "oHbbbbddo",
        "obbHbdbdo",
        "ooooooooo",
    ];

    private static readonly string[] SmallGumdrop =
    [
        "..ooo..",
        ".oHlbo.",
        "oHlbHdo",
        "olbbbdo",
        "obHbddo",
        "ooooooo",
    ];

    private static readonly Dictionary<char, uint> Chocolate = new()
        { ['o'] = 0xFF2A140A, ['g'] = 0xFF3E1E0E, ['d'] = 0xFF55291A, ['b'] = 0xFF73401F, ['l'] = 0xFF98603A };
    private static readonly Dictionary<char, uint> Foil = new() { ['S'] = 0xFFF4F6FA, ['s'] = 0xFFB4BCC8 };
    private static readonly Dictionary<char, uint> RedWrapper = new()
        { ['q'] = 0xFF5A0A12, ['e'] = 0xFF9A1420, ['r'] = 0xFFC81E2A, ['R'] = 0xFFE8505A, ['y'] = 0xFFD8A020, ['Y'] = 0xFFFFE070 };

    private static readonly string[] ChocolateBar =
    [
        "oooooooSsqqqqqqqq",
        "olbglbgSSRRRYYRRq",
        "obdgbdgsSrrrYyrrq",
        "oggggggSsrrrYyrrq",
        "olbglbgSSrrryyrrq",
        "obdgbdgsSrrryyrrq",
        "oggggggSseeeyyeeq",
        "ooooooosSqqqqqqqq",
    ];

    private static readonly Dictionary<char, uint> Cream = new() { ['c'] = 0xFFFFF6E6, ['C'] = 0xFFE6D2B2 };
    private static readonly Dictionary<char, uint> Ganache = new() { ['c'] = 0xFF6A3A1E, ['C'] = 0xFF45220E };
    private static readonly Dictionary<char, uint> Jam = new() { ['c'] = 0xFFE03050, ['C'] = 0xFF9E1830 };
    private static readonly Dictionary<char, uint> PinkShell = Pal(0xFF8A3456, 0xFFD8749A, 0xFFF5A3C0, 0xFFFFCCDD, 0xFFFFF0F6);
    private static readonly Dictionary<char, uint> MintShell = Pal(0xFF2E6A54, 0xFF6CC0A0, 0xFF9EE6C8, 0xFFCCF6E4, 0xFFF4FFFA);
    private static readonly Dictionary<char, uint> LavenderShell = Pal(0xFF4E3A7A, 0xFF9A84CC, 0xFFC2AEEC, 0xFFE2D6FA, 0xFFF8F4FF);
    private static readonly Dictionary<char, uint> LemonShell = Pal(0xFF7A6410, 0xFFE0C04A, 0xFFF8E07A, 0xFFFFF0B4, 0xFFFFFFF0);

    private static readonly string[] Macaron =
    [
        "..oooooooo..",
        ".oHlllbbbdo.",
        "olbbbbbbbbdo",
        "oldldldldldo",
        "occcccccccCo",
        "oCCCCCCCCCCo",
        "oldldldldldo",
        "obbbbbbbbddo",
        ".oddddddddo.",
        "..oooooooo..",
    ];

    private static readonly string[] BeanFlat = [".oooo.", "oHlbbo", "obbddo", ".oooo."];
    private static readonly string[] BeanTilt = ["..ooo", ".oHbo", "olbdo", "obdo.", "ooo.."];

    private static PixelImage JellyBeans(uint seed, params Dictionary<char, uint>[] colors)
    {
        var parts = new List<PixelImage>();
        for (int i = 0; i < colors.Length; i++)
            parts.Add((i % 4) switch
            {
                0 => Art(BeanFlat, colors[i]),
                1 => Art(BeanTilt, colors[i]),
                2 => Transpose(Art(BeanFlat, colors[i])),
                _ => FlipX(Art(BeanTilt, colors[i])),
            });
        return Scatter(13, 11, parts, seed, 0);
    }

    private static readonly Dictionary<char, uint> Dough = Pal(0xFF6A3810, 0xFFB06A2A, 0xFFD99A4E, 0xFFF0C27E, 0xFFFFE6B8);
    private static readonly Dictionary<char, uint> PinkIcing = Pal(0xFFA02E62, 0xFFE0609A, 0xFFFF8EC0, 0xFFFFC2DE, 0xFFFFFFFF);
    private static readonly Dictionary<char, uint> ChocoIcing = Pal(0xFF2A1408, 0xFF4E2814, 0xFF6E3C20, 0xFF94603A, 0xFFC8987A);
    private static readonly uint[] SprinkleColors = [0xFFFFE04A, 0xFF4AD8F0, 0xFFFFFFFF, 0xFFFF4A5A, 0xFF5AD86A, 0xFF5A7AFF];

    // 위에서 본 도넛: 반죽 고리 + 물결 진 아이싱 + 뿌린 알갱이
    private static PixelImage Donut(Dictionary<char, uint> icing, uint seed)
    {
        const int S = 14;
        const double c = (S - 1) / 2.0;
        double D(int x, int y) => Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
        bool DoughIn(int x, int y) => D(x, y) <= 6.6 && D(x, y) >= 2.2;
        bool IcingIn(int x, int y)
        {
            double d = D(x, y), ang = Math.Atan2(y - c, x - c);
            return d >= 2.9 && d <= 5.0 + 0.8 * Math.Sin(ang * 6 + seed);
        }
        var dough = Shade(S, S, DoughIn, Dough);
        var top = Shade(S, S, IcingIn, icing, (c - 3.2, c - 3.2, 0.6));
        var rng = new Lcg(seed);
        for (int i = 0, placed = 0; i < 200 && placed < 9; i++)
        {
            int x = rng.Next(S), y = rng.Next(S), dir = rng.Next(2);
            int x2 = x + dir, y2 = y + 1 - dir;
            if (Tone(IcingIn, x, y, null) is 'o' or ' ' || Tone(IcingIn, x2, y2, null) is 'o' or ' ') continue;
            var col = new Color32(SprinkleColors[placed % SprinkleColors.Length]);
            top.Set(x, y, col);
            top.Set(x2, y2, col);
            placed++;
        }
        return Compose(S, S, [(dough, 0, 0), (top, 0, 0)]);
    }

    /// <summary>사탕·과자: 비튼 포장 사탕, 막대사탕, 젤리, 판 초콜릿, 마카롱, 젤리빈 한 줌, 도넛.</summary>
    public static StampSet Candies()
    {
        var shapes = new List<PixelImage>
        {
            WrappedCandy(Red, true), WrappedCandy(Blue, false), WrappedCandy(Green, true), WrappedCandy(Purple, false),
            Transpose(WrappedCandy(Pink, true)), Transpose(WrappedCandy(Yellow, false)),
            Lollipop(0xFFE8283A, 0xFFFFF4F4, 0xFF7A0E1A, 11, 8),
            Lollipop(0xFFFF6AB0, 0xFFFFE04A, 0xFF8A1E50, 9, 7),
            Lollipop(0xFF2E74E8, 0xFF7EE68A, 0xFF0E2A6E, 9, 6),
            Art(BigGumdrop, Red), Art(SmallGumdrop, Green), Art(BigGumdrop, Yellow), Art(SmallGumdrop, Purple), Art(BigGumdrop, Orange),
            Art(Macaron, Cream, PinkShell), Art(Macaron, Ganache, MintShell), Art(Macaron, Jam, LavenderShell), Art(Macaron, Cream, LemonShell),
            JellyBeans(1, Red, Yellow, Green, Purple), JellyBeans(2, Orange, Pink, White, Blue), JellyBeans(3, Black, Red, Teal),
            Donut(PinkIcing, 3), Donut(ChocoIcing, 7),
        };
        shapes.AddRange(Mirrored([Art(ChocolateBar, Chocolate, Foil, RedWrapper)]));
        return Set("Candies & Sweets", shapes);
    }

    // ───────────── 풍선 ─────────────

    // 풍선 몸통(달걀꼴, 기울기) + 매듭. 줄이 시작할 자리를 함께 준다
    private static (PixelImage Img, int Kx, int Ky) BalloonBody(Dictionary<char, uint> p, int bw, int bh, double tiltDeg)
    {
        double t = tiltDeg * Math.PI / 180, cs = Math.Cos(t), sn = Math.Sin(t);
        int w = bw + 6, h = bh + 6;
        double cx = (w - 1) / 2.0, cy = (bh - 1) / 2.0 + 1, rx = bw / 2.0, ry = bh / 2.0;
        bool In(int x, int y)
        {
            double dx = x - cx, dy = y - cy, u = dx * cs + dy * sn, v = -dx * sn + dy * cs;
            double r = v > 0 ? rx * (1 - 0.2 * v / ry) : rx;
            return (u / r) * (u / r) + (v / ry) * (v / ry) <= 1 - 0.4 / ry;
        }
        double su = -0.4 * rx, sv = -0.45 * ry;
        var img = Shade(w, h, In, p, (cx + su * cs - sv * sn, cy + su * sn + sv * cs, Math.Max(0.9, rx * 0.24)));
        return Knot(img, (int)Math.Round(cx - ry * sn), (int)Math.Round(cy), p);
    }

    // 몸통 아래 끝(kx 열)에 매듭을 달고 줄 시작 자리를 준다
    private static (PixelImage Img, int Kx, int Ky) Knot(PixelImage img, int kx, int fromY, Dictionary<char, uint> p)
    {
        int y = fromY;
        while (y < img.Height && img.Get(kx, y).A > 0) y++;
        var big = Compose(img.Width, Math.Max(img.Height, y + 3), [(img, 0, 0)]);
        big.Set(kx, y, new Color32(p['o']));
        big.Set(kx - 1, y + 1, new Color32(p['o']));
        big.Set(kx, y + 1, new Color32(p['d']));
        big.Set(kx + 1, y + 1, new Color32(p['o']));
        return (big, kx, y + 2);
    }

    // 꼬불꼬불한 리본 줄
    private static void Curl(PixelImage img, int x0, int y0, int len, double phase, Color32 c)
    {
        int px = x0;
        for (int i = 0; i < len; i++)
        {
            int x = x0 + (int)Math.Round(1.8 * (Math.Sin(i * 0.8 + phase) - Math.Sin(phase))), y = y0 + i;
            while (Math.Abs(x - px) > 1) { px += Math.Sign(x - px); if (img.Contains(px, y)) img.Set(px, y, c); }
            if (img.Contains(x, y)) img.Set(x, y, c);
            px = x;
        }
    }

    private static PixelImage WithString((PixelImage Img, int Kx, int Ky) body, int len, double phase, uint color)
    {
        var img = Compose(body.Img.Width + 4, body.Ky + len, [(body.Img, 2, 0)]);
        Curl(img, body.Kx + 2, body.Ky, len, phase, new Color32(color));
        return img;
    }

    private static PixelImage Balloon(Dictionary<char, uint> p, int bw, int bh, double tilt, int len, double phase)
        => WithString(BalloonBody(p, bw, bh, tilt), len, phase, p['d']);

    private static PixelImage HeartBalloon(Dictionary<char, uint> p, int w, int h, int len, double phase)
    {
        bool In(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return false;
            double X = -1.15 + 2.3 * (x + 0.5) / w, Y = 1.25 - 2.3 * (y + 0.5) / h, q = X * X + Y * Y - 1;
            return q * q * q - X * X * Y * Y * Y <= 0;
        }
        var img = Shade(w, h, In, p, (w * 0.27, h * 0.27, 0.9));
        return WithString(Knot(img, w / 2, h / 2, p), len, phase, p['d']);
    }

    private static PixelImage StarBalloon(Dictionary<char, uint> p, int size, int len, double phase)
    {
        double c = (size - 1) / 2.0, R = size / 2.0, r = R * 0.58;
        var pts = new (double X, double Y)[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, rad = i % 2 == 0 ? R : r;
            pts[i] = (c + Math.Cos(a) * rad, c + 0.5 + Math.Sin(a) * rad);
        }
        bool In(int x, int y)
        {
            bool inside = false;
            for (int i = 0, j = 9; i < 10; j = i++)
                if ((pts[i].Y > y) != (pts[j].Y > y) && x < (pts[j].X - pts[i].X) * (y - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
                    inside = !inside;
            return inside;
        }
        var img = Shade(size, size + 1, In, p, (c - R * 0.3, c - R * 0.2, 0.8));
        return WithString(Knot(img, (int)Math.Round(c), (int)c, p), len, phase, p['d']);
    }

    // 풍선 셋을 한 줄로 묶은 다발
    private static PixelImage Bunch(Dictionary<char, uint> a, Dictionary<char, uint> b, Dictionary<char, uint> c, double phase)
    {
        var back = BalloonBody(b, 10, 12, 2);
        var left = BalloonBody(a, 9, 11, -16);
        var right = BalloonBody(c, 9, 11, 16);
        const int bx = 7, lx = 0, ly = 5, rx = 13, ry = 5;
        int mx = back.Kx + bx, my = Math.Max(left.Ky + ly, right.Ky + ry) + 5;
        var img = Compose(32, my + 10, [(back.Img, bx, 0), (left.Img, lx, ly), (right.Img, rx, ry)]);
        Line(img, back.Kx + bx, back.Ky, mx, my, new Color32(b['d']));
        Line(img, left.Kx + lx, left.Ky + ly, mx, my, new Color32(a['d']));
        Line(img, right.Kx + rx, right.Ky + ry, mx, my, new Color32(c['d']));
        Curl(img, mx, my + 1, 8, phase, new Color32(b['d']));
        return img;
    }

    /// <summary>풍선: 여러 색·크기·기울기의 고무풍선(반짝이와 꼬불 리본), 하트 풍선, 세 개 다발, 별 은박 풍선.</summary>
    public static StampSet Balloons() => Set("Balloons",
    [
        Balloon(Red, 10, 12, 0, 10, 0.3),
        Balloon(Blue, 9, 11, -12, 9, 1.7),
        Balloon(Yellow, 9, 11, 10, 9, 3.1),
        Balloon(Green, 8, 10, 0, 8, 4.4),
        Balloon(Purple, 10, 12, 8, 11, 2.2),
        Balloon(Pink, 8, 10, -8, 8, 5.3),
        Balloon(Orange, 9, 11, -15, 10, 0.9),
        Balloon(Teal, 7, 9, 6, 7, 2.8),
        Balloon(White, 9, 11, 4, 9, 1.2),
        HeartBalloon(Red, 11, 10, 10, 1.4),
        HeartBalloon(Pink, 9, 8, 8, 3.6),
        Bunch(Red, Yellow, Blue, 0.6),
        Bunch(Pink, Purple, Teal, 2.4),
        StarBalloon(Gold, 15, 9, 1.0),
        StarBalloon(Silver, 13, 8, 4.0),
    ]);

    // ───────────── 색종이 조각 ─────────────

    // c 밝은 면, a 바탕, b 그늘 면
    private static Dictionary<char, uint> Conf(Dictionary<char, uint> p) => new() { ['c'] = p['l'], ['a'] = p['b'], ['b'] = p['d'] };

    private static readonly Dictionary<char, uint>[] ConfettiColors = [Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink, Gold];

    private static readonly string[][] Pieces =
    [
        ["ca", "ab"],
        ["cca", "caa", "abb"],
        ["caaa", "abbb"],
        ["ca", "ca", "aa", "ab", "bb"],
        [".c", "ca", "ab", "b."],
        ["..ca", ".cab", "cab.", "ab.."],
        ["ca..", "cab.", ".aab", "..bb"],
        [".c.", "cab", ".b."],
        [".ca.", "caab", "aabb", ".bb."],
        ["..c..", "ccaab", ".cab.", ".a.b."],
        [".cc..aa.", "c..ca..a", "a..ab..b", ".bb..bb."],
        ["ca...ca.", "..a.b..a", "...b...b"],
        [".cca.", "c...a", "a.c.b", "a..b.", ".bb.."],
    ];

    private static PixelImage ConfettiCluster(uint seed)
    {
        var rng = new Lcg(seed);
        var parts = new List<PixelImage>();
        int n = 6 + rng.Next(3);
        for (int i = 0; i < n; i++)
        {
            var glyph = Pieces[rng.Next(Pieces.Length - 3)];
            var img = Art(glyph, Conf(ConfettiColors[rng.Next(ConfettiColors.Length)]));
            parts.Add(rng.Next(2) == 0 ? Transpose(img) : img);
        }
        return Scatter(16, 14, parts, seed * 7 + 3, 1);
    }

    /// <summary>색종이 조각: 여러 색의 네모·띠·동그라미 조각(기울기 여러 가지), 꼬불 리본 조각, 작은 별, 조각 여럿을 흩은 한 줌.</summary>
    public static StampSet Confetti()
    {
        var shapes = new List<PixelImage>();
        for (int i = 0; i < Pieces.Length; i++)
        {
            shapes.Add(Art(Pieces[i], Conf(ConfettiColors[i % ConfettiColors.Length])));
            shapes.Add(Art(Pieces[i], Conf(ConfettiColors[(i + 4) % ConfettiColors.Length])));
        }
        for (uint s = 1; s <= 8; s++) shapes.Add(ConfettiCluster(s));
        return Set("Confetti & Streamers", shapes);
    }

    // ───────────── 물감 자국 ─────────────

    private static bool InBlobs(List<(double X, double Y, double R)> blobs, int x, int y)
    {
        foreach (var (bx, by, r) in blobs)
            if ((x - bx) * (x - bx) + (y - by) * (y - by) <= r * r) return true;
        return false;
    }

    // 사방으로 튄 물감: 울퉁불퉁한 가운데 덩어리 + 뾰족하게 뻗은 줄기(끝에 방울) + 떨어진 방울
    private static PixelImage Splat(Dictionary<char, uint> p, int size, uint seed)
    {
        var rng = new Lcg(seed);
        double c = (size - 1) / 2.0, R = size * 0.24;
        var waves = new (int K, double A, double P)[4];
        for (int i = 0; i < waves.Length; i++) waves[i] = (3 + i + rng.Next(3), 0.05 + rng.Next(7) / 100.0, rng.Next(628) / 100.0);
        var spikes = new (double A, double L, double W)[3 + rng.Next(3)];
        var drops = new List<(double X, double Y, double R)>();
        double first = rng.Next(628) / 100.0;
        for (int i = 0; i < spikes.Length; i++)
        {
            double a = first + i * 2 * Math.PI / spikes.Length + (rng.Next(80) - 40) / 100.0, l = 0.25 + rng.Next(30) / 100.0;
            spikes[i] = (a, l, 0.3 + rng.Next(15) / 100.0);
            if (rng.Next(3) > 0)
            {
                double d = R * (1 + l) + 1.6 + rng.Next(10) / 10.0;
                drops.Add((c + Math.Cos(a) * d, c + Math.Sin(a) * d, 0.75 + rng.Next(50) / 100.0));
            }
        }
        for (int i = 0, n = 3 + rng.Next(4); i < n; i++)
        {
            double a = rng.Next(628) / 100.0, d = R * (1.45 + rng.Next(45) / 100.0);
            drops.Add((c + Math.Cos(a) * d, c + Math.Sin(a) * d, 0.75 + rng.Next(70) / 100.0));
        }
        double Radius(double ang)
        {
            double r = 1;
            foreach (var (k, amp, ph) in waves) r += amp * Math.Sin(k * ang + ph);
            foreach (var (sa, sl, sw) in spikes)
            {
                double d = Math.Abs(Math.IEEERemainder(ang - sa, 2 * Math.PI)) / sw;
                if (d < 1) r += sl * (1 - d) * (1 - d);
            }
            return R * r;
        }
        bool In(int x, int y)
        {
            double dx = x - c, dy = y - c;
            return dx * dx + dy * dy <= Radius(Math.Atan2(dy, dx)) * Radius(Math.Atan2(dy, dx)) || InBlobs(drops, x, y);
        }
        return Shade(size, size, In, p, (c - R * 0.4, c - R * 0.4, Math.Max(0.8, R * 0.22)));
    }

    // 흘러내린 물감: 위쪽 웅덩이 + 아래로 흐른 줄기(이음매는 둥글게)와 끝 방울
    private static PixelImage Drip(Dictionary<char, uint> p, int w, int h, uint seed)
    {
        var rng = new Lcg(seed);
        double cx = (w - 1) / 2.0, rx = w / 2.0, cy = 2.8, ph1 = rng.Next(628) / 100.0, ph2 = rng.Next(628) / 100.0;
        // 위쪽 물감 띠(아래 가장자리가 물결) + 길이가 제각각인 줄기와 끝에 맺힌 방울
        double Top(double x) => 0.6 + 0.6 * Math.Sin(x * 0.6 + ph1);
        double Bottom(double x) => 5.2 + 0.8 * Math.Sin(x * 0.8 + ph2);
        var streams = new List<(double Mid, double Half, int End)>();
        int count = Math.Max(2, w / 5);
        for (int i = 0; i < count; i++)
        {
            double mid = 2 + (w - 5) * (i + 0.5) / count;
            int len = ((i + rng.Next(3)) % 3) switch { 0 => 1 + rng.Next(2), 1 => 4 + rng.Next(3), _ => 7 + rng.Next(Math.Max(1, h - 14)) };
            streams.Add((mid, 1.0, (int)Bottom(mid) + len));
        }
        bool In(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return false;
            double dx = (x - cx) / rx;
            if (dx * dx * dx * dx < 1 - 0.15 && y >= Top(x) && y <= Bottom(x)) return true;
            foreach (var (mid, half, end) in streams)
            {
                if (y >= cy && y <= end && Math.Abs(x - mid) <= half) return true;
                double drop = half + 0.8;
                if ((x - mid) * (x - mid) + (y - end - 0.5) * (y - end - 0.5) <= drop * drop) return true;
            }
            return false;
        }
        return Shade(w, h, In, p, (2.5, 2.2, 0.7));
    }

    // 붓으로 쓱 칠한 자국: 폭이 거의 같은 붓길, 양 끝은 붓털 길이대로 들쭉날쭉, 붓결 줄무늬
    private static PixelImage Smear(Dictionary<char, uint> p, int w, int h, uint seed)
    {
        var rng = new Lcg(seed);
        double ph = rng.Next(628) / 100.0, half = h / 2.0 - 1.2, amp = 0.8;
        double Mid(double x) => (h - 1) / 2.0 + amp * Math.Sin(x / w * Math.PI * 1.4 + ph);
        var cutL = new int[h * 2 + 1];
        var cutR = new int[h * 2 + 1];
        for (int i = 0; i < cutL.Length; i++) { cutL[i] = rng.Next(3); cutR[i] = rng.Next(6); }
        int Band(int x, int y) => Math.Clamp((int)Math.Round(y - Mid(x)) + h, 0, h * 2);
        bool In(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return false;
            double off = y - Mid(x);
            // 왼쪽 끝은 둥글게 눌린 자리
            double edge = Math.Abs(off) / (half + 0.3), left = 1 + (1 - Math.Sqrt(Math.Max(0, 1 - edge * edge))) * 2.5;
            return Math.Abs(off) <= half + 0.3 && x >= left + cutL[Band(x, y)] && x < w - cutR[Band(x, y)];
        }
        return Paint(w, h, (x, y) =>
        {
            char t = Tone(In, x, y, (4, Mid(4) - half * 0.5, 0.8));
            if (t == ' ') return Color32.Transparent;
            int streak = Band(x, y) % 3;
            if (t == 'b') t = streak == 0 ? 'l' : streak == 2 && (x + y) % 7 != 0 ? 'd' : 'b';
            return new Color32(p[t]);
        });
    }

    /// <summary>물감 자국: 여러 색(빨강·파랑·노랑·초록·보라·하양·검정 등)의 튄 자국과 방울, 흘러내린 자국, 붓으로 문지른 자국. 왼쪽 위에 윤기.</summary>
    public static StampSet PaintSplatters() => Set("Paint Splatters",
    [
        Splat(Red, 22, 1), Splat(Blue, 20, 2), Splat(Yellow, 18, 3), Splat(Green, 24, 4), Splat(Purple, 16, 5),
        Splat(White, 18, 6), Splat(Black, 20, 7), Splat(Orange, 14, 8), Splat(Pink, 16, 9), Splat(Teal, 12, 10),
        Drip(Red, 20, 20, 11), Drip(Blue, 18, 17, 12), Drip(Green, 15, 16, 13),
        Smear(Blue, 26, 10, 14), Smear(Purple, 22, 9, 15),
    ]);

    // ───────────── 통나무·그루터기 ─────────────

    // 나무껍질: o 테두리, k 갈라진 틈, d·b·l 어두운→밝은 면
    private static readonly Dictionary<char, uint> Oak = new()
        { ['o'] = 0xFF2C1A0E, ['k'] = 0xFF3E2614, ['d'] = 0xFF5A3A22, ['b'] = 0xFF77502E, ['l'] = 0xFF946A40, ['H'] = 0xFFAE8456 };
    private static readonly Dictionary<char, uint> Birch = new()
        { ['o'] = 0xFF4A4642, ['k'] = 0xFF2A2624, ['d'] = 0xFFB8B2A6, ['b'] = 0xFFDEDAD0, ['l'] = 0xFFF6F4EE, ['H'] = 0xFFFFFFFF };

    private const uint Sapwood = 0xFFE8C48A, Heartwood = 0xFFD3A462, GrowthRing = 0xFFAE7A40, Pith = 0xFF8A5A2C;

    // 자른 면(나이테): 껍질 테두리 + 나이테 + 가운데 심
    private static PixelImage EndGrain(int w, int h, Dictionary<char, uint> bark)
    {
        bool In(int x, int y) => InEllipse(w, h, x, y);
        double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
        return Paint(w, h, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return new Color32(bark['o']);
            double Q(int i, int j)
            {
                double dx = (i - cx) / (w / 2.0), dy = (j - cy) / (h / 2.0);
                return Math.Sqrt(dx * dx + dy * dy) * w / 4.0;
            }
            bool rim = !In(x - 2, y) || !In(x + 2, y) || (h >= 8 && (!In(x, y - 2) || !In(x, y + 2)));
            if (rim) return new Color32(bark[(x - cx) + (y - cy) < 0 ? 'l' : 'b']);
            double q = Q(x, y);
            if (q < 0.45) return new Color32(Pith);
            // 나이테 선: 안쪽 띠와 맞닿은 칸
            int band = (int)Math.Floor(q);
            if (band >= 1 && ((int)Math.Floor(Q(x - 1, y)) < band || (int)Math.Floor(Q(x, y - 1)) < band
                || (int)Math.Floor(Q(x + 1, y)) < band || (int)Math.Floor(Q(x, y + 1)) < band)) return new Color32(GrowthRing);
            return new Color32((x - cx) / w + (y - cy) / h < -0.1 ? Sapwood : Heartwood);
        });
    }

    // 누운 통나무: 왼쪽 끝에 자른 면, 몸통은 가로 껍질 결
    private static PixelImage Log(int len, int d, Dictionary<char, uint> bark, uint seed)
    {
        int ew = Math.Max(4, (int)Math.Round(d * 0.7));
        bool Body(int x, int y) => y >= 0 && y < d && x >= ew / 2 && x < len && (x <= len - 1 - ew / 2 || InEllipse(ew, d, x - (len - ew), y));
        var rng = new Lcg(seed);
        var cracks = new HashSet<(int, int)>();
        for (int i = 0; i < len * d / 14; i++)
        {
            int y = 2 + rng.Next(d - 4), x = ew / 2 + rng.Next(Math.Max(1, len - ew)), l = 3 + rng.Next(4);
            for (int k = 0; k < l; k++) cracks.Add((x + k, y));
        }
        var body = Paint(len, d, (x, y) =>
        {
            if (!Body(x, y)) return Color32.Transparent;
            if (Edge(Body, x, y)) return new Color32(bark['o']);
            char c = y * 4 < d ? 'l' : y * 4 >= d * 3 ? 'd' : 'b';
            if (cracks.Contains((x, y))) c = 'k';
            return new Color32(bark[c]);
        });
        return Compose(len, d, [(body, 0, 0), (EndGrain(ew, d, bark), 0, 0)]);
    }

    private static PixelImage LogPile()
    {
        var end = EndGrain(9, 9, Oak);
        return Compose(17, 16, [(end, 0, 7), (end, 8, 7), (end, 4, 0)]);
    }

    // 그루터기: 위에서 비스듬히 본 나이테 면 + 세로 껍질 결 + 바닥에 퍼진 뿌리
    private static PixelImage Stump(int w, int h, int topH, Dictionary<char, uint> bark, uint seed)
    {
        int W = w + 4;
        var rng = new Lcg(seed);
        var grooves = new HashSet<int>();
        for (int x = 3 + rng.Next(2); x < w + 1; x += 2 + rng.Next(2)) grooves.Add(x);
        bool Trunk(int x, int y)
        {
            if (y < topH / 2 || y >= h) return false;
            int root = Math.Min(2, y - (h - 4));
            int l = 2, r = w + 1;
            if (root > 0) { l -= root; r += root; }
            return x >= l && x <= r;
        }
        var trunk = Paint(W, h, (x, y) =>
        {
            if (!Trunk(x, y)) return Color32.Transparent;
            if (Edge(Trunk, x, y)) return new Color32(bark['o']);
            char c = x <= 3 ? 'l' : x >= w ? 'd' : 'b';
            if (grooves.Contains(x) && (y * 3 + x) % 7 != 0) c = 'k';
            return new Color32(bark[c]);
        });
        return Compose(W, h, [(trunk, 0, 0), (EndGrain(w, topH, bark), 2, 0)]);
    }

    private static readonly Dictionary<char, uint> Axe = new()
    {
        ['n'] = 0xFF2E343C, ['m'] = 0xFF8A96A4, ['M'] = 0xFFDCE2EA,
        ['k'] = 0xFF4A2A12, ['j'] = 0xFF8E5A2C, ['h'] = 0xFFC48A4E,
    };

    private static readonly string[] AxeArt =
    [
        "..........kk",
        ".........khk",
        "........khjk",
        ".......khjk.",
        "......khjk..",
        "..nnnkhjk...",
        ".nmmmnjk....",
        "nMmmmmn.....",
        "nMMmmmn.....",
        ".nnnnn......",
    ];

    private static PixelImage StumpWithAxe()
    {
        var stump = Stump(14, 13, 6, Oak, 9);
        return Compose(stump.Width, stump.Height + 6, [(stump, 0, 6), (Art(AxeArt, Axe), 4, 0)]);
    }

    private static readonly Dictionary<char, uint> SplitFace = new()
    {
        ['o'] = 0xFF2C1A0E, ['B'] = 0xFF946A40, ['b'] = 0xFF77502E,
        ['e'] = 0xFFE2B878, ['E'] = 0xFFB98448, ['w'] = 0xFFD8AE74, ['g'] = 0xFFB08050,
    };

    private static readonly string[] SplitLog =
    [
        "...ooooooooooo..",
        "..oeoBBBBBBBBBo.",
        ".oeeeoBbbbbbbbbo",
        ".oeEeowwwwwwwwwo",
        "oeeEeowgwwwwgwwo",
        "oeeeeowwwwgwwwwo",
        "oooooooooooooooo",
    ];

    private static readonly string[] SmallSplit =
    [
        "..ooooooooo.",
        ".oeoBBBBBBBo",
        ".oeeowwwwwwo",
        "oeEeowgwwgwo",
        "oeeeowwwwwwo",
        "oooooooooooo",
    ];

    private static readonly Dictionary<char, uint> MossyBark = new()
    {
        ['o'] = 0xFF2C1A0E, ['k'] = 0xFF3E2614, ['d'] = 0xFF5A3A22, ['b'] = 0xFF77502E, ['l'] = 0xFF946A40,
        ['w'] = 0xFFE0BC86, ['M'] = 0xFF8FCB4A, ['m'] = 0xFF5A9E2E, ['n'] = 0xFF2F6A1E,
    };

    private static readonly string[] Branch =
    [
        "...................oo...",
        ".................obo....",
        "...MM...........obo.....",
        "..MmmM...MMm...obo......",
        ".oommnooommmnoobooooooo.",
        "owwlllllllllllllllllllko",
        "owwbbbbbkbbbbbbbbkbbbbbo",
        "oowddddddddkddddddddddo.",
        "..oooooooooooooooooooo..",
    ];

    /// <summary>통나무·그루터기: 나이테가 보이는 누운 통나무(참나무·자작나무), 쌓은 통나무 셋, 크고 작은 그루터기, 도끼 꽂힌 그루터기, 쪼갠 장작, 이끼 낀 나뭇가지.</summary>
    public static StampSet Firewood()
    {
        var shapes = new List<PixelImage>();
        shapes.AddRange(Mirrored([
            Log(18, 8, Oak, 1), Log(13, 7, Oak, 2), Log(16, 7, Birch, 3),
            Art(SplitLog, SplitFace), Art(SmallSplit, SplitFace), Art(Branch, MossyBark),
        ]));
        shapes.Add(LogPile());
        shapes.Add(Stump(12, 12, 6, Oak, 4));
        shapes.Add(Stump(8, 9, 4, Oak, 5));
        shapes.Add(StumpWithAxe());
        return Set("Logs & Stumps", shapes);
    }
}
