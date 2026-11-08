namespace Chickxel.Core.Community;

/// <summary>레벨 배지 모양 단계(높을수록 화려함).</summary>
public enum CommunityLevelTier
{
    /// <summary>1~4: 흐린 회색.</summary>
    Plain,
    /// <summary>5~9.</summary>
    Bronze,
    /// <summary>10~19.</summary>
    Silver,
    /// <summary>20~29.</summary>
    Gold,
    /// <summary>30~49: 청록–보라 그러데이션과 은은한 빛.</summary>
    Crystal,
    /// <summary>50 이상: 무지개 그러데이션과 빛.</summary>
    Legend,
}

/// <summary>
/// 서버와 같은 계정 레벨 규칙. 경험치(XP)는 서버가 지금 올라 있는 공유(갤러리 그림·에셋 패키지·받은 좋아요)와 앱을 켜 둔 시간에서 계산한다.
/// 레벨 L에서 L+1까지는 <c>10 + 2 × (L − 1)</c> XP(10, 12, 14 …)라 레벨 L(1부터)은 <c>(L − 1) × (L + 8)</c> XP에서 시작한다
/// (L1 = 0, L2 = 10, L3 = 22, L4 = 36). 최고 레벨은 없다.
/// </summary>
public static class CommunityLevel
{
    /// <summary>레벨 <paramref name="level"/>(1 미만은 1)이 시작하는 XP.</summary>
    public static long StartXp(int level)
    {
        long l = Math.Max(1, level);
        return (l - 1) * (l + 8);
    }

    /// <summary>XP <paramref name="xp"/>(음수는 0)의 레벨: <c>⌊(−7 + √(81 + 4·xp)) / 2⌋</c>를 정수로 바로잡은 값(1 이상).</summary>
    public static int LevelOf(long xp)
    {
        xp = Math.Max(0, xp);
        int level = Math.Max(1, (int)Math.Floor((-7 + Math.Sqrt(81 + 4.0 * xp)) / 2));
        // 큰 값의 부동소수 오차를 정수로 바로잡는다.
        while (level > 1 && StartXp(level) > xp) level--;
        while (StartXp(level + 1) <= xp) level++;
        return level;
    }

    /// <summary>배지 모양 단계: 1~4 흐림, 5~9 동, 10~19 은, 20~29 금, 30~49 크리스털, 50 이상 전설.</summary>
    public static CommunityLevelTier Tier(int level) => level switch
    {
        >= 50 => CommunityLevelTier.Legend,
        >= 30 => CommunityLevelTier.Crystal,
        >= 20 => CommunityLevelTier.Gold,
        >= 10 => CommunityLevelTier.Silver,
        >= 5 => CommunityLevelTier.Bronze,
        _ => CommunityLevelTier.Plain,
    };

    /// <summary>지금 레벨 안에서 다음 레벨까지 간 몫(0~1).</summary>
    public static double Progress(int xp, int levelStartXp, int nextLevelXp)
        => nextLevelXp <= levelStartXp ? 0 : Math.Clamp((double)(xp - levelStartXp) / (nextLevelXp - levelStartXp), 0, 1);
}
