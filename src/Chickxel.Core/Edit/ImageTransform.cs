using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Edit;

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

    /// <summary>
    /// 캔버스 크기 바꾸기 + 크기 바꾸기에서 캔버스 밖까지 든 그림 하나: 원래 캔버스 좌표 <paramref name="origin"/>에 놓인 <paramref name="full"/>을
    /// 원래 캔버스(<paramref name="oldCanvas"/>)가 새 캔버스 좌표 <paramref name="image"/> 자리에 그 크기로 놓이는 대응 하나로 옮긴다 —
    /// 캔버스 안·밖을 함께 같은 배율로 늘이거나 줄여 이음매가 없다. 캔버스 안은 <see cref="ResizeCanvasScaled"/>와 같은 픽셀을 집고,
    /// 밖은 같은 최근접 대응을 이어 쓴다(9-slice면 테두리처럼 1:1). <paramref name="clip"/>(새 캔버스 좌표) 밖은 계산하지 않고, 남는 칸이 없으면 null.
    /// </summary>
    public static (PixelImage Image, Point Origin)? ScaleWithCanvas(PixelImage full, Point origin, Size oldCanvas, Rectangle image, Rectangle clip,
        SpriteBorder slice = default, NineSliceFill fill = NineSliceFill.Stretch)
    {
        bool sliced = NineSlice.Applies(slice, oldCanvas.Width, oldCanvas.Height);
        var xs = NineSlice.SourceIndices(image.Width, oldCanvas.Width, slice.Left, slice.Right, fill, sliced);
        var ys = NineSlice.SourceIndices(image.Height, oldCanvas.Height, slice.Top, slice.Bottom, fill, sliced);
        var (x0, x1) = DestRange(origin.X, origin.X + full.Width, oldCanvas.Width, image.Width, sliced);
        var (y0, y1) = DestRange(origin.Y, origin.Y + full.Height, oldCanvas.Height, image.Height, sliced);
        var dest = Rectangle.Intersect(new Rectangle(image.X + x0, image.Y + y0, x1 - x0, y1 - y0), clip);
        if (dest.Width <= 0 || dest.Height <= 0) return null;
        var dst = new PixelImage(dest.Width, dest.Height);
        var columns = new int[dest.Width];
        for (int i = 0; i < dest.Width; i++) columns[i] = SourceAt(dest.X + i - image.X, xs, oldCanvas.Width, image.Width, sliced) - origin.X;
        for (int j = 0; j < dest.Height; j++)
        {
            int sy = SourceAt(dest.Y + j - image.Y, ys, oldCanvas.Height, image.Height, sliced) - origin.Y;
            if (sy < 0 || sy >= full.Height) continue;
            int row = sy * full.Width, outRow = j * dest.Width;
            for (int i = 0; i < dest.Width; i++)
            {
                int sx = columns[i];
                if (sx >= 0 && sx < full.Width) dst.Pixels[outRow + i] = full.Pixels[row + sx];
            }
        }
        return (dst, dest.Location);
    }

    /// <summary>새 그림 자리 기준 칸 <paramref name="d"/>가 집는 원래 캔버스 칸: 안은 <paramref name="inside"/>, 밖은 같은 최근접 대응(9-slice면 가장자리에서 1:1).</summary>
    private static int SourceAt(int d, int[] inside, int srcLength, int dstLength, bool sliced)
    {
        if (d >= 0 && d < dstLength) return inside[d];
        if (sliced) return d < 0 ? d : srcLength + d - dstLength;
        return (int)Math.Floor((decimal)d * srcLength / dstLength);
    }

    /// <summary>원래 칸 [<paramref name="start"/>, <paramref name="end"/>)를 집는 새 칸 범위(새 그림 자리 기준, 넉넉하게).</summary>
    private static (int From, int To) DestRange(int start, int end, int srcLength, int dstLength, bool sliced)
    {
        if (sliced)
            return (start < 0 ? start : start < srcLength ? 0 : dstLength + start - srcLength,
                end > srcLength ? dstLength + end - srcLength : end > 0 ? dstLength : end);
        return ((int)Math.Ceiling((decimal)start * dstLength / srcLength), (int)Math.Ceiling((decimal)end * dstLength / srcLength));
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
