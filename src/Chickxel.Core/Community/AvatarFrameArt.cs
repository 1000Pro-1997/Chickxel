using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.Core.Community;

/// <summary>테두리 고리 몸통을 칠하는 방법.</summary>
public enum FrameRing
{
    /// <summary>금속: 왼쪽 위에서 빛 — 밝은 면·가운데·그늘 세 단계.</summary>
    Metal,
    /// <summary>나무: 금속처럼 칠하고 옹이 무늬.</summary>
    Wood,
    /// <summary>두 색 줄무늬가 고리를 따라 행진.</summary>
    Stripes,
    /// <summary>두 색 결정 면이 고리를 따라 흐름.</summary>
    Flow,
    /// <summary>무지개가 고리를 따라 돎.</summary>
    Rainbow,
    /// <summary>네온관: 두 색 사이를 숨 쉬듯 오가고 바깥으로 번짐(바깥 선 대신 빛).</summary>
    Neon,
    /// <summary>불꽃: 주황 고리 바깥으로 불길이 날름거림(위쪽이 더 높음).</summary>
    Flame,
    /// <summary>용암: 끓는 붉은 무늬가 고리 안에서 흐름.</summary>
    Lava,
}

/// <summary>고리에 붙는 장식(여럿 겹칠 수 있음). 작은 모양에서는 작게 줄인 장식.</summary>
[Flags]
public enum FrameOrnament
{
    None = 0,
    /// <summary>리벳(작게 4, 크게 8).</summary>
    Rivets = 1,
    /// <summary>잎과 차례로 피는 꽃.</summary>
    Flowers = 2,
    /// <summary>양옆 날개(퍼덕임).</summary>
    Wings = 4,
    /// <summary>위에 얹은 왕관(보석이 차례로 반짝).</summary>
    Crown = 8,
    /// <summary>둘레를 도는 결정 조각.</summary>
    Shards = 16,
    /// <summary>위로 휜 두 뿔.</summary>
    Horns = 32,
    /// <summary>머리 위에 뜬 빛나는 고리.</summary>
    Halo = 64,
    /// <summary>불꽃 날개와 정수리 볏(불길 고리 전용).</summary>
    FlameWings = 128,
    /// <summary>정수리에 돋은 떡잎 새싹(잎끝이 살랑임).</summary>
    Sprout = 256,
}

/// <summary>
/// 테두리 모양 하나(<c>frame.*</c> 보상): 고리 몸통·색, 지나가는 빛, 보석, 둘레 빛, 장식, 도는 반짝이, 불티. 같은 모양으로 작은 것·큰 것을 그린다(<see cref="AvatarFrameArt"/>).
/// 모든 주기는 <see cref="RewardArt.Loop"/>의 약수.
/// </summary>
public sealed record FrameStyle(string Id)
{
    public FrameRing Ring { get; init; }
    /// <summary>금속 세 단계와 바깥 선.</summary>
    public Color32 Light { get; init; }
    public Color32 Mid { get; init; }
    public Color32 Dark { get; init; }
    public Color32 Line { get; init; }
    /// <summary>줄무늬·결정 면·네온의 두 색.</summary>
    public Color32 A { get; init; }
    public Color32 B { get; init; }
    /// <summary>지나가는 빛 주기(프레임, 0 = 없음).</summary>
    public int Glint { get; init; }
    public int Gems { get; init; }
    public Color32[] GemColors { get; init; } = [];
    public BadgeGlow Glow { get; init; }
    public Color32 GlowColor { get; init; }
    public FrameOrnament Ornaments { get; init; }
    /// <summary>날개·뿔·왕관 색(밝은 면·가운데·그늘·선).</summary>
    public Color32[] OrnamentColors { get; init; } = [];
    public int Orbiters { get; init; }
    public SparkleShape OrbitShape { get; init; }
    public Color32 OrbitColor { get; init; } = Hex(0xFFF3B0);
    public bool RainbowOrbit { get; init; }
    public int Embers { get; init; }
    public Color32[] EmberColors { get; init; } = [];

    public bool Animated => Glint > 0 || Gems > 0 || Glow != BadgeGlow.None || Orbiters > 0 || Embers > 0
        || Ring is not (FrameRing.Metal or FrameRing.Wood)
        || (Ornaments & ~FrameOrnament.Rivets & ~FrameOrnament.Horns) != 0;

    private static readonly Color32[] SilverWing = [Hex(0xFFFFFF), Hex(0xC9D1DB), Hex(0x8A94A3), Hex(0x343B45)];
    private static readonly Color32[] WhiteWing = [Hex(0xFFFFFF), Hex(0xF1F5F9), Hex(0xCBD5E1), Hex(0x64748B)];
    private static readonly Color32[] GoldWing = [Hex(0xFFF3B0), Hex(0xFFD23F), Hex(0xC98A0A), Hex(0x5A3A00)];
    private static readonly Color32[] NightWing = [Hex(0x6D5BD0), Hex(0x3B2A7A), Hex(0x1E1446), Hex(0x0A0620)];
    private static readonly Color32[] GoldCrown = [Hex(0xFFF3B0), Hex(0xFFD23F), Hex(0xC98A0A), Hex(0x5A3A00)];
    private static readonly Color32[] DragonHorn = [Hex(0xFDF6E3), Hex(0xD6C7A1), Hex(0x8C7A55), Hex(0x2E2615)];

    private static Color32[] Metal3(uint light, uint mid, uint dark, uint line) => [Hex(light), Hex(mid), Hex(dark), Hex(line)];

    private static FrameStyle M(string id, uint light, uint mid, uint dark, uint line) => new(id)
    {
        Ring = FrameRing.Metal, Light = Hex(light), Mid = Hex(mid), Dark = Hex(dark), Line = Hex(line),
    };

    private static readonly FrameStyle[] Styles =
    [
        // ── 1~50
        M("frame.wood", 0xC69057, 0x9C6A3A, 0x6E4626, 0x3A2410) with { Ring = FrameRing.Wood },
        M("frame.medal", 0xF2B880, 0xC9864A, 0x8A5228, 0x43260F) with { Glint = 60, Ornaments = FrameOrnament.Rivets },
        M("frame.bronze", 0xF2B880, 0xC9864A, 0x8A5228, 0x43260F) with { Glint = 60 },
        M("frame.silver", 0xFFFFFF, 0xC9D1DB, 0x8A94A3, 0x343B45) with { Glint = 40 },
        M("frame.silver-wings", 0xFFFFFF, 0xC9D1DB, 0x8A94A3, 0x343B45) with { Glint = 40, Ornaments = FrameOrnament.Wings, OrnamentColors = SilverWing },
        new("frame.marching") { Ring = FrameRing.Stripes, A = Hex(0xFFD54A), B = Hex(0xFFFFFF), Line = Hex(0x2B2B33) },
        M("frame.gold", 0xFFF3B0, 0xFFD23F, 0xC98A0A, 0x5A3A00) with
        {
            Glint = 30, Gems = 4, GemColors = [Hex(0xFF3B5C), Hex(0x3B82F6), Hex(0x22C55E), Hex(0xA855F7)],
        },
        M("frame.crown", 0xFFF3B0, 0xFFD23F, 0xC98A0A, 0x5A3A00) with
        {
            Glint = 30, Gems = 4, GemColors = [Hex(0xFF3B5C), Hex(0x3B82F6), Hex(0x22C55E), Hex(0xA855F7)],
            Ornaments = FrameOrnament.Crown, OrnamentColors = GoldCrown,
        },
        new("frame.neon") { Ring = FrameRing.Neon, A = Hex(0xFF3EA5), B = Hex(0x22D3EE) },
        new("frame.crystal") { Ring = FrameRing.Flow, A = Hex(0x7FE9FA), B = Hex(0xB79CFF), Line = Hex(0x2A1E6B), Glow = BadgeGlow.Solid, GlowColor = Hex(0x8B5CF6) },
        new("frame.crystal-shards")
        {
            Ring = FrameRing.Flow, A = Hex(0x7FE9FA), B = Hex(0xB79CFF), Line = Hex(0x2A1E6B), Glow = BadgeGlow.Solid, GlowColor = Hex(0x8B5CF6),
            Ornaments = FrameOrnament.Shards,
        },
        M("frame.vine", 0x86EFAC, 0x34C76A, 0x1E8E47, 0x14532D) with
        {
            Ornaments = FrameOrnament.Flowers, OrnamentColors = [Hex(0xF9A8D4), Hex(0xFDE68A), Hex(0x4ADE80), Hex(0x166534)],
        },
        M("frame.flame", 0xFFB020, 0xE25A00, 0xA83200, 0x4A0A00) with { Ring = FrameRing.Flame },
        M("frame.phoenix", 0xFFB020, 0xE25A00, 0xA83200, 0x4A0A00) with
        {
            Ring = FrameRing.Flame, Ornaments = FrameOrnament.FlameWings, Embers = 4, EmberColors = [Hex(0xFFE066), Hex(0xFF9A1F)],
        },
        new("frame.rainbow") { Ring = FrameRing.Rainbow, Line = Hex(0x2A0A3A), Orbiters = 2 },

        // ── 51~100: 높을수록 장식·움직임이 많아짐
        M("frame.sakura", 0xFFE4EF, 0xF9A8C9, 0xD9608F, 0x5A1730) with
        {
            Glint = 60, Ornaments = FrameOrnament.Flowers, OrnamentColors = [Hex(0xFFD1E3), Hex(0xFFFFFF), Hex(0xF472B6), Hex(0x9D174D)],
            Orbiters = 2, OrbitShape = SparkleShape.Leaf, OrbitColor = Hex(0xF9A8D4),
        },
        new("frame.frost")
        {
            Ring = FrameRing.Flow, A = Hex(0xF0FBFF), B = Hex(0x8CCDEB), Line = Hex(0x1E4E79), Glow = BadgeGlow.Solid, GlowColor = Hex(0x7FDBFF),
            Orbiters = 3, OrbitShape = SparkleShape.Snow, OrbitColor = Hex(0xE0F7FF),
        },
        new("frame.candy") { Ring = FrameRing.Stripes, A = Hex(0xFF5C8A), B = Hex(0xFFFFFF), Line = Hex(0x7A1037), Glint = 40 },
        M("frame.emerald", 0xFFF3B0, 0xFFD23F, 0xC98A0A, 0x5A3A00) with
        {
            Glint = 30, Gems = 8, GemColors = [Hex(0x22C55E), Hex(0x10B981)],
        },
        M("frame.dragon", 0xBEF264, 0x65A30D, 0x365314, 0x14200A) with
        {
            Glint = 40, Ornaments = FrameOrnament.Horns, OrnamentColors = DragonHorn, Embers = 2, EmberColors = [Hex(0xFFD54A), Hex(0xFF8A1F)],
        },
        new("frame.electric")
        {
            Ring = FrameRing.Neon, A = Hex(0xFDE047), B = Hex(0x60A5FA), Orbiters = 3, OrbitColor = Hex(0xFFFFFF), Embers = 2, EmberColors = [Hex(0xFDE047), Hex(0xBFDBFE)],
        },
        M("frame.ruby", 0xFECACA, 0xDC2626, 0x7F1D1D, 0x3B0A0A) with
        {
            Glint = 30, Gems = 8, GemColors = [Hex(0xFFFFFF), Hex(0xFDA4AF)], Glow = BadgeGlow.Solid, GlowColor = Hex(0xF43F5E),
        },
        M("frame.halo", 0xFFFBEB, 0xFDE68A, 0xD9A21B, 0x7A5A00) with
        {
            Glint = 30, Ornaments = FrameOrnament.Halo | FrameOrnament.Wings, OrnamentColors = WhiteWing, Glow = BadgeGlow.Solid, GlowColor = Hex(0xFDE68A),
        },
        new("frame.ocean")
        {
            Ring = FrameRing.Stripes, A = Hex(0x38BDF8), B = Hex(0x1E3A8A), Line = Hex(0x0B1E4A), Glint = 30,
            Orbiters = 3, OrbitShape = SparkleShape.Star, OrbitColor = Hex(0xBAE6FD),
        },
        M("frame.lava", 0xFFB020, 0xB01E00, 0x5A0A00, 0x2A0500) with
        {
            Ring = FrameRing.Lava, Glow = BadgeGlow.Solid, GlowColor = Hex(0xFF6A00), Embers = 4, EmberColors = [Hex(0xFFD54A), Hex(0xFF8A1F), Hex(0xFF4A1A)],
        },
        M("frame.night-wings", 0x8B7CF6, 0x4C3BB0, 0x241A5E, 0x0A0620) with
        {
            Glint = 40, Ornaments = FrameOrnament.Wings | FrameOrnament.Horns, OrnamentColors = NightWing, Glow = BadgeGlow.Solid, GlowColor = Hex(0x7C3AED),
        },
        new("frame.galaxy")
        {
            Ring = FrameRing.Flow, A = Hex(0x4C1D95), B = Hex(0x1E3A8A), Line = Hex(0x0A0620), Glow = BadgeGlow.Solid, GlowColor = Hex(0x7C5CFF),
            Orbiters = 4, OrbitColor = Hex(0xFFFFFF), Ornaments = FrameOrnament.Shards,
        },
        new("frame.prism") { Ring = FrameRing.Rainbow, Line = Hex(0x1A0A2A), Glow = BadgeGlow.Rainbow, Orbiters = 3, RainbowOrbit = true },
        M("frame.royal", 0xFFF3B0, 0xFFD23F, 0xC98A0A, 0x5A3A00) with
        {
            Glint = 24, Gems = 8, GemColors = [Hex(0xFF3B5C), Hex(0x3B82F6), Hex(0x22C55E), Hex(0xA855F7)],
            Ornaments = FrameOrnament.Crown | FrameOrnament.Wings, OrnamentColors = GoldWing, Glow = BadgeGlow.Solid, GlowColor = Hex(0xFFD54A),
            Orbiters = 2, OrbitColor = Hex(0xFFF3B0),
        },
        new("frame.celestial")
        {
            Ring = FrameRing.Rainbow, Line = Hex(0x3A2A00), Glow = BadgeGlow.Rainbow, Glint = 24,
            Ornaments = FrameOrnament.Halo | FrameOrnament.Wings, OrnamentColors = WhiteWing,
            Orbiters = 4, RainbowOrbit = true, Embers = 3, EmberColors = [Hex(0xFFF3B0), Hex(0xFFFFFF)],
        },

        // ── 레벨 튜토리얼 보상(LevelRewards.Tutorial)
        M("frame.sprout", 0xD9F99D, 0x84CC16, 0x4D7C0F, 0x1A2E05) with
        {
            Glint = 60, Ornaments = FrameOrnament.Sprout, OrnamentColors = [Hex(0xD9F99D), Hex(0x65C32A), Hex(0x3F7A12), Hex(0x1A2E05)],
        },
    ];

    private static readonly Dictionary<string, FrameStyle> ById = Styles.ToDictionary(s => s.Id, StringComparer.Ordinal);

    public static IEnumerable<FrameStyle> All => Styles;

    public static FrameStyle? Find(string? id) => id is not null && ById.TryGetValue(id, out var style) ? style : null;
}

/// <summary>
/// 둥근 프로필 그림 둘레의 픽셀 테두리(<c>frame.*</c> 보상, <see cref="FrameStyle"/>). 같은 모양을 작은 것(<see cref="Large"/> = false — 목록·이름 줄의 작은 프로필 그림,
/// 고리 세 칸, 여백 <see cref="SmallMargin"/>, 장식은 작게)과 큰 것(프로필 머리·계정 창 — 고리 네 칸, 여백 <see cref="LargeMargin"/>, 장식을 크게)으로 그린다.
/// 프로필 그림 지름 <see cref="Diameter"/>(픽셀 그림 칸)에 맞춰 그때그때 그리고, 고리 안(프로필 그림 자리)은 비워 둔다.
/// </summary>
public sealed class AvatarFrameArt : IPixelArt
{
    /// <summary>작은·큰 테두리 둘레 여백(장식·빛 자리, 픽셀). 작은 모양에 날개·왕관·뿔·고리 장식이 있으면 <see cref="SmallOrnamentMargin"/>.</summary>
    public const int SmallMargin = 3, SmallOrnamentMargin = 5, LargeMargin = 12;

    private const FrameOrnament OutsideOrnaments = FrameOrnament.Wings | FrameOrnament.Crown | FrameOrnament.Horns | FrameOrnament.Halo | FrameOrnament.FlameWings | FrameOrnament.Sprout;

    private readonly FrameStyle _style;
    private readonly double _center, _radius;
    private readonly int _bands;

    /// <summary>테두리 <paramref name="id"/>를 지름 <paramref name="diameter"/>칸의 프로필 그림에 맞춰 만든다(<paramref name="large"/>면 큰 모양).</summary>
    public AvatarFrameArt(string id, int diameter, bool large)
    {
        _style = FrameStyle.Find(id) ?? throw new ArgumentException("unknown frame: " + id, nameof(id));
        Id = id;
        Large = large;
        Diameter = Math.Max(4, diameter);
        Margin = large ? LargeMargin : (_style.Ornaments & OutsideOrnaments) != 0 ? SmallOrnamentMargin : SmallMargin;
        _bands = large ? 4 : 3;
        _center = Width / 2.0;
        _radius = Diameter / 2.0;
    }

    public static bool IsKnown(string? id) => FrameStyle.Find(id) is not null;

    public string Id { get; }
    public bool Large { get; }
    public int Diameter { get; }
    public int Margin { get; }
    public int Width => Diameter + 2 * Margin;
    public int Height => Width;
    public ArtBox Box => new(Margin, Margin, Diameter, Diameter);
    public int Frames => _style.Animated ? Loop : 1;

    public void Render(int frame, Span<Color32> pixels)
    {
        frame = Frames > 1 ? Mod(frame, Loop) : 0;
        pixels[..(Width * Height)].Clear();
        var p = new PixelPainter(pixels, Width, Height);
        var s = _style;
        if ((s.Ornaments & FrameOrnament.Halo) != 0) Halo(p, frame);
        if ((s.Ornaments & FrameOrnament.Wings) != 0) Wings(p, frame);
        if (s.Glow != BadgeGlow.None) Glow(p, frame);
        if (s.Ring == FrameRing.Flame) FlameRing(p, frame);
        else BodyRing(p, frame);
        if ((s.Ornaments & FrameOrnament.Rivets) != 0) Rivets(p);
        if (s.Gems > 0) Gems(p, frame);
        if ((s.Ornaments & FrameOrnament.Flowers) != 0) Flowers(p, frame);
        if ((s.Ornaments & FrameOrnament.Horns) != 0) Horns(p);
        if ((s.Ornaments & FrameOrnament.Crown) != 0) Crown(p, frame);
        if ((s.Ornaments & FrameOrnament.Sprout) != 0) Sprout(p, frame);
        if ((s.Ornaments & FrameOrnament.Shards) != 0) Shards(p, frame);
        if (s.Orbiters > 0) Orbiters(p, frame);
        if (s.Embers > 0) Embers(p, frame);
    }

    // ───────────── 고리 ─────────────

    /// <summary>
    /// 고리 한 칸마다 <paramref name="paint"/>를 부른다: 칸 번호 k(0 = 그림 가장자리에 겹친 안쪽 칸 …), 각도(라디안, 0 = 오른쪽, 아래가 +),
    /// 빛(왼쪽 위를 보는 정도 −1~1). null이면 그리지 않는다.
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

    private static double AngleGap(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI));

    private static double Turn(double angle) => (angle + Math.PI) / (2 * Math.PI);

    private Color32 Shade(double light, Color32 c) => light > 0.45 ? Lighten(c, 0.45) : light < -0.35 ? Darken(c, 0.3) : c;

    /// <summary>
    /// 고리 몸통: 작은 것은 몸통 두 칸 + 바깥 선, 큰 것은 안쪽 선 + 몸통 두 칸 + 바깥 선(바깥 몸통 칸은 조금 어둡게). 네온은 선 대신 번지는 빛.
    /// </summary>
    private void BodyRing(PixelPainter p, int frame)
    {
        var s = _style;
        double glint = s.Glint > 0 ? 2 * Math.PI * Mod(frame, s.Glint) / s.Glint - Math.PI / 2 : double.NaN;
        var tube = Mix(s.A, s.B, Pulse(frame, 40));
        int segments = Math.Max(8, (int)Math.Round(2 * Math.PI * (_radius + 1) / (Large ? 4 : 3)) / 2 * 2);
        Ring(p, _bands, (k, a, light) =>
        {
            bool outer = k == _bands - 1, inner = Large && k == 0;
            if (s.Ring == FrameRing.Neon)
            {
                if (outer) return Fade(tube, 0.3 + 0.3 * Pulse(frame, 20));
                return light > 0.45 ? Lighten(tube, 0.5) : inner ? Darken(tube, 0.15) : tube;
            }
            if (outer || inner) return s.Ring == FrameRing.Metal || s.Ring == FrameRing.Wood || s.Ring == FrameRing.Lava ? s.Line : s.Line.A == 0 ? Darken(s.A, 0.6) : s.Line;
            Color32 c = s.Ring switch
            {
                FrameRing.Metal => light > 0.45 ? s.Light : light < -0.35 ? s.Dark : s.Mid,
                FrameRing.Wood => WoodAt(a, light),
                FrameRing.Stripes => Shade(light, Mod((int)Math.Floor(Turn(a) * segments + frame / 4.0), 2) == 0 ? s.A : s.B),
                FrameRing.Flow => Shade(light, Mod((int)Math.Floor(Turn(a) * 10 + frame / 12.0), 2) == 0 ? s.A : s.B),
                FrameRing.Rainbow => Shade(light, RainbowAt(Turn(a) + frame / 60.0)),
                FrameRing.Lava => LavaAt(a, k, frame),
                _ => s.Mid,
            };
            if (k == _bands - 2) c = Darken(c, 0.12);
            if (!double.IsNaN(glint))
            {
                double gap = AngleGap(a, glint);
                c = gap < 0.3 ? Lighten(c, 0.75) : gap < 0.6 ? Lighten(c, 0.35) : c;
            }
            return c;
        });
    }

    private Color32 WoodAt(double a, double light)
    {
        var s = _style;
        var c = light > 0.45 ? s.Light : light < -0.35 ? s.Dark : s.Mid;
        return HashInt((int)Math.Floor((a + Math.PI) * 5), 3, 11, 4) == 0 ? Darken(c, 0.25) : c;
    }

    private static Color32 LavaAt(double a, int k, int frame)
    {
        double v = Math.Sin(a * 6 + 2 * Math.PI * frame / 40) + Math.Sin(a * 11 - 2 * Math.PI * frame / 30 + k) + HashUnit((int)(a * 20), k, 7) * 0.6;
        Color32[] lava = [Hex(0x5A0A00), Hex(0xB01E00), Hex(0xF05A00), Hex(0xFFB020)];
        return lava[Math.Clamp((int)Math.Floor((v + 2) / 4.6 * lava.Length), 0, lava.Length - 1)];
    }

    /// <summary>고리 바로 바깥 빛(숨 쉬듯, 무지개면 둘레를 돎). 큰 것은 두 겹.</summary>
    private void Glow(PixelPainter p, int frame)
    {
        var s = _style;
        double pulse = Pulse(frame, 20);
        int rings = Large ? 2 : 1;
        Ring(p, _bands + rings, (k, a, _) =>
        {
            int d = k - _bands + 1;
            if (d < 1) return null;
            var c = s.Glow == BadgeGlow.Rainbow ? RainbowAt(Turn(a) + frame / 60.0) : s.GlowColor;
            return Fade(c, d == 1 ? 0.25 + 0.3 * pulse : 0.08 + 0.15 * pulse);
        });
    }

    /// <summary>불꽃 고리: 몸통 위로 불길(큰 것은 더 높게), 불꽃 날개면 왼쪽·오른쪽 위로 길게 + 정수리 볏.</summary>
    private void FlameRing(PixelPainter p, int frame)
    {
        var s = _style;
        bool wings = (s.Ornaments & FrameOrnament.FlameWings) != 0;
        int inner = Large ? 3 : 2;
        int reach = Margin - 1;
        double scale = Large ? 2.2 : 1;
        Ring(p, inner + reach, (k, a, light) =>
        {
            if (k < inner) return Large && k == 0 ? s.Line : light > 0.45 ? s.Light : light < -0.35 ? s.Dark : s.Mid;
            double up = -Math.Sin(a);
            double height = (1 + Math.Max(0, up) * 1.2) * scale;
            if (wings)
            {
                double side = Math.Max(Gauss(AngleGap(a, -Math.PI * 0.85), 0.45), Gauss(AngleGap(a, -Math.PI * 0.15), 0.45));
                height += side * (reach - 1) + Gauss(AngleGap(a, -Math.PI / 2), 0.2) * (Large ? 3 : 1);
            }
            int bucket = (int)Math.Floor(Turn(a) * (Large ? 40 : 24));
            double flicker = HashUnit(bucket, frame / 2, wings ? 5 : 3) * 1.4 - 0.4;
            int depth = k - inner + 1;
            if (depth > height + flicker) return null;
            double t = depth / Math.Max(1.0, height + flicker);
            return t < 0.34 ? Hex(0xFFE066) : t < 0.67 ? Hex(0xFF9A1F) : Hex(0xE0401A);
        });
    }

    private static double Gauss(double x, double width) => Math.Exp(-x * x / (2 * width * width));

    /// <summary>몸통 가운데 칸의 반지름(장식을 얹는 자리).</summary>
    private double BodyRadius => _radius - 0.6 + (Large ? 2 : 1);

    private (int X, int Y) At(double angle, double r) => ((int)Math.Floor(_center + Math.Cos(angle) * r), (int)Math.Floor(_center + Math.Sin(angle) * r));

    // ───────────── 장식 ─────────────

    private void Rivets(PixelPainter p)
    {
        int n = Large ? 8 : 4;
        for (int i = 0; i < n; i++)
        {
            var (x, y) = At(Math.PI / 4 + i * 2 * Math.PI / n, BodyRadius);
            p.Set(x, y, Lighten(_style.Light, 0.4));
            if (Large) p.Set(x, y + 1, Darken(_style.Dark, 0.2));
        }
    }

    /// <summary>보석: 고리 위에 고르게, 하나씩 차례로 반짝.</summary>
    private void Gems(PixelPainter p, int frame)
    {
        var s = _style;
        int n = Large ? s.Gems : Math.Min(4, s.Gems);
        int step = Loop / n;
        for (int i = 0; i < n; i++)
        {
            var (x, y) = At(Math.PI / 4 + i * 2 * Math.PI / n, BodyRadius);
            var gem = s.GemColors[i % s.GemColors.Length];
            bool lit = Mod(frame, Loop) / step == i && Mod(frame, step) < 8;
            p.Set(x, y, lit ? Color32.White : gem);
            if (Large)
            {
                p.Set(x + 1, y, Darken(gem, 0.3));
                p.Set(x, y + 1, Darken(gem, 0.45));
                p.Set(x + 1, y + 1, Darken(gem, 0.55));
            }
        }
    }

    /// <summary>잎(바깥)과 네 송이 꽃이 차례로 핀다(작은 것은 꽃이 점 하나 크기).</summary>
    private void Flowers(PixelPainter p, int frame)
    {
        var c = _style.OrnamentColors;
        Color32 petal = c[0], center = c[1], leaf = c[2], leafDark = c[3];
        double r = BodyRadius;
        int leaves = Large ? 8 : 4;
        for (int i = 0; i < leaves; i++)
        {
            double a = Math.PI / leaves + i * 2 * Math.PI / leaves;
            var (x, y) = At(a, r + (Large ? 2.5 : 1.5));
            p.Blend(x, y, leaf);
            if (Large) p.Blend(x + (Math.Cos(a) > 0 ? 1 : -1), y, leafDark);
        }
        for (int i = 0; i < 4; i++)
        {
            var (x, y) = At(-Math.PI / 2 + i * Math.PI / 2, r);
            int age = Mod(frame - i * 30, 120);
            bool open = age is >= 4 and < 26;
            if (open)
            {
                int d = Large ? 1 : 1;
                p.Blend(x - d, y, petal); p.Blend(x + d, y, petal); p.Blend(x, y - d, petal); p.Blend(x, y + d, petal);
                if (Large) { p.Blend(x - 1, y - 1, Fade(petal, 0.6)); p.Blend(x + 1, y + 1, Fade(petal, 0.6)); }
                p.Set(x, y, center);
            }
            else p.Set(x, y, petal);
        }
    }

    private static readonly string[][] WingPoses =
    [
        ["aa.......", ".aab.....", ".abbb....", "..abbbb..", "..abbbbbb", "...bcbcbb", "...c.cbcb", ".......c."],
        [".........", "aa.......", ".aabb....", "..abbbb..", "..abbbbbb", "..bcbcbbb", "..c.cbcbc", "....c...c"],
    ];

    private static readonly string[][] MiniWingPoses = [["a..", "ab.", "bbc"], ["...", "aab", "bbc"]];

    /// <summary>양옆 날개: 12프레임마다 자세를 바꿔 퍼덕인다(작은 것은 3×3).</summary>
    private void Wings(PixelPainter p, int frame)
    {
        var c = _style.OrnamentColors;
        Color32 Feather(char ch) => ch switch { 'a' => c[0], 'b' => c[1], _ => c[2] };
        var pose = (Large ? WingPoses : MiniWingPoses)[Mod(frame, 24) < 12 ? 0 : 1];
        int w = pose[0].Length;
        int outer = (int)Math.Ceiling(_radius + (Large ? 3.4 : 2.4));
        int top = (int)Math.Floor(_center) - (Large ? 9 : 2);
        int leftX = (int)Math.Floor(_center) - outer - w + 1, rightX = (int)Math.Floor(_center) + outer;
        p.SpriteOutlined(leftX, top, pose, Feather, c[3]);
        p.SpriteOutlined(rightX, top, pose, Feather, c[3], mirror: true);
    }

    private static readonly string[] CrownRows = ["p...p...p", "y...y...y", "yy.yyy.yy", "yyyyyyyyy", "yRyByRyBy", "ddddddddd"];
    private static readonly string[] MiniCrownRows = ["y.y.y", "yRyBy"];

    /// <summary>왕관: 고리 위에 얹고 진주·보석이 차례로 반짝(작은 것은 5×2).</summary>
    private void Crown(PixelPainter p, int frame)
    {
        var c = _style.OrnamentColors;
        var rows = Large ? CrownRows : MiniCrownRows;
        int x = (int)Math.Floor(_center) - rows[0].Length / 2;
        int y = (int)Math.Floor(_center - _radius - (Large ? 2.4 : 1.6)) - rows.Length + (Large ? 2 : 1);
        int lit = Mod(frame, 120) / 15;
        int jewel = 0;
        p.SpriteOutlined(x, y, rows, ch => ch switch
        {
            'p' => lit % 3 == 0 ? Color32.White : Hex(0xFDF6E3),
            'y' => c[1],
            'd' => c[2],
            'R' => jewel++ % 4 == lit % 4 ? Hex(0xFFB3C0) : Hex(0xE11D48),
            _ => jewel++ % 4 == lit % 4 ? Hex(0xBFDBFE) : Hex(0x2563EB),
        }, c[3]);
        if (Large && Mod(frame, 40) < 10) LevelBadgeArt.Sparkle(p, x + 4, y - 2, Mod(frame, 40) < 5 ? 1 : 2, SparkleShape.Star, Hex(0xFFF3B0));
    }

    // 새싹: a 밝은 잎 · b 잎 · s 줄기(바깥 선은 SpriteOutlined). 두 자세를 오가며 잎끝이 살랑인다.
    private static readonly string[][] SproutPoses =
    [
        ["aa.....aa", "abb...bba", ".abbsbba.", "...bsb...", "....s....", "....s...."],
        [".........", "aab...baa", "abbbsbbba", "...bsb...", "....s....", "....s...."],
    ];
    private static readonly string[][] MiniSproutPoses = [["a...a", "bbsbb", "..s.."], [".....", "absba", "..s.."]];

    /// <summary>정수리 새싹: 고리 위에 줄기를 꽂고 떡잎 두 장이 30프레임마다 자세를 바꿔 살랑인다(작은 것은 5×3).</summary>
    private void Sprout(PixelPainter p, int frame)
    {
        var c = _style.OrnamentColors;
        var rows = (Large ? SproutPoses : MiniSproutPoses)[Mod(frame, 60) < 30 ? 0 : 1];
        int x = (int)Math.Floor(_center) - rows[0].Length / 2;
        // 줄기 밑동이 고리 몸통에 묻히게
        int y = (int)Math.Floor(_center - _radius - (Large ? 1.4 : 0.6)) - rows.Length + 1;
        p.SpriteOutlined(x, y, rows, ch => ch switch { 'a' => c[0], 'b' => c[1], _ => c[2] }, c[3]);
    }

    private static readonly string[] HornRows = ["...a", "..ab", ".abb", "abbc", "bbc.", "bc.."];
    private static readonly string[] MiniHornRows = ["..a", ".ab", "ab."];

    /// <summary>위로 휜 두 뿔(머리 양쪽 위).</summary>
    private void Horns(PixelPainter p)
    {
        var c = _style.OrnamentColors.Length >= 4 ? _style.OrnamentColors : [Hex(0xFDF6E3), Hex(0xD6C7A1), Hex(0x8C7A55), Hex(0x2E2615)];
        var rows = Large ? HornRows : MiniHornRows;
        int w = rows[0].Length, h = rows.Length;
        double r = _radius + (Large ? 1.5 : 0.8);
        var (lx, ly) = At(-Math.PI * 0.72, r);
        var (rx, _) = At(-Math.PI * 0.28, r);
        Color32 Horn(char ch) => ch switch { 'a' => c[0], 'b' => c[1], _ => c[2] };
        // 왼쪽 뿔은 뒤집어 바깥(왼쪽 위)으로 휘게
        p.SpriteOutlined(lx - w + 1, ly - h + 1, rows, Horn, c[3], mirror: true);
        p.SpriteOutlined(rx, ly - h + 1, rows, Horn, c[3]);
    }

    /// <summary>머리 위에 뜬 빛나는 고리(위아래로 살짝 떠다님, 큰 것은 납작한 타원).</summary>
    private void Halo(PixelPainter p, int frame)
    {
        int bob = Pulse(frame, 40) > 0.5 ? 1 : 0;
        var gold = Hex(0xFDE68A);
        var glow = Fade(Hex(0xFFF7CC), 0.35 + 0.35 * Pulse(frame, 20));
        if (!Large)
        {
            int cx = (int)Math.Floor(_center), y = Math.Max(0, (int)Math.Floor(_center - _radius) - 4 - bob);
            for (int dx = -2; dx <= 2; dx++) p.Blend(cx + dx, y, Math.Abs(dx) == 2 ? Fade(gold, 0.8) : gold);
            return;
        }
        double hx = _center, hy = _center - _radius - 6 + bob, rx = Math.Max(4, _radius * 0.75), ry = 1.6;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                double dx = (x + 0.5 - hx) / rx, dy = (y + 0.5 - hy) / ry, e = Math.Sqrt(dx * dx + dy * dy);
                if (Math.Abs(e - 1) < 0.28) p.Blend(x, y, y + 0.5 < hy ? Lighten(gold, 0.4) : gold);
                else if (Math.Abs(e - 1) < 0.6) p.Blend(x, y, glow);
            }
    }

    private static readonly string[] ShardRows = [".a.", "aab", "abb", "abb", ".b."];

    /// <summary>결정 조각이 둘레를 천천히 돈다(큰 것은 여섯 개 결정, 작은 것은 두 점).</summary>
    private void Shards(PixelPainter p, int frame)
    {
        var s = _style;
        int n = Large ? 6 : 2;
        double r = _radius + (Large ? 6.5 : 2.6);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * (i / (double)n + frame / (double)Loop);
            var light = i % 2 == 0 ? Lighten(s.A, 0.5) : Lighten(s.B, 0.5);
            var dark = i % 2 == 0 ? s.A : s.B;
            var (x, y) = At(a, r);
            if (Large) p.SpriteOutlined(x - 1, y - 2, ShardRows, ch => ch == 'a' ? light : dark, Fade(s.Line, 0.8));
            else
            {
                p.Blend(x, y, light);
                p.Blend(x, y + 1, dark);
            }
        }
    }

    /// <summary>반짝이가 고리 바깥을 돈다(크기가 숨 쉬듯 바뀜).</summary>
    private void Orbiters(PixelPainter p, int frame)
    {
        var s = _style;
        double r = _radius + (Large ? 5 : 2);
        for (int i = 0; i < s.Orbiters; i++)
        {
            double a = 2 * Math.PI * (frame / 60.0 + i / (double)s.Orbiters);
            var (x, y) = At(a, r);
            int size = Pulse(frame + i * 15, 30) > 0.5 ? (Large ? 2 : 1) : (Large ? 1 : 0);
            var color = s.RainbowOrbit ? Mix(RainbowAt(i / (double)s.Orbiters + frame / 120.0), Color32.White, 0.35) : s.OrbitColor;
            LevelBadgeArt.Sparkle(p, x, y, size, s.OrbitShape, color);
        }
    }

    /// <summary>불티가 위로 오르며 흐려진다.</summary>
    private void Embers(PixelPainter p, int frame)
    {
        var s = _style;
        int n = Large ? s.Embers : Math.Max(1, s.Embers / 2);
        for (int i = 0; i < n; i++)
        {
            var (age, cycle) = Life(frame, i, n, 12);
            int spread = Large ? 9 : 5;
            int x = (int)Math.Floor(_center) - spread / 2 + HashInt(Seed(Id) + i, cycle, 45, spread);
            int y = (int)Math.Floor(_center - _radius) - (Large ? 3 : 1) - age / 2;
            var c = s.EmberColors.Length > 0 ? s.EmberColors[(i + cycle) % s.EmberColors.Length] : Hex(0xFFE066);
            p.Blend(x, y, Fade(c, 1 - age / 12.0));
        }
    }
}
