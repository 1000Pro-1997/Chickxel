using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>
/// 레이어 나무 파일·내보내기: 형식 6(그룹·그룹 스킨·부위 전용 스킨·착장·그룹 효과) 파일을 나무로 바꿔 읽어도 그림·레이어 아틀라스 칸 이름이 같고,
/// 프리셋이 생기며, 형식 7로 다시 저장해도 같다. Unity 프리팹은 나무대로 중첩, Sprite Library 분류 이름은 겹치지 않게.
/// </summary>
public class LayerTreeFileTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    /// <summary>예전 판(형식 6)으로 만든 고정 파일과 그 판이 그린 기준 결과(<c>tests/fixtures/tree</c> — 예전 Core로 만듦).</summary>
    private static string Tree(string name) => TestUtil.Fixture(Path.Combine("tree", name));

    private static string[] AtlasLines(SpriteProject p)
        => [.. ProjectExport.BuildPartsSheet(p, "x").Entries
            .Select(e => $"{e.Name}\t{e.Rect.Width}x{e.Rect.Height}\t{e.Pivot.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{e.Pivot.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}")
            .OrderBy(s => s, StringComparer.Ordinal)];

    [Fact]
    public void Version6GroupSkinsOutfitAndGroupEffect_ConvertToTree_RenderIdentically_AndKeepAtlasNames()
    {
        var warnings = new List<string>();
        var p = ProjectIO.Load(Tree("group-skins-v6.chickxel"), warnings);
        Assert.Equal(6, p.ConvertedFromVersion);
        Assert.Equal(["바닥", "Arm", "Head", "body", "Tail", "rig2"], p.Layers.Select(l => l.Name));
        PartsTests.AssertHierarchy(p);

        // 그룹 → 캔버스 크기 빈 부모(셀 없음), 부위 → 자식
        var body = p.Layers[3];
        var arm = p.Layers[1];
        var head = p.Layers[2];
        Assert.True(body.IsPart);
        Assert.Equal(new Rectangle(0, 0, p.Width, p.Height), body.PartRect);
        Assert.Equal([arm, head], p.ChildrenOf(body));
        Assert.DoesNotContain(p.StoredCels, c => c.Layer == body);
        Assert.True(p.Layers[5].Collapsed);
        Assert.Equal([p.Layers[4]], p.ChildrenOf(p.Layers[5]));
        // 그룹 효과 → 자손까지, 부위 효과 → 자기 셀
        Assert.All(body.Effects, e => Assert.True(e.IncludeChildren));
        Assert.NotEmpty(body.Effects);
        Assert.All(arm.Effects, e => Assert.False(e.IncludeChildren));

        // 그룹 전체 스킨 → 부위마다 같은 이름의 레이어 스킨, 부위 전용 스킨 → 그 레이어 스킨
        Assert.Equal(["기본", "knight"], arm.Skins.Select(s => s.Name));
        Assert.Equal(["기본", "knight", "hat"], head.Skins.Select(s => s.Name));
        Assert.Single(p.Layers[4].Skins);
        // 지금 스킨: 부위 재정의 → 그룹의 지금 스킨
        Assert.Equal(("knight", "hat"), (arm.ActiveSkin!.Name, head.ActiveSkin!.Name));

        // 프리셋: 그룹 전체 스킨마다(그 부위들 → 그 스킨) + 착장
        var knight = p.SkinPresets.Single(s => s.Name == "knight");
        Assert.Equal([(arm, "knight"), (head, "knight")], knight.Entries.Select(e => (e.Layer, e.Skin.Name)).OrderBy(e => p.IndexOf(e.Layer)));
        var mix = p.SkinPresets.Single(s => s.Name == "mix");
        Assert.Equal([(arm, "knight"), (head, "knight")], mix.Entries.Select(e => (e.Layer, e.Skin.Name)).OrderBy(e => p.IndexOf(e.Layer)));
        Assert.All(p.SkinPresets.SelectMany(s => s.Entries), e => Assert.Contains(e.Skin, e.Layer.Skins));
        Assert.Contains(mix, p.Export.Frames.Presets);

        // 그림: 프레임마다 지금 착장 합성, 착장 → 프리셋 합성이 예전과 같다
        for (int f = 0; f < p.Frames.Count; f++)
        {
            Assert.True(PngIO.Load(Tree($"group-skins-v6.frame{f}.png")).ContentEquals(p.Flatten(f)), $"frame {f}");
            Assert.True(PngIO.Load(Tree($"group-skins-v6.mix{f}.png")).ContentEquals(p.FlattenSkinPreset(f, mix)), $"mix {f}");
        }
        // 레이어 아틀라스 칸 이름·크기·피벗이 예전과 같다(Unity 참조가 이어짐)
        Assert.Equal(File.ReadAllLines(Tree("group-skins-v6.atlas.txt")), AtlasLines(p));

        // 형식 7로 다시 저장 → 그대로
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        var back = ProjectIO.Load(stream);
        Assert.Null(back.ConvertedFromVersion);
        Assert.Equal(p.Layers.Select(l => (l.Name, l.Kind, l.Parent?.Name, l.PartRect, l.ActiveSkin?.Name)),
            back.Layers.Select(l => (l.Name, l.Kind, l.Parent?.Name, l.PartRect, l.ActiveSkin?.Name)));
        Assert.Equal(p.SkinPresets.Select(s => s.Name), back.SkinPresets.Select(s => s.Name));
        for (int f = 0; f < p.Frames.Count; f++) Assert.True(p.Flatten(f).ContentEquals(back.Flatten(f)));
        Assert.Equal(AtlasLines(p), AtlasLines(back));
        Assert.True(back.Layers[3].Effects.All(e => e.IncludeChildren));
    }

    private static void Fill(SpriteProject p, Layer part, int frame, Color32 c)
    {
        var s = p.BeginCelStroke(p.IndexOf(part), frame);
        s.Clear(c);
        Assert.True(p.CommitCelStroke("칠"));
    }

    /// <summary>Unity YAML 파일의 문서들(<c>--- !u!형식 &amp;번호</c>마다): 형식 이름(GameObject·Transform …), 번호, 내용.</summary>
    private static List<(string Type, string Id, YamlNode Node)> Documents(string text)
    {
        var docs = new List<(string, string, YamlNode)>();
        foreach (var part in text.Split("--- !u!").Skip(1))
        {
            int nl = part.IndexOf('\n');
            var id = part[..nl].Split('&')[1].Trim().Split(' ')[0];
            var root = (YamlMapping)MiniYaml.Parse(part[(nl + 1)..]);
            var (type, node) = root.Entries[0];
            docs.Add((type, id, node));
        }
        return docs;
    }

    [Fact]
    public void UnityPrefab_NestsByTree_EmptyParentsAreTransforms_SortingOrderIsDrawRank()
    {
        using var dir = new TempDir();
        var p = new SpriteProject(16, 16);
        var root = p.AddEmptyParent("Root");
        var bodyPart = p.AddPart(root, "Body", 8, 8, new Point(4, 4));
        var arm = p.AddPart(bodyPart, "Arm", 4, 4, new Point(10, 6));
        var head = p.AddPart(root, "Head", 6, 6, new Point(5, 0));
        Fill(p, bodyPart, 0, Red);
        Fill(p, arm, 0, Blue);
        Fill(p, head, 0, Red);
        p.Sprite.DefaultPivot = new Pivot(0.5f, 0f);
        p.Export.ExportParts = true;
        p.Export.WritePrefab = true;
        p.Export.Engine = ExportEngine.Unity;
        var result = ProjectIO.SaveWithExport(p, dir.File("hero.chickxel")).Export!;
        Assert.Contains(result.Engine, r => Path.GetFileName(r.ImagePath) == "hero.prefab");
        var prefab = File.ReadAllText(dir.File("hero.prefab"));
        var yaml = Documents(prefab);

        // 이름 → 게임 오브젝트·Transform(부모 Transform으로 나무 확인)
        var objects = yaml.Where(d => d.Type == "GameObject").ToDictionary(d => d.Id, d => d.Node["m_Name"]!.Text!);
        var transforms = yaml.Where(d => d.Type == "Transform").ToList();
        string NameOfTransform(string id) => objects[transforms.Single(t => t.Id == id).Node["m_GameObject"]!["fileID"]!.Text!];
        string? ParentOf(string name)
        {
            var t = transforms.Single(t => objects[t.Node["m_GameObject"]!["fileID"]!.Text!] == name);
            var father = t.Node["m_Father"]!["fileID"]!.Text!;
            return father == "0" ? null : NameOfTransform(father);
        }
        Assert.Equal("hero", ParentOf("Root"));
        Assert.Equal("Root", ParentOf("Body"));
        Assert.Equal("Body", ParentOf("Arm"));
        Assert.Equal("Root", ParentOf("Head"));

        // 빈 부모(셀 없음)는 SpriteRenderer 없이 Transform만
        var renderers = yaml.Where(d => d.Type == "SpriteRenderer").ToDictionary(d => objects[d.Node["m_GameObject"]!["fileID"]!.Text!], d => d.Node);
        Assert.Equal(["Arm", "Body", "Head"], renderers.Keys.Order());
        // 그리는 차례(부모 자신 → 자식 덩어리): Body 0, Arm 1, Head 2 — 목록 순서(Arm이 Body 아래)와 다름
        Assert.Equal(["0", "1", "2"], new[] { "Body", "Arm", "Head" }.Select(n => renderers[n]["m_SortingOrder"]!.Text));
    }

    [Fact]
    public void SpriteLibrary_CategoryNamesStayUnique_AtlasNamesUnchanged()
    {
        using var dir = new TempDir();
        var p = new SpriteProject(8, 8);
        var a = p.AddEmptyParent("a");
        var b = p.AddEmptyParent("b");
        var armA = p.AddPart(a, "Arm", 2, 2, new Point(0, 0));
        var armB = p.AddPart(b, "Arm", 2, 2, new Point(4, 4));
        // 레이어 이름은 프로젝트 안에서 겹치지 않게 바뀌므로, 같은 이름을 일부러 둔다
        p.SetLayerProperties(p.IndexOf(armB), name: "Arm");
        Fill(p, armA, 0, Red);
        Fill(p, armB, 0, Blue);
        p.Export.ExportParts = true;
        p.Export.WriteSpriteLibrary = true;
        p.Export.Engine = ExportEngine.Unity;
        var result = ProjectIO.SaveWithExport(p, dir.File("dup.chickxel")).Export!;
        var lib = Assert.Single(result.Libraries);
        Assert.Equal("dup_parts.spriteLib", Path.GetFileName(lib.ImagePath));
        var categories = MiniYaml.Parse(File.ReadAllText(lib.ImagePath))["MonoBehaviour"]!["m_Library"]!.Items.Select(c => c["m_Name"]!.Text).ToList();
        Assert.Equal(["Arm", "Arm 2"], categories); // 나무 차례(위 → 아래): b의 Arm이 먼저
    }
}
