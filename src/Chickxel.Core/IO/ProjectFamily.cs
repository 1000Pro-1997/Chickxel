namespace Chickxel.Core.IO;

/// <summary>
/// 파일 목록에서 프로젝트 하나에 딸려 보일 파일(Unity 하위 에셋처럼 프로젝트 칸 옆 화살표로 펼침):
/// 프로젝트에 등록된 내보내기 결과 중 있는 것 + 같은 폴더에서 이름이 프로젝트 이름으로 시작하는 그림·영상
/// (<c>{이름}.gif</c>, <c>{이름}_01.png</c>, <c>{이름}@skin.png</c> — 내보낸 GIF·저장한 프레임·착장 파일).
/// 파일을 읽지 않는 순수 계산(등록 경로·존재 여부는 넘겨받음).
/// </summary>
public static class ProjectFamily
{
    /// <summary>
    /// 프로젝트마다 딸린 파일(이름 순)을 돌려준다. 모든 프로젝트가 키로 들어간다(딸린 파일이 없으면 빈 목록).
    /// 여러 프로젝트 이름에 맞는 파일은 가장 긴 이름의 프로젝트에 딸린다(<c>sprite_3.gif</c> → <c>sprite_3</c>, <c>sprite</c> 아님).
    /// 등록된 내보내기는 이름·폴더와 무관하게 그 프로젝트에 딸리고(다른 폴더로 내보낸 것도), 프로젝트 파일은 어디에도 딸리지 않는다.
    /// </summary>
    /// <param name="projects">목록의 프로젝트 파일 경로.</param>
    /// <param name="files">목록의 다른 파일 경로(그림·영상이 아닌 것은 건너뜀).</param>
    /// <param name="registeredExports">프로젝트에 등록된 내보내기 경로(<see cref="ProjectIO.GetRegisteredExportPaths"/>).</param>
    /// <param name="exists">목록에 없는 등록 경로가 실제로 있는지(기본 <see cref="File.Exists(string?)"/>).</param>
    /// <returns>프로젝트 경로(넘겨받은 그대로) → 딸린 파일 경로(목록에 있으면 목록의 경로, 아니면 전체 경로).</returns>
    public static Dictionary<string, List<string>> Group(IReadOnlyList<string> projects, IReadOnlyList<string> files,
        Func<string, IEnumerable<string>> registeredExports, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects) result.TryAdd(p, []);
        var projectSet = projects.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 전체 경로 → 목록에 적힌 경로
        var listed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files) listed.TryAdd(Path.GetFullPath(f), f);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool IsChildKind(string full) => !projectSet.Contains(full) && MediaFormats.KindOf(full) is MediaKind.Image or MediaKind.Video;

        // 1) 등록된 내보내기(있는 것만)
        foreach (var (project, children) in result)
        {
            foreach (var r in registeredExports(project))
            {
                string full;
                try
                {
                    full = Path.GetFullPath(r);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }
                if (!IsChildKind(full) || taken.Contains(full)) continue;
                if (listed.TryGetValue(full, out var shown)) children.Add(shown);
                else if (exists(full)) children.Add(full);
                else continue;
                taken.Add(full);
            }
        }

        // 2) 같은 폴더의 이름이 프로젝트 이름으로 시작하는 파일(가장 긴 이름의 프로젝트에)
        var byFolder = result.Keys
            .Select(p => (Project: p, Full: Path.GetFullPath(p)))
            .GroupBy(p => Path.GetDirectoryName(p.Full) ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(p => (p.Project, Stem: Path.GetFileNameWithoutExtension(p.Full))).ToList(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var (full, shown) in listed)
        {
            if (taken.Contains(full) || !IsChildKind(full)
                || !byFolder.TryGetValue(Path.GetDirectoryName(full) ?? "", out var candidates)) continue;
            var name = Path.GetFileName(full);
            string? owner = null;
            int best = -1;
            foreach (var (project, stem) in candidates)
            {
                if (stem.Length > best && ProjectIO.HasStem(name, stem))
                {
                    owner = project;
                    best = stem.Length;
                }
            }
            if (owner is null) continue;
            result[owner].Add(shown);
            taken.Add(full);
        }

        foreach (var children in result.Values)
            children.Sort((a, b) =>
            {
                int byName = StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(a), Path.GetFileName(b));
                return byName != 0 ? byName : StringComparer.OrdinalIgnoreCase.Compare(a, b);
            });
        return result;
    }
}
