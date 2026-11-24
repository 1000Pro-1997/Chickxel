using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.Core.Community;

/// <summary>
/// 프로필 카드 바탕(<c>card.*</c> 보상): 프로필 머리(큰 프로필 그림·이름·칭호·레벨) 뒤에 깔리는 움직이는 픽셀 그림.
/// 흰 글자가 잘 읽히게 모두 어두운 바탕이고, 가로로 이어 붙여도 이음매가 없다(모든 무늬가 폭 <see cref="Width"/>마다 되풀이 — 홈페이지는 넓은 머리에 이어 붙임).
/// 은빛(솔질한 금속 + 지나가는 빛), 금빛(호박색 + 빛 + 반짝이), 크리스털(결정 면 + 무지개 빛), 밤하늘(별·별똥별·달·언덕), 불사조(아래에서 타오르는 불길·불티).
/// </summary>
public sealed class ProfileCardArt : IPixelArt
{
    private readonly int _seed;

    public ProfileCardArt(string id, int width, int height)
    {
        if (!IsKnown(id)) throw new ArgumentException("unknown card: " + id, nameof(id));
        Id = id;
        Width = Math.Max(16, width);
        Height = Math.Max(16, height);
        _seed = Seed(id);
    }

    public static bool IsKnown(string? id) => LevelRewards.Find(id) is { Slot: RewardSlot.Card };

    public string Id { get; }
    public int Width { get; }
    public int Height { get; }
    public ArtBox Box => new(0, 0, Width, Height);
    public int Frames => Loop;

    public void Render(int frame, Span<Color32> pixels)
    {
        frame = Mod(frame, Loop);
        var p = new PixelPainter(pixels, Width, Height);
        switch (Id)
        {
            case "card.silver": Silver(p, frame); break;
            case "card.gold": Gold(p, frame); break;
            case "card.crystal": Crystal(p, frame); break;
            case "card.night-sky": NightSky(p, frame); break;
            case "card.phoenix": Phoenix(p, frame); break;
        }
    }

    /// <summary>위 → 아래 색을 <paramref name="steps"/>단계로 끊은 띠.</summary>
    private Color32 Gradient(int y, Color32 top, Color32 bottom, int steps)
        => Mix(top, bottom, Math.Round(y / (double)Math.Max(1, Height - 1) * (steps - 1)) / (steps - 1));

    /// <summary>왼쪽 위 → 오른쪽 아래로 기운 빛 띠가 주기 <paramref name="period"/>로 지나가는지(폭 마다 되풀이 — 이음매 없음).</summary>
    private double Sheen(int x, int y, int frame, int period, int width)
    {
        int pos = (int)Math.Floor(Mod(frame, period) / (double)period * Width);
        int d = Mod(x - y / 2 - pos, Width);
        return d < width ? (d == 0 || d == width - 1 ? 0.5 : 1) : 0;
    }

    private void Edges(PixelPainter p, Color32 top, Color32 bottom)
    {
        for (int x = 0; x < Width; x++)
        {
            p.Set(x, 0, top);
            p.Set(x, Height - 1, bottom);
        }
    }

    /// <summary>둘레에서 반짝이가 피었다 진다(자리는 폭 안에서 — 이음매 없음).</summary>
    private void Twinkles(PixelPainter p, int frame, int count, Color32 color, int life = 24)
    {
        for (int i = 0; i < count; i++)
        {
            var (age, cycle) = Life(frame, i, count, life);
            int x = HashInt(_seed, i, cycle, Width), y = 2 + HashInt(_seed + 1, i, cycle, Height - 4);
            double t = age / (double)life;
            int size = t < 0.15 || t > 0.85 ? 0 : t < 0.35 || t > 0.65 ? 1 : 2;
            LevelBadgeArt.Sparkle(p, x, y, size, SparkleShape.Star, color);
        }
    }

    private void Silver(PixelPainter p, int frame)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var c = Gradient(y, Hex(0x5B6573), Hex(0x1F2530), 6);
                // 솔질한 결: 가로로 길게 밝고 어두운 줄
                double grain = HashUnit(Mod(x / 6, Width), y, _seed) - 0.5;
                c = grain > 0.3 ? Lighten(c, 0.06) : grain < -0.35 ? Darken(c, 0.08) : c;
                double sheen = Sheen(x, y, frame, 60, 7);
                p.Set(x, y, sheen > 0 ? Mix(c, Hex(0xE5E7EB), 0.22 * sheen) : c);
            }
        Edges(p, Hex(0x9CA3AF), Hex(0x0F141B));
    }

    private void Gold(PixelPainter p, int frame)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var c = Gradient(y, Hex(0x6B4708), Hex(0x2A1803), 6);
                // 은은한 사선 무늬
                if (Mod(x + y, 10) == 0) c = Lighten(c, 0.07);
                double sheen = Sheen(x, y, frame, 40, 6);
                p.Set(x, y, sheen > 0 ? Mix(c, Hex(0xFFD54A), 0.3 * sheen) : c);
            }
        Edges(p, Hex(0xE0A82A), Hex(0x1A0F02));
        Twinkles(p, frame, Math.Max(4, Width / 24), Hex(0xFFE58A));
    }

    private void Crystal(PixelPainter p, int frame)
    {
        Color32[] faces = [Hex(0x1E1B4B), Hex(0x2E2A78), Hex(0x0B4F63), Hex(0x14657A)];
        const int Cell = 8;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int cx = Mod(x, Width) / Cell, cy = y / Cell;
                int lx = Mod(x, Cell), ly = y % Cell;
                // 칸마다 대각선으로 나뉜 두 삼각 면
                bool upper = lx + ly < Cell;
                var c = faces[HashInt(cx, cy * 2 + (upper ? 0 : 1), _seed, faces.Length)];
                if (lx + ly == Cell - 1 || lx + ly == Cell) c = Lighten(c, 0.18);
                // 프리즘 빛: 무지개 띠가 천천히 지나감
                double sheen = Sheen(x, y, frame, 60, 10);
                if (sheen > 0) c = Mix(c, RainbowAt(Mod(x - y / 2, Width) / 10.0 + frame / 60.0), 0.28 * sheen);
                p.Set(x, y, c);
            }
        Edges(p, Hex(0x7FE9FA), Hex(0x0B0A24));
        Twinkles(p, frame, Math.Max(3, Width / 32), Hex(0xBFF6FF));
    }

    private void NightSky(PixelPainter p, int frame)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                p.Set(x, y, Gradient(y, Hex(0x070B1F), Hex(0x2A1B4D), 7));
        // 별: 자리는 그대로, 별마다 다른 때에 깜빡
        int stars = Width * Height / 40;
        for (int i = 0; i < stars; i++)
        {
            int x = HashInt(_seed, i, 1, Width), y = HashInt(_seed, i, 2, Height - 8);
            double twinkle = Pulse(frame + HashInt(_seed, i, 3, 40), 40);
            p.Blend(x, y, Fade(i % 7 == 0 ? Hex(0xFDE68A) : Color32.White, 0.25 + 0.75 * twinkle * (i % 3 == 0 ? 1 : 0.6)));
        }
        // 달(초승달)
        int mx = Width * 4 / 5, my = 7;
        for (int y = -4; y <= 4; y++)
            for (int x = -4; x <= 4; x++)
                if (x * x + y * y <= 16 && (x - 2) * (x - 2) + (y + 1) * (y + 1) > 10) p.Set(mx + x, my + y, x < 0 ? Hex(0xFFF7CC) : Hex(0xF5E7A1));
        // 별똥별: 한 되풀이에 두 번
        int age = Mod(frame, 60);
        if (age < 12)
        {
            double sx = HashInt(_seed, frame / 60, 9, Width) + age * 3.0, sy = 2 + age * 1.2;
            for (int k = 0; k < 7; k++)
                p.Blend(Mod((int)Math.Floor(sx - k * 1.5), Width), (int)Math.Floor(sy - k * 0.6), k == 0 ? Color32.White : Fade(Hex(0xFDE68A), 1 - k / 7.0));
        }
        // 언덕 그림자(폭마다 되풀이되는 물결)
        for (int x = 0; x < Width; x++)
        {
            double h = 4 + 2 * Math.Sin(2 * Math.PI * x * 2 / Width) + 1.2 * Math.Sin(2 * Math.PI * x * 5 / Width + 1);
            for (int y = Height - (int)Math.Round(h); y < Height; y++) p.Set(x, y, y == Height - (int)Math.Round(h) ? Hex(0x1B1838) : Hex(0x0C0A1E));
        }
    }

    private void Phoenix(PixelPainter p, int frame)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                p.Set(x, y, Gradient(y, Hex(0x120404), Hex(0x4A0E04), 6));
        // 아래에서 타오르는 불길: 기둥마다 높이가 출렁이고 끝이 날름거림
        Color32[] fire = [Hex(0xFFF1A8), Hex(0xFFD23F), Hex(0xFF8A1F), Hex(0xE0401A), Hex(0x8A1A08)];
        for (int x = 0; x < Width; x++)
        {
            double h = Height * 0.32 + Height * 0.12 * Math.Sin(2 * Math.PI * (x * 4.0 / Width + frame / 40.0))
                       + HashUnit(x / 2, frame / 3, _seed) * 3;
            int top = Height - (int)Math.Round(h);
            for (int y = Math.Max(0, top); y < Height; y++)
            {
                double t = (y - top) / Math.Max(1.0, h);
                var c = fire[Math.Clamp((int)Math.Floor((1 - t) * fire.Length), 0, fire.Length - 1)];
                p.Set(x, y, t < 0.15 ? Mix(p.Get(x, y), c, 0.6) : c);
            }
        }
        // 불티: 불길 위로 오르며 꺼짐
        int count = Math.Max(6, Width / 12);
        for (int i = 0; i < count; i++)
        {
            var (age, cycle) = Life(frame, i, count, 30);
            int x = Mod(HashInt(_seed, i, cycle, Width) + (int)Math.Round(Math.Sin(age / 4.0) * 1.5), Width);
            int y = Height - (int)(Height * 0.3) - age;
            p.Blend(x, y, Fade(age < 10 ? Hex(0xFFE066) : Hex(0xFF8A1F), 1 - age / 30.0));
        }
        Edges(p, Hex(0xFF8A1F), Hex(0x1A0404));
    }
}
