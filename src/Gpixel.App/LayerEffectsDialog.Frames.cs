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
            _timer.Stop();
            _play.Content = Ui.IconLabel(VectorIcon.Play, "재생");
            _previewFrame = f;
            RefreshPreview();
        };
        _strip.DragStarted += () => _dragBase = (_extension, VirtualCount);
        _strip.RangeDragged += DragRange;
        // 효과를 바꿀 때마다 모든 프레임을 다시 그리지 않도록 잠깐 모았다가 그린다.
        _stripTimer.Tick += (_, _) =>
        {
            _stripTimer.Stop();
            RenderStrip();
        };
        RenderStrip();
        return new ScrollViewer
        {
            Content = _strip,
            Width = PreviewBox * 2 + 12,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
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

    /// <summary>프레임 칸 줄(가로 스크롤). 칸 = 효과를 입힌 그림 + 프레임 번호.</summary>
    private sealed class FrameStrip : Control, IDisposable
    {
        private const int Cell = 44, Gap = 2;
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
        /// <summary>끄는 동안 미룬 칸 다시 그리기(칸 수가 바뀌면 끄는 칸 차례가 흔들리지 않게 놓을 때 그림).</summary>
        private Action? _deferred;

        public FrameStrip()
        {
            Height = Cell + 6;
            HorizontalAlignment = HorizontalAlignment.Left; // 칸이 늘어도(끌어 늘리기) 앞 칸 자리가 그대로
            Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(this, "누르면 그 프레임을 보고, 끌면 프레임 범위를 정합니다. 효과를 고른 채 마지막 칸 너머로 끌면 프레임을 늘립니다(점선 칸).");
        }

        /// <summary>누름(끌지 않음): 그 프레임을 미리보기에.</summary>
        public event Action<int>? FrameClicked;

        /// <summary>누름: 끌기 시작(이때의 칸 차례로 <see cref="RangeDragged"/>를 읽음).</summary>
        public event Action? DragStarted;

        /// <summary>끄는 중: 누른 칸 ~ 지금 칸(작은 쪽, 큰 쪽). <see cref="CanExtend"/>면 큰 쪽이 칸 수 이상(마지막 칸 너머)일 수 있다.</summary>
        public event Action<int, int>? RangeDragged;

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
                _deferred = () => SetFrames(count, render, isCopy);
                return;
            }
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

        private void UpdateWidth() => Width = Math.Max(1, Math.Max(_thumbs.Length, _dragTo + 1) * (Cell + Gap) + Gap);

        private int CellAt(double x) => Math.Max(0, (int)Math.Floor((x - Gap) / (Cell + Gap)));

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (_thumbs.Length == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _press = Math.Min(CellAt(e.GetPosition(this).X), _thumbs.Length - 1);
            _dragging = false;
            DragStarted?.Invoke();
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_press < 0) return;
            int f = Math.Min(CellAt(e.GetPosition(this).X), CanExtend ? _thumbs.Length + FrameExtension.MaxCount : _thumbs.Length - 1);
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
            var deferred = _deferred;
            _deferred = null;
            deferred?.Invoke();
            UpdateWidth();
            InvalidateVisual();
        }

        private static Rect CellRect(int f) => new(Gap + f * (Cell + Gap), 3, Cell, Cell);

        public override void Render(DrawingContext g)
        {
            g.FillRectangle(AppTheme.BackBrush, new Rect(Bounds.Size));
            if (_thumbs.Length == 0) return;
            // 끄는 동안은 누른 때의 칸 차례로 끈 칸을, 아니면 받은 범위를 보인다.
            var (from, to) = _dragging && _dragTo >= 0 ? (Math.Min(_press, _dragTo), Math.Max(_press, _dragTo)) : _range;
            g.FillRectangle(RangeBrush, new Rect(Gap / 2.0 + from * (Cell + Gap), 0, (to - from + 1) * (Cell + Gap), Bounds.Height));
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
            g.DrawRectangle(null, RangePen, new Rect(Gap / 2.0 + from * (Cell + Gap) + 1, 1, (to - from + 1) * (Cell + Gap) - 2, Bounds.Height - 2));
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
