using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scene3D;

/// <summary>
/// glTF 2.0 읽기: .gltf(JSON + 옆 파일 .bin·그림, 또는 data: base64 주소)와 .glb(머리글 12바이트 + JSON 덩이 + BIN 덩이).
/// 접근자(정규화 정수·성긴 접근자 포함), 삼각형·띠·부채 프리미티브, 재질(바탕 색·텍스처·알파·발광·unlit), 노드 계층, 뼈대, 이동·회전·크기 애니메이션을 읽는다.
/// 잘못된 번호·범위 밖 읽기는 모두 <see cref="InvalidDataException"/>으로 바꾼다.
/// </summary>
internal sealed class GltfReader
{
    private const uint GlbMagic = 0x46546C67;     // "glTF"
    private const uint ChunkJson = 0x4E4F534A;    // "JSON"
    private const uint ChunkBin = 0x004E4942;     // "BIN\0"

    private const int CtByte = 5120, CtUByte = 5121, CtShort = 5122, CtUShort = 5123, CtUInt = 5125, CtFloat = 5126;
    private const int WrapClamp = 33071;

    /// <summary>메시 압축 확장: 풀 수 없으니 다시 내보내라고 알린다.</summary>
    private static readonly string[] CompressionExtensions = ["KHR_draco_mesh_compression", "EXT_meshopt_compression"];

    /// <summary>extensionsRequired에 있어도 읽을 수 있는 확장.</summary>
    private static readonly string[] SupportedRequired =
    [
        "KHR_materials_unlit", "KHR_mesh_quantization", "EXT_texture_webp",
        "KHR_materials_emissive_strength", "KHR_materials_pbrSpecularGlossiness",
    ];

    private readonly JsonElement _root;
    private readonly string? _dir;
    private readonly ReadOnlyMemory<byte>? _glbBin;
    private readonly List<string> _warnings = [];
    private readonly JsonElement[] _buffers, _views, _accessors, _images, _textures, _samplers;
    private readonly ReadOnlyMemory<byte>?[] _bufferData;
    private readonly PixelImage?[] _imageData;
    private readonly bool[] _imageRead;
    private readonly bool _meshoptUsed;

    private GltfReader(JsonElement root, string? dir, ReadOnlyMemory<byte>? glbBin)
    {
        _root = root;
        _dir = dir;
        _glbBin = glbBin;
        _buffers = List(root, "buffers", "buffers");
        _views = List(root, "bufferViews", "bufferViews");
        _accessors = List(root, "accessors", "accessors");
        _images = List(root, "images", "images");
        _textures = List(root, "textures", "textures");
        _samplers = List(root, "samplers", "samplers");
        _bufferData = new ReadOnlyMemory<byte>?[_buffers.Length];
        _imageData = new PixelImage?[_images.Length];
        _imageRead = new bool[_images.Length];
        foreach (var e in List(root, "extensionsUsed", "extensionsUsed"))
            if (e.ValueKind == JsonValueKind.String && e.GetString() == "EXT_meshopt_compression") _meshoptUsed = true;
    }

    public static ModelScene Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        return Read(bytes, dir, Path.GetFileNameWithoutExtension(path));
    }

    /// <param name="dir">옆 파일(.bin·그림)을 찾을 폴더(null이면 옆 파일을 읽지 않음 — data: 주소·GLB만).</param>
    internal static ModelScene Read(byte[] bytes, string? dir, string name)
    {
        ReadOnlyMemory<byte> json = bytes;
        ReadOnlyMemory<byte>? bin = null;
        if (bytes.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == GlbMagic)
            (json, bin) = SplitGlb(bytes);
        // UTF-8 BOM은 JSON 파서가 받지 않는다.
        if (json.Length >= 3 && json.Span[0] == 0xEF && json.Span[1] == 0xBB && json.Span[2] == 0xBF) json = json[3..];

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new InvalidDataException(CoreStrings.FormatGltf_InvalidJson(message: ex.Message), ex); }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw Malformed("$");
            return new GltfReader(doc.RootElement, dir, bin).Build(name);
        }
    }

    private static (ReadOnlyMemory<byte> Json, ReadOnlyMemory<byte>? Bin) SplitGlb(byte[] bytes)
    {
        if (bytes.Length < 20) throw new InvalidDataException(CoreStrings.Gltf_InvalidGlb);
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        if (version != 2) throw new InvalidDataException(CoreStrings.FormatGltf_UnsupportedVersion(version: version));
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        if (length < 20 || length > bytes.Length) throw new InvalidDataException(CoreStrings.Gltf_InvalidGlb);

        ReadOnlyMemory<byte>? json = null, bin = null;
        long pos = 12;
        while (pos + 8 <= length)
        {
            uint chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)pos));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)pos + 4));
            long start = pos + 8;
            if (start + chunkLength > length) throw new InvalidDataException(CoreStrings.Gltf_InvalidGlb);
            var chunk = new ReadOnlyMemory<byte>(bytes, (int)start, (int)chunkLength);
            if (json == null)
            {
                if (type != ChunkJson) throw new InvalidDataException(CoreStrings.Gltf_InvalidGlb);
                json = chunk;
            }
            else if (type == ChunkBin && bin == null) bin = chunk;
            pos = start + chunkLength;
        }
        if (json == null) throw new InvalidDataException(CoreStrings.Gltf_InvalidGlb);
        return (json.Value, bin);
    }

    private ModelScene Build(string fileName)
    {
        if (Prop(_root, "asset") is { } asset && Str(asset, "version") is { } version && !version.StartsWith('2'))
            throw new InvalidDataException(CoreStrings.FormatGltf_UnsupportedVersion(version: version));
        foreach (var e in List(_root, "extensionsRequired", "extensionsRequired"))
        {
            var ext = e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString();
            if (CompressionExtensions.Contains(ext)) throw new InvalidDataException(CoreStrings.FormatGltf_CompressedMesh(extension: ext));
            if (!SupportedRequired.Contains(ext)) throw new InvalidDataException(CoreStrings.FormatGltf_UnsupportedExtension(extension: ext));
        }

        // 재질(프리미티브가 재질의 texCoord 번호로 UV 묶음을 고르므로 먼저).
        var materialJson = List(_root, "materials", "materials");
        var materials = new ModelMaterial[materialJson.Length];
        var texCoordSets = new int[materialJson.Length];
        for (int i = 0; i < materials.Length; i++) materials[i] = ReadMaterial(materialJson[i], $"materials[{i}]", out texCoordSets[i]);

        var meshJson = List(_root, "meshes", "meshes");
        var meshes = new ModelMesh[meshJson.Length];
        for (int i = 0; i < meshes.Length; i++)
        {
            string where = $"meshes[{i}]";
            var prims = List(meshJson[i], "primitives", where);
            var list = new List<ModelPrimitive>(prims.Length);
            for (int p = 0; p < prims.Length; p++)
                if (ReadPrimitive(prims[p], $"{where}.primitives[{p}]", materials.Length, texCoordSets) is { } prim) list.Add(prim);
            meshes[i] = new ModelMesh { Name = Str(meshJson[i], "name") ?? "", Primitives = list };
        }

        var skinJson = List(_root, "skins", "skins");
        var nodeJson = List(_root, "nodes", "nodes");
        var nodes = new ModelNode[nodeJson.Length];
        for (int i = 0; i < nodes.Length; i++) nodes[i] = ReadNode(nodeJson[i], $"nodes[{i}]", nodes.Length, meshes.Length, skinJson.Length);
        LinkParents(nodes);

        var skins = new ModelSkin[skinJson.Length];
        for (int i = 0; i < skins.Length; i++) skins[i] = ReadSkin(skinJson[i], $"skins[{i}]", nodes.Length);

        var animJson = List(_root, "animations", "animations");
        var animations = new ModelAnimation[animJson.Length];
        for (int i = 0; i < animations.Length; i++) animations[i] = ReadAnimation(animJson[i], $"animations[{i}]", nodes.Length);

        // 장면: scene 번호(없으면 0번 장면), 장면이 하나도 없으면 부모 없는 노드 모두.
        var scenes = List(_root, "scenes", "scenes");
        int sceneIndex = Int(_root, "scene", scenes.Length > 0 ? 0 : -1, "scene");
        var roots = new List<int>();
        string sceneName = "";
        if (sceneIndex >= 0)
        {
            var scene = At(scenes, sceneIndex, "scene");
            sceneName = Str(scene, "name") ?? "";
            foreach (var n in IntList(scene, "nodes", $"scenes[{sceneIndex}].nodes"))
            {
                if ((uint)n >= (uint)nodes.Length) throw Malformed($"scenes[{sceneIndex}].nodes");
                if (!roots.Contains(n)) roots.Add(n);
            }
        }
        else
        {
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i].Parent < 0) roots.Add(i);
        }

        return new ModelScene
        {
            Name = sceneName.Length > 0 ? sceneName : fileName,
            Nodes = nodes,
            Roots = roots,
            Meshes = meshes,
            Materials = materials,
            Skins = skins,
            Animations = animations,
            Warnings = _warnings,
        };
    }

    // ---- 노드·뼈대·애니메이션 ----

    private static ModelNode ReadNode(JsonElement n, string where, int nodeCount, int meshCount, int skinCount)
    {
        var children = IntList(n, "children", where + ".children");
        foreach (var c in children)
            if ((uint)c >= (uint)nodeCount) throw Malformed(where + ".children");
        int mesh = Int(n, "mesh", -1, where + ".mesh");
        if (mesh >= meshCount || mesh < -1) throw Malformed(where + ".mesh");
        int skin = Int(n, "skin", -1, where + ".skin");
        if (skin >= skinCount || skin < -1) throw Malformed(where + ".skin");

        Matrix4x4? matrix = null;
        if (FloatArray(n, "matrix", 16, where + ".matrix") is { } m)
            matrix = MemoryMarshal.Cast<float, Matrix4x4>(m)[0];   // 열 우선 16개를 차례로 M11..M44에 = 행 벡터 규칙
        var t = FloatArray(n, "translation", 3, where + ".translation");
        var r = FloatArray(n, "rotation", 4, where + ".rotation");
        var s = FloatArray(n, "scale", 3, where + ".scale");
        return new ModelNode
        {
            Name = Str(n, "name") ?? "",
            Children = children,
            Matrix = matrix,
            Translation = t != null ? new Vector3(t[0], t[1], t[2]) : Vector3.Zero,
            Rotation = r != null ? new Quaternion(r[0], r[1], r[2], r[3]) : Quaternion.Identity,
            Scale = s != null ? new Vector3(s[0], s[1], s[2]) : Vector3.One,
            Mesh = mesh,
            Skin = skin,
        };
    }

    /// <summary>children으로 부모를 채운다. 부모가 둘이거나 고리가 있으면 잘못된 파일.</summary>
    private static void LinkParents(ModelNode[] nodes)
    {
        for (int i = 0; i < nodes.Length; i++)
            foreach (var c in nodes[i].Children)
            {
                if (c == i || nodes[c].Parent >= 0) throw Malformed($"nodes[{i}].children");
                nodes[c].Parent = i;
            }
        for (int i = 0; i < nodes.Length; i++)
        {
            int k = nodes[i].Parent, steps = 0;
            while (k >= 0)
            {
                if (++steps > nodes.Length) throw Malformed($"nodes[{i}]");
                k = nodes[k].Parent;
            }
        }
    }

    private ModelSkin ReadSkin(JsonElement s, string where, int nodeCount)
    {
        var joints = IntList(s, "joints", where + ".joints");
        foreach (var j in joints)
            if ((uint)j >= (uint)nodeCount) throw Malformed(where + ".joints");
        Matrix4x4[] ibm;
        int acc = Int(s, "inverseBindMatrices", -1, where + ".inverseBindMatrices");
        if (acc >= 0)
        {
            ibm = Vectors<Matrix4x4>(acc, 16, false, where + ".inverseBindMatrices");
            if (ibm.Length < joints.Length) throw Malformed(where + ".inverseBindMatrices");
        }
        else
        {
            ibm = new Matrix4x4[joints.Length];
            Array.Fill(ibm, Matrix4x4.Identity);
        }
        return new ModelSkin { Joints = joints, InverseBindMatrices = ibm };
    }

    private ModelAnimation ReadAnimation(JsonElement a, string where, int nodeCount)
    {
        var samplers = List(a, "samplers", where + ".samplers");
        var channels = new List<AnimationChannel>();
        var channelJson = List(a, "channels", where + ".channels");
        for (int c = 0; c < channelJson.Length; c++)
        {
            string cw = $"{where}.channels[{c}]";
            var ch = channelJson[c];
            var target = Prop(ch, "target") ?? throw Malformed(cw + ".target");
            int node = Int(target, "node", -1, cw + ".target.node");
            if (node < 0) continue;   // 다른 확장이 가리키는 대상 — 노드 애니메이션이 아님
            if (node >= nodeCount) throw Malformed(cw + ".target.node");
            AnimationPath path;
            switch (Str(target, "path"))
            {
                case "translation": path = AnimationPath.Translation; break;
                case "rotation": path = AnimationPath.Rotation; break;
                case "scale": path = AnimationPath.Scale; break;
                case "weights": ModelTexture.Warn(_warnings, CoreStrings.Gltf_Warning_WeightsAnimation); continue;
                default: continue;
            }
            int si = Int(ch, "sampler", -1, cw + ".sampler");
            var sampler = At(samplers, si, cw + ".sampler");
            string sw = $"{where}.samplers[{si}]";
            var interpolation = Str(sampler, "interpolation") switch
            {
                "STEP" => AnimationInterpolation.Step,
                "CUBICSPLINE" => AnimationInterpolation.CubicSpline,
                _ => AnimationInterpolation.Linear,
            };
            var times = Vectors<float>(Int(sampler, "input", -1, sw + ".input"), 1, false, sw + ".input");
            int comps = path == AnimationPath.Rotation ? 4 : 3;
            var values = Vectors<float>(Int(sampler, "output", -1, sw + ".output"), comps, false, sw + ".output");
            long need = (long)times.Length * comps * (interpolation == AnimationInterpolation.CubicSpline ? 3 : 1);
            if (values.Length < need) throw Malformed(sw + ".output");
            channels.Add(new AnimationChannel { Node = node, Path = path, Interpolation = interpolation, Times = times, Values = values });
        }
        return new ModelAnimation { Name = Str(a, "name") ?? "", Channels = channels };
    }

    // ---- 메시 ----

    private ModelPrimitive? ReadPrimitive(JsonElement p, string where, int materialCount, int[] texCoordSets)
    {
        int mode = Int(p, "mode", 4, where + ".mode");
        if (mode is not (4 or 5 or 6))
        {
            ModelTexture.Warn(_warnings, CoreStrings.FormatGltf_Warning_PrimitiveMode(mode: mode));
            return null;
        }
        var attrs = Prop(p, "attributes") ?? throw Malformed(where + ".attributes");
        if (Prop(p, "targets") is { ValueKind: JsonValueKind.Array } targets && targets.GetArrayLength() > 0)
            ModelTexture.Warn(_warnings, CoreStrings.Gltf_Warning_MorphTargets);
        int posAcc = Int(attrs, "POSITION", -1, where + ".attributes.POSITION");
        if (posAcc < 0) return null;

        var positions = Vectors<Vector3>(posAcc, 3, false, where + ".POSITION");
        int n = positions.Length;

        Vector3[]? normals = Attribute<Vector3>(attrs, "NORMAL", 3, false, n, where);
        int material = Int(p, "material", -1, where + ".material");
        if (material >= materialCount || material < -1) throw Malformed(where + ".material");
        int set = material >= 0 ? texCoordSets[material] : 0;
        Vector2[]? uvs = Attribute<Vector2>(attrs, "TEXCOORD_" + set, 2, true, n, where)
            ?? (set != 0 ? Attribute<Vector2>(attrs, "TEXCOORD_0", 2, true, n, where) : null);
        Vector4[]? colors = Colors(attrs, n, where);

        int[]? joints = null;
        float[]? weights = null;
        int jAcc = Int(attrs, "JOINTS_0", -1, where + ".JOINTS_0"), wAcc = Int(attrs, "WEIGHTS_0", -1, where + ".WEIGHTS_0");
        if (jAcc >= 0 && wAcc >= 0)
        {
            joints = Ints(jAcc, 4, where + ".JOINTS_0");
            weights = Vectors<float>(wAcc, 4, true, where + ".WEIGHTS_0");
            if (joints.Length != n * 4 || weights.Length != n * 4) throw Malformed(where + ".JOINTS_0");
        }

        int[] indices;
        int iAcc = Int(p, "indices", -1, where + ".indices");
        if (iAcc >= 0)
        {
            indices = Ints(iAcc, 1, where + ".indices");
            foreach (var i in indices)
                if ((uint)i >= (uint)n) throw Malformed(where + ".indices");
        }
        else
        {
            indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;
        }

        return new ModelPrimitive
        {
            Positions = positions,
            Normals = normals,
            Uvs = uvs,
            Colors = colors,
            Indices = Triangles(mode, indices),
            Material = material,
            Joints = joints,
            Weights = weights,
        };
    }

    private T[]? Attribute<T>(JsonElement attrs, string name, int comps, bool forceNormalize, int count, string where) where T : unmanaged
    {
        int acc = Int(attrs, name, -1, where + "." + name);
        if (acc < 0) return null;
        var data = Vectors<T>(acc, comps, forceNormalize, where + "." + name);
        if (data.Length != count) throw Malformed(where + "." + name);
        return data;
    }

    /// <summary>COLOR_0: VEC3이면 알파 1을 붙인다.</summary>
    private Vector4[]? Colors(JsonElement attrs, int count, string where)
    {
        int acc = Int(attrs, "COLOR_0", -1, where + ".COLOR_0");
        if (acc < 0) return null;
        var info = Info(acc, where + ".COLOR_0");
        if (info.Count != count || (info.Comps != 3 && info.Comps != 4)) throw Malformed(where + ".COLOR_0");
        var colors = new Vector4[count];
        if (info.Comps == 4)
        {
            DecodeFloats(info, MemoryMarshal.Cast<Vector4, float>(colors.AsSpan()), true);
            return colors;
        }
        var rgb = new float[count * 3];
        DecodeFloats(info, rgb, true);
        for (int i = 0; i < count; i++) colors[i] = new Vector4(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], 1);
        return colors;
    }

    /// <summary>띠·부채를 삼각형 목록으로 바꾸고, 같은 꼭짓점이 겹친 삼각형(띠 잇기용)은 뺀다.</summary>
    private static int[] Triangles(int mode, int[] idx)
    {
        int n = idx.Length;
        int tris = mode == 4 ? n / 3 : Math.Max(0, n - 2);
        var result = new int[tris * 3];
        int o = 0;
        for (int t = 0; t < tris; t++)
        {
            int a, b, c;
            if (mode == 4) (a, b, c) = (idx[t * 3], idx[t * 3 + 1], idx[t * 3 + 2]);
            else if (mode == 5) (a, b, c) = (t & 1) == 0 ? (idx[t], idx[t + 1], idx[t + 2]) : (idx[t + 1], idx[t], idx[t + 2]);
            else (a, b, c) = (idx[t + 1], idx[t + 2], idx[0]);
            if (a == b || b == c || a == c) continue;
            result[o++] = a;
            result[o++] = b;
            result[o++] = c;
        }
        if (o < result.Length) Array.Resize(ref result, o);
        return result;
    }

    // ---- 재질·텍스처 ----

    private ModelMaterial ReadMaterial(JsonElement m, string where, out int texCoord)
    {
        var pbr = Prop(m, "pbrMetallicRoughness");
        var ext = Prop(m, "extensions");
        var baseColor = Vec4(pbr, "baseColorFactor", Vector4.One, where + ".baseColorFactor");
        var texInfo = pbr is { } pp ? Prop(pp, "baseColorTexture") : null;
        if (pbr == null && ext is { } e0 && Prop(e0, "KHR_materials_pbrSpecularGlossiness") is { } sg)
        {
            baseColor = Vec4(sg, "diffuseFactor", Vector4.One, where + ".diffuseFactor");
            texInfo = Prop(sg, "diffuseTexture");
        }

        PixelImage? texture = null;
        bool repeat = true;
        texCoord = 0;
        if (texInfo is { } ti)
        {
            texCoord = Int(ti, "texCoord", 0, where + ".texCoord");
            (texture, repeat) = Texture(Int(ti, "index", -1, where + ".index"), where);
        }

        var alphaMode = Str(m, "alphaMode") switch
        {
            "MASK" => ModelAlphaMode.Mask,
            "BLEND" => ModelAlphaMode.Blend,
            _ => ModelAlphaMode.Opaque,
        };
        // 스스로 내는 빛 텍스처는 그리지 않는다: 그 텍스처가 가리는 작은 곳(불빛·화면)만 빛나야 하는데 계수만 쓰면 재질 전체가 흰색이 된다.
        var emissive = Prop(m, "emissiveTexture") is null ? Vec3(m, "emissiveFactor", Vector3.Zero, where + ".emissiveFactor") : Vector3.Zero;
        bool unlit = false;
        if (ext is { } e)
        {
            unlit = Prop(e, "KHR_materials_unlit") != null;
            if (Prop(e, "KHR_materials_emissive_strength") is { } es) emissive *= Float(es, "emissiveStrength", 1, where + ".emissiveStrength");
        }
        return new ModelMaterial
        {
            Name = Str(m, "name") ?? "",
            BaseColor = baseColor,
            Texture = texture,
            Repeat = repeat,
            AlphaMode = alphaMode,
            AlphaCutoff = Float(m, "alphaCutoff", 0.5f, where + ".alphaCutoff"),
            Emissive = emissive,
            Unlit = unlit,
        };
    }

    private (PixelImage? Image, bool Repeat) Texture(int index, string where)
    {
        var t = At(_textures, index, where + ".index");
        string tw = $"textures[{index}]";
        int source = Int(t, "source", -1, tw + ".source");
        if (source < 0 && Prop(t, "extensions") is { } ext && Prop(ext, "EXT_texture_webp") is { } webp)
            source = Int(webp, "source", -1, tw + ".source");
        bool repeat = true;
        int sampler = Int(t, "sampler", -1, tw + ".sampler");
        if (sampler >= 0)
        {
            var s = At(_samplers, sampler, tw + ".sampler");
            repeat = Int(s, "wrapS", 10497, tw + ".wrapS") != WrapClamp && Int(s, "wrapT", 10497, tw + ".wrapT") != WrapClamp;
        }
        return (source >= 0 ? Image(source) : null, repeat);
    }

    private PixelImage? Image(int index)
    {
        string where = $"images[{index}]";
        var img = At(_images, index, where);
        if (_imageRead[index]) return _imageData[index];
        _imageRead[index] = true;

        var uri = Str(img, "uri");
        string label = Str(img, "name") ?? (uri != null && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? uri : where);
        ReadOnlyMemory<byte> bytes;
        if (uri != null)
        {
            try { bytes = LoadUri(uri, where); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                ModelTexture.Warn(_warnings, CoreStrings.FormatModel_Warning_TextureMissing(file: uri));
                return null;
            }
        }
        else
        {
            var view = View(Int(img, "bufferView", -1, where + ".bufferView"), where + ".bufferView");
            bytes = view.Data.Slice(view.Offset, view.Length);
        }
        return _imageData[index] = ModelTexture.Decode(bytes.Span, label, _warnings);
    }

    // ---- 버퍼·접근자 ----

    private ReadOnlyMemory<byte> Buffer(int index, string where)
    {
        var b = At(_buffers, index, where);
        if (_bufferData[index] is { } cached) return cached;
        string bw = $"buffers[{index}]";
        ReadOnlyMemory<byte> data;
        if (Str(b, "uri") is { } uri) data = LoadUri(uri, bw);
        else if (index == 0 && _glbBin is { } bin) data = bin;
        else if (_meshoptUsed) throw new InvalidDataException(CoreStrings.FormatGltf_CompressedMesh(extension: "EXT_meshopt_compression"));
        else throw Malformed(bw + ".uri");
        int length = Int(b, "byteLength", -1, bw + ".byteLength");
        if (length < 0 || length > data.Length) throw Malformed(bw + ".byteLength");
        data = data[..length];
        _bufferData[index] = data;
        return data;
    }

    /// <summary>data: base64 주소, 또는 .gltf 파일 옆의 상대 경로(퍼센트 인코딩 풀어서).</summary>
    private byte[] LoadUri(string uri, string where)
    {
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = uri.IndexOf(',');
            if (comma < 0 || !uri.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) throw Malformed(where + ".uri");
            try { return Convert.FromBase64String(uri[(comma + 1)..]); }
            catch (FormatException ex) { throw new InvalidDataException(CoreStrings.FormatGltf_Malformed(where: where + ".uri"), ex); }
        }
        if (_dir == null) throw Malformed(where + ".uri");
        return File.ReadAllBytes(Path.Combine(_dir, Uri.UnescapeDataString(uri)));
    }

    private readonly record struct BufferView(ReadOnlyMemory<byte> Data, int Offset, int Length, int Stride);

    private BufferView View(int index, string where)
    {
        var v = At(_views, index, where);
        string vw = $"bufferViews[{index}]";
        var data = Buffer(Int(v, "buffer", -1, vw + ".buffer"), vw + ".buffer");
        int offset = Int(v, "byteOffset", 0, vw + ".byteOffset");
        int length = Int(v, "byteLength", -1, vw + ".byteLength");
        int stride = Int(v, "byteStride", 0, vw + ".byteStride");
        if (offset < 0 || length < 0 || stride < 0 || (long)offset + length > data.Length) throw Malformed(vw);
        return new BufferView(data, offset, length, stride);
    }

    private sealed record AccessorInfo(JsonElement Json, int Count, int Comps, int ComponentType, bool Normalized, string Where);

    private AccessorInfo Info(int index, string where)
    {
        var a = At(_accessors, index, where);
        string aw = $"accessors[{index}]";
        int ct = Int(a, "componentType", -1, aw + ".componentType");
        if (ComponentSize(ct) == 0) throw Malformed(aw + ".componentType");
        int count = Int(a, "count", -1, aw + ".count");
        if (count < 0) throw Malformed(aw + ".count");
        int comps = Str(a, "type") switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" or "MAT2" => 4,
            "MAT3" => 9,
            "MAT4" => 16,
            _ => throw Malformed(aw + ".type"),
        };
        if ((long)count * comps > Array.MaxLength) throw Malformed(aw + ".count");
        bool normalized = Prop(a, "normalized") is { ValueKind: JsonValueKind.True };
        return new AccessorInfo(a, count, comps, ct, normalized, aw);
    }

    private static int ComponentSize(int ct) => ct switch
    {
        CtByte or CtUByte => 1,
        CtShort or CtUShort => 2,
        CtUInt or CtFloat => 4,
        _ => 0,
    };

    /// <summary>
    /// 접근자(원소마다 성분 <paramref name="comps"/>개)를 <typeparamref name="T"/> 배열로 읽는다.
    /// T가 성분 묶음 하나(Vector3 등)면 원소마다 하나, float면 성분을 모두 이어 붙인 배열.
    /// </summary>
    private T[] Vectors<T>(int index, int comps, bool forceNormalize, string where) where T : unmanaged
    {
        var info = Info(index, where);
        if (info.Comps != comps) throw Malformed(info.Where + ".type");
        var result = new T[info.Count * comps / (Unsafe.SizeOf<T>() / sizeof(float))];
        DecodeFloats(info, MemoryMarshal.Cast<T, float>(result.AsSpan()), forceNormalize);
        return result;
    }

    /// <summary>정수 접근자(번호·뼈 번호) 읽기 — 부호 없는 정수 형식만.</summary>
    private int[] Ints(int index, int comps, string where)
    {
        var info = Info(index, where);
        if (info.Comps != comps || info.ComponentType is not (CtUByte or CtUShort or CtUInt)) throw Malformed(info.Where);
        var result = new int[info.Count * comps];
        int size = ComponentSize(info.ComponentType);
        if (Locate(info) is { } src)
        {
            var span = src.Span;
            for (int i = 0; i < info.Count; i++)
                for (int c = 0; c < comps; c++)
                    result[i * comps + c] = IntComponent(span.Slice(i * src.Stride + c * size), info.ComponentType, info.Where);
        }
        if (Sparse(info) is { } sparse)
        {
            var values = sparse.Values.Span;
            for (int k = 0; k < sparse.Targets.Length; k++)
                for (int c = 0; c < comps; c++)
                    result[sparse.Targets[k] * comps + c] = IntComponent(values.Slice((k * comps + c) * size), info.ComponentType, info.Where);
        }
        return result;
    }

    /// <summary>float로 읽기: 정규화 정수는 glTF 규칙대로 −1~1·0~1로, 아니면 정수 값 그대로.</summary>
    private void DecodeFloats(AccessorInfo info, Span<float> dst, bool forceNormalize)
    {
        int comps = info.Comps, ct = info.ComponentType, size = ComponentSize(ct);
        bool norm = ct != CtFloat && (info.Normalized || forceNormalize);
        if (Locate(info) is { } src)
        {
            var span = src.Span;
            for (int i = 0; i < info.Count; i++)
            {
                var element = span.Slice(i * src.Stride, comps * size);
                for (int c = 0; c < comps; c++) dst[i * comps + c] = FloatComponent(element.Slice(c * size), ct, norm);
            }
        }
        if (Sparse(info) is { } sparse)
        {
            var values = sparse.Values.Span;
            for (int k = 0; k < sparse.Targets.Length; k++)
                for (int c = 0; c < comps; c++)
                    dst[sparse.Targets[k] * comps + c] = FloatComponent(values.Slice((k * comps + c) * size), ct, norm);
        }
    }

    private readonly record struct Located(ReadOnlyMemory<byte> Data, int Stride)
    {
        public ReadOnlySpan<byte> Span => Data.Span;
    }

    /// <summary>접근자의 바이트 자리(bufferView가 없으면 null = 모두 0). 마지막 원소까지 범위 안인지 확인한다.</summary>
    private Located? Locate(AccessorInfo info)
    {
        int bv = Int(info.Json, "bufferView", -1, info.Where + ".bufferView");
        if (bv < 0 || info.Count == 0) return null;
        var view = View(bv, info.Where + ".bufferView");
        int elem = info.Comps * ComponentSize(info.ComponentType);
        int stride = view.Stride > 0 ? view.Stride : elem;
        int offset = Int(info.Json, "byteOffset", 0, info.Where + ".byteOffset");
        if (stride < elem || offset < 0 || offset + (long)stride * (info.Count - 1) + elem > view.Length) throw Malformed(info.Where);
        return new Located(view.Data.Slice(view.Offset + offset, view.Length - offset), stride);
    }

    private sealed record SparseData(int[] Targets, ReadOnlyMemory<byte> Values);

    /// <summary>성긴 접근자: 바꿀 원소 번호들과 그 값들(빽빽하게 이어진 바이트).</summary>
    private SparseData? Sparse(AccessorInfo info)
    {
        if (Prop(info.Json, "sparse") is not { } sp) return null;
        string sw = info.Where + ".sparse";
        int count = Int(sp, "count", -1, sw + ".count");
        if (count < 0 || count > info.Count) throw Malformed(sw + ".count");
        var idx = Prop(sp, "indices") ?? throw Malformed(sw + ".indices");
        var val = Prop(sp, "values") ?? throw Malformed(sw + ".values");

        int ict = Int(idx, "componentType", -1, sw + ".indices.componentType");
        if (ict is not (CtUByte or CtUShort or CtUInt)) throw Malformed(sw + ".indices.componentType");
        var targets = new int[count];
        var idxBytes = SparseBytes(idx, (long)count * ComponentSize(ict), sw + ".indices").Span;
        for (int k = 0; k < count; k++)
        {
            int t = IntComponent(idxBytes.Slice(k * ComponentSize(ict)), ict, sw);
            if ((uint)t >= (uint)info.Count) throw Malformed(sw + ".indices");
            targets[k] = t;
        }
        var values = SparseBytes(val, (long)count * info.Comps * ComponentSize(info.ComponentType), sw + ".values");
        return new SparseData(targets, values);
    }

    private ReadOnlyMemory<byte> SparseBytes(JsonElement e, long length, string where)
    {
        var view = View(Int(e, "bufferView", -1, where + ".bufferView"), where + ".bufferView");
        int offset = Int(e, "byteOffset", 0, where + ".byteOffset");
        if (offset < 0 || offset + length > view.Length) throw Malformed(where);
        return view.Data.Slice(view.Offset + offset, (int)length);
    }

    private static float FloatComponent(ReadOnlySpan<byte> s, int ct, bool norm) => ct switch
    {
        CtFloat => BinaryPrimitives.ReadSingleLittleEndian(s),
        CtUByte => norm ? s[0] / 255f : s[0],
        CtByte => norm ? MathF.Max((sbyte)s[0] / 127f, -1f) : (sbyte)s[0],
        CtUShort => norm ? BinaryPrimitives.ReadUInt16LittleEndian(s) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian(s),
        CtShort => norm ? MathF.Max(BinaryPrimitives.ReadInt16LittleEndian(s) / 32767f, -1f) : BinaryPrimitives.ReadInt16LittleEndian(s),
        _ => norm ? (float)(BinaryPrimitives.ReadUInt32LittleEndian(s) / 4294967295.0) : BinaryPrimitives.ReadUInt32LittleEndian(s),
    };

    private static int IntComponent(ReadOnlySpan<byte> s, int ct, string where)
    {
        uint v = ct switch
        {
            CtUByte => s[0],
            CtUShort => BinaryPrimitives.ReadUInt16LittleEndian(s),
            _ => BinaryPrimitives.ReadUInt32LittleEndian(s),
        };
        if (v > int.MaxValue) throw Malformed(where);
        return (int)v;
    }

    // ---- JSON 도우미(형식이 다르면 잘못된 파일) ----

    private static InvalidDataException Malformed(string where) => new(CoreStrings.FormatGltf_Malformed(where: where));

    private static JsonElement? Prop(JsonElement obj, string name)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    private static JsonElement? Prop(JsonElement? obj, string name) => obj is { } o ? Prop(o, name) : null;

    private static JsonElement[] List(JsonElement obj, string name, string where)
    {
        if (Prop(obj, name) is not { } v) return [];
        if (v.ValueKind != JsonValueKind.Array) throw Malformed(where);
        var result = new JsonElement[v.GetArrayLength()];
        int i = 0;
        foreach (var e in v.EnumerateArray()) result[i++] = e;
        return result;
    }

    private static JsonElement At(JsonElement[] list, int index, string where)
        => (uint)index < (uint)list.Length ? list[index] : throw Malformed(where);

    private static int Int(JsonElement obj, string name, int fallback, string where)
    {
        if (Prop(obj, name) is not { } v) return fallback;
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt32(out int i)) return i;
            // 1.0처럼 소수점으로 적은 정수도 받는다.
            if (v.TryGetDouble(out double d) && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue) return (int)d;
        }
        throw Malformed(where);
    }

    private static float Float(JsonElement obj, string name, float fallback, string where)
    {
        if (Prop(obj, name) is not { } v) return fallback;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) ? (float)d : throw Malformed(where);
    }

    private static string? Str(JsonElement obj, string name)
        => Prop(obj, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static int[] IntList(JsonElement obj, string name, string where)
    {
        var list = List(obj, name, where);
        var result = new int[list.Length];
        for (int i = 0; i < list.Length; i++)
            result[i] = list[i].ValueKind == JsonValueKind.Number && list[i].TryGetInt32(out int v) ? v : throw Malformed(where);
        return result;
    }

    /// <summary>길이가 꼭 <paramref name="length"/>인 수 배열(없으면 null).</summary>
    private static float[]? FloatArray(JsonElement? obj, string name, int length, string where)
    {
        if (obj is not { } o) return null;
        var list = List(o, name, where);
        if (list.Length == 0 && Prop(o, name) == null) return null;
        if (list.Length != length) throw Malformed(where);
        var result = new float[length];
        for (int i = 0; i < length; i++)
            result[i] = list[i].ValueKind == JsonValueKind.Number && list[i].TryGetDouble(out double d) ? (float)d : throw Malformed(where);
        return result;
    }

    private static Vector4 Vec4(JsonElement? obj, string name, Vector4 fallback, string where)
        => FloatArray(obj, name, 4, where) is { } f ? new Vector4(f[0], f[1], f[2], f[3]) : fallback;

    private static Vector3 Vec3(JsonElement? obj, string name, Vector3 fallback, string where)
        => FloatArray(obj, name, 3, where) is { } f ? new Vector3(f[0], f[1], f[2]) : fallback;
}
