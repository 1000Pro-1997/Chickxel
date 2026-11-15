using System.Diagnostics;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App.PlayTest;

/// <summary>
/// 움직임 테스트 창(기획 4.7): 탑뷰 8방향 이동 / 플랫포머 이동·점프·사다리·벽타기로 문서의 애니메이션을 시험한다.
/// 열린 문서의 타일맵 레이어를 깔고 지형·사다리로 쓸 층을 고를 수 있다.
/// 메모리에 있는 문서 내용을 쓰며, 저장·애니메이션 편집 뒤 <see cref="DocumentChanged"/>로 다시 읽는다.
/// </summary>
public sealed class PlayTestForm : Window
{
    private const int SidePanelWidth = 290;
    private const int RowWidth = SidePanelWidth - 40;

    private readonly Func<IReadOnlyList<PlayCharacterSource>> _characterSources;
    /// <summary>캐릭터로 쓸 탭 목록과 지금 목록의 출처들, 고른 탭의 열쇠(창을 연 때의 탭으로 시작).</summary>
    private readonly ComboBox _character = Ui.Combo(170);
    private IReadOnlyList<PlayCharacterSource> _characters = [];
    private object? _characterKey;
    private bool _charactersUpdating;
    private readonly Func<IReadOnlyList<PlayMapSource>> _mapSources;
    /// <summary>깔 타일맵 목록(맨 앞은 "없음")과 지금 목록의 출처들.</summary>
    private readonly ComboBox _map = Ui.Combo(170);
    private IReadOnlyList<PlayMapSource> _maps = [];
    private bool _mapsUpdating;
    /// <summary>타일맵(출처 이름)·모드마다 지형·사다리로 고른 층 Id(창이 열려 있는 동안 기억, 처음에는 층 이름으로 고름).</summary>
    private readonly Dictionary<(string Map, PlayMode Mode), (HashSet<int> Terrain, HashSet<int> Ladder)> _floorChoices = new();
    /// <summary>지금 층 체크 상자를 만든 맵·모드의 층 목록 서명(같으면 다시 만들지 않음).</summary>
    private string? _floorsShown;
    private readonly StackPanel _floorsGroup = Group();
    private readonly WrapPanel _terrainList = FloorList();
    private readonly WrapPanel _ladderList = FloorList();
    private readonly Control _ladderHeader;
    private readonly TextBlock _noTerrainLabel = InfoLabel();
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
    private readonly CheckBox _showCollider = Check(UiStrings.PlayTest_ShowColliders, false);
    private readonly CheckBox _plainFacesRight = Check(UiStrings.S_PlayTestForm__plainFacesRight_D2362E1EE6, true);
    private readonly NumericUpDown _topDownSpeed;
    private readonly NumericUpDown _moveSpeed;
    private readonly NumericUpDown _gravity;
    private readonly NumericUpDown _jumpPower;
    private readonly NumericUpDown _maxFall;
    private readonly NumericUpDown _climbSpeed;
    private readonly CheckBox _wallClimb = Check(UiStrings.PlayTest_WallClimb, false);
    private readonly StackPanel _topDownGroup = Group();
    private readonly StackPanel _platformerGroup = Group();
    private readonly TextBlock _playingLabel = InfoLabel();
    /// <summary>재생 중 이름 후보 줄들(표시 아이콘 + 이름).</summary>
    private readonly StackPanel _candidateList = MarkedList();
    /// <summary>기대하는 애니메이션 이름 줄들(표시 아이콘 + 이름).</summary>
    private readonly StackPanel _expectedList = MarkedList();
    private readonly TextBlock _hintLabel = InfoLabel();
    private readonly TextBlock _keysLabel = InfoLabel();

    internal PlayTestForm(Func<IReadOnlyList<PlayCharacterSource>> characterSources, object? initialCharacter, Func<IReadOnlyList<PlayMapSource>> mapSources)
    {
        _characterSources = characterSources;
        _characterKey = initialCharacter;
        _mapSources = mapSources;

        Title = UiStrings.S_PlayTestForm_PlayTestForm_B00519A8EF;
        // 대상 도움말(F1): 사용 가이드의 움직임 테스트 쪽
        this.HelpAt("screen-play-test");
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
        _climbSpeed = Number(0, 2000, (decimal)ps.ClimbSpeed, 10).Presets(speeds).Unit("px/s").RecentKey("playtest.climb");

        _topDownGroup.Children.Add(Header(UiStrings.S_PlayTestForm_PlayTestForm_A24362C504));
        _topDownGroup.Children.Add(Row(UiStrings.S_PlayTestForm_PlayTestForm_BC9D1F57FB, _topDownSpeed));

        // 캐릭터: 그릴 탭 고르기(창을 연 때의 탭으로 시작 — 다른 탭으로 옮겨 가도 바뀌지 않음)
        _character.Width = RowWidth;
        // 맵(두 모드 함께): 깔 타일맵 고르기, 그 아래 지형·사다리로 쓸 층
        _map.Width = RowWidth;
        _ladderHeader = LabelWithInfo(UiStrings.PlayTest_LadderFloors, UiStrings.PlayTest_LadderFloorsInfo);
        _noTerrainLabel.Foreground = AppTheme.WarningBrush;
        _noTerrainLabel.Text = UiStrings.PlayTest_NoTerrain;
        _floorsGroup.Children.Add(LabelWithInfo(UiStrings.PlayTest_TerrainFloors, UiStrings.PlayTest_TerrainFloorsInfo));
        _floorsGroup.Children.Add(_terrainList);
        _floorsGroup.Children.Add(_ladderHeader);
        _floorsGroup.Children.Add(_ladderList);
        _floorsGroup.Children.Add(_noTerrainLabel);
        _floorsGroup.IsVisible = false;
        var mapGroup = Group();
        mapGroup.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 4),
            Children =
            {
                new TextBlock { Text = UiStrings.PlayTest_Map, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center },
                Ui.InfoBadge(UiStrings.PlayTest_MapInfo),
            },
        });
        mapGroup.Children.Add(_map);
        mapGroup.Children.Add(_floorsGroup);

        _platformerGroup.Children.Add(Header(UiStrings.S_PlayTestForm_PlayTestForm_50E363BD43));
        _platformerGroup.Children.Add(Row(UiStrings.S_PlayTestForm_PlayTestForm_BC9D1F57FB, _moveSpeed));
        _platformerGroup.Children.Add(Row(UiStrings.S_PlayTestForm_PlayTestForm_C1DC7F8923, _gravity));
        _platformerGroup.Children.Add(Row(UiStrings.S_PlayTestForm_PlayTestForm_CA9B701E99, _jumpPower));
        _platformerGroup.Children.Add(Row(UiStrings.S_PlayTestForm_PlayTestForm_CEDC62ED3F, _maxFall));
        _platformerGroup.Children.Add(Row(UiStrings.PlayTest_ClimbSpeed, _climbSpeed));
        _platformerGroup.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _wallClimb, Ui.InfoBadge(UiStrings.PlayTest_WallClimbInfo) },
        });

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
            Header(UiStrings.PlayTest_Character), _character,
            Header(UiStrings.S_PlayTestForm_PlayTestForm_21F5816FA3), Row(UiStrings.S_PlayTestForm_PlayTestForm_B566CF8EC7, _scale),
            _showGrid, _showPivot,
            new StackPanel { Orientation = Orientation.Horizontal, Children = { _showCollider, Ui.InfoBadge(UiStrings.PlayTest_ShowCollidersInfo) } },
            _plainFacesRight,
            mapGroup, _topDownGroup, _platformerGroup, resetButton,
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
        _character.DropDownOpened += (_, _) => RefreshCharacterList();
        _character.SelectionChanged += (_, _) =>
        {
            if (_charactersUpdating || _character.SelectedIndex < 0 || _character.SelectedIndex >= _characters.Count) return;
            _characterKey = _characters[_character.SelectedIndex].Key;
            ReloadDocument();
            _view.Focus();
        };
        _map.DropDownOpened += (_, _) => RefreshMapList();
        _map.SelectionChanged += (_, _) =>
        {
            if (_mapsUpdating) return;
            ApplyMap(SelectedMap?.Read(), restart: true);
        };
        _wallClimb.IsCheckedChanged += (_, _) =>
        {
            ps.WallClimb = _wallClimb.On();
            RefreshExpected();
            _view.Focus();
        };
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
        _climbSpeed.ValueChanged += (_, _) => ps.ClimbSpeed = (float)_climbSpeed.NumValue();
        _scale.ValueChanged += (_, _) =>
        {
            _scaleChosen = true;
            _view.InvalidateVisual();
        };
        foreach (var number in new[] { _scale, _topDownSpeed, _moveSpeed, _gravity, _jumpPower, _maxFall, _climbSpeed })
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

    /// <summary>고른 타일맵 출처(없음이면 null).</summary>
    private PlayMapSource? SelectedMap => _map.SelectedIndex > 0 && _map.SelectedIndex <= _maps.Count ? _maps[_map.SelectedIndex - 1] : null;

    /// <summary>타일맵 목록을 다시 채운다(고른 것은 이름으로 지킴, 없어졌으면 "없음").</summary>
    private void RefreshMapList()
    {
        var keep = SelectedMap?.Name;
        _maps = _mapSources();
        _mapsUpdating = true;
        try
        {
            _map.ItemsSource = new[] { UiStrings.PlayTest_MapNone }.Concat(_maps.Select(m => m.Name)).ToList();
            int at = keep is null ? -1 : _maps.ToList().FindIndex(m => m.Name == keep);
            _map.SelectedIndex = at + 1;
        }
        finally
        {
            _mapsUpdating = false;
        }
        if (keep is not null && SelectedMap is null) ApplyMap(null, restart: true);
    }

    /// <summary>
    /// 고른 타일맵의 지금 모드 층 선택. 처음이면 층 이름으로 고른다(탑뷰 = 벽·물건·물, 플랫포머 = 바닥 — <see cref="PlayMap.DefaultTerrain"/>).
    /// </summary>
    private (HashSet<int> Terrain, HashSet<int> Ladder) FloorChoice(PlayMap? map)
    {
        if (SelectedMap is not { } source || map is null) return ([], []);
        var key = (source.Name, _sim.Mode);
        if (!_floorChoices.TryGetValue(key, out var choice))
            _floorChoices[key] = choice = (map.DefaultTerrain(_sim.Mode == PlayMode.TopDown), []);
        return choice;
    }

    /// <summary>타일맵을 깔고(null = 없음) 층 체크 상자를 맞춘다.</summary>
    private void ApplyMap(PlayMap? map, bool restart)
    {
        var choice = FloorChoice(map);
        ShowFloors(map, choice);
        _sim.SetMap(map, choice.Terrain, choice.Ladder, restart);
        RefreshFloorHints();
        RefreshExpected();
        _view.InvalidateVisual();
    }

    /// <summary>맵 층마다 지형·사다리 체크 상자를 만든다(층 목록이 그대로면 두고, 숨긴 층은 이름 뒤에 표시).</summary>
    private void ShowFloors(PlayMap? map, (HashSet<int> Terrain, HashSet<int> Ladder) choice)
    {
        var floors = map?.Tileset.Floors ?? [];
        string signature = SelectedMap?.Name + "\n" + _sim.Mode + "\n" + string.Join("\n", floors.Select(f => $"{f.Id}:{f.Name}:{map!.Data.IsFloorHidden(f.Id)}"));
        _floorsGroup.IsVisible = floors.Length > 0;
        if (signature == _floorsShown) return;
        _floorsShown = signature;
        _terrainList.Children.Clear();
        _ladderList.Children.Clear();
        foreach (var floor in floors)
        {
            string name = map!.Data.IsFloorHidden(floor.Id)
                ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.PlayTest_HiddenFloor, floor.Name)
                : floor.Name;
            _terrainList.Children.Add(FloorCheck(name, choice.Terrain, floor.Id));
            _ladderList.Children.Add(FloorCheck(name, choice.Ladder, floor.Id));
        }
    }

    /// <summary>층 하나의 체크 상자: 바꾸면 그 층을 넣거나 빼고 맵을 다시 만든다(처음 위치로).</summary>
    private CheckBox FloorCheck(string text, HashSet<int> set, int floor)
    {
        var check = Check(text, set.Contains(floor));
        check.Margin = new Thickness(0, 0, 8, 0);
        check.IsCheckedChanged += (_, _) =>
        {
            if (check.On()) set.Add(floor);
            else set.Remove(floor);
            ApplyMap(_sim.Map, restart: true);
            _view.Focus();
        };
        return check;
    }

    /// <summary>사다리 층은 플랫포머에서만, 지형이 없다는 안내는 플랫포머에 맵을 깔고 지형 층이 없을 때만.</summary>
    private void RefreshFloorHints()
    {
        bool platformer = _sim.Mode == PlayMode.Platformer;
        _ladderHeader.IsVisible = platformer;
        _ladderList.IsVisible = platformer;
        _noTerrainLabel.IsVisible = platformer && _sim.Map is { } map && FloorChoice(map).Terrain.Count == 0;
    }

    /// <summary>창이 닫혔는지(닫힌 창은 다시 보일 수 없다).</summary>
    public bool IsClosed { get; private set; }

    /// <summary>현재 문서에서 그림·칸·애니메이션을 다시 읽는다(저장·애니메이션 편집 뒤 호출).</summary>
    public void DocumentChanged()
    {
        if (IsClosed) return;
        ReloadDocument();
    }

    /// <summary>캐릭터 목록을 다시 채운다(고른 탭은 열쇠로 지킴, 닫혔으면 첫 탭).</summary>
    private void RefreshCharacterList()
    {
        _characters = _characterSources();
        int at = _characters.ToList().FindIndex(c => Equals(c.Key, _characterKey));
        if (at < 0 && _characters.Count > 0) at = 0;
        _characterKey = at >= 0 ? _characters[at].Key : null;
        _charactersUpdating = true;
        try
        {
            _character.ItemsSource = _characters.Select(c => c.Name).ToList();
            _character.SelectedIndex = at;
        }
        finally
        {
            _charactersUpdating = false;
        }
    }

    private void ReloadDocument()
    {
        RefreshCharacterList();
        var doc = _characterKey is null ? null : _characters.First(c => Equals(c.Key, _characterKey)).Build();
        var old = _sprites;
        _sprites = PlaySprites.Load(doc);
        _sim.Load(_sprites);
        old.Dispose();
        // 깐 타일맵도 지금 내용으로(자리는 지킴)
        RefreshMapList();
        if (SelectedMap is { } map) ApplyMap(map.Read(), restart: false);

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
        if (_sim.Map is { } map) ApplyMap(map, restart: true);
        _topDownGroup.IsVisible = mode == PlayMode.TopDown;
        _platformerGroup.IsVisible = mode == PlayMode.Platformer;
        _keysLabel.Text = mode == PlayMode.TopDown
            ? UiStrings.S_PlayTestForm_ApplyMode_47A35B1C1A
            : UiStrings.PlayTest_KeysPlatformer;
        RefreshFloorHints();
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
        // 플랫포머의 위 키는 컨트롤러가 사다리·벽에서는 오르기로, 그 밖에서는 점프로 받는다.
        _sim.Tick(dt, new PlayInput(left, right, up, down, _held.Contains(Key.Space), _jumpPressed));
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
            case Key.Space:
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

    private static WrapPanel FloorList() => new() { Orientation = Orientation.Horizontal, MaxWidth = RowWidth, Margin = new Thickness(6, 0, 0, 2) };

    /// <summary>작은 제목(보통 굵기) + 정보박스.</summary>
    private static Control LabelWithInfo(string text, string info) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 6, 0, 2),
        Children =
        {
            new TextBlock { Text = text, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center },
            Ui.InfoBadge(info),
        },
    };

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
