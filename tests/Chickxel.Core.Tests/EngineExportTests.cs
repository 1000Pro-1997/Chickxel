using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class EngineExportTests
{
    /// <summary>3프레임(100·200·100ms), 태그 walk = 0~1.</summary>
    private static SpriteProject Walker(ExportEngine engine)
    {
        var p = new SpriteProject(4, 4);
        p.AddFrame();
        p.AddFrame();
        p.Edit("설정", ProjectChange.Settings | ProjectChange.Frames | ProjectChange.Tags, () =>
        {
            p.Frames[0].DurationMs = 100;
            p.Frames[1].DurationMs = 200;
            p.Frames[2].DurationMs = 100;
            p.Export.Engine = engine;
        });
        p.AddTag("walk", 0, 1);
        return p;
    }

    /// <summary>저장한 파일의 <c>project.json</c> 내보내기 설정을 고쳐 예전 파일처럼 만들고 경로로 다시 연다.</summary>
    private static SpriteProject LoadLegacy(TempDir dir, SpriteProject p, Action<JsonObject> export)
    {
        var path = dir.File("hero.chickxel");
        ProjectIO.Save(p, path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            JsonNode json;
            using (var s = entry.Open()) json = JsonNode.Parse(s)!;
            var x = json["export"]!.AsObject();
            x.Remove("engine");
            x.Remove("writeMeta");
            export(x);
            entry.Delete();
            using var w = new StreamWriter(zip.CreateEntry(ProjectIO.ProjectEntryName).Open());
            w.Write(json.ToJsonString());
        }
        return ProjectIO.Load(path);
    }

    [Fact]
    public void LegacyMetaSetting_MapsToEngine()
    {
        using var dir = new TempDir();
        Assert.Equal(ExportEngine.Unity, LoadLegacy(dir, new SpriteProject(2, 2), x => x["writeMeta"] = true).Export.Engine);
        Assert.Equal(ExportEngine.None, LoadLegacy(dir, new SpriteProject(2, 2), x => x["writeMeta"] = false).Export.Engine);
        // 자동: Unity 파일도 .meta도 없으면 없음, 프리팹을 쓰거나 시트 .meta가 이미 있으면 Unity
        Assert.Equal(ExportEngine.None, LoadLegacy(dir, new SpriteProject(2, 2), _ => { }).Export.Engine);
        Assert.Equal(ExportEngine.Unity, LoadLegacy(dir, new SpriteProject(2, 2), x => x["prefab"] = true).Export.Engine);
        File.WriteAllText(dir.File("hero.png.meta"), "fileFormatVersion: 2\n");
        Assert.Equal(ExportEngine.Unity, LoadLegacy(dir, new SpriteProject(2, 2), _ => { }).Export.Engine);
    }

    [Fact]
    public void NewProject_WritesNoMeta()
    {
        using var dir = new TempDir();
        ProjectIO.SaveWithExport(new SpriteProject(2, 2), dir.File("hero.chickxel"));
        Assert.True(File.Exists(dir.File("hero.png")));
        Assert.False(File.Exists(dir.File("hero.png.meta")));
    }

    [Fact]
    public void Godot_WritesSpriteFramesAndScene()
    {
        using var dir = new TempDir();
        var p = Walker(ExportEngine.Godot);
        p.Edit("씬", ProjectChange.Settings, () => p.Export.GodotScene = true);
        var result = ProjectIO.SaveWithExport(p, dir.File("hero.chickxel"));

        Assert.Equal(["hero.tres", "hero.tscn"], result.Export!.Engine.Select(r => Path.GetFileName(r.ImagePath)));
        Assert.False(File.Exists(dir.File("hero.png.meta")));
        var tres = File.ReadAllText(dir.File("hero.tres"));
        Assert.StartsWith("[gd_resource type=\"SpriteFrames\"", tres);
        Assert.Contains("path=\"hero.png\"", tres);
        Assert.Contains("\"name\": &\"walk\"", tres);
        Assert.Contains("\"speed\": 10", tres); // 가장 흔한 100ms = 1배
        Assert.Contains("\"duration\": 2", tres); // 200ms
        Assert.Contains("ExtResource(\"1\")", File.ReadAllText(dir.File("hero.tscn")));
        Assert.Contains(Path.GetFullPath(dir.File("hero.tres")), p.ExportedFiles!, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Godot_InsideGodotProject_UsesResPath()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("project.godot"), "");
        Directory.CreateDirectory(dir.File("art"));
        ProjectIO.SaveWithExport(Walker(ExportEngine.Godot), dir.File("art/hero.chickxel"));
        Assert.Contains("path=\"res://art/hero.png\"", File.ReadAllText(dir.File("art/hero.tres")));
    }

    [Fact]
    public void Json_WritesAsepriteHash()
    {
        using var dir = new TempDir();
        ProjectIO.SaveWithExport(Walker(ExportEngine.Json), dir.File("hero.chickxel"));

        using var doc = JsonDocument.Parse(File.ReadAllText(dir.File("hero.json")));
        var root = doc.RootElement;
        var frames = root.GetProperty("frames").EnumerateObject().ToList();
        Assert.Equal(3, frames.Count);
        Assert.Equal(200, frames[1].Value.GetProperty("duration").GetInt32());
        Assert.Equal(4, frames[0].Value.GetProperty("sourceSize").GetProperty("w").GetInt32());
        Assert.Equal("hero.png", root.GetProperty("meta").GetProperty("image").GetString());
        var tag = root.GetProperty("meta").GetProperty("frameTags")[0];
        Assert.Equal(("walk", 0, 1, "forward"), (tag.GetProperty("name").GetString(), tag.GetProperty("from").GetInt32(), tag.GetProperty("to").GetInt32(), tag.GetProperty("direction").GetString()));
        Assert.Equal(2, root.GetProperty("animations").GetProperty("walk").GetArrayLength());
    }
}
