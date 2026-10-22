using Path = Avalonia.Controls.Shapes.Path;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>
/// 왼쪽 도구 막대의 한 칸. 비슷한 도구 묶음(예: 사각·원·자유·스마트 선택)이면 마지막으로 고른 도구를 보이고
/// 오른쪽 아래에 작은 삼각형이 붙는다. 길게 누르거나(<see cref="LongPressMs"/>) 오른쪽 클릭하면 옆에 묶음 도구 목록
/// (아이콘·이름·단축키)이 펼쳐지고, 고른 도구가 이 칸으로 바뀐다. 그냥 누르면 칸의 지금 도구를 고른다.
/// </summary>
public sealed class ToolSlotButton : ToggleButton
{
    /// <summary>묶음 목록을 펼치는 길게 누르기 시간(ms).</summary>
    public const int LongPressMs = 400;

    private static readonly Geometry CornerMark = Geometry.Parse("M5,0 L5,5 L0,5 Z");

    private readonly IReadOnlyList<(ToolKind Tool, string Name)> _tools;
    /// <summary>도구 단축키 표기(사용자 설정 단축키, 없으면 빈 문자열).</summary>
    private readonly Func<ToolKind, string> _keyText;
    private readonly Image _icon;
    private readonly DispatcherTimer _hold = new() { Interval = TimeSpan.FromMilliseconds(LongPressMs) };
    /// <summary>길게 눌러 목록을 펼쳤으면 이번 누름의 클릭(도구 고르기)은 버린다.</summary>
    private bool _suppressClick;

    /// <param name="tools">칸의 도구(이름). 첫 도구가 처음 보이는 도구.</param>
    /// <param name="keyText">도구 단축키 표기(바뀌면 <see cref="RefreshTip"/>).</param>
    public ToolSlotButton(IReadOnlyList<(ToolKind Tool, string Name)> tools, Func<ToolKind, string> keyText)
    {
        if (tools.Count == 0) throw new ArgumentException(UiStrings.S_ToolSlotButton_ToolSlotButton_3CFF705466, nameof(tools));
        _tools = tools;
        _keyText = keyText;
        Current = tools[0].Tool;
        _icon = MainForm.IconImage(Icons.For(Current), Icons.Size);
        var content = new Grid { Children = { _icon } };
        if (IsGroup)
            content.Children.Add(new Path
            {
                Data = CornerMark,
                Fill = AppTheme.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -1, -1),
                IsHitTestVisible = false,
            });
        Content = content;
        Padding = new Thickness(2);
        Margin = new Thickness(1);
        MinWidth = 0;
        MinHeight = 0;
        HorizontalAlignment = HorizontalAlignment.Center;
        Focusable = false;
        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            _suppressClick = true;
            ShowGroup();
        };
        RefreshTip();
    }

    protected override Type StyleKeyOverride => typeof(ToggleButton);

    /// <summary>칸에 지금 보이는(누르면 고를) 도구.</summary>
    public ToolKind Current { get; private set; }

    /// <summary>도구가 둘 이상인 묶음 칸인지.</summary>
    public bool IsGroup => _tools.Count > 1;

    /// <summary>그냥 클릭(칸의 지금 도구) 또는 펼친 목록에서 도구를 골랐다.</summary>
    public event Action<ToolKind>? ToolPicked;

    public bool Contains(ToolKind tool)
    {
        foreach (var t in _tools) if (t.Tool == tool) return true;
        return false;
    }

    /// <summary>칸 안에서 <paramref name="tool"/> 다음 도구(끝이면 처음으로).</summary>
    public ToolKind Next(ToolKind tool)
    {
        for (int i = 0; i < _tools.Count; i++)
            if (_tools[i].Tool == tool) return _tools[(i + 1) % _tools.Count].Tool;
        return Current;
    }

    /// <summary>칸에 보일 도구를 바꾼다(칸에 없는 도구면 무시).</summary>
    public void SetCurrent(ToolKind tool)
    {
        if (tool == Current || !Contains(tool)) return;
        Current = tool;
        _icon.Source = Icons.For(tool);
        RefreshTip();
    }

    /// <summary>도움말 풍선을 다시 만든다(단축키를 바꿨을 때).</summary>
    public void RefreshTip()
    {
        string tip = Label(Current);
        if (IsGroup)
            tip += string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ToolSlotButton_RefreshTip_0CDC077422,string.Join(" · ", _tools.Select(t => Label(t.Tool))));
        this.Tip(tip);
    }

    private string Label(ToolKind tool)
    {
        var key = _keyText(tool);
        var name = _tools.First(t => t.Tool == tool).Name;
        return key.Length > 0 ? $"{name} ({key})" : name;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _suppressClick = false;
        _hold.Stop();
        base.OnPointerPressed(e);
        if (IsGroup && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) _hold.Start();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _hold.Stop();
        if (IsGroup && e.InitialPressMouseButton == MouseButton.Right)
        {
            e.Handled = true;
            ShowGroup();
            return;
        }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _hold.Stop();
        base.OnPointerCaptureLost(e);
    }

    protected override void OnClick()
    {
        if (_suppressClick)
        {
            _suppressClick = false;
            return;
        }
        base.OnClick();
        ToolPicked?.Invoke(Current);
    }

    /// <summary>칸 오른쪽에 묶음 도구 목록을 펼친다.</summary>
    private void ShowGroup()
    {
        var list = new StackPanel { Spacing = 1 };
        var flyout = new Flyout { Placement = PlacementMode.RightEdgeAlignedTop, Content = list };
        foreach (var (tool, name) in _tools)
        {
            bool current = tool == Current;
            var icon = MainForm.IconImage(Icons.For(tool), Icons.Size);
            icon.Margin = new Thickness(0, 0, 8, 0);
            var label = new TextBlock
            {
                Text = name,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = current ? FontWeight.Bold : FontWeight.Normal,
            };
            var shortcut = new TextBlock
            {
                Text = Ui.Shortcut(_keyText(tool)),
                Foreground = AppTheme.DimTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0),
            };
            Grid.SetColumn(label, 1);
            Grid.SetColumn(shortcut, 2);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { icon, label, shortcut } };
            var item = new Button
            {
                Content = row,
                Padding = new Thickness(4, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = current ? AppTheme.HoverBrush : Brushes.Transparent,
                BorderThickness = new Thickness(current ? 1 : 0),
                BorderBrush = AppTheme.AccentBrush,
                Focusable = false,
            };
            item.Click += (_, _) =>
            {
                flyout.Hide();
                SetCurrent(tool);
                ToolPicked?.Invoke(tool);
            };
            list.Children.Add(item);
        }
        PopupMenus.Show(this, flyout);
    }
}
