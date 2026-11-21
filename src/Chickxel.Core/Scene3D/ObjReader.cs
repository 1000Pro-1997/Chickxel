using System.Globalization;
using System.Numerics;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scene3D;

/// <summary>
/// Wavefront OBJ(+MTL) 읽기: v(뒤에 r g b가 있으면 꼭짓점 색)·vt·vn·f(다각형은 부채꼴로 나눔, 음수 = 뒤에서부터 센 번호), usemtl·mtllib.
/// 재질마다 프리미티브 하나, v/vt/vn 묶음이 같으면 꼭짓점 하나로 합친다. UV는 (u, 1 − v)로 뒤집어 glTF처럼 (0,0) = 그림 왼쪽 위.
/// 노드는 하나(메시 0, 단위 변환)이고 o·g는 묶음으로 나누지 않는다.
/// </summary>
internal static class ObjReader
{
    /// <summary>재질 하나의 삼각형 모음(v/vt/vn 번호 묶음 → 꼭짓점 번호).</summary>
    private sealed class Part
    {
        public readonly Dictionary<(int V, int T, int N), int> Map = [];
        public readonly List<Vector3> Positions = [];
        public readonly List<Vector2> Uvs = [];
        public readonly List<Vector3> Normals = [];
        public readonly List<Vector4> Colors = [];
        public readonly List<int> Indices = [];
        public bool AnyUv, AnyNormal, MissingNormal;
    }

    public static ModelScene Read(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        var warnings = new List<string>();
        var positions = new List<Vector3>();
        var colors = new List<Vector4>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        bool anyColor = false;

        var materials = new List<ModelMaterial>();
        var materialIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var textures = new Dictionary<string, PixelImage?>(StringComparer.OrdinalIgnoreCase);

        var parts = new List<(string Material, Part Part)>();
        Part? current = null;
        string currentMaterial = "";
        var face = new List<int>(8);
        Span<float> nums = stackalloc float[6];

        int lineNo = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNo++;
            var line = raw.AsSpan().Trim();
            if (line.IsEmpty || line[0] == '#') continue;
            var rest = line;
            var key = NextToken(ref rest);

            if (key.SequenceEqual("v"))
            {
                int count = Numbers(rest, nums);
                if (count < 3) throw Malformed(lineNo);
                positions.Add(new Vector3(nums[0], nums[1], nums[2]));
                if (count >= 6)
                {
                    anyColor = true;
                    colors.Add(new Vector4(nums[3], nums[4], nums[5], 1));
                }
                else colors.Add(Vector4.One);
            }
            else if (key.SequenceEqual("vt"))
            {
                int count = Numbers(rest, nums);
                if (count < 1) throw Malformed(lineNo);
                uvs.Add(new Vector2(nums[0], 1 - (count >= 2 ? nums[1] : 0)));
            }
            else if (key.SequenceEqual("vn"))
            {
                if (Numbers(rest, nums) < 3) throw Malformed(lineNo);
                normals.Add(new Vector3(nums[0], nums[1], nums[2]));
            }
            else if (key.SequenceEqual("f"))
            {
                if (current == null)
                {
                    current = Find(parts, currentMaterial);
                }
                face.Clear();
                while (!rest.IsEmpty)
                {
                    var token = NextToken(ref rest);
                    if (token.IsEmpty) break;
                    face.Add(Vertex(current, token, positions, uvs, normals, colors, lineNo));
                }
                for (int k = 1; k + 1 < face.Count; k++)
                {
                    int a = face[0], b = face[k], c = face[k + 1];
                    if (a == b || b == c || a == c) continue;
                    current.Indices.Add(a);
                    current.Indices.Add(b);
                    current.Indices.Add(c);
                }
            }
            else if (key.SequenceEqual("usemtl"))
            {
                currentMaterial = rest.ToString();
                current = null;
            }
            else if (key.SequenceEqual("mtllib"))
            {
                LoadMtlLib(dir, rest.ToString(), materials, materialIndex, textures, warnings);
            }
        }

        var primitives = new List<ModelPrimitive>(parts.Count);
        foreach (var (name, part) in parts)
        {
            if (part.Indices.Count == 0) continue;
            primitives.Add(new ModelPrimitive
            {
                Positions = [.. part.Positions],
                Normals = part.AnyNormal && !part.MissingNormal ? [.. part.Normals] : null,
                Uvs = part.AnyUv ? [.. part.Uvs] : null,
                Colors = anyColor ? [.. part.Colors] : null,
                Indices = [.. part.Indices],
                Material = materialIndex.TryGetValue(name, out int m) ? m : -1,
            });
        }

        var file = Path.GetFileNameWithoutExtension(path);
        return new ModelScene
        {
            Name = file,
            Nodes = [new ModelNode { Name = file, Mesh = 0 }],
            Roots = [0],
            Meshes = [new ModelMesh { Name = file, Primitives = primitives }],
            Materials = materials,
            Warnings = warnings,
        };
    }

    private static Part Find(List<(string Material, Part Part)> parts, string material)
    {
        foreach (var (name, part) in parts)
            if (name == material) return part;
        var created = new Part();
        parts.Add((material, created));
        return created;
    }

    /// <summary>"v", "v/vt", "v//vn", "v/vt/vn" 하나를 그 재질 묶음의 꼭짓점 번호로.</summary>
    private static int Vertex(Part part, ReadOnlySpan<char> token, List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals, List<Vector4> colors, int lineNo)
    {
        int s1 = token.IndexOf('/');
        var vs = s1 < 0 ? token : token[..s1];
        ReadOnlySpan<char> ts = default, ns = default;
        if (s1 >= 0)
        {
            var after = token[(s1 + 1)..];
            int s2 = after.IndexOf('/');
            ts = s2 < 0 ? after : after[..s2];
            if (s2 >= 0) ns = after[(s2 + 1)..];
        }
        int v = Resolve(vs, positions.Count, lineNo);
        int t = ts.IsEmpty ? -1 : Resolve(ts, uvs.Count, lineNo);
        int n = ns.IsEmpty ? -1 : Resolve(ns, normals.Count, lineNo);

        if (part.Map.TryGetValue((v, t, n), out int index)) return index;
        index = part.Positions.Count;
        part.Map.Add((v, t, n), index);
        part.Positions.Add(positions[v]);
        part.Colors.Add(colors[v]);
        part.Uvs.Add(t >= 0 ? uvs[t] : Vector2.Zero);
        part.Normals.Add(n >= 0 ? normals[n] : Vector3.Zero);
        part.AnyUv |= t >= 0;
        part.AnyNormal |= n >= 0;
        part.MissingNormal |= n < 0;
        return index;
    }

    /// <summary>1부터 센 번호(음수 = 지금까지 읽은 것의 뒤에서부터)를 0부터 센 번호로.</summary>
    private static int Resolve(ReadOnlySpan<char> s, int count, int lineNo)
    {
        if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int i) || i == 0) throw Malformed(lineNo);
        int index = i > 0 ? i - 1 : count + i;
        if ((uint)index >= (uint)count) throw Malformed(lineNo);
        return index;
    }

    private static InvalidDataException Malformed(int lineNo) => new(CoreStrings.FormatObj_Malformed(line: lineNo));

    private static ReadOnlySpan<char> NextToken(ref ReadOnlySpan<char> s)
    {
        s = s.TrimStart();
        int end = 0;
        while (end < s.Length && !char.IsWhiteSpace(s[end])) end++;
        var token = s[..end];
        s = s[end..].TrimStart();
        return token;
    }

    /// <summary>앞에서부터 수를 <paramref name="dst"/> 길이만큼 읽고 읽은 개수를 돌려준다(수가 아닌 것을 만나면 멈춤).</summary>
    private static int Numbers(ReadOnlySpan<char> s, Span<float> dst)
    {
        int count = 0;
        while (count < dst.Length && !s.IsEmpty)
        {
            if (!float.TryParse(NextToken(ref s), NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) break;
            dst[count++] = f;
        }
        return count;
    }

    // ---- MTL ----

    private static void LoadMtlLib(string dir, string spec, List<ModelMaterial> materials, Dictionary<string, int> index,
        Dictionary<string, PixelImage?> textures, List<string> warnings)
    {
        if (spec.Length == 0) return;
        // 이름에 빈칸이 있을 수 있으니 줄 전체를 먼저, 없으면 빈칸으로 나눈 파일 여럿으로 본다.
        var whole = Path.Combine(dir, spec);
        if (File.Exists(whole))
        {
            ReadMtl(whole, materials, index, textures, warnings);
            return;
        }
        var names = spec.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 1)
        {
            ModelTexture.Warn(warnings, CoreStrings.FormatObj_Warning_MtlMissing(file: spec));
            return;
        }
        foreach (var name in names)
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) ReadMtl(p, materials, index, textures, warnings);
            else ModelTexture.Warn(warnings, CoreStrings.FormatObj_Warning_MtlMissing(file: name));
        }
    }

    private sealed class MtlEntry(string name)
    {
        public string Name = name;
        public Vector3? Kd;
        public float Alpha = 1;
        public Vector3 Ke;
        public PixelImage? Texture;
    }

    private static void ReadMtl(string path, List<ModelMaterial> materials, Dictionary<string, int> index,
        Dictionary<string, PixelImage?> textures, List<string> warnings)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        MtlEntry? entry = null;
        Span<float> nums = stackalloc float[3];

        void Finish()
        {
            if (entry == null) return;
            var alphaMode = entry.Alpha < 1 ? ModelAlphaMode.Blend
                : entry.Texture != null && ModelTexture.HasTransparency(entry.Texture) ? ModelAlphaMode.Mask
                : ModelAlphaMode.Opaque;
            // Kd가 없으면 텍스처는 그대로(1), 텍스처도 없으면 흔한 기본 회색.
            var kd = entry.Kd ?? (entry.Texture != null ? Vector3.One : new Vector3(0.8f));
            var material = new ModelMaterial
            {
                Name = entry.Name,
                BaseColor = new Vector4(kd, entry.Alpha),
                Texture = entry.Texture,
                AlphaMode = alphaMode,
                Emissive = entry.Ke,
            };
            if (index.TryGetValue(entry.Name, out int existing)) materials[existing] = material;
            else
            {
                index[entry.Name] = materials.Count;
                materials.Add(material);
            }
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.AsSpan().Trim();
            if (line.IsEmpty || line[0] == '#') continue;
            var rest = line;
            var key = NextToken(ref rest);
            if (key.SequenceEqual("newmtl"))
            {
                Finish();
                entry = new MtlEntry(rest.ToString());
                continue;
            }
            if (entry == null) continue;
            if (key.SequenceEqual("Kd"))
            {
                int n = Numbers(rest, nums);
                if (n >= 3) entry.Kd = new Vector3(nums[0], nums[1], nums[2]);
                else if (n >= 1) entry.Kd = new Vector3(nums[0]);
            }
            else if (key.SequenceEqual("Ke"))
            {
                int n = Numbers(rest, nums);
                if (n >= 3) entry.Ke = new Vector3(nums[0], nums[1], nums[2]);
                else if (n >= 1) entry.Ke = new Vector3(nums[0]);
            }
            else if (key.SequenceEqual("d"))
            {
                if (Numbers(rest, nums) >= 1) entry.Alpha = Math.Clamp(nums[0], 0, 1);
            }
            else if (key.SequenceEqual("Tr"))
            {
                if (Numbers(rest, nums) >= 1) entry.Alpha = Math.Clamp(1 - nums[0], 0, 1);
            }
            else if (key.SequenceEqual("map_Kd"))
            {
                // 앞에 -s 1 1 1 같은 선택 사항이 올 수 있으니 마지막 낱말을 파일 이름으로 본다.
                var file = LastToken(rest);
                if (!file.IsEmpty) entry.Texture = Texture(dir, file.ToString(), textures, warnings);
            }
        }
        Finish();
    }

    private static ReadOnlySpan<char> LastToken(ReadOnlySpan<char> s)
    {
        s = s.TrimEnd();
        int start = s.Length;
        while (start > 0 && !char.IsWhiteSpace(s[start - 1])) start--;
        return s[start..];
    }

    /// <summary>.mtl 옆의 텍스처(다른 컴퓨터의 절대 경로면 파일 이름만으로 .mtl 옆에서 찾음). 없으면 알림.</summary>
    private static PixelImage? Texture(string dir, string file, Dictionary<string, PixelImage?> cache, List<string> warnings)
    {
        var candidate = Path.Combine(dir, file.Replace('\\', Path.DirectorySeparatorChar));
        if (!File.Exists(candidate)) candidate = Path.Combine(dir, Path.GetFileName(file.Replace('\\', '/')));
        if (!File.Exists(candidate))
        {
            ModelTexture.Warn(warnings, CoreStrings.FormatModel_Warning_TextureMissing(file: file));
            return null;
        }
        var full = Path.GetFullPath(candidate);
        if (cache.TryGetValue(full, out var cached)) return cached;
        var image = ModelTexture.Decode(File.ReadAllBytes(full), file, warnings);
        cache[full] = image;
        return image;
    }
}
