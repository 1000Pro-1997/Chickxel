using Chickxel.App.Localization;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 층 목록(도구 설정의 층 묶음, 타일셋 설정 창의 층 페이지): 위에 있을수록 위에 그려지는 층. 줄마다 지금 찍을 층 막대(잠겼으면 경고 색), 보이기,
/// 잠금(잠기면 자물쇠와 이름이 노랑), 이름, 찍은 칸 수, 위로·아래로. 도구 설정에서는 줄을 누르면 그 층에만 찍고(수동), "자동"을 켜면 타일마다 기본 층
/// (자동 타일은 세트의 층). 관리용(<c>manage</c>)이면 "자동"과 누르기 고르기가 없다.
/// 두 번 누르기 = 이름 바꾸기, 오른쪽 버튼 = 메뉴(이름 바꾸기·지우기), 층 더하기. 층을 고치는 것은 모두 실행취소 한 단계씩이다.
/// </summary>
internal sealed class TileFloorList : UserControl
{
    private static readonly IBrush ManualRowBrush = AppTheme.Brush(Color.FromArgb(70, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));

    private readonly EditorContext _ctx;
    private readonly bool _manage;
    private readonly CheckBox _auto;
    private readonly StackPanel _rows = new() { Spacing = 1 };
    private bool _updating;

    public TileFloorList(EditorContext ctx, bool manage = false)
    {
        _ctx = ctx;
        _manage = manage;
        _auto = Ui.Check(UiStrings.TileFloors_Auto).Tip(UiStrings.TileFloors_AutoTip);
        _auto.Focusable = false;
        _auto.IsVisible = !manage;
        _auto.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _ctx.TileFloor = _auto.On() ? null : ManualDefault();
        };
        var add = Ui.Button(VectorIcon.Plus, UiStrings.Tileset_AddFloor, (_, _) => AddFloor());
        add.Focusable = false;
        Content = new StackPanel { Spacing = 2, Children = { _auto, _rows, Ui.Row(add, Ui.InfoBadge(UiStrings.Tileset_FloorsInfo)) } };
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    // 화면에 붙어 있는 동안만 상태를 따라간다(닫힌 설정 창이 남아 받지 않게).
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ctx.TileStateChanged += OnChanged;
        _ctx.ProjectChanged += OnChanged;
        _ctx.ProjectReplaced += OnChanged;
        _ctx.CurrentChanged += OnChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _ctx.TileStateChanged -= OnChanged;
        _ctx.ProjectChanged -= OnChanged;
        _ctx.ProjectReplaced -= OnChanged;
        _ctx.CurrentChanged -= OnChanged;
    }

    /// <summary>"자동"을 끌 때 고정할 층: 지금 찍을 것이 들어갈 첫 층, 없으면 맨 위 층.</summary>
    private int? ManualDefault()
    {
        if (_ctx.CurrentTileset is not { Floors.Length: > 0 } ts) return null;
        var target = TilePick.Describe(_ctx, ts).Floors;
        return target.Count > 0 ? target[0] : ts.Floors[^1].Id;
    }

    /// <summary>목록을 지금 타일셋·맵·찍을 층에 맞춘다.</summary>
    public void Refresh()
    {
        _rows.Children.Clear();
        if (!_ctx.IsTilemapSelected || _ctx.CurrentTileset is not { } ts) return;
        _updating = true;
        try
        {
            _auto.IsChecked = _ctx.TileFloor is null;
        }
        finally
        {
            _updating = false;
        }
        var data = _ctx.CurrentTiles;
        var target = TilePick.Describe(_ctx, ts).Floors;
        int? manual = _ctx.TileFloor is { } f ? ts.ValidFloor(f) : null;
        for (int i = ts.Floors.Length - 1; i >= 0; i--)
        {
            var floor = ts.Floors[i];
            int id = floor.Id;
            bool hidden = data?.IsFloorHidden(id) ?? false, locked = data?.IsFloorLocked(id) ?? false;
            var eye = RowButton(hidden ? VectorIcon.EyeOff : VectorIcon.Eye, UiStrings.Tileset_FloorVisibleTip, () => _ctx.SetTileFloorState(id, hidden: !hidden));
            var lockBtn = RowButton(locked ? VectorIcon.Lock : VectorIcon.LockOpen, UiStrings.Tileset_FloorLockTip, () => _ctx.SetTileFloorState(id, locked: !locked));
            if (locked) ((IconView)lockBtn.Content!).Foreground = AppTheme.LockedBrush;
            var up = RowButton(VectorIcon.ChevronUp, UiStrings.Tileset_FloorUpTip, () => EditFloors(UiStrings.Tileset_MoveFloorLabel, t => TilesetEdit.MoveFloor(t, id, +1)));
            var down = RowButton(VectorIcon.ChevronDown, UiStrings.Tileset_FloorDownTip, () => EditFloors(UiStrings.Tileset_MoveFloorLabel, t => TilesetEdit.MoveFloor(t, id, -1)));
            up.IsEnabled = i < ts.Floors.Length - 1;
            down.IsEnabled = i > 0;
            bool isManual = manual == id;
            var name = Ui.Label(floor.Name, bold: isManual);
            name.Foreground = locked ? AppTheme.LockedBrush : hidden ? AppTheme.DimTextBrush : AppTheme.TextBrush;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            int count = data?.Map.Floor(id).Count ?? 0;
            var countText = Ui.Label(count.ToString(System.Globalization.CultureInfo.CurrentCulture));
            countText.Foreground = AppTheme.DimTextBrush;
            countText.FontSize = AppTheme.SmallFontSize;
            bool isTarget = target.Contains(id);
            var mark = new Border { Width = 3, Margin = new Thickness(0, 2), Background = !isTarget ? Brushes.Transparent : locked ? AppTheme.WarningBrush : AppTheme.AccentBrush };
            if (isTarget) ToolTip.SetTip(mark, UiStrings.TilePick_TargetFloorTip);
            var row = _manage
                ? RowGrid(mark, eye, lockBtn, name, countText, up, down,
                    RowButton(VectorIcon.Pencil, UiStrings.Tileset_RenameFloor, () => RenameFloor(floor)),
                    RowButton(VectorIcon.Trash, UiStrings.Tileset_DeleteFloor, () => DeleteFloor(floor)))
                : RowGrid(mark, eye, lockBtn, name, countText, up, down);
            row.Background = isManual && !_manage ? ManualRowBrush : Brushes.Transparent;
            row.ContextMenu = FloorMenu(floor, ts);
            if (!_manage)
            {
                row.Cursor = new Cursor(StandardCursorType.Hand);
                row.PointerPressed += (_, e) =>
                {
                    if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) _ctx.TileFloor = id;
                };
            }
            row.DoubleTapped += (_, _) => RenameFloor(floor);
            ToolTip.SetTip(row, _manage ? UiStrings.Tileset_FloorRowManageTip : UiStrings.Tileset_FloorRowTip);
            _rows.Children.Add(row);
        }
    }

    /// <summary>목록 줄의 작은 아이콘 단추(포커스를 가져가지 않음).</summary>
    internal static Button RowButton(VectorIcon icon, string tip, Action onClick)
    {
        var b = new Button
        {
            Content = Ui.Icon(icon, 12),
            Width = 22,
            Height = 22,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        }.Tip(tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>한 줄: 첫 글자 칸(<see cref="TextBlock"/>)만 남는 폭을 다 쓰고 나머지는 제 크기.</summary>
    private static Grid RowGrid(params Control[] cells)
    {
        var g = new Grid { Background = Brushes.Transparent };
        bool filled = false;
        for (int i = 0; i < cells.Length; i++)
        {
            bool fill = !filled && cells[i] is TextBlock;
            filled |= fill;
            g.ColumnDefinitions.Add(new ColumnDefinition(fill ? new GridLength(1, GridUnitType.Star) : GridLength.Auto));
            Grid.SetColumn(cells[i], i);
            g.Children.Add(cells[i]);
        }
        return g;
    }

    private ContextMenu FloorMenu(TileFloor floor, Tileset ts)
    {
        var rename = new MenuItem { Header = UiStrings.Tileset_RenameFloor };
        rename.Click += (_, _) => Dispatcher.UIThread.Post(() => RenameFloor(floor));
        var delete = new MenuItem { Header = UiStrings.Tileset_DeleteFloor, IsEnabled = ts.Floors.Length > 1 };
        delete.Click += (_, _) => Dispatcher.UIThread.Post(() => DeleteFloor(floor));
        return new ContextMenu { Items = { rename, new Separator(), delete } };
    }

    private void AddFloor()
    {
        if (_ctx.CurrentTileset is not { } ts) return;
        var name = InputDialog.Ask(this, UiStrings.Tileset_AddFloor, UiStrings.Tileset_FloorName,
            string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_NewFloorName, ts.Floors.Length + 1));
        if (name is null) return;
        EditFloors(UiStrings.Tileset_AddFloor, t => TilesetEdit.AddFloor(t, name).Tileset);
    }

    private void RenameFloor(TileFloor floor)
    {
        var name = InputDialog.Ask(this, UiStrings.Tileset_RenameFloor, UiStrings.Tileset_FloorName, floor.Name);
        if (name is null || name == floor.Name) return;
        EditFloors(UiStrings.Tileset_RenameFloor, t => TilesetEdit.RenameFloor(t, floor.Id, name));
    }

    private void DeleteFloor(TileFloor floor)
    {
        if (_ctx.Project is not { } p || _ctx.CurrentTileset is not { } ts || ts.Floors.Length <= 1 || _ctx.CurrentPrimaryTilesetKey is not { } key) return;
        bool used = p.Layers.Any(l => l.Tiles is { } d && d.PrimaryTilesetKey == key && d.Map.Floor(floor.Id).Count > 0);
        if (used && Ui.Ask(this, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_DeleteFloorAsk, floor.Name), UiStrings.Tileset_DeleteFloor,
                MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        // 층은 첫 타일셋 것이라 그 타일셋을 첫째로 쓰는 맵에서만 그 층을 비운다
        EditFloors(UiStrings.Tileset_DeleteFloor, t => TilesetEdit.RemoveFloor(t, floor.Id), (map, offset) => offset == 0 ? map.WithoutFloor(floor.Id) : map);
        if (_ctx.TileFloor == floor.Id) _ctx.TileFloor = null;
    }

    /// <summary>층을 고친다: 층은 레이어의 첫 타일셋 것이므로 그 타일셋에 쓴다(실행취소 한 단계).</summary>
    private void EditFloors(string label, Func<Tileset, Tileset> change, Func<TileMap, int, TileMap>? remap = null)
    {
        if (_ctx.CurrentPrimaryTilesetKey is { } key) _ctx.EditTileset(key, label, change, remap);
    }
}
