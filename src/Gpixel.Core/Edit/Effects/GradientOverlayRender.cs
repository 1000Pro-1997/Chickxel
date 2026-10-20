using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>그라데이션 덮기(<see cref="GradientOverlayEffect"/>) 그리기.</summary>
internal static class GradientOverlayRender
{
    public static void Apply(PixelImage image, GradientOverlayEffect g, Rectangle source)
    {
        int amount = (g.Strength * 255 + 50) / 100;
        if (amount == 0) return;
        var bounds = image.GetOpaqueBounds();
        if (bounds.IsEmpty) return;
        double angle = g.Angle * Math.PI / 180, dx = Math.Cos(angle), dy = -Math.Sin(angle); // y는 아래로
        double Along(double x, double y) => x * dx + y * dy;
        // 보이는 영역의 픽셀 가운데 중 A 쪽 끝(s0) → B 쪽 끝(s1)
        double l = bounds.Left + 0.5, r = bounds.Right - 0.5, top = bounds.Top + 0.5, bottom = bounds.Bottom - 0.5;
        double s0 = Math.Min(Math.Min(Along(l, top), Along(r, top)), Math.Min(Along(l, bottom), Along(r, bottom)));
        double s1 = Math.Max(Math.Max(Along(l, top), Along(r, top)), Math.Max(Along(l, bottom), Along(r, bottom)));
        double span = s1 - s0;
        int w = image.Width;
        var px = image.Pixels;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                int i = y * w + x;
                if (px[i].A == 0) continue;
                double t = span < 1e-9 ? 0.5 : Math.Clamp((Along(x + 0.5, y + 0.5) - s0) / span, 0, 1);
                Color32 c = g.Dither == EffectDither.None ? Lerp(g.ColorA, g.ColorB, t)
                    : Bayer.On(g.Dither, t, x - source.X, y - source.Y) ? g.ColorB : g.ColorA;
                px[i] = Blend(px[i], c, amount, g.Blend);
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

    private static Color32 Lerp(Color32 a, Color32 b, double t)
    {
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t, MidpointRounding.AwayFromZero);
        return new Color32(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }
}
