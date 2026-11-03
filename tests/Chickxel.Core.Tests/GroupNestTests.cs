using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>그룹에 넣기·꺼내기: 그림 자리 유지(캔버스 밖 포함), 링크, 스킨·착장 정리, 자리, 잠금, 실행취소.</summary>
public class GroupNestTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);

    private static void Paint(SpriteProject p, Layer layer, int frame, int x, int y, Color32 c)
    {
        p.BeginCelStroke(p.IndexOf(layer), frame).Set(x, y, c);
        Assert.True(p.CommitCelStroke("점"));
    }

    /// <summary>캔버스 좌표 (x, y)의 일반 레이어 셀 색(캔버스 밖 그림 포함).</summary>
    private static Color32 FullAt(SpriteProject p, Layer layer, int frame, int x, int y)
    {
        var (image, origin) = p.GetFullCel(p.IndexOf(layer), frame);
        return image.Contains(x - origin.X, y - origin.Y) ? image[x - origin.X, y - origin.Y] : default;
    }

    [Fact]
    public void NormalLayer_IntoGroupAndBack_KeepsPixelsInPlaceIncludingOffCanvas()
    {
        var p = new SpriteProject(8, 6);
        p.AddFrame();
        var layer = p.Layers[0];
        Paint(p, layer, 0, 0, 0, Red);
        Paint(p, layer, 0, 2, 1, Blue);
        p.ShiftCels("옮기기", [(0, 0)], -1, 0); // 빨강 (−1, 0)은 캔버스 밖, 파랑 (1, 1)
        p.LinkCels([(0, 0), (0, 1)]);
        var g = p.AddGroup("G");
        var a = p.AddPart(g, "A", 2, 2, new Point(0, 0));
        var flat = p.Flatten(0);

        p.MoveIntoGroup([layer], g, 1);
        Assert.True(layer.IsPart);
        Assert.Same(g, layer.Parent);
        Assert.Equal([a, layer], p.ChildrenOf(g));
        Assert.Equal(new Rectangle(-1, 0, 3, 2), layer.PartRect); // 모든 프레임 그림(캔버스 밖 포함)을 감쌈
        Assert.Equal((Red, Blue), (p.GetCel(layer, p.Frames[0])![0, 0], p.GetCel(layer, p.Frames[0])![2, 1]));
        Assert.True(p.AreCelsLinked(p.IndexOf(layer), 0, 1));
        Assert.True(p.Flatten(0).ContentEquals(flat));

        p.MoveOutOfGroup([layer], 0);
        Assert.False(layer.IsPart);
        Assert.Null(layer.Parent);
        Assert.Equal(0, p.IndexOf(layer));
        Assert.Equal((Red, Blue), (FullAt(p, layer, 0, -1, 0), FullAt(p, layer, 0, 1, 1)));
        Assert.True(p.AreCelsLinked(0, 0, 1));
        Assert.True(p.Flatten(0).ContentEquals(flat));

        p.Undo();
        Assert.Same(g, layer.Parent);
        p.Undo();
        Assert.Null(layer.Parent);
        Assert.Equal(Red, FullAt(p, layer, 0, -1, 0));
        Assert.DoesNotContain(layer, p.ChildrenOf(g));
    }

    [Fact]
    public void Part_Out_KeepsShownSkinOnly_DropsOwnSkinAndOutfitEntry_UndoRestores()
    {
        var p = new SpriteProject(4, 4);
        var g = p.AddGroup("body");
        var arm = p.AddPart(g, "Arm", 2, 2, new Point(2, 2));
        var head = p.AddPart(g, "Head", 2, 2, new Point(1, 0));
        Paint(p, head, 0, 0, 0, Red);   // 첫 스킨
        Paint(p, arm, 0, 0, 0, Red);
        Assert.False(p.HasHiddenSkinContent(head));
        var hat = p.AddPartSkin(head, "모자"); // 입혀짐(재정의)
        Assert.False(p.HasHiddenSkinContent(head)); // 모자 셀이 비어 첫 스킨을 물려받음
        Paint(p, head, 0, 1, 1, Blue);
        Assert.True(p.HasHiddenSkinContent(head)); // 첫 스킨 빨강이 가려짐
        var outfit = p.SaveOutfit("모자 착장");
        Assert.Contains(outfit.Parts, e => e.Part == head);

        p.MoveOutOfGroup([head], 2); // 최상위: [레이어 1, 그룹, head]
        Assert.Equal([p.Layers[0], arm, g, head], p.Layers);
        Assert.Equal(Blue, p.GetCel(head, p.Frames[0])![2, 1]); // 모자 그림(그릴 때 물려받은 빨강 포함)이 캔버스 같은 자리에
        Assert.Equal(Red, p.GetCel(head, p.Frames[0])![1, 0]);
        Assert.DoesNotContain(hat, g.AllSkins);
        Assert.Empty(outfit.Parts);

        p.Undo();
        Assert.True(head.IsPart);
        Assert.Contains(hat, g.AllSkins);
        Assert.Same(hat, head.SkinOverride);
        Assert.Contains(outfit.Parts, e => e.Part == head);
        Assert.Equal(Red, p.GetSkinCel(head, p.Frames[0], g.BaseSkin!)![0, 0]);
    }

    [Fact]
    public void Part_IntoOtherGroup_TakesShownSkinAsBase_AndSlotOrders()
    {
        var p = new SpriteProject(4, 4);
        var g1 = p.AddGroup("g1");
        var x = p.AddPart(g1, "X", 2, 2, new Point(0, 0));
        Paint(p, x, 0, 0, 0, Red);
        var g2 = p.AddGroup("g2");
        var low = p.AddPart(g2, "Low", 1, 1);
        var high = p.AddPart(g2, "High", 1, 1);
        var winter = p.AddSkin(g1, "winter");
        p.SetActiveSkin(g1, winter);
        Paint(p, x, 0, 1, 1, Blue);          // winter 셀: 물려받은 빨강 + 파랑
        p.SetActiveSkin(g1, g1.BaseSkin!);
        Paint(p, x, 0, 0, 1, Green);         // 첫 스킨에만 초록
        p.SetActiveSkin(g1, winter);
        Assert.True(p.HasHiddenSkinContent(x));

        p.MoveIntoGroup([x], g2, 1); // Low와 High 사이
        Assert.Equal([low, x, high], p.ChildrenOf(g2));
        Assert.Empty(p.ChildrenOf(g1));
        Assert.Equal(new Rectangle(0, 0, 2, 2), x.PartRect);
        var cel = p.GetSkinCel(x, p.Frames[0], g2.BaseSkin!)!;
        Assert.Equal((Red, Blue, default(Color32)), (cel[0, 0], cel[1, 1], cel[0, 1])); // 보이던 winter 그림만 첫 스킨으로
    }

    [Fact]
    public void Nest_RejectsGroupsLockedLayersAndSameGroup()
    {
        var p = new SpriteProject(4, 4);
        var layer = p.Layers[0];
        var g = p.AddGroup("G");
        var part = p.AddPart(g, "A", 1, 1);
        var other = p.AddGroup("H");
        int undo = p.History.UndoCount;

        Assert.Throws<InvalidOperationException>(() => p.MoveIntoGroup([other], g, 0));
        Assert.Throws<ArgumentException>(() => p.MoveIntoGroup([part], g, 0));
        Assert.Throws<ArgumentException>(() => p.MoveOutOfGroup([layer], 0));
        layer.Locked = true;
        Assert.Throws<InvalidOperationException>(() => p.MoveIntoGroup([layer], g, 0));
        layer.Locked = false;
        g.Locked = true;
        Assert.Throws<InvalidOperationException>(() => p.MoveIntoGroup([layer], g, 0));
        Assert.Throws<InvalidOperationException>(() => p.MoveOutOfGroup([part], 0)); // 그룹이 잠기면 부위도 잠김
        Assert.Equal(undo, p.History.UndoCount);
    }

    [Fact]
    public void EmptyLayer_BecomesCanvasSizedPart()
    {
        var p = new SpriteProject(5, 3);
        var layer = p.Layers[0];
        var g = p.AddGroup("G");
        p.MoveIntoGroup([layer], g, 0);
        Assert.Equal(new Rectangle(0, 0, 5, 3), layer.PartRect);
    }
}
