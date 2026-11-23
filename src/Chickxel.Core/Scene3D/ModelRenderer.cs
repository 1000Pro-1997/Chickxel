using System.Numerics;
using System.Runtime.CompilerServices;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.Scene3D;

/// <summary>
/// 자세를 잡은 3D 모델(<see cref="PosedMesh"/>)을 CPU 소프트웨어 래스터라이저로 그림 한 장에 그린다.
/// <para>
/// 방향 규칙: 카메라는 맞춤 범위 가운데를 본다. <see cref="ModelRenderSettings.Yaw"/> 양수면 카메라가 +Y축 둘레로 +X 쪽으로 돌아(위에서 보면 반시계)
/// 모델 오른쪽(+X) 면이 보이고, <see cref="ModelRenderSettings.Pitch"/> 양수면 위에서 내려다본다(±90에서 멈춤). 빛은 카메라 기준 —
/// <see cref="ModelRenderSettings.LightYaw"/> 양수면 화면 오른쪽에서, <see cref="ModelRenderSettings.LightPitch"/> 양수면 위에서 온다.
/// </para>
/// <para>
/// 표본마다 깊이 버퍼(가까운 것이 이김)로 가리고, 뒷면을 버리지 않는다(한쪽 면만 내보낸 모델이 흔함 — 뒷면은 법선을 뒤집어 빛을 받음).
/// 반투명(<see cref="ModelAlphaMode.Blend"/>)·유리(<see cref="ModelMaterial.Transmission"/>) 재질은 불투명을 다 그린 뒤 표본마다 먼 겹부터 섞는다
/// (표본은 미리 곱한 색 + 알파). 섞은 겹은 서로 가리지 않고 불투명에만 가려지며, 가장 가까운 겹의 깊이·법선은 외곽선용으로 따로 남겨 외곽선이 유리 겉면을 따른다.
/// 텍스처는 가장 가까운 텍셀을 그대로 쓴다(픽셀 그림에는 흐린 쌍선형보다 또렷한 쪽이 낫고, 부드러움은 표본 나눔이 맡는다).
/// 색 계산과 표본 평균은 선형광에서 하고 끝에 sRGB 8비트로 바꾼다.
/// </para>
/// </summary>
public static class ModelRenderer
{
    /// <summary>내부 표본 버퍼 전체(가로·세로 × 표본 수²)의 상한 — 넘으면 표본 수를 줄인다.</summary>
    private const long MaxSamples = 4096L * 4096L;
    /// <summary>한 번에 그리는 띠(가로줄 묶음)의 표본 수 상한 — 큰 그림은 띠로 나눠 메모리를 묶어 둔다.</summary>
    private const int BandSamples = 1 << 21;
    /// <summary>취소 확인 간격(삼각형 수).</summary>
    private const int CancelBatch = 256;
    /// <summary>꼭짓점 화면 좌표를 맞추는 격자(1/256 픽셀) — 이웃 삼각형의 공유 변이 같은 값이 되어 틈·겹침이 없다.</summary>
    private const double Snap = 256;
    /// <summary>격자에 맞춘 좌표에서 변 함수가 가질 수 있는 가장 작은 양수(1/256 × 1/256) — 위·왼쪽 규칙의 "0 초과"를 "≥ 이 값"으로 바꾼다.</summary>
    private const double EdgeQuantum = 1.0 / (Snap * Snap);
    /// <summary>표본 알파(위 16비트 — 0이면 빈 표본). 불투명 표본은 꽉 찬 값, 색은 알파를 미리 곱한 선형 16비트.</summary>
    private const int AlphaShift = 48;
    private const ulong OpaqueAlpha = 0xFFFFUL << AlphaShift;
    /// <summary>둘레 하늘(선형): 꼭대기·지평선·땅 — 유리·반투명이 비추는 색.</summary>
    private static readonly Vector3 SkyZenith = new(0.30f, 0.45f, 0.75f), SkyHorizon = new(0.80f, 0.85f, 0.90f), SkyGround = new(0.10f, 0.10f, 0.11f);
    /// <summary>반짝임 날카로움의 상한을 정하는 가장 작은 거칠기.</summary>
    private const float MinRoughness = 0.12f;
    /// <summary>표본 하나에 섞는 반투명 겹의 상한(가까운 것부터 남고 그 너머는 버림) — 유리 상자 몇 겹·병 안 액체까지 넉넉하다.</summary>
    private const int MaxBlendLayers = 16;

    /// <summary>선형 16비트 → sRGB 8비트.</summary>
    private static readonly byte[] ToSrgb = BuildToSrgb();

    private static byte[] BuildToSrgb()
    {
        var t = new byte[65536];
        for (int i = 0; i < t.Length; i++)
        {
            double c = i / 65535.0;
            double s = c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
            t[i] = (byte)Math.Clamp((int)Math.Round(s * 255), 0, 255);
        }
        return t;
    }

    /// <summary>선형 RGB(0~1 밖은 자름) → 불투명 sRGB 8비트 — 바탕 색 층(<see cref="ModelRenderLayers.Albedo"/>)과 같은 반올림.</summary>
    internal static Color32 Srgb8(Vector3 linear) => new(ToSrgb[Quantize(linear.X)], ToSrgb[Quantize(linear.Y)], ToSrgb[Quantize(linear.Z)]);

    private static int Quantize(float v) => v >= 1 ? 65535 : v > 0 ? (int)(v * 65535 + 0.5f) : 0;

    /// <summary>
    /// 삼각형 꼭짓점들이 차지하는 축 정렬 상자(번호가 가리키는 위치만, NaN·무한대는 뺌). 그릴 것이 없으면 <see cref="ModelBounds.Empty"/>.
    /// </summary>
    public static ModelBounds Bounds(PosedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;
        foreach (var prim in mesh.Primitives)
        {
            var pos = prim.Positions;
            var idx = prim.Indices;
            int n = idx.Length - idx.Length % 3;
            for (int i = 0; i < n; i++)
            {
                int k = idx[i];
                if ((uint)k >= (uint)pos.Length) continue;
                var p = pos[k];
                if (!IsFinite(p)) continue;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }
        }
        return any ? new ModelBounds(min, max) : ModelBounds.Empty;
    }

    /// <summary>
    /// <paramref name="mesh"/>를 <paramref name="settings"/>대로 그린다. 카메라는 <paramref name="fit"/>(보통 <see cref="Bounds"/> 또는 여러 자세를 합친 범위)에 맞춘다.
    /// 범위가 비었거나 그릴 삼각형이 없으면 바탕만 칠한 그림.
    /// <para>
    /// 순서: <see cref="RenderLayers"/> → <see cref="ModelRenderSettings.HardAlpha"/>면 <see cref="ModelPixelPass.HardAlpha"/> →
    /// <see cref="ModelRenderSettings.Colors"/>가 있으면 이 그림 한 장만의 팔레트로 줄임(<see cref="ModelPixelPass.Palette"/> — 여러 프레임이 한 팔레트를
    /// 나눠 쓰려면 <see cref="ModelFrames.Render"/>) → 외곽선(<see cref="ModelPixelPass.Outline"/>).
    /// </para>
    /// </summary>
    public static PixelImage Render(PosedMesh mesh, ModelRenderSettings settings, ModelBounds fit, CancellationToken ct = default)
    {
        var layers = RenderLayers(mesh, settings, fit, ct);
        var image = layers.Image;
        if (settings.HardAlpha) ModelPixelPass.HardAlpha(image);
        if (settings.Colors > 0) ModelPixelPass.Remap(image, ModelPixelPass.Palette([image], settings.Colors));
        if (settings.HasOutline) ModelPixelPass.Outline(layers, settings);
        return image;
    }

    /// <summary>
    /// <see cref="Render"/>의 그리기 단계만: 그림(다듬기 전)과 픽셀마다 덮음·깊이·법선(<see cref="ModelRenderLayers"/>).
    /// 반투명 없애기·색 줄이기·외곽선은 하지 않는다. 겉 외곽선을 바깥에 그리면(<see cref="ModelRenderSettings.OutlineOuter"/> +
    /// <see cref="ModelRenderSettings.OutlineOutside"/>) 둘레 구를 짧은 변 − 2 × 선 두께에 맞춰, 확대 1에서도 선이 그림 밖으로 잘리지 않게 한다.
    /// </summary>
    public static ModelRenderLayers RenderLayers(PosedMesh mesh, ModelRenderSettings settings, ModelBounds fit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(settings);
        ct.ThrowIfCancellationRequested();
        var s = Sanitized.From(settings);
        if (fit.IsEmpty || !IsFinite(fit.Min) || !IsFinite(fit.Max) || !float.IsFinite(fit.Radius))
        {
            var empty = new ModelRenderLayers(new PixelImage(s.Width, s.Height), 0, false);
            empty.Image.Clear(s.Background);
            return empty;
        }
        var rasterizer = new Rasterizer(s, fit);
        var layers = new ModelRenderLayers(new PixelImage(s.Width, s.Height), rasterizer.PixelScale, s.Perspective);
        rasterizer.Run(mesh, layers, ct);
        return layers;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static float Finite(float v, float fallback) => float.IsFinite(v) ? v : fallback;

    /// <summary>범위를 맞춘 설정 값.</summary>
    private readonly struct Sanitized
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public int Supersample { get; init; }
        public float Yaw { get; init; }
        public float Pitch { get; init; }
        public float Roll { get; init; }
        public bool Perspective { get; init; }
        public float FieldOfView { get; init; }
        public float Zoom { get; init; }
        public float PanX { get; init; }
        public float PanY { get; init; }
        public float LightYaw { get; init; }
        public float LightPitch { get; init; }
        public bool LightBack { get; init; }
        public float Ambient { get; init; }
        public float LightStrength { get; init; }
        public int ShadeSteps { get; init; }
        public bool Textures { get; init; }
        public Color32 Background { get; init; }
        public ModelOutlineObjects Objects { get; init; }
        /// <summary>겉 외곽선을 바깥에 그릴 때 둘레에 남길 결과 픽셀 수(선 두께, 아니면 0).</summary>
        public int OutlineMargin { get; init; }

        public static Sanitized From(ModelRenderSettings s)
        {
            int w = Math.Clamp(s.Width, 1, PixelImage.MaxSize);
            int h = Math.Clamp(s.Height, 1, PixelImage.MaxSize);
            int ss = Math.Clamp(s.Supersample, 1, 8);
            while (ss > 1 && (long)w * h * ss * ss > MaxSamples) ss--;
            int steps = s.ShadeSteps < 2 ? 0 : Math.Min(s.ShadeSteps, 16);
            float zoom = Finite(s.Zoom, 1);
            return new Sanitized
            {
                Width = w,
                Height = h,
                Supersample = ss,
                Yaw = Finite(s.Yaw, 0),
                Pitch = Math.Clamp(Finite(s.Pitch, 0), -90, 90),
                Roll = Finite(s.Roll, 0),
                Perspective = s.Projection == ModelProjection.Perspective,
                FieldOfView = Math.Clamp(Finite(s.FieldOfView, 35), 5, 120),
                Zoom = zoom > 0 ? Math.Clamp(zoom, 1e-3f, 1e4f) : 1,
                PanX = Finite(s.PanX, 0),
                PanY = Finite(s.PanY, 0),
                LightYaw = Finite(s.LightYaw, 0),
                LightPitch = Finite(s.LightPitch, 0),
                LightBack = s.LightBack,
                Ambient = Math.Clamp(Finite(s.Ambient, 0.4f), 0, 1),
                LightStrength = Math.Clamp(Finite(s.LightStrength, 0.8f), 0, 2),
                ShadeSteps = steps,
                Textures = s.Textures,
                Background = s.Background,
                Objects = s.OutlineObjects,
                OutlineMargin = s.OutlineOuter && s.OutlineOutside ? Math.Clamp(s.OutlineWidth, 1, 3) : 0,
            };
        }
    }

    /// <summary>래스터화할 꼭짓점: 표본 버퍼 좌표(격자에 맞춤)·깊이 키(작을수록 가까움)·1/w(원근 보정용, 정사영은 1)와 속성.</summary>
    private struct RasterVertex
    {
        public double X, Y;
        public float Z, Iw;
        public Vector3 N;
        public Vector2 Uv;
        public Vector4 C;
    }

    /// <summary>가까운 면 자르기용 꼭짓점(카메라 좌표 — x 오른쪽, y 위, w 앞으로의 거리).</summary>
    private struct ClipVertex
    {
        public Vector3 View;
        public Vector3 N;
        public Vector2 Uv;
        public Vector4 C;

        public static ClipVertex Lerp(in ClipVertex a, in ClipVertex b, float t) => new()
        {
            View = Vector3.Lerp(a.View, b.View, t),
            N = Vector3.Lerp(a.N, b.N, t),
            Uv = Vector2.Lerp(a.Uv, b.Uv, t),
            C = Vector4.Lerp(a.C, b.C, t),
        };
    }

    /// <summary>삼각형 묶음 하나를 그릴 준비: 꼭짓점마다 한 번 계산한 화면 좌표와 재질 값.</summary>
    private sealed class PreparedPrimitive
    {
        public required PosedPrimitive Source;
        public required double[] X, Y;
        public required float[] Z, Iw;
        /// <summary>카메라 좌표(원근일 때만 — 가까운 면 자르기에 씀).</summary>
        public Vector3[]? View;
        /// <summary>꼭짓점 상태: 0 = 그림, 1 = 가까운 면 뒤(원근), 2 = 잘못된 값.</summary>
        public required byte[] State;
        public bool HasNormals, HasColors, HasUvs;
        public Vector4 Base;
        public Color32[]? Texture;
        public int TexWidth, TexHeight;
        public bool Repeat;
        /// <summary>이 값보다 알파가 작으면 버림(<see cref="ModelAlphaMode.Mask"/>만).</summary>
        public bool AlphaTest;
        public float Cutoff;
        public Vector3 Emissive;
        public bool Unlit;
        /// <summary>반투명·유리: 불투명 뒤에 표본마다 먼 겹부터 섞어 그림(깊이 벗기기).</summary>
        public bool Blend;
        public float Transmission;
        /// <summary>정면 반사율, 비침 세기(1 − 거칠기), 반짝임 지수.</summary>
        public float F0, Gloss, Shininess;
        /// <summary>오브젝트 번호(노드마다) — 이어진 덩어리로 가르면 삼각형마다 <see cref="TriObject"/>.</summary>
        public int ObjectId;
        public int[]? TriObject;
    }

    private sealed class Rasterizer
    {
        private readonly Sanitized _s;
        private readonly int _ss, _bw, _bh;
        private readonly bool _persp;
        private readonly Vector3 _center, _eye, _right, _up, _back, _light;
        /// <summary>정사영: 화면 짧은 변 절반에 해당하는 월드 길이. 원근: tan(반 시야각) / 확대.</summary>
        private readonly float _half, _tan, _near;
        private readonly double _cx, _cy, _scale;
        private readonly float _ambient, _strength, _envScale;
        private readonly int _steps;
        private readonly float _invRadius;

        /// <summary>결과 픽셀 한 칸의 월드 크기 ÷ 맞춤 반지름(정사영) 또는 그 값 ÷ 깊이(원근 — <see cref="ModelRenderLayers.PixelSize"/>).</summary>
        public float PixelScale { get; }

        // 현재 띠(표본 버퍼 가로줄 [_bandY0, _bandY1)).
        /// <summary>불투명 표본 깊이 — 가림은 이것만 본다.</summary>
        private float[] _depth = [];
        /// <summary>
        /// 섞어 그린 가장 가까운 겹의 깊이(섞을 것이 없으면 빈 배열). 섞은 겹은 서로 가리지 않고 표본마다 먼 겹부터 벗겨 섞으므로(깊이 벗기기)
        /// 이것은 외곽선용 깊이·법선을 고르는 데만 쓴다.
        /// </summary>
        private float[] _blendDepth = [];
        /// <summary>
        /// 깊이 벗기기: 표본마다 이미 섞은 겹 중 가장 가까운 것의 (깊이, 삼각형 번호)와, 이번 차례에 고른 그다음 먼 겹.
        /// 같은 깊이는 번호로 가른다(번호가 클수록 먼저 = 아래).
        /// </summary>
        private float[] _peelZ = [], _pickZ = [];
        private int[] _peelId = [], _pickId = [];
        private PeelPass _pass;
        private int _triId;
        private bool _picked;
        private ulong[] _color = [];
        /// <summary>표본 법선(월드, 카메라 쪽으로 뒤집은 단위 벡터를 8면체 16:16으로 묶음) — 덮인 표본만 뜻이 있다.</summary>
        private uint[] _normal = [];
        /// <summary>표본 바탕 색(빛을 받기 전, sRGB 0xRRGGBB) — 법선과 같은 표본이 쓴다(색 바뀌는 곳 외곽선용).</summary>
        private uint[] _albedo = [];
        /// <summary>표본 오브젝트 번호 — 법선과 같은 표본이 쓴다(오브젝트 경계 외곽선용).</summary>
        private int[] _object = [];
        /// <summary>지금 그리는 삼각형의 오브젝트 번호.</summary>
        private int _curObject;
        private int _bandY0, _bandY1;
        private readonly ClipVertex[] _clipIn = new ClipVertex[3];
        private readonly ClipVertex[] _clipOut = new ClipVertex[4];

        public Rasterizer(in Sanitized s, ModelBounds fit)
        {
            _s = s;
            _ss = s.Supersample;
            _bw = s.Width * _ss;
            _bh = s.Height * _ss;
            _persp = s.Perspective;
            float yaw = s.Yaw * (MathF.PI / 180), pitch = s.Pitch * (MathF.PI / 180);
            // 카메라의 시선은 그대로 두고 화면의 수평·수직 축만 돌린다. 양수 roll은 그림을 시계 방향으로 돌린다.
            _back = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
            var right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
            var up = Vector3.Normalize(Vector3.Cross(_back, right));
            float roll = s.Roll * (MathF.PI / 180);
            _right = right * MathF.Cos(roll) + up * MathF.Sin(roll);
            _up = up * MathF.Cos(roll) - right * MathF.Sin(roll);
            float ly = s.LightYaw * (MathF.PI / 180), lp = s.LightPitch * (MathF.PI / 180);
            _light = Vector3.Normalize(_right * (MathF.Sin(ly) * MathF.Cos(lp)) + _up * MathF.Sin(lp)
                + _back * ((s.LightBack ? -1 : 1) * MathF.Cos(ly) * MathF.Cos(lp)));
            _center = fit.Center;
            float radius = fit.Radius;
            _invRadius = 1 / radius;
            int shortOut = Math.Min(s.Width, s.Height);
            int shortSide = shortOut * _ss;
            // 바깥 외곽선 자리: 둘레 구를 짧은 변 − 2 × 선 두께에 맞춘다(가운데·옮기기는 그대로 전체 짧은 변 기준).
            _scale = Math.Max(1, shortOut - 2 * s.OutlineMargin) * _ss * 0.5;
            _cx = _bw * 0.5 + s.PanX * shortSide;
            _cy = _bh * 0.5 + s.PanY * shortSide;
            _half = radius / s.Zoom;
            float halfFov = s.FieldOfView * 0.5f * (MathF.PI / 180);
            // 둘레 구가 시야각에 꼭 맞는 거리(구에 닿는 접선 = 시야 가장자리). 확대는 시야를 좁힌다.
            float distance = radius / MathF.Sin(halfFov);
            _eye = _center + _back * distance;
            _tan = MathF.Tan(halfFov) / s.Zoom;
            _near = distance * 0.01f;
            _ambient = s.Ambient;
            _strength = s.LightStrength;
            _steps = s.ShadeSteps;
            // 하늘 비침 밝기: 빛 설정을 따라가 어두운 장면에서 유리만 떠 보이지 않게.
            _envScale = _ambient + _strength * 0.5f;
            PixelScale = _persp ? (float)(_tan * _ss / _scale) : (float)(_half * _ss / _scale) * _invRadius;
        }

        public void Run(PosedMesh mesh, ModelRenderLayers output, CancellationToken ct)
        {
            var opaque = new List<PreparedPrimitive>(mesh.Primitives.Count);
            var blended = new List<PreparedPrimitive>();
            var all = new List<PreparedPrimitive>(mesh.Primitives.Count);
            foreach (var p in mesh.Primitives)
            {
                if (p.Indices.Length < 3 || p.Positions.Length == 0) continue;
                var prep = Prepare(p);
                all.Add(prep);
                (prep.Blend ? blended : opaque).Add(prep);
            }
            AssignObjects(all);
            var blendTris = BlendTriangles(blended);
            bool any = opaque.Count > 0 || blendTris.Length > 0;
            int rowsPerBand = Math.Clamp(BandSamples / (_bw * _ss), 1, _s.Height);
            int bandLength = rowsPerBand * _ss * _bw;
            if (any)
            {
                _depth = new float[bandLength];
                _color = new ulong[bandLength];
                _normal = new uint[bandLength];
                _albedo = new uint[bandLength];
                _object = new int[bandLength];
            }
            if (blendTris.Length > 0)
            {
                _blendDepth = new float[bandLength];
                _peelZ = new float[bandLength];
                _pickZ = new float[bandLength];
                _peelId = new int[bandLength];
                _pickId = new int[bandLength];
            }
            int counter = 0;
            for (int oy0 = 0; oy0 < _s.Height; oy0 += rowsPerBand)
            {
                int oy1 = Math.Min(_s.Height, oy0 + rowsPerBand);
                if (any)
                {
                    _bandY0 = oy0 * _ss;
                    _bandY1 = oy1 * _ss;
                    int used = (_bandY1 - _bandY0) * _bw;
                    _depth.AsSpan(0, used).Fill(float.PositiveInfinity);
                    _color.AsSpan(0, used).Clear();
                    _pass = PeelPass.Opaque;
                    foreach (var p in opaque) DrawPrimitive(p, ref counter, ct);
                    if (blendTris.Length > 0) DrawBlended(blendTris, used, ref counter, ct);
                }
                Resolve(output, oy0, oy1, any, ct);
            }
        }

        /// <summary>
        /// 섞을 겹을 표본마다 먼 것부터 하나씩 벗겨 섞는다(깊이 벗기기): 한 차례에 삼각형을 두 번 훑어 — 먼저 표본마다 이미 섞은 겹보다 가까운 것 중
        /// 가장 먼 겹을 고르고, 다음에 고른 겹만 칠한다. 고를 겹이 없거나 <see cref="MaxBlendLayers"/>번이면 멈춘다(가장 가까운 겹부터 남음 — 그 너머는 버림).
        /// 삼각형 순서와 상관없이 표본마다 정확한 순서라 큰 삼각형·맞물린 유리에도 줄무늬가 생기지 않는다.
        /// </summary>
        private void DrawBlended((PreparedPrimitive P, int T)[] tris, int used, ref int counter, CancellationToken ct)
        {
            _blendDepth.AsSpan(0, used).Fill(float.PositiveInfinity);
            _peelZ.AsSpan(0, used).Fill(float.PositiveInfinity);
            _peelId.AsSpan(0, used).Fill(int.MaxValue);
            for (int layer = 0; layer < MaxBlendLayers; layer++)
            {
                _pickZ.AsSpan(0, used).Fill(float.NegativeInfinity);
                _pickId.AsSpan(0, used).Fill(-1);
                _picked = false;
                foreach (var pass in (ReadOnlySpan<PeelPass>)[PeelPass.Pick, PeelPass.Paint])
                {
                    _pass = pass;
                    for (int k = 0; k < tris.Length; k++)
                    {
                        if (++counter >= CancelBatch)
                        {
                            counter = 0;
                            ct.ThrowIfCancellationRequested();
                        }
                        _triId = k;
                        DrawTriangle(tris[k].P, tris[k].T);
                    }
                    if (!_picked) return;
                }
                for (int i = 0; i < used; i++)
                {
                    if (_pickId[i] < 0) continue;
                    _peelZ[i] = _pickZ[i];
                    _peelId[i] = _pickId[i];
                }
            }
        }

        /// <summary>섞어 그릴 삼각형(잘못된 꼭짓점이 있는 것은 뺌) — 목록 안 번호가 깊이 벗기기의 삼각형 번호.</summary>
        private static (PreparedPrimitive P, int T)[] BlendTriangles(List<PreparedPrimitive> prims)
        {
            var items = new List<(PreparedPrimitive P, int T)>();
            foreach (var p in prims)
            {
                var idx = p.Source.Indices;
                int n = idx.Length - idx.Length % 3, count = p.Source.Positions.Length;
                for (int t = 0; t < n; t += 3)
                {
                    int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                    if ((uint)i0 >= (uint)count || (uint)i1 >= (uint)count || (uint)i2 >= (uint)count) continue;
                    if (((p.State[i0] | p.State[i1] | p.State[i2]) & 2) != 0) continue;
                    items.Add((p, t));
                }
            }
            return items.ToArray();
        }

        /// <summary>
        /// 오브젝트 번호를 정한다(<see cref="ModelRenderSettings.OutlineObjects"/>): 노드마다(노드를 모르면 묶음마다 따로) 또는 같은 자리 꼭짓점으로 이어진
        /// 삼각형 덩어리마다(덩어리에서 가장 앞 꼭짓점 번호). 꺼져 있으면 모두 0.
        /// </summary>
        private void AssignObjects(List<PreparedPrimitive> prims)
        {
            if (_s.Objects == ModelOutlineObjects.Nodes)
            {
                for (int k = 0; k < prims.Count; k++) prims[k].ObjectId = prims[k].Source.Node >= 0 ? prims[k].Source.Node : -1 - k;
                return;
            }
            if (_s.Objects != ModelOutlineObjects.Connected) return;
            var offsets = new int[prims.Count];
            int total = 0;
            for (int k = 0; k < prims.Count; k++)
            {
                offsets[k] = total;
                total += prims[k].Source.Positions.Length;
            }
            var parent = new int[total];
            for (int i = 0; i < total; i++) parent[i] = i;
            // 같은 자리 꼭짓점: 맞춤 반지름의 10만분의 1 격자에 맞춰 하나로 본다(묶음·노드가 달라도).
            float step = 1e-5f / _invRadius;
            var seen = new Dictionary<(long, long, long), int>(total);
            for (int k = 0; k < prims.Count; k++)
            {
                var pos = prims[k].Source.Positions;
                for (int i = 0; i < pos.Length; i++)
                {
                    var v = pos[i];
                    if (!IsFinite(v)) continue;
                    var key = ((long)MathF.Round(v.X / step), (long)MathF.Round(v.Y / step), (long)MathF.Round(v.Z / step));
                    if (seen.TryGetValue(key, out int first)) Union(parent, first, offsets[k] + i);
                    else seen[key] = offsets[k] + i;
                }
            }
            for (int k = 0; k < prims.Count; k++)
            {
                var idx = prims[k].Source.Indices;
                int count = prims[k].Source.Positions.Length, off = offsets[k];
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
                    if ((uint)i0 >= (uint)count || (uint)i1 >= (uint)count || (uint)i2 >= (uint)count) continue;
                    Union(parent, off + i0, off + i1);
                    Union(parent, off + i0, off + i2);
                }
            }
            for (int k = 0; k < prims.Count; k++)
            {
                var idx = prims[k].Source.Indices;
                int count = prims[k].Source.Positions.Length, off = offsets[k];
                var tri = prims[k].TriObject = new int[idx.Length / 3];
                for (int t = 0; t + 2 < idx.Length; t += 3)
                    tri[t / 3] = (uint)idx[t] < (uint)count ? Find(parent, off + idx[t]) : 0;
            }

            static int Find(int[] parent, int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            static void Union(int[] parent, int a, int b)
            {
                a = Find(parent, a);
                b = Find(parent, b);
                if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
            }
        }

        private enum PeelPass
        {
            /// <summary>불투명: 깊이 버퍼로 가리고 덮는다.</summary>
            Opaque,
            /// <summary>섞을 겹 고르기: 칠하지 않고 표본마다 다음에 벗길 겹만 정한다.</summary>
            Pick,
            /// <summary>고른 겹 칠하기: 표본의 고른 삼각형만 섞는다.</summary>
            Paint,
        }

        private PreparedPrimitive Prepare(PosedPrimitive p)
        {
            int n = p.Positions.Length;
            var m = p.Material ?? ModelMaterial.Default;
            var tex = _s.Textures ? m.Texture : null;
            var prep = new PreparedPrimitive
            {
                Source = p,
                X = new double[n],
                Y = new double[n],
                Z = new float[n],
                Iw = new float[n],
                View = _persp ? new Vector3[n] : null,
                State = new byte[n],
                HasNormals = p.Normals is { } nr && nr.Length >= n,
                HasColors = p.Colors is { } cl && cl.Length >= n,
                HasUvs = tex != null && p.Uvs is { } uv && uv.Length >= n,
                Base = m.BaseColor,
                Texture = tex?.Pixels,
                TexWidth = tex?.Width ?? 0,
                TexHeight = tex?.Height ?? 0,
                Repeat = m.Repeat,
                AlphaTest = m.AlphaMode == ModelAlphaMode.Mask,
                Cutoff = m.AlphaCutoff,
                Emissive = m.Emissive,
                Unlit = m.Unlit,
                Blend = m.AlphaMode == ModelAlphaMode.Blend || m.Transmission > 0,
                Transmission = Math.Clamp(Finite(m.Transmission, 0), 0, 1),
            };
            if (m.AlphaMode == ModelAlphaMode.Opaque) prep.Base.W = 1;
            if (prep.Blend)
            {
                float ior = Finite(m.Ior, 1.5f), f = (ior - 1) / (ior + 1);
                float rough = Math.Clamp(Finite(m.Roughness, 1), 0, 1), r = MathF.Max(rough, MinRoughness);
                prep.F0 = Math.Clamp(f * f, 0, 1);
                prep.Gloss = 1 - rough;
                prep.Shininess = 2 / (r * r);
            }
            var pos = p.Positions;
            for (int i = 0; i < n; i++)
            {
                var v = pos[i];
                if (!IsFinite(v))
                {
                    prep.State[i] = 2;
                    continue;
                }
                if (_persp)
                {
                    var view = ToView(v);
                    prep.View![i] = view;
                    if (view.Z < _near)
                    {
                        prep.State[i] = 1;
                        continue;
                    }
                    Project(view, out prep.X[i], out prep.Y[i], out prep.Z[i], out prep.Iw[i]);
                }
                else
                {
                    var d = v - _center;
                    prep.X[i] = SnapCoord(_cx + Vector3.Dot(d, _right) / _half * _scale);
                    prep.Y[i] = SnapCoord(_cy - Vector3.Dot(d, _up) / _half * _scale);
                    prep.Z[i] = -Vector3.Dot(d, _back);
                    prep.Iw[i] = 1;
                }
                if (!double.IsFinite(prep.X[i]) || !double.IsFinite(prep.Y[i])) prep.State[i] = 2;
            }
            return prep;
        }

        private static double SnapCoord(double v) => Math.Round(v * Snap) / Snap;

        /// <summary>월드 → 카메라 좌표(원근: x 오른쪽, y 위, z = 앞으로의 거리 w).</summary>
        private Vector3 ToView(Vector3 v)
        {
            var d = v - _eye;
            return new Vector3(Vector3.Dot(d, _right), Vector3.Dot(d, _up), -Vector3.Dot(d, _back));
        }

        private void Project(Vector3 view, out double x, out double y, out float z, out float iw)
        {
            iw = 1 / view.Z;
            double k = _scale / (view.Z * (double)_tan);
            x = SnapCoord(_cx + view.X * k);
            y = SnapCoord(_cy - view.Y * k);
            z = -iw;
        }

        private void DrawPrimitive(PreparedPrimitive p, ref int counter, CancellationToken ct)
        {
            int n = p.Source.Indices.Length - p.Source.Indices.Length % 3;
            for (int t = 0; t < n; t += 3)
            {
                if (++counter >= CancelBatch)
                {
                    counter = 0;
                    ct.ThrowIfCancellationRequested();
                }
                DrawTriangle(p, t);
            }
        }

        /// <summary>삼각형 하나(번호 목록의 <paramref name="t"/>번째부터 셋)를 현재 띠에 그린다.</summary>
        private void DrawTriangle(PreparedPrimitive p, int t)
        {
            var src = p.Source;
            var idx = src.Indices;
            int count = src.Positions.Length;
            var state = p.State;
            var ys = p.Y;
            int i0 = idx[t], i1 = idx[t + 1], i2 = idx[t + 2];
            _curObject = p.TriObject is { } tri ? tri[t / 3] : p.ObjectId;
            if ((uint)i0 >= (uint)count || (uint)i1 >= (uint)count || (uint)i2 >= (uint)count) return;
            int st = state[i0] | state[i1] | state[i2];
            if ((st & 2) != 0) return;
            if (st == 0)
            {
                // 띠 밖이면 바로 건너뛴다.
                double minY = Math.Min(ys[i0], Math.Min(ys[i1], ys[i2]));
                double maxY = Math.Max(ys[i0], Math.Max(ys[i1], ys[i2]));
                if (maxY - 0.5 < _bandY0 || minY - 0.5 >= _bandY1) return;
                var fn = p.HasNormals ? default : FaceNormal(src.Positions, i0, i1, i2);
                var a = Vertex(p, i0, fn);
                var b = Vertex(p, i1, fn);
                var c = Vertex(p, i2, fn);
                Triangle(a, b, c, p);
            }
            else
            {
                ClipNear(p, i0, i1, i2);
            }
        }

        private static Vector3 FaceNormal(Vector3[] pos, int i0, int i1, int i2)
        {
            var n = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
            float len = n.Length();
            return len > 0 && float.IsFinite(len) ? n / len : Vector3.UnitZ;
        }

        private static RasterVertex Vertex(PreparedPrimitive p, int i, Vector3 faceNormal)
        {
            var src = p.Source;
            return new RasterVertex
            {
                X = p.X[i],
                Y = p.Y[i],
                Z = p.Z[i],
                Iw = p.Iw[i],
                N = p.HasNormals ? src.Normals[i] : faceNormal,
                Uv = p.HasUvs ? src.Uvs![i] : default,
                C = p.HasColors ? src.Colors![i] : Vector4.One,
            };
        }

        /// <summary>카메라 뒤로 걸친 삼각형을 가까운 면(w = near)에서 잘라 그린다(Sutherland–Hodgman, 면 하나).</summary>
        private void ClipNear(PreparedPrimitive p, int i0, int i1, int i2)
        {
            var src = p.Source;
            var view = p.View!;
            var fn = p.HasNormals ? default : FaceNormal(src.Positions, i0, i1, i2);
            _clipIn[0] = Clip(p, i0, view, fn);
            _clipIn[1] = Clip(p, i1, view, fn);
            _clipIn[2] = Clip(p, i2, view, fn);
            int count = 0;
            for (int k = 0; k < 3; k++)
            {
                ref readonly var a = ref _clipIn[k];
                ref readonly var b = ref _clipIn[(k + 1) % 3];
                float da = a.View.Z - _near, db = b.View.Z - _near;
                if (da >= 0) _clipOut[count++] = a;
                if ((da >= 0) != (db >= 0)) _clipOut[count++] = ClipVertex.Lerp(a, b, da / (da - db));
            }
            if (count < 3) return;
            var r0 = ToRaster(_clipOut[0]);
            var r1 = ToRaster(_clipOut[1]);
            for (int k = 2; k < count; k++)
            {
                var r2 = ToRaster(_clipOut[k]);
                Triangle(r0, r1, r2, p);
                r1 = r2;
            }
        }

        private static ClipVertex Clip(PreparedPrimitive p, int i, Vector3[] view, Vector3 faceNormal)
        {
            var src = p.Source;
            return new ClipVertex
            {
                View = view[i],
                N = p.HasNormals ? src.Normals[i] : faceNormal,
                Uv = p.HasUvs ? src.Uvs![i] : default,
                C = p.HasColors ? src.Colors![i] : Vector4.One,
            };
        }

        private RasterVertex ToRaster(in ClipVertex v)
        {
            // 잘린 꼭짓점은 w가 near보다 아주 조금 작을 수 있다(반올림) — near로 올려 0으로 나누지 않게 한다.
            var view = v.View with { Z = MathF.Max(v.View.Z, _near) };
            Project(view, out double x, out double y, out float z, out float iw);
            return new RasterVertex { X = x, Y = y, Z = z, Iw = iw, N = v.N, Uv = v.Uv, C = v.C };
        }

        private static double Orient(double ax, double ay, double bx, double by, double px, double py)
            => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

        /// <summary>변 a→b가 위·왼쪽 변이면 0(경계 위 표본 포함), 아니면 −가장 작은 양수(경계 위 표본 제외). 화면 y는 아래로.</summary>
        private static double TopLeftBias(double dx, double dy) => dy < 0 || (dy == 0 && dx > 0) ? 0 : -EdgeQuantum;

        private void Triangle(in RasterVertex v0, in RasterVertex v1, in RasterVertex v2, PreparedPrimitive p)
        {
            ref readonly var a = ref v0;
            ref readonly var b = ref v1;
            ref readonly var c = ref v2;
            double area = Orient(a.X, a.Y, b.X, b.Y, c.X, c.Y);
            if (!(area != 0) || !double.IsFinite(area)) return;
            if (area < 0)
            {
                b = ref v2;
                c = ref v1;
                area = -area;
            }
            double minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
            double minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)), maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
            // 표본 중심은 (i + 0.5) — 상자 안 표본 범위를 버퍼·띠에 맞춘다(아주 큰 좌표도 정수 변환 전에 자름).
            double fx0 = Math.Ceiling(Math.Max(minX - 0.5, 0)), fx1 = Math.Floor(Math.Min(maxX - 0.5, _bw - 1));
            double fy0 = Math.Ceiling(Math.Max(minY - 0.5, _bandY0)), fy1 = Math.Floor(Math.Min(maxY - 0.5, _bandY1 - 1));
            if (fx1 < fx0 || fy1 < fy0) return;
            int x0 = (int)fx0, x1 = (int)fx1, y0 = (int)fy0, y1 = (int)fy1;

            // 변 함수: e0 = b→c(a의 무게), e1 = c→a(b의 무게), e2 = a→b(c의 무게). 표본이 안쪽이면 셋 다 ≥ 0(치우침 포함).
            double px = x0 + 0.5, py = y0 + 0.5;
            double e0Row = Orient(b.X, b.Y, c.X, c.Y, px, py);
            double e1Row = Orient(c.X, c.Y, a.X, a.Y, px, py);
            double e2Row = Orient(a.X, a.Y, b.X, b.Y, px, py);
            double sx0 = -(c.Y - b.Y), sy0 = c.X - b.X;
            double sx1 = -(a.Y - c.Y), sy1 = a.X - c.X;
            double sx2 = -(b.Y - a.Y), sy2 = b.X - a.X;
            double bias0 = TopLeftBias(sy0, -sx0), bias1 = TopLeftBias(sy1, -sx1), bias2 = TopLeftBias(sy2, -sx2);
            double invArea = 1 / area;

            float za = a.Z, zb = b.Z, zc = c.Z;
            float wa = a.Iw, wb = b.Iw, wc = c.Iw;
            var depth = _depth;
            var blendDepth = _blendDepth;
            bool blend = p.Blend;
            var color = _color;
            var normals = _normal;
            var albedos = _albedo;
            var objects = _object;
            int obj = _curObject;
            var peelZ = _peelZ;
            var pickZ = _pickZ;
            var peelId = _peelId;
            var pickId = _pickId;
            // 섞을 삼각형은 벗기기 차례(고르기·칠하기)대로, 불투명은 늘 덮기.
            var pass = blend ? _pass : PeelPass.Opaque;
            int id = _triId;
            int bw = _bw;
            for (int y = y0; y <= y1; y++)
            {
                double e0 = e0Row, e1 = e1Row, e2 = e2Row;
                int row = (y - _bandY0) * bw;
                for (int x = x0; x <= x1; x++)
                {
                    if (e0 + bias0 >= 0 && e1 + bias1 >= 0 && e2 + bias2 >= 0)
                    {
                        float l0 = (float)(e0 * invArea), l1 = (float)(e1 * invArea), l2 = (float)(e2 * invArea);
                        float z = l0 * za + l1 * zb + l2 * zc;
                        int i = row + x;
                        if (z < depth[i])
                        {
                            if (pass == PeelPass.Pick)
                            {
                                // 이미 섞은 겹보다 가깝고, 지금까지 고른 것보다 먼 겹(같은 깊이는 번호가 큰 쪽이 멂).
                                if ((z < peelZ[i] || (z == peelZ[i] && id < peelId[i])) && (z > pickZ[i] || (z == pickZ[i] && id > pickId[i])))
                                {
                                    pickZ[i] = z;
                                    pickId[i] = id;
                                    _picked = true;
                                }
                            }
                            else if (pass == PeelPass.Opaque || pickId[i] == id)
                            {
                                // 원근 보정 무게: 화면에서 선형인 l × (1/w)를 다시 합 1로.
                                float q0 = l0 * wa, q1 = l1 * wb, q2 = l2 * wc;
                                float inv = 1 / (q0 + q1 + q2);
                                q0 *= inv;
                                q1 *= inv;
                                q2 *= inv;
                                if (Shade(p, a, b, c, q0, q1, q2, x + 0.5f, y + 0.5f, color[i], out ulong packed, out uint normal, out uint albedo))
                                {
                                    color[i] = packed;
                                    if (!blend)
                                    {
                                        depth[i] = z;
                                        normals[i] = normal;
                                        albedos[i] = albedo;
                                        objects[i] = obj;
                                    }
                                    else if (z < blendDepth[i])
                                    {
                                        blendDepth[i] = z;
                                        normals[i] = normal;
                                        albedos[i] = albedo;
                                        objects[i] = obj;
                                    }
                                }
                            }
                        }
                    }
                    e0 += sx0;
                    e1 += sx1;
                    e2 += sx2;
                }
                e0Row += sy0;
                e1Row += sy1;
                e2Row += sy2;
            }
        }

        /// <summary>
        /// 표본 하나의 색(알파를 미리 곱한 선형 16비트 RGB + 16비트 알파)과 법선(카메라 쪽으로 뒤집은 단위 벡터, 묶음 — 빛 없는 재질도 외곽선용으로 채움),
        /// 빛을 받기 전 바탕 색(sRGB 0xRRGGBB).
        /// 불투명은 그 색으로 덮고, 반투명·유리(<see cref="PreparedPrimitive.Blend"/>)는 그 표본에 이미 있던 <paramref name="under"/> 위에 섞는다.
        /// 알파 문턱에 걸리거나 섞어도 아무것도 남지 않으면 false(깊이도 쓰지 않음).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Shade(PreparedPrimitive p, in RasterVertex a, in RasterVertex b, in RasterVertex c,
            float q0, float q1, float q2, float sx, float sy, ulong under, out ulong packed, out uint normal, out uint albedoKey)
        {
            var baseColor = p.Base;
            if (p.HasColors) baseColor *= a.C * q0 + b.C * q1 + c.C * q2;
            if (p.HasUvs) baseColor *= SampleTexture(p, a.Uv * q0 + b.Uv * q1 + c.Uv * q2);
            if (p.AlphaTest && !(baseColor.W >= p.Cutoff))
            {
                packed = 0;
                normal = 0;
                albedoKey = 0;
                return false;
            }
            var albedo = new Vector3(baseColor.X, baseColor.Y, baseColor.Z);
            var n = a.N * q0 + b.N * q1 + c.N * q2;
            float len = n.Length();
            n = len > 1e-12f && float.IsFinite(len) ? n / len : _back;
            // 카메라 쪽을 향하는 방향 — 뒷면이 보이면 법선을 뒤집는다.
            Vector3 toEye = _back;
            if (_persp)
            {
                float xn = (float)((sx - _cx) / _scale) * _tan, yn = (float)((_cy - sy) / _scale) * _tan;
                toEye = _back - _right * xn - _up * yn;
            }
            if (Vector3.Dot(n, toEye) < 0) n = -n;
            var rgb = albedo;
            if (!p.Unlit)
            {
                float diffuse = MathF.Max(0, Vector3.Dot(n, _light));
                if (_steps >= 2) diffuse = Math.Min(_steps - 1, (int)(diffuse * _steps)) / (float)(_steps - 1);
                rgb *= _ambient + _strength * diffuse;
            }
            rgb += p.Emissive;
            normal = PackNormal(n);
            var a8 = Srgb8(albedo);
            albedoKey = (uint)a8.R << 16 | (uint)a8.G << 8 | a8.B;
            if (!p.Blend)
            {
                packed = To16(rgb.X) | ((ulong)To16(rgb.Y) << 16) | ((ulong)To16(rgb.Z) << 32) | OpaqueAlpha;
                return true;
            }
            return Blend(p, rgb, albedo, Math.Clamp(baseColor.W, 0, 1), n, toEye, under, out packed);
        }

        /// <summary>
        /// 반투명·유리 한 겹을 표본 <paramref name="under"/>(미리 곱한 색 + 알파) 위에 섞는다. 보는 쪽 반사율 F(슐릭 프레넬 × 비침 세기) 만큼 하늘을 비추고
        /// 빛 쪽 반짝임(블린–퐁)을 더하며, 나머지 중 <see cref="PreparedPrimitive.Transmission"/>만큼은 바탕 색으로 물들어 지나가고 그 밖은 빛 받은 색으로 덮는다.
        /// 재질 알파는 이 한 겹이 덮는 몫이다(나머지는 그대로 지나감). 결과 알파 = 이 겹이 막은 빛의 평균 + 아래 알파 중 남은 몫.
        /// </summary>
        private bool Blend(PreparedPrimitive p, Vector3 lit, Vector3 albedo, float alpha, Vector3 n, Vector3 toEye, ulong under, out ulong packed)
        {
            float t = p.Transmission, fresnel = 0;
            var reflect = Vector3.Zero;
            if (p.Gloss > 0)
            {
                var v = Vector3.Normalize(toEye);
                float nv = Math.Clamp(Vector3.Dot(n, v), 0, 1);
                fresnel = Schlick(p.F0, nv);
                reflect = Sky(2 * nv * n - v) * (_envScale * fresnel);
                float nl = Vector3.Dot(n, _light);
                if (!p.Unlit && nl > 0)
                {
                    var h = Vector3.Normalize(_light + v);
                    float nh = MathF.Max(0, Vector3.Dot(n, h));
                    // 정규화한 블린–퐁: 날카로울수록 작고 밝다.
                    reflect += new Vector3(_strength * Schlick(p.F0, MathF.Max(0, Vector3.Dot(h, v))) * MathF.Pow(nh, p.Shininess) * (p.Shininess + 2) / 8);
                }
                reflect *= p.Gloss;
                fresnel *= p.Gloss;
            }
            float keep = 1 - fresnel;
            var add = alpha * (keep * (1 - t) * lit + reflect);
            var pass = alpha * keep * t * albedo + new Vector3(1 - alpha);
            const float Inv = 1 / 65535f;
            var below = new Vector3(under & 0xFFFF, (under >> 16) & 0xFFFF, (under >> 32) & 0xFFFF) * Inv;
            float belowAlpha = (under >> AlphaShift) * Inv;
            var outColor = add + pass * below;
            float cover = Math.Clamp(1 - (pass.X + pass.Y + pass.Z) / 3, 0, 1);
            float outAlpha = cover + (1 - cover) * belowAlpha;
            if (!(outAlpha > 0))
            {
                packed = 0;
                return false;
            }
            packed = To16(outColor.X) | ((ulong)To16(outColor.Y) << 16) | ((ulong)To16(outColor.Z) << 32) | ((ulong)Math.Max(1u, To16(outAlpha)) << AlphaShift);
            return true;
        }

        private static float Schlick(float f0, float cos)
        {
            float k = 1 - cos, k2 = k * k;
            return f0 + (1 - f0) * (k2 * k2 * k);
        }

        /// <summary>월드 방향 <paramref name="dir"/>의 둘레 하늘(위 = +Y): 지평선에서 꼭대기로, 아래는 땅으로 어두워진다.</summary>
        private static Vector3 Sky(Vector3 dir)
        {
            float y = dir.Y / MathF.Max(1e-6f, dir.Length());
            return y >= 0
                ? Vector3.Lerp(SkyHorizon, SkyZenith, MathF.Sqrt(MathF.Min(1, y)))
                : Vector3.Lerp(SkyHorizon * 0.55f, SkyGround, MathF.Sqrt(MathF.Min(1, -y)));
        }

        /// <summary>단위 벡터 → 8면체 사상 16:16비트(아래 반구는 접어 넣음).</summary>
        private static uint PackNormal(Vector3 n)
        {
            float s = MathF.Abs(n.X) + MathF.Abs(n.Y) + MathF.Abs(n.Z);
            float x = n.X / s, y = n.Y / s;
            if (n.Z < 0)
            {
                float fx = (1 - MathF.Abs(y)) * (x >= 0 ? 1 : -1);
                y = (1 - MathF.Abs(x)) * (y >= 0 ? 1 : -1);
                x = fx;
            }
            uint ux = (uint)Math.Clamp((int)MathF.Round((x * 0.5f + 0.5f) * 65535), 0, 65535);
            uint uy = (uint)Math.Clamp((int)MathF.Round((y * 0.5f + 0.5f) * 65535), 0, 65535);
            return ux | uy << 16;
        }

        private static Vector3 UnpackNormal(uint packed)
        {
            float x = (packed & 0xFFFF) * (2f / 65535) - 1, y = (packed >> 16) * (2f / 65535) - 1;
            float z = 1 - MathF.Abs(x) - MathF.Abs(y);
            if (z < 0)
            {
                float fx = (1 - MathF.Abs(y)) * (x >= 0 ? 1 : -1);
                y = (1 - MathF.Abs(x)) * (y >= 0 ? 1 : -1);
                x = fx;
            }
            return Vector3.Normalize(new Vector3(x, y, z));
        }

        private static uint To16(float v) => v >= 1 ? 65535u : v > 0 ? (uint)(v * 65535 + 0.5f) : 0u;

        /// <summary>가장 가까운 텍셀(sRGB → 선형). UV (0,0) = 그림 왼쪽 위.</summary>
        private static Vector4 SampleTexture(PreparedPrimitive p, Vector2 uv)
        {
            int w = p.TexWidth, h = p.TexHeight;
            float u = float.IsFinite(uv.X) ? uv.X : 0, v = float.IsFinite(uv.Y) ? uv.Y : 0;
            if (p.Repeat)
            {
                u -= MathF.Floor(u);
                v -= MathF.Floor(v);
            }
            else
            {
                u = Math.Clamp(u, 0, 1);
                v = Math.Clamp(v, 0, 1);
            }
            int tx = Math.Min(w - 1, (int)(u * w)), ty = Math.Min(h - 1, (int)(v * h));
            var t = p.Texture![ty * w + tx];
            return new Vector4(OkLab.ToLinear(t.R), OkLab.ToLinear(t.G), OkLab.ToLinear(t.B), t.A * (1 / 255f));
        }

        /// <summary>
        /// 띠의 표본을 출력 픽셀로 모은다. 표본 색은 알파를 미리 곱한 값이고 불투명 표본의 알파는 1이다:
        /// 알파 = 표본 알파의 평균, 색 = 표본 색의 합 ÷ 알파의 합(가장자리가 어두워지지 않게 — 모두 불투명이면 덮인 표본만의 평균).
        /// 바탕이 불투명하면 비어 있는 몫(1 − 알파)만큼 바탕 색을 넣어 선형광에서 섞는다.
        /// 덮인 픽셀(알파가 0이 아닌 표본이 있음)은 덮음(그런 표본의 비율 — 유리처럼 옅게 비친 곳도 모양으로 셈)·가장 가까운 표본의 깊이·대표 법선
        /// (덮인 표본 법선 중 그 평균에 가장 가까운 것 — 꺾인 곳에 걸친 픽셀도 두 면의 중간이 아니라 더 많이 덮은 면의 법선이라 각진 곳 선이 표본 수와
        /// 상관없이 같은 문턱에서 나온다)도 채운다.
        /// </summary>
        private void Resolve(ModelRenderLayers layers, int oy0, int oy1, bool drawn, CancellationToken ct)
        {
            var output = layers.Image.Pixels;
            var bg = _s.Background;
            int w = _s.Width;
            if (!drawn)
            {
                output.AsSpan(oy0 * w, (oy1 - oy0) * w).Fill(bg);
                return;
            }
            int ss = _ss, n = ss * ss, bw = _bw;
            bool bgOpaque = bg.A == 255;
            uint bgR = To16(OkLab.ToLinear(bg.R)), bgG = To16(OkLab.ToLinear(bg.G)), bgB = To16(OkLab.ToLinear(bg.B));
            var color = _color;
            var depth = _depth;
            var blendDepth = _blendDepth;
            bool hasBlend = blendDepth.Length > 0;
            var packedNormals = _normal;
            var packedAlbedo = _albedo;
            var outAlbedo = layers.Albedo;
            var packedObjects = _object;
            var outObject = layers.Object;
            var coverage = layers.Coverage;
            var outDepth = layers.Depth;
            var outNormal = layers.Normal;
            var srgb = ToSrgb;
            Span<Vector3> sampleNormals = stackalloc Vector3[64];
            Span<int> sampleIndex = stackalloc int[64];
            for (int oy = oy0; oy < oy1; oy++)
            {
                ct.ThrowIfCancellationRequested();
                int sampleRow = (oy - oy0) * ss * bw;
                int outRow = oy * w;
                for (int ox = 0; ox < w; ox++)
                {
                    int covered = 0;
                    uint r = 0, g = 0, b = 0, aSum = 0;
                    float front = float.PositiveInfinity;
                    var sum = Vector3.Zero;
                    int start = sampleRow + ox * ss;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        int s0 = start + sy * bw;
                        for (int sx = 0; sx < ss; sx++)
                        {
                            int k = s0 + sx;
                            ulong c = color[k];
                            uint sa = (uint)(c >> AlphaShift);
                            if (sa == 0) continue;
                            r += (uint)(c & 0xFFFF);
                            g += (uint)((c >> 16) & 0xFFFF);
                            b += (uint)((c >> 32) & 0xFFFF);
                            aSum += sa;
                            front = MathF.Min(front, hasBlend ? MathF.Min(depth[k], blendDepth[k]) : depth[k]);
                            var sn = UnpackNormal(packedNormals[k]);
                            sampleIndex[covered] = k;
                            sampleNormals[covered++] = sn;
                            sum += sn;
                        }
                    }
                    if (covered > 0)
                    {
                        int at = outRow + ox;
                        int bestAt = 0;
                        float bestDot = Vector3.Dot(sampleNormals[0], sum);
                        for (int k = 1; k < covered; k++)
                        {
                            float d = Vector3.Dot(sampleNormals[k], sum);
                            if (d > bestDot)
                            {
                                bestDot = d;
                                bestAt = k;
                            }
                        }
                        var best = sampleNormals[bestAt];
                        uint key = packedAlbedo[sampleIndex[bestAt]];
                        outAlbedo[at] = new Color32((byte)(key >> 16), (byte)(key >> 8), (byte)key);
                        outObject[at] = packedObjects[sampleIndex[bestAt]];
                        coverage[at] = (byte)((covered * 255 + n / 2) / n);
                        // 깊이 키 → 앞쪽 거리(정사영: 맞춤 가운데를 지나는 면에서, 원근: 눈에서) ÷ 맞춤 반지름.
                        outDepth[at] = (_persp ? -1 / front : front) * _invRadius;
                        outNormal[at] = new Vector3(Vector3.Dot(best, _right), Vector3.Dot(best, _up), Vector3.Dot(best, _back));
                    }
                    Color32 result;
                    if (covered == 0) result = bg;
                    else if (aSum != (uint)covered * 0xFFFF) result = Partial(r, g, b, aSum, n, bgOpaque, bgR, bgG, bgB, bg);
                    else if (bgOpaque)
                    {
                        uint rest = (uint)(n - covered), half = (uint)n / 2;
                        result = new Color32(
                            srgb[(r + rest * bgR + half) / (uint)n],
                            srgb[(g + rest * bgG + half) / (uint)n],
                            srgb[(b + rest * bgB + half) / (uint)n]);
                    }
                    else
                    {
                        uint cv = (uint)covered, half = cv / 2;
                        byte alpha = (byte)((covered * 255 + n / 2) / n);
                        result = new Color32(srgb[(r + half) / cv], srgb[(g + half) / cv], srgb[(b + half) / cv], alpha);
                        if (bg.A != 0) result = Color32.BlendOver(bg, result);
                    }
                    output[outRow + ox] = result;
                }
            }
        }

        /// <summary>반투명 표본이 섞인 픽셀: 미리 곱한 색 합 <paramref name="r"/>·<paramref name="g"/>·<paramref name="b"/>와 알파 합(16비트 단위) → 결과 색.</summary>
        private static Color32 Partial(uint r, uint g, uint b, uint aSum, int n, bool bgOpaque, uint bgR, uint bgG, uint bgB, Color32 bg)
        {
            var srgb = ToSrgb;
            ulong full = (ulong)n * 0xFFFF;
            if (bgOpaque)
            {
                // 채널 = (Σ 색 + Σ(1 − 알파) × 바탕) ÷ n, 16비트 단위로 한 번에 나눈다.
                ulong rest = full - aSum, h = full / 2;
                return new Color32(
                    srgb[Math.Min(0xFFFFUL, (r * 0xFFFFUL + rest * bgR + h) / full)],
                    srgb[Math.Min(0xFFFFUL, (g * 0xFFFFUL + rest * bgG + h) / full)],
                    srgb[Math.Min(0xFFFFUL, (b * 0xFFFFUL + rest * bgB + h) / full)]);
            }
            byte alpha = (byte)((aSum * 255UL + full / 2) / full);
            if (alpha == 0) return bg;
            ulong ah = aSum / 2;
            // 반짝임은 덮는 몫보다 밝을 수 있다(더하는 빛) — 알파를 나눈 색은 흰색에서 멈춘다.
            var result = new Color32(
                srgb[Math.Min(0xFFFFUL, (r * 0xFFFFUL + ah) / aSum)],
                srgb[Math.Min(0xFFFFUL, (g * 0xFFFFUL + ah) / aSum)],
                srgb[Math.Min(0xFFFFUL, (b * 0xFFFFUL + ah) / aSum)],
                alpha);
            return bg.A != 0 ? Color32.BlendOver(bg, result) : result;
        }
    }
}

/// <summary>
/// <see cref="ModelRenderer.RenderLayers"/>의 결과: 다듬기 전 그림과 결과 픽셀마다(가로줄 순서, 번호 = y × 가로 + x) 외곽선 찾기에 쓰는 값.
/// 덮이지 않은 픽셀은 덮음 0, 깊이 +∞, 법선 0, 바탕 색 투명.
/// </summary>
public sealed class ModelRenderLayers
{
    private readonly float _pixelScale;
    private readonly bool _perspective;

    internal ModelRenderLayers(PixelImage image, float pixelScale, bool perspective)
    {
        Image = image;
        int n = image.Width * image.Height;
        Coverage = new byte[n];
        Depth = new float[n];
        Array.Fill(Depth, float.PositiveInfinity);
        Normal = new Vector3[n];
        Albedo = new Color32[n];
        Object = new int[n];
        _pixelScale = pixelScale;
        _perspective = perspective;
    }

    /// <summary>그린 그림(바탕 섞기까지 마침, 반투명 없애기·색 줄이기·외곽선 전).</summary>
    public PixelImage Image { get; }
    public int Width => Image.Width;
    public int Height => Image.Height;
    /// <summary>
    /// 덮인 표본 비율(0~255, 바탕이 불투명해도 그대로). 128 이상이면 모델 픽셀. 불투명 재질만 덮었으면 바탕이 투명할 때의 알파와 같고,
    /// 유리·반투명이 덮은 표본도 덮인 것으로 세므로 거기서는 알파보다 클 수 있다(옅은 유리도 외곽선 모양에 들어감).
    /// </summary>
    public byte[] Coverage { get; }
    /// <summary>
    /// 가장 가까운 덮인 표본의 앞쪽 거리 ÷ 맞춤 범위의 둘레 구 반지름(클수록 멂 — 그림 크기·모델 크기와 상관없는 단위).
    /// 정사영은 맞춤 가운데를 지나는 화면과 나란한 면에서, 원근은 눈에서 잰다(차이만 뜻이 있다).
    /// </summary>
    public float[] Depth { get; }
    /// <summary>
    /// 카메라 좌표 단위 법선(X = 화면 오른쪽, Y = 화면 위, Z = 카메라 쪽) — 카메라 쪽으로 뒤집혀 있어 Z가 0 이상(원근 가장자리는 조금 음수일 수 있음).
    /// 덮인 표본 법선 중 그 평균에 가장 가까운 것(걸친 픽셀은 더 많이 덮은 면).
    /// </summary>
    public Vector3[] Normal { get; }
    /// <summary>대표 법선과 같은 표본의 바탕 색(빛을 받기 전 재질·꼭짓점·텍스처 색, sRGB 불투명) — 색 바뀌는 곳 외곽선이 본다.</summary>
    public Color32[] Albedo { get; }
    /// <summary>대표 법선과 같은 표본의 오브젝트 번호(<see cref="ModelRenderSettings.OutlineObjects"/>가 꺼져 있으면 모두 0) — 오브젝트 경계 외곽선이 본다.</summary>
    public int[] Object { get; }

    /// <summary>픽셀 <paramref name="index"/> 한 칸의 월드 크기를 <see cref="Depth"/>와 같은 단위로(원근은 깊이에 비례).</summary>
    public float PixelSize(int index) => _perspective ? _pixelScale * Depth[index] : _pixelScale;
}
