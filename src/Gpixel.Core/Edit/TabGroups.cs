namespace Gpixel.Core.Edit;

/// <summary>
/// 편집기 탭 묶음(VS Code 편집기 그룹처럼, 최대 <see cref="MaxGroups"/>개 — 왼쪽·오른쪽): 묶음마다 탭 순서와 고른 탭, 포커스된 묶음.
/// 탭을 닫아 묶음이 비면 그 묶음을 없앤다(나눔 해제). 화면과 무관한 순서·선택 규칙만 다룬다.
/// </summary>
public sealed class TabGroups<T> where T : class
{
    public const int MaxGroups = 2;

    private readonly List<List<T>> _groups = [new()];
    private readonly List<int> _active = [-1];

    /// <summary>묶음 수(1 = 나누지 않음, 2 = 좌우로 나눔).</summary>
    public int GroupCount => _groups.Count;

    public bool IsSplit => _groups.Count > 1;

    /// <summary>포커스된 묶음 번호(이 묶음의 고른 탭이 편집 대상).</summary>
    public int Focused { get; private set; }

    /// <summary>탭이 하나도 없는지.</summary>
    public bool IsEmpty => _groups.All(g => g.Count == 0);

    /// <summary>모든 탭(묶음 순서, 묶음 안에서는 탭 순서).</summary>
    public IEnumerable<T> All => _groups.SelectMany(g => g);

    public IReadOnlyList<T> Tabs(int group) => _groups[group];

    /// <summary>묶음의 고른 탭 번호(빈 묶음 −1).</summary>
    public int ActiveIndex(int group) => _active[group];

    /// <summary>묶음의 고른 탭(빈 묶음 null).</summary>
    public T? Active(int group) => _active[group] is var i and >= 0 ? _groups[group][i] : null;

    /// <summary>포커스된 묶음의 고른 탭(편집 대상, 없으면 null).</summary>
    public T? FocusedActive => Active(Focused);

    /// <summary>탭이 든 묶음 번호(없으면 −1).</summary>
    public int GroupOf(T tab)
    {
        for (int g = 0; g < _groups.Count; g++)
            if (_groups[g].Contains(tab)) return g;
        return -1;
    }

    /// <summary>
    /// 탭을 묶음에 넣고 고르며 그 묶음에 포커스를 준다. <paramref name="index"/>가 없으면 고른 탭 바로 뒤(빈 묶음이면 처음).
    /// <paramref name="group"/>이 지금 묶음 수와 같으면(최대 묶음 수 안에서) 새 묶음을 오른쪽에 만든다. 이미 열린 탭이면 고르기만 한다.
    /// </summary>
    public void Add(T tab, int group, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (Activate(tab)) return;
        EnsureGroup(group);
        var list = _groups[group];
        int at = Math.Clamp(index ?? _active[group] + 1, 0, list.Count);
        list.Insert(at, tab);
        _active[group] = at;
        Focused = group;
    }

    /// <summary>탭을 그 묶음의 고른 탭으로 하고 그 묶음에 포커스를 준다. 없는 탭이면 false.</summary>
    public bool Activate(T tab)
    {
        int g = GroupOf(tab);
        if (g < 0) return false;
        _active[g] = _groups[g].IndexOf(tab);
        Focused = g;
        return true;
    }

    /// <summary>묶음에 포커스를 준다(없는 묶음이면 그대로).</summary>
    public void Focus(int group)
    {
        if (group >= 0 && group < _groups.Count) Focused = group;
    }

    /// <summary>
    /// 탭을 닫는다. 고른 탭이었으면 오른쪽(없으면 왼쪽) 이웃을 고른다. 나눈 상태에서 묶음이 비면 그 묶음을 없애고(나눔 해제)
    /// 남은 묶음에 포커스를 준다. 나눔을 풀었으면 true.
    /// </summary>
    public bool Remove(T tab)
    {
        int g = GroupOf(tab);
        if (g < 0) return false;
        var list = _groups[g];
        int i = list.IndexOf(tab);
        list.RemoveAt(i);
        if (_active[g] > i || _active[g] >= list.Count) _active[g]--;
        if (list.Count > 0 && _active[g] < 0) _active[g] = 0;
        if (list.Count > 0 || _groups.Count == 1)
        {
            if (list.Count == 0) _active[g] = -1;
            return false;
        }
        _groups.RemoveAt(g);
        _active.RemoveAt(g);
        Focused = 0;
        return true;
    }

    /// <summary>묶음 안에서 탭 순서를 바꾼다(<paramref name="from"/>번 탭이 <paramref name="to"/>번 자리로). 고른 탭은 그대로 따라간다.</summary>
    public void Move(int group, int from, int to)
    {
        var list = _groups[group];
        if (from < 0 || from >= list.Count) return;
        to = Math.Clamp(to, 0, list.Count - 1);
        if (from == to) return;
        var active = Active(group);
        var tab = list[from];
        list.RemoveAt(from);
        list.Insert(to, tab);
        if (active is not null) _active[group] = list.IndexOf(active);
    }

    /// <summary>
    /// 탭을 다른 묶음 끝으로 옮겨 고르고 그 묶음에 포커스를 준다(<paramref name="target"/>이 묶음 수와 같으면 새 묶음을 만들어 나눔).
    /// 원래 묶음이 비면 없앤다. 같은 묶음이거나 옮길 수 없으면 false.
    /// </summary>
    public bool MoveToGroup(T tab, int target)
    {
        int g = GroupOf(tab);
        if (g < 0 || g == target || target < 0 || target > _groups.Count || target >= MaxGroups) return false;
        // 하나뿐인 묶음의 마지막 탭을 새 묶음으로 옮기면 그대로다.
        if (target == _groups.Count && _groups[g].Count == 1) return false;
        EnsureGroup(target);
        var moving = _groups[target];
        Remove(tab);
        // 앞 묶음이 비어 없어졌으면 대상 묶음 번호가 하나 당겨진다.
        int t = _groups.IndexOf(moving);
        _groups[t].Add(tab);
        _active[t] = _groups[t].Count - 1;
        Focused = t;
        return true;
    }

    /// <summary>
    /// 묶음의 다른 쪽 묶음 번호: 나눈 상태면 반대쪽, 아니면 새로 만들 오른쪽 묶음 번호(=1, 아직 만들지 않음 — <see cref="Add"/>가 만든다).
    /// </summary>
    public int OtherGroup(int group) => IsSplit ? 1 - group : 1;

    /// <summary>포커스된 묶음에서 <paramref name="delta"/>만큼 돌아가며 다음 탭을 고른다(끝 다음은 처음). 고른 탭(없으면 null).</summary>
    public T? Cycle(int delta)
    {
        var list = _groups[Focused];
        if (list.Count == 0) return null;
        int n = list.Count;
        _active[Focused] = ((_active[Focused] + Math.Sign(delta)) % n + n) % n;
        return FocusedActive;
    }

    /// <summary>모든 탭을 닫는다(나눔 해제).</summary>
    public void Clear()
    {
        _groups.Clear();
        _active.Clear();
        _groups.Add(new());
        _active.Add(-1);
        Focused = 0;
    }

    /// <summary>
    /// 다시 열 수 있게 저장할 모양: 묶음마다 탭 경로(<paramref name="pathOf"/>가 null이면 — 저장한 적 없는 새 문서 — 뺀다), 고른 탭, 포커스된 묶음.
    /// 탭이 모두 빠진 묶음은 없앤다. 열린 탭이 없으면 null.
    /// </summary>
    public TabSession? ToSession(Func<T, string?> pathOf)
    {
        var session = new TabSession();
        int focused = 0;
        for (int g = 0; g < _groups.Count; g++)
        {
            var paths = new List<string>();
            int active = -1;
            for (int i = 0; i < _groups[g].Count; i++)
            {
                if (pathOf(_groups[g][i]) is not { Length: > 0 } path) continue;
                // 고른 탭이 빠지면 그 앞 탭(없으면 처음)을 고른다.
                if (i <= _active[g]) active = paths.Count;
                paths.Add(path);
            }
            if (paths.Count == 0) continue;
            if (g == Focused) focused = session.Groups.Count;
            session.Groups.Add(paths);
            session.Active.Add(Math.Max(0, active));
        }
        if (session.Groups.Count == 0) return null;
        session.Focused = focused;
        return session;
    }

    private void EnsureGroup(int group)
    {
        if (group < 0 || group >= MaxGroups || group > _groups.Count) throw new ArgumentOutOfRangeException(nameof(group));
        if (group < _groups.Count) return;
        _groups.Add(new());
        _active.Add(-1);
    }
}

/// <summary>
/// 다음에 켤 때 다시 열 탭(앱 설정에 저장): 묶음마다 파일 경로, 묶음마다 고른 탭 번호, 포커스된 묶음.
/// 읽은 값이 어긋나도(손으로 고침) <see cref="Normalized"/>가 맞춘다.
/// </summary>
public sealed class TabSession
{
    public List<List<string>> Groups { get; set; } = new();
    public List<int> Active { get; set; } = new();
    public int Focused { get; set; }

    /// <summary>
    /// 빈 경로·빈 묶음을 빼고(묶음 최대 <see cref="TabGroups{T}.MaxGroups"/>개), 고른 탭·포커스 번호를 범위 안으로 맞춘 사본.
    /// 묶음 안 같은 경로는 한 번만 둔다.
    /// </summary>
    public TabSession Normalized()
    {
        var result = new TabSession();
        int focused = 0;
        for (int g = 0; g < (Groups?.Count ?? 0) && result.Groups.Count < TabGroups<object>.MaxGroups; g++)
        {
            var paths = (Groups![g] ?? new()).Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count == 0) continue;
            if (g == Focused) focused = result.Groups.Count;
            int active = Active is not null && g < Active.Count ? Active[g] : 0;
            result.Groups.Add(paths);
            result.Active.Add(Math.Clamp(active, 0, paths.Count - 1));
        }
        result.Focused = result.Groups.Count == 0 ? 0 : Math.Clamp(focused, 0, result.Groups.Count - 1);
        return result;
    }
}
