using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>피격 번쩍임(<see cref="HitFlashEffect"/>) 그리기.</summary>
internal static class HitFlashRender
{
    public static void Apply(PixelImage image, HitFlashEffect e, Rectangle source = default)
    {
        int amount = (int)Math.Round(e.Amount(e.Progress) * 255, MidpointRounding.AwayFromZero);
        if (amount <= 0) return;
        var px = image.Pixels;
        var color = EffectGradientSampler.For(e, nameof(e.Color), image.GetOpaqueBounds(), e.Color, source.X, source.Y);
        int w = image.Width;
        for (int i = 0; i < px.Length; i++)
            if (px[i].A != 0)
            {
                var sampled = color.Sample(i % w, i / w);
                int strength = color.HasGradient ? (amount * sampled.A + 127) / 255 : amount;
                px[i] = LayerEffects.Mix(px[i], sampled, strength);
            }
    }
}
