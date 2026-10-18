using System.Drawing.Drawing2D;
using System.Globalization;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// Aseprite식 왼쪽 열: 위에 기본색/보조색과 팔레트 메뉴, 가운데 팔레트 색 칸, 아래에 색 고르기
/// (채도·명도 상자 + 색상 막대 + 알파 막대, RGBA·HSV·HEX 입력, 최근 색 16개).
/// 팔레트는 추가·삭제·정렬, .hex 저장, .hex/.gpl 불러오기, 이미지에서 만들기를 지원한다.
/// </summary>
public sealed class ColorPanel : UserControl
{
    private readonly EditorContext _ctx;
    private readonly ColorSlot _primarySlot, _secondarySlot;
    private readonly SvBox _sv;
    private readonly HueBar _hue;
    private readonly AlphaBar _alpha;
    private readonly NumericUpDown _r, _g, _b, _a, _hNum, _sNum, _vNum;
    private readonly TextBox _hex;
    private readonly SwatchGrid _recent, _palette;
    private readonly Panel _paletteHost;
    private bool _editSecondary;
    private bool _updating;
    private float _h, _s, _v;

    /// <summary>팔레트 내용이 바뀜(설정 저장용).</summary>
    public event EventHandler? PaletteChanged;

    public ColorPanel(EditorContext ctx)
    {
        _ctx = ctx;
        BackColor = Theme.PanelColor;

        _primarySlot = new ColorSlot("기본") { Selected = true };
        _secondarySlot = new ColorSlot("보조");
        _primarySlot.Click += (_, _) => SetEditTarget(false);
        _secondarySlot.Click += (_, _) => SetEditTarget(true);
        var swap = Ui.Button("⇄", (_, _) => _ctx.SwapColors(), 26);
        new ToolTip().SetToolTip(swap, "기본색 ↔ 보조색 (X)");

        _sv = new SvBox { Size = new Size(170, 110), Margin = new Padding(3) };
        _hue = new HueBar { Size = new Size(16, 110), Margin = new Padding(3) };
        _alpha = new AlphaBar { Size = new Size(192, 12), Margin = new Padding(3) };
        _sv.ValueChanged += (_, _) => { _s = _sv.S; _v = _sv.V; ApplyHsv(); };
        _hue.ValueChanged += (_, _) => { _h = _hue.Hue; _sv.Hue = _h; ApplyHsv(); };
        _alpha.ValueChanged += (_, _) => { if (!_updating) _a!.Value = _alpha.Alpha; };

        _r = Ui.Num(0, 255, 0, 44);
        _g = Ui.Num(0, 255, 0, 44);
        _b = Ui.Num(0, 255, 0, 44);
        _a = Ui.Num(0, 255, 255, 44);
        _hNum = Ui.Num(0, 360, 0, 44);
        _sNum = Ui.Num(0, 100, 0, 44);
        _vNum = Ui.Num(0, 100, 0, 44);
        foreach (var n in new[] { _r, _g, _b, _a }) n.ValueChanged += (_, _) => ApplyRgba();
        foreach (var n in new[] { _hNum, _sNum, _vNum })
            n.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                _h = (float)_hNum.Value;
                _s = (float)_sNum.Value / 100f;
                _v = (float)_vNum.Value / 100f;
                ApplyHsv();
            };
        _hex = Ui.Text("", 80);
        _hex.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { ApplyHex(); e.SuppressKeyPress = true; } };
        _hex.Leave += (_, _) => ApplyHex();

        _recent = new SwatchGrid { Width = 196, Height = 30, Cell = 10 };
        _recent.ColorClicked += OnSwatchClicked;
        _palette = new SwatchGrid { Cell = 14, Selectable = true, Location = Point.Empty };
        _palette.ColorClicked += OnSwatchClicked;
        _palette.Colors.CollectionChangedByUser += (_, _) => { LayoutPalette(); PaletteChanged?.Invoke(this, EventArgs.Empty); };

        // ── 위: 팔레트 머리글(메뉴) + 기본/보조색
        var menuButton = Ui.Button("☰", null, 26);
        var menu = new ContextMenuStrip { Renderer = Theme.Renderer };
        menu.Items.AddRange([
            new ToolStripMenuItem("현재 색 추가", null, (_, _) => AddToPalette(CurrentColor)),
            new ToolStripMenuItem("고른 색 삭제", null, (_, _) => RemoveFromPalette()),
            new ToolStripMenuItem("색상 순으로 정렬", null, (_, _) => SortPalette()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("팔레트 불러오기(.hex/.gpl)…", null, (_, _) => LoadPalette()),
            new ToolStripMenuItem("팔레트 저장(.hex)…", null, (_, _) => SavePalette()),
            new ToolStripMenuItem("이미지에서 팔레트 만들기", null, (_, _) => PaletteFromImage()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("팔레트 비우기", null, (_, _) => { _palette.SetColors([]); _palette.Commit(); }),
        ]);
        foreach (ToolStripItem it in menu.Items) it.ForeColor = Theme.TextColor;
        menuButton.Click += (_, _) => menu.Show(menuButton, new Point(0, menuButton.Height));
        new ToolTip().SetToolTip(menuButton, "팔레트 메뉴");
        var header = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Theme.BackColor };
        var title = Ui.Label("팔레트", bold: true);
        title.Location = new Point(6, 4);
        title.Margin = Padding.Empty;
        menuButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        menuButton.Height = 22;
        header.Controls.Add(title);
        header.Controls.Add(menuButton);
        header.Resize += (_, _) => menuButton.Location = new Point(header.Width - menuButton.Width - 2, 2);
        var slots = Ui.Row(_primarySlot, _secondarySlot, swap);
        slots.Dock = DockStyle.Top;
        slots.AutoSize = false;
        slots.Height = 48;
        slots.Padding = new Padding(2, 2, 0, 0);

        // ── 가운데: 팔레트 색 칸(스크롤)
        _paletteHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.InputColor };
        _paletteHost.Controls.Add(_palette);
        _paletteHost.Resize += (_, _) => LayoutPalette();

        // ── 아래: 색 고르기
        var picker = Ui.Stack(
            Ui.Row(_sv, _hue),
            _alpha,
            Ui.Row(Ui.Label("R"), _r, Ui.Label("G"), _g, Ui.Label("B"), _b),
            Ui.Row(Ui.Label("H"), _hNum, Ui.Label("S"), _sNum, Ui.Label("V"), _vNum),
            Ui.Row(Ui.Label("A"), _a, Ui.Label("#"), _hex),
            Ui.Label("최근 색"),
            _recent);
        var pickerHost = new Panel { Dock = DockStyle.Bottom, Height = picker.PreferredSize.Height + 26, BackColor = Theme.PanelColor };
        picker.Location = new Point(0, 24);
        pickerHost.Controls.Add(picker);
        pickerHost.Controls.Add(Ui.Header("색"));

        Controls.Add(_paletteHost);
        Controls.Add(pickerHost);
        Controls.Add(slots);
        Controls.Add(header);

        ctx.ColorsChanged += (_, _) => SyncFromContext();
        ctx.RecentColorsChanged += (_, _) => { _recent.SetColors(_ctx.RecentColors); };
        SyncFromContext();
    }

    /// <summary>팔레트 칸 격자를 스크롤 영역 너비에 맞추고 높이를 행 수에 맞춘다.</summary>
    private void LayoutPalette()
    {
        int w = Math.Max(40, _paletteHost.ClientSize.Width);
        _palette.Width = w;
        _palette.Height = Math.Max(_paletteHost.ClientSize.Height, _palette.RequiredHeight(w));
        _palette.Invalidate();
    }

    private Color32 CurrentColor => _editSecondary ? _ctx.Secondary : _ctx.Primary;

    private void SetCurrent(Color32 c)
    {
        if (_editSecondary) _ctx.Secondary = c;
        else _ctx.Primary = c;
    }

    private void SetEditTarget(bool secondary)
    {
        _editSecondary = secondary;
        _primarySlot.Selected = !secondary;
        _secondarySlot.Selected = secondary;
        SyncFromContext();
    }

    private void SyncFromContext()
    {
        if (_updating) return;
        _primarySlot.Color = _ctx.Primary;
        _secondarySlot.Color = _ctx.Secondary;
        var c = CurrentColor;
        var (h, s, v) = c.ToHsv();
        // 무채색이면 색상(H)을 유지해 상자가 튀지 않게 한다.
        if (s > 0 && v > 0) _h = h;
        if (v > 0) _s = s;
        _v = v;
        UpdateEditors(c, fromHsv: false);
    }

    private void UpdateEditors(Color32 c, bool fromHsv)
    {
        _updating = true;
        _r.Value = c.R;
        _g.Value = c.G;
        _b.Value = c.B;
        _a.Value = c.A;
        _alpha.Alpha = c.A;
        _alpha.BaseColor = c;
        _hNum.Value = (decimal)Math.Clamp(MathF.Round(_h), 0, 360);
        _sNum.Value = (decimal)Math.Clamp(MathF.Round(_s * 100), 0, 100);
        _vNum.Value = (decimal)Math.Clamp(MathF.Round(_v * 100), 0, 100);
        if (!_hex.Focused) _hex.Text = c.ToHex();
        _hue.Hue = _h;
        _sv.Hue = _h;
        if (!fromHsv || !_sv.Dragging) _sv.SetSv(_s, _v);
        _updating = false;
    }

    private void ApplyHsv()
    {
        if (_updating) return;
        var c = Color32.FromHsv(_h, _s, _v, (byte)_a.Value);
        _updating = true;
        SetCurrent(c);
        _updating = false;
        _primarySlot.Color = _ctx.Primary;
        _secondarySlot.Color = _ctx.Secondary;
        UpdateEditors(c, fromHsv: true);
    }

    private void ApplyRgba()
    {
        if (_updating) return;
        SetCurrent(new Color32((byte)_r.Value, (byte)_g.Value, (byte)_b.Value, (byte)_a.Value));
    }

    private void ApplyHex()
    {
        if (Color32.TryParseHex(_hex.Text.Trim(), out var c)) SetCurrent(c);
        else _hex.Text = CurrentColor.ToHex();
    }

    private void OnSwatchClicked(object? sender, (Color32 Color, MouseButtons Button) e)
    {
        if (e.Button == MouseButtons.Right) _ctx.Secondary = e.Color;
        else _ctx.Primary = e.Color;
    }

    // ───────────── 팔레트 ─────────────

    public IReadOnlyList<Color32> Palette => _palette.Colors;

    public void SetPalette(IEnumerable<Color32> colors)
    {
        _palette.SetColors(colors);
        LayoutPalette();
    }

    public void SetRecent(IEnumerable<Color32> colors) => _recent.SetColors(colors);

    private void AddToPalette(Color32 c)
    {
        if (_palette.Colors.Contains(c)) return;
        _palette.Colors.Add(c);
        _palette.Commit();
    }

    private void RemoveFromPalette()
    {
        if (_palette.SelectedIndex < 0 || _palette.SelectedIndex >= _palette.Colors.Count) return;
        _palette.Colors.RemoveAt(_palette.SelectedIndex);
        _palette.SelectedIndex = Math.Min(_palette.SelectedIndex, _palette.Colors.Count - 1);
        _palette.Commit();
    }

    private void SortPalette()
    {
        var sorted = _palette.Colors
            .OrderBy(c => c.ToHsv().S < 0.08f ? 1 : 0)
            .ThenBy(c => c.ToHsv().S < 0.08f ? 0 : MathF.Round(c.ToHsv().H / 15f))
            .ThenBy(c => c.ToHsv().V)
            .ToList();
        _palette.Colors.Clear();
        _palette.Colors.AddRange(sorted);
        _palette.Commit();
    }

    private void LoadPalette()
    {
        using var dlg = new OpenFileDialog { Filter = "팔레트 (*.hex;*.gpl)|*.hex;*.gpl|모든 파일|*.*", Title = "팔레트 불러오기" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var colors = PaletteIO.Load(dlg.FileName);
            if (colors.Count == 0)
            {
                Ui.Error(FindForm(), "파일에서 색을 찾지 못했습니다.");
                return;
            }
            _palette.SetColors(colors);
            _palette.Commit();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(FindForm(), $"팔레트를 읽지 못했습니다.\n{ex.Message}");
        }
    }

    private void SavePalette()
    {
        using var dlg = new SaveFileDialog { Filter = "HEX 팔레트 (*.hex)|*.hex", Title = "팔레트 저장", FileName = "palette.hex" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            PaletteIO.SaveHex(dlg.FileName, _palette.Colors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(FindForm(), $"팔레트를 저장하지 못했습니다.\n{ex.Message}");
        }
    }

    /// <summary>프로젝트의 모든 프레임(아틀라스면 모든 스프라이트, 보이는 레이어 합성)에 쓰인 색으로 팔레트를 만든다.</summary>
    public void PaletteFromImage()
    {
        if (_ctx.Project is not { } p) return;
        var colors = new List<Color32>();
        var seen = new HashSet<Color32>();
        int count = p.IsAtlas ? p.Sprites.Count : p.Frames.Count;
        for (int i = 0; i < count && colors.Count < 256; i++)
            foreach (var c in PaletteIO.ExtractFromImage(p.IsAtlas ? p.FlattenSprite(i) : p.Flatten(i)))
                if (seen.Add(c) && colors.Count < 256) colors.Add(c);
        if (colors.Count == 0)
        {
            Ui.Info(FindForm(), "이미지에 투명이 아닌 색이 없습니다.");
            return;
        }
        if (_palette.Colors.Count > 0 && MessageBox.Show(FindForm(), $"이미지에서 {colors.Count}색을 찾았습니다. 지금 팔레트를 바꿀까요?",
                "이미지에서 팔레트 만들기", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _palette.SetColors(colors);
        _palette.Commit();
    }

    // ───────────── 내부 컨트롤 ─────────────

    /// <summary>기본/보조 색 칸.</summary>
    private sealed class ColorSlot : Control
    {
        private Color32 _color;
        private bool _selected;

        public ColorSlot(string caption)
        {
            Text = caption;
            Size = new Size(84, 40);
            Margin = new Padding(3);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        public Color32 Color { get => _color; set { _color = value; Invalidate(); } }
        public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.PanelColor);
            var sw = new Rectangle(2, 2, 34, Height - 4);
            ImageUtil.DrawSwatch(g, sw, _color);
            using (var pen = new Pen(_selected ? Theme.AccentColor : Theme.BorderColor, _selected ? 2 : 1))
                g.DrawRectangle(pen, sw);
            TextRenderer.DrawText(g, Text, Theme.SmallFont, new Rectangle(38, 2, Width - 38, Height / 2), Theme.TextColor, TextFormatFlags.Left);
            TextRenderer.DrawText(g, _color.ToHex(), Theme.SmallFont, new Rectangle(38, Height / 2, Width - 38, Height / 2), Theme.DimTextColor, TextFormatFlags.Left);
        }
    }

    /// <summary>채도(가로)·명도(세로) 상자.</summary>
    private sealed class SvBox : Control
    {
        private float _hue, _s, _v;
        private Bitmap? _cache;
        private float _cacheHue = -1;

        public SvBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Cross;
        }

        public event EventHandler? ValueChanged;
        public float S => _s;
        public float V => _v;
        public bool Dragging { get; private set; }

        public float Hue
        {
            get => _hue;
            set { if (Math.Abs(_hue - value) < 0.01f) return; _hue = value; Invalidate(); }
        }

        public void SetSv(float s, float v)
        {
            _s = s;
            _v = v;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_cache is null || _cache.Size != ClientSize || Math.Abs(_cacheHue - _hue) > 0.01f) RebuildCache();
            e.Graphics.DrawImageUnscaled(_cache!, 0, 0);
            float x = _s * (Width - 1), y = (1 - _v) * (Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outer = new Pen(Color.Black, 1.5f);
            using var inner = new Pen(Color.White, 1.5f);
            e.Graphics.DrawEllipse(outer, x - 5, y - 5, 10, 10);
            e.Graphics.DrawEllipse(inner, x - 4, y - 4, 8, 8);
        }

        private void RebuildCache()
        {
            _cache?.Dispose();
            _cache = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            _cacheHue = _hue;
            using var g = Graphics.FromImage(_cache);
            var rect = new Rectangle(0, 0, _cache.Width, _cache.Height);
            using (var b = new SolidBrush(Color32.FromHsv(_hue, 1, 1).ToColor())) g.FillRectangle(b, rect);
            using (var white = new LinearGradientBrush(rect, Color.White, Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Horizontal))
                g.FillRectangle(white, rect);
            using (var black = new LinearGradientBrush(rect, Color.FromArgb(0, 0, 0, 0), Color.Black, LinearGradientMode.Vertical))
                g.FillRectangle(black, rect);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Dragging = true;
            Capture = true;
            Pick(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Dragging) Pick(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Dragging = false;
            Capture = false;
        }

        private void Pick(Point p)
        {
            _s = Math.Clamp(p.X / (float)Math.Max(1, Width - 1), 0, 1);
            _v = 1 - Math.Clamp(p.Y / (float)Math.Max(1, Height - 1), 0, 1);
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cache?.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>가로 알파 막대(체커 위에 현재 색의 투명→불투명).</summary>
    private sealed class AlphaBar : Control
    {
        private byte _alpha = 255;
        private Color32 _base = Color32.Black;
        private bool _drag;

        public AlphaBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public event EventHandler? ValueChanged;

        public byte Alpha { get => _alpha; set { if (_alpha == value) return; _alpha = value; Invalidate(); } }
        public Color32 BaseColor { get => _base; set { _base = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var rect = new Rectangle(0, 0, Width, Height);
            Theme.DrawChecker(e.Graphics, rect, 4);
            using (var brush = new LinearGradientBrush(rect, _base.WithAlpha(0).ToColor(), _base.WithAlpha(255).ToColor(), LinearGradientMode.Horizontal))
                e.Graphics.FillRectangle(brush, rect);
            float x = _alpha / 255f * (Width - 1);
            using var pen = new Pen(Color.White, 2);
            e.Graphics.DrawRectangle(pen, x - 2, 1, 4, Height - 3);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _drag = true;
            Capture = true;
            Pick(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) Pick(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = false;
            Capture = false;
        }

        private void Pick(int x)
        {
            _alpha = (byte)Math.Round(Math.Clamp(x / (float)Math.Max(1, Width - 1), 0, 1) * 255);
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>세로 색상(H) 막대.</summary>
    private sealed class HueBar : Control
    {
        private float _hue;
        private bool _drag;

        public HueBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public event EventHandler? ValueChanged;

        public float Hue
        {
            get => _hue;
            set { if (Math.Abs(_hue - value) < 0.01f) return; _hue = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var rect = new Rectangle(0, 0, Width, Height);
            using (var brush = new LinearGradientBrush(rect, Color.Red, Color.Red, LinearGradientMode.Vertical))
            {
                var blend = new ColorBlend
                {
                    Colors = Enumerable.Range(0, 7).Select(i => Color32.FromHsv(i * 60 % 360, 1, 1).ToColor()).ToArray(),
                    Positions = [0f, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1f],
                };
                blend.Colors[6] = Color.Red;
                brush.InterpolationColors = blend;
                e.Graphics.FillRectangle(brush, rect);
            }
            float y = _hue / 360f * (Height - 1);
            using var pen = new Pen(Color.White, 2);
            e.Graphics.DrawRectangle(pen, 1, y - 2, Width - 3, 4);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _drag = true;
            Capture = true;
            Pick(e.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) Pick(e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = false;
            Capture = false;
        }

        private void Pick(int y)
        {
            _hue = Math.Clamp(y / (float)Math.Max(1, Height - 1), 0, 1) * 360f;
            if (_hue >= 360f) _hue = 359.9f;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>색 목록(사용자가 바꾸면 이벤트를 내는 List).</summary>
public sealed class ColorList : List<Color32>
{
    public event EventHandler? CollectionChangedByUser;
    public void RaiseChanged() => CollectionChangedByUser?.Invoke(this, EventArgs.Empty);
}

/// <summary>색 칸 격자(최근 색·팔레트). 왼쪽 클릭 = 기본색, 오른쪽 = 보조색.</summary>
public sealed class SwatchGrid : Control
{
    private readonly ToolTip _tip = new();
    private int _hoverIndex = -1;

    public SwatchGrid()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Margin = new Padding(3);
    }

    public ColorList Colors { get; } = new();
    public int Cell { get; set; } = 16;
    public bool Selectable { get; set; }
    public int SelectedIndex { get; set; } = -1;

    public event EventHandler<(Color32 Color, MouseButtons Button)>? ColorClicked;

    public void SetColors(IEnumerable<Color32> colors)
    {
        Colors.Clear();
        Colors.AddRange(colors);
        SelectedIndex = -1;
        Invalidate();
    }

    /// <summary>사용자 변경을 알리고 다시 그린다.</summary>
    public void Commit()
    {
        Colors.RaiseChanged();
        Invalidate();
    }

    private int Columns => Math.Max(1, (Width - 2) / (Cell + 2));

    /// <summary>주어진 너비에서 모든 칸을 보이는 데 필요한 높이.</summary>
    public int RequiredHeight(int width)
    {
        int cols = Math.Max(1, (width - 2) / (Cell + 2));
        return 2 + (Colors.Count + cols - 1) / cols * (Cell + 2);
    }

    private Rectangle CellRect(int i) => new(1 + i % Columns * (Cell + 2), 1 + i / Columns * (Cell + 2), Cell, Cell);

    private int IndexAt(Point p)
    {
        for (int i = 0; i < Colors.Count; i++)
            if (Rectangle.Inflate(CellRect(i), 1, 1).Contains(p)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.InputColor);
        if (Colors.Count == 0)
        {
            TextRenderer.DrawText(g, "비어 있음", Theme.SmallFont, ClientRectangle, Theme.DimTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        for (int i = 0; i < Colors.Count; i++)
        {
            var r = CellRect(i);
            if (r.Top > Height) break;
            ImageUtil.DrawSwatch(g, r, Colors[i]);
            if (Selectable && i == SelectedIndex)
            {
                using var pen = new Pen(Color.White, 2);
                g.DrawRectangle(pen, Rectangle.Inflate(r, 1, 1));
            }
            else if (i == _hoverIndex)
            {
                using var pen = new Pen(Theme.AccentColor);
                g.DrawRectangle(pen, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Location);
        if (i == _hoverIndex) return;
        _hoverIndex = i;
        _tip.SetToolTip(this, i >= 0 ? Colors[i].ToHex() : null);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = IndexAt(e.Location);
        if (i < 0) return;
        if (Selectable) SelectedIndex = i;
        Invalidate();
        ColorClicked?.Invoke(this, (Colors[i], e.Button));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

internal static class ColorParse
{
    public static Color32 ParseOr(string? hex, Color32 fallback)
        => Color32.TryParseHex(hex, out var c) ? c : fallback;

    public static string Format(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
