using System.Runtime.CompilerServices;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>
/// 파일을 닫아도 남는 실행취소 기록(<see cref="UndoHistoryStore"/>, <c>&lt;설정 폴더&gt;/Gpixel/History</c> — 환경 변수 <see cref="RootVariable"/>로 바꿀 수 있음)을
/// 문서 열기·저장·닫기에 잇는다. 기록은 늘 "프로젝트 파일에 저장된 상태" 기준으로만 쓴다: 저장한 직후, 그리고 수정 없는(저장한 그대로인) 문서를 닫거나
/// 다른 문서로 바꾸거나 끝낼 때(파일이 그사이 밖에서 바뀌지 않았으면). 저장하지 않은 수정은 쓰지 않으므로 "저장 안 함"으로 버린 상태를 가리키는 기록은 생기지 않는다
/// (그 파일의 기록은 마지막으로 저장할 때 쓴 것 그대로). 쓰기·정리는 작업 한 줄(백그라운드)로 차례대로 하고, 읽기 전에는 그 줄이 끝나기를 기다린다.
/// </summary>
public sealed class UndoHistoryKeeper
{
    /// <summary>기록 폴더를 바꾸는 환경 변수(시험용).</summary>
    public const string RootVariable = "GPIXEL_HISTORY_DIR";

    private readonly AppSettings _settings;
    private readonly object _lock = new();
    private Task _tail = Task.CompletedTask;
    private readonly ConditionalWeakTable<SpriteProject, DocState> _docs = new();

    /// <summary>문서마다: 지금 상태와 같은 프로젝트 파일의 경로·표시(열거나 저장할 때), 마지막으로 기록을 쓸 때(또는 읽은 뒤)의 기록 번호.</summary>
    private sealed class DocState
    {
        public string? Path;
        public FileStamp? Stamp;
        public long WrittenVersion = -1;
    }

    public UndoHistoryKeeper(AppSettings settings, string? root = null)
    {
        _settings = settings;
        Store = new UndoHistoryStore(root ?? DefaultRoot);
    }

    public static string DefaultRoot => Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } dir
        ? dir
        : Path.Combine(Platform.AppDataDirectory, "Gpixel", "History");

    public UndoHistoryStore Store { get; }

    public bool Enabled => _settings.UndoHistoryEnabled;

    /// <summary>백그라운드 작업 실패(상태 표시줄 알림용, 작업 스레드에서 발생).</summary>
    public event Action<string>? Failed;

    private DocState State(SpriteProject p) => _docs.GetValue(p, _ => new DocState());

    private void Enqueue(Action work)
    {
        lock (_lock)
            _tail = _tail.ContinueWith(_ =>
            {
                try { work(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
                {
                    Failed?.Invoke(ex.Message);
                }
            }, TaskScheduler.Default);
    }

    /// <summary>기다리던 쓰기·정리가 끝날 때까지 기다린다. 시간 안에 끝났으면 true.</summary>
    public bool Flush(TimeSpan? timeout = null)
    {
        Task tail;
        lock (_lock) tail = _tail;
        return tail.Wait(timeout ?? Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// 방금 디스크에서 연 문서(<see cref="SpriteProject.Path"/>의 <c>.gpixel</c>)에 남은 기록을 읽는다(켜져 있을 때, 실행취소 횟수까지).
    /// 상태 표시줄에 덧붙일 문구(" — …")를 돌려준다. 알릴 것이 없으면 null.
    /// </summary>
    public string? Restore(SpriteProject p)
    {
        if (p.Path is not { } path || !File.Exists(path)) return null;
        var state = State(p);
        state.Path = Path.GetFullPath(path);
        state.Stamp = FileStamp.Of(path);
        state.WrittenVersion = p.History.Version;
        if (!Enabled) return null;
        Flush();
        p.History.Capacity = _settings.UndoSteps;
        var result = Store.Load(p, DateTime.UtcNow);
        state.WrittenVersion = p.History.Version;
        return result.Status switch
        {
            UndoHistoryLoadStatus.Loaded => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UndoHistoryKeeper_Restore_50F9884DE9,result.Steps),
            UndoHistoryLoadStatus.Stale => UiStrings.S_UndoHistoryKeeper_Restore_9182EA34DA,
            UndoHistoryLoadStatus.Failed => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UndoHistoryKeeper_Restore_3476961A46,result.Error),
            _ => null,
        };
    }

    /// <summary>문서를 저장했다(지금 상태 = 파일): 기록을 쓴다(켜져 있을 때).</summary>
    public void Saved(SpriteProject p)
    {
        if (p.Path is not { } path) return;
        var state = State(p);
        state.Path = Path.GetFullPath(path);
        state.Stamp = FileStamp.Of(path);
        if (Enabled && state.Stamp is not null) Write(p, state);
    }

    /// <summary>
    /// 문서를 닫거나 다른 문서로 바꾸거나 끝낸다: 저장한 그대로이고(수정 없음) 파일이 그사이 바뀌지 않았으며 마지막으로 쓴 뒤 기록이 바뀌었으면 쓴다.
    /// 수정된 문서는 쓰지 않는다(마지막으로 저장할 때 쓴 기록이 파일과 맞음).
    /// </summary>
    public void Leaving(SpriteProject p)
    {
        if (!Enabled || p.IsDirty || p.Path is not { } path || !_docs.TryGetValue(p, out var state)) return;
        if (!string.Equals(state.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) || state.Stamp is null
            || FileStamp.Of(path) != state.Stamp || state.WrittenVersion == p.History.Version)
            return;
        Write(p, state);
    }

    private void Write(SpriteProject p, DocState state)
    {
        if (UndoHistoryIO.Capture(p, _settings.UndoSteps) is not { } capture) return;
        state.WrittenVersion = p.History.Version;
        var (path, stamp, policy) = (state.Path!, state.Stamp!.Value, _settings.UndoHistoryPolicy);
        Enqueue(() =>
        {
            var now = DateTime.UtcNow;
            Store.Write(capture, path, stamp, now);
            Store.Cleanup(policy, now);
        });
    }

    /// <summary>앱 안에서 파일 이름을 바꿈: 기록을 새 경로로 옮기고, 그 파일을 연 문서의 파일 표시를 새로 잰다(이름을 바꾸며 앱이 파일을 고쳐 씀).</summary>
    public void Renamed(string oldPath, string newPath, IEnumerable<SpriteProject> open)
    {
        foreach (var p in open)
            if (p.Path is { } path && string.Equals(path, newPath, StringComparison.OrdinalIgnoreCase) && _docs.TryGetValue(p, out var state)
                && string.Equals(state.Path, Path.GetFullPath(oldPath), StringComparison.OrdinalIgnoreCase))
            {
                state.Path = Path.GetFullPath(newPath);
                state.Stamp = FileStamp.Of(newPath);
            }
        Enqueue(() => Store.Move(oldPath, newPath));
    }

    /// <summary>앱 안에서 파일을 (앱 휴지통으로) 지움: 그 파일의 기록도 지운다(휴지통에서 되살려도 기록은 없음).</summary>
    public void Deleted(string path) => Enqueue(() => Store.Delete(path));

    /// <summary>정책대로 정리한다(시작할 때·정책을 바꿨을 때, 백그라운드).</summary>
    public void Cleanup() => Enqueue(() => Store.Cleanup(_settings.UndoHistoryPolicy, DateTime.UtcNow));

    /// <summary>지금 쓰는 용량과 기록 수(기다리던 작업이 끝난 뒤).</summary>
    public (long Bytes, int Files) Usage()
    {
        Flush();
        return Store.Usage();
    }

    /// <summary>"지금 12.3 MB · 파일 8개".</summary>
    public string DescribeUsage()
    {
        var (bytes, files) = Usage();
        return files == 0 ? UiStrings.S_UndoHistoryKeeper_DescribeUsage_D8C7803DCD : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UndoHistoryKeeper_DescribeUsage_4F87DAB40A,AppTrash.FormatSize(bytes),files);
    }

    /// <summary>남긴 기록을 모두 지운다(열린 문서의 실행취소 기록은 그대로 — 다음에 저장하면 다시 씀).</summary>
    public void Clear()
    {
        Flush();
        Store.Clear();
    }
}
