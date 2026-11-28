using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;
using static Chickxel.Core.Tiles.AutoTileShapes;

// One-time local redraw of the unified village's 16px plot auto tiles. The first draft cut a soil window and painted
// a 2px darkest rim on open sides with only 4-neighbor rules, so inner corners lost their outline. These tiles follow
// the source plot's own grammar (1px outline, 1px soil margin, 8px furrow pitch, 2px front face on the south side)
// with 47 rules so inner corners close. "--preview <png>" only writes a sample layout and leaves the map untouched.
string folder = Path.GetFullPath(args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "../CHICKXEL_Village");
string? preview = args.SkipWhile(a => a != "--preview").Skip(1).FirstOrDefault();
string path = Path.Combine(folder, "village-unified.chickxel");
string backup = Path.Combine(folder, "village-unified.before-plot.chickxel");
var warnings = new List<string>();
var project = ProjectIO.Load(path, warnings);
if (warnings.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, warnings));
var layer = project.Layers.Single(l => l.IsTilemap);
var ts = project.Tilesets[layer.Tiles!.PrimaryTilesetKey];
var plot = ts.AutoTiles.Single(a => a.Name == "밭 (16픽셀 연결)");
if (ts.CellWidth != 16 || ts.CellHeight != 16 || plot.Rules.Length != Count(AutoTilePreset.Edge4) || plot.Rules.Any(r => r.Tiles.Length != 1))
    throw new InvalidDataException("Unexpected plot auto-tile set (expected the 16-rule draft).");

var source = TilesetIO.Load(Path.Combine(folder, "plots.chickxel-tileset")).Find(1)?.Image
    ?? throw new InvalidDataException("Missing source plot.");
Color32 outline = source[0, 0], soil = source[1, 1], ridge = source[2, 3], furrow = source[2, 4], face = source[1, 33];
if (new[] { outline, soil, ridge, furrow, face }.Distinct().Count() != 5 || source[2, 11] != ridge || source[1, 35] != outline)
    throw new InvalidDataException("Source plot colors changed; re-check the sampling points.");

PixelImage Draw(byte shape)
{
    bool n = (shape & N) == 0, e = (shape & E) == 0, s = (shape & S) == 0, w = (shape & W) == 0;
    var img = new PixelImage(16, 16);
    img.FillRect(new Rectangle(0, 0, 16, 16), soil);
    int x0 = w ? 2 : 0, x1 = e ? 14 : 16;
    foreach (int top in (int[])[2, 10])
    {
        img.FillRect(new Rectangle(x0, top, x1 - x0, 1), ridge);
        img.FillRect(new Rectangle(x0, top + 1, x1 - x0, 2), furrow);
    }
    if (s) img.FillRect(new Rectangle(0, 13, 16, 2), face);
    if (n) img.FillRect(new Rectangle(0, 0, 16, 1), outline);
    if (s) img.FillRect(new Rectangle(0, 15, 16, 1), outline);
    if (w) img.FillRect(new Rectangle(0, 0, 1, 16), outline);
    if (e) img.FillRect(new Rectangle(15, 0, 1, 16), outline);
    // Inner corners: both sides continue but the diagonal cell is open.
    if (!n && !e && (shape & NE) == 0) img[15, 0] = outline;
    if (!n && !w && (shape & NW) == 0) img[0, 0] = outline;
    if (!s && !e && (shape & SE) == 0) img.FillRect(new Rectangle(15, 13, 1, 3), outline);
    if (!s && !w && (shape & SW) == 0) img.FillRect(new Rectangle(0, 13, 1, 3), outline);
    return img;
}

// Keep the 16 draft tile Ids for the matching corner-free shapes; add new Ids for the other 31 shapes.
var oldIds = plot.Rules.ToDictionary(r => r.Same, r => r.Tiles[0].Tile);
int nextId = ts.NextTileId, row = ts.Tiles.Max(t => t.PaletteY) + 1;
var template = ts.Find(oldIds[0])!;
var shapes = Enumerable.Range(0, Count(AutoTilePreset.Blob47)).Select(i => Shape(AutoTilePreset.Blob47, i)).ToList();
var ids = shapes.Select(shape => oldIds.TryGetValue(shape, out int id) ? id : nextId++).ToList();
var images = shapes.Select((shape, i) => (Id: ids[i], Image: Draw(shape))).ToDictionary(x => x.Id, x => x.Image);
var plotTiles = shapes.Select((shape, i) => template with
{
    Id = ids[i], Name = $"밭 연결 {i}", Image = images[ids[i]], DrawOffset = Point.Empty, PaletteX = i % 8, PaletteY = row + i / 8
}).ToList();
var newSet = plot with { Rules = [.. shapes.Select((shape, i) => Rule(AutoTilePreset.Blob47, shape) with { Tiles = [new AutoTileChoice(ids[i])] })] };
var redrawn = ts with
{
    Tiles = [.. ts.Tiles.Where(t => !images.ContainsKey(t.Id)), .. plotTiles],
    AutoTiles = [.. ts.AutoTiles.Select(a => a.Id == plot.Id ? newSet : a)],
};
plot = newSet;

if (preview is not null)
{
    var cells = new List<Point>();
    for (int y = 0; y < 3; y++) for (int x = 0; x < 4; x++) cells.Add(new(x, y));
    for (int x = 5; x < 10; x++) { cells.Add(new(x, 0)); cells.Add(new(x, 4)); }
    for (int y = 1; y < 4; y++) { cells.Add(new(5, y)); cells.Add(new(9, y)); }
    cells.AddRange([new(11, 0), new(13, 0), new(14, 0), new(15, 0), new(11, 2), new(11, 3), new(11, 4), new(0, 5), new(1, 5), new(1, 6)]);
    var sample = AutoTiler.Paint(redrawn, TileMap.Empty, plot, cells);
    PngIO.Save(TileMapRenderer.RenderArea(redrawn, sample, Point.Empty, new Rectangle(0, 0, 16 * 16, 16 * 7)), Path.GetFullPath(preview));
    Console.WriteLine($"Preview: {Path.GetFullPath(preview)}");
    return;
}

if (File.Exists(backup)) throw new IOException("Plot tiles were already redrawn; refusing to run twice.");
File.Copy(path, backup);
File.Copy(ts.SourcePath!, Path.ChangeExtension(backup, Tileset.FileExtension));
project.EditTileset("Redraw plot tiles", redrawn);
ProjectIO.Save(project, path);
var check = ProjectIO.Load(path).Tilesets[ts.Key];
if (images.Any(kv => !check.Find(kv.Key)!.Image.ContentEquals(kv.Value))) throw new InvalidDataException($"Plot tiles did not save; original at {backup}.");
Console.WriteLine($"Redrew {images.Count} plot tiles. Original: {backup}");
