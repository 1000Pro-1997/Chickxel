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
        Assert.True(p.HasHiddenSkinContent(head)); // 모자 셀은 비어 있고 첫 스킨 빨강은 가려짐
        Paint(p, head, 0, 1, 1, Blue);
        Assert.True(p.HasHiddenSkinContent(head));
        var outfit = p.SaveOutfit("모자 착장");
        Assert.Contains(outfit.Parts, e => e.Part == head);

        p.MoveOutOfGroup([head], 2); // 최상위: [레이어 1, 그룹, head]
        Assert.Equal([p.Layers[0], arm, g, head], p.Layers);
        Assert.Equal(Blue, p.GetCel(head, p.Frames[0])![2, 1]); // 모자 그림이 캔버스 같은 자리에
        Assert.Equal(Color32.Transparent, p.GetCel(head, p.Frames[0])![1, 0]); // 첫 스킨 빨강은 가져가지 않음
        Assert.DoesNotContain(hat, g.AllSkins);
        Assert.Empty(outfit.Parts);

        p.Undo();
        Assert.True(head.IsPart);
        Assert.Contains(hat, g.AllSkins);
        Assert.Same(hat, head.SkinOverride);
        Assert.Contains(outfit.Parts, e => e.Part == head);
        Assert.Equal(Red, p.GetOwnCel(head, p.Frames[0], g.BaseSkin!)![0, 0]);
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
        Paint(p, x, 0, 1, 1, Blue);          // winter 셀: 파랑만(빈 셀에서 시작)
        p.SetActiveSkin(g1, g1.BaseSkin!);
        Paint(p, x, 0, 0, 1, Green);         // 첫 스킨에만 초록
        p.SetActiveSkin(g1, winter);
        Assert.True(p.HasHiddenSkinContent(x));

        p.MoveIntoGroup([x], g2, 1); // Low와 High 사이
        Assert.Equal([low, x, high], p.ChildrenOf(g2));
        Assert.Empty(p.ChildrenOf(g1));
        Assert.Equal(new Rectangle(0, 0, 2, 2), x.PartRect);
        var cel = p.GetOwnCel(x, p.Frames[0], g2.BaseSkin!)!;
        Assert.Equal((default(Color32), Blue, default(Color32)), (cel[0, 0], cel[1, 1], cel[0, 1])); // 보이던 winter 그림만 첫 스킨으로
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

    [Fact]
    public void AddSkinToLayer_WrapsTheLayerInAGroupAndAddsASkin_OneUndoStep()
    {
        var p = new SpriteProject(6, 4);
        var below = p.Layers[0];
        var layer = p.AddLayer(name: "몸");
        var above = p.AddLayer(name: "위");
        Paint(p, layer, 0, 2, 1, Red);
        var flat = p.Flatten(0);
        int undoBefore = p.History.UndoCount;

        var g = p.AddSkinToLayer(layer, copy: false);
        Assert.True(g.IsGroup);
        Assert.Same(g, layer.Parent);
        Assert.Equal([below, layer, g, above], p.Layers);
        Assert.Equal(2, g.Skins.Count);
        Assert.Same(g.Skins[1], g.ActiveSkin);
        // 빈 새 스킨은 빈 칸(첫 스킨 그림을 빌리지 않음), 지금 그림은 첫 스킨에
        Assert.Equal(Color32.Transparent, p.Flatten(0)[2, 1]);
        Assert.Equal(Red, p.GetOwnCel(layer, p.Frames[0], g.BaseSkin!)![2 - layer.PartOffset.X, 1 - layer.PartOffset.Y]);
        Assert.Equal(undoBefore + 1, p.History.UndoCount);

        p.Undo();
        Assert.False(layer.IsPart);
        Assert.Equal([below, layer, above], p.Layers);
        Assert.True(p.Flatten(0).ContentEquals(flat));

        // 복제: 새 스킨이 지금 그림을 자기 셀로 가짐
        var g2 = p.AddSkinToLayer(layer, copy: true);
        Assert.Equal(Red, p.GetOwnCel(layer, p.Frames[0], g2.ActiveSkin!)![2 - layer.PartOffset.X, 1 - layer.PartOffset.Y]);

        // 잠긴 레이어는 바꾸지 않는다
        above.Locked = true;
        Assert.Throws<InvalidOperationException>(() => p.AddSkinToLayer(above, copy: false));
        Assert.False(above.IsPart);
    }
}
