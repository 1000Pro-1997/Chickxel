using System.Collections.Immutable;
using System.Drawing;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using static Chickxel.Core.Tiles.AutoTileShapes;
using static Chickxel.Core.Tiles.TileArt;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 앱에 들어 있는 기본 타일셋(코드로 그림 — 같은 코드면 늘 같은 그림). 고를 때마다 새 이름표의 사본을 만든다.
/// </summary>
public static class BuiltInTilesets
{
    /// <summary>기본 타일셋 하나: 고정 Id, 화면 이름, 만들기.</summary>
    public sealed record Entry(string Id, string Name, Func<Tileset> Create);

    public static IReadOnlyList<Entry> All { get; } =
    [
        new("sandbox", DomainStrings.TileBuiltIn_Sandbox, Sandbox),
    ];

    // ───────────── 색(Endesga 64, 어두움 → 밝음) ─────────────

    private static readonly Color32 Ink = C("#000000");
    private static readonly Color32 White = C("#FFFFFF");
    private static readonly Color32[] Brown = [C("#1C121C"), C("#391F21"), C("#5D2C28"), C("#8A4836"), C("#BF6F4A"), C("#E69C69"), C("#F6CA9F"), C("#F9E6CF")];
    private static readonly Color32[] Cool = [C("#0E071B"), C("#1A1932"), C("#2A2F4E"), C("#424C6E"), C("#657392"), C("#92A1B9"), C("#C7CFDD")];
    private static readonly Color32[] Green = [C("#0C2E44"), C("#134C4C"), C("#1E6F50"), C("#33984B"), C("#5AC54F"), C("#99E65F"), C("#D3FC7E")];
    private static readonly Color32[] Blue = [C("#03193F"), C("#00396D"), C("#0069AA"), C("#0098DC"), C("#00CDF9"), C("#0CF1FF"), C("#94FDFF")];
    private static readonly Color32[] Fire = [C("#8E251D"), C("#C64524"), C("#E07438"), C("#ED7614"), C("#FFA214"), C("#FFC825"), C("#FFEB57")];
    private static readonly Color32[] Red = [C("#571C27"), C("#891E2B"), C("#C42430"), C("#EA323C"), C("#F5555D"), C("#F68187")];
    private static readonly Color32[] Violet = [C("#3B1443"), C("#622461"), C("#93388F"), C("#CA52C9"), C("#F389F5"), C("#FDD2ED")];

    // 움직이는 타일 한 장 시간(모두 이 값의 배수라 화면이 같은 간격으로 바뀜)
    private const int Tick = 150;

    // ───────────── 샌드박스 지상·동굴 ─────────────

    /// <summary>
    /// 샌드박스 지상·동굴(16px, 옆에서 본 시점, 검은 외곽선의 깔끔한 그림): 층은 배경 벽 → 바닥 → 덮개 → 장식 → 앞.
    /// 바닥(흙·진흙 = 무리 "흙", 돌·광석 섞인 돌 = 무리 "돌", 모래·돌 벽돌·나무 판자 — 8방향 47규칙), 배경 벽(흙·돌·벽돌 — 무리 "배경 벽"),
    /// 덮개(풀·눈·낙엽 — 바닥 윗면 칸에 덧칠하면 아래로 흘러내림), 장식(나무·풀·꽃·버섯·바위·종유석·수정·횃불·보석·동전·상자·통·문 등, 발판·사다리·덩굴·울타리 줄 자동 타일),
    /// 앞(물·용암 — 위가 비면 물결 수면, 옆이 비면 옆 가장자리, 더 높은 물 옆은 비스듬히 이어짐, 흐르는 움직임).
    /// </summary>
    public static Tileset Sandbox()
    {
        var b = new Builder(DomainStrings.TileBuiltIn_Sandbox);
        int wall = b.Floor(DomainStrings.Tile_FloorWall);
        int ground = b.Floor(DomainStrings.Tile_FloorGround);
        int cover = b.Floor(DomainStrings.Tile_FloorCover);
        int decor = b.Floor(DomainStrings.Tile_FloorDecor);
        int front = b.Floor(DomainStrings.Tile_FloorFront);
        string dirtGroup = DomainStrings.TileBuiltIn_Dirt, stoneGroup = DomainStrings.TileBuiltIn_Stone, wallGroup = DomainStrings.Tile_FloorWall;

        // 줄 0~3: 장식 낱개
        b.Tile(Tree(Green, 900), decor, 0, 0, offset: new Point(-8, Cell - 44));
        b.Tile(Tree([Fire[0], Fire[1], Fire[3], Fire[4], Fire[5]], 910), decor, 1, 0, offset: new Point(-8, Cell - 44));
        b.Tile(Pine(), decor, 2, 0, offset: new Point(-4, Cell - 40));
        b.Tile(BigBush(), decor, 3, 0, w: 2);
        b.Tile(Bush(), decor, 4, 0);
        b.Tile(Rock(small: false), decor, 5, 0);
        b.Tile(Rock(small: true), decor, 6, 0);
        b.Tile(Cactus(), decor, 7, 0, offset: new Point(0, Cell - 24));
        b.Tile(GrassTuft(false), decor, 0, 1);
        b.Tile(GrassTuft(true), decor, 1, 1);
        b.Tile(Flower([Red[1], Red[3], Red[5]], Fire[5]), decor, 2, 1);
        b.Tile(Flower([Blue[2], Blue[4], Blue[6]], Fire[6]), decor, 3, 1);
        b.Tile(Flower([Fire[3], Fire[5], Fire[6]], Brown[3]), decor, 4, 1);
        b.Tile(Mushroom(), decor, 5, 1);
        b.Tile(GlowMushroom(), decor, 6, 1);
        b.Tile(Crystal(), decor, 7, 1);
        b.Tile(Spike(down: true), decor, 0, 2);
        b.Tile(Spike(down: false), decor, 1, 2);
        b.Animated([.. Enumerable.Range(0, 4).Select(Torch)], Tick, decor, 2, 2);
        b.Animated([.. Enumerable.Range(0, 4).Select(Coin)], Tick, decor, 3, 2);
        b.Tile(Gem(Red), decor, 4, 2);
        b.Tile(Gem([Blue[1], Blue[2], Blue[3], Blue[4], Blue[5], Blue[6]]), decor, 5, 2);
        b.Tile(Key(), decor, 6, 2);
        b.Tile(Pot(), decor, 7, 2);
        b.Tile(Chest(), decor, 0, 3);
        b.Tile(Crate(), decor, 1, 3);
        b.Tile(Barrel(), decor, 2, 3);
        b.Tile(Sign(), decor, 3, 3);
        b.Tile(Door(), decor, 4, 3, h: 2);
        b.Tile(Workbench(), decor, 5, 3, w: 2);
        b.Tile(WoodWall(0), wall, 6, 3);
        b.Tile(WoodWall(1), wall, 7, 3);

        // 줄 4~5: 줄로 잇는 자동 타일(가로 줄 = 외딴·오른쪽에 이웃·양쪽·왼쪽에 이웃, 세로 줄 = 외딴·아래·위아래·위)
        b.Set(DomainStrings.TileBuiltIn_Platform, decor, "", Line(b, AutoTilePreset.Row, decor, 0, 4, Platform));
        b.Set(DomainStrings.TileBuiltIn_Ladder, decor, "", Line(b, AutoTilePreset.Column, decor, 4, 4, Ladder));
        b.Set(DomainStrings.TileBuiltIn_Vine, decor, "", Line(b, AutoTilePreset.Column, decor, 0, 5, Vine));
        b.Set(DomainStrings.TileBuiltIn_Fence, decor, "", Line(b, AutoTilePreset.Row, decor, 4, 5, Fence));

        // 줄 6~8: 덮개(바닥 윗면 칸에 덧칠)
        b.Set(DomainStrings.TileBuiltIn_Grass, cover, "", CoverRules(b, cover, 6, GrassCover));
        b.Set(DomainStrings.TileBuiltIn_Snow, cover, "", CoverRules(b, cover, 7, SnowCover));
        b.Set(DomainStrings.TileBuiltIn_Autumn, cover, "", CoverRules(b, cover, 8, AutumnCover));

        // 줄 9~12: 물·용암(움직이는 타일) — "뭐라도 있음"은 바닥·앞 층만 본다(배경 벽·장식 앞에서는 수면이 남게)
        b.Set(DomainStrings.TileBuiltIn_Water, front, "", LiquidRules(b, front, 9, Water), [ground, front]);
        b.Set(DomainStrings.TileBuiltIn_Lava, front, "", LiquidRules(b, front, 11, Lava), [ground, front]);

        // 줄 13~: 바닥·배경 벽(8방향 47규칙, 가득 찬 칸은 무늬 여러 장을 비중으로 무작위)
        int row = 13;
        b.Set(DomainStrings.TileBuiltIn_Dirt, ground, dirtGroup, Terrain(b, ground, row, DirtGround, [6, 3, 3]));
        b.Set(DomainStrings.TileBuiltIn_Mud, ground, dirtGroup, Terrain(b, ground, row += 7, MudGround, [6, 3, 3]));
        var stone = Terrain(b, ground, row += 7, StoneGround, [6, 3, 3]);
        b.Set(DomainStrings.TileBuiltIn_Stone, ground, stoneGroup, stone);
        row += 7;
        Color32[][] ores =
        [
            [Fire[0], Fire[1], Fire[2], Brown[5]], // 구리
            [Fire[1], Fire[4], Fire[5], Fire[6]],  // 금
            [Blue[2], Blue[3], Blue[5], Blue[6]],  // 보석
            [Violet[1], Violet[2], Violet[3], Violet[4]], // 자수정
        ];
        var oreTiles = ores.Select((o, i) => b.Tile(OreStone(o, i), ground, i, row)).ToArray();
        b.Set(DomainStrings.TileBuiltIn_OreStone, ground, stoneGroup,
            [.. stone.Select(r => r.Same == 255
                ? r with { Tiles = [.. r.Tiles.Select(c => c with { Weight = c.Weight * 2 }), .. oreTiles.Select(t => new AutoTileChoice(t, 3))] }
                : r)]);
        b.Set(DomainStrings.TileBuiltIn_Sand, ground, "", Terrain(b, ground, row += 1, SandGround, [6, 3, 3]));
        b.Set(DomainStrings.TileBuiltIn_Brick, ground, "", Terrain(b, ground, row += 7, BrickGround, [8, 2]));
        b.Set(DomainStrings.TileBuiltIn_Planks, ground, "", Terrain(b, ground, row += 7, PlankGround, [6, 3]));
        b.Set(DomainStrings.TileBuiltIn_DirtWall, wall, wallGroup, Terrain(b, wall, row += 7, DirtWall, [6, 3]));
        b.Set(DomainStrings.TileBuiltIn_StoneWall, wall, wallGroup, Terrain(b, wall, row += 7, StoneWall, [6, 3]));
        b.Set(DomainStrings.TileBuiltIn_BrickWall, wall, wallGroup, Terrain(b, wall, row += 7, BrickWall, [6, 3]));
        return b.Build();
    }

    // ───────────── 바닥·배경 벽(8방향 47규칙) ─────────────

    /// <summary>47규칙 목록 자리: 3×3 상자·세로 줄·가로 줄·외딴 칸·안쪽 모서리 2×2를 왼쪽 위에 모아 목록에서 사각형으로 집어 찍기 쉽게, 나머지는 모양 차례로.</summary>
    private static readonly (int X, int Y, byte Shape)[] TerrainLayout =
    [
        (0, 0, E | SE | S), (1, 0, E | SE | S | SW | W), (2, 0, S | SW | W), (3, 0, S),
        (0, 1, N | NE | E | SE | S), (1, 1, 255), (2, 1, N | S | SW | W | NW), (3, 1, N | S),
        (0, 2, N | NE | E), (1, 2, N | NE | E | W | NW), (2, 2, N | W | NW), (3, 2, N),
        (0, 3, E), (1, 3, E | W), (2, 3, W), (3, 3, 0),
        (4, 0, 255 & ~SE), (5, 0, 255 & ~SW), (4, 1, 255 & ~NE), (5, 1, 255 & ~NW),
    ];

    /// <summary>
    /// 바닥·배경 벽 세트의 규칙(47모양, 모양마다 타일 하나): 목록 <paramref name="row"/>줄부터 7줄에 놓고, 가득 찬 모양은 무늬 차례마다 <paramref name="fillWeights"/> 비중으로 여러 장.
    /// </summary>
    private static ImmutableArray<AutoTileRule> Terrain(Builder b, int floor, int row, Ground g, int[] fillWeights)
    {
        var slots = new Queue<(int X, int Y)>();
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 8; x++)
                if (!TerrainLayout.Any(l => l.X == x && l.Y == y)) slots.Enqueue((x, y));
        var tileOf = new Dictionary<byte, ImmutableArray<AutoTileChoice>>();
        foreach (var (x, y, shape) in TerrainLayout.Where(l => l.Shape != 255))
            tileOf[shape] = [new(b.Tile(GroundTile(shape, g, 0), floor, x, row + y))];
        int n = Count(AutoTilePreset.Blob47);
        for (int i = 0; i < n; i++)
        {
            byte shape = Shape(AutoTilePreset.Blob47, i);
            if (shape == 255 || tileOf.ContainsKey(shape)) continue;
            var (x, y) = slots.Dequeue();
            tileOf[shape] = [new(b.Tile(GroundTile(shape, g, 0), floor, x, row + y))];
        }
        var fills = fillWeights.Select((w, v) =>
        {
            var (x, y) = v == 0 ? (1, 1) : slots.Dequeue();
            return new AutoTileChoice(b.Tile(GroundTile(255, g, v), floor, x, row + y), w);
        });
        tileOf[255] = [.. fills];
        return [.. Enumerable.Range(0, n).Select(i => Shape(AutoTilePreset.Blob47, i)).Select(s => Rule(AutoTilePreset.Blob47, s) with { Tiles = tileOf[s] })];
    }

    private enum Grain { Cobble, Mud, Sand, Brick, Plank }

    /// <summary>
    /// 바닥 재질: 음영 5단계(<see cref="Ramp"/>[0] = 돌 틈·깊은 안쪽 → [4] 가장 밝음), 윗면 띠 색과 그 틈 색, 무늬, 돌 씨앗(돌 무늬일 때)과 세로 납작함,
    /// 바깥 모서리 반지름, 아래·옆 빈 쪽을 돌 틈에서 파 들어가는 깊이(돌이 매달린 듯 울퉁불퉁), 배경 벽이면 외곽선 없이 가장자리만 어둡게(<see cref="Wall"/>), 깊은 안쪽에 보이는 돌 무리의 비율(<see cref="Clusters"/>, 1 = 기본).
    /// </summary>
    private sealed record Ground(Color32[] Ramp, Color32 Top, Color32 TopGap, Grain Grain, (double X, double Y)[] Seeds, double StoneR, double SquashY, double Round,
        double BottomCut, double SideCut, bool Wall = false, double Clusters = 1);

    private static readonly Ground DirtGround = new([Brown[1], Brown[2], Brown[3], Brown[4], Brown[5]], C("#EDAB50"), Fire[2], Grain.Cobble, Seeds(3, 11, 0.45), 3.6, 0.9, 2.5, 2.5, 1.5);
    private static readonly Ground MudGround = new([Brown[0], Brown[1], Brown[2], Brown[3], Brown[4]], Brown[4], Brown[3], Grain.Mud, [], 0, 1, 2.5, 0, 0);
    private static readonly Ground StoneGround = new([Cool[1], Cool[2], Cool[3], Cool[4], Cool[5]], Cool[6], Cool[5], Grain.Cobble, Seeds(2, 31, 0.4), 4.5, 1, 3, 3.5, 1, Clusters: 0.4);
    private static readonly Ground SandGround = new([Brown[2], Brown[3], Brown[4], Brown[5], Brown[6]], Brown[7], Brown[6], Grain.Sand, [], 0, 1, 2.5, 0, 0);
    private static readonly Ground BrickGround = new([Cool[1], Cool[2], Cool[3], Cool[4], Cool[5]], Cool[6], Cool[5], Grain.Brick, [], 0, 1, 1.5, 0, 0);
    private static readonly Ground PlankGround = new([Brown[1], Brown[2], Brown[3], Brown[4], Brown[5]], Brown[6], Brown[3], Grain.Plank, [], 0, 1, 1.5, 0, 0);
    private static readonly Ground DirtWall = new([C("#160E16"), C("#24151B"), Brown[1], C("#4A2525"), Brown[2]], default, default, Grain.Cobble, Seeds(3, 11, 0.45), 3.3, 0.9, 3, 1.5, 1.5, Wall: true);
    private static readonly Ground StoneWall = new([C("#0B0616"), Cool[0], Cool[1], C("#22243F"), Cool[2]], default, default, Grain.Cobble, Seeds(2, 31, 0.4), 4.5, 1, 3, 2, 2, Wall: true);
    private static readonly Ground BrickWall = new([C("#0B0616"), Cool[0], Cool[1], C("#22243F"), Cool[2]], default, default, Grain.Brick, [], 0, 1, 2, 0, 0, Wall: true);

    /// <summary>
    /// 바닥·배경 벽 한 칸: 빈 이웃 쪽은 검은 1px 외곽선(배경 벽은 외곽선 없이 가장자리를 어둡게), 아래·옆 빈 쪽은 돌 틈에서 파여 울퉁불퉁,
    /// 안쪽은 재질 무늬 — 가장자리 가까이는 밝고 깊을수록 어두워 가득 찬 칸은 거의 어두운 바탕(가끔 돌 무리).
    /// </summary>
    private static PixelImage GroundTile(byte mask, Ground g, int variant)
    {
        var img = new PixelImage(Cell, Cell);
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                var (d, nx, ny) = OpenDistance(mask, px, py, g.Round);
                double inStone = 9;
                if (g.Grain == Grain.Cobble)
                {
                    var (index, _, _, dist, edge) = Cobble(g.Seeds, px, py, g.SquashY);
                    inStone = Math.Min(edge - 1.15, StoneRadius(g, index) - dist);
                }
                // 아래·옆 빈 쪽: 돌 사이 틈을 파 들어가 돌 아랫면이 둥글게 매달림
                double cut = ny > 0.6 ? g.BottomCut : Math.Abs(nx) > 0.6 ? g.SideCut : 0;
                if (cut > 0 && inStone < 1.5) d -= cut * Math.Min(1, (1.5 - inStone) / 2.5);
                else if (g.Grain == Grain.Mud && ny > 0.6) d -= Drip(x, 0, 2);
                if (d < 0) continue;
                Color32 c;
                if (g.Wall) c = d < 1.5 ? g.Ramp[0] : Shade(g, mask, variant, x, y, 99, 0, 0);
                else c = d < 1 ? Ink : Shade(g, mask, variant, x, y, d, nx, ny);
                img.Set(x, y, c);
            }
        return img;
    }

    /// <summary>돌 하나의 반지름(돌마다 조금씩 다름).</summary>
    private static double StoneRadius(Ground g, int index) => g.StoneR * (0.85 + 0.3 * Rand(index, 0, 7));

    /// <summary>재질 무늬 한 점(<paramref name="d"/> = 빈 쪽까지 거리, (<paramref name="nx"/>, <paramref name="ny"/>) = 그쪽 방향).</summary>
    private static Color32 Shade(Ground g, byte mask, int variant, int x, int y, double d, double nx, double ny)
    {
        var r = g.Ramp;
        bool top = !g.Wall && ny < -0.6, bottom = !g.Wall && ny > 0.6, left = !g.Wall && nx < -0.6;
        double px = x + 0.5, py = y + 0.5;
        switch (g.Grain)
        {
            case Grain.Cobble:
            {
                var (index, sx, sy, dist, edge) = Cobble(g.Seeds, px, py, g.SquashY);
                double rad = StoneRadius(g, index);
                bool gap = edge < 1.15 || dist > rad;
                // 돌 하나의 밝기 단계: 돌 가운데가 빈 쪽에서 얼마나 깊은지. 이음매에 걸친 돌은 두 칸이 같은 단계로 보도록
                // 가장 깊은 단계 끝(limit)을 두 칸이 다 아는 거리(칸 − 돌 반지름) 안에 둔다.
                double depth = g.Wall ? 0 : OpenDistance(mask, sx, sy, g.Round).D;
                double near = g.StoneR + 2.5, limit = Cell - g.StoneR * 1.15 - 0.5;
                int tier = depth < near ? 0 : depth < (near + limit) / 2 ? 1 : depth < limit ? 2 : 3;
                bool inside = sx - rad > 0 && sx + rad < Cell && sy - rad > 0 && sy + rad < Cell;
                // 깊은 안쪽: 칸 안에 다 들어가는 돌만 가끔(모양·무늬 차례마다 다르게) 보임
                if (tier == 3 && inside && Rand(index, variant * 257 + mask, 40) < (variant == 0 ? 0.45 : 0.7) * g.Clusters)
                    tier = Rand(index, variant * 257 + mask, 41) < 0.4 ? 1 : 2;
                if (top && d < 2) return gap ? g.TopGap : g.Top;
                if (gap) return top && d < 3.2 ? r[2] : r[0];
                if (tier == 3) return r[0];
                // 둥근 돌: 왼쪽 위 테는 밝게, 오른쪽 아래 테는 어둡게
                double rx = (px - sx) / rad, ry = (py - sy) * g.SquashY / rad;
                double lit = rx * 0.6 + ry * 0.8;
                bool rim = Math.Min(edge - 1.15, rad - dist) < 1.6;
                int b = 3 - tier;
                int idx = lit < -0.25 && (rim || lit < -0.5) ? b + 1 : lit > 0.3 && rim ? b - 1 : b;
                if (top && d < 3.2) idx = Math.Max(idx, 3);
                if (left && d < 2.2) idx = Math.Min(4, idx + 1);
                if (bottom && d < 2.2) idx = Math.Max(0, idx - 1);
                return r[Math.Clamp(idx, 0, 4)];
            }
            case Grain.Mud:
            {
                // 고운 진흙: 깊이로 어두워지는 물결 결, 가는 줄무늬, 박힌 자갈 몇 개, 윗면은 젖어 반짝임
                double w = d + 0.9 * Math.Sin(2 * Math.PI * (x * 2 + y) / Cell);
                int idx = w < 3.5 ? 3 : w < 8 ? 2 : 1;
                int stripe = (y + (int)Math.Round(1.4 * Math.Sin(2 * Math.PI * x / Cell))) % 5;
                if (stripe == 4 && idx > 1) idx--;
                (int X, int Y)[] pebbles = variant switch { 1 => [(3, 4), (11, 10)], 2 => [(9, 3), (4, 12)], _ => [(5, 9), (12, 5)] };
                foreach (var (qx, qy) in pebbles)
                {
                    int dx = x - qx, dy = y - qy;
                    if (dx is 0 or 1 && dy is 0 or 1) idx = dx + dy == 0 ? 4 : dx + dy == 2 ? 2 : 3;
                    else if (dx == 0 && dy == 2 || dx == 2 && dy is 0 or 1) idx = 0;
                }
                if (top && d < 2) return Rand(x, 0, 51) > 0.7 ? Brown[5] : g.Top;
                if (top && d < 3.2) return r[3];
                if (left && d < 2.2) idx = Math.Min(4, idx + 1);
                if (bottom && d < 2.2) idx = Math.Max(1, idx - 1);
                return r[Math.Clamp(idx, 0, 4)];
            }
            case Grain.Sand:
            {
                // 모래: 깊이로 어두워지는 결, 물결 줄, 알갱이 점
                double w = d + 0.8 * Math.Sin(2 * Math.PI * (x * 2 + y * 1.5) / Cell);
                int idx = w < 4.5 ? 3 : w < 9 ? 2 : 1;
                int ripple = ((y + (int)Math.Round(1.2 * Math.Sin(2 * Math.PI * (x + y / 5 * 3) / 8))) % 5 + 5) % 5;
                double n = Rand(x, y, 62 + variant);
                if (ripple == 0 && Rand(x / 4, y, 61 + variant) > 0.25) idx--;
                else if (n > 0.93) idx++;
                else if (n < 0.05) idx--;
                if (top && d < 2) return g.Top;
                if (top && d < 3.2) return r[3];
                if (left && d < 2.2) idx++;
                if (bottom && d < 2.2) idx--;
                return r[Math.Clamp(idx, 0, 4)];
            }
            case Grain.Brick:
            {
                // 8×4 벽돌(줄마다 반 장 어긋남): 줄눈은 가장 어둡게, 벽돌 위·왼쪽 테는 밝게, 깊을수록 어두운 벽돌
                int rowI = y / 4, by = y % 4, off = rowI % 2 * 4;
                int bx = (x + off) % 8;
                bool mortar = by == 3 || bx == 7;
                double cx = (x + off) / 8 * 8 + 3.5 - off, cy = rowI * 4 + 1.5;
                double depth = g.Wall ? 0 : OpenDistance(mask, cx + 0.5, cy + 0.5, g.Round).D;
                int tier = depth < 4.5 ? 0 : depth < 8 ? 1 : 2;
                if (!g.Wall && tier == 2 && Rand((x + off) / 8, rowI, 70 + variant) > 0.8) tier = 1;
                if (top && d < 2) return mortar ? g.TopGap : g.Top;
                if (mortar) return r[0];
                int b = 3 - tier;
                int idx = by == 0 || bx == 0 ? b + 1 : by == 2 && bx >= 5 ? b - 1 : b;
                if (bottom && d < 2.2) idx = Math.Max(0, idx - 1);
                return r[Math.Clamp(idx, 0, 4)];
            }
            default:
            {
                // 나무 판자: 4px 판자, 판자마다 이음매 자리가 다르고 못 자국, 깊을수록 한 단계 어둡게
                int rowI = y / 4, py4 = y % 4;
                int joint = (rowI * 7 + variant * 5 + 3) % Cell;
                int shift = d < 6 ? 0 : 1;
                if (top && d < 2) return g.Top;
                if (py4 == 3 || x == joint) return r[1];
                if ((x == (joint + 2) % Cell || x == (joint + Cell - 2) % Cell) && py4 == 1) return r[1];
                int idx = py4 == 0 ? 4 : Rand(x / 3, y, 80 + rowI + variant) > 0.78 ? 2 : 3;
                if (left && d < 2.2) idx = 4;
                if (bottom && d < 2.2) idx = 2;
                return r[Math.Clamp(idx - shift, 0, 4)];
            }
        }
    }

    /// <summary>방울져 흘러내린 깊이(칸 크기로 이어짐, 0 ~ <paramref name="max"/> px).</summary>
    private static double Drip(int x, int salt, int max)
    {
        double a = 2 * Math.PI * x / Cell;
        double v = 0.5 + 0.35 * Math.Sin(a * 2 + salt) + 0.25 * Math.Sin(a * 3 + 1.7 + salt);
        return Math.Clamp(v, 0, 1) * max;
    }

    /// <summary>광석 섞인 돌 바탕: 돌 무늬 위에 광석 덩이 셋(밝은 왼쪽 위, 어두운 테두리).</summary>
    private static PixelImage OreStone(Color32[] ore, int v)
    {
        var img = GroundTile(255, StoneGround, v % 3);
        (int X, int Y)[] spots = (v % 2) switch { 0 => [(4, 4), (11, 8), (5, 12)], _ => [(11, 3), (4, 8), (11, 12)] };
        var p = new Dictionary<char, Color32> { ['0'] = ore[0], ['1'] = ore[1], ['2'] = ore[2], ['3'] = ore[3] };
        var nugget = Art(p, ".23.", "2331", "2210", ".10.");
        var rimmed = new PixelImage(6, 6);
        Stamp(rimmed, nugget, 1, 1);
        Outline(rimmed, Cool[0]);
        foreach (var (sx, sy) in spots) Stamp(img, rimmed, sx - 3, sy - 3);
        return img;
    }

    private static PixelImage WoodWall(int v) => Paint(Cell, Cell, (x, y) =>
    {
        // 세로 널빤지 벽(4px), 널마다 다른 이음매
        int px = x % 4, board = x / 4;
        int joint = (board * 5 + v * 7 + 2) % Cell;
        if (px == 3 || y == joint) return Brown[0];
        if (px == 0) return C("#4A2525");
        return Rand(x, y / 3, 600 + v) > 0.82 ? Brown[0] : Brown[1];
    });

    // ───────────── 덮개(풀·눈·낙엽) ─────────────

    /// <summary>덮개 재질: 색 4단계(아래 어두움 → 위 밝음), x마다 흘러내린 깊이(px), 고드름처럼 뾰족한지.</summary>
    private sealed record CoverStyle(Color32[] Ramp, int[] Depth, bool Icicle, int Salt);

    private static readonly CoverStyle GrassCover = new([Green[1], Green[3], Green[4], Green[5]], [8, 9, 8, 7, 8, 10, 9, 8, 7, 8, 9, 11, 9, 8, 7, 8], false, 1);
    private static readonly CoverStyle SnowCover = new([Blue[3], Blue[5], Blue[6], White], [7, 8, 9, 10, 11, 10, 9, 8, 7, 8, 9, 10, 11, 10, 9, 8], true, 2);
    private static readonly CoverStyle AutumnCover = new([Fire[3], Fire[4], Fire[5], Fire[6]], [8, 9, 9, 10, 9, 8, 7, 8, 9, 10, 11, 10, 9, 8, 7, 7], false, 3);

    /// <summary>덮개 세트 규칙(가로 줄 틀): 외딴 칸, 왼쪽 끝(오른쪽에 이웃), 가운데 3가지(비중), 오른쪽 끝 — 목록 한 줄 6칸.</summary>
    private static ImmutableArray<AutoTileRule> CoverRules(Builder b, int floor, int row, CoverStyle st)
    {
        int alone = b.Tile(CoverTile(false, false, st, 0), floor, 0, row);
        int leftEnd = b.Tile(CoverTile(false, true, st, 0), floor, 1, row);
        var mid = Enumerable.Range(0, 3).Select(v => new AutoTileChoice(b.Tile(CoverTile(true, true, st, v), floor, 2 + v, row), v == 0 ? 3 : 2)).ToArray();
        int rightEnd = b.Tile(CoverTile(true, false, st, 0), floor, 5, row);
        return LineRules(AutoTilePreset.Row, [[new(alone)], [new(leftEnd)], [.. mid], [new(rightEnd)]]);
    }

    /// <summary>
    /// 덮개 한 칸(바닥 윗면 칸에 덧칠): 칸 위 끝부터 아래로 흘러내린 덮개, 둘레는 검은 외곽선, 위쪽 2px은 가장 밝게, 아래로 갈수록 어둡게.
    /// 이웃이 없는 쪽은 바닥과 같은 반지름으로 둥글게 닫고 옆면으로 조금 더 흘러내린다. 가운데 무늬 차례는 칸 가장자리 깊이가 같아 서로 이어진다.
    /// </summary>
    private static PixelImage CoverTile(bool left, bool right, CoverStyle st, int variant)
    {
        var r = st.Ramp;
        var img = new PixelImage(Cell, Cell);
        byte mask = (byte)(S | SE | SW | (left ? W : 0) | (right ? E : 0));
        for (int x = 0; x < Cell; x++)
        {
            double h = st.Depth[x];
            if (variant > 0 && x is > 2 and < 13) h += Rand(x, variant, 90 + st.Salt) > 0.6 ? 1 : Rand(x, variant, 91) > 0.75 ? -1 : 0;
            if (!left && x < 3) h = Math.Max(h, 9 - x);
            if (!right && x > Cell - 4) h = Math.Max(h, 9 - (Cell - 1 - x));
            for (int y = 0; y < Cell; y++)
            {
                double px = x + 0.5, py = y + 0.5;
                var (d, nx, ny) = OpenDistance(mask, px, py, 2.5);
                double toBottom = h - py;
                // 고드름: 뾰족한 끝은 1px 폭
                if (st.Icicle && toBottom < 2 && toBottom >= 0) toBottom -= x % 8 is 4 ? 0 : 0.6;
                bool sideNear = d < toBottom;
                double e = Math.Min(d, toBottom);
                if (e < 0) continue;
                if (e < 1) { img.Set(x, y, Ink); continue; }
                int idx;
                if (!sideNear && toBottom < 2) idx = 0;
                else if (ny < -0.6 && d < 3) idx = 3;
                else
                {
                    double rel = py / h;
                    double n = Rand(x, y, 95 + st.Salt + variant * 7);
                    idx = rel < 0.45 ? (n > 0.8 ? 2 : 3) : rel < 0.72 ? (n > 0.75 ? 3 : n < 0.12 ? 1 : 2) : (n > 0.7 ? 2 : 1);
                }
                if (sideNear && nx < -0.6 && d < 2) idx = Math.Max(idx, 2);
                if (sideNear && nx > 0.6 && d < 2) idx = Math.Min(idx, 1);
                img.Set(x, y, r[idx]);
            }
        }
        return img;
    }

    // ───────────── 물·용암 ─────────────

    /// <summary>
    /// 물·용암 재질: 수면 높이(칸 위에서 px), 물결 높이, 한 장마다 옆으로 흐르는 px, 그림 장수·한 장 시간, 색(수면 줄·수면 아래 띠 둘·몸통·반짝임·몸통 어두운 무늬), 불투명도.
    /// </summary>
    private sealed record LiquidStyle(double Level, double Wave, int Step, int Frames, int FrameMs, Color32 Line, Color32 Band1, Color32 Band2, Color32 Body, Color32 Glint, Color32 Dark, bool Lava);

    private static readonly LiquidStyle Water = new(3, 1, 2, 8, Tick, Blue[6], Blue[5].WithAlpha(235), Blue[4].WithAlpha(205), Blue[3].WithAlpha(170), Blue[5].WithAlpha(215), Blue[2].WithAlpha(185), false);
    private static readonly LiquidStyle Lava = new(3, 0.6, 4, 4, Tick * 2, Fire[6], Fire[5], Fire[4], Fire[3], Fire[5], Fire[2], true);

    // 용암 껍질 조각 무늬의 씨앗
    private static readonly (double X, double Y)[] LavaPlates = Seeds(2, 51, 0.5);

    // 물 둘레 한쪽(왼쪽·오른쪽): 빔, 이어짐(옆 칸도 수면), 비스듬히 이어짐(위가 빈 칸에서 옆 칸 위까지 물 — 더 높은 물 옆)
    private enum LiquidSide { Open, Join, Slope }

    /// <summary>
    /// 물·용암 세트 규칙(13가지, 그림 차례는 수면 9 → 몸통 4): 위에 뭐라도 있으면(물이든 바닥 블록이든 — "뭐라도 있음") 몸통 칸, 아니면 수면 칸.
    /// 좌우는 각각 빔(옆 가장자리 + 수면이면 비스듬히 깎인 모서리)·막힘(옆에 뭐라도 있음 — 물이거나 벽, 가장자리 없음)·비스듬히 이어짐(수면 칸에서 옆과 옆 위가 물이면
    /// 수면이 그쪽으로 비스듬히 올라감). 몸통 규칙을 앞에 두어 벽 아래 물은 수면 없이 꽉 찬다. 아래쪽은 보지 않는다. 그림마다 <see cref="LiquidStyle.Frames"/>장.
    /// </summary>
    private static ImmutableArray<AutoTileRule> LiquidRules(Builder b, int floor, int row, LiquidStyle st)
    {
        var top = new List<AutoTileRule>();
        var body = new List<AutoTileRule>();
        int k = 0;
        int Tile(bool isTop, LiquidSide l, LiquidSide r)
        {
            var frames = Enumerable.Range(0, st.Frames).Select(f => LiquidTile(isTop, l, r, st, f)).ToArray();
            int id = b.Animated(frames, st.FrameMs, floor, k % 8, row + k / 8);
            k++;
            return id;
        }
        // 한쪽 조건(같음, 다름, 뭐라도 있음): 빔 = 옆이 물이 아님, 막힘 = 옆에 뭐라도 있음(수면이면 옆 위는 물이 아님), 비스듬히 = 옆과 옆 위가 물
        static (int, int, int) Side(int same, int other, int filled, LiquidSide s, int side, int corner, bool isTop) => s switch
        {
            LiquidSide.Open => (same, other | side, filled),
            LiquidSide.Join => (same, other | (isTop ? corner : 0), filled | side),
            _ => (same | side | corner, other, filled),
        };
        LiquidSide[] topSides = [LiquidSide.Join, LiquidSide.Open, LiquidSide.Slope];
        foreach (var r in topSides)
            foreach (var l in topSides)
            {
                var (same, other, filled) = Side(0, N, 0, r, E, NE, true);
                (same, other, filled) = Side(same, other, filled, l, W, NW, true);
                top.Add(new AutoTileRule { Same = (byte)same, Other = (byte)other, Filled = (byte)filled, Tiles = [new(Tile(true, l, r))] });
            }
        foreach (var r in new[] { LiquidSide.Join, LiquidSide.Open })
            foreach (var l in new[] { LiquidSide.Join, LiquidSide.Open })
            {
                var (same, other, filled) = Side(0, 0, N, r, E, NE, false);
                (same, other, filled) = Side(same, other, filled, l, W, NW, false);
                body.Add(new AutoTileRule { Same = (byte)same, Other = (byte)other, Filled = (byte)filled, Tiles = [new(Tile(false, l, r))] });
            }
        return [.. body, .. top];
    }

    /// <summary>
    /// 물·용암 한 칸의 그림 <paramref name="frame"/>: 수면(위가 빈 칸)은 칸 주기 물결이 한 장마다 <see cref="LiquidStyle.Step"/>px씩 옆으로 흐르고,
    /// 빈 옆쪽은 밝은 가장자리 선과 비스듬히 깎인 위 모서리, 더 높은 물 옆(<see cref="LiquidSide.Slope"/>)은 수면이 2:1로 비스듬히 올라가 이어진다.
    /// 몸통은 반짝이는 줄이 흐르고(물), 용암은 어두운 껍질 무늬가 천천히 바뀌며 수면에 거품이 터진다. 모든 무늬가 x 방향 칸 주기라 어느 순간에도 이음매가 맞는다.
    /// </summary>
    private static PixelImage LiquidTile(bool top, LiquidSide left, LiquidSide right, LiquidStyle st, int frame)
    {
        var img = new PixelImage(Cell, Cell);
        int shift = frame * st.Step;
        double Surf(double x) => st.Level + Math.Round(st.Wave * Math.Sin(2 * Math.PI * (x - shift) / Cell));
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                // 빈 곳까지 거리: 수면(아래로 잼)과 빈 옆
                double surf = 99, side = 99;
                if (top)
                {
                    double s = Surf(px);
                    if (right == LiquidSide.Slope) s = Math.Min(s, st.Level * (Cell - px) / (st.Level * 2));
                    if (left == LiquidSide.Slope) s = Math.Min(s, st.Level * px / (st.Level * 2));
                    // 빈 옆으로 비스듬히 깎인 모서리
                    if (right == LiquidSide.Open) s = Math.Max(s, st.Level + 3 - (Cell - px));
                    if (left == LiquidSide.Open) s = Math.Max(s, st.Level + 3 - px);
                    surf = py - s;
                }
                if (right == LiquidSide.Open) side = Math.Min(side, Cell - px);
                if (left == LiquidSide.Open) side = Math.Min(side, px);
                if (surf < 0) continue;
                Color32 c;
                if (surf < 1) c = st.Lava || Surf(px) >= st.Level ? st.Line : White;
                else if (side < 1) c = st.Band1;
                else if (surf < 2) c = st.Band1;
                else if (surf < 3.5 || side < 2) c = st.Band2;
                else if (st.Lava)
                {
                    // 칸 주기로 이어지는 식은 껍질 조각이 옆으로 흐르고, 조각 사이 틈은 밝게 녹아 있음
                    var (index, _, _, _, edge) = Cobble(LavaPlates, px - shift, py);
                    c = edge < 1.1 ? st.Glint : edge < 1.9 ? st.Band2 : Rand(index, 0, 97) < 0.35 ? st.Dark : st.Body;
                }
                else
                {
                    // 반짝이는 짧은 줄이 흐름(한 바퀴에 칸 너비만큼 움직여 처음 그림으로 이어짐)
                    int u = ((x - shift) % Cell + Cell) % Cell, v = ((x + shift + 9) % Cell + Cell) % Cell;
                    bool glint = y == 8 && u is >= 2 and <= 4 || y == 13 && v is >= 10 and <= 11;
                    c = glint ? st.Glint : st.Body;
                }
                img.Set(x, y, c);
            }
        // 용암 거품: 수면 위로 부풀었다 터짐(칸마다 두 자리, 장마다 단계)
        if (st.Lava && top)
            foreach (var (bx, start) in new[] { (5, 0), (11, 2) })
            {
                int stage = (frame + start) % st.Frames;
                if (left != LiquidSide.Join && bx < 6 || right != LiquidSide.Join && bx > 10) continue;
                int sy = (int)st.Level;
                if (stage == 1) { img.Set(bx, sy - 1, st.Line); img.Set(bx + 1, sy - 1, st.Line); }
                else if (stage == 2)
                {
                    img.Set(bx, sy - 2, st.Line); img.Set(bx + 1, sy - 2, st.Line);
                    img.Set(bx - 1, sy - 1, st.Line); img.Set(bx + 2, sy - 1, st.Line);
                    img.Set(bx, sy - 1, st.Band2); img.Set(bx + 1, sy - 1, st.Band2);
                }
                else if (stage == 3) { img.Set(bx - 1, sy - 2, st.Band1); img.Set(bx + 2, sy - 3, st.Band1); }
            }
        return img;
    }

    // ───────────── 줄 자동 타일(발판·사다리·덩굴·울타리) ─────────────

    /// <summary>
    /// 가로·세로 줄 틀의 규칙: 틀의 모양 차례(외딴, 오른쪽·아래에 이웃, 양쪽, 왼쪽·위에 이웃)마다 <paramref name="draw"/>(앞쪽 이웃, 뒤쪽 이웃)로 그린 타일, 목록 (<paramref name="x"/>, <paramref name="y"/>)부터 4칸.
    /// </summary>
    private static ImmutableArray<AutoTileRule> Line(Builder b, AutoTilePreset preset, int floor, int x, int y, Func<bool, bool, PixelImage> draw)
    {
        (bool Before, bool After)[] ends = [(false, false), (false, true), (true, true), (true, false)];
        return LineRules(preset, [.. ends.Select((e, i) => ImmutableArray.Create(new AutoTileChoice(b.Tile(draw(e.Before, e.After), floor, x + i, y))))]);
    }

    private static ImmutableArray<AutoTileRule> LineRules(AutoTilePreset preset, ImmutableArray<AutoTileChoice>[] tiles)
        => [.. Rules(preset).Select((r, i) => r with { Tiles = tiles[i] })];

    /// <summary>나무 발판(가로 줄): 위 5px 판자(판자 이음매), 끝(이웃이 없는 쪽)에는 아래로 받침 빗대.</summary>
    private static PixelImage Platform(bool left, bool right)
    {
        var img = Paint(Cell, Cell, (x, y) =>
        {
            if (y is >= 1 and <= 4)
            {
                if (x == 7) return Brown[2];
                return y == 1 ? Brown[5] : y == 4 ? Brown[3] : Brown[4];
            }
            // 끝 받침: 바깥 세로 막대와 안쪽으로 올라가는 빗대
            if (y is >= 5 and < 12 && (!left && (x is 2 or 3 || x - 3 == 11 - y) || !right && (x is 12 or 13 || 12 - x == 11 - y)))
                return x is 2 or 12 ? Brown[4] : Brown[3];
            return null;
        });
        Outline(img, Ink);
        return img;
    }

    /// <summary>사다리(세로 줄): 두 기둥과 가로대, 맨 위는 기둥 끝이 둥글게.</summary>
    private static PixelImage Ladder(bool up, bool down)
    {
        var img = Paint(Cell, Cell, (x, y) =>
        {
            if (!up && y < 2) return null;
            if (x is 3 or 4 || x is 11 or 12) return x is 3 or 11 ? Brown[5] : Brown[3];
            if (x is > 4 and < 11 && y % 8 is 5 or 6) return y % 8 == 5 ? Brown[5] : Brown[3];
            return null;
        });
        Outline(img, Ink);
        return img;
    }

    /// <summary>덩굴(세로 줄, 천장에서 늘어짐): 구불구불한 줄기와 엇갈린 둥근 잎, 아래 끝은 잎 송이.</summary>
    private static PixelImage Vine(bool up, bool down)
    {
        var img = new PixelImage(Cell, Cell);
        int end = down ? Cell : 10;
        for (int y = 0; y < end; y++)
        {
            int x = 8 + (int)Math.Round(1.5 * Math.Sin(2 * Math.PI * y / Cell));
            img.Set(x, y, Green[3]);
        }
        void Leaf(int cx, int cy, int dir)
        {
            img.Set(cx, cy, Green[5]); img.Set(cx + dir, cy, Green[4]);
            img.Set(cx, cy + 1, Green[4]); img.Set(cx + dir, cy + 1, Green[3]);
        }
        for (int y = 1; y < end - 1; y += 4)
        {
            int x = 8 + (int)Math.Round(1.5 * Math.Sin(2 * Math.PI * y / Cell));
            int dir = y % 8 == 1 ? -1 : 1;
            Leaf(x + dir, y, dir);
        }
        if (!down)
        {
            Leaf(7, 10, -1); Leaf(9, 10, 1); img.Set(8, 12, Green[4]);
        }
        Outline(img, Ink);
        return img;
    }

    /// <summary>울타리(가로 줄): 끝이 뾰족한 말뚝 둘과 가로대 둘, 끝(이웃이 없는 쪽)에서 가로대가 멈춘다.</summary>
    private static PixelImage Fence(bool left, bool right)
    {
        var img = Paint(Cell, Cell, (x, y) =>
        {
            int px = x % 8;
            if (px is 3 or 4 && y >= 4 || px == 3 && y == 3) return px == 3 ? Brown[5] : Brown[3];
            bool rail = y is 7 or 8 or 12 or 13;
            if (rail && (left || x >= 3) && (right || x <= 12)) return y is 7 or 12 ? Brown[4] : Brown[3];
            return null;
        });
        Outline(img, Ink);
        return img;
    }

    // ───────────── 장식 ─────────────

    /// <summary>둥근 잎 나무(32×44): 뿌리가 퍼진 줄기와 두 가지, 위에 둥근 잎 덩어리(색 <paramref name="leaf"/> 어두움 → 밝음 5단계), 뿌리께 풀.</summary>
    private static PixelImage Tree(Color32[] leaf, int seed)
    {
        var img = new PixelImage(32, 44);
        for (int y = 22; y < 44; y++)
        {
            double half = 2.5 + (y > 38 ? (y - 38) / 1.6 : 0);
            for (int x = 0; x < 32; x++)
            {
                double dx = x + 0.5 - 16;
                if (Math.Abs(dx) >= half) continue;
                double t = (dx + half) / (2 * half);
                img.Set(x, y, t < 0.3 ? Brown[4] : t < 0.7 ? Brown[3] : Brown[2]);
            }
        }
        for (int i = 0; i < 6; i++)
        {
            img.Set(14 - i, 32 - i, Brown[3]); img.Set(14 - i, 33 - i, Brown[2]);
            img.Set(17 + i, 30 - i, Brown[3]); img.Set(17 + i, 31 - i, Brown[2]);
        }
        Outline(img, Ink);
        var crown = new PixelImage(32, 44);
        Clumps(crown,
        [
            new(8, 22, 6.5, 5.5), new(24, 22, 6.5, 5.5), new(16, 24, 8, 5),
            new(16, 13, 10, 9), new(8, 14, 6.5, 6), new(24, 13, 6.5, 6),
            new(12, 6, 6, 5), new(21, 6, 6, 5),
        ], leaf[1..], leaf[0], Ink, seed, speckle: 0.1);
        Stamp(img, crown, 0, 0);
        // 뿌리께 풀
        foreach (var (x, h) in new[] { (11, 2), (12, 3), (20, 3), (21, 2), (22, 1) })
            for (int i = 0; i < h; i++) img.Set(x, 43 - i, i == h - 1 ? Green[5] : Green[4]);
        return img;
    }

    /// <summary>바늘잎 나무(24×40): 층층이 겹친 세모 잎(아래 층부터 그려 위 층이 덮음), 검은 외곽선.</summary>
    private static PixelImage Pine()
    {
        var img = new PixelImage(24, 40);
        for (int y = 32; y < 40; y++)
            for (int x = 10; x < 14; x++)
                img.Set(x, y, x < 11 ? Brown[4] : x < 13 ? Brown[3] : Brown[2]);
        (int Top, int Bottom, double Half)[] tiers = [(1, 12, 5.5), (7, 21, 8), (14, 30, 10.5), (22, 34, 11)];
        for (int t = tiers.Length - 1; t >= 0; t--)
        {
            var (topY, bottomY, half) = tiers[t];
            for (int y = topY; y <= bottomY; y++)
            {
                double w = half * (y - topY + 1) / (bottomY - topY + 1);
                for (int x = 0; x < 24; x++)
                {
                    double dx = x + 0.5 - 12;
                    if (Math.Abs(dx) > w) continue;
                    double s = dx / Math.Max(1, w);
                    var c = y >= bottomY - 1 ? Green[2] : s < -0.4 ? Green[5] : s < 0.25 ? Green[4] : Green[3];
                    img.Set(x, y, c);
                }
            }
            // 층 아래 테두리 그늘
            for (int x = 0; x < 24; x++)
                if (img.Get(x, bottomY + 1).A == 0 && img.Get(x, bottomY).A > 0) img.Set(x, bottomY, Green[2]);
        }
        Outline(img, Ink);
        return img;
    }

    private static PixelImage BigBush()
    {
        var img = new PixelImage(32, Cell);
        Clumps(img, [new(8, 12, 7, 5), new(24, 12, 7, 5), new(16, 10, 9, 7), new(10, 7, 5.5, 4.5), new(21, 6, 6, 5)], Green[3..6], Green[2], Ink, 830, speckle: 0.1);
        return img;
    }

    private static PixelImage Bush()
    {
        var img = new PixelImage(Cell, Cell);
        Clumps(img, [new(5, 12, 4.5, 4), new(11, 12, 4.5, 4), new(8, 9, 5.5, 5)], Green[3..6], Green[2], Ink, 840, speckle: 0.1);
        foreach (var (x, y) in new[] { (5, 9), (10, 10), (8, 13) }) { img.Set(x, y, Red[3]); img.Set(x + 1, y, Red[2]); img.Set(x, y - 1, Red[5]); }
        return img;
    }

    private static PixelImage Rock(bool small)
    {
        var img = new PixelImage(Cell, Cell);
        Blob[] blobs = small ? [new(8, 13, 4.5, 3)] : [new(6, 12, 5, 4), new(10, 11, 5, 5), new(12, 14, 3.5, 2.5)];
        Clumps(img, blobs, Cool[3..6], Cool[2], Ink, small ? 851 : 850);
        return img;
    }

    /// <summary>선인장(16×24): 굵은 몸통과 양팔, 세로 골, 가시 점.</summary>
    private static PixelImage Cactus()
    {
        var img = Paint(Cell, 24, (x, y) =>
        {
            bool body = x is >= 6 and <= 9 && y >= 2;
            bool armL = x is >= 2 and <= 3 && y is >= 7 and <= 13 || x is >= 2 and <= 6 && y is 12 or 13;
            bool armR = x is >= 12 and <= 13 && y is >= 4 and <= 10 || x is >= 9 and <= 13 && y is 9 or 10;
            if (!(body || armL || armR)) return null;
            if (body) return x == 6 ? Green[5] : x == 9 ? Green[3] : (y + x) % 5 == 0 ? Green[5] : Green[4];
            return x is 2 or 12 ? Green[5] : Green[3];
        });
        Outline(img, Ink);
        // 몸통 꼭대기·팔 끝을 둥글게
        foreach (var (x, y) in new[] { (6, 2), (9, 2), (2, 7), (13, 4) }) img.Set(x, y, Ink);
        foreach (var (x, y) in new[] { (7, 5), (8, 9), (7, 15), (8, 19), (3, 9), (12, 6) }) img.Set(x, y, Green[6]);
        return img;
    }

    /// <summary>풀 포기: 바닥에서 휘어 오르는 잎(아래 어둡게 → 끝 밝게), 검은 외곽선.</summary>
    private static PixelImage GrassTuft(bool tall)
    {
        var img = new PixelImage(Cell, Cell);
        (int X, int H, double Lean)[] blades = tall
            ? [(4, 9, -0.3), (6, 12, -0.1), (8, 10, 0.15), (10, 13, 0.2), (12, 8, 0.35)]
            : [(4, 4, -0.4), (6, 6, -0.15), (8, 5, 0.1), (10, 6, 0.25), (12, 4, 0.4)];
        foreach (var (bx, h, lean) in blades)
            for (int i = 0; i < h; i++)
            {
                double f = i / (double)h;
                int x = (int)Math.Round(bx + lean * i * f * 2);
                img.Set(Math.Clamp(x, 1, Cell - 2), Cell - 1 - i, f < 0.35 ? Green[3] : f < 0.75 ? Green[4] : Green[5]);
            }
        Outline(img, Ink);
        return img;
    }

    /// <summary>꽃: 네 꽃잎 송이(색 <paramref name="petal"/> 어두움·바탕·밝음)와 가운데, 줄기와 잎 둘.</summary>
    private static PixelImage Flower(Color32[] petal, Color32 center)
    {
        var img = Paint(Cell, Cell, (x, y) =>
        {
            double dx = x + 0.5 - 8, dy = y + 0.5 - 5.5;
            bool bloom = Math.Abs(dx) <= 1.6 && Math.Abs(dy) <= 3.6 || Math.Abs(dy) <= 1.6 && Math.Abs(dx) <= 3.6 || dx * dx + dy * dy < 7.5;
            if (bloom)
            {
                if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1) return center;
                return dx + dy < -2 ? petal[2] : dx + dy > 2 ? petal[0] : petal[1];
            }
            if (x is 7 or 8 && y >= 9) return x == 7 ? Green[4] : Green[3];
            if (y is 11 or 12 && x is >= 4 and <= 6 && x - 4 >= 12 - y - 1) return Green[5];
            if (y is 12 or 13 && x is >= 9 and <= 11 && 11 - x >= y - 12) return Green[4];
            return null;
        });
        Outline(img, Ink);
        return img;
    }

    private static PixelImage Mushroom()
    {
        var p = new Dictionary<char, Color32> { ['o'] = Ink, ['r'] = Red[3], ['R'] = Red[1], ['h'] = Red[5], ['w'] = White, ['s'] = Brown[7], ['S'] = Brown[6] };
        return Art(p,
            "................",
            "................",
            "................",
            "................",
            "................",
            ".....oooooo.....",
            "....ohhrwwro....",
            "...ohrrrwwrro...",
            "...owwrrrrrRo...",
            "..owwrrrrwrRRo..",
            "..oRRRrrrRRRRo..",
            "...oooooooooo...",
            "......osSo......",
            "......osSo......",
            ".....ossSSo.....",
            ".....osssSo.....");
    }

    /// <summary>빛나는 버섯(동굴): 청록 갓 두 개와 둘레의 옅은 빛.</summary>
    private static PixelImage GlowMushroom()
    {
        var img = new PixelImage(Cell, Cell);
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                double d = Math.Sqrt((x + 0.5 - 8) * (x + 0.5 - 8) + (y + 0.5 - 10) * (y + 0.5 - 10));
                if (d < 7.5) Put(img, x, y, Blue[6].WithAlpha((byte)(d < 4.5 ? 50 : 26)));
            }
        var p = new Dictionary<char, Color32> { ['o'] = Ink, ['c'] = Blue[4], ['C'] = Blue[3], ['h'] = Blue[6], ['s'] = Brown[7], ['S'] = Cool[5] };
        Stamp(img, Art(p,
            "................",
            "................",
            "................",
            "................",
            "................",
            ".....oooo.......",
            "....ohhccCo.....",
            "...ohccccCCo....",
            "...oCCCCCCCo....",
            "....ooosSoo.....",
            "......osSo.oooo.",
            "......osSoohccCo",
            "......osSooCCCCo",
            "......osSo.oSSo.",
            ".....osssSooSSo.",
            ".....osssSooSSo."), 0, 0);
        return img;
    }

    /// <summary>종유석(천장에 매달림, <paramref name="down"/>)·석순(바닥에서 솟음): 돌 고깔 둘, 검은 외곽선.</summary>
    private static PixelImage Spike(bool down)
    {
        var img = new PixelImage(Cell, Cell);
        void Cone(double cx, double len, double half)
        {
            for (int i = 0; i < len; i++)
            {
                double w = half * (1 - i / len);
                int y = down ? i : Cell - 1 - i;
                for (int x = 0; x < Cell; x++)
                {
                    double dx = x + 0.5 - cx;
                    if (Math.Abs(dx) > w) continue;
                    double s = dx / Math.Max(0.8, w);
                    img.Set(x, y, s < -0.3 ? Cool[5] : s < 0.35 ? Cool[4] : Cool[3]);
                }
            }
        }
        Cone(5, 9, 3.5);
        Cone(10.5, 14, 4.5);
        Outline(img, Ink);
        return img;
    }

    /// <summary>수정 무리: 육각 기둥 셋(왼쪽 면 밝게), 옅은 빛.</summary>
    private static PixelImage Crystal()
    {
        var img = new PixelImage(Cell, Cell);
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                double d = Math.Sqrt((x + 0.5 - 8) * (x + 0.5 - 8) + (y + 0.5 - 11) * (y + 0.5 - 11));
                if (d < 7) Put(img, x, y, Violet[4].WithAlpha(36));
            }
        // 기둥마다 따로 외곽선을 둘러 뒤(가운데 큰 것)부터 겹침
        void Prism(int cx, int topY, int half)
        {
            var shard = new PixelImage(Cell, Cell);
            for (int y = topY; y < Cell; y++)
                for (int x = cx - half; x <= cx + half; x++)
                {
                    int fromTop = y - topY;
                    if (Math.Abs(x - cx) > fromTop) continue; // 뾰족한 머리
                    var c = x < cx ? Violet[4] : x == cx ? Violet[3] : Violet[2];
                    if (fromTop == Math.Abs(x - cx) && x <= cx || x == cx - half && y > topY + half + 1 && y % 3 == 0) c = Violet[5];
                    shard.Set(x, y, c);
                }
            Outline(shard, Ink);
            Stamp(img, shard, 0, 0);
        }
        Prism(8, 2, 2);
        Prism(4, 8, 2);
        Prism(12, 9, 2);
        return img;
    }

    /// <summary>
    /// 횃불(벽에 꽂힌 나무 자루, 그림 4장): 불꽃 모양·빛이 장마다 흔들림. 차지 칸은 1칸이고 그림은 1×2칸 —
    /// 횃불은 아래 칸에, 빛은 위 칸까지 번진다(그림 아래 맞춤이라 위로 솟아 그려짐).
    /// </summary>
    private static PixelImage Torch(int frame)
    {
        const int top = Cell; // 횃불이 놓이는 아래 칸의 위쪽 y
        var img = new PixelImage(Cell, Cell * 2);
        double rx = frame % 2 == 0 ? 6.8 : 7.6, ry = frame % 2 == 0 ? 10.5 : 11.5;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < Cell; x++)
            {
                double dx = (x + 0.5 - 8) / rx, dy = (y + 0.5 - (top + 4)) / ry;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1) Put(img, x, y, Fire[5].WithAlpha((byte)(d < 0.5 ? 54 : 28)));
            }
        var stick = Paint(Cell, Cell, (x, y) => y >= 9 && x is 7 or 8 ? (x == 7 ? Brown[4] : Brown[3]) : y == 8 && x is >= 6 and <= 9 ? Cool[4] : null);
        Outline(stick, Ink);
        Stamp(img, stick, 0, top);
        string[][] flames =
        [
            ["....r...", "...rfr..", "..rfyfr.", "..fywyf.", "..fyyyf.", "...fff.."],
            ["...r....", "...rf...", "..rfyr..", "..fywyf.", "..fyyyf.", "...fff.."],
            [".....r..", "....rf..", "..rfyfr.", ".rfywyf.", "..fyyyf.", "...fff.."],
            ["...r.r..", "..rfrfr.", "..rfyfr.", "..fywyf.", "..fyyyf.", "...fff.."],
        ];
        var p = new Dictionary<char, Color32> { ['r'] = Fire[1], ['f'] = Fire[3], ['y'] = Fire[5], ['w'] = Fire[6] };
        Stamp(img, Art(p, flames[frame % 4]), 4, top + 2);
        return img;
    }

    /// <summary>동전(그림 4장, 돌아감): 앞면 → 기욺 → 옆 → 기욺.</summary>
    private static PixelImage Coin(int frame)
    {
        double half = frame switch { 0 => 3.6, 1 => 2.4, 2 => 0.9, _ => 2.4 };
        var img = Paint(Cell, Cell, (x, y) =>
        {
            double dx = (x + 0.5 - 8) / half, dy = (y + 0.5 - 9) / 3.8;
            double r2 = dx * dx + dy * dy;
            if (r2 > 1) return null;
            if (frame == 2) return y % 3 == 0 ? Fire[4] : Fire[5];
            if (r2 > 0.55) return dx + dy * 0.6 < 0 ? Fire[6] : Fire[3];
            return frame == 0 && x == 8 && y is >= 7 and <= 10 ? Fire[3] : Fire[5];
        });
        Outline(img, Ink);
        return img;
    }

    /// <summary>보석(색 <paramref name="c"/> 어두움 → 밝음 6단계): 위가 넓은 깎은 모양, 면마다 밝기.</summary>
    private static PixelImage Gem(Color32[] c)
    {
        var p = new Dictionary<char, Color32> { ['0'] = c[0], ['1'] = c[1], ['2'] = c[2], ['3'] = c[3], ['4'] = c[4], ['5'] = c[5], ['w'] = White };
        var img = Art(p,
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            ".....444333.....",
            "....45w44332....",
            "....44443322....",
            ".....443221.....",
            "......4321......",
            ".......21.......",
            "................",
            "................",
            "................");
        Outline(img, Ink);
        return img;
    }

    private static PixelImage Key()
    {
        var p = new Dictionary<char, Color32> { ['1'] = Fire[3], ['2'] = Fire[5], ['3'] = Fire[6] };
        var img = Art(p,
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "..323...........",
            ".3...32222222...",
            ".2...21111111...",
            "..211......1.1..",
            "................");
        Outline(img, Ink);
        return img;
    }

    /// <summary>항아리(점토): 좁은 목과 둥근 몸통, 가운데 띠 무늬.</summary>
    private static PixelImage Pot()
    {
        var img = Paint(Cell, Cell, (x, y) =>
        {
            if (y < 4) return null;
            double half = y == 4 ? 3.5 : y is 5 or 6 ? 2.5 : 2.5 + 3.5 * Math.Sin(Math.PI * (y - 6) / 10.5);
            double dx = x + 0.5 - 8;
            if (Math.Abs(dx) > half) return null;
            double t = (dx + half) / (2 * half);
            if (y == 4) return Fire[2];
            if (y is 10 or 11) return t < 0.3 ? Blue[5] : t < 0.75 ? Blue[3] : Blue[2];
            return t < 0.3 ? Fire[2] : t < 0.75 ? Fire[1] : Fire[0];
        });
        Outline(img, Ink);
        return img;
    }

    private static PixelImage Chest()
    {
        var p = new Dictionary<char, Color32>
        {
            ['o'] = Ink, ['1'] = Brown[2], ['2'] = Brown[3], ['3'] = Brown[4], ['4'] = Brown[5],
            ['m'] = Cool[3], ['M'] = Cool[5], ['y'] = Fire[5], ['Y'] = Fire[3], ['L'] = Fire[6],
        };
        return Art(p,
            "................",
            "................",
            "................",
            "..oooooooooooo..",
            ".oM4444444444Mo.",
            ".oM3333333333Mo.",
            ".oM2222222222Mo.",
            ".ommmmmyLmmmmmo.",
            ".oM3333yY3333Mo.",
            ".oM2222oo2222Mo.",
            ".oM2222222222Mo.",
            ".oM2222222222Mo.",
            ".oM1111111111Mo.",
            ".oM1111111111Mo.",
            ".ommmmmmmmmmmmo.",
            ".oooooooooooooo.");
    }

    private static PixelImage Crate()
    {
        var img = Paint(Cell, Cell, (x, y) =>
        {
            if (x < 2 || x > 13 || y < 4) return null;
            int lx = x - 2, ly = y - 4; // 12×12
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

    private static PixelImage Barrel()
    {
        var img = Paint(Cell, Cell, (x, y) =>
        {
            if (y < 3) return null;
            double half = 4.5 + 1.2 * Math.Sin(Math.PI * (y - 3) / 12.5);
            double dx = x + 0.5 - 8;
            if (Math.Abs(dx) > half) return null;
            double t = (dx + half) / (2 * half);
            if (y is 5 or 13) return t < 0.3 ? Cool[5] : t < 0.7 ? Cool[4] : Cool[3];
            if (y == 3) return Brown[5];
            if ((int)(t * 5) is 1 or 3 && Math.Abs(t * 5 - Math.Round(t * 5)) < 0.12) return Brown[2];
            return t < 0.25 ? Brown[5] : t < 0.65 ? Brown[4] : Brown[3];
        });
        Outline(img, Ink);
        return img;
    }

    private static PixelImage Sign()
    {
        var p = new Dictionary<char, Color32> { ['1'] = Brown[2], ['2'] = Brown[3], ['3'] = Brown[4], ['4'] = Brown[5], ['t'] = Brown[1] };
        var img = Art(p,
            "................",
            "................",
            "................",
            "................",
            "..444444444443..",
            "..3tttt3ttt332..",
            "..333333333332..",
            "..3ttt3tttt332..",
            "..222222222221..",
            "......3221......",
            ".......32.......",
            ".......32.......",
            ".......32.......",
            ".......21.......",
            ".......21.......",
            ".......21.......");
        Outline(img, Ink);
        return img;
    }

    /// <summary>나무 문(16×32, 차지 칸 1×2): 둥근 윗머리, 세로 널, 쇠 경첩 둘, 금빛 손잡이.</summary>
    private static PixelImage Door()
    {
        var img = Paint(Cell, 32, (x, y) =>
        {
            if (x < 2 || x > 13) return null;
            double dx = x + 0.5 - 8;
            if (y < 6 && dx * dx + (y - 6) * (y - 6) > 36.5) return null;
            if (x is 3 or 4 && y is 8 or 9 or 24 or 25) return y % 2 == 0 ? Cool[5] : Cool[4];
            if (x == 11 && y is 17 or 18) return y == 17 ? Fire[6] : Fire[3];
            int px = (x - 2) % 4;
            return px == 3 ? Brown[2] : px == 0 ? Brown[5] : Brown[4];
        });
        Outline(img, Ink);
        return img;
    }

    private static PixelImage Workbench()
    {
        var img = Paint(32, Cell, (x, y) =>
        {
            if (y is >= 6 and <= 8 && x is >= 1 and <= 30) return Brown[y == 6 ? 5 : y == 7 ? 4 : 3];
            if (y >= 9 && (x is >= 3 and <= 5 || x is >= 26 and <= 28)) return Brown[x is 3 or 26 ? 4 : 3];
            if (y == 12 && x is > 5 and < 26) return Brown[3];
            return null;
        });
        // 위에 놓인 망치와 톱
        foreach (var (x, y, c) in new[] { (9, 4, Cool[5]), (10, 4, Cool[5]), (11, 4, Cool[4]), (10, 3, Cool[5]), (10, 5, Cool[4]), (12, 4, Brown[4]), (13, 4, Brown[4]), (14, 4, Brown[3]) })
            img.Set(x, y, c);
        for (int x = 19; x <= 25; x++) img.Set(x, 5, x < 22 ? Cool[6] : Cool[5]);
        img.Set(18, 5, Brown[4]); img.Set(17, 5, Brown[4]);
        Outline(img, Ink);
        return img;
    }

    // ───────────── 만들기 ─────────────

    /// <summary>타일셋을 차례로 쌓는 도우미(Id는 1부터).</summary>
    private sealed class Builder(string name)
    {
        private readonly List<TileFloor> _floors = [];
        private readonly List<TileDef> _tiles = [];
        private readonly List<AutoTileSet> _auto = [];

        public int Floor(string floorName)
        {
            int id = _floors.Count + 1;
            _floors.Add(new TileFloor(id, floorName));
            return id;
        }

        public int Tile(PixelImage image, int floor, int px, int py, int w = 1, int h = 1, Point? offset = null)
        {
            int id = _tiles.Count + 1;
            _tiles.Add(new TileDef
            {
                Id = id, Image = image, Floor = floor, PaletteX = px, PaletteY = py, Width = w, Height = h,
                DrawOffset = offset ?? TileDef.BottomAligned(image.Height, h, Cell),
            });
            return id;
        }

        /// <summary>움직이는 타일(그림 여러 장, 한 장 <paramref name="frameMs"/>).</summary>
        public int Animated(PixelImage[] frames, int frameMs, int floor, int px, int py)
        {
            int id = Tile(frames[0], floor, px, py);
            _tiles[^1] = _tiles[^1].WithFrames(frames, frameMs);
            return id;
        }

        /// <summary>자동 타일 세트(무리 <paramref name="group"/>, 빈 문자열 = 자기 세트만; "뭐라도 있음"이 볼 층 <paramref name="filledFloors"/>, 비면 모든 층).</summary>
        public void Set(string setName, int floor, string group, ImmutableArray<AutoTileRule> rules, ImmutableArray<int> filledFloors = default)
            => _auto.Add(new AutoTileSet
            {
                Id = _auto.Count + 1, Name = setName, Floor = floor, Group = group, Rules = rules,
                FilledFloors = filledFloors.IsDefault ? [] : filledFloors,
            });

        public Tileset Build() => new()
        {
            Key = Tileset.NewKey(),
            Name = name,
            CellWidth = Cell,
            CellHeight = Cell,
            Floors = [.. _floors],
            Tiles = [.. _tiles],
            AutoTiles = [.. _auto],
        };
    }
}
