using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>
/// Unity 프리팹·애니메이션 내보내기(<see cref="ExportSettings.WritePrefab"/>·<see cref="ExportSettings.WriteAnimations"/>):
/// 레이어 나무대로 중첩한 오브젝트 자리·스프라이트 참조, 태그마다 클립(스프라이트·움직인 레이어 자리·길이), Controller·Animator 연결, 다시 내보내도 guid 유지, 끄면 옛 파일 정리.
/// (같은 꼴의 파일을 Unity 6000.6 배치 모드로 가져와 Animator 재생까지 확인함.)
/// </summary>
public class UnityAssetExportTests
{
    private static void Fill(SpriteProject p, Layer part, int frame, Color32 c)
    {
        var s = p.BeginCelStroke(p.IndexOf(part), frame);
        for (int y = 0; y < part.PartSize.Height; y++)
            for (int x = 0; x < part.PartSize.Width; x++) s.Set(x, y, c);
        Assert.True(p.CommitCelStroke("칠"));
    }

    /// <summary>
    /// 16×16, 프레임 4개(idle 0~1 100ms, walk 2~3 150ms). 빈 부모 Hero(캔버스 크기 @ (0,0), 피벗 가운데 (8,8)) 아래 Legs 8×6 @ (4,10), Head 8×8 @ (4,2)
    /// — walk에서 Head가 1px 아래로.
    /// 캔버스 피벗 = 아래 가운데, PPU 16.
    /// </summary>
    private static (SpriteProject P, Layer Head) Hero()
    {
        var p = new SpriteProject(16, 16);
        for (int i = 0; i < 3; i++) p.AddFrame();
        var g = p.AddEmptyParent("Hero");
        var legs = p.AddPart(g, "Legs", 8, 6, new Point(4, 10));
        var head = p.AddPart(g, "Head", 8, 8, new Point(4, 2));
        for (int f = 0; f < 4; f++)
        {
            Fill(p, legs, f, new Color32(0, 0, 255, 255));
            Fill(p, head, f, f < 2 ? new Color32(255, 0, 0, 255) : new Color32(0, 255, 0, 255));
        }
        p.MovePartFrom(p.IndexOf(head), 2, 0, 1);
        p.Sprite.DefaultPivot = new Pivot(0.5f, 0f);
        p.AddTag("idle", 0, 1);
        p.AddTag("walk", 2, 3);
        p.SetFrameDuration(2, 150);
        p.SetFrameDuration(3, 150);
        p.Export.ExportParts = true;
        p.Export.WritePrefab = true;
        p.Export.Engine = ExportEngine.Unity;
        p.Export.WriteAnimations = true;
        return (p, head);
    }

    private static string Guid(string path) => UnityMeta.Read(UnityMeta.GetMetaPath(path)).Guid!;

    private static long SpriteId(string atlas, string name) => UnityMeta.Read(UnityMeta.GetMetaPath(atlas)).Sprites.Single(s => s.Name == name).InternalId;

    /// <summary>YAML 덩어리 안에서 <paramref name="from"/> 줄 다음에 처음 나오는 <paramref name="key"/> 줄의 값.</summary>
    private static string After(string yaml, string from, string key)
    {
        int i = yaml.IndexOf(from, StringComparison.Ordinal);
        Assert.True(i >= 0, from);
        int k = yaml.IndexOf(key, i, StringComparison.Ordinal);
        Assert.True(k >= 0, key);
        int end = yaml.IndexOf('\n', k);
        return yaml[(k + key.Length)..end].Trim();
    }

    [Fact]
    public void Parts_PrefabPlacesPartsAtPivots_ClipsSwapSpritesAndMoveParts_ControllerWired()
    {
        using var dir = new TempDir();
        var (p, _) = Hero();
        var result = ProjectIO.SaveWithExport(p, dir.File("hero.chickxel")).Export!;
        Assert.Equal(["hero.prefab", "hero_idle.anim", "hero_walk.anim", "hero.controller"], result.Engine.Select(r => Path.GetFileName(r.ImagePath)));
        foreach (var r in result.Engine)
        {
            Assert.Contains(r.ImagePath, p.ExportedFiles!);
            Assert.Contains(UnityMeta.GetMetaPath(r.ImagePath), p.ExportedFiles!);
        }

        var atlas = dir.File("hero_parts.png");
        var atlasGuid = Guid(atlas);
        var prefab = File.ReadAllText(dir.File("hero.prefab"));
        // 맨 위 = 캔버스 피벗(8,16). 빈 부모 Hero = 자기 피벗 (8,8) → 위로 8px = 0.5(SpriteRenderer 없음).
        // 자식은 부모 기준: Head 피벗(가운데) (8,6) → Hero보다 위로 2px = 0.125(맨 위 기준 0.625), Legs (8,13) → 아래로 5px = −0.3125(맨 위 기준 0.1875).
        Assert.Equal("{x: 0, y: 0.5, z: 0}", After(prefab, "m_Name: Hero", "m_LocalPosition:"));
        Assert.Equal("{x: 0, y: 0.125, z: 0}", After(prefab, "m_Name: Head", "m_LocalPosition:"));
        Assert.Equal("{x: 0, y: -0.3125, z: 0}", After(prefab, "m_Name: Legs", "m_LocalPosition:"));
        Assert.Equal(2, prefab.Split("\nSpriteRenderer:").Length - 1);
        // 그리는 순서 = 최종 그리는 차례(SpriteRenderer 있는 레이어만): Legs(아래) 0, Head 1
        Assert.Equal("1", After(prefab, "m_Name: Head", "m_SortingOrder:"));
        Assert.Equal("0", After(prefab, "m_Name: Legs", "m_SortingOrder:"));
        Assert.Equal($"{{fileID: {SpriteId(atlas, "Head_idle_0")}, guid: {atlasGuid}, type: 3}}", After(prefab, "m_Name: Head", "m_Sprite:"));
        Assert.Equal($"{{fileID: 9100000, guid: {Guid(dir.File("hero.controller"))}, type: 2}}", After(prefab, "Animator:", "m_Controller:"));
        Assert.Contains(UnityAssetWriter.BuiltInSpriteMaterial, prefab); // Unity 프로젝트 밖 = 내장 머티리얼

        // walk: 150ms 두 칸 → 샘플 20, 길이 0.3. Head는 1px 아래(Hero 기준 0.0625, 맨 위 기준 0.5625)로 움직이고 스프라이트는 walk 칸.
        var walk = File.ReadAllText(dir.File("hero_walk.anim"));
        Assert.Equal("hero_walk", After(walk, "AnimationClip:", "m_Name:"));
        Assert.Equal("20", After(walk, "m_PPtrCurves", "m_SampleRate:"));
        Assert.Equal("0.3", After(walk, "m_AnimationClipSettings", "m_StopTime:"));
        Assert.Equal("{x: 0, y: 0.0625, z: 0}", After(walk, "m_PositionCurves", "value:"));
        Assert.Equal("Hero/Head", After(walk, "m_PositionCurves", "path:"));
        Assert.Equal($"{{fileID: {SpriteId(atlas, "Head_walk_1")}, guid: {atlasGuid}, type: 3}}", After(walk, "time: 0.15\n", "value:"));
        // idle: Head가 제자리라 위치 곡선 없음
        Assert.Contains("m_PositionCurves: []", File.ReadAllText(dir.File("hero_idle.anim")));

        var controller = File.ReadAllText(dir.File("hero.controller"));
        Assert.Equal($"{{fileID: 7400000, guid: {Guid(dir.File("hero_idle.anim"))}, type: 2}}", After(controller, "m_Name: idle", "m_Motion:"));
        Assert.Equal($"{{fileID: 7400000, guid: {Guid(dir.File("hero_walk.anim"))}, type: 2}}", After(controller, "m_Name: walk", "m_Motion:"));

        // 설정은 파일에 남는다
        var loaded = ProjectIO.Load(dir.File("hero.chickxel"));
        Assert.Equal((true, true), (loaded.Export.WritePrefab, loaded.Export.WriteAnimations));
    }

    [Fact]
    public void Reexport_KeepsGuidsAndText_TurningOffLeavesOldFilesStale()
    {
        using var dir = new TempDir();
        var (p, _) = Hero();
        var path = dir.File("hero.chickxel");
        ProjectIO.SaveWithExport(p, path);
        var guid = Guid(dir.File("hero.prefab"));
        var text = File.ReadAllText(dir.File("hero.prefab"));

        ProjectIO.SaveWithExport(p, path);
        Assert.Equal(guid, Guid(dir.File("hero.prefab")));
        Assert.Equal(text, File.ReadAllText(dir.File("hero.prefab")));

        p.Export.WriteAnimations = false;
        var again = ProjectIO.SaveWithExport(p, path).Export!;
        Assert.Contains(dir.File("hero_walk.anim"), again.Stale);
        Assert.Contains(dir.File("hero.controller"), again.Stale);
        Assert.DoesNotContain("Animator:", File.ReadAllText(dir.File("hero.prefab")));
    }

    [Fact]
    public void FramesOnly_PrefabIsOneSprite_ClipSwapsFrameSprites_UnityOffWritesNothing()
    {
        using var dir = new TempDir();
        var p = new SpriteProject(4, 4);
        p.AddFrame();
        for (int f = 0; f < 2; f++)
        {
            var s = p.BeginCelStroke(0, f);
            s.Set(f, f, new Color32(255, 0, 0, 255));
            Assert.True(p.CommitCelStroke("점"));
        }
        p.Export.WritePrefab = true;
        p.Export.Engine = ExportEngine.Unity;
        p.Export.WriteAnimations = true;
        var result = ProjectIO.SaveWithExport(p, dir.File("solo.chickxel")).Export!;
        Assert.Equal(["solo.prefab", "solo.anim", "solo.controller"], result.Engine.Select(r => Path.GetFileName(r.ImagePath)));
        var sheet = dir.File("solo.png");
        var clip = File.ReadAllText(dir.File("solo.anim"));
        Assert.Equal("", After(clip, "m_PPtrCurves", "path:").Trim('\''));
        Assert.Contains($"{{fileID: {SpriteId(sheet, "solo_1")}, guid: {Guid(sheet)}, type: 3}}", clip);
        Assert.Equal($"{{fileID: {SpriteId(sheet, "solo_0")}, guid: {Guid(sheet)}, type: 3}}", After(File.ReadAllText(dir.File("solo.prefab")), "SpriteRenderer:", "m_Sprite:"));

        // Unity 파일을 끄면(이미지만) 프리팹·애니메이션도 쓰지 않는다
        p.Export.Engine = ExportEngine.None;
        Assert.Empty(ProjectExport.GetUnityAssetPaths(p, dir.File("solo.chickxel"), assumePartContent: false));
    }
}
