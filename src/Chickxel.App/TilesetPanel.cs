using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 타일셋 패널: 타일맵 레이어를 고르면 왼쪽 색 패널 자리에 보인다. 타일셋(새로·열기·파일로 저장·설정·PNG/지금 프레임에서 가져오기),
/// 층(차례·보이기·잠금·이름·더하기·지우기), 타일 목록(고르면 타일 붓에 듦), 고른 타일 속성(이름·차지 칸·그림 위치·기본 층), 자동 타일 세트.
/// 타일셋을 고치는 것은 모두 실행취소 한 단계씩이고, 파일 경로가 있으면 프로젝트를 저장할 때 타일셋 파일에도 쓴다.
/// </summary>
public sealed class TilesetPanel : UserControl
{
    private readonly EditorContext _ctx;
    private readonly StackPanel _empty;
    private readonly StackPanel _content;
    private readonly TextBlock _name, _path;
    private readonly StackPanel _floors;
    private readonly TilePaletteView _palette = new();
    private readonly Border _tileProps;
    private readonly TextBox _tileName;
    private readonly NumericUpDown _tileW, _tileH, _tileOffX, _tileOffY;
    private readonly ComboBox _tileFloor;
    private readonly TextBlock _tileInfo;
    private readonly StackPanel _autoList;
    /// <summary>'지금 찍을 것' 카드(패널 맨 위).</summary>
    private readonly TilePickCard _pickCard;
    /// <summary>자동 타일 목록 줄의 대표 타일 그림(시계가 가면 다시 그림).</summary>
    private readonly List<TileThumb> _autoThumbs = new();
    /// <summary>타일 목록에서 고른 칸으로 만든 붓(지금 붓이 이것일 때만 고른 칸을 강조).</summary>
    private TileStamp? _paletteStamp;
    private static readonly IBrush CurrentRowBrush = AppTheme.Brush(Color.FromArgb(70, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
    private readonly Button _merge, _deleteTiles;
    private bool _updating;
    /// <summary>속성을 보이는 타일(하나만 골랐을 때).</summary>
    private int? _propsTile;

    /// <summary>상태 표시줄에 보일 알림.</summary>
    public event EventHandler<string>? Message;

    public TilesetPanel(EditorContext ctx)
    {
        _ctx = ctx;
        var menu = Ui.IconButton(VectorIcon.Menu, UiStrings.Tileset_MenuTip, (s, _) => ShowMenu((Control)s!), size: 14);
        menu.Focusable = false;
        var header = Ui.Header(UiStrings.Tileset_Header);
        var headerTitle = (TextBlock)header.Child!;
        header.Child = null;
        var headerRow = new DockPanel();
        DockPanel.SetDock(menu, Dock.Right);
        headerRow.Children.Add(menu);
        headerRow.Children.Add(headerTitle);
        header.Child = headerRow;

        // 타일셋이 없을 때
        var note = Ui.Label(UiStrings.Tile_NoTileset);
        note.TextWrapping = TextWrapping.Wrap;
        _empty = Ui.Stack(note, Ui.Button(VectorIcon.Plus, UiStrings.Tileset_ChooseOrCreate, (_, _) => ChooseTileset()));

        _name = Ui.Label("", bold: true);
        _path = Ui.Label("");
        _path.Foreground = AppTheme.DimTextBrush;
        _path.FontSize = AppTheme.SmallFontSize;
        _path.TextTrimming = TextTrimming.CharacterEllipsis;

        // 층
        _floors = new StackPanel { Spacing = 1 };
        var addFloor = Ui.Button(VectorIcon.Plus, UiStrings.Tileset_AddFloor, (_, _) => AddFloor());
        var floorsBox = Ui.Group(UiStrings.Tileset_Floors, Ui.Stack(_floors, Ui.Row(addFloor, Ui.InfoBadge(UiStrings.Tileset_FloorsInfo))));

        // 타일 목록
        _palette.SelectionChanged += OnPaletteSelection;
        _palette.TileActivated += id => _ctx.TileTool = TileTool.Brush;
        _palette.TileMenuRequested += (id, e) => ShowTileMenu(id, e);
        var paletteScroll = new ScrollViewer
        {
            Content = _palette,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 320,
        };
        var import = Ui.Button(VectorIcon.ImageDown, UiStrings.Tileset_ImportPng, (_, _) => ImportFromFile());
        var fromFrame = Ui.Button(VectorIcon.Image, UiStrings.Tileset_ImportFrame, (_, _) => ImportFromFrame()).Tip(UiStrings.Tileset_ImportFrameTip);
        _merge = Ui.Button(VectorIcon.Square, UiStrings.Tileset_Merge, (_, _) => MergeSelection()).Tip(UiStrings.Tileset_MergeTip);
        _deleteTiles = Ui.Button(VectorIcon.Trash, UiStrings.Tileset_DeleteTiles, (_, _) => DeleteSelectedTiles());
        var tilesBox = Ui.Group(UiStrings.Tileset_Tiles, Ui.Stack(paletteScroll, new WrapPanel { Children = { import, fromFrame, _merge, _deleteTiles } }));

        // 고른 타일 속성
        _tileName = Ui.Text("", 150);
        _tileName.LostFocus += (_, _) => ApplyTile(t => t with { Name = _tileName.Text?.Trim() ?? "" });
        _tileName.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyTile(t => t with { Name = _tileName.Text?.Trim() ?? "" }); };
        _tileW = Ui.Num(1, Tileset.MaxFootprint, 1, 50);
        _tileH = Ui.Num(1, Tileset.MaxFootprint, 1, 50);
        _tileW.ValueChanged += (_, _) => ApplyFootprint();
        _tileH.ValueChanged += (_, _) => ApplyFootprint();
        _tileOffX = Ui.Num(-4096, 4096, 0, 56).Unit("px");
        _tileOffY = Ui.Num(-4096, 4096, 0, 56).Unit("px");
        _tileOffX.ValueChanged += (_, _) => ApplyTile(t => t with { DrawOffset = new Point((int)_tileOffX.NumValue(), t.DrawOffset.Y) });
        _tileOffY.ValueChanged += (_, _) => ApplyTile(t => t with { DrawOffset = new Point(t.DrawOffset.X, (int)_tileOffY.NumValue()) });
        var bottom = Ui.Button(UiStrings.Tileset_AlignBottom, (_, _) => ApplyTile(t => t with { DrawOffset = TileDef.BottomAligned(t.Image.Height, t.Height, _ctx.CurrentTileset!.CellHeight) })).Tip(UiStrings.Tileset_AlignBottomTip);
        _tileFloor = Ui.Combo(130);
        _tileFloor.SelectionChanged += (_, _) =>
        {
            if (_updating || _ctx.CurrentTileset is not { } ts || _tileFloor.SelectedIndex < 0 || _tileFloor.SelectedIndex >= ts.Floors.Length) return;
            int floor = ts.Floors[_tileFloor.SelectedIndex].Id;
            ApplyTile(t => t with { Floor = floor });
        };
        _tileInfo = Ui.Label("");
        _tileInfo.Foreground = AppTheme.DimTextBrush;
        _tileInfo.FontSize = AppTheme.SmallFontSize;
        _tileProps = Ui.Group(UiStrings.Tileset_TileProps, Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.TilesetChoice_Name), _tileName),
            Ui.Row(Ui.Label(UiStrings.Tileset_Footprint), _tileW, Ui.Label("×"), _tileH, Ui.InfoBadge(UiStrings.Tileset_FootprintInfo)),
            Ui.Row(Ui.Label(UiStrings.Tileset_DrawOffset), _tileOffX, _tileOffY),
            Ui.Row(bottom),
            Ui.Row(Ui.Label(UiStrings.Tileset_DefaultFloor), _tileFloor),
            _tileInfo));

        // 자동 타일
        _autoList = new StackPanel { Spacing = 1 };
        var addAuto = Ui.Button(VectorIcon.Plus, UiStrings.Tileset_AddAutoTile, (_, _) => EditAutoTile(null));
        var autoBox = Ui.Group(UiStrings.Tileset_AutoTiles, Ui.Stack(_autoList, Ui.Row(addAuto, Ui.InfoBadge(UiStrings.Tileset_AutoTilesInfo))));

        _pickCard = new TilePickCard(ctx);
        _content = Ui.Stack(Ui.Stack(_name, _path), _pickCard, floorsBox, tilesBox, _tileProps, autoBox);
        foreach (var box in new[] { floorsBox, tilesBox, _tileProps, autoBox }) Ui.Foldable(box);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(Ui.ScrollHost(Ui.Stack(_empty, _content)));
        Content = root;

        ctx.ProjectReplaced += (_, _) => Refresh();
        ctx.ProjectChanged += (_, _) => Refresh();
        ctx.CurrentChanged += (_, _) => Refresh();
        ctx.TileStateChanged += (_, _) =>
        {
            if (_ctx.CurrentTileset is { } t) RefreshFloors(t);
            RefreshAutoList();
            SyncPalette();
        };
        ctx.TileClockTick += OnTileClock;
        SizeChanged += (_, _) => FitPalette();
        Refresh();
    }

    private void Say(string text) => Message?.Invoke(this, text);

    // ───────────── 상태 → 화면 ─────────────

    private void Refresh()
    {
        if (!IsVisible && !_ctx.IsTilemapSelected) return;
        var ts = _ctx.CurrentTileset;
        _empty.IsVisible = ts is null && _ctx.IsTilemapSelected;
        _content.IsVisible = ts is not null;
        if (ts is null) return;
        _name.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_NameLine, ts.Name.Length > 0 ? ts.Name : UiStrings.TilesetChoice_Unnamed, ts.CellWidth, ts.CellHeight);
        _path.Text = ts.SourcePath is { } path ? Path.GetFileName(path) : UiStrings.Tileset_NotSaved;
        ToolTip.SetTip(_path, ts.SourcePath ?? UiStrings.Tileset_NotSavedTip);
        RefreshFloors(ts);
        if (!ReferenceEquals(_palette.Tileset, ts)) _palette.Tileset = ts;
        FitPalette();
        RefreshTileProps(ts);
        RefreshAutoList();
        SyncPalette();
    }

    /// <summary>팔레트 표시를 지금 찍을 것에 맞춘다: 자동 타일 세트를 쓰면 그 세트의 타일을 옅게, 고른 칸은 붓일 때만 강조.</summary>
    private void SyncPalette()
    {
        bool auto = _ctx.TileTool != TileTool.Eraser && _ctx.UsesAutoTileSource && _ctx.CurrentAutoTileSet is not null;
        _palette.HighlightTiles = auto ? TileVisuals.TilesOf(_ctx.CurrentAutoTileSet!) : null;
        _palette.SelectionActive = !auto && _ctx.TileTool != TileTool.Eraser && _ctx.TileStamp is { } stamp && ReferenceEquals(stamp, _paletteStamp);
        _palette.TimeMs = _ctx.TileClockMs;
    }

    /// <summary>화면 시계: 움직이는 타일이 있으면 팔레트·자동 타일 그림을 돌린다.</summary>
    private void OnTileClock()
    {
        if (!IsEffectivelyVisible || _ctx.CurrentTileset is not { HasAnimation: true }) return;
        long now = _ctx.TileClockMs;
        _palette.TimeMs = now;
        foreach (var thumb in _autoThumbs)
        {
            if (!thumb.IsAnimated) continue;
            thumb.TimeMs = now;
            thumb.InvalidateVisual();
        }
    }

    /// <summary>타일 목록 칸 크기: 패널 폭에 목록 열이 다 들어가게(18~40px).</summary>
    private void FitPalette()
    {
        if (_palette.Tileset is not { } ts) return;
        int cols = Math.Max(_palette.MinColumns, ts.Tiles.Length == 0 ? 0 : ts.Tiles.Max(t => t.PaletteX) + 1);
        double width = Bounds.Width - 40;
        double size = width > 0 ? Math.Clamp(Math.Floor(width / cols), 18, 40) : 32;
        if (Math.Abs(size - _palette.SlotSize) < 0.5) return;
        _palette.SlotSize = size;
        _palette.InvalidateMeasure();
        _palette.InvalidateVisual();
    }

    private void RefreshFloors(Tileset ts)
    {
        _floors.Children.Clear();
        var data = _ctx.CurrentTiles;
        // 지금 찍을 것이 들어갈 층(왼쪽 막대 — 잠겼으면 경고 색)
        var target = TilePick.Describe(_ctx, ts).Floors;
        for (int i = ts.Floors.Length - 1; i >= 0; i--)
        {
            var floor = ts.Floors[i];
            int id = floor.Id;
            bool hidden = data?.IsFloorHidden(id) ?? false, locked = data?.IsFloorLocked(id) ?? false;
            var eye = RowButton(hidden ? VectorIcon.EyeOff : VectorIcon.Eye, UiStrings.Tileset_FloorVisibleTip, () => _ctx.SetTileFloorState(id, hidden: !hidden));
            var lockBtn = RowButton(locked ? VectorIcon.Lock : VectorIcon.LockOpen, UiStrings.Tileset_FloorLockTip, () => _ctx.SetTileFloorState(id, locked: !locked));
            var up = RowButton(VectorIcon.ChevronUp, UiStrings.Tileset_FloorUpTip, () => _ctx.EditCurrentTileset(UiStrings.Tileset_MoveFloorLabel, t => TilesetEdit.MoveFloor(t, id, +1)));
            var down = RowButton(VectorIcon.ChevronDown, UiStrings.Tileset_FloorDownTip, () => _ctx.EditCurrentTileset(UiStrings.Tileset_MoveFloorLabel, t => TilesetEdit.MoveFloor(t, id, -1)));
            up.IsEnabled = i < ts.Floors.Length - 1;
            down.IsEnabled = i > 0;
            var name = Ui.Label(floor.Name);
            name.Foreground = hidden ? AppTheme.DimTextBrush : AppTheme.TextBrush;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            int count = data?.Map.Floor(id).Count ?? 0;
            var countText = Ui.Label(count.ToString(System.Globalization.CultureInfo.CurrentCulture));
            countText.Foreground = AppTheme.DimTextBrush;
            countText.FontSize = AppTheme.SmallFontSize;
            bool isTarget = target.Contains(id);
            var mark = new Border { Width = 3, Margin = new Thickness(0, 2), Background = !isTarget ? Brushes.Transparent : locked ? AppTheme.WarningBrush : AppTheme.AccentBrush };
            if (isTarget) ToolTip.SetTip(mark, UiStrings.TilePick_TargetFloorTip);
            var row = RowGrid(mark, eye, lockBtn, name, countText, up, down);
            row.Background = _ctx.TileFloor == id ? AppTheme.HoverBrush : Brushes.Transparent;
            row.ContextMenu = FloorMenu(floor, ts);
            row.DoubleTapped += (_, _) => RenameFloor(floor);
            ToolTip.SetTip(row, UiStrings.Tileset_FloorRowTip);
            _floors.Children.Add(row);
        }
    }

    /// <summary>목록 줄의 작은 아이콘 단추(포커스를 가져가지 않음).</summary>
    private static Button RowButton(VectorIcon icon, string tip, Action onClick)
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

    /// <summary>한 줄: 첫 글자 칸(<see cref="TextBlock"/> 또는 글을 쌓은 <see cref="StackPanel"/>)만 남는 폭을 다 쓰고 나머지는 제 크기.</summary>
    private static Grid RowGrid(params Control[] cells)
    {
        var g = new Grid { Background = Brushes.Transparent };
        for (int i = 0; i < cells.Length; i++)
        {
            bool fill = cells[i] is TextBlock or StackPanel && !cells.Take(i).Any(c => c is TextBlock or StackPanel);
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
        var fix = new MenuItem { Header = UiStrings.Tileset_UseFloor };
        fix.Click += (_, _) => _ctx.TileFloor = floor.Id;
        var delete = new MenuItem { Header = UiStrings.Tileset_DeleteFloor, IsEnabled = ts.Floors.Length > 1 };
        delete.Click += (_, _) => Dispatcher.UIThread.Post(() => DeleteFloor(floor));
        return new ContextMenu { Items = { rename, fix, new Separator(), delete } };
    }

    private void RefreshTileProps(Tileset ts)
    {
        var selected = _palette.SelectedTiles;
        _merge.IsEnabled = _palette.Selection is { } sel && (sel.Width > 1 || sel.Height > 1) && selected.Count > 0;
        _deleteTiles.IsEnabled = selected.Count > 0;
        var tile = selected.Count == 1 ? selected[0] : null;
        _propsTile = tile?.Id;
        _tileProps.IsVisible = tile is not null;
        if (tile is null) return;
        _updating = true;
        try
        {
            if (!_tileName.IsFocused) _tileName.Text = tile.Name;
            _tileW.Value = tile.Width;
            _tileH.Value = tile.Height;
            _tileOffX.Value = tile.DrawOffset.X;
            _tileOffY.Value = tile.DrawOffset.Y;
            _tileFloor.ItemsSource = ts.Floors.Select(f => f.Name).ToList();
            _tileFloor.SelectedIndex = Math.Max(0, ts.FloorOrder(tile.Floor));
            _tileInfo.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_TileInfo, tile.Id, tile.Image.Width, tile.Image.Height);
        }
        finally
        {
            _updating = false;
        }
    }

    private void RefreshAutoList()
    {
        _autoList.Children.Clear();
        _autoThumbs.Clear();
        if (_ctx.CurrentTileset is not { } ts) return;
        // 무리 점은 무리를 쓴 세트가 하나라도 있을 때만(같은 무리는 같은 색)
        bool anyGroup = ts.AutoTiles.Any(a => a.Group.Length > 0);
        foreach (var set in ts.AutoTiles)
        {
            int id = set.Id;
            bool current = _ctx.AutoTileSet == id && _ctx.UsesAutoTileSource && _ctx.TileTool != TileTool.Eraser;
            var name = Ui.Label(set.Name.Length > 0 ? set.Name : UiStrings.TilesetChoice_Unnamed, bold: current);
            var mode = Ui.Label(AutoTileEditorDialog.Summary(set));
            mode.Foreground = AppTheme.DimTextBrush;
            mode.FontSize = AppTheme.SmallFontSize;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(3, 1, 3, 0);
            mode.Margin = new Thickness(0);
            mode.TextTrimming = TextTrimming.CharacterEllipsis;
            var thumb = new TileThumb(26)
            {
                Tileset = ts,
                Cells = TileVisuals.Representative(ts, set) is { } rep ? [(default, rep)] : [],
                Icon = VectorIcon.Grid3x3,
                TimeMs = _ctx.TileClockMs,
                Margin = new Thickness(3, 2, 2, 2),
            };
            _autoThumbs.Add(thumb);
            var group = new IconView(VectorIcon.Circle, TileVisuals.GroupBrush(set.Group) ?? Brushes.Transparent)
            {
                Width = 9,
                Height = 9,
                Filled = true,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = anyGroup,
            };
            if (set.Group.Length > 0) ToolTip.SetTip(group, UiStrings.FormatTileAuto_GroupTip(name: set.Group));
            // 두 줄: 이름 / 무리 점 + 요약
            var summary = new DockPanel { Margin = new Thickness(3, 0, 3, 1) };
            DockPanel.SetDock(group, Dock.Left);
            summary.Children.Add(group);
            summary.Children.Add(mode);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, summary } };
            var edit = RowButton(VectorIcon.Pencil, UiStrings.Tileset_EditAutoTile, () => EditAutoTile(set));
            var delete = RowButton(VectorIcon.Trash, UiStrings.Tileset_DeleteAutoTile, () => DeleteAutoTile(set));
            var mark = new Border { Width = 3, Background = current ? AppTheme.AccentBrush : Brushes.Transparent, Margin = new Thickness(0, 1) };
            var row = RowGrid(mark, thumb, text, edit, delete);
            row.Background = current ? CurrentRowBrush : Brushes.Transparent;
            row.Cursor = new Cursor(StandardCursorType.Hand);
            row.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                _ctx.AutoTileSet = id;
                _ctx.TileTool = TileTool.AutoTile;
            };
            ToolTip.SetTip(row, UiStrings.Tileset_AutoTileRowTip);
            _autoList.Children.Add(row);
        }
    }

    // ───────────── 타일 목록 ─────────────

    /// <summary>목록에서 칸을 고르면 그 타일들(목록 자리대로)을 붓에 든다.</summary>
    private void OnPaletteSelection(Rectangle? sel)
    {
        if (_ctx.CurrentTileset is not { } ts) return;
        var tiles = _palette.SelectedTiles;
        if (sel is { } r && tiles.Count > 0)
        {
            var cells = tiles.Select(t => new StampCell(t.PaletteX - r.X, t.PaletteY - r.Y, new TileCell(t.Id), null)).ToList();
            _ctx.TileTransform = TileTransform.None;
            _paletteStamp = new TileStamp(cells);
            _ctx.TileStamp = _paletteStamp;
            if (_ctx.TileTool is not (TileTool.Brush or TileTool.Rect or TileTool.Fill)) _ctx.TileTool = TileTool.Brush;
        }
        RefreshTileProps(ts);
    }

    private void ApplyTile(Func<TileDef, TileDef> change)
    {
        if (_updating || _propsTile is not { } id || _ctx.CurrentTileset?.Find(id) is not { } tile) return;
        var next = change(tile);
        if (next == tile) return;
        _ctx.EditCurrentTileset(UiStrings.Tileset_TileEditLabel, ts => TilesetEdit.ReplaceTile(ts, next));
    }

    /// <summary>차지 칸을 바꾸면 그림을 차지 칸 아래에 다시 맞춘다(나무처럼 위로 솟게).</summary>
    private void ApplyFootprint()
    {
        if (_updating || _ctx.CurrentTileset is not { } ts) return;
        int w = (int)_tileW.NumValue(), h = (int)_tileH.NumValue();
        ApplyTile(t => t with { Width = w, Height = h, DrawOffset = TileDef.BottomAligned(t.Image.Height, h, ts.CellHeight) });
    }

    /// <summary>타일 오른쪽 클릭 메뉴(<paramref name="clicked"/> = 누른 칸의 타일).</summary>
    private void ShowTileMenu(int? clicked, PointerPressedEventArgs e)
    {
        var tiles = _palette.SelectedTiles;
        int? animId = clicked ?? (tiles.Count == 1 ? tiles[0].Id : null);
        var animate = new MenuItem { Header = UiStrings.Tileset_AnimateTile, IsEnabled = animId is not null, Icon = Ui.Icon(VectorIcon.Film, 14) };
        animate.Click += (_, _) => Dispatcher.UIThread.Post(() => { if (animId is { } aid) AnimateTile(aid); });
        var dup = new MenuItem { Header = UiStrings.Tileset_DuplicateTile, IsEnabled = tiles.Count == 1 };
        dup.Click += (_, _) =>
        {
            if (tiles.Count != 1) return;
            _ctx.EditCurrentTileset(UiStrings.Tileset_DuplicateTile, ts => TilesetEdit.DuplicateTile(ts, tiles[0].Id).Tileset);
        };
        var merge = new MenuItem { Header = UiStrings.Tileset_Merge, IsEnabled = _merge.IsEnabled };
        merge.Click += (_, _) => MergeSelection();
        var delete = new MenuItem { Header = UiStrings.Tileset_DeleteTiles, IsEnabled = tiles.Count > 0 };
        delete.Click += (_, _) => Dispatcher.UIThread.Post(DeleteSelectedTiles);
        var menu = new ContextMenu { Items = { animate, new Separator(), dup, merge, new Separator(), delete } };
        menu.Open(_palette);
    }

    /// <summary>움직이는 타일 창: 타일의 그림들·장당 시간을 고친다(실행취소 한 단계).</summary>
    private void AnimateTile(int id)
    {
        if (_ctx.CurrentTileset is not { } ts || ts.Find(id) is not { } def) return;
        if (TileAnimationDialog.Run(this, ts, def) is not { } edited) return;
        _ctx.EditCurrentTileset(UiStrings.Tileset_AnimateTileLabel, t => TilesetEdit.ReplaceTile(t, edited));
    }

    private void MergeSelection()
    {
        if (_palette.Selection is not { } sel) return;
        int? made = null;
        _ctx.EditCurrentTileset(UiStrings.Tileset_Merge, ts =>
        {
            if (TilesetEdit.MergeToBigTile(ts, sel) is not { } r) return ts;
            made = r.Id;
            return r.Tileset;
        });
        if (made is { } id && _ctx.CurrentTileset?.Find(id) is { } big)
        {
            _palette.Selection = new Rectangle(big.PaletteX, big.PaletteY, 1, 1);
            OnPaletteSelection(_palette.Selection);
            Say(UiStrings.Tileset_MergedMessage);
        }
    }

    private void DeleteSelectedTiles()
    {
        var ids = _palette.SelectedTiles.Select(t => t.Id).ToList();
        if (ids.Count == 0 || _ctx.Project is not { } p || _ctx.CurrentTileset is not { } ts) return;
        // 맵에 쓰인 타일이면 묻는다(지우면 맵에서도 빠짐).
        bool used = p.Layers.Any(l => l.Tiles is { } d && d.TilesetKey == ts.Key && d.Map.All.Any(c => ids.Contains(c.Cell.Tile)));
        if (used && Ui.Ask(this, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_DeleteUsedAsk, ids.Count), UiStrings.Tileset_DeleteTiles,
                MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        _ctx.EditCurrentTileset(UiStrings.Tileset_DeleteTiles, t => TilesetEdit.RemoveTiles(t, ids), map =>
        {
            foreach (var id in ids) map = map.WithoutTile(id);
            return map;
        });
        _palette.Selection = null;
        _ctx.TileStamp = null;
    }

    // ───────────── 층 ─────────────

    private void AddFloor()
    {
        if (_ctx.CurrentTileset is not { } ts) return;
        var name = InputDialog.Ask(this, UiStrings.Tileset_AddFloor, UiStrings.Tileset_FloorName,
            string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_NewFloorName, ts.Floors.Length + 1));
        if (name is null) return;
        _ctx.EditCurrentTileset(UiStrings.Tileset_AddFloor, t => TilesetEdit.AddFloor(t, name).Tileset);
    }

    private void RenameFloor(TileFloor floor)
    {
        var name = InputDialog.Ask(this, UiStrings.Tileset_RenameFloor, UiStrings.Tileset_FloorName, floor.Name);
        if (name is null || name == floor.Name) return;
        _ctx.EditCurrentTileset(UiStrings.Tileset_RenameFloor, t => TilesetEdit.RenameFloor(t, floor.Id, name));
    }

    private void DeleteFloor(TileFloor floor)
    {
        if (_ctx.Project is not { } p || _ctx.CurrentTileset is not { } ts || ts.Floors.Length <= 1) return;
        bool used = p.Layers.Any(l => l.Tiles is { } d && d.TilesetKey == ts.Key && d.Map.Floor(floor.Id).Count > 0);
        if (used && Ui.Ask(this, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_DeleteFloorAsk, floor.Name), UiStrings.Tileset_DeleteFloor,
                MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        _ctx.EditCurrentTileset(UiStrings.Tileset_DeleteFloor, t => TilesetEdit.RemoveFloor(t, floor.Id), map => map.WithoutFloor(floor.Id));
        if (_ctx.TileFloor == floor.Id) _ctx.TileFloor = null;
    }

    // ───────────── 자동 타일 ─────────────

    private void EditAutoTile(AutoTileSet? set)
    {
        if (_ctx.CurrentTileset is not { } ts) return;
        if (AutoTileEditorDialog.Run(this, ts, set) is not { } edited) return;
        _ctx.EditCurrentTileset(set is null ? UiStrings.Tileset_AddAutoTile : UiStrings.Tileset_EditAutoTile, t => TilesetEdit.PutAutoTile(t, edited));
        _ctx.AutoTileSet = edited.Id;
        _ctx.TileTool = TileTool.AutoTile;
    }

    private void DeleteAutoTile(AutoTileSet set)
    {
        if (Ui.Ask(this, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_DeleteAutoTileAsk, set.Name), UiStrings.Tileset_DeleteAutoTile,
                MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        _ctx.EditCurrentTileset(UiStrings.Tileset_DeleteAutoTile, t => TilesetEdit.RemoveAutoTile(t, set.Id));
        if (_ctx.AutoTileSet == set.Id) _ctx.AutoTileSet = 0;
    }

    // ───────────── 타일셋 ─────────────

    private void ShowMenu(Control anchor)
    {
        bool has = _ctx.CurrentTileset is not null;
        MenuItem Item(string text, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled && _ctx.IsTilemapSelected };
            mi.Click += (_, _) => Dispatcher.UIThread.Post(action);
            return mi;
        }
        var menu = new ContextMenu
        {
            Items =
            {
                Item(UiStrings.Tileset_ChooseOrCreate, ChooseTileset),
                Item(UiStrings.Tileset_SaveFile, SaveTilesetFile, has),
                Item(UiStrings.Tileset_ReloadFile, ReloadTilesetFile, has && _ctx.CurrentTileset?.SourcePath is not null),
                Item(UiStrings.TilesetSettings_Title, EditSettings, has),
                new Separator(),
                Item(UiStrings.Tileset_ImportPng, ImportFromFile, has),
                Item(UiStrings.Tileset_ImportFrame, ImportFromFrame, has),
            },
        };
        menu.Open(anchor);
    }

    /// <summary>이 타일맵 레이어가 쓸 타일셋을 고르거나 새로 만든다.</summary>
    public void ChooseTileset()
    {
        if (_ctx.Project is not { } p || _ctx.CurrentTilemap is not { } layer) return;
        if (TilesetChoiceDialog.Run(this, p, UiStrings.Tileset_ChooseOrCreate) is not { } ts) return;
        _ctx.Structural(() => p.SetLayerTileset(layer, ts));
        if (ts.SourcePath is not null) p.MarkTilesetSynced(ts);
        _ctx.TileStamp = null;
        Refresh();
    }

    private void SaveTilesetFile()
    {
        if (_ctx.Project is not { } p || _ctx.CurrentTileset is not { } ts) return;
        var dir = ts.SourcePath is { } sp ? Path.GetDirectoryName(sp) : p.Path is { } pp ? Path.GetDirectoryName(pp) : null;
        var path = Ui.SaveFile(this, UiStrings.Tileset_SaveFile, dir, (ts.Name.Length > 0 ? ts.Name : "tileset") + Tileset.FileExtension, Tileset.FileExtension,
            new FileFilter(UiStrings.Tileset_FileFilter, "*" + Tileset.FileExtension));
        if (path is null) return;
        var full = Path.GetFullPath(path);
        try
        {
            if (!string.Equals(ts.SourcePath, full, StringComparison.Ordinal))
                _ctx.EditCurrentTileset(UiStrings.Tileset_SaveFile, t => t with { SourcePath = full });
            var saved = _ctx.CurrentTileset!;
            TilesetIO.Save(saved, full);
            p.MarkTilesetSynced(saved);
            Say(string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_SavedMessage, Path.GetFileName(full)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
        Refresh();
    }

    /// <summary>타일셋 파일 내용으로 다시 읽는다(다른 프로젝트에서 고친 것을 가져옴, 실행취소 한 단계).</summary>
    private void ReloadTilesetFile()
    {
        if (_ctx.Project is not { } p || _ctx.CurrentTileset is not { SourcePath: { } path } ts) return;
        try
        {
            var loaded = TilesetIO.Load(path) with { Key = ts.Key };
            _ctx.EditCurrentTileset(UiStrings.Tileset_ReloadFile, _ => loaded);
            p.MarkTilesetSynced(loaded);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
    }

    private void EditSettings()
    {
        if (_ctx.CurrentTileset is not { } ts || TilesetSettingsDialog.Run(this, ts) is not { } next) return;
        _ctx.EditCurrentTileset(UiStrings.TilesetSettings_Title, _ => next);
    }

    private void ImportFromFile()
    {
        if (_ctx.CurrentTileset is null) return;
        var path = Ui.OpenFile(this, UiStrings.Tileset_ImportPng, null, new FileFilter("PNG", "*.png"));
        if (path is null) return;
        PixelImage sheet;
        try
        {
            sheet = PngIO.Load(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
            return;
        }
        Import(sheet);
    }

    /// <summary>지금 프레임의 보이는 그림(타일맵 레이어 빼고)을 시트로 가져온다 — 일반 레이어에 타일을 그려 두고 옮길 때.</summary>
    private void ImportFromFrame()
    {
        if (_ctx.Project is not { } p || _ctx.CurrentTileset is null) return;
        var sheet = p.PlanFrame(_ctx.CurrentFrame, layerFilter: l => !l.IsTilemap).Render();
        Import(sheet);
    }

    private void Import(PixelImage sheet)
    {
        if (_ctx.CurrentTileset is not { } ts) return;
        if (TileImportDialog.Run(this, ts, sheet) is not { } result) return;
        if (result.Added == 0)
        {
            Say(UiStrings.Tileset_ImportNone);
            return;
        }
        _ctx.EditCurrentTileset(UiStrings.Tileset_ImportLabel, _ => result.Tileset);
        Say(string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_ImportedMessage, result.Added));
    }
}
