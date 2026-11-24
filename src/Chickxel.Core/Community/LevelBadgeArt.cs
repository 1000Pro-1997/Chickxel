using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.Core.Community;

/// <summary>
/// 계정 레벨 배지의 픽셀 그림(앱은 정수 배로 키워 또렷하게 보임). 3×5 픽셀 글꼴의 "Lv N", 모서리를 깎은 테두리, 모양(<see cref="BadgeSkin"/>)마다
/// 바탕·지나가는 빛·둘레 빛·반짝이·불티. 장착한 배지 보상이 없으면 레벨 단계 색만 보이는 멈춘 모양(1~4 회색, 5~9 동, 10~19 은, 20~29 금, 30~49 청록–보라,
/// 50+ 무지개 띠 — 움직임은 배지 보상에만).
/// 세 겹으로 그린다: 아래(<see cref="RenderUnder"/> — 둘레 빛·테두리·바탕·빛 띠) → 글자(<see cref="RenderText"/>) → 위(<see cref="RenderOver"/> — 반짝이·불티).
/// 홈페이지는 아래·위 겹을 스프라이트로 받고 글자는 <see cref="Font"/>로 직접 찍는다(레벨 숫자가 사람마다 달라서).
/// 배지 둘레에 <see cref="Margin"/>칸의 여백이 있어 빛·반짝이는 배지 밖까지 번진다. 그림은 프레임 번호만으로 정해지고 <see cref="RewardArt.Loop"/>마다 되풀이된다.
/// </summary>
public sealed class LevelBadgeArt : IPixelArt
{
    /// <summary>배지 둘레 여백(빛 테두리·반짝이·불티 자리, 픽셀).</summary>
    public const int Margin = 3;
    /// <summary>배지 높이(테두리 1 + 여백 1 + 글자 5 + 여백 1 + 테두리 1).</summary>
    public const int BadgeHeight = 9;
    /// <summary>배지 안 글자 시작 자리와 글자·빈칸 너비.</summary>
    public const int TextX = 3, TextY = 2, GlyphAdvance = 4, SpaceAdvance = 2;

    private const int GlyphW = 3, SparkleLife = 12, EmberLife = 8;

    /// <summary>3×5 글꼴: 위 줄부터 세 칸씩(1 = 칠함).</summary>
    public static IReadOnlyDictionary<char, string> Font { get; } = new Dictionary<char, string>
    {
        ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111",
        ['4'] = "101101111001001", ['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001001010010",
        ['8'] = "111101111101111", ['9'] = "111101111001111", ['L'] = "100100100100111", ['v'] = "000000101101010",
    };

    private readonly string _text;
    private readonly int _shinePeriod;

    /// <summary>레벨 <paramref name="level"/>의 배지 — <paramref name="skin"/>(배지 보상 ID)이 없거나 모르는 ID면 레벨 단계 모양.</summary>
    public LevelBadgeArt(int level, string? skin = null)
    {
        Level = Math.Max(1, level);
        Skin = BadgeSkin.Find(skin) ?? BadgeSkin.ForTier(Level);
        _text = "Lv " + Level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        BadgeWidth = WidthFor(Level.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        // 빛이 배지를 다 지나간 뒤에 다시 오게(긴 레벨 숫자도) — 되풀이 길이의 약수로만 늘린다.
        _shinePeriod = Skin.ShinePeriod <= 0 ? 0
            : new[] { 24, 30, 40, 60, 120 }.First(p => p >= Skin.ShinePeriod && p >= (BadgeWidth + BadgeHeight) / 2 + 6);
    }

    public int Level { get; }
    public BadgeSkin Skin { get; }
    /// <summary>배지(테두리 포함) 폭 — 여백 뺌.</summary>
    public int BadgeWidth { get; }
    public int Width => BadgeWidth + 2 * Margin;
    public int Height => BadgeHeight + 2 * Margin;
    public ArtBox Box => new(Margin, Margin, BadgeWidth, BadgeHeight);
    public int Frames => Skin.Animated ? Loop : 1;

    /// <summary>레벨 숫자가 <paramref name="digits"/>자리인 배지의 폭.</summary>
    public static int WidthFor(int digits) => TextX * 2 + 2 * GlyphAdvance + SpaceAdvance + digits * GlyphAdvance - 1;

    public void Render(int frame, Span<Color32> pixels)
    {
        RenderUnder(frame, pixels);
        RenderText(pixels);
        RenderOver(frame, pixels);
    }

    /// <summary>아래 겹: 둘레 빛, 테두리, 바탕, 지나가는 빛(버퍼를 비우고 그림).</summary>
    public void RenderUnder(int frame, Span<Color32> pixels)
    {
        frame = Frames > 1 ? Mod(frame, Loop) : 0;
        pixels[..(Width * Height)].Clear();
        var p = new PixelPainter(pixels, Width, Height);
        if (Skin.Glow != BadgeGlow.None) DrawGlow(frame, p);
        for (int y = 0; y < BadgeHeight; y++)
            for (int x = 0; x < BadgeWidth; x++)
            {
                if (IsCorner(x, y)) continue;
                bool edge = x == 0 || x == BadgeWidth - 1 || y == 0 || y == BadgeHeight - 1;
                p.Set(x + Margin, y + Margin, edge ? OutlineAt(frame) : Fill(x, y, frame));
            }
        if (_shinePeriod > 0) DrawShine(frame, p);
    }

    /// <summary>글자 겹: 글자 아래 한 칸 그림자, 그 위 글자.</summary>
    public void RenderText(Span<Color32> pixels)
    {
        var p = new PixelPainter(pixels, Width, Height);
        int maxY = BadgeHeight - 2;
        for (int pass = 0; pass < 2; pass++)
        {
            int gx = TextX;
            foreach (char ch in _text)
            {
                if (ch == ' ') { gx += SpaceAdvance; continue; }
                string glyph = Font[ch];
                for (int i = 0; i < glyph.Length; i++)
                {
                    if (glyph[i] != '1') continue;
                    int x = gx + i % GlyphW, y = TextY + i / GlyphW;
                    if (pass == 1) p.Set(x + Margin, y + Margin, Skin.Text);
                    else if (y + 1 <= maxY) p.Set(x + Margin, y + 1 + Margin, Skin.Shadow);
                }
                gx += GlyphAdvance;
            }
        }
    }

    /// <summary>위 겹: 반짝이·불티(버퍼에 덧그림).</summary>
    public void RenderOver(int frame, Span<Color32> pixels)
    {
        frame = Frames > 1 ? Mod(frame, Loop) : 0;
        var p = new PixelPainter(pixels, Width, Height);
        for (int i = 0; i < Skin.Sparkles; i++) DrawSparkle(frame, i, p);
        for (int i = 0; i < Skin.Embers; i++) DrawEmber(frame, i, p);
    }

    private bool IsCorner(int x, int y) => (x == 0 || x == BadgeWidth - 1) && (y == 0 || y == BadgeHeight - 1);

    /// <summary>네온은 테두리 색이 분홍 ↔ 청록으로 천천히 바뀐다.</summary>
    private Color32 OutlineAt(int frame)
        => Skin.Fill == BadgeFill.Neon ? Mix(Skin.Outline, Hex(0x22D3EE), Pulse(frame, 40)) : Skin.Outline;

    /// <summary>배지 안 바탕 한 칸: 위 줄은 밝게, 아래 줄은 어둡게(픽셀 그림 명암 띠).</summary>
    private Color32 Fill(int x, int y, int frame)
    {
        int band = y == 1 ? 0 : y == BadgeHeight - 2 ? 2 : 1;
        Color32 Shade(Color32 c) => band switch { 0 => Lighten(c, 0.35), 2 => Darken(c, 0.25), _ => c };
        switch (Skin.Fill)
        {
            case BadgeFill.Flow:
                {
                    double t = Frac((x + y) / 12.0 + frame / 40.0);
                    double tri = t < 0.5 ? t * 2 : 2 - t * 2;
                    return Shade(Mix(Skin.Top, Skin.Bottom, Math.Round(tri * 3) / 3));
                }
            case BadgeFill.Rainbow:
                return Shade(Darken(Rainbow[Mod((int)Math.Floor((x + y - frame) / 4.0), Rainbow.Length)], 0.3));
            case BadgeFill.Seeds:
                {
                    var c = band switch { 0 => Skin.Top, 2 => Skin.Bottom, _ => Skin.Mid };
                    return band == 1 && Mod(x * 3 + y * 5, 7) == 0 ? Hex(0xFFE08A) : c;
                }
            case BadgeFill.Waves:
                {
                    // 가로로 흐르는 물결: 물결선 위는 밝은 물, 아래는 깊은 물, 선에는 거품
                    int wave = 3 + (int)Math.Round(Math.Sin(2 * Math.PI * (x / 8.0 + frame / 24.0)));
                    if (y == wave) return Lighten(Skin.Top, 0.55);
                    return y < wave ? Skin.Top : Mix(Skin.Mid, Skin.Bottom, (y - wave) / 4.0);
                }
            case BadgeFill.Lava:
                {
                    double v = Math.Sin(x * 0.9 + 2 * Math.PI * frame / 40) + Math.Sin(y * 1.7 - 2 * Math.PI * frame / 30) + HashUnit(x, y, 7) * 0.6;
                    Color32[] lava = [Hex(0x5A0A00), Hex(0xB01E00), Hex(0xF05A00), Hex(0xFFB020)];
                    return lava[Math.Clamp((int)Math.Floor((v + 2) / 4.6 * lava.Length), 0, lava.Length - 1)];
                }
            case BadgeFill.Galaxy:
                {
                    double t = Frac((x - y * 2) / 16.0 + frame / 60.0);
                    double tri = t < 0.5 ? t * 2 : 2 - t * 2;
                    var c = Mix(Skin.Top, Skin.Bottom, Math.Round(tri * 3) / 3);
                    // 별: 글자가 없는 위·아래 줄에만(글자가 또렷하게), 자리는 그대로, 별마다 다른 때에 반짝
                    if ((y == 1 || y == BadgeHeight - 2) && HashInt(x, y, 37, 3) == 0)
                    {
                        double twinkle = Pulse(frame + HashInt(x, y, 38, 24), 24);
                        return Mix(c, Color32.White, 0.3 + 0.7 * twinkle);
                    }
                    return c;
                }
            case BadgeFill.Facets:
                {
                    // 대각선 결정 면: 면마다 밝기가 다름
                    int face = Mod((int)Math.Floor((x + y) / 3.0) + (int)Math.Floor((x - y + 20) / 4.0), 3);
                    return Shade(face switch { 0 => Skin.Top, 1 => Skin.Mid, _ => Skin.Bottom });
                }
            case BadgeFill.Neon:
                return band switch { 0 => Skin.Top, 2 => Skin.Bottom, _ => Skin.Mid };
            default:
                return band switch { 0 => Skin.Top, 2 => Skin.Bottom, _ => Skin.Mid };
        }
    }

    /// <summary>왼쪽 아래 → 오른쪽 위로 기운 빛 띠가 한 칸씩 지나간다(배지 바탕만 — 글자는 그 위에 또렷하게).</summary>
    private void DrawShine(int frame, PixelPainter p)
    {
        int pos = Mod(frame, _shinePeriod) * 2 - BadgeHeight;
        for (int y = 1; y < BadgeHeight - 1; y++)
            for (int x = 1; x < BadgeWidth - 1; x++)
            {
                int d = x + (BadgeHeight - 1 - y) - pos;
                if (d < 0 || d >= Skin.ShineWidth) continue;
                var c = p.Get(x + Margin, y + Margin);
                p.Set(x + Margin, y + Margin, Mix(c, Color32.White, d == 0 && Skin.ShineWidth > 1 ? 0.35 : 0.6));
            }
    }

    /// <summary>배지 둘레 빛: 바로 바깥 한 겹(두 겹이면 더 흐리게), 밝기가 숨 쉬듯 오르내림. 무지개는 둘레를 돈다.</summary>
    private void DrawGlow(int frame, PixelPainter p)
    {
        double pulse = Pulse(frame, 20);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int bx = x - Margin, by = y - Margin;
                int dx = bx < 0 ? -bx : bx >= BadgeWidth ? bx - BadgeWidth + 1 : 0;
                int dy = by < 0 ? -by : by >= BadgeHeight ? by - BadgeHeight + 1 : 0;
                int d = dx + dy;
                // 깎은 모서리 칸은 배지 바로 옆 칸으로 본다
                if (d == 0 && IsCorner(bx, by)) d = 1;
                if (d == 0 || d > Skin.GlowRings) continue;
                var color = Skin.Glow switch
                {
                    BadgeGlow.Rainbow => Rainbow[Mod((x + y + frame) / 2, Rainbow.Length)],
                    BadgeGlow.Neon => OutlineAt(frame),
                    _ => Skin.GlowColor,
                };
                double alpha = d == 1 ? 0.30 + 0.30 * pulse : 0.10 + 0.15 * pulse;
                p.Set(x, y, Fade(color, alpha));
            }
    }

    /// <summary>
    /// 반짝이 하나: 배지 테두리 근처 아무 데서 피었다 진다(별: 점 → 십자 → 네 갈래 별, 하트·잎·눈송이는 그 모양). 반짝이마다 시작이 어긋나 번갈아 피고,
    /// 필 때마다 자리가 바뀐다.
    /// </summary>
    private void DrawSparkle(int frame, int index, PixelPainter p)
    {
        var (age, cycle) = Life(frame, index, Skin.Sparkles, SparkleLife);
        if (age >= 10) return;
        uint h = Hash(Seed(Skin.Id), index, cycle);
        int side = (int)(h % 4), along = (int)(h >> 3), off = (int)(h >> 9) % 2;
        var (cx, cy) = side switch
        {
            0 => (Margin + along % BadgeWidth, Margin - off),
            1 => (Margin + along % BadgeWidth, Margin + BadgeHeight - 1 + off),
            2 => (Margin - off, Margin + along % BadgeHeight),
            _ => (Margin + BadgeWidth - 1 + off, Margin + along % BadgeHeight),
        };
        var color = Skin.RainbowSparkles ? Mix(Rainbow[(index + cycle) % Rainbow.Length], Color32.White, 0.4) : Skin.Sparkle;
        int size = age switch { <= 1 or 9 => 0, <= 3 or 7 or 8 => 1, _ => 2 };
        Sparkle(p, cx, cy, size, Skin.SparkleShape, color);
    }

    /// <summary>모양 하나를 크기 0(점)·1·2로 찍는다(배지·테두리·효과가 같이 씀).</summary>
    public static void Sparkle(PixelPainter p, int cx, int cy, int size, SparkleShape shape, Color32 color)
    {
        if (size == 0)
        {
            p.Blend(cx, cy, Fade(color, 0.8));
            return;
        }
        switch (shape)
        {
            case SparkleShape.Heart:
                p.Sprite(cx - 1, cy - 1, size == 1 ? ["x.x", "xxx", ".x."] : ["xx.xx", "xxxxx", ".xxx.", "..x.."], _ => color);
                if (size == 2) p.Blend(cx - 1, cy, Lighten(color, 0.6));
                return;
            case SparkleShape.Leaf:
                p.Sprite(cx - 1, cy - 1, size == 1 ? [".x", "x."] : [".xx", "xx.", "x.."], _ => color);
                if (size == 2) p.Blend(cx, cy - 1, Lighten(color, 0.4));
                return;
            case SparkleShape.Snow:
                p.Blend(cx, cy, Color32.White);
                for (int r = 1; r <= size; r++)
                {
                    var arm = r == 2 ? Fade(color, 0.7) : color;
                    p.Blend(cx - r, cy, arm); p.Blend(cx + r, cy, arm); p.Blend(cx, cy - r, arm); p.Blend(cx, cy + r, arm);
                }
                if (size == 2)
                {
                    p.Blend(cx - 1, cy - 1, Fade(color, 0.6)); p.Blend(cx + 1, cy - 1, Fade(color, 0.6));
                    p.Blend(cx - 1, cy + 1, Fade(color, 0.6)); p.Blend(cx + 1, cy + 1, Fade(color, 0.6));
                }
                return;
            default:
                p.Blend(cx, cy, Color32.White);
                for (int r = 1; r <= size; r++)
                {
                    var arm = r == 2 ? Fade(color, 0.67) : color;
                    p.Blend(cx - r, cy, arm); p.Blend(cx + r, cy, arm); p.Blend(cx, cy - r, arm); p.Blend(cx, cy + r, arm);
                }
                return;
        }
    }

    /// <summary>불티: 배지 위 끝에서 한 점이 두 프레임에 한 칸씩 올라가며 흐려진다.</summary>
    private void DrawEmber(int frame, int index, PixelPainter p)
    {
        var (age, cycle) = Life(frame, index, Skin.Embers, EmberLife);
        uint h = Hash(Seed(Skin.Id) + 1000, index, cycle);
        int x = Margin + 1 + (int)(h % (uint)Math.Max(1, BadgeWidth - 2));
        int y = Margin - 1 - age / 2;
        var color = Skin.EmberColors.Length > 0 ? Skin.EmberColors[(int)(h >> 8) % Skin.EmberColors.Length] : Color32.White;
        p.Blend(x, y, color.WithAlpha((byte)(255 - age * 22)));
    }
}
