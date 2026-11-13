using System.Collections.Immutable;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 타일맵 레이어 하나가 여러 타일셋을 겹쳐 쓸 때(<see cref="TilemapData.TilesetKeys"/>)의 Id 규칙과 합친 타일셋.
/// 맵의 타일 Id·자동 타일 세트 Id = 자리(<c>slot</c>) × <see cref="Stride"/> + 그 타일셋 안의 Id. 자리 0(첫 타일셋)은 Id가 그대로라 타일셋 하나인 맵은 바뀌지 않는다.
/// 첫 타일셋이 칸 크기와 층을 정하고, 다른 타일셋의 층은 첫 타일셋의 층으로 옮겨 쓴다(<see cref="Combine"/>).
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
    /// 타일셋들을 하나로 합친다(자리 차례). 하나뿐이면 그것 그대로(같은 객체). 아니면 첫 타일셋에 다른 타일셋의 타일·자동 타일 세트를 Id를 자리만큼 옮겨 더한 것:
    /// 층은 첫 타일셋의 것만 쓰고, 다른 타일셋의 층(타일·세트의 층, "뭐라도 있음" 층)은 첫 타일셋에서 이름이 같은 층, 없으면 같은 차례(넘으면 맨 위) 층으로 옮긴다.
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
        tiles.AddRange(primary.Tiles);
        sets.AddRange(primary.AutoTiles);
        var sortMode = primary.SortMode;
        for (int slot = 1; slot < sources.Count; slot++)
        {
            var source = sources[slot];
            if (source.CellWidth != primary.CellWidth || source.CellHeight != primary.CellHeight) continue;
            if (source.SortMode == TileSortMode.ByPosition) sortMode = TileSortMode.ByPosition;
            int offset = Offset(slot);
            foreach (var t in source.Tiles)
                if (t.Id >= 0 && t.Id < Stride)
                    tiles.Add(t with { Id = offset + t.Id, Floor = MapFloor(source, primary, t.Floor) });
            foreach (var a in source.AutoTiles)
            {
                if (a.Id < 0 || a.Id >= Stride) continue;
                sets.Add(a with
                {
                    Id = offset + a.Id,
                    Floor = MapFloor(source, primary, a.Floor),
                    FilledFloors = a.FilledFloors.IsDefaultOrEmpty ? a.FilledFloors : [.. a.FilledFloors.Select(f => MapFloor(source, primary, f)).Distinct()],
                    ClearFloors = a.ClearFloors.IsDefaultOrEmpty ? a.ClearFloors : [.. a.ClearFloors.Select(f => MapFloor(source, primary, f)).Distinct()],
                    Rules = [.. a.Rules.Select(r => r with
                    {
                        Tiles = r.Tiles.IsDefaultOrEmpty ? r.Tiles : [.. r.Tiles.Where(c => c.Tile >= 0 && c.Tile < Stride).Select(c => c with { Tile = offset + c.Tile })],
                    })],
                });
            }
        }
        return primary with { Tiles = tiles.ToImmutable(), AutoTiles = sets.ToImmutable(), SortMode = sortMode };
    }

    /// <summary>
    /// <paramref name="source"/>의 층 <paramref name="floorId"/> → <paramref name="target"/>의 층: 이름이 같은 층, 없으면 같은 차례(넘으면 맨 위) 층.
    /// <paramref name="source"/>에 없는 층이면 <see cref="Tileset.ValidFloor"/>, <paramref name="target"/>에 층이 없으면 그대로.
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
