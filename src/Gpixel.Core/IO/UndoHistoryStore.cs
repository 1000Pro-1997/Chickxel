using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>파일이 바뀌었는지 볼 때 쓰는 표시: 마지막 수정 시각(UTC 틱)과 크기.</summary>
public readonly record struct FileStamp(long LastWriteUtcTicks, long Length)
{
    /// <summary>파일의 지금 표시. 없거나 읽지 못하면 null.</summary>
    public static FileStamp? Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc.Ticks, info.Length) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>실행취소 기록 보관 정책: 쓰지 않은 지 <see cref="Days"/>일(0 = 제한 없음)이 지나면 지우고, 전체가 <see cref="MaxMegabytes"/>MB(0 = 제한 없음)를 넘으면 오래 안 쓴 것부터 지운다.</summary>
public readonly record struct UndoHistoryPolicy(int Days, int MaxMegabytes)
{
    public const int DefaultDays = 30, DefaultMaxMegabytes = 500;

    public static UndoHistoryPolicy Default => new(DefaultDays, DefaultMaxMegabytes);

    public long MaxBytes => MaxMegabytes <= 0 ? 0 : MaxMegabytes * 1024L * 1024L;
}

/// <summary>기록 하나의 정보(<c>entry.json</c>): 원래 파일 경로, 기록을 쓸 때 그 파일의 표시, 단계 수, 크기, 마지막으로 쓰거나 읽은 시각.</summary>
public sealed record UndoHistoryEntry(string Key, string FilePath, FileStamp Stamp, int Steps, long Bytes, DateTime LastUsedUtc);

/// <summary>문서를 열 때 기록을 읽은 결과.</summary>
public enum UndoHistoryLoadStatus
{
    /// <summary>남은 기록이 없다.</summary>
    None,
    /// <summary>읽었다.</summary>
    Loaded,
    /// <summary>파일이 밖에서 바뀌어(표시가 다름) 기록을 버렸다.</summary>
    Stale,
    /// <summary>기록이 손상됐거나 프로젝트와 맞지 않아 버렸다.</summary>
    Failed,
}

public readonly record struct UndoHistoryLoadResult(UndoHistoryLoadStatus Status, int Steps = 0, string? Error = null);

/// <summary>
/// 파일을 닫아도 남는 실행취소 기록 보관소(<c>&lt;앱 설정 폴더&gt;/Gpixel/History</c>). 문서마다 폴더 하나(이름 = 전체 경로의 해시):
/// <c>entry.json</c>(<see cref="UndoHistoryEntry"/>) + 기록(<see cref="UndoHistoryIO"/>, 조각은 그 폴더 안에만 — 지우면 통째로 사라짐).
/// 기록은 프로젝트 파일의 표시(<see cref="FileStamp"/>)가 쓸 때와 같을 때만 읽는다. <see cref="Cleanup"/>이 원래 파일이 없어졌거나·바뀌었거나·
/// 오래 쓰지 않은 기록과, 전체 용량이 넘치면 오래 안 쓴 기록부터 지운다. 한 번에 한 스레드에서만 쓴다(앱은 작업을 한 줄로 세움).
/// </summary>
public sealed class UndoHistoryStore(string root)
{
    public const string EntryName = "entry.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Root { get; } = root;

    /// <summary>경로 → 폴더 이름(대소문자를 가리지 않은 전체 경로의 SHA-256 앞 32자).</summary>
    public static string KeyOf(string path)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..32];

    private string DirectoryOf(string key) => Path.Combine(Root, key);

    /// <summary>보관 중인 기록(깨진 항목은 뺌).</summary>
    public List<UndoHistoryEntry> Entries()
    {
        var list = new List<UndoHistoryEntry>();
        if (!Directory.Exists(Root)) return list;
        foreach (var dir in Directory.EnumerateDirectories(Root))
            if (ReadEntry(Path.GetFileName(dir)) is { } e) list.Add(e);
        return list;
    }

    /// <summary><paramref name="path"/> 문서의 기록 정보. 없으면 null.</summary>
    public UndoHistoryEntry? Find(string path) => ReadEntry(KeyOf(path));

    private UndoHistoryEntry? ReadEntry(string key)
    {
        try
        {
            var file = Path.Combine(DirectoryOf(key), EntryName);
            return File.Exists(file) && JsonSerializer.Deserialize<UndoHistoryEntry>(File.ReadAllText(file), Options) is { } e && e.Key == key ? e : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void WriteEntry(UndoHistoryEntry e)
        => UndoHistoryIO.WriteAtomic(Path.Combine(DirectoryOf(e.Key), EntryName), JsonSerializer.SerializeToUtf8Bytes(e, Options));

    /// <summary>
    /// <paramref name="path"/> 문서의 기록을 쓴다(<paramref name="stamp"/> = 기록이 맞는 프로젝트 파일 표시). 단계가 없으면 기록을 지우고 null.
    /// </summary>
    public UndoHistoryEntry? Write(UndoHistoryCapture capture, string path, FileStamp stamp, DateTime nowUtc)
    {
        var full = Path.GetFullPath(path);
        var key = KeyOf(full);
        if (capture.StepCount == 0)
        {
            Delete(full);
            return null;
        }
        var result = UndoHistoryIO.Write(capture, DirectoryOf(key));
        var entry = new UndoHistoryEntry(key, full, stamp, result.Steps, result.Bytes, nowUtc);
        WriteEntry(entry);
        return entry;
    }

    /// <summary>
    /// 방금 연 <paramref name="project"/>(경로 = <see cref="SpriteProject.Path"/>)에 남은 기록을 읽는다. 파일 표시가 기록과 다르면(밖에서 바뀜)
    /// 기록을 지우고 <see cref="UndoHistoryLoadStatus.Stale"/>, 읽지 못하면 지우고 <see cref="UndoHistoryLoadStatus.Failed"/>.
    /// 실행취소 단계는 프로젝트의 <see cref="Edit.UndoStack.Capacity"/>까지(오래된 것부터 버림).
    /// </summary>
    public UndoHistoryLoadResult Load(SpriteProject project, DateTime nowUtc)
    {
        if (project.Path is not { } path || Find(path) is not { } entry) return new(UndoHistoryLoadStatus.None);
        if (FileStamp.Of(path) != entry.Stamp)
        {
            Delete(path);
            return new(UndoHistoryLoadStatus.Stale);
        }
        try
        {
            int steps = UndoHistoryIO.Read(project, DirectoryOf(entry.Key));
            WriteEntry(entry with { LastUsedUtc = nowUtc });
            return new(UndoHistoryLoadStatus.Loaded, steps);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Delete(path);
            return new(UndoHistoryLoadStatus.Failed, Error: ex.Message);
        }
    }

    /// <summary>
    /// 앱 안에서 파일 이름을 바꿈·옮김: 기록을 새 경로로 옮기고 표시를 새 파일 것으로 바꾼다(이름을 바꾸며 앱이 내보내기 기록을 고쳐 쓰므로).
    /// 옮겼으면 true.
    /// </summary>
    public bool Move(string oldPath, string newPath)
    {
        if (Find(oldPath) is not { } entry) return false;
        var full = Path.GetFullPath(newPath);
        var key = KeyOf(full);
        if (key == entry.Key)
        {
            WriteEntry(entry with { FilePath = full, Stamp = FileStamp.Of(full) ?? entry.Stamp });
            return true;
        }
        if (FileStamp.Of(full) is not { } stamp)
        {
            Delete(oldPath);
            return false;
        }
        var target = DirectoryOf(key);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.Move(DirectoryOf(entry.Key), target);
        WriteEntry(entry with { Key = key, FilePath = full, Stamp = stamp });
        return true;
    }

    /// <summary><paramref name="path"/> 문서의 기록을 지운다. 지웠으면 true.</summary>
    public bool Delete(string path) => DeleteKey(KeyOf(path));

    private bool DeleteKey(string key)
    {
        var dir = DirectoryOf(key);
        if (!Directory.Exists(dir)) return false;
        try
        {
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 지울 기록(앞에서부터 이유 순): (a) 원래 파일이 없음, (b) 파일 표시가 기록을 쓸 때와 다름(밖에서 바뀜), (c) <see cref="UndoHistoryPolicy.Days"/>일 넘게 쓰지 않음,
    /// (d) 남은 기록의 크기 합이 <see cref="UndoHistoryPolicy.MaxBytes"/>를 넘으면 오래 안 쓴 것부터 넘지 않을 때까지.
    /// </summary>
    /// <param name="stampOf">파일 경로 → 지금 표시(없으면 null).</param>
    public static List<UndoHistoryEntry> SelectPurge(IEnumerable<UndoHistoryEntry> entries, UndoHistoryPolicy policy, DateTime nowUtc,
        Func<string, FileStamp?> stampOf)
    {
        var purge = new List<UndoHistoryEntry>();
        var keep = new List<UndoHistoryEntry>();
        var limit = policy.Days > 0 ? nowUtc.AddDays(-policy.Days) : DateTime.MinValue;
        foreach (var e in entries)
            (stampOf(e.FilePath) is { } stamp && stamp == e.Stamp && e.LastUsedUtc >= limit ? keep : purge).Add(e);
        if (policy.MaxBytes > 0)
        {
            long total = keep.Sum(e => e.Bytes);
            foreach (var e in keep.OrderBy(e => e.LastUsedUtc))
            {
                if (total <= policy.MaxBytes) break;
                purge.Add(e);
                total -= e.Bytes;
            }
        }
        return purge;
    }

    /// <summary>정책(<see cref="SelectPurge"/>)대로 기록을 지우고, 정보 파일이 없거나 깨진 폴더도 지운다. 지운 기록 수.</summary>
    public int Cleanup(UndoHistoryPolicy policy, DateTime nowUtc)
    {
        if (!Directory.Exists(Root)) return 0;
        int removed = 0;
        var entries = new List<UndoHistoryEntry>();
        foreach (var dir in Directory.EnumerateDirectories(Root).ToList())
        {
            var key = Path.GetFileName(dir);
            if (ReadEntry(key) is { } e) entries.Add(e);
            else if (DeleteKey(key)) removed++;
        }
        foreach (var e in SelectPurge(entries, policy, nowUtc, FileStamp.Of))
            if (DeleteKey(e.Key)) removed++;
        return removed;
    }

    /// <summary>지금 쓰는 용량(바이트)과 기록(파일) 수.</summary>
    public (long Bytes, int Files) Usage()
    {
        var entries = Entries();
        return (Directory.Exists(Root) ? UndoHistoryIO.DirectorySize(Root) : 0, entries.Count);
    }

    /// <summary>기록을 모두 지운다.</summary>
    public void Clear()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var dir in Directory.EnumerateDirectories(Root).ToList()) DeleteKey(Path.GetFileName(dir));
    }
}
