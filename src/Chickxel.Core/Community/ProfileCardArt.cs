using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.Core.Community;

/// <summary>
/// 프로필 카드 바탕(<c>card.*</c> 보상): 프로필 머리(큰 프로필 그림·이름·칭호·레벨) 뒤에 깔리는 움직이는 픽셀 그림.
/// 흰 글자가 잘 읽히게 모두 어두운 바탕이고, 가로로 이어 붙여도 이음매가 없다(모든 무늬가 폭 <see cref="Width"/>마다 되풀이 — 홈페이지는 넓은 머리에 이어 붙임).
/// 은빛(솔질한 금속 + 지나가는 빛), 금빛(호박색 + 빛 + 반짝이), 크리스털(결정 면 + 무지개 빛), 밤하늘(별·별똥별·달·언덕), 불사조(아래에서 타오르는 불길·불티),
/// 벚꽃(노을 · 꽃 핀 가지 · 꽃잎), 깊은 바다(빛줄기 · 거품 · 해초 · 물고기), 오로라(빛 커튼 · 별 · 눈 덮인 언덕 · 전나무), 우주(성운 · 고리 행성 · 달), 전설(무지개 금빛 · 탐조등 · 반짝이 · 금가루).
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
            case "card.sakura": Sakura(p, frame); break;
            case "card.deep-sea": DeepSea(p, frame); break;
            case "card.aurora": Aurora(p, frame); break;
            case "card.cosmos": Cosmos(p, frame); break;
            case "card.legend": Legend(p, frame); break;
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

    /// <summary>가로로 감싸 찍는다(폭을 넘으면 반대쪽으로 — 이음매 없음).</summary>
    private void Put(PixelPainter p, int x, int y, Color32 c) => p.Blend(Mod(x, Width), y, c);

    /// <summary>폭 <see cref="Width"/>마다 <paramref name="cycles"/>번 되풀이하는 물결(-1~1).</summary>
    private double Wave(int x, int cycles, double phase = 0) => Math.Sin(2 * Math.PI * cycles * x / Width + phase);

    /// <summary>벚꽃 가지가 지나가는 높이(위쪽 가장자리를 따라 물결침).</summary>
    private double BranchY(int x) => 2.5 + 1.8 * Wave(x, 1) + 0.9 * Wave(x, 3, 1);

    private void Sakura(PixelPainter p, int frame)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                p.Set(x, y, Gradient(y, Hex(0x1F0E2C), Hex(0x6E2B4F), 7));
        // 옅은 달(한 칸에 하나)
        double mx = Width * 0.8, my = Height * 0.42, mr = Math.Max(3, Height * 0.16);
        for (int y = (int)(my - mr - 3); y <= (int)(my + mr + 3); y++)
            for (int x = (int)(mx - mr - 3); x <= (int)(mx + mr + 3); x++)
            {
                double d = Math.Sqrt((x + 0.5 - mx) * (x + 0.5 - mx) + (y + 0.5 - my) * (y + 0.5 - my));
                if (d <= mr) Put(p, x, y, Fade(Hex(0xFFE4EC), 0.26));
                else if (d <= mr + 2.5) Put(p, x, y, Fade(Hex(0xFFB3C8), 0.08));
            }
        // 먼 언덕
        for (int x = 0; x < Width; x++)
        {
            int top = Height - (int)Math.Round(3.5 + 1.5 * Wave(x, 2, 0.5) + 0.8 * Wave(x, 5));
            for (int y = top; y < Height; y++) p.Set(x, y, y == top ? Hex(0x4A1D45) : Hex(0x2C1031));
        }
        // 가지: 굵은 데는 네 칸
        var bark = Hex(0x150810);
        for (int x = 0; x < Width; x++)
        {
            int by = (int)Math.Round(BranchY(x));
            for (int y = by - 1; y <= by + 1; y++) p.Set(x, y, bark);
            if (Wave(x, 2) > 0.3) p.Set(x, by - 2, bark);
            p.Set(x, by + 2, Hex(0x2A1222));
        }
        // 잔가지: 가지에서 비스듬히 아래로, 끝에 꽃
        int twigs = Math.Max(3, Width / 14);
        for (int k = 0; k < twigs; k++)
        {
            int tx = HashInt(_seed, k, 5, Width), dir = k % 2 == 0 ? 1 : -1, len = 3 + HashInt(_seed, k, 6, 4);
            int ty = (int)Math.Round(BranchY(tx)) + 2;
            for (int s = 1; s <= len; s++) p.Set(Mod(tx + s * dir, Width), ty + (s + 1) / 2, bark);
            Blossom(p, tx + len * dir, ty + (len + 1) / 2 + 1, k + 100, frame);
        }
        int blossoms = Math.Max(5, Width / 5);
        for (int k = 0; k < blossoms; k++)
        {
            int bx = HashInt(_seed, k, 7, Width);
            Blossom(p, bx, (int)Math.Round(BranchY(bx)) + HashInt(_seed, k, 8, 5) - 1, k, frame);
        }
        // 꽃잎: 바람에 오른쪽으로 날리며 빙글 돈다
        int count = Math.Max(5, Width / 10);
        for (int i = 0; i < count; i++)
        {
            var (age, cycle) = Life(frame, i, count, 60);
            double t = age / 60.0;
            int x = (int)Math.Floor(HashInt(_seed + 3, i, cycle, Width) + age * 0.45 + Math.Sin(2 * Math.PI * (t * 2 + i * 0.3)) * 2);
            int y = (int)Math.Floor(4 + t * (Height - 5));
            double alpha = Math.Clamp(Math.Min(t / 0.1, (1 - t) / 0.15), 0, 1);
            var c = Fade(i % 3 == 0 ? Hex(0xFFD6E2) : Hex(0xFFA9C2), alpha);
            int turn = Mod(age / 5 + i, 4);
            Put(p, x, y, c);
            var shade = Fade(Hex(0xE7789E), alpha);
            if (turn == 0) Put(p, x + 1, y, shade);
            else if (turn == 1) Put(p, x + 1, y + 1, shade);
            else if (turn == 2) Put(p, x, y + 1, shade);
            else Put(p, x - 1, y + 1, shade);
        }
        Edges(p, Hex(0xF9A8C9), Hex(0x12060F));
    }

    /// <summary>벚꽃 송이(십자 꽃잎 + 진분홍 가운데, 가끔 은은히 밝아짐). 둘에 하나는 옆에 진한 꽃이 겹쳐 핀 덩이, 넷에 하나는 꽃봉오리.</summary>
    private void Blossom(PixelPainter p, int x, int y, int k, int frame)
    {
        var petal = k % 3 == 0 ? Hex(0xFFD6E2) : Hex(0xFFAFC7);
        if (Pulse(frame + k * 17, 60) > 0.85) petal = Lighten(petal, 0.35);
        if (k % 4 == 3)
        {
            Put(p, x, y, petal);
            Put(p, x, y + 1, Hex(0xE0487A));
            return;
        }
        if (k % 2 == 0) Flower(p, x + 2, y + 1, Hex(0xF593B2));
        if (k % 3 == 0) Flower(p, x - 2, y + 1, Hex(0xF0A0BC));
        Flower(p, x, y, petal);
    }

    private void Flower(PixelPainter p, int x, int y, Color32 petal)
    {
        Put(p, x - 1, y, petal);
        Put(p, x + 1, y, petal);
        Put(p, x, y - 1, petal);
        Put(p, x, y + 1, petal);
        Put(p, x, y, Hex(0xE0487A));
    }

    private static readonly string[] FishRows = ["x.xx.", ".xxxx", "x.xx."];

    private void DeepSea(PixelPainter p, int frame)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                p.Set(x, y, Gradient(y, Hex(0x0D4870), Hex(0x020A18), 8));
        // 빛줄기: 위에서 비스듬히 내려오며 흔들리고 일렁인다
        int rays = Math.Max(2, Width / 40);
        for (int k = 0; k < rays; k++)
        {
            double cx0 = (k + HashUnit(_seed, k, 1) * 0.6) * Width / rays;
            double sway = 2 * Math.Sin(2 * Math.PI * (frame / (double)Loop + k / (double)rays));
            int half = 2 + HashInt(_seed, k, 2, 3);
            double shine = 0.1 + 0.07 * Pulse(frame + k * 23, 40);
            int reach = (int)(Height * 0.8);
            for (int y = 0; y < reach; y++)
            {
                int center = (int)Math.Round(cx0 + sway + y * 0.45);
                double strength = (1 - y / (double)reach) * shine;
                for (int dx = -half; dx <= half; dx++)
                    Put(p, center + dx, y, Fade(Hex(0x9FE8FF), strength * (1 - Math.Abs(dx) / (half + 1.0))));
            }
        }
        // 물고기 무리: 한 되풀이에 폭을 한 번 가로지른다
        int fx = HashInt(_seed, 0, 30, Width) + (int)Math.Floor(frame * Width / (double)Loop);
        int fy = (int)(Height * 0.4);
        for (int f = 0; f < 3; f++)
        {
            int ox = fx - f * 6, oy = fy + (f == 1 ? -3 : f == 2 ? 2 : 0) + (Mod(frame / 6 + f, 4) == 0 ? 1 : 0);
            for (int r = 0; r < FishRows.Length; r++)
                for (int i = 0; i < FishRows[r].Length; i++)
                    if (FishRows[r][i] != '.') Put(p, ox + i, oy + r, Hex(0x062033));
        }
        // 바닥
        for (int x = 0; x < Width; x++)
        {
            int top = Height - (int)Math.Round(2.5 + Wave(x, 3) + 0.7 * Wave(x, 7, 1));
            for (int y = top; y < Height; y++) p.Set(x, y, y == top ? Hex(0x1B3B4A) : Hex(0x081A26));
        }
        // 해초: 아래에서 위로 갈수록 크게 흔들린다
        int weeds = Math.Max(3, Width / 12);
        for (int s = 0; s < weeds; s++)
        {
            int x0 = HashInt(_seed, s, 11, Width);
            int height = (int)(Height * 0.25 + HashUnit(_seed, s, 12) * Height * 0.35);
            var c = s % 2 == 0 ? Hex(0x0E4A35) : Hex(0x166B45);
            double phase = HashUnit(_seed, s, 13);
            for (int k = 0; k <= height; k++)
            {
                double frac = k / (double)height;
                int x = x0 + (int)Math.Round(2.2 * frac * Math.Sin(2 * Math.PI * (frame / 60.0 + phase) - k * 0.3));
                int y = Height - 2 - k;
                Put(p, x, y, c);
                if (frac < 0.5) Put(p, x + 1, y, Darken(c, 0.25));
                if (k % 4 == 2 && k < height) Put(p, x + (k % 8 == 2 ? 1 : -1), y - 1, Lighten(c, 0.12));
            }
        }
        // 거품: 흔들리며 오르다 위에서 흐려진다
        int bubbles = Math.Max(5, Width / 12);
        var rim = Hex(0xA5F3FC);
        for (int i = 0; i < bubbles; i++)
        {
            var (age, cycle) = Life(frame, i, bubbles, 60);
            int x = HashInt(_seed + 4, i, cycle, Width) + (int)Math.Round(Math.Sin(2 * Math.PI * age / 20.0 + i) * 1.2);
            double yf = Height - 3 - age * (Height - 2) / 60.0;
            int y = (int)Math.Floor(yf);
            double alpha = Math.Clamp(yf / (Height * 0.3), 0, 1) * 0.85;
            var c = Fade(rim, alpha);
            switch (i % 3)
            {
                case 0:
                    Put(p, x, y, c);
                    break;
                case 1:
                    Put(p, x - 1, y, c); Put(p, x + 1, y, c); Put(p, x, y - 1, c); Put(p, x, y + 1, c);
                    Put(p, x, y, Fade(Color32.White, alpha * 0.5));
                    break;
                default:
                    Put(p, x, y - 1, c); Put(p, x + 1, y - 1, c); Put(p, x, y + 2, c); Put(p, x + 1, y + 2, c);
                    Put(p, x - 1, y, c); Put(p, x - 1, y + 1, c); Put(p, x + 2, y, c); Put(p, x + 2, y + 1, c);
                    Put(p, x, y, Fade(Color32.White, alpha));
                    break;
            }
        }
        Edges(p, Hex(0x3FB6E0), Hex(0x01060E));
    }

    private void Aurora(PixelPainter p, int frame)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                p.Set(x, y, Gradient(y, Hex(0x030716), Hex(0x0E2A3C), 7));
        int stars = Width * Height / 45;
        for (int i = 0; i < stars; i++)
        {
            int x = HashInt(_seed, i, 1, Width), y = HashInt(_seed, i, 2, Math.Max(1, Height - 8));
            p.Blend(x, y, Fade(Color32.White, 0.2 + 0.6 * Pulse(frame + HashInt(_seed, i, 3, 40), 40) * (i % 3 == 0 ? 1 : 0.5)));
        }
        // 빛 커튼 둘: 밝은 아래 끝에서 빛살이 위로 뻗고(초록 → 보라), 물결치며 빛살이 옆으로 흐른다
        int stripes = Math.Max(5, Width / 6);
        for (int c = 0; c < 2; c++)
        {
            double strength = c == 0 ? 0.7 : 0.38;
            Color32 edge = c == 0 ? Hex(0xB4FFD9) : Hex(0x9FFCEB), body = c == 0 ? Hex(0x34D399) : Hex(0x2DD4BF), top = c == 0 ? Hex(0xA78BFA) : Hex(0xC084FC);
            for (int x = 0; x < Width; x++)
            {
                double bottom = Height * (c == 0 ? 0.58 : 0.4) + Height * 0.1 * Wave(x, c + 1, 2 * Math.PI * frame / Loop + c)
                                + Height * 0.05 * Wave(x, 3, c * 2 - 2 * Math.PI * frame * 2 / Loop);
                // 빛살마다 길이가 들쭉날쭉(자리는 폭 안에서 고정)
                double len = (Height * (c == 0 ? 0.42 : 0.3) + Height * 0.1 * Wave(x, 2, 2 * Math.PI * frame / 60.0 + 1 + c)) * (0.65 + 0.35 * HashUnit(x, c, _seed));
                // 빛살: 폭마다 정수 번 되풀이하는 줄무늬를 제곱해 또렷하게
                double stria = 0.2 + 0.8 * Math.Pow(0.5 + 0.5 * Wave(x, stripes, 2 * Math.PI * frame * 3 / Loop + c * 0.5), 2);
                for (int y = (int)Math.Floor(bottom - len); y <= (int)Math.Ceiling(bottom) + 1; y++)
                {
                    double v = (bottom - y) / len;
                    double inten = v < 0 ? Math.Max(0, 1 + v * 5) * 0.7 : v < 0.1 ? 1.15 : Math.Pow(1 - v, 2);
                    double alpha = Math.Round(Math.Min(1, inten * stria * strength) * 8) / 8;
                    if (alpha <= 0.05) continue;
                    var col = v < 0.1 ? edge : v < 0.5 ? body : Mix(body, top, (v - 0.5) / 0.5 * 1.6);
                    p.Blend(x, y, Fade(col, alpha));
                }
            }
        }
        // 눈 덮인 언덕
        int GroundTop(int x) => Height - (int)Math.Round(4.5 + 1.5 * Wave(x, 2, 0.4) + Wave(x, 5, 1.1));
        for (int x = 0; x < Width; x++)
        {
            int top = GroundTop(x);
            for (int y = top; y < Height; y++)
                p.Set(x, y, y == top ? Hex(0xE6F2FF) : y == top + 1 ? Hex(0xA9BED6) : Mix(Hex(0x4D6585), Hex(0x1C2A40), (y - top - 2) / 3.0));
        }
        // 전나무: 층층이 넓어지는 그림자, 층 끝과 꼭대기에 눈
        int trees = Math.Max(2, Width / 18);
        for (int k = 0; k < trees; k++)
        {
            int tx = HashInt(_seed, k, 20, Width), h = Math.Max(4, Height / 6) + HashInt(_seed, k, 21, Height / 8 + 1);
            int baseY = GroundTop(tx) + 1;
            for (int r = 0; r < h; r++)
            {
                int y = baseY - h + r, half = Math.Max(0, (int)Math.Round(r * 0.45) - (r >= 3 && r % 3 == 0 ? 1 : 0));
                bool tier = r % 3 == 2;
                for (int dx = -half; dx <= half; dx++)
                    Put(p, tx + dx, y, r == 0 || (tier && dx == -half && half > 0) ? Hex(0xCFE0F2) : Hex(0x0A1626));
            }
            Put(p, tx, baseY, Hex(0x2A1A12));
        }
        Edges(p, Hex(0x5EEAD4), Hex(0x050B14));
    }

    private void Cosmos(PixelPainter p, int frame)
    {
        double t = frame / (double)Loop;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var c = Gradient(y, Hex(0x060418), Hex(0x140A30), 7);
                double u = x / (double)Width, v = y / (double)Height;
                // 성운: 폭마다 되풀이하는 물결을 겹쳐 천천히 흐르는 보라·파랑 구름
                double n = 0.55 * Math.Sin(2 * Math.PI * (2 * u + 0.25 * Math.Sin(2 * Math.PI * (u + v * 0.8)) + t))
                           + 0.45 * Math.Sin(2 * Math.PI * (3 * u - v * 0.9) - 2 * Math.PI * t + 1);
                double m = 0.6 * Math.Sin(2 * Math.PI * (u + v * 0.5 + 0.2 * Math.Sin(2 * Math.PI * (2 * u - v))) - 2 * Math.PI * t + 2)
                           + 0.4 * Math.Sin(2 * Math.PI * (3 * u + v * 0.4) + 1);
                if (n > 0.75) c = Mix(c, Hex(0x8A46C8), 0.42);
                else if (n > 0.45) c = Mix(c, Hex(0x7B3FB8), 0.28);
                else if (n > 0.15) c = Mix(c, Hex(0x6A35A0), 0.14);
                if (m > 0.6) c = Mix(c, Hex(0x2B6AD8), 0.3);
                else if (m > 0.3) c = Mix(c, Hex(0x2B5FD0), 0.15);
                p.Set(x, y, c);
            }
        int stars = Width * Height / 28;
        for (int i = 0; i < stars; i++)
        {
            int x = HashInt(_seed, i, 1, Width), y = HashInt(_seed, i, 2, Height);
            var c = i % 9 == 0 ? Hex(0xFFD6F5) : i % 5 == 0 ? Hex(0xBFE0FF) : Color32.White;
            p.Blend(x, y, Fade(c, 0.2 + 0.7 * Pulse(frame + HashInt(_seed, i, 3, 40), 40) * (i % 3 == 0 ? 1 : 0.5)));
        }
        Twinkles(p, frame, Math.Max(3, Width / 40), Hex(0xE9D5FF), 20);
        // 고리 행성과 그 둘레를 도는 작은 달(달이 뒤로 가면 행성에 가려짐)
        double px = Width * 0.74, py = Height * 0.5, r = Math.Max(3, Height * 0.2);
        double th = 2 * Math.PI * t;
        double mx = px + Math.Cos(th) * r * 2.7, my = py + Math.Sin(th) * r * 0.9 - Math.Cos(th) * r * 0.35;
        bool moonFront = Math.Sin(th) > 0;
        PlanetRing(p, px, py, r, frame, front: false);
        if (!moonFront) Moon(p, mx, my);
        RingedPlanetBody(p, px, py, r);
        PlanetRing(p, px, py, r, frame, front: true);
        if (moonFront) Moon(p, mx, my);
        // 별똥별: 한 되풀이에 한 번
        int age = frame - 40;
        if (age >= 0 && age < 12)
        {
            double sx = HashInt(_seed, 0, 9, Width) + age * 3.0, sy = 2 + age * 1.1;
            for (int k = 0; k < 7; k++)
                Put(p, (int)Math.Floor(sx - k * 1.5), (int)Math.Floor(sy - k * 0.55), k == 0 ? Color32.White : Fade(Hex(0xC4B5FD), 1 - k / 7.0));
        }
        Edges(p, Hex(0x8B5CF6), Hex(0x03020C));
    }

    /// <summary>행성 고리의 앞(<paramref name="front"/>) 또는 뒤 반쪽. 반짝이는 빛 한 점이 고리를 따라 돈다.</summary>
    private void PlanetRing(PixelPainter p, double cx, double cy, double r, int frame, bool front)
    {
        double rx = r * 2.1, ry = r * 0.6, tilt = -0.22;
        double glint = 2 * Math.PI * frame / 60.0;
        int reach = (int)Math.Ceiling(rx) + 1;
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = (int)Math.Floor(cx) + dx, y = (int)Math.Floor(cy) + dy;
                double ux = x + 0.5 - cx, uy = y + 0.5 - cy;
                double lx = ux * Math.Cos(tilt) + uy * Math.Sin(tilt), ly = -ux * Math.Sin(tilt) + uy * Math.Cos(tilt);
                double e = (lx / rx) * (lx / rx) + (ly / ry) * (ly / ry);
                if (e < 0.62 || e > 1.0 || (ly > 0) != front) continue;
                double ang = Math.Atan2(ly / ry, lx / rx);
                double toGlint = Math.Abs(Math.IEEERemainder(ang - glint, 2 * Math.PI));
                var c = toGlint < 0.25 ? Color32.White : e < 0.8 ? Hex(0xB89B6A) : Hex(0xF1DFAE);
                Put(p, x, y, c);
            }
    }

    /// <summary>띠 무늬 행성(왼쪽 위에서 빛, 오른쪽 아래는 그림자).</summary>
    private void RingedPlanetBody(PixelPainter p, double cx, double cy, double r)
    {
        int reach = (int)Math.Ceiling(r) + 1;
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = (int)Math.Floor(cx) + dx, y = (int)Math.Floor(cy) + dy;
                double ux = x + 0.5 - cx, uy = y + 0.5 - cy;
                if (ux * ux + uy * uy > r * r) continue;
                double lit = (-ux * 0.8 - uy * 0.6) / r;
                var c = Mod((int)Math.Floor((uy + r) / 1.6), 3) == 1 ? Hex(0xC9774A) : Hex(0xE9A46A);
                if (lit > 0.5) c = Lighten(c, 0.35);
                else if (lit < -0.45) c = Hex(0x4A2238);
                else if (lit < -0.1) c = Darken(c, 0.35);
                Put(p, x, y, c);
            }
    }

    private void Moon(PixelPainter p, double cx, double cy)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = (int)Math.Floor(cx) + dx, y = (int)Math.Floor(cy) + dy;
                double ux = x + 0.5 - cx, uy = y + 0.5 - cy;
                if (ux * ux + uy * uy > 2.6) continue;
                Put(p, x, y, ux + uy > 0.6 ? Hex(0x7D8496) : Hex(0xDDE2EC));
            }
    }

    private void Legend(PixelPainter p, int frame)
    {
        double t = frame / (double)Loop;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var c = Gradient(y, Hex(0x542C07), Hex(0x1A0A03), 8);
                double u = x / (double)Width;
                // 은은한 무지개 빛이 깔리고, 홀로그램 같은 무지개 띠가 비스듬히 훑고 지나간다
                c = Mix(c, Color32.FromHsv((float)(Frac(2 * u + y * 0.01 + t) * 360), 0.6f, 0.9f), 0.05);
                double s = Frac(2 * u + y / (Height * 4.0) - 2 * t) - 0.5;
                double band = Math.Exp(-(s / 0.09) * (s / 0.09));
                if (band > 0.05) c = Mix(c, Color32.FromHsv((float)(Frac(6 * u + y * 0.03 + 2 * t) * 360), 0.45f, 1f), 0.3 * band);
                // 금박 반짝이: 자리는 그대로, 저마다 다른 때에 반짝
                double grain = HashUnit(x, y, _seed);
                if (grain < 0.02) c = Mix(c, Hex(0xFFE9A0), 0.2 + 0.65 * Math.Pow(Pulse(frame + (int)(grain * 5000), 24), 2));
                p.Set(x, y, c);
            }
        // 탐조등: 아래에서 뻗은 금빛 빛줄기들이 함께 좌우로 훑는다
        int beams = Math.Max(2, Width / 42);
        var beam = Hex(0xFFD45A);
        for (int k = 0; k < beams; k++)
        {
            double bx = (k + 0.5) * Width / beams;
            double slope = Math.Tan(0.5 * Math.Sin(2 * Math.PI * (t * 2 + k * 0.08)));
            for (int y = 0; y < Height; y++)
            {
                double rise = Height - y, center = bx + rise * slope, half = 1 + rise * 0.12;
                for (int x = (int)Math.Floor(center - half); x <= (int)Math.Ceiling(center + half); x++)
                {
                    double dd = Math.Abs(x + 0.5 - center);
                    if (dd > half) continue;
                    double strength = Math.Round((1 - dd / half) * (0.28 - 0.14 * rise / Height) * 10) / 10;
                    if (strength > 0) Put(p, x, y, Fade(beam, strength));
                }
            }
            Put(p, (int)bx, Height - 2, Fade(Color32.White, 0.8));
            Put(p, (int)bx - 1, Height - 2, Fade(beam, 0.6));
            Put(p, (int)bx + 1, Height - 2, Fade(beam, 0.6));
        }
        // 떠오르는 금가루
        int motes = Math.Max(6, Width / 10);
        for (int i = 0; i < motes; i++)
        {
            var (age, cycle) = Life(frame, i, motes, 40);
            double life = age / 40.0;
            int x = HashInt(_seed + 5, i, cycle, Width) + (int)Math.Round(Math.Sin(2 * Math.PI * (life * 2 + i * 0.3)) * 1.2);
            int y = Height - 2 - (int)(age * (Height - 4) / 40.0);
            Put(p, x, y, Fade(life < 0.5 ? Hex(0xFFF1A8) : Hex(0xFFC53D), 1 - life));
        }
        Twinkles(p, frame, Math.Max(5, Width / 16), Hex(0xFFE58A), 20);
        // 무지개 반짝이
        int sparkles = Math.Max(3, Width / 32);
        for (int i = 0; i < sparkles; i++)
        {
            var (age, cycle) = Life(frame, i, sparkles, 24);
            int x = HashInt(_seed + 9, i, cycle, Width), y = 3 + HashInt(_seed + 10, i, cycle, Height - 6);
            double life = age / 24.0;
            int size = life < 0.15 || life > 0.85 ? 0 : life < 0.35 || life > 0.65 ? 1 : 2;
            LevelBadgeArt.Sparkle(p, x, y, size, SparkleShape.Star, Mix(Rainbow[(i + cycle) % Rainbow.Length], Color32.White, 0.35));
        }
        // 위 테두리: 금빛 위로 무지개가 흐른다
        for (int x = 0; x < Width; x++)
        {
            var top = Mix(Hex(0xFFD54A), RainbowAt(Frac(2.0 * x / Width - 2 * t)), 0.4);
            p.Set(x, 0, top);
            p.Set(x, 1, Mix(p.Get(x, 1), top, 0.35));
            p.Set(x, Height - 1, Hex(0x120A01));
            p.Set(x, Height - 2, Mix(Hex(0x8A5A10), RainbowAt(Frac(2.0 * x / Width + 2 * t)), 0.2));
        }
    }
}
