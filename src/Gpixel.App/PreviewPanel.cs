using System.Diagnostics;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// Piskel식 미리보기(오른쪽 위): 현재 태그(없으면 전체 프레임)를 실시간으로 재생한다.
/// 자체 FPS(0 = 프레임 길이 사용), 배율 1×/2×/4×/맞춤, 배경색을 고를 수 있고, 멈춰 있으면 현재 프레임을 보여 준다.
/// 아틀라스 프로젝트에서는 재생 없이 고른 스프라이트(보이는 레이어 합성)를 보여 준다.
/// </summary>
public sealed class PreviewPanel : UserControl
{
    private static readonly string[] Backgrounds = ["체커", "검정", "흰색", "회색", "초록", "사용자 색…"];
    private static readonly Color?[] PresetColors = [null, Color.Black, Color.White, Color.FromArgb(128, 128, 128), Color.FromArgb(60, 140, 70), null];

    private readonly EditorContext _ctx;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = new();
    private readonly View _view;
    private readonly Button _play;
    private readonly NumericUpDown _fps;
    private readonly ComboBox _background;
    private readonly CheckBox[] _scaleButtons;
    private int _scale; // 0 = 맞춤
    private Color? _bgColor;
    private Color _customColor = Color.FromArgb(40, 60, 90);
    private bool _suppressBackground;
    private List<int> _order = new();
    private int _orderIndex;
    private double _elapsedInFrame;

    /// <summary>재생 중 보이는 프레임이 바뀜(-1 = 재생 멈춤).</summary>
    public event EventHandler<int>? PlayingFrameChanged;

    public PreviewPanel(EditorContext ctx)
    {
        _ctx = ctx;
        BackColor = Theme.PanelColor;
        _view = new View(this) { Dock = DockStyle.Fill };
        _play = Ui.Button("▶", (_, _) => TogglePlay(), 34);
        new ToolTip().SetToolTip(_play, "재생/정지 (Shift+Enter)");
        _fps = Ui.Num(0, 60, 0, 46);
        new ToolTip().SetToolTip(_fps, "미리보기 FPS (0 = 프레임마다 정한 길이)");
        _scaleButtons = [ScaleButton("맞춤", 0), ScaleButton("1×", 1), ScaleButton("2×", 2), ScaleButton("4×", 4)];
        _background = Ui.Combo(80, Backgrounds);
        _background.SelectedIndexChanged += (_, _) => { if (!_suppressBackground) OnBackgroundChanged(); };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = Theme.PanelColor, WrapContents = true };
        bar.Controls.AddRange([_play, Ui.Label("FPS"), _fps, .. _scaleButtons, _background]);
        Controls.Add(_view);
        Controls.Add(bar);
        Controls.Add(Ui.Header("미리보기"));
        SetScale(0);

        _timer.Tick += (_, _) => Tick();
        ctx.ProjectReplaced += (_, _) =>
        {
            Stop();
            _play.Enabled = _fps.Enabled = !_ctx.IsAtlas;
            _view.Invalidate();
        };
        ctx.ViewChanged += (_, _) => _view.Invalidate();
    }

    private CheckBox ScaleButton(string text, int scale)
    {
        var b = new CheckBox
        {
            Text = text,
            Appearance = Appearance.Button,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.InputColor,
            ForeColor = Theme.TextColor,
            Margin = new Padding(1, 2, 1, 2),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        b.FlatAppearance.BorderColor = Theme.BorderColor;
        b.FlatAppearance.CheckedBackColor = Theme.AccentColor;
        b.Click += (_, _) => SetScale(scale);
        return b;
    }

    private void SetScale(int scale)
    {
        _scale = scale;
        int[] values = [0, 1, 2, 4];
        for (int i = 0; i < _scaleButtons.Length; i++) _scaleButtons[i].Checked = values[i] == scale;
        _view.Invalidate();
    }

    /// <summary>배율 설정값(0 = 맞춤).</summary>
    public int ZoomSetting
    {
        get => _scale;
        set => SetScale(value is 1 or 2 or 4 ? value : 0);
    }

    /// <summary>미리보기 FPS(0 = 프레임 길이).</summary>
    public int FpsSetting
    {
        get => (int)_fps.Value;
        set => _fps.Value = Math.Clamp(value, 0, 60);
    }

    /// <summary>배경 설정값("checker" 또는 #RRGGBB).</summary>
    public string BackgroundSetting
    {
        get => _bgColor is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "checker";
        set
        {
            if (value != "checker" && Color32.TryParseHex(value, out var c))
            {
                _customColor = c.ToColor();
                _bgColor = _customColor;
                int idx = Array.FindIndex(PresetColors, p => p?.ToArgb() == _customColor.ToArgb());
                _suppressBackground = true;
                _background.SelectedIndex = idx >= 0 ? idx : Backgrounds.Length - 1;
                _suppressBackground = false;
            }
            _view.Invalidate();
        }
    }

    private void OnBackgroundChanged()
    {
        int i = _background.SelectedIndex;
        if (i == Backgrounds.Length - 1)
        {
            using var dlg = new ColorDialog { Color = _customColor, FullOpen = true };
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK) _customColor = dlg.Color;
            _bgColor = _customColor;
        }
        else _bgColor = PresetColors[i];
        _view.Invalidate();
    }

    // ───────────── 재생 ─────────────

    public bool IsPlaying => _timer.Enabled;

    /// <summary>재생 중 보이는 프레임(멈춰 있으면 현재 프레임).</summary>
    public int ShownFrame => IsPlaying && _orderIndex < _order.Count ? _order[_orderIndex] : _ctx.CurrentFrame;

    public void TogglePlay()
    {
        if (IsPlaying) Stop();
        else Play();
    }

    public void Play()
    {
        if (!_ctx.HasProject || _ctx.IsAtlas || _ctx.FrameCount == 0) return;
        _order = _ctx.PlaybackOrder();
        if (_order.Count == 0) return;
        int at = _order.IndexOf(_ctx.CurrentFrame);
        _orderIndex = at < 0 ? 0 : at;
        _elapsedInFrame = 0;
        _clock.Restart();
        _timer.Start();
        _play.Text = "⏸";
        PlayingFrameChanged?.Invoke(this, ShownFrame);
        _view.Invalidate();
    }

    public void Stop()
    {
        if (!_timer.Enabled) return;
        _timer.Stop();
        _play.Text = "▶";
        PlayingFrameChanged?.Invoke(this, -1);
        _view.Invalidate();
    }

    private int FrameMs(int frame) => _fps.Value > 0 ? (int)Math.Round(1000 / (double)_fps.Value) : _ctx.FrameDuration(frame);

    private void Tick()
    {
        if (!_ctx.HasProject || _order.Count == 0) { Stop(); return; }
        _elapsedInFrame += _clock.Elapsed.TotalMilliseconds;
        _clock.Restart();
        bool changed = false;
        int guard = 0;
        while (_elapsedInFrame >= FrameMs(_order[_orderIndex]) && guard++ < 1000)
        {
            _elapsedInFrame -= FrameMs(_order[_orderIndex]);
            if (_orderIndex + 1 < _order.Count) _orderIndex++;
            else if (_ctx.PlaybackLoops) _orderIndex = 0;
            else { Stop(); return; }
            changed = true;
        }
        if (!changed) return;
        PlayingFrameChanged?.Invoke(this, ShownFrame);
        _view.Invalidate();
    }

    /// <summary>미리보기를 그리는 영역.</summary>
    private sealed class View : Control
    {
        private readonly PreviewPanel _owner;

        public View(PreviewPanel owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.InputColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var ctx = _owner._ctx;
            if (_owner._bgColor is { } bg)
            {
                using var b = new SolidBrush(bg);
                g.FillRectangle(b, ClientRectangle);
            }
            else Theme.DrawChecker(g, ClientRectangle, 8);
            if (!ctx.HasProject) return;
            var area = Rectangle.FromLTRB(4, 4, Width - 4, Height - 20);
            string caption;
            if (ctx.CurrentAtlasSprite is { } sprite)
            {
                if (ctx.DisplayBitmap is not { } sbmp) return;
                ImageUtil.DrawFitted(g, sbmp, new Rectangle(0, 0, sbmp.Width, sbmp.Height), area, _owner._scale);
                caption = $"{sprite.Name} · {sprite.Width}×{sprite.Height} · 스프라이트 {ctx.CurrentSprite + 1}/{ctx.SpriteCount}";
            }
            else
            {
                int frame = Math.Clamp(_owner.ShownFrame, 0, Math.Max(0, ctx.FrameCount - 1));
                if (ctx.FrameBitmap(frame) is not { } bmp) return;
                ImageUtil.DrawFitted(g, bmp, new Rectangle(0, 0, bmp.Width, bmp.Height), area, _owner._scale);
                var tag = ctx.ActiveTag;
                caption = $"{frame + 1}/{ctx.FrameCount} · {ctx.FrameDuration(frame)}ms" + (tag is null ? "" : $" · {tag.Name}");
            }
            var textColor = _owner._bgColor is { } c && c.GetBrightness() > 0.6f ? Color.Black : Color.White;
            TextRenderer.DrawText(g, caption, Theme.SmallFont, new Rectangle(4, Height - 18, Width - 8, 16), textColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
