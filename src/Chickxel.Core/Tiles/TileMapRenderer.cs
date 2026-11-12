using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 맵 그리기: 층 차례(타일셋의 <see cref="Tileset.Floors"/>, 타일셋에 없는 층은 그리지 않음)대로, 층 안에서는 차지 칸 아래 줄 → 왼쪽부터
/// (위로 솟은 그림이 앞 줄을 가리게). 그림은 알파로 겹친다.
/// </summary>
public static class TileMapRenderer
{
    /// <summary>
    /// <paramref name="target"/>에 그린다(지우지 않음). <paramref name="targetOrigin"/> = target 왼쪽 위의 캔버스 좌표, <paramref name="gridOrigin"/> = 칸 (0,0) 왼쪽 위의 캔버스 좌표.
    /// <paramref name="floors"/>가 있으면 그 층만. 움직이는 타일은 시각 <paramref name="timeMs"/>의 그림.
    /// <paramref name="floorOpacity"/>가 있으면 층마다 불투명도(0~255, 0 = 그리지 않음 — 화면의 층 보기에서 다른 층을 흐리게).
    /// </summary>
    public static void Render(Tileset tileset, TileMap map, Point gridOrigin, PixelImage target, Point targetOrigin, Func<int, bool>? floors = null, long timeMs = 0,
        Func<int, byte>? floorOpacity = null)
    {
        var area = new Rectangle(targetOrigin, target.Size);
        PixelImage? layer = null;
        foreach (var floor in tileset.Floors)
        {
            if (floors is not null && !floors(floor.Id)) continue;
            byte opacity = floorOpacity?.Invoke(floor.Id) ?? 255;
            if (opacity == 0) continue;
            if (opacity == 255)
            {
                foreach (var (rect, image) in Placements(tileset, map, floor.Id, gridOrigin, area, timeMs))
                    target.Blit(image, rect.X - targetOrigin.X, rect.Y - targetOrigin.Y, alphaBlend: true);
                continue;
            }
            // 흐린 층: 층 하나를 따로 그린 뒤 알파를 줄여 겹친다(층 안의 겹침은 그대로).
            if (layer is null) layer = new PixelImage(target.Width, target.Height);
            else layer.FillRect(layer.Bounds, default);
            bool any = false;
            foreach (var (rect, image) in Placements(tileset, map, floor.Id, gridOrigin, area, timeMs))
            {
                layer.Blit(image, rect.X - targetOrigin.X, rect.Y - targetOrigin.Y, alphaBlend: true);
                any = true;
            }
            if (!any) continue;
            var px = layer.Pixels;
            for (int i = 0; i < px.Length; i++)
                if (px[i].A != 0) px[i] = px[i].WithAlpha((byte)(px[i].A * opacity / 255));
            target.Blit(layer, 0, 0, alphaBlend: true);
        }
    }

    /// <summary>층 하나에서 <paramref name="area"/>(캔버스 좌표)와 겹치는 그림 자리(캔버스 좌표)와 그림, 그리는 차례.</summary>
    public static IEnumerable<(Rectangle Rect, PixelImage Image)> Placements(Tileset tileset, TileMap map, int floor, Point gridOrigin, Rectangle area, long timeMs = 0)
    {
        var list = new List<(int Bottom, int X, Rectangle Rect, PixelImage Image)>();
        foreach (var (at, cell) in map.Floor(floor))
        {
            if (tileset.Placement(at, cell, timeMs) is not { } p) continue;
            var rect = p.Rect;
            rect.Offset(gridOrigin);
            if (!rect.IntersectsWith(area)) continue;
            var size = tileset.Footprint(cell);
            list.Add((at.Y + size.Height, at.X, rect, p.Image));
        }
        list.Sort((a, b) => a.Bottom != b.Bottom ? a.Bottom.CompareTo(b.Bottom) : a.X.CompareTo(b.X));
        return list.Select(e => (e.Rect, e.Image));
    }

    /// <summary>그림이 닿는 캔버스 사각형(층 거르기 가능). 없으면 빈 것.</summary>
    public static Rectangle PixelBounds(Tileset tileset, TileMap map, Point gridOrigin, Func<int, bool>? floors = null)
    {
        var bounds = Rectangle.Empty;
        foreach (var p in map.All)
        {
            if (floors is not null && !floors(p.Floor) || tileset.FloorOrder(p.Floor) < 0 || tileset.Placement(p.At, p.Cell) is not { } placed) continue;
            var r = placed.Rect;
            r.Offset(gridOrigin);
            bounds = bounds.IsEmpty ? r : Rectangle.Union(bounds, r);
        }
        return bounds;
    }

    /// <summary>캔버스 사각형 <paramref name="area"/>를 새 그림에 그린다(움직이는 타일은 시각 <paramref name="timeMs"/>의 그림, 층마다 불투명도는 <see cref="Render"/>).</summary>
    public static PixelImage RenderArea(Tileset tileset, TileMap map, Point gridOrigin, Rectangle area, Func<int, bool>? floors = null, long timeMs = 0,
        Func<int, byte>? floorOpacity = null)
    {
        var image = new PixelImage(area.Width, area.Height);
        Render(tileset, map, gridOrigin, image, area.Location, floors, timeMs, floorOpacity);
        return image;
    }
}
