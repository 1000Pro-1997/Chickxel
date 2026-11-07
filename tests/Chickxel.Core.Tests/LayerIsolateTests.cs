using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>타임라인 Alt+눈·자물쇠: 레이어 하나만 남기고 숨기기·잠그기, 다시 누르면 모두 보이기·풀기.</summary>
public class LayerIsolateTests
{
    [Fact]
    public void AltEye_HidesOthers_ThenShowsAll_EachOneUndoStep()
    {
        var p = new SpriteProject(4, 4);
        p.AddLayer();
        p.AddLayer();
        p.SetLayerProperties(1, visible: false);
        int steps = p.History.UndoCount;

        Assert.True(p.IsolateLayer(1, lockToggle: false));
        Assert.Equal([false, true, false], p.Layers.Select(l => l.Visible));
        Assert.Equal(steps + 1, p.History.UndoCount);

        Assert.False(p.IsolateLayer(1, lockToggle: false));
        Assert.All(p.Layers, l => Assert.True(l.Visible));

        p.Undo();
        Assert.Equal([false, true, false], p.Layers.Select(l => l.Visible));
        // 다른 레이어에서 누르면 그 레이어만 남긴다(이미 혼자 보이는 상태는 그 레이어 기준).
        Assert.True(p.IsolateLayer(2, lockToggle: false));
        Assert.Equal([false, false, true], p.Layers.Select(l => l.Visible));
    }

    [Fact]
    public void AltLock_LocksOthers_KeepsGroupAndItsParts_ThenUnlocksAll()
    {
        var p = new SpriteProject(8, 8);
        var group = p.AddGroup();
        var arm = p.AddPart(group, "팔", 2, 2, new System.Drawing.Point(0, 0));
        var leg = p.AddPart(group, "다리", 2, 2, new System.Drawing.Point(2, 2));
        var normal = p.Layers[0];

        // 부위를 고르면 그 그룹도 남긴다(다른 부위와 일반 레이어는 잠김).
        Assert.True(p.IsolateLayer(p.IndexOf(arm), lockToggle: true));
        Assert.Equal((true, false, false, true), (normal.Locked, group.Locked, arm.Locked, leg.Locked));

        // 그룹을 고르면 그 부위들도 남긴다.
        Assert.True(p.IsolateLayer(p.IndexOf(group), lockToggle: true));
        Assert.Equal((true, false, false, false), (normal.Locked, group.Locked, arm.Locked, leg.Locked));

        Assert.False(p.IsolateLayer(p.IndexOf(group), lockToggle: true));
        Assert.All(p.Layers, l => Assert.False(l.Locked));
        Assert.All(p.Layers, l => Assert.True(l.Visible));
    }
}
