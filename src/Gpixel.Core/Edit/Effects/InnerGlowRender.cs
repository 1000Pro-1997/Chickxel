using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>안쪽 빛(<see cref="InnerGlowEffect"/>) 그리기.</summary>
internal static class InnerGlowRender
{
    public static void Apply(PixelImage image, InnerGlowEffect g)
    {
        int amount = (g.Strength * 255 + 50) / 100;
        if (amount == 0) return;
        int w = image.Width, h = image.Height, n = g.Size;
        var px = image.Pixels;
        var outer = LayerEffects.Mask(px);
        // k번째 껍질 = (k−1)번 깎은 모양에서 k번 깎은 모양을 뺀 것(가장자리에서 k px 안쪽)
        for (int k = 1; k <= n; k++)
        {
            var inner = LayerEffects.Erode(outer, w, h, diagonal: true);
            int a = g.Fade ? amount * (n - k + 1) / n : amount;
            bool any = false;
            for (int i = 0; i < px.Length; i++)
            {
                if (!outer[i]) continue;
                any = true;
                if (!inner[i]) px[i] = GradientOverlayRender.Blend(px[i], g.Color, a, g.Blend);
            }
            if (!any) break;
            outer = inner;
        }
    }
}
