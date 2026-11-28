using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tiles;

/// <summary>그릴 타일 하나: 놓인 타일, 그림 자리(캔버스 좌표), 돌리고 뒤집은 그림.</summary>
public readonly record struct TileDraw(PlacedTile Tile, Rectangle Rect, PixelImage Image);

/// <summary>
/// 맵 그리기. 타일셋에 없는 층은 그리지 않는다. 그림은 알파로 겹친다. 그리는 순서(<see cref="Tileset.SortMode"/>):
/// <list type="bullet">
/// <item>층 차례: 층 차례(<see cref="Tileset.Floors"/>)대로, 층 안에서는 차지 칸 아래 줄 → 왼쪽부터(위로 솟은 그림이 앞 줄을 가리게).</item>
/// <item>위치: 바닥 묶음(층 차례 → 기준선) → 세운 것 묶음(기준선 → 층 차례 — 층과 상관없이 섞임) → 머리 위 묶음(층 차례 → 기준선). 같으면 왼쪽부터.</item>
/// </list>
/// </summary>
public static class TileMapRenderer
{
    /// <summary>
    /// <paramref name="target"/>에 그린다(지우지 않음). <paramref name="targetOrigin"/> = target 왼쪽 위의 캔버스 좌표, <paramref name="gridOrigin"/> = 기준 원점의 캔버스 좌표.
    /// <paramref name="freeFloor"/>가 true인 층의 타일 위치는 원점 기준 픽셀 좌표, 그 밖의 층은 칸 좌표다.
    /// <paramref name="floors"/>가 있으면 그 층만, <paramref name="classes"/>가 있으면 그 그리기 묶음만(위치로 그리는 타일셋). 움직이는 타일은 시각 <paramref name="timeMs"/>의 그림.
    /// <paramref name="floorOpacity"/>가 있으면 층마다 불투명도(0~255, 0 = 그리지 않음 — 화면의 층 보기에서 다른 층을 흐리게).
    /// </summary>
    public static void Render(Tileset tileset, TileMap map, Point gridOrigin, PixelImage target, Point targetOrigin, Func<int, bool>? floors = null, long timeMs = 0,
        Func<int, byte>? floorOpacity = null, Func<TileDrawClass, bool>? classes = null, Func<int, bool>? freeFloor = null)
    {
        var area = new Rectangle(targetOrigin, target.Size);
        if (tileset.SortMode == TileSortMode.ByPosition)
        {
            // 층이 섞여 그려지므로 흐린 층은 타일마다 알파를 줄여 겹친다.
            foreach (var d in DrawOrder(tileset, map, gridOrigin, area, floors, timeMs, classes, freeFloor))
            {
                byte opacity = floorOpacity?.Invoke(d.Tile.Floor) ?? 255;
                if (opacity == 0) continue;
                if (opacity == 255) target.Blit(d.Image, d.Rect.X - targetOrigin.X, d.Rect.Y - targetOrigin.Y, alphaBlend: true);
                else target.Composite(d.Image, d.Rect.X - targetOrigin.X, d.Rect.Y - targetOrigin.Y, opacity);
            }
            return;
        }
        PixelImage? layer = null;
        foreach (var floor in tileset.Floors)
        {
            if (floors is not null && !floors(floor.Id)) continue;
            byte opacity = floorOpacity?.Invoke(floor.Id) ?? 255;
            if (opacity == 0) continue;
            if (opacity == 255)
            {
                foreach (var d in FloorOrder(tileset, map, floor.Id, gridOrigin, area, timeMs, freeFloor?.Invoke(floor.Id) == true))
                    target.Blit(d.Image, d.Rect.X - targetOrigin.X, d.Rect.Y - targetOrigin.Y, alphaBlend: true);
                continue;
            }
            // 흐린 층: 층 하나를 따로 그린 뒤 알파를 줄여 겹친다(층 안의 겹침은 그대로).
            if (layer is null) layer = new PixelImage(target.Width, target.Height);
            else layer.FillRect(layer.Bounds, default);
            bool any = false;
            foreach (var d in FloorOrder(tileset, map, floor.Id, gridOrigin, area, timeMs, freeFloor?.Invoke(floor.Id) == true))
            {
                layer.Blit(d.Image, d.Rect.X - targetOrigin.X, d.Rect.Y - targetOrigin.Y, alphaBlend: true);
                any = true;
            }
            if (!any) continue;
            var px = layer.Pixels;
            for (int i = 0; i < px.Length; i++)
                if (px[i].A != 0) px[i] = px[i].WithAlpha((byte)(px[i].A * opacity / 255));
            target.Blit(layer, 0, 0, alphaBlend: true);
        }
    }

    /// <summary>
    /// <paramref name="area"/>(캔버스 좌표)와 겹치는 타일을 그리는 차례대로(타일셋의 그리는 순서, 타일셋에 없는 층은 뺌).
    /// <paramref name="floors"/>·<paramref name="classes"/>가 있으면 그 층·그리기 묶음만(묶음은 위치로 그리는 타일셋에서만 봄).
    /// </summary>
    public static List<TileDraw> DrawOrder(Tileset tileset, TileMap map, Point gridOrigin, Rectangle area, Func<int, bool>? floors = null, long timeMs = 0,
        Func<TileDrawClass, bool>? classes = null, Func<int, bool>? freeFloor = null)
    {
        if (tileset.SortMode != TileSortMode.ByPosition)
        {
            var all = new List<TileDraw>();
            foreach (var floor in tileset.Floors)
                if (floors is null || floors(floor.Id)) all.AddRange(FloorOrder(tileset, map, floor.Id, gridOrigin, area, timeMs, freeFloor?.Invoke(floor.Id) == true));
            return all;
        }
        // 같은 기준선이면 세운 것은 아래 칸에 놓인 것(앞에 선 것)을, 바닥·머리 위는 위 칸에 놓인 것(아래 칸으로 내려와 덮는 것)을 나중에 그린다.
        var list = new List<(int Group, int Major, int Rank, int Minor, int Row, int X, TileDraw Draw)>();
        foreach (var floor in map.FloorIds)
        {
            int order = tileset.FloorOrder(floor);
            if (order < 0 || floors is not null && !floors(floor)) continue;
            bool free = freeFloor?.Invoke(floor) == true;
            foreach (var (owner, at, cell) in Drawn(tileset, map, floor, free))
            {
                var kind = tileset.DrawClassOf(cell);
                if (classes is not null && !classes(kind) || tileset.PlacementAt(floor, at, cell, timeMs, free) is not { } p) continue;
                var rect = p.Rect;
                rect.Offset(gridOrigin);
                if (!rect.IntersectsWith(area)) continue;
                int line = tileset.SortLineAt(at, cell, free);
                var draw = new TileDraw(owner, rect, p.Image);
                int rank = tileset.FloorRankOf(cell);
                int row = free ? at.Y : at.Y * tileset.CellHeight, x = free ? at.X : at.X * tileset.CellWidth;
                list.Add(kind == TileDrawClass.Sorted ? ((int)kind, line, order, rank, row, x, draw) : ((int)kind, order, rank, line, -row, x, draw));
            }
        }
        list.Sort((a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group)
            : a.Major != b.Major ? a.Major.CompareTo(b.Major)
            : a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank)
            : a.Minor != b.Minor ? a.Minor.CompareTo(b.Minor)
            : a.Row != b.Row ? a.Row.CompareTo(b.Row)
            : a.X.CompareTo(b.X));
        return list.ConvertAll(e => e.Draw);
    }

    /// <summary>층 차례로 그리는 타일셋: 층 하나에서 <paramref name="area"/>와 겹치는 타일을 차지 칸 아래 줄 → 왼쪽 차례로.</summary>
    private static List<TileDraw> FloorOrder(Tileset tileset, TileMap map, int floor, Point gridOrigin, Rectangle area, long timeMs, bool free)
    {
        var list = new List<(int Rank, int Bottom, int X, TileDraw Draw)>();
        foreach (var (owner, at, cell) in Drawn(tileset, map, floor, free))
        {
            if (tileset.PlacementAt(floor, at, cell, timeMs, free) is not { } p) continue;
            var rect = p.Rect;
            rect.Offset(gridOrigin);
            if (!rect.IntersectsWith(area)) continue;
            var size = tileset.Footprint(cell);
            list.Add((tileset.FloorRankOf(cell), (free ? at.Y : at.Y * tileset.CellHeight) + size.Height * tileset.CellHeight, free ? at.X : at.X * tileset.CellWidth, new TileDraw(owner, rect, p.Image)));
        }
        list.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Bottom != b.Bottom ? a.Bottom.CompareTo(b.Bottom) : a.X.CompareTo(b.X));
        return list.ConvertAll(e => e.Draw);
    }

    /// <summary>
    /// 캔버스 점 <paramref name="point"/>에 보이는 맨 위 타일(그림이 투명하지 않은 곳, 그리는 차례의 반대로 찾음). <paramref name="floors"/>·<paramref name="classes"/>로 거른다.
    /// 없으면 null.
    /// </summary>
    public static PlacedTile? HitTest(Tileset tileset, TileMap map, Point gridOrigin, Point point, Func<int, bool>? floors = null, long timeMs = 0,
        Func<TileDrawClass, bool>? classes = null, Func<int, bool>? freeFloor = null)
    {
        var draws = DrawOrder(tileset, map, gridOrigin, new Rectangle(point, new Size(1, 1)), floors, timeMs, classes, freeFloor);
        for (int i = draws.Count - 1; i >= 0; i--)
        {
            var d = draws[i];
            if (d.Image[point.X - d.Rect.X, point.Y - d.Rect.Y].A != 0) return d.Tile;
        }
        return null;
    }

    /// <summary>그림이 닿는 캔버스 사각형(층·그리기 묶음 거르기 가능). 없으면 빈 것.</summary>
    public static Rectangle PixelBounds(Tileset tileset, TileMap map, Point gridOrigin, Func<int, bool>? floors = null, Func<TileDrawClass, bool>? classes = null, Func<int, bool>? freeFloor = null)
    {
        var bounds = Rectangle.Empty;
        bool byClass = classes is not null && tileset.SortMode == TileSortMode.ByPosition;
        foreach (var floor in map.FloorIds)
        {
            if (floors is not null && !floors(floor) || tileset.FloorOrder(floor) < 0) continue;
            bool free = freeFloor?.Invoke(floor) == true;
            foreach (var (_, at, cell) in Drawn(tileset, map, floor, free))
            {
                if (byClass && !classes!(tileset.DrawClassOf(cell)) || tileset.PlacementAt(floor, at, cell, free: free) is not { } placed) continue;
                var r = placed.Rect;
                r.Offset(gridOrigin);
                bounds = bounds.IsEmpty ? r : Rectangle.Union(bounds, r);
            }
        }
        return bounds;
    }

    /// <summary>캔버스 사각형 <paramref name="area"/>를 새 그림에 그린다(움직이는 타일은 시각 <paramref name="timeMs"/>의 그림, 층마다 불투명도는 <see cref="Render"/>).</summary>
    public static PixelImage RenderArea(Tileset tileset, TileMap map, Point gridOrigin, Rectangle area, Func<int, bool>? floors = null, long timeMs = 0,
        Func<int, byte>? floorOpacity = null, Func<TileDrawClass, bool>? classes = null, Func<int, bool>? freeFloor = null)
    {
        var image = new PixelImage(area.Width, area.Height);
        Render(tileset, map, gridOrigin, image, area.Location, floors, timeMs, floorOpacity, classes, freeFloor);
        return image;
    }

    /// <summary>
    /// 층 <paramref name="floor"/>에서 그릴 것: 맵의 칸(보통 그대로 — <c>Owner</c>는 그 칸), 모서리 격자 세트(<see cref="AutoTileSet.IsCorner"/>)로 찍은 칸은 빼고 대신
    /// 그 칸들이 닿는 모서리마다 세트별 모서리 그림(<c>At</c> = 모서리 남동쪽 칸, <c>Cell</c> = 모서리 그림 타일, <c>Owner</c> = 그 모서리를 만든 세트의 칸 — 고르기·지우기는 그 칸).
    /// </summary>
    private static IEnumerable<(PlacedTile Owner, Point At, TileCell Cell)> Drawn(Tileset tileset, TileMap map, int floor, bool free)
    {
        var cells = map.Floor(floor);
        if (free || !tileset.HasCornerSets)
        {
            foreach (var (at, cell) in cells) yield return (new PlacedTile(floor, at, cell), at, cell);
            yield break;
        }
        var corners = new HashSet<Point>();
        foreach (var (at, cell) in cells)
        {
            if (tileset.CornerSetOf(cell) is null)
            {
                yield return (new PlacedTile(floor, at, cell), at, cell);
                continue;
            }
            corners.Add(at);
            corners.Add(new Point(at.X + 1, at.Y));
            corners.Add(new Point(at.X, at.Y + 1));
            corners.Add(new Point(at.X + 1, at.Y + 1));
        }
        var around = new (Point At, TileCell Cell, AutoTileSet? Set)[4];
        var present = new List<AutoTileSet>(4);
        foreach (var c in corners)
        {
            present.Clear();
            for (int i = 0; i < 4; i++)
            {
                var at = new Point(c.X - 1 + (i & 1), c.Y - 1 + (i >> 1)); // 북서·북동·남서·남동
                bool has = cells.TryGetValue(at, out var cell);
                var set = has ? tileset.CornerSetOf(cell) : null;
                around[i] = (at, cell, set);
                if (set is not null && !present.Any(s => s.Id == set.Id)) present.Add(set);
            }
            foreach (var set in present.OrderBy(s => s.CornerRank))
            {
                int b = set.CornerBase, index = 0;
                for (int i = 0; i < 4; i++)
                {
                    var other = around[i].Set;
                    int state = other is not null && (other.Id == set.Id || other.CornerRank >= set.CornerRank) ? 1
                        : set.CornerTarget.Length > 0 && around[i].Cell.AutoTile != 0 && tileset.FindAutoTile(around[i].Cell.AutoTile) is { } t
                            && string.Equals(t.JoinName, set.CornerTarget, StringComparison.Ordinal) ? 2 : 0;
                    index = index * b + state;
                }
                if (index >= set.CornerTiles.Length || set.CornerTiles[index] is not (> 0 and var tile)) continue;
                // 고르기·지우기가 가리킬 칸: 이 세트로 찍은 칸 중 남동쪽부터
                int owner = 3;
                while (owner > 0 && around[owner].Set?.Id != set.Id) owner--;
                yield return (new PlacedTile(floor, around[owner].At, around[owner].Cell), c, new TileCell(tile));
            }
        }
    }
}
