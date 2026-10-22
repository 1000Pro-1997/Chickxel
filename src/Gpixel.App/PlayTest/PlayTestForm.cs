using System.Diagnostics;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App.PlayTest;

/// <summary>
/// 움직임 테스트 창(기획 4.7): 탑뷰 8방향 이동 / 플랫포머 이동·점프로 문서의 애니메이션을 시험한다.
/// 메모리에 있는 문서 내용을 쓰며, 저장·애니메이션 편집 뒤 <see cref="DocumentChanged"/>로 다시 읽는다.
/// </summary>
public sealed class PlayTestForm : Window
{
    private const int SidePanelWidth = 290;
    private const int RowWidth = SidePanelWidth - 40;

    private readonly Func<Document?> _currentDocument;
    private readonly PlaySimulation _sim = new();
    private readonly PlayView _view = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Stopwatch _clock = new();
    private readonly HashSet<Key> _held = new();
    private bool _jumpPressed;
    private PlaySprites _sprites = PlaySprites.Empty;
    private bool _scaleChosen;

    /// <summary>모드: 0 = 탑뷰, 1 = 플랫포머(고르면 아래 설정 묶음이 바뀜).</summary>
    private readonly TabStrip _mode = new(TabStripKind.Segmented, UiStrings.S_PlayTestForm__mode_E6FD209C5A, UiStrings.S_PlayTestForm__mode_C301A9DEA7) { Margin = new Thickness(0, 0, 0, 6) };
    private readonly NumericUpDown _scale = Number(1, 16, 4, 1).Presets(1, 2, 3, 4, 5, 6, 8, 10, 12, 16).Unit("×").RecentKey("playtest.scale");
    private readonly CheckBox _showGrid = Check(UiStrings.S_PlayTestForm__showGrid_8744E58387, true);
    private readonly CheckBox _showPivot = Check(UiStrings.S_PlayTestForm__showPivot_C16F3DE272, true);
    private readonly CheckBox _showCollider = Check(UiStrings.S_PlayTestForm__showCollider_96996CCC00, false);
    private readonly CheckBox _plainFacesRight = Check(UiStrings.S_PlayTestForm__plainFacesRight_D2362E1EE6, true);
    private readonly NumericUpDown _topDownSpeed;
    private readonly NumericUpDown _moveSpeed;
    private readonly NumericUpDown _gravity;
    private readonly NumericUpDown _jumpPower;
    private readonly NumericUpDown _maxFall;
    private readonly StackPanel _topDownGroup = Group();
    private readonly StackPanel _platformerGroup = Group();
    private readonly TextBlock _playingLabel = InfoLabel();
    /// <summary>재생 중 이름 후보 줄들(표시 아이콘 + 이름).</summary>
    private readonly StackPanel _candidateList = MarkedList();
    /// <summary>기대하는 애니메이션 이름 줄들(표시 아이콘 + 이름).</summary>
    private readonly StackPanel _expectedList = MarkedList();
    private readonly TextBlock _hintLabel = InfoLabel();
    private readonly TextBlock _keysLabel = InfoLabel();

    public PlayTestForm(Func<Document?> currentDocument)
    {
        _currentDocument = currentDocument ?? throw new ArgumentNullException(nameof(currentDocument));

        Title = UiStrings.S_PlayTestForm_PlayTestForm_B00519A8EF;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 1060;
        Height = 680;
        MinWidth = 720;
        MinHeight = 480;
        Background = AppTheme.BackBrush;
        Foreground = AppTheme.TextBrush;
        FontFamily = AppTheme.FontFamily;
        FontSize = AppTheme.FontSize;
        ShowInTaskbar = true;
        KeyState.Attach(this);

        decimal[] speeds = [30, 45, 60, 75, 90, 120, 150, 180, 240, 300];
        _topDownSpeed = Number(0, 2000, (decimal)_sim.TopDown.Speed, 10).Presets(speeds).Unit("px/s").RecentKey("playtest.speed");
        var ps = _sim.Platformer.Settings;
        _moveSpeed = Number(0, 2000, (decimal)ps.MoveSpeed, 10).Presets(speeds).Unit("px/s").RecentKey("playtest.speed");
        _gravity = Number(0, 10000, (decimal)ps.Gravity, 50)
            .Presets(300, 500, 600, 800, 900, 1000, 1200, 1500, 2000, 3000).Unit("px/s²").RecentKey("playtest.gravity");
        _jumpPower = Number(0, 5000, (decimal)ps.JumpVelocity, 10)
            .Presets(150, 200, 250, 300, 350, 400, 500, 600).Unit("px/s").RecentKey("playtest.jump");
        _maxFall = Number(10, 10000, (decimal)ps.MaxFallSpeed, 10)
            .Presets(200, 300, 400, 500, 600, 800, 1000).Unit("px/s").RecentKey("playtest.fall");

        _topDownGroup.Children.Add(Header(UiStrings.S_PlayTestForm_PlayTestForm_A24362C504));
        _topDownGroup.Children.Add(Row(UiStrings.S_PlayTestForm_PlayTestForm_BC9D1F57FB, _topDownSpeed));

        _platformerGroup.Children.Add(Header(UiStrings.S_PlayTestForm_PlayTestForm_50E363BD43));
        _platformerGroup.Children.Add(Row(UiStrings.S_PlayTestForm_PlayTestForm_BC9D1F57FB, _moveSpeed));
        _platformerGroup.Children.Add(Row(UiStrings.S_PlayTestForm_PlayTestForm_C1DC7F8923, _gravity));
        _platformerGroup.Children.Add(Row(UiStrings.S_PlayTestForm_PlayTestForm_CA9B701E99, _jumpPower));
        _platformerGroup.Children.Add(Row(UiStrings.S_PlayTestForm_PlayTestForm_CEDC62ED3F, _maxFall));

        var resetButton = new Button
        {
            Content = UiStrings.S_PlayTestForm_PlayTestForm_E8671BED08,
            Width = RowWidth,
            Height = 28,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 4),
        };
        resetButton.Click += (_, _) =>
        {
            _sim.Reset();
            _view.Focus();
        };

        _hintLabel.Foreground = AppTheme.WarningBrush;
        _keysLabel.Foreground = AppTheme.DimTextBrush;
        _keysLabel.Margin = new Thickness(0, 12, 0, 0);

        var sideStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12, 10, 8, 10) };
        sideStack.Children.AddRange(new Control[]
        {
            Header(UiStrings.S_PlayTestForm_PlayTestForm_EB62D0B4C6), _mode,
            Header(UiStrings.S_PlayTestForm_PlayTestForm_21F5816FA3), Row(UiStrings.S_PlayTestForm_PlayTestForm_B566CF8EC7, _scale),
            _showGrid, _showPivot, _showCollider, _plainFacesRight,
            _topDownGroup, _platformerGroup, resetButton,
            Header(UiStrings.S_PlayTestForm_PlayTestForm_BCE64AD0E9), _playingLabel, _candidateList,
            Header(UiStrings.S_PlayTestForm_PlayTestForm_2086878572), _expectedList, _hintLabel, _keysLabel,
        });
        var side = new ScrollViewer
        {
            Width = SidePanelWidth,
            Background = AppTheme.PanelBrush,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = sideStack,
        };
        var divider = new Border { Width = 1, Background = AppTheme.BorderBrush };

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(side, Dock.Right);
        DockPanel.SetDock(divider, Dock.Right);
        root.Children.Add(side);
        root.Children.Add(divider);
        root.Children.Add(_view);
        Content = root;

        _mode.SelectedIndex = 0;
        _mode.SelectionChanged += (_, _) => ApplyMode();
        _plainFacesRight.IsCheckedChanged += (_, _) =>
        {
            _sim.PlainFacesRight = _plainFacesRight.On();
            RefreshExpected();
        };
        _topDownSpeed.ValueChanged += (_, _) => _sim.TopDown.Speed = (float)_topDownSpeed.NumValue();
        _moveSpeed.ValueChanged += (_, _) => ps.MoveSpeed = (float)_moveSpeed.NumValue();
        _gravity.ValueChanged += (_, _) => ps.Gravity = (float)_gravity.NumValue();
        _jumpPower.ValueChanged += (_, _) => ps.JumpVelocity = (float)_jumpPower.NumValue();
        _maxFall.ValueChanged += (_, _) => ps.MaxFallSpeed = (float)_maxFall.NumValue();
        _scale.ValueChanged += (_, _) =>
        {
            _scaleChosen = true;
            _view.InvalidateVisual();
        };
        foreach (var number in new[] { _scale, _topDownSpeed, _moveSpeed, _gravity, _jumpPower, _maxFall })
            number.AddHandler(KeyDownEvent, ReturnFocusToView, RoutingStrategies.Tunnel);
        foreach (var check in new[] { _showGrid, _showPivot, _showCollider, _plainFacesRight })
            check.IsCheckedChanged += (_, _) => _view.InvalidateVisual();

        _view.Paint = (g, size) => _sim.Render(g, size, new PlayRenderOptions(
            (int)_scale.NumValue(), _showGrid.On(), _showPivot.On(), _showCollider.On(), _view.IsFocused));
        _view.KeyDown += OnViewKeyDown;
        _view.KeyUp += OnViewKeyUp;
        _view.LostFocus += (_, _) => ReleaseKeys();
        _timer.Tick += OnTick;

        Opened += (_, _) =>
        {
            _view.Focus();
            _clock.Restart();
            _timer.Start();
        };
        Deactivated += (_, _) => ReleaseKeys();
        Closed += (_, _) =>
        {
            IsClosed = true;
            _timer.Stop();
            _sprites.Dispose();
            _sprites = PlaySprites.Empty;
        };

        ApplyMode();
        ReloadDocument();
    }

    /// <summary>창이 닫혔는지(닫힌 창은 다시 보일 수 없다).</summary>
    public bool IsClosed { get; private set; }

    /// <summary>현재 문서에서 그림·칸·애니메이션을 다시 읽는다(저장·애니메이션 편집 뒤 호출).</summary>
    public void DocumentChanged()
    {
        if (IsClosed) return;
        ReloadDocument();
    }

    private void ReloadDocument()
    {
        var doc = _currentDocument();
        var old = _sprites;
        _sprites = PlaySprites.Load(doc);
        _sim.Load(_sprites);
        old.Dispose();

        Title = _sprites.HasDocument ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PlayTestForm_ReloadDocument_2215BDB9EC,_sprites.DocumentName) : UiStrings.S_PlayTestForm_ReloadDocument_224FD48AD2;
        if (!_scaleChosen && _sprites.HasDocument)
        {
            // 처음 한 번만 캐릭터 크기에 맞춰 배율을 고른다(이후에는 사용자가 정한 값을 유지).
            _scale.Value = Math.Clamp(160 / Math.Max(1, _sim.ReferenceHeight), 1, 8);
            _scaleChosen = true;
        }
        RefreshExpected();
        RefreshPlaying();
        _view.InvalidateVisual();
    }

    private void ApplyMode()
    {
        var mode = _mode.SelectedIndex == 1 ? PlayMode.Platformer : PlayMode.TopDown;
        _sim.SetMode(mode);
        _topDownGroup.IsVisible = mode == PlayMode.TopDown;
        _platformerGroup.IsVisible = mode == PlayMode.Platformer;
        _keysLabel.Text = mode == PlayMode.TopDown
            ? UiStrings.S_PlayTestForm_ApplyMode_47A35B1C1A
            : UiStrings.S_PlayTestForm_ApplyMode_85EA432E50;
        RefreshExpected();
        RefreshPlaying();
        _view.InvalidateVisual();
        if (IsVisible) _view.Focus();
    }

    private void RefreshExpected()
    {
        var (lines, hint) = _sim.DescribeExpected();
        SetMarked(_expectedList, lines);
        SetText(_hintLabel, hint ?? "");
        _hintLabel.IsVisible = hint is not null;
    }

    private void RefreshPlaying()
    {
        var (text, candidates) = _sim.DescribePlaying();
        SetText(_playingLabel, text);
        SetMarked(_candidateList, candidates);
    }

    private static void SetText(TextBlock label, string text)
    {
        if (label.Text != text) label.Text = text;
    }

    /// <summary>표시 붙은 이름 줄들을 채운다(매 틱 불리므로 내용이 같으면 그대로 둔다).</summary>
    private static void SetMarked(StackPanel list, IReadOnlyList<MarkedName> lines)
    {
        if (list.Tag is IReadOnlyList<MarkedName> shown && shown.SequenceEqual(lines)) return;
        list.Tag = lines;
        list.Children.Clear();
        foreach (var line in lines)
        {
            var (icon, brush, filled) = line.Mark switch
            {
                NameMark.Chosen => (VectorIcon.Play, AppTheme.AccentBrush, true),
                NameMark.Found => (VectorIcon.Check, AppTheme.TextBrush, false),
                NameMark.Substitute => (VectorIcon.Triangle, AppTheme.WarningBrush, false),
                _ => (VectorIcon.X, AppTheme.WarningBrush, false),
            };
            list.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children =
                {
                    new IconView(icon, brush) { Width = 11, Height = 11, Filled = filled, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = line.Text, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center },
                },
            });
        }
        list.IsVisible = lines.Count > 0;
    }

    // ───────────────────────── 실행 루프·입력 ─────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        float dt = (float)_clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (WindowState == WindowState.Minimized) return;

        bool left = IsHeld(Key.A, Key.Left), right = IsHeld(Key.D, Key.Right);
        bool up = IsHeld(Key.W, Key.Up), down = IsHeld(Key.S, Key.Down);
        // 플랫포머에서는 위쪽 키도 점프로 받는다.
        bool jumpHeld = _held.Contains(Key.Space) || (_sim.Mode == PlayMode.Platformer && up);
        _sim.Tick(dt, new PlayInput(left, right, up, down, jumpHeld, _jumpPressed));
        _jumpPressed = false;

        RefreshPlaying();
        _view.InvalidateVisual();
    }

    private bool IsHeld(Key a, Key b) => _held.Contains(a) || _held.Contains(b);

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.Key;
        switch (key)
        {
            case Key.R:
                _sim.Reset();
                break;
            case Key.Space or Key.W or Key.Up:
                if (!_held.Contains(key)) _jumpPressed = true;
                break;
        }
        _held.Add(key);
        // 방향키·Tab 등으로 포커스가 옮겨 가지 않게 한다.
        e.Handled = true;
    }

    private void OnViewKeyUp(object? sender, KeyEventArgs e)
    {
        _held.Remove(e.Key);
        e.Handled = true;
    }

    private void ReleaseKeys()
    {
        _held.Clear();
        _jumpPressed = false;
    }

    /// <summary>숫자 칸에서 Enter/Esc를 누르면 다시 화면으로 키보드 입력을 돌린다.</summary>
    private void ReturnFocusToView(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        _view.Focus();
        e.Handled = true;
    }

    // ───────────────────────── 컨트롤 만들기 ─────────────────────────

    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Increment = step,
        Width = 110,
        FormatString = "0",
        ClipValueToMinMax = true,
        HorizontalContentAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static CheckBox Check(string text, bool value) => Ui.Check(text, value);

    private static StackPanel Group() => new() { Orientation = Orientation.Vertical };

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        Foreground = AppTheme.TextBrush,
        Margin = new Thickness(0, 10, 0, 4),
    };

    private static TextBlock InfoLabel() => new()
    {
        Foreground = AppTheme.TextBrush,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = RowWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static StackPanel MarkedList() => new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 1,
        MaxWidth = RowWidth,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static Control Row(string text, Control input)
    {
        var row = new DockPanel { Width = RowWidth, Margin = new Thickness(0, 1, 0, 1), LastChildFill = true };
        DockPanel.SetDock(input, Dock.Right);
        var label = new TextBlock
        {
            Text = text,
            Foreground = AppTheme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.Children.Add(input);
        row.Children.Add(label);
        return row;
    }
}
