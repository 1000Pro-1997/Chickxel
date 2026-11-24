using System.Numerics;
using Chickxel.Core.Model;
using Chickxel.Core.Scene3D;
using Xunit;

namespace Chickxel.Core.Tests;

public class ModelColorsTests
{
    private static ModelPrimitive Triangles(int count, Vector4? vertexColor, int material)
    {
        var pos = new Vector3[count * 3];
        for (int i = 0; i < pos.Length; i++) pos[i] = new Vector3(i, i % 3, 0);
        return new ModelPrimitive
        {
            Positions = pos,
            Indices = [.. Enumerable.Range(0, pos.Length)],
            Colors = vertexColor is { } c ? [.. Enumerable.Repeat(c, pos.Length)] : null,
            Material = material,
        };
    }

    private static ModelPrimitive Textured(int count)
    {
        var t = Triangles(count, null, 2);
        return new ModelPrimitive { Positions = t.Positions, Indices = t.Indices, Uvs = new Vector2[t.Positions.Length], Material = 2 };
    }

    /// <summary>흰 재질 + 빨간 꼭짓점 색 삼각형 1개, 회색 재질(꼭짓점 색 없음) 삼각형 2개, 텍스처 재질 삼각형 3개.</summary>
    private static ModelScene Scene()
    {
        var texture = new PixelImage(1, 1);
        return new ModelScene
        {
            Nodes = [new ModelNode { Mesh = 0 }],
            Roots = [0],
            Meshes = [new ModelMesh { Primitives = [Triangles(1, new Vector4(1, 0, 0, 1), 0), Triangles(2, null, 1), Textured(3)] }],
            Materials =
            [
                new ModelMaterial { BaseColor = Vector4.One },
                new ModelMaterial { BaseColor = new Vector4(0.5f, 0.5f, 0.5f, 1) },
                new ModelMaterial { BaseColor = Vector4.One, Texture = texture },
            ],
        };
    }

    private static readonly Color32 Red = new(255, 0, 0);
    /// <summary>선형 0.5 회색의 sRGB.</summary>
    private static readonly Color32 Gray = new(188, 188, 188);

    [Fact]
    public void Used_ListsBaseColorsMostUsedFirstWithoutTextured()
    {
        Assert.Equal([Gray, Red], ModelColors.Used(Scene()));
    }

    [Fact]
    public void Recolor_ChangesOnlyMappedColorsAndKeepsTheRest()
    {
        var scene = Scene();
        var blue = new Color32(0, 0, 255);
        var dark = new Color32(60, 60, 60);
        var recolored = ModelColors.Recolor(scene, new Dictionary<Color32, Color32> { [Red] = blue, [Gray] = dark });
        Assert.Equal([dark, blue], ModelColors.Used(recolored));
        // 원래 재질은 그대로 앞에 두고(바꾼 묶음만 끝에 더한 흰 사본을 씀), 텍스처 묶음과 원래 장면은 그대로.
        Assert.Equal(scene.Materials, recolored.Materials.Take(scene.Materials.Count));
        Assert.Same(scene.Meshes[0].Primitives[2], recolored.Meshes[0].Primitives[2]);
        Assert.Equal([Gray, Red], ModelColors.Used(scene));
        Assert.Same(scene, ModelColors.Recolor(scene, new Dictionary<Color32, Color32>()));
    }

    [Fact]
    public void Recolor_ReplacesWithColorsTheMaterialHasNoneOf()
    {
        // 파랑이 전혀 없는 노란 재질: 예전에는 꼭짓점 색으로만 물들여 파랑을 만들 수 없었다.
        var yellowMaterial = new ModelMaterial { BaseColor = new Vector4(1, 0.5f, 0, 1), Unlit = true };
        var scene = new ModelScene
        {
            Nodes = [new ModelNode { Mesh = 0 }],
            Roots = [0],
            Meshes = [new ModelMesh { Primitives = [Triangles(2, null, 0)] }],
            Materials = [yellowMaterial],
        };
        var yellow = ModelColors.Used(scene).Single();
        var blue = new Color32(40, 90, 255);
        var recolored = ModelColors.Recolor(scene, new Dictionary<Color32, Color32> { [yellow] = blue });
        Assert.Equal([blue], ModelColors.Used(recolored));
        var copy = recolored.Materials[recolored.Meshes[0].Primitives[0].Material];
        Assert.True(copy.Unlit);
        Assert.Equal(new Vector4(1, 0.5f, 0, 1), yellowMaterial.BaseColor);
    }

    [Fact]
    public void Recolor_WithTransparentColorMakesThatPartTranslucent()
    {
        var scene = Scene();
        var glassBlue = new Color32(0, 0, 255, 128);
        var recolored = ModelColors.Recolor(scene, new Dictionary<Color32, Color32> { [Red] = glassBlue });
        var p = recolored.Meshes[0].Primitives[0];
        Assert.Equal(ModelAlphaMode.Blend, recolored.Materials[p.Material].AlphaMode);
        Assert.All(p.Colors!, c => Assert.Equal(128 / 255f, c.W, 3));
        // 투명도 없이 바꾼 회색 묶음은 반투명이 되지 않는다
        var gray = ModelColors.Recolor(scene, new Dictionary<Color32, Color32> { [Gray] = new Color32(60, 60, 60) });
        Assert.Equal(ModelAlphaMode.Opaque, gray.Materials[gray.Meshes[0].Primitives[1].Material].AlphaMode);
    }
}
