using System.Numerics;
using System.Text;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scene3D;

namespace Chickxel.Core.Tests;

public class ModelLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chickxel-model-tests-" + Guid.NewGuid().ToString("N"));

    public ModelLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>시험용 glTF 조립: 데이터를 BIN 하나에 4바이트 맞춤으로 이어 붙이고 bufferView·accessor JSON을 만든다.</summary>
    private sealed class Gltf
    {
        private readonly MemoryStream _bin = new();
        private readonly List<string> _views = [];
        private readonly List<string> _accessors = [];

        public byte[] Bin => _bin.ToArray();

        private int View(byte[] bytes)
        {
            while (_bin.Length % 4 != 0) _bin.WriteByte(0);
            long offset = _bin.Length;
            _bin.Write(bytes);
            _views.Add($"{{\"buffer\":0,\"byteOffset\":{offset},\"byteLength\":{bytes.Length}}}");
            return _views.Count - 1;
        }

        public int Floats(string type, params float[] data)
        {
            var bytes = new byte[data.Length * 4];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            int comps = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => 16 };
            _accessors.Add($"{{\"bufferView\":{View(bytes)},\"componentType\":5126,\"count\":{data.Length / comps},\"type\":\"{type}\"}}");
            return _accessors.Count - 1;
        }

        public int UShorts(string type, int comps, params ushort[] data)
        {
            var bytes = new byte[data.Length * 2];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            _accessors.Add($"{{\"bufferView\":{View(bytes)},\"componentType\":5123,\"count\":{data.Length / comps},\"type\":\"{type}\"}}");
            return _accessors.Count - 1;
        }

        public int UBytes(string type, int comps, bool normalized, params byte[] data)
        {
            _accessors.Add($"{{\"bufferView\":{View(data)},\"componentType\":5121,\"count\":{data.Length / comps},\"type\":\"{type}\",\"normalized\":{(normalized ? "true" : "false")}}}");
            return _accessors.Count - 1;
        }

        /// <summary>그림(바이트 그대로)을 bufferView 하나로.</summary>
        public int RawView(byte[] bytes) => View(bytes);

        /// <summary>accessor JSON을 그대로 더한다(잘못된 파일 시험용).</summary>
        public int RawAccessor(string json)
        {
            _accessors.Add(json);
            return _accessors.Count - 1;
        }

        public string Json(string rest, string? bufferUri = null)
        {
            var uri = bufferUri == null ? "" : $",\"uri\":\"{bufferUri}\"";
            return $"{{\"asset\":{{\"version\":\"2.0\"}},\"buffers\":[{{\"byteLength\":{_bin.Length}{uri}}}]," +
                $"\"bufferViews\":[{string.Join(",", _views)}],\"accessors\":[{string.Join(",", _accessors)}],{rest}}}";
        }

        public byte[] Glb(string rest)
        {
            var json = Encoding.UTF8.GetBytes(Json(rest));
            int jsonLen = (json.Length + 3) & ~3;
            var bin = Bin;
            int binLen = (bin.Length + 3) & ~3;
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(0x46546C67u);
            w.Write(2u);
            w.Write((uint)(12 + 8 + jsonLen + 8 + binLen));
            w.Write((uint)jsonLen);
            w.Write(0x4E4F534Au);
            w.Write(json);
            for (int i = json.Length; i < jsonLen; i++) w.Write((byte)' ');
            w.Write((uint)binLen);
            w.Write(0x004E4942u);
            w.Write(bin);
            for (int i = bin.Length; i < binLen; i++) w.Write((byte)0);
            w.Flush();
            return ms.ToArray();
        }
    }

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteText(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static void Near(Vector3 expected, Vector3 actual, float eps = 1e-4f)
        => Assert.True(Vector3.Distance(expected, actual) < eps, $"expected {expected}, actual {actual}");

    /// <summary>XY 평면의 반시계 삼각형(앞면 +Z) 하나: 위치 접근자 0, 번호 접근자 1.</summary>
    private static Gltf Triangle()
    {
        var g = new Gltf();
        g.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        g.UShorts("SCALAR", 1, 0, 1, 2);
        return g;
    }

    private const string TriangleMesh = "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1,\"material\":0}]}]";

    [Fact]
    public void Glb_triangle_reads_geometry_material_and_node_translation()
    {
        var g = Triangle();
        var path = WriteFile("tri.glb", g.Glb(
            TriangleMesh + ",\"materials\":[{\"name\":\"red\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,0,0,0.5]},\"alphaMode\":\"MASK\",\"alphaCutoff\":0.3,\"emissiveFactor\":[0,0.2,0]}]," +
            "\"nodes\":[{\"mesh\":0,\"translation\":[1,2,3]}],\"scenes\":[{\"nodes\":[0]}],\"scene\":0"));

        var scene = ModelLoader.Load(path);

        var prim = Assert.Single(Assert.Single(scene.Meshes).Primitives);
        Assert.Equal([new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0)], prim.Positions);
        Assert.Equal([0, 1, 2], prim.Indices);
        var mat = Assert.Single(scene.Materials);
        Assert.Equal("red", mat.Name);
        Assert.Equal(new Vector4(1, 0, 0, 0.5f), mat.BaseColor);
        Assert.Equal(ModelAlphaMode.Mask, mat.AlphaMode);
        Assert.Equal(0.3f, mat.AlphaCutoff);
        Assert.Equal(new Vector3(0, 0.2f, 0), mat.Emissive);
        Assert.Equal([0], scene.Roots);
        Assert.Empty(scene.Warnings);

        var posed = Assert.Single(ModelPose.Pose(scene).Primitives);
        Near(new Vector3(1, 2, 3), posed.Positions[0]);
        Near(new Vector3(2, 2, 3), posed.Positions[1]);
        Near(new Vector3(1, 3, 3), posed.Positions[2]);
        Assert.Same(mat, posed.Material);
        // 법선이 없으면 면에서 구한다(반시계 = +Z).
        Assert.All(posed.Normals, nv => Near(Vector3.UnitZ, nv));
    }

    [Fact]
    public void Node_hierarchy_composes_parent_rotation_with_child_translation()
    {
        var g = Triangle();
        float h = MathF.Sqrt(0.5f);
        // 부모: Y축 90° 회전, 자식: x로 2 이동 → 월드에서 자식 원점은 (0,0,−2).
        var path = WriteFile("tree.glb", g.Glb(
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1}]}]," +
            $"\"nodes\":[{{\"children\":[1],\"rotation\":[0,{h},0,{h}]}},{{\"mesh\":0,\"translation\":[2,0,0]}}]"));

        var scene = ModelLoader.Load(path);
        Assert.Equal(0, scene.Nodes[1].Parent);
        Assert.Equal([0], scene.Roots);   // 장면이 없으면 부모 없는 노드
        var posed = Assert.Single(ModelPose.Pose(scene).Primitives);
        Near(new Vector3(0, 0, -2), posed.Positions[0]);
        Near(new Vector3(0, 0, -3), posed.Positions[1]);
        Assert.Same(ModelMaterial.Default, posed.Material);
    }

    [Fact]
    public void Gltf_reads_external_bin_and_data_uri_buffers()
    {
        // 위치는 옆 파일 .bin, 번호는 data: 주소 버퍼.
        var positions = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0 };
        var posBytes = new byte[positions.Length * 4];
        Buffer.BlockCopy(positions, 0, posBytes, 0, posBytes.Length);
        File.WriteAllBytes(Path.Combine(_dir, "geo data.bin"), posBytes);
        var idx = new ushort[] { 0, 1, 2, 2, 1, 3 };
        var idxBytes = new byte[idx.Length * 2];
        Buffer.BlockCopy(idx, 0, idxBytes, 0, idxBytes.Length);
        var json = $$"""
            {"asset":{"version":"2.0"},
             "buffers":[{"byteLength":{{posBytes.Length}},"uri":"geo%20data.bin"},
                        {"byteLength":{{idxBytes.Length}},"uri":"data:application/octet-stream;base64,{{Convert.ToBase64String(idxBytes)}}"}],
             "bufferViews":[{"buffer":0,"byteLength":{{posBytes.Length}}},{"buffer":1,"byteLength":{{idxBytes.Length}}}],
             "accessors":[{"bufferView":0,"componentType":5126,"count":4,"type":"VEC3"},
                          {"bufferView":1,"componentType":5123,"count":6,"type":"SCALAR"}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],
             "nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}]}
            """;
        var scene = ModelLoader.Load(WriteText("quad.gltf", json));

        var prim = Assert.Single(scene.Meshes[0].Primitives);
        Assert.Equal(new Vector3(1, 1, 0), prim.Positions[3]);
        Assert.Equal([0, 1, 2, 2, 1, 3], prim.Indices);
        Assert.Equal(2, ModelPose.Pose(scene).TriangleCount);
    }

    [Fact]
    public void Triangle_strip_is_converted_with_alternating_winding_and_normalized_colors()
    {
        var g = new Gltf();
        g.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0);
        g.UBytes("VEC3", 3, true, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255);
        var scene = ModelLoader.Load(WriteFile("strip.glb", g.Glb(
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"COLOR_0\":1},\"mode\":5}]}],\"nodes\":[{\"mesh\":0}]")));

        var prim = scene.Meshes[0].Primitives[0];
        Assert.Equal([0, 1, 2, 2, 1, 3], prim.Indices);
        Assert.Equal(new Vector4(1, 0, 0, 1), prim.Colors![0]);
        Assert.Equal(new Vector4(1, 1, 1, 1), prim.Colors[3]);
        // 두 삼각형 모두 +Z를 본다.
        Assert.All(ModelPose.Pose(scene).Primitives[0].Normals, nv => Near(Vector3.UnitZ, nv));
    }

    [Fact]
    public void Embedded_png_texture_keeps_channels_and_clamp_sampler()
    {
        var tex = new PixelImage(2, 1);
        tex[0, 0] = new Color32(255, 0, 0);
        tex[1, 0] = new Color32(0, 0, 255, 128);
        using var png = new MemoryStream();
        PngIO.Save(tex, png);

        var g = Triangle();
        g.Floats("VEC2", 0, 0, 1, 0, 0, 1);
        int view = g.RawView(png.ToArray());
        var scene = ModelLoader.Load(WriteFile("tex.glb", g.Glb(
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"TEXCOORD_0\":2},\"indices\":1,\"material\":0}]}]," +
            "\"materials\":[{\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}]," +
            $"\"textures\":[{{\"source\":0,\"sampler\":0}}],\"samplers\":[{{\"wrapS\":33071,\"wrapT\":33071}}]," +
            $"\"images\":[{{\"bufferView\":{view},\"mimeType\":\"image/png\"}}],\"nodes\":[{{\"mesh\":0}}]")));

        var mat = scene.Materials[0];
        Assert.NotNull(mat.Texture);
        Assert.Equal(new Color32(255, 0, 0), mat.Texture![0, 0]);
        Assert.Equal(new Color32(0, 0, 255, 128), mat.Texture[1, 0]);
        Assert.False(mat.Repeat);
        Assert.Equal(new Vector2(1, 0), scene.Meshes[0].Primitives[0].Uvs![1]);
    }

    [Fact]
    public void Linear_translation_animation_is_halfway_at_half_time_and_clamped_outside()
    {
        var g = Triangle();
        int input = g.Floats("SCALAR", 0, 1);
        int output = g.Floats("VEC3", 0, 0, 0, 2, 0, 0);
        var scene = ModelLoader.Load(WriteFile("anim.glb", g.Glb(
            TriangleMesh.Replace(",\"material\":0", "") + ",\"nodes\":[{\"mesh\":0}]," +
            $"\"animations\":[{{\"name\":\"move\",\"samplers\":[{{\"input\":{input},\"output\":{output}}}],\"channels\":[{{\"sampler\":0,\"target\":{{\"node\":0,\"path\":\"translation\"}}}}]}}]")));

        var anim = Assert.Single(scene.Animations);
        Assert.Equal("move", anim.Name);
        Assert.Equal(1f, anim.Duration);
        Near(new Vector3(1, 0, 0), ModelPose.Pose(scene, 0, 0.5f).Primitives[0].Positions[0]);
        Near(new Vector3(2, 0, 0), ModelPose.Pose(scene, 0, 5f).Primitives[0].Positions[0]);
        Near(new Vector3(0, 0, 0), ModelPose.Pose(scene, 0, -1f).Primitives[0].Positions[0]);
        // 쉬는 자세는 애니메이션을 쓰지 않는다.
        Near(new Vector3(1, 0, 0), ModelPose.Pose(scene).Primitives[0].Positions[1]);
    }

    [Fact]
    public void Rotation_animation_slerps_and_step_holds_previous_key()
    {
        var g = Triangle();
        float h = MathF.Sqrt(0.5f);
        int input = g.Floats("SCALAR", 0, 1);
        int output = g.Floats("VEC4", 0, 0, 0, 1, 0, h, 0, h);   // 단위 → Y축 90°
        var animations = $"\"animations\":[" +
            $"{{\"samplers\":[{{\"input\":{input},\"output\":{output}}}],\"channels\":[{{\"sampler\":0,\"target\":{{\"node\":0,\"path\":\"rotation\"}}}}]}}," +
            $"{{\"samplers\":[{{\"input\":{input},\"output\":{output},\"interpolation\":\"STEP\"}}],\"channels\":[{{\"sampler\":0,\"target\":{{\"node\":0,\"path\":\"rotation\"}}}}]}}]";
        var scene = ModelLoader.Load(WriteFile("rot.glb", g.Glb(
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1}]}],\"nodes\":[{\"mesh\":0}]," + animations)));

        var expected = Vector3.Transform(Vector3.UnitX, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4));
        Near(expected, ModelPose.Pose(scene, 0, 0.5f).Primitives[0].Positions[1]);
        Near(new Vector3(0, 0, -1), ModelPose.Pose(scene, 0, 1f).Primitives[0].Positions[1]);
        Near(Vector3.UnitX, ModelPose.Pose(scene, 1, 0.9f).Primitives[0].Positions[1]);
    }

    [Fact]
    public void Cubic_spline_with_zero_tangents_eases_between_keys()
    {
        var g = Triangle();
        int input = g.Floats("SCALAR", 0, 1);
        // 키마다 [들어오는 접선, 값, 나가는 접선].
        int output = g.Floats("VEC3", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0);
        var scene = ModelLoader.Load(WriteFile("cubic.glb", g.Glb(
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1}]}],\"nodes\":[{\"mesh\":0}]," +
            $"\"animations\":[{{\"samplers\":[{{\"input\":{input},\"output\":{output},\"interpolation\":\"CUBICSPLINE\"}}],\"channels\":[{{\"sampler\":0,\"target\":{{\"node\":0,\"path\":\"translation\"}}}}]}}]")));

        Near(new Vector3(2, 0, 0), ModelPose.Pose(scene, 0, 0.5f).Primitives[0].Positions[0]);
        // u = 0.25: 3u² − 2u³ = 0.15625
        Near(new Vector3(4 * 0.15625f, 0, 0), ModelPose.Pose(scene, 0, 0.25f).Primitives[0].Positions[0]);
    }

    [Fact]
    public void Skin_moves_only_vertices_weighted_to_the_moved_joint()
    {
        var g = new Gltf();
        // v0 (0,0,0) 뼈 0, v1 (0,2,0) 뼈 1, v2 (1,0,0) 뼈 0, v3 (0,1,0) 뼈 0·1 무게 1:1(합 2 → 맞춤).
        g.Floats("VEC3", 0, 0, 0, 0, 2, 0, 1, 0, 0, 0, 1, 0);
        g.UShorts("SCALAR", 1, 0, 2, 1, 2, 3, 1);
        int joints = g.UBytes("VEC4", 4, false, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0);
        int weights = g.Floats("VEC4", 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0);
        var ibm = Matrix4x4.CreateTranslation(0, -1, 0);
        int ibmAcc = g.Floats("MAT4", [.. Floats(Matrix4x4.Identity), .. Floats(ibm)]);
        int input = g.Floats("SCALAR", 0, 1);
        int output = g.Floats("VEC3", 0, 1, 0, 3, 1, 0);
        var scene = ModelLoader.Load(WriteFile("skin.glb", g.Glb(
            $"\"meshes\":[{{\"primitives\":[{{\"attributes\":{{\"POSITION\":0,\"JOINTS_0\":{joints},\"WEIGHTS_0\":{weights}}},\"indices\":1}}]}}]," +
            // 메시 노드의 이동(100)은 뼈대 메시에 쓰이지 않아야 한다.
            $"\"nodes\":[{{\"mesh\":0,\"skin\":0,\"translation\":[100,0,0]}},{{\"children\":[2]}},{{\"translation\":[0,1,0]}}]," +
            $"\"skins\":[{{\"joints\":[1,2],\"inverseBindMatrices\":{ibmAcc}}}],\"scenes\":[{{\"nodes\":[0,1]}}]," +
            $"\"animations\":[{{\"samplers\":[{{\"input\":{input},\"output\":{output}}}],\"channels\":[{{\"sampler\":0,\"target\":{{\"node\":2,\"path\":\"translation\"}}}}]}}]")));

        var skin = Assert.Single(scene.Skins);
        Assert.Equal([1, 2], skin.Joints);
        Assert.Equal(ibm, skin.InverseBindMatrices[1]);

        var rest = ModelPose.Pose(scene).Primitives[0].Positions;
        Near(new Vector3(0, 0, 0), rest[0]);
        Near(new Vector3(0, 2, 0), rest[1]);
        Near(new Vector3(0, 1, 0), rest[3]);

        var moved = ModelPose.Pose(scene, 0, 1).Primitives[0].Positions;
        Near(new Vector3(0, 0, 0), moved[0]);
        Near(new Vector3(3, 2, 0), moved[1]);
        Near(new Vector3(1, 0, 0), moved[2]);
        Near(new Vector3(1.5f, 1, 0), moved[3]);
    }

    private static float[] Floats(Matrix4x4 m) =>
        [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

    [Fact]
    public void Sparse_accessor_overrides_selected_elements()
    {
        var g = Triangle();
        // 위치 접근자 0을 성기게 덮는 새 접근자: 2번 꼭짓점 → (5,5,5).
        int idxView = g.RawView([2, 0, 0, 0]);
        var values = new float[] { 5, 5, 5 };
        var valueBytes = new byte[12];
        Buffer.BlockCopy(values, 0, valueBytes, 0, 12);
        int valView = g.RawView(valueBytes);
        int sparse = g.RawAccessor($"{{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"sparse\":{{\"count\":1," +
            $"\"indices\":{{\"bufferView\":{idxView},\"componentType\":5121}},\"values\":{{\"bufferView\":{valView}}}}}}}");
        var scene = ModelLoader.Load(WriteFile("sparse.glb", g.Glb(
            $"\"meshes\":[{{\"primitives\":[{{\"attributes\":{{\"POSITION\":{sparse}}},\"indices\":1}}]}}],\"nodes\":[{{\"mesh\":0}}]")));

        var p = scene.Meshes[0].Primitives[0].Positions;
        Assert.Equal(new Vector3(1, 0, 0), p[1]);
        Assert.Equal(new Vector3(5, 5, 5), p[2]);
    }

    [Fact]
    public void Lines_and_morph_targets_become_warnings_not_errors()
    {
        var g = Triangle();
        var scene = ModelLoader.Load(WriteFile("warn.glb", g.Glb(
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1,\"targets\":[{\"POSITION\":0}]},{\"attributes\":{\"POSITION\":0},\"mode\":1}]}]," +
            "\"nodes\":[{\"mesh\":0}]")));

        Assert.Single(scene.Meshes[0].Primitives);
        Assert.Equal(2, scene.Warnings.Count);
    }

    [Theory]
    [InlineData("KHR_draco_mesh_compression")]
    [InlineData("EXT_meshopt_compression")]
    public void Compressed_meshes_are_rejected_with_a_clear_message(string extension)
    {
        var g = Triangle();
        var path = WriteFile("draco.glb", g.Glb(TriangleMesh + $",\"nodes\":[{{\"mesh\":0}}],\"extensionsUsed\":[\"{extension}\"],\"extensionsRequired\":[\"{extension}\"]"));
        var ex = Assert.Throws<InvalidDataException>(() => ModelLoader.Load(path));
        Assert.Contains(extension, ex.Message);
    }

    [Fact]
    public void Unknown_required_extension_is_named_in_the_error()
    {
        var g = Triangle();
        var path = WriteFile("ext.glb", g.Glb(TriangleMesh + ",\"nodes\":[{\"mesh\":0}],\"extensionsRequired\":[\"KHR_made_up\"]"));
        Assert.Contains("KHR_made_up", Assert.Throws<InvalidDataException>(() => ModelLoader.Load(path)).Message);
    }

    [Fact]
    public void Out_of_range_indices_and_accessors_are_invalid_data()
    {
        var bad = new Gltf();
        bad.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        bad.UShorts("SCALAR", 1, 0, 1, 7);
        var badIndex = WriteFile("badindex.glb", bad.Glb(TriangleMesh.Replace(",\"material\":0", "") + ",\"nodes\":[{\"mesh\":0}]"));
        Assert.Throws<InvalidDataException>(() => ModelLoader.Load(badIndex));

        var tooLong = Triangle();
        int over = tooLong.RawAccessor("{\"bufferView\":0,\"componentType\":5126,\"count\":100,\"type\":\"VEC3\"}");
        var badAccessor = WriteFile("badacc.glb", tooLong.Glb($"\"meshes\":[{{\"primitives\":[{{\"attributes\":{{\"POSITION\":{over}}}}}]}}],\"nodes\":[{{\"mesh\":0}}]"));
        Assert.Throws<InvalidDataException>(() => ModelLoader.Load(badAccessor));

        var cyclic = Triangle();
        var cycle = WriteFile("cycle.glb", cyclic.Glb(TriangleMesh.Replace(",\"material\":0", "") + ",\"nodes\":[{\"mesh\":0,\"children\":[1]},{\"children\":[0]}]"));
        Assert.Throws<InvalidDataException>(() => ModelLoader.Load(cycle));

        Assert.Throws<InvalidDataException>(() => ModelLoader.Load(WriteFile("trunc.glb", [0x67, 0x6C, 0x54, 0x46, 2, 0, 0, 0, 99, 0, 0, 0])));
        Assert.Throws<InvalidDataException>(() => ModelLoader.Load(WriteText("broken.gltf", "{\"asset\":")));
        Assert.Throws<InvalidDataException>(() => ModelLoader.Load(WriteText("model.fbx", "x")));
    }

    [Fact]
    public void Obj_reads_quad_with_negative_indices_materials_and_vertex_colors()
    {
        WriteText("box.mtl", """
            newmtl Red
            Kd 1 0 0
            d 0.5
            map_Kd -s 1 1 1 missing.png
            newmtl Blue
            Kd 0 0 1
            Tr 0.25
            """);
        var path = WriteText("box.obj", """
            # 사각형 하나 + 삼각형 하나
            mtllib box.mtl
            o thing
            v 0 0 0 1 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vt 1 1
            vt 0 1
            vn 0 0 1
            usemtl Red
            f -4/-4/-1 -3/-3/-1 -2/-2/-1 -1/-1/-1
            usemtl Blue
            f 1//1 2//1 4//1
            """);

        var scene = ModelLoader.Load(path);

        Assert.Equal(["Red", "Blue"], scene.Materials.Select(m => m.Name));
        Assert.Equal(new Vector4(1, 0, 0, 0.5f), scene.Materials[0].BaseColor);
        Assert.Equal(ModelAlphaMode.Blend, scene.Materials[0].AlphaMode);
        Assert.Null(scene.Materials[0].Texture);
        Assert.Equal(new Vector4(0, 0, 1, 0.75f), scene.Materials[1].BaseColor);
        Assert.Contains(scene.Warnings, w => w.Contains("missing.png"));

        var node = Assert.Single(scene.Nodes);
        Assert.Equal(0, node.Mesh);
        var prims = scene.Meshes[0].Primitives;
        Assert.Equal(2, prims.Count);
        var quad = prims[0];
        Assert.Equal(0, quad.Material);
        Assert.Equal(4, quad.Positions.Length);
        Assert.Equal([0, 1, 2, 0, 2, 3], quad.Indices);
        Assert.Equal(new Vector2(0, 1), quad.Uvs![0]);    // v 뒤집기: (0,0) → 왼쪽 아래 = (0,1)
        Assert.Equal(new Vector2(1, 0), quad.Uvs[2]);
        Assert.Equal(Vector3.UnitZ, quad.Normals![0]);
        Assert.Equal(new Vector4(1, 0, 0, 1), quad.Colors![0]);
        Assert.Equal(Vector4.One, quad.Colors[1]);
        Assert.Equal(1, prims[1].Material);
        Assert.Null(prims[1].Uvs);
        Assert.Equal(3, ModelPose.Pose(scene).TriangleCount);
    }

    [Fact]
    public void Obj_missing_mtl_is_a_warning_and_bad_index_is_invalid_data()
    {
        var scene = ModelLoader.Load(WriteText("plain.obj", "mtllib nowhere.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl X\nf 1 2 3\n"));
        Assert.Contains(scene.Warnings, w => w.Contains("nowhere.mtl"));
        Assert.Equal(-1, scene.Meshes[0].Primitives[0].Material);
        Assert.Same(ModelMaterial.Default, ModelPose.Pose(scene).Primitives[0].Material);

        Assert.Throws<InvalidDataException>(() => ModelLoader.Load(WriteText("bad.obj", "v 0 0 0\nv 1 0 0\nf 1 2 5\n")));
        Assert.Throws<InvalidDataException>(() => ModelLoader.Load(WriteText("empty.obj", "v 0 0 0\n")));
    }

    [Fact]
    public void IsModelFile_matches_case_insensitively()
    {
        Assert.True(ModelLoader.IsModelFile("a/B.GLB"));
        Assert.True(ModelLoader.IsModelFile("x.Obj"));
        Assert.True(ModelLoader.IsModelFile("rig.FBX"));
        Assert.False(ModelLoader.IsModelFile("x.png"));
    }
}
