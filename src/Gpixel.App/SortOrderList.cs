using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 격자 정렬 기준(<see cref="GridOrder"/>)을 세로 목록으로 고치는 칸: 줄마다 끌기 손잡이 ⠿ + 순번 + 기준 이름 + ▲▼.
/// 끌어 놓기(넣을 자리에 강조 선) · ▲▼ · Alt+↑/↓(고른 줄)로 순서를 바꾼다. 태그는 늘 프레임 바로 앞에 붙어 다니고(Core 정규화와 같음) 끔/켬만 고른다 —
/// 끄면 그 자리에 흐리게. 프레임 시트만 이 설정을 쓰면 부위 줄은 흐리게(자리는 그대로, 순번 없음).
/// 줄 사이 "○ 여기서 새 줄"을 누르면 그 위 기준까지가 줄바꿈 단위(<see cref="GridOrder.RowBreak"/>), 맨 아래 "줄바꿈 없음" = 0.
/// 바꿀 때마다 <see cref="Changed"/>(실행취소 이름, 새 순서) — 부르는 쪽이 실행취소 1단계로 반영한다.
/// </summary>
public sealed class SortOrderList : Border
{
    private const double RowHeight = 24;
    private const double SlotHeight = 16;

    private static readonly Cursor GrabCursor = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly Border _insertLine = new()
    {
        Height = 2,
        Background = AppTheme.AccentBrush,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
        IsVisible = false,
    };

    private GridOrder? _order;
    private bool _parts, _breaks;
    /// <summary>고른(키보드로 옮길) 기준. 태그 줄을 고르면 프레임과 같이 움직인다.</summary>
    private GridKey _selected = GridKey.Frame;
    /// <summary>끌고 있는 기준 단위(태그+프레임은 프레임), 끄는 중이 아니면 null.</summary>
    private GridKey? _dragging;
    private int _dropAt = -1;
    /// <summary>단위(부위·스킨·프레임) 줄 컨트롤(끌어 놓을 자리 계산용), 단위 순서대로.</summary>
    private readonly List<(GridKey Key, Control Row)> _unitRows = new();

    public SortOrderList()
    {
        Child = new Panel { Children = { _rows, _insertLine } };
        BorderBrush = AppTheme.BorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(3);
        Padding = new Thickness(2);
        Background = AppTheme.InputBrush;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        MinWidth = 220;
        MaxWidth = 420;
        Focusable = true;
        IsTabStop = true;
    }

    /// <summary>사용자가 순서·태그·줄바꿈을 바꿈: (실행취소 이름, 새 순서).</summary>
    public event Action<string, GridOrder>? Changed;

    /// <summary>
    /// 보일 순서. <paramref name="parts"/>가 false면 부위 기준은 흐리게(그 설정을 프레임 시트만 씀), <paramref name="breaks"/>가 false면 줄바꿈 칸을 숨긴다(자동 배치).
    /// 값이 같으면 다시 만들지 않는다.
    /// </summary>
    public void Show(GridOrder order, bool parts, bool breaks)
    {
        if (order == _order && parts == _parts && breaks == _breaks) return;
        _order = order;
        _parts = parts;
        _breaks = breaks;
        Build();
    }

    // ───────────── 순서 계산 ─────────────

    /// <summary>움직이는 단위(태그 뺀 기준: 부위·스킨·프레임 순서).</summary>
    private static List<GridKey> Units(GridOrder order) => order.Keys.Where(k => k != GridKey.Tag).ToList();

    /// <summary>단위 순서를 바꾼 새 순서(태그는 프레임 바로 앞으로, 줄바꿈 단위는 앞에서 센 수 그대로 — 표시가 제자리에 남음).</summary>
    private static GridOrder WithUnits(GridOrder order, List<GridKey> units)
    {
        var keys = new List<GridKey>(units);
        if (order.Keys.Contains(GridKey.Tag)) keys.Insert(keys.IndexOf(GridKey.Frame), GridKey.Tag);
        return new GridOrder(keys, order.RowBreak).Normalized();
    }

    /// <summary>
    /// 태그 기준을 켜고 끈다(프레임 바로 앞). 줄바꿈 단위 안이나 바로 뒤에 들어가면 단위를 하나 늘려(빼면 줄여) 묶음이 그대로이게:
    /// 부위 → 스킨 → 프레임(부위·스킨마다 한 줄)에 켜면 부위·스킨·태그마다 한 줄.
    /// </summary>
    private static GridOrder WithTag(GridOrder order, bool on)
    {
        var keys = order.Keys.Where(k => k != GridKey.Tag).ToList();
        int rowBreak = order.RowBreak;
        if (on)
        {
            if (order.Keys.Contains(GridKey.Tag)) return order;
            int t = keys.IndexOf(GridKey.Frame);
            keys.Insert(t, GridKey.Tag);
            if (rowBreak > 0 && t <= rowBreak) rowBreak++;
        }
        else
        {
            int t = order.Keys.ToList().IndexOf(GridKey.Tag);
            if (t < 0) return order;
            if (t < rowBreak) rowBreak--;
        }
        return new GridOrder(keys, rowBreak).Normalized();
    }

    /// <summary>단위 <paramref name="key"/>를 단위 목록의 <paramref name="to"/>번(옮긴 뒤 위치)으로.</summary>
    private void MoveUnit(GridKey key, int to)
    {
        if (_order is not { } order) return;
        var units = Units(order);
        int from = units.IndexOf(key);
        to = Math.Clamp(to, 0, units.Count - 1);
        if (from < 0 || from == to) return;
        units.RemoveAt(from);
        units.Insert(to, key);
        _selected = key;
        Raise("정렬 순서", WithUnits(order, units));
    }

    private void MoveBy(GridKey key, int delta)
    {
        if (_order is not { } order) return;
        var unit = key == GridKey.Tag ? GridKey.Frame : key;
        MoveUnit(unit, Units(order).IndexOf(unit) + delta);
    }

    private void Raise(string label, GridOrder order)
    {
        if (_order is { } old && order == old) return;
        Changed?.Invoke(label, order);
    }

    // ───────────── 화면 만들기 ─────────────

    private static string KeyName(GridKey k) => k switch
    {
        GridKey.Part => "부위",
        GridKey.Skin => "스킨",
        GridKey.Tag => "태그",
        _ => "프레임",
    };

    private bool Shown(GridKey k) => _parts || k != GridKey.Part;

    private void Build()
    {
        // 다시 만들면 단추가 사라져 포커스를 잃으므로, 목록 안에 있었으면 목록으로 되돌린다(Alt+↑/↓를 이어서 쓰게).
        bool hadFocus = IsKeyboardFocusWithin;
        _rows.Children.Clear();
        _unitRows.Clear();
        if (_order is not { } order) return;
        var keys = order.Keys;
        bool tagOn = keys.Contains(GridKey.Tag);
        // 보이는(이 결과가 쓰는) 기준 기준의 줄바꿈 단위 — 프레임 시트만 쓰면 부위를 빼고 센다(GridOrder.For와 같음).
        int shownBreak = order.For(_parts).RowBreak;
        int ordinal = 0, shownSoFar = 0;
        int shownTotal = keys.Count(Shown);
        for (int i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            // 태그를 끈 상태: 프레임 바로 앞에 흐린 태그 줄(켜면 들어갈 자리)
            if (k == GridKey.Frame && !tagOn) _rows.Children.Add(TagRow(on: false, ordinal: 0));
            bool shown = Shown(k);
            if (shown) ordinal++;
            var row = k == GridKey.Tag ? TagRow(on: true, ordinal) : KeyRow(k, shown ? ordinal : 0, keys);
            _rows.Children.Add(row);
            if (k != GridKey.Tag) _unitRows.Add((k, row));
            if (!shown) continue;
            shownSoFar++;
            // 이 기준 뒤 줄바꿈 자리(마지막 보이는 기준 뒤에는 없음)
            if (_breaks && shownSoFar < shownTotal) _rows.Children.Add(BreakSlot(i + 1, shownSoFar == shownBreak, keys));
        }
        if (_breaks) _rows.Children.Add(NoBreakSlot(shownBreak == 0));
        if (hadFocus) Dispatcher.UIThread.Post(() => Focus());
    }

    /// <summary>부위·스킨·프레임 줄: ⠿ + 순번 + 이름 + ▲▼.</summary>
    private Control KeyRow(GridKey key, int ordinal, IReadOnlyList<GridKey> keys)
    {
        var units = keys.Where(k => k != GridKey.Tag).ToList();
        int unit = units.IndexOf(key);
        bool shown = ordinal > 0;
        var handle = new TextBlock
        {
            Text = "⠿",
            Foreground = AppTheme.DimTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent,
            Cursor = GrabCursor,
        }.Tip("끌어서 순서 바꾸기 (Alt+↑/↓)");
        string hint = key switch
        {
            GridKey.Part when !shown => " (프레임 시트에는 없는 축 — 건너뜀)",
            GridKey.Frame when keys.Contains(GridKey.Tag) => " (태그 안 번호)",
            _ => "",
        };
        var row = RowGrid(key, handle, ordinal, KeyName(key) + hint, shown,
            ArrowButton("▲", "앞으로(더 큰 기준) — Alt+↑", unit > 0, () => MoveBy(key, -1)),
            ArrowButton("▼", "뒤로 — Alt+↓", unit < units.Count - 1, () => MoveBy(key, 1)));
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Focus();
            _selected = key;
            _dragging = key;
            _dropAt = -1;
            e.Pointer.Capture(handle);
            UpdateSelection();
        };
        handle.PointerMoved += (_, e) =>
        {
            if (_dragging is null) return;
            UpdateDrop(e.GetPosition(_rows).Y);
        };
        handle.PointerReleased += (_, e) =>
        {
            if (_dragging is not { } dragged) return;
            // 놓을 자리를 먼저 읽는다(붙잡기를 풀면 PointerCaptureLost가 끄기 상태를 지움).
            int at = _dropAt;
            EndDrag();
            e.Pointer.Capture(null);
            if (at < 0 || _order is not { } order) return;
            int from = Units(order).IndexOf(dragged);
            // 넣을 자리(단위 사이 번호) → 옮긴 뒤 위치
            MoveUnit(dragged, at > from ? at - 1 : at);
        };
        handle.PointerCaptureLost += (_, _) => EndDrag();
        return row;
    }

    /// <summary>태그 줄: 프레임에 붙어 다님(손잡이·▲▼ 없음) + 끔/켬. 끄면 흐리게, 순번 없음.</summary>
    private Control TagRow(bool on, int ordinal)
    {
        var toggle = new SegmentedToggle("끔", "켬") { SelectedIndex = on ? 1 : 0, Margin = new Thickness(2, 0) }
            .SetOptionTip(0, "프레임은 프레임 번호 순서.")
            .SetOptionTip(1, "프레임 앞에 태그 기준을 더합니다(태그 순서, 태그 없는 프레임은 끝 — 프레임은 태그 안 번호). 프레임 시트는 여러 태그에 든 프레임을 태그마다 넣습니다.");
        toggle.SelectionChanged += (_, _) =>
        {
            if (_order is not { } order || toggle.SelectedIndex < 0) return;
            Raise("태그 정렬", WithTag(order, toggle.SelectedIndex == 1));
        };
        var handle = new TextBlock { Text = "↳", Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
            .Tip("태그는 늘 프레임 바로 앞에 붙어 다닙니다(프레임을 옮기면 같이 옮겨짐)");
        var row = RowGrid(GridKey.Tag, handle, ordinal, on ? "태그 (프레임 바로 앞)" : "태그 (꺼짐 — 켜면 여기)", on, toggle);
        return row;
    }

    /// <summary>줄 한 칸: [손잡이 18][순번 22][이름 *][오른쪽 칸들]. 흐린 줄은 이름·순번만 흐리게(오른쪽 단추는 그대로 쓸 수 있음).</summary>
    private Border RowGrid(GridKey key, Control handle, int ordinal, string name, bool active, params Control[] right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("18,22,*" + string.Concat(right.Select(_ => ",Auto"))) };
        var number = new TextBlock
        {
            Text = ordinal > 0 ? ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) : "–",
            Foreground = active ? AppTheme.AccentBrush : AppTheme.DimTextBrush,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var label = new TextBlock
        {
            Text = name,
            Foreground = active ? AppTheme.TextBrush : AppTheme.DimTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0),
        };
        Grid.SetColumn(number, 1);
        Grid.SetColumn(label, 2);
        g.Children.Add(handle);
        g.Children.Add(number);
        g.Children.Add(label);
        for (int i = 0; i < right.Length; i++)
        {
            right[i].VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right[i], 3 + i);
            g.Children.Add(right[i]);
        }
        var row = new Border
        {
            Height = RowHeight,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(2, 0),
            Background = Brushes.Transparent,
            Tag = key,
            Child = g,
        };
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            Focus();
            _selected = key;
            UpdateSelection();
        };
        if (key == _selected) row.Background = AppTheme.HoverBrush;
        return row;
    }

    private static Button ArrowButton(string text, string tip, bool enabled, Action action)
    {
        var b = new Button
        {
            Content = text,
            Width = 22,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0),
            FontSize = AppTheme.SmallFontSize,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = enabled,
        }.Tip(tip);
        b.Click += (_, _) => action();
        return b;
    }

    /// <summary>
    /// 기준 사이 줄바꿈 자리: 고른 자리 = "◉ ↵ 여기서 새 줄"(강조색 선), 아니면 흐린 "○ 여기서 새 줄"(누르면 그 자리로).
    /// <paramref name="rowBreak"/> = 이 자리를 고르면 될 줄바꿈 단위(앞에서 센 전체 기준 수).
    /// </summary>
    private Control BreakSlot(int rowBreak, bool selected, IReadOnlyList<GridKey> keys)
    {
        var above = string.Join("·", keys.Take(rowBreak).Where(Shown).Select(KeyName));
        var text = new TextBlock
        {
            Text = selected ? $"◉ ↵ 여기서 새 줄 — {above}마다 한 줄" : "○ 여기서 새 줄",
            FontSize = AppTheme.SmallFontSize,
            Foreground = selected ? AppTheme.AccentBrush : AppTheme.DimTextBrush,
            FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(40, 0, 0, 0),
        };
        var slot = new Border
        {
            Height = SlotHeight,
            Background = Brushes.Transparent,
            BorderBrush = selected ? AppTheme.AccentBrush : Brushes.Transparent,
            BorderThickness = new Thickness(0, selected ? 1 : 0, 0, 0),
            Margin = new Thickness(18, 0, 4, 0),
            Opacity = selected ? 1 : 0.6,
            Cursor = HandCursor,
            Child = text,
        }.Tip($"앞쪽 기준({above})이 같은 칸끼리 한 줄로 묶어 새 줄에서 시작");
        slot.PointerEntered += (_, _) => { if (!selected) slot.Opacity = 1; };
        slot.PointerExited += (_, _) => { if (!selected) slot.Opacity = 0.6; };
        slot.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(slot).Properties.IsLeftButtonPressed || _order is not { } order) return;
            e.Handled = true;
            if (selected) return;
            Raise("줄바꿈", new GridOrder(order.Keys, rowBreak).Normalized());
        };
        return slot;
    }

    /// <summary>맨 아래 "줄바꿈 없음"(칸 수로만 줄바꿈 = 줄바꿈 단위 0).</summary>
    private Control NoBreakSlot(bool selected)
    {
        var slot = new Border
        {
            Height = SlotHeight + 2,
            Background = Brushes.Transparent,
            Margin = new Thickness(18, 2, 4, 0),
            Opacity = selected ? 1 : 0.6,
            Cursor = HandCursor,
            Child = new TextBlock
            {
                Text = selected ? "◉ 줄바꿈 없음 (칸 수로만 줄을 바꿈)" : "○ 줄바꿈 없음 (칸 수로만)",
                FontSize = AppTheme.SmallFontSize,
                Foreground = selected ? AppTheme.AccentBrush : AppTheme.DimTextBrush,
                FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(40, 0, 0, 0),
            },
        }.Tip("모든 칸을 이어서 놓고 칸 수로만 줄을 바꿉니다");
        slot.PointerEntered += (_, _) => { if (!selected) slot.Opacity = 1; };
        slot.PointerExited += (_, _) => { if (!selected) slot.Opacity = 0.6; };
        slot.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(slot).Properties.IsLeftButtonPressed || _order is not { } order) return;
            e.Handled = true;
            if (selected) return;
            Raise("줄바꿈", new GridOrder(order.Keys, 0));
        };
        return slot;
    }

    private void UpdateSelection()
    {
        foreach (var child in _rows.Children)
            if (child is Border { Tag: GridKey k } row) row.Background = k == _selected ? AppTheme.HoverBrush : Brushes.Transparent;
    }

    // ───────────── 끌어 놓기 ─────────────

    /// <summary>끄는 위치 <paramref name="y"/>(목록 기준) → 넣을 자리(단위 사이 번호 0..단위 수)와 강조 선.</summary>
    private void UpdateDrop(double y)
    {
        if (_unitRows.Count == 0) return;
        int at = _unitRows.Count;
        for (int i = 0; i < _unitRows.Count; i++)
        {
            var b = _unitRows[i].Row.Bounds;
            if (y < b.Y + b.Height / 2)
            {
                at = i;
                break;
            }
        }
        _dropAt = at;
        // 단위의 맨 위(태그가 켜졌으면 프레임 단위는 태그 줄부터) 또는 마지막 단위 아래에 선
        double lineY;
        if (at < _unitRows.Count)
        {
            var row = _unitRows[at].Row;
            int index = _rows.Children.IndexOf(row);
            if (_unitRows[at].Key == GridKey.Frame && index > 0 && _rows.Children[index - 1] is Border { Tag: GridKey.Tag } tag) row = tag;
            lineY = row.Bounds.Y - 1;
        }
        else lineY = _unitRows[^1].Row.Bounds.Bottom - 1;
        _insertLine.Margin = new Thickness(0, Math.Max(0, lineY), 0, 0);
        _insertLine.IsVisible = true;
    }

    private void EndDrag()
    {
        _dragging = null;
        _dropAt = -1;
        _insertLine.IsVisible = false;
    }

    // ───────────── 키보드 ─────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _order is not { } order) return;
        if (e.Key is not (Key.Up or Key.Down)) return;
        int delta = e.Key == Key.Up ? -1 : 1;
        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            MoveBy(_selected, delta);
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None) return;
        // ↑/↓ = 고른 줄 바꾸기(보이는 줄 순서, 태그 포함)
        var keys = order.Keys;
        int i = keys.ToList().IndexOf(_selected);
        int next = Math.Clamp(i + delta, 0, keys.Count - 1);
        _selected = keys[next];
        UpdateSelection();
        e.Handled = true;
    }
}
