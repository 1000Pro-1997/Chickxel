using Gpixel.Core.Model;

namespace Gpixel.Core.Pixelate;

/// <summary>CIELAB 색(D65, L 0~100).</summary>
public readonly record struct CieLab(double L, double A, double B);

/// <summary>색 거리 계산에 쓸 색 한 점: OKLab와(CIEDE2000이면) CIELAB.</summary>
public readonly record struct ColorPoint(Lab Ok, CieLab Cie);

/// <summary>
/// 팔레트 단계의 색 거리(<see cref="PaletteSettings.ColorDistance"/>). 값은 모두 OKLab 거리와 견줄 수 있는 크기다 —
/// CIEDE2000은 ΔE00 / <see cref="CiedeScale"/>(작은 차이에서 ΔE00/OKLab 비의 중앙값이 색마다 115~336, 평균 약 200 — 채도 높은 색은 OKLab보다 조금 작게,
/// 무채색은 더 크게 잼), OKLCh 색상 가중은 OKLab와 같은 단위(w = 1이면 같음). 그래서 OKLab로 정한 문턱값을 그대로 쓴다.
/// </summary>
public sealed class ColorMetric
{
    /// <summary>ΔE00을 OKLab 크기로 옮기는 나눗수(위 설명).</summary>
    public const double CiedeScale = 200;

    public static readonly ColorMetric OkLab = new(ColorDistanceMode.OkLab, 1);

    public ColorDistanceMode Mode { get; }
    public double HueWeight { get; }
    /// <summary>명암 계열 우선(<see cref="PaletteSettings.PreferShadeFamily"/>): <see cref="Nearest"/>가 같은 색상 계열 팔레트 색을 먼저 본다.</summary>
    public bool ShadeFamily { get; }

    public ColorMetric(ColorDistanceMode mode, double hueWeight, bool shadeFamily = false)
    {
        Mode = mode;
        HueWeight = PixelateRanges.HueWeight.Clamp(hueWeight);
        ShadeFamily = shadeFamily;
    }

    public static ColorMetric For(PaletteSettings s)
        => s.ColorDistance == ColorDistanceMode.OkLab && !s.PreferShadeFamily ? OkLab : new ColorMetric(s.ColorDistance, s.HueWeight, s.PreferShadeFamily);

    public ColorPoint Point(Color32 c)
        => new(Pixelate.OkLab.FromColor(c), Mode == ColorDistanceMode.Ciede2000 ? ColorDistance.ToCieLab(c) : default);

    public ColorPoint Point(in Lab ok)
        => new(ok, Mode == ColorDistanceMode.Ciede2000 ? ColorDistance.ToCieLab(ok) : default);

    public double Distance(in ColorPoint a, in ColorPoint b) => Mode switch
    {
        ColorDistanceMode.Ciede2000 => ColorDistance.Ciede2000(a.Cie, b.Cie) / CiedeScale,
        ColorDistanceMode.OkLchHue => ColorDistance.OkLchHue(a.Ok, b.Ok, HueWeight),
        _ => Lab.Distance(a.Ok, b.Ok),
    };

    public double Distance(Color32 a, Color32 b) => Distance(Point(a), Point(b));

    /// <summary>
    /// 팔레트 점 중 가장 가까운 번호(같으면 앞 번호). 명암 계열 우선(<see cref="ShadeFamily"/>)이면, 가장 가까운 색이 다른 색상의 유채색(색상 차 &gt;
    /// <see cref="ColorDistance.ShadeMaxHue"/>)일 때 <paramref name="p"/>와 같은 계열(<see cref="ColorDistance.SameShadeFamily"/>)인 팔레트 색 중 밝기가
    /// 가장 가까운 색(밝기 차 ≤ <see cref="ColorDistance.ShadeMaxLightness"/>, 같으면 거리가 가까운 색) — 진한 살구색 그늘이 밝기가 비슷한 금색이 아니라 살구색으로.
    /// </summary>
    public int Nearest(in ColorPoint p, ReadOnlySpan<ColorPoint> palette)
    {
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            double d = Distance(p, palette[i]);
            if (d < bestD) { bestD = d; best = i; }
        }
        if (!ShadeFamily || palette.Length == 0) return best;
        var near = palette[best].Ok;
        if (ColorDistance.Chroma(p.Ok) < ColorDistance.ShadeMinChroma || ColorDistance.Chroma(near) < ColorDistance.ShadeMinChroma
            || ColorDistance.HueDifference(p.Ok, near) <= ColorDistance.ShadeMaxHue)
            return best;
        int family = -1;
        double familyL = double.MaxValue, familyD = double.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            if (!ColorDistance.SameShadeFamily(p.Ok, palette[i].Ok)) continue;
            double dl = Math.Abs(palette[i].Ok.L - p.Ok.L);
            if (dl > ColorDistance.ShadeMaxLightness) continue;
            double d = Distance(p, palette[i]);
            if (dl < familyL - 1e-9 || (Math.Abs(dl - familyL) <= 1e-9 && d < familyD)) (family, familyL, familyD) = (i, dl, d);
        }
        return family >= 0 ? family : best;
    }
}

/// <summary>색 거리 공식: CIEDE2000(Sharma·Wu·Dalal 2005), OKLCh 색상 가중 거리, sRGB → CIELAB(D65).</summary>
public static class ColorDistance
{
    private static readonly double[] Linear = BuildLinear();

    private static double[] BuildLinear()
    {
        var t = new double[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            t[i] = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return t;
    }

    public static CieLab ToCieLab(Color32 c) => FromLinear(Linear[c.R], Linear[c.G], Linear[c.B]);

    /// <summary>OKLab → 선형 sRGB(범위 밖도 자르지 않음) → CIELAB — 평균 색처럼 바이트가 아닌 색.</summary>
    public static CieLab ToCieLab(in Lab lab)
    {
        double l = lab.L + 0.3963377774 * lab.A + 0.2158037573 * lab.B;
        double m = lab.L - 0.1055613458 * lab.A - 0.0638541728 * lab.B;
        double s = lab.L - 0.0894841775 * lab.A - 1.2914855480 * lab.B;
        l = l * l * l; m = m * m * m; s = s * s * s;
        return FromLinear(
            4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }

    private static CieLab FromLinear(double r, double g, double b)
    {
        // sRGB(D65) → XYZ, D65 기준 백색으로 나눔.
        double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
        double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;
        double fx = F(x), fy = F(y), fz = F(z);
        return new CieLab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));

        static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
    }

    /// <summary>CIEDE2000 색차 ΔE00(kL = kC = kH = 1).</summary>
    public static double Ciede2000(in CieLab x, in CieLab y)
    {
        const double Pow25To7 = 6103515625.0;
        double c1 = Math.Sqrt(x.A * x.A + x.B * x.B), c2 = Math.Sqrt(y.A * y.A + y.B * y.B);
        double cMean = (c1 + c2) / 2, cMean7 = Math.Pow(cMean, 7);
        double g = 0.5 * (1 - Math.Sqrt(cMean7 / (cMean7 + Pow25To7)));
        double a1 = (1 + g) * x.A, a2 = (1 + g) * y.A;
        double c1p = Math.Sqrt(a1 * a1 + x.B * x.B), c2p = Math.Sqrt(a2 * a2 + y.B * y.B);
        double h1 = Hue(x.B, a1), h2 = Hue(y.B, a2);

        double dL = y.L - x.L, dC = c2p - c1p;
        double dh = 0;
        if (c1p * c2p != 0)
        {
            dh = h2 - h1;
            if (dh > 180) dh -= 360;
            else if (dh < -180) dh += 360;
        }
        double dH = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(Rad(dh / 2));

        double lMean = (x.L + y.L) / 2, cpMean = (c1p + c2p) / 2;
        double hMean = h1 + h2;
        if (c1p * c2p != 0)
        {
            if (Math.Abs(h1 - h2) <= 180) hMean /= 2;
            else hMean = hMean < 360 ? (hMean + 360) / 2 : (hMean - 360) / 2;
        }
        double t = 1 - 0.17 * Math.Cos(Rad(hMean - 30)) + 0.24 * Math.Cos(Rad(2 * hMean))
            + 0.32 * Math.Cos(Rad(3 * hMean + 6)) - 0.20 * Math.Cos(Rad(4 * hMean - 63));
        double dTheta = 30 * Math.Exp(-Math.Pow((hMean - 275) / 25, 2));
        double cpMean7 = Math.Pow(cpMean, 7);
        double rc = 2 * Math.Sqrt(cpMean7 / (cpMean7 + Pow25To7));
        double l50 = (lMean - 50) * (lMean - 50);
        double sl = 1 + 0.015 * l50 / Math.Sqrt(20 + l50);
        double sc = 1 + 0.045 * cpMean;
        double sh = 1 + 0.015 * cpMean * t;
        double rt = -Math.Sin(Rad(2 * dTheta)) * rc;
        double tl = dL / sl, tc = dC / sc, th = dH / sh;
        return Math.Sqrt(tl * tl + tc * tc + th * th + rt * tc * th);

        static double Hue(double b, double a)
        {
            if (a == 0 && b == 0) return 0;
            double h = Math.Atan2(b, a) * 180 / Math.PI;
            return h < 0 ? h + 360 : h;
        }
        static double Rad(double d) => d * Math.PI / 180;
    }

    /// <summary>
    /// OKLCh 색상 가중 거리 √(ΔL² + ΔC² + (w·ΔH)²): ΔH는 채도를 곱한 색상 차(ΔH² = Δa² + Δb² − ΔC²) — w = 1이면 OKLab 거리와 같다.
    /// </summary>
    public static double OkLchHue(in Lab x, in Lab y, double hueWeight)
    {
        double dl = x.L - y.L, da = x.A - y.A, db = x.B - y.B;
        double c1 = Math.Sqrt(x.A * x.A + x.B * x.B), c2 = Math.Sqrt(y.A * y.A + y.B * y.B);
        double dc = c1 - c2;
        double dh2 = Math.Max(0, da * da + db * db - dc * dc);
        return Math.Sqrt(dl * dl + dc * dc + hueWeight * hueWeight * dh2);
    }

    /// <summary>OKLCh 채도.</summary>
    public static double Chroma(in Lab c) => Math.Sqrt(c.A * c.A + c.B * c.B);

    /// <summary>두 OKLab 색의 색상각 차(도, 0~180).</summary>
    public static double HueDifference(in Lab x, in Lab y)
    {
        double d = Math.Abs(Math.Atan2(x.B, x.A) - Math.Atan2(y.B, y.A)) * 180 / Math.PI;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>명암 계열 우선: 계열을 따질 최소 채도(OKLCh C — 이보다 흐린 색은 무채색으로 봄).</summary>
    public const double ShadeMinChroma = 0.04;
    /// <summary>명암 계열 우선: 같은 계열로 볼 최대 색상 차(도 — 살구색과 금색은 약 35도).</summary>
    public const double ShadeMaxHue = 20;
    /// <summary>명암 계열 우선: 계열 색으로 바꿀 최대 밝기 차(OKLab L — 깊은 그늘을 밝은 빛 색으로 보내지 않음).</summary>
    public const double ShadeMaxLightness = 0.2;

    /// <summary>
    /// 같은 명암 계열: 둘 다 채도 ≥ <see cref="ShadeMinChroma"/>, 색상 차 ≤ <see cref="ShadeMaxHue"/>, 채도 비 ½~2(밝기는 보지 않음 — 같은 색의 밝고 어두운 칠).
    /// </summary>
    public static bool SameShadeFamily(in Lab x, in Lab y)
    {
        double cx = Chroma(x), cy = Chroma(y);
        return cx >= ShadeMinChroma && cy >= ShadeMinChroma && cy >= cx / 2 && cy <= cx * 2 && HueDifference(x, y) <= ShadeMaxHue;
    }

    /// <summary>
    /// 명암 계열 우선의 디더 짝: 원래 색 <paramref name="source"/>에 섞어도 되는 팔레트 색 — 둘 중 하나가 무채색(채도 &lt; <see cref="ShadeMinChroma"/>,
    /// 밝기만 바뀜)이거나 색상 차 ≤ <see cref="ShadeMaxHue"/>. 살구색 그늘에 금색 점을 섞지 않는다.
    /// </summary>
    public static bool CanDither(in Lab source, in Lab partner)
        => Chroma(source) < ShadeMinChroma || Chroma(partner) < ShadeMinChroma || HueDifference(source, partner) <= ShadeMaxHue;
}
