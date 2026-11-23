using System.Globalization;
using System.Numerics;
using System.Text;

namespace GuideShots;

/// <summary>
/// 3D 모델 가이드의 예시 파일: 상자로 쌓은 병아리(GLB — 노드 7개, 재질 7개, 쉬는 애니메이션 "Idle"), 따로 받은 애니메이션 두 개(뼈대와 키만 든 GLB,
/// 뼈 이름 앞에 <c>mixamorig:</c> — 믹사모 Without Skin 파일처럼), 나무 상자(OBJ + MTL), 바닥 위 자판기(오브젝트 경계용 — 노드 셋),
/// 유리 수조(유리·반투명 재질용). 샷마다 코드로 만들어 저장소에 3D 파일을 두지 않는다.
/// </summary>
internal static class ModelSample
{
    /// <summary>병아리 노드(같은 순서·쉬는 자세를 애니메이션 파일도 씀): 이름, 부모, 쉬는 자리.</summary>
    private static readonly (string Name, int Parent, Vector3 At)[] Bones =
    [
        ("Hips", -1, new(0, 0.58f, 0)),
        ("Body", 0, Vector3.Zero),
        ("Head", 0, new(0, 0.5f, 0.08f)),
        ("WingL", 0, new(0.41f, 0.12f, 0)),
        ("WingR", 0, new(-0.41f, 0.12f, 0)),
        ("LegL", 0, new(0.17f, -0.33f, 0.02f)),
        ("LegR", 0, new(-0.17f, -0.33f, 0.02f)),
    ];

    private const int Hips = 0, Head = 2, WingL = 3, WingR = 4, LegL = 5, LegR = 6;

    /// <summary>재질(선형 RGB): 노랑 몸, 짙은 노랑 날개·꼬리, 주황 부리·다리, 빨간 볏, 검은 눈, 분홍 볼, 흰 눈빛.</summary>
    private static readonly (string Name, Vector3 Color)[] Materials =
    [
        ("Feather", new(1.0f, 0.70f, 0.08f)),
        ("Wing", new(0.92f, 0.47f, 0.04f)),
        ("Beak", new(1.0f, 0.26f, 0.02f)),
        ("Comb", new(0.80f, 0.04f, 0.04f)),
        ("Eye", new(0.02f, 0.02f, 0.03f)),
        ("Cheek", new(1.0f, 0.32f, 0.36f)),
        ("Shine", new(1, 1, 1)),
    ];

    private const int Feather = 0, Wing = 1, Beak = 2, Comb = 3, Eye = 4, Cheek = 5, Shine = 6;

    /// <summary>병아리 캐릭터(<c>chick.glb</c>)를 <paramref name="dir"/>에 쓴다.</summary>
    public static string Chick(string dir)
    {
        var g = new Glb();
        var meshes = new List<string>
        {
            g.Mesh("Body", [(Feather, Box(new(0, 0, 0), new(0.82f, 0.72f, 0.78f))), (Wing, Box(new(0, 0.22f, -0.44f), new(0.34f, 0.22f, 0.14f)))]),
            g.Mesh("Head",
            [
                (Feather, Box(new(0, 0.24f, 0), new(0.62f, 0.52f, 0.56f))),
                (Beak, Box(new(0, 0.17f, 0.33f), new(0.2f, 0.12f, 0.14f))),
                (Eye, Box(new(0.16f, 0.3f, 0.285f), new(0.08f, 0.12f, 0.02f)).Concat(Box(new(-0.16f, 0.3f, 0.285f), new(0.08f, 0.12f, 0.02f)))),
                (Shine, Box(new(0.175f, 0.33f, 0.297f), new(0.03f, 0.03f, 0.01f)).Concat(Box(new(-0.145f, 0.33f, 0.297f), new(0.03f, 0.03f, 0.01f)))),
                (Cheek, Box(new(0.24f, 0.16f, 0.285f), new(0.08f, 0.05f, 0.01f)).Concat(Box(new(-0.24f, 0.16f, 0.285f), new(0.08f, 0.05f, 0.01f)))),
                (Comb, Box(new(0, 0.56f, 0.04f), new(0.08f, 0.14f, 0.16f)).Concat(Box(new(0, 0.53f, -0.11f), new(0.08f, 0.09f, 0.12f)))),
            ]),
            g.Mesh("WingL", [(Wing, Box(new(0.04f, -0.14f, 0), new(0.08f, 0.36f, 0.46f)))]),
            g.Mesh("WingR", [(Wing, Box(new(-0.04f, -0.14f, 0), new(0.08f, 0.36f, 0.46f)))]),
            g.Mesh("Leg", [(Beak, Box(new(0, -0.08f, 0), new(0.07f, 0.18f, 0.07f)).Concat(Box(new(0, -0.2f, 0.06f), new(0.2f, 0.05f, 0.24f))))]),
        };
        int[] meshOf = [-1, 0, 1, 2, 3, 4, 4];
        var idle = g.Animation("Idle",
        [
            g.Translation(Hips, [0, 0.6f, 1.2f], [Bones[Hips].At, Bones[Hips].At - new Vector3(0, 0.02f, 0), Bones[Hips].At]),
            g.Rotation(Head, [0, 0.6f, 1.2f], [Quaternion.Identity, Rot(Vector3.UnitX, 6), Quaternion.Identity]),
            g.Rotation(WingL, [0, 0.6f, 1.2f], [Quaternion.Identity, Rot(Vector3.UnitZ, 12), Quaternion.Identity]),
            g.Rotation(WingR, [0, 0.6f, 1.2f], [Quaternion.Identity, Rot(Vector3.UnitZ, -12), Quaternion.Identity]),
        ]);
        var materials = string.Join(",", Materials.Select(m =>
            $"{{\"name\":\"{m.Name}\",\"pbrMetallicRoughness\":{{\"baseColorFactor\":[{F(m.Color.X)},{F(m.Color.Y)},{F(m.Color.Z)},1],\"metallicFactor\":0,\"roughnessFactor\":0.9}}}}"));
        var path = Path.Combine(dir, "chick.glb");
        File.WriteAllBytes(path, g.Build(Nodes("", meshOf), $"\"meshes\":[{string.Join(",", meshes)}],\"materials\":[{materials}],\"animations\":[{idle}]"));
        return path;
    }

    /// <summary>걷기 애니메이션만 든 파일(<c>chick_walk.glb</c> — 뼈 이름 앞에 mixamorig:, 메시 없음).</summary>
    public static string Walk(string dir)
    {
        var g = new Glb();
        float[] t = [0, 0.2f, 0.4f, 0.6f, 0.8f];
        var hip = Bones[Hips].At;
        var up = hip + new Vector3(0, 0.05f, 0);
        var anim = g.Animation("mixamo.com",
        [
            g.Translation(Hips, t, [hip, up, hip, up, hip]),
            g.Rotation(Hips, t, [Quaternion.Identity, Rot(Vector3.UnitZ, 5), Quaternion.Identity, Rot(Vector3.UnitZ, -5), Quaternion.Identity]),
            g.Rotation(LegL, t, [Quaternion.Identity, Rot(Vector3.UnitX, -35), Quaternion.Identity, Rot(Vector3.UnitX, 35), Quaternion.Identity]),
            g.Rotation(LegR, t, [Quaternion.Identity, Rot(Vector3.UnitX, 35), Quaternion.Identity, Rot(Vector3.UnitX, -35), Quaternion.Identity]),
            g.Rotation(WingL, t, [Rot(Vector3.UnitZ, 5), Rot(Vector3.UnitZ, 25), Rot(Vector3.UnitZ, 5), Rot(Vector3.UnitZ, 25), Rot(Vector3.UnitZ, 5)]),
            g.Rotation(WingR, t, [Rot(Vector3.UnitZ, -5), Rot(Vector3.UnitZ, -25), Rot(Vector3.UnitZ, -5), Rot(Vector3.UnitZ, -25), Rot(Vector3.UnitZ, -5)]),
            g.Rotation(Head, t, [Quaternion.Identity, Rot(Vector3.UnitX, 8), Quaternion.Identity, Rot(Vector3.UnitX, 8), Quaternion.Identity]),
        ]);
        var path = Path.Combine(dir, "chick_walk.glb");
        File.WriteAllBytes(path, g.Build(Nodes("mixamorig:", null), $"\"animations\":[{anim}]"));
        return path;
    }

    /// <summary>뛰기 애니메이션만 든 파일(<c>chick_jump.glb</c>).</summary>
    public static string Jump(string dir)
    {
        var g = new Glb();
        float[] t = [0, 0.2f, 0.45f, 0.7f, 1.0f];
        var hip = Bones[Hips].At;
        var anim = g.Animation("mixamo.com",
        [
            g.Translation(Hips, t, [hip, hip - new Vector3(0, 0.08f, 0), hip + new Vector3(0, 0.5f, 0), hip + new Vector3(0, 0.04f, 0), hip]),
            g.Rotation(WingL, t, [Quaternion.Identity, Rot(Vector3.UnitZ, 15), Rot(Vector3.UnitZ, 75), Rot(Vector3.UnitZ, 25), Quaternion.Identity]),
            g.Rotation(WingR, t, [Quaternion.Identity, Rot(Vector3.UnitZ, -15), Rot(Vector3.UnitZ, -75), Rot(Vector3.UnitZ, -25), Quaternion.Identity]),
            g.Rotation(LegL, t, [Quaternion.Identity, Rot(Vector3.UnitX, 20), Rot(Vector3.UnitX, -30), Rot(Vector3.UnitX, 10), Quaternion.Identity]),
            g.Rotation(LegR, t, [Quaternion.Identity, Rot(Vector3.UnitX, 20), Rot(Vector3.UnitX, -30), Rot(Vector3.UnitX, 10), Quaternion.Identity]),
            g.Rotation(Head, t, [Quaternion.Identity, Rot(Vector3.UnitX, 12), Rot(Vector3.UnitX, -10), Rot(Vector3.UnitX, 6), Quaternion.Identity]),
        ]);
        var path = Path.Combine(dir, "chick_jump.glb");
        File.WriteAllBytes(path, g.Build(Nodes("mixamorig:", null), $"\"animations\":[{anim}]"));
        return path;
    }

    /// <summary>매끈한 공(<c>ball.glb</c> — 꼭짓점 법선이 부드러운 구, 명암 단계 비교용).</summary>
    public static string Ball(string dir)
    {
        const int Rings = 24, Segments = 32;
        var positions = new List<Vector3>();
        var indices = new List<int>();
        for (int r = 0; r <= Rings; r++)
        {
            float v = MathF.PI * r / Rings;
            for (int s = 0; s <= Segments; s++)
            {
                float u = 2 * MathF.PI * s / Segments;
                positions.Add(new Vector3(MathF.Sin(v) * MathF.Sin(u), MathF.Cos(v), MathF.Sin(v) * MathF.Cos(u)) * 0.5f);
            }
        }
        for (int r = 0; r < Rings; r++)
            for (int s = 0; s < Segments; s++)
            {
                int a = r * (Segments + 1) + s, b = a + Segments + 1;
                indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
            }
        var normals = positions.Select(Vector3.Normalize).ToArray();
        var g = new Glb();
        var mesh = g.Mesh("Ball", [(Feather, new[] { (positions.ToArray(), normals, indices.ToArray()) })]);
        var materials = string.Join(",", Materials.Select(m =>
            $"{{\"name\":\"{m.Name}\",\"pbrMetallicRoughness\":{{\"baseColorFactor\":[{F(m.Color.X)},{F(m.Color.Y)},{F(m.Color.Z)},1],\"metallicFactor\":0,\"roughnessFactor\":0.9}}}}"));
        var path = Path.Combine(dir, "ball.glb");
        File.WriteAllBytes(path, g.Build("\"nodes\":[{\"name\":\"Ball\",\"mesh\":0}],\"scenes\":[{\"nodes\":[0]}],\"scene\":0", $"\"meshes\":[{mesh}],\"materials\":[{materials}]"));
        return path;
    }

    /// <summary>나무 상자(<c>crate.obj</c> + <c>crate.mtl</c> — 판자 색 둘, 띠 색 하나).</summary>
    public static string Crate(string dir)
    {
        var obj = new StringBuilder("mtllib crate.mtl\n");
        int vertices = 0;
        void Add(string material, Vector3 center, Vector3 size)
        {
            obj.Append("usemtl ").Append(material).Append('\n');
            var (positions, _, indices) = Box(center, size).Single();
            foreach (var p in positions) obj.Append(CultureInfo.InvariantCulture, $"v {F(p.X)} {F(p.Y)} {F(p.Z)}\n");
            for (int i = 0; i < indices.Length; i += 3)
                obj.Append(CultureInfo.InvariantCulture, $"f {vertices + indices[i] + 1} {vertices + indices[i + 1] + 1} {vertices + indices[i + 2] + 1}\n");
            vertices += positions.Length;
        }
        Add("Plank", new(0, 0.5f, 0), new(0.96f, 0.96f, 0.96f));
        foreach (float y in new[] { 0.06f, 0.94f })
        {
            Add("Band", new(0, y, 0.49f), new(1, 0.12f, 0.02f));
            Add("Band", new(0, y, -0.49f), new(1, 0.12f, 0.02f));
            Add("Band", new(0.49f, y, 0), new(0.02f, 0.12f, 1));
            Add("Band", new(-0.49f, y, 0), new(0.02f, 0.12f, 1));
        }
        Add("Dark", new(0, 0.5f, 0.495f), new(0.16f, 0.76f, 0.02f));
        Add("Dark", new(0.495f, 0.5f, 0), new(0.02f, 0.76f, 0.16f));
        File.WriteAllText(Path.Combine(dir, "crate.mtl"),
            "newmtl Plank\nKd 0.55 0.28 0.09\nnewmtl Band\nKd 0.32 0.33 0.36\nnewmtl Dark\nKd 0.30 0.13 0.04\n");
        var path = Path.Combine(dir, "crate.obj");
        File.WriteAllText(path, obj.ToString());
        return path;
    }

    /// <summary>
    /// 바닥 위 자판기(<c>kiosk.glb</c> — 노드 셋: 바닥판, 상자 여럿을 쌓은 자판기, 옆에 붙은 휴지통). 자판기와 휴지통이 바닥·서로에 맞닿아
    /// 깊이로는 경계가 없어, 오브젝트 경계 외곽선(노드마다 / 이어진 덩어리마다)의 차이를 보인다.
    /// </summary>
    public static string Kiosk(string dir)
    {
        var g = new Glb();
        const int Floor = 0, Body = 1, Panel = 2, Button = 3, Slot = 4, Bin = 5, Lid = 6;
        var meshes = new List<string>
        {
            g.Mesh("Floor", [(Floor, Box(new(0.15f, -0.05f, 0), new(1.9f, 0.1f, 1.2f)))]),
            g.Mesh("Machine",
            [
                (Body, Box(new(0, 0.75f, 0), new(0.8f, 1.5f, 0.6f))),
                (Panel, Box(new(-0.1f, 0.95f, 0.31f), new(0.46f, 0.8f, 0.04f))),
                (Button, Box(new(0.27f, 1.05f, 0.31f), new(0.12f, 0.4f, 0.04f))),
                (Slot, Box(new(-0.05f, 0.25f, 0.31f), new(0.5f, 0.16f, 0.04f))),
            ]),
            g.Mesh("Bin", [(Bin, Box(new(0, 0.3f, 0), new(0.36f, 0.6f, 0.36f))), (Lid, Box(new(0, 0.62f, 0), new(0.4f, 0.06f, 0.4f)))]),
        };
        (string Name, float R, float G, float B)[] materials =
        [
            ("Floor", 0.42f, 0.44f, 0.48f), ("Body", 0.75f, 0.08f, 0.08f), ("Panel", 0.12f, 0.32f, 0.6f), ("Button", 0.85f, 0.85f, 0.8f),
            ("Slot", 0.05f, 0.05f, 0.06f), ("Bin", 0.1f, 0.42f, 0.18f), ("Lid", 0.06f, 0.2f, 0.09f),
        ];
        var mats = string.Join(",", materials.Select(m => Material(m.Name, m.R, m.G, m.B)));
        const string nodes = "\"nodes\":[{\"name\":\"Floor\",\"mesh\":0},{\"name\":\"Machine\",\"mesh\":1},{\"name\":\"Bin\",\"mesh\":2,\"translation\":[0.58,0,0.08]}]," +
            "\"scenes\":[{\"nodes\":[0,1,2]}],\"scene\":0";
        var path = Path.Combine(dir, "kiosk.glb");
        File.WriteAllBytes(path, g.Build(nodes, $"\"meshes\":[{string.Join(",", meshes)}],\"materials\":[{mats}]"));
        return path;
    }

    /// <summary>
    /// 유리 수조(<c>aquarium.glb</c>): 나무 받침 위 유리 상자(KHR_materials_transmission·KHR_materials_ior), 안에 반투명 물(BLEND)과 모래·물풀·물고기,
    /// 위에 검은 뚜껑. 유리·물 너머로 바탕이 비쳐 반투명 픽셀이 생긴다(반투명 없애기 비교용).
    /// </summary>
    public static string Aquarium(string dir)
    {
        var g = new Glb();
        const int Wood = 0, Glass = 1, Water = 2, Sand = 3, Plant = 4, Fish = 5, Lid = 6;
        var meshes = new List<string>
        {
            g.Mesh("Stand", [(Wood, Box(new(0, 0.08f, 0), new(1.3f, 0.16f, 0.7f)))]),
            g.Mesh("Tank", [(Glass, Box(new(0, 0.56f, 0), new(1.2f, 0.8f, 0.6f)))]),
            g.Mesh("Water", [(Water, Box(new(0, 0.42f, 0), new(1.14f, 0.46f, 0.54f)))]),
            g.Mesh("Inside",
            [
                (Sand, Box(new(0, 0.22f, 0), new(1.14f, 0.1f, 0.54f))),
                (Plant, Box(new(-0.4f, 0.42f, -0.1f), new(0.08f, 0.32f, 0.08f)).Concat(Box(new(-0.3f, 0.36f, 0.05f), new(0.08f, 0.2f, 0.08f)))
                    .Concat(Box(new(0.42f, 0.4f, -0.12f), new(0.08f, 0.28f, 0.08f)))),
                (Fish, Box(new(0.05f, 0.55f, 0.02f), new(0.26f, 0.14f, 0.08f)).Concat(Box(new(0.22f, 0.55f, 0.02f), new(0.08f, 0.12f, 0.04f)))),
            ]),
            g.Mesh("Lid", [(Lid, Box(new(0, 0.98f, 0), new(1.24f, 0.04f, 0.64f)))]),
        };
        var mats = string.Join(",",
            Material("Wood", 0.35f, 0.17f, 0.06f),
            Material("Glass", 0.8f, 0.95f, 1f, extra: ",\"extensions\":{\"KHR_materials_transmission\":{\"transmissionFactor\":1},\"KHR_materials_ior\":{\"ior\":1.5}}", roughness: 0.05f),
            Material("Water", 0.15f, 0.5f, 0.85f, 0.3f, ",\"alphaMode\":\"BLEND\"", roughness: 0.2f),
            Material("Sand", 0.85f, 0.68f, 0.38f),
            Material("Plant", 0.08f, 0.55f, 0.14f),
            Material("Fish", 1f, 0.32f, 0.02f),
            Material("Lid", 0.05f, 0.05f, 0.06f));
        const string nodes = "\"nodes\":[{\"name\":\"Stand\",\"mesh\":0},{\"name\":\"Tank\",\"mesh\":1},{\"name\":\"Water\",\"mesh\":2},{\"name\":\"Inside\",\"mesh\":3},{\"name\":\"Lid\",\"mesh\":4}]," +
            "\"scenes\":[{\"nodes\":[0,1,2,3,4]}],\"scene\":0";
        var path = Path.Combine(dir, "aquarium.glb");
        File.WriteAllBytes(path, g.Build(nodes,
            $"\"extensionsUsed\":[\"KHR_materials_transmission\",\"KHR_materials_ior\"],\"meshes\":[{string.Join(",", meshes)}],\"materials\":[{mats}]"));
        return path;
    }

    /// <summary>glTF 재질 하나(선형 RGB 바탕 색, 금속 아님) — <paramref name="extra"/>는 뒤에 붙일 JSON 조각(알파 방식·확장).</summary>
    private static string Material(string name, float r, float g, float b, float a = 1, string extra = "", float roughness = 0.9f)
        => $"{{\"name\":\"{name}\",\"pbrMetallicRoughness\":{{\"baseColorFactor\":[{F(r)},{F(g)},{F(b)},{F(a)}],\"metallicFactor\":0,\"roughnessFactor\":{F(roughness)}}}{extra}}}";

    private static string Nodes(string prefix, int[]? meshOf)
    {
        var nodes = new List<string>();
        for (int i = 0; i < Bones.Length; i++)
        {
            var (name, _, at) = Bones[i];
            var children = Enumerable.Range(0, Bones.Length).Where(c => Bones[c].Parent == i).ToList();
            var sb = new StringBuilder($"{{\"name\":\"{prefix}{name}\",\"translation\":[{F(at.X)},{F(at.Y)},{F(at.Z)}]");
            if (children.Count > 0) sb.Append($",\"children\":[{string.Join(",", children)}]");
            if (meshOf is not null && meshOf[i] >= 0) sb.Append($",\"mesh\":{meshOf[i]}");
            nodes.Add(sb.Append('}').ToString());
        }
        return $"\"nodes\":[{string.Join(",", nodes)}],\"scenes\":[{{\"nodes\":[0]}}],\"scene\":0";
    }

    private static Quaternion Rot(Vector3 axis, float degrees) => Quaternion.CreateFromAxisAngle(axis, degrees * MathF.PI / 180);

    private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    /// <summary>축 정렬 상자 하나(면마다 꼭짓점 넷 — 면 법선이 또렷하게).</summary>
    private static IEnumerable<(Vector3[] Positions, Vector3[] Normals, int[] Indices)> Box(Vector3 c, Vector3 s)
    {
        var h = s / 2;
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        void Face(Vector3 n, Vector3 u, Vector3 v)
        {
            int b = positions.Count;
            var center = c + n * h;
            foreach (var (a, d) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                positions.Add(center + u * h * a + v * h * d);
                normals.Add(n);
            }
            indices.AddRange([b, b + 1, b + 2, b, b + 2, b + 3]);
        }
        // u × v = n가 되게(바깥에서 보면 반시계).
        Face(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY);
        Face(-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY);
        Face(Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ);
        Face(-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);
        Face(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
        Face(-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY);
        yield return ([.. positions], [.. normals], [.. indices]);
    }

    /// <summary>GLB 쓰기: 바이너리 버퍼 하나에 bufferView·accessor를 쌓고 JSON 조각을 받아 파일 바이트로.</summary>
    private sealed class Glb
    {
        private readonly MemoryStream _bin = new();
        private readonly List<string> _views = [];
        private readonly List<string> _accessors = [];

        private int View(ReadOnlySpan<byte> bytes)
        {
            while (_bin.Length % 4 != 0) _bin.WriteByte(0);
            long offset = _bin.Length;
            _bin.Write(bytes);
            _views.Add($"{{\"buffer\":0,\"byteOffset\":{offset},\"byteLength\":{bytes.Length}}}");
            return _views.Count - 1;
        }

        private int Floats(string type, int count, float[] data, string minMax = "")
        {
            int view = View(System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan()));
            _accessors.Add($"{{\"bufferView\":{view},\"componentType\":5126,\"count\":{count},\"type\":\"{type}\"{minMax}}}");
            return _accessors.Count - 1;
        }

        private int Vec3(Vector3[] v)
        {
            var min = v.Aggregate(Vector3.Min);
            var max = v.Aggregate(Vector3.Max);
            return Floats("VEC3", v.Length, [.. v.SelectMany(p => new[] { p.X, p.Y, p.Z })],
                $",\"min\":[{F(min.X)},{F(min.Y)},{F(min.Z)}],\"max\":[{F(max.X)},{F(max.Y)},{F(max.Z)}]");
        }

        private int Indices(int[] indices)
        {
            var data = indices.Select(i => (uint)i).ToArray();
            int view = View(System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan()));
            _accessors.Add($"{{\"bufferView\":{view},\"componentType\":5125,\"count\":{data.Length},\"type\":\"SCALAR\"}}");
            return _accessors.Count - 1;
        }

        /// <summary>메시 하나: 재질마다 상자들을 합친 primitive 하나.</summary>
        public string Mesh(string name, IEnumerable<(int Material, IEnumerable<(Vector3[] Positions, Vector3[] Normals, int[] Indices)> Boxes)> parts)
        {
            var primitives = new List<string>();
            foreach (var (material, boxes) in parts)
            {
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var indices = new List<int>();
                foreach (var (p, n, i) in boxes)
                {
                    int b = positions.Count;
                    positions.AddRange(p);
                    normals.AddRange(n);
                    indices.AddRange(i.Select(x => x + b));
                }
                int pos = Vec3([.. positions]), nor = Vec3([.. normals]), idx = Indices([.. indices]);
                primitives.Add($"{{\"attributes\":{{\"POSITION\":{pos},\"NORMAL\":{nor}}},\"indices\":{idx},\"material\":{material}}}");
            }
            return $"{{\"name\":\"{name}\",\"primitives\":[{string.Join(",", primitives)}]}}";
        }

        public (int Node, string Path, int Input, int Output) Translation(int node, float[] times, Vector3[] values)
            => (node, "translation", Floats("SCALAR", times.Length, times), Floats("VEC3", values.Length, [.. values.SelectMany(v => new[] { v.X, v.Y, v.Z })]));

        public (int Node, string Path, int Input, int Output) Rotation(int node, float[] times, Quaternion[] values)
            => (node, "rotation", Floats("SCALAR", times.Length, times), Floats("VEC4", values.Length, [.. values.SelectMany(q => new[] { q.X, q.Y, q.Z, q.W })]));

        public string Animation(string name, IEnumerable<(int Node, string Path, int Input, int Output)> channels)
        {
            var list = channels.ToList();
            var samplers = list.Select(c => $"{{\"input\":{c.Input},\"output\":{c.Output},\"interpolation\":\"LINEAR\"}}");
            var targets = list.Select((c, i) => $"{{\"sampler\":{i},\"target\":{{\"node\":{c.Node},\"path\":\"{c.Path}\"}}}}");
            return $"{{\"name\":\"{name}\",\"samplers\":[{string.Join(",", samplers)}],\"channels\":[{string.Join(",", targets)}]}}";
        }

        public byte[] Build(string nodes, string rest)
        {
            var bin = _bin.ToArray();
            var json = Encoding.UTF8.GetBytes($"{{\"asset\":{{\"version\":\"2.0\",\"generator\":\"Chickxel GuideShots\"}},\"buffers\":[{{\"byteLength\":{bin.Length}}}]," +
                $"\"bufferViews\":[{string.Join(",", _views)}],\"accessors\":[{string.Join(",", _accessors)}],{nodes},{rest}}}");
            int jsonLen = (json.Length + 3) & ~3, binLen = (bin.Length + 3) & ~3;
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
}
