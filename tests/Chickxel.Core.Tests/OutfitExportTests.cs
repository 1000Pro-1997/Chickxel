using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>착장 프리셋(저장·적용·이름 추적·실행취소), 착장별 프레임 시트, 레이어 빼고 합성, 내보낼 스킨 고르기, 저장 왕복.</summary>
public class OutfitExportTests
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
    /// 2×2 캔버스, 프레임 2개. 아래 그룹 body(Body 1×1 @ (0,0): 기본 빨강, zombie 초록), 위 그룹 armor(Plate 1×1 @ (1,0): "없음" 비움, iron 파랑).
    /// 지금 착장 = body 기본, armor 없음. 부위 아틀라스 켬, 칸 이름 프레임 번호.
    /// </summary>
    private static (SpriteProject P, Layer Body, Layer Armor, Layer BodyPart, Layer Plate, Skin Zombie, Skin Iron) Rig()
    {
        var p = new SpriteProject(2, 2);
        p.AddFrame();
        var body = p.AddGroup("body");
        var bodyPart = p.AddPart(body, "Body", 1, 1, new Point(0, 0));
        for (int f = 0; f < 2; f++) Paint(p, bodyPart, f, Red);
        var zombie = p.AddSkin(body, "zombie");
        for (int f = 0; f < 2; f++) Paint(p, bodyPart, f, Green);
        p.SetActiveSkin(body, body.BaseSkin!);
        var armor = p.AddGroup("armor");
        var plate = p.AddPart(armor, "Plate", 1, 1, new Point(1, 0));
        p.RenameSkin(armor, armor.BaseSkin!, "없음");
        var iron = p.AddSkin(armor, "iron");
        for (int f = 0; f < 2; f++) Paint(p, plate, f, Blue);
        p.SetActiveSkin(armor, armor.BaseSkin!);
        p.Export.ExportParts = true;
        p.Export.Common.Naming = SpriteNaming.FrameNumber;
        p.Export.Common.Spacing = 0;
        return (p, body, armor, bodyPart, plate, zombie, iron);
    }

    [Fact]
    public void Outfit_SaveApplyTracksRenamesSkipsMissingAndUndoes()
    {
        var (p, body, armor, bodyPart, plate, zombie, iron) = Rig();
        p.SetActiveSkin(body, zombie);
        p.SetActiveSkin(armor, iron);
        var knight = p.SaveOutfit("좀비 기사");
        Assert.Equal(["좀비 기사"], p.Outfits.Select(o => o.Name));
        Assert.Equal([(armor, iron), (body, zombie)], knight.Slots.Select(s => (s.Group, s.Skin)));
        Assert.Empty(knight.Parts);

        // 부위 재정의도 담긴다
        p.SetActiveSkin(body, body.BaseSkin!);
        p.SetActiveSkin(armor, armor.BaseSkin!);
        p.SetPartSkin(bodyPart, zombie);
        var mixed = p.SaveOutfit("좀비 기사");
        Assert.Equal("좀비 기사 2", mixed.Name);
        Assert.Equal([(bodyPart, zombie)], mixed.Parts.Select(e => (e.Part, e.Skin)));
        p.ResetPartSkins(body);

        // 적용: 기록 없음, 지금 착장 바뀜(재정의는 착장대로 — 없으면 지움)
        int undo = p.History.UndoCount;
        p.SetPartSkin(bodyPart, body.BaseSkin!);
        Assert.Empty(p.ApplyOutfit(knight));
        Assert.Equal(undo, p.History.UndoCount);
        Assert.Equal((zombie, iron), (body.ActiveSkin, armor.ActiveSkin));
        Assert.Null(bodyPart.SkinOverride);
        Assert.Equal((Green, Blue), (p.Flatten(0)[0, 0], p.Flatten(0)[1, 0]));

        // 합성만(지금 착장은 그대로)
        p.ApplyOutfit(mixed);
        Assert.Same(zombie, bodyPart.SkinOverride);
        var flat = p.FlattenOutfit(0, knight);
        Assert.Equal((Green, Blue), (flat[0, 0], flat[1, 0]));
        Assert.Same(armor.BaseSkin, armor.ActiveSkin);

        // 이름 바꾸기 추적
        p.RenameSkin(body, zombie, "undead");
        p.RenameLayerLike(armor, "갑옷");
        Assert.Empty(p.MissingOutfitEntries(knight));
        Assert.Equal("undead", knight.Slots.Single(s => s.Group == body).Skin.Name);

        // 지운 스킨은 건너뛰고 알림, 실행취소하면 다시 쓰임
        p.DeleteSkin(armor, iron);
        var missing = p.ApplyOutfit(knight);
        Assert.Single(missing);
        Assert.Contains("iron", missing[0]);
        Assert.Same(zombie, body.ActiveSkin);
        Assert.Same(armor.BaseSkin, armor.ActiveSkin);
        p.Undo();
        Assert.Empty(p.ApplyOutfit(knight));
        Assert.Same(iron, armor.ActiveSkin);

        // 착장 목록 편집은 실행취소된다(지운 착장은 내보내기 목록에서도 빠짐)
        p.Edit("착장 내보내기", ProjectChange.Settings, () => p.Export.Common.Outfits = [knight, mixed]);
        p.RenameOutfit(knight, "기사");
        Assert.Throws<ArgumentException>(() => p.RenameOutfit(mixed, "기사"));
        p.DeleteOutfit(knight);
        Assert.Equal([mixed], p.Export.Common.Outfits);
        p.Undo();
        Assert.Equal(["기사", "좀비 기사 2"], p.Outfits.Select(o => o.Name));
        Assert.Equal([knight, mixed], p.Export.Common.Outfits);
        p.Undo();
        Assert.Equal("좀비 기사", knight.Name);
        p.SetActiveSkin(body, body.BaseSkin!);
        p.OverwriteOutfit(knight);
        Assert.Same(body.BaseSkin, knight.Slots.Single(s => s.Group == body).Skin);
        p.Undo();
        Assert.Same(zombie, knight.Slots.Single(s => s.Group == body).Skin);
        p.MoveOutfit(1, 0);
        Assert.Equal([mixed, knight], p.Outfits);
    }

    [Fact]
    public void Outfit_LockedLayersChangedBy_ListsLockedGroupsAndPartsItWouldChange()
    {
        var (p, body, armor, bodyPart, plate, zombie, iron) = Rig();
        p.SetActiveSkin(body, zombie);
        p.SetActiveSkin(armor, iron);
        p.SetPartSkin(plate, armor.BaseSkin!);
        var knight = p.SaveOutfit("기사"); // body = zombie, armor = iron, Plate 재정의 = 없음
        p.SetActiveSkin(body, body.BaseSkin!);
        p.SetActiveSkin(armor, armor.BaseSkin!);
        p.SetPartSkin(plate, null);

        // 잠긴 레이어가 없으면 비어 있다
        Assert.Empty(p.LockedLayersChangedBy(knight));

        // 잠긴 그룹: 스킨이 바뀔 그룹과 재정의가 바뀔 부위
        p.SetLayerProperties(p.IndexOf(armor), locked: true);
        Assert.Equal([armor, plate], p.LockedLayersChangedBy(knight));

        // 부위만 잠김: 재정의가 바뀔 때만
        p.SetLayerProperties(p.IndexOf(armor), locked: false);
        p.SetLayerProperties(p.IndexOf(plate), locked: true);
        Assert.Equal([plate], p.LockedLayersChangedBy(knight));
        p.SetPartSkin(plate, armor.BaseSkin!);
        Assert.Empty(p.LockedLayersChangedBy(knight));

        // 잠긴 그룹이라도 바뀌지 않는 부위는 빠진다
        p.SetLayerProperties(p.IndexOf(body), locked: true);
        Assert.Equal([body], p.LockedLayersChangedBy(knight));
        Assert.DoesNotContain(bodyPart, p.LockedLayersChangedBy(knight));
    }

    [Fact]
    public void FrameSheets_PerOutfitFiles_SameSpriteNames_AndExcludedLayers()
    {
        var (p, body, armor, _, _, zombie, iron) = Rig();
        p.SetActiveSkin(armor, iron);
        var knight = p.SaveOutfit("knight");
        p.SetActiveSkin(body, zombie);
        var undead = p.SaveOutfit("undead");
        p.SetActiveSkin(body, body.BaseSkin!);
        p.SetActiveSkin(armor, armor.BaseSkin!);

        // 고른 스킨 = 착장 프리셋만(그룹 스킨은 모두 체크 해제), 스킨마다 따로 파일 — 예전 "착장마다 따로 파일"
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.ExcludedSkins = [.. body.Skins, .. armor.Skins];
        o.Outfits = [knight, undead];
        o.SplitSkins = true;
        var files = ProjectExport.BuildFrameSheets(p, "hero");
        Assert.Equal(["hero@knight", "hero@undead"], files.Select(f => f.Name));
        Assert.Equal([knight, undead], files.Select(f => f.Variant?.Outfit));
        Assert.All(files, f => Assert.Equal(["hero_0", "hero_1"], f.Sheet.Entries.Select(e => e.Name)));
        Color32 At(SheetFile f, int x) => f.Sheet.Image[f.Sheet.Entries[0].Rect.X + x, f.Sheet.Entries[0].Rect.Y];
        Assert.Equal((Red, Blue), (At(files[0], 0), At(files[0], 1)));
        Assert.Equal((Green, Blue), (At(files[1], 0), At(files[1], 1)));

        // 태그마다 따로 파일과 함께
        o.SplitTags = true;
        p.AddTag("idle", 0, 1);
        Assert.Equal(["hero_idle@knight", "hero_idle@undead"], ProjectExport.BuildFrameSheets(p, "hero").Select(f => f.Name));
        var (frames, _) = ProjectExport.GetOutputPaths(p, "C:/x/hero.chickxel", assumePartContent: true);
        Assert.Equal(["hero_idle@knight.png", "hero_idle@undead.png"], frames.Select(Path.GetFileName));

        // 아무것도 고르지 않으면 지금 착장
        o.Outfits = [];
        Assert.Equal(["hero_idle"], ProjectExport.BuildFrameSheets(p, "hero").Select(f => f.Name));

        // 몸 그룹을 빼면 장비만
        o.SplitTags = false;
        o.Outfits = [knight];
        o.ExcludedLayers = [body];
        var armorOnly = ProjectExport.BuildFrameSheets(p, "hero")[0];
        Assert.Equal((Color32.Transparent, Blue), (At(armorOnly, 0), At(armorOnly, 1)));
    }

    [Fact]
    public void PartsAtlas_SkinFilter_OnlyOwnCelsOfExportedSkins()
    {
        var (p, body, armor, bodyPart, plate, zombie, iron) = Rig();
        string[] Names() => ProjectExport.BuildPartsSheet(p, "x_parts").Entries.Select(e => e.Name).ToArray();
        p.Export.Common.Skins = SkinSource.Selected; // 고른 스킨(모두 체크 = 예전 부위 아틀라스 기본)
        Assert.Equal(["Plate@iron", "Plate_1@iron", "Body", "Body_1", "Body@zombie", "Body_1@zombie"], Names());

        // 스킨 빼기: 스프라이트와 라이브러리 모두
        p.Export.Common.ExcludedSkins = [zombie];
        Assert.Equal(["Plate@iron", "Plate_1@iron", "Body", "Body_1"], Names());

        // 다른 스킨이 그리지 않은 칸은 빈 칸: 첫 스킨을 빼면 첫 스킨 스프라이트는 하나도 남지 않고, 대신 굽지도 않는다
        var hair = p.AddGroup("hair");
        var bangs = p.AddPart(hair, "Bangs", 1, 1);
        Paint(p, bangs, 0, Red);
        Paint(p, bangs, 1, Red);
        var curly = p.AddSkin(hair, "curly");
        Paint(p, bangs, 1, Green); // curly는 프레임 1만 직접 그림, 프레임 0은 빈 칸
        p.Export.Common.ExcludedSkins = [hair.BaseSkin!];
        var names = Names();
        Assert.Equal(["Bangs_1@curly"], names.Where(n => n.StartsWith("Bangs")));
        var sheet = ProjectExport.BuildPartsSheet(p, "x_parts");
        var e1 = sheet.Entries.Single(e => e.Name == "Bangs_1@curly");
        Assert.Equal(Green, sheet.Image[e1.Rect.X, e1.Rect.Y]);
        Assert.Same(curly, e1.Skin);

        // 모두 내보내면 첫 스킨은 그린 칸 모두, 다른 스킨은 직접 그린 칸만
        p.Export.Common.ExcludedSkins = [];
        Assert.Equal(["Bangs", "Bangs_1", "Bangs_1@curly"], Names().Where(n => n.StartsWith("Bangs")));
    }

    [Fact]
    public void SpriteLibrary_OnlyExportedSkins_AndRecordsOutfitFiles()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.chickxel");
        var (p, body, armor, _, _, zombie, iron) = Rig();
        p.Export.WriteSpriteLibrary = true;
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.ExcludedSkins = [zombie, armor.BaseSkin!];
        o.SplitSkins = true;
        p.SetActiveSkin(armor, iron);
        var knight = p.SaveOutfit("knight");
        p.SetActiveSkin(armor, armor.BaseSkin!);
        o.Outfits = [knight];

        // 프레임 시트: 위 그룹부터 체크한 스킨(armor iron → body 기본 = 원래 이름) → 착장
        var result = ProjectIO.SaveWithExport(p, path).Export!;
        Assert.Equal(["hero@iron.png", "hero.png", "hero@knight.png"], result.Frames.Select(r => Path.GetFileName(r.ImagePath)));
        Assert.Equal(["hero_parts_armor@iron.spriteLib", "hero_parts_body@기본.spriteLib"], result.Libraries.Select(r => Path.GetFileName(r.ImagePath)));
        Assert.Contains(dir.File("hero@knight.png"), p.ExportedFiles!);
        Assert.Contains(dir.File("hero@knight.png"), ProjectIO.GetRegisteredExportPaths(path));

        // 착장 파일도 프로젝트 소유 결과물(파일 목록 숨김·이름 바꾸기·삭제에 딸림)
        Assert.Contains(dir.File("hero@knight.png"), Chickxel.Core.IO.ProjectIO.GetOwnedDefaultExportPaths(path));

        // 착장을 내보내기에서 빼면 옛 착장 파일은 정리 대상
        o.Outfits = [];
        var again = ProjectIO.SaveWithExport(p, path).Export!;
        Assert.Contains(dir.File("hero@knight.png"), again.Stale);
    }

    [Fact]
    public void SaveLoad_RoundTripsOutfitsAndExportSelections()
    {
        using var dir = new TempDir();
        var (p, body, armor, bodyPart, _, zombie, iron) = Rig();
        p.SetActiveSkin(armor, iron);
        p.SetPartSkin(bodyPart, zombie);
        var knight = p.SaveOutfit("knight");
        p.SaveOutfit("plain");
        var o = p.Export.Common;
        o.Skins = SkinSource.Selected;
        o.Outfits = [knight];
        o.ExcludedLayers = [body];
        o.ExcludedSkins = [armor.BaseSkin!];
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
        Assert.Equal(["knight", "plain"], back.Outfits.Select(o => o.Name));
        var bk = back.Outfits[0];
        Assert.Equal([("armor", "iron"), ("body", "기본")], bk.Slots.Select(s => (s.Group.Name, s.Skin.Name)));
        Assert.Equal([("Body", "zombie")], bk.Parts.Select(e => (e.Part.Name, e.Skin.Name)));
        Assert.Same(bArmor, bk.Slots[0].Group);
        Assert.Same(bPart, bk.Parts[0].Part);
        var e = back.Export.Common;
        Assert.Equal(SkinSource.Selected, e.Skins);
        Assert.Equal([bk], e.Outfits);
        Assert.Equal([bBody], e.ExcludedLayers);
        Assert.Equal([bArmor.BaseSkin!], e.ExcludedSkins);
        Assert.Equal(new GridOrder([GridKey.Skin, GridKey.Part, GridKey.Frame], 1), e.Order);
        var po = back.Export.PartsOverride!;
        Assert.Equal((SheetLayout.Packed, SkinSource.Selected), (po.Layout, po.Skins));
        Assert.Equal([bBody.Skins.Single(s => s.Name == "zombie")], po.ExcludedSkins);
        Assert.Equal([bArmor], po.ExcludedLayers);

        // 예전 파일(설정 없음) = 기본값
        var fresh = new SpriteProject(2, 2);
        ProjectIO.Save(fresh, dir.File("f.chickxel"));
        var fe = ProjectIO.Load(dir.File("f.chickxel"));
        Assert.Empty(fe.Outfits);
        Assert.Null(fe.Export.PartsOverride);
        var c = fe.Export.Common;
        Assert.Equal(SkinSource.Current, c.Skins);
        Assert.Empty(c.Outfits);
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

internal static class OutfitTestExtensions
{
    /// <summary>테스트용: 레이어 이름 바꾸기.</summary>
    public static void RenameLayerLike(this SpriteProject p, Layer layer, string name) => p.SetLayerProperties(p.IndexOf(layer), name: name);
}
