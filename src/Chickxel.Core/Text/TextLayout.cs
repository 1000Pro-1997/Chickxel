using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Text;

/// <summary>
/// 글씨 효과(<see cref="TextEffect"/>)의 글자 배치: 줄('\n')마다 너비를 재어 맞춤에 따라 줄 왼쪽 끝을 정하고(왼쪽 = X, 가운데 = X − ⌊너비/2⌋,
/// 오른쪽 = X − 너비), i번째 줄 위쪽 끝 = Y + i × (줄 높이 + 줄 간격). 좌표는 효과 원본 왼쪽 위 기준 px다. 그리기와 앱의 글씨 고르기(<see cref="Bounds"/>)가 같이 쓴다.
/// </summary>
public static class TextLayout
{
    /// <summary>
    /// 모든 글자(타자기 진행과 관계없이)의 잉크 사각형을 합친 사각형(효과 원본 왼쪽 위 기준). 찍히는 글자가 없으면 <see cref="Rectangle.Empty"/>.
    /// </summary>
    public static Rectangle Bounds(TextEffect e)
    {
        var bounds = Rectangle.Empty;
        foreach (var p in Place(e, PixelFonts.Get(e.Font), out _))
            bounds = bounds.IsEmpty ? p.Rect : Rectangle.Union(bounds, p.Rect);
        return bounds;
    }

    /// <summary>
    /// 잉크가 있는 글자들의 자리(글 순서). <paramref name="count"/> = 타자기가 세는 글자 수(줄바꿈을 뺀 코드 포인트 — 탭·없는 글자도 하나).
    /// </summary>
    internal static List<PlacedGlyph> Place(TextEffect e, PixelFont font, out int count)
    {
        var placed = new List<PlacedGlyph>();
        count = 0;
        var rest = e.Text.AsSpan();
        int spacing = e.LetterSpacing, lineTop = e.Y, pen;
        while (true)
        {
            int end = rest.IndexOf('\n');
            var line = end < 0 ? rest : rest[..end];
            int width = font.LineWidth(line, spacing);
            pen = e.Align switch
            {
                TextAlign.Center => e.X - (int)Math.Floor(width / 2.0),
                TextAlign.Right => e.X - width,
                _ => e.X,
            };
            foreach (var rune in line.EnumerateRunes())
            {
                int order = count++;
                if (rune.Value == '\t')
                {
                    if (font.Space < 0) continue;
                    for (int i = 0; i < PixelFont.TabSpaces; i++) Put(font.Space, order);
                }
                else if (font.Find(rune.Value) is int glyph and >= 0)
                {
                    Put(glyph, order);
                }
            }
            if (end < 0) break;
            rest = rest[(end + 1)..];
            lineTop += font.LineHeight + e.LineSpacing;
        }
        return placed;

        void Put(int glyph, int order)
        {
            ref readonly var g = ref font[glyph];
            if (g.Width > 0 && g.Height > 0)
                placed.Add(new PlacedGlyph(glyph, order, new Rectangle(pen + g.XOffset, lineTop + font.Ascent - (g.YOffset + g.Height), g.Width, g.Height)));
            pen += g.Advance + spacing;
        }
    }
}

/// <summary>놓인 글자 하나: 글자 번호, 타자기 순서(0부터), 잉크 사각형(효과 원본 기준).</summary>
internal readonly record struct PlacedGlyph(int Glyph, int Order, Rectangle Rect);
