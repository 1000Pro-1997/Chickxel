using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class FrameLengthTests
{
    [Fact]
    public void Total_keeps_ratio_and_hits_exact_sum()
    {
        // 100:100:200을 합 1000으로: 정확히 250:250:500, 나누어떨어지지 않는 합 1001도 합이 맞아야 한다.
        Assert.Equal([250, 250, 500], FrameLength.Apply([100, 100, 200], FrameLengthMode.Total, 1000));
        var odd = FrameLength.Apply([100, 100, 100], FrameLengthMode.Total, 1001);
        Assert.Equal(1001, odd.Sum());
        Assert.True(odd.Max() - odd.Min() <= 1);
    }

    [Fact]
    public void Uniform_modes_convert_units()
    {
        Assert.Equal([83, 83], FrameLength.Apply([100, 40], FrameLengthMode.Fps, 12));
        Assert.Equal([250, 250], FrameLength.Apply([100, 40], FrameLengthMode.Seconds, 0.25));
        Assert.Equal([100, 100], FrameLength.Apply([100, 40], FrameLengthMode.GameFrames, 6));
        Assert.Equal([50, 20], FrameLength.Apply([100, 40], FrameLengthMode.Speed, 2));
        Assert.Equal([1], FrameLength.Apply([100], FrameLengthMode.Fps, 5000));
    }
}
