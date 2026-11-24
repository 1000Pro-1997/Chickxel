using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.Core.Community;

/// <summary>배지 바탕을 칠하는 방법.</summary>
public enum BadgeFill
{
    /// <summary>위 밝은 줄·가운데·아래 어두운 줄.</summary>
    Bands,
    /// <summary>두 색이 대각선으로 흐름(네 단계로 끊음).</summary>
    Flow,
    /// <summary>흐르는 무지개 줄무늬.</summary>
    Rainbow,
    /// <summary>띠 + 씨앗 점(딸기).</summary>
    Seeds,
    /// <summary>출렁이는 물결과 거품.</summary>
    Waves,
    /// <summary>끓는 용암.</summary>
    Lava,
    /// <summary>흐르는 성운 + 반짝이는 별.</summary>
    Galaxy,
    /// <summary>얼음 결정 면.</summary>
    Facets,
    /// <summary>어두운 바탕(테두리가 네온으로 빛남).</summary>
    Neon,
}

/// <summary>배지 둘레 빛.</summary>
public enum BadgeGlow { None, Solid, Rainbow, Neon }

/// <summary>배지 둘레·위에 피었다 지는 것의 모양.</summary>
public enum SparkleShape { Star, Heart, Leaf, Snow }

/// <summary>
/// 레벨 배지 모양 하나(<see cref="LevelBadgeArt"/>): 바탕·테두리·글자 색, 지나가는 빛, 둘레 빛, 반짝이, 불티.
/// 장착한 배지 보상이 없으면 레벨 단계 모양(<see cref="ForTier"/>, ID <c>tier.*</c>)을 쓴다. 모든 주기는 <see cref="RewardArt.Loop"/>의 약수.
/// </summary>
public sealed record BadgeSkin(string Id)
{
    public BadgeFill Fill { get; init; }
    public Color32 Outline { get; init; }
    public Color32 Top { get; init; }
    public Color32 Mid { get; init; }
    public Color32 Bottom { get; init; }
    public Color32 Text { get; init; } = Color32.White;
    public Color32 Shadow { get; init; }
    /// <summary>지나가는 빛 주기(프레임, 0 = 없음 — 배지가 길면 더 긴 약수로 늘림)와 굵기.</summary>
    public int ShinePeriod { get; init; }
    public int ShineWidth { get; init; }
    public BadgeGlow Glow { get; init; }
    public Color32 GlowColor { get; init; }
    public int GlowRings { get; init; } = 1;
    public int Sparkles { get; init; }
    public SparkleShape SparkleShape { get; init; }
    public Color32 Sparkle { get; init; } = Color32.White;
    public bool RainbowSparkles { get; init; }
    public int Embers { get; init; }
    public Color32[] EmberColors { get; init; } = [];

    /// <summary>멈춘 그림으로 그린다(흐르는 바탕도 첫 프레임 그대로) — 레벨 단계 기본 모양.</summary>
    public bool Still { get; init; }

    public bool Animated => !Still && (ShinePeriod > 0 || Glow != BadgeGlow.None || Sparkles > 0 || Embers > 0 || Fill is not (BadgeFill.Bands or BadgeFill.Seeds or BadgeFill.Facets));

    // ───────────── 레벨 단계(기본) ─────────────
    // 기본 모양은 단계 색만 보이는 멈춘 그림이다(회색·동·은·금·청록–보라·무지개 띠). 빛·반짝이·흐르는 색·불티 같은 움직임은 배지 보상에만 있다.

    public static readonly BadgeSkin Plain = new("tier.plain")
    {
        Outline = Hex(0x55555F), Top = Hex(0x4A4A54), Mid = Hex(0x3C3C44), Bottom = Hex(0x33333A), Text = Hex(0xC8C8D0), Shadow = Hex(0x26262C), Still = true,
    };
    public static readonly BadgeSkin Bronze = new("tier.bronze")
    {
        Outline = Hex(0x5A3416), Top = Hex(0xE0A066), Mid = Hex(0xB87333), Bottom = Hex(0x8E5527), Text = Hex(0xFFF1E0), Shadow = Hex(0x5A3416), Still = true,
    };
    public static readonly BadgeSkin Silver = new("tier.silver")
    {
        Outline = Hex(0x4A525C), Top = Hex(0xF4F7FA), Mid = Hex(0xC4CCD6), Bottom = Hex(0x9AA4B0), Text = Hex(0x1F242B), Shadow = Hex(0xEEF2F6), Still = true,
    };
    public static readonly BadgeSkin Gold = new("tier.gold")
    {
        Outline = Hex(0x6B4A00), Top = Hex(0xFFF0A8), Mid = Hex(0xFFD54A), Bottom = Hex(0xD9A21B), Text = Hex(0x3A2600), Shadow = Hex(0xFFF0B0), Still = true,
    };
    public static readonly BadgeSkin Crystal = new("tier.crystal")
    {
        Fill = BadgeFill.Flow, Outline = Hex(0x2A1E6B), Top = Hex(0x0891B2), Bottom = Hex(0x6D28D9), Shadow = Hex(0x2A1E6B), Still = true,
    };
    public static readonly BadgeSkin Legend = new("tier.legend")
    {
        Fill = BadgeFill.Rainbow, Outline = Hex(0x3A1450), Shadow = Hex(0x2A0A3A), Still = true,
    };

    // ───────────── 배지 보상 ─────────────

    private static readonly BadgeSkin[] Rewards =
    [
        new("badge.mint")
        {
            Outline = Hex(0x1F5C4A), Top = Hex(0xB8F7DD), Mid = Hex(0x5FD8A8), Bottom = Hex(0x34A87E), Text = Hex(0x0F3D30), Shadow = Hex(0xC8FAE6),
            ShinePeriod = 60, ShineWidth = 1,
        },
        new("badge.strawberry")
        {
            Fill = BadgeFill.Seeds, Outline = Hex(0x6B1028), Top = Hex(0xFF8FA8), Mid = Hex(0xF0506E), Bottom = Hex(0xC02848), Shadow = Hex(0x6B1028),
            ShinePeriod = 40, ShineWidth = 2,
        },
        new("badge.heart")
        {
            Outline = Hex(0x7A1F4A), Top = Hex(0xFFB3D1), Mid = Hex(0xFF6FA8), Bottom = Hex(0xE04585), Shadow = Hex(0x7A1F4A),
            ShinePeriod = 30, ShineWidth = 2, Sparkles = 3, SparkleShape = SparkleShape.Heart, Sparkle = Hex(0xFF4F8B),
        },
        new("badge.ocean")
        {
            Fill = BadgeFill.Waves, Outline = Hex(0x0B2A5B), Top = Hex(0x7DD3FC), Mid = Hex(0x2B8BD8), Bottom = Hex(0x1A4FA0), Shadow = Hex(0x0B2A5B),
            Sparkles = 2, Sparkle = Hex(0xBFF6FF),
        },
        new("badge.forest")
        {
            Outline = Hex(0x14532D), Top = Hex(0x86EFAC), Mid = Hex(0x2FAF5F), Bottom = Hex(0x1E7A42), Text = Hex(0xF7FFE8), Shadow = Hex(0x14532D),
            ShinePeriod = 30, ShineWidth = 2, Sparkles = 3, SparkleShape = SparkleShape.Leaf, Sparkle = Hex(0xA3E635),
        },
        new("badge.neon")
        {
            Fill = BadgeFill.Neon, Outline = Hex(0xFF3EA5), Top = Hex(0x1A1030), Mid = Hex(0x120A24), Bottom = Hex(0x0C0618), Shadow = Hex(0x5B1E8C),
            Glow = BadgeGlow.Neon,
        },
        new("badge.lava")
        {
            Fill = BadgeFill.Lava, Outline = Hex(0x3B0A00), Shadow = Hex(0x3B0A00),
            Glow = BadgeGlow.Solid, GlowColor = Hex(0xFF6A00), Embers = 3, EmberColors = [Hex(0xFFD54A), Hex(0xFF8A1F), Hex(0xFF4A1A)],
        },
        new("badge.galaxy")
        {
            Fill = BadgeFill.Galaxy, Outline = Hex(0x1B0B3A), Top = Hex(0x2A1060), Bottom = Hex(0x3E2A9A), Shadow = Hex(0x12062A),
            Glow = BadgeGlow.Solid, GlowColor = Hex(0x7C5CFF), Sparkles = 2,
        },
        new("badge.ice")
        {
            Fill = BadgeFill.Facets, Outline = Hex(0x1E4E79), Top = Hex(0xF0FBFF), Mid = Hex(0xBDE8FA), Bottom = Hex(0x8CCDEB), Text = Hex(0x0B2545), Shadow = Hex(0xF0FBFF),
            ShinePeriod = 24, ShineWidth = 2, Glow = BadgeGlow.Solid, GlowColor = Hex(0x7FDBFF), Sparkles = 3, SparkleShape = SparkleShape.Snow, Sparkle = Hex(0xE0F7FF),
        },
        new("badge.legend-gold")
        {
            Outline = Hex(0x6B4A00), Top = Hex(0xFFF5C0), Mid = Hex(0xFFD23F), Bottom = Hex(0xE09A10), Text = Hex(0x3A2600), Shadow = Hex(0xFFF0B0),
            ShinePeriod = 24, ShineWidth = 3, Glow = BadgeGlow.Rainbow, GlowRings = 2, Sparkles = 6, RainbowSparkles = true,
            Embers = 3, EmberColors = [Hex(0xFFE58A), Hex(0xFFD23F), Hex(0xFFFFFF)],
        },

        // ───────────── 51~100: 높을수록 빛·반짝이·불티가 많아짐
        new("badge.sakura")
        {
            Outline = Hex(0x7A2048), Top = Hex(0xFFE4EF), Mid = Hex(0xF9A8C9), Bottom = Hex(0xE36B9C), Text = Hex(0x5A1730), Shadow = Hex(0xFFF0F6),
            ShinePeriod = 40, ShineWidth = 2, Sparkles = 4, SparkleShape = SparkleShape.Leaf, Sparkle = Hex(0xF9A8D4),
        },
        new("badge.sunset")
        {
            Fill = BadgeFill.Flow, Outline = Hex(0x4A1530), Top = Hex(0xF97316), Bottom = Hex(0xA21CAF), Shadow = Hex(0x3B0A24),
            ShinePeriod = 30, ShineWidth = 2, Glow = BadgeGlow.Solid, GlowColor = Hex(0xFB7185), Sparkles = 2, Sparkle = Hex(0xFFE4B5),
        },
        new("badge.emerald")
        {
            Fill = BadgeFill.Facets, Outline = Hex(0x064E3B), Top = Hex(0x6EE7B7), Mid = Hex(0x10B981), Bottom = Hex(0x047857), Shadow = Hex(0x022C22),
            ShinePeriod = 24, ShineWidth = 2, Glow = BadgeGlow.Solid, GlowColor = Hex(0x34D399), Sparkles = 3, Sparkle = Hex(0xD1FAE5),
        },
        new("badge.thunder")
        {
            Fill = BadgeFill.Neon, Outline = Hex(0xFDE047), Top = Hex(0x1E1B4B), Mid = Hex(0x111033), Bottom = Hex(0x0A0920), Text = Hex(0xFEF9C3), Shadow = Hex(0x3730A3),
            Glow = BadgeGlow.Neon, Sparkles = 3, Sparkle = Hex(0xFFFFFF), Embers = 2, EmberColors = [Hex(0xFDE047), Hex(0xBFDBFE)],
        },
        new("badge.ruby")
        {
            Fill = BadgeFill.Facets, Outline = Hex(0x4C0519), Top = Hex(0xFDA4AF), Mid = Hex(0xE11D48), Bottom = Hex(0x9F1239), Shadow = Hex(0x3B0A14),
            ShinePeriod = 24, ShineWidth = 3, Glow = BadgeGlow.Solid, GlowColor = Hex(0xF43F5E), GlowRings = 2, Sparkles = 4, Sparkle = Hex(0xFFE4E6),
        },
        new("badge.aurora")
        {
            Fill = BadgeFill.Flow, Outline = Hex(0x0B2A3A), Top = Hex(0x10B981), Bottom = Hex(0x7C3AED), Shadow = Hex(0x06202C),
            ShinePeriod = 30, ShineWidth = 2, Glow = BadgeGlow.Rainbow, Sparkles = 4, Sparkle = Hex(0xA7F3D0),
        },
        new("badge.starlight")
        {
            Fill = BadgeFill.Galaxy, Outline = Hex(0x0B1026), Top = Hex(0x1E2A6B), Bottom = Hex(0x0E1440), Text = Hex(0xFFF7CC), Shadow = Hex(0x050818),
            Glow = BadgeGlow.Solid, GlowColor = Hex(0xFDE68A), GlowRings = 2, Sparkles = 5, Sparkle = Hex(0xFFF3B0),
        },
        new("badge.obsidian")
        {
            Fill = BadgeFill.Facets, Outline = Hex(0x000000), Top = Hex(0x3F3A52), Mid = Hex(0x1C1A26), Bottom = Hex(0x0B0A10), Text = Hex(0xE9D5FF), Shadow = Hex(0x000000),
            ShinePeriod = 24, ShineWidth = 2, Glow = BadgeGlow.Solid, GlowColor = Hex(0xA855F7), GlowRings = 2, Sparkles = 3, Sparkle = Hex(0xD8B4FE),
            Embers = 3, EmberColors = [Hex(0xC084FC), Hex(0x7C3AED)],
        },
        new("badge.prism")
        {
            Fill = BadgeFill.Rainbow, Outline = Hex(0x1A0A2A), Shadow = Hex(0x14061F),
            ShinePeriod = 24, ShineWidth = 3, Glow = BadgeGlow.Rainbow, GlowRings = 2, Sparkles = 6, RainbowSparkles = true,
        },
        new("badge.celestial")
        {
            Fill = BadgeFill.Flow, Outline = Hex(0x7A5A00), Top = Hex(0xFFFBEB), Bottom = Hex(0xFDE68A), Text = Hex(0x5A3A00), Shadow = Hex(0xFFFFFF),
            ShinePeriod = 24, ShineWidth = 3, Glow = BadgeGlow.Rainbow, GlowRings = 2, Sparkles = 8, RainbowSparkles = true,
            Embers = 4, EmberColors = [Hex(0xFFF3B0), Hex(0xFFFFFF), Hex(0xFDE68A)],
        },
    ];

    private static readonly Dictionary<string, BadgeSkin> ById =
        new[] { Plain, Bronze, Silver, Gold, Crystal, Legend }.Concat(Rewards).ToDictionary(s => s.Id, StringComparer.Ordinal);

    /// <summary>레벨 단계 모양 여섯(<c>tier.*</c>)과 배지 보상 모양.</summary>
    public static IEnumerable<BadgeSkin> All => ById.Values;

    /// <summary>ID의 모양(없으면 null).</summary>
    public static BadgeSkin? Find(string? id) => id is not null && ById.TryGetValue(id, out var skin) ? skin : null;

    /// <summary>레벨 단계의 기본 모양.</summary>
    public static BadgeSkin ForTier(int level) => CommunityLevel.Tier(level) switch
    {
        CommunityLevelTier.Legend => Legend,
        CommunityLevelTier.Crystal => Crystal,
        CommunityLevelTier.Gold => Gold,
        CommunityLevelTier.Silver => Silver,
        CommunityLevelTier.Bronze => Bronze,
        _ => Plain,
    };
}
