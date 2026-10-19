using System.Drawing;
using System.IO.Compression;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

/// <summary>부위 전용(개별) 스킨: 목록·입히기·합성·물려받기, 실행취소(추가·이름·삭제·순서·올리기·내리기), 부위·그룹 복제·삭제, 저장 왕복, 내보내기.</summary>
public class PartSkinTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);

    private static void Paint(SpriteProject p, Layer layer, int frame, int x, int y, Color32 c)
    {
        p.BeginCelStroke(p.IndexOf(layer), frame).Set(x, y, c);
        Assert.True(p.CommitCelStroke("점"));
    }

    /// <summary>4×4, 그룹 "body"(Arm 2×2 @ (2,2), 위에 Head 2×2 @ (0,0)), 첫 스킨 Head·Arm (0,0)에 빨강.</summary>
    private static (SpriteProject P, Layer Group, Layer Head, Layer Arm) Body()
    {
        var p = new SpriteProject(4, 4);
        var g = p.AddGroup("body");
        var arm = p.AddPart(g, "Arm", 2, 2, new Point(2, 2));
        var head = p.AddPart(g, "Head", 2, 2, new Point(0, 0));
        Paint(p, head, 0, 0, 0, Red);
        Paint(p, arm, 0, 0, 0, Red);
        return (p, g, head, arm);
    }

    // ───────────── 모델 ─────────────

    [Fact]
    public void AddPartSkin_ListsOnlyForOwner_WornByOverride_CompositesOwnerOnly()
    {
        var (p, g, head, arm) = Body();
        var baseSkin = g.BaseSkin!;
        int undo = p.History.UndoCount;
        var hat = p.AddPartSkin(head, "모자");
        Assert.Equal(undo + 1, p.History.UndoCount);

        Assert.True(hat.IsPartOnly);
        Assert.Same(head, hat.Owner);
        Assert.Equal([baseSkin], g.Skins);
        Assert.Equal([baseSkin, hat], g.AllSkins);
        Assert.Equal([baseSkin], g.SkinChoices);
        Assert.Equal([baseSkin, hat], head.SkinChoices);
        Assert.Equal([hat], head.OwnSkins);
        Assert.Equal([baseSkin], arm.SkinChoices);
        Assert.Empty(arm.OwnSkins);
        Assert.Empty(g.OwnSkins);

        // 만들면 그 부위에 입혀진다(재정의), 그룹 지금 스킨은 그대로
        Assert.Same(hat, head.SkinOverride);
        Assert.Same(hat, head.ShownSkin);
        Assert.Same(baseSkin, g.ActiveSkin);
        Assert.Same(baseSkin, arm.ShownSkin);

        // 빈 칸은 첫 스킨을 빌린다
        Assert.True(p.IsCelInherited(head, p.Frames[0]));
        Assert.Equal(Red, p.Flatten(0)[0, 0]);

        // 그리면 그 부위 전용 스킨 셀에(복사해 시작)
        Paint(p, head, 0, 1, 1, Blue);
        var own = p.GetOwnCel(head, p.Frames[0], hat)!;
        Assert.Equal((Red, Blue), (own[0, 0], own[1, 1]));
        Assert.Equal(Color32.Transparent, p.GetOwnCel(head, p.Frames[0], baseSkin)![1, 1]);
        Assert.Equal(Blue, p.Flatten(0)[1, 1]);
        Assert.Equal((Red, Red), (p.Flatten(0)[0, 0], p.Flatten(0)[2, 2]));

        // 재정의를 지우면 그룹 스킨
        p.SetPartSkin(head, null);
        Assert.Equal(Color32.Transparent, p.Flatten(0)[1, 1]);
        p.SetPartSkin(head, hat);
        Assert.Equal(Blue, p.Flatten(0)[1, 1]);

        // 그룹 전체에·다른 부위에는 입힐 수 없다
        Assert.Throws<ArgumentException>(() => p.SetActiveSkin(g, hat));
        Assert.Throws<ArgumentException>(() => p.SetPartSkin(arm, hat));
        Assert.Throws<ArgumentException>(() => p.AddPartSkin(arm, null, hat)); // 다른 부위 전용 스킨은 복사 원본이 될 수 없다
        Assert.Throws<ArgumentException>(() => p.AddPartSkin(g));

        // 이름은 그룹 전체(전체 스킨 포함)에서 겹치지 않는다
        var knight = p.AddSkin(g, "knight");
        Assert.Throws<ArgumentException>(() => p.RenameSkin(g, hat, "knight"));
        Assert.Throws<ArgumentException>(() => p.RenameSkin(g, knight, "모자"));
        Assert.Equal("모자 2", p.UniqueSkinName(g, "모자"));
        Assert.Equal("모자 2", p.AddPartSkin(arm, "모자").Name);
        Assert.Equal("Arm 스킨", p.AddPartSkin(arm).Name);
        Assert.Equal("knight 2", p.AddPartSkin(arm, "knight").Name);
        // 목록 순서: 전체 스킨이 앞
        Assert.Equal(["기본", "knight", "모자", "모자 2", "Arm 스킨", "knight 2"], g.AllSkins.Select(s => s.Name));
        Assert.Equal(["기본", "knight"], g.Skins.Select(s => s.Name));
        Assert.Equal(["기본", "knight", "모자"], head.SkinChoices.Select(s => s.Name));
    }

    [Fact]
    public void AddPartSkin_CopyFromCopiesOwnerCels_AndUndoRedo()
    {
        var (p, g, head, arm) = Body();
        var knight = p.AddSkin(g, "knight");
        Paint(p, head, 0, 0, 0, Blue);
        Paint(p, arm, 0, 0, 0, Blue);

        var copy = p.AddPartSkin(head, null, knight);
        Assert.Equal("knight 복사", copy.Name);
        var cel = p.GetOwnCel(head, p.Frames[0], copy)!;
        Assert.Equal(Blue, cel[0, 0]);
        Assert.NotSame(p.GetOwnCel(head, p.Frames[0], knight), cel);
        Assert.Null(p.GetOwnCel(arm, p.Frames[0], copy)); // 주인 부위만
        Assert.Same(copy, head.SkinOverride);

        // 첫 스킨에서 복사: 그 부위 셀 사본
        var fromBase = p.AddPartSkin(head, "b", g.BaseSkin);
        Assert.Equal(Red, p.GetOwnCel(head, p.Frames[0], fromBase)![0, 0]);

        // 실행취소: 목록·셀이 돌아가고 재정의는 그룹을 따름
        Assert.True(p.Undo());
        Assert.DoesNotContain(fromBase, g.AllSkins);
        Assert.True(p.Undo());
        Assert.DoesNotContain(copy, g.AllSkins);
        Assert.Empty(head.OwnSkins);
        Assert.Null(head.SkinOverride);
        Assert.Equal(Blue, p.Flatten(0)[0, 0]); // 그룹 스킨 knight
        Assert.True(p.Redo());
        Assert.Equal([copy], head.OwnSkins);
        Assert.Same(head, copy.Owner);
        Assert.Equal(Blue, p.GetOwnCel(head, p.Frames[0], copy)![0, 0]);
        Assert.Null(head.SkinOverride); // 재정의(보기 상태)는 마지막으로 만든 b를 가리킨 채 — b가 돌아와야 다시 보임
        Assert.True(p.Redo());
        Assert.Same(fromBase, head.SkinOverride);
    }

    [Fact]
    public void PartSkin_RenameDeleteMove_UndoRedo()
    {
        var (p, g, head, arm) = Body();
        var knight = p.AddSkin(g, "knight");
        var a = p.AddPartSkin(head, "a");
        Paint(p, head, 0, 1, 0, Green);
        var b = p.AddPartSkin(head, "b");
        var c = p.AddPartSkin(head, "c");
        var armOnly = p.AddPartSkin(arm, "팔");
        Assert.Equal([a, b, c], head.OwnSkins);

        // 이름
        p.RenameSkin(g, a, "모자");
        Assert.Equal("모자", a.Name);
        p.Undo();
        Assert.Equal("a", a.Name);
        p.Redo();
        Assert.Equal("모자", a.Name);

        // 순서: 같은 부위 전용 스킨끼리만
        int undo = p.History.UndoCount;
        p.MoveSkin(g, c, -1);
        Assert.Equal([a, c, b], head.OwnSkins);
        Assert.Equal([armOnly], arm.OwnSkins);
        Assert.Equal([g.BaseSkin!, knight], g.Skins);
        p.MoveSkin(g, a, -1); // 맨 앞이면 그대로(기록 없음)
        Assert.Equal(undo + 1, p.History.UndoCount);
        p.MoveSkin(g, b, 5); // 끝에서 더 뒤로도 그대로
        Assert.Equal(undo + 1, p.History.UndoCount);
        p.MoveSkin(g, a, 1);
        Assert.Equal([c, a, b], head.OwnSkins);
        p.Undo();
        p.Undo();
        Assert.Equal([a, b, c], head.OwnSkins);
        p.Redo();
        Assert.Equal([a, c, b], head.OwnSkins);
        // 전체 스킨은 전체 스킨끼리(부위 전용 스킨 뒤로 가지 않음)
        p.MoveSkin(g, knight, 10);
        Assert.Equal([g.BaseSkin!, knight], g.Skins);
        p.MoveSkin(g, knight, -1);
        Assert.Equal([knight, g.Skins[1]], g.Skins);
        Assert.Equal(g.AllSkins.Take(2), g.Skins);
        p.Undo();

        // 삭제: 부위 전용 스킨은 언제든(셀도), 재정의는 그룹을 따르고, 실행취소하면 돌아온다
        p.SetPartSkin(head, a);
        Assert.Equal(Green, p.Flatten(0)[1, 0]);
        p.DeleteSkin(g, a);
        Assert.DoesNotContain(a, g.AllSkins);
        Assert.Null(head.SkinOverride);
        Assert.Null(p.GetOwnCel(head, p.Frames[0], a));
        Assert.Equal(Color32.Transparent, p.Flatten(0)[1, 0]);
        p.Undo();
        Assert.Contains(a, head.OwnSkins);
        Assert.Same(a, head.SkinOverride);
        Assert.Equal(Green, p.Flatten(0)[1, 0]);
        p.DeleteSkin(g, armOnly);
        p.DeleteSkin(g, knight); // 부위 전용 스킨이 남아도 전체 스킨이 하나면 그 하나는 못 지운다
        Assert.Throws<InvalidOperationException>(() => p.DeleteSkin(g, g.BaseSkin!));
    }

    [Fact]
    public void PromoteDemote_UndoRedo()
    {
        var (p, g, head, arm) = Body();
        var baseSkin = g.BaseSkin!;
        var hat = p.AddPartSkin(head, "모자");
        Paint(p, head, 0, 1, 1, Blue);

        // 올리기: 전체 스킨 끝에, 다른 부위는 빈 칸이라 첫 스킨을 빌림
        var other = p.AddPartSkin(arm, "팔");
        p.PromoteSkin(g, hat);
        Assert.Null(hat.Owner);
        Assert.Equal([baseSkin, hat], g.Skins);
        Assert.Equal([baseSkin, hat, other], g.AllSkins);
        Assert.Contains(hat, arm.SkinChoices);
        Assert.Empty(head.OwnSkins);
        p.SetActiveSkin(g, hat);
        Assert.Equal((Blue, Red), (p.Flatten(0)[1, 1], p.Flatten(0)[2, 2]));
        p.PromoteSkin(g, hat); // 이미 전체 스킨이면 아무 일 없음
        p.SetActiveSkin(g, baseSkin);
        p.SetPartSkin(head, hat);

        p.Undo();
        Assert.Same(head, hat.Owner);
        Assert.Equal([baseSkin], g.Skins);
        Assert.Equal([baseSkin, hat, other], g.AllSkins);
        Assert.Same(hat, head.SkinOverride);
        p.Redo();
        Assert.Null(hat.Owner);

        // 내리기: 다른 부위의 그 스킨 셀은 지우고, 그룹 지금 스킨이면 첫 스킨으로, 다른 부위 재정의는 그룹을 따름
        p.SetPartSkin(arm, hat);
        Paint(p, arm, 0, 0, 1, Green);
        Assert.NotNull(p.GetOwnCel(arm, p.Frames[0], hat));
        p.SetActiveSkin(g, hat);
        p.DemoteSkin(g, hat, head);
        Assert.Same(head, hat.Owner);
        Assert.Equal([baseSkin], g.Skins);
        Assert.Equal([baseSkin, other, hat], g.AllSkins);
        Assert.Null(p.GetOwnCel(arm, p.Frames[0], hat));
        Assert.NotNull(p.GetOwnCel(head, p.Frames[0], hat));
        Assert.Same(baseSkin, g.ActiveSkin);
        Assert.Null(arm.SkinOverride);
        Assert.DoesNotContain(hat, arm.SkinChoices);

        p.Undo();
        Assert.Null(hat.Owner);
        Assert.Equal([baseSkin, hat], g.Skins);
        Assert.Equal(Green, p.GetOwnCel(arm, p.Frames[0], hat)![0, 1]);
        Assert.Same(hat, arm.SkinOverride); // 셀·목록이 돌아오면 재정의도 다시 유효
        p.Redo();
        Assert.Same(head, hat.Owner);
        Assert.Null(p.GetOwnCel(arm, p.Frames[0], hat));

        // 첫 스킨·마지막 전체 스킨은 내릴 수 없다, 다른 그룹의 부위는 안 됨
        Assert.Throws<InvalidOperationException>(() => p.DemoteSkin(g, baseSkin, head));
        var g2 = p.AddGroup("armor");
        var plate = p.AddPart(g2, "Plate", 1, 1);
        var iron = p.AddSkin(g2, "iron");
        Assert.Throws<ArgumentException>(() => p.DemoteSkin(g2, iron, head));
        p.DemoteSkin(g2, iron, plate);
        Assert.Throws<InvalidOperationException>(() => p.DemoteSkin(g2, g2.BaseSkin!, plate));
    }

    [Fact]
    public void DuplicateAndDeleteLayers_CarryPartSkins()
    {
        var (p, g, head, arm) = Body();
        var hat = p.AddPartSkin(head, "모자");
        Paint(p, head, 0, 1, 1, Blue);

        // 부위 복제: 부위 전용 스킨을 새 부위 전용으로 복사(셀·재정의까지)
        var dupHead = p.DuplicateLayer(p.IndexOf(head));
        var dupHat = Assert.Single(dupHead.OwnSkins);
        Assert.NotSame(hat, dupHat);
        Assert.Same(dupHead, dupHat.Owner);
        Assert.Equal("모자 복사", dupHat.Name);
        Assert.Equal([hat], head.OwnSkins);
        Assert.Same(dupHat, dupHead.SkinOverride);
        Assert.Equal(Blue, p.GetOwnCel(dupHead, p.Frames[0], dupHat)![1, 1]);
        Assert.DoesNotContain(dupHat, head.SkinChoices);
        p.Undo();
        Assert.Equal([g.BaseSkin!, hat], g.AllSkins);

        // 그룹 복제: 부위 전용 스킨의 주인도 새 부위로
        var dup = p.DuplicateLayer(p.IndexOf(g));
        var newHead = p.ChildrenOf(dup).Single(l => l.Name.StartsWith("Head"));
        var newArm = p.ChildrenOf(dup).Single(l => l.Name.StartsWith("Arm"));
        var newHat = Assert.Single(newHead.OwnSkins);
        Assert.Equal("모자", newHat.Name);
        Assert.Same(newHead, newHat.Owner);
        Assert.NotSame(hat, newHat);
        Assert.Same(newHat, newHead.SkinOverride);
        Assert.Empty(newArm.OwnSkins);
        Assert.Equal([hat], head.OwnSkins);
        Assert.Equal(Blue, p.GetOwnCel(newHead, p.Frames[0], newHat)![1, 1]);
        p.Undo();

        // 부위 삭제: 그 부위 전용 스킨도 지우고, 실행취소하면 돌아온다
        p.DeleteLayer(p.IndexOf(head));
        Assert.Equal([g.BaseSkin!], g.AllSkins);
        p.Undo();
        Assert.Equal([g.BaseSkin!, hat], g.AllSkins);
        Assert.Same(head, hat.Owner);
        Assert.Same(hat, head.SkinOverride);
        Assert.Equal(Blue, p.Flatten(0)[1, 1]);
        Assert.Empty(arm.OwnSkins);
    }

    // ───────────── 저장·읽기 ─────────────

    [Fact]
    public void SaveLoad_RoundTripsPartSkins_OverridesAndOutfits()
    {
        using var dir = new TempDir();
        var (p, g, head, arm) = Body();
        var knight = p.AddSkin(g, "knight");
        Paint(p, arm, 0, 1, 1, Green);
        p.SetActiveSkin(g, g.BaseSkin!);
        var hat = p.AddPartSkin(head, "모자");
        Paint(p, head, 0, 1, 1, Blue);
        var sleeve = p.AddPartSkin(arm, "소매");
        p.SetPartSkin(arm, knight);
        var outfit = p.SaveOutfit("모자 착장");
        Assert.Equal([(head, hat), (arm, knight)], outfit.Parts.Select(e => (e.Part, e.Skin)).OrderBy(e => e.Part == arm));
        p.SetPartSkin(arm, sleeve);

        var path = dir.File("hero.gpixel");
        ProjectIO.Save(p, path);
        Assert.Equal(["기본", "knight", "모자", "소매"], g.AllSkins.Select(s => s.Name));
        using (var zip = ZipFile.OpenRead(path))
        {
            Assert.Contains($"cels/L{p.IndexOf(head)}_F0_S2.png", zip.Entries.Select(e => e.FullName));
            using var r = new StreamReader(zip.GetEntry("project.json")!.Open());
            Assert.Contains("\"skinOwners\"", r.ReadToEnd());
        }

        var warnings = new List<string>();
        var back = ProjectIO.Load(path, warnings);
        Assert.Empty(warnings);
        var bg = back.Layers.Single(l => l.IsGroup);
        var bHead = back.Layers.Single(l => l.Name == "Head");
        var bArm = back.Layers.Single(l => l.Name == "Arm");
        Assert.Equal(["기본", "knight"], bg.Skins.Select(s => s.Name));
        Assert.Equal(["기본", "knight", "모자", "소매"], bg.AllSkins.Select(s => s.Name));
        var bHat = Assert.Single(bHead.OwnSkins);
        Assert.Equal("모자", bHat.Name);
        Assert.Same(bHead, bHat.Owner);
        Assert.Equal("소매", Assert.Single(bArm.OwnSkins).Name);
        Assert.Same(bHat, bHead.SkinOverride);
        Assert.Same(bArm.OwnSkins[0], bArm.SkinOverride);
        Assert.Equal(Blue, back.GetOwnCel(bHead, back.Frames[0], bHat)![1, 1]);
        Assert.True(p.Flatten(0).ContentEquals(back.Flatten(0)));

        var bOutfit = Assert.Single(back.Outfits);
        Assert.Contains(bOutfit.Parts, e => ReferenceEquals(e.Part, bHead) && ReferenceEquals(e.Skin, bHat));
        Assert.Contains(bOutfit.Parts, e => ReferenceEquals(e.Part, bArm) && e.Skin.Name == "knight");
        back.ApplyOutfit(bOutfit);
        Assert.Same(bHat, bHead.SkinOverride);
        Assert.Equal("knight", bArm.SkinOverride!.Name);
    }

    [Fact]
    public void SaveLoad_FilesWithoutPartSkins_HaveNone()
    {
        using var dir = new TempDir();
        var (p, g, _, _) = Body();
        p.AddSkin(g, "knight");
        var path = dir.File("plain.gpixel");
        ProjectIO.Save(p, path);
        using (var zip = ZipFile.OpenRead(path))
        using (var r = new StreamReader(zip.GetEntry("project.json")!.Open()))
            Assert.DoesNotContain("skinOwners", r.ReadToEnd());
        var back = ProjectIO.Load(path);
        var bg = back.Layers.Single(l => l.IsGroup);
        Assert.Equal(["기본", "knight"], bg.AllSkins.Select(s => s.Name));
        Assert.All(bg.AllSkins, s => Assert.False(s.IsPartOnly));
        Assert.All(back.ChildrenOf(bg), part => Assert.Empty(part.OwnSkins));

        // 예전(버전 2) 파일
        var old = new MemoryStream();
        using (var zip = new ZipArchive(old, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("project.json").Open()))
            w.Write("""
                { "version": 2, "width": 4, "height": 4, "frames": [ {} ],
                  "layers": [ { "name": "Head", "kind": "Part", "parent": 1, "partWidth": 2, "partHeight": 2 },
                              { "name": "몸", "kind": "Group" } ] }
                """);
        old.Position = 0;
        var migrated = ProjectIO.Load(old);
        Assert.All(migrated.Layers[1].AllSkins, s => Assert.Null(s.Owner));
        Assert.Empty(migrated.Layers[0].OwnSkins);
    }

    // ───────────── 내보내기 ─────────────

    /// <summary><see cref="Body"/> + 스킨 knight(Head (1,1) 파랑), Head 전용 "모자"(Head (1,0) 초록). 지금 착장 = 기본(재정의 없음). 부위 아틀라스 켬, 프레임 번호 이름, 고른 스킨.</summary>
    private static (SpriteProject P, Layer Group, Layer Head, Layer Arm, Skin Knight, Skin Hat) Rig()
    {
        var (p, g, head, arm) = Body();
        var knight = p.AddSkin(g, "knight");
        Paint(p, head, 0, 1, 1, Blue);
        p.SetActiveSkin(g, g.BaseSkin!);
        var hat = p.AddPartSkin(head, "모자");
        Paint(p, head, 0, 1, 0, Green);
        p.SetPartSkin(head, null);
        p.Export.ExportParts = true;
        p.Export.Common.Naming = SpriteNaming.FrameNumber;
        p.Export.Common.Skins = SkinSource.Selected;
        p.Export.Common.Spacing = 0;
        return (p, g, head, arm, knight, hat);
    }

    [Fact]
    public void PartsAtlas_PartSkinOnlyForOwner()
    {
        var (p, g, head, _, knight, hat) = Rig();
        var entries = ProjectExport.BuildPartsSheet(p, "x_parts").Entries;
        Assert.Equal(["Head", "Head@knight", "Head@모자", "Arm"], entries.Select(e => e.Name));
        var e = entries.Single(x => x.Name == "Head@모자");
        Assert.Same(hat, e.Skin);
        Assert.Same(head, e.Part);
        var sheet = ProjectExport.BuildPartsSheet(p, "x_parts");
        Assert.Equal((Red, Green), (sheet.Image[e.Rect.X, e.Rect.Y], sheet.Image[e.Rect.X + 1, e.Rect.Y]));

        p.Export.Common.ExcludedSkins = [hat];
        Assert.Equal(["Head", "Head@knight", "Arm"], ProjectExport.BuildPartsSheet(p, "x_parts").Entries.Select(x => x.Name));
        p.Export.Common.ExcludedSkins = [knight];
        Assert.Equal(["Head", "Head@모자", "Arm"], ProjectExport.BuildPartsSheet(p, "x_parts").Entries.Select(x => x.Name));
        _ = g;
    }

    [Fact]
    public void FrameSheet_SelectedIncludesPartSkin_OnlyOwnerChanges()
    {
        var (p, g, head, _, knight, hat) = Rig();
        p.SetActiveSkin(g, knight); // 지금 착장 = knight: 부위 전용 항목은 그 부위만 바꾸고 나머지는 지금 착장
        var variants = ProjectExport.FrameVariants(p);
        Assert.Equal([null, "knight", "모자"], variants.Select(v => v.Suffix));
        Assert.Same(hat, variants[2].Skin);

        p.Export.Common.SplitSkins = true;
        var files = ProjectExport.BuildFrameSheets(p, "hero");
        Assert.Equal(["hero", "hero@knight", "hero@모자"], files.Select(f => f.Name));
        Color32 At(SheetFile f, int x, int y) => f.Sheet.Image[f.Sheet.Entries[0].Rect.X + x, f.Sheet.Entries[0].Rect.Y + y];
        // 모자: Head는 모자(빨강 + 초록, 그 밖은 첫 스킨을 빌림), Arm은 지금 착장(knight → 첫 스킨 빨강)
        var hatFile = files[2];
        Assert.Equal((Red, Green, Color32.Transparent, Red), (At(hatFile, 0, 0), At(hatFile, 1, 0), At(hatFile, 1, 1), At(hatFile, 2, 2)));
        // knight 항목은 Head (1,1) 파랑, 모자 초록 없음
        Assert.Equal((Blue, Color32.Transparent), (At(files[1], 1, 1), At(files[1], 1, 0)));
        // 내보내기는 지금 착장을 바꾸지 않는다
        Assert.Same(knight, g.ActiveSkin);
        Assert.Null(head.SkinOverride);

        // 한 장에 모두: 칸 이름 뒤에 @모자
        p.Export.Common.SplitSkins = false;
        var names = ProjectExport.BuildFrameSheets(p, "hero").Single().Sheet.Entries.Select(x => x.Name).ToList();
        Assert.Contains("hero_0@모자", names);

        p.Export.Common.ExcludedSkins = [hat];
        Assert.Equal([null, "knight"], ProjectExport.FrameVariants(p).Select(v => v.Suffix));
    }

    [Fact]
    public void SpriteLibrary_PartSkinLibrary_OwnerUsesPartSprites_OthersBase()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.gpixel");
        var (p, _, _, _, knight, hat) = Rig();
        p.Export.ExportFrames = false;
        p.Export.WriteSpriteLibrary = true;
        var result = ProjectIO.SaveWithExport(p, path).Export!;
        Assert.Equal(["hero_parts_body@기본.spriteLib", "hero_parts_body@knight.spriteLib", "hero_parts_body@모자.spriteLib"],
            result.Libraries.Select(r => Path.GetFileName(r.ImagePath)));

        var atlas = Assert.Single(result.Parts);
        var meta = UnityMeta.Read(atlas.ImagePath + ".meta");
        string Id(string name) => meta.Sprites.Single(s => s.Name == name).InternalId.ToString();
        List<YamlNode> Categories(int i) => MiniYaml.Parse(File.ReadAllText(result.Libraries[i].ImagePath))["MonoBehaviour"]!["m_Library"]!.Items.ToList();
        string Ref(YamlNode category) => category["m_OverrideEntries"]!.Items.Single()["m_SpriteOverride"]!["fileID"]!.Text!;

        var hatLib = Categories(2);
        Assert.Equal(["Head", "Arm"], hatLib.Select(c => c["m_Name"]!.Text));
        Assert.Equal(Id("Head@모자"), Ref(hatLib[0]));
        Assert.Equal(Id("Arm"), Ref(hatLib[1]));
        // 범주·라벨은 다른 스킨 라이브러리와 같다
        var baseLib = Categories(0);
        Assert.Equal(baseLib.Select(c => c["m_Name"]!.Text), hatLib.Select(c => c["m_Name"]!.Text));
        Assert.Equal(Id("Head"), Ref(baseLib[0]));
        Assert.Equal(Id("Head@knight"), Ref(Categories(1)[0]));

        // 고른 스킨에서 빼면 스프라이트·라이브러리 모두 없음
        p.Export.Common.ExcludedSkins = [hat];
        var off = ProjectIO.SaveWithExport(p, path).Export!;
        Assert.Equal(["hero_parts_body@기본.spriteLib", "hero_parts_body@knight.spriteLib"], off.Libraries.Select(r => Path.GetFileName(r.ImagePath)));
        Assert.DoesNotContain(UnityMeta.Read(off.Parts.Single().ImagePath + ".meta").Sprites, s => s.Name == "Head@모자");
        Assert.Contains(result.Libraries[2].ImagePath, off.Stale);
        _ = knight;
    }
}
