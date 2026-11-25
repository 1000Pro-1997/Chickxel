using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>레이어 나무에 넣기·꺼내기: 일반 → 자리 있는 레이어 바꾸기(그림 자리 유지, 캔버스 밖 포함), 링크, 스킨 유지, 자리, 잠금, 고리 거부, 실행취소.</summary>
public class LayerNestTests
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
    public void NormalLayer_IntoParent_BecomesPartInPlace_OutStaysPart()
    {
        var p = new SpriteProject(8, 6);
        p.AddFrame();
        var layer = p.Layers[0];
        Paint(p, layer, 0, 0, 0, Red);
        Paint(p, layer, 0, 2, 1, Blue);
        p.ShiftCels("옮기기", [(0, 0)], -1, 0); // 빨강 (−1, 0)은 캔버스 밖, 파랑 (1, 1)
        p.LinkCels([(0, 0), (0, 1)]);
        Assert.Equal(Red, FullAt(p, layer, 0, -1, 0));
        var g = p.AddEmptyParent("G");
        var a = p.AddPart(g, "A", 2, 2, new Point(0, 0));
        var flat = p.Flatten(0);
        int undo = p.History.UndoCount;

        p.MoveLayersTo([layer], g, 1);
        Assert.Equal(undo + 1, p.History.UndoCount); // 바꾸기와 넣기가 한 단계
        Assert.True(layer.IsPart);
        Assert.Same(g, layer.Parent);
        Assert.Equal([a, layer], p.ChildrenOf(g));
        Assert.Equal(new Rectangle(-1, 0, 3, 2), layer.PartRect); // 모든 프레임 그림(캔버스 밖 포함)을 감쌈
        Assert.Equal((Red, Blue), (p.GetCel(layer, p.Frames[0])![0, 0], p.GetCel(layer, p.Frames[0])![2, 1]));
        Assert.True(p.AreCelsLinked(p.IndexOf(layer), 0, 1));
        Assert.True(p.Flatten(0).ContentEquals(flat));
        PartsTests.AssertHierarchy(p);

        // 꺼내도 자리 있는 레이어 그대로(보이는 그림이 같으므로 되돌리지 않음)
        p.MoveLayersTo([layer], null, 0);
        Assert.True(layer.IsPart);
        Assert.Null(layer.Parent);
        Assert.Equal(0, p.IndexOf(layer));
        Assert.Equal(new Rectangle(-1, 0, 3, 2), layer.PartRect);
        Assert.True(p.Flatten(0).ContentEquals(flat));

        p.Undo();
        Assert.Same(g, layer.Parent);
        p.Undo();
        Assert.False(layer.IsPart);
        Assert.Null(layer.Parent);
        Assert.Equal(Red, FullAt(p, layer, 0, -1, 0));
        Assert.DoesNotContain(layer, p.ChildrenOf(g));
    }

    [Fact]
    public void NormalParent_ReceivingChild_IsConvertedInPlace_SameUndoStep()
    {
        var p = new SpriteProject(6, 6);
        var body = p.Layers[0];
        Paint(p, body, 0, 2, 3, Red);
        var arm = p.AddLayer(name: "팔");
        Paint(p, arm, 0, 4, 4, Blue);
        var flat = p.Flatten(0);
        int undo = p.History.UndoCount;

        p.MoveLayersTo([arm], body, 0);
        Assert.Equal(undo + 1, p.History.UndoCount);
        Assert.True(body.IsPart && arm.IsPart);
        Assert.Equal(new Rectangle(2, 3, 1, 1), body.PartRect);
        Assert.Equal(new Rectangle(4, 4, 1, 1), arm.PartRect);
        Assert.Equal([arm, body], p.Layers);
        Assert.True(p.Flatten(0).ContentEquals(flat));

        // 자식 넣기(AddPart)도 일반 부모를 같은 단계에서 바꾼다
        var q = new SpriteProject(4, 4);
        var top = q.Layers[0];
        int qu = q.History.UndoCount;
        var child = q.AddPart(top, "c", 1, 1);
        Assert.Equal(qu + 1, q.History.UndoCount);
        Assert.True(top.IsPart);
        Assert.Equal(new Rectangle(0, 0, 4, 4), top.PartRect); // 빈 레이어는 캔버스 크기
        Assert.Same(top, child.Parent);
        q.Undo();
        Assert.False(top.IsPart);
    }

    [Fact]
    public void Part_IntoOtherParent_KeepsAllSkins_AndSlotOrders()
    {
        var p = new SpriteProject(4, 4);
        var g1 = p.AddEmptyParent("g1");
        var x = p.AddPart(g1, "X", 2, 2, new Point(0, 0));
        Paint(p, x, 0, 0, 0, Red);
        var winter = p.AddSkin(x, "winter");
        Paint(p, x, 0, 1, 1, Blue);
        var g2 = p.AddEmptyParent("g2");
        var low = p.AddPart(g2, "Low", 1, 1);
        var high = p.AddPart(g2, "High", 1, 1);

        p.MoveLayersTo([x], g2, 1); // Low와 High 사이
        Assert.Equal([low, x, high], p.ChildrenOf(g2));
        Assert.Empty(p.ChildrenOf(g1));
        Assert.Equal(new Rectangle(0, 0, 2, 2), x.PartRect);
        Assert.Equal([x.Skins[0], winter], x.Skins);
        Assert.Same(winter, x.ActiveSkin);
        Assert.Equal(Red, p.GetOwnCel(x, p.Frames[0], x.BaseSkin!)![0, 0]);
        Assert.Equal(Blue, p.GetOwnCel(x, p.Frames[0], winter)![1, 1]);
        PartsTests.AssertHierarchy(p);
    }

    [Fact]
    public void MoveLayersTo_CarriesSubtree_MultiLevel_AndMoveOutOfParentGoesOneLevelUp()
    {
        var p = new SpriteProject(8, 8);
        var root = p.AddEmptyParent("root");
        var body = p.AddPart(root, "body", 4, 4, new Point(2, 2));
        var arm = p.AddPart(body, "arm", 2, 2, new Point(5, 3));
        var hand = p.AddPart(arm, "hand", 1, 1, new Point(6, 4));
        var other = p.AddEmptyParent("other");
        Assert.Equal(["레이어 1", "hand", "arm", "body", "root", "other"], p.Layers.Select(l => l.Name));
        Assert.Equal(3, p.DepthOf(hand));
        Assert.Equal([arm, body, root], p.AncestorsOf(hand));
        PartsTests.AssertHierarchy(p);

        // 자손째 다른 부모로(고른 것 중 자손은 조상과 함께)
        p.MoveLayersTo([arm, hand], other, 0);
        Assert.Equal(["레이어 1", "body", "root", "hand", "arm", "other"], p.Layers.Select(l => l.Name));
        Assert.Same(other, arm.Parent);
        Assert.Same(arm, hand.Parent);
        PartsTests.AssertHierarchy(p);

        // 한 단계 바깥으로: 원래 부모 바로 위
        p.MoveOutOfParent([hand]);
        Assert.Same(other, hand.Parent);
        Assert.Equal(["레이어 1", "body", "root", "arm", "hand", "other"], p.Layers.Select(l => l.Name));
        p.MoveOutOfParent([hand]);
        Assert.Null(hand.Parent);
        Assert.Equal(["레이어 1", "body", "root", "arm", "other", "hand"], p.Layers.Select(l => l.Name));
        PartsTests.AssertHierarchy(p);
    }

    [Fact]
    public void Nest_RejectsCyclesTilemapsAndLockedLayers()
    {
        var p = new SpriteProject(4, 4);
        var layer = p.Layers[0];
        var g = p.AddEmptyParent("G");
        var part = p.AddPart(g, "A", 1, 1);
        var tiles = p.AddTilemapLayer(Chickxel.Core.Tiles.Tileset.Create("t", 2, 2));
        int undo = p.History.UndoCount;

        Assert.Throws<ArgumentException>(() => p.MoveLayersTo([g], part, 0));    // 자기 자손 밑으로
        Assert.Throws<ArgumentException>(() => p.MoveLayersTo([g], g, 0));       // 자기 밑으로
        Assert.Throws<InvalidOperationException>(() => p.MoveLayersTo([tiles], g, 0));
        Assert.Throws<InvalidOperationException>(() => p.MoveLayersTo([layer], tiles, 0));
        Assert.Throws<InvalidOperationException>(() => p.AddPart(tiles, "x", 1, 1));
        layer.Locked = true;
        Assert.Throws<InvalidOperationException>(() => p.MoveLayersTo([layer], g, 0));
        layer.Locked = false;
        g.Locked = true;
        Assert.Throws<InvalidOperationException>(() => p.MoveLayersTo([layer], g, 0));
        Assert.Throws<InvalidOperationException>(() => p.MoveOutOfParent([part])); // 부모가 잠기면 자식도 잠김
        Assert.Equal(undo, p.History.UndoCount);
    }

    [Fact]
    public void MovingParent_MovesWholeSubtree_BaseAndFromFrame()
    {
        var p = new SpriteProject(10, 10);
        p.AddFrame();
        var body = p.AddPart(null, "body", 4, 4, new Point(1, 1));
        var arm = p.AddPart(body, "arm", 2, 2, new Point(4, 2));
        var hand = p.AddPart(arm, "hand", 1, 1, new Point(5, 3));
        Paint(p, hand, 0, 0, 0, Red);
        Paint(p, hand, 1, 0, 0, Red);

        p.SetPartOffset(p.IndexOf(body), new Point(3, 2)); // (+2, +1)
        Assert.Equal((new Point(3, 2), new Point(6, 3), new Point(7, 4)), (body.PartOffset, arm.PartOffset, hand.PartOffset));
        Assert.Equal(Red, p.Flatten(0)[7, 4]);

        // 한 프레임부터: 자손도 그 프레임부터 같은 거리
        p.MovePartFrom(p.IndexOf(arm), 1, 0, 2);
        Assert.Equal((new Point(6, 3), new Point(6, 5)), (p.GetPartOffset(arm, 0), p.GetPartOffset(arm, 1)));
        Assert.Equal((new Point(7, 4), new Point(7, 6)), (p.GetPartOffset(hand, 0), p.GetPartOffset(hand, 1)));
        Assert.Equal(new Point(3, 2), p.GetPartOffset(body, 1)); // 부모는 그대로
        Assert.Equal(Red, p.Flatten(1)[7, 6]);

        // 그 프레임 자리를 되돌리면 자손도 되돌아감
        p.SetLayerFramesFrom(p.IndexOf(arm), 1, own: false);
        Assert.Equal(new Point(7, 4), p.GetPartOffset(hand, 1));

        // 크기 바꾸기는 자식을 옮기지 않는다
        p.ResizePart(p.IndexOf(body), 8, 8, Chickxel.Core.Model.Anchor.BottomRight);
        Assert.Equal(new Point(6, 3), arm.PartOffset);

        p.Undo(); p.Undo(); p.Undo(); p.Undo();
        Assert.Equal((new Point(1, 1), new Point(4, 2), new Point(5, 3)), (body.PartOffset, arm.PartOffset, hand.PartOffset));
    }

    [Fact]
    public void HiddenOrLockedAncestor_HidesAndLocksDescendants()
    {
        var p = new SpriteProject(4, 4);
        var root = p.AddEmptyParent("root");
        var mid = p.AddPart(root, "mid", 4, 4, Point.Empty);
        var leaf = p.AddPart(mid, "leaf", 1, 1, new Point(1, 1));
        Paint(p, leaf, 0, 0, 0, Red);
        Assert.Equal(Red, p.Flatten(0)[1, 1]);

        p.SetLayerProperties(p.IndexOf(root), visible: false);
        Assert.False(p.IsLayerVisible(leaf));
        Assert.Equal(Color32.Transparent, p.Flatten(0)[1, 1]);
        Assert.Equal(Red, p.Flatten(0, includeHidden: true)[1, 1]);
        p.SetLayerProperties(p.IndexOf(root), visible: true, locked: true);
        Assert.True(p.IsLayerLocked(leaf));
        Assert.Throws<InvalidOperationException>(() => p.BeginCelStroke(p.IndexOf(leaf), 0));
    }

    [Fact]
    public void DuplicateDeleteAndAutoLayout_WorkOnSubtrees()
    {
        var p = new SpriteProject(12, 4);
        var root = p.AddEmptyParent("root");
        var a = p.AddPart(root, "a", 2, 2, new Point(0, 0));
        var aChild = p.AddPart(a, "a.child", 1, 1, new Point(1, 1));
        var b = p.AddPart(root, "b", 3, 2, new Point(5, 0));
        var skin = p.AddSkin(a, "alt");

        var copy = p.DuplicateLayer(p.IndexOf(root));
        Assert.Equal(3, p.DescendantCount(copy));
        var aCopy = p.ChildrenOf(copy)[0];
        Assert.Equal(["기본", "alt"], aCopy.Skins.Select(s => s.Name));
        Assert.DoesNotContain(skin, aCopy.Skins);
        Assert.Equal("alt", aCopy.ActiveSkin!.Name);
        PartsTests.AssertHierarchy(p);
        p.DeleteLayer(p.IndexOf(copy));
        Assert.Equal(["레이어 1", "a.child", "a", "b", "root"], p.Layers.Select(l => l.Name));

        // 자동 배치: 바로 아래 자식을 자손째 옮긴다
        p.SetChildLayout(root, new GroupLayout { Direction = GroupLayoutDirection.Row, Gap = 1 });
        Assert.Equal(new Point(a.PartOffset.X + 1, a.PartOffset.Y + 1), aChild.PartOffset);
        Assert.True(a.IsAutoLaidOut && b.IsAutoLaidOut && !aChild.IsAutoLaidOut);
        Assert.Throws<InvalidOperationException>(() => p.SetPartOffset(p.IndexOf(a), new Point(9, 9)));
        Assert.Throws<ArgumentException>(() => p.SetChildLayout(p.Layers[0], null)); // 일반 레이어
    }
}
