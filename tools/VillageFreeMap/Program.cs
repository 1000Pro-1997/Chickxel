using Chickxel.Core.IO;

// Make a separate local draft; never overwrite either the published source or an edited auto-tile draft.
string folder = Path.GetFullPath(args.Length == 0 ? "../CHICKXEL_Village" : args.Single());
string output = Path.Combine(folder, "village-local.chickxel");
if (File.Exists(output)) throw new IOException("Local village already exists; refusing to overwrite edited artwork.");
var warnings = new List<string>();
var project = ProjectIO.Load(Path.Combine(folder, "village-auto.chickxel"), warnings);
if (warnings.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, warnings));
var props = project.Layers.Single(l => l.Tiles?.PrimaryTilesetKey == "village-props");
var terrain = project.Layers.Single(l => l.Tiles?.PrimaryTilesetKey != "village-props" && l.Tiles is not null
    && project.TilesetOf(l) is { CellWidth: 16, CellHeight: 16 });
int objects = props.Tiles!.Map.Count;
if (project.TilesetOf(props) is not { CellWidth: 1, CellHeight: 1 })
    throw new InvalidDataException("Village object tileset must use 1px anchors.");
project.SetTilemapFreePlacement(props, true);
project.SetLayerProperties(project.IndexOf(props), name: "나무·물건 배치 (자유 타일맵)");
ProjectIO.Save(project, output);

// A saved free layer must keep every old object at its original pixel anchor.
var reopened = ProjectIO.Load(output, warnings = []);
if (warnings.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, warnings));
var saved = reopened.Layers.Single(l => l.Tiles?.PrimaryTilesetKey == "village-props");
if (saved.Tiles?.FreePlacement != true || saved.Tiles.Map.Count != objects
    || !saved.Tiles.Map.All.ToHashSet().SetEquals(props.Tiles.Map.All))
    throw new InvalidDataException("Object placements changed while saving the free tilemap.");
Console.WriteLine($"Created {output}: {terrain.Tiles!.Map.Count} terrain tiles, {objects} independently placed objects.");
