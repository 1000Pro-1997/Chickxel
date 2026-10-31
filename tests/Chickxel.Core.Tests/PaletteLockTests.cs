using Chickxel.Core.AiDraw;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class PaletteLockTests
{
    private static readonly Color32 Black = new(0, 0, 0), White = new(255, 255, 255), Red = new(200, 30, 30), Green = new(40, 170, 60), Blue = new(40, 70, 200);

    [Fact]
    public void Maps_to_nearest_palette_color_and_keeps_exact_colors()
    {
        var lockp = new PaletteLock([Black, White, Red, Green, Blue]);
        Assert.Equal(Red, lockp.Map(new Color32(230, 50, 40)));
        Assert.Equal(Green, lockp.Map(new Color32(20, 140, 40)));
        Assert.Equal(Blue, lockp.Map(new Color32(70, 90, 240)));
        Assert.Equal(Black, lockp.Map(new Color32(20, 20, 25)));
        Assert.Equal(White, lockp.Map(new Color32(240, 240, 235)));
        Assert.Equal(Red, lockp.Map(Red));
    }

    [Fact]
    public void Transparent_stays_and_translucent_becomes_opaque_palette_color()
    {
        var lockp = new PaletteLock([Black, Red]);
        Assert.Equal(Color32.Transparent, lockp.Map(Color32.Transparent));
        Assert.Equal(0, lockp.Map(new Color32(200, 30, 30, 0)).A);
        var half = lockp.Map(new Color32(200, 30, 30, 128));
        Assert.Equal(Red, half);
        Assert.Equal(255, half.A);
    }

    [Fact]
    public void Palette_alpha_is_ignored_transparent_entries_and_duplicates_are_dropped()
    {
        var lockp = new PaletteLock([Color32.Transparent, new Color32(200, 30, 30, 100), Red, Black]);
        Assert.Equal([Red, Black], lockp.Colors);
        Assert.Equal("#C81E1E, #000000", lockp.ListColors());
        Assert.True(new PaletteLock([Color32.Transparent]).IsEmpty);
        // 빈 팔레트는 제한하지 않음.
        var none = new PaletteLock([]);
        Assert.Equal(new Color32(1, 2, 3), none.Map(new Color32(1, 2, 3)));
        Assert.Empty(none.Changes);
    }

    [Fact]
    public void Records_each_change_once_in_order()
    {
        var lockp = new PaletteLock([Black, Red]);
        lockp.Map(new Color32(230, 50, 40));
        lockp.Map(Red);
        lockp.Map(new Color32(230, 50, 40));
        lockp.Map(new Color32(10, 10, 10, 128));
        Assert.Equal(2, lockp.Changes.Count);
        Assert.Equal("#E63228→#C81E1E, #0A0A0A80→#000000", lockp.DescribeChanges());
        Assert.Equal("#E63228→#C81E1E, +1", lockp.DescribeChanges(max: 1));
    }

    [Fact]
    public void Lists_at_most_64_colors()
    {
        var many = Enumerable.Range(0, 100).Select(i => new Color32((byte)i, (byte)(i * 2), 7)).ToList();
        Assert.Equal(PaletteLock.MaxListed, new PaletteLock(many).ListColors().Split(", ").Length);
    }

    [Fact]
    public void Default_settings_keep_palette_only_off()
    {
        Assert.False(new AiDrawSettings().PaletteOnly);
    }
}
