using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

// One-time local drafting tool. Never overwrite a Chickxel map someone may have edited.
string folder = Path.GetFullPath(args.Length == 0 ? "../CHICKXEL_Village" : args.Single());
string projectPath = Path.Combine(folder, "village-auto.chickxel");
string tilesetPath = Path.Combine(folder, "village-auto.chickxel-tileset");
if (File.Exists(projectPath) || File.Exists(tilesetPath))
    throw new IOException("Auto-tile draft already exists; refusing to overwrite edited artwork.");
var warnings = new List<string>();
var project = ProjectIO.Load(Path.Combine(folder, "village.chickxel"), warnings);
if (warnings.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, warnings));
var terrain = project.Layers.Single(l => l.Tiles?.PrimaryTilesetKey == "village-terrain");
var oldSet = project.TilesetOf(terrain)!;
var oldMap = terrain.Tiles!.Map;
int cols = project.Width / 16, rows = project.Height / 16;
if (cols * 16 != project.Width || rows * 16 != project.Height || terrain.Tiles.Origin != Point.Empty)
    throw new InvalidDataException("Expected 16px terrain cells at origin (0,0).");

var builtIn = BuiltInTilesets.TopDownVillage() with { SourcePath = tilesetPath };
AutoTileSet Set(string name) => builtIn.AutoTiles.Single(s => s.Name == name);
var grass = Set(DomainStrings.TileBuiltIn_Grass);
var path = Set(DomainStrings.TileBuiltIn_StonePath);
var water = Set(DomainStrings.TileBuiltIn_Water);
var wall = Set(DomainStrings.TileBuiltIn_StoneWall);
var allCells = new List<Point>(cols * rows);
var paths = new List<Point>();
var waters = new List<Point>();
var walls = new List<Point>();
for (int y = 0; y < rows; y++)
    for (int x = 0; x < cols; x++)
    {
        var at = new Point(x, y);
        var old = oldMap.Get(1, at) ?? throw new InvalidDataException($"Missing terrain cell ({x}, {y}).");
        string name = oldSet.Find(old.Tile)?.Name ?? throw new InvalidDataException($"Missing tile {old.Tile}.");
        string group = name.Split(' ', 2)[0];
        List<Point>? overlay = group switch
        {
            "풀" => null,
            "길" or "돌계단" => paths,
            "강" or "호수" or "폭포" or "다리물" => waters,
            "절벽" => walls,
            _ => throw new InvalidDataException($"Unclassified terrain tile: {name}"),
        };
        allCells.Add(at);
        overlay?.Add(at);
    }

// Replace only the terrain layer in this copy. Preserve the existing house, plot and object placements.
project.SetTileMap(terrain, TileMap.Empty, "Clear terrain in auto-tile draft");
project.SetLayerTileset(terrain, 0, builtIn);
project.RemoveLayerTileset(terrain, 1);
var map = AutoTiler.Paint(builtIn, TileMap.Empty, grass, allCells);
map = AutoTiler.Paint(builtIn, map, path, paths);
map = AutoTiler.Paint(builtIn, map, water, waters);
map = AutoTiler.Paint(builtIn, map, wall, walls);
project.SetTileMap(terrain, map, "Paint 16px connected terrain");

TilesetIO.Save(builtIn, tilesetPath);
ProjectIO.Save(project, projectPath);
Console.WriteLine($"Created {projectPath}: {map.Count} connected terrain cells; {builtIn.AutoTiles.Length} auto-tile sets.");
