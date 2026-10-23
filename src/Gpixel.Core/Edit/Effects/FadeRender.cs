using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

internal static class FadeRender
{
    public static void Apply(PixelImage image, FadeEffect effect)
        => Apply(image, effect.FadeOut ? 1 - effect.Progress : effect.Progress);

    internal static void Apply(PixelImage image, double visibility)
    {
        if (visibility >= 1) return;
        var pixels = image.Pixels;
        if (visibility <= 0)
        {
            Array.Clear(pixels);
            return;
        }
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].A == 0) continue;
            byte alpha = (byte)Math.Round(pixels[i].A * visibility, MidpointRounding.AwayFromZero);
            pixels[i] = alpha == 0 ? Color32.Transparent : pixels[i].WithAlpha(alpha);
        }
    }
}
