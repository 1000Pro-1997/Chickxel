using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>색 순환(<see cref="ColorCycleEffect"/>) 그리기.</summary>
internal static class ColorCycleRender
{
    public static void Apply(PixelImage image, ColorCycleEffect e, Rectangle source = default)
    {
        var colors = e.Colors;
        int n = colors.Count, k = e.Shift(e.Progress);
        if (n < ColorCycleEffect.MinColors || k == 0) return;
        var px = image.Pixels;
        if (e.ColorGradients.Count == 0)
        {
            var index = new Dictionary<uint, int>(n);
            for (int i = 0; i < n; i++) index.TryAdd(colors[i].Argb & 0x00FFFFFFu, i);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.A != 0 && index.TryGetValue(c.Argb & 0x00FFFFFFu, out int at)) px[i] = colors[(at + k) % n].WithAlpha(c.A);
            }
            return;
        }

        var bounds = image.GetOpaqueBounds();
        var samplers = new EffectGradientSampler[n];
        for (int j = 0; j < n; j++)
            samplers[j] = EffectGradientSampler.For(e, $"Colors[{j}]", bounds, colors[j], source.X, source.Y);
        int w = image.Width;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.A == 0) continue;
            int x = i % w, y = i / w;
            for (int j = 0; j < n; j++)
            {
                var match = samplers[j].Sample(x, y);
                if (samplers[j].HasGradient && match.A == 0) continue;
                if (c.R != match.R || c.G != match.G || c.B != match.B) continue;
                var target = samplers[(j + k) % n].Sample(x, y);
                px[i] = samplers[(j + k) % n].HasGradient
                    ? LayerEffects.Mix(c, target, target.A)
                    : target.WithAlpha(c.A);
                break;
            }
        }
    }
}
