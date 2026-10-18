namespace Gpixel.App;

/// <summary>코드로 UI를 만들 때 쓰는 작은 도우미들.</summary>
public static class Ui
{
    public static Label Label(string text, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        Font = bold ? Theme.BoldFont : Theme.UiFont,
        ForeColor = Theme.TextColor,
        Margin = new Padding(3, 6, 3, 3),
    };

    public static Label Header(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 24,
        Font = Theme.BoldFont,
        ForeColor = Theme.TextColor,
        BackColor = Theme.BackColor,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(6, 0, 0, 0),
    };

    public static NumericUpDown Num(decimal min, decimal max, decimal value, int width = 60, int decimals = 0, decimal increment = 1)
        => new()
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = width,
            DecimalPlaces = decimals,
            Increment = increment,
            BackColor = Theme.InputColor,
            ForeColor = Theme.TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(3, 3, 3, 3),
        };

    public static Button Button(string text, EventHandler? onClick = null, int width = 0)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = width == 0,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 26,
            Padding = new Padding(4, 0, 4, 0),
            Margin = new Padding(2),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.InputColor,
            ForeColor = Theme.TextColor,
            UseVisualStyleBackColor = false,
        };
        if (width > 0) b.Width = width;
        b.FlatAppearance.BorderColor = Theme.BorderColor;
        b.FlatAppearance.MouseOverBackColor = Theme.HoverColor;
        if (onClick is not null) b.Click += onClick;
        return b;
    }

    public static CheckBox Check(string text, bool value = false) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        ForeColor = Theme.TextColor,
        Margin = new Padding(3, 5, 3, 3),
    };

    public static TextBox Text(string value = "", int width = 120) => new()
    {
        Text = value,
        Width = width,
        BackColor = Theme.InputColor,
        ForeColor = Theme.TextColor,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(3),
    };

    public static ComboBox Combo(int width, params string[] items)
    {
        var c = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = width,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.InputColor,
            ForeColor = Theme.TextColor,
            Margin = new Padding(3),
        };
        c.Items.AddRange(items);
        if (items.Length > 0) c.SelectedIndex = 0;
        return c;
    }

    /// <summary>자식들을 한 줄로 늘어놓는 흐름 패널.</summary>
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent,
        };
        f.Controls.AddRange(controls);
        return f;
    }

    /// <summary>자식들을 위에서 아래로 쌓고 너비를 부모에 맞추는 패널(스크롤 패널 안에서 사용).</summary>
    public static FlowLayoutPanel Stack(params Control[] controls)
    {
        var f = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0),
            Padding = new Padding(4),
            BackColor = Color.Transparent,
        };
        f.Controls.AddRange(controls);
        return f;
    }

    /// <summary>제목 줄이 있는 묶음 상자.</summary>
    public static GroupBox Group(string title, Control content)
    {
        var g = new GroupBox
        {
            Text = title,
            ForeColor = Theme.TextColor,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4, 2, 4, 4),
            Margin = new Padding(2, 4, 2, 2),
        };
        content.Location = new Point(6, 20);
        g.Controls.Add(content);
        return g;
    }

    /// <summary>내용을 세로로 스크롤해 보여 주는 채움 패널.</summary>
    public static Panel ScrollHost(Control content)
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.PanelColor };
        content.Location = Point.Empty;
        p.Controls.Add(content);
        return p;
    }

    /// <summary>오류 대화상자.</summary>
    public static void Error(IWin32Window? owner, string message, string title = "오류")
        => MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Info(IWin32Window? owner, string message, string title = "Gpixel")
        => MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
}

/// <summary>어두운 테마의 확인/취소 대화상자 바탕.</summary>
public class DialogBase : Form
{
    protected readonly FlowLayoutPanel Body;
    protected readonly Button OkButton;
    protected readonly Button CancelButtonControl;
    private readonly FlowLayoutPanel _buttons;

    public DialogBase(string title, string okText = "확인")
    {
        Text = title;
        Font = Theme.UiFont;
        BackColor = Theme.PanelColor;
        ForeColor = Theme.TextColor;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);

        Body = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.PanelColor,
            Margin = new Padding(0),
        };
        OkButton = Ui.Button(okText, width: 90);
        OkButton.DialogResult = DialogResult.OK;
        CancelButtonControl = Ui.Button("취소", width: 90);
        CancelButtonControl.DialogResult = DialogResult.Cancel;
        _buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 10, 0, 0),
            BackColor = Theme.PanelColor,
        };
        _buttons.Controls.Add(CancelButtonControl);
        _buttons.Controls.Add(OkButton);
        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.PanelColor,
            Location = new Point(10, 10),
        };
        root.Controls.Add(Body, 0, 0);
        root.Controls.Add(_buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = OkButton;
        CancelButton = CancelButtonControl;
    }

    /// <summary>확인 버튼 없이 닫기 버튼만 둔다.</summary>
    protected void CloseOnly()
    {
        OkButton.Visible = false;
        CancelButtonControl.Text = "닫기";
        AcceptButton = CancelButtonControl;
    }

    /// <summary>확인·취소 사이에 결과 단추를 하나 더 둔다(누르면 그 결과로 닫힘).</summary>
    protected Button AddButton(string text, DialogResult result)
    {
        var b = Ui.Button(text, width: 90);
        b.DialogResult = result;
        _buttons.Controls.Add(b);
        _buttons.Controls.SetChildIndex(b, 1);
        return b;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkTitleBar(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.Apply(this);
    }
}
