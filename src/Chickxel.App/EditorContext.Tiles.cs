using Chickxel.App.Localization;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>타일맵 레이어를 골랐을 때의 도구(도구 막대가 이것으로 바뀐다).</summary>
public enum TileTool
{
    /// <summary>타일 붓: 고른 타일(여러 칸 묶음)을 칸에 맞춰 찍는다. 오른쪽 버튼 = 지우기.</summary>
    Brush,
    /// <summary>지우개: 칸을 덮은 타일을 지운다(큰 타일은 통째로).</summary>
    Eraser,
    /// <summary>사각형: 끌어 그린 칸 사각형을 고른 타일로 채운다(오른쪽 버튼 = 지우기).</summary>
    Rect,
    /// <summary>채우기: 같은 타일(또는 빈 칸)로 이어진 칸을 고른 타일로 채운다.</summary>
    Fill,
    /// <summary>스포이드: 맵에서 타일을 집어 붓에 든다.</summary>
    Picker,
    /// <summary>칸 선택: 칸 사각형을 고른다(복사하면 그 칸들이 붓이 됨, Delete = 지우기).</summary>
    Select,
    /// <summary>자동 타일 붓: 고른 자동 타일 세트로 칠하면 이웃을 보고 모양을 고른다. 오른쪽 버튼 = 지우기.</summary>
    AutoTile,
}

/// <summary>붓에 든 칸 하나: 붓 기준 칸(왼쪽 위 = 0, 0)에서의 자리, 칸, 층(null = 타일의 기본 층).</summary>
public readonly record struct StampCell(int Dx, int Dy, TileCell Cell, int? Floor);

/// <summary>타일 붓: 칸 여러 개 묶음(타일 목록에서 고르거나 맵에서 복사한 것).</summary>
public sealed class TileStamp(IReadOnlyList<StampCell> cells)
{
    public IReadOnlyList<StampCell> Cells { get; } = cells;

    public static TileStamp Single(int tile) => new([new StampCell(0, 0, new TileCell(tile), null)]);

    /// <summary>모든 칸의 차지 칸을 감싼 칸 사각형(붓 기준).</summary>
    public Rectangle Bounds(Tileset tileset)
    {
        var r = Rectangle.Empty;
        foreach (var c in Cells)
        {
            var cell = new Rectangle(new Point(c.Dx, c.Dy), tileset.Footprint(c.Cell));
            r = r.IsEmpty ? cell : Rectangle.Union(r, cell);
        }
        return r;
    }

    /// <summary>붓 전체를 돌리고 뒤집은 것(칸 자리와 칸마다의 뒤집기·회전을 함께). 정사각형 칸이 아니면 회전은 뺀다.</summary>
    public TileStamp Transformed(Tileset tileset, TileTransform transform)
    {
        transform = tileset.Allowed(transform);
        if (transform == TileTransform.None) return this;
        var box = Bounds(tileset);
        var list = new List<StampCell>(Cells.Count);
        foreach (var c in Cells)
        {
            var rect = new Rectangle(c.Dx - box.X, c.Dy - box.Y, 0, 0) with { Size = tileset.Footprint(c.Cell) };
            var (moved, _) = transform.ApplyToRect(rect, box.Size);
            list.Add(c with { Dx = moved.X, Dy = moved.Y, Cell = c.Cell with { Transform = c.Cell.Transform.Then(transform) } });
        }
        return new TileStamp(list);
    }
}

// 타일맵 레이어 편집: 타일 도구 상태(붓·뒤집기·층·무작위·자동 타일 세트·칸 선택)와 찍기·지우기·채우기·스포이드.
public sealed partial class EditorContext
{
    private TileTool _tileTool = TileTool.Brush;
    private TileStamp? _tileStamp;
    private TileTransform _tileTransform;
    private int? _tileFloor;
    private bool _tileRandom;
    private int _autoTileSet;
    private Rectangle? _tileSelection;
    private bool _showTileCells = true;
    // 사각형·채우기가 쓸 것: 마지막으로 고른 것이 자동 타일 세트면 true, 붓(타일)이면 false
    private bool _autoSource;

    // 끌어 찍기: 시작 칸(여러 칸 붓은 이 칸에서 붓 크기마다 찍음), 이미 찍은 칸, 무작위 씨앗
    private Point _tileStrokeStart;
    private readonly HashSet<Point> _tileStrokeDone = new();
    private int _tileStrokeSalt;
    private bool _tileLockWarned;

    /// <summary>타일 붓·뒤집기·층·무작위·자동 타일 세트·칸 선택이 바뀜(타일셋 패널·도구 설정·캔버스 미리보기).</summary>
    public event EventHandler? TileStateChanged;

    private void TileChanged()
    {
        SyncFloorView();
        TileStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private TileFloorViewMode _tileViewMode;
    // 마지막으로 캔버스 그림에 넣은 층 보기(바뀌면 캔버스 합성을 무효로)
    private TileFloorView? _shownFloorView;

    /// <summary>캔버스의 층 보기(위 막대): 모든 층 그대로, 고른 층 강조(다른 층은 흐리게), 고른 층만. 미리보기·내보내기는 늘 모든 층.</summary>
    public TileFloorViewMode TileViewMode
    {
        get => _tileViewMode;
        set
        {
            if (_tileViewMode == value) return;
            _tileViewMode = value;
            SyncFloorView();
            TileStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>층 보기가 맞출 층: 손으로 고른 층, 아니면 지금 찍을 것이 들어갈 첫 층(없으면 null — 지우개 자동 등).</summary>
    public int? TileViewFloor
    {
        get
        {
            if (CurrentTileset is not { } ts) return null;
            if (_tileFloor is { } f) return ts.ValidFloor(f);
            var floors = Chickxel.App.TilePick.Describe(this, ts).Floors;
            return floors.Count > 0 ? floors[0] : null;
        }
    }

    /// <summary>캔버스 합성에 층 보기를 입힐 타일맵 레이어(층 보기가 꺼졌거나 맞출 층이 없으면 null).</summary>
    private Layer? FloorViewLayer => _shownFloorView?.Layer;

    /// <summary>레이어 <paramref name="layer"/>에 입힐 층 보기(지금 캔버스의 층 보기가 그 레이어 것일 때만).</summary>
    private TileFloorView? FloorViewFor(Layer layer) => _shownFloorView is { } v && ReferenceEquals(v.Layer, layer) ? v : null;

    /// <summary>층 보기(모드·레이어·맞출 층)를 다시 정하고, 바뀌었으면 층 보기를 입힌 캔버스 합성을 무효로 하고 화면을 다시 그린다.</summary>
    private void SyncFloorView()
    {
        var view = _tileViewMode != TileFloorViewMode.All && CurrentTilemap is { } layer && TileViewFloor is { } floor
            ? new TileFloorView(layer, floor, _tileViewMode) : null;
        if (view == _shownFloorView) return;
        _shownFloorView = view;
        _renders.Invalidate(k => k.Kind == RenderKind.Composite && k.Layer is not null);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    // ───────────── 타일셋 패널 보기(설정 창 "보기", 앱 설정에 저장) ─────────────

    private bool _tileListByFloor = true, _tileListHideAuto = true, _tileShowAutoParts = true;
    private int _tileSlotSize;

    /// <summary>타일 목록을 기본 층별로 나눠 보인다(층 이름 줄 아래 그 층 타일).</summary>
    public bool TileListByFloor
    {
        get => _tileListByFloor;
        set { if (_tileListByFloor == value) return; _tileListByFloor = value; TileChanged(); }
    }

    /// <summary>자동 타일 세트에 쓴 타일을 타일 목록에서 감춘다(세트의 구성에서 고름).</summary>
    public bool TileListHideAutoTiles
    {
        get => _tileListHideAuto;
        set { if (_tileListHideAuto == value) return; _tileListHideAuto = value; TileChanged(); }
    }

    /// <summary>자동 타일 세트를 고르면 그 아래에 세트의 구성(든 타일)을 보인다.</summary>
    public bool TileShowAutoParts
    {
        get => _tileShowAutoParts;
        set { if (_tileShowAutoParts == value) return; _tileShowAutoParts = value; TileChanged(); }
    }

    /// <summary>타일셋 패널의 타일·자동 타일 칸 크기(px, 0 = 자동 — 패널 폭에 목록 열이 다 들어가게).</summary>
    public int TileSlotSize
    {
        get => _tileSlotSize;
        set
        {
            value = value <= 0 ? 0 : Math.Clamp(value, MinTileSlotSize, MaxTileSlotSize);
            if (_tileSlotSize == value) return;
            _tileSlotSize = value;
            TileChanged();
        }
    }

    public const int MinTileSlotSize = 16, MaxTileSlotSize = 64;

    /// <summary>현재 레이어가 타일맵 레이어인지(도구 막대·메뉴·캔버스가 타일 편집으로 바뀜).</summary>
    public bool IsTilemapSelected => CurrentLayerObject?.IsTilemap == true;

    /// <summary>현재 타일맵 레이어(아니면 null).</summary>
    public Layer? CurrentTilemap => CurrentLayerObject is { IsTilemap: true } l ? l : null;

    /// <summary>
    /// 현재 타일맵 레이어가 그리는 타일셋(없으면 null): 레이어에 쌓은 타일셋을 합친 것(<see cref="TileSources.Combine"/> — 둘째 타일셋부터의 타일·세트 Id는
    /// 자리만큼 밀림). 칸 크기·층은 첫 타일셋 것. 찍기·그리기·이름 보이기는 이것으로, 타일셋 고치기는 <see cref="EditTileset"/>로 원래 타일셋에.
    /// </summary>
    public Tileset? CurrentTileset => Project is { } p && CurrentTilemap is { } l ? p.TilesetOf(l) : null;

    /// <summary>현재 타일맵 레이어에 쌓은 타일셋(자리 차례, 첫째 = 칸 크기·층을 정하는 타일셋). 타일맵 레이어가 아니면 빈 목록.</summary>
    public IReadOnlyList<Tileset> CurrentTilesetSources => Project is { } p && CurrentTilemap is { } l ? p.TilesetSources(l) : [];

    /// <summary>현재 타일맵 레이어의 첫 타일셋 이름표(층은 이 타일셋 것, 없으면 null).</summary>
    public string? CurrentPrimaryTilesetKey => CurrentTiles?.PrimaryTilesetKey;

    /// <summary>현재 타일맵 레이어 내용(없으면 null).</summary>
    public TilemapData? CurrentTiles => CurrentTilemap?.Tiles;

    public TileTool TileTool
    {
        get => _tileTool;
        set
        {
            if (_tileTool == value) return;
            _tileTool = value;
            if (value == TileTool.AutoTile) _autoSource = true;
            else if (value == TileTool.Brush) _autoSource = false;
            ToolChanged?.Invoke(this, EventArgs.Empty);
            TileChanged();
        }
    }

    /// <summary>붓에 든 타일들(없으면 null).</summary>
    public TileStamp? TileStamp
    {
        get => _tileStamp;
        set { _tileStamp = value; if (value is not null) _autoSource = false; TileChanged(); }
    }

    /// <summary>붓을 찍기 전에 돌리고 뒤집기.</summary>
    public TileTransform TileTransform
    {
        get => _tileTransform;
        set { if (_tileTransform == value) return; _tileTransform = value; TileChanged(); }
    }

    /// <summary>찍을 층을 손으로 고정(수동, null = 자동 — 타일마다 기본 층, 자동 타일은 세트의 층).</summary>
    public int? TileFloor
    {
        get => _tileFloor;
        set { if (_tileFloor == value) return; _tileFloor = value; TileChanged(); }
    }

    /// <summary>붓에 여러 타일이 들었으면 칸마다 하나를 무작위로 찍는다.</summary>
    public bool TileRandom
    {
        get => _tileRandom;
        set { if (_tileRandom == value) return; _tileRandom = value; TileChanged(); }
    }

    /// <summary>자동 타일 붓이 쓸 세트 Id(0 = 없음).</summary>
    public int AutoTileSet
    {
        get => _autoTileSet;
        set { _autoSource = true; if (_autoTileSet == value) { TileChanged(); return; } _autoTileSet = value; TileChanged(); }
    }

    /// <summary>캔버스에 칸 격자를 보일지(타일맵 레이어를 골랐을 때).</summary>
    public bool ShowTileCells
    {
        get => _showTileCells;
        set { if (_showTileCells == value) return; _showTileCells = value; TileChanged(); ViewChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>고른 칸 사각형(칸 선택 도구, 없으면 null).</summary>
    public Rectangle? TileSelection
    {
        get => _tileSelection;
        set { if (_tileSelection == value) return; _tileSelection = value; TileChanged(); ViewChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>찍을 붓(뒤집기·회전을 입힌 것). 없으면 null.</summary>
    public TileStamp? EffectiveStamp => _tileStamp is { } s && CurrentTileset is { } ts ? s.Transformed(ts, _tileTransform) : null;

    /// <summary>사각형·채우기가 자동 타일 세트로 칠하는지(마지막으로 고른 것이 세트일 때).</summary>
    public bool UsesAutoTileSource => (_autoSource || _tileTool == TileTool.AutoTile) && CurrentAutoTileSet is not null;

    /// <summary>지금 자동 타일 세트(없으면 null). 찍을 층을 손으로 고정했으면 그 층에 칠하는 세트(층만 바꾼 사본)다.</summary>
    public AutoTileSet? CurrentAutoTileSet
    {
        get
        {
            if (CurrentTileset is not { } ts || ts.FindAutoTile(_autoTileSet) is not { } set) return null;
            if (_tileFloor is not { } f || ts.ValidFloor(f) == set.Floor) return set;
            return set with { Floor = ts.ValidFloor(f) };
        }
    }

    /// <summary>붓 뒤집기·회전을 하나 더 입힌다(X·Y·Z 키, 뒤집기·회전 메뉴).</summary>
    public void TurnStamp(TileTransform step)
    {
        if (CurrentTileset is { } ts && !ts.CanRotate) step &= ~TileTransform.Rotate90;
        TileTransform = _tileTransform.Then(step);
    }

    /// <summary>캔버스 좌표(px) → 현재 타일맵의 칸.</summary>
    public Point? TileCellAt(Point px) => CurrentTiles is { } d && CurrentTileset is { } ts ? d.CellAt(ts, px.X, px.Y) : null;

    // ───────────── 찍기 ─────────────

    /// <summary>찍을 층: 손으로 고른 층 → 붓 칸의 층 → 타일의 기본 층(타일셋에 있는 층으로).</summary>
    private static int FloorFor(Tileset ts, int? fixedFloor, StampCell c)
        => ts.ValidFloor(fixedFloor ?? c.Floor ?? ts.Find(c.Cell.Tile)?.Floor ?? 0);

    /// <summary>타일 도구로 고칠 수 있는지(아니면 상태 표시줄에 보일 이유).</summary>
    public string? TileEditBlocker()
    {
        if (Project is not { } p || CurrentTilemap is not { } layer) return UiStrings.Tile_NotTilemap;
        if (p.IsLayerLocked(p.IndexOf(layer))) return UiStrings.S_PixelCanvas_OnPointerPressed_F29C349441;
        if (CurrentTileset is null) return UiStrings.Tile_NoTileset;
        return null;
    }

    /// <summary>끌어 찍기 시작(실행취소 한 단계로 묶음). 시작할 수 없으면 그 이유.</summary>
    public string? BeginTileStroke(Point startCell)
    {
        if (TileEditBlocker() is { } why) return why;
        CommitFloating();
        Project!.BeginTileStroke(CurrentTilemap!);
        _tileStrokeStart = startCell;
        _tileStrokeDone.Clear();
        _tileStrokeSalt = Environment.TickCount;
        _tileLockWarned = false;
        return null;
    }

    /// <summary>
    /// 끌어 찍기의 칸 하나: 지금 도구로(붓·자동 타일 — <paramref name="erase"/>면 지우기, 지우개는 늘 지우기). <paramref name="px"/> = 누른 캔버스 점
    /// (지우개가 그림 기준으로 고름, 없으면 칸 가운데).
    /// </summary>
    public void TileStrokeAt(Point cell, bool erase, Point? px = null)
    {
        if (Project is not { IsTileStrokeActive: true } p || CurrentTilemap is not { } layer || CurrentTileset is not { } ts) return;
        var data = layer.Tiles!;
        var map = data.Map;
        if (_tileTool == TileTool.AutoTile && CurrentAutoTileSet is { } set)
        {
            if (data.IsFloorLocked(set.Floor)) { WarnLockedFloor(); return; }
            map = erase ? AutoTiler.Erase(ts, map, set, [cell]) : AutoTiler.Paint(ts, map, set, [cell], f => !data.IsFloorLocked(f));
        }
        else if (erase || _tileTool == TileTool.Eraser) map = EraseCell(ts, data, map, cell, px ?? CellCenter(ts, data, cell));
        else if (EffectiveStamp is { } stamp)
        {
            var size = stamp.Bounds(ts).Size;
            // 여러 칸 붓은 시작 칸에서 붓 크기마다만 찍는다(겹쳐 덮지 않게). 무작위면 한 칸씩.
            if (!_tileRandom && (size.Width > 1 || size.Height > 1))
            {
                int dx = cell.X - _tileStrokeStart.X, dy = cell.Y - _tileStrokeStart.Y;
                cell = new Point(_tileStrokeStart.X + FloorDiv(dx, size.Width) * size.Width, _tileStrokeStart.Y + FloorDiv(dy, size.Height) * size.Height);
            }
            if (!_tileStrokeDone.Add(cell)) return;
            map = StampAt(ts, data, map, stamp, cell, _tileStrokeSalt);
        }
        p.SetStrokeTileMap(map);
    }

    /// <summary>끌어 찍기를 실행취소 한 단계로 확정한다.</summary>
    public void EndTileStroke()
    {
        if (Project is not { IsTileStrokeActive: true } p) return;
        p.CommitTileStroke(_tileTool switch
        {
            TileTool.Eraser => UiStrings.Tile_EraseLabel,
            TileTool.AutoTile => UiStrings.Tile_AutoTileLabel,
            _ => UiStrings.Tile_PaintLabel,
        });
        _tileStrokeDone.Clear();
    }

    public void CancelTileStroke()
    {
        Project?.CancelTileStroke();
        _tileStrokeDone.Clear();
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    /// <summary>붓을 칸 <paramref name="at"/>(붓 왼쪽 위)에 찍은 맵. 잠긴 층에 갈 칸은 건너뛴다. 둘레 자동 타일은 다시 고른다(덮어쓴 자동 타일·"뭐라도 있음" 이웃).</summary>
    private TileMap StampAt(Tileset ts, TilemapData data, TileMap map, TileStamp stamp, Point at, int salt)
    {
        IEnumerable<StampCell> cells = stamp.Cells;
        if (_tileRandom && stamp.Cells.Count > 1)
        {
            var pick = stamp.Cells[(int)(AutoTiler.Hash(at.X, at.Y, salt) % (ulong)stamp.Cells.Count)];
            cells = [pick with { Dx = 0, Dy = 0 }];
        }
        var touched = new List<Point>();
        foreach (var c in cells)
        {
            int floor = FloorFor(ts, _tileFloor, c);
            if (data.IsFloorLocked(floor)) { WarnLockedFloor(); continue; }
            var p = new Point(at.X + c.Dx, at.Y + c.Dy);
            map = map.Place(ts, floor, p, c.Cell with { AutoTile = 0 });
            var size = ts.Footprint(c.Cell);
            for (int y = 0; y < size.Height; y++)
                for (int x = 0; x < size.Width; x++)
                    touched.Add(new Point(p.X + x, p.Y + y));
        }
        return touched.Count == 0 ? map : AutoTiler.RefreshAround(ts, map, touched);
    }

    /// <summary>
    /// 점 <paramref name="px"/>(칸 <paramref name="cell"/>)의 타일 하나를 지운 맵: 층을 고정했으면 그 층, 아니면 보이고 잠기지 않은 층에서.
    /// 위치로 그리는 타일셋은 점이 있으면 세운 것·머리 위 타일을 그림 기준으로 먼저 찾고(솟거나 내려온 그림을 눌러도 그 타일), 없으면 칸을 덮은 위층부터.
    /// 둘레 자동 타일은 다시 고른다.
    /// </summary>
    private TileMap EraseCell(Tileset ts, TilemapData data, TileMap map, Point cell, Point? px)
    {
        bool FloorOk(int f) => !data.IsFloorLocked(f) && (_tileFloor is { } only ? f == only : data.ShowsFloor(f));
        Func<TileDrawClass, bool>? byImage = px is not null && ts.SortMode == TileSortMode.ByPosition ? c => c != TileDrawClass.Ground : null;
        if (TileUnder(ts, data, map, cell, px ?? default, FloorOk, byImage) is not { } hit) return map;
        if (hit.Cell.AutoTile != 0 && ts.FindAutoTile(hit.Cell.AutoTile) is { } set)
            return AutoTiler.Erase(ts, map, set.Floor == hit.Floor ? set : set with { Floor = hit.Floor }, [hit.At]);
        return AutoTiler.RefreshAround(ts, map.With(hit.Floor, hit.At, null), [hit.At, cell]);
    }

    /// <summary>
    /// 점 <paramref name="px"/>(칸 <paramref name="cell"/>)에서 잡을 타일: <paramref name="byImage"/> 묶음의 타일 중 그림이 투명하지 않은 맨 위(없으면 그림 기준을 쓰지 않음),
    /// 없으면 칸을 덮은 타일을 위층부터. <paramref name="floorOk"/> 층만.
    /// </summary>
    private static PlacedTile? TileUnder(Tileset ts, TilemapData data, TileMap map, Point cell, Point px, Func<int, bool> floorOk, Func<TileDrawClass, bool>? byImage)
    {
        if (byImage is not null && TileMapRenderer.HitTest(ts, map, data.Origin, px, floorOk, classes: byImage) is { } hit) return hit;
        foreach (int f in map.FloorsTopDown(ts))
            if (floorOk(f) && map.Covering(ts, f, cell) is { } covering) return covering;
        return null;
    }

    /// <summary>캔버스 점 <paramref name="px"/>에 보이는 맨 위 타일(보이는 층, 그림 기준 — 그림이 투명한 곳이면 그 칸을 덮은 위층부터). 없으면 null.</summary>
    public PlacedTile? TileUnder(Point px)
    {
        if (CurrentTiles is not { } data || CurrentTileset is not { } ts) return null;
        return TileUnder(ts, data, data.Map, data.CellAt(ts, px.X, px.Y), px, data.ShowsFloor, _ => true);
    }

    /// <summary>칸 가운데의 캔버스 점.</summary>
    private static Point CellCenter(Tileset ts, TilemapData data, Point cell)
    {
        var r = data.PixelRect(ts, new Rectangle(cell, new Size(1, 1)));
        return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    private void WarnLockedFloor()
    {
        if (_tileLockWarned) return;
        _tileLockWarned = true;
        TileMessage?.Invoke(this, UiStrings.Tile_FloorLocked);
    }

    /// <summary>타일 편집 알림(상태 표시줄).</summary>
    public event EventHandler<string>? TileMessage;

    /// <summary>현재 맵을 한 단계로 바꾼다.</summary>
    private void CommitTileMap(Func<Tileset, TilemapData, TileMap> change, string label)
    {
        if (TileEditBlocker() is { } why) { TileMessage?.Invoke(this, why); return; }
        CommitFloating();
        var layer = CurrentTilemap!;
        var ts = CurrentTileset!;
        _tileLockWarned = false;
        var map = change(ts, layer.Tiles!);
        Project!.SetTileMap(layer, map, label);
    }

    /// <summary>칸 사각형을 붓으로 채운다(붓 크기마다, 자동 타일 붓이면 세트로). <paramref name="erase"/>면 사각형 안 타일을 지운다.</summary>
    public void TileRect(Rectangle cells, bool erase)
    {
        if (cells.Width < 1 || cells.Height < 1) return;
        CommitTileMap((ts, data) =>
        {
            var map = data.Map;
            if (UsesAutoTileSource)
            {
                if (CurrentAutoTileSet is not { } set || data.IsFloorLocked(set.Floor)) return map;
                var all = CellsOf(cells);
                return erase ? AutoTiler.Erase(ts, map, set, all) : AutoTiler.Paint(ts, map, set, all, f => !data.IsFloorLocked(f));
            }
            if (erase)
            {
                foreach (var cell in CellsOf(cells)) map = EraseCell(ts, data, map, cell, null);
                return map;
            }
            if (EffectiveStamp is not { } stamp) return map;
            int salt = Environment.TickCount;
            if (_tileRandom && stamp.Cells.Count > 1)
            {
                foreach (var cell in CellsOf(cells)) map = StampAt(ts, data, map, stamp, cell, salt);
                return map;
            }
            var size = stamp.Bounds(ts).Size;
            for (int y = cells.Top; y + size.Height <= cells.Bottom || y == cells.Top; y += size.Height)
                for (int x = cells.Left; x + size.Width <= cells.Right || x == cells.Left; x += size.Width)
                    map = StampAt(ts, data, map, stamp, new Point(x, y), salt);
            return map;
        }, erase ? UiStrings.Tile_EraseLabel : UiStrings.Tile_RectLabel);
    }

    private static List<Point> CellsOf(Rectangle cells)
    {
        var list = new List<Point>(cells.Width * cells.Height);
        for (int y = cells.Top; y < cells.Bottom; y++)
            for (int x = cells.Left; x < cells.Right; x++)
                list.Add(new Point(x, y));
        return list;
    }

    /// <summary>
    /// 채우기: 칸 <paramref name="start"/>와 보이는 모습(보이는 층마다의 타일 — 자동 타일 칸은 세트, 또는 빈 칸)이 같은 이어진 칸을 붓으로 채운다
    /// (<paramref name="erase"/>면 지운다). 그래서 다른 층의 벽 안쪽만 채운다. 빈 칸은 끝없이 이어지므로 캔버스와 지금 맵을 감싼 범위 안에서만.
    /// </summary>
    public void TileFill(Point start, bool erase)
    {
        CommitTileMap((ts, data) =>
        {
            var map = data.Map;
            var stamp = EffectiveStamp;
            int floor;
            if (UsesAutoTileSource && CurrentAutoTileSet is { } set) floor = set.Floor;
            else if (_tileFloor is { } f) floor = f;
            else if (erase) floor = map.FloorsTopDown(ts).FirstOrDefault(f => map.Covering(ts, f, start) is not null, ts.ValidFloor(0));
            else if (stamp is { Cells.Count: > 0 }) floor = FloorFor(ts, null, stamp.Cells[0]);
            else return map;
            if (data.IsFloorLocked(floor)) { WarnLockedFloor(); return map; }
            var limit = TileLimit(ts, data);
            var floors = ts.Floors.Select(fl => fl.Id).Where(id => id == floor || data.ShowsFloor(id)).ToList();
            var region = map.FillRegion(ts, floors, start, limit);
            if (region.Count == 0) return map;
            if (UsesAutoTileSource && CurrentAutoTileSet is { } auto)
                return erase ? AutoTiler.Erase(ts, map, auto, region) : AutoTiler.Paint(ts, map, auto, region, f => !data.IsFloorLocked(f));
            if (erase)
            {
                var erased = new List<Point>();
                foreach (var cell in region)
                    if (map.Covering(ts, floor, cell) is { } hit)
                    {
                        map = map.With(floor, hit.At, null);
                        erased.Add(hit.At);
                    }
                return AutoTiler.RefreshAround(ts, map, erased, onlyFilled: false);
            }
            if (stamp is null) return map;
            int salt = Environment.TickCount;
            // 붓 크기마다(시작 칸 기준), 붓 전체가 영역 안에 들어가는 자리만
            var size = stamp.Bounds(ts).Size;
            bool random = _tileRandom && stamp.Cells.Count > 1;
            foreach (var cell in region.OrderBy(c => c.Y).ThenBy(c => c.X))
            {
                if (!random && ((cell.X - start.X) % size.Width != 0 || (cell.Y - start.Y) % size.Height != 0)) continue;
                if (!random && !Fits(region, cell, size)) continue;
                map = StampAt(ts, data, map, stamp, cell, salt);
            }
            return map;
        }, erase ? UiStrings.Tile_EraseLabel : UiStrings.Tile_FillLabel);
    }

    private static bool Fits(HashSet<Point> region, Point at, Size size)
    {
        for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
                if (!region.Contains(new Point(at.X + x, at.Y + y))) return false;
        return true;
    }

    /// <summary>채우기 범위(칸): 캔버스를 덮는 칸과 지금 맵을 감싼 사각형.</summary>
    private Rectangle TileLimit(Tileset ts, TilemapData data)
    {
        var p = Project!;
        var first = data.CellAt(ts, 0, 0);
        var last = data.CellAt(ts, p.Width - 1, p.Height - 1);
        var r = Rectangle.FromLTRB(first.X, first.Y, last.X + 1, last.Y + 1);
        var used = data.Map.CellBounds(ts);
        if (!used.IsEmpty) r = Rectangle.Union(r, used);
        return r;
    }

    /// <summary>
    /// 스포이드: 점 <paramref name="px"/>에 보이는 맨 위 타일(그림 기준, <see cref="TileUnder(Point)"/>)을 붓에 든다(그 층·뒤집기 그대로). 자동 타일로 찍은 칸이면 그 세트를 고르고
    /// 자동 타일 붓으로, 아니면 타일 붓으로 바꾼다. 집은 것이 없으면 false.
    /// </summary>
    public bool TilePick(Point px)
    {
        if (CurrentTileset is not { } ts || TileUnder(px) is not { } hit) return false;
        if (hit.Cell.AutoTile != 0 && ts.FindAutoTile(hit.Cell.AutoTile) is not null)
        {
            AutoTileSet = hit.Cell.AutoTile;
            TileTool = TileTool.AutoTile;
            return true;
        }
        _tileTransform = TileTransform.None;
        TileStamp = new TileStamp([new StampCell(0, 0, hit.Cell, hit.Floor)]);
        TileTool = TileTool.Brush;
        return true;
    }

    /// <summary>고른 칸 사각형의 보이는 타일을 붓에 든다(<paramref name="cut"/>면 그 타일을 지움 — 한 단계). 담은 것이 없으면 false.</summary>
    public bool CopyTileSelection(bool cut)
    {
        if (_tileSelection is not { } sel || CurrentTiles is not { } data || CurrentTileset is not { } ts) return false;
        var cells = new List<StampCell>();
        var taken = new List<PlacedTile>();
        foreach (var floor in ts.Floors)
        {
            if (!data.ShowsFloor(floor.Id)) continue;
            foreach (var p in data.Map.Overlapping(ts, floor.Id, sel))
            {
                if (!sel.Contains(p.At)) continue; // 기준 칸이 사각형 안인 타일만
                cells.Add(new StampCell(p.At.X - sel.X, p.At.Y - sel.Y, p.Cell with { AutoTile = 0 }, floor.Id));
                taken.Add(p);
            }
        }
        if (cells.Count == 0) return false;
        _tileTransform = TileTransform.None;
        TileStamp = new TileStamp(cells);
        if (cut)
            CommitTileMap((_, d) =>
            {
                var map = d.Map;
                var erased = new List<Point>();
                foreach (var p in taken)
                    if (!d.IsFloorLocked(p.Floor))
                    {
                        map = map.With(p.Floor, p.At, null);
                        erased.Add(p.At);
                    }
                return AutoTiler.RefreshAround(CurrentTileset!, map, erased, onlyFilled: false);
            }, UiStrings.Tile_CutLabel);
        return true;
    }

    /// <summary>고른 칸 사각형의 타일을 지운다(보이고 잠기지 않은 층, 기준 칸이 안에 든 타일). 지운 것이 없으면 false.</summary>
    public bool DeleteTileSelection()
    {
        if (_tileSelection is not { } sel || CurrentTiles is not { } data || CurrentTileset is not { } ts) return false;
        var map = data.Map;
        var erased = new List<Point>();
        foreach (var floor in ts.Floors)
        {
            if (!data.ShowsFloor(floor.Id) || data.IsFloorLocked(floor.Id)) continue;
            foreach (var p in map.Overlapping(ts, floor.Id, sel).ToList())
                if (sel.Contains(p.At))
                {
                    map = map.With(floor.Id, p.At, null);
                    erased.Add(p.At);
                }
        }
        if (ReferenceEquals(map, data.Map)) return false;
        map = AutoTiler.RefreshAround(ts, map, erased, onlyFilled: false);
        CommitTileMap((_, _) => map, UiStrings.Tile_EraseLabel);
        return true;
    }

    // ───────────── 타일셋 ─────────────

    /// <summary>
    /// 프로젝트의 타일셋 <paramref name="key"/>를 바꾼다(실행취소 한 단계). <paramref name="remap"/>은 그 타일셋을 쓰는 모든 맵에 함께 입힌다 —
    /// 둘째 인자는 그 맵에서 이 타일셋 타일 Id의 밀린 만큼(<see cref="TileSources.Offset"/>).
    /// </summary>
    public void EditTileset(string key, string label, Func<Tileset, Tileset> change, Func<TileMap, int, TileMap>? remap = null)
    {
        if (Project is not { } p || !p.Tilesets.TryGetValue(key, out var ts)) return;
        var next = change(ts);
        if (ReferenceEquals(next, ts) && remap is null) return;
        Structural(() => p.EditTileset(label, next, remap));
        TileChanged();
    }

    /// <summary>현재 타일맵 레이어의 층 보이기·잠금.</summary>
    public void SetTileFloorState(int floor, bool? hidden = null, bool? locked = null)
    {
        if (Project is not { } p || CurrentTilemap is not { } layer) return;
        Structural(() => p.SetTileFloorState(layer, floor, hidden, locked));
    }
}
