using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.Core.Edit;

/// <summary>
/// 채우기·스마트 선택이 허용 오차를 재는 색 비교 방식(<see cref="FillColorMatcher"/>). 값은 설정에 숫자로 저장되므로 순서를 바꾸지 않는다.
/// </summary>
public enum FillColorMode
{
    /// <summary>RGBA 채널 차이의 최댓값(기본 — 예전부터의 방식).</summary>
    ChannelMax,
    /// <summary>RGB 직선(유클리드) 거리.</summary>
    RgbDistance,
    /// <summary>OKLab 직선 거리(눈에 고른 차이).</summary>
    OkLab,
    /// <summary>CIEDE2000 색차 ΔE00(CIELAB D65).</summary>
    Ciede2000,
    /// <summary>OKLCh 색상각만(밝기·채도는 보지 않음).</summary>
    Hue,
}

/// <summary>
/// 채우기·스마트 선택의 색 비교: 씨앗 색 하나와 허용 오차 t(0~255, 비율 f = t/255 — 0% = 0, 100% = 255)로 칸 색이 맞는지.
/// <para>
/// 모든 방식 공통: t ≥ 255면 모든 색(투명 포함), 같은 ARGB면 늘 맞음. 한쪽이 투명(알파 0)이면 RGB가 없는 것으로 보아 알파 차이 ≤ t만 잰다
/// (둘 다 투명이면 같은 색). 둘 다 보이면 알파 차이 ≤ t이고 아래 색 차이도 문턱 안이어야 한다. t = 0이면 정확히 같은 색만.
/// </para>
/// <para>
/// 색 차이 문턱(f를 방식마다 sRGB 안 최대 차이에 곱함 — 0 = 같은 색, 100% = 모든 색):
/// 채널 최대 = max|ΔR|,|ΔG|,|ΔB| ≤ t. RGB 거리 = √(ΔR²+ΔG²+ΔB²) ≤ t·√3(최대 255√3). OKLab = 거리 ≤ f(검정–흰색이 1로 최대).
/// CIEDE2000 = ΔE00 ≤ f × <see cref="CiedeMax"/>. 색상 = OKLCh 색상각 차 ≤ f × 180도 — 채도 &lt; <see cref="ColorDistance.ShadeMinChroma"/>인
/// 무채색끼리는 같은 색상으로, 무채색과 유채색은 가장 먼 것으로 본다.
/// </para>
/// 채널 최대·RGB 거리 밖의 방식은 칸마다 색 변환이 들므로 본 색의 결과를 기억한다(도트 그림은 색 수가 적다).
/// </summary>
public sealed class FillColorMatcher
{
    /// <summary>OKLab 최대 거리(sRGB 안 — 검정–흰색 1.0).</summary>
    public const double OkLabMax = 1.0;
    /// <summary>CIEDE2000 최대 색차(sRGB 안에서 잰 최댓값 약 119.2 — 짙은 파랑–연두).</summary>
    public const double CiedeMax = 120;
    /// <summary>색상 모드의 최대 색상각 차(도).</summary>
    public const double HueMax = 180;

    private readonly Color32 _seed;
    private readonly int _tolerance;
    private readonly FillColorMode _mode;
    /// <summary>방식 단위의 색 차이 문턱(채널 최대·RGB 거리는 정수로 따로 잼).</summary>
    private readonly double _limit;
    private readonly Lab _seedOk;
    private readonly CieLab _seedCie;
    private readonly bool _seedGray;
    private readonly Dictionary<uint, bool>? _cache;

    public FillColorMatcher(Color32 seed, int tolerance, FillColorMode mode = FillColorMode.ChannelMax)
    {
        _seed = seed;
        _tolerance = Math.Clamp(tolerance, 0, 255);
        _mode = Enum.IsDefined(mode) ? mode : FillColorMode.ChannelMax;
        double f = _tolerance / 255.0;
        _limit = _mode switch
        {
            FillColorMode.OkLab => f * OkLabMax,
            FillColorMode.Ciede2000 => f * CiedeMax,
            FillColorMode.Hue => f * HueMax,
            _ => 0,
        };
        if (_mode is FillColorMode.OkLab or FillColorMode.Ciede2000 or FillColorMode.Hue)
        {
            _seedOk = Pixelate.OkLab.FromColor(seed);
            if (_mode == FillColorMode.Ciede2000) _seedCie = ColorDistance.ToCieLab(seed);
            _seedGray = ColorDistance.Chroma(_seedOk) < ColorDistance.ShadeMinChroma;
            if (_tolerance is > 0 and < 255) _cache = new();
        }
    }

    public bool Matches(Color32 c)
    {
        if (_tolerance >= 255 || c.Argb == _seed.Argb) return true;
        int da = Math.Abs(c.A - _seed.A);
        if (da > _tolerance) return false;
        if (c.A == 0 || _seed.A == 0) return true;
        if (_tolerance == 0) return false;
        int dr = c.R - _seed.R, dg = c.G - _seed.G, db = c.B - _seed.B;
        switch (_mode)
        {
            case FillColorMode.ChannelMax:
                return Math.Abs(dr) <= _tolerance && Math.Abs(dg) <= _tolerance && Math.Abs(db) <= _tolerance;
            case FillColorMode.RgbDistance:
                // √(ΔR²+ΔG²+ΔB²) ≤ t·√3 ⇔ 제곱합 ≤ 3t²
                return dr * dr + dg * dg + db * db <= 3 * _tolerance * _tolerance;
        }
        // 알파만 다른 같은 RGB는 색 차이 0
        uint rgb = c.Argb & 0xFFFFFFu;
        if (rgb == (_seed.Argb & 0xFFFFFFu)) return true;
        if (_cache!.TryGetValue(rgb, out bool hit)) return hit;
        bool ok = ColorMatches(c);
        _cache[rgb] = ok;
        return ok;
    }

    /// <summary>OKLab·CIEDE2000·색상 방식의 색 차이(알파 제외) ≤ 문턱.</summary>
    private bool ColorMatches(Color32 c)
    {
        var ok = Pixelate.OkLab.FromColor(c);
        switch (_mode)
        {
            case FillColorMode.OkLab:
                return Lab.Distance(ok, _seedOk) <= _limit + 1e-6;
            case FillColorMode.Ciede2000:
                return ColorDistance.Ciede2000(ColorDistance.ToCieLab(c), _seedCie) <= _limit + 1e-9;
            default:
                bool gray = ColorDistance.Chroma(ok) < ColorDistance.ShadeMinChroma;
                if (gray || _seedGray) return gray && _seedGray;
                return ColorDistance.HueDifference(ok, _seedOk) <= _limit + 1e-9;
        }
    }
}
