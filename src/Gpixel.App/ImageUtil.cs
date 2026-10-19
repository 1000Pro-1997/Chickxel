using Avalonia.Platform;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>PixelImage ↔ Avalonia 비트맵 변환과 썸네일·점 확대 그리기.</summary>
public static class ImageUtil
{
    /// <summary>같은 크기의 BGRA(직선 알파) 비트맵을 만든다. Color32 메모리 배치(ARGB uint, 리틀 엔디언)와 같다.</summary>
    public static WriteableBitmap ToBitmap(PixelImage image)
    {
        var bmp = NewBitmap(image.Width, image.Height);
        CopyToBitmap(image, bmp);
        return bmp;
    }

    /// <summary>빈 BGRA(직선 알파) 비트맵.</summary>
    public static WriteableBitmap NewBitmap(int width, int height)
        => new(new PixelSize(Math.Max(1, width), Math.Max(1, height)), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);

    /// <summary>이미지 픽셀을 같은 크기 비트맵에 그대로 복사한다.</summary>
    public static void CopyToBitmap(PixelImage image, WriteableBitmap bmp) => CopyToBitmap(image.Pixels, image.Width, image.Height, bmp);

    private static unsafe void CopyToBitmap(ReadOnlySpan<Color32> src, int width, int height, WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        int w = Math.Min(width, fb.Size.Width), h = Math.Min(height, fb.Size.Height);
        for (int y = 0; y < h; y++)
            src.Slice(y * width, w).CopyTo(new Span<Color32>((byte*)fb.Address + (long)y * fb.RowBytes, w));
    }

    /// <summary>
    /// 점 단위 확대(정수 배율, 최근접)로 <paramref name="area"/> 가운데에 그린다. 그린 화면 사각형을 돌려준다.
    /// <paramref name="fixedScale"/>가 0보다 크면 그 배율, 아니면 들어가는 가장 큰 정수 배율(작으면 축소).
    /// </summary>
    public static Rect DrawFitted(DrawingContext g, Bitmap bmp, Rectangle src, Rect area, int fixedScale = 0, bool checker = false)
    {
        if (src.Width <= 0 || src.Height <= 0 || area.Width <= 0 || area.Height <= 0) return default;
        double scale;
        if (fixedScale > 0) scale = fixedScale;
        else
        {
            double fit = Math.Min(area.Width / src.Width, area.Height / src.Height);
            scale = fit >= 1 ? Math.Floor(fit) : fit;
        }
        double w = Math.Max(1, Math.Floor(src.Width * scale)), h = Math.Max(1, Math.Floor(src.Height * scale));
        var dest = new Rect(Math.Floor(area.X + (area.Width - w) / 2), Math.Floor(area.Y + (area.Height - h) / 2), w, h);
        if (checker) AppTheme.DrawChecker(g, dest, Math.Max(2, Math.Min(8, (int)scale * 2)));
        using (Pixelated(g))
            g.DrawImage(bmp, new Rect(src.X, src.Y, src.Width, src.Height), dest);
        return dest;
    }

    /// <summary>비트맵을 최근접(번지지 않게)으로 확대해 그리는 구간. using으로 감싼다.</summary>
    public static DrawingContext.PushedState Pixelated(DrawingContext g)
        => g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None });

    /// <summary>체커 배경 위에 이미지를 정수 배율로 맞춘 정사각 썸네일(UI 스레드가 아니어도 만들 수 있음).</summary>
    public static WriteableBitmap MakeThumbnail(PixelImage image, int size)
    {
        var px = new Color32[size * size];
        var bg = AppTheme.InputColor.ToColor32();
        Array.Fill(px, bg);
        int inner = size - 2;
        if (image.Width > 0 && image.Height > 0 && inner > 0)
        {
            double fit = Math.Min(inner / (double)image.Width, inner / (double)image.Height);
            double scale = fit >= 1 ? Math.Floor(fit) : fit;
            int w = Math.Max(1, (int)(image.Width * scale)), h = Math.Max(1, (int)(image.Height * scale));
            int ox = 1 + (inner - w) / 2, oy = 1 + (inner - h) / 2;
            int cell = Math.Max(2, Math.Min(8, (int)scale * 2));
            var ca = AppTheme.CheckerA.ToColor32();
            var cb = AppTheme.CheckerB.ToColor32();
            for (int y = 0; y < h; y++)
            {
                int sy = Math.Min(image.Height - 1, (int)(y / scale));
                for (int x = 0; x < w; x++)
                {
                    int sx = Math.Min(image.Width - 1, (int)(x / scale));
                    var back = ((x / cell) + (y / cell)) % 2 == 0 ? ca : cb;
                    px[(oy + y) * size + ox + x] = Blend(back, image.Pixels[sy * image.Width + sx]);
                }
            }
        }
        var bmp = NewBitmap(size, size);
        CopyToBitmap(px, size, size, bmp);
        return bmp;
    }

    /// <summary>불투명 바탕 위에 색을 덮는다.</summary>
    private static Color32 Blend(Color32 back, Color32 c)
    {
        if (c.A == 255) return c;
        if (c.A == 0) return back;
        int a = c.A, ia = 255 - a;
        return new Color32((byte)((c.R * a + back.R * ia) / 255), (byte)((c.G * a + back.G * ia) / 255), (byte)((c.B * a + back.B * ia) / 255), 255);
    }

    /// <summary>Avalonia 색으로.</summary>
    public static Color ToColor(this Color32 c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static Color32 ToColor32(this Color c) => new(c.R, c.G, c.B, c.A);

    /// <summary>색 칸(투명이면 체커 위에)을 그린다.</summary>
    public static void DrawSwatch(DrawingContext g, Rect r, Color32 c)
    {
        if (c.A < 255) AppTheme.DrawChecker(g, r, Math.Max(2, r.Height / 4));
        g.FillRectangle(AppTheme.Brush(c.ToColor()), r);
    }
}
