using System.Numerics;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Scene3D;

internal static partial class FbxReader
{
    /// <summary>층 값이 무엇마다 하나인지(MappingInformationType).</summary>
    private enum LayerMapping
    {
        PolygonVertex,
        ControlPoint,
        Polygon,
        AllSame,
    }

    /// <summary>
    /// LayerElement 하나(법선·UV·색·재질): 값 배열(원소마다 <see cref="Comps"/>개)과, IndexToDirect면 번호 배열.
    /// </summary>
    private sealed class LayerData(float[] values, int[]? index, int comps, LayerMapping mapping)
    {
        /// <summary>다각형 꼭짓점 <paramref name="corner"/>(제어점 <paramref name="point"/>, 다각형 <paramref name="polygon"/>)의 값 시작 자리, 없으면 −1.</summary>
        public int At(int corner, int point, int polygon)
        {
            int key = mapping switch
            {
                LayerMapping.PolygonVertex => corner,
                LayerMapping.ControlPoint => point,
                LayerMapping.Polygon => polygon,
                _ => 0,
            };
            if (index != null)
            {
                if ((uint)key >= (uint)index.Length) return -1;
                key = index[key];
            }
            if (key < 0) return -1;
            long start = (long)key * comps;
            return start + comps <= values.Length ? (int)start : -1;
        }

        public float this[int i] => values[i];
    }

    /// <summary>재질 하나의 삼각형 모음(같은 꼭짓점 값 묶음은 꼭짓점 하나로).</summary>
    private sealed class MeshPart
    {
        public readonly Dictionary<VertexKey, int> Map = [];
        public readonly List<Vector3> Positions = [];
        public readonly List<Vector3> Normals = [];
        public readonly List<Vector2> Uvs = [];
        public readonly List<Vector4> Colors = [];
        public readonly List<int> Points = [];
        public readonly List<int> Indices = [];
    }

    private readonly record struct VertexKey(int Point, Vector3 Normal, Vector2 Uv, Vector4 Color);

    private sealed partial class Builder
    {
        /// <summary>
        /// Geometry(Mesh) 하나를 재질마다 프리미티브로: 제어점(Vertices)·다각형(PolygonVertexIndex — 음수 = ~번호, 다각형 끝)을 부채꼴 삼각형으로,
        /// 법선·UV(v를 뒤집어 (u, 1 − v))·꼭짓점 색은 다각형 꼭짓점마다 풀어 같은 값끼리 합친다. 위치·법선에는 기하 변환과 축 변환을 굽고,
        /// 둘을 합친 것이 거울이면 감기 방향을 뒤집는다. 뼈대가 있으면 제어점마다 무게 큰 뼈 넷(합 1)을 단다.
        /// </summary>
        private List<ModelPrimitive> ReadGeometry(FbxObject geometry, NodeInfo info, List<int> materials, SkinBinding? skin)
        {
            var g = geometry.Node;
            string where = $"Geometry '{geometry.Name}'";
            var vertices = g.Child("Vertices")?[0].ToDoubles() ?? [];
            var polygons = g.Child("PolygonVertexIndex")?[0].ToInts() ?? [];
            int points = vertices.Length / 3;
            if (points == 0 || polygons.Length == 0) return [];

            var geo = info.Geometry;
            bool hasGeometry = !geo.IsIdentity;
            var geoNormal = hasGeometry && Matrix4x4.Invert(geo, out var inv) ? Matrix4x4.Transpose(inv) : geo;
            bool flip = (hasGeometry && geo.GetDeterminant() < 0) != _axes.Mirror;

            var positions = new Vector3[points];
            for (int i = 0; i < points; i++)
            {
                var v = new Vector3((float)vertices[i * 3], (float)vertices[i * 3 + 1], (float)vertices[i * 3 + 2]);
                if (hasGeometry) v = Vector3.Transform(v, geo);
                positions[i] = _axes.Point(v);
            }

            var normals = Layer(g, "LayerElementNormal", "Normals", "NormalsIndex", 3, where);
            var uvs = Layer(g, "LayerElementUV", "UV", "UVIndex", 2, where);
            var colors = Layer(g, "LayerElementColor", "Colors", "ColorIndex", 4, where);
            var materialLayer = Layer(g, "LayerElementMaterial", "Materials", null, 1, where);

            int[]? pointJoints = null;
            float[]? pointWeights = null;
            if (skin != null) (pointJoints, pointWeights) = Weights(skin, points);

            var parts = new Dictionary<int, MeshPart>();
            var order = new List<int>();
            var corners = new List<int>(8);
            int polygon = 0, start = 0;
            for (int k = 0; k < polygons.Length; k++)
            {
                // 마지막 다각형에 끝 표시(음수)가 없어도 배열 끝에서 닫는다.
                if (polygons[k] >= 0 && k != polygons.Length - 1) continue;
                int count = k - start + 1;
                if (count >= 3)
                {
                    // 재질 층이 없으면 모델의 첫 재질(재질이 없으면 기본 재질).
                    int local = 0;
                    int first = polygons[start] < 0 ? ~polygons[start] : polygons[start];
                    int mi = materialLayer?.At(start, first, polygon) ?? -1;
                    if (mi >= 0) local = (int)materialLayer![mi];
                    int material = (uint)local < (uint)materials.Count ? materials[local] : -1;
                    if (!parts.TryGetValue(material, out var part))
                    {
                        parts[material] = part = new MeshPart();
                        order.Add(material);
                    }

                    corners.Clear();
                    for (int c = start; c <= k; c++)
                    {
                        int raw = polygons[c];
                        int point = raw < 0 ? ~raw : raw;
                        if ((uint)point >= (uint)points) throw Malformed(where + " PolygonVertexIndex");
                        var normal = Vector3.Zero;
                        int ni = normals?.At(c, point, polygon) ?? -1;
                        if (ni >= 0)
                        {
                            normal = new Vector3(normals![ni], normals[ni + 1], normals[ni + 2]);
                            if (hasGeometry) normal = Vector3.TransformNormal(normal, geoNormal);
                            normal = _axes.Direction(normal);
                            float len = normal.Length();
                            normal = len > 1e-12f && float.IsFinite(len) ? normal / len : Vector3.Zero;
                        }
                        var uv = Vector2.Zero;
                        int ui = uvs?.At(c, point, polygon) ?? -1;
                        if (ui >= 0) uv = new Vector2(uvs![ui], 1 - uvs[ui + 1]);
                        var color = Vector4.One;
                        int ci = colors?.At(c, point, polygon) ?? -1;
                        if (ci >= 0) color = new Vector4(colors![ci], colors[ci + 1], colors[ci + 2], colors[ci + 3]);

                        var key = new VertexKey(point, normal, uv, color);
                        if (!part.Map.TryGetValue(key, out int vertex))
                        {
                            vertex = part.Positions.Count;
                            part.Map[key] = vertex;
                            part.Positions.Add(positions[point]);
                            part.Normals.Add(normal);
                            part.Uvs.Add(uv);
                            part.Colors.Add(color);
                            part.Points.Add(point);
                        }
                        corners.Add(vertex);
                    }
                    for (int i = 1; i + 1 < corners.Count; i++)
                    {
                        part.Indices.Add(corners[0]);
                        part.Indices.Add(corners[flip ? i + 1 : i]);
                        part.Indices.Add(corners[flip ? i : i + 1]);
                    }
                }
                polygon++;
                start = k + 1;
            }

            var result = new List<ModelPrimitive>(order.Count);
            foreach (int material in order)
            {
                var part = parts[material];
                if (part.Indices.Count == 0) continue;
                int n = part.Positions.Count;
                int[]? joints = null;
                float[]? weights = null;
                if (pointJoints != null && pointWeights != null)
                {
                    joints = new int[n * 4];
                    weights = new float[n * 4];
                    for (int i = 0; i < n; i++)
                    {
                        int p = part.Points[i];
                        Array.Copy(pointJoints, p * 4, joints, i * 4, 4);
                        Array.Copy(pointWeights, p * 4, weights, i * 4, 4);
                    }
                }
                result.Add(new ModelPrimitive
                {
                    Positions = [.. part.Positions],
                    Normals = normals != null ? [.. part.Normals] : null,
                    Uvs = uvs != null ? [.. part.Uvs] : null,
                    Colors = colors != null ? [.. part.Colors] : null,
                    Indices = [.. part.Indices],
                    Material = material,
                    Joints = joints,
                    Weights = weights,
                });
            }
            return result;
        }

        /// <summary>
        /// 첫 <paramref name="element"/> 층(없거나 매핑 방식을 모르면 null — 모르는 방식은 알림).
        /// <paramref name="indexName"/>이 null이면(재질 층) 값 배열이 곧 번호.
        /// </summary>
        private LayerData? Layer(FbxNode geometry, string element, string valuesName, string? indexName, int comps, string where)
        {
            var layer = geometry.Child(element);
            if (layer == null) return null;
            var values = layer.Child(valuesName)?[0];
            if (values is not { IsArray: true } && values is not { IsNumber: true }) return null;
            string mappingText = layer.Child("MappingInformationType")?[0].Text ?? "";
            LayerMapping mapping;
            switch (mappingText)
            {
                case "ByPolygonVertex": mapping = LayerMapping.PolygonVertex; break;
                case "ByVertice" or "ByVertex" or "ByControlPoint": mapping = LayerMapping.ControlPoint; break;
                case "ByPolygon": mapping = LayerMapping.Polygon; break;
                case "AllSame": mapping = LayerMapping.AllSame; break;
                default:
                    ModelTexture.Warn(_warnings, CoreStrings.FormatFbx_Warning_LayerMapping(element: element, mapping: mappingText));
                    return null;
            }
            int[]? index = null;
            string reference = layer.Child("ReferenceInformationType")?[0].Text ?? "Direct";
            if (indexName != null && reference is "IndexToDirect" or "Index")
            {
                index = layer.Child(indexName)?[0].ToInts();
                if (index == null) throw Malformed($"{where} {element} {indexName}");
            }
            return new LayerData(values.Value.ToFloats(), index, comps, mapping);
        }

        /// <summary>제어점마다 무게가 가장 큰 뼈 넷과 그 무게(합이 1이 되게 맞춤, 뼈가 없는 점은 무게 0).</summary>
        private static (int[] Joints, float[] Weights) Weights(SkinBinding skin, int points)
        {
            var joints = new int[points * 4];
            var weights = new float[points * 4];
            foreach (var (joint, cluster) in skin.Clusters)
            {
                var indexes = cluster.Child("Indexes")?[0].ToInts();
                var values = cluster.Child("Weights")?[0].ToDoubles();
                if (indexes == null || values == null) continue;
                int n = Math.Min(indexes.Length, values.Length);
                for (int i = 0; i < n; i++)
                {
                    int p = indexes[i];
                    float w = (float)values[i];
                    if ((uint)p >= (uint)points || !(w > 0) || !float.IsFinite(w)) continue;
                    int b = p * 4, min = b;
                    for (int k = 1; k < 4; k++)
                        if (weights[b + k] < weights[min]) min = b + k;
                    if (w > weights[min])
                    {
                        weights[min] = w;
                        joints[min] = joint;
                    }
                }
            }
            for (int p = 0; p < points; p++)
            {
                int b = p * 4;
                float sum = weights[b] + weights[b + 1] + weights[b + 2] + weights[b + 3];
                if (!(sum > 0)) continue;
                float inv = 1 / sum;
                for (int k = 0; k < 4; k++) weights[b + k] *= inv;
            }
            return (joints, weights);
        }
    }
}
