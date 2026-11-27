using System.Drawing;
using System.Text.Json;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

string folder = Path.GetFullPath(args.Length == 0 ? "docs/assets/village" : args.Single());
var warnings = new List<string>();
var map = ProjectIO.Load(Path.Combine(folder, "village.chickxel"), warnings);
if (warnings.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, warnings));
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var sources = JsonSerializer.Deserialize<Sources>(File.ReadAllText(Path.Combine(folder, "sources.json")), json)!;
var props = map.Layers.Single(l => l.Tiles?.PrimaryTilesetKey == "village-props");
var terrain = map.Layers.Single(l => l.Tiles?.PrimaryTilesetKey == "village-terrain");
var plots = map.Layers.Single(l => l.Tiles?.PrimaryTilesetKey == "village-plots");
var propsSet = map.TilesetSources(props)[0];
var sourceArt = new Dictionary<int, PixelImage>();
foreach (var (name, tiles) in sources.Scenery)
{
    var doc = LoadObject(name);
    if (doc.Frames.Count != tiles.Length) throw new InvalidDataException($"{name}: expected {tiles.Length} frames.");
    for (int i = 0; i < tiles.Length; i++)
    {
        var tile = propsSet.Find(tiles[i]) ?? throw new InvalidDataException($"{name}: missing linked tile {tiles[i]}.");
        if (doc.Width < tile.Image.Width || doc.Height < tile.Image.Height)
            throw new InvalidDataException($"{name}: frame {i + 1} is smaller than its tile.");
        sourceArt.Add(tile.Id, doc.Flatten(i).Crop(tile.Image.Bounds));
    }
}
var door = LoadObject("door");
var doorImages = Enumerable.Range(0, door.Frames.Count).Select(i => door.Flatten(i)).ToArray();
if (doorImages.Length < 2) throw new InvalidDataException("door: expected at least two frames from closed to open.");
var doorTile = propsSet.Find(sources.DoorTile) ?? throw new InvalidDataException("Missing house door tile.");
if (doorImages[0].Size != doorTile.Image.Size) throw new InvalidDataException("door: keep the original canvas size.");
sourceArt.Add(doorTile.Id, doorImages[0]);

// Keep the linked tiles usable in Chickxel after editing a separate sprite. The map still owns placement and depth.
SyncArt(propsSet, sourceArt);
var soil = LoadObject("soil");
var drySoil = soil.Flatten(0);
SyncArt(map.TilesetSources(plots)[0], new Dictionary<int, PixelImage> { [1] = drySoil });

var ground = new PixelImage(map.Width, map.Height);
map.FlattenInto(ground, 0, layerFilter: l => !ReferenceEquals(l, props) && !ReferenceEquals(l, plots));
PngIO.Save(ground, Path.Combine(folder, "ground.png"));
var water = DrawTiles(terrain)
    .Select(t => (At: t.Rect.Location, Kind: WaterKind(map.TilesetOf(terrain)!.Find(t.Tile.Cell.Tile)!.Name)))
    .Where(t => t.Kind != 0)
    .Select(t => new[] {t.At.X, t.At.Y, t.Kind}).ToArray();

var atlas = new List<PixelImage>();
var spriteCache = new Dictionary<TileCell, int>();
var placed = new List<double[]>();
var doorwayFrames = Array.Empty<int>();
int doorway = -1;
var objectsSet = map.TilesetOf(props)!;
foreach (var draw in DrawTiles(props))
{
    var cell = draw.Tile.Cell;
    if (!spriteCache.TryGetValue(cell, out var sprite))
        spriteCache.Add(cell, sprite = Add(draw.Image));
    double depth = props.Tiles!.Origin.Y + objectsSet.SortLine(draw.Tile.At, cell);
    if (cell.Tile == sources.DoorTile)
    {
        depth += 0.5;
        if (doorway < 0)
        {
            doorway = placed.Count;
            doorwayFrames = new int[doorImages.Length];
            doorwayFrames[0] = sprite;
            for (int i = 1; i < doorImages.Length; i++) doorwayFrames[i] = Add(cell.Transform.ApplyToImage(doorImages[i]));
        }
    }
    placed.Add([sprite, draw.Rect.X, draw.Rect.Y, depth]);
}
var chick = LoadObject("chick").Flatten(0);
if (chick.Width != 96 || chick.Height != 56) throw new InvalidDataException("chick: expected four directions of six 16×14 sprites.");
int chicks = Add(chick), can = Add(LoadObject("watering-can").Flatten(0));
var soils = new[] { Add(drySoil), Add(soil.Flatten(1)) };
var fields = DrawTiles(plots).OrderBy(t => t.Rect.X).ThenBy(t => t.Rect.Y)
    .Select(t => new[] { t.Rect.X + 1, t.Rect.Y + 1 }).ToArray();
string[] cropNames = ["carrot", "cabbage", "wheat", "tomato", "pumpkin", "sunflower"];
var crops = cropNames.ToDictionary(name => name, name =>
{
    var doc = LoadObject(name);
    int first = Add(doc.Flatten(0));
    return new[] {first, doc.Frames.Count > 1 ? Add(doc.Flatten(1)) : first};
});

var (sheet, rects) = Pack(atlas);
PngIO.Save(sheet, Path.Combine(folder, "objects.png"));
var scene = new { size = new[] {sheet.Width, sheet.Height}, sprites = rects, objects = placed,
    door = new { @object = doorway, frames = doorwayFrames }, crops, chicks, can, soils, fields, water };
File.WriteAllText(Path.Combine(folder, "scene.json"), JsonSerializer.Serialize(scene, json) + "\n");
Console.WriteLine($"{map.Width}×{map.Height}, {terrain.Tiles!.Map.Count} terrain cells, {placed.Count} placed objects, {atlas.Count} sprites.");

SpriteProject LoadObject(string name) => ProjectIO.Load(Path.Combine(folder, "objects", name + ".chickxel"));
int Add(PixelImage image) { int id = atlas.Count; atlas.Add(image); return id; }

void SyncArt(Tileset set, Dictionary<int, PixelImage> images)
{
    bool changed = false;
    var tiles = set.Tiles.Select(tile =>
    {
        if (!images.TryGetValue(tile.Id, out var image) ||
            (image.Size == tile.Image.Size && image.Pixels.AsSpan().SequenceEqual(tile.Image.Pixels))) return tile;
        if (image.Size != tile.Image.Size) throw new InvalidDataException($"{tile.Name}: keep the original canvas size.");
        changed = true;
        return tile with { Image = image, Frames = [] };
    }).ToArray();
    if (!changed) return;
    set = set with { Tiles = [.. tiles] };
    TilesetIO.Save(set, set.SourcePath ?? throw new InvalidDataException($"Missing linked tileset: {set.Name}."));
    map.EditTileset("Sync linked artwork", set);
}

IEnumerable<TileDraw> DrawTiles(Layer layer)
{
    var data = layer.Tiles!;
    return layer.Visible
        ? TileMapRenderer.DrawOrder(map.TilesetOf(layer)!, data.Map, data.Origin, map.Bounds, data.ShowsFloor)
        : [];
}

// The named water tiles keep their surface effect when moved on the map.
static int WaterKind(string name) => name.Split(' ', 2)[0] switch { "강" or "다리물" => 1, "호수" => 2, "폭포" => 3, _ => 0 };

static (PixelImage Image, int[][] Rects) Pack(List<PixelImage> sprites)
{
    int width = Math.Max(512, sprites.Max(s => s.Width));
    var rects = new int[sprites.Count][];
    int x = 0, y = 0, rowHeight = 0;
    foreach (int i in Enumerable.Range(0, sprites.Count).OrderByDescending(i => sprites[i].Height))
    {
        var image = sprites[i];
        if (x + image.Width > width) { x = 0; y += rowHeight + 1; rowHeight = 0; }
        rects[i] = [x, y, image.Width, image.Height];
        x += image.Width + 1;
        rowHeight = Math.Max(rowHeight, image.Height);
    }
    var result = new PixelImage(width, y + rowHeight);
    for (int i = 0; i < sprites.Count; i++)
        result.Blit(sprites[i], sprites[i].Bounds, rects[i][0], rects[i][1]);
    return (result, rects);
}

sealed record Sources(Dictionary<string, int[]> Scenery, int DoorTile);
