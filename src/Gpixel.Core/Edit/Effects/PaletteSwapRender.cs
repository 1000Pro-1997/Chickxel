using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>색 바꾸기(<see cref="PaletteSwapEffect"/>) 그리기.</summary>
internal static class PaletteSwapRender
{
    public static void Apply(PixelImage image, PaletteSwapEffect p)
    {
        var pairs = p.Pairs;
        if (pairs.Count == 0) return;
        int tol = p.Tolerance;
        var px = image.Pixels;
        // 같은 색이 많으므로 한 번 찾은 색은 기억한다(그림 색 수만큼)
        var memo = new Dictionary<uint, Color32>();
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.A == 0) continue;
            if (!memo.TryGetValue(c.Argb, out var result))
            {
                result = c;
                foreach (var pair in pairs)
                {
                    var f = pair.From;
                    if (Math.Abs(c.R - f.R) > tol || Math.Abs(c.G - f.G) > tol || Math.Abs(c.B - f.B) > tol) continue;
                    result = pair.To.WithAlpha((byte)((c.A * pair.To.A + 127) / 255));
                    break;
                }
                memo[c.Argb] = result;
            }
            px[i] = result;
        }
    }
}
