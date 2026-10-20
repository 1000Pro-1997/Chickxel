using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>잔상(<see cref="AfterimageEffect"/>) 그리기: 앞 프레임 원본들을 옅게 깐 뒤 지금 그림을 그 위에 올린다.</summary>
internal static class AfterimageRender
{
    public static void Apply(PixelImage image, AfterimageEffect e, Rectangle source)
    {
        if (e.FrameSource is not { } frameSource) return;
        int w = image.Width, h = image.Height;
        Color32[]? ghosts = null;
        for (int k = e.Count; k >= 1; k--) // 먼 잔상부터 깔고 가까운 잔상을 위에
        {
            int back = k * e.Interval;
            if (back > e.FrameStep) continue; // 범위 안 프레임만
            if (frameSource(e.FrameIndex - back) is not { } ghost || ghost.Width != source.Width || ghost.Height != source.Height) continue;
            byte opacity = e.GhostOpacity(k);
            if (opacity == 0) continue;
            ghosts ??= new Color32[w * h];
            var gp = ghost.Pixels;
            for (int gy = 0; gy < ghost.Height; gy++)
            {
                int y = gy + source.Y;
                if ((uint)y >= (uint)h) continue;
                for (int gx = 0; gx < ghost.Width; gx++)
                {
                    int x = gx + source.X;
                    if ((uint)x >= (uint)w) continue;
                    var c = gp[gy * ghost.Width + gx];
                    if (c.A == 0) continue;
                    if (e.Tint) c = LayerEffects.Mix(c, e.TintColor, e.TintColor.A);
                    byte a = (byte)((c.A * opacity + 127) / 255);
                    if (a == 0) continue;
                    int i = y * w + x;
                    ghosts[i] = Color32.BlendOver(ghosts[i], c.WithAlpha(a));
                }
            }
        }
        if (ghosts is null) return;
        var px = image.Pixels;
        for (int i = 0; i < px.Length; i++)
            if (ghosts[i].A != 0) px[i] = Color32.BlendOver(ghosts[i], px[i]);
    }
}
