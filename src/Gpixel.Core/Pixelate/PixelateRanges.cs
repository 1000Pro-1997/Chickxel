namespace Gpixel.Core.Pixelate;

/// <summary>값의 허용 범위 [<see cref="Min"/>, <see cref="Max"/>].</summary>
public readonly record struct PixelateRange(double Min, double Max)
{
    public double Clamp(double v) => Math.Clamp(v, Min, Max);

    public bool Contains(double v) => v >= Min && v <= Max;

    /// <summary>[<paramref name="lo"/>, <paramref name="hi"/>]를 이 범위 안으로 줄인다(늘 lo ≤ hi — 둘 다 밖이면 가까운 끝 한 점).</summary>
    public (double Lo, double Hi) Bound(double lo, double hi)
    {
        lo = Clamp(lo);
        return (lo, Math.Max(lo, Clamp(hi)));
    }
}

/// <summary>
/// 자동 최적화가 바꾸는 숫자 설정의 허용 범위 — 정리(<see cref="Pixelator"/>)의 자르기, 자동 최적화의 찾기 범위, 대화상자 숫자 칸의 최소·최대가 모두 이것을 쓴다.
/// 비율 값(<see cref="BorderFraction"/>·<see cref="AlphaThreshold"/>)은 0~1(화면에서는 %).
/// </summary>
public static class PixelateRanges
{
    /// <summary>수동 칸 크기(원본 px).</summary>
    public static readonly PixelateRange Scale = new(1, 256);
    /// <summary>자동 칸 크기에 더할 값(원본 px) — 찾은 칸 크기가 무엇이든 수동 칸 크기 범위(1~256) 어디에나 닿게.</summary>
    public static readonly PixelateRange ScaleAdjust = new(-255, 255);
    /// <summary>격자 시작 위치 X·Y(원본 px).</summary>
    public static readonly PixelateRange Offset = new(-256, 256);
    /// <summary>배율 크기의 배율.</summary>
    public static readonly PixelateRange Factor = new(0.01, 1);
    /// <summary>칸 경계 지역 보정: 경계가 전체 자리에서 움직일 수 있는 거리(칸 크기 비율).</summary>
    public static readonly PixelateRange LocalShift = new(0.05, 0.99);
    /// <summary>칸 경계 지역 보정: 띠 너비(칸).</summary>
    public static readonly PixelateRange LocalBand = new(1, 32);
    /// <summary>지역 보정을 적용할 최소 평균 칸 크기(원본 px).</summary>
    public static readonly PixelateRange LocalMinCell = new(1, 16);
    /// <summary>팔레트 색 수(0 = 자동은 따로).</summary>
    public static readonly PixelateRange Colors = new(2, PaletteBuilder.MaxColors);
    /// <summary>칸 가장자리에서 뺄 비율.</summary>
    public static readonly PixelateRange BorderFraction = new(0, 0.45);
    /// <summary>칸을 칠할 최소 불투명 비율.</summary>
    public static readonly PixelateRange AlphaThreshold = new(0, 1);
    /// <summary>최빈 색 묶음 간격.</summary>
    public static readonly PixelateRange SnapStep = new(1, 128);
    /// <summary>외곽선으로 볼 밝기(OKLab L).</summary>
    public static readonly PixelateRange OutlineDarkness = new(0, 1);
    /// <summary>원본 색에서 가장자리·섞인 칸 무게.</summary>
    public static readonly PixelateRange EdgeWeight = new(0, 1);
    /// <summary>OKLCh 가중 거리의 무게(색상 H, 점수 기준에서는 밝기 L·채도 C도) — 최대값이면 그 차가 나머지를 사실상 압도한다(색상이면 같은 색상 계열을 먼저 고름).</summary>
    public static readonly PixelateRange HueWeight = new(0, 1000);
    /// <summary>다른 색상 지키기: 최소 채도(OKLCh C).</summary>
    public static readonly PixelateRange DistinctMinChroma = new(0.01, 0.2);
    /// <summary>다른 색상 지키기: 최소 색상 차(도).</summary>
    public static readonly PixelateRange DistinctMinHue = new(5, 90);
    /// <summary>다른 색상 지키기: 최소 넓이(칸).</summary>
    public static readonly PixelateRange DistinctMinArea = new(1, 200);
    /// <summary>OKLCh 묶기: 최대 밝기 차(OKLCh L).</summary>
    public static readonly PixelateRange OkLchMaxL = new(0.005, 0.5);
    /// <summary>OKLCh 묶기: 최대 채도 차(OKLCh C).</summary>
    public static readonly PixelateRange OkLchMaxC = new(0.005, 0.4);
    /// <summary>OKLCh 묶기: 최대 색상 차(도).</summary>
    public static readonly PixelateRange OkLchMaxH = new(0.5, 180);
    /// <summary>벡터로 다시 그리기의 단순화 허용 오차(결과 px).</summary>
    public static readonly PixelateRange VectorTolerance = new(0, 3);
    /// <summary>벡터로 다시 그리기에서 그대로 둘 작은 안쪽 덩어리 넓이(칸).</summary>
    public static readonly PixelateRange VectorBlobCells = new(0, 100);
}
