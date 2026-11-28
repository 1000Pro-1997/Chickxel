using System.Collections.Immutable;
using System.Drawing;
using System.Text.Json;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;
using static Chickxel.Core.Tiles.AutoTileShapes;

// One-time local conversion, intentionally never overwriting either an edited source or an earlier conversion.
string folder = Path.GetFullPath(args.Length == 0 ? "../CHICKXEL_Village" : args.Single());
string output = Path.Combine(folder, "village-unified.chickxel");
string tilesetFile = Path.Combine(folder, "village-unified.chickxel-tileset");
if (File.Exists(output) || File.Exists(tilesetFile))
    throw new IOException("Unified village already exists; refusing to overwrite edited artwork.");
var warnings = new List<string>();
var project = ProjectIO.Load(Path.Combine(folder, "village-local.chickxel"), warnings);
if (warnings.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, warnings));
if (project.Frames.Count != 1) throw new InvalidDataException("Expected one village frame; refusing to drop animation frames.");
var terrain = project.Layers.Single(l => l.Tiles?.PrimaryTilesetKey is { } key && project.Tilesets.TryGetValue(key, out var t) && t.CellWidth == 16);
var floor = project.Layers.Single(l => l.Tiles?.PrimaryTilesetKey == "village-floor");
var plots = project.Layers.Single(l => l.Tiles?.PrimaryTilesetKey == "village-plots");
var props = project.Layers.Single(l => l.Tiles?.PrimaryTilesetKey == "village-props");
if (project.Layers.Count != 5 || project.Layers.Count(l => l.IsTilemap) != 4 ||
    project.Layers.Any(l => l != terrain && l != floor && l != plots && l != props && l.Name != "그림자"))
    throw new InvalidDataException("Unexpected village layer: refusing to discard user artwork.");
foreach (var layer in new[] { terrain, floor, plots, props })
    if (layer.Tiles is not { Origin: { X: 0, Y: 0 }, TilesetKeys.Length: 1 } || project.TilesetOf(layer) is null)
        throw new InvalidDataException($"Unexpected tilemap origin or tileset slots on {layer.Name}.");
if (props.Tiles!.FreePlacement != true ||
    project.TilesetOf(floor) is not { CellWidth: 1, CellHeight: 1 } ||
    project.TilesetOf(plots) is not { CellWidth: 1, CellHeight: 1 } ||
    project.TilesetOf(props) is not { CellWidth: 1, CellHeight: 1 })
    throw new InvalidDataException("Expected original 1px floor, plots and free-placement props.");
var builtIn = project.TilesetOf(terrain)!;
if (builtIn.CellWidth != 16 || builtIn.CellHeight != 16 || builtIn.SortMode != TileSortMode.ByPosition)
    throw new InvalidDataException("Expected built-in 16px top-down village terrain.");
var floorSet = project.TilesetOf(floor)!;
var plotSet = project.TilesetOf(plots)!;
var propSet = project.TilesetOf(props)!;
var source = JsonSerializer.Deserialize<Sources>(File.ReadAllText(Path.Combine(folder, "sources.json")),
    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Missing source variant table.");
var knownKinds = new HashSet<string>(StringComparer.Ordinal)
{
    "oak", "fir", "poplar", "bush", "rock", "stump", "flowers", "mushroom", "reeds", "log", "tall-grass",
    "house-wall", "bed", "bookshelf", "stove", "chest", "table", "chair", "pot", "candlestick",
    "fence", "wheat-field", "barrel", "fruit-crate", "streetlamp", "signpost"
};
if (!source.Scenery.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(knownKinds))
    throw new InvalidDataException("Unrecognized/missing scenery category in sources.json.");
var kinds = new Dictionary<int, string>();
foreach (var (kind, ids) in source.Scenery)
    foreach (int id in ids)
        if (!kinds.TryAdd(id, kind)) throw new InvalidDataException($"Duplicate props source ID {id}.");
if (!kinds.TryAdd(source.DoorTile, "door") || !kinds.Keys.ToHashSet().SetEquals(propSet.Tiles.Select(t => t.Id)))
    throw new InvalidDataException("sources.json must account for each props variant exactly once.");
if (!floorSet.Tiles.Select(t => t.Id).ToHashSet().SetEquals(new[] { 1, 2 }) ||
    !plotSet.Tiles.Select(t => t.Id).ToHashSet().SetEquals(new[] { 1 }))
    throw new InvalidDataException("Unknown floor/plot art; add an explicit mapping before conversion.");
if (floorSet.AutoTiles.Length != 0 || plotSet.AutoTiles.Length != 0 || propSet.AutoTiles.Length != 0)
    throw new InvalidDataException("Unexpected old auto-tile rules; refusing to drop them.");

// Disjoint source IDs preserve every old variant; generated rule art starts at 20k.
// The source has two overlapping props at identical pixel anchors on distinct old floors.
int Plot = builtIn.NextFloorId, Fence = checked(Plot + 1), Free = checked(Plot + 2), FreeOverlap = checked(Plot + 3);
const int FloorBase = 10_000, PlotBase = 11_000, PropBase = 12_000, OverlapOffset = 1_000;
if (builtIn.NextTileId >= FloorBase || builtIn.NextAutoTileId >= 100)
    throw new InvalidDataException("Built-in IDs overlap unified namespace; adjust the explicit ID mapping.");
int paletteBase = builtIn.Tiles.Max(t => t.PaletteY) + 2;
var added = new List<TileDef>();
foreach (var tile in floorSet.Tiles)
    added.Add(tile with { Id = FloorBase + tile.Id, Floor = Free, PaletteY = paletteBase + 5 + tile.PaletteY,
        Name = $"원본 바닥 시설 {tile.Id}: {tile.Name}" });
foreach (var tile in plotSet.Tiles)
    added.Add(tile with { Id = PlotBase + tile.Id, Floor = Free, PaletteY = paletteBase + 7 + tile.PaletteY,
        Name = $"원본 밭 {tile.Id}: {tile.Name}" });
foreach (var tile in propSet.Tiles)
    added.Add(tile with
    {
        Id = PropBase + tile.Id, Floor = Free, DrawClass = TileDrawClass.Sorted,
        PaletteY = paletteBase + 9 + tile.PaletteY,
        Name = $"{kinds[tile.Id]} / 원본 props {tile.Id}: {tile.Name}"
    });
// A distinct palette entry on the second free floor keeps even overlapping old variants editable.
foreach (int id in props.Tiles.Map.All.Where(p => p.Floor == 3).Select(p => p.Cell.Tile).Distinct())
{
    var tile = propSet.Find(id) ?? throw new InvalidDataException($"Missing overlapping props variant {id}.");
    added.Add(tile with { Id = PropBase + OverlapOffset + id, Floor = FreeOverlap,
        DrawClass = TileDrawClass.Sorted, PaletteY = paletteBase + 22 + tile.PaletteY,
        Name = $"{kinds[id]} / 원본 props {id} (겹침): {tile.Name}" });
}

// The old 34x36 plots and 91x14 / 7x74 fences are *not* grid art. Keep those originals intact
// on Free, and derive separate genuinely neighbor-sensitive 16px brush art for future grid editing.
var newSets = new List<AutoTileSet>();
int nextTile = 20_000;
int nextAuto = Math.Max(100, builtIn.NextAutoTileId);
var plotImage = plotSet.Find(1)!.Image;
var dark = plotImage.Pixels.Where(c => c.A != 0).OrderBy(c => c.R + c.G + c.B).First();
var plotRules = ImmutableArray.CreateBuilder<AutoTileRule>();
var fenceRules = ImmutableArray.CreateBuilder<AutoTileRule>();
var horizontal = propSet.Find(23)!.Image;
var vertical = propSet.Find(24)!.Image;
if (plotImage.Width < 34 || plotImage.Height < 36 || horizontal.Width < 52 || horizontal.Height < 14 || vertical.Width < 7 || vertical.Height < 48)
    throw new InvalidDataException("Source plot/fence art changed; re-evaluate brush samples before conversion.");
for (int i = 0; i < Count(AutoTilePreset.Edge4); i++)
{
    byte shape = Shape(AutoTilePreset.Edge4, i);
    var soil = plotImage.Crop(new Rectangle(9, 10, 16, 16));
    // Only exposed edges receive a rim. The center and every existing source instance are unchanged.
    if ((shape & N) == 0) soil.FillRect(new Rectangle(0, 0, 16, 2), dark);
    if ((shape & E) == 0) soil.FillRect(new Rectangle(14, 0, 2, 16), dark);
    if ((shape & S) == 0) soil.FillRect(new Rectangle(0, 14, 16, 2), dark);
    if ((shape & W) == 0) soil.FillRect(new Rectangle(0, 0, 2, 16), dark);
    int id = nextTile++;
    added.Add(new TileDef { Id = id, Name = $"밭 연결 {i}", Floor = Plot, Image = soil, PaletteX = i % 8,
        PaletteY = paletteBase + i / 8 });
    plotRules.Add(Rule(AutoTilePreset.Edge4, shape) with { Tiles = [new AutoTileChoice(id)] });

    var rails = new PixelImage(16, 16);
    // Samples from the actual horizontal and vertical source strips. Missing links are capped,
    // not merely renamed: straight runs, bends, tees, crosses and endpoints have different pixels.
    if ((shape & (E | W)) != 0)
        rails.Blit(horizontal, new Rectangle(36, 0, 16, 14), 0, 1);
    rails.Blit(vertical, new Rectangle(0, (shape & N) != 0 ? 32 : 0, 7, 8), 4, 0, alphaBlend: true);
    rails.Blit(vertical, new Rectangle(0, (shape & S) != 0 ? 40 : vertical.Height - 8, 7, 8), 4, 8, alphaBlend: true);
    if ((shape & W) == 0) rails.FillRect(new Rectangle(0, 1, 3, 14), default);
    if ((shape & E) == 0) rails.FillRect(new Rectangle(13, 1, 3, 14), default);
    id = nextTile++;
    added.Add(new TileDef { Id = id, Name = $"울타리 연결 {i}", Floor = Fence, Image = rails,
        DrawClass = TileDrawClass.Sorted, PaletteX = i % 8, PaletteY = paletteBase + 2 + i / 8 });
    fenceRules.Add(Rule(AutoTilePreset.Edge4, shape) with { Tiles = [new AutoTileChoice(id)] });
}
newSets.Add(new AutoTileSet { Id = nextAuto++, Name = "밭 (16픽셀 연결)", Floor = Plot, Rules = plotRules.ToImmutable() });
newSets.Add(new AutoTileSet { Id = nextAuto++, Name = "울타리 (16픽셀 연결)", Floor = Fence, Rules = fenceRules.ToImmutable() });
// Reuse the built-in wood-wall 47-neighbor, door 16-neighbor, wood-floor and stone-path
// rules rather than duplicating their artwork. The custom sets above complete plots/fences.
var merged = builtIn with
{
    Name = "마을 통합: 지형·시설·밭·울타리·물건",
    SourcePath = tilesetFile,
    Floors = [.. builtIn.Floors, new TileFloor(Plot, "밭"), new TileFloor(Fence, "울타리"),
        new TileFloor(Free, "개별 물건 (픽셀 좌표)"), new TileFloor(FreeOverlap, "겹친 개별 물건 (픽셀 좌표)")],
    Tiles = [.. builtIn.Tiles, .. added],
    AutoTiles = [.. builtIn.AutoTiles, .. newSets]
};
if (merged.Tiles.Select(t => t.Id).Distinct().Count() != merged.Tiles.Length ||
    merged.AutoTiles.Select(a => a.Id).Distinct().Count() != merged.AutoTiles.Length)
    throw new InvalidDataException("Unified tile/auto-tile IDs overlap.");

var oldTerrain = terrain.Tiles!.Map.All.ToArray();
if (oldTerrain.Any(p => builtIn.FindFloor(p.Floor) is null || builtIn.Find(p.Cell.Tile) is null ||
    (p.Cell.AutoTile != 0 && builtIn.FindAutoTile(p.Cell.AutoTile) is null)))
    throw new InvalidDataException("Unrecognized original terrain floor/tile/auto-tile ID.");
var moved = new List<PlacedTile>(floor.Tiles!.Map.Count + plots.Tiles!.Map.Count + props.Tiles!.Map.Count);
void Copy(Layer layer, Tileset old, int offset, IReadOnlyDictionary<int, string>? categories = null)
{
    foreach (var p in layer.Tiles!.Map.All)
    {
        var tile = old.Find(p.Cell.Tile) ?? throw new InvalidDataException($"{layer.Name}: missing tile {p.Cell.Tile} at {p.At}.");
        if (p.Cell.AutoTile != 0 || old.FindFloor(p.Floor) is null ||
            (categories is not null && !categories.ContainsKey(p.Cell.Tile)))
            throw new InvalidDataException($"{layer.Name}: unmapped placement {p.Floor}:{p.Cell.Tile} at {p.At}.");
        bool overlap = layer == props && p.Floor == 3;
        moved.Add(new PlacedTile(overlap ? FreeOverlap : Free, p.At,
            p.Cell with { Tile = offset + p.Cell.Tile + (overlap ? OverlapOffset : 0) }));
    }
}
Copy(floor, floorSet, FloorBase);
Copy(plots, plotSet, PlotBase);
Copy(props, propSet, PropBase, kinds);
var positions = new HashSet<(int Floor, Point At)>();
foreach (var p in moved)
    if (!positions.Add((p.Floor, p.At)))
        throw new InvalidDataException($"Two source instances share floor {p.Floor} pixel anchor {p.At}; no output written.");
var map = TileMap.FromCells(oldTerrain.Concat(moved));
if (map.Count != oldTerrain.Length + moved.Count ||
    moved.Any(p => map.Get(p.Floor, p.At) != p.Cell))
    throw new InvalidDataException("Source instance lost during merge.");

// Empty the terrain before switching its tileset. A nonempty floor must never be reinterpreted
// when it becomes free placement; only the freshly constructed map is ever installed afterward.
project.SetTileMap(terrain, TileMap.Empty, "Prepare unified map");
project.SetLayerTileset(terrain, 0, merged);
project.SetTileFloorFreePlacement(terrain, Free, true);
project.SetTileFloorFreePlacement(terrain, FreeOverlap, true);
project.SetTileMap(terrain, map, "Merge village placements into one editable layer");
project.SetLayerProperties(project.IndexOf(terrain), name: "마을 통합 (16픽셀 지형 + 자유 물건)");
for (int i = project.Layers.Count - 1; i >= 0; i--)
    if (project.Layers[i] != terrain) project.DeleteLayer(i); // intentionally exclude the static painted shadow layer
if (project.Layers.Count != 1 || project.Layers[0] != terrain)
    throw new InvalidDataException("Unified map must have precisely one editable tilemap layer.");
foreach (var old in project.AllTilesets.Where(t => t.Key != merged.Key).ToArray())
    if (!project.RemoveTileset(old.Key))
        throw new InvalidDataException($"Old tileset {old.Key} is still in use; refusing to discard its layer.");
if (project.AllTilesets.Count != 1)
    throw new InvalidDataException("Unified map must carry exactly one combined tileset.");
// Avoid creating a dangling output map if the tileset write fails; neither output existed on entry.
try
{
    TilesetIO.Save(merged, tilesetFile);
    ProjectIO.Save(project, output);
}
catch
{
    if (File.Exists(output)) File.Delete(output);
    if (File.Exists(tilesetFile)) File.Delete(tilesetFile);
    throw;
}
Console.WriteLine($"Created {output}: {oldTerrain.Length} terrain cells, {moved.Count} pixel-positioned source instances.");

sealed record Sources(Dictionary<string, int[]> Scenery, int DoorTile);
