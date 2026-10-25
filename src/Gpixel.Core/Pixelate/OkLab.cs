using Gpixel.Core.Model;

namespace Gpixel.Core.Pixelate;

/// <summary>OKLab 색(L 0~1, a·b 대략 −0.4~0.4). 사람 눈에 고른 거리 — 색 줄이기·점수에 쓴다.</summary>
public readonly record struct Lab(float L, float A, float B)
{
    public static float Distance(in Lab x, in Lab y) => MathF.Sqrt(DistanceSquared(x, y));

    public static float DistanceSquared(in Lab x, in Lab y)
    {
        float dl = x.L - y.L, da = x.A - y.A, db = x.B - y.B;
        return dl * dl + da * da + db * db;
    }
}

/// <summary>sRGB ↔ OKLab 변환(Björn Ottosson, 2020).</summary>
public static class OkLab
{
    private static readonly float[] Linear = BuildLinear();

    private static float[] BuildLinear()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            t[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
        return t;
    }

    public static Lab FromRgb(int r, int g, int b)
    {
        float lr = Linear[r], lg = Linear[g], lb = Linear[b];
        float l = MathF.Cbrt(0.4122214708f * lr + 0.5363325363f * lg + 0.0514459929f * lb);
        float m = MathF.Cbrt(0.2119034982f * lr + 0.6806995451f * lg + 0.1073969566f * lb);
        float s = MathF.Cbrt(0.0883024619f * lr + 0.2817188376f * lg + 0.6299787005f * lb);
        return new Lab(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    public static Lab FromColor(Color32 c) => FromRgb(c.R, c.G, c.B);

    /// <summary>불투명 sRGB 색(범위 밖은 자름).</summary>
    public static Color32 ToColor(in Lab lab)
    {
        var (r, g, b) = LinearRgb(lab);
        return new Color32(ToByte(r), ToByte(g), ToByte(b));
    }

    /// <summary>
    /// 밝기만 옮긴 색(알파 그대로): <paramref name="amount"/> −1~1, 음수 = 검정 쪽(L × (1 + amount)), 양수 = 흰색 쪽(L + (1 − L) × amount).
    /// 색상(a·b 방향)은 그대로 두고, 새 밝기에서 sRGB 밖이면 채도만 범위 안까지 줄인다.
    /// </summary>
    public static Color32 ShiftLightness(Color32 c, float amount)
    {
        if (amount == 0) return c;
        var lab = FromColor(c);
        float l = Math.Clamp(amount < 0 ? lab.L * (1 + amount) : lab.L + (1 - lab.L) * amount, 0f, 1f);
        float keep = 1;
        if (!InGamut(new Lab(l, lab.A, lab.B)))
        {
            float lo = 0, hi = 1;
            for (int k = 0; k < 16; k++)
            {
                float mid = (lo + hi) / 2;
                if (InGamut(new Lab(l, lab.A * mid, lab.B * mid))) lo = mid;
                else hi = mid;
            }
            keep = lo;
        }
        return ToColor(new Lab(l, lab.A * keep, lab.B * keep)).WithAlpha(c.A);
    }

    private static bool InGamut(in Lab lab)
    {
        const float eps = 1e-4f;
        var (r, g, b) = LinearRgb(lab);
        return r is >= -eps and <= 1 + eps && g is >= -eps and <= 1 + eps && b is >= -eps and <= 1 + eps;
    }

    private static (float R, float G, float B) LinearRgb(in Lab lab)
    {
        float l = lab.L + 0.3963377774f * lab.A + 0.2158037573f * lab.B;
        float m = lab.L - 0.1055613458f * lab.A - 0.0638541728f * lab.B;
        float s = lab.L - 0.0894841775f * lab.A - 1.2914855480f * lab.B;
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;
        return (4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
            -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
            -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
    }

    private static byte ToByte(float linear)
    {
        double c = Math.Clamp(linear, 0f, 1f);
        double v = c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
    }
}
