using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>반사(<see cref="ReflectionEffect"/>) 그리기.</summary>
internal static class ReflectionRender
{
    /// <summary>높이 <paramref name="height"/>px 그림에서 비칠 줄 수(<paramref name="percent"/>%, 올림, 1줄 이상).</summary>
    public static int RowsFor(int height, int percent) => height <= 0 ? 0 : Math.Max(1, (height * percent + 99) / 100);

    /// <summary>그림 쪽부터 <paramref name="row"/>번째 줄의 가로 흔들림(px).</summary>
    public static int WaveAt(int wave, int row)
        => wave == 0 ? 0 : (int)Math.Round(wave * Math.Sin(2 * Math.PI * row / ReflectionEffect.WavePeriod), MidpointRounding.AwayFromZero);

    public static void Apply(PixelImage image, ReflectionEffect m, Rectangle source)
    {
        var bounds = image.GetOpaqueBounds();
        if (bounds.IsEmpty) return;
        int w = image.Width, h = image.Height, rows = RowsFor(bounds.Height, m.Height);
        var px = image.Pixels;
        // 비친 줄은 모두 보이는 영역 밖(위 또는 아래)이라 읽는 줄과 겹치지 않는다
        for (int j = 0; j < rows; j++)
        {
            int sy = m.Above ? bounds.Top + j : bounds.Bottom - 1 - j;
            int ty = m.Above ? bounds.Top - 1 - m.Gap - j : bounds.Bottom + m.Gap + j;
            if ((uint)ty >= (uint)h) continue;
            double opacity = (rows == 1 ? m.OpacityStart : m.OpacityStart + (m.OpacityEnd - m.OpacityStart) * (double)j / (rows - 1)) / 100;
            if (opacity <= 0) continue;
            int dx = WaveAt(m.Wave, j);
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                var c = px[sy * w + x];
                if (c.A == 0) continue;
                int tx = x + dx;
                if ((uint)tx >= (uint)w) continue;
                if (m.DitherFade)
                {
                    if (!Bayer.On(EffectDither.Bayer4, opacity, tx - source.X, ty - source.Y)) continue;
                }
                else
                {
                    byte a = (byte)Math.Round(c.A * opacity, MidpointRounding.AwayFromZero);
                    if (a == 0) continue;
                    c = c.WithAlpha(a);
                }
                if (m.Tint.A != 0) c = LayerEffects.Mix(c, m.Tint, m.Tint.A);
                int i = ty * w + tx;
                px[i] = Color32.BlendOver(c, px[i]);
            }
        }
    }
}
