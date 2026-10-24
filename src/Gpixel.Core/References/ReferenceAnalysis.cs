using System.Globalization;
using System.Numerics;
using System.Text;
using Gpixel.Core.Model;

namespace Gpixel.Core.References;

/// <summary>참고 그림에서 저절로 읽는 정보: 크기, 프레임 수, 색 수, 팔레트(많이 쓴 색부터), 실루엣.</summary>
public sealed record ReferenceStats(int Width, int Height, int Frames, int Colors, IReadOnlyList<Color32> Palette, Silhouette Silhouette);

/// <summary>
/// 실루엣 지문: 첫 프레임의 불투명 영역(경계 상자)을 16×16 칸으로 나눠, 칸의 절반 이상이 불투명하면 1인 256비트와 경계 상자 가로/세로 비.
/// 크기가 달라도 모양이 비슷하면 가깝다(해밍 거리).
/// </summary>
public readonly record struct Silhouette(ulong A, ulong B, ulong C, ulong D, double Aspect)
{
    public const int Size = 16;
    public const int Bits = Size * Size;

    public static Silhouette Empty => new(0, 0, 0, 0, 1);

    public bool IsEmpty => (A | B | C | D) == 0;

    public bool Get(int x, int y)
    {
        int i = y * Size + x;
        ulong word = (i >> 6) switch { 0 => A, 1 => B, 2 => C, _ => D };
        return ((word >> (63 - (i & 63))) & 1) != 0;
    }

    /// <summary>다른 비트 수(0~256).</summary>
    public int Hamming(Silhouette other)
        => BitOperations.PopCount(A ^ other.A) + BitOperations.PopCount(B ^ other.B)
         + BitOperations.PopCount(C ^ other.C) + BitOperations.PopCount(D ^ other.D);

    /// <summary>64자 16진수(앞 비트 = 왼쪽 위).</summary>
    public string ToHex() => $"{A:x16}{B:x16}{C:x16}{D:x16}";

    public static Silhouette Parse(string? hex, double aspect)
    {
        if (hex is not { Length: 64 }) return Empty with { Aspect = Sane(aspect) };
        ulong Part(int i) => ulong.TryParse(hex.AsSpan(i * 16, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
        return new Silhouette(Part(0), Part(1), Part(2), Part(3), Sane(aspect));
    }

    private static double Sane(double aspect) => double.IsFinite(aspect) && aspect > 0 ? aspect : 1;

    /// <summary>시험·디버그용 그림(줄마다 '#'/'.').</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++) sb.Append(Get(x, y) ? '#' : '.');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>비슷한 그림 찾기의 기준(지금 캔버스나 다른 참고 자료): 팔레트와 실루엣.</summary>
public sealed record ReferenceSignature(IReadOnlyList<Color32> Palette, Silhouette Silhouette)
{
    public static ReferenceSignature Of(ReferenceItem item)
        => new(item.PaletteColors(), Silhouette.Parse(item.Silhouette, item.Aspect));

    public static ReferenceSignature Of(IReadOnlyList<PixelImage> frames)
    {
        var s = ReferenceAnalysis.Analyze(frames);
        return new(s.Palette, s.Silhouette);
    }
}

public static class ReferenceAnalysis
{
    /// <summary>색인에 남기는 팔레트 색 수 상한.</summary>
    public const int PaletteCap = 64;

    /// <summary>프레임들(크기가 같아야 함)을 살펴 정보를 만든다. 실루엣은 첫 프레임에서, 색은 모든 프레임에서 센다.</summary>
    public static ReferenceStats Analyze(IReadOnlyList<PixelImage> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("no frames", nameof(frames));
        var counts = new Dictionary<uint, int>();
        foreach (var f in frames)
        {
            foreach (var c in f.Pixels)
            {
                if (c.A == 0) continue;
                counts[c.Argb] = counts.GetValueOrDefault(c.Argb) + 1;
            }
        }
        var palette = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(PaletteCap)
            .Select(kv => new Color32(kv.Key))
            .ToList();
        return new ReferenceStats(frames[0].Width, frames[0].Height, frames.Count, counts.Count, palette, SilhouetteOf(frames[0]));
    }

    /// <summary>불투명 경계 상자를 16×16 칸으로 나눈 실루엣(칸 픽셀의 절반 이상이 불투명하면 1). 빈 그림은 <see cref="Silhouette.Empty"/>.</summary>
    public static Silhouette SilhouetteOf(PixelImage image)
    {
        var box = image.GetOpaqueBounds();
        if (box.Width <= 0 || box.Height <= 0) return Silhouette.Empty;
        Span<ulong> words = stackalloc ulong[4];
        for (int cy = 0; cy < Silhouette.Size; cy++)
        {
            int y0 = box.Y + cy * box.Height / Silhouette.Size;
            int y1 = Math.Max(y0 + 1, box.Y + (cy + 1) * box.Height / Silhouette.Size);
            for (int cx = 0; cx < Silhouette.Size; cx++)
            {
                int x0 = box.X + cx * box.Width / Silhouette.Size;
                int x1 = Math.Max(x0 + 1, box.X + (cx + 1) * box.Width / Silhouette.Size);
                int opaque = 0, total = 0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        total++;
                        if (image.Pixels[y * image.Width + x].A != 0) opaque++;
                    }
                }
                if (opaque * 2 < total) continue;
                int i = cy * Silhouette.Size + cx;
                words[i >> 6] |= 1UL << (63 - (i & 63));
            }
        }
        return new Silhouette(words[0], words[1], words[2], words[3], box.Width / (double)box.Height);
    }

    /// <summary>
    /// 팔레트 거리(0 = 같음 ~ 1): 한쪽 각 색에서 다른 쪽 가장 가까운 색까지 RGB 거리의 평균을 양쪽으로 구해 평균한다. 한쪽이 비면 1.
    /// </summary>
    public static double PaletteDistance(IReadOnlyList<Color32> a, IReadOnlyList<Color32> b)
    {
        if (a.Count == 0 || b.Count == 0) return 1;
        return (OneWay(a, b) + OneWay(b, a)) / 2;

        static double OneWay(IReadOnlyList<Color32> from, IReadOnlyList<Color32> to)
        {
            const double Max = 441.6729559300637; // √(3·255²)
            double sum = 0;
            foreach (var c in from)
            {
                double best = double.MaxValue;
                foreach (var d in to)
                {
                    int dr = c.R - d.R, dg = c.G - d.G, db = c.B - d.B;
                    best = Math.Min(best, dr * dr + dg * dg + db * db);
                }
                sum += Math.Sqrt(best) / Max;
            }
            return sum / from.Count;
        }
    }

    /// <summary>실루엣 거리(0~1): 다른 비트 비율 80% + 가로/세로 비 차이 20%(4배 차이면 최대).</summary>
    public static double SilhouetteDistance(Silhouette a, Silhouette b)
    {
        double bits = a.Hamming(b) / (double)Silhouette.Bits;
        double ratio = Math.Min(1, Math.Abs(Math.Log(a.Aspect / b.Aspect)) / Math.Log(4));
        return bits * 0.8 + ratio * 0.2;
    }

    /// <summary>닮음(0~1, 클수록 비슷): 팔레트 거리와 실루엣 거리의 평균을 1에서 뺀다.</summary>
    public static double Similarity(ReferenceSignature a, ReferenceSignature b)
        => 1 - (PaletteDistance(a.Palette, b.Palette) + SilhouetteDistance(a.Silhouette, b.Silhouette)) / 2;
}
