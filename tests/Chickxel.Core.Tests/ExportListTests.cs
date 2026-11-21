using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class ExportListTests
{
    private static SpriteProject TwoFrames()
    {
        var p = new SpriteProject(4, 4);
        p.AddFrame();
        p.Edit("시트 끄기", ProjectChange.Settings, () => p.Export.ExportOnSave = false);
        return p;
    }

    private static ExportEntry Anim(AnimationExportFormat format, string path, bool onSave)
        => ExportEntry.ForAnimation(new AnimationExport { Format = format, Path = path, OnSave = onSave });

    [Fact]
    public void OnSaveRows_AreWrittenOnSave_AndRoundTrip()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.chickxel");
        var p = TwoFrames();
        p.Edit("목록", ProjectChange.Settings, () =>
        {
            var gif = Anim(AnimationExportFormat.Gif, "out/hero.gif", true);
            gif.Animation!.Scale = 3;
            gif.Animation.PlayCount = 2;
            gif.Animation.Timing = new GifTiming { Mode = GifTimingMode.Fps, Fps = 8 };
            p.Export.Entries.Add(gif);
            p.Export.Entries.Add(Anim(AnimationExportFormat.FramePngs, "hero_frames", true));
            p.Export.Entries.Add(Anim(AnimationExportFormat.Apng, "skip.png", false));
        });

        var result = ProjectIO.SaveWithExport(p, path);

        Assert.True(File.Exists(dir.File("out/hero.gif")));
        Assert.True(File.Exists(dir.File("hero_frames/hero_001.png")));
        Assert.True(File.Exists(dir.File("hero_frames/hero_002.png")));
        Assert.False(File.Exists(dir.File("skip.png")));
        Assert.False(File.Exists(dir.File("hero.png"))); // 프로젝트의 시트는 꺼짐
        Assert.Equal(2, result.Export!.Animations.Count);
        Assert.Contains(Path.GetFullPath(dir.File("out/hero.gif")), p.ExportedFiles!, StringComparer.OrdinalIgnoreCase);

        var loaded = ProjectIO.Load(path).Export.Animations.ToList();
        Assert.Equal(3, loaded.Count);
        Assert.Equal((AnimationExportFormat.Gif, "out/hero.gif", 3, 2, true), (loaded[0].Format, loaded[0].Path, loaded[0].Scale, loaded[0].PlayCount, loaded[0].OnSave));
        Assert.Equal((GifTimingMode.Fps, 8), (loaded[0].Timing.Mode, loaded[0].Timing.Fps));
        Assert.False(loaded[2].OnSave);
    }

    [Fact]
    public void SecondSheetRow_WritesItsOwnFileAndSettings_AndRoundTrips()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.chickxel");
        var p = TwoFrames();
        p.Edit("목록", ProjectChange.Settings, () =>
        {
            p.Export.ExportOnSave = true;
            var big = p.Export.CloneSheet();
            big.FileName = "hero_big";
            big.Folder = "big";
            big.Common.Scale = 2;
            p.Export.Entries.Add(ExportEntry.ForSheet(big));
        });

        var result = ProjectIO.SaveWithExport(p, path);

        Assert.True(File.Exists(dir.File("hero.png")));
        Assert.True(File.Exists(dir.File("big/hero_big.png")));
        Assert.Equal(2, result.Export!.Frames.Count);
        var small = DocumentIO.Load(dir.File("hero.png")).Document.Image;
        var large = DocumentIO.Load(dir.File("big/hero_big.png")).Document.Image;
        Assert.True(large.Width > small.Width); // 그 줄의 배율 2
        var loaded = ProjectIO.Load(path).Export.Entries.Single().Sheet!;
        Assert.Equal(("hero_big", "big", 2, true), (loaded.FileName, loaded.Folder, loaded.Common.Scale, loaded.ExportOnSave));
        Assert.Empty(loaded.Entries);
    }

    [Fact]
    public void DuplicateTargetsAcrossRows_AreRejected()
    {
        using var dir = new TempDir();
        var p = TwoFrames();
        p.Edit("목록", ProjectChange.Settings, () =>
        {
            p.Export.ExportOnSave = true;
            p.Export.Entries.Add(ExportEntry.ForSheet(p.Export.CloneSheet()));
        });
        Assert.Throws<InvalidOperationException>(() => ProjectExport.Plan(p, dir.File("hero.chickxel")));
    }

    [Fact]
    public void RowTurnedOff_KeepsItsFile_RemovedRowBecomesStale()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.chickxel");
        var p = TwoFrames();
        p.Edit("목록", ProjectChange.Settings, () => p.Export.Entries.Add(Anim(AnimationExportFormat.Gif, "hero.gif", true)));
        ProjectIO.SaveWithExport(p, path);
        var gif = Path.GetFullPath(dir.File("hero.gif"));

        p.Edit("끄기", ProjectChange.Settings, () => p.Export.Entries[0].OnSave = false);
        p.Edit("시트", ProjectChange.Settings, () => p.Export.ExportOnSave = true);
        var kept = ProjectIO.SaveWithExport(p, path);
        Assert.DoesNotContain(gif, kept.Export!.Stale, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(gif, p.ExportedFiles!, StringComparer.OrdinalIgnoreCase);

        p.Edit("빼기", ProjectChange.Settings, () => p.Export.Entries.Clear());
        var removed = ProjectIO.SaveWithExport(p, path);
        Assert.Contains(gif, removed.Export!.Stale, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportingOneRow_KeepsOtherRowsFiles()
    {
        using var dir = new TempDir();
        var path = dir.File("hero.chickxel");
        var p = TwoFrames();
        p.Edit("목록", ProjectChange.Settings, () =>
        {
            p.Export.ExportOnSave = true;
            var other = p.Export.CloneSheet();
            other.FileName = "hero_other";
            p.Export.Entries.Add(ExportEntry.ForSheet(other));
        });
        ProjectIO.SaveWithExport(p, path);

        var plan = ProjectExport.BeginExport(p, entry: 0);
        Assert.Empty(plan.Stale);
        Assert.Equal(["hero_other.png"], ProjectExport.Write(p, plan).Frames.Select(f => Path.GetFileName(f.ImagePath)));
        Assert.Empty(ProjectExport.BeginExport(p).Stale);
    }

    /// <summary>GIF 줄 + 프레임마다 PNG 줄(둘 다 저장할 때)로 한 번 저장한 프로젝트.</summary>
    private static SpriteProject SavedWithTwoRows(TempDir dir)
    {
        var p = TwoFrames();
        p.Edit("목록", ProjectChange.Settings, () =>
        {
            p.Export.Entries.Add(Anim(AnimationExportFormat.Gif, "hero.gif", true));
            p.Export.Entries.Add(Anim(AnimationExportFormat.FramePngs, "hero_frames", true));
        });
        ProjectIO.SaveWithExport(p, dir.File("hero.chickxel"));
        return p;
    }

    [Fact]
    public void RemovedRow_ReportsOnlyItsOwnFiles()
    {
        using var dir = new TempDir();
        var p = SavedWithTwoRows(dir);
        var gif = Path.GetFullPath(dir.File("hero.gif"));

        var dropped = ProjectExport.OutputsDroppedBy(p, () => p.Edit("빼기", ProjectChange.Settings, () => p.Export.Entries.RemoveAt(0)));
        Assert.Equal([gif], dropped);
        Assert.True(File.Exists(gif)); // 파일은 건드리지 않는다

        // 이미 뺀 줄(GIF)의 결과는 다음 줄을 뺄 때 다시 들지 않는다
        var frames = ProjectExport.OutputsDroppedBy(p, () => p.Edit("빼기", ProjectChange.Settings, () => p.Export.Entries.RemoveAt(0)));
        Assert.Equal(["hero_001.png", "hero_002.png"], frames.Select(Path.GetFileName).Order());
    }

    [Fact]
    public void RemovedRow_WithoutRecord_ReportsNothing()
    {
        var p = TwoFrames();
        p.Edit("목록", ProjectChange.Settings, () => p.Export.Entries.Add(Anim(AnimationExportFormat.Gif, "hero.gif", true)));
        Assert.Empty(ProjectExport.OutputsDroppedBy(p, () => p.Edit("빼기", ProjectChange.Settings, () => p.Export.Entries.Clear())));
        Assert.Empty(p.Export.Entries);
    }

    [Fact]
    public void RemovedRow_KeptFiles_AreNotCleanedUpOnSave()
    {
        using var dir = new TempDir();
        var p = SavedWithTwoRows(dir);
        var gif = Path.GetFullPath(dir.File("hero.gif"));

        var dropped = ProjectExport.OutputsDroppedBy(p, () => p.Edit("빼기", ProjectChange.Settings, () => p.Export.Entries.RemoveAt(0)));
        ProjectExport.ForgetOutputs(p, dropped); // 취소: 파일은 남기고 기록에서만 지움
        Assert.DoesNotContain(gif, p.ExportedFiles!, StringComparer.OrdinalIgnoreCase);

        var saved = ProjectIO.SaveWithExport(p);
        Assert.DoesNotContain(gif, saved.Export!.Stale, StringComparer.OrdinalIgnoreCase);
        Assert.True(File.Exists(gif));
        Assert.DoesNotContain(gif, ProjectIO.Load(dir.File("hero.chickxel")).ExportedFiles!, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemovedRow_TrashedFiles_AreForgotten()
    {
        using var dir = new TempDir();
        var p = SavedWithTwoRows(dir);
        var gif = Path.GetFullPath(dir.File("hero.gif"));

        var dropped = ProjectExport.OutputsDroppedBy(p, () => p.Edit("빼기", ProjectChange.Settings, () => p.Export.Entries.RemoveAt(0)));
        foreach (var f in dropped) File.Delete(f); // 확인: 앱이 앱 휴지통으로 옮김
        ProjectExport.ForgetOutputs(p, dropped);

        Assert.DoesNotContain(gif, p.ExportedFiles!, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(p.ExportedFiles!, f => Path.GetFileName(f) == "hero_001.png"); // 남은 줄의 기록은 그대로
        Assert.Empty(ProjectIO.SaveWithExport(p).Export!.Stale);
    }

    [Fact]
    public void RemovedRow_NotForgotten_IsStillCleanedUpOnSave()
    {
        using var dir = new TempDir();
        var p = SavedWithTwoRows(dir);
        var dropped = ProjectExport.OutputsDroppedBy(p, () => p.Edit("빼기", ProjectChange.Settings, () => p.Export.Entries.RemoveAt(0)));
        Assert.Equal(dropped, ProjectIO.SaveWithExport(p).Export!.Stale);
    }

    [Fact]
    public void ListPreset_CarriesRowsToAnotherProject_WithItsName()
    {
        using var dir = new TempDir();
        var hero = TwoFrames();
        hero.Edit("목록", ProjectChange.Settings, () =>
        {
            hero.Export.ImageFormat = ExportImageFormat.Jpg;
            var big = hero.Export.CloneSheet();
            big.FileName = "hero_big";
            big.Folder = "big";
            big.Common.Scale = 3;
            big.Engine = ExportEngine.Godot;
            hero.Export.Entries.Add(ExportEntry.ForSheet(big));
            hero.Export.Entries.Add(Anim(AnimationExportFormat.Gif, "anim/hero_walk.gif", true));
            hero.Export.Entries.Add(Anim(AnimationExportFormat.Apng, Path.Combine(Path.GetTempPath(), "x", "elsewhere.png"), false));
        });
        var preset = ExportListPreset.Capture(hero.Export, "hero", "내 목록");
        var path = dir.File("presets.json");
        ExportListPresets.Save(path, [preset]);
        var loaded = ExportListPresets.Load(path).Single();

        var knight = new SpriteProject(2, 2);
        knight.Edit("적용", ProjectChange.Settings, () => loaded.ApplyTo(knight.Export, "knight"));

        var x = knight.Export;
        Assert.Equal((ExportImageFormat.Jpg, false), (x.ImageFormat, x.ExportOnSave));
        var big = x.Entries[0].Sheet!;
        Assert.Equal(("knight_big", "big", 3, ExportEngine.Godot), (big.FileName, big.Folder, big.Common.Scale, big.Engine));
        Assert.Equal(("anim" + Path.DirectorySeparatorChar + "knight_walk.gif", true), (x.Entries[1].Animation!.Path, x.Entries[1].Animation!.OnSave));
        Assert.Equal("elsewhere.png", x.Entries[2].Animation!.Path); // 전체 경로는 이름만
        knight.Undo();
        Assert.Empty(knight.Export.Entries);
    }

    [Fact]
    public void UndoRestoresRows()
    {
        var p = TwoFrames();
        p.Edit("목록", ProjectChange.Settings, () => p.Export.Entries.Add(ExportEntry.ForSheet(p.Export.CloneSheet())));
        p.Edit("이름", ProjectChange.Settings, () => p.Export.Entries[0].Sheet!.FileName = "other");
        p.Undo();
        Assert.Null(p.Export.Entries.Single().Sheet!.FileName);
        p.Undo();
        Assert.Empty(p.Export.Entries);
    }

    [Fact]
    public void SharedCopy_KeepsOnlyFileNames()
    {
        var p = TwoFrames();
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "hero.gif");
        p.Edit("목록", ProjectChange.Settings, () =>
        {
            p.Export.Entries.Add(Anim(AnimationExportFormat.Gif, outside, false));
            var sheet = p.Export.CloneSheet();
            sheet.Folder = "../secret/place";
            sheet.FileName = "../x/hero_alt";
            p.Export.Entries.Add(ExportEntry.ForSheet(sheet));
        });
        using var shared = new MemoryStream();
        ProjectIO.SaveForSharing(p, shared);
        shared.Position = 0;
        var copy = ProjectIO.Load(shared).Export.Entries;
        Assert.Equal("hero.gif", copy[0].Animation!.Path);
        Assert.Equal((null, "hero_alt"), (copy[1].Sheet!.Folder, copy[1].Sheet!.FileName));
    }
}
