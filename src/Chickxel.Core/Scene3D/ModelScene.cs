using System.Numerics;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scene3D;

/// <summary>재질의 투명 처리(glTF alphaMode).</summary>
public enum ModelAlphaMode
{
    Opaque,
    /// <summary>알파가 <see cref="ModelMaterial.AlphaCutoff"/>보다 작으면 그리지 않는다.</summary>
    Mask,
    /// <summary>반투명 — 그리기는 <see cref="Mask"/>처럼 문턱 0.5로 자른다(픽셀 그림에는 반투명 겹침이 필요 없음).</summary>
    Blend,
}

/// <summary>
/// 재질 하나. 색은 선형광(glTF baseColorFactor 그대로, OBJ Kd도 그대로 선형으로 봄)이고, 텍스처는 sRGB 그림 그대로(그릴 때 선형으로 바꿔 곱함).
/// </summary>
public sealed class ModelMaterial
{
    public string Name { get; init; } = "";
    /// <summary>바탕 색(선형 RGB + 알파, 0~1).</summary>
    public Vector4 BaseColor { get; init; } = Vector4.One;
    /// <summary>바탕 색 텍스처(sRGB, 없으면 null). UV (0,0) = 그림 왼쪽 위(glTF 규칙 — OBJ는 읽을 때 v를 뒤집어 맞춤).</summary>
    public PixelImage? Texture { get; init; }
    /// <summary>텍스처를 덮어 반복할지(glTF sampler wrap REPEAT가 기본). false면 가장자리에 붙인다.</summary>
    public bool Repeat { get; init; } = true;
    public ModelAlphaMode AlphaMode { get; init; } = ModelAlphaMode.Opaque;
    public float AlphaCutoff { get; init; } = 0.5f;
    /// <summary>스스로 내는 빛(선형 RGB) — 빛 계산 뒤 더한다.</summary>
    public Vector3 Emissive { get; init; }
    /// <summary>빛 계산 없이 바탕 색 그대로(KHR_materials_unlit).</summary>
    public bool Unlit { get; init; }

    public static ModelMaterial Default { get; } = new() { Name = "default", BaseColor = new Vector4(0.8f, 0.8f, 0.8f, 1) };
}

/// <summary>
/// 삼각형 묶음 하나(재질 하나). 위치·법선은 그 메시를 단 노드의 좌표(로컬), <see cref="Indices"/>는 삼각형마다 꼭짓점 셋.
/// 뼈대가 있으면 꼭짓점마다 뼈 넷(<see cref="Joints"/> — 스킨의 <see cref="ModelSkin.Joints"/> 안 번호)과 무게 넷(<see cref="Weights"/>).
/// </summary>
public sealed class ModelPrimitive
{
    public required Vector3[] Positions { get; init; }
    /// <summary>꼭짓점 법선(없으면 null — <see cref="ModelPose"/>가 면 법선으로 채움).</summary>
    public Vector3[]? Normals { get; init; }
    public Vector2[]? Uvs { get; init; }
    /// <summary>꼭짓점 색(선형 RGBA, glTF COLOR_0).</summary>
    public Vector4[]? Colors { get; init; }
    public required int[] Indices { get; init; }
    /// <summary><see cref="ModelScene.Materials"/> 안 번호(−1 = <see cref="ModelMaterial.Default"/>).</summary>
    public int Material { get; init; } = -1;
    /// <summary>꼭짓점마다 뼈 번호 넷(길이 = 꼭짓점 수 × 4), 없으면 null.</summary>
    public int[]? Joints { get; init; }
    /// <summary>꼭짓점마다 뼈 무게 넷(길이 = 꼭짓점 수 × 4), 없으면 null.</summary>
    public float[]? Weights { get; init; }
}

public sealed class ModelMesh
{
    public string Name { get; init; } = "";
    public required IReadOnlyList<ModelPrimitive> Primitives { get; init; }
}

/// <summary>장면 노드: 쉬는 자세의 이동·회전·크기(애니메이션이 바꿈). <see cref="Matrix"/>가 있으면 그 행렬이 쉬는 자세이고 애니메이션이 바꾸지 않는다.</summary>
public sealed class ModelNode
{
    public string Name { get; init; } = "";
    public int Parent { get; set; } = -1;
    public IReadOnlyList<int> Children { get; init; } = [];
    public Vector3 Translation { get; init; }
    public Quaternion Rotation { get; init; } = Quaternion.Identity;
    public Vector3 Scale { get; init; } = Vector3.One;
    /// <summary>로컬 행렬(System.Numerics 행 벡터 규칙 — v' = v × M, glTF 열 우선 배열을 그대로 읽으면 이 규칙이 됨). null이면 TRS.</summary>
    public Matrix4x4? Matrix { get; init; }
    /// <summary><see cref="ModelScene.Meshes"/> 안 번호(−1 = 없음).</summary>
    public int Mesh { get; init; } = -1;
    /// <summary><see cref="ModelScene.Skins"/> 안 번호(−1 = 없음).</summary>
    public int Skin { get; init; } = -1;
}

/// <summary>뼈대: 뼈가 될 노드들과 뼈마다 바인드 역행렬(행 벡터 규칙).</summary>
public sealed class ModelSkin
{
    public required IReadOnlyList<int> Joints { get; init; }
    public required IReadOnlyList<Matrix4x4> InverseBindMatrices { get; init; }
}

public enum AnimationPath
{
    Translation,
    Rotation,
    Scale,
}

public enum AnimationInterpolation
{
    Linear,
    Step,
    CubicSpline,
}

/// <summary>
/// 노드 하나의 이동·회전·크기 하나를 바꾸는 키 목록. <see cref="Values"/>는 키마다 값(이동·크기 3개, 회전 4개 x,y,z,w)을 이어 붙인 것이고,
/// <see cref="AnimationInterpolation.CubicSpline"/>이면 키마다 [들어오는 접선, 값, 나가는 접선] 세 벌.
/// </summary>
public sealed class AnimationChannel
{
    public required int Node { get; init; }
    public required AnimationPath Path { get; init; }
    public AnimationInterpolation Interpolation { get; init; } = AnimationInterpolation.Linear;
    public required float[] Times { get; init; }
    public required float[] Values { get; init; }
}

public sealed class ModelAnimation
{
    public string Name { get; init; } = "";
    public required IReadOnlyList<AnimationChannel> Channels { get; init; }
    /// <summary>길이(초) = 모든 키 시각의 최댓값.</summary>
    public float Duration => Channels.Count == 0 ? 0 : Channels.Max(c => c.Times.Length == 0 ? 0 : c.Times[^1]);
}

/// <summary>불러온 3D 모델(glTF·GLB·OBJ·FBX) 한 장면. 좌표는 오른손, +Y 위, 카메라 기본 방향은 +Z에서 −Z를 봄(glTF 규칙).</summary>
public sealed class ModelScene
{
    public string Name { get; init; } = "";
    public required IReadOnlyList<ModelNode> Nodes { get; init; }
    /// <summary>그릴 장면의 맨 위 노드들.</summary>
    public required IReadOnlyList<int> Roots { get; init; }
    public required IReadOnlyList<ModelMesh> Meshes { get; init; }
    public IReadOnlyList<ModelMaterial> Materials { get; init; } = [];
    public IReadOnlyList<ModelSkin> Skins { get; init; } = [];
    public IReadOnlyList<ModelAnimation> Animations { get; init; } = [];
    /// <summary>읽기는 했지만 그리지 못하는 것(모프 타깃·지원하지 않는 확장 등) — 열기 창에 알림으로 보인다. 없으면 빈 목록.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// 한 자세로 놓은 삼각형 묶음(월드 좌표 — 노드 변환·뼈대 변형을 마친 것). 법선은 월드 좌표의 단위 벡터, 꼭짓점마다 하나(늘 채움).
/// </summary>
public sealed record PosedPrimitive(Vector3[] Positions, Vector3[] Normals, Vector2[]? Uvs, Vector4[]? Colors, int[] Indices, ModelMaterial Material);

/// <summary>한 자세의 장면 전체(<see cref="ModelPose.Pose"/>의 결과).</summary>
public sealed record PosedMesh(IReadOnlyList<PosedPrimitive> Primitives)
{
    public int TriangleCount => Primitives.Sum(p => p.Indices.Length / 3);
}
