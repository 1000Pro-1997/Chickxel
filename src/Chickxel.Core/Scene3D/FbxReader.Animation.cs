using System.Numerics;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Scene3D;

internal static partial class FbxReader
{
    /// <summary>회전을 다시 표본 낼 때 키 사이의 최대 간격(틱) — 성긴 오일러 키 사이도 사원수 보간이 오일러 보간을 따르게.</summary>
    private const double MaxRotationStep = TicksPerSecond / 30.0;

    /// <summary>다시 표본 내는 키 수의 상한(엄청 긴 애니메이션은 촘촘히 채우지 않고 원래 키만).</summary>
    private const int MaxResampledKeys = 200_000;

    /// <summary>
    /// AnimationCurve 하나: 키 시각(틱, 오름차순)·값과 키마다 '다음 키까지 그대로'(constant 보간) 여부.
    /// 3차 보간 키는 직선으로 본다(Mixamo는 프레임마다 키가 있어 차이가 거의 없음).
    /// </summary>
    private sealed class Curve(long[] ticks, float[] values, bool[] constant)
    {
        public readonly long[] Ticks = ticks;
        public readonly float[] Values = values;
        public readonly bool[] Constant = constant;

        /// <summary>마지막 키를 뺀 모든 키가 constant(키가 둘 이상) — 채널을 Step으로.</summary>
        public bool AllConstant
        {
            get
            {
                if (Ticks.Length < 2) return false;
                for (int i = 0; i < Ticks.Length - 1; i++)
                    if (!Constant[i]) return false;
                return true;
            }
        }

        /// <summary>시각 <paramref name="tick"/>의 값(첫 키 앞은 첫 값, 마지막 키 뒤는 마지막 값).</summary>
        public float Evaluate(double tick)
        {
            var t = Ticks;
            int n = t.Length;
            if (!(tick > t[0])) return Values[0];
            if (tick >= t[n - 1]) return Values[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (t[mid] <= tick) lo = mid;
                else hi = mid;
            }
            if (Constant[lo]) return Values[lo];
            double span = t[hi] - t[lo];
            double u = span > 0 ? (tick - t[lo]) / span : 0;
            return (float)(Values[lo] + (Values[hi] - Values[lo]) * u);
        }
    }

    /// <summary>노드 하나의 곡선 아홉 칸(이동 XYZ, 회전 XYZ, 크기 XYZ)과 AnimationCurveNode의 기본값(d|X…).</summary>
    private sealed class NodeCurves
    {
        public readonly Curve?[] Curves = new Curve?[9];
        public readonly float?[] Defaults = new float?[9];

        public bool Any(int path) => Curves[path * 3] != null || Curves[path * 3 + 1] != null || Curves[path * 3 + 2] != null;

        /// <summary>그 칸의 곡선이 모두 constant.</summary>
        public bool AllConstant(int path)
        {
            for (int i = path * 3; i < path * 3 + 3; i++)
                if (Curves[i] is { } c && !c.AllConstant) return false;
            return true;
        }
    }

    private sealed partial class Builder
    {
        private readonly Dictionary<FbxObject, Curve?> _curves = [];

        /// <summary>
        /// AnimationStack마다 애니메이션 하나(곡선이 하나도 없는 스택은 뺌). 층은 곡선이 있는 첫 층만 쓰고, 더 있으면 알림.
        /// 이름은 스택 이름(비었거나 Mixamo의 "mixamo.com"이면 파일 이름), 겹치면 " 2", " 3"을 붙인다.
        /// 시각은 스택 시작(LocalStart, 첫 키가 더 이르면 첫 키)을 0초로 맞춘다.
        /// </summary>
        private List<ModelAnimation> ReadAnimations()
        {
            var result = new List<ModelAnimation>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stack in _ordered)
            {
                if (stack.Kind != "AnimationStack") continue;
                FbxObject? layer = null;
                int layers = 0;
                foreach (var s in stack.Sources)
                    if (s.Object.Kind == "AnimationLayer" && FirstSource(s.Object, "AnimationCurveNode") != null)
                    {
                        layer ??= s.Object;
                        layers++;
                    }
                if (layer == null) continue;

                string name = stack.Name.Trim();
                if (name.Length == 0 || name.Equals("mixamo.com", StringComparison.OrdinalIgnoreCase)) name = _fileName;
                var channels = Channels(stack, layer);
                if (channels.Count == 0) continue;
                if (layers > 1) ModelTexture.Warn(_warnings, CoreStrings.FormatFbx_Warning_AnimationLayers(name: name));

                string unique = name;
                for (int k = 2; !names.Add(unique); k++) unique = $"{name} {k}";
                result.Add(new ModelAnimation { Name = unique, Channels = channels });
            }
            return result;
        }

        /// <summary>
        /// 층 하나의 채널들. 이동·크기는 세 성분 키 시각의 합집합에서(없는 성분은 그 곡선의 직선 보간이나 기본값) 직선 키로,
        /// 회전은 오일러 곡선을 같은 합집합(키 사이가 1/30초보다 멀면 그 사이도)에서 표본 내 앞·뒤 회전까지 합친 사원수 직선 키로(부호를 이어 붙임).
        /// 피벗이 있는 노드는 회전·크기가 이동을 바꾸므로 그 시각들에서 이동 채널도 만든다. 곡선이 모두 constant면 Step.
        /// </summary>
        private List<AnimationChannel> Channels(FbxObject stack, FbxObject layer)
        {
            var perNode = new Dictionary<int, NodeCurves>();
            long earliest = long.MaxValue;
            foreach (var s in layer.Sources)
            {
                var curveNode = s.Object;
                if (curveNode.Kind != "AnimationCurveNode") continue;
                foreach (var target in curveNode.Targets)
                {
                    if (target.Object.Kind != "Model") continue;
                    int path = target.Property switch
                    {
                        "Lcl Translation" => 0,
                        "Lcl Rotation" => 1,
                        "Lcl Scaling" => 2,
                        _ => -1,
                    };
                    if (path < 0) continue;
                    int node = target.Object.Index;
                    if (!perNode.TryGetValue(node, out var nc)) perNode[node] = nc = new NodeCurves();
                    for (int axis = 0; axis < 3; axis++)
                        if (FindP(curveNode.Properties, AxisProperty(axis)) is { } d && d[4].IsNumber && double.IsFinite(d[4].Number))
                            nc.Defaults[path * 3 + axis] = (float)d[4].Number;
                    foreach (var c in curveNode.Sources)
                    {
                        if (c.Object.Kind != "AnimationCurve" || c.Property is not { Length: 3 } prop || !prop.StartsWith("d|", StringComparison.Ordinal)) continue;
                        int axis = char.ToUpperInvariant(prop[2]) - 'X';
                        if ((uint)axis > 2) continue;
                        if (!_curves.TryGetValue(c.Object, out var curve)) _curves[c.Object] = curve = ReadCurve(c.Object);
                        if (curve == null) continue;
                        nc.Curves[path * 3 + axis] = curve;
                        earliest = Math.Min(earliest, curve.Ticks[0]);
                    }
                }
            }
            var channels = new List<AnimationChannel>();
            if (earliest == long.MaxValue) return channels;

            var startP = P(stack, "LocalStart");
            double offset = startP != null && startP[4].IsNumber ? Math.Min(startP[4].Integer, earliest) : earliest;

            var nodes = new List<int>(perNode.Keys);
            nodes.Sort();
            foreach (int node in nodes)
            {
                var nc = perNode[node];
                var info = _info[node];
                bool hasT = nc.Any(0), hasR = nc.Any(1), hasS = nc.Any(2);
                if (!hasT && !hasR && !hasS) continue;
                var rest = new float[9];
                Span<float> lcl = [info.Translation.X, info.Translation.Y, info.Translation.Z, info.Rotation.X, info.Rotation.Y, info.Rotation.Z, info.Scale.X, info.Scale.Y, info.Scale.Z];
                for (int i = 0; i < 9; i++) rest[i] = nc.Defaults[i] ?? lcl[i];

                double[] rTicks = hasR ? Union(nc, 1) : [];
                if (hasR && !nc.AllConstant(1)) rTicks = Densify(rTicks);
                double[] sTicks = hasS ? Union(nc, 2) : [];
                bool pivotMix = info.Pivoted && (hasR || hasS);
                double[] tTicks = hasT ? Union(nc, 0) : [];
                if (pivotMix) tTicks = Merge(tTicks, Merge(rTicks, sTicks));

                if (tTicks.Length > 0)
                {
                    var values = new float[tTicks.Length * 3];
                    for (int i = 0; i < tTicks.Length; i++)
                    {
                        var v = _axes.Point(Sample(info, nc, rest, tTicks[i]).T);
                        values[i * 3] = v.X;
                        values[i * 3 + 1] = v.Y;
                        values[i * 3 + 2] = v.Z;
                    }
                    bool step = nc.AllConstant(0) && (!pivotMix || (nc.AllConstant(1) && nc.AllConstant(2)));
                    channels.Add(Channel(node, AnimationPath.Translation, step, tTicks, offset, values));
                }
                if (rTicks.Length > 0)
                {
                    var values = new float[rTicks.Length * 4];
                    var previous = Quaternion.Identity;
                    for (int i = 0; i < rTicks.Length; i++)
                    {
                        var q = _axes.Rotation(Sample(info, nc, rest, rTicks[i]).R);
                        // q와 −q는 같은 회전 — 앞 키와 같은 쪽을 골라 보간이 먼 길로 돌지 않게.
                        if (i > 0 && Quaternion.Dot(q, previous) < 0) q = Quaternion.Negate(q);
                        previous = q;
                        values[i * 4] = q.X;
                        values[i * 4 + 1] = q.Y;
                        values[i * 4 + 2] = q.Z;
                        values[i * 4 + 3] = q.W;
                    }
                    channels.Add(Channel(node, AnimationPath.Rotation, nc.AllConstant(1), rTicks, offset, values));
                }
                if (sTicks.Length > 0)
                {
                    var values = new float[sTicks.Length * 3];
                    for (int i = 0; i < sTicks.Length; i++)
                    {
                        var v = _axes.Scale(Sample(info, nc, rest, sTicks[i]).S);
                        values[i * 3] = v.X;
                        values[i * 3 + 1] = v.Y;
                        values[i * 3 + 2] = v.Z;
                    }
                    channels.Add(Channel(node, AnimationPath.Scale, nc.AllConstant(2), sTicks, offset, values));
                }
            }
            return channels;
        }

        private static string AxisProperty(int axis) => axis switch { 0 => "d|X", 1 => "d|Y", _ => "d|Z" };

        private static AnimationChannel Channel(int node, AnimationPath path, bool step, double[] ticks, double offset, float[] values)
        {
            var times = new float[ticks.Length];
            for (int i = 0; i < ticks.Length; i++) times[i] = (float)((ticks[i] - offset) / TicksPerSecond);
            return new AnimationChannel
            {
                Node = node,
                Path = path,
                Interpolation = step ? AnimationInterpolation.Step : AnimationInterpolation.Linear,
                Times = times,
                Values = values,
            };
        }

        /// <summary>시각 <paramref name="tick"/>의 로컬 TRS(파일 좌표) — 곡선이 없는 성분은 <paramref name="rest"/>.</summary>
        private static (Vector3 T, Quaternion R, Vector3 S) Sample(NodeInfo info, NodeCurves nc, float[] rest, double tick)
        {
            var c = nc.Curves;
            float At(int i) => c[i] is { } curve ? curve.Evaluate(tick) : rest[i];
            return info.Local(new Vector3(At(0), At(1), At(2)), new Vector3(At(3), At(4), At(5)), new Vector3(At(6), At(7), At(8)));
        }

        /// <summary>한 칸(이동·회전·크기) 곡선들의 키 시각 합집합(오름차순, 겹침 없음).</summary>
        private static double[] Union(NodeCurves nc, int path)
        {
            double[] result = [];
            for (int i = path * 3; i < path * 3 + 3; i++)
            {
                if (nc.Curves[i] is not { } curve) continue;
                var ticks = new double[curve.Ticks.Length];
                for (int k = 0; k < ticks.Length; k++) ticks[k] = curve.Ticks[k];
                result = Merge(result, ticks);
            }
            return result;
        }

        /// <summary>오름차순 두 목록을 합친다(같은 시각은 하나).</summary>
        private static double[] Merge(double[] a, double[] b)
        {
            if (b.Length == 0) return a;
            if (a.Length == 0) return b;
            var result = new List<double>(a.Length + b.Length);
            int i = 0, j = 0;
            while (i < a.Length || j < b.Length)
            {
                double v = j >= b.Length || (i < a.Length && a[i] <= b[j]) ? a[i++] : b[j++];
                if (result.Count == 0 || v > result[^1]) result.Add(v);
            }
            return [.. result];
        }

        /// <summary>키 사이가 <see cref="MaxRotationStep"/>보다 멀면 고르게 사이 시각을 넣는다(1% 여유 — 1/30초 키에는 넣지 않음).</summary>
        private static double[] Densify(double[] ticks)
        {
            if (ticks.Length < 2 || (ticks[^1] - ticks[0]) / MaxRotationStep > MaxResampledKeys) return ticks;
            var result = new List<double>(ticks.Length);
            result.Add(ticks[0]);
            for (int i = 1; i < ticks.Length; i++)
            {
                double a = ticks[i - 1], gap = ticks[i] - a;
                int segments = Math.Max(1, (int)Math.Ceiling(gap / MaxRotationStep - 0.01));
                for (int s = 1; s < segments; s++) result.Add(a + gap * s / segments);
                result.Add(ticks[i]);
            }
            return [.. result];
        }

        /// <summary>
        /// AnimationCurve의 KeyTime(틱)·KeyValueFloat. 보간 표시(KeyAttrFlags, KeyAttrRefCount개 키마다 하나)에서 constant(0x02)만 가린다.
        /// 시각이 거꾸로 가는 키·값이 수가 아닌 키는 버린다. 키가 없으면 null.
        /// </summary>
        private static Curve? ReadCurve(FbxObject o)
        {
            var n = o.Node;
            var ticks = n.Child("KeyTime")?[0].ToLongs() ?? [];
            var values = (n.Child("KeyValueFloat") ?? n.Child("KeyValueDouble"))?[0].ToFloats() ?? [];
            int count = Math.Min(ticks.Length, values.Length);
            if (count == 0) return null;

            var flags = n.Child("KeyAttrFlags")?[0].ToInts() ?? [];
            var refs = n.Child("KeyAttrRefCount")?[0].ToInts() ?? [];
            var constant = new bool[count];
            int key = 0;
            for (int g = 0; g < flags.Length && key < count; g++)
            {
                // 무리 하나가 몇 키에 쓰이는지 — 없으면 남은 키 모두.
                int span = g < refs.Length ? refs[g] : count - key;
                bool isConstant = (flags[g] & 0x0E) == 0x02;
                for (int k = 0; k < span && key < count; k++) constant[key++] = isConstant;
            }

            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                if (!float.IsFinite(values[i]) || (kept > 0 && ticks[i] < ticks[kept - 1])) continue;
                ticks[kept] = ticks[i];
                values[kept] = values[i];
                constant[kept] = constant[i];
                kept++;
            }
            if (kept == 0) return null;
            return new Curve(ticks[..kept], values[..kept], constant[..kept]);
        }
    }
}
