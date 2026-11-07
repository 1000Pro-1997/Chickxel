using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.Edit;

/// <summary>밝게/어둡게 도구가 색을 바꾸는 방식. 설정에 정수로 저장되므로 순서를 바꾸지 않는다.</summary>
public enum DodgeMode
{
    /// <summary>OKLab 밝기를 옮기면서 색상을 밝게는 따뜻한 노랑 쪽, 어둡게는 차가운 파랑 쪽으로 돌린다(픽셀 아트 명암). 기본.</summary>
    HueShift,
    /// <summary>OKLab 밝기만 옮긴다(색상 유지, sRGB 밖이면 채도만 줄임).</summary>
    Perceptual,
    /// <summary>HSV 명도(V)를 더하거나 뺀다. 가장 밝으면 채도를 내린다(예전 방식).</summary>
    Value,
    /// <summary>sRGB 채널마다 흰색이나 검정과 섞는다(틴트·셰이드).</summary>
    TintShade,
    /// <summary>OKLab 밝기와 색상은 두고 채도만: 밝게 = 진하게, 어둡게 = 흐리게.</summary>
    Saturation,
}

/// <summary>밝게/어둡게 한 번의 색 변화.</summary>
public static class DodgeBurn
{
    public const DodgeMode DefaultMode = DodgeMode.HueShift;

    /// <summary>색상 이동이 향하는 OKLCH 색상각: 밝게 = 금빛 노랑, 어둡게 = 파랑·보라.</summary>
    private const float WarmHue = 85f * MathF.PI / 180f, CoolHue = 275f * MathF.PI / 180f;
    /// <summary>세기 100%일 때 한 번에 돌리는 최대 색상각(세기에 비례).</summary>
    private const float HueStepAtFull = 60f * MathF.PI / 180f;
    /// <summary>이보다 채도가 낮으면 무채색으로 보고 색상을 돌리지 않는다.</summary>
    private const float GrayChroma = 0.02f;

    /// <summary>
    /// <paramref name="c"/>를 <paramref name="amount"/>(0~1, 0.1 = 10%)만큼 밝게(<paramref name="lighten"/>) 또는 어둡게 바꾼 색.
    /// 투명은 그대로이고 알파는 유지한다.
    /// </summary>
    public static Color32 Apply(Color32 c, bool lighten, float amount, DodgeMode mode)
    {
        if (c.A == 0 || amount <= 0) return c;
        amount = Math.Min(amount, 1f);
        return mode switch
        {
            DodgeMode.Perceptual => OkLab.ShiftLightness(c, lighten ? amount : -amount),
            DodgeMode.Value => ShiftValue(c, lighten, amount),
            DodgeMode.TintShade => TintShade(c, lighten, amount),
            DodgeMode.Saturation => ScaleChroma(c, lighten ? 1 + amount : 1 - amount),
            _ => HueShift(c, lighten, amount),
        };
    }

    private static Color32 HueShift(Color32 c, bool lighten, float amount)
    {
        var lab = OkLab.FromColor(c);
        float l = Math.Clamp(lighten ? lab.L + (1 - lab.L) * amount : lab.L * (1 - amount), 0f, 1f);
        float a = lab.A, b = lab.B, chroma = MathF.Sqrt(a * a + b * b);
        if (chroma >= GrayChroma)
        {
            float hue = MathF.Atan2(b, a);
            float diff = lighten ? WarmHue - hue : CoolHue - hue;
            diff = MathF.IEEERemainder(diff, 2 * MathF.PI);
            float step = HueStepAtFull * amount;
            hue += Math.Clamp(diff, -step, step);
            a = chroma * MathF.Cos(hue);
            b = chroma * MathF.Sin(hue);
        }
        return OkLab.ToGamutColor(new Lab(l, a, b)).WithAlpha(c.A);
    }

    private static Color32 ShiftValue(Color32 c, bool lighten, float amount)
    {
        var (h, s, v) = c.ToHsv();
        if (lighten)
        {
            if (v >= 0.999f) s = Math.Max(0, s - amount);
            else v = Math.Min(1, v + amount);
        }
        else v = Math.Max(0, v - amount);
        return Color32.FromHsv(h, s, v, c.A);
    }

    private static Color32 TintShade(Color32 c, bool lighten, float amount)
    {
        byte Mix(byte ch) => (byte)Math.Clamp((int)MathF.Round(lighten ? ch + (255 - ch) * amount : ch * (1 - amount)), 0, 255);
        return new Color32(Mix(c.R), Mix(c.G), Mix(c.B), c.A);
    }

    private static Color32 ScaleChroma(Color32 c, float factor)
    {
        var lab = OkLab.FromColor(c);
        if (MathF.Sqrt(lab.A * lab.A + lab.B * lab.B) < GrayChroma) return c;
        return OkLab.ToGamutColor(new Lab(lab.L, lab.A * factor, lab.B * factor)).WithAlpha(c.A);
    }
}
