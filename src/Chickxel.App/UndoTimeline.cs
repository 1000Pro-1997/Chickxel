using System.Runtime.CompilerServices;
using Chickxel.Core.Edit;

namespace Chickxel.App;

/// <summary>
/// 그림 편집(지금 문서의 실행취소 기록)과 팔레트 변경(앱 설정, 문서와 무관)을 한 줄로 세운다:
/// 실행취소는 둘 중 마지막에 한 것부터, 다시 실행은 마지막에 되돌린 것부터.
/// 단계마다 순번(움직일 때마다 새로 매김)을 붙여 두 목록의 맨 위를 비교한다. 문서 쪽 단계는 기록이 바뀔 때 알아서 매기므로
/// 어디서 되돌리든(메뉴·AI 채팅 단추) 순서가 맞다. 팔레트 기록은 앱을 켜 둔 동안만 남는다(파일을 닫아도 남는 기록에는 넣지 않음).
/// </summary>
internal sealed class UndoTimeline
{
    /// <summary>팔레트 기록을 이만큼만 남긴다.</summary>
    public const int PaletteCapacity = 200;

    public enum Side { None, Document, Palette }

    /// <summary>팔레트 변경 한 단계: 바꾸기 전과 후의 팔레트 목록.</summary>
    public sealed class PaletteStep(IReadOnlyList<NamedPalette> before, IReadOnlyList<NamedPalette> after)
    {
        public IReadOnlyList<NamedPalette> Before { get; } = before;
        public IReadOnlyList<NamedPalette> After { get; } = after;
        public long Stamp { get; set; }
    }

    private long _clock;
    private readonly ConditionalWeakTable<IUndoCommand, StrongBox<long>> _stamps = new();
    private readonly List<PaletteStep> _paletteUndo = new(), _paletteRedo = new();
    private UndoStack? _history;
    private IUndoCommand? _undoTop, _redoTop;

    /// <summary>되돌릴·다시 실행할 것이 바뀜(메뉴 갱신용).</summary>
    public event EventHandler? Changed;

    /// <summary>지금 문서의 실행취소 기록을 따라간다(문서를 바꿀 때마다, 없으면 null). 이미 있던 단계는 팔레트 변경보다 앞선 것으로 본다.</summary>
    public void Track(UndoStack? history)
    {
        if (_history is not null) _history.Changed -= OnHistoryChanged;
        _history = history;
        _undoTop = history?.UndoTop;
        _redoTop = history?.RedoTop;
        if (history is not null) history.Changed += OnHistoryChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        var history = _history!;
        IUndoCommand? undoTop = history.UndoTop, redoTop = history.RedoTop;
        if (redoTop is not null && !ReferenceEquals(redoTop, _redoTop) && ReferenceEquals(redoTop, _undoTop))
            Stamp(redoTop); // 되돌림
        else if (undoTop is not null && !ReferenceEquals(undoTop, _undoTop) && ReferenceEquals(undoTop, _redoTop))
            Stamp(undoTop); // 다시 실행
        else if (undoTop is not null && !ReferenceEquals(undoTop, _undoTop) && !_stamps.TryGetValue(undoTop, out _))
        {
            Stamp(undoTop); // 새 편집: 팔레트 쪽 다시 실행은 버린다
            _paletteRedo.Clear();
        }
        _undoTop = undoTop;
        _redoTop = redoTop;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Stamp(IUndoCommand command) => _stamps.AddOrUpdate(command, new StrongBox<long>(++_clock));

    private long StampOf(IUndoCommand? command)
        => command is not null && _stamps.TryGetValue(command, out var box) ? box.Value : 0;

    /// <summary>팔레트를 바꿨다. 새 편집이므로 팔레트·문서 쪽 다시 실행을 모두 버린다.</summary>
    public void PushPalette(IReadOnlyList<NamedPalette> before, IReadOnlyList<NamedPalette> after)
    {
        _paletteUndo.Add(new PaletteStep(before, after) { Stamp = ++_clock });
        if (_paletteUndo.Count > PaletteCapacity) _paletteUndo.RemoveAt(0);
        _paletteRedo.Clear();
        _history?.ClearRedo();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>실행취소가 되돌릴 쪽. 문서에 열린 묶음이 있으면 문서.</summary>
    public Side NextUndo
    {
        get
        {
            bool document = _history is { CanUndo: true };
            if (_history is { IsGrouping: true } && document) return Side.Document;
            bool palette = _paletteUndo.Count > 0;
            if (document && palette) return _paletteUndo[^1].Stamp > StampOf(_history!.UndoTop) ? Side.Palette : Side.Document;
            return document ? Side.Document : palette ? Side.Palette : Side.None;
        }
    }

    /// <summary>다시 실행할 쪽.</summary>
    public Side NextRedo
    {
        get
        {
            bool document = _history is { CanRedo: true };
            bool palette = _paletteRedo.Count > 0;
            if (document && palette) return _paletteRedo[^1].Stamp > StampOf(_history!.RedoTop) ? Side.Palette : Side.Document;
            return document ? Side.Document : palette ? Side.Palette : Side.None;
        }
    }

    /// <summary>팔레트 쪽 마지막 변경을 되돌릴 목록으로 옮기고 돌려준다(되살릴 것은 <see cref="PaletteStep.Before"/>).</summary>
    public PaletteStep UndoPalette()
    {
        var step = _paletteUndo[^1];
        _paletteUndo.RemoveAt(_paletteUndo.Count - 1);
        step.Stamp = ++_clock;
        _paletteRedo.Add(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return step;
    }

    /// <summary>팔레트 쪽 마지막으로 되돌린 변경을 다시 실행할 목록에서 꺼낸다(되살릴 것은 <see cref="PaletteStep.After"/>).</summary>
    public PaletteStep RedoPalette()
    {
        var step = _paletteRedo[^1];
        _paletteRedo.RemoveAt(_paletteRedo.Count - 1);
        step.Stamp = ++_clock;
        _paletteUndo.Add(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return step;
    }
}
