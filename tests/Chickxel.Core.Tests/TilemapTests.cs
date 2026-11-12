using System.Collections.Immutable;
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
            Assert.True(AutoTiler.HasExact(set, (byte)m));
            Assert.Single(set.Rules, r => r.Matches((byte)m));
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
        Assert.Equal(7, AutoTiler.Pick(set, AutoTileShapes.S | AutoTileShapes.E, Point.Empty)?.Tile);
        Assert.Equal(8, AutoTiler.Pick(set, AutoTileShapes.N, Point.Empty)?.Tile);
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
            var a = AutoTiler.Pick(set, 0, new Point(x, 5))!.Value.Tile;
            Assert.Equal(a, AutoTiler.Pick(set, 0, new Point(x, 5))!.Value.Tile);
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
    public void EditTileset_RemovingTile_RemovesItFromMaps_InOneUndoStep()
    {
        var ts = Sample();
        var (p, layer) = NewProject(ts);
        Paint(p, layer, m => m.Place(ts, 1, new Point(0, 0), new TileCell(1)).Place(ts, 2, new Point(0, 0), new TileCell(3)));
        p.EditTileset("타일 지우기", ts with { Tiles = ts.Tiles.RemoveAt(0) }, m => m.WithoutTile(1));
        Assert.Equal([3], layer.Tiles!.Map.All.Select(t => t.Cell.Tile));
        p.Undo();
        Assert.Equal(2, layer.Tiles!.Map.Count);
        Assert.NotNull(p.TilesetOf(layer)!.Find(1));
    }

    [Fact]
    public void ConvertToGroup_MakesOnePartPerFloor_WithSamePixels()
    {
        var ts = Sample();
        var (p, layer) = NewProject(ts);
        p.AddFrame();
        Paint(p, layer, m => m.Place(ts, 1, new Point(0, 0), new TileCell(2)).Place(ts, 2, new Point(1, 1), new TileCell(3)));
        var before = p.Flatten(1);
        var group = p.ConvertTilemapToGroup(p.IndexOf(layer));
        Assert.True(group.IsGroup);
        var parts = p.ChildrenOf(group);
        Assert.Equal(["바닥", "장식"], parts.Select(l => l.Name));
        Assert.Equal(new Rectangle(4, 4, 4, 4), parts[1].PartRect);
        Assert.True(before.ContentEquals(p.Flatten(1)));
        Assert.Same(p.GetCel(p.IndexOf(parts[0]), 0), p.GetCel(p.IndexOf(parts[0]), 1));
        p.Undo();
        Assert.Same(layer, p.Layers.Single(l => l.IsTilemap));
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
}
