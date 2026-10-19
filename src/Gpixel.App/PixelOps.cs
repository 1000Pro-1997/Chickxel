using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>편집기 전용 픽셀 연산: 외곽선, 영역 밀기(순환), 밝게/어둡게.</summary>
public static class PixelOps
{
    /// <summary>
    /// 영역 안의 불투명 픽셀 둘레(투명 픽셀 중 이웃이 불투명한 곳)에 1픽셀 외곽선을 그린다.
    /// <paramref name="diagonal"/>이면 대각선 이웃도 본다. 바뀐 픽셀 수를 돌려준다.
    /// </summary>
    public static int AddOutline(PixelImage image, Rectangle region, Color32 color, bool diagonal)
    {
        var r = Rectangle.Intersect(region, image.Bounds);
        if (r.IsEmpty) return 0;
        var src = image.Crop(r);
        int changed = 0;
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                if (src.Pixels[y * r.Width + x].A != 0) continue;
                bool touch = false;
                for (int dy = -1; dy <= 1 && !touch; dy++)
                    for (int dx = -1; dx <= 1 && !touch; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (!diagonal && dx != 0 && dy != 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx < (uint)r.Width && (uint)ny < (uint)r.Height && src.Pixels[ny * r.Width + nx].A != 0) touch = true;
                    }
                if (touch && image.Set(r.X + x, r.Y + y, color)) changed++;
            }
        return changed;
    }

    /// <summary>
    /// <paramref name="source"/>의 영역 내용을 (dx,dy)만큼 밀어 <paramref name="target"/>의 같은 영역에 쓴다.
    /// <paramref name="wrap"/>이면 밖으로 나간 픽셀이 반대편으로 들어오고, 아니면 빈 곳은 투명이 된다.
    /// </summary>
    public static void Shift(PixelImage source, PixelImage target, Rectangle region, int dx, int dy, bool wrap)
    {
        var r = Rectangle.Intersect(region, source.Bounds);
        int w = r.Width, h = r.Height;
        if (w == 0 || h == 0) return;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = x - dx, sy = y - dy;
                Color32 c;
                if (wrap) c = source.Pixels[(r.Y + Mod(sy, h)) * source.Width + r.X + Mod(sx, w)];
                else c = (uint)sx < (uint)w && (uint)sy < (uint)h ? source.Pixels[(r.Y + sy) * source.Width + r.X + sx] : Color32.Transparent;
                target.Pixels[(r.Y + y) * target.Width + r.X + x] = c;
            }
    }

    public static int Mod(int a, int m) => ((a % m) + m) % m;

    /// <summary>명도를 <paramref name="amount"/>(0~1, 예: 0.1 = 10%)만큼 올리거나 내린 색. 투명은 그대로. 이미 가장 밝으면 채도를 내린다.</summary>
    public static Color32 Dodge(Color32 c, bool lighten, float amount)
    {
        if (c.A == 0) return c;
        var (h, s, v) = c.ToHsv();
        if (lighten)
        {
            if (v >= 0.999f) s = Math.Max(0, s - amount);
            else v = Math.Min(1, v + amount);
        }
        else v = Math.Max(0, v - amount);
        return Color32.FromHsv(h, s, v, c.A);
    }
}
