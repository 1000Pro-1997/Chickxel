using Chickxel.Core.IO;
using Chickxel.Core.Tiles;

// One-time local sync: the unified village keeps copies of the built-in top-down ground tiles (merged onto one floor).
// After the built-in shore art changed (rounded bank ends), copy the new pictures over by tile Id. Only ground tiles of
// the built-in ground auto-tile sets change; floors, ranks, offsets and every other tile stay as they are.
string folder = Path.GetFullPath(args.Length == 0 ? "../CHICKXEL_Village" : args.Single());
string path = Path.Combine(folder, "village-unified.chickxel");
// Each run keeps its own dated original next to the map; the sync itself only copies pictures that differ.
string backup = Path.Combine(folder, $"village-unified.before-shore-{DateTime.Now:yyyyMMdd-HHmmss}.chickxel");
var warnings = new List<string>();
var project = ProjectIO.Load(path, warnings);
if (warnings.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, warnings));
var layer = project.Layers.Single(l => l.IsTilemap);
var ts = project.Tilesets[layer.Tiles!.PrimaryTilesetKey];
var builtIn = BuiltInTilesets.TopDownVillage();
string[] groundSets = ["물", "모래", "흙", "풀", "돌길", "나무 바닥"];
var ids = ts.AutoTiles.Where(a => groundSets.Contains(a.Name)).SelectMany(a => a.Rules).SelectMany(r => r.Tiles).Select(c => c.Tile).ToHashSet();
int changed = 0;
var tiles = ts.Tiles.Select(t =>
{
    if (!ids.Contains(t.Id) || builtIn.Find(t.Id) is not { } b) return t;
    if (b.Width != t.Width || b.Height != t.Height || b.DrawOffset != t.DrawOffset || b.Frames.Length != t.Frames.Length)
        throw new InvalidDataException($"Tile {t.Id} no longer matches the built-in layout.");
    bool same = b.Image.ContentEquals(t.Image) && b.Frames.Zip(t.Frames).All(p => p.First.ContentEquals(p.Second));
    if (same) return t;
    changed++;
    return t with { Image = b.Image, Frames = b.Frames };
}).ToList();
if (changed == 0) { Console.WriteLine("Nothing to sync."); return; }
File.Copy(path, backup);
File.Copy(ts.SourcePath!, Path.ChangeExtension(backup, Tileset.FileExtension));
project.EditTileset("Sync shore tiles", ts with { Tiles = [.. tiles] });
ProjectIO.Save(project, path);
Console.WriteLine($"Synced {changed} ground tiles. Original: {backup}");
