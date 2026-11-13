using System.Collections.Immutable;
using System.Drawing;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using static Chickxel.Core.Tiles.AutoTileShapes;
using static Chickxel.Core.Tiles.TileArt;

namespace Chickxel.Core.Tiles;

public static partial class BuiltInTilesets
{
    // ───────────── 탑뷰 마을·물가 ─────────────
    // 색 배열은 다른 파일의 정적 필드라 초기화 차례가 정해지지 않으므로 이 파일에서는 색을 정적 필드로 담지 않고 쓸 때 꺼낸다.

    // 땅이 빈 이웃 칸으로 넘치는 최대 깊이(위·옆, 그림 여백), 남쪽 둑 깊이(칸 아래부터 — 모든 땅이 같아 물 거품이 둑 바로 아래에 맞음), 벽 윗면 띠 높이(px)
    private const int Lap = 4, BankDepth = 7, CapHeight = 5;

    // 가장자리 물결(칸 주기 — 칸 안 자리마다 빈 이웃으로 넘치는 깊이 px): 위 가장자리(x마다), 옆 가장자리(y마다), 물이 아닌 아래 가장자리(x마다).
    // 끝이 처음과 이어져 맞닿은 칸끼리 맞는다.
    private static readonly int[] EdgeTop = [3, 3, 4, 4, 3, 3, 2, 2, 3, 3, 3, 4, 4, 3, 2, 3];
    private static readonly int[] EdgeSide = [3, 2, 2, 3, 3, 4, 4, 3, 3, 2, 3, 3, 4, 3, 3, 3];
    private static readonly int[] EdgeBottom = [2, 3, 3, 2, 2, 3, 4, 3, 2, 2, 3, 3, 2, 3, 3, 2];

    // 풀 가장자리에 더하는 술(칸 주기): 위로 솟는 잎, 옆으로 삐친 잎, 아래로 늘어진 잎(물가면 둑 위로)
    private static readonly int[] GrassTuftTop = [0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0];
    private static readonly int[] GrassTuftSide = [0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0];
    private static readonly int[] GrassTuftBottom = [0, 1, 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 2, 1, 0];

    /// <summary>
    /// 탑뷰 마을·물가(16px, 위에서 비스듬히 내려다본 시점, 위치로 그림): 층은 물 → 모래 → 흙 → 풀 → 바닥재 → 장식 → 벽 → 물건 → 위.
    /// 땅(모래·흙·풀·돌길·나무 바닥 — 8방향 47규칙, 바닥 묶음)은 층 차례가 덮는 차례: 같은 땅이 아닌 이웃 칸으로 물결진 가장자리(외곽선·테, 풀은 술)가 넘쳐
    /// 아래 층 땅을 덮는다(땅끼리는 납작하게 섞임). 남쪽 이웃이 물이면("무리 있음" 조건) 남쪽 가장자리 대신 그 땅의 둑(흙·모래·돌·나무 기둥)이 물 칸 위쪽으로 내려와
    /// 물이 한 단 낮아 보인다. 물(8방향 47규칙, 움직임)은 둑 바로 아래와
    /// 가장자리 안쪽에 거품 줄이 일렁이고 물결 마루가 흐른다. 벽(나무·돌 — 무리 "벽", 세운 것)은 그린 칸 안에만: 남쪽이 비면 윗면 띠 아래 앞면,
    /// 남쪽에 벽이 이어지면 윗면만. 문(같은 무리, 4방향 16규칙)은 벽 줄 사이에서 윗면 띠가 이어진다. 물건(세운 것), 장식(바닥 묶음), 차양(머리 위).
    /// </summary>
    public static Tileset TopDownVillage()
    {
        var b = new Builder(DomainStrings.TileBuiltIn_Village) { SortMode = TileSortMode.ByPosition };
        int water = b.Floor(DomainStrings.TileBuiltIn_Water);
        int sand = b.Floor(DomainStrings.TileBuiltIn_Sand);
        int dirt = b.Floor(DomainStrings.TileBuiltIn_Dirt);
        int grass = b.Floor(DomainStrings.TileBuiltIn_Grass);
        int paved = b.Floor(DomainStrings.Tile_FloorPaved);
        int decor = b.Floor(DomainStrings.Tile_FloorDecor);
        int walls = b.Floor(DomainStrings.Tile_FloorWalls);
        int things = b.Floor(DomainStrings.Tile_FloorObjects);
        int top = b.Floor(DomainStrings.Tile_FloorTop);
        // 땅(물 포함)은 층이 달라도 한 칸에 하나: 찍으면 그 칸의 다른 땅을 지운다(풀 위에 물을 찍으면 풀이 빠짐)
        ImmutableArray<int> ground = [water, sand, dirt, grass, paved];
        const TileDrawClass sorted = TileDrawClass.Sorted;
        Point Wide(int w, int h) => new(-(w - Cell) / 2, Cell - h);

        // 줄 0: 물건(세운 것, 그림 아래 맞춤이라 위로 솟음)
        b.Tile(VillageChest(), things, 0, 0, drawClass: sorted);
        b.Tile(VillageBarrel(), things, 1, 0, drawClass: sorted);
        b.Tile(VillageCrate(), things, 2, 0, drawClass: sorted);
        b.Tile(VillagePot(), things, 3, 0, drawClass: sorted);
        b.Animated([.. Enumerable.Range(0, 4).Select(LampPost)], Tick, things, 4, 0, drawClass: sorted);
        b.Tile(Table(), things, 5, 0, drawClass: sorted);
        b.Tile(Bench(), things, 6, 0, drawClass: sorted);
        b.Tile(Sign(), things, 7, 0, drawClass: sorted);

        // 줄 1: 바위·덤불·나무(2칸 넘게 솟음, 칸보다 넓은 그림은 가운데 맞춤), 차양(머리 위 — 벽 앞면 위에 걸침)
        b.Tile(Rock(false), things, 0, 1, drawClass: sorted);
        b.Tile(Rock(true), things, 1, 1, drawClass: sorted);
        b.Tile(Bush(), things, 2, 1, drawClass: sorted);
        b.Tile(Palm(), things, 3, 1, offset: Wide(32, 40), drawClass: sorted);
        b.Tile(Tree(Green[1..], 870), things, 4, 1, offset: Wide(32, 44), drawClass: sorted);
        b.Tile(Awning(), top, 5, 1, offset: new Point(0, CapHeight - 2), drawClass: TileDrawClass.Overhead);

        // 줄 2: 바닥 장식(바닥 묶음) — 꽃·풀 포기·조약돌·조개(모래), 연잎(물 위)
        b.Tile(FlowerPatch(Red[2], Red[4], Fire[5]), decor, 0, 2);
        b.Tile(FlowerPatch(Blue[3], Blue[5], White), decor, 1, 2);
        b.Tile(FlowerPatch(Fire[4], Fire[6], Brown[2]), decor, 2, 2);
        b.Tile(GrassTuft(false), decor, 3, 2);
        b.Tile(Pebbles(), decor, 4, 2);
        b.Tile(Shells(), decor, 5, 2);
        b.Tile(LilyPad(false), decor, 6, 2);
        b.Tile(LilyPad(true), decor, 7, 2);

        // 줄 3~9: 물(움직임, 가득 찬 칸은 깊은 물 무늬 셋을 비중으로)
        int[] waterFills = [4, 3, 3];
        b.Set(DomainStrings.TileBuiltIn_Water, water, "", BlobRules(3, (shape, next) =>
            [.. Enumerable.Range(0, shape == 255 ? waterFills.Length : 1).Select(v =>
            {
                var at = next();
                int id = b.Animated([.. Enumerable.Range(0, 8).Select(f => WaterTile(shape, f, v))], Tick, water, at.X, at.Y);
                return new AutoTileChoice(id, shape == 255 ? waterFills[v] : 1);
            })]), clearFloors: ground);

        // 줄 10~: 땅(층마다 하나 — 위 층 땅의 가장자리가 아래 층 땅을 덮음). 세트마다 47모양 7줄, 이어서 물가 모양(남쪽 둘레가 물 — 둑) 줄들.
        // 물가 규칙이 앞에 있어 남쪽에 물이 있으면 먼저 맞고(물을 나중에 찍어도 둘레를 다시 고름), 같은 모양에서는 물이 많은 쪽이 먼저다.
        string waterGroup = DomainStrings.TileBuiltIn_Water;
        int Land(string name, int floor, int row, LandStyle st, int[] fills)
        {
            int Tile(byte shape, byte wet, int v, Point at)
                => b.Tile(LandTile(shape, wet, st, v), floor, at.X, at.Y, offset: new Point(-Lap, -Lap), sortY: LandSortY(shape, wet));
            var dry = BlobRules(row, (shape, next) =>
                [.. Enumerable.Range(0, shape == 255 ? fills.Length : 1).Select(v => new AutoTileChoice(Tile(shape, 0, v, next()), shape == 255 ? fills[v] : 1))]);
            var wetRules = new List<AutoTileRule>();
            foreach (var rule in dry)
                foreach (byte wet in WetCases(rule.Same))
                {
                    var at = new Point(wetRules.Count % 8, row + 7 + wetRules.Count / 8);
                    wetRules.Add(rule with { Other = (byte)(rule.Other & ~wet), Target = wet, TargetGroup = waterGroup, Tiles = [new(Tile(rule.Same, wet, 0, at))] });
                }
            b.Set(name, floor, "", [.. wetRules.OrderByDescending(r => System.Numerics.BitOperations.PopCount(r.Target)), .. dry], clearFloors: ground);
            return row + 7 + (wetRules.Count + 7) / 8;
        }
        int landRow = Land(DomainStrings.TileBuiltIn_Sand, sand, 10, SandLand(), [6, 2, 2]);
        landRow = Land(DomainStrings.TileBuiltIn_Dirt, dirt, landRow, DirtLand(), [6, 2, 2]);
        landRow = Land(DomainStrings.TileBuiltIn_Grass, grass, landRow, GrassLand(), [8, 2, 1]);
        landRow = Land(DomainStrings.TileBuiltIn_StonePath, paved, landRow, StoneLand(), [6, 2]);
        landRow = Land(DomainStrings.TileBuiltIn_WoodFloor, paved, landRow, WoodLand(), [6, 2]);

        // 벽(세운 것, 그린 칸 안에만) — 나무·돌 벽은 같은 무리라 서로 이어짐
        string wallGroup = DomainStrings.Tile_FloorWalls;
        void Wall(string name, int row, WallStyle st)
            => b.Set(name, walls, wallGroup, BlobRules(row, (shape, next) =>
            {
                var at = next();
                return [new(b.Tile(WallTile(shape, st), walls, at.X, at.Y, offset: Point.Empty, drawClass: sorted))];
            }));
        Wall(DomainStrings.TileBuiltIn_WoodWall, landRow, WoodWallStyle());
        Wall(DomainStrings.TileBuiltIn_StoneWall, landRow + 7, StoneWallStyle());
        int doorRow = landRow + 14;

        // 나무 문(4방향 16규칙, 벽과 같은 무리) — 문은 곧은 벽 줄에만 놓이므로 대각 이웃은 그림을 바꾸지 않는다
        int n4 = Count(AutoTilePreset.Edge4);
        b.Set(DomainStrings.TileBuiltIn_WoodDoor, walls, wallGroup,
        [
            .. Enumerable.Range(0, n4).Select(i =>
            {
                byte shape = Shape(AutoTilePreset.Edge4, i);
                int id = b.Tile(DoorTile(shape, WoodWallStyle()), walls, i % 8, doorRow + i / 8, offset: Point.Empty, drawClass: sorted);
                return Rule(AutoTilePreset.Edge4, shape) with { Tiles = [new(id)] };
            }),
        ]);
        return b.Build();
    }

    /// <summary>
    /// 47규칙 세트를 목록 <paramref name="row"/>줄부터 7줄에 놓는다: 모양마다 <paramref name="make"/>(모양, 다음 빈 자리)로 타일을 만든다 —
    /// 첫 자리는 <see cref="TerrainLayout"/>의 자리(있으면), 나머지(무늬 여러 장 포함)는 남은 자리를 차례로.
    /// </summary>
    private static ImmutableArray<AutoTileRule> BlobRules(int row, Func<byte, Func<Point>, ImmutableArray<AutoTileChoice>> make)
    {
        var slots = new Queue<(int X, int Y)>();
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 8; x++)
                if (!TerrainLayout.Any(l => l.X == x && l.Y == y)) slots.Enqueue((x, y));
        int n = Count(AutoTilePreset.Blob47);
        var shapes = Enumerable.Range(0, n).Select(i => Shape(AutoTilePreset.Blob47, i)).ToArray();
        var tileOf = new Dictionary<byte, ImmutableArray<AutoTileChoice>>();
        foreach (byte shape in TerrainLayout.Select(l => l.Shape).Concat(shapes.Where(s => !TerrainLayout.Any(l => l.Shape == s))))
        {
            int? fixedAt = Array.FindIndex(TerrainLayout, l => l.Shape == shape) is >= 0 and var i ? i : null;
            Point Next()
            {
                if (fixedAt is { } f)
                {
                    fixedAt = null;
                    return new Point(TerrainLayout[f].X, row + TerrainLayout[f].Y);
                }
                var (x, y) = slots.Dequeue();
                return new Point(x, row + y);
            }
            tileOf[shape] = make(shape, Next);
        }
        return [.. shapes.Select(s => Rule(AutoTilePreset.Blob47, s) with { Tiles = tileOf[s] })];
    }

    /// <summary>모양 <paramref name="mask"/>에 이웃 칸 (<paramref name="cx"/>, <paramref name="cy"/>)(−1~1, 가운데 아님)가 있는지.</summary>
    private static bool Has(byte mask, int cx, int cy) => (cx, cy) != (0, 0) && (mask & (1 << Array.IndexOf(Directions, (cx, cy)))) != 0;

    /// <summary>
    /// 땅 모양 <paramref name="shape"/>에서 물이 있는지가 그림을 바꾸는 남쪽 둘레(<see cref="LandTile"/>의 <c>wet</c>, 물이 하나라도 있는 경우만):
    /// 남쪽이 비면 남쪽(둑)과, 그때 같은 땅이 이어진 옆의 아래 대각(옆 칸 둑이 이어지는지 — 아니면 둑 끝을 닫음). 남쪽이 이어지면 남쪽 안쪽 모서리의 빈 대각(옆 칸 둑이 있는지).
    /// </summary>
    private static IEnumerable<byte> WetCases(byte shape)
    {
        bool e = (shape & E) != 0, w = (shape & W) != 0;
        if ((shape & S) == 0)
        {
            foreach (int se in e ? new[] { 0, SE } : [0])
                foreach (int sw in w ? new[] { 0, SW } : [0])
                    yield return (byte)(S | se | sw);
            yield break;
        }
        int corners = (e && (shape & SE) == 0 ? SE : 0) | (w && (shape & SW) == 0 ? SW : 0);
        for (int sub = corners; sub > 0; sub = (sub - 1) & corners) yield return (byte)sub;
    }

    /// <summary>
    /// 땅 타일의 기준선: 마른 남쪽 안쪽 모서리를 고쳐 그리는 타일은 아래 줄 다음(2칸 + 1), 북쪽 안쪽 모서리를 고쳐 그리는 타일은 같은 줄 다음(1칸 + 1) — 고칠 모서리에
    /// 가장자리를 그리는 두 이웃(서로를 몰라 곧은 끝을 그림)보다 나중에 그려지게. 그 밖은 차지 칸 아래.
    /// </summary>
    private static int? LandSortY(byte shape, byte wet)
    {
        bool s = (shape & S) != 0, n = (shape & N) != 0, e = (shape & E) != 0, w = (shape & W) != 0;
        if (s && (e && (shape & SE) == 0 && (wet & SE) == 0 || w && (shape & SW) == 0 && (wet & SW) == 0)) return 2 * Cell + 1;
        if (n && (e && (shape & NE) == 0 || w && (shape & NW) == 0)) return Cell + 1;
        return null;
    }

    // ───────────── 땅·물가 ─────────────

    /// <summary>
    /// 땅 한 가지의 그림: 속 무늬(칸 주기 x, y, 무늬 차례), 가장자리 외곽선, 안쪽 테, 둑 바로 위 턱, 둑 한 점(칸 주기 x, 칸 아래부터 줄 0~<see cref="BankDepth"/>−1),
    /// 가장자리에 더하는 술(위·옆·아래, 칸 주기, 없으면 0), 가장자리 들쭉날쭉(0~1, 바깥 한 점을 덧붙일 확률)과 그 씨앗.
    /// </summary>
    private sealed record LandStyle(Func<int, int, int, Color32> Body, Color32 Outline, Color32 Rim, Color32 Lip, Func<int, int, Color32> Bank,
        int[]? TuftTop = null, int[]? TuftSide = null, int[]? TuftBottom = null, double Ragged = 0, int Salt = 0);

    /// <summary>
    /// 땅 칸 둘레 3×3(가운데 = 그리는 칸, 칸 좌표 −1~1)에서 땅이 덮는 점과 남쪽 둑 점(좌표 = 가운데 칸 왼쪽 위 기준 px).
    /// 땅 칸은 같은 땅이 아닌 이웃 칸으로 물결진 가장자리만큼 넘치고(칸 주기 물결, 바깥 모서리는 둥글게), 남쪽이 물인 칸은 남쪽 가장자리 대신(술만) 아래 칸 위쪽으로
    /// <see cref="BankDepth"/>만큼 둑이 내려온다(그 칸 맨 아래 줄을 덮은 너비만큼 — 둥근 옆 가장자리 끝도 감쌈). 모르는 칸(3×3 밖, 줄인 모양에서 빠진 대각)은 땅도 물도 아닌 칸으로 친다:
    /// 곧은 가장자리로 그려지고, 안쪽 모서리는 그것을 아는 칸이 나중에 고쳐 그린다. 모든 무늬가 칸 주기라 맞닿은 칸이 같은 점을 얻는다.
    /// </summary>
    private sealed class Shore
    {
        private const int X0 = -Lap - 4, Y0 = -Lap - 4, W = Cell + 2 * Lap + 8, H = Lap + Cell + BankDepth + 8;
        private readonly bool[] _land = new bool[9], _wet = new bool[9];
        private readonly LandStyle? _style;
        private readonly bool[] _cover = new bool[W * H], _bank = new bool[W * H];

        /// <summary>
        /// <paramref name="land"/>(칸 좌표 −1~1) = 땅인 칸, <paramref name="wet"/> = 물인 칸(그 위 땅이 둑을 내림), <paramref name="style"/>이 없으면 술·들쭉날쭉 없는 바탕 물결(물이 거품 자리를 셈).
        /// </summary>
        public Shore(Func<int, int, bool> land, Func<int, int, bool> wet, LandStyle? style)
        {
            for (int cy = -1; cy <= 1; cy++)
                for (int cx = -1; cx <= 1; cx++)
                {
                    _land[(cy + 1) * 3 + cx + 1] = land(cx, cy);
                    _wet[(cy + 1) * 3 + cx + 1] = wet(cx, cy);
                }
            _style = style;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    _cover[y * W + x] = CoverAny(x + X0, y + Y0);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    _bank[y * W + x] = !_cover[y * W + x] && BankAny(x + X0, y + Y0);
        }

        public bool Land(int cx, int cy) => cx is >= -1 and <= 1 && cy is >= -1 and <= 1 && _land[(cy + 1) * 3 + cx + 1];

        private bool Wet(int cx, int cy) => cx is >= -1 and <= 1 && cy is >= -1 and <= 1 && _wet[(cy + 1) * 3 + cx + 1];

        private static bool Inside(int x, int y) => x >= X0 && x < X0 + W && y >= Y0 && y < Y0 + H;

        /// <summary>점을 땅이 덮는지(어느 땅 칸이든).</summary>
        public bool Cover(int x, int y) => Inside(x, y) ? _cover[(y - Y0) * W + x - X0] : CoverAny(x, y);

        /// <summary>점이 둑인지(덮이지 않고 어느 땅 칸의 둑 안).</summary>
        public bool Bank(int x, int y) => Inside(x, y) ? _bank[(y - Y0) * W + x - X0] : !CoverAny(x, y) && BankAny(x, y);

        /// <summary>땅도 둑도 아닌 점.</summary>
        public bool Open(int x, int y) => !Cover(x, y) && !Bank(x, y);

        private bool CoverAny(int x, int y)
        {
            for (int cy = -1; cy <= 1; cy++)
                for (int cx = -1; cx <= 1; cx++)
                    if (Land(cx, cy) && CoverBy(cx, cy, x, y)) return true;
            return false;
        }

        private bool BankAny(int x, int y)
        {
            for (int cy = -1; cy <= 1; cy++)
                for (int cx = -1; cx <= 1; cx++)
                    if (Land(cx, cy) && BankUnder(cx, cy, x, y)) return true;
            return false;
        }

        // 가장자리 깊이: 물결 + 술, 들쭉날쭉이면 점마다 한 점 더(칸 주기 난수), 최대 Lap
        private int Depth(int[] edge, int[]? tuft, int t, int x, int y)
        {
            int d = edge[t] + (tuft?[t] ?? 0);
            if (_style is { Ragged: > 0 } s && Rand(x & 15, y & 15, 900 + s.Salt) < s.Ragged) d++;
            return Math.Min(d, Lap);
        }

        /// <summary>땅 칸 (<paramref name="cx"/>, <paramref name="cy"/>)가 제 칸과 넘친 가장자리로 점을 덮는지(둑 제외).</summary>
        public bool CoverBy(int cx, int cy, int x, int y)
        {
            int lx = x - Cell * cx, ly = y - Cell * cy;
            if ((uint)lx < Cell && (uint)ly < Cell) return true;
            if (lx < -Cell || lx >= 2 * Cell || ly < -Cell || ly >= 2 * Cell) return false;
            int qx = lx < 0 ? -1 : lx >= Cell ? 1 : 0, qy = ly < 0 ? -1 : ly >= Cell ? 1 : 0;
            if (Land(cx + qx, cy + qy)) return false; // 이어진 땅 칸은 제 타일이 그림
            int[]? tuftTop = _style?.TuftTop, tuftSide = _style?.TuftSide, tuftBottom = _style?.TuftBottom;
            if (qx == 0)
                return qy < 0
                    ? -1 - ly < Depth(EdgeTop, tuftTop, lx, x, y)
                    : Wet(cx, cy + 1)
                        ? tuftBottom is not null && ly - Cell < tuftBottom[lx] // 물가: 둑 위로 늘어진 술만
                        : ly - Cell < Depth(EdgeBottom, tuftBottom, lx, x, y);
            bool bank = !Land(cx, cy + 1) && Wet(cx, cy + 1);
            if (qy == 0)
            {
                int k = qx > 0 ? lx - Cell : -1 - lx;
                int d = Depth(EdgeSide, tuftSide, ly, x, y);
                if (bank) d = Math.Min(d, Cell - ly); // 아래가 둑이면 아래 끝을 둥글게
                return k < d;
            }
            // 바깥 모서리: 양옆 변이 다 비면 둥글게(아래가 둑이면 옆 가장자리 끝이 둥글고 그 아래가 둑)
            if (Land(cx + qx, cy) || Land(cx, cy + qy) || qy > 0 && bank) return false;
            double i = (qx > 0 ? lx - Cell : -1 - lx) + 0.5, j = (qy > 0 ? ly - Cell : -1 - ly) + 0.5;
            double a = EdgeSide[qy > 0 ? Cell - 1 : 0], bb = (qy > 0 ? EdgeBottom : EdgeTop)[qx > 0 ? Cell - 1 : 0];
            return i * i / (a * a) + j * j / (bb * bb) < 1;
        }

        /// <summary>남쪽이 물인 땅 칸 (<paramref name="cx"/>, <paramref name="cy"/>) 아래의 둑: 그 칸이 맨 아래 줄에서 덮은 너비만큼, 칸 아래 <see cref="BankDepth"/>줄(덮인 점 제외).</summary>
        public bool BankUnder(int cx, int cy, int x, int y)
        {
            int ly = y - Cell * cy;
            if (Land(cx, cy + 1) || !Wet(cx, cy + 1) || ly < Cell || ly >= Cell + BankDepth) return false;
            return CoverBy(cx, cy, x, Cell * cy + Cell - 1) && !Cover(x, y);
        }

        /// <summary>덮이거나 둑인 점까지 맨해튼 거리(<paramref name="max"/>까지, 없으면 max + 1). 물의 거품 자리.</summary>
        public int DistanceToLand(int x, int y, int max)
        {
            int best = max + 1;
            for (int dy = -max; dy <= max; dy++)
                for (int dx = -max; dx <= max; dx++)
                {
                    int d = Math.Abs(dx) + Math.Abs(dy);
                    if (d < best && !Open(x + dx, y + dy)) best = d;
                }
            return best;
        }
    }

    /// <summary>
    /// 땅 한 칸(모양 <paramref name="mask"/>, 그림 (Cell + 2·<see cref="Lap"/>) × (<see cref="Lap"/> + Cell + <see cref="BankDepth"/>), 칸 왼쪽 위 기준 (−Lap, −Lap)에 놓임):
    /// 제 칸과 넘친 가장자리, 남쪽이 물이면(<paramref name="wet"/> = 물인 남쪽 둘레 S·SE·SW 비트) 아래 칸 위쪽의 둑. 안쪽 모서리가 있으면 그 대각 칸 모서리(두 이웃의 가장자리가
    /// 만나는 곳)를 합친 모양으로 다시 그린다 — 두 이웃은 서로를 몰라 곧은 끝을 그리므로(남쪽은 그 대각이 물이 아닐 때만 — 물이면 옆 칸 둑 위로 아래 칸 땅이 겹치는 것이 맞음).
    /// 가장자리 둘레는 외곽선(둑 바로 위는 턱), 그 안 한 줄은 테, 속은 칸 주기 무늬.
    /// </summary>
    private static PixelImage LandTile(byte mask, byte wet, LandStyle st, int variant)
    {
        var f = new Shore((cx, cy) => cx == 0 && cy == 0 || Has(mask, cx, cy), (cx, cy) => cy == 1 && Has(wet, cx, cy), st);
        bool n = (mask & N) != 0, s = (mask & S) != 0, e = (mask & E) != 0, w = (mask & W) != 0;
        bool fixNE = n && e && (mask & NE) == 0, fixNW = n && w && (mask & NW) == 0;
        bool fixSE = s && e && (mask & SE) == 0 && (wet & SE) == 0, fixSW = s && w && (mask & SW) == 0 && (wet & SW) == 0;
        return Paint(Cell + 2 * Lap, Lap + Cell + BankDepth, (ix, iy) =>
        {
            int x = ix - Lap, y = iy - Lap;
            bool fix = y < 0 && (fixNE && x >= Cell || fixNW && x < 0)
                || y is >= Cell and < Cell + Lap && (fixSE && x >= Cell || fixSW && x < 0);
            if (f.CoverBy(0, 0, x, y) || fix && f.Cover(x, y))
            {
                if (f.Open(x - 1, y) || f.Open(x + 1, y) || f.Open(x, y - 1) || f.Open(x, y + 1)) return st.Outline;
                if (f.Bank(x, y + 1)) return st.Lip;
                if (!f.Cover(x - 2, y) || !f.Cover(x + 2, y) || !f.Cover(x, y - 2) || !f.Cover(x, y + 2)
                    || !f.Cover(x - 1, y - 1) || !f.Cover(x + 1, y - 1) || !f.Cover(x - 1, y + 1) || !f.Cover(x + 1, y + 1)) return st.Rim;
                return st.Body(x & 15, y & 15, variant);
            }
            if (!f.BankUnder(0, 0, x, y)) return null;
            if (f.Open(x - 1, y) || f.Open(x + 1, y)) return st.Outline;
            return st.Bank(x & 15, y - Cell);
        });
    }

    /// <summary>모래: 밝은 모래에 바람 물결 줄과 알갱이, 주황 테, 모래 둑. 무늬 1 = 조개 조각, 2 = 짙은 모래 얼룩.</summary>
    private static LandStyle SandLand() => new(
        (x, y, v) =>
        {
            int ripple = (x + 2 * (y / 5) + (int)(2 * Math.Sin(x * Math.PI / 8))) % 8;
            if (y % 5 == 2 && ripple is 0 or 1 or 2) return Brown[7];
            if (Rand(x, y, 410) > 0.94) return Brown[5];
            if (v == 1 && (x, y) is (10, 11) or (11, 11) or (10, 12)) return (x, y) == (10, 11) ? Violet[5] : Red[5];
            if (v == 2 && (x - 5) * (x - 5) + (y - 6) * (y - 6) < 10 && Rand(x, y, 411) > 0.35) return Brown[5];
            return Brown[6];
        },
        Brown[4], Brown[5], Brown[4],
        (x, j) => j switch
        {
            0 => Brown[5],
            <= 3 => Rand(x, j, 412) > 0.8 ? Brown[5] : Brown[4],
            <= 5 => Rand(x, j, 413) > 0.85 ? Brown[4] : Brown[3],
            _ => Brown[2],
        },
        Ragged: 0.12, Salt: 1);

    /// <summary>흙: 붉은 흙에 어두운 점·작은 돌, 어두운 테, 흙 둑. 무늬 1 = 돌 박힘, 2 = 바퀴 자국.</summary>
    private static LandStyle DirtLand() => new(
        (x, y, v) =>
        {
            if (v == 1 && (x, y) is (4, 5) or (5, 5) or (11, 10) or (12, 10)) return x is 4 or 11 ? Cool[5] : Cool[4];
            if (v == 1 && (x, y) is (4, 6) or (5, 6) or (11, 11) or (12, 11)) return Cool[2];
            if (v == 2 && x is 4 or 11 && Rand(x, y, 421) > 0.25) return Brown[2];
            double r = Rand(x, y, 420);
            return r > 0.93 ? Brown[4] : r > 0.8 ? Brown[2] : Brown[3];
        },
        Brown[1], Brown[2], Brown[1],
        (x, j) => j switch
        {
            0 => Brown[3],
            <= 3 => Rand(x, j, 422) > 0.8 ? Brown[3] : Brown[2],
            <= 5 => Brown[1],
            _ => Brown[0],
        },
        Ragged: 0.15, Salt: 2);

    /// <summary>풀: 고른 풀빛에 칸마다 몇 자리의 풀 포기(밝은 잎끝 둘·어두운 밑동), 드문 밝고 어두운 점, 어두운 테, 흙 둑. 가장자리에 술이 솟고 둑 위로 늘어진다.
    /// 무늬 1 = 짙은 풀 무더기, 2 = 작은 들꽃.</summary>
    private static LandStyle GrassLand() => new(
        (x, y, v) =>
        {
            if (v == 2 && (x, y) is (4, 10) or (12, 5)) return White;
            if (v == 2 && (x, y) is (3, 10) or (5, 10) or (4, 9) or (4, 11) or (11, 5) or (13, 5) or (12, 4) or (12, 6)) return Fire[6];
            foreach (var (mx, my) in new[] { (3, 4), (11, 7), (7, 12), (14, 14) })
            {
                if ((x, y) == (mx, my)) return Green[2];
                if (y == my - 1 && Math.Abs(x - mx) == 1) return Green[4];
            }
            if (v == 1 && (x - 9) * (x - 9) + (y - 3) * (y - 3) < 7 && (x + y) % 2 == 0) return Green[2];
            double r = Rand(x, y, 430);
            return r > 0.97 ? Green[4] : r < 0.025 ? Green[2] : Green[3];
        },
        Green[1], Green[2], Green[1],
        (x, j) => j switch
        {
            0 => Brown[3],
            <= 3 => Rand(x, j, 433) > 0.78 ? Brown[3] : Brown[2],
            <= 5 => Rand(x, j, 434) > 0.85 ? Brown[2] : Brown[1],
            _ => Brown[0],
        },
        GrassTuftTop, GrassTuftSide, GrassTuftBottom, Ragged: 0.22, Salt: 3);

    /// <summary>돌길: 칸 주기로 이어지는 둥근 판석(보로노이 — 위 왼쪽 밝게, 아래 오른쪽 어둡게, 사이 줄눈), 밝은 돌 턱, 쌓은 돌 둑. 무늬 1 = 금 간 판석.</summary>
    private static LandStyle StoneLand()
    {
        var seeds = Seeds(2, 460, 0.55);
        return new(
            (x, y, v) =>
            {
                double px = x + 0.5, py = y + 0.5;
                var (index, sx, sy, dist, edge) = Cobble(seeds, px, py, 1.15);
                if (edge < 1.1) return Cool[3];
                if (v == 1 && index == 1 && Math.Abs(px - sx - (py - sy) * 0.8) < 0.6) return Cool[3];
                double lit = (px - sx) * 0.6 + (py - sy) * 0.8;
                if (lit < -1.8 && edge > 1.6) return Cool[6];
                if (lit > 1.8 || edge < 1.6) return Cool[4];
                return Rand(x, y, 462) > 0.9 ? Cool[4] : Cool[5];
            },
            Cool[2], Cool[4], Cool[2],
            (x, j) =>
            {
                if (j >= BankDepth - 1) return Cool[0];
                if (j >= BankDepth - 3) return Cool[1];
                int bx = (x + (j < 3 ? 0 : 5)) % 8;
                if (j == 2 || bx == 7) return Cool[1];
                return j is 0 or 3 ? Cool[4] : Cool[3];
            },
            Salt: 4);
    }

    /// <summary>나무 바닥: 세로 널(왼쪽 테 밝게, 이음매 엇갈림 — 가로 널인 벽 앞면과 구별), 어두운 테, 널 옆면과 기둥의 둑(물 위면 선창). 무늬 1 = 옹이.</summary>
    private static LandStyle WoodLand() => new(
        (x, y, v) =>
        {
            int plank = x / 4, lx = x % 4, joint = (plank * 5 + 3) % 16;
            if (lx == 3 || y == joint) return Brown[2];
            if (v == 1 && plank == 2 && (lx, y) is (1, 9) or (1, 10)) return Brown[2];
            if (lx == 0) return Brown[4];
            return Rand(x, y, 470) > 0.93 ? Brown[2] : Brown[3];
        },
        Brown[1], Brown[2], Brown[1],
        (x, j) =>
        {
            if (j >= BankDepth - 1) return Brown[0];
            bool post = x % 8 is 3 or 4;
            if (post) return x % 8 == 3 ? Brown[3] : Brown[2];
            if (j == 0) return Brown[4];
            if (j <= 2) return j == 2 ? Brown[2] : Brown[3];
            return Brown[1];
        },
        Salt: 5);

    /// <summary>
    /// 물 한 칸(모양 <paramref name="mask"/>)의 그림 <paramref name="frame"/>(8장): 이웃이 물이 아닌 쪽에 땅이 있다고 보고(바탕 물결·둑) 그 밑은 짙은 물,
    /// 맞닿은 줄은 흰 거품(땅 둑 바로 아래·옆 가장자리 바로 안쪽 — 장마다 1px 오르내림), 그 안 몇 px는 밝은 얕은 물, 더 안쪽은 깊은 물(<see cref="WaterBody"/>).
    /// 모든 무늬가 칸 주기이고 땅까지 거리는 둘레 칸만으로 정해져 어느 장에서도 이음매가 맞는다.
    /// </summary>
    private static PixelImage WaterTile(byte mask, int frame, int variant)
    {
        var f = new Shore((cx, cy) => (cx, cy) != (0, 0) && !Has(mask, cx, cy), (cx, cy) => (cx, cy) == (0, 0) || Has(mask, cx, cy), null);
        int bob = frame is >= 2 and <= 5 ? 1 : 0;
        return Paint(Cell, Cell, (x, y) =>
        {
            if (!f.Open(x, y)) return Blue[1];
            int d = f.DistanceToLand(x, y, ShallowWidth + 1);
            if (d <= bob) return Blue[4];
            if (d == 1 + bob) return (x * 3 + y + frame) % 9 == 0 ? Blue[5] : White;
            if (d == 2 + bob && (x + y * 3 + frame) % 5 == 0) return Blue[5];
            bool shallow = d <= ShallowWidth || d == ShallowWidth + 1 && (x + y) % 2 == 0;
            return WaterBody(x, y, frame, shallow, variant);
        });
    }

    // 물가에서 밝은 얕은 물 띠의 너비(px, 거품 포함)
    private const int ShallowWidth = 5;

    // 깊은 물 무늬(무늬 차례마다): 짙은 얼룩(가운데·반지름 — 칸 안에 들어 이웃 무늬와 이어짐이 끊기지 않음), 물결 마루(줄, 흐르는 쪽, 시작 x), 반짝임(x, y, 그림 차례)
    private static readonly (double X, double Y, double Rx, double Ry)[][] WaterBlobs =
        [[(5, 9, 4.5, 2.8), (12.5, 3, 2.5, 1.6)], [(10, 10, 4, 2.6)], [(4.5, 4, 3, 1.8)]];
    private static readonly (int Row, int Dir, int Start)[][] WaterCrests = [[(6, 1, 2), (13, -1, 10)], [(4, -1, 6)], [(11, 1, 0)]];
    private static readonly (int X, int Y, int Frame)[][] WaterSparkles = [[(11, 3, 1), (3, 12, 5)], [(6, 13, 3)], [(12, 7, 6)]];

    /// <summary>
    /// 물 한 점(칸 주기, 무늬 <paramref name="variant"/>): 깊은 물은 짙은 바탕에 체크 무늬로 흐린 더 짙은 얼룩, 얕은 물(<paramref name="shallow"/>)은 밝은 바탕.
    /// 한 장마다 2px씩 흐르는 물결 마루(반 칸씩만 보임), 장마다 다른 자리의 반짝임.
    /// </summary>
    private static Color32 WaterBody(int x, int y, int frame, bool shallow, int variant)
    {
        foreach (var (sx, sy, sf) in WaterSparkles[variant])
        {
            if (frame == sf && (x, y) == (sx, sy)) return White;
            if (frame == sf && Math.Abs(x - sx) + Math.Abs(y - sy) == 1) return Blue[5];
            if (frame == (sf + 1) % 8 && (x, y) == (sx, sy)) return Blue[4];
        }
        foreach (var (row, dir, start) in WaterCrests[variant])
        {
            int u = ((x - start - dir * frame * 2) % Cell + Cell) % Cell;
            if (u >= 8) continue;
            if (y == row && u is 0 or 4 || y == row + 1 && u is >= 1 and <= 3) return shallow ? Blue[4] : Blue[3];
        }
        if (shallow) return Blue[3];
        double blob = 0;
        foreach (var (bx, by, rx, ry) in WaterBlobs[variant])
        {
            double dx = x + 0.5 - bx, dy = y + 0.5 - by;
            blob = Math.Max(blob, 1 - (dx * dx / (rx * rx) + dy * dy / (ry * ry)));
        }
        if (blob > 0.45 || blob > 0 && (x + y) % 2 == 0) return Blue[1];
        return Blue[2];
    }

    // ───────────── 벽·문 ─────────────

    /// <summary>벽 한 가지의 그림: 윗면 테(밝음·중간), 윗면 속(어두움), 앞면 위 턱, 앞면 한 점(x, 띠 아래부터 줄 0~10).</summary>
    private sealed record WallStyle(Color32 Light, Color32 Mid, Color32 Core, Color32 Lip, Func<int, int, Color32> Face);

    /// <summary>나무 벽: 주황 테에 짙은 밤색 윗면, 앞면은 가로 널(위 테 밝게, 이음매 엇갈림)과 돌 밑단.</summary>
    private static WallStyle WoodWallStyle() => new(Brown[5], Brown[4], Brown[1], Brown[5], (x, fy) =>
    {
        if (fy == 0) return Brown[1];
        if (fy >= 10) return Ink;
        if (fy >= 8) return fy == 8 ? (x % 4 == 3 ? Cool[2] : Cool[4]) : (x % 4 == 1 ? Cool[2] : Cool[3]);
        int plank = (fy - 1) / 3, r = (fy - 1) % 3, joint = (plank * 6 + 3) % 8;
        if (r == 2 || x % 8 == joint) return Brown[2];
        return r == 0 ? Brown[4] : Brown[3];
    });

    /// <summary>돌 벽: 밝은 회색 테에 짙은 윗면, 앞면은 엇갈려 쌓은 회색 벽돌(위 테 밝게, 줄눈 어둡게).</summary>
    private static WallStyle StoneWallStyle() => new(Cool[6], Cool[5], Cool[1], Cool[5], (x, fy) =>
    {
        if (fy == 0) return Cool[1];
        if (fy >= 10) return Ink;
        int course = (fy - 1) / 3, r = (fy - 1) % 3, bx = (x + course % 2 * 4) % 8;
        if (r == 2 || bx == 7) return Cool[2];
        if (r == 0 || bx == 0) return Cool[5];
        return Rand((x + course % 2 * 4) / 8, course, 450) > 0.5 ? Cool[4] : Cool[3];
    });

    /// <summary>
    /// 벽 한 칸(모양 <paramref name="mask"/>, 그림 = 칸 하나): 빈 쪽을 따라 윗면 테(검은 외곽선 → 밝음 → 중간), 안쪽은 짙은 윗면.
    /// 남쪽이 비면 칸 위 <see cref="CapHeight"/>px가 윗면(아래 줄은 밝은 턱)이고 그 아래가 앞면(빈 옆쪽 끝은 검은 외곽선, 맨 아래는 바닥에 닿는 검은 줄),
    /// 남쪽에 벽이 이어지면 칸 전체가 윗면이다. 안쪽 모서리(양옆이 벽, 대각이 빔)는 그 이웃의 앞면이나 빈 대각에 맞닿은 테를 꺾어 이어 그린다.
    /// <paramref name="faceOverride"/>는 앞면 자리를 바꾼다(문).
    /// </summary>
    private static PixelImage WallTile(byte mask, WallStyle st, Func<int, int, Color32?>? faceOverride = null)
    {
        bool n = (mask & N) != 0, e = (mask & E) != 0, s = (mask & S) != 0, w = (mask & W) != 0;
        bool innerNE = n && e && (mask & NE) == 0, innerNW = n && w && (mask & NW) == 0;
        bool innerSE = s && e && (mask & SE) == 0, innerSW = s && w && (mask & SW) == 0;
        Color32[] ramp = [Ink, st.Light, st.Mid];
        const int far = 1000;
        // 빈 곳까지 거리(체비셰프, 0 = 맞닿음): 빈 이웃 칸, 안쪽 모서리 너머 이웃의 앞면(남쪽)·빈 대각(북쪽)
        int Gap(int x, int y)
        {
            int best = far;
            void Open(int x0, int x1, int y0, int y1)
            {
                int dx = x < x0 ? x0 - x : x > x1 ? x - x1 : 0;
                int dy = y < y0 ? y0 - y : y > y1 ? y - y1 : 0;
                best = Math.Min(best, Math.Max(dx, dy) - 1);
            }
            if (!w) Open(-far, -1, -far, far);
            if (!e) Open(Cell, far, -far, far);
            if (!n) Open(-far, far, -far, -1);
            if (innerSE) Open(Cell, far, CapHeight, far);
            if (innerSW) Open(-far, -1, CapHeight, far);
            if (innerNE) Open(Cell, far, -far, -1);
            if (innerNW) Open(-far, -1, -far, -1);
            return best;
        }
        return Paint(Cell, Cell, (x, y) =>
        {
            if (!s && y >= CapHeight)
            {
                int fy = y - CapHeight;
                if (faceOverride?.Invoke(x, fy) is { } door) return door;
                if (!w && x == 0 || !e && x == Cell - 1) return Ink;
                return st.Face(x, fy);
            }
            int d = Gap(x, y);
            if (!s && y == CapHeight - 1 && d > 0) return st.Lip;
            return d < ramp.Length ? ramp[d] : st.Core;
        });
    }

    /// <summary>
    /// 문 한 칸(4방향 모양 <paramref name="mask"/>, 벽 <paramref name="st"/>의 윗면이 이어짐): 남북으로 벽이 이어지고 동서가 비면 세로 벽 속의 옆에서 본 문
    /// (윗면 테 사이에 좁은 문짝 윗면, 경첩·손잡이), 그 밖에는 가로 문(윗면 띠가 문 위로 이어지고 앞면 자리에 문틀·세로 널 문짝·띠쇠·금빛 손잡이·문턱).
    /// </summary>
    private static PixelImage DoorTile(byte mask, WallStyle st)
    {
        bool vertical = (mask & (N | S)) != 0 && (mask & (E | W)) == 0;
        if (vertical)
        {
            var img = WallTile((byte)(mask | N | S), st);
            for (int y = 2; y <= 13; y++)
                for (int x = 4; x <= 11; x++)
                {
                    Color32 c = x is 4 or 11 ? Brown[0]
                        : y is 2 or 13 ? Brown[2]
                        : (x, y) is (5, 4) or (5, 11) ? Cool[5]
                        : (x, y) == (10, 8) ? Fire[5]
                        : x is 7 or 8 ? Brown[3] : Brown[4];
                    img.Set(x, y, c);
                }
            return img;
        }
        return WallTile((byte)(mask & ~S), st, (x, fy) =>
        {
            if (x is < 2 or > 13) return null;
            if (fy == 0 || x is 2 or 13) return Ink;
            if (fy >= 10) return x is 3 or 12 ? Ink : Cool[4];
            if (x is 3 or 12) return Brown[1];
            if (fy == 1 && x is 4 or 11) return Brown[1]; // 둥근 윗머리
            if ((x, fy) is (10, 5) or (10, 6)) return fy == 5 ? Fire[6] : Fire[4];
            if (fy is 3 or 7) return x is 4 or 6 or 9 ? Cool[5] : Cool[2];
            return (x - 4) % 2 == 0 ? Brown[4] : Brown[3];
        });
    }

    // ───────────── 물건(세운 것)·머리 위 ─────────────

    private static PixelImage VillageChest()
    {
        var p = new Dictionary<char, Color32>
        {
            ['o'] = Ink, ['1'] = Brown[2], ['2'] = Brown[3], ['3'] = Brown[4], ['4'] = Brown[5], ['5'] = Brown[6],
            ['m'] = Cool[3], ['M'] = Cool[5], ['y'] = Fire[5], ['Y'] = Fire[3],
        };
        // 3/4 시점: 뚜껑 윗면(밝음) → 뚜껑 앞 → 쇠 띠·자물쇠 → 몸통 앞
        return Art(p,
            "..oooooooooooo..",
            ".o555555555555o.",
            ".oM4444444444Mo.",
            ".oM4444444444Mo.",
            ".oM3333333333Mo.",
            ".oM3333333333Mo.",
            ".oommmmmmmmmmoo.",
            ".oM3333yy3333Mo.",
            ".oM2222yY2222Mo.",
            ".ommmmmoommmmmo.",
            ".oM2222oo2222Mo.",
            ".oM2222222222Mo.",
            ".oM1111111111Mo.",
            ".ommmmmmmmmmmmo.",
            ".oooooooooooooo.");
    }

    /// <summary>나무 상자(3/4 시점): 널빤지 윗면과 테·엇댄 대각 널의 앞면.</summary>
    private static PixelImage VillageCrate()
    {
        var img = Paint(Cell, 18, (x, y) =>
        {
            if (x < 2 || x > 13 || y < 1 || y > 16) return null;
            int lx = x - 2;
            if (y <= 4) return y == 4 ? Brown[3] : lx % 4 == 3 ? Brown[4] : Brown[5];
            int ly = y - 5;
            bool frame = lx < 2 || lx > 9 || ly < 2 || ly > 9;
            if (frame) return Brown[lx < 1 || ly < 1 ? 5 : lx > 10 || ly > 10 ? 2 : 4];
            int diag = lx - ly;
            if (diag is 0 or 1) return Brown[diag == 0 ? 5 : 4];
            if (diag is -1 or 2) return Brown[2];
            return ly % 3 == 2 ? Brown[2] : Brown[3];
        });
        Outline(img, Ink);
        return img;
    }

    /// <summary>통(3/4 시점): 둥근 뚜껑 윗면(테와 널), 불룩한 몸통, 쇠 테 둘.</summary>
    private static PixelImage VillageBarrel()
    {
        var img = Paint(Cell, 18, (x, y) =>
        {
            double dx = x + 0.5 - 8;
            double tx = dx / 5.5, ty = (y + 0.5 - 4) / 2.6, t2 = tx * tx + ty * ty;
            if (t2 <= 1) return t2 > 0.55 ? Brown[3] : x % 3 == 0 ? Brown[4] : Brown[5];
            double half = 5.5 + 0.7 * Math.Sin(Math.PI * Math.Clamp((y - 4) / 12.0, 0, 1));
            if (y < 4 || y > 16 || Math.Abs(dx) > half || y == 16 && Math.Abs(dx) > half - 1.5) return null;
            double t = (dx + half) / (2 * half);
            if (y is 8 or 13) return t < 0.3 ? Cool[5] : t < 0.7 ? Cool[4] : Cool[3];
            return t < 0.2 ? Brown[5] : t < 0.6 ? Brown[4] : t < 0.85 ? Brown[3] : Brown[2];
        });
        Outline(img, Ink);
        return img;
    }

    /// <summary>항아리(3/4 시점): 어두운 입구 타원, 좁은 목, 둥근 몸통(왼쪽 위 빛), 가운데 파란 띠.</summary>
    private static PixelImage VillagePot()
    {
        var img = Paint(Cell, Cell, (x, y) =>
        {
            double dx = x + 0.5 - 8;
            double mx = dx / 3.5, my = (y + 0.5 - 3.5) / 1.6, m2 = mx * mx + my * my;
            if (m2 <= 1) return m2 < 0.4 ? Brown[0] : Fire[2];
            if (y is 5 && Math.Abs(dx) <= 2.5) return Fire[1];
            double bx = dx / 5.8, by = (y + 0.5 - 10) / 5, r2 = bx * bx + by * by;
            if (r2 > 1 || y < 5) return null;
            double lit = bx * 0.6 + by * 0.8;
            if (y is 9 or 10) return lit < -0.3 ? Blue[4] : lit < 0.4 ? Blue[3] : Blue[2];
            return lit < -0.45 ? Fire[2] : lit < 0.35 ? Fire[1] : Fire[0];
        });
        Outline(img, Ink);
        return img;
    }

    /// <summary>탁자(3/4 시점): 널 세 장의 윗면, 앞 테, 네 다리.</summary>
    private static PixelImage Table()
    {
        var img = Paint(Cell, Cell, (x, y) =>
        {
            if (x is < 1 or > 14 || y < 3) return null;
            if (y <= 9) return y == 3 ? Brown[6] : y is 6 or 9 ? Brown[4] : Brown[5];
            if (y <= 11) return y == 10 ? Brown[3] : Brown[2];
            return x is 2 or 3 or 12 or 13 ? (x is 2 or 12 ? Brown[3] : Brown[2]) : null;
        });
        Outline(img, Ink);
        return img;
    }

    /// <summary>긴 의자(3/4 시점): 등받이 널, 앉는 널, 앞 테, 다리 둘.</summary>
    private static PixelImage Bench()
    {
        var img = Paint(Cell, 14, (x, y) =>
        {
            if (x is < 1 or > 14) return null;
            if (y is 1 or 2) return y == 1 ? Brown[5] : Brown[3];
            if (y is 3 or 4) return x is 2 or 13 ? Brown[2] : null;
            if (y is >= 5 and <= 7) return y == 5 ? Brown[6] : y == 6 ? Brown[5] : Brown[3];
            if (y is >= 8 and <= 12) return x is 2 or 3 or 12 or 13 ? (x is 2 or 12 ? Brown[3] : Brown[2]) : null;
            return null;
        });
        Outline(img, Ink);
        return img;
    }

    // 불꽃 그림(8×6, 장마다)
    private static readonly string[][] Flames =
    [
        ["....r...", "...rfr..", "..rfyfr.", "..fywyf.", "..fyyyf.", "...fff.."],
        ["...r....", "...rf...", "..rfyr..", "..fywyf.", "..fyyyf.", "...fff.."],
        [".....r..", "....rf..", "..rfyfr.", ".rfywyf.", "..fyyyf.", "...fff.."],
        ["...r.r..", "..rfrfr.", "..rfyfr.", "..fywyf.", "..fyyyf.", "...fff.."],
    ];

    private static PixelImage Flame(int frame)
        => Art(new Dictionary<char, Color32> { ['r'] = Fire[1], ['f'] = Fire[3], ['y'] = Fire[5], ['w'] = Fire[6] }, Flames[frame % 4]);

    /// <summary>둘레에 옅은 불빛(장마다 크기가 흔들림).</summary>
    private static void Glow(PixelImage img, double cx, double cy, int frame)
    {
        double rx = frame % 2 == 0 ? 6.8 : 7.6, ry = frame % 2 == 0 ? 7.5 : 8.5;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                double dx = (x + 0.5 - cx) / rx, dy = (y + 0.5 - cy) / ry;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1) Put(img, x, y, Fire[5].WithAlpha((byte)(d < 0.5 ? 54 : 28)));
            }
    }

    /// <summary>등불 기둥(16×32, 그림 4장): 돌 받침, 쇠 기둥, 지붕 얹은 등 안에 흔들리는 불꽃과 둘레 불빛.</summary>
    private static PixelImage LampPost(int frame)
    {
        var img = new PixelImage(Cell, 32);
        Glow(img, 8, 8, frame);
        var solid = Paint(Cell, 32, (x, y) =>
        {
            if (y is >= 1 and <= 3 && Math.Abs(x + 0.5 - 8) <= y + 1.5) return y == 1 ? Cool[4] : Cool[3];
            if (y is >= 4 and <= 12 && x is >= 4 and <= 11)
            {
                if (x is 4 or 11 || y is 4 or 12) return x == 4 ? Cool[4] : Cool[2];
                return y >= 10 ? Fire[3] : Fire[5];
            }
            if (y is >= 13 and <= 27 && x is 7 or 8) return x == 7 ? Cool[4] : Cool[2];
            if (y == 18 && x is >= 6 and <= 9) return Cool[3];
            if (y is >= 28 and <= 31 && x is >= 5 and <= 10) return y == 28 ? Cool[5] : x == 5 ? Cool[4] : Cool[3];
            return null;
        });
        Outline(solid, Ink);
        Stamp(img, solid, 0, 0);
        Stamp(img, Flame(frame), 4, 5);
        return img;
    }

    /// <summary>야자나무(32×40, 차지 칸 1칸): 마디진 휜 줄기, 꼭대기에서 늘어지는 잎 여섯(위는 밝게, 아래는 어둡게), 열매 둘.</summary>
    private static PixelImage Palm()
    {
        var img = new PixelImage(32, 40);
        // 줄기: 아래(16, 39)에서 위(18, 12)로 살짝 휨
        for (int y = 12; y < 40; y++)
        {
            double t = (y - 12) / 27.0;
            double cx = 18 - 3 * t + 1.5 * Math.Sin(t * Math.PI);
            double half = 1.6 + 0.8 * t;
            for (int x = 0; x < 32; x++)
            {
                double dx = x + 0.5 - cx;
                if (Math.Abs(dx) > half) continue;
                bool ring = (y - 12) % 4 == 3;
                img.Set(x, y, ring ? Brown[2] : dx < -half / 3 ? Brown[5] : dx < half / 3 ? Brown[4] : Brown[3]);
            }
        }
        Outline(img, Ink);
        var crown = new PixelImage(32, 40);
        (double Angle, double Len, double Droop)[] fronds = [(-170, 13, 0.06), (-140, 12, 0.05), (-100, 9, 0.02), (-75, 9, 0.02), (-35, 12, 0.05), (-5, 13, 0.06), (150, 9, 0.03), (35, 9, 0.03)];
        const double ox = 18, oy = 11;
        foreach (var (angle, len, droop) in fronds)
        {
            double ca = Math.Cos(angle * Math.PI / 180), sa = Math.Sin(angle * Math.PI / 180);
            for (double s = 0; s <= len; s += 0.25)
            {
                double px = ox + ca * s, py = oy + sa * s + droop * s * s;
                double width = 2.2 * (1 - s / len) + 0.7;
                for (int y = (int)(py - width - 1); y <= (int)(py + width + 1); y++)
                    for (int x = (int)(px - width - 1); x <= (int)(px + width + 1); x++)
                    {
                        if ((uint)x >= 32 || (uint)y >= 40) continue;
                        double dx = x + 0.5 - px, dy = y + 0.5 - py;
                        if (dx * dx + dy * dy > width * width) continue;
                        var c = dy < -0.3 ? Green[5] : dy < 0.6 ? Green[4] : Green[3];
                        if (s > len * 0.5 && (int)(s * 2) % 3 == 0 && dy > 0) c = Green[2];
                        crown.Set(x, y, c);
                    }
            }
        }
        Outline(crown, Ink);
        Stamp(img, crown, 0, 0);
        foreach (var (x, y) in new[] { (16, 13), (19, 13) })
        {
            img.Set(x, y, Brown[2]); img.Set(x + 1, y, Brown[1]); img.Set(x, y + 1, Brown[1]); img.Set(x + 1, y + 1, Brown[0]);
        }
        return img;
    }

    /// <summary>차양(16×10, 머리 위): 붉은·흰 세로 줄 천, 위 쇠 막대, 아래 둥근 물결 끝(아래로 갈수록 그늘).</summary>
    private static PixelImage Awning()
    {
        var img = Paint(Cell, 10, (x, y) =>
        {
            if (y == 0) return Cool[3];
            int stripe = x / 4;
            double scallop = 7.5 + Math.Sqrt(Math.Max(0, 4 - Math.Pow(x % 4 + 0.5 - 2, 2))) * 0.9;
            if (y > scallop) return null;
            bool red = stripe % 2 == 0;
            if (y >= 7) return red ? Red[1] : Brown[6];
            return red ? (y == 1 ? Red[3] : Red[2]) : (y == 1 ? White : Brown[7]);
        });
        Outline(img, Ink);
        return img;
    }

    // ───────────── 장식(바닥 묶음) ─────────────

    /// <summary>꽃 무더기: 작은 다섯 꽃잎 꽃 셋(색 <paramref name="petal"/>·밝은 <paramref name="light"/>, 가운데 <paramref name="center"/>)과 잎.</summary>
    private static PixelImage FlowerPatch(Color32 petal, Color32 light, Color32 center)
    {
        var img = new PixelImage(Cell, Cell);
        foreach (var (x, y) in new[] { (3, 9), (12, 11), (9, 5), (6, 13), (13, 6) })
        {
            img.Set(x, y, Green[3]); img.Set(x + 1, y, Green[4]); img.Set(x, y + 1, Green[2]);
        }
        foreach (var (cx, cy) in new[] { (4, 6), (11, 9), (7, 11) })
        {
            foreach (var (dx, dy) in new[] { (0, -1), (-1, 0), (1, 0), (0, 1) }) img.Set(cx + dx, cy + dy, dy < 0 || dx < 0 ? light : petal);
            img.Set(cx, cy, center);
        }
        Outline(img, Green[1]);
        return img;
    }

    /// <summary>조약돌 셋(빛 받는 위 왼쪽 밝게).</summary>
    private static PixelImage Pebbles()
    {
        var img = new PixelImage(Cell, Cell);
        foreach (var (x, y, w) in new[] { (3, 10, 3), (9, 6, 2), (10, 12, 2) })
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < w; dx++)
                    img.Set(x + dx, y + dy, dy == 0 && dx == 0 ? Cool[6] : dy == 0 ? Cool[5] : Cool[4]);
        Outline(img, Cool[1]);
        return img;
    }

    /// <summary>조개와 불가사리(모래 위).</summary>
    private static PixelImage Shells()
    {
        var p = new Dictionary<char, Color32> { ['o'] = Brown[3], ['s'] = Red[5], ['S'] = Brown[7], ['f'] = Fire[4], ['F'] = Fire[5] };
        return Art(p,
            "................",
            "................",
            "................",
            "..ooo...........",
            ".oSsSo..........",
            ".osSsSo.........",
            ".oSsSso.........",
            "..oooo..........",
            "..........o.....",
            ".........oFo....",
            "......oooofoooo.",
            "......oFffffFo..",
            ".......ooffoo...",
            "........ofoFo...",
            "........oo.oo...",
            "................");
    }

    /// <summary>연잎(물 위): 갈라진 둥근 잎(잎맥), 무늬 있으면 분홍 꽃.</summary>
    private static PixelImage LilyPad(bool flower)
    {
        var img = Paint(Cell, Cell, (x, y) =>
        {
            double dx = x + 0.5 - 8, dy = (y + 0.5 - 9) * 1.35;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (r > 6.2) return null;
            if (dx > 0 && Math.Abs(dy) < dx * 0.45) return null; // 갈라진 틈
            if ((x + y) % 5 == 0 && r > 1.5 && r < 5) return Green[3];
            return dx + dy < -2 ? Green[5] : Green[4];
        });
        Outline(img, Green[1]);
        if (flower)
        {
            foreach (var (x, y, c) in new[] { (6, 6, Violet[5]), (5, 7, Violet[4]), (7, 7, Violet[4]), (6, 8, Violet[3]), (6, 7, Fire[5]), (6, 5, Violet[5]) })
                img.Set(x, y, c);
        }
        return img;
    }
}
