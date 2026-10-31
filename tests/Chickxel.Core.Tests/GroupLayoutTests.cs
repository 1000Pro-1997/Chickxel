using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class GroupLayoutTests
{
    /// <summary>32×16 캔버스, 그룹 하나에 아래부터 A(4×4)·B(6×2)·C(2×8) — 타임라인 순서(맨 위 먼저)는 C, B, A.</summary>
    private static (SpriteProject P, Layer Group, Layer A, Layer B, Layer C) Rig()
    {
        var p = new SpriteProject(32, 16);
        var g = p.AddGroup("rig");
        var a = p.AddPart(g, "A", 4, 4, new Point(20, 1));
        var b = p.AddPart(g, "B", 6, 2, new Point(3, 9));
        var c = p.AddPart(g, "C", 2, 8, new Point(-5, 0));
        return (p, g, a, b, c);
    }

    private static Point[] Offsets(params Layer[] parts) => parts.Select(l => l.PartOffset).ToArray();

    [Theory]
    // 안쪽 영역 (1,1,30,14), 폭 합 12 + 간격 2×2 = 16
    [InlineData(LayoutDistribution.Start, LayoutAlign.Center, 1, 4, 5, 7, 13, 6)]
    [InlineData(LayoutDistribution.Center, LayoutAlign.Start, 8, 1, 12, 1, 20, 1)]
    [InlineData(LayoutDistribution.End, LayoutAlign.End, 15, 7, 19, 13, 27, 11)]
    // 고르게: 남는 18px을 사이 두 곳에 9씩, 끝 부위가 영역 오른쪽 끝(31)에 붙음
    [InlineData(LayoutDistribution.SpaceBetween, LayoutAlign.Center, 1, 4, 12, 7, 27, 6)]
    public void Row_PlacesPartsByGapMarginAlignmentAndDistribution(LayoutDistribution dist, LayoutAlign align,
        int cx, int cy, int bx, int by, int ax, int ay)
    {
        var (p, g, a, b, c) = Rig();
        p.SetGroupLayout(g, new GroupLayout { Direction = GroupLayoutDirection.Row, Gap = 2, Margin = 1, Distribution = dist, Align = align });
        Assert.Equal([new Point(cx, cy), new Point(bx, by), new Point(ax, ay)], Offsets(c, b, a));
        Assert.Equal(new Size(6, 2), b.PartSize); // 크기는 그대로
    }

    [Fact]
    public void Column_StacksTopFirstWithGap()
    {
        var (p, g, a, b, c) = Rig();
        p.SetGroupLayout(g, new GroupLayout
        {
            Direction = GroupLayoutDirection.Column, Gap = 1, Distribution = LayoutDistribution.Start, Align = LayoutAlign.End,
        });
        // x: 오른쪽 끝(32 - 폭), y: 0 → 8+1 → 9+2+1
        Assert.Equal([new Point(30, 0), new Point(26, 9), new Point(28, 12)], Offsets(c, b, a));
    }

    [Fact]
    public void Grid_UsesColumnWidthsAndRowHeightsAndAlignsInCell()
    {
        var (p, g, a, b, c) = Rig();
        p.SetGroupLayout(g, new GroupLayout
        {
            Direction = GroupLayoutDirection.Grid, Columns = 2, Gap = 1, Distribution = LayoutDistribution.Start, Align = LayoutAlign.Center,
        });
        // 칸: [C B] / [A], 열 폭 = 4(A)·6(B), 줄 높이 = 8·4 → 열 x 0·5, 줄 y 0·9. 칸 안 가운데.
        Assert.Equal([new Point(1, 0), new Point(5, 3), new Point(0, 9)], Offsets(c, b, a));
    }

    [Fact]
    public void BottomFirstOrder_ReversesTimelineOrder()
    {
        var (p, g, a, b, c) = Rig();
        p.SetGroupLayout(g, new GroupLayout { Gap = 0, Distribution = LayoutDistribution.Start, Align = LayoutAlign.Start, Order = GroupLayoutOrder.BottomFirst });
        Assert.Equal([new Point(0, 0), new Point(4, 0), new Point(10, 0)], Offsets(a, b, c));
    }

    [Fact]
    public void CustomArea_LaysOutInsideArea()
    {
        var (p, g, a, b, c) = Rig();
        p.SetGroupLayout(g, new GroupLayout
        {
            Gap = 0, Distribution = LayoutDistribution.Start, Align = LayoutAlign.Start, Area = new Rectangle(10, 2, 12, 12),
        });
        Assert.Equal([new Point(10, 2), new Point(12, 2), new Point(18, 2)], Offsets(c, b, a));
    }

    [Fact]
    public void ApplyingLayout_IsOneUndoStepThatRestoresOriginalOffsets()
    {
        var (p, g, a, b, c) = Rig();
        var before = Offsets(a, b, c);
        int depth = p.History.UndoCount;
        p.SetGroupLayout(g, new GroupLayout());
        Assert.Equal(depth + 1, p.History.UndoCount);
        Assert.NotEqual(before, Offsets(a, b, c));
        var after = Offsets(a, b, c);

        p.Undo();
        Assert.Null(g.AutoLayout);
        Assert.Equal(before, Offsets(a, b, c));
        p.Redo();
        Assert.NotNull(g.AutoLayout);
        Assert.Equal(after, Offsets(a, b, c));
    }

    [Fact]
    public void AddingPart_RelaysOutInSameUndoStep()
    {
        var (p, g, a, b, c) = Rig();
        p.SetGroupLayout(g, new GroupLayout { Gap = 0, Distribution = LayoutDistribution.Start, Align = LayoutAlign.Start });
        var laidOut = Offsets(a, b, c);
        int depth = p.History.UndoCount;

        var d = p.AddPart(g, "D", 3, 3, new Point(25, 10)); // 맨 위 부위 → 맨 앞
        Assert.Equal(depth + 1, p.History.UndoCount);
        Assert.Equal([new Point(0, 0), new Point(3, 0), new Point(5, 0), new Point(11, 0)], Offsets(d, c, b, a));

        p.Undo();
        Assert.DoesNotContain(d, p.Layers);
        Assert.Equal(laidOut, Offsets(a, b, c));
    }

    [Fact]
    public void ResizingPart_RelaysOutInSameUndoStep()
    {
        var (p, g, a, b, c) = Rig();
        p.SetGroupLayout(g, new GroupLayout { Gap = 1, Distribution = LayoutDistribution.Start, Align = LayoutAlign.Start });
        var laidOut = Offsets(a, b, c); // C 0, B 3, A 10
        int depth = p.History.UndoCount;

        p.ResizePart(p.IndexOf(c), 5, 8, Anchor.TopLeft);
        Assert.Equal(depth + 1, p.History.UndoCount);
        Assert.Equal([new Point(0, 0), new Point(6, 0), new Point(13, 0)], Offsets(c, b, a));

        p.Undo();
        Assert.Equal(new Size(2, 8), c.PartSize);
        Assert.Equal(laidOut, Offsets(a, b, c));
    }

    [Fact]
    public void ReorderingAndDeletingParts_Relayout()
    {
        var (p, g, a, b, c) = Rig();
        p.SetGroupLayout(g, new GroupLayout { Gap = 0, Distribution = LayoutDistribution.Start, Align = LayoutAlign.Start });
        p.MoveLayer(p.IndexOf(a), p.IndexOf(c)); // A를 맨 위로 → A, C, B
        Assert.Equal([new Point(0, 0), new Point(4, 0), new Point(6, 0)], Offsets(a, c, b));
        p.DeleteLayer(p.IndexOf(c));
        Assert.Equal([new Point(0, 0), new Point(4, 0)], Offsets(a, b));
    }

    [Fact]
    public void CanvasResize_RecentersLayout()
    {
        var (p, g, a, b, c) = Rig();
        p.SetGroupLayout(g, new GroupLayout { Gap = 0, Align = LayoutAlign.Start, Distribution = LayoutDistribution.Center });
        Assert.Equal(10, c.PartOffset.X); // (32 - 12) / 2
        p.ResizeCanvas(40, 16, Anchor.TopLeft);
        Assert.Equal([new Point(14, 0), new Point(16, 0), new Point(22, 0)], Offsets(c, b, a));
    }

    [Fact]
    public void HiddenParts_TakeSpaceByDefault_OrStayPutWhenExcluded()
    {
        var (p, g, a, b, c) = Rig();
        var layout = new GroupLayout { Gap = 0, Distribution = LayoutDistribution.Start, Align = LayoutAlign.Start };
        p.SetGroupLayout(g, layout);
        p.SetLayerProperties(p.IndexOf(b), visible: false);
        Assert.Equal([new Point(0, 0), new Point(2, 0), new Point(8, 0)], Offsets(c, b, a));

        p.SetGroupLayout(g, layout with { IncludeHidden = false });
        Assert.Equal(new Point(2, 0), b.PartOffset); // 숨긴 부위는 제자리
        Assert.Equal([new Point(0, 0), new Point(2, 0)], Offsets(c, a));
        p.SetLayerProperties(p.IndexOf(b), visible: true);
        Assert.Equal([new Point(0, 0), new Point(2, 0), new Point(8, 0)], Offsets(c, b, a));
    }

    [Fact]
    public void TurningOff_KeepsCurrentOffsetsAndAllowsManualMoves()
    {
        var (p, g, a, b, c) = Rig();
        p.SetGroupLayout(g, new GroupLayout { Direction = GroupLayoutDirection.Grid, Columns = 2 });
        var laidOut = Offsets(a, b, c);
        Assert.True(b.IsAutoLaidOut);
        Assert.Throws<InvalidOperationException>(() => p.SetPartOffset(p.IndexOf(b), new Point(0, 0)));
        Assert.Equal(laidOut, Offsets(a, b, c));

        p.SetGroupLayout(g, null);
        Assert.False(b.IsAutoLaidOut);
        Assert.Equal(laidOut, Offsets(a, b, c));
        p.SetPartOffset(p.IndexOf(b), new Point(0, 0));
        Assert.Equal(new Point(0, 0), b.PartOffset);
    }

    [Fact]
    public void TurnOffAndMove_FoldIntoOneUndoStep()
    {
        var (p, g, a, b, c) = Rig();
        var layout = new GroupLayout();
        p.SetGroupLayout(g, layout);
        var laidOut = Offsets(a, b, c);
        int depth = p.History.UndoCount;
        p.Edit("부위 위치", ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            p.SetGroupLayout(g, null);
            p.SetPartOffset(p.IndexOf(b), new Point(0, 0));
        });
        Assert.Equal(depth + 1, p.History.UndoCount);
        p.Undo();
        Assert.Equal(layout, g.AutoLayout);
        Assert.Equal(laidOut, Offsets(a, b, c));
    }

    [Fact]
    public void Normalized_ClampsOutOfRangeValues()
    {
        var l = new GroupLayout { Columns = 0, Gap = -3, Margin = -1, Area = new Rectangle(0, 0, 0, 5) }.Normalized();
        Assert.Equal(1, l.Columns);
        Assert.Equal(0, l.Gap);
        Assert.Equal(0, l.Margin);
        Assert.Null(l.Area);
    }

    [Fact]
    public void SaveLoad_RoundTripsLayout_AndMissingMeansOff()
    {
        using var dir = new TempDir();
        var (p, g, a, b, c) = Rig();
        var other = p.AddGroup("plain");
        p.AddPart(other, "X", 2, 2, new Point(3, 3));
        var layout = new GroupLayout
        {
            Direction = GroupLayoutDirection.Grid, Columns = 3, Align = LayoutAlign.End, Distribution = LayoutDistribution.SpaceBetween,
            Gap = 2, Margin = 3, Order = GroupLayoutOrder.BottomFirst, IncludeHidden = false, Area = new Rectangle(1, 2, 20, 10),
        };
        p.SetGroupLayout(g, layout);
        var laidOut = Offsets(a, b, c);
        var path = dir.File("layout.chickxel");
        ProjectIO.Save(p, path);

        var back = ProjectIO.Load(path);
        var bg = back.Layers.Single(l => l.Name == "rig");
        Assert.Equal(laidOut, new[] { "A", "B", "C" }.Select(n => back.Layers.Single(l => l.Name == n).PartOffset).ToArray());
        Assert.Equal(layout, bg.AutoLayout);
        Assert.Null(back.Layers.Single(l => l.Name == "plain").AutoLayout);
        Assert.True(back.Layers.Single(l => l.Name == "B").IsAutoLaidOut);
    }
}
