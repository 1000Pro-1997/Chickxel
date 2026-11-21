using System.Numerics;
using Chickxel.Core.Scene3D;
using Xunit;

namespace Chickxel.Core.Tests;

public class ModelFramesTests
{
    /// <summary>노드 하나에 단위 상자 하나, 1초 동안 +X로 2만큼 옮기는 애니메이션 하나.</summary>
    private static ModelScene MovingBox()
    {
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f);
        int[] idx =
        [
            0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4,
            2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5,
        ];
        var mesh = new ModelMesh { Primitives = [new ModelPrimitive { Positions = p, Indices = idx }] };
        var move = new AnimationChannel { Node = 0, Path = AnimationPath.Translation, Times = [0, 1], Values = [0, 0, 0, 2, 0, 0] };
        return new ModelScene
        {
            Nodes = [new ModelNode { Mesh = 0 }],
            Roots = [0],
            Meshes = [mesh],
            Animations = [new ModelAnimation { Name = "move", Channels = [move] }],
        };
    }

    [Fact]
    public void PlanSplitsTurntableAndAnimation()
    {
        var scene = MovingBox();
        var turn = ModelFrames.Plan(scene, new ModelFrameOptions { Mode = ModelFrameMode.Turntable, TurntableFrames = 8 });
        Assert.Equal([0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f], turn.Select(f => f.YawOffset));
        Assert.All(turn, f => Assert.Equal(-1, f.Animation));

        // 1초·12fps = 12프레임(끝 시각 1초는 첫 프레임과 겹치므로 넣지 않음), 최대 프레임 수로 자름.
        var anim = ModelFrames.Plan(scene, new ModelFrameOptions { Mode = ModelFrameMode.Animation, Fps = 12 });
        Assert.Equal(12, anim.Count);
        Assert.Equal(11 / 12f, anim[^1].Time, 4);
        Assert.Equal(5, ModelFrames.Plan(scene, new ModelFrameOptions { Mode = ModelFrameMode.Animation, Fps = 12, MaxFrames = 5 }).Count);
        Assert.Equal(83, ModelFrames.DurationMs(new ModelFrameOptions { Mode = ModelFrameMode.Animation, Fps = 12 }));

        // 없는 애니메이션 번호는 쉬는 자세 한 장.
        var none = ModelFrames.Plan(scene, new ModelFrameOptions { Mode = ModelFrameMode.Animation, Animation = 3 });
        Assert.Equal([new ModelFrame(-1, 0, 0)], none);
    }

    /// <summary>애니메이션 프레임은 모든 자세를 합친 범위에 맞춰 그려 움직임이 그림 안에서 보인다(프레임마다 다시 맞추면 상자가 늘 가운데).</summary>
    [Fact]
    public void AnimationFramesShareOneFit()
    {
        var scene = MovingBox();
        var settings = new ModelRenderSettings { Width = 48, Height = 48, Yaw = 0, Pitch = 0, Supersample = 1 };
        var anim = ModelFrames.Render(scene, settings, new ModelFrameOptions { Mode = ModelFrameMode.Animation, Fps = 4 });
        Assert.Equal(4, anim.FrameCount);
        Assert.All(anim.Images, i => Assert.Equal((48, 48), (i.Width, i.Height)));
        var first = anim.Images[0].GetOpaqueBounds();
        var last = anim.Images[^1].GetOpaqueBounds();
        Assert.False(first.IsEmpty);
        Assert.True(last.Left > first.Left + 5, $"{first} → {last}");
        Assert.InRange(last.Width, first.Width - 1, first.Width + 1);
    }
}
