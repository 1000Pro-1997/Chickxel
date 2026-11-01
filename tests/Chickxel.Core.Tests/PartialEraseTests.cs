using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>지우개 강도·채널: 알파를 줄여 투명하게(R·G·B 모두), 고른 색 채널만 줄이기(일부), 100%·모두면 완전히 지움.</summary>
public class PartialEraseTests
{
    private static readonly Color32 Orange = new(200, 100, 50, 200);

    [Fact]
    public void All_channels_reduce_alpha_and_full_strength_clears()
    {
        Assert.Equal(new Color32(200, 100, 50, 100), PartialErase.Apply(Orange, 50, true, true, true));
        Assert.Equal(Color32.Transparent, PartialErase.Apply(Orange, 100, true, true, true));
        Assert.True(PartialErase.IsFull(100, true, true, true));
        Assert.False(PartialErase.IsFull(99, true, true, true));
        Assert.False(PartialErase.IsFull(100, true, false, true));
    }

    [Fact]
    public void Rubbing_again_reduces_what_is_left_until_transparent()
    {
        var c = Orange;
        for (int i = 0; i < 3; i++) c = PartialErase.Apply(c, 50, true, true, true);
        Assert.Equal(25, c.A); // 200 → 100 → 50 → 25
        for (int i = 0; i < 20 && c.A != 0; i++) c = PartialErase.Apply(c, 50, true, true, true);
        Assert.Equal(Color32.Transparent, c); // 알파 0이면 완전히 지운 값과 같음
    }

    [Fact]
    public void Some_channels_remove_only_that_color_and_keep_alpha()
    {
        Assert.Equal(new Color32(0, 100, 50, 200), PartialErase.Apply(Orange, 100, true, false, false));
        Assert.Equal(new Color32(200, 75, 37, 200), PartialErase.Apply(Orange, 25, false, true, true));
        Assert.Equal(Orange, PartialErase.Apply(Orange, 50, false, false, false));
        Assert.Equal(Color32.Transparent, PartialErase.Apply(Color32.Transparent, 50, true, false, false));
    }
}
