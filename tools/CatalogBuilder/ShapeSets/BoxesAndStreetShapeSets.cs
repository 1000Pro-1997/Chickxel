using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 상자·거리 소품 모양 펜 세트: 물음표 블록, 물음표 선물 상자, 보물 상자, 금화 더미, 돈 자루, 종이 상자(옆모습·45도), 나무 상자(옆모습·45도), 길에 버려진 쓰레기, 라바콘·바리케이드, 통·자루.
/// 손으로 그린 글자 그림과 글자 판(<see cref="Grid"/>)에 그린 상자·원통(<see cref="Lathe"/>)을 섞는다. 빛은 왼쪽 위에서, 테두리는 바탕보다 어둡게.
/// </summary>
internal static class BoxesAndStreetShapeSets
{
    // ───────────── 공통: 글자 판 ─────────────

    /// <summary>글자 그림을 코드로 그리는 판('.' = 투명).</summary>
    private sealed class Grid
    {
        private readonly char[][] _rows;

        public Grid(int w, int h)
        {
            W = w;
            H = h;
            _rows = [.. Enumerable.Range(0, h).Select(_ => Enumerable.Repeat('.', w).ToArray())];
        }

        public int W { get; }
        public int H { get; }

        public char this[int x, int y]
        {
            get => x >= 0 && y >= 0 && x < W && y < H ? _rows[y][x] : '.';
            set { if (x >= 0 && y >= 0 && x < W && y < H) _rows[y][x] = value; }
        }

        public void Fill(int x, int y, int w, int h, char c)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++) this[i, j] = c;
        }

        public void Stamp(string[] art, int x, int y)
        {
            for (int j = 0; j < art.Length; j++)
                for (int i = 0; i < art[j].Length; i++)
                    if (art[j][i] != '.') this[x + i, y + j] = art[j][i];
        }

        public string[] Rows() => [.. _rows.Select(r => new string(r))];
    }

    // 원기둥 명암(빛은 왼쪽 위): u = 가운데에서 -1(왼쪽 끝) ~ 1(오른쪽 끝) → 재질 글자 순서 [테두리, 밝은 곳, 밝음, 바탕, 그늘]
    private static int Tone(double u) => u < -0.62 ? 2 : u < -0.25 ? 1 : u < 0.4 ? 3 : 4;

    /// <summary>
    /// 세워 둔 원통(통·항아리·양동이): 줄마다 반지름 <paramref name="radius"/>, 줄마다 재질 글자 다섯(테두리·밝은 곳·밝음·바탕·그늘, 여섯째가 있으면 판자 이음매).
    /// <paramref name="capH"/> 높이의 윗면 타원(글자 넷: 테두리·앞 가장자리·안쪽 위·안쪽 아래)과 같은 높이로 둥근 밑면.
    /// <paramref name="seams"/>는 이음매 자리(가운데에서 반지름 비율, -1~1).
    /// </summary>
    private static string[] Lathe(int w, int h, Func<int, double> radius, Func<int, string> material, int capH = 0, string cap = "", double[]? seams = null)
    {
        double cx = (w - 1) / 2.0;
        int mid = capH / 2;
        (int X0, int W) Span(int y)
        {
            double r = radius(y);
            int x0 = (int)Math.Ceiling(cx - r + 0.001), x1 = (int)Math.Floor(cx + r - 0.001);
            return (x0, x1 - x0 + 1);
        }
        bool InCap(int x, int y)
        {
            if (capH == 0 || y < 0 || y >= capH) return false;
            var (x0, cw) = Span(mid);
            return InEllipse(cw, capH, x - x0, y);
        }
        bool InBody(int x, int y)
        {
            double r = y < capH ? Math.Min(radius(y), radius(mid)) : radius(y);
            if (y < mid || y >= h || x < 0 || x >= w || Math.Abs(x - cx) >= r) return false;
            int bottom = Math.Max(capH, 2);
            if (y < h - bottom / 2) return true;
            var (x0, bw) = Span(h - 1 - bottom / 2);
            return InEllipse(bw, bottom, x - x0, y - (h - bottom));
        }
        bool In(int x, int y) => InCap(x, y) || InBody(x, y);

        var g = new Grid(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (InCap(x, y))
                {
                    bool outline = Edge(In, x, y) || (y < mid && Edge((i, j) => InCap(i, j), x, y));
                    bool rim = !outline && Edge((i, j) => InCap(i, j), x, y);
                    g[x, y] = outline ? cap[0] : rim ? cap[1] : y < mid ? cap[2] : cap[3];
                }
                else if (InBody(x, y))
                {
                    string m = material(y);
                    double r = radius(y);
                    bool seam = m.Length > 5 && seams != null && seams.Any(k => (int)Math.Round(cx + k * r) == x);
                    g[x, y] = Edge(In, x, y) ? m[0] : seam ? m[5] : m[Tone((x - cx) / r)];
                }
            }
        return g.Rows();
    }

    // 반짝임(흰·노랑)
    private static readonly Dictionary<char, uint> Sparkle = new() { ['w'] = 0xFFFFF0A0, ['W'] = 0xFFFFFFFF };
    private static readonly string[] BigStar = ["..w..", "..w..", "wwWww", "..w..", "..w.."];
    private static readonly string[] SmallStar = [".w.", "wWw", ".w."];

    // ───────────── 1. 물음표 블록 ─────────────

    // o 테두리, d 그늘, y 바탕, H 밝은 곳, r 리벳, q 물음표, Q 물음표 그림자
    private static readonly Dictionary<char, uint> GoldBlock = new()
        { ['o'] = 0xFF6A3A04, ['d'] = 0xFFC27412, ['y'] = 0xFFF2A81C, ['H'] = 0xFFFFE07A, ['r'] = 0xFF9A5A0C, ['q'] = 0xFFFFF8E0, ['Q'] = 0xFF8A4A08 };
    private static readonly Dictionary<char, uint> PurpleBlock = new()
        { ['o'] = 0xFF341058, ['d'] = 0xFF6A28A6, ['y'] = 0xFF9A50DE, ['H'] = 0xFFD4A8FF, ['r'] = 0xFF4E1C80, ['q'] = 0xFFFFF4FF, ['Q'] = 0xFF46187A };
    private static readonly Dictionary<char, uint> BlueBlock = new()
        { ['o'] = 0xFF0C2A66, ['d'] = 0xFF1C58BE, ['y'] = 0xFF3A8CF0, ['H'] = 0xFFAAD8FF, ['r'] = 0xFF143E8E, ['q'] = 0xFFF4FAFF, ['Q'] = 0xFF123A86 };
    private static readonly Dictionary<char, uint> RedBlock = new()
        { ['o'] = 0xFF560A12, ['d'] = 0xFFA81C26, ['y'] = 0xFFE23A3C, ['H'] = 0xFFFF9C8C, ['r'] = 0xFF7A1018, ['q'] = 0xFFFFF2E8, ['Q'] = 0xFF701018 };
    private static readonly Dictionary<char, uint> GreenBlock = new()
        { ['o'] = 0xFF0E3E1A, ['d'] = 0xFF1E8A3A, ['y'] = 0xFF3CC060, ['H'] = 0xFFA8F0B0, ['r'] = 0xFF145A28, ['q'] = 0xFFF4FFF4, ['Q'] = 0xFF125224 };
    private static readonly Dictionary<char, uint> SilverBlock = new()
        { ['o'] = 0xFF3A4250, ['d'] = 0xFF8A94A4, ['y'] = 0xFFBCC6D4, ['H'] = 0xFFF4F8FC, ['r'] = 0xFF5A6474, ['q'] = 0xFF2E3C58, ['Q'] = 0xFF8A94A4 };
    // 다 쓴 빈 블록(물음표 없음)
    private static readonly Dictionary<char, uint> UsedBlock = new()
        { ['o'] = 0xFF3A1E0C, ['d'] = 0xFF6E4020, ['y'] = 0xFF9A6034, ['H'] = 0xFFC48A58, ['r'] = 0xFF4E2A12 };

    private static readonly string[] BigQ =
    [
        ".qqqq.",
        "qq..qq",
        "qq..qq",
        "...qq.",
        "..qq..",
        "..qq..",
        "......",
        "..qq..",
        "..qq..",
    ];

    private static readonly string[] SmallQ =
    [
        ".qqq.",
        "qq.qq",
        "...qq",
        "..qq.",
        "..qq.",
        ".....",
        "..qq.",
    ];

    /// <summary>앞에서 본 물음표 블록(한 변 <paramref name="s"/>): 모서리 리벳, 오른쪽 아래로 그림자 진 물음표(<paramref name="mark"/>가 거짓이면 빈 블록).</summary>
    private static string[] QBlock(int s, bool mark = true)
    {
        var g = new Grid(s, s);
        g.Fill(0, 0, s, s, 'o');
        g.Fill(1, 1, s - 2, s - 2, 'y');
        g.Fill(1, 1, s - 2, 1, 'H');
        g.Fill(1, 1, 1, s - 2, 'H');
        g.Fill(1, s - 2, s - 2, 1, 'd');
        g.Fill(s - 2, 2, 1, s - 3, 'd');
        g[0, 0] = g[s - 1, 0] = g[0, s - 1] = g[s - 1, s - 1] = '.';
        if (s >= 14) g[2, 2] = g[s - 3, 2] = g[2, s - 3] = g[s - 3, s - 3] = 'r';
        if (!mark) return g.Rows();
        var glyph = s >= 14 ? BigQ : SmallQ;
        int mw = glyph[0].Length, mh = glyph.Length, x0 = (s - mw - 1) / 2, y0 = (s - mh - 1) / 2;
        for (int j = 0; j < mh; j++)
            for (int i = 0; i < mw; i++)
                if (glyph[j][i] == 'q') g[x0 + i + 1, y0 + j + 1] = 'Q';
        for (int j = 0; j < mh; j++)
            for (int i = 0; i < mw; i++)
                if (glyph[j][i] == 'q') g[x0 + i, y0 + j] = 'q';
        return g.Rows();
    }

    // 빛나는 블록: 한 단계씩 밝게
    private static Dictionary<char, uint> LitBlock(Dictionary<char, uint> p) => new()
        { ['o'] = p['d'], ['d'] = p['y'], ['y'] = p['H'], ['H'] = 0xFFFFFFFF, ['r'] = p['y'], ['q'] = 0xFFFFFFFF, ['Q'] = p['d'] };

    /// <summary>물음표 블록: 금·보라·파랑·빨강·초록 큰 블록, 금·빨강·파랑·은 작은 블록, 빛나는 희귀 블록 둘, 다 쓴 빈 블록.</summary>
    public static StampSet MysteryBoxes()
    {
        var shapes = new List<PixelImage>();
        foreach (var p in new[] { GoldBlock, PurpleBlock, BlueBlock, RedBlock, GreenBlock }) shapes.Add(Art(QBlock(16), p));
        foreach (var p in new[] { GoldBlock, RedBlock, BlueBlock, SilverBlock }) shapes.Add(Art(QBlock(12), p));
        shapes.Add(Glow(Art(QBlock(16), LitBlock(GoldBlock)), 0xFFFFD040));
        shapes.Add(Glow(Art(QBlock(14), LitBlock(PurpleBlock)), 0xFFC07CFF));
        shapes.Add(Art(QBlock(14, mark: false), UsedBlock));
        return Set("Mystery Boxes", shapes);
    }

    // ───────────── 2. 물음표 선물 상자 ─────────────

    // 선물 상자: 3/4 상자(BoxGrid)의 o·U·T·e·F·f·s + G·g·k 리본, q 물음표 무늬
    private static readonly Dictionary<char, uint> GiftPurple = new()
        { ['o'] = 0xFF2E0E4E, ['U'] = 0xFFC898F4, ['T'] = 0xFFA870E4, ['e'] = 0xFF5A2290, ['F'] = 0xFF9A5AD8, ['f'] = 0xFF8244C4, ['s'] = 0xFF6430A0, ['q'] = 0xFFE8D0FF };
    private static readonly Dictionary<char, uint> GiftTeal = new()
        { ['o'] = 0xFF0A3A3A, ['U'] = 0xFF8AEAD8, ['T'] = 0xFF4CCCB8, ['e'] = 0xFF16706A, ['F'] = 0xFF38B4A2, ['f'] = 0xFF24A090, ['s'] = 0xFF187E72, ['q'] = 0xFFD0FFF4 };
    private static readonly Dictionary<char, uint> GiftRed = new()
        { ['o'] = 0xFF4E0810, ['U'] = 0xFFFF8A86, ['T'] = 0xFFF0545A, ['e'] = 0xFF8E1420, ['F'] = 0xFFE0444C, ['f'] = 0xFFC8303A, ['s'] = 0xFF9A1C28, ['q'] = 0xFFFFD8D0 };
    private static readonly Dictionary<char, uint> GiftGold = new()
        { ['o'] = 0xFF5A3A06, ['U'] = 0xFFFFE9A0, ['T'] = 0xFFFFD050, ['e'] = 0xFFA0700E, ['F'] = 0xFFF6C238, ['f'] = 0xFFE6A820, ['s'] = 0xFFB8800E, ['q'] = 0xFFFFF8E0 };
    private static readonly Dictionary<char, uint> GiftBlue = new()
        { ['o'] = 0xFF0C2458, ['U'] = 0xFF9ACCFF, ['T'] = 0xFF5A9CF0, ['e'] = 0xFF1A4A9A, ['F'] = 0xFF4A8AE6, ['f'] = 0xFF2E70D0, ['s'] = 0xFF1E56A8, ['q'] = 0xFFE0F0FF };
    private static readonly Dictionary<char, uint> GiftPink = new()
        { ['o'] = 0xFF5A1038, ['U'] = 0xFFFFC4E0, ['T'] = 0xFFFF8CC0, ['e'] = 0xFFA02A68, ['F'] = 0xFFF878B0, ['f'] = 0xFFE45A98, ['s'] = 0xFFB83C78, ['q'] = 0xFFFFF0F8 };
    private static readonly Dictionary<char, uint> GiftGreen = new()
        { ['o'] = 0xFF0E3A1A, ['U'] = 0xFFA8F0B0, ['T'] = 0xFF5CCC74, ['e'] = 0xFF1A6E30, ['F'] = 0xFF4AB862, ['f'] = 0xFF34A04E, ['s'] = 0xFF22783A, ['q'] = 0xFFF0FFF0 };
    private static readonly Dictionary<char, uint> RibbonGold = new() { ['G'] = 0xFFFFE890, ['g'] = 0xFFF2BA30, ['k'] = 0xFFA0700E };
    private static readonly Dictionary<char, uint> RibbonPink = new() { ['G'] = 0xFFFFD0E8, ['g'] = 0xFFFF7AB8, ['k'] = 0xFFB83A78 };
    private static readonly Dictionary<char, uint> RibbonWhite = new() { ['G'] = 0xFFFFFFFF, ['g'] = 0xFFE4E8EE, ['k'] = 0xFFA8B0BC };
    private static readonly Dictionary<char, uint> RibbonRed = new() { ['G'] = 0xFFFF8A86, ['g'] = 0xFFE0303A, ['k'] = 0xFF8E1420 };
    private static readonly Dictionary<char, uint> RibbonPurple = new() { ['G'] = 0xFFE0C0FF, ['g'] = 0xFF9A50DE, ['k'] = 0xFF4E1C80 };

    private static readonly string[] TinyQ = ["qq.", "..q", ".q.", "...", ".q."];

    private static readonly string[] GiftBow =
    [
        ".ooo..ooo.",
        "oGggoogGgo",
        "oggkGgkggo",
        ".oooooooo.",
    ];

    /// <summary>
    /// 위·앞에서 본 선물 상자 판: o 테두리, U·T 윗면(밝은 곳·바탕), e 윗면과 앞면 사이 모서리, F·f·s 앞면(밝은 곳·바탕·그늘).
    /// 높이 = 윗면 + 앞면 + 3.
    /// </summary>
    private static Grid BoxGrid(int w, int top, int front)
    {
        int h = top + front + 3;
        var g = new Grid(w, h);
        g.Fill(0, 0, w, h, 'o');
        g.Fill(1, 1, w - 2, top, 'T');
        g.Fill(1, 1, w - 2, 1, 'U');
        g.Fill(1, 1, 1, top, 'U');
        g.Fill(1, top + 1, w - 2, 1, 'e');
        g.Fill(1, top + 2, w - 2, front, 'f');
        g.Fill(1, top + 2, 1, front, 'F');
        g.Fill(w - 2, top + 2, 1, front, 's');
        g.Fill(1, h - 2, w - 2, 1, 's');
        return g;
    }

    /// <summary>물음표 무늬 포장지에 리본을 두른 3/4 상자.</summary>
    private static string[] MysteryGift(int w, int top, int front)
    {
        var box = BoxGrid(w, top, front);
        int c = w / 2 - 1, h = box.H;
        for (int y = 1; y <= top; y++) { box[c, y] = 'G'; box[c + 1, y] = 'g'; }
        for (int x = 1; x < w - 1; x++) box[x, 1 + top / 2] = x == c ? 'G' : 'g';
        box[c, top + 1] = box[c + 1, top + 1] = 'k';
        for (int y = top + 2; y < h - 1; y++) { box[c, y] = 'g'; box[c + 1, y] = 'k'; }
        int qy = top + 2 + (front - TinyQ.Length) / 2;
        box.Stamp(TinyQ, (c - 3) / 2 + 1, qy);
        box.Stamp(TinyQ, c + 2 + (w - 1 - (c + 2) - 3) / 2, qy);
        var g = new Grid(w, h + 3);
        g.Stamp(box.Rows(), 0, 3);
        g.Stamp(GiftBow, c - 4, 0);
        return g.Rows();
    }

    /// <summary>물음표 선물 상자: 일곱 색·크기의 물음표 무늬 포장 상자(리본·나비 매듭).</summary>
    public static StampSet MysteryGifts()
    {
        var shapes = new List<PixelImage>
        {
            Art(MysteryGift(14, 4, 9), GiftPurple, RibbonGold),
            Art(MysteryGift(12, 3, 8), GiftTeal, RibbonPink),
            Art(MysteryGift(16, 4, 11), GiftRed, RibbonWhite),
            Art(MysteryGift(12, 3, 9), GiftGold, RibbonRed),
            Art(MysteryGift(18, 5, 12), GiftBlue, RibbonWhite),
            Art(MysteryGift(10, 3, 7), GiftPink, RibbonPurple),
            Art(MysteryGift(14, 4, 10), GiftGreen, RibbonGold),
        };
        return Set("Mystery Gifts", shapes);
    }

    // ───────────── 3. 보물 상자 ─────────────

    // 보물 상자: o 테두리, W·w·v 나무(밝음·바탕·그늘), G·g·k 쇠띠(밝음·바탕·그늘), x 뚜껑 안쪽
    private static readonly Dictionary<char, uint> ChestWood = new()
        { ['o'] = 0xFF3A1E0C, ['W'] = 0xFFD08A48, ['w'] = 0xFFAA6630, ['v'] = 0xFF7A4420, ['x'] = 0xFF4E2A12 };
    private static readonly Dictionary<char, uint> ChestDarkWood = new()
        { ['o'] = 0xFF22120A, ['W'] = 0xFF9A6238, ['w'] = 0xFF7A4A28, ['v'] = 0xFF54301A, ['x'] = 0xFF2E180C };
    private static readonly Dictionary<char, uint> GoldTrim = new() { ['G'] = 0xFFFFE27A, ['g'] = 0xFFE0A42A, ['k'] = 0xFF8A5A10 };
    private static readonly Dictionary<char, uint> IronTrim = new() { ['G'] = 0xFFF0F4F8, ['g'] = 0xFFAEB6C2, ['k'] = 0xFF5A626E };
    // 보물: Y·C·c·d·n 금화(가장 밝음~테두리), E·e 보석
    private static readonly Dictionary<char, uint> Treasure = new()
        { ['Y'] = 0xFFFFFFE0, ['C'] = 0xFFFFF0A0, ['c'] = 0xFFF6C430, ['d'] = 0xFFD09018, ['n'] = 0xFFA06A0C, ['E'] = 0xFFFF9AB0, ['e'] = 0xFFE0204A };
    // 보석 상자: 사파이어 더미에 에메랄드
    private static readonly Dictionary<char, uint> Gems = new()
        { ['Y'] = 0xFFFFFFFF, ['C'] = 0xFFB8ECFF, ['c'] = 0xFF3AA8F0, ['d'] = 0xFF2A78C8, ['n'] = 0xFF16407E, ['E'] = 0xFFB0FFC0, ['e'] = 0xFF1EB050 };

    private static readonly string[] ChestClosed =
    [
        "..oooooooooooo..",
        ".oGgWWWWWWWWGgo.",
        "oWGgwwwwwwwwGgvo",
        "owGgwwwwwwwwGgvo",
        "ovGkvvvvvvvvGkvo",
        "ooooooGGgkoooooo",
        "oWGgWWGookWWGgvo",
        "owGgwwgkkkwwGgvo",
        "owGgwwwwwwwwGgvo",
        "ovGkvvvvvvvvGkvo",
        "owGgwwwwwwwwGgvo",
        "owGgwwwwwwwwGgvo",
        "ovGkvvvvvvvvGkvo",
        ".oooooooooooooo.",
    ];

    private static readonly string[] ChestSmall =
    [
        "..oooooooo..",
        ".oGWWWWWWGo.",
        "oWGwwwwwwgvo",
        "ovgvvvvvvkvo",
        "ooooGGgkoooo",
        "oWGWGookWgvo",
        "owGwgkkkwgvo",
        "owGwwwwwwgvo",
        "ovgvvvvvvkvo",
        "owGwwwwwwgvo",
        ".oooooooooo.",
    ];

    private static readonly string[] ChestOpen =
    [
        "..oooooooooooo..",
        ".oGgxxxxxxxxGgo.",
        "oxGgxxxxxxxxGgxo",
        "oxGgxxnnnnxxGgxo",
        "oxGgnnCCccnnnkxo",
        "oxnnCcccCcceEnxo",
        "onCcCccCccceecno",
        "ooooooGGgkoooooo",
        "oWGgWWGookWWGgvo",
        "owGgwwgkkkwwGgvo",
        "owGgwwwwwwwwGgvo",
        "ovGkvvvvvvvvGkvo",
        "owGgwwwwwwwwGgvo",
        "owGgwwwwwwwwGgvo",
        "ovGkvvvvvvvvGkvo",
        ".oooooooooooooo.",
    ];

    // 상자 밖으로 넘치는 금화 더미(아래 두 줄은 앞면으로 흘러내림)
    private static readonly string[] CoinHeap =
    [
        ".......nnn........",
        ".....nnYCcnn......",
        "....nCCcCccCnn....",
        "...nYCcCcCcccCn...",
        "..nCcCCcCcEEcCcn..",
        ".nCCcCcccCceeEcCn.",
        "nYCcCcCCcCccccCcCn",
        "nCcccCcCccCcCcccdn",
        ".nnCcnnnCcdnnnCdn.",
        "...nn...nn....nn..",
    ];

    // 바닥에 누운 금화
    private static readonly string[] FlatCoin = [".nnn.", "nYCcn", "ncddn", ".nnn."];

    // 자물쇠: o 테두리, P·p 놋쇠, K 열쇠 구멍
    private static readonly Dictionary<char, uint> Brass = new() { ['o'] = 0xFF3A2406, ['P'] = 0xFFFFE27A, ['p'] = 0xFFD8A030, ['K'] = 0xFF4A2E06 };
    private static readonly string[] Padlock = [".oooo.", ".o..o.", "oooooo", "oPPppo", "oPKKpo", "oppppo", ".oooo."];

    /// <summary>열린 상자 위로 반짝임 셋.</summary>
    private static PixelImage OpenChest(Dictionary<char, uint> wood, Dictionary<char, uint> trim, Dictionary<char, uint> loot) => Compose(21, 21,
    [
        (Art(ChestOpen, wood, trim, loot), 2, 5),
        (Art(BigStar, Sparkle), 0, 0),
        (Art(SmallStar, Sparkle), 16, 0),
        (Art(SmallStar, Sparkle), 9, 1),
    ]);

    /// <summary>금화가 넘쳐 앞면으로 흘러내리고 바닥에도 흩어진 열린 상자.</summary>
    private static PixelImage OverflowingChest(Dictionary<char, uint> wood, Dictionary<char, uint> trim) => Compose(26, 24,
    [
        (Art(BigStar, Sparkle), 0, 1),
        (Art(SmallStar, Sparkle), 21, 3),
        (Art(ChestOpen, wood, trim, Treasure), 5, 8),
        (Art(CoinHeap, Treasure), 4, 8),
        (Art(FlatCoin, Treasure), 0, 20),
        (Art(FlatCoin, Treasure), 2, 17),
        (Art(FlatCoin, Treasure), 21, 20),
        (Art(FlatCoin, Treasure), 9, 20),
    ]);

    /// <summary>보물 상자: 금띠·쇠띠·어두운 나무 상자, 작은 상자 둘, 자물쇠 채운 상자, 금화·보석이 든 열린 상자, 금화가 넘쳐 흘러내리는 상자 둘.</summary>
    public static StampSet TreasureChests()
    {
        var shapes = new List<PixelImage>
        {
            Art(ChestClosed, ChestWood, GoldTrim),
            Art(ChestClosed, ChestWood, IronTrim),
            Art(ChestClosed, ChestDarkWood, GoldTrim),
            Art(ChestSmall, ChestWood, GoldTrim),
            Art(ChestSmall, ChestDarkWood, IronTrim),
            Compose(16, 14, [(Art(ChestClosed, ChestDarkWood, IronTrim), 0, 0), (Art(Padlock, Brass), 5, 3)]),
            OpenChest(ChestWood, GoldTrim, Treasure),
            OpenChest(ChestDarkWood, IronTrim, Gems),
            OverflowingChest(ChestWood, GoldTrim),
            OverflowingChest(ChestDarkWood, GoldTrim),
        };
        return Set("Treasure Chests", shapes);
    }

    // ───────────── 4. 금화 더미(옆에서 본) ─────────────

    // 금화: n 테두리, Y 가장 밝음, C 윗면, c 바탕, d 옆테, z 옆테 그늘, x 자루 안 어둠
    private static readonly Dictionary<char, uint> Coins = new()
        { ['n'] = 0xFF7A4A06, ['Y'] = 0xFFFFF8C8, ['C'] = 0xFFFFE070, ['c'] = 0xFFF6C430, ['d'] = 0xFFC88A10, ['z'] = 0xFFA06A08, ['x'] = 0xFF3A2410 };

    /// <summary>옆에서 본 금화 한 닢(테두리 포함 4줄, 위 금화와 테두리 한 줄을 나눔): 밝은 윗면 줄, 어두운 옆테 줄.</summary>
    private static void CoinSlab(Grid g, int x, int y, int cw)
    {
        // 모서리는 비워 둥근 테(아래 금화 위에 얹혀도 칸이 갈라져 보임)
        g.Fill(x + 1, y, cw, 1, 'n');
        g.Fill(x + 1, y + 3, cw, 1, 'n');
        g.Fill(x, y + 1, 1, 2, 'n');
        g.Fill(x + cw + 1, y + 1, 1, 2, 'n');
        for (int i = 0; i < cw; i++)
        {
            g[x + 1 + i, y + 1] = i < 2 ? 'Y' : i == cw - 1 ? 'c' : 'C';
            g[x + 1 + i, y + 2] = i == 0 ? 'c' : i == cw - 1 || i % 3 == 2 ? 'z' : 'd';
        }
    }

    /// <summary>금화 탑: <paramref name="n"/>닢을 아래부터 쌓으며 <paramref name="lean"/>만큼 기울고 씨앗에 따라 가끔 한 칸씩 어긋남. <paramref name="bottom"/> = 맨 아래 테두리 줄.</summary>
    private static void CoinTower(Grid g, int x, int bottom, int n, int cw, double lean, uint seed)
    {
        var rng = new Lcg(seed);
        for (int i = 0; i < n; i++)
        {
            int jitter = i == 0 ? 0 : rng.Next(6) switch { 0 => -1, 1 => 1, _ => 0 };
            CoinSlab(g, x + (int)Math.Round(lean * i) + jitter, bottom - 3 - 3 * i, cw);
        }
    }

    // 세워 둔 금화(앞면)
    private static readonly string[] CoinFace = ["..nnn..", ".nYCCn.", "nYcccdn", "nCcYcdn", "nCcccdn", ".nddzn.", "..nnn.."];

    /// <summary>금화 탑들을 나란히(탑마다 닢 수·기울기·씨앗), 금화 폭 8.</summary>
    private static string[] Towers(params (int N, double Lean, uint Seed)[] towers)
    {
        var g = new Grid(10 * towers.Length + 8, 40);
        for (int t = 0; t < towers.Length; t++) CoinTower(g, 3 + t * 10, 39, towers[t].N, 8, towers[t].Lean, towers[t].Seed);
        return g.Rows();
    }

    /// <summary>낮은 금화 무더기: 층마다 금화를 반 닢쯤 엇갈려 쌓고, 세워 둔 금화를 앞에 기댐.</summary>
    private static string[] CoinPile(int[] layers, int faces, uint seed)
    {
        var rng = new Lcg(seed);
        var g = new Grid(48, 30);
        const int bottom = 29;
        for (int l = 0; l < layers.Length; l++)
        {
            int x0 = 2 + l * 4 + rng.Next(2);
            for (int i = 0; i < layers[l]; i++) CoinSlab(g, x0 + i * 8 + rng.Next(2), bottom - 3 - 3 * l, 7);
        }
        for (int f = 0; f < faces; f++) g.Stamp(CoinFace, f == 0 ? 0 : layers[0] * 8 - 2, bottom - 6);
        return g.Rows();
    }

    /// <summary>금화 더미: 옆에서 본 금화 탑(2~10닢, 기울거나 어긋난), 탑 두세 개 묶음, 낮은 무더기.</summary>
    public static StampSet CoinStacks()
    {
        var shapes = new List<PixelImage>
        {
            Art(Towers((2, 0, 1)), Coins),
            Art(Towers((4, 0, 2)), Coins),
            Art(Towers((6, 0.15, 3)), Coins),
            Art(Towers((9, -0.2, 4)), Coins),
            Art(Towers((10, 0, 5)), Coins),
            Art(Towers((3, 0, 6), (6, 0.1, 7)), Coins),
            Art(Towers((7, 0, 8), (4, 0, 9)), Coins),
            Art(Towers((2, 0, 10), (5, -0.1, 11), (3, 0, 12)), Coins),
            Art(Towers((5, 0, 13), (9, 0.1, 14), (6, 0, 15)), Coins),
            Art(CoinPile([3, 2, 1], 1, 16), Coins),
            Art(CoinPile([4, 3, 1], 2, 17), Coins),
        };
        return Set("Gold Coin Stacks", shapes);
    }

    // ───────────── 5. 돈 자루 ─────────────

    // 자루 몸통 글자는 통·자루 세트의 삼베(Burlap)와 같음(o·H·L·M·D, r·R 끈), 금화 글자는 Coins
    private static readonly Dictionary<char, uint> Leather = new()
    {
        ['o'] = 0xFF2E160A, ['H'] = 0xFFC88250, ['L'] = 0xFFA45E32, ['M'] = 0xFF824624, ['D'] = 0xFF5C2E16,
        ['r'] = 0xFFC8901C, ['R'] = 0xFFFFE070,
    };

    // 자루에 찍은 금화 무늬
    private static readonly string[] CoinMark = [".nnn.", "nYCcn", "nCccn", "ncddn", ".nnn."];

    private static readonly string[] MoneyBagLarge =
    [
        ".....o.o.o.....",
        "....oLoMoDo....",
        "....oLLMMDo....",
        ".....oLMDo.....",
        "....orRRrro....",
        "....ooLMDoo....",
        "...oHLMMMDDo...",
        "..oHLMMMMMMDo..",
        ".oHLMMMMMMMMDo.",
        "oHLLMMMMMMMMDDo",
        "oHLMMMMMMMMMDDo",
        "oLLMMMMMMMMMDDo",
        "oLMMMMMMMMMMDDo",
        "oLMMMMMMMMMDDDo",
        ".oMMMMMMMMMDDo.",
        "..oDMMMMMMDDo..",
        "...ooooooooo...",
    ];

    private static readonly string[] MoneyBagMedium =
    [
        "...o.o.o...",
        "...oLMDo...",
        "...orRro...",
        "...oLMDo...",
        "..oHLMMDo..",
        ".oHLMMMMDo.",
        "oHLMMMMMMDo",
        "oLLMMMMMMDo",
        "oLMMMMMMDDo",
        "oLMMMMMMDDo",
        ".oMMMMMMDo.",
        "..ooooooo..",
    ];

    private static readonly string[] MoneyBagSmall =
    [
        "..o.o.o..",
        "..oLMDo..",
        "..orRro..",
        "..oLMDo..",
        ".oHLMMDo.",
        "oHLMMMMDo",
        "oLMMMMMDo",
        "oLMMMMDDo",
        ".oMMMDDo.",
        "..ooooo..",
    ];

    // 입을 벌린 자루 위로 소복한 금화
    private static readonly string[] MoneyBagOpen =
    [
        "......nnn......",
        "....nnYCcnn....",
        "..nnCcCYcCcnn..",
        ".oLnCcCcCcCnDo.",
        ".oHLLLMMMMMDDo.",
        "..oLMMMMMMMDo..",
        ".oHLMMMMMMMMDo.",
        "oHLMMMMMMMMMDDo",
        "oLLMMMMMMMMMDDo",
        "oLMMMMMMMMMMDDo",
        "oLMMMMMMMMMDDDo",
        ".oMMMMMMMMMDDo.",
        "..ooooooooooo..",
    ];

    // 옆으로 쓰러져 입에서 금화가 쏟아지는 자루
    private static readonly string[] MoneyBagTipped =
    [
        "...oooooooo...........",
        ".ooHLLLLMMMoo.oo......",
        "oHLLMMMMMMMMDooLo.....",
        "oLLMMMMMMMMMDDoxLo....",
        "oLMMMMMMMMMMDDoxxDo...",
        "oLMMMMMMMMMMDDoxnnnn..",
        "oLMMMMMMMMMDDDonCYCcnn",
        ".oMMMMMMMMDDDnCcCcCcCn",
        "..oooooooooonCcCcCcCcn",
        ".........nnnnnnnnnnnn.",
    ];

    /// <summary>돈 자루: 삼베 자루(큰·중간·작은, 금화 무늬), 가죽 주머니 둘, 금화가 소복한 열린 자루, 주저앉은 자루, 쓰러져 금화를 쏟는 자루(좌우).</summary>
    public static StampSet CoinSacks()
    {
        var shapes = new List<PixelImage>
        {
            Compose(15, 17, [(Art(MoneyBagLarge, Burlap), 0, 0), (Art(CoinMark, Coins), 5, 9)]),
            Compose(11, 12, [(Art(MoneyBagMedium, Burlap), 0, 0), (Art(CoinMark, Coins), 3, 5)]),
            Art(MoneyBagSmall, Burlap),
            Compose(11, 12, [(Art(MoneyBagMedium, Leather), 0, 0), (Art(CoinMark, Coins), 3, 5)]),
            Art(MoneyBagSmall, Leather),
            Compose(19, 15, [(Art(MoneyBagOpen, Burlap, Coins), 2, 0), (Art(FlatCoin, Treasure), 0, 11), (Art(FlatCoin, Treasure), 14, 11)]),
            Compose(16, 12, [(Art(SackSlumped, Burlap), 0, 0), (Art(CoinMark, Coins), 5, 6)]),
        };
        shapes.AddRange(Mirrored([Art(MoneyBagTipped, Burlap, Coins)]));
        return Set("Coin Sacks", shapes);
    }

    // ───────────── 2. 종이 상자·나무 상자(옆모습·45도) ─────────────
    // 시점마다 따로 세트: 옆모습은 앞면만, 45도는 2:1 아이소메트릭(윗면·왼쪽 면·오른쪽 면). 한 세트 안에서는 시점이 섞이지 않는다.

    private enum Face { Top, Left, Right, Inner }

    /// <summary>
    /// 45도(2:1 아이소메트릭) 장면. X는 화면 오른쪽 아래, Y는 왼쪽 아래, Z는 위로: 화면 x = X − Y, y = (X + Y)/2 − Z.
    /// 사각형(모서리 P, 변 U·V)을 넣은 차례로 칠하고(뒤 → 앞), 칸마다 면 좌표(s = U 쪽 거리, t = V 쪽 거리)로 글자를 고른다.
    /// 다 칠한 뒤 바깥 둘레와 먼저 칠한 다른 물체(<c>part</c>)에 닿는 칸은 'o', 같은 물체의 먼저 칠한 면에 닿는 칸은 그 사각형의 모서리 글자(<c>rim</c>).
    /// 꼭짓점을 정수로 두면 칸 가운데가 모서리에 걸리지 않아 비스듬한 모서리가 늘 두 칸씩 내려간다.
    /// </summary>
    private sealed class Iso
    {
        public readonly record struct V(double X, double Y, double Z);

        private sealed record Quad(int Part, Face Face, V P, V U, V V, Func<double, double, char> Paint, Func<Face, char?>? Rim);

        private readonly List<Quad> _quads = [];

        public void Add(int part, Face face, V p, V u, V v, Func<double, double, char> paint, Func<Face, char?>? rim = null)
            => _quads.Add(new(part, face, p, u, v, paint, rim));

        /// <summary>상자 하나(바닥 모서리 <paramref name="o"/>, 크기 a×b×h): 오른쪽 면(s = 뒤에서 앞으로 Y) → 왼쪽 면(s = X) → 윗면(s = X, t = Y). 옆면의 t는 바닥에서 위로.</summary>
        public void Box(int part, V o, double a, double b, double h, Func<Face, double, double, char> paint, char? sideRim, char? topRim)
        {
            Add(part, Face.Right, new(o.X + a, o.Y, o.Z), new(0, b, 0), new(0, 0, h), (s, t) => paint(Face.Right, s, t));
            Add(part, Face.Left, new(o.X, o.Y + b, o.Z), new(a, 0, 0), new(0, 0, h), (s, t) => paint(Face.Left, s, t), f => f == Face.Right ? sideRim : topRim);
            Add(part, Face.Top, new(o.X, o.Y, o.Z + h), new(a, 0, 0), new(0, b, 0), (s, t) => paint(Face.Top, s, t), _ => topRim);
        }

        private static (double X, double Y) Screen(V p) => (p.X - p.Y, (p.X + p.Y) / 2 - p.Z);

        private static double Length(V v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

        public string[] Rows()
        {
            var corners = _quads.SelectMany(q => new[] { q.P, Add3(q.P, q.U), Add3(q.P, q.V), Add3(Add3(q.P, q.U), q.V) }).Select(Screen).ToList();
            int x0 = (int)Math.Floor(corners.Min(c => c.X)), y0 = (int)Math.Floor(corners.Min(c => c.Y));
            int w = (int)Math.Ceiling(corners.Max(c => c.X)) - x0, h = (int)Math.Ceiling(corners.Max(c => c.Y)) - y0;
            var owner = new int[w, h];
            var cell = new char[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    owner[x, y] = -1;
                    double cx = x0 + x + 0.5, cy = y0 + y + 0.5;
                    for (int i = 0; i < _quads.Count; i++)
                    {
                        var q = _quads[i];
                        var (px, py) = Screen(q.P);
                        var (ux, uy) = Screen(q.U);
                        var (vx, vy) = Screen(q.V);
                        double det = ux * vy - uy * vx;
                        if (Math.Abs(det) < 1e-9) continue;
                        double dx = cx - px, dy = cy - py, a = (dx * vy - dy * vx) / det, b = (ux * dy - uy * dx) / det;
                        if (a < 0 || a >= 1 || b < 0 || b >= 1) continue;
                        owner[x, y] = i;
                        cell[x, y] = q.Paint(a * Length(q.U), b * Length(q.V));
                    }
                }

            var g = new Grid(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = owner[x, y];
                    if (i < 0) continue;
                    var q = _quads[i];
                    char c = cell[x, y];
                    foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                    {
                        int n = nx >= 0 && ny >= 0 && nx < w && ny < h ? owner[nx, ny] : -1;
                        if (n < 0 || _quads[n].Part < q.Part) { c = 'o'; break; }
                        if (n < i && _quads[n].Part == q.Part && q.Rim?.Invoke(_quads[n].Face) is char rim) c = rim;
                    }
                    g[x, y] = c;
                }
            return g.Rows();
        }

        private static V Add3(V a, V b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    /// <summary>면 좌표 (s, t)에 놓은 글자 그림의 글자(그림 왼쪽 위 = (s0, tTop), t는 위로 갈수록 큼). 그림 밖이나 '.'이면 null.</summary>
    private static char? Decal(string[] art, double s, double t, double s0, double tTop)
    {
        int i = (int)Math.Floor(s - s0), j = (int)Math.Floor(tTop - t);
        return j >= 0 && j < art.Length && i >= 0 && i < art[j].Length && art[j][i] != '.' ? art[j][i] : null;
    }

    // 종이 상자: o 테두리, U·T 윗면(밝은 곳·바탕), F·f 왼쪽 면·옆모습 앞면(밝은 곳·바탕), s 오른쪽 면·그늘, S 오른쪽으로 열린 날개,
    // t·u 테이프(밝음·그늘), L·l 라벨(종이·글줄), a 화살표 인쇄, k 손잡이 구멍·상자 안 그늘진 벽, m 상자 안 밝은 벽, K 상자 바닥
    private static readonly Dictionary<char, uint> Cardboard = new()
    {
        ['o'] = 0xFF5A3816, ['U'] = 0xFFF2D29A, ['T'] = 0xFFE2B878, ['F'] = 0xFFD8A464, ['f'] = 0xFFC48C4C, ['s'] = 0xFFA06E36, ['S'] = 0xFFB27C40,
        ['t'] = 0xFFEEE2C0, ['u'] = 0xFFC8B48A, ['L'] = 0xFFF8F6F0, ['l'] = 0xFF8A8E96, ['a'] = 0xFF7A4E22,
        ['k'] = 0xFF6A4220, ['m'] = 0xFF8A5A2C, ['K'] = 0xFF4E3014,
    };
    private static readonly Dictionary<char, uint> FragileTape = new() { ['t'] = 0xFFF04A3A, ['u'] = 0xFFB82A22 };

    private static readonly string[] UpArrow = ["..a..", ".aaa.", "a.a.a", "..a..", "..a.."];
    // 45도 옆면용(면을 따라 비스듬히 밀려 그려지므로 좁게)
    private static readonly string[] SmallUpArrow = [".a.", "aaa", ".a.", ".a."];

    /// <summary>옆에서 본 종이 상자 판(앞면만): 왼쪽·위 밝은 가장자리, 오른쪽·아래 그늘.</summary>
    private static Grid SideBoxGrid(int w, int h)
    {
        var g = new Grid(w, h);
        g.Fill(0, 0, w, h, 'o');
        g.Fill(1, 1, w - 2, h - 2, 'f');
        g.Fill(1, 1, w - 2, 1, 'F');
        g.Fill(1, 1, 1, h - 2, 'F');
        g.Fill(w - 2, 1, 1, h - 2, 's');
        g.Fill(1, h - 2, w - 2, 1, 's');
        return g;
    }

    /// <summary>
    /// 옆에서 본 종이 상자: 윗면 이음매를 덮은 테이프가 앞면 가운데로 내려옴(<paramref name="tape"/>가 거짓이면 이음매 없는 옆판 — 손잡이 구멍을 뚫기 좋음).
    /// 라벨·화살표 인쇄·손잡이 구멍은 고르기.
    /// </summary>
    private static string[] SideBox(int w, int h, bool tape = true, bool label = false, bool arrows = false, bool handle = false)
    {
        var g = SideBoxGrid(w, h);
        int c = w / 2 - 1, tapeH = Math.Max(3, (h - 2) / 3);
        if (tape)
            for (int y = 1; y <= tapeH; y++) { g[c, y] = 't'; g[c + 1, y] = 'u'; }
        if (arrows) g.Stamp(UpArrow, 2, !tape || c >= 7 ? 3 : tapeH + 2);
        if (handle) g.Fill(c - 1, 3, 4, 1, 'k');
        if (label)
        {
            int lw = Math.Max(5, w / 3), lx = w - 2 - lw - 1, ly = h - 2 - 5;
            g.Fill(lx, ly, lw, 4, 'L');
            g.Fill(lx + 1, ly + 1, lw - 2, 1, 'l');
            g.Fill(lx + 1, ly + 2, lw - 3, 1, 'l');
        }
        return g.Rows();
    }

    /// <summary>
    /// 옆에서 본 열린 상자: 뒤 날개는 세워 안쪽 면이 보이고, 옆 날개 둘은 바깥 위로 비스듬히(옆에서 보여 얇은 판), 앞 날개는 앞으로 젖혀 앞면 위쪽에 늘어짐.
    /// </summary>
    private static string[] SideOpenBox(int w, int h)
    {
        const int pad = 4, rise = 7;
        var g = new Grid(w + 2 * pad, h + rise);
        var back = new Grid(w - 4, rise + 1);
        back.Fill(0, 0, back.W, back.H, 'o');
        back.Fill(1, 1, back.W - 2, rise - 1, 's');
        back.Fill(1, 1, back.W - 2, 1, 'f');
        g.Stamp(back.Rows(), pad + 2, 0);
        // 옆 날개: 경첩(상자 위 모서리 끝)에서 두 줄 오를 때마다 한 칸 바깥으로
        for (int k = 0; k < rise; k++)
        {
            int y = rise - k, shift = (k + 1) / 2;
            string left = k == rise - 1 ? "oooo" : "oFFo", right = k == rise - 1 ? "oooo" : "osso";
            g.Stamp([left], pad - shift, y);
            g.Stamp([right], pad + w - 4 + shift, y);
        }
        var body = SideBoxGrid(w, h);
        body.Fill(1, 1, w - 2, 3, 'T');
        body.Fill(1, 1, w - 2, 1, 'U');
        body.Fill(1, 4, w - 2, 1, 'o');
        g.Stamp(body.Rows(), pad, rise);
        return g.Rows();
    }

    /// <summary>옆모습 그림을 아래부터 쌓는다(층마다 (그림, x) 여럿 — 한 층의 그림은 높이가 같고, 위층은 아래층 윗줄 테두리와 겹침).</summary>
    private static string[] SideStack(params (string[] Art, int X)[][] layers)
    {
        int w = layers.SelectMany(l => l).Max(p => p.X + p.Art[0].Length);
        int h = layers.Sum(l => l[0].Art.Length) - (layers.Length - 1);
        var g = new Grid(w, h);
        int bottom = h;
        foreach (var layer in layers)
        {
            int top = bottom - layer[0].Art.Length;
            foreach (var (art, x) in layer) g.Stamp(art, x, top);
            bottom = top + 1;
        }
        return g.Rows();
    }

    /// <summary>
    /// 45도로 본 테이프 상자(a: 오른쪽 아래로 가는 변, b: 왼쪽 아래로 가는 변, h: 높이). 윗면 이음매를 덮은 테이프가 <paramref name="seamAlongX"/>면 오른쪽 면으로,
    /// 아니면 왼쪽 면으로 내려온다. 라벨·화살표는 왼쪽 면, 손잡이 구멍은 두 옆면.
    /// </summary>
    private static Func<Face, double, double, char> IsoCardboard(double a, double b, double h, bool seamAlongX = true, bool label = false, bool arrows = false, bool handles = false)
    {
        double tapeLow = h - Math.Max(3, Math.Round(h / 3));
        return (face, s, t) =>
        {
            switch (face)
            {
                case Face.Top:
                    return Math.Abs((seamAlongX ? t - b / 2 : s - a / 2)) < 1 ? 't' : 'T';
                case Face.Left:
                    if (!seamAlongX && Math.Abs(s - a / 2) < 1 && t >= tapeLow) return 't';
                    if (handles && Math.Abs(s - a / 2) < 2 && t >= h - 4 && t < h - 3) return 'k';
                    if (label && s >= a - 2 - Math.Max(6, Math.Round(a / 3)) && s < a - 2 && t >= 2 && t < 5)
                        return t >= 3 && t < 4 && s >= a - 1 - Math.Max(6, Math.Round(a / 3)) && s < a - 3 ? 'l' : 'L';
                    if (arrows && Decal(SmallUpArrow, s, t, 2, h - 2) is char arrow) return arrow;
                    return 'f';
                default:
                    if (seamAlongX && Math.Abs(s - b / 2) < 1 && t >= tapeLow) return 'u';
                    if (handles && Math.Abs(s - b / 2) < 2 && t >= h - 4 && t < h - 3) return 'k';
                    return 's';
            }
        };
    }

    private static string[] IsoBox(double a, double b, double h, bool seamAlongX = true, bool label = false, bool arrows = false, bool handles = false)
    {
        var scene = new Iso();
        scene.Box(0, new(0, 0, 0), a, b, h, IsoCardboard(a, b, h, seamAlongX, label, arrows, handles), 'F', 'U');
        return scene.Rows();
    }

    /// <summary>45도로 본 열린 상자: 안쪽 벽과 바닥이 보이고, 뒤 날개 둘은 바깥으로 젖혀 세우고, 앞 날개 둘은 바깥으로 거의 눕혀 펼침.</summary>
    private static string[] IsoOpenBox(int a, int b, int h)
    {
        var scene = new Iso();
        scene.Add(0, Face.Inner, new(0, 0, h), new(a, 0, 0), new(0, -3, 5), (_, t) => t < 1 ? 'k' : 'F');
        scene.Add(1, Face.Inner, new(0, 0, h), new(0, b, 0), new(-3, 0, 5), (_, t) => t < 1 ? 'k' : 's');
        scene.Add(2, Face.Inner, new(0, 0, 0), new(a, 0, 0), new(0, 0, h), (_, _) => 'm');
        scene.Add(2, Face.Inner, new(0, 0, 0), new(0, b, 0), new(0, 0, h), (_, _) => 'k');
        scene.Add(2, Face.Inner, new(0, 0, 0), new(a, 0, 0), new(0, b, 0), (_, _) => 'K');
        scene.Add(2, Face.Right, new(a, 0, 0), new(0, b, 0), new(0, 0, h), (_, _) => 's', _ => 'U');
        scene.Add(2, Face.Left, new(0, b, 0), new(a, 0, 0), new(0, 0, h), (_, _) => 'f', f => f == Face.Right ? 'F' : 'U');
        scene.Add(3, Face.Top, new(a, 0, h), new(0, b, 0), new(5, 0, -2), (_, _) => 'S');
        scene.Add(4, Face.Top, new(0, b, h), new(a, 0, 0), new(0, 5, -2), (_, _) => 'T');
        return scene.Rows();
    }

    /// <summary>45도로 쌓은 테이프 상자들(뒤에서 앞 차례로 (바닥 모서리, 크기)).</summary>
    private static string[] IsoStack(params (Iso.V At, double A, double B, double H)[] boxes)
    {
        var scene = new Iso();
        for (int i = 0; i < boxes.Length; i++)
        {
            var (at, a, b, h) = boxes[i];
            scene.Box(i, at, a, b, h, IsoCardboard(a, b, h), 'F', 'U');
        }
        return scene.Rows();
    }

    /// <summary>종이 상자(옆모습): 테이프 상자(작은·중간·큰·세로), 손잡이 구멍 난 옆판, 깨짐 주의 상자, 열린 상자, 둘·셋 쌓은 더미.</summary>
    public static StampSet CardboardBoxesSide()
    {
        var shapes = new List<PixelImage>
        {
            Art(SideBox(12, 10), Cardboard),
            Art(SideBox(16, 13, arrows: true), Cardboard),
            Art(SideBox(22, 17, label: true, arrows: true), Cardboard),
            Art(SideBox(11, 17, label: true), Cardboard),
            Art(SideBox(24, 11, tape: false, handle: true), Cardboard),
            Art(SideBox(14, 12, arrows: true), FragileTape, Cardboard),
            Art(SideOpenBox(16, 12), Cardboard),
            Art(SideStack([(SideBox(18, 14, label: true), 0)], [(SideBox(11, 9), 1)]), Cardboard),
            Art(SideStack([(SideBox(18, 14, label: true), 0)], [(SideBox(11, 9), 6)]), Cardboard),
            Art(SideStack([(SideBox(13, 12), 0), (SideBox(13, 12, arrows: true), 12)], [(SideBox(14, 10, tape: false, handle: true), 5)]), Cardboard),
        };
        return Set("Cardboard Boxes (Side)", shapes);
    }

    /// <summary>종이 상자(45도): 테이프 상자(작은·중간·큰·세로·납작한, 테이프 방향 둘), 깨짐 주의 상자, 열린 상자, 둘·셋 쌓은 더미.</summary>
    public static StampSet CardboardBoxesIsometric()
    {
        var fragile = new Iso();
        fragile.Box(0, new(0, 0, 0), 12, 12, 10, IsoCardboard(12, 12, 10, arrows: true), 'F', 'U');
        var shapes = new List<PixelImage>
        {
            Art(IsoBox(10, 8, 8), Cardboard),
            Art(IsoBox(14, 10, 11, arrows: true), Cardboard),
            Art(IsoBox(18, 14, 14, label: true, arrows: true), Cardboard),
            Art(IsoBox(10, 10, 17, label: true), Cardboard),
            Art(IsoBox(20, 12, 7, handles: true), Cardboard),
            Art(IsoBox(10, 14, 10, seamAlongX: false), Cardboard),
            Art(fragile.Rows(), FragileTape, Cardboard),
            Art(IsoOpenBox(14, 12, 10), Cardboard),
            Art(IsoStack((new(0, 0, 0), 16, 12, 11), (new(3, 2, 11), 9, 8, 8)), Cardboard),
            Art(IsoStack((new(0, 0, 0), 12, 10, 10), (new(2, 2, 10), 8, 7, 7), (new(0, 11, 0), 11, 10, 9)), Cardboard),
        };
        return Set("Cardboard Boxes (Isometric)", shapes);
    }

    // 나무 상자: o 테두리, P·p·q 테두리 널(밝음·바탕·그늘 — 옆모습과 45도 왼쪽 면), b·g 판자·판자 틈(옆모습·왼쪽 면), n 못,
    // U·T·G 45도 윗면(밝은 곳·판자·틈), r·c·h 45도 오른쪽 면(테두리 널·판자·틈)
    private static readonly Dictionary<char, uint> CrateLight = new()
    {
        ['o'] = 0xFF3E2410, ['P'] = 0xFFE8B472, ['p'] = 0xFFC8904E, ['q'] = 0xFF9A6430, ['b'] = 0xFFAE743A, ['g'] = 0xFF5A3418, ['n'] = 0xFF4A4A52,
        ['U'] = 0xFFF2CE8E, ['T'] = 0xFFDCA866, ['G'] = 0xFF8A5628, ['r'] = 0xFFA87038, ['c'] = 0xFF8A5828, ['h'] = 0xFF5E3618,
    };
    private static readonly Dictionary<char, uint> CrateDark = new()
    {
        ['o'] = 0xFF2A160A, ['P'] = 0xFFAE7A48, ['p'] = 0xFF8E6036, ['q'] = 0xFF6A4422, ['b'] = 0xFF7C5028, ['g'] = 0xFF3A2010, ['n'] = 0xFF2E2E34,
        ['U'] = 0xFFC49060, ['T'] = 0xFFA87548, ['G'] = 0xFF5A361A, ['r'] = 0xFF7A5030, ['c'] = 0xFF5E3A1C, ['h'] = 0xFF2E1A0A,
    };

    /// <summary>옆에서 본 나무 상자(앞면만): 테두리 널, 가로 판자, 왼쪽 아래 → 오른쪽 위 버팀목, 네 귀 못.</summary>
    private static string[] SideCrate(int w, int h)
    {
        var g = new Grid(w, h);
        g.Fill(0, 0, w, h, 'o');
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
                g[x, y] = (y - 1) % 3 == 2 ? 'g' : 'b';
        for (int x = 1; x < w - 1; x++) { g[x, 1] = x < w - 2 ? 'P' : 'p'; g[x, 2] = 'p'; g[x, h - 3] = 'p'; g[x, h - 2] = 'q'; }
        for (int y = 1; y < h - 1; y++) { g[1, y] = 'P'; g[2, y] = 'p'; g[w - 3, y] = 'p'; g[w - 2, y] = 'q'; }
        int iw = w - 6, ih = h - 6;
        for (int i = 0; i <= iw; i++)
        {
            int x = 3 + i, y = h - 4 - (int)Math.Round(i * (ih - 1) / (double)iw);
            g[x, y] = 'p';
            if (g[x, y - 1] != 'p') g[x, y - 1] = 'P';
            if (g[x, y + 1] != 'P') g[x, y + 1] = 'q';
        }
        g[2, 2] = g[w - 3, 2] = g[2, h - 3] = g[w - 3, h - 3] = 'n';
        return g.Rows();
    }

    /// <summary>옆에서 본 나무 팔레트: 윗판, 받침 토막 셋(사이는 비어 있음), 아랫판.</summary>
    private static string[] SidePallet(int w)
    {
        var g = new Grid(w, 9);
        g.Fill(0, 0, w, 4, 'o');
        g.Fill(1, 1, w - 2, 2, 'p');
        g.Fill(1, 1, w - 2, 1, 'P');
        foreach (int bx in new[] { 0, (w - 5) / 2, w - 5 })
        {
            g.Fill(bx, 3, 5, 4, 'o');
            g.Fill(bx + 1, 3, 3, 3, 'b');
            g[bx + 1, 3] = 'p';
        }
        g.Fill(0, 6, w, 3, 'o');
        g.Fill(1, 7, w - 2, 1, 'q');
        g[1, 7] = 'p';
        return g.Rows();
    }

    /// <summary>45도 나무 상자의 면: 윗면은 X 쪽으로 놓은 판자, 옆면은 테두리 널·가로 판자·대각선 버팀목.</summary>
    private static Func<Face, double, double, char> IsoCrate(double a, double b, double h) => (face, s, t) =>
    {
        if (face == Face.Top) return (int)Math.Floor(t) % 4 == 3 ? 'G' : 'T';
        bool left = face == Face.Left;
        double len = left ? a : b;
        char frame = left ? 'P' : 'r', plank = left ? 'b' : 'c', gap = left ? 'g' : 'h';
        int si = (int)Math.Floor(s), ti = (int)Math.Floor(t);
        if ((si == 1 || si == (int)len - 2) && (ti == 1 || ti == (int)h - 2)) return 'n';
        if (s < 2 || s >= len - 2 || t < 2 || t >= h - 2) return frame;
        double brace = 2 + (s - 2) * (h - 4) / (len - 4);
        if (Math.Abs(t - brace) < 1.1) return frame;
        return (ti - 2) % 4 == 3 ? gap : plank;
    };

    /// <summary>45도 나무 팔레트의 면(높이 <paramref name="h"/>): 윗면은 Y 쪽 널 사이 틈, 옆면은 윗판 모서리 두 줄·받침 토막 셋(사이는 그늘진 빈 곳)·아랫판 한 줄.</summary>
    private static Func<Face, double, double, char> IsoPallet(double a, double b, double h) => (face, s, t) =>
    {
        if (face == Face.Top) return (int)Math.Floor(s) % 4 == 3 ? 'G' : 'T';
        bool left = face == Face.Left;
        double len = left ? a : b;
        char board = left ? 'P' : 'r', opening = left ? 'g' : 'o';
        if (t >= h - 2 || t < 1) return board;
        bool isBlock = s < 3 || s >= len - 3 || Math.Abs(s - len / 2) < 1.5;
        return isBlock ? board : opening;
    };

    /// <summary>45도로 놓은 나무 상자·팔레트들(뒤에서 앞 차례로 (바닥 모서리, 크기, 팔레트인가)).</summary>
    private static string[] IsoCrates(params (Iso.V At, double A, double B, double H, bool Pallet)[] items)
    {
        var scene = new Iso();
        for (int i = 0; i < items.Length; i++)
        {
            var (at, a, b, h, pallet) = items[i];
            scene.Box(i, at, a, b, h, pallet ? IsoPallet(a, b, h) : IsoCrate(a, b, h), null, 'U');
        }
        return scene.Rows();
    }

    /// <summary>나무 상자(옆모습): 크기가 다른 밝은·어두운 나무 상자, 쌓은 상자, 팔레트, 팔레트에 올린 상자.</summary>
    public static StampSet WoodenCratesSide()
    {
        var shapes = new List<PixelImage>
        {
            Art(SideCrate(12, 11), CrateLight),
            Art(SideCrate(18, 15), CrateLight),
            Art(SideCrate(22, 12), CrateLight),
            Art(SideCrate(15, 13), CrateDark),
            Art(SideCrate(12, 17), CrateDark),
            Art(SideStack([(SideCrate(18, 15), 0)], [(SideCrate(12, 11), 3)]), CrateLight),
            Art(SidePallet(26), CrateLight),
            Art(SideStack([(SidePallet(26), 0)], [(SideCrate(18, 15), 4)]), CrateLight),
        };
        return Set("Wooden Crates (Side)", shapes);
    }

    /// <summary>나무 상자(45도): 크기가 다른 밝은·어두운 나무 상자, 쌓은 상자, 팔레트, 팔레트에 올린 상자.</summary>
    public static StampSet WoodenCratesIsometric()
    {
        var shapes = new List<PixelImage>
        {
            Art(IsoCrates((new(0, 0, 0), 10, 10, 10, false)), CrateLight),
            Art(IsoCrates((new(0, 0, 0), 16, 12, 13, false)), CrateLight),
            Art(IsoCrates((new(0, 0, 0), 20, 10, 10, false)), CrateLight),
            Art(IsoCrates((new(0, 0, 0), 12, 12, 12, false)), CrateDark),
            Art(IsoCrates((new(0, 0, 0), 10, 10, 16, false)), CrateDark),
            Art(IsoCrates((new(0, 0, 0), 16, 12, 13, false), (new(3, 2, 13), 10, 8, 9, false)), CrateLight),
            Art(IsoCrates((new(0, 0, 0), 20, 16, 6, true)), CrateLight),
            Art(IsoCrates((new(0, 0, 0), 20, 16, 6, true), (new(2, 2, 6), 16, 12, 13, false)), CrateLight),
        };
        return Set("Wooden Crates (Isometric)", shapes);
    }

    // ───────────── 3. 쓰레기 ─────────────

    // 찌그러진 캔: o 테두리, H·L·M·D 몸통(밝은 곳~그늘), w 글씨 띠, S·s 은색 뚜껑
    private static readonly string[] Can =
    [
        ".oooooooo.ooo.",
        "oSsoHHHHLoHHLo",
        "oSsoLwwwwDwwMo",
        "oSsoLwwwwwDwMo",
        "osSoMMMMMMDMDo",
        "ossoDDDDDDDDDo",
        ".oooooooooooo.",
    ];

    private static Dictionary<char, uint> CanColor(uint o, uint h, uint l, uint m, uint d, uint w) => new()
        { ['o'] = o, ['H'] = h, ['L'] = l, ['M'] = m, ['D'] = d, ['w'] = w, ['S'] = 0xFFE8ECF0, ['s'] = 0xFFA0A8B4 };

    // 플라스틱 병(누운): H·L·M·D 몸통, b·B 라벨, c·C 뚜껑
    private static readonly string[] Bottle =
    [
        ".oooooooooo......",
        "oHHbbbbHHHHoo....",
        "oLLbbbbLLLLLLoocc",
        "oMMBBBBMMMMMMMMcC",
        "oDDBBBBDDDDDDooCC",
        "oDDBBBBDDDDoo....",
        ".oooooooooo......",
    ];

    private static readonly Dictionary<char, uint> ClearPlastic = new()
    {
        ['o'] = 0xFF5A7C94, ['H'] = 0xD0FFFFFF, ['L'] = 0x80D8F0FF, ['M'] = 0x70A8D4F0, ['D'] = 0x9080B0D0,
        ['b'] = 0xFF3A8AE0, ['B'] = 0xFF2460B0, ['c'] = 0xFF3A8AE0, ['C'] = 0xFF1E4E96,
    };

    // 초록 유리병(누운, 목이 긴)
    private static readonly string[] GlassBottle =
    [
        ".ooooooooo........",
        "oHHHHHHHHHoo......",
        "oLLyyyyLLLLLooooo.",
        "oMMyYYyMMMMMMMMMco",
        "oDDYYYYDDDDDooooo.",
        "oDDDDDDDDDoo......",
        ".ooooooooo........",
    ];

    private static readonly Dictionary<char, uint> GreenGlass = new()
    {
        ['o'] = 0xFF1A4A22, ['H'] = 0xD0D8FFD8, ['L'] = 0xA05AC86A, ['M'] = 0xA03A9A48, ['D'] = 0xC0246E30,
        ['y'] = 0xFFF0E2B0, ['Y'] = 0xFFC8B880, ['c'] = 0xFFC8A040,
    };

    // 구긴 종이: o 테두리, W 흰, w 바탕, g 주름, d 그늘
    private static readonly string[] PaperBall =
    [
        "..ooo..",
        ".oWWwo.",
        "oWwWggo",
        "oWgwWgo",
        "owWggdo",
        ".ogwdo.",
        "..ooo..",
    ];

    private static readonly string[] PaperBallBig =
    [
        "...oooo..",
        ".ooWWwoo.",
        "oWWwgWwgo",
        "oWgWWwggo",
        "owwgWggdo",
        "oWggwwgdo",
        ".ogwgddo.",
        "..ooooo..",
    ];

    private static readonly Dictionary<char, uint> WhitePaper = new()
        { ['o'] = 0xFF6E7682, ['W'] = 0xFFFFFFFF, ['w'] = 0xFFE6E9EE, ['g'] = 0xFFBEC6D0, ['d'] = 0xFF98A2AE };
    private static readonly Dictionary<char, uint> YellowPaper = new()
        { ['o'] = 0xFF8A7430, ['W'] = 0xFFFFF8C8, ['w'] = 0xFFF6E6A0, ['g'] = 0xFFD8C478, ['d'] = 0xFFB8A458 };

    // 바나나 껍질: o 테두리, Y·y·d 노랑(밝음·바탕·그늘), W 속껍질, b 갈색 점, k 꼭지
    private static readonly string[] BananaPeel =
    [
        "......kk......",
        "......ok......",
        ".....oYdo.....",
        ".....oYyo.....",
        "....oYyydo....",
        "...oYyWWydo...",
        "..oYyoWWoydo..",
        ".oYyo.oWo.oydo",
        "oYyo..oWo..odo",
        "obo...obo...oo",
        ".o.....o......",
    ];

    private static readonly Dictionary<char, uint> Banana = new()
        { ['o'] = 0xFF6A4A0A, ['Y'] = 0xFFFFE466, ['y'] = 0xFFF0C228, ['d'] = 0xFFB88A12, ['W'] = 0xFFFBF2C8, ['b'] = 0xFF7A5418, ['k'] = 0xFF3A2A10 };

    // 사과 속: o 테두리, R·r·d 껍질, W·w 과육, s 씨, k 꼭지
    private static readonly string[] AppleCore =
    [
        "...k...",
        ".oRkro.",
        "oRrrrdo",
        ".oWwwo.",
        "..oWso.",
        "..oWwo.",
        "..oWso.",
        ".oWwwdo",
        "oRrrrdo",
        ".odddo.",
    ];

    private static readonly Dictionary<char, uint> Apple = new()
        { ['o'] = 0xFF5A1010, ['R'] = 0xFFF06058, ['r'] = 0xFFD02A2A, ['d'] = 0xFF8E1818, ['W'] = 0xFFFFF4D8, ['w'] = 0xFFEAD8A8, ['s'] = 0xFF4A2A10, ['k'] = 0xFF5A3A14 };

    // 과자 봉지: o 테두리, S·s 접힌 끝(은색), H·r·d 봉지, Y·y 라벨
    private static readonly string[] ChipBag =
    [
        "..oooooooooo",
        ".oSsSsSsSsSo",
        ".oHrrrrrrrdo",
        "oHrYYYYYyrdo",
        "oHrYyyyyyrdo",
        "orrrYyyyrrdo",
        "orrrrrrrrddo",
        "orrrrrrrrdo.",
        "oSsSsSsSsSo.",
        "oooooooooo..",
    ];

    private static readonly Dictionary<char, uint> ChipsRed = new()
        { ['o'] = 0xFF5E0C12, ['S'] = 0xFFF0F2F6, ['s'] = 0xFFAAB2BE, ['H'] = 0xFFFF7A70, ['r'] = 0xFFE0303A, ['d'] = 0xFFA01C26, ['Y'] = 0xFFFFE470, ['y'] = 0xFFF0B020 };
    private static readonly Dictionary<char, uint> ChipsBlue = new()
        { ['o'] = 0xFF0C2458, ['S'] = 0xFFF0F2F6, ['s'] = 0xFFAAB2BE, ['H'] = 0xFF7AB8FF, ['r'] = 0xFF2A70D8, ['d'] = 0xFF1A4A9A, ['Y'] = 0xFFFFE470, ['y'] = 0xFFF0B020 };

    // 비닐봉지: o 테두리, W·w·g·d 흰 비닐
    private static readonly string[] PlasticBag =
    [
        "..ooo..ooo..",
        ".o...oo...o.",
        ".o..oWWo..o.",
        "..ooWwwWoo..",
        ".oWWwwwwwgo.",
        ".oWwwwwwwgo.",
        "oWrRRrrrrrgo",
        "oWwwrrrwwggo",
        "owwgwwwwgwdo",
        ".odwgddgddo.",
        "..oo.oo.oo..",
    ];

    private static readonly Dictionary<char, uint> BagWhite = new()
        { ['o'] = 0xFF7C8490, ['W'] = 0xFFFFFFFF, ['w'] = 0xFFEEF0F4, ['g'] = 0xFFCDD3DC, ['d'] = 0xFFAAB2BE, ['r'] = 0xFFE03A3A, ['R'] = 0xFFFF8A80 };

    /// <summary>땅에 펼쳐진 신문지(비스듬한 네모, 제목 줄·사진·글줄·접힌 금).</summary>
    private static string[] Newspaper()
    {
        const int w = 20, h = 11;
        var g = new Grid(w, h);
        for (int y = 0; y < h; y++)
        {
            int x0 = (h - 1 - y) / 3, x1 = w - 1 - y / 3 - 1;
            for (int x = x0; x <= x1; x++)
            {
                bool edge = y == 0 || y == h - 1 || x == x0 || x == x1;
                int col = x - x0;
                char c = edge ? 'o' : y == 1 ? 'W' : 'w';
                if (!edge && y == 2 && col >= 2 && col <= x1 - x0 - 2) c = 'k';
                else if (!edge && y >= 4 && y <= 7 && col >= 2 && col <= 6) c = y == 4 ? 'P' : 'p';
                else if (!edge && y >= 4 && y % 2 == 0 && col >= 8 && col <= x1 - x0 - 2 && col != (x1 - x0) / 2 + 3) c = 'g';
                else if (!edge && y == 8 && col >= 2 && col <= 6) c = 'g';
                if (!edge && col == (x1 - x0) / 2 + 3) c = 'f';
                g[x, y] = c;
            }
        }
        return g.Rows();
    }

    private static readonly Dictionary<char, uint> NewsPaper = new()
        { ['o'] = 0xFF6A6E76, ['W'] = 0xFFF8F6EE, ['w'] = 0xFFE8E4D8, ['k'] = 0xFF3A3C42, ['g'] = 0xFFA4A6AC, ['P'] = 0xFFA8B4C4, ['p'] = 0xFF7C8A9E, ['f'] = 0xFFC8C4B6 };

    // 꽁초 둘: F·f 필터, W·w 종이, A 재
    private static readonly string[] Butts =
    [
        ".ooooo....",
        "oFfWwAo...",
        ".ooooo....",
        ".....oo...",
        "....oFfo..",
        "...oFfWo..",
        "....oWwoo.",
        ".....owAo.",
        "......oo..",
    ];

    private static readonly Dictionary<char, uint> Cigarette = new()
        { ['o'] = 0xFF6A5640, ['F'] = 0xFFF0B060, ['f'] = 0xFFC88A3A, ['W'] = 0xFFFAFAF6, ['w'] = 0xFFD8DADE, ['A'] = 0xFF6E6E72 };

    // 병뚜껑 셋: 톱니 테두리, 가운데 빛
    private static readonly string[] BottleCaps =
    [
        ".ooo.........",
        "oRRro...ooo..",
        "oRrdo..oSSso.",
        "orddo..oSsso.",
        ".ooo...ossso.",
        "........ooo..",
        "..ooo........",
        ".oBBbo.......",
        ".oBbbo.......",
        ".obbbo.......",
        "..ooo........",
    ];

    private static readonly Dictionary<char, uint> Caps = new()
        { ['o'] = 0xFF3A3C44, ['R'] = 0xFFFF6A5A, ['r'] = 0xFFD8282E, ['d'] = 0xFF9A1820, ['S'] = 0xFFF0F2F6, ['s'] = 0xFFA8B0BC, ['B'] = 0xFF6AB0FF, ['b'] = 0xFF2A6AD0 };

    // 테이크아웃 컵: W·w·g 컵, L·l 뚜껑, S·s·t 홀더
    private static readonly string[] Cup =
    [
        "...ooo...",
        ".ooLLloo.",
        "oLLLLLllo",
        "ooooooooo",
        "oWWwwwwgo",
        ".oSsssto.",
        ".oSsssto.",
        ".oSsssto.",
        ".oWwwwgo.",
        "..oWwgo..",
        "..oWwgo..",
        "..ooooo..",
    ];

    // 쓰러진 컵과 흘린 커피(반투명)
    private static readonly string[] CupTipped =
    [
        ".....ooooooooo",
        ".ooooSSSWWWoLo",
        "oWWWSSSSWWWoLo",
        "owwwsssswwwoLo",
        "owwwsssswwwoLo",
        "ogggttttgggoLo",
        ".oooottttggolo",
        ".....ooooooooo",
    ];

    private static readonly Dictionary<char, uint> CupColors = new()
    {
        ['o'] = 0xFF5A4A40, ['W'] = 0xFFFFFFFF, ['w'] = 0xFFEAE6E0, ['g'] = 0xFFC2BAB0, ['L'] = 0xFFFFFFFF, ['l'] = 0xFFC8C2BA,
        ['S'] = 0xFFC88E5A, ['s'] = 0xFFAA7040, ['t'] = 0xFF7E4E2A,
    };

    private static readonly string[] Spill =
    [
        "...ccccc...",
        ".cCCccccccc",
        "cCcccccccc.",
        "ccccccccc..",
        ".ccccccc...",
        "...ccc.....",
    ];

    private static readonly Dictionary<char, uint> Coffee = new() { ['c'] = 0xA05A3214, ['C'] = 0xB08A5A34 };

    /// <summary>쓰레기: 찌그러진 캔 셋, 페트병·유리병, 구긴 종이 둘, 바나나 껍질, 사과 속, 과자 봉지 둘, 비닐봉지, 신문지, 꽁초, 병뚜껑, 컵 둘(좌우 뒤집은 것까지).</summary>
    public static StampSet Trash()
    {
        var flip = new List<PixelImage>
        {
            Art(Can, CanColor(0xFF5A0A10, 0xFFFF8078, 0xFFF0444A, 0xFFC82830, 0xFF8A1820, 0xFFFFFFFF)),
            Art(Can, CanColor(0xFF0C2458, 0xFF8AC4FF, 0xFF3C8CF0, 0xFF2266C8, 0xFF16448C, 0xFFE8F2FF)),
            Art(Can, CanColor(0xFF0E3E1A, 0xFF9AF0A0, 0xFF44C860, 0xFF28A044, 0xFF1A6E30, 0xFFFFF0A0)),
            Art(Bottle, ClearPlastic),
            Art(GlassBottle, GreenGlass),
            Art(BananaPeel, Banana),
            Art(ChipBag, ChipsRed),
            Art(ChipBag, ChipsBlue),
            Compose(22, 10, [(Art(Spill, Coffee), 11, 4), (Art(CupTipped, CupColors), 0, 0)]),
            Art(Butts, Cigarette),
        };
        var shapes = new List<PixelImage>(Mirrored(flip))
        {
            Art(PaperBall, WhitePaper),
            Art(PaperBallBig, WhitePaper),
            Art(PaperBallBig, YellowPaper),
            Art(AppleCore, Apple),
            Art(PlasticBag, BagWhite),
            Art(Newspaper(), NewsPaper),
            Art(BottleCaps, Caps),
            Art(Cup, CupColors),
        };
        return Set("Litter & Trash", shapes);
    }

    // ───────────── 4. 라바콘·바리케이드 ─────────────

    // 주황: o 테두리, C 밝음, c 바탕, d 그늘, w·W 반사띠, B·b·k 검은 받침
    private static readonly Dictionary<char, uint> ConeColors = new()
    {
        ['o'] = 0xFF7A2A06, ['C'] = 0xFFFFA858, ['c'] = 0xFFFF7A1A, ['d'] = 0xFFC84A10, ['w'] = 0xFFF8F8F4, ['W'] = 0xFFC8CCD2,
        ['B'] = 0xFF4A4E56, ['b'] = 0xFF2A2C30, ['k'] = 0xFF16181C,
    };

    private static readonly string[] ConeLarge =
    [
        ".....ooo.....",
        ".....oCo.....",
        "....oCcdo....",
        "....oCcdo....",
        "...owwwwWo...",
        "...owwwwWo...",
        "...oCcccdo...",
        "..oCccccddo..",
        "..oCccccddo..",
        "..owwwwwwWo..",
        ".owwwwwwwWWo.",
        ".oCccccccddo.",
        ".oCccccccddo.",
        "kBBBBBBBBBBBk",
        "kbbbbbbbbbbbk",
        ".kkkkkkkkkkk.",
    ];

    private static readonly string[] ConeMedium =
    [
        "...ooo...",
        "...oCo...",
        "..oCcdo..",
        "..owwWo..",
        "..owwWo..",
        ".oCccddo.",
        ".oCccddo.",
        ".owwwwWo.",
        "oCcccccdo",
        "kBBBBBBBk",
        "kbbbbbbbk",
        ".kkkkkkk.",
    ];

    private static readonly string[] ConeSmall =
    [
        "..ooo..",
        "..oCo..",
        "..owo..",
        ".oCcdo.",
        ".owwWo.",
        ".oCcdo.",
        "oCcccdo",
        "kBBBBBk",
        ".kkkkk.",
    ];

    /// <summary>옆으로 쓰러진 라바콘(받침이 왼쪽, 끝이 오른쪽).</summary>
    private static string[] ConeTipped()
    {
        const int w = 17, h = 11;
        var g = new Grid(w, h);
        // 받침(옆에서 본 납작한 판)
        g.Fill(0, 0, 3, h, 'k');
        g.Fill(1, 1, 1, h - 2, 'B');
        double cy = (h - 1) / 2.0;
        for (int x = 3; x < w; x++)
        {
            double hh = 4.4 * (1 - (x - 3) / (double)(w - 3)) + 0.6;
            for (int y = 0; y < h; y++)
            {
                double v = (y - cy) / hh;
                if (Math.Abs(y - cy) > hh) continue;
                bool edge = Math.Abs(y - 1 - cy) > hh || Math.Abs(y + 1 - cy) > hh || x == w - 1;
                bool band = (x >= 6 && x <= 7) || (x >= 10 && x <= 11);
                g[x, y] = edge ? 'o' : band ? (v < 0.3 ? 'w' : 'W') : v < -0.35 ? 'C' : v < 0.35 ? 'c' : 'd';
            }
        }
        return g.Rows();
    }

    // 바리케이드: 판 줄무늬(R·r·d 빨강/노랑, P·w·v 흰/검정), m·n 다리, k 발
    private static readonly Dictionary<char, uint> BarricadeRed = new()
    {
        ['o'] = 0xFF4E1010, ['R'] = 0xFFFF6A5E, ['r'] = 0xFFE02A2A, ['d'] = 0xFFA81C1E, ['P'] = 0xFFFFFFFF, ['w'] = 0xFFEEF0F2, ['v'] = 0xFFC2C8D0,
        ['m'] = 0xFFE4E8EE, ['n'] = 0xFFA4ACB8, ['k'] = 0xFF2A2C30,
    };
    private static readonly Dictionary<char, uint> BarricadeYellow = new()
    {
        ['o'] = 0xFF2A2410, ['R'] = 0xFFFFEA70, ['r'] = 0xFFF8C820, ['d'] = 0xFFC09410, ['P'] = 0xFF4A4A50, ['w'] = 0xFF2E2E34, ['v'] = 0xFF1E1E22,
        ['m'] = 0xFFE4E8EE, ['n'] = 0xFFA4ACB8, ['k'] = 0xFF2A2C30,
    };

    /// <summary>줄무늬 판 둘을 다리 둘에 건 바리케이드.</summary>
    private static string[] Barricade(int w)
    {
        const int h = 14;
        var g = new Grid(w, h);
        foreach (int by in new[] { 1, 7 })
        {
            g.Fill(0, by, w, 5, 'o');
            for (int y = by + 1; y <= by + 3; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    bool red = (x + y) / 3 % 2 == 0;
                    int t = y - by - 1;
                    g[x, y] = red ? "Rrd"[t] : "Pwv"[t];
                }
        }
        // 다리는 판 앞에
        foreach (int lx in new[] { 2, w - 6 })
        {
            g.Fill(lx, 0, 4, h - 1, 'o');
            g.Fill(lx + 1, 1, 1, h - 3, 'm');
            g.Fill(lx + 2, 1, 1, h - 3, 'n');
            g.Fill(lx - 2, h - 2, 8, 2, 'o');
            g.Fill(lx - 1, h - 2, 6, 1, 'k');
        }
        return g.Rows();
    }

    // 물 채우는 플라스틱 방호벽: H·L·c·d 몸통, D 홈, K 물 넣는 마개
    private static readonly string[] WaterBarrier =
    [
        "..oooooooooooooo..",
        ".oHHHHHHKKHHHHHHo.",
        "oHLLLLLLLLLLLLLLdo",
        "oLccccccccccccccdo",
        "oLccccccccccccccdo",
        "oDDDDDDDDDDDDDDDDo",
        "oLccccccccccccccdo",
        "oLccccccccccccccdo",
        "oLccoooccccoooccdo",
        "oooo...oooo...oooo",
    ];

    private static readonly Dictionary<char, uint> BarrierOrange = new()
        { ['o'] = 0xFF7A2A06, ['H'] = 0xFFFFC08A, ['L'] = 0xFFFF9A40, ['c'] = 0xFFF06A12, ['d'] = 0xFFB84408, ['D'] = 0xFFC2500C, ['K'] = 0xFF3A3C42 };
    private static readonly Dictionary<char, uint> BarrierWhite = new()
        { ['o'] = 0xFF505662, ['H'] = 0xFFFFFFFF, ['L'] = 0xFFF2F4F6, ['c'] = 0xFFDDE1E6, ['d'] = 0xFFB0B6BE, ['D'] = 0xFFBCC2CA, ['K'] = 0xFFD02A2A };

    // 표지판: o 테두리, R·r 빨간 테, y 바탕, k 글씨, p·P 기둥
    private static readonly Dictionary<char, uint> SignColors = new()
        { ['o'] = 0xFF4A0E10, ['R'] = 0xFFF0504A, ['r'] = 0xFFC8202A, ['y'] = 0xFFFFFFFF, ['Y'] = 0xFFE4E6EA, ['k'] = 0xFF1E1E22, ['p'] = 0xFF8C949E, ['P'] = 0xFF4A5058 };

    /// <summary>접는 다리에 세운 세모 경고 표지(느낌표).</summary>
    private static string[] WarningSign()
    {
        const int w = 15, h = 22, th = 13;
        var g = new Grid(w, h);
        double cx = (w - 1) / 2.0;
        bool Tri(int x, int y) => y >= 0 && y < th && Math.Abs(x - cx) <= y * (cx + 0.3) / (th - 1) + 0.5;
        bool Inner(int x, int y) => Tri(x, y) && Tri(x - 1, y) && Tri(x + 1, y) && Tri(x, y - 1) && Tri(x, y + 1);
        // 기둥과 다리
        for (int y = th; y < h; y++) { g[7, y] = 'p'; g[6, y] = 'P'; g[8, y] = 'P'; }
        for (int i = 0; i < 6; i++)
        {
            g[6 - i / 1 - 1, h - 6 + i] = 'P';
            g[8 + i / 1 + 1, h - 6 + i] = 'P';
        }
        for (int y = 0; y < th; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Tri(x, y)) continue;
                if (Edge(Tri, x, y)) g[x, y] = 'o';
                else if (!Inner(x, y) || Edge(Inner, x, y)) g[x, y] = x < cx ? 'R' : 'r';
                else g[x, y] = y > th - 5 ? 'Y' : 'y';
            }
        for (int y = 4; y <= 8; y++) g[7, y] = 'k';
        g[7, 10] = 'k';
        return g.Rows();
    }

    // 우회 화살표 표지: o 테두리, Y·y·d 노랑 판, k 화살표, P·p 다리
    private static readonly Dictionary<char, uint> DetourColors = new()
        { ['o'] = 0xFF4A3406, ['Y'] = 0xFFFFE680, ['y'] = 0xFFFFC820, ['d'] = 0xFFD09A10, ['k'] = 0xFF1E1E22, ['p'] = 0xFF8C949E, ['P'] = 0xFF4A5058 };

    private static readonly string[] DetourSign =
    [
        "ooooooooooooooo",
        "oYYYYYYYYYYYYYo",
        "oYyyyyyykyyyydo",
        "oYyyyyyykkyyydo",
        "oYykkkkkkkkyydo",
        "oYyyyyyykkyyydo",
        "oYyyyyyykyyyydo",
        "oddddddddddddddo",
        "ooooooooooooooo",
        "..PpP.....PpP..",
        "..PpP.....PpP..",
        "..PpP.....PpP..",
        ".PPpPP...PPpPP.",
    ];

    // 길 드럼통(주황·흰 띠, 검은 뚜껑·받침)
    private static string[] RoadDrum()
    {
        const int w = 13, h = 17;
        string Mat(int y) => y >= h - 3 ? "kGgbB" : (y >= 5 && y <= 6) || (y >= 9 && y <= 10) ? "oWwvV" : "oHLMD";
        return Lathe(w, h, y => y >= h - 3 ? 6.5 : 5.6, Mat, capH: 4, cap: "kgbB");
    }

    private static readonly Dictionary<char, uint> DrumColors = new()
    {
        ['o'] = 0xFF7A2A06, ['H'] = 0xFFFFC08A, ['L'] = 0xFFFF9A40, ['M'] = 0xFFF06A12, ['D'] = 0xFFB84408,
        ['W'] = 0xFFFFFFFF, ['w'] = 0xFFF2F4F6, ['v'] = 0xFFD8DCE2, ['V'] = 0xFFAEB4BC,
        ['k'] = 0xFF141618, ['G'] = 0xFF6A6E76, ['g'] = 0xFF4A4E56, ['b'] = 0xFF34373C, ['B'] = 0xFF24262A,
    };

    private static readonly string[] Delineator =
    [
        "..ooo..",
        ".oCcdo.",
        ".owwWo.",
        ".owwWo.",
        ".oCcdo.",
        ".oCcdo.",
        ".owwWo.",
        ".owwWo.",
        ".oCcdo.",
        ".oCcdo.",
        ".oCcdo.",
        ".oCcdo.",
        "kBBBBBk",
        "kbbbbbk",
        ".kkkkk.",
    ];

    /// <summary>라바콘·바리케이드: 라바콘 셋과 쓰러진 것, 바리케이드 둘, 물 방호벽 둘, 경고 표지, 우회 표지, 드럼통, 시선 유도봉.</summary>
    public static StampSet TrafficCones()
    {
        var shapes = new List<PixelImage>
        {
            Art(ConeLarge, ConeColors),
            Art(ConeMedium, ConeColors),
            Art(ConeSmall, ConeColors),
            Art(ConeTipped(), ConeColors),
            FlipX(Art(ConeTipped(), ConeColors)),
            Art(Barricade(24), BarricadeRed),
            Art(Barricade(20), BarricadeYellow),
            Art(WaterBarrier, BarrierOrange),
            Art(WaterBarrier, BarrierWhite),
            Art(WarningSign(), SignColors),
            Art(DetourSign, DetourColors),
            FlipX(Art(DetourSign, DetourColors)),
            Art(RoadDrum(), DrumColors),
            Art(Delineator, ConeColors),
        };
        return Set("Traffic Cones & Barriers", shapes);
    }

    // ───────────── 5. 통·자루 ─────────────

    // 나무 통: o 테두리, H·L·M·D 나무, A·B·C·E 쇠테, R 뚜껑 가장자리, T·t 뚜껑
    private static readonly Dictionary<char, uint> BarrelWood = new()
    {
        ['o'] = 0xFF3A200C, ['H'] = 0xFFE4A462, ['L'] = 0xFFC4843E, ['M'] = 0xFF9E602A, ['D'] = 0xFF703E18,
        ['A'] = 0xFFD0D4DC, ['B'] = 0xFF9AA0AA, ['C'] = 0xFF6E747E, ['E'] = 0xFF484C54,
        ['R'] = 0xFFD69A58, ['T'] = 0xFFB27838, ['t'] = 0xFF8E5A28,
    };
    private static readonly Dictionary<char, uint> BarrelDark = new()
    {
        ['o'] = 0xFF24140A, ['H'] = 0xFFB07848, ['L'] = 0xFF8E5C32, ['M'] = 0xFF6E4424, ['D'] = 0xFF4A2C16,
        ['A'] = 0xFFB8BCC4, ['B'] = 0xFF868C96, ['C'] = 0xFF5C626C, ['E'] = 0xFF3A3E46,
        ['R'] = 0xFF9A6838, ['T'] = 0xFF2A5A9A, ['t'] = 0xFF3A78C0,
    };

    /// <summary>나무 통(쇠테, 판자 이음매). <paramref name="hoops"/>는 쇠테 줄들, <paramref name="cap"/>은 윗면(뚜껑 또는 물).</summary>
    private static string[] Barrel(int w, int h, int capH, double bulge, int[] hoops, string cap = "oRTT")
    {
        double rEnd = (w - 1) / 2.0 + 0.5 - bulge, mid = (h - 1) / 2.0;
        return Lathe(w, h, y => rEnd + bulge * (1 - Math.Pow((y - mid) / mid, 2)), y => hoops.Contains(y) ? "oABCE" : "oHLMDD", capH, cap, [-0.45, 0.1, 0.6]);
    }

    /// <summary>옆으로 눕힌 나무 통(둥근 뚜껑이 왼쪽).</summary>
    private static string[] LyingBarrel()
    {
        const int w = 21, h = 13, ew = 8;
        int capH = h - 2;
        var g = new Grid(w, h);
        double cy = (h - 1) / 2.0, x0 = ew / 2.0, x1 = w - 1 - ew / 2.0, xm = (x0 + x1) / 2;
        double Half(int x) => capH / 2.0 + 1.0 * (1 - Math.Pow((x - xm) / (xm - x0), 2));
        bool Body(int x, int y)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return false;
            if (x > x1) return InEllipse(ew, capH, x - (w - ew), y - 1);
            return x >= x0 && Math.Abs(y - cy) < Half(Math.Min(x, (int)x1));
        }
        bool End(int x, int y) => InEllipse(ew, capH, x, y - 1);
        bool In(int x, int y) => Body(x, y) || End(x, y);
        int[] hoopCols = [ew + 1, ew + 2, w - 6, w - 5];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (End(x, y))
                {
                    bool edge = Edge(End, x, y);
                    bool ring = !edge && Edge((i, j) => End(i, j) && !Edge(End, i, j), x, y);
                    g[x, y] = edge ? 'o' : ring ? 'R' : y % 3 == 0 ? 't' : 'T';
                }
                else if (Body(x, y))
                {
                    string m = hoopCols.Contains(x) ? "oABCE" : "oHLMD";
                    double v = (y - cy) / Half(Math.Min(x, (int)x1));
                    bool seam = !hoopCols.Contains(x) && new[] { -0.45, 0.1, 0.6 }.Any(k => (int)Math.Round(cy + k * Half(Math.Min(x, (int)x1))) == y);
                    g[x, y] = Edge(In, x, y) ? 'o' : seam ? 'D' : m[Tone(v)];
                }
            }
        return g.Rows();
    }

    // 자루: o 테두리, H·L·M·D 삼베, r·R 끈, g·G 곡식, b·B 찍힌 무늬
    private static readonly Dictionary<char, uint> Burlap = new()
    {
        ['o'] = 0xFF5A3E1C, ['H'] = 0xFFF0D8A2, ['L'] = 0xFFDCBC80, ['M'] = 0xFFC29C5E, ['D'] = 0xFF9A7440,
        ['r'] = 0xFF8A6430, ['R'] = 0xFFC09048, ['g'] = 0xFFE8B838, ['G'] = 0xFFFFE070, ['b'] = 0xFF8A5A2A, ['B'] = 0xFFA0703A,
    };
    private static readonly Dictionary<char, uint> FlourSack = new()
    {
        ['o'] = 0xFF6A6458, ['H'] = 0xFFFFFFFA, ['L'] = 0xFFF4F0E6, ['M'] = 0xFFE2DCCE, ['D'] = 0xFFBEB6A6,
        ['r'] = 0xFF8A6430, ['R'] = 0xFFC09048, ['b'] = 0xFF2A5AA8, ['B'] = 0xFF5A8AD8,
    };

    private static readonly string[] SackFull =
    [
        "....o.o.o....",
        "...oLoMoDo...",
        "...oLLMMDo...",
        "....oLMDo....",
        "...orRRrro...",
        "..ooLMMMDoo..",
        ".oHLLMMMMMDo.",
        "oHLLMMMMMMMDo",
        "oHLMMbBbMMMDo",
        "oLLMMBbBMMDDo",
        "oLMMMbBbMMDDo",
        "oLMMMMMMMMDDo",
        "oLMMMMMMMDDDo",
        ".oDMMMMMMDDo.",
        "..ooooooooo..",
    ];

    private static readonly string[] SackOpen =
    [
        "...ooooooo...",
        "..oGgGggGgo..",
        ".oLoGgggGoDo.",
        ".oHLoooooMDo.",
        ".oHLMMMMMMDo.",
        "oHLLMMMMMMMDo",
        "oLLMMMMMMMDDo",
        "oLMMMMMMMMDDo",
        "oLMMMMMMMMDDo",
        ".oMMMMMMMDDo.",
        "..ooooooooo..",
    ];

    private static readonly string[] SackSlumped =
    [
        "....oooo........",
        "...oLLMDoooo....",
        "...oLMMMMMDDo...",
        "....orRro..oDo..",
        "...ooLMDoo..oo..",
        "..oHLMMMMDDo....",
        ".oHLLMMMMMMDDo..",
        "oHLLMMMMMMMMDDo.",
        "oLLMMMMMMMMMDDDo",
        "oLMMMMMMMMMMDDDo",
        ".oDMMMMMMMMDDDo.",
        "..oooooooooooo..",
    ];

    // 질그릇: 테라코타·초록 유약, 안쪽 어둠
    private static readonly Dictionary<char, uint> Terracotta = new()
    {
        ['o'] = 0xFF5A220E, ['H'] = 0xFFF4A878, ['L'] = 0xFFDA7C46, ['M'] = 0xFFBA5C2E, ['D'] = 0xFF8C3E1C,
        ['W'] = 0xFFFFF0D0, ['w'] = 0xFFF0D8A8, ['v'] = 0xFFD8BC88, ['V'] = 0xFFB0946A,
        ['R'] = 0xFFC8683A, ['T'] = 0xFF3A1A0C, ['t'] = 0xFF5A2A14,
    };
    private static readonly Dictionary<char, uint> GreenGlaze = new()
    {
        ['o'] = 0xFF16341E, ['H'] = 0xFFB4E8A8, ['L'] = 0xFF6CBE6A, ['M'] = 0xFF3E9450, ['D'] = 0xFF266838,
        ['W'] = 0xFFF2E6C0, ['w'] = 0xFFDCCA98, ['v'] = 0xFFC0AC78, ['V'] = 0xFF948250,
        ['R'] = 0xFF4E9A58, ['T'] = 0xFF8A6A40, ['t'] = 0xFF6E5030,
    };

    /// <summary>배가 불룩한 항아리(입이 열린, 가운데 띠).</summary>
    private static string[] Pot(int w, int h, int capH, int neck, int band)
    {
        double rLip = w / 2.0 - 1.5, rNeck = rLip - 0.8, rBelly = w / 2.0, bellyY = neck + (h - neck) * 0.45;
        double R(int y)
        {
            if (y < capH) return rLip;
            if (y <= neck) return rNeck;
            double t = (y - bellyY) / (y < bellyY ? bellyY - neck : h - bellyY);
            return y < bellyY ? rNeck + (rBelly - rNeck) * Math.Sqrt(1 - t * t) : rBelly - (rBelly - rLip + 0.6) * t * t;
        }
        return Lathe(w, h, R, y => y == band || y == band + 1 ? "oWwvV" : "oHLMD", capH, "oRTt");
    }

    // 양동이: 나무 판, 쇠테, 물
    private static readonly Dictionary<char, uint> BucketColors = new()
    {
        ['o'] = 0xFF3A200C, ['H'] = 0xFFE4A462, ['L'] = 0xFFC4843E, ['M'] = 0xFF9E602A, ['D'] = 0xFF703E18,
        ['A'] = 0xFFD0D4DC, ['B'] = 0xFF9AA0AA, ['C'] = 0xFF6E747E, ['E'] = 0xFF484C54,
        ['R'] = 0xFFC4843E, ['T'] = 0xFF2A5A9A, ['t'] = 0xFF4A8AD0, ['h'] = 0xFF5A606A,
    };

    private static string[] Bucket()
    {
        const int w = 13, h = 11;
        var body = Lathe(w, h, y => 6.5 - y * 0.16, y => y == 3 || y == h - 3 ? "oABCE" : "oHLMD", 4, "oRTt");
        var g = new Grid(w, h + 4);
        string[] handle = ["...hhhhhhh...", "..h.......h..", ".h.........h.", ".h.........h."];
        g.Stamp(handle, 0, 0);
        g.Stamp(body, 0, 4);
        return g.Rows();
    }

    /// <summary>통·자루: 나무 통(서 있는·작은 술통·눕힌·빗물 통), 자루(가득·열린·주저앉은·밀가루), 항아리 셋, 양동이.</summary>
    public static StampSet BarrelsAndSacks()
    {
        var shapes = new List<PixelImage>
        {
            Art(Barrel(13, 17, 4, 1.0, [4, 5, 12, 13]), BarrelWood),
            Art(Barrel(9, 11, 3, 0.7, [3, 8]), BarrelWood),
            Art(Barrel(13, 15, 4, 0.9, [4, 11], "oRTt"), BarrelDark),
            Art(SackFull, Burlap),
            Art(SackOpen, Burlap),
            Art(SackFull, FlourSack),
            Art(Pot(12, 14, 4, 4, 8), Terracotta),
            Art(Pot(9, 10, 3, 2, -9), Terracotta),
            Art(Pot(11, 15, 4, 5, 9), GreenGlaze),
            Art(Bucket(), BucketColors),
        };
        shapes.AddRange(Mirrored([Art(LyingBarrel(), BarrelWood), Art(SackSlumped, Burlap)]));
        return Set("Barrels & Sacks", shapes);
    }
}
