using System.Collections.Immutable;
using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tiles;

/// <summary>타일셋 고치기(새 값을 돌려줌): 시트에서 가져오기, 큰 타일로 합치기, 타일·층·자동 타일 세트 더하기·바꾸기·지우기.</summary>
public static class TilesetEdit
{
    /// <summary>타일 목록 화면에서 비어 있는 자리(위 줄부터, 한 줄 <paramref name="columns"/>칸).</summary>
    public static Point NextFreeSlot(Tileset ts, int columns = 8)
    {
        var used = ts.Tiles.Select(t => new Point(t.PaletteX, t.PaletteY)).ToHashSet();
        columns = Math.Max(1, Math.Max(columns, ts.Tiles.Length == 0 ? 1 : ts.Tiles.Max(t => t.PaletteX) + 1));
        for (int i = 0; ; i++)
        {
            var p = new Point(i % columns, i / columns);
            if (!used.Contains(p)) return p;
        }
    }

    /// <summary>
    /// 시트 그림을 칸 크기로 잘라 타일로 더한다(칸 하나 = 1×1 타일, 시트의 칸 자리를 목록 자리로 — 지금 목록 아래 줄부터).
    /// <paramref name="skipEmpty"/>면 투명한 칸, <paramref name="skipDuplicates"/>면 이미 있는(또는 이번에 더한) 타일과 픽셀이 같은 칸은 뺀다.
    /// 층은 <paramref name="floor"/>(없는 층이면 첫 층). 더한 타일 수도 돌려준다.
    /// </summary>
    public static (Tileset Tileset, int Added) ImportSheet(Tileset ts, PixelImage sheet, int offsetX = 0, int offsetY = 0, int gapX = 0, int gapY = 0,
        bool skipEmpty = true, bool skipDuplicates = true, int floor = 0)
    {
        int cw = ts.CellWidth, ch = ts.CellHeight;
        floor = ts.ValidFloor(floor);
        int row0 = ts.Tiles.Length == 0 ? 0 : ts.Tiles.Max(t => t.PaletteY) + 1;
        int nextId = ts.NextTileId;
        var known = skipDuplicates ? ts.Tiles.Where(t => t.Image.Width == cw && t.Image.Height == ch).Select(t => t.Image).ToList() : [];
        var added = new List<TileDef>();
        for (int row = 0, y = offsetY; y + ch <= sheet.Height; row++, y += ch + gapY)
            for (int col = 0, x = offsetX; x + cw <= sheet.Width; col++, x += cw + gapX)
            {
                var rect = new Rectangle(x, y, cw, ch);
                if (skipEmpty && sheet.IsRegionEmpty(rect)) continue;
                var image = sheet.Crop(rect);
                if (skipDuplicates && known.Any(k => k.ContentEquals(image))) continue;
                known.Add(image);
                added.Add(new TileDef { Id = nextId++, Image = image, Floor = floor, PaletteX = col, PaletteY = row0 + row });
            }
        return (ts with { Tiles = ts.Tiles.AddRange(added) }, added.Count);
    }

    /// <summary>
    /// 목록 자리 사각형 <paramref name="slots"/> 안의 1칸 타일들을 그 자리대로 붙여 큰 타일 하나를 만든다(차지 칸 = 사각형 크기, 원래 타일은 그대로).
    /// 층은 왼쪽 위 타일의 층. 붙일 타일이 없으면 null. 새 타일은 빈 자리에 놓는다.
    /// </summary>
    public static (Tileset Tileset, int Id)? MergeToBigTile(Tileset ts, Rectangle slots)
    {
        if (slots.Width < 1 || slots.Height < 1 || slots.Width > Tileset.MaxFootprint || slots.Height > Tileset.MaxFootprint) return null;
        int cw = ts.CellWidth, ch = ts.CellHeight;
        var image = new PixelImage(slots.Width * cw, slots.Height * ch);
        TileDef? first = null;
        foreach (var t in ts.Tiles)
        {
            if (!slots.Contains(t.PaletteX, t.PaletteY) || t.Width != 1 || t.Height != 1) continue;
            image.Blit(t.Image, (t.PaletteX - slots.X) * cw, (t.PaletteY - slots.Y) * ch, alphaBlend: true);
            if (first is null || (t.PaletteY, t.PaletteX).CompareTo((first.PaletteY, first.PaletteX)) < 0) first = t;
        }
        if (first is null) return null;
        int id = ts.NextTileId;
        var slot = NextFreeSlot(ts);
        var big = new TileDef { Id = id, Image = image, Width = slots.Width, Height = slots.Height, Floor = first.Floor, PaletteX = slot.X, PaletteY = slot.Y };
        return (ts with { Tiles = ts.Tiles.Add(big) }, id);
    }

    /// <summary>타일 하나를 바꾼다(같은 Id). 차지 칸은 1~<see cref="Tileset.MaxFootprint"/>로 맞춘다.</summary>
    public static Tileset ReplaceTile(Tileset ts, TileDef tile)
    {
        tile = tile with
        {
            Width = Math.Clamp(tile.Width, 1, Tileset.MaxFootprint),
            Height = Math.Clamp(tile.Height, 1, Tileset.MaxFootprint),
            Floor = ts.ValidFloor(tile.Floor),
        };
        int i = ts.Tiles.IndexOf(ts.Find(tile.Id)!);
        return i < 0 ? ts : ts with { Tiles = ts.Tiles.SetItem(i, tile) };
    }

    /// <summary>타일들을 지운다(자동 타일 세트에서도 뺌). 맵에서 빼는 것은 <see cref="TileMap.WithoutTile"/>.</summary>
    public static Tileset RemoveTiles(Tileset ts, IReadOnlyCollection<int> ids)
    {
        var set = ids.ToHashSet();
        return ts with
        {
            Tiles = ts.Tiles.RemoveAll(t => set.Contains(t.Id)),
            AutoTiles = ts.AutoTiles.Select(a => a with { Rules = a.Rules.Select(r => r with { Tiles = r.Tiles.RemoveAll(c => set.Contains(c.Tile)) }).ToImmutableArray() }).ToImmutableArray(),
        };
    }

    /// <summary>타일 복제(새 Id, 빈 자리).</summary>
    public static (Tileset Tileset, int Id) DuplicateTile(Tileset ts, int id)
    {
        var src = ts.Find(id) ?? throw new ArgumentException(null, nameof(id));
        int newId = ts.NextTileId;
        var slot = NextFreeSlot(ts);
        return (ts with { Tiles = ts.Tiles.Add(src with { Id = newId, PaletteX = slot.X, PaletteY = slot.Y }) }, newId);
    }

    /// <summary>층 더하기(맨 위).</summary>
    public static (Tileset Tileset, int Id) AddFloor(Tileset ts, string name)
    {
        int id = ts.NextFloorId;
        return (ts with { Floors = ts.Floors.Add(new TileFloor(id, name)) }, id);
    }

    public static Tileset RenameFloor(Tileset ts, int id, string name)
        => ts with { Floors = ts.Floors.Select(f => f.Id == id ? f with { Name = name } : f).ToImmutableArray() };

    /// <summary>층 차례를 한 칸 옮긴다(<paramref name="delta"/> = +1 위로, −1 아래로).</summary>
    public static Tileset MoveFloor(Tileset ts, int id, int delta)
    {
        int i = ts.FloorOrder(id), j = i + delta;
        if (i < 0 || j < 0 || j >= ts.Floors.Length) return ts;
        var list = ts.Floors.ToList();
        (list[i], list[j]) = (list[j], list[i]);
        return ts with { Floors = [.. list] };
    }

    /// <summary>층 지우기(그 층이 기본 층인 타일·세트는 첫 층으로). 맵에서 빼는 것은 <see cref="TileMap.WithoutFloor"/>. 마지막 층은 지우지 않는다.</summary>
    public static Tileset RemoveFloor(Tileset ts, int id)
    {
        if (ts.Floors.Length <= 1 || ts.FloorOrder(id) < 0) return ts;
        var floors = ts.Floors.RemoveAll(f => f.Id == id);
        int fallback = floors[0].Id;
        return ts with
        {
            Floors = floors,
            Tiles = ts.Tiles.Select(t => t.Floor == id ? t with { Floor = fallback } : t).ToImmutableArray(),
            AutoTiles = ts.AutoTiles.Select(a => a.Floor == id ? a with { Floor = fallback } : a).ToImmutableArray(),
        };
    }

    /// <summary>자동 타일 세트 더하기·바꾸기(같은 Id면 바꿈).</summary>
    public static Tileset PutAutoTile(Tileset ts, AutoTileSet set)
    {
        set = set with { Floor = ts.ValidFloor(set.Floor), Group = set.Group.Trim() };
        int i = ts.AutoTiles.ToList().FindIndex(a => a.Id == set.Id);
        return ts with { AutoTiles = i < 0 ? ts.AutoTiles.Add(set) : ts.AutoTiles.SetItem(i, set) };
    }

    public static Tileset RemoveAutoTile(Tileset ts, int id) => ts with { AutoTiles = ts.AutoTiles.RemoveAll(a => a.Id == id) };
}
