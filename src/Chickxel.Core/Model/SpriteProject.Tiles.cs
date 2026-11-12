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

    /// <summary>타일맵 레이어가 쓰는 타일셋(타일맵 레이어가 아니거나 없으면 null).</summary>
    public Tileset? TilesetOf(Layer layer) => layer.Tiles is { } t && _tilesets.TryGetValue(t.TilesetKey, out var ts) ? ts : null;

    /// <summary>레이어들이 쓰는 타일셋(저장할 것, 이름표 차례).</summary>
    public IReadOnlyList<Tileset> UsedTilesets
        => _layers.Where(l => l.IsTilemap).Select(TilesetOf).OfType<Tileset>().Distinct().OrderBy(t => t.Key, StringComparer.Ordinal).ToList();

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
            Tiles = new TilemapData(tileset.Key, TileMap.Empty, Point.Empty),
        };
        Edit(DomainStrings.Tile_AddLayer, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            _tilesets[tileset.Key] = tileset;
            _layers.Insert(at, layer);
        });
        return layer;
    }

    /// <summary>
    /// 타일셋을 바꾼다(같은 이름표, 실행취소 한 단계). <paramref name="remap"/>이 있으면 그 타일셋을 쓰는 모든 맵을 같이 고친다(지운 타일 빼기 등).
    /// 자동 타일 세트가 바뀌었으면(새 값·지움) 모든 자동 타일 칸을 다시 고른다(무리로 다른 세트와 이어지므로).
    /// </summary>
    public void EditTileset(string label, Tileset tileset, Func<TileMap, TileMap>? remap = null)
    {
        if (!_tilesets.TryGetValue(tileset.Key, out var old)) throw new ArgumentException(DomainStrings.Tile_TilesetNotInProject, nameof(tileset));
        if (ReferenceEquals(old, tileset) && remap is null) return;
        Edit(label, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            _tilesets[tileset.Key] = tileset;
            bool setsChanged = tileset.AutoTiles.Length != old.AutoTiles.Length || tileset.AutoTiles.Any(a => !ReferenceEquals(old.FindAutoTile(a.Id), a));
            foreach (var layer in _layers)
            {
                if (!layer.IsTilemap || layer.Tiles is not { } data || data.TilesetKey != tileset.Key) continue;
                var map = remap?.Invoke(data.Map) ?? data.Map;
                if (setsChanged) map = AutoTiler.RefreshAll(tileset, map);
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

    /// <summary>타일맵 레이어가 쓸 타일셋을 바꾼다(프로젝트에 담은 이름표, 맵은 그대로 — 같은 Id의 타일로 보임).</summary>
    public void SetLayerTileset(Layer layer, Tileset tileset)
    {
        RequireTilemap(layer);
        if (layer.Tiles!.TilesetKey == tileset.Key && ReferenceEquals(TilesetOf(layer), tileset)) return;
        Edit(DomainStrings.Tile_ChangeTileset, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            _tilesets[tileset.Key] = tileset;
            layer.Tiles = layer.Tiles! with { TilesetKey = tileset.Key };
        });
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

    /// <summary>타일맵 레이어의 그린 결과(바뀌지 않는 한 다시 쓰는 캔버스 크기 셀과 캔버스 밖 그림 — 움직이는 타일이 있으면 그림 묶음 번호마다).</summary>
    private sealed class TilemapRender(Tileset tileset, TilemapData data, Size canvas)
    {
        public Tileset Tileset { get; } = tileset;
        public TilemapData Data { get; } = data;
        public Size Canvas { get; } = canvas;
        public readonly Dictionary<long, PixelImage?> Cels = [];
        public readonly Dictionary<long, CelOverflow?> Outside = [];
        public Rectangle? Bounds;
    }

    // 그림 묶음마다 담아 두는 그림 수(넘으면 비움)
    private const int TileRenderCacheLimit = 64;

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Layer, TilemapRender> _tileRenders = new();

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
        if (_tileRenders.TryGetValue(layer, out var r) && ReferenceEquals(r.Tileset, tileset) && ReferenceEquals(r.Data, data) && r.Canvas == Size) return r;
        r = new TilemapRender(tileset, data, Size);
        _tileRenders.AddOrUpdate(layer, r);
        return r;
    }

    private static Rectangle MapBounds(TilemapRender r)
        => r.Bounds ??= TileMapRenderer.PixelBounds(r.Tileset, r.Data.Map, r.Data.Origin, r.Data.ShowsFloor);

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
            cel = bounds.Width < 1 || bounds.Height < 1 ? null : TileMapRenderer.RenderArea(r.Tileset, r.Data.Map, r.Data.Origin, Bounds, r.Data.ShowsFloor, time);
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
                    var image = TileMapRenderer.RenderArea(r.Tileset, r.Data.Map, r.Data.Origin, keep, r.Data.ShowsFloor, time);
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
    /// 타일맵 레이어를 그룹으로 바꾼다(실행취소 한 단계): 층마다(아래 층부터) 그 층 그림을 감싼 크기의 부위 하나 — 모든 프레임이 같은 그림(링크).
    /// 그룹은 타일맵 레이어의 이름·보이기·잠금·불투명도·효과·피벗을 물려받는다. 그릴 것이 없는 층은 빠진다.
    /// </summary>
    public Layer ConvertTilemapToGroup(int index)
    {
        var layer = RequireTilemap(_layers[index]);
        var tileset = TilesetOf(layer) ?? throw new InvalidOperationException(DomainStrings.Tile_TilesetNotInProject);
        var data = layer.Tiles!;
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
            foreach (var floor in tileset.Floors)
            {
                bool Only(int id) => id == floor.Id;
                var bounds = TileMapRenderer.PixelBounds(tileset, data.Map, data.Origin, Only);
                if (bounds.Width < 1 || bounds.Height < 1) continue;
                bounds = Rectangle.Intersect(bounds, OverflowBounds);
                if (bounds.Width < 1 || bounds.Height < 1 || bounds.Width > PixelImage.MaxSize || bounds.Height > PixelImage.MaxSize) continue;
                var image = TileMapRenderer.RenderArea(tileset, data.Map, data.Origin, bounds, Only);
                if (_frames.Count > 1) image = Tagged(image, 0);
                var part = new Layer(UniqueLayerName(floor.Name.Length > 0 ? floor.Name : DomainStrings.Project_DefaultPartName))
                {
                    Kind = LayerKind.Part,
                    Parent = group,
                    PartSize = bounds.Size,
                    PartOffset = bounds.Location,
                    Pivot = Pivot.Center,
                    Visible = data.ShowsFloor(floor.Id),
                    Locked = data.IsFloorLocked(floor.Id),
                };
                _layers.Insert(at++, part);
                foreach (var f in _frames) _cels[new CelKey(part, f, null)] = image;
            }
            _layers.Insert(at, group);
            foreach (var f in _frames)
                if (f.LayerPivots.Remove(layer, out var pivot)) f.LayerPivots[group] = pivot;
            if (ReferenceEquals(PivotLayer, layer)) PivotLayer = group;
            return group;
        });
    }

    // ───────────── 타일셋 파일 ─────────────

    /// <summary>
    /// 저장할 때 타일셋 파일에도 써야 하는 것: 레이어가 쓰고 파일 경로가 있으며, 파일에서 읽었거나 마지막으로 쓴 값과 다른 타일셋.
    /// </summary>
    public IReadOnlyList<Tileset> TilesetsToWrite
        => UsedTilesets.Where(t => t.SourcePath is not null && !(_tilesetSynced.TryGetValue(t.Key, out var s) && ReferenceEquals(s, t))).ToList();

    /// <summary>타일셋을 파일과 맞춘 것으로 적어 둔다(파일에서 읽었거나 썼을 때).</summary>
    public void MarkTilesetSynced(Tileset tileset) => _tilesetSynced[tileset.Key] = tileset;

    /// <summary>파일 읽기·가져오기: 타일셋을 그대로 담는다(기록 없음).</summary>
    internal void PutTilesetRaw(Tileset tileset) => _tilesets[tileset.Key] = tileset;
}
