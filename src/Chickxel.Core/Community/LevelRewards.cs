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
    /// <summary>테두리: 프로필 그림 둘레의 픽셀 테두리 세트 — 작은 프로필 그림(목록·이름 줄)에는 작은 모양, 큰 프로필 그림(프로필 머리·계정 창)에는 같은 디자인의 큰 모양(<see cref="AvatarFrameArt"/>).</summary>
    Frame,
    /// <summary>효과: 큰 프로필 그림 둘레에서 움직이는 것(<see cref="AvatarEffectArt"/>).</summary>
    Effect,
    /// <summary>카드: 프로필 머리 뒤 바탕(<see cref="ProfileCardArt"/>).</summary>
    Card,
    /// <summary>이름 효과: 닉네임 글자의 색과 움직임(물결·글리치·순간이동·환각 등, <see cref="NameStyle"/>).</summary>
    NameStyle,
}

/// <summary>
/// 레벨 <see cref="Level"/>에 오르면 받는 보상 하나(<see cref="LevelRewards"/>). <see cref="Tutorial"/>이면 레벨과 상관없이 레벨 튜토리얼을 마치면 받는 보상
/// (<see cref="LevelRewards.Tutorial"/> — <see cref="Level"/>은 1로 두어 "자동"에서 가장 낮은 보상으로 침).
/// </summary>
public sealed record LevelReward(string Id, int Level, RewardSlot Slot, bool Tutorial = false)
{
    /// <summary>화면 언어의 이름(CSV로 더한 언어 포함).</summary>
    public string Name => CoreStrings.GetResourceString(LevelRewards.NameKey(Id)) ?? Id;

    /// <summary>앱에 든 언어 <paramref name="culture"/>의 이름(홈페이지용 목록을 내보낼 때).</summary>
    public string NameIn(CultureInfo culture) => CoreStrings.ResourceManager.GetString(LevelRewards.NameKey(Id), culture) ?? Id;
}

/// <summary>
/// 레벨 보상 목록(앱·홈페이지·서버가 같은 것을 씀 — <c>tools/RewardExport</c>가 홈페이지용 <c>catalog.json</c>으로 내보냄).
/// 1레벨은 보상이 없고 2~100레벨에 하나씩(99개). 열 레벨마다 같은 차례로 돌아 칸이 고르게 늘어난다:
/// 끝자리 1 칭호, 2·7 배지, 3·5·8 테두리, 4·9 효과, 6 이름 효과, 0 카드. 해금은 레벨만으로 정해진다(레벨이 내려가면 다시 잠김).
/// 보상이 더 생기면 101레벨부터 이어 붙인다.
/// </summary>
public static class LevelRewards
{
    public const int MaxLevel = 100;

    public static IReadOnlyList<LevelReward> All { get; } =
    [
        new("badge.mint", 2, RewardSlot.Badge),
        new("frame.wood", 3, RewardSlot.Frame),
        new("effect.twinkle", 4, RewardSlot.Effect),
        new("frame.medal", 5, RewardSlot.Frame),
        new("name.sunrise", 6, RewardSlot.NameStyle),
        new("badge.strawberry", 7, RewardSlot.Badge),
        new("frame.bronze", 8, RewardSlot.Frame),
        new("effect.feathers", 9, RewardSlot.Effect),
        new("card.silver", 10, RewardSlot.Card),
        new("title.cluck", 11, RewardSlot.Title),
        new("badge.heart", 12, RewardSlot.Badge),
        new("frame.silver", 13, RewardSlot.Frame),
        new("effect.snow", 14, RewardSlot.Effect),
        new("frame.silver-wings", 15, RewardSlot.Frame),
        new("name.ocean", 16, RewardSlot.NameStyle),
        new("badge.ocean", 17, RewardSlot.Badge),
        new("frame.marching", 18, RewardSlot.Frame),
        new("effect.notes", 19, RewardSlot.Effect),
        new("card.gold", 20, RewardSlot.Card),
        new("title.dot-artisan", 21, RewardSlot.Title),
        new("badge.forest", 22, RewardSlot.Badge),
        new("frame.gold", 23, RewardSlot.Frame),
        new("effect.hearts", 24, RewardSlot.Effect),
        new("frame.crown", 25, RewardSlot.Frame),
        new("name.neon", 26, RewardSlot.NameStyle),
        new("badge.neon", 27, RewardSlot.Badge),
        new("frame.neon", 28, RewardSlot.Frame),
        new("effect.bubbles", 29, RewardSlot.Effect),
        new("card.crystal", 30, RewardSlot.Card),
        new("title.palette-collector", 31, RewardSlot.Title),
        new("badge.lava", 32, RewardSlot.Badge),
        new("frame.crystal", 33, RewardSlot.Frame),
        new("effect.shooting-stars", 34, RewardSlot.Effect),
        new("frame.crystal-shards", 35, RewardSlot.Frame),
        new("name.wave", 36, RewardSlot.NameStyle),
        new("badge.galaxy", 37, RewardSlot.Badge),
        new("frame.vine", 38, RewardSlot.Frame),
        new("effect.gold-dust", 39, RewardSlot.Effect),
        new("card.night-sky", 40, RewardSlot.Card),
        new("title.rooster", 41, RewardSlot.Title),
        new("badge.ice", 42, RewardSlot.Badge),
        new("frame.flame", 43, RewardSlot.Frame),
        new("effect.sparks", 44, RewardSlot.Effect),
        new("frame.phoenix", 45, RewardSlot.Frame),
        new("name.glitch", 46, RewardSlot.NameStyle),
        new("badge.legend-gold", 47, RewardSlot.Badge),
        new("frame.rainbow", 48, RewardSlot.Frame),
        new("effect.aura", 49, RewardSlot.Effect),
        new("card.phoenix", 50, RewardSlot.Card),
        new("title.animator", 51, RewardSlot.Title),
        new("badge.sakura", 52, RewardSlot.Badge),
        new("frame.sakura", 53, RewardSlot.Frame),
        new("effect.petals", 54, RewardSlot.Effect),
        new("frame.frost", 55, RewardSlot.Frame),
        new("name.teleport", 56, RewardSlot.NameStyle),
        new("badge.sunset", 57, RewardSlot.Badge),
        new("frame.candy", 58, RewardSlot.Frame),
        new("effect.fireflies", 59, RewardSlot.Effect),
        new("card.sakura", 60, RewardSlot.Card),
        new("title.tile-architect", 61, RewardSlot.Title),
        new("badge.emerald", 62, RewardSlot.Badge),
        new("frame.emerald", 63, RewardSlot.Frame),
        new("effect.leaves", 64, RewardSlot.Effect),
        new("frame.dragon", 65, RewardSlot.Frame),
        new("name.hallucination", 66, RewardSlot.NameStyle),
        new("badge.thunder", 67, RewardSlot.Badge),
        new("frame.electric", 68, RewardSlot.Frame),
        new("effect.confetti", 69, RewardSlot.Effect),
        new("card.deep-sea", 70, RewardSlot.Card),
        new("title.pixel-wizard", 71, RewardSlot.Title),
        new("badge.ruby", 72, RewardSlot.Badge),
        new("frame.ruby", 73, RewardSlot.Frame),
        new("effect.planets", 74, RewardSlot.Effect),
        new("frame.halo", 75, RewardSlot.Frame),
        new("name.fire", 76, RewardSlot.NameStyle),
        new("badge.aurora", 77, RewardSlot.Badge),
        new("frame.ocean", 78, RewardSlot.Frame),
        new("effect.runes", 79, RewardSlot.Effect),
        new("card.aurora", 80, RewardSlot.Card),
        new("title.legendary-dotter", 81, RewardSlot.Title),
        new("badge.starlight", 82, RewardSlot.Badge),
        new("frame.lava", 83, RewardSlot.Frame),
        new("effect.flames", 84, RewardSlot.Effect),
        new("frame.night-wings", 85, RewardSlot.Frame),
        new("name.galaxy", 86, RewardSlot.NameStyle),
        new("badge.obsidian", 87, RewardSlot.Badge),
        new("frame.galaxy", 88, RewardSlot.Frame),
        new("effect.galaxy-swirl", 89, RewardSlot.Effect),
        new("card.cosmos", 90, RewardSlot.Card),
        new("title.phoenix", 91, RewardSlot.Title),
        new("badge.prism", 92, RewardSlot.Badge),
        new("frame.prism", 93, RewardSlot.Frame),
        new("effect.meteors", 94, RewardSlot.Effect),
        new("frame.royal", 95, RewardSlot.Frame),
        new("name.legend", 96, RewardSlot.NameStyle),
        new("badge.celestial", 97, RewardSlot.Badge),
        new("frame.celestial", 98, RewardSlot.Frame),
        new("effect.phoenix-aura", 99, RewardSlot.Effect),
        new("card.legend", 100, RewardSlot.Card),
    ];

    /// <summary>
    /// 레벨 튜토리얼을 마치면 받는 새싹 보상(레벨 해금과 따로 — 서버가 계정에 받았음을 적어 두고 <c>tutorialRewards</c>로 알려 줌).
    /// 카탈로그에는 <c>"unlock": "tutorial"</c>로 나간다.
    /// </summary>
    public static IReadOnlyList<LevelReward> Tutorial { get; } =
    [
        new("title.sprout", 1, RewardSlot.Title, Tutorial: true),
        new("frame.sprout", 1, RewardSlot.Frame, Tutorial: true),
    ];

    private static readonly Dictionary<string, LevelReward> ById = All.Concat(Tutorial).ToDictionary(r => r.Id, StringComparer.Ordinal);

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
    /// 칭호 글자 색: 받은 레벨이 높을수록 귀한 색(11 동·21 은·31 금·41 청록·51~61 분홍, 71 이상은 무지개로 흐름 — <see cref="IsRainbowTitle"/>, 이 색은 멈춘 화면용). 튜토리얼 새싹 칭호는 연두.
    /// </summary>
    public static Model.Color32 TitleColor(LevelReward reward) => reward.Tutorial ? new Model.Color32(0xA3, 0xE6, 0x35) : reward.Level switch
    {
        >= 51 => new Model.Color32(0xF9, 0xA8, 0xD4),
        >= 41 => new Model.Color32(0x67, 0xE8, 0xF9),
        >= 31 => new Model.Color32(0xFF, 0xD5, 0x4A),
        >= 21 => new Model.Color32(0xC9, 0xD3, 0xDD),
        _ => new Model.Color32(0xE0, 0xA0, 0x66),
    };

    /// <summary>무지개로 흐르는 칭호(71레벨 이상에서 받은 것).</summary>
    public static bool IsRainbowTitle(LevelReward reward) => reward.Slot == RewardSlot.Title && reward.Level >= 71;

    /// <summary>칸에서 "없음"을 고른 값(장착 선택에 저장 — 고르지 않은 칸은 자동).</summary>
    public const string NoneChoice = "none";

    /// <summary>"자동"이 낄 보상: 레벨 <paramref name="level"/>까지 그 칸에서 받은 것 중 가장 높은 레벨(없으면 null).</summary>
    public static LevelReward? Highest(RewardSlot slot, int level) => All.LastOrDefault(r => r.Slot == slot && r.Level <= level);

    /// <summary>
    /// 고른 값(<paramref name="choice"/> — null = 자동, <see cref="NoneChoice"/> = 없음, 아니면 보상 ID)을 레벨 <paramref name="level"/>에서 실제로 낄 보상으로 푼다.
    /// 아직 못 받은(레벨이 내려간) 보상이나 칸이 다른 ID는 자동으로 본다.
    /// </summary>
    public static LevelReward? Resolve(RewardSlot slot, string? choice, int level)
    {
        if (choice == NoneChoice) return null;
        return Find(choice) is { } chosen && chosen.Slot == slot && chosen.Level <= level ? chosen : Highest(slot, level);
    }

    /// <summary>칸의 JSON 이름(<c>title</c>, <c>badge</c>, <c>frame</c>, <c>effect</c>, <c>card</c>, <c>nameStyle</c>).</summary>
    public static string SlotKey(RewardSlot slot) => slot switch
    {
        RewardSlot.Title => "title",
        RewardSlot.Badge => "badge",
        RewardSlot.Frame => "frame",
        RewardSlot.Effect => "effect",
        RewardSlot.Card => "card",
        _ => "nameStyle",
    };

    internal static string NameKey(string id) => "Reward_" + id.Replace('.', '_').Replace('-', '_');
}

/// <summary>
/// 계정이 실제로 낀 보상(칸마다 보상 ID, null = 없음). 사용자 JSON의 <c>rewards</c> 칸으로, 서버가 고른 값을 풀어서 보낸다:
/// 고르지 않은 칸은 "자동"(그 칸에서 받은 것 중 가장 높은 레벨 — <see cref="LevelRewards.Highest"/>), "없음"(<see cref="LevelRewards.NoneChoice"/>)을 고른 칸은 빠진다.
/// 레벨보다 높은 보상이나 칸이 맞지 않는 ID는 끼지 않은 것으로 본다(서버도 그렇게 거름).
/// </summary>
public sealed record ProfileRewards(string? Title = null, string? Badge = null, string? Frame = null, string? Effect = null, string? Card = null, string? NameStyle = null)
{
    public static ProfileRewards None { get; } = new();

    public bool IsEmpty => Title is null && Badge is null && Frame is null && Effect is null && Card is null && NameStyle is null;

    /// <summary>칸의 ID.</summary>
    public string? this[RewardSlot slot] => slot switch
    {
        RewardSlot.Title => Title,
        RewardSlot.Badge => Badge,
        RewardSlot.Frame => Frame,
        RewardSlot.Effect => Effect,
        RewardSlot.Card => Card,
        _ => NameStyle,
    };

    /// <summary>칸에 낀 보상(레벨 <paramref name="level"/>로 받을 수 있는 것만, 아니면 null).</summary>
    public LevelReward? Equipped(RewardSlot slot, int level)
        => LevelRewards.Find(this[slot]) is { } reward && reward.Slot == slot && reward.Level <= level ? reward : null;

    internal static ProfileRewards Read(JsonNode? node)
    {
        if (node is not JsonObject o) return None;
        string? Id(RewardSlot slot) => CommunityJson.String(o[LevelRewards.SlotKey(slot)]);
        return new ProfileRewards(Id(RewardSlot.Title), Id(RewardSlot.Badge), Id(RewardSlot.Frame), Id(RewardSlot.Effect), Id(RewardSlot.Card), Id(RewardSlot.NameStyle));
    }

    internal JsonObject ToJson()
    {
        var o = new JsonObject();
        foreach (var slot in Enum.GetValues<RewardSlot>())
            if (this[slot] is { } id) o[LevelRewards.SlotKey(slot)] = id;
        return o;
    }
}
