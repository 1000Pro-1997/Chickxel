using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Chickxel.App;

/// <summary>
/// 모든 숫자 상자(<see cref="NumericUpDown"/>) 공통: 오른쪽 끝에 위/아래 단추(<see cref="SpinButtons"/>)를 둔다.
/// <list type="bullet">
/// <item>단추를 누르면 한 칸(<see cref="NumericUpDown.Increment"/>) 올리거나 내린다. Ctrl(⌘) = 10칸.</item>
/// <item>단추 위에서 위아래로 끌면 값이 바뀐다(위 = 증가, 3px마다 한 칸, Shift = 10배 느리게, Ctrl(⌘) = 한 번에 10칸).
/// 끌다가 화면 위/아래 끝에 닿으면 커서가 반대편 끝으로 넘어가 계속 끌 수 있다.</item>
/// <item>숫자 칸을 누르면 글자 입력(전체 선택). 입력 중(포커스가 있는) 상자는 보통 글상자처럼 마우스로 글자를 고르고, 휠 한 칸에 한 칸씩 바뀐다.</item>
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
        /// <summary>누른 단추: +1 위, -1 아래, 0 숫자 칸.</summary>
        public int Spin;
        public PixelPoint Start, Last;
        public double Pixels;
        public decimal BaseValue;
        /// <summary>누른 순간의 값(끌기·단추가 바꾸기 전 — 최근 값으로 기억).</summary>
        public decimal? PressValue;
        public KeyModifiers Mods;
        public TextBox? Text;
        public SpinButtons? Buttons;
    }

    private static readonly Cursor SpinCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NumericUpDown, State> States = new();

    /// <summary>앱 시작 때 한 번: 모든 숫자 상자에 붙인다.</summary>
    public static void Install()
    {
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<NumericUpDown>((n, e) =>
        {
            // 테마의 단추 대신 오른쪽 끝 위/아래 단추(SpinButtons)를 쓴다.
            n.ShowButtonSpinner = false;
            // Install on the control, not Ui.Num: independently-created numeric fields use the same input path.
            var expression = n.TextConverter as NumberExpression ?? new NumberExpression(n);
            n.TextConverter = expression;
            var text = e.NameScope.Find<TextBox>("PART_TextBox");
            bool alreadyInstalled = States.TryGetValue(n, out var state);
            state ??= new State();
            if (state.Buttons is null)
            {
                state.Buttons = new SpinButtons();
                n.InnerRightContent = state.Buttons;
            }
            if (alreadyInstalled && ReferenceEquals(state.Text, text)) return;
            state.Text = text;
            if (!alreadyInstalled) States.Add(n, state);
            if (text is not null)
            {
                decimal? editingValue = n.Value;
                void CommitExpression(bool enter)
                {
                    if (!expression.IsArithmeticInput(text.Text)) return;
                    if (!expression.IsExpression(text.Text))
                    {
                        n.Value = editingValue;
                    }
                    else if (!enter) return; // NumericUpDown formats the result itself on focus loss.
                    text.Text = expression.ConvertBack(n.Value, typeof(string), null,
                        System.Globalization.CultureInfo.CurrentCulture)?.ToString();
                    if (enter) editingValue = n.Value;
                }
                decimal? focusValue = n.Value;
                text.GotFocus += (_, _) => editingValue = focusValue = n.Value;
                text.LostFocus += (_, _) =>
                {
                    CommitExpression(false);
                    NumberPresets.Remember(n, focusValue);
                };
                text.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) CommitExpression(true);
                };
            }
            if (alreadyInstalled) return;
            n.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            n.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            n.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            n.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Direct, handledEventsToo: true);
            n.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
            // 입력 중이 아닐 때 우클릭 = 예시 값 목록(글상자 기본 메뉴보다 먼저 받도록 터널).
            n.AddHandler(Control.ContextRequestedEvent, NumberPresets.OnContextRequested, RoutingStrategies.Tunnel);
        });
    }

    private static TextBox? TextOf(NumericUpDown n) => States.TryGetValue(n, out var s) ? s.Text : null;

    private static PixelPoint ScreenPoint(Visual v, PointerEventArgs e) => v.PointToScreen(e.GetPosition(v));

    /// <summary>한 번에 움직일 칸(Ctrl(⌘) = 10칸).</summary>
    private static decimal IncrementOf(NumericUpDown n, KeyModifiers mods) => n.Increment * (Ui.HasCommand(mods) ? 10 : 1);

    /// <summary>지금 값에서 <paramref name="steps"/>칸 옮긴다(범위 안으로).</summary>
    private static void Step(NumericUpDown n, int steps, KeyModifiers mods)
    {
        var value = Math.Clamp((n.Value ?? n.Minimum) + steps * IncrementOf(n, mods), n.Minimum, n.Maximum);
        if (n.Value != value) n.Value = value;
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not NumericUpDown n || !n.IsEnabled || !States.TryGetValue(n, out var s)) return;
        if (!e.GetCurrentPoint(n).Properties.IsLeftButtonPressed) return;
        var buttons = (e.Source as Visual)?.FindAncestorOfType<SpinButtons>(includeSelf: true);
        // 입력 중이면 숫자 칸은 보통 글상자처럼(글자 고르기). 단추는 입력 중에도 쓴다.
        if (buttons is null && TextOf(n) is { IsKeyboardFocusWithin: true }) return;
        s.Pressed = true;
        s.Scrubbing = false;
        s.Spin = buttons?.HalfAt(e.GetPosition(buttons)) ?? 0;
        if (buttons is not null) buttons.Pressed = s.Spin;
        s.Start = s.Last = ScreenPoint(n, e);
        s.Pixels = 0;
        s.BaseValue = n.Value ?? n.Minimum;
        s.PressValue = n.Value;
        s.Mods = e.KeyModifiers;
        e.Pointer.Capture(n);
        e.Handled = true;
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not NumericUpDown n || !States.TryGetValue(n, out var s) || !s.Pressed) return;
        e.Handled = true;
        // 숫자 칸을 누른 채 움직여도 끌기가 아니다(떼면 글자 입력).
        if (s.Spin == 0) return;
        var p = ScreenPoint(n, e);
        if (!s.Scrubbing)
        {
            if (Math.Abs(p.Y - s.Start.Y) < DragThreshold * ScalingOf(n)) return;
            s.Scrubbing = true;
            if (s.Buttons is { } b) b.Pressed = 0;
            Started?.Invoke(null, n);
        }
        // 보조 키가 바뀌면 지금 값에서 새로 센다(값이 튀지 않게).
        if (e.KeyModifiers != s.Mods)
        {
            s.Mods = e.KeyModifiers;
            s.BaseValue = n.Value ?? n.Minimum;
            s.Pixels = 0;
        }
        // 위로 끌면 증가.
        s.Pixels -= (p.Y - s.Last.Y) / ScalingOf(n);
        s.Last = p;
        Apply(n, s);
        WrapAtScreenEdge(n, s, p);
    }

    private static void Apply(NumericUpDown n, State s)
    {
        double perStep = PixelsPerStep * ((s.Mods & KeyModifiers.Shift) != 0 ? 10 : 1);
        decimal steps = (decimal)Math.Truncate(s.Pixels / perStep);
        var value = Math.Clamp(s.BaseValue + steps * IncrementOf(n, s.Mods), n.Minimum, n.Maximum);
        if (n.Value != value) n.Value = value;
    }

    /// <summary>커서가 화면 위/아래 끝에 닿으면 반대편 끝으로 옮긴다(옮긴 자리를 새 기준으로).</summary>
    private static void WrapAtScreenEdge(NumericUpDown n, State s, PixelPoint p)
    {
        if (TopLevel.GetTopLevel(n)?.Screens?.ScreenFromPoint(p) is not { } screen) return;
        var b = screen.Bounds;
        int margin = Math.Max(2, (int)Math.Ceiling(2 * screen.Scaling));
        int? y = p.Y <= b.Y + margin ? b.Bottom - margin * 3
            : p.Y >= b.Bottom - margin ? b.Y + margin * 3
            : null;
        if (y is not { } target) return;
        var to = new PixelPoint(p.X, target);
        if (Platform.WarpCursor(to, screen.Scaling)) s.Last = to;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not NumericUpDown n || !States.TryGetValue(n, out var s) || !s.Pressed) return;
        e.Handled = true;
        bool scrubbed = s.Scrubbing;
        int spin = s.Spin;
        Reset(s);
        e.Pointer.Capture(null);
        if (scrubbed)
        {
            Ended?.Invoke(null, n);
            NumberPresets.Remember(n, s.PressValue);
            return;
        }
        // 끌지 않고 단추를 뗌: 한 칸.
        if (spin != 0)
        {
            Step(n, spin, e.KeyModifiers);
            NumberPresets.Remember(n, s.PressValue);
            return;
        }
        // 숫자 칸 클릭: 글자 입력.
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
        Reset(s);
        if (scrubbed) { Ended?.Invoke(null, n); NumberPresets.Remember(n, s.PressValue); }
    }

    private static void Reset(State s)
    {
        s.Pressed = s.Scrubbing = false;
        s.Spin = 0;
        if (s.Buttons is { } b) b.Pressed = 0;
    }

    /// <summary>입력 중인 상자: 휠 한 칸에 한 칸(위 = 증가, Ctrl(⌘) = 10칸). 입력 중이 아니면 휠은 바깥(목록 스크롤 등)으로 넘긴다.</summary>
    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not NumericUpDown n || !n.IsEnabled || TextOf(n) is not { IsKeyboardFocusWithin: true } || e.Delta.Y == 0) return;
        e.Handled = true;
        Step(n, e.Delta.Y > 0 ? 1 : -1, e.KeyModifiers);
    }

    private static double ScalingOf(Visual v) => TopLevel.GetTopLevel(v)?.RenderScaling ?? 1;

    /// <summary>
    /// 숫자 상자 오른쪽 끝의 위/아래 단추(한 칸을 위아래 반으로 나눠 그림). 누르기·끌기는 <see cref="NumberScrub"/>가 상자 쪽에서 받고,
    /// 여기서는 마우스를 올린 반쪽과 누른 반쪽만 칠한다.
    /// </summary>
    private sealed class SpinButtons : Control
    {
        private const double ButtonWidth = 14, IconSize = 12;
        /// <summary>마우스를 올린 반쪽(+1 위, -1 아래, 0 없음).</summary>
        private int _hover;
        private int _pressed;

        public SpinButtons()
        {
            Width = ButtonWidth;
            VerticalAlignment = VerticalAlignment.Stretch;
            Cursor = SpinCursor;
        }

        /// <summary>누르고 있는 반쪽(+1 위, -1 아래, 0 없음).</summary>
        public int Pressed
        {
            get => _pressed;
            set
            {
                if (_pressed == value) return;
                _pressed = value;
                InvalidateVisual();
            }
        }

        /// <summary><paramref name="p"/>가 위 반쪽이면 +1, 아래 반쪽이면 -1.</summary>
        public int HalfAt(Avalonia.Point p) => p.Y < Bounds.Height / 2 ? 1 : -1;

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            SetHover(HalfAt(e.GetPosition(this)));
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            SetHover(0);
        }

        private void SetHover(int half)
        {
            if (_hover == half) return;
            _hover = half;
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            var size = Bounds.Size;
            // 투명 바탕: 칸 전체가 마우스를 받게.
            g.FillRectangle(Brushes.Transparent, new Rect(size));
            double half = Math.Round(size.Height / 2);
            var up = new Rect(0, 0, size.Width, half);
            var down = new Rect(0, half, size.Width, size.Height - half);
            foreach (var (rect, side) in new[] { (up, 1), (down, -1) })
            {
                if (_pressed == side) g.FillRectangle(AppTheme.AccentBrush, rect);
                else if (_hover == side && IsEffectivelyEnabled) g.FillRectangle(AppTheme.HoverBrush, rect);
            }
            var brush = IsEffectivelyEnabled ? AppTheme.TextBrush : AppTheme.DimTextBrush;
            // 아이콘 칸(24)의 꺾쇠는 가운데 6칸 높이뿐이라, 위 꺾쇠는 아래로·아래 꺾쇠는 위로 조금 당겨 두 꺾쇠를 가운데에 모은다.
            VectorIcons.Draw(g, VectorIcon.ChevronUp, up.Translate(new Vector(0, 1)), IconSize, brush, 2.6);
            VectorIcons.Draw(g, VectorIcon.ChevronDown, down.Translate(new Vector(0, -1)), IconSize, brush, 2.6);
        }
    }
}
