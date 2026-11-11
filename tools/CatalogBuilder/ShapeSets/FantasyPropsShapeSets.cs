using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using static Chickxel.Core.IO.ShapeArt;

namespace CatalogBuilder;

/// <summary>
/// 판타지 소품 모양 펜 세트 여섯 가지: 뼈·해골, 물약병, 수정, 비석, 호박, 책·종이.
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

    private static readonly uint[] Amethyst = [0xFF3A1458, 0xFF6A2A9A, 0xFF9450CC, 0xFFBC86EE, 0xFFF0DCFF];
    private static readonly uint[] Sapphire = [0xFF0E2A5E, 0xFF1E58A8, 0xFF3A8CDA, 0xFF78C4F6, 0xFFDCF4FF];
    private static readonly uint[] Emerald = [0xFF0C4232, 0xFF178A5E, 0xFF2EBE82, 0xFF7AE8B2, 0xFFE0FFF0];
    private static readonly uint[] RoseQuartz = [0xFF5E1440, 0xFFA8306E, 0xFFDA5A9C, 0xFFF89ACA, 0xFFFFE4F2];
    private static readonly uint[] Quartz = [0xFF3E5468, 0xFF8AA4B8, 0xFFB8D0E0, 0xFFE2F0F8, 0xFFFFFFFF];
    private static readonly uint[] RockT = [0xFF2A262C, 0xFF46404A, 0xFF625C66, 0xFF807A84, 0xFFA29CA6];

    /// <summary>수정 기둥 하나: 밑동 가운데 X, 밑 줄 Base, 반폭 Half, 길이 Len, 한 줄 오를 때 옆으로 Lean.</summary>
    private readonly record struct Shard(double X, int Base, double Half, int Len, double Lean);

    private static PixelImage Crystal(int w, int h, Shard s, uint[] t)
    {
        double tip = Math.Max(2, s.Half * 1.7);
        (double Cx, double Hw) Row(int y)
        {
            int k = s.Base - y;
            double hw = k > s.Len - tip ? s.Half * (s.Len - k + 0.6) / tip : s.Half;
            return (s.X + s.Lean * k, hw);
        }
        bool Inside(int x, int y)
        {
            int k = s.Base - y;
            if (k < 0 || k > s.Len) return false;
            var (cx, hw) = Row(y);
            return Math.Abs(x + 0.5 - cx) <= hw + 0.3;
        }
        return Paint(w, h, (x, y) =>
        {
            if (!Inside(x, y)) return Color32.Transparent;
            if (Edge(Inside, x, y)) return C(t[0]);
            var (cx, hw) = Row(y);
            double rel = (x + 0.5 - cx) / Math.Max(hw, 0.8);
            bool nearTip = s.Base - y > s.Len - tip - 1;
            if (rel < -0.25) return C(!Inside(x - 2, y) && (nearTip || s.Base - y > s.Len / 2) ? t[4] : t[3]);
            if (rel < 0.3) return C(nearTip && rel < 0 ? t[3] : t[2]);
            return C(t[1]);
        });
    }

    /// <summary>바위 받침: 밑 줄에 중심을 둔 반타원 둔덕들(X, Y, 가로 반지름, 세로 반지름).</summary>
    private static PixelImage Rock(int w, int h, (double X, double Y, double Rx, double Ry)[] domes)
        => Shaded(w, h, (x, y) => y < h && domes.Any(d => Math.Pow((x + 0.5 - d.X) / d.Rx, 2) + Math.Pow((y + 0.5 - d.Y) / d.Ry, 2) <= 1), RockT);

    private static PixelImage Cluster(int w, int h, uint[] t, Shard[] shards, (double X, double Y, double Rx, double Ry)[]? rock = null)
    {
        var parts = shards.Select(s => (Crystal(w, h, s, t), 0, 0)).ToList();
        if (rock is not null) parts.Add((Rock(w, h, rock), 0, 0));
        return Compose(w, h, parts);
    }

    private static PixelImage LargeCluster(uint[] t) => Cluster(26, 26, t,
    [
        new(6.5, 22, 2.2, 12, -0.34),
        new(19.5, 22, 2.2, 11, 0.34),
        new(13, 23, 2.8, 20, 0),
        new(9.5, 23, 1.7, 8, -0.2),
        new(17.5, 23, 1.7, 7, 0.25),
    ], [(12.5, 26, 12, 7), (19, 26, 6, 8.5)]);

    private static PixelImage MediumCluster(uint[] t) => Cluster(19, 18, t,
    [
        new(5, 15, 1.8, 8, -0.4),
        new(13.5, 15, 1.8, 9, 0.3),
        new(9, 16, 2.4, 14, -0.1),
    ], [(9.5, 18, 9, 5), (5.5, 18, 4.5, 6)]);

    private static PixelImage SmallCluster(uint[] t) => Cluster(12, 12, t,
    [
        new(4, 10, 1.6, 6, -0.34),
        new(7.5, 11, 1.9, 9, 0.2),
    ]);

    private static PixelImage GroundCluster(uint[] t) => Cluster(16, 15, t,
    [
        new(4, 14, 1.7, 7, -0.5),
        new(11.5, 14, 1.7, 6, 0.5),
        new(8, 14, 2.2, 13, 0),
    ]);

    private static PixelImage LyingShard(uint[] t) => Rotate(Crystal(7, 14, new Shard(3.5, 13, 2.2, 12, 0), t), 62, t[0]);

    private static PixelImage LoneCrystal(uint[] t) => Cluster(11, 16, t, [new(5.5, 14, 2.4, 13, 0)], [(5.5, 16, 5, 3)]);

    /// <summary>수정: 바위에서 자란 큰·중간 무리(자수정·청수정·녹수정·장미 수정·맑은 수정), 땅에서 솟은 무리, 작은 무리, 낱개 조각, 빛나는 수정.</summary>
    public static StampSet Crystals()
    {
        var shapes = new List<PixelImage>
        {
            LargeCluster(Amethyst),
            LargeCluster(Quartz),
            MediumCluster(Sapphire),
            MediumCluster(RoseQuartz),
            MediumCluster(Emerald),
            GroundCluster(Amethyst),
            GroundCluster(Sapphire),
            SmallCluster(Emerald),
            SmallCluster(RoseQuartz),
            SmallCluster(Quartz),
            LoneCrystal(Amethyst),
            LoneCrystal(Emerald),
            Glow(LargeCluster(Sapphire), Sapphire[3]),
            Glow(GroundCluster(Emerald), Emerald[3]),
            Glow(SmallCluster(Amethyst), Amethyst[3]),
        };
        shapes.Add(LyingShard(RoseQuartz));
        shapes.Add(FlipX(LyingShard(Sapphire)));
        return Set("Crystals", shapes);
    }

    // ───────────── 비석 ─────────────

    private static readonly uint[] StoneT = [0xFF34363F, 0xFF62646F, 0xFF878995, 0xFFAAACB6, 0xFFD2D4DC];
    private static readonly uint[] StoneSideT = [0xFF34363F, 0xFF555864, 0xFF666975, 0xFF6E717D, 0xFF7A7D88];
    private static readonly uint[] MossT = [0xFF2E4A1C, 0xFF4A7228, 0xFF6E9A38, 0xFF9CC456];
    private static readonly uint[] DirtT = [0xFF2A1E14, 0xFF46321F, 0xFF5C4229, 0xFF725334, 0xFF8E6C46];
    private static readonly uint[] StrawT = [0xFF5E4A2A, 0xFF8A7444, 0xFFB09A5C, 0xFFD2BE80];

    private enum Head { Round, Arch, Flat, Cross }

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
            default: // Cross
                double arm = -h + h * 0.3;
                return Math.Abs(u) <= 2.1 || (v >= arm - 2 && v <= arm + 2 && Math.Abs(u) <= hw);
        }
    }

    /// <summary>
    /// 비석: 앞면(빛 받는 돌) 뒤에 오른쪽 위로 두께, 새김(1 십자, 2 글줄), 금, 이끼, 밑에 흙 둔덕.
    /// </summary>
    private static PixelImage Tombstone(Head head, int w, int h, double tilt, int engrave = 0, bool crack = false, bool moss = false, bool chip = false, int seed = 1)
    {
        int W = w + 14, H = h + 10;
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
        bool Back(int x, int y) => Front(x - 2, y + 1) && !Front(x, y);
        double mw = (w + 6) / 2.0;
        bool Mound(int x, int y)
        {
            double dx = (x + 0.5 - px) / mw;
            return y <= H - 1 && Math.Abs(dx) < 1 && H - 1 - y + 0.5 < 4.2 * Math.Sqrt(1 - dx * dx);
        }
        double topV = -h, crackU = w * 0.15;
        var stone = Paint(W, H, (x, y) =>
        {
            if (Front(x, y))
            {
                var c = ShadeAt(Front, x, y, StoneT);
                if (Edge(Front, x, y)) return c;
                var (u, v) = L(x, y);
                double tv = v - topV;
                if (crack && tv < h * 0.6 && Math.Abs(u - crackU - Math.Sin(tv * 1.3) * 1.2 + tv * 0.15) < 0.55) return C(StoneT[0]);
                if (moss)
                {
                    bool nearTop = !Front(x, y - 1) || !Front(x, y - 2) || (!Front(x, y - 3) && Hash(x, y, seed) % 2 == 0);
                    bool nearBottom = v > -3 - Hash(x, 0, seed) % 3;
                    if ((nearTop && Hash(x, y, seed + 7) % 5 != 0) || (nearBottom && Hash(x, y, seed + 3) % 3 != 0))
                        return C(!Front(x, y - 1) ? MossT[3] : Hash(x, y, seed + 11) % 3 == 0 ? MossT[1] : MossT[2]);
                }
                if (engrave == 1 && head != Head.Cross)
                {
                    double cv = topV + Math.Min(w * 0.5, h * 0.35) + 1.5;
                    if ((Math.Abs(u) < 0.5 && v >= cv - 2.5 && v <= cv + 3.5) || (Math.Abs(v - cv + 0.5) < 0.5 && Math.Abs(u) <= 2))
                        return C(StoneT[1]);
                }
                if (engrave == 2)
                {
                    double first = topV + Math.Min(w * 0.5, h * 0.4) + 1;
                    for (int i = 0; i < 3; i++)
                    {
                        double lv = first + i * 2, lw = (w / 2.0 - 2.5) - (i == 2 ? 1 : 0);
                        if (Math.Abs(v - lv - 0.5) < 0.5 && Math.Abs(u) <= lw && Hash((int)Math.Floor(u + 10), i, seed) % 4 != 0) return C(StoneT[1]);
                    }
                }
                return c;
            }
            if (Back(x, y))
                return Edge((a2, b2) => Front(a2 - 2, b2 + 1) || Front(a2, b2), x, y) ? C(StoneSideT[0]) : C(StoneSideT[2]);
            return Color32.Transparent;
        });
        var mound = Paint(W, H, (x, y) =>
        {
            if (Mound(x, y))
                return !Edge(Mound, x, y) && Hash(x, y, seed) % 6 == 0 ? C(DirtT[1]) : ShadeAt(Mound, x, y, DirtT);
            // 둔덕 위로 삐죽 솟은 마른 풀
            if (Mound(x, y + 1) && Hash(x, 0, seed + 5) % 4 == 0) return C(StrawT[2]);
            if (Mound(x, y + 2) && !Mound(x, y + 1) && Hash(x, 0, seed + 5) % 8 == 0) return C(StrawT[3]);
            return Color32.Transparent;
        });
        return Compose(W, H, [(stone, 0, 0), (mound, 0, 0)]);
    }

    private static readonly Dictionary<char, uint> StrawP = new() { ['a'] = StrawT[0], ['b'] = StrawT[1], ['c'] = StrawT[2], ['d'] = StrawT[3] };

    private static readonly string[] DeadGrass =
    [
        "....d.......",
        ".d..c....c..",
        "..c.c...c...",
        "..b.cd..b..d",
        "c.b.b.c.b.c.",
        ".bb.b.cbb.b.",
        "..bab.bab.b.",
        "..aaabaaaba.",
    ];

    private static readonly string[] DeadGrassSmall =
    [
        "..d...",
        "c.c..d",
        ".bc.c.",
        ".bbcb.",
        "..aab.",
    ];

    /// <summary>비석: 둥근·뾰족한·네모 비석, 돌 십자가, 금 간·기운·이끼 낀 비석, 작은 표석, 마른 풀 포기. 묘지 바닥에.</summary>
    public static StampSet Tombstones()
    {
        var shapes = new List<PixelImage>
        {
            Tombstone(Head.Round, 10, 14, 0, engrave: 1),
            Tombstone(Head.Round, 11, 15, -9, engrave: 2, moss: true, seed: 3),
            Tombstone(Head.Arch, 10, 16, 0, engrave: 2, seed: 5),
            Tombstone(Head.Flat, 11, 12, 6, engrave: 2, crack: true, chip: true, seed: 2),
            Tombstone(Head.Cross, 11, 17, 0),
            Tombstone(Head.Cross, 9, 14, 12, moss: true, seed: 9),
            Tombstone(Head.Round, 12, 15, 0, engrave: 1, moss: true, seed: 4),
            Tombstone(Head.Round, 10, 13, 11, crack: true, seed: 6),
            Tombstone(Head.Round, 6, 6, 0),
            Tombstone(Head.Flat, 6, 5, -10, seed: 8),
            Art(DeadGrass, StrawP),
            Art(DeadGrassSmall, StrawP),
            FlipX(Art(DeadGrass, StrawP)),
        };
        return Set("Tombstones", shapes);
    }

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

    // ───────────── 책·종이 ─────────────

    private static readonly uint[][] Covers =
    [
        [0xFF4A0E14, 0xFF7A1A22, 0xFF9E2630, 0xFFC23A3E, 0xFFE8706A], // 빨강
        [0xFF0E1E4A, 0xFF1A3270, 0xFF26489A, 0xFF3A64C2, 0xFF7AA0E8], // 파랑
        [0xFF0E3A1E, 0xFF1A5A2C, 0xFF287A3A, 0xFF3A9A4C, 0xFF7ACC80], // 초록
        [0xFF3A2414, 0xFF5E3A20, 0xFF7E5230, 0xFF9E6C40, 0xFFC89A68], // 갈색 가죽
        [0xFF2E1240, 0xFF4A1E66, 0xFF64308A, 0xFF8048AE, 0xFFB088DA], // 보라
        [0xFF0E3A3A, 0xFF16585A, 0xFF22787A, 0xFF34989A, 0xFF7ACCCC], // 청록
    ];

    private const int Red = 0, Blue = 1, Green = 2, Brown = 3, Purple = 4, Teal = 5;
    private const uint PageA = 0xFFF4ECD6, PageB = 0xFFD8CBA8, PageEdge = 0xFFBCAC88, GoldA = 0xFFE0B040, GoldB = 0xFFFFE89A, Ink = 0xFF8A8270;

    /// <summary>누운 책(살짝 위에서): 윗표지 두 줄, 앞으로 보이는 책장, 왼쪽 책등.</summary>
    private static PixelImage LyingBook(int w, int pages, int cover)
    {
        var c = Covers[cover];
        int h = pages + 6;
        return Paint(w, h, (x, y) =>
        {
            bool l = x == 0, r = x == w - 1, t = y == 0, b = y == h - 1;
            if ((l || r) && (t || b)) return Color32.Transparent;
            if (l || r || t || b) return C(c[0]);
            if (y <= 2) return C(y == 1 && x <= 3 ? c[4] : x >= w - 3 ? c[2] : c[3]);
            if (x <= 2) return C(y == 4 && x == 1 ? GoldA : x == 1 ? c[3] : c[2]);
            if (y == 3 || y == h - 2) return C(c[1]);
            return C(x == w - 2 ? PageEdge : (y - 4) % 2 == 0 ? PageA : PageB);
        });
    }

    /// <summary>누운 책 더미(아래 책부터): 위 책의 밑 줄이 아래 책 윗면 둘째 줄에 얹힌다.</summary>
    private static PixelImage BookStack(params (int W, int Pages, int Cover, int Dx)[] books)
    {
        int width = books.Max(b => b.Dx + b.W);
        int total = books.Sum(b => b.Pages + 6) - 3 * (books.Length - 1);
        var parts = new List<(PixelImage, int, int)>();
        int y = total;
        foreach (var b in books)
        {
            y -= b.Pages + 6;
            parts.Add((LyingBook(b.W, b.Pages, b.Cover), b.Dx, y));
            y += 3;
        }
        return Compose(width, total, parts);
    }

    /// <summary>세운 책의 책등(앞에서): 금띠 둘, 가운데 제목 딱지(<paramref name="label"/>).</summary>
    private static PixelImage Spine(int w, int h, int cover, bool label)
    {
        var c = Covers[cover];
        return Paint(w, h, (x, y) =>
        {
            if (x == 0 || x == w - 1 || y == 0 || y == h - 1) return C(c[0]);
            if (y == 2 || y == h - 3) return C(x == 1 ? GoldB : GoldA);
            if (label && y >= h / 2 - 2 && y <= h / 2 && x >= 1 && x <= w - 2 && w > 4) return C(x == w - 2 ? PageB : PageA);
            return C(x == 1 ? c[4] : x == w - 2 ? c[1] : c[3]);
        });
    }

    private static PixelImage Shelf(params (int W, int H, int Cover, bool Label)[] books)
    {
        int width = books.Sum(b => b.W - 1) + 1, height = books.Max(b => b.H);
        var parts = new List<(PixelImage, int, int)>();
        int x = 0;
        foreach (var b in books)
        {
            parts.Add((Spine(b.W, b.H, b.Cover, b.Label), x, height - b.H));
            x += b.W - 1;
        }
        return Compose(width, height, parts);
    }

    private static PixelImage LeaningShelf()
    {
        var group = Shelf((5, 15, Red, true), (4, 13, Green, false), (5, 16, Blue, true));
        var lean = Rotate(Spine(6, 14, Brown, true), -20, Covers[Brown][0]);
        return Compose(group.Width + 12, group.Height + 1, [(lean, group.Width - 6, 0), (group, 0, 1)]);
    }

    private static PixelImage OpenBook(int cover)
    {
        var c = Covers[cover];
        const int half = 9, w = half * 2 + 1, pageH = 8;
        static int Top(int d) => d == 0 ? 2 : d == 1 ? 1 : d <= 7 ? 0 : 1;
        bool Page(int x, int y) { int d = Math.Abs(x - half); return d <= 8 && y >= Top(d) + 1 && y <= Top(d) + pageH; }
        bool Cover(int x, int y) { int d = Math.Abs(x - half); return d <= 9 && y >= Top(Math.Min(d, 8)) + 2 && y <= Top(Math.Min(d, 8)) + pageH + 1; }
        bool In(int x, int y) => Page(x, y) || Cover(x, y);
        return Paint(w, pageH + 6, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return C(c[0]);
            if (!Page(x, y)) return C(x < half ? c[3] : c[2]);
            int d = Math.Abs(x - half), row = y - Top(d);
            if (d == 0) return C(PageEdge);
            if (row == pageH) return C(PageEdge);
            if (row % 2 == 1 && row >= 3 && row <= pageH - 2 && d >= 2 && d <= 7 && Hash(x, row, cover) % 5 != 0) return C(Ink);
            return C(x < half ? (d == 1 ? PageB : PageA) : PageB);
        });
    }

    private static readonly Dictionary<char, uint> ParchP = new()
    {
        ['o'] = 0xFF6A4A26, ['d'] = 0xFFB08A50, ['m'] = 0xFFD2AE70, ['l'] = 0xFFEED6A0, ['H'] = 0xFFFFF2D0,
        ['q'] = 0xFF6E101C, ['r'] = 0xFFC0283A, ['R'] = 0xFFE85060, ['i'] = 0xFF8A6A48,
    };

    private static readonly string[] ScrollRolled =
    [
        ".ooooooqqqoooooo.",
        "omHHHHHqRqHHHHHlo",
        "odlllllqrqlllllmo",
        "odmmmmmqrqmmmmmdo",
        "oodddddqqqddddddo",
        ".ooooooooooooooo.",
    ];

    private static readonly string[] ScrollOpen =
    [
        ".ooooooooooo.",
        "oHHlllllllmmo",
        "oddmmmmmmmddo",
        ".ooooooooooo.",
        "..oHllllllo..",
        "..oliiilili..",
        "..olllllllo..",
        "..oliiliiio..",
        "..olllllllo..",
        "..oliiiilmo..",
        "..olllllllo..",
        "..oliilllmo..",
        "..olllllmmo..",
        ".ooooooooooo.",
        "oHHlllllllmmo",
        "oddmmmmmmmddo",
        ".ooooooooooo.",
    ];

    private static readonly Dictionary<char, uint> EnvelopeP = new()
    {
        ['o'] = 0xFF6E6A60, ['d'] = 0xFFB8B0A0, ['m'] = 0xFFD6D0C0, ['l'] = 0xFFF4F0E4, ['H'] = 0xFFFFFFFF,
        ['z'] = 0xFF6A1018, ['s'] = 0xFFB0202E, ['S'] = 0xFFE04A50,
    };

    private static readonly string[] Envelope =
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

    private static PixelImage Paper(int w, int h, bool parchment, int seed)
    {
        uint o = parchment ? 0xFF6A4A26 : 0xFF6E6A60, b = parchment ? 0xFFEED6A0 : 0xFFF6F3EA, s = parchment ? 0xFFD2AE70 : 0xFFD8D3C6, ink = parchment ? 0xFF8A6A48 : 0xFF8A90A0;
        bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && !(x - (w - 4) > y);
        return Paint(w, h, (x, y) =>
        {
            if (!In(x, y)) return Color32.Transparent;
            if (Edge(In, x, y)) return C(o);
            if (x >= w - 4 && y <= 3 && x - (w - 4) >= y - 1) return C(s);
            if (y % 2 == 0 && y >= 3 && y <= h - 3 && x >= 2 && x <= w - 3 && Hash(x, y, seed) % 6 != 0) return C(ink);
            return C(x == w - 2 || y == h - 2 ? s : b);
        });
    }

    /// <summary>깃펜: 오른쪽 위 깃 끝에서 왼쪽 아래 펜촉으로 45° 축, 줄마다 축 왼쪽(빛 받는 쪽)·오른쪽 깃 폭.</summary>
    private static PixelImage Quill(uint[] t)
    {
        int[] left = [0, 1, 2, 2, 3, 3, 1, 3, 2, 2, 1, 0, 0, 0];
        int[] right = [0, 0, 1, 1, 1, 2, 2, 2, 2, 0, 1, 0, 0, 0];
        var px = new Dictionary<(int X, int Y), uint>();
        for (int y = 0; y < left.Length; y++)
        {
            int s = 14 - y, row = y + 1;
            px[(s, row)] = y >= 13 ? 0xFF2A2A34 : y >= 11 ? 0xFFE8DCC0 : t[4];
            for (int k = 1; k <= left[y]; k++) px[(s - k, row)] = k == left[y] ? t[3] : t[4];
            for (int k = 1; k <= right[y]; k++) px[(s + k, row)] = k == right[y] ? t[1] : t[2];
        }
        return Paint(18, 17, (x, y) =>
        {
            if (px.TryGetValue((x, y), out var c)) return C(c);
            bool near = px.ContainsKey((x - 1, y)) || px.ContainsKey((x + 1, y)) || px.ContainsKey((x, y - 1)) || px.ContainsKey((x, y + 1));
            return near ? C(y >= 11 ? 0xFF4A3E2E : t[0]) : Color32.Transparent;
        });
    }

    private static readonly uint[] WhiteFeather = [0xFF4A4E60, 0xFF9CA0B2, 0xFFC4C8D4, 0xFFE8EAF0, 0xFFFFFFFF];
    private static readonly uint[] HawkFeather = [0xFF4A2E18, 0xFF8A5A2E, 0xFFB07A40, 0xFFD49A58, 0xFFF0C890];

    /// <summary>책·종이: 여러 색·두께의 누운 책, 쌓은 책, 세운 책 묶음, 펼친 책, 두루마리, 흩어진 종이, 편지 봉투, 깃펜. 서재·책상 위에.</summary>
    public static StampSet BooksAndPapers()
    {
        var shapes = new List<PixelImage>
        {
            LyingBook(14, 2, Red),
            LyingBook(12, 3, Blue),
            LyingBook(16, 4, Brown),
            BookStack((15, 3, Red, 0), (12, 2, Green, 2)),
            BookStack((16, 4, Brown, 0), (14, 2, Blue, 1), (11, 2, Purple, 3)),
            BookStack((16, 3, Purple, 0), (12, 3, Green, 3), (13, 2, Teal, 1), (10, 2, Red, 4)),
            Shelf((4, 13, Purple, false), (5, 15, Teal, true), (4, 12, Red, false)),
            LeaningShelf(),
            OpenBook(Brown),
            OpenBook(Red),
            Art(ScrollRolled, ParchP),
            Art(ScrollOpen, ParchP),
            Rotate(Paper(9, 12, false, 1), 9, 0xFF6E6A60),
            Rotate(Paper(10, 12, false, 2), -12, 0xFF6E6A60),
            Rotate(Paper(9, 11, true, 3), -6, 0xFF6A4A26),
            Art(Envelope, EnvelopeP),
        };
        shapes.AddRange(Mirrored([Quill(WhiteFeather)]));
        shapes.Add(Quill(HawkFeather));
        return Set("Books & Papers", shapes);
    }
}
