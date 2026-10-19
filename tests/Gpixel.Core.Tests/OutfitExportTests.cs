using System.Drawing;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

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
        var (frames, _) = ProjectExport.GetOutputPaths(p, "C:/x/hero.gpixel", assumePartContent: true);
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
    public void PartsAtlas_SkinFilter_KeepsNeededBaseSprites_OrBakes()
    {
        var (p, body, armor, bodyPart, plate, zombie, iron) = Rig();
        string[] Names() => ProjectExport.BuildPartsSheet(p, "x_parts").Entries.Select(e => e.Name).ToArray();
        p.Export.Common.Skins = SkinSource.Selected; // 고른 스킨(모두 체크 = 예전 부위 아틀라스 기본)
        Assert.Equal(["Plate@iron", "Plate_1@iron", "Body", "Body_1", "Body@zombie", "Body_1@zombie"], Names());

        // 스킨 빼기: 스프라이트와 라이브러리 모두
        p.Export.Common.ExcludedSkins = [zombie];
        Assert.Equal(["Plate@iron", "Plate_1@iron", "Body", "Body_1"], Names());

        // 첫 스킨을 빼도 남은 스킨이 물려받는 칸의 첫 스킨 스프라이트는 남는다
        var hair = p.AddGroup("hair");
        var bangs = p.AddPart(hair, "Bangs", 1, 1);
        Paint(p, bangs, 0, Red);
        Paint(p, bangs, 1, Red);
        var curly = p.AddSkin(hair, "curly");
        Paint(p, bangs, 1, Green); // curly는 프레임 1만 직접 그림, 프레임 0은 기본을 물려받음
        p.Export.Common.ExcludedSkins = [hair.BaseSkin!];
        Assert.Equal(["Bangs", "Bangs_1@curly"], Names().Take(2));

        // 구워 넣기: 물려받는 칸도 그 스킨 스프라이트로, 뺀 첫 스킨 스프라이트는 없음
        p.Export.Common.BakeInheritedSkins = true;
        var names = Names();
        Assert.Equal(["Bangs@curly", "Bangs_1@curly"], names.Take(2));
        Assert.DoesNotContain("Bangs", names);
        var baked = ProjectExport.BuildPartsSheet(p, "x_parts");
        var e0 = baked.Entries.Single(e => e.Name == "Bangs@curly");
        Assert.Equal(Red, baked.Image[e0.Rect.X, e0.Rect.Y]); // 물려받은 그림
        Assert.Same(curly, e0.Skin);

        // 구워 넣으면 첫 스킨도 내보낼 때 다른 스킨이 모든 칸을 가진다
        p.Export.Common.ExcludedSkins = [];
        Assert.Contains("Body@zombie", Names());
        Assert.Contains("Plate@iron", Names());
        Assert.Contains("Bangs@curly", Names());
        Assert.Contains("Bangs", Names());
    }

    [Fact]
    public void SpriteLibrary_OnlyExportedSkins_AndRecordsOutfitFiles()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.gpixel");
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
        Assert.Contains(dir.File("hero@knight.png"), Gpixel.Core.IO.ProjectIO.GetOwnedDefaultExportPaths(path));

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
        o.BakeInheritedSkins = true;
        o.Order = new GridOrder([GridKey.Skin, GridKey.Part, GridKey.Frame], 1);
        p.Export.PartsOverride = new SheetOptions { Layout = SheetLayout.Packed, Skins = SkinSource.Selected, ExcludedSkins = [zombie], ExcludedLayers = [armor] };
        var path = dir.File("o.gpixel");
        ProjectIO.Save(p, path);

        var back = ProjectIO.Load(path);
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
        Assert.True(e.BakeInheritedSkins);
        Assert.Equal(new GridOrder([GridKey.Skin, GridKey.Part, GridKey.Frame], 1), e.Order);
        var po = back.Export.PartsOverride!;
        Assert.Equal((SheetLayout.Packed, SkinSource.Selected), (po.Layout, po.Skins));
        Assert.Equal([bBody.Skins.Single(s => s.Name == "zombie")], po.ExcludedSkins);
        Assert.Equal([bArmor], po.ExcludedLayers);

        // 예전 파일(설정 없음) = 기본값
        var fresh = new SpriteProject(2, 2);
        ProjectIO.Save(fresh, dir.File("f.gpixel"));
        var fe = ProjectIO.Load(dir.File("f.gpixel"));
        Assert.Empty(fe.Outfits);
        Assert.Null(fe.Export.PartsOverride);
        var c = fe.Export.Common;
        Assert.Equal(SkinSource.Current, c.Skins);
        Assert.Empty(c.Outfits);
        Assert.Empty(c.ExcludedLayers);
        Assert.Empty(c.ExcludedSkins);
        Assert.False(c.BakeInheritedSkins);
        Assert.Equal(GridOrder.ByPart, c.Order);
    }
}

internal static class OutfitTestExtensions
{
    /// <summary>테스트용: 레이어 이름 바꾸기.</summary>
    public static void RenameLayerLike(this SpriteProject p, Layer layer, string name) => p.SetLayerProperties(p.IndexOf(layer), name: name);
}
