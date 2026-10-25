using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.Core.Pixelate;

/// <summary>배경 지우기 방식.</summary>
public enum BackgroundMode
{
    /// <summary>지우지 않는다(이미 투명한 픽셀만 투명).</summary>
    None,
    /// <summary>테두리에서 가장 많은 색(테두리가 대부분 투명이면 기존 알파)을 배경으로 본다.</summary>
    Auto,
    /// <summary><see cref="BackgroundSettings.Color"/>을 배경으로 본다.</summary>
    PickColor,
}

/// <summary>프레임 나누기 방식.</summary>
public enum FrameSplitMode
{
    /// <summary>그림 전체가 한 프레임.</summary>
    Single,
    /// <summary>배경을 지운 뒤 완전히 빈 열·행으로 나누고, 가까운 작은 조각(떨어진 효과 등)은 가까운 프레임에 합친다.</summary>
    Auto,
    /// <summary>가로 <see cref="FrameSettings.Columns"/> × 세로 <see cref="FrameSettings.Rows"/> 칸.</summary>
    Grid,
}

/// <summary>뽑은 프레임을 공통 캔버스에 놓는 기준.</summary>
public enum FrameAlign
{
    BottomCenter,
    Center,
    /// <summary>나눈 칸(자동이면 조각 띠, 격자면 칸) 안의 원래 위치를 그대로.</summary>
    KeepOffsets,
}

/// <summary>픽셀 격자(한 칸 크기·시작 위치)를 정하는 방식.</summary>
public enum GridMode
{
    /// <summary>경계(색 바뀜) 간격을 분석해 칸 크기와 위치를 찾는다. 칸 크기가 조금씩 흔들려도(예: 7~9px) 경계를 따라간다.</summary>
    Auto,
    /// <summary><see cref="PixelGridSettings.Scale"/>·<see cref="PixelGridSettings.OffsetX"/>·<see cref="PixelGridSettings.OffsetY"/>로 고른 격자.</summary>
    Manual,
}

/// <summary>결과 크기를 정하는 방식.</summary>
public enum OutputSizeMode
{
    /// <summary>찾은(또는 고른) 칸 크기 그대로 — 칸 하나가 결과 픽셀 하나.</summary>
    FromScale,
    /// <summary>고정 크기(W×H) 안에 스프라이트를 여백을 두고 맞춘다(필요하면 더 크게 묶음).</summary>
    FixedSize,
    /// <summary>원본 크기 × 배율(예: 0.125).</summary>
    ScaleFactor,
}

/// <summary>칸 하나의 색을 고르는 방식.</summary>
public enum DownsampleMode
{
    /// <summary>가벼운 색 묶기 뒤 가장 많은 색(묶음 안 평균).</summary>
    Mode,
    /// <summary>채널별 중앙값.</summary>
    Median,
    /// <summary>칸 가운데 픽셀.</summary>
    Center,
    /// <summary>평균.</summary>
    Mean,
}

/// <summary>팔레트(색 줄이기) 방식.</summary>
public enum PaletteMethod
{
    /// <summary>OKLab 공간 k-평균.</summary>
    KMeans,
    /// <summary>RGB 중앙 분할.</summary>
    MedianCut,
    /// <summary>고른 기존 팔레트(<see cref="PaletteSettings.ExistingColors"/>).</summary>
    Existing,
    /// <summary>색을 줄이지 않는다.</summary>
    KeepOriginal,
}

/// <summary>만든 팔레트(k-평균·중앙 분할)의 색을 무엇으로 정하는지.</summary>
public enum PaletteColorSource
{
    /// <summary>묶음마다 실제 칸 색 중 가장 많이 쓰인 색(번진 중간 색에 끌리지 않음). 묶을 때 가장자리·섞인 칸은 <see cref="PaletteSettings.EdgeWeight"/>만큼 덜 센다.</summary>
    Original,
    /// <summary>묶음 평균 색.</summary>
    Mean,
}

/// <summary>팔레트 단계의 색 거리(칸 → 팔레트 색, 고정 색, 색 빼기, 원본 색 고르기, '주요 색 변화' 점수 — <see cref="ColorMetric"/>).</summary>
public enum ColorDistanceMode
{
    /// <summary>OKLab 직선 거리.</summary>
    OkLab,
    /// <summary>CIEDE2000(CIELAB D65).</summary>
    Ciede2000,
    /// <summary>OKLCh 색상 가중(<see cref="PaletteSettings.HueWeight"/>).</summary>
    OkLchHue,
}

/// <summary>외곽선을 1px로 맞추는 방법.</summary>
public enum OutlineFit
{
    /// <summary>크기를 줄여서: 칸 크기를 바깥 외곽선 두께로(줄이면 외곽선이 1px), 남은 겹은 한 겹만 얇게 정리.</summary>
    Shrink,
    /// <summary>외곽선만 얇게: 칸 크기는 격자 설정대로(기본 자동), 안쪽으로 겹친 외곽선을 최대 3겹 벗겨 낸다.</summary>
    Thin,
    /// <summary>맞추지 않음.</summary>
    Off,
    /// <summary>
    /// 벡터로 다시 그리기: 칸 크기는 격자 설정대로('외곽선만 얇게'와 같음), 원본의 어두운 외곽선 띠를 뼈대 선으로 따서 결과 칸에 1px 픽셀 퍼펙트 선으로
    /// 다시 그린다(<see cref="VectorOutline"/>).
    /// </summary>
    Vector,
}

/// <summary>외곽선 정리 방식.</summary>
public enum OutlineMode
{
    None,
    /// <summary>가장자리(투명과 맞닿은)의 어두운 픽셀을 가장 어두운 색 하나로.</summary>
    UnifyDarkest,
    /// <summary>가장자리에 외곽선이 빠진 곳에 가장 어두운 색 1px 외곽선을 더한다(모두 닫힌 외곽선).</summary>
    EnsureClosed,
}

public sealed record BackgroundSettings
{
    public BackgroundMode Mode { get; init; } = BackgroundMode.Auto;
    /// <summary>배경색(<see cref="BackgroundMode.PickColor"/>일 때) — "#RRGGBB".</summary>
    public string Color { get; init; } = "#000000";
    /// <summary>배경으로 볼 색 거리(RGB 유클리드, 0~441). <see cref="AutoTolerance"/>면 테두리 잡음에서 정한다.</summary>
    public double Tolerance { get; init; } = 24;
    public bool AutoTolerance { get; init; } = true;
    /// <summary>테두리와 이어진 배경만 지운다(스프라이트 안쪽의 같은 색·외곽선은 남김).</summary>
    public bool ConnectedOnly { get; init; } = true;
    /// <summary>그림에 이미 투명한 배경이 있으면(테두리가 대부분 투명) 그 알파를 그대로 쓴다.</summary>
    public bool KeepExistingAlpha { get; init; } = true;
    /// <summary>이 알파보다 낮은 픽셀은 배경(0~255).</summary>
    public int AlphaThreshold { get; init; } = 128;
    /// <summary>이 픽셀 수보다 작은 떨어진 조각(잡티)은 배경으로(0 = 끔).</summary>
    public int RemoveSpecks { get; init; } = 24;
    /// <summary>스티커처럼 외곽선 바깥을 두른 흰 띠를 저절로 찾아 지운다(<see cref="BackgroundRemoval.RemoveStickerBorder"/>).</summary>
    public bool RemoveStickerBorder { get; init; } = true;
    /// <summary>찾은 배경을 지우지 않고 한 색(배경색)으로 고르게 남긴다 — 결과도 그 색 바탕(색으로 찾은 배경일 때만).</summary>
    public bool KeepBackground { get; init; }
}

public sealed record FrameSettings
{
    public FrameSplitMode Mode { get; init; } = FrameSplitMode.Auto;
    public int Columns { get; init; } = 4;
    public int Rows { get; init; } = 1;
    /// <summary>자동 나누기: 가장 큰 조각 넓이의 이 비율보다 작은 조각은 가까운 프레임에 합친다(0~1).</summary>
    public double MergeSmallFraction { get; init; } = 0.12;
    /// <summary>자동 나누기: 합칠 조각이 프레임에서 이만큼(원본 px) 넘게 떨어져 있으면 버린다(잡티).</summary>
    public int MergeDistance { get; init; } = 48;
    public FrameAlign Align { get; init; } = FrameAlign.BottomCenter;
    /// <summary>모든 프레임을 같은 캔버스 크기(가장 큰 프레임 + 여백)로.</summary>
    public bool CommonCanvas { get; init; } = true;
}

public sealed record PixelGridSettings
{
    public GridMode Mode { get; init; } = GridMode.Auto;
    /// <summary>수동 칸 크기(원본 px).</summary>
    public double Scale { get; init; } = 8;
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    /// <summary>자동: 찾은 칸 크기에 더할 값(후보 만들기에서 ±1 등).</summary>
    public double ScaleAdjust { get; init; }
    /// <summary>자동: 칸 크기가 흔들려도 되는 비율(0 = 고른 간격 그대로, 0.15 = ±15%).</summary>
    public double Jitter { get; init; } = 0.15;
    /// <summary>자동: 프레임마다 따로 찾는다(끄면 그림 전체에서 한 번 — 모든 프레임이 같은 격자).</summary>
    public bool PerFrame { get; init; }
    /// <summary>자동 칸 크기를 무엇으로 정할지(기본: 색 경계). 정리의 외곽선 맞추기가 "크기를 줄여서"면 그쪽이 이겨 외곽선 두께를 쓴다(<see cref="Pixelator.ScaleSourceFor"/>).</summary>
    public ScaleSource Source { get; init; } = ScaleSource.Auto;
    /// <summary>
    /// 칸 경계 지역 보정(<see cref="LocalGrid"/>, 기본 켬): 축 전체에 한 줄로 고른 칸 경계를 띠(<see cref="LocalBand"/>칸)마다 그 띠의 뚜렷한 경계에 맞춰
    /// ±<see cref="LocalShift"/>칸 안에서 다시 고른다 — 그림 한쪽이 반 칸쯤 어긋난 AI 그림. 표본 6개 중 4개가 나아지고(점수 +2~+9) 2개는 그대로·조금 낮아
    /// (−0.1, 칸 5px 미만은 하지 않음 — <see cref="LocalGrid.MinCell"/>) 기본으로 켠다.
    /// </summary>
    public bool LocalCorrection { get; init; } = true;
    /// <summary>지역 보정: 경계가 움직일 수 있는 거리(칸 크기 비율, <see cref="PixelateRanges.LocalShift"/>).</summary>
    public double LocalShift { get; init; } = 0.35;
    /// <summary>지역 보정: 띠 너비(칸, <see cref="PixelateRanges.LocalBand"/>) — 표본에서 2칸이 4·8칸보다 나음.</summary>
    public int LocalBand { get; init; } = 2;
}

public sealed record SizeSettings
{
    public OutputSizeMode Mode { get; init; } = OutputSizeMode.FromScale;
    public int Width { get; init; } = 32;
    public int Height { get; init; } = 32;
    public bool KeepAspect { get; init; } = true;
    /// <summary>스프라이트 둘레 여백(결과 px).</summary>
    public int Padding { get; init; } = 1;
    /// <summary><see cref="OutputSizeMode.ScaleFactor"/>일 때의 배율(결과 = 원본 × 배율).</summary>
    public double Factor { get; init; } = 0.125;
}

public sealed record DownsampleSettings
{
    public DownsampleMode Mode { get; init; } = DownsampleMode.Mode;
    /// <summary>칸 둘레에서 무시할 비율(가장자리는 번져 있음, 0~0.45).</summary>
    public double BorderFraction { get; init; } = 0.2;
    /// <summary>칸 안 배경 아닌 픽셀이 이 비율보다 적으면 투명(0~1).</summary>
    public double AlphaThreshold { get; init; } = 0.5;
    /// <summary>가장 많은 색 방식에서 색을 묶는 간격(RGB 채널 단위).</summary>
    public int SnapStep { get; init; } = 20;
}

public sealed record PaletteSettings
{
    public PaletteMethod Method { get; init; } = PaletteMethod.KMeans;
    /// <summary>색 수 2~64, 0 = 자동(오차 곡선의 팔꿈치).</summary>
    public int Colors { get; init; }
    /// <summary>기존 팔레트 색("#RRGGBB").</summary>
    public IReadOnlyList<string> ExistingColors { get; init; } = [];
    /// <summary>기존 팔레트 이름(화면 표시·기억용).</summary>
    public string? ExistingName { get; init; }
    /// <summary>모든 프레임이 한 팔레트를 쓴다.</summary>
    public bool Shared { get; init; } = true;
    public PaletteDither Dither { get; init; } = PaletteDither.None;
    /// <summary>아주 어두운 색들을 외곽선 색 하나로 모은다.</summary>
    public bool SnapOutline { get; init; } = true;
    /// <summary>만든 팔레트의 색: 실제 칸 색(기본) 또는 묶음 평균.</summary>
    public PaletteColorSource ColorSource { get; init; } = PaletteColorSource.Original;
    /// <summary>원본 색에서 묶을 때 가장자리(투명과 맞닿은)·섞인(양옆 칸 색 사이 — 번진 경계) 칸을 셀 무게(0~1).</summary>
    public double EdgeWeight { get; init; } = 0.25;
    /// <summary>
    /// 외곽선 두께를 잴 때(원본의 외곽선 두께, 결과의 '외곽선 1px' 점수) 적어도 이 밝기 이하는 외곽선으로 본다(<see cref="OutlineDarkness"/>의 기본값과 같음):
    /// 밝기를 이보다 낮추면 검은 외곽선도 외곽선으로 세지 않아 외곽선 1px 점수가 거저 오르고(정리가 외곽선을 얇게 하지 않는데도) 원본 두께도 잘못 잰다.
    /// </summary>
    public const double DefaultOutlineDarkness = 0.24;
    /// <summary>외곽선으로 볼 밝기(OKLab L, 0~1).</summary>
    public double OutlineDarkness { get; init; } = DefaultOutlineDarkness;

    /// <summary>외곽선 두께를 잴 때 쓸 밝기: <see cref="OutlineDarkness"/>와 <see cref="DefaultOutlineDarkness"/> 중 큰 쪽.</summary>
    public static double MeasureDarkness(double darkness) => Math.Max(darkness, DefaultOutlineDarkness);

    /// <summary>
    /// 결과에서 뺄 색("#RRGGBB") — 다 만든 결과에서 가까운(<see cref="PaletteEdits.MatchDistance"/>) 색을 지우는 뒤처리(<see cref="PaletteEdits.Apply"/>):
    /// 그 색 픽셀은 원본 칸 색에 가장 가까운 남은 색으로, 나머지 픽셀·색은 그대로. 고정 색(<see cref="PinnedColors"/>)은 빠지지 않는다.
    /// </summary>
    public IReadOnlyList<string> ExcludedColors { get; init; } = [];
    /// <summary>
    /// 고정 색("#RRGGBB" — 원본 칸에서 집은 색): 만든 팔레트(k-평균·중앙 분할)와 기존 팔레트에 그대로 들어가는 색. 색 수(자동이면 찾은 수)에 들어가며
    /// 나머지 색을 (색 수 − 고정 색 수, 적어도 1)로 만든다. 원본 칸 색이 다른 팔레트 색보다 고정 색에 가까운(OKLab) 칸은 늘 그 고정 색.
    /// 원본 색 유지(<see cref="PaletteMethod.KeepOriginal"/>)는 고정 색과 같은 색으로 맞는(<see cref="PaletteEdits.MatchDistance"/>) 칸만 그 색으로 맞춘다.
    /// 색 빼기·외곽선 색 모으기(<see cref="SnapOutline"/>)에 빠지지 않는다.
    /// </summary>
    public IReadOnlyList<string> PinnedColors { get; init; } = [];
    /// <summary>
    /// 색 거리: 칸 → 팔레트 색(디더 없을 때 — 디더는 RGB 거리 <see cref="Edit.PaletteRemap"/>), 고정 색 맞추기, 색 빼기의 바꿀 색, 원본 색 고르기,
    /// '주요 색 변화' 점수. k-평균 묶기는 OKLab 그대로. 기본은 OKLCh 색상 가중(w = 2 — 색상 차를 두 배로 봐 작은 살구색 그늘이 금색으로 가지 않음, w = 1이면 OKLab와 같아
    /// OKLab로 정한 문턱값이 그대로 맞음).
    /// </summary>
    public ColorDistanceMode ColorDistance { get; init; } = ColorDistanceMode.OkLchHue;
    /// <summary>OKLCh 색상 가중의 색상 무게(1 = OKLab와 같음, <see cref="PixelateRanges.HueWeight"/>).</summary>
    public double HueWeight { get; init; } = 2;
    /// <summary>
    /// 명암 계열 우선: 칸 색과 색상·채도가 비슷한(<see cref="Pixelate.ColorDistance.SameShadeFamily"/>) 팔레트 색이 있으면 다른 색상의 색보다 그 계열에서 밝기가 가장
    /// 가까운 색으로(<see cref="ColorMetric.Nearest"/>), 디더는 색상이 다른 유채색과 섞지 않는다(<see cref="Pixelate.ColorDistance.CanDither"/>) — 진한 살구색 그늘이
    /// 금색이나 금색 점 무늬가 되지 않게.
    /// </summary>
    public bool PreferShadeFamily { get; init; } = true;
    /// <summary>
    /// 다른 색상 지키기: 원본에서 채도 ≥ <see cref="DistinctMinChroma"/>이고 넓이 ≥ <see cref="DistinctMinArea"/>칸인 색 무리가 바뀔 팔레트 색과
    /// 색상이 <see cref="DistinctMinHue"/>도 이상 다르면 그 색을 넣는다(자동 색 수는 더하고, 고른 색 수는 가장 덜 중요한 색과 바꿈 — 고정 색은 그대로).
    /// </summary>
    public bool KeepDistinctHues { get; init; } = true;
    /// <summary>다른 색상 지키기: 볼 색의 최소 채도(OKLCh C).</summary>
    public double DistinctMinChroma { get; init; } = 0.04;
    /// <summary>다른 색상 지키기: 바뀔 팔레트 색과의 최소 색상 차(도).</summary>
    public double DistinctMinHue { get; init; } = 20;
    /// <summary>다른 색상 지키기: 색 무리의 최소 넓이(칸 — 원본 색이면 가장자리 무게를 준 칸 수).</summary>
    public double DistinctMinArea { get; init; } = 4;
}

public sealed record CleanupSettings
{
    /// <summary>둘레 8칸 어디에도 같은 색이 없는 외톨이 픽셀을 이웃 색으로.</summary>
    public bool RemoveOrphans { get; init; } = true;
    /// <summary>외톨이로 지울 최대 대비(OKLab 거리) — 눈동자처럼 대비가 큰 점은 남긴다. 1이면 모두.</summary>
    public double OrphanMaxContrast { get; init; } = 0.18;
    /// <summary>상하좌우가 모두 칠해진 1px 구멍을 메운다.</summary>
    public bool FillHoles { get; init; } = true;
    public OutlineMode Outline { get; init; } = OutlineMode.None;
    /// <summary>외곽선 1px 맞추기(기본: 크기를 줄여서 — 격자가 자동이면 칸 크기를 외곽선 두께로 정함, 수동 칸 크기가 이김).</summary>
    public OutlineFit OutlineFit { get; init; } = OutlineFit.Shrink;
    /// <summary>외곽선의 L자 겹 픽셀(계단 모서리)을 줄인다.</summary>
    public bool ReduceJaggies { get; init; }
    /// <summary>벡터로 다시 그리기: 선을 단순하게 할 허용 오차(결과 px, 더글러스–포이커 — 0이면 뼈대를 그대로 따라감).</summary>
    public double VectorTolerance { get; init; } = 0.6;
    /// <summary>벡터로 다시 그리기: 바깥과 닿지 않은 어두운 덩어리는 넓이가 이 칸 수보다 작으면(눈 등) 다시 그리지 않고 그대로 둔다.</summary>
    public double VectorBlobCells { get; init; } = 6;
}

public sealed record ConsistencySettings
{
    /// <summary>앞뒤 프레임이 같은 색인데 이 프레임만 조금 다른 픽셀을 앞뒤 색으로(프레임 떨림 줄이기).</summary>
    public bool TemporalMajority { get; init; }
    /// <summary>바꿀 최대 대비(OKLab 거리).</summary>
    public double MaxContrast { get; init; } = 0.12;
}

/// <summary>AI 그림 픽셀로 정리의 모든 설정. 단계마다 기본값이 있다.</summary>
public sealed record PixelateSettings
{
    public BackgroundSettings Background { get; init; } = new();
    public FrameSettings Frames { get; init; } = new();
    public PixelGridSettings Grid { get; init; } = new();
    public SizeSettings Size { get; init; } = new();
    public DownsampleSettings Downsample { get; init; } = new();
    public PaletteSettings Palette { get; init; } = new();
    public CleanupSettings Cleanup { get; init; } = new();
    public ConsistencySettings Consistency { get; init; } = new();

    /// <summary>값이 같은 설정이면 같은 문자열(후보 중복 거르기).</summary>
    public string Key() => PixelatePresets.SerializeSettings(this, indented: false);

    internal static Color32 ParseColor(string? hex, Color32 fallback)
        => Color32.TryParseHex(hex, out var c) ? c : fallback;
}
