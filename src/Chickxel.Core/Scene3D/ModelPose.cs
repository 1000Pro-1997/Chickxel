using System.Numerics;

namespace Chickxel.Core.Scene3D;

/// <summary>
/// 장면을 한 자세로 놓는다: 애니메이션을 그 시각에 표본 내어 노드의 이동·회전·크기를 바꾸고, 월드 행렬을 구해
/// 메시를 월드 좌표로 옮긴다(뼈대가 있으면 뼈 무게대로 변형). 결과 법선은 늘 채운 단위 벡터.
/// </summary>
public static class ModelPose
{
    /// <param name="animation">애니메이션 번호(−1이거나 범위 밖이면 쉬는 자세).</param>
    /// <param name="time">초. 첫 키보다 앞이면 첫 키, 마지막 키보다 뒤면 마지막 키의 값.</param>
    public static PosedMesh Pose(ModelScene scene, int animation = -1, float time = 0)
    {
        var nodes = scene.Nodes;
        int n = nodes.Count;
        var t = new Vector3[n];
        var r = new Quaternion[n];
        var s = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            t[i] = nodes[i].Translation;
            r[i] = nodes[i].Rotation;
            s[i] = nodes[i].Scale;
        }
        if ((uint)animation < (uint)scene.Animations.Count)
            foreach (var channel in scene.Animations[animation].Channels)
                if ((uint)channel.Node < (uint)n && nodes[channel.Node].Matrix == null)
                    Apply(channel, time, t, r, s);

        var local = new Matrix4x4[n];
        for (int i = 0; i < n; i++)
            local[i] = nodes[i].Matrix ?? Matrix4x4.CreateScale(s[i]) * Matrix4x4.CreateFromQuaternion(Normalize(r[i])) * Matrix4x4.CreateTranslation(t[i]);
        var world = Worlds(nodes, local);

        var skins = new SkinMatrices?[scene.Skins.Count];
        var result = new List<PosedPrimitive>();
        var visited = new bool[n];
        var stack = new Stack<int>();
        for (int i = scene.Roots.Count - 1; i >= 0; i--) stack.Push(scene.Roots[i]);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if ((uint)i >= (uint)n || visited[i]) continue;
            visited[i] = true;
            var node = nodes[i];
            if ((uint)node.Mesh < (uint)scene.Meshes.Count)
            {
                SkinMatrices? skin = null;
                if ((uint)node.Skin < (uint)skins.Length)
                    skin = skins[node.Skin] ??= SkinMatrices.Of(scene.Skins[node.Skin], world);
                var normalMatrix = NormalMatrix(world[i]);
                foreach (var p in scene.Meshes[node.Mesh].Primitives)
                    if (PosePrimitive(scene, p, world[i], normalMatrix, skin) is { } posed) result.Add(posed with { Node = i });
            }
            for (int c = node.Children.Count - 1; c >= 0; c--) stack.Push(node.Children[c]);
        }
        return new PosedMesh(result);
    }

    // ---- 애니메이션 표본 ----

    private static void Apply(AnimationChannel c, float time, Vector3[] t, Quaternion[] r, Vector3[] s)
    {
        int comps = c.Path == AnimationPath.Rotation ? 4 : 3;
        Span<float> v = stackalloc float[4];
        if (!Sample(c, comps, time, v)) return;
        for (int k = 0; k < comps; k++)
            if (!float.IsFinite(v[k])) return;
        switch (c.Path)
        {
            case AnimationPath.Translation: t[c.Node] = new Vector3(v[0], v[1], v[2]); break;
            case AnimationPath.Scale: s[c.Node] = new Vector3(v[0], v[1], v[2]); break;
            default: r[c.Node] = Normalize(new Quaternion(v[0], v[1], v[2], v[3])); break;
        }
    }

    /// <summary>시각 <paramref name="time"/>의 값(<paramref name="comps"/>개)을 <paramref name="dst"/>에. 키가 없거나 값이 모자라면 false.</summary>
    private static bool Sample(AnimationChannel c, int comps, float time, Span<float> dst)
    {
        var times = c.Times;
        var values = c.Values;
        int keys = times.Length;
        bool cubic = c.Interpolation == AnimationInterpolation.CubicSpline;
        int keyStride = cubic ? comps * 3 : comps;
        int valueAt = cubic ? comps : 0;   // 세 벌 [들어오는 접선, 값, 나가는 접선] 중 값의 자리
        if (keys == 0 || values.Length < keys * keyStride) return false;

        if (!(time > times[0]) || keys == 1)
        {
            values.AsSpan(valueAt, comps).CopyTo(dst);
            return true;
        }
        if (time >= times[keys - 1])
        {
            values.AsSpan((keys - 1) * keyStride + valueAt, comps).CopyTo(dst);
            return true;
        }

        // times[k] <= time < times[k + 1]
        int lo = 0, hi = keys - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (times[mid] <= time) lo = mid;
            else hi = mid;
        }
        int k0 = lo, k1 = lo + 1;
        float td = times[k1] - times[k0];
        float u = td > 0 ? Math.Clamp((time - times[k0]) / td, 0, 1) : 0;

        switch (c.Interpolation)
        {
            case AnimationInterpolation.Step:
                values.AsSpan(k0 * keyStride, comps).CopyTo(dst);
                return true;
            case AnimationInterpolation.CubicSpline:
            {
                // glTF 부록 C: p(u) = (2u³−3u²+1)v₀ + td(u³−2u²+u)b₀ + (−2u³+3u²)v₁ + td(u³−u²)a₁
                float u2 = u * u, u3 = u2 * u;
                float h00 = 2 * u3 - 3 * u2 + 1, h10 = (u3 - 2 * u2 + u) * td, h01 = -2 * u3 + 3 * u2, h11 = (u3 - u2) * td;
                int b0 = k0 * keyStride, b1 = k1 * keyStride;
                for (int e = 0; e < comps; e++)
                    dst[e] = h00 * values[b0 + comps + e] + h10 * values[b0 + 2 * comps + e]
                        + h01 * values[b1 + comps + e] + h11 * values[b1 + e];
                return true;
            }
            default:
                int o0 = k0 * keyStride, o1 = k1 * keyStride;
                if (comps == 4)
                {
                    var q0 = new Quaternion(values[o0], values[o0 + 1], values[o0 + 2], values[o0 + 3]);
                    var q1 = new Quaternion(values[o1], values[o1 + 1], values[o1 + 2], values[o1 + 3]);
                    var q = Quaternion.Slerp(Normalize(q0), Normalize(q1), u);
                    dst[0] = q.X; dst[1] = q.Y; dst[2] = q.Z; dst[3] = q.W;
                }
                else
                {
                    for (int e = 0; e < comps; e++) dst[e] = values[o0 + e] + (values[o1 + e] - values[o0 + e]) * u;
                }
                return true;
        }
    }

    private static Quaternion Normalize(Quaternion q)
    {
        float len = q.Length();
        return len > 1e-12f && float.IsFinite(len) ? q * (1 / len) : Quaternion.Identity;
    }

    // ---- 행렬 ----

    /// <summary>월드 = 로컬 × 부모 월드. 부모 고리(잘못 만든 장면)는 끊어 단위 행렬 부모로 본다.</summary>
    private static Matrix4x4[] Worlds(IReadOnlyList<ModelNode> nodes, Matrix4x4[] local)
    {
        int n = nodes.Count;
        var world = new Matrix4x4[n];
        var state = new byte[n];   // 0 = 아직, 1 = 계산 중, 2 = 끝
        var chain = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (state[i] == 2) continue;
            chain.Clear();
            int k = i;
            while ((uint)k < (uint)n && state[k] == 0)
            {
                state[k] = 1;
                chain.Add(k);
                k = nodes[k].Parent;
            }
            var parent = (uint)k < (uint)n && state[k] == 2 ? world[k] : Matrix4x4.Identity;
            for (int j = chain.Count - 1; j >= 0; j--)
            {
                int idx = chain[j];
                parent = local[idx] * parent;
                world[idx] = parent;
                state[idx] = 2;
            }
        }
        return world;
    }

    /// <summary>법선 변환 행렬(역행렬의 전치 — 고르지 않은 크기에서도 면에 수직). 역행렬이 없으면 그 행렬 그대로.</summary>
    private static Matrix4x4 NormalMatrix(Matrix4x4 m)
        => Matrix4x4.Invert(m, out var inv) ? Matrix4x4.Transpose(inv) : m;

    /// <summary>뼈마다 행렬(바인드 역행렬 × 뼈 월드)과 그 법선 행렬.</summary>
    private sealed class SkinMatrices(Matrix4x4[] joints, Matrix4x4[] normals)
    {
        public readonly Matrix4x4[] Joints = joints;
        public readonly Matrix4x4[] Normals = normals;

        public static SkinMatrices Of(ModelSkin skin, Matrix4x4[] world)
        {
            int count = skin.Joints.Count;
            var joints = new Matrix4x4[count];
            var normals = new Matrix4x4[count];
            for (int j = 0; j < count; j++)
            {
                int node = skin.Joints[j];
                var jointWorld = (uint)node < (uint)world.Length ? world[node] : Matrix4x4.Identity;
                var inverseBind = j < skin.InverseBindMatrices.Count ? skin.InverseBindMatrices[j] : Matrix4x4.Identity;
                joints[j] = inverseBind * jointWorld;
                normals[j] = NormalMatrix(joints[j]);
            }
            return new SkinMatrices(joints, normals);
        }
    }

    // ---- 프리미티브 ----

    private static PosedPrimitive? PosePrimitive(ModelScene scene, ModelPrimitive p, Matrix4x4 world, Matrix4x4 normalMatrix, SkinMatrices? skin)
    {
        var src = p.Positions;
        int n = src.Length;
        var srcNormals = p.Normals is { } sn && sn.Length == n ? sn : null;
        var pos = new Vector3[n];
        var nrm = new Vector3[n];
        var joints = p.Joints;
        var weights = p.Weights;
        bool skinned = skin != null && joints != null && weights != null && joints.Length >= n * 4 && weights.Length >= n * 4;

        if (skinned)
        {
            // glTF 규칙: 뼈대로 변형하는 메시는 메시 노드 자신의 변환을 쓰지 않는다(무게가 모두 0인 꼭짓점만 노드 변환으로).
            var jm = skin!.Joints;
            var jn = skin.Normals;
            for (int i = 0; i < n; i++)
            {
                Matrix4x4 m = default, nm = default;
                float sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    float w = weights![i * 4 + k];
                    int j = joints![i * 4 + k];
                    if (!(w > 0) || !float.IsFinite(w) || (uint)j >= (uint)jm.Length) continue;
                    sum += w;
                    m += jm[j] * w;
                    nm += jn[j] * w;
                }
                if (sum > 1e-6f)
                {
                    float inv = 1 / sum;   // 무게 합이 1이 아니면 맞춘다
                    m *= inv;
                    nm *= inv;
                }
                else
                {
                    m = world;
                    nm = normalMatrix;
                }
                pos[i] = Vector3.Transform(src[i], m);
                if (srcNormals != null) nrm[i] = Vector3.TransformNormal(srcNormals[i], nm);
            }
        }
        else
        {
            for (int i = 0; i < n; i++) pos[i] = Vector3.Transform(src[i], world);
            if (srcNormals != null)
                for (int i = 0; i < n; i++) nrm[i] = Vector3.TransformNormal(srcNormals[i], normalMatrix);
        }

        // 범위 밖 번호·NaN 꼭짓점이 있는 삼각형은 뺀다. 거울 변환(행렬식 < 0)이면 감기 방향을 되돌려 앞면을 지킨다.
        var srcIdx = p.Indices;
        bool flip = !skinned && world.GetDeterminant() < 0;
        var indices = new int[srcIdx.Length - srcIdx.Length % 3];
        int count = 0;
        for (int f = 0; f + 2 < srcIdx.Length; f += 3)
        {
            int a = srcIdx[f], b = srcIdx[f + 1], c = srcIdx[f + 2];
            if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n) continue;
            if (!Finite(pos[a]) || !Finite(pos[b]) || !Finite(pos[c])) continue;
            indices[count++] = a;
            indices[count++] = flip ? c : b;
            indices[count++] = flip ? b : c;
        }
        if (count == 0) return null;
        if (count < indices.Length) Array.Resize(ref indices, count);

        // 법선: 단위 길이로, 없거나 망가진 것은 둘레 삼각형 넓이로 무게 준 매끈한 법선으로.
        Vector3[]? smooth = null;
        for (int i = 0; i < n; i++)
        {
            var v = nrm[i];
            float len = v.Length();
            if (srcNormals != null && len > 1e-12f && float.IsFinite(len))
            {
                nrm[i] = v / len;
                continue;
            }
            smooth ??= SmoothNormals(pos, indices);
            nrm[i] = smooth[i];
        }

        var material = (uint)p.Material < (uint)scene.Materials.Count ? scene.Materials[p.Material] : ModelMaterial.Default;
        return new PosedPrimitive(pos, nrm,
            p.Uvs is { } uv && uv.Length == n ? uv : null,
            p.Colors is { } col && col.Length == n ? col : null,
            indices, material);
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>꼭짓점마다 붙은 삼각형의 (넓이로 무게 준) 면 법선 합을 단위 벡터로. 붙은 면이 없으면 +Z.</summary>
    private static Vector3[] SmoothNormals(Vector3[] pos, int[] indices)
    {
        var acc = new Vector3[pos.Length];
        for (int f = 0; f + 2 < indices.Length; f += 3)
        {
            int a = indices[f], b = indices[f + 1], c = indices[f + 2];
            var face = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);   // 길이 = 넓이의 두 배
            acc[a] += face;
            acc[b] += face;
            acc[c] += face;
        }
        for (int i = 0; i < acc.Length; i++)
        {
            float len = acc[i].Length();
            acc[i] = len > 1e-20f && float.IsFinite(len) ? acc[i] / len : Vector3.UnitZ;
        }
        return acc;
    }
}
