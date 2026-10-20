using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>깜빡이기(<see cref="BlinkEffect"/>) 그리기.</summary>
internal static class BlinkRender
{
    public static void Apply(PixelImage image, BlinkEffect e, Rectangle source)
    {
        if (!e.Hidden) return;
        var px = image.Pixels;
        switch (e.Hide)
        {
            case BlinkHide.Transparent:
                Array.Clear(px);
                break;
            case BlinkHide.Translucent:
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].A == 0) continue;
                    byte a = (byte)((px[i].A * e.Opacity + 50) / 100);
                    px[i] = a == 0 ? Color32.Transparent : px[i].WithAlpha(a);
                }
                break;
            default:
                int w = image.Width, h = image.Height;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (((x - source.X + y - source.Y) & 1) != 0) px[y * w + x] = Color32.Transparent;
                break;
        }
    }
}
