using Chickxel.Core.Model;

namespace Chickxel.Core.Edit;

/// <summary>실행취소 가능한 동작. 스택에 넣을 때는 이미 적용된 상태여야 한다.</summary>
public interface IUndoCommand
{
    string Label { get; }
    void Undo();
    void Redo();
}

/// <summary>
/// 실행취소/다시 스택(기본 <see cref="DefaultCapacity"/>단계, 최대 <see cref="MaxCapacity"/>). 모든 기록은 <see cref="IUndoCommand"/> 하나의 목록으로 쌓인다:
/// 셀 픽셀 패치(스트로크), 프로젝트 구조 변경(레이어·프레임·태그·캔버스·설정). 둘 다 파일에 남길 수 있다(<see cref="IO.UndoHistoryIO"/>).
/// 프로젝트 편집은 <see cref="SpriteProject"/>가 이 스택에 직접 기록한다.
/// <see cref="BeginGroup"/>~<see cref="EndGroup"/> 사이의 기록은 한 단계로 묶인다(AI 한 번의 응답 등).
/// </summary>
public sealed class UndoStack
{
    public const int DefaultCapacity = 1000;
    public const int MaxCapacity = 10000;

    private readonly List<IUndoCommand> _undo = new();
    private readonly List<IUndoCommand> _redo = new();
    private List<IUndoCommand>? _group;
    private string? _groupLabel;
    private int _capacity;

    public UndoStack(int capacity = DefaultCapacity) => Capacity = capacity;

    /// <summary>실행취소 단계 수(1~<see cref="MaxCapacity"/>로 맞춤). 줄이면 오래된 기록부터 버린다.</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Clamp(value, 1, MaxCapacity);
            if (_undo.Count <= _capacity) return;
            _undo.RemoveRange(0, _undo.Count - _capacity);
            OnChanged();
        }
    }

    /// <summary>스택 내용이 바뀌면 발생(메뉴 활성화 갱신용).</summary>
    public event EventHandler? Changed;

    /// <summary>내용이 바뀔 때마다 늘어나는 번호(기록을 파일에 남긴 뒤 바뀌었는지 볼 때 쓴다).</summary>
    public long Version { get; private set; }

    private void OnChanged()
    {
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool GroupHasItems => _group is { Count: > 0 };

    public bool CanUndo => _undo.Count > 0 || GroupHasItems;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count + (GroupHasItems ? 1 : 0);
    public int RedoCount => _redo.Count;
    public string? UndoLabel => GroupHasItems ? _groupLabel ?? _group![0].Label : _undo.Count > 0 ? _undo[^1].Label : null;
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>다음에 되돌릴 단계(열린 묶음은 제외 — 묶음이 열려 있으면 <see cref="IsGrouping"/>을 볼 것). 없으면 null.</summary>
    public IUndoCommand? UndoTop => _undo.Count > 0 ? _undo[^1] : null;

    /// <summary>다음에 다시 실행할 단계. 없으면 null.</summary>
    public IUndoCommand? RedoTop => _redo.Count > 0 ? _redo[^1] : null;

    /// <summary>묶음이 열려 있는지.</summary>
    public bool IsGrouping => _group is not null;

    /// <summary>이미 적용된 동작을 기록한다. 다시 실행 목록은 지워진다. 묶음이 열려 있으면 묶음에 넣는다.</summary>
    public void Push(IUndoCommand command)
    {
        if (_group is not null) _group.Add(command);
        else
        {
            _undo.Add(command);
            if (_undo.Count > Capacity) _undo.RemoveRange(0, _undo.Count - Capacity);
        }
        _redo.Clear();
        OnChanged();
    }

    /// <summary>묶음이 열려 있는지.</summary>
    public bool InGroup => _group is not null;

    /// <summary>
    /// 이후 기록을 한 단계(<paramref name="label"/>)로 묶기 시작한다. 열린 묶음이 있으면 먼저 닫는다.
    /// <paramref name="label"/>이 null이면 묶음 안 첫 기록의 이름을 쓴다.
    /// </summary>
    public void BeginGroup(string? label)
    {
        EndGroup();
        _group = new List<IUndoCommand>();
        _groupLabel = label;
    }

    /// <summary>묶음을 닫고 한 단계로 기록한다. 기록된 것이 없으면 null. 실행취소·다시 실행도 열린 묶음을 먼저 닫는다.</summary>
    public IUndoCommand? EndGroup()
    {
        if (_group is not { } items) return null;
        _group = null;
        if (items.Count == 0) return null;
        var command = new GroupCommand(_groupLabel ?? items[0].Label, items);
        _undo.Add(command);
        if (_undo.Count > Capacity) _undo.RemoveRange(0, _undo.Count - Capacity);
        OnChanged();
        return command;
    }

    /// <summary><paramref name="command"/>를 실행취소하려면 몇 번 되돌려야 하는지(맨 위 = 1). 없으면 -1.</summary>
    public int UndoDepthOf(IUndoCommand command)
    {
        int i = _undo.LastIndexOf(command);
        return i < 0 ? -1 : _undo.Count - i;
    }

    /// <summary><paramref name="command"/>를 다시 실행하려면 몇 번 다시 해야 하는지(맨 위 = 1). 없으면 -1.</summary>
    public int RedoDepthOf(IUndoCommand command)
    {
        int i = _redo.LastIndexOf(command);
        return i < 0 ? -1 : _redo.Count - i;
    }

    public bool Undo()
    {
        EndGroup();
        if (_undo.Count == 0) return false;
        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo();
        _redo.Add(command);
        OnChanged();
        return true;
    }

    public bool Redo()
    {
        EndGroup();
        if (_redo.Count == 0) return false;
        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Redo();
        _undo.Add(command);
        OnChanged();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _group = null;
        OnChanged();
    }

    /// <summary>다시 실행 목록만 비운다(문서 밖에서 새 편집을 했을 때 — 실행취소 순서를 한 줄로 맞춤). 비어 있으면 아무것도 안 함.</summary>
    public void ClearRedo()
    {
        if (_redo.Count == 0) return;
        _redo.Clear();
        OnChanged();
    }

    /// <summary>파일 저장용 사본: 실행취소 목록(오래된 것부터), 다시 실행 목록(먼 것부터 — 마지막이 다음 다시 실행), 열린 묶음(기록이 있으면 한 단계로).</summary>
    internal (List<IUndoCommand> Undo, List<IUndoCommand> Redo, IUndoCommand? OpenGroup) Contents()
        => (_undo.ToList(), _redo.ToList(), GroupHasItems ? new GroupCommand(_groupLabel ?? _group![0].Label, _group!.ToList()) : null);

    /// <summary>파일에서 읽은 기록으로 바꾼다(실행취소 목록은 <see cref="Capacity"/>를 넘으면 오래된 것부터 버림). 열린 묶음은 닫지 않고 버린다.</summary>
    internal void Load(IEnumerable<IUndoCommand> undo, IEnumerable<IUndoCommand> redo)
    {
        _undo.Clear();
        _undo.AddRange(undo);
        if (_undo.Count > Capacity) _undo.RemoveRange(0, _undo.Count - Capacity);
        _redo.Clear();
        _redo.AddRange(redo);
        _group = null;
        OnChanged();
    }

    /// <summary>묶음: 되돌릴 때는 거꾸로, 다시 할 때는 순서대로.</summary>
    internal sealed class GroupCommand(string label, List<IUndoCommand> items) : IUndoCommand
    {
        public string Label { get; } = label;

        public IReadOnlyList<IUndoCommand> Items => items;

        public void Undo()
        {
            for (int i = items.Count - 1; i >= 0; i--) items[i].Undo();
        }

        public void Redo()
        {
            foreach (var c in items) c.Redo();
        }
    }
}

/// <summary>같은 크기 두 이미지 사이에서 바뀐 픽셀만 담은 패치(위치, 이전 값, 이후 값).</summary>
public sealed class PixelPatch
{
    private readonly int _width, _height;
    private readonly int[] _indices;
    private readonly Color32[] _before, _after;

    private PixelPatch(int width, int height, int[] indices, Color32[] before, Color32[] after)
    {
        _width = width;
        _height = height;
        _indices = indices;
        _before = before;
        _after = after;
    }

    /// <summary>바뀐 픽셀 수.</summary>
    public int Count => _indices.Length;

    /// <summary>파일 저장용: 크기, 바뀐 픽셀 번호(오름차순), 이전 값, 이후 값(그대로 — 고치지 말 것).</summary>
    internal (int Width, int Height, int[] Indices, Color32[] Before, Color32[] After) Parts => (_width, _height, _indices, _before, _after);

    /// <summary>파일에서 읽은 패치. 길이·번호 범위가 맞지 않으면 <see cref="InvalidDataException"/>.</summary>
    internal static PixelPatch FromParts(int width, int height, int[] indices, Color32[] before, Color32[] after)
    {
        if (width < 1 || height < 1 || indices.Length != before.Length || indices.Length != after.Length
            || indices.Any(i => i < 0 || i >= width * height))
            throw new InvalidDataException(Chickxel.Core.Localization.DomainStrings.History_CorruptPixelPatch);
        return new PixelPatch(width, height, indices, before, after);
    }

    /// <summary>두 이미지의 차이. 같으면 null. 크기가 다르면 <see cref="ArgumentException"/>.</summary>
    public static PixelPatch? Create(PixelImage before, PixelImage after)
    {
        if (before.Width != after.Width || before.Height != after.Height)
            throw new ArgumentException(Chickxel.Core.Localization.DomainStrings.Image_DifferentComparisonSize, nameof(after));
        var b = before.Pixels;
        var a = after.Pixels;
        var indices = new List<int>();
        for (int i = 0; i < b.Length; i++)
            if (b[i] != a[i]) indices.Add(i);
        if (indices.Count == 0) return null;
        var idx = indices.ToArray();
        var bv = new Color32[idx.Length];
        var av = new Color32[idx.Length];
        for (int k = 0; k < idx.Length; k++)
        {
            bv[k] = b[idx[k]];
            av[k] = a[idx[k]];
        }
        return new PixelPatch(before.Width, before.Height, idx, bv, av);
    }

    /// <summary>이전 값으로 되돌린다.</summary>
    public void ApplyBefore(PixelImage image) => Apply(image, _before);

    /// <summary>이후 값을 다시 적용한다.</summary>
    public void ApplyAfter(PixelImage image) => Apply(image, _after);

    private void Apply(PixelImage image, Color32[] values)
    {
        if (image.Width != _width || image.Height != _height)
            throw new InvalidOperationException(Chickxel.Core.Localization.DomainStrings.History_PatchSizeMismatch);
        for (int k = 0; k < _indices.Length; k++) image.Pixels[_indices[k]] = values[k];
    }
}
