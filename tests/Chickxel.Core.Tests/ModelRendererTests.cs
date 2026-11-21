using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using Chickxel.Core.Model;
using Chickxel.Core.Scene3D;
using Xunit.Abstractions;

namespace Chickxel.Core.Tests;

public class ModelRendererTests(ITestOutputHelper output)
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Green = new(0, 255, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    private static ModelMaterial Unlit(Vector4 color, ModelAlphaMode mode = ModelAlphaMode.Opaque, float cutoff = 0.5f)
        => new() { BaseColor = color, Unlit = true, AlphaMode = mode, AlphaCutoff = cutoff };

    private static ModelMaterial Unlit(float r, float g, float b) => Unlit(new Vector4(r, g, b, 1));

    /// <summary>가운데 <paramref name="c"/>, 두 축 반 길이 벡터 <paramref name="u"/>·<paramref name="v"/>인 사각형(삼각형 둘).</summary>
    private static PosedPrimitive Quad(Vector3 c, Vector3 u, Vector3 v, ModelMaterial m)
    {
        var n = Vector3.Normalize(Vector3.Cross(u, v));
        return new PosedPrimitive([c - u - v, c + u - v, c + u + v, c - u + v], [n, n, n, n], null, null, [0, 1, 2, 0, 2, 3], m);
    }

    /// <summary>원점 중심 한 변 1인 정육면체(삼각형 12개): 앞(+Z) 빨강, 오른쪽(+X) 파랑, 왼쪽(−X) 초록, 나머지 회색 — 모두 빛 없이.</summary>
    private static PosedMesh Cube()
    {
        PosedPrimitive Face(Vector3 n, Vector3 u, ModelMaterial m) => Quad(n * 0.5f, u * 0.5f, Vector3.Cross(n, u) * 0.5f, m);
        var gray = Unlit(0.5f, 0.5f, 0.5f);
        return new PosedMesh([
            Face(Vector3.UnitZ, Vector3.UnitX, Unlit(1, 0, 0)),
            Face(Vector3.UnitX, -Vector3.UnitZ, Unlit(0, 0, 1)),
            Face(-Vector3.UnitX, Vector3.UnitZ, Unlit(0, 1, 0)),
            Face(-Vector3.UnitZ, -Vector3.UnitX, gray),
            Face(Vector3.UnitY, Vector3.UnitX, gray),
            Face(-Vector3.UnitY, Vector3.UnitX, gray),
        ]);
    }

    /// <summary>반지름 1 UV 구(위도 <paramref name="rings"/> × 경도 <paramref name="segments"/> 칸, 법선 = 위치).</summary>
    private static PosedMesh Sphere(int rings, int segments, ModelMaterial m)
    {
        var pos = new List<Vector3>();
        for (int i = 0; i <= rings; i++)
        {
            float lat = MathF.PI * i / rings;
            for (int j = 0; j <= segments; j++)
            {
                float lon = 2 * MathF.PI * j / segments;
                pos.Add(new Vector3(MathF.Sin(lat) * MathF.Cos(lon), MathF.Cos(lat), MathF.Sin(lat) * MathF.Sin(lon)));
            }
        }
        var idx = new List<int>();
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < segments; j++)
            {
                int a = i * (segments + 1) + j, b = a + segments + 1;
                idx.AddRange([a, b, a + 1, a + 1, b, b + 1]);
            }
        var p = pos.ToArray();
        return new PosedMesh([new PosedPrimitive(p, p.Select(Vector3.Normalize).ToArray(), null, null, idx.ToArray(), m)]);
    }

    private static ModelRenderSettings Front(int size = 32, int ss = 1) => new()
    {
        Width = size,
        Height = size,
        Yaw = 0,
        Pitch = 0,
        Projection = ModelProjection.Orthographic,
        Supersample = ss,
    };

    private static PixelImage Draw(PosedMesh mesh, ModelRenderSettings s) => ModelRenderer.Render(mesh, s, ModelRenderer.Bounds(mesh));

    [Fact]
    public void Bounds_UsesIndexedFinitePositionsOnly()
    {
        var prim = new PosedPrimitive(
            [new(0, 0, 0), new(1, 2, 3), new(float.NaN, 0, 0), new(100, 100, 100)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], null, null, [0, 1, 2], ModelMaterial.Default);
        var b = ModelRenderer.Bounds(new PosedMesh([prim]));
        Assert.Equal(Vector3.Zero, b.Min);
        Assert.Equal(new Vector3(1, 2, 3), b.Max);
        Assert.True(ModelRenderer.Bounds(new PosedMesh([])).IsEmpty);
    }

    [Fact]
    public void Cube_FrontOn_CoversExactCenteredSquare()
    {
        var img = Draw(Cube(), Front());
        // 둘레 구 반지름 √3/2가 짧은 변 절반(16px)에 맞으므로 앞면 반 변 0.5는 16·0.5/(√3/2) ≈ 9.2376px:
        // 모서리 16 ± 9.2376 = [6.762, 25.238] 안에 중심(i + 0.5)이 드는 픽셀은 7..24(18칸). 옆·위 면은 정면에서 넓이 0.
        Assert.Equal(new Rectangle(7, 7, 18, 18), img.GetOpaqueBounds());
        for (int y = 7; y < 25; y++)
            for (int x = 7; x < 25; x++)
                Assert.Equal(Red, img[x, y]);
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(90, 0, 0, 255)]
    [InlineData(-90, 0, 255, 0)]
    public void Cube_Yaw_ShowsExpectedFace(float yaw, byte r, byte g, byte b)
    {
        var img = Draw(Cube(), Front() with { Yaw = yaw });
        Assert.Equal(new Color32(r, g, b), img[16, 16]);
    }

    [Fact]
    public void Cube_PositivePitch_LooksDownOnTop()
    {
        var mesh = Cube();
        var top = mesh.Primitives[4] with { Material = Unlit(1, 1, 0) };
        var withTop = new PosedMesh([.. mesh.Primitives.Take(4), top, mesh.Primitives[5]]);
        var img = Draw(withTop, Front() with { Pitch = 60 });
        Assert.Equal(new Color32(255, 255, 0), img[16, 12]);
        Assert.Equal(Red, img[16, 22]);
    }

    [Theory]
    [InlineData(ModelProjection.Orthographic, false)]
    [InlineData(ModelProjection.Orthographic, true)]
    [InlineData(ModelProjection.Perspective, false)]
    [InlineData(ModelProjection.Perspective, true)]
    public void DepthTest_NearQuadOccludesFarQuad_RegardlessOfOrder(ModelProjection projection, bool nearFirst)
    {
        var far = Quad(new Vector3(0, 0, -1), Vector3.UnitX, Vector3.UnitY, Unlit(0, 1, 0));
        var near = Quad(new Vector3(0, 0, 0.5f), Vector3.UnitX * 0.25f, Vector3.UnitY * 0.25f, Unlit(1, 0, 0));
        var mesh = new PosedMesh(nearFirst ? [near, far] : [far, near]);
        var img = Draw(mesh, Front() with { Projection = projection });
        Assert.Equal(Red, img[16, 16]);
        Assert.Equal(Green, img[10, 16]);
        Assert.Equal(Green, img[16, 22]);
    }

    [Theory]
    [InlineData(ModelAlphaMode.Mask, 0.3f, 0.5f, false)]
    [InlineData(ModelAlphaMode.Mask, 0.7f, 0.5f, true)]
    [InlineData(ModelAlphaMode.Mask, 0.7f, 0.8f, false)]
    [InlineData(ModelAlphaMode.Blend, 0.4f, 0.9f, false)]
    [InlineData(ModelAlphaMode.Blend, 0.6f, 0.9f, true)]
    [InlineData(ModelAlphaMode.Opaque, 0.1f, 0.5f, true)]
    public void AlphaCutoff_DiscardsWithoutWritingDepth(ModelAlphaMode mode, float alpha, float cutoff, bool drawn)
    {
        // 앞 사각형을 먼저 넣는다: 버린 표본이 깊이를 쓰면 뒤 사각형이 가려져 투명이 된다.
        var front = Quad(Vector3.Zero, Vector3.UnitX * 0.5f, Vector3.UnitY * 0.5f, Unlit(new Vector4(1, 0, 0, alpha), mode, cutoff));
        var back = Quad(new Vector3(0, 0, -1), Vector3.UnitX, Vector3.UnitY, Unlit(0, 1, 0));
        var img = Draw(new PosedMesh([front, back]), Front());
        Assert.Equal(drawn ? Red : Green, img[16, 16]);
    }

    [Fact]
    public void ShadeSteps_QuantizesLightingIntoBands()
    {
        var mesh = Sphere(24, 32, new ModelMaterial { BaseColor = new Vector4(0.8f, 0.8f, 0.8f, 1) });
        var s = Front(64) with { LightYaw = 40, LightPitch = 30 };
        static int Distinct(PixelImage img)
        {
            var set = new HashSet<uint>();
            foreach (var p in img.Pixels)
                if (p.A != 0) set.Add(p.Argb);
            return set.Count;
        }
        Assert.Equal(2, Distinct(Draw(mesh, s with { ShadeSteps = 2 })));
        Assert.True(Distinct(Draw(mesh, s with { ShadeSteps = 4 })) <= 4);
        Assert.True(Distinct(Draw(mesh, s)) > 20);
    }

    [Fact]
    public void Light_FromRightBrightensRightSide()
    {
        var mesh = Sphere(24, 32, new ModelMaterial { BaseColor = new Vector4(0.8f, 0.8f, 0.8f, 1) });
        var img = Draw(mesh, Front(64) with { LightYaw = 60, LightPitch = 0 });
        Assert.True(img[44, 32].R > img[20, 32].R);
        var top = Draw(mesh, Front(64) with { LightYaw = 0, LightPitch = 60 });
        Assert.True(top[32, 20].R > top[32, 44].R);
    }

    [Fact]
    public void Supersample_ProducesPartialAlphaOnDiagonalEdge_WithoutDarkening()
    {
        var tri = new PosedPrimitive([new(-1, -1, 0), new(1, -1, 0), new(-1, 1, 0)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], null, null, [0, 1, 2], Unlit(1, 0, 0));
        var mesh = new PosedMesh([tri]);
        var hard = Draw(mesh, Front(ss: 1));
        Assert.All(hard.Pixels, p => Assert.True(p.A is 0 or 255));
        var soft = Draw(mesh, Front(ss: 4));
        Assert.Contains(soft.Pixels, p => p.A is > 0 and < 255);
        // 가장자리 색은 덮인 표본만의 평균 — 투명 쪽과 섞여 어두워지지 않는다.
        Assert.All(soft.Pixels, p => Assert.True(p.A == 0 || p.WithAlpha(255) == Red));
    }

    [Fact]
    public void OpaqueBackground_CompositesEdgesInsteadOfAlpha()
    {
        var tri = new PosedPrimitive([new(-1, -1, 0), new(1, -1, 0), new(-1, 1, 0)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], null, null, [0, 1, 2], Unlit(1, 0, 0));
        var img = Draw(new PosedMesh([tri]), Front(ss: 4) with { Background = Blue });
        Assert.All(img.Pixels, p => Assert.Equal(255, p.A));
        Assert.Equal(Blue, img[31, 0]);
        Assert.Equal(Red, img[8, 24]);
        Assert.Contains(img.Pixels, p => p.R > 0 && p.B > 0);
    }

    [Fact]
    public void Perspective_TriangleCrossingBehindCamera_IsClipped()
    {
        // 맞춤 범위는 원점의 작은 사각형 — 카메라는 +Z 쪽 약 2.35에 있고, 바닥(y = −0.5) 삼각형 한 꼭짓점은 z = 50(카메라 뒤).
        var fit = new ModelBounds(new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0));
        var floor = new PosedPrimitive([new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0, -0.5f, 50)],
            [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY], null, null, [0, 1, 2], Unlit(1, 0, 0));
        var s = Front() with { Projection = ModelProjection.Perspective };
        var img = ModelRenderer.Render(new PosedMesh([floor]), s, fit);
        // 눈높이보다 낮은 바닥은 모두 화면 가운데 줄 아래에 그려진다 — 자르지 않으면 뒤 꼭짓점이 뒤집혀 위쪽까지 칠한다.
        Assert.Contains(img.Pixels, p => p.A != 0);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 32; x++)
                Assert.Equal(0, img[x, y].A);
        Assert.Contains(img.Pixels, p => p.A == 0);

        var behind = new PosedPrimitive([new(-1, -1, 10), new(1, -1, 10), new(0, 1, 20)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], null, null, [0, 1, 2], Unlit(1, 0, 0));
        Assert.All(ModelRenderer.Render(new PosedMesh([behind]), s, fit).Pixels, p => Assert.Equal(0, p.A));
    }

    [Fact]
    public void EmptyMeshOrFit_FillsBackground()
    {
        var bg = new Color32(10, 20, 30);
        var s = Front() with { Background = bg };
        var empty = ModelRenderer.Render(new PosedMesh([]), s, ModelRenderer.Bounds(new PosedMesh([])));
        Assert.All(empty.Pixels, p => Assert.Equal(bg, p));
        var noFit = ModelRenderer.Render(Cube(), s, ModelBounds.Empty);
        Assert.All(noFit.Pixels, p => Assert.Equal(bg, p));
        Assert.All(ModelRenderer.Render(new PosedMesh([]), Front(), ModelBounds.Empty).Pixels, p => Assert.Equal(Color32.Transparent, p));
    }

    [Fact]
    public void CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => ModelRenderer.Render(Cube(), Front(), ModelRenderer.Bounds(Cube()), cts.Token));
    }

    [Fact]
    public void OutOfRangeSettings_AreClamped()
    {
        var img = Draw(Cube(), Front() with { Width = 0, Height = -5, Supersample = 100, Zoom = 0, FieldOfView = float.NaN, ShadeSteps = 99 });
        Assert.Equal(1, img.Width);
        Assert.Equal(1, img.Height);
    }

    [Fact]
    public void Texture_NearestSample_TopLeftIsUvOrigin()
    {
        // 2×2 텍스처: 왼쪽 위 빨강, 오른쪽 위 초록, 왼쪽 아래 파랑, 오른쪽 아래 흰색.
        var tex = new PixelImage(2, 2);
        tex[0, 0] = Red;
        tex[1, 0] = Green;
        tex[0, 1] = Blue;
        tex[1, 1] = Color32.White;
        var m = new ModelMaterial { BaseColor = Vector4.One, Texture = tex, Unlit = true };
        var quad = new PosedPrimitive([new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], [new(0, 1), new(1, 1), new(1, 0), new(0, 0)], null, [0, 1, 2, 0, 2, 3], m);
        var mesh = new PosedMesh([quad]);
        var img = Draw(mesh, Front());
        Assert.Equal(Red, img[10, 10]);
        Assert.Equal(Green, img[22, 10]);
        Assert.Equal(Blue, img[10, 22]);
        Assert.Equal(Color32.White, img[22, 22]);
        Assert.Equal(Color32.White, Draw(mesh, Front() with { Textures = false })[10, 10]);
    }

    [Fact]
    public void JitteredGrid_HasNoCracks()
    {
        // 이웃 삼각형이 변을 나누는 격자(안쪽 꼭짓점을 흔듦) — 위·왼쪽 규칙이 맞으면 안쪽에 빈 픽셀이 없다.
        const int n = 24;
        var rng = new Random(3);
        var pos = new Vector3[(n + 1) * (n + 1)];
        for (int y = 0; y <= n; y++)
            for (int x = 0; x <= n; x++)
            {
                bool inner = x > 0 && y > 0 && x < n && y < n;
                float jx = inner ? (float)(rng.NextDouble() - 0.5) * 0.06f : 0, jy = inner ? (float)(rng.NextDouble() - 0.5) * 0.06f : 0;
                pos[y * (n + 1) + x] = new Vector3(-1 + 2f * x / n + jx, -1 + 2f * y / n + jy, 0);
            }
        var idx = new List<int>();
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int a = y * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1;
                idx.AddRange((x + y) % 2 == 0 ? [a, b, d, a, d, c] : [a, b, c, b, d, c]);
            }
        var normals = Enumerable.Repeat(Vector3.UnitZ, pos.Length).ToArray();
        var mesh = new PosedMesh([new PosedPrimitive(pos, normals, null, null, idx.ToArray(), Unlit(1, 0, 0))]);
        var img = Draw(mesh, Front(64) with { Supersample = 3 });
        var r = img.GetOpaqueBounds();
        for (int y = r.Top + 1; y < r.Bottom - 1; y++)
            for (int x = r.Left + 1; x < r.Right - 1; x++)
                Assert.Equal(Red, img[x, y]);
    }

    [Fact]
    public void LargeSphere_RendersFilledDisc()
    {
        var mesh = Sphere(100, 100, Unlit(1, 0, 0));
        Assert.Equal(20000, mesh.TriangleCount);
        var s = Front(256, 4);
        ModelRenderer.Render(mesh, s with { Width = 16, Height = 16 }, ModelRenderer.Bounds(mesh));
        var sw = Stopwatch.StartNew();
        var img = Draw(mesh, s);
        sw.Stop();
        output.WriteLine($"20k triangles, 256x256, ss=4: {sw.ElapsedMilliseconds} ms");
        // 상자 [−1, 1]³의 둘레 구 반지름 √3이 128px에 맞으므로 구의 윤곽 반지름은 128/√3 ≈ 73.9px.
        double radius = 128 / Math.Sqrt(3);
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
            {
                double d = Math.Sqrt((x + 0.5 - 128) * (x + 0.5 - 128) + (y + 0.5 - 128) * (y + 0.5 - 128));
                if (d < radius - 2) Assert.Equal(Red, img[x, y]);
                else if (d > radius + 1) Assert.Equal(0, img[x, y].A);
            }
    }
}
