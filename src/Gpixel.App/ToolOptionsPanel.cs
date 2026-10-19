using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 오른쪽 독 "도구 설정": 고른 도구의 세부 설정만 보인다. 브러시(모양·크기·미리보기), 채우기(허용 오차·이어진 영역·대각선·기준),
/// 스포이드(기준), 밝게/어둡게(세기), 디더링(무늬), 도형(채움), 이동(순환), 대칭, 피벗 도구(K)의 피벗(픽셀 단위), 9-slice 도구(S)의 테두리(L·T·R·B).
/// 도구 설정은 <see cref="EditorContext"/>에 있고(실행취소 없음) 바뀌면 <see cref="EditorContext.ToolOptionsChanged"/>로 다시 맞춘다.
/// 피벗 값 바꾸기는 프로젝트 편집이라 실행취소된다.
/// </summary>
public sealed class ToolOptionsPanel : UserControl
{
    private static readonly string[] PresetArrows = ["↖", "↑", "↗", "←", "●", "→", "↙", "↓", "↘"];
    private static readonly PivotPreset[] PresetOrder =
    [
        PivotPreset.TopLeft, PivotPreset.TopCenter, PivotPreset.TopRight,
        PivotPreset.LeftCenter, PivotPreset.Center, PivotPreset.RightCenter,
        PivotPreset.BottomLeft, PivotPreset.BottomCenter, PivotPreset.BottomRight,
    ];
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
    private readonly TextBlock _brushTitle;
    private readonly ToggleButton[] _shapeButtons = new ToggleButton[Shapes.Length];
    private readonly NumericUpDown _sizeNum;
    private readonly BrushPreview _brushPreview = new();

    // 도구별
    private readonly Border _fillBox, _pickerBox, _dodgeBox, _ditherBox, _optionsBox, _mirrorBox;
    private readonly NumericUpDown _tolNum, _dodgeNum;
    private readonly CheckBox _contiguous, _diagonal, _pixelPerfect, _shapeFilled, _rightErase, _wrap, _mirrorX, _mirrorY;
    private readonly ComboBox _fillSource, _pickSource, _ditherPattern;
    private readonly TextBlock _hint;

    // 피벗
    private readonly Border _pivotBox;
    private readonly Border _sliceBox;
    private readonly NumericUpDown _sL, _sT, _sR, _sB;
    private readonly Button _sliceClear;
    private readonly TextBlock _sliceTarget;
    private readonly Button[] _presetButtons = new Button[9];
    private readonly NumericUpDown _pivotX, _pivotY;
    private readonly CheckBox _framePivot;
    private readonly TextBlock _pivotTarget, _pivotXMax, _pivotYMax;

    public ToolOptionsPanel(EditorContext ctx)
    {
        _ctx = ctx;
        Background = AppTheme.PanelBrush;

        // ── 브러시: 모양 단추 + 크기(슬라이더·숫자) + 미리보기
        var shapeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (int i = 0; i < Shapes.Length; i++)
        {
            var (shape, text, tip) = Shapes[i];
            var b = new ToggleButton
            {
                Content = text,
                Padding = new Thickness(6, 2, 6, 2),
                MinHeight = 0,
                Focusable = false,
            }.Tip(tip);
            b.Click += (_, _) => { _ctx.BrushShape = shape; Refresh(); };
            _shapeButtons[i] = b;
            shapeRow.Children.Add(b);
        }
        _sizeNum = NumBox(1, BrushFootprint.MaxSize, v => _ctx.BrushSize = v);
        _sizeNum.Tip($"브러시 크기 1~{BrushFootprint.MaxSize} — 좌우로 끌거나 [ / ]");
        var brushRight = new StackPanel { Spacing = 4, Children = { shapeRow, Ui.Row(Ui.Label("크기"), _sizeNum) } };
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
        _contiguous = Check("이어진 영역만", "끄면 화면 안의 맞는 색을 모두 바꿉니다 (Shift+G)", v => _ctx.FillContiguous = v);
        _diagonal = Check("대각선으로도 잇기(8방향)", "이어진 영역을 대각선 이웃까지 넓힙니다", v => _ctx.FillDiagonal = v);
        _fillSource = Combo(v => _ctx.FillSampleVisible = v == 1);
        _fillSource.Tip("영역을 정하는 그림. 보이는 레이어 전체면 합성된 모습으로 경계를 찾고 칠하기는 현재 레이어에 합니다.");
        _fillBox = Ui.Group("채우기", Ui.Stack(
            Ui.Row(Ui.Label("허용 오차"), _tolNum, Ui.Label("%")),
            _contiguous,
            _diagonal,
            Ui.Row(Ui.Label("기준"), _fillSource)));

        // ── 스포이드
        _pickSource = Combo(v => _ctx.PickerSampleVisible = v == 1);
        _pickSource.Tip("색을 집을 그림(Alt/Ctrl+클릭 임시 스포이드도 같음)");
        _pickerBox = Ui.Group("스포이드", Ui.Stack(Ui.Row(Ui.Label("기준"), _pickSource), Dim("Alt/Ctrl+클릭으로 쓰는 임시 스포이드에도 적용됩니다.")));

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
        var presetGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(0) };
        for (int i = 0; i < 9; i++)
        {
            var preset = PresetOrder[i];
            var b = Ui.Button(PresetArrows[i], (_, _) => ApplyPivot(SnappedPreset(preset)), 28);
            b.Height = 24;
            b.Padding = new Thickness(0);
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.VerticalContentAlignment = VerticalAlignment.Center;
            b.Margin = new Thickness(1);
            b.Tip(preset.ToString());
            _presetButtons[i] = b;
            Grid.SetColumn(b, i % 3);
            Grid.SetRow(b, i / 3);
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
            Ui.Row(presetGrid, Ui.Stack(Ui.Row(Ui.Label("X"), _pivotX, _pivotXMax), Ui.Row(Ui.Label("Y"), _pivotY, _pivotYMax))),
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

        var stack = Ui.Stack(_brushBox, _fillBox, _pickerBox, _dodgeBox, _ditherBox, _optionsBox, _mirrorBox, _pivotBox, _sliceBox, _hint);
        var header = Ui.Header("도구 설정");
        _title = (TextBlock)header.Child!;
        var root = new DockPanel();
        root.Children.Add(header);
        root.Children.Add(Ui.ScrollHost(stack));
        Content = root;

        ctx.ToolChanged += (_, _) => Refresh();
        ctx.ToolOptionsChanged += (_, _) => Refresh();
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
        _fillBox.IsVisible = tool == ToolKind.Fill;
        _pickerBox.IsVisible = tool == ToolKind.Picker;
        _dodgeBox.IsVisible = tool == ToolKind.Dodge;
        _ditherBox.IsVisible = tool == ToolKind.Dither;
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
            ToolKind.Select => "사각 선택은 따로 설정이 없습니다. 드래그로 선택하고 안쪽을 끌어 옮깁니다.",
            ToolKind.Move => "선택이 있으면 선택 내용을, 없으면 현재 셀 전체를 옮깁니다.",
            _ => "",
        };
        _hint.IsVisible = _hint.Text.Length > 0;

        _updating = true;
        try
        {
            for (int i = 0; i < Shapes.Length; i++) _shapeButtons[i].IsChecked = Shapes[i].Shape == _ctx.BrushShape;
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
        }
        finally
        {
            _updating = false;
        }
        RefreshPivot();
        RefreshSlice();
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

    /// <summary>프리셋을 정수 픽셀에 맞춘 피벗(가운데는 내림: 15px → 7px, 오른쪽 = 너비, 위 = 높이).</summary>
    private Pivot SnappedPreset(PivotPreset preset)
    {
        int w = TargetWidth, h = TargetHeight;
        var n = Pivot.FromPreset(preset);
        static int Snap(float v, int size) => v <= 0f ? 0 : v >= 1f ? size : size / 2;
        return new Pivot(Snap(n.X, w) / (float)w, Snap(n.Y, h) / (float)h);
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
            for (int i = 0; i < 9; i++)
            {
                var preset = PresetOrder[i];
                var snapped = SnappedPreset(preset);
                bool on = pivot == Pivot.FromPreset(preset)
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
