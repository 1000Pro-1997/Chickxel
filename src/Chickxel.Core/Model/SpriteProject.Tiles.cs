using System.Drawing;
using Chickxel.Core.Localization;
using Chickxel.Core.Tiles;

namespace Chickxel.Core.Model;

// 타일맵 레이어: 프로젝트가 담은 타일셋(이름표 → 타일셋), 레이어마다 맵, 끌어 찍기(타일 스트로크), 합성용 셀(맵 그림), 레이어로 변환.
public sealed partial class SpriteProject
{
    private readonly Dictionary<string, Tileset> _tilesets = new(StringComparer.Ordinal);

    /// <summary>
    /// 타일셋 파일과 맞춘 값(이름표 → 그때의 타일셋): 파일에서 읽었거나 파일에 쓴 것. 저장할 때 지금 값이 이것과 다르면 파일에도 쓴다(<see cref="TilesetsToWrite"/>).
    /// 실행취소 대상이 아니다.
    /// </summary>
    private readonly Dictionary<string, Tileset> _tilesetSynced = new(StringComparer.Ordinal);

    /// <summary>타일 스트로크: 고치는 타일맵 레이어, 시작 전 상태(기록용)와 그때의 내용.</summary>
    private (Layer Layer, Snapshot Before, TilemapData Data)? _tileStroke;

    /// <summary>프로젝트가 담은 타일셋(이름표 → 타일셋). 바꿀 때는 타일 메서드로(실행취소됨).</summary>
    public IReadOnlyDictionary<string, Tileset> Tilesets => _tilesets;

    /// <summary>
    /// 타일맵 레이어가 쓰는 타일셋(타일맵 레이어가 아니거나 첫 타일셋이 없으면 null). 여러 타일셋을 쓰면 합친 것(<see cref="TileSources.Combine"/>) —
    /// 쓰는 타일셋들이 그대로인 동안은 같은 객체.
    /// </summary>
    public Tileset? TilesetOf(Layer layer)
    {
        if (layer.Tiles is not { } t || t.TilesetKeys.IsDefaultOrEmpty || !_tilesets.TryGetValue(t.TilesetKeys[0], out var primary)) return null;
        if (t.TilesetKeys.Length == 1) return primary;
        if (_combinedTilesets.TryGetValue(layer, out var hit) && SameSources(hit.Sources, t.TilesetKeys)) return hit.Combined;
        var sources = TilesetSources(layer);
        if (sources.Count == 1) return primary;
        var combined = new CombinedTileset([.. sources], TileSources.Combine(sources));
        _combinedTilesets.AddOrUpdate(layer, combined);
        return combined.Combined;
    }

    /// <summary>
    /// 타일맵 레이어가 쓰는 타일셋들(자리 차례, 타일맵 레이어가 아니면 빈 것). 프로젝트에 없는 이름표가 있으면 그 앞까지(뒤 자리의 Id가 어긋나지 않게).
    /// </summary>
    public IReadOnlyList<Tileset> TilesetSources(Layer layer)
    {
        if (layer.Tiles is not { } t || t.TilesetKeys.IsDefaultOrEmpty) return [];
        var list = new List<Tileset>(t.TilesetKeys.Length);
        foreach (var key in t.TilesetKeys)
        {
            if (!_tilesets.TryGetValue(key, out var ts)) break;
            list.Add(ts);
        }
        return list;
    }

    /// <summary>레이어별 합친 타일셋과 그때의 타일셋들(같은 객체들이면 다시 씀).</summary>
    private sealed class CombinedTileset(Tileset[] sources, Tileset combined)
    {
        public Tileset[] Sources { get; } = sources;
        public Tileset Combined { get; } = combined;
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Layer, CombinedTileset> _combinedTilesets = new();

    /// <summary>이름표들이 가리키는 지금 타일셋(없는 이름표 앞까지)이 <paramref name="cached"/>와 같은 객체들인지.</summary>
    private bool SameSources(Tileset[] cached, System.Collections.Immutable.ImmutableArray<string> keys)
    {
        int n = 0;
        foreach (var key in keys)
        {
            if (!_tilesets.TryGetValue(key, out var ts)) break;
            if (n >= cached.Length || !ReferenceEquals(cached[n], ts)) return false;
            n++;
        }
        return n == cached.Length;
    }

    /// <summary>레이어들이 쓰는 타일셋(합치기 전 것, 이름표 차례).</summary>
    public IReadOnlyList<Tileset> UsedTilesets
        => _layers.Where(l => l.IsTilemap).SelectMany(TilesetSources).Distinct().OrderBy(t => t.Key, StringComparer.Ordinal).ToList();

    /// <summary>프로젝트가 담은 모든 타일셋(레이어에서 닫은 것도 — 저장할 것, 이름표 차례).</summary>
    public IReadOnlyList<Tileset> AllTilesets => _tilesets.Values.OrderBy(t => t.Key, StringComparer.Ordinal).ToList();

    /// <summary>타일맵 레이어 어디선가 쓰는 타일셋인지.</summary>
    public bool IsTilesetUsed(string key) => _layers.Any(l => l.Tiles is { } d && d.SlotOf(key) >= 0);

    /// <summary>타일셋을 프로젝트에 담는다(레이어에 열지 않음, 실행취소 한 단계 — 같은 이름표가 있으면 이것으로 바꿈).</summary>
    public void AddTileset(Tileset tileset)
    {
        if (_tilesets.TryGetValue(tileset.Key, out var old) && ReferenceEquals(old, tileset)) return;
        Edit(DomainStrings.Tile_AddProjectTileset, ProjectChange.Layers, () => _tilesets[tileset.Key] = tileset);
    }

    /// <summary>프로젝트에서 타일셋을 지운다(실행취소 한 단계). 타일맵 레이어가 쓰고 있거나 없는 이름표면 false.</summary>
    public bool RemoveTileset(string key)
    {
        if (!_tilesets.ContainsKey(key) || IsTilesetUsed(key)) return false;
        Edit(DomainStrings.Tile_RemoveProjectTileset, ProjectChange.Layers, () =>
        {
            _tilesets.Remove(key);
            _tilesetSynced.Remove(key);
        });
        return true;
    }

    private Layer RequireTilemap(Layer layer)
        => layer.IsTilemap && layer.Tiles is not null && _layers.Contains(layer) ? layer : throw new ArgumentException(DomainStrings.Tile_NotTilemapLayer, nameof(layer));

    /// <summary>
    /// 빈 타일맵 레이어를 <paramref name="index"/>(기본: 맨 위, 그룹 안이면 그 그룹 위)에 넣는다. 타일셋은 프로젝트에 담는다(같은 이름표가 있으면 이것으로 바꿈).
    /// </summary>
    public Layer AddTilemapLayer(Tileset tileset, int? index = null, string? name = null)
    {
        int at = TopLevelInsertIndex(index ?? _layers.Count);
        var layer = new Layer(UniqueLayerName(string.IsNullOrWhiteSpace(name) ? DomainStrings.Tile_DefaultLayerName : name.Trim()))
        {
            Kind = LayerKind.Tilemap,
            Pivot = Sprite.DefaultPivot,
            Tiles = new TilemapData([tileset.Key], TileMap.Empty, Point.Empty),
        };
        Edit(DomainStrings.Tile_AddLayer, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            _tilesets[tileset.Key] = tileset;
            _layers.Insert(at, layer);
        });
        return layer;
    }

    /// <summary>
    /// 타일셋을 바꾼다(같은 이름표, 실행취소 한 단계). <paramref name="remap"/>이 있으면 그 타일셋을 쓰는 모든 맵을 같이 고친다(지운 타일 빼기 등) —
    /// 두 번째 값은 그 레이어에서 이 타일셋 자리의 Id 오프셋(<see cref="TileSources.Offset"/>, 맵 Id = 오프셋 + 타일셋 안 Id).
    /// 자동 타일 세트가 바뀌었으면(새 값·지움) 그 맵의 모든 자동 타일 칸을 레이어의 새 합친 타일셋으로 다시 고른다(무리로 다른 세트와 이어지므로).
    /// </summary>
    public void EditTileset(string label, Tileset tileset, Func<TileMap, int, TileMap>? remap = null)
    {
        if (!_tilesets.TryGetValue(tileset.Key, out var old)) throw new ArgumentException(DomainStrings.Tile_TilesetNotInProject, nameof(tileset));
        if (ReferenceEquals(old, tileset) && remap is null) return;
        Edit(label, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            _tilesets[tileset.Key] = tileset;
            bool setsChanged = tileset.AutoTiles.Length != old.AutoTiles.Length || tileset.AutoTiles.Any(a => !ReferenceEquals(old.FindAutoTile(a.Id), a));
            foreach (var layer in _layers)
            {
                if (!layer.IsTilemap || layer.Tiles is not { } data || data.SlotOf(tileset.Key) is not (>= 0 and var slot)) continue;
                var map = remap?.Invoke(data.Map, TileSources.Offset(slot)) ?? data.Map;
                if (setsChanged && TilesetOf(layer) is { } combined) map = AutoTiler.RefreshAll(combined, map);
                if (!ReferenceEquals(map, data.Map)) layer.Tiles = data with { Map = map };
            }
        });
    }

    /// <summary>타일맵 레이어의 맵을 바꾼다(실행취소 한 단계, 같으면 기록 없음). 끌어 찍기는 <see cref="BeginTileStroke"/>로.</summary>
    public void SetTileMap(Layer layer, TileMap map, string label)
    {
        RequireTilemap(layer);
        if (ReferenceEquals(layer.Tiles!.Map, map)) return;
        Edit(label, ProjectChange.Pixels, () => layer.Tiles = layer.Tiles! with { Map = map });
    }

    /// <summary>
    /// 타일맵 레이어의 자리 <paramref name="slot"/> 타일셋을 바꾼다(실행취소 한 단계, 프로젝트에 담음 — 같은 이름표가 있으면 이것으로 바꿈, 맵은 그대로 — 같은 Id의 타일로 보임).
    /// 다른 자리에 이미 쓰는 타일셋이거나 칸 크기가 다른 자리의 타일셋과 다르면 <see cref="ArgumentException"/>.
    /// </summary>
    public void SetLayerTileset(Layer layer, int slot, Tileset tileset)
    {
        RequireTilemap(layer);
        var keys = layer.Tiles!.TilesetKeys;
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, keys.Length);
        if (keys[slot] == tileset.Key && _tilesets.TryGetValue(tileset.Key, out var current) && ReferenceEquals(current, tileset)) return;
        if (keys.IndexOf(tileset.Key) is >= 0 and var other && other != slot) throw new ArgumentException(DomainStrings.Tile_TilesetAlreadyInLayer, nameof(tileset));
        for (int i = 0; i < keys.Length; i++)
            if (i != slot && _tilesets.TryGetValue(keys[i], out var ts) && ts.CellSize != tileset.CellSize)
                throw new ArgumentException(DomainStrings.Tile_TilesetCellSizeMismatch, nameof(tileset));
        Edit(DomainStrings.Tile_ChangeTileset, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            _tilesets[tileset.Key] = tileset;
            layer.Tiles = layer.Tiles! with { TilesetKeys = keys.SetItem(slot, tileset.Key) };
        });
    }

    /// <summary>
    /// 타일맵 레이어가 쓸 타일셋을 맨 뒤 자리에 더한다(실행취소 한 단계, 프로젝트에 담음 — 같은 이름표가 있으면 이것으로 바꿈). 이미 쓰는 타일셋이면 아무것도 하지 않고 false.
    /// 칸 크기가 첫 타일셋과 다르면 <see cref="ArgumentException"/>.
    /// </summary>
    public bool AddLayerTileset(Layer layer, Tileset tileset)
    {
        RequireTilemap(layer);
        var keys = layer.Tiles!.TilesetKeys;
        if (keys.Contains(tileset.Key)) return false;
        if (_tilesets.TryGetValue(keys[0], out var primary) && primary.CellSize != tileset.CellSize)
            throw new ArgumentException(DomainStrings.Tile_TilesetCellSizeMismatch, nameof(tileset));
        Edit(DomainStrings.Tile_AddLayerTileset, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            _tilesets[tileset.Key] = tileset;
            layer.Tiles = layer.Tiles! with { TilesetKeys = keys.Add(tileset.Key) };
        });
        return true;
    }

    /// <summary>
    /// 타일맵 레이어에서 자리 <paramref name="slot"/>의 타일셋을 뺀다(실행취소 한 단계). 하나뿐이면 아무것도 하지 않고 false.
    /// 그 자리의 칸(자동 타일 칸은 세트의 자리로 셈)은 모든 층에서 지우고 뒤 자리의 칸은 Id를 한 자리(<see cref="TileSources.Stride"/>) 당긴다.
    /// 첫 타일셋을 빼면 다음 타일셋이 첫 타일셋이 되고, 맵의 층과 숨긴·잠근 층은 그 타일셋의 층(이름이 같은 층, 없으면 같은 차례)으로 옮긴다.
    /// 남은 자동 타일 칸은 새 합친 타일셋으로 다시 고른다(빠진 세트와 무리로 이어졌을 수 있으므로).
    /// </summary>
    public bool RemoveLayerTileset(Layer layer, int slot)
    {
        RequireTilemap(layer);
        int count = layer.Tiles!.TilesetKeys.Length;
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, count);
        if (count == 1) return false;
        Edit(DomainStrings.Tile_RemoveLayerTileset, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            var data = layer.Tiles!;
            var keys = data.TilesetKeys.RemoveAt(slot);
            Func<int, int> floorOf = f => f;
            if (slot == 0 && _tilesets.TryGetValue(data.TilesetKeys[0], out var oldPrimary) && _tilesets.TryGetValue(keys[0], out var newPrimary))
                floorOf = f => TileSources.MapFloor(oldPrimary, newPrimary, f);
            static int Shift(int id, int slot) => TileSources.Slot(id) > slot ? id - TileSources.Stride : id;
            var map = TileMap.FromCells(data.Map.All
                .Where(p => TileSources.Slot(p.Cell) != slot)
                .Select(p => new PlacedTile(floorOf(p.Floor), p.At, p.Cell with { Tile = Shift(p.Cell.Tile, slot), AutoTile = Shift(p.Cell.AutoTile, slot) })));
            layer.Tiles = data with
            {
                TilesetKeys = keys,
                Map = map,
                HiddenFloors = [.. data.HiddenFloors.Select(floorOf).Distinct()],
                LockedFloors = [.. data.LockedFloors.Select(floorOf).Distinct()],
            };
            if (TilesetOf(layer) is { AutoTiles.Length: > 0 } combined) layer.Tiles = layer.Tiles with { Map = AutoTiler.RefreshAll(combined, map) };
        });
        return true;
    }

    // ───────────── 타일 스트로크 ─────────────

    /// <summary>타일 스트로크(끌어 찍기)가 열려 있는지.</summary>
    public bool IsTileStrokeActive => _tileStroke is not null;

    /// <summary>
    /// 타일맵 레이어에 끌어 찍기를 시작한다: 이 뒤 <see cref="SetStrokeTileMap"/>로 고친 것이 <see cref="CommitTileStroke"/>에서 실행취소 한 단계가 된다.
    /// 잠긴 레이어면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public void BeginTileStroke(Layer layer)
    {
        RequireTilemap(layer);
        CommitOpenStrokes();
        if (layer.Locked) throw new InvalidOperationException(DomainStrings.Project_LockedLayerCannotDraw);
        _tileStroke = (layer, Snapshot.Capture(this), layer.Tiles!);
    }

    /// <summary>열린 타일 스트로크의 맵을 바꾼다(화면을 다시 그리게 알림, 수정됨 표시).</summary>
    public void SetStrokeTileMap(TileMap map)
    {
        if (_tileStroke is not { } s) throw new InvalidOperationException(DomainStrings.Tile_NoStroke);
        if (ReferenceEquals(s.Layer.Tiles!.Map, map)) return;
        s.Layer.Tiles = s.Layer.Tiles! with { Map = map };
        NotifyChanged(ProjectChange.Pixels);
    }

    /// <summary>타일 스트로크를 실행취소 한 단계로 확정한다. 바뀐 것이 없으면 기록하지 않고 false.</summary>
    public bool CommitTileStroke(string label)
    {
        if (_tileStroke is not { } s) return false;
        _tileStroke = null;
        if (ReferenceEquals(s.Layer.Tiles!.Map, s.Data.Map)) return false;
        History.Push(new SnapshotCommand(this, label, s.Before, Snapshot.Capture(this), ProjectChange.Pixels));
        return true;
    }

    /// <summary>타일 스트로크를 버린다(시작 전 맵으로).</summary>
    public void CancelTileStroke()
    {
        if (_tileStroke is not { } s) return;
        _tileStroke = null;
        if (ReferenceEquals(s.Layer.Tiles, s.Data)) return;
        s.Layer.Tiles = s.Data;
        NotifyChanged(ProjectChange.Pixels, markDirty: false);
    }

    /// <summary>열린 픽셀·타일 스트로크를 확정한다(다른 편집·실행취소 전에).</summary>
    private void CommitOpenStrokes()
    {
        if (_stroke is not null) CommitCelStroke(DomainStrings.Project_Edit);
        if (_tileStroke is not null) CommitTileStroke(DomainStrings.Tile_Paint);
    }

    /// <summary>타일맵 레이어의 층 하나를 숨기거나 보이고, 잠그거나 푼다(null은 그대로, 실행취소 한 단계).</summary>
    public void SetTileFloorState(Layer layer, int floor, bool? hidden = null, bool? locked = null)
    {
        RequireTilemap(layer);
        var data = layer.Tiles!;
        static System.Collections.Immutable.ImmutableArray<int> Toggle(System.Collections.Immutable.ImmutableArray<int> list, int floor, bool? on)
            => on is not { } v || list.Contains(floor) == v ? list : v ? list.Add(floor) : list.Remove(floor);
        var next = data with { HiddenFloors = Toggle(data.HiddenFloors, floor, hidden), LockedFloors = Toggle(data.LockedFloors, floor, locked) };
        if (next.HiddenFloors == data.HiddenFloors && next.LockedFloors == data.LockedFloors) return;
        Edit(DomainStrings.Tile_FloorState, ProjectChange.Layers | ProjectChange.Pixels, () => layer.Tiles = next);
    }

    // ───────────── 합성 ─────────────

    /// <summary>타일맵 레이어의 그린 결과(바뀌지 않는 한 다시 쓰는 캔버스 크기 셀과 캔버스 밖 그림 — 움직이는 타일이 있으면 그림 묶음 번호마다). 층 보기를 입힌 것은 따로.</summary>
    private sealed class TilemapRender(Tileset tileset, TilemapData data, Size canvas, TileFloorView? view)
    {
        public Tileset Tileset { get; } = tileset;
        public TilemapData Data { get; } = data;
        public Size Canvas { get; } = canvas;
        public TileFloorView? View { get; } = view;
        public readonly Dictionary<long, PixelImage?> Cels = [];
        public readonly Dictionary<long, CelOverflow?> Outside = [];
        public Rectangle? Bounds;

        /// <summary>그릴 층: 숨기지 않았고, "고른 층만" 보기면 고른 층.</summary>
        public bool Shows(int floor) => Data.ShowsFloor(floor) && (View is not { Mode: TileFloorViewMode.Only } v || v.Floor == floor);

        /// <summary>층 불투명도: "고른 층 강조" 보기면 다른 층을 흐리게(층 보기가 없으면 null).</summary>
        public Func<int, byte>? Opacity => View is { Mode: TileFloorViewMode.Dim } v ? f => f == v.Floor ? (byte)255 : TileFloorView.DimOpacity : null;
    }

    // 그림 묶음마다 담아 두는 그림 수(넘으면 비움)
    private const int TileRenderCacheLimit = 64;

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Layer, TilemapRender> _tileRenders = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Layer, TilemapRender> _tileViewRenders = new();

    /// <summary>
    /// 화면의 층 보기(저장하지 않음, 없으면 null): 앱이 캔버스 그림을 만드는 동안만 넣고 끝나면 null로 되돌린다 — 그 레이어만 고른 층을 강조하거나 고른 층만 그린다.
    /// 미리보기·내보내기·저장은 늘 모든 층.
    /// </summary>
    public TileFloorView? TileFloorView { get; set; }

    /// <summary>
    /// 움직이는 타일을 화면에서 돌리는 시계(ms, 저장하지 않음): 프레임 <c>f</c>의 타일 시각 = 프레임 <c>f</c>가 시작하는 시각 + 이 값.
    /// 앱은 화면 그림을 만들 때만 넣고 끝나면 0으로 되돌린다(내보내기·저장은 늘 프레임 시각 기준).
    /// </summary>
    public long TileClockMs { get; set; }

    /// <summary>레이어가 쓰는 타일셋에 움직이는 타일이 있는지.</summary>
    public bool HasTileAnimation => UsedTilesets.Any(t => t.HasAnimation);

    /// <summary>프레임 <paramref name="frameIndex"/>의 타일 시각(ms): 그 프레임이 시작하는 시각 + <see cref="TileClockMs"/>.</summary>
    public long TileTimeMs(int frameIndex)
    {
        long start = 0;
        for (int i = 0; i < frameIndex && i < _frames.Count; i++) start += _frames[i].DurationMs;
        return start + TileClockMs;
    }

    private TilemapRender? TileRenderOf(Layer layer)
    {
        if (!layer.IsTilemap || layer.Tiles is not { } data || TilesetOf(layer) is not { } tileset) return null;
        var view = TileFloorView is { } v && ReferenceEquals(v.Layer, layer) ? v : null;
        var cache = view is null ? _tileRenders : _tileViewRenders;
        if (cache.TryGetValue(layer, out var r) && ReferenceEquals(r.Tileset, tileset) && ReferenceEquals(r.Data, data) && r.Canvas == Size && r.View == view) return r;
        r = new TilemapRender(tileset, data, Size, view);
        cache.AddOrUpdate(layer, r);
        return r;
    }

    private static Rectangle MapBounds(TilemapRender r)
        => r.Bounds ??= TileMapRenderer.PixelBounds(r.Tileset, r.Data.Map, r.Data.Origin, r.Shows);

    /// <summary>
    /// 타일맵 레이어의 셀: 맵을 캔버스 크기로 그린 그림(그릴 타일이 없으면 null, 움직이는 타일은 프레임 <paramref name="frameIndex"/>의 타일 시각).
    /// 만든 뒤 고치지 않는다(맵이 바뀌면 새 그림).
    /// </summary>
    private PixelImage? TilemapCel(Layer layer, int frameIndex)
    {
        if (TileRenderOf(layer) is not { } r) return null;
        long time = TileTimeMs(frameIndex), phase = r.Tileset.AnimationPhase(time);
        if (!r.Cels.TryGetValue(phase, out var cel))
        {
            var bounds = Rectangle.Intersect(MapBounds(r), Bounds);
            cel = bounds.Width < 1 || bounds.Height < 1 ? null : TileMapRenderer.RenderArea(r.Tileset, r.Data.Map, r.Data.Origin, Bounds, r.Shows, time, r.Opacity);
            if (r.Cels.Count >= TileRenderCacheLimit) r.Cels.Clear();
            r.Cels[phase] = cel;
        }
        return cel;
    }

    /// <summary>타일맵 레이어의 캔버스 밖 그림(캔버스 밖 그림을 남기는 범위 안, 캔버스와 겹치는 칸은 투명). 없으면 null.</summary>
    private CelOverflow? TilemapOutside(Layer layer, int frameIndex)
    {
        if (TileRenderOf(layer) is not { } r) return null;
        long time = TileTimeMs(frameIndex), phase = r.Tileset.AnimationPhase(time);
        if (!r.Outside.TryGetValue(phase, out var outside))
        {
            var bounds = MapBounds(r);
            outside = null;
            if (!bounds.IsEmpty && !Bounds.Contains(bounds))
            {
                var keep = Rectangle.Intersect(bounds, OverflowBounds);
                if (keep.Width > 0 && keep.Height > 0)
                {
                    var image = TileMapRenderer.RenderArea(r.Tileset, r.Data.Map, r.Data.Origin, keep, r.Shows, time, r.Opacity);
                    image.FillRect(new Rectangle(-keep.X, -keep.Y, _width, _height), default);
                    outside = new CelOverflow(image, keep.Location);
                }
            }
            if (r.Outside.Count >= TileRenderCacheLimit) r.Outside.Clear();
            r.Outside[phase] = outside;
        }
        return outside;
    }

    /// <summary>타일맵 레이어 그림이 닿는 캔버스 사각형(캔버스 밖 포함). 없으면 빈 것.</summary>
    public Rectangle TilemapBounds(Layer layer)
        => layer.Tiles is { } data && TilesetOf(layer) is { } ts ? TileMapRenderer.PixelBounds(ts, data.Map, data.Origin) : Rectangle.Empty;

    /// <summary>캔버스 크기 바꾸기에서 그림이 옮겨 간 만큼 타일맵 격자 원점도 옮긴다(Edit 안에서, 배율은 무시 — 타일은 늘이지 않음).</summary>
    private void ShiftTilemaps(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        foreach (var layer in _layers)
            if (layer.IsTilemap && layer.Tiles is { } data)
                layer.Tiles = data with { Origin = new Point(data.Origin.X + dx, data.Origin.Y + dy) };
    }

    // ───────────── 레이어로 변환 ─────────────

    /// <summary>
    /// 타일맵 레이어를 그룹으로 바꾼다(실행취소 한 단계): 층마다(아래 층부터) 그 층 그림을 감싼 크기의 부위 하나. 프레임마다 그 프레임의 타일 시각(내보내기와 같음 —
    /// 프레임이 시작하는 시각)의 그림이라 움직이는 타일이 프레임을 따라 움직이고, 같은 그림이 되는 프레임끼리는 한 그림을 나눠 쓴다(링크).
    /// 프레임이 하나뿐인데 맵에 움직이는 타일이 있으면 먼저 프레임을 맵의 움직이는 타일 모두가 한 바퀴 도는 길이(주기들의 최소공배수)만큼, 그림이 바뀌는 간격
    /// (장당 시간들의 최대공약수)마다 늘린다(다른 레이어는 원래 프레임과 링크) — 주기가 다른 타일도 끊김 없이 되풀이된다.
    /// 그룹은 타일맵 레이어의 이름·보이기·잠금·불투명도·효과·피벗을 물려받는다. 그릴 것이 없는 층은 빠진다.
    /// </summary>
    public Layer ConvertTilemapToGroup(int index)
    {
        var layer = RequireTilemap(_layers[index]);
        var tileset = TilesetOf(layer) ?? throw new InvalidOperationException(DomainStrings.Tile_TilesetNotInProject);
        var data = layer.Tiles!;
        bool grouping = _frames.Count == 1 && MapAnimation(tileset, data.Map) is { Frames: > 1 } && !History.InGroup;
        if (grouping) History.BeginGroup(DomainStrings.Tile_ConvertToGroup);
        try
        {
            if (_frames.Count == 1 && MapAnimation(tileset, data.Map) is { Frames: > 1 } anim)
            {
                ExtendFrames(0, 0, anim.Frames, FrameExtendMode.Stretch);
                for (int i = 0; i < _frames.Count; i++) SetFrameDuration(i, anim.StepMs);
            }
            return ConvertTilemapCore(layer, tileset, data);
        }
        finally
        {
            if (grouping) History.EndGroup();
        }
    }

    /// <summary>프레임 수 상한(맵의 움직이는 타일 한 바퀴로 프레임을 늘릴 때 — 넘으면 간격을 넓힘).</summary>
    private const int MaxConvertFrames = 240;

    /// <summary>
    /// 맵에 쓰인 움직이는 타일이 한 바퀴 도는 데 필요한 프레임: 그림이 바뀌는 간격(장당 시간들의 최대공약수, ms)과 한 바퀴(주기들의 최소공배수)를 그 간격으로 나눈 수
    /// (<see cref="MaxConvertFrames"/>를 넘으면 간격을 넓혀 맞춤). 움직이는 타일이 없으면 null.
    /// </summary>
    private static (int StepMs, int Frames)? MapAnimation(Tileset tileset, TileMap map)
    {
        long step = 0, period = 0;
        var seen = new HashSet<int>();
        foreach (var p in map.All)
        {
            if (!seen.Add(p.Cell.Tile) || tileset.Find(p.Cell.Tile) is not { IsAnimated: true } def) continue;
            long ms = Math.Max(TileDef.MinFrameMs, def.FrameMs), cycle = ms * def.FrameCount;
            step = step == 0 ? ms : Gcd(step, ms);
            period = period == 0 ? cycle : Math.Min(period / Gcd(period, cycle) * cycle, 600_000);
        }
        if (step == 0) return null;
        long frames = period / step;
        if (frames > MaxConvertFrames)
        {
            step = (period + MaxConvertFrames - 1) / MaxConvertFrames;
            frames = (period + step - 1) / step;
        }
        return ((int)step, (int)frames);

        static long Gcd(long a, long b)
        {
            while (b != 0) (a, b) = (b, a % b);
            return a;
        }
    }

    private Layer ConvertTilemapCore(Layer layer, Tileset tileset, TilemapData data)
    {
        return Edit(DomainStrings.Tile_ConvertToGroup, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            int at = _layers.IndexOf(layer);
            _layers.RemoveAt(at);
            var group = new Layer(UniqueLayerName(layer.Name))
            {
                Kind = LayerKind.Group,
                Visible = layer.Visible,
                Locked = layer.Locked,
                Opacity = layer.Opacity,
                Effects = layer.Effects,
                Pivot = layer.Pivot,
                EffectsCollapsed = layer.EffectsCollapsed,
            };
            int linkColor = 0;
            foreach (var part in ConvertParts(tileset, data))
            {
                var bounds = TileMapRenderer.PixelBounds(tileset, data.Map, data.Origin, part.Floors, part.Classes);
                if (bounds.Width < 1 || bounds.Height < 1) continue;
                bounds = Rectangle.Intersect(bounds, OverflowBounds);
                if (bounds.Width < 1 || bounds.Height < 1 || bounds.Width > PixelImage.MaxSize || bounds.Height > PixelImage.MaxSize) continue;
                // 프레임마다 그 프레임 시각의 그림: 이 부위에 쓰인 움직이는 타일들이 보이는 그림 차례가 같은 프레임끼리는 한 장을 나눠 씀
                var animated = data.Map.All
                    .Where(p => part.Floors(p.Floor) && (part.Classes is null || part.Classes(tileset.DrawClassOf(p.Cell))))
                    .Select(p => p.Cell.Tile).Distinct()
                    .Select(tileset.Find).Where(d => d is { IsAnimated: true }).Select(d => d!).ToList();
                var byPhase = new Dictionary<string, (PixelImage Image, int Frames)>();
                var phases = new string[_frames.Count];
                long start = 0;
                for (int i = 0; i < _frames.Count; i++)
                {
                    long time = start;
                    string phase = string.Join(',', animated.Select(d => d.FrameAt(time)));
                    phases[i] = phase;
                    byPhase[phase] = byPhase.TryGetValue(phase, out var hit)
                        ? (hit.Image, hit.Frames + 1)
                        : (TileMapRenderer.RenderArea(tileset, data.Map, data.Origin, bounds, part.Floors, time, classes: part.Classes), 1);
                    start += _frames[i].DurationMs;
                }
                foreach (var (image, frames) in byPhase.Values)
                    if (frames > 1) Tagged(image, linkColor++);
                var layerPart = new Layer(UniqueLayerName(part.Name.Length > 0 ? part.Name : DomainStrings.Project_DefaultPartName))
                {
                    Kind = LayerKind.Part,
                    Parent = group,
                    PartSize = bounds.Size,
                    PartOffset = bounds.Location,
                    Pivot = Pivot.Center,
                    Visible = part.Visible,
                    Locked = part.Locked,
                };
                _layers.Insert(at++, layerPart);
                for (int i = 0; i < _frames.Count; i++) _cels[new CelKey(layerPart, _frames[i], null)] = byPhase[phases[i]].Image;
            }
            _layers.Insert(at, group);
            foreach (var f in _frames)
                if (f.LayerPivots.Remove(layer, out var pivot)) f.LayerPivots[group] = pivot;
            if (ReferenceEquals(PivotLayer, layer)) PivotLayer = group;
            return group;
        });
    }

    /// <summary>레이어로 변환할 부위(아래 → 위): 이름, 담을 층·그리기 묶음(null이면 모두), 보이기·잠금.</summary>
    private sealed record ConvertPart(string Name, Func<int, bool> Floors, Func<TileDrawClass, bool>? Classes, bool Visible, bool Locked);

    /// <summary>
    /// 변환 부위: 층 차례로 그리는 타일셋은 층마다 하나. 위치로 그리는 타일셋은 그리는 순서를 지키게 바닥 묶음을 층마다, 세운 것은 보이는 층을 모두 한 부위로
    /// (숨긴 층의 세운 것은 층마다 숨긴 부위로), 머리 위 묶음을 층마다.
    /// </summary>
    private static List<ConvertPart> ConvertParts(Tileset tileset, TilemapData data)
    {
        var parts = new List<ConvertPart>();
        if (tileset.SortMode != TileSortMode.ByPosition)
        {
            foreach (var floor in tileset.Floors)
                parts.Add(new ConvertPart(floor.Name, id => id == floor.Id, null, data.ShowsFloor(floor.Id), data.IsFloorLocked(floor.Id)));
            return parts;
        }
        foreach (var floor in tileset.Floors)
            parts.Add(new ConvertPart(floor.Name, id => id == floor.Id, c => c == TileDrawClass.Ground, data.ShowsFloor(floor.Id), data.IsFloorLocked(floor.Id)));
        parts.Add(new ConvertPart(DomainStrings.Tile_PartSorted, data.ShowsFloor, c => c == TileDrawClass.Sorted, true, false));
        foreach (var floor in tileset.Floors)
            if (!data.ShowsFloor(floor.Id))
                parts.Add(new ConvertPart(DomainStrings.FormatTile_PartFloorSorted(floor: floor.Name), id => id == floor.Id, c => c == TileDrawClass.Sorted, false, data.IsFloorLocked(floor.Id)));
        foreach (var floor in tileset.Floors)
            parts.Add(new ConvertPart(DomainStrings.FormatTile_PartFloorOverhead(floor: floor.Name), id => id == floor.Id, c => c == TileDrawClass.Overhead,
                data.ShowsFloor(floor.Id), data.IsFloorLocked(floor.Id)));
        return parts;
    }

    // ───────────── 타일셋 파일 ─────────────

    /// <summary>
    /// 저장할 때 타일셋 파일에도 써야 하는 것: 프로젝트가 담았고 파일 경로가 있으며, 파일에서 읽었거나 마지막으로 쓴 값과 다른 타일셋.
    /// </summary>
    public IReadOnlyList<Tileset> TilesetsToWrite
        => AllTilesets.Where(t => t.SourcePath is not null && !(_tilesetSynced.TryGetValue(t.Key, out var s) && ReferenceEquals(s, t))).ToList();

    /// <summary>타일셋을 파일과 맞춘 것으로 적어 둔다(파일에서 읽었거나 썼을 때).</summary>
    public void MarkTilesetSynced(Tileset tileset) => _tilesetSynced[tileset.Key] = tileset;

    /// <summary>파일 읽기·가져오기: 타일셋을 그대로 담는다(기록 없음).</summary>
    internal void PutTilesetRaw(Tileset tileset) => _tilesets[tileset.Key] = tileset;
}
