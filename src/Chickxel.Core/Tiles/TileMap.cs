using System.Collections.Immutable;
using System.Drawing;

namespace Chickxel.Core.Tiles;

/// <summary>칸 하나: 타일 Id + 뒤집기·회전 + 자동 타일로 찍었으면 그 세트 Id(아니면 0).</summary>
public readonly record struct TileCell(int Tile, TileTransform Transform = TileTransform.None, int AutoTile = 0);

/// <summary>맵에 놓인 타일 하나: 층, 기준 칸(차지 칸 왼쪽 위), 칸.</summary>
public readonly record struct PlacedTile(int Floor, Point At, TileCell Cell);

/// <summary>
/// 타일맵: 층 Id → (기준 칸 → 칸). 큰 타일은 기준 칸(차지 칸 왼쪽 위) 하나에만 들어 있고, 차지 칸은 타일셋으로 안다(<see cref="Tileset.Footprint"/>).
/// 칸 좌표는 음수도 된다(캔버스 밖). 바뀌지 않는 값이고 고치면 새 값을 만든다(실행취소 사본은 참조만 담음).
/// </summary>
public sealed class TileMap
{
    public static TileMap Empty { get; } = new(ImmutableDictionary<int, ImmutableDictionary<Point, TileCell>>.Empty);

    private readonly ImmutableDictionary<int, ImmutableDictionary<Point, TileCell>> _floors;

    private TileMap(ImmutableDictionary<int, ImmutableDictionary<Point, TileCell>> floors) => _floors = floors;

    /// <summary>칸 목록으로 한 번에 만든다(같은 층·칸이 겹치면 뒤의 것).</summary>
    public static TileMap FromCells(IEnumerable<PlacedTile> cells)
    {
        var floors = new Dictionary<int, ImmutableDictionary<Point, TileCell>.Builder>();
        foreach (var p in cells)
        {
            if (!floors.TryGetValue(p.Floor, out var b)) floors[p.Floor] = b = ImmutableDictionary.CreateBuilder<Point, TileCell>();
            b[p.At] = p.Cell;
        }
        return floors.Count == 0 ? Empty : new TileMap(floors.ToImmutableDictionary(f => f.Key, f => f.Value.ToImmutable()));
    }

    /// <summary>타일이 있는 층 Id들.</summary>
    public IEnumerable<int> FloorIds => _floors.Keys;

    /// <summary>층의 기준 칸 → 칸(없으면 빈 것).</summary>
    public IReadOnlyDictionary<Point, TileCell> Floor(int floor)
        => _floors.TryGetValue(floor, out var cells) ? cells : ImmutableDictionary<Point, TileCell>.Empty;

    public bool IsEmpty => _floors.IsEmpty;

    /// <summary>놓인 타일 수.</summary>
    public int Count => _floors.Values.Sum(f => f.Count);

    /// <summary>놓인 타일 모두(층 Id 차례가 아니라 아무 차례).</summary>
    public IEnumerable<PlacedTile> All => _floors.SelectMany(f => f.Value.Select(c => new PlacedTile(f.Key, c.Key, c.Value)));

    /// <summary>기준 칸 <paramref name="at"/>의 칸(기준 칸이 아니면 null — 덮은 타일은 <see cref="Covering"/>).</summary>
    public TileCell? Get(int floor, Point at) => _floors.TryGetValue(floor, out var cells) && cells.TryGetValue(at, out var c) ? c : null;

    /// <summary>기준 칸에 칸을 둔다(null이면 지움). 덮어쓰기 규칙 없이 그대로 — 보통은 <see cref="Place"/>.</summary>
    public TileMap With(int floor, Point at, TileCell? cell)
    {
        var cells = _floors.TryGetValue(floor, out var c) ? c : ImmutableDictionary<Point, TileCell>.Empty;
        if (cell is { } value)
        {
            if (cells.TryGetValue(at, out var old) && old == value) return this;
            cells = cells.SetItem(at, value);
        }
        else
        {
            if (!cells.ContainsKey(at)) return this;
            cells = cells.Remove(at);
        }
        return new TileMap(cells.IsEmpty ? _floors.Remove(floor) : _floors.SetItem(floor, cells));
    }

    /// <summary>층 <paramref name="floor"/>에서 칸 <paramref name="cell"/>을 덮은 타일(큰 타일 포함). 없으면 null.</summary>
    public PlacedTile? Covering(Tileset tileset, int floor, Point cell)
    {
        if (!_floors.TryGetValue(floor, out var cells)) return null;
        if (cells.TryGetValue(cell, out var here)) return new PlacedTile(floor, cell, here);
        int span = tileset.MaxSpan;
        for (int y = cell.Y; y > cell.Y - span; y--)
            for (int x = cell.X; x > cell.X - span; x--)
            {
                if (!cells.TryGetValue(new Point(x, y), out var c)) continue;
                var size = tileset.Footprint(c);
                if (cell.X < x + size.Width && cell.Y < y + size.Height) return new PlacedTile(floor, new Point(x, y), c);
            }
        return null;
    }

    /// <summary>층 <paramref name="floor"/>에서 칸 사각형 <paramref name="area"/>와 차지 칸이 겹치는 타일들.</summary>
    public IEnumerable<PlacedTile> Overlapping(Tileset tileset, int floor, Rectangle area)
    {
        if (!_floors.TryGetValue(floor, out var cells) || area.Width < 1 || area.Height < 1) yield break;
        int span = tileset.MaxSpan;
        long scan = (long)(area.Width + span) * (area.Height + span);
        if (scan > cells.Count)
        {
            foreach (var (at, c) in cells)
                if (new Rectangle(at, tileset.Footprint(c)).IntersectsWith(area)) yield return new PlacedTile(floor, at, c);
            yield break;
        }
        for (int y = area.Top - span + 1; y < area.Bottom; y++)
            for (int x = area.Left - span + 1; x < area.Right; x++)
                if (cells.TryGetValue(new Point(x, y), out var c) && new Rectangle(new Point(x, y), tileset.Footprint(c)).IntersectsWith(area))
                    yield return new PlacedTile(floor, new Point(x, y), c);
    }

    /// <summary>
    /// 층 <paramref name="floor"/>의 기준 칸 <paramref name="at"/>에 타일을 찍는다: 새 차지 칸과 겹치는 같은 층 타일은 통째로 지운다.
    /// </summary>
    public TileMap Place(Tileset tileset, int floor, Point at, TileCell cell)
    {
        cell = cell with { Transform = tileset.Allowed(cell.Transform) };
        var map = this;
        foreach (var old in Overlapping(tileset, floor, new Rectangle(at, tileset.Footprint(cell))).ToList())
            map = map.With(floor, old.At, null);
        return map.With(floor, at, cell);
    }

    /// <summary>
    /// 칸 <paramref name="cell"/>을 덮은 타일을 지운다(큰 타일이면 통째로). <paramref name="floor"/>가 있으면 그 층만, 없으면 위층부터 처음 만난 것 하나.
    /// 지운 타일(없으면 null)도 돌려준다.
    /// </summary>
    public (TileMap Map, PlacedTile? Removed) Erase(Tileset tileset, Point cell, int? floor = null)
    {
        foreach (int f in floor is { } only ? [only] : FloorsTopDown(tileset))
            if (Covering(tileset, f, cell) is { } hit)
                return (With(f, hit.At, null), hit);
        return (this, null);
    }

    /// <summary>타일이 있는 층들을 위층부터(타일셋에 없는 층은 맨 위로 침).</summary>
    public IEnumerable<int> FloorsTopDown(Tileset tileset)
        => _floors.Keys.OrderByDescending(f => tileset.FloorOrder(f) is var o && o < 0 ? int.MaxValue : o);

    /// <summary>타일 <paramref name="tile"/>을 쓴 칸을 모두 지운 맵.</summary>
    public TileMap WithoutTile(int tile)
    {
        var map = this;
        foreach (var p in All.Where(p => p.Cell.Tile == tile).ToList()) map = map.With(p.Floor, p.At, null);
        return map;
    }

    /// <summary>층 <paramref name="floor"/>을 통째로 지운 맵.</summary>
    public TileMap WithoutFloor(int floor) => _floors.ContainsKey(floor) ? new TileMap(_floors.Remove(floor)) : this;

    /// <summary>놓인 타일 차지 칸을 모두 감싸는 칸 사각형(없으면 빈 것).</summary>
    public Rectangle CellBounds(Tileset tileset)
    {
        var bounds = Rectangle.Empty;
        foreach (var p in All)
        {
            var r = new Rectangle(p.At, tileset.Footprint(p.Cell));
            bounds = bounds.IsEmpty ? r : Rectangle.Union(bounds, r);
        }
        return bounds;
    }

    /// <summary>모든 칸을 (<paramref name="dx"/>, <paramref name="dy"/>)칸 옮긴 맵.</summary>
    public TileMap Shifted(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return this;
        var floors = _floors.ToImmutableDictionary(f => f.Key, f => f.Value.ToImmutableDictionary(c => new Point(c.Key.X + dx, c.Key.Y + dy), c => c.Value));
        return new TileMap(floors);
    }

    /// <summary>
    /// 칸 <paramref name="start"/>에서 4방향으로 이어진, 층 <paramref name="floor"/>에서 같은 타일(같은 Id·뒤집기·회전, 또는 빈 칸)이 덮은 칸들.
    /// <paramref name="limit"/>(칸 사각형) 밖으로는 넓히지 않는다(빈 칸은 끝없이 이어지므로).
    /// </summary>
    public HashSet<Point> FillRegion(Tileset tileset, int floor, Point start, Rectangle limit)
    {
        var region = new HashSet<Point>();
        if (!limit.Contains(start)) return region;
        var key = KeyAt(tileset, floor, start);
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        region.Add(start);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var n in (ReadOnlySpan<Point>)[new(p.X + 1, p.Y), new(p.X - 1, p.Y), new(p.X, p.Y + 1), new(p.X, p.Y - 1)])
            {
                if (!limit.Contains(n) || region.Contains(n) || KeyAt(tileset, floor, n) != key) continue;
                region.Add(n);
                queue.Enqueue(n);
            }
        }
        return region;
    }

    private (int Tile, TileTransform Transform)? KeyAt(Tileset tileset, int floor, Point cell)
        => Covering(tileset, floor, cell) is { } p ? (p.Cell.Tile, p.Cell.Transform) : null;
}
