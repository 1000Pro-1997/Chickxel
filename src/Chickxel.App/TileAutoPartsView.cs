using Chickxel.App.Localization;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 자동 타일 세트의 구성(세트 규칙들에 든 타일): 도구 설정의 타일 묶음에서 '지금 찍을 것' 카드 아래에 보인다.
/// 자동 타일 붓으로 세트를 쓰는 중이거나 지금 붓이 그 세트의 구성 타일 한 장일 때만 보이고(보기 설정 "구성 보기"를 끄면 숨김),
/// 칸을 누르면 그 타일 한 장을 세트의 층에 그대로 찍는 붓에 든다(자동으로 고른 모양을 손으로 바꿀 때).
/// </summary>
internal sealed class TileAutoPartsView : StackPanel
{
    private readonly EditorContext _ctx;
    private readonly TextBlock _title;
    private readonly TileSlotsView _slots = new() { SlotSize = 32 };
    /// <summary>칸에 보이는 타일 Id(칸 차례)와 그 구성을 만든 타일셋·세트.</summary>
    private readonly List<int> _tiles = [];
    private (Tileset Tileset, AutoTileSet Set)? _of;

    public TileAutoPartsView(EditorContext ctx)
    {
        _ctx = ctx;
        Spacing = 2;
        Margin = new Thickness(0, 4, 0, 0);
        _title = new TextBlock { Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, VerticalAlignment = VerticalAlignment.Center };
        _slots.Clicked += OnClicked;
        Children.Add(Ui.Row(_title, Ui.InfoBadge(UiStrings.Tileset_AutoPartsInfo)));
        Children.Add(_slots);
        ctx.TileStateChanged += (_, _) => Refresh();
        ctx.CurrentChanged += (_, _) => Refresh();
        ctx.ProjectReplaced += (_, _) => Refresh();
        ctx.ProjectChanged += (_, _) => Refresh();
        ctx.TileClockTick += () =>
        {
            if (IsEffectivelyVisible && _ctx.CurrentTileset is { HasAnimation: true }) _slots.TimeMs = _ctx.TileClockMs;
        };
        Refresh();
    }

    private void Refresh()
    {
        if (!_ctx.IsTilemapSelected || _ctx.CurrentTileset is not { } ts || ts.FindAutoTile(_ctx.AutoTileSet) is not { } set || !_ctx.TileShowAutoParts)
        {
            IsVisible = false;
            return;
        }
        if (_of is not { } of || !ReferenceEquals(of.Tileset, ts) || !ReferenceEquals(of.Set, set))
        {
            _of = (ts, set);
            _tiles.Clear();
            foreach (var rule in set.Rules)
                foreach (var choice in rule.Tiles)
                    if (!_tiles.Contains(choice.Tile) && ts.Find(choice.Tile) is not null) _tiles.Add(choice.Tile);
            var slots = new List<TileSlotsView.Slot>(_tiles.Count);
            foreach (int id in _tiles)
                slots.Add(new TileSlotsView.Slot([(default, new TileCell(id))], null, TileVisuals.TileName(ts.Find(id)!) + "\n" + UiStrings.Tileset_AutoPartTip));
            _title.Text = UiStrings.FormatTileset_AutoParts(count: _tiles.Count);
            _slots.Tileset = ts;
            _slots.Slots = slots;
            _slots.TimeMs = _ctx.TileClockMs;
        }
        // 지금 붓이 구성 타일 한 장이면 그 칸을 고름
        int picked = _ctx.TileStamp is { Cells.Count: 1 } stamp ? _tiles.IndexOf(stamp.Cells[0].Cell.Tile) : -1;
        bool stamping = !_ctx.UsesAutoTileSource && _ctx.TileTool is TileTool.Brush or TileTool.Rect or TileTool.Fill;
        IsVisible = _ctx.TileTool == TileTool.AutoTile || (picked >= 0 && stamping);
        _slots.Selected = picked;
        _slots.SelectionActive = stamping;
    }

    /// <summary>구성 타일을 누르면 그 타일 한 장을 세트의 층으로 찍는 붓에 든다(자동 타일 모양을 손으로 바꿔 찍기).</summary>
    private void OnClicked(int index)
    {
        if (index < 0 || index >= _tiles.Count || _ctx.CurrentTileset is not { } ts || ts.FindAutoTile(_ctx.AutoTileSet) is not { } set) return;
        _ctx.TileTransform = TileTransform.None;
        _ctx.TileStamp = new TileStamp([new StampCell(0, 0, new TileCell(_tiles[index]), set.Floor)]);
        if (_ctx.TileTool is not (TileTool.Brush or TileTool.Rect or TileTool.Fill)) _ctx.TileTool = TileTool.Brush;
    }
}
