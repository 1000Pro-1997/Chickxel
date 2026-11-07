using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>효과 하나의 '캔버스 늘리기…': 그 효과까지의 결과 범위를 재고, 고른 방식으로 캔버스를 늘린다.</summary>
public class EffectCanvasTests
{
    private static readonly Color32 Red = new(255, 0, 0);

    [Fact]
    public void ThroughEffect_MeasuresUpToThatEffect_AndApplyGrowsCanvasInOneUndoStep()
    {
        var p = new SpriteProject(4, 4);
        var cel = p.BeginCelStroke(0, 0);
        cel.Set(3, 1, Red);
        Assert.True(p.CommitCelStroke("연필"));
        // 0번: 오른쪽 2칸 그림자(캔버스 밖으로 나감), 1번: 아래 3칸 그림자(캔버스 늘리기를 꺼 둠).
        p.SetLayerEffects(0, [new ShadowEffect { OffsetX = 2, OffsetY = 0 }, new ShadowEffect { OffsetX = 0, OffsetY = 3, ExpandCanvas = false }]);
        var layer = p.Layers[0];

        // 0번까지: x 3..5. 1번까지(늘리기를 끈 효과도 이 단추에서는 잼): 아래로도 나감.
        Assert.Equal(new Rectangle(3, 1, 3, 1), p.EffectOutputBounds(layer, 0, expandingOnly: false));
        Assert.Equal(new Rectangle(3, 1, 3, 4), p.EffectOutputBounds(layer, 1, expandingOnly: false));
        // 확인 때처럼 늘리기를 끈 효과를 빼면 0번 결과와 같다.
        Assert.Equal(new Rectangle(3, 1, 3, 1), p.EffectOutputBounds(layer));

        var plan = p.PlanEffectCanvas(p.EffectOutputBounds(layer, 0, expandingOnly: false)!.Value, EffectCanvasExpandMode.Minimum);
        Assert.Equal((6, 4, 0, 0), (plan.Width, plan.Height, plan.OffsetX, plan.OffsetY));
        int steps = p.History.UndoCount;
        Assert.True(p.ApplyEffectCanvas(plan));
        Assert.Equal((6, 4), (p.Width, p.Height));
        Assert.Equal(steps + 1, p.History.UndoCount);
        Assert.False(p.ApplyEffectCanvas(p.PlanEffectCanvas(new Rectangle(0, 0, 1, 1), EffectCanvasExpandMode.Minimum)));
        p.Undo();
        Assert.Equal((4, 4), (p.Width, p.Height));
    }

    [Fact]
    public void NoEnabledEffect_ReturnsNull()
    {
        var p = new SpriteProject(4, 4);
        p.SetLayerEffects(0, [new ShadowEffect { Enabled = false }]);
        Assert.Null(p.EffectOutputBounds(p.Layers[0], 0, expandingOnly: false));
    }
}
