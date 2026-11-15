using System.Globalization;
using System.Text;
using System.Text.Json;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

// 다른 게임 엔진 파일(ExportSettings.Engine): Godot 4 SpriteFrames·씬, JSON 데이터(Aseprite 형식). 프레임 시트로만 만든다.
public static partial class ProjectExport
{
    public const string GodotResourceExtension = ".tres";
    public const string GodotSceneExtension = ".tscn";
    public const string JsonDataExtension = ".json";

    /// <summary>
    /// Godot·JSON 파일 경로(프레임 시트를 내보낼 때만): Godot = 프레임 시트 옆 <c>{시트}.tres</c>(+ 씬이면 <c>{시트}.tscn</c>),
    /// JSON = 프레임 시트 파일마다 같은 이름의 <c>.json</c>. 다른 엔진이면 빈 목록.
    /// </summary>
    public static IReadOnlyList<string> GetEngineDataPaths(SpriteProject project, string projectPath)
    {
        var x = Sheet(project);
        if (!x.ExportFrames) return [];
        switch (x.Engine)
        {
            case ExportEngine.Godot:
            {
                var image = GetExportImagePath(x, projectPath);
                var stem = Path.Combine(Path.GetDirectoryName(image)!, Path.GetFileNameWithoutExtension(image));
                return x.GodotScene ? [stem + GodotResourceExtension, stem + GodotSceneExtension] : [stem + GodotResourceExtension];
            }
            case ExportEngine.Json:
                return GetOutputPaths(project, projectPath, assumePartContent: true).Frames.Select(f => Path.ChangeExtension(f, JsonDataExtension)).ToList();
            default:
                return [];
        }
    }

    /// <summary>프레임 시트 파일 하나와 그 이미지 경로.</summary>
    private sealed record WrittenSheet(string ImagePath, SheetFile File);

    /// <summary>계획의 Godot·JSON 경로대로 방금 쓴 프레임 시트들을 가리키는 파일을 쓴다(내용이 같으면 다시 쓰지 않음).</summary>
    private static List<ExportResult> WriteEngineData(SpriteProject project, ExportPlan plan, IReadOnlyList<WrittenSheet> sheets)
    {
        var results = new List<ExportResult>();
        var paths = plan.EnginePaths ?? [];
        if (paths.Count == 0 || sheets.Count == 0) return results;
        switch (Sheet(project).Engine)
        {
            case ExportEngine.Godot:
            {
                var tres = paths.First(p => p.EndsWith(GodotResourceExtension, StringComparison.OrdinalIgnoreCase));
                WriteIfChanged(tres, GodotSpriteFrames(project, sheets, tres, out int frames));
                results.Add(new ExportResult(tres, false, frames));
                if (paths.FirstOrDefault(p => p.EndsWith(GodotSceneExtension, StringComparison.OrdinalIgnoreCase)) is { } tscn)
                {
                    WriteIfChanged(tscn, GodotScene(project, sheets, tres, tscn));
                    results.Add(new ExportResult(tscn, false, 1));
                }
                break;
            }
            case ExportEngine.Json:
                foreach (var s in sheets)
                {
                    var json = Path.ChangeExtension(s.ImagePath, JsonDataExtension);
                    if (!paths.Contains(json, StringComparer.OrdinalIgnoreCase)) continue;
                    WriteIfChanged(json, AsepriteJson(project, s));
                    results.Add(new ExportResult(json, false, s.File.Sheet.Entries.Count));
                }
                break;
        }
        return results;
    }

    private static void WriteIfChanged(string path, string text)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == text) return;
        }
        catch (IOException)
        {
        }
        AtomicFile.WriteText(path, text);
    }

    /// <summary>재생할 애니메이션: 태그마다(재생 차례 — 역방향·왕복 그대로, 시트에 그 프레임이 다 있을 때), 태그가 없으면 "default" 하나(모든 프레임).</summary>
    private static List<(string Name, bool Loop, TagDirection Direction, List<int> Frames)> EngineAnimations(SpriteProject project, IReadOnlySet<int> inSheets)
    {
        var list = new List<(string, bool, TagDirection, List<int>)>();
        foreach (var tag in project.Tags)
        {
            var order = tag.GetPlaybackOrder();
            if (order.Count > 0 && order.All(inSheets.Contains)) list.Add((tag.Name, tag.Loop, tag.Direction, order));
        }
        if (project.Tags.Count == 0)
        {
            var all = Enumerable.Range(0, project.Frames.Count).Where(inSheets.Contains).ToList();
            if (all.Count > 0) list.Add(("default", true, TagDirection.Forward, all));
        }
        return list;
    }

    /// <summary>프레임 하나를 그린 칸: 그 태그 줄의 원래 이름 항목 먼저, 없으면 같은 프레임의 원래 이름 항목, 없으면 첫 항목.</summary>
    private static (WrittenSheet Sheet, SheetEntry Entry)? FrameEntry(IReadOnlyList<WrittenSheet> sheets, int frame, string? tag)
    {
        (WrittenSheet, SheetEntry)? Find(Func<SheetEntry, bool> match)
        {
            foreach (var s in sheets)
                if (s.File.Sheet.Entries.FirstOrDefault(match) is { } e) return (s, e);
            return null;
        }
        return Find(e => e.FrameIndex == frame && e.Tag == tag && e.Variant?.Suffix is null)
               ?? Find(e => e.FrameIndex == frame && e.Variant?.Suffix is null)
               ?? Find(e => e.FrameIndex == frame);
    }

    /// <summary>
    /// 칸 그림이 원래 프레임(캔버스 × 배율) 안 어디에 있는지: 왼쪽 위 어긋남과 원래 크기. 잘라내기를 하지 않았으면 (0, 0)과 칸 크기.
    /// 피벗이 같은 점을 가리키도록 거꾸로 구한다.
    /// </summary>
    private static (int X, int Y, int W, int H) SourceBox(SpriteProject project, SheetResult sheet, SheetEntry e)
    {
        int scale = Math.Max(1, sheet.Scale);
        int w = project.Width * scale, h = project.Height * scale;
        if (e.Rect.Width >= w && e.Rect.Height >= h) return (0, 0, e.Rect.Width, e.Rect.Height);
        var (px, py) = project.GetFramePivot(e.FrameIndex).ToLocalPixel(w, h);
        var (qx, qy) = e.Pivot.ToLocalPixel(e.Rect.Width, e.Rect.Height);
        return ((int)Math.Round(px - qx), (int)Math.Round(py - qy), w, h);
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Godot 리소스가 다른 파일을 가리키는 경로: Godot 프로젝트(<c>project.godot</c>) 안이면 <c>res://</c>, 아니면 리소스 파일 기준 상대 경로.</summary>
    private static string GodotPath(string from, string target)
    {
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(from)); dir is not null; dir = Path.GetDirectoryName(dir))
            if (File.Exists(Path.Combine(dir, "project.godot")))
                return "res://" + Path.GetRelativePath(dir, target).Replace('\\', '/');
        return Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(from))!, target).Replace('\\', '/');
    }

    private static string GodotString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>
    /// Godot 4 SpriteFrames(<c>.tres</c>): 시트 이미지마다 Texture2D, 칸마다 AtlasTexture(잘라냈으면 원래 크기로 margin), 애니메이션마다 프레임(가장 흔한 길이 = 1배,
    /// 나머지는 그 배수의 duration), 반복은 태그의 반복.
    /// </summary>
    private static string GodotSpriteFrames(SpriteProject project, IReadOnlyList<WrittenSheet> sheets, string tresPath, out int frameCount)
    {
        var inSheets = sheets.SelectMany(s => s.File.Sheet.Entries).Select(e => e.FrameIndex).ToHashSet();
        var animations = EngineAnimations(project, inSheets);
        var textures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var atlases = new Dictionary<SheetEntry, int>();
        var body = new StringBuilder();
        var sub = new StringBuilder();
        var frames = animations.Select(a => (a, a.Frames.Select(f => (f, FrameEntry(sheets, f, a.Name))).Where(t => t.Item2 is not null).ToList())).ToList();
        foreach (var (_, list) in frames)
            foreach (var (_, found) in list)
            {
                var (sheet, entry) = found!.Value;
                if (!textures.ContainsKey(sheet.ImagePath)) textures[sheet.ImagePath] = textures.Count + 1;
                if (atlases.ContainsKey(entry)) continue;
                atlases[entry] = atlases.Count;
                sub.Append(CultureInfo.InvariantCulture, $"[sub_resource type=\"AtlasTexture\" id=\"AtlasTexture_{atlases[entry]}\"]\n");
                sub.Append(CultureInfo.InvariantCulture, $"atlas = ExtResource(\"{textures[sheet.ImagePath]}\")\n");
                sub.Append(CultureInfo.InvariantCulture, $"region = Rect2({entry.Rect.X}, {entry.Rect.Y}, {entry.Rect.Width}, {entry.Rect.Height})\n");
                var box = SourceBox(project, sheet.File.Sheet, entry);
                if (box.X != 0 || box.Y != 0 || box.W != entry.Rect.Width || box.H != entry.Rect.Height)
                    sub.Append(CultureInfo.InvariantCulture, $"margin = Rect2({box.X}, {box.Y}, {box.W - entry.Rect.Width}, {box.H - entry.Rect.Height})\n");
                sub.Append('\n');
            }

        frameCount = atlases.Count;
        var head = new StringBuilder();
        head.Append(CultureInfo.InvariantCulture, $"[gd_resource type=\"SpriteFrames\" load_steps={textures.Count + atlases.Count + 1} format=3]\n\n");
        foreach (var (path, id) in textures)
            head.Append(CultureInfo.InvariantCulture, $"[ext_resource type=\"Texture2D\" path={GodotString(GodotPath(tresPath, path))} id=\"{id}\"]\n");
        head.Append('\n');

        body.Append("[resource]\nanimations = [");
        for (int i = 0; i < frames.Count; i++)
        {
            var (anim, list) = frames[i];
            var ms = list.Select(t => project.Frames[t.f].DurationMs).ToList();
            int common = ms.Count == 0 ? 100 : Math.Max(1, ms.GroupBy(m => m).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key);
            if (i > 0) body.Append(", ");
            body.Append("{\n\"frames\": [");
            for (int j = 0; j < list.Count; j++)
            {
                if (j > 0) body.Append(", ");
                body.Append(CultureInfo.InvariantCulture,
                    $"{{\n\"duration\": {F(Math.Max(1, ms[j]) / (double)common)},\n\"texture\": SubResource(\"AtlasTexture_{atlases[list[j].Item2!.Value.Entry]}\")\n}}");
            }
            body.Append(CultureInfo.InvariantCulture, $"],\n\"loop\": {(anim.Loop ? "true" : "false")},\n\"name\": &{GodotString(anim.Name)},\n\"speed\": {F(1000.0 / common)}\n}}");
        }
        body.Append("]\n");
        return head.Append(sub).Append(body).ToString();
    }

    /// <summary>
    /// Godot 4 씬(<c>.tscn</c>): AnimatedSprite2D 하나(픽셀 그대로 — Nearest, 첫 프레임의 캔버스 피벗이 노드 원점), 첫 애니메이션을 자동 재생.
    /// </summary>
    private static string GodotScene(SpriteProject project, IReadOnlyList<WrittenSheet> sheets, string tresPath, string tscnPath)
    {
        var inSheets = sheets.SelectMany(s => s.File.Sheet.Entries).Select(e => e.FrameIndex).ToHashSet();
        var first = EngineAnimations(project, inSheets).FirstOrDefault();
        int scale = Math.Max(1, sheets[0].File.Sheet.Scale);
        int frame = first.Frames is { Count: > 0 } f ? f[0] : 0;
        var (px, py) = project.GetFramePivot(frame).ToLocalPixel(project.Width * scale, project.Height * scale);
        var sb = new StringBuilder();
        sb.Append("[gd_scene load_steps=2 format=3]\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"[ext_resource type=\"SpriteFrames\" path={GodotString(GodotPath(tscnPath, tresPath))} id=\"1\"]\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"[node name={GodotString(Path.GetFileNameWithoutExtension(tscnPath))} type=\"AnimatedSprite2D\"]\n");
        sb.Append("texture_filter = 1\n");
        sb.Append("sprite_frames = ExtResource(\"1\")\n");
        if (first.Name is { } name)
        {
            sb.Append(CultureInfo.InvariantCulture, $"animation = &{GodotString(name)}\n");
            sb.Append(CultureInfo.InvariantCulture, $"autoplay = {GodotString(name)}\n");
        }
        sb.Append("centered = false\n");
        sb.Append(CultureInfo.InvariantCulture, $"offset = Vector2({F(-px)}, {F(-py)})\n");
        return sb.ToString();
    }

    /// <summary>
    /// JSON 데이터(Aseprite "hash" 형식 — Phaser·PixiJS 등이 읽음): 칸마다 자리·원래 크기·길이, <c>meta.frameTags</c>(태그 프레임이 칸 차례로 이어질 때),
    /// 그리고 PixiJS용 <c>animations</c>(태그마다 칸 이름 — 재생 차례).
    /// </summary>
    private static string AsepriteJson(SpriteProject project, WrittenSheet s)
    {
        var sheet = s.File.Sheet;
        var entries = sheet.Entries;
        var inSheet = entries.Select(e => e.FrameIndex).ToHashSet();
        var animations = EngineAnimations(project, inSheet);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteStartObject("frames");
            foreach (var e in entries)
            {
                var box = SourceBox(project, sheet, e);
                w.WriteStartObject(e.Name);
                WriteRect(w, "frame", e.Rect.X, e.Rect.Y, e.Rect.Width, e.Rect.Height);
                w.WriteBoolean("rotated", false);
                w.WriteBoolean("trimmed", box.W != e.Rect.Width || box.H != e.Rect.Height);
                WriteRect(w, "spriteSourceSize", box.X, box.Y, e.Rect.Width, e.Rect.Height);
                w.WriteStartObject("sourceSize");
                w.WriteNumber("w", box.W);
                w.WriteNumber("h", box.H);
                w.WriteEndObject();
                w.WriteNumber("duration", project.Frames[e.FrameIndex].DurationMs);
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteStartObject("animations");
            foreach (var a in animations)
            {
                w.WriteStartArray(a.Name);
                foreach (var f in a.Frames)
                    if (FrameEntry([s], f, a.Name == "default" && project.Tags.Count == 0 ? null : a.Name) is { } found) w.WriteStringValue(found.Entry.Name);
                w.WriteEndArray();
            }
            w.WriteEndObject();

            w.WriteStartObject("meta");
            w.WriteString("app", "Chickxel");
            w.WriteString("image", Path.GetFileName(s.ImagePath));
            w.WriteString("format", "RGBA8888");
            w.WriteStartObject("size");
            w.WriteNumber("w", sheet.Image.Width);
            w.WriteNumber("h", sheet.Image.Height);
            w.WriteEndObject();
            w.WriteString("scale", "1");
            w.WriteStartArray("frameTags");
            foreach (var tag in project.Tags)
            {
                // 칸 차례에서 태그의 첫·끝 프레임 자리가 이어진 구간일 때만(Aseprite 형식은 from~to 구간)
                var index = Enumerable.Range(tag.From, tag.To - tag.From + 1)
                    .Select(f => FrameEntry([s], f, tag.Name) is { } found ? entries.ToList().IndexOf(found.Entry) : -1).ToList();
                if (index.Count == 0 || index.Any(i => i < 0) || index.Zip(index.Skip(1)).Any(p => p.Second != p.First + 1)) continue;
                w.WriteStartObject();
                w.WriteString("name", tag.Name);
                w.WriteNumber("from", index[0]);
                w.WriteNumber("to", index[^1]);
                w.WriteString("direction", tag.Direction switch { TagDirection.Reverse => "reverse", TagDirection.PingPong => "pingpong", _ => "forward" });
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteRect(Utf8JsonWriter w, string name, int x, int y, int width, int height)
    {
        w.WriteStartObject(name);
        w.WriteNumber("x", x);
        w.WriteNumber("y", y);
        w.WriteNumber("w", width);
        w.WriteNumber("h", height);
        w.WriteEndObject();
    }
}
