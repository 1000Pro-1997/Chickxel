using System.Diagnostics;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

public sealed partial class AutoTileEditorDialog
{
    /// <summary>
    /// 자동 타일 시험 판(저장 안 함): 왼쪽 버튼으로 칠하고 오른쪽 버튼으로 지우면 고치는 중인 세트로 타일이 어떻게 붙는지 바로 보인다.
    /// Shift+왼쪽 버튼(또는 <see cref="BlockMode"/>)은 "뭐라도 있음" 칸(회색 — 이 세트가 아닌 다른 타일, 벽 같은 것)을 놓아 그 조건을 시험한다.
    /// 움직이는 타일은 판에서도 움직인다. 마우스가 올라간 칸은 <see cref="HoverChanged"/>로 알린다.
    /// </summary>
    private sealed class PreviewBoard : Control
    {
        public const int Cols = 12, Rows = 9;
        private static readonly IPen GridPen = AppTheme.Pen(Color.FromArgb(28, 255, 255, 255));
        private static readonly IPen HoverPen = AppTheme.Pen(AppTheme.WarningColor, 2);
        private static readonly IBrush MarkBrush = new SolidColorBrush(Color.FromArgb(70, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
        private static readonly IBrush BlockBrush = new SolidColorBrush(Color.FromRgb(96, 96, 106));
        private static readonly IPen BlockPen = AppTheme.Pen(Color.FromRgb(140, 140, 152), 1.5);
        // 판에 놓는 "뭐라도 있음" 칸의 타일 Id(타일셋에 없는 Id — 그리지 않고 칸만 차지)
        private const int BlockTile = -1;

        // 처음 칠해 둘 모양(# = 이 세트, B = 뭐라도 있음 칸): 넓은 땅·안쪽 구멍·가는 가로·세로 줄·혼자 있는 칸·T자, 세트 위·옆에 다른 것이 닿은 자리까지
        private static readonly string[] StartShape =
        [
            "............",
            ".####..BB.#.",
            ".####..##.#.",
            ".#..#..##.#.",
            ".####.....#.",
            "..##..B#B...",
            "......###.##",
            ".###...#....",
            "............",
        ];

        private readonly Tileset _source;
        private readonly HashSet<Point> _cells = [];
        private readonly HashSet<Point> _blocks = [];
        private readonly double _zoom;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly WriteableBitmap _bitmap;
        private Tileset _ts;
        private AutoTileSet? _set;
        private TileMap _map = TileMap.Empty;
        private bool _dirty = true;
        private long _phase = -1;
        private DispatcherTimer? _timer;
        // 끄는 동안 하는 일: 세트 칠하기, 뭐라도 있음 칸 놓기, 지우기
        private enum Stroke { Paint, Block, Erase }
        private Stroke? _stroke;
        private bool _attached;

        /// <summary>마우스가 올라간 칸(판 밖이면 null).</summary>
        public Point? Hover { get; private set; }

        /// <summary>왼쪽 버튼이 "뭐라도 있음" 칸을 놓는지(아니면 Shift+왼쪽 버튼일 때만).</summary>
        public bool BlockMode { get; set; }

        public event Action<Point?>? HoverChanged;

        public PreviewBoard(Tileset ts)
        {
            _source = _ts = ts;
            int w = Cols * ts.CellWidth, h = Rows * ts.CellHeight;
            double zoom = Math.Min(576.0 / w, 432.0 / h);
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
            _blocks.Clear();
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    if (StartShape[y][x] == '#') _cells.Add(new Point(x, y));
                    else if (StartShape[y][x] == 'B') _blocks.Add(new Point(x, y));
                }
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
            _blocks.Clear();
            Rebuild();
            HoverChanged?.Invoke(Hover);
        }

        public void Reset()
        {
            ResetCells();
            Rebuild();
            HoverChanged?.Invoke(Hover);
        }

        /// <summary>"뭐라도 있음" 칸을 놓을 층: 세트가 보는 층 중 세트의 층이 아닌 것(없으면 세트의 층).</summary>
        private int BlockFloor(AutoTileSet set)
            => _ts.Floors.Select(f => f.Id).FirstOrDefault(f => f != set.Floor && set.CountsFloor(f), set.Floor);

        private void Rebuild()
        {
            if (_set is not { } set) _map = TileMap.Empty;
            else
            {
                var map = TileMap.Empty;
                int floor = BlockFloor(set);
                foreach (var b in _blocks) map = map.With(floor, b, new TileCell(BlockTile));
                _map = AutoTiler.Paint(_ts, map, set, _cells);
            }
            _dirty = true;
            InvalidateVisual();
        }

        /// <summary>칸 <paramref name="at"/>에 쓰인 규칙 번호와 조건이 그대로 맞는지(칠하지 않은 칸이거나 타일 있는 규칙이 없으면 null).</summary>
        public (int Rule, bool Exact)? RuleAt(Point at)
        {
            if (_set is null || !_cells.Contains(at)) return null;
            return AutoTiler.RuleAt(_ts, _map, _set, at);
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
            double sx = cw * _zoom, sy = ch * _zoom;
            // 뭐라도 있음 칸: 회색 덩어리(그 위로 솟은 타일이 겹쳐 보이게 타일보다 먼저)
            foreach (var b in _blocks)
            {
                var r = new Rect(b.X * sx, b.Y * sy, sx, sy);
                g.FillRectangle(BlockBrush, r);
                g.DrawRectangle(null, BlockPen, r.Deflate(1.5));
            }
            using (ImageUtil.Pixelated(g))
                g.DrawImage(_bitmap, new Rect(0, 0, Cols * cw, Rows * ch), area);
            for (int x = 1; x < Cols; x++) g.DrawLine(GridPen, new Avalonia.Point(x * sx, 0), new Avalonia.Point(x * sx, Height));
            for (int y = 1; y < Rows; y++) g.DrawLine(GridPen, new Avalonia.Point(0, y * sy), new Avalonia.Point(Width, y * sy));
            // 타일 있는 규칙이 없으면 칠한 칸이 보이지 않으므로 자리만 흐리게
            if (_set is null || AutoTiler.RuleFor(_set, 0, 0) < 0)
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
            bool changed = _stroke switch
            {
                Stroke.Paint => _cells.Add(cell) | _blocks.Remove(cell),
                Stroke.Block => _blocks.Add(cell) | _cells.Remove(cell),
                Stroke.Erase => _cells.Remove(cell) | _blocks.Remove(cell),
                _ => false,
            };
            if (changed) Rebuild();
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
            bool block = BlockMode || (e.KeyModifiers & KeyModifiers.Shift) != 0;
            _stroke = pt.Properties.IsLeftButtonPressed ? block ? Stroke.Block : Stroke.Paint
                : pt.Properties.IsRightButtonPressed ? Stroke.Erase : null;
            if (_stroke is null) return;
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
            SetHover(cell, force: cell is not null && _stroke is not null);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _stroke = null;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _stroke = null;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_stroke is null) SetHover(null);
        }
    }
}
