using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.Core.Community;

/// <summary>
/// 큰 프로필 그림 둘레에서 움직이는 효과(<c>effect.*</c> 보상): 반짝 먼지·깃털·눈송이·음표·하트·비눗방울·별똥별·금가루·번개·무지개 오라,
/// 벚꽃잎·반딧불·낙엽·꽃가루 종이·행성·룬·불꽃·은하 소용돌이·유성우·불사조 오라.
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
            case "effect.petals": Falling(p, frame, 8, 40, Petal, wind: 6, sway: 2.2); break;
            case "effect.fireflies": Fireflies(p, frame); break;
            case "effect.leaves": Falling(p, frame, 8, 40, Leaf, wind: -5, sway: 2.6); break;
            case "effect.confetti": Confetti(p, frame); break;
            case "effect.planets": Planets(p, frame); break;
            case "effect.runes": Runes(p, frame); break;
            case "effect.flames": Flames(p, frame); break;
            case "effect.galaxy-swirl": GalaxySwirl(p, frame); break;
            case "effect.meteors": Meteors(p, frame); break;
            case "effect.phoenix-aura": PhoenixAura(p, frame); break;
        }
    }

    /// <summary>점(<paramref name="x"/>, <paramref name="y"/>)이 프로필 그림 안쪽(가장자리 1칸 안)이면 true — 얼굴을 가리지 않게 건너뛴다.</summary>
    private bool OverFace(double x, double y)
    {
        double dx = x + 0.5 - _center, dy = y + 0.5 - _center;
        return dx * dx + dy * dy < (_radius - 1.5) * (_radius - 1.5);
    }

    private delegate void Particle(PixelPainter p, int x, int y, int index, double life);

    /// <summary>
    /// 위에서 아래로 흔들리며 떨어진다(<paramref name="count"/>개, 하나가 <paramref name="life"/>프레임 — 되풀이 길이의 약수).
    /// <paramref name="wind"/>만큼 사는 동안 옆으로 밀리고 <paramref name="sway"/>만큼 좌우로 흔들린다.
    /// </summary>
    private void Falling(PixelPainter p, int frame, int count, int life, Particle draw, double wind = 0, double sway = 1.6)
    {
        for (int i = 0; i < count; i++)
        {
            var (age, cycle) = Life(frame, i, count, life);
            double t = age / (double)life;
            double x0 = HashUnit(_seed, i, cycle) * (Width - 4) + 2 - wind / 2;
            double x = x0 + t * wind + Math.Sin(2 * Math.PI * (t * 2 + i * 0.3)) * sway;
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

    /// <summary>점 하나를 얼굴 밖에서만 찍는다.</summary>
    private void Dot(PixelPainter p, double x, double y, Color32 c)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        if (!OverFace(ix, iy)) p.Blend(ix, iy, c);
    }

    /// <summary>점(<paramref name="x"/>, <paramref name="y"/>)의 둘레 각도(라디안, 오른쪽 0 · 아래쪽 +)와 프로필 그림 가장자리 바깥으로 떨어진 거리.</summary>
    private (double Angle, double Out) Polar(double x, double y)
    {
        double dx = x + 0.5 - _center, dy = y + 0.5 - _center;
        return (Math.Atan2(dy, dx), Math.Sqrt(dx * dx + dy * dy) - _radius);
    }

    /// <summary>정사각 글자 그림을 시계 방향으로 0·90·180·270° 돌린 네 장(빙글 도는 조각용).</summary>
    private static string[][] Turns(string[] rows)
    {
        int n = rows.Length;
        var turns = new string[4][];
        turns[0] = rows;
        for (int k = 1; k < 4; k++)
        {
            var prev = turns[k - 1];
            turns[k] = new string[n];
            for (int r = 0; r < n; r++)
            {
                var chars = new char[n];
                for (int c = 0; c < n; c++) chars[c] = prev[n - 1 - c][r];
                turns[k][r] = new string(chars);
            }
        }
        return turns;
    }

    private static readonly string[][] PetalTurns = Turns(["hx.", "xxd", ".dd"]);
    private static readonly string[][] SmallPetalTurns = Turns(["hx", "xd"]);

    /// <summary>벚꽃잎: 빙글 돌며(네 방향) 떨어진다. 둘에 하나는 작은 꽃잎.</summary>
    private static void Petal(PixelPainter p, int x, int y, int index, double t)
    {
        double a = FadeInOut(t);
        var turns = index % 2 == 0 ? PetalTurns : SmallPetalTurns;
        var rows = turns[Mod((int)(t * 12) + index, 4)];
        Color32 h = Fade(Hex(0xFFF0F5), a), body = Fade(Hex(0xFFB3C8), a), shade = Fade(Hex(0xEE7FA5), a);
        p.Sprite(x - 1, y - 1, rows, ch => ch switch { 'h' => h, 'd' => shade, _ => body });
    }

    private static readonly string[][] LeafTurns = Turns([".xx", "xvx", "sx."]);
    private static readonly Color32[] LeafColors = [Hex(0xF28C28), Hex(0xD9442B), Hex(0xF2C230)];
    private static readonly Color32[] ConfettiColors =
        [Hex(0xFF5C7A), Hex(0xFFB627), Hex(0xFFE14D), Hex(0x3DDC84), Hex(0x3FA9FF), Hex(0xB06CFF), Hex(0xFF7BD5), Hex(0x2EE6E6)];

    /// <summary>낙엽: 주황·빨강·노랑 잎이 잎맥을 보이며 뒤집히듯 돈다.</summary>
    private static void Leaf(PixelPainter p, int x, int y, int index, double t)
    {
        double a = FadeInOut(t);
        var c = LeafColors[index % LeafColors.Length];
        Color32 body = Fade(c, a), vein = Fade(Darken(c, 0.3), a), stem = Fade(Hex(0x7A4A22), a);
        var rows = LeafTurns[Mod((int)(t * 9) + index, 4)];
        p.Sprite(x - 1, y - 1, rows, ch => ch switch { 'v' => vein, 's' => stem, _ => body });
    }

    /// <summary>반딧불: 둘레를 떠돌며 깜빡이는 연두빛 점(은은한 빛무리).</summary>
    private void Fireflies(PixelPainter p, int frame)
    {
        for (int i = 0; i < 7; i++)
        {
            double phase = HashUnit(_seed, i, 0);
            int dir = i % 2 == 0 ? 1 : -1;
            // 둘레를 천천히 돌며 안팎·앞뒤로 흔들린다(모든 주기가 되풀이 길이의 약수)
            double a = 2 * Math.PI * (phase + dir * frame / (double)Loop) + 0.45 * Math.Sin(2 * Math.PI * (frame * 3 / (double)Loop + phase * 5));
            double r = _radius + 3 + (Margin - 5.5) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * (frame * (1 + i % 3) / (double)Loop + phase)));
            double x = _center + Math.Cos(a) * r, y = _center + Math.Sin(a) * r;
            double glow = Math.Pow(Pulse(frame + i * 11, i % 3 == 0 ? 24 : i % 3 == 1 ? 30 : 40), 1.6);
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist == 0 || dist > 2.3) continue;
                    double alpha = glow * (dist < 1.1 ? 0.55 : dist < 1.5 ? 0.3 : 0.14);
                    Dot(p, ix + dx, iy + dy, Fade(Hex(0xA8E63D), alpha));
                }
            Dot(p, ix, iy, Fade(glow > 0.6 ? Hex(0xFBFFD0) : Hex(0xE4F57A), 0.25 + 0.75 * glow));
        }
    }

    /// <summary>꽃가루 종이: 여러 색 종이 조각이 뒤집히며(앞면·옆면·뒷면) 쏟아진다.</summary>
    private void Confetti(PixelPainter p, int frame)
    {
        const int Count = 13, Life_ = 30;
        for (int i = 0; i < Count; i++)
        {
            var (age, cycle) = Life(frame, i, Count, Life_);
            double t = age / (double)Life_;
            double x = HashUnit(_seed, i, cycle) * (Width - 2) + 1 + Math.Sin(2 * Math.PI * (t * 1.5 + HashUnit(_seed + 1, i, cycle))) * 1.8;
            double y = -1 + t * (Height + 2);
            var c = ConfettiColors[HashInt(_seed + 2, i, cycle, ConfettiColors.Length)];
            int flip = Mod(age / 2 + i, 4);
            // 앞면은 밝게(가로), 비스듬히, 뒷면은 어둡게(세로), 옆면은 점 하나
            var face = flip == 0 ? Lighten(c, 0.25) : flip == 2 ? Darken(c, 0.18) : c;
            face = Fade(face, Math.Clamp(Math.Min(t, 1 - t) / 0.06, 0, 1));
            Dot(p, x, y, face);
            if (flip == 0) Dot(p, x + 1, y, face);
            else if (flip == 1) Dot(p, x + 1, y + 1, face);
            else if (flip == 2) Dot(p, x, y + 1, face);
        }
    }

    /// <summary>행성 궤도 <paramref name="index"/>번 위의 <paramref name="turn"/>(1 = 한 바퀴) 자리와 앞뒤(-1 = 뒤 · 1 = 앞).</summary>
    private (double X, double Y, double Depth) Orbit(int index, double turn)
    {
        double a = index == 0 ? _radius + Margin * 0.72 : _radius + Margin * 0.42;
        double b = index == 0 ? _radius + 2.6 : _radius + 1.6;
        double tilt = index == 0 ? -0.38 : 0.52;
        double th = 2 * Math.PI * turn;
        double ex = Math.Cos(th) * a, ey = Math.Sin(th) * b;
        return (_center + ex * Math.Cos(tilt) - ey * Math.Sin(tilt), _center + ex * Math.Sin(tilt) + ey * Math.Cos(tilt), Math.Sin(th));
    }

    /// <summary>행성: 기운 타원 궤도 둘을 도는 작은 행성 둘(고리 달린 금빛 · 푸른). 앞으로 오면 커지고 뒤로 가면 작아져 프로필 그림 뒤로 숨는다.</summary>
    private void Planets(PixelPainter p, int frame)
    {
        // 궤도 점선
        for (int i = 0; i < 2; i++)
            for (int k = 0; k < 64; k += 2)
            {
                var (x, y, _) = Orbit(i, k / 64.0);
                Dot(p, x, y, Fade(i == 0 ? Hex(0xFCD34D) : Hex(0x7DD3FC), 0.16));
            }
        double turn0 = frame / (double)Loop + 0.1, turn1 = frame * 2 / (double)Loop + 0.55;
        var o0 = Orbit(0, turn0);
        var o1 = Orbit(1, turn1);
        // 뒤에 있는 행성부터
        bool firstIsZero = o0.Depth <= o1.Depth;
        for (int n = 0; n < 2; n++)
        {
            int i = (n == 0) == firstIsZero ? 0 : 1;
            double turn = i == 0 ? turn0 : turn1;
            var o = i == 0 ? o0 : o1;
            // 꼬리: 지나온 자리에 흐린 점
            for (int k = 1; k <= 6; k++)
            {
                var (tx, ty, _) = Orbit(i, turn - k * (i == 0 ? 0.012 : 0.02));
                Dot(p, tx, ty, Fade(i == 0 ? Hex(0xFDE68A) : Hex(0xBAE6FD), 0.5 * (1 - k / 7.0)));
            }
            double near = 0.5 + 0.5 * o.Depth;
            if (i == 0)
                Planet(p, o.X, o.Y, 1.4 + 1.3 * near, o.Depth < 0, Hex(0xFFE9A8), Hex(0xF0B54A), Hex(0xA8641E), bands: true, ring: true);
            else
                Planet(p, o.X, o.Y, 1.2 + 1.0 * near, o.Depth < 0, Hex(0xC9F1FF), Hex(0x3B9BE0), Hex(0x1E4F9A), bands: false, ring: false);
        }
    }

    /// <summary>왼쪽 위에서 빛을 받는 둥근 행성(띠·고리 선택). <paramref name="behind"/>면 프로필 그림에 가려진다.</summary>
    private void Planet(PixelPainter p, double cx, double cy, double size, bool behind, Color32 light, Color32 mid, Color32 dark, bool bands, bool ring)
    {
        int reach = (int)Math.Ceiling(size) + (ring ? 3 : 0);
        int bx = (int)Math.Floor(cx), by = (int)Math.Floor(cy);
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = bx + dx, y = by + dy;
                if (behind && OverFace(x, y)) continue;
                double ux = x + 0.5 - cx, uy = y + 0.5 - cy;
                bool inDisc = ux * ux + uy * uy <= size * size;
                // 고리: 살짝 기운 가는 선(뒤쪽 반은 행성에 가려짐)
                bool onRing = ring && Math.Abs(uy - ux * 0.35) < 0.5 && Math.Abs(ux) <= size + 2.5 && Math.Abs(ux) > 0.4;
                if (onRing && (!inDisc || uy - ux * 0.35 >= 0 && ux * uy >= 0))
                {
                    p.Blend(x, y, Fade(Hex(0xFFF3C4), Math.Abs(ux) > size + 1.5 ? 0.55 : 0.9));
                    continue;
                }
                if (!inDisc) continue;
                double lit = (-ux - uy) / (size * 1.4);
                var c = lit > 0.3 ? light : lit < -0.35 ? dark : mid;
                if (bands && size > 1.8 && Mod((int)Math.Floor(uy + size), 2) == 1) c = Darken(c, 0.14);
                p.Blend(x, y, c);
            }
    }

    private static readonly string[][] RuneGlyphs =
    [
        ["x.x", "x.x", ".x.", ".x.", ".x."],
        ["x..", "xx.", "x.x", "xx.", "x.."],
        [".x.", "x.x", ".x.", "x.x", "x.x"],
        [".x.", "xxx", "x.x", ".x.", ".x."],
        ["x..", "x.x", "xx.", "x..", "x.."],
        [".x.", ".xx", ".x.", "xx.", ".x."],
    ];

    /// <summary>룬: 마법진 점선 고리 둘이 서로 거꾸로 돌고, 그 사이를 빛나는 룬 글자 여섯이 천천히 돌며 차례로 밝아진다(청록·보라).</summary>
    private void Runes(PixelPainter p, int frame)
    {
        Color32 cyan = Hex(0x5EEAF4), violet = Hex(0xC08CFF);
        double inner = _radius + 1.5, outer = _radius + Margin - 1.5;
        for (int k = 0; k < 40; k++)
        {
            if (k % 4 == 3) continue;
            double a = 2 * Math.PI * (k / 40.0 - frame / (double)Loop);
            Dot(p, _center + Math.Cos(a) * inner - 0.5, _center + Math.Sin(a) * inner - 0.5, Fade(k % 8 < 4 ? cyan : violet, 0.35 + 0.25 * Pulse(frame + k * 3, 40)));
        }
        for (int k = 0; k < 64; k++)
        {
            if (k % 8 >= 5) continue;
            double a = 2 * Math.PI * (k / 64.0 + frame / (double)Loop);
            Dot(p, _center + Math.Cos(a) * outer - 0.5, _center + Math.Sin(a) * outer - 0.5, Fade(violet, 0.28));
        }
        double ring = _radius + Margin / 2.0;
        for (int i = 0; i < RuneGlyphs.Length; i++)
        {
            double a = 2 * Math.PI * (i / (double)RuneGlyphs.Length + frame / (double)Loop);
            int x = (int)Math.Round(_center + Math.Cos(a) * ring) - 1, y = (int)Math.Round(_center + Math.Sin(a) * ring) - 2;
            // 빛이 글자에서 글자로 둘레를 따라 옮겨 간다
            double glow = Math.Pow(Pulse(frame - i * 20, 40), 1.5);
            var color = i % 2 == 0 ? cyan : violet;
            var core = Fade(Mix(color, Color32.White, 0.55 * glow), 0.55 + 0.45 * glow);
            p.SpriteOutlined(x, y, RuneGlyphs[i], _ => core, Fade(color, 0.12 + 0.3 * glow));
        }
        // 둘레에 작은 빛 조각이 피었다 진다
        for (int i = 0; i < 5; i++)
        {
            var (age, cycle) = Life(frame, i, 5, 20);
            if (age >= 12) continue;
            double a = 2 * Math.PI * HashUnit(_seed, i, cycle);
            double r = _radius + 2 + HashUnit(_seed + 1, i, cycle) * (Margin - 4);
            Dot(p, _center + Math.Cos(a) * r, _center + Math.Sin(a) * r - age * 0.3, Fade(i % 2 == 0 ? cyan : violet, Math.Sin(Math.PI * age / 12)));
        }
    }

    /// <summary>
    /// 프로필 그림 둘레 불길의 세기(1 = 가장 뜨거움, 0 이하 = 불 없음). 위쪽이 더 높이 타오르고(<paramref name="bottomReach"/> → <paramref name="topReach"/>),
    /// 뾰족한 혀처럼 날름거리는 끝(<paramref name="tongues"/>)과 위로 흐르는 일렁임이 겹치고, 옆쪽 불길은 위로 휘어 오른다.
    /// </summary>
    private double FireHeat(int x, int y, int frame, double bottomReach, double topReach, double tongues)
    {
        var (a, d) = Polar(x, y);
        if (d < -0.5) return 0;
        double up = -Math.Sin(a);
        // 바깥으로 갈수록 아래쪽 뿌리에서 올라온 불길(옆에서는 위로 휨)
        double root = a + 0.08 * Math.Max(0, d) * Math.Cos(a);
        double tri = 1 - 2 * Math.Abs(Frac(7 * root / (2 * Math.PI) + frame / 30.0) - 0.5);
        double lick = tri * tri * (0.6 + 0.4 * Math.Sin(3 * root - 2 * Math.PI * frame / 40.0));
        double reach = bottomReach + (topReach - bottomReach) * (0.5 + 0.5 * up) + tongues * lick;
        double rise = 0.5 * Math.Sin(2 * Math.PI * (y / 5.0 + frame / 12.0) + 3 * root) + 0.5 * Math.Sin(2 * Math.PI * (y / 7.0 + frame / 20.0) - 5 * root + 1.3);
        return 0.92 - Math.Max(0, d) / reach + 0.26 * rise;
    }

    /// <summary>불길 세기를 다섯 단계 불꽃 색으로(<paramref name="palette"/>: 가장 뜨거운 색부터).</summary>
    private static Color32 FireColor(double heat, Color32[] palette)
        => heat > 0.85 ? palette[0] : heat > 0.66 ? palette[1] : heat > 0.47 ? palette[2] : heat > 0.3 ? palette[3] : heat > 0.15 ? Fade(palette[4], 0.8) : Color32.Transparent;

    private static readonly Color32[] FlamePalette = [Hex(0xFFF6C2), Hex(0xFFD23F), Hex(0xFF8A1F), Hex(0xE0401A), Hex(0x9A1C0A)];
    private static readonly Color32[] PhoenixPalette = [Hex(0xFFFBE6), Hex(0xFFE066), Hex(0xFFB020), Hex(0xFF6A1A), Hex(0xB8231A)];

    /// <summary>
    /// 불꽃: 프로필 그림 위쪽 반 둘레에서 곧게 위로 치솟는 불길(줄마다 높이가 날름거림)과 둘레를 얇게 감싼 불, 오르는 불티.
    /// </summary>
    private void Flames(PixelPainter p, int frame)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var (_, d) = Polar(x, y);
                if (d < -0.5) continue;
                // 이 줄(x)에서 프로필 그림 위쪽 가장자리까지 아래로 떨어진 거리 — 거기서 불길이 위로 솟는다
                double cx = x + 0.5 - _center;
                double heat = 0.92 - Math.Max(0, d) / 2.8;
                if (Math.Abs(cx) < _radius)
                {
                    double rimY = _center - Math.Sqrt(_radius * _radius - cx * cx);
                    double below = rimY - (y + 0.5);
                    if (below >= -0.5)
                    {
                        double tall = Margin * (0.55 + 0.3 * (1 - Math.Abs(cx) / _radius))
                                      + 2.2 * Math.Sin(2 * Math.PI * (x / 6.0 + frame / 20.0))
                                      + 1.3 * Math.Sin(2 * Math.PI * (x / 3.7 - frame / 12.0) + 1)
                                      + 1.5 * Math.Sin(2 * Math.PI * (x / 9.0 + frame / 30.0) + 2);
                        heat = Math.Max(heat, 0.95 - Math.Max(0, below) / Math.Max(1.5, tall));
                    }
                }
                heat += 0.24 * (0.5 * Math.Sin(2 * Math.PI * (y / 5.0 + frame / 12.0) + x * 0.9) + 0.5 * Math.Sin(2 * Math.PI * (y / 7.0 + frame / 20.0) - x * 0.6 + 1.3));
                var c = FireColor(heat, FlamePalette);
                if (c.A > 0) p.Blend(x, y, c);
            }
        Embers(p, frame, 8, 24, 0.8);
    }

    /// <summary>불티: 위쪽 둘레에서 흔들리며 올라가 꺼진다(노랑 → 주황 → 빨강).</summary>
    private void Embers(PixelPainter p, int frame, int count, int life, double speed)
    {
        for (int i = 0; i < count; i++)
        {
            var (age, cycle) = Life(frame, i, count, life);
            double t = age / (double)life;
            double a = -Math.PI / 2 + (HashUnit(_seed + 5, i, cycle) - 0.5) * Math.PI * 1.5;
            double r = _radius + 2.5 + HashUnit(_seed + 6, i, cycle) * 2;
            double x = _center + Math.Cos(a) * r + Math.Sin(2 * Math.PI * (t * 2 + i * 0.37)) * 1.2;
            double y = _center + Math.Sin(a) * r - age * speed;
            var c = t < 0.35 ? Hex(0xFFF1A8) : t < 0.7 ? Hex(0xFFA62B) : Hex(0xE5482A);
            Dot(p, x, y, Fade(c, 1 - t * 0.8));
        }
    }

    /// <summary>은하 소용돌이: 두 갈래 나선 팔(보라·파랑)이 둘레를 돌고, 팔을 따라 별이 반짝이며 함께 돈다.</summary>
    private void GalaxySwirl(PixelPainter p, int frame)
    {
        double spin = 2 * Math.PI * frame / Loop;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var (a, d) = Polar(x, y);
                if (d < 0.5 || d > Margin + 2) continue;
                double fall = Math.Clamp(1.2 - d / Margin, 0, 1);
                double arm = Math.Pow(0.5 + 0.5 * Math.Cos(2 * (a - spin - 0.42 * d)), 3) * fall;
                // 프로필 그림 바로 바깥은 은은한 빛 무리
                double core = Math.Clamp(1 - (d - 0.5) / 3, 0, 1) * 0.5;
                double v = Math.Max(arm, core * 0.7);
                Color32 c = v > 0.62 ? Fade(Hex(0xDCD3FF), 0.9) : v > 0.38 ? Fade(Hex(0x7C83F5), 0.75) : v > 0.18 ? Fade(Hex(0x6D3FC8), 0.55) : v > 0.07 ? Fade(Hex(0x3B1A7A), 0.4) : Color32.Transparent;
                if (c.A > 0) p.Blend(x, y, c);
            }
        // 팔 위의 별: 팔과 함께 돌며 저마다 깜빡인다
        for (int i = 0; i < 22; i++)
        {
            double d = 1 + HashUnit(_seed, i, 0) * (Margin - 1.5);
            double a = spin + 0.42 * d + (i % 2) * Math.PI + (HashUnit(_seed, i, 1) - 0.5) * 0.5;
            double x = _center + Math.Cos(a) * (_radius + d), y = _center + Math.Sin(a) * (_radius + d);
            double tw = Pulse(frame + i * 7, i % 3 == 0 ? 24 : 40);
            var c = i % 5 == 0 ? Hex(0xFFD6F5) : i % 3 == 0 ? Hex(0xBFE0FF) : Color32.White;
            if (i % 6 == 0 && tw > 0.7 && !OverFace(x, y))
                LevelBadgeArt.Sparkle(p, (int)Math.Floor(x), (int)Math.Floor(y), 1, SparkleShape.Star, c);
            else Dot(p, x, y, Fade(c, 0.35 + 0.65 * tw));
        }
    }

    /// <summary>유성우: 오른쪽 위에서 왼쪽 아래로 긴 빛 꼬리를 끄는 유성이 잇달아 지나간다(푸른빛·금빛).</summary>
    private void Meteors(PixelPainter p, int frame)
    {
        for (int i = 0; i < 6; i++)
        {
            int x = HashInt(_seed, i, 99, Width), y = HashInt(_seed + 1, i, 99, Height);
            Dot(p, x, y, Fade(Color32.White, 0.2 + 0.5 * Pulse(frame + i * 13, 40)));
        }
        const double Dx = -0.8, Dy = 0.6, Speed = 3.8;
        for (int i = 0; i < 8; i++)
        {
            var (age, cycle) = Life(frame, i, 8, 20);
            if (age >= 13) continue;
            // 위쪽 가장자리나 오른쪽 가장자리에서 들어온다
            double s = HashUnit(_seed + 2, i, cycle) * (Width + Height * 0.6);
            double sx = s < Width ? s + 6 : Width + 2, sy = s < Width ? -3 : s - Width - 3;
            double hx = sx + Dx * Speed * age, hy = sy + Dy * Speed * age;
            var tail = MeteorTails[(i + cycle) % 2];
            for (int k = 15; k >= 0; k--)
            {
                double tx = hx - Dx * k * 0.9, ty = hy - Dy * k * 0.9;
                double fade = 1 - k / 16.0;
                var c = Fade(tail[Math.Min(tail.Length - 1, k / 3)], fade);
                Dot(p, tx, ty, c);
                // 꼬리 앞쪽은 양옆으로 은은히 번진다
                if (k < 6)
                {
                    Dot(p, tx - Dy, ty + Dx, Fade(tail[2], 0.3 * fade));
                    Dot(p, tx + Dy, ty - Dx, Fade(tail[2], 0.3 * fade));
                }
            }
            // 머리는 십자로 빛남
            Dot(p, hx + 1, hy, Fade(tail[1], 0.6));
            Dot(p, hx, hy - 1, Fade(tail[1], 0.6));
            Dot(p, hx - 1, hy, Fade(tail[1], 0.4));
            Dot(p, hx, hy + 1, Fade(tail[1], 0.4));
        }
    }

    private static readonly Color32[][] MeteorTails =
    [
        [Color32.White, Hex(0xCFF6FF), Hex(0x7DD3FC), Hex(0x60A5FA), Hex(0x818CF8), Hex(0x6366F1)],
        [Color32.White, Hex(0xFFF3C4), Hex(0xFDE68A), Hex(0xFBBF24), Hex(0xF97316), Hex(0xEF4444)],
    ];

    private static readonly string[] GoldFeatherRows = ["....h", "...gh", "..gg.", ".gw..", "o...."];

    /// <summary>
    /// 불사조 오라: 양옆으로 펄럭이는 불꽃 날개, 금빛 불길 고리와 바깥으로 퍼지는 빛 물결, 오르는 불티, 가끔 흩날리는 금빛 깃털과 반짝이.
    /// </summary>
    private void PhoenixAura(PixelPainter p, int frame)
    {
        Halo(p, frame, Hex(0xFF9A2E), 0.22, 0.18);
        PhoenixWings(p, frame);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var c = FireColor(FireHeat(x, y, frame, 1.4, 3.6, 1.6), PhoenixPalette);
                if (c.A > 0) p.Blend(x, y, c);
            }
        // 빛 물결: 30프레임마다 금빛 불똥 고리가 바깥으로 퍼지며 흐려진다
        double wave = Mod(frame, 30) / 30.0;
        double rd = _radius + 2.5 + wave * (Margin - 3.5);
        for (int k = 0; k < 28; k++)
        {
            double a = 2 * Math.PI * (k + 0.5 * (Mod(frame / 30, 2))) / 28;
            Dot(p, _center + Math.Cos(a) * rd - 0.5, _center + Math.Sin(a) * rd - 0.5, Fade(k % 2 == 0 ? Hex(0xFFE58A) : Hex(0xFFB020), 1 - wave * wave));
        }
        Embers(p, frame, 10, 30, 0.7);
        // 금빛 깃털: 둘레 위쪽에서 흔들리며 내려오다 사라진다
        for (int i = 0; i < 2; i++)
        {
            var (age, cycle) = Life(frame, i, 2, 60);
            if (age >= 44) continue;
            double t = age / 44.0;
            bool left = (i + cycle) % 2 == 0;
            double x0 = left ? 2 + HashUnit(_seed, i, cycle) * 5 : Width - 7 - HashUnit(_seed, i, cycle) * 5;
            double swing = Math.Sin(2 * Math.PI * t * 2);
            int x = (int)Math.Floor(x0 + swing * 2.2), y = (int)Math.Floor(2 + t * (Height * 0.7));
            double alpha = Math.Clamp(Math.Min(t, 1 - t) / 0.2, 0, 1);
            if (OverFace(x + 2, y + 2)) continue;
            Color32 h = Fade(Hex(0xFFFBE6), alpha), g = Fade(Hex(0xFFC93C), alpha), w = Fade(Hex(0xFF8A1F), alpha), o = Fade(Hex(0xB8641E), alpha);
            p.SpriteOutlined(x, y, GoldFeatherRows, ch => ch switch { 'h' => h, 'w' => w, 'o' => o, _ => g }, Fade(Hex(0x5A1206), alpha * 0.7),
                mirror: Math.Cos(2 * Math.PI * t * 2) < 0);
        }
        for (int i = 0; i < 5; i++)
        {
            var (age, cycle) = Life(frame, i, 5, 12);
            if (age >= 10) continue;
            double a = 2 * Math.PI * HashUnit(_seed + 7, i, cycle);
            double r = _radius + 3 + HashUnit(_seed + 8, i, cycle) * (Margin - 5);
            int size = age switch { <= 1 or 9 => 0, <= 3 or 7 or 8 => 1, _ => 2 };
            int sx = (int)Math.Floor(_center + Math.Cos(a) * r), sy = (int)Math.Floor(_center + Math.Sin(a) * r);
            if (!OverFace(sx, sy)) LevelBadgeArt.Sparkle(p, sx, sy, size, SparkleShape.Star, Hex(0xFFE58A));
        }
    }

    /// <summary>
    /// 불사조 날개: 양옆 위쪽에서 부채처럼 펼친 깃 다섯 장(뿌리 금빛 → 끝 붉은빛, 깃 끝은 둥글고 깃 사이는 어두운 줄)이 40프레임마다 한 번 펄럭인다.
    /// </summary>
    private void PhoenixWings(PixelPainter p, int frame)
    {
        // 왼쪽 날개를 구해 좌우 대칭으로 찍는다
        double pivotAngle = Math.PI + 0.35;
        double px = _center + Math.Cos(pivotAngle) * (_radius - 1), py = _center + Math.Sin(pivotAngle) * (_radius - 1);
        double lift = 0.22 * Math.Sin(2 * Math.PI * frame / 40.0);
        const double Low = Math.PI - 0.35, High = Math.PI + 1.25;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width / 2; x++)
            {
                if (OverFace(x, y)) continue;
                double dx = x + 0.5 - px, dy = y + 0.5 - py, dist = Math.Sqrt(dx * dx + dy * dy);
                double phi = Math.Atan2(dy, dx);
                if (phi < 0) phi += 2 * Math.PI;
                phi -= lift;
                if (phi < Low || phi > High) continue;
                double s = (phi - Low) / (High - Low);
                double feather = Frac(s * 5);
                double length = Margin * (0.55 + 0.5 * s) * (0.8 + 0.2 * Math.Sin(Math.PI * feather));
                if (dist > length) continue;
                double r = dist / length;
                var c = r < 0.35 ? Hex(0xFFF1A8) : r < 0.6 ? Hex(0xFFC53D) : r < 0.82 ? Hex(0xFF7A1F) : Hex(0xD9361A);
                if (feather < 0.14 && r > 0.4) c = Darken(c, 0.35);
                c = Fade(c, 0.92);
                p.Blend(x, y, c);
                p.Blend(Width - 1 - x, y, c);
            }
    }
}
