using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 전·후 미리보기 아래 프레임 줄: 프레임마다 효과를 입힌 작은 그림. 고른 효과(없으면 "추가 범위")의 프레임 범위가 파랗게 보이고,
/// 끌어서 범위를 정하며(아래 숫자 칸과 같은 값), 누르면 그 프레임을 미리보기에 보인다.
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
            _play.Content = "▶ 재생";
            _previewFrame = f;
            RefreshPreview();
        };
        _strip.RangeDragged += (a, b) => SetRangeFromStrip(FrameRange.Of(a, b, _project.Frames.Count));
        // 효과를 바꿀 때마다 모든 프레임을 다시 그리지 않도록 잠깐 모았다가 그린다.
        _stripTimer.Tick += (_, _) =>
        {
            _stripTimer.Stop();
            _strip.SetFrames(_project.Frames.Count, RenderFrameThumb);
        };
        _strip.SetFrames(_project.Frames.Count, RenderFrameThumb);
        return new ScrollViewer
        {
            Content = _strip,
            Width = PreviewBox * 2 + 12,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>효과가 바뀜: 프레임 그림을 곧 다시 그린다(범위 표시는 바로).</summary>
    private void InvalidateStrip()
    {
        SyncStripRange();
        _stripTimer.Stop();
        _stripTimer.Start();
    }

    /// <summary>프레임 줄의 파란 범위: 고른 효과의 범위, 효과를 고르지 않았으면 추가 범위.</summary>
    private void SyncStripRange()
    {
        int n = _project.Frames.Count;
        _strip.Range = Selected >= 0 && Selected < _effects.Count ? RangeOf(_effects[Selected])
            : _addAll.On() ? (0, n - 1) : ((int)_addFrom.NumValue() - 1, (int)_addTo.NumValue() - 1);
    }

    /// <summary>프레임 줄에서 끌어 정한 범위를 고른 효과(없으면 추가 범위)에 넣는다. null = 모든 프레임.</summary>
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

    /// <summary>프레임 <paramref name="f"/>에 모든 효과를 입힌 그림(미리보기 "후"와 같은 크기).</summary>
    private PixelImage? RenderFrameThumb(int f)
    {
        if (SourceAt(f) is not { } source) return null;
        int n = _project.Frames.Count;
        var effects = LayerEffects.Animate(_effects, f, n, _project.EffectPivot(_layer, f), LayerEffects.UsesOtherFrames(_effects) ? SourceAt : null);
        return LayerEffects.Render(source, effects, LayerEffects.Padding(_effects, source.Width, source.Height)).Image;
    }

    /// <summary>프레임 칸 줄(가로 스크롤). 칸 = 효과를 입힌 그림 + 프레임 번호.</summary>
    private sealed class FrameStrip : Control, IDisposable
    {
        private const int Cell = 44, Gap = 2;
        private static readonly IBrush RangeBrush = AppTheme.Brush(Color.FromArgb(70, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
        private static readonly IBrush OutsideBrush = AppTheme.Brush(Color.FromArgb(140, 0, 0, 0));
        private static readonly IPen CurrentPen = new Pen(Brushes.White, 2);
        private static readonly IPen RangePen = new Pen(AppTheme.AccentBrush, 2);

        private WriteableBitmap?[] _thumbs = [];
        private (int From, int To) _range;
        private int _current, _press = -1;
        private bool _dragging;

        public FrameStrip()
        {
            Height = Cell + 6;
            Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(this, "누르면 그 프레임을 보고, 끌면 프레임 범위를 정합니다.");
        }

        /// <summary>누름(끌지 않음): 그 프레임을 미리보기에.</summary>
        public event Action<int>? FrameClicked;

        /// <summary>끄는 중: 누른 프레임 ~ 지금 프레임(작은 쪽, 큰 쪽).</summary>
        public event Action<int, int>? RangeDragged;

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

        public void SetFrames(int count, Func<int, PixelImage?> render)
        {
            Dispose();
            _thumbs = new WriteableBitmap?[count];
            for (int f = 0; f < count; f++)
                if (render(f) is { } img) _thumbs[f] = ImageUtil.ToBitmap(img);
            Width = Math.Max(1, count * (Cell + Gap) + Gap);
            InvalidateVisual();
        }

        private int FrameAt(double x) => Math.Clamp((int)((x - Gap) / (Cell + Gap)), 0, Math.Max(0, _thumbs.Length - 1));

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (_thumbs.Length == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _press = FrameAt(e.GetPosition(this).X);
            _dragging = false;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_press < 0) return;
            int f = FrameAt(e.GetPosition(this).X);
            if (!_dragging && f == _press) return;
            _dragging = true;
            RangeDragged?.Invoke(Math.Min(_press, f), Math.Max(_press, f));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_press < 0) return;
            int press = _press;
            _press = -1;
            e.Pointer.Capture(null);
            if (!_dragging) FrameClicked?.Invoke(press);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _press = -1;
        }

        public override void Render(DrawingContext g)
        {
            g.FillRectangle(AppTheme.BackBrush, new Rect(Bounds.Size));
            if (_thumbs.Length == 0) return;
            var (from, to) = _range;
            g.FillRectangle(RangeBrush, new Rect(Gap / 2.0 + from * (Cell + Gap), 0, (to - from + 1) * (Cell + Gap), Bounds.Height));
            for (int f = 0; f < _thumbs.Length; f++)
            {
                var cell = new Rect(Gap + f * (Cell + Gap), 3, Cell, Cell);
                g.FillRectangle(AppTheme.InputBrush, cell);
                if (_thumbs[f] is { } bmp)
                {
                    var src = new Rectangle(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
                    double fit = Math.Min(cell.Width / src.Width, cell.Height / src.Height);
                    ImageUtil.DrawFitted(g, bmp, src, cell, fit >= 1 ? (int)Math.Floor(fit) : 0, checker: true);
                }
                if (f < from || f > to) g.FillRectangle(OutsideBrush, cell);
                AppTheme.DrawTextIn(g, (f + 1).ToString(), new Rect(cell.X + 2, cell.Bottom - 14, cell.Width - 4, 13), AppTheme.TextBrush, AppTheme.SmallFontSize - 1);
            }
            g.DrawRectangle(null, RangePen, new Rect(Gap / 2.0 + from * (Cell + Gap) + 1, 1, (to - from + 1) * (Cell + Gap) - 2, Bounds.Height - 2));
            if (_current >= 0 && _current < _thumbs.Length)
                g.DrawRectangle(null, CurrentPen, new Rect(Gap + _current * (Cell + Gap), 3, Cell, Cell).Deflate(1));
        }

        public void Dispose()
        {
            foreach (var b in _thumbs) b?.Dispose();
            _thumbs = [];
        }
    }
}
