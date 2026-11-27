using System.Text.Json;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

// Run once from the repository root: dotnet run --project tools/VillageObjects -- ../CHICKXEL_Village
// A frame is a static variant, not a time step. Pad at the top-left: VillageExport
// crops each frame to its matching tile's original bounds before syncing artwork.
string folder = Path.GetFullPath(args.Length == 0 ? "../CHICKXEL_Village" : args.Single());
var sources = JsonSerializer.Deserialize<Sources>(File.ReadAllText(Path.Combine(folder, "sources.json")),
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
var tileset = TilesetIO.Load(Path.Combine(folder, "props.chickxel-tileset"));
var existing = sources.Scenery.Keys.Where(name => File.Exists(Path.Combine(folder, "objects", name + ".chickxel"))).ToArray();
var uncovered = sources.Scenery.Keys.Except(existing).ToArray();
var assigned = sources.Scenery.SelectMany(pair => pair.Value).Append(sources.DoorTile).ToArray();
if (assigned.Length != tileset.Tiles.Length || assigned.Distinct().Count() != assigned.Length ||
    !assigned.Order().SequenceEqual(tileset.Tiles.Select(tile => tile.Id).Order()))
    throw new InvalidDataException("Scenery and door must cover each props tile exactly once.");
foreach (string name in uncovered)
{
    var tiles = sources.Scenery[name].Select(id => tileset.Find(id) ??
        throw new InvalidDataException($"Missing tile {id} for {name}")).ToArray();
    int width = tiles.Max(tile => tile.Image.Width), height = tiles.Max(tile => tile.Image.Height);
    var frames = new PixelImage[tiles.Length];
    for (int i = 0; i < tiles.Length; i++)
    {
        var image = tiles[i].Image;
        var frame = new PixelImage(width, height);
        frame.Blit(image, image.Bounds, 0, 0);
        if (frame.Width < image.Width || frame.Height < image.Height ||
            !frame.Crop(image.Bounds).Pixels.AsSpan().SequenceEqual(image.Pixels))
            throw new InvalidDataException($"{name}: tile {tiles[i].Id} cannot round-trip through its frame.");
        frames[i] = frame;
    }
    var project = SpriteProject.FromFrames(frames, Array.Empty<int>(), name);
    string path = Path.Combine(folder, "objects", name + ".chickxel");
    ProjectIO.Save(project, path);
    Console.WriteLine($"{name}: {tiles.Length} variants, {width}×{height}");
}

sealed record Sources(Dictionary<string, int[]> Scenery, int DoorTile);
