using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.Core.Tests;

/// <summary>타일맵: 덮어쓰기·층·큰 타일·뒤집기 회전·자동 타일·그리기·파일·실행취소·레이어로 변환.</summary>
public class TilemapTests
{
    private static readonly Color32 Red = new(255, 0, 0), Green = new(0, 255, 0), Blue = new(0, 0, 255);

    private static PixelImage Solid(int w, int h, Color32 c)
    {
        var img = new PixelImage(w, h);
        img.Clear(c);
        return img;
    }

    /// <summary>4×4 칸, 층 1(바닥)·2(장식): 1 = 빨강 1×1(바닥), 2 = 초록 2×2(바닥), 3 = 파랑 1×1(장식), 4 = 2×1(바닥, 왼쪽 빨강·오른쪽 파랑).</summary>
    private static Tileset Sample()
    {
        var wide = new PixelImage(8, 4);
        wide.FillRect(new Rectangle(0, 0, 4, 4), Red);
        wide.FillRect(new Rectangle(4, 0, 4, 4), Blue);
        return Tileset.Create("t", 4, 4) with
        {
            Floors = [new TileFloor(1, "바닥"), new TileFloor(2, "장식")],
            Tiles =
            [
                new TileDef { Id = 1, Image = Solid(4, 4, Red), Floor = 1 },
                new TileDef { Id = 2, Image = Solid(8, 8, Green), Width = 2, Height = 2, Floor = 1 },
                new TileDef { Id = 3, Image = Solid(4, 4, Blue), Floor = 2 },
                new TileDef { Id = 4, Image = wide, Width = 2, Height = 1, Floor = 1 },
            ],
        };
    }

    [Fact]
    public void Place_SameFloorOverwritesWholeBigTile_OtherFloorStays()
    {
        var ts = Sample();
        var map = TileMap.Empty
            .Place(ts, 1, new Point(0, 0), new TileCell(2))
            .Place(ts, 2, new Point(1, 1), new TileCell(3));
        // 큰 타일의 오른쪽 아래 칸에 같은 층 1×1을 찍으면 큰 타일이 통째로 빠지고, 장식 층은 그대로
        map = map.Place(ts, 1, new Point(1, 1), new TileCell(1));
        Assert.Null(map.Get(1, new Point(0, 0)));
        Assert.Equal(1, map.Get(1, new Point(1, 1))?.Tile);
        Assert.Equal(3, map.Get(2, new Point(1, 1))?.Tile);
        // 큰 타일을 다시 찍으면 그 차지 칸 안의 1×1이 빠진다
        map = map.Place(ts, 1, new Point(0, 0), new TileCell(2));
        Assert.Null(map.Get(1, new Point(1, 1)));
        Assert.Equal(new Point(0, 0), map.Covering(ts, 1, new Point(1, 1))?.At);
    }

    [Fact]
    public void Erase_TopFloorFirst_AndBigTileFromAnyCoveredCell()
    {
        var ts = Sample();
        var map = TileMap.Empty
            .Place(ts, 1, new Point(0, 0), new TileCell(2))
            .Place(ts, 2, new Point(1, 1), new TileCell(3));
        var (afterTop, removed) = map.Erase(ts, new Point(1, 1));
        Assert.Equal(2, removed?.Floor);
        var (afterBig, big) = afterTop.Erase(ts, new Point(1, 1));
        Assert.Equal(new Point(0, 0), big?.At);
        Assert.True(afterBig.IsEmpty);
    }

    [Fact]
    public void Rotate90_SwapsFootprint_AndTurnsImageClockwise()
    {
        var ts = Sample();
        var cell = new TileCell(4, TileTransform.Rotate90);
        Assert.Equal(new Size(1, 2), ts.Footprint(cell));
        var map = TileMap.Empty.Place(ts, 1, new Point(0, 0), cell);
        var img = TileMapRenderer.RenderArea(ts, map, Point.Empty, new Rectangle(0, 0, 4, 8));
        // 왼쪽(빨강)이 위로, 오른쪽(파랑)이 아래로
        Assert.Equal(Red, img[1, 1]);
        Assert.Equal(Blue, img[1, 6]);
        // 회전한 1×2가 아래 칸 1×1과 겹치므로 같은 층에 찍으면 빠진다
        Assert.Null(map.Place(ts, 1, new Point(0, 1), new TileCell(1)).Get(1, new Point(0, 0)));
    }

    [Fact]
    public void Transforms_ComposeLikeImages()
    {
        foreach (var a in TileTransforms.All)
            foreach (var b in TileTransforms.All)
            {
                var img = TestUtil.RandomImage(3, 2, seed: 7);
                var stepwise = b.ApplyToImage(a.ApplyToImage(img));
                var once = a.Then(b).ApplyToImage(img);
                Assert.True(stepwise.ContentEquals(once), $"{a} then {b}");
            }
        Assert.Equal(4, TileTransforms.Reachable(false, false, true).Count);
        Assert.Equal(8, TileTransforms.Reachable(true, false, true).Count);
        Assert.Equal(2, TileTransforms.Reachable(true, false, false).Count);
    }

    [Fact]
    public void Presets_MakeExpectedRuleCounts_AndBlob47CoversEveryNeighborhoodExactly()
    {
        Assert.Equal(4, AutoTileShapes.Count(AutoTilePreset.Row));
        Assert.Equal(16, AutoTileShapes.Count(AutoTilePreset.Edge4));
        Assert.Equal(13, AutoTileShapes.Count(AutoTilePreset.Box13));
        Assert.Equal(47, AutoTileShapes.Count(AutoTilePreset.Blob47));
        // 47규칙에 모두 타일을 넣으면 256가지 이웃 모두 꼭 하나에 그대로 맞는다
        var set = AutoTileSet.FromPreset(1, "", 1, AutoTilePreset.Blob47);
        set = set with { Rules = [.. set.Rules.Select((r, i) => r with { Tiles = [new AutoTileChoice(100 + i)] })] };
        for (int m = 0; m < 256; m++)
        {
            Assert.True(AutoTiler.HasExact(set, (byte)m, 0));
            Assert.Single(set.Rules, r => r.Matches((byte)m, 0));
        }
    }

    /// <summary>가로 줄 세트: 왼쪽 끝(Id 10)·가운데(11)만 그리고 왼쪽 끝 규칙에 좌우 뒤집기 허용 — 오른쪽 끝은 왼쪽 끝을 뒤집어 쓴다.</summary>
    [Fact]
    public void AutoTile_FillsMissingRuleWithFlippedTile_AndRepicksNeighbors()
    {
        var set = AutoTileSet.FromPreset(1, "", 1, AutoTilePreset.Row);
        set = set with { Rules = set.Rules.SetItem(1, set.Rules[1] with { Tiles = [new(10)], AllowFlipX = true }).SetItem(2, set.Rules[2] with { Tiles = [new(11)] }) };
        var ts = Sample() with
        {
            Tiles = [.. Sample().Tiles, new TileDef { Id = 10, Image = Solid(4, 4, Red), Floor = 1 }, new TileDef { Id = 11, Image = Solid(4, 4, Green), Floor = 1 }],
            AutoTiles = [set],
        };
        var map = AutoTiler.Paint(ts, TileMap.Empty, set, [new Point(0, 0), new Point(1, 0), new Point(2, 0)]);
        Assert.Equal(new TileCell(10, TileTransform.None, 1), map.Get(1, new Point(0, 0)));
        Assert.Equal(new TileCell(11, TileTransform.None, 1), map.Get(1, new Point(1, 0)));
        Assert.Equal(new TileCell(10, TileTransform.FlipX, 1), map.Get(1, new Point(2, 0)));
        // 가운데를 지우면 양옆이 다시 골라진다(혼자 규칙은 비었으므로 가장 덜 어긋난 규칙)
        map = AutoTiler.Erase(ts, map, set, [new Point(1, 0)]);
        Assert.Null(map.Get(1, new Point(1, 0)));
        Assert.Equal(1, map.Get(1, new Point(0, 0))?.AutoTile);
        Assert.Equal(1, map.Get(1, new Point(2, 0))?.AutoTile);
    }

    [Fact]
    public void AutoTile_FirstMatchingRuleWins_AndAnyNeighborIsIgnored()
    {
        // 규칙 1: 위가 비어야 함(나머지 상관없음) → 7, 규칙 2: 조건 없음 → 8
        var top = new AutoTileRule { Other = AutoTileShapes.N, Tiles = [new(7)] };
        var rest = new AutoTileRule { Tiles = [new(8)] };
        var set = new AutoTileSet { Id = 1, Floor = 1, Rules = [top, rest] };
        Assert.Equal(7, AutoTiler.Pick(set, AutoTileShapes.S | AutoTileShapes.E, 0, Point.Empty)?.Tile);
        Assert.Equal(8, AutoTiler.Pick(set, AutoTileShapes.N, 0, Point.Empty)?.Tile);
        Assert.Equal(NeighborCondition.Other, top.Condition(0));
        Assert.Equal(NeighborCondition.Same, top.WithCondition(0, NeighborCondition.Same).Condition(0));
        Assert.Equal(0, top.WithCondition(0, NeighborCondition.Any).Other);
    }

    /// <summary>흙(1층)과 진흙(2층)이 같은 무리면 서로 이웃으로 쳐서, 진흙을 찍으면 흙의 모양도 바뀐다. 무리가 다르면 이어지지 않는다.</summary>
    [Fact]
    public void AutoTile_SetsInSameGroupJoinAcrossSetsAndFloors()
    {
        static AutoTileSet Row(int id, int floor, string group, int alone, int left)
        {
            var s = AutoTileSet.FromPreset(id, "", floor, AutoTilePreset.Row);
            return s with { Group = group, Rules = s.Rules.SetItem(0, s.Rules[0] with { Tiles = [new(alone)] }).SetItem(1, s.Rules[1] with { Tiles = [new(left)] }) };
        }
        var dirt = Row(1, 1, "땅", 20, 21);
        var mud = Row(2, 2, "땅", 30, 31);
        var ts = Sample() with { AutoTiles = [dirt, mud] };
        var map = AutoTiler.Paint(ts, TileMap.Empty, dirt, [new Point(0, 0)]);
        Assert.Equal(20, map.Get(1, new Point(0, 0))?.Tile);
        map = AutoTiler.Paint(ts, map, mud, [new Point(1, 0)]);
        Assert.Equal(21, map.Get(1, new Point(0, 0))?.Tile);
        // 무리를 나누고 다시 고르면 혼자 모양으로
        ts = ts with { AutoTiles = [dirt with { Group = "" }, mud] };
        map = AutoTiler.RefreshAll(ts, map);
        Assert.Equal(20, map.Get(1, new Point(0, 0))?.Tile);
    }

    [Fact]
    public void AutoTile_RandomChoiceIsStablePerCell_AndFollowsWeights()
    {
        var set = new AutoTileSet { Id = 3, Floor = 1, Rules = [new AutoTileRule { Tiles = [new(1, 1), new(3, 3)] }] };
        var counts = new Dictionary<int, int>();
        for (int x = 0; x < 400; x++)
        {
            var a = AutoTiler.Pick(set, 0, 0, new Point(x, 5))!.Value.Tile;
            Assert.Equal(a, AutoTiler.Pick(set, 0, 0, new Point(x, 5))!.Value.Tile);
            counts[a] = counts.GetValueOrDefault(a) + 1;
        }
        Assert.InRange(counts[3], 240, 360);
    }

    [Fact]
    public void AnimatedTile_PicksFrameByTime_AndRoundTripsThroughFile()
    {
        var frames = new[] { Solid(2, 2, Red), Solid(2, 2, Green), Solid(2, 2, Blue) };
        var ts = Sample();
        ts = ts with { Tiles = ts.Tiles.SetItem(0, ts.Tiles[0].WithFrames(frames, 100)) };
        var def = ts.Find(1)!;
        Assert.Equal((0, 1, 2, 0), (def.FrameAt(0), def.FrameAt(150), def.FrameAt(299), def.FrameAt(300)));
        Assert.True(ts.HasAnimation);
        Assert.Equal(ts.AnimationPhase(0), ts.AnimationPhase(99));
        Assert.NotEqual(ts.AnimationPhase(0), ts.AnimationPhase(100));
        Assert.Equal(Green, ts.Placement(Point.Empty, new TileCell(1), 120)!.Value.Image[0, 0]);
        Assert.Throws<ArgumentException>(() => def.WithFrames([Solid(2, 2, Red), Solid(3, 2, Red)], 100));
        var back = TilesetIO.FromBytes(TilesetIO.ToBytes(ts)).Find(1)!;
        Assert.Equal((3, 100), (back.FrameCount, back.FrameMs));
        Assert.True(back.FrameImage(2).ContentEquals(frames[2]));
        // 프로젝트: 프레임 시각 + 화면 시계로 그림이 바뀐다
        var (p, layer) = NewProject(ts);
        p.SetTileMap(layer, TileMap.Empty.Place(ts, 1, Point.Empty, new TileCell(1)), "t");
        var before = p.Flatten(0)[0, 0];
        p.TileClockMs = 100;
        Assert.NotEqual(before, p.Flatten(0)[0, 0]);
    }

    [Fact]
    public void TilesetFile_RoundTrips()
    {
        using var tmp = new TempDir();
        var path = tmp.File("a" + Tileset.FileExtension);
        var rule = new AutoTileRule { Same = AutoTileShapes.N, Other = AutoTileShapes.S, AllowFlipY = true, AllowRotate = true, Tiles = [new AutoTileChoice(3, 2)] };
        var set = new AutoTileSet { Id = 5, Name = "길", Floor = 2, Group = "땅", Rules = [new AutoTileRule(), rule] };
        var ts = Sample() with { AutoTiles = [set] };
        ts = ts with { Tiles = ts.Tiles.SetItem(1, ts.Tiles[1] with { DrawOffset = new Point(0, -4), PaletteX = 2, PaletteY = 1, Name = "나무" }) };
        TilesetIO.Save(ts, path);
        var back = TilesetIO.Load(path);
        Assert.Equal(ts.Key, back.Key);
        Assert.Equal(path, back.SourcePath);
        Assert.Equal(ts.Floors.ToArray(), back.Floors.ToArray());
        Assert.Equal(ts.Tiles.Length, back.Tiles.Length);
        var tree = back.Find(2)!;
        Assert.Equal((2, 2, new Point(0, -4), 2, 1, "나무"), (tree.Width, tree.Height, tree.DrawOffset, tree.PaletteX, tree.PaletteY, tree.Name));
        Assert.True(tree.Image.ContentEquals(ts.Find(2)!.Image));
        var auto = back.AutoTiles.Single();
        Assert.Equal((2, "땅", 2), (auto.Floor, auto.Group, auto.Rules.Length));
        var r = auto.Rules[1];
        Assert.Equal((rule.Same, rule.Other, false, true, true), (r.Same, r.Other, r.AllowFlipX, r.AllowFlipY, r.AllowRotate));
        Assert.Equal([new AutoTileChoice(3, 2)], r.Tiles.ToArray());
        // 같은 타일셋은 같은 바이트(프로젝트 파일 비교·기록 조각)
        Assert.Equal(TilesetIO.ToBytes(ts), TilesetIO.ToBytes(ts));
    }

    private static (SpriteProject Project, Layer Layer) NewProject(Tileset ts)
    {
        var p = new SpriteProject(8, 8);
        var layer = p.AddTilemapLayer(ts);
        return (p, layer);
    }

    private static void Paint(SpriteProject p, Layer layer, Func<TileMap, TileMap> change)
    {
        p.BeginTileStroke(layer);
        p.SetStrokeTileMap(change(layer.Tiles!.Map));
        Assert.True(p.CommitTileStroke("찍기"));
    }

    [Fact]
    public void TileStroke_IsOneUndoStep_AndRendersIntoComposite()
    {
        var ts = Sample();
        var (p, layer) = NewProject(ts);
        p.BeginTileStroke(layer);
        p.SetStrokeTileMap(layer.Tiles!.Map.Place(ts, 1, new Point(0, 0), new TileCell(1)));
        p.SetStrokeTileMap(layer.Tiles!.Map.Place(ts, 2, new Point(1, 0), new TileCell(3)));
        Assert.True(p.CommitTileStroke("찍기"));
        var frame = p.Flatten(0);
        Assert.Equal(Red, frame[1, 1]);
        Assert.Equal(Blue, frame[5, 1]);
        p.Undo();
        Assert.True(layer.Tiles!.Map.IsEmpty);
        Assert.True(p.IsCelEmpty(p.IndexOf(layer), 0));
        p.Redo();
        Assert.Equal(2, layer.Tiles!.Map.Count);
        Assert.Throws<InvalidOperationException>(() => p.BeginCelStroke(p.IndexOf(layer), 0));
    }

    [Fact]
    public void TilesOutsideCanvas_ShowAsOverflow_AndCanvasResizeMovesGrid()
    {
        var ts = Sample();
        var (p, layer) = NewProject(ts);
        Paint(p, layer, m => m.Place(ts, 1, new Point(-1, 0), new TileCell(1)).Place(ts, 1, new Point(0, 0), new TileCell(3)));
        int li = p.IndexOf(layer);
        var outside = p.GetCelOverflow(li, 0);
        Assert.NotNull(outside);
        Assert.Equal(new Point(-4, 0), outside!.Value.Origin);
        // 왼쪽으로 4px 늘리면 캔버스 밖 타일이 안으로 들어온다
        p.ResizeCanvas(12, 8, Anchor.TopRight);
        Assert.Equal(new Point(4, 0), layer.Tiles!.Origin);
        Assert.Equal(Red, p.Flatten(0)[1, 1]);
        Assert.Null(p.GetCelOverflow(li, 0));
    }

    [Fact]
    public void Save_Load_RoundTrip_KeepsMapsAndWritesTilesetFile()
    {
        using var tmp = new TempDir();
        var tilesetPath = tmp.File("tiles" + Tileset.FileExtension);
        var ts = Sample() with { SourcePath = tilesetPath };
        var (p, layer) = NewProject(ts);
        Paint(p, layer, m => m.Place(ts, 1, new Point(0, 0), new TileCell(4, TileTransform.FlipX)).Place(ts, 2, new Point(-3, 7), new TileCell(3)));
        p.SetLayerProperties(p.IndexOf(layer), opacity: 128);
        var file = tmp.File("a.chickxel");
        ProjectIO.Save(p, file);
        Assert.True(File.Exists(tilesetPath));

        var q = ProjectIO.Load(file);
        var loaded = q.Layers.Single(l => l.IsTilemap);
        Assert.False(loaded.Tiles!.FreePlacement);
        Assert.Equal(layer.Tiles!.Map.All.OrderBy(t => t.At.X).ToList(), loaded.Tiles!.Map.All.OrderBy(t => t.At.X).ToList());
        Assert.Equal(128, loaded.Opacity);
        Assert.True(p.Flatten(0).ContentEquals(q.Flatten(0)));

        // 타일셋 파일을 다른 곳에서 고쳤으면 열 때 파일 내용을 쓴다
        TilesetIO.Save(ts with { Tiles = ts.Tiles.SetItem(3, ts.Tiles[3] with { Image = Solid(8, 4, Green) }) }, tilesetPath);
        var r = ProjectIO.Load(file);
        Assert.Equal(Green.WithAlpha(128), r.Flatten(0)[1, 1]);
        // 바뀐 것이 없으면 저장해도 타일셋 파일을 다시 쓰지 않는다
        Assert.Empty(r.TilesetsToWrite);
    }

    [Fact]
    public void FreePlacement_RequiresOnePixelPrimary_AndRejectsLargerReplacements()
    {
        var (ordinary, ordinaryLayer) = NewProject(Sample());
        Assert.Throws<InvalidOperationException>(() => ordinary.SetTilemapFreePlacement(ordinaryLayer, true));
        Assert.False(ordinaryLayer.Tiles!.FreePlacement);

        var onePixel = Tileset.Create("one", 1, 1);
        var (p, layer) = NewProject(onePixel);
        p.SetTilemapFreePlacement(layer, true);
        Assert.True(layer.Tiles!.FreePlacement);
        int undo = p.History.UndoCount;
        p.SetTilemapFreePlacement(layer, true);
        Assert.Equal(undo, p.History.UndoCount);
        var larger = Tileset.Create("larger", 2, 2) with { Key = onePixel.Key };
        Assert.Throws<ArgumentException>(() => p.EditTileset("resize", larger));
        Assert.Throws<ArgumentException>(() => p.AddTileset(larger));
        Assert.Throws<ArgumentException>(() => p.AddTilemapLayer(larger));
        Assert.Throws<ArgumentException>(() => p.SetLayerTileset(layer, 0, Tileset.Create("other", 2, 2)));
        Assert.Same(onePixel, p.Tilesets[onePixel.Key]);
        Assert.Equal(undo, p.History.UndoCount);

        p.Undo();
        Assert.False(layer.Tiles!.FreePlacement);
        p.Redo();
        Assert.True(layer.Tiles!.FreePlacement);
        p.SetTilemapFreePlacement(layer, false);
        p.EditTileset("resize", larger);
        Assert.Same(larger, p.Tilesets[onePixel.Key]);

        var (otherProject, otherLayer) = NewProject(onePixel);
        var second = Tileset.Create("second", 1, 1);
        Assert.True(otherProject.AddLayerTileset(otherLayer, second));
        otherProject.SetTilemapFreePlacement(otherLayer, true);
        Assert.Throws<ArgumentException>(() => otherProject.EditTileset("resize secondary",
            Tileset.Create("second larger", 2, 2) with { Key = second.Key }));
        Assert.True(otherProject.RemoveLayerTileset(otherLayer, 0));
        Assert.True(otherLayer.Tiles!.FreePlacement);
    }

    [Fact]
    public void SixteenPixelGridAndFreeObjectFloor_RenderTogether()
    {
        var ts = Tileset.Create("sixteen", 16, 16) with
        {
            Floors = [new TileFloor(1, "terrain"), new TileFloor(2, "objects")],
            Tiles = [new TileDef { Id = 1, Floor = 1, Image = Solid(16, 16, Red) },
                new TileDef { Id = 2, Floor = 2, Image = Solid(3, 3, Blue) }],
        };
        var p = new SpriteProject(32, 32);
        var layer = p.AddTilemapLayer(ts);
        p.SetTileFloorFreePlacement(layer, 2, true);
        p.SetTileMap(layer, TileMap.Empty.With(1, new Point(1, 1), new TileCell(1))
            .With(2, new Point(18, 19), new TileCell(2)), "mixed");
        Assert.Equal(Red, p.Flatten(0)[16, 16]);
        Assert.Equal(Blue, p.Flatten(0)[18, 19]);
        Assert.Equal(Red, p.Flatten(0)[22, 22]);
        Assert.Equal(2, TileMapRenderer.HitTest(ts, layer.Tiles!.Map, Point.Empty, new Point(18, 19),
            freeFloor: layer.Tiles.IsFreeFloor)?.Floor);
    }

    [Fact]
    public void MixedGridAndFreeFloors_RenderSortHitAndBoundsInTheirOwnCoordinates()
    {
        var ts = Sample() with
        {
            SortMode = TileSortMode.ByPosition,
            Tiles = [Sample().Tiles[0] with { DrawClass = TileDrawClass.Sorted },
                Sample().Tiles[1] with { DrawClass = TileDrawClass.Sorted },
                Sample().Tiles[2] with { DrawClass = TileDrawClass.Sorted }],
        };
        var (p, layer) = NewProject(ts);
        p.SetTileFloorFreePlacement(layer, 2, true);
        var map = TileMap.Empty.With(1, new Point(1, 1), new TileCell(1))
            .With(2, new Point(5, 5), new TileCell(3));
        p.SetTileMap(layer, map, "mixed");
        Assert.False(layer.Tiles!.IsFreeFloor(1));
        Assert.True(layer.Tiles.IsFreeFloor(2));
        Assert.Equal(new Point(5, 5), ts.PlacementAt(2, new Point(5, 5), new TileCell(3), free: true)!.Value.Rect.Location);
        Assert.Equal(Blue, p.Flatten(0)[5, 5]);
        Assert.Equal(Red, p.Flatten(0)[4, 4]);
        Assert.Equal(new Rectangle(4, 4, 5, 5), p.TilemapBounds(layer));
        Assert.Equal(2, TileMapRenderer.HitTest(ts, map, Point.Empty, new Point(5, 5), freeFloor: layer.Tiles.IsFreeFloor)?.Floor);
        // Two free instances can overlap within the same floor; pixel baselines decide depth.
        map = map.With(2, new Point(6, 5), new TileCell(2));
        p.SetTileMap(layer, map, "overlap");
        Assert.Equal(2, map.Floor(2).Count);
        Assert.Equal(Green, p.Flatten(0)[6, 5]);
        Assert.Equal(new Point(6, 5), TileMapRenderer.HitTest(ts, map, Point.Empty, new Point(6, 5),
            freeFloor: layer.Tiles.IsFreeFloor)?.At);
        map = map.With(2, new Point(6, 5), null).With(2, new Point(5, 5), null).With(2, new Point(5, 3), new TileCell(3));
        p.SetTileMap(layer, map, "move");
        Assert.Equal(Red, p.Flatten(0)[5, 5]);
        Assert.Equal(1, TileMapRenderer.HitTest(ts, map, Point.Empty, new Point(5, 5), freeFloor: layer.Tiles.IsFreeFloor)?.Floor);
    }

    [Fact]
    public void MixedFloors_DisablingNonemptyFreeFloorSnapsAnchors_AndHistoryAndFilePreserveAnchors()
    {
        using var tmp = new TempDir();
        var ts = Sample();
        var (p, layer) = NewProject(ts);
        p.SetTileFloorFreePlacement(layer, 2, true);
        var map = TileMap.Empty.With(1, new Point(1, 1), new TileCell(1))
            .With(2, new Point(5, 5), new TileCell(3));
        p.SetTileMap(layer, map, "mixed");
        using var file = new MemoryStream();
        ProjectIO.Save(p, file);
        // 자유 층을 모르는 옛 판이 열지 못하게 자유 층 파일만 새 형식 판으로 쓴다.
        using (var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true))
        using (var json = zip.GetEntry(ProjectIO.ProjectEntryName)!.Open())
            Assert.Equal(ProjectIO.FreeFloorVersion, JsonNode.Parse(json)!["version"]!.GetValue<int>());
        var dir = tmp.File("history");
        UndoHistoryIO.Write(UndoHistoryIO.Capture(p, 100)!, dir);
        file.Position = 0;
        var q = ProjectIO.Load(file);
        var loaded = q.Layers.Single(l => l.IsTilemap);
        Assert.Equal([2], loaded.Tiles!.FreeFloors.ToArray());
        Assert.Equal(map.All.OrderBy(t => t.Floor).ToArray(), loaded.Tiles.Map.All.OrderBy(t => t.Floor).ToArray());
        Assert.Equal(Blue, q.Flatten(0)[5, 5]);
        Assert.Equal(p.History.UndoCount, UndoHistoryIO.Read(q, dir));
        q.Undo();
        Assert.Empty(loaded.Tiles!.Map.All);
        Assert.True(loaded.Tiles.IsFreeFloor(2));
        q.Undo();
        Assert.False(loaded.Tiles!.IsFreeFloor(2));
        q.Redo();
        q.Redo();
        Assert.Equal(Blue, q.Flatten(0)[5, 5]);
        q.SetTileFloorFreePlacement(loaded, 2, false);
        Assert.False(loaded.Tiles!.IsFreeFloor(2));
        Assert.Equal(new TileCell(3), loaded.Tiles.Map.Get(2, new Point(1, 1)));
        q.Undo();
        Assert.True(loaded.Tiles!.IsFreeFloor(2));
        Assert.Equal(new TileCell(3), loaded.Tiles.Map.Get(2, new Point(5, 5)));
    }

    [Fact]
    public void DisablingFreeFloor_SnapsToNearestCellAndRemovesOverlappingFootprints()
    {
        var ts = Sample();
        var (project, layer) = NewProject(ts);
        project.SetTileFloorFreePlacement(layer, 1, true);
        var map = TileMap.Empty.With(1, new Point(4, 4), new TileCell(2))
            .With(1, new Point(7, 4), new TileCell(1))
            .With(1, new Point(5, 5), new TileCell(1))
            .With(1, new Point(3, 0), new TileCell(1, TileTransform.FlipX))
            .With(1, new Point(4, 0), new TileCell(1))
            .With(1, new Point(-2, -2), new TileCell(1))
            .With(2, new Point(1, 1), new TileCell(3));
        project.SetTileMap(layer, map, "free objects");
        int before = project.History.UndoCount;

        project.SetTileFloorFreePlacement(layer, 1, false);

        Assert.Equal(before + 1, project.History.UndoCount);
        Assert.False(layer.Tiles!.IsFreeFloor(1));
        Assert.Equal(3, layer.Tiles.Map.Floor(1).Count);
        Assert.Equal(new TileCell(2), layer.Tiles.Map.Get(1, new Point(1, 1)));
        Assert.Null(layer.Tiles.Map.Get(1, new Point(2, 1)));
        Assert.Equal(new TileCell(1), layer.Tiles.Map.Get(1, new Point(1, 0)));
        Assert.Equal(new TileCell(1), layer.Tiles.Map.Get(1, Point.Empty));
        Assert.Equal(new TileCell(3), layer.Tiles.Map.Get(2, new Point(1, 1)));
        project.Undo();
        Assert.True(layer.Tiles!.IsFreeFloor(1));
        Assert.Equal(map.All.OrderBy(p => p.Floor).ThenBy(p => p.At.Y).ThenBy(p => p.At.X).ToArray(),
            layer.Tiles.Map.All.OrderBy(p => p.Floor).ThenBy(p => p.At.Y).ThenBy(p => p.At.X).ToArray());
        project.Redo();
        Assert.Equal(3, layer.Tiles!.Map.Floor(1).Count);
    }

    [Fact]
    public void EnablingFreeFloor_MigratesGridAnchorsInOneUndoStep()
    {
        var ts = Sample();
        var (p, layer) = NewProject(ts);
        var map = TileMap.Empty.With(1, new Point(0, 0), new TileCell(1))
            .With(2, new Point(1, 1), new TileCell(3, TileTransform.FlipX));
        p.SetTileMap(layer, map, "grid");
        int before = p.History.UndoCount;
        p.SetTileFloorFreePlacement(layer, 2, true);
        Assert.Equal(before + 1, p.History.UndoCount);
        Assert.True(layer.Tiles!.IsFreeFloor(2));
        Assert.Null(layer.Tiles.Map.Get(2, new Point(1, 1)));
        Assert.Equal(new TileCell(3, TileTransform.FlipX), layer.Tiles.Map.Get(2, new Point(4, 4)));
        Assert.Equal(new TileCell(1), layer.Tiles.Map.Get(1, Point.Empty));
        Assert.Equal(Blue, p.Flatten(0)[4, 4]);
        p.Undo();
        Assert.False(layer.Tiles!.IsFreeFloor(2));
        Assert.Equal(map.All.OrderBy(t => t.Floor).ToArray(), layer.Tiles.Map.All.OrderBy(t => t.Floor).ToArray());
        p.Redo();
        Assert.Equal(new TileCell(3, TileTransform.FlipX), layer.Tiles!.Map.Get(2, new Point(4, 4)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void EnablingFreeFloor_DetachesPlacedAutoTilesWithoutChangingArt(int cellSize)
    {
        var set = AutoTileSet.FromPreset(9, "", 1, AutoTilePreset.Row);
        set = set with { Rules = [.. set.Rules.Select(rule => rule with { Tiles = [new AutoTileChoice(1)] })] };
        var ts = Tileset.Create("water", cellSize, cellSize) with
        {
            Floors = [new TileFloor(1, "water"), new TileFloor(2, "objects")],
            Tiles = [new TileDef { Id = 1, Floor = 1, Image = Solid(cellSize, cellSize, Red) },
                new TileDef { Id = 2, Floor = 2, Image = Solid(cellSize, cellSize, Blue) }],
            AutoTiles = [set],
        };
        var (project, layer) = NewProject(ts);
        var map = AutoTiler.Paint(ts, TileMap.Empty.With(2, new Point(0, 1), new TileCell(2)),
            set, [new Point(0, 0), new Point(1, 0)]);
        project.SetTileMap(layer, map, "water");
        var before = project.Flatten(0);
        int undoCount = project.History.UndoCount;

        project.SetTileFloorFreePlacement(layer, 1, true);

        Assert.Equal(undoCount + 1, project.History.UndoCount);
        Assert.True(layer.Tiles!.IsFreeFloor(1));
        Assert.Equal(new TileCell(1), layer.Tiles.Map.Get(1, Point.Empty));
        Assert.Equal(new TileCell(1), layer.Tiles.Map.Get(1, new Point(cellSize, 0)));
        Assert.Equal(new TileCell(2), layer.Tiles.Map.Get(2, new Point(0, 1)));
        Assert.True(before.ContentEquals(project.Flatten(0)));
        project.Undo();
        Assert.False(layer.Tiles!.IsFreeFloor(1));
        Assert.Equal(map.Get(1, new Point(1, 0)), layer.Tiles.Map.Get(1, new Point(1, 0)));
        project.Redo();
        Assert.True(before.ContentEquals(project.Flatten(0)));

        // 격자로 되돌리면 그 층 세트의 타일이 다시 자동 타일로 이어진다.
        project.SetTileFloorFreePlacement(layer, 1, false);
        Assert.False(layer.Tiles!.IsFreeFloor(1));
        Assert.Equal(map.Get(1, Point.Empty), layer.Tiles.Map.Get(1, Point.Empty));
        Assert.Equal(map.Get(1, new Point(1, 0)), layer.Tiles.Map.Get(1, new Point(1, 0)));
        Assert.True(before.ContentEquals(project.Flatten(0)));
    }

    /// <summary>모서리 격자 세트: 모서리마다 차례 = 북서·북동·남서·남동(덮음 = 1)인 4×4 그림(Id 100 + 차례, 색 = 차례), 반 칸 당겨 놓음.</summary>
    private static (Tileset Tileset, AutoTileSet Set) CornerSample(int rank = 0, int setId = 9, int idBase = 100, string target = "")
    {
        int count = target.Length > 0 ? 81 : 16;
        var ts = Sample();
        var set = new AutoTileSet
        {
            Id = setId, Name = "corner" + setId, Floor = 1, CornerRank = rank, CornerTarget = target,
            Rules = [new AutoTileRule { Tiles = [new AutoTileChoice(1)] }],
            CornerTiles = [.. Enumerable.Range(0, count).Select(i => i == 0 ? 0 : idBase + i)],
        };
        var tiles = Enumerable.Range(1, count - 1).Select(i => new TileDef
        {
            Id = idBase + i, Floor = 1, Image = Solid(4, 4, new Color32((byte)i, (byte)(rank * 50), 7)), DrawOffset = new Point(-2, -2), FloorRank = rank,
        });
        return (ts with { Tiles = [.. ts.Tiles, .. tiles], AutoTiles = [set], SortMode = TileSortMode.ByPosition }, set);
    }

    [Fact]
    public void CornerSet_DrawsCornerPicturesBetweenCells_InsteadOfTheCellTile()
    {
        var (ts, set) = CornerSample();
        var map = AutoTiler.Paint(ts, TileMap.Empty, set, [new Point(1, 1)]);
        var img = TileMapRenderer.RenderArea(ts, map, Point.Empty, new Rectangle(0, 0, 12, 12));
        // 칸 (1,1)의 네 모서리: 북서 모서리는 남동 칸만 덮음(차례 1), 북동은 남서(2), 남서는 북동(4), 남동은 북서(8)
        Assert.Equal(new Color32(1, 0, 7), img[4, 4]);
        Assert.Equal(new Color32(2, 0, 7), img[7, 4]);
        Assert.Equal(new Color32(4, 0, 7), img[4, 7]);
        Assert.Equal(new Color32(8, 0, 7), img[7, 7]);
        Assert.Equal(new Color32(1, 0, 7), img[2, 2]); // 그림은 칸 사이에 걸쳐 바깥으로도 그려짐
        Assert.Equal(default, img[1, 1]);
        Assert.DoesNotContain(img.Pixels, p => p == Red); // 칸 타일(빨강)은 그리지 않음
        // 고르기는 그 모서리를 만든 칸을 가리킨다
        Assert.Equal(new PlacedTile(1, new Point(1, 1), map.Get(1, new Point(1, 1))!.Value), TileMapRenderer.HitTest(ts, map, Point.Empty, new Point(2, 2)));
        Assert.Equal(new Rectangle(2, 2, 8, 8), TileMapRenderer.PixelBounds(ts, map, Point.Empty));
    }

    [Fact]
    public void CornerSets_HigherRankCoversLower_AndTargetGroupGetsItsOwnState()
    {
        var (low, water) = CornerSample(rank: 0, setId: 9, idBase: 100);
        var (_, land) = CornerSample(rank: 1, setId: 10, idBase: 200, target: "corner9");
        var landTiles = Enumerable.Range(1, 80).Select(i => new TileDef { Id = 200 + i, Floor = 1, Image = Solid(4, 4, new Color32((byte)i, 50, 7)), DrawOffset = new Point(-2, -2), FloorRank = 1 });
        var ts = low with { Tiles = [.. low.Tiles, .. landTiles], AutoTiles = [water, land] };
        var map = AutoTiler.Paint(ts, TileMap.Empty, water, [new Point(0, 0)]);
        map = AutoTiler.Paint(ts, map, land, [new Point(1, 0)]);
        var img = TileMapRenderer.RenderArea(ts, map, Point.Empty, new Rectangle(0, 0, 12, 8));
        // 모서리 (1,1)(물 북서 · 땅 북동): 땅 차례 = (2·3 + 1)·9 = 63(물 = 무리 상태 2, 땅 = 1)이 물(땅을 덮음으로 쳐 차례 12) 위에 그려짐
        Assert.Equal(new Color32(63, 50, 7), img[4, 4]);
        // 물만 닿는 모서리 (0,1)(남동 칸 없음, 북동이 물): 물 차례 4
        Assert.Equal(new Color32(4, 0, 7), img[0, 4]);
    }

    [Fact]
    public void TilesetFile_KeepsCornerSets_AndMarksTheCornerVersion()
    {
        var (ts, set) = CornerSample(rank: 3, target: "water");
        var bytes = TilesetIO.ToBytes(ts);
        var loaded = TilesetIO.FromBytes(bytes).FindAutoTile(set.Id)!;
        Assert.Equal(set.CornerTiles.ToArray(), loaded.CornerTiles.ToArray());
        Assert.Equal(3, loaded.CornerRank);
        Assert.Equal("water", loaded.CornerTarget);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var json = zip.GetEntry(TilesetIO.JsonEntryName)!.Open();
        Assert.Equal(TilesetIO.CornerVersion, JsonNode.Parse(json)!["version"]!.GetValue<int>());
    }

    [Fact]
    public void TilesetFile_KeepsFloorRank_AndOnlyThenUsesTheNewerVersion()
    {
        var plain = Sample();
        var ranked = plain with { Tiles = [.. plain.Tiles.Select(t => t.Id == 1 ? t with { FloorRank = 2 } : t)] };
        var loaded = TilesetIO.FromBytes(TilesetIO.ToBytes(ranked));
        Assert.Equal(2, loaded.Find(1)!.FloorRank);
        Assert.Equal(0, loaded.Find(2)!.FloorRank);
        Assert.Equal(TilesetIO.FloorRankVersion, TilesetJsonVersion(TilesetIO.ToBytes(ranked)));
        Assert.Equal(TilesetIO.CurrentVersion, TilesetJsonVersion(TilesetIO.ToBytes(plain)));

        static int TilesetJsonVersion(byte[] bytes)
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            using var json = zip.GetEntry(TilesetIO.JsonEntryName)!.Open();
            return JsonNode.Parse(json)!["version"]!.GetValue<int>();
        }
    }

    [Fact]
    public void AutoTiler_GridFloorFilterIgnoresAndPreservesFreeFloorCells()
    {
        var ts = Sample();
        var set = new AutoTileSet
        {
            Id = 9, Floor = 1, FilledFloors = [2], ClearFloors = [2],
            Rules = [new AutoTileRule { Filled = AutoTileShapes.E, Tiles = [new AutoTileChoice(1)] }],
        };
        ts = ts with { AutoTiles = [set] };
        var map = TileMap.Empty.With(2, Point.Empty, new TileCell(3))
            .With(2, new Point(1, 0), new TileCell(3));
        Assert.Equal(AutoTileShapes.E, AutoTiler.FilledMask(ts, map, set, Point.Empty));
        Assert.Equal(0, AutoTiler.FilledMask(ts, map, set, Point.Empty, gridFloor: f => f != 2));
        var painted = AutoTiler.Paint(ts, map, set, [Point.Empty], gridFloor: f => f != 2);
        Assert.Equal(map.Floor(2).ToArray(), painted.Floor(2).ToArray());
        Assert.Equal(new TileCell(1, AutoTile: 9), painted.Get(1, Point.Empty));
        Assert.Throws<InvalidOperationException>(() => AutoTiler.Paint(ts, map, set, [Point.Empty], gridFloor: _ => false));
    }

    [Fact]
    public void LegacyOnePixelFreePlacement_TurningOffOneFloorPreservesOtherFloors()
    {
        var ts = Tileset.Create("legacy", 1, 1) with
        {
            Floors = [new TileFloor(1, "one"), new TileFloor(2, "two")],
            Tiles = [new TileDef { Id = 1, Floor = 1, Image = Solid(2, 2, Red) },
                new TileDef { Id = 2, Floor = 2, Image = Solid(2, 2, Blue) }],
        };
        var (p, layer) = NewProject(ts);
        p.SetTilemapFreePlacement(layer, true);
        p.SetTileMap(layer, TileMap.Empty.With(1, new Point(3, 4), new TileCell(1))
            .With(2, new Point(4, 4), new TileCell(2)), "props");
        p.SetTileFloorFreePlacement(layer, 1, false);
        Assert.False(layer.Tiles!.FreePlacement);
        Assert.False(layer.Tiles.IsFreeFloor(1));
        Assert.True(layer.Tiles.IsFreeFloor(2));
        Assert.Equal(Blue, p.Flatten(0)[4, 4]);
        p.Undo();
        Assert.True(layer.Tiles!.FreePlacement);
        Assert.Equal(Blue, p.Flatten(0)[4, 4]);
    }

    [Fact]
    public void FreePlacement_SaveAndHistoryRoundTrip_PreservesPixelAnchorsAndMode()
    {
        using var tmp = new TempDir();
        var ts = Tileset.Create("props", 1, 1) with
        {
            Tiles = [new TileDef { Id = 1, Image = Solid(2, 3, Red), Floor = 1 }],
        };
        var (p, layer) = NewProject(ts);
        var anchor = new Point(3, 4);
        p.SetTileMap(layer, TileMap.Empty.With(1, anchor, new TileCell(1)), "place");
        p.SetTilemapFreePlacement(layer, true);
        var file = tmp.File("free.chickxel");
        ProjectIO.Save(p, file);
        var dir = tmp.File("history");
        UndoHistoryIO.Write(UndoHistoryIO.Capture(p, 100)!, dir);

        var q = ProjectIO.Load(file);
        var loaded = q.Layers.Single(l => l.IsTilemap);
        Assert.True(loaded.Tiles!.FreePlacement);
        Assert.Equal(new TileCell(1), loaded.Tiles.Map.Get(1, anchor));
        Assert.True(loaded.Tiles.IsFreeFloor(1));
        Assert.Equal(anchor, loaded.Tiles.CellAt(q.TilesetOf(loaded)!, anchor.X, anchor.Y));
        Assert.Equal(p.History.UndoCount, UndoHistoryIO.Read(q, dir));
        q.Undo();
        Assert.False(loaded.Tiles!.FreePlacement);
        Assert.Equal(new TileCell(1), loaded.Tiles.Map.Get(1, anchor));
        q.Redo();
        Assert.True(loaded.Tiles!.FreePlacement);
        Assert.Equal(new TileCell(1), loaded.Tiles.Map.Get(1, anchor));
    }

    [Fact]
    public void EditTileset_RemovingTile_RemovesItFromMaps_InOneUndoStep()
    {
        var ts = Sample();
        var (p, layer) = NewProject(ts);
        Paint(p, layer, m => m.Place(ts, 1, new Point(0, 0), new TileCell(1)).Place(ts, 2, new Point(0, 0), new TileCell(3)));
        p.EditTileset("타일 지우기", ts with { Tiles = ts.Tiles.RemoveAt(0) }, (m, offset) => m.WithoutTile(offset + 1));
        Assert.Equal([3], layer.Tiles!.Map.All.Select(t => t.Cell.Tile));
        p.Undo();
        Assert.Equal(2, layer.Tiles!.Map.Count);
        Assert.NotNull(p.TilesetOf(layer)!.Find(1));
    }

    [Fact]
    public void ConvertToLayers_MakesOneChildPerFloor_UnderEmptyParent_WithSamePixels()
    {
        var ts = Sample();
        var (p, layer) = NewProject(ts);
        p.AddFrame();
        Paint(p, layer, m => m.Place(ts, 1, new Point(0, 0), new TileCell(2)).Place(ts, 2, new Point(1, 1), new TileCell(3)));
        var before = p.Flatten(1);
        var group = p.ConvertTilemapToLayers(p.IndexOf(layer));
        Assert.True(group.IsPart && p.HasChildren(group));
        Assert.Equal(new Rectangle(0, 0, p.Width, p.Height), group.PartRect);
        Assert.Null(p.GetCel(p.IndexOf(group), 0));
        var parts = p.ChildrenOf(group);
        Assert.Equal(["바닥", "장식"], parts.Select(l => l.Name));
        Assert.Equal(new Rectangle(4, 4, 4, 4), parts[1].PartRect);
        Assert.True(before.ContentEquals(p.Flatten(1)));
        Assert.Same(p.GetCel(p.IndexOf(parts[0]), 0), p.GetCel(p.IndexOf(parts[0]), 1));
        p.Undo();
        Assert.Same(layer, p.Layers.Single(l => l.IsTilemap));
    }

    /// <summary>
    /// 움직이는 타일이 있는 맵을 한 프레임 문서에서 레이어로 바꾸면, 주기가 다른 타일(100ms×2장, 150ms×2장)이 모두 한 바퀴 도는 길이(600ms)를
    /// 그림이 바뀌는 간격(50ms)마다 프레임으로 늘리고, 프레임마다 그 시각의 그림이 들어간다(같은 그림은 나눠 씀). 되돌리면 한 단계로 돌아온다.
    /// </summary>
    [Fact]
    public void ConvertToGroup_ExpandsOneFrameDoc_ToFullCycleOfAnimatedTiles()
    {
        var ts = Sample();
        ts = ts with
        {
            Tiles = ts.Tiles
                .SetItem(0, ts.Tiles[0].WithFrames([Solid(4, 4, Red), Solid(4, 4, Green)], 100))
                .SetItem(2, ts.Tiles[2].WithFrames([Solid(4, 4, Blue), Solid(4, 4, Red)], 150)),
        };
        var (p, layer) = NewProject(ts);
        Paint(p, layer, m => m.Place(ts, 1, new Point(0, 0), new TileCell(1)).Place(ts, 2, new Point(1, 1), new TileCell(3)));
        int undoBefore = p.History.UndoCount;
        var group = p.ConvertTilemapToLayers(p.IndexOf(layer));
        Assert.Equal(12, p.Frames.Count);
        Assert.All(p.Frames, f => Assert.Equal(50, f.DurationMs));
        var parts = p.ChildrenOf(group);
        PixelImage Cel(int part, int frame) => p.GetCel(p.IndexOf(parts[part]), frame)!;
        // 바닥(100ms마다 빨강↔초록): 0~1 빨강, 2~3 초록, 4~5 빨강 …; 장식(150ms마다 파랑↔빨강): 0~2 파랑, 3~5 빨강
        Assert.Equal([Red, Red, Green, Green, Red], Enumerable.Range(0, 5).Select(f => Cel(0, f)[0, 0]));
        Assert.Equal([Blue, Blue, Blue, Red, Red, Red, Blue], Enumerable.Range(0, 7).Select(f => Cel(1, f)[0, 0]));
        Assert.Same(Cel(0, 0), Cel(0, 1));
        Assert.Equal(undoBefore + 1, p.History.UndoCount);
        p.Undo();
        Assert.Single(p.Frames);
        Assert.Same(layer, p.Layers.Single(l => l.IsTilemap));
    }

    /// <summary>"뭐라도 있음" 조건: 위에 다른 세트의 벽(보는 층)이 있으면 몸통 규칙, 보지 않는 층의 타일이나 빈 칸이면 수면 규칙. 벽을 손으로 지우면 다시 수면이 된다.</summary>
    [Fact]
    public void AutoTile_FilledCondition_SeesAnyTileOnChosenFloors()
    {
        var body = new AutoTileRule { Filled = AutoTileShapes.N, Tiles = [new(1)] };
        var surface = new AutoTileRule { Tiles = [new(3)] };
        var water = new AutoTileSet { Id = 1, Floor = 2, Rules = [body, surface], FilledFloors = [1] };
        var ts = Sample() with { AutoTiles = [water] };
        var map = AutoTiler.Paint(ts, TileMap.Empty, water, [new Point(0, 1), new Point(0, 2), new Point(1, 1)]);
        Assert.Equal(3, map.Get(2, new Point(0, 1))?.Tile); // 위가 빔 → 수면
        Assert.Equal(1, map.Get(2, new Point(0, 2))?.Tile); // 위가 같은 세트 → 몸통
        // 층 1(보는 층)에 벽 → 몸통. 층 2(보지 않는 층)의 다른 타일은 빈 칸으로 본다.
        map = map.Place(ts, 1, new Point(0, 0), new TileCell(1));
        map = AutoTiler.RefreshAround(ts, map, [new Point(0, 0)]);
        Assert.Equal(1, map.Get(2, new Point(0, 1))?.Tile);
        map = map.Place(ts, 2, new Point(1, 0), new TileCell(3));
        map = AutoTiler.RefreshAround(ts, map, [new Point(1, 0)]);
        Assert.Equal(3, map.Get(2, new Point(1, 1))?.Tile);
        map = AutoTiler.RefreshAround(ts, map.With(1, new Point(0, 0), null), [new Point(0, 0)]);
        Assert.Equal(3, map.Get(2, new Point(0, 1))?.Tile);
        // 파일로 저장해도 조건과 층이 남는다
        var back = TilesetIO.FromBytes(TilesetIO.ToBytes(ts)).AutoTiles.Single();
        Assert.Equal(AutoTileShapes.N, back.Rules[0].Filled);
        Assert.Equal([1], back.FilledFloors.ToArray());
    }

    [Fact]
    public void UndoHistory_RoundTripsTileSteps()
    {
        using var tmp = new TempDir();
        var ts = Sample();
        var (p, layer) = NewProject(ts);
        Paint(p, layer, m => m.Place(ts, 1, new Point(0, 0), new TileCell(1)));
        p.EditTileset("타일셋", ts with { Tiles = ts.Tiles.SetItem(0, ts.Tiles[0] with { Image = Solid(4, 4, Green) }) });
        Paint(p, layer, m => m.Place(ts, 1, new Point(1, 1), new TileCell(2)));
        var file = tmp.File("a.chickxel");
        ProjectIO.Save(p, file);
        var dir = tmp.File("history");
        UndoHistoryIO.Write(UndoHistoryIO.Capture(p, 100)!, dir);

        var q = ProjectIO.Load(file);
        Assert.Equal(p.History.UndoCount, UndoHistoryIO.Read(q, dir));
        var images = new List<PixelImage>();
        while (q.History.CanUndo)
        {
            images.Add(q.Flatten(0));
            q.Undo();
        }
        Assert.Equal(Green, images[1][1, 1]);
        Assert.Equal(Red, images[2][1, 1]);
        Assert.DoesNotContain(q.Layers, l => l.IsTilemap);
    }

    [Fact]
    public void ImportSheet_SkipsEmptyAndDuplicateCells_AndPlacesBelowExistingRows()
    {
        var ts = Sample();
        var sheet = new PixelImage(16, 8);
        sheet.FillRect(new Rectangle(0, 0, 4, 4), Red);   // 빨강 1×1 타일과 같음 → 뺌
        sheet.FillRect(new Rectangle(4, 0, 4, 4), Green); // 새 타일
        sheet.FillRect(new Rectangle(12, 4, 4, 4), Green); // 방금 더한 것과 같음 → 뺌
        var (next, added) = TilesetEdit.ImportSheet(ts, sheet, floor: 2);
        Assert.Equal(1, added);
        var tile = next.Tiles[^1];
        Assert.Equal((1, 1, 2), (tile.PaletteX, tile.PaletteY, tile.Floor));
        Assert.True(tile.Image.ContentEquals(Solid(4, 4, Green)));
    }

    [Fact]
    public void MergeToBigTile_JoinsSlotsIntoOneFootprint()
    {
        var ts = Tileset.Create("t", 4, 4) with
        {
            Tiles = [new TileDef { Id = 1, Image = Solid(4, 4, Red), PaletteX = 0 }, new TileDef { Id = 2, Image = Solid(4, 4, Blue), PaletteX = 1 }],
        };
        var (merged, id) = TilesetEdit.MergeToBigTile(ts, new Rectangle(0, 0, 2, 1))!.Value;
        var big = merged.Find(id)!;
        Assert.Equal((2, 1), (big.Width, big.Height));
        Assert.Equal(Red, big.Image[1, 1]);
        Assert.Equal(Blue, big.Image[6, 1]);
        Assert.Equal(3, merged.Tiles.Length);
    }

    [Fact]
    public void HiddenFloor_IsLeftOutOfComposite_AndKeptOnSave()
    {
        var ts = Sample();
        var (p, layer) = NewProject(ts);
        Paint(p, layer, m => m.Place(ts, 1, new Point(0, 0), new TileCell(1)).Place(ts, 2, new Point(0, 0), new TileCell(3)));
        Assert.Equal(Blue, p.Flatten(0)[1, 1]);
        p.SetTileFloorState(layer, 2, hidden: true);
        Assert.Equal(Red, p.Flatten(0)[1, 1]);
        using var ms = new MemoryStream();
        ProjectIO.Save(p, ms);
        ms.Position = 0;
        var q = ProjectIO.Load(ms);
        Assert.True(q.Layers.Single(l => l.IsTilemap).Tiles!.IsFloorHidden(2));
        Assert.Equal(Red, q.Flatten(0)[1, 1]);
        p.Undo();
        Assert.Equal(Blue, p.Flatten(0)[1, 1]);
    }

    [Fact]
    public void BuiltInTilesets_AreConsistent_AndRepeatable()
    {
        foreach (var entry in BuiltInTilesets.All)
        {
            var ts = entry.Create();
            Assert.Equal(ts.Tiles.Length, ts.Tiles.Select(t => (t.PaletteX, t.PaletteY)).Distinct().Count());
            Assert.All(ts.Tiles, t => Assert.True(ts.FloorOrder(t.Floor) >= 0));
            foreach (var set in ts.AutoTiles)
            {
                Assert.All(set.Rules.SelectMany(r => r.Tiles), c => Assert.NotNull(ts.Find(c.Tile)));
                Assert.Contains(set.Rules, r => r.HasTiles);
            }
            // 같은 코드면 같은 그림(새 이름표만 다름) — 프로젝트에 담은 사본끼리 비교할 수 있게
            var again = entry.Create();
            Assert.NotEqual(ts.Key, again.Key);
            Assert.All(ts.Tiles.Zip(again.Tiles), p => Assert.True(p.First.Image.ContentEquals(p.Second.Image)));
            var back = TilesetIO.FromBytes(TilesetIO.ToBytes(ts));
            Assert.Equal(ts.Tiles.Length, back.Tiles.Length);
            Assert.Equal(ts.AutoTiles.Length, back.AutoTiles.Length);
        }
    }

    /// <summary>둘째 타일셋: 같은 4×4 칸, 층 7(장식)·8(기타)·9(끝) — 이름이 같은 층은 그 층을 같이 쓰고, 아니면 합친 타일셋에 그 층을 더한다.</summary>
    private static Tileset Second() => Tileset.Create("u", 4, 4) with
    {
        Floors = [new TileFloor(7, "장식"), new TileFloor(8, "기타"), new TileFloor(9, "끝")],
        Tiles = [new TileDef { Id = 1, Image = Solid(4, 4, Green), Floor = 7 }, new TileDef { Id = 2, Image = Solid(4, 4, Blue), Floor = 9 }],
        AutoTiles = [new AutoTileSet { Id = 1, Floor = 8, FilledFloors = [7, 9], Rules = [new AutoTileRule { Tiles = [new(1), new(2, 3)] }] }],
    };

    [Fact]
    public void CombineTilesets_OffsetsIdsAndMapsFloorsAndAutoTileRefs()
    {
        var primary = Sample();
        var second = Second();
        Assert.Same(primary, TileSources.Combine([primary]));
        // 칸 크기가 다른 타일셋은 뺀다
        var combined = TileSources.Combine([primary, second, Tileset.Create("큰", 8, 8) with { Tiles = [new TileDef { Id = 1, Image = Solid(8, 8, Red) }] }]);
        // 이름이 같은 층(장식)은 같이 쓰고, 없는 층(기타·끝)은 그 타일셋의 차례대로 같이 쓰는 층 뒤에 더한다 — 첫 타일셋 층으로 뭉치지 않음
        int other = TileSources.Offset(1) + 8, end = TileSources.Offset(1) + 9;
        Assert.Equal([new TileFloor(1, "바닥"), new TileFloor(2, "장식"), new TileFloor(other, "기타"), new TileFloor(end, "끝")], combined.Floors.ToArray());
        Assert.Equal(primary.Key, combined.Key);
        Assert.Equal(primary.Tiles.Length + 2, combined.Tiles.Length);
        Assert.Same(primary.Find(1), combined.Find(1));
        int a = TileSources.Global(1, 1), b = TileSources.Global(1, 2);
        Assert.Equal((1, 1, TileSources.Stride + 1), (TileSources.Slot(a), TileSources.Local(a), a));
        Assert.Equal(2, combined.Find(a)!.Floor); // "장식" 이름이 같음
        Assert.Equal(end, combined.Find(b)!.Floor);
        Assert.Null(combined.Find(TileSources.Global(2, 1)));
        var set = combined.FindAutoTile(TileSources.Global(1, 1))!;
        Assert.Equal(other, set.Floor);
        Assert.Equal([2, end], set.FilledFloors.ToArray());
        Assert.Equal([new AutoTileChoice(a), new AutoTileChoice(b, 3)], set.Rules[0].Tiles.ToArray());
        Assert.Equal(1, TileSources.Slot(new TileCell(0, TileTransform.None, set.Id)));
    }

    private static AutoTileSet GroupRow(int id, int floor, string group, int alone, int left)
    {
        var s = AutoTileSet.FromPreset(id, "", floor, AutoTilePreset.Row);
        return s with { Group = group, Rules = s.Rules.SetItem(0, s.Rules[0] with { Tiles = [new(alone)] }).SetItem(1, s.Rules[1] with { Tiles = [new(left)] }) };
    }

    /// <summary>다른 타일셋의 같은 무리 세트끼리도 이어진다(세트 Id가 같아도 자리로 나뉨).</summary>
    [Fact]
    public void AutoTile_SetsFromTwoTilesetsInSameGroupJoin()
    {
        var primary = Sample() with { AutoTiles = [GroupRow(1, 1, "땅", 20, 21)] };
        var second = Sample() with { AutoTiles = [GroupRow(1, 1, "땅", 30, 31)] };
        var (p, layer) = NewProject(primary);
        Assert.True(p.AddLayerTileset(layer, second));
        var ts = p.TilesetOf(layer)!;
        Assert.Same(ts, p.TilesetOf(layer));
        var dirt = ts.FindAutoTile(1)!;
        var mud = ts.FindAutoTile(TileSources.Global(1, 1))!;
        var map = AutoTiler.Paint(ts, TileMap.Empty, dirt, [new Point(0, 0)]);
        Assert.Equal(20, map.Get(1, new Point(0, 0))?.Tile);
        map = AutoTiler.Paint(ts, map, mud, [new Point(2, 0)]);
        Assert.Equal(TileSources.Global(1, 30), map.Get(1, new Point(2, 0))?.Tile);
        map = AutoTiler.Paint(ts, map, mud, [new Point(1, 0)]);
        Assert.Equal(21, map.Get(1, new Point(0, 0))?.Tile);
        Assert.Equal(mud.Id, map.Get(1, new Point(1, 0))?.AutoTile);
        // 둘째 타일셋의 무리를 나누고 고치면 레이어의 새 합친 타일셋으로 다시 고른다
        p.SetTileMap(layer, map, "t");
        p.EditTileset("세트", second with { AutoTiles = [GroupRow(1, 1, "", 30, 31)] });
        Assert.NotSame(ts, p.TilesetOf(layer));
        Assert.Equal(20, layer.Tiles!.Map.Get(1, new Point(0, 0))?.Tile);
    }

    [Fact]
    public void AddRemoveLayerTileset_ShiftsAndDropsCells_AndIsUndoable()
    {
        var primary = Sample();
        var second = Sample() with { Floors = [new TileFloor(6, "바닥"), new TileFloor(5, "장식")] };
        var third = Second();
        var (p, layer) = NewProject(primary);
        Assert.False(p.RemoveLayerTileset(layer, 0));
        Assert.True(p.AddLayerTileset(layer, second));
        Assert.False(p.AddLayerTileset(layer, second));
        Assert.Throws<ArgumentException>(() => p.AddLayerTileset(layer, Tileset.Create("큰", 8, 8)));
        Assert.True(p.AddLayerTileset(layer, third));
        Assert.Equal([primary.Key, second.Key, third.Key], layer.Tiles!.TilesetKeys.ToArray());
        Assert.Equal([primary, second, third], p.TilesetSources(layer));
        Assert.Equal(3, p.UsedTilesets.Count);
        var ts = p.TilesetOf(layer)!;
        Paint(p, layer, m => m
            .Place(ts, 1, new Point(0, 0), new TileCell(1))
            .Place(ts, 2, new Point(1, 0), new TileCell(TileSources.Global(1, 3)))
            .Place(ts, 2, new Point(2, 0), new TileCell(TileSources.Global(2, 1))));
        var frame = p.Flatten(0);
        Assert.Equal((Red, Blue), (frame[1, 1], frame[5, 1]));
        // 둘째 타일셋을 고치면 그 자리의 Id만 맵에서 빠진다
        int undo = p.History.UndoCount;
        p.EditTileset("타일 지우기", TilesetEdit.RemoveTiles(second, [3]), (m, offset) => m.WithoutTile(offset + 3));
        Assert.Null(layer.Tiles!.Map.Get(2, new Point(1, 0)));
        p.Undo();

        Assert.True(p.RemoveLayerTileset(layer, 1));
        Assert.Equal(undo + 1, p.History.UndoCount);
        Assert.Equal([primary.Key, third.Key], layer.Tiles!.TilesetKeys.ToArray());
        Assert.Equal(
            [new PlacedTile(1, new Point(0, 0), new TileCell(1)), new PlacedTile(2, new Point(2, 0), new TileCell(TileSources.Global(1, 1)))],
            layer.Tiles.Map.All.OrderBy(t => t.At.X));
        Assert.Equal(Green, p.TilesetOf(layer)!.Find(TileSources.Global(1, 1))!.Image[0, 0]);
        p.Undo();
        Assert.Equal(3, layer.Tiles!.TilesetKeys.Length);
        Assert.Equal(TileSources.Global(1, 3), layer.Tiles.Map.Get(2, new Point(1, 0))?.Tile);

        // 첫 타일셋을 빼면 다음 것이 첫 타일셋: 맵의 층과 숨긴 층은 그 타일셋의 같은 이름 층으로(장식 2 → 5)
        p.SetTileFloorState(layer, 2, hidden: true);
        Assert.True(p.RemoveLayerTileset(layer, 0));
        Assert.Equal([second.Key, third.Key], layer.Tiles!.TilesetKeys.ToArray());
        Assert.Equal(
            [new PlacedTile(5, new Point(1, 0), new TileCell(3)), new PlacedTile(5, new Point(2, 0), new TileCell(TileSources.Global(1, 1)))],
            layer.Tiles.Map.All.OrderBy(t => t.At.X));
        Assert.Equal([5], layer.Tiles.HiddenFloors.ToArray());
        p.Undo();
        Assert.Equal(3, layer.Tiles!.TilesetKeys.Length);
    }

    /// <summary>
    /// 기본 샌드박스(층 차례) 레이어에 탑뷰 마을을 더하면 마을의 층(물·풀·장식·벽·물건 …)이 뭉치지 않아 풀 위에 꽃을 찍어도 풀이 남고,
    /// 예전 합치기로 첫 타일셋 층에 뭉쳐 저장한 칸은 읽을 때 제 층으로 옮긴다.
    /// </summary>
    [Fact]
    public void SandboxPlusVillage_KeepsVillageFloorsApart_AndUpgradesOldMaps()
    {
        var sandbox = BuiltInTilesets.Sandbox();
        var village = BuiltInTilesets.TopDownVillage();
        var p = new SpriteProject(64, 64);
        var layer = p.AddTilemapLayer(sandbox);
        Assert.True(p.AddLayerTileset(layer, village));
        var ts = p.TilesetOf(layer)!;
        // 땅(물·모래·흙·풀·바닥재)은 샌드박스의 바닥 층 하나로, 장식은 같은 이름 층으로, 벽·물건·위만 더한다
        int ground = sandbox.Floors.Single(f => f.Name == Chickxel.Core.Localization.DomainStrings.Tile_FloorGround).Id;
        Assert.Equal(sandbox.Floors.Length + 3, ts.Floors.Length);
        var grass = ts.AutoTiles.Single(a => TileSources.Slot(a.Id) == 1 && a.Name == Chickxel.Core.Localization.DomainStrings.TileBuiltIn_Grass);
        var water = ts.AutoTiles.Single(a => TileSources.Slot(a.Id) == 1 && a.Name == Chickxel.Core.Localization.DomainStrings.TileBuiltIn_Water);
        var flower = ts.Tiles.First(t => TileSources.Slot(t.Id) == 1 && t.PaletteX == 0 && t.PaletteY == 2);
        Assert.Equal((ground, ground), (grass.Floor, water.Floor));
        Assert.NotEqual(grass.Floor, flower.Floor);
        // 바닥 층 안에서는 원래 층 차례대로(풀 가장자리가 물 위에)
        Assert.True(ts.Find(grass.Rules.Last().Tiles[0].Tile)!.FloorRank > ts.Find(water.Rules.Last().Tiles[0].Tile)!.FloorRank);
        var map = AutoTiler.Paint(ts, TileMap.Empty, grass, [new Point(1, 1)]).Place(ts, flower.Floor, new Point(1, 1), new TileCell(flower.Id));
        Assert.NotNull(map.Get(grass.Floor, new Point(1, 1)));
        Assert.NotNull(map.Get(flower.Floor, new Point(1, 1)));

        // 예전 파일: 마을의 꽃이 예전 규칙의 층(첫 타일셋 층)에 들어 있음
        int localFlowerFloor = village.Find(TileSources.Local(flower.Id))!.Floor;
        int oldFloor = TileSources.MapFloor(village, sandbox, localFlowerFloor);
        p.SetTileMap(layer, TileMap.Empty.With(oldFloor, new Point(2, 2), new TileCell(flower.Id)), "예전");
        using var ms = new MemoryStream();
        ProjectIO.Save(p, ms);
        ms.Position = 0;
        var loaded = ProjectIO.Load(ms).Layers.Single(l => l.IsTilemap).Tiles!.Map;
        Assert.Equal([new PlacedTile(flower.Floor, new Point(2, 2), new TileCell(flower.Id))], loaded.All.ToArray());
    }

    [Fact]
    public void Save_Load_RoundTrip_KeepsLayerTilesetList()
    {
        var primary = Sample();
        var second = Second();
        var (p, layer) = NewProject(primary);
        p.AddLayerTileset(layer, second);
        var ts = p.TilesetOf(layer)!;
        Paint(p, layer, m => m.Place(ts, 1, new Point(0, 0), new TileCell(1)).Place(ts, 2, new Point(1, 0), new TileCell(TileSources.Global(1, 1))));
        using var ms = new MemoryStream();
        ProjectIO.Save(p, ms);
        ms.Position = 0;
        var q = ProjectIO.Load(ms);
        var loaded = q.Layers.Single(l => l.IsTilemap);
        Assert.Equal([primary.Key, second.Key], loaded.Tiles!.TilesetKeys.ToArray());
        Assert.Equal(2, q.UsedTilesets.Count);
        Assert.Equal(2, q.TilesetSources(loaded).Count);
        Assert.True(p.Flatten(0).ContentEquals(q.Flatten(0)));
        Assert.Equal(Green, q.Flatten(0)[5, 1]);
    }

    [Fact]
    public void ClosedTileset_StaysInProjectAcrossSave_AndOnlyUnusedCanBeDeleted()
    {
        var primary = Sample();
        var second = Second();
        var (p, layer) = NewProject(primary);
        p.AddLayerTileset(layer, second);
        p.RemoveLayerTileset(layer, 1);
        Assert.False(p.RemoveTileset(primary.Key));
        using var ms = new MemoryStream();
        ProjectIO.Save(p, ms);
        ms.Position = 0;
        var q = ProjectIO.Load(ms);
        Assert.True(q.Tilesets.ContainsKey(second.Key));
        Assert.True(q.RemoveTileset(second.Key));
        Assert.False(q.Tilesets.ContainsKey(second.Key));
        q.Undo();
        Assert.True(q.Tilesets.ContainsKey(second.Key));
    }
}
