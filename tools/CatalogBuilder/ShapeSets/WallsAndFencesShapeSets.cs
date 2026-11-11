using Chickxel.Core.Edit;   // StampOrder
using Chickxel.Core.IO;     // StampSet, ShapeArt
using Chickxel.Core.Model;  // PixelImage, Color32
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 담장·울타리 모양 펜 세트(옆으로 끌어 이어 붙이는 띠): 회색 벽돌담, 붉은 벽돌 담장, 흰 말뚝 울타리, 산울타리, 만국기 줄.
/// 세트마다 조각 높이가 같고 왼쪽·오른쪽 끝이 불투명하며 무늬 주기가 양 끝에서 맞아 어떤 순서로 붙여도 이음매가 보이지 않는다.
/// 정면에서 본 모습(땅은 아래), 빛은 왼쪽 위에서. 같은 코드면 늘 같은 픽셀.
/// </summary>
internal static class WallsAndFencesShapeSets
{
    // ───────────── 공통 ─────────────

    private static Color32 C(uint argb) => new(argb);

    private static Color32 Mix(Color32 a, Color32 b, double t) => new(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t), a.A);

    private static Color32 Lit(Color32 c, double t) => Mix(c, new Color32(255, 248, 232), t);
    private static Color32 Dim(Color32 c, double t) => Mix(c, new Color32(20, 16, 28), t);

    private static void Put(PixelImage img, int x, int y, Color32 c)
    {
        if (img.Contains(x, y)) img.Set(x, y, c);
    }

    private static void Tint(PixelImage img, int x, int y, Func<Color32, Color32> f)
    {
        if (img.Contains(x, y) && img.Get(x, y).A > 0) img.Set(x, y, f(img.Get(x, y)));
    }

    private static void Blit(PixelImage dst, PixelImage src, int ox, int oy)
    {
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                if (src.Get(x, y).A > 0) Put(dst, ox + x, oy + y, src.Get(x, y));
    }

    // 벽돌 판: 줄마다 4행(벽돌 3 + 아래 줄눈 1), 벽돌 길이 period-1 + 세로 줄눈 1, 홀수 줄은 반 칸 밀림(엇쌓기).
    // special(줄, 벽돌 번호)이 색을 내면 그 벽돌만 그 색. 벽돌 번호 i의 왼쪽 x = i*period - (홀수 줄이면 period/2).
    private static PixelImage BrickField(int w, int courses, uint seed, Color32[] tones, Color32 mortar, int period = 8, Func<int, int, Color32?>? special = null)
    {
        var rng = new Lcg(seed);
        int cols = w / period + 2;
        var tone = new Color32[courses, cols];
        for (int c = 0; c < courses; c++)
            for (int i = 0; i < cols; i++)
            {
                var t = tones[rng.Next(tones.Length)];
                tone[c, i] = special?.Invoke(c, i) ?? t;
            }
        var grain = new Lcg(seed * 31 + 5);
        var img = new PixelImage(w, courses * 4);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < w; x++)
            {
                int c = y / 4, ly = y % 4, sx = x + ((c & 1) == 1 ? period / 2 : 0), lx = sx % period;
                int g = grain.Next(10);
                if (ly == 3 || lx == period - 1)
                {
                    img.Set(x, y, ly == 3 && lx == 0 ? Dim(mortar, .08) : mortar);
                    continue;
                }
                var b = tone[c, sx / period];
                var col = ly == 0 ? Lit(b, .2) : ly == 2 ? Dim(b, .18) : b;
                if (lx == 0 && ly < 2) col = Lit(col, .12);
                if (lx == period - 2 && ly > 0) col = Dim(col, .12);
                if (g == 0 && ly > 0) col = Dim(col, .1);
                else if (g == 1 && ly < 2) col = Lit(col, .07);
                img.Set(x, y, col);
            }
        return img;
    }

    private static int BrickX(int course, int index, int period = 8) => index * period - ((course & 1) == 1 ? period / 2 : 0);

    // 잎 하나(2×2 + 아래 그늘)
    private static void Leaf(PixelImage img, int x, int y, Color32 hi, Color32 light, Color32 mid, Color32 dark)
    {
        Put(img, x, y, hi);
        Put(img, x + 1, y, light);
        Put(img, x, y + 1, light);
        Put(img, x + 1, y + 1, mid);
        Put(img, x + 2, y + 1, dark);
        Put(img, x + 1, y + 2, dark);
    }

    // ───────────── 회색 벽돌담 ─────────────

    private static readonly Color32[] GrayBricks = [C(0xFF80868E), C(0xFF8C9299), C(0xFF767C85), C(0xFF979CA3), C(0xFF858A90)];
    private static readonly Color32[] GrayCap = [C(0xFFA2A6AB), C(0xFFABAFB3), C(0xFF979CA2)];
    private static readonly Color32 GrayMortar = C(0xFFCDCAC2), GrayOutline = C(0xFF464B53);
    private const int GrayH = 21;

    /// <summary>회색 벽돌담: 납작한 갓돌, 엇쌓은 회색 벽돌 네 줄. 어두운 벽돌·금 간 벽돌·깨진 모서리·이끼·물때·빠진 반 장 변형.</summary>
    public static StampSet GrayBrickWall()
    {
        Color32 dark = C(0xFF5F656D), darker = C(0xFF565B62), pale = C(0xFFA8ADB3);
        var shapes = new List<PixelImage>
        {
            GrayWall(16, 1),
            GrayWall(24, 2),
            GrayWall(32, 3),
            GrayWall(24, 4, (c, i) => (c, i) switch { (1, 1) => dark, (3, 2) => darker, (2, 0) => pale, _ => null }),
            Cracked(GrayWall(16, 5)),
            Chipped(GrayWall(16, 6)),
            Moss(GrayWall(24, 7), 3, 19, 7),
            Stain(GrayWall(16, 8), 9),
            MissingHalf(GrayWall(24, 9, (c, i) => (c, i) == (0, 2) ? dark : null), 2, 8),
            Moss(Stain(GrayWall(32, 10), 22), 1, 13, 11),
        };
        return Set("Gray Brick Wall", shapes);
    }

    private static PixelImage GrayWall(int w, uint seed, Func<int, int, Color32?>? special = null)
    {
        var img = new PixelImage(w, GrayH);
        var rng = new Lcg(seed + 101);
        var capTone = new Color32[w / 8 + 2];
        for (int i = 0; i < capTone.Length; i++) capTone[i] = GrayCap[rng.Next(GrayCap.Length)];
        // 갓돌(0~3행): 테두리, 밝은 윗면, 앞면, 그늘. 이음은 x%8 == 3
        for (int x = 0; x < w; x++)
        {
            int sx = x + 4, lx = sx % 8;
            var t = capTone[sx / 8];
            img.Set(x, 0, GrayOutline);
            img.Set(x, 1, lx == 7 ? Dim(t, .3) : lx == 0 ? Lit(t, .45) : Lit(t, .28));
            img.Set(x, 2, lx == 7 ? Dim(t, .4) : lx == 0 ? Lit(t, .1) : t);
            img.Set(x, 3, Dim(t, .5));
        }
        Blit(img, BrickField(w, 4, seed, GrayBricks, GrayMortar, special: special), 0, 4);
        for (int x = 0; x < w; x++)
        {
            img.Set(x, 4, Dim(img.Get(x, 4), .25));
            img.Set(x, GrayH - 1, GrayOutline);
        }
        return img;
    }

    // 벽돌 두 장을 지나 줄눈까지 이어지는 금
    private static PixelImage Cracked(PixelImage img)
    {
        (int X, int Y)[] path = [(11, 5), (10, 6), (10, 7), (9, 8), (9, 9), (8, 10), (8, 11), (7, 12), (6, 13), (6, 14), (5, 15)];
        foreach (var (x, y) in path) Tint(img, x, y, c => Dim(c, .55));
        foreach (var (x, y) in path) if (!path.Contains((x + 1, y))) Tint(img, x + 1, y, c => Lit(c, .12));
        Tint(img, 11, 6, c => Dim(c, .4));
        Tint(img, 7, 13, c => Dim(c, .35));
        return img;
    }

    // 갓돌 위 모서리와 벽돌 한 장 모서리가 떨어져 나감
    private static PixelImage Chipped(PixelImage img)
    {
        foreach (var x in new[] { 10, 11, 12, 13 }) img.Set(x, 0, Color32.Transparent);
        foreach (var x in new[] { 11, 12 }) img.Set(x, 1, Color32.Transparent);
        foreach (var (x, y) in new[] { (10, 1), (13, 1), (11, 2), (12, 2) }) img.Set(x, y, GrayOutline);
        Tint(img, 10, 2, c => Dim(c, .2));
        // 둘째 줄(홀수, 8~10행) 벽돌 x 4~10의 왼쪽 아래 모서리
        var hole = C(0xFF3E434A);
        foreach (var (x, y) in new[] { (4, 10), (5, 10), (4, 9) }) img.Set(x, y, hole);
        Tint(img, 6, 10, c => Lit(c, .2));
        Tint(img, 5, 9, c => Lit(c, .2));
        Tint(img, 4, 8, c => Lit(c, .15));
        return img;
    }

    // 아래에서 자라 오른 이끼(낮고 고르지 않은 띠, 줄눈을 따라 위로 번짐)
    private static PixelImage Moss(PixelImage img, int x0, int x1, uint seed)
    {
        var rng = new Lcg(seed);
        Color32 hi = C(0xFF8DB352), light = C(0xFF6A9440), mid = C(0xFF4F7834), dark = C(0xFF3A5E2C), deep = C(0xFF2B4826);
        double c = (x0 + x1) / 2.0, r = (x1 - x0) / 2.0;
        int h = img.Height;
        for (int x = x0; x <= x1; x++)
        {
            double u = (x - c) / r;
            int height = (int)Math.Round(3.6 * Math.Sqrt(Math.Max(0, 1 - u * u))) + rng.Next(2) - (rng.Next(3) == 0 ? 1 : 0);
            if (height <= 0) continue;
            for (int k = 0; k < height; k++)
            {
                int y = h - 1 - k;
                var col = k == height - 1 ? (u < 0.1 ? hi : light) : k == 0 ? deep : rng.Next(3) == 0 ? dark : mid;
                img.Set(x, y, col);
            }
            // 줄눈 위로 몇 칸 더
            for (int y = h - 1 - height; y > h - 1 - height - 6 && y > 4; y--)
                if (img.Get(x, y) == GrayMortar && rng.Next(2) == 0) img.Set(x, y, rng.Next(2) == 0 ? light : mid);
        }
        return img;
    }

    // 갓돌에서 흘러내린 물때(아래로 갈수록 옅어짐)
    private static PixelImage Stain(PixelImage img, int x0)
    {
        var water = C(0xFF39433F);
        int[] lengths = [9, 15, 12, 6];
        for (int k = 0; k < lengths.Length; k++)
        {
            int x = x0 + k;
            Tint(img, x, 2, c => Dim(c, .15));
            Tint(img, x, 3, c => Mix(c, water, .3));
            for (int y = 4; y < 4 + lengths[k]; y++)
            {
                double t = .42 * (1 - (y - 4) / (double)lengths[k]);
                Tint(img, x, y, c => Mix(c, water, t));
            }
        }
        return img;
    }

    // 벽돌 반 장이 빠진 어두운 구멍(안쪽 오른벽은 빛을 받음)
    private static PixelImage MissingHalf(PixelImage img, int course, int x0)
    {
        int y0 = 4 + course * 4;
        Color32 deep = C(0xFF17181C), gap = C(0xFF25272C), lit = C(0xFF3D4047);
        for (int y = y0; y < y0 + 3; y++)
            for (int x = x0; x < x0 + 3; x++)
                img.Set(x, y, y == y0 ? deep : x == x0 + 2 ? lit : gap);
        img.Set(x0 + 3, y0, Dim(img.Get(x0 + 3, y0), .3));
        for (int x = x0; x < x0 + 4; x++) Tint(img, x, y0 + 3, c => Lit(c, .15));
        return img;
    }

    // ───────────── 붉은 벽돌 담장 ─────────────

    private static readonly Color32[] RedBricks = [C(0xFFB4472F), C(0xFFA6402A), C(0xFFBF5538), C(0xFF9C3A26), C(0xFFB04E36)];
    private static readonly Color32[] BurntBricks = [C(0xFF6E2C22), C(0xFF7D3427), C(0xFF5E2620)];
    private static readonly Color32 RedMortar = C(0xFFD8CBB3), RedEdge = C(0xFF5C2219);
    private static readonly Color32 CapOutline = C(0xFF6A665E), CapLight = C(0xFFE8E4DA), CapBase = C(0xFFC9C4B8), CapShade = C(0xFF99948A);
    private static readonly Color32 PlinthLight = C(0xFFB8B1A3), PlinthBase = C(0xFF9F988A), PlinthOutline = C(0xFF57524A);
    private const int RedH = 24;

    /// <summary>붉은 벽돌 담장: 콘크리트 갓돌과 돌 굽 사이 벽돌 네 줄. 탄 벽돌·담쟁이·철 난간 창·구멍 무늬 벽·기둥 두 가지.</summary>
    public static StampSet RedBrickFence()
    {
        var burnt = new Lcg(77);
        var shapes = new List<PixelImage>
        {
            RedWall(16, 1),
            RedWall(32, 2),
            RedWall(32, 3, (c, i) => burnt.Next(3) == 0 ? BurntBricks[burnt.Next(BurntBricks.Length)] : null),
            RedWall(16, 4, (c, i) => (c, i) switch { (1, 1) => BurntBricks[0], (3, 0) => BurntBricks[1], _ => null }),
            Ivy(RedWall(32, 5), 51, 4, 20, 8, 21),
            Ivy(RedWall(16, 6), 52, 4, 18, 9),
            Railing(RedWall(32, 7)),
            Pierced(RedWall(32, 8)),
            Pillar(9, plaque: false),
            Pillar(10, plaque: true),
        };
        return Set("Red Brick Fence", shapes);
    }

    private static PixelImage RedWall(int w, uint seed, Func<int, int, Color32?>? special = null)
    {
        var img = new PixelImage(w, RedH);
        var rng = new Lcg(seed + 7);
        for (int x = 0; x < w; x++)
        {
            bool joint = x % 16 == 11;
            img.Set(x, 0, CapOutline);
            img.Set(x, 1, joint ? CapBase : CapLight);
            img.Set(x, 2, joint ? CapShade : rng.Next(6) == 0 ? Dim(CapBase, .07) : CapBase);
            img.Set(x, 3, CapShade);
        }
        Blit(img, BrickField(w, 4, seed, RedBricks, RedMortar, special: special), 0, 4);
        for (int x = 0; x < w; x++)
        {
            img.Set(x, 4, Dim(img.Get(x, 4), .25));
            Plinth(img, x, x % 16 == 5);
        }
        return img;
    }

    // 돌 굽(20~23행)
    private static void Plinth(PixelImage img, int x, bool joint)
    {
        img.Set(x, 20, PlinthLight);
        img.Set(x, 21, joint ? Dim(PlinthBase, .3) : PlinthBase);
        img.Set(x, 22, joint ? Dim(PlinthBase, .35) : Dim(PlinthBase, .12));
        img.Set(x, 23, PlinthOutline);
    }

    private static PixelImage Ivy(PixelImage img, uint seed, int top, int bottom, params int[] starts)
    {
        Color32 dark = C(0xFF1E4A22), mid = C(0xFF2F7A32), light = C(0xFF4FA444), hi = C(0xFF8ACD5E), stem = C(0xFF4B3B24);
        var rng = new Lcg(seed);
        var leaves = new List<(int X, int Y)>();
        foreach (int start in starts)
        {
            int x = start, len = bottom - top - rng.Next(5);
            for (int y = top; y < top + len; y++)
            {
                if (rng.Next(3) == 0) x = Math.Clamp(x + rng.Next(3) - 1, 3, img.Width - 5);
                Put(img, x, y, stem);
                if (rng.Next(y < top + len / 2 ? 2 : 3) == 0) leaves.Add((x - 2 - rng.Next(2), y - 1 + rng.Next(2)));
                if (rng.Next(y < top + len / 2 ? 2 : 3) == 0) leaves.Add((x + 1 + rng.Next(2), y - 1 + rng.Next(2)));
            }
            // 갓돌 아래로 넘쳐 늘어진 잎
            for (int k = -4; k <= 3; k += 2) leaves.Add((start + k, top - 1 + rng.Next(2)));
        }
        foreach (var (x, y) in leaves) Leaf(img, Math.Clamp(x, 1, img.Width - 4), y, hi, light, mid, dark);
        return img;
    }

    // 윗부분(4~10행)을 뚫고 철 난간을 끼움, 아래에 밝은 창턱
    private static PixelImage Railing(PixelImage img)
    {
        int w = img.Width;
        Color32 iron = C(0xFF2B2E37), ironHi = C(0xFF5D6474);
        for (int y = 4; y <= 10; y++)
        {
            for (int x = 4; x <= w - 5; x++) img.Set(x, y, Color32.Transparent);
            img.Set(3, y, RedEdge);
            img.Set(w - 4, y, RedEdge);
        }
        for (int x = 3; x <= w - 4; x++)
        {
            img.Set(x, 11, x == 3 || x == w - 4 ? CapBase : CapLight);
            Tint(img, x, 12, c => Dim(c, .3));
        }
        for (int x = 4; x <= w - 5; x++)
        {
            img.Set(x, 5, iron);
            img.Set(x, 9, iron);
        }
        for (int x = 5; x <= w - 6; x += 3)
            for (int y = 4; y <= 10; y++)
                img.Set(x, y, y is 6 or 7 ? ironHi : iron);
        return img;
    }

    // 둘째 줄 벽돌을 한 장 걸러 빼 낸 구멍 무늬
    private static PixelImage Pierced(PixelImage img)
    {
        Color32 shade = C(0xFF6B2A1E);
        for (int i = 1; BrickX(1, i) < img.Width; i += 2)
        {
            int x0 = BrickX(1, i);
            if (x0 < 2 || x0 + 7 > img.Width - 2) continue;
            for (int y = 8; y <= 10; y++)
                for (int x = x0; x < x0 + 7; x++) img.Set(x, y, Color32.Transparent);
            for (int x = x0; x < x0 + 7; x++) Tint(img, x, 7, c => Dim(c, .25));
            for (int y = 8; y <= 10; y++) img.Set(x0 - 1, y, shade);
            for (int x = x0 - 1; x < x0 + 8; x++) Tint(img, x, 11, c => Lit(c, .2));
        }
        return img;
    }

    private static readonly Color32[] PillarBricks = [C(0xFF9A3826), C(0xFFA23E2A), C(0xFF8E3424), C(0xFFA8452E)];

    // 넓은 기둥: 두 단 갓돌, 양옆 테두리와 밝은/어두운 모서리, 짧은 벽돌, 가운데 장식 띠(또는 돌 명판), 앞으로 나온 굽
    private static PixelImage Pillar(uint seed, bool plaque)
    {
        const int w = 14;
        var img = new PixelImage(w, RedH);
        Blit(img, BrickField(w - 2, 4, seed, PillarBricks, RedMortar, period: 6), 1, 7);
        for (int x = 0; x < w; x++)
        {
            bool side = x == 0 || x == w - 1;
            img.Set(x, 0, CapOutline);
            img.Set(x, 1, side ? CapOutline : x <= 2 ? Lit(CapLight, .4) : CapLight);
            img.Set(x, 2, side ? CapOutline : x >= w - 3 ? CapShade : CapBase);
            img.Set(x, 3, side ? CapOutline : CapShade);
            img.Set(x, 4, CapOutline);
            img.Set(x, 5, side ? CapOutline : x <= 2 ? Lit(CapLight, .3) : x >= w - 3 ? CapBase : CapLight);
            img.Set(x, 6, side ? CapOutline : Dim(CapShade, .15));
            Tint(img, x, 7, c => Dim(c, .3));
            Plinth(img, x, false);
        }
        for (int y = 7; y <= 19; y++)
        {
            img.Set(0, y, RedEdge);
            img.Set(w - 1, y, RedEdge);
            Tint(img, 1, y, c => Lit(c, .25));
            Tint(img, w - 2, y, c => Dim(c, .3));
        }
        if (plaque)
        {
            for (int y = 10; y <= 16; y++)
                for (int x = 3; x <= 10; x++)
                {
                    bool frame = x == 3 || x == 10 || y == 10 || y == 16;
                    img.Set(x, y, frame ? CapOutline : x == 4 || y == 11 ? CapLight : y == 13 && x is >= 5 and <= 8 || y == 14 && x is 5 or 6 ? CapShade : CapBase);
                }
            for (int x = 3; x <= 10; x++) Tint(img, x, 17, c => Dim(c, .25));
        }
        else
        {
            for (int x = 0; x < w; x++)
            {
                bool side = x == 0 || x == w - 1;
                img.Set(x, 13, side ? CapOutline : CapLight);
                img.Set(x, 14, side ? CapOutline : x % 3 == 1 ? CapShade : CapBase);
                img.Set(x, 15, side ? CapOutline : CapShade);
                Tint(img, x, 16, c => Dim(c, .2));
            }
        }
        for (int y = 20; y <= 22; y++)
        {
            img.Set(0, y, Dim(PlinthBase, .35));
            img.Set(w - 1, y, Dim(PlinthBase, .35));
        }
        return img;
    }

    // ───────────── 흰 말뚝 울타리 ─────────────

    private const int PicketH = 24;

    // o 테두리, H 밝은 면, l 바탕, m 그늘, n 못, w 드러난 나무, W 짙은 나무
    private static readonly Dictionary<char, uint> WhitePaint = new()
    {
        ['o'] = 0xFF7D8796, ['H'] = 0xFFFFFFFF, ['l'] = 0xFFECEFF2, ['m'] = 0xFFC6CDD6, ['n'] = 0xFF9AA2AD, ['w'] = 0xFFC9A57A, ['W'] = 0xFF9A7650,
    };

    private static readonly Dictionary<char, uint> WeatheredPaint = new()
    {
        ['o'] = 0xFF737A82, ['H'] = 0xFFEEEEE8, ['l'] = 0xFFDADBD5, ['m'] = 0xFFB7BAB7, ['n'] = 0xFF8C8F92, ['w'] = 0xFFAE9678, ['W'] = 0xFF8A735E,
    };

    private static readonly Dictionary<char, uint> RailPaint = new() { ['o'] = 0xFF7D8796, ['l'] = 0xFFD8DDE3, ['m'] = 0xFFB0B8C3 };

    private enum Piece { Picket, Weathered, Broken, Missing, Post }

    /// <summary>흰 말뚝 울타리: 뾰족한 말뚝, 뒤쪽 가로대 두 개, 기둥. 바랜 말뚝·빠진 말뚝·부러진 말뚝·덩굴꽃 변형.</summary>
    public static StampSet PicketFence()
    {
        var shapes = new List<PixelImage>
        {
            Fence(Piece.Picket),
            Fence(Piece.Picket, Piece.Picket),
            Fence(Piece.Picket, Piece.Picket, Piece.Picket),
            Fence(Piece.Weathered),
            Fence(Piece.Picket, Piece.Weathered),
            Fence(Piece.Picket, Piece.Missing, Piece.Picket),
            Fence(Piece.Broken, Piece.Picket),
            Fence(Piece.Post),
            Climber(Fence(Piece.Picket, Piece.Picket), 8, 31, (0xFFFF8FB4, 0xFFD8406E)),
            Climber(Fence(Piece.Picket, Piece.Picket, Piece.Picket), 15, 32, (0xFF9DB8FF, 0xFF4D6FD8)),
        };
        return Set("Picket Fence", shapes);
    }

    private static PixelImage Fence(params Piece[] pieces)
    {
        int w = 2 + pieces.Sum(p => p == Piece.Post ? 7 : 5) + 2 * (pieces.Length - 1);
        var parts = new List<(PixelImage, int, int)> { (Rails(w), 0, 0) };
        int x = 1;
        foreach (var p in pieces)
        {
            if (p != Piece.Missing) parts.Add((PieceArt(p, (uint)(x * 7 + w)), x, 0));
            else parts.Add((Art(["", "", "", "", "", "", "", "..n..", "", "", "", "", "", "", "", "", "", "", "..n.."], WhitePaint), x, 0));
            x += (p == Piece.Post ? 7 : 5) + 2;
        }
        return Compose(w, PicketH, parts);
    }

    private static PixelImage Rails(int w)
    {
        string[] rows = new string[PicketH];
        for (int y = 0; y < PicketH; y++)
            rows[y] = new string(y switch { 6 or 9 or 17 or 20 => 'o', 7 or 18 => 'l', 8 or 19 => 'm', _ => '.' }, w);
        return Art(rows, RailPaint);
    }

    private static PixelImage PieceArt(Piece piece, uint seed)
    {
        var rows = new List<string>();
        if (piece == Piece.Post)
        {
            rows.AddRange(["..ooo..", ".oHlmo.", "oHHllmo", "ooooooo", "oHHllmo", "ooooooo"]);
            for (int y = rows.Count; y < PicketH - 1; y++) rows.Add(y is 8 or 19 ? "oHlnlmo" : "oHllmmo");
            rows.Add("ooooooo");
            return Art([.. rows], WhitePaint);
        }
        if (piece == Piece.Broken) rows.AddRange([".....", ".....", ".....", ".....", ".....", ".....", "...o.", "..owo", ".owmo", "owlmo"]);
        else rows.AddRange(["..o..", ".oHo.", "oHlmo"]);
        for (int y = rows.Count; y < PicketH - 1; y++) rows.Add(y is 8 or 19 ? "oHnmo" : "oHlmo");
        rows.Add("ooooo");
        if (piece != Piece.Weathered) return Art([.. rows], WhitePaint);
        // 바랜 말뚝: 칠이 벗겨져 나무가 드러난 자리
        var rng = new Lcg(seed);
        for (int k = 0; k < 4; k++)
        {
            int y = 3 + rng.Next(PicketH - 6), x = 1 + rng.Next(3), len = 1 + rng.Next(2);
            for (int j = 0; j < len && y + j < PicketH - 1; j++)
            {
                var r = rows[y + j].ToCharArray();
                if (r[x] != 'n') r[x] = j == 0 ? 'W' : 'w';
                rows[y + j] = new string(r);
            }
        }
        return Art([.. rows], WeatheredPaint);
    }

    // 말뚝 하나를 타고 오르는 덩굴과 꽃
    private static PixelImage Climber(PixelImage img, int x0, uint seed, (uint Light, uint Shade) petals)
    {
        Color32 dark = C(0xFF1E5226), mid = C(0xFF2F8034), light = C(0xFF4FAA44), hi = C(0xFF8AD460), stem = C(0xFF3C6A2A);
        Color32 petal = C(petals.Light), petalShade = C(petals.Shade), center = C(0xFFFFD84A);
        var rng = new Lcg(seed);
        int x = x0;
        var leaves = new List<(int, int)>();
        var flowers = new List<(int, int)>();
        for (int y = PicketH - 2; y >= 4; y--)
        {
            if (rng.Next(3) == 0) x = Math.Clamp(x + rng.Next(3) - 1, x0 - 1, x0 + 1);
            Put(img, x, y, stem);
            if (y % 3 == 0) leaves.Add((rng.Next(2) == 0 ? x - 2 : x + 1, y));
            if (y is 6 or 13 or 19) flowers.Add((x + (y == 13 ? -1 : 1), y));
        }
        foreach (var (lx, ly) in leaves) Leaf(img, lx, ly, hi, light, mid, dark);
        foreach (var (fx, fy) in flowers)
        {
            Put(img, fx, fy - 1, petal);
            Put(img, fx - 1, fy, petal);
            Put(img, fx + 1, fy, petalShade);
            Put(img, fx, fy + 1, petalShade);
            Put(img, fx, fy, center);
        }
        return img;
    }

    // ───────────── 산울타리 ─────────────

    private const int HedgeH = 22;

    // 8칸마다 둥근 봉우리(양 끝 열은 늘 3행에서 시작해 어느 조각끼리 붙여도 윗선이 이어짐)
    private static readonly int[][] Bumps = [[3, 1, 0, 0, 0, 0, 1, 3], [3, 2, 1, 0, 0, 0, 1, 3], [3, 1, 0, 0, 0, 1, 2, 3], [3, 2, 1, 1, 1, 1, 2, 3]];

    // 0 뿌리 그늘, 1 테두리, 2 짙음, 3 바탕, 4 밝음, 5 하이라이트
    private static readonly Color32[] HedgeTones = [C(0xFF1C3A1C), C(0xFF1B4420), C(0xFF285F2A), C(0xFF367A33), C(0xFF4E9A40), C(0xFF7CC25A)];

    private enum HedgeExtra { None, WhiteFlowers, PinkFlowers, Sunlit, Trunk, Berries }

    /// <summary>산울타리: 둥글게 다듬은 잎 윗선, 어두운 밑동. 꽃·볕 든 자리·줄기가 보이는 빈틈·열매 변형.</summary>
    public static StampSet Hedge()
    {
        var shapes = new List<PixelImage>
        {
            HedgeStrip(16, 1),
            HedgeStrip(24, 2),
            HedgeStrip(32, 3),
            HedgeStrip(24, 4, HedgeExtra.WhiteFlowers),
            HedgeStrip(16, 5, HedgeExtra.PinkFlowers),
            HedgeStrip(24, 6, HedgeExtra.Sunlit),
            HedgeStrip(24, 7, HedgeExtra.Trunk),
            HedgeStrip(16, 8, HedgeExtra.Berries),
        };
        return Set("Hedge", shapes);
    }

    private static PixelImage HedgeStrip(int w, uint seed, HedgeExtra extra = HedgeExtra.None)
    {
        var rng = new Lcg(seed);
        int bumps = w / 8;
        var pick = new int[bumps];
        for (int b = 0; b < bumps; b++) pick[b] = rng.Next(Bumps.Length);
        if (!pick.Contains(0)) pick[rng.Next(bumps)] = 0;
        var top = new int[w];
        for (int x = 0; x < w; x++) top[x] = Bumps[pick[x / 8]][x % 8];
        bool Inside(int x, int y) => y >= (x < 0 || x >= w ? 3 : top[x]);

        // 바탕: 위는 밝게, 밑동은 어둡게, 윗선은 테두리, 맨 아래는 뿌리 그늘
        var tone = new int[w, HedgeH];
        for (int y = 0; y < HedgeH; y++)
            for (int x = 0; x < w; x++)
                tone[x, y] = !Inside(x, y) ? -1
                    : y == HedgeH - 1 ? 0
                    : Edge(Inside, x, y) ? 1
                    : y >= HedgeH - 4 ? 2
                    : y - top[x] <= 3 ? 4
                    : 3;
        // 잎 덩이: 줄마다 엇갈린 둥근 덩이, 왼쪽 위는 밝게 오른쪽 아래는 어둡게(아래 덩이가 위 덩이를 덮음)
        for (int row = 0, gy = 2; gy < HedgeH - 3; row++, gy += 4)
            for (int gx = (row & 1) * 3 - 2; gx < w + 3; gx += 6)
            {
                int size = 5 + rng.Next(2), cx = gx + rng.Next(3) - 1, cy = gy + rng.Next(2);
                for (int py = 0; py < size; py++)
                    for (int px = 0; px < size; px++)
                    {
                        int x = cx - size / 2 + px, y = cy - size / 2 + py;
                        if (x < 0 || x >= w || y < 0 || y >= HedgeH - 1 || tone[x, y] < 2 || !InCircle(size, px, py)) continue;
                        int lower = y >= HedgeH - 5 ? 1 : 0, t = px + py - (size - 1);
                        bool rim = !InCircle(size, px + 1, py + 1) && InCircle(size, px, py);
                        tone[x, y] = rim ? 2 : t <= -3 ? 5 - lower : t <= 0 ? 4 - lower : 3 - lower;
                    }
            }
        if (extra == HedgeExtra.Sunlit)
            for (int y = 0; y < HedgeH - 4; y++)
                for (int x = 0; x < w; x++)
                    if (tone[x, y] >= 2 && InEllipse(14, 14, x - 4, y - 1)) tone[x, y] = Math.Min(5, tone[x, y] + 1);
        // 잎 결: 밝은 점 하나, 오른쪽 아래 그늘
        for (int k = 0; k < w * HedgeH / 12; k++)
        {
            int x = rng.Next(w), y = rng.Next(HedgeH - 2);
            if (tone[x, y] < 2 || x + 1 >= w || tone[x + 1, y + 1] < 2) continue;
            tone[x, y] = Math.Min(5, tone[x, y] + 1);
            tone[x + 1, y + 1] = Math.Max(2, tone[x + 1, y + 1] - 1);
        }
        var img = new PixelImage(w, HedgeH);
        for (int y = 0; y < HedgeH; y++)
            for (int x = 0; x < w; x++)
                if (tone[x, y] >= 0) img.Set(x, y, HedgeTones[tone[x, y]]);

        switch (extra)
        {
            case HedgeExtra.WhiteFlowers: Blossoms(img, top, rng, 6, C(0xFFFFFFFF), C(0xFFD8DCE6)); break;
            case HedgeExtra.PinkFlowers: Blossoms(img, top, rng, 4, C(0xFFFF9CC8), C(0xFFE0558E)); break;
            case HedgeExtra.Berries:
                // 둥근 열매 송이(밝은 점, 빨강, 그늘)
                for (int k = 0; k < 5; k++)
                {
                    int x = 2 + rng.Next(w - 5), y = top[x] + 3 + rng.Next(HedgeH - top[x] - 9);
                    foreach (var (bx, by) in new[] { (x, y), (x + 2, y + 1), (x, y + 2) })
                    {
                        Put(img, bx, by, C(0xFFFF6A6A));
                        Put(img, bx + 1, by, C(0xFFD8283A));
                        Put(img, bx, by + 1, C(0xFFD8283A));
                        Put(img, bx + 1, by + 1, C(0xFF8E1424));
                    }
                }
                break;
            case HedgeExtra.Trunk:
                {
                    // 아래쪽 잎이 성긴 빈틈과 그 안의 줄기
                    int cx = w / 2 - 5;
                    Color32 hollow = C(0xFF12261A), hollowDeep = C(0xFF0C1A12), bark = C(0xFF6B4A2E), barkDark = C(0xFF452E1C);
                    for (int y = HedgeH - 9; y < HedgeH - 1; y++)
                        for (int x = cx; x < cx + 10; x++)
                            if (InEllipse(10, 14, x - cx, y - (HedgeH - 9)))
                                img.Set(x, y, y < HedgeH - 6 ? hollowDeep : hollow);
                    for (int y = HedgeH - 8; y < HedgeH; y++)
                    {
                        Put(img, cx + 4, y, bark);
                        Put(img, cx + 5, y, barkDark);
                    }
                    foreach (var (x, y) in new[] { (cx + 3, HedgeH - 6), (cx + 2, HedgeH - 7), (cx + 6, HedgeH - 5), (cx + 7, HedgeH - 6), (cx + 8, HedgeH - 7) })
                        Put(img, x, y, bark);
                    break;
                }
        }
        return img;
    }

    // 작은 꽃(가운데 노랑, 위·왼쪽 꽃잎 밝게)
    private static void Blossoms(PixelImage img, int[] top, Lcg rng, int count, Color32 petal, Color32 shade)
    {
        var yellow = C(0xFFFFD040);
        for (int k = 0; k < count; k++)
        {
            int x = 2 + rng.Next(img.Width - 4), y = top[x] + 3 + rng.Next(HedgeH - top[x] - 8);
            Put(img, x, y - 1, petal);
            Put(img, x - 1, y, petal);
            Put(img, x + 1, y, shade);
            Put(img, x, y + 1, shade);
            Put(img, x, y, yellow);
        }
    }

    // ───────────── 만국기 줄 ─────────────

    private const int BuntH = 13, Slot = 13, FlagLen = 9;

    // 깃발 줄마다 반폭(위 9칸 → 아래 끝 1칸)
    private static readonly int[] FlagHalf = [4, 4, 3, 3, 2, 2, 1, 1, 0];
    private static readonly Color32 Twine = C(0xFF5E4B3A);

    // 바탕, 밝음, 그늘, 테두리
    private static readonly uint[][] FlagColors =
    [
        [0xFFE0383A, 0xFFFF7A6E, 0xFFB02028, 0xFF6E1418], // 0 빨강
        [0xFFF5841F, 0xFFFFB25A, 0xFFC85E10, 0xFF7A3608], // 1 주황
        [0xFFFFD23A, 0xFFFFF08A, 0xFFD9A41A, 0xFF7A5A0A], // 2 노랑
        [0xFF3DBE4E, 0xFF86E07A, 0xFF27903A, 0xFF135024], // 3 초록
        [0xFF22B8B0, 0xFF7AE6DC, 0xFF15867F, 0xFF0A4A47], // 4 청록
        [0xFF3A7BE8, 0xFF86B4FF, 0xFF2556B8, 0xFF102C66], // 5 파랑
        [0xFF8E4FD8, 0xFFC296FF, 0xFF6732A8, 0xFF361660], // 6 보라
        [0xFFFF6FAE, 0xFFFFB0D2, 0xFFD94A88, 0xFF7A1C46], // 7 분홍
        [0xFFF4F1EA, 0xFFFFFFFF, 0xFFCFCBC0, 0xFF7E7A70], // 8 흰색
    ];

    private enum Pat { Plain, Stripes, Band, Dots }

    /// <summary>만국기 줄: 양 끝이 같은 높이인 늘어진 끈에 여러 색 삼각 깃발 1~3장(무늬 없음·세로 줄·가로 띠·물방울).</summary>
    public static StampSet BuntingFlags()
    {
        const int red = 0, orange = 1, yellow = 2, green = 3, teal = 4, blue = 5, purple = 6, pink = 7, white = 8;
        var shapes = new List<PixelImage>
        {
            Bunting((red, Pat.Plain, white)),
            Bunting((blue, Pat.Dots, white)),
            Bunting((yellow, Pat.Stripes, orange)),
            Bunting((green, Pat.Band, white)),
            Bunting((orange, Pat.Plain, white), (teal, Pat.Dots, white)),
            Bunting((pink, Pat.Stripes, white), (purple, Pat.Plain, white)),
            Bunting((white, Pat.Dots, red), (red, Pat.Band, white)),
            Bunting((green, Pat.Plain, white), (yellow, Pat.Dots, red)),
            Bunting((blue, Pat.Plain, white), (yellow, Pat.Plain, white), (red, Pat.Plain, white)),
            Bunting((purple, Pat.Dots, white), (pink, Pat.Plain, white), (teal, Pat.Stripes, white)),
            Bunting((orange, Pat.Band, white), (white, Pat.Plain, blue), (green, Pat.Dots, yellow)),
        };
        return Set("Bunting Flags", shapes);
    }

    private static PixelImage Bunting(params (int Color, Pat Pattern, int Accent)[] flags)
    {
        int w = flags.Length * Slot, sag = flags.Length == 1 ? 2 : 3;
        // 끈: 양 끝 세 칸은 0행에 평평, 가운데로 sag만큼 처짐
        double c = (w - 1) / 2.0, span = c - 2;
        var sy = new int[w];
        for (int x = 0; x < w; x++)
        {
            double u = Math.Abs(x - c) / span;
            sy[x] = u >= 1 ? 0 : (int)Math.Round(sag * (1 - u * u));
        }
        int deficit = BuntH - 1 - Enumerable.Range(0, flags.Length).Max(i => sy[i * Slot + Slot / 2] + FlagLen);
        var img = new PixelImage(w, BuntH);
        for (int i = 0; i < flags.Length; i++)
        {
            var (color, pattern, accent) = flags[i];
            int cx = i * Slot + Slot / 2, topC = sy[cx] + 1, tip = sy[cx] + FlagLen + deficit;
            bool Inside(int x, int y)
            {
                if (x < 0 || x >= w || y <= sy[x] || y > tip) return false;
                int r = Math.Max(0, y - topC) * FlagHalf.Length / (tip - topC + 1);
                return Math.Abs(x - cx) <= FlagHalf[r];
            }
            bool Solid(int x, int y) => Inside(x, y) || x >= 0 && x < w && y == sy[x];
            var pal = FlagColors[color];
            Color32 baseC = C(pal[0]), light = C(pal[1]), shade = C(pal[2]), outline = C(pal[3]);
            Color32 acc = C(FlagColors[accent][0]), accLight = C(FlagColors[accent][1]);
            for (int y = 0; y < BuntH; y++)
                for (int x = cx - 5; x <= cx + 5; x++)
                {
                    if (!Inside(x, y)) continue;
                    Color32 col;
                    if (Edge(Solid, x, y)) col = outline;
                    else
                    {
                        int dx = x - cx, dy = y - sy[x];
                        bool marked = pattern switch
                        {
                            Pat.Stripes => ((dx + y - topC) % 3 + 3) % 3 == 0,
                            Pat.Band => y - topC is 2 or 3,
                            Pat.Dots => (dx, y - topC) is (-2, 1) or (1, 1) or (0, 3) or (-1, 5) or (2, 0),
                            _ => false,
                        };
                        bool leftLit = !Inside(x - 2, y) || dy == 1;
                        bool rightShade = !Inside(x + 2, y);
                        col = marked ? (leftLit ? accLight : acc) : leftLit ? light : rightShade ? shade : baseC;
                        if (marked && rightShade) col = Dim(acc, .12);
                    }
                    img.Set(x, y, col);
                }
        }
        for (int x = 0; x < w; x++) img.Set(x, sy[x], Twine);
        return img;
    }
}
