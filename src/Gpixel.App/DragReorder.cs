using Avalonia.VisualTree;

namespace Gpixel.App;

/// <summary>
/// 끌어서 순서 바꾸기의 공통 계산(목록 컨트롤 <see cref="DragReorder"/>와 직접 그리는 타임라인이 함께 쓴다):
/// 끌기 문턱, 놓을 자리(옮긴 뒤 번호), 미리보기 순서, 미리보기 배치(칸 사이 틈 유지), 가장자리 자동 스크롤 속도.
/// 한 축(세로 또는 가로) 기준이며, 여러 칸을 한 덩어리로 옮기는 곳은 덩어리를 한 단위로 넘긴다.
/// </summary>
public static class ReorderCore
{
    /// <summary>누른 뒤 이만큼(px) 움직여야 끌기(그 전에 떼면 클릭 = 고르기).</summary>
    public const double Threshold = 4;

    /// <summary>다른 칸이 비켜 서는 애니메이션 길이.</summary>
    public static readonly long DurationTicks = System.Diagnostics.Stopwatch.Frequency * 120 / 1000;

    /// <summary>가장자리 자동 스크롤이 시작되는 띠 폭(px)과 한 번(16ms)에 움직이는 최대 거리(px).</summary>
    public const double EdgeZone = 24, EdgeMaxStep = 14;

    public static bool Exceeded(Avalonia.Point press, Avalonia.Point now)
        => Math.Max(Math.Abs(now.X - press.X), Math.Abs(now.Y - press.Y)) >= Threshold;

    /// <summary>
    /// 끄는 칸의 가운데가 <paramref name="center"/>일 때 옮긴 뒤 번호: 끄는 칸을 뺀 나머지 중 가운데가 그보다 앞인 칸 수.
    /// <paramref name="starts"/>·<paramref name="sizes"/>는 끌기 전(제자리) 배치.
    /// </summary>
    public static int TargetIndex(IReadOnlyList<double> starts, IReadOnlyList<double> sizes, int from, double center)
    {
        int to = 0;
        for (int i = 0; i < starts.Count; i++)
            if (i != from && starts[i] + sizes[i] / 2 < center) to++;
        return to;
    }

    /// <summary>미리보기 순서: 칸 번호를 새 자리 차례로(<paramref name="from"/>을 <paramref name="to"/>번으로).</summary>
    public static int[] Order(int count, int from, int to)
    {
        var order = new List<int>(count);
        for (int i = 0; i < count; i++)
            if (i != from) order.Add(i);
        order.Insert(Math.Clamp(to, 0, order.Count), from);
        return [.. order];
    }

    /// <summary>
    /// <paramref name="sequence"/> 차례로 다시 쌓은 시작 위치(원래 번호별). 칸 k번 자리 뒤의 틈(다음 칸 시작 − 이 칸 끝)은 자리마다 그대로 둔다 —
    /// 칸 사이에 고정된 것(줄바꿈 칸 등)이나 간격이 있어도 맞다. 첫 자리는 원래 첫 칸 시작.
    /// </summary>
    public static double[] Stack(IReadOnlyList<double> starts, IReadOnlyList<double> sizes, IReadOnlyList<int> sequence)
    {
        int n = starts.Count;
        var result = new double[n];
        if (n == 0) return result;
        double pos = starts[0];
        for (int k = 0; k < n; k++)
        {
            int i = sequence[k];
            result[i] = pos;
            double gap = k + 1 < n ? starts[k + 1] - (starts[k] + sizes[k]) : 0;
            pos += sizes[i] + gap;
        }
        return result;
    }

    /// <summary>보이는 범위 [<paramref name="start"/>, <paramref name="end"/>]의 가장자리 띠 안(또는 밖)이면 그쪽 스크롤 거리(−: 앞쪽), 아니면 0.</summary>
    public static double EdgeSpeed(double pos, double start, double end)
    {
        if (end - start < EdgeZone * 3) return 0;
        if (pos < start + EdgeZone) return -EdgeMaxStep * Math.Min(1, (start + EdgeZone - pos) / EdgeZone);
        if (pos > end - EdgeZone) return EdgeMaxStep * Math.Min(1, (pos - end + EdgeZone) / EdgeZone);
        return 0;
    }
}

/// <summary>한 값의 ease-out cubic 이동(<see cref="ReorderCore.DurationTicks"/>, Stopwatch 시각).</summary>
public struct ReorderTween
{
    public double From, To;
    public long Start;

    public readonly double At(long now)
    {
        if (From == To) return To;
        double t = (double)(now - Start) / ReorderCore.DurationTicks;
        if (t >= 1) return To;
        double u = 1 - t;
        return From + (To - From) * (1 - u * u * u);
    }

    public void Go(double target, long now, bool animate)
    {
        if (!animate) { From = To = target; return; }
        if (target == To) return;
        From = At(now);
        To = target;
        Start = now;
    }

    /// <summary>끝났으면 목표에 고정하고 true.</summary>
    public bool Settle(long now)
    {
        if (From != To && now - Start >= ReorderCore.DurationTicks) From = To;
        return From == To;
    }
}

/// <summary>끄는 동안 가장자리에 멈춰 있어도 계속 스크롤하는 타이머(16ms). <see cref="Set"/>으로 속도를 주고 0이면 멈춘다.</summary>
public sealed class ReorderAutoScroll
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _speed;

    /// <param name="scrollBy">거리만큼 스크롤하고 실제로 움직였으면 true(그 뒤 놓을 자리를 다시 셈).</param>
    public ReorderAutoScroll(Func<double, bool> scrollBy)
    {
        _timer.Tick += (_, _) =>
        {
            if (_speed == 0 || !scrollBy(_speed)) _timer.Stop();
        };
    }

    public void Set(double speed)
    {
        _speed = speed;
        if (speed == 0) _timer.Stop();
        else if (!_timer.IsEnabled) _timer.Start();
    }
}

/// <summary>
/// 컨트롤 목록의 끌어서 순서 바꾸기(모든 목록이 같은 모양·동작): 줄을 누른 채 <see cref="ReorderCore.Threshold"/>px 넘게 끌면
/// 그 줄이 불투명한 강조 바탕으로 떠서(다른 줄 위에) 포인터를 따라오고, 다른 줄은 120ms 동안 미끄러져 놓을 자리에 틈을 연다(같은 부모의 고정된 줄도 함께 다시 쌓임).
/// 놓으면 <see cref="Moved"/>(원래 번호, 옮긴 뒤 번호)를 한 번 부른다 — 부르는 쪽이 실행취소 한 단계로 반영하고 목록을 다시 만든다.
/// Esc · 붙잡기를 잃음 · 목록 옆(가로축) 밖에서 놓기는 취소(줄이 제자리로 돌아감). 스크롤되는 목록이면 가장자리에서 자동으로 스크롤한다.
/// 누르기만 하고 떼면 아무것도 하지 않으므로 클릭(고르기)은 그대로 된다. 단추·체크 상자·글 상자를 누른 것은 끌기가 아니다.
/// </summary>
public sealed class DragReorder
{
    private readonly Orientation _axis;
    private readonly List<(Control Item, Control Handle)> _items = new();

    // 누른 상태
    private int _from = -1;
    private Avalonia.Point _press;
    private Control? _handle;
    // 끄는 상태
    private bool _dragging;
    private Panel? _panel;
    private ScrollViewer? _scroll;
    private List<Control> _siblings = new();
    private double[] _starts = [], _sizes = [];
    private int[] _itemSibling = [];
    private int _to;
    private Avalonia.Point _lastInScroll, _lastInPanel;
    private IPointer? _pointer;
    private TopLevel? _top;
    private readonly Dictionary<Control, ReorderTween> _tweens = new();
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly ReorderAutoScroll _autoScroll;
    /// <summary>뜬 줄의 원래 바탕(직접 정했던 값, 아니면 스타일 값으로 되돌리도록 null 속성).</summary>
    private (AvaloniaProperty? Property, bool WasSet, object? Value) _savedBackground;

    private static AvaloniaProperty? BackgroundProperty(Control c) => c switch
    {
        TemplatedControl => TemplatedControl.BackgroundProperty,
        Panel => Panel.BackgroundProperty,
        Border => Border.BackgroundProperty,
        _ => null,
    };

    /// <summary>뜬 줄은 불투명한 바탕(밑의 줄 글자가 비치지 않게).</summary>
    private void Lift(Control c)
    {
        var property = BackgroundProperty(c);
        _savedBackground = (property, property is not null && c.IsSet(property), property is null ? null : c.GetValue(property));
        if (property is not null) c.SetValue(property, AppTheme.HoverBrush);
    }

    private void Drop(Control c)
    {
        var (property, wasSet, value) = _savedBackground;
        _savedBackground = default;
        if (property is null) return;
        if (wasSet) c.SetValue(property, value);
        else c.ClearValue(property);
    }

    /// <param name="axis">줄이 늘어선 방향(세로 목록 = Vertical).</param>
    public DragReorder(Orientation axis = Orientation.Vertical)
    {
        _axis = axis;
        _anim.Tick += (_, _) => Animate();
        _autoScroll = new ReorderAutoScroll(ScrollBy);
    }

    /// <summary>놓음: (원래 번호, 옮긴 뒤 번호). 제자리면 부르지 않는다.</summary>
    public event Action<int, int>? Moved;

    /// <summary>목록을 다시 만들 때: 등록한 줄을 모두 지운다(끄는 중이면 취소).</summary>
    public void Clear()
    {
        if (_dragging || _from >= 0) End(commit: false, animate: false);
        _items.Clear();
    }

    /// <summary>
    /// 줄 하나를 차례대로 등록한다(번호 = 등록 순서). 줄들은 같은 부모 패널에 있어야 한다.
    /// <paramref name="handle"/>을 누르면 끌기 시작(기본 = 줄 전체).
    /// </summary>
    public void Attach(Control item, Control? handle = null)
    {
        handle ??= item;
        int index = _items.Count;
        _items.Add((item, handle));
        handle.AddHandler(InputElement.PointerPressedEvent, (_, e) => OnPressed(index, handle, e), RoutingStrategies.Tunnel);
        handle.AddHandler(InputElement.PointerMovedEvent, (_, e) => OnMoved(handle, e), RoutingStrategies.Tunnel);
        handle.AddHandler(InputElement.PointerReleasedEvent, (_, e) => OnReleased(handle, e), RoutingStrategies.Tunnel);
        handle.PointerCaptureLost += (_, _) =>
        {
            if (ReferenceEquals(_handle, handle) && _dragging) End(commit: false, animate: true);
        };
    }

    private static bool IsInteractive(object? source)
        => source is Visual v && (v.FindAncestorOfType<Button>(includeSelf: true) is not null
                                  || v.FindAncestorOfType<TextBox>(includeSelf: true) is not null
                                  || v.FindAncestorOfType<NumericUpDown>(includeSelf: true) is not null);

    private void OnPressed(int index, Control handle, PointerPressedEventArgs e)
    {
        if (_dragging || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || IsInteractive(e.Source)) return;
        if (index >= _items.Count || _items[index].Item.GetVisualParent() is not Panel panel) return;
        _from = index;
        _handle = handle;
        _panel = panel;
        _press = e.GetPosition(panel);
    }

    private void OnMoved(Control handle, PointerEventArgs e)
    {
        if (_from < 0 || !ReferenceEquals(_handle, handle) || _panel is not { } panel) return;
        if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            if (_dragging) End(commit: false, animate: true);
            else _from = -1;
            return;
        }
        var p = e.GetPosition(panel);
        if (!_dragging)
        {
            if (!ReorderCore.Exceeded(_press, p)) return;
            Begin(e.Pointer);
            if (!_dragging) return;
        }
        e.Handled = true;
        _lastInPanel = p;
        if (_scroll is { } sv)
        {
            _lastInScroll = e.GetPosition(sv);
            double pos = Along(_lastInScroll), extent = _axis == Orientation.Vertical ? sv.Viewport.Height : sv.Viewport.Width;
            _autoScroll.Set(ReorderCore.EdgeSpeed(pos, 0, extent));
        }
        Update();
    }

    private void OnReleased(Control handle, PointerReleasedEventArgs e)
    {
        if (_from < 0 || !ReferenceEquals(_handle, handle)) return;
        if (!_dragging) { _from = -1; return; }
        e.Handled = true;
        // 목록 옆(가로축)으로 크게 벗어나 놓으면 취소.
        bool outside = _scroll is { } sv ? Across(e.GetPosition(sv)) is var c && (c < -32 || c > Across(sv.Bounds.Size) + 32)
            : _panel is { } panel && Across(e.GetPosition(panel)) is var d && (d < -32 || d > Across(panel.Bounds.Size) + 32);
        End(commit: !outside, animate: outside);
    }

    private double Along(Avalonia.Point p) => _axis == Orientation.Vertical ? p.Y : p.X;
    private double Across(Avalonia.Point p) => _axis == Orientation.Vertical ? p.X : p.Y;
    private double Across(Avalonia.Size s) => _axis == Orientation.Vertical ? s.Width : s.Height;
    private double Along(Rect r) => _axis == Orientation.Vertical ? r.Y : r.X;
    private double Length(Rect r) => _axis == Orientation.Vertical ? r.Height : r.Width;

    private void Begin(IPointer pointer)
    {
        var panel = _panel!;
        // 같은 부모의 모든 줄(고정된 줄 포함)을 제자리 순서로: 등록한 줄은 자리를 바꾸고 나머지는 자리(번호)를 지킨다.
        _siblings = panel.Children.Where(c => c.IsVisible).OrderBy(c => Along(c.Bounds)).ToList();
        _itemSibling = _items.Select(x => _siblings.IndexOf(x.Item)).ToArray();
        if (_itemSibling.Any(s => s < 0)) { _from = -1; _siblings = new(); return; }
        _starts = _siblings.Select(c => Along(c.Bounds)).ToArray();
        _sizes = _siblings.Select(c => Length(c.Bounds)).ToArray();
        _dragging = true;
        _pointer = pointer;
        _to = _from;
        _scroll = panel.FindAncestorOfType<ScrollViewer>();
        foreach (var c in _siblings)
        {
            c.RenderTransform = new TranslateTransform();
            _tweens[c] = default;
        }
        var dragged = _items[_from].Item;
        dragged.ZIndex = 100;
        dragged.Opacity = 1;
        Lift(dragged);
        dragged.Cursor = new Cursor(_axis == Orientation.Vertical ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast);
        pointer.Capture(_handle);
        _top = TopLevel.GetTopLevel(panel);
        _top?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_dragging || e.Key != Key.Escape) return;
        e.Handled = true;
        End(commit: false, animate: true);
    }

    private bool ScrollBy(double delta)
    {
        if (!_dragging || _scroll is not { } sv || _panel is not { } panel) return false;
        var old = sv.Offset;
        // 스크롤 전 배치로 포인터의 내용 좌표를 잡고, 내용이 움직인 만큼 더한다(새 배치는 다음 레이아웃에서 반영됨).
        var before = sv.TranslatePoint(_lastInScroll, panel) ?? _lastInPanel;
        sv.Offset = _axis == Orientation.Vertical ? old.WithY(Math.Clamp(old.Y + delta, 0, Math.Max(0, sv.Extent.Height - sv.Viewport.Height)))
            : old.WithX(Math.Clamp(old.X + delta, 0, Math.Max(0, sv.Extent.Width - sv.Viewport.Width)));
        if (sv.Offset == old) return false;
        _lastInPanel = before + new Avalonia.Point(sv.Offset.X - old.X, sv.Offset.Y - old.Y);
        Update();
        return true;
    }

    /// <summary>포인터 위치로 놓을 자리를 다시 셈하고 줄들의 목표 위치를 맞춘다.</summary>
    private void Update()
    {
        if (!_dragging) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        int fromSib = _itemSibling[_from];
        double offset = Along(_lastInPanel) - Along(_press);
        // 뜬 줄은 등록한 줄들의 범위 안에서만 따라오고(고정된 줄 위로 나가지 않게), 놓을 자리는 가운데로 센다(끝까지 끌면 맨 앞·맨 뒤).
        var itemStarts = _itemSibling.Select(s => _starts[s]).ToArray();
        var itemSizes = _itemSibling.Select(s => _sizes[s]).ToArray();
        double first = itemStarts.Min(), last = itemStarts.Zip(itemSizes, (s, z) => s + z).Max();
        double raw = offset;
        offset = Math.Clamp(raw, first - _starts[fromSib], last - _starts[fromSib] - _sizes[fromSib]);
        double center = _starts[fromSib] + Math.Clamp(raw, first - _starts[fromSib] - _sizes[fromSib] / 2, last - _starts[fromSib] - _sizes[fromSib] / 2) + _sizes[fromSib] / 2;
        _to = ReorderCore.TargetIndex(itemStarts, itemSizes, _from, center);
        // 형제 줄 순서: 등록한 줄이 놓인 자리들에 미리보기 순서대로 채우고, 나머지는 제자리.
        var order = ReorderCore.Order(_items.Count, _from, _to);
        var sequence = Enumerable.Range(0, _siblings.Count).ToArray();
        var itemSlots = _itemSibling.OrderBy(s => s).ToArray();
        for (int k = 0; k < order.Length; k++) sequence[itemSlots[k]] = _itemSibling[order[k]];
        var stacked = ReorderCore.Stack(_starts, _sizes, sequence);
        for (int s = 0; s < _siblings.Count; s++)
        {
            var c = _siblings[s];
            var tween = _tweens[c];
            if (s == fromSib) tween.Go(offset, now, animate: false);
            else tween.Go(stacked[s] - _starts[s], now, animate: true);
            _tweens[c] = tween;
        }
        Animate();
    }

    private void Animate()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        bool moving = false;
        foreach (var c in _siblings)
        {
            if (!_tweens.TryGetValue(c, out var tween)) continue;
            moving |= !tween.Settle(now);
            _tweens[c] = tween;
            if (c.RenderTransform is TranslateTransform t)
            {
                double v = tween.At(now);
                if (_axis == Orientation.Vertical) t.Y = v;
                else t.X = v;
            }
        }
        if (moving) { if (!_anim.IsEnabled) _anim.Start(); }
        else
        {
            _anim.Stop();
            if (!_dragging) Reset();
        }
    }

    private void End(bool commit, bool animate)
    {
        bool dragging = _dragging;
        int from = _from, to = _to;
        _from = -1;
        _dragging = false;
        _autoScroll.Set(0);
        _top?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _top = null;
        var handle = _handle;
        _handle = null;
        if (!dragging) return;
        if (from < _items.Count)
        {
            var dragged = _items[from].Item;
            dragged.Opacity = 1;
            Drop(dragged);
            dragged.Cursor = null;
        }
        if (_pointer is { } pointer && ReferenceEquals(pointer.Captured, handle)) pointer.Capture(null);
        _pointer = null;
        if (commit)
        {
            Reset();
            if (to != from) Moved?.Invoke(from, to);
            return;
        }
        if (!animate) { Reset(); return; }
        // 취소: 모든 줄이 제자리로 미끄러져 돌아간다.
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var c in _siblings)
            if (_tweens.TryGetValue(c, out var tween)) { tween.Go(0, now, animate: true); _tweens[c] = tween; }
        Animate();
    }

    /// <summary>줄 위치 변환·겹침 순서를 원래대로.</summary>
    private void Reset()
    {
        _anim.Stop();
        foreach (var c in _siblings)
        {
            c.RenderTransform = null;
            c.ZIndex = 0;
        }
        _siblings = new();
        _tweens.Clear();
    }
}
