using System.Drawing;
using Chickxel.Core.Model;
using Chickxel.Core.Text;

namespace Chickxel.Core.Edit;

internal static class TextRender
{
    /// <summary>
    /// 글씨: 글자 잉크 픽셀에 색을 덮는다(효과 원본 영역 밖은 잘림 — 여백 없음). 글자끼리 겹친 픽셀(음수 글자 간격)도 한 번만 덮는다.
    /// 타자기면 진행(빠르기 곡선·반복 포함) × 글자 수만큼 앞 글자부터(진행 1이면 전부). 색 그라데이션의 범위는 글 전체의 잉크 사각형이다.
    /// </summary>
    public static void Apply(PixelImage image, TextEffect e, Rectangle source)
    {
        if (e.Text.Length == 0) return;
        var font = PixelFonts.Get(e.Font);
        var placed = TextLayout.Place(e, font, out int count);
        int visible = !e.Typewriter || e.Progress >= 1 ? count : (int)Math.Floor(e.Progress * count);
        if (visible <= 0 || placed.Count == 0) return;

        // 글 전체·보이는 글자의 잉크 사각형(이미지 좌표)
        Rectangle all = Rectangle.Empty, shown = Rectangle.Empty;
        foreach (var p in placed)
        {
            var r = p.Rect with { X = p.Rect.X + source.X, Y = p.Rect.Y + source.Y };
            all = all.IsEmpty ? r : Rectangle.Union(all, r);
            if (p.Order < visible) shown = shown.IsEmpty ? r : Rectangle.Union(shown, r);
        }
        var area = Rectangle.Intersect(shown, Rectangle.Intersect(source, new Rectangle(0, 0, image.Width, image.Height)));
        if (area.Width <= 0 || area.Height <= 0) return;

        var mask = new bool[area.Width * area.Height];
        foreach (var p in placed)
        {
            if (p.Order >= visible) continue;
            int left = p.Rect.X + source.X, top = p.Rect.Y + source.Y;
            int y0 = Math.Max(0, area.Top - top), y1 = Math.Min(p.Rect.Height, area.Bottom - top);
            int x0 = Math.Max(0, area.Left - left), x1 = Math.Min(p.Rect.Width, area.Right - left);
            for (int gy = y0; gy < y1; gy++)
            {
                int row = (top + gy - area.Top) * area.Width + left - area.Left;
                for (int gx = x0; gx < x1; gx++)
                    if (font.Ink(p.Glyph, gx, gy)) mask[row + gx] = true;
            }
        }

        var color = EffectGradientSampler.For(e, nameof(TextEffect.Color), e.ColorGradients.ContainsKey(nameof(TextEffect.Color)) ? all : default,
            e.Color, source.X, source.Y);
        var px = image.Pixels;
        int w = image.Width;
        for (int y = 0; y < area.Height; y++)
        {
            int iy = area.Top + y;
            for (int x = 0; x < area.Width; x++)
            {
                if (!mask[y * area.Width + x]) continue;
                int ix = area.Left + x, i = iy * w + ix;
                px[i] = Color32.BlendOver(px[i], color.Sample(ix, iy));
            }
        }
    }
}
