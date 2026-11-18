using Avalonia.Data;
using Avalonia.VisualTree;

namespace Chickxel.App;

/// <summary>
/// 풍선 도움말 자리: 마우스 자리가 아니라 대상 컨트롤 가장자리에 붙여 띄워, 같은 컨트롤이면 늘 같은 곳에 뜬다.
/// 위아래로 늘어선 컨트롤(도구 막대·설정 줄)은 옆에, 옆으로 늘어선 컨트롤(단추 줄)은 위나 아래에 띄워 이어서 훑는 이웃 컨트롤을 가리지 않는다.
/// 옆이면 창 가운데 쪽(창 오른쪽 절반의 컨트롤은 왼쪽), 위아래면 창 가운데 쪽(아래 절반의 컨트롤은 위)으로 띄운다.
/// 칸마다 풍선이 다른 직접 그리는 컨트롤(색 칸 격자 등)은 <see cref="FollowPointer"/>로 마우스 자리를 지킨다
/// (직접 준 값이 여기서 스타일 우선순위로 주는 값보다 앞섬). 큰 컨트롤(가로·세로 모두 <see cref="LargeSide"/> 넘음)도 마우스 자리.
/// </summary>
public static class TipPlacement
{
    /// <summary>가로·세로가 모두 이보다 크면 가장자리가 마우스에서 멀어 마우스 자리에 띄운다.</summary>
    private const double LargeSide = 160;
    /// <summary>마우스 자리 풍선의 세로 간격(Avalonia 기본값 — 커서에 가리지 않게).</summary>
    private const double PointerOffset = 20;
    /// <summary>컨트롤 가장자리와 풍선 사이 간격.</summary>
    private const double Gap = 4;
    /// <summary>이웃 컨트롤을 찾으며 올라갈 부모 수(감싸는 Border·ContentPresenter 등을 지나 늘어선 묶음까지).</summary>
    private const int MaxDepth = 6;

    [Flags]
    private enum Side { None = 0, Left = 1, Right = 2, Above = 4, Below = 8, Beside = Left | Right, Stacked = Above | Below }

    /// <summary>모든 컨트롤의 풍선이 열릴 때 자리를 정하게 한다(앱 시작 때 한 번).</summary>
    public static void Install() =>
        ToolTip.ToolTipOpeningEvent.AddClassHandler<Control>((control, _) => Place(control));

    /// <summary>칸마다 풍선이 다른 직접 그리는 컨트롤: 풍선을 가장자리가 아니라 마우스 자리에 띄운다(생성자에서).</summary>
    public static void FollowPointer(Control control) => ToolTip.SetPlacement(control, PlacementMode.Pointer);

    private static void Place(Control control)
    {
        control.SetValue(ToolTip.PlacementProperty, Choose(control), BindingPriority.Style);
        // 직접 준 자리(FollowPointer)가 앞서므로 간격은 실제 자리로 정한다.
        var (h, v) = control.GetValue(ToolTip.PlacementProperty) switch
        {
            PlacementMode.Pointer => (0d, PointerOffset),
            PlacementMode.Left => (-Gap, 0d),
            PlacementMode.Right => (Gap, 0d),
            PlacementMode.Top => (0d, -Gap),
            _ => (0d, Gap),
        };
        control.SetValue(ToolTip.HorizontalOffsetProperty, h, BindingPriority.Style);
        control.SetValue(ToolTip.VerticalOffsetProperty, v, BindingPriority.Style);
    }

    private static PlacementMode Choose(Control control)
    {
        var size = control.Bounds.Size;
        if (size.Width > LargeSide && size.Height > LargeSide) return PlacementMode.Pointer;
        if (TopLevel.GetTopLevel(control) is not { } top
            || control.TranslatePoint(new Avalonia.Point(size.Width / 2, size.Height / 2), top) is not { } center)
            return PlacementMode.Pointer;
        bool rightHalf = center.X > top.Bounds.Width / 2, lowerHalf = center.Y > top.Bounds.Height / 2;
        var (side, otherSide) = rightHalf ? (Side.Left, Side.Right) : (Side.Right, Side.Left);
        var (vertical, otherVertical) = lowerHalf ? (Side.Above, Side.Below) : (Side.Below, Side.Above);

        var near = Neighbors(control);
        if ((near & Side.Stacked) != 0 && (near & Side.Beside) == 0) return Mode(side);
        if ((near & Side.Stacked) == 0) return Mode(vertical);
        // 격자(옆과 위아래 모두 이웃): 이웃이 없는 쪽, 옆 먼저
        foreach (var free in new[] { side, otherSide, vertical, otherVertical })
            if ((near & free) == 0) return Mode(free);
        return Mode(vertical);
    }

    private static PlacementMode Mode(Side side) => side switch
    {
        Side.Left => PlacementMode.Left,
        Side.Right => PlacementMode.Right,
        Side.Above => PlacementMode.Top,
        _ => PlacementMode.Bottom,
    };

    /// <summary>
    /// 컨트롤(또는 그것만 감싼 부모)과 같은 부모 안에서 옆(세로 범위가 겹침)·위아래(가로 범위가 겹침)에 놓인 이웃의 방향.
    /// 겹쳐 놓인 것(덧그림 층)과 대각선에 놓인 것은 세지 않는다. 이웃이 없으면 한 단계 위 부모에서 찾는다.
    /// </summary>
    private static Side Neighbors(Control control)
    {
        Visual item = control;
        for (int depth = 0; depth < MaxDepth && item.GetVisualParent() is { } parent and not TopLevel; depth++, item = parent)
        {
            var r = item.Bounds;
            var found = Side.None;
            foreach (var other in parent.GetVisualChildren())
            {
                if (other == item || !other.IsVisible) continue;
                var s = other.Bounds;
                if (s.Width <= 0 || s.Height <= 0) continue;
                bool sameRow = s.Top < r.Bottom && r.Top < s.Bottom;
                bool sameColumn = s.Left < r.Right && r.Left < s.Right;
                if (sameRow == sameColumn) continue;
                found |= sameRow
                    ? s.Center.X < r.Center.X ? Side.Left : Side.Right
                    : s.Center.Y < r.Center.Y ? Side.Above : Side.Below;
            }
            if (found != Side.None) return found;
        }
        return Side.None;
    }
}
