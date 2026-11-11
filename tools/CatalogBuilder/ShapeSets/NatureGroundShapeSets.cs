using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 땅 위에 흩뿌리는 자연물 모양 펜 세트 여섯 가지: 버섯, 숲 바닥(도토리·솔방울·잔가지·낙엽), 수련 잎, 조개·불가사리, 발자국, 물웅덩이.
/// 손으로 그린 글자 그림과 글자 마스크(둘레 테두리·왼쪽 위 빛을 자동으로 칠함)를 섞는다. 발자국·웅덩이는 반투명이라 어느 땅 색에도 어울린다.
/// 같은 코드면 늘 같은 모양이다.
/// </summary>
internal static class NatureGroundShapeSets
{
    // ───────────── 공통: 마스크 칠하기 ─────────────

    // 재질 다섯 톤: 테두리, 그늘, 바탕, 밝은 곳, 가장 밝은 곳
    private readonly record struct Tones(uint O, uint D, uint M, uint L, uint H);

    /// <summary>
    /// 글자 마스크 → 그림. '#' = 몸(둘레는 테두리색, 안쪽 가장자리는 왼쪽 위 밝게·오른쪽 아래 어둡게),
    /// '=' 테두리색, '-' 그늘, '*' 바탕, '+' 밝은 곳, '^' 가장 밝은 곳, 그 밖 글자는 details 팔레트, '.' 비움.
    /// </summary>
    private static PixelImage Shade(string[] rows, Tones t, params Dictionary<char, uint>[] details)
    {
        int w = rows.Max(r => r.Length), h = rows.Length;
        char At(int x, int y) => y >= 0 && y < h && x >= 0 && x < rows[y].Length ? rows[y][x] : '.';
        bool In(int x, int y) => At(x, y) != '.';
        bool Core(int x, int y) => In(x, y) && !Edge(In, x, y);
        uint Rim(int x, int y)
        {
            bool left = !Core(x - 1, y), up = !Core(x, y - 1), right = !Core(x + 1, y), down = !Core(x, y + 1);
            if ((left || up) && !(right || down)) return left && up ? t.H : t.L;
            if ((right || down) && !(left || up)) return t.D;
            return t.M;
        }
        var image = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char c = At(x, y);
                uint argb = c switch
                {
                    '.' => 0,
                    '#' => Edge(In, x, y) ? t.O : Rim(x, y),
                    '=' => t.O,
                    '-' => t.D,
                    '*' => t.M,
                    '+' => t.L,
                    '^' => t.H,
                    _ => Lookup(details, c),
                };
                if (argb != 0) image.Pixels[y * w + x] = new Color32(argb);
            }
        return image;
    }

    private static uint Lookup(Dictionary<char, uint>[] palettes, char c)
    {
        foreach (var palette in palettes)
            if (palette.TryGetValue(c, out var argb)) return argb;
        return 0;
    }

    private static string[] Grid(int w, int h, Func<int, int, char> f)
        => [.. Enumerable.Range(0, h).Select(y => new string(Enumerable.Range(0, w).Select(x => f(x, y)).ToArray()))];

    private static char CharAt(string[] rows, int x, int y) => y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length ? rows[y][x] : '.';

    // 시계 방향 90도
    private static string[] RotCW(string[] rows)
    {
        int h = rows.Length, w = rows.Max(r => r.Length);
        return Grid(h, w, (x, y) => CharAt(rows, y, h - 1 - x));
    }

    private static double Sq(double v) => v * v;

    private static IEnumerable<(int X, int Y)> Line(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            yield return (x0, y0);
            if (x0 == x1 && y0 == y1) yield break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>
    /// 막대들(잔가지·줄기): 마디마다 굵기 T(완만하면 세로로, 가파르면 가로로 쌓음). 위·왼쪽 줄은 밝게, 아래·오른쪽 끝 줄은 테두리색.
    /// 먼저 모든 막대의 테두리를 깔고 그 위에 속을 칠해 이음매가 깔끔하다.
    /// </summary>
    private static PixelImage Sticks(Tones t, IEnumerable<(int X0, int Y0, int X1, int Y1, int T)> segments)
    {
        var segs = segments.ToList();
        int w = segs.Max(s => Math.Max(s.X0, s.X1)) + 4, h = segs.Max(s => Math.Max(s.Y0, s.Y1)) + 4;
        var image = new PixelImage(w, h);
        for (int pass = 0; pass < 2; pass++)
            foreach (var (x0, y0, x1, y1, th) in segs)
            {
                bool steep = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
                foreach (var (x, y) in Line(x0, y0, x1, y1))
                    for (int k = 0; k < th; k++)
                    {
                        int px = steep ? x + k : x, py = steep ? y : y + k;
                        uint c = pass == 0 ? t.O : k == th - 1 ? 0 : k == 0 && th >= 3 ? t.L : t.M;
                        if (c != 0 && image.Contains(px, py)) image.Set(px, py, new Color32(c));
                    }
            }
        return image;
    }

    /// <summary>
    /// 잎 마스크(위에서 봄): 길이 len, 너비 wid, 방향 deg(0 = 오른쪽, 90 = 위). 밑동이 넓고 끝이 뾰족하다.
    /// 'v' = 잎맥·잎자루, '#' = 잎.
    /// </summary>
    private static string[] LeafMask(double len, double wid, double deg, bool vein = true, bool stem = true)
    {
        int size = (int)Math.Ceiling(len) + 6;
        double a = deg * Math.PI / 180, ux = Math.Cos(a), uy = -Math.Sin(a), c = (size - 1) / 2.0, hl = len / 2.0, hw = wid / 2.0;
        return Grid(size, size, (x, y) =>
        {
            double dx = x - c, dy = y - c, u = dx * ux + dy * uy, v = -dx * uy + dy * ux;
            if (stem && u <= -hl + 0.5 && u >= -hl - 2.5 && Math.Abs(v) < 0.55) return 'v';
            double t = u / hl;
            if (t <= -1 || t >= 1) return '.';
            double half = hw * Math.Sqrt(1 - t * t) * (t > 0 ? 1 - 0.45 * t : 1);
            if (Math.Abs(v) > half) return '.';
            if (vein && Math.Abs(v) < 0.55 && t < 0.7) return 'v';
            return '#';
        });
    }

    // ───────────── 버섯 ─────────────

    // o 테두리, d 그늘, r 바탕, R 밝은 곳, w 흰 점, v 그늘진 흰 점, g 주름
    private static readonly Dictionary<char, uint> RedCap = new()
        { ['o'] = 0xFF5A0E12, ['d'] = 0xFFA41C22, ['r'] = 0xFFD8302C, ['R'] = 0xFFF46A50, ['w'] = 0xFFF8F2E6, ['v'] = 0xFFD8C8BC, ['g'] = 0xFFB89C78 };
    private static readonly Dictionary<char, uint> OrangeCap = new()
        { ['o'] = 0xFF6A2A08, ['d'] = 0xFFC0501A, ['r'] = 0xFFEE8226, ['R'] = 0xFFFFB45A, ['w'] = 0xFFFAF4E2, ['v'] = 0xFFDCCCB0, ['g'] = 0xFFC0A27A };
    // 대: s 테두리, t 그늘, c 바탕, C 밝은 곳
    private static readonly Dictionary<char, uint> Stalk = new() { ['s'] = 0xFF6E5A42, ['t'] = 0xFFC4AE88, ['c'] = 0xFFE6D8B8, ['C'] = 0xFFFAF2DE };
    // 그물버섯: b 바탕, B 밝은 곳, y 관공
    private static readonly Dictionary<char, uint> BoleteCap = new()
        { ['o'] = 0xFF3C2210, ['d'] = 0xFF6E401C, ['b'] = 0xFF94582A, ['B'] = 0xFFBE8446, ['y'] = 0xFFD6C27A };
    // o 테두리, d 그늘, m 바탕, l 밝은 곳, H 가장 밝은 곳
    private static readonly Dictionary<char, uint> TanCap = new() { ['o'] = 0xFF5C3E22, ['d'] = 0xFFA0723E, ['m'] = 0xFFC8985C, ['l'] = 0xFFE2BC80, ['H'] = 0xFFF4DCA8 };
    private static readonly Dictionary<char, uint> GreyCap = new() { ['o'] = 0xFF443426, ['d'] = 0xFF7E6048, ['m'] = 0xFFA88866, ['l'] = 0xFFC4A47E, ['H'] = 0xFFDCC09C };
    private static readonly Dictionary<char, uint> WhiteCap = new() { ['o'] = 0xFF6E6252, ['d'] = 0xFFC8BCA8, ['m'] = 0xFFE4DACA, ['l'] = 0xFFF4EEE2, ['H'] = 0xFFFFFFFF };

    private static readonly string[] Toadstool =
    [
        "....ooooo....",
        "..ooRRwwroo..",
        ".oRRrwwwrrro.",
        ".oRrrrwwrrdo.",
        "oRwwrrrrrvvdo",
        "orwwrrrrrvvdo",
        "orrrrrwrrrddo",
        "oddrrrrrrdddo",
        ".oogggggggoo.",
        "....sCcts....",
        "...sCCccts...",
        "...sttttts...",
        "....sCcts....",
        "....sCcts....",
        "...sCcccts...",
        "...sssssss...",
    ];

    private static readonly string[] FlatToadstool =
    [
        "...ooooooooo...",
        ".ooRwwrrrwwroo.",
        "oRRrrrrwwrrrddo",
        "orwwrrrwwrrvddo",
        "oddrrrrrrrrdddo",
        ".oogggggggggoo.",
        ".....sCcts.....",
        ".....sCcts.....",
        "....sCCccts....",
        "....sttttts....",
        ".....sCcts.....",
        ".....sCcts.....",
        ".....sCcts.....",
        "....sCcccts....",
        "....sssssss....",
    ];

    private static readonly string[] SmallToadstool =
    [
        "..ooo..",
        ".oRwro.",
        "oRrrrdo",
        "owrrvdo",
        "odddddo",
        ".ogggo.",
        "..sCs..",
        "..sCs..",
        ".sCcts.",
        ".sssss.",
    ];

    private static readonly string[] Bolete =
    [
        "...ooooo...",
        ".ooBBbbboo.",
        "oBBbbbbbbdo",
        "oBbbbbbbddo",
        "obbbbbbdddo",
        "odddddddddo",
        ".oyyyyyyyo.",
        "..sCcccts..",
        "..sCcccts..",
        ".sCCcccts..",
        ".sCcccctts.",
        "..sssssss..",
    ];

    private static readonly string[] SmallBolete =
    [
        "..ooo..",
        ".oBbbo.",
        "oBbbbdo",
        "odddddo",
        ".oyyyo.",
        ".sCcts.",
        ".sCcts.",
        ".sssss.",
    ];

    private static readonly string[] TanTall =
    [
        "..ooo..",
        ".oHlmo.",
        "oHllmdo",
        "odddddo",
        "..sCs..",
        "..sCs..",
        "..sCs..",
        "..sCs..",
        "..sCs..",
    ];

    private static readonly string[] TanMid =
    [
        ".ooo.",
        "oHlmo",
        "odddo",
        ".sCs.",
        ".sCs.",
        ".sCs.",
    ];

    private static readonly string[] TanTiny =
    [
        ".ooo.",
        "oHmdo",
        ".sCs.",
        ".sCs.",
    ];

    private static readonly string[] Slender =
    [
        "..ooo..",
        ".oHmmo.",
        "oHmmmdo",
        "ommmddo",
        ".odddo.",
        "..sCs..",
        "..sCs..",
        "..sCs..",
        "...sCs.",
        "...sCs.",
        "...sCs.",
        "...sCs.",
        "..sCcs.",
        "..ssss.",
    ];

    private static readonly string[] Button =
    [
        ".ooo.",
        "oHlmo",
        "odddo",
        ".sCs.",
        ".sss.",
    ];

    /// <summary>버섯: 흰 점 빨간 광대버섯(세 크기·주황 변형), 그물버섯, 황갈색 버섯 무리, 가늘고 키 큰 버섯, 작은 양송이.</summary>
    public static StampSet Mushrooms()
    {
        var shapes = new List<PixelImage>();
        shapes.AddRange(Mirrored([Art(Toadstool, Stalk, RedCap), Art(FlatToadstool, Stalk, RedCap)]));
        shapes.Add(Art(SmallToadstool, Stalk, RedCap));
        shapes.Add(Art(Toadstool, Stalk, OrangeCap));
        shapes.Add(Art(SmallToadstool, Stalk, OrangeCap));
        shapes.Add(Art(Bolete, Stalk, BoleteCap));
        shapes.Add(Art(SmallBolete, Stalk, BoleteCap));
        var tall = Art(TanTall, Stalk, TanCap);
        var mid = Art(TanMid, Stalk, TanCap);
        var tiny = Art(TanTiny, Stalk, TanCap);
        shapes.AddRange(Mirrored(
        [
            Compose(17, 12, [(Art(Bolete, Stalk, BoleteCap), 0, 0), (Art(SmallBolete, Stalk, BoleteCap), 10, 4)]),
            Compose(14, 9, [(tall, 3, 0), (mid, 0, 3), (tiny, 9, 5)]),
            Compose(13, 10, [(tall, 0, 1), (mid, 7, 0), (tiny, 3, 6)]),
            Art(Slender, Stalk, GreyCap),
            Compose(9, 7, [(Art(Button, Stalk, WhiteCap), 0, 2), (Art(Button, Stalk, WhiteCap), 4, 0)]),
        ]));
        shapes.Add(Art(Button, Stalk, WhiteCap));
        return Set("Mushrooms", shapes);
    }

    // ───────────── 숲 바닥 ─────────────

    // 도토리: a 깍정이 테두리, c 깍정이, C 깍정이 밝은 곳, k 꼭지 / 열매 o d m l H
    private static readonly Dictionary<char, uint> AcornCap = new() { ['a'] = 0xFF3E2A14, ['c'] = 0xFF6E5030, ['C'] = 0xFF9C7A4A, ['k'] = 0xFF4A3218 };
    private static readonly Dictionary<char, uint> AcornNut = new() { ['o'] = 0xFF4A2810, ['d'] = 0xFF8A4E1C, ['m'] = 0xFFB0702A, ['l'] = 0xFFCC8E3E, ['H'] = 0xFFEAB868 };

    private static readonly string[] Acorn =
    [
        "...k...",
        ".aaaaa.",
        "aCcCcCa",
        "acCcCca",
        "aaaaaaa",
        "oHllmmo",
        "oHlmmdo",
        "olmmddo",
        ".omddo.",
        "..odo..",
        "...o...",
    ];

    private static readonly string[] SmallAcorn =
    [
        "..k..",
        ".aaa.",
        "aCcCa",
        "aaaaa",
        "oHlmo",
        "olmdo",
        ".odo.",
        "..o..",
    ];

    private static readonly string[] LyingAcorn =
    [
        "..aaaooooo..",
        ".aCcaHHlllo.",
        "kacCaHllllmo",
        ".aCcalllmmdo",
        "..aaaommmdo.",
        ".....ooooo..",
    ];

    private static readonly Tones Bark = new(0xFF3A2414, 0xFF5A3A1E, 0xFF7C5634, 0xFFA27A50, 0xFFC09A6C);
    private static readonly Tones Cone = new(0xFF3A2212, 0xFF5E3A1C, 0xFF8A5A2E, 0xFFB07E46, 0xFFD0A066);
    private static readonly Tones Chestnut = new(0xFF2E140A, 0xFF5E2614, 0xFF7E3418, 0xFFA84E26, 0xFFD88A5A);
    private static readonly Dictionary<char, uint> ChestnutBase = new() { ['p'] = 0xFFC8AE86 };
    private static readonly Tones OakBrown = new(0xFF4A2A12, 0xFF7A4A20, 0xFF9E6430, 0xFFC08448, 0xFFD8A060);
    private static readonly Tones LeafOchre = new(0xFF5E4010, 0xFFA87A1E, 0xFFCC9A2E, 0xFFE8C05A, 0xFFF6DC8A);
    private static readonly Tones LeafRust = new(0xFF5A2210, 0xFFA0461E, 0xFFC8622A, 0xFFE88A48, 0xFFF6B070);
    private static readonly Tones FernGreen = new(0xFF1E4A18, 0xFF2E7224, 0xFF4C9A34, 0xFF7CC450, 0xFFA8E07A);
    private static Dictionary<char, uint> Vein(uint argb) => new() { ['v'] = argb };

    private static readonly string[] ChestnutMask =
    [
        "...##...",
        "..####..",
        ".######.",
        "########",
        "########",
        "########",
        "#pppppp#",
        ".######.",
    ];

    private static readonly string[] ConeStanding =
    [
        "...#...",
        "..###..",
        ".#####.",
        ".#####.",
        "#######",
        "#######",
        "#######",
        "#######",
        "#######",
        ".#####.",
        "..###..",
    ];

    private static readonly string[] SmallCone =
    [
        "..#..",
        ".###.",
        "#####",
        "#####",
        "#####",
        "#####",
        ".###.",
    ];

    // 솔방울 비늘: 안쪽 칸에 대각선 격자(어두운 틈)와 비늘마다 밝은 위쪽
    private static string[] Scales(string[] mask)
    {
        bool In(int x, int y) => CharAt(mask, x, y) != '.';
        return Grid(mask.Max(r => r.Length), mask.Length, (x, y) =>
        {
            char c = CharAt(mask, x, y);
            if (c != '#' || Edge(In, x, y)) return c;
            int a = (x + y) % 3, b = ((x - y) % 3 + 3) % 3;
            if (a == 0 || b == 0) return '-';
            return a == 1 ? '+' : '*';
        });
    }

    private static readonly string[] Maple = Symmetric(
    [
        "......#",
        ".....##",
        "...#.##",
        "...###v",
        ".#..##v",
        ".##v##v",
        "..##v#v",
        "...###v",
        "..##.#v",
        "......v",
        "......v",
    ]);

    // 참나무 잎: 가장자리가 둥글게 굽이치는 잎(가운데 열이 잎맥)
    private static readonly string[] Oak = Symmetric(
    [
        "....#",
        "...##",
        "..##v",
        ".###v",
        "..##v",
        ".###v",
        "####v",
        ".###v",
        "..##v",
        ".###v",
        "..##v",
        "...#v",
        "....v",
        "....v",
    ]);

    // 은행잎: 위 가운데가 갈라진 부채, 잎맥은 아래로 모인다
    private static readonly string[] Ginkgo =
    [
        ".####.####.",
        "###-#-#-###",
        "###-#-#-###",
        ".###-#-###.",
        "..##-#-##..",
        "...#####...",
        "....###....",
        ".....v.....",
        ".....v.....",
    ];

    private static PixelImage Fern()
    {
        var segs = new List<(int, int, int, int, int)>();
        void Frond(int bx, int by, int lean, int height, int reach)
        {
            segs.Add((bx, by, bx + lean, by - height, 2));
            for (int i = 2; i < height - 1; i += 2)
            {
                int px = bx + lean * i / height, py = by - i, len = Math.Max(1, reach * (height - i + 2) / height);
                if (i % 4 == 2) segs.Add((px, py, px - len, py - len, 2));
                else segs.Add((px + 1, py, px + 1 + len, py - len, 2));
            }
        }
        Frond(11, 15, 0, 14, 4);
        Frond(9, 15, -6, 9, 3);
        Frond(12, 15, 6, 9, 3);
        return Sticks(FernGreen, segs);
    }

    /// <summary>숲 바닥: 도토리(서 있는·누운), 밤, 솔방울(선·누운), 잔가지, 마른 잎(참나무·너도밤나무·단풍), 고사리 싹.</summary>
    public static StampSet ForestFloor()
    {
        var shapes = new List<PixelImage>
        {
            Art(Acorn, AcornCap, AcornNut),
            Art(SmallAcorn, AcornCap, AcornNut),
            Shade(ChestnutMask, Chestnut, ChestnutBase),
            Shade(Scales(ConeStanding), Cone),
            Shade(Scales(SmallCone), Cone),
        };
        shapes.AddRange(Mirrored(
        [
            Art(LyingAcorn, AcornCap, AcornNut),
            Shade(Scales(RotCW(ConeStanding)), Cone),
            Sticks(Bark, [(0, 5, 16, 1, 3), (9, 3, 13, 0, 2), (5, 5, 8, 8, 2)]),
            Sticks(Bark, [(0, 2, 11, 4, 2), (6, 3, 10, 0, 2)]),
            Fern(),
        ]));
        shapes.Add(Shade(Oak, OakBrown, Vein(0xFF6A3E1A)));
        shapes.Add(Shade(RotCW(Oak), OakBrown, Vein(0xFF6A3E1A)));
        shapes.Add(Shade(LeafMask(9, 5, 20), LeafOchre, Vein(0xFF8A6418)));
        shapes.Add(Shade(LeafMask(9, 5, 115), LeafRust, Vein(0xFF8A3A16)));
        shapes.Add(Shade(Maple, LeafRust, Vein(0xFF8A3A16)));
        shapes.Add(Shade(Ginkgo, LeafOchre, Vein(0xFF8A6418)));
        return Set("Forest Floor", shapes);
    }

    // ───────────── 수련 잎 ─────────────

    private static readonly Tones PadGreen = new(0xFF1C4A22, 0xFF2E7A34, 0xFF48A044, 0xFF72C45C, 0xFFA4E084);
    private static readonly Tones PadOlive = new(0xFF2E4A16, 0xFF547A22, 0xFF7AA234, 0xFF9EC452, 0xFFC8E07C);
    private static readonly Dictionary<char, uint> PadVein = new() { ['v'] = 0xFF3C8C3A };
    private static readonly Dictionary<char, uint> OliveVein = new() { ['v'] = 0xFF668E2A };
    private static readonly Tones PinkPetal = new(0xFFA8326E, 0xFFE070A8, 0xFFF496C8, 0xFFFFC8E4, 0xFFFFEEF6);
    private static readonly Tones WhitePetal = new(0xFF8C88A8, 0xFFCACADC, 0xFFE8E8F2, 0xFFF8F8FC, 0xFFFFFFFF);
    private static readonly Dictionary<char, uint> FlowerHeart = new() { ['y'] = 0xFFF8C830, ['Y'] = 0xFFFFF09A };

    // 둥근 잎에 가운데까지 파인 홈(notchDeg 방향, 0 = 오른쪽, 90 = 위)과 방사형 잎맥
    private static string[] PadMask(int size, double notchDeg)
    {
        double c = (size - 1) / 2.0, a = notchDeg * Math.PI / 180;
        return Grid(size, size, (x, y) =>
        {
            if (!InCircle(size, x, y)) return '.';
            double dx = x - c, dy = c - y, r = Math.Sqrt(dx * dx + dy * dy);
            double ang = Math.Atan2(dy, dx), diff = Math.Abs(Math.IEEERemainder(ang - a, 2 * Math.PI));
            if (r > 0.6 && diff * r < 0.45 + 0.13 * r) return '.';
            if (size >= 11 && r > 1.5 && r < size / 2.0 - 1.5)
                for (int k = 1; k < 8; k++)
                    if (Math.Abs(Math.IEEERemainder(ang - a - k * Math.PI / 4, 2 * Math.PI)) * r < 0.5) return 'v';
            return '#';
        });
    }

    private static PixelImage Pad(int size, double notchDeg, bool olive = false)
        => Shade(PadMask(size, notchDeg), olive ? PadOlive : PadGreen, olive ? OliveVein : PadVein);

    // 수련꽃 꽃잎 지도(위에서 봄): 대문자 = 위 꽃잎(위아래·좌우), 소문자 = 그 밑 대각선 꽃잎, y = 노란 술
    private static readonly string[] BigBloom =
    [
        ".....N.....",
        ".aa.NNN.bb.",
        ".aaaNNNbbb.",
        "..aaNNNbb..",
        ".WWWyyyEEE.",
        "WWWWyyyEEEE",
        ".WWWyyyEEE.",
        "..ccSSSdd..",
        ".cccSSSddd.",
        ".cc.SSS.dd.",
        ".....S.....",
    ];

    private static readonly string[] SmallBloom =
    [
        "....N....",
        ".a.NNN.b.",
        ".aaNNNbb.",
        ".WWyyyEE.",
        "WWWyyyEEE",
        ".WWyyyEE.",
        ".ccSSSdd.",
        ".c.SSS.d.",
        "....S....",
    ];

    /// <summary>
    /// 수련꽃: 바깥 둘레는 테두리색, 밑 꽃잎이 위 꽃잎에 가린 경계는 그늘색, 꽃잎마다 왼쪽 위 가장자리가 밝다. 술은 왼쪽 위가 밝은 노랑.
    /// </summary>
    private static PixelImage LilyFlower(string[] map, Tones t)
    {
        int w = map.Max(r => r.Length), h = map.Length;
        char At(int x, int y) => CharAt(map, x, y);
        return Paint(w, h, (x, y) =>
        {
            char k = At(x, y);
            if (k == '.') return Color32.Transparent;
            if (k == 'y') return new Color32(At(x - 1, y) != 'y' || At(x, y - 1) != 'y' ? FlowerHeart['Y'] : FlowerHeart['y']);
            char left = At(x - 1, y), up = At(x, y - 1), right = At(x + 1, y), down = At(x, y + 1);
            if (left == '.' || up == '.' || right == '.' || down == '.') return new Color32(t.O);
            bool Under(char n) => n != k && n != 'y' && char.IsLower(k) && char.IsUpper(n);
            if (Under(left) || Under(up) || Under(right) || Under(down)) return new Color32(t.D);
            if (left != k || up != k) return new Color32(t.L);
            return new Color32(t.M);
        });
    }

    private static readonly Dictionary<char, uint> BudColors = new()
        { ['r'] = 0xFF8A2A5A, ['p'] = 0xFFF08CC0, ['P'] = 0xFFFFC2E0, ['q'] = 0xFFC85A96, ['g'] = 0xFF2E7A34, ['G'] = 0xFF5AAE48, ['k'] = 0xFF1C4A22 };

    private static readonly string[] Bud =
    [
        "..r..",
        ".rPr.",
        "rPpqr",
        "rPpqr",
        "kGpgk",
        ".kgk.",
    ];

    // 개구리밥 한 잎(2×2, 3×2)
    private static readonly Dictionary<char, uint> Duck = new() { ['L'] = 0xFFA8E060, ['g'] = 0xFF62AE38, ['k'] = 0xFF2E6A1E };
    private static readonly string[] DuckSmall = ["Lg", "gk"];
    private static readonly string[] DuckBig = ["LLg", "ggk"];

    private static PixelImage Duckweed(uint seed, int count, int w, int h)
    {
        var rng = new Lcg(seed);
        var parts = new List<(PixelImage, int, int)>();
        var small = Art(DuckSmall, Duck);
        var big = Art(DuckBig, Duck);
        for (int i = 0; i < count; i++) parts.Add((rng.Next(3) == 0 ? big : small, rng.Next(w - 2), rng.Next(h - 1)));
        return Compose(w, h, parts);
    }

    // 물결 고리(반투명): 왼쪽 위는 밝게, 오른쪽 아래는 옅게
    private static PixelImage Ripple(int w, int h)
        => Paint(w, h, (x, y) =>
        {
            bool In(int px, int py) => px >= 0 && py >= 0 && px < w && py < h && InEllipse(w, h, px, py);
            if (!In(x, y) || !Edge(In, x, y)) return Color32.Transparent;
            return x + y < (w + h) / 2 ? new Color32(0x90E8F4FF) : new Color32(0x50E8F4FF);
        });

    private static PixelImage WithRipple(PixelImage pad)
    {
        int w = pad.Width + 4, h = pad.Height + 4;
        return Compose(w, h, [(Ripple(w, h), 0, 0), (pad, 2, 2)]);
    }

    /// <summary>수련 잎(위에서 봄): 홈 방향이 다른 여러 크기의 둥근 잎, 분홍·흰 수련꽃과 봉오리를 얹은 잎, 개구리밥, 물결 고리.</summary>
    public static StampSet LilyPads()
    {
        var shapes = new List<PixelImage>
        {
            Pad(15, 30),
            Pad(13, 200),
            Pad(11, 110),
            Pad(9, 300),
            Pad(7, 160),
            Pad(13, 250, olive: true),
            Pad(9, 45, olive: true),
            WithRipple(Pad(11, 340)),
            Compose(15, 15, [(Pad(15, 120), 0, 0), (LilyFlower(BigBloom, PinkPetal), 2, 2)]),
            Compose(13, 13, [(Pad(13, 300), 0, 0), (LilyFlower(SmallBloom, WhitePetal), 2, 2)]),
            Compose(11, 11, [(Pad(11, 20), 0, 0), (Art(Bud, BudColors), 3, 3)]),
            LilyFlower(SmallBloom, PinkPetal),
            Duckweed(3, 6, 9, 6),
            Duckweed(8, 4, 6, 5),
            Compose(13, 10, [(Ripple(13, 10), 0, 0), (Ripple(7, 5), 3, 2)]),
        };
        return Set("Lily Pads", shapes);
    }

    // ───────────── 조개·불가사리 ─────────────

    private static readonly Tones ShellPeach = new(0xFF7A3A1E, 0xFFD07A4A, 0xFFF0A070, 0xFFFFC898, 0xFFFFE8D0);
    private static readonly Tones ShellPink = new(0xFF7A2A44, 0xFFD06A8A, 0xFFF09AB0, 0xFFFFC4D2, 0xFFFFE8EE);
    private static readonly Tones ShellCream = new(0xFF6E5A3C, 0xFFC8B08A, 0xFFE6D4B0, 0xFFF6EAD0, 0xFFFFFAF0);
    private static readonly Tones ShellLilac = new(0xFF5A4A6A, 0xFFB0A0C4, 0xFFD8CCE6, 0xFFF0EAF6, 0xFFFFFFFF);
    private static readonly Tones Mussel = new(0xFF141A2A, 0xFF26304A, 0xFF3A4A6E, 0xFF5A6E98, 0xFF8AA0C8);
    private static readonly Tones StarOrange = new(0xFF7A2A0A, 0xFFC8501A, 0xFFF07A2A, 0xFFFFA454, 0xFFFFD090);
    private static readonly Tones StarPurple = new(0xFF3E1A5A, 0xFF7A3AA0, 0xFFA45AC8, 0xFFC88AE4, 0xFFE8C0F6);
    private static readonly Tones StarRed = new(0xFF6A1414, 0xFFB02828, 0xFFD84438, 0xFFF07858, 0xFFFFB090);
    private static readonly Tones SandDollar = new(0xFF8A7458, 0xFFC8B494, 0xFFE2D4B8, 0xFFF2EAD6, 0xFFFFFAF0);
    private static readonly Tones PebbleGrey = new(0xFF3C4048, 0xFF6E747E, 0xFF9198A2, 0xFFB6BCC4, 0xFFDCE0E6);
    private static readonly Tones PebbleSand = new(0xFF5C4630, 0xFF9C8060, 0xFFBCA07C, 0xFFD6BE9C, 0xFFEEDCC0);
    private static readonly Tones PebbleDark = new(0xFF1E2024, 0xFF34383E, 0xFF4A5058, 0xFF666E78, 0xFF8C96A0);

    // 가리비: 부채 모양에 경첩 쪽으로 모이는 골, 아래 양쪽 귀
    private static readonly string[] Scallop =
    [
        "...#####...",
        ".#########.",
        "##-##-##-##",
        "##-##-##-##",
        "###-#-#-###",
        ".##-#-#-##.",
        "..##-#-##..",
        "...#####...",
        "..#######..",
        "..#######..",
    ];

    private static readonly string[] SmallScallop =
    [
        "..###..",
        ".#####.",
        "##-#-##",
        "##-#-##",
        ".#-#-#.",
        "..###..",
        ".#####.",
    ];

    // 대합: 위 가운데 꼭지에서 퍼지는 타원과 성장선
    private static string[] ClamMask(int w, int h)
    {
        double cx = (w - 1) / 2.0;
        return Grid(w, h, (x, y) =>
        {
            if (y == 0) return Math.Abs(x - cx) <= 1 ? '#' : '.';
            if (!InEllipse(w, h - 1, x, y - 1)) return '.';
            double d = Math.Sqrt(Sq((x - cx) / (w / 2.0)) + Sq(y / (h - 0.5)));
            return (d > 0.52 && d < 0.64) || (d > 0.8 && d < 0.9) ? '-' : '#';
        });
    }

    private static readonly Dictionary<char, uint> ConchColors = new()
    {
        ['o'] = 0xFF6A3E22, ['s'] = 0xFFA06A44, ['d'] = 0xFFC88A5A, ['m'] = 0xFFE6B07E, ['l'] = 0xFFF4CFA0, ['H'] = 0xFFFFEBD0,
        ['p'] = 0xFFE88A8A, ['P'] = 0xFFFFC8C0,
    };

    private static readonly string[] Conch =
    [
        "....o.....",
        "...oHo....",
        "...oHmo...",
        "..osssso..",
        "..oHllmo..",
        ".osssssso.",
        ".oHlllmmo.",
        "oHllllmmdo",
        "oHlllmppdo",
        "ollllmpPdo",
        ".ollmmpdo.",
        "..olmmdo..",
        "...oldo...",
        "....oo....",
    ];

    // 개오지: 반들반들한 달걀꼴 껍데기에 갈색 점
    private static readonly Tones Cowrie = new(0xFF5A3A1E, 0xFFB08050, 0xFFD8A870, 0xFFEED0A0, 0xFFFFF4E0);
    private static readonly Dictionary<char, uint> CowrieSpots = new() { ['k'] = 0xFF8A5428 };

    private static readonly string[] CowrieMask =
    [
        "..#####..",
        ".#k##k##.",
        "#^##k##k#",
        "##k##k###",
        ".###k##k.",
        "..#####..",
    ];

    private static readonly string[] BigStar =
    [
        "......#......",
        ".....###.....",
        ".....###.....",
        "....##^##....",
        "#####+^+#####",
        ".####^^^####.",
        "...##+^+##...",
        "...#######...",
        "..####.####..",
        "..###...###..",
        ".###.....###.",
        ".##.......##.",
    ];

    private static readonly string[] SmallStar =
    [
        "....#....",
        "...###...",
        "...###...",
        "####^####",
        ".##^^^##.",
        "..#####..",
        "..##.##..",
        ".##...##.",
        ".#.....#.",
    ];

    private static readonly string[] SandDollarBig =
    [
        "...#####...",
        ".#########.",
        ".####-####.",
        "#####-#####",
        "##--###--##",
        "#####=#####",
        "###########",
        "####-#-####",
        ".##-###-##.",
        ".#########.",
        "...#####...",
    ];

    private static readonly string[] SandDollarSmall =
    [
        "..###..",
        ".##-##.",
        "#-###-#",
        "###=###",
        "##-#-##",
        ".#####.",
        "..###..",
    ];

    private static string[] Oval(int w, int h) => Grid(w, h, (x, y) => InEllipse(w, h, x, y) ? '#' : '.');

    /// <summary>조개·불가사리: 가리비, 소라, 대합, 홍합, 개오지, 주황·보라·빨강 불가사리, 성게 껍데기(모래 달러), 조약돌.</summary>
    public static StampSet Seashells()
    {
        var shapes = new List<PixelImage>
        {
            Shade(Scallop, ShellPeach),
            Shade(Scallop, ShellPink),
            Shade(SmallScallop, ShellCream),
            Shade(SmallScallop, ShellPeach),
            Shade(ClamMask(10, 9), ShellLilac),
            Shade(ClamMask(8, 7), ShellCream),
            Shade(BigStar, StarOrange),
            Shade(RotCW(BigStar), StarPurple),
            Shade(SmallStar, StarOrange),
            Shade([.. SmallStar.Reverse()], StarRed),
            Shade(SandDollarBig, SandDollar),
            Shade(SandDollarSmall, SandDollar),
            Compose(9, 5, [(Shade(Oval(5, 4), PebbleGrey), 0, 1), (Shade(Oval(4, 3), PebbleSand), 5, 0)]),
            Shade(Oval(6, 4), PebbleDark),
            Shade(Oval(4, 3), PebbleGrey),
        };
        shapes.AddRange(Mirrored(
        [
            Art(Conch, ConchColors),
            Shade(CowrieMask, Cowrie, CowrieSpots),
            Shade(LeafMask(9, 5, 30, vein: false, stem: false), Mussel),
        ]));
        return Set("Seashells & Starfish", shapes);
    }

    // ───────────── 발자국 ─────────────

    private const uint TrackRgb = 0x2C2A30;

    /// <summary>
    /// 눌린 자국(반투명): 빛이 왼쪽 위에서 오므로 위·왼쪽 벽은 진하게, 아래·오른쪽 벽은 옅게. 'x' = 가는 자국(새 발가락), 't' = 밑창 무늬 홈(옅게).
    /// </summary>
    private static PixelImage Print(string[] rows)
    {
        int w = rows.Max(r => r.Length), h = rows.Length;
        bool In(int x, int y) => CharAt(rows, x, y) != '.';
        return Paint(w, h, (x, y) =>
        {
            char c = CharAt(rows, x, y);
            if (c == '.') return Color32.Transparent;
            uint a = c == 'x' ? 0x80u
                : c == 't' ? 0x34u
                : !In(x - 1, y) || !In(x, y - 1) ? 0x96u
                : !In(x + 1, y) || !In(x, y + 1) ? 0x4Cu
                : 0x66u;
            return new Color32(a << 24 | TrackRgb);
        });
    }

    // 왼발 자국과 그 오른발(좌우 뒤집기)을 한 걸음 엇갈리게
    private static PixelImage Stride(string[] left, int gap, int stagger)
    {
        var l = Print(left);
        return Compose(l.Width * 2 + gap, l.Height + stagger, [(l, 0, stagger), (FlipX(l), l.Width + gap, 0)]);
    }

    private static readonly string[] Shoe =
    [
        "..###.",
        ".#####",
        "######",
        "#t#t##",
        "######",
        "##t#t#",
        "######",
        ".#t#t#",
        ".####.",
        "..###.",
        "......",
        ".####.",
        ".#tt#.",
        ".####.",
        "..##..",
    ];

    private static readonly string[] Boot =
    [
        "..####.",
        ".######",
        "##t#t##",
        "#t#t#t#",
        "##t#t##",
        "#t#t#t#",
        "##t#t##",
        "#######",
        ".#####.",
        ".#####.",
        "..###..",
        ".......",
        ".#####.",
        ".#t#t#.",
        ".#####.",
        "..###..",
    ];

    private static readonly string[] BareFoot =
    [
        "..#.##.",
        "#.#.##.",
        "#......",
        ".#####.",
        "#######",
        "######.",
        "######.",
        ".####..",
        ".###...",
        ".###...",
        ".####..",
        ".####..",
        "..##...",
    ];

    private static readonly string[] DogPaw =
    [
        "..x..x...",
        "..##.##..",
        "x.##.##.x",
        "##.....##",
        "##.###.##",
        "..#####..",
        ".#######.",
        ".#######.",
        "..##.##..",
    ];

    private static readonly string[] CatPaw =
    [
        "..#.#..",
        "#.....#",
        "..###..",
        ".#####.",
        ".#####.",
        "..#.#..",
    ];

    private static readonly string[] BirdTrack =
    [
        "x.....x",
        ".x.x.x.",
        "..xxx..",
        "...x...",
        "...x...",
        "...x...",
    ];

    private static readonly string[] Rabbit =
    [
        ".#...#.",
        "###.###",
        "###.###",
        "###.###",
        "###.###",
        ".#...#.",
        ".......",
        "..##...",
        ".####..",
        "..##...",
        ".......",
        "...##..",
        "..###..",
        "...##..",
    ];

    private static readonly string[] Hoof =
    [
        ".#.#.",
        "##.##",
        "##.##",
        "##.##",
        "##.##",
        ".#.#.",
    ];

    /// <summary>발자국(반투명, 눈·모래·진흙 어디에나): 신발·장화·맨발 한 걸음, 개·고양이 발자국, 새 발자국, 토끼 발자국, 사슴 발굽.</summary>
    public static StampSet Footprints()
    {
        var bird = Print(BirdTrack);
        var dog = Print(DogPaw);
        var cat = Print(CatPaw);
        var hoof = Print(Hoof);
        var shapes = new List<PixelImage>();
        shapes.AddRange(Mirrored(
        [
            Stride(Shoe, 2, 6),
            Stride(Boot, 2, 7),
            Stride(BareFoot, 2, 6),
            Compose(19, 14, [(dog, 0, 5), (dog, 10, 0)]),
            Compose(13, 13, [(cat, 0, 7), (cat, 5, 0)]),
            Compose(16, 10, [(bird, 0, 4), (bird, 8, 0)]),
            Compose(12, 13, [(hoof, 0, 7), (hoof, 6, 0)]),
        ]));
        shapes.Add(dog);
        shapes.Add(Compose(15, 6, [(bird, 0, 0), (bird, 8, 0)]));
        shapes.Add(Print(Rabbit));
        return Set("Footprints & Tracks", shapes);
    }

    // ───────────── 물웅덩이 ─────────────

    private static readonly Color32 PuddleEdge = new(0xC0345878);
    private static readonly Color32 PuddleShadow = new(0xA8466E96);
    private static readonly Color32 PuddleWater = new(0x98628CB8);
    private static readonly Color32 PuddleRim = new(0xB0A8D0EC);
    private static readonly Color32 PuddleGlint = new(0xD8EEF8FF);
    private static readonly Color32 PuddleRing = new(0xA0C8E2F4);

    /// <summary>웅덩이: 타원 몇 개를 겹친 불규칙한 물(씨앗 고정), 가장자리·안쪽 테두리 빛, 하늘 반사 줄, 선택적인 물결 고리.</summary>
    private static PixelImage Puddle(int w, int h, uint seed, bool ripple = false)
    {
        var rng = new Lcg(seed);
        double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
        var blobs = new List<(double X, double Y, double Rx, double Ry)> { (cx, cy, w * 0.3, h * 0.4) };
        for (int i = 0; i < 4; i++)
        {
            double bx = cx + (rng.Next(w / 2 + 1) - w / 4), by = cy + (rng.Next(5) - 2) * h * 0.09;
            double rx = Math.Min(w * (0.14 + rng.Next(16) / 100.0), Math.Min(bx, w - 1 - bx) + 0.4);
            double ry = Math.Min(h * (0.26 + rng.Next(18) / 100.0), Math.Min(by, h - 1 - by) + 0.4);
            blobs.Add((bx, by, rx, ry));
        }
        bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && blobs.Any(b => Sq((x - b.X) / b.Rx) + Sq((y - b.Y) / b.Ry) <= 1);
        bool Core(int x, int y) => In(x, y) && !Edge(In, x, y);
        int gy = (int)Math.Round(cy - h * 0.18), gx = (int)Math.Round(cx - w * 0.22), glen = Math.Max(2, w / 6);
        int rw = Math.Max(5, w / 3) | 1, rh = Math.Max(3, h / 3) | 1, rx0 = (int)cx + w / 10 - rw / 2, ry0 = (int)cy - rh / 2 + 1;
        return Paint(w, h, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return PuddleEdge;
            if ((y == gy && x >= gx && x < gx + glen) || (y == gy + 1 && x >= gx + glen && x < gx + glen + 2)) return PuddleGlint;
            if (ripple)
            {
                int qx = x - rx0, qy = y - ry0;
                bool R(int px, int py) => px >= 0 && py >= 0 && px < rw && py < rh && InEllipse(rw, rh, px, py);
                if (R(qx, qy) && Edge(R, qx, qy)) return PuddleRing;
            }
            if (!Core(x - 1, y) || !Core(x, y - 1)) return PuddleShadow;
            if (!Core(x + 1, y) || !Core(x, y + 1)) return PuddleRim;
            return PuddleWater;
        });
    }

    // 물방울: a 반짝임, b 물, c 가장자리
    private static readonly Dictionary<char, uint> Splash = new() { ['a'] = 0xE0EEF8FF, ['b'] = 0xA8628CB8, ['c'] = 0xC0345878 };
    private static readonly string[] Drop = ["ab", "bc"];
    private static readonly string[] BigDrop = ["ab.", "abc", ".cc"];
    private static readonly string[] Speck = ["a"];

    private static PixelImage Drops(params (string[] Art, int X, int Y)[] drops)
        => Compose(drops.Max(d => d.X + d.Art[0].Length), drops.Max(d => d.Y + d.Art.Length), drops.Select(d => (Art(d.Art, Splash), d.X, d.Y)));

    /// <summary>물웅덩이: 크기·모양이 다른 반투명 웅덩이, 물결 고리나 떠 있는 잎이 있는 웅덩이, 작은 물방울.</summary>
    public static StampSet Puddles()
    {
        var leaf = Shade(LeafMask(7, 4, 30), LeafOchre, Vein(0xFF8A6418));
        var redLeaf = Shade(LeafMask(7, 4, 150), LeafRust, Vein(0xFF8A3A16));
        var shapes = new List<PixelImage>
        {
            Puddle(34, 13, 1),
            Puddle(28, 11, 2),
            Puddle(24, 10, 3, ripple: true),
            Puddle(20, 8, 4),
            Puddle(16, 7, 5),
            Puddle(12, 6, 6),
            Puddle(9, 5, 7),
            Puddle(30, 12, 8, ripple: true),
            Puddle(18, 9, 10),
            Compose(22, 9, [(Puddle(22, 9, 9), 0, 0), (leaf, 5, -2)]),
            Compose(26, 10, [(Puddle(26, 10, 11), 0, 0), (redLeaf, 7, -2)]),
            Drops((BigDrop, 0, 2), (Drop, 5, 0), (Drop, 7, 4), (Speck, 3, 6), (Speck, 9, 1), (Speck, 4, 3)),
            Drops((Drop, 0, 0), (BigDrop, 3, 3), (Speck, 6, 1), (Speck, 0, 4)),
        };
        return Set("Puddles", shapes);
    }
}
