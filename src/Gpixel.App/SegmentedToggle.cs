namespace Gpixel.App;

/// <summary>
/// 작은 선택(2~4개)을 한 줄로 붙인 둥근 사각형 단추 묶음(칸 사이 구분선). 고른 칸은 강조색 바탕 + 흰 글자, 나머지는 테두리만.
/// 클릭 또는 키보드(Tab으로 포커스, ←/→·Home/End)로 고른다. 칸마다 도움말(<see cref="SetOptionTip"/>), 묶음 전체 도움말은 <see cref="Ui.Tip{T}"/>.
/// <see cref="SelectedIndex"/>가 바뀌면(코드로 바꿔도) <see cref="SelectionChanged"/> — 콤보 상자와 같다.
/// </summary>
public sealed class SegmentedToggle : Border
{
    private const double SegmentHeight = 22;
    /// <summary>바깥 모서리 반지름(앱의 다른 단추와 같은 둥근 사각형).</summary>
    private const double Radius = 4;
    private static readonly IBrush Clear = Brushes.Transparent;

    private readonly Border[] _segments;
    private readonly TextBlock[] _texts;
    private int _selected = -1;
    private int _hover = -1;
    private bool _focusRing;

    /// <param name="options">칸 글자(2~4개).</param>
    public SegmentedToggle(params string[] options)
    {
        if (options.Length is < 2 or > 4) throw new ArgumentException("선택지는 2~4개여야 합니다.", nameof(options));
        const double r = Radius;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", options.Select(_ => "Auto"))) };
        _segments = new Border[options.Length];
        _texts = new TextBlock[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var text = new TextBlock
            {
                Text = options[i],
                FontSize = AppTheme.FontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            bool first = i == 0, last = i == options.Length - 1;
            var segment = new Border
            {
                Child = text,
                MinWidth = 40,
                Height = SegmentHeight - 2,
                Padding = new Thickness(10, 0),
                Background = Clear,
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(first ? 0 : 1, 0, 0, 0),
                // 안쪽 칸 모서리 = 바깥 테두리 안쪽 반지름(바탕색이 둥근 모서리 밖으로 나가지 않게)
                CornerRadius = new CornerRadius(first ? r - 1 : 0, last ? r - 1 : 0, last ? r - 1 : 0, first ? r - 1 : 0),
            };
            segment.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(segment).Properties.IsLeftButtonPressed) return;
                Focus();
                SelectedIndex = index;
                e.Handled = true;
            };
            segment.PointerEntered += (_, _) => { _hover = index; UpdateVisuals(); };
            segment.PointerExited += (_, _) => { if (_hover == index) _hover = -1; UpdateVisuals(); };
            Grid.SetColumn(segment, i);
            grid.Children.Add(segment);
            _segments[i] = segment;
            _texts[i] = text;
        }
        Child = grid;
        CornerRadius = new CornerRadius(r);
        BorderThickness = new Thickness(1);
        Background = Clear;
        Margin = new Thickness(3);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = true;
        IsTabStop = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        UpdateVisuals();
    }

    /// <summary>선택지 수.</summary>
    public int Count => _segments.Length;

    /// <summary>고른 칸 번호(−1 = 아무것도 고르지 않음). 바뀌면 <see cref="SelectionChanged"/>.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int v = Math.Clamp(value, -1, _segments.Length - 1);
            if (v == _selected) return;
            _selected = v;
            UpdateVisuals();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>고른 칸이 바뀜(사용자 조작이든 코드든).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>칸 하나의 도움말 풍선(null = 묶음 전체 도움말을 씀).</summary>
    public SegmentedToggle SetOptionTip(int index, string? tip)
    {
        _segments[index].Tip(tip);
        return this;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None) return;
        int start = _selected < 0 ? 0 : _selected;
        int? next = e.Key switch
        {
            Key.Left => start - 1,
            Key.Right => _selected < 0 ? 0 : start + 1,
            Key.Home => 0,
            Key.End => _segments.Length - 1,
            _ => null,
        };
        if (next is not { } n) return;
        SelectedIndex = Math.Clamp(n, 0, _segments.Length - 1);
        e.Handled = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        // 키보드로 들어왔을 때만 포커스 테두리(클릭할 때마다 테두리가 번쩍이지 않게)
        _focusRing = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        UpdateVisuals();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _focusRing = false;
        UpdateVisuals();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        bool enabled = IsEffectivelyEnabled;
        BorderBrush = _focusRing && enabled ? AppTheme.AccentBrush : AppTheme.BorderBrush;
        for (int i = 0; i < _segments.Length; i++)
        {
            bool selected = i == _selected;
            // 끈 상태는 흐린 색으로(줄째 흐리게 하는 Opacity와 겹쳐 너무 어두워지지 않게 투명도는 쓰지 않음)
            _segments[i].Background = selected ? (enabled ? AppTheme.AccentBrush : AppTheme.HoverBrush)
                : enabled && i == _hover ? AppTheme.HoverBrush : Clear;
            _texts[i].Foreground = !enabled ? AppTheme.DimTextBrush : selected ? Brushes.White : AppTheme.TextBrush;
            _texts[i].FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }
}
