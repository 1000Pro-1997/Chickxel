using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Community;

/// <summary>레벨 보상이 들어가는 칸. 칸마다 하나씩 따로 장착한다(<see cref="ProfileRewards"/>).</summary>
public enum RewardSlot
{
    /// <summary>칭호: 이름 아래(또는 옆)의 글.</summary>
    Title,
    /// <summary>레벨 배지 스킨: 이름 옆 <c>Lv N</c> 배지 모양(<see cref="LevelBadgeArt"/>).</summary>
    Badge,
    /// <summary>작은 테두리: 목록·작성자·이름 줄의 작은 프로필 그림 둘레(<see cref="AvatarFrameArt"/>).</summary>
    FrameSmall,
    /// <summary>큰 테두리: 프로필 쪽·계정 창의 큰 프로필 그림 둘레.</summary>
    FrameLarge,
    /// <summary>효과: 큰 프로필 그림 둘레에서 움직이는 것(<see cref="AvatarEffectArt"/>).</summary>
    Effect,
    /// <summary>카드: 프로필 머리 뒤 바탕(<see cref="ProfileCardArt"/>).</summary>
    Card,
}

/// <summary>레벨 <see cref="Level"/>에 오르면 받는 보상 하나(<see cref="LevelRewards"/>).</summary>
public sealed record LevelReward(string Id, int Level, RewardSlot Slot)
{
    /// <summary>화면 언어의 이름(CSV로 더한 언어 포함).</summary>
    public string Name => CoreStrings.GetResourceString(LevelRewards.NameKey(Id)) ?? Id;

    /// <summary>앱에 든 언어 <paramref name="culture"/>의 이름(홈페이지용 목록을 내보낼 때).</summary>
    public string NameIn(CultureInfo culture) => CoreStrings.ResourceManager.GetString(LevelRewards.NameKey(Id), culture) ?? Id;
}

/// <summary>
/// 레벨 보상 목록(앱·홈페이지·서버가 같은 것을 씀 — <c>tools/RewardExport</c>가 홈페이지용 <c>catalog.json</c>으로 내보냄).
/// 1레벨은 보상이 없고 2~50레벨에 하나씩(49개). 열 레벨마다 같은 차례로 돌아 칸이 고르게 늘어난다:
/// 끝자리 2·7 배지, 3·8 작은 테두리, 4·9 효과, 5 큰 테두리, 6·1 칭호, 0 카드. 해금은 레벨만으로 정해진다(레벨이 내려가면 다시 잠김).
/// </summary>
public static class LevelRewards
{
    public const int MaxLevel = 50;

    public static IReadOnlyList<LevelReward> All { get; } =
    [
        new("badge.mint", 2, RewardSlot.Badge),
        new("frame-s.wood", 3, RewardSlot.FrameSmall),
        new("effect.twinkle", 4, RewardSlot.Effect),
        new("frame-l.bronze", 5, RewardSlot.FrameLarge),
        new("title.cluck", 6, RewardSlot.Title),
        new("badge.strawberry", 7, RewardSlot.Badge),
        new("frame-s.bronze", 8, RewardSlot.FrameSmall),
        new("effect.feathers", 9, RewardSlot.Effect),
        new("card.silver", 10, RewardSlot.Card),
        new("title.dot-artisan", 11, RewardSlot.Title),
        new("badge.heart", 12, RewardSlot.Badge),
        new("frame-s.silver", 13, RewardSlot.FrameSmall),
        new("effect.snow", 14, RewardSlot.Effect),
        new("frame-l.silver-wings", 15, RewardSlot.FrameLarge),
        new("title.palette-collector", 16, RewardSlot.Title),
        new("badge.ocean", 17, RewardSlot.Badge),
        new("frame-s.marching", 18, RewardSlot.FrameSmall),
        new("effect.notes", 19, RewardSlot.Effect),
        new("card.gold", 20, RewardSlot.Card),
        new("title.rooster", 21, RewardSlot.Title),
        new("badge.forest", 22, RewardSlot.Badge),
        new("frame-s.gold", 23, RewardSlot.FrameSmall),
        new("effect.hearts", 24, RewardSlot.Effect),
        new("frame-l.crown", 25, RewardSlot.FrameLarge),
        new("title.animator", 26, RewardSlot.Title),
        new("badge.neon", 27, RewardSlot.Badge),
        new("frame-s.neon", 28, RewardSlot.FrameSmall),
        new("effect.bubbles", 29, RewardSlot.Effect),
        new("card.crystal", 30, RewardSlot.Card),
        new("title.tile-architect", 31, RewardSlot.Title),
        new("badge.lava", 32, RewardSlot.Badge),
        new("frame-s.crystal", 33, RewardSlot.FrameSmall),
        new("effect.shooting-stars", 34, RewardSlot.Effect),
        new("frame-l.crystal-shards", 35, RewardSlot.FrameLarge),
        new("title.pixel-wizard", 36, RewardSlot.Title),
        new("badge.galaxy", 37, RewardSlot.Badge),
        new("frame-s.vine", 38, RewardSlot.FrameSmall),
        new("effect.gold-dust", 39, RewardSlot.Effect),
        new("card.night-sky", 40, RewardSlot.Card),
        new("title.legendary-dotter", 41, RewardSlot.Title),
        new("badge.ice", 42, RewardSlot.Badge),
        new("frame-s.flame", 43, RewardSlot.FrameSmall),
        new("effect.sparks", 44, RewardSlot.Effect),
        new("frame-l.phoenix", 45, RewardSlot.FrameLarge),
        new("title.phoenix", 46, RewardSlot.Title),
        new("badge.legend-gold", 47, RewardSlot.Badge),
        new("frame-s.rainbow", 48, RewardSlot.FrameSmall),
        new("effect.aura", 49, RewardSlot.Effect),
        new("card.phoenix", 50, RewardSlot.Card),
    ];

    private static readonly Dictionary<string, LevelReward> ById = All.ToDictionary(r => r.Id, StringComparer.Ordinal);

    /// <summary>ID의 보상(없으면 null).</summary>
    public static LevelReward? Find(string? id) => id is not null && ById.TryGetValue(id, out var reward) ? reward : null;

    /// <summary>레벨 <paramref name="level"/>에 오를 때 받는 보상(1레벨·51레벨 이상은 null).</summary>
    public static LevelReward? At(int level) => level is >= 2 and <= MaxLevel ? All[level - 2] : null;

    /// <summary>레벨 <paramref name="level"/>까지 받은 보상(레벨 차례).</summary>
    public static IEnumerable<LevelReward> UnlockedAt(int level) => All.TakeWhile(r => r.Level <= level);

    /// <summary><paramref name="fromLevel"/>보다 높고 <paramref name="toLevel"/> 이하인 레벨에서 받은 보상(레벨이 오르며 새로 생긴 것).</summary>
    public static IEnumerable<LevelReward> Between(int fromLevel, int toLevel) => All.Where(r => r.Level > fromLevel && r.Level <= toLevel);

    /// <summary><paramref name="level"/> 다음에 받을 보상(다음 레벨부터 — 다 받았으면 null).</summary>
    public static LevelReward? Next(int level) => All.FirstOrDefault(r => r.Level > level);

    /// <summary>칸의 화면 언어 이름.</summary>
    public static string SlotName(RewardSlot slot) => CoreStrings.GetResourceString("RewardSlot_" + slot) ?? slot.ToString();

    /// <summary>앱에 든 언어 <paramref name="culture"/>의 칸 이름(홈페이지용 목록을 내보낼 때).</summary>
    public static string SlotNameIn(RewardSlot slot, CultureInfo culture) => CoreStrings.ResourceManager.GetString("RewardSlot_" + slot, culture) ?? slot.ToString();

    /// <summary>
    /// 칭호 글자 색: 받은 레벨의 배지 단계 색(동·은·금·크리스털 — 6~29는 단계 색, 30~39 청록). 40 이상 칭호는 무지개로 흐른다(<see cref="IsRainbowTitle"/> — 이 색은 멈춘 화면용).
    /// </summary>
    public static Model.Color32 TitleColor(LevelReward reward) => CommunityLevel.Tier(reward.Level) switch
    {
        CommunityLevelTier.Legend or CommunityLevelTier.Crystal when reward.Level >= 40 => new Model.Color32(0xF9, 0xA8, 0xD4),
        CommunityLevelTier.Legend or CommunityLevelTier.Crystal => new Model.Color32(0x67, 0xE8, 0xF9),
        CommunityLevelTier.Gold => new Model.Color32(0xFF, 0xD5, 0x4A),
        CommunityLevelTier.Silver => new Model.Color32(0xC9, 0xD3, 0xDD),
        _ => new Model.Color32(0xE0, 0xA0, 0x66),
    };

    /// <summary>무지개로 흐르는 칭호(40레벨 이상에서 받은 것).</summary>
    public static bool IsRainbowTitle(LevelReward reward) => reward.Slot == RewardSlot.Title && reward.Level >= 40;

    /// <summary>칸의 JSON 이름(<c>title</c>, <c>badge</c>, <c>frameSmall</c>, <c>frameLarge</c>, <c>effect</c>, <c>card</c>).</summary>
    public static string SlotKey(RewardSlot slot) => slot switch
    {
        RewardSlot.Title => "title",
        RewardSlot.Badge => "badge",
        RewardSlot.FrameSmall => "frameSmall",
        RewardSlot.FrameLarge => "frameLarge",
        RewardSlot.Effect => "effect",
        _ => "card",
    };

    internal static string NameKey(string id) => "Reward_" + id.Replace('.', '_').Replace('-', '_');
}

/// <summary>
/// 계정이 장착한 보상(칸마다 보상 ID, null = 기본 — 칭호 없음·배지는 레벨 단계 모양·테두리·효과·카드 없음). 사용자 JSON의 <c>rewards</c> 칸.
/// 레벨보다 높은 보상이나 칸이 맞지 않는 ID는 장착하지 않은 것으로 본다(서버도 그렇게 거름).
/// </summary>
public sealed record ProfileRewards(string? Title = null, string? Badge = null, string? FrameSmall = null, string? FrameLarge = null, string? Effect = null, string? Card = null)
{
    public static ProfileRewards None { get; } = new();

    public bool IsEmpty => Title is null && Badge is null && FrameSmall is null && FrameLarge is null && Effect is null && Card is null;

    /// <summary>칸의 장착 ID.</summary>
    public string? this[RewardSlot slot] => slot switch
    {
        RewardSlot.Title => Title,
        RewardSlot.Badge => Badge,
        RewardSlot.FrameSmall => FrameSmall,
        RewardSlot.FrameLarge => FrameLarge,
        RewardSlot.Effect => Effect,
        _ => Card,
    };

    /// <summary>칸에 장착한 보상(레벨 <paramref name="level"/>로 받을 수 있는 것만, 아니면 null).</summary>
    public LevelReward? Equipped(RewardSlot slot, int level)
        => LevelRewards.Find(this[slot]) is { } reward && reward.Slot == slot && reward.Level <= level ? reward : null;

    internal static ProfileRewards Read(JsonNode? node)
    {
        if (node is not JsonObject o) return None;
        string? Id(RewardSlot slot) => CommunityJson.String(o[LevelRewards.SlotKey(slot)]);
        return new ProfileRewards(Id(RewardSlot.Title), Id(RewardSlot.Badge), Id(RewardSlot.FrameSmall), Id(RewardSlot.FrameLarge), Id(RewardSlot.Effect), Id(RewardSlot.Card));
    }

    internal JsonObject ToJson()
    {
        var o = new JsonObject();
        foreach (var slot in Enum.GetValues<RewardSlot>())
            if (this[slot] is { } id) o[LevelRewards.SlotKey(slot)] = id;
        return o;
    }
}
