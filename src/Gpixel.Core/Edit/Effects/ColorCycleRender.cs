using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>색 순환(<see cref="ColorCycleEffect"/>) 그리기.</summary>
internal static class ColorCycleRender
{
    public static void Apply(PixelImage image, ColorCycleEffect e)
    {
        var colors = e.Colors;
        int n = colors.Count, k = e.Shift(e.Progress);
        if (n < ColorCycleEffect.MinColors || k == 0) return;
        var index = new Dictionary<uint, int>(n);
        for (int i = 0; i < n; i++) index.TryAdd(colors[i].Argb & 0x00FFFFFFu, i);
        var px = image.Pixels;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.A != 0 && index.TryGetValue(c.Argb & 0x00FFFFFFu, out int at)) px[i] = colors[(at + k) % n].WithAlpha(c.A);
        }
    }
}
