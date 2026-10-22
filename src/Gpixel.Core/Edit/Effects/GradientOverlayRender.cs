using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>시간 그라데이션(<see cref="GradientOverlayEffect"/>) 그리기.</summary>
internal static class GradientOverlayRender
{
    public static void Apply(PixelImage image, GradientOverlayEffect g, Rectangle source)
    {
        int amount = (g.Strength * 255 + 50) / 100;
        if (amount == 0) return;
        var gradient = g.Gradient;
        var px = image.Pixels;
        if (gradient.Dither == GradientDither.None)
        {
            var sampled = Gradient.Sample(gradient.Stops, gradient.AlphaStops, g.Progress, gradient.Interpolation);
            int strength = (amount * sampled.A + 127) / 255;
            if (strength == 0) return;
            for (int i = 0; i < px.Length; i++)
                if (px[i].A != 0) px[i] = Blend(px[i], sampled, strength, g.Blend);
            return;
        }

        int w = image.Width;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].A == 0) continue;
            int x = i % w, y = i / w;
            var sampled = Gradient.Sample(gradient.Stops, gradient.AlphaStops, g.Progress,
                gradient.Interpolation, gradient.Dither, x - source.X, y - source.Y);
            int strength = (amount * sampled.A + 127) / 255;
            if (strength != 0) px[i] = Blend(px[i], sampled, strength, g.Blend);
        }
    }

    /// <summary><paramref name="dst"/>의 RGB를 섞기 방법대로 <paramref name="color"/>와 섞은 결과 쪽으로 <paramref name="amount"/>/255만큼(알파는 그대로).</summary>
    internal static Color32 Blend(Color32 dst, Color32 color, int amount, EffectBlend blend)
    {
        switch (blend)
        {
            case EffectBlend.Screen:
                return LayerEffects.Screen(dst, color, amount);
            case EffectBlend.Multiply:
                var m = new Color32((byte)((dst.R * color.R + 127) / 255), (byte)((dst.G * color.G + 127) / 255), (byte)((dst.B * color.B + 127) / 255));
                return LayerEffects.Mix(dst, m, amount);
            default:
                return LayerEffects.Mix(dst, color, amount);
        }
    }
}
