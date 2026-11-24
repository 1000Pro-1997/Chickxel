using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.Core.Community;
using Xunit;

namespace Chickxel.Core.Tests;

public class LevelRewardsTests
{
    [Fact]
    public void OneRewardPerLevelFromTwoToFifty_EachWithArtAndNames()
    {
        Assert.Equal(Enumerable.Range(2, 49), LevelRewards.All.Select(r => r.Level));
        Assert.Null(LevelRewards.At(1));
        foreach (var reward in LevelRewards.All)
        {
            Assert.Same(reward, LevelRewards.At(reward.Level));
            // 이름이 두 언어 모두 있음(없으면 ID가 나옴)
            Assert.NotEqual(reward.Id, reward.NameIn(CultureInfo.GetCultureInfo("ko")));
            Assert.NotEqual(reward.Id, reward.NameIn(CultureInfo.GetCultureInfo("en")));
            // 칸마다 그릴 그림이 있음
            IPixelArt? art = reward.Slot switch
            {
                RewardSlot.Badge => new LevelBadgeArt(reward.Level, reward.Id),
                RewardSlot.FrameSmall or RewardSlot.FrameLarge => new AvatarFrameArt(reward.Id, 14),
                RewardSlot.Effect => new AvatarEffectArt(reward.Id, 14),
                RewardSlot.Card => new ProfileCardArt(reward.Id, 64, 40),
                _ => null,
            };
            if (art is LevelBadgeArt badge) Assert.Equal(reward.Id, badge.Skin.Id);
            if (art is null) continue;
            var pixels = new Chickxel.Core.Model.Color32[art.Width * art.Height];
            art.Render(7, pixels);
            Assert.Contains(pixels, p => p.A > 0);
        }
    }

    [Fact]
    public void EquippedRewardsAreReadFromUserJsonAndOnlyCountWhenUnlockedInTheirSlot()
    {
        var json = JsonNode.Parse("""
            {"id":"u1","handle":"hen","name":"Hen","level":21,"xp":620,"levelStartXp":580,"nextLevelXp":630,
             "rewards":{"title":"title.rooster","badge":"frame-s.wood","frameLarge":"frame-l.crown","card":"card.gold"}}
            """);
        var user = CommunityUser.Read(json);
        Assert.Equal("title.rooster", user.Equipped.Equipped(RewardSlot.Title, user.Level)?.Id);
        Assert.Equal("card.gold", user.Equipped.Equipped(RewardSlot.Card, user.Level)?.Id);
        // 칸이 다른 ID, 아직 받지 않은 레벨(왕관 = 25)은 장착하지 않은 것으로
        Assert.Null(user.Equipped.Equipped(RewardSlot.Badge, user.Level));
        Assert.Null(user.Equipped.Equipped(RewardSlot.FrameLarge, user.Level));
        Assert.Equal(["badge.forest"], LevelRewards.Between(21, 22).Select(r => r.Id));
        Assert.Equal("badge.forest", LevelRewards.Next(21)?.Id);
    }
}
