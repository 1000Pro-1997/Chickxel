using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;
using static Chickxel.Core.Tiles.AutoTileShapes;

// One-time local redraw of the unified village's 16px fence auto tiles. The first draft cut 16px windows out of
// the 10px-pitch source strips, which left half posts and stray rails at cell edges. These tiles are drawn with the
// source fence's own pixel grammar (5px post, 2px cap, two 3px rails) so neighbouring cells meet exactly.
// "--preview <png>" only writes a sample layout and leaves the map untouched.
string folder = Path.GetFullPath(args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "../CHICKXEL_Village");
string? preview = args.SkipWhile(a => a != "--preview").Skip(1).FirstOrDefault();
string path = Path.Combine(folder, "village-unified.chickxel");
string backup = Path.Combine(folder, "village-unified.before-fence.chickxel");
var warnings = new List<string>();
var project = ProjectIO.Load(path, warnings);
if (warnings.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, warnings));
var layer = project.Layers.Single(l => l.IsTilemap);
var ts = project.Tilesets[layer.Tiles!.PrimaryTilesetKey];
var fence = ts.AutoTiles.Single(a => a.Name == "울타리 (16픽셀 연결)");
if (ts.CellWidth != 16 || ts.CellHeight != 16 || fence.Rules.Length != 16 || fence.Rules.Any(r => r.Tiles.Length != 1))
    throw new InvalidDataException("Unexpected fence auto-tile set.");

var source = TilesetIO.Load(Path.Combine(folder, "props.chickxel-tileset")).Find(23)?.Image
    ?? throw new InvalidDataException("Missing source fence strip.");
Color32 outline = source[0, 0], cap = source[1, 1], light = source[1, 3], dark = source[2, 3];
if (new[] { outline, cap, light, dark }.Distinct().Count() != 4 || source[5, 4] != light)
    throw new InvalidDataException("Source fence colors changed; re-check the sampling points.");

PixelImage Draw(byte shape)
{
    var img = new PixelImage(16, 16);
    void Rail(int x0, int x1)
    {
        foreach (int top in (int[])[5, 10])
        {
            img.FillRect(new Rectangle(x0, top, x1 - x0, 1), outline);
            img.FillRect(new Rectangle(x0, top + 1, x1 - x0, 1), light);
            img.FillRect(new Rectangle(x0, top + 2, x1 - x0, 1), outline);
        }
    }
    void PostRow(int y, Color32 a, Color32 b, Color32 c)
    {
        img[5, y] = outline;
        img[6, y] = a;
        img[7, y] = b;
        img[8, y] = c;
        img[9, y] = outline;
    }
    void Body(int y) => PostRow(y, light, dark, dark);
    if ((shape & W) != 0) Rail(0, 5);
    if ((shape & E) != 0) Rail(10, 16);
    // A north link continues the post above; a south link runs down to the next cell's post.
    if ((shape & N) != 0) { Body(0); Body(1); }
    img.FillRect(new Rectangle(5, 2, 5, 1), outline);
    PostRow(3, cap, cap, cap);
    PostRow(4, cap, cap, cap);
    for (int y = 5; y <= 13; y++) Body(y);
    if ((shape & S) != 0) { Body(14); Body(15); }
    else img.FillRect(new Rectangle(5, 14, 5, 1), outline);
    return img;
}

var images = fence.Rules.ToDictionary(r => r.Tiles[0].Tile, r => Draw(r.Same));
var redrawn = ts with
{
    Tiles = [.. ts.Tiles.Select(t => images.TryGetValue(t.Id, out var image) ? t with { Image = image, DrawOffset = Point.Empty } : t)]
};

if (preview is not null)
{
    // Rectangle loop, a tee, a cross, a straight run and a lone post.
    var cells = new List<Point>();
    for (int x = 0; x < 6; x++) { cells.Add(new(x, 0)); cells.Add(new(x, 4)); }
    for (int y = 1; y < 4; y++) { cells.Add(new(0, y)); cells.Add(new(5, y)); }
    cells.AddRange([new(3, 1), new(3, 2), new(8, 0), new(8, 1), new(8, 2), new(7, 1), new(9, 1), new(11, 2), new(12, 2), new(13, 2), new(11, 4)]);
    var sample = AutoTiler.Paint(redrawn, TileMap.Empty, fence, cells);
    PngIO.Save(TileMapRenderer.RenderArea(redrawn, sample, Point.Empty, new Rectangle(0, 0, 16 * 14, 16 * 5)), Path.GetFullPath(preview));
    Console.WriteLine($"Preview: {Path.GetFullPath(preview)}");
    return;
}

if (File.Exists(backup)) throw new IOException("Fence tiles were already redrawn; refusing to run twice.");
File.Copy(path, backup);
File.Copy(ts.SourcePath!, Path.ChangeExtension(backup, Tileset.FileExtension));
project.EditTileset("Redraw fence tiles", redrawn);
ProjectIO.Save(project, path);
var check = ProjectIO.Load(path).Tilesets[ts.Key];
if (images.Any(kv => !check.Find(kv.Key)!.Image.ContentEquals(kv.Value))) throw new InvalidDataException($"Fence tiles did not save; original at {backup}.");
Console.WriteLine($"Redrew {images.Count} fence tiles. Original: {backup}");
