using System.Numerics;
using Chickxel.Core.Scene3D;

namespace Chickxel.Core.Tests;

public class AnimationImportTests
{
    private static readonly Quaternion SpineTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);

    /// <summary>
    /// 캐릭터: Hips(0,1,0) → Spine(+0.5) → Head(+0.5), Hips → LeftFoot(−1, 월드 Y 0). 골반 높이 1.
    /// 메시 노드 Body는 삼각형 하나를 Spine에 무게 1로 단다(Head 높이, 월드 Y 2).
    /// </summary>
    private static ModelScene Character(string prefix = "mixamorig:", IReadOnlyList<ModelAnimation>? animations = null)
    {
        var nodes = new List<ModelNode>
        {
            new() { Name = prefix + "Hips", Translation = new Vector3(0, 1, 0), Children = [1, 3] },
            new() { Name = prefix + "Spine", Parent = 0, Translation = new Vector3(0, 0.5f, 0), Children = [2] },
            new() { Name = prefix + "Head", Parent = 1, Translation = new Vector3(0, 0.5f, 0) },
            new() { Name = prefix + "LeftFoot", Parent = 0, Translation = new Vector3(0, -1, 0) },
            new() { Name = "Body", Mesh = 0, Skin = 0 },
        };
        int[] joints = [0, 1, 2, 3];
        var inverseBind = new Matrix4x4[joints.Length];
        Vector3[] rest = [new(0, 1, 0), new(0, 1.5f, 0), new(0, 2, 0), new(0, 0, 0)];
        for (int j = 0; j < joints.Length; j++) inverseBind[j] = Matrix4x4.CreateTranslation(-rest[j]);
        var primitive = new ModelPrimitive
        {
            Positions = [new(0, 2, 0), new(0.1f, 2, 0), new(0, 2.1f, 0)],
            Indices = [0, 1, 2],
            Joints = [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0],
            Weights = [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0],
        };
        return new ModelScene
        {
            Name = "character",
            Nodes = nodes,
            Roots = [0, 4],
            Meshes = [new ModelMesh { Primitives = [primitive] }],
            Skins = [new ModelSkin { Joints = joints, InverseBindMatrices = inverseBind }],
            Animations = animations ?? [],
            Warnings = ["target warning"],
        };
    }

    /// <summary>
    /// 애니메이션만 든 원본: 키가 캐릭터의 절반(골반 높이 0.5), 골반이 옆으로 0.2 비켜 쉼. Spine은 접두사 없음, Tail은 캐릭터에 없음.
    /// </summary>
    private static ModelScene Source(params ModelAnimation[] animations) => new()
    {
        Nodes =
        [
            new() { Name = "mixamorig1:Hips", Translation = new Vector3(0.2f, 0.5f, 0), Children = [1, 3, 4] },
            new() { Name = "Spine", Parent = 0, Translation = new Vector3(0, 0.25f, 0), Children = [2] },
            new() { Name = "mixamorig1:Head", Parent = 1, Translation = new Vector3(0, 0.25f, 0) },
            new() { Name = "mixamorig1:LeftFoot", Parent = 0, Translation = new Vector3(0, -0.5f, 0) },
            new() { Name = "mixamorig1:Tail", Parent = 0 },
        ],
        Roots = [0],
        Meshes = [],
        Animations = animations,
    };

    private static ModelAnimation Dance(string name = "mixamo.com") => new()
    {
        Name = name,
        Channels =
        [
            new AnimationChannel { Node = 0, Path = AnimationPath.Translation, Times = [0, 1], Values = [0.2f, 0.5f, 0, 0.2f, 0.75f, 0.1f] },
            new AnimationChannel { Node = 1, Path = AnimationPath.Rotation, Times = [0, 1], Values = [0, 0, 0, 1, SpineTurn.X, SpineTurn.Y, SpineTurn.Z, SpineTurn.W] },
            new AnimationChannel { Node = 2, Path = AnimationPath.Translation, Times = [0], Values = [0, 0.3f, 0] },
            new AnimationChannel { Node = 1, Path = AnimationPath.Scale, Times = [0], Values = [1, 1, 1] },
            new AnimationChannel { Node = 4, Path = AnimationPath.Rotation, Times = [0], Values = [0, 0, 0, 1] },
        ],
    };

    private static void Near(Vector3 expected, Vector3 actual)
        => Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, actual {actual}");

    [Theory]
    [InlineData("mixamorig:Hips", "hips")]
    [InlineData("mixamorig1:Hips", "hips")]
    [InlineData("Armature|mixamorig:Hips", "hips")]
    [InlineData(" LeftUpLeg ", "leftupleg")]
    [InlineData("", "")]
    public void BoneKey_StripsPrefixes(string name, string key) => Assert.Equal(key, ModelAnimationImport.BoneKey(name));

    [Fact]
    public void Attach_RetargetsChannels()
    {
        var target = Character();
        var result = ModelAnimationImport.Attach(target, Source(Dance()), "Samba");

        Assert.Empty(target.Animations);
        Assert.Equal(1, result.Added);
        Assert.Equal(4, result.SourceBones);   // Hips, Spine, Head, Tail
        Assert.Equal(3, result.MatchedBones);
        Assert.Equal(["target warning"], result.Scene.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("Tail"));
        Assert.Same(target.Nodes, result.Scene.Nodes);

        var anim = Assert.Single(result.Scene.Animations);
        Assert.Equal("Samba", anim.Name);
        Assert.Equal(2, anim.Channels.Count);   // Head 이동·크기·Tail 회전은 버림

        var spine = Assert.Single(anim.Channels, c => c.Path == AnimationPath.Rotation);
        Assert.Equal(1, spine.Node);
        Assert.Equal(Dance().Channels[1].Values, spine.Values);

        // 캐릭터 쉬는 위치 + (키 − 원본 쉬는 위치) × 2(골반 높이 1 / 0.5)
        var hips = Assert.Single(anim.Channels, c => c.Path == AnimationPath.Translation);
        Assert.Equal(0, hips.Node);
        Near(new Vector3(0, 1, 0), new Vector3(hips.Values[0], hips.Values[1], hips.Values[2]));
        Near(new Vector3(0, 1.5f, 0.2f), new Vector3(hips.Values[3], hips.Values[4], hips.Values[5]));
    }

    [Fact]
    public void Attach_PosedSpineRotationMovesSkinnedVertex()
    {
        var result = ModelAnimationImport.Attach(Character(), Source(Dance()), "Samba");
        var posed = ModelPose.Pose(result.Scene, 0, 1);
        // 골반 (0,1.5,0.2) → Spine (0,2,0.2), Spine에서 (0,0.5,0)이 Z축 90° 돌아 (−0.5,0,0).
        Near(new Vector3(-0.5f, 2, 0.2f), Assert.Single(posed.Primitives).Positions[0]);
    }

    [Fact]
    public void Attach_NoMatchingBones_Throws()
    {
        var source = new ModelScene
        {
            Nodes = [new() { Name = "Bip01 Pelvis" }],
            Roots = [0],
            Meshes = [],
            Animations = [new ModelAnimation { Channels = [new AnimationChannel { Node = 0, Path = AnimationPath.Rotation, Times = [0], Values = [0, 0, 0, 1] }] }],
        };
        Assert.Throws<InvalidDataException>(() => ModelAnimationImport.Attach(Character(), source, "x"));
    }

    [Fact]
    public void Attach_NamesClipsUniquely()
    {
        var existing = new ModelAnimation { Name = "Samba", Channels = [] };
        var one = ModelAnimationImport.Attach(Character(animations: [existing]), Source(Dance()), "Samba");
        Assert.Equal(["Samba", "Samba 2"], one.Scene.Animations.Select(a => a.Name));

        var again = ModelAnimationImport.Attach(one.Scene, Source(Dance()), "Samba");
        Assert.Equal(["Samba", "Samba 2", "Samba 3"], again.Scene.Animations.Select(a => a.Name));

        var many = ModelAnimationImport.Attach(Character(), Source(Dance("Idle"), Dance("Run"), Dance("Run")), "Pack");
        Assert.Equal(3, many.Added);
        Assert.Equal(["Pack: Idle", "Pack: Run", "Pack: Run 2"], many.Scene.Animations.Select(a => a.Name));
    }

    [Fact]
    public void Attach_SkipsClipWithoutMatchingChannels()
    {
        var tailOnly = new ModelAnimation
        {
            Name = "Wag",
            Channels = [new AnimationChannel { Node = 4, Path = AnimationPath.Rotation, Times = [0], Values = [0, 0, 0, 1] }],
        };
        var result = ModelAnimationImport.Attach(Character(), Source(Dance("Idle"), tailOnly), "Pack");
        Assert.Equal(1, result.Added);
        Assert.Equal("Pack: Idle", Assert.Single(result.Scene.Animations).Name);
        Assert.Contains(result.Warnings, w => w.Contains("Pack: Wag"));
    }

    [Fact]
    public void Attach_PrefersSkinJointAmongDuplicateNames()
    {
        var c = Character();
        // 같은 이름 Spine이 둘: 앞의 1번은 스킨 뼈가 아니고, 뒤의 5번이 스킨 뼈.
        var nodes = new List<ModelNode>(c.Nodes) { new() { Name = "mixamorig:Spine", Parent = 0, Translation = new Vector3(0, 0.5f, 0) } };
        var skin = new ModelSkin { Joints = [0, 5, 2, 3], InverseBindMatrices = c.Skins[0].InverseBindMatrices };
        var target = new ModelScene { Nodes = nodes, Roots = [0, 4], Meshes = c.Meshes, Skins = [skin] };
        var result = ModelAnimationImport.Attach(target, Source(Dance()), "Samba");
        Assert.Equal(5, Assert.Single(result.Scene.Animations[0].Channels, ch => ch.Path == AnimationPath.Rotation).Node);
        Assert.Contains(result.Warnings, w => w.Contains("Spine"));
    }

    [Fact]
    public void Attach_DropsChannelsOnMatrixNodes()
    {
        var c = Character();
        var nodes = new List<ModelNode>(c.Nodes);
        nodes[1] = new ModelNode { Name = nodes[1].Name, Parent = 0, Children = [2], Matrix = Matrix4x4.CreateTranslation(0, 0.5f, 0) };
        var target = new ModelScene { Nodes = nodes, Roots = c.Roots, Meshes = c.Meshes, Skins = c.Skins };
        var result = ModelAnimationImport.Attach(target, Source(Dance()), "Samba");
        Assert.DoesNotContain(result.Scene.Animations[0].Channels, ch => ch.Node == 1);
        Assert.Contains(result.Warnings, w => w.Contains("mixamorig:Spine"));
    }
}
