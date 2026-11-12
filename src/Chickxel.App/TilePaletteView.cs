using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 타일 목록: 타일셋의 타일을 목록 자리(<see cref="TileDef.PaletteX"/>, <see cref="TileDef.PaletteY"/>)에 칸으로 보인다(큰 타일은 칸에 맞춰 줄임).
/// 누르면 그 칸, Shift+누르기·끌기면 칸 사각형을 고른다. 두 번 누르면 <see cref="TileActivated"/>, 오른쪽 버튼이면 <see cref="TileMenuRequested"/>.
/// 고른 칸은 강조색 굵은 테두리, 마우스 올린 칸은 옅은 테두리, <see cref="HighlightTiles"/>는 옅게 칠하고, 움직이는 타일은 <see cref="TimeMs"/>의 그림과 작은 재생 표시로 그린다.
/// </summary>
public sealed class TilePaletteView : Control
{
    private static readonly IPen GridPen = AppTheme.Pen(Color.FromArgb(40, 255, 255, 255));
    private static readonly IPen SelectionPen = AppTheme.Pen(AppTheme.AccentColor, 3);
    private static readonly IPen SelectionOutlinePen = AppTheme.Pen(Color.FromArgb(220, 10, 10, 12), 1);
    private static readonly IPen InactiveSelectionPen = AppTheme.Pen(Color.FromArgb(160, AppTheme.DimTextColor.R, AppTheme.DimTextColor.G, AppTheme.DimTextColor.B), 1.5,
        new Avalonia.Media.Immutable.ImmutableDashStyle([3.0, 2.0], 0));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(50, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
    private static readonly IPen HoverPen = AppTheme.Pen(Color.FromArgb(200, 255, 255, 255), 1.5);
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(70, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
    private static readonly IPen HighlightPen = AppTheme.Pen(Color.FromArgb(170, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B), 1);
    private static readonly IBrush BigTileBrush = new SolidColorBrush(Color.FromArgb(200, 255, 200, 60));
    private static readonly IBrush BadgeBrush = new SolidColorBrush(Color.FromArgb(190, 15, 15, 18));

    private Tileset? _tileset;
    private Rectangle? _selection;
    private Point? _dragFrom;
    private Point? _hover;
    private IReadOnlySet<int>? _highlight;
    private bool _selectionActive = true;
    private long _timeMs;

    /// <summary>칸 한 변(화면 px).</summary>
    public double SlotSize { get; set; } = 32;

    /// <summary>보이는 열 수 최소(타일이 적어도 이만큼은 칸을 그림).</summary>
    public int MinColumns { get; set; } = 8;

    /// <summary>고른 칸 사각형이 바뀜.</summary>
    public event Action<Rectangle?>? SelectionChanged;
    /// <summary>타일을 두 번 누름(타일 Id).</summary>
    public event Action<int>? TileActivated;
    /// <summary>오른쪽 버튼(누른 칸에 타일이 있으면 그 Id).</summary>
    public event Action<int?, PointerPressedEventArgs>? TileMenuRequested;

    public Tileset? Tileset
    {
        get => _tileset;
        set
        {
            _tileset = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>고른 칸 사각형(목록 자리 기준, 없으면 null).</summary>
    public Rectangle? Selection
    {
        get => _selection;
        set
        {
            if (_selection == value) return;
            _selection = value;
            InvalidateVisual();
        }
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

    /// <summary>옅게 칠해 보일 타일 Id(고른 자동 타일 세트에 쓰인 타일 등, 없으면 null).</summary>
    public IReadOnlySet<int>? HighlightTiles
    {
        get => _highlight;
        set
        {
            if (ReferenceEquals(_highlight, value) || _highlight is not null && value is not null && _highlight.SetEquals(value)) return;
            _highlight = value;
            InvalidateVisual();
        }
    }

    /// <summary>고른 칸이 지금 찍을 것인지(아니면 — 자동 타일 세트·지우개를 쓰는 중 — 고른 칸을 옅은 점선으로).</summary>
    public bool SelectionActive
    {
        get => _selectionActive;
        set
        {
            if (_selectionActive == value) return;
            _selectionActive = value;
            InvalidateVisual();
        }
    }

    /// <summary>고른 칸 안에 기준 자리가 있는 타일들(목록 차례).</summary>
    public IReadOnlyList<TileDef> SelectedTiles
        => _tileset is { } ts && _selection is { } sel ? ts.Tiles.Where(t => sel.Contains(t.PaletteX, t.PaletteY)).ToList() : [];

    private int Columns => Math.Max(MinColumns, _tileset is { Tiles.Length: > 0 } ts ? ts.Tiles.Max(t => t.PaletteX) + 1 : 0);

    private int Rows => Math.Max(1, _tileset is { Tiles.Length: > 0 } ts ? ts.Tiles.Max(t => t.PaletteY) + 1 : 0);

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
        => new(Columns * SlotSize + 1, Rows * SlotSize + 1);

    private Rect SlotRect(int x, int y) => new(x * SlotSize, y * SlotSize, SlotSize, SlotSize);

    private Point SlotAt(Avalonia.Point p) => new((int)Math.Floor(p.X / SlotSize), (int)Math.Floor(p.Y / SlotSize));

    private TileDef? TileAt(Point slot) => _tileset?.Tiles.FirstOrDefault(t => t.PaletteX == slot.X && t.PaletteY == slot.Y);

    public override void Render(DrawingContext g)
    {
        var area = new Rect(Bounds.Size);
        g.FillRectangle(AppTheme.BackBrush, area);
        if (_tileset is not { } ts) return;
        int cols = Columns, rows = Rows;
        AppTheme.DrawChecker(g, new Rect(0, 0, cols * SlotSize, rows * SlotSize), Math.Max(4, SlotSize / 4));
        using (ImageUtil.Pixelated(g))
            foreach (var t in ts.Tiles)
            {
                var slot = SlotRect(t.PaletteX, t.PaletteY).Deflate(1);
                var image = t.FrameImage(t.FrameAt(_timeMs));
                var bmp = TileVisuals.Bitmap(image);
                double scale = Math.Min(slot.Width / image.Width, slot.Height / image.Height);
                // 칸 크기 그림은 칸에 꽉 차게(정수배가 아니어도), 큰 그림은 칸 안에 줄여 가운데
                var w = image.Width * scale;
                var h = image.Height * scale;
                g.DrawImage(bmp, new Rect(0, 0, image.Width, image.Height), new Rect(slot.X + (slot.Width - w) / 2, slot.Y + (slot.Height - h) / 2, w, h));
                if (t.Width > 1 || t.Height > 1) g.FillRectangle(BigTileBrush, new Rect(slot.Right - 5, slot.Y + 1, 4, 4));
            }
        for (int x = 0; x <= cols; x++) g.DrawLine(GridPen, new Avalonia.Point(x * SlotSize + 0.5, 0), new Avalonia.Point(x * SlotSize + 0.5, rows * SlotSize));
        for (int y = 0; y <= rows; y++) g.DrawLine(GridPen, new Avalonia.Point(0, y * SlotSize + 0.5), new Avalonia.Point(cols * SlotSize, y * SlotSize + 0.5));
        double badge = Math.Clamp(Math.Round(SlotSize * 0.38), 9, 14);
        foreach (var t in ts.Tiles)
        {
            var slot = SlotRect(t.PaletteX, t.PaletteY);
            if (_highlight is { } hl && hl.Contains(t.Id))
            {
                g.FillRectangle(HighlightBrush, slot.Deflate(1));
                g.DrawRectangle(null, HighlightPen, slot.Deflate(1.5));
            }
        }
        if (_hover is { } hv && hv.X < cols && hv.Y < rows && (_selection is not { } cur || !cur.Contains(hv)))
            g.DrawRectangle(null, HoverPen, SlotRect(hv.X, hv.Y).Deflate(1.5));
        if (_selection is { } sel)
        {
            var r = new Rect(sel.X * SlotSize, sel.Y * SlotSize, sel.Width * SlotSize, sel.Height * SlotSize);
            if (_selectionActive)
            {
                g.FillRectangle(SelectionBrush, r);
                g.DrawRectangle(null, SelectionOutlinePen, r.Deflate(0.5));
                g.DrawRectangle(null, SelectionPen, r.Deflate(2));
                g.DrawRectangle(null, SelectionOutlinePen, r.Deflate(4));
            }
            else g.DrawRectangle(null, InactiveSelectionPen, r.Deflate(1.5));
        }
        // 움직이는 타일 표시(고른 테두리 위에): 오른쪽 아래 어두운 네모 위 재생 삼각형
        foreach (var t in ts.Tiles)
        {
            if (!t.IsAnimated) continue;
            var slot = SlotRect(t.PaletteX, t.PaletteY);
            var mark = new Rect(slot.Right - badge - 2, slot.Bottom - badge - 2, badge, badge);
            g.FillRectangle(BadgeBrush, mark, 2);
            VectorIcons.Draw(g, VectorIcon.Play, mark, badge - 3, AppTheme.TextBrush, 2, filled: true);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_tileset is null) return;
        var pt = e.GetCurrentPoint(this);
        var slot = SlotAt(pt.Position);
        if (slot.X < 0 || slot.Y < 0) return;
        e.Handled = true;
        if (pt.Properties.IsRightButtonPressed)
        {
            if (_selection is not { } s || !s.Contains(slot)) SetSelection(new Rectangle(slot, new System.Drawing.Size(1, 1)));
            TileMenuRequested?.Invoke(TileAt(slot)?.Id, e);
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && TileAt(slot) is { } tile)
        {
            TileActivated?.Invoke(tile.Id);
            return;
        }
        bool extend = (e.KeyModifiers & KeyModifiers.Shift) != 0 && _selection is not null;
        _dragFrom = extend ? _selection!.Value.Location : slot;
        // 같은 칸을 다시 눌러도 알린다(자동 타일 세트를 쓰다가 같은 타일로 돌아올 때)
        SetSelection(Span(_dragFrom.Value, slot), always: true);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var slot = SlotAt(e.GetPosition(this));
        SetHover(slot.X >= 0 && slot.Y >= 0 ? slot : null);
        if (_dragFrom is not { } from) return;
        slot = new Point(Math.Max(0, slot.X), Math.Max(0, slot.Y));
        SetSelection(Span(from, slot));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(null);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is null) return;
        _dragFrom = null;
        e.Pointer.Capture(null);
    }

    /// <summary>마우스 올린 칸을 바꾸고 풍선 도움말을 그 타일 설명으로.</summary>
    private void SetHover(Point? slot)
    {
        if (_hover == slot) return;
        _hover = slot;
        InvalidateVisual();
        ToolTip.SetTip(this, slot is { } s && TileAt(s) is { } t ? TileTip(t) : null);
    }

    /// <summary>타일 설명: 이름, Id·기본 층·차지 칸, 움직임 장수, 고른 자동 타일 세트에 쓰이는지.</summary>
    private string TileTip(TileDef t)
    {
        var floor = _tileset?.FindFloor(t.Floor)?.Name ?? "?";
        var lines = new List<string> { TileVisuals.TileName(t), UiStrings.FormatTilePalette_TipInfo(id: t.Id, floor: floor, w: t.Width, h: t.Height) };
        if (t.IsAnimated) lines.Add(UiStrings.FormatTilePalette_TipFrames(count: t.FrameCount, ms: t.FrameMs));
        if (_highlight?.Contains(t.Id) == true) lines.Add(UiStrings.TilePalette_TipAuto);
        return string.Join("\n", lines);
    }

    private static Rectangle Span(Point a, Point b)
        => Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);

    private void SetSelection(Rectangle? sel, bool always = false)
    {
        if (_selection == sel && !always) return;
        _selection = sel;
        InvalidateVisual();
        SelectionChanged?.Invoke(sel);
    }
}
