using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>스킨 프리셋 내보내기: 프리셋별 프레임 시트, 레이어 빼고 합성, 내보낼 스킨 고르기, Sprite Library·결과 기록, 저장 왕복.</summary>
public class SkinPresetExportTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);

    private static void Paint(SpriteProject p, Layer layer, int frame, Color32 c)
    {
        p.BeginCelStroke(p.IndexOf(layer), frame).Set(0, 0, c);
        Assert.True(p.CommitCelStroke("점"));
    }

    /// <summary>
    /// 2×2 캔버스, 프레임 2개. 아래 빈 부모 body 아래 Body(1×1 @ (0,0): 기본 빨강, zombie 초록), 위 빈 부모 armor 아래 Plate(1×1 @ (1,0): "없음" 비움, iron 파랑).
    /// 지금 스킨 = 모두 첫 스킨. 레이어 아틀라스 켬, 칸 이름 프레임 번호.
    /// </summary>
    private static (SpriteProject P, Layer Body, Layer Armor, Layer BodyPart, Layer Plate, Skin Zombie, Skin Iron) Rig()
    {
        var p = new SpriteProject(2, 2);
        p.AddFrame();
        var body = p.AddEmptyParent("body");
        var bodyPart = p.AddPart(body, "Body", 1, 1, new Point(0, 0));
        for (int f = 0; f < 2; f++) Paint(p, bodyPart, f, Red);
        var zombie = p.AddSkin(bodyPart, "zombie");
        for (int f = 0; f < 2; f++) Paint(p, bodyPart, f, Green);
        p.SetActiveSkin(bodyPart, bodyPart.BaseSkin!);
        var armor = p.AddEmptyParent("armor");
        var plate = p.AddPart(armor, "Plate", 1, 1, new Point(1, 0));
        p.RenameSkin(plate, plate.BaseSkin!, "없음");
        var iron = p.AddSkin(plate, "iron");
        for (int f = 0; f < 2; f++) Paint(p, plate, f, Blue);
        p.SetActiveSkin(plate, plate.BaseSkin!);
        p.Export.ExportParts = true;
        p.Export.Common.Naming = SpriteNaming.FrameNumber;
        p.Export.Common.Spacing = 0;
        return (p, body, armor, bodyPart, plate, zombie, iron);
    }

    [Fact]
    public void FrameSheets_PerPresetFiles_SameSpriteNames_AndExcludedLayers()
    {
        var (p, body, _, bodyPart, plate, zombie, iron) = Rig();
        var knight = p.SaveSkinPreset("knight", [new SkinPresetEntry(plate, iron)]);
        var undead = p.SaveSkinPreset("undead", [new SkinPresetEntry(bodyPart, zombie), new SkinPresetEntry(plate, iron)]);

        // 고른 스킨 = 스킨 프리셋만(레이어 스킨은 모두 체크 해제), 스킨마다 따로 파일 — 원래 이름 파일은 지금 착장
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.ExcludedSkins = [.. bodyPart.Skins, .. plate.Skins];
        o.Presets = [knight, undead];
        o.SplitSkins = true;
        var files = ProjectExport.BuildFrameSheets(p, "hero");
        Assert.Equal(["hero", "hero@knight", "hero@undead"], files.Select(f => f.Name));
        Assert.Equal([null, knight, undead], files.Select(f => f.Variant?.Preset));
        Assert.All(files, f => Assert.Equal(["hero_0", "hero_1"], f.Sheet.Entries.Select(e => e.Name)));
        Color32 At(SheetFile f, int x) => f.Sheet.Image[f.Sheet.Entries[0].Rect.X + x, f.Sheet.Entries[0].Rect.Y];
        Assert.Equal((Red, Color32.Transparent), (At(files[0], 0), At(files[0], 1)));
        Assert.Equal((Red, Blue), (At(files[1], 0), At(files[1], 1)));
        Assert.Equal((Green, Blue), (At(files[2], 0), At(files[2], 1)));

        // 태그마다 따로 파일과 함께
        o.SplitTags = true;
        p.AddTag("idle", 0, 1);
        Assert.Equal(["hero_idle", "hero_idle@knight", "hero_idle@undead"], ProjectExport.BuildFrameSheets(p, "hero").Select(f => f.Name));
        var (frames, _) = ProjectExport.GetOutputPaths(p, "C:/x/hero.chickxel", assumePartContent: true);
        Assert.Equal(["hero_idle.png", "hero_idle@knight.png", "hero_idle@undead.png"], frames.Select(Path.GetFileName));

        // 아무것도 고르지 않으면 지금 착장
        o.Presets = [];
        Assert.Equal(["hero_idle"], ProjectExport.BuildFrameSheets(p, "hero").Select(f => f.Name));

        // 레이어 스킨 항목: 그 레이어만 그 스킨(위 레이어부터 — Plate의 iron, Body의 zombie)
        o.SplitTags = false;
        o.ExcludedSkins = [];
        Assert.Equal(["hero", "hero@iron", "hero@zombie"], ProjectExport.BuildFrameSheets(p, "hero").Select(f => f.Name));

        // 몸(부모)을 빼면 자손째 빠지고 장비만
        o.ExcludedSkins = [.. bodyPart.Skins, .. plate.Skins];
        o.Presets = [knight];
        o.ExcludedLayers = [body];
        var armorOnly = ProjectExport.BuildFrameSheets(p, "hero").Single(f => f.Name == "hero@knight");
        Assert.Equal((Color32.Transparent, Blue), (At(armorOnly, 0), At(armorOnly, 1)));
    }

    [Fact]
    public void FirstSkinOnlyPreset_BecomesTheUnsuffixedSheet()
    {
        // 예전 파일에서 바뀐 "그룹 첫 스킨" 프리셋처럼 첫 스킨만 담은 프리셋은 원래 이름 파일이 된다(지금 착장 대신)
        var (p, _, _, bodyPart, plate, zombie, iron) = Rig();
        p.SetActiveSkin(bodyPart, zombie);
        p.SetActiveSkin(plate, iron);
        var plain = p.SaveSkinPreset("기본", [new SkinPresetEntry(bodyPart, bodyPart.BaseSkin!)]);
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.ExcludedSkins = [.. bodyPart.Skins, .. plate.Skins];
        o.Presets = [plain];
        o.SplitSkins = true;
        var file = Assert.Single(ProjectExport.BuildFrameSheets(p, "hero"));
        Assert.Equal("hero", file.Name);
        Assert.Same(plain, file.Variant!.Preset);
        Assert.Equal(Red, file.Sheet.Image[file.Sheet.Entries[0].Rect.X, file.Sheet.Entries[0].Rect.Y]);
    }

    [Fact]
    public void PartsAtlas_SkinFilter_OnlyOwnCelsOfExportedSkins()
    {
        var (p, _, _, bodyPart, _, zombie, _) = Rig();
        string[] Names() => ProjectExport.BuildPartsSheet(p, "x_parts").Entries.Select(e => e.Name).ToArray();
        p.Export.Common.Skins = SkinSource.Selected; // 고른 스킨(모두 체크 = 예전 부위 아틀라스 기본)
        Assert.Equal(["Plate@iron", "Plate_1@iron", "Body", "Body_1", "Body@zombie", "Body_1@zombie"], Names());

        // 스킨 빼기: 스프라이트와 라이브러리 모두
        p.Export.Common.ExcludedSkins = [zombie];
        Assert.Equal(["Plate@iron", "Plate_1@iron", "Body", "Body_1"], Names());

        // 다른 스킨이 그리지 않은 칸은 빈 칸: 첫 스킨을 빼면 첫 스킨 스프라이트는 하나도 남지 않고, 대신 굽지도 않는다
        var hair = p.AddEmptyParent("hair");
        var bangs = p.AddPart(hair, "Bangs", 1, 1);
        Paint(p, bangs, 0, Red);
        Paint(p, bangs, 1, Red);
        var curly = p.AddSkin(bangs, "curly");
        Paint(p, bangs, 1, Green); // curly는 프레임 1만 직접 그림, 프레임 0은 빈 칸
        p.Export.Common.ExcludedSkins = [bangs.BaseSkin!];
        var names = Names();
        Assert.Equal(["Bangs_1@curly"], names.Where(n => n.StartsWith("Bangs")));
        var sheet = ProjectExport.BuildPartsSheet(p, "x_parts");
        var e1 = sheet.Entries.Single(e => e.Name == "Bangs_1@curly");
        Assert.Equal(Green, sheet.Image[e1.Rect.X, e1.Rect.Y]);
        Assert.Same(curly, e1.Skin);

        // 모두 내보내면 첫 스킨은 그린 칸 모두, 다른 스킨은 직접 그린 칸만
        p.Export.Common.ExcludedSkins = [];
        Assert.Equal(["Bangs", "Bangs_1", "Bangs_1@curly"], Names().Where(n => n.StartsWith("Bangs")));
        _ = bodyPart;
    }

    [Fact]
    public void SpriteLibrary_OnlyExportedSkins_AndRecordsPresetFiles()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.chickxel");
        var (p, _, _, _, plate, zombie, iron) = Rig();
        p.Export.WriteSpriteLibrary = true;
        p.Export.Engine = ExportEngine.Unity;
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.ExcludedSkins = [zombie, plate.BaseSkin!];
        o.SplitSkins = true;
        var knight = p.SaveSkinPreset("knight", [new SkinPresetEntry(plate, iron)]);
        o.Presets = [knight];

        // 프레임 시트: 지금 착장(원래 이름) → 체크한 레이어 스킨(위 레이어부터) → 프리셋
        var result = ProjectIO.SaveWithExport(p, path).Export!;
        Assert.Equal(["hero.png", "hero@iron.png", "hero@knight.png"], result.Frames.Select(r => Path.GetFileName(r.ImagePath)));
        Assert.Equal(["hero_parts.spriteLib", "hero_parts@knight.spriteLib"], result.Libraries.Select(r => Path.GetFileName(r.ImagePath)));
        Assert.Contains(dir.File("hero@knight.png"), p.ExportedFiles!);
        Assert.Contains(dir.File("hero@knight.png"), ProjectIO.GetRegisteredExportPaths(path));

        // 프리셋 파일도 프로젝트 소유 결과물(파일 목록 숨김·이름 바꾸기·삭제에 딸림)
        Assert.Contains(dir.File("hero@knight.png"), ProjectIO.GetOwnedDefaultExportPaths(path));

        // 프리셋을 내보내기에서 빼면 옛 프리셋 파일은 정리 대상
        o.Presets = [];
        var again = ProjectIO.SaveWithExport(p, path).Export!;
        Assert.Contains(dir.File("hero@knight.png"), again.Stale);
    }

    [Fact]
    public void SaveLoad_RoundTripsPresetsAndExportSelections()
    {
        using var dir = new TempDir();
        var (p, body, armor, bodyPart, plate, zombie, iron) = Rig();
        var knight = p.SaveSkinPreset("knight", [new SkinPresetEntry(plate, iron), new SkinPresetEntry(bodyPart, zombie)]);
        p.SaveSkinPreset("plain", [new SkinPresetEntry(bodyPart, bodyPart.BaseSkin!)]);
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.Presets = [knight];
        o.ExcludedLayers = [body];
        o.ExcludedSkins = [plate.BaseSkin!];
        o.Order = new GridOrder([GridKey.Skin, GridKey.Part, GridKey.Frame], 1);
        p.Export.PartsOverride = new SheetOptions { Layout = SheetLayout.Packed, Skins = SkinSource.Selected, ExcludedSkins = [zombie], ExcludedLayers = [armor] };
        var path = dir.File("o.chickxel");
        ProjectIO.Save(p, path);
        AddLegacyBakeKey(path); // 예전 파일의 없어진 "물려받은 칸 굽기" 값은 무시하고 읽는다

        var warnings = new List<string>();
        var back = ProjectIO.Load(path, warnings);
        Assert.Empty(warnings);
        var bBody = back.Layers.Single(l => l.Name == "body");
        var bArmor = back.Layers.Single(l => l.Name == "armor");
        var bPart = back.Layers.Single(l => l.Name == "Body");
        var bPlate = back.Layers.Single(l => l.Name == "Plate");
        Assert.Equal(["knight", "plain"], back.SkinPresets.Select(o => o.Name));
        var bk = back.SkinPresets[0];
        Assert.Equal([("Plate", "iron"), ("Body", "zombie")], bk.Entries.Select(e => (e.Layer.Name, e.Skin.Name)));
        Assert.Same(bPlate, bk.Entries[0].Layer);
        Assert.Same(bPart.Skins[1], bk.Entries[1].Skin);
        var e = back.Export.Common;
        Assert.Equal(SkinSource.Selected, e.Skins);
        Assert.Equal([bk], e.Presets);
        Assert.Equal([bBody], e.ExcludedLayers);
        Assert.Equal([bPlate.BaseSkin!], e.ExcludedSkins);
        Assert.Equal(new GridOrder([GridKey.Skin, GridKey.Part, GridKey.Frame], 1), e.Order);
        var po = back.Export.PartsOverride!;
        Assert.Equal((SheetLayout.Packed, SkinSource.Selected), (po.Layout, po.Skins));
        Assert.Equal([bPart.Skins.Single(s => s.Name == "zombie")], po.ExcludedSkins);
        Assert.Equal([bArmor], po.ExcludedLayers);

        // 예전 파일(설정 없음) = 기본값
        var fresh = new SpriteProject(2, 2);
        ProjectIO.Save(fresh, dir.File("f.chickxel"));
        var fe = ProjectIO.Load(dir.File("f.chickxel"));
        Assert.Empty(fe.SkinPresets);
        Assert.Null(fe.Export.PartsOverride);
        var c = fe.Export.Common;
        Assert.Equal(SkinSource.Current, c.Skins);
        Assert.Empty(c.Presets);
        Assert.Empty(c.ExcludedLayers);
        Assert.Empty(c.ExcludedSkins);
        Assert.Equal(GridOrder.ByPart, c.Order);
    }

    /// <summary>project.json의 시트 설정마다 예전 키 "bakeInheritedSkins": true를 넣는다.</summary>
    private static void AddLegacyBakeKey(string path)
    {
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update);
        var entry = zip.GetEntry("project.json")!;
        System.Text.Json.Nodes.JsonNode root;
        using (var s = entry.Open()) root = System.Text.Json.Nodes.JsonNode.Parse(s)!;
        int added = 0;
        void Walk(System.Text.Json.Nodes.JsonNode? node)
        {
            if (node is System.Text.Json.Nodes.JsonObject o)
            {
                if (o.ContainsKey("layout") && o.ContainsKey("order")) { o["bakeInheritedSkins"] = true; added++; }
                foreach (var (_, v) in o.ToList()) Walk(v);
            }
            else if (node is System.Text.Json.Nodes.JsonArray a)
                foreach (var v in a) Walk(v);
        }
        Walk(root);
        Assert.True(added > 0);
        entry.Delete();
        using var w = new StreamWriter(zip.CreateEntry("project.json").Open());
        w.Write(root.ToJsonString());
    }
}

