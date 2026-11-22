namespace Chickxel.App;

/// <summary>곡선 펜 그리기 방식: 점을 찍어 잇기(포토샵 펜) 또는 손으로 그린 획을 맞추기.</summary>
public enum CurvePenMode { Points, Freehand }

/// <summary>
/// 곡선 펜 설정(도구 설정 — 실행취소 없음): 그리기 방식, 맞춤 허용치와 모서리 각도(손으로 그리기 — <see cref="Chickxel.Core.Pixelate.BezierLines.Fit"/>에 그대로 넘김).
/// </summary>
public sealed partial class EditorContext
{
    /// <summary>맞춤 허용치 범위(px)와 기본값.</summary>
    public const double MinCurvePenTolerance = 0.1, MaxCurvePenTolerance = 4, DefaultCurvePenTolerance = 0.6;
    /// <summary>모서리 각도 범위(도)와 기본값. 180°면 모서리로 나누지 않는다.</summary>
    public const double MinCurvePenCornerAngle = 5, MaxCurvePenCornerAngle = 180, DefaultCurvePenCornerAngle = 40;

    private CurvePenMode _curvePenMode = CurvePenMode.Points;
    private double _curvePenTolerance = DefaultCurvePenTolerance;
    private double _curvePenCornerAngle = DefaultCurvePenCornerAngle;

    /// <summary>그리기 방식: 점 찍기(누름 = 모서리 점, 누르고 끌기 = 매끈한 점과 손잡이) 또는 손으로 그리기(놓으면 베지어로 맞춤).</summary>
    public CurvePenMode CurvePenMode
    {
        get => _curvePenMode;
        set
        {
            if (!Enum.IsDefined(value)) value = CurvePenMode.Points;
            if (_curvePenMode == value) return;
            _curvePenMode = value;
            OptionsChanged();
        }
    }

    /// <summary>맞춤 허용치(px): 손으로 그린 획이 베지어 곡선·직선에서 이만큼까지 벗어나도 한 조각으로 본다(클수록 점이 적음).</summary>
    public double CurvePenTolerance
    {
        get => _curvePenTolerance;
        set
        {
            value = double.IsFinite(value) ? Math.Clamp(value, MinCurvePenTolerance, MaxCurvePenTolerance) : DefaultCurvePenTolerance;
            if (_curvePenTolerance == value) return;
            _curvePenTolerance = value;
            OptionsChanged();
        }
    }

    /// <summary>모서리 각도(도): 획이 이만큼 이상 꺾인 자리는 날카로운 모서리로 남기고 그 앞뒤를 따로 맞춘다.</summary>
    public double CurvePenCornerAngle
    {
        get => _curvePenCornerAngle;
        set
        {
            value = double.IsFinite(value) ? Math.Clamp(value, MinCurvePenCornerAngle, MaxCurvePenCornerAngle) : DefaultCurvePenCornerAngle;
            if (_curvePenCornerAngle == value) return;
            _curvePenCornerAngle = value;
            OptionsChanged();
        }
    }
}
