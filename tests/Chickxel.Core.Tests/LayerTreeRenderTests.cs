using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>레이어 나무 합성: 자식이 부모 자신의 그림 위, 자손까지 효과·불투명도는 덩어리 합성, 숨긴 조상, 그리는 차례.</summary>
public class LayerTreeRenderTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);
    private static readonly Color32 Black = new(0, 0, 0);

    private static PixelImage Filled(int w, int h, Color32 c)
    {
        var img = new PixelImage(w, h);
        img.Clear(c);
        return img;
    }

    private static int Count(PixelImage image, Color32 c) => image.Pixels.Count(x => x == c);

    /// <summary>8×8: 몸(4×4 빨강 @ (2,2)) 아래 팔(2×2 파랑 @ (3,3) — 몸과 겹침) 아래 손(1×1 초록 @ (4,4) — 팔과 겹침).</summary>
    private static (SpriteProject P, Layer Body, Layer Arm, Layer Hand) Rig()
    {
        var p = new SpriteProject(8, 8);
        var body = p.AddPart(null, "몸", 4, 4, new Point(2, 2));
        var arm = p.AddPart(body, "팔", 2, 2, new Point(3, 3));
        var hand = p.AddPart(arm, "손", 1, 1, new Point(4, 4));
        p.SetCel(p.IndexOf(body), 0, Filled(4, 4, Red));
        p.SetCel(p.IndexOf(arm), 0, Filled(2, 2, Blue));
        p.SetCel(p.IndexOf(hand), 0, Filled(1, 1, Green));
        return (p, body, arm, hand);
    }

    [Fact]
    public void Children_DrawAboveParentsOwnPicture_DespiteListOrder()
    {
        var (p, body, arm, hand) = Rig();
        // 목록은 자식이 부모 아래(손, 팔, 몸 순) — 그리는 차례는 부모 자신의 그림이 아래
        Assert.Equal(["레이어 1", "손", "팔", "몸"], p.Layers.Select(l => l.Name));
        Assert.Equal(["레이어 1", "몸", "팔", "손"], p.DrawOrder().Select(l => l.Name));
        var flat = p.Flatten(0);
        Assert.Equal(Red, flat[2, 2]);
        Assert.Equal(Blue, flat[3, 3]);
        Assert.Equal(Green, flat[4, 4]);
        Assert.True(p.FlattenSubtree(arm, 0).ContentEquals(p.PlanSubtree(arm, 0).Render()));
        Assert.Equal(Color32.Transparent, p.FlattenSubtree(arm, 0)[2, 2]);

        // 형제끼리는 목록 순서(아래 형제 덩어리 → 위 형제 덩어리)
        var other = p.AddPart(body, "다른 팔", 2, 2, new Point(4, 4)); // 맨 위 자식 → 팔 덩어리(손 포함) 위
        p.SetCel(p.IndexOf(other), 0, Filled(2, 2, Black));
        Assert.Equal(Black, p.Flatten(0)[4, 4]);
        p.MoveLayerDown(p.IndexOf(other)); // 팔 덩어리 아래로
        Assert.Equal(Green, p.Flatten(0)[4, 4]);
        PartsTests.AssertHierarchy(p);
    }

    [Fact]
    public void IncludeChildrenEffect_WrapsTheWholeBlock_OwnEffect_OnlyTheOwnCel()
    {
        var (p, body, arm, _) = Rig();
        p.SetCel(p.IndexOf(body), 0, null); // 몸 자신의 그림 없음: 덩어리 = 팔 + 손
        p.SetLayerEffects(p.IndexOf(body), [new OutlineEffect { Color = Black, IncludeChildren = true }]);
        // 팔(2×2 @ (3,3)) 둘레 4방향 외곽선: 8칸, 손은 팔 안이라 선이 없다
        Assert.Equal(8, Count(p.Flatten(0), Black));
        Assert.Equal(Green, p.Flatten(0)[4, 4]);
        Assert.True(p.FlattenSubtree(body, 0).ContentEquals(p.Flatten(0)));
        // 자손까지 효과의 원본은 효과 없이 덩어리, 자기 셀 원본은 자기 셀
        Assert.Equal(0, Count(p.SubtreeEffectSource(body, 0)!, Black));
        Assert.Null(p.EffectSource(body, 0));
        Assert.Equal(body.Effects, p.SubtreeEffectsOf(body));
        Assert.Empty(p.OwnEffectsOf(body));

        // 이 레이어만: 자기 셀이 없으니 아무것도 그리지 않는다
        p.SetLayerEffects(p.IndexOf(body), [new OutlineEffect { Color = Black }]);
        Assert.Equal(0, Count(p.Flatten(0), Black));
        Assert.Equal(body.Effects, p.OwnEffectsOf(body));

        // 자식이 없는 레이어의 자손까지 효과는 자기 셀에(값과 관계없이)
        p.SetLayerEffects(p.IndexOf(body), []);
        p.SetLayerEffects(p.IndexOf(arm), []);
        var leaf = p.Layers.Single(l => l.Name == "손");
        p.SetLayerEffects(p.IndexOf(leaf), [new OutlineEffect { Color = Black, IncludeChildren = true }]);
        Assert.Empty(p.SubtreeEffectsOf(leaf));
        Assert.Equal(leaf.Effects, p.OwnEffectsOf(leaf));
        Assert.True(Count(p.Flatten(0), Black) > 0);
    }

    [Fact]
    public void ParentOpacity_AppliesOnceToTheBlockComposite()
    {
        var (p, body, arm, hand) = Rig();
        p.SetLayerProperties(p.IndexOf(body), opacity: 128);
        var flat = p.Flatten(0);
        // 덩어리를 먼저 합친 뒤 한 번: 겹친 자리에서도 위 그림 색 그대로(아래 빨강이 비치지 않음)
        Assert.Equal(Blue.WithAlpha(128), flat[3, 3]);
        Assert.Equal(Green.WithAlpha(128), flat[4, 4]);
        Assert.Equal(Red.WithAlpha(128), flat[2, 2]);
        // 자식 불투명도는 그 자식 덩어리에
        p.SetLayerProperties(p.IndexOf(body), opacity: 255);
        p.SetLayerProperties(p.IndexOf(arm), opacity: 0);
        Assert.Equal(Red, p.Flatten(0)[4, 4]); // 팔 덩어리(손 포함)가 통째로 투명
        Assert.Equal(Red, p.Flatten(0)[3, 3]);
        _ = hand;
    }

    [Fact]
    public void HiddenAncestor_HidesSubtree_ChildHiddenOnlyHidesItsBlock()
    {
        var (p, body, arm, _) = Rig();
        p.SetLayerProperties(p.IndexOf(arm), visible: false);
        var flat = p.Flatten(0);
        Assert.Equal(Red, flat[3, 3]);
        Assert.Equal(Red, flat[4, 4]); // 손도 같이 숨음
        p.SetLayerProperties(p.IndexOf(arm), visible: true);
        p.SetLayerProperties(p.IndexOf(body), visible: false);
        Assert.True(p.Flatten(0).IsRegionEmpty(new Rectangle(0, 0, 8, 8)));
        Assert.Equal(Green, p.Flatten(0, includeHidden: true)[4, 4]);
        // 빼기(내보내기 착장 합성)도 자손째
        p.SetLayerProperties(p.IndexOf(body), visible: true);
        Assert.Equal(Red, p.FlattenDressed(0, null, [arm])[4, 4]);
        // 걸러내기는 자기 그림만
        Assert.Equal(Green, p.PlanFrame(0, layerFilter: l => l != arm).Render()[4, 4]);
        Assert.Equal(Red, p.PlanFrame(0, layerFilter: l => l != arm).Render()[3, 3]);
    }

    [Fact]
    public void RawPlan_AndCollapsedThumbnail_FollowDrawOrder()
    {
        var (p, body, _, _) = Rig();
        p.SetLayerEffects(p.IndexOf(body), [new OutlineEffect { Color = Black, IncludeChildren = true }]);
        var raw = p.PlanFrameRaw(0).Render();
        Assert.Equal(0, Count(raw, Black));
        Assert.Equal((Blue, Green), (raw[3, 3], raw[4, 4]));
        var thumb = p.RenderCelThumbnail(p.IndexOf(body), 0, withChildren: true)!;
        Assert.Equal(new Rectangle(2, 2, 4, 4), p.ThumbnailBounds(p.IndexOf(body), withChildren: true));
        Assert.Equal((Red, Blue, Green), (thumb[0, 0], thumb[1, 1], thumb[2, 2]));
        Assert.Equal(new Size(4, 4), p.RenderCelThumbnail(p.IndexOf(body), 0)!.Size); // 자기 그림만
    }

    [Fact]
    public void PartInFrame_WrapsTheLayerWithEveryActiveAncestorSubtreeEffect()
    {
        var (p, body, arm, hand) = Rig();
        p.SetLayerEffects(p.IndexOf(body), [new OutlineEffect { Color = Black, IncludeChildren = true }]);
        var alone = p.PlanPartInFrame(hand, 0).Render();
        Assert.Equal(Green, alone[4, 4]);
        Assert.Equal(4, Count(alone, Black)); // 손 하나만 든 덩어리에 조상(몸)의 외곽선
        p.SetLayerEffects(p.IndexOf(body), []);
        Assert.Equal(0, Count(p.PlanPartInFrame(hand, 0).Render(), Black));
        _ = arm;
    }
}
