using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>실행취소 가능한 동작. 스택에 넣을 때는 이미 적용된 상태여야 한다.</summary>
public interface IUndoCommand
{
    string Label { get; }
    void Undo();
    void Redo();
}

/// <summary>
/// 실행취소/다시 스택(기본 100단계). 모든 기록은 <see cref="IUndoCommand"/> 하나의 목록으로 쌓인다:
/// 셀 픽셀 패치(스트로크), 프로젝트 구조 변경(레이어·프레임·태그·캔버스·설정), 임의 동작.
/// 프로젝트 편집은 <see cref="SpriteProject"/>가 이 스택에 직접 기록한다.
/// </summary>
public sealed class UndoStack
{
    public const int DefaultCapacity = 100;

    private readonly List<IUndoCommand> _undo = new();
    private readonly List<IUndoCommand> _redo = new();

    public UndoStack(int capacity = DefaultCapacity) => Capacity = Math.Max(1, capacity);

    public int Capacity { get; }

    /// <summary>스택 내용이 바뀌면 발생(메뉴 활성화 갱신용).</summary>
    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>이미 적용된 동작을 기록한다. 다시 실행 목록은 지워진다.</summary>
    public void Push(IUndoCommand command)
    {
        _undo.Add(command);
        if (_undo.Count > Capacity) _undo.RemoveRange(0, _undo.Count - Capacity);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>이미 적용된 임의 동작을 기록한다(실행취소/다시 때 각 함수가 불림).</summary>
    public void RecordAction(string label, Action undo, Action redo) => Push(new ActionCommand(label, undo, redo));

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo();
        _redo.Add(command);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Redo();
        _undo.Add(command);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ActionCommand(string label, Action undo, Action redo) : IUndoCommand
    {
        public string Label { get; } = label;
        public void Undo() => undo();
        public void Redo() => redo();
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

    /// <summary>두 이미지의 차이. 같으면 null. 크기가 다르면 <see cref="ArgumentException"/>.</summary>
    public static PixelPatch? Create(PixelImage before, PixelImage after)
    {
        if (before.Width != after.Width || before.Height != after.Height)
            throw new ArgumentException("크기가 같은 이미지만 비교할 수 있습니다.", nameof(after));
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
            throw new InvalidOperationException("패치와 이미지 크기가 다릅니다.");
        for (int k = 0; k < _indices.Length; k++) image.Pixels[_indices[k]] = values[k];
    }
}
