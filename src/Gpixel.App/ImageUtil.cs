using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>PixelImage ↔ GDI+ Bitmap 변환과 썸네일 그리기.</summary>
public static class ImageUtil
{
    /// <summary>같은 크기의 Format32bppArgb 비트맵을 만든다.</summary>
    public static Bitmap ToBitmap(PixelImage image)
    {
        var bmp = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        CopyToBitmap(image, bmp);
        return bmp;
    }

    /// <summary>이미지 픽셀을 같은 크기 비트맵에 그대로 복사한다(Color32 배치 = Format32bppArgb).</summary>
    public static unsafe void CopyToBitmap(PixelImage image, Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var src = image.Pixels.AsSpan();
            if (data.Stride == image.Width * 4)
            {
                src.CopyTo(new Span<Color32>((void*)data.Scan0, src.Length));
            }
            else
            {
                for (int y = 0; y < image.Height; y++)
                    src.Slice(y * image.Width, image.Width)
                        .CopyTo(new Span<Color32>((byte*)data.Scan0 + (long)y * data.Stride, image.Width));
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>GDI+ 이미지(클립보드 등)를 PixelImage로 바꾼다.</summary>
    public static unsafe PixelImage FromImage(Image source)
    {
        using var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(source, 0, 0, source.Width, source.Height);
        }
        var img = new PixelImage(bmp.Width, bmp.Height);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < img.Height; y++)
                new ReadOnlySpan<Color32>((byte*)data.Scan0 + (long)y * data.Stride, img.Width)
                    .CopyTo(img.Pixels.AsSpan(y * img.Width, img.Width));
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return img;
    }

    /// <summary>
    /// 점 단위 확대(정수 배율, 최근접)로 <paramref name="area"/> 가운데에 그린다. 그린 화면 사각형을 돌려준다.
    /// <paramref name="fixedScale"/>가 0보다 크면 그 배율, 아니면 들어가는 가장 큰 정수 배율(작으면 축소).
    /// </summary>
    public static Rectangle DrawFitted(Graphics g, Image bmp, Rectangle src, Rectangle area, int fixedScale = 0, bool checker = false)
    {
        if (src.Width <= 0 || src.Height <= 0 || area.Width <= 0 || area.Height <= 0) return Rectangle.Empty;
        float scale;
        if (fixedScale > 0) scale = fixedScale;
        else
        {
            float fit = Math.Min(area.Width / (float)src.Width, area.Height / (float)src.Height);
            scale = fit >= 1 ? MathF.Floor(fit) : fit;
        }
        int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
        var dest = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        if (checker) Theme.DrawChecker(g, dest, Math.Max(2, Math.Min(8, (int)scale * 2)));
        var oldInterp = g.InterpolationMode;
        var oldOffset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bmp, dest, src, GraphicsUnit.Pixel);
        g.InterpolationMode = oldInterp;
        g.PixelOffsetMode = oldOffset;
        return dest;
    }

    /// <summary>체커 배경 위에 이미지를 정수 배율로 맞춘 정사각 썸네일.</summary>
    public static Bitmap MakeThumbnail(PixelImage image, int size)
    {
        var thumb = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var src = ToBitmap(image);
        using var g = Graphics.FromImage(thumb);
        g.Clear(Theme.InputColor);
        DrawFitted(g, src, new Rectangle(0, 0, src.Width, src.Height), new Rectangle(1, 1, size - 2, size - 2), checker: true);
        return thumb;
    }

    /// <summary>System.Drawing 색으로.</summary>
    public static Color ToColor(this Color32 c) => Color.FromArgb((int)c.Argb);

    public static Color32 ToColor32(this Color c) => new((uint)c.ToArgb());

    /// <summary>색 칸(투명이면 체커 위에)을 그린다.</summary>
    public static void DrawSwatch(Graphics g, Rectangle r, Color32 c)
    {
        if (c.A < 255) Theme.DrawChecker(g, r, Math.Max(2, r.Height / 4));
        using var b = new SolidBrush(c.ToColor());
        g.FillRectangle(b, r);
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>키가 지금 눌려 있는지(포커스와 무관).</summary>
    public static bool IsKeyDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    /// <summary>
    /// 키보드 포커스를 가진 컨트롤. WinForms 컨트롤이 아닌 창(목록 이름 편집 상자·콤보 입력란 등)이면
    /// 그 창을 품은 컨트롤과 함께 <paramref name="isNativeChild"/>=true를 돌려준다.
    /// </summary>
    public static Control? FocusedControl(out bool isNativeChild)
    {
        var h = GetFocus();
        var c = Control.FromHandle(h);
        isNativeChild = c is null && h != IntPtr.Zero;
        return c ?? Control.FromChildHandle(h);
    }
}
