using System.Text.Json.Nodes;
using Chickxel.Core.Community;
using Xunit;

namespace Chickxel.Core.Tests;

public class CommunityLevelTests
{
    /// <summary>저장한 계정 목록과 같은 길: JSON 글자로 썼다가 다시 읽는다.</summary>
    private static CommunityUser RoundTrip(CommunityUser user) => CommunityUser.Read(JsonNode.Parse(user.ToJson().ToJsonString()));

    [Fact]
    public void Curve_StepsGrowByTwo_AndMatchTheServer()
    {
        Assert.Equal([0L, 10, 22, 36, 52], Enumerable.Range(1, 5).Select(CommunityLevel.StartXp));
        for (int level = 1; level < 200; level++)
            Assert.Equal(10 + 2 * (level - 1), CommunityLevel.StartXp(level + 1) - CommunityLevel.StartXp(level));
        Assert.Equal(0, CommunityLevel.StartXp(0));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(21, 2)]
    [InlineData(22, 3)]
    [InlineData(35, 3)]
    [InlineData(36, 4)]
    [InlineData(-5, 1)]
    public void LevelOf_SmallValues(long xp, int level) => Assert.Equal(level, CommunityLevel.LevelOf(xp));

    [Fact]
    public void LevelOf_IsExactAtEveryBoundary_EvenForLargeValues()
    {
        foreach (int level in new[] { 2, 50, 99, 100, 1_000, 46_340, 1_000_000, 3_000_000 })
        {
            long start = CommunityLevel.StartXp(level);
            Assert.Equal(level, CommunityLevel.LevelOf(start));
            Assert.Equal(level - 1, CommunityLevel.LevelOf(start - 1));
            Assert.Equal(level, CommunityLevel.LevelOf(CommunityLevel.StartXp(level + 1) - 1));
        }
        Assert.True(CommunityLevel.LevelOf(int.MaxValue) > 46_000);
    }

    [Theory]
    [InlineData(1, CommunityLevelTier.Plain)]
    [InlineData(4, CommunityLevelTier.Plain)]
    [InlineData(5, CommunityLevelTier.Bronze)]
    [InlineData(10, CommunityLevelTier.Silver)]
    [InlineData(20, CommunityLevelTier.Gold)]
    [InlineData(30, CommunityLevelTier.Crystal)]
    [InlineData(49, CommunityLevelTier.Crystal)]
    [InlineData(50, CommunityLevelTier.Legend)]
    [InlineData(500, CommunityLevelTier.Legend)]
    public void Tier_FollowsTheLevelBands(int level, CommunityLevelTier tier) => Assert.Equal(tier, CommunityLevel.Tier(level));

    [Fact]
    public void Progress_IsTheShareOfTheCurrentLevel()
    {
        Assert.Equal(0.5, CommunityLevel.Progress(16, 10, 22));
        Assert.Equal(0, CommunityLevel.Progress(5, 10, 22));
        Assert.Equal(1, CommunityLevel.Progress(40, 10, 22));
        Assert.Equal(0, CommunityLevel.Progress(10, 10, 10));
    }

    [Fact]
    public void User_ReadsLevelFields_AndRoundTripsThem()
    {
        var json = JsonNode.Parse("""
            {"id":"abcdefghijklmnop","handle":"pixel-cat","name":"Pixel Cat","createdAt":"2026-11-01T00:00:00Z",
             "level":3,"xp":30,"levelStartXp":22,"nextLevelXp":36}
            """);
        var user = CommunityUser.Read(json);
        Assert.Equal((3, 30, 22, (int?)36), (user.Level, user.Xp, user.LevelStartXp, user.NextLevelXp));
        Assert.True(user.HasXp);
        Assert.Equal(user, RoundTrip(user));

        var high = user with { Level = 250, Xp = 64_800, LevelStartXp = 64_242, NextLevelXp = 64_750 + 508 };
        Assert.Equal(high, RoundTrip(high));
    }

    [Fact]
    public void User_WithoutLevelFields_StaysUnknown_AndWritesNoLevelFields()
    {
        var old = CommunityUser.Read(JsonNode.Parse("""{"id":"abcdefghijklmnop","handle":"pixel-cat","name":"Pixel Cat"}"""));
        Assert.Equal((0, 0, 0, (int?)null), (old.Level, old.Xp, old.LevelStartXp, old.NextLevelXp));
        Assert.False(old.HasXp);
        var written = old.ToJson();
        Assert.False(written.ContainsKey("level"));
        Assert.False(written.ContainsKey("xp"));
        Assert.Equal(old, RoundTrip(old));
    }

    [Fact]
    public void User_WithOnlyALevel_HasNoXp_AndBadValuesAreIgnored()
    {
        var author = CommunityUser.Read(JsonNode.Parse("""{"id":"abcdefghijklmnop","handle":"pixel-cat","name":"Pixel Cat","level":12}"""));
        Assert.Equal(12, author.Level);
        Assert.False(author.HasXp);
        Assert.Equal(author, RoundTrip(author));

        var big = CommunityUser.Read(JsonNode.Parse("""{"id":"abcdefghijklmnop","handle":"pixel-cat","name":"Pixel Cat","level":500,"xp":"lots","nextLevelXp":null}"""));
        Assert.Equal((500, 0, (int?)null), (big.Level, big.Xp, big.NextLevelXp));
        Assert.False(big.HasXp);
        var negative = CommunityUser.Read(JsonNode.Parse("""{"id":"abcdefghijklmnop","handle":"pixel-cat","name":"Pixel Cat","level":-3,"xp":40}"""));
        Assert.Equal((0, 0), (negative.Level, negative.Xp));
    }
}
