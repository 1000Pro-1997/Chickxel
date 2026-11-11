using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 상자·거리 소품 모양 펜 세트 다섯 가지: 랜덤 박스, 종이 상자·나무 상자, 길에 버려진 쓰레기, 라바콘·바리케이드, 통·자루.
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

    // ───────────── 1. 랜덤 박스 ─────────────

    // o 테두리, d 그늘, y 바탕, H 밝은 곳, r 리벳, q 물음표, Q 물음표 그림자
    private static readonly Dictionary<char, uint> GoldBlock = new()
        { ['o'] = 0xFF6A3A04, ['d'] = 0xFFC27412, ['y'] = 0xFFF2A81C, ['H'] = 0xFFFFE07A, ['r'] = 0xFF9A5A0C, ['q'] = 0xFFFFF8E0, ['Q'] = 0xFF8A4A08 };
    private static readonly Dictionary<char, uint> PurpleBlock = new()
        { ['o'] = 0xFF341058, ['d'] = 0xFF6A28A6, ['y'] = 0xFF9A50DE, ['H'] = 0xFFD4A8FF, ['r'] = 0xFF4E1C80, ['q'] = 0xFFFFF4FF, ['Q'] = 0xFF46187A };
    private static readonly Dictionary<char, uint> BlueBlock = new()
        { ['o'] = 0xFF0C2A66, ['d'] = 0xFF1C58BE, ['y'] = 0xFF3A8CF0, ['H'] = 0xFFAAD8FF, ['r'] = 0xFF143E8E, ['q'] = 0xFFF4FAFF, ['Q'] = 0xFF123A86 };
    private static readonly Dictionary<char, uint> RedBlock = new()
        { ['o'] = 0xFF560A12, ['d'] = 0xFFA81C26, ['y'] = 0xFFE23A3C, ['H'] = 0xFFFF9C8C, ['r'] = 0xFF7A1018, ['q'] = 0xFFFFF2E8, ['Q'] = 0xFF701018 };

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

    /// <summary>앞에서 본 물음표 블록(한 변 <paramref name="s"/>): 모서리 리벳, 오른쪽 아래로 그림자 진 물음표.</summary>
    private static string[] QBlock(int s)
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
        var mark = s >= 14 ? BigQ : SmallQ;
        int mw = mark[0].Length, mh = mark.Length, x0 = (s - mw - 1) / 2, y0 = (s - mh - 1) / 2;
        for (int j = 0; j < mh; j++)
            for (int i = 0; i < mw; i++)
                if (mark[j][i] == 'q') g[x0 + i + 1, y0 + j + 1] = 'Q';
        for (int j = 0; j < mh; j++)
            for (int i = 0; i < mw; i++)
                if (mark[j][i] == 'q') g[x0 + i, y0 + j] = 'q';
        return g.Rows();
    }

    // 빛나는 블록: 한 단계씩 밝게
    private static Dictionary<char, uint> LitBlock(Dictionary<char, uint> p) => new()
        { ['o'] = p['d'], ['d'] = p['y'], ['y'] = p['H'], ['H'] = 0xFFFFFFFF, ['r'] = p['y'], ['q'] = 0xFFFFFFFF, ['Q'] = p['d'] };

    // 보물 상자: o 테두리, W·w·v 나무(밝음·바탕·그늘), G·g·k 쇠띠(밝음·바탕·그늘), x 뚜껑 안쪽, C·c·n 금화, E·e 보석
    private static readonly Dictionary<char, uint> ChestWood = new()
        { ['o'] = 0xFF3A1E0C, ['W'] = 0xFFD08A48, ['w'] = 0xFFAA6630, ['v'] = 0xFF7A4420, ['x'] = 0xFF4E2A12 };
    private static readonly Dictionary<char, uint> GoldTrim = new() { ['G'] = 0xFFFFE27A, ['g'] = 0xFFE0A42A, ['k'] = 0xFF8A5A10 };
    private static readonly Dictionary<char, uint> IronTrim = new() { ['G'] = 0xFFF0F4F8, ['g'] = 0xFFAEB6C2, ['k'] = 0xFF5A626E };
    private static readonly Dictionary<char, uint> Treasure = new()
        { ['C'] = 0xFFFFF4A8, ['c'] = 0xFFF6C430, ['n'] = 0xFFB07810, ['E'] = 0xFFFF9AB0, ['e'] = 0xFFE0204A };

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

    // 선물 상자: 3/4 상자(BoxGrid)의 o·U·T·e·F·f·s + G·g·k 리본, q 물음표 무늬
    private static readonly Dictionary<char, uint> GiftPurple = new()
        { ['o'] = 0xFF2E0E4E, ['U'] = 0xFFC898F4, ['T'] = 0xFFA870E4, ['e'] = 0xFF5A2290, ['F'] = 0xFF9A5AD8, ['f'] = 0xFF8244C4, ['s'] = 0xFF6430A0, ['q'] = 0xFFE8D0FF };
    private static readonly Dictionary<char, uint> GiftTeal = new()
        { ['o'] = 0xFF0A3A3A, ['U'] = 0xFF8AEAD8, ['T'] = 0xFF4CCCB8, ['e'] = 0xFF16706A, ['F'] = 0xFF38B4A2, ['f'] = 0xFF24A090, ['s'] = 0xFF187E72, ['q'] = 0xFFD0FFF4 };
    private static readonly Dictionary<char, uint> GiftRed = new()
        { ['o'] = 0xFF4E0810, ['U'] = 0xFFFF8A86, ['T'] = 0xFFF0545A, ['e'] = 0xFF8E1420, ['F'] = 0xFFE0444C, ['f'] = 0xFFC8303A, ['s'] = 0xFF9A1C28, ['q'] = 0xFFFFD8D0 };
    private static readonly Dictionary<char, uint> RibbonGold = new() { ['G'] = 0xFFFFE890, ['g'] = 0xFFF2BA30, ['k'] = 0xFFA0700E };
    private static readonly Dictionary<char, uint> RibbonPink = new() { ['G'] = 0xFFFFD0E8, ['g'] = 0xFFFF7AB8, ['k'] = 0xFFB83A78 };
    private static readonly Dictionary<char, uint> RibbonWhite = new() { ['G'] = 0xFFFFFFFF, ['g'] = 0xFFE4E8EE, ['k'] = 0xFFA8B0BC };

    private static readonly string[] TinyQ = ["qq.", "..q", ".q.", "...", ".q."];

    private static readonly string[] GiftBow =
    [
        ".ooo..ooo.",
        "oGggoogGgo",
        "oggkGgkggo",
        ".oooooooo.",
    ];

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

    /// <summary>랜덤 박스: 네 색 물음표 블록(큰·작은), 빛나는 희귀 블록 둘, 물음표 선물 상자 셋, 보물 상자(닫힘·쇠띠·열려 반짝임).</summary>
    public static StampSet MysteryBoxes()
    {
        var shapes = new List<PixelImage>();
        foreach (var p in new[] { GoldBlock, PurpleBlock, BlueBlock, RedBlock }) shapes.Add(Art(QBlock(16), p));
        foreach (var p in new[] { GoldBlock, BlueBlock, RedBlock }) shapes.Add(Art(QBlock(12), p));
        shapes.Add(Glow(Art(QBlock(16), LitBlock(GoldBlock)), 0xFFFFD040));
        shapes.Add(Glow(Art(QBlock(14), LitBlock(PurpleBlock)), 0xFFC07CFF));
        shapes.Add(Art(MysteryGift(14, 4, 9), GiftPurple, RibbonGold));
        shapes.Add(Art(MysteryGift(12, 3, 8), GiftTeal, RibbonPink));
        shapes.Add(Art(MysteryGift(16, 4, 11), GiftRed, RibbonWhite));
        shapes.Add(Art(ChestClosed, ChestWood, GoldTrim));
        shapes.Add(Art(ChestClosed, ChestWood, IronTrim));
        shapes.Add(Compose(21, 21,
        [
            (Art(ChestOpen, ChestWood, GoldTrim, Treasure), 2, 5),
            (Art(BigStar, Sparkle), 0, 0),
            (Art(SmallStar, Sparkle), 16, 0),
            (Art(SmallStar, Sparkle), 9, 1),
        ]));
        return Set("Mystery Boxes", shapes);
    }

    // ───────────── 2. 종이 상자·나무 상자 ─────────────

    /// <summary>
    /// 위·앞에서 본 상자 판: o 테두리, U·T 윗면(밝은 곳·바탕), e 윗면과 앞면 사이 모서리, F·f·s 앞면(밝은 곳·바탕·그늘).
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

    // 종이 상자: t·u 테이프(밝음·그늘), L·l 라벨(종이·글줄), a 화살표 인쇄, k 상자 안 그늘
    private static readonly Dictionary<char, uint> Cardboard = new()
    {
        ['o'] = 0xFF5A3816, ['U'] = 0xFFF2D29A, ['T'] = 0xFFE2B878, ['e'] = 0xFF9A6630, ['F'] = 0xFFD8A464, ['f'] = 0xFFC48C4C, ['s'] = 0xFFA06E36,
        ['t'] = 0xFFEEE2C0, ['u'] = 0xFFC8B48A, ['L'] = 0xFFF8F6F0, ['l'] = 0xFF8A8E96, ['a'] = 0xFF7A4E22, ['k'] = 0xFF6A4220,
    };
    private static readonly Dictionary<char, uint> FragileTape = new() { ['t'] = 0xFFF04A3A, ['u'] = 0xFFB82A22 };

    private static readonly string[] UpArrow = ["..a..", ".aaa.", "a.a.a", "..a..", "..a.."];

    /// <summary>테이프로 봉한 상자(라벨·화살표는 고르기).</summary>
    private static string[] TapedBox(int w, int top, int front, bool label = false, bool arrows = false)
    {
        var g = BoxGrid(w, top, front);
        int c = w / 2 - 1;
        for (int y = 1; y <= top; y++) { g[c, y] = 't'; g[c + 1, y] = 'u'; }
        g[c, top + 1] = g[c + 1, top + 1] = 'u';
        int tape = Math.Max(2, front / 3);
        for (int y = top + 2; y < top + 2 + tape; y++) { g[c, y] = 't'; g[c + 1, y] = 'u'; }
        if (arrows) g.Stamp(UpArrow, 2, c >= 7 ? top + 3 : top + 3 + tape);
        if (label)
        {
            int lw = Math.Max(5, w / 3), lx = w - 2 - lw - 1, ly = g.H - 2 - 5;
            g.Fill(lx, ly, lw, 4, 'L');
            g.Fill(lx + 1, ly + 1, lw - 2, 1, 'l');
            g.Fill(lx + 1, ly + 2, lw - 3, 1, 'l');
        }
        return g.Rows();
    }

    // 옆 날개(경첩이 상자 옆 모서리, 바깥 아래로 처짐) — 윗면 5줄 상자용
    private static readonly string[] LeftFlap = ["..oo", ".oUo", "oUTo", "oTTo", "oTTo", "oTTo", "oTo.", "oo.."];
    private static readonly string[] RightFlap = ["oo..", "oUo.", "oTso", "oTso", "oTso", "oTso", ".oso", "..oo"];

    /// <summary>날개를 펼친 빈 상자(뒤 날개는 세우고, 앞 날개는 앞면에 늘어뜨리고, 옆 날개는 바깥으로). 윗면은 5줄.</summary>
    private static string[] OpenBox(int w, int front)
    {
        const int top = 5;
        var body = BoxGrid(w, top, front);
        for (int y = 1; y <= top; y++)
            for (int x = 1; x < w - 1; x++) body[x, y] = y <= 3 ? 's' : 'k';
        body.Fill(1, top + 2, w - 2, 3, 'T');
        body.Fill(1, top + 2, w - 2, 1, 'U');
        body.Fill(1, top + 2, 1, 3, 'U');
        body.Fill(1, top + 5, w - 2, 1, 'o');
        var back = new Grid(w - 4, 5);
        back.Fill(0, 0, w - 4, 5, 'o');
        back.Fill(1, 1, w - 6, 3, 'f');
        back.Fill(1, 1, w - 6, 1, 'F');
        var g = new Grid(w + 6, body.H + 4);
        g.Stamp(back.Rows(), 5, 0);
        g.Stamp(body.Rows(), 3, 4);
        g.Stamp(LeftFlap, 0, 4);
        g.Stamp(RightFlap, w + 2, 4);
        return g.Rows();
    }

    /// <summary>접어서 납작하게 눕힌 상자 판(접힌 금 둘, 떼다 만 테이프).</summary>
    private static string[] FlatCardboard()
    {
        const int w = 24, h = 8;
        var g = new Grid(w, h);
        for (int y = 0; y < h; y++)
        {
            int x0 = (h - 1 - y) / 2, x1 = w - 1 - y / 2;
            for (int x = x0; x <= x1; x++)
            {
                bool edge = y == 0 || y == h - 1 || x == x0 || x == x1;
                g[x, y] = edge ? 'o' : y == h - 2 ? 'f' : y == 1 ? 'U' : 'T';
            }
            int fold1 = x0 + 8 - y / 3, fold2 = x0 + 16 - y / 3;
            if (y > 0 && y < h - 2) { g[fold1, y] = 'e'; g[fold2, y] = 'e'; }
        }
        g[12, 2] = g[13, 2] = g[12, 3] = 't';
        g[13, 3] = g[12, 4] = 'u';
        return g.Rows();
    }

    /// <summary>큰 상자 위에 작은 상자를 얹은 한 쌍(<paramref name="smallLeft"/>면 작은 상자가 왼쪽).</summary>
    private static string[] StackedBoxes(bool smallLeft)
    {
        var big = TapedBox(18, 4, 11, label: true);
        var small = TapedBox(11, 3, 7);
        int bigH = big.Length, smallH = small.Length, overlap = 3;
        var g = new Grid(18, bigH + smallH - overlap);
        g.Stamp(big, 0, smallH - overlap);
        g.Stamp(small, smallLeft ? 1 : 6, 0);
        return g.Rows();
    }

    // 나무 상자: P·p·q 테두리 널(밝음·바탕·그늘), b 판자, g 판자 틈, n 못
    private static readonly Dictionary<char, uint> CrateLight = new()
    {
        ['o'] = 0xFF3E2410, ['U'] = 0xFFEEC688, ['T'] = 0xFFD8A464, ['e'] = 0xFF7A4A20, ['F'] = 0xFFC08646, ['f'] = 0xFFAA7038, ['s'] = 0xFF8A5828,
        ['P'] = 0xFFE8B472, ['p'] = 0xFFC8904E, ['q'] = 0xFF9A6430, ['b'] = 0xFFAE743A, ['g'] = 0xFF5A3418, ['n'] = 0xFF4A4A52,
    };
    private static readonly Dictionary<char, uint> CrateDark = new()
    {
        ['o'] = 0xFF2A160A, ['U'] = 0xFFB88456, ['T'] = 0xFF9E6C40, ['e'] = 0xFF4E2C12, ['F'] = 0xFF8A5A30, ['f'] = 0xFF784C26, ['s'] = 0xFF5E3A1C,
        ['P'] = 0xFFAE7A48, ['p'] = 0xFF8E6036, ['q'] = 0xFF6A4422, ['b'] = 0xFF7C5028, ['g'] = 0xFF3A2010, ['n'] = 0xFF2E2E34,
    };

    /// <summary>테두리 널과 대각선 버팀목을 댄 나무 상자.</summary>
    private static string[] Crate(int w, int top, int front)
    {
        var g = BoxGrid(w, top, front);
        for (int y = 1; y <= top; y++)
            for (int x = 1; x < w - 1; x++)
                if (x % 4 == 0) g[x, y] = 'g';
        int fy = top + 2, fh = front;
        // 앞면 판자
        for (int y = fy; y < fy + fh; y++)
            for (int x = 1; x < w - 1; x++)
                g[x, y] = (y - fy) % 3 == 2 ? 'g' : 'b';
        // 테두리 널
        for (int x = 1; x < w - 1; x++) { g[x, fy] = x < w - 2 ? 'P' : 'p'; g[x, fy + 1] = 'p'; g[x, fy + fh - 2] = 'p'; g[x, fy + fh - 1] = 'q'; }
        for (int y = fy; y < fy + fh; y++) { g[1, y] = 'P'; g[2, y] = 'p'; g[w - 3, y] = 'p'; g[w - 2, y] = 'q'; }
        // 대각선 버팀목(왼쪽 아래 → 오른쪽 위)
        int iw = w - 6, ih = fh - 4;
        for (int i = 0; i <= iw; i++)
        {
            int x = 3 + i, y = fy + fh - 3 - (int)Math.Round(i * (ih - 1) / (double)iw);
            g[x, y] = 'p';
            if (g[x, y - 1] != 'p') g[x, y - 1] = 'P';
            if (g[x, y + 1] != 'P') g[x, y + 1] = 'q';
        }
        g[2, fy + 1] = g[w - 3, fy + 1] = g[2, fy + fh - 2] = g[w - 3, fy + fh - 2] = 'n';
        return g.Rows();
    }

    /// <summary>나무 팔레트: 윗판 틈, 앞에서 보이는 받침 토막 셋.</summary>
    private static string[] Pallet()
    {
        const int w = 26, h = 9;
        var g = new Grid(w, h);
        g.Fill(0, 0, w, 5, 'o');
        for (int y = 1; y <= 2; y++)
            for (int x = 1; x < w - 1; x++) g[x, y] = x % 5 == 0 ? 'g' : y == 1 ? 'U' : 'T';
        g.Fill(1, 3, w - 2, 1, 'F');
        int[] blocks = [0, (w - 5) / 2, w - 5];
        foreach (int bx in blocks)
        {
            g.Fill(bx, 4, 5, 4, 'o');
            g.Fill(bx + 1, 4, 3, 3, 'f');
            g[bx + 1, 4] = 'F';
        }
        g.Fill(0, 7, w, 2, 'o');
        g.Fill(1, 7, w - 2, 1, 'f');
        g[1, 7] = 'F';
        return g.Rows();
    }

    /// <summary>종이 상자·나무 상자: 테이프 상자 넷(작은·중간·큰·세로), 깨짐 주의 상자, 펼친 상자, 납작한 판, 쌓은 한 쌍 둘, 나무 상자 셋, 팔레트.</summary>
    public static StampSet CardboardBoxes()
    {
        var shapes = new List<PixelImage>
        {
            Art(TapedBox(12, 3, 8), Cardboard),
            Art(TapedBox(16, 4, 11, arrows: true), Cardboard),
            Art(TapedBox(22, 5, 14, label: true, arrows: true), Cardboard),
            Art(TapedBox(11, 3, 15, label: true), Cardboard),
            Art(TapedBox(14, 4, 10, arrows: true), FragileTape, Cardboard),
            Art(OpenBox(16, 11), Cardboard),
            Art(FlatCardboard(), Cardboard),
            Art(StackedBoxes(true), Cardboard),
            Art(StackedBoxes(false), Cardboard),
            Art(Crate(12, 3, 10), CrateLight),
            Art(Crate(18, 4, 14), CrateLight),
            Art(Crate(15, 4, 12), CrateDark),
            Art(Pallet(), CrateLight),
        };
        return Set("Cardboard Boxes & Crates", shapes);
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
