using System.Drawing;
using System.Numerics;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;
using Chickxel.Core.Scene3D;
using Xunit;

namespace Chickxel.Core.Tests;

public class ModelPixelPassTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Green = new(0, 255, 0);
    private static readonly Color32 Line = new ModelRenderSettings().OutlineColor;

    private static ModelMaterial Unlit(float r, float g, float b) => new() { BaseColor = new Vector4(r, g, b, 1), Unlit = true };

    /// <summary>가운데 <paramref name="c"/>, 두 축 반 길이 벡터 <paramref name="u"/>·<paramref name="v"/>인 사각형(삼각형 둘).</summary>
    private static PosedPrimitive Quad(Vector3 c, Vector3 u, Vector3 v, ModelMaterial m)
    {
        var n = Vector3.Normalize(Vector3.Cross(u, v));
        return new PosedPrimitive([c - u - v, c + u - v, c + u + v, c - u + v], [n, n, n, n], null, null, [0, 1, 2, 0, 2, 3], m);
    }

    /// <summary>원점 중심 한 변 1인 정육면체: 앞(+Z) 빨강, 오른쪽(+X) 파랑, 왼쪽(−X) 초록, 나머지 회색 — 모두 빛 없이.</summary>
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

    /// <summary>반지름 1 UV 구(법선 = 위치, 빛 받는 재질).</summary>
    private static PosedMesh Sphere(int rings = 24, int segments = 32)
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
        var m = new ModelMaterial { BaseColor = new Vector4(0.8f, 0.6f, 0.4f, 1) };
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

    /// <summary>같은 설정으로 다듬기 전 그림과 다듬은 그림.</summary>
    private static (PixelImage Plain, PixelImage Lined) Both(PosedMesh mesh, ModelRenderSettings s)
    {
        var fit = ModelRenderer.Bounds(mesh);
        return (ModelRenderer.RenderLayers(mesh, s, fit).Image, ModelRenderer.Render(mesh, s, fit));
    }

    private static readonly (int X, int Y)[] Neighbours = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    /// <summary>상하좌우 이웃 중 <paramref name="match"/>에 맞는 것이 있는지(그림 밖은 투명).</summary>
    private static bool AnyNeighbour(PixelImage img, int x, int y, Func<Color32, bool> match)
    {
        foreach (var (dx, dy) in Neighbours)
            if (match(img.Get(x + dx, y + dy))) return true;
        return false;
    }

    [Fact]
    public void OuterOutside_AddsOnePixelRingAroundModel()
    {
        var (plain, lined) = Both(Cube(), Front() with { OutlineOuter = true });
        var bounds = plain.GetOpaqueBounds();
        Assert.False(bounds.IsEmpty);
        Assert.Equal(Rectangle.Inflate(bounds, 1, 1), lined.GetOpaqueBounds());
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                if (plain[x, y].A != 0) Assert.Equal(plain[x, y], lined[x, y]);
                else if (AnyNeighbour(plain, x, y, c => c.A != 0)) Assert.Equal(Line, lined[x, y]);
                else Assert.Equal(Color32.Transparent, lined[x, y]);
            }
        // 상하좌우 이웃만 — 고리의 대각선 모서리 칸은 비어 있다.
        Assert.Equal(Color32.Transparent, lined[bounds.Left - 1, bounds.Top - 1]);
    }

    [Fact]
    public void OuterInside_KeepsBoundsAndPaintsBorderPixels()
    {
        var (plain, lined) = Both(Cube(), Front() with { OutlineOuter = true, OutlineOutside = false });
        Assert.Equal(plain.GetOpaqueBounds(), lined.GetOpaqueBounds());
        int border = 0;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                if (plain[x, y].A == 0) Assert.Equal(Color32.Transparent, lined[x, y]);
                else if (AnyNeighbour(plain, x, y, c => c.A == 0))
                {
                    Assert.Equal(Line, lined[x, y]);
                    border++;
                }
                else Assert.Equal(plain[x, y], lined[x, y]);
            }
        Assert.Equal(4 * 17, border);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DarkenOutline_IsDarkerThanAdjacentFace(bool outside)
    {
        var s = Front() with { OutlineOuter = true, OutlineOutside = outside, OutlineColorMode = ModelOutlineColor.Darken, OutlineDarken = 0.5f };
        var (plain, lined) = Both(Cube(), s);
        float faceL = OkLab.FromColor(Red).L;
        int checkedPixels = 0;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                if (lined[x, y] == plain[x, y]) continue;
                Assert.Equal(255, lined[x, y].A);
                float l = OkLab.FromColor(lined[x, y]).L;
                Assert.InRange(l, faceL * 0.45f, faceL * 0.55f);
                checkedPixels++;
            }
        Assert.True(checkedPixels > 40);
    }

    [Fact]
    public void Crease_DrawsLinesBetweenFacesOnly()
    {
        var (plain, lined) = Both(Cube(), Front() with { Yaw = 30, Pitch = 20, OutlineCrease = 45 });
        int lines = 0;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var p = plain[x, y];
                if (lined[x, y] != p)
                {
                    // 선은 다른 면과 맞닿은 모델 픽셀에만.
                    Assert.Equal(Line, lined[x, y]);
                    Assert.Equal(255, p.A);
                    Assert.True(AnyNeighbour(plain, x, y, c => c.A != 0 && c != p), $"({x}, {y}) inside a face");
                    lines++;
                }
                // 다른 면끼리 맞닿은 쌍은 어느 한쪽이 선.
                if (p.A == 0) continue;
                foreach (var (dx, dy) in Neighbours)
                {
                    var q = plain.Get(x + dx, y + dy);
                    if (q.A != 0 && q != p)
                        Assert.True(lined[x, y] == Line || lined[x + dx, y + dy] == Line, $"({x}, {y})–({x + dx}, {y + dy}) missing crease");
                }
            }
        Assert.True(lines > 10);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void SmoothSphere_GetsNoCreaseOrInnerLines(int ss)
    {
        var s = Front(48, ss) with { Yaw = 30, Pitch = 20, OutlineCrease = 45, OutlineInner = true };
        var (plain, lined) = Both(Sphere(), s);
        Assert.True(plain.ContentEquals(lined));
    }

    [Fact]
    public void Inner_LinesFarSideWhereNearQuadOverlaps()
    {
        var far = Quad(new Vector3(0, 0, -1), Vector3.UnitX, Vector3.UnitY, Unlit(0, 1, 0));
        var near = Quad(new Vector3(0, 0, 0.5f), Vector3.UnitX * 0.25f, Vector3.UnitY * 0.25f, Unlit(1, 0, 0));
        var (plain, lined) = Both(new PosedMesh([far, near]), Front() with { OutlineInner = true });
        int lines = 0;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                if (plain[x, y] == Green && AnyNeighbour(plain, x, y, c => c == Red))
                {
                    Assert.Equal(Line, lined[x, y]);
                    lines++;
                }
                else Assert.Equal(plain[x, y], lined[x, y]);
            }
        Assert.True(lines > 8);
    }

    /// <summary>나란히 붙은 두 사각형(왼쪽 x −1~0, 오른쪽 0~1+<paramref name="gap"/>, 같은 깊이).</summary>
    private static (PosedPrimitive Left, PosedPrimitive Right) Pair(ModelMaterial left, ModelMaterial right, float gap = 0) =>
        (Quad(new Vector3(-0.5f, 0, 0), Vector3.UnitX * 0.5f, Vector3.UnitY * 0.5f, left),
         Quad(new Vector3(0.5f + gap, 0, 0), Vector3.UnitX * 0.5f, Vector3.UnitY * 0.5f, right));

    /// <summary>가운데 줄에서 선으로 바뀐 픽셀의 x들.</summary>
    private static List<int> LineColumns(PixelImage plain, PixelImage lined, int y = 16)
    {
        var xs = new List<int>();
        for (int x = 0; x < plain.Width; x++)
            if (lined[x, y] != plain[x, y])
            {
                Assert.Equal(Line, lined[x, y]);
                xs.Add(x);
            }
        return xs;
    }

    [Fact]
    public void ColorEdge_LinesDarkerSideWhereMaterialColorChanges()
    {
        var (left, right) = Pair(Unlit(1, 0, 0), Unlit(0, 1, 0));
        var mesh = new PosedMesh([left, right]);
        var (plain, lined) = Both(mesh, Front() with { OutlineColorEdge = 10 });
        // 빨강(OKLab L 0.63)이 초록(0.87)보다 어둡다 → 경계의 빨강 쪽 한 칸.
        var xs = LineColumns(plain, lined);
        Assert.Single(xs);
        Assert.Equal(Red, plain[xs[0], 16]);
        Assert.Equal(Green, plain[xs[0] + 1, 16]);
        Assert.Empty(LineColumns(plain, Both(mesh, Front() with { OutlineColorEdge = 80 }).Lined));
    }

    [Fact]
    public void ColorEdge_IgnoresShadingOfOneColor()
    {
        var gray = new ModelMaterial { BaseColor = new Vector4(0.6f, 0.6f, 0.6f, 1) };
        PosedPrimitive Face(Vector3 n, Vector3 u) => Quad(n * 0.5f, u * 0.5f, Vector3.Cross(n, u) * 0.5f, gray);
        var cube = new PosedMesh([Face(Vector3.UnitZ, Vector3.UnitX), Face(Vector3.UnitX, -Vector3.UnitZ), Face(Vector3.UnitY, Vector3.UnitX)]);
        var (plain, lined) = Both(cube, Front() with { Yaw = 30, Pitch = 25, OutlineColorEdge = 3 });
        // 세 면의 명암은 다르지만 바탕 색이 같아 선이 없다.
        Assert.True(plain.ContentEquals(lined));
    }

    [Theory]
    [InlineData(ModelOutlineObjects.Off, 0f, false)]
    [InlineData(ModelOutlineObjects.Nodes, 0f, true)]
    [InlineData(ModelOutlineObjects.Connected, 0f, false)]
    [InlineData(ModelOutlineObjects.Connected, 0.001f, true)]
    public void Objects_LineBetweenDifferentObjects(ModelOutlineObjects mode, float gap, bool expected)
    {
        // 같은 색 두 사각형: 노드가 다르고, 틈이 없으면 경계 꼭짓점 자리가 같아 한 덩어리.
        var (left, right) = Pair(Unlit(1, 0, 0), Unlit(1, 0, 0), gap);
        var mesh = new PosedMesh([left with { Node = 0 }, right with { Node = 1 }]);
        var (plain, lined) = Both(mesh, Front() with { OutlineObjects = mode });
        Assert.Equal(expected ? 1 : 0, LineColumns(plain, lined).Count);
    }

    [Fact]
    public void HardAlpha_LeavesOnlyOpaqueOrTransparent()
    {
        var mesh = Sphere();
        var s = Front(32, 4) with { HardAlpha = true };
        var (plain, hard) = Both(mesh, s);
        Assert.Contains(plain.Pixels, p => p.A is > 0 and < 255);
        Assert.All(hard.Pixels, p => Assert.True(p.A == 255 || p == Color32.Transparent));
        Assert.Contains(hard.Pixels, p => p.A == 255);
    }

    /// <summary>노드 하나에 앞(+Z) 빨강, 뒤(−Z) 파랑, 위(+Y) 회색 사각형 — 돌려 보기 0°는 빨강·회색, 180°는 파랑·회색.</summary>
    private static ModelScene TwoColorBox()
    {
        static ModelPrimitive Face(Vector3 c, Vector3 u, Vector3 v, int material)
        {
            var n = Vector3.Normalize(Vector3.Cross(u, v));
            return new ModelPrimitive
            {
                Positions = [c - u - v, c + u - v, c + u + v, c - u + v],
                Normals = [n, n, n, n],
                Indices = [0, 1, 2, 0, 2, 3],
                Material = material,
            };
        }
        var h = 0.5f;
        var mesh = new ModelMesh
        {
            Primitives =
            [
                Face(new Vector3(0, 0, h), Vector3.UnitX * h, Vector3.UnitY * h, 0),
                Face(new Vector3(0, 0, -h), -Vector3.UnitX * h, Vector3.UnitY * h, 1),
                Face(new Vector3(0, h, 0), Vector3.UnitX * h, -Vector3.UnitZ * h, 2),
            ],
        };
        return new ModelScene
        {
            Nodes = [new ModelNode { Mesh = 0 }],
            Roots = [0],
            Meshes = [mesh],
            Materials = [Unlit(1, 0, 0), Unlit(0, 0, 1), Unlit(0.5f, 0.5f, 0.5f)],
        };
    }

    private static HashSet<uint> Colors(IEnumerable<PixelImage> images)
    {
        var set = new HashSet<uint>();
        foreach (var img in images)
            foreach (var p in img.Pixels)
                if (p.A != 0) set.Add(p.Argb);
        return set;
    }

    [Fact]
    public void Frames_ShareOnePalette()
    {
        var scene = TwoColorBox();
        var options = new ModelFrameOptions { Mode = ModelFrameMode.Turntable, TurntableFrames = 2 };
        var s = Front() with { Pitch = 20 };
        var full = ModelFrames.Render(scene, s, options);
        Assert.Equal(3, Colors(full.Images).Count);
        var reduced = ModelFrames.Render(scene, s with { Colors = 2 }, options);
        Assert.True(Colors(reduced.Images).Count <= 2);
        Assert.All(reduced.Images, i => Assert.NotEmpty(Colors([i])));
        Assert.Empty(ModelPixelPass.Palette(full.Images, 0));
        Assert.Empty(ModelPixelPass.Palette([new PixelImage(4, 4)], 8));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OutsideOutline_NotClippedAtZoomOne(int width)
    {
        // 납작하고 넓은 사각형: 둘레 구가 가로로 거의 꽉 차, 맞춤을 줄이지 않으면 모델이 그림 양 끝에 닿는다.
        var quad = Quad(Vector3.Zero, Vector3.UnitX, Vector3.UnitY * 0.1f, Unlit(1, 0, 0));
        var mesh = new PosedMesh([quad]);
        var fit = ModelRenderer.Bounds(mesh);
        var s = Front() with { OutlineOuter = true, OutlineWidth = width };
        Assert.Equal(0, ModelRenderer.RenderLayers(mesh, s with { OutlineOutside = false }, fit).Image.GetOpaqueBounds().Left);

        var layers = ModelRenderer.RenderLayers(mesh, s, fit);
        var model = layers.Image.GetOpaqueBounds();
        Assert.True(model.Left >= width && model.Top >= width && model.Right <= 32 - width && model.Bottom <= 32 - width, $"{model}");
        var lined = ModelRenderer.Render(mesh, s, fit);
        Assert.Equal(Rectangle.Inflate(model, width, width), lined.GetOpaqueBounds());
        int midY = model.Top + model.Height / 2;
        for (int k = 1; k <= width; k++)
        {
            Assert.Equal(Line, lined[model.Left - k, midY]);
            Assert.Equal(Line, lined[model.Right - 1 + k, midY]);
        }
    }
}
