using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>다른 이름으로 저장(<see cref="ProjectExport.PlanCopy"/>): 고른 이름 기준 파일들, .meta·기록 없음, 프로젝트 그대로.</summary>
public class ExportCopyTests
{
    /// <summary>2×2, 프레임 2개(태그 idle 0, walk 1), 그룹 body의 부위 Body(1×1, 빨강). 프레임 시트·부위 아틀라스 켬, .meta 항상.</summary>
    private static SpriteProject Rig()
    {
        var p = new SpriteProject(2, 2);
        p.AddFrame();
        var body = p.AddGroup("body");
        var part = p.AddPart(body, "Body", 1, 1, new Point(0, 0));
        for (int f = 0; f < 2; f++)
        {
            p.BeginCelStroke(p.IndexOf(part), f).Set(0, 0, new Color32(255, 0, 0));
            Assert.True(p.CommitCelStroke("점"));
        }
        p.AddTag("idle", 0, 0);
        p.AddTag("walk", 1, 1);
        p.Export.ExportFrames = true;
        p.Export.ExportParts = true;
        p.Export.WriteMeta = true;
        p.Export.FileName = "configured";
        return p;
    }

    [Fact]
    public void Copy_UsesChosenName_NoMeta_NoRecord_ProjectUntouched()
    {
        using var dir = new TempDir();
        var p = Rig();
        p.ExportedFiles = ["old.png"];
        p.IsDirty = false;

        var plan = ProjectExport.PlanCopy(p, dir.File("copy.png"));
        var result = ProjectExport.Write(p, plan);

        Assert.Equal([dir.File("copy.png")], plan.FramePaths);
        Assert.Equal([dir.File("copy_parts.png")], plan.PartPaths);
        Assert.Empty(plan.LibraryPaths);
        Assert.Empty(plan.Record);
        Assert.Empty(plan.Stale);
        Assert.All(result.All, r => Assert.False(r.MetaWritten));
        Assert.True(File.Exists(dir.File("copy.png")));
        Assert.True(File.Exists(dir.File("copy_parts.png")));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.meta"));
        Assert.Equal(["old.png"], p.ExportedFiles);
        Assert.False(p.IsDirty);
        Assert.Equal("configured", p.Export.FileName);
    }

    [Fact]
    public void Copy_SplitFiles_KeepSuffixRules_AndPartsOnlyUsesChosenName()
    {
        using var dir = new TempDir();
        var p = Rig();
        p.Export.Common.SplitTags = true;
        p.Export.ImageFormat = ExportImageFormat.Jpg;

        var plan = ProjectExport.PlanCopy(p, dir.File("hero.png"));
        Assert.Equal([dir.File("hero_idle.jpg"), dir.File("hero_walk.jpg")], plan.FramePaths);
        Assert.All(plan.PartPaths, path => Assert.StartsWith(dir.File("hero_parts_"), path, StringComparison.Ordinal));
        ProjectExport.Write(p, plan);
        Assert.All(plan.FramePaths.Concat(plan.PartPaths), path => Assert.True(File.Exists(path)));

        p.Export.ExportFrames = false;
        var partsOnly = ProjectExport.PlanCopy(p, dir.File("solo.jpg"));
        Assert.Empty(partsOnly.FramePaths);
        Assert.All(partsOnly.PartPaths, path => Assert.StartsWith(dir.File("solo_"), path, StringComparison.Ordinal));
        Assert.DoesNotContain(partsOnly.PartPaths, path => path.Contains("_parts", StringComparison.Ordinal));
    }
}
