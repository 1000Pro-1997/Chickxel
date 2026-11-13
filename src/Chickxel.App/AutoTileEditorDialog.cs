using System.Runtime.CompilerServices;
using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 자동 타일 세트 고치기(Unity Rule Tile 식): 이름·층·이어지는 무리, 규칙 목록(규칙마다 둘레 8칸 조건·타일·뒤집기/회전 허용), 틀로 규칙 만들기, 시험 판 미리보기.
/// 왼쪽 타일 목록에서 고른 타일을 선택한 규칙(파란 테두리)에 넣는다. 규칙 카드는 <see cref="RuleCard"/>, 시험 판은 <see cref="PreviewBoard"/>.
/// </summary>
public sealed partial class AutoTileEditorDialog : DialogBase
{
    private static readonly AutoTilePreset[] Presets = [AutoTilePreset.Row, AutoTilePreset.Column, AutoTilePreset.Edge4, AutoTilePreset.Box13, AutoTilePreset.Blob47];

    private readonly Tileset _ts;
    private readonly int _id;
    /// <summary>이름 칸을 비우면 쓸 이름(처음 이름).</summary>
    private readonly string _defaultName;
    private readonly List<AutoTileRule> _rules;
    private readonly List<RuleCard> _cards = [];
    private readonly TextBox _name;
    private readonly ComboBox _floor, _group, _preset;
    private readonly TextBlock _groupMembers, _picked, _previewStatus;
    private readonly Button _putPicked, _fillOrder;
    private readonly TilePaletteView _palette = new() { MinColumns = 6 };
    private readonly StackPanel _ruleList = new() { Spacing = 3 };
    /// <summary>"뭐라도 있음" 조건이 볼 층 체크 상자(Tag = 층 Id).</summary>
    private readonly List<CheckBox> _filledFloors = [];
    /// <summary>찍으면 그 칸을 비울 층 체크 상자(Tag = 층 Id).</summary>
    private readonly List<CheckBox> _clearFloors = [];
    private readonly ScrollViewer _ruleScroll;
    private readonly PreviewBoard _preview;
    private readonly ConditionalWeakTable<PixelImage, WriteableBitmap> _bitmaps = new();
    /// <summary>선택한 규칙(타일을 넣을 곳, 없으면 -1).</summary>
    private int _selected;
    /// <summary>미리보기 판에서 마우스가 올라간 칸에 쓰인 규칙(없으면 -1).</summary>
    private int _hovered = -1;

    /// <summary>세트 목록에 보일 짧은 설명(규칙 수, 무리).</summary>
    public static string Summary(AutoTileSet set)
        => set.Group.Length > 0
            ? UiStrings.FormatAutoTile_SummaryGroup(count: set.Rules.Length, group: set.Group)
            : UiStrings.FormatAutoTile_Summary(count: set.Rules.Length);

    private static string PresetName(AutoTilePreset preset) => preset switch
    {
        AutoTilePreset.Row => UiStrings.AutoTile_PresetRow,
        AutoTilePreset.Column => UiStrings.AutoTile_PresetColumn,
        AutoTilePreset.Edge4 => UiStrings.AutoTile_PresetEdge4,
        AutoTilePreset.Box13 => UiStrings.AutoTile_PresetBox13,
        _ => UiStrings.AutoTile_PresetBlob47,
    };

    private AutoTileEditorDialog(Tileset ts, AutoTileSet? set) : base(set is null ? UiStrings.Tileset_AddAutoTile : UiStrings.Tileset_EditAutoTile)
    {
        _ts = ts;
        set ??= AutoTileSet.FromPreset(ts.NextAutoTileId,
            string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.AutoTile_DefaultName, ts.AutoTiles.Length + 1),
            ts.Floors.Length > 0 ? ts.Floors[0].Id : 0, AutoTilePreset.Blob47);
        _id = set.Id;
        _defaultName = set.Name;
        _rules = [.. set.Rules];
        _selected = _rules.Count > 0 ? 0 : -1;

        // 위: 이름·층·이어지는 무리
        _name = Ui.Text(set.Name, 160);
        _floor = Ui.Combo(120, ts.Floors.Select(f => f.Name).ToArray());
        _floor.SelectedIndex = Math.Max(0, ts.FloorOrder(set.Floor));
        _group = new ComboBox { IsEditable = true, Width = 150, Margin = new Thickness(3), PlaceholderText = UiStrings.AutoTile_GroupPlaceholder };
        foreach (var g in ts.AutoTiles.Select(a => a.Group).Where(g => g.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCulture))
            _group.Items.Add(g);
        _group.Text = set.Group;
        _group.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.TextProperty) UpdateGroupMembers();
        };
        _groupMembers = Dim("");
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.TilesetChoice_Name), _name, Ui.Label(UiStrings.TileOptions_Floor), _floor,
            Ui.Label(UiStrings.AutoTile_Group), _group, Ui.InfoBadge(UiStrings.AutoTile_GroupInfo)));
        Body.Children.Add(_groupMembers);
        // "뭐라도 있음" 조건이 볼 층(모두 켜면 모든 층)
        var filledRow = Ui.Row(Ui.Label(UiStrings.AutoTile_FilledFloors));
        foreach (var f in ts.Floors)
        {
            var check = Ui.Check(f.Name, set.FilledFloors.IsDefaultOrEmpty || set.FilledFloors.Contains(f.Id));
            check.Tag = f.Id;
            _filledFloors.Add(check);
            filledRow.Children.Add(check);
        }
        filledRow.Children.Add(Ui.InfoBadge(UiStrings.AutoTile_FilledFloorsInfo));
        Body.Children.Add(filledRow);
        // 찍으면 그 칸을 비울 다른 층(땅끼리 바꿔 찍기)
        var clearRow = Ui.Row(Ui.Label(UiStrings.AutoTile_ClearFloors));
        foreach (var f in ts.Floors)
        {
            var check = Ui.Check(f.Name, !set.ClearFloors.IsDefaultOrEmpty && set.ClearFloors.Contains(f.Id));
            check.Tag = f.Id;
            _clearFloors.Add(check);
            clearRow.Children.Add(check);
        }
        clearRow.Children.Add(Ui.InfoBadge(UiStrings.AutoTile_ClearFloorsInfo));
        Body.Children.Add(clearRow);

        // 왼쪽: 타일 목록과 고른 타일
        _palette.Tileset = ts;
        _palette.SlotSize = Math.Clamp(Math.Max(ts.CellWidth, ts.CellHeight) * 2, 24, 36);
        _palette.SelectionChanged += _ => UpdatePicked();
        _palette.TileActivated += _ => PutPicked(add: false);
        var paletteScroll = new ScrollViewer
        {
            Content = _palette,
            Width = 230,
            Height = 430,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _picked = Dim("");
        _picked.Width = 230;
        _picked.TextWrapping = TextWrapping.Wrap;
        _putPicked = Ui.Button(VectorIcon.ArrowRight, "", (_, _) => PutPicked(add: false));
        _putPicked.Tip(UiStrings.AutoTile_PutPickedTip);
        var left = new StackPanel { Spacing = 2, Children = { Ui.Label(UiStrings.Tileset_Tiles, bold: true), paletteScroll, _picked, _putPicked } };

        // 가운데: 규칙 목록과 틀
        _ruleScroll = new ScrollViewer { Content = _ruleList, Width = 480, Height = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var addRule = Ui.Button(VectorIcon.Plus, UiStrings.AutoTile_AddRule, (_, _) => AddRule());
        _preset = Ui.Combo(150, Presets.Select(PresetName).ToArray());
        // 지금 규칙의 조건이 틀 그대로면 그 틀(아니면 8방향)
        _preset.SelectedIndex = Array.IndexOf(Presets, Presets.FirstOrDefault(
            p => AutoTileShapes.Rules(p).Select(r => (r.Same, r.Other)).SequenceEqual(_rules.Select(r => (r.Same, r.Other))), AutoTilePreset.Blob47));
        var makeRules = Ui.Button(VectorIcon.Grid3x3, UiStrings.AutoTile_MakeRules, (_, _) => MakeRules());
        _fillOrder = Ui.Button(UiStrings.AutoTile_FillInOrder, (_, _) => FillInOrder());
        _fillOrder.Tip(UiStrings.AutoTile_FillInOrderTip);
        var middle = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Row(Ui.Label(UiStrings.AutoTile_Rules, bold: true), Ui.InfoBadge(UiStrings.AutoTile_RulesInfo), addRule),
                _ruleScroll,
                Ui.Row(Ui.Label(UiStrings.AutoTile_Preset), _preset, makeRules, Ui.InfoBadge(UiStrings.AutoTile_PresetInfo)),
                Ui.Row(_fillOrder),
            },
        };

        // 오른쪽: 시험 판
        _preview = new PreviewBoard(ts);
        _preview.HoverChanged += OnPreviewHover;
        _previewStatus = Dim(UiStrings.AutoTile_PreviewHint);
        _previewStatus.Width = _preview.Width;
        _previewStatus.TextWrapping = TextWrapping.Wrap;
        var clearBoard = Ui.IconButton(VectorIcon.Eraser, UiStrings.AutoTile_PreviewClear, (_, _) => _preview.Clear());
        var resetBoard = Ui.IconButton(VectorIcon.Refresh, UiStrings.AutoTile_PreviewReset, (_, _) => _preview.Reset());
        // 왼쪽 버튼으로 "뭐라도 있음" 칸(회색) 놓기 — 끄면 Shift+왼쪽 버튼으로
        var blockMode = new ToggleButton
        {
            Content = Ui.IconLabel(VectorIcon.Triangle, UiStrings.AutoTile_PreviewBlock),
            Padding = new Thickness(6, 1),
            Margin = new Thickness(2, 0),
            VerticalAlignment = VerticalAlignment.Center,
        }.Tip(UiStrings.AutoTile_PreviewBlockTip);
        blockMode.IsCheckedChanged += (_, _) => _preview.BlockMode = blockMode.IsChecked == true;
        var right = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Row(Ui.Label(UiStrings.AutoTile_Preview, bold: true), Ui.InfoBadge(UiStrings.AutoTile_PreviewInfo), clearBoard, resetBoard, blockMode),
                new Border { BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), Child = _preview, HorizontalAlignment = HorizontalAlignment.Left },
                _previewStatus,
            },
        };

        // 창이 화면보다 넓어지면 옆으로 스크롤하지 않고 다음 열을 아래로 내린다
        foreach (var column in new Control[] { left, middle, right }) column.Margin = new Thickness(0, 0, 12, 8);
        var columns = new WrapPanel { Orientation = Orientation.Horizontal, Children = { left, middle, right } };
        Body.Children.Add(columns);

        UpdateGroupMembers();
        UpdatePicked();
        BuildRuleList();
        PreviewChanged();
    }

    private static TextBlock Dim(string text) => new()
    {
        Text = text,
        Foreground = AppTheme.DimTextBrush,
        FontSize = AppTheme.SmallFontSize,
        Margin = new Thickness(3, 0),
    };

    /// <summary>지금 고치는 세트(창의 값 그대로).</summary>
    private AutoTileSet Current()
    {
        var name = string.IsNullOrWhiteSpace(_name.Text) ? _defaultName : _name.Text.Trim();
        int floor = _floor.SelectedIndex >= 0 && _floor.SelectedIndex < _ts.Floors.Length ? _ts.Floors[_floor.SelectedIndex].Id : _ts.ValidFloor(0);
        var filled = _filledFloors.Where(c => c.IsChecked == true).Select(c => (int)c.Tag!).ToList();
        return new AutoTileSet
        {
            Id = _id, Name = name, Floor = floor, Group = (_group.Text ?? "").Trim(), Rules = [.. _rules],
            // 모두 = 모든 층(빈 목록), 하나도 안 고르면 세트의 층만
            FilledFloors = filled.Count == _filledFloors.Count ? [] : filled.Count == 0 ? [floor] : [.. filled],
            ClearFloors = [.. _clearFloors.Where(c => c.IsChecked == true).Select(c => (int)c.Tag!).Where(f => f != floor)],
        };
    }

    // ───────────── 무리 ─────────────

    private void UpdateGroupMembers()
    {
        var group = (_group.Text ?? "").Trim();
        if (group.Length == 0)
        {
            _groupMembers.Text = UiStrings.AutoTile_GroupAlone;
            return;
        }
        var names = _ts.AutoTiles.Where(a => a.Id != _id && string.Equals(a.Group, group, StringComparison.Ordinal))
            .Select(a => a.Name.Length > 0 ? a.Name : UiStrings.TilesetChoice_Unnamed).ToList();
        _groupMembers.Text = names.Count > 0 ? UiStrings.FormatAutoTile_GroupMembers(names: string.Join(", ", names)) : UiStrings.AutoTile_GroupNew;
    }

    // ───────────── 고른 타일 ─────────────

    /// <summary>목록에서 고른 타일(목록 읽는 차례: 위 줄부터 왼쪽부터).</summary>
    private List<TileDef> PickedTiles() => [.. _palette.SelectedTiles.OrderBy(t => t.PaletteY).ThenBy(t => t.PaletteX)];

    private void UpdatePicked()
    {
        var picked = PickedTiles();
        _picked.Text = picked.Count switch
        {
            0 => UiStrings.AutoTile_PickedNone,
            1 => UiStrings.FormatAutoTile_PickedOne(name: picked[0].Name.Length > 0 ? picked[0].Name : picked[0].Id.ToString(System.Globalization.CultureInfo.CurrentCulture)),
            _ => UiStrings.FormatAutoTile_Picked(count: picked.Count),
        };
        _picked.Foreground = picked.Count > 0 ? AppTheme.AccentBrush : AppTheme.DimTextBrush;
        _fillOrder.IsEnabled = picked.Count > 0 && _rules.Count > 0;
        UpdatePutPicked();
    }

    private void UpdatePutPicked()
    {
        bool ok = _selected >= 0 && _selected < _rules.Count;
        ((StackPanel)_putPicked.Content!).Children.OfType<TextBlock>().First().Text = UiStrings.FormatAutoTile_PutPicked(index: ok ? _selected + 1 : 0);
        _putPicked.IsEnabled = ok && _palette.SelectedTiles.Count > 0;
    }

    /// <summary>고른 타일을 규칙 <paramref name="index"/>에 넣는다(<paramref name="add"/>면 더하기 — 같은 타일은 비중 +1).</summary>
    private void PutPicked(int index, bool add)
    {
        var picked = PickedTiles();
        if (picked.Count == 0 || index < 0 || index >= _rules.Count) return;
        var current = add ? _rules[index].Tiles.ToList() : [];
        foreach (var t in picked)
        {
            int k = current.FindIndex(c => c.Tile == t.Id);
            if (k >= 0) current[k] = current[k] with { Weight = current[k].Weight + 1 };
            else current.Add(new AutoTileChoice(t.Id));
        }
        SetRule(index, _rules[index] with { Tiles = [.. current] });
    }

    private void PutPicked(bool add) => PutPicked(_selected, add);

    /// <summary>고른 타일을 차례대로 규칙 1, 2, …에 한 장씩 넣는다(남는 규칙은 그대로).</summary>
    private void FillInOrder()
    {
        var picked = PickedTiles();
        for (int i = 0; i < _rules.Count && i < picked.Count; i++)
            _rules[i] = _rules[i] with { Tiles = [new AutoTileChoice(picked[i].Id)] };
        foreach (var card in _cards) card.Refresh();
        PreviewChanged();
    }

    // ───────────── 규칙 목록 ─────────────

    private void BuildRuleList()
    {
        _ruleList.Children.Clear();
        _cards.Clear();
        for (int i = 0; i < _rules.Count; i++)
        {
            var card = new RuleCard(this, i);
            _cards.Add(card);
            _ruleList.Children.Add(card);
        }
        UpdateCardStates();
        UpdatePicked();
    }

    private void UpdateCardStates()
    {
        foreach (var card in _cards) card.UpdateState(card.Index == _selected, card.Index == _hovered);
    }

    private void Select(int index)
    {
        if (_selected == index) return;
        _selected = index;
        UpdateCardStates();
        UpdatePutPicked();
    }

    /// <summary>규칙 하나를 바꾼다(카드·미리보기를 다시 그림).</summary>
    private void SetRule(int index, AutoTileRule rule)
    {
        _rules[index] = rule;
        _cards[index].Refresh();
        PreviewChanged();
    }

    /// <summary>규칙 목록 모양이 바뀜(더하기·지우기·옮기기): 카드를 다시 만들고 <paramref name="select"/>를 골라 보이게 한다.</summary>
    private void RulesChanged(int select)
    {
        _selected = _rules.Count == 0 ? -1 : Math.Clamp(select, 0, _rules.Count - 1);
        _hovered = -1;
        BuildRuleList();
        PreviewChanged();
        if (_selected >= 0) Dispatcher.UIThread.Post(() => { if (_selected < _cards.Count) _cards[_selected].BringIntoView(); }, DispatcherPriority.Background);
    }

    private void AddRule()
    {
        _rules.Add(new AutoTileRule());
        RulesChanged(_rules.Count - 1);
    }

    private void MoveRule(int index, int delta)
    {
        int j = index + delta;
        if (j < 0 || j >= _rules.Count) return;
        (_rules[index], _rules[j]) = (_rules[j], _rules[index]);
        RulesChanged(j);
    }

    private void DuplicateRule(int index)
    {
        _rules.Insert(index + 1, _rules[index]);
        RulesChanged(index + 1);
    }

    private void DeleteRule(int index)
    {
        _rules.RemoveAt(index);
        RulesChanged(Math.Min(index, _rules.Count - 1));
    }

    /// <summary>고른 틀의 규칙으로 바꾼다(타일이 들어 있으면 먼저 묻는다).</summary>
    private void MakeRules()
    {
        if (_preset.SelectedIndex < 0) return;
        if (_rules.Any(r => r.HasTiles)
            && Ui.Ask(this, UiStrings.FormatAutoTile_MakeRulesAsk(count: _rules.Count), UiStrings.AutoTile_MakeRules, MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK)
            return;
        _rules.Clear();
        _rules.AddRange(AutoTileShapes.Rules(Presets[_preset.SelectedIndex]));
        RulesChanged(0);
    }

    // ───────────── 미리보기 ─────────────

    private void PreviewChanged()
    {
        _preview.SetSet(Current());
        OnPreviewHover(_preview.Hover);
    }

    /// <summary>판 위 칸이 바뀜: 그 칸에 쓰인 규칙을 목록에서 강조하고 글로 보인다.</summary>
    private void OnPreviewHover(Point? cell)
    {
        int rule = -1;
        string status = UiStrings.AutoTile_PreviewHint;
        if (!_rules.Any(r => r.HasTiles)) status = UiStrings.AutoTile_PreviewNoTiles;
        else if (cell is { } at && _preview.RuleAt(at) is { } hit)
        {
            rule = hit.Rule;
            status = hit.Exact
                ? UiStrings.FormatAutoTile_PreviewRule(x: at.X, y: at.Y, index: rule + 1)
                : UiStrings.FormatAutoTile_PreviewNearest(x: at.X, y: at.Y, index: rule + 1);
        }
        _previewStatus.Text = status;
        if (rule == _hovered) return;
        _hovered = rule;
        UpdateCardStates();
        if (rule >= 0 && rule < _cards.Count) _cards[rule].BringIntoView();
    }

    private WriteableBitmap Bitmap(PixelImage image) => _bitmaps.GetValue(image, ImageUtil.ToBitmap);

    /// <summary>자동 타일 세트를 고친 결과(새 세트면 새 Id). 취소하면 null.</summary>
    public static AutoTileSet? Run(Visual? owner, Tileset ts, AutoTileSet? set)
    {
        var dlg = new AutoTileEditorDialog(ts, set);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Current() : null;
    }
}
