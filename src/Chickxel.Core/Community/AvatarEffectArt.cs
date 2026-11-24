using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.Core.Community;

/// <summary>
/// 큰 프로필 그림 둘레에서 움직이는 효과(<c>effect.*</c> 보상): 반짝 먼지·깃털·눈송이·음표·하트·비눗방울·별똥별·금가루·번개·무지개 오라.
/// 그림 크기는 큰 테두리(<see cref="AvatarFrameArt"/>)와 같아 함께 겹쳐 그린다(효과가 아래, 테두리가 위). 얼굴을 가리지 않게 프로필 그림 안쪽에는 거의 그리지 않는다.
/// 모든 움직임은 <see cref="RewardArt.Loop"/>마다 처음과 이어진다.
/// </summary>
public sealed class AvatarEffectArt : IPixelArt
{
    public const int Margin = AvatarFrameArt.LargeMargin;

    private readonly double _center, _radius;
    private readonly int _seed;

    public AvatarEffectArt(string id, int diameter)
    {
        if (!IsKnown(id)) throw new ArgumentException("unknown effect: " + id, nameof(id));
        Id = id;
        Diameter = Math.Max(4, diameter);
        _center = Width / 2.0;
        _radius = Diameter / 2.0;
        _seed = Seed(id);
    }

    public static bool IsKnown(string? id) => LevelRewards.Find(id) is { Slot: RewardSlot.Effect };

    public string Id { get; }
    public int Diameter { get; }
    public int Width => Diameter + 2 * Margin;
    public int Height => Width;
    public ArtBox Box => new(Margin, Margin, Diameter, Diameter);
    public int Frames => Loop;

    public void Render(int frame, Span<Color32> pixels)
    {
        frame = Mod(frame, Loop);
        pixels[..(Width * Height)].Clear();
        var p = new PixelPainter(pixels, Width, Height);
        switch (Id)
        {
            case "effect.twinkle": Twinkle(p, frame); break;
            case "effect.feathers": Falling(p, frame, 4, 40, Feather); break;
            case "effect.snow": Falling(p, frame, 8, 30, Snowflake); break;
            case "effect.notes": Rising(p, frame, 4, 40, Note); break;
            case "effect.hearts": Rising(p, frame, 5, 30, Heart); break;
            case "effect.bubbles": Rising(p, frame, 5, 40, Bubble); break;
            case "effect.shooting-stars": ShootingStars(p, frame); break;
            case "effect.gold-dust": GoldDust(p, frame); break;
            case "effect.sparks": Sparks(p, frame); break;
            case "effect.aura": Aura(p, frame); break;
        }
    }

    /// <summary>점(<paramref name="x"/>, <paramref name="y"/>)이 프로필 그림 안쪽(가장자리 1칸 안)이면 true — 얼굴을 가리지 않게 건너뛴다.</summary>
    private bool OverFace(double x, double y)
    {
        double dx = x + 0.5 - _center, dy = y + 0.5 - _center;
        return dx * dx + dy * dy < (_radius - 1.5) * (_radius - 1.5);
    }

    private delegate void Particle(PixelPainter p, int x, int y, int index, double life);

    /// <summary>위에서 아래로 흔들리며 떨어진다(<paramref name="count"/>개, 하나가 <paramref name="life"/>프레임 — 되풀이 길이의 약수).</summary>
    private void Falling(PixelPainter p, int frame, int count, int life, Particle draw)
    {
        for (int i = 0; i < count; i++)
        {
            var (age, cycle) = Life(frame, i, count, life);
            double t = age / (double)life;
            double x0 = HashUnit(_seed, i, cycle) * (Width - 4) + 2;
            double x = x0 + Math.Sin(2 * Math.PI * (t * 2 + i * 0.3)) * 1.6;
            double y = -2 + t * (Height + 2);
            if (OverFace(x, y)) continue;
            draw(p, (int)Math.Floor(x), (int)Math.Floor(y), i, t);
        }
    }

    /// <summary>아래에서 위로 흔들리며 오른다.</summary>
    private void Rising(PixelPainter p, int frame, int count, int life, Particle draw)
    {
        for (int i = 0; i < count; i++)
        {
            var (age, cycle) = Life(frame, i, count, life);
            double t = age / (double)life;
            // 양옆에서 올라 얼굴을 비켜 간다
            bool left = (HashInt(_seed, i, cycle, 2) == 0);
            double edge = HashUnit(_seed + 1, i, cycle) * (Margin - 3) + 1;
            double x = (left ? edge : Width - 1 - edge) + Math.Sin(2 * Math.PI * (t * 1.5 + i * 0.25)) * 1.2;
            double y = Height - 2 - t * (Height - 2);
            if (OverFace(x, y)) continue;
            draw(p, (int)Math.Floor(x), (int)Math.Floor(y), i, t);
        }
    }

    /// <summary>나타났다 사라지는 밝기(처음·끝 20%에서 흐려짐).</summary>
    private static double FadeInOut(double t) => Math.Clamp(Math.Min(t, 1 - t) / 0.2, 0, 1);

    private static void Feather(PixelPainter p, int x, int y, int index, double t)
    {
        double a = FadeInOut(t);
        bool tilt = ((int)(t * 8) + index) % 2 == 0;
        p.Blend(x, y, Fade(Color32.White, a));
        p.Blend(x + 1, y + (tilt ? 1 : 0), Fade(Hex(0xE5E7EB), a));
        p.Blend(x + 2, y + (tilt ? 1 : 0), Fade(Hex(0xC9CED6), a));
    }

    private static void Snowflake(PixelPainter p, int x, int y, int index, double t)
    {
        double a = FadeInOut(t);
        if (index % 3 == 0)
        {
            p.Blend(x, y, Fade(Color32.White, a));
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }) p.Blend(x + dx, y + dy, Fade(Hex(0xD6F1FF), a * 0.7));
        }
        else p.Blend(x, y, Fade(index % 2 == 0 ? Color32.White : Hex(0xD6F1FF), a));
    }

    private static readonly string[] NoteRows = ["..x", "..x", "..x", "xxx", "xx."];

    private static void Note(PixelPainter p, int x, int y, int index, double t)
    {
        Color32[] colors = [Hex(0xF472B6), Hex(0x60A5FA), Hex(0xFBBF24), Hex(0x34D399)];
        var c = Fade(colors[index % colors.Length], FadeInOut(t));
        p.Sprite(x - 1, y - 2, NoteRows, _ => c);
    }

    private static void Heart(PixelPainter p, int x, int y, int index, double t)
    {
        var c = Fade(index % 2 == 0 ? Hex(0xFF4F8B) : Hex(0xFF8FB8), FadeInOut(t));
        p.Sprite(x - 1, y - 1, ["x.x", "xxx", ".x."], _ => c);
        p.Blend(x - 1, y - 1, Fade(Color32.White, FadeInOut(t) * 0.6));
    }

    private static void Bubble(PixelPainter p, int x, int y, int index, double t)
    {
        double a = FadeInOut(t);
        var rim = Fade(Hex(0xA5F3FC), a);
        if (t > 0.9)
        {
            // 터짐: 네 점이 퍼짐
            foreach (var (dx, dy) in new[] { (-2, 0), (2, 0), (0, -2), (0, 2) }) p.Blend(x + dx, y + dy, rim);
            return;
        }
        if (index % 2 == 0) p.Sprite(x - 1, y - 1, [".x.", "x.x", ".x."], _ => rim);
        else p.Sprite(x - 2, y - 2, [".xxx.", "x...x", "x...x", "x...x", ".xxx."], _ => rim);
        p.Blend(x - (index % 2 == 0 ? 0 : 1), y - (index % 2 == 0 ? 0 : 1), Fade(Color32.White, a));
    }

    /// <summary>프로필 그림 둘레 고리 안 아무 데서 작은 점이 반짝였다 꺼진다.</summary>
    private void Twinkle(PixelPainter p, int frame)
    {
        for (int i = 0; i < 7; i++)
        {
            var (age, cycle) = Life(frame, i, 7, 20);
            double a = 2 * Math.PI * HashUnit(_seed, i, cycle);
            double r = _radius + 1.5 + HashUnit(_seed + 2, i, cycle) * (Margin - 4);
            int x = (int)Math.Floor(_center + Math.Cos(a) * r), y = (int)Math.Floor(_center + Math.Sin(a) * r);
            double glow = Math.Sin(Math.PI * age / 20);
            var c = Fade(Hex(0xFFF7CC), glow);
            LevelBadgeArt.Sparkle(p, x, y, glow > 0.8 ? 1 : 0, SparkleShape.Star, c);
        }
    }

    /// <summary>별똥별: 가끔 대각선으로 꼬리를 끌며 지나간다(셋이 번갈아).</summary>
    private void ShootingStars(PixelPainter p, int frame)
    {
        for (int i = 0; i < 4; i++)
        {
            var (age, cycle) = Life(frame, i, 4, 30);
            if (age >= 20) continue;
            double start = HashUnit(_seed, i, cycle) * Width * 0.7;
            double x = start + age * 1.6, y = -2 + age * 1.2 + (i % 2) * 3;
            for (int k = 0; k < 6; k++)
            {
                double tx = x - k * 1.1, ty = y - k * 0.8;
                if (OverFace(tx, ty)) continue;
                var c = k == 0 ? Color32.White : Fade(Hex(0xFDE68A), 1 - k / 6.0);
                p.Blend((int)Math.Floor(tx), (int)Math.Floor(ty), c);
            }
        }
        // 멀리 작은 별 셋이 깜빡임
        for (int i = 0; i < 3; i++)
        {
            int x = HashInt(_seed, i, 99, Width), y = HashInt(_seed + 1, i, 99, Height);
            if (OverFace(x, y)) continue;
            p.Blend(x, y, Fade(Color32.White, 0.3 + 0.6 * Pulse(frame + i * 13, 40)));
        }
    }

    /// <summary>금가루: 프로필 그림 둘레를 타원으로 돌며 꼬리를 남긴다.</summary>
    private void GoldDust(PixelPainter p, int frame)
    {
        for (int i = 0; i < 10; i++)
        {
            double phase = i / 10.0 + HashUnit(_seed, i, 0) * 0.05;
            double r = _radius + 2.5 + (i % 3) * 2.2;
            for (int k = 0; k < 3; k++)
            {
                double a = 2 * Math.PI * (phase + (frame - k * 1.5) / (double)Loop * (i % 2 == 0 ? 1 : -1));
                double x = _center + Math.Cos(a) * r, y = _center + Math.Sin(a) * r * 0.9;
                var c = k == 0 ? (Pulse(frame + i * 7, 24) > 0.7 ? Color32.White : Hex(0xFFD54A)) : Fade(Hex(0xF5B014), 0.5 - k * 0.15);
                p.Blend((int)Math.Floor(x), (int)Math.Floor(y), c);
            }
        }
    }

    /// <summary>번개: 프로필 그림 둘레 아무 데서 지그재그 불꽃이 두세 프레임 번쩍인다.</summary>
    private void Sparks(PixelPainter p, int frame)
    {
        for (int i = 0; i < 5; i++)
        {
            var (age, cycle) = Life(frame, i, 5, 12);
            if (age >= 4) continue;
            double a = 2 * Math.PI * HashUnit(_seed, i, cycle);
            double x = _center + Math.Cos(a) * (_radius + 1), y = _center + Math.Sin(a) * (_radius + 1);
            double ox = Math.Cos(a), oy = Math.Sin(a);
            for (int k = 0; k < 7; k++)
            {
                // 바깥으로 뻗으며 좌우로 꺾인다
                double jitter = (HashInt(_seed + k, i, cycle, 3) - 1) * 0.9;
                x += ox + -oy * jitter;
                y += oy + ox * jitter;
                var c = age == 0 ? Color32.White : Hex(0x93C5FD);
                p.Blend((int)Math.Floor(x), (int)Math.Floor(y), k < 5 ? c : Fade(Hex(0x60A5FA), 0.6));
            }
        }
        // 둘레에 푸른 빛이 은은하게
        Halo(p, frame, Hex(0x60A5FA), 0.12, 0.12);
    }

    /// <summary>무지개 오라: 둘레 빛이 바깥으로 퍼지는 고리가 계속 나오고 색이 돈다.</summary>
    private void Aura(PixelPainter p, int frame)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                double dx = x + 0.5 - _center, dy = y + 0.5 - _center, r = Math.Sqrt(dx * dx + dy * dy);
                double d = r - _radius;
                if (d < 0 || d > Margin - 1) continue;
                double a = Math.Atan2(dy, dx);
                // 두 고리가 20프레임마다 바깥으로 한 번씩 퍼짐
                double wave = Frac(d / 6.0 - frame / 20.0);
                double strength = Math.Max(0, 1 - Math.Abs(wave - 0.5) * 4) * (1 - d / Margin);
                if (strength <= 0.05) continue;
                var c = RainbowAt((a + Math.PI) / (2 * Math.PI) + frame / 60.0);
                p.Blend(x, y, Fade(c, 0.3 + 0.6 * strength));
            }
        for (int i = 0; i < 4; i++)
        {
            var (age, cycle) = Life(frame, i, 4, 12);
            if (age >= 10) continue;
            double a = 2 * Math.PI * HashUnit(_seed, i, cycle);
            double r = _radius + 3 + HashUnit(_seed + 3, i, cycle) * (Margin - 6);
            int size = age switch { <= 1 or 9 => 0, <= 3 or 7 or 8 => 1, _ => 2 };
            LevelBadgeArt.Sparkle(p, (int)Math.Floor(_center + Math.Cos(a) * r), (int)Math.Floor(_center + Math.Sin(a) * r), size, SparkleShape.Star,
                Mix(Rainbow[(i + cycle) % Rainbow.Length], Color32.White, 0.4));
        }
    }

    /// <summary>프로필 그림 바로 바깥 은은한 빛(숨 쉬듯).</summary>
    private void Halo(PixelPainter p, int frame, Color32 color, double base_, double amp)
    {
        double pulse = Pulse(frame, 40);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                double dx = x + 0.5 - _center, dy = y + 0.5 - _center, d = Math.Sqrt(dx * dx + dy * dy) - _radius;
                if (d < 0.5 || d > 3.5) continue;
                p.Blend(x, y, Fade(color, (base_ + amp * pulse) * (1 - (d - 0.5) / 3)));
            }
    }
}
