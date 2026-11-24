using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>
/// 새 레벨 보상 알림: 계정마다 본 레벨(<see cref="AppSettings.RewardsSeenLevel"/>, 없으면 1)보다 지금 레벨이 높으면 그 사이 레벨의 보상이 "새 보상"이다.
/// 계정 단추의 레벨 배지와 계정 메뉴의 계정 관리가 반짝이고, 계정 창에서 보상을 보면(<see cref="MarkSeen"/>) 멈춘다.
/// </summary>
internal static class RewardNotice
{
    /// <summary>본 레벨이 바뀜(계정 단추가 다시 그림).</summary>
    public static event Action? Changed;

    private static AppSettings Settings => App.Settings;

    public static int SeenLevel(CommunityUser user) => Settings.RewardsSeenLevel.TryGetValue(user.Id, out int level) ? level : 1;

    /// <summary>아직 보지 않은 새 보상(레벨 차례). 레벨을 모르면 없음.</summary>
    public static IReadOnlyList<LevelReward> New(CommunityUser? user)
        => user is { Level: > 1 } u ? LevelRewards.Between(SeenLevel(u), u.Level).ToList() : [];

    public static bool HasNew(CommunityUser? user) => New(user).Count > 0;

    /// <summary>지금 레벨까지 봤다고 기억한다(저장).</summary>
    public static void MarkSeen(CommunityUser user)
    {
        if (user.Level <= 0 || SeenLevel(user) >= user.Level) return;
        Settings.RewardsSeenLevel[user.Id] = user.Level;
        Settings.Save();
        Changed?.Invoke();
    }
}
