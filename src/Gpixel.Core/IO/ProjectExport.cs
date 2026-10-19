using System.Drawing;
using Gpixel.Core.Model;
using Gpixel.Core.Slicing;

namespace Gpixel.Core.IO;

/// <summary>시트의 칸 하나.</summary>
/// <param name="FrameIndex">프로젝트 프레임 번호.</param>
/// <param name="Name">칸(스프라이트) 이름.</param>
/// <param name="Rect">시트 안 영역(왼쪽 위 원점).</param>
/// <param name="Pivot">칸 기준 피벗(아래 왼쪽 원점, 잘라낸 경우 0~1 밖일 수 있음).</param>
/// <param name="Tag">이 칸이 속한(이름을 정한) 태그 이름. 없으면 null.</param>
/// <param name="Border">9-slice 테두리(부위 아틀라스).</param>
/// <param name="Part">부위 아틀라스 칸이면 그 부위 레이어, 프레임 시트면 null.</param>
public sealed record SheetEntry(int FrameIndex, string Name, Rectangle Rect, Pivot Pivot, string? Tag, SpriteBorder Border = default, Layer? Part = null);

/// <summary>만들어진 시트 이미지와 칸 목록.</summary>
public sealed record SheetResult(PixelImage Image, IReadOnlyList<SheetEntry> Entries, Size CellSize);

/// <summary>내보내기 결과.</summary>
public sealed record ExportResult(string ImagePath, bool MetaWritten, int SpriteCount);

/// <summary>프로젝트 내보내기 결과: 프레임 시트와 부위 아틀라스(꺼져 있거나 쓸 부위가 없으면 null).</summary>
public sealed record ProjectExportResult(ExportResult? Frames, ExportResult? Parts);

/// <summary>
/// 프로젝트 → 스프라이트 시트 이미지 + Unity 메타 내보내기. 편집 정보(칸·태그 애니메이션)는 <c>.gpixel</c>에만 있고 이미지 옆에 따로 쓰지 않는다.
/// <list type="bullet">
/// <item>칸 이름: 프레임 이름 재정의 → <c>{태그}_{태그 안 번호}</c>(첫 번째로 그 프레임을 포함한 태그) → <c>{파일}_{프레임 번호}</c>. 겹치면 <c>_1</c>, <c>_2</c> …</item>
/// <item>배치: <see cref="SheetLayout.Columns"/>는 프레임 순서로 가로 N칸(0 = 한 줄), <see cref="SheetLayout.RowPerTag"/>는 태그마다 한 줄
/// (여러 태그에 속한 프레임은 줄마다 들어감) + 태그 없는 프레임 줄.</item>
/// <item>Single 모드: <see cref="ExportSettings.SingleFrameIndex"/> 프레임 하나를 캔버스 크기 그대로.</item>
/// <item>부위 아틀라스(<c>{시트 이름}_parts.png</c> 또는 <see cref="ExportSettings.PartsFileName"/>): 부위 × 내용 있는 프레임마다 스프라이트 하나
/// (이름은 프레임 0이 <c>{부위}</c>, 그다음 프레임은 <c>{부위}_{프레임}</c> — 프레임을 늘려도 기존 Unity 스프라이트 이름·ID가 그대로)를 <see cref="AtlasPacker"/>로 한 장에 배치(항상 Multiple).
/// "원래 위치 유지"면 가져온 칸에 아직 들어가는 부위(프레임 0)는 원래 자리, 시트는 최소 캔버스 크기.</item>
/// </list>
/// </summary>
public static class ProjectExport
{
    /// <summary>프레임 시트 이미지 경로: (지정 폴더 또는 프로젝트 폴더)/(지정 이름 또는 프로젝트 이름).(형식 확장자).</summary>
    public static string GetExportImagePath(SpriteProject project, string? projectPath = null)
        => GetExportImagePath(project.Export, projectPath ?? project.Path ?? throw new InvalidOperationException("프로젝트를 먼저 저장해야 내보낼 수 있습니다."));

    /// <inheritdoc cref="GetExportImagePath(SpriteProject, string?)"/>
    public static string GetExportImagePath(ExportSettings export, string projectPath)
    {
        var name = string.IsNullOrWhiteSpace(export.FileName)
            ? Path.GetFileNameWithoutExtension(projectPath)
            : export.FileName.Trim();
        return Path.Combine(GetExportFolder(export, projectPath), name + export.ImageFormat.Extension());
    }

    /// <summary>부위 아틀라스 이미지 경로: 프레임 시트와 같은 폴더의 (지정 이름 또는 <c>{프레임 시트 이름}_parts</c>).(형식 확장자).</summary>
    public static string GetPartsImagePath(SpriteProject project, string? projectPath = null)
        => GetPartsImagePath(project.Export, projectPath ?? project.Path ?? throw new InvalidOperationException("프로젝트를 먼저 저장해야 내보낼 수 있습니다."));

    /// <inheritdoc cref="GetPartsImagePath(SpriteProject, string?)"/>
    public static string GetPartsImagePath(ExportSettings export, string projectPath)
    {
        var name = string.IsNullOrWhiteSpace(export.PartsFileName)
            ? Path.GetFileNameWithoutExtension(GetExportImagePath(export, projectPath)) + "_parts"
            : export.PartsFileName.Trim();
        return Path.Combine(GetExportFolder(export, projectPath), name + export.ImageFormat.Extension());
    }

    private static string GetExportFolder(ExportSettings export, string projectPath)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "";
        return string.IsNullOrWhiteSpace(export.Folder)
            ? projectDir
            : Path.GetFullPath(Path.Combine(projectDir, export.Folder));
    }

    /// <summary>메타를 쓸지: 설정값, 자동이면 Unity Assets 안이거나 이미 .meta가 있을 때.</summary>
    public static bool ShouldWriteMeta(SpriteProject project, string pngPath)
        => project.Export.WriteMeta
           ?? (UnityProjectLocator.IsInUnityAssets(pngPath) || File.Exists(UnityMeta.GetMetaPath(pngPath)));

    /// <summary>
    /// 시트를 만든다. Single 모드이고 <paramref name="allFrames"/>가 false면 프레임 하나짜리 시트.
    /// </summary>
    /// <param name="baseName">이름 규칙의 <c>{파일}</c>(보통 내보낼 PNG 이름).</param>
    public static SheetResult BuildSheet(SpriteProject project, string baseName, bool allFrames = false)
    {
        if (!allFrames && project.Sprite.Mode == SpriteMode.Single)
        {
            int fi = Math.Clamp(project.Export.SingleFrameIndex, 0, project.Frames.Count - 1);
            var image = project.Flatten(fi);
            var entry = new SheetEntry(fi, baseName, image.Bounds, project.GetFramePivot(fi), null);
            return new SheetResult(image, [entry], image.Size);
        }

        // 1) 배치할 (프레임, 이름을 정하는 태그) 줄 목록
        var rows = new List<List<(int Frame, AnimationTag? Tag)>>();
        if (project.Export.Layout == SheetLayout.RowPerTag && project.Tags.Count > 0)
        {
            foreach (var tag in project.Tags)
            {
                var row = new List<(int, AnimationTag?)>();
                for (int i = tag.From; i <= tag.To; i++) row.Add((i, tag));
                rows.Add(row);
            }
            var untagged = Enumerable.Range(0, project.Frames.Count)
                .Where(i => project.TagOfFrame(i) is null)
                .Select(i => (i, (AnimationTag?)null))
                .ToList();
            if (untagged.Count > 0) rows.Add(untagged);
        }
        else
        {
            int count = project.Frames.Count;
            int cols = project.Export.Columns <= 0 ? count : project.Export.Columns;
            for (int start = 0; start < count; start += cols)
                rows.Add(Enumerable.Range(start, Math.Min(cols, count - start))
                    .Select(i => (i, project.TagOfFrame(i)))
                    .ToList());
        }

        // 2) 프레임 합성·잘라내기(프레임마다 한 번)
        var content = new Dictionary<int, (PixelImage Image, Pivot Pivot)>();
        foreach (var fi in rows.SelectMany(r => r).Select(e => e.Frame).Distinct())
            content[fi] = RenderFrame(project, fi, project.Export.Trim);
        int cellW = content.Values.Max(c => c.Image.Width);
        int cellH = content.Values.Max(c => c.Image.Height);
        int spacing = project.Export.Spacing;
        int maxCols = rows.Max(r => r.Count);
        var sheet = new PixelImage(
            maxCols * cellW + (maxCols - 1) * spacing,
            rows.Count * cellH + (rows.Count - 1) * spacing);

        // 3) 배치·이름
        var used = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<SheetEntry>();
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < rows[r].Count; c++)
            {
                var (fi, tag) = rows[r][c];
                var (img, pivot) = content[fi];
                int x = c * (cellW + spacing), y = r * (cellH + spacing);
                sheet.Blit(img, x, y);
                var name = project.Frames[fi].Name
                           ?? (tag is not null ? $"{tag.Name}_{fi - tag.From}" : $"{baseName}_{fi}");
                var unique = name;
                for (int n = 1; !used.Add(unique); n++) unique = $"{name}_{n}";
                entries.Add(new SheetEntry(fi, unique, new Rectangle(x, y, img.Width, img.Height), pivot, tag?.Name));
            }
        return new SheetResult(sheet, entries, new Size(cellW, cellH));
    }

    /// <summary>부위 아틀라스에 들어갈 (부위, 프레임) 목록: 그룹 위 → 아래, 그룹 안 위 부위부터, 부위마다 내용 있는 프레임 순.</summary>
    private static List<(Layer Part, int Frame)> PartCels(SpriteProject project)
    {
        var cels = new List<(Layer, int)>();
        for (int g = project.Layers.Count - 1; g >= 0; g--)
        {
            var group = project.Layers[g];
            if (!group.IsGroup) continue;
            var children = project.ChildrenOf(group);
            for (int c = children.Count - 1; c >= 0; c--)
                for (int fi = 0; fi < project.Frames.Count; fi++)
                    if (project.GetCel(children[c], project.Frames[fi]) is { } cel && !cel.IsRegionEmpty(cel.Bounds))
                        cels.Add((children[c], fi));
        }
        return cels;
    }

    /// <summary>
    /// 부위 아틀라스: 부위 × 내용 있는 프레임마다 스프라이트 하나를 한 장에 배치한다(이름은 프레임 0이 <c>{부위}</c>,
    /// 그다음 프레임은 <c>{부위}_{프레임}</c>, 겹치면 <c>_1</c> …). 부위가 없으면 칸 없는 1×1 시트.
    /// </summary>
    /// <param name="baseName">내보낼 부위 아틀라스 이름(칸 이름에는 쓰지 않음, <see cref="BuildSheet"/>와 같은 꼴).</param>
    public static SheetResult BuildPartsSheet(SpriteProject project, string baseName)
    {
        var export = project.Export;
        var cels = PartCels(project);
        if (cels.Count == 0) return new SheetResult(new PixelImage(1, 1), [], new Size(1, 1));
        var fixedRects = export.KeepImportedRects ? cels.Select(c => c.Frame == 0 ? c.Part.PartSourceRect : null).ToList() : null;
        var minSize = fixedRects is not null && fixedRects.Any(r => r is not null) ? project.Size : Size.Empty;
        var pack = AtlasPacker.Pack(
            cels.Select(c => c.Part.PartSize).ToList(),
            new AtlasPackOptions(export.AtlasMaxWidth, export.Spacing, export.AtlasPowerOfTwo, export.AtlasOrder, minSize),
            fixedRects);
        var sheet = new PixelImage(pack.Size.Width, pack.Size.Height);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<SheetEntry>(cels.Count);
        for (int i = 0; i < cels.Count; i++)
        {
            var (part, fi) = cels[i];
            var rect = pack.Rects[i];
            sheet.Blit(project.GetCel(part, project.Frames[fi])!, rect.X, rect.Y);
            var name = fi == 0 ? part.Name : $"{part.Name}_{fi}";
            var unique = name;
            for (int n = 1; !used.Add(unique); n++) unique = $"{name}_{n}";
            entries.Add(new SheetEntry(fi, unique, rect, part.PartPivot, null, part.PartBorder, part));
        }
        var cell = new Size(cels.Max(c => c.Part.PartSize.Width), cels.Max(c => c.Part.PartSize.Height));
        return new SheetResult(sheet, entries, cell);
    }

    /// <summary>부위 아틀라스 문서(항상 Multiple, 칸마다 피벗·테두리, 애니메이션 없음).</summary>
    private static Document BuildPartsDocument(SpriteProject project, string baseName)
    {
        var sheet = BuildPartsSheet(project, baseName);
        var doc = new Document(sheet.Image);
        doc.Sprite.CopyFrom(project.Sprite);
        doc.Sprite.Mode = SpriteMode.Multiple;
        doc.Grid = new GridSettings { CellW = sheet.CellSize.Width, CellH = sheet.CellSize.Height, PadX = project.Export.Spacing, PadY = project.Export.Spacing };
        foreach (var e in sheet.Entries)
            doc.Slices.Add(new Slice(e.Name, e.Rect, e.Pivot, e.Border));
        doc.IsDirty = false;
        return doc;
    }

    /// <summary>프레임 합성 이미지와 그 이미지 기준 피벗(잘라내면 보정).</summary>
    private static (PixelImage Image, Pivot Pivot) RenderFrame(SpriteProject project, int frameIndex, bool trim)
    {
        var flat = project.Flatten(frameIndex);
        var pivot = project.GetFramePivot(frameIndex);
        if (!trim) return (flat, pivot);
        var r = flat.GetOpaqueBounds();
        if (r.IsEmpty || r == flat.Bounds) return (flat, pivot);
        var (px, py) = pivot.ToLocalPixel(flat.Width, flat.Height);
        return (flat.Crop(r), new Pivot((px - r.X) / r.Width, 1f - (py - r.Y) / r.Height));
    }

    /// <summary>
    /// 내보낼 내용을 메모리 <see cref="Document"/>로 만든다: 시트 이미지, 칸(이름·피벗), 태그를 방향대로 펼친
    /// 애니메이션(프레임별 ms), 스프라이트 설정. 미리보기·움직임 테스트에도 쓴다.
    /// </summary>
    /// <param name="allFrames">true면 Single 모드여도 모든 프레임 시트(움직임 테스트용).</param>
    public static Document BuildDocument(SpriteProject project, string baseName, bool allFrames = false)
    {
        bool multiple = allFrames || project.Sprite.Mode == SpriteMode.Multiple;
        var sheet = BuildSheet(project, baseName, allFrames);
        var doc = new Document(sheet.Image);
        doc.Sprite.CopyFrom(project.Sprite);
        doc.Grid = new GridSettings { CellW = sheet.CellSize.Width, CellH = sheet.CellSize.Height, PadX = project.Export.Spacing, PadY = project.Export.Spacing };
        if (!multiple)
        {
            doc.Sprite.Mode = SpriteMode.Single;
            doc.Sprite.DefaultPivot = sheet.Entries[0].Pivot;
            return doc;
        }

        doc.Sprite.Mode = SpriteMode.Multiple;
        foreach (var e in sheet.Entries)
            doc.Slices.Add(new Slice(e.Name, e.Rect, e.Pivot, e.Border));
        foreach (var tag in project.Tags)
        {
            var clip = new AnimationClip(tag.Name) { Loop = tag.Loop };
            foreach (var fi in tag.GetPlaybackOrder())
            {
                var entry = sheet.Entries.FirstOrDefault(e => e.FrameIndex == fi && e.Tag == tag.Name)
                            ?? sheet.Entries.First(e => e.FrameIndex == fi);
                clip.Frames.Add(new AnimationFrame(entry.Name, project.Frames[fi].DurationMs));
            }
            if (clip.Frames.Count > 0)
                clip.Fps = Math.Clamp((int)Math.Round(1000.0 / clip.Frames.Average(f => f.Ms)), 1, 120);
            doc.Animations.Add(clip);
        }
        doc.IsDirty = false;
        return doc;
    }

    /// <summary>
    /// 켜진 것을 내보낸다: 프레임 시트(<see cref="ExportSettings.ExportFrames"/>)와 부위 아틀라스(<see cref="ExportSettings.ExportParts"/>이고
    /// 내용 있는 부위 셀이 있을 때). 각각 이미지 + (메타 쓰기면) Unity 메타. 기존 메타의 guid·spriteID·internalID는 칸 이름 기준으로 유지된다.
    /// 두 파일 경로가 같으면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public static ProjectExportResult Export(SpriteProject project)
    {
        bool frames = project.Export.ExportFrames;
        bool parts = project.Export.ExportParts && PartCels(project).Count > 0;
        var framesPath = frames ? Path.GetFullPath(GetExportImagePath(project)) : null;
        var partsPath = parts ? Path.GetFullPath(GetPartsImagePath(project)) : null;
        if (framesPath is not null && string.Equals(framesPath, partsPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("프레임 시트와 부위 아틀라스의 파일 이름이 같습니다. 내보내기 이름을 바꿔 주세요.");
        return new ProjectExportResult(
            framesPath is null ? null : Write(project, framesPath, name => BuildDocument(project, name)),
            partsPath is null ? null : Write(project, partsPath, name => BuildPartsDocument(project, name)));
    }

    private static ExportResult Write(SpriteProject project, string path, Func<string, Document> build)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var doc = build(Path.GetFileNameWithoutExtension(path));
        bool writeMeta = ShouldWriteMeta(project, path);
        DocumentIO.Save(doc, path, writeMeta, project.Export.ImageFormat, project.Export.JpegQuality);
        return new ExportResult(path, writeMeta, Math.Max(1, doc.Slices.Count));
    }
}
