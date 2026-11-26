using Avalonia.Controls.Primitives;
using Avalonia.Rendering;
using Avalonia.VisualTree;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 튜토리얼 한 단계: 말풍선 글, 비출 컨트롤, 넘어갈 조건. 점검 기록(저장소 루트 <c>튜토리얼.md</c>)과 점검 샷(GuideShots <c>tutorial-check</c>)은 <see cref="Id"/>로 단계를 가리킨다.
/// </summary>
internal sealed class TutorialStep
{
    /// <summary>점검용 단계 이름(<c>pixelate.drop</c>처럼 튜토리얼.단계).</summary>
    public required string Id { get; init; }

    /// <summary>말풍선 글(단계에 있는 동안 계속 다시 읽음 — 도는 중·끝남처럼 바뀌는 글).</summary>
    public required Func<string> Text { get; init; }

    /// <summary>
    /// 비출 컨트롤(계속 다시 찾음 — 아직 없거나 보이지 않으면 암전 없이 <see cref="TutorialSpotlight"/>을 시작한 창 가운데에 말풍선만).
    /// null이면 대상 없이 가운데 말풍선.
    /// </summary>
    public Func<Control?>? Target { get; init; }

    /// <summary>
    /// 비출 곳을 컨트롤 안 사각형으로(그 컨트롤과 그 안 사각형, 계속 다시 찾음 — 타임라인의 칸 몇 개처럼 컨트롤 일부). 있으면 <see cref="Target"/> 대신 쓴다.
    /// </summary>
    public Func<(Control Owner, Rect Area)?>? TargetArea { get; init; }

    /// <summary>비춘 칸을 왼쪽 끝 칸에서 오른쪽 끝 칸으로 끄는 모습(유령 커서)을 되풀이해 보인다(칸 여러 개를 끌어 고르는 단계).</summary>
    public bool DragAcross { get; init; }

    /// <summary>
    /// 끌어 놓을 자리(그 자리를 가진 컨트롤과 그 안 사각형, 계속 다시 찾음): 대상과 같은 창에 있으면 함께 밝게 비추고 빛을 두르며,
    /// 대상에서 이 자리로 끄는 모습(유령 상자 + 커서)을 되풀이해 보인다. 누르기·끌어 놓기도 통과.
    /// </summary>
    public Func<(Control Owner, Rect Area)?>? DropTarget { get; init; }

    /// <summary>
    /// 해 볼 일을 했는지(계속 다시 물음 — 처음 온 단계에서만). 해 보면 '다음'이 켜지고(꼭 해야 하는 단계), '다음'을 눌러야 넘어간다 — 사용자가 마음껏 해 본 뒤 넘어가게.
    /// <see cref="AutoAdvance"/>면 하는 대로 저절로 넘어간다. null이면 '다음' 단추로만 넘어간다.
    /// </summary>
    public Func<bool>? Done { get; init; }

    /// <summary>
    /// 꼭 해야 하는 단계: 처음 왔을 때는 <see cref="Done"/>이 참이 될 때까지 '다음'이 꺼져 있다(이전·다음으로 다시 왔을 때는 켜져 있음).
    /// 꺼져 있으면 해 보지 않고 '다음'으로 넘어갈 수 있다.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// <see cref="Done"/>이 참이 되면 '다음'을 기다리지 않고 저절로 넘어간다: 창을 열거나 닫아 말풍선이 있을 창이 바뀌는 단계(창 열기, 적용·가져오기로 창이 닫힘),
    /// 누르면 앱 밖으로 나가는 마지막 단계(상점 열기). 조건이 거짓 → 참으로 바뀔 때만이고, 꼭 해야 하면 그때까지 '다음'이 없다.
    /// </summary>
    public bool AutoAdvance { get; init; }

    /// <summary>이 단계에 처음 들어올 때 한 번(예시 파일 열기, 창 띄우기, 비교할 처음 값 기억 — 이전·다음으로 다시 올 때는 부르지 않음).</summary>
    public Action? Enter { get; init; }

    /// <summary>
    /// 어두운 곳도 누르기·끌어 놓기를 그대로 통과시킨다(비춘 칸 밖으로 끌어 놓아야 하는 단계 — 효과 상자를 타임라인으로 끌기).
    /// 꺼져 있으면 비춘 칸 밖은 누를 수 없다.
    /// </summary>
    public bool PassThrough { get; init; }

    /// <summary>
    /// 말풍선 위쪽에 되풀이해 보일 작은 움짤(그 단계에서 할 일만 가까이 잘라 찍은 것): 앱 내장 <c>Assets/Tutorial/clip-&lt;이름&gt;.webp</c>.
    /// GuideShots <c>clip-tutorial-&lt;이름&gt;</c>이 만든다. 둘이면 좌우로 나란히(움짤마다 아래에 설명). 비었으면 글만.
    /// </summary>
    public IReadOnlyList<TutorialClip> Clips { get; init; } = [];

    /// <summary>움짤을 크게(말풍선 폭 640): 화면에서 직접 해 보지 않는 일(갤러리에 올리기 등)을 움짤로 대신 보일 때.</summary>
    public bool BigClip { get; init; }

    /// <summary>말풍선 폭을 이 값으로(움짤이 여럿이어도 — 비춘 칸 옆 좁은 자리에 말풍선이 들어가야 할 때, 움짤은 줄여 맞춤).</summary>
    public double? BubbleWidth { get; init; }

    /// <summary>
    /// 열린 메뉴 안에서 눌러야 할 항목(계속 다시 찾음): 그 메뉴가 열려 있으면 항목을 노란 빛으로 감싸고, 같은 메뉴의 다른 항목은 누를 수 없게 한다
    /// (누르면 말풍선이 번쩍임).
    /// </summary>
    public Func<MenuItem?>? MenuItem { get; init; }

    /// <summary>
    /// 말풍선이 가리면 안 되는 곳(컨트롤과 그 안 사각형, 계속 다시 찾음): 끌어서 하는 일이 지나갈 자리(프레임 + 를 오른쪽으로 끌어 여러 개 더하기 등).
    /// 밝게 비추지는 않고 말풍선 자리만 피한다.
    /// </summary>
    public Func<(Control Owner, Rect Area)?>? KeepClear { get; init; }

    /// <summary>참이면 말풍선을 잠깐 숨긴다(계속 다시 물음 — 스포이드로 원본을 누르는 동안처럼 말풍선이 누를 곳을 가릴 때). 비춘 칸·덮개는 그대로.</summary>
    public Func<bool>? HideBubble { get; init; }

    /// <summary>
    /// 이 단계 동안 잠깐 숨길 앱 창(비출 곳·말풍선을 가리는 떠 있는 창 — 메인 창의 타임라인을 비출 때의 AI 채팅 창 등).
    /// 떠 있던 것만 숨기고, 단계를 떠나거나 튜토리얼이 끝나면 다시 보인다.
    /// </summary>
    public Func<IEnumerable<Window?>>? HideWindows { get; init; }
}

/// <summary>말풍선 움짤 하나: 앱 내장 <c>clip-&lt;<paramref name="Name"/>&gt;.webp</c>와 그 아래 설명(<see cref="TutorialText"/> 입력 표시를 씀, 없으면 움짤만).</summary>
internal sealed record TutorialClip(string Name, Func<string>? Caption = null);

/// <summary>
/// 화면 위 따라 하기 튜토리얼: 대상 컨트롤이 있는 창의 덮개 층(<see cref="OverlayLayer"/>)에 그 칸만 남기고 어둡게 덮은 판과 말풍선(설명 · 이전 · 다음, 머리 줄 끝 닫기)을 띄운다.
/// 한 번에 하나만 돈다. 단계마다 대상·넘어갈 조건을 짧은 간격으로 다시 확인해, 대상 창이 바뀌면(메인 창 → 픽셀로 정리 창 → AI 채팅 창) 덮개도 따라 옮긴다.
/// 꼭 해야 하는 일을 하면 '다음'이 켜지고 '다음'을 눌러 넘어간다(창을 여닫는 단계만 저절로). 비춘 칸은 누르기·끌어 놓기가 그대로 아래로 가고,
/// 어두운 곳은 막는다(<see cref="TutorialStep.PassThrough"/>면 막지 않음).
/// </summary>
public static class TutorialSpotlight
{
    private static Session? _session;

    /// <summary>튜토리얼이 도는 중인지.</summary>
    public static bool IsRunning => _session is not null;

    /// <summary>지금 단계 id(점검 샷이 진행을 확인), 돌지 않으면 null.</summary>
    public static string? CurrentStepId => _session?.Step.Id;

    /// <summary>
    /// <paramref name="steps"/>를 처음부터 시작한다(돌던 튜토리얼은 그만둔다). <paramref name="home"/>은 대상이 없을 때 말풍선을 띄울 창(메인 창)이고, 닫히면 튜토리얼도 끝난다.
    /// 마지막 단계에서 '끝내기'를 누르면 <paramref name="completed"/>, 도중에 그만두면(말풍선 닫기, 그 단계의 대상이 있던 창 닫기) <paramref name="aborted"/>
    /// (메인 창이 닫히거나 다른 튜토리얼을 시작해 끝난 때는 어느 것도 부르지 않음).
    /// </summary>
    internal static void Start(Window home, string title, IReadOnlyList<TutorialStep> steps, Action? completed = null, Action? aborted = null)
    {
        Stop();
        if (steps.Count == 0) return;
        _session = new Session(home, title, steps, completed, aborted);
        _session.Begin();
    }

    /// <summary>돌던 튜토리얼을 그만둔다(덮개를 걷음).</summary>
    public static void Stop()
    {
        var s = _session;
        _session = null;
        s?.Dispose();
    }

    /// <summary>
    /// 열린 우클릭 메뉴에서 머리글 차례 <paramref name="path"/>(맨 위 항목 → 하위 메뉴 항목)를 따라 찾은 항목: 하위 메뉴가 펼쳐져 있으면 그 안 항목, 아니면 펼칠 항목
    /// (<see cref="TutorialStep.MenuItem"/> — 눌러야 할 곳에 노란 테두리). 머리글에 <c>{</c>가 있으면(형식 글 — "삭제 ({0})") 그 앞까지로 시작을 맞추고, 아니면 글이 같아야 한다.
    /// 메뉴가 닫혀 있으면 null.
    /// </summary>
    internal static MenuItem? OpenMenuItem(params string[] path)
    {
        static string HeaderText(MenuItem m) => (m.Header as string ?? (m.Header as TextBlock)?.Text ?? "").Replace("_", "");
        static bool Matches(MenuItem m, string want)
        {
            var text = HeaderText(m);
            int brace = want.IndexOf('{');
            return brace >= 0 ? text.StartsWith(want[..brace].TrimEnd(), StringComparison.Ordinal) : text == want.Replace("_", "");
        }
        foreach (var menu in PopupMenus.OpenContextMenus)
        {
            IEnumerable<MenuItem> items = menu.Items.OfType<MenuItem>();
            MenuItem? found = null;
            foreach (var want in path)
            {
                var next = items.FirstOrDefault(m => m.IsVisible && Matches(m, want));
                if (next is null) break;
                found = next;
                if (!next.IsSubMenuOpen) break;
                items = next.Items.OfType<MenuItem>();
            }
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>지금 단계의 대상·조건을 바로 다시 확인한다(점검 샷이 기다리지 않고 진행을 보게).</summary>
    public static void Tick() => _session?.Update();

    private sealed class Session : IDisposable
    {
        private const double HolePad = 6;

        private readonly Window _home;
        private readonly Action? _completed, _aborted;
        private readonly IReadOnlyList<TutorialStep> _steps;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private readonly Canvas _root = new();
        private readonly Shade _shade = new();
        private readonly Border _bubble;
        private readonly TextBlock _counter, _text;
        private readonly Button _prev, _next;
        /// <summary>꼭 해야 하는 단계에서 '다음' 옆(저절로 넘어가는 단계는 그 자리)에 보이는 "직접 해 보세요".</summary>
        private readonly StackPanel _hint;
        /// <summary>말풍선 위쪽 움짤 자리(<see cref="TutorialStep.Clips"/>가 없으면 숨김): 움짤마다 한 칸(좌우로 나란히) — 움짤 + 아래 설명.</summary>
        private readonly Grid _clips = new() { Margin = new Thickness(0, 0, 0, 8), IsVisible = false };
        /// <summary>움짤 설명 글과 그 글을 읽는 함수(단계에 있는 동안 다시 읽음).</summary>
        private readonly List<(TextBlock Block, Func<string> Text)> _captions = [];
        private OverlayLayer? _layer;
        private int _index;
        /// <summary>들어간 적 있는 단계(<see cref="TutorialStep.Enter"/>는 처음 한 번, 저절로 넘어가기도 처음 온 때만).</summary>
        private readonly HashSet<int> _visited = [];
        /// <summary>지금 단계에 처음 왔는지.</summary>
        private bool _firstVisit;
        /// <summary>
        /// 지금 단계의 조건이 이 단계에서 거짓인 것을 본 뒤인지: 저절로 넘어가기(<see cref="TutorialStep.AutoAdvance"/>)는 조건이 거짓 → 참으로 바뀔 때만이다
        /// (지난번에 해 둔 것 — 펼쳐 둔 설정 칸·끝난 준비 — 때문에 들어오자마자 넘어가 1 → 4처럼 건너뛰지 않게. 이미 참이면 '다음'으로).
        /// </summary>
        private bool _armed;
        /// <summary>지금 단계에서 해 볼 일을 한 번이라도 했는지(그 뒤로는 되돌려도 '다음'이 켜진 채).</summary>
        private bool _doneSeen;
        /// <summary>이 단계의 대상이 있던 메인 창 아닌 창(닫히면 튜토리얼 끝).</summary>
        private Window? _stepWindow;
        /// <summary>말풍선이 없는 앱 창을 통째로 덮은 어두운 판.</summary>
        private readonly Dictionary<Window, (OverlayLayer Layer, Shade Shade)> _dims = [];
        /// <summary>지금 단계가 숨긴 창(<see cref="TutorialStep.HideWindows"/> — 단계를 떠나면 다시 보임).</summary>
        private readonly List<Window> _hidden = [];

        public Session(Window home, string title, IReadOnlyList<TutorialStep> steps, Action? completed, Action? aborted)
        {
            _home = home;
            _completed = completed;
            _aborted = aborted;
            _steps = steps;
            _counter = new TextBlock { Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, VerticalAlignment = VerticalAlignment.Center };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var titleText = new TextBlock { Text = title, Foreground = HelpWindow.Yellow, FontWeight = FontWeight.Bold, FontSize = AppTheme.SmallFontSize, VerticalAlignment = VerticalAlignment.Center };
            var close = Ui.IconButton(VectorIcon.X, UiStrings.Tutorial_Stop, (_, _) => Abort(), size: 11);
            close.Background = Brushes.Transparent;
            close.BorderThickness = new Thickness(0);
            close.Padding = new Thickness(3);
            close.Margin = new Thickness(6, -4, -6, 0);
            DockPanel.SetDock(close, Dock.Right);
            DockPanel.SetDock(_counter, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(_counter);
            head.Children.Add(titleText);
            _text = new TextBlock { Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
            _prev = Ui.Button(UiStrings.Tutorial_Prev, (_, _) => Back());
            _next = Ui.Button(UiStrings.Tutorial_Next, (_, _) => Advance());
            _next.Background = AppTheme.AccentBrush;
            _next.Foreground = Brushes.White;
            Ui.KeepFillOnHover(_next);
            // 말풍선 단추는 초점을 가져가지 않는다: 캔버스 크기 모드의 Enter·단축키가 캔버스(메인 창)로 그대로 가게(초점 받은 '다음'이 Enter를 먹지 않게).
            close.Focusable = _prev.Focusable = _next.Focusable = false;
            var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            DockPanel.SetDock(_prev, Dock.Left);
            DockPanel.SetDock(_next, Dock.Right);
            // 꼭 해야 하는 단계: 직접 해 보라는 표시('다음' 옆, 저절로 넘어가는 단계는 '다음' 자리에).
            var hintText = new TextBlock { Text = UiStrings.Tutorial_DoIt, Foreground = HelpWindow.Yellow, FontSize = AppTheme.SmallFontSize, VerticalAlignment = VerticalAlignment.Center };
            _hint = Ui.Row(new IconView(VectorIcon.MousePointerClick, HelpWindow.Yellow) { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center }, hintText);
            _hint.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(_hint, Dock.Right);
            buttons.Children.Add(_prev);
            buttons.Children.Add(_next);
            buttons.Children.Add(_hint);
            _hint.Margin = new Thickness(0, 0, 10, 0);
            _bubble = new Border
            {
                Background = AppTheme.PanelBrush,
                BorderBrush = HelpWindow.Yellow,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12),
                Width = 340,
                Child = new StackPanel { Children = { head, _clips, _text, buttons } },
                BoxShadow = BoxShadows.Parse("0 6 24 0 #99000000"),
            };
            _shade.Blocked += FlashBubble;
            _root.Children.Add(_shade);
            _root.Children.Add(_bubble);
            _timer.Tick += (_, _) => Update();
            _home.Closed += OnHomeClosed;
        }

        public TutorialStep Step => _steps[_index];

        public void Begin()
        {
            EnterStep();
            _timer.Start();
        }

        private void OnHomeClosed(object? sender, EventArgs e) => TutorialSpotlight.Stop();

        public void Dispose()
        {
            _timer.Stop();
            _home.Closed -= OnHomeClosed;
            DetachMenu();
            Detach();
            foreach (var (layer, shade) in _dims.Values) layer.Children.Remove(shade);
            _dims.Clear();
            ShowHidden();
        }

        /// <summary>앞 단계가 숨긴 창을 다시 보인다(닫힌 창은 빼고).</summary>
        private void ShowHidden()
        {
            foreach (var w in _hidden)
            {
                if (w.PlatformImpl is null) continue;
                // 숨기기 전처럼 주인 창 위에 뜨게(주인 없이 띄우면 메인 창 뒤로 갈 수 있음).
                if (w.Owner is Window owner) w.Show(owner);
                else w.Show();
            }
            _hidden.Clear();
        }

        private void EnterStep()
        {
            var step = Step;
            _stepWindow = null;
            _next.Content = _index == _steps.Count - 1 ? UiStrings.Tutorial_Finish : UiStrings.Tutorial_Next;
            _prev.IsEnabled = _index > 0;
            _firstVisit = _visited.Add(_index);
            _armed = false;
            _doneSeen = false;
            _counter.Text = UiStrings.FormatTutorial_StepCount(index: _index + 1, count: _steps.Count);
            if (_firstVisit) step.Enter?.Invoke();
            ShowHidden();
            foreach (var w in step.HideWindows?.Invoke() ?? [])
            {
                if (w is not { IsVisible: true }) continue;
                w.Hide();
                _hidden.Add(w);
            }
            ShowClips(step.Clips, step.BigClip, step.BubbleWidth);
            DetachMenu();
            Update();
        }

        private void Advance()
        {
            if (!ReferenceEquals(_session, this)) return;
            if (_index >= _steps.Count - 1)
            {
                TutorialSpotlight.Stop();
                _completed?.Invoke();
                return;
            }
            _index++;
            EnterStep();
        }

        private void Back()
        {
            if (!ReferenceEquals(_session, this) || _index == 0) return;
            _index--;
            EnterStep();
        }

        /// <summary>도중에 그만둠(말풍선 닫기, 대상 창 닫기): 덮개를 걷고 <see cref="_aborted"/>.</summary>
        private void Abort()
        {
            if (!ReferenceEquals(_session, this)) return;
            TutorialSpotlight.Stop();
            _aborted?.Invoke();
        }

        /// <summary>
        /// 조건을 확인하고(맞으면 다음 단계), 대상 창에 덮개를 옮겨 비출 칸·말풍선 자리를 맞추고, 앱의 다른 창은 통째로 어둡게 덮는다.
        /// 이 단계의 대상이 있던 창(픽셀로 정리 창 등)을 닫으면 튜토리얼을 끝낸다 — 마지막 단계면 마친 것으로(적용 단추로 닫음).
        /// </summary>
        public void Update()
        {
            if (!ReferenceEquals(_session, this)) return;
            var step = Step;
            bool locked = false;
            if (_firstVisit && step.Done is { } done)
            {
                bool now = done();
                if (step.AutoAdvance)
                {
                    if (now && _armed)
                    {
                        Advance();
                        return;
                    }
                    if (!now) _armed = true;
                    locked = step.Required && !now;
                }
                else
                {
                    if (now) _doneSeen = true;
                    locked = step.Required && !_doneSeen;
                }
            }
            // 저절로 넘어가는 단계는 하기 전까지 '다음'을 숨기고 그 자리에 표시, 나머지는 '다음'을 꺼 두고 옆에 표시(해 보면 켜짐).
            _next.IsVisible = !(locked && step.AutoAdvance);
            _next.IsEnabled = !locked;
            _next.Opacity = locked ? 0.4 : 1;
            _hint.IsVisible = locked;
            if (_stepWindow is { IsVisible: false })
            {
                if (_index == _steps.Count - 1) Advance();
                else Abort();
                return;
            }
            // 비출 곳: 컨트롤 안 사각형(TargetArea — 타임라인 칸 몇 개 등)이 있으면 그것, 아니면 컨트롤 전체.
            Control? target;
            Rect area;
            if (step.TargetArea?.Invoke() is { } ta)
            {
                target = ta.Owner;
                area = ta.Area;
            }
            else
            {
                target = step.Target?.Invoke();
                area = target is null ? default : new Rect(target.Bounds.Size);
            }
            var top = target is not null && target.IsEffectivelyVisible && TopLevel.GetTopLevel(target) is { IsVisible: true } t ? t : null;
            if (top is null) target = null;
            else if (top is Window window && !ReferenceEquals(window, _home)) _stepWindow = window;
            var host = top ?? _home;
            Attach(host);
            DimOthers(host);
            if (_layer is null) return;
            var size = _layer.Bounds.Size;
            _root.Width = size.Width;
            _root.Height = size.Height;
            _shade.Width = size.Width;
            _shade.Height = size.Height;
            Rect? hole = target is not null && target.TranslatePoint(area.Position, _root) is { } at
                ? new Rect(at, area.Size).Inflate(HolePad).Intersect(new Rect(size))
                : null;
            Rect? drop = target is not null && step.DropTarget?.Invoke() is { } d && d.Owner.IsEffectivelyVisible
                         && ReferenceEquals(TopLevel.GetTopLevel(d.Owner), host) && d.Owner.TranslatePoint(d.Area.Position, _root) is { } dropAt
                ? new Rect(dropAt, d.Area.Size).Inflate(HolePad).Intersect(new Rect(size))
                : null;
            Rect? clear = target is not null && step.KeepClear?.Invoke() is { } kc && kc.Owner.IsEffectivelyVisible
                          && ReferenceEquals(TopLevel.GetTopLevel(kc.Owner), host) && kc.Owner.TranslatePoint(kc.Area.Position, _root) is { } clearAt
                ? new Rect(clearAt, kc.Area.Size).Inflate(HolePad).Intersect(new Rect(size))
                : null;
            _shade.Set(hole, step.PassThrough, target is not null, pulse: _hint.IsVisible, drop: drop, dragAcross: step.DragAcross);
            TutorialText.Fill(_text, step.Text());
            foreach (var (block, text) in _captions) TutorialText.Fill(block, text());
            UpdateMenu(step);
            bool hidden = step.HideBubble?.Invoke() == true;
            _bubble.IsVisible = !hidden;
            if (hidden) _shade.SetTail(Side.None, default, default);
            else Place(hole, drop, size, clear);
        }

        /// <summary>
        /// 말풍선이 없는 앱 창(메인 창과 그 위에 뜬 창 중 <paramref name="host"/>가 아닌 것)을 통째로 어둡게 덮는다(누르기·휠은 막고, 누르면 말풍선이 번쩍임).
        /// 닫히거나 숨은 창과 이제 말풍선이 뜬 창에서는 걷는다.
        /// </summary>
        private void DimOthers(TopLevel host)
        {
            var windows = new List<Window> { _home };
            windows.AddRange(_home.OwnedWindows);
            // 말풍선이 없는 모달 창(AI 상태 점검 등)은 막지 않는다 — 그 창을 닫기 전에는 다른 창을 누를 수 없어 앱이 멈춘 것처럼 된다.
            var keep = windows.Where(w => w.IsVisible && !ReferenceEquals(w, host) && w is not DialogBase { IsModal: true }).ToHashSet();
            foreach (var gone in _dims.Keys.Where(w => !keep.Contains(w)).ToList())
            {
                var (layer, shade) = _dims[gone];
                layer.Children.Remove(shade);
                _dims.Remove(gone);
            }
            foreach (var w in keep)
            {
                if (!_dims.TryGetValue(w, out var dim))
                {
                    if (OverlayLayer.GetOverlayLayer(w) is not { } layer) continue;
                    dim = (layer, new Shade());
                    dim.Shade.Blocked += FlashBubble;
                    layer.Children.Add(dim.Shade);
                    _dims[w] = dim;
                }
                dim.Shade.Width = dim.Layer.Bounds.Width;
                dim.Shade.Height = dim.Layer.Bounds.Height;
                dim.Shade.Set(null, false, true);
            }
        }

        /// <summary>
        /// 말풍선 자리: 비춘 칸 오른쪽 → 왼쪽 → 아래 → 위(끌어 놓을 자리가 있으면 그 둘레도, 열린 메뉴가 있으면 그 둘레도) 중 창 안에 들어가고
        /// 두 칸과 끄는 길, 열린 메뉴(<see cref="_menuRect"/> — 따로 뜨는 창이라 말풍선을 덮음)를 가리지 않는 첫 자리
        /// (칸이 없거나 어디도 안 되면 창 가운데). 꼬리는 붙은 칸 쪽으로.
        /// </summary>
        private void Place(Rect? hole, Rect? drop, Avalonia.Size area, Rect? clear = null)
        {
            _bubble.Measure(Avalonia.Size.Infinity);
            var b = _bubble.DesiredSize;
            const double Gap = 16, Edge = 8;
            Avalonia.Point at = new((area.Width - b.Width) / 2, (area.Height - b.Height) / 2);
            Side side = Side.None;
            Rect anchor = default;
            if (hole is { } h)
            {
                double Clamp(double v, double size, double max) => Math.Clamp(v, Edge, Math.Max(Edge, max - size - Edge));
                IEnumerable<(Side Side, Avalonia.Point At, Rect Anchor)> Around(Rect r) =>
                [
                    (Side.Right, new(r.Right + Gap, Clamp(r.Center.Y - b.Height / 2, b.Height, area.Height)), r),
                    (Side.Left, new(r.Left - Gap - b.Width, Clamp(r.Center.Y - b.Height / 2, b.Height, area.Height)), r),
                    (Side.Below, new(Clamp(r.Center.X - b.Width / 2, b.Width, area.Width), r.Bottom + Gap), r),
                    (Side.Above, new(Clamp(r.Center.X - b.Width / 2, b.Width, area.Width), r.Top - Gap - b.Height), r),
                ];
                // 가리면 안 되는 곳: 두 칸과(끌어 놓을 자리가 있으면) 대상에서 그 자리로 가는 길, 끌어서 하는 일이 지나갈 자리(KeepClear), 열린 메뉴.
                var avoid = new List<Rect> { h };
                if (drop is { } d)
                {
                    avoid.Add(d);
                    avoid.Add(new Rect(h.Center, d.Center).Normalize().Inflate(18));
                }
                var options = drop is { } dd ? Around(h).Concat(Around(dd)) : Around(h);
                if (clear is { } c)
                {
                    avoid.Add(c);
                    options = options.Concat(Around(c.Union(h)));
                }
                if (_menuRect is { } menu)
                {
                    avoid.Add(menu);
                    options = options.Concat(Around(menu));
                }
                foreach (var (s, p, r) in options)
                {
                    var rect = new Rect(p, b);
                    if (p.X < Edge || p.Y < Edge || rect.Right > area.Width - Edge || rect.Bottom > area.Height - Edge) continue;
                    if (avoid.Any(a => a.Intersects(rect))) continue;
                    at = p;
                    side = s;
                    anchor = r;
                    break;
                }
                // 다 가리면 길은 가려도 칸만 피하는 첫 자리로.
                if (side == Side.None)
                    foreach (var (s, p, r) in options)
                    {
                        var rect = new Rect(p, b);
                        if (p.X < Edge || p.Y < Edge || rect.Right > area.Width - Edge || rect.Bottom > area.Height - Edge || rect.Intersects(h)
                            || _menuRect is { } open && rect.Intersects(open)) continue;
                        at = p;
                        side = s;
                        anchor = r;
                        break;
                    }
            }
            Canvas.SetLeft(_bubble, Math.Round(at.X));
            Canvas.SetTop(_bubble, Math.Round(at.Y));
            _shade.SetTail(side, new Rect(at, b), anchor);
        }

        /// <summary>
        /// 단계 움짤을 말풍선 위쪽에(없으면 숨김). 움짤이 있으면 말풍선을 넓힌다: 하나면 400(<paramref name="big"/>면 640), 둘 이상이면 좌우로 나란히 840.
        /// <paramref name="width"/>가 있으면 그 폭(움짤은 그 안에 줄여 맞춤).
        /// </summary>
        private void ShowClips(IReadOnlyList<TutorialClip> clips, bool big, double? width)
        {
            _clips.Children.Clear();
            _clips.ColumnDefinitions.Clear();
            _captions.Clear();
            foreach (var clip in clips)
            {
                var image = AnimatedImage.TryCreate(() => typeof(TutorialSpotlight).Assembly.GetManifestResourceStream($"Chickxel.Tutorial.clip-{clip.Name}.webp"));
                if (image is null) continue;
                var cell = new StackPanel { Spacing = 6, Margin = new Thickness(_clips.ColumnDefinitions.Count == 0 ? 0 : 10, 0, 0, 0) };
                cell.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, Child = image });
                if (clip.Caption is { } caption)
                {
                    var block = new TextBlock { Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
                    _captions.Add((block, caption));
                    cell.Children.Add(block);
                }
                Grid.SetColumn(cell, _clips.ColumnDefinitions.Count);
                _clips.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                _clips.Children.Add(cell);
            }
            int count = _clips.ColumnDefinitions.Count;
            _clips.IsVisible = count > 0;
            _bubble.Width = width ?? count switch { 0 => 340, 1 => big ? 640 : 400, _ => 840 };
        }

        /// <summary>막힌 어두운 곳을 누르면 말풍선 테두리를 잠깐 희게(여기를 보라는 표시).</summary>
        private void FlashBubble()
        {
            _bubble.BorderBrush = Brushes.White;
            DispatcherTimer.RunOnce(() => _bubble.BorderBrush = HelpWindow.Yellow, TimeSpan.FromMilliseconds(350));
        }

        // ── 열린 메뉴 안의 눌러야 할 항목(TutorialStep.MenuItem) ──
        // 메뉴는 따로 뜨는 창(팝업)이라 덮개가 닿지 않는다: 그 항목에 노란 테두리(꾸밈 층 — 팝업 안에도 있음)를 숨 쉬듯 두르고, 같은 메뉴의 다른 항목은 누르기를 막는다.

        private MenuItem? _menuItem;
        private Border? _menuGlow;
        /// <summary>열린 메뉴(팝업 창)의 자리(덮개 좌표) — 말풍선이 그 밑에 깔려 가려지지 않게 피한다. 메뉴가 닫혀 있으면 null.</summary>
        private Rect? _menuRect;
        private readonly List<MenuItem> _menuBlocked = [];

        /// <summary>
        /// 단계의 메뉴 항목이 열린 메뉴에 보이면 감싸고 다른 항목을 막고, 메뉴가 닫히면 걷는다.
        /// 항목은 메뉴 줄·하위 메뉴(펼친 MenuItem 안) 것도, 우클릭 메뉴(열린 ContextMenu 바로 안 — 하위 메뉴를 펼칠 항목 등) 것도 된다.
        /// </summary>
        private void UpdateMenu(TutorialStep step)
        {
            var item = step.MenuItem?.Invoke();
            bool open = item is { Parent: MenuItem { IsSubMenuOpen: true } or ContextMenu { IsOpen: true } } && item.IsAttachedToVisualTree();
            if (!open)
            {
                DetachMenu();
                return;
            }
            if (!ReferenceEquals(item, _menuItem))
            {
                DetachMenu();
                if (AdornerLayer.GetAdornerLayer(item!) is not { } layer) return;
                _menuItem = item;
                _menuGlow = new Border
                {
                    BorderBrush = HelpWindow.Yellow,
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(5),
                    IsHitTestVisible = false,
                };
                AdornerLayer.SetAdornedElement(_menuGlow, item);
                layer.Children.Add(_menuGlow);
                foreach (var other in ((ItemsControl)item!.Parent!).Items.OfType<MenuItem>().Where(m => !ReferenceEquals(m, item)))
                {
                    other.AddHandler(InputElement.PointerPressedEvent, BlockMenuPress, RoutingStrategies.Tunnel, handledEventsToo: true);
                    other.AddHandler(InputElement.PointerReleasedEvent, BlockMenuRelease, RoutingStrategies.Tunnel, handledEventsToo: true);
                    _menuBlocked.Add(other);
                }
            }
            // 메뉴 팝업의 자리를 덮개 좌표로(말풍선 자리를 정할 때 피함): 따로 뜬 창(PopupRoot)이면 화면 좌표를 거쳐, 덮개 층 안 팝업이면 바로.
            _menuRect = item!.GetVisualAncestors().OfType<IPopupHost>().FirstOrDefault() switch
            {
                PopupRoot popup => new Rect(_root.PointToClient(popup.PointToScreen(default)), popup.ClientSize).Inflate(4),
                Visual host when host.TranslatePoint(default, _root) is { } at => new Rect(at, host.Bounds.Size).Inflate(4),
                _ => null,
            };
            // 숨 쉬듯(1.6초 주기) 밝아졌다 흐려진다.
            double phase = DateTime.UtcNow.TimeOfDay.TotalSeconds % 1.6 / 1.6;
            _menuGlow!.Opacity = 0.55 + 0.45 * Math.Cos(phase * 2 * Math.PI);
        }

        private void DetachMenu()
        {
            if (_menuGlow?.Parent is AdornerLayer layer) layer.Children.Remove(_menuGlow);
            foreach (var other in _menuBlocked)
            {
                other.RemoveHandler(InputElement.PointerPressedEvent, BlockMenuPress);
                other.RemoveHandler(InputElement.PointerReleasedEvent, BlockMenuRelease);
            }
            _menuBlocked.Clear();
            _menuGlow = null;
            _menuItem = null;
            _menuRect = null;
        }

        private void BlockMenuPress(object? sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
            FlashBubble();
        }

        private void BlockMenuRelease(object? sender, PointerReleasedEventArgs e) => e.Handled = true;

        /// <summary>덮개를 <paramref name="top"/>의 덮개 층으로 옮긴다(이미 거기면 그대로).</summary>
        private void Attach(TopLevel top)
        {
            var layer = OverlayLayer.GetOverlayLayer(top);
            if (ReferenceEquals(layer, _layer)) return;
            Detach();
            if (layer is null) return;
            _layer = layer;
            layer.Children.Add(_root);
        }

        private void Detach()
        {
            _layer?.Children.Remove(_root);
            _layer = null;
        }
    }

    private enum Side { None, Right, Left, Below, Above }

    /// <summary>
    /// 덮개 판: 비춘 칸(둥근 네모)만 뚫고 나머지를 어둡게, 칸 둘레에 노란 테두리(꼭 해야 하는 단계면 숨 쉬듯 퍼지는 빛), 말풍선에서 칸 쪽으로 꼬리.
    /// 칸이 바뀌면 지난 자리에서 새 자리로 미끄러지듯 옮긴다. 누르기는 뚫린 칸(과 <see cref="TutorialStep.PassThrough"/>면 어두운 곳도)을 그대로 통과하고,
    /// 막힌 어두운 곳을 누르면 <see cref="Blocked"/>(칸 테두리가 한 번 밝게 번쩍임).
    /// </summary>
    private sealed class Shade : Control, ICustomHitTest
    {
        private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
        private static readonly IPen HolePen = new Pen(HelpWindow.Yellow, 2);
        private static readonly TimeSpan Slide = TimeSpan.FromMilliseconds(220);
        private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private Rect? _hole, _shown, _from, _drop;
        private DateTime _moved, _flashed;
        private bool _passThrough;
        private bool _dim;
        private bool _pulse, _dragAcross;
        private Side _side;
        private Rect _bubble, _anchor;

        public Shade()
        {
            _anim.Tick += (_, _) => Animate();
            PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _flashed = DateTime.UtcNow;
                _anim.Start();
                Blocked?.Invoke();
            };
            // 막힌 곳에서는 휠·끌기도 아래로 가지 않게.
            PointerWheelChanged += (_, e) => e.Handled = true;
        }

        /// <summary>막힌 어두운 곳을 눌렀다.</summary>
        public event Action? Blocked;

        public void Set(Rect? hole, bool passThrough, bool dim, bool pulse = false, Rect? drop = null, bool dragAcross = false)
        {
            if (_hole == hole && _passThrough == passThrough && _dim == dim && _pulse == pulse && _drop == drop && _dragAcross == dragAcross) return;
            if (_hole != hole)
            {
                // 새 칸으로 미끄러지기(처음 생긴 칸·없어진 칸은 바로).
                _from = _shown is { } shown && hole is not null ? shown : null;
                _moved = DateTime.UtcNow;
                if (_from is null) _shown = hole;
            }
            _hole = hole;
            _passThrough = passThrough;
            _dim = dim;
            _pulse = pulse;
            _drop = drop;
            _dragAcross = dragAcross;
            if (_from is not null || _pulse || _drop is not null || _dragAcross) _anim.Start();
            InvalidateVisual();
        }

        public void SetTail(Side side, Rect bubble, Rect anchor)
        {
            if (_side == side && _bubble == bubble && _anchor == anchor) return;
            _side = side;
            _bubble = bubble;
            _anchor = anchor;
            InvalidateVisual();
        }

        private void Animate()
        {
            var now = DateTime.UtcNow;
            if (_from is { } from && _hole is { } to)
            {
                double t = Math.Min(1, (now - _moved) / Slide);
                double e = 1 - Math.Pow(1 - t, 3);
                _shown = new Rect(from.X + (to.X - from.X) * e, from.Y + (to.Y - from.Y) * e,
                    from.Width + (to.Width - from.Width) * e, from.Height + (to.Height - from.Height) * e);
                if (t >= 1) _from = null;
            }
            if (_from is null && !_pulse && _drop is null && !_dragAcross && now - _flashed > TimeSpan.FromMilliseconds(400)) _anim.Stop();
            InvalidateVisual();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _anim.Stop();
        }

        public bool HitTest(Avalonia.Point point)
            => _dim && !_passThrough && !(_hole is { } h && h.Contains(point)) && !(_drop is { } d && d.Contains(point));

        public override void Render(DrawingContext g)
        {
            var full = new Rect(Bounds.Size);
            var now = DateTime.UtcNow;
            if (_dim)
            {
                if (_shown is { } h)
                {
                    var holes = new GeometryGroup { Children = { new RectangleGeometry(h, 8, 8) } };
                    if (_drop is { } d) holes.Children.Add(new RectangleGeometry(d, 6, 6));
                    g.DrawGeometry(Dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), holes));
                    double phase = (now.TimeOfDay.TotalSeconds % 1.6) / 1.6;
                    if (_pulse) Glow(g, h, phase);
                    double flash = 1 - Math.Min(1, (now - _flashed).TotalMilliseconds / 400);
                    g.DrawRectangle(null, flash > 0 ? new Pen(Brushes.White, 2 + 3 * flash) : HolePen, h, 8, 8);
                    if (_drop is { } drop)
                    {
                        Glow(g, drop, phase);
                        g.DrawRectangle(null, flash > 0 ? new Pen(Brushes.White, 2 + 3 * flash) : HolePen, drop, 6, 6);
                        Ghost(g, h.Center, drop.Center, now);
                    }
                    else if (_dragAcross && h.Width > h.Height * 1.5)
                    {
                        // 칸 여러 개 끌어 고르기: 왼쪽 끝 칸 가운데 → 오른쪽 끝 칸 가운데(칸이 거의 네모라 높이의 반만큼 안쪽).
                        double inset = h.Height / 2;
                        Ghost(g, new Avalonia.Point(h.Left + inset, h.Center.Y), new Avalonia.Point(h.Right - inset, h.Center.Y), now, carry: false);
                    }
                }
                else g.FillRectangle(Dim, full);
            }
            if (_side == Side.None || _hole is null) return;
            var hole = _anchor;
            // 꼬리: 말풍선 변 가운데(칸 쪽으로 당김)에서 칸 쪽으로 뾰족한 세모.
            const double Half = 9, Length = 12;
            var b = _bubble;
            (Avalonia.Point A, Avalonia.Point B, Avalonia.Point Tip) tri = _side switch
            {
                Side.Right => Vertical(b.Left, Math.Clamp(hole.Center.Y, b.Top + 20, b.Bottom - 20), -1),
                Side.Left => Vertical(b.Right, Math.Clamp(hole.Center.Y, b.Top + 20, b.Bottom - 20), 1),
                Side.Below => Horizontal(b.Top, Math.Clamp(hole.Center.X, b.Left + 20, b.Right - 20), -1),
                _ => Horizontal(b.Bottom, Math.Clamp(hole.Center.X, b.Left + 20, b.Right - 20), 1),
            };
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(tri.A, true);
                c.LineTo(tri.Tip);
                c.LineTo(tri.B);
                c.EndFigure(true);
            }
            g.DrawGeometry(AppTheme.PanelBrush, HolePen, geo);

            static (Avalonia.Point, Avalonia.Point, Avalonia.Point) Vertical(double x, double y, int dir)
                => (new(x + 1 * -dir, y - Half), new(x + 1 * -dir, y + Half), new(x + dir * Length, y));
            static (Avalonia.Point, Avalonia.Point, Avalonia.Point) Horizontal(double y, double x, int dir)
                => (new(x - Half, y + 1 * -dir), new(x + Half, y + 1 * -dir), new(x, y + dir * Length));
        }

        /// <summary>숨 쉬듯 퍼지는 빛(<paramref name="phase"/> 0~1, 1.6초 주기): 칸 밖으로 0~8px, 멀어질수록 옅게.</summary>
        private static void Glow(DrawingContext g, Rect r, double phase)
        {
            var c = HelpWindow.Yellow is ISolidColorBrush y ? y.Color : Colors.Gold;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(170 * (1 - phase)), c.R, c.G, c.B)), 3);
            g.DrawRectangle(null, pen, r.Inflate(2 + 8 * phase), 10, 10);
        }

        /// <summary>
        /// 끌기 보이기(2.4초마다 되풀이): 대상 가운데에서 누르고(커서 둘레 고리) 유령 상자를 들어 끌어 놓을 자리로 옮긴 뒤 놓고 흐려진다.
        /// </summary>
        private static void Ghost(DrawingContext g, Avalonia.Point from, Avalonia.Point to, DateTime now, bool carry = true)
        {
            double t = now.TimeOfDay.TotalSeconds % 2.4 / 2.4;
            double move = Math.Clamp((t - 0.15) / 0.55, 0, 1);
            move = move < 0.5 ? 2 * move * move : 1 - Math.Pow(-2 * move + 2, 2) / 2;
            double fade = t < 0.85 ? 1 : 1 - (t - 0.85) / 0.15;
            var at = new Avalonia.Point(from.X + (to.X - from.X) * move, from.Y + (to.Y - from.Y) * move);
            var c = HelpWindow.Yellow is ISolidColorBrush y ? y.Color : Colors.Gold;
            using (g.PushOpacity(fade))
            {
                bool pressed = t >= 0.08 && t < 0.75;
                if (pressed && carry)
                {
                    // 들고 가는 유령 상자.
                    var ghost = new Rect(at.X - 17, at.Y - 17, 34, 34);
                    g.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B)), new Pen(new SolidColorBrush(c), 1.5), ghost, 6, 6);
                }
                else if (pressed)
                {
                    // 끌어 고르기: 누른 곳에서 커서까지 고르는 띠.
                    var band = new Rect(new Avalonia.Point(Math.Min(from.X, at.X) - 14, from.Y - 14), new Avalonia.Point(Math.Max(from.X, at.X) + 14, from.Y + 14));
                    g.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, c.R, c.G, c.B)), new Pen(new SolidColorBrush(c), 1.5), band, 4, 4);
                }
                if (pressed) g.DrawEllipse(null, new Pen(Brushes.White, 1.5), at, 9, 9);
                // 커서(끝이 점에): 흰 화살표 + 검은 테두리.
                using (g.PushTransform(Matrix.CreateTranslation(Math.Round(at.X) + 0.5, Math.Round(at.Y) + 0.5)))
                    g.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1), CursorShape);
            }
        }

        private static readonly Geometry CursorShape = Geometry.Parse("M0,0 L0,16 L3.6,12.6 L6.2,18.6 L8.8,17.5 L6.3,11.6 L11.3,11.6 Z");
    }
}
