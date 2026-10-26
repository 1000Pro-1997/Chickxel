using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 그라데이션 바(도구 설정, PowerPoint/Photoshop 방식): 왼쪽 0% ~ 오른쪽 100% 미리보기 띠(체커 위, 불투명도 반영),
/// 띠 **위**에 불투명도 정지점 상자(회색 = 불투명도), 띠 **아래**에 색 정지점 상자.
/// 띠 위쪽 빈 곳 클릭 = 그 위치에 불투명도 정지점 추가, 띠·아래쪽 빈 곳 클릭 = 색 정지점 추가(값은 그 지점의 지금 값),
/// 상자 클릭 = 고르기(강조), 상자 좌우 끌기 = 위치 바꾸기, Delete = 고른 정지점 삭제(종류마다 2개는 남긴다).
/// </summary>
public sealed class GradientBar : Control
{
    private const double Side = 7;          // 좌우 여백(상자 반 폭)
    private const double MarkerWidth = 11, MarkerHeight = 14;
    private const double AlphaTop = 0;
    private const double BandTop = MarkerHeight + 3;
    private const double BandHeight = 18;
    private const double BandBottom = BandTop + BandHeight;
    private const double ColorTop = BandBottom + 3;

    private static readonly IPen OutlinePen = AppTheme.Pen(AppTheme.BorderColor);
    private static readonly IPen MarkerPen = AppTheme.Pen(Color.FromRgb(200, 200, 205));
    private static readonly IPen SelectedPen = AppTheme.Pen(AppTheme.AccentColor, 2);

    private IReadOnlyList<GradientStop> _stops = EditorContext.DefaultGradientStops;
    private IReadOnlyList<GradientAlphaStop> _alphaStops = EditorContext.DefaultGradientAlphaStops;
    private GradientInterpolation _mode;
    private GradientColorSpace _colorSpace = GradientColorSpace.OkLch;
    private bool _selectedAlpha;
    private int _selected;
    private bool _dragging;

    public GradientBar()
    {
        Height = ColorTop + MarkerHeight + 2;
        MinWidth = 120;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>색 정지점이 바뀌었다(추가·이동·삭제). 새 목록(편집 순서 유지)을 넘긴다.</summary>
    public event Action<IReadOnlyList<GradientStop>>? StopsChanged;
    /// <summary>불투명도 정지점이 바뀌었다(추가·이동·삭제).</summary>
    public event Action<IReadOnlyList<GradientAlphaStop>>? AlphaStopsChanged;
    /// <summary>고른 정지점(종류·번호)이 바뀌었다.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>색 정지점(화면 표시용, 바깥에서 맞춘다).</summary>
    public IReadOnlyList<GradientStop> Stops
    {
        get => _stops;
        set
        {
            _stops = value;
            if (!_selectedAlpha) _selected = Math.Clamp(_selected, 0, _stops.Count - 1);
            InvalidateVisual();
        }
    }

    /// <summary>불투명도 정지점(화면 표시용, 바깥에서 맞춘다).</summary>
    public IReadOnlyList<GradientAlphaStop> AlphaStops
    {
        get => _alphaStops;
        set
        {
            _alphaStops = value;
            if (_selectedAlpha) _selected = Math.Clamp(_selected, 0, _alphaStops.Count - 1);
            InvalidateVisual();
        }
    }

    /// <summary>미리보기 띠의 보간 방식.</summary>
    public GradientInterpolation Mode
    {
        get => _mode;
        set { _mode = value; InvalidateVisual(); }
    }

    /// <summary>미리보기 띠와 새 색 정지점의 색 공간.</summary>
    public GradientColorSpace ColorSpace
    {
        get => _colorSpace;
        set { _colorSpace = value; InvalidateVisual(); }
    }

    /// <summary>고른 정지점이 불투명도 정지점인지(아니면 색 정지점).</summary>
    public bool SelectedIsAlpha => _selectedAlpha;

    /// <summary>고른 정지점 번호(그 종류 목록의 순서).</summary>
    public int SelectedIndex => _selected;

    private int Count(bool alpha) => alpha ? _alphaStops.Count : _stops.Count;
    private double PositionOf(bool alpha, int i) => alpha ? _alphaStops[i].Position : _stops[i].Position;

    private void Select(bool alpha, int index)
    {
        index = Math.Clamp(index, 0, Math.Max(0, Count(alpha) - 1));
        if (_selectedAlpha == alpha && _selected == index) return;
        _selectedAlpha = alpha;
        _selected = index;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>고른 정지점을 지운다(그 종류가 2개 이하면 안 지움). 지웠으면 true.</summary>
    public bool DeleteSelected()
    {
        if (Count(_selectedAlpha) <= 2) return false;
        if (_selectedAlpha)
        {
            var list = _alphaStops.ToList();
            list.RemoveAt(_selected);
            _selected = Math.Min(_selected, list.Count - 1);
            CommitAlpha(list);
        }
        else
        {
            var list = _stops.ToList();
            list.RemoveAt(_selected);
            _selected = Math.Min(_selected, list.Count - 1);
            Commit(list);
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private double BandWidth => Math.Max(1, Bounds.Width - 2 * Side);
    private double XOf(double position) => Side + position * BandWidth;
    private double PositionAt(double x) => Math.Clamp((x - Side) / BandWidth, 0, 1);

    private void Commit(List<GradientStop> list)
    {
        _stops = list;
        InvalidateVisual();
        StopsChanged?.Invoke(list);
    }

    private void CommitAlpha(List<GradientAlphaStop> list)
    {
        _alphaStops = list;
        InvalidateVisual();
        AlphaStopsChanged?.Invoke(list);
    }

    public override void Render(DrawingContext g)
    {
        var band = new Rect(Side, BandTop, BandWidth, BandHeight);
        AppTheme.DrawChecker(g, band, 4);
        var sorted = Gradient.Sorted(_stops);
        var sortedAlpha = Gradient.SortedAlpha(_alphaStops);
        // 한 화면 픽셀 열마다 그 위치 색(디더링 없이 보간 방식만, 불투명도 반영).
        int cols = (int)Math.Ceiling(band.Width);
        for (int i = 0; i < cols; i++)
        {
            double t = cols <= 1 ? 0 : i / (double)(cols - 1);
            var c = Gradient.Sample(sorted, sortedAlpha, t, _mode, colorSpace: _colorSpace);
            if (c.A == 0) continue;
            g.FillRectangle(new SolidColorBrush(c.ToColor()), new Rect(band.X + i, BandTop, Math.Min(1, band.Width - i) + 0.01, BandHeight));
        }
        g.DrawRectangle(null, OutlinePen, band.Inflate(0.5));

        // 정지점 상자: 고른 것을 맨 위에.
        for (int i = 0; i < _alphaStops.Count; i++) if (!(_selectedAlpha && i == _selected)) DrawMarker(g, true, i);
        for (int i = 0; i < _stops.Count; i++) if (_selectedAlpha || i != _selected) DrawMarker(g, false, i);
        if (Count(_selectedAlpha) > 0) DrawMarker(g, _selectedAlpha, _selected);
    }

    private void DrawMarker(DrawingContext g, bool alpha, int i)
    {
        double x = Math.Round(XOf(PositionOf(alpha, i)));
        bool sel = alpha == _selectedAlpha && i == _selected;
        var pen = sel ? SelectedPen : MarkerPen;
        // 띠를 가리키는 작은 삼각형 + 상자(불투명도는 띠 위, 색은 띠 아래).
        double boxTop = alpha ? AlphaTop : ColorTop;
        double tipBase = alpha ? AlphaTop + MarkerHeight : ColorTop;
        double tipPoint = alpha ? BandTop - 0.5 : BandBottom + 0.5;
        var tip = new StreamGeometry();
        using (var c = tip.Open())
        {
            c.BeginFigure(new Avalonia.Point(x, tipPoint), true);
            c.LineTo(new Avalonia.Point(x + 3.5, tipBase));
            c.LineTo(new Avalonia.Point(x - 3.5, tipBase));
            c.EndFigure(true);
        }
        g.DrawGeometry(sel ? AppTheme.AccentBrush : MarkerPen.Brush, null, tip);
        var box = new Rect(x - MarkerWidth / 2, boxTop, MarkerWidth, MarkerHeight);
        if (alpha)
        {
            // 불투명도: 검정(투명) ~ 흰색(불투명) 회색.
            byte v = _alphaStops[i].Alpha;
            g.FillRectangle(new SolidColorBrush(Color.FromRgb(v, v, v)), box);
        }
        else ImageUtil.DrawSwatch(g, box, _stops[i].Color);
        g.DrawRectangle(null, pen, box);
    }

    /// <summary>화면 점에 있는 정지점(고른 것 먼저). 없으면 null.</summary>
    private (bool Alpha, int Index)? MarkerAt(Avalonia.Point p)
    {
        bool alpha;
        if (p.Y < BandTop) alpha = true;
        else if (p.Y >= BandBottom - 2) alpha = false;
        else return null;
        bool Hit(int i) => Math.Abs(p.X - XOf(PositionOf(alpha, i))) <= MarkerWidth / 2 + 1;
        if (alpha == _selectedAlpha && Count(alpha) > 0 && Hit(_selected)) return (alpha, _selected);
        for (int i = Count(alpha) - 1; i >= 0; i--) if (Hit(i)) return (alpha, i);
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        e.Handled = true;
        var p = e.GetPosition(this);
        if (MarkerAt(p) is { } hit) Select(hit.Alpha, hit.Index);
        else
        {
            // 빈 곳: 그 위치의 지금 값(부드럽게 보간한 값)으로 정지점을 더한다. 띠 위쪽 = 불투명도, 띠·아래쪽 = 색.
            double pos = Math.Round(PositionAt(p.X), 3);
            if (p.Y < BandTop)
            {
                var list = _alphaStops.ToList();
                list.Add(new GradientAlphaStop(pos, Gradient.AlphaAt(Gradient.SortedAlpha(_alphaStops), pos)));
                _selectedAlpha = true;
                _selected = list.Count - 1;
                CommitAlpha(list);
            }
            else
            {
                var list = _stops.ToList();
                list.Add(new GradientStop(pos, Gradient.ColorAt(Gradient.Sorted(_stops), pos, _mode, colorSpace: _colorSpace)));
                _selectedAlpha = false;
                _selected = list.Count - 1;
                Commit(list);
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        _dragging = true;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _selected >= Count(_selectedAlpha)) return;
        double pos = Math.Round(PositionAt(e.GetPosition(this).X), 3);
        if (pos == PositionOf(_selectedAlpha, _selected)) return;
        if (_selectedAlpha)
        {
            var list = _alphaStops.ToList();
            list[_selected] = list[_selected] with { Position = pos };
            CommitAlpha(list);
        }
        else
        {
            var list = _stops.ToList();
            list[_selected] = list[_selected] with { Position = pos };
            Commit(list);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back && DeleteSelected()) e.Handled = true;
        else if (e.Key == Key.Left) { Select(_selectedAlpha, _selected - 1); e.Handled = true; }
        else if (e.Key == Key.Right) { Select(_selectedAlpha, _selected + 1); e.Handled = true; }
        else if (e.Key is Key.Up or Key.Down) { Select(e.Key == Key.Up, 0); e.Handled = true; }
    }
}
