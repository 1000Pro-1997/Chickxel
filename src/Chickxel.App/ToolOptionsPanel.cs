using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 오른쪽 독 "도구 설정": 고른 도구의 세부 설정만 보인다. 브러시(모양·크기·미리보기), 채우기(허용 오차·이어진 영역·대각선·기준),
/// 스포이드(기준), 밝게/어둡게(세기), 디더링(무늬), 글씨(글꼴), 그라데이션(색 정지점 바·보간·디더링), 그라데이션 연필(방식·반복 간격·프리셋·정지점 바), 도형(모양·꼭짓점/뿔/감은 수·채움), 이동(순환), 대칭(피벗 기준 포함), 피벗 도구(K)의 피벗(픽셀 단위), 9-slice 도구(S)의 테두리(L·T·R·B).
/// 변형 모드(Ctrl+T) 중에는 변형 값(각도·가로·세로 %·중심 X/Y)·리샘플링·적용/취소만 보인다(캔버스 손잡이와 함께 움직임).
/// 캔버스 크기 모드(Ctrl+Alt+C) 중에는 캔버스 가로·세로와 중심 기준, 그림 가로·세로(%)·위치, 그림 기준점 9방향, 비율 유지, 확인/취소를 보인다(캔버스 손잡이와 함께 움직임).
/// 도구 설정은 <see cref="EditorContext"/>에 있고(실행취소 없음) 바뀌면 <see cref="EditorContext.ToolOptionsChanged"/>로 다시 맞춘다.
/// 피벗 값 바꾸기는 프로젝트 편집이라 실행취소된다.
/// </summary>
public sealed class ToolOptionsPanel : UserControl
{
    /// <summary>피벗 5×5 단추 칸 수(한 변). 칸 사이 간격 = 1/4.</summary>
    private const int PivotSteps = 5;
    /// <summary>바깥 8방향·가운데 단추 아이콘(그 밖은 작은 점), 위 왼쪽부터 행 순서.</summary>
    private static readonly Dictionary<(int Col, int Row), VectorIcon> PivotArrows = new()
    {
        [(0, 0)] = VectorIcon.ArrowUpLeft, [(2, 0)] = VectorIcon.ArrowUp, [(4, 0)] = VectorIcon.ArrowUpRight,
        [(0, 2)] = VectorIcon.ArrowLeft, [(2, 2)] = VectorIcon.Dot, [(4, 2)] = VectorIcon.ArrowRight,
        [(0, 4)] = VectorIcon.ArrowDownLeft, [(2, 4)] = VectorIcon.ArrowDown, [(4, 4)] = VectorIcon.ArrowDownRight,
    };
    private static readonly (BrushShape Shape, VectorIcon Icon, string Text, string Tip)[] BrushShapes =
    [
        (BrushShape.Square, VectorIcon.Square, UiStrings.S_ToolOptionsPanel_Shapes_157988A91F, UiStrings.S_ToolOptionsPanel_Shapes_B8823EE594),
        (BrushShape.Circle, VectorIcon.Circle, UiStrings.S_ToolOptionsPanel_Shapes_C6B5555BE7, UiStrings.S_ToolOptionsPanel_Shapes_C3C41E96DA),
        (BrushShape.Diamond, VectorIcon.Diamond, UiStrings.S_ToolOptionsPanel_Shapes_E6AFBF1490, UiStrings.S_ToolOptionsPanel_Shapes_8D0E0B04AC),
    ];
    /// <summary>도형 도구 모양 단추(순서대로): 모양, 아이콘, 이름(실행취소 이름으로도 씀), 안내.</summary>
    private static readonly (ShapeKind Kind, VectorIcon Icon, string Name, string Hint)[] ShapeDefs =
    [
        (ShapeKind.Line, VectorIcon.LineDiagonal, UiStrings.Shape_Line, UiStrings.Shape_Line_Hint),
        (ShapeKind.Curve, VectorIcon.Curve, UiStrings.Shape_Curve, UiStrings.Shape_Curve_Hint),
        (ShapeKind.Rectangle, VectorIcon.Square, UiStrings.Shape_Rectangle, UiStrings.Shape_Rectangle_Hint),
        (ShapeKind.Ellipse, VectorIcon.Oval, UiStrings.Shape_Ellipse, UiStrings.Shape_Ellipse_Hint),
        (ShapeKind.Triangle, VectorIcon.Triangle, UiStrings.Shape_Triangle, UiStrings.Shape_Triangle_Hint),
        (ShapeKind.IsoscelesTriangle, VectorIcon.TriangleTall, UiStrings.Shape_Isosceles, UiStrings.Shape_Isosceles_Hint),
        (ShapeKind.Polygon, VectorIcon.Pentagon, UiStrings.Shape_Polygon, UiStrings.Shape_Polygon_Hint),
        (ShapeKind.Star, VectorIcon.Star, UiStrings.Shape_Star, UiStrings.Shape_Star_Hint),
        (ShapeKind.Spiral, VectorIcon.Spiral, UiStrings.Shape_Spiral, UiStrings.Shape_Spiral_Hint),
        (ShapeKind.Cylinder, VectorIcon.Cylinder, UiStrings.Shape_Cylinder, UiStrings.Shape_Cylinder_Hint),
        (ShapeKind.Cube, VectorIcon.Cube, UiStrings.Shape_Cube, UiStrings.Shape_Cube_Hint),
    ];
    private static readonly string[] SourceNames = [UiStrings.S_ToolOptionsPanel_SourceNames_6198D7D9B6, UiStrings.S_ToolOptionsPanel_SourceNames_06199A4484];

    /// <summary>도형 모양 이름(예: "별").</summary>
    internal static string ShapeName(ShapeKind kind) => ShapeDefs.First(d => d.Kind == kind).Name;

    private readonly EditorContext _ctx;
    private readonly TextBlock _title;
    private bool _updating;
    private bool _gradientSyncing, _pencilGradientSyncing;

    // 브러시
    private readonly Border _brushBox;
    private readonly TextBlock _brushTitle, _fillTitle;
    /// <summary>브러시 모양(<see cref="BrushShapes"/> 순서). 클릭해도 캔버스 포커스를 빼앗지 않는다.</summary>
    private readonly TabStrip _shapes = new(TabStripKind.Segmented) { Focusable = false, IsTabStop = false, Margin = default };
    private readonly NumericUpDown _sizeNum;
    private readonly BrushPreview _brushPreview = new();

    // 도구별
    private readonly Border _fillBox, _pickerBox, _dodgeBox, _eraserBox, _ditherBox, _textBox, _optionsBox, _mirrorBox;
    private readonly NumericUpDown _tolNum, _dodgeNum, _eraserNum;
    private readonly CheckBox _contiguous, _diagonal, _pixelPerfect, _shapeFilled, _rightErase, _wrap, _mirrorX, _mirrorY, _mirrorAtPivot;
    private readonly CheckBox _eraseRed, _eraseGreen, _eraseBlue;
    private readonly ComboBox _fillSource, _pickSource, _ditherPattern, _textFont;
    /// <summary>제목 줄의 "i": 도구 쓰는 법(선택·이동·그라데이션 등, 없으면 숨김).</summary>
    private readonly Border _hint;

    // 도형
    private readonly Border _shapeBox;
    /// <summary>모양 단추(<see cref="ShapeDefs"/> 순서). 클릭해도 캔버스 포커스를 빼앗지 않는다.</summary>
    private readonly Button[] _shapeButtons = new Button[ShapeDefs.Length];
    private readonly TextBlock _shapeHint;
    private readonly NumericUpDown _sidesNum, _starNum, _turnsNum;
    /// <summary>선·곡선 픽셀 잇기(0 = 대각선, 1 = ㄱ자 4방향). 클릭해도 캔버스 포커스를 빼앗지 않는다.</summary>
    private readonly TabStrip _lineSteps = new(TabStripKind.Segmented) { Focusable = false, IsTabStop = false, Margin = default };
    private readonly StackPanel _lineStepsRow;
    private readonly StackPanel _sidesRow, _starRow, _turnsRow;

    // 그라데이션
    private readonly Border _gradientBox;
    private readonly GradientEditor _gradientEditor;

    // 그라데이션 연필
    private readonly Border _pencilGradientBox;
    private readonly GradientEditor _pencilGradientEditor;
    /// <summary>획 전체 / 반복. 클릭해도 캔버스 포커스를 빼앗지 않는다.</summary>
    private readonly TabStrip _pencilGradientMode = new(TabStripKind.Segmented) { Focusable = false, IsTabStop = false, Margin = default };
    private readonly NumericUpDown _pencilIntervalNum;
    private readonly StackPanel _pencilIntervalRow;

    // 피벗
    private readonly Border _pivotBox;
    private readonly Border _sliceBox;
    private readonly NumericUpDown _sL, _sT, _sR, _sB;
    private readonly Button _sliceClear;
    private readonly TextBlock _sliceTarget;
    private readonly Button[] _presetButtons = new Button[PivotSteps * PivotSteps];
    private readonly NumericUpDown _pivotX, _pivotY;
    private readonly CheckBox _framePivot;
    private readonly TextBlock _pivotTarget, _pivotXMax, _pivotYMax;

    // 변형 모드
    private readonly Border _transformBox;
    private readonly NumericUpDown _tfAngle, _tfScaleX, _tfScaleY, _tfX, _tfY;
    private readonly ComboBox _tfResample;
    private readonly TextBlock _tfInfo;

    // 캔버스 크기 모드
    private readonly Border _canvasResizeBox;
    private readonly NumericUpDown _crWidth, _crHeight, _crImageWidth, _crImageHeight, _crX, _crY;
    private readonly TextBlock _crWidthPercent, _crHeightPercent;
    private readonly CheckBox _crKeepRatio, _crFromCenter, _crSlice;
    private readonly ComboBox _crSliceFill;
    private readonly AnchorPicker _crAnchor = new();

    public ToolOptionsPanel(EditorContext ctx)
    {
        _ctx = ctx;
        Background = AppTheme.PanelBrush;

        // ── 브러시: 모양 단추 + 크기(슬라이더·숫자) + 미리보기
        foreach (var s in BrushShapes)
            _shapes.Add(s.Text, (g, r, b) => VectorIcons.Draw(g, s.Icon, r, 10, b, filled: true), s.Tip);
        _shapes.SelectionChanged += (_, _) =>
        {
            if (_updating || _shapes.SelectedIndex < 0) return;
            _ctx.BrushShape = BrushShapes[_shapes.SelectedIndex].Shape;
            Refresh();
        };
        _sizeNum = NumBox(1, BrushFootprint.MaxSize, v => _ctx.BrushSize = v)
            .Presets(1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 24, 32).Unit("px").RecentKey("brush.size");
        _sizeNum.Tip(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_041B00982B,BrushFootprint.MaxSize));
        var brushRight = new StackPanel { Spacing = 4, Children = { _shapes, Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_CAF69A1BB7), _sizeNum) } };
        _brushPreview.Margin = new Thickness(0, 0, 6, 0);
        _brushPreview.VerticalAlignment = VerticalAlignment.Top;
        _brushPreview.Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_FC317FE6C0);
        Grid.SetColumn(brushRight, 1);
        var brushGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { _brushPreview, brushRight } };
        _brushBox = Ui.Group(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_244D56A524, Ui.Stack(brushGrid));
        _brushTitle = GroupTitle(_brushBox);

        // ── 도형: 모양 단추 + 모양별 개수(정다각형 꼭짓점·별 뿔·나선 감은 수)
        var shapeButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < ShapeDefs.Length; i++)
        {
            var kind = ShapeDefs[i].Kind;
            var b = Ui.Button("", (_, _) => _ctx.ShapeKind = kind, 30);
            b.Height = 26;
            b.Padding = new Thickness(0);
            b.Margin = new Thickness(1);
            b.Focusable = false;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.VerticalContentAlignment = VerticalAlignment.Center;
            b.Tip(ShapeDefs[i].Name);
            _shapeButtons[i] = b;
            shapeButtons.Children.Add(b);
        }
        _shapeHint = Dim("");
        _shapeHint.TextWrapping = TextWrapping.Wrap;
        _sidesNum = NumBox(Shapes.MinSides, Shapes.MaxSides, v => _ctx.ShapeSides = v)
            .Presets(3, 4, 5, 6, 7, 8, 10, 12, 16).RecentKey("shape.sides");
        _sidesNum.Tip(UiStrings.Shape_SidesTip);
        _starNum = NumBox(Shapes.MinSides, Shapes.MaxSides, v => _ctx.StarPoints = v)
            .Presets(3, 4, 5, 6, 7, 8, 10, 12).RecentKey("shape.star");
        _starNum.Tip(UiStrings.Shape_PointsTip);
        _turnsNum = NumBox(Shapes.MinTurns, Shapes.MaxTurns, v => _ctx.SpiralTurns = v)
            .Presets(1, 2, 3, 4, 5, 6, 8, 10).RecentKey("shape.turns");
        _turnsNum.Tip(UiStrings.Shape_TurnsTip);
        _sidesRow = Ui.Row(Ui.Label(UiStrings.Shape_Sides), _sidesNum);
        _starRow = Ui.Row(Ui.Label(UiStrings.Shape_Points), _starNum);
        _turnsRow = Ui.Row(Ui.Label(UiStrings.Shape_Turns), _turnsNum);
        _lineSteps.Add(UiStrings.Shape_StepsDiagonal, tip: UiStrings.Shape_StepsDiagonalTip);
        _lineSteps.Add(UiStrings.Shape_StepsOrthogonal, tip: UiStrings.Shape_StepsOrthogonalTip);
        _lineSteps.SelectionChanged += (_, _) => { if (!_updating && _lineSteps.SelectedIndex >= 0) _ctx.ShapeOrthogonal = _lineSteps.SelectedIndex == 1; };
        _lineStepsRow = Ui.Row(Ui.Label(UiStrings.Shape_Steps), _lineSteps);
        _shapeBox = Ui.Group(UiStrings.Shape_Group, Ui.Stack(shapeButtons, _shapeHint, _sidesRow, _starRow, _turnsRow, _lineStepsRow));

        // ── 채우기
        _tolNum = NumBox(0, 100, v => _ctx.FillTolerance = v)
            .Presets(0, 5, 10, 15, 20, 25, 33, 50, 66, 75, 100).Unit("%").RecentKey("fill.tolerance");
        _tolNum.Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_2990A5AB95);
        _contiguous = Check(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_A91BCFC490, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_0A75032EF4, v => _ctx.FillContiguous = v);
        _diagonal = Check(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_B062F4CFD2, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_1D5510BF06, v => _ctx.FillDiagonal = v);
        _fillSource = Combo(v => _ctx.FillSampleVisible = v == 1);
        _fillSource.Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_37EFFDFA5B);
        _fillBox = Ui.Group(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_096C96B0A9, Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_A41D1B8B02), _tolNum, Ui.Label("%")),
            _contiguous,
            _diagonal,
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_810B27E1C0), _fillSource)));
        _fillTitle = GroupTitle(_fillBox);

        // ── 스포이드
        _pickSource = Combo(v => _ctx.PickerSampleVisible = v == 1);
        _pickSource.Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_D2DF12FE4F);
        _pickerBox = InfoGroup(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_410CC5D3AF, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_F8FA881EE9,
            Ui.Stack(Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_810B27E1C0), _pickSource)));

        // ── 밝게/어둡게
        _dodgeNum = NumBox(1, EditorContext.MaxDodgeStrength, v => _ctx.DodgeStrength = v)
            .Presets(1, 2, 3, 5, 8, 10, 15, 20, 25, 33, 50).Unit("%").RecentKey("dodge.strength");
        _dodgeNum.Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_5D33990D7E);
        _dodgeBox = Ui.Group(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_D0A7B65132, Ui.Stack(Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_43C9B223D1), _dodgeNum, Ui.Label("%"))));

        // ── 지우개: 강도와 채널
        _eraserNum = NumBox(1, 100, v => _ctx.EraserStrength = v)
            .Presets(5, 10, 20, 25, 33, 50, 75, 100).Unit("%").RecentKey("eraser.strength");
        _eraserNum.Tip(UiStrings.Eraser_StrengthTip);
        _eraseRed = Ui.Check("R");
        _eraseGreen = Ui.Check("G");
        _eraseBlue = Ui.Check("B");
        _eraseRed.IsCheckedChanged += (_, _) => { if (!_updating) _ctx.EraserRed = _eraseRed.On(); };
        _eraseGreen.IsCheckedChanged += (_, _) => { if (!_updating) _ctx.EraserGreen = _eraseGreen.On(); };
        _eraseBlue.IsCheckedChanged += (_, _) => { if (!_updating) _ctx.EraserBlue = _eraseBlue.On(); };
        var channels = Ui.Row(Ui.Label(UiStrings.Eraser_Channels), _eraseRed, _eraseGreen, _eraseBlue);
        ToolTip.SetTip(channels, UiStrings.Eraser_ChannelsTip);
        _eraserBox = Ui.Group(UiStrings.Eraser_Group, Ui.Stack(Ui.Row(Ui.Label(UiStrings.Eraser_Strength), _eraserNum, Ui.Label("%")), channels));

        // ── 디더링
        _ditherPattern = Ui.Combo(150, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_189071FAD4, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_0CF48A0E79, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_A181F0EEA8);
        _ditherPattern.SelectionChanged += (_, _) =>
        {
            if (_updating || _ditherPattern.SelectedIndex < 0) return;
            _ctx.DitherPattern = (DitherPattern)_ditherPattern.SelectedIndex;
        };
        _ditherPattern.Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_7D99E90556);
        _ditherBox = Ui.Group(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_FC57CE224F, Ui.Stack(Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_607280800B), _ditherPattern)));

        // ── 글씨: 새 글씨 레이어의 글꼴
        _textFont = Ui.Combo(150, LayerEffectsDialog.TextFontNames);
        _textFont.SelectionChanged += (_, _) =>
        {
            if (_updating || _textFont.SelectedIndex < 0) return;
            _ctx.TextFont = (TextFont)_textFont.SelectedIndex;
        };
        _textFont.Tip(UiStrings.TextTool_FontTip);
        _textBox = Ui.Group(UiStrings.TextTool_Group, Ui.Stack(Ui.Row(Ui.Label(UiStrings.TextEffect_Font), _textFont)));

        // ── 그라데이션: 같은 정지점 편집기를 도구와 레이어 효과에 사용한다.
        _gradientEditor = new GradientEditor(() => _ctx.Primary);
        _gradientEditor.Changed += () =>
        {
            _gradientSyncing = true;
            try
            {
                _ctx.GradientStops = _gradientEditor.Stops;
                _ctx.GradientAlphaStops = _gradientEditor.AlphaStops;
                _ctx.GradientInterpolation = _gradientEditor.Interpolation;
                _ctx.GradientDither = _gradientEditor.Dither;
                _ctx.GradientColorSpace = _gradientEditor.ColorSpace;
            }
            finally { _gradientSyncing = false; }
        };
        _gradientBox = InfoGroup(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_3700B54FB1, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_DA4BE7471C, Ui.Stack(_gradientEditor));

        // ── 그라데이션 연필: 방식(획 전체·반복)·반복 간격 + 프리셋 고르기·기본값 + 같은 정지점 편집기
        _pencilGradientMode.Add(UiStrings.GradientPencil_ModeWhole, tip: UiStrings.GradientPencil_ModeWholeTip);
        _pencilGradientMode.Add(UiStrings.GradientPencil_ModeRepeat, tip: UiStrings.GradientPencil_ModeRepeatTip);
        _pencilGradientMode.SelectionChanged += (_, _) =>
        {
            if (!_updating && _pencilGradientMode.SelectedIndex >= 0) _ctx.PencilGradientMode = (GradientStrokeMode)_pencilGradientMode.SelectedIndex;
        };
        _pencilIntervalNum = NumBox(GradientStroke.MinInterval, GradientStroke.MaxInterval, v => _ctx.PencilGradientInterval = v)
            .Presets(2, 4, 8, 12, 16, 24, 32, 48, 64, 128).Unit("px").RecentKey("gradient.pencil.interval");
        _pencilIntervalNum.Tip(UiStrings.GradientPencil_IntervalTip);
        _pencilIntervalRow = Ui.Row(Ui.Label(UiStrings.GradientPencil_Interval), _pencilIntervalNum, Ui.Label("px"));
        _pencilGradientEditor = new GradientEditor(() => _ctx.Primary);
        _pencilGradientEditor.Changed += () =>
        {
            _pencilGradientSyncing = true;
            try { _ctx.PencilGradient = _pencilGradientEditor.ToPreset("", favorite: false); }
            finally { _pencilGradientSyncing = false; }
        };
        var pencilPresets = Ui.Button(VectorIcon.ChevronDown, UiStrings.GradientPencil_Presets, iconAfter: true)
            .Tip(UiStrings.GradientPencil_PresetsTip);
        pencilPresets.Click += (_, _) => _pencilGradientEditor.ShowPresetMenu(pencilPresets);
        var pencilReset = Ui.Button(VectorIcon.Undo2, UiStrings.GradientPencil_Reset, (_, _) => _ctx.PencilGradient = EditorContext.DefaultPencilGradient)
            .Tip(UiStrings.GradientPencil_ResetTip);
        _pencilGradientBox = InfoGroup(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_3700B54FB1, UiStrings.GradientPencil_Info, Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.GradientPencil_Mode), _pencilGradientMode),
            _pencilIntervalRow,
            Ui.Row(pencilPresets, pencilReset),
            _pencilGradientEditor));

        // ── 옵션(도구마다 보이는 것만)
        _pixelPerfect = Check(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_B655203628, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_4DC4117198, v => _ctx.PixelPerfect = v);
        _shapeFilled = Check(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_FCE800703F, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_A43E20CBEA, v => _ctx.ShapeFilled = v);
        _rightErase = Check(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_4E9F04FE20, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_E1EAC3C042, v => _ctx.RightButtonErases = v);
        _wrap = Check(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_4AF6C42C92, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_07A907E085, v => _ctx.MoveWrap = v);
        _optionsBox = Ui.Group(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_0B9E51B984, Ui.Stack(_pixelPerfect, _shapeFilled, _rightErase, _wrap));

        // ── 대칭
        _mirrorX = Check(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_8BE1DA620D, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_2ADAF37DF1, v => _ctx.MirrorX = v);
        _mirrorX.Content = Ui.IconLabel(VectorIcon.MoveHorizontal, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_8BE1DA620D, iconAfter: true);
        _mirrorY = Check(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_B4DDF34C98, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_3FD1E9A85E, v => _ctx.MirrorY = v);
        _mirrorY.Content = Ui.IconLabel(VectorIcon.MoveVertical, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_B4DDF34C98, iconAfter: true);
        _mirrorAtPivot = Check(UiStrings.Mirror_AtPivot, UiStrings.Mirror_AtPivotTip, v => _ctx.MirrorAtPivot = v);
        _mirrorBox = Ui.Group(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_0E1824FF47, Ui.Stack(_mirrorX, _mirrorY, _mirrorAtPivot));

        // ── 피벗(픽셀: X는 왼쪽에서, Y는 아래에서)
        // 5×5: 가로·세로 0, 1/4, 1/2, 3/4, 1 지점(위 왼쪽 단추 = 왼쪽 위).
        var stepDefs = string.Join(",", Enumerable.Repeat("Auto", PivotSteps));
        var presetGrid = new Grid { ColumnDefinitions = new ColumnDefinitions(stepDefs), RowDefinitions = new RowDefinitions(stepDefs), Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        string[] quarters = ["0", "1/4", "1/2", "3/4", "1"];
        for (int i = 0; i < _presetButtons.Length; i++)
        {
            int col = i % PivotSteps, row = i / PivotSteps;
            var b = Ui.Button("", (_, _) => ApplyPivot(SnappedStep(col, row)), 26);
            b.Content = PivotArrows.TryGetValue((col, row), out var arrow)
                ? Ui.Icon(arrow, arrow == VectorIcon.Dot ? 14 : 12, filled: arrow == VectorIcon.Dot)
                : Ui.Icon(VectorIcon.Dot, 8, filled: true);
            b.Height = 22;
            b.Padding = new Thickness(0);
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.VerticalContentAlignment = VerticalAlignment.Center;
            b.Margin = new Thickness(1);
            b.Tip($"X {quarters[col]} · Y {quarters[PivotSteps - 1 - row]}");
            _presetButtons[i] = b;
            Grid.SetColumn(b, col);
            Grid.SetRow(b, row);
            presetGrid.Children.Add(b);
        }
        _pivotX = PixelNum("pivot.x");
        _pivotY = PixelNum("pivot.y");
        _pivotX.Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_862EF0756D);
        _pivotY.Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_6F1F475A03);
        _pivotXMax = Dim("");
        _pivotYMax = Dim("");
        _pivotX.ValueChanged += (_, _) =>
        {
            if (_updating || TargetPivot() is not { } cur) return;
            ApplyPivot(new Pivot((float)(_pivotX.NumValue() / TargetWidth), cur.Y));
        };
        _pivotY.ValueChanged += (_, _) =>
        {
            if (_updating || TargetPivot() is not { } cur) return;
            ApplyPivot(new Pivot(cur.X, (float)(_pivotY.NumValue() / TargetHeight)));
        };
        _framePivot = Ui.Check(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_8A93F258A3);
        _framePivot.IsCheckedChanged += (_, _) =>
        {
            if (_updating || _ctx.Project is not { } p) return;
            int f = _ctx.CurrentFrame;
            _ctx.Structural(() => p.SetFramePivot(f, _framePivot.On() ? p.GetFramePivot(f) : null));
        };
        _pivotTarget = Dim("");
        _pivotTarget.TextWrapping = TextWrapping.Wrap;
        _pivotBox = Ui.Group(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_C554693D22, Ui.Stack(
            presetGrid,
            Ui.Row(Ui.Label("X"), _pivotX, _pivotXMax),
            Ui.Row(Ui.Label("Y"), _pivotY, _pivotYMax),
            _framePivot,
            _pivotTarget));

        // ── 9-slice(S): 캔버스에서 초록 선을 끌거나 숫자로
        _sL = SliceNum();
        _sT = SliceNum();
        _sR = SliceNum();
        _sB = SliceNum();
        foreach (var n in new[] { _sL, _sT, _sR, _sB })
            n.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                _ctx.SetSliceBorder(new SpriteBorder((int)_sL.NumValue(), (int)_sT.NumValue(), (int)_sR.NumValue(), (int)_sB.NumValue()));
            };
        _sliceTarget = Dim("");
        _sliceTarget.TextWrapping = TextWrapping.Wrap;
        _sliceClear = Ui.Button(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_6A9E3F0F27, (_, _) => _ctx.SetSliceBorder(SpriteBorder.None)).Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_98ABAB10EB);
        _sliceBox = InfoGroup(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_A5622FB876, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_A527F1585C, Ui.Stack(
            Ui.Row(Ui.Label("L"), _sL, Ui.Label("R"), _sR),
            Ui.Row(Ui.Label("T"), _sT, Ui.Label("B"), _sB),
            _sliceClear,
            _sliceTarget));

        _hint = Ui.InfoBadge("");

        // ── 변형 모드(Ctrl+T): 숫자는 캔버스 손잡이와 함께 움직인다.
        _tfAngle = TransformNum(-180, 180, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_DF32263ADE,
            v => _ctx.SetTransformParams(_ctx.RotatedAboutPivot(_ctx.TransformParams, v)))
            .Presets(-180, -135, -90, -45, -30, -15, 0, 15, 30, 45, 90, 180).Unit("°").RecentKey("transform.angle");
        // 0%는 적용하지 않으므로 목록에 없음(−100% = 뒤집기).
        decimal[] scales = [-200, -100, 25, 50, 75, 100, 150, 200, 300, 400, 800];
        _tfScaleX = TransformNum(-3200, 3200, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_F91C1F9AF4,
            v => { if (v != 0) _ctx.SetTransformParams(_ctx.TransformParams with { ScaleX = v / 100 }); })
            .Presets(scales).Unit("%").RecentKey("transform.scale");
        _tfScaleY = TransformNum(-3200, 3200, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_162B6458A5,
            v => { if (v != 0) _ctx.SetTransformParams(_ctx.TransformParams with { ScaleY = v / 100 }); })
            .Presets(scales).Unit("%").RecentKey("transform.scale");
        // 중심 X·Y 예시 값은 캔버스 크기에 맞춰 RefreshTransform에서 정한다(0 · 1/4 · 1/2 · 3/4 · 끝).
        _tfX = TransformNum(-PixelImage.MaxSize, PixelImage.MaxSize, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_704C85E278,
            v => _ctx.SetTransformParams(_ctx.TransformParams with { CenterX = v })).Unit("px").RecentKey("transform.center.x");
        _tfY = TransformNum(-PixelImage.MaxSize, PixelImage.MaxSize, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_48CEA6539D,
            v => _ctx.SetTransformParams(_ctx.TransformParams with { CenterY = v })).Unit("px").RecentKey("transform.center.y");
        _tfResample = Ui.Combo(150, "RotSprite", UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_592987DB9E);
        _tfResample.Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_EA92356D34);
        _tfResample.SelectionChanged += (_, _) =>
        {
            if (_updating || _tfResample.SelectedIndex < 0) return;
            _ctx.TransformResample = (ResampleMode)_tfResample.SelectedIndex;
        };
        _tfInfo = Dim("");
        var tfApply = Ui.Button(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_8D7A9F13B5, (_, _) => _ctx.CommitTransform()).Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_2077BDA76D);
        var tfCancel = Ui.Button(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_BDB140E2ED, (_, _) => _ctx.CancelTransform()).Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_F1CB28FBF9);
        _transformBox = InfoGroup(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_E4C9B1697C, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_BFD255FFEA, Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_51B15B9385), _tfAngle, Ui.Label("°")),
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_571538645F), _tfScaleX, Ui.Label("%"), Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_A5A2849291), _tfScaleY, Ui.Label("%")),
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_CADA521CE3), _tfX, Ui.Label("Y"), _tfY),
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_652CB69BF4), _tfResample),
            _tfInfo,
            Ui.Row(tfApply, tfCancel)));

        // ── 캔버스 크기 모드(Ctrl+Alt+C): 숫자는 캔버스 손잡이와 함께 움직인다.
        _crKeepRatio = Ui.Check(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_93E9A22B2D, true).Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_2CD545CAF3);
        _crKeepRatio.Focusable = false;
        _crFromCenter = Ui.Check(UiStrings.CanvasResize_FromCenter, false).Tip(UiStrings.CanvasResize_FromCenterTip);
        _crFromCenter.Focusable = false;
        _crFromCenter.IsCheckedChanged += (_, _) => { if (!_updating) _ctx.CanvasResizeFromCenter = _crFromCenter.On(); };
        // 9-slice로 늘리기(기본 켬): 쓸 테두리가 없으면 끄고 풍선으로 까닭을 보인다.
        _crSlice = Ui.Check(UiStrings.CanvasResize_NineSlice, true);
        _crSlice.Focusable = false;
        ToolTip.SetShowOnDisabled(_crSlice, true);
        _crSlice.IsCheckedChanged += (_, _) => { if (!_updating) _ctx.CanvasResizeNineSlice = _crSlice.On(); };
        _crSliceFill = Ui.Combo(90, UiStrings.CanvasResize_FillStretch, UiStrings.CanvasResize_FillTile);
        _crSliceFill.Tip(UiStrings.CanvasResize_FillTip);
        _crSliceFill.SelectionChanged += (_, _) =>
        {
            if (!_updating && _crSliceFill.SelectedIndex >= 0) _ctx.CanvasResizeSliceFill = (NineSliceFill)_crSliceFill.SelectedIndex;
        };
        _crWidth = CanvasResizeNum(1, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_74D4C14CA4,
            v => SetCanvasResizeSize(v, null))
            .Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        _crHeight = CanvasResizeNum(1, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_F4AD01A341,
            v => SetCanvasResizeSize(null, v))
            .Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        _crImageWidth = CanvasResizeNum(1, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_FE3628272B,
            v => SetCanvasResizeImage(i => i with { Width = v, Height = _crKeepRatio.On() ? RatioHeight(v) : i.Height }))
            .Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        _crImageHeight = CanvasResizeNum(1, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_CD5D0492CE,
            v => SetCanvasResizeImage(i => i with { Height = v, Width = _crKeepRatio.On() ? RatioWidth(v) : i.Width }))
            .Presets(Ui.SpriteSizes).Unit("px").RecentKey("canvas.size");
        _crX = CanvasResizeNum(-PixelImage.MaxSize, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_63B49FC3F3,
            v => SetCanvasResizeImage(i => i with { X = v }))
            .Presets(Ui.Offsets).Unit("px").RecentKey("offset");
        _crY = CanvasResizeNum(-PixelImage.MaxSize, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_1C39223AA7,
            v => SetCanvasResizeImage(i => i with { Y = v }))
            .Presets(Ui.Offsets).Unit("px").RecentKey("offset");
        _crWidthPercent = Dim("");
        _crHeightPercent = Dim("");
        _crAnchor.Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_A61B0CA3D3);
        _crAnchor.Changed += (_, _) =>
        {
            if (_updating) return;
            var c = _ctx.CanvasResizeCanvas;
            var (x, y) = PixelImage.AnchorOffset(_ctx.CanvasResizeImage.Width, _ctx.CanvasResizeImage.Height, c.Width, c.Height, _crAnchor.Value);
            SetCanvasResizeImage(i => i with { X = x, Y = y });
        };
        var crApply = Ui.Button(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_666F59D00B, (_, _) => _ctx.CommitCanvasResize()).Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_E905102901);
        var crCancel = Ui.Button(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_BDB140E2ED, (_, _) => _ctx.CancelCanvasResize()).Tip(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_495785D5E4);
        _canvasResizeBox = InfoGroup(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_EDE86CE801, UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_8CE505E7D6, Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_4ED6D3C2A9), _crWidth, Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_A5A2849291), _crHeight),
            _crFromCenter,
            Ui.Row(_crSlice, _crSliceFill),
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_42E9812F72), _crImageWidth, _crWidthPercent),
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_EA7C872715), _crImageHeight, _crHeightPercent),
            _crKeepRatio,
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_CD2CB5A3E8), _crX, Ui.Label("Y"), _crY),
            Ui.Row(Ui.Label(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_2B00AF339B), _crAnchor),
            Ui.Row(crApply, crCancel)));

        var stack = Ui.Stack(_canvasResizeBox, _transformBox, _shapeBox, _brushBox, _pencilGradientBox, _fillBox, _gradientBox, _pickerBox, _dodgeBox, _eraserBox, _ditherBox, _textBox, _optionsBox, _mirrorBox, _pivotBox, _sliceBox);
        var header = Ui.Header(UiStrings.S_ToolOptionsPanel_ToolOptionsPanel_E39101186F);
        _title = (TextBlock)header.Child!;
        header.Child = null;
        var headerRow = Ui.Row(_title, _hint);
        headerRow.VerticalAlignment = VerticalAlignment.Center;
        header.Child = headerRow;
        var root = new DockPanel();
        root.Children.Add(header);
        root.Children.Add(Ui.ScrollHost(stack));
        Content = root;

        ctx.ToolChanged += (_, _) => Refresh();
        ctx.ToolOptionsChanged += (_, _) => Refresh();
        ctx.TransformChanged += (_, _) =>
        {
            // 들어가고 나올 때는 보이는 묶음을 바꾸고, 그 밖(끄는 중 등)은 숫자만 맞춘다.
            if (_transformBox.IsVisible != _ctx.IsTransforming) Refresh();
            else RefreshTransform();
        };
        ctx.CanvasResizeChanged += (_, _) =>
        {
            if (_canvasResizeBox.IsVisible != _ctx.IsCanvasResizing) Refresh();
            else RefreshCanvasResize();
        };
        ctx.ProjectReplaced += (_, _) => { RefreshPivot(); RefreshSlice(); };
        ctx.ProjectChanged += (_, _) => { RefreshPivot(); RefreshSlice(); };
        ctx.CurrentChanged += (_, _) => { RefreshPivot(); RefreshSlice(); };
        // 피벗 도구로 끄는 동안(미리보기 값)에도 숫자가 따라가게.
        ctx.ViewChanged += (_, _) => { if (_pivotBox.IsVisible) RefreshPivot(); };
        Refresh();
    }

    // ───────────── 만들기 도우미 ─────────────

    private static TextBlock Dim(string text)
    {
        var t = Ui.Label(text);
        t.Foreground = AppTheme.DimTextBrush;
        t.FontSize = AppTheme.SmallFontSize;
        return t;
    }

    private static TextBlock GroupTitle(Border group) => (TextBlock)((StackPanel)group.Child!).Children[0];

    /// <summary>제목 오른쪽에 긴 설명 "i"(마우스를 올리면 풍선)를 단 묶음 상자.</summary>
    private static Border InfoGroup(string title, string info, Control content)
    {
        var box = Ui.Group(title, content);
        var stack = (StackPanel)box.Child!;
        var head = stack.Children[0];
        stack.Children.RemoveAt(0);
        stack.Children.Insert(0, Ui.Row(head, Ui.InfoBadge(info)));
        return box;
    }

    private CheckBox Check(string text, string tip, Action<bool> apply)
    {
        var c = Ui.Check(text).Tip(tip);
        c.Focusable = false;
        c.IsCheckedChanged += (_, _) => { if (!_updating) apply(c.On()); };
        return c;
    }

    private ComboBox Combo(Action<int> apply)
    {
        var c = Ui.Combo(150, SourceNames);
        c.SelectionChanged += (_, _) => { if (!_updating && c.SelectedIndex >= 0) apply(c.SelectedIndex); };
        return c;
    }

    /// <summary>정수 숫자 칸(좌우로 끌어 바꾸기). 바꾸면 바로 반영한다(도구 설정은 실행취소 없음).</summary>
    private NumericUpDown NumBox(int min, int max, Action<int> apply)
    {
        var num = Ui.Num(min, max, min, 48);
        num.ValueChanged += (_, _) => { if (_updating) return; apply((int)num.NumValue()); };
        return num;
    }

    /// <summary>변형 값 숫자 칸(소수 한 자리, 좌우로 끌어 바꾸기). 바꾸면 바로 변형에 반영한다.</summary>
    private NumericUpDown TransformNum(int min, int max, string tip, Action<double> apply)
    {
        var num = Ui.Num(min, max, 0, 60, 1, 1m);
        num.FormatString = "0.#";
        num.Tip(tip);
        num.ValueChanged += (_, _) => { if (!_updating && num.Value is { } v) apply((double)v); };
        return num;
    }

    /// <summary>캔버스 크기 모드 숫자 칸(정수, 좌우로 끌어 바꾸기). 바꾸면 바로 캔버스 크기 모드에 반영한다.</summary>
    private NumericUpDown CanvasResizeNum(int min, string tip, Action<int> apply)
    {
        var num = Ui.Num(min, PixelImage.MaxSize, 1, 60);
        num.Tip(tip);
        num.ValueChanged += (_, _) => { if (!_updating && num.Value is { } v) apply((int)v); };
        return num;
    }

    /// <summary>캔버스는 그대로 두고 그림 사각형(새 캔버스 좌표)만 바꾼다.</summary>
    private void SetCanvasResizeImage(Func<Rectangle, Rectangle> change)
        => _ctx.SetCanvasResize(_ctx.CanvasResizeCanvas, change(_ctx.CanvasResizeImage));

    /// <summary>숫자 크기 변경: 중심 기준이면 캔버스 원점을 함께 옮기고 그림의 화면 위치는 유지한다.</summary>
    private void SetCanvasResizeSize(int? width, int? height)
    {
        var c = _ctx.CanvasResizeCanvas;
        var image = _ctx.CanvasResizeImage;
        int x = _ctx.CanvasResizeFromCenter && width is not null ? (_ctx.CanvasResizeCenter2.X - width.Value) / 2 : c.X;
        int y = _ctx.CanvasResizeFromCenter && height is not null ? (_ctx.CanvasResizeCenter2.Y - height.Value) / 2 : c.Y;
        image.Offset(c.X - x, c.Y - y);
        _ctx.SetCanvasResize(new Rectangle(x, y, width ?? c.Width, height ?? c.Height), image);
    }

    /// <summary>원래 그림 비율로 그림 가로 <paramref name="width"/>에 맞는 세로(1 이상).</summary>
    private int RatioHeight(int width)
    {
        var s = _ctx.CanvasResizeSource;
        return Math.Max(1, (int)Math.Round((double)width * s.Height / s.Width));
    }

    /// <summary>원래 그림 비율로 그림 세로 <paramref name="height"/>에 맞는 가로(1 이상).</summary>
    private int RatioWidth(int height)
    {
        var s = _ctx.CanvasResizeSource;
        return Math.Max(1, (int)Math.Round((double)height * s.Width / s.Height));
    }

    /// <summary>피벗 픽셀 칸(0.5 단위). 예시 값은 대상 크기에 맞춰 <see cref="RefreshPivot"/>에서 정한다.</summary>
    private static NumericUpDown PixelNum(string key)
    {
        var n = Ui.Num(0, PixelImage.MaxSize, 0, 56, 2, 0.5m).Unit("px").RecentKey(key);
        n.FormatString = "0.##";
        return n;
    }

    /// <summary>9-slice 테두리 칸(px, 네 칸이 최근 값을 함께 씀).</summary>
    private static NumericUpDown SliceNum()
        => Ui.Num(0, PixelImage.MaxSize, 0, 44).Presets(0, 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 24).Unit("px").RecentKey("slice.border");

    /// <summary>크기 <paramref name="size"/>(px)의 대표 위치: 0 · 1/4 · 가운데(내림·정확) · 3/4 · 끝.</summary>
    private static decimal[] Quarters(int size) => [0, size / 4, size / 2, size / 2m, size - size / 4, size];

    // ───────────── 상태 → 화면 ─────────────

    /// <summary>현재 도구에 맞는 묶음만 보이고 값을 맞춘다.</summary>
    private void Refresh()
    {
        var tool = _ctx.Tool;
        bool shape = tool == ToolKind.Shape;
        bool brush = shape || tool is ToolKind.Pencil or ToolKind.GradientPencil or ToolKind.Eraser or ToolKind.Dodge or ToolKind.Dither or ToolKind.ReplaceBrush;
        bool paint = brush || tool == ToolKind.Fill;

        _title.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ToolOptionsPanel_Refresh_2AA5BFEE57,MainForm.ToolName(tool));
        _shapeBox.IsVisible = tool == ToolKind.Shape;
        _brushBox.IsVisible = brush;
        _brushTitle.Text = shape ? UiStrings.S_ToolOptionsPanel_Refresh_FD736DAE6B : UiStrings.S_ToolOptionsPanel_Refresh_5A8AEEEBD3;
        _fillBox.IsVisible = tool is ToolKind.Fill or ToolKind.MagicWand;
        _fillTitle.Text = tool == ToolKind.MagicWand ? UiStrings.S_ToolOptionsPanel_Refresh_EA94B5AD1D : UiStrings.S_ToolOptionsPanel_Refresh_ADEB941184;
        _pickerBox.IsVisible = tool == ToolKind.Picker;
        _dodgeBox.IsVisible = tool == ToolKind.Dodge;
        _eraserBox.IsVisible = tool == ToolKind.Eraser;
        _ditherBox.IsVisible = tool == ToolKind.Dither;
        _textBox.IsVisible = tool == ToolKind.Text;
        _gradientBox.IsVisible = tool == ToolKind.Gradient;
        _pencilGradientBox.IsVisible = tool == ToolKind.GradientPencil;
        _pixelPerfect.IsVisible = tool is ToolKind.Pencil or ToolKind.GradientPencil or ToolKind.Eraser;
        _shapeFilled.IsVisible = tool == ToolKind.Shape && Shapes.CanFill(_ctx.ShapeKind);
        _rightErase.IsVisible = tool is ToolKind.Pencil or ToolKind.GradientPencil or ToolKind.Shape or ToolKind.Fill;
        _wrap.IsVisible = tool == ToolKind.Move;
        _optionsBox.IsVisible = _pixelPerfect.IsVisible || _shapeFilled.IsVisible || _rightErase.IsVisible || _wrap.IsVisible;
        _mirrorBox.IsVisible = paint;
        _pivotBox.IsVisible = tool == ToolKind.Pivot;
        _sliceBox.IsVisible = tool == ToolKind.Slice;
        var hint = tool switch
        {
            ToolKind.Select => UiStrings.S_ToolOptionsPanel_Refresh_147A90B5B7,
            ToolKind.EllipseSelect => UiStrings.S_ToolOptionsPanel_Refresh_E1081EA049,
            ToolKind.Lasso => UiStrings.S_ToolOptionsPanel_Refresh_84BF1C9645,
            ToolKind.MagicWand => UiStrings.S_ToolOptionsPanel_Refresh_E90544A4CC,
            ToolKind.Move => UiStrings.S_ToolOptionsPanel_Refresh_00F0C73AC7,
            ToolKind.Gradient => UiStrings.S_ToolOptionsPanel_Refresh_FB6D406AF9,
            ToolKind.Text => UiStrings.TextTool_Info,
            _ => "",
        };
        ToolTip.SetTip(_hint, hint);
        _hint.IsVisible = hint.Length > 0;
        _transformBox.IsVisible = _ctx.IsTransforming;
        _canvasResizeBox.IsVisible = _ctx.IsCanvasResizing;
        if (_ctx.IsTransforming || _ctx.IsCanvasResizing)
        {
            // 변형·캔버스 크기 모드 중에는 그 값만 보인다.
            _title.Text = _ctx.IsTransforming ? UiStrings.S_ToolOptionsPanel_Refresh_DD7EA3035B : UiStrings.S_ToolOptionsPanel_Refresh_6281D2CC20;
            foreach (var box in new Control[] { _shapeBox, _brushBox, _pencilGradientBox, _fillBox, _gradientBox, _pickerBox, _dodgeBox, _eraserBox, _ditherBox, _textBox, _optionsBox, _mirrorBox, _pivotBox, _sliceBox, _hint })
                box.IsVisible = false;
        }

        _updating = true;
        try
        {
            _shapes.SelectedIndex = Array.FindIndex(BrushShapes, s => s.Shape == _ctx.BrushShape);
            _sizeNum.Value = _ctx.BrushSize;
            _brushPreview.Footprint = _ctx.Brush;
            _tolNum.Value = _ctx.FillTolerance;
            _contiguous.IsChecked = _ctx.FillContiguous;
            _diagonal.IsChecked = _ctx.FillDiagonal;
            _diagonal.IsEnabled = _ctx.FillContiguous;
            _fillSource.SelectedIndex = _ctx.FillSampleVisible ? 1 : 0;
            _pickSource.SelectedIndex = _ctx.PickerSampleVisible ? 1 : 0;
            _dodgeNum.Value = _ctx.DodgeStrength;
            _eraserNum.Value = _ctx.EraserStrength;
            _eraseRed.IsChecked = _ctx.EraserRed;
            _eraseGreen.IsChecked = _ctx.EraserGreen;
            _eraseBlue.IsChecked = _ctx.EraserBlue;
            _ditherPattern.SelectedIndex = (int)_ctx.DitherPattern;
            _textFont.SelectedIndex = (int)_ctx.TextFont;
            _pixelPerfect.IsChecked = _ctx.PixelPerfect;
            _shapeFilled.IsChecked = _ctx.ShapeFilled;
            _rightErase.IsChecked = _ctx.RightButtonErases;
            _wrap.IsChecked = _ctx.MoveWrap;
            _mirrorX.IsChecked = _ctx.MirrorX;
            _mirrorY.IsChecked = _ctx.MirrorY;
            _mirrorAtPivot.IsChecked = _ctx.MirrorAtPivot;
            _mirrorAtPivot.IsEnabled = _ctx.MirrorX || _ctx.MirrorY;
            _pencilGradientMode.SelectedIndex = (int)_ctx.PencilGradientMode;
            _pencilIntervalNum.Value = _ctx.PencilGradientInterval;
            _pencilIntervalRow.IsVisible = _ctx.PencilGradientMode == GradientStrokeMode.Repeat;
            if (!_pencilGradientSyncing) _pencilGradientEditor.LoadPreset(_ctx.PencilGradient);
            RefreshShape();
            if (!_gradientSyncing)
                _gradientEditor.Load(_ctx.GradientStops, _ctx.GradientAlphaStops,
                    _ctx.GradientInterpolation, _ctx.GradientDither, colorSpace: _ctx.GradientColorSpace);
        }
        finally
        {
            _updating = false;
        }
        RefreshPivot();
        RefreshSlice();
        RefreshTransform();
        RefreshCanvasResize();
    }

    /// <summary>도형 모양 단추(고른 것 = 강조색, 채움이면 아이콘도 채움)·안내·모양별 개수 칸.</summary>
    private void RefreshShape()
    {
        var kind = _ctx.ShapeKind;
        for (int i = 0; i < ShapeDefs.Length; i++)
        {
            var def = ShapeDefs[i];
            _shapeButtons[i].Background = def.Kind == kind ? AppTheme.AccentBrush : AppTheme.InputBrush;
            _shapeButtons[i].Content = Ui.Icon(def.Icon, 16, filled: _ctx.ShapeFilled && Shapes.CanFill(def.Kind));
        }
        _shapeHint.Text = ShapeDefs.First(d => d.Kind == kind).Hint;
        _sidesRow.IsVisible = kind == ShapeKind.Polygon;
        _starRow.IsVisible = kind == ShapeKind.Star;
        _turnsRow.IsVisible = kind == ShapeKind.Spiral;
        _lineStepsRow.IsVisible = kind is ShapeKind.Line or ShapeKind.Curve;
        _lineSteps.SelectedIndex = _ctx.ShapeOrthogonal ? 1 : 0;
        _sidesNum.Value = _ctx.ShapeSides;
        _starNum.Value = _ctx.StarPoints;
        _turnsNum.Value = _ctx.SpiralTurns;
    }

    // ───────────── 변형 ─────────────

    /// <summary>변형 값을 숫자 칸에 보인다(변형 중일 때만).</summary>
    private void RefreshTransform()
    {
        if (!_transformBox.IsVisible || _ctx.Transform is not { } t) return;
        var p = _ctx.TransformParams;
        bool wasUpdating = _updating;
        _updating = true;
        try
        {
            static decimal Num(double v, NumericUpDown n) => Math.Clamp((decimal)Math.Round(v, 1), n.Minimum, n.Maximum);
            _tfAngle.Value = Num(p.Angle, _tfAngle);
            _tfScaleX.Value = Num(p.ScaleX * 100, _tfScaleX);
            _tfScaleY.Value = Num(p.ScaleY * 100, _tfScaleY);
            _tfX.Value = Num(p.CenterX, _tfX);
            _tfY.Value = Num(p.CenterY, _tfY);
            _tfX.Presets(Quarters(Math.Max(1, _ctx.CanvasWidth)));
            _tfY.Presets(Quarters(Math.Max(1, _ctx.CanvasHeight)));
            _tfResample.SelectedIndex = (int)_ctx.TransformResample;
            var src = t.SourceBounds;
            var dst = _ctx.Selection.Bounds;
            bool fallback = _ctx.TransformResample == ResampleMode.RotSprite && (long)src.Width * src.Height > FreeTransform.MaxRotSpriteArea;
            _tfInfo.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ToolOptionsPanel_RefreshTransform_DB65681040,src.Width,src.Height,dst.Width,dst.Height)
                           + (fallback ? UiStrings.S_ToolOptionsPanel_RefreshTransform_08535E328B : "");
        }
        finally
        {
            _updating = wasUpdating;
        }
    }

    // ───────────── 캔버스 크기 ─────────────

    /// <summary>캔버스 크기 모드 값을 숫자 칸·기준점에 보인다(모드일 때만). 그림 자리가 9방향 중 하나와 맞으면 그 단추가 눌린다.</summary>
    private void RefreshCanvasResize()
    {
        if (!_canvasResizeBox.IsVisible || !_ctx.IsCanvasResizing) return;
        var c = _ctx.CanvasResizeCanvas;
        var i = _ctx.CanvasResizeImage;
        var s = _ctx.CanvasResizeSource;
        bool wasUpdating = _updating;
        _updating = true;
        try
        {
            _crWidth.Value = c.Width;
            _crHeight.Value = c.Height;
            _crFromCenter.IsChecked = _ctx.CanvasResizeFromCenter;
            bool available = _ctx.CanvasResizeSliceAvailable;
            _crSlice.IsEnabled = available;
            _crSlice.IsChecked = available && _ctx.CanvasResizeNineSlice;
            _crSlice.Tip(available ? UiStrings.CanvasResize_NineSliceTip : UiStrings.CanvasResize_NineSliceNone);
            _crSliceFill.IsEnabled = _ctx.CanvasResizeUsesSlice;
            _crSliceFill.SelectedIndex = (int)_ctx.CanvasResizeSliceFill;
            _crImageWidth.Value = i.Width;
            _crImageHeight.Value = i.Height;
            _crX.Value = Math.Clamp(i.X, _crX.Minimum, _crX.Maximum);
            _crY.Value = Math.Clamp(i.Y, _crY.Minimum, _crY.Maximum);
            _crWidthPercent.Text = $"{i.Width * 100.0 / s.Width:0.#}%";
            _crHeightPercent.Text = $"{i.Height * 100.0 / s.Height:0.#}%";
            var anchors = Enum.GetValues<Anchor>();
            int match = Array.FindIndex(anchors, a => PixelImage.AnchorOffset(i.Width, i.Height, c.Width, c.Height, a) == (i.X, i.Y));
            _crAnchor.Show(match < 0 ? null : anchors[match]);
        }
        finally
        {
            _updating = wasUpdating;
        }
    }

    // ───────────── 9-slice ─────────────

    /// <summary>9-slice 대상(부위 = 그 부위, 그 밖 = 프레임 시트 모든 칸)의 테두리를 보인다. 9-slice 도구일 때만.</summary>
    private void RefreshSlice()
    {
        if (!_sliceBox.IsVisible) return;
        bool group = _ctx.IsGroupSelected;
        _sliceBox.IsEnabled = _ctx.HasProject;
        // 그룹 화면: 부위마다 캔버스의 초록 선을 끌어 바꾼다(숫자는 부위 하나를 고르면).
        foreach (var c in new Control[] { _sL, _sT, _sR, _sB, _sliceClear }) c.IsEnabled = !group;
        if (_ctx.Project is null) return;
        bool wasUpdating = _updating;
        _updating = true;
        try
        {
            var b = _ctx.SliceBorder;
            int w = Math.Max(0, _ctx.CanvasWidth), h = Math.Max(0, _ctx.CanvasHeight);
            _sL.Maximum = _sR.Maximum = Math.Max(0, w);
            _sT.Maximum = _sB.Maximum = Math.Max(0, h);
            _sL.Value = Math.Clamp(b.Left, 0, w);
            _sR.Value = Math.Clamp(b.Right, 0, w);
            _sT.Value = Math.Clamp(b.Top, 0, h);
            _sB.Value = Math.Clamp(b.Bottom, 0, h);
            _sliceTarget.Text = group
                ? UiStrings.S_ToolOptionsPanel_RefreshSlice_4979FEE313
                : _ctx.CurrentPart is { } part
                    ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ToolOptionsPanel_RefreshSlice_6437D6774C,part.Name,part.PartSize.Width,part.PartSize.Height)
                    : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ToolOptionsPanel_RefreshSlice_0CBE78E12F,w,h);
        }
        finally
        {
            _updating = wasUpdating;
        }
    }

    // ───────────── 피벗(픽셀) ─────────────

    /// <summary>피벗 대상의 픽셀 크기: 부위면 부위 크기, 아니면 프레임(캔버스) 크기.</summary>
    private int TargetWidth => Math.Max(1, _ctx.CanvasWidth);
    private int TargetHeight => Math.Max(1, _ctx.CanvasHeight);

    /// <summary>저장된 대상 피벗(부위 = 그 부위, 그 밖 = 현재 프레임/기본값).</summary>
    private Pivot? TargetPivot()
    {
        if (_ctx.Project is not { } p) return null;
        return _ctx.CurrentPart?.PartPivot ?? p.GetFramePivot(_ctx.CurrentFrame);
    }

    /// <summary>
    /// 5×5 단추(<paramref name="col"/>, <paramref name="row"/> = 0~4, 위 왼쪽부터)를 정수 픽셀에 맞춘 피벗:
    /// 0 / 1/4 / 1/2 / 3/4 / 끝(가운데는 내림 15px → 7px, 1/4 = 16px → 4px, 3/4 = 너비 − 1/4 → 12px, 오른쪽 = 너비, 위 = 높이).
    /// </summary>
    private Pivot SnappedStep(int col, int row)
    {
        int w = TargetWidth, h = TargetHeight;
        static int Snap(int step, int size) => step switch
        {
            0 => 0,
            1 => size / 4,
            2 => size / 2,
            3 => size - size / 4,
            _ => size,
        };
        return new Pivot(Snap(col, w) / (float)w, Snap(PivotSteps - 1 - row, h) / (float)h);
    }

    /// <summary>피벗 대상(부위 = 그 부위, 그 밖 = 현재 프레임/기본값)을 화면에 보인다. 피벗 도구일 때만.</summary>
    private void RefreshPivot()
    {
        _pivotBox.IsEnabled = _ctx.HasProject;
        if (!_pivotBox.IsVisible || _ctx.Project is not { } p) return;
        bool wasUpdating = _updating;
        _updating = true;
        try
        {
            if (_ctx.CurrentPart is { } part)
            {
                _framePivot.IsVisible = false;
                _pivotTarget.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ToolOptionsPanel_RefreshPivot_7752FB81E5,part.Name,part.PartSize.Width,part.PartSize.Height);
            }
            else
            {
                int f = _ctx.CurrentFrame;
                bool own = p.Frames[f].Pivot is not null;
                _framePivot.IsVisible = true;
                _framePivot.IsChecked = own;
                _pivotTarget.Text = own ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ToolOptionsPanel_RefreshPivot_DC250333A2,f + 1) : UiStrings.S_ToolOptionsPanel_RefreshPivot_880B20EF26;
            }
            // 끄는 중이면 미리보기 값.
            var pivot = _ctx.Pivot;
            int w = TargetWidth, h = TargetHeight;
            _pivotX.Maximum = w;
            _pivotY.Maximum = h;
            _pivotX.Presets(Quarters(w));
            _pivotY.Presets(Quarters(h));
            _pivotX.Value = Math.Round((decimal)Math.Clamp(pivot.X * w, 0, w), 2);
            _pivotY.Value = Math.Round((decimal)Math.Clamp(pivot.Y * h, 0, h), 2);
            _pivotXMax.Text = $"/ {w} px";
            _pivotYMax.Text = $"/ {h} px";
            for (int i = 0; i < _presetButtons.Length; i++)
            {
                int col = i % PivotSteps, row = i / PivotSteps;
                var exact = new Pivot(col / 4f, (PivotSteps - 1 - row) / 4f);
                var snapped = SnappedStep(col, row);
                bool on = pivot == exact
                    || (Math.Abs((pivot.X - snapped.X) * w) < 0.01f && Math.Abs((pivot.Y - snapped.Y) * h) < 0.01f);
                _presetButtons[i].Background = on ? AppTheme.AccentBrush : AppTheme.InputBrush;
            }
        }
        finally
        {
            _updating = wasUpdating;
        }
    }

    private void ApplyPivot(Pivot pivot)
    {
        if (_updating || _ctx.Project is not { } p) return;
        if (_ctx.CurrentPart is { } part)
        {
            int index = p.IndexOf(part);
            if (part.PartPivot != pivot) _ctx.Structural(() => p.SetPartPivot(index, pivot));
            return;
        }
        int f = _ctx.CurrentFrame;
        if (p.Frames[f].Pivot is not null) _ctx.Structural(() => p.SetFramePivot(f, pivot));
        else if (p.Sprite.DefaultPivot != pivot)
            _ctx.Structural(() => p.Edit(UiStrings.S_ToolOptionsPanel_ApplyPivot_4E8342E979, ProjectChange.Settings, () => p.Sprite.DefaultPivot = pivot));
    }

    /// <summary>브러시 도장을 칸(픽셀)으로 그린 작은 미리보기.</summary>
    private sealed class BrushPreview : Control
    {
        private const double Box = 66;
        private BrushFootprint? _footprint;

        public BrushPreview()
        {
            Width = Box;
            Height = Box;
        }

        public BrushFootprint? Footprint
        {
            get => _footprint;
            set { if (ReferenceEquals(_footprint, value)) return; _footprint = value; InvalidateVisual(); }
        }

        public override void Render(DrawingContext g)
        {
            var area = new Rect(0, 0, Box, Box);
            g.FillRectangle(AppTheme.BackBrush, area);
            g.DrawRectangle(null, AppTheme.Pen(AppTheme.BorderColor), area.Deflate(0.5));
            if (_footprint is not { } fp) return;
            double cell = Math.Clamp(Math.Floor((Box - 2) / fp.Size), 1, 12);
            double size = cell * fp.Size;
            double ox = Math.Floor((Box - size) / 2), oy = Math.Floor((Box - size) / 2);
            int start = fp.Start;
            foreach (var o in fp.Offsets)
                g.FillRectangle(AppTheme.TextBrush, new Rect(ox + (o.X + start) * cell, oy + (o.Y + start) * cell, cell, cell));
        }
    }
}
