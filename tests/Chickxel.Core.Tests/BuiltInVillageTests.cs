using System.Drawing;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.Core.Tests;

/// <summary>기본 탑뷰 마을·물가 타일셋: 위치로 그림, 벽은 그린 칸 안에만, 문을 사이에 둔 벽 이음, 땅 재질마다 다른 물가 둑, 물 움직임.</summary>
public class BuiltInVillageTests
{
    private const int Cell = 16;
    private static readonly Tileset Village = BuiltInTilesets.All.Single(e => e.Id == "village").Create();

    private static AutoTileSet SetNamed(string name) => Village.AutoTiles.Single(a => a.Name == name);

    private static IEnumerable<Point> Box(int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                yield return new Point(x, y);
    }

    private static PixelImage Render(TileMap map, long timeMs = 0)
        => TileMapRenderer.RenderArea(Village, map, Point.Empty, new Rectangle(0, 0, 8 * Cell, 8 * Cell), timeMs: timeMs);

    private static int Luma(Color32 c) => c.R * 3 + c.G * 6 + c.B;

    [Fact]
    public void Village_IsRegistered_AndDrawsByPosition()
    {
        Assert.Equal(TileSortMode.ByPosition, Village.SortMode);
        Assert.Equal(TileSortMode.ByFloor, BuiltInTilesets.Sandbox().SortMode);
        Assert.Contains(Village.Tiles, t => t.DrawClass == TileDrawClass.Overhead);
        Assert.Contains(Village.Tiles, t => t.DrawClass == TileDrawClass.Sorted && t.DrawOffset.Y <= -Cell); // 위로 솟은 나무·등불
        // 나무·돌 벽과 문은 같은 무리(서로 이어짐)
        string group = SetNamed(DomainStrings.TileBuiltIn_WoodWall).Group;
        Assert.NotEmpty(group);
        Assert.Equal(group, SetNamed(DomainStrings.TileBuiltIn_StoneWall).Group);
        Assert.Equal(group, SetNamed(DomainStrings.TileBuiltIn_WoodDoor).Group);
    }

    [Fact]
    public void Wall_DrawsOnlyInsideItsPaintedCells()
    {
        var wood = SetNamed(DomainStrings.TileBuiltIn_WoodWall);
        foreach (var t in wood.Rules.SelectMany(r => r.Tiles).Select(c => Village.Find(c.Tile)!))
            Assert.Equal((TileDrawClass.Sorted, Cell, Cell, Point.Empty, (int?)null), (t.DrawClass, t.Image.Width, t.Image.Height, t.DrawOffset, t.SortY));

        // 2줄에 가로 벽(남쪽이 빔 = 앞면): 그림은 2줄 안에만, 앞면이 그 줄 아래 끝까지 닿고 3줄에는 아무것도 없다
        var img = Render(AutoTiler.Paint(Village, TileMap.Empty, wood, Box(1, 2, 4, 2)));
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (img[x, y].A != 0)
                {
                    Assert.InRange(y, 2 * Cell, 3 * Cell - 1);
                    Assert.InRange(x, Cell, 5 * Cell - 1);
                }
        Assert.NotEqual(0, img[2 * Cell + 8, 3 * Cell - 1].A);
        Assert.Equal(0, img[2 * Cell + 8, 3 * Cell].A);
    }

    [Fact]
    public void Door_KeepsTheWallLineJoined()
    {
        var wood = SetNamed(DomainStrings.TileBuiltIn_WoodWall);
        var stone = SetNamed(DomainStrings.TileBuiltIn_StoneWall);
        var door = SetNamed(DomainStrings.TileBuiltIn_WoodDoor);
        var map = AutoTiler.Paint(Village, TileMap.Empty, wood, Box(0, 2, 1, 2));
        map = AutoTiler.Paint(Village, map, stone, Box(3, 2, 4, 2));
        map = AutoTiler.Paint(Village, map, door, [new Point(2, 2)]);
        byte Same(AutoTileSet set, int x) => set.Rules[AutoTiler.RuleAt(Village, map, set, new Point(x, 2))!.Value.Rule].Same;

        // 문 양옆 벽은 동서로 이어진 모양, 문은 동서 벽 사이의 모양
        Assert.Equal(AutoTileShapes.E | AutoTileShapes.W, Same(wood, 1));
        Assert.Equal(AutoTileShapes.E | AutoTileShapes.W, Same(stone, 3));
        Assert.Equal(AutoTileShapes.E | AutoTileShapes.W, Same(door, 2));
        // 문을 지우면 양옆 벽은 그쪽 끝이 닫힌다
        var open = AutoTiler.Erase(Village, map, door, [new Point(2, 2)]);
        Assert.Equal(AutoTileShapes.W, wood.Rules[AutoTiler.RuleAt(Village, open, wood, new Point(1, 2))!.Value.Rule].Same);
    }

    [Fact]
    public void LandAboveWater_DrawsItsOwnBankInsideTheWaterCell()
    {
        var water = SetNamed(DomainStrings.TileBuiltIn_Water);
        var waterMap = AutoTiler.Paint(Village, TileMap.Empty, water, Box(0, 1, 5, 4));
        var waterOnly = Render(waterMap);
        PixelImage Above(string land) => Render(AutoTiler.Paint(Village, waterMap, SetNamed(land), Box(0, 0, 5, 0)));
        var grass = Above(DomainStrings.TileBuiltIn_Grass);
        var sand = Above(DomainStrings.TileBuiltIn_Sand);

        // 물 칸(2, 1) 위쪽: 풀 아래는 흙 둑, 모래 아래는 더 밝은 모래 둑 — 둘 다 물과 다름
        var bank = new Point(2 * Cell + 8, Cell + 3);
        Assert.Equal(255, grass[bank.X, bank.Y].A);
        Assert.NotEqual(waterOnly[bank.X, bank.Y], grass[bank.X, bank.Y]);
        Assert.NotEqual(waterOnly[bank.X, bank.Y], sand[bank.X, bank.Y]);
        Assert.NotEqual(grass[bank.X, bank.Y], sand[bank.X, bank.Y]);
        Assert.True(Luma(sand[bank.X, bank.Y]) > Luma(grass[bank.X, bank.Y]));

        // 둑은 물 칸 위쪽에만: 둑 깊이는 땅마다 같고 그 아래 물은 그대로
        int FirstWater(PixelImage img)
        {
            int y = Cell;
            while (img[bank.X, y] != waterOnly[bank.X, y]) y++;
            return y;
        }
        int depth = FirstWater(grass);
        Assert.InRange(depth, Cell + 3, 2 * Cell - 4);
        Assert.Equal(depth, FirstWater(sand));
        for (int y = depth; y < 4 * Cell; y++)
            Assert.Equal(waterOnly[bank.X, y], grass[bank.X, y]);

        // 물을 나중에 찍어도(둘레를 다시 고름) 위 땅이 둑 모양으로 바뀐다
        var landFirst = AutoTiler.Paint(Village, TileMap.Empty, SetNamed(DomainStrings.TileBuiltIn_Grass), Box(0, 0, 5, 0));
        Assert.True(grass.ContentEquals(Render(AutoTiler.Paint(Village, landFirst, water, Box(0, 1, 5, 4)))));
    }

    [Fact]
    public void LandAboveOtherLand_BlendsWithASoftEdge_WithoutBank()
    {
        var water = SetNamed(DomainStrings.TileBuiltIn_Water);
        var dirt = SetNamed(DomainStrings.TileBuiltIn_Dirt);
        var grass = SetNamed(DomainStrings.TileBuiltIn_Grass);
        var waterOnly = Render(AutoTiler.Paint(Village, TileMap.Empty, water, Box(0, 1, 5, 4)));
        var overWater = Render(AutoTiler.Paint(Village, AutoTiler.Paint(Village, TileMap.Empty, water, Box(0, 1, 5, 4)), grass, Box(0, 0, 5, 0)));
        var dirtMap = AutoTiler.Paint(Village, TileMap.Empty, dirt, Box(0, 1, 5, 4));
        var dirtOnly = Render(dirtMap);
        var overDirt = Render(AutoTiler.Paint(Village, dirtMap, grass, Box(0, 0, 5, 0)));

        // 풀 아래 줄마다(1~4칸) 아래 칸으로 내려온 맨 아래 점: 물 위는 둑 깊이, 흙 위는 그보다 얕은 가장자리뿐
        int Reach(PixelImage img, PixelImage under, int x)
        {
            int last = Cell - 1;
            for (int y = Cell; y < 4 * Cell; y++)
                if (img[x, y] != under[x, y]) last = y;
            return last - Cell + 1;
        }
        int bank = Reach(overWater, waterOnly, 2 * Cell + 8);
        int soft = 0;
        for (int x = Cell; x < 5 * Cell; x++)
        {
            Assert.Equal(bank, Reach(overWater, waterOnly, x));
            soft = Math.Max(soft, Reach(overDirt, dirtOnly, x));
        }
        Assert.InRange(soft, 1, bank - 2);
    }

    [Fact]
    public void Water_Moves_AndIsDeterministic()
    {
        var water = SetNamed(DomainStrings.TileBuiltIn_Water);
        var map = AutoTiler.Paint(Village, TileMap.Empty, water, Box(0, 0, 5, 5));
        int frameMs = Village.Find(map.Get(water.Floor, new Point(2, 2))!.Value.Tile)!.FrameMs;
        var first = Render(map);
        Assert.False(first.ContentEquals(Render(map, 4 * frameMs)));
        Assert.True(first.ContentEquals(Render(map)));
        var again = BuiltInTilesets.All.Single(e => e.Id == "village").Create();
        Assert.True(first.ContentEquals(TileMapRenderer.RenderArea(again, map, Point.Empty, new Rectangle(0, 0, 8 * Cell, 8 * Cell))));
    }
}
