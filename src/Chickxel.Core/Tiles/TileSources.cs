using System.Collections.Immutable;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 타일맵 레이어 하나가 여러 타일셋을 겹쳐 쓸 때(<see cref="TilemapData.TilesetKeys"/>)의 Id 규칙과 합친 타일셋.
/// 맵의 타일 Id·자동 타일 세트 Id = 자리(<c>slot</c>) × <see cref="Stride"/> + 그 타일셋 안의 Id. 자리 0(첫 타일셋)은 Id가 그대로라 타일셋 하나인 맵은 바뀌지 않는다.
/// 첫 타일셋이 칸 크기를 정하고, 다른 타일셋의 층은 첫 타일셋에 같은 이름 층이 있으면 같이 쓰고 없으면 더한다(<see cref="Combine"/>).
/// </summary>
public static class TileSources
{
    /// <summary>자리 하나가 쓰는 Id 폭(이 이상인 타일셋 안 Id는 쓰지 않음).</summary>
    public const int Stride = 1 << 20;

    /// <summary>맵 Id의 자리(타일셋 차례).</summary>
    public static int Slot(int globalId) => globalId / Stride;

    /// <summary>칸이 속한 자리: 자동 타일로 찍었으면 그 세트의 자리, 아니면 타일의 자리.</summary>
    public static int Slot(TileCell cell) => Slot(cell.AutoTile != 0 ? cell.AutoTile : cell.Tile);

    /// <summary>맵 Id → 그 타일셋 안의 Id.</summary>
    public static int Local(int globalId) => globalId % Stride;

    /// <summary>자리 <paramref name="slot"/>의 타일셋 안 Id → 맵 Id.</summary>
    public static int Global(int slot, int localId) => slot * Stride + localId;

    /// <summary>자리 <paramref name="slot"/>의 Id 오프셋.</summary>
    public static int Offset(int slot) => slot * Stride;

    /// <summary>
    /// 타일셋들을 하나로 합친다(자리 차례). 하나뿐이면 그것 그대로(같은 객체). 아니면 첫 타일셋에 다른 타일셋의 타일·자동 타일 세트를 Id를 자리만큼 옮겨 더한 것.
    /// 층: 다른 타일셋의 층은 첫 타일셋에 이름이 같은 층이 있으면 그 층을 같이 쓰고, 땅 층(서로 지우는 자동 타일 땅 — 물·모래·풀 등)은 첫 타일셋의 바닥 층 하나로 모으며
    /// (원래 층 차례는 <see cref="TileDef.FloorRank"/>로 — 위층 땅 가장자리가 아래층 땅을 덮음), 나머지는 그 층을 더한다(Id = 자리 오프셋 + 원래 Id — <see cref="CombinedFloor"/>).
    /// 더한 층은 그 타일셋에서 바로 앞 층(같이 쓰는 층이나 앞서 더한 층) 바로 뒤에 놓아 그 타일셋의 층 차례를 지킨다.
    /// 그래서 서로 다른 층(풀과 장식처럼)이 한 층으로 뭉쳐 한 칸에 하나만 남지 않는다.
    /// 칸 크기가 첫 타일셋과 다른 타일셋과 Id가 <see cref="Stride"/> 이상인 타일은 뺀다. 무리 이름은 그대로라 다른 타일셋의 같은 무리 세트끼리도 이어진다.
    /// 더한 타일셋 중 하나라도 위치로 그리면(<see cref="TileSortMode.ByPosition"/>) 합친 것도 위치로 그린다 — 층 차례로 그리는 타일셋의 타일은 모두 바닥 묶음이라
    /// 그 타일들끼리는 층 차례 → 차지 칸 아래 차례 그대로이고, 위치로 그리는 타일셋의 벽·물건이 그 위에 기준선대로 그려진다.
    /// </summary>
    public static Tileset Combine(IReadOnlyList<Tileset> sources)
    {
        if (sources.Count == 0) throw new ArgumentException(null, nameof(sources));
        var primary = sources[0];
        if (sources.Count == 1) return primary;
        var tiles = ImmutableArray.CreateBuilder<TileDef>(primary.Tiles.Length);
        var sets = ImmutableArray.CreateBuilder<AutoTileSet>(primary.AutoTiles.Length);
        var floors = primary.Floors.ToList();
        tiles.AddRange(primary.Tiles);
        sets.AddRange(primary.AutoTiles);
        var sortMode = primary.SortMode;
        for (int slot = 1; slot < sources.Count; slot++)
        {
            var source = sources[slot];
            if (source.CellWidth != primary.CellWidth || source.CellHeight != primary.CellHeight) continue;
            if (source.SortMode == TileSortMode.ByPosition) sortMode = TileSortMode.ByPosition;
            // 이 타일셋의 층: 같이 쓰는 층은 그 자리, 없는 층은 앞 층 바로 뒤에 더한다(처음이 없는 층이면 맨 앞)
            if (primary.Floors.Length > 0)
            {
                int insertAt = 0;
                foreach (var f in source.Floors)
                {
                    int id = CombinedFloor(slot, source, primary, f.Id);
                    int at = floors.FindIndex(x => x.Id == id);
                    if (at < 0)
                    {
                        floors.Insert(insertAt, new TileFloor(id, f.Name));
                        at = insertAt;
                    }
                    insertAt = at + 1;
                }
            }
            int offset = Offset(slot);
            int Floor(int f) => CombinedFloor(slot, source, primary, f);
            var sourceGround = GroundFloors(source);
            foreach (var t in source.Tiles)
                if (t.Id >= 0 && t.Id < Stride)
                {
                    int floor = Floor(t.Floor);
                    // 바닥 층에 모은 땅은 원래 층 차례대로(첫 타일셋의 바닥 타일 다음)
                    int rank = sourceGround.Contains(t.Floor) && floor == GroundFloor(primary) ? 1 + source.FloorOrder(t.Floor) : t.FloorRank;
                    tiles.Add(t with { Id = offset + t.Id, Floor = floor, FloorRank = rank });
                }
            foreach (var a in source.AutoTiles)
            {
                if (a.Id < 0 || a.Id >= Stride) continue;
                sets.Add(a with
                {
                    Id = offset + a.Id,
                    Floor = Floor(a.Floor),
                    FilledFloors = a.FilledFloors.IsDefaultOrEmpty ? a.FilledFloors : [.. a.FilledFloors.Select(Floor).Distinct()],
                    ClearFloors = a.ClearFloors.IsDefaultOrEmpty ? a.ClearFloors : [.. a.ClearFloors.Select(Floor).Distinct()],
                    Rules = [.. a.Rules.Select(r => r with
                    {
                        Tiles = r.Tiles.IsDefaultOrEmpty ? r.Tiles : [.. r.Tiles.Where(c => c.Tile >= 0 && c.Tile < Stride).Select(c => c with { Tile = offset + c.Tile })],
                    })],
                    CornerTiles = a.CornerTiles.IsDefaultOrEmpty ? a.CornerTiles : [.. a.CornerTiles.Select(t => t > 0 && t < Stride ? offset + t : 0)],
                });
            }
        }
        return primary with { Tiles = tiles.ToImmutable(), AutoTiles = sets.ToImmutable(), SortMode = sortMode, Floors = [.. floors] };
    }

    /// <summary>
    /// 합친 타일셋에서 자리 <paramref name="slot"/>의 타일셋 <paramref name="source"/>의 층 <paramref name="floorId"/>: 첫 타일셋 <paramref name="primary"/>에 이름이 같은 층이 있으면
    /// 그 층, 없으면 자리 오프셋 + 원래 Id(더한 층). 첫 타일셋에 층이 없으면 그대로, <paramref name="source"/>에 없는 층이면 그 타일셋의 첫 층으로 본다.
    /// </summary>
    public static int CombinedFloor(int slot, Tileset source, Tileset primary, int floorId)
    {
        if (slot == 0 || primary.Floors.Length == 0) return floorId;
        int order = source.FloorOrder(floorId);
        if (order < 0)
        {
            if (source.Floors.Length == 0) return primary.ValidFloor(floorId);
            order = 0;
        }
        var f = source.Floors[order];
        foreach (var p in primary.Floors)
            if (string.Equals(p.Name, f.Name, StringComparison.Ordinal)) return p.Id;
        // 땅 층(물·모래·흙·풀처럼 서로 지우는 땅)은 첫 타일셋의 바닥 층 하나로
        if (GroundFloor(primary) is { } ground && GroundFloors(source).Contains(f.Id)) return ground;
        return Offset(slot) + f.Id;
    }

    /// <summary>타일셋의 땅 층: 자동 타일 세트가 찍을 때 함께 지우는 층들(<see cref="AutoTileSet.ClearFloors"/>, 둘 이상 — 한 칸에 땅 하나).</summary>
    internal static HashSet<int> GroundFloors(Tileset tileset)
    {
        var set = new HashSet<int>();
        foreach (var a in tileset.AutoTiles)
            if (a.ClearFloors.Length >= 2) set.UnionWith(a.ClearFloors);
        return set;
    }

    /// <summary>
    /// 첫 타일셋의 바닥 층: 이름이 "바닥"(기본 타일셋의 땅 층 이름)인 층, 없으면 땅 층(<see cref="GroundFloors"/>) 중 맨 아래 층. 없으면 null(땅 층도 따로 더함).
    /// </summary>
    internal static int? GroundFloor(Tileset primary)
    {
        foreach (var f in primary.Floors)
            if (string.Equals(f.Name, Chickxel.Core.Localization.DomainStrings.Tile_FloorGround, StringComparison.Ordinal)) return f.Id;
        var ground = GroundFloors(primary);
        foreach (var f in primary.Floors)
            if (ground.Contains(f.Id)) return f.Id;
        return null;
    }

    /// <summary>
    /// 예전 합치기(이름이 같은 층, 없으면 같은 차례 — 넘으면 맨 위 — 첫 타일셋의 층으로 뭉침)에서 <paramref name="source"/>의 층 <paramref name="floorId"/>가 간 층.
    /// 예전에 저장한 맵의 칸을 지금 층으로 옮길 때(<see cref="Model.SpriteProject"/>) 쓴다.
    /// </summary>
    internal static int MapFloor(Tileset source, Tileset target, int floorId)
    {
        if (target.Floors.Length == 0) return floorId;
        int order = source.FloorOrder(floorId);
        if (order < 0) return target.ValidFloor(floorId);
        var name = source.Floors[order].Name;
        foreach (var f in target.Floors)
            if (string.Equals(f.Name, name, StringComparison.Ordinal)) return f.Id;
        return target.Floors[Math.Min(order, target.Floors.Length - 1)].Id;
    }
}
