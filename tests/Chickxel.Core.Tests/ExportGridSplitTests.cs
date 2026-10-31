using System.Drawing;
using System.IO.Compression;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>격자 칸 수(자동·가로·세로), 부위 × 태그 배치, 부위 칸 이름 규칙, 따로 파일 내보내기, 내보내기 기록·옛 결과 정리.</summary>
public class ExportGridSplitTests
{
    private static readonly Color32 Red = new(255, 0, 0);

    /// <summary>프레임 <paramref name="count"/>개, 칸 간격 0(격자 크기 계산을 간단히).</summary>
    private static SpriteProject Frames(int count, int w, int h)
    {
        var p = new SpriteProject(w, h);
        for (int i = 1; i < count; i++) p.AddFrame();
        p.Export.Common.Spacing = 0;
        return p;
    }

    [Fact]
    public void NewProject_Defaults_FrameSheetOnly_AutoGrid_OnePixelSpacing()
    {
        var p = new SpriteProject(4, 4);
        p.AddFrame();
        var g = p.AddGroup();
        p.AddPart(g, "Head", 2, 2);
        var e = p.Export;
        Assert.Equal((true, false, 1, SheetLayout.Grid, SheetGrid.Auto, false, SkinSource.Current),
            (e.ExportFrames, e.ExportParts, e.Common.Spacing, e.Common.Layout, e.Common.Grid, e.Common.SplitTags, e.Common.Skins));
        Assert.Null(e.PartsOverride);
        Assert.Equal(new Size(9, 4), ProjectExport.BuildSheet(p, "x").Image.Size); // 4 + 1 + 4
        // 부위가 있어도 결과는 프레임 시트 하나
        var (frames, parts) = ProjectExport.GetOutputPaths(p, "C:/x/hero.chickxel", assumePartContent: true);
        Assert.Single(frames);
        Assert.Empty(parts);
    }

    private static void Paint(SpriteProject p, Layer layer, int frame)
    {
        p.BeginCelStroke(p.IndexOf(layer), frame).Set(0, 0, Red);
        p.CommitCelStroke("점");
    }

    /// <summary>그룹 하나에 Head(4×2, 위)·Arm(2×3), 프레임 5개: idle 0~1, walk 1~3(프레임 1은 두 태그), 프레임 4는 태그 없음. Arm은 0·2·4만.</summary>
    private static SpriteProject Rig()
    {
        var p = new SpriteProject(8, 8);
        var g = p.AddGroup("rig");
        var arm = p.AddPart(g, "Arm", 2, 3);
        var head = p.AddPart(g, "Head", 4, 2);
        for (int i = 1; i < 5; i++) p.AddFrame();
        for (int f = 0; f < 5; f++) Paint(p, head, f);
        foreach (var f in new[] { 0, 2, 4 }) Paint(p, arm, f);
        p.AddTag("idle", 0, 1);
        p.AddTag("walk", 1, 3);
        p.Export.Common.Spacing = 1;
        p.Export.ExportParts = true; // 새 프로젝트 기본은 프레임 시트만 — 이 테스트들은 둘 다 내보냄
        return p;
    }

    private static Rectangle RectOf(SheetResult s, string name) => s.Entries.Single(e => e.Name == name).Rect;

    /// <summary>부위 아틀라스만 다르게(시트별 다르게): 공통 설정 복사본을 고쳐 넣는다.</summary>
    private static SheetOptions PartsOnly(SpriteProject p, Action<SheetOptions> change)
    {
        var o = p.Export.Common.Clone();
        change(o);
        p.Export.PartsOverride = o;
        return o;
    }

    private static MemoryStream Zip(string json)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry(ProjectIO.ProjectEntryName).Open()))
            w.Write(json);
        ms.Position = 0;
        return ms;
    }

    // ───────────── 격자 칸 수 ─────────────

    [Theory]
    [InlineData(7, 16, 16, 48, 48)] // 3×3
    [InlineData(6, 16, 16, 48, 32)] // 3×2 (2×3과 같으면 가로로 넓게)
    [InlineData(4, 16, 16, 32, 32)] // 2×2
    [InlineData(4, 8, 16, 32, 16)] // 세로로 긴 칸 → 한 줄(4×1)
    [InlineData(4, 16, 8, 32, 16)] // 가로로 긴 칸 → 2×2
    public void AutoGrid_SmallestSideThenAreaThenWider(int count, int w, int h, int sheetW, int sheetH)
    {
        var p = Frames(count, w, h);
        Assert.Equal(SheetGrid.Auto, p.Export.Common.Grid); // 새 프로젝트 기본값
        Assert.Equal(new Size(sheetW, sheetH), ProjectExport.BuildSheet(p, "x").Image.Size);
    }

    [Fact]
    public void ManualColumnsAndRows()
    {
        var p = Frames(7, 4, 4);
        Size Sheet(SheetGrid mode, int n)
        {
            p.Export.Common.Grid = mode;
            p.Export.Common.GridCount = n;
            return ProjectExport.BuildSheet(p, "x").Image.Size;
        }
        Assert.Equal(new Size(12, 12), Sheet(SheetGrid.Columns, 3)); // 3+3+1
        Assert.Equal(new Size(28, 4), Sheet(SheetGrid.Columns, 10)); // 프레임 수보다 많으면 한 줄
        Assert.Equal(new Size(16, 8), Sheet(SheetGrid.Rows, 2)); // 두 줄 이하인 가장 적은 칸 수 = 4
        Assert.Equal(new Size(28, 4), Sheet(SheetGrid.Rows, 1));
        Assert.Equal(new Size(4, 28), Sheet(SheetGrid.Columns, 1));

        // 태그마다 한 줄(부위 → 스킨 → 태그 → 프레임, 줄바꿈 없음 = 칸 수 최대) — 예전 "태그마다 한 줄"
        p.AddTag("a", 0, 4);
        p.Export.Common.Order = GridOrder.ByPartTag;
        Assert.Equal(new Size(20, 8), Sheet(SheetGrid.Columns, SheetOptions.MaxGridCount));
        // 칸 수를 쓰면 태그 줄 안에서도 줄바꿈
        Assert.Equal(new Size(12, 12), Sheet(SheetGrid.Columns, 3)); // a: 3 + 2칸, 태그 없음: 2칸
    }

    [Theory]
    [InlineData("0", SheetGrid.Rows, 1, 12, 4)] // 예전 0 = 한 줄
    [InlineData("2", SheetGrid.Columns, 2, 8, 8)]
    public void LegacyColumns_LoadAsBefore(string columns, SheetGrid grid, int count, int sheetW, int sheetH)
    {
        var p = ProjectIO.Load(Zip($$"""
            { "version": 2, "width": 4, "height": 4, "frames": [ {}, {}, {} ],
              "export": { "columns": {{columns}}, "partsLayout": "RowPerPart" } }
            """));
        Assert.Equal((grid, count), (p.Export.Common.Grid, p.Export.Common.GridCount));
        Assert.Null(p.Export.PartsOverride); // 부위가 없으면 부위 설정은 뜻이 없어 공통 한 벌
        Assert.Equal(new Size(sheetW, sheetH), ProjectExport.BuildSheet(p, "x").Image.Size);
        Assert.Null(p.ExportedFiles); // 기록 없는 예전 파일
    }

    [Fact]
    public void Import_OneRowSheet_MapsToOneRow()
    {
        var doc = new Document(new PixelImage(12, 4));
        doc.Sprite.Mode = SpriteMode.Multiple;
        for (int i = 0; i < 3; i++) doc.Slices.Add(new Slice($"s{i}", new Rectangle(i * 4, 0, 4, 4), Pivot.Center));
        var p = ProjectImport.FromDocument(doc);
        Assert.Equal((SheetGrid.Rows, 1), (p.Export.Common.Grid, p.Export.Common.GridCount));
        Assert.Equal(new Size(12, 4), ProjectExport.BuildSheet(p, "x").Image.Size);
    }

    // ───────────── 부위 아틀라스 격자 ─────────────

    [Fact]
    public void RowPerPartTag_FirstTagOnly_UntaggedLastRow()
    {
        var p = Rig();
        var o = p.Export.Common;
        o.Naming = SpriteNaming.FrameNumber;
        o.Order = GridOrder.ByPartTag;
        o.Grid = SheetGrid.Columns;
        o.GridCount = 10;
        var s = ProjectExport.BuildPartsSheet(p, "rig_parts");
        // 열 폭 = 4 + 간격 1, 줄: Head idle / walk / 태그 없음, Arm idle / walk / 태그 없음
        Assert.Equal(new Rectangle(0, 0, 4, 2), RectOf(s, "Head"));
        Assert.Equal(new Rectangle(5, 0, 4, 2), RectOf(s, "Head_1")); // idle·walk 둘 다지만 첫 태그(idle)에만
        Assert.Equal(new Rectangle(5, 3, 4, 2), RectOf(s, "Head_2")); // walk 안 번호 1
        Assert.Equal(new Rectangle(10, 3, 4, 2), RectOf(s, "Head_3"));
        Assert.Equal(new Rectangle(0, 6, 4, 2), RectOf(s, "Head_4")); // 태그 없는 줄
        Assert.Equal(new Rectangle(0, 9, 2, 3), RectOf(s, "Arm"));
        Assert.Equal(new Rectangle(5, 13, 2, 3), RectOf(s, "Arm_2"));
        Assert.Equal(new Rectangle(0, 17, 2, 3), RectOf(s, "Arm_4"));
        Assert.Equal(8, s.Entries.Count); // 같은 스프라이트를 두 번 넣지 않음
        Assert.Equal(new Size(14, 20), s.Image.Size);

        // 태그가 없으면 부위마다 한 줄과 같다
        while (p.Tags.Count > 0) p.RemoveTag(0);
        var tagless = ProjectExport.BuildPartsSheet(p, "x").Entries.Select(e => (e.Name, e.Rect)).ToList();
        o.Order = GridOrder.ByPart;
        Assert.Equal(ProjectExport.BuildPartsSheet(p, "x").Entries.Select(e => (e.Name, e.Rect)), tagless);
    }

    [Fact]
    public void PartsGrid_WrapsEachPartAtColumnCount()
    {
        var p = Rig();
        var o = p.Export.Common;
        o.Naming = SpriteNaming.FrameNumber;
        o.Grid = SheetGrid.Columns;
        o.GridCount = 2;
        var s = ProjectExport.BuildPartsSheet(p, "x");
        Assert.Equal(new Rectangle(5, 3, 4, 2), RectOf(s, "Head_3")); // 2칸마다 줄바꿈
        Assert.Equal(new Rectangle(0, 6, 4, 2), RectOf(s, "Head_4"));
        Assert.Equal(new Rectangle(0, 13, 2, 3), RectOf(s, "Arm_2")); // Arm은 새 줄(y 9)에서 시작, 프레임 2 = 둘째 줄
        Assert.Equal(new Size(9, 20), s.Image.Size);

        // 세로 N칸: 줄 수가 6 이하가 되는 가장 적은 칸 수(2)
        o.Grid = SheetGrid.Rows;
        o.GridCount = 6;
        Assert.Equal(new Size(9, 20), ProjectExport.BuildPartsSheet(p, "x").Image.Size);
    }

    // ───────────── 이름 ─────────────

    [Fact]
    public void PartsNaming_ByTag_AndCleanNames()
    {
        var p = Rig();
        Assert.Equal(SpriteNaming.ByTag, new SheetOptions().Naming); // 새 프로젝트 기본값
        p.Export.Common.Naming = SpriteNaming.ByTag;
        Assert.Equal(["Head_idle_0", "Head_idle_1", "Head_walk_1", "Head_walk_2", "Head_4", "Arm_idle_0", "Arm_walk_1", "Arm_4"],
            ProjectExport.BuildPartsSheet(p, "x").Entries.Select(e => e.Name));

        p.Export.Common.Naming = SpriteNaming.FrameNumber;
        Assert.Equal(["Head", "Head_1", "Head_2", "Head_3", "Head_4", "Arm", "Arm_2", "Arm_4"],
            ProjectExport.BuildPartsSheet(p, "x").Entries.Select(e => e.Name));

        Assert.Equal("Arm_Left", ProjectExport.CleanSpriteName(" Arm/Left\t"));
        Assert.Equal("a_b", ProjectExport.CleanSpriteName("a\\b"));
        Assert.Equal("sprite", ProjectExport.CleanSpriteName("  "));
        Assert.Equal("a_b_c", ProjectExport.CleanFileName("a:b?c. "));
    }

    [Fact]
    public void SavedProject_KeepsNamingAndNewSettings()
    {
        using var dir = new TempDir();
        var path = dir.File("rig.chickxel");
        var p = Rig();
        var c = p.Export.Common;
        c.Naming = SpriteNaming.FrameNumber;
        c.Grid = SheetGrid.Rows;
        c.GridCount = 3;
        c.SplitTags = true;
        c.SplitParts = true;
        PartsOnly(p, o => { o.Grid = SheetGrid.Columns; o.GridCount = 5; o.Order = GridOrder.ByPartTag; });
        ProjectIO.Save(p, path);
        var back = ProjectIO.Load(path).Export;
        Assert.Equal((SheetGrid.Rows, 3, true, true, SpriteNaming.FrameNumber),
            (back.Common.Grid, back.Common.GridCount, back.Common.SplitTags, back.Common.SplitParts, back.Common.Naming));
        Assert.NotNull(back.PartsOverride);
        Assert.Equal((SheetGrid.Columns, 5, GridOrder.ByPartTag, true, SpriteNaming.FrameNumber),
            (back.Parts.Grid, back.Parts.GridCount, back.Parts.Order, back.Parts.SplitParts, back.Parts.Naming));

        // 실행취소 스냅숏(복사)에도 들어간다
        var copy = new ExportSettings();
        copy.CopyFrom(back);
        Assert.Equal((SheetGrid.Rows, 3, true, true, SpriteNaming.FrameNumber),
            (copy.Common.Grid, copy.Common.GridCount, copy.Common.SplitTags, copy.Common.SplitParts, copy.Common.Naming));
        Assert.Equal(5, copy.Parts.GridCount);
        Assert.NotSame(back.PartsOverride, copy.PartsOverride);
    }

    // ───────────── 따로 파일 ─────────────

    [Fact]
    public void SplitExport_FilesMetaNamesAndRegisteredPaths()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.chickxel");
        var p = Rig();
        p.Export.WriteMeta = true;
        p.Export.Common.SplitTags = true;
        p.Export.Common.SplitParts = true;
        PartsOnly(p, o => o.SplitTags = false); // 부위 아틀라스는 부위마다만
        var whole = ProjectExport.BuildPartsSheet(p, "hero_parts").Entries.Select(e => e.Name).ToList();

        var result = ProjectIO.SaveWithExport(p, path).Export!;
        Assert.Equal([dir.File("hero_idle.png"), dir.File("hero_walk.png"), dir.File("hero_untagged.png")], result.Frames.Select(r => r.ImagePath));
        Assert.Equal([dir.File("hero_parts_Head.png"), dir.File("hero_parts_Arm.png")], result.Parts.Select(r => r.ImagePath));
        Assert.All(result.All, r => Assert.True(r.MetaWritten && File.Exists(r.ImagePath + ".meta")));
        Assert.Empty(result.Stale);

        // .meta 스프라이트 이름 = 미리보기 칸 목록(파일마다)
        var frameSheets = ProjectExport.BuildFrameSheets(p, "hero");
        var partSheets = ProjectExport.BuildPartsSheets(p, "hero_parts");
        foreach (var (file, sheet) in result.Frames.Zip(frameSheets).Concat(result.Parts.Zip(partSheets)))
            Assert.Equal(sheet.Sheet.Entries.Select(e => e.Name), UnityMeta.Read(file.ImagePath + ".meta").Sprites.Select(s => s.Name));
        Assert.Equal(["walk_0", "walk_1", "walk_2"], frameSheets[1].Sheet.Entries.Select(e => e.Name)); // 그 파일의 태그로 이름
        Assert.Equal(["hero_4"], frameSheets[2].Sheet.Entries.Select(e => e.Name));
        Assert.Equal(whole, partSheets.SelectMany(s => s.Sheet.Entries).Select(e => e.Name)); // 나눠도 한 장일 때와 같은 이름

        // 파일 목록에 숨길 등록 경로·이름 바꾸기/삭제에 딸린 파일
        var registered = ProjectIO.GetRegisteredExportPaths(path);
        Assert.All(result.All, r => Assert.Contains(r.ImagePath, registered));
        var owned = ProjectIO.GetOwnedDefaultExportPaths(path);
        Assert.All(result.All, r => Assert.Contains(r.ImagePath + ".meta", owned));

        // 이름 바꾸기: 나눈 파일도 함께, 기록도 새 이름
        var renamed = dir.File("knight.chickxel");
        ProjectIO.RenameWithOwnedDefaultExport(path, renamed);
        Assert.True(File.Exists(dir.File("knight_walk.png")) && File.Exists(dir.File("knight_parts_Arm.png.meta")));
        Assert.False(File.Exists(dir.File("hero_walk.png")));
        var loaded = ProjectIO.Load(renamed);
        Assert.Contains(dir.File("knight_walk.png"), loaded.ExportedFiles!);
        Assert.DoesNotContain(loaded.ExportedFiles!, f => Path.GetFileName(f).StartsWith("hero", StringComparison.Ordinal));
        Assert.Empty(ProjectIO.SaveWithExport(loaded).Export!.Stale);
    }

    [Fact]
    public void SplitExport_DuplicateFileNameIsRejected()
    {
        var p = Rig();
        p.Export.Common.SplitTags = true;
        PartsOnly(p, o => o.SplitTags = false);
        p.AddTag("parts", 4, 4); // hero_parts.png = 부위 아틀라스 이름
        Assert.Throws<InvalidOperationException>(() => ProjectExport.Plan(p, "C:/x/hero.chickxel"));
    }

    // ───────────── 기록·옛 결과 정리 ─────────────

    [Fact]
    public void Record_SplitToSingle_ListsOldFilesAsStale()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.chickxel");
        var p = Rig();
        p.Export.WriteMeta = true;
        p.Export.Common.SplitTags = true;
        PartsOnly(p, o => o.SplitTags = false);
        ProjectIO.SaveWithExport(p, path);
        Assert.Equal([dir.File("hero_idle.png"), dir.File("hero_idle.png.meta"), dir.File("hero_walk.png"), dir.File("hero_walk.png.meta"),
                dir.File("hero_untagged.png"), dir.File("hero_untagged.png.meta"), dir.File("hero_parts.png"), dir.File("hero_parts.png.meta")],
            p.ExportedFiles);

        // 기록 왕복(프로젝트 기준 상대 경로)
        Assert.Equal(p.ExportedFiles, ProjectIO.Load(path).ExportedFiles);

        p.Edit("한 장", ProjectChange.Settings, () => p.Export.Common.SplitTags = false);
        var e = ProjectIO.SaveWithExport(p).Export!;
        Assert.Equal([dir.File("hero_idle.png"), dir.File("hero_idle.png.meta"), dir.File("hero_walk.png"), dir.File("hero_walk.png.meta"),
            dir.File("hero_untagged.png"), dir.File("hero_untagged.png.meta")], e.Stale);
        Assert.DoesNotContain(dir.File("hero_parts.png"), e.Stale); // 계속 쓰는 파일은 정리하지 않음
        Assert.All(e.Stale, f => Assert.True(File.Exists(f))); // Core는 지우지 않는다(앱이 휴지통으로)

        // 앱이 휴지통으로 옮긴 뒤 다시 내보내면 정리할 것이 없다
        foreach (var f in e.Stale) File.Delete(f);
        Assert.Empty(ProjectIO.SaveWithExport(p).Export!.Stale);
    }

    [Fact]
    public void Record_OldProjectOrCopyOrSaveAs_CleansNothing()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.chickxel");
        var p = Rig();
        ProjectIO.Save(p, path); // 내보내기 없이 저장 = 기록 없음(예전 파일과 같음)
        File.WriteAllText(dir.File("hero_walk.png"), "user file");
        var old = ProjectIO.Load(path);
        Assert.Null(old.ExportedFiles);
        Assert.Empty(ProjectIO.SaveWithExport(old).Export!.Stale); // 처음엔 아무것도 지우지 않고
        Assert.NotNull(ProjectIO.Load(path).ExportedFiles); // 이번 내보내기부터 기록

        // 밖에서 복사한 프로젝트: 기록은 원래 이름의 것이라 쓰지 않는다
        File.Copy(path, dir.File("copy.chickxel"));
        Assert.Null(ProjectIO.Load(dir.File("copy.chickxel")).ExportedFiles);

        // 다른 이름으로 저장: 원래 프로젝트의 결과물을 정리하지 않는다
        old.Edit("나누기", ProjectChange.Settings, () => old.Export.Common.SplitTags = true);
        Assert.Empty(ProjectIO.SaveWithExport(old, dir.File("other.chickxel")).Export!.Stale);
        Assert.True(File.Exists(dir.File("hero.png")));
        Assert.True(File.Exists(dir.File("hero_walk.png")));
    }
}
