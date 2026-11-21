using System.Numerics;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scene3D;

/// <summary>
/// FBX 바이너리 7.x 읽기(Mixamo의 "FBX Binary" 기준): Objects·Connections로 엮인 모델 노드 계층(Lcl 이동·회전·크기, 회전 순서,
/// 앞·뒤 회전, 피벗·오프셋, 메시에만 쓰는 기하 변환), 메시(다각형은 부채꼴로 나눔, 법선·UV·꼭짓점 색·재질 층), 재질(확산 색·투명도·확산 텍스처),
/// 뼈대(Skin·Cluster), 애니메이션(스택마다 하나, 첫 층만)을 읽는다.
/// 좌표는 GlobalSettings의 축(위·앞·옆)과 단위(UnitScaleFactor, cm 기준)를 읽어 오른손 +Y 위·앞 +Z·미터로 바꾼다 —
/// 위치·법선은 바꾼 좌표로 굽고, 노드·뼈대·애니메이션 변환은 축 바꾸기 행렬로 켤레(A⁻¹ × M × A)를 취해 같은 계층을 그대로 둔다.
/// 잘못된 번호·범위 밖 읽기는 모두 <see cref="InvalidDataException"/>으로 바꾼다.
/// </summary>
internal static partial class FbxReader
{
    /// <summary>FBX 시각 단위: 1초 = 46,186,158,000틱.</summary>
    public const long TicksPerSecond = 46_186_158_000;

    public static ModelScene Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        return Read(bytes, dir, Path.GetFileNameWithoutExtension(path));
    }

    /// <param name="dir">텍스처 파일을 찾을 폴더(null이면 파일 안에 넣은 텍스처만).</param>
    /// <param name="name">파일 이름(확장자 없이) — 장면 이름, 이름 없는 애니메이션 이름, 텍스처 폴더 "이름.fbm".</param>
    internal static ModelScene Read(byte[] bytes, string? dir, string name)
    {
        var top = FbxBinary.Parse(bytes, out _);
        return new Builder(top, dir, name).Build();
    }

    /// <summary>Objects 안의 객체 하나(Model·Geometry·Material·Deformer·Animation… — Kind = 노드 이름, Type = 세 번째 속성).</summary>
    private sealed class FbxObject(long id, string kind, string name, string type, FbxNode node)
    {
        public readonly long Id = id;
        public readonly string Kind = kind;
        public readonly string Name = name;
        public readonly string Type = type;
        public readonly FbxNode Node = node;
        public readonly FbxNode? Properties = node.Child("Properties70");
        /// <summary>이 객체로 이어진 것들(Connections의 앞쪽 — 자식·재료 쪽), 파일 순서대로.</summary>
        public readonly List<Link> Sources = [];
        /// <summary>이 객체가 이어진 대상들(Connections의 뒤쪽 — 부모 쪽).</summary>
        public readonly List<Link> Targets = [];
        /// <summary>장면 안 번호(모델 = 노드 번호, 재질 = 재질 번호, 없으면 −1).</summary>
        public int Index = -1;
    }

    /// <summary>연결 하나: 상대 객체와 속성 이름(OP 연결, OO면 null).</summary>
    private readonly record struct Link(FbxObject Object, string? Property);

    /// <summary>
    /// 파일 좌표 → 장면 좌표. <see cref="P"/> = 파일 축을 장면 축으로 옮기는 부호 있는 치환(행 벡터 규칙, 행 i = 파일 i축의 장면 방향),
    /// <see cref="K"/> = 길이 배율(파일 단위 → 미터). 위치 v' = v × P × K, 변환 M' = A⁻¹ × M × A (A = P × K).
    /// </summary>
    private readonly struct Axes
    {
        public readonly Matrix4x4 P, PInverse, PAbs;
        public readonly float K;
        public readonly bool Identity, Mirror;

        public Axes(Matrix4x4 p, float k)
        {
            P = p;
            PInverse = Matrix4x4.Transpose(p);   // 직교 행렬
            PAbs = new Matrix4x4(
                MathF.Abs(p.M11), MathF.Abs(p.M12), MathF.Abs(p.M13), 0,
                MathF.Abs(p.M21), MathF.Abs(p.M22), MathF.Abs(p.M23), 0,
                MathF.Abs(p.M31), MathF.Abs(p.M32), MathF.Abs(p.M33), 0,
                0, 0, 0, 1);
            K = k;
            Identity = p.IsIdentity;
            Mirror = p.GetDeterminant() < 0;
        }

        public Vector3 Point(Vector3 v) => Vector3.TransformNormal(v, P) * K;
        public Vector3 Direction(Vector3 v) => Vector3.TransformNormal(v, P);
        /// <summary>크기는 축만 바뀐다(부호는 켤레에서 지워짐).</summary>
        public Vector3 Scale(Vector3 s) => Vector3.TransformNormal(s, PAbs);

        public Quaternion Rotation(Quaternion q)
        {
            if (Identity) return q;
            var m = PInverse * Matrix4x4.CreateFromQuaternion(q) * P;
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        }

        public Matrix4x4 Matrix(Matrix4x4 m)
        {
            var r = PInverse * m * P;
            r.M41 *= K;
            r.M42 *= K;
            r.M43 *= K;
            return r;
        }
    }

    /// <summary>
    /// 모델 노드 하나의 FBX 변환 값(파일 좌표). 로컬 = T·Roff·Rp·Rpre·R·Rpost⁻¹·Rp⁻¹·Soff·Sp·S·Sp⁻¹(열 벡터 규칙)을
    /// 이동 t' = T + <see cref="Offset"/> + R'(<see cref="PivotB"/> + S∘<see cref="PivotC"/>), 회전 R' = Rpre·R·Rpost⁻¹, 크기 S로 푼다.
    /// </summary>
    private sealed class NodeInfo
    {
        public Vector3 Translation, Rotation, Scale = Vector3.One;   // Lcl 값(회전은 도)
        public int Order;
        public Quaternion Pre = Quaternion.Identity, PostInverse = Quaternion.Identity;
        /// <summary>Roff + Rp.</summary>
        public Vector3 Offset;
        /// <summary>Soff + Sp − Rp.</summary>
        public Vector3 PivotB;
        /// <summary>−Sp.</summary>
        public Vector3 PivotC;
        /// <summary>메시에만 쓰는 기하 변환(행 벡터 규칙, 파일 좌표).</summary>
        public Matrix4x4 Geometry = Matrix4x4.Identity;

        /// <summary>회전·크기가 이동에 섞이는지(피벗·크기 오프셋이 있음).</summary>
        public bool Pivoted => PivotB != Vector3.Zero || PivotC != Vector3.Zero;

        /// <summary>Lcl 값 <paramref name="t"/>·<paramref name="r"/>(도)·<paramref name="s"/>로 로컬 TRS(파일 좌표).</summary>
        public (Vector3 T, Quaternion R, Vector3 S) Local(Vector3 t, Vector3 r, Vector3 s)
        {
            var q = Quaternion.Normalize(Pre * Euler(r, Order) * PostInverse);
            var translation = t + Offset;
            if (Pivoted) translation += Vector3.Transform(PivotB + s * PivotC, q);
            return (translation, q, s);
        }
    }

    /// <summary>
    /// 오일러 각(도)을 사원수로. FBX 회전 순서 eEulerXYZ(0)는 X를 먼저, 그다음 Y, Z(열 벡터 Rz·Ry·Rx) —
    /// Hamilton 곱 a * b는 b를 먼저 돌리므로 qz * qy * qx. 1 XZY, 2 YZX, 3 YXZ, 4 ZXY, 5 ZYX, 6(구면 XYZ)은 XYZ로 본다.
    /// </summary>
    internal static Quaternion Euler(Vector3 degrees, int order)
    {
        const float rad = MathF.PI / 180;
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, degrees.X * rad);
        var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees.Y * rad);
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees.Z * rad);
        return order switch
        {
            1 => qy * qz * qx,
            2 => qx * qz * qy,
            3 => qz * qx * qy,
            4 => qy * qx * qz,
            5 => qx * qy * qz,
            _ => qz * qy * qx,
        };
    }

    /// <summary>FBX 행렬 16개(열 우선 열 벡터 규칙)를 차례로 M11..M44에 = 행 벡터 규칙(전치).</summary>
    private static Matrix4x4? ToMatrix(double[]? m)
    {
        if (m == null || m.Length < 16) return null;
        for (int i = 0; i < 16; i++)
            if (!double.IsFinite(m[i])) return null;
        return new Matrix4x4(
            (float)m[0], (float)m[1], (float)m[2], (float)m[3],
            (float)m[4], (float)m[5], (float)m[6], (float)m[7],
            (float)m[8], (float)m[9], (float)m[10], (float)m[11],
            (float)m[12], (float)m[13], (float)m[14], (float)m[15]);
    }

    /// <summary>"이름\0\x01종류" 꼴의 FBX 이름에서 이름만.</summary>
    private static string ObjectName(string? full)
    {
        if (string.IsNullOrEmpty(full)) return "";
        int sep = full.IndexOf("\0\u0001", StringComparison.Ordinal);
        return sep >= 0 ? full[..sep] : full;
    }

    private static InvalidDataException Malformed(string where) => new(CoreStrings.FormatFbx_Malformed(where: where));

    private sealed partial class Builder
    {
        private readonly List<FbxNode> _top;
        private readonly string? _dir;
        private readonly string _fileName;
        private readonly List<string> _warnings = [];
        private readonly Dictionary<long, FbxObject> _objects = [];
        private readonly List<FbxObject> _ordered = [];
        /// <summary>Definitions의 속성 틀: 객체 종류 → 속성 이름 → P 노드(객체에 없는 속성의 기본값).</summary>
        private readonly Dictionary<string, Dictionary<string, FbxNode>> _templates = new(StringComparer.Ordinal);
        private Axes _axes;

        private readonly List<FbxObject> _models = [];
        private NodeInfo[] _info = [];
        /// <summary>노드마다 쉬는 자세의 월드 행렬(파일 좌표, 행 벡터 규칙).</summary>
        private Matrix4x4[] _fileWorld = [];
        private int[] _parent = [];

        private readonly List<ModelMaterial> _materials = [];
        private readonly List<ModelMesh> _meshes = [];
        private readonly List<ModelSkin> _skins = [];
        private readonly Dictionary<FbxObject, SkinBinding?> _skinOf = [];
        private readonly Dictionary<FbxObject, int> _meshOf = [];
        private readonly Dictionary<FbxObject, PixelImage?> _textureOf = [];
        private readonly Dictionary<string, PixelImage?> _textureFiles = new(StringComparer.OrdinalIgnoreCase);

        public Builder(List<FbxNode> top, string? dir, string fileName)
        {
            _top = top;
            _dir = dir;
            _fileName = fileName;
        }

        public ModelScene Build()
        {
            ReadObjects();
            ReadAxes();
            ReadNodes();

            int n = _models.Count;
            var mesh = new int[n];
            var skin = new int[n];
            for (int i = 0; i < n; i++) (mesh[i], skin[i]) = ReadModelMesh(i);

            var animations = ReadAnimations();

            var children = new List<int>[n];
            for (int i = 0; i < n; i++) children[i] = [];
            var roots = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (_parent[i] >= 0) children[_parent[i]].Add(i);
                else roots.Add(i);
            }
            var nodes = new ModelNode[n];
            for (int i = 0; i < n; i++)
            {
                var info = _info[i];
                var (t, r, s) = info.Local(info.Translation, info.Rotation, info.Scale);
                nodes[i] = new ModelNode
                {
                    Name = _models[i].Name,
                    Parent = _parent[i],
                    Children = children[i],
                    Translation = _axes.Point(t),
                    Rotation = _axes.Rotation(r),
                    Scale = _axes.Scale(s),
                    Mesh = mesh[i],
                    Skin = skin[i],
                };
            }

            return new ModelScene
            {
                Name = _fileName,
                Nodes = nodes,
                Roots = roots,
                Meshes = _meshes,
                Materials = _materials,
                Skins = _skins,
                Animations = animations,
                Warnings = _warnings,
            };
        }

        // ---- 객체·연결·속성 ----

        private FbxNode? Top(string name)
        {
            foreach (var n in _top)
                if (n.Name == name) return n;
            return null;
        }

        private void ReadObjects()
        {
            if (Top("Objects") is { } objects)
                foreach (var node in objects.Children)
                {
                    var id = node[0];
                    if (id.Type is not ('L' or 'I')) continue;
                    var o = new FbxObject(id.Integer, node.Name, ObjectName(node[1].Text), node[2].Text ?? "", node);
                    if (_objects.TryAdd(o.Id, o)) _ordered.Add(o);
                }

            // C: "OO", 자식, 부모 / "OP", 자식, 부모, "속성 이름". 부모 0 = 장면 뿌리.
            if (Top("Connections") is { } connections)
                foreach (var c in connections.Children)
                {
                    if (c.Name != "C") continue;
                    if (!_objects.TryGetValue(c[1].Integer, out var source) || !_objects.TryGetValue(c[2].Integer, out var target)) continue;
                    if (source == target) continue;
                    string? property = c[0].Text == "OP" ? c[3].Text : null;
                    target.Sources.Add(new Link(source, property));
                    source.Targets.Add(new Link(target, property));
                }

            if (Top("Definitions") is { } definitions)
                foreach (var type in definitions.Children)
                {
                    if (type.Name != "ObjectType" || type[0].Text is not { } kind) continue;
                    foreach (var template in type.Children)
                    {
                        if (template.Name != "PropertyTemplate" || template.Child("Properties70") is not { } props) continue;
                        if (!_templates.TryGetValue(kind, out var map)) _templates[kind] = map = new Dictionary<string, FbxNode>(StringComparer.Ordinal);
                        foreach (var p in props.Children)
                            if (p.Name == "P" && p[0].Text is { } name) map.TryAdd(name, p);
                    }
                }
        }

        /// <summary>Properties70 안의 P 하나(없으면 null).</summary>
        private static FbxNode? FindP(FbxNode? props, string name)
        {
            if (props == null) return null;
            foreach (var p in props.Children)
                if (p.Name == "P" && p[0].Text == name) return p;
            return null;
        }

        /// <summary>객체의 속성(없으면 Definitions 틀의 기본값, 그것도 없으면 null). P의 값은 다섯 번째 속성부터.</summary>
        private FbxNode? P(FbxObject o, string name)
            => FindP(o.Properties, name) ?? (_templates.TryGetValue(o.Kind, out var map) && map.TryGetValue(name, out var p) ? p : null);

        private static double Number(FbxNode? p, double fallback)
            => p != null && p[4].IsNumber && double.IsFinite(p[4].Number) ? p[4].Number : fallback;

        private static Vector3? Vector(FbxNode? p)
        {
            if (p == null || !p[4].IsNumber || !p[5].IsNumber || !p[6].IsNumber) return null;
            var v = new Vector3((float)p[4].Number, (float)p[5].Number, (float)p[6].Number);
            return float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) ? v : null;
        }

        private double Number(FbxObject o, string name, double fallback) => Number(P(o, name), fallback);
        private Vector3 Vector(FbxObject o, string name, Vector3 fallback) => Vector(P(o, name)) ?? fallback;

        private static FbxObject? FirstSource(FbxObject o, string kind)
        {
            foreach (var s in o.Sources)
                if (s.Object.Kind == kind) return s.Object;
            return null;
        }

        // ---- 축·단위 ----

        /// <summary>
        /// GlobalSettings의 위(UpAxis)·앞(FrontAxis)·옆(CoordAxis) 축과 부호로 파일 축 → 장면 축(옆 → +X, 위 → +Y, 앞 → +Z)을,
        /// UnitScaleFactor(1 = cm)로 미터 배율을 정한다. 왼손 좌표면 거울 변환이 되어 메시를 구울 때 감기 방향을 뒤집는다.
        /// </summary>
        private void ReadAxes()
        {
            var props = Top("GlobalSettings")?.Child("Properties70");
            int up = (int)Number(FindP(props, "UpAxis"), 1), front = (int)Number(FindP(props, "FrontAxis"), 2), coord = (int)Number(FindP(props, "CoordAxis"), 0);
            float upSign = Number(FindP(props, "UpAxisSign"), 1) < 0 ? -1 : 1;
            float frontSign = Number(FindP(props, "FrontAxisSign"), 1) < 0 ? -1 : 1;
            float coordSign = Number(FindP(props, "CoordAxisSign"), 1) < 0 ? -1 : 1;
            if ((uint)up > 2 || (uint)front > 2 || (uint)coord > 2 || up == front || up == coord || front == coord)
            {
                (up, front, coord) = (1, 2, 0);
                upSign = frontSign = coordSign = 1;
            }
            var p = new Matrix4x4 { M44 = 1 };
            p[coord, 0] = coordSign;
            p[up, 1] = upSign;
            p[front, 2] = frontSign;

            double unit = Number(FindP(props, "UnitScaleFactor"), 1);
            if (!(unit > 0) || !double.IsFinite(unit)) unit = 1;
            _axes = new Axes(p, (float)(unit / 100));
        }

        // ---- 노드 ----

        private void ReadNodes()
        {
            foreach (var o in _ordered)
                if (o.Kind == "Model")
                {
                    o.Index = _models.Count;
                    _models.Add(o);
                }
            int n = _models.Count;
            _info = new NodeInfo[n];
            _parent = new int[n];
            for (int i = 0; i < n; i++)
            {
                _parent[i] = -1;
                foreach (var t in _models[i].Targets)
                    if (t.Object.Kind == "Model" && t.Property == null)
                    {
                        _parent[i] = t.Object.Index;
                        break;
                    }
                _info[i] = ReadNodeInfo(_models[i]);
            }

            // 부모 고리(잘못된 파일)는 그 자리에서 끊어 뿌리로 만든다.
            for (int i = 0; i < n; i++)
            {
                int k = _parent[i], steps = 0;
                while (k >= 0)
                {
                    if (++steps > n)
                    {
                        _parent[i] = -1;
                        break;
                    }
                    k = _parent[k];
                }
            }

            // 쉬는 자세 월드(파일 좌표): 부모를 먼저 계산한다(고리는 위에서 끊음).
            _fileWorld = new Matrix4x4[n];
            var done = new bool[n];
            var chain = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (done[i]) continue;
                chain.Clear();
                for (int k = i; k >= 0 && !done[k]; k = _parent[k]) chain.Add(k);
                for (int j = chain.Count - 1; j >= 0; j--)
                {
                    int idx = chain[j];
                    var info = _info[idx];
                    var (t, r, s) = info.Local(info.Translation, info.Rotation, info.Scale);
                    var local = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
                    _fileWorld[idx] = _parent[idx] >= 0 ? local * _fileWorld[_parent[idx]] : local;
                    done[idx] = true;
                }
            }
        }

        private NodeInfo ReadNodeInfo(FbxObject o)
        {
            var rp = Vector(o, "RotationPivot", Vector3.Zero);
            var sp = Vector(o, "ScalingPivot", Vector3.Zero);
            var info = new NodeInfo
            {
                Translation = Vector(o, "Lcl Translation", Vector3.Zero),
                Rotation = Vector(o, "Lcl Rotation", Vector3.Zero),
                Scale = Vector(o, "Lcl Scaling", Vector3.One),
                Order = (int)Number(o, "RotationOrder", 0),
                // 앞·뒤 회전은 회전 순서와 상관없이 늘 XYZ.
                Pre = Euler(Vector(o, "PreRotation", Vector3.Zero), 0),
                PostInverse = Quaternion.Conjugate(Euler(Vector(o, "PostRotation", Vector3.Zero), 0)),
                Offset = Vector(o, "RotationOffset", Vector3.Zero) + rp,
                PivotB = Vector(o, "ScalingOffset", Vector3.Zero) + sp - rp,
                PivotC = -sp,
            };
            var gt = Vector(o, "GeometricTranslation", Vector3.Zero);
            var gr = Vector(o, "GeometricRotation", Vector3.Zero);
            var gs = Vector(o, "GeometricScaling", Vector3.One);
            if (gt != Vector3.Zero || gr != Vector3.Zero || gs != Vector3.One)
                info.Geometry = Matrix4x4.CreateScale(gs) * Matrix4x4.CreateFromQuaternion(Euler(gr, 0)) * Matrix4x4.CreateTranslation(gt);
            return info;
        }

        // ---- 메시·뼈대 ----

        /// <summary>뼈대 하나: 장면 안 번호와 Cluster마다 (뼈대 안 뼈 번호, Cluster 노드).</summary>
        private sealed record SkinBinding(int Index, List<(int Joint, FbxNode Cluster)> Clusters);

        /// <summary>모델에 이어진 Geometry들을 메시 하나로(재질마다 프리미티브). 반환 = (메시 번호, 뼈대 번호), 없으면 −1.</summary>
        private (int Mesh, int Skin) ReadModelMesh(int node)
        {
            var model = _models[node];
            var info = _info[node];
            var geometries = new List<FbxObject>();
            foreach (var s in model.Sources)
            {
                if (s.Object.Kind != "Geometry" || s.Property != null) continue;
                if (s.Object.Type == "Mesh") geometries.Add(s.Object);
                else ModelTexture.Warn(_warnings, CoreStrings.FormatFbx_Warning_UnsupportedGeometry(type: s.Object.Type));
            }
            if (geometries.Count == 0) return (-1, -1);

            // 모델에 이어진 재질 순서 = 재질 층의 번호.
            var materials = new List<int>();
            foreach (var s in model.Sources)
                if (s.Object.Kind == "Material") materials.Add(Material(s.Object));

            bool shareable = geometries.Count == 1 && info.Geometry.IsIdentity;
            if (shareable && _meshOf.TryGetValue(geometries[0], out int cached))
                return (cached, SkinOf(geometries[0], node)?.Index ?? -1);

            var primitives = new List<ModelPrimitive>();
            SkinBinding? skin = null;
            foreach (var g in geometries)
            {
                var binding = SkinOf(g, node);
                // 메시 노드 하나는 뼈대 하나만 쓴다 — 다른 뼈대의 Geometry는 무게 없이 노드 변환으로 그린다.
                if (binding != null && skin != null && binding != skin) binding = null;
                skin ??= binding;
                primitives.AddRange(ReadGeometry(g, info, materials, binding));
            }
            if (primitives.Count == 0) return (-1, -1);
            int index = _meshes.Count;
            _meshes.Add(new ModelMesh { Name = model.Name, Primitives = primitives });
            if (shareable) _meshOf[geometries[0]] = index;
            return (index, skin?.Index ?? -1);
        }

        /// <summary>
        /// Geometry에 이어진 Skin 변형(없으면 null). 바인드 역행렬은 Cluster의 Transform(파일에는 뼈 바인드 월드⁻¹ × 메시 바인드 월드가 들어 있음 —
        /// 메시 좌표 꼭짓점을 뼈 좌표로 옮김, glTF inverseBindMatrices와 같은 뜻)이고, 없으면 메시 쉬는 월드 × TransformLink⁻¹.
        /// </summary>
        private SkinBinding? SkinOf(FbxObject geometry, int meshNode)
        {
            FbxObject? skin = null;
            foreach (var s in geometry.Sources)
            {
                if (s.Object.Kind != "Deformer") continue;
                if (s.Object.Type == "Skin") skin ??= s.Object;
                else if (s.Object.Type == "BlendShape") ModelTexture.Warn(_warnings, CoreStrings.Fbx_Warning_BlendShapes);
            }
            if (skin == null) return null;
            if (_skinOf.TryGetValue(skin, out var known)) return known;

            var joints = new List<int>();
            var inverseBinds = new List<Matrix4x4>();
            var clusters = new List<(int, FbxNode)>();
            foreach (var s in skin.Sources)
            {
                var cluster = s.Object;
                if (cluster.Kind != "Deformer" || cluster.Type != "Cluster") continue;
                var bone = FirstSource(cluster, "Model");
                if (bone == null) continue;
                var ibm = ToMatrix(cluster.Node.Child("Transform")?[0].ToDoubles());
                if (ibm == null)
                {
                    var link = ToMatrix(cluster.Node.Child("TransformLink")?[0].ToDoubles()) ?? _fileWorld[bone.Index];
                    ibm = _fileWorld[meshNode] * (Matrix4x4.Invert(link, out var inv) ? inv : Matrix4x4.Identity);
                }
                joints.Add(bone.Index);
                inverseBinds.Add(_axes.Matrix(ibm.Value));
                clusters.Add((joints.Count - 1, cluster.Node));
            }
            SkinBinding? binding = null;
            if (joints.Count > 0)
            {
                binding = new SkinBinding(_skins.Count, clusters);
                _skins.Add(new ModelSkin { Joints = joints, InverseBindMatrices = inverseBinds });
            }
            _skinOf[skin] = binding;
            return binding;
        }

        // ---- 재질·텍스처 ----

        /// <summary>
        /// Material 하나: 바탕 색 = DiffuseColor × DiffuseFactor(OBJ Kd처럼 선형으로 봄 — 확산 텍스처가 있으면 흰색, 텍스처가 색을 대신함),
        /// 알파 = 1 − TransparencyFactor(0이나 1이면 Opacity, 그것도 없으면 1 − TransparentColor — 내보내기마다 뜻이 달라 Blender와 같게 고름),
        /// 발광 = EmissiveColor × EmissiveFactor.
        /// </summary>
        private int Material(FbxObject m)
        {
            if (m.Index >= 0) return m.Index;
            var diffuse = Vector(P(m, "DiffuseColor")) ?? Vector(P(m, "Diffuse")) ?? new Vector3(0.8f);
            float factor = (float)Number(m, "DiffuseFactor", 1);
            double alpha = 1 - Number(m, "TransparencyFactor", 0);
            if (alpha is <= 0 or >= 1)
                alpha = FindP(m.Properties, "Opacity") is { } opacity ? Number(opacity, 1) : 1 - Vector(m, "TransparentColor", Vector3.Zero).X;
            alpha = Math.Clamp(alpha, 0, 1);
            var emissive = Vector(m, "EmissiveColor", Vector3.Zero) * (float)Number(m, "EmissiveFactor", 1);

            var (texture, repeat) = DiffuseTexture(m);
            var color = texture != null ? Vector3.One : Vector3.Clamp(diffuse * factor, Vector3.Zero, Vector3.One);
            var alphaMode = alpha < 1 ? ModelAlphaMode.Blend
                : texture != null && ModelTexture.HasTransparency(texture) ? ModelAlphaMode.Mask
                : ModelAlphaMode.Opaque;
            m.Index = _materials.Count;
            _materials.Add(new ModelMaterial
            {
                Name = m.Name,
                BaseColor = new Vector4(color, (float)alpha),
                Texture = texture,
                Repeat = repeat,
                AlphaMode = alphaMode,
                Emissive = Vector3.Max(emissive, Vector3.Zero),
            });
            return m.Index;
        }

        /// <summary>재질의 확산 색 자리(DiffuseColor, 없으면 이름에 diffuse·base color가 든 자리)에 이은 텍스처(LayeredTexture면 그 첫 텍스처).</summary>
        private (PixelImage? Image, bool Repeat) DiffuseTexture(FbxObject material)
        {
            FbxObject? texture = null;
            foreach (var s in material.Sources)
                if (s.Object.Kind is "Texture" or "LayeredTexture" && s.Property == "DiffuseColor")
                {
                    texture = s.Object;
                    break;
                }
            if (texture == null)
                foreach (var s in material.Sources)
                    if (s.Object.Kind is "Texture" or "LayeredTexture" && s.Property is { } prop
                        && (prop.Contains("diffuse", StringComparison.OrdinalIgnoreCase) || prop.Contains("base_color", StringComparison.OrdinalIgnoreCase)
                            || prop.Contains("basecolor", StringComparison.OrdinalIgnoreCase)))
                    {
                        texture = s.Object;
                        break;
                    }
            if (texture is { Kind: "LayeredTexture" }) texture = FirstSource(texture, "Texture");
            if (texture == null) return (null, true);

            bool repeat = Number(texture, "WrapModeU", 0) == 0 && Number(texture, "WrapModeV", 0) == 0;
            if (!_textureOf.TryGetValue(texture, out var image)) _textureOf[texture] = image = TextureImage(texture);
            return (image, repeat);
        }

        /// <summary>
        /// 텍스처 그림: Video의 Content(파일 안에 넣은 그림)를 먼저, 없으면 RelativeFilename·FileName을 FBX 옆에서 찾는다
        /// (적힌 경로, 파일 이름만, "파일이름.fbm/" 폴더 순). 못 찾으면 알림을 남기고 null.
        /// </summary>
        private PixelImage? TextureImage(FbxObject texture)
        {
            var video = FirstSource(texture, "Video");
            var names = new List<string>(4);
            void Add(string? name)
            {
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
            }
            Add(texture.Node.Child("RelativeFilename")?[0].Text);
            Add(texture.Node.Child("FileName")?[0].Text);
            if (video != null)
            {
                Add(video.Node.Child("RelativeFilename")?[0].Text);
                Add(video.Node.Child("Filename")?[0].Text);
            }
            string label = names.Count > 0 ? names[0] : texture.Name;

            if (video?.Node.Child("Content") is { } content && content[0].Raw.Length > 0)
                return ModelTexture.Decode(content[0].Raw, label, _warnings);

            if (_dir != null)
                foreach (var name in names)
                {
                    var path = name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    var file = Path.GetFileName(path);
                    foreach (var candidate in (ReadOnlySpan<string>)[Path.Combine(_dir, path), Path.Combine(_dir, file), Path.Combine(_dir, _fileName + ".fbm", file)])
                    {
                        if (file.Length == 0 || !File.Exists(candidate)) continue;
                        var full = Path.GetFullPath(candidate);
                        if (!_textureFiles.TryGetValue(full, out var image))
                            _textureFiles[full] = image = ModelTexture.Decode(File.ReadAllBytes(full), name, _warnings);
                        return image;
                    }
                }
            ModelTexture.Warn(_warnings, CoreStrings.FormatModel_Warning_TextureMissing(file: label));
            return null;
        }
    }
}
