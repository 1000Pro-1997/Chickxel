using Avalonia.Controls.Primitives;

namespace Gpixel.App;

/// <summary>
/// 모든 숫자 상자(<see cref="NumericUpDown"/>) 공통: 좌우로 끌면 값이 바뀌고(Blender식), 끌다가 화면 끝에 닿으면 커서가 반대편 끝으로 넘어가
/// 계속 끌 수 있다. 끌지 않고 클릭하면 글자 입력(전체 선택). 위/아래 단추는 쓰지 않는다.
/// <list type="bullet">
/// <item>3px마다 한 칸(<see cref="NumericUpDown.Increment"/>), Shift = 10배 느리게, Ctrl(⌘) = 한 번에 10칸.</item>
/// <item>입력 중(포커스가 있는) 상자는 보통 글상자처럼 마우스로 글자를 고른다.</item>
/// <item>끄는 동안의 값 변화는 <see cref="Started"/>~<see cref="Ended"/> 사이에 일어나므로 받는 쪽이 실행취소를 한 단계로 묶을 수 있다.</item>
/// </list>
/// </summary>
public static class NumberScrub
{
    private const double PixelsPerStep = 3;
    private const double DragThreshold = 3;

    /// <summary>값 끌기 시작(첫 값 변화 전).</summary>
    public static event EventHandler<NumericUpDown>? Started;
    /// <summary>값 끌기 끝.</summary>
    public static event EventHandler<NumericUpDown>? Ended;

    private sealed class State
    {
        public bool Pressed, Scrubbing;
        public PixelPoint Start, Last;
        public double Pixels;
        public decimal BaseValue;
        public KeyModifiers Mods;
        public TextBox? Text;
    }

    private static readonly Cursor ScrubCursor = new(StandardCursorType.SizeWestEast);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NumericUpDown, State> States = new();

    /// <summary>앱 시작 때 한 번: 모든 숫자 상자에 붙인다.</summary>
    public static void Install()
    {
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<NumericUpDown>((n, e) =>
        {
            n.ShowButtonSpinner = false;
            var text = e.NameScope.Find<TextBox>("PART_TextBox");
            if (States.TryGetValue(n, out var existing))
            {
                existing.Text = text;
                return;
            }
            States.Add(n, new State { Text = text });
            if (text is not null)
            {
                void UpdateCursor() => text.Cursor = text.IsKeyboardFocusWithin ? null : ScrubCursor;
                text.GotFocus += (_, _) => UpdateCursor();
                text.LostFocus += (_, _) => UpdateCursor();
                UpdateCursor();
            }
            n.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            n.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            n.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            n.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
            // 입력 중이 아닐 때 우클릭 = 예시 값 목록(글상자 기본 메뉴보다 먼저 받도록 터널).
            n.AddHandler(Control.ContextRequestedEvent, NumberPresets.OnContextRequested, RoutingStrategies.Tunnel);
        });
    }

    private static TextBox? TextOf(NumericUpDown n) => States.TryGetValue(n, out var s) ? s.Text : null;

    private static PixelPoint ScreenPoint(Visual v, PointerEventArgs e) => v.PointToScreen(e.GetPosition(v));

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not NumericUpDown n || !n.IsEnabled || !States.TryGetValue(n, out var s)) return;
        if (!e.GetCurrentPoint(n).Properties.IsLeftButtonPressed) return;
        // 입력 중이면 보통 글상자처럼(글자 고르기).
        if (TextOf(n) is { IsKeyboardFocusWithin: true }) return;
        s.Pressed = true;
        s.Scrubbing = false;
        s.Start = s.Last = ScreenPoint(n, e);
        s.Pixels = 0;
        s.BaseValue = n.Value ?? n.Minimum;
        s.Mods = e.KeyModifiers;
        e.Pointer.Capture(n);
        e.Handled = true;
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not NumericUpDown n || !States.TryGetValue(n, out var s) || !s.Pressed) return;
        e.Handled = true;
        var p = ScreenPoint(n, e);
        if (!s.Scrubbing)
        {
            if (Math.Abs(p.X - s.Start.X) < DragThreshold * ScalingOf(n)) return;
            s.Scrubbing = true;
            Started?.Invoke(null, n);
        }
        // 보조 키가 바뀌면 지금 값에서 새로 센다(값이 튀지 않게).
        if (e.KeyModifiers != s.Mods)
        {
            s.Mods = e.KeyModifiers;
            s.BaseValue = n.Value ?? n.Minimum;
            s.Pixels = 0;
        }
        s.Pixels += (p.X - s.Last.X) / ScalingOf(n);
        s.Last = p;
        Apply(n, s);
        WrapAtScreenEdge(n, s, p);
    }

    private static void Apply(NumericUpDown n, State s)
    {
        double perStep = PixelsPerStep * ((s.Mods & KeyModifiers.Shift) != 0 ? 10 : 1);
        decimal steps = (decimal)Math.Truncate(s.Pixels / perStep);
        decimal increment = n.Increment * (Ui.HasCommand(s.Mods) ? 10 : 1);
        var value = Math.Clamp(s.BaseValue + steps * increment, n.Minimum, n.Maximum);
        if (n.Value != value) n.Value = value;
    }

    /// <summary>커서가 화면 왼쪽/오른쪽 끝에 닿으면 반대편 끝으로 옮긴다(옮긴 자리를 새 기준으로).</summary>
    private static void WrapAtScreenEdge(NumericUpDown n, State s, PixelPoint p)
    {
        if (TopLevel.GetTopLevel(n)?.Screens?.ScreenFromPoint(p) is not { } screen) return;
        var b = screen.Bounds;
        int margin = Math.Max(2, (int)Math.Ceiling(2 * screen.Scaling));
        int? x = p.X <= b.X + margin ? b.Right - margin * 3
            : p.X >= b.Right - margin ? b.X + margin * 3
            : null;
        if (x is not { } target) return;
        var to = new PixelPoint(target, p.Y);
        if (Platform.WarpCursor(to, screen.Scaling)) s.Last = to;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not NumericUpDown n || !States.TryGetValue(n, out var s) || !s.Pressed) return;
        e.Handled = true;
        bool scrubbed = s.Scrubbing;
        s.Pressed = s.Scrubbing = false;
        e.Pointer.Capture(null);
        if (scrubbed)
        {
            Ended?.Invoke(null, n);
            return;
        }
        // 끌지 않은 클릭: 글자 입력.
        if (TextOf(n) is { } text)
        {
            text.Focus();
            text.SelectAll();
        }
    }

    private static void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is not NumericUpDown n || !States.TryGetValue(n, out var s) || !s.Pressed) return;
        bool scrubbed = s.Scrubbing;
        s.Pressed = s.Scrubbing = false;
        if (scrubbed) Ended?.Invoke(null, n);
    }

    private static double ScalingOf(Visual v) => TopLevel.GetTopLevel(v)?.RenderScaling ?? 1;
}
