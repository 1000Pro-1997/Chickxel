namespace Chickxel.App;

/// <summary>
/// 선택된 보기 이름을 보이는 분할 단추. 본 단추는 다음 상태로 넘기고, 화살표·우클릭은 현재 상태를 표시한 라디오 메뉴를 연다.
/// 보기 값을 가진 쪽이 <see cref="Refresh"/>로 외부 변경을 동기화한다.
/// </summary>
public sealed class ViewModeButton : StackPanel
{
    private readonly IReadOnlyList<string> _options;
    private readonly Func<int> _selected;
    private readonly Action<int> _select;
    private readonly Func<int, string>? _label;
    private readonly string _tip;
    private readonly IReadOnlyList<string>? _descriptions;
    private readonly Button _button;

    public ViewModeButton(IReadOnlyList<string> options, Func<int> selected, Action<int> select, string tip,
        Func<int, string>? label = null, IReadOnlyList<string>? descriptions = null)
    {
        if (options.Count == 0) throw new ArgumentException("At least one view mode is required.", nameof(options));
        if (descriptions is not null && descriptions.Count != options.Count)
            throw new ArgumentException("Descriptions must match the view modes.", nameof(descriptions));
        _options = options;
        _selected = selected;
        _select = select;
        _tip = tip;
        _descriptions = descriptions;
        _label = label;
        _button = new Button
        {
            Padding = new Thickness(6, 2, 4, 2),
            Margin = new Thickness(1),
            MinHeight = 0,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
        }.Tip(tip);
        _button.Click += (_, _) => Choose((_selected() + 1) % _options.Count);
        _button.ContextRequested += (_, e) => PopupMenus.Show(_button, MenuItems(), e);
        Children.Add(Ui.WithMenuArrow(_button, tip,
            anchor => PopupMenus.Show(anchor, MenuItems(), PlacementMode.BottomEdgeAlignedRight)));
        Refresh();
    }

    private void Choose(int index)
    {
        _select(index);
        Refresh();
    }

    public void Refresh()
    {
        int index = Math.Clamp(_selected(), 0, _options.Count - 1);
        _button.Content = _label is null ? _options[index] : _label(index);
        _button.Tip(_descriptions is null ? _tip : _descriptions[index] + "\n" + _tip);
    }

    private List<Control> MenuItems()
    {
        var items = new List<Control>(_options.Count);
        int selected = _selected();
        for (int i = 0; i < _options.Count; i++)
        {
            int index = i;
            var item = new MenuItem
            {
                Header = new TextBlock { Text = _options[i], HorizontalAlignment = HorizontalAlignment.Left },
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = i == selected,
            };
            if (_descriptions is not null) item.Tip(_descriptions[i]);
            item.Click += (_, _) => Choose(index);
            items.Add(item);
        }
        return items;
    }
}
