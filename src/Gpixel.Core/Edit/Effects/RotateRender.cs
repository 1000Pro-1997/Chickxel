using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>
/// 회전(<see cref="RotateEffect"/>) 그리기: RotSprite 방식 — 보이는 그림을 Scale2x(EPX)로 k번 키우고(한 변 128px 이하 3번 = 8배, 256px 이하 2번, 512px 이하 1번, 그보다 크면 0번)
/// 결과 픽셀마다 가운데 점을 거꾸로 돌려 키운 그림에서 최근접으로 집는다(새 색·반투명 없음). 90°의 배수는 키우지 않고 최근접으로(손실 없음).
/// </summary>
internal static class RotateRender
{
    public static void Apply(PixelImage image, RotateEffect e, Rectangle source)
    {
        double deg = e.DegreesAt(e.Progress);
        double turn = deg % 360;
        if (Math.Abs(turn) < 1e-9) return; // 0° · 한 바퀴 = 원래 그림
        var b = image.GetOpaqueBounds();
        if (b.IsEmpty) return;
        var (ax, ay) = e.Anchor switch
        {
            SquashAnchor.BottomCenter => (b.Left + b.Width / 2.0, (double)b.Bottom),
            SquashAnchor.Middle => (b.Left + b.Width / 2.0, b.Top + b.Height / 2.0),
            _ => LayerEffects.PivotIn(e.PivotPoint, source),
        };
        double quarter = turn / 90;
        bool exact = Math.Abs(quarter - Math.Round(quarter)) < 1e-9;
        double cos, sin;
        if (exact)
        {
            int q = (((int)Math.Round(quarter) % 4) + 4) % 4;
            (cos, sin) = q switch { 0 => (1.0, 0.0), 1 => (0.0, 1.0), 2 => (-1.0, 0.0), _ => (0.0, -1.0) };
        }
        else
        {
            double r = deg * Math.PI / 180;
            (cos, sin) = (Math.Cos(r), Math.Sin(r));
        }

        // 원본 그림 둘레 1px(Scale2x 이웃 투명) 잘라 키운다
        var crop = Rectangle.Inflate(b, 1, 1);
        int levels = exact ? 0 : Math.Max(b.Width, b.Height) switch { <= 128 => 3, <= 256 => 2, <= 512 => 1, _ => 0 };
        var big = image.Crop(crop);
        Normalize(big);
        for (int k = 0; k < levels; k++) big = Scale2x(big);
        int scale = 1 << levels;

        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        Array.Clear(px);
        // 돌린 그림이 닿는 자리(원본 범위 네 모서리를 돌린 사각형, 한 칸 여유)
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (cx, cy) in new[] { (crop.Left, crop.Top), (crop.Right, crop.Top), (crop.Left, crop.Bottom), (crop.Right, crop.Bottom) })
        {
            double dx = cx - ax, dy = cy - ay;
            double rx = ax + cos * dx - sin * dy, ry = ay + sin * dx + cos * dy;
            minX = Math.Min(minX, rx);
            maxX = Math.Max(maxX, rx);
            minY = Math.Min(minY, ry);
            maxY = Math.Max(maxY, ry);
        }
        int x0 = Math.Max(0, (int)Math.Floor(minX) - 1), x1 = Math.Min(w, (int)Math.Ceiling(maxX) + 1);
        int y0 = Math.Max(0, (int)Math.Floor(minY) - 1), y1 = Math.Min(h, (int)Math.Ceiling(maxY) + 1);
        int bw = big.Width, bh = big.Height;
        var bp = big.Pixels;
        for (int y = y0; y < y1; y++)
        {
            double dy = y + 0.5 - ay;
            for (int x = x0; x < x1; x++)
            {
                double dx = x + 0.5 - ax;
                // 거꾸로 돌린 자리(원본 좌표) → 키운 그림 칸
                double sx = ax + cos * dx + sin * dy, sy = ay - sin * dx + cos * dy;
                int ix = (int)Math.Floor((sx - crop.X) * scale), iy = (int)Math.Floor((sy - crop.Y) * scale);
                if ((uint)ix >= (uint)bw || (uint)iy >= (uint)bh) continue;
                px[y * w + x] = bp[iy * bw + ix];
            }
        }
    }

    /// <summary>알파 0인 픽셀을 하나의 투명으로(Scale2x가 RGB만 다른 투명을 다른 색으로 보지 않게).</summary>
    private static void Normalize(PixelImage image)
    {
        var p = image.Pixels;
        for (int i = 0; i < p.Length; i++)
            if (p[i].A == 0) p[i] = Color32.Transparent;
    }

    /// <summary>Scale2x(EPX): 픽셀 하나를 2×2로, 이웃 모양을 따라 모서리를 이웃 색으로(새 색 없음, 대각선이 계단 대신 이어짐).</summary>
    internal static PixelImage Scale2x(PixelImage s)
    {
        int w = s.Width, h = s.Height;
        var result = new PixelImage(w * 2, h * 2);
        var src = s.Pixels;
        var dst = result.Pixels;
        int dw = w * 2;
        Color32 At(int x, int y) => (uint)x < (uint)w && (uint)y < (uint)h ? src[y * w + x] : Color32.Transparent;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var e = src[y * w + x];
                Color32 e0 = e, e1 = e, e2 = e, e3 = e;
                var b = At(x, y - 1);
                var d = At(x - 1, y);
                var f = At(x + 1, y);
                var hh = At(x, y + 1);
                if (!b.Equals(hh) && !d.Equals(f))
                {
                    if (d.Equals(b)) e0 = d;
                    if (b.Equals(f)) e1 = f;
                    if (d.Equals(hh)) e2 = d;
                    if (hh.Equals(f)) e3 = f;
                }
                int i = y * 2 * dw + x * 2;
                dst[i] = e0;
                dst[i + 1] = e1;
                dst[i + dw] = e2;
                dst[i + dw + 1] = e3;
            }
        return result;
    }
}
