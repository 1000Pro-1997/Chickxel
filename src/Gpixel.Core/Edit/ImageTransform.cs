using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>이미지 전체 변환(뒤집기·회전·캔버스 크기). 모두 새 이미지를 돌려준다.</summary>
public static class ImageTransform
{
    /// <summary>좌우 뒤집기.</summary>
    public static PixelImage FlipHorizontal(PixelImage src)
    {
        var dst = new PixelImage(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
        {
            int row = y * src.Width;
            for (int x = 0; x < src.Width; x++)
                dst.Pixels[row + x] = src.Pixels[row + src.Width - 1 - x];
        }
        return dst;
    }

    /// <summary>상하 뒤집기.</summary>
    public static PixelImage FlipVertical(PixelImage src)
    {
        var dst = new PixelImage(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            Array.Copy(src.Pixels, (src.Height - 1 - y) * src.Width, dst.Pixels, y * src.Width, src.Width);
        return dst;
    }

    /// <summary>90° 회전(가로·세로가 바뀐다). <paramref name="clockwise"/>가 false면 반시계.</summary>
    public static PixelImage Rotate90(PixelImage src, bool clockwise = true)
    {
        int w = src.Width, h = src.Height;
        var dst = new PixelImage(h, w);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = src.Pixels[y * w + x];
                int nx = clockwise ? h - 1 - y : y;
                int ny = clockwise ? x : w - 1 - x;
                dst.Pixels[ny * h + nx] = c;
            }
        return dst;
    }

    /// <summary>180° 회전.</summary>
    public static PixelImage Rotate180(PixelImage src)
    {
        var dst = new PixelImage(src.Width, src.Height);
        int n = src.Pixels.Length;
        for (int i = 0; i < n; i++) dst.Pixels[n - 1 - i] = src.Pixels[i];
        return dst;
    }

    /// <summary>캔버스 크기 바꾸기(<see cref="PixelImage.ResizeCanvas"/>와 같음).</summary>
    public static PixelImage ResizeCanvas(PixelImage src, int width, int height, Anchor anchor, Color32 fill = default)
        => src.ResizeCanvas(width, height, anchor, fill);

    /// <summary>
    /// 캔버스 크기 바꾸기 + 크기 바꾸기(최근접): <paramref name="width"/>×<paramref name="height"/> 새 이미지의 <paramref name="rect"/> 자리에
    /// 원본을 rect 크기로 늘이거나 줄여 놓는다(새 이미지 밖은 잘리고 나머지는 투명). 집는 픽셀은 <see cref="ScaleNearest"/>와 같고, 보이는 부분만 계산한다.
    /// <paramref name="slice"/>를 쓸 수 있으면(<see cref="NineSlice.Applies"/>) 최근접 대신 9-slice로 늘인다(<see cref="NineSlice.Scale"/>와 같음).
    /// </summary>
    public static PixelImage ResizeCanvasScaled(PixelImage src, int width, int height, Rectangle rect,
        SpriteBorder slice = default, NineSliceFill fill = NineSliceFill.Stretch)
    {
        var dst = new PixelImage(width, height);
        if (rect.Width == src.Width && rect.Height == src.Height)
        {
            dst.Blit(src, rect.X, rect.Y);
            return dst;
        }
        var r = Rectangle.Intersect(rect, dst.Bounds);
        if (r.IsEmpty) return dst;
        bool sliced = NineSlice.Applies(slice, src.Width, src.Height);
        var xs = NineSlice.SourceIndices(rect.Width, src.Width, slice.Left, slice.Right, fill, sliced);
        var ys = NineSlice.SourceIndices(rect.Height, src.Height, slice.Top, slice.Bottom, fill, sliced);
        for (int y = r.Top; y < r.Bottom; y++)
        {
            int row = ys[y - rect.Y] * src.Width;
            for (int x = r.Left; x < r.Right; x++)
                dst.Pixels[y * width + x] = src.Pixels[row + xs[x - rect.X]];
        }
        return dst;
    }

    /// <summary>정수 배율 확대/축소(최근접). 축소는 각 칸의 왼쪽 위 픽셀을 쓴다.</summary>
    public static PixelImage ScaleNearest(PixelImage src, int width, int height)
    {
        var dst = new PixelImage(width, height);
        for (int y = 0; y < height; y++)
        {
            int sy = (int)((long)y * src.Height / height);
            for (int x = 0; x < width; x++)
            {
                int sx = (int)((long)x * src.Width / width);
                dst.Pixels[y * width + x] = src.Pixels[sy * src.Width + sx];
            }
        }
        return dst;
    }
}
