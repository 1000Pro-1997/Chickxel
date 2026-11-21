using System.Numerics;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Scene3D;

/// <summary>
/// <see cref="ModelAnimationImport.Attach"/>의 결과: 애니메이션을 더한 새 장면, 더한 클립 수, 움직임이 있는 원본 뼈 중 캐릭터에 맞춘 수와 전체 수,
/// 붙이며 생긴 알림(장면의 <see cref="ModelScene.Warnings"/>에는 넣지 않음).
/// </summary>
public sealed record AnimationImportResult(ModelScene Scene, int Added, int MatchedBones, int SourceBones, IReadOnlyList<string> Warnings);

/// <summary>
/// 다른 파일(뼈대 + 애니메이션만 든 Mixamo FBX 등)의 애니메이션을 캐릭터 장면에 뼈 이름으로 맞춰 붙인다.
/// 가정: 두 뼈대는 같은 뼈대(예: 둘 다 Mixamo)라 뼈마다 로컬 축과 쉬는 자세 방향 규칙이 같다 — 그래서 회전 키는 그대로 옮긴다.
/// 이동은 맨 위 뼈(Mixamo의 Hips) 하나만 옮기고, 키 크기 차이(골반 높이 비)와 쉬는 자세 위치 차이를 맞춘다. 크기 키는 버린다.
/// </summary>
public static class ModelAnimationImport
{
    private const float Epsilon = 1e-6f;
    private const int UnmatchedNamesShown = 5;

    /// <summary>뼈 이름 맞추기용 열쇠: 마지막 ':' 또는 '|' 뒤만 남기고 앞뒤 공백을 지운 소문자("mixamorig1:Hips", "Armature|mixamorig:Hips" → "hips").</summary>
    public static string BoneKey(string nodeName)
    {
        int cut = nodeName.LastIndexOfAny([':', '|']);
        return nodeName[(cut + 1)..].Trim().ToLowerInvariant();
    }

    /// <summary>
    /// <paramref name="source"/>의 애니메이션을 모두 <paramref name="target"/> 뼈에 맞춰 옮긴 새 장면을 만든다(<paramref name="target"/>은 그대로).
    /// 이름은 원본 클립이 하나면 <paramref name="label"/>, 여럿이면 "label: 클립 이름"이고, 이미 있는 이름과 겹치면 " 2", " 3"을 붙인다.
    /// </summary>
    /// <exception cref="InvalidDataException">원본에 애니메이션이 없거나, 움직이는 뼈 중 캐릭터와 이름이 맞는 것이 하나도 없을 때.</exception>
    public static AnimationImportResult Attach(ModelScene target, ModelScene source, string label)
    {
        var warnings = new List<string>();
        var tNodes = target.Nodes;
        var sNodes = source.Nodes;
        int tn = tNodes.Count, sn = sNodes.Count;

        // 캐릭터 쪽 열쇠 → 노드. 같은 열쇠가 여럿이면 스킨의 뼈를 고르고, 나머지는 알림과 함께 무시한다.
        var isJoint = new bool[tn];
        foreach (var skin in target.Skins)
            foreach (int j in skin.Joints)
                if ((uint)j < (uint)tn) isJoint[j] = true;
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < tn; i++)
        {
            string key = BoneKey(tNodes[i].Name);
            if (key.Length == 0) continue;
            if (!byKey.TryGetValue(key, out int prev)) { byKey[key] = i; continue; }
            if (isJoint[i] && !isJoint[prev]) byKey[key] = i;
            if (duplicateKeys.Add(key)) warnings.Add(CoreStrings.FormatAnimImport_Warning_DuplicateBone(bone: tNodes[i].Name));
        }

        // 원본 노드 → 캐릭터 노드(−1 = 없음). 한 캐릭터 노드는 원본 노드 하나에만(먼저 온 것).
        var map = new int[sn];
        var claimed = new bool[tn];
        for (int j = 0; j < sn; j++)
        {
            map[j] = -1;
            string key = BoneKey(sNodes[j].Name);
            if (key.Length == 0 || !byKey.TryGetValue(key, out int i) || claimed[i]) continue;
            claimed[i] = true;
            map[j] = i;
        }

        // 움직임이 있는 원본 뼈 세기.
        var animated = new bool[sn];
        bool anyChannel = false;
        foreach (var anim in source.Animations)
            foreach (var c in anim.Channels)
                if ((uint)c.Node < (uint)sn) { animated[c.Node] = true; anyChannel = true; }
        if (!anyChannel) throw new InvalidDataException(CoreStrings.AnimImport_NoAnimations);
        int sourceBones = 0, matchedBones = 0;
        var unmatched = new List<string>();
        for (int j = 0; j < sn; j++)
        {
            if (!animated[j]) continue;
            sourceBones++;
            if (map[j] >= 0) matchedBones++;
            else unmatched.Add(sNodes[j].Name);
        }
        if (matchedBones == 0) throw new InvalidDataException(CoreStrings.AnimImport_NoMatchingBones);
        if (unmatched.Count > 0)
        {
            string names = string.Join(", ", unmatched.Count > UnmatchedNamesShown ? unmatched.GetRange(0, UnmatchedNamesShown) : unmatched);
            if (unmatched.Count > UnmatchedNamesShown) names += ", …";
            warnings.Add(CoreStrings.FormatAnimImport_Warning_UnmatchedBones(count: unmatched.Count, bones: names));
        }

        var tWorld = RestWorlds(tNodes);
        var sWorld = RestWorlds(sNodes);
        var depth = Depths(sNodes);
        var mappings = new Dictionary<int, TranslationMapping>();   // 원본 노드 → 이동 키 옮기는 법
        var matrixWarned = new bool[tn];

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in target.Animations) usedNames.Add(a.Name);
        var animations = new List<ModelAnimation>(target.Animations.Count + source.Animations.Count);
        animations.AddRange(target.Animations);
        int added = 0;
        bool single = source.Animations.Count == 1;

        foreach (var anim in source.Animations)
        {
            // 이동을 옮길 뼈: 이동 키가 있고 캐릭터에 맞는 뼈 중 가장 위(깊이가 가장 얕은) 것.
            int root = -1;
            foreach (var c in anim.Channels)
                if (c.Path == AnimationPath.Translation && (uint)c.Node < (uint)sn && map[c.Node] >= 0
                    && (root < 0 || depth[c.Node] < depth[root]))
                    root = c.Node;

            var channels = new List<AnimationChannel>();
            foreach (var c in anim.Channels)
            {
                if ((uint)c.Node >= (uint)sn || map[c.Node] < 0 || c.Path == AnimationPath.Scale) continue;
                if (c.Path == AnimationPath.Translation && c.Node != root) continue;
                int t = map[c.Node];
                if (tNodes[t].Matrix != null)
                {
                    if (!matrixWarned[t])
                    {
                        matrixWarned[t] = true;
                        warnings.Add(CoreStrings.FormatAnimImport_Warning_MatrixNode(bone: tNodes[t].Name));
                    }
                    continue;
                }
                if (c.Path == AnimationPath.Rotation)
                {
                    channels.Add(new AnimationChannel { Node = t, Path = c.Path, Interpolation = c.Interpolation, Times = c.Times, Values = c.Values });
                    continue;
                }
                if (!mappings.TryGetValue(c.Node, out var m))
                    mappings[c.Node] = m = TranslationMapping.Of(c.Node, t, sNodes, tNodes, sWorld, tWorld, map);
                channels.Add(new AnimationChannel { Node = t, Path = c.Path, Interpolation = c.Interpolation, Times = c.Times, Values = m.Apply(c.Values, c.Interpolation) });
            }

            string name = single ? label : label + ": " + anim.Name;
            if (channels.Count == 0)
            {
                warnings.Add(CoreStrings.FormatAnimImport_Warning_EmptyClip(clip: name));
                continue;
            }
            name = Unique(name, usedNames);
            animations.Add(new ModelAnimation { Name = name, Channels = channels });
            added++;
        }

        var scene = new ModelScene
        {
            Name = target.Name,
            Nodes = target.Nodes,
            Roots = target.Roots,
            Meshes = target.Meshes,
            Materials = target.Materials,
            Skins = target.Skins,
            Animations = animations,
            Warnings = target.Warnings,
        };
        return new AnimationImportResult(scene, added, matchedBones, sourceBones, warnings);
    }

    /// <summary>겹치지 않는 이름: 이미 있으면 " 2", " 3"…을 붙인다. 고른 이름은 <paramref name="used"/>에 더한다.</summary>
    private static string Unique(string name, HashSet<string> used)
    {
        string result = name;
        for (int k = 2; used.Contains(result); k++) result = name + " " + k;
        used.Add(result);
        return result;
    }

    /// <summary>
    /// 원본 뼈의 이동 키 → 캐릭터 뼈의 이동 키: 캐릭터 쉬는 위치 + (키 − 원본 쉬는 위치)를 골반 높이 비만큼 늘린 것.
    /// 차이는 원본 부모 좌표 → 월드(비만큼 늘림) → 캐릭터 부모 좌표로 옮긴다(부모의 크기·방향이 달라도 맞음).
    /// </summary>
    private readonly struct TranslationMapping(Vector3 sourceRest, Vector3 targetRest, Matrix4x4 delta)
    {
        public static TranslationMapping Of(int s, int t, IReadOnlyList<ModelNode> sNodes, IReadOnlyList<ModelNode> tNodes,
            Matrix4x4[] sWorld, Matrix4x4[] tWorld, int[] map)
        {
            var sParent = Linear(ParentWorld(sNodes, sWorld, s));
            var tParent = Linear(ParentWorld(tNodes, tWorld, t));
            var sRest = RestTranslation(sNodes[s]);
            var tRest = RestTranslation(tNodes[t]);

            // 골반 높이 = 그 뼈의 월드 Y − 맞춘 뼈들 중 가장 낮은 월드 Y(양쪽 같은 뼈 묶음으로).
            float sMin = float.PositiveInfinity, tMin = float.PositiveInfinity;
            for (int j = 0; j < map.Length; j++)
            {
                if (map[j] < 0) continue;
                sMin = MathF.Min(sMin, sWorld[j].M42);
                tMin = MathF.Min(tMin, tWorld[map[j]].M42);
            }
            float sHeight = sWorld[s].M42 - sMin, tHeight = tWorld[t].M42 - tMin;
            float ratio = 1;
            if (sHeight > Epsilon && tHeight > Epsilon && float.IsFinite(sHeight) && float.IsFinite(tHeight))
                ratio = tHeight / sHeight;
            else
            {
                // 다음: 쉬는 이동 길이의 비(월드 크기로).
                float sLen = Vector3.TransformNormal(sRest, sParent).Length();
                float tLen = Vector3.TransformNormal(tRest, tParent).Length();
                if (sLen > Epsilon && tLen > Epsilon && float.IsFinite(sLen) && float.IsFinite(tLen)) ratio = tLen / sLen;
            }

            var delta = Matrix4x4.Invert(tParent, out var tInv) ? sParent * tInv * ratio : Matrix4x4.CreateScale(ratio);
            return new TranslationMapping(sRest, tRest, delta);
        }

        public float[] Apply(float[] values, AnimationInterpolation interpolation)
        {
            var result = new float[values.Length];
            bool cubic = interpolation == AnimationInterpolation.CubicSpline;
            int count = values.Length / 3;
            for (int k = 0; k < count; k++)
            {
                int o = k * 3;
                var v = new Vector3(values[o], values[o + 1], values[o + 2]);
                // 3차 곡선의 접선([들어오는 접선, 값, 나가는 접선] 중 0·2번째)은 방향이라 늘리기만 한다.
                var r = cubic && k % 3 != 1
                    ? Vector3.TransformNormal(v, delta)
                    : targetRest + Vector3.TransformNormal(v - sourceRest, delta);
                result[o] = r.X; result[o + 1] = r.Y; result[o + 2] = r.Z;
            }
            return result;
        }
    }

    private static Vector3 RestTranslation(ModelNode node) => node.Matrix is { } m ? m.Translation : node.Translation;

    private static Matrix4x4 ParentWorld(IReadOnlyList<ModelNode> nodes, Matrix4x4[] world, int i)
    {
        int p = nodes[i].Parent;
        return (uint)p < (uint)world.Length ? world[p] : Matrix4x4.Identity;
    }

    private static Matrix4x4 Linear(Matrix4x4 m)
    {
        m.M41 = 0; m.M42 = 0; m.M43 = 0;
        return m;
    }

    /// <summary>쉬는 자세의 월드 행렬(월드 = 로컬 × 부모 월드, <see cref="ModelPose"/>와 같은 규칙). 부모 고리는 끊어 단위 행렬 부모로 본다.</summary>
    private static Matrix4x4[] RestWorlds(IReadOnlyList<ModelNode> nodes)
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
                var node = nodes[idx];
                var local = node.Matrix ?? Matrix4x4.CreateScale(node.Scale) * Matrix4x4.CreateFromQuaternion(Normalize(node.Rotation)) * Matrix4x4.CreateTranslation(node.Translation);
                parent = local * parent;
                world[idx] = parent;
                state[idx] = 2;
            }
        }
        return world;
    }

    /// <summary>노드마다 부모 고리 깊이(맨 위 = 0). 부모 고리는 노드 수에서 끊는다.</summary>
    private static int[] Depths(IReadOnlyList<ModelNode> nodes)
    {
        int n = nodes.Count;
        var depth = new int[n];
        for (int i = 0; i < n; i++)
        {
            int d = 0;
            for (int k = nodes[i].Parent; (uint)k < (uint)n && d < n; k = nodes[k].Parent) d++;
            depth[i] = d;
        }
        return depth;
    }

    private static Quaternion Normalize(Quaternion q)
    {
        float len = q.Length();
        return len > 1e-12f && float.IsFinite(len) ? q * (1 / len) : Quaternion.Identity;
    }
}
