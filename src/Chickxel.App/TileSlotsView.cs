using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 타일 목록처럼 칸을 늘어놓은 그림 판(폭이 차면 다음 줄): 칸마다 칸 묶음 그림(없으면 아이콘), 아래 색 띠(무리 색 등).
/// 누르면 <see cref="Clicked"/>, 두 번 누르면 <see cref="Activated"/>, 오른쪽 버튼이면 <see cref="MenuRequested"/>.
/// 고른 칸(<see cref="Selected"/>)은 강조색 굵은 테두리(<see cref="SelectionActive"/>가 아니면 옅은 점선), 마우스 올린 칸은 옅은 테두리, 풍선은 칸 설명.
/// </summary>
internal sealed class TileSlotsView : Control
{
    /// <summary>칸 하나: 그림(칸 묶음), 그림이 없을 때 아이콘, 풍선 글, 아래 색 띠(없으면 null).</summary>
    public sealed record Slot(IReadOnlyList<(Point At, TileCell Cell)> Cells, VectorIcon? Icon, string Tip, IBrush? Band = null);

    private static readonly IPen GridPen = AppTheme.Pen(Color.FromArgb(40, 255, 255, 255));
    private static readonly IPen SelectionPen = AppTheme.Pen(AppTheme.AccentColor, 3);
    private static readonly IPen SelectionOutlinePen = AppTheme.Pen(Color.FromArgb(220, 10, 10, 12), 1);
    private static readonly IPen InactiveSelectionPen = AppTheme.Pen(Color.FromArgb(160, AppTheme.DimTextColor.R, AppTheme.DimTextColor.G, AppTheme.DimTextColor.B), 1.5,
        new Avalonia.Media.Immutable.ImmutableDashStyle([3.0, 2.0], 0));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(50, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
    private static readonly IPen HoverPen = AppTheme.Pen(Color.FromArgb(200, 255, 255, 255), 1.5);

    private Tileset? _tileset;
    private IReadOnlyList<Slot> _slots = [];
    private int _selected = -1;
    private bool _selectionActive = true;
    private int _hover = -1;
    private long _timeMs;
    private int _columns = 1;

    /// <summary>칸 한 변(화면 px).</summary>
    public double SlotSize { get; set; } = 32;

    /// <summary>칸을 누름(칸 번호).</summary>
    public event Action<int>? Clicked;
    /// <summary>칸을 두 번 누름(칸 번호).</summary>
    public event Action<int>? Activated;
    /// <summary>칸 위 오른쪽 버튼(칸 번호).</summary>
    public event Action<int, PointerPressedEventArgs>? MenuRequested;

    public Tileset? Tileset
    {
        get => _tileset;
        set { _tileset = value; InvalidateVisual(); }
    }

    public IReadOnlyList<Slot> Slots
    {
        get => _slots;
        set
        {
            _slots = value;
            _hover = -1;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>고른 칸 번호(없으면 -1).</summary>
    public int Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; InvalidateVisual(); }
    }

    /// <summary>고른 칸이 지금 찍을 것인지(아니면 옅은 점선).</summary>
    public bool SelectionActive
    {
        get => _selectionActive;
        set { if (_selectionActive == value) return; _selectionActive = value; InvalidateVisual(); }
    }

    /// <summary>움직이는 타일을 그릴 시각(ms). 보이는 그림이 바뀌면 다시 그린다.</summary>
    public long TimeMs
    {
        get => _timeMs;
        set
        {
            if (_timeMs == value) return;
            bool redraw = _tileset is { HasAnimation: true } ts && ts.AnimationPhase(_timeMs) != ts.AnimationPhase(value);
            _timeMs = value;
            if (redraw) InvalidateVisual();
        }
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        int cols = ColumnsFor(availableSize.Width);
        int rows = Math.Max(1, (_slots.Count + cols - 1) / cols);
        return new(cols * SlotSize + 1, rows * SlotSize + 1);
    }

    protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
    {
        _columns = ColumnsFor(finalSize.Width);
        return base.ArrangeOverride(finalSize);
    }

    private int ColumnsFor(double width)
        => double.IsFinite(width) && width > 0 ? Math.Max(1, (int)Math.Floor((width - 1) / SlotSize)) : Math.Max(1, _slots.Count);

    private Rect SlotRect(int index) => new(index % _columns * SlotSize, index / _columns * SlotSize, SlotSize, SlotSize);

    private int SlotAt(Avalonia.Point p)
    {
        int x = (int)Math.Floor(p.X / SlotSize), y = (int)Math.Floor(p.Y / SlotSize);
        if (x < 0 || y < 0 || x >= _columns) return -1;
        int index = y * _columns + x;
        return index < _slots.Count ? index : -1;
    }

    public override void Render(DrawingContext g)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            var r = SlotRect(i);
            AppTheme.DrawChecker(g, r, Math.Max(4, SlotSize / 4));
            if (_tileset is { } ts && slot.Cells.Count > 0) TileVisuals.DrawCells(g, ts, slot.Cells, r.Deflate(2), _timeMs);
            else if (slot.Icon is { } icon)
            {
                g.FillRectangle(AppTheme.InputBrush, r.Deflate(1));
                VectorIcons.Draw(g, icon, r, Math.Round(SlotSize * 0.5), AppTheme.TextBrush);
            }
            if (slot.Band is { } band) g.FillRectangle(band, new Rect(r.X + 2, r.Bottom - 5, r.Width - 4, 3));
            g.DrawRectangle(null, GridPen, r.Deflate(0.5));
        }
        if (_hover >= 0 && _hover < _slots.Count && _hover != _selected) g.DrawRectangle(null, HoverPen, SlotRect(_hover).Deflate(1.5));
        if (_selected >= 0 && _selected < _slots.Count)
        {
            var r = SlotRect(_selected);
            if (_selectionActive)
            {
                g.FillRectangle(SelectionBrush, r);
                g.DrawRectangle(null, SelectionOutlinePen, r.Deflate(0.5));
                g.DrawRectangle(null, SelectionPen, r.Deflate(2));
                g.DrawRectangle(null, SelectionOutlinePen, r.Deflate(4));
            }
            else g.DrawRectangle(null, InactiveSelectionPen, r.Deflate(1.5));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        int index = SlotAt(pt.Position);
        if (index < 0) return;
        e.Handled = true;
        if (pt.Properties.IsRightButtonPressed)
        {
            MenuRequested?.Invoke(index, e);
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) Activated?.Invoke(index);
        else Clicked?.Invoke(index);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHover(SlotAt(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(-1);
    }

    private void SetHover(int index)
    {
        if (_hover == index) return;
        _hover = index;
        InvalidateVisual();
        ToolTip.SetTip(this, index >= 0 ? _slots[index].Tip : null);
    }
}
