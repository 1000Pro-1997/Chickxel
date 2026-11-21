using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Scene3D;

namespace Chickxel.Core.Tests;

public class FbxReaderTests : IDisposable
{
    private const long Second = FbxReader.TicksPerSecond;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chickxel-fbx-tests-" + Guid.NewGuid().ToString("N"));

    public FbxReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // ---- 시험용 FBX 바이너리 쓰기 ----

    /// <summary>노드 기록 하나: 이름, 속성(short·bool·int·float·double·long·string·byte[]·배열·<see cref="Zipped"/>), 아래 노드.</summary>
    private sealed record FbxRecord(string Name, object[] Props, FbxRecord[] Children);

    /// <summary>zlib으로 압축해 쓸 배열(encoding 1).</summary>
    private sealed record Zipped(Array Values);

    private static FbxRecord Leaf(string name, params object[] props) => new(name, props, []);
    private static FbxRecord Node(string name, object[] props, params FbxRecord[] children) => new(name, props, children);
    private static FbxRecord Props(params FbxRecord[] ps) => Node("Properties70", [], ps);
    private static FbxRecord P(string name, params object[] values) => Leaf("P", [name, "", "", "A", .. values]);

    private static byte[] Fbx(int version, params FbxRecord[] top)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("Kaydara FBX Binary  \0"u8);
        w.Write((byte)0x1A);
        w.Write((byte)0);
        w.Write((uint)version);
        bool wide = version >= 7500;
        foreach (var n in top) Write(w, n, wide);
        w.Write(new byte[wide ? 25 : 13]);   // 맨 위 목록 끝
        w.Write(new byte[16]);               // 꼬리말 자리(읽지 않음)
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] Fbx(params FbxRecord[] top) => Fbx(7400, top);

    private static void Write(BinaryWriter w, FbxRecord n, bool wide)
    {
        var s = w.BaseStream;
        long start = s.Position;
        w.Write(new byte[wide ? 24 : 12]);
        var name = Encoding.ASCII.GetBytes(n.Name);
        w.Write((byte)name.Length);
        w.Write(name);
        long propStart = s.Position;
        foreach (var p in n.Props) WriteProperty(w, p);
        long propEnd = s.Position;
        if (n.Children.Length > 0)
        {
            foreach (var c in n.Children) Write(w, c, wide);
            w.Write(new byte[wide ? 25 : 13]);
        }
        long end = s.Position;
        s.Position = start;
        if (wide)
        {
            w.Write((ulong)end);
            w.Write((ulong)n.Props.Length);
            w.Write((ulong)(propEnd - propStart));
        }
        else
        {
            w.Write((uint)end);
            w.Write((uint)n.Props.Length);
            w.Write((uint)(propEnd - propStart));
        }
        s.Position = end;
    }

    private static void WriteProperty(BinaryWriter w, object p)
    {
        switch (p)
        {
            case short v: w.Write((byte)'Y'); w.Write(v); break;
            case bool v: w.Write((byte)'C'); w.Write((byte)(v ? 1 : 0)); break;
            case int v: w.Write((byte)'I'); w.Write(v); break;
            case float v: w.Write((byte)'F'); w.Write(v); break;
            case double v: w.Write((byte)'D'); w.Write(v); break;
            case long v: w.Write((byte)'L'); w.Write(v); break;
            case string v:
            {
                var bytes = Encoding.UTF8.GetBytes(v);
                w.Write((byte)'S');
                w.Write((uint)bytes.Length);
                w.Write(bytes);
                break;
            }
            case byte[] v: w.Write((byte)'R'); w.Write((uint)v.Length); w.Write(v); break;
            case Zipped z: WriteArray(w, z.Values, zip: true); break;
            case Array a: WriteArray(w, a, zip: false); break;
            default: throw new ArgumentException(p.GetType().Name);
        }
    }

    private static void WriteArray(BinaryWriter w, Array a, bool zip)
    {
        (char type, byte[] raw) = a switch
        {
            double[] d => ('d', MemoryMarshal.AsBytes(d.AsSpan()).ToArray()),
            float[] f => ('f', MemoryMarshal.AsBytes(f.AsSpan()).ToArray()),
            int[] i => ('i', MemoryMarshal.AsBytes(i.AsSpan()).ToArray()),
            long[] l => ('l', MemoryMarshal.AsBytes(l.AsSpan()).ToArray()),
            bool[] b => ('b', b.Select(x => (byte)(x ? 1 : 0)).ToArray()),
            _ => throw new ArgumentException(a.GetType().Name),
        };
        if (zip)
        {
            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
            raw = ms.ToArray();
        }
        w.Write((byte)type);
        w.Write((uint)a.Length);
        w.Write(zip ? 1u : 0u);
        w.Write((uint)raw.Length);
        w.Write(raw);
    }

    // ---- 장면 조각 ----

    private static FbxRecord Settings(int up = 1, int upSign = 1, int front = 2, int frontSign = 1, int coord = 0, int coordSign = 1, double unit = 100)
        => Node("GlobalSettings", [], Leaf("Version", 1000), Props(
            P("UpAxis", up), P("UpAxisSign", upSign), P("FrontAxis", front), P("FrontAxisSign", frontSign),
            P("CoordAxis", coord), P("CoordAxisSign", coordSign), P("UnitScaleFactor", unit)));

    private static FbxRecord Objects(params FbxRecord[] objects) => Node("Objects", [], objects);
    private static FbxRecord Connections(params FbxRecord[] cs) => Node("Connections", [], cs);

    /// <summary>C: OO(자식 → 부모) 또는 OP(자식 → 부모의 속성).</summary>
    private static FbxRecord C(long child, long parent, string? property = null)
        => property == null ? Leaf("C", "OO", child, parent) : Leaf("C", "OP", child, parent, property);

    private static FbxRecord Model(long id, string name, string type, params FbxRecord[] props)
        => Node("Model", [id, name + "\0\u0001Model", type], Leaf("Version", 232), Props(props));

    private static FbxRecord Mesh(long id, object vertices, int[] polygons, params FbxRecord[] layers)
        => Node("Geometry", [id, "mesh\0\u0001Geometry", "Mesh"], [Leaf("Vertices", vertices), Leaf("PolygonVertexIndex", polygons), .. layers]);

    private static FbxRecord Vec(string name, double x, double y, double z) => P(name, x, y, z);

    /// <summary>XY 평면의 반시계 삼각형(앞면 +Z): (0,0,0), (1,0,0), (0,1,0).</summary>
    private static FbxRecord Triangle(long id) => Mesh(id, new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, [0, 1, ~2]);

    /// <summary>열 우선 이동 행렬(FBX Transform·TransformLink).</summary>
    private static double[] Translate(double x, double y, double z) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];

    private static FbxRecord Stack(long id, string name, long localStart = 0)
        => Node("AnimationStack", [id, name + "\0\u0001AnimStack", ""], Props(P("LocalStart", localStart)));

    private static FbxRecord Layer(long id) => Leaf("AnimationLayer", id, "Layer0\0\u0001AnimLayer", "");

    private static FbxRecord CurveNode(long id, string name)
        => Node("AnimationCurveNode", [id, name + "\0\u0001AnimCurveNode", ""], Props(P("d|X", 0.0), P("d|Y", 0.0), P("d|Z", 0.0)));

    /// <summary>곡선 하나. 보간 표시 기본값 0x2108 = Mixamo가 쓰는 3차(직선으로 읽음).</summary>
    private static FbxRecord Curve(long id, long[] ticks, float[] values, int flags = 0x2108)
        => Node("AnimationCurve", [id, "\0\u0001AnimCurve", ""], Leaf("Default", 0.0), Leaf("KeyVer", 4008),
            Leaf("KeyTime", ticks), Leaf("KeyValueFloat", values), Leaf("KeyAttrFlags", new[] { flags }), Leaf("KeyAttrRefCount", new[] { ticks.Length }));

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void Near(Vector3 expected, Vector3 actual, float eps = 1e-4f)
        => Assert.True(Vector3.Distance(expected, actual) < eps, $"expected {expected}, actual {actual}");

    // ---- 시험 ----

    [Fact]
    public void Triangle_with_node_translation_is_placed_in_world_by_pose()
    {
        var path = WriteFile("tri.fbx", Fbx(Settings(),
            Objects(Model(10, "Tri", "Mesh", Vec("Lcl Translation", 1, 2, 3)), Triangle(20)),
            Connections(C(10, 0), C(20, 10))));

        var scene = ModelLoader.Load(path);

        Assert.Equal("tri", scene.Name);
        var node = Assert.Single(scene.Nodes);
        Assert.Equal("Tri", node.Name);
        Assert.Equal([0], scene.Roots);
        var posed = Assert.Single(ModelPose.Pose(scene).Primitives);
        Near(new Vector3(1, 2, 3), posed.Positions[posed.Indices[0]]);
        Near(new Vector3(2, 2, 3), posed.Positions[posed.Indices[1]]);
        Near(new Vector3(1, 3, 3), posed.Positions[posed.Indices[2]]);
        Near(Vector3.UnitZ, posed.Normals[0]);   // 반시계 그대로 — 앞면 +Z
        Assert.Equal(-1, Assert.Single(Assert.Single(scene.Meshes).Primitives).Material);
    }

    [Fact]
    public void Default_unit_is_centimeters_converted_to_meters()
    {
        var bytes = Fbx(
            Objects(Model(10, "Tri", "Mesh", Vec("Lcl Translation", 100, 0, 0)), Mesh(20, new double[] { 50, 0, 0, 60, 0, 0, 50, 10, 0 }, [0, 1, ~2])),
            Connections(C(10, 0), C(20, 10)));

        var scene = FbxReader.Read(bytes, null, "cm");

        Near(new Vector3(1, 0, 0), scene.Nodes[0].Translation);
        var posed = Assert.Single(ModelPose.Pose(scene).Primitives);
        Near(new Vector3(1.5f, 0, 0), posed.Positions[posed.Indices[0]]);
    }

    [Fact]
    public void Quad_reads_fan_triangles_index_to_direct_uvs_and_polygon_vertex_normals()
    {
        // 사각형 하나 + 두 꼭짓점을 같은 UV로 나눠 쓰는 삼각형 하나. 제어점은 압축 배열.
        var vertices = new Zipped(new double[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 2, 0.5, 0 });
        int[] polygons = [0, 1, 2, ~3, 1, 4, ~2];
        var normals = Node("LayerElementNormal", [0], Leaf("Version", 102), Leaf("MappingInformationType", "ByPolygonVertex"),
            Leaf("ReferenceInformationType", "Direct"), Leaf("Normals", Enumerable.Range(0, 7).SelectMany(_ => new double[] { 0, 0, 1 }).ToArray()));
        var uvs = Node("LayerElementUV", [0], Leaf("Version", 101), Leaf("MappingInformationType", "ByPolygonVertex"),
            Leaf("ReferenceInformationType", "IndexToDirect"), Leaf("UV", new double[] { 9, 9, 0, 0, 1, 0, 1, 1, 0, 1, 2, 0.5 }),
            Leaf("UVIndex", new[] { 1, 2, 3, 4, 2, 5, 3 }));
        var bytes = Fbx(Settings(),
            Objects(Model(10, "Quad", "Mesh"), Mesh(20, vertices, polygons, normals, uvs)),
            Connections(C(10, 0), C(20, 10)));

        var scene = FbxReader.Read(bytes, null, "quad");

        var prim = Assert.Single(Assert.Single(scene.Meshes).Primitives);
        Assert.Equal(5, prim.Positions.Length);   // 7개 다각형 꼭짓점 중 같은 값 둘을 합침
        Assert.Equal([0, 1, 2, 0, 2, 3, 1, 4, 2], prim.Indices);
        Assert.NotNull(prim.Uvs);
        Assert.Equal(new Vector2(0, 1), prim.Uvs[0]);   // FBX v는 아래에서 위 → (u, 1 − v)
        Assert.Equal(new Vector2(1, 1), prim.Uvs[1]);
        Assert.Equal(new Vector2(1, 0), prim.Uvs[2]);
        Assert.Equal(new Vector2(0, 0), prim.Uvs[3]);
        Assert.Equal(new Vector2(2, 0.5f), prim.Uvs[4]);
        Assert.NotNull(prim.Normals);
        Assert.All(prim.Normals, n => Near(Vector3.UnitZ, n));
        Near(new Vector3(2, 0.5f, 0), prim.Positions[4]);
    }

    [Fact]
    public void Z_up_file_is_converted_to_Y_up()
    {
        // 3ds Max식 Z 위·앞 −Y, 7.5 판(64비트 위치). 노드는 위 축(Z)으로 90도 돌고 위로 5만큼 올라감.
        var bytes = Fbx(7500, Settings(up: 2, front: 1, frontSign: -1, coord: 0),
            Objects(Model(10, "Tri", "Mesh", Vec("Lcl Translation", 0, 0, 5), Vec("Lcl Rotation", 0, 0, 90)), Triangle(20)),
            Connections(C(10, 0), C(20, 10)));

        var scene = FbxReader.Read(bytes, null, "zup");

        var node = Assert.Single(scene.Nodes);
        Near(new Vector3(0, 5, 0), node.Translation);
        var prim = Assert.Single(Assert.Single(scene.Meshes).Primitives);
        Near(new Vector3(0, 0, -1), prim.Positions[2]);   // 파일 +Y(앞의 반대) → 장면 −Z
        var posed = Assert.Single(ModelPose.Pose(scene).Primitives);
        // (1,0,0)을 파일 Z로 90도 → 파일 (0,1,0) → 장면 (0,0,−1), 그리고 위로 5.
        Near(new Vector3(0, 5, -1), posed.Positions[posed.Indices[1]]);
        // 파일의 앞면 +Z(위) → 장면 +Y, 감기 방향은 그대로.
        Near(Vector3.UnitY, posed.Normals[0]);
    }

    [Fact]
    public void Left_handed_axes_mirror_positions_and_keep_front_faces()
    {
        var bytes = Fbx(Settings(coordSign: -1),
            Objects(Model(10, "Tri", "Mesh"), Triangle(20)),
            Connections(C(10, 0), C(20, 10)));

        var scene = FbxReader.Read(bytes, null, "lh");

        var posed = Assert.Single(ModelPose.Pose(scene).Primitives);
        Assert.Contains(posed.Positions, p => Vector3.Distance(p, new Vector3(-1, 0, 0)) < 1e-4f);
        Near(Vector3.UnitZ, posed.Normals[0]);   // 거울이 되어도 감기 방향을 뒤집어 +Z 면은 +Z
    }

    /// <summary>
    /// 뼈 둘(Hips 원점, Spine (0,1,0))과 삼각형 둘(아래는 Hips, 위는 Spine에 무게 1). Spine은 0초 0도 → 1초 Z 90도.
    /// </summary>
    private static byte[] TwoBoneSkin()
    {
        var mesh = Mesh(30, new double[] { 0, 0, 0, 1, 0, 0, 0, 0.5, 0, 0, 2, 0, 1, 2, 0, 0, 2.5, 0 }, [0, 1, ~2, 3, 4, ~5]);
        return Fbx(Settings(),
            Objects(
                Model(10, "mixamorig:Hips", "LimbNode"),
                Model(11, "mixamorig:Spine", "LimbNode", Vec("Lcl Translation", 0, 1, 0)),
                Model(20, "Body", "Mesh"),
                mesh,
                Node("Deformer", [40L, "Skin\0\u0001Deformer", "Skin"], Leaf("Version", 101)),
                Node("Deformer", [41L, "Cluster Hips\0\u0001SubDeformer", "Cluster"], Leaf("Indexes", new[] { 0, 1, 2 }), Leaf("Weights", new double[] { 1, 1, 1 }),
                    Leaf("Transform", Translate(0, 0, 0)), Leaf("TransformLink", Translate(0, 0, 0))),
                Node("Deformer", [42L, "Cluster Spine\0\u0001SubDeformer", "Cluster"], Leaf("Indexes", new[] { 3, 4, 5 }), Leaf("Weights", new double[] { 0.5, 0.5, 0.5 }),
                    Leaf("Transform", Translate(0, -1, 0)), Leaf("TransformLink", Translate(0, 1, 0))),
                Stack(50, "mixamo.com"),
                Layer(51),
                CurveNode(52, "R"),
                Curve(53, [0, Second], [0, 90])),
            Connections(
                C(10, 0), C(11, 10), C(20, 0), C(30, 20),
                C(40, 30), C(41, 40), C(42, 40), C(10, 41), C(11, 42),
                C(51, 50), C(52, 51), C(52, 11, "Lcl Rotation"), C(53, 52, "d|Z")));
    }

    [Fact]
    public void Two_bone_skin_rest_pose_matches_mesh_and_animation_moves_only_weighted_vertices()
    {
        var scene = FbxReader.Read(TwoBoneSkin(), null, "dance");

        var skin = Assert.Single(scene.Skins);
        Assert.Equal(["mixamorig:Hips", "mixamorig:Spine"], skin.Joints.Select(j => scene.Nodes[j].Name));
        var body = scene.Nodes.Single(n => n.Name == "Body");
        Assert.Equal(0, body.Skin);
        var prim = Assert.Single(scene.Meshes[body.Mesh].Primitives);
        Assert.NotNull(prim.Weights);
        Assert.Equal(1f, prim.Weights[12..16].Sum(), 5);   // 점 3의 0.5 무게 하나도 합 1로 맞춤

        var rest = Assert.Single(ModelPose.Pose(scene).Primitives);
        for (int i = 0; i < prim.Positions.Length; i++) Near(prim.Positions[i], rest.Positions[i]);

        var animation = Assert.Single(scene.Animations);
        Assert.Equal("dance", animation.Name);   // "mixamo.com" → 파일 이름
        Assert.Equal(1f, animation.Duration, 5);
        var posed = Assert.Single(ModelPose.Pose(scene, 0, 1).Primitives);
        for (int i = 0; i < 3; i++) Near(prim.Positions[i], posed.Positions[i]);   // Hips 쪽은 그대로
        // Spine 쪽은 (0,1,0)을 중심으로 Z 90도: (0,2,0) → (−1,1,0), (1,2,0) → (−1,2,0), (0,2.5,0) → (−1.5,1,0).
        Near(new Vector3(-1, 1, 0), posed.Positions[3]);
        Near(new Vector3(-1, 2, 0), posed.Positions[4]);
        Near(new Vector3(-1.5f, 1, 0), posed.Positions[5]);
    }

    [Fact]
    public void Rotation_curve_with_PreRotation_and_KeyTime_seconds()
    {
        var bytes = Fbx(Settings(),
            Objects(
                Model(10, "Arm", "Mesh", Vec("PreRotation", 90, 0, 0)),
                Mesh(20, new double[] { 1, 0, 0, 0, 0, 0, 0, 0, 1 }, [0, 1, ~2]),
                Stack(50, "Wave"), Layer(51), CurveNode(52, "R"),
                Curve(53, [0, Second], [0, 90])),
            Connections(C(10, 0), C(20, 10), C(51, 50), C(52, 51), C(52, 10, "Lcl Rotation"), C(53, 52, "d|Z")));

        var scene = FbxReader.Read(bytes, null, "arm");

        var animation = Assert.Single(scene.Animations);
        Assert.Equal("Wave", animation.Name);
        var channel = Assert.Single(animation.Channels);
        Assert.Equal(AnimationPath.Rotation, channel.Path);
        Assert.Equal(AnimationInterpolation.Linear, channel.Interpolation);
        // 1초 = 46186158000틱. 키가 1초 떨어져 있어 1/30초마다 다시 표본 냄.
        Assert.Equal(31, channel.Times.Length);
        Assert.Equal(0f, channel.Times[0]);
        Assert.Equal(1f, channel.Times[^1], 5);
        Assert.Equal(1 / 30f, channel.Times[1], 5);

        // 회전 = 앞 회전 × 곡선 회전(곡선 회전을 먼저): Hamilton qx(90) * qz(90).
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var v = channel.Values.AsSpan(channel.Values.Length - 4);
        Assert.True(MathF.Abs(Quaternion.Dot(expected, new Quaternion(v[0], v[1], v[2], v[3]))) > 0.9999f);
        // 쉬는 자세는 앞 회전만.
        var restQ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        Assert.True(MathF.Abs(Quaternion.Dot(restQ, scene.Nodes[0].Rotation)) > 0.9999f);

        // (1,0,0): Z 90도 → (0,1,0), 앞 회전 X 90도 → (0,0,1).
        var posed = Assert.Single(ModelPose.Pose(scene, 0, 1).Primitives);
        Near(new Vector3(0, 0, 1), posed.Positions[0]);
        var rest = Assert.Single(ModelPose.Pose(scene).Primitives);
        Near(new Vector3(1, 0, 0), rest.Positions[0]);
    }

    [Fact]
    public void Euler_orders_rotate_the_first_named_axis_first()
    {
        // XYZ: X 90도 먼저 (0,1,0) → (0,0,1), 그다음 Y 90도 → (1,0,0). YXZ: Y 먼저(그대로), X 90도 → (0,0,1).
        Near(new Vector3(1, 0, 0), Vector3.Transform(Vector3.UnitY, FbxReader.Euler(new Vector3(90, 90, 0), 0)));
        Near(new Vector3(0, 0, 1), Vector3.Transform(Vector3.UnitY, FbxReader.Euler(new Vector3(90, 90, 0), 3)));
        // ZYX: Z 90도 먼저 (1,0,0) → (0,1,0), Y 90도(그대로), X 90도 → (0,0,1).
        Near(new Vector3(0, 0, 1), Vector3.Transform(Vector3.UnitX, FbxReader.Euler(new Vector3(90, 90, 90), 5)));
    }

    [Fact]
    public void Rotation_pivot_is_kept_in_rest_pose_and_animation()
    {
        // 피벗 (1,0,0)을 중심으로 Z 90도: (2,0,0) → (1,1,0), 피벗 자리 (1,0,0)은 그대로.
        var bytes = Fbx(Settings(),
            Objects(
                Model(10, "Door", "Mesh", Vec("RotationPivot", 1, 0, 0), Vec("ScalingPivot", 1, 0, 0), Vec("Lcl Rotation", 0, 0, 90)),
                Mesh(20, new double[] { 1, 0, 0, 2, 0, 0, 1, 0, 1 }, [0, 1, ~2]),
                Stack(50, "Open"), Layer(51), CurveNode(52, "R"), Curve(53, [0, Second], [0, 90])),
            Connections(C(10, 0), C(20, 10), C(51, 50), C(52, 51), C(52, 10, "Lcl Rotation"), C(53, 52, "d|Z")));

        var scene = FbxReader.Read(bytes, null, "door");

        var rest = Assert.Single(ModelPose.Pose(scene).Primitives);
        Near(new Vector3(1, 0, 0), rest.Positions[0]);
        Near(new Vector3(1, 1, 0), rest.Positions[1]);
        // 피벗이 있으면 회전이 이동을 바꾸므로 이동 채널도 생긴다.
        Assert.Equal([AnimationPath.Translation, AnimationPath.Rotation], Assert.Single(scene.Animations).Channels.Select(c => c.Path));
        var closed = Assert.Single(ModelPose.Pose(scene, 0, 0).Primitives);
        Near(new Vector3(2, 0, 0), closed.Positions[1]);
        var open = Assert.Single(ModelPose.Pose(scene, 0, 1).Primitives);
        Near(new Vector3(1, 0, 0), open.Positions[0]);
        Near(new Vector3(1, 1, 0), open.Positions[1]);
    }

    [Fact]
    public void Key_times_start_at_stack_start_and_names_are_unique()
    {
        var bytes = Fbx(Settings(),
            Objects(
                Model(10, "Hips", "LimbNode"),
                Stack(50, "mixamo.com"), Layer(51), CurveNode(52, "T"), Curve(53, [Second / 2, 2 * Second], [0, 10]),
                Stack(60, "Walk", localStart: Second), Layer(61), CurveNode(62, "T"), Curve(63, [Second, 3 * Second], [0, 10], flags: 0x0002),
                Stack(70, "Walk"), Layer(71), CurveNode(72, "T"), Curve(73, [0, Second], [5, 5])),
            Connections(C(10, 0),
                C(51, 50), C(52, 51), C(52, 10, "Lcl Translation"), C(53, 52, "d|X"),
                C(61, 60), C(62, 61), C(62, 10, "Lcl Translation"), C(63, 62, "d|Y"),
                C(71, 70), C(72, 71), C(72, 10, "Lcl Translation"), C(73, 72, "d|Z")));

        var scene = FbxReader.Read(bytes, null, "clip");

        Assert.Equal(["clip", "Walk", "Walk 2"], scene.Animations.Select(a => a.Name));
        var first = Assert.Single(scene.Animations[0].Channels);
        Assert.Equal([0.5f, 2f], first.Times);
        Assert.Equal([0, 0, 0, 10, 0, 0], first.Values);
        Assert.Equal(2f, scene.Animations[0].Duration);

        var second = Assert.Single(scene.Animations[1].Channels);
        Assert.Equal([0f, 2f], second.Times);   // LocalStart 1초 = 0초
        Assert.Equal(AnimationInterpolation.Step, second.Interpolation);   // constant 키
        Assert.Equal([0, 0, 0, 0, 10, 0], second.Values);
    }

    [Fact]
    public void Materials_split_primitives_and_read_color_alpha_and_textures()
    {
        var png = new MemoryStream();
        var image = new PixelImage(2, 2);
        Array.Fill(image.Pixels, new Color32(0, 0, 255, 255));
        image.Pixels[0] = new Color32(255, 0, 0, 255);
        PngIO.Save(image, png);
        Directory.CreateDirectory(Path.Combine(_dir, "mats.fbm"));
        PngIO.Save(image, Path.Combine(_dir, "mats.fbm", "side.png"));

        var materials = Node("LayerElementMaterial", [0], Leaf("Version", 101), Leaf("MappingInformationType", "ByPolygon"),
            Leaf("ReferenceInformationType", "IndexToDirect"), Leaf("Materials", new[] { 0, 1, 2, 3 }));
        var mesh = Mesh(20, new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, [0, 1, ~2, 0, 1, ~2, 0, 1, ~2, 0, 1, ~2], materials);
        static FbxRecord Material(long id, string name, params FbxRecord[] props)
            => Node("Material", [id, name + "\0\u0001Material", ""], Leaf("Version", 102), Leaf("ShadingModel", "phong"), Props(props));
        var path = WriteFile("mats.fbx", Fbx(Settings(),
            Objects(
                Model(10, "Box", "Mesh"), mesh,
                Material(30, "Paint", Vec("DiffuseColor", 0.5, 0.25, 1), P("DiffuseFactor", 0.5), P("TransparencyFactor", 0.25)),
                Material(31, "Skin", Vec("DiffuseColor", 0.1, 0.1, 0.1), P("TransparencyFactor", 1.0), P("Opacity", 1.0)),
                Material(32, "Lost"),
                Material(33, "Side"),
                Node("Texture", [40L, "SkinTex\0\u0001Texture", ""], Leaf("FileName", "C:\\elsewhere\\skin.png"), Leaf("RelativeFilename", "skin.png")),
                Node("Video", [41L, "SkinVideo\0\u0001Video", "Clip"], Leaf("Content", png.ToArray())),
                Node("Texture", [42L, "LostTex\0\u0001Texture", ""], Leaf("RelativeFilename", "textures\\missing.png")),
                Node("Texture", [43L, "SideTex\0\u0001Texture", ""], Props(P("WrapModeU", 1)), Leaf("RelativeFilename", "far\\away\\side.png"))),
            Connections(C(10, 0), C(20, 10), C(30, 10), C(31, 10), C(32, 10), C(33, 10),
                C(40, 31, "DiffuseColor"), C(41, 40), C(42, 32, "DiffuseColor"), C(43, 33, "DiffuseColor"))));

        var scene = ModelLoader.Load(path);

        var prims = Assert.Single(scene.Meshes).Primitives;
        Assert.Equal([0, 1, 2, 3], prims.Select(p => p.Material));
        var paint = scene.Materials[0];
        Assert.Equal("Paint", paint.Name);
        Assert.Equal(0.25f, paint.BaseColor.X, 5);
        Assert.Equal(0.125f, paint.BaseColor.Y, 5);
        Assert.Equal(0.5f, paint.BaseColor.Z, 5);
        Assert.Equal(0.75f, paint.BaseColor.W, 5);
        Assert.Equal(ModelAlphaMode.Blend, paint.AlphaMode);

        var skin = scene.Materials[1];   // TransparencyFactor 1 + Opacity 1(Mixamo) = 불투명
        Assert.Equal(ModelAlphaMode.Opaque, skin.AlphaMode);
        Assert.NotNull(skin.Texture);
        Assert.Equal(new Color32(255, 0, 0, 255), skin.Texture.Pixels[0]);
        Assert.Equal(new Vector4(1, 1, 1, 1), skin.BaseColor);   // 텍스처가 확산 색을 대신

        Assert.Null(scene.Materials[2].Texture);
        Assert.Contains(CoreStrings.FormatModel_Warning_TextureMissing(file: "textures\\missing.png"), scene.Warnings);

        var side = scene.Materials[3];   // "파일이름.fbm/" 폴더에서 찾음
        Assert.NotNull(side.Texture);
        Assert.False(side.Repeat);
    }

    [Fact]
    public void Ascii_old_version_and_garbage_files_are_rejected()
    {
        var ascii = WriteFile("ascii.fbx", Encoding.ASCII.GetBytes("; FBX 7.4.0 project file\nFBXHeaderExtension:  {\n}\n"));
        Assert.Equal(CoreStrings.Fbx_AsciiNotSupported, Assert.Throws<InvalidDataException>(() => ModelLoader.Load(ascii)).Message);

        var old = Fbx(6100, Objects());
        Assert.Equal(CoreStrings.FormatFbx_UnsupportedVersion(version: "6.1"),
            Assert.Throws<InvalidDataException>(() => FbxReader.Read(old, null, "old")).Message);

        Assert.Equal(CoreStrings.Fbx_InvalidFile, Assert.Throws<InvalidDataException>(() => FbxReader.Read([1, 2, 3], null, "x")).Message);
    }

    [Fact]
    public void Truncated_and_corrupted_files_throw_only_InvalidDataException()
    {
        var bytes = TwoBoneSkin();
        // 노드 중간에서 자르면 잘못된 파일.
        Assert.Throws<InvalidDataException>(() => FbxReader.Read(bytes[..(bytes.Length / 2)], null, "cut"));

        for (int length = 0; length < bytes.Length; length++)
        {
            try { FbxReader.Read(bytes[..length], null, "cut"); }
            catch (InvalidDataException) { }
        }

        var random = new Random(1234);
        for (int round = 0; round < 300; round++)
        {
            var broken = (byte[])bytes.Clone();
            for (int k = 0; k < 4; k++) broken[random.Next(27, broken.Length)] = (byte)random.Next(256);
            try { FbxReader.Read(broken, null, "broken"); }
            catch (InvalidDataException) { }
        }
    }

    [Fact]
    public void LoadAnimations_reads_skeleton_only_file()
    {
        var path = WriteFile("Walking.fbx", Fbx(Settings(),
            Objects(
                Model(10, "mixamorig:Hips", "LimbNode", Vec("Lcl Translation", 0, 100, 0)),
                Model(11, "mixamorig:Spine", "LimbNode", Vec("Lcl Translation", 0, 10, 0)),
                Stack(50, "mixamo.com"), Layer(51), CurveNode(52, "R"), Curve(53, [0, Second / 30], [0, 10])),
            Connections(C(10, 0), C(11, 10), C(51, 50), C(52, 51), C(52, 11, "Lcl Rotation"), C(53, 52, "d|X"))));

        Assert.Equal(CoreStrings.Model_NoTriangles, Assert.Throws<InvalidDataException>(() => ModelLoader.Load(path)).Message);

        var scene = ModelLoader.LoadAnimations(path);

        Assert.Equal(["mixamorig:Hips", "mixamorig:Spine"], scene.Nodes.Select(n => n.Name));
        Assert.Equal(0, scene.Nodes[1].Parent);
        var animation = Assert.Single(scene.Animations);
        Assert.Equal("Walking", animation.Name);
        var channel = Assert.Single(animation.Channels);
        Assert.Equal(1, channel.Node);
        Assert.Equal(2, channel.Times.Length);
    }

    [Fact]
    public void LoadAnimations_on_file_without_animation_throws()
    {
        var path = WriteFile("still.fbx", Fbx(Settings(), Objects(Model(10, "Tri", "Mesh"), Triangle(20)), Connections(C(10, 0), C(20, 10))));

        Assert.Single(ModelLoader.Load(path).Meshes);
        Assert.Equal(CoreStrings.Model_NoAnimations, Assert.Throws<InvalidDataException>(() => ModelLoader.LoadAnimations(path)).Message);
    }
}
