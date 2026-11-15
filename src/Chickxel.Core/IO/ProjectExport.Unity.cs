using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

// Unity 프리팹·애니메이션(ExportSettings.WritePrefab·WriteAnimations): 방금 쓴 시트 .meta의 스프라이트로 오브젝트를 짜서 배치·재생까지 Unity에서 바로 쓰게 한다.
public static partial class ProjectExport
{
    public const string PrefabExtension = ".prefab";
    public const string ClipExtension = ".anim";
    public const string ControllerExtension = ".controller";

    /// <summary>프리팹·애니메이션이 가리킬 스프라이트: 부위 아틀라스(부위마다 오브젝트), 프레임 시트(오브젝트 하나), 없음.</summary>
    private enum UnityRig { None, Parts, Frames }

    private static UnityRig RigOf(SpriteProject project, bool assumePartContent)
    {
        var x = project.Export;
        if (!x.WritesUnityAssets) return UnityRig.None;
        if (x.ExportParts && (assumePartContent ? PartLayers(project).Count > 0 : PartCels(project).Count > 0)) return UnityRig.Parts;
        return x.ExportFrames ? UnityRig.Frames : UnityRig.None;
    }

    /// <summary>클립 결과: 태그마다 <c>{시트}_{태그}</c>(태그가 없으면 <c>{시트}</c> 하나 — 모든 프레임).</summary>
    private static List<(AnimationTag? Tag, string Name)> ClipOutputs(SpriteProject project, string baseName)
    {
        if (project.Tags.Count == 0) return [(null, baseName)];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return project.Tags.Select(t => ((AnimationTag?)t, Unique(used, $"{baseName}_{CleanFileName(t.Name)}"))).ToList();
    }

    /// <summary>
    /// Unity 프리팹·애니메이션 경로(프레임 시트와 같은 폴더, 이름 앞부분 = 프레임 시트 이름): <c>{시트}.prefab</c>, 클립들(<see cref="ClipOutputs"/>), <c>{시트}.controller</c>.
    /// 켜져 있지 않거나 가리킬 스프라이트(부위 아틀라스 또는 프레임 시트)가 없으면 빈 목록.
    /// </summary>
    /// <param name="assumePartContent">true면 셀 내용을 보지 않는다(화면 표시용).</param>
    public static IReadOnlyList<string> GetUnityAssetPaths(SpriteProject project, string projectPath, bool assumePartContent)
    {
        var export = project.Export;
        if (RigOf(project, assumePartContent) == UnityRig.None) return [];
        var folder = GetExportFolder(export, projectPath);
        var baseName = Path.GetFileNameWithoutExtension(GetExportImagePath(export, projectPath));
        var paths = new List<string>();
        if (export.WritePrefab) paths.Add(Path.Combine(folder, baseName + PrefabExtension));
        if (export.WriteAnimations)
        {
            paths.AddRange(ClipOutputs(project, baseName).Select(c => Path.Combine(folder, c.Name + ClipExtension)));
            paths.Add(Path.Combine(folder, baseName + ControllerExtension));
        }
        return paths;
    }

    /// <summary>스프라이트 <c>.meta</c>의 칸 이름 → 참조.</summary>
    private static Dictionary<string, SpriteLibraryAssetWriter.SpriteRef> SpriteRefs(string imagePath)
    {
        if (UnityMeta.TryRead(UnityMeta.GetMetaPath(imagePath)) is not { Guid: { } guid } meta) return [];
        var refs = new Dictionary<string, SpriteLibraryAssetWriter.SpriteRef>(StringComparer.Ordinal);
        foreach (var s in meta.Sprites) refs.TryAdd(s.Name, new SpriteLibraryAssetWriter.SpriteRef(s.InternalId, guid));
        // Single 모드 텍스처: 스프라이트 하나(fileID 21300000)
        if (meta.Sprites.Count == 0) refs[""] = new SpriteLibraryAssetWriter.SpriteRef(21300000, guid);
        return refs;
    }

    /// <summary>
    /// 프레임 시트 스프라이트 → 프레임마다 참조(지금 착장 항목 — 원래 이름 항목 먼저, 없으면 첫 항목). Single 모드면 그 스프라이트 하나가 모든 프레임.
    /// </summary>
    private static void AddFrameSprites(Dictionary<int, SpriteLibraryAssetWriter.SpriteRef> map, string imagePath, SheetResult sheet, bool single)
    {
        var refs = SpriteRefs(imagePath);
        if (single)
        {
            if (refs.Values.FirstOrDefault() is { } only)
                foreach (var e in sheet.Entries) map.TryAdd(e.FrameIndex, only);
            return;
        }
        foreach (bool original in (bool[])[true, false])
            foreach (var e in sheet.Entries)
                if ((e.Variant?.Suffix is null) == original && refs.TryGetValue(e.Name, out var r))
                    map.TryAdd(e.FrameIndex, r);
    }

    /// <summary>Unity 프로젝트가 URP를 쓰면 Sprite-Lit-Default, 아니면(찾지 못해도) 내장 Sprites-Default 머티리얼.</summary>
    private static string SpriteMaterial(string path)
    {
        if (UnityProjectLocator.FindProjectRoot(path) is not { } root) return UnityAssetWriter.BuiltInSpriteMaterial;
        var manifest = Path.Combine(root, "Packages", "manifest.json");
        try
        {
            return File.Exists(manifest) && File.ReadAllText(manifest).Contains("\"com.unity.render-pipelines.universal\"", StringComparison.Ordinal)
                ? UnityAssetWriter.UrpSpriteMaterial
                : UnityAssetWriter.BuiltInSpriteMaterial;
        }
        catch (IOException)
        {
            return UnityAssetWriter.BuiltInSpriteMaterial;
        }
    }

    /// <summary>오브젝트 하나의 프레임마다 값: 스프라이트와 부모 기준 위치(유닛).</summary>
    private sealed record RigTrack(string Path, Func<int, SpriteLibraryAssetWriter.SpriteRef?> Sprite, Func<int, PointF> Position);

    /// <summary>
    /// 계획의 Unity 경로대로 클립 → Controller → 프리팹 순서로 쓴다(서로 가리킬 guid를 먼저 정함). 클립·Controller·프리팹 <c>.meta</c>는 없을 때만 새로 만들고
    /// 있으면 guid를 그대로 쓴다. 내용이 같으면 파일을 다시 쓰지 않는다.
    /// </summary>
    private static List<ExportResult> WriteUnityAssets(SpriteProject project, ExportPlan plan,
        IReadOnlyDictionary<(Layer Part, int Frame, Skin? Skin), SpriteLibraryAssetWriter.SpriteRef> partSprites,
        IReadOnlyDictionary<int, SpriteLibraryAssetWriter.SpriteRef> frameSprites)
    {
        var results = new List<ExportResult>();
        var paths = plan.UnityPaths ?? [];
        if (paths.Count == 0) return results;
        var export = project.Export;
        var rig = RigOf(project, assumePartContent: false);
        if (rig == UnityRig.None) return results;
        var baseName = Path.GetFileNameWithoutExtension(GetExportImagePath(export, plan.ProjectPath));
        var folder = Path.GetDirectoryName(paths[0])!;
        float ppu = project.Sprite.PixelsPerUnit;
        int firstFrame = project.Sprite.Mode == SpriteMode.Single ? Math.Clamp(export.SingleFrameIndex, 0, project.Frames.Count - 1) : 0;

        // ── 오브젝트 짜기 ──
        UnityAssetWriter.Node root;
        var tracks = new List<RigTrack>();
        if (rig == UnityRig.Parts)
        {
            PointF RootPixel(int f)
            {
                var (x, y) = project.GetFramePivot(f).ToLocalPixel(project.Width, project.Height);
                return new PointF(x, y);
            }
            // 정렬 순서: 아래 레이어부터 0, 1, …
            var parts = PartLayers(project);
            var order = parts.OrderBy(project.IndexOf).Select((p, i) => (p, i)).ToDictionary(t => t.p, t => t.i);
            var groups = new List<UnityAssetWriter.Node>();
            var usedGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in parts.Select(p => p.Parent!).Distinct())
            {
                var groupName = Unique(usedGroups, CleanSpriteName(group.Name));
                var usedParts = new HashSet<string>(StringComparer.Ordinal);
                var children = new List<UnityAssetWriter.Node>();
                foreach (var part in parts.Where(p => ReferenceEquals(p.Parent, group)))
                {
                    var name = Unique(usedParts, CleanSpriteName(part.Name));
                    var shown = part.ShownSkin;
                    Skin? key = shown is null || ReferenceEquals(shown, group.BaseSkin) ? null : shown;
                    SpriteLibraryAssetWriter.SpriteRef? SpriteAt(int f) => partSprites.GetValueOrDefault((part, f, key));
                    PointF PositionAt(int f)
                    {
                        var at = project.LayerPivotPixel(part, f);
                        var origin = RootPixel(f);
                        return new PointF((at.X - origin.X) / ppu, -(at.Y - origin.Y) / ppu);
                    }
                    var start = PositionAt(firstFrame);
                    children.Add(new UnityAssetWriter.Node(name, part.Visible, start.X, start.Y, true, SpriteAt(firstFrame), order[part], []));
                    tracks.Add(new RigTrack(groupName + "/" + name, SpriteAt, PositionAt));
                }
                groups.Add(new UnityAssetWriter.Node(groupName, group.Visible, 0, 0, false, null, 0, children));
            }
            root = new UnityAssetWriter.Node(CleanSpriteName(baseName), true, 0, 0, false, null, 0, groups);
        }
        else
        {
            // Single 모드는 스프라이트 하나가 모든 프레임
            bool single = project.Sprite.Mode == SpriteMode.Single;
            SpriteLibraryAssetWriter.SpriteRef? SpriteAt(int f) => frameSprites.GetValueOrDefault(single ? firstFrame : f);
            root = new UnityAssetWriter.Node(CleanSpriteName(baseName), true, 0, 0, true, SpriteAt(firstFrame), 0, []);
            tracks.Add(new RigTrack("", SpriteAt, _ => PointF.Empty));
        }

        // ── 클립·Controller ──
        string? controllerGuid = null;
        if (export.WriteAnimations)
        {
            var states = new List<UnityAssetWriter.State>();
            foreach (var (tag, name) in ClipOutputs(project, baseName))
            {
                var frames = tag?.GetPlaybackOrder() ?? Enumerable.Range(0, project.Frames.Count).ToList();
                var clip = BuildClip(project, name, tag?.Loop ?? true, frames, tracks, root);
                var path = Path.Combine(folder, name + ClipExtension);
                var guid = UnityAssetWriter.EnsureMeta(path, g => UnityAssetWriter.NativeMeta(g, 7400000));
                UnityAssetWriter.WriteIfChanged(path, UnityAssetWriter.GenerateClip(clip));
                states.Add(new UnityAssetWriter.State(tag?.Name ?? name, guid));
                results.Add(new ExportResult(path, true, frames.Count));
            }
            var controllerPath = Path.Combine(folder, baseName + ControllerExtension);
            controllerGuid = UnityAssetWriter.EnsureMeta(controllerPath, g => UnityAssetWriter.NativeMeta(g, 9100000));
            UnityAssetWriter.WriteIfChanged(controllerPath, UnityAssetWriter.GenerateController(baseName, states));
            results.Add(new ExportResult(controllerPath, true, states.Count));
        }

        // ── 프리팹 ──
        if (export.WritePrefab)
        {
            var path = Path.Combine(folder, baseName + PrefabExtension);
            UnityAssetWriter.EnsureMeta(path, UnityAssetWriter.PrefabMeta);
            UnityAssetWriter.WriteIfChanged(path, UnityAssetWriter.GeneratePrefab(root, SpriteMaterial(path), controllerGuid));
            results.Insert(0, new ExportResult(path, true, Math.Max(1, tracks.Count)));
        }
        return results;
    }

    /// <summary>
    /// 클립 하나: 프레임 순서(<paramref name="frames"/>)대로 프레임 길이를 쌓은 시각에 오브젝트마다 스프라이트(바뀔 때만)와, 프리팹 자리와 다른 프레임이 있으면 위치(계단).
    /// 초당 샘플 수는 모든 키가 정수 샘플 칸에 오는 가장 작은 정수(1000 ÷ gcd(1000, 프레임 길이들의 최대공약수))이고, 마지막 프레임 길이만큼 끝에 키를 하나 더 둬 클립 길이 = 프레임 길이 합.
    /// </summary>
    private static UnityAssetWriter.Clip BuildClip(SpriteProject project, string name, bool loop, IReadOnlyList<int> frames,
        IReadOnlyList<RigTrack> tracks, UnityAssetWriter.Node root)
    {
        int step = 0;
        foreach (var f in frames) step = Gcd(step, project.Frames[f].DurationMs);
        // 모든 키가 정수 샘플 칸에 오는 가장 작은 정수 샘플 수(100ms → 10, 150ms → 20)
        float rate = 1000f / Gcd(1000, Math.Max(1, step));
        var starts = new float[frames.Count];
        int ms = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            starts[i] = ms / 1000f;
            ms += project.Frames[frames[i]].DurationMs;
        }
        float endKey = ms / 1000f - 1f / rate;

        var statics = new Dictionary<string, PointF>(StringComparer.Ordinal);
        void Collect(UnityAssetWriter.Node n, string path)
        {
            if (path.Length > 0) statics[path] = new PointF(n.X, n.Y);
            foreach (var c in n.Children) Collect(c, path.Length == 0 ? c.Name : path + "/" + c.Name);
        }
        Collect(root, "");

        var sprites = new List<UnityAssetWriter.SpriteCurve>();
        var positions = new List<UnityAssetWriter.PositionCurve>();
        foreach (var track in tracks)
        {
            if (frames.Count == 0) break;
            var keys = new List<(float, SpriteLibraryAssetWriter.SpriteRef?)>();
            SpriteLibraryAssetWriter.SpriteRef? last = null;
            for (int i = 0; i < frames.Count; i++)
            {
                var s = track.Sprite(frames[i]);
                if (i > 0 && Equals(s, last)) continue;
                keys.Add((starts[i], s));
                last = s;
            }
            if (endKey > keys[^1].Item1 + 1e-6f) keys.Add((endKey, last));
            sprites.Add(new UnityAssetWriter.SpriteCurve(track.Path, keys));

            var at = frames.Select(track.Position).ToList();
            var home = statics.GetValueOrDefault(track.Path);
            if (at.All(p => Near(p, home))) continue;
            var moves = new List<(float, float, float)>();
            for (int i = 0; i < frames.Count; i++)
                if (i == 0 || !Near(at[i], at[i - 1])) moves.Add((starts[i], at[i].X, at[i].Y));
            if (endKey > moves[^1].Item1 + 1e-6f) moves.Add((endKey, at[^1].X, at[^1].Y));
            positions.Add(new UnityAssetWriter.PositionCurve(track.Path, moves));
        }
        return new UnityAssetWriter.Clip(name, loop, rate, sprites, positions);
    }

    private static bool Near(PointF a, PointF b) => MathF.Abs(a.X - b.X) < 1e-5f && MathF.Abs(a.Y - b.Y) < 1e-5f;

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return Math.Abs(a);
    }
}
