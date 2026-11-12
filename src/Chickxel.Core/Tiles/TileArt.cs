using Chickxel.Core.Model;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 기본 타일셋 그림을 코드로 그리는 도우미: 칸 하나의 자동 타일 모양(이웃 비트)으로 빈 이웃까지의 거리를 재는 지형 그리기, 칸 크기로 이어지는 돌 무늬,
/// 빛 방향 음영의 덩어리(나무 잎·바위), 외곽선, 글자 그림. 같은 코드면 늘 같은 그림이다.
/// </summary>
public static class TileArt
{
    public const int Cell = 16;

    /// <summary>"#RRGGBB" → 색.</summary>
    public static Color32 C(string hex) => Color32.TryParseHex(hex, out var c) ? c : throw new ArgumentException(hex);

    /// <summary>칸 좌표의 고른 난수(0~1).</summary>
    public static double Rand(int x, int y, int seed) => (AutoTiler.Hash(x, y, seed) & 0xFFFFFF) / (double)0x1000000;

    /// <summary>
    /// 자동 타일 모양 <paramref name="mask"/>(8방향 이웃 비트, 빈 비트 = 빈 이웃 칸)의 칸에서 점 (<paramref name="px"/>, <paramref name="py"/>)(칸 왼쪽 위 기준 px, 칸 밖도 됨)이
    /// 가장 가까운 빈 이웃 칸까지 떨어진 거리와 그쪽 방향(단위 벡터, 점 → 빈 곳). 두 변이 빈 바깥 모서리는 반지름 <paramref name="round"/>로 둥글게(밖이면 음수),
    /// 안쪽 모서리는 빈 대각 칸까지의 거리라 저절로 둥글다. 빈 이웃이 없으면 (99, 0, 0).
    /// 칸 안의 거리는 둘레 칸만으로 정해지므로, 맞닿은 두 칸이 이음매 근처에서 같은 값을 얻는다(무늬가 이어짐).
    /// </summary>
    public static (double D, double Nx, double Ny) OpenDistance(byte mask, double px, double py, double round)
    {
        double best = 99, bx = 0, by = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            if ((mask & (1 << bit)) != 0) continue;
            var (dx, dy) = AutoTileShapes.Directions[bit];
            double x0 = dx * Cell, y0 = dy * Cell;
            double vx = px < x0 ? x0 - px : px > x0 + Cell ? x0 + Cell - px : 0;
            double vy = py < y0 ? y0 - py : py > y0 + Cell ? y0 + Cell - py : 0;
            double d = Math.Sqrt(vx * vx + vy * vy);
            if (d >= best) continue;
            best = d;
            (bx, by) = d > 0 ? (vx / d, vy / d) : (dx, dy);
        }
        // 바깥 모서리: 두 변이 비면 원으로
        void Outer(int a, int b, double cx, double cy, double sx, double sy)
        {
            if ((mask & a) != 0 || (mask & b) != 0) return;
            double ox = cx + sx * round, oy = cy + sy * round;
            double lx = px - ox, ly = py - oy;
            if (lx * sx > 0 || ly * sy > 0) return;
            double r = Math.Sqrt(lx * lx + ly * ly);
            double d = round - r;
            if (d >= best || r <= 0) return;
            best = d;
            (bx, by) = (lx / r, ly / r);
        }
        Outer(AutoTileShapes.N, AutoTileShapes.W, 0, 0, 1, 1);
        Outer(AutoTileShapes.N, AutoTileShapes.E, Cell, 0, -1, 1);
        Outer(AutoTileShapes.S, AutoTileShapes.W, 0, Cell, 1, -1);
        Outer(AutoTileShapes.S, AutoTileShapes.E, Cell, Cell, -1, -1);
        return (best, bx, by);
    }

    /// <summary>칸 크기로 이어지는 돌 무늬의 씨앗들: <paramref name="n"/>×<paramref name="n"/> 격자(줄마다 반 칸 어긋남)를 조금씩 흔든 자리.</summary>
    public static (double X, double Y)[] Seeds(int n, int salt, double jitter = 0.6)
    {
        var seeds = new (double, double)[n * n];
        double step = (double)Cell / n;
        for (int gy = 0; gy < n; gy++)
            for (int gx = 0; gx < n; gx++)
            {
                double x = (gx + 0.5 + gy % 2 * 0.5 + (Rand(gx, gy, salt) - 0.5) * jitter) * step;
                double y = (gy + 0.5 + (Rand(gx, gy, salt + 1) - 0.5) * jitter) * step;
                seeds[gy * n + gx] = ((x % Cell + Cell) % Cell, y);
            }
        return seeds;
    }

    /// <summary>
    /// 칸 크기로 이어지는 돌 무늬(보로노이): 점이 속한 돌의 번호, 그 돌 가운데(칸 밖일 수 있음)와 거기까지 거리, 이웃 돌과의 경계까지 여유(둘째 − 첫째 거리, 0 = 경계).
    /// <paramref name="squashY"/> &gt; 1이면 돌이 가로로 납작하다.
    /// </summary>
    public static (int Index, double Sx, double Sy, double Dist, double Edge) Cobble((double X, double Y)[] seeds, double px, double py, double squashY = 1)
    {
        double best = 1e9, second = 1e9, sx = 0, sy = 0;
        int index = 0;
        for (int i = 0; i < seeds.Length; i++)
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    double cx = seeds[i].X + ox * Cell, cy = seeds[i].Y + oy * Cell;
                    double dx = px - cx, dy = (py - cy) * squashY;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < best) { second = best; best = d; sx = cx; sy = cy; index = i; }
                    else if (d < second) second = d;
                }
        return (index, sx, sy, best, second - best);
    }

    /// <summary>점마다 칠한다(null = 그대로).</summary>
    public static PixelImage Paint(int w, int h, Func<int, int, Color32?> color)
    {
        var img = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (color(x, y) is { } c) img.Pixels[y * w + x] = c;
        return img;
    }

    /// <summary>점 하나를 알파로 겹친다.</summary>
    public static void Put(PixelImage img, int x, int y, Color32 c)
    {
        if (!img.Contains(x, y)) return;
        img.Pixels[y * img.Width + x] = Color32.BlendOver(img.Pixels[y * img.Width + x], c);
    }

    /// <summary>덩어리 하나(원·타원)의 모양: 가운데, 반지름.</summary>
    public readonly record struct Blob(double X, double Y, double Rx, double Ry);

    /// <summary>
    /// 덩어리들을 뒤에서 앞으로(목록 차례) 칠한다: 위 왼쪽 빛으로 음영 단계(<paramref name="ramp"/> 어두움 → 밝음)를 고르고,
    /// 앞 덩어리의 아래·오른쪽 가장자리가 뒤 덩어리 위에 걸치면 그 자리를 <paramref name="crease"/>로(덩어리끼리 갈라진 선). 끝으로 실루엣 둘레를 <paramref name="outline"/>로.
    /// <paramref name="speckle"/>(0~1)만큼 밝은 쪽에는 밝은 점, 어두운 쪽에는 어두운 점을 흩뿌린다(잎 질감).
    /// </summary>
    public static void Clumps(PixelImage img, IReadOnlyList<Blob> blobs, Color32[] ramp, Color32 crease, Color32 outline, int seed, double speckle = 0)
    {
        int w = img.Width, h = img.Height;
        var filled = new bool[w * h];
        // 빛 방향(위 왼쪽 앞)
        double lx = -0.55, ly = -0.65, lz = 0.52;
        foreach (var b in blobs)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double dx = (x + 0.5 - b.X) / b.Rx, dy = (y + 0.5 - b.Y) / b.Ry;
                    double r2 = dx * dx + dy * dy;
                    if (r2 > 1) continue;
                    double nz = Math.Sqrt(Math.Max(0, 1 - r2));
                    double shade = (dx * lx + dy * ly + nz * lz + 0.15) / 1.15; // −1 ~ 1
                    // 덩어리 가장자리 아래·오른쪽은 한 단계 더 어둡게(둥근 맛)
                    if (r2 > 0.72 && dx * 0.6 + dy > 0.3) shade -= 0.35;
                    int idx = (int)Math.Clamp(Math.Floor((shade + 0.55) / 1.45 * ramp.Length), 0, ramp.Length - 1);
                    var c = ramp[idx];
                    if (speckle > 0)
                    {
                        double rnd = Rand(x, y, seed);
                        if (rnd < speckle * 0.5 && idx > 0 && idx < ramp.Length - 1) c = ramp[idx + 1];
                        else if (rnd > 1 - speckle * 0.5 && idx > 0) c = ramp[idx - 1];
                    }
                    // 앞 덩어리의 아래 둘레가 이미 칠한 곳 위에 오면 갈라진 선
                    bool rim = r2 > 0.80 && dy > -0.2;
                    if (rim && filled[y * w + x]) c = crease;
                    img.Pixels[y * w + x] = c;
                    filled[y * w + x] = true;
                }
        }
        Outline(img, outline);
    }

    /// <summary>불투명 픽셀 둘레(4방향으로 투명과 맞닿은 바깥 칸)에 외곽선을 그린다.</summary>
    public static void Outline(PixelImage img, Color32 color, bool diagonal = false)
    {
        int w = img.Width, h = img.Height;
        var src = (Color32[])img.Pixels.Clone();
        bool Solid(int x, int y) => (uint)x < (uint)w && (uint)y < (uint)h && src[y * w + x].A > 128;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (Solid(x, y)) continue;
                bool edge = Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1)
                            || diagonal && (Solid(x - 1, y - 1) || Solid(x + 1, y - 1) || Solid(x - 1, y + 1) || Solid(x + 1, y + 1));
                if (edge) img.Pixels[y * w + x] = Color32.BlendOver(src[y * w + x], color);
            }
    }

    /// <summary>
    /// 글자 그림: 글자마다 한 픽셀, <paramref name="palette"/>에 없는 글자는 투명.
    /// </summary>
    public static PixelImage Art(Dictionary<char, Color32> palette, params string[] rows)
    {
        int w = rows.Max(r => r.Length), h = rows.Length;
        var img = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (palette.TryGetValue(rows[y][x], out var c)) img.Pixels[y * w + x] = c;
        return img;
    }

    /// <summary>그림을 (<paramref name="x"/>, <paramref name="y"/>)에 알파로 겹친다.</summary>
    public static void Stamp(PixelImage dst, PixelImage src, int x, int y) => dst.Blit(src, x, y, alphaBlend: true);
}
