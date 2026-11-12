using System.Diagnostics;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

public sealed partial class AutoTileEditorDialog
{
    /// <summary>
    /// 자동 타일 시험 판(저장 안 함): 왼쪽 버튼으로 칠하고 오른쪽 버튼으로 지우면 고치는 중인 세트로 타일이 어떻게 붙는지 바로 보인다.
    /// 움직이는 타일은 판에서도 움직인다. 마우스가 올라간 칸은 <see cref="HoverChanged"/>로 알린다.
    /// </summary>
    private sealed class PreviewBoard : Control
    {
        public const int Cols = 10, Rows = 7;
        private static readonly IPen GridPen = AppTheme.Pen(Color.FromArgb(28, 255, 255, 255));
        private static readonly IPen HoverPen = AppTheme.Pen(AppTheme.WarningColor, 2);
        private static readonly IBrush MarkBrush = new SolidColorBrush(Color.FromArgb(70, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));

        // 처음 칠해 둘 덩어리(# = 칠한 칸): 넓은 땅·가는 줄·혼자 있는 칸·안쪽 모서리가 다 보이게
        private static readonly string[] StartShape =
        [
            "..........",
            ".####...#.",
            ".#####..#.",
            ".##.##..#.",
            ".#####....",
            "...##..##.",
            "..........",
        ];

        private readonly Tileset _source;
        private readonly HashSet<Point> _cells = [];
        private readonly double _zoom;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly WriteableBitmap _bitmap;
        private Tileset _ts;
        private AutoTileSet? _set;
        private TileMap _map = TileMap.Empty;
        private bool _dirty = true;
        private long _phase = -1;
        private DispatcherTimer? _timer;
        private bool? _painting;
        private bool _attached;

        /// <summary>마우스가 올라간 칸(판 밖이면 null).</summary>
        public Point? Hover { get; private set; }

        public event Action<Point?>? HoverChanged;

        public PreviewBoard(Tileset ts)
        {
            _source = _ts = ts;
            int w = Cols * ts.CellWidth, h = Rows * ts.CellHeight;
            double zoom = Math.Min(320.0 / w, 240.0 / h);
            _zoom = zoom >= 1 ? Math.Floor(zoom) : zoom;
            Width = w * _zoom;
            Height = h * _zoom;
            _bitmap = ImageUtil.NewBitmap(w, h);
            Cursor = new Cursor(StandardCursorType.Cross);
            ResetCells();
        }

        private void ResetCells()
        {
            _cells.Clear();
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                    if (StartShape[y][x] == '#') _cells.Add(new Point(x, y));
        }

        /// <summary>고치는 중인 세트가 바뀜: 판을 그 세트로 다시 고른다.</summary>
        public void SetSet(AutoTileSet set)
        {
            _ts = TilesetEdit.PutAutoTile(_source, set);
            _set = _ts.FindAutoTile(set.Id);
            Rebuild();
            UpdateTimer();
        }

        public void Clear()
        {
            _cells.Clear();
            Rebuild();
            HoverChanged?.Invoke(Hover);
        }

        public void Reset()
        {
            ResetCells();
            Rebuild();
            HoverChanged?.Invoke(Hover);
        }

        private void Rebuild()
        {
            _map = _set is null ? TileMap.Empty : AutoTiler.Paint(_ts, TileMap.Empty, _set, _cells);
            _dirty = true;
            InvalidateVisual();
        }

        /// <summary>칸 <paramref name="at"/>에 쓰인 규칙 번호와 조건이 그대로 맞는지(칠하지 않은 칸이거나 타일 있는 규칙이 없으면 null).</summary>
        public (int Rule, bool Exact)? RuleAt(Point at)
        {
            if (_set is null || !_cells.Contains(at)) return null;
            byte mask = AutoTiler.NeighborMask(_ts, _map, _set, at);
            int rule = AutoTiler.RuleFor(_set, mask);
            return rule < 0 ? null : (rule, AutoTiler.HasExact(_set, mask));
        }

        // ───────────── 움직이는 타일 ─────────────

        private void UpdateTimer()
        {
            bool animate = _ts.HasAnimation && _attached;
            if (animate && _timer is null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                _timer.Tick += (_, _) =>
                {
                    if (_ts.AnimationPhase(_clock.ElapsedMilliseconds) != _phase) InvalidateVisual();
                };
                _timer.Start();
            }
            else if (!animate && _timer is not null)
            {
                _timer.Stop();
                _timer = null;
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            UpdateTimer();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _attached = false;
            _timer?.Stop();
            _timer = null;
        }

        // ───────────── 그리기 ─────────────

        public override void Render(DrawingContext g)
        {
            int cw = _ts.CellWidth, ch = _ts.CellHeight;
            long now = _clock.ElapsedMilliseconds;
            long phase = _ts.AnimationPhase(now);
            if (_dirty || phase != _phase)
            {
                var image = TileMapRenderer.RenderArea(_ts, _map, Point.Empty, new Rectangle(0, 0, Cols * cw, Rows * ch), timeMs: now);
                ImageUtil.CopyToBitmap(image, _bitmap);
                _dirty = false;
                _phase = phase;
            }
            var area = new Rect(0, 0, Width, Height);
            AppTheme.DrawChecker(g, area, 8);
            using (ImageUtil.Pixelated(g))
                g.DrawImage(_bitmap, new Rect(0, 0, Cols * cw, Rows * ch), area);
            double sx = cw * _zoom, sy = ch * _zoom;
            for (int x = 1; x < Cols; x++) g.DrawLine(GridPen, new Avalonia.Point(x * sx, 0), new Avalonia.Point(x * sx, Height));
            for (int y = 1; y < Rows; y++) g.DrawLine(GridPen, new Avalonia.Point(0, y * sy), new Avalonia.Point(Width, y * sy));
            // 타일 있는 규칙이 없으면 칠한 칸이 보이지 않으므로 자리만 흐리게
            if (_set is null || AutoTiler.RuleFor(_set, 0) < 0)
                foreach (var c in _cells) g.FillRectangle(MarkBrush, new Rect(c.X * sx, c.Y * sy, sx, sy));
            if (Hover is { } h) g.DrawRectangle(null, HoverPen, new Rect(h.X * sx + 1, h.Y * sy + 1, sx - 2, sy - 2));
        }

        // ───────────── 칠하기 ─────────────

        private Point? CellAt(Avalonia.Point p)
        {
            int x = (int)Math.Floor(p.X / (_ts.CellWidth * _zoom)), y = (int)Math.Floor(p.Y / (_ts.CellHeight * _zoom));
            return x >= 0 && x < Cols && y >= 0 && y < Rows ? new Point(x, y) : null;
        }

        private void Apply(Point cell)
        {
            if (_painting is not { } paint) return;
            if (paint ? !_cells.Add(cell) : !_cells.Remove(cell)) return;
            Rebuild();
        }

        private void SetHover(Point? cell, bool force = false)
        {
            if (!force && cell == Hover) return;
            Hover = cell;
            InvalidateVisual();
            HoverChanged?.Invoke(cell);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            if (CellAt(pt.Position) is not { } cell) return;
            _painting = pt.Properties.IsLeftButtonPressed ? true : pt.Properties.IsRightButtonPressed ? false : null;
            if (_painting is null) return;
            e.Pointer.Capture(this);
            Apply(cell);
            SetHover(cell, force: true);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var cell = CellAt(e.GetPosition(this));
            if (cell is { } c) Apply(c);
            SetHover(cell, force: cell is not null && _painting is not null);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _painting = null;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _painting = null;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_painting is null) SetHover(null);
        }
    }
}
