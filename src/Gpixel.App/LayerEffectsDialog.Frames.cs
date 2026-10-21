using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 전·후 미리보기 아래 프레임 줄: 프레임마다 효과를 입힌 작은 그림. 고른 효과(없으면 "추가 범위")의 프레임 범위가 파랗게 보이고,
/// 끌어서 범위를 정하며(아래 숫자 칸과 같은 값), 누르면 그 프레임을 미리보기에 보인다.
/// 효과를 고른 채 범위 끝을 마지막 칸 너머로 끌면 점선 빈 칸이 생기며 그만큼 프레임을 늘린다(<see cref="DragRange"/>).
/// 프레임을 늘리는 중이면 늘린 뒤 차례의 프레임을 보이고 더해질 사본 칸은 점선 테두리.
/// </summary>
public sealed partial class LayerEffectsDialog
{
    private readonly FrameStrip _strip = new();
    private readonly DispatcherTimer _stripTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private Control BuildFrameStrip()
    {
        _strip.FrameClicked += f =>
        {
            // 칸 그림은 잠깐 모았다가 그리므로(_stripTimer) 줄이 아직 이전 칸 수일 수 있다 — 지금 없는 프레임 칸은 무시
            if (f >= VirtualCount) return;
            _timer.Stop();
            _play.Content = Ui.IconLabel(VectorIcon.Play, "재생");
            _previewFrame = f;
            RefreshPreview();
        };
        _strip.DragStarted += () => _dragBase = (_extension, VirtualCount);
        _strip.RangeDragged += DragRange;
        _strip.RenderRequested += RenderStrip;
        // 효과를 바꿀 때마다 모든 프레임을 다시 그리지 않도록 잠깐 모았다가 그린다.
        _stripTimer.Tick += (_, _) =>
        {
            _stripTimer.Stop();
            RenderStrip();
        };
        // 칸이 너비를 넘으면 다음 줄로(세로 스크롤은 여러 줄이 넘칠 때만).
        _strip.Width = PreviewBox * 2 + 12 - 14;
        RenderStrip();
        return new ScrollViewer
        {
            Content = _strip,
            Width = PreviewBox * 2 + 12,
            MaxHeight = FrameStrip.HeightFor(3),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
    }

    /// <summary>프레임 칸을 모두 다시 그린다(늘리는 중이면 늘린 뒤 차례).</summary>
    private void RenderStrip()
    {
        var effects = VirtualEffects();
        _strip.SetFrames(VirtualCount, f => RenderFrameThumb(f, effects), IsCopyFrame);
    }

    /// <summary>효과가 바뀜: 프레임 그림을 곧 다시 그린다(범위 표시는 바로).</summary>
    private void InvalidateStrip()
    {
        SyncStripRange();
        _stripTimer.Stop();
        _stripTimer.Start();
    }

    /// <summary>프레임 줄의 파란 범위(늘린 뒤 번호): 고른 효과의 범위, 효과를 고르지 않았으면 추가 범위.</summary>
    private void SyncStripRange()
    {
        int n = _project.Frames.Count;
        bool selected = Selected >= 0 && Selected < _effects.Count;
        _strip.CanExtend = selected;
        if (selected)
        {
            _strip.Range = VRangeOf(_effects[Selected]);
            return;
        }
        var (from, to) = _addAll.On() ? (0, n - 1) : ((int)_addFrom.NumValue() - 1, (int)_addTo.NumValue() - 1);
        _strip.Range = (VirtualOf(from), _extension is { } x ? x.MapRange(new FrameRange(from, Math.Max(from, to))).To : to);
    }

    /// <summary>프레임 줄에서 끌어 정한 범위(원래 번호)를 고른 효과(없으면 추가 범위)에 넣는다. null = 모든 프레임.</summary>
    private void SetRangeFromStrip(FrameRange? range)
    {
        int n = _project.Frames.Count;
        if (Selected >= 0 && Selected < _effects.Count)
        {
            Update(Selected, _effects[Selected] with { Frames = range });
            ShowSettings(); // 숫자 칸 갱신
        }
        else
        {
            _addAll.IsChecked = range is null;
            _addFrom.Value = (range?.From ?? 0) + 1;
            _addTo.Value = (range?.To ?? n - 1) + 1;
            _addRangeTouched = true;
        }
        SyncStripRange();
    }

    /// <summary>프레임 <paramref name="f"/>(늘린 뒤 번호)에 모든 효과를 입힌 그림(미리보기 "후"와 같은 크기).</summary>
    private PixelImage? RenderFrameThumb(int f, IReadOnlyList<LayerEffect> all)
    {
        var (source, effects) = AnimateAt(f, all);
        if (source is null) return null;
        return LayerEffects.Render(source, effects, LayerEffects.Padding(all, source.Width, source.Height)).Image;
    }

    /// <summary>프레임 칸(너비가 차면 다음 줄로 줄바꿈, 세로 스크롤). 칸 = 효과를 입힌 그림 + 프레임 번호.</summary>
    private sealed class FrameStrip : Control, IDisposable
    {
        private const int Cell = 44, Gap = 2, RowPitch = Cell + 4, Top = 3;

        /// <summary><paramref name="rows"/>줄일 때 높이.</summary>
        public static double HeightFor(int rows) => Top + Math.Max(1, rows) * RowPitch - 1;
        private static readonly IBrush RangeBrush = AppTheme.Brush(Color.FromArgb(70, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
        private static readonly IBrush OutsideBrush = AppTheme.Brush(Color.FromArgb(140, 0, 0, 0));
        private static readonly IPen CurrentPen = new Pen(Brushes.White, 2);
        private static readonly IPen RangePen = new Pen(AppTheme.AccentBrush, 2);
        private static readonly IPen GhostPen = new Pen(AppTheme.AccentBrush, 1, new DashStyle([3, 2], 0));
        private static readonly IPen CopyPen = new Pen(AppTheme.DimTextBrush, 1, new DashStyle([2, 2], 0));

        private WriteableBitmap?[] _thumbs = [];
        private bool[] _copies = [];
        private (int From, int To) _range;
        private int _current, _press = -1, _dragTo = -1;
        private bool _dragging;
        /// <summary>끄는 동안 칸 수가 바뀌어 다시 그리기를 미뤘는지(칸 차례가 흔들리지 않게 — 놓을 때 <see cref="RenderRequested"/>).</summary>
        private bool _stale;

        public FrameStrip()
        {
            Height = HeightFor(1);
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top; // 줄이 늘어도(끌어 늘리기) 앞 칸 자리가 그대로
            Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(this, "누르면 그 프레임을 보고, 끌면 프레임 범위를 정합니다. 효과를 고른 채 마지막 칸 너머로 끌면 프레임을 늘립니다(점선 칸).");
        }

        /// <summary>누름(끌지 않음): 그 프레임을 미리보기에.</summary>
        public event Action<int>? FrameClicked;

        /// <summary>누름: 끌기 시작(이때의 칸 차례로 <see cref="RangeDragged"/>를 읽음).</summary>
        public event Action? DragStarted;

        /// <summary>끄는 중: 누른 칸 ~ 지금 칸(작은 쪽, 큰 쪽). <see cref="CanExtend"/>면 큰 쪽이 칸 수 이상(마지막 칸 너머)일 수 있다.</summary>
        public event Action<int, int>? RangeDragged;

        /// <summary>놓음: 끄는 동안 미룬 칸 다시 그리기 — 받는 쪽이 지금 상태로 <see cref="SetFrames"/>를 다시 부른다.</summary>
        public event Action? RenderRequested;

        /// <summary>마지막 칸 너머로 끌어 늘릴 수 있는지.</summary>
        public bool CanExtend { get; set; }

        public (int From, int To) Range
        {
            get => _range;
            set
            {
                if (_range == value) return;
                _range = value;
                InvalidateVisual();
            }
        }

        public int Current
        {
            get => _current;
            set
            {
                if (_current == value) return;
                _current = value;
                InvalidateVisual();
            }
        }

        /// <param name="isCopy">더해질 사본 칸(점선 테두리).</param>
        public void SetFrames(int count, Func<int, PixelImage?> render, Func<int, bool> isCopy)
        {
            if (_dragging && count != _thumbs.Length)
            {
                _stale = true;
                return;
            }
            _stale = false;
            Dispose();
            _thumbs = new WriteableBitmap?[count];
            _copies = new bool[count];
            for (int f = 0; f < count; f++)
            {
                if (render(f) is { } img) _thumbs[f] = ImageUtil.ToBitmap(img);
                _copies[f] = isCopy(f);
            }
            UpdateWidth();
            InvalidateVisual();
        }

        /// <summary>한 줄에 들어가는 칸 수(너비 기준).</summary>
        private int Columns
        {
            get
            {
                double w = double.IsNaN(Width) ? Bounds.Width : Width;
                return Math.Max(1, (int)Math.Floor((w - Gap) / (Cell + Gap)));
            }
        }

        /// <summary>칸 수(끄는 중 늘린 빈 칸 포함)에 맞춰 줄 수 = 높이를 정한다.</summary>
        private void UpdateWidth()
        {
            int cells = Math.Max(1, Math.Max(_thumbs.Length, _dragTo + 1));
            Height = HeightFor((cells + Columns - 1) / Columns);
        }

        /// <summary>점 아래 칸 번호: 줄 × 한 줄 칸 수 + 열(오른쪽 밖은 그 줄 끝 칸, 마지막 줄 아래는 그다음 줄 = 칸 수 너머).</summary>
        private int CellAt(Avalonia.Point p)
        {
            int cols = Columns;
            int col = Math.Clamp((int)Math.Floor((p.X - Gap) / (Cell + Gap)), 0, cols - 1);
            int row = Math.Max(0, (int)Math.Floor((p.Y - Top) / RowPitch));
            return row * cols + col;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (_thumbs.Length == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _press = Math.Min(CellAt(e.GetPosition(this)), _thumbs.Length - 1);
            _dragging = false;
            DragStarted?.Invoke();
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_press < 0) return;
            int f = Math.Min(CellAt(e.GetPosition(this)), CanExtend ? _thumbs.Length + FrameExtension.MaxCount : _thumbs.Length - 1);
            if (!_dragging && f == _press) return;
            _dragging = true;
            if (f != _dragTo)
            {
                _dragTo = f;
                UpdateWidth();
                InvalidateVisual();
                if (f >= _thumbs.Length) this.BringIntoView(CellRect(f)); // 창 밖까지 끌면 따라 보이게(보이는 칸이면 그대로)
            }
            RangeDragged?.Invoke(Math.Min(_press, f), Math.Max(_press, f));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_press < 0) return;
            int press = _press;
            bool dragged = _dragging;
            e.Pointer.Capture(null);
            EndDrag();
            if (!dragged) FrameClicked?.Invoke(press);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            EndDrag();
        }

        private void EndDrag()
        {
            _press = _dragTo = -1;
            _dragging = false;
            UpdateWidth();
            InvalidateVisual();
            if (!_stale) return;
            // 미룬 동안 늘리기가 또 바뀌었을 수 있으므로 그때 받은 칸 수·그리기가 아니라 지금 상태로 다시 그린다.
            _stale = false;
            RenderRequested?.Invoke();
        }

        private Rect CellRect(int f)
        {
            int cols = Columns;
            return new(Gap + f % cols * (Cell + Gap), Top + f / cols * RowPitch, Cell, Cell);
        }

        /// <summary>칸 <paramref name="from"/>~<paramref name="to"/>을 줄마다 덮는 사각형(줄바꿈되면 줄마다 하나).</summary>
        private IEnumerable<Rect> RangeRects(int from, int to)
        {
            int cols = Columns;
            for (int row = from / cols; row <= to / cols; row++)
            {
                int a = Math.Max(from, row * cols), b = Math.Min(to, row * cols + cols - 1);
                yield return new Rect(Gap / 2.0 + a % cols * (Cell + Gap), Top - 2 + row * RowPitch, (b - a + 1) * (Cell + Gap), Cell + 4);
            }
        }

        public override void Render(DrawingContext g)
        {
            g.FillRectangle(AppTheme.BackBrush, new Rect(Bounds.Size));
            if (_thumbs.Length == 0) return;
            // 끄는 동안은 누른 때의 칸 차례로 끈 칸을, 아니면 받은 범위를 보인다.
            var (from, to) = _dragging && _dragTo >= 0 ? (Math.Min(_press, _dragTo), Math.Max(_press, _dragTo)) : _range;
            foreach (var r in RangeRects(from, to)) g.FillRectangle(RangeBrush, r);
            for (int f = 0; f < _thumbs.Length; f++)
            {
                var cell = CellRect(f);
                g.FillRectangle(AppTheme.InputBrush, cell);
                if (_thumbs[f] is { } bmp)
                {
                    var src = new Rectangle(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
                    double fit = Math.Min(cell.Width / src.Width, cell.Height / src.Height);
                    ImageUtil.DrawFitted(g, bmp, src, cell, fit >= 1 ? (int)Math.Floor(fit) : 0, checker: true);
                }
                if (f < from || f > to) g.FillRectangle(OutsideBrush, cell);
                if (_copies[f]) g.DrawRectangle(null, CopyPen, cell.Deflate(0.5));
                AppTheme.DrawTextIn(g, (f + 1).ToString(), new Rect(cell.X + 2, cell.Bottom - 14, cell.Width - 4, 13), AppTheme.TextBrush, AppTheme.SmallFontSize - 1);
            }
            // 마지막 칸 너머로 끄는 중: 더해질 빈 칸(점선)
            for (int f = _thumbs.Length; f <= to; f++)
            {
                var cell = CellRect(f);
                g.DrawRectangle(null, GhostPen, cell.Deflate(0.5));
                AppTheme.DrawTextIn(g, "+", cell, AppTheme.DimTextBrush, center: true);
                AppTheme.DrawTextIn(g, (f + 1).ToString(), new Rect(cell.X + 2, cell.Bottom - 14, cell.Width - 4, 13), AppTheme.TextBrush, AppTheme.SmallFontSize - 1);
            }
            foreach (var r in RangeRects(from, to)) g.DrawRectangle(null, RangePen, r.Deflate(1));
            if (_current >= 0 && _current < _thumbs.Length)
                g.DrawRectangle(null, CurrentPen, CellRect(_current).Deflate(1));
        }

        public void Dispose()
        {
            foreach (var b in _thumbs) b?.Dispose();
            _thumbs = [];
            _copies = [];
        }
    }
}
