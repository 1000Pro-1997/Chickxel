using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>도구 설정 "곡선 펜" 묶음: 그리기 방식(점 찍기·손으로 그리기), 손으로 그리기의 맞춤 허용치(px)·모서리 각도(°)와 동작 설명 정보박스.</summary>
public sealed partial class ToolOptionsPanel
{
    private ComboBox _curvePenMode = null!;
    private NumericUpDown _curvePenTolerance = null!, _curvePenCornerAngle = null!;
    private Control _curvePenToleranceRow = null!, _curvePenCornerAngleRow = null!;

    /// <summary>곡선 펜 묶음을 만든다(생성자에서 한 번).</summary>
    private Border BuildCurvePen()
    {
        // 순서 = CurvePenMode 값
        _curvePenMode = Ui.Combo(150, UiStrings.CurvePen_ModePoints, UiStrings.CurvePen_ModeFreehand);
        _curvePenMode.SelectionChanged += (_, _) =>
        {
            if (_updating || _curvePenMode.SelectedIndex < 0) return;
            _ctx.CurvePenMode = (CurvePenMode)_curvePenMode.SelectedIndex;
        };
        _curvePenMode.Tip(UiStrings.CurvePen_ModeTip);
        _curvePenTolerance = Ui.Num((decimal)EditorContext.MinCurvePenTolerance, (decimal)EditorContext.MaxCurvePenTolerance,
                (decimal)EditorContext.DefaultCurvePenTolerance, 52, 1, 0.1m)
            .Presets(0.3m, 0.5m, 0.6m, 0.8m, 1, 1.5m, 2, 3).Unit("px").RecentKey("curvepen.tolerance");
        _curvePenTolerance.Tip(UiStrings.CurvePen_ToleranceTip);
        _curvePenTolerance.ValueChanged += (_, _) => { if (!_updating && _curvePenTolerance.Value is { } v) _ctx.CurvePenTolerance = (double)v; };
        _curvePenCornerAngle = Ui.Num((decimal)EditorContext.MinCurvePenCornerAngle, (decimal)EditorContext.MaxCurvePenCornerAngle,
                (decimal)EditorContext.DefaultCurvePenCornerAngle, 52, 0, 5)
            .Presets(20, 30, 40, 60, 90, 120, 180).Unit("°").RecentKey("curvepen.corner");
        _curvePenCornerAngle.Tip(UiStrings.CurvePen_CornerAngleTip);
        _curvePenCornerAngle.ValueChanged += (_, _) => { if (!_updating && _curvePenCornerAngle.Value is { } v) _ctx.CurvePenCornerAngle = (double)v; };
        _curvePenToleranceRow = Ui.Row(Ui.Label(UiStrings.CurvePen_Tolerance), _curvePenTolerance, Ui.Label("px"));
        _curvePenCornerAngleRow = Ui.Row(Ui.Label(UiStrings.CurvePen_CornerAngle), _curvePenCornerAngle, Ui.Label("°"));
        return InfoGroup(UiStrings.CurvePen_Group, UiStrings.CurvePen_Info, Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.CurvePen_Mode), _curvePenMode),
            _curvePenToleranceRow,
            _curvePenCornerAngleRow));
    }

    /// <summary>곡선 펜 값을 지금 설정으로 맞춘다(<see cref="_updating"/> 안에서). 맞춤 허용치·모서리 각도는 손으로 그리기에서만 보인다.</summary>
    private void RefreshCurvePen()
    {
        _curvePenMode.SelectedIndex = (int)_ctx.CurvePenMode;
        _curvePenTolerance.Value = (decimal)Math.Round(_ctx.CurvePenTolerance, 1);
        _curvePenCornerAngle.Value = (decimal)Math.Round(_ctx.CurvePenCornerAngle);
        _curvePenToleranceRow.IsVisible = _curvePenCornerAngleRow.IsVisible = _ctx.CurvePenMode == CurvePenMode.Freehand;
    }

    /// <summary>곡선 펜 묶음 안 컨트롤의 가이드 자리(아니면 null).</summary>
    private HelpTarget? CurvePenHelpTarget(Control c)
    {
        const string page = "tools-curve-pen";
        bool InRow(Control control) => ReferenceEquals(c, control) || ReferenceEquals(c, control.Parent);
        if (InRow(_curvePenMode)) return new(page, Anchor: UiStrings.CurvePen_Mode);
        if (InRow(_curvePenTolerance)) return new(page, Anchor: UiStrings.CurvePen_Tolerance);
        if (InRow(_curvePenCornerAngle)) return new(page, Anchor: UiStrings.CurvePen_CornerAngle);
        if (ReferenceEquals(c, _curvePenBox)) return new(page);
        return null;
    }
}
