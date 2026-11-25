namespace Chickxel.App;

// 튜토리얼이 비추는 타임라인 칸(격자와 그 안 사각형 — TutorialStep.TargetArea·DropTarget)과 넘어갈 조건.
public sealed partial class TimelinePanel
{
    /// <summary>레이어 <paramref name="layer"/>의 프레임 <paramref name="from"/>~<paramref name="to"/> 칸들(없는 칸이면 null).</summary>
    internal (Control Owner, Rect Area)? TutorialCels(int layer, int from, int to)
        => P is { } p && layer >= 0 && layer < p.Layers.Count && from >= 0 && to < p.Frames.Count && from <= to
            ? (_grid, _grid.CelRect(layer, from).Union(_grid.CelRect(layer, to)))
            : null;

    /// <summary>레이어 <paramref name="layer"/>의 프레임 <paramref name="from"/>~<paramref name="to"/>를 셀 묶음으로 골랐는지.</summary>
    internal bool TutorialCelsSelected(int layer, int from, int to) => _grid.CelsSelected(layer, from, to);

    /// <summary>레이어 <paramref name="layer"/>의 이름 칸(없는 레이어면 null).</summary>
    internal (Control Owner, Rect Area)? TutorialLayerName(int layer)
        => P is { } p && layer >= 0 && layer < p.Layers.Count ? (_grid, _grid.LayerNameRect(layer)) : null;

    /// <summary>레이어 <paramref name="layer"/>의 자물쇠 단추(없는 레이어면 null).</summary>
    internal (Control Owner, Rect Area)? TutorialLock(int layer)
        => P is { } p && layer >= 0 && layer < p.Layers.Count ? (_grid, _grid.LockRect(layer)) : null;

    /// <summary>"다음 레이어" 줄의 ＋(새 레이어) 칸(문서가 없으면 null).</summary>
    internal (Control Owner, Rect Area)? TutorialAddLayer() => P is null ? null : (_grid, _grid.AddLayerRect());

    /// <summary>"다음 프레임" 머리글의 ＋(빈 프레임) 칸(문서가 없으면 null).</summary>
    internal (Control Owner, Rect Area)? TutorialAddFrame() => P is null ? null : (_grid, _grid.AddFrameRect());

    /// <summary>프레임 번호 머리글 줄(문서가 없으면 null).</summary>
    internal (Control Owner, Rect Area)? TutorialFrameHeaders() => P is null ? null : (_grid, _grid.FrameHeadersRect());
}
