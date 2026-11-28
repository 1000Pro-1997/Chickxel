using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App.Ai;

/// <summary>
/// 타일맵 도구: 타일맵 레이어 읽기(get_tilemap), 칸에 타일 놓기·지우기(set_tiles), 자동 타일 칠하기(paint_auto_tile), 영역 비우기(clear_tiles),
/// 층 설정(set_tile_floor), 타일맵 레이어 더하기(add_tilemap_layer)·레이어 지우기(delete_layer), 타일셋 타일 더하기·고치기·지우기(add_tile·set_tile·delete_tile).
/// 일반 층 좌표는 칸, 자유 층(<see cref="TilemapData.IsFreeFloor"/>) 좌표는 픽셀(격자 원점 기준). 바꾸는 호출은 실행취소 한 단계, 잠긴 레이어·층은 고치지 않는다.
/// </summary>
public sealed partial class PixelTools
{
    /// <summary>한 번에 고치거나 돌려주는 칸 수.</summary>
    private const int MaxTileCells = 4000;

    private const string TileCoordNote = "Grid floors use cell coordinates (cell (0,0) = top-left cell at the tilemap origin); free floors (free = true in get_tilemap) use pixel " +
                                         "coordinates of the tile's anchor relative to the origin.";

    private static IEnumerable<JsonObject> TilemapToolDefinitions() =>
    [
        Tool("get_tilemap",
            "Read a tilemap layer as JSON: cell size, origin, canvas size in cells, tilesets, floors [{id,name,free,hidden,locked,count}], autoTiles [{id,name,floor,corner}] " +
            "(corner = corner-grid set: rounded terrain drawn between cells), and placed cells [[floor,x,y,tile,autoTile,transform]] inside the region (autoTile 0 = a fixed tile; " +
            "transform bits 1 = flip x, 2 = flip y, 4 = rotate 90°). With include_tiles also tiles [{id,name,floor,w,h (cells),frames,drawClass}] (ground/sorted/overhead). " +
            TileCoordNote + " Look at the result with get_image. At most 4000 cells are listed (truncated = true if more).",
            new JsonObject
            {
                ["layer"] = Int("Tilemap layer index. Default: current layer.", 0),
                ["x"] = Int("Region left edge in cells (default: the whole canvas)."),
                ["y"] = Int("Region top edge in cells."),
                ["width"] = Int("Region width in cells.", 1),
                ["height"] = Int("Region height in cells.", 1),
                ["floor"] = Int("Only this floor id."),
                ["include_tiles"] = Bool("Also list the tileset's tiles (optionally only on `floor`)."),
                ["include_cells"] = Bool("List placed cells (default true)."),
            }),
        Tool("set_tiles",
            "Place or erase individual tiles on a tilemap layer (one undo step). Each cell: {x, y, tile (tile id; 0 = erase), floor (default: the tile's floor; required to erase), " +
            "flip_x, flip_y, rotate (quarter turns 0-3)}. Placed tiles are fixed (not auto tiles); neighboring auto tiles on grid floors reconnect. A big tile covers its whole " +
            "footprint and replaces tiles it overlaps on that floor. " + TileCoordNote,
            new JsonObject
            {
                ["layer"] = Int("Tilemap layer index. Default: current layer.", 0),
                ["cells"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" }, ["description"] = "Cells to set (at most 4000)." },
            },
            "cells"),
        Tool("paint_auto_tile",
            "Paint or erase with an auto tile set on grid cells (one undo step): the cells and their neighbors pick connected shapes, painting may clear other ground floors the " +
            "set replaces. Give `cells` [[x,y],...] and/or a rectangle x/y/width/height in cells. Not for free floors.",
            new JsonObject
            {
                ["layer"] = Int("Tilemap layer index. Default: current layer.", 0),
                ["set"] = new JsonObject { ["type"] = new JsonArray("integer", "string"), ["description"] = "Auto tile set id or exact name (see get_tilemap autoTiles)." },
                ["cells"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "array" }, ["description"] = "Cells [[x,y],...]." },
                ["x"] = Int("Rectangle left in cells."),
                ["y"] = Int("Rectangle top in cells."),
                ["width"] = Int("Rectangle width in cells.", 1),
                ["height"] = Int("Rectangle height in cells.", 1),
                ["erase"] = Bool("Erase this set's tiles instead of painting."),
            },
            "set"),
        Tool("clear_tiles",
            "Remove every tile whose anchor lies in a rectangle (in cells; free floors: anchors inside the same area in pixels), on one floor or all unlocked floors " +
            "(one undo step). Default rectangle: everything.",
            new JsonObject
            {
                ["layer"] = Int("Tilemap layer index. Default: current layer.", 0),
                ["x"] = Int("Rectangle left in cells."),
                ["y"] = Int("Rectangle top in cells."),
                ["width"] = Int("Rectangle width in cells.", 1),
                ["height"] = Int("Rectangle height in cells.", 1),
                ["floor"] = Int("Only this floor id."),
            }),
        Tool("set_tile_floor",
            "Change one floor of a tilemap layer (one undo step): free = place tiles at pixel coordinates (switching converts existing anchors; going back to the grid snaps " +
            "them to the nearest cells and drops overlaps), hidden, locked.",
            new JsonObject
            {
                ["layer"] = Int("Tilemap layer index. Default: current layer.", 0),
                ["floor"] = Int("Floor id."),
                ["free"] = Bool("Free floor (pixel coordinates) or grid floor."),
                ["hidden"] = Bool("Hide the floor."),
                ["locked"] = Bool("Lock the floor (tools never change locked floors)."),
            },
            "floor"),
        Tool("add_tilemap_layer",
            "Add a tilemap layer above the current layer and make it current. tileset: \"village\" (built-in top-down village with terrain auto tiles), \"sandbox\" " +
            "(built-in side view), or the key or name of a tileset already in the project (see get_tilemap on another tilemap layer).",
            new JsonObject
            {
                ["tileset"] = Str("Tileset to use."),
                ["name"] = Str("Layer name (optional)."),
                ["position"] = Int("Layer index to insert at (default: above the current layer).", 0),
            },
            "tileset"),
        Tool("delete_layer",
            "Delete a layer with its child layers (one undo step). Locked layers are kept; the last layer can't be deleted.",
            new JsonObject { ["layer"] = Int("Layer index.", 0) },
            "layer"),
        Tool("add_tile",
            "Add a tile to the first tileset of a tilemap layer, cut from a canvas region (one undo step): x/y/width/height in canvas pixels of `frame` and `source_layer` " +
            "(default: the visible composite). The picture sits at the bottom of its footprint (cells_w × cells_h, default: the picture size rounded up to cells), so tall " +
            "pictures rise above it. Returns the new tile id.",
            new JsonObject
            {
                ["layer"] = Int("Tilemap layer index. Default: current layer.", 0),
                ["floor"] = Int("Floor id the tile belongs to (default: the first floor)."),
                ["name"] = Str("Tile name."),
                ["frame"] = FrameProp(),
                ["source_layer"] = Int("Layer index to copy from (default: visible composite).", 0),
                ["x"] = Int("Region left in canvas pixels."),
                ["y"] = Int("Region top in canvas pixels."),
                ["width"] = Int("Region width in pixels.", 1),
                ["height"] = Int("Region height in pixels.", 1),
                ["cells_w"] = Int("Footprint width in cells.", 1, Tileset.MaxFootprint),
                ["cells_h"] = Int("Footprint height in cells.", 1, Tileset.MaxFootprint),
                ["draw_class"] = Enum("Draw group: ground (floor), sorted (stands up; sorted by its base line), overhead (always on top).", "ground", "sorted", "overhead"),
            },
            "x", "y", "width", "height"),
        Tool("set_tile",
            "Change a tile of the first tileset of a tilemap layer (one undo step): name, floor, draw_class, and/or its picture from a canvas region (same arguments as add_tile). " +
            "Every map using the tile shows the change.",
            new JsonObject
            {
                ["layer"] = Int("Tilemap layer index. Default: current layer.", 0),
                ["tile"] = Int("Tile id.", 1),
                ["name"] = Str("New name."),
                ["floor"] = Int("New floor id."),
                ["draw_class"] = Enum("New draw group.", "ground", "sorted", "overhead"),
                ["frame"] = FrameProp(),
                ["source_layer"] = Int("Layer index to copy the new picture from (default: visible composite).", 0),
                ["x"] = Int("New picture: region left in canvas pixels."),
                ["y"] = Int("New picture: region top in canvas pixels."),
                ["width"] = Int("New picture: region width in pixels.", 1),
                ["height"] = Int("New picture: region height in pixels.", 1),
            },
            "tile"),
        Tool("delete_tile",
            "Delete a tile from the first tileset of a tilemap layer (one undo step): every placed copy disappears from all maps using the tileset and auto tile rules drop it.",
            new JsonObject
            {
                ["layer"] = Int("Tilemap layer index. Default: current layer.", 0),
                ["tile"] = Int("Tile id.", 1),
            },
            "tile"),
    ];

    private readonly record struct TileTarget(SpriteProject Project, int Index, Layer Layer, TilemapData Data, Tileset Tileset);

    /// <summary>타일맵 도구의 대상 레이어(<c>layer</c>, 없으면 지금 레이어). 바꾸는 도구면 그리는 중·잠김을 검사한다.</summary>
    private TileTarget TilemapTarget(JsonObject? a, bool mutate)
    {
        var p = mutate ? BeginMutation() : RequireProject();
        int index = LayerIndex(a);
        var layer = p.Layers[index];
        if (!layer.IsTilemap || layer.Tiles is not { } data) throw new ToolError(UiStrings.FormatAi_Tile_NotTilemap(index: index, name: layer.Name));
        if (mutate) RequireUnlocked(p, index);
        var ts = p.TilesetOf(layer) ?? throw new ToolError(UiStrings.Tile_NoTileset);
        return new TileTarget(p, index, layer, data, ts);
    }

    private static void RequireFloor(TileTarget t, int floor, bool unlocked)
    {
        if (t.Tileset.FloorOrder(floor) < 0) throw new ToolError(UiStrings.FormatAi_Tile_UnknownFloor(floor: floor));
        if (unlocked && t.Data.IsFloorLocked(floor)) throw new ToolError(UiStrings.Tile_FloorLocked);
    }

    /// <summary>격자 층만 자동 타일 이웃으로 보는 거르개(자유 층이 없으면 null).</summary>
    private static Func<int, bool>? GridFloor(TilemapData data)
        => data.FreePlacement || !data.FreeFloors.IsEmpty ? f => !data.IsFreeFloor(f) : null;

    /// <summary>칸 사각형 인자(x, y, width, height; 없으면 null).</summary>
    private static Rectangle? CellRect(JsonObject? a)
    {
        int? x = OptInt(a, "x", -CoordLimit, CoordLimit), y = OptInt(a, "y", -CoordLimit, CoordLimit);
        int? w = OptInt(a, "width", 1, CoordLimit), h = OptInt(a, "height", 1, CoordLimit);
        if (x is null && y is null && w is null && h is null) return null;
        return new Rectangle(x ?? 0, y ?? 0, w ?? 1, h ?? 1);
    }

    /// <summary>캔버스를 덮는 칸 사각형.</summary>
    private static Rectangle CanvasCells(SpriteProject p, TilemapData data, Tileset ts)
    {
        int x0 = FloorDiv(-data.Origin.X, ts.CellWidth), y0 = FloorDiv(-data.Origin.Y, ts.CellHeight);
        int x1 = FloorDiv(p.Width - 1 - data.Origin.X, ts.CellWidth), y1 = FloorDiv(p.Height - 1 - data.Origin.Y, ts.CellHeight);
        return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    /// <summary>칸 사각형 안에 기준점이 있는지(자유 층은 같은 넓이의 픽셀 사각형).</summary>
    private static bool InCells(Rectangle cells, Tileset ts, bool free, Point at)
        => free ? new Rectangle(cells.X * ts.CellWidth, cells.Y * ts.CellHeight, cells.Width * ts.CellWidth, cells.Height * ts.CellHeight).Contains(at) : cells.Contains(at);

    private static string DrawClassName(TileDrawClass c) => c switch { TileDrawClass.Sorted => "sorted", TileDrawClass.Overhead => "overhead", _ => "ground" };

    private static TileDrawClass DrawClassOf(string name) => name switch { "sorted" => TileDrawClass.Sorted, "overhead" => TileDrawClass.Overhead, _ => TileDrawClass.Ground };

    private JsonObject GetTilemap(JsonObject? a)
    {
        var t = TilemapTarget(a, mutate: false);
        var (p, data, ts) = (t.Project, t.Data, t.Tileset);
        int? onlyFloor = OptInt(a, "floor");
        if (onlyFloor is { } of) RequireFloor(t, of, unlocked: false);
        var region = CellRect(a) ?? CanvasCells(p, data, ts);
        var o = new JsonObject
        {
            ["layer"] = t.Index, ["name"] = t.Layer.Name,
            ["cellWidth"] = ts.CellWidth, ["cellHeight"] = ts.CellHeight,
            ["origin"] = new JsonArray(data.Origin.X, data.Origin.Y),
            ["canvasCells"] = JsonRect(CanvasCells(p, data, ts)),
            ["region"] = JsonRect(region),
            ["tilesets"] = new JsonArray([.. data.TilesetKeys.Select((k, slot) => (JsonNode)new JsonObject
            {
                ["slot"] = slot, ["key"] = k, ["name"] = p.Tilesets.TryGetValue(k, out var s) ? s.Name : null,
            })]),
            ["floors"] = new JsonArray([.. ts.Floors.Select(f => (JsonNode)new JsonObject
            {
                ["id"] = f.Id, ["name"] = f.Name, ["free"] = data.IsFreeFloor(f.Id), ["hidden"] = data.IsFloorHidden(f.Id),
                ["locked"] = data.IsFloorLocked(f.Id), ["count"] = data.Map.Floor(f.Id).Count,
            })]),
            ["autoTiles"] = new JsonArray([.. ts.AutoTiles.Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s.Id, ["name"] = s.Name, ["floor"] = s.Floor, ["corner"] = s.IsCorner,
            })]),
        };
        if (OptBool(a, "include_tiles") == true)
            o["tiles"] = new JsonArray([.. ts.Tiles.Where(d => onlyFloor is null || d.Floor == onlyFloor).Select(d => (JsonNode)new JsonObject
            {
                ["id"] = d.Id, ["name"] = d.Name, ["floor"] = d.Floor, ["w"] = d.Width, ["h"] = d.Height, ["frames"] = d.FrameCount, ["drawClass"] = DrawClassName(d.DrawClass),
            })]);
        if (OptBool(a, "include_cells") != false)
        {
            var cells = new JsonArray();
            bool truncated = false;
            foreach (var floor in ts.Floors)
            {
                if (onlyFloor is { } only && floor.Id != only) continue;
                bool free = data.IsFreeFloor(floor.Id);
                foreach (var (at, cell) in data.Map.Floor(floor.Id).OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
                {
                    if (!InCells(region, ts, free, at)) continue;
                    if (cells.Count >= MaxTileCells) { truncated = true; break; }
                    cells.Add(new JsonArray(floor.Id, at.X, at.Y, cell.Tile, cell.AutoTile, (int)cell.Transform));
                }
            }
            o["cells"] = cells;
            if (truncated) o["truncated"] = true;
        }
        return TextResult(o.ToJsonString(JsonOptions));
    }

    private static JsonArray JsonRect(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    private JsonObject SetTiles(string tool, JsonObject? a)
    {
        var t = TilemapTarget(a, mutate: true);
        var (data, ts) = (t.Data, t.Tileset);
        var items = ReqArray(a, "cells");
        if (items.Count > MaxTileCells) throw new ToolError(UiStrings.FormatAi_Tile_TooMany(max: MaxTileCells));
        var map = data.Map;
        var touched = new List<Point>();
        int placed = 0, erased = 0;
        foreach (var node in items)
        {
            var c = node as JsonObject ?? throw new ToolError(UiStrings.FormatAi_Tile_UnknownTile(tile: node?.ToJsonString() ?? "null"));
            var at = new Point(ReqInt(c, "x", -CoordLimit, CoordLimit), ReqInt(c, "y", -CoordLimit, CoordLimit));
            int tile = OptInt(c, "tile", 0) ?? 0;
            var def = tile > 0 ? ts.Find(tile) ?? throw new ToolError(UiStrings.FormatAi_Tile_UnknownTile(tile: tile)) : null;
            int floor = OptInt(c, "floor") ?? def?.Floor ?? throw new ToolError(UiStrings.Ai_Tile_EraseNeedsFloor);
            RequireFloor(t, floor, unlocked: true);
            bool free = data.IsFreeFloor(floor);
            if (def is null)
            {
                if (free)
                {
                    if (map.Get(floor, at) is null) continue;
                    map = map.With(floor, at, null);
                }
                else
                {
                    var (next, removed) = map.Erase(ts, at, floor);
                    if (removed is null) continue;
                    map = next;
                    touched.Add(removed.Value.At);
                }
                erased++;
                continue;
            }
            var transform = (OptBool(c, "flip_x") == true ? TileTransform.FlipX : 0) | (OptBool(c, "flip_y") == true ? TileTransform.FlipY : 0);
            int turns = OptInt(c, "rotate", 0, 3) ?? 0;
            for (int i = 0; i < turns; i++) transform = transform.Then(TileTransform.Rotate90);
            var cell = new TileCell(tile, ts.Allowed(transform));
            map = free ? map.With(floor, at, cell) : map.Place(ts, floor, at, cell);
            if (!free) touched.Add(at);
            placed++;
        }
        if (touched.Count > 0) map = AutoTiler.RefreshAround(ts, map, touched, onlyFilled: false, gridFloor: GridFloor(data));
        if (ReferenceEquals(map, data.Map)) return TextResult($"{tool}: nothing changed on layer {t.Index}");
        t.Project.SetTileMap(t.Layer, map, Label(tool));
        Ctx.SetCurrentLayer(t.Index);
        return TextResult($"{tool}: placed {placed}, erased {erased} on layer {t.Index} \"{t.Layer.Name}\"");
    }

    private JsonObject PaintAutoTile(string tool, JsonObject? a)
    {
        var t = TilemapTarget(a, mutate: true);
        var (data, ts) = (t.Data, t.Tileset);
        var key = Arg(a, "set");
        var set = key is not null && TryInt(key, out int id) ? ts.FindAutoTile(id)
            : AsString(key) is { } name ? ts.AutoTiles.FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.Ordinal)) : null;
        if (set is null) throw new ToolError(UiStrings.FormatAi_Tile_UnknownSet(set: key?.ToJsonString() ?? ""));
        RequireFloor(t, set.Floor, unlocked: true);
        if (data.IsFreeFloor(set.Floor)) throw new ToolError(UiStrings.FormatAi_Tile_FreeFloorAuto(floor: set.Floor));
        var cells = new List<Point>();
        if (Arg(a, "cells") is JsonArray list)
            foreach (var n in list)
                if (n is JsonArray { Count: 2 } xy && TryInt(xy[0]!, out int cx) && TryInt(xy[1]!, out int cy)) cells.Add(new Point(cx, cy));
        if (CellRect(a) is { } r)
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++) cells.Add(new Point(x, y));
        if (cells.Count == 0) throw new ToolError(UiStrings.Ai_Tile_NothingToChange);
        if (cells.Count > MaxTileCells) throw new ToolError(UiStrings.FormatAi_Tile_TooMany(max: MaxTileCells));
        var grid = GridFloor(data);
        var map = OptBool(a, "erase") == true
            ? AutoTiler.Erase(ts, data.Map, set, cells, gridFloor: grid)
            : AutoTiler.Paint(ts, data.Map, set, cells, f => !data.IsFloorLocked(f), gridFloor: grid);
        if (ReferenceEquals(map, data.Map)) return TextResult($"{tool}: nothing changed on layer {t.Index}");
        t.Project.SetTileMap(t.Layer, map, Label(tool));
        Ctx.SetCurrentLayer(t.Index);
        return TextResult($"{tool}: {(OptBool(a, "erase") == true ? "erased" : "painted")} \"{set.Name}\" on {cells.Distinct().Count()} cells of layer {t.Index}");
    }

    private JsonObject ClearTiles(string tool, JsonObject? a)
    {
        var t = TilemapTarget(a, mutate: true);
        var (data, ts) = (t.Data, t.Tileset);
        int? only = OptInt(a, "floor");
        if (only is { } of) RequireFloor(t, of, unlocked: true);
        var region = CellRect(a);
        var map = data.Map;
        var touched = new List<Point>();
        int removed = 0;
        foreach (var floor in ts.Floors)
        {
            if (only is { } o && floor.Id != o || data.IsFloorLocked(floor.Id)) continue;
            bool free = data.IsFreeFloor(floor.Id);
            foreach (var at in map.Floor(floor.Id).Keys.Where(at => region is not { } r || InCells(r, ts, free, at)).ToList())
            {
                map = map.With(floor.Id, at, null);
                if (!free) touched.Add(at);
                removed++;
            }
        }
        if (removed == 0) return TextResult($"{tool}: nothing to remove on layer {t.Index}");
        if (touched.Count > 0) map = AutoTiler.RefreshAround(ts, map, touched, onlyFilled: false, gridFloor: GridFloor(data));
        t.Project.SetTileMap(t.Layer, map, Label(tool));
        return TextResult($"{tool}: removed {removed} tiles from layer {t.Index}");
    }

    private JsonObject SetTileFloor(string tool, JsonObject? a)
    {
        var t = TilemapTarget(a, mutate: true);
        int floor = ReqInt(a, "floor");
        RequireFloor(t, floor, unlocked: false);
        bool? free = OptBool(a, "free"), hidden = OptBool(a, "hidden"), locked = OptBool(a, "locked");
        if (free is null && hidden is null && locked is null) throw new ToolError(UiStrings.Ai_Tile_NothingToChange);
        // 잠긴 층은 풀기만 할 수 있다(자유/격자 바꾸기는 잠금을 푼 뒤)
        if (free is not null && t.Data.IsFloorLocked(floor) && locked != false) throw new ToolError(UiStrings.Tile_FloorLocked);
        var p = t.Project;
        p.Edit(Label(tool), ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            if (locked == false || hidden is not null) p.SetTileFloorState(t.Layer, floor, hidden, locked == false ? false : null);
            if (free is { } f) p.SetTileFloorFreePlacement(t.Layer, floor, f);
            if (locked == true) p.SetTileFloorState(t.Layer, floor, null, true);
        });
        var d = t.Layer.Tiles!;
        return TextResult($"{tool}: floor {floor} of layer {t.Index} — free {d.IsFreeFloor(floor)}, hidden {d.IsFloorHidden(floor)}, locked {d.IsFloorLocked(floor)}");
    }

    private JsonObject AddTilemapLayer(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        var name = ReqString(a, "tileset").Trim();
        var tileset = BuiltInTilesets.All.FirstOrDefault(e => string.Equals(e.Id, name, StringComparison.OrdinalIgnoreCase))?.Create()
            ?? (p.Tilesets.TryGetValue(name, out var byKey) ? byKey : p.AllTilesets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal)))
            ?? throw new ToolError(UiStrings.FormatAi_Tile_UnknownTileset(tileset: name));
        var layerName = OptString(a, "name");
        int? position = OptInt(a, "position", 0, p.Layers.Count);
        var layer = p.Edit<Layer>(Label(tool), ProjectChange.Layers | ProjectChange.Pixels,
            () => p.AddTilemapLayer(tileset, position, string.IsNullOrWhiteSpace(layerName) ? null : layerName.Trim()));
        int index = p.IndexOf(layer);
        Ctx.SetCurrentLayer(index);
        return TextResult($"{tool}: added tilemap layer {index} \"{layer.Name}\" with tileset \"{tileset.Name}\" ({tileset.CellWidth}×{tileset.CellHeight}px cells); it is now the current layer");
    }

    private JsonObject DeleteLayer(string tool, JsonObject? a)
    {
        var p = BeginMutation();
        int index = ReqIndex(a, "layer", p.Layers.Count, UiStrings.S_PixelTools_OptLayer_44CE7E7287);
        RequireUnlocked(p, index);
        var layer = p.Layers[index];
        foreach (var child in p.DescendantsOf(layer))
            RequireUnlocked(p, p.IndexOf(child));
        if (p.Layers.Count - p.DescendantsOf(layer).Count <= 1) throw new ToolError(UiStrings.Ai_Layer_CannotDeleteLast);
        p.Edit(Label(tool), ProjectChange.Layers | ProjectChange.Pixels, () => p.DeleteLayer(index));
        Ctx.SetCurrentLayer(Math.Clamp(index - 1, 0, p.Layers.Count - 1));
        return TextResult($"{tool}: deleted layer {index} \"{layer.Name}\"; {p.Layers.Count} layers left");
    }

    /// <summary>타일 그림 인자(frame, source_layer, x, y, width, height)의 캔버스 영역 그림. 영역이 없으면 null.</summary>
    private PixelImage? TileImageArg(SpriteProject p, JsonObject? a)
    {
        if (Arg(a, "x") is null && Arg(a, "y") is null && Arg(a, "width") is null && Arg(a, "height") is null) return null;
        int frame = Frame(a);
        int? source = OptIndex(a, "source_layer", p.Layers.Count, UiStrings.S_PixelTools_OptLayer_44CE7E7287);
        var image = Source(p, frame, source);
        var region = Region(a, image.Width, image.Height);
        return image.Crop(region);
    }

    /// <summary>레이어의 첫 타일셋(자리 0 — 그 타일 Id는 합친 Id와 같다).</summary>
    private static Tileset PrimaryTileset(TileTarget t)
        => t.Project.Tilesets.TryGetValue(t.Data.PrimaryTilesetKey, out var ts) ? ts : throw new ToolError(UiStrings.Tile_NoTileset);

    private JsonObject AddTile(string tool, JsonObject? a)
    {
        var t = TilemapTarget(a, mutate: true);
        var ts = PrimaryTileset(t);
        var image = TileImageArg(t.Project, a) ?? throw new ToolError(UiStrings.Ai_Tile_NothingToChange);
        int floor = OptInt(a, "floor") ?? (ts.Floors.Length > 0 ? ts.Floors[0].Id : 0);
        if (ts.Floors.Length > 0 && ts.FloorOrder(floor) < 0) throw new ToolError(UiStrings.FormatAi_Tile_UnknownFloor(floor: floor));
        int cw = OptInt(a, "cells_w", 1, Tileset.MaxFootprint) ?? Math.Clamp((image.Width + ts.CellWidth - 1) / ts.CellWidth, 1, Tileset.MaxFootprint);
        int ch = OptInt(a, "cells_h", 1, Tileset.MaxFootprint) ?? Math.Clamp((image.Height + ts.CellHeight - 1) / ts.CellHeight, 1, Tileset.MaxFootprint);
        int id = ts.NextTileId;
        var (px, py) = FreePaletteSpot(ts);
        var def = new TileDef
        {
            Id = id, Name = OptString(a, "name")?.Trim() ?? "", Image = image, Width = cw, Height = ch, Floor = floor,
            DrawOffset = TileDef.BottomAligned(image.Height, ch, ts.CellHeight),
            DrawClass = DrawClassOf(OptEnum(a, "draw_class", "ground", "sorted", "overhead") ?? "ground"), PaletteX = px, PaletteY = py,
        };
        t.Project.EditTileset(Label(tool), ts with { Tiles = [.. ts.Tiles, def] });
        return TextResult($"{tool}: added tile {id} \"{def.Name}\" ({image.Width}×{image.Height}px, {cw}×{ch} cells, floor {floor}) to tileset \"{ts.Name}\"");
    }

    /// <summary>타일셋 목록에서 비어 있는 첫 자리(한 줄 8칸).</summary>
    private static (int X, int Y) FreePaletteSpot(Tileset ts)
    {
        var used = new HashSet<(int, int)>();
        foreach (var d in ts.Tiles)
            for (int y = 0; y < d.Height; y++)
                for (int x = 0; x < d.Width; x++) used.Add((d.PaletteX + x, d.PaletteY + y));
        for (int y = 0; ; y++)
            for (int x = 0; x < 8; x++)
                if (!used.Contains((x, y))) return (x, y);
    }

    private JsonObject SetTile(string tool, JsonObject? a)
    {
        var t = TilemapTarget(a, mutate: true);
        var ts = PrimaryTileset(t);
        int id = ReqInt(a, "tile", 1);
        var def = ts.Find(id) ?? throw new ToolError(t.Tileset.Find(id) is null ? UiStrings.FormatAi_Tile_UnknownTile(tile: id) : UiStrings.FormatAi_Tile_NotPrimary(tile: id));
        var name = OptString(a, "name");
        int? floor = OptInt(a, "floor");
        if (floor is { } f && ts.FloorOrder(f) < 0) throw new ToolError(UiStrings.FormatAi_Tile_UnknownFloor(floor: f));
        var drawClass = OptEnum(a, "draw_class", "ground", "sorted", "overhead");
        var image = TileImageArg(t.Project, a);
        if (name is null && floor is null && drawClass is null && image is null) throw new ToolError(UiStrings.Ai_Tile_NothingToChange);
        var next = def with
        {
            Name = name?.Trim() ?? def.Name,
            Floor = floor ?? def.Floor,
            DrawClass = drawClass is null ? def.DrawClass : DrawClassOf(drawClass),
            Image = image ?? def.Image,
            Frames = image is null ? def.Frames : [],
            DrawOffset = image is null ? def.DrawOffset : TileDef.BottomAligned(image.Height, def.Height, ts.CellHeight),
        };
        t.Project.EditTileset(Label(tool), ts with { Tiles = [.. ts.Tiles.Select(d => d.Id == id ? next : d)] });
        return TextResult($"{tool}: tile {id} \"{next.Name}\" — floor {next.Floor}, {DrawClassName(next.DrawClass)}, {next.Image.Width}×{next.Image.Height}px");
    }

    private JsonObject DeleteTile(string tool, JsonObject? a)
    {
        var t = TilemapTarget(a, mutate: true);
        var ts = PrimaryTileset(t);
        int id = ReqInt(a, "tile", 1);
        if (ts.Find(id) is null) throw new ToolError(t.Tileset.Find(id) is null ? UiStrings.FormatAi_Tile_UnknownTile(tile: id) : UiStrings.FormatAi_Tile_NotPrimary(tile: id));
        var next = ts with
        {
            Tiles = [.. ts.Tiles.Where(d => d.Id != id)],
            AutoTiles = [.. ts.AutoTiles.Select(s => s with
            {
                Rules = [.. s.Rules.Select(r => r with { Tiles = [.. r.Tiles.Where(c => c.Tile != id)] })],
                CornerTiles = s.IsCorner ? [.. s.CornerTiles.Select(c => c == id ? 0 : c)] : s.CornerTiles,
            })],
        };
        int removed = 0;
        t.Project.EditTileset(Label(tool), next, (map, offset) =>
        {
            var keep = map.All.Where(c => c.Cell.Tile != offset + id).ToList();
            removed += map.Count - keep.Count;
            return keep.Count == map.Count ? map : TileMap.FromCells(keep);
        });
        return TextResult($"{tool}: deleted tile {id} from tileset \"{ts.Name}\" and {removed} placed copies");
    }
}
