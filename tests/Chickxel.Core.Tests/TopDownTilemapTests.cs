using System.Drawing;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.Core.Tests;

/// <summary>위치로 그리는 타일셋(탑뷰): 그리기 묶음·기준선 순서, 내려온 그림, 그림 기준 판정, 파일, "무리 있음" 자동 타일.</summary>
public class TopDownTilemapTests
{
    private static readonly Color32 Floor = new(90, 90, 90), Water = new(0, 0, 255), Bank = new(0, 200, 0), Chest = new(255, 0, 0), Wall = new(255, 255, 0);

    private static PixelImage Solid(int w, int h, Color32 c)
    {
        var img = new PixelImage(w, h);
        img.Clear(c);
        return img;
    }

    /// <summary>
    /// 4×4 칸, 층 1 바닥 · 2 물건 · 3 벽(물건보다 위 층). 1 = 바닥, 2 = 물, 3 = 물가 절벽(바닥 묶음, 아래 칸으로 4px 내려옴, 기준선 = 그림 아래),
    /// 4 = 상자(세운 것, 위 칸으로 4px 솟음), 5 = 남쪽 벽 윗면(세운 것, 위 칸으로 4px 솟음). 솟은 것의 기준선은 차지 칸 아래.
    /// </summary>
    private static Tileset Sample(TileSortMode mode) => Tileset.Create("탑뷰", 4, 4) with
    {
        SortMode = mode,
        Floors = [new TileFloor(1, "바닥"), new TileFloor(2, "물건"), new TileFloor(3, "벽")],
        Tiles =
        [
            new TileDef { Id = 1, Image = Solid(4, 4, Floor), Floor = 1 },
            new TileDef { Id = 2, Image = Solid(4, 4, Water), Floor = 1 },
            new TileDef { Id = 3, Image = Solid(4, 8, Bank), Floor = 1, SortY = 8 },
            new TileDef { Id = 4, Image = Solid(4, 8, Chest), Floor = 2, DrawOffset = new Point(0, -4), DrawClass = TileDrawClass.Sorted },
            new TileDef { Id = 5, Image = Solid(4, 8, Wall), Floor = 3, DrawOffset = new Point(0, -4), DrawClass = TileDrawClass.Sorted },
        ],
    };

    [Fact]
    public void SortedTiles_MixAcrossFloors_ByBaseLine()
    {
        var ts = Sample(TileSortMode.ByPosition);
        // 벽 윗면 바로 북쪽의 상자는 벽 뒤, 남쪽의 상자는 벽 앞(솟은 상자가 벽을 덮음) — 벽이 위 층이어도
        var map = TileMap.Empty
            .Place(ts, 3, new Point(0, 2), new TileCell(5))
            .Place(ts, 2, new Point(0, 1), new TileCell(4))
            .Place(ts, 3, new Point(1, 2), new TileCell(5))
            .Place(ts, 2, new Point(1, 3), new TileCell(4));
        var img = TileMapRenderer.RenderArea(ts, map, Point.Empty, new Rectangle(0, 0, 8, 16));
        Assert.Equal(Wall, img[1, 5]);
        Assert.Equal(Chest, img[5, 9]);
        // 층 차례로 그리면 벽 층이 늘 위
        var byFloor = TileMapRenderer.RenderArea(Sample(TileSortMode.ByFloor), map, Point.Empty, new Rectangle(0, 0, 8, 16));
        Assert.Equal(Wall, byFloor[5, 9]);
    }

    [Fact]
    public void GroundTileHangingDown_IsNotCoveredByTheCellBelow()
    {
        // 물가 절벽(칸 (0,0))이 아래 물 칸으로 내려온다: 기준선이 그림 아래라 물보다 나중에 그려짐
        var map = TileMap.Empty
            .Place(Sample(TileSortMode.ByPosition), 1, new Point(0, 0), new TileCell(3))
            .Place(Sample(TileSortMode.ByPosition), 1, new Point(0, 1), new TileCell(2));
        var img = TileMapRenderer.RenderArea(Sample(TileSortMode.ByPosition), map, Point.Empty, new Rectangle(0, 0, 4, 8));
        Assert.Equal(Bank, img[1, 6]);
        // 층 차례로 그리면 아래 줄의 물이 덮는다(예전 동작 그대로)
        Assert.Equal(Water, TileMapRenderer.RenderArea(Sample(TileSortMode.ByFloor), map, Point.Empty, new Rectangle(0, 0, 4, 8))[1, 6]);
    }

    [Fact]
    public void Overhead_DrawsAfterSorted_AndGroundBeforeEverything()
    {
        var ts = Sample(TileSortMode.ByPosition);
        ts = ts with { Tiles = [.. ts.Tiles, new TileDef { Id = 6, Image = Solid(4, 4, Water), Floor = 1, DrawClass = TileDrawClass.Overhead }] };
        var map = TileMap.Empty
            .Place(ts, 1, new Point(0, 0), new TileCell(6))
            .Place(ts, 2, new Point(0, 0), new TileCell(4))
            .Place(ts, 1, new Point(1, 0), new TileCell(1))
            .Place(ts, 2, new Point(1, 0), new TileCell(4));
        var img = TileMapRenderer.RenderArea(ts, map, Point.Empty, new Rectangle(0, 0, 8, 4));
        Assert.Equal(Water, img[1, 1]);
        Assert.Equal(Chest, img[5, 1]);
    }

    [Fact]
    public void SortLine_FollowsVerticalFlip()
    {
        var ts = Sample(TileSortMode.ByPosition);
        Assert.Equal(4 + 8, ts.SortLine(new Point(0, 1), new TileCell(3)));
        Assert.Equal(4 + 4 - 8, ts.SortLine(new Point(0, 1), new TileCell(3, TileTransform.FlipY)));
        // 기준선을 정하지 않으면 차지 칸 아래
        Assert.Equal(8, ts.SortLine(new Point(0, 1), new TileCell(5)));
    }

    [Fact]
    public void HitTest_PicksTheTopImage_EvenOutsideItsFootprint()
    {
        var ts = Sample(TileSortMode.ByPosition);
        var map = TileMap.Empty
            .Place(ts, 1, new Point(0, 1), new TileCell(1))
            .Place(ts, 3, new Point(0, 2), new TileCell(5));
        // 칸 (0,1) 안이지만 벽 윗면이 솟아 덮은 곳
        Assert.Equal(new PlacedTile(3, new Point(0, 2), new TileCell(5)), TileMapRenderer.HitTest(ts, map, Point.Empty, new Point(1, 5)));
        // 바닥만 보면 바닥
        Assert.Equal(1, TileMapRenderer.HitTest(ts, map, Point.Empty, new Point(1, 5), classes: c => c == TileDrawClass.Ground)?.Cell.Tile);
        Assert.Null(TileMapRenderer.HitTest(ts, map, Point.Empty, new Point(1, 1)));
    }

    [Fact]
    public void File_KeepsSortModeDrawClassSortLineAndTargetRules()
    {
        var rule = new AutoTileRule { Target = AutoTileShapes.S, TargetGroup = "물", Tiles = [new(3)] };
        var ts = Sample(TileSortMode.ByPosition) with { AutoTiles = [new AutoTileSet { Id = 1, Name = "땅", Floor = 1, Rules = [rule], ClearFloors = [2, 3] }] };
        var back = TilesetIO.FromBytes(TilesetIO.ToBytes(ts));
        Assert.Equal(TileSortMode.ByPosition, back.SortMode);
        Assert.Equal(8, back.Find(3)!.SortY);
        Assert.Null(back.Find(1)!.SortY);
        Assert.Equal(TileDrawClass.Sorted, back.Find(5)!.DrawClass);
        var r = back.AutoTiles[0].Rules[0];
        Assert.Equal((AutoTileShapes.S, "물"), (r.Target, r.TargetGroup));
        Assert.Equal([2, 3], back.AutoTiles[0].ClearFloors.ToArray());
        Assert.Equal(TileSortMode.ByFloor, TilesetIO.FromBytes(TilesetIO.ToBytes(Sample(TileSortMode.ByFloor))).SortMode);
    }

    [Fact]
    public void TargetCondition_PicksShoreOnlyWhenTheGroupIsNextToIt()
    {
        var ts = Sample(TileSortMode.ByPosition);
        // 땅: 남쪽이 "물" 무리면 물가 절벽(3), 아니면 바닥(1). 물 세트는 무리 없이 이름이 "물"
        var land = new AutoTileSet
        {
            Id = 1, Name = "땅", Floor = 1,
            Rules = [new AutoTileRule { Target = AutoTileShapes.S, TargetGroup = "물", Tiles = [new(3)] }, new AutoTileRule { Tiles = [new(1)] }],
        };
        var water = new AutoTileSet { Id = 2, Name = "물", Floor = 1, Rules = [new AutoTileRule { Tiles = [new(2)] }] };
        ts = ts with { AutoTiles = [land, water] };
        var map = AutoTiler.Paint(ts, TileMap.Empty, land, [new Point(0, 0), new Point(3, 0)]);
        Assert.Equal(1, map.Get(1, new Point(0, 0))?.Tile);
        // 물을 찍으면 그 위 땅만 물가로 바뀐다
        map = AutoTiler.Paint(ts, map, water, [new Point(0, 1)]);
        Assert.Equal(3, map.Get(1, new Point(0, 0))?.Tile);
        Assert.Equal(1, map.Get(1, new Point(3, 0))?.Tile);
        // 무리 이름이 다르면 맞지 않음
        var lava = water with { Id = 3, Name = "용암" };
        ts = ts with { AutoTiles = [land, water, lava] };
        var other = AutoTiler.Paint(ts, AutoTiler.Paint(ts, TileMap.Empty, land, [new Point(0, 0)]), lava, [new Point(0, 1)]);
        Assert.Equal(1, other.Get(1, new Point(0, 0))?.Tile);
    }

    [Fact]
    public void Paint_ClearsTheSetsOtherFloors_ButNotLockedOnes()
    {
        var ts = Sample(TileSortMode.ByPosition);
        // 물(층 1)은 찍으면 층 2·3을 비운다
        var water = new AutoTileSet { Id = 1, Name = "물", Floor = 1, ClearFloors = [2, 3], Rules = [new AutoTileRule { Tiles = [new(2)] }] };
        ts = ts with { AutoTiles = [water] };
        var map = TileMap.Empty
            .Place(ts, 2, new Point(0, 0), new TileCell(4))
            .Place(ts, 3, new Point(1, 0), new TileCell(5))
            .Place(ts, 2, new Point(2, 0), new TileCell(4));
        map = AutoTiler.Paint(ts, map, water, [new Point(0, 0), new Point(1, 0)], f => f != 3);
        Assert.Equal(2, map.Get(1, new Point(0, 0))?.Tile);
        Assert.Null(map.Get(2, new Point(0, 0)));
        Assert.NotNull(map.Get(3, new Point(1, 0)));
        Assert.NotNull(map.Get(2, new Point(2, 0)));
    }

    [Fact]
    public void Combine_DrawsByPosition_WhenAnyTilesetDoes()
    {
        var side = Sample(TileSortMode.ByFloor);
        var top = Sample(TileSortMode.ByPosition) with { Key = "top" };
        Assert.Equal(TileSortMode.ByPosition, TileSources.Combine([side, top]).SortMode);
        Assert.Equal(TileSortMode.ByPosition, TileSources.Combine([top, side]).SortMode);
        Assert.Equal(TileSortMode.ByFloor, TileSources.Combine([side, side with { Key = "other" }]).SortMode);
    }
}
