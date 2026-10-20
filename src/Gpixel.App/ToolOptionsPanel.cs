using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 오른쪽 독 "도구 설정": 고른 도구의 세부 설정만 보인다. 브러시(모양·크기·미리보기), 채우기(허용 오차·이어진 영역·대각선·기준),
/// 스포이드(기준), 밝게/어둡게(세기), 디더링(무늬), 그라데이션(색 정지점 바·보간·디더링), 도형(채움), 이동(순환), 대칭, 피벗 도구(K)의 피벗(픽셀 단위), 9-slice 도구(S)의 테두리(L·T·R·B).
/// 변형 모드(Ctrl+T) 중에는 변형 값(각도·가로·세로 %·중심 X/Y)·리샘플링·적용/취소만 보인다(캔버스 손잡이와 함께 움직임).
/// 캔버스 크기 모드(Ctrl+Alt+C) 중에는 캔버스 가로·세로, 그림 가로·세로(%)·위치, 기준점 9방향, 비율 유지, 확인/취소만 보인다(캔버스 손잡이와 함께 움직임).
/// 도구 설정은 <see cref="EditorContext"/>에 있고(실행취소 없음) 바뀌면 <see cref="EditorContext.ToolOptionsChanged"/>로 다시 맞춘다.
/// 피벗 값 바꾸기는 프로젝트 편집이라 실행취소된다.
/// </summary>
public sealed class ToolOptionsPanel : UserControl
{
    /// <summary>피벗 5×5 단추 칸 수(한 변). 칸 사이 간격 = 1/4.</summary>
    private const int PivotSteps = 5;
    /// <summary>바깥 8방향·가운데 단추 글자(그 밖은 "·"), 위 왼쪽부터 행 순서.</summary>
    private static readonly Dictionary<(int Col, int Row), string> PivotArrows = new()
    {
        [(0, 0)] = "↖", [(2, 0)] = "↑", [(4, 0)] = "↗",
        [(0, 2)] = "←", [(2, 2)] = "●", [(4, 2)] = "→",
        [(0, 4)] = "↙", [(2, 4)] = "↓", [(4, 4)] = "↘",
    };
    private static readonly (BrushShape Shape, string Text, string Tip)[] Shapes =
    [
        (BrushShape.Square, "■ 사각", "사각 브러시"),
        (BrushShape.Circle, "● 원", "원 브러시(크기 3 이상부터 둥글게)"),
        (BrushShape.Diamond, "◆ 마름모", "마름모 브러시(크기 3 이상부터)"),
    ];
    private static readonly string[] SourceNames = ["현재 레이어", "보이는 레이어 전체"];

    private readonly EditorContext _ctx;
    private readonly TextBlock _title;
    private bool _updating;

    // 브러시
    private readonly Border _brushBox;
    private readonly TextBlock _brushTitle, _fillTitle;
    /// <summary>브러시 모양(<see cref="Shapes"/> 순서). 클릭해도 캔버스 포커스를 빼앗지 않는다.</summary>
    private readonly TabStrip _shapes = new(TabStripKind.Segmented, [.. Shapes.Select(s => s.Text)]) { Focusable = false, IsTabStop = false, Margin = default };
    private readonly NumericUpDown _sizeNum;
    private readonly BrushPreview _brushPreview = new();

    // 도구별
    private readonly Border _fillBox, _pickerBox, _dodgeBox, _ditherBox, _optionsBox, _mirrorBox;
    private readonly NumericUpDown _tolNum, _dodgeNum;
    private readonly CheckBox _contiguous, _diagonal, _pixelPerfect, _shapeFilled, _rightErase, _wrap, _mirrorX, _mirrorY;
    private readonly ComboBox _fillSource, _pickSource, _ditherPattern;
    private readonly TextBlock _hint;

    // 그라데이션
    private readonly Border _gradientBox;
    private readonly GradientBar _gradientBar;
    private readonly NumericUpDown _stopPos, _stopAlpha;
    private readonly ColorField _stopColor;
    private readonly StackPanel _stopColorRow, _stopAlphaRow;
    private readonly Button _stopDelete;
    private readonly ComboBox _gradientMode, _gradientDither;

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
    private readonly CheckBox _crKeepRatio;
    private readonly AnchorPicker _crAnchor = new();

    public ToolOptionsPanel(EditorContext ctx)
    {
        _ctx = ctx;
        Background = AppTheme.PanelBrush;

        // ── 브러시: 모양 단추 + 크기(슬라이더·숫자) + 미리보기
        for (int i = 0; i < Shapes.Length; i++) _shapes.SetTip(i, Shapes[i].Tip);
        _shapes.SelectionChanged += (_, _) =>
        {
            if (_updating || _shapes.SelectedIndex < 0) return;
            _ctx.BrushShape = Shapes[_shapes.SelectedIndex].Shape;
            Refresh();
        };
        _sizeNum = NumBox(1, BrushFootprint.MaxSize, v => _ctx.BrushSize = v);
        _sizeNum.Tip($"브러시 크기 1~{BrushFootprint.MaxSize} — 좌우로 끌거나 [ / ]");
        var brushRight = new StackPanel { Spacing = 4, Children = { _shapes, Ui.Row(Ui.Label("크기"), _sizeNum) } };
        _brushPreview.Margin = new Thickness(0, 0, 6, 0);
        _brushPreview.VerticalAlignment = VerticalAlignment.Top;
        _brushPreview.Tip("브러시 모양 미리보기(한 칸 = 한 픽셀)");
        Grid.SetColumn(brushRight, 1);
        var brushGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { _brushPreview, brushRight } };
        _brushBox = Ui.Group("브러시", Ui.Stack(brushGrid));
        _brushTitle = GroupTitle(_brushBox);

        // ── 채우기
        _tolNum = NumBox(0, 100, v => _ctx.FillTolerance = v);
        _tolNum.Tip("허용 오차(%): 0 = 정확히 같은 색만. 높일수록 비슷한 색(RGBA 채널 차이의 최댓값이 이 비율×255 이하)도 같은 색으로 채웁니다.");
        _contiguous = Check("이어진 영역만", "끄면 화면 안의 맞는 색을 모두 고릅니다(채우기·스마트 선택) (Shift+G)", v => _ctx.FillContiguous = v);
        _diagonal = Check("대각선으로도 잇기(8방향)", "이어진 영역을 대각선 이웃까지 넓힙니다", v => _ctx.FillDiagonal = v);
        _fillSource = Combo(v => _ctx.FillSampleVisible = v == 1);
        _fillSource.Tip("영역을 정하는 그림. 보이는 레이어 전체면 합성된 모습으로 경계를 찾고 칠하기는 현재 레이어에 합니다.");
        _fillBox = Ui.Group("채우기", Ui.Stack(
            Ui.Row(Ui.Label("허용 오차"), _tolNum, Ui.Label("%")),
            _contiguous,
            _diagonal,
            Ui.Row(Ui.Label("기준"), _fillSource)));
        _fillTitle = GroupTitle(_fillBox);

        // ── 스포이드
        _pickSource = Combo(v => _ctx.PickerSampleVisible = v == 1);
        _pickSource.Tip("색을 집을 그림(Alt+클릭 임시 스포이드도 같음)");
        _pickerBox = Ui.Group("스포이드", Ui.Stack(Ui.Row(Ui.Label("기준"), _pickSource), Dim("Alt+클릭으로 쓰는 임시 스포이드에도 적용됩니다.")));

        // ── 밝게/어둡게
        _dodgeNum = NumBox(1, EditorContext.MaxDodgeStrength, v => _ctx.DodgeStrength = v);
        _dodgeNum.Tip("한 번에 바꿀 명도(%) — 한 획에 한 픽셀은 한 번만");
        _dodgeBox = Ui.Group("밝게/어둡게", Ui.Stack(Ui.Row(Ui.Label("세기"), _dodgeNum, Ui.Label("%"))));

        // ── 디더링
        _ditherPattern = Ui.Combo(150, "체커 (50%)", "성기게 (25%)", "촘촘하게 (75%)");
        _ditherPattern.SelectionChanged += (_, _) =>
        {
            if (_updating || _ditherPattern.SelectedIndex < 0) return;
            _ctx.DitherPattern = (DitherPattern)_ditherPattern.SelectedIndex;
        };
        _ditherPattern.Tip("기본색이 차지하는 비율(2×2 순서 디더링). 나머지는 보조색");
        _ditherBox = Ui.Group("디더링", Ui.Stack(Ui.Row(Ui.Label("무늬"), _ditherPattern)));

        // ── 그라데이션: 바(위 = 불투명도 정지점, 아래 = 색 정지점) + 고른 정지점의 위치·색/불투명도 + 보간·디더링
        _gradientBar = new GradientBar { Margin = new Thickness(0, 2, 0, 2) };
        _gradientBar.Tip("띠 위쪽 빈 곳 = 불투명도 정지점 추가 · 띠·아래쪽 빈 곳 = 색 정지점 추가 · 상자를 누르면 고르기, 좌우로 끌면 위치 · Delete: 고른 정지점 삭제(종류마다 2개는 남음)");
        _gradientBar.StopsChanged += stops => _ctx.GradientStops = stops;
        _gradientBar.AlphaStopsChanged += stops => _ctx.GradientAlphaStops = stops;
        _gradientBar.SelectionChanged += (_, _) => RefreshGradientStop();
        _stopPos = Ui.Num(0, 100, 0, 56, 1, 1m);
        _stopPos.FormatString = "0.#";
        _stopPos.Tip("고른 정지점의 위치(%, 왼쪽 0 ~ 오른쪽 100)");
        _stopPos.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            double pos = (double)_stopPos.NumValue() / 100;
            if (_gradientBar.SelectedIsAlpha) SetSelectedAlphaStop(s => s with { Position = pos });
            else SetSelectedStop(s => s with { Position = pos });
        };
        _stopColor = new ColorField(Color32.Black);
        _stopColor.Tip("고른 색 정지점의 색(칸을 누르면 색 고르기)");
        _stopColor.ColorChanged += (_, _) => { if (!_updating) SetSelectedStop(s => s with { Color = _stopColor.Color }); };
        var usePrimary = Ui.Button("기본색", (_, _) => SetSelectedStop(s => s with { Color = _ctx.Primary })).Tip("고른 색 정지점을 지금 기본색으로");
        _stopAlpha = Ui.Num(0, 100, 100, 56, 1, 1m);
        _stopAlpha.FormatString = "0.#";
        _stopAlpha.Tip("고른 불투명도 정지점의 불투명도(%, 0 = 투명 ~ 100 = 불투명)");
        _stopAlpha.ValueChanged += (_, _) =>
        {
            if (!_updating) SetSelectedAlphaStop(s => s with { Alpha = (byte)Math.Round((double)_stopAlpha.NumValue() * 255 / 100) });
        };
        _stopColorRow = Ui.Row(Ui.Label("색"), _stopColor, usePrimary);
        _stopAlphaRow = Ui.Row(Ui.Label("불투명도"), _stopAlpha, Ui.Label("%"));
        _stopDelete = Ui.Button("삭제", (_, _) => _gradientBar.DeleteSelected()).Tip("고른 정지점 삭제(Delete) — 종류마다 2개 이상 남습니다");
        _gradientMode = Ui.Combo(150, "부드럽게", "단계(띠)");
        _gradientMode.Tip("부드럽게: 이웃한 정지점 색을 RGBA(알파 포함)로 섞기 · 단계: 가장 가까운 정지점 색만 쓰는 띠");
        _gradientMode.SelectionChanged += (_, _) =>
        {
            if (_updating || _gradientMode.SelectedIndex < 0) return;
            _ctx.GradientInterpolation = (GradientInterpolation)_gradientMode.SelectedIndex;
        };
        _gradientDither = Ui.Combo(150, "없음", "2×2 Bayer", "4×4 Bayer");
        _gradientDither.Tip("부드럽게일 때 이웃한 두 정지점 색만 순서 디더링 무늬로 섞습니다(픽셀 아트용)");
        _gradientDither.SelectionChanged += (_, _) =>
        {
            if (_updating || _gradientDither.SelectedIndex < 0) return;
            _ctx.GradientDither = (GradientDither)_gradientDither.SelectedIndex;
        };
        var gradientHelp = Dim("캔버스에서 시작점 → 끝점으로 끌어 칠합니다(Shift: 45°). 선택 영역이 있으면 그 안만 칠하고, 불투명도가 낮은 쪽은 아래를 그만큼 투명하게 바꿉니다.");
        gradientHelp.TextWrapping = TextWrapping.Wrap;
        _gradientBox = Ui.Group("그라데이션", Ui.Stack(
            _gradientBar,
            Ui.Row(Ui.Label("위치"), _stopPos, Ui.Label("%"), _stopDelete),
            _stopColorRow,
            _stopAlphaRow,
            Ui.Row(Ui.Label("보간"), _gradientMode),
            Ui.Row(Ui.Label("디더링"), _gradientDither),
            gradientHelp));

        // ── 옵션(도구마다 보이는 것만)
        _pixelPerfect = Check("픽셀 퍼펙트", "1px 연필·지우개의 L자 모서리 제거 (P)", v => _ctx.PixelPerfect = v);
        _shapeFilled = Check("채움", "사각형·원을 채워 그리기 (U)", v => _ctx.ShapeFilled = v);
        _rightErase = Check("오른쪽 버튼 = 지우개", "오른쪽 버튼으로 보조색 대신 지우기", v => _ctx.RightButtonErases = v);
        _wrap = Check("순환", "이동: 밖으로 나간 픽셀을 반대편으로", v => _ctx.MoveWrap = v);
        _optionsBox = Ui.Group("옵션", Ui.Stack(_pixelPerfect, _shapeFilled, _rightErase, _wrap));

        // ── 대칭
        _mirrorX = Check("좌우 대칭 ↔", "좌우 대칭 그리기 (Shift+X)", v => _ctx.MirrorX = v);
        _mirrorY = Check("상하 대칭 ↕", "상하 대칭 그리기 (Shift+Y)", v => _ctx.MirrorY = v);
        _mirrorBox = Ui.Group("대칭 그리기", Ui.Stack(_mirrorX, _mirrorY));

        // ── 피벗(픽셀: X는 왼쪽에서, Y는 아래에서)
        // 5×5: 가로·세로 0, 1/4, 1/2, 3/4, 1 지점(위 왼쪽 단추 = 왼쪽 위).
        var stepDefs = string.Join(",", Enumerable.Repeat("Auto", PivotSteps));
        var presetGrid = new Grid { ColumnDefinitions = new ColumnDefinitions(stepDefs), RowDefinitions = new RowDefinitions(stepDefs), Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        string[] quarters = ["0", "1/4", "1/2", "3/4", "1"];
        for (int i = 0; i < _presetButtons.Length; i++)
        {
            int col = i % PivotSteps, row = i / PivotSteps;
            var b = Ui.Button(PivotArrows.GetValueOrDefault((col, row), "·"), (_, _) => ApplyPivot(SnappedStep(col, row)), 26);
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
        _pivotX = PixelNum();
        _pivotY = PixelNum();
        _pivotX.Tip("왼쪽에서 몇 px(0.5 단위)");
        _pivotY.Tip("아래에서 몇 px(0.5 단위, Unity와 같이 아래가 0)");
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
        _framePivot = Ui.Check("현재 프레임만 따로");
        _framePivot.IsCheckedChanged += (_, _) =>
        {
            if (_updating || _ctx.Project is not { } p) return;
            int f = _ctx.CurrentFrame;
            _ctx.Structural(() => p.SetFramePivot(f, _framePivot.On() ? p.GetFramePivot(f) : null));
        };
        _pivotTarget = Dim("");
        _pivotTarget.TextWrapping = TextWrapping.Wrap;
        _pivotBox = Ui.Group("피벗(포커스) — 캔버스에서 끌기", Ui.Stack(
            presetGrid,
            Ui.Row(Ui.Label("X"), _pivotX, _pivotXMax),
            Ui.Row(Ui.Label("Y"), _pivotY, _pivotYMax),
            _framePivot,
            _pivotTarget));

        // ── 9-slice(S): 캔버스에서 초록 선을 끌거나 숫자로
        _sL = Ui.Num(0, PixelImage.MaxSize, 0, 44);
        _sT = Ui.Num(0, PixelImage.MaxSize, 0, 44);
        _sR = Ui.Num(0, PixelImage.MaxSize, 0, 44);
        _sB = Ui.Num(0, PixelImage.MaxSize, 0, 44);
        foreach (var n in new[] { _sL, _sT, _sR, _sB })
            n.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                _ctx.SetSliceBorder(new SpriteBorder((int)_sL.NumValue(), (int)_sT.NumValue(), (int)_sR.NumValue(), (int)_sB.NumValue()));
            };
        _sliceTarget = Dim("");
        _sliceTarget.TextWrapping = TextWrapping.Wrap;
        var sliceHelp = Dim("크기를 늘려도 초록 선 바깥(모서리·가장자리)은 늘어나지 않고 가운데만 늘어납니다. Unity에서 Image Type = Sliced로 쓰는 UI 버튼·패널용이고, 0이면 꺼짐입니다.");
        sliceHelp.TextWrapping = TextWrapping.Wrap;
        _sliceClear = Ui.Button("모두 0", (_, _) => _ctx.SetSliceBorder(SpriteBorder.None)).Tip("테두리 지우기(9-slice 끄기)");
        _sliceBox = Ui.Group("9-slice 테두리(px) — 초록 선 끌기", Ui.Stack(
            Ui.Row(Ui.Label("L"), _sL, Ui.Label("R"), _sR),
            Ui.Row(Ui.Label("T"), _sT, Ui.Label("B"), _sB),
            _sliceClear,
            _sliceTarget,
            sliceHelp));

        _hint = Dim("");
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.Margin = new Thickness(6, 6, 6, 6);

        // ── 변형 모드(Ctrl+T): 숫자는 캔버스 손잡이와 함께 움직인다.
        _tfAngle = TransformNum(-180, 180, "회전 각도(°, 시계 방향) — 회전 중심(캔버스의 주황 표시)을 기준으로",
            v => _ctx.SetTransformParams(_ctx.RotatedAboutPivot(_ctx.TransformParams, v)));
        _tfScaleX = TransformNum(-3200, 3200, "가로 크기(%) — 음수 = 좌우 뒤집기, 중심 기준",
            v => { if (v != 0) _ctx.SetTransformParams(_ctx.TransformParams with { ScaleX = v / 100 }); });
        _tfScaleY = TransformNum(-3200, 3200, "세로 크기(%) — 음수 = 상하 뒤집기, 중심 기준",
            v => { if (v != 0) _ctx.SetTransformParams(_ctx.TransformParams with { ScaleY = v / 100 }); });
        _tfX = TransformNum(-PixelImage.MaxSize, PixelImage.MaxSize, "변형한 영역 중심의 X(픽셀, 크기가 홀수면 .5)",
            v => _ctx.SetTransformParams(_ctx.TransformParams with { CenterX = v }));
        _tfY = TransformNum(-PixelImage.MaxSize, PixelImage.MaxSize, "변형한 영역 중심의 Y(픽셀, 위가 0)",
            v => _ctx.SetTransformParams(_ctx.TransformParams with { CenterY = v }));
        _tfResample = Ui.Combo(150, "RotSprite", "최근접(Nearest)");
        _tfResample.Tip("RotSprite: Scale2x를 세 번(8배) 키운 그림에서 집어 회전한 선이 덜 깨집니다(섞지 않아 새 색 없음). 최근접: 원본 픽셀을 그대로 집습니다. 0°·90°·180°·270°는 둘이 같습니다.");
        _tfResample.SelectionChanged += (_, _) =>
        {
            if (_updating || _tfResample.SelectedIndex < 0) return;
            _ctx.TransformResample = (ResampleMode)_tfResample.SelectedIndex;
        };
        _tfInfo = Dim("");
        var tfApply = Ui.Button("적용", (_, _) => _ctx.CommitTransform()).Tip("변형을 적용합니다(Enter) — 실행취소 한 단계");
        var tfCancel = Ui.Button("취소", (_, _) => _ctx.CancelTransform()).Tip("변형 전으로 되돌립니다(Esc, Ctrl+Z)");
        var tfHelp = Dim("캔버스: 안쪽 끌기 = 옮기기(1px 단위), 네모 손잡이 = 크기(Shift 비율 유지, Alt 가운데 기준, 반대쪽을 넘기면 뒤집기), 바깥 = 회전(Shift 15°), 가운데 표시 끌기 = 회전 중심(더블클릭 = 가운데로). 방향키 = 1px 옮기기.");
        tfHelp.TextWrapping = TextWrapping.Wrap;
        _transformBox = Ui.Group("변형(회전·크기)", Ui.Stack(
            Ui.Row(Ui.Label("각도"), _tfAngle, Ui.Label("°")),
            Ui.Row(Ui.Label("가로"), _tfScaleX, Ui.Label("%"), Ui.Label("세로"), _tfScaleY, Ui.Label("%")),
            Ui.Row(Ui.Label("중심 X"), _tfX, Ui.Label("Y"), _tfY),
            Ui.Row(Ui.Label("리샘플링"), _tfResample),
            _tfInfo,
            Ui.Row(tfApply, tfCancel),
            tfHelp));

        // ── 캔버스 크기 모드(Ctrl+Alt+C): 숫자는 캔버스 손잡이와 함께 움직인다.
        _crKeepRatio = Ui.Check("그림 비율 유지", true).Tip("그림 가로·세로 칸 하나를 바꾸면 다른 쪽도 원래 그림 비율대로 맞춥니다");
        _crKeepRatio.Focusable = false;
        _crWidth = CanvasResizeNum(1, "새 캔버스 가로(px) — 오른쪽 가장자리가 움직이고 그림은 제자리",
            v => _ctx.SetCanvasResize(_ctx.CanvasResizeCanvas with { Width = v }, _ctx.CanvasResizeImage));
        _crHeight = CanvasResizeNum(1, "새 캔버스 세로(px) — 아래 가장자리가 움직이고 그림은 제자리",
            v => _ctx.SetCanvasResize(_ctx.CanvasResizeCanvas with { Height = v }, _ctx.CanvasResizeImage));
        _crImageWidth = CanvasResizeNum(1, "그림 가로(px) — 최근접으로 늘이거나 줄입니다(왼쪽 위 기준)",
            v => SetCanvasResizeImage(i => i with { Width = v, Height = _crKeepRatio.On() ? RatioHeight(v) : i.Height }));
        _crImageHeight = CanvasResizeNum(1, "그림 세로(px) — 최근접으로 늘이거나 줄입니다(왼쪽 위 기준)",
            v => SetCanvasResizeImage(i => i with { Height = v, Width = _crKeepRatio.On() ? RatioWidth(v) : i.Width }));
        _crX = CanvasResizeNum(-PixelImage.MaxSize, "그림 왼쪽 위의 X(새 캔버스 기준, 음수 = 왼쪽이 잘림)",
            v => SetCanvasResizeImage(i => i with { X = v }));
        _crY = CanvasResizeNum(-PixelImage.MaxSize, "그림 왼쪽 위의 Y(새 캔버스 기준, 음수 = 위가 잘림)",
            v => SetCanvasResizeImage(i => i with { Y = v }));
        _crWidthPercent = Dim("");
        _crHeightPercent = Dim("");
        _crAnchor.Tip("그림을 지금 캔버스 안의 이 자리(9방향)에 놓습니다");
        _crAnchor.Changed += (_, _) =>
        {
            if (_updating) return;
            var c = _ctx.CanvasResizeCanvas;
            var (x, y) = PixelImage.AnchorOffset(_ctx.CanvasResizeImage.Width, _ctx.CanvasResizeImage.Height, c.Width, c.Height, _crAnchor.Value);
            SetCanvasResizeImage(i => i with { X = x, Y = y });
        };
        var crApply = Ui.Button("확인", (_, _) => _ctx.CommitCanvasResize()).Tip("캔버스 크기를 적용합니다(Enter) — 실행취소 한 단계");
        var crCancel = Ui.Button("취소", (_, _) => _ctx.CancelCanvasResize()).Tip("바꾸지 않고 끝냅니다(Esc, Ctrl+Z)");
        var crHelp = Dim("캔버스: 네모 손잡이 = 그 가장자리 옮기기(그림은 제자리, 여백이 늘거나 잘림), Ctrl+끌기 = 그림 가장자리도 함께(여백 그대로 그림 크기, 모서리 + Shift 비율 유지), 그림 안쪽 끌기 = 옮기기. 방향키 = 그림 1px 옮기기.");
        crHelp.TextWrapping = TextWrapping.Wrap;
        _canvasResizeBox = Ui.Group("캔버스 크기", Ui.Stack(
            Ui.Row(Ui.Label("캔버스 가로"), _crWidth, Ui.Label("세로"), _crHeight),
            Ui.Row(Ui.Label("그림 가로"), _crImageWidth, _crWidthPercent),
            Ui.Row(Ui.Label("그림 세로"), _crImageHeight, _crHeightPercent),
            _crKeepRatio,
            Ui.Row(Ui.Label("그림 위치 X"), _crX, Ui.Label("Y"), _crY),
            Ui.Row(Ui.Label("기준점"), _crAnchor),
            Ui.Row(crApply, crCancel),
            crHelp));

        var stack = Ui.Stack(_canvasResizeBox, _transformBox, _brushBox, _fillBox, _gradientBox, _pickerBox, _dodgeBox, _ditherBox, _optionsBox, _mirrorBox, _pivotBox, _sliceBox, _hint);
        var header = Ui.Header("도구 설정");
        _title = (TextBlock)header.Child!;
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

    /// <summary>바에서 고른 색 정지점을 바꾼다.</summary>
    private void SetSelectedStop(Func<GradientStop, GradientStop> change)
    {
        if (_gradientBar.SelectedIsAlpha) return;
        var list = _ctx.GradientStops.ToList();
        int i = Math.Clamp(_gradientBar.SelectedIndex, 0, list.Count - 1);
        list[i] = change(list[i]);
        _ctx.GradientStops = list;
    }

    /// <summary>바에서 고른 불투명도 정지점을 바꾼다.</summary>
    private void SetSelectedAlphaStop(Func<GradientAlphaStop, GradientAlphaStop> change)
    {
        if (!_gradientBar.SelectedIsAlpha) return;
        var list = _ctx.GradientAlphaStops.ToList();
        int i = Math.Clamp(_gradientBar.SelectedIndex, 0, list.Count - 1);
        list[i] = change(list[i]);
        _ctx.GradientAlphaStops = list;
    }

    /// <summary>고른 정지점의 위치·색(또는 불투명도)과 삭제 단추를 맞춘다. 색 줄·불투명도 줄은 고른 종류만 보인다.</summary>
    private void RefreshGradientStop()
    {
        bool alpha = _gradientBar.SelectedIsAlpha;
        bool was = _updating;
        _updating = true;
        try
        {
            _stopColorRow.IsVisible = !alpha;
            _stopAlphaRow.IsVisible = alpha;
            if (alpha)
            {
                var stops = _ctx.GradientAlphaStops;
                var s = stops[Math.Clamp(_gradientBar.SelectedIndex, 0, stops.Count - 1)];
                _stopPos.Value = (decimal)Math.Round(s.Position * 100, 1);
                _stopAlpha.Value = (decimal)Math.Round(s.Alpha * 100 / 255.0, 1);
                _stopDelete.IsEnabled = stops.Count > 2;
            }
            else
            {
                var stops = _ctx.GradientStops;
                var s = stops[Math.Clamp(_gradientBar.SelectedIndex, 0, stops.Count - 1)];
                _stopPos.Value = (decimal)Math.Round(s.Position * 100, 1);
                _stopColor.Color = s.Color;
                _stopDelete.IsEnabled = stops.Count > 2;
            }
        }
        finally
        {
            _updating = was;
        }
    }

    private static NumericUpDown PixelNum()
    {
        var n = Ui.Num(0, PixelImage.MaxSize, 0, 56, 2, 0.5m);
        n.FormatString = "0.##";
        return n;
    }

    // ───────────── 상태 → 화면 ─────────────

    /// <summary>현재 도구에 맞는 묶음만 보이고 값을 맞춘다.</summary>
    private void Refresh()
    {
        var tool = _ctx.Tool;
        bool shape = tool is ToolKind.Line or ToolKind.Rectangle or ToolKind.Ellipse;
        bool brush = shape || tool is ToolKind.Pencil or ToolKind.Eraser or ToolKind.Dodge or ToolKind.Dither or ToolKind.ReplaceBrush;
        bool paint = brush || tool == ToolKind.Fill;

        _title.Text = $"도구 설정 · {MainForm.ToolName(tool)}";
        _brushBox.IsVisible = brush;
        _brushTitle.Text = shape ? "선 두께·펜 모양" : "브러시";
        _fillBox.IsVisible = tool is ToolKind.Fill or ToolKind.MagicWand;
        _fillTitle.Text = tool == ToolKind.MagicWand ? "스마트 선택(채우기와 같은 설정)" : "채우기";
        _pickerBox.IsVisible = tool == ToolKind.Picker;
        _dodgeBox.IsVisible = tool == ToolKind.Dodge;
        _ditherBox.IsVisible = tool == ToolKind.Dither;
        _gradientBox.IsVisible = tool == ToolKind.Gradient;
        _pixelPerfect.IsVisible = tool is ToolKind.Pencil or ToolKind.Eraser;
        _shapeFilled.IsVisible = tool is ToolKind.Rectangle or ToolKind.Ellipse;
        _rightErase.IsVisible = tool is ToolKind.Pencil or ToolKind.Line or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Fill;
        _wrap.IsVisible = tool == ToolKind.Move;
        _optionsBox.IsVisible = _pixelPerfect.IsVisible || _shapeFilled.IsVisible || _rightErase.IsVisible || _wrap.IsVisible;
        _mirrorBox.IsVisible = paint;
        _pivotBox.IsVisible = tool == ToolKind.Pivot;
        _sliceBox.IsVisible = tool == ToolKind.Slice;
        _hint.Text = tool switch
        {
            ToolKind.Select => "드래그로 사각형을 선택합니다(Shift: 정사각형). 안쪽을 끌어 옮기고 Enter로 내려놓습니다. M: 다음 선택 도구.",
            ToolKind.EllipseSelect => "드래그로 원을 선택합니다(Shift: 정원). 안쪽을 끌어 옮기고 Enter로 내려놓습니다. M: 다음 선택 도구.",
            ToolKind.Lasso => "끌어서 테두리를 그리면 놓을 때 처음 점과 이어 닫고 그 안을 선택합니다. 안쪽을 끌어 옮깁니다.",
            ToolKind.MagicWand => "클릭한 곳과 비슷한 색 영역을 선택합니다. 옮기려면 이동(V)이나 방향키를 씁니다.",
            ToolKind.Move => "선택이 있으면 선택 내용을, 없으면 현재 셀 전체를 옮깁니다.",
            ToolKind.Gradient => "끌어서 시작점 → 끝점 방향으로 칠합니다. 실행취소는 한 번에 한 단계입니다.",
            _ => "",
        };
        _hint.IsVisible = _hint.Text.Length > 0;
        _transformBox.IsVisible = _ctx.IsTransforming;
        _canvasResizeBox.IsVisible = _ctx.IsCanvasResizing;
        if (_ctx.IsTransforming || _ctx.IsCanvasResizing)
        {
            // 변형·캔버스 크기 모드 중에는 그 값만 보인다.
            _title.Text = _ctx.IsTransforming ? "도구 설정 · 변형" : "도구 설정 · 캔버스 크기";
            foreach (var box in new Control[] { _brushBox, _fillBox, _gradientBox, _pickerBox, _dodgeBox, _ditherBox, _optionsBox, _mirrorBox, _pivotBox, _sliceBox, _hint })
                box.IsVisible = false;
        }

        _updating = true;
        try
        {
            _shapes.SelectedIndex = Array.FindIndex(Shapes, s => s.Shape == _ctx.BrushShape);
            _sizeNum.Value = _ctx.BrushSize;
            _brushPreview.Footprint = _ctx.Brush;
            _tolNum.Value = _ctx.FillTolerance;
            _contiguous.IsChecked = _ctx.FillContiguous;
            _diagonal.IsChecked = _ctx.FillDiagonal;
            _diagonal.IsEnabled = _ctx.FillContiguous;
            _fillSource.SelectedIndex = _ctx.FillSampleVisible ? 1 : 0;
            _pickSource.SelectedIndex = _ctx.PickerSampleVisible ? 1 : 0;
            _dodgeNum.Value = _ctx.DodgeStrength;
            _ditherPattern.SelectedIndex = (int)_ctx.DitherPattern;
            _pixelPerfect.IsChecked = _ctx.PixelPerfect;
            _shapeFilled.IsChecked = _ctx.ShapeFilled;
            _rightErase.IsChecked = _ctx.RightButtonErases;
            _wrap.IsChecked = _ctx.MoveWrap;
            _mirrorX.IsChecked = _ctx.MirrorX;
            _mirrorY.IsChecked = _ctx.MirrorY;
            _gradientBar.Stops = _ctx.GradientStops;
            _gradientBar.AlphaStops = _ctx.GradientAlphaStops;
            _gradientBar.Mode = _ctx.GradientInterpolation;
            _gradientMode.SelectedIndex = (int)_ctx.GradientInterpolation;
            _gradientDither.SelectedIndex = (int)_ctx.GradientDither;
            _gradientDither.IsEnabled = _ctx.GradientInterpolation == GradientInterpolation.Smooth;
            RefreshGradientStop();
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
            _tfResample.SelectedIndex = (int)_ctx.TransformResample;
            var src = t.SourceBounds;
            var dst = _ctx.Selection.Bounds;
            bool fallback = _ctx.TransformResample == ResampleMode.RotSprite && (long)src.Width * src.Height > FreeTransform.MaxRotSpriteArea;
            _tfInfo.Text = $"원본 {src.Width}×{src.Height} → {dst.Width}×{dst.Height} px"
                           + (fallback ? " · 큰 영역이라 최근접으로 집습니다" : "");
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
                ? "그룹 화면: 부위마다 캔버스의 초록 선을 끌어 바꿉니다. 숫자로 바꾸려면 타임라인에서 부위를 고르세요."
                : _ctx.CurrentPart is { } part
                    ? $"대상: 부위 \"{part.Name}\" ({part.PartSize.Width}×{part.PartSize.Height})"
                    : $"대상: 프레임 시트 모든 칸 ({w}×{h})";
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
                _pivotTarget.Text = $"대상: 부위 \"{part.Name}\" ({part.PartSize.Width}×{part.PartSize.Height}, 부위 아틀라스)";
            }
            else
            {
                int f = _ctx.CurrentFrame;
                bool own = p.Frames[f].Pivot is not null;
                _framePivot.IsVisible = true;
                _framePivot.IsChecked = own;
                _pivotTarget.Text = own ? $"대상: 프레임 {f + 1}만" : "대상: 프로젝트 기본 피벗(모든 프레임)";
            }
            // 끄는 중이면 미리보기 값.
            var pivot = _ctx.Pivot;
            int w = TargetWidth, h = TargetHeight;
            _pivotX.Maximum = w;
            _pivotY.Maximum = h;
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
            _ctx.Structural(() => p.Edit("기본 피벗", ProjectChange.Settings, () => p.Sprite.DefaultPivot = pivot));
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
