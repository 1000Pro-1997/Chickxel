using System.Globalization;
using System.Text;

namespace Chickxel.Core.IO;

/// <summary>
/// Unity 네이티브 에셋(YAML 직렬화) 쓰기: 프리팹(<c>.prefab</c>), 애니메이션 클립(<c>.anim</c>), Animator Controller(<c>.controller</c>)와 그 <c>.meta</c>.
/// <para>
/// 형식은 Unity 6(6000.6)이 직접 만든 파일과 같은 꼴이다(필드가 빠지면 Unity가 기본값으로 읽고, 옛 버전은 모르는 필드를 무시한다).
/// 클립의 런타임 바인딩(<c>m_ClipBindingConstant</c>)은 비워 둔다 — 편집기가 곡선에서 다시 만든다(배치 모드로 Animator 재생을 확인함).
/// 스프라이트는 시트 <c>.meta</c>의 guid와 internalID로 가리킨다(<see cref="SpriteLibraryAssetWriter.SpriteRef"/>).
/// 오브젝트 fileID는 경로·종류에서 계산해 다시 내보내도 같다(씬에서 자식 오브젝트를 가리킨 참조가 끊기지 않게).
/// </para>
/// </summary>
public static class UnityAssetWriter
{
    /// <summary>내장 렌더 파이프라인의 Sprites-Default 머티리얼.</summary>
    public const string BuiltInSpriteMaterial = "{fileID: 10754, guid: 0000000000000000f000000000000000, type: 0}";

    /// <summary>URP의 Sprite-Lit-Default 머티리얼(com.unity.render-pipelines.universal).</summary>
    public const string UrpSpriteMaterial = "{fileID: 2100000, guid: a97c105638bdf8b4a8650670310a4cd3, type: 2}";

    /// <summary>프리팹 오브젝트 하나: 이름, 켜짐, 부모 기준 위치(유닛), SpriteRenderer(있으면 스프라이트·정렬 순서), 자식.</summary>
    public sealed record Node(string Name, bool Active, float X, float Y, bool HasRenderer, SpriteLibraryAssetWriter.SpriteRef? Sprite, int SortingOrder, IReadOnlyList<Node> Children);

    /// <summary>스프라이트 곡선: 오브젝트 경로(맨 위 기준, <c>부모/자식/…</c>, 맨 위면 빈 문자열)와 시각(초)마다 스프라이트(null = 비움).</summary>
    public sealed record SpriteCurve(string Path, IReadOnlyList<(float Time, SpriteLibraryAssetWriter.SpriteRef? Sprite)> Keys);

    /// <summary>위치 곡선(계단): 오브젝트 경로와 시각마다 부모 기준 위치(유닛).</summary>
    public sealed record PositionCurve(string Path, IReadOnlyList<(float Time, float X, float Y)> Keys);

    /// <summary>클립 하나: 이름(파일 이름과 같게), 반복, 초당 샘플 수, 곡선.</summary>
    public sealed record Clip(string Name, bool Loop, float SampleRate, IReadOnlyList<SpriteCurve> Sprites, IReadOnlyList<PositionCurve> Positions);

    /// <summary>Controller 상태 하나: 이름과 클립 <c>.anim</c>의 guid.</summary>
    public sealed record State(string Name, string ClipGuid);

    // ───────────── 프리팹 ─────────────

    /// <summary>
    /// 프리팹 YAML. 맨 위 오브젝트에 <paramref name="controllerGuid"/>가 있으면 그 Controller를 쓰는 Animator를 단다.
    /// </summary>
    public static string GeneratePrefab(Node root, string material, string? controllerGuid)
    {
        var sb = new StringBuilder();
        sb.Append("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
        WriteNode(sb, root, "", null, material, controllerGuid);
        return sb.ToString();
    }

    private static void WriteNode(StringBuilder sb, Node node, string path, long? father, string material, string? controllerGuid)
    {
        void L(string s) => sb.Append(s).Append('\n');
        long go = Id("go", path), tr = Id("transform", path), sr = Id("renderer", path), an = Id("animator", path);
        string ChildPath(Node c) => path.Length == 0 ? c.Name : path + "/" + c.Name;
        bool animator = father is null && controllerGuid is not null;

        L($"--- !u!1 &{N(go)}");
        L("GameObject:");
        Header(L);
        L("  serializedVersion: 6");
        L("  m_Component:");
        L($"  - component: {{fileID: {N(tr)}}}");
        if (node.HasRenderer) L($"  - component: {{fileID: {N(sr)}}}");
        if (animator) L($"  - component: {{fileID: {N(an)}}}");
        L("  m_Layer: 0");
        L($"  m_Name: {MiniYaml.QuoteIfNeeded(node.Name)}");
        L("  m_TagString: Untagged");
        L("  m_Icon: {fileID: 0}");
        L("  m_NavMeshLayer: 0");
        L("  m_StaticEditorFlags: 0");
        L($"  m_IsActive: {(node.Active ? 1 : 0)}");

        L($"--- !u!4 &{N(tr)}");
        L("Transform:");
        Header(L);
        L($"  m_GameObject: {{fileID: {N(go)}}}");
        L("  serializedVersion: 2");
        L("  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}");
        L($"  m_LocalPosition: {{x: {F(node.X)}, y: {F(node.Y)}, z: 0}}");
        L("  m_LocalScale: {x: 1, y: 1, z: 1}");
        L("  m_ConstrainProportionsScale: 0");
        if (node.Children.Count == 0) L("  m_Children: []");
        else
        {
            L("  m_Children:");
            foreach (var c in node.Children) L($"  - {{fileID: {N(Id("transform", ChildPath(c)))}}}");
        }
        L($"  m_Father: {{fileID: {N(father ?? 0)}}}");
        L("  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}");

        if (node.HasRenderer)
        {
            L($"--- !u!212 &{N(sr)}");
            L("SpriteRenderer:");
            L("  serializedVersion: 3");
            Header(L);
            L($"  m_GameObject: {{fileID: {N(go)}}}");
            L("  m_Enabled: 1");
            L("  m_CastShadows: 0");
            L("  m_ReceiveShadows: 0");
            L("  m_DynamicOccludee: 1");
            L("  m_StaticShadowCaster: 0");
            L("  m_MotionVectors: 1");
            L("  m_LightProbeUsage: 1");
            L("  m_ReflectionProbeUsage: 1");
            L("  m_RenderingLayerMask: 1");
            L("  m_RendererPriority: 0");
            L("  m_Materials:");
            L($"  - {material}");
            L("  m_StaticBatchInfo:");
            L("    firstSubMesh: 0");
            L("    subMeshCount: 0");
            L("  m_StaticBatchRoot: {fileID: 0}");
            L("  m_ProbeAnchor: {fileID: 0}");
            L("  m_LightProbeVolumeOverride: {fileID: 0}");
            L("  m_SortingLayerID: 0");
            L("  m_SortingLayer: 0");
            L($"  m_SortingOrder: {node.SortingOrder.ToString(CultureInfo.InvariantCulture)}");
            L("  m_MaskInteraction: 0");
            L($"  m_Sprite: {Reference(node.Sprite)}");
            L("  m_Color: {r: 1, g: 1, b: 1, a: 1}");
            L("  m_FlipX: 0");
            L("  m_FlipY: 0");
            L("  m_DrawMode: 0");
            L("  m_AdaptiveModeThreshold: 0.5");
            L("  m_SpriteTileMode: 0");
            L($"  m_WasSpriteAssigned: {(node.Sprite is null ? 0 : 1)}");
            L("  m_SpriteSortPoint: 0");
        }

        if (animator)
        {
            L($"--- !u!95 &{N(an)}");
            L("Animator:");
            L("  serializedVersion: 7");
            Header(L);
            L($"  m_GameObject: {{fileID: {N(go)}}}");
            L("  m_Enabled: 1");
            L("  m_Avatar: {fileID: 0}");
            L($"  m_Controller: {{fileID: 9100000, guid: {controllerGuid}, type: 2}}");
            L("  m_CullingMode: 0");
            L("  m_UpdateMode: 0");
            L("  m_ApplyRootMotion: 0");
            L("  m_LinearVelocityBlending: 0");
            L("  m_StabilizeFeet: 0");
            L("  m_AnimatePhysics: 0");
            L("  m_WarningMessage: ");
            L("  m_HasTransformHierarchy: 1");
            L("  m_AllowConstantClipSamplingOptimization: 1");
            L("  m_KeepAnimatorStateOnDisable: 0");
            L("  m_WriteDefaultValuesOnDisable: 0");
        }

        foreach (var c in node.Children) WriteNode(sb, c, ChildPath(c), tr, material, null);
    }

    private static void Header(Action<string> L)
    {
        L("  m_ObjectHideFlags: 0");
        L("  m_CorrespondingSourceObject: {fileID: 0}");
        L("  m_PrefabInstance: {fileID: 0}");
        L("  m_PrefabAsset: {fileID: 0}");
    }

    // ───────────── 클립 ─────────────

    /// <summary>
    /// 애니메이션 클립 YAML: 스프라이트 곡선(<c>m_PPtrCurves</c>)과 위치 곡선(<c>m_PositionCurves</c> + 같은 값의 편집기 곡선, 계단).
    /// 클립 길이는 마지막 키 + 한 샘플(Unity 규칙)이라, 끝 시각을 맞추려면 부르는 쪽이 마지막 키를 끝 − 한 샘플에 둔다.
    /// </summary>
    public static string GenerateClip(Clip clip)
    {
        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append('\n');
        float end = 0;
        foreach (var c in clip.Sprites) if (c.Keys.Count > 0) end = Math.Max(end, c.Keys[^1].Time);
        foreach (var c in clip.Positions) if (c.Keys.Count > 0) end = Math.Max(end, c.Keys[^1].Time);
        float stop = end + 1f / clip.SampleRate;

        L("%YAML 1.1");
        L("%TAG !u! tag:unity3d.com,2011:");
        L("--- !u!74 &7400000");
        L("AnimationClip:");
        Header(L);
        L($"  m_Name: {MiniYaml.QuoteIfNeeded(clip.Name)}");
        L("  serializedVersion: 8");
        L("  m_Legacy: 0");
        L("  m_Compressed: 0");
        L("  m_UseHighQualityCurve: 1");
        L("  m_RotationCurves: []");
        L("  m_CompressedRotationCurves: []");
        L("  m_EulerCurves: []");
        if (clip.Positions.Count == 0) L("  m_PositionCurves: []");
        else
        {
            L("  m_PositionCurves:");
            foreach (var c in clip.Positions)
            {
                L("  - curve:");
                L("      serializedVersion: 2");
                L("      m_Curve:");
                foreach (var (t, x, y) in c.Keys)
                {
                    L("      - serializedVersion: 3");
                    L($"        time: {F(t)}");
                    L($"        value: {{x: {F(x)}, y: {F(y)}, z: 0}}");
                    L("        inSlope: {x: Infinity, y: Infinity, z: Infinity}");
                    L("        outSlope: {x: Infinity, y: Infinity, z: Infinity}");
                    L("        tangentMode: 0");
                    L("        weightedMode: 0");
                    L("        inWeight: {x: 0, y: 0, z: 0}");
                    L("        outWeight: {x: 0, y: 0, z: 0}");
                }
                L("      m_PreInfinity: 2");
                L("      m_PostInfinity: 2");
                L("      m_RotationOrder: 4");
                L($"    path: {MiniYaml.QuoteIfNeeded(c.Path)}");
            }
        }
        L("  m_ScaleCurves: []");
        L("  m_FloatCurves: []");
        if (clip.Sprites.Count == 0) L("  m_PPtrCurves: []");
        else
        {
            L("  m_PPtrCurves:");
            foreach (var c in clip.Sprites)
            {
                L("  - serializedVersion: 2");
                L("    curve:");
                foreach (var (t, s) in c.Keys)
                {
                    L($"    - time: {F(t)}");
                    L($"      value: {Reference(s)}");
                }
                L("    attribute: m_Sprite");
                L($"    path: {MiniYaml.QuoteIfNeeded(c.Path)}");
                L("    classID: 212");
                L("    script: {fileID: 0}");
                L("    flags: 2");
            }
        }
        L($"  m_SampleRate: {F(clip.SampleRate)}");
        L("  m_WrapMode: 0");
        L("  m_Bounds:");
        L("    m_Center: {x: 0, y: 0, z: 0}");
        L("    m_Extent: {x: 0, y: 0, z: 0}");
        L("  m_ClipBindingConstant:");
        L("    genericBindings: []");
        L("    pptrCurveMapping: []");
        L("  m_AnimationClipSettings:");
        L("    serializedVersion: 2");
        L("    m_AdditiveReferencePoseClip: {fileID: 0}");
        L("    m_AdditiveReferencePoseTime: 0");
        L("    m_StartTime: 0");
        L($"    m_StopTime: {F(stop)}");
        L("    m_OrientationOffsetY: 0");
        L("    m_Level: 0");
        L("    m_CycleOffset: 0");
        L("    m_HasAdditiveReferencePose: 0");
        L($"    m_LoopTime: {(clip.Loop ? 1 : 0)}");
        L("    m_LoopBlend: 0");
        L("    m_LoopBlendOrientation: 0");
        L("    m_LoopBlendPositionY: 0");
        L("    m_LoopBlendPositionXZ: 0");
        L("    m_KeepOriginalOrientation: 0");
        L("    m_KeepOriginalPositionY: 1");
        L("    m_KeepOriginalPositionXZ: 0");
        L("    m_HeightFromFeet: 0");
        L("    m_Mirror: 0");
        if (clip.Positions.Count == 0) L("  m_EditorCurves: []");
        else
        {
            L("  m_EditorCurves:");
            foreach (var c in clip.Positions)
                foreach (var (axis, pick) in new (string, Func<(float, float, float), float>)[] { ("x", k => k.Item2), ("y", k => k.Item3), ("z", _ => 0f) })
                {
                    L("  - serializedVersion: 2");
                    L("    curve:");
                    L("      serializedVersion: 2");
                    L("      m_Curve:");
                    foreach (var k in c.Keys)
                    {
                        L("      - serializedVersion: 3");
                        L($"        time: {F(k.Time)}");
                        L($"        value: {F(pick(k))}");
                        L("        inSlope: Infinity");
                        L("        outSlope: Infinity");
                        L("        tangentMode: 103");
                        L("        weightedMode: 0");
                        L("        inWeight: 0");
                        L("        outWeight: 0");
                    }
                    L("      m_PreInfinity: 2");
                    L("      m_PostInfinity: 2");
                    L("      m_RotationOrder: 4");
                    L($"    attribute: m_LocalPosition.{axis}");
                    L($"    path: {MiniYaml.QuoteIfNeeded(c.Path)}");
                    L("    classID: 4");
                    L("    script: {fileID: 0}");
                    L("    flags: 16");
                }
        }
        L("  m_EulerEditorCurves: []");
        L("  m_HasGenericRootTransform: 0");
        L("  m_HasMotionFloatCurves: 0");
        L("  m_Events: []");
        return sb.ToString();
    }

    // ───────────── Animator Controller ─────────────

    /// <summary>Animator Controller YAML: 레이어 하나(Base Layer)에 상태마다 클립, 첫 상태가 기본. 상태 사이 전환은 없다(코드·Unity에서 정함).</summary>
    public static string GenerateController(string name, IReadOnlyList<State> states)
    {
        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append('\n');
        long machine = Id("statemachine", name);
        long[] ids = states.Select((s, i) => Id("state", i.ToString(CultureInfo.InvariantCulture) + "/" + s.Name)).ToArray();
        L("%YAML 1.1");
        L("%TAG !u! tag:unity3d.com,2011:");
        for (int i = 0; i < states.Count; i++)
        {
            L($"--- !u!1102 &{N(ids[i])}");
            L("AnimatorState:");
            L("  serializedVersion: 6");
            L("  m_ObjectHideFlags: 1");
            L("  m_CorrespondingSourceObject: {fileID: 0}");
            L("  m_PrefabInstance: {fileID: 0}");
            L("  m_PrefabAsset: {fileID: 0}");
            L($"  m_Name: {MiniYaml.QuoteIfNeeded(states[i].Name)}");
            L("  m_Speed: 1");
            L("  m_CycleOffset: 0");
            L("  m_Transitions: []");
            L("  m_StateMachineBehaviours: []");
            L("  m_Position: {x: 50, y: 50, z: 0}");
            L("  m_IKOnFeet: 0");
            L("  m_WriteDefaultValues: 1");
            L("  m_Mirror: 0");
            L("  m_SpeedParameterActive: 0");
            L("  m_MirrorParameterActive: 0");
            L("  m_CycleOffsetParameterActive: 0");
            L("  m_TimeParameterActive: 0");
            L($"  m_Motion: {{fileID: 7400000, guid: {states[i].ClipGuid}, type: 2}}");
            L("  m_Tag: ");
            L("  m_SpeedParameter: ");
            L("  m_MirrorParameter: ");
            L("  m_CycleOffsetParameter: ");
            L("  m_TimeParameter: ");
        }
        L($"--- !u!1107 &{N(machine)}");
        L("AnimatorStateMachine:");
        L("  serializedVersion: 7");
        L("  m_ObjectHideFlags: 1");
        L("  m_CorrespondingSourceObject: {fileID: 0}");
        L("  m_PrefabInstance: {fileID: 0}");
        L("  m_PrefabAsset: {fileID: 0}");
        L("  m_Name: Base Layer");
        if (states.Count == 0) L("  m_ChildStates: []");
        else
        {
            L("  m_ChildStates:");
            for (int i = 0; i < states.Count; i++)
            {
                L("  - serializedVersion: 1");
                L($"    m_State: {{fileID: {N(ids[i])}}}");
                L($"    m_Position: {{x: {200 + 40 * (i % 2)}, y: {60 * i}, z: 0}}");
            }
        }
        L("  m_ChildStateMachines: []");
        L("  m_AnyStateTransitions: []");
        L("  m_EntryTransitions: []");
        L("  m_StateMachineTransitions: {}");
        L("  m_StateMachineBehaviours: []");
        L("  m_AnyStatePosition: {x: 50, y: 20, z: 0}");
        L("  m_EntryPosition: {x: 50, y: 120, z: 0}");
        L("  m_ExitPosition: {x: 800, y: 120, z: 0}");
        L("  m_ParentStateMachinePosition: {x: 800, y: 20, z: 0}");
        L($"  m_DefaultState: {{fileID: {(states.Count == 0 ? "0" : N(ids[0]))}}}");
        L("--- !u!91 &9100000");
        L("AnimatorController:");
        Header(L);
        L($"  m_Name: {MiniYaml.QuoteIfNeeded(name)}");
        L("  serializedVersion: 6");
        L("  m_AnimatorParameters: []");
        L("  m_AnimatorLayers:");
        L("  - serializedVersion: 5");
        L("    m_Name: Base Layer");
        L($"    m_StateMachine: {{fileID: {N(machine)}}}");
        L("    m_Mask: {fileID: 0}");
        L("    m_Motions: []");
        L("    m_Behaviours: []");
        L("    m_BlendingMode: 0");
        L("    m_SyncedLayerIndex: -1");
        L("    m_DefaultWeight: 0");
        L("    m_IKPass: 0");
        L("    m_SyncedLayerAffectsTiming: 0");
        L("    m_Controller: {fileID: 9100000}");
        L("  m_EvaluateTransitionsOnStart: 1");
        return sb.ToString();
    }

    // ───────────── 메타·쓰기 ─────────────

    /// <summary>네이티브 에셋 메타(클립 7400000, Controller 9100000).</summary>
    public static string NativeMeta(string guid, long mainObjectFileId) =>
        "fileFormatVersion: 2\n" +
        $"guid: {guid}\n" +
        "NativeFormatImporter:\n" +
        "  externalObjects: {}\n" +
        $"  mainObjectFileID: {mainObjectFileId.ToString(CultureInfo.InvariantCulture)}\n" +
        "  userData: \n" +
        "  assetBundleName: \n" +
        "  assetBundleVariant: \n";

    /// <summary>프리팹 메타.</summary>
    public static string PrefabMeta(string guid) =>
        "fileFormatVersion: 2\n" +
        $"guid: {guid}\n" +
        "PrefabImporter:\n" +
        "  externalObjects: {}\n" +
        "  userData: \n" +
        "  assetBundleName: \n" +
        "  assetBundleVariant: \n";

    /// <summary>
    /// <paramref name="path"/>의 guid: <c>.meta</c>가 있으면 그 guid(Unity에서 가리킨 참조 유지), 없으면 새 guid로 <paramref name="meta"/>를 쓴다.
    /// 에셋을 쓰기 전에 불러 서로 가리킬 guid를 먼저 정한다.
    /// </summary>
    public static string EnsureMeta(string path, Func<string, string> meta)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var metaPath = UnityMeta.GetMetaPath(path);
        if (File.Exists(metaPath) && UnityMeta.TryRead(metaPath)?.Guid is { Length: > 0 } guid) return guid;
        guid = UnityMeta.NewGuid();
        AtomicFile.WriteText(metaPath, meta(guid));
        return guid;
    }

    /// <summary>내용이 다를 때만 쓴다(같으면 Unity가 다시 가져오지 않게).</summary>
    public static void WriteIfChanged(string path, string text)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != text) AtomicFile.WriteText(path, text);
    }

    // ───────────── 값 ─────────────

    private static string Reference(SpriteLibraryAssetWriter.SpriteRef? sprite)
        => sprite is null || sprite.Guid.Length == 0
            ? "{fileID: 0}"
            : $"{{fileID: {sprite.FileId.ToString(CultureInfo.InvariantCulture)}, guid: {sprite.Guid}, type: 3}}";

    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static string F(float v)
    {
        if (float.IsPositiveInfinity(v)) return "Infinity";
        if (MathF.Abs(v) < 1e-7f) return "0";
        return v.ToString("0.#######", CultureInfo.InvariantCulture);
    }

    /// <summary>경로·종류에서 계산한 오브젝트 fileID(FNV-1a 64비트, 양수, 작은 예약 값 피함).</summary>
    private static long Id(string kind, string path)
    {
        ulong h = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(kind + "\n" + path))
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return (long)(h & 0x3FFFFFFFFFFFFFFFUL) | 0x1000000000L;
    }
}
