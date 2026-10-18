using System.Drawing;
using Gpixel.Core.Model;
using Gpixel.Core.Slicing;

namespace Gpixel.Core.IO;

/// <summary>시트의 칸 하나.</summary>
/// <param name="FrameIndex">애니메이션: 프로젝트 프레임 번호. 아틀라스: 스프라이트 번호.</param>
/// <param name="Name">칸(스프라이트) 이름.</param>
/// <param name="Rect">시트 안 영역(왼쪽 위 원점).</param>
/// <param name="Pivot">칸 기준 피벗(아래 왼쪽 원점, 잘라낸 경우 0~1 밖일 수 있음).</param>
/// <param name="Tag">이 칸이 속한(이름을 정한) 태그 이름. 없으면 null.</param>
/// <param name="Border">9-slice 테두리(아틀라스 스프라이트).</param>
public sealed record SheetEntry(int FrameIndex, string Name, Rectangle Rect, Pivot Pivot, string? Tag, SpriteBorder Border = default);

/// <summary>만들어진 시트 이미지와 칸 목록.</summary>
public sealed record SheetResult(PixelImage Image, IReadOnlyList<SheetEntry> Entries, Size CellSize);

/// <summary>내보내기 결과.</summary>
public sealed record ExportResult(string PngPath, bool MetaWritten, string SidecarPath, int SpriteCount);

/// <summary>
/// 프로젝트 → 스프라이트 시트 PNG + 사이드카(<c>.png.gpixel.json</c>, 칸·태그 애니메이션) + Unity 메타 내보내기.
/// <list type="bullet">
/// <item>칸 이름: 프레임 이름 재정의 → <c>{태그}_{태그 안 번호}</c>(첫 번째로 그 프레임을 포함한 태그) → <c>{파일}_{프레임 번호}</c>. 겹치면 <c>_1</c>, <c>_2</c> …</item>
/// <item>배치: <see cref="SheetLayout.Columns"/>는 프레임 순서로 가로 N칸(0 = 한 줄), <see cref="SheetLayout.RowPerTag"/>는 태그마다 한 줄
/// (여러 태그에 속한 프레임은 줄마다 들어감) + 태그 없는 프레임 줄.</item>
/// <item>Single 모드: <see cref="ExportSettings.SingleFrameIndex"/> 프레임 하나를 캔버스 크기 그대로.</item>
/// <item>아틀라스 프로젝트: 모든 스프라이트를 <see cref="AtlasPacker"/>로 한 장에 배치(항상 Multiple, 잘라내기·Single 없음).
/// "원래 위치 유지"면 가져온 칸에 아직 들어가는 스프라이트는 원래 자리, 시트는 최소 원래 크기.</item>
/// </list>
/// </summary>
public static class ProjectExport
{
    /// <summary>내보낼 PNG 경로: (지정 폴더 또는 프로젝트 폴더)/(지정 이름 또는 프로젝트 이름).png.</summary>
    public static string GetExportPngPath(SpriteProject project, string? projectPath = null)
    {
        projectPath ??= project.Path ?? throw new InvalidOperationException("프로젝트를 먼저 저장해야 내보낼 수 있습니다.");
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? "";
        var folder = string.IsNullOrWhiteSpace(project.Export.Folder)
            ? projectDir
            : Path.GetFullPath(Path.Combine(projectDir, project.Export.Folder));
        var name = string.IsNullOrWhiteSpace(project.Export.FileName)
            ? Path.GetFileNameWithoutExtension(projectPath)
            : project.Export.FileName.Trim();
        return Path.Combine(folder, name + ".png");
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
        if (project.IsAtlas) return BuildAtlasSheet(project);
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

    /// <summary>아틀라스: 스프라이트들을 한 장에 배치한다.</summary>
    private static SheetResult BuildAtlasSheet(SpriteProject project)
    {
        var export = project.Export;
        var sprites = project.Sprites;
        var fixedRects = export.KeepImportedRects ? sprites.Select(s => s.SourceRect).ToList() : null;
        var pack = AtlasPacker.Pack(
            sprites.Select(s => s.Size).ToList(),
            new AtlasPackOptions(export.AtlasMaxWidth, export.Spacing, export.AtlasPowerOfTwo, export.AtlasOrder,
                export.KeepImportedRects ? project.AtlasSourceSize : Size.Empty),
            fixedRects);
        var sheet = new PixelImage(pack.Size.Width, pack.Size.Height);
        var entries = new List<SheetEntry>(sprites.Count);
        for (int i = 0; i < sprites.Count; i++)
        {
            var rect = pack.Rects[i];
            sheet.Blit(project.FlattenSprite(i), rect.X, rect.Y);
            entries.Add(new SheetEntry(i, sprites[i].Name, rect, sprites[i].Pivot, null, sprites[i].Border));
        }
        var cell = new Size(sprites.Max(s => s.Width), sprites.Max(s => s.Height));
        return new SheetResult(sheet, entries, cell);
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
        bool multiple = allFrames || project.IsAtlas || project.Sprite.Mode == SpriteMode.Multiple;
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
        foreach (var tag in project.IsAtlas ? [] : project.Tags)
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
    /// 시트 PNG + 사이드카 + (메타 쓰기면) Unity 메타를 쓴다. 기존 메타의 guid·spriteID·internalID는 칸 이름 기준으로 유지된다.
    /// </summary>
    /// <param name="pngPath">null이면 <see cref="GetExportPngPath"/>.</param>
    public static ExportResult Export(SpriteProject project, string? pngPath = null)
    {
        var path = Path.GetFullPath(pngPath ?? GetExportPngPath(project));
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var doc = BuildDocument(project, Path.GetFileNameWithoutExtension(path));
        bool writeMeta = ShouldWriteMeta(project, path);
        DocumentIO.Save(doc, path, writeMeta);
        return new ExportResult(path, writeMeta, SidecarIO.GetPath(path), Math.Max(1, doc.Slices.Count));
    }
}
