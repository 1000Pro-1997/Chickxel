using System.Drawing;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>하나로 합친 시트 설정: 프레임 시트 고른 스킨 항목, 부위 아틀라스 정렬 기준, 부위 잘라내기, 예전 파일 이행, 저장 왕복.</summary>
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
    /// 2×2 캔버스, 프레임 2개(태그 idle 0~1). 아래 그룹 body(Body 1×1 @ (0,0): 기본 빨강, knight 파랑),
    /// 위 그룹 armor(Plate 1×1 @ (1,0): 기본 초록, knight 파랑). 지금 착장 = 둘 다 기본. 간격 0, 부위 아틀라스 켬.
    /// </summary>
    private static (SpriteProject P, Layer Body, Layer Armor, Skin BodyKnight, Skin ArmorKnight) Rig()
    {
        var p = new SpriteProject(2, 2);
        p.AddFrame();
        var body = p.AddGroup("body");
        var bodyPart = p.AddPart(body, "Body", 1, 1, new Point(0, 0));
        for (int f = 0; f < 2; f++) Paint(p, bodyPart, f, Red);
        var bk = p.AddSkin(body, "knight");
        for (int f = 0; f < 2; f++) Paint(p, bodyPart, f, Blue);
        p.SetActiveSkin(body, body.BaseSkin!);
        var armor = p.AddGroup("armor");
        var plate = p.AddPart(armor, "Plate", 1, 1, new Point(1, 0));
        for (int f = 0; f < 2; f++) Paint(p, plate, f, Green);
        var ak = p.AddSkin(armor, "knight");
        for (int f = 0; f < 2; f++) Paint(p, plate, f, Blue);
        p.SetActiveSkin(armor, armor.BaseSkin!);
        p.AddTag("idle", 0, 1);
        p.Export.ExportParts = true;
        p.Export.Common.Spacing = 0;
        return (p, body, armor, bk, ak);
    }

    /// <summary>body 스킨만 고름(armor 스킨은 모두 체크 해제).</summary>
    private static SheetOptions BodyOnly(SpriteProject p, Layer armor)
    {
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.ExcludedSkins = [.. armor.Skins];
        return o;
    }

    private static Color32 At(SheetResult s, SheetEntry e, int x) => s.Image[e.Rect.X + x, e.Rect.Y];

    // ───────────── 프레임 시트: 고른 스킨 ─────────────

    [Fact]
    public void FrameSheet_Selected_Combined_SuffixedNames_RowPerItem()
    {
        var (p, _, armor, bk, _) = Rig();
        BodyOnly(p, armor);
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
        Assert.Equal(Green, At(s, s.Entries[2], 1)); // 다른 그룹은 지금 착장
        Assert.Same(bk, s.Entries[2].Variant!.Skin);
    }

    [Fact]
    public void FrameSheet_Selected_SplitSkins_FileNames_WithSplitTags()
    {
        var (p, _, armor, _, _) = Rig();
        var o = BodyOnly(p, armor);
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
    public void FrameSheet_Selected_UncheckedSkinExcluded_AndNoPlainItem()
    {
        var (p, body, armor, bk, _) = Rig();
        var o = BodyOnly(p, armor);
        o.ExcludedSkins = [.. armor.Skins, bk];
        Assert.Equal(["idle_0", "idle_1"], Assert.Single(ProjectExport.BuildFrameSheets(p, "hero")).Sheet.Entries.Select(e => e.Name));

        // 첫 스킨을 빼면 원래 이름 항목이 없다 → 항목에 @(항목이 하나뿐이면 파일 이름에, 칸 이름은 그대로)
        o.ExcludedSkins = [.. armor.Skins, body.BaseSkin!];
        var only = Assert.Single(ProjectExport.BuildFrameSheets(p, "hero"));
        Assert.Equal("hero@knight", only.Name);
        Assert.Equal(["idle_0", "idle_1"], only.Sheet.Entries.Select(e => e.Name));
        // 항목이 둘 이상이면 한 장 안의 모든 칸 이름에 @
        o.Outfits = [p.SaveOutfit("plain")];
        var both = Assert.Single(ProjectExport.BuildFrameSheets(p, "hero"));
        Assert.Equal("hero", both.Name);
        Assert.Equal(["idle_0@knight", "idle_1@knight", "idle_0@plain", "idle_1@plain"], both.Sheet.Entries.Select(e => e.Name));
        o.SplitSkins = true;
        Assert.Equal(["hero@knight", "hero@plain"], ProjectExport.BuildFrameSheets(p, "hero").Select(f => f.Name));
    }

    [Fact]
    public void FrameSheet_Selected_DuplicateSkinNames_GetGroupPrefix_AndOutfits()
    {
        var (p, body, armor, bk, ak) = Rig();
        p.SetActiveSkin(body, bk);
        p.SetActiveSkin(armor, ak);
        var full = p.SaveOutfit("full");
        p.SetActiveSkin(body, body.BaseSkin!);
        p.SetActiveSkin(armor, armor.BaseSkin!);
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.Outfits = [full];
        // 위 그룹(armor)부터: armor 기본 = 원래 이름, 이름이 겹치는 knight는 그룹 이름을 붙임
        Assert.Equal([null, "armor.knight", "기본", "body.knight", "full"], ProjectExport.FrameVariants(p).Select(v => v.Suffix));

        o.SplitSkins = true;
        var files = ProjectExport.BuildFrameSheets(p, "hero");
        Assert.Equal(["hero", "hero@armor.knight", "hero@기본", "hero@body.knight", "hero@full"], files.Select(f => f.Name));
        var outfit = files[^1].Sheet;
        Assert.Equal((Blue, Blue), (At(outfit, outfit.Entries[0], 0), At(outfit, outfit.Entries[0], 1)));
        Assert.Same(full, files[^1].Variant!.Outfit);
    }

    [Fact]
    public void FrameSheet_ExcludedLayers_DropFromCompositeAndItems()
    {
        var (p, _, armor, _, _) = Rig();
        var o = BodyOnly(p, armor);
        o.ExcludedSkins = [];
        o.ExcludedLayers = [armor];
        // armor 그룹 스킨은 항목에서 빠지고 합성에서도 빠진다
        Assert.Equal([null, "knight"], ProjectExport.FrameVariants(p).Select(v => v.Suffix));
        var s = Assert.Single(ProjectExport.BuildFrameSheets(p, "hero")).Sheet;
        Assert.Equal((Red, Color32.Transparent), (At(s, s.Entries[0], 0), At(s, s.Entries[0], 1)));

        // 부위 아틀라스에서도 그 그룹의 부위가 빠진다
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
        var g = p.AddGroup("g");
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
        var (p, body, armor, bk, _) = Rig();
        p.SetActiveSkin(body, bk);
        p.SaveOutfit("knight");
        p.SetActiveSkin(body, body.BaseSkin!);

        var warnings = new List<string>();
        var loaded = LoadWithLegacyExport(p, """{ "exportParts": false, "frameOutfits": [0] }""", warnings);
        var o = loaded.Export.Common;
        Assert.Equal(SkinSource.Selected, o.Skins);
        Assert.Equal([loaded.Outfits[0]], o.Outfits);
        Assert.True(o.SplitSkins);
        Assert.Equal(loaded.Layers.Where(l => l.IsGroup).SelectMany(l => l.Skins).Count(), o.ExcludedSkins.Count);
        Assert.Contains(warnings, w => w.Contains("고른 스킨"));
        Assert.Equal(["hero@knight"], ProjectExport.BuildFrameSheets(loaded, "hero").Select(f => f.Name));

        // 지금 착장을 끈 예전 설정은 결과가 같아 알림 없음
        warnings.Clear();
        LoadWithLegacyExport(p, """{ "exportParts": false, "frameOutfits": [0], "frameCurrentOutfit": false }""", warnings);
        Assert.DoesNotContain(warnings, w => w.Contains("고른 스킨"));
    }
}
