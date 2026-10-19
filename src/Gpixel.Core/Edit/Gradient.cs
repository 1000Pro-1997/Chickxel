using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>그라데이션 색 정지점: 위치(0~1, 처음 → 끝)와 색.</summary>
public readonly record struct GradientStop(double Position, Color32 Color);

/// <summary>그라데이션 불투명도 정지점: 위치(0~1)와 불투명도(0 = 투명 ~ 255 = 불투명). 색 정지점과 따로 둔다.</summary>
public readonly record struct GradientAlphaStop(double Position, byte Alpha);

/// <summary>정지점 사이 색 정하기.</summary>
public enum GradientInterpolation
{
    /// <summary>이웃한 두 정지점 색을 RGBA(알파 포함) 선형으로 섞는다.</summary>
    Smooth,
    /// <summary>정지점 색만 쓴다: 가장 가까운 정지점 색의 띠.</summary>
    Steps,
}

/// <summary>부드럽게 섞을 때의 순서 디더링(Bayer). 켜면 이웃한 두 정지점 색만 써서 섞인 비율을 무늬로 낸다.</summary>
public enum GradientDither
{
    None,
    /// <summary>2×2 Bayer(4단계).</summary>
    Bayer2,
    /// <summary>4×4 Bayer(16단계).</summary>
    Bayer4,
}

/// <summary>
/// 선형 그라데이션: 시작점 → 끝점 방향으로 픽셀마다 위치 t(0~1)를 재고(<see cref="Project"/>), 색 정지점에서 색을(<see cref="ColorAt"/>),
/// 불투명도 정지점에서 불투명도를(<see cref="AlphaAt"/>) 골라 곱한다(<see cref="Sample"/>).
/// 칠하기는 픽셀을 그 색으로 바꾼다(알파도 그대로 — 투명 쪽으로 가면 아래가 지워진다).
/// </summary>
public static class Gradient
{
    private static readonly int[] Bayer2Matrix = [0, 2, 3, 1];
    private static readonly int[] Bayer4Matrix = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    /// <summary>
    /// 픽셀 (x,y)의 위치 t: 시작점에서 끝점 쪽으로의 투영 비율(픽셀 중심 기준, 0~1로 자름).
    /// 시작점 앞은 0, 끝점 뒤는 1. 두 점이 같으면 0.
    /// </summary>
    public static double Project(Point start, Point end, int x, int y)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 == 0) return 0;
        double t = ((x - start.X) * dx + (y - start.Y) * dy) / len2;
        return Math.Clamp(t, 0, 1);
    }

    /// <summary>위치 순으로 정렬한 정지점(같은 위치는 원래 순서 유지, 위치는 0~1로 자름).</summary>
    public static List<GradientStop> Sorted(IEnumerable<GradientStop> stops)
        => stops.Select((s, i) => (Stop: s with { Position = Math.Clamp(s.Position, 0, 1) }, Index: i))
            .OrderBy(p => p.Stop.Position).ThenBy(p => p.Index).Select(p => p.Stop).ToList();

    /// <summary>위치 순으로 정렬한 불투명도 정지점(같은 위치는 원래 순서 유지, 위치는 0~1로 자름).</summary>
    public static List<GradientAlphaStop> SortedAlpha(IEnumerable<GradientAlphaStop> stops)
        => stops.Select((s, i) => (Stop: s with { Position = Math.Clamp(s.Position, 0, 1) }, Index: i))
            .OrderBy(p => p.Stop.Position).ThenBy(p => p.Index).Select(p => p.Stop).ToList();

    /// <summary>
    /// 위치 <paramref name="t"/>의 불투명도(0~255). <paramref name="stops"/>는 위치 순(<see cref="SortedAlpha"/>)이어야 하고,
    /// 비었으면 255. 규칙은 <see cref="ColorAt"/>와 같다(단계 = 가까운 정지점, 디더링 = 같은 Bayer 문턱으로 이웃 둘 중 하나).
    /// </summary>
    public static byte AlphaAt(IReadOnlyList<GradientAlphaStop> stops, double t, GradientInterpolation mode = GradientInterpolation.Smooth,
        GradientDither dither = GradientDither.None, int x = 0, int y = 0)
    {
        if (stops.Count == 0) return 255;
        if (t <= stops[0].Position) return stops[0].Alpha;
        if (t >= stops[^1].Position) return stops[^1].Alpha;
        int i = 0;
        while (i + 2 < stops.Count && t >= stops[i + 1].Position) i++;
        var a = stops[i];
        var b = stops[i + 1];
        double span = b.Position - a.Position;
        double u = span <= 0 ? 1 : (t - a.Position) / span;
        if (mode == GradientInterpolation.Steps) return u >= 0.5 ? b.Alpha : a.Alpha;
        if (dither != GradientDither.None) return u > Threshold(dither, x, y) ? b.Alpha : a.Alpha;
        return (byte)Math.Round(a.Alpha + (b.Alpha - a.Alpha) * u);
    }

    /// <summary>위치 <paramref name="t"/>의 최종 색: 색 정지점 색의 알파 × 불투명도 정지점 값(둘 다 위치 순이어야 함).</summary>
    public static Color32 Sample(IReadOnlyList<GradientStop> stops, IReadOnlyList<GradientAlphaStop> alphaStops, double t,
        GradientInterpolation mode = GradientInterpolation.Smooth, GradientDither dither = GradientDither.None, int x = 0, int y = 0)
    {
        var c = ColorAt(stops, t, mode, dither, x, y);
        int alpha = (c.A * AlphaAt(alphaStops, t, mode, dither, x, y) + 127) / 255;
        return alpha == 0 ? Color32.Transparent : new Color32(c.R, c.G, c.B, (byte)alpha);
    }

    /// <summary>
    /// 위치 <paramref name="t"/>의 색. <paramref name="stops"/>는 위치 순(<see cref="Sorted"/>)이어야 한다.
    /// 첫 정지점 앞은 첫 색, 마지막 뒤는 마지막 색.
    /// 단계 모드는 가장 가까운 정지점 색(가운데는 뒤 정지점). 부드럽게 + 디더링이면 (x,y)의 Bayer 문턱으로 이웃한 두 색 중 하나를 고른다.
    /// </summary>
    public static Color32 ColorAt(IReadOnlyList<GradientStop> stops, double t, GradientInterpolation mode = GradientInterpolation.Smooth,
        GradientDither dither = GradientDither.None, int x = 0, int y = 0)
    {
        if (stops.Count == 0) throw new ArgumentException("정지점이 하나 이상 있어야 합니다.", nameof(stops));
        if (t <= stops[0].Position) return stops[0].Color;
        if (t >= stops[^1].Position) return stops[^1].Color;
        int i = 0;
        while (i + 2 < stops.Count && t >= stops[i + 1].Position) i++;
        var a = stops[i];
        var b = stops[i + 1];
        double span = b.Position - a.Position;
        double u = span <= 0 ? 1 : (t - a.Position) / span;
        if (mode == GradientInterpolation.Steps) return u >= 0.5 ? b.Color : a.Color;
        if (dither != GradientDither.None) return u > Threshold(dither, x, y) ? b.Color : a.Color;
        return Lerp(a.Color, b.Color, u);
    }

    /// <summary>Bayer 문턱(0~1 사이, 칸마다 다름). 섞는 비율이 이보다 크면 뒤 색.</summary>
    public static double Threshold(GradientDither dither, int x, int y)
    {
        if (dither == GradientDither.Bayer2)
            return (Bayer2Matrix[(y & 1) * 2 + (x & 1)] + 0.5) / 4;
        return (Bayer4Matrix[(y & 3) * 4 + (x & 3)] + 0.5) / 16;
    }

    /// <summary>
    /// RGBA 선형 섞기(알파 포함). 색은 알파를 곱해 섞으므로 투명 정지점 쪽으로 가도 검게 물들지 않는다.
    /// </summary>
    public static Color32 Lerp(Color32 a, Color32 b, double u)
    {
        u = Math.Clamp(u, 0, 1);
        double aa = a.A / 255.0, ba = b.A / 255.0;
        double alpha = aa + (ba - aa) * u;
        if (alpha <= 0) return Color32.Transparent;
        byte Ch(int ca, int cb) => (byte)Math.Clamp(Math.Round((ca * aa + (cb * ba - ca * aa) * u) / alpha), 0, 255);
        return new Color32(Ch(a.R, b.R), Ch(a.G, b.G), Ch(a.B, b.B), (byte)Math.Round(alpha * 255));
    }

    /// <summary>
    /// <paramref name="image"/>에 <paramref name="start"/> → <paramref name="end"/> 선형 그라데이션을 칠한다(<paramref name="clip"/>이 있으면 그 안만).
    /// <paramref name="alphaStops"/>가 없거나 비었으면 불투명도는 색 정지점 알파 그대로. 두 점이 같으면 아무것도 안 한다. 바뀐 픽셀 수를 돌려준다.
    /// </summary>
    public static int Apply(PixelImage image, Point start, Point end, IEnumerable<GradientStop> stops,
        GradientInterpolation mode = GradientInterpolation.Smooth, GradientDither dither = GradientDither.None, PixelMask? clip = null,
        IEnumerable<GradientAlphaStop>? alphaStops = null)
    {
        if (start == end) return 0;
        var sorted = Sorted(stops);
        if (sorted.Count == 0) throw new ArgumentException("정지점이 하나 이상 있어야 합니다.", nameof(stops));
        var alpha = SortedAlpha(alphaStops ?? []);
        var area = Rectangle.Intersect(clip?.Bounds ?? image.Bounds, image.Bounds);
        int changed = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                if (clip is { IsRectangle: false } c && !c.Contains(x, y)) continue;
                var color = Sample(sorted, alpha, Project(start, end, x, y), mode, dither, x, y);
                if (image.Set(x, y, color)) changed++;
            }
        return changed;
    }
}
