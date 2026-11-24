using Chickxel.Core.Model;

namespace Chickxel.Core.Community;

/// <summary>
/// 계정 레벨 배지의 픽셀 그림(앱은 정수 배로 키워 또렷하게 보임). 3×5 픽셀 글꼴의 "Lv N", 모서리를 깎은 테두리, 위 밝은 줄·아래 어두운 줄 명암.
/// 단계(<see cref="CommunityLevel.Tier"/>)가 높을수록 화려하다 — 흐림: 멈춤 · 동: 가끔 지나가는 빛 · 은: 더 자주·굵게 · 금: 빛 + 반짝이 ·
/// 크리스털: 청록–보라가 흐르는 바탕 + 숨 쉬는 빛 테두리 + 반짝이 · 전설: 흐르는 무지개 + 무지개 빛 테두리 두 겹 + 반짝이 + 위로 오르는 불티.
/// 같은 단계 안에서도 레벨이 오를수록 반짝이·불티가 늘어난다. 그림은 프레임 번호만으로 정해진다(같은 프레임 = 같은 그림).
/// 배지 둘레에 <see cref="Margin"/>칸의 여백이 있어 빛·반짝이는 배지 밖까지 번진다.
/// </summary>
public sealed class LevelBadgeArt
{
    /// <summary>배지 둘레 여백(빛 테두리·반짝이·불티 자리, 픽셀).</summary>
    public const int Margin = 3;
    /// <summary>배지 높이(테두리 1 + 여백 1 + 글자 5 + 여백 1 + 테두리 1).</summary>
    public const int BadgeHeight = 9;
    /// <summary>프레임 하나의 길이(ms) — 픽셀 그림답게 초당 12.5장.</summary>
    public const int FrameMs = 80;

    private const int GlyphW = 3, GlyphH = 5, TextX = 3, TextY = 2, SparkleLife = 12, EmberLife = 8;

    /// <summary>3×5 글꼴: 위 줄부터 세 칸씩(1 = 칠함).</summary>
    private static readonly Dictionary<char, string> Font = new()
    {
        ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111",
        ['4'] = "101101111001001", ['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001001010010",
        ['8'] = "111101111101111", ['9'] = "111101111001111", ['L'] = "100100100100111", ['v'] = "000000101101010",
    };

    private static readonly Color32[] Rainbow =
        [Hex(0xE8475A), Hex(0xE59A2E), Hex(0xF2D33B), Hex(0x2FB868), Hex(0x2C84D8), Hex(0x9050E0)];
    private static readonly Color32 Cyan = Hex(0x22D3EE), Violet = Hex(0x8B5CF6), DeepCyan = Hex(0x0891B2), DeepViolet = Hex(0x6D28D9);

    /// <summary>단계의 색: 테두리, 바탕 위·가운데·아래 줄, 글자, 글자 그림자, 반짝이.</summary>
    private sealed record Palette(Color32 Outline, Color32 Top, Color32 Mid, Color32 Bottom, Color32 Text, Color32 Shadow, Color32 Sparkle);

    private static readonly Palette PlainColors = new(Hex(0x55555F), Hex(0x4A4A54), Hex(0x3C3C44), Hex(0x33333A), Hex(0xC8C8D0), Hex(0x26262C), Hex(0xFFFFFF));
    private static readonly Palette BronzeColors = new(Hex(0x5A3416), Hex(0xE0A066), Hex(0xB87333), Hex(0x8E5527), Hex(0xFFF1E0), Hex(0x5A3416), Hex(0xFFD9B0));
    private static readonly Palette SilverColors = new(Hex(0x4A525C), Hex(0xF4F7FA), Hex(0xC4CCD6), Hex(0x9AA4B0), Hex(0x1F242B), Hex(0xEEF2F6), Hex(0xFFFFFF));
    private static readonly Palette GoldColors = new(Hex(0x6B4A00), Hex(0xFFF0A8), Hex(0xFFD54A), Hex(0xD9A21B), Hex(0x3A2600), Hex(0xFFF0B0), Hex(0xFFF3B0));
    private static readonly Palette CrystalColors = new(Hex(0x2A1E6B), Cyan, Cyan, Violet, Hex(0xFFFFFF), Hex(0x2A1E6B), Hex(0xBFF6FF));
    private static readonly Palette LegendColors = new(Hex(0x3A1450), Rainbow[0], Rainbow[0], Rainbow[0], Hex(0xFFFFFF), Hex(0x2A0A3A), Hex(0xFFFFFF));

    private readonly Palette _colors;
    private readonly string _text;
    private readonly int _shinePeriod, _shineWidth, _sparkles, _embers;

    public LevelBadgeArt(int level)
    {
        Level = Math.Max(1, level);
        Tier = CommunityLevel.Tier(Level);
        _text = "Lv " + Level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int textWidth = 0;
        foreach (char c in _text) textWidth += c == ' ' ? 2 : GlyphW + 1;
        textWidth--;
        BadgeWidth = TextX + textWidth + TextX;
        (_colors, _shinePeriod, _shineWidth, _sparkles, _embers) = Tier switch
        {
            CommunityLevelTier.Bronze => (BronzeColors, 50, 1, 0, 0),
            CommunityLevelTier.Silver => (SilverColors, 40, 2, 0, 0),
            CommunityLevelTier.Gold => (GoldColors, 32, 2, 1 + (Level - 20) / 4, 0),
            CommunityLevelTier.Crystal => (CrystalColors, 28, 2, 2 + (Level - 30) / 7, 0),
            CommunityLevelTier.Legend => (LegendColors, 24, 3, Math.Min(8, 4 + (Level - 50) / 10), Math.Min(4, 1 + (Level - 50) / 15)),
            _ => (PlainColors, 0, 0, 0, 0),
        };
        // 빛이 배지를 다 지나간 뒤에 다시 오게(긴 레벨 숫자도)
        if (_shinePeriod > 0) _shinePeriod = Math.Max(_shinePeriod, (BadgeWidth + BadgeHeight) / 2 + 6);
    }

    public int Level { get; }
    public CommunityLevelTier Tier { get; }
    /// <summary>배지(테두리 포함) 폭 — 여백 뺌.</summary>
    public int BadgeWidth { get; }
    /// <summary>그림 전체(여백 포함) 폭·높이.</summary>
    public int Width => BadgeWidth + 2 * Margin;
    public int Height => BadgeHeight + 2 * Margin;
    /// <summary>움직이는지(흐림 단계만 멈춤).</summary>
    public bool Animated => Tier != CommunityLevelTier.Plain;

    /// <summary><paramref name="frame"/>번째 프레임을 <paramref name="pixels"/>(<see cref="Width"/> × <see cref="Height"/>, 위 줄부터)에 그린다.</summary>
    public void Render(int frame, Span<Color32> pixels)
    {
        if (pixels.Length < Width * Height) throw new ArgumentException("pixel buffer too small", nameof(pixels));
        frame = Animated ? Math.Max(0, frame) : 0;
        pixels[..(Width * Height)].Clear();
        if (Tier >= CommunityLevelTier.Crystal) DrawGlow(frame, pixels);
        DrawBadge(frame, pixels);
        DrawText(pixels);
        if (_shinePeriod > 0) DrawShine(frame, pixels);
        for (int i = 0; i < _sparkles; i++) DrawSparkle(frame, i, pixels);
        for (int i = 0; i < _embers; i++) DrawEmber(frame, i, pixels);
    }

    private static bool IsCorner(int x, int y, int w) => (x == 0 || x == w - 1) && (y == 0 || y == BadgeHeight - 1);

    private void DrawBadge(int frame, Span<Color32> pixels)
    {
        int w = BadgeWidth;
        for (int y = 0; y < BadgeHeight; y++)
            for (int x = 0; x < w; x++)
            {
                if (IsCorner(x, y, w)) continue;
                bool edge = x == 0 || x == w - 1 || y == 0 || y == BadgeHeight - 1;
                pixels[(y + Margin) * Width + x + Margin] = edge ? _colors.Outline : Fill(x, y, frame);
            }
    }

    /// <summary>배지 안 바탕 한 칸: 위 줄은 밝게, 아래 줄은 어둡게(픽셀 그림 명암 띠).</summary>
    private Color32 Fill(int x, int y, int frame)
    {
        int band = y == 1 ? 0 : y == BadgeHeight - 2 ? 2 : 1;
        Color32 c;
        switch (Tier)
        {
            case CommunityLevelTier.Crystal:
                {
                    // 대각선으로 흐르는 짙은 청록–보라(네 단계로 끊어 픽셀답게, 흰 글자가 잘 읽히게 짙게)
                    double t = ((x + y) / 12.0 + frame / 40.0) % 1.0;
                    double tri = t < 0.5 ? t * 2 : 2 - t * 2;
                    c = Mix(DeepCyan, DeepViolet, Math.Round(tri * 3) / 3);
                    break;
                }
            case CommunityLevelTier.Legend:
                // 흐르는 무지개(흰 글자가 읽히게 조금 어둡게)
                c = Mix(Rainbow[Mod((int)Math.Floor((x + y - frame) / 3.0), Rainbow.Length)], Color32.Black, 0.3);
                break;
            default:
                return band switch { 0 => _colors.Top, 2 => _colors.Bottom, _ => _colors.Mid };
        }
        return band switch { 0 => Mix(c, Color32.White, 0.35), 2 => Mix(c, Color32.Black, 0.25), _ => c };
    }

    private void DrawText(Span<Color32> pixels)
    {
        // 글자 아래 한 칸 그림자
        int maxY = BadgeHeight - 2;
        for (int pass = 0; pass < 2; pass++)
        {
            int gx = TextX;
            foreach (char ch in _text)
            {
                if (ch == ' ') { gx += 2; continue; }
                string glyph = Font[ch];
                for (int i = 0; i < glyph.Length; i++)
                {
                    if (glyph[i] != '1') continue;
                    int x = gx + i % GlyphW, y = TextY + i / GlyphW;
                    if (pass == 1)
                    {
                        pixels[(y + Margin) * Width + x + Margin] = _colors.Text;
                        continue;
                    }
                    if (y + 1 <= maxY) pixels[(y + 1 + Margin) * Width + x + Margin] = _colors.Shadow;
                }
                gx += GlyphW + 1;
            }
        }
    }

    /// <summary>왼쪽 아래 → 오른쪽 위로 기운 빛 띠가 한 칸씩 지나간다(배지 안만).</summary>
    private void DrawShine(int frame, Span<Color32> pixels)
    {
        int pos = frame % _shinePeriod * 2 - BadgeHeight;
        for (int y = 1; y < BadgeHeight - 1; y++)
            for (int x = 1; x < BadgeWidth - 1; x++)
            {
                int d = x + (BadgeHeight - 1 - y) - pos;
                if (d < 0 || d >= _shineWidth) continue;
                ref var p = ref pixels[(y + Margin) * Width + x + Margin];
                p = Mix(p, Color32.White, d == 0 && _shineWidth > 1 ? 0.35 : 0.6);
            }
    }

    /// <summary>배지 둘레 빛: 바로 바깥 한 겹(전설은 두 겹), 밝기가 숨 쉬듯 오르내림. 전설은 무지개가 둘레를 돈다.</summary>
    private void DrawGlow(int frame, Span<Color32> pixels)
    {
        double pulse = 0.5 + 0.5 * Math.Sin(frame * 2 * Math.PI / 20);
        int rings = Tier == CommunityLevelTier.Legend ? 2 : 1;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int bx = x - Margin, by = y - Margin;
                int dx = bx < 0 ? -bx : bx >= BadgeWidth ? bx - BadgeWidth + 1 : 0;
                int dy = by < 0 ? -by : by >= BadgeHeight ? by - BadgeHeight + 1 : 0;
                int d = dx + dy;
                // 깎은 모서리 칸은 배지 바로 옆 칸으로 본다
                if (d == 0 && IsCorner(bx, by, BadgeWidth)) d = 1;
                if (d == 0 || d > rings) continue;
                var color = Tier == CommunityLevelTier.Legend ? Rainbow[Mod((x + y + frame) / 2, Rainbow.Length)] : Violet;
                double alpha = (d == 1 ? 0.30 + 0.30 * pulse : 0.10 + 0.15 * pulse);
                pixels[y * Width + x] = color.WithAlpha((byte)(alpha * 255));
            }
    }

    /// <summary>
    /// 반짝이 하나: 배지 테두리 근처 아무 데서 점 → 십자 → 네 갈래 별 → 십자 → 점으로 피었다 진다.
    /// 반짝이마다 시작이 어긋나 번갈아 피고, 필 때마다 자리가 바뀐다.
    /// </summary>
    private void DrawSparkle(int frame, int index, Span<Color32> pixels)
    {
        int shifted = frame + index * SparkleLife / Math.Max(1, _sparkles) + index * 5;
        int age = shifted % SparkleLife, cycle = shifted / SparkleLife;
        if (age >= 10) return;
        uint h = Hash(Level, index, cycle);
        int cx, cy;
        int side = (int)(h % 4);
        int along = (int)(h >> 3);
        switch (side)
        {
            case 0: cx = Margin + along % BadgeWidth; cy = Margin - (int)(h >> 9) % 2; break;
            case 1: cx = Margin + along % BadgeWidth; cy = Margin + BadgeHeight - 1 + (int)(h >> 9) % 2; break;
            case 2: cx = Margin - (int)(h >> 9) % 2; cy = Margin + along % BadgeHeight; break;
            default: cx = Margin + BadgeWidth - 1 + (int)(h >> 9) % 2; cy = Margin + along % BadgeHeight; break;
        }
        var color = Tier == CommunityLevelTier.Legend ? Mix(Rainbow[(index + cycle) % Rainbow.Length], Color32.White, 0.4) : _colors.Sparkle;
        int radius = age switch { <= 1 or 9 => 0, <= 3 or 7 or 8 => 1, _ => 2 };
        Put(pixels, cx, cy, radius == 0 ? color.WithAlpha(200) : Color32.White);
        for (int r = 1; r <= radius; r++)
        {
            var arm = r == 2 ? color.WithAlpha(170) : color;
            Put(pixels, cx - r, cy, arm);
            Put(pixels, cx + r, cy, arm);
            Put(pixels, cx, cy - r, arm);
            Put(pixels, cx, cy + r, arm);
        }
    }

    /// <summary>전설: 배지 위 끝에서 무지개 불티 한 점이 두 프레임에 한 칸씩 올라가며 흐려진다.</summary>
    private void DrawEmber(int frame, int index, Span<Color32> pixels)
    {
        int shifted = frame + index * 3 + index * EmberLife / Math.Max(1, _embers);
        int age = shifted % EmberLife, cycle = shifted / EmberLife;
        uint h = Hash(Level + 1000, index, cycle);
        int x = Margin + 1 + (int)(h % (uint)Math.Max(1, BadgeWidth - 2));
        int y = Margin - 1 - age / 2;
        var color = Rainbow[(int)(h >> 8) % Rainbow.Length];
        Put(pixels, x, y, color.WithAlpha((byte)(255 - age * 22)));
    }

    private void Put(Span<Color32> pixels, int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        ref var p = ref pixels[y * Width + x];
        p = Color32.BlendOver(p, c);
    }

    private static Color32 Mix(Color32 a, Color32 b, double t)
    {
        byte L(byte u, byte v) => (byte)Math.Round(u + (v - u) * t);
        return new Color32(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B), a.A);
    }

    private static int Mod(int a, int m) => ((a % m) + m) % m;

    private static Color32 Hex(uint rgb) => new(0xFF000000u | rgb);

    private static uint Hash(int a, int b, int c)
    {
        uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return h;
    }
}
