using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// 모양 펜 세트 그림을 코드로 그리는 도우미(공식 세트 <see cref="Assets.OfficialShapeSets"/>와 <c>tools/CatalogBuilder</c>의 다른 공식 세트가 같이 씀):
/// 글자 그림(글자마다 한 픽셀, 팔레트 0xAARRGGBB), 좌우 뒤집기, 원·타원, 겹쳐 놓기, 빛 번짐, 씨앗 고정 난수. 같은 코드면 늘 같은 모양이다.
/// </summary>
public static class ShapeArt
{
    /// <summary>이름 붙은 세트(모양은 투명한 둘레를 잘라 담음 — 빈 모양이면 <see cref="InvalidOperationException"/>).</summary>
    public static StampSet Set(string name, IEnumerable<PixelImage> shapes, StampOrder order = StampOrder.Random)
    {
        var set = new StampSet { Name = name, Order = order };
        foreach (var shape in shapes) set.Shapes.Add(StampLibrary.Trim(shape) ?? throw new InvalidOperationException(name));
        return set;
    }

    /// <summary>글자 그림 → 모양(글자마다 한 픽셀, 앞 팔레트부터 찾음, 없는 글자 = 투명).</summary>
    public static PixelImage Art(string[] rows, params Dictionary<char, uint>[] palettes)
    {
        int w = rows.Max(r => r.Length), h = rows.Length;
        var image = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < rows[y].Length; x++)
                foreach (var palette in palettes)
                    if (palette.TryGetValue(rows[y][x], out var argb))
                    {
                        image.Pixels[y * w + x] = new Color32(argb);
                        break;
                    }
        return image;
    }

    /// <summary>왼쪽 반(마지막 글자가 가운데 열)을 좌우로 펼친 줄들.</summary>
    public static string[] Symmetric(string[] left)
        => [.. left.Select(row => row + new string(row[..^1].Reverse().ToArray()))];

    /// <summary>모양들 다음에 좌우로 뒤집은 것들(좌우 대칭이라 같은 것은 빼고).</summary>
    public static IEnumerable<PixelImage> Mirrored(IEnumerable<PixelImage> shapes)
    {
        var list = shapes.ToList();
        return list.Concat(list.Select(FlipX).Where(f => !list.Any(s => s.ContentEquals(f))));
    }

    public static PixelImage FlipX(PixelImage image)
    {
        var result = new PixelImage(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                result.Pixels[y * image.Width + image.Width - 1 - x] = image.Pixels[y * image.Width + x];
        return result;
    }

    /// <summary><paramref name="w"/>×<paramref name="h"/> 그림을 칸마다 <paramref name="color"/>로 칠한다.</summary>
    public static PixelImage Paint(int w, int h, Func<int, int, Color32> color)
    {
        var image = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) image.Pixels[y * w + x] = color(x, y);
        return image;
    }

    /// <summary>픽셀 아트 원(브러시 원과 같은 식: 칸 가운데까지 거리² ≤ r² − r/2, r = 지름/2).</summary>
    public static bool InCircle(int size, double x, double y)
    {
        double r = size / 2.0, c = (size - 1) / 2.0;
        return size <= 2 || (x - c) * (x - c) + (y - c) * (y - c) <= r * r - r / 2;
    }

    /// <summary>타원 안(가로 <paramref name="w"/>·세로 <paramref name="h"/> 칸에 꼭 맞게).</summary>
    public static bool InEllipse(int w, int h, int x, int y)
    {
        double rx = w / 2.0, ry = h / 2.0, dx = (x - (w - 1) / 2.0) / rx, dy = (y - (h - 1) / 2.0) / ry;
        return dx * dx + dy * dy <= 1.0 - 0.5 / Math.Max(rx, ry);
    }

    /// <summary>안쪽 칸 중 위·아래·왼쪽·오른쪽 이웃이 바깥인 칸(테두리).</summary>
    public static bool Edge(Func<int, int, bool> inside, int x, int y)
        => !inside(x - 1, y) || !inside(x + 1, y) || !inside(x, y - 1) || !inside(x, y + 1);

    /// <summary><paramref name="width"/>×<paramref name="height"/> 판에 그림들을 차례로 겹쳐 놓는다(뒤 그림의 보이는 픽셀이 앞 그림을 덮음, 판 밖은 버림).</summary>
    public static PixelImage Compose(int width, int height, IEnumerable<(PixelImage Image, int X, int Y)> parts)
    {
        var result = new PixelImage(width, height);
        foreach (var (image, ox, oy) in parts)
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    int tx = ox + x, ty = oy + y;
                    if (tx < 0 || ty < 0 || tx >= width || ty >= height) continue;
                    if (image.Pixels[y * image.Width + x] is { A: > 0 } pixel) result.Pixels[ty * width + tx] = pixel;
                }
        return result;
    }

    /// <summary>
    /// 빛 번짐: 그림 둘레 2px에 <paramref name="glow"/> 색을 반투명으로(가까울수록 진하게). <paramref name="fromRow"/> 위 줄(전구 꼭지 등)은 빛나지 않는다.
    /// </summary>
    public static PixelImage Glow(PixelImage art, uint glow, int fromRow = 0)
    {
        const int pad = 2;
        int w = art.Width + pad * 2, h = art.Height + pad * 2;
        var result = new PixelImage(w, h);
        uint rgb = glow & 0xFFFFFF;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = x - pad, sy = y - pad;
                if (Inside(art, sx, sy) && art.Pixels[sy * art.Width + sx].A > 0)
                {
                    result.Pixels[y * w + x] = art.Pixels[sy * art.Width + sx];
                    continue;
                }
                int nearest = int.MaxValue;
                for (int dy = -pad; dy <= pad; dy++)
                    for (int dx = -pad; dx <= pad; dx++)
                    {
                        int ny = sy + dy, nx = sx + dx;
                        if (ny >= fromRow && Inside(art, nx, ny) && art.Pixels[ny * art.Width + nx].A > 0) nearest = Math.Min(nearest, dx * dx + dy * dy);
                    }
                uint alpha = nearest switch { <= 1 => 0x90, <= 2 => 0x60, <= 5 => 0x30, _ => 0 };
                if (alpha > 0) result.Pixels[y * w + x] = new Color32(alpha << 24 | rgb);
            }
        return result;
    }

    private static bool Inside(PixelImage image, int x, int y) => x >= 0 && y >= 0 && x < image.Width && y < image.Height;

    /// <summary>씨앗이 같으면 늘 같은 수를 내는 작은 난수(.NET 버전에 따라 바뀌지 않게 직접).</summary>
    public struct Lcg(uint seed)
    {
        private uint _state = seed * 2654435761u + 1;

        /// <summary>0 이상 <paramref name="max"/> 미만.</summary>
        public int Next(int max)
        {
            _state = _state * 1664525u + 1013904223u;
            return (int)((_state >> 8) % (uint)max);
        }
    }
}
