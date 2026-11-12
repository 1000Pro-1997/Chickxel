using System.Collections.Immutable;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 이웃 비트(위 1, 오른쪽 위 2, 오른쪽 4, 오른쪽 아래 8, 아래 16, 왼쪽 아래 32, 왼쪽 64, 왼쪽 위 128)와 규칙 틀(<see cref="AutoTilePreset"/>)의 모양.
/// </summary>
public static class AutoTileShapes
{
    public const byte N = 1, NE = 2, E = 4, SE = 8, S = 16, SW = 32, W = 64, NW = 128;

    /// <summary>비트 차례의 이웃 방향(칸).</summary>
    public static readonly (int X, int Y)[] Directions = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    private static readonly byte[] RowShapes = [0, E, E | W, W];
    private static readonly byte[] ColumnShapes = [0, S, N | S, N];
    private static readonly byte[] Edge4Shapes = Enumerable.Range(0, 16)
        .Select(i => (byte)(((i & 1) != 0 ? N : 0) | ((i & 2) != 0 ? E : 0) | ((i & 4) != 0 ? S : 0) | ((i & 8) != 0 ? W : 0))).ToArray();
    // 3×3: 왼쪽 위·위·오른쪽 위·왼쪽·가운데·오른쪽·왼쪽 아래·아래·오른쪽 아래, 안쪽 모서리(왼쪽 위·오른쪽 위·왼쪽 아래·오른쪽 아래가 빈 것)
    private static readonly byte[] Box13Shapes =
    [
        E | SE | S, E | SE | S | SW | W, S | SW | W,
        N | NE | E | SE | S, 255, N | S | SW | W | NW,
        N | NE | E, N | NE | E | W | NW, N | W | NW,
        255 & ~NW, 255 & ~NE, 255 & ~SW, 255 & ~SE,
    ];
    private static readonly byte[] Blob47Shapes = Enumerable.Range(0, 256).Select(m => Reduce((byte)m)).Distinct().Order().ToArray();

    /// <summary>틀의 모양 수.</summary>
    public static int Count(AutoTilePreset preset) => Shapes(preset).Length;

    /// <summary>틀의 모양 <paramref name="index"/>(같은 무리 이웃 비트).</summary>
    public static byte Shape(AutoTilePreset preset, int index) => Shapes(preset)[index];

    private static byte[] Shapes(AutoTilePreset preset) => preset switch
    {
        AutoTilePreset.Row => RowShapes,
        AutoTilePreset.Column => ColumnShapes,
        AutoTilePreset.Edge4 => Edge4Shapes,
        AutoTilePreset.Box13 => Box13Shapes,
        _ => Blob47Shapes,
    };

    /// <summary>틀이 보는 이웃 비트.</summary>
    public static byte Relevant(AutoTilePreset preset) => preset switch
    {
        AutoTilePreset.Row => E | W,
        AutoTilePreset.Column => N | S,
        AutoTilePreset.Edge4 => N | E | S | W,
        _ => 255,
    };

    /// <summary>
    /// 틀의 모양 하나를 규칙 조건으로: 보는 변은 있으면 "같음", 없으면 "다름". 8방향 틀의 모서리는 양옆 변이 다 이어질 때만 보고(아니면 상관없음), 그 밖의 틀은 보지 않는 비트를 상관없음으로.
    /// </summary>
    public static AutoTileRule Rule(AutoTilePreset preset, byte shape)
    {
        byte relevant = Relevant(preset);
        int same = 0, other = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            int b = 1 << bit;
            if ((relevant & b) == 0) continue;
            if (bit % 2 == 1 && preset is AutoTilePreset.Box13 or AutoTilePreset.Blob47)
            {
                int a = 1 << (bit - 1), c = 1 << ((bit + 1) % 8);
                if ((shape & a) == 0 || (shape & c) == 0) continue;
            }
            if ((shape & b) != 0) same |= b;
            else other |= b;
        }
        return new AutoTileRule { Same = (byte)same, Other = (byte)other };
    }

    /// <summary>틀의 규칙들(타일 없음, 모양 차례).</summary>
    public static IEnumerable<AutoTileRule> Rules(AutoTilePreset preset) => Shapes(preset).Select(s => Rule(preset, s));

    /// <summary>모서리 비트는 양옆 비트가 다 있을 때만 남긴다(8방향 모양).</summary>
    public static byte Reduce(byte mask)
    {
        int m = mask;
        if ((m & N) == 0 || (m & E) == 0) m &= ~NE;
        if ((m & S) == 0 || (m & E) == 0) m &= ~SE;
        if ((m & S) == 0 || (m & W) == 0) m &= ~SW;
        if ((m & N) == 0 || (m & W) == 0) m &= ~NW;
        return (byte)m;
    }

    /// <summary>모양을 돌리고 뒤집는다(그 타일 그림을 같은 만큼 돌리면 이 모양에 맞음).</summary>
    public static byte Transform(byte mask, TileTransform t)
    {
        if (t == TileTransform.None) return mask;
        int result = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;
            var (dx, dy) = Directions[bit];
            var (tx, ty) = t.ApplyToVector(dx, dy);
            result |= 1 << Array.IndexOf(Directions, (tx, ty));
        }
        return (byte)result;
    }
}

/// <summary>
/// 자동 타일 찍기·지우기: 칸에 세트 표시(<see cref="TileCell.AutoTile"/>)를 두고, 그 칸과 둘레 8칸의 타일을 규칙으로 다시 고른다.
/// 이웃 = 어느 층이든 그 칸에 이어지는 세트(<see cref="AutoTileSet.JoinsWith"/>)로 찍은 타일.
/// 규칙 고르기: 앞에서부터 그대로 맞는 규칙 → 그 규칙이 허용한 뒤집기·회전으로 맞는 것, 하나도 없으면 조건이 가장 덜 어긋난 규칙.
/// 한 규칙에 타일이 여럿이면 비중대로 고르되 칸 좌표로 정해진다(다시 골라도 같음).
/// </summary>
public static class AutoTiler
{
    private sealed class Table
    {
        // 이웃 모양 → (규칙 번호, 뒤집기·회전), 맞는 규칙이 있는지(가장 비슷한 것으로 대신하지 않음)
        public readonly (int Rule, TileTransform Transform)[] Best = new (int, TileTransform)[256];
        public readonly bool[] Exact = new bool[256];
        public bool Any;
    }

    private static readonly ConditionalWeakTable<AutoTileSet, Table> Tables = new();

    private static Table TableOf(AutoTileSet set) => Tables.GetValue(set, Build);

    private static Table Build(AutoTileSet set)
    {
        var table = new Table();
        var candidates = new List<(AutoTileRule Rule, int Index, TileTransform T)>();
        for (int i = 0; i < set.Rules.Length; i++)
        {
            var rule = set.Rules[i];
            if (!rule.HasTiles) continue;
            foreach (var t in TileTransforms.Reachable(rule.AllowFlipX, rule.AllowFlipY, rule.AllowRotate))
                candidates.Add((rule.Transformed(t), i, t));
        }
        if (candidates.Count == 0) return table;
        table.Any = true;
        for (int m = 0; m < 256; m++)
        {
            byte mask = (byte)m;
            int best = -1, bestMisses = int.MaxValue;
            for (int c = 0; c < candidates.Count; c++)
            {
                int misses = candidates[c].Rule.Misses(mask);
                if (misses < bestMisses)
                {
                    best = c;
                    bestMisses = misses;
                    if (misses == 0) break; // 앞의 규칙이 이김
                }
            }
            table.Best[m] = (candidates[best].Index, candidates[best].T);
            table.Exact[m] = bestMisses == 0;
        }
        return table;
    }

    /// <summary>이웃 모양 <paramref name="mask"/>에 놓을 타일과 뒤집기·회전. 세트에 타일이 하나도 없으면 null.</summary>
    public static (int Tile, TileTransform Transform)? Pick(AutoTileSet set, byte mask, Point at)
    {
        var table = TableOf(set);
        if (!table.Any) return null;
        var (rule, t) = table.Best[mask];
        return (Weighted(set.Rules[rule].Tiles, at, set.Id), t);
    }

    /// <summary>이웃 모양 <paramref name="mask"/>에 조건이 그대로 맞는 규칙이 있는지(없으면 가장 비슷한 규칙으로 대신함).</summary>
    public static bool HasExact(AutoTileSet set, byte mask) => TableOf(set).Exact[mask];

    /// <summary>이웃 모양 <paramref name="mask"/>에 쓰일 규칙 번호(타일 있는 규칙이 없으면 -1).</summary>
    public static int RuleFor(AutoTileSet set, byte mask) => TableOf(set) is { Any: true } t ? t.Best[mask].Rule : -1;

    /// <summary>비중대로 하나(칸 좌표로 정해짐).</summary>
    private static int Weighted(ImmutableArray<AutoTileChoice> choices, Point at, int salt)
    {
        if (choices.Length == 1) return choices[0].Tile;
        long total = 0;
        foreach (var c in choices) total += Math.Max(1, c.Weight);
        long roll = (long)(Hash(at.X, at.Y, salt) % (ulong)total);
        foreach (var c in choices)
        {
            roll -= Math.Max(1, c.Weight);
            if (roll < 0) return c.Tile;
        }
        return choices[^1].Tile;
    }

    /// <summary>칸 좌표의 고른 해시(무작위 찍기에도 씀).</summary>
    public static ulong Hash(int x, int y, int salt)
    {
        ulong h = (ulong)(uint)x * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(uint)salt * 0x165667B19E3779F9UL;
        h ^= h >> 33;
        h *= 0xFF51AFD7ED558CCDUL;
        h ^= h >> 33;
        h *= 0xC4CEB9FE1A85EC53UL;
        h ^= h >> 33;
        return h;
    }

    /// <summary>칸 <paramref name="at"/>에 <paramref name="set"/>과 이어지는 세트로 찍은 타일이 어느 층에든 있는지.</summary>
    public static bool Joins(Tileset tileset, TileMap map, AutoTileSet set, Point at)
    {
        foreach (int floor in map.FloorIds)
            if (map.Get(floor, at) is { AutoTile: not 0 } c && (c.AutoTile == set.Id || tileset.FindAutoTile(c.AutoTile) is { } other && set.JoinsWith(other)))
                return true;
        return false;
    }

    /// <summary>칸 <paramref name="at"/>의 이웃 모양(이어지는 세트로 찍은 이웃, 8방향 비트).</summary>
    public static byte NeighborMask(Tileset tileset, TileMap map, AutoTileSet set, Point at)
    {
        int mask = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            var (dx, dy) = AutoTileShapes.Directions[bit];
            if (Joins(tileset, map, set, new Point(at.X + dx, at.Y + dy))) mask |= 1 << bit;
        }
        return (byte)mask;
    }

    /// <summary>칸들에 세트로 찍는다(같은 층의 다른 타일은 덮어씀) — 찍은 칸과 둘레의 자동 타일을 다시 고른다.</summary>
    public static TileMap Paint(Tileset tileset, TileMap map, AutoTileSet set, IEnumerable<Point> cells)
    {
        var touched = new HashSet<Point>();
        foreach (var at in cells)
        {
            if (map.Get(set.Floor, at) is { } old && old.AutoTile == set.Id) continue;
            // 타일은 아래에서 고르므로 우선 세트 표시만(덮어쓰기는 1×1로)
            map = map.Place(tileset, set.Floor, at, new TileCell(NoTile, TileTransform.None, set.Id));
            touched.Add(at);
        }
        return Refresh(tileset, map, Around(touched));
    }

    /// <summary>칸들에서 세트로 찍은 타일을 지운다 — 둘레의 자동 타일을 다시 고른다.</summary>
    public static TileMap Erase(Tileset tileset, TileMap map, AutoTileSet set, IEnumerable<Point> cells)
    {
        var touched = new HashSet<Point>();
        foreach (var at in cells)
        {
            if (map.Get(set.Floor, at) is not { } old || old.AutoTile != set.Id) continue;
            map = map.With(set.Floor, at, null);
            touched.Add(at);
        }
        return Refresh(tileset, map, Around(touched));
    }

    /// <summary>칸들에 있는 자동 타일(모든 층)을 제 세트의 규칙으로 다시 고른다. 세트가 없어진 칸은 그대로.</summary>
    public static TileMap Refresh(Tileset tileset, TileMap map, IEnumerable<Point> cells)
    {
        var updates = new List<(int Floor, Point At, TileCell Cell)>();
        var floors = map.FloorIds.ToList();
        foreach (var at in cells)
            foreach (int floor in floors)
            {
                if (map.Get(floor, at) is not { AutoTile: not 0 } cell || tileset.FindAutoTile(cell.AutoTile) is not { } set) continue;
                var picked = Pick(set, NeighborMask(tileset, map, set, at), at);
                var next = picked is { } p ? new TileCell(p.Tile, tileset.Allowed(p.Transform), set.Id) : cell with { Tile = NoTile, Transform = TileTransform.None };
                if (next != cell) updates.Add((floor, at, next));
            }
        foreach (var (floor, at, c) in updates) map = map.With(floor, at, c);
        return map;
    }

    /// <summary>모든 자동 타일 칸을 다시 고른다(세트의 규칙·무리를 바꾼 뒤).</summary>
    public static TileMap RefreshAll(Tileset tileset, TileMap map)
        => Refresh(tileset, map, map.All.Where(p => p.Cell.AutoTile != 0).Select(p => p.At).Distinct().ToList());

    private static HashSet<Point> Around(HashSet<Point> cells)
    {
        var all = new HashSet<Point>();
        foreach (var at in cells)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    all.Add(new Point(at.X + dx, at.Y + dy));
        return all;
    }

    // 세트에 타일이 없을 때 칸에 남기는 타일(없는 Id라 그리지 않음 — 세트 표시만)
    private const int NoTile = 0;
}
