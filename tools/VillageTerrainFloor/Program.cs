using System.Collections.Immutable;
using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Tiles;

// One-time local repair of the unified village: the five built-in ground floors (water, sand, dirt, grass, paving)
// become one "지형" floor. Their old floor order moves into FloorRank so upper ground edges still cover lower ground,
// and grid tiles that lost their auto-tile link (free-floor round trip) are linked to their set again.
// Refuses to run twice; the untouched originals are kept next to the map.
string folder = Path.GetFullPath(args.Length == 0 ? "../CHICKXEL_Village" : args.Single());
string path = Path.Combine(folder, "village-unified.chickxel");
string backup = Path.Combine(folder, "village-unified.before-terrain-floor.chickxel");
var warnings = new List<string>();
var project = ProjectIO.Load(path, warnings);
if (warnings.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, warnings));
var layer = project.Layers.Single(l => l.IsTilemap);
var data = layer.Tiles!;
if (data.TilesetKeys.Length != 1) throw new InvalidDataException("Expected one tileset slot.");
var ts = project.Tilesets[data.PrimaryTilesetKey];
string tilesetPath = ts.SourcePath ?? throw new InvalidDataException("Expected a linked tileset file.");
string tilesetBackup = Path.ChangeExtension(backup, Tileset.FileExtension);
if (File.Exists(backup) || File.Exists(tilesetBackup)) throw new IOException("Terrain floors were already merged; refusing to run twice.");

string[] names = ["물", "모래", "흙", "풀", "바닥재"];
int[] ground = [.. names.Select(n => ts.Floors.Single(f => f.Name == n).Id)];
var groundSet = ground.ToHashSet();
int target = ground[0];
if (ground.Any(data.IsFreeFloor)) throw new InvalidDataException("A ground floor is a free floor.");
if (ts.Tiles.Any(t => groundSet.Contains(t.Floor) && t.FloorRank != 0)) throw new InvalidDataException("Ground tiles already use FloorRank.");
var groundCells = data.Map.All.Where(p => groundSet.Contains(p.Floor)).ToList();
if (groundCells.Select(p => p.At).Distinct().Count() != groundCells.Count)
    throw new InvalidDataException("Two ground tiles share a cell; merging would drop one.");

// Tile -> its only ground auto-tile set (tiles shared by two sets stay unlinked).
var owners = new Dictionary<int, int>();
var shared = new HashSet<int>();
foreach (var set in ts.AutoTiles.Where(s => groundSet.Contains(s.Floor)))
    foreach (int tile in set.Rules.SelectMany(r => r.Tiles).Select(c => c.Tile).Distinct())
        if (!owners.TryAdd(tile, set.Id) && owners[tile] != set.Id) shared.Add(tile);
foreach (int tile in shared) owners.Remove(tile);

var rank = ground.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
ImmutableArray<int> Floors(ImmutableArray<int> floors, bool dropGround)
    => [.. floors.Where(f => !dropGround || !groundSet.Contains(f)).Select(f => groundSet.Contains(f) ? target : f).Distinct()];
var merged = ts with
{
    Floors = [.. ts.Floors.Where(f => !groundSet.Contains(f.Id) || f.Id == target).Select(f => f.Id == target ? f with { Name = "지형" } : f)],
    Tiles = [.. ts.Tiles.Select(t => groundSet.Contains(t.Floor) ? t with { Floor = target, FloorRank = rank[t.Floor] } : t)],
    AutoTiles = [.. ts.AutoTiles.Select(s => groundSet.Contains(s.Floor)
        ? s with { Floor = target, ClearFloors = Floors(s.ClearFloors, dropGround: true), FilledFloors = Floors(s.FilledFloors, dropGround: false) }
        : s with { ClearFloors = Floors(s.ClearFloors, dropGround: false), FilledFloors = Floors(s.FilledFloors, dropGround: false) })],
};

var relinked = new List<Point>();
var map = TileMap.FromCells(data.Map.All.Select(p =>
{
    if (!groundSet.Contains(p.Floor)) return p;
    var cell = p.Cell;
    if (cell.AutoTile == 0 && owners.TryGetValue(cell.Tile, out int set))
    {
        cell = cell with { AutoTile = set };
        relinked.Add(p.At);
    }
    return new PlacedTile(target, p.At, cell);
}));
if (map.Count != data.Map.Count) throw new InvalidDataException("A tile was lost while merging floors.");

var before = project.Flatten(0);
project.EditTileset("Merge ground floors", merged, (_, _) => map);
var after = project.Flatten(0);

// Only the relinked tiles and their neighbors (ground edges lap 4px into the next cell) may change.
var allowed = relinked.Select(c => new Rectangle(data.Origin.X + (c.X - 1) * ts.CellWidth - 4, data.Origin.Y + (c.Y - 1) * ts.CellHeight - 4,
    3 * ts.CellWidth + 8, 3 * ts.CellHeight + 8)).ToList();
int changed = 0, unexpected = 0;
for (int y = 0; y < before.Height; y++)
    for (int x = 0; x < before.Width; x++)
        if (before[x, y] != after[x, y])
        {
            changed++;
            if (!allowed.Any(r => r.Contains(x, y))) unexpected++;
        }
if (unexpected != 0) throw new InvalidDataException($"{unexpected} pixels changed away from relinked tiles; no output written.");

File.Copy(path, backup);
File.Copy(tilesetPath, tilesetBackup);
ProjectIO.Save(project, path);
if (!ProjectIO.Load(path).Flatten(0).ContentEquals(after)) throw new InvalidDataException($"Saved map does not reload identically; originals are at {backup}.");
Console.WriteLine($"Merged {groundCells.Count} ground cells into floor {target} (지형); relinked {relinked.Count} auto tiles; {changed} pixels changed.");
Console.WriteLine($"Originals: {backup}, {tilesetBackup}");
