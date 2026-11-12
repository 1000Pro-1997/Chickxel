using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 타일 목록: 타일셋의 타일을 목록 자리(<see cref="TileDef.PaletteX"/>, <see cref="TileDef.PaletteY"/>)에 칸으로 보인다(큰 타일은 칸에 맞춰 줄임).
/// <see cref="HiddenTiles"/>는 그리지 않고 다 빈 줄은 접으며, <see cref="GroupByFloor"/>면 기본 층마다(위층부터) 층 이름 줄 아래 그 층 타일을 모은다.
/// 그래서 화면 칸은 목록 자리와 다를 수 있다 — 고른 칸(<see cref="Selection"/>)·<see cref="DisplayPos"/>는 화면 칸 기준.
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
    private IReadOnlySet<int>? _hidden;
    private bool _groupByFloor;
    private bool _selectionActive = true;
    private long _timeMs;
    // 타일 Id → 화면 칸, 화면 칸 → 타일, 층 이름 줄(화면 줄, 이름), 화면 줄 수, 보이는 타일(목록 차례)
    private readonly Dictionary<int, Point> _pos = [];
    private readonly Dictionary<Point, TileDef> _at = [];
    private readonly List<(int Row, string Name)> _headers = [];
    private int _rowCount;
    private readonly List<TileDef> _visible = [];

    /// <summary>칸 한 변(화면 px)의 최대 — 받은 폭에 열이 다 들어가지 않으면 그만큼 줄여 그린다(옆으로 스크롤하지 않게).</summary>
    public double SlotSize { get; set; } = 32;

    // 실제로 그리는 칸 한 변(폭에 맞춰 줄인 값)
    private double _slot = 32;

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
            Rebuild();
        }
    }

    /// <summary>목록에서 감출 타일 Id(자동 타일 세트에 쓴 타일 등, 없으면 null). 감춘 타일뿐인 줄은 접힌다.</summary>
    public IReadOnlySet<int>? HiddenTiles
    {
        get => _hidden;
        set
        {
            if (ReferenceEquals(_hidden, value) || _hidden is not null && value is not null && _hidden.SetEquals(value)) return;
            _hidden = value;
            Rebuild();
        }
    }

    /// <summary>기본 층마다(위층부터) 층 이름 줄 아래 그 층 타일을 모아 보인다.</summary>
    public bool GroupByFloor
    {
        get => _groupByFloor;
        set
        {
            if (_groupByFloor == value) return;
            _groupByFloor = value;
            Rebuild();
        }
    }

    /// <summary>고른 칸 사각형(화면 칸 기준, 없으면 null).</summary>
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

    /// <summary>고른 칸 안에 화면 칸이 있는 보이는 타일들(목록 차례).</summary>
    public IReadOnlyList<TileDef> SelectedTiles
        => _selection is { } sel ? _visible.Where(t => sel.Contains(DisplayPos(t))).ToList() : [];

    /// <summary>타일의 화면 칸(보이지 않는 타일이면 목록 자리 그대로).</summary>
    public Point DisplayPos(TileDef t) => _pos.TryGetValue(t.Id, out var p) ? p : new Point(t.PaletteX, t.PaletteY);

    /// <summary>
    /// 화면 칸 사각형 → 목록 자리 사각형: 안의 타일이 모두 같은 만큼 옮겨진 자리이고, 그 목록 사각형에 감춘 타일이 없을 때만(아니면 null — 큰 타일로 합치기 등).
    /// </summary>
    public Rectangle? PaletteRect(Rectangle display)
    {
        if (_tileset is not { } ts) return null;
        Point? shift = null;
        foreach (var t in _visible)
        {
            var d = DisplayPos(t);
            if (!display.Contains(d)) continue;
            var s = new Point(t.PaletteX - d.X, t.PaletteY - d.Y);
            if (shift is { } prev && prev != s) return null;
            shift = s;
        }
        if (shift is not { } sh) return null;
        var rect = new Rectangle(display.X + sh.X, display.Y + sh.Y, display.Width, display.Height);
        // 목록 사각형에 화면에 없는 타일(감춘 타일·다른 층 타일)이 끼면 화면과 달라진다
        foreach (var t in ts.Tiles)
            if (rect.Contains(t.PaletteX, t.PaletteY) && (!_pos.TryGetValue(t.Id, out var p) || !display.Contains(p))) return null;
        return rect;
    }

    /// <summary>타일 하나를 고른다(알리지 않음 — 부르는 쪽이 붓을 맞춤). 보이지 않는 타일이면 고른 것을 지운다.</summary>
    public void SelectTile(int id)
        => Selection = _pos.TryGetValue(id, out var p) ? new Rectangle(p, new System.Drawing.Size(1, 1)) : null;

    /// <summary>보이는 타일과 화면 칸을 다시 정한다(고른 타일이 같은 모양으로 남으면 그 자리로 옮기고, 아니면 고른 것을 지움).</summary>
    private void Rebuild()
    {
        var picked = SelectedTiles.Select(t => t.Id).ToList();
        var oldSel = _selection;
        _pos.Clear();
        _at.Clear();
        _headers.Clear();
        _visible.Clear();
        _rowCount = 0;
        if (_tileset is { } ts)
        {
            foreach (var t in ts.Tiles)
                if (_hidden is null || !_hidden.Contains(t.Id)) _visible.Add(t);
            if (_groupByFloor && ts.Floors.Length > 0)
            {
                for (int i = ts.Floors.Length - 1; i >= 0; i--)
                {
                    var floor = ts.Floors[i];
                    var tiles = _visible.Where(t => ts.ValidFloor(t.Floor) == floor.Id).ToList();
                    if (tiles.Count == 0) continue;
                    _headers.Add((_rowCount, floor.Name));
                    Place(tiles, _rowCount + 1);
                }
            }
            else if (_hidden is null)
            {
                foreach (var t in _visible) Put(t, new Point(t.PaletteX, t.PaletteY));
                _rowCount = _visible.Count == 0 ? 0 : _visible.Max(t => t.PaletteY) + 1;
            }
            else Place(_visible, 0);
        }
        _selection = null;
        if (oldSel is { } old && picked.Count > 0 && picked.All(_pos.ContainsKey))
        {
            var pts = picked.Select(id => _pos[id]).ToList();
            var box = Rectangle.FromLTRB(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X) + 1, pts.Max(p => p.Y) + 1);
            if (box.Size == old.Size || picked.Count == 1) _selection = picked.Count == 1 ? new Rectangle(pts[0], new System.Drawing.Size(1, 1)) : box;
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>타일들을 <paramref name="firstRow"/>부터 목록 줄 차례대로(빈 줄은 접어) 놓는다.</summary>
    private void Place(IReadOnlyList<TileDef> tiles, int firstRow)
    {
        var rows = tiles.Select(t => t.PaletteY).Distinct().Order().ToList();
        var rowOf = new Dictionary<int, int>(rows.Count);
        for (int i = 0; i < rows.Count; i++) rowOf[rows[i]] = firstRow + i;
        foreach (var t in tiles) Put(t, new Point(t.PaletteX, rowOf[t.PaletteY]));
        _rowCount = firstRow + rows.Count;
    }

    private void Put(TileDef t, Point at)
    {
        _pos[t.Id] = at;
        _at.TryAdd(at, t);
    }

    private int Columns => Math.Max(MinColumns, _tileset is { Tiles.Length: > 0 } ts ? ts.Tiles.Max(t => t.PaletteX) + 1 : 0);

    private int Rows => Math.Max(1, _rowCount);

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        int cols = Columns;
        double fit = double.IsFinite(availableSize.Width) && availableSize.Width > 1 ? Math.Floor((availableSize.Width - 1) / cols) : SlotSize;
        _slot = Math.Max(8, Math.Min(SlotSize, fit));
        return new(cols * _slot + 1, Rows * _slot + 1);
    }

    private Rect SlotRect(int x, int y) => new(x * _slot, y * _slot, _slot, _slot);

    private Rect SlotRect(TileDef t)
    {
        var at = DisplayPos(t);
        return SlotRect(at.X, at.Y);
    }

    private Point SlotAt(Avalonia.Point p) => new((int)Math.Floor(p.X / _slot), (int)Math.Floor(p.Y / _slot));

    private TileDef? TileAt(Point slot) => _at.GetValueOrDefault(slot);

    private bool IsHeaderRow(int row) => _headers.Exists(h => h.Row == row);

    public override void Render(DrawingContext g)
    {
        var area = new Rect(Bounds.Size);
        g.FillRectangle(AppTheme.BackBrush, area);
        if (_tileset is null) return;
        int cols = Columns, rows = Rows;
        double width = cols * _slot;
        AppTheme.DrawChecker(g, new Rect(0, 0, width, rows * _slot), Math.Max(4, _slot / 4));
        // 층 이름 줄: 체커 대신 패널 색 띠와 이름
        foreach (var (row, name) in _headers)
        {
            var band = new Rect(0, row * _slot, width + 1, _slot);
            g.FillRectangle(AppTheme.PanelBrush, band);
            var text = new FormattedText(name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AppTheme.BoldTypeface,
                Math.Min(AppTheme.FontSize, Math.Max(9, _slot * 0.55)), AppTheme.DimTextBrush);
            g.DrawText(text, new Avalonia.Point(4, band.Y + (band.Height - text.Height) / 2));
        }
        using (ImageUtil.Pixelated(g))
            foreach (var t in _visible)
            {
                var slot = SlotRect(t).Deflate(1);
                var image = t.FrameImage(t.FrameAt(_timeMs));
                var bmp = TileVisuals.Bitmap(image);
                double scale = Math.Min(slot.Width / image.Width, slot.Height / image.Height);
                // 칸 크기 그림은 칸에 꽉 차게(정수배가 아니어도), 큰 그림은 칸 안에 줄여 가운데
                var w = image.Width * scale;
                var h = image.Height * scale;
                g.DrawImage(bmp, new Rect(0, 0, image.Width, image.Height), new Rect(slot.X + (slot.Width - w) / 2, slot.Y + (slot.Height - h) / 2, w, h));
                if (t.Width > 1 || t.Height > 1) g.FillRectangle(BigTileBrush, new Rect(slot.Right - 5, slot.Y + 1, 4, 4));
            }
        for (int x = 0; x <= cols; x++)
            for (int y = 0; y < rows; y++)
                if (!IsHeaderRow(y)) g.DrawLine(GridPen, new Avalonia.Point(x * _slot + 0.5, y * _slot), new Avalonia.Point(x * _slot + 0.5, (y + 1) * _slot));
        for (int y = 0; y <= rows; y++) g.DrawLine(GridPen, new Avalonia.Point(0, y * _slot + 0.5), new Avalonia.Point(width, y * _slot + 0.5));
        double badge = Math.Clamp(Math.Round(_slot * 0.38), 9, 14);
        if (_highlight is { } hl)
            foreach (var t in _visible)
            {
                if (!hl.Contains(t.Id)) continue;
                var slot = SlotRect(t);
                g.FillRectangle(HighlightBrush, slot.Deflate(1));
                g.DrawRectangle(null, HighlightPen, slot.Deflate(1.5));
            }
        if (_hover is { } hv && hv.X < cols && hv.Y < rows && !IsHeaderRow(hv.Y) && (_selection is not { } cur || !cur.Contains(hv)))
            g.DrawRectangle(null, HoverPen, SlotRect(hv.X, hv.Y).Deflate(1.5));
        if (_selection is { } sel)
        {
            var r = new Rect(sel.X * _slot, sel.Y * _slot, sel.Width * _slot, sel.Height * _slot);
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
        foreach (var t in _visible)
        {
            if (!t.IsAnimated) continue;
            var slot = SlotRect(t);
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
        if (slot.X < 0 || slot.Y < 0 || IsHeaderRow(slot.Y)) return;
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
