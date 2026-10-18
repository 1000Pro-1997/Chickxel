namespace Gpixel.App;

/// <summary>모든 창이 함께 쓰는 어두운 평면 테마 색과 도우미.</summary>
public static class Theme
{
    public static readonly Color BackColor = Color.FromArgb(30, 30, 33);
    public static readonly Color PanelColor = Color.FromArgb(43, 43, 48);
    public static readonly Color InputColor = Color.FromArgb(55, 55, 61);
    public static readonly Color HoverColor = Color.FromArgb(64, 64, 72);
    public static readonly Color TextColor = Color.FromArgb(222, 222, 226);
    public static readonly Color DimTextColor = Color.FromArgb(140, 140, 148);
    public static readonly Color AccentColor = Color.FromArgb(86, 156, 214);
    public static readonly Color BorderColor = Color.FromArgb(70, 70, 78);
    public static readonly Color WarningColor = Color.FromArgb(230, 160, 70);
    public static readonly Color CheckerA = Color.FromArgb(68, 68, 72);
    public static readonly Color CheckerB = Color.FromArgb(92, 92, 98);

    public static readonly Font UiFont = new("Malgun Gothic", 9f);
    public static readonly Font SmallFont = new("Malgun Gothic", 8f);
    public static readonly Font BoldFont = new("Malgun Gothic", 9f, FontStyle.Bold);

    /// <summary>컨트롤과 그 자식들에 테마 색을 재귀적으로 적용한다.</summary>
    public static void Apply(Control root)
    {
        Style(root);
        foreach (Control c in root.Controls) Apply(c);
    }

    private static bool IsDefaultBack(Control c) => c.BackColor == SystemColors.Control || c.BackColor == SystemColors.Window;

    private static bool IsDefaultFore(Control c) => c.ForeColor == SystemColors.ControlText || c.ForeColor == SystemColors.WindowText;

    /// <summary>시스템 기본색을 그대로 가진 속성만 테마 색으로 바꾼다(직접 정한 색은 유지).</summary>
    private static void Style(Control c)
    {
        if (IsDefaultFore(c)) c.ForeColor = TextColor;
        switch (c)
        {
            case Button b:
                if (b.FlatStyle != FlatStyle.Flat)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = BorderColor;
                    b.FlatAppearance.MouseOverBackColor = HoverColor;
                }
                if (IsDefaultBack(b)) b.BackColor = InputColor;
                break;
            case ButtonBase bb when bb is CheckBox { Appearance: Appearance.Button } or RadioButton { Appearance: Appearance.Button }:
                bb.FlatStyle = FlatStyle.Flat;
                bb.FlatAppearance.BorderColor = BorderColor;
                bb.FlatAppearance.CheckedBackColor = AccentColor;
                bb.FlatAppearance.MouseOverBackColor = HoverColor;
                if (IsDefaultBack(bb)) bb.BackColor = InputColor;
                break;
            case TextBox or NumericUpDown or ComboBox or ListBox or ListView or TreeView:
                if (IsDefaultBack(c)) c.BackColor = InputColor;
                if (c is TextBox { BorderStyle: BorderStyle.Fixed3D } tb) tb.BorderStyle = BorderStyle.FixedSingle;
                if (c is NumericUpDown { BorderStyle: BorderStyle.Fixed3D } nud) nud.BorderStyle = BorderStyle.FixedSingle;
                if (c is ComboBox { FlatStyle: FlatStyle.Standard } combo) combo.FlatStyle = FlatStyle.Flat;
                if (c is ListBox { BorderStyle: BorderStyle.Fixed3D } lb) lb.BorderStyle = BorderStyle.None;
                if (c is ListView { BorderStyle: BorderStyle.Fixed3D } lv) lv.BorderStyle = BorderStyle.None;
                if (c is TreeView { BorderStyle: BorderStyle.Fixed3D } tv) { tv.BorderStyle = BorderStyle.None; tv.LineColor = BorderColor; }
                break;
            case ToolStrip ts:
                ts.Renderer = Renderer;
                ts.BackColor = PanelColor;
                ts.ForeColor = TextColor;
                break;
            case SplitContainer sc:
                sc.BackColor = BorderColor;
                if (IsDefaultBack(sc.Panel1)) sc.Panel1.BackColor = PanelColor;
                if (IsDefaultBack(sc.Panel2)) sc.Panel2.BackColor = PanelColor;
                break;
            case Form or Panel or TableLayoutPanel or FlowLayoutPanel or TabPage or UserControl or GroupBox:
                if (IsDefaultBack(c)) c.BackColor = PanelColor;
                break;
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Windows 10/11에서 창 제목 표시줄을 어둡게 한다(지원하지 않으면 무시).</summary>
    public static void UseDarkTitleBar(Form form)
    {
        int on = 1;
        if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
    }

    /// <summary>어두운 테마용 ToolStrip/MenuStrip 렌더러.</summary>
    public static readonly ToolStripRenderer Renderer = new DarkRenderer();

    private static readonly Dictionary<int, TextureBrush> CheckerBrushes = new();

    /// <summary>한 칸이 <paramref name="cell"/> 화면 픽셀인 체커 무늬 브러시(캐시됨, Dispose 금지).</summary>
    public static TextureBrush CheckerBrush(int cell)
    {
        cell = Math.Max(1, cell);
        if (CheckerBrushes.TryGetValue(cell, out var brush)) return brush;
        var bmp = new Bitmap(cell * 2, cell * 2);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(CheckerA);
            using var b = new SolidBrush(CheckerB);
            g.FillRectangle(b, cell, 0, cell, cell);
            g.FillRectangle(b, 0, cell, cell, cell);
        }
        brush = new TextureBrush(bmp, System.Drawing.Drawing2D.WrapMode.Tile);
        CheckerBrushes[cell] = brush;
        return brush;
    }

    /// <summary>투명 체커 배경을 그린다. cell은 화면 픽셀 단위, 무늬는 area 왼쪽 위에 맞춘다.</summary>
    public static void DrawChecker(Graphics g, Rectangle area, int cell)
    {
        if (area.Width <= 0 || area.Height <= 0) return;
        var brush = CheckerBrush(cell);
        brush.ResetTransform();
        brush.TranslateTransform(area.X, area.Y);
        g.FillRectangle(brush, area);
    }

    private sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColorTable()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? TextColor : DimTextColor;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? DimTextColor : TextColor;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var b = new SolidBrush(AccentColor);
            e.Graphics.FillRectangle(b, r);
            using var pen = new Pen(Color.White, 2f);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(pen, [
                new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.3f)]);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public DarkColorTable() { UseSystemColors = false; }
        public override Color MenuStripGradientBegin => PanelColor;
        public override Color MenuStripGradientEnd => PanelColor;
        public override Color ToolStripGradientBegin => PanelColor;
        public override Color ToolStripGradientMiddle => PanelColor;
        public override Color ToolStripGradientEnd => PanelColor;
        public override Color ToolStripBorder => BorderColor;
        public override Color ToolStripDropDownBackground => PanelColor;
        public override Color ImageMarginGradientBegin => PanelColor;
        public override Color ImageMarginGradientMiddle => PanelColor;
        public override Color ImageMarginGradientEnd => PanelColor;
        public override Color MenuBorder => BorderColor;
        public override Color MenuItemBorder => AccentColor;
        public override Color MenuItemSelected => HoverColor;
        public override Color MenuItemSelectedGradientBegin => HoverColor;
        public override Color MenuItemSelectedGradientEnd => HoverColor;
        public override Color MenuItemPressedGradientBegin => InputColor;
        public override Color MenuItemPressedGradientEnd => InputColor;
        public override Color ButtonSelectedHighlight => HoverColor;
        public override Color ButtonSelectedGradientBegin => HoverColor;
        public override Color ButtonSelectedGradientEnd => HoverColor;
        public override Color ButtonSelectedBorder => AccentColor;
        public override Color ButtonCheckedGradientBegin => AccentColor;
        public override Color ButtonCheckedGradientEnd => AccentColor;
        public override Color ButtonCheckedHighlight => AccentColor;
        public override Color ButtonPressedGradientBegin => AccentColor;
        public override Color ButtonPressedGradientEnd => AccentColor;
        public override Color SeparatorDark => BorderColor;
        public override Color SeparatorLight => BorderColor;
        public override Color StatusStripGradientBegin => PanelColor;
        public override Color StatusStripGradientEnd => PanelColor;
        public override Color CheckBackground => AccentColor;
        public override Color CheckSelectedBackground => AccentColor;
        public override Color CheckPressedBackground => AccentColor;
    }
}
