using System.Drawing;
using Gpixel.Core.Model;
using Gpixel.Core.Pixelate;

namespace Gpixel.Core.Tests;

public class LocalGridBoundaryTests
{
    [Fact]
    public void SharpDarkSkinBoundaryBeatsLongerWeakParallelBoundary()
    {
        const int width = 32, height = 40;
        var image = new PixelImage(width, height);
        var dark = new Color32(26, 26, 38);
        var darkTexture = new Color32(60, 53, 65);
        var cream = new Color32(245, 221, 190);
        var weakTexture = new Color32(205, 186, 159);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                image[x, y] = x < 7
                    ? y < 9 ? dark : y < 12 ? darkTexture : cream
                    : x < 16 && y < 9 ? weakTexture : cream;

        var edges = GridDetector.BlockEdges(image, GridDetector.LabOf(image), image.Bounds);
        // The weaker horizontal edge has sixteen hits at y=9, while the dark/skin
        // edge has only seven at y=12 in the same two-cell vertical stripe.
        Assert.Equal(16, edges.Cols.Take(16).Count(line => line.Contains(9)));
        Assert.Equal(7, edges.Cols.Take(16).Count(line => line.Contains(12)));
        Assert.Equal(16, edges.Y[9]);
        Assert.Equal(7, edges.Y[12]);
        float strong = edges.ColStrengths[3][Array.IndexOf(edges.Cols[3], 12)];
        float weak = edges.ColStrengths[10][Array.IndexOf(edges.Cols[10], 9)];
        Assert.True(strong > weak, $"contrast weights {strong} vs {weak}");
        Assert.InRange(strong, 0.25f, 2f);
        Assert.InRange(weak, 0.25f, 2f);

        int[] xc = [0, 8, 16, 24, 32];
        int[] yc = [0, 10, 20, 30, 40];
        var local = LocalGrid.Correct(edges, xc, yc, 0.35, 2, LocalGrid.DefaultMinCell);
        Assert.NotNull(local);
        Assert.Equal(12, local.YByBand[0][1]);
        Assert.Equal(2, local.Band);
        foreach (int[] cuts in local.YByBand)
            for (int r = 1; r < cuts.Length; r++)
                Assert.InRange(cuts[r] - cuts[r - 1], 7, 13);
        var layout = new CellLayout(xc, yc, local);
        for (int c = 0; c < layout.Columns; c++)
            for (int r = 0; r < layout.Rows; r++)
            {
                var (x0, y0, x1, y1) = layout.Cell(c, r);
                Assert.True(x0 < x1 && y0 < y1, $"cell ({c}, {r}) must remain rectangular");
            }
    }

    [Fact]
    public void IsolatedContradictoryHitsDoNotPullCellBoundary()
    {
        int[][] cols = Enumerable.Range(0, 16).Select(x => x is 1 or 2 ? new[] { 12 } : Array.Empty<int>()).ToArray();
        int[][] rows = Enumerable.Range(0, 30).Select(_ => Array.Empty<int>()).ToArray();
        var edges = new GridDetector.BlockEdgeMap([], [], [])
        {
            Area = new Rectangle(0, 0, 16, 30),
            Rows = rows,
            Cols = cols,
            ColStrengths = Enumerable.Range(0, 16).Select(x => x is 1 or 2 ? new[] { 2f } : Array.Empty<float>()).ToArray(),
        };
        Assert.Null(LocalGrid.Correct(edges, [0, 8, 16], [0, 10, 20, 30], 0.35, 2, LocalGrid.DefaultMinCell));
    }

    [Fact]
    public void CoherentBrickEdgesRetainOppositeOffsetsInAdjacentBands()
    {
        int[][] rows = Enumerable.Range(0, 20)
            .Select(y => y < 10 ? new[] { 12 } : new[] { 8 }).ToArray();
        var edges = new GridDetector.BlockEdgeMap([], [], [])
        {
            Area = new Rectangle(0, 0, 30, 20),
            Rows = rows,
            Cols = Enumerable.Range(0, 30).Select(_ => Array.Empty<int>()).ToArray(),
        };
        var local = LocalGrid.Correct(edges, [0, 10, 20, 30], [0, 10, 20], 0.35, 1, LocalGrid.DefaultMinCell);
        Assert.NotNull(local);
        Assert.Equal(12, local.XByBand[0][1]);
        Assert.Equal(8, local.XByBand[1][1]);
    }
}
