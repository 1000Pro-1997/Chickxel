using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>레이어마다 스킨(일반·자리 있는 레이어)과 스킨 프리셋: 만들기·지우기·순서·지금 스킨(보기 상태), 자손까지 스킨, 프리셋 겹쳐 입히기, 실행취소, 저장.</summary>
public class SkinPresetTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);

    private static void Paint(SpriteProject p, Layer layer, int x, int y, Color32 c, int frame = 0)
    {
        p.BeginCelStroke(p.IndexOf(layer), frame).Set(x, y, c);
        Assert.True(p.CommitCelStroke("점"));
    }

    /// <summary>4×4: 빈 부모 body 아래 Arm(2×2 @ (2,2))·Head(2×2 @ (0,0)), 첫 스킨 (0,0)에 빨강.</summary>
    private static (SpriteProject P, Layer Body, Layer Arm, Layer Head) Body()
    {
        var p = new SpriteProject(4, 4);
        var body = p.AddEmptyParent("body");
        var arm = p.AddPart(body, "Arm", 2, 2, new Point(2, 2));
        var head = p.AddPart(body, "Head", 2, 2, new Point(0, 0));
        Paint(p, arm, 0, 0, Red);
        Paint(p, head, 0, 0, Red);
        return (p, body, arm, head);
    }

    [Fact]
    public void EveryLayerHasItsOwnSkins_NormalLayersToo_EmptyCelsStayEmpty()
    {
        var p = new SpriteProject(4, 4);
        var layer = p.Layers[0];
        Assert.Equal(["기본"], layer.Skins.Select(s => s.Name));
        Paint(p, layer, 1, 1, Red);
        var alt = p.AddSkin(layer, "alt");
        Assert.Same(alt, layer.ActiveSkin);
        Assert.False(layer.IsPart); // 스킨을 달아도 구조는 그대로
        Assert.Equal(Color32.Transparent, p.Flatten(0)[1, 1]); // 새 빈 스킨은 빈 칸
        Paint(p, layer, 2, 2, Blue);
        Assert.Equal(Blue, p.Flatten(0)[2, 2]);
        p.SetActiveSkin(layer, layer.BaseSkin!);
        Assert.Equal((Red, Color32.Transparent), (p.Flatten(0)[1, 1], p.Flatten(0)[2, 2]));
        var copy = p.AddSkin(layer, null, layer.BaseSkin);
        Assert.Equal("기본 복사", copy.Name);
        Assert.Equal(Red, p.Flatten(0)[1, 1]);
        Assert.Throws<InvalidOperationException>(() => p.MergeLayerDown(p.IndexOf(p.AddLayer())));
        Assert.Throws<ArgumentException>(() => p.AddSkin(p.AddTilemapLayer(Chickxel.Core.Tiles.Tileset.Create("t", 2, 2))));
    }

    [Fact]
    public void SkinOps_RenameDeleteReorder_RemapBaseKeys_AndUndo()
    {
        var (p, _, arm, _) = Body();
        var knight = p.AddSkin(arm, "knight");
        Paint(p, arm, 1, 1, Blue);
        Assert.Throws<ArgumentException>(() => p.RenameSkin(arm, knight, "기본"));
        p.RenameSkin(arm, knight, "기사");
        Assert.Equal("기사", knight.Name);

        // 순서: knight가 첫 스킨(기본)이 되어도 셀은 그대로
        p.MoveSkin(arm, 1, 0);
        Assert.Same(knight, arm.BaseSkin);
        Assert.Equal(Blue, p.GetOwnCel(arm, p.Frames[0], knight)![1, 1]);
        Assert.Equal(Red, p.GetOwnCel(arm, p.Frames[0], arm.Skins[1])![0, 0]);

        // 첫 스킨 지우기: 다음 스킨이 첫 스킨
        var old = arm.Skins[1];
        p.DeleteSkin(arm, knight);
        Assert.Same(old, arm.BaseSkin);
        Assert.Equal(Red, p.GetOwnCel(arm, p.Frames[0], old)![0, 0]);
        Assert.Throws<InvalidOperationException>(() => p.DeleteSkin(arm, old));

        p.Undo();
        p.Undo();
        Assert.Same(arm.Skins[1], knight);
        Assert.Equal(Blue, p.GetOwnCel(arm, p.Frames[0], knight)![1, 1]);
        Assert.Equal(Red, p.GetOwnCel(arm, p.Frames[0], arm.BaseSkin!)![0, 0]);

        // 지금 스킨 고르기는 실행취소 기록 없이 수정됨만
        int undo = p.History.UndoCount;
        p.IsDirty = false;
        p.SetActiveSkin(arm, arm.BaseSkin!);
        Assert.Equal(undo, p.History.UndoCount);
        Assert.True(p.IsDirty);
    }

    [Fact]
    public void AddSkinToSubtree_AddsSameNameSkinEverywhere_AndRegistersAPreset_OneUndoStep()
    {
        var (p, body, arm, head) = Body();
        int undo = p.History.UndoCount;
        var preset = p.AddSkinToSubtree(body, "knight", copy: true);
        Assert.Equal(undo + 1, p.History.UndoCount);
        Assert.Equal("knight", preset.Name);
        Assert.Equal([body, head, arm], preset.Entries.Select(e => e.Layer)); // 위 레이어부터
        Assert.All(preset.Entries, e => Assert.Equal("knight", e.Skin.Name));
        Assert.All([body, head, arm], l => Assert.Equal("knight", l.ActiveSkin!.Name));
        Assert.Equal(Red, p.Flatten(0)[0, 0]); // 복사라 그림 그대로
        Assert.Same(preset, Assert.Single(p.SkinPresets));

        // 이름이 어느 레이어에서든 겹치면 모두 피한다
        p.AddSkin(head, "winter");
        var second = p.AddSkinToSubtree(body, "winter");
        Assert.Equal("winter 2", second.Entries[0].Skin.Name);
        Assert.Equal(Color32.Transparent, p.Flatten(0)[0, 0]); // 빈 스킨

        p.Undo();
        p.Undo();
        p.Undo();
        Assert.Empty(p.SkinPresets);
        Assert.All([body, head, arm], l => Assert.Single(l.Skins));
    }

    [Fact]
    public void Presets_ApplyOnlyListedLayers_Stack_ReportMissing_AndSaveCurrentEntries()
    {
        var (p, body, arm, head) = Body();
        var armKnight = p.AddSkin(arm, "knight");
        var headHat = p.AddSkin(head, "hat");
        Paint(p, head, 1, 1, Green);
        var armOnly = p.SaveSkinPreset("팔", [new SkinPresetEntry(arm, armKnight)]);
        var hatOnly = p.SaveSkinPreset("모자", [new SkinPresetEntry(head, headHat)]);
        var all = p.SaveSkinPreset("전부"); // 지금 착장: 스킨이 둘 이상인 레이어마다(위부터)
        Assert.Equal([new SkinPresetEntry(head, headHat), new SkinPresetEntry(arm, armKnight)], all.Entries);
        Assert.Throws<ArgumentException>(() => p.SaveSkinPreset("x", [new SkinPresetEntry(arm, headHat)]));

        p.SetActiveSkin(arm, arm.BaseSkin!);
        p.SetActiveSkin(head, head.BaseSkin!);
        // 겹쳐 입히기: 프리셋에 없는 레이어는 그대로
        Assert.Empty(p.ApplySkinPreset(hatOnly));
        Assert.Same(headHat, head.ActiveSkin);
        Assert.Same(arm.BaseSkin, arm.ActiveSkin);
        p.ApplySkinPreset(armOnly);
        Assert.Same(headHat, head.ActiveSkin);
        Assert.Same(armKnight, arm.ActiveSkin);
        Assert.Same(armKnight, SpriteProject.SkinUnder(arm, armOnly));
        Assert.Same(head.ActiveSkin, SpriteProject.SkinUnder(head, armOnly));

        // 렌더만 입혀 보기(지금 스킨은 그대로)
        p.SetActiveSkin(head, head.BaseSkin!);
        Assert.Equal(Green, p.FlattenSkinPreset(0, hatOnly)[1, 1]);
        Assert.Same(head.BaseSkin, head.ActiveSkin);

        // 잠긴 레이어 알림
        p.SetLayerProperties(p.IndexOf(body), locked: true);
        Assert.Equal([head], p.LockedLayersChangedBy(hatOnly));
        p.SetLayerProperties(p.IndexOf(body), locked: false);

        // 덮어쓰기(항목을 주지 않으면 그 프리셋의 레이어만 지금 스킨으로), 이름·순서·삭제
        p.OverwriteSkinPreset(hatOnly);
        Assert.Equal([new SkinPresetEntry(head, head.BaseSkin!)], hatOnly.Entries);
        Assert.Throws<ArgumentException>(() => p.RenameSkinPreset(hatOnly, "팔"));
        p.RenameSkinPreset(hatOnly, "머리");
        p.MoveSkinPreset(2, 0);
        Assert.Equal(["전부", "팔", "머리"], p.SkinPresets.Select(o => o.Name));
        p.Edit("시트", ProjectChange.Settings, () => p.Export.Common.Presets = [all]);
        p.DeleteSkinPreset(all);
        Assert.Empty(p.Export.Common.Presets);
        p.Undo();
        Assert.Same(all, Assert.Single(p.Export.Common.Presets));

        // 지운 스킨·레이어는 건너뛰고 알린다
        p.DeleteSkin(arm, armKnight);
        p.DeleteLayer(p.IndexOf(head));
        var missing = p.ApplySkinPreset(all);
        Assert.Equal(2, missing.Count);
        Assert.Contains(missing, m => m.Contains("Head"));
        Assert.Contains(missing, m => m.Contains("knight"));
        p.Undo();
        p.Undo();
        Assert.Empty(p.MissingSkinPresetEntries(all));
    }

    [Fact]
    public void DuplicateLayer_CopiesSkinsAsNewObjects_PresetsKeepPointingToOriginal()
    {
        var (p, body, arm, _) = Body();
        var knight = p.AddSkin(arm, "knight");
        Paint(p, arm, 1, 1, Blue);
        var preset = p.SaveSkinPreset("k", [new SkinPresetEntry(arm, knight)]);
        var copy = p.DuplicateLayer(p.IndexOf(arm));
        Assert.Equal(["기본", "knight"], copy.Skins.Select(s => s.Name));
        Assert.DoesNotContain(knight, copy.Skins);
        Assert.Equal(Blue, p.GetOwnCel(copy, p.Frames[0], copy.Skins[1])![1, 1]);
        Assert.Same(arm, Assert.Single(preset.Entries).Layer);
        _ = body;
    }

    [Fact]
    public void SaveLoad_RoundTripsLayerSkinsActiveSkinsAndPresets()
    {
        var (p, body, arm, head) = Body();
        var normal = p.Layers[0];
        Paint(p, normal, 3, 0, Red);
        var normalAlt = p.AddSkin(normal, "밤");
        Paint(p, normal, 3, 1, Blue);
        var preset = p.AddSkinToSubtree(body, "knight");
        Paint(p, arm, 1, 1, Blue);
        p.SetActiveSkin(head, head.BaseSkin!);
        p.SaveSkinPreset("밤만", [new SkinPresetEntry(normal, normalAlt)]);

        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        var back = ProjectIO.Load(stream);
        Assert.Null(back.ConvertedFromVersion);
        for (int i = 0; i < p.Layers.Count; i++)
        {
            Assert.Equal(p.Layers[i].Skins.Select(s => s.Name), back.Layers[i].Skins.Select(s => s.Name));
            Assert.Equal(p.Layers[i].ActiveSkin?.Name, back.Layers[i].ActiveSkin?.Name);
            foreach (var (skin, k) in p.Layers[i].Skins.Select((s, k) => (s, k)))
                Assert.Equal(p.GetOwnCel(p.Layers[i], p.Frames[0], skin)?.Pixels, back.GetOwnCel(back.Layers[i], back.Frames[0], back.Layers[i].Skins[k])?.Pixels);
        }
        Assert.Equal(["knight", "밤만"], back.SkinPresets.Select(o => o.Name));
        Assert.Equal(preset.Entries.Select(e => (p.IndexOf(e.Layer), e.Skin.Name)),
            back.SkinPresets[0].Entries.Select(e => (back.IndexOf(e.Layer), e.Skin.Name)));
        Assert.Same(back.Layers[0].Skins[1], back.SkinPresets[1].Entries[0].Skin);
        Assert.True(back.Flatten(0).ContentEquals(p.Flatten(0)));
    }
}
