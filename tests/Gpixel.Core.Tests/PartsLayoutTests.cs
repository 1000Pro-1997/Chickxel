using System.Drawing;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class PartsLayoutTests
{
    /// <summary>그룹 하나에 Head(4×2)·Arm(2×5), 프레임 3개. Arm은 프레임 1이 비어 있다.</summary>
    private static SpriteProject Rig()
    {
        var p = new SpriteProject(8, 8);
        var g = p.AddGroup("rig");
        var arm = p.AddPart(g, "Arm", 2, 5);
        var head = p.AddPart(g, "Head", 4, 2);
        p.AddFrame();
        p.AddFrame();
        void Paint(Layer l, int f)
        {
            p.BeginCelStroke(p.IndexOf(l), f).Set(0, 0, new Color32(255, 0, 0));
            p.CommitCelStroke("점");
        }
        for (int f = 0; f < 3; f++) Paint(head, f);
        Paint(arm, 0);
        Paint(arm, 2);
        p.Export.Common.Spacing = 1;
        return p;
    }

    private static Rectangle RectOf(SheetResult s, string name) => s.Entries.Single(e => e.Name == name).Rect;

    [Fact]
    public void RowPerPart_RowsArePartsColumnsAreFrames()
    {
        var p = Rig();
        p.Export.Common.Order = GridOrder.ByPart;
        var s = ProjectExport.BuildPartsSheet(p, "rig_parts");
        // 맨 위 부위(Head)가 첫 줄, 열 폭 = 가장 넓은 부위(4) + 간격 1
        Assert.Equal(new Rectangle(0, 0, 4, 2), RectOf(s, "Head"));
        Assert.Equal(new Rectangle(5, 0, 4, 2), RectOf(s, "Head_1"));
        Assert.Equal(new Rectangle(10, 0, 4, 2), RectOf(s, "Head_2"));
        Assert.Equal(new Rectangle(0, 3, 2, 5), RectOf(s, "Arm"));
        Assert.Equal(new Rectangle(10, 3, 2, 5), RectOf(s, "Arm_2")); // 빈 프레임 1 자리는 비움
        Assert.Equal(new Size(14, 8), s.Image.Size);
    }

    [Fact]
    public void RowPerFrame_RowsAreFramesColumnsAreParts()
    {
        var p = Rig();
        p.Export.Common.Order = GridOrder.ByFrame;
        var s = ProjectExport.BuildPartsSheet(p, "rig_parts");
        // 줄 높이 = 가장 높은 부위(5) + 간격 1, 열 = Head(4) 다음 Arm
        Assert.Equal(new Rectangle(0, 0, 4, 2), RectOf(s, "Head"));
        Assert.Equal(new Rectangle(5, 0, 2, 5), RectOf(s, "Arm"));
        Assert.Equal(new Rectangle(0, 6, 4, 2), RectOf(s, "Head_1"));
        Assert.Equal(new Rectangle(5, 12, 2, 5), RectOf(s, "Arm_2"));
        Assert.Equal(new Size(7, 17), s.Image.Size);
    }

    [Fact]
    public void PartsLayout_PowerOfTwo_AndSavedInProject()
    {
        var p = Rig();
        p.Export.Common.Order = GridOrder.ByPart;
        p.Export.Common.PowerOfTwo = true;
        Assert.Equal(new Size(16, 8), ProjectExport.BuildPartsSheet(p, "x").Image.Size);

        using var dir = new TempDir();
        var path = dir.File("rig.gpixel");
        ProjectIO.Save(p, path);
        var back = ProjectIO.Load(path).Export.Common;
        Assert.Equal((SheetLayout.Grid, GridOrder.ByPart, true), (back.Layout, back.Order, back.PowerOfTwo));
    }
}
