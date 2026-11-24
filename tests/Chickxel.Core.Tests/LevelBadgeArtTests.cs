using Chickxel.Core.Community;
using Chickxel.Core.Model;
using Xunit;

namespace Chickxel.Core.Tests;

public class LevelBadgeArtTests
{
    private static Color32[][] Frames(LevelBadgeArt art, int count)
    {
        var frames = new Color32[count][];
        for (int f = 0; f < count; f++)
        {
            frames[f] = new Color32[art.Width * art.Height];
            art.Render(f, frames[f]);
        }
        return frames;
    }

    [Fact]
    public void DefaultTierBadgesStayStill_BadgeRewardsMove()
    {
        foreach (int level in new[] { 3, 7, 15, 21, 35, 55 })
        {
            var still = Frames(new LevelBadgeArt(level), 60);
            Assert.All(still, f => Assert.Equal(still[0], f));
        }
        foreach (string skin in new[] { "badge.mint", "badge.ocean", "badge.lava", "badge.legend-gold" })
        {
            var frames = Frames(new LevelBadgeArt(21, skin), 60);
            Assert.Contains(frames, f => !f.AsSpan().SequenceEqual(frames[0]));
        }
    }

    [Fact]
    public void LongerLevelNumbersWidenTheBadge()
        => Assert.Equal(new LevelBadgeArt(21).BadgeWidth + 8, new LevelBadgeArt(1234).BadgeWidth);
}
