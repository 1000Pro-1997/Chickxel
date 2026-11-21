using System.Drawing;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;
using Xunit;

namespace Chickxel.Core.Tests;

public class PixelateSamplingTests
{
    [Fact]
    public void SampleIndicesSpreadEvenlyIncludingEnds()
    {
        Assert.Equal([0, 30, 60, 89, 119], PixelateOptimizer.SampleIndices(120, 5));
        Assert.Equal([0, 4], PixelateOptimizer.SampleIndices(5, 2));
        Assert.Equal([0], PixelateOptimizer.SampleIndices(9, 1));
        Assert.Equal([0, 1, 2], PixelateOptimizer.SampleIndices(3, 5));
        Assert.Equal([0, 1, 2], PixelateOptimizer.SampleIndices(3, 0));
    }

    /// <summary>프레임 7개를 3개만 뽑아 찾으면 시도는 3프레임으로 돌고, 1위는 같은 설정을 7프레임 모두에 적용한 결과다.</summary>
    [Fact]
    public void SampledOptimizeAppliesBestToEveryFrame()
    {
        const int frames = 7, cell = 40;
        var sheet = new PixelImage(cell * frames, cell);
        var slots = new FrameSlot[frames];
        for (int f = 0; f < frames; f++)
        {
            // 8px 블록 4×4개짜리 무늬(프레임마다 조금씩 다른 색) — 깨끗한 칸 8px.
            for (int by = 0; by < 4; by++)
                for (int bx = 0; bx < 4; bx++)
                    sheet.FillRect(new Rectangle(f * cell + 4 + bx * 8, 4 + by * 8, 8, 8),
                        (bx + by) % 2 == 0 ? new Color32((byte)(180 + f * 10), 40, 40) : new Color32(40, 60, (byte)(150 + f * 10)));
            slots[f] = new FrameSlot(f * cell, 0, cell, cell);
        }
        var start = new PixelateSettings
        {
            Background = new BackgroundSettings { Mode = BackgroundMode.None },
            Frames = new FrameSettings { Mode = FrameSplitMode.Grid, Columns = frames, Rows = 1, ManualRegions = slots, SourceFrames = frames },
            Cleanup = new CleanupSettings { OutlineFit = OutlineFit.Off },
        };
        var src = PixelateSource.Prepare(sheet, start);
        var options = new PixelateOptimizeOptions { Params = OptimizeParam.Colors | OptimizeParam.BorderFraction, MaxTrials = 6, MaxTime = TimeSpan.FromSeconds(60), SampleFrames = 3 };
        var r = PixelateOptimizer.Run(src, start, options);

        Assert.Equal(3, r.SampledFrames);
        Assert.Equal(frames, r.TotalFrames);
        Assert.All(r.Trials, t => Assert.Equal(3, t.Result.Frames.Count));
        Assert.Equal(frames, r.Best.Result.Frames.Count);
        Assert.Same(slots, r.Best.Settings.Frames.ManualRegions);
        Assert.Equal(frames, r.Best.Settings.Frames.SourceFrames);
        // 뽑지 않으면(0) 예전처럼 모든 프레임으로 찾는다.
        var all = PixelateOptimizer.Run(src, start, options with { SampleFrames = 0, MaxTrials = 2 });
        Assert.Equal(0, all.SampledFrames);
        Assert.All(all.Trials, t => Assert.Equal(frames, t.Result.Frames.Count));
    }
}
