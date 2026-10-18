using System.Globalization;
using System.Runtime.InteropServices;

namespace Gpixel.Core.Model;

/// <summary>
/// 8비트 RGBA 색. 내부 값은 0xAARRGGBB 로 묶여 있어 메모리 배치(B,G,R,A)가
/// GDI+ <c>Format32bppArgb</c>와 같다. 따라서 <see cref="PixelImage.Pixels"/>를 그대로 비트맵에 복사할 수 있다.
/// 알파는 미리 곱하지 않은(straight) 값이다.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct Color32 : IEquatable<Color32>
{
    /// <summary>0xAARRGGBB 값.</summary>
    public readonly uint Argb;

    public Color32(uint argb) => Argb = argb;

    public Color32(byte r, byte g, byte b, byte a = 255)
        => Argb = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    public byte A => (byte)(Argb >> 24);
    public byte R => (byte)(Argb >> 16);
    public byte G => (byte)(Argb >> 8);
    public byte B => (byte)Argb;

    /// <summary>완전 투명(알파 0).</summary>
    public bool IsTransparent => A == 0;

    public static Color32 Transparent => default;
    public static Color32 Black => new(0, 0, 0);
    public static Color32 White => new(255, 255, 255);

    public Color32 WithAlpha(byte a) => new((Argb & 0x00FFFFFFu) | ((uint)a << 24));

    /// <summary>
    /// 눈에 보이는 색이 같은지 비교한다. 알파가 0이면 RGB와 관계없이 모두 같은 "투명"으로 본다.
    /// 채우기·자동 잘라내기에서 사용한다.
    /// </summary>
    public static bool SameVisible(Color32 a, Color32 b)
        => a.Argb == b.Argb || (a.A == 0 && b.A == 0);

    /// <summary>
    /// <paramref name="src"/>를 이 색 위에 알파 합성(source-over)한 결과.
    /// </summary>
    public Color32 Over(Color32 src) => BlendOver(this, src);

    /// <summary>straight 알파 기준 source-over 합성.</summary>
    public static Color32 BlendOver(Color32 dst, Color32 src)
    {
        if (src.A == 255 || dst.A == 0) return src;
        if (src.A == 0) return dst;
        float sa = src.A / 255f, da = dst.A / 255f;
        float oa = sa + da * (1 - sa);
        byte Mix(byte s, byte d) => (byte)Math.Clamp(MathF.Round((s * sa + d * da * (1 - sa)) / oa), 0, 255);
        return new Color32(Mix(src.R, dst.R), Mix(src.G, dst.G), Mix(src.B, dst.B),
            (byte)Math.Clamp(MathF.Round(oa * 255f), 0, 255));
    }

    /// <summary>"#RRGGBB" 또는 알파가 255가 아니면 "#RRGGBBAA".</summary>
    public string ToHex() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    /// <summary>"RRGGBB", "#RRGGBB", "RRGGBBAA", "#RGB" 형식을 읽는다.</summary>
    public static bool TryParseHex(string? text, out Color32 color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (s.Length == 3 || s.Length == 4)
            s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 && s.Length != 8) return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = s.Length == 6
            ? new Color32(0xFF000000u | v)
            : new Color32(((v & 0xFFu) << 24) | (v >> 8)); // RRGGBBAA → AARRGGBB
        return true;
    }

    /// <summary>HSV(색상 0~360, 채도·명도 0~1)로 변환한다.</summary>
    public (float H, float S, float V) ToHsv()
    {
        float r = R / 255f, g = G / 255f, b = B / 255f;
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b));
        float d = max - min;
        float h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
            if (h < 0) h += 360;
        }
        return (h, max == 0 ? 0 : d / max, max);
    }

    /// <summary>HSV(색상 0~360, 채도·명도 0~1)에서 색을 만든다.</summary>
    public static Color32 FromHsv(float h, float s, float v, byte a = 255)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        float c = v * s, x = c * (1 - MathF.Abs((h / 60) % 2 - 1)), m = v - c;
        (float r, float g, float b) = (int)(h / 60) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        byte To(float f) => (byte)Math.Clamp(MathF.Round((f + m) * 255), 0, 255);
        return new Color32(To(r), To(g), To(b), a);
    }

    public bool Equals(Color32 other) => Argb == other.Argb;
    public override bool Equals(object? obj) => obj is Color32 c && Equals(c);
    public override int GetHashCode() => (int)Argb;
    public static bool operator ==(Color32 a, Color32 b) => a.Argb == b.Argb;
    public static bool operator !=(Color32 a, Color32 b) => a.Argb != b.Argb;
    public override string ToString() => ToHex();
}
