using Chickxel.Core.Model;

namespace Chickxel.Core.Community;

/// <summary>그림 안에서 붙는 대상(배지·프로필 그림·카드)이 차지하는 칸(픽셀 그림 좌표).</summary>
public readonly record struct ArtBox(int X, int Y, int Width, int Height);

/// <summary>
/// 레벨 보상의 움직이는 픽셀 그림 하나(배지·테두리·효과·카드). 앱은 정수 배로 키워 바로 그리고, 홈페이지용으로는 같은 프레임을 스프라이트 시트로 내보낸다.
/// 그림은 대상 칸(<see cref="Box"/>) 밖까지 번질 수 있다(빛·날개·반짝이). 움직이는 그림은 <see cref="Frames"/>장마다 처음과 이어진다.
/// </summary>
public interface IPixelArt
{
    /// <summary>그림 전체(여백 포함) 폭·높이(픽셀).</summary>
    int Width { get; }
    int Height { get; }
    /// <summary>대상이 차지하는 칸.</summary>
    ArtBox Box { get; }
    /// <summary>되풀이 길이(프레임 수, 1 = 멈춘 그림).</summary>
    int Frames { get; }
    /// <summary><paramref name="frame"/>번째 프레임을 <paramref name="pixels"/>(<see cref="Width"/> × <see cref="Height"/>, 위 줄부터)에 그린다.</summary>
    void Render(int frame, Span<Color32> pixels);
}

/// <summary>보상 그림이 함께 쓰는 도구: 되풀이 길이, 해시, 색 섞기, 픽셀 찍기.</summary>
public static class RewardArt
{
    /// <summary>움직이는 그림의 되풀이 길이(80ms × 120 = 9.6초). 그림 안의 모든 주기는 이 수의 약수다.</summary>
    public const int Loop = 120;
    /// <summary>프레임 하나의 길이(ms).</summary>
    public const int FrameMs = 80;

    public static readonly Color32[] Rainbow =
        [Hex(0xE8475A), Hex(0xE59A2E), Hex(0xF2D33B), Hex(0x2FB868), Hex(0x2C84D8), Hex(0x9050E0)];

    public static Color32 Hex(uint rgb) => new(0xFF000000u | rgb);

    public static Color32 Mix(Color32 a, Color32 b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte L(byte u, byte v) => (byte)Math.Round(u + (v - u) * t);
        return new Color32(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B), L(a.A, b.A));
    }

    public static Color32 Lighten(Color32 c, double t) => Mix(c, Color32.White.WithAlpha(c.A), t);

    public static Color32 Darken(Color32 c, double t) => Mix(c, new Color32(0, 0, 0, c.A), t);

    /// <summary>알파에 <paramref name="alpha"/>(0~1)를 곱한 색.</summary>
    public static Color32 Fade(Color32 c, double alpha) => c.WithAlpha((byte)Math.Round(c.A * Math.Clamp(alpha, 0, 1)));

    public static int Mod(int a, int m) => ((a % m) + m) % m;

    public static double Frac(double v) => v - Math.Floor(v);

    /// <summary>주기 <paramref name="period"/>(<see cref="Loop"/>의 약수)로 0 → 1 → 0을 오가는 값.</summary>
    public static double Pulse(int frame, int period) => 0.5 - 0.5 * Math.Cos(2 * Math.PI * Mod(frame, period) / period);

    /// <summary>무지개를 <paramref name="t"/>(0~1, 넘으면 되풀이)에서 고른 색(여섯 색을 끊어서 — 픽셀 그림답게).</summary>
    public static Color32 RainbowAt(double t) => Rainbow[Mod((int)Math.Floor(Frac(t) * Rainbow.Length), Rainbow.Length)];

    public static uint Hash(int a, int b, int c)
    {
        uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return h;
    }

    /// <summary>0 이상 <paramref name="n"/> 미만의 해시 수.</summary>
    public static int HashInt(int a, int b, int c, int n) => (int)(Hash(a, b, c) % (uint)Math.Max(1, n));

    /// <summary>0~1의 해시 수.</summary>
    public static double HashUnit(int a, int b, int c) => (Hash(a, b, c) & 0xFFFFFF) / (double)0x1000000;

    /// <summary>
    /// 되풀이 안에서 <paramref name="life"/>프레임씩 피었다 지는 것 <paramref name="index"/>번의 지금 나이와 몇 번째 삶인지(되풀이 끝에서 처음과 이어짐 —
    /// <paramref name="life"/>는 <see cref="Loop"/>의 약수). 것마다 시작이 어긋난다.
    /// </summary>
    public static (int Age, int Cycle) Life(int frame, int index, int count, int life)
    {
        int shifted = Mod(frame + index * Loop / Math.Max(1, count) + index * 7, Loop);
        return (shifted % life, shifted / life);
    }

    /// <summary>글(보상 ID)의 고정 씨앗 수(FNV-1a — 실행마다 같음).</summary>
    public static int Seed(string text)
    {
        uint h = 2166136261;
        foreach (char c in text) h = (h ^ c) * 16777619;
        return (int)(h & 0x7FFFFFFF);
    }
}

/// <summary>픽셀 버퍼에 찍는 도구(범위 밖은 버림). <see cref="Blend"/>는 알파로 덮고 <see cref="Set"/>은 바꾼다.</summary>
public readonly ref struct PixelPainter(Span<Color32> pixels, int width, int height)
{
    private readonly Span<Color32> _pixels = pixels;

    public int Width { get; } = width;
    public int Height { get; } = height;

    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public Color32 Get(int x, int y) => Inside(x, y) ? _pixels[y * Width + x] : Color32.Transparent;

    public void Set(int x, int y, Color32 c)
    {
        if (Inside(x, y)) _pixels[y * Width + x] = c;
    }

    public void Blend(int x, int y, Color32 c)
    {
        if (!Inside(x, y) || c.A == 0) return;
        ref var p = ref _pixels[y * Width + x];
        p = Color32.BlendOver(p, c);
    }

    /// <summary>
    /// 한 줄 글자 그림(<paramref name="rows"/> — 위 줄부터, 글자마다 색 이름: '.' = 빈칸, 나머지는 <paramref name="palette"/>의 색)을
    /// (<paramref name="x"/>, <paramref name="y"/>)부터 찍는다. <paramref name="mirror"/>면 좌우를 뒤집는다.
    /// </summary>
    public void Sprite(int x, int y, string[] rows, Func<char, Color32> palette, bool mirror = false)
    {
        for (int r = 0; r < rows.Length; r++)
        {
            string row = rows[r];
            for (int i = 0; i < row.Length; i++)
            {
                char ch = row[mirror ? row.Length - 1 - i : i];
                if (ch != '.') Blend(x + i, y + r, palette(ch));
            }
        }
    }

    /// <summary><see cref="Sprite"/>와 같고, 먼저 그림 둘레(상하좌우로 닿는 빈칸)를 <paramref name="outline"/>으로 감싼다(어떤 바탕에서도 모양이 보이게).</summary>
    public void SpriteOutlined(int x, int y, string[] rows, Func<char, Color32> palette, Color32 outline, bool mirror = false)
    {
        bool Filled(int r, int i)
        {
            if (r < 0 || r >= rows.Length) return false;
            string row = rows[r];
            return i >= 0 && i < row.Length && row[mirror ? row.Length - 1 - i : i] != '.';
        }
        int widest = rows.Max(r => r.Length);
        for (int r = -1; r <= rows.Length; r++)
            for (int i = -1; i <= widest; i++)
                if (!Filled(r, i) && (Filled(r - 1, i) || Filled(r + 1, i) || Filled(r, i - 1) || Filled(r, i + 1)))
                    Blend(x + i, y + r, outline);
        Sprite(x, y, rows, palette, mirror);
    }
}
