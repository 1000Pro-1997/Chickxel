using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 판타지 소품 모양 펜 세트: 뼈·해골, 물약병, 수정, 비석, 호박, 그리고 서재 소품(쌓은 책, 세운 책, 펼친 책, 종이·편지, 두루마리, 깃펜).
/// 던전 바닥·연금술 탁자·동굴·묘지·추수 밭·서재에 흩어 찍는 소품들. 빛은 왼쪽 위에서, 테두리는 바탕색의 어두운 색.
/// 같은 코드면 늘 같은 모양이다(난수는 씨앗 고정 해시만 씀).
/// </summary>
internal static class FantasyPropsShapeSets
{
    // ───────────── 공용 ─────────────

    // 톤 배열: [0] 테두리, [1] 그늘, [2] 중간, [3] 바탕, [4] 밝은 곳
    private static Color32 C(uint argb) => new(argb);

    /// <summary>모양 안 칸의 색: 테두리, 아니면 가까운 바깥 방향(법선)으로 왼쪽 위는 밝게, 오른쪽 아래는 어둡게.</summary>
    private static Color32 ShadeAt(Func<int, int, bool> inside, int x, int y, uint[] t)
    {
        if (!inside(x, y)) return Color32.Transparent;
        if (Edge(inside, x, y)) return C(t[0]);
        double nx = 0, ny = 0;
        int near = 99;
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int d2 = dx * dx + dy * dy;
                if (d2 == 0 || d2 > 10 || inside(x + dx, y + dy)) continue;
                nx += (double)dx / d2;
                ny += (double)dy / d2;
                near = Math.Min(near, d2);
            }
        if (near > 10) return C(t[3]);
        double len = Math.Sqrt(nx * nx + ny * ny);
        double dot = len < 1e-6 ? 0 : -(nx + ny) / (len * Math.Sqrt(2));
        if (near <= 5) return C(dot > 0.35 ? t[4] : dot < -0.35 ? t[1] : dot < -0.05 ? t[2] : t[3]);
        return C(dot < -0.35 ? t[2] : t[3]);
    }

    private static PixelImage Shaded(int w, int h, Func<int, int, bool> inside, uint[] t) => Paint(w, h, (x, y) => ShadeAt(inside, x, y, t));

    /// <summary>씨앗 고정 정수 해시(0 이상).</summary>
    private static int Hash(int x, int y, int seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return (int)((h ^ (h >> 16)) & 0x7FFFFFFF);
    }

    /// <summary>원본 그림을 가운데 기준으로 돌린다(돌린 모양의 둘레는 <paramref name="outline"/>, 안은 원본에서 한 칸 안쪽 색을 집음).</summary>
    private static PixelImage Rotate(PixelImage src, double deg, uint outline)
    {
        double a = deg * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        int size = (int)Math.Ceiling(Math.Sqrt(src.Width * src.Width + src.Height * src.Height)) + 2;
        double cx = src.Width / 2.0, cy = src.Height / 2.0, oc = size / 2.0;
        (double U, double V) Local(int x, int y)
        {
            double dx = x + 0.5 - oc, dy = y + 0.5 - oc;
            return (dx * cos + dy * sin + cx, -dx * sin + dy * cos + cy);
        }
        Color32 Sample(double u, double v)
        {
            int iu = (int)Math.Floor(u), iv = (int)Math.Floor(v);
            return src.Contains(iu, iv) ? src.Get(iu, iv) : Color32.Transparent;
        }
        bool Inside(int x, int y)
        {
            var (u, v) = Local(x, y);
            return Sample(u, v).A > 0;
        }
        return Paint(size, size, (x, y) =>
        {
            if (!Inside(x, y)) return Color32.Transparent;
            if (Edge(Inside, x, y)) return C(outline);
            var (u, v) = Local(x, y);
            var c = Sample(u, v);
            if (c.A == 0 || c == C(outline))
            {
                // 원본의 테두리를 집었으면 가운데 쪽으로 한 칸
                double du = Math.Sign(cx - u), dv = Math.Sign(cy - v);
                var inner = Sample(u + du, v + dv);
                if (inner.A > 0 && inner != C(outline)) c = inner;
                else
                {
                    inner = Sample(u + du, v);
                    if (inner.A > 0 && inner != C(outline)) c = inner;
                    else
                    {
                        inner = Sample(u, v + dv);
                        if (inner.A > 0) c = inner;
                    }
                }
            }
            return c;
        });
    }

    /// <summary>왼쪽 반(마지막 열이 가운데)을 펼치되 오른쪽 반은 빛 반대편이라 한 단계 어둡게(H→l, m→d, e→c).</summary>
    private static string[] LitSymmetric(string[] left)
        => [.. left.Select(row => row + new string(row[..^1].Reverse().Select(ch => ch switch { 'H' => 'l', 'm' => 'd', 'e' => 'c', _ => ch }).ToArray()))];

    private static void Line(HashSet<(int X, int Y)> set, int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            set.Add((x0, y0));
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    // ───────────── 뼈·해골 ─────────────

    private static readonly uint[] BoneT = [0xFF5C4B3B, 0xFF9A8670, 0xFFC4B49A, 0xFFE6DCC6, 0xFFFFF8E8];

    private static readonly Dictionary<char, uint> BoneP = new()
    {
        ['o'] = BoneT[0], ['d'] = BoneT[1], ['m'] = BoneT[2], ['l'] = BoneT[3], ['H'] = BoneT[4],
        ['k'] = 0xFF2E2420,
        // 뿔
        ['a'] = 0xFF3A2A1C, ['b'] = 0xFF6A5034, ['c'] = 0xFF94744C, ['e'] = 0xFFC0A070,
    };

    private static readonly string[] SkullFront =
    [
        "....ooooo....",
        "..ooHHllmoo..",
        ".oHHllllllmo.",
        "oHllllllllmdo",
        "oHllllllllmdo",
        "olkkkllmkkkdo",
        "olkkklmlkkkdo",
        "omlkllkllkmdo",
        ".omllkkklmdo.",
        ".omllllllmdo.",
        "..olololomo..",
        "...ooooooo...",
    ];

    private static readonly string[] SkullSmall =
    [
        "..ooooo..",
        ".oHHllmo.",
        "oHllllmdo",
        "olkklkkdo",
        "olkklkkdo",
        ".omlklmo.",
        "..olomo..",
        "..ooooo..",
    ];

    // 3/4로 돌아본 해골(먼 눈은 작게 오른쪽 끝에)
    private static readonly string[] SkullSide =
    [
        "...oooooo....",
        ".ooHHHlllmo..",
        "oHHllllllllmo",
        "oHllllllllldo",
        "oHlkkklllkkdo",
        "ollkkklllkkdo",
        "omllkllkllmdo",
        ".omllllkkldo.",
        "..omdmmmmmdo.",
        "..oololololo.",
        "...oooooooo..",
    ];

    // 뿔 달린 짐승 해골(가운데 열이 마지막 글자)
    private static readonly string[] AnimalSkull = LitSymmetric(
    [
        "a.........",
        "aea.......",
        "aeca......",
        ".aeca.....",
        ".aecbaoooo",
        "..aecboHHl",
        "...aaoHlll",
        "....okklll",
        "....okkmll",
        ".....omlll",
        ".....omlll",
        "......omll",
        "......okll",
        "......omll",
        ".......ooo",
    ]);

    private static Func<int, int, bool> BoneMask(double x0, double y0, double x1, double y1, double r, double knob)
    {
        double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy), ux = dx / len, uy = dy / len, px = -uy, py = ux;
        double o = knob * 0.8;
        (double X, double Y)[] knobs =
        [
            (x0 + px * o - ux * 0.4, y0 + py * o - uy * 0.4), (x0 - px * o - ux * 0.4, y0 - py * o - uy * 0.4),
            (x1 + px * o + ux * 0.4, y1 + py * o + uy * 0.4), (x1 - px * o + ux * 0.4, y1 - py * o + uy * 0.4),
        ];
        return (x, y) =>
        {
            double cx = x + 0.5, cy = y + 0.5;
            double t = Math.Clamp(((cx - x0) * ux + (cy - y0) * uy) / len, 0, 1);
            double qx = x0 + dx * t - cx, qy = y0 + dy * t - cy;
            if (qx * qx + qy * qy <= r * r) return true;
            foreach (var k in knobs)
                if ((cx - k.X) * (cx - k.X) + (cy - k.Y) * (cy - k.Y) <= knob * knob) return true;
            return false;
        };
    }

    private static PixelImage Bone(double x0, double y0, double x1, double y1, double r = 1.5, double knob = 1.8)
    {
        int w = (int)Math.Ceiling(Math.Max(x0, x1) + knob * 2 + 2), h = (int)Math.Ceiling(Math.Max(y0, y1) + knob * 2 + 2);
        return Shaded(w, h, BoneMask(x0, y0, x1, y1, r, knob), BoneT);
    }

    // 위에서 본 갈비뼈(가운데 등뼈에 양쪽으로 휘어 내려가는 갈비)
    private static PixelImage RibCage()
    {
        const int c = 10, w = 21, h = 17;
        var bone = new HashSet<(int X, int Y)>();
        Line(bone, c, 1, c, 15);
        (int Y, int R)[] ribs = [(3, 8), (7, 8), (11, 6)];
        foreach (var (y0, r) in ribs)
            foreach (int s in new[] { -1, 1 })
            {
                Line(bone, c, y0, c + s * (r - 3), y0);
                Line(bone, c + s * (r - 3), y0, c + s * r, y0 + 2);
                Line(bone, c + s * r, y0 + 2, c + s * r, y0 + 3);
            }
        return Paint(w, h, (x, y) =>
        {
            if (bone.Contains((x, y)))
            {
                if (x == c) return C(y % 4 == 1 ? BoneT[2] : BoneT[3]);
                bool top = !bone.Contains((x, y - 1));
                if (x < c) return C(top ? BoneT[4] : BoneT[3]);
                return C(top ? BoneT[3] : BoneT[2]);
            }
            bool near = bone.Contains((x - 1, y)) || bone.Contains((x + 1, y)) || bone.Contains((x, y - 1)) || bone.Contains((x, y + 1));
            return near ? C(BoneT[0]) : Color32.Transparent;
        });
    }

    /// <summary>뼈·해골: 앞·옆 해골, 작은 해골, 여러 길이·방향의 뼈, 엇갈린 뼈, 뼈 무더기, 갈비뼈, 뿔 달린 짐승 해골, 뼛조각. 던전 바닥에.</summary>
    public static StampSet Bones()
    {
        var crossA = Bone(3, 3, 17, 17);
        var crossed = Compose(crossA.Width, crossA.Height, [(crossA, 0, 0), (FlipX(crossA), 0, 0)]);
        var pile = Compose(30, 22,
        [
            (Bone(3, 3, 22, 5), 0, 12),
            (Bone(3, 5, 19, 3), 4, 7),
            (Bone(3, 3, 8, 8, 1.3, 1.6), 19, 8),
            (Art(SkullSmall, BoneP), 8, 3),
        ]);
        var brokenMask = BoneMask(3, 4, 16, 4, 1.5, 1.8);
        var broken = Shaded(14, 9, (x, y) => brokenMask(x, y) && x <= 10 + (y * 5 % 3) - 1, BoneT);
        var shapes = new List<PixelImage>
        {
            Art(SkullFront, BoneP),
            Art(SkullSmall, BoneP),
            Bone(3, 4, 19, 4),
            Bone(3, 3, 9, 4, 1.2, 1.5),
            crossed,
            Art(AnimalSkull, BoneP),
            RibCage(),
        };
        shapes.AddRange(Mirrored(
        [
            Art(SkullSide, BoneP),
            Bone(3, 3, 13, 13),
            Bone(4, 3, 8, 15),
            pile,
            broken,
        ]));
        return Set("Bones & Skulls", shapes);
    }

    // ───────────── 물약병 ─────────────

    private static readonly uint[] RedLiquid = [0xFF5A0E1A, 0xFF9E1A2A, 0xFFC8283A, 0xFFE8484E, 0xFFFF9A9A];
    private static readonly uint[] BlueLiquid = [0xFF0E1E5A, 0xFF1E3E9E, 0xFF2E62D0, 0xFF4A8CF0, 0xFFA8D0FF];
    private static readonly uint[] GreenLiquid = [0xFF0E4A1E, 0xFF1E7A2E, 0xFF2EA040, 0xFF56CC5A, 0xFFB4F2A0];
    private static readonly uint[] PurpleLiquid = [0xFF3A0E5A, 0xFF5E1E8E, 0xFF8030B8, 0xFFA850E0, 0xFFDAA8FF];
    private static readonly uint[] GoldLiquid = [0xFF5E3A06, 0xFFA06A10, 0xFFD09A20, 0xFFF0C440, 0xFFFFF0A0];
    private static readonly uint[] CorkT = [0xFF4A2E18, 0xFF7A4E2A, 0xFFA0703E, 0xFFC49060, 0xFFDEB080];
    private const uint GlassRim = 0xFF3A4656, GlassEmpty = 0x60D4ECF6, GlassShine = 0xD8FFFFFF;

    private static PixelImage Potion(int w, int h, Func<int, int, bool> glass, (int X0, int Y0, int X1, int Y1) cork, int liquidTop, uint[] liq, (int X, int Y)[] shine)
    {
        bool Cork(int x, int y) => x >= cork.X0 && x <= cork.X1 && y >= cork.Y0 && y <= cork.Y1;
        return Paint(w, h, (x, y) =>
        {
            if (Cork(x, y)) return ShadeAt(Cork, x, y, CorkT);
            if (!glass(x, y)) return Color32.Transparent;
            if (Edge(glass, x, y)) return C(y >= liquidTop ? liq[0] : GlassRim);
            if (shine.Contains((x, y))) return C(GlassShine);
            if (y < liquidTop) return C(GlassEmpty);
            if (y == liquidTop) return C(liq[4]);
            return ShadeAt(glass, x, y, liq);
        });
    }

    private static PixelImage RoundFlask(uint[] liq) => Potion(13, 19,
        (x, y) => (y == 4 && x >= 3 && x <= 9) || (y >= 4 && y <= 8 && x >= 4 && x <= 8) || InCircle(13, x, y - 6),
        (4, 0, 8, 3), 11, liq, [(3, 9), (2, 10), (2, 11), (4, 8)]);

    private static PixelImage MiniFlask(uint[] liq) => Potion(9, 14,
        (x, y) => (y == 3 && x >= 2 && x <= 6) || (y >= 3 && y <= 6 && x >= 3 && x <= 5) || InCircle(9, x, y - 5),
        (3, 0, 5, 2), 8, liq, [(2, 7), (2, 8)]);

    private static PixelImage Vial(uint[] liq) => Potion(7, 19,
        (x, y) => (y == 3 && x >= 0 && x <= 6) || (y >= 3 && y <= 17 && x >= 1 && x <= 5) || (y == 18 && x >= 2 && x <= 4),
        (2, 0, 4, 2), 8, liq, [(2, 5), (2, 6), (2, 9), (2, 10), (2, 11)]);

    private static PixelImage SquareBottle(uint[] liq) => Potion(11, 19,
        (x, y) => (y == 3 && x >= 2 && x <= 8) || (y >= 3 && y <= 6 && x >= 3 && x <= 7) || (y == 6 && x >= 1 && x <= 9)
            || (y >= 7 && y <= 18 && !((x == 0 || x == 10) && (y == 7 || y == 18))),
        (3, 0, 7, 2), 10, liq, [(2, 8), (2, 9), (2, 12), (2, 13), (2, 14)]);

    private static PixelImage ConeFlask(uint[] liq) => Potion(13, 19,
        (x, y) => (y == 3 && x >= 3 && x <= 9) || (y >= 3 && y <= 8 && x >= 4 && x <= 8)
            || (y >= 8 && y <= 18 && Math.Abs(x + 0.5 - 6.5) <= 2.5 + (y - 8) * 0.4),
        (4, 0, 8, 2), 13, liq, [(4, 10), (3, 12), (3, 14)]);

    // 목이 깨져 나간 둥근 플라스크: 위는 들쭉날쭉, 밑에 쏟아진 웅덩이, 옆에 유리 조각
    private static PixelImage BrokenFlask(uint[] liq)
    {
        int[] jag = [3, 3, 2, 0, 1, 3, 5, 4, 2, 1, 0, 2, 3];
        bool Glass(int x, int y) => x >= 0 && x < 13 && InCircle(13, x, y - 1) && y >= 2 + jag[x];
        var flask = Paint(13, 14, (x, y) =>
        {
            if (!Glass(x, y)) return Color32.Transparent;
            if (Edge(Glass, x, y)) return C(y >= 8 ? liq[0] : GlassRim);
            if ((x == 2 && y == 6) || (x == 3 && y == 5)) return C(GlassShine);
            if (y < 8) return C(GlassEmpty);
            if (y == 8) return C(liq[4]);
            return ShadeAt(Glass, x, y, liq);
        });
        uint pool = liq[3] & 0xFFFFFF, poolEdge = liq[1] & 0xFFFFFF;
        bool Pool(int x, int y) => InEllipse(21, 5, x, y);
        var puddle = Paint(21, 5, (x, y) =>
        {
            if (!Pool(x, y)) return Color32.Transparent;
            if (Edge(Pool, x, y)) return new Color32(0xD0000000 | poolEdge);
            if (y == 1 && x >= 14 && x <= 16) return C(liq[4] & 0xFFFFFF | 0xD0000000);
            return new Color32(0xB8000000 | pool);
        });
        var glassP = new Dictionary<char, uint> { ['o'] = GlassRim, ['G'] = GlassShine, ['g'] = 0xA0D4ECF6 };
        var shardA = Art([".o..", "oGo.", "oggo", "oooo"], glassP);
        var shardB = Art(["oo.", "oGo", ".oo"], glassP);
        return Compose(23, 17, [(puddle, 1, 11), (flask, 4, 0), (shardA, 18, 10), (shardB, 0, 13)]);
    }

    /// <summary>물약병: 둥근 플라스크·작은 플라스크·긴 시약병·네모병·원뿔 플라스크(빨강·파랑·초록·보라·금색 물약, 코르크), 깨진 병, 빛나는 마법 물약.</summary>
    public static StampSet Potions()
    {
        var shapes = new List<PixelImage>
        {
            RoundFlask(RedLiquid),
            RoundFlask(BlueLiquid),
            RoundFlask(GreenLiquid),
            MiniFlask(GoldLiquid),
            MiniFlask(RedLiquid),
            Vial(PurpleLiquid),
            Vial(GreenLiquid),
            Vial(BlueLiquid),
            SquareBottle(GreenLiquid),
            SquareBottle(PurpleLiquid),
            ConeFlask(BlueLiquid),
            ConeFlask(GoldLiquid),
            Glow(RoundFlask(PurpleLiquid), 0xFFC890FF, fromRow: 4),
            Glow(MiniFlask(GreenLiquid), 0xFF90FFA0, fromRow: 3),
        };
        shapes.Add(BrokenFlask(RedLiquid));
        shapes.Add(BrokenFlask(BlueLiquid));
        return Set("Potions & Bottles", shapes);
    }

    // ───────────── 수정 ─────────────

    // 수정 톤: [0] 테두리, [1] 그늘 면, [2] 어두운 중간, [3] 앞면, [4] 빛 받는 면, [5] 거의 흰 빛
    private static readonly uint[] Amethyst = [0xFF2A0F45, 0xFF4E1F7A, 0xFF6E34A8, 0xFF9056D0, 0xFFB98AEE, 0xFFF0E2FF];
    private static readonly uint[] Sapphire = [0xFF0B1F4D, 0xFF153F8C, 0xFF2462C0, 0xFF3E8CE6, 0xFF86C2FA, 0xFFE4F4FF];
    private static readonly uint[] Emerald = [0xFF07382A, 0xFF0F6648, 0xFF1B8E62, 0xFF2FB884, 0xFF7AE2B4, 0xFFE2FFF2];
    private static readonly uint[] RoseQuartz = [0xFF4E1236, 0xFF8A2A5C, 0xFFBE4A84, 0xFFE07AAC, 0xFFF8B4D2, 0xFFFFEEF6];
    private static readonly uint[] Quartz = [0xFF34485C, 0xFF6A8298, 0xFF94ACC0, 0xFFBED2E0, 0xFFE2EEF6, 0xFFFFFFFF];
    private static readonly uint[] Citrine = [0xFF4E2E04, 0xFF8A5A0A, 0xFFBE8614, 0xFFE6B22C, 0xFFF8D86C, 0xFFFFF8D8];
    private static readonly uint[] CaveRockT = [0xFF1C1A20, 0xFF2E2B33, 0xFF433F4A, 0xFF5A5562, 0xFF7A7484];

    /// <summary>
    /// 육각 기둥 수정 하나: 밑동 (X, Y), 기울기 Angle(도, +면 오른쪽으로), 반폭 Half, 길이 Len,
    /// 뾰족 끝 꼭짓점의 가로 치우침 Apex(-1 왼쪽 ~ 1 오른쪽: 큰 면 하나와 가는 면 하나), Back이면 뒤쪽이라 한 톤 어둡게.
    /// </summary>
    private readonly record struct Prism(double X, double Y, double Angle, double Half, double Len, double Apex = -0.25, bool Back = false);

    /// <summary>
    /// 수정 기둥 그림: 왼쪽 빛 받는 면(바깥 모서리에 또렷한 빛줄), 앞면(왼쪽 모서리 빛, 가는 가로 결, 큰 수정엔 속 그림자 줄),
    /// 오른쪽 그늘 면(밑동 쪽은 빛이 비쳐 밝게), 끝은 같은 면들이 꼭짓점으로 모이는 깎인 면(한 단계 밝게).
    /// </summary>
    private static PixelImage PrismImage(int w, int h, Prism p, uint[] t)
    {
        double th = p.Angle * Math.PI / 180, ax = Math.Sin(th), ay = -Math.Cos(th), qx = Math.Cos(th), qy = Math.Sin(th);
        double tip = p.Half * 1.7, shaft = p.Len - tip;
        const double e1 = -0.42, e2 = 0.36;
        (double S, double T) Local(int x, int y)
        {
            double dx = x + 0.5 - p.X, dy = y + 0.5 - p.Y;
            return (dx * ax + dy * ay, (dx * qx + dy * qy) / p.Half);
        }
        bool In(int x, int y)
        {
            var (s, tt) = Local(x, y);
            if (s < 0 || s > p.Len) return false;
            if (s <= shaft) return Math.Abs(tt) <= 1;
            double k = (s - shaft) / tip;
            return tt >= -1 + (1 + p.Apex) * k && tt <= 1 - (1 - p.Apex) * k;
        }
        int Face(double s, double tt)
        {
            double k = s <= shaft ? 0 : (s - shaft) / tip;
            double a = e1 + (p.Apex - e1) * k, b = e2 + (p.Apex - e2) * k;
            return tt < a ? 0 : tt < b ? 1 : 2;
        }
        bool Phantom(double s, double tt)
        {
            if (p.Back || p.Len < 16) return false;
            double top = shaft * 0.6, ptip = tip * 0.8, k = (s - (top - ptip)) / ptip;
            if (k < 0 || k > 1) return false;
            double l = -0.75 + (0.75 + p.Apex) * k, r = 0.75 - (0.75 - p.Apex) * k;
            return Math.Abs(tt - l) * p.Half < 0.5 || Math.Abs(tt - r) * p.Half < 0.5;
        }
        int shift = p.Back ? 1 : 0;
        return Paint(w, h, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return C(t[0]);
            var (s, tt) = Local(x, y);
            bool tipZone = s > shaft;
            int face = Face(s, tt), tone;
            if (face == 0)
            {
                bool rim = In(x - 1, y) && Edge(In, x - 1, y) && s > p.Len * 0.2;
                tone = tipZone || rim ? 5 : 4;
            }
            else if (face == 1)
            {
                var (ls, lt) = Local(x - 1, y);
                tone = tipZone ? 4 : 3;
                if (!tipZone && s > p.Len * 0.45 && Face(ls, lt) == 0) tone = 4;
                else if (!tipZone && Phantom(s, tt)) tone = 4;
            }
            else
            {
                tone = tipZone ? 2 : s < p.Len * 0.35 ? 2 : 1;
            }
            return C(t[Math.Max(1, tone - shift)]);
        });
    }

    private static bool InPolygon((double X, double Y)[] poly, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > y) != (poly[j].Y > y) && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    /// <summary>모난 동굴 바위 덩이(다각형, 왼쪽 위 빛): 가장 높은 꼭짓점에서 오른쪽 아래로 그은 선 오른쪽은 그늘 면, 군데군데 얼룩.</summary>
    private static PixelImage RockChunk(int w, int h, (double X, double Y)[] poly, int seed)
    {
        bool In(int x, int y) => y < h && InPolygon(poly, x + 0.5, y + 0.5);
        var peak = poly.MinBy(q => q.Y);
        return Paint(w, h, (x, y) =>
        {
            var c = ShadeAt(In, x, y, CaveRockT);
            if (c.A == 0 || Edge(In, x, y)) return c;
            bool shaded = x + 0.5 > peak.X + (y + 0.5 - peak.Y) * 0.8;
            if (shaded) c = c == C(CaveRockT[4]) || c == C(CaveRockT[3]) ? C(CaveRockT[2]) : C(CaveRockT[1]);
            if (Hash(x, y, seed) % 9 == 0) return C(shaded ? CaveRockT[1] : CaveRockT[2]);
            return c;
        });
    }

    /// <summary>수정 무리: 뒤쪽 수정 → 큰 수정 → 앞쪽 수정 순서로 겹치고, 바위 덩이(뒤·앞)가 밑동을 덮는다.</summary>
    private static PixelImage Cluster(int w, int h, uint[] t, Prism[] prisms, params (double X, double Y)[][] rocks)
    {
        var parts = prisms.Select(p => (PrismImage(w, h, p, t), 0, 0)).ToList();
        for (int i = 0; i < rocks.Length; i++) parts.Add((RockChunk(w, h, rocks[i], i + 1), 0, 0));
        return Compose(w, h, parts);
    }

    private static PixelImage LargeCluster(uint[] t) => Cluster(30, 30, t,
    [
        new(10, 26, -27, 2.2, 14, 0.2, true),
        new(20, 26, 27, 2.4, 15, -0.3, true),
        new(15.5, 25, 0, 1.8, 9, 0.3, true),
        new(14.5, 28, 0, 3.8, 26, -0.3),
        new(8, 28, -45, 2.2, 11, 0.1),
        new(22, 28, 45, 2.6, 13, -0.2),
        new(18, 29, 27, 1.6, 7, 0),
    ],
    [(2, 30), (3, 27), (6, 24), (10, 22), (14, 21), (19, 22), (23, 23.5), (26, 26), (27, 30)],
    [(16, 30), (17, 27.5), (20, 26), (24, 26.5), (26, 28.5), (26, 30)]);

    private static PixelImage LargeClusterB(uint[] t) => Cluster(30, 30, t,
    [
        new(19, 26, 27, 2.2, 15, -0.2, true),
        new(11, 26, -18, 2.6, 17, 0.25, true),
        new(15.5, 28, 0, 3.6, 24, 0.3),
        new(7, 28, -45, 2.4, 13, 0.1),
        new(23, 28, 45, 2.0, 10, 0),
        new(11, 29, -27, 1.6, 8, 0.2),
    ],
    [(2, 30), (3, 26), (8, 23.5), (14, 22.5), (20, 23), (26, 25), (28, 30)],
    [(3, 30), (4, 27.5), (8, 26.5), (11, 28), (11, 30)]);

    private static PixelImage MediumCluster(uint[] t) => Cluster(22, 22, t,
    [
        new(7, 18, -27, 1.8, 10, 0.2, true),
        new(15, 18, 27, 1.9, 11, -0.2, true),
        new(11, 20, 0, 3.0, 18, -0.25),
        new(16, 20, 45, 1.8, 8, 0),
        new(6, 20, -45, 1.6, 6, 0.1),
    ],
    [(1, 22), (2, 19), (6, 16.5), (11, 15.5), (16, 16.5), (20, 19), (21, 22)],
    [(13, 22), (14, 20), (17, 19), (20, 20), (20, 22)]);

    private static PixelImage MediumClusterB(uint[] t) => Cluster(22, 22, t,
    [
        new(14, 18, 27, 1.8, 11, -0.2, true),
        new(8, 18, -18, 1.9, 12, 0.2, true),
        new(10.5, 20, 0, 2.8, 17, 0.25),
        new(5, 20, -45, 1.8, 8, 0.1),
        new(15, 20, 27, 1.5, 6, 0),
    ],
    [(1, 22), (2, 18.5), (7, 16), (13, 15.5), (18, 17), (21, 22)],
    [(2, 22), (3, 20), (6, 19), (8, 20.5), (8, 22)]);

    private static PixelImage SmallCluster(uint[] t) => Cluster(15, 15, t,
    [
        new(10.5, 13, 27, 1.7, 9, -0.1, true),
        new(7.5, 13, 0, 2.4, 12, -0.2),
        new(4, 13, -45, 1.6, 9, 0.2),
    ],
    [(1, 15), (2, 12.5), (5, 10.5), (10, 10.5), (13, 12.5), (14, 15)]);

    private static PixelImage SmallClusterB(uint[] t) => Cluster(15, 15, t,
    [
        new(4.5, 13, -27, 1.6, 10, 0.2, true),
        new(8, 13, 0, 2.3, 11, 0.25),
        new(11.5, 13, 45, 1.5, 8, 0),
    ],
    [(1, 15), (2, 12.5), (6, 11), (11, 10.5), (13.5, 12.5), (14, 15)]);

    /// <summary>수정: 동굴 바위에서 부채꼴로 솟은 자수정·청수정·녹수정·장미 수정·맑은 수정·황수정 무리(작은·중간·큰), 몇은 은은하게 빛남.</summary>
    public static StampSet Crystals() => Set("Crystals",
    [
        LargeCluster(Amethyst),
        LargeClusterB(Quartz),
        MediumCluster(Sapphire),
        MediumClusterB(RoseQuartz),
        MediumCluster(Emerald),
        MediumClusterB(Citrine),
        SmallCluster(Amethyst),
        SmallClusterB(Citrine),
        SmallCluster(RoseQuartz),
        SmallClusterB(Quartz),
        Glow(LargeClusterB(Emerald), Emerald[4]),
        Glow(MediumCluster(Amethyst), Amethyst[4]),
    ]);

    // ───────────── 비석 ─────────────

    // 돌 톤 세 가지: 푸른 회색, 따뜻한 회갈색, 어두운 점판암
    private static readonly uint[] StoneT = [0xFF34363F, 0xFF62646F, 0xFF878995, 0xFFAAACB6, 0xFFD2D4DC];
    private static readonly uint[] WarmStoneT = [0xFF3E3830, 0xFF6E665A, 0xFF948A7C, 0xFFB6AC9C, 0xFFDAD2C4];
    private static readonly uint[] SlateT = [0xFF22252E, 0xFF40444F, 0xFF575C69, 0xFF707684, 0xFF9AA0AE];
    private static readonly uint[] MossT = [0xFF2E4A1C, 0xFF4A7228, 0xFF6E9A38, 0xFF9CC456];
    private static readonly uint[] DirtT = [0xFF2A1E14, 0xFF46321F, 0xFF5C4229, 0xFF725334, 0xFF8E6C46];

    private enum Head { Round, Arch, Flat, Cross, Celtic }

    private static bool HeadShape(Head head, double w, double h, double u, double v, bool chip)
    {
        // u: 가운데에서 가로, v: 밑에서 위로 음수(-h가 꼭대기)
        double hw = w / 2;
        if (v > 0 || v < -h) return false;
        if (chip && (u - hw) * (u - hw) + (v + h * 0.75) * (v + h * 0.75) < 5) return false;
        switch (head)
        {
            case Head.Round:
                if (Math.Abs(u) > hw) return false;
                double cy = -h + hw;
                return v >= cy || u * u + (v - cy) * (v - cy) <= hw * hw;
            case Head.Arch:
                if (Math.Abs(u) > hw) return false;
                double ay = -h + hw * 1.3;
                return v >= ay || Math.Abs(u) <= hw * (1 - (ay - v) / (hw * 1.3)) * 1.25;
            case Head.Flat:
                if (Math.Abs(u) > hw) return false;
                return !(v < -h + 1.5 && Math.Abs(u) > hw - 1.5);
            case Head.Cross:
                double arm = -h + h * 0.3;
                return Math.Abs(u) <= 2.1 || (v >= arm - 2 && v <= arm + 2 && Math.Abs(u) <= hw);
            default: // Celtic: 굵기 3칸 십자 + 고리, 고리 안쪽은 움푹(Tombstone에서 어둡게)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (Math.Abs(dx) + Math.Abs(dy) <= 1 && CelticBody(u + dx, v + dy, h)) return true;
                double cu = Math.Round(u), cv = Math.Round(v) + h - 7;
                return cu * cu + cv * cv <= 5.8 * 5.8;
        }
    }

    /// <summary>켈트 십자가의 돌 부분(테두리 안쪽): 가운데가 꼭대기에서 7칸 아래인 굵기 3칸 십자와 반지름 4.6~5.8 고리.</summary>
    private static bool CelticBody(double u, double v, double h)
    {
        double cu = Math.Round(u), cv = Math.Round(v) + h - 7, r = Math.Sqrt(cu * cu + cv * cv);
        if (v > 0 || cv < -6) return false;
        return Math.Abs(cu) <= 1 || (Math.Abs(cv) <= 1 && Math.Abs(cu) <= 6) || (r >= 4.6 && r <= 5.8);
    }

    /// <summary>
    /// 비석(정면, 두께 없음): 돌 실루엣 앞면 명암(왼쪽 위 밝게), 새김(1 십자, 2 글줄, 3 움푹한 이름판), 금, 이 빠진 귀퉁이, 이끼,
    /// 밑에는 납작한 흙 둔덕.
    /// </summary>
    private static PixelImage Tombstone(Head head, int w, int h, uint[] t, double tilt = 0, int engrave = 0, bool crack = false, bool moss = false, bool chip = false, int seed = 1)
    {
        int W = w + 10, H = h + 8;
        double px = W / 2.0, py = H - 3.5;
        double a = tilt * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        (double U, double V) L(int x, int y)
        {
            double dx = x + 0.5 - px, dy = y + 0.5 - py;
            return (dx * cos + dy * sin, -dx * sin + dy * cos);
        }
        bool Front(int x, int y)
        {
            var (u, v) = L(x, y);
            return HeadShape(head, w, h, u, v, chip);
        }
        double mw = (w + 4) / 2.0;
        bool Mound(int x, int y) => y >= H - 4 && y <= H - 1 && Math.Abs(x + 0.5 - px) <= mw - (y == H - 4 ? 1.5 : 0);
        double topV = -h, crackU = w * 0.15;
        var stone = Paint(W, H, (x, y) =>
        {
            if (!Front(x, y)) return Color32.Transparent;
            var c = ShadeAt(Front, x, y, t);
            if (Edge(Front, x, y)) return c;
            var (u, v) = L(x, y);
            double tv = v - topV;
            if (head == Head.Celtic)
            {
                bool Body(int bx, int by) { var (bu, bv) = L(bx, by); return CelticBody(bu, bv, h); }
                if (!Body(x, y)) return C(Body(x - 1, y) || Body(x + 1, y) || Body(x, y - 1) || Body(x, y + 1) ? t[0] : t[1]);
                if (!Body(x - 1, y) || !Body(x, y - 1)) return C(t[4]);
                if (!Body(x + 1, y) || !Body(x, y + 1)) return C(t[2]);
                return C(t[3]);
            }
            if (crack && tv < h * 0.6 && Math.Abs(u - crackU - Math.Sin(tv * 1.3) * 1.2 + tv * 0.15) < 0.55) return C(t[0]);
            if (moss)
            {
                bool nearTop = !Front(x, y - 1) || !Front(x, y - 2) || (!Front(x, y - 3) && Hash(x, y, seed) % 2 == 0);
                bool nearBottom = v > -3 - Hash(x, 0, seed) % 3;
                if ((nearTop && Hash(x, y, seed + 7) % 5 != 0) || (nearBottom && Hash(x, y, seed + 3) % 3 != 0))
                    return C(!Front(x, y - 1) ? MossT[3] : Hash(x, y, seed + 11) % 3 == 0 ? MossT[1] : MossT[2]);
            }
            double headRoom = Math.Min(w * 0.5, h * 0.4);
            if (engrave == 1)
            {
                double cv = topV + Math.Min(w * 0.5, h * 0.35) + 1.5;
                if ((Math.Abs(u) < 0.5 && v >= cv - 2.5 && v <= cv + 3.5) || (Math.Abs(v - cv + 0.5) < 0.5 && Math.Abs(u) <= 2))
                    return C(t[1]);
            }
            if (engrave == 2)
            {
                double first = topV + headRoom + 1;
                for (int i = 0; i < 3; i++)
                {
                    double lv = first + i * 2, lw = (w / 2.0 - 2.5) - (i == 2 ? 1 : 0);
                    if (Math.Abs(v - lv - 0.5) < 0.5 && Math.Abs(u) <= lw && Hash((int)Math.Floor(u + 10), i, seed) % 4 != 0) return C(t[1]);
                }
            }
            if (engrave == 3)
            {
                // 움푹한 이름판: 위·왼쪽 안 테두리는 그늘, 아래·오른쪽은 밝게, 가운데 글줄
                double lw = w / 2.0 - 2, pv = v - (topV + headRoom);
                if (Math.Abs(u) <= lw && pv >= 0 && pv < 6)
                {
                    if (pv < 1 || u < -lw + 1) return C(t[1]);
                    if (pv >= 5 || u > lw - 1) return C(t[4]);
                    if ((pv >= 2 && pv < 3 || pv >= 3.99 && pv < 4) && Math.Abs(u) <= lw - 2 && Hash((int)Math.Floor(u + 10), (int)pv, seed) % 4 != 0) return C(t[1]);
                    return C(t[2]);
                }
            }
            return c;
        });
        var mound = Paint(W, H, (x, y) =>
        {
            if (!Mound(x, y)) return Color32.Transparent;
            return !Edge(Mound, x, y) && Hash(x, y, seed) % 5 == 0 ? C(DirtT[1]) : ShadeAt(Mound, x, y, DirtT);
        });
        return Compose(W, H, [(stone, 0, 0), (mound, 0, 0)]);
    }

    /// <summary>비석: 정면에서 본 둥근·뾰족한·네모 비석, 돌 십자가, 켈트 십자가, 금 가고 이끼 낀 비석, 살짝 기운 비석, 작은 표석(회색 돌 세 가지 톤).</summary>
    public static StampSet Tombstones() => Set("Tombstones",
    [
        Tombstone(Head.Round, 10, 14, StoneT, engrave: 1),
        Tombstone(Head.Round, 11, 15, WarmStoneT, engrave: 3, moss: true, seed: 3),
        Tombstone(Head.Arch, 10, 16, StoneT, engrave: 2, seed: 5),
        Tombstone(Head.Flat, 11, 12, SlateT, engrave: 2, crack: true, chip: true, seed: 2),
        Tombstone(Head.Cross, 11, 17, StoneT),
        Tombstone(Head.Celtic, 15, 20, WarmStoneT, seed: 9),
        Tombstone(Head.Round, 12, 15, SlateT, engrave: 1, moss: true, seed: 4),
        Tombstone(Head.Round, 10, 13, StoneT, tilt: 7, crack: true, seed: 6),
        Tombstone(Head.Arch, 9, 12, WarmStoneT, engrave: 3, seed: 7),
        Tombstone(Head.Cross, 9, 13, SlateT, moss: true, seed: 10),
        Tombstone(Head.Round, 6, 6, StoneT),
        Tombstone(Head.Flat, 7, 5, WarmStoneT, seed: 8),
    ]);

    // ───────────── 호박 ─────────────

    private static readonly uint[] OrangeT = [0xFF6A2A08, 0xFFB04A0E, 0xFFDA6A14, 0xFFF59028, 0xFFFFC870];
    private static readonly uint[] JackT = [0xFF4E1A04, 0xFF8A340A, 0xFFB4500E, 0xFFD66C16, 0xFFF29A3C];
    private static readonly uint[] GreenPumpkinT = [0xFF1E3A14, 0xFF3A6424, 0xFF5A8A34, 0xFF7EAE48, 0xFFB8DA88];
    private static readonly uint[] WhitePumpkinT = [0xFF6A6458, 0xFFB0A894, 0xFFD2CCB8, 0xFFECE8DA, 0xFFFFFFF8];
    private static readonly uint[] YellowGourdT = [0xFF6A4A08, 0xFFB08A14, 0xFFDAB424, 0xFFF4D44A, 0xFFFFF0A0];

    private static readonly Dictionary<char, uint> StemP = new() { ['o'] = 0xFF2E2A12, ['S'] = 0xFF8A8A3A, ['s'] = 0xFF6A6C2C, ['d'] = 0xFF4E4E22 };

    private static readonly string[] Stem =
    [
        "..oo",
        ".oSo",
        "oSso",
        "oSdo",
        "oSdo",
    ];

    private static readonly string[] StemCurl =
    [
        "...oo..",
        "..oSso.",
        ".oSo.oo",
        ".oSo...",
        "oSso...",
        "oSdo...",
    ];

    /// <summary>
    /// 골이 진 호박 몸통: 둥근 몸통(위·아래 가운데가 살짝 패임)을 경선으로 <paramref name="lobes"/>쪽 골로 나누고, 골마다 볼록하게 칠하고 골 사이는 어두운 주름.
    /// <paramref name="stripe"/>면 한 칸 건너 골 색이 다름.
    /// </summary>
    private static PixelImage PumpkinBody(int w, int h, int lobes, uint[] t, uint[]? stripe = null)
    {
        double cx = w / 2.0, cy = h / 2.0, rx = w / 2.0, ry = h / 2.0;
        (double Gx, double Gy) G(int x, int y) => ((x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry);
        bool In(int x, int y)
        {
            var (gx, gy) = G(x, y);
            double dent = 0.2 * Math.Exp(-gx * gx * 14);
            return gx * gx + gy * gy <= 1 && gy >= -(1 - dent);
        }
        (int Index, double U) Sector(int x, int y)
        {
            var (gx, gy) = G(x, y);
            double q = Math.Clamp(gx / Math.Sqrt(Math.Max(1 - gy * gy, 0.05)), -1, 1);
            double f = (Math.Asin(q) + Math.PI / 2) / (Math.PI / lobes);
            int i = Math.Clamp((int)Math.Floor(f), 0, lobes - 1);
            return (i, (f - i) * 2 - 1);
        }
        return Paint(w, h, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return C(t[0]);
            var (i, u) = Sector(x, y);
            var p = stripe is not null && Math.Abs(i - lobes / 2) % 2 == 1 ? stripe : t;
            var (gx, gy) = G(x, y);
            double light = -0.45 * u - 0.35 * gx - 0.6 * gy;
            int side = x + 0.5 < cx ? 1 : -1;
            if (Math.Abs(gy) < 0.86 && In(x + side, y) && Sector(x + side, y).Index != i) return C(light > 0.35 ? p[2] : p[1]);
            return C(light > 0.6 ? p[4] : light > 0.05 ? p[3] : light > -0.5 ? p[2] : p[1]);
        });
    }

    private static PixelImage Pumpkin(int w, int h, int lobes, uint[] t, uint[]? stripe = null, string[]? stem = null, string[]? face = null, int faceY = 0)
    {
        var body = PumpkinBody(w, h, lobes, t, stripe);
        if (face is not null)
        {
            int fw = face.Max(r => r.Length), fx = (w - fw) / 2, fy = h / 2 - face.Length / 2 + faceY;
            bool Hole(int x, int y) => y >= 0 && y < face.Length && x >= 0 && x < face[y].Length && face[y][x] == 'y';
            for (int y = 0; y < face.Length; y++)
                for (int x = 0; x < fw; x++)
                    if (Hole(x, y)) body.Set(fx + x, fy + y, C(!Hole(x, y - 1) && Hole(x, y + 1) ? 0xFFE07818 : Hole(x, y - 1) && Hole(x, y + 1) && Hole(x - 1, y) && Hole(x + 1, y) ? 0xFFFFF6B0 : 0xFFFFD84A));
        }
        stem ??= Stem;
        var stemImg = Art(stem, StemP);
        int sx = w / 2 - stemImg.Width / 2;
        return Compose(w, h + stemImg.Height - 2, [(body, 0, stemImg.Height - 2), (stemImg, sx, 0)]);
    }

    private static readonly string[] FaceGrin =
    [
        ".y.....y.",
        "yyy...yyy",
        "....y....",
        "y.......y",
        "yyy.y.yyy",
        ".yyyyyyy.",
        "..y...y..",
    ];

    private static readonly string[] FaceScary =
    [
        "yy.....yy",
        "yyy...yyy",
        ".y..y..y.",
        "...yyy...",
        "y.y...y.y",
        "yyyyyyyyy",
        ".yy.y.yy.",
    ];

    private static readonly string[] FaceSmall =
    [
        "y...y",
        "yy.yy",
        ".....",
        "yyyyy",
        ".y.y.",
    ];

    private static PixelImage BottleGourd()
    {
        bool In(int x, int y) => InCircle(7, x - 3, y) || InCircle(11, x - 1, y - 5) || (x >= 5 && x <= 7 && y >= 3 && y <= 7);
        var body = Paint(13, 16, (x, y) => ShadeAt(In, x, y, y < 6 ? YellowGourdT : GreenPumpkinT));
        var stemImg = Art(Stem, StemP);
        return Compose(13, 19, [(body, 0, 3), (stemImg, 5, 0)]);
    }

    /// <summary>호박: 둥근·길쭉한·납작한·작은 주황 호박, 초록·흰 호박, 빛나는 잭오랜턴, 줄무늬·조롱박 박, 쌓은 호박. 추수·핼러윈에.</summary>
    public static StampSet Pumpkins()
    {
        var stack = Compose(20, 32,
        [
            (Pumpkin(20, 13, 5, OrangeT), 0, 16),
            (Pumpkin(15, 11, 5, OrangeT), 2, 8),
            (Pumpkin(10, 8, 3, OrangeT, stem: StemCurl), 5, 0),
        ]);
        var shapes = new List<PixelImage>
        {
            Pumpkin(18, 13, 5, OrangeT),
            Pumpkin(12, 15, 5, OrangeT),
            Pumpkin(22, 11, 7, OrangeT, stem: StemCurl),
            Pumpkin(9, 7, 3, OrangeT),
            Pumpkin(14, 11, 5, GreenPumpkinT),
            Pumpkin(14, 10, 5, WhitePumpkinT),
            Pumpkin(8, 7, 3, WhitePumpkinT),
            Pumpkin(11, 9, 5, YellowGourdT, stripe: GreenPumpkinT),
            BottleGourd(),
            Glow(Pumpkin(18, 14, 5, JackT, face: FaceGrin), 0xFFFFA634),
            Glow(Pumpkin(15, 13, 5, JackT, face: FaceScary, stem: StemCurl), 0xFFFFA634),
            Glow(Pumpkin(10, 9, 3, JackT, face: FaceSmall), 0xFFFFA634),
            stack,
        };
        return Set("Pumpkins", shapes);
    }

    // ───────────── 책 공용 ─────────────

    private static readonly uint[][] Covers =
    [
        [0xFF4A0E14, 0xFF7A1A22, 0xFF9E2630, 0xFFC23A3E, 0xFFE8706A], // 빨강
        [0xFF0E1E4A, 0xFF1A3270, 0xFF26489A, 0xFF3A64C2, 0xFF7AA0E8], // 파랑
        [0xFF0E3A1E, 0xFF1A5A2C, 0xFF287A3A, 0xFF3A9A4C, 0xFF7ACC80], // 초록
        [0xFF3A2414, 0xFF5E3A20, 0xFF7E5230, 0xFF9E6C40, 0xFFC89A68], // 갈색 가죽
        [0xFF2E1240, 0xFF4A1E66, 0xFF64308A, 0xFF8048AE, 0xFFB088DA], // 보라
        [0xFF0E3A3A, 0xFF16585A, 0xFF22787A, 0xFF34989A, 0xFF7ACCCC], // 청록
        [0xFF5A3A08, 0xFF8E6410, 0xFFB88A1C, 0xFFDCAE34, 0xFFF6DC84], // 겨자
    ];

    private const int Red = 0, Blue = 1, Green = 2, Brown = 3, Purple = 4, Teal = 5, Mustard = 6;
    private const uint PageA = 0xFFF4ECD6, PageM = 0xFFE6DCC2, PageB = 0xFFD8CBA8, PageEdge = 0xFFBCAC88, GoldA = 0xFFE0B040, GoldB = 0xFFFFE89A, Ink = 0xFF8A8270;

    // ───────────── 쌓은 책 ─────────────

    /// <summary>
    /// 옆(정면)에서 본 누운 책 한 권. <paramref name="spine"/>이면 책등 쪽(표지색 판에 금띠 둘과 제목 딱지),
    /// 아니면 책장 쪽(위·아래 표지 줄 사이에 책장 줄무늬, 왼쪽은 책등을 감싼 표지, 책장은 표지보다 한 칸 들어감).
    /// </summary>
    private static PixelImage FlatBook(int w, int h, int cover, bool spine)
    {
        var c = Covers[cover];
        return Paint(w, h, (x, y) =>
        {
            bool sideX = x == 0 || x == w - 1, sideY = y == 0 || y == h - 1;
            if (sideX && sideY) return Color32.Transparent;
            if (spine)
            {
                if (sideX || sideY) return C(c[0]);
                if (x == 2 || x == w - 3) return C(y == 1 ? GoldB : GoldA);
                if (h >= 6 && y >= 2 && y <= h - 3 && x >= w / 2 - 2 && x <= w / 2 + 1) return C(y == h - 3 ? PageB : PageA);
                return C(y == 1 ? c[4] : y == h - 2 ? c[1] : x == 1 ? c[3] : c[3]);
            }
            bool pageRow = y >= 2 && y <= h - 3;
            if (pageRow && x == w - 1) return Color32.Transparent;
            if (pageRow && x == w - 2) return C(c[0]);
            if (sideX || sideY) return C(c[0]);
            if (y == 1) return C(x <= 3 ? c[4] : c[3]);
            if (y == h - 2) return C(c[1]);
            if (x <= 2) return C(x == 1 ? c[3] : c[2]);
            return C(x == w - 3 ? PageEdge : (y - 2) % 2 == 0 ? PageA : PageB);
        });
    }

    /// <summary>누운 책 더미(아래 책부터, 책마다 가로 어긋남 Dx). 위 책의 밑 테두리가 아래 책의 윗 테두리와 겹친다.</summary>
    private static PixelImage BookStack(params (int W, int H, int Cover, bool Spine, int Dx)[] books)
    {
        int width = books.Max(b => b.Dx + b.W);
        int total = books.Sum(b => b.H) - (books.Length - 1);
        var parts = new List<(PixelImage, int, int)>();
        int bottom = total - 1;
        foreach (var b in books)
        {
            int top = bottom - b.H + 1;
            parts.Add((FlatBook(b.W, b.H, b.Cover, b.Spine), b.Dx, top));
            bottom = top;
        }
        return Compose(width, total, parts);
    }

    /// <summary>쌓은 책: 옆에서 본 누운 책 한 권부터 여섯 권 더미까지(책장 쪽·책등 쪽, 여러 색·두께·어긋남).</summary>
    public static StampSet BookStacks() => Set("Book Stacks",
    [
        FlatBook(16, 6, Red, false),
        FlatBook(14, 5, Blue, true),
        FlatBook(18, 7, Brown, false),
        BookStack((17, 6, Brown, false, 0), (14, 5, Green, true, 2)),
        BookStack((15, 5, Purple, true, 1), (17, 6, Red, false, 0)),
        BookStack((16, 5, Blue, false, 1), (18, 6, Mustard, true, 0), (13, 5, Purple, false, 3)),
        BookStack((18, 7, Brown, false, 0), (15, 5, Teal, true, 2), (16, 5, Red, false, 1), (12, 5, Green, true, 4)),
        BookStack((19, 6, Green, true, 0), (16, 6, Blue, false, 2), (17, 5, Red, true, 1), (14, 5, Mustard, false, 3), (13, 6, Purple, true, 2)),
        BookStack((18, 6, Red, false, 1), (19, 5, Brown, true, 0), (15, 6, Teal, false, 2), (16, 5, Blue, true, 1), (13, 5, Green, false, 4), (12, 5, Purple, true, 3)),
        BookStack((14, 5, Teal, false, 0), (12, 5, Red, true, 1)),
    ]);

    // ───────────── 세운 책 ─────────────

    /// <summary>세운 책의 책등(앞에서). 꾸밈 0: 금띠 둘 + 제목 딱지, 1: 금띠 둘, 2: 금띠 셋, 3: 위쪽 어두운 가죽 띠 + 금띠.</summary>
    private static PixelImage Spine(int w, int h, int cover, int style)
    {
        var c = Covers[cover];
        return Paint(w, h, (x, y) =>
        {
            if (x == 0 || x == w - 1 || y == 0 || y == h - 1) return C(c[0]);
            bool band = style switch
            {
                2 => y == 2 || y == h / 2 || y == h - 3,
                3 => y == 5 || y == h - 3,
                _ => y == 2 || y == h - 3,
            };
            if (band) return C(x == 1 ? GoldB : GoldA);
            if (style == 3 && y >= 2 && y <= 4) return C(x == 1 ? c[2] : c[1]);
            if (style == 0 && w > 4 && y >= h / 2 - 2 && y <= h / 2 && x >= 2 && x <= w - 3) return C(y == h / 2 ? PageB : PageA);
            return C(x == 1 ? c[4] : x == w - 2 ? c[1] : c[3]);
        });
    }

    /// <summary>기울어 기댄 책: 책등을 줄마다 옆으로 밀어(1/<paramref name="every"/> 기울기) 그린다. <paramref name="left"/>면 위가 왼쪽으로.</summary>
    private static PixelImage LeaningSpine(int w, int h, int cover, int style, int every, bool left)
    {
        var spine = Spine(w, h, cover, style);
        int shift = (h - 1) / every;
        int Left(int y) => left ? y / every : shift - y / every;
        return Paint(w + shift, h, (x, y) =>
        {
            int u = x - Left(y);
            return u >= 0 && u < w ? spine.Get(u, y) : Color32.Transparent;
        });
    }

    /// <summary>세운 책 한 줄(밑 줄 맞춤, 이웃 책과 테두리 공유). <paramref name="lean"/>: 0 없음, 1 오른쪽 끝에 왼쪽으로 기댄 책, -1 왼쪽 끝에 오른쪽으로 기댄 책.</summary>
    private static PixelImage BookRow(int lean, params (int W, int H, int Cover, int Style)[] books)
    {
        int rowW = books.Sum(b => b.W - 1) + 1, height = books.Max(b => b.H);
        var parts = new List<(PixelImage, int, int)>();
        int ox = 0;
        PixelImage? leaner = null;
        if (lean != 0)
        {
            var edge = lean > 0 ? books[^1] : books[0];
            leaner = LeaningSpine(6, edge.H - 1, lean > 0 ? Mustard : Teal, 1, 4, lean > 0);
            if (lean < 0) ox = leaner.Width - 1;
        }
        int x = ox;
        foreach (var b in books)
        {
            parts.Add((Spine(b.W, b.H, b.Cover, b.Style), x, height - b.H));
            x += b.W - 1;
        }
        int width = rowW + (leaner is null ? 0 : leaner.Width - 1);
        if (leaner is not null) parts.Add((leaner, lean > 0 ? rowW - 1 : 0, height - leaner.Height));
        return Compose(width, height, parts);
    }

    /// <summary>세운 책: 책장에 꽂힌 듯 앞(책등)에서 본 책 한 권부터 여섯 권 줄까지, 끝에 기댄 책이 있는 줄.</summary>
    public static StampSet StandingBooks() => Set("Standing Books",
    [
        Spine(5, 16, Red, 0),
        Spine(7, 13, Brown, 3),
        BookRow(0, (5, 15, Blue, 0), (4, 13, Green, 1)),
        BookRow(0, (4, 13, Purple, 1), (6, 16, Teal, 0), (4, 12, Red, 2)),
        BookRow(0, (5, 14, Brown, 3), (4, 16, Red, 1), (5, 13, Blue, 0), (4, 15, Green, 2)),
        BookRow(0, (4, 12, Mustard, 1), (5, 15, Purple, 0), (4, 14, Brown, 2), (6, 16, Red, 3), (4, 13, Teal, 1)),
        BookRow(0, (5, 16, Green, 0), (4, 13, Red, 1), (4, 14, Blue, 2), (6, 15, Brown, 3), (4, 12, Purple, 1), (5, 16, Mustard, 0)),
        BookRow(1, (5, 15, Red, 0), (4, 13, Green, 1), (5, 16, Blue, 0)),
        BookRow(-1, (4, 14, Purple, 2), (6, 16, Brown, 3)),
        BookRow(1, (4, 13, Teal, 1), (5, 15, Red, 2), (4, 16, Purple, 0), (5, 14, Green, 3)),
    ]);

    // ───────────── 펼친 책 ─────────────

    /// <summary>
    /// 살짝 위에서 본 펼친 책(가운데 골로 책장이 휘어 내려감). 쪽 내용: 0 글줄, 1 그림, 2 붉은 머리글자 + 글줄, 3 글줄 위로 넘어가는 한 장.
    /// </summary>
    private static PixelImage OpenBook(int half, int pageH, int cover, int leftPage, int rightPage, int seed)
    {
        var c = Covers[cover];
        const int lift = 4;
        int w = half * 2 + 1;
        int Top(int d) => lift + (d == 0 ? 2 : d == 1 ? 1 : d <= half - 2 ? 0 : 1);
        bool Page(int x, int y) { int d = Math.Abs(x - half); return d <= half - 1 && y >= Top(d) + 1 && y <= Top(d) + pageH; }
        bool Cover(int x, int y) { int d = Math.Abs(x - half), e = Math.Min(d, half - 1); return d <= half && y >= Top(e) + 2 && y <= Top(e) + pageH + 1; }
        bool turning = rightPage == 3;
        int Lift(int d) => (int)Math.Round(d * 0.5);
        bool Turned(int x, int y)
        {
            int d = x - half;
            return turning && d >= 1 && d <= half - 2 && y >= Top(d) + 1 - Lift(d) && y <= Top(d) + pageH - 1 - Lift(d);
        }
        bool In(int x, int y) => Page(x, y) || Cover(x, y) || Turned(x, y);
        return Paint(w, lift + pageH + 3, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return C(c[0]);
            if (Turned(x, y)) return C(Edge(Turned, x, y) ? PageEdge : x - half <= 2 ? PageM : PageA);
            if (!Page(x, y)) return C(x < half ? c[3] : c[2]);
            int d = Math.Abs(x - half), row = y - Top(d), content = x < half ? leftPage : rightPage;
            if (d == 0 || row == pageH) return C(PageEdge);
            bool area = d >= 2 && d <= half - 2 && row >= 2 && row <= pageH - 2;
            if (content == 1 && area && row <= pageH - 3)
            {
                if (d == 2 || d == half - 2 || row == 2 || row == pageH - 3) return C(Ink);
                if (d == half - 3 && row == 3) return C(0xFFF8D040);
                return C(row <= (pageH - 1) / 2 ? 0xFF9CC8E8 : 0xFF6EA850);
            }
            if (content == 2 && row >= 2 && row <= 3 && d >= half - 3 && d <= half - 2) return C(0xFFB0202E);
            if (area && row % 2 == 1 && row >= 3 && content != 1 && !(content == 2 && row <= 3 && d >= half - 4) && Hash(x, row, seed) % 5 != 0) return C(Ink);
            return C(x < half ? (d == 1 ? PageB : PageA) : d == 1 ? PageB : PageM);
        });
    }

    /// <summary>펼친 책: 여러 표지 색·크기, 글줄·그림·머리글자 쪽, 넘어가는 책장.</summary>
    public static StampSet OpenBooks() => Set("Open Books",
    [
        OpenBook(9, 9, Brown, 0, 0, 1),
        OpenBook(9, 9, Red, 1, 0, 2),
        OpenBook(9, 9, Blue, 0, 3, 3),
        OpenBook(7, 8, Green, 0, 0, 4),
        OpenBook(7, 8, Purple, 2, 3, 5),
        OpenBook(11, 10, Teal, 2, 1, 6),
        OpenBook(11, 10, Brown, 1, 3, 7),
        OpenBook(7, 8, Red, 0, 1, 8),
    ]);

    // ───────────── 종이·편지 ─────────────

    private static readonly uint[] PaperT = [0xFF6E6A60, 0xFFB8B2A2, 0xFFD8D3C6, 0xFFF0EDE4, 0xFFFFFFFF];

    /// <summary>낱장 종이(오른쪽 위 귀퉁이 접힘). 종류 0: 글줄, 1: 제목 + 글줄, 2: 양피지 글줄, 3: 줄 공책(파란 줄·붉은 여백선).</summary>
    private static PixelImage Sheet(int w, int h, int kind, int seed)
    {
        bool parch = kind == 2;
        uint o = parch ? 0xFF6A4A26 : PaperT[0], b = parch ? 0xFFEED6A0 : 0xFFF6F3EA, s = parch ? 0xFFD2AE70 : PaperT[2], ink = parch ? 0xFF8A6A48 : 0xFF8A90A0;
        bool fold = kind != 3;
        bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && !(fold && x - (w - 4) > y);
        return Paint(w, h, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return C(o);
            if (fold && x >= w - 4 && y <= 3 && x - (w - 4) >= y - 1) return C(s);
            if (kind == 3)
            {
                if (x == 2) return C(0xFFE08080);
                if (y % 2 == 0 && y >= 2 && y <= h - 2) return C(0xFFA8C4E8);
                return C(b);
            }
            if (kind == 1 && y == 2 && x >= 2 && x <= w / 2 + 1) return C(0xFF5A6070);
            int first = kind == 1 ? 4 : 3;
            if ((y - first) % 2 == 0 && y >= first && y <= h - 3 && x >= 2 && x <= w - 3 && Hash(x, y, seed) % 6 != 0) return C(ink);
            return C(x == w - 2 || y == h - 2 ? s : b);
        });
    }

    /// <summary>구긴 종이 뭉치: 둘레가 울퉁불퉁한 공에 짧은 구김 선.</summary>
    private static PixelImage Crumpled(int size, int seed)
    {
        double c = size / 2.0;
        bool In(int x, int y)
        {
            double dx = x + 0.5 - c, dy = y + 0.5 - c, ang = Math.Atan2(dy, dx);
            double r = c - 0.3 - Hash((int)((ang + Math.PI) * 1.6), 0, seed) % 2 * 0.7;
            return dx * dx + dy * dy <= r * r;
        }
        var creases = new HashSet<(int X, int Y)>();
        for (int i = 0; i < 4; i++)
        {
            int x0 = 2 + Hash(i, 1, seed) % (size - 4), y0 = 2 + Hash(i, 2, seed) % (size - 4);
            Line(creases, x0, y0, x0 + Hash(i, 3, seed) % 5 - 2, y0 + Hash(i, 4, seed) % 5 - 2);
        }
        return Paint(size, size, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (!Edge(In, x, y) && creases.Contains((x, y))) return C(PaperT[1]);
            if (!Edge(In, x, y) && creases.Contains((x + 1, y + 1)) && !creases.Contains((x, y + 1))) return C(PaperT[4]);
            return ShadeAt(In, x, y, PaperT);
        });
    }

    // 반으로 접은 쪽지: 위 반은 밝고 아래 반은 그늘, 가운데 접힌 줄
    private static PixelImage FoldedNote(int w, int h, int seed) => Paint(w, h, (x, y) =>
    {
        if (x == 0 || y == 0 || x == w - 1 || y == h - 1) return C(PaperT[0]);
        int mid = h / 2;
        if (y == mid) return C(PaperT[1]);
        if (y < mid)
        {
            if (y % 2 == 0 && x >= 2 && x <= w - 3 && Hash(x, y, seed) % 5 != 0) return C(0xFF8A90A0);
            return C(y == 1 ? PaperT[4] : x == w - 2 ? PaperT[2] : PaperT[3]);
        }
        return C(y == mid + 1 ? PaperT[3] : x == w - 2 || y == h - 2 ? PaperT[1] : PaperT[2]);
    });

    private static readonly Dictionary<char, uint> EnvelopeP = new()
    {
        ['o'] = 0xFF6E6A60, ['d'] = 0xFFB8B0A0, ['m'] = 0xFFD6D0C0, ['l'] = 0xFFF4F0E4, ['H'] = 0xFFFFFFFF,
        ['i'] = 0xFF5A6070, ['w'] = 0xFFD86040, ['b'] = 0xFF3A6AB0,
    };

    private static readonly Dictionary<char, uint> RedSeal = new() { ['z'] = 0xFF6A1018, ['s'] = 0xFFB0202E, ['S'] = 0xFFE04A50 };
    private static readonly Dictionary<char, uint> GreenSeal = new() { ['z'] = 0xFF0E3A1E, ['s'] = 0xFF287A3A, ['S'] = 0xFF6AB864 };

    // 뒷면: 덮개 V자와 밀랍 봉인
    private static readonly string[] EnvelopeBack =
    [
        "ooooooooooooo",
        "odHHllllllldo",
        "oldllllllldmo",
        "olldllllldlmo",
        "ollldzzzdllmo",
        "olllzSsszllmo",
        "ommmmzszmmmmo",
        "odddddddddddo",
        "ooooooooooooo",
    ];

    // 앞면: 우표와 주소 글줄
    private static readonly string[] EnvelopeFront =
    [
        "ooooooooooooo",
        "oHllllllwwwmo",
        "olllllllwbwmo",
        "olllllllwwwmo",
        "olliiiiiillmo",
        "olliiilllllmo",
        "olliiiiilllmo",
        "odddddddddddo",
        "ooooooooooooo",
    ];

    /// <summary>종이·편지: 살짝 돌아간 낱장 종이(글줄·제목·양피지·줄 공책), 구긴 종이, 접은 쪽지, 봉인한 편지 봉투와 우표 붙은 봉투.</summary>
    public static StampSet Papers() => Set("Papers & Letters",
    [
        Rotate(Sheet(9, 12, 0, 1), 9, PaperT[0]),
        Rotate(Sheet(10, 12, 1, 2), -12, PaperT[0]),
        Rotate(Sheet(9, 11, 2, 3), -6, 0xFF6A4A26),
        Sheet(10, 13, 3, 4),
        Rotate(Sheet(10, 13, 0, 5), 4, PaperT[0]),
        Crumpled(11, 1),
        Crumpled(9, 2),
        Rotate(FoldedNote(11, 9, 6), -10, PaperT[0]),
        Art(EnvelopeBack, EnvelopeP, RedSeal),
        Rotate(Art(EnvelopeBack, EnvelopeP, GreenSeal), 10, EnvelopeP['o']),
        Art(EnvelopeFront, EnvelopeP),
    ]);

    // ───────────── 두루마리 ─────────────

    // 양피지 톤: [0] 테두리, [1] 그늘, [2] 중간, [3] 바탕, [4] 밝은 곳, [5] 글씨
    private static readonly uint[] ParchLight = [0xFF6A4A26, 0xFFB08A50, 0xFFD2AE70, 0xFFEED6A0, 0xFFFFF2D0, 0xFF8A6A48];
    private static readonly uint[] ParchAged = [0xFF5A3A1C, 0xFF9A6E3A, 0xFFBE9058, 0xFFD8B47A, 0xFFF0D8A4, 0xFF6A4A2E];
    // 끈 톤: [0] 테두리, [1] 바탕, [2] 밝은 곳
    private static readonly uint[] RedTie = [0xFF6E101C, 0xFFC0283A, 0xFFE85060];
    private static readonly uint[] BlueTie = [0xFF101E5A, 0xFF2A4AB0, 0xFF5A82E0];
    private static readonly uint[] GreenTie = [0xFF0E3A1E, 0xFF287A3A, 0xFF6AB864];

    /// <summary>말린 두루마리(가로 원통, 왼쪽 끝은 말린 단면), 가운데를 끈으로 묶음. <paramref name="seal"/>이면 끈 밑에 밀랍 봉인을 단다.</summary>
    private static PixelImage RolledScroll(int len, uint[] p, uint[] tie, bool seal)
    {
        const int h = 6;
        int mid = len / 2;
        var roll = Paint(len, h, (x, y) =>
        {
            bool sx = x == 0 || x == len - 1, sy = y == 0 || y == h - 1;
            if (sx && sy) return Color32.Transparent;
            if (x >= mid - 1 && x <= mid + 1) return C(x == mid && !sy ? (y == 1 ? tie[2] : tie[1]) : tie[0]);
            if (sx || sy) return C(p[0]);
            if (x == 1) return C(y == 2 || y == 3 ? p[0] : p[2]);
            if (x == 2 && (y == 2 || y == 3)) return C(p[1]);
            return C(y == 1 ? p[4] : y == 2 ? p[3] : y == 3 ? p[2] : p[1]);
        });
        if (!seal) return roll;
        var wax = Art(["..z..", ".zSz.", "zSssz", ".zsz.", "..z.."], RedSeal);
        return Compose(len, h + 4, [(roll, 0, 0), (wax, mid - 2, 4)]);
    }

    /// <summary>세로 두루마리: 위·아래 말린 막대 사이의 양피지에 글줄. 막대 높이 4면 굵게 말림, 3이면 살짝 말린 끝.</summary>
    private static PixelImage HangingScroll(int w, int sheetH, uint[] p, int topRoll, int bottomRoll, int seed)
    {
        int h = topRoll + sheetH + bottomRoll;
        return Paint(w, h, (x, y) =>
        {
            bool inTop = y < topRoll, inBottom = y >= topRoll + sheetH;
            if (inTop || inBottom)
            {
                int ry = inTop ? y : y - topRoll - sheetH, rh = inTop ? topRoll : bottomRoll;
                bool sx = x == 0 || x == w - 1, sy = ry == 0 || ry == rh - 1;
                if (sx && sy) return Color32.Transparent;
                if (sx || sy) return C(p[0]);
                return C(rh == 3 ? p[2] : ry == 1 ? (x <= 2 ? p[4] : x >= w - 3 ? p[2] : p[3]) : p[1]);
            }
            int sy2 = y - topRoll;
            if (x < 2 || x > w - 3) return Color32.Transparent;
            if (x == 2 || x == w - 3) return C(p[0]);
            if (sy2 % 2 == 1 && sy2 >= 1 && sy2 <= sheetH - 2 && x >= 4 && x <= w - 5 && Hash(x, sy2, seed) % 5 != 0) return C(p[5]);
            return C(x == w - 4 ? p[2] : x == 3 ? p[4] : p[3]);
        });
    }

    /// <summary>가로로 펼친 두루마리: 양쪽 세로 막대 사이의 양피지.</summary>
    private static PixelImage WideScroll(int w, int h, uint[] p, int seed) => Paint(w, h, (x, y) =>
    {
        bool roll = x < 4 || x >= w - 4;
        if (roll)
        {
            int rx = x < 4 ? x : x - (w - 4);
            bool sx = rx == 0 || rx == 3, ey = y == 0 || y == h - 1;
            if (sx && ey) return Color32.Transparent;
            if (sx || ey) return C(p[0]);
            return C(rx == 1 ? (y <= 2 ? p[4] : p[3]) : y >= h - 3 ? p[1] : p[2]);
        }
        if (y < 2 || y > h - 3) return Color32.Transparent;
        if (y == 2 || y == h - 3) return C(p[0]);
        int sy = y - 2;
        if (sy % 2 == 0 && sy >= 2 && sy <= h - 7 && x >= 6 && x <= w - 7 && Hash(x, sy, seed) % 5 != 0) return C(p[5]);
        return C(y == 3 ? p[4] : y == h - 4 ? p[2] : p[3]);
    });

    /// <summary>두루마리: 끈으로 묶거나 밀랍으로 봉인한 말린 두루마리, 반쯤 펼친 것, 세로·가로로 다 펼친 것(밝은·바랜 양피지).</summary>
    public static StampSet Scrolls() => Set("Scrolls",
    [
        RolledScroll(17, ParchLight, RedTie, false),
        RolledScroll(13, ParchAged, BlueTie, false),
        RolledScroll(19, ParchLight, GreenTie, true),
        Rotate(RolledScroll(15, ParchAged, RedTie, false), -25, ParchAged[0]),
        HangingScroll(13, 9, ParchLight, 5, 3, 1),
        HangingScroll(11, 7, ParchAged, 5, 3, 2),
        HangingScroll(13, 13, ParchLight, 4, 4, 3),
        HangingScroll(11, 11, ParchAged, 4, 4, 4),
        WideScroll(21, 12, ParchLight, 5),
    ]);

    // ───────────── 깃펜 ─────────────

    /// <summary>
    /// 깃펜: 오른쪽 위 깃 끝에서 왼쪽 아래 펜촉으로 내려오는 축(줄마다 <paramref name="slope"/>칸씩 왼쪽), 줄마다 축 왼쪽(빛 받는 쪽)·오른쪽 깃 폭.
    /// </summary>
    private static PixelImage Quill(uint[] t, int rows = 14, double slope = 1)
    {
        int[] left = [0, 1, 2, 2, 3, 3, 1, 3, 2, 2, 1, 0, 0, 0];
        int[] right = [0, 0, 1, 1, 1, 2, 2, 2, 2, 0, 1, 0, 0, 0];
        int x0 = (int)Math.Round((rows - 1) * slope) + 4;
        var px = new Dictionary<(int X, int Y), uint>();
        for (int y = 0; y < rows; y++)
        {
            int i = (int)Math.Round(y * 13.0 / (rows - 1));
            int s = x0 - (int)Math.Round(y * slope), row = y + 1;
            px[(s, row)] = i >= 13 ? 0xFF2A2A34 : i >= 11 ? 0xFFE8DCC0 : t[4];
            for (int k = 1; k <= left[i]; k++) px[(s - k, row)] = k == left[i] ? t[3] : t[4];
            for (int k = 1; k <= right[i]; k++) px[(s + k, row)] = k == right[i] ? t[1] : t[2];
        }
        return Paint(x0 + 5, rows + 2, (x, y) =>
        {
            if (px.TryGetValue((x, y), out var c)) return C(c);
            bool near = px.ContainsKey((x - 1, y)) || px.ContainsKey((x + 1, y)) || px.ContainsKey((x, y - 1)) || px.ContainsKey((x, y + 1));
            return near ? C(y >= rows * 0.8 ? 0xFF4A3E2E : t[0]) : Color32.Transparent;
        });
    }

    private static readonly uint[] WhiteFeather = [0xFF4A4E60, 0xFF9CA0B2, 0xFFC4C8D4, 0xFFE8EAF0, 0xFFFFFFFF];
    private static readonly uint[] HawkFeather = [0xFF4A2E18, 0xFF8A5A2E, 0xFFB07A40, 0xFFD49A58, 0xFFF0C890];
    private static readonly uint[] RavenFeather = [0xFF101218, 0xFF262A38, 0xFF3A4054, 0xFF525A74, 0xFF8A94B0];
    private static readonly uint[] RedFeather = [0xFF4A0E14, 0xFF9E1A2A, 0xFFC8283A, 0xFFE8484E, 0xFFFF9A9A];
    private static readonly uint[] BlueFeather = [0xFF0E2A4A, 0xFF1E5A8A, 0xFF2E86B8, 0xFF5AB4E0, 0xFFB0E4FF];
    private static readonly uint[] GreenFeather = [0xFF0E3A1E, 0xFF1E7A2E, 0xFF2EA040, 0xFF56CC5A, 0xFFB4F2A0];

    private static readonly Dictionary<char, uint> InkwellP = new() { ['o'] = 0xFF10121A, ['k'] = 0xFF2A3046, ['d'] = 0xFF1C2030, ['H'] = 0xFF7A86A8, ['i'] = 0xFF0A0A10 };

    private static readonly string[] Inkwell =
    [
        "..ooooo..",
        "..oiiio..",
        ".ooooooo.",
        "oHHkkkkdo",
        "oHkkkkkdo",
        "okkkkkddo",
        ".ooooooo.",
    ];

    // 잉크병에 꽂은 깃펜(펜촉이 병 입구 속으로)
    private static PixelImage QuillInInkwell(uint[] t)
    {
        var quill = Quill(t);
        return Compose(quill.Width + 2, 20, [(quill, 0, 0), (Art(Inkwell, InkwellP), 0, 13)]);
    }

    /// <summary>깃펜: 흰·매·까마귀·빨강·파랑·초록 깃털 펜(두 가지 기울기, 좌우 뒤집기), 잉크병에 꽂은 깃펜.</summary>
    public static StampSet Quills() => Set("Quill Pens", Mirrored(
    [
        Quill(WhiteFeather),
        Quill(HawkFeather, 18, 0.6),
        Quill(RavenFeather),
        Quill(RedFeather, 18, 0.6),
        Quill(BlueFeather, 12),
        Quill(GreenFeather, 18, 0.6),
        QuillInInkwell(WhiteFeather),
    ]));
}
