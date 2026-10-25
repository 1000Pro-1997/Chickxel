using Gpixel.Core.Model;
using Gpixel.Core.Pixelate;

namespace Gpixel.Core.Tests;

public class WeightedCellSamplingTests
{
    private static readonly Color32 Dark = new(32, 25, 28);
    private static readonly Color32 Skin = new(225, 184, 148);

    private static PixelateSource Prepare(PixelImage image) => PixelateSource.Prepare(image, new PixelateSettings
    {
        Background = new BackgroundSettings { Mode = BackgroundMode.None, RemoveSpecks = 0 },
        Frames = new FrameSettings { Mode = FrameSplitMode.Single },
    });

    private static Color32 Sample(PixelateSource source, DownsampleMode mode, int cell, double border = 0, double alpha = 0.5)
        => Pixelator.CellColor(source, 0, new CellLayout([0, cell], [0, cell]), 0, 0,
            new DownsampleSettings { Mode = mode, BorderFraction = border, AlphaThreshold = alpha });

    [Fact]
    public void WeightedModesAppendWithoutChangingPersistedValues()
    {
        Assert.Equal(0, (int)DownsampleMode.Mode);
        Assert.Equal(1, (int)DownsampleMode.Median);
        Assert.Equal(2, (int)DownsampleMode.Center);
        Assert.Equal(3, (int)DownsampleMode.Mean);
        Assert.Equal(4, (int)DownsampleMode.CenterWeighted);
        Assert.Equal(5, (int)DownsampleMode.ClusterWeighted);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void CenterWeightedFavorsCenteredDetailMoreThanMean(int cell)
    {
        var image = new PixelImage(cell, cell);
        for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++) image[x, y] = Dark;
        // Centered 2x2 patch has less area but greater spatial support than its surroundings.
        for (int y = (cell - 2) / 2; y < (cell - 2) / 2 + 2; y++)
            for (int x = (cell - 2) / 2; x < (cell - 2) / 2 + 2; x++) image[x, y] = Skin;
        var source = Prepare(image);
        var mean = Sample(source, DownsampleMode.Mean, cell);
        var weighted = Sample(source, DownsampleMode.CenterWeighted, cell);
        Assert.True(weighted.R > mean.R && weighted.G > mean.G && weighted.B > mean.B,
            $"mean {mean.ToHex()}, weighted {weighted.ToHex()}");
        Assert.Equal(weighted, Sample(source, DownsampleMode.CenterWeighted, cell));
    }

    [Fact]
    public void ClusterWeightedSelectsCenteredMaterialWithoutBlendingOthers()
    {
        var image = new PixelImage(5, 5);
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++) image[x, y] = y < 2 ? Dark : Skin;
        var source = Prepare(image);
        Assert.Equal(Skin, Sample(source, DownsampleMode.ClusterWeighted, 5));
        var mean = Sample(source, DownsampleMode.Mean, 5);
        Assert.True(mean.R > Dark.R && mean.R < Skin.R);
        // A stray accent is not a new material and does not pull the selected skin toward it.
        image[0, 4] = new Color32(255, 205, 20);
        image[4, 0] = new Color32(0, 105, 225);
        source = Prepare(image);
        Assert.Equal(Skin, Sample(source, DownsampleMode.ClusterWeighted, 5));
        Assert.Equal(Skin, Sample(source, DownsampleMode.ClusterWeighted, 5));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void ClusterWeightedRetainsSmallCenteredFeatureAndHonorsAlpha(int cell)
    {
        var image = new PixelImage(cell, cell);
        for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++) image[x, y] = Skin;
        int feature = cell == 5 ? 3 : 2;
        int start = (cell - feature) / 2;
        for (int y = start; y < start + feature; y++)
            for (int x = start; x < start + feature; x++) image[x, y] = Dark;
        var source = Prepare(image);
        Assert.Equal(Dark, Sample(source, DownsampleMode.ClusterWeighted, cell));
        image[0, 0] = Color32.Transparent;
        source = Prepare(image);
        Assert.Equal(Dark, Sample(source, DownsampleMode.ClusterWeighted, cell));
        Assert.Equal(Color32.Transparent, Sample(source, DownsampleMode.ClusterWeighted, cell, alpha: 1));
    }

    [Theory]
    [InlineData(DownsampleMode.CenterWeighted)]
    [InlineData(DownsampleMode.ClusterWeighted)]
    public void WeightedModesRespectBorderAndTransparentCenter(DownsampleMode mode)
    {
        var image = new PixelImage(5, 5);
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++) image[x, y] = Skin;
        for (int i = 0; i < 5; i++)
        {
            image[i, 0] = Dark;
            image[i, 4] = Dark;
            image[0, i] = Dark;
            image[4, i] = Dark;
        }
        image[2, 2] = Color32.Transparent;
        var source = Prepare(image);
        Assert.Equal(Skin, Sample(source, mode, 5, border: 0.2));
        Assert.Equal(Color32.Transparent, Sample(source, mode, 5, border: 0.2, alpha: 1));
    }
}
