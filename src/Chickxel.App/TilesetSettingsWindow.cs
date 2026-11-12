using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 타일셋 설정 창(닫기만): 지금 타일맵 레이어에 쌓은 타일셋 하나(<see cref="Run"/>의 이름표)를 한곳에서 고친다. 페이지 —
/// 타일(모든 타일을 층별로, 고른 타일의 이름·차지 칸·그림 위치·기본 층, 그림 고치기·움직이는 타일·복제·지우기 — 여러 개를 고르면 기본 층을 함께),
/// 층(<see cref="TileFloorList"/> 관리용 — 층은 레이어의 첫 타일셋 것이라 둘째 타일셋부터는 안내만), 자동 타일·무리(세트마다 이름·층·무리·규칙 고치기·지우기, 무리 이름 바꾸기),
/// 타일셋(이름·칸 크기·파일 저장·다시 읽기, PNG·지금 프레임에서 타일 가져오기), 보기(타일셋 패널의 층별 나누기·자동 타일에 쓴 타일 감추기·구성 보기·칸 크기).
/// 고치는 것은 곧바로 타일셋에 쓰여 실행취소 한 단계씩이다(타일셋 패널과 같음). 그림 고치기는 창을 닫고 문서 탭으로 연다(<see cref="EditImageTile"/>).
/// </summary>
public sealed class TilesetSettingsWindow : DialogBase
{
    /// <summary>처음 보일 페이지.</summary>
    public enum Page { Tiles, Floors, AutoTiles, Tileset, View }

    private readonly EditorContext _ctx;
    private readonly string _key;
    private readonly TabView _tabs = new();
    // 타일 페이지
    private readonly TilePaletteView _palette = new() { MinColumns = 8, SlotSize = 28, GroupByFloor = true };
    private readonly TileThumb _thumb = new(48) { Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _tileInfo;
    private readonly TextBox _tileName;
    private readonly NumericUpDown _tileW, _tileH, _offX, _offY;
    private readonly ComboBox _tileFloor;
    private readonly Control _singleOnly;
    private readonly Button _editImage, _animate, _duplicate, _delete;
    // 자동 타일·무리 페이지
    private readonly Grid _sets = new() { RowSpacing = 2, ColumnSpacing = 4 };
    private readonly StackPanel _groups = new() { Spacing = 3 };
    // 타일셋 페이지
    private readonly TextBox _tsName;
    private readonly NumericUpDown _cellW, _cellH;
    private readonly TextBlock _tsFile, _cellNote;
    private readonly Button _reload;
    // 보기 페이지
    private readonly CheckBox _byFloor, _hideAuto, _showParts, _slotAuto;
    private readonly NumericUpDown _slotSize;
    private bool _updating;
    // 자동 타일 페이지를 마지막으로 만든 타일셋(같으면 다시 만들지 않음 — 입력 중인 칸을 지키려고)
    private Tileset? _setsOf;

    /// <summary>"그림 고치기"를 눌러 닫았으면 그 타일 Id(이 타일셋 안의 Id — 부른 쪽이 문서 탭으로 연다).</summary>
    public int? EditImageTile { get; private set; }

    private TilesetSettingsWindow(EditorContext ctx, string key, Page page, int? tile) : base(UiStrings.TilesetSettings_Title)
    {
        _ctx = ctx;
        _key = key;
        CloseOnly();

        // ── 타일
        _palette.SelectionChanged += _ => RefreshTile();
        var paletteScroll = new ScrollViewer
        {
            Content = _palette,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Width = 300,
            Height = 380,
        };
        _tileInfo = Ui.Label("");
        _tileInfo.Foreground = AppTheme.DimTextBrush;
        _tileInfo.TextWrapping = TextWrapping.Wrap;
        _tileInfo.MaxWidth = 220;
        _tileName = Ui.Text("", 170);
        _tileName.LostFocus += (_, _) => ApplyName();
        _tileName.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyName(); };
        _tileW = Ui.Num(1, Tileset.MaxFootprint, 1, 50);
        _tileH = Ui.Num(1, Tileset.MaxFootprint, 1, 50);
        _tileW.ValueChanged += (_, _) => ApplyFootprint();
        _tileH.ValueChanged += (_, _) => ApplyFootprint();
        _offX = Ui.Num(-4096, 4096, 0, 60).Unit("px");
        _offY = Ui.Num(-4096, 4096, 0, 60).Unit("px");
        _offX.ValueChanged += (_, _) => ApplyOne(t => t with { DrawOffset = new Point((int)_offX.NumValue(), t.DrawOffset.Y) });
        _offY.ValueChanged += (_, _) => ApplyOne(t => t with { DrawOffset = new Point(t.DrawOffset.X, (int)_offY.NumValue()) });
        var bottom = Ui.Button(UiStrings.Tileset_AlignBottom, (_, _) => ApplyOne(t => t with { DrawOffset = TileDef.BottomAligned(t.Image.Height, t.Height, Ts!.CellHeight) }))
            .Tip(UiStrings.Tileset_AlignBottomTip);
        _tileFloor = Ui.Combo(150);
        _tileFloor.SelectionChanged += (_, _) => ApplyFloor();
        _editImage = Ui.Button(VectorIcon.Pencil, UiStrings.TilesetWin_EditImage, (_, _) => EditImage()).Tip(UiStrings.TilesetWin_EditImageTip);
        _animate = Ui.Button(VectorIcon.Film, UiStrings.Tileset_AnimateTile, (_, _) => Animate());
        _duplicate = Ui.Button(VectorIcon.Copy, UiStrings.Tileset_DuplicateTile, (_, _) => Duplicate());
        _delete = Ui.Button(VectorIcon.Trash, UiStrings.Tileset_DeleteTiles, (_, _) => DeleteTiles());
        _singleOnly = Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.TilesetChoice_Name), _tileName),
            Ui.Row(Ui.Label(UiStrings.Tileset_Footprint), _tileW, Ui.Label("×"), _tileH, Ui.InfoBadge(UiStrings.Tileset_FootprintInfo)),
            Ui.Row(Ui.Label(UiStrings.Tileset_DrawOffset), _offX, _offY),
            Ui.Row(bottom));
        var props = new StackPanel
        {
            Spacing = 4,
            Width = 300,
            Children =
            {
                Ui.Row(_thumb, _tileInfo),
                _singleOnly,
                Ui.Row(Ui.Label(UiStrings.Tileset_DefaultFloor), _tileFloor, Ui.InfoBadge(UiStrings.Tileset_DefaultFloorInfo)),
                new WrapPanel { Children = { _editImage, _animate, _duplicate, _delete } },
            },
        };
        var tilesPage = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(paletteScroll, Dock.Left);
        props.Margin = new Thickness(10, 0, 0, 0);
        tilesPage.Children.Add(paletteScroll);
        tilesPage.Children.Add(props);

        // ── 층(레이어의 첫 타일셋 것)
        Control floorsPage = ctx.CurrentPrimaryTilesetKey == key
            ? new StackPanel { Width = 420, Children = { new TileFloorList(ctx, manage: true) } }
            : new TextBlock { Text = UiStrings.TilesetWin_FloorsSecondary, TextWrapping = TextWrapping.Wrap, Width = 420, Foreground = AppTheme.DimTextBrush };

        // ── 자동 타일·무리
        var addSet = Ui.Button(VectorIcon.Plus, UiStrings.Tileset_AddAutoTile, (_, _) => EditRules(null));
        var autoPage = new ScrollViewer
        {
            MaxHeight = 440,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    _sets,
                    Ui.Row(addSet, Ui.InfoBadge(UiStrings.Tileset_AutoTilesInfo)),
                    Ui.Row(Ui.Label(UiStrings.TilesetWin_Groups, bold: true), Ui.InfoBadge(UiStrings.TilesetWin_GroupsInfo)),
                    _groups,
                },
            },
        };

        // ── 타일셋
        _tsName = Ui.Text("", 200);
        _tsName.LostFocus += (_, _) => ApplyTilesetName();
        _tsName.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyTilesetName(); };
        _cellW = Ui.Num(1, Tileset.MaxCellSize, 16, 60).Unit("px");
        _cellH = Ui.Num(1, Tileset.MaxCellSize, 16, 60).Unit("px");
        _cellW.ValueChanged += (_, _) => ApplyCellSize();
        _cellH.ValueChanged += (_, _) => ApplyCellSize();
        _cellNote = Ui.Label("");
        _cellNote.Foreground = AppTheme.DimTextBrush;
        _cellNote.TextWrapping = TextWrapping.Wrap;
        _cellNote.MaxWidth = 400;
        _tsFile = Ui.Label("");
        _tsFile.Foreground = AppTheme.DimTextBrush;
        _tsFile.TextTrimming = TextTrimming.CharacterEllipsis;
        _tsFile.MaxWidth = 360;
        _reload = Ui.Button(VectorIcon.Refresh, UiStrings.Tileset_ReloadFile, (_, _) => ReloadFile());
        var tilesetPage = Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.TilesetChoice_Name), _tsName),
            Ui.Row(Ui.Label(UiStrings.TilesetChoice_CellSize), _cellW, Ui.Label("×"), _cellH),
            _cellNote,
            Ui.Row(Ui.Label(UiStrings.TilesetWin_File), _tsFile, Ui.InfoBadge(UiStrings.Tileset_NotSavedTip)),
            Ui.Row(Ui.Button(VectorIcon.Save, UiStrings.Tileset_SaveFile, (_, _) => SaveFile()), _reload),
            Ui.Row(Ui.Button(VectorIcon.Image, UiStrings.Tileset_ImportPng, (_, _) => ImportFromFile()),
                Ui.Button(VectorIcon.Film, UiStrings.Tileset_ImportFrame, (_, _) => ImportFromFrame())));

        // ── 보기
        _byFloor = ViewCheck(UiStrings.TilesetWin_ByFloor, UiStrings.TilesetWin_ByFloorTip, v => _ctx.TileListByFloor = v);
        _hideAuto = ViewCheck(UiStrings.TilesetWin_HideAuto, UiStrings.TilesetWin_HideAutoTip, v => _ctx.TileListHideAutoTiles = v);
        _showParts = ViewCheck(UiStrings.TilesetWin_ShowParts, UiStrings.TilesetWin_ShowPartsTip, v => _ctx.TileShowAutoParts = v);
        _slotAuto = ViewCheck(UiStrings.TilesetWin_SlotAuto, UiStrings.TilesetWin_SlotAutoTip, v => _ctx.TileSlotSize = v ? 0 : (int)_slotSize!.NumValue());
        _slotSize = Ui.Num(EditorContext.MinTileSlotSize, EditorContext.MaxTileSlotSize, 32, 60).Unit("px").Presets(16, 24, 32, 40, 48, 64);
        _slotSize.ValueChanged += (_, _) => { if (!_updating) _ctx.TileSlotSize = (int)_slotSize.NumValue(); };
        var viewPage = Ui.Stack(_byFloor, _hideAuto, _showParts, Ui.Row(Ui.Label(UiStrings.TilesetWin_SlotSize), _slotSize, _slotAuto));

        _tabs.AddPage(UiStrings.TilesetWin_PageTiles, tilesPage);
        _tabs.AddPage(UiStrings.TilesetWin_PageFloors, floorsPage);
        _tabs.AddPage(UiStrings.TilesetWin_PageAuto, autoPage);
        _tabs.AddPage(UiStrings.TilesetWin_PageTileset, tilesetPage);
        _tabs.AddPage(UiStrings.TilesetWin_PageView, viewPage);
        _tabs.SelectedIndex = (int)page;
        Body.Children.Add(_tabs);

        RefreshAll();
        if (tile is { } id)
        {
            _palette.SelectTile(id);
            RefreshTile();
        }
        ctx.TileStateChanged += OnChanged;
        ctx.ProjectChanged += OnChanged;
        Closed += (_, _) =>
        {
            ctx.TileStateChanged -= OnChanged;
            ctx.ProjectChanged -= OnChanged;
        };
    }

    /// <summary>타일셋 <paramref name="key"/>(지금 타일맵 레이어에 쌓은 것)의 설정 창을 띄운다. 그림 고치기를 눌러 닫았으면 그 타일 Id(이 타일셋 안의 Id).</summary>
    public static int? Run(Visual owner, EditorContext ctx, string key, Page page, int? tile = null)
    {
        if (!ctx.CurrentTilesetSources.Any(t => t.Key == key)) return null;
        var win = new TilesetSettingsWindow(ctx, key, page, tile);
        win.ShowDialog(owner);
        return win.EditImageTile;
    }

    /// <summary>고치는 타일셋(지금 레이어에서 빠졌으면 null — 창을 닫음).</summary>
    private Tileset? Ts => _ctx.CurrentTilesetSources.FirstOrDefault(t => t.Key == _key);

    /// <summary>지금 레이어에서 이 타일셋의 자리(없으면 -1).</summary>
    private int Slot => _ctx.CurrentTiles is { } d ? d.TilesetKeys.IndexOf(_key) : -1;

    private void Edit(string label, Func<Tileset, Tileset> change, Func<TileMap, int, TileMap>? remap = null) => _ctx.EditTileset(_key, label, change, remap);

    private void OnChanged(object? sender, EventArgs e) => RefreshAll();

    private CheckBox ViewCheck(string text, string tip, Action<bool> apply)
    {
        var c = Ui.Check(text).Tip(tip);
        c.IsCheckedChanged += (_, _) => { if (!_updating) apply(c.On()); };
        return c;
    }

    private void RefreshAll()
    {
        if (Ts is not { } ts)
        {
            Close();
            return;
        }
        if (!ReferenceEquals(_palette.Tileset, ts)) _palette.Tileset = ts;
        _palette.TimeMs = _ctx.TileClockMs;
        RefreshTile();
        RefreshSets(ts);
        _updating = true;
        try
        {
            if (!_tsName.IsFocused) _tsName.Text = ts.Name;
            _cellW.Value = ts.CellWidth;
            _cellH.Value = ts.CellHeight;
            _cellW.IsEnabled = _cellH.IsEnabled = ts.Tiles.Length == 0;
            _cellNote.Text = ts.Tiles.Length == 0 ? UiStrings.TilesetChoice_CellSizeInfo : UiStrings.TilesetSettings_CellLocked;
            _tsFile.Text = ts.SourcePath ?? UiStrings.Tileset_NotSaved;
            ToolTip.SetTip(_tsFile, ts.SourcePath);
            _reload.IsEnabled = ts.SourcePath is not null;
            _byFloor.IsChecked = _ctx.TileListByFloor;
            _hideAuto.IsChecked = _ctx.TileListHideAutoTiles;
            _showParts.IsChecked = _ctx.TileShowAutoParts;
            _slotAuto.IsChecked = _ctx.TileSlotSize == 0;
            _slotSize.IsEnabled = _ctx.TileSlotSize != 0;
            if (_ctx.TileSlotSize != 0) _slotSize.Value = _ctx.TileSlotSize;
        }
        finally
        {
            _updating = false;
        }
    }

    // ───────────── 타일 ─────────────

    private void RefreshTile()
    {
        if (Ts is not { } ts) return;
        var tiles = _palette.SelectedTiles;
        var one = tiles.Count == 1 ? tiles[0] : null;
        _updating = true;
        try
        {
            _singleOnly.IsEnabled = one is not null;
            _editImage.IsEnabled = _animate.IsEnabled = _duplicate.IsEnabled = one is not null;
            _delete.IsEnabled = _tileFloor.IsEnabled = tiles.Count > 0;
            _thumb.Tileset = ts;
            _thumb.Cells = one is null ? [] : [(default, new TileCell(one.Id))];
            _thumb.Icon = VectorIcon.Square;
            _thumb.InvalidateVisual();
            _tileInfo.Text = one is not null
                ? TileVisuals.TileName(one) + "\n" + string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_TileInfo, one.Id, one.Image.Width, one.Image.Height)
                : tiles.Count > 1 ? UiStrings.FormatTilesetWin_Picked(count: tiles.Count) : UiStrings.TilesetWin_PickHint;
            _tileFloor.ItemsSource = ts.Floors.Select(f => f.Name).ToList();
            var floors = tiles.Select(t => ts.ValidFloor(t.Floor)).Distinct().ToList();
            _tileFloor.SelectedIndex = floors.Count == 1 ? ts.FloorOrder(floors[0]) : -1;
            if (one is null) return;
            if (!_tileName.IsFocused) _tileName.Text = one.Name;
            _tileW.Value = one.Width;
            _tileH.Value = one.Height;
            _offX.Value = one.DrawOffset.X;
            _offY.Value = one.DrawOffset.Y;
        }
        finally
        {
            _updating = false;
        }
    }

    private TileDef? OneTile => _palette.SelectedTiles is { Count: 1 } t ? t[0] : null;

    private void ApplyOne(Func<TileDef, TileDef> change)
    {
        if (_updating || OneTile is not { } tile) return;
        var next = change(tile);
        if (next == tile) return;
        Edit(UiStrings.Tileset_TileEditLabel, ts => TilesetEdit.ReplaceTile(ts, next));
    }

    private void ApplyName() => ApplyOne(t => t with { Name = _tileName.Text?.Trim() ?? "" });

    /// <summary>차지 칸을 바꾸면 그림을 차지 칸 아래에 다시 맞춘다(나무처럼 위로 솟게).</summary>
    private void ApplyFootprint()
    {
        if (_updating || Ts is not { } ts) return;
        int w = (int)_tileW.NumValue(), h = (int)_tileH.NumValue();
        ApplyOne(t => t with { Width = w, Height = h, DrawOffset = TileDef.BottomAligned(t.Image.Height, h, ts.CellHeight) });
    }

    /// <summary>고른 타일 모두의 기본 층을 바꾼다(한 단계).</summary>
    private void ApplyFloor()
    {
        if (_updating || Ts is not { } ts || _tileFloor.SelectedIndex < 0 || _tileFloor.SelectedIndex >= ts.Floors.Length) return;
        int floor = ts.Floors[_tileFloor.SelectedIndex].Id;
        var ids = _palette.SelectedTiles.Where(t => t.Floor != floor).Select(t => t.Id).ToList();
        if (ids.Count == 0) return;
        Edit(UiStrings.Tileset_TileEditLabel, t =>
        {
            foreach (int id in ids)
                if (t.Find(id) is { } def) t = TilesetEdit.ReplaceTile(t, def with { Floor = floor });
            return t;
        });
    }

    private void EditImage()
    {
        if (OneTile is not { } tile) return;
        EditImageTile = tile.Id;
        Close();
    }

    private void Animate()
    {
        if (Ts is not { } ts || OneTile is not { } tile) return;
        if (TileAnimationDialog.Run(this, ts, tile) is not { } edited) return;
        Edit(UiStrings.Tileset_AnimateTileLabel, t => TilesetEdit.ReplaceTile(t, edited));
    }

    private void Duplicate()
    {
        if (OneTile is not { } tile) return;
        int? made = null;
        Edit(UiStrings.Tileset_DuplicateTile, ts =>
        {
            var r = TilesetEdit.DuplicateTile(ts, tile.Id);
            made = r.Id;
            return r.Tileset;
        });
        if (made is { } id)
        {
            _palette.SelectTile(id);
            RefreshTile();
        }
    }

    private void DeleteTiles()
    {
        var ids = _palette.SelectedTiles.Select(t => t.Id).ToList();
        if (ids.Count == 0 || _ctx.Project is not { } p || Ts is null) return;
        bool used = p.Layers.Any(l => l.Tiles is { } d && d.TilesetKeys.IndexOf(_key) is >= 0 and var slot
            && d.Map.All.Any(c => TileSources.Slot(c.Cell.Tile) == slot && ids.Contains(TileSources.Local(c.Cell.Tile))));
        if (used && Ui.Ask(this, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_DeleteUsedAsk, ids.Count), UiStrings.Tileset_DeleteTiles,
                MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        int offset = TileSources.Offset(Math.Max(0, Slot));
        Edit(UiStrings.Tileset_DeleteTiles, t => TilesetEdit.RemoveTiles(t, ids), (map, off) =>
        {
            foreach (var id in ids) map = map.WithoutTile(off + id);
            return map;
        });
        _palette.Selection = null;
        if (_ctx.TileStamp is { } stamp && stamp.Cells.Any(c => ids.Contains(c.Cell.Tile - offset))) _ctx.TileStamp = null;
        RefreshTile();
    }

    // ───────────── 자동 타일·무리 ─────────────

    /// <summary>세트 줄(그림·이름·층·무리·규칙 고치기·지우기)과 무리 줄을 타일셋에 맞춰 다시 만든다(같은 타일셋이면 그대로).</summary>
    private void RefreshSets(Tileset ts)
    {
        if (ReferenceEquals(_setsOf, ts)) return;
        _setsOf = ts;
        _sets.Children.Clear();
        _sets.RowDefinitions.Clear();
        _sets.ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto");
        void Put(Control c, int row, int col)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, col);
            c.VerticalAlignment = VerticalAlignment.Center;
            _sets.Children.Add(c);
        }
        _sets.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] heads = [UiStrings.TilesetChoice_Name, UiStrings.TileOptions_Floor, UiStrings.AutoTile_Group, UiStrings.TilesetWin_RulesHead];
        for (int c = 0; c < heads.Length; c++) Put(DimText(heads[c]), 0, c + 1);
        var groupNames = ts.AutoTiles.Select(a => a.Group).Where(g => g.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCulture).ToList();
        int row = 0;
        foreach (var set in ts.AutoTiles)
        {
            int id = set.Id;
            var thumb = new TileThumb(32)
            {
                Tileset = ts,
                Cells = TileVisuals.Representative(ts, set) is { } rep ? [(default, rep)] : [],
                Icon = VectorIcon.Grid3x3,
                TimeMs = _ctx.TileClockMs,
            };
            var name = Ui.Text(set.Name, 130);
            void ApplySetName()
            {
                var text = name.Text?.Trim() ?? "";
                if (text.Length > 0 && text != CurrentSet(id)?.Name) PutSet(id, s => s with { Name = text });
            }
            name.LostFocus += (_, _) => ApplySetName();
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplySetName(); };
            var floor = Ui.Combo(100, ts.Floors.Select(f => f.Name).ToArray());
            floor.SelectedIndex = Math.Max(0, ts.FloorOrder(set.Floor));
            floor.SelectionChanged += (_, _) =>
            {
                if (floor.SelectedIndex >= 0 && floor.SelectedIndex < ts.Floors.Length) PutSet(id, s => s with { Floor = ts.Floors[floor.SelectedIndex].Id });
            };
            var group = new ComboBox { IsEditable = true, Width = 150, Margin = new Thickness(3), PlaceholderText = UiStrings.AutoTile_GroupPlaceholder, Text = set.Group };
            foreach (var g in groupNames) group.Items.Add(g);
            void ApplyGroup()
            {
                var text = group.Text?.Trim() ?? "";
                if (text != CurrentSet(id)?.Group) PutSet(id, s => s with { Group = text });
            }
            group.LostFocus += (_, _) => ApplyGroup();
            group.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyGroup(); };
            group.SelectionChanged += (_, _) => { if (group.SelectedItem is string) Dispatcher.UIThread.Post(ApplyGroup); };
            var rules = Ui.Button(VectorIcon.Pencil, UiStrings.FormatTilesetWin_Rules(count: set.Rules.Length), (_, _) => EditRules(CurrentSet(id))).Tip(UiStrings.Tileset_EditAutoTile);
            var delete = Ui.IconButton(VectorIcon.Trash, UiStrings.Tileset_DeleteAutoTile, (_, _) => DeleteSet(id), size: 13);
            _sets.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            row++;
            Put(thumb, row, 0);
            Put(name, row, 1);
            Put(floor, row, 2);
            Put(group, row, 3);
            Put(rules, row, 4);
            Put(delete, row, 5);
        }
        if (ts.AutoTiles.Length == 0)
        {
            _sets.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var none = DimText(UiStrings.TilesetWin_NoSets);
            Grid.SetColumnSpan(none, 6);
            Put(none, 1, 0);
        }

        _groups.Children.Clear();
        foreach (var g in groupNames)
        {
            var members = string.Join(", ", ts.AutoTiles.Where(a => a.Group == g).Select(a => a.Name.Length > 0 ? a.Name : UiStrings.TilesetChoice_Unnamed));
            var dot = new IconView(VectorIcon.Circle, TileVisuals.GroupBrush(g) ?? Brushes.Transparent) { Width = 10, Height = 10, Filled = true, VerticalAlignment = VerticalAlignment.Center };
            var name = Ui.Text(g, 120);
            string old = g;
            void Rename()
            {
                var text = name.Text?.Trim() ?? "";
                if (text.Length == 0 || text == old) return;
                Edit(UiStrings.TilesetWin_GroupRename, t =>
                {
                    foreach (var a in t.AutoTiles.Where(a => a.Group == old).ToList()) t = TilesetEdit.PutAutoTile(t, a with { Group = text });
                    return t;
                });
            }
            name.LostFocus += (_, _) => Rename();
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(); };
            var list = DimText(members);
            list.TextTrimming = TextTrimming.CharacterEllipsis;
            list.MaxWidth = 380;
            ToolTip.SetTip(list, members);
            _groups.Children.Add(Ui.Row(dot, name, list));
        }
        if (groupNames.Count == 0) _groups.Children.Add(DimText(UiStrings.TilesetWin_NoGroups));
    }

    private static TextBlock DimText(string text)
    {
        var t = Ui.Label(text);
        t.Foreground = AppTheme.DimTextBrush;
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    private AutoTileSet? CurrentSet(int id) => Ts?.FindAutoTile(id);

    private void PutSet(int id, Func<AutoTileSet, AutoTileSet> change)
    {
        if (CurrentSet(id) is not { } set) return;
        var next = change(set);
        if (next == set) return;
        Edit(UiStrings.Tileset_EditAutoTile, t => TilesetEdit.PutAutoTile(t, next));
    }

    /// <summary>규칙 고치기 창(새 세트면 null).</summary>
    private void EditRules(AutoTileSet? set)
    {
        if (Ts is not { } ts || AutoTileEditorDialog.Run(this, ts, set) is not { } edited) return;
        Edit(set is null ? UiStrings.Tileset_AddAutoTile : UiStrings.Tileset_EditAutoTile, t => TilesetEdit.PutAutoTile(t, edited));
    }

    private void DeleteSet(int id)
    {
        if (CurrentSet(id) is not { } set) return;
        if (Ui.Ask(this, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_DeleteAutoTileAsk, set.Name), UiStrings.Tileset_DeleteAutoTile,
                MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        Edit(UiStrings.Tileset_DeleteAutoTile, t => TilesetEdit.RemoveAutoTile(t, id));
        if (Slot >= 0 && _ctx.AutoTileSet == TileSources.Global(Slot, id)) _ctx.AutoTileSet = 0;
    }

    // ───────────── 타일셋 ─────────────

    private void ApplyTilesetName()
    {
        if (_updating || Ts is not { } ts) return;
        var name = _tsName.Text?.Trim() ?? "";
        if (name.Length == 0 || name == ts.Name) return;
        Edit(UiStrings.TilesetSettings_Title, t => t with { Name = name });
    }

    private void ApplyCellSize()
    {
        if (_updating || Ts is not { Tiles.Length: 0 } ts) return;
        int w = (int)_cellW.NumValue(), h = (int)_cellH.NumValue();
        if (w == ts.CellWidth && h == ts.CellHeight) return;
        Edit(UiStrings.TilesetSettings_Title, t => t with { CellWidth = w, CellHeight = h });
    }

    // ───────────── 파일·가져오기 ─────────────

    private void SaveFile()
    {
        if (_ctx.Project is not { } p || Ts is not { } ts) return;
        var dir = ts.SourcePath is { } sp ? Path.GetDirectoryName(sp) : p.Path is { } pp ? Path.GetDirectoryName(pp) : null;
        var path = Ui.SaveFile(this, UiStrings.Tileset_SaveFile, dir, (ts.Name.Length > 0 ? ts.Name : "tileset") + Tileset.FileExtension, Tileset.FileExtension,
            new FileFilter(UiStrings.Tileset_FileFilter, "*" + Tileset.FileExtension));
        if (path is null) return;
        var full = Path.GetFullPath(path);
        try
        {
            if (!string.Equals(ts.SourcePath, full, StringComparison.Ordinal)) Edit(UiStrings.Tileset_SaveFile, t => t with { SourcePath = full });
            var saved = p.Tilesets[_key];
            TilesetIO.Save(saved, full);
            p.MarkTilesetSynced(saved);
            Ui.Info(this, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_SavedMessage, Path.GetFileName(full)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
        RefreshAll();
    }

    /// <summary>타일셋 파일 내용으로 다시 읽는다(다른 프로젝트에서 고친 것을 가져옴, 실행취소 한 단계).</summary>
    private void ReloadFile()
    {
        if (_ctx.Project is not { } p || Ts is not { SourcePath: { } path }) return;
        try
        {
            var loaded = TilesetIO.Load(path) with { Key = _key };
            Edit(UiStrings.Tileset_ReloadFile, _ => loaded);
            p.MarkTilesetSynced(loaded);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
    }

    private void ImportFromFile()
    {
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
        if (_ctx.Project is not { } p) return;
        Import(p.PlanFrame(_ctx.CurrentFrame, layerFilter: l => !l.IsTilemap).Render());
    }

    private void Import(PixelImage sheet)
    {
        if (Ts is not { } ts || TileImportDialog.Run(this, ts, sheet) is not { } result) return;
        if (result.Added == 0)
        {
            Ui.Info(this, UiStrings.Tileset_ImportNone);
            return;
        }
        Edit(UiStrings.Tileset_ImportLabel, _ => result.Tileset);
        Ui.Info(this, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_ImportedMessage, result.Added));
    }
}
