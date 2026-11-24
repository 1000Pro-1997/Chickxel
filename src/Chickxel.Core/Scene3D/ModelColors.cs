using System.Numerics;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.Scene3D;

/// <summary>
/// 3D 모델이 쓰는 바탕 색(빛을 받기 전 재질 색 × 꼭짓점 색, sRGB 8비트 — <see cref="ModelRenderLayers.Albedo"/>와 같은 값)을 모으고 바꾼다.
/// 텍스처를 입힌 묶음은 색이 텍스처에 있으므로 뺀다.
/// </summary>
public static class ModelColors
{
    /// <summary>장면이 쓰는 바탕 색: 많이 쓴 색(그 색 꼭짓점을 쓰는 삼각형 모서리 수)부터.</summary>
    public static IReadOnlyList<Color32> Used(ModelScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var counts = new Dictionary<Color32, int>();
        foreach (var mesh in scene.Meshes)
            foreach (var p in mesh.Primitives)
            {
                if (!Plain(scene, p, out var base_)) continue;
                int n = p.Positions.Length;
                var keys = new Color32[n];
                var vc = VertexColors(p);
                for (int i = 0; i < n; i++) keys[i] = Key(base_, vc, i);
                foreach (int i in p.Indices)
                    if ((uint)i < (uint)n) counts[keys[i]] = counts.GetValueOrDefault(keys[i]) + 1;
            }
        return [.. counts.OrderByDescending(c => c.Value).ThenBy(c => c.Key.Argb).Select(c => c.Key)];
    }

    /// <summary>
    /// <paramref name="map"/>(원래 바탕 색 → 새 색)대로 색을 바꾼 새 장면. 새 색으로 그대로 바꾼다(원래 색에 물들이지 않음 — 노랑을 파랑으로 바꾸면 파랑):
    /// 바뀐 색이 있는 묶음은 재질 색을 꼭짓점 색으로 옮기고(꼭짓점마다 원래 바탕 색, 바꿀 꼭짓점은 새 색) 바탕 색이 흰색인 재질 사본을 쓴다
    /// (사본은 원래 재질마다 하나를 장면 재질 끝에 더함 — 원래 재질은 그대로라 같은 재질을 쓰는 다른 묶음에는 번지지 않음, 알파·빛·유리 등 다른 성질은 그대로).
    /// 나머지(노드·애니메이션 등)는 그대로 나눠 쓰고, 바꿀 것이 없으면 <paramref name="scene"/> 그대로.
    /// </summary>
    public static ModelScene Recolor(ModelScene scene, IReadOnlyDictionary<Color32, Color32> map)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(map);
        if (map.Count == 0) return scene;
        var materials = new List<ModelMaterial>(scene.Materials);
        // 원래 재질 번호(-1 = 기본 재질) → 바탕 색을 흰색으로 둔 사본 번호
        var whites = new Dictionary<int, int>();
        int White(int material)
        {
            int key = (uint)material < (uint)scene.Materials.Count ? material : -1;
            if (whites.TryGetValue(key, out int index)) return index;
            var m = key >= 0 ? scene.Materials[key] : ModelMaterial.Default;
            materials.Add(m.WithBaseColor(new Vector4(1, 1, 1, m.BaseColor.W)));
            return whites[key] = materials.Count - 1;
        }
        bool any = false;
        var meshes = new List<ModelMesh>(scene.Meshes.Count);
        foreach (var mesh in scene.Meshes)
        {
            var prims = new List<ModelPrimitive>(mesh.Primitives.Count);
            bool changed = false;
            foreach (var p in mesh.Primitives)
            {
                var q = Plain(scene, p, out var base_) ? RecolorPrimitive(p, base_, map, White) : p;
                changed |= !ReferenceEquals(q, p);
                prims.Add(q);
            }
            any |= changed;
            meshes.Add(changed ? new ModelMesh { Name = mesh.Name, Primitives = prims } : mesh);
        }
        if (!any) return scene;
        return new ModelScene
        {
            Name = scene.Name,
            Nodes = scene.Nodes,
            Roots = scene.Roots,
            Meshes = meshes,
            Materials = materials,
            Skins = scene.Skins,
            Animations = scene.Animations,
            Warnings = scene.Warnings,
        };
    }

    /// <summary>
    /// 바꿀 꼭짓점이 있으면: 꼭짓점 색 = 원래 바탕 색(재질 색 × 꼭짓점 색, 알파는 꼭짓점 알파) 또는 새 색(선형), 재질 = 흰 사본(<paramref name="white"/>).
    /// 바꾸지 않은 꼭짓점은 재질 × 꼭짓점과 같은 값이라 그대로 보인다.
    /// </summary>
    private static ModelPrimitive RecolorPrimitive(ModelPrimitive p, Vector4 base_, IReadOnlyDictionary<Color32, Color32> map, Func<int, int> white)
    {
        int n = p.Positions.Length;
        var vc = VertexColors(p);
        Vector4[]? colors = null;
        for (int i = 0; i < n; i++)
        {
            if (!map.TryGetValue(Key(base_, vc, i), out var target)) continue;
            if (colors is null)
            {
                colors = new Vector4[n];
                for (int j = 0; j < n; j++)
                {
                    var c = vc is not null ? vc[j] : Vector4.One;
                    colors[j] = new Vector4(base_.X * c.X, base_.Y * c.Y, base_.Z * c.Z, c.W);
                }
            }
            colors[i] = new Vector4(OkLab.ToLinear(target.R), OkLab.ToLinear(target.G), OkLab.ToLinear(target.B), colors[i].W);
        }
        if (colors is null) return p;
        return new ModelPrimitive
        {
            Positions = p.Positions,
            Normals = p.Normals,
            Uvs = p.Uvs,
            Colors = colors,
            Indices = p.Indices,
            Material = white(p.Material),
            Joints = p.Joints,
            Weights = p.Weights,
        };
    }

    /// <summary>그리기가 쓰는 꼭짓점 색(꼭짓점 수보다 짧으면 쓰지 않음 — 그리기와 같음).</summary>
    private static Vector4[]? VertexColors(ModelPrimitive p) => p.Colors is { } c && c.Length >= p.Positions.Length ? c : null;

    /// <summary>텍스처 없는 묶음인지와 그 재질 색(<see cref="ModelPose"/>와 같은 재질 찾기).</summary>
    private static bool Plain(ModelScene scene, ModelPrimitive p, out Vector4 baseColor)
    {
        var m = (uint)p.Material < (uint)scene.Materials.Count ? scene.Materials[p.Material] : ModelMaterial.Default;
        baseColor = m.BaseColor;
        return !(m.Texture is not null && p.Uvs is not null);
    }

    /// <summary>꼭짓점 <paramref name="i"/>의 바탕 색(재질 색 × 꼭짓점 색 → sRGB 8비트, 그리기와 같은 계산).</summary>
    private static Color32 Key(Vector4 base_, Vector4[]? colors, int i)
    {
        var c = colors is { } cs && i < cs.Length ? base_ * cs[i] : base_;
        return ModelRenderer.Srgb8(new Vector3(c.X, c.Y, c.Z));
    }
}
