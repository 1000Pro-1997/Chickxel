using Chickxel.Core.Community;
using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.App;

/// <summary>
/// 새 레벨 보상 알림 반짝임: 대상 칸(배지 등, <paramref name="width"/> × <paramref name="height"/>) 둘레에 금빛이 숨 쉬듯 번지고, 네 모서리에서 별이 차례로 터진다.
/// 대상 뒤에 겹쳐 그린다(<see cref="LevelChip"/>, 계정 메뉴).
/// </summary>
internal sealed class RewardAttentionArt(int width, int height) : IPixelArt
{
    private const int Margin = 5;

    public int Width => width + 2 * Margin;
    public int Height => height + 2 * Margin;
    public ArtBox Box => new(Margin, Margin, width, height);
    public int Frames => Loop;

    public void Render(int frame, Span<Color32> pixels)
    {
        frame = Mod(frame, Loop);
        pixels[..(Width * Height)].Clear();
        var p = new PixelPainter(pixels, Width, Height);
        double pulse = Pulse(frame, 24);
        var gold = Hex(0xFFD54A);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int bx = x - Margin, by = y - Margin;
                int dx = bx < 0 ? -bx : bx >= width ? bx - width + 1 : 0;
                int dy = by < 0 ? -by : by >= height ? by - height + 1 : 0;
                int d = dx + dy;
                if (d is 0 or > 3) continue;
                p.Blend(x, y, Fade(gold, (0.55 - d * 0.15) * (0.35 + 0.65 * pulse)));
            }
        // 네 모서리 별: 24프레임마다 다음 모서리
        (int X, int Y)[] corners = [(Margin - 2, Margin - 2), (Margin + width + 1, Margin - 2), (Margin + width + 1, Margin + height + 1), (Margin - 2, Margin + height + 1)];
        for (int i = 0; i < corners.Length; i++)
        {
            int age = Mod(frame - i * 6, 24);
            if (age >= 10) continue;
            int size = age switch { <= 1 or 9 => 0, <= 3 or 7 or 8 => 1, _ => 2 };
            LevelBadgeArt.Sparkle(p, corners[i].X, corners[i].Y, size, SparkleShape.Star, Hex(0xFFF3B0));
        }
    }
}

/// <summary>메뉴 아이콘 크기의 반짝이는 별(7×7 — 피었다 지기를 되풀이, 둘레에 금빛). 새 보상이 있을 때 계정 메뉴의 계정 관리 앞에.</summary>
internal sealed class RewardStarArt : IPixelArt
{
    public int Width => 9;
    public int Height => 9;
    public ArtBox Box => new(1, 1, 7, 7);
    public int Frames => Loop;

    public void Render(int frame, Span<Color32> pixels)
    {
        frame = Mod(frame, Loop);
        pixels[..(Width * Height)].Clear();
        var p = new PixelPainter(pixels, Width, Height);
        int age = Mod(frame, 20);
        int size = age switch { < 3 or >= 17 => 1, < 6 or >= 14 => 2, _ => 3 };
        var gold = Hex(0xFFD54A);
        p.Blend(4, 4, Color32.White);
        for (int r = 1; r <= size; r++)
        {
            var arm = r == size && size > 1 ? Fade(gold, 0.7) : gold;
            p.Blend(4 - r, 4, arm); p.Blend(4 + r, 4, arm); p.Blend(4, 4 - r, arm); p.Blend(4, 4 + r, arm);
        }
        if (size == 3)
        {
            p.Blend(3, 3, Fade(gold, 0.6)); p.Blend(5, 3, Fade(gold, 0.6)); p.Blend(3, 5, Fade(gold, 0.6)); p.Blend(5, 5, Fade(gold, 0.6));
        }
    }
}
