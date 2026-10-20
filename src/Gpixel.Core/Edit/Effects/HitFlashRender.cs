using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>피격 번쩍임(<see cref="HitFlashEffect"/>) 그리기.</summary>
internal static class HitFlashRender
{
    public static void Apply(PixelImage image, HitFlashEffect e)
    {
        int amount = (int)Math.Round(e.Amount(e.Progress) * 255, MidpointRounding.AwayFromZero);
        if (amount <= 0) return;
        var px = image.Pixels;
        for (int i = 0; i < px.Length; i++)
            if (px[i].A != 0) px[i] = LayerEffects.Mix(px[i], e.Color, amount);
    }
}
