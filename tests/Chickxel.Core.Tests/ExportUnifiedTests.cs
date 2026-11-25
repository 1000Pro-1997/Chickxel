using System.Drawing;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>하나로 합친 시트 설정: 프레임 시트 고른 스킨 항목, 레이어 아틀라스 정렬 기준, 레이어 잘라내기, 예전 파일 이행, 저장 왕복.</summary>
public class ExportUnifiedTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);

    private static void Paint(SpriteProject p, Layer layer, int frame, Color32 c, int x = 0, int y = 0)
    {
        p.BeginCelStroke(p.IndexOf(layer), frame).Set(x, y, c);
        Assert.True(p.CommitCelStroke("점"));
    }

    /// <summary>
    /// 2×2 캔버스, 프레임 2개(태그 idle 0~1). 아래 빈 부모 body 아래 Body(1×1 @ (0,0): 기본 빨강, knight 파랑),
    /// 위 빈 부모 armor 아래 Plate(1×1 @ (1,0): 기본 초록, knight 파랑). 지금 스킨 = 모두 기본. 간격 0, 레이어 아틀라스 켬.
    /// </summary>
    private static (SpriteProject P, Layer Body, Layer Plate, Skin BodyKnight, Skin PlateKnight) Rig()
    {
        var p = new SpriteProject(2, 2);
        p.AddFrame();
        var body = p.AddEmptyParent("body");
        var bodyPart = p.AddPart(body, "Body", 1, 1, new Point(0, 0));
        for (int f = 0; f < 2; f++) Paint(p, bodyPart, f, Red);
        var bk = p.AddSkin(bodyPart, "knight");
        for (int f = 0; f < 2; f++) Paint(p, bodyPart, f, Blue);
        p.SetActiveSkin(bodyPart, bodyPart.BaseSkin!);
        var armor = p.AddEmptyParent("armor");
        var plate = p.AddPart(armor, "Plate", 1, 1, new Point(1, 0));
        for (int f = 0; f < 2; f++) Paint(p, plate, f, Green);
        var pk = p.AddSkin(plate, "knight");
        for (int f = 0; f < 2; f++) Paint(p, plate, f, Blue);
        p.SetActiveSkin(plate, plate.BaseSkin!);
        p.AddTag("idle", 0, 1);
        p.Export.ExportParts = true;
        p.Export.Common.Spacing = 0;
        return (p, bodyPart, plate, bk, pk);
    }

    /// <summary>Body 스킨만 고름(Plate 스킨은 모두 체크 해제).</summary>
    private static SheetOptions BodyOnly(SpriteProject p, Layer plate)
    {
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.ExcludedSkins = [.. plate.Skins];
        return o;
    }

    private static Color32 At(SheetResult s, SheetEntry e, int x) => s.Image[e.Rect.X + x, e.Rect.Y];

    // ───────────── 프레임 시트: 고른 스킨 ─────────────

    [Fact]
    public void FrameSheet_Selected_Combined_SuffixedNames_RowPerItem()
    {
        var (p, body, plate, bk, _) = Rig();
        BodyOnly(p, plate);
        var files = ProjectExport.BuildFrameSheets(p, "hero");
        var file = Assert.Single(files);
        Assert.Equal("hero", file.Name);
        var s = file.Sheet;
        Assert.Equal(["idle_0", "idle_1", "idle_0@knight", "idle_1@knight"], s.Entries.Select(e => e.Name));
        // 기본 순서: 항목마다 한 줄
        Assert.Equal([0, 0, 2, 2], s.Entries.Select(e => e.Rect.Y));
        Assert.Equal([0, 2, 0, 2], s.Entries.Select(e => e.Rect.X));
        Assert.Equal(Red, At(s, s.Entries[0], 0));
        Assert.Equal(Blue, At(s, s.Entries[2], 0));
        Assert.Equal(Green, At(s, s.Entries[2], 1)); // 다른 레이어는 지금 스킨
        Assert.True(s.Entries[0].Variant!.IsCurrent);
        Assert.Same(bk, s.Entries[2].Variant!.Skin);
        Assert.Same(body, s.Entries[2].Variant!.Layer);
    }

    [Fact]
    public void FrameSheet_Selected_SplitSkins_FileNames_WithSplitTags()
    {
        var (p, _, plate, _, _) = Rig();
        var o = BodyOnly(p, plate);
        o.SplitSkins = true;
        var files = ProjectExport.BuildFrameSheets(p, "hero");
        Assert.Equal(["hero", "hero@knight"], files.Select(f => f.Name));
        Assert.All(files, f => Assert.Equal(2, f.Sheet.Entries.Count));
        Assert.Equal(Blue, At(files[1].Sheet, files[1].Sheet.Entries[0], 0));

        o.SplitTags = true;
        Assert.Equal(["hero_idle", "hero_idle@knight"], ProjectExport.BuildFrameSheets(p, "hero").Select(f => f.Name));
        var (frames, _) = ProjectExport.GetOutputPaths(p, "C:/x/hero.chickxel", assumePartContent: true);
        Assert.Equal(["hero_idle.png", "hero_idle@knight.png"], frames.Select(Path.GetFileName));
    }

    [Fact]
    public void FrameSheet_Selected_UncheckedSkinExcluded_PresetItems_AndPlainPreset()
    {
        var (p, body, plate, bk, pk) = Rig();
        var o = BodyOnly(p, plate);
        o.ExcludedSkins = [.. plate.Skins, bk];
        // 고른 레이어 스킨이 없으면 지금 스킨 항목 하나(원래 이름)
        Assert.Equal(["idle_0", "idle_1"], Assert.Single(ProjectExport.BuildFrameSheets(p, "hero")).Sheet.Entries.Select(e => e.Name));

        // 첫 스킨은 항목이 아니다 — 빼도 지금 스킨 항목은 그대로
        o.ExcludedSkins = [.. plate.Skins, body.BaseSkin!];
        Assert.Equal([null, "knight"], ProjectExport.FrameVariants(p).Select(v => v.Suffix));

        // 항목이 둘 이상이면 한 장 안의 모든 칸 이름에 @(지금 스킨 항목은 원래 이름)
        o.ExcludedSkins = [.. plate.Skins, bk];
        var plateKnight = p.SaveSkinPreset("shiny", [new SkinPresetEntry(plate, pk)]);
        o.Presets = [plateKnight];
        var both = Assert.Single(ProjectExport.BuildFrameSheets(p, "hero"));
        Assert.Equal("hero", both.Name);
        Assert.Equal(["idle_0", "idle_1", "idle_0@shiny", "idle_1@shiny"], both.Sheet.Entries.Select(e => e.Name));
        Assert.Equal((Red, Blue), (At(both.Sheet, both.Sheet.Entries[2], 0), At(both.Sheet, both.Sheet.Entries[2], 1)));
        o.SplitSkins = true;
        Assert.Equal(["hero", "hero@shiny"], ProjectExport.BuildFrameSheets(p, "hero").Select(f => f.Name));

        // 모든 항목이 첫 스킨인 고른 프리셋이 있으면 그것이 원래 이름 항목(지금 스킨 항목 대신, 맨 앞)
        var plain = p.SaveSkinPreset("plain", [new SkinPresetEntry(body, body.BaseSkin!), new SkinPresetEntry(plate, plate.BaseSkin!)]);
        p.SetActiveSkin(body, bk);
        p.SetActiveSkin(plate, pk);
        o.Presets = [plateKnight, plain];
        var variants = ProjectExport.FrameVariants(p);
        Assert.Equal([null, "shiny"], variants.Select(v => v.Suffix));
        Assert.Same(plain, variants[0].Preset);
        Assert.False(variants[0].IsCurrent);
        var files = ProjectExport.BuildFrameSheets(p, "hero");
        Assert.Equal(["hero", "hero@shiny"], files.Select(f => f.Name));
        var first = files[0].Sheet;
        Assert.Equal((Red, Green), (At(first, first.Entries[0], 0), At(first, first.Entries[0], 1))); // 지금 스킨(knight)이 아니라 프리셋
    }

    [Fact]
    public void FrameSheet_Selected_DuplicateSkinNames_GetLayerPrefix_AndPresets()
    {
        var (p, body, plate, bk, pk) = Rig();
        p.SetActiveSkin(body, bk);
        p.SetActiveSkin(plate, pk);
        var full = p.SaveSkinPreset("full");
        p.SetActiveSkin(body, body.BaseSkin!);
        p.SetActiveSkin(plate, plate.BaseSkin!);
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.Presets = [full];
        // 지금 스킨 = 원래 이름, 위 레이어(Plate)부터, 이름이 겹치는 knight는 레이어 이름을 붙임
        Assert.Equal([null, "Plate.knight", "Body.knight", "full"], ProjectExport.FrameVariants(p).Select(v => v.Suffix));

        o.SplitSkins = true;
        var files = ProjectExport.BuildFrameSheets(p, "hero");
        Assert.Equal(["hero", "hero@Plate.knight", "hero@Body.knight", "hero@full"], files.Select(f => f.Name));
        var preset = files[^1].Sheet;
        Assert.Equal((Blue, Blue), (At(preset, preset.Entries[0], 0), At(preset, preset.Entries[0], 1)));
        Assert.Same(full, files[^1].Variant!.Preset);
        Assert.Equal("full", files[^1].Variant!.Label);
        Assert.Equal("Plate · knight", files[1].Variant!.Label);
    }

    [Fact]
    public void FrameSheet_ExcludedLayers_DropFromCompositeAndItems()
    {
        var (p, _, plate, _, _) = Rig();
        var armor = plate.Parent!;
        var o = BodyOnly(p, plate);
        o.ExcludedSkins = [];
        o.ExcludedLayers = [armor];
        // 뺀 부모 아래 레이어의 스킨은 항목에서 빠지고 합성에서도 빠진다
        Assert.Equal([null, "knight"], ProjectExport.FrameVariants(p).Select(v => v.Suffix));
        var s = Assert.Single(ProjectExport.BuildFrameSheets(p, "hero")).Sheet;
        Assert.Equal((Red, Color32.Transparent), (At(s, s.Entries[0], 0), At(s, s.Entries[0], 1)));

        // 레이어 아틀라스에서도 그 부모의 자손이 빠진다
        Assert.DoesNotContain(ProjectExport.BuildPartsSheet(p, "x").Entries, e => e.Name.StartsWith("Plate", StringComparison.Ordinal));
    }

    // ───────────── 부위 아틀라스: 정렬 기준 ─────────────

    private static SpriteProject PartsRig()
    {
        var (p, _, _, _, _) = Rig();
        p.Export.Common.Skins = SkinSource.Selected;
        p.Export.Common.Naming = SpriteNaming.FrameNumber;
        return p;
    }

    [Fact]
    public void PartsAtlas_ByFrame_RowPerFrame()
    {
        var p = PartsRig();
        p.Export.Common.Order = GridOrder.ByFrame;
        var s = ProjectExport.BuildPartsSheet(p, "x");
        foreach (var e in s.Entries) Assert.Equal(e.FrameIndex, e.Rect.Y); // 칸 높이 1, 간격 0
    }

    [Fact]
    public void PartsAtlas_CustomOrder_SkinFirst_BaseBeforeKnight_NamesUnchanged()
    {
        var p = PartsRig();
        var byPart = ProjectExport.BuildPartsSheet(p, "x").Entries.Select(e => e.Name).ToHashSet();
        p.Export.Common.Order = new GridOrder([GridKey.Skin, GridKey.Part, GridKey.Frame], 1);
        var s = ProjectExport.BuildPartsSheet(p, "x");
        Assert.True(byPart.SetEquals(s.Entries.Select(e => e.Name)));
        int maxBase = s.Entries.Where(e => !e.Name.Contains('@')).Max(e => e.Rect.Y);
        int minKnight = s.Entries.Where(e => e.Name.Contains('@')).Min(e => e.Rect.Y);
        Assert.True(maxBase < minKnight);
    }

    [Fact]
    public void PartsAtlas_Packed_KeepOrder_FollowsOrder()
    {
        var p = PartsRig();
        p.Export.Common.Layout = SheetLayout.Packed;
        p.Export.Common.PackOrder = AtlasPackOrder.KeepOrder;
        p.Export.Common.Order = new GridOrder([GridKey.Skin, GridKey.Part, GridKey.Frame], 1);
        var names = ProjectExport.BuildPartsSheet(p, "x").Entries.Select(e => e.Name).ToList();
        int lastBase = names.FindLastIndex(n => !n.Contains('@'));
        int firstKnight = names.FindIndex(n => n.Contains('@'));
        Assert.True(lastBase < firstKnight);

        p.Export.Common.Order = GridOrder.ByFrame;
        var byFrame = ProjectExport.BuildPartsSheet(p, "x").Entries.Select(e => e.FrameIndex).ToList();
        Assert.Equal(byFrame.Order(), byFrame);
    }

    [Fact]
    public void PartsAtlas_SplitTags_FileNames()
    {
        var p = PartsRig();
        p.AddFrame(); // 태그 없는 프레임 2(빈 칸은 스프라이트가 없으므로 그림)
        Paint(p, p.Layers.Single(l => l.Name == "Body"), 2, Red);
        p.Export.Common.SplitTags = true;
        Assert.Equal(["x_parts_idle", "x_parts_untagged"], ProjectExport.BuildPartsSheets(p, "x_parts").Select(f => f.Name));
        p.Export.Common.SplitParts = true;
        Assert.Contains("x_parts_Body_idle", ProjectExport.BuildPartsSheets(p, "x_parts").Select(f => f.Name));
    }

    [Fact]
    public void PartsAtlas_Trim_AdjustsPivot()
    {
        var p = new SpriteProject(8, 8);
        var g = p.AddEmptyParent("g");
        var part = p.AddPart(g, "Dot", 4, 4);
        Paint(p, part, 0, Red, 1, 1);
        p.Export.ExportParts = true;
        Assert.Equal(new Size(4, 4), ProjectExport.BuildPartsSheet(p, "x").Entries[0].Rect.Size);
        p.Export.Common.Trim = true;
        var e = ProjectExport.BuildPartsSheet(p, "x").Entries[0];
        Assert.Equal(new Size(1, 1), e.Rect.Size);
        Assert.Equal(new Pivot(1f, 0f), e.Pivot); // 가운데(2,2) = 잘린 칸 (1,1) 오른쪽 아래 모서리
    }

    // ───────────── 예전 파일 이행 ─────────────

    /// <summary>프로젝트를 저장한 뒤 export 블록을 예전 형식으로 바꿔 다시 읽는다.</summary>
    private static SpriteProject LoadWithLegacyExport(SpriteProject p, string legacyExport, List<string>? warnings = null)
    {
        var ms = new MemoryStream();
        ProjectIO.Save(p, ms);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            JsonNode root;
            using (var r = new StreamReader(entry.Open())) root = JsonNode.Parse(r.ReadToEnd())!;
            root["export"] = JsonNode.Parse(legacyExport);
            entry.Delete();
            using var w = new StreamWriter(zip.CreateEntry(ProjectIO.ProjectEntryName).Open());
            w.Write(root.ToJsonString());
        }
        ms.Position = 0;
        return ProjectIO.Load(ms, warnings);
    }

    [Fact]
    public void Legacy_FramesColumns_PartsPacked_KeepsPartsResultInOverride()
    {
        var (p, _, _, _, _) = Rig();
        var loaded = LoadWithLegacyExport(p, """{ "exportFrames": true, "exportParts": true, "columns": 2, "spacing": 1 }""");
        var x = loaded.Export;
        Assert.Equal((SheetLayout.Grid, SheetGrid.Columns, 2, 1), (x.Common.Layout, x.Common.Grid, x.Common.GridCount, x.Common.Spacing));
        Assert.Equal(SkinSource.Current, x.Common.Skins);
        Assert.NotNull(x.PartsOverride);
        Assert.Equal((SheetLayout.Packed, SkinSource.Selected, SpriteNaming.FrameNumber, 1),
            (x.Parts.Layout, x.Parts.Skins, x.Parts.Naming, x.Parts.Spacing));

        // 예전 부위 아틀라스 결과(자동 배치·모든 스킨·프레임 번호 이름)와 같다
        var (expected, _, _, _, _) = Rig();
        var c = expected.Export.Common;
        c.Layout = SheetLayout.Packed;
        c.Skins = SkinSource.Selected;
        c.Naming = SpriteNaming.FrameNumber;
        c.Spacing = 1;
        Assert.Equal(ProjectExport.BuildPartsSheet(expected, "x").Entries.Select(e => (e.Name, e.Rect)),
            ProjectExport.BuildPartsSheet(loaded, "x").Entries.Select(e => (e.Name, e.Rect)));
    }

    [Fact]
    public void Legacy_PartsNotExported_SingleCommon()
    {
        var (p, _, _, _, _) = Rig();
        var x = LoadWithLegacyExport(p, """{ "exportFrames": true, "exportParts": false, "columns": 0 }""").Export;
        Assert.Null(x.PartsOverride);
        Assert.Equal((SheetGrid.Rows, 1), (x.Common.Grid, x.Common.GridCount));
    }

    [Fact]
    public void Legacy_FrameOutfits_BecomeSelected_WithWarning()
    {
        var (p, body, _, bk, _) = Rig();
        p.SetActiveSkin(body, bk);
        p.SaveSkinPreset("knight");
        p.SetActiveSkin(body, body.BaseSkin!);

        var warnings = new List<string>();
        var loaded = LoadWithLegacyExport(p, """{ "exportParts": false, "frameOutfits": [0] }""", warnings);
        var o = loaded.Export.Common;
        Assert.Equal(SkinSource.Selected, o.Skins);
        Assert.Equal([loaded.SkinPresets[0]], o.Presets);
        Assert.True(o.SplitSkins);
        // 버전 7 본문에는 예전 그룹 전체 스킨이 없어 뺄 스킨도 없다 — 레이어 스킨 항목(이름이 겹쳐 레이어 이름 붙음) + 프리셋
        Assert.Empty(o.ExcludedSkins);
        Assert.Contains(warnings, w => w.Contains("고른 스킨"));
        Assert.Equal(["hero", "hero@Plate.knight", "hero@Body.knight", "hero@knight"], ProjectExport.BuildFrameSheets(loaded, "hero").Select(f => f.Name));

        // 지금 착장을 끈 예전 설정은 결과가 같아 알림 없음
        warnings.Clear();
        LoadWithLegacyExport(p, """{ "exportParts": false, "frameOutfits": [0], "frameCurrentOutfit": false }""", warnings);
        Assert.DoesNotContain(warnings, w => w.Contains("고른 스킨"));
    }
}
