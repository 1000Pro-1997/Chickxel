using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.Core.Community;

/// <summary>
/// 둥근 프로필 그림 둘레의 픽셀 테두리(작은 테두리 <c>frame-s.*</c>·큰 테두리 <c>frame-l.*</c> 보상). 프로필 그림 지름 <see cref="Diameter"/>(픽셀 그림 칸)에 맞춰
/// 그때그때 그린다: 그림 가장자리에 살짝 겹치는 고리(왼쪽 위에서 빛 — 밝은 면·가운데·그늘 세 단계, 바깥 테두리선), 모양마다 지나가는 빛·점선 행진·흐르는 색·
/// 보석·꽃·불꽃 같은 움직임. 큰 테두리는 둘레에 장식(리벳·날개·왕관·결정·불꽃 날개)이 붙어 여백이 넓다. 고리 안(프로필 그림 자리)은 비워 둔다.
/// </summary>
public sealed class AvatarFrameArt : IPixelArt
{
    /// <summary>작은·큰 테두리 둘레 여백(장식·빛 자리, 픽셀).</summary>
    public const int SmallMargin = 3, LargeMargin = 12;

    private static readonly HashSet<string> Animated = new(StringComparer.Ordinal)
    {
        "frame-s.bronze", "frame-s.silver", "frame-s.marching", "frame-s.gold", "frame-s.neon", "frame-s.crystal", "frame-s.vine", "frame-s.flame", "frame-s.rainbow",
        "frame-l.bronze", "frame-l.silver-wings", "frame-l.crown", "frame-l.crystal-shards", "frame-l.phoenix",
    };

    private readonly double _center, _radius;

    /// <summary>테두리 <paramref name="id"/>를 지름 <paramref name="diameter"/>칸의 프로필 그림에 맞춰 만든다.</summary>
    public AvatarFrameArt(string id, int diameter)
    {
        if (!IsKnown(id)) throw new ArgumentException("unknown frame: " + id, nameof(id));
        Id = id;
        Large = id.StartsWith("frame-l.", StringComparison.Ordinal);
        Diameter = Math.Max(4, diameter);
        Margin = Large ? LargeMargin : SmallMargin;
        _center = Width / 2.0;
        _radius = Diameter / 2.0;
    }

    public static bool IsKnown(string? id) => LevelRewards.Find(id) is { Slot: RewardSlot.FrameSmall or RewardSlot.FrameLarge };

    public string Id { get; }
    public bool Large { get; }
    public int Diameter { get; }
    public int Margin { get; }
    public int Width => Diameter + 2 * Margin;
    public int Height => Width;
    public ArtBox Box => new(Margin, Margin, Diameter, Diameter);
    public int Frames => Animated.Contains(Id) ? Loop : 1;

    public void Render(int frame, Span<Color32> pixels)
    {
        frame = Frames > 1 ? Mod(frame, Loop) : 0;
        pixels[..(Width * Height)].Clear();
        var p = new PixelPainter(pixels, Width, Height);
        switch (Id)
        {
            case "frame-s.wood": Wood(p); break;
            case "frame-s.bronze": MetalRing(p, frame, Bronze, 3, glint: 60); break;
            case "frame-s.silver": MetalRing(p, frame, Silver, 3, glint: 40); break;
            case "frame-s.marching": Marching(p, frame); break;
            case "frame-s.gold": MetalRing(p, frame, Gold, 3, glint: 30); Gems(p, frame, 1); break;
            case "frame-s.neon": Neon(p, frame); break;
            case "frame-s.crystal": CrystalRing(p, frame, 3); break;
            case "frame-s.vine": Vine(p, frame); break;
            case "frame-s.flame": FlameRing(p, frame, wings: false); break;
            case "frame-s.rainbow": RainbowRing(p, frame); break;
            case "frame-l.bronze": MetalRing(p, frame, Bronze, 4, glint: 60); Rivets(p); break;
            case "frame-l.silver-wings": Wings(p, frame); MetalRing(p, frame, Silver, 4, glint: 40); break;
            case "frame-l.crown": MetalRing(p, frame, Gold, 4, glint: 30); Gems(p, frame, 1); Crown(p, frame); break;
            case "frame-l.crystal-shards": CrystalRing(p, frame, 4); Shards(p, frame); break;
            case "frame-l.phoenix": FlameRing(p, frame, wings: true); Embers(p, frame); break;
        }
    }

    // ───────────── 고리 ─────────────

    /// <summary>금속 세 단계(밝은 면·가운데·그늘)와 바깥 선.</summary>
    private sealed record Metal(Color32 Light, Color32 Mid, Color32 Dark, Color32 Line);

    private static readonly Metal Bronze = new(Hex(0xF2B880), Hex(0xC9864A), Hex(0x8A5228), Hex(0x43260F));
    private static readonly Metal Silver = new(Hex(0xFFFFFF), Hex(0xC9D1DB), Hex(0x8A94A3), Hex(0x343B45));
    private static readonly Metal Gold = new(Hex(0xFFF3B0), Hex(0xFFD23F), Hex(0xC98A0A), Hex(0x5A3A00));

    /// <summary>
    /// 고리 한 칸마다 <paramref name="paint"/>를 부른다: 칸 번호 k(0 = 그림 가장자리에 겹친 안쪽 칸 … <paramref name="bands"/>−1 = 바깥),
    /// 각도(라디안, 0 = 오른쪽, 아래가 +), 빛(왼쪽 위를 보는 정도 −1~1). null이면 그 칸은 그리지 않는다.
    /// </summary>
    private void Ring(PixelPainter p, int bands, Func<int, double, double, Color32?> paint)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                double dx = x + 0.5 - _center, dy = y + 0.5 - _center, r = Math.Sqrt(dx * dx + dy * dy);
                int k = (int)Math.Floor(r - _radius + 0.6);
                if (k < 0 || k >= bands) continue;
                double light = r > 0 ? (dx * -0.6 + dy * -0.8) / r : 0;
                if (paint(k, Math.Atan2(dy, dx), light) is { } c) p.Blend(x, y, c);
            }
    }

    private static Color32 Shade(double light, Metal m) => light > 0.45 ? m.Light : light < -0.35 ? m.Dark : m.Mid;

    /// <summary>각도 <paramref name="a"/>와 <paramref name="b"/>의 차이(0~π).</summary>
    private static double AngleGap(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI));

    /// <summary>지나가는 빛의 지금 각도(위에서 시작해 시계 방향으로 한 바퀴, 주기 <paramref name="period"/>).</summary>
    private static double GlintAngle(int frame, int period) => 2 * Math.PI * Mod(frame, period) / period - Math.PI / 2;

    private static Color32 WithGlint(Color32 c, double angle, double glint)
    {
        double gap = AngleGap(angle, glint);
        return gap < 0.3 ? Lighten(c, 0.75) : gap < 0.6 ? Lighten(c, 0.35) : c;
    }

    /// <summary>
    /// 금속 고리: 작은 테두리(<paramref name="bands"/> = 3)는 금속 두 칸 + 바깥 선, 큰 테두리(4)는 안쪽 선 + 금속 두 칸 + 바깥 선(바깥 금속 칸은 조금 어둡게).
    /// <paramref name="glint"/> 주기로 빛이 고리를 한 바퀴 돈다.
    /// </summary>
    private void MetalRing(PixelPainter p, int frame, Metal m, int bands, int glint)
    {
        double g = GlintAngle(frame, glint);
        Ring(p, bands, (k, a, light) =>
        {
            if (k == bands - 1 || (bands == 4 && k == 0)) return m.Line;
            var c = Shade(light, m);
            if (k == bands - 2) c = Darken(c, 0.12);
            return WithGlint(c, a, g);
        });
    }

    private void Wood(PixelPainter p)
        => Ring(p, 3, (k, a, light) =>
        {
            if (k == 2) return Hex(0x3A2410);
            var c = Shade(light, new Metal(Hex(0xC69057), Hex(0x9C6A3A), Hex(0x6E4626), Hex(0x3A2410)));
            // 나뭇결 옹이
            return HashInt((int)Math.Floor((a + Math.PI) * 5), 3, 11, 4) == 0 ? Darken(c, 0.25) : c;
        });

    /// <summary>노랑·흰 점선이 고리를 따라 행진한다.</summary>
    private void Marching(PixelPainter p, int frame)
    {
        int segments = Math.Max(8, (int)Math.Round(2 * Math.PI * (_radius + 0.5) / 3) / 2 * 2);
        Ring(p, 3, (k, a, light) =>
        {
            if (k == 2) return Hex(0x2B2B33);
            int seg = (int)Math.Floor((a + Math.PI) / (2 * Math.PI) * segments + frame / 4.0);
            var c = Mod(seg, 2) == 0 ? Hex(0xFFD54A) : Hex(0xFFFFFF);
            return light < -0.35 ? Darken(c, 0.2) : c;
        });
    }

    /// <summary>네 모서리 보석(빨강·파랑·초록·보라)이 차례로 반짝인다. <paramref name="k"/> = 보석을 놓을 고리 칸.</summary>
    private void Gems(PixelPainter p, int frame, int k)
    {
        Color32[] gems = [Hex(0xFF3B5C), Hex(0x3B82F6), Hex(0x22C55E), Hex(0xA855F7)];
        double r = _radius - 0.6 + k + 0.5;
        for (int i = 0; i < 4; i++)
        {
            double a = Math.PI / 4 + i * Math.PI / 2;
            int x = (int)Math.Floor(_center + Math.Cos(a) * r), y = (int)Math.Floor(_center + Math.Sin(a) * r);
            bool lit = Mod(frame, 120) / 30 == i && Mod(frame, 30) < 8;
            p.Set(x, y, lit ? Color32.White : gems[i]);
            if (Large) p.Set(x + 1, y, Darken(gems[i], 0.3));
        }
    }

    private void Neon(PixelPainter p, int frame)
    {
        var tube = Mix(Hex(0xFF3EA5), Hex(0x22D3EE), Pulse(frame, 40));
        double glow = Pulse(frame, 20);
        Ring(p, 3, (k, a, light) => k switch
        {
            0 or 1 => light > 0.45 ? Lighten(tube, 0.5) : k == 1 ? Darken(tube, 0.1) : tube,
            _ => Fade(tube, 0.3 + 0.3 * glow),
        });
    }

    /// <summary>청록·보라 결정 면이 고리를 따라 흐르고 바깥에 보라 빛이 숨 쉰다.</summary>
    private void CrystalRing(PixelPainter p, int frame, int bands)
    {
        double glow = Pulse(frame, 20);
        Ring(p, bands + 1, (k, a, light) =>
        {
            if (k == bands) return Fade(Hex(0x8B5CF6), 0.2 + 0.3 * glow);
            if (k == bands - 1 || (bands == 4 && k == 0)) return Hex(0x2A1E6B);
            int facet = (int)Math.Floor((a + Math.PI) / (2 * Math.PI) * 10 + frame / 12.0);
            var c = Mod(facet, 2) == 0 ? Hex(0x7FE9FA) : Hex(0xB79CFF);
            if (bands == 4 && k == 2) c = Darken(c, 0.15);
            return light > 0.45 ? Lighten(c, 0.5) : light < -0.35 ? Darken(c, 0.3) : c;
        });
    }

    /// <summary>초록 덩굴 고리, 바깥에 잎, 네 송이 꽃이 차례로 핀다.</summary>
    private void Vine(PixelPainter p, int frame)
    {
        Ring(p, 3, (k, a, light) => k == 2 ? Hex(0x14532D) : Shade(light, new Metal(Hex(0x86EFAC), Hex(0x34C76A), Hex(0x1E8E47), Hex(0x14532D))));
        double r = _radius - 0.6 + 1.5;
        for (int i = 0; i < 8; i++)
        {
            double a = Math.PI / 8 + i * Math.PI / 4;
            int x = (int)Math.Floor(_center + Math.Cos(a) * (r + 1)), y = (int)Math.Floor(_center + Math.Sin(a) * (r + 1));
            p.Blend(x, y, Hex(0x4ADE80));
            p.Blend(x + (Math.Cos(a) > 0 ? 1 : -1), y, Hex(0x166534));
        }
        for (int i = 0; i < 4; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 2;
            int x = (int)Math.Floor(_center + Math.Cos(a) * r), y = (int)Math.Floor(_center + Math.Sin(a) * r);
            // 꽃마다 30프레임씩 차례로: 봉오리 → 활짝 → 봉오리
            int age = Mod(frame - i * 30, 120);
            bool open = age is >= 4 and < 26;
            var petal = i % 2 == 0 ? Hex(0xF9A8D4) : Hex(0xFDE68A);
            if (open)
            {
                p.Blend(x - 1, y, petal); p.Blend(x + 1, y, petal); p.Blend(x, y - 1, petal); p.Blend(x, y + 1, petal);
                p.Set(x, y, Hex(0xFACC15));
            }
            else p.Set(x, y, petal);
        }
    }

    /// <summary>
    /// 불꽃 고리: 주황 고리 바깥으로 불길이 날름거린다(위쪽이 더 높음). <paramref name="wings"/>면 왼쪽·오른쪽 위로 불꽃 날개와 정수리 불꽃(불사조).
    /// </summary>
    private void FlameRing(PixelPainter p, int frame, bool wings)
    {
        var ring = new Metal(Hex(0xFFB020), Hex(0xE25A00), Hex(0xA83200), Hex(0x4A0A00));
        int inner = wings ? 3 : 2;
        int reach = Margin - 1;
        Ring(p, inner + reach, (k, a, light) =>
        {
            if (k < inner) return wings && k == 0 ? ring.Line : Shade(light, ring);
            double up = -Math.Sin(a); // 1 = 위
            double height = 1 + Math.Max(0, up) * 1.2;
            if (wings)
            {
                // 날개: 수평보다 조금 위(±30°)에서 바깥으로 길게, 정수리에 볏
                double side = Math.Max(Gauss(AngleGap(a, -Math.PI * 0.85), 0.45), Gauss(AngleGap(a, -Math.PI * 0.15), 0.45));
                height += side * (reach - 1) + Gauss(AngleGap(a, -Math.PI / 2), 0.2) * 3;
            }
            int bucket = (int)Math.Floor((a + Math.PI) / (2 * Math.PI) * (wings ? 40 : 24));
            double flicker = HashUnit(bucket, frame / 2, wings ? 5 : 3) * 1.4 - 0.4;
            int depth = k - inner + 1;
            if (depth > height + flicker) return null;
            double t = depth / Math.Max(1.0, height + flicker);
            return t < 0.34 ? Hex(0xFFE066) : t < 0.67 ? Hex(0xFF9A1F) : Hex(0xE0401A);
        });
    }

    private static double Gauss(double x, double width) => Math.Exp(-x * x / (2 * width * width));

    private void RainbowRing(PixelPainter p, int frame)
    {
        Ring(p, 3, (k, a, light) =>
        {
            if (k == 2) return Hex(0x2A0A3A);
            var c = RainbowAt((a + Math.PI) / (2 * Math.PI) + frame / 60.0);
            return light > 0.45 ? Lighten(c, 0.35) : light < -0.35 ? Darken(c, 0.25) : c;
        });
        // 두 반짝이가 고리 바깥을 돈다
        for (int i = 0; i < 2; i++)
        {
            double a = 2 * Math.PI * frame / 60.0 + i * Math.PI;
            double r = _radius + 2;
            int size = Pulse(frame + i * 15, 30) > 0.5 ? 1 : 0;
            LevelBadgeArt.Sparkle(p, (int)Math.Floor(_center + Math.Cos(a) * r), (int)Math.Floor(_center + Math.Sin(a) * r), size, SparkleShape.Star, Hex(0xFFF3B0));
        }
    }

    // ───────────── 큰 테두리 장식 ─────────────

    /// <summary>동 메달: 금속 위 여덟 리벳.</summary>
    private void Rivets(PixelPainter p)
    {
        double r = _radius - 0.6 + 1.5;
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            int x = (int)Math.Floor(_center + Math.Cos(a) * r), y = (int)Math.Floor(_center + Math.Sin(a) * r);
            p.Set(x, y, Hex(0xFFE0B0));
            p.Set(x, y + 1, Hex(0x6B3A16));
        }
    }

    private static readonly string[][] WingPoses =
    [
        [
            "aa.......",
            ".aab.....",
            ".abbb....",
            "..abbbb..",
            "..abbbbbb",
            "...bcbcbb",
            "...c.cbcb",
            ".......c.",
        ],
        [
            ".........",
            "aa.......",
            ".aabb....",
            "..abbbb..",
            "..abbbbbb",
            "..bcbcbbb",
            "..c.cbcbc",
            "....c...c",
        ],
    ];

    /// <summary>은빛 날개: 고리 양옆에서 천천히 퍼덕인다(12프레임마다 자세 바꿈).</summary>
    private void Wings(PixelPainter p, int frame)
    {
        var pose = WingPoses[Mod(frame, 24) < 12 ? 0 : 1];
        Color32 Feather(char ch) => ch switch { 'a' => Hex(0xFFFFFF), 'b' => Hex(0xC9D1DB), _ => Hex(0x8A94A3) };
        int top = (int)Math.Floor(_center) - 9;
        int outer = (int)Math.Ceiling(_radius + 2.4);
        int leftX = (int)Math.Floor(_center) - outer - 9 + 1, rightX = (int)Math.Floor(_center) + outer;
        p.SpriteOutlined(leftX, top, pose, Feather, Hex(0x343B45));
        p.SpriteOutlined(rightX, top, pose, Feather, Hex(0x343B45), mirror: true);
    }

    private static readonly string[] CrownRows =
    [
        "p...p...p",
        "y...y...y",
        "yy.yyy.yy",
        "yyyyyyyyy",
        "yRyByRyBy",
        "ddddddddd",
    ];

    /// <summary>황금 왕관: 고리 위에 얹고, 진주·보석이 차례로 반짝인다.</summary>
    private void Crown(PixelPainter p, int frame)
    {
        int x = (int)Math.Floor(_center) - 4;
        int y = (int)Math.Floor(_center - _radius - 2.4) - CrownRows.Length + 2;
        int lit = Mod(frame, 120) / 15;
        int jewel = 0;
        p.SpriteOutlined(x, y, CrownRows, ch => ch switch
        {
            'p' => lit % 3 == 0 ? Color32.White : Hex(0xFDF6E3),
            'y' => Hex(0xFFD23F),
            'd' => Hex(0xC98A0A),
            'R' => jewel++ % 4 == lit % 4 ? Hex(0xFFB3C0) : Hex(0xE11D48),
            _ => jewel++ % 4 == lit % 4 ? Hex(0xBFDBFE) : Hex(0x2563EB),
        }, Hex(0x5A3A00));
        // 정점에 별 반짝
        if (Mod(frame, 40) < 10) LevelBadgeArt.Sparkle(p, x + 4, y - 2, Mod(frame, 40) < 5 ? 1 : 2, SparkleShape.Star, Hex(0xFFF3B0));
    }

    private static readonly string[] ShardRows = [".a.", "aab", "abb", "abb", ".b."];

    /// <summary>크리스털 결정 여섯 개가 고리 둘레를 천천히 돈다(되풀이마다 한 바퀴).</summary>
    private void Shards(PixelPainter p, int frame)
    {
        double r = _radius + 5.5;
        for (int i = 0; i < 6; i++)
        {
            double a = 2 * Math.PI * (i / 6.0 + frame / (double)Loop);
            int x = (int)Math.Floor(_center + Math.Cos(a) * r) - 1, y = (int)Math.Floor(_center + Math.Sin(a) * r) - 2;
            var light = i % 2 == 0 ? Hex(0xBFF6FF) : Hex(0xD9CCFF);
            var dark = i % 2 == 0 ? Hex(0x22D3EE) : Hex(0x8B5CF6);
            p.SpriteOutlined(x, y, ShardRows, ch => ch == 'a' ? light : dark, Fade(Hex(0x2A1E6B), 0.8));
        }
    }

    /// <summary>불사조: 정수리 위로 불티가 오른다.</summary>
    private void Embers(PixelPainter p, int frame)
    {
        for (int i = 0; i < 4; i++)
        {
            var (age, cycle) = Life(frame, i, 4, 12);
            int x = (int)Math.Floor(_center) - 4 + HashInt(i, cycle, 45, 9);
            int y = (int)Math.Floor(_center - _radius) - 3 - age / 2;
            p.Blend(x, y, Fade(age < 4 ? Hex(0xFFE066) : Hex(0xFF9A1F), 1 - age / 12.0));
        }
    }
}
