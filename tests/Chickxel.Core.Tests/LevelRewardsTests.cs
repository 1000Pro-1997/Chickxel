using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.Core.Community;
using Xunit;

namespace Chickxel.Core.Tests;

public class LevelRewardsTests
{
    [Fact]
    public void OneRewardPerLevelFromTwoToHundred_EachWithArtAndNames()
    {
        Assert.Equal(Enumerable.Range(2, 99), LevelRewards.All.Select(r => r.Level));
        Assert.Null(LevelRewards.At(1));
        foreach (var reward in LevelRewards.All)
        {
            Assert.Same(reward, LevelRewards.At(reward.Level));
            // 이름이 두 언어 모두 있음(없으면 ID가 나옴)
            Assert.NotEqual(reward.Id, reward.NameIn(CultureInfo.GetCultureInfo("ko")));
            Assert.NotEqual(reward.Id, reward.NameIn(CultureInfo.GetCultureInfo("en")));
            // 칸마다 그릴 것이 있음(테두리는 작은 모양·큰 모양 둘 다)
            IPixelArt[] arts = reward.Slot switch
            {
                RewardSlot.Badge => [new LevelBadgeArt(reward.Level, reward.Id)],
                RewardSlot.Frame => [new AvatarFrameArt(reward.Id, 14, large: false), new AvatarFrameArt(reward.Id, 18, large: true)],
                RewardSlot.Effect => [new AvatarEffectArt(reward.Id, 14)],
                RewardSlot.Card => [new ProfileCardArt(reward.Id, 64, 40)],
                _ => [],
            };
            if (reward.Slot == RewardSlot.Badge) Assert.Equal(reward.Id, ((LevelBadgeArt)arts[0]).Skin.Id);
            if (reward.Slot == RewardSlot.NameStyle) Assert.NotNull(NameStyle.Find(reward.Id));
            foreach (var art in arts)
            {
                var pixels = new Chickxel.Core.Model.Color32[art.Width * art.Height];
                art.Render(7, pixels);
                Assert.Contains(pixels, p => p.A > 0);
            }
        }
    }

    [Fact]
    public void AutoPicksTheHighestUnlockedInTheSlot_NoneAndLockedChoicesResolve()
    {
        Assert.Equal("frame.gold", LevelRewards.Resolve(RewardSlot.Frame, null, 24)?.Id);
        Assert.Null(LevelRewards.Resolve(RewardSlot.Frame, LevelRewards.NoneChoice, 24));
        Assert.Equal("frame.wood", LevelRewards.Resolve(RewardSlot.Frame, "frame.wood", 24)?.Id);
        // 레벨이 내려가 잠긴 보상·칸이 다른 ID는 자동으로
        Assert.Equal("frame.gold", LevelRewards.Resolve(RewardSlot.Frame, "frame.crown", 24)?.Id);
        Assert.Equal("frame.gold", LevelRewards.Resolve(RewardSlot.Frame, "badge.mint", 24)?.Id);
        // 아직 받은 것이 없는 칸은 자동이어도 없음
        Assert.Null(LevelRewards.Resolve(RewardSlot.Card, null, 9));
    }

    [Fact]
    public void EquippedRewardsAreReadFromUserJsonAndOnlyCountWhenUnlockedInTheirSlot()
    {
        var json = JsonNode.Parse("""
            {"id":"u1","handle":"hen","name":"Hen","level":21,"xp":620,"levelStartXp":580,"nextLevelXp":630,
             "rewards":{"title":"title.cluck","badge":"frame.wood","frame":"frame.crown","card":"card.gold","nameStyle":"name.ocean"}}
            """);
        var user = CommunityUser.Read(json);
        Assert.Equal("title.cluck", user.Equipped.Equipped(RewardSlot.Title, user.Level)?.Id);
        Assert.Equal("card.gold", user.Equipped.Equipped(RewardSlot.Card, user.Level)?.Id);
        Assert.Equal("name.ocean", user.Equipped.Equipped(RewardSlot.NameStyle, user.Level)?.Id);
        // 칸이 다른 ID, 아직 받지 않은 레벨(왕관 = 25)은 끼지 않은 것으로
        Assert.Null(user.Equipped.Equipped(RewardSlot.Badge, user.Level));
        Assert.Null(user.Equipped.Equipped(RewardSlot.Frame, user.Level));
        Assert.Equal(["badge.forest"], LevelRewards.Between(21, 22).Select(r => r.Id));
        Assert.Equal("badge.forest", LevelRewards.Next(21)?.Id);
    }
}
