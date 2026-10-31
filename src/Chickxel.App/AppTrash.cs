using System.Text.Json;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 앱 자체 휴지통(<c>&lt;설정 폴더&gt;/Chickxel/Trash</c>): 파일 브라우저에서 지운 파일(합치기로 지운 원본 포함)을 한 번의 삭제마다
/// 항목 하나로 보관한다. 항목 폴더 = <c>{Id}/</c>(보관한 파일 + <c>entry.json</c>). <see cref="Purge"/>가 정책(<see cref="TrashPolicy"/>)대로
/// 보관 기간이 지난 항목과, 전체 용량이 넘치면 오래된 항목부터 영구 삭제한다.
/// </summary>
/// <param name="Days">보관 기간(일). 0 = 제한 없음.</param>
/// <param name="MaxMegabytes">최대 용량(MB). 0 = 제한 없음.</param>
public readonly record struct TrashPolicy(int Days, int MaxMegabytes)
{
    /// <summary>기본값: 기간 제한 없음, 용량 1GB.</summary>
    public static TrashPolicy Default => new(0, 1024);

    public long MaxBytes => MaxMegabytes <= 0 ? 0 : MaxMegabytes * 1024L * 1024L;

    /// <summary>"기간 제한 없음 · 용량 1GB까지"처럼 짧은 설명.</summary>
    public string Describe()
    {
        string days = Days > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_AppTrash_Describe_11EBAE0E3E,Days) : UiStrings.S_AppTrash_Describe_ED0D38DA1D;
        string size = MaxMegabytes > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_AppTrash_Describe_E7FB7A8909,AppTrash.FormatSize(MaxBytes)) : UiStrings.S_AppTrash_Describe_715BEC5E40;
        return $"{days} · {size}";
    }
}

public sealed class AppTrash
{
    private const string ManifestName = "entry.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>보관한 파일 하나: 원래 경로와 항목 폴더 안 이름.</summary>
    public sealed record TrashFile(string OriginalPath, string StoredName);

    /// <summary>삭제 한 번. <see cref="Label"/>은 목록에 보이는 설명.</summary>
    public sealed record Entry(string Id, DateTime DeletedUtc, string Label, List<TrashFile> Files);

    public AppTrash(string root) => Root = root;

    public string Root { get; }

    public static string DefaultRoot => Path.Combine(Platform.AppFolder, "Trash");

    /// <summary>보관 중인 항목(최근 것부터). 깨진 항목은 건너뛴다.</summary>
    public List<Entry> Entries()
    {
        var list = new List<Entry>();
        if (!Directory.Exists(Root)) return list;
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            try
            {
                var manifest = Path.Combine(dir, ManifestName);
                if (File.Exists(manifest) && JsonSerializer.Deserialize<Entry>(File.ReadAllText(manifest), Options) is { } e
                    && string.Equals(e.Id, Path.GetFileName(dir), StringComparison.Ordinal))
                    list.Add(e);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }
        return list.OrderByDescending(e => e.DeletedUtc).ToList();
    }

    /// <summary>
    /// 파일들을 새 항목으로 옮긴다(없는 파일은 건너뜀). 도중에 실패하면 이미 옮긴 파일까지만 항목으로 남기고 예외를 다시 던진다
    /// (옮긴 파일은 휴지통에서 되살릴 수 있다). 옮긴 파일이 없으면 null.
    /// </summary>
    public Entry? Add(IEnumerable<string> paths, string label)
    {
        var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6];
        var dir = Path.Combine(Root, id);
        Directory.CreateDirectory(dir);
        var entry = new Entry(id, DateTime.UtcNow, label, new List<TrashFile>());
        try
        {
            foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) continue;
                var stored = $"{entry.Files.Count}_{Path.GetFileName(path)}";
                File.Move(path, Path.Combine(dir, stored));
                entry.Files.Add(new TrashFile(path, stored));
            }
        }
        finally
        {
            if (entry.Files.Count > 0) File.WriteAllText(Path.Combine(dir, ManifestName), JsonSerializer.Serialize(entry, Options));
            else Directory.Delete(dir, recursive: true);
        }
        return entry.Files.Count > 0 ? entry : null;
    }

    /// <summary>되살릴 때 이미 같은 경로에 파일이 있는 원래 경로들.</summary>
    public static List<string> Conflicts(Entry entry) => entry.Files.Select(f => f.OriginalPath).Where(File.Exists).ToList();

    /// <summary>
    /// 항목의 파일을 원래 자리로 되돌리고 항목을 지운다(폴더가 없으면 만듦). 같은 경로에 파일이 있으면 아무것도 옮기지 않고
    /// <see cref="IOException"/>. 되돌린 원래 경로들을 돌려준다.
    /// </summary>
    public List<string> Restore(Entry entry)
    {
        var conflicts = Conflicts(entry);
        if (conflicts.Count > 0)
            throw new IOException(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_AppTrash_Restore_022042A106,string.Join(", ", conflicts.Select(Path.GetFileName))));
        var dir = Path.Combine(Root, entry.Id);
        var restored = new List<string>();
        foreach (var f in entry.Files)
        {
            var stored = Path.Combine(dir, f.StoredName);
            if (!File.Exists(stored)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(f.OriginalPath)!);
            File.Move(stored, f.OriginalPath);
            restored.Add(f.OriginalPath);
        }
        Directory.Delete(dir, recursive: true);
        return restored;
    }

    /// <summary>항목을 영구 삭제한다.</summary>
    public void Delete(Entry entry)
    {
        var dir = Path.Combine(Root, entry.Id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// 정책대로 영구 삭제한다: 보관 기간이 지난 항목, 그다음 전체 용량이 최대를 넘으면 오래된 항목부터 넘지 않을 때까지.
    /// 지우지 못한 항목은 다음에 다시 시도한다. 지운 항목 수를 돌려준다.
    /// </summary>
    public int Purge(TrashPolicy policy)
    {
        int removed = 0;
        var entries = Entries();
        if (policy.Days > 0)
        {
            var limit = DateTime.UtcNow.AddDays(-policy.Days);
            foreach (var e in entries.Where(e => e.DeletedUtc < limit).ToList())
                if (TryDelete(e)) { entries.Remove(e); removed++; }
        }
        if (policy.MaxBytes > 0)
        {
            var sizes = entries.ToDictionary(e => e.Id, SizeOf);
            long total = sizes.Values.Sum();
            // 오래된 것부터(목록은 최근 것부터).
            for (int i = entries.Count - 1; i >= 0 && total > policy.MaxBytes; i--)
                if (TryDelete(entries[i])) { total -= sizes[entries[i].Id]; removed++; }
        }
        return removed;
    }

    private bool TryDelete(Entry e)
    {
        try { Delete(e); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>항목 하나가 차지하는 크기(바이트).</summary>
    public long SizeOf(Entry entry)
    {
        var dir = Path.Combine(Root, entry.Id);
        if (!Directory.Exists(dir)) return 0;
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>휴지통 전체 크기(바이트).</summary>
    public long TotalSize() => Entries().Sum(SizeOf);

    /// <summary>"12.3 MB"처럼.</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };

    /// <summary>보관된 파일의 실제 경로(휴지통 폴더 안).</summary>
    public string StoredPath(Entry entry, TrashFile file) => Path.Combine(Root, entry.Id, file.StoredName);

    /// <summary>남은 보관 일수(올림, 최소 0).</summary>
    public static int DaysLeft(Entry e, int days) => Math.Max(0, (int)Math.Ceiling((e.DeletedUtc.AddDays(days) - DateTime.UtcNow).TotalDays));
}
