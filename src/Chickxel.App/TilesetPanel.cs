using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 타일셋 패널: 타일맵 레이어를 고르면 왼쪽 색 패널 자리에 보인다. 찍을 것을 고르는 곳이다 — 팔레트처럼 레이어에 연 타일셋마다 묶음 하나:
/// 머리글(접기/펼치기 화살표·이름·타일 수·메뉴 — 설정·다른 타일셋으로 바꾸기·이름 바꾸기·이 레이어에서 닫기, 두 번 누르면 이름 바꾸기),
/// 자동 타일 세트(칸 — 누르면 자동 타일 붓), 타일 목록(고르면 타일 붓에 듦 — 보기 설정에 따라 층별로 나누고 자동 타일에 쓴 타일은 감춤).
/// 맨 아래 "타일셋 더하기"로 타일셋을 더 연다(칸 크기가 같은 것). 닫은 타일셋은 프로젝트에 남아 다시 열 수 있다(타일셋 관리 창에서 지움).
/// 둘째 타일셋부터의 타일·세트는 붓에 들 때 자리만큼 밀린 Id(<see cref="TileSources"/>)가 된다. 고른 자동 타일 세트의 구성은 도구 설정에 있다.
/// 타일셋을 고치는 것은 모두 실행취소 한 단계씩이고, 파일 경로가 있으면 프로젝트를 저장할 때 타일셋 파일에도 쓴다.
/// </summary>
public sealed class TilesetPanel : UserControl
{
    private readonly EditorContext _ctx;
    private readonly StackPanel _empty;
    private readonly StackPanel _sectionHost = new() { Spacing = 2 };
    private readonly Button _addTileset;
    private readonly List<Section> _sections = [];
    /// <summary>타일 목록에서 고른 칸으로 만든 붓과 그 묶음(지금 붓이 이것일 때만 그 묶음의 고른 칸을 강조).</summary>
    private (TileStamp Stamp, Section Section)? _paletteStamp;
    /// <summary>접어 둔 타일셋(이름표, 화면에서만).</summary>
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    /// <summary>상태 표시줄에 보일 알림.</summary>
    public event EventHandler<string>? Message;

    /// <summary>타일 그림을 문서 탭에서 고치기(타일셋 이름표, 그 타일셋 안의 타일 Id) — 메인 창이 연다.</summary>
    public event Action<string, int>? EditTileImageRequested;

    public TilesetPanel(EditorContext ctx)
    {
        _ctx = ctx;
        var header = Ui.Header(UiStrings.Tileset_Header);

        // 타일셋이 없을 때(이름표가 프로젝트에 없는 레이어)
        var note = Ui.Label(UiStrings.Tile_NoTileset);
        note.TextWrapping = TextWrapping.Wrap;
        _empty = Ui.Stack(note, Ui.Button(VectorIcon.Plus, UiStrings.Tileset_ChooseOrCreate, (_, _) => ChooseTileset(0)));

        _addTileset = Ui.Button(VectorIcon.Plus, UiStrings.Tileset_AddTileset, (_, _) => AddTileset()).Tip(UiStrings.Tileset_AddTilesetTip);
        _addTileset.HorizontalAlignment = HorizontalAlignment.Stretch;
        _addTileset.HorizontalContentAlignment = HorizontalAlignment.Center;
        _addTileset.Margin = new Thickness(4, 6, 4, 8);
        _addTileset.Focusable = false;

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(Ui.ScrollHost(Ui.Stack(_empty, _sectionHost, _addTileset)));
        Content = root;

        ctx.ProjectReplaced += (_, _) => Refresh();
        ctx.ProjectChanged += (_, _) => Refresh();
        ctx.CurrentChanged += (_, _) => Refresh();
        ctx.TileStateChanged += (_, _) => Refresh();
        ctx.TileClockTick += OnTileClock;
        SizeChanged += (_, _) => { foreach (var s in _sections) s.FitSlots(Bounds.Width); };
        Refresh();
    }

    private void Say(string text) => Message?.Invoke(this, text);

    // ───────────── 상태 → 화면 ─────────────

    private void Refresh()
    {
        if (!IsVisible && !_ctx.IsTilemapSelected) return;
        var sources = _ctx.CurrentTilesetSources;
        _empty.IsVisible = sources.Count == 0 && _ctx.IsTilemapSelected;
        _addTileset.IsVisible = sources.Count > 0;
        while (_sections.Count > sources.Count)
        {
            var last = _sections[^1];
            _sections.RemoveAt(_sections.Count - 1);
            _sectionHost.Children.Remove(last);
        }
        while (_sections.Count < sources.Count)
        {
            var s = new Section(this);
            _sections.Add(s);
            _sectionHost.Children.Add(s);
        }
        if (_paletteStamp is { } ps && (!ReferenceEquals(_ctx.TileStamp, ps.Stamp) || !_sections.Contains(ps.Section))) _paletteStamp = null;
        for (int i = 0; i < sources.Count; i++) _sections[i].Update(i, sources[i]);
    }

    /// <summary>화면 시계: 움직이는 타일이 있으면 목록·자동 타일 그림을 돌린다.</summary>
    private void OnTileClock()
    {
        if (!IsEffectivelyVisible || _ctx.CurrentTileset is not { HasAnimation: true }) return;
        foreach (var s in _sections) s.SetTime(_ctx.TileClockMs);
    }

    // ───────────── 타일셋 고르기·더하기 ─────────────

    /// <summary>자리 <paramref name="slot"/>의 타일셋을 다른 것으로 바꾼다(맵 칸은 그대로 — 같은 Id의 타일로 보임).</summary>
    private void ChooseTileset(int slot)
    {
        if (_ctx.Project is not { } p || _ctx.CurrentTilemap is not { } layer) return;
        var sources = _ctx.CurrentTilesetSources;
        // 타일셋이 여럿이면 어느 자리든 다른 타일셋과 칸 크기가 같아야 함
        var other = sources.Count > 1 ? sources[slot == 0 ? 1 : 0] : null;
        Size? cell = other is not null ? new Size(other.CellWidth, other.CellHeight) : null;
        var open = sources.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        if (TilesetChoiceDialog.Run(this, _ctx, UiStrings.Tileset_Replace, open, cell) is not { } ts) return;
        _ctx.Structural(() => p.SetLayerTileset(layer, slot, ts));
        if (ts.SourcePath is not null) p.MarkTilesetSynced(ts);
        _ctx.TileStamp = null;
    }

    /// <summary>타일셋을 하나 더 연다(칸 크기가 첫 타일셋과 같은 것만).</summary>
    private void AddTileset()
    {
        if (_ctx.Project is not { } p || _ctx.CurrentTilemap is not { } layer || _ctx.CurrentTilesetSources is not { Count: > 0 } sources) return;
        var open = sources.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        if (TilesetChoiceDialog.Run(this, _ctx, UiStrings.Tileset_AddTileset, open, new Size(sources[0].CellWidth, sources[0].CellHeight)) is not { } ts) return;
        _ctx.Structural(() => p.AddLayerTileset(layer, ts));
        if (ts.SourcePath is not null) p.MarkTilesetSynced(ts);
    }

    /// <summary>자리 <paramref name="slot"/>의 타일셋을 이 레이어에서 닫는다(그 타일셋으로 찍은 칸도 빠짐 — 있으면 묻고, 타일셋은 프로젝트에 남음).</summary>
    private void CloseTileset(int slot)
    {
        if (_ctx.Project is not { } p || _ctx.CurrentTilemap is not { } layer || _ctx.CurrentTilesetSources.Count <= 1) return;
        int used = layer.Tiles!.Map.All.Count(c => TileSources.Slot(c.Cell) == slot);
        if (used > 0 && Ui.Ask(this, UiStrings.FormatTileset_CloseAsk(count: used), UiStrings.Tileset_Close,
                MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        _ctx.Structural(() => p.RemoveLayerTileset(layer, slot));
        _ctx.TileStamp = null;
        _ctx.AutoTileSet = 0;
    }

    /// <summary>타일셋 이름을 바꾼다(실행취소 한 단계).</summary>
    private void RenameTileset(Tileset ts)
    {
        var name = InputDialog.Ask(this, UiStrings.Tileset_Rename, UiStrings.TilesetChoice_Name, ts.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == ts.Name) return;
        _ctx.EditTileset(ts.Key, UiStrings.Tileset_Rename, t => t with { Name = name.Trim() });
    }

    /// <summary>타일셋 <paramref name="key"/>의 설정 창(그림 고치기를 눌러 닫았으면 그 타일을 문서 탭으로).</summary>
    private void OpenSettings(string key, TilesetSettingsWindow.Page page, int? tile = null)
    {
        if (TilesetSettingsWindow.Run(this, _ctx, key, page, tile) is { } edit) EditTileImageRequested?.Invoke(key, edit);
    }

    /// <summary>
    /// 레이어에 연 타일셋 하나의 묶음(팔레트 묶음처럼): 머리글(접기 화살표·이름·타일 수·메뉴), 자동 타일 세트 칸, 타일 목록.
    /// 타일 Id·세트 Id는 이 타일셋 안의 것이고, 붓에 넣을 때 자리만큼 민다.
    /// </summary>
    private sealed class Section : StackPanel
    {
        private readonly TilesetPanel _owner;
        private readonly PaletteSection _head;
        private readonly StackPanel _body;
        private readonly TilePaletteView _palette = new();
        private readonly TileSlotsView _autoView = new();
        /// <summary>자동 타일 칸에 보이는 세트(칸 차례, 끝 칸은 '세트 만들기').</summary>
        private readonly List<AutoTileSet> _autoSets = [];
        // 자동 타일 세트에 쓴 타일(목록에서 감출 것)과 그 타일셋
        private (Tileset Tileset, HashSet<int> Tiles)? _autoTiles;
        private Tileset? _ts;
        private int _slot;

        private EditorContext Ctx => _owner._ctx;

        public Section(TilesetPanel owner)
        {
            _owner = owner;
            // 머리글은 팔레트 묶음과 같은 모양(색 칸 자리는 쓰지 않음)
            _head = new PaletteSection("", [], false, editable: true);
            _head.Grid.IsVisible = false;
            _head.Children.Remove(_head.Grid);
            _head.Margin = new Thickness(0);
            ToolTip.SetTip(_head.Title, UiStrings.Tileset_HeaderTip);
            _head.ArrowButton.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(_head.ArrowButton).Properties.IsLeftButtonPressed || _ts is not { } ts) return;
                e.Handled = true;
                if (!_owner._collapsed.Remove(ts.Key)) _owner._collapsed.Add(ts.Key);
                ApplyCollapsed();
            };
            _head.Header.DoubleTapped += (_, e) =>
            {
                if (ReferenceEquals(e.Source, _head.ArrowButton) || _ts is not { } ts) return;
                e.Handled = true;
                Dispatcher.UIThread.Post(() => _owner.RenameTileset(ts));
            };
            PopupMenus.Attach(_head.Header, () => HeaderMenu());
            _head.MenuButton.Click += (_, _) => PopupMenus.Show(_head.MenuButton, HeaderMenu(), PlacementMode.BottomEdgeAlignedLeft);

            // 자동 타일 세트 칸(끝 칸 = 세트 만들기)
            _autoView.Clicked += OnAutoClicked;
            _autoView.Activated += i => { if (i < _autoSets.Count) EditAutoTile(_autoSets[i]); };
            _autoView.MenuRequested += ShowAutoMenu;
            var autoLabel = Ui.Label(UiStrings.Tileset_AutoTiles);
            autoLabel.Foreground = AppTheme.DimTextBrush;
            autoLabel.FontSize = AppTheme.SmallFontSize;

            // 타일 목록(층 이름 줄이 목록 안에 있음)
            _palette.SelectionChanged += OnPaletteSelection;
            _palette.TileActivated += _ => Ctx.TileTool = TileTool.Brush;
            _palette.TileMenuRequested += ShowTileMenu;
            var paletteScroll = new ScrollViewer
            {
                Content = _palette,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 320,
                Margin = new Thickness(0, 4, 0, 0),
            };
            _body = new StackPanel
            {
                Spacing = 2,
                Margin = new Thickness(2, 4, 2, 6),
                Children = { Ui.Row(autoLabel, Ui.InfoBadge(UiStrings.Tileset_AutoTilesInfo)), _autoView, paletteScroll },
            };
            Children.Add(_head);
            Children.Add(_body);
        }

        /// <summary>머리글 메뉴: 설정, 다른 타일셋으로 바꾸기, 이름 바꾸기, 이 레이어에서 닫기(하나뿐이면 못 닫음).</summary>
        private object[] HeaderMenu()
        {
            MenuItem Item(string text, VectorIcon? icon, Action action, bool enabled = true)
            {
                var mi = new MenuItem { Header = text, IsEnabled = enabled, Icon = icon is { } i ? Ui.Icon(i, 14) : null };
                mi.Click += (_, _) => Dispatcher.UIThread.Post(action);
                return mi;
            }
            if (_ts is not { } ts) return [];
            int slot = _slot;
            return
            [
                Item(UiStrings.Tileset_TileSettings, VectorIcon.Settings, () => _owner.OpenSettings(ts.Key, TilesetSettingsWindow.Page.Tiles, SingleSelected())),
                Item(UiStrings.Tileset_Replace, VectorIcon.Refresh, () => _owner.ChooseTileset(slot)),
                Item(UiStrings.Tileset_Rename, VectorIcon.Pencil, () => _owner.RenameTileset(ts)),
                new Separator(),
                Item(UiStrings.Tileset_Close, VectorIcon.X, () => _owner.CloseTileset(slot), Ctx.CurrentTilesetSources.Count > 1),
            ];
        }

        private void ApplyCollapsed()
        {
            if (_ts is not { } ts) return;
            bool collapsed = _owner._collapsed.Contains(ts.Key);
            _head.Collapsed = collapsed;
            _head.Refresh(IsActive);
            _head.Count.Text = ts.Tiles.Length.ToString(System.Globalization.CultureInfo.CurrentCulture);
            _body.IsVisible = !collapsed;
        }

        /// <summary>지금 붓이나 자동 타일 세트가 이 타일셋 것인지(머리글 아래 강조선).</summary>
        private bool IsActive
            => Ctx.UsesAutoTileSource ? TileSources.Slot(Ctx.AutoTileSet) == _slot
               : Ctx.TileStamp is { Cells.Count: > 0 } s && s.Cells.All(c => TileSources.Slot(c.Cell.Tile) == _slot);

        /// <summary>자리 <paramref name="slot"/>의 타일셋 <paramref name="ts"/>로 맞춘다(같은 타일셋이면 목록은 두고 고른 표시만).</summary>
        public void Update(int slot, Tileset ts)
        {
            bool changed = !ReferenceEquals(_ts, ts) || _slot != slot;
            _slot = slot;
            _ts = ts;
            if (changed)
            {
                _head.PaletteName = ts.Name.Length > 0 ? ts.Name : UiStrings.TilesetChoice_Unnamed;
                if (!ReferenceEquals(_palette.Tileset, ts)) _palette.Tileset = ts;
                RefreshAutoSlots(ts);
            }
            ApplyCollapsed();
            ToolTip.SetTip(_head.Count, UiStrings.FormatTilesetChoice_Summary(w: ts.CellWidth, h: ts.CellHeight, tiles: ts.Tiles.Length, sets: ts.AutoTiles.Length));
            ApplyListView(ts);
            FitSlots(_owner.Bounds.Width);
            SyncSelection();
        }

        public void SetTime(long now)
        {
            if (_ts is not { HasAnimation: true }) return;
            _palette.TimeMs = now;
            _autoView.TimeMs = now;
        }

        /// <summary>타일 목록 보기 설정(층별로 나누기, 자동 타일 세트에 쓴 타일 감추기)을 목록에 입힌다.</summary>
        private void ApplyListView(Tileset ts)
        {
            _palette.GroupByFloor = Ctx.TileListByFloor;
            if (!Ctx.TileListHideAutoTiles)
            {
                _palette.HiddenTiles = null;
                return;
            }
            if (_autoTiles is not { } cached || !ReferenceEquals(cached.Tileset, ts))
            {
                var ids = new HashSet<int>();
                foreach (var set in ts.AutoTiles) ids.UnionWith(TileVisuals.TilesOf(set));
                _autoTiles = cached = (ts, ids);
            }
            _palette.HiddenTiles = cached.Tiles;
        }

        /// <summary>칸 크기: 보기 설정의 칸 크기, 자동이면 패널 폭에 타일 목록 열이 다 들어가게(18~40px). 자동 타일 칸도 같은 크기. 목록은 폭이 모자라면 더 줄여 그린다.</summary>
        public void FitSlots(double panelWidth)
        {
            if (_ts is not { } ts) return;
            double size = Ctx.TileSlotSize;
            if (size <= 0)
            {
                int cols = Math.Max(_palette.MinColumns, ts.Tiles.Length == 0 ? 0 : ts.Tiles.Max(t => t.PaletteX) + 1);
                double width = panelWidth - 40;
                size = width > 0 ? Math.Clamp(Math.Floor(width / cols), 18, 40) : 32;
            }
            if (Math.Abs(size - _palette.SlotSize) < 0.5 && Math.Abs(size - _autoView.SlotSize) < 0.5) return;
            _palette.SlotSize = size;
            _autoView.SlotSize = size;
            foreach (var view in new Control[] { _palette, _autoView })
            {
                view.InvalidateMeasure();
                view.InvalidateVisual();
            }
        }

        /// <summary>자동 타일 칸을 타일셋의 세트로 다시 만든다(끝 칸 = 세트 만들기).</summary>
        private void RefreshAutoSlots(Tileset ts)
        {
            _autoSets.Clear();
            _autoSets.AddRange(ts.AutoTiles);
            var slots = new List<TileSlotsView.Slot>(_autoSets.Count + 1);
            foreach (var set in _autoSets)
            {
                var name = set.Name.Length > 0 ? set.Name : UiStrings.TilesetChoice_Unnamed;
                var floor = ts.FindFloor(set.Floor)?.Name ?? "?";
                var tip = string.Join("\n", name, AutoTileEditorDialog.Summary(set), UiStrings.FormatTilePick_Floor(name: floor), UiStrings.Tileset_AutoTileRowTip);
                slots.Add(new TileSlotsView.Slot(TileVisuals.Representative(ts, set) is { } rep ? [(default, rep)] : [], VectorIcon.Grid3x3, tip, TileVisuals.GroupBrush(set.Group)));
            }
            slots.Add(new TileSlotsView.Slot([], VectorIcon.Plus, UiStrings.Tileset_AddAutoTile));
            _autoView.Tileset = ts;
            _autoView.Slots = slots;
            _autoView.TimeMs = Ctx.TileClockMs;
        }

        /// <summary>
        /// 고른 표시를 지금 찍을 것에 맞춘다: 이 타일셋의 세트를 쓰면 그 칸을 고르고 목록에서 그 세트의 타일을 옅게, 목록의 고른 칸은 지금 붓이 이 목록에서 든 것일 때만 강조.
        /// </summary>
        private void SyncSelection()
        {
            int global = Ctx.AutoTileSet;
            var set = TileSources.Slot(global) == _slot ? _ts?.FindAutoTile(TileSources.Local(global)) : null;
            bool autoActive = Ctx.TileTool != TileTool.Eraser && Ctx.UsesAutoTileSource;
            _autoView.Selected = set is null ? -1 : _autoSets.FindIndex(a => a.Id == set.Id);
            _autoView.SelectionActive = autoActive;
            _palette.HighlightTiles = autoActive && set is not null ? TileVisuals.TilesOf(set) : null;
            bool mine = _owner._paletteStamp is { } ps && ReferenceEquals(ps.Section, this) && ReferenceEquals(Ctx.TileStamp, ps.Stamp);
            if (!mine && _palette.Selection is not null && _owner._paletteStamp is { } other && !ReferenceEquals(other.Section, this)) _palette.Selection = null;
            _palette.SelectionActive = mine && !autoActive && Ctx.TileTool != TileTool.Eraser;
            _palette.TimeMs = Ctx.TileClockMs;
        }

        private int? SingleSelected() => _palette.SelectedTiles is { Count: 1 } t ? t[0].Id : null;

        // ───────────── 자동 타일 ─────────────

        private void OnAutoClicked(int index)
        {
            if (index >= _autoSets.Count)
            {
                EditAutoTile(null);
                return;
            }
            Ctx.AutoTileSet = TileSources.Global(_slot, _autoSets[index].Id);
            Ctx.TileTool = TileTool.AutoTile;
        }

        private void ShowAutoMenu(int index, PointerPressedEventArgs e)
        {
            if (index >= _autoSets.Count) return;
            var set = _autoSets[index];
            var edit = new MenuItem { Header = UiStrings.Tileset_EditAutoTile, Icon = Ui.Icon(VectorIcon.Pencil, 14) };
            edit.Click += (_, _) => Dispatcher.UIThread.Post(() => EditAutoTile(set));
            var delete = new MenuItem { Header = UiStrings.Tileset_DeleteAutoTile, Icon = Ui.Icon(VectorIcon.Trash, 14) };
            delete.Click += (_, _) => Dispatcher.UIThread.Post(() => DeleteAutoTile(set));
            new ContextMenu { Items = { edit, new Separator(), delete } }.Open(_autoView);
        }

        private void EditAutoTile(AutoTileSet? set)
        {
            if (_ts is not { } ts) return;
            if (AutoTileEditorDialog.Run(_owner, ts, set) is not { } edited) return;
            Ctx.EditTileset(ts.Key, set is null ? UiStrings.Tileset_AddAutoTile : UiStrings.Tileset_EditAutoTile, t => TilesetEdit.PutAutoTile(t, edited));
            Ctx.AutoTileSet = TileSources.Global(_slot, edited.Id);
            Ctx.TileTool = TileTool.AutoTile;
        }

        private void DeleteAutoTile(AutoTileSet set)
        {
            if (_ts is not { } ts) return;
            if (Ui.Ask(_owner, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_DeleteAutoTileAsk, set.Name), UiStrings.Tileset_DeleteAutoTile,
                    MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
            int global = TileSources.Global(_slot, set.Id);
            Ctx.EditTileset(ts.Key, UiStrings.Tileset_DeleteAutoTile, t => TilesetEdit.RemoveAutoTile(t, set.Id));
            if (Ctx.AutoTileSet == global) Ctx.AutoTileSet = 0;
        }

        // ───────────── 타일 목록 ─────────────

        /// <summary>목록에서 칸을 고르면 그 타일들(화면 자리대로, 자리만큼 민 Id)을 붓에 든다.</summary>
        private void OnPaletteSelection(Rectangle? sel)
        {
            var tiles = _palette.SelectedTiles;
            if (sel is not { } r || tiles.Count == 0) return;
            var cells = tiles.Select(t =>
            {
                var at = _palette.DisplayPos(t);
                return new StampCell(at.X - r.X, at.Y - r.Y, new TileCell(TileSources.Global(_slot, t.Id)), null);
            }).ToList();
            var stamp = new TileStamp(cells);
            _owner._paletteStamp = (stamp, this);
            Ctx.TileTransform = TileTransform.None;
            Ctx.TileStamp = stamp;
            if (Ctx.TileTool is not (TileTool.Brush or TileTool.Rect or TileTool.Fill)) Ctx.TileTool = TileTool.Brush;
        }

        /// <summary>타일 오른쪽 클릭 메뉴(<paramref name="clicked"/> = 누른 칸의 타일).</summary>
        private void ShowTileMenu(int? clicked, PointerPressedEventArgs e)
        {
            if (_ts is not { } ts) return;
            var tiles = _palette.SelectedTiles;
            int? one = clicked ?? (tiles.Count == 1 ? tiles[0].Id : null);
            var settings = new MenuItem { Header = UiStrings.Tileset_TileSettings, Icon = Ui.Icon(VectorIcon.Settings, 14) };
            settings.Click += (_, _) => Dispatcher.UIThread.Post(() => _owner.OpenSettings(ts.Key, TilesetSettingsWindow.Page.Tiles, one));
            var image = new MenuItem { Header = UiStrings.TilesetWin_EditImage, IsEnabled = one is not null, Icon = Ui.Icon(VectorIcon.Pencil, 14) };
            image.Click += (_, _) => Dispatcher.UIThread.Post(() => { if (one is { } id) _owner.EditTileImageRequested?.Invoke(ts.Key, id); });
            var animate = new MenuItem { Header = UiStrings.Tileset_AnimateTile, IsEnabled = one is not null, Icon = Ui.Icon(VectorIcon.Film, 14) };
            animate.Click += (_, _) => Dispatcher.UIThread.Post(() => { if (one is { } id) AnimateTile(id); });
            var dup = new MenuItem { Header = UiStrings.Tileset_DuplicateTile, IsEnabled = tiles.Count == 1 };
            dup.Click += (_, _) =>
            {
                if (tiles.Count != 1) return;
                Ctx.EditTileset(ts.Key, UiStrings.Tileset_DuplicateTile, t => TilesetEdit.DuplicateTile(t, tiles[0].Id).Tileset);
            };
            bool canMerge = _palette.Selection is { } sel && (sel.Width > 1 || sel.Height > 1) && tiles.Count > 0 && _palette.PaletteRect(sel) is not null;
            var merge = new MenuItem { Header = UiStrings.Tileset_Merge, IsEnabled = canMerge };
            merge.Click += (_, _) => MergeSelection();
            ToolTip.SetTip(merge, UiStrings.Tileset_MergeTip);
            var delete = new MenuItem { Header = UiStrings.Tileset_DeleteTiles, IsEnabled = tiles.Count > 0, Icon = Ui.Icon(VectorIcon.Trash, 14) };
            delete.Click += (_, _) => Dispatcher.UIThread.Post(DeleteSelectedTiles);
            new ContextMenu { Items = { settings, image, animate, new Separator(), dup, merge, new Separator(), delete } }.Open(_palette);
        }

        /// <summary>움직이는 타일 창: 타일의 그림들·장당 시간을 고친다(실행취소 한 단계).</summary>
        private void AnimateTile(int id)
        {
            if (_ts is not { } ts || ts.Find(id) is not { } def) return;
            if (TileAnimationDialog.Run(_owner, ts, def) is not { } edited) return;
            Ctx.EditTileset(ts.Key, UiStrings.Tileset_AnimateTileLabel, t => TilesetEdit.ReplaceTile(t, edited));
        }

        private void MergeSelection()
        {
            if (_ts is not { } ts || _palette.Selection is not { } display || _palette.PaletteRect(display) is not { } sel) return;
            int? made = null;
            Ctx.EditTileset(ts.Key, UiStrings.Tileset_Merge, t =>
            {
                if (TilesetEdit.MergeToBigTile(t, sel) is not { } r) return t;
                made = r.Id;
                return r.Tileset;
            });
            if (made is { } id && _ts?.Find(id) is { } big)
            {
                _palette.SelectTile(big.Id);
                OnPaletteSelection(_palette.Selection);
                _owner.Say(UiStrings.Tileset_MergedMessage);
            }
        }

        private void DeleteSelectedTiles()
        {
            var ids = _palette.SelectedTiles.Select(t => t.Id).ToList();
            if (ids.Count == 0 || Ctx.Project is not { } p || _ts is not { } ts) return;
            // 맵에 쓰인 타일이면 묻는다(지우면 맵에서도 빠짐).
            bool used = p.Layers.Any(l => l.Tiles is { } d && d.TilesetKeys.IndexOf(ts.Key) is >= 0 and var slot
                && d.Map.All.Any(c => TileSources.Slot(c.Cell.Tile) == slot && ids.Contains(TileSources.Local(c.Cell.Tile))));
            if (used && Ui.Ask(_owner, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_DeleteUsedAsk, ids.Count), UiStrings.Tileset_DeleteTiles,
                    MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
            Ctx.EditTileset(ts.Key, UiStrings.Tileset_DeleteTiles, t => TilesetEdit.RemoveTiles(t, ids), (map, offset) =>
            {
                foreach (var id in ids) map = map.WithoutTile(offset + id);
                return map;
            });
            _palette.Selection = null;
            Ctx.TileStamp = null;
        }
    }
}
