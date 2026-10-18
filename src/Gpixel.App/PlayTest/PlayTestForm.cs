using System.Diagnostics;
using Gpixel.Core.Model;

namespace Gpixel.App.PlayTest;

/// <summary>
/// 움직임 테스트 창(기획 4.7): 탑뷰 8방향 이동 / 플랫포머 이동·점프로 문서의 애니메이션을 시험한다.
/// 메모리에 있는 문서 내용을 쓰며, 저장·애니메이션 편집 뒤 <see cref="DocumentChanged"/>로 다시 읽는다.
/// </summary>
public sealed class PlayTestForm : Form
{
    private const int SidePanelWidth = 290;
    private const int RowWidth = SidePanelWidth - 40;

    private readonly Func<Document?> _currentDocument;
    private readonly PlaySimulation _sim = new();
    private readonly PlayView _view = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = new();
    private readonly HashSet<Keys> _held = new();
    private bool _jumpPressed;
    private PlaySprites _sprites = PlaySprites.Empty;
    private bool _scaleChosen;

    private readonly RadioButton _topDownMode = ModeButton("탑뷰");
    private readonly RadioButton _platformerMode = ModeButton("플랫포머");
    private readonly NumericUpDown _scale = Number(1, 16, 4, 1);
    private readonly CheckBox _showGrid = Check("격자 표시 (1칸 = PPU)", true);
    private readonly CheckBox _showPivot = Check("피벗 표시", true);
    private readonly CheckBox _showCollider = Check("충돌 상자 표시", false);
    private readonly CheckBox _plainFacesRight = Check("방향 없는 그림은 오른쪽을 봄", true);
    private readonly NumericUpDown _topDownSpeed;
    private readonly NumericUpDown _moveSpeed;
    private readonly NumericUpDown _gravity;
    private readonly NumericUpDown _jumpPower;
    private readonly NumericUpDown _maxFall;
    private readonly FlowLayoutPanel _topDownGroup = Group();
    private readonly FlowLayoutPanel _platformerGroup = Group();
    private readonly Label _playingLabel = InfoLabel();
    private readonly Label _expectedLabel = InfoLabel();
    private readonly Label _hintLabel = InfoLabel();
    private readonly Label _keysLabel = InfoLabel();

    public PlayTestForm(Func<Document?> currentDocument)
    {
        _currentDocument = currentDocument ?? throw new ArgumentNullException(nameof(currentDocument));

        Text = "움직임 테스트";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1060, 680);
        MinimumSize = new Size(720, 480);
        BackColor = Theme.BackColor;
        ForeColor = Theme.TextColor;
        Font = Theme.UiFont;
        ShowInTaskbar = true;

        _topDownSpeed = Number(0, 2000, (decimal)_sim.TopDown.Speed, 10);
        var ps = _sim.Platformer.Settings;
        _moveSpeed = Number(0, 2000, (decimal)ps.MoveSpeed, 10);
        _gravity = Number(0, 10000, (decimal)ps.Gravity, 50);
        _jumpPower = Number(0, 5000, (decimal)ps.JumpVelocity, 10);
        _maxFall = Number(10, 10000, (decimal)ps.MaxFallSpeed, 10);

        var side = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = SidePanelWidth,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12, 10, 8, 10),
            BackColor = Theme.PanelColor,
        };
        var divider = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Theme.BorderColor };

        var modeRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 6),
            BackColor = Theme.PanelColor,
        };
        modeRow.Controls.Add(_topDownMode);
        modeRow.Controls.Add(_platformerMode);

        _topDownGroup.Controls.Add(Header("탑뷰"));
        _topDownGroup.Controls.Add(Row("이동 속도 (px/s)", _topDownSpeed));

        _platformerGroup.Controls.Add(Header("플랫포머"));
        _platformerGroup.Controls.Add(Row("이동 속도 (px/s)", _moveSpeed));
        _platformerGroup.Controls.Add(Row("중력 (px/s²)", _gravity));
        _platformerGroup.Controls.Add(Row("점프 힘 (px/s)", _jumpPower));
        _platformerGroup.Controls.Add(Row("최대 낙하 속도 (px/s)", _maxFall));

        var resetButton = new Button
        {
            Text = "처음 위치로 (R)",
            Width = RowWidth,
            Height = 28,
            Margin = new Padding(0, 8, 0, 4),
        };
        resetButton.Click += (_, _) =>
        {
            _sim.Reset();
            _view.Focus();
        };

        side.Controls.Add(Header("모드"));
        side.Controls.Add(modeRow);
        side.Controls.Add(Header("보기"));
        side.Controls.Add(Row("배율 (정수)", _scale));
        side.Controls.Add(_showGrid);
        side.Controls.Add(_showPivot);
        side.Controls.Add(_showCollider);
        side.Controls.Add(_plainFacesRight);
        side.Controls.Add(_topDownGroup);
        side.Controls.Add(_platformerGroup);
        side.Controls.Add(resetButton);
        side.Controls.Add(Header("재생 중"));
        side.Controls.Add(_playingLabel);
        side.Controls.Add(Header("애니메이션 이름 확인"));
        side.Controls.Add(_expectedLabel);
        side.Controls.Add(_hintLabel);
        side.Controls.Add(_keysLabel);

        Controls.Add(_view);
        Controls.Add(divider);
        Controls.Add(side);

        Theme.Apply(this);
        _hintLabel.ForeColor = Theme.WarningColor;
        _keysLabel.ForeColor = Theme.DimTextColor;
        _keysLabel.Margin = new Padding(0, 12, 0, 0);

        _topDownMode.Checked = true;
        _topDownMode.CheckedChanged += (_, _) => ApplyMode();
        _platformerMode.CheckedChanged += (_, _) => ApplyMode();
        _plainFacesRight.CheckedChanged += (_, _) =>
        {
            _sim.PlainFacesRight = _plainFacesRight.Checked;
            RefreshExpected();
        };
        _topDownSpeed.ValueChanged += (_, _) => _sim.TopDown.Speed = (float)_topDownSpeed.Value;
        _moveSpeed.ValueChanged += (_, _) => ps.MoveSpeed = (float)_moveSpeed.Value;
        _gravity.ValueChanged += (_, _) => ps.Gravity = (float)_gravity.Value;
        _jumpPower.ValueChanged += (_, _) => ps.JumpVelocity = (float)_jumpPower.Value;
        _maxFall.ValueChanged += (_, _) => ps.MaxFallSpeed = (float)_maxFall.Value;
        _scale.ValueChanged += (_, _) => _scaleChosen = true;
        foreach (var number in new[] { _scale, _topDownSpeed, _moveSpeed, _gravity, _jumpPower, _maxFall })
            number.KeyDown += ReturnFocusToView;
        foreach (var check in new[] { _showGrid, _showPivot, _showCollider, _plainFacesRight })
            check.CheckedChanged += (_, _) => _view.Invalidate();

        _view.Paint += (_, e) => _sim.Render(e.Graphics, _view.ClientSize, new PlayRenderOptions(
            (int)_scale.Value, _showGrid.Checked, _showPivot.Checked, _showCollider.Checked, _view.Focused));
        _view.KeyDown += OnViewKeyDown;
        _view.KeyUp += OnViewKeyUp;
        _view.LostFocus += (_, _) => ReleaseKeys();
        _timer.Tick += OnTick;

        ApplyMode();
        ReloadDocument();
    }

    /// <summary>현재 문서에서 그림·칸·애니메이션을 다시 읽는다(저장·애니메이션 편집 뒤 호출).</summary>
    public void DocumentChanged()
    {
        if (IsDisposed) return;
        ReloadDocument();
    }

    private void ReloadDocument()
    {
        var doc = _currentDocument();
        var old = _sprites;
        _sprites = PlaySprites.Load(doc);
        _sim.Load(_sprites);
        old.Dispose();

        Text = _sprites.HasDocument ? $"움직임 테스트 - {_sprites.DocumentName}" : "움직임 테스트";
        if (!_scaleChosen && _sprites.HasDocument)
        {
            // 처음 한 번만 캐릭터 크기에 맞춰 배율을 고른다(이후에는 사용자가 정한 값을 유지).
            _scale.Value = Math.Clamp(160 / Math.Max(1, _sim.ReferenceHeight), 1, 8);
            _scaleChosen = true;
        }
        RefreshExpected();
        RefreshPlaying();
        _view.Invalidate();
    }

    private void ApplyMode()
    {
        var mode = _platformerMode.Checked ? PlayMode.Platformer : PlayMode.TopDown;
        _sim.SetMode(mode);
        _topDownGroup.Visible = mode == PlayMode.TopDown;
        _platformerGroup.Visible = mode == PlayMode.Platformer;
        _keysLabel.Text = mode == PlayMode.TopDown
            ? "조작: WASD / 방향키로 8방향 이동, R 처음 위치"
            : "조작: A·D / ←·→ 이동, 스페이스(또는 W·↑) 점프, R 처음 위치";
        RefreshExpected();
        RefreshPlaying();
        _view.Invalidate();
        if (Visible) _view.Focus();
    }

    private void RefreshExpected()
    {
        var (lines, hint) = _sim.DescribeExpected();
        SetText(_expectedLabel, lines);
        SetText(_hintLabel, hint ?? "");
        _hintLabel.Visible = hint is not null;
    }

    private void RefreshPlaying() => SetText(_playingLabel, _sim.DescribePlaying());

    private static void SetText(Label label, string text)
    {
        if (label.Text != text) label.Text = text;
    }

    // ───────────────────────── 실행 루프·입력 ─────────────────────────

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _view.Focus();
        _clock.Restart();
        _timer.Start();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        ReleaseKeys();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        base.OnFormClosed(e);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        float dt = (float)_clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (WindowState == FormWindowState.Minimized) return;

        bool left = IsHeld(Keys.A, Keys.Left), right = IsHeld(Keys.D, Keys.Right);
        bool up = IsHeld(Keys.W, Keys.Up), down = IsHeld(Keys.S, Keys.Down);
        // 플랫포머에서는 위쪽 키도 점프로 받는다.
        bool jumpHeld = _held.Contains(Keys.Space) || (_sim.Mode == PlayMode.Platformer && up);
        _sim.Tick(dt, new PlayInput(left, right, up, down, jumpHeld, _jumpPressed));
        _jumpPressed = false;

        RefreshPlaying();
        _view.Invalidate();
    }

    private bool IsHeld(Keys a, Keys b) => _held.Contains(a) || _held.Contains(b);

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        var key = e.KeyCode;
        switch (key)
        {
            case Keys.R:
                _sim.Reset();
                break;
            case Keys.Space or Keys.W or Keys.Up:
                if (!_held.Contains(key)) _jumpPressed = true;
                break;
        }
        _held.Add(key);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void OnViewKeyUp(object? sender, KeyEventArgs e)
    {
        _held.Remove(e.KeyCode);
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
        if (e.KeyCode is not (Keys.Enter or Keys.Escape)) return;
        _view.Focus();
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _sim.Dispose();
            _sprites.Dispose();
        }
        base.Dispose(disposing);
    }

    // ───────────────────────── 컨트롤 만들기 ─────────────────────────

    private static RadioButton ModeButton(string text) => new()
    {
        Text = text,
        Appearance = Appearance.Button,
        TextAlign = ContentAlignment.MiddleCenter,
        Size = new Size(RowWidth / 2 - 2, 30),
        Margin = new Padding(0, 0, 4, 0),
    };

    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Increment = step,
        Width = 90,
        TextAlign = HorizontalAlignment.Right,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private static CheckBox Check(string text, bool value) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        Margin = new Padding(0, 2, 0, 2),
    };

    private static FlowLayoutPanel Group() => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        Margin = Padding.Empty,
        BackColor = Theme.PanelColor,
    };

    private static Label Header(string text) => new()
    {
        Text = text,
        Font = Theme.BoldFont,
        AutoSize = true,
        Margin = new Padding(0, 10, 0, 4),
    };

    private static Label InfoLabel() => new()
    {
        AutoSize = true,
        MaximumSize = new Size(RowWidth, 0),
        Margin = new Padding(0, 0, 0, 4),
    };

    private static Panel Row(string text, Control input)
    {
        var row = new Panel { Size = new Size(RowWidth, 28), Margin = new Padding(0, 1, 0, 1), BackColor = Theme.PanelColor };
        var label = new Label
        {
            Text = text,
            AutoSize = false,
            Size = new Size(RowWidth - input.Width - 6, 28),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        input.Location = new Point(RowWidth - input.Width, 3);
        row.Controls.Add(label);
        row.Controls.Add(input);
        return row;
    }
}
