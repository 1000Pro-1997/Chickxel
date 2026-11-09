using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Chickxel.App.Localization;
using Chickxel.Core.Text;

namespace Chickxel.App;

/// <summary>편집 도구 종류.</summary>
public enum ToolKind
{
    Pencil,
    /// <summary>그라데이션 연필: 연필처럼 그리되 픽셀 색을 획을 따라 잰 거리로 그라데이션에서 고른다(획 전체 / 반복, 도구 설정).</summary>
    GradientPencil,
    Eraser,
    Fill,
    /// <summary>선형 그라데이션: 끌어서 시작점 → 끝점(색 정지점·보간·디더링은 도구 설정).</summary>
    Gradient,
    Picker,
    /// <summary>도형: 선·사각형·원·삼각형·정다각형·별·나선·원기둥·주사위(모양과 개수는 도구 설정, <see cref="EditorContext.ShapeStyle"/>).</summary>
    Shape,
    /// <summary>사각 선택.</summary>
    Select,
    /// <summary>원 선택(Shift = 정원).</summary>
    EllipseSelect,
    /// <summary>자유 선택(올가미): 끌어 그린 닫힌 모양.</summary>
    Lasso,
    /// <summary>스마트 선택(마술봉): 클릭한 곳과 비슷한 색 영역(채우기 설정을 같이 씀).</summary>
    MagicWand,
    Move,
    /// <summary>밝게(왼쪽)/어둡게(오른쪽) 브러시.</summary>
    Dodge,
    /// <summary>체커 무늬 디더링 브러시(기본색·보조색 번갈아).</summary>
    Dither,
    /// <summary>처음 닿은 색만 바꾸는 색 바꾸기 브러시.</summary>
    ReplaceBrush,
    /// <summary>피벗 점 끌기.</summary>
    Pivot,
    /// <summary>9-slice 테두리(Unity Sprite Border) 초록 선 끌기.</summary>
    Slice,
    /// <summary>글씨: 캔버스를 눌러 글을 쓰면 글씨 효과(<see cref="Chickxel.Core.Model.TextEffect"/>)가 붙은 빈 레이어를 만든다(글씨 레이어 위를 누르면 그 글을 고침).</summary>
    Text,
}

/// <summary>
/// 편집기 전체가 함께 쓰는 상태: 열린 프로젝트, 현재 레이어·프레임, 선택 영역, 도구·색, 합성 비트맵 캐시.
/// 패널들은 이 객체의 이벤트로 서로 동기화된다.
/// <para>
/// 타임라인의 열은 항상 프레임이다. 현재 레이어가 부위면 캔버스 크기(<see cref="CanvasWidth"/>)·그리기 대상(<see cref="EditImage"/>)·
/// 피벗은 그 부위 기준(부위 안 좌표)이고, 그룹이면 프레임 합성을 보며 부위를 배치한다(그리기 없음).
/// </para>
/// </summary>
public sealed partial class EditorContext
{
    public const int MaxRecentColors = 16;

    private ToolKind _tool = ToolKind.Pencil;
    private Color32 _primary = Color32.Black;
    private Color32 _secondary = Color32.Transparent;
    private int _brushSize = 1;
    private bool _selectionStrokeOpen;
    private int _layer, _frame;
    private Size _canvasSize;
    private bool _onionSkin;
    private AnimationTag? _selectedTag;
    private Pivot? _pivotPreview;
    private PixelImage? _blank;
    private PixelImage? _strokeImage;
    private WriteableBitmap? _blankBitmap;

    // 프레임 합성·부위 하나 뺀 합성·셀(그룹이면 그룹 합성)·타임라인 썸네일 결과(가벼우면 바로, 무거우면 작업 스레드에서 계산)
    private readonly RenderService _renders = new();
    // 무효화를 레이어 단위로 하려고 기억해 두는 레이어별 그리기 상태와 프레임 차례
    private LayerRenderState[] _layerStates = [];
    private Frame[] _frameOrder = [];

    public SpriteProject? Project { get; private set; }
    public bool HasProject => Project is not null;
    public Selection Selection { get; } = new();

    /// <summary>복사한 픽셀(앱 내부 클립보드).</summary>
    public PixelImage? Clipboard { get; set; }

    public EditorContext()
    {
        // 선택 영역 떼기·내려놓기는 셀 픽셀을 직접 바꾼다.
        Selection.Changed += (_, _) => InvalidateCurrent();
        // 캔버스 밖으로 나간 떠 있는 픽셀(옮긴 선택, 캔버스보다 큰 붙여넣기)은 일반 레이어 셀의 캔버스 밖 그림으로 남긴다(같은 기록 단계).
        Selection.AnchoringOutside = (image, floating, at) =>
        {
            if (Project is { IsStrokeActive: true } p && ReferenceEquals(image, _strokeImage)) p.AddStrokeOverflow(floating, at);
        };
        _renders.Updated += () => RenderUpdated?.Invoke(this, EventArgs.Empty);
        // 스크립트 효과: 실행 허락·멈춤 해제로 같은 효과 값의 결과가 달라지면 다시 그리고, 실패 표시만 바뀌면 화면만 다시 그린다(작업 스레드에서도 옴).
        ScriptLibrary.Changed += () => Dispatcher.UIThread.Post(RerenderScripts);
        ScriptEffectRuntime.StatusChanged += () => Dispatcher.UIThread.Post(() => ViewChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>다른 프로젝트가 열리거나 닫힘.</summary>
    public event EventHandler? ProjectReplaced;
    /// <summary>현재 프로젝트 내용이 바뀜.</summary>
    public event EventHandler<ProjectChangedEventArgs>? ProjectChanged;
    /// <summary>캔버스·미리보기·타임라인을 다시 그려야 함(내용 변경, 현재 프레임/레이어 변경 등).</summary>
    public event EventHandler? ViewChanged;
    /// <summary>현재 레이어·프레임·선택한 태그가 바뀜.</summary>
    public event EventHandler? CurrentChanged;
    public event EventHandler? DirtyChanged;
    public event EventHandler? ToolChanged;
    public event EventHandler? ToolOptionsChanged;
    public event EventHandler? ColorsChanged;
    public event EventHandler? RecentColorsChanged;
    public event EventHandler? OnionSkinChanged;
    public event EventHandler? UndoChanged;
    /// <summary>작업 스레드에서 계산하던 합성 결과·중간 그림이 왔거나 회색 덮개를 씌울 때(캔버스·타임라인·미리보기 다시 그리기).</summary>
    public event EventHandler? RenderUpdated;

    // ───────────── 도구·색 ─────────────

    public ToolKind Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            // 변형·캔버스 크기 모드면 어떤 도구로 바꾸든 먼저 적용한다.
            if (IsTransforming || IsCanvasResizing || (!IsSelectionTool(value) && value != ToolKind.Move)) CommitFloating();
            // 지우개는 브러시(모양·크기)를 따로 기억한다: 지우개 ↔ 다른 도구로 바뀌면 쓰던 브러시를 맞바꾼다
            bool swap = UsesEraserBrush(_tool) != UsesEraserBrush(value);
            _tool = value;
            if (swap) (_brushSize, _brushShape, _otherBrush) = (_otherBrush.Size, _otherBrush.Shape, (_brushSize, _brushShape));
            ToolChanged?.Invoke(this, EventArgs.Empty);
            if (swap) OptionsChanged();
        }
    }

    /// <summary>선택 영역을 만드는 도구(사각·원·자유·스마트 선택): 떠 있는 선택을 유지하고 안쪽을 끌거나 방향키로 옮긴다.</summary>
    public static bool IsSelectionTool(ToolKind t)
        => t is ToolKind.Select or ToolKind.EllipseSelect or ToolKind.Lasso or ToolKind.MagicWand;

    public Color32 Primary
    {
        get => _primary;
        set { if (_primary == value) return; _primary = value; ColorsChanged?.Invoke(this, EventArgs.Empty); }
    }

    public Color32 Secondary
    {
        get => _secondary;
        set { if (_secondary == value) return; _secondary = value; ColorsChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void SwapColors()
    {
        (_primary, _secondary) = (_secondary, _primary);
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    public List<Color32> RecentColors { get; } = new();

    /// <summary>그리기에 쓴 색을 최근 색 맨 앞에 넣는다.</summary>
    public void PushRecentColor(Color32 c)
    {
        if (RecentColors.Count > 0 && RecentColors[0] == c) return;
        RecentColors.Remove(c);
        RecentColors.Insert(0, c);
        if (RecentColors.Count > MaxRecentColors) RecentColors.RemoveRange(MaxRecentColors, RecentColors.Count - MaxRecentColors);
        RecentColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>브러시 크기(1~<see cref="BrushFootprint.MaxSize"/>): 연필·지우개·밝게/어둡게·디더링·색 바꾸기 브러시, 선·도형 외곽 두께.</summary>
    public int BrushSize
    {
        get => _brushSize;
        set { value = Math.Clamp(value, 1, BrushFootprint.MaxSize); if (_brushSize == value) return; _brushSize = value; OptionsChanged(); }
    }

    /// <summary>지금 도구가 아닌 쪽 브러시(지금 지우개면 연필 쪽, 아니면 지우개 쪽).</summary>
    private (int Size, BrushShape Shape) _otherBrush = (1, BrushShape.Square);

    /// <summary>지우개 브러시를 쓰는 도구인지(나머지 도구 — 연필·그라데이션 연필·밝게/어둡게·디더링·색 바꾸기·도형 외곽 — 는 연필 브러시를 같이 씀).</summary>
    private static bool UsesEraserBrush(ToolKind tool) => tool == ToolKind.Eraser;

    /// <summary>연필 쪽 브러시(모양·크기): 지우개가 아닌 브러시 도구가 같이 쓴다. 설정 저장·불러오기용.</summary>
    public (int Size, BrushShape Shape) PencilBrush
    {
        get => UsesEraserBrush(_tool) ? _otherBrush : (_brushSize, _brushShape);
        set => SetBrushSlot(eraser: false, value);
    }

    /// <summary>지우개 브러시(모양·크기). 설정 저장·불러오기용.</summary>
    public (int Size, BrushShape Shape) EraserBrush
    {
        get => UsesEraserBrush(_tool) ? (_brushSize, _brushShape) : _otherBrush;
        set => SetBrushSlot(eraser: true, value);
    }

    private void SetBrushSlot(bool eraser, (int Size, BrushShape Shape) brush)
    {
        var value = (Math.Clamp(brush.Size, 1, BrushFootprint.MaxSize), Enum.IsDefined(brush.Shape) ? brush.Shape : BrushShape.Square);
        if (UsesEraserBrush(_tool) == eraser)
        {
            BrushSize = value.Item1;
            BrushShape = value.Item2;
        }
        else
        {
            _otherBrush = value;
        }
    }

    private bool _pixelPerfect, _fillContiguous = true, _shapeFilled, _mirrorX, _mirrorY, _mirrorAtPivot, _rightErases, _moveWrap;
    private bool _fillDiagonal, _fillSampleVisible, _pickerSampleVisible = true;
    private BrushShape _brushShape = BrushShape.Square;
    private int _fillTolerance, _dodgeStrength = DefaultDodgeStrength;
    private DitherPattern _ditherPattern = DitherPattern.Checker;

    public const int DefaultDodgeStrength = 10;
    public const int MaxDodgeStrength = 50;

    /// <summary>브러시 모양(사각·원·마름모). 크기 1은 모양과 관계없이 한 점.</summary>
    public BrushShape BrushShape
    {
        get => _brushShape;
        set { if (!Enum.IsDefined(value)) value = BrushShape.Square; if (_brushShape == value) return; _brushShape = value; OptionsChanged(); }
    }

    /// <summary>현재 브러시 도장(모양·크기).</summary>
    public BrushFootprint Brush => BrushFootprint.Get(_brushShape, _brushSize);

    public bool PixelPerfect { get => _pixelPerfect; set { _pixelPerfect = value; OptionsChanged(); } }
    private bool _pencilOrthogonal = true;
    /// <summary>연필·그라데이션 연필·지우개 획의 픽셀 잇기: true = ㄱ자(4방향, 모든 픽셀이 변으로 붙음 — 픽셀 퍼펙트를 켜면 쓰지 않고 대각선), false = 대각선으로도 이음.</summary>
    public bool PencilOrthogonal { get => _pencilOrthogonal; set { if (_pencilOrthogonal == value) return; _pencilOrthogonal = value; OptionsChanged(); } }
    /// <summary>채우기: true = 이어진 영역만, false = 맞는 색 전체.</summary>
    public bool FillContiguous { get => _fillContiguous; set { _fillContiguous = value; OptionsChanged(); } }
    /// <summary>채우기 허용 오차(0~100%, 0 = 정확히 같은 색만). 채널 차이 최댓값 기준(<see cref="Raster.Matches"/>).</summary>
    public int FillTolerance
    {
        get => _fillTolerance;
        set { value = Math.Clamp(value, 0, 100); if (_fillTolerance == value) return; _fillTolerance = value; OptionsChanged(); }
    }
    /// <summary><see cref="FillTolerance"/>(%)를 채널 단위(0~255)로.</summary>
    public int FillToleranceChannels => (int)Math.Round(_fillTolerance * 255 / 100.0);
    /// <summary>채우기: 이어진 영역을 대각선으로도 잇는다(8방향).</summary>
    public bool FillDiagonal { get => _fillDiagonal; set { _fillDiagonal = value; OptionsChanged(); } }
    /// <summary>채우기 영역을 보이는 레이어 합성으로 정한다(칠하기는 현재 레이어). false = 현재 레이어.</summary>
    public bool FillSampleVisible { get => _fillSampleVisible; set { _fillSampleVisible = value; OptionsChanged(); } }
    /// <summary>스포이드(임시 스포이드 포함): 보이는 레이어 합성에서 집는다. false = 현재 레이어에서.</summary>
    public bool PickerSampleVisible { get => _pickerSampleVisible; set { _pickerSampleVisible = value; OptionsChanged(); } }
    /// <summary>밝게/어둡게 한 번의 변화량(1~50%).</summary>
    public int DodgeStrength
    {
        get => _dodgeStrength;
        set { value = Math.Clamp(value, 1, MaxDodgeStrength); if (_dodgeStrength == value) return; _dodgeStrength = value; OptionsChanged(); }
    }
    private DodgeMode _dodgeMode = DodgeBurn.DefaultMode;
    /// <summary>밝게/어둡게가 색을 바꾸는 방식(<see cref="DodgeBurn"/>).</summary>
    public DodgeMode DodgeMode
    {
        get => _dodgeMode;
        set { if (!Enum.IsDefined(value)) value = DodgeBurn.DefaultMode; if (_dodgeMode == value) return; _dodgeMode = value; OptionsChanged(); }
    }

    private int _eraserStrength = 100;
    private bool _eraserRed = true, _eraserGreen = true, _eraserBlue = true;

    /// <summary>지우개 강도(1~100%): 한 획에 픽셀마다 한 번 줄이는 정도(<see cref="PartialErase"/>). 100%면 완전히 지움.</summary>
    public int EraserStrength
    {
        get => _eraserStrength;
        set { value = Math.Clamp(value, 1, 100); if (_eraserStrength == value) return; _eraserStrength = value; OptionsChanged(); }
    }
    /// <summary>지우개 채널: R·G·B를 모두 고르면 투명하게, 일부만 고르면 그 색 채널만 줄인다.</summary>
    public bool EraserRed { get => _eraserRed; set { _eraserRed = value; OptionsChanged(); } }
    public bool EraserGreen { get => _eraserGreen; set { _eraserGreen = value; OptionsChanged(); } }
    public bool EraserBlue { get => _eraserBlue; set { _eraserBlue = value; OptionsChanged(); } }
    /// <summary>디더링 무늬.</summary>
    public DitherPattern DitherPattern
    {
        get => _ditherPattern;
        set { if (!Enum.IsDefined(value)) value = DitherPattern.Checker; if (_ditherPattern == value) return; _ditherPattern = value; OptionsChanged(); }
    }

    private TextStyle _textStyle = new();

    /// <summary>
    /// 글씨 도구 설정(글꼴·크기·굵게·기울임·매끄럽게·간격·가로/세로 배율·줄 맞춤): 새 글씨 레이어에 쓰고, 캔버스에서 고치는 글씨에는 바로 반영된다.
    /// 글씨 레이어를 고치기 시작하면 그 레이어의 설정으로 바뀐다. 범위 밖 값은 글씨 효과 범위로 맞춰 넣고, 바뀌면 <see cref="TextStyleChanged"/>를 알린다.
    /// </summary>
    public TextStyle TextStyle
    {
        get => _textStyle;
        set
        {
            value = NormalizeTextStyle(value);
            if (_textStyle == value) return;
            _textStyle = value;
            TextStyleChanged?.Invoke(this, EventArgs.Empty);
            OptionsChanged();
        }
    }
    /// <summary><see cref="TextStyle"/>가 바뀜(캔버스가 고치는 글씨에 바로 반영).</summary>
    public event EventHandler? TextStyleChanged;

    /// <summary>글씨 효과에 한 번 넣었다 빼서 범위를 맞춘다(글꼴 이름이 비었으면 기본 글꼴).</summary>
    private static TextStyle NormalizeTextStyle(TextStyle? style)
    {
        style ??= new();
        if (string.IsNullOrWhiteSpace(style.FontName)) style = style with { FontName = FontLibrary.DefaultName };
        return TextStyle.From(style.ApplyTo(new TextEffect()));
    }
    /// <summary>도형: 채움(true) / 외곽(false). 나선은 늘 선(<see cref="Shapes.CanFill"/>).</summary>
    public bool ShapeFilled { get => _shapeFilled; set { _shapeFilled = value; OptionsChanged(); } }

    private ShapeKind _shapeKind = ShapeKind.Rectangle;
    private int _shapeSides = 5, _starPoints = 5, _spiralTurns = 3;

    /// <summary>도형 도구가 그리는 모양.</summary>
    public ShapeKind ShapeKind
    {
        get => _shapeKind;
        set { if (!Enum.IsDefined(value)) value = ShapeKind.Rectangle; if (_shapeKind == value) return; _shapeKind = value; OptionsChanged(); }
    }
    /// <summary>정다각형 꼭짓점 수(<see cref="Shapes.MinSides"/>~<see cref="Shapes.MaxSides"/>).</summary>
    public int ShapeSides
    {
        get => _shapeSides;
        set { value = Math.Clamp(value, Shapes.MinSides, Shapes.MaxSides); if (_shapeSides == value) return; _shapeSides = value; OptionsChanged(); }
    }
    /// <summary>별 뿔 수(<see cref="Shapes.MinSides"/>~<see cref="Shapes.MaxSides"/>).</summary>
    public int StarPoints
    {
        get => _starPoints;
        set { value = Math.Clamp(value, Shapes.MinSides, Shapes.MaxSides); if (_starPoints == value) return; _starPoints = value; OptionsChanged(); }
    }
    /// <summary>나선 감은 수(<see cref="Shapes.MinTurns"/>~<see cref="Shapes.MaxTurns"/>).</summary>
    public int SpiralTurns
    {
        get => _spiralTurns;
        set { value = Math.Clamp(value, Shapes.MinTurns, Shapes.MaxTurns); if (_spiralTurns == value) return; _spiralTurns = value; OptionsChanged(); }
    }
    private bool _shapeOrthogonal;
    /// <summary>선·곡선 픽셀 잇기: true = ㄱ자(4방향, 모든 픽셀이 변으로 붙음), false = 대각선으로도 이음.</summary>
    public bool ShapeOrthogonal { get => _shapeOrthogonal; set { if (_shapeOrthogonal == value) return; _shapeOrthogonal = value; OptionsChanged(); } }
    /// <summary>지금 도형 설정(모양·개수·선 잇기).</summary>
    public ShapeStyle ShapeStyle => new(_shapeKind, _shapeSides, _starPoints, _spiralTurns, _shapeOrthogonal);
    /// <summary>대칭 그리기: 좌우(세로 축 기준 반전).</summary>
    public bool MirrorX { get => _mirrorX; set { _mirrorX = value; OptionsChanged(); } }
    /// <summary>대칭 그리기: 상하.</summary>
    public bool MirrorY { get => _mirrorY; set { _mirrorY = value; OptionsChanged(); } }
    /// <summary>
    /// 대칭 그리기 축을 캔버스 가운데 대신 지금 레이어의 피벗(<see cref="SpriteProject.LayerPivotPixel"/> — 부위면 부위 피벗, 그 밖에는 레이어 피벗)을 지나는 선으로 둔다.
    /// </summary>
    public bool MirrorAtPivot { get => _mirrorAtPivot; set { if (_mirrorAtPivot == value) return; _mirrorAtPivot = value; OptionsChanged(); } }
    /// <summary>지금 대칭 축(캔버스 좌표): 피벗 기준이면 피벗을 지나는 반 픽셀 단위 선, 아니면 캔버스 가운데.</summary>
    public MirrorAxes MirrorAxes
    {
        get
        {
            if (!_mirrorAtPivot) return MirrorAxes.Center(CanvasWidth, CanvasHeight);
            var (x, y) = CurrentLayerPivotLocal();
            return MirrorAxes.Through(x, y);
        }
    }
    /// <summary>오른쪽 버튼으로 보조색 대신 지운다(연필·도형·채우기).</summary>
    public bool RightButtonErases { get => _rightErases; set { _rightErases = value; OptionsChanged(); } }
    /// <summary>이동 도구로 셀 내용을 옮길 때 밖으로 나간 픽셀을 반대편으로 돌린다. 끄면 일반 레이어는 밖으로 나간 픽셀을 캔버스 밖 그림으로 남긴다.</summary>
    public bool MoveWrap { get => _moveWrap; set { _moveWrap = value; OptionsChanged(); } }

    /// <summary>그라데이션 연필 기본 그라데이션(검정 → 흰색, 불투명). 아래 그라데이션 기본값보다 앞에 있어 정지점을 따로 적는다.</summary>
    public static readonly GradientPreset DefaultPencilGradient = new()
    {
        Stops = [new(0, Color32.Black), new(1, Color32.White)],
        AlphaStops = [new(0, 255), new(1, 255)],
    };
    private GradientPreset _pencilGradient = DefaultPencilGradient;
    private GradientStrokeMode _pencilGradientMode = GradientStrokeMode.WholeStroke;
    private int _pencilGradientInterval = GradientStroke.DefaultInterval;

    /// <summary>그라데이션 연필이 칠하는 그라데이션(색·불투명도 정지점, 보간, 색 공간, 디더링만 씀).</summary>
    public GradientPreset PencilGradient
    {
        get => _pencilGradient;
        set
        {
            if (value.Stops.Count < 2) throw new ArgumentException(UiStrings.S_EditorContext_GradientStops_B89D363377, nameof(value));
            _pencilGradient = value;
            OptionsChanged();
        }
    }
    /// <summary>그라데이션 연필: 획 전체 / 반복.</summary>
    public GradientStrokeMode PencilGradientMode
    {
        get => _pencilGradientMode;
        set { if (!Enum.IsDefined(value)) value = GradientStrokeMode.WholeStroke; if (_pencilGradientMode == value) return; _pencilGradientMode = value; OptionsChanged(); }
    }
    /// <summary>그라데이션 연필 반복 간격(px, <see cref="GradientStroke.MinInterval"/>~<see cref="GradientStroke.MaxInterval"/>).</summary>
    public int PencilGradientInterval
    {
        get => _pencilGradientInterval;
        set { value = Math.Clamp(value, GradientStroke.MinInterval, GradientStroke.MaxInterval); if (_pencilGradientInterval == value) return; _pencilGradientInterval = value; OptionsChanged(); }
    }

    /// <summary>그라데이션 기본 정지점(검정 → 흰색).</summary>
    public static readonly IReadOnlyList<GradientStop> DefaultGradientStops = [new(0, Color32.Black), new(1, Color32.White)];
    private IReadOnlyList<GradientStop> _gradientStops = DefaultGradientStops;
    private GradientInterpolation _gradientInterpolation = GradientInterpolation.Smooth;
    private GradientDither _gradientDither = GradientDither.None;
    private GradientColorSpace _gradientColorSpace = GradientColorSpace.OkLch;

    /// <summary>그라데이션 색 정지점(2개 이상, 위치 0~1). 편집 순서를 그대로 두고 칠할 때 위치 순으로 쓴다.</summary>
    public IReadOnlyList<GradientStop> GradientStops
    {
        get => _gradientStops;
        set
        {
            if (value.Count < 2) throw new ArgumentException(UiStrings.S_EditorContext_GradientStops_B89D363377, nameof(value));
            _gradientStops = value.Select(s => s with { Position = Math.Clamp(s.Position, 0, 1) }).ToArray();
            OptionsChanged();
        }
    }

    /// <summary>그라데이션 기본 불투명도 정지점(처음·끝 모두 100%).</summary>
    public static readonly IReadOnlyList<GradientAlphaStop> DefaultGradientAlphaStops = [new(0, 255), new(1, 255)];
    private IReadOnlyList<GradientAlphaStop> _gradientAlphaStops = DefaultGradientAlphaStops;

    /// <summary>그라데이션 불투명도 정지점(2개 이상, 위치 0~1, 색 정지점과 따로). 칠할 때 색 알파에 곱한다.</summary>
    public IReadOnlyList<GradientAlphaStop> GradientAlphaStops
    {
        get => _gradientAlphaStops;
        set
        {
            if (value.Count < 2) throw new ArgumentException(UiStrings.S_EditorContext_GradientAlphaStops_AB19024421, nameof(value));
            _gradientAlphaStops = value.Select(s => s with { Position = Math.Clamp(s.Position, 0, 1) }).ToArray();
            OptionsChanged();
        }
    }
    /// <summary>그라데이션 보간: 부드럽게 / 단계(정지점 색 띠).</summary>
    public GradientInterpolation GradientInterpolation
    {
        get => _gradientInterpolation;
        set { if (!Enum.IsDefined(value)) value = GradientInterpolation.Smooth; if (_gradientInterpolation == value) return; _gradientInterpolation = value; OptionsChanged(); }
    }
    /// <summary>그라데이션 색 정지점 사이의 보간 색 공간.</summary>
    public GradientColorSpace GradientColorSpace
    {
        get => _gradientColorSpace;
        set { if (!Enum.IsDefined(value)) value = GradientColorSpace.OkLch; if (_gradientColorSpace == value) return; _gradientColorSpace = value; OptionsChanged(); }
    }
    /// <summary>그라데이션 순서 디더링(부드럽게일 때만 씀).</summary>
    public GradientDither GradientDither
    {
        get => _gradientDither;
        set { if (!Enum.IsDefined(value)) value = GradientDither.None; if (_gradientDither == value) return; _gradientDither = value; OptionsChanged(); }
    }

    private void OptionsChanged() => ToolOptionsChanged?.Invoke(this, EventArgs.Empty);

    public bool OnionSkin
    {
        get => _onionSkin;
        set { if (_onionSkin == value) return; _onionSkin = value; OnionSkinChanged?.Invoke(this, EventArgs.Empty); }
    }

    // ───────────── 프로젝트 ─────────────

    /// <summary>캔버스(그리는 그림) 가로: 부위를 고르면 그 부위 크기, 아니면 프로젝트 캔버스.</summary>
    public int CanvasWidth => CurrentPart?.PartSize.Width ?? Project?.Width ?? 0;
    public int CanvasHeight => CurrentPart?.PartSize.Height ?? Project?.Height ?? 0;
    public int FrameCount => Project?.Frames.Count ?? 0;
    public int LayerCount => Project?.Layers.Count ?? 0;

    /// <summary>현재 레이어 번호(0 = 맨 아래).</summary>
    public int CurrentLayer => _layer;
    public int CurrentFrame => _frame;

    /// <summary>현재 레이어 객체(프로젝트가 없으면 null).</summary>
    public Layer? CurrentLayerObject
        => Project is { } p && p.Layers.Count > 0 ? p.Layers[Math.Clamp(_layer, 0, p.Layers.Count - 1)] : null;

    /// <summary>현재 레이어가 부위면 그 부위, 아니면 null.</summary>
    public Layer? CurrentPart => CurrentLayerObject is { IsPart: true } l ? l : null;

    /// <summary>현재 레이어가 그룹인지(캔버스는 부위 배치 모드).</summary>
    public bool IsGroupSelected => CurrentLayerObject?.IsGroup == true;

    /// <summary>고른 그룹, 또는 고른 부위가 속한 그룹(없으면 null).</summary>
    public Layer? CurrentGroup => CurrentLayerObject switch
    {
        { IsGroup: true } g => g,
        { IsPart: true } part => part.Parent,
        _ => null,
    };

    private Size CurrentCanvasSize => new(CanvasWidth, CanvasHeight);

    /// <summary>실행취소 단계 수(설정). 바꾸면 열린 프로젝트에도 바로 적용한다.</summary>
    public int UndoCapacity
    {
        get => _undoCapacity;
        set
        {
            _undoCapacity = Math.Clamp(value, 1, UndoStack.MaxCapacity);
            if (Project is not null) Project.History.Capacity = _undoCapacity;
        }
    }
    private int _undoCapacity = UndoStack.DefaultCapacity;

    public void SetProject(SpriteProject? project)
    {
        if (Project is not null)
        {
            CommitFloating();
            Project.Changed -= OnProjectChanged;
            Project.DirtyChanged -= OnProjectDirty;
            Project.History.Changed -= OnUndoChanged;
        }
        Selection.Deselect(_blank ?? new PixelImage(1, 1));
        _selectionStrokeOpen = false;
        _strokeImage = null;
        _renders.Reset();
        _thumbBounds.Clear();
        _pivotShifts.Clear();
        _renders.CheckpointLayer = null;
        Project = project;
        RememberRenderState(project);
        _layer = project is null ? 0 : project.Layers.Count - 1;
        _frame = 0;
        _canvasSize = CurrentCanvasSize;
        _selectedTag = null;
        _pivotPreview = null;
        _pivotCanvas = false;
        if (project is not null)
        {
            project.History.Capacity = UndoCapacity;
            project.Changed += OnProjectChanged;
            project.DirtyChanged += OnProjectDirty;
            project.History.Changed += OnUndoChanged;
        }
        ProjectReplaced?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        UndoChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnUndoChanged(object? sender, EventArgs e) => UndoChanged?.Invoke(this, EventArgs.Empty);

    private void OnProjectDirty(object? sender, EventArgs e) => DirtyChanged?.Invoke(this, EventArgs.Empty);

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        var p = Project!;
        // 스트로크 중에는 현재 셀만 바뀐다. 그 밖의 변경은 바뀐 것을 보고 결과를 무효로 한다(효과만 바뀐 레이어면 그 레이어가 들어간 결과만).
        // 결과 키(레이어, 프레임)는 그룹의 지금 스킨 기준이므로 스킨이 바뀌면(Layers | Pixels) 모두 무효로 한다.
        // 다른 레이어·테두리를 읽는 스크립트가 있으면 설정(프로젝트 9-slice 테두리)만 바뀌어도 결과가 바뀔 수 있다.
        if (e.Change == ProjectChange.Pixels && p.IsStrokeActive) InvalidateCurrent(stroking: true);
        else if ((e.Change & ~ProjectChange.Path & ~ProjectChange.Settings) != 0 || (e.Change & ProjectChange.Settings) != 0 && p.UsesOtherLayers)
        {
            InvalidateRenders(p, e.Change);
            SyncCanvasResize();
            SyncLayerTransform(p);
        }
        int layer = Math.Clamp(_layer, 0, p.Layers.Count - 1), frame = Math.Clamp(_frame, 0, p.Frames.Count - 1);
        bool currentChanged = layer != _layer || frame != _frame;
        _layer = layer;
        _frame = frame;
        if (_selectedTag is not null && !p.Tags.Contains(_selectedTag)) { _selectedTag = null; currentChanged = true; }
        var size = CurrentCanvasSize;
        if ((e.Change & ProjectChange.Canvas) != 0 || size != _canvasSize)
        {
            _canvasSize = size;
            Selection.Deselect(EditImageOrEmpty);
            _selectionStrokeOpen = false;
        }
        ProjectChanged?.Invoke(this, e);
        if (currentChanged) CurrentChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 현재 레이어·프레임을 바꾼다(떠 있는 선택·변형은 먼저 적용함). 레이어 전체 변형 중이면 같은 레이어의 다른 프레임으로 가도 변형을 이어 가고
    /// (그 프레임 모습을 보임), 다른 레이어로 가면 변형을 취소한다. 캔버스 크기가 다른 레이어(부위 ↔ 프레임, 크기가 다른 부위)로 바뀌면 선택 영역은 해제된다.
    /// </summary>
    public void SetCurrent(int layer, int frame)
    {
        if (Project is not { } p) return;
        layer = Math.Clamp(layer, 0, p.Layers.Count - 1);
        frame = Math.Clamp(frame, 0, p.Frames.Count - 1);
        if (layer == _layer && frame == _frame) return;
        bool keepLayerTransform = IsLayerTransform && layer == _layer;
        if (IsLayerTransform && !keepLayerTransform) CancelTransform();
        else if (!keepLayerTransform) CommitFloating();
        if (layer != _layer)
        {
            var next = p.Layers[layer];
            var size = next.IsPart ? next.PartSize : p.Size;
            if (size != _canvasSize)
            {
                Selection.Deselect(EditImageOrEmpty);
                _selectionStrokeOpen = false;
            }
            _pivotPreview = null;
            _pivotCanvas = false;
        }
        _layer = layer;
        _frame = frame;
        _canvasSize = CurrentCanvasSize;
        if (keepLayerTransform) RebaseLayerTransform(p);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCurrentFrame(int frame) => SetCurrent(_layer, frame);

    public void SetCurrentLayer(int layer) => SetCurrent(layer, _frame);

    /// <summary>타임라인에서 고른 태그(재생·미리보기 대상).</summary>
    public AnimationTag? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (ReferenceEquals(_selectedTag, value)) return;
            _selectedTag = value;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>재생할 태그: 타임라인에서 누른 태그(고른 태그가 없으면 null = 모든 프레임 재생).</summary>
    public AnimationTag? ActiveTag
        => Project is { } p && _selectedTag is { } t && p.Tags.Contains(t) ? t : null;

    /// <summary>재생 순서(태그가 있으면 그 방향대로, 없으면 모든 프레임).</summary>
    public List<int> PlaybackOrder() => ActiveTag?.GetPlaybackOrder() ?? Enumerable.Range(0, FrameCount).ToList();

    public bool PlaybackLoops => ActiveTag?.Loop ?? true;

    public int FrameDuration(int frame) => Project is { } p && frame >= 0 && frame < p.Frames.Count ? p.Frames[frame].DurationMs : 100;

    // ───────────── 그리기 대상 ─────────────

    /// <summary>현재 레이어가 그룹이 아니고 잠기지 않았으면(그룹 포함) true. 숨긴 레이어에도 그릴 수 있다.</summary>
    public bool CanEdit
        => Project is { } p && !p.Layers[_layer].IsGroup && !p.IsLayerLocked(_layer);

    /// <summary>현재 셀 이미지(스트로크 중이면 그 이미지, 부위면 부위 크기, 그룹·빈 셀이면 null).</summary>
    public PixelImage? EditImage => _strokeImage ?? Project?.GetCel(_layer, _frame);

    /// <summary>선택 영역 계산용: 현재 셀이 없으면 캔버스 크기의 빈 이미지.</summary>
    public PixelImage EditImageOrEmpty
    {
        get
        {
            if (EditImage is { } img) return img;
            if (_blank is null || _blank.Width != CanvasWidth || _blank.Height != CanvasHeight)
                _blank = new PixelImage(Math.Max(1, CanvasWidth), Math.Max(1, CanvasHeight));
            return _blank;
        }
    }

    /// <summary>현재 셀에 그리기 시작(빈 셀이면 새로 만듦). 그룹에는 그릴 수 없다.</summary>
    public void BeginStroke()
    {
        if (Project is not { } p || p.Layers[_layer].IsGroup) return;
        _strokeImage = p.BeginCelStroke(_layer, _frame);
    }
    /// <summary>스트로크 확정(바뀐 것이 없으면 기록 안 함).</summary>
    public void CommitStroke(string label)
    {
        if (Project is null) return;
        _strokeImage = null;
        Project.CommitCelStroke(label);
    }

    /// <summary>스트로크 취소: 셀을 시작 전으로 되돌리고 기록하지 않는다.</summary>
    public void CancelStroke()
    {
        if (Project is null) return;
        _strokeImage = null;
        Project.CancelCelStroke();
        InvalidateCurrent();
    }

    /// <summary>
    /// 타임라인에서 고른 셀들(레이어·프레임 번호) — 이동 도구가 지금 셀과 함께 옮길 셀. 창이 타임라인 선택에 잇는다(null이면 지금 셀만).
    /// </summary>
    public Func<IReadOnlyList<(int Layer, int Frame)>>? SelectedCels { get; set; }

    /// <summary>
    /// 이동 도구로 지금 셀 전체를 (<paramref name="dx"/>, <paramref name="dy"/>) 민 스트로크를 확정한다. 타임라인에서 다른 셀도 함께 골라 두었으면
    /// 그 셀들도 같은 만큼 밀어 실행취소 한 단계로 묶는다(그룹·빈 셀·잠긴 레이어는 건너뜀). <paramref name="wrap"/>이면 밖으로 나간 픽셀이 반대쪽으로,
    /// 아니면 일반 레이어는 캔버스 밖 그림까지 통째로 옮겨 밖으로 나간 픽셀을 남긴다(<see cref="SpriteProject.ShiftCels"/>, 부위는 부위 칸 밖이 잘림).
    /// 지금 셀이 일반 레이어이고 순환이 아니면 열린 스트로크(미리 보기)는 버리고 모델에서 다시 옮긴다. 함께 민 다른 셀 수를 돌려준다.
    /// <paramref name="movePivot"/>가 false면(이동 도구 우클릭 끌기) 레이어 피벗은 제자리에 둔다.
    /// </summary>
    public int CommitCelShift(string label, int dx, int dy, bool wrap, bool movePivot = true)
    {
        if (Project is not { } p) return 0;
        var others = (SelectedCels?.Invoke() ?? []).Where(c => c.Layer != _layer || c.Frame != _frame).Distinct().ToList();
        if (!wrap && p.Layers[_layer].Kind == LayerKind.Normal) return CommitCelShiftKeepingOutside(p, label, others, dx, dy, movePivot);
        if (others.Count == 0 || (dx == 0 && dy == 0))
        {
            CommitStroke(label);
            return 0;
        }
        int layer = _layer, frame = _frame;
        var after = TakeStrokeResult(p);
        var moved = ShiftedPixels(p, others, dx, dy, wrap);
        if (after is null && moved.Count == 0) return 0;
        p.Edit(label, ProjectChange.Pixels, () =>
        {
            if (after is not null) p.SetCel(layer, frame, after);
            foreach (var (l, f, image) in moved) p.SetCel(l, f, image);
        });
        return moved.Count;
    }

    /// <summary>순환 없이 일반 레이어 지금 셀을 옮김: 일반 레이어 셀은 캔버스 밖 그림까지 옮기고, 함께 고른 부위 셀은 부위 칸 안에서 민다(실행취소 한 단계).</summary>
    private int CommitCelShiftKeepingOutside(SpriteProject p, string label, List<(int Layer, int Frame)> others, int dx, int dy, bool movePivot)
    {
        if (_strokeImage is not null)
        {
            _strokeImage = null;
            p.CancelCelStroke();
        }
        InvalidateCurrent();
        if (dx == 0 && dy == 0) return 0;
        var normal = others.Where(c => c.Layer >= 0 && c.Layer < p.Layers.Count && p.Layers[c.Layer].Kind == LayerKind.Normal).Prepend((_layer, _frame)).ToList();
        var parts = ShiftedPixels(p, others.Where(c => c.Layer >= 0 && c.Layer < p.Layers.Count && p.Layers[c.Layer].IsPart).ToList(), dx, dy, wrap: false);
        bool current = !p.IsCelEmpty(_layer, _frame) || p.GetCelOverflow(_layer, _frame) is not null;
        int shifted = 0;
        if (parts.Count == 0) shifted = p.ShiftCels(label, normal, dx, dy, movePivot);
        else
            p.Edit(label, ProjectChange.Pixels, () =>
            {
                shifted = p.ShiftCels(label, normal, dx, dy, movePivot);
                foreach (var (l, f, image) in parts) p.SetCel(l, f, image);
            });
        return Math.Max(0, shifted - (current ? 1 : 0)) + parts.Count;
    }

    /// <summary>셀들을 셀 칸 안에서 민 새 그림(그룹·빈 셀·잠긴 레이어는 건너뜀).</summary>
    private static List<(int Layer, int Frame, PixelImage Image)> ShiftedPixels(SpriteProject p, IEnumerable<(int Layer, int Frame)> cels, int dx, int dy, bool wrap)
    {
        var moved = new List<(int Layer, int Frame, PixelImage Image)>();
        foreach (var (l, f) in cels)
        {
            if (l < 0 || l >= p.Layers.Count || f < 0 || f >= p.Frames.Count || p.Layers[l].IsGroup || p.IsLayerLocked(l) || p.IsCelEmpty(l, f)) continue;
            var cel = p.GetCel(l, f)!;
            var image = new PixelImage(cel.Width, cel.Height);
            PixelOps.Shift(cel, image, cel.Bounds, dx, dy, wrap);
            moved.Add((l, f, image));
        }
        return moved;
    }

    /// <summary>스트로크 중 픽셀이 바뀌었음을 알린다(다시 그리기).</summary>
    public void NotifyPixels()
    {
        if (Project is null) return;
        InvalidateCurrent(stroking: Project.IsStrokeActive);
        Project.NotifyPixelsChanged();
    }

    // ───────────── 선택 영역 편집(실행취소 묶음) ─────────────

    /// <summary>
    /// 선택 영역을 바꾸는 동작 전에 부른다: 아직 열린 기록이 없으면 현재 셀 스트로크를 시작한다.
    /// 변형·캔버스 크기 모드면 다른 편집이므로 먼저 적용한다.
    /// </summary>
    public void BeginSelectionEdit()
    {
        if (Project is null) return;
        if (IsTransforming || IsCanvasResizing) CommitFloating();
        if (_selectionStrokeOpen) return;
        BeginStroke();
        _selectionStrokeOpen = true;
    }

    /// <summary>
    /// 떠 있는 픽셀을 내려놓고 열린 선택 편집 기록을 확정한다. 변형 중이면 변형을 적용하고(실행취소 한 단계),
    /// 캔버스 크기 모드면 그것을 적용한다(실행취소 한 단계). 먼저 <see cref="CommittingEdits"/>를 알린다(캔버스 글씨 편집 확정).
    /// </summary>
    public void CommitFloating()
    {
        if (Project is not { } p) return;
        CommittingEdits?.Invoke(this, EventArgs.Empty);
        ApplyCanvasResize();
        ApplyLayerTransform();
        var transform = _transform;
        _transform = null;
        if (Selection.IsFloating && _strokeImage is { } img) Selection.Anchor(img);
        if (_selectionStrokeOpen)
        {
            _selectionStrokeOpen = false;
            CommitStroke(transform is not null ? UiStrings.S_EditorContext_CommitFloating_19E799222E : UiStrings.S_EditorContext_CommitFloating_9E9E894C6C);
        }
        if (transform is not null) TransformChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 열린 스트로크를 기록 없이 되돌리고 지금 셀이 스트로크로 바뀐 그림(바뀐 것이 없으면 null)을 돌려준다 — 다른 셀들과 한 편집(실행취소 한 단계)으로
    /// 묶어 <see cref="SpriteProject.SetCel"/>로 다시 넣을 때.
    /// </summary>
    private PixelImage? TakeStrokeResult(SpriteProject p)
    {
        var after = _strokeImage?.Clone();
        _strokeImage = null;
        p.CancelCelStroke();
        if (after is null) return null;
        var before = p.GetCel(_layer, _frame);
        return (before is null ? after.GetOpaqueBounds().IsEmpty : after.ContentEquals(before)) ? null : after;
    }

    // ───────────── 변형 모드 ─────────────

    /// <summary>
    /// 떠 있는 픽셀 변형(넣은 그림을 들고 들어간 변형 — <see cref="BeginPlacedTransform"/>): 원본(여백을 바꾸면 바뀜), 지금 값, 회전 중심(원본 좌표), 시작 전 선택 상태.
    /// </summary>
    private sealed class FloatingTransform(FreeTransform source, SelectionState before)
    {
        /// <summary>여백 사각형에 맞춘 원본(<see cref="Source"/>는 여기서 여백 모양 밖을 뺀 것).</summary>
        public FreeTransform Framed { get; set; } = source;
        /// <summary>그리는 원본: 여백 모양(<see cref="MarginQuad"/>)이 있으면 그 밖을 뺀 것, 아니면 <see cref="Framed"/>.</summary>
        public FreeTransform Source { get; set; } = source;
        public SelectionState Before { get; } = before;
        public TransformParams Params { get; set; } = source.Box.Identity;
        public (double X, double Y) PivotSource { get; set; } = source.Box.Center;
        public (double X, double Y)[]? MarginQuad { get; set; }
    }

    /// <summary>
    /// 셀 변형(Ctrl+T, 지금 셀)·레이어 변형(Alt+Shift+C, 레이어 전체): 픽셀 엔진, 지금 값(기준 프레임 값), 회전 중심(원본 좌표), 미리보기.
    /// 셀은 적용할 때까지 그대로이고 캔버스에는 미리보기 합성을 그린다.
    /// </summary>
    private sealed class LayerTransformSession(SpriteProject.LayerTransform engine)
    {
        public SpriteProject.LayerTransform Engine { get; } = engine;
        public TransformParams Params { get; set; } = engine.Box.Identity;
        public (double X, double Y) PivotSource { get; set; } = engine.Box.Center;
        /// <summary>
        /// 그림을 따라가는 레이어 피벗이 원본에서 가리키는 점과 기준 피벗(<see cref="SpriteProject.LayerTransform.ReferencePivot"/>)의 차이(원본 좌표).
        /// 그림만 옮기면(<see cref="MoveTransformImage"/>) 피벗이 제자리에 남도록 바뀐다.
        /// </summary>
        public (double X, double Y) PivotSlip { get; set; }
        /// <summary>
        /// Ctrl+손잡이 우클릭 끌기로 마음대로 옮긴 여백 상자의 네 꼭짓점(원본 좌표, 왼쪽 위·오른쪽 위·오른쪽 아래·왼쪽 아래). 실제 상자는 이것을 감싸는 사각형이고
        /// 원본은 이 모양 밖이 잘린다(적용해도 그대로). Ctrl 없이 여백을 끌거나 상자 크기 숫자를 바꾸면 없어진다(사각형 여백으로). 없으면 null.
        /// </summary>
        public (double X, double Y)[]? MarginQuad { get; set; }
        /// <summary>지금 프레임 합성 미리보기(캔버스 크기)와 캔버스 밖·잘릴 픽셀(흐리게, 캔버스 좌표). 값이 바뀌면 버린다.</summary>
        public WriteableBitmap? Preview { get; set; }
        public WriteableBitmap? Outside { get; set; }
        public Rectangle OutsideRect { get; set; }
        /// <summary>대상 레이어 아래·위 레이어 합성(<see cref="BackdropFrame"/> 프레임). 프레임·프로젝트가 바뀌면 버린다.</summary>
        public PixelImage? Below { get; set; }
        public PixelImage? Above { get; set; }
        public int BackdropFrame { get; set; } = -1;
    }

    private FloatingTransform? _transform;
    private LayerTransformSession? _layerTransform;
    private ResampleMode _transformResample = ResampleMode.RotSprite;

    /// <summary>
    /// 열린 편집을 확정하기 직전(<see cref="CommitFloating"/> 맨 앞 — 저장·실행취소·레이어 바꾸기·구조 편집 앞):
    /// 캔버스에서 고치던 글씨를 확정할 때 쓴다.
    /// </summary>
    public event EventHandler? CommittingEdits;

    /// <summary>변형 모드에 들어가거나 나옴, 변형 값·여백·회전 중심·리샘플링·보이는 프레임이 바뀜.</summary>
    public event EventHandler? TransformChanged;

    /// <summary>변형 모드(Ctrl+T·Alt+Shift+C·넣은 그림 변형)인지.</summary>
    public bool IsTransforming => _transform is not null || _layerTransform is not null;

    /// <summary>레이어 전체 변형(Alt+Shift+C, 주황 상자 — 모든 프레임)인지. 아니면 지금 셀 변형(파란 상자).</summary>
    public bool IsLayerTransform => _layerTransform?.Engine.WholeLayer == true;

    /// <summary>선택 영역(또는 넣은 그림)만 바꾸는 변형인지 — 피벗은 그대로이고 여백을 넓히면 가장자리 픽셀로 채운다.</summary>
    public bool TransformHasSelection => _transform is not null || _layerTransform?.Engine.HasSelection == true;

    /// <summary>변형 상자(손잡이·값의 기준, 지금 그리는 그림 좌표). 변형 중이 아니면 null.</summary>
    public TransformBox? TransformBox => _layerTransform?.Engine.Box ?? _transform?.Source.Box;

    /// <summary>지금 변형 값(변형 중이 아니면 기본값).</summary>
    public TransformParams TransformParams => _layerTransform?.Params ?? _transform?.Params ?? default;

    /// <summary>변형 리샘플링(기본 RotSprite). 변형 중에 바꾸면 바로 다시 그린다.</summary>
    public ResampleMode TransformResample
    {
        get => _transformResample;
        set
        {
            if (!Enum.IsDefined(value)) value = ResampleMode.RotSprite;
            if (_transformResample == value) return;
            _transformResample = value;
            RenderTransform();
        }
    }

    /// <summary>레이어 변형이 부위를 늘릴 때 쓸 9-slice 가운데 채우기(캔버스 크기 모드의 9-slice 설정을 같이 씀). 끄면 null.</summary>
    private NineSliceFill? TransformSlice => _canvasResizeNineSlice ? _canvasResizeSliceFill : null;

    /// <summary>9-slice로 늘릴 수 있는 레이어 변형인지: 레이어 전체, 선택 없음, 테두리가 있는 부위.</summary>
    public bool TransformSliceAvailable
        => _layerTransform is { Engine: { WholeLayer: true, HasSelection: false, ReferenceLayer: { IsPart: true, PartBorder.IsNone: false } } };

    /// <summary>회전 중심(지금 그리는 그림 좌표, 연속). 변형 중이 아니면 (0,0).</summary>
    public (double X, double Y) TransformPivot
    {
        get
        {
            if (TransformBox is not { } box) return (0, 0);
            var (x, y) = _layerTransform?.PivotSource ?? _transform!.PivotSource;
            return box.Map(TransformParams, x, y);
        }
    }

    /// <summary>
    /// 변형 모드에 들어간다: <paramref name="wholeLayer"/>면 지금 레이어의 모든 프레임(Alt+Shift+C, 프레임마다 그 피벗을 중심으로 돎), 아니면 지금 셀 하나(Ctrl+T).
    /// 선택 영역이 있으면 그 모양 안 픽셀만(피벗은 그대로), 없으면 칠한 픽셀 전체(피벗도 따라감).
    /// 셀은 적용할 때까지 그대로이고 캔버스는 미리보기를 그린다. 회전 중심은 처음에 지금 레이어의 피벗 자리.
    /// 못 들어가면 상태 표시줄에 보일 이유, 들어갔으면(이미 변형 중이어도) null.
    /// </summary>
    public string? BeginTransform(bool wholeLayer)
    {
        if (Project is not { } p) return UiStrings.S_EditorContext_BeginTransform_E708BAFBC9;
        if (IsTransforming) return null;
        if (IsGroupSelected) return wholeLayer ? UiStrings.LayerTransform_Group : UiStrings.S_EditorContext_BeginTransform_10405489AE;
        // 캔버스 크기 모드·떠 있는 선택은 먼저 적용한다(캔버스가 바뀌면 선택이 풀리므로 선택을 읽기 전에).
        CommitFloating();
        if (!CanEdit) return wholeLayer ? UiStrings.LayerTransform_Locked : UiStrings.S_EditorContext_BeginTransform_C545365EE1;
        PixelMask? selection = Selection.HasSelection ? Selection.Region : null;
        if (SpriteProject.LayerTransform.Create(p, _layer, _frame, selection, wholeLayer) is not { } engine)
            return wholeLayer ? UiStrings.LayerTransform_Empty : UiStrings.S_EditorContext_BeginTransform_433795A813;
        var (px, py) = CurrentLayerPivotLocal();
        _layerTransform = new LayerTransformSession(engine) { PivotSource = (px, py) };
        TransformChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>
    /// 방금 지금 셀에 넣은 그림(<paramref name="image"/>를 <paramref name="location"/>에 — 캔버스 밖은 캔버스 밖 그림으로 들어감)을 그대로 떠 있는 상태로 들고
    /// 변형 모드에 들어간다: 셀에서 넣은 픽셀과 캔버스 밖 그림을 지우고 원본 전체(투명 테두리는 뺌)를 띄운다. 적용(Enter)하면 그때 자리에 내려앉고
    /// 캔버스 밖으로 나간 픽셀은 다시 캔버스 밖 그림이 되며(실행취소 한 단계 — 넣기와 따로), 취소(Esc)하면 넣은 그대로 돌아간다.
    /// 못 들어가면 상태 표시줄에 보일 이유, 들어갔으면 null.
    /// </summary>
    public string? BeginPlacedTransform(PixelImage image, Point location)
    {
        if (Project is null) return UiStrings.S_EditorContext_BeginTransform_E708BAFBC9;
        CommitFloating();
        if (IsGroupSelected) return UiStrings.S_EditorContext_BeginTransform_10405489AE;
        if (!CanEdit) return UiStrings.S_EditorContext_BeginTransform_C545365EE1;
        var content = image.GetOpaqueBounds();
        if (content.IsEmpty) return UiStrings.S_EditorContext_BeginTransform_433795A813;
        var floating = image.Crop(content);
        var region = new PixelMask(new Rectangle(location.X + content.X, location.Y + content.Y, content.Width, content.Height));
        var before = Selection.Save();
        BeginSelectionEdit();
        var cel = EditImage!;
        // 넣으며 셀에 그려진 픽셀과 캔버스 밖 그림을 지운다 — 떠 있는 원본이 그 자리를 대신한다(옮긴 뒤 예전 자리에 남지 않게).
        Project.ClearStrokeOverflow();
        var r = region.Bounds;
        for (int y = 0; y < floating.Height; y++)
            for (int x = 0; x < floating.Width; x++)
                if (floating.Pixels[y * floating.Width + x].A != 0) cel.Set(r.X + x, r.Y + y, Color32.Transparent);
        Selection.SetFloating(floating, region);
        _transform = new FloatingTransform(new FreeTransform(floating, region), before);
        NotifyPixels();
        TransformChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>변형 값을 바꾸고 원본에서 다시 그린다(끄는 동안의 미리보기도 이것). 배율 0은 막는다.</summary>
    public void SetTransformParams(TransformParams p)
    {
        if (!IsTransforming) return;
        if (!double.IsFinite(p.CenterX) || !double.IsFinite(p.CenterY) || !double.IsFinite(p.Angle)
            || !double.IsFinite(p.ScaleX) || !double.IsFinite(p.ScaleY) || p.ScaleX == 0 || p.ScaleY == 0) return;
        p = p with { Angle = TransformParams.NormalizeAngle(p.Angle) };
        if (p == TransformParams) return;
        if (_layerTransform is { } s) s.Params = p;
        else _transform!.Params = p;
        RenderTransform();
    }

    /// <summary>
    /// Ctrl+손잡이 우클릭 끌기로 마음대로 옮긴 여백 상자의 네 꼭짓점(원본 좌표, 왼쪽 위부터 시계 방향). 실제 상자(<see cref="TransformBox"/>)는 이것을 감싸는 사각형이다.
    /// 없으면 null.
    /// </summary>
    public (double X, double Y)[]? TransformMarginQuad => _layerTransform is { } s ? s.MarginQuad : _transform?.MarginQuad;

    /// <summary>
    /// 여백: 변형 상자의 원본 사각형을 <paramref name="bounds"/>(원본 좌표)로 바꾼다. 그림은 제자리에 있고, 좁힌 쪽 밖은 잘리며 넓힌 쪽은 선택 영역(넣은 그림)이면
    /// 가장자리 픽셀을 복사해 늘리고 아니면 빈 자리(부위는 영역이 넓어짐)가 된다. 크기는 1px 이상.
    /// <paramref name="quad"/>는 자유롭게 옮긴 여백 꼭짓점(<see cref="TransformMarginQuad"/> — 상자는 그것을 감싸는 사각형으로 넘기고, 원본은 그 모양 밖이 잘림), null이면 지운다.
    /// </summary>
    public void SetTransformBox(Rectangle bounds, (double X, double Y)[]? quad = null)
    {
        bounds.Width = Math.Clamp(bounds.Width, 1, PixelImage.MaxSize);
        bounds.Height = Math.Clamp(bounds.Height, 1, PixelImage.MaxSize);
        bool quadChanged = !SameQuad(TransformMarginQuad, quad);
        // 여백 모양: 원본에서 이 모양 안 픽셀만 남긴다(상자는 그것을 감싸는 사각형).
        PixelMask? clip = quad is null ? null : PixelMask.CentersInside(quad);
        if (_layerTransform is { } s)
        {
            if (bounds == s.Engine.Box.Bounds && !quadChanged) return;
            s.MarginQuad = quad;
            if (bounds != s.Engine.Box.Bounds) s.Params = s.Engine.SetBox(bounds, s.Params);
            if (quadChanged) s.Engine.Clip = clip;
        }
        else if (_transform is { } t)
        {
            if (bounds == t.Framed.SourceBounds && !quadChanged) return;
            t.MarginQuad = quad;
            if (bounds != t.Framed.SourceBounds)
            {
                var next = t.Framed.Reframe(bounds, extendEdges: true);
                t.Params = t.Framed.Box.ParamsFor(next.Box, t.Params);
                t.Framed = next;
            }
            t.Source = clip is { } c ? t.Framed.Clip(c) : t.Framed;
        }
        else return;
        RenderTransform();
    }

    /// <summary>자유롭게 옮긴 여백 꼭짓점을 지우고 그것을 감싸는 사각형(지금 상자)만 남긴다. 없으면 아무것도 하지 않는다.</summary>
    public void DropTransformMarginQuad()
    {
        if (TransformMarginQuad is null || TransformBox is not { } box) return;
        SetTransformBox(box.Bounds);
    }

    /// <summary>두 꼭짓점 목록이 같은지(둘 다 null이면 같음).</summary>
    private static bool SameQuad((double X, double Y)[]? a, (double X, double Y)[]? b)
        => a is null || b is null ? a is null && b is null : a.AsSpan().SequenceEqual(b);

    /// <summary>변형 상자를 그 가운데를 지나는 축으로 뒤집는다(<paramref name="horizontal"/> = 좌우, 상자의 가로 축 기준 — 배율 부호를 바꿈).</summary>
    public void FlipTransform(bool horizontal)
    {
        var p = TransformParams;
        SetTransformParams(horizontal ? p with { ScaleX = -p.ScaleX } : p with { ScaleY = -p.ScaleY });
    }

    /// <summary>회전 중심을 지금 그리는 그림 좌표로 옮긴다(그림은 그대로).</summary>
    public void SetTransformPivot(double x, double y)
    {
        if (TransformBox is not { } box) return;
        var src = box.Unmap(TransformParams, x, y);
        if (!double.IsFinite(src.X) || !double.IsFinite(src.Y)) return;
        if (_layerTransform is { } s) s.PivotSource = src;
        else _transform!.PivotSource = src;
        TransformChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>회전 중심을 원본 가운데로 되돌린다.</summary>
    public void ResetTransformPivot()
    {
        if (TransformBox is not { } box) return;
        if (_layerTransform is { } s) s.PivotSource = box.Center;
        else _transform!.PivotSource = box.Center;
        TransformChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 회전 중심을 기준으로 <paramref name="angle"/>(도)로 돌린 값: 회전 중심은 화면에서 제자리에 있다.
    /// </summary>
    public TransformParams RotatedAboutPivot(TransformParams start, double angle)
    {
        if (TransformBox is not { } box) return start;
        var (sx, sy) = _layerTransform?.PivotSource ?? _transform!.PivotSource;
        var (px, py) = box.Map(start, sx, sy);
        return RotatedAbout(start, angle, px, py);
    }

    /// <summary>
    /// 점 (<paramref name="px"/>, <paramref name="py"/>)(지금 그리는 그림 좌표)를 기준으로 <paramref name="angle"/>(도)로 돌린 값: 그 점은 화면에서 제자리에 있다.
    /// </summary>
    public static TransformParams RotatedAbout(TransformParams start, double angle, double px, double py)
    {
        double rad = (angle - start.Angle) * Math.PI / 180;
        double s = Math.Sin(rad), c = Math.Cos(rad);
        double dx = start.CenterX - px, dy = start.CenterY - py;
        return start with { Angle = angle, CenterX = px + dx * c - dy * s, CenterY = py + dx * s + dy * c };
    }

    /// <summary>회전 중심을 기준으로 지금 각도에서 <paramref name="degrees"/>(도, 시계 방향)만큼 더 돌린다(우클릭 메뉴의 90°·180° 회전).</summary>
    public void RotateTransformBy(double degrees)
    {
        var p = TransformParams;
        SetTransformParams(RotatedAboutPivot(p, p.Angle + degrees));
    }

    /// <summary>
    /// 그림만 (<paramref name="dx"/>, <paramref name="dy"/>)만큼 옮긴다(우클릭 끌기): 회전 중심과, 선택 없이 셀·레이어를 바꿀 때 그림을 따라가는 레이어 피벗은
    /// 화면에서 제자리에 둔다(적용해도 피벗은 그 자리).
    /// </summary>
    public void MoveTransformImage(double dx, double dy)
    {
        if (TransformBox is not { } box || (dx == 0 && dy == 0)) return;
        var p = TransformParams;
        var next = p with { CenterX = p.CenterX + dx, CenterY = p.CenterY + dy };
        var (cx, cy) = TransformPivot;
        var center = box.Unmap(next, cx, cy);
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y)) return;
        if (_layerTransform is { } s)
        {
            var (rx, ry) = s.Engine.ReferencePivot;
            var at = box.Map(p, rx + s.PivotSlip.X, ry + s.PivotSlip.Y);
            var src = box.Unmap(next, at.X, at.Y);
            if (!double.IsFinite(src.X) || !double.IsFinite(src.Y)) return;
            s.PivotSlip = (src.X - rx, src.Y - ry);
            s.PivotSource = center;
        }
        else _transform!.PivotSource = center;
        SetTransformParams(next);
    }

    /// <summary>변형을 적용한다(실행취소 한 단계). 선택 영역 변형이면 옮긴 모양이 선택으로 남는다.</summary>
    public void CommitTransform() => CommitFloating();

    /// <summary>변형을 취소한다: 셀 픽셀과 선택을 변형 전 그대로 둔다. 기록은 남지 않는다.</summary>
    public void CancelTransform()
    {
        if (Project is null) return;
        if (_layerTransform is { } s)
        {
            _layerTransform = null;
            DropLayerTransformPreview(s);
        }
        else if (_transform is { } t)
        {
            _transform = null;
            _selectionStrokeOpen = false;
            CancelStroke();
            Selection.Restore(t.Before);
        }
        else return;
        TransformChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>값이 바뀌면 다시 그린다: 넣은 그림은 떠 있는 픽셀을, 셀·레이어 변형은 미리보기를.</summary>
    private void RenderTransform()
    {
        if (_layerTransform is { } s) DropLayerTransformPreview(s);
        else if (_transform is { } t)
        {
            var r = t.Source.Render(t.Params, _transformResample);
            Selection.SetFloating(r.Image, r.Region);
            NotifyPixels();
        }
        TransformChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>셀·레이어 변형을 적용한다(실행취소 한 단계). 선택 영역 변형이면 옮긴 모양을 선택으로 남긴다.</summary>
    private void ApplyLayerTransform()
    {
        if (_layerTransform is not { } s) return;
        _layerTransform = null;
        DropLayerTransformPreview(s);
        var engine = s.Engine;
        var layer = engine.ReferenceLayer;
        var origin = layer.IsPart ? layer.PartOffset : Point.Empty;
        var after = engine.SelectionAfter(s.Params, _transformResample);
        string label = engine.WholeLayer ? UiStrings.LayerTransform_Undo : UiStrings.S_EditorContext_CommitFloating_19E799222E;
        if (engine.Commit(label, s.Params, _transformResample, TransformSlice, s.PivotSlip) && after is { } mask && ReferenceEquals(CurrentLayerObject, layer))
        {
            // 부위 영역이 넓어졌으면 부위 좌표가 그만큼 옮겨 간다.
            var moved = layer.IsPart ? layer.PartOffset : Point.Empty;
            Selection.Select(EditImageOrEmpty, mask.Offset(origin.X - moved.X, origin.Y - moved.Y));
        }
        TransformChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 레이어 전체 변형 중 같은 레이어의 다른 프레임으로 감: 그 프레임을 기준으로 값과 회전 중심을 옮긴다(모든 프레임의 결과는 그대로).
    /// </summary>
    private void RebaseLayerTransform(SpriteProject p)
    {
        if (_layerTransform is not { } s) return;
        var (q, center) = s.Engine.Rebase(p.Frames[_frame], s.Params, TransformPivot);
        s.Params = q;
        s.PivotSource = s.Engine.Box.Unmap(q, center.X, center.Y);
        DropLayerTransformPreview(s);
        TransformChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>모드 중에 프로젝트가 바뀜(레이어 보이기 등): 대상 레이어가 없어졌거나 잠겼으면 취소하고, 아니면 미리보기를 새 합성으로.</summary>
    private void SyncLayerTransform(SpriteProject p)
    {
        if (_layerTransform is not { } s) return;
        int index = p.IndexOf(s.Engine.ReferenceLayer);
        if (index < 0 || p.IsLayerLocked(index) || !p.Frames.Contains(s.Engine.ReferenceFrame))
        {
            CancelTransform();
            return;
        }
        s.Below = s.Above = null;
        s.BackdropFrame = -1;
        DropLayerTransformPreview(s);
        TransformChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void DropLayerTransformPreview(LayerTransformSession s)
    {
        s.Preview?.Dispose();
        s.Preview = null;
        s.Outside?.Dispose();
        s.Outside = null;
    }

    /// <summary>셀·레이어 변형의 미리보기(지금 프레임 합성, 캔버스 크기 — 대상 레이어는 효과 없이 변형한 모습). 그런 변형이 아니면 null. 호출자는 Dispose하지 않는다.</summary>
    public WriteableBitmap? LayerTransformPreview
    {
        get
        {
            if (_layerTransform is not { } s) return null;
            EnsureLayerTransformPreview(s);
            return s.Preview;
        }
    }

    /// <summary>미리보기의 캔버스 밖으로 나간 픽셀과 잘릴 픽셀(흐리게 그림)과 그 자리(캔버스 좌표). 없으면 null. 호출자는 Dispose하지 않는다.</summary>
    public (WriteableBitmap Bitmap, Rectangle Rect)? LayerTransformOutside
    {
        get
        {
            if (_layerTransform is not { } s) return null;
            EnsureLayerTransformPreview(s);
            return s.Outside is { } bmp ? (bmp, s.OutsideRect) : null;
        }
    }

    /// <summary>
    /// 미리보기를 만든다: 아래 레이어 합성 위에 대상 레이어의 지금 프레임 결과(레이어 불투명도, 효과 없이)를 놓고 위 레이어 합성을 덮는다.
    /// 캔버스 밖으로 나간 결과와 상자 밖으로 잘릴 원래 픽셀은 <see cref="LayerTransformSession.Outside"/>로 따로 둔다.
    /// </summary>
    private void EnsureLayerTransformPreview(LayerTransformSession s)
    {
        if (s.Preview is not null || Project is not { } p || _frame < 0 || _frame >= p.Frames.Count) return;
        var layer = s.Engine.ReferenceLayer;
        int index = p.IndexOf(layer);
        if (index < 0) return;
        if (s.BackdropFrame != _frame || s.Below is null)
        {
            if (layer.IsPart)
            {
                s.Below = p.PlanFrame(_frame, layerFilter: l => !ReferenceEquals(l, layer)).Render();
                s.Above = null;
            }
            else
            {
                // 그룹과 그 부위는 레이어 목록에 이어 붙어 있어 일반 레이어의 같은 쪽에 함께 있다.
                s.Below = p.PlanFrame(_frame, layerFilter: l => p.IndexOf(l) < index).Render();
                s.Above = p.PlanFrame(_frame, layerFilter: l => p.IndexOf(l) > index).Render();
            }
            s.BackdropFrame = _frame;
        }
        var preview = s.Below.Clone();
        var at = layer.IsPart ? p.GetPartOffset(layer, _frame) : Point.Empty;
        var outside = new List<(PixelImage Image, Point Origin)>();
        var canvas = new Rectangle(Point.Empty, p.Size);
        void Place(PixelImage image, Point origin)
        {
            var rect = new Rectangle(origin.X + at.X, origin.Y + at.Y, image.Width, image.Height);
            preview.Composite(image, rect.X, rect.Y, layer.Opacity);
            if (canvas.Contains(rect)) return;
            var cut = image.Clone();
            cut.FillRect(new Rectangle(-rect.X, -rect.Y, canvas.Width, canvas.Height), Color32.Transparent);
            outside.Add((cut, rect.Location));
        }
        var frame = p.Frames[_frame];
        if (s.Engine.ShownTarget(frame) is { } target)
        {
            if (s.Engine.Render(target, s.Params, _transformResample, TransformSlice) is { } r) Place(r.Image, r.Origin);
            if (s.Engine.CutPixels(target) is { } cut) outside.Add((cut.Image, new Point(cut.Origin.X + at.X, cut.Origin.Y + at.Y)));
        }
        else if (layer.IsPart) { if (p.GetCel(index, _frame) is { } cel) Place(cel, Point.Empty); }
        else
        {
            var (full, origin) = p.GetFullCel(index, _frame);
            Place(full, origin);
        }
        if (s.Above is { } above) preview.Composite(above, 0, 0, 255);
        s.Preview = ImageUtil.ToBitmap(preview);
        if (outside.Count == 0) return;
        var bounds = outside.Select(o => new Rectangle(o.Origin, o.Image.Size)).Aggregate(Rectangle.Union);
        var all = new PixelImage(bounds.Width, bounds.Height);
        foreach (var (image, origin) in outside) all.Composite(image, origin.X - bounds.X, origin.Y - bounds.Y, layer.Opacity);
        s.Outside = ImageUtil.ToBitmap(all);
        s.OutsideRect = bounds;
    }

    // ───────────── 캔버스 크기 모드 ─────────────

    /// <summary>
    /// 캔버스 크기 모드 상태: 원래 그림(현재 프레임 합성), 새 캔버스 자리, 원래 그림이 놓일 사각형, 미리보기.
    /// </summary>
    private sealed class CanvasResizeSession(PixelImage source)
    {
        /// <summary>현재 프레임 합성(미리보기 원본 — 늘 이것에서 다시 그려 누적되지 않음).</summary>
        public PixelImage Source { get; set; } = source;
        /// <summary>새 캔버스(원래 프레임 좌표: 가장자리를 끄는 동안 화면에서 제자리에 보이게).</summary>
        public Rectangle Canvas { get; set; } = new(0, 0, source.Width, source.Height);
        /// <summary>원래 그림이 놓일 사각형(새 캔버스 좌표, 크기 = 그림 크기).</summary>
        public Rectangle Image { get; set; } = new(0, 0, source.Width, source.Height);
        /// <summary>
        /// Ctrl+손잡이 끌기로 마음대로 옮긴 캔버스 테두리의 네 꼭짓점(원래 프레임 좌표, 왼쪽 위부터 시계 방향). 새 캔버스(<see cref="Canvas"/>)는 이것을 감싸는 사각형.
        /// Ctrl 없이 손잡이를 끌거나 숫자를 바꾸면 없어진다. 없으면 null.
        /// </summary>
        public (double X, double Y)[]? Quad { get; set; }
        /// <summary>
        /// <see cref="Quad"/>를 늘리기(왼쪽 Ctrl 끌기)로 만들었으면 원래 캔버스의 네 꼭짓점이 놓일 자리(새 캔버스 좌표) — 그림을 그 모양으로 원근 변형하고 남는 자리는 여백.
        /// 여백(우클릭)으로 만든 모양이거나 모양이 없으면 null.
        /// </summary>
        public (double X, double Y)[]? Warp { get; set; }
        /// <summary>원근 변형을 시작할 때의 캔버스·그림 사각형(<see cref="Warp"/>를 셈하는 기준).</summary>
        public Rectangle WarpBaseCanvas { get; set; }
        public Rectangle WarpBaseImage { get; set; }
        /// <summary>원근 변형 중에 그림만 옮긴 양(새 캔버스 좌표, 변형한 뒤에 더함).</summary>
        public Point WarpShift { get; set; }
        /// <summary>중심 기준 크기 조정의 고정된 캔버스 중심(픽셀 두 배 좌표).</summary>
        public Point Center2 { get; set; }
        public bool ResizeFromCenter { get; set; }
        /// <summary>결과 미리보기(새 캔버스 크기). 값이 바뀌면 버리고 그릴 때 다시 만든다.</summary>
        public WriteableBitmap? Preview { get; set; }
        /// <summary>적용하면 될 그림 전체(캔버스 밖으로 나가는 부분 포함)와 그 자리(새 캔버스 좌표) — <see cref="Preview"/>와 함께 만들고 버린다.</summary>
        public WriteableBitmap? Outside { get; set; }
        public Rectangle OutsideRect { get; set; }
    }

    private CanvasResizeSession? _canvasResize;

    /// <summary>캔버스 크기 모드에 들어가거나 나옴, 값이 바뀜.</summary>
    public event EventHandler? CanvasResizeChanged;

    /// <summary>캔버스 크기 모드를 적용해 프로젝트가 바뀜(상태 표시줄 알림 글).</summary>
    public event EventHandler<string>? CanvasResized;

    /// <summary>캔버스 크기 모드(Ctrl+Alt+C)인지.</summary>
    public bool IsCanvasResizing => _canvasResize is not null;

    /// <summary>원래 그림 크기(원래 캔버스·프레임 크기). 모드가 아니면 빈 크기.</summary>
    public Size CanvasResizeSource => _canvasResize?.Source.Size ?? Size.Empty;

    /// <summary>캔버스 크기 모드의 원래 그림(현재 프레임 합성 — 특수 비율 창 미리보기). 모드가 아니면 null. 호출자는 바꾸지 않는다.</summary>
    public PixelImage? CanvasResizeSourceImage => _canvasResize?.Source;

    /// <summary>새 캔버스(원래 프레임 좌표, 크기 = 새 캔버스 크기). 모드가 아니면 빈 사각형.</summary>
    public Rectangle CanvasResizeCanvas => _canvasResize?.Canvas ?? Rectangle.Empty;

    /// <summary>원래 그림이 놓일 사각형(새 캔버스 좌표). 모드가 아니면 빈 사각형.</summary>
    public Rectangle CanvasResizeImage => _canvasResize?.Image ?? Rectangle.Empty;
    /// <summary>크기 변경 시 캔버스 중심을 고정하고 양쪽 가장자리를 함께 움직이는지.</summary>
    public bool CanvasResizeFromCenter
    {
        get => _canvasResize?.ResizeFromCenter ?? false;
        set
        {
            if (_canvasResize is not { } s || s.ResizeFromCenter == value) return;
            s.ResizeFromCenter = value;
            if (value) s.Center2 = new Point(2 * s.Canvas.X + s.Canvas.Width, 2 * s.Canvas.Y + s.Canvas.Height);
            CanvasResizeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>중심 기준을 켠 순간의 캔버스 중심(픽셀 두 배 좌표).</summary>
    public Point CanvasResizeCenter2 => _canvasResize?.Center2 ?? Point.Empty;

    private bool _canvasResizeNineSlice = true;
    private NineSliceFill _canvasResizeSliceFill = NineSliceFill.Stretch;

    /// <summary>캔버스 크기 모드에서 9-slice로 늘릴지(기본 켬, 앱을 켜 둔 동안 기억). 쓸 테두리가 없으면 켜 있어도 쓰지 않는다(<see cref="CanvasResizeUsesSlice"/>).</summary>
    public bool CanvasResizeNineSlice
    {
        get => _canvasResizeNineSlice;
        set
        {
            if (_canvasResizeNineSlice == value) return;
            _canvasResizeNineSlice = value;
            OnCanvasResizeSliceChanged();
        }
    }

    /// <summary>9-slice로 늘릴 때 가운데를 채우는 방법(늘이기·되풀이).</summary>
    public NineSliceFill CanvasResizeSliceFill
    {
        get => _canvasResizeSliceFill;
        set
        {
            if (_canvasResizeSliceFill == value) return;
            _canvasResizeSliceFill = value;
            OnCanvasResizeSliceChanged();
        }
    }

    /// <summary>프레임 칸 9-slice 테두리(캔버스 크기 모드가 늘일 테두리 — 쓸 수 없으면 없음).</summary>
    public SpriteBorder CanvasResizeBorder
        => Project is { } p && NineSlice.Applies(p.Sprite.Border, p.Width, p.Height) ? p.Sprite.Border : SpriteBorder.None;

    /// <summary>9-slice로 늘릴 테두리가 있는지: 프레임 칸 테두리 또는 자기 테두리가 있는 부위.</summary>
    public bool CanvasResizeSliceAvailable
        => Project is { } p && (!CanvasResizeBorder.IsNone || p.Layers.Any(l => l.IsPart && NineSlice.Applies(l.PartBorder, l.PartSize.Width, l.PartSize.Height)));

    /// <summary>
    /// 캔버스 크기 모드가 그림 크기를 9-slice로 바꾸는지(켰고 쓸 테두리가 있음). 조작(Ctrl+끌기·그림 가로/세로)은 같고 늘리는 방법만 바뀐다.
    /// </summary>
    public bool CanvasResizeUsesSlice => _canvasResizeNineSlice && CanvasResizeSliceAvailable;

    /// <summary>9-slice로 늘릴 때 그림의 가장 작은 크기: 프레임 칸 테두리 두 쪽 + 가운데 1px(테두리가 없으면 1).</summary>
    public Size CanvasResizeMinImage
    {
        get
        {
            var b = CanvasResizeUsesSlice ? CanvasResizeBorder : SpriteBorder.None;
            return b.IsNone ? new Size(1, 1) : new Size(b.Left + b.Right + 1, b.Top + b.Bottom + 1);
        }
    }

    private void OnCanvasResizeSliceChanged()
    {
        // 레이어 변형도 같은 9-slice 설정을 쓴다.
        if (_layerTransform is not null) RenderTransform();
        if (_canvasResize is not { } s) return;
        var min = CanvasResizeMinImage;
        s.Image = s.Image with { Width = Math.Max(s.Image.Width, min.Width), Height = Math.Max(s.Image.Height, min.Height) };
        DropCanvasResizePreview(s);
        CanvasResizeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 적용하면 될 모습(새 캔버스 크기, 현재 프레임 합성을 최근접 — 9-slice면 프레임 칸 테두리로). 보이는 일반 레이어의 캔버스 밖 그림도
    /// 같은 대응으로(효과 없이) 옮겨 새 캔버스에 들어오는 부분을 그린다. 모드가 아니면 null. 호출자는 Dispose하지 않는다.
    /// </summary>
    public WriteableBitmap? CanvasResizeBitmap
    {
        get
        {
            if (_canvasResize is not { } s) return null;
            EnsureCanvasResizePreview(s);
            return s.Preview;
        }
    }

    /// <summary>
    /// 캔버스 크기 모드: 적용하면 될 그림 전체(새 캔버스 밖으로 나가는 부분과 캔버스 밖 그림 포함)와 그 자리(새 캔버스 좌표) — 화면이 캔버스 밖을 흐리게 그린다.
    /// 모드가 아니거나 그림이 없으면 null. 호출자는 Dispose하지 않는다.
    /// </summary>
    public (WriteableBitmap Bitmap, Rectangle Rect)? CanvasResizeOutside
    {
        get
        {
            if (_canvasResize is not { } s) return null;
            EnsureCanvasResizePreview(s);
            return s.Outside is { } bmp ? (bmp, s.OutsideRect) : null;
        }
    }

    /// <summary>
    /// 캔버스 크기 모드를 적용하면 지금 프레임 피벗이 놓일 자리(새 캔버스 좌표, 연속). 피벗은 그림을 따라간다(<see cref="SpriteProject.MapResizedCanvasPoint"/>). 모드가 아니면 null.
    /// </summary>
    public System.Drawing.PointF? CanvasResizePivot
    {
        get
        {
            if (_canvasResize is not { } s || Project is not { } p || _frame < 0 || _frame >= p.Frames.Count) return null;
            var (fx, fy) = p.GetFramePivot(_frame).ToLocalPixel(s.Source.Width, s.Source.Height);
            if (s.Warp is { } warp)
            {
                var box = new TransformBox(new Rectangle(Point.Empty, s.Source.Size));
                if (box.WithCorners(box.Identity, warp) is not { } h) return null;
                var (wx, wy) = box.Map(h, fx, fy);
                return new System.Drawing.PointF((float)wx, (float)wy);
            }
            return SpriteProject.MapResizedCanvasPoint(new System.Drawing.PointF(fx, fy), s.Source.Size, s.Image, CanvasResizeUsesSlice ? CanvasResizeBorder : SpriteBorder.None);
        }
    }

    /// <summary>
    /// 미리보기를 만든다: 현재 프레임 합성 + 캔버스 밖 그림 전체를 적용과 같은 대응 하나로 옮기고(<see cref="ImageTransform.ScaleWithCanvas"/> — 캔버스 안·밖이
    /// 같은 배율), 새 캔버스 안은 <see cref="CanvasResizeSession.Preview"/>로, 전체는 <see cref="CanvasResizeSession.Outside"/>로 둔다.
    /// </summary>
    private void EnsureCanvasResizePreview(CanvasResizeSession s)
    {
        if (s.Preview is not null || Project is not { } p) return;
        var (full, origin) = FrameWithOverflow(p, s.Source);
        var canvas = s.Canvas.Size;
        int m = SpriteProject.OverflowMargin;
        var clip = new Rectangle(-m, -m, canvas.Width + 2 * m, canvas.Height + 2 * m);
        // 테두리 모양대로 늘리면 원근 변형(리샘플링은 변형 모드 설정), 아니면 그림 사각형으로 늘리기
        var moved = s.Warp is { } warp
            ? ImageTransform.WarpWithCanvas(full, origin, s.Source.Size, warp, clip, _transformResample)
            : ImageTransform.ScaleWithCanvas(full, origin, s.Source.Size, s.Image, clip, CanvasResizeUsesSlice ? CanvasResizeBorder : SpriteBorder.None, _canvasResizeSliceFill);
        var preview = new PixelImage(canvas.Width, canvas.Height);
        if (moved is { } mv)
        {
            var shown = mv.Image;
            // 여백 모양(사다리꼴 등)이면 그 밖은 적용할 때 지워지므로 미리보기에서도 뺀다(받은 그림은 원본일 수 있어 복사해서).
            if (CanvasResizeKeep(s) is { } keep)
            {
                shown = shown.Clone();
                for (int y = 0; y < shown.Height; y++)
                    for (int x = 0; x < shown.Width; x++)
                        if (!keep.Contains(mv.Origin.X + x, mv.Origin.Y + y)) shown.Pixels[y * shown.Width + x] = Color32.Transparent;
            }
            preview.Blit(shown, mv.Origin.X, mv.Origin.Y);
            s.Outside = ImageUtil.ToBitmap(shown);
            s.OutsideRect = new Rectangle(mv.Origin, shown.Size);
        }
        s.Preview = ImageUtil.ToBitmap(preview);
    }

    /// <summary>여백으로 테두리 꼭짓점을 마음대로 옮겼으면 남길 모양(새 캔버스 좌표 — 그 밖은 적용할 때 지워짐). 아니면 null.</summary>
    private static PixelMask? CanvasResizeKeep(CanvasResizeSession s)
        => s.Quad is { } quad && s.Warp is null ? PixelMask.CentersInside(quad.Select(c => (c.X - s.Canvas.X, c.Y - s.Canvas.Y)).ToArray()) : null;

    /// <summary>현재 프레임 합성(캔버스 안)에 보이는 일반 레이어의 캔버스 밖 그림을 아래 레이어부터(레이어 불투명도) 겹친 그림과 왼쪽 위(캔버스 좌표).</summary>
    private (PixelImage Image, Point Origin) FrameWithOverflow(SpriteProject p, PixelImage source)
    {
        var outside = new List<(PixelImage Image, Point Origin, byte Opacity)>();
        var rect = new Rectangle(Point.Empty, source.Size);
        for (int l = 0; l < p.Layers.Count; l++)
        {
            if (p.Layers[l].Kind != LayerKind.Normal || !p.IsLayerVisible(l) || p.GetCelOverflow(l, _frame) is not { } o) continue;
            outside.Add((o.Image, o.Origin, p.Layers[l].Opacity));
            rect = Rectangle.Union(rect, new Rectangle(o.Origin, o.Image.Size));
        }
        if (outside.Count == 0) return (source, Point.Empty);
        var full = new PixelImage(rect.Width, rect.Height);
        full.Blit(source, -rect.X, -rect.Y);
        foreach (var (image, at, opacity) in outside) full.Composite(image, at.X - rect.X, at.Y - rect.Y, opacity);
        return (full, rect.Location);
    }

    /// <summary>
    /// 캔버스 크기 모드에 들어간다: 떠 있는 선택·변형을 먼저 적용하고, 현재 프레임 합성 전체(부위를 골랐어도)를 미리보기로 보인다.
    /// 못 들어가면 상태 표시줄에 보일 이유, 들어갔으면(이미 모드여도) null.
    /// </summary>
    public string? BeginCanvasResize()
    {
        if (Project is null) return UiStrings.S_EditorContext_BeginCanvasResize_9D67D0C8F0;
        if (_canvasResize is not null) return null;
        CommitFloating();
        if (FrameImage(_frame) is not { } image) return UiStrings.S_EditorContext_BeginCanvasResize_1378D58776;
        _canvasResize = new CanvasResizeSession(image);
        CanvasResizeChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>Ctrl+손잡이 끌기로 마음대로 옮긴 캔버스 테두리의 네 꼭짓점(원래 프레임 좌표, 왼쪽 위부터 시계 방향). 새 캔버스는 이것을 감싸는 사각형. 없으면 null.</summary>
    public (double X, double Y)[]? CanvasResizeQuad => _canvasResize?.Quad;

    /// <summary>테두리 모양(<see cref="CanvasResizeQuad"/>)대로 그림을 원근 변형하는 중이면 원래 캔버스 네 꼭짓점이 놓일 자리(새 캔버스 좌표). 아니면 null.</summary>
    public (double X, double Y)[]? CanvasResizeWarp => _canvasResize?.Warp;

    /// <summary>
    /// 새 캔버스(원래 프레임 좌표)와 그림 사각형(새 캔버스 좌표)을 바꾼다. 크기는 1~<see cref="PixelImage.MaxSize"/>로 맞추고,
    /// 9-slice로 늘이면 그림은 테두리 두 쪽 + 1px보다 작아지지 않는다(<see cref="CanvasResizeMinImage"/>).
    /// <paramref name="quad"/>는 여백으로 자유롭게 옮긴 캔버스 테두리(<see cref="CanvasResizeQuad"/> — 캔버스는 그것을 감싸는 사각형으로 넘김), null이면 지운다.
    /// 원근 변형(<see cref="CanvasResizeWarp"/>)은 없앤다(그림은 <paramref name="image"/> 사각형으로).
    /// </summary>
    public void SetCanvasResize(Rectangle canvas, Rectangle image, (double X, double Y)[]? quad = null)
    {
        if (_canvasResize is not { } s) return;
        static int Clamp(int v, int min = 1) => Math.Clamp(v, min, PixelImage.MaxSize);
        var min = CanvasResizeMinImage;
        canvas.Size = new Size(Clamp(canvas.Width), Clamp(canvas.Height));
        image.Size = new Size(Clamp(image.Width, min.Width), Clamp(image.Height, min.Height));
        bool shapeChanged = !SameQuad(s.Quad, quad) || s.Warp is not null;
        s.Quad = quad;
        s.Warp = null;
        if (canvas == s.Canvas && image == s.Image && !shapeChanged) return;
        s.Canvas = canvas;
        s.Image = image;
        DropCanvasResizePreview(s);
        CanvasResizeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>원근 변형 중이면 그 기준(시작할 때의 캔버스·그림 사각형, 그 뒤 그림만 옮긴 양). 아니면 null.</summary>
    public (Rectangle Canvas, Rectangle Image, Point Shift)? CanvasResizeWarpBase
        => _canvasResize is { Warp: not null } s ? (s.WarpBaseCanvas, s.WarpBaseImage, s.WarpShift) : null;

    /// <summary>
    /// 늘리기를 Ctrl로: 캔버스 테두리 꼭짓점을 <paramref name="quad"/>(원래 프레임 좌표, 왼쪽 위부터 시계 방향)로 옮기고, 그림은 그 모양을 따라 원근으로 찌그러뜨린다
    /// (<paramref name="baseCanvas"/> 사각형이 그 모양으로 가는 대응 — 그 안의 그림 자리 <paramref name="baseImage"/>도 같은 대응, 그 뒤 <paramref name="shift"/>만큼 옮김).
    /// 새 캔버스는 그 모양을 감싸는 사각형이고 남는 자리는 여백이다. 꼬이거나 오목한 모양이면 받지 않고 false.
    /// </summary>
    public bool SetCanvasResizeWarp((double X, double Y)[] quad, Rectangle baseCanvas, Rectangle baseImage, Point shift)
    {
        if (_canvasResize is not { } s) return false;
        var box = new TransformBox(baseCanvas);
        if (box.WithCorners(box.Identity, quad) is not { } h) return false;
        int left = (int)Math.Floor(quad.Min(c => c.X)), top = (int)Math.Floor(quad.Min(c => c.Y));
        int right = (int)Math.Ceiling(quad.Max(c => c.X)), bottom = (int)Math.Ceiling(quad.Max(c => c.Y));
        var canvas = Rectangle.FromLTRB(left, top, Math.Max(right, left + 1), Math.Max(bottom, top + 1));
        if (canvas.Width > PixelImage.MaxSize || canvas.Height > PixelImage.MaxSize) return false;
        (double X, double Y) At(double u, double v)
        {
            var (x, y) = box.Map(h, baseCanvas.X + baseImage.X + u * baseImage.Width, baseCanvas.Y + baseImage.Y + v * baseImage.Height);
            return (x + shift.X - canvas.X, y + shift.Y - canvas.Y);
        }
        (double X, double Y)[] warp = [At(0, 0), At(1, 0), At(1, 1), At(0, 1)];
        int il = (int)Math.Floor(warp.Min(c => c.X)), it = (int)Math.Floor(warp.Min(c => c.Y));
        int ir = (int)Math.Ceiling(warp.Max(c => c.X)), ib = (int)Math.Ceiling(warp.Max(c => c.Y));
        s.WarpBaseCanvas = baseCanvas;
        s.WarpBaseImage = baseImage;
        s.WarpShift = shift;
        s.Quad = quad;
        s.Warp = warp;
        s.Canvas = canvas;
        s.Image = Rectangle.FromLTRB(il, it, Math.Max(ir, il + 1), Math.Max(ib, it + 1));
        DropCanvasResizePreview(s);
        CanvasResizeChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// 캔버스는 그대로 두고 그림을 (<paramref name="dx"/>, <paramref name="dy"/>)만큼 옮긴다(그림 안쪽 끌기·방향키). 테두리 모양은 그대로이고, 원근 변형 중이면 그 그림도 함께 옮긴다.
    /// </summary>
    public void MoveCanvasResizeImage(int dx, int dy)
    {
        if (_canvasResize is not { } s || (dx == 0 && dy == 0)) return;
        var image = s.Image;
        image.Offset(dx, dy);
        s.Image = image;
        if (s.Warp is { } warp)
        {
            s.Warp = warp.Select(c => (c.X + dx, c.Y + dy)).ToArray();
            s.WarpShift = new Point(s.WarpShift.X + dx, s.WarpShift.Y + dy);
        }
        DropCanvasResizePreview(s);
        CanvasResizeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>캔버스 크기 모드를 적용한다(바뀐 것이 있으면 실행취소 한 단계).</summary>
    public void CommitCanvasResize() => CommitFloating();

    /// <summary>캔버스 크기 모드를 취소한다(프로젝트는 그대로).</summary>
    public void CancelCanvasResize()
    {
        if (_canvasResize is not { } s) return;
        _canvasResize = null;
        DropCanvasResizePreview(s);
        CanvasResizeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 캔버스 크기 모드 중 뒤집기(<paramref name="horizontal"/> = 좌우): 캔버스 전체(모든 레이어·프레임)를 바로 뒤집고(실행취소 한 단계) 모드는 이어진다 —
    /// 미리보기는 뒤집힌 그림으로 다시 그린다(<see cref="SyncCanvasResize"/>). 모드 적용(<see cref="CommitFloating"/>)을 거치지 않도록 프로젝트를 직접 고친다.
    /// 상태 표시줄에 보일 글을 돌려준다. 모드가 아니면 null.
    /// </summary>
    public string? FlipDuringResize(bool horizontal)
    {
        if (_canvasResize is null || Project is not { } p) return null;
        string dir = horizontal ? UiStrings.Flip_DirHorizontal : UiStrings.Flip_DirVertical;
        if (horizontal) p.FlipCanvasHorizontal();
        else p.FlipCanvasVertical();
        return UiStrings.FormatCanvasResize_Flipped(direction: dir);
    }

    private void ApplyCanvasResize()
    {
        if (_canvasResize is not { } s || Project is not { } p) return;
        _canvasResize = null;
        DropCanvasResizePreview(s);
        var (canvas, image) = (s.Canvas.Size, s.Image);
        var keep = CanvasResizeKeep(s);
        bool changed = canvas != p.Size || image != new Rectangle(Point.Empty, p.Size) || s.Warp is not null || keep is not null;
        if (changed)
        {
            if (s.Warp is { } warp) p.WarpCanvas(canvas.Width, canvas.Height, warp, _transformResample);
            else p.ResizeCanvasAndScale(canvas.Width, canvas.Height, image, CanvasResizeUsesSlice ? _canvasResizeSliceFill : null, keep);
        }
        CanvasResizeChanged?.Invoke(this, EventArgs.Empty);
        if (!changed) return;
        bool scaled = image.Size != s.Source.Size;
        CanvasResized?.Invoke(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_EditorContext_ApplyCanvasResize_B3AC8F3521,canvas.Width,canvas.Height) + (scaled ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_EditorContext_ApplyCanvasResize_2F84F43E48,image.Width,image.Height) : ""));
    }

    /// <summary>
    /// 모드 중에 프로젝트가 바뀜(레이어 보이기 등): 캔버스 크기가 그대로면 미리보기 원본을 새 합성으로, 바뀌었으면 모드를 취소한다.
    /// </summary>
    private void SyncCanvasResize()
    {
        if (_canvasResize is not { } s || Project is not { } p) return;
        if (p.Size != s.Source.Size || FrameImage(_frame) is not { } image)
        {
            CancelCanvasResize();
            return;
        }
        s.Source = image;
        DropCanvasResizePreview(s);
        CanvasResizeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void DropCanvasResizePreview(CanvasResizeSession s)
    {
        s.Preview?.Dispose();
        s.Preview = null;
        s.Outside?.Dispose();
        s.Outside = null;
    }

    /// <summary>구조 편집(레이어·프레임·태그·설정) 전에 열린 그리기를 마무리한다.</summary>
    /// <summary>9-slice 대상의 테두리: 부위를 골랐으면 그 부위, 아니면 프레임 시트 모든 칸(프로젝트 설정).</summary>
    public SpriteBorder SliceBorder => CurrentPart?.PartBorder ?? Project?.Sprite.Border ?? SpriteBorder.None;

    /// <summary>9-slice 대상의 테두리를 바꾼다(실행취소 한 단계). 같으면 아무것도 하지 않는다.</summary>
    public void SetSliceBorder(SpriteBorder border)
    {
        if (Project is not { } p || border == SliceBorder) return;
        if (CurrentPart is { } part) SetPartBorder(part, border);
        else Structural(() => p.Edit(UiStrings.S_EditorContext_SetSliceBorder_738B84377F, ProjectChange.Settings, () => p.Sprite.Border = border));
    }

    /// <summary>부위 하나의 9-slice 테두리를 바꾼다(그룹 화면에서 끌 때, 실행취소 한 단계).</summary>
    public void SetPartBorder(Layer part, SpriteBorder border)
    {
        if (Project is not { } p || part.PartBorder == border) return;
        int index = p.IndexOf(part);
        Structural(() => p.SetPartBorder(index, border));
    }

    /// <summary>
    /// 부위를 옮긴다(실행취소 한 단계): <paramref name="offset"/> = 지금 프레임에서 놓일 위치 — 기준 위치를 같은 만큼 옮기므로 모든 프레임이 같이 옮겨 간다
    /// (프레임마다 옮긴 거리는 그대로). 그룹이 자동 배치 중이면 같은 단계에서 자동 배치를 끈다 — 먼저 <see cref="GroupLayoutDialog.ConfirmManualMove"/>로 물어본 뒤 부른다.
    /// </summary>
    public void MovePart(Layer part, Point offset)
    {
        if (Project is not { } p || p.IndexOf(part) is var index && index < 0) return;
        var shown = p.GetPartOffset(part, _frame);
        var target = new Point(part.PartOffset.X + offset.X - shown.X, part.PartOffset.Y + offset.Y - shown.Y);
        if (target == part.PartOffset) return;
        Structural(() => p.Edit(part.IsAutoLaidOut ? UiStrings.S_EditorContext_MovePart_9205E8CE1C : UiStrings.S_EditorContext_MovePart_B02E0EA2B6, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            if (part.Parent is { AutoLayout: not null } group) p.SetGroupLayout(group, null);
            p.SetPartOffset(index, target);
        }));
    }

    /// <summary>그룹 자동 배치 켜기·설정(null = 끄기, 부위는 그 자리에), 실행취소 한 단계.</summary>
    public void SetGroupLayout(Layer group, GroupLayout? layout)
    {
        if (Project is not { } p || !p.Layers.Contains(group)) return;
        Structural(() => p.SetGroupLayout(group, layout));
    }

    public void Structural(Action action)
    {
        if (Project is null) return;
        CommitFloating();
        action();
    }

    // ───────────── 스킨(착장) ─────────────

    /// <summary>그룹의 지금 스킨(착장)을 고른다(실행취소 기록 없음, 떠 있는 선택은 먼저 내려놓음).</summary>
    public void SetActiveSkin(Layer group, Skin skin)
    {
        if (Project is not { } p || ReferenceEquals(group.ActiveSkin, skin)) return;
        Structural(() => p.SetActiveSkin(group, skin));
    }

    /// <summary>부위 하나만 보일 스킨을 고른다(null = 그룹 스킨을 따름, 실행취소 기록 없음, 떠 있는 선택은 먼저 내려놓음).</summary>
    public void SetPartSkin(Layer part, Skin? skin)
    {
        if (Project is not { } p || ReferenceEquals(part.SkinOverride, skin)) return;
        Structural(() => p.SetPartSkin(part, skin));
    }

    /// <summary>그룹의 모든 부위를 그룹 스킨으로 되돌린다(재정의 지우기). 바뀐 것이 있으면 true.</summary>
    public bool ResetPartSkins(Layer group)
    {
        if (Project is not { } p) return false;
        bool changed = false;
        Structural(() => changed = p.ResetPartSkins(group));
        return changed;
    }

    /// <summary>그룹의 지금 스킨을 <paramref name="delta"/>칸 앞뒤로 돌린다(끝에서는 반대쪽으로). 스킨이 하나뿐이면 false.</summary>
    public bool StepSkin(Layer group, int delta)
    {
        var skins = group.Skins;
        if (skins.Count < 2 || group.ActiveSkin is not { } active) return false;
        int i = 0;
        while (i < skins.Count && !ReferenceEquals(skins[i], active)) i++;
        SetActiveSkin(group, skins[((i + delta) % skins.Count + skins.Count) % skins.Count]);
        return true;
    }

    public bool UndoStep()
    {
        if (Project is null) return false;
        CommitFloating();
        return Project.Undo();
    }

    public bool RedoStep()
    {
        if (Project is null) return false;
        CommitFloating();
        return Project.Redo();
    }

    // ───────────── 합성 결과(RenderService) ─────────────

    /// <summary>레이어 하나의 그리기에 영향을 주는 상태(이름·잠금 같은 것은 빠짐).</summary>
    private readonly record struct LayerRenderState(Layer Layer, LayerKind Kind, Layer? Parent, bool Visible, byte Opacity,
        IReadOnlyList<LayerEffect> Effects, Point Offset, Size PartSize, Skin? Shown)
    {
        public static LayerRenderState Of(Layer l) => new(l, l.Kind, l.Parent, l.Visible, l.Opacity, l.Effects, l.PartOffset, l.PartSize, l.ShownSkin);

        /// <summary>효과 목록 말고는 같은지.</summary>
        public bool SameExceptEffects(LayerRenderState other) => this with { Effects = other.Effects } == other;

        /// <summary>
        /// 그리기가 같은지: 효과 목록은 내용으로 비교한다(효과 창 확인·타임라인 끌기를 놓으면 미리보던 것과 같은 효과가 새 배열로 들어온다 —
        /// 참조로 비교하면 방금 다 그린 모든 프레임을 다시 계산했다).
        /// </summary>
        public bool SameRender(LayerRenderState other)
            => this == other || SameExceptEffects(other) && Effects.SequenceEqual(other.Effects);
    }

    private void RememberRenderState(SpriteProject? p)
    {
        _layerStates = p is null ? [] : p.Layers.Select(LayerRenderState.Of).ToArray();
        _frameOrder = p is null ? [] : p.Frames.ToArray();
    }

    /// <summary>
    /// 스트로크가 아닌 변경: 캔버스 크기가 바뀌면 모두 버리고, 셀 픽셀·프레임·레이어 구조가 바뀌면 모두 무효로 한다(그림은 다시 계산될 때까지 보임).
    /// 레이어 속성만 바뀌었으면 바뀐 레이어(와 그 그룹)의 셀·썸네일과 프레임 합성만 무효로 하고, 효과 목록만 바뀌었으면 효과 결과 캐시도 그대로 쓴다.
    /// 이름·잠금처럼 그리기와 관계없는 변경은 아무것도 무효로 하지 않는다 — 다만 다른 레이어나 9-slice 테두리를 읽는 스크립트 효과가 있으면
    /// (<see cref="SpriteProject.UsesOtherLayers"/>) 어느 변경이든 모든 결과를 무효로 한다(이름·순서·이웃 효과·테두리가 다른 레이어 결과를 바꿈).
    /// </summary>
    private void InvalidateRenders(SpriteProject p, ProjectChange change)
    {
        var before = _layerStates;
        var frames = _frameOrder;
        RememberRenderState(p);
        _thumbBounds.Clear();
        _pivotShifts.Clear();
        if ((change & ProjectChange.Canvas) != 0)
        {
            _renders.Reset();
            return;
        }
        bool sameStructure = before.Length == _layerStates.Length && frames.Length == _frameOrder.Length
            && before.Zip(_layerStates).All(t => ReferenceEquals(t.First.Layer, t.Second.Layer))
            && frames.Zip(_frameOrder).All(t => ReferenceEquals(t.First, t.Second));
        if (!sameStructure || (change & (ProjectChange.Pixels | ProjectChange.Frames)) != 0)
        {
            _renders.Effects.Clear();
            _renders.CheckpointLayer = null;
            _renders.InvalidateAll();
            return;
        }
        var changed = new HashSet<Layer>();
        bool effectsOnly = true;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i].SameRender(_layerStates[i])) continue;
            changed.Add(before[i].Layer);
            if (before[i].Layer.Parent is { } parent) changed.Add(parent);
            effectsOnly &= before[i].SameExceptEffects(_layerStates[i]);
        }
        // 다른 레이어·9-slice 테두리를 읽는 스크립트 효과가 있으면 이름·순서·이웃의 효과·테두리만 바뀌어도 다른 레이어 결과가 바뀔 수 있다 — 결과를 모두 무효로 한다
        // (효과 결과 캐시는 그 내용을 열쇠에 담으므로 그대로 써도 된다 — ScriptLayerContext.Key)
        bool layered = p.UsesOtherLayers;
        if (changed.Count == 0)
        {
            if (layered) _renders.InvalidateAll();
            return;
        }
        if (!effectsOnly) _renders.Effects.Clear();
        // 효과만 바뀐 레이어 하나(효과 창에서 값을 바꾸는 중): 지금 프레임 합성에서 효과 1~k 중간 결과를 담아 다음 변경 때 이어 그린다.
        _renders.CheckpointLayer = effectsOnly ? changed.FirstOrDefault(l => before.Any(s => ReferenceEquals(s.Layer, l) && !ReferenceEquals(s.Effects, l.Effects))) : null;
        // 부위 화면의 효과 그림(RenderKind.Part)은 그 그룹 효과도 입히므로 그룹이 바뀌어도 무효
        _renders.Invalidate(k => layered || k.Kind is RenderKind.Composite or RenderKind.Context || k.Layer is { } l && changed.Contains(l)
                                 || k.Kind == RenderKind.Part && k.Layer?.Parent is { } g && changed.Contains(g));
    }

    /// <summary>
    /// 스크립트 실행 허락·멈춤 해제: 효과 값은 같아도 결과가 달라지므로 효과 결과 캐시를 비우고 모든 결과를 무효로 한다(그림은 다시 계산될 때까지 보임).
    /// </summary>
    private void RerenderScripts()
    {
        if (Project is null) return;
        _renders.Effects.Clear();
        _pivotShifts.Clear();
        _renders.CheckpointLayer = null;
        _renders.InvalidateAll();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 현재 셀이 바뀜: 현재 프레임의 합성, 현재 레이어가 들어간 "부위 하나 뺀 합성", 현재 셀과 그 그룹의 셀·썸네일을 무효로 한다.
    /// 셀 픽셀을 그 자리에서 고치므로 그 셀을 담은 효과 결과 캐시도 비운다. 잔상이 있으면 다른 프레임 합성도 이 셀을 보므로 무효로 한다 —
    /// 다만 그리는 중(<paramref name="stroking"/>)에는 다른 프레임은 두고 지금 프레임만 다시 그린다(포인터가 움직일 때마다 모든 프레임을 다시 계산해
    /// 코어를 다 쓰지 않게). 그리기를 마치면(<see cref="SpriteProject.CommitCelStroke"/>) 모두 무효가 된다.
    /// </summary>
    private void InvalidateCurrent(bool stroking = false)
    {
        if (Project is not { } p || _frame < 0 || _frame >= p.Frames.Count || _layer < 0 || _layer >= p.Layers.Count) return;
        var frame = p.Frames[_frame];
        var layer = p.Layers[_layer];
        var group = layer.Parent;
        bool others = !stroking && (LayerEffects.UsesOtherFrames(layer.Effects) || group is not null && LayerEffects.UsesOtherFrames(group.Effects));
        // 다른 레이어를 읽는 스크립트가 있으면 이 프레임의 다른 레이어 결과(셀·썸네일·이 레이어를 뺀 합성)도 이 셀을 본다
        bool layered = p.UsesOtherLayers;
        _renders.Effects.Clear();
        _pivotShifts.Clear();
        _renders.CheckpointLayer = null;
        _renders.Invalidate(k => k.Kind switch
        {
            RenderKind.Composite => others || ReferenceEquals(k.Frame, frame),
            RenderKind.Context => ReferenceEquals(k.Frame, frame) && (layered || !ReferenceEquals(k.Layer, layer)),
            _ => ReferenceEquals(k.Frame, frame) && (layered || ReferenceEquals(k.Layer, layer) || group is not null && ReferenceEquals(k.Layer, group))
                 || others && (ReferenceEquals(k.Layer, layer) || group is not null && ReferenceEquals(k.Layer, group)),
        });
    }

    /// <summary>
    /// 프레임 전체 합성(보이는 레이어·그룹)의 그릴 것. 무거우면 작업 스레드에서 계산하는 동안 이전 결과(<paramref name="live"/>면 중간 그림)를 준다.
    /// 비트맵은 호출자가 Dispose하지 않는다.
    /// </summary>
    public RenderView FrameView(int frame, RenderPriority priority = RenderPriority.Background, bool live = false)
    {
        if (Project is not { } p || frame < 0 || frame >= p.Frames.Count) return RenderView.None;
        return _renders.Get(new RenderKey(RenderKind.Composite, p.Frames[frame]), priority, () => p.PlanFrame(frame), live);
    }

    /// <summary>프레임 전체 합성 비트맵(계산 중이면 이전 결과). 호출자는 Dispose하지 않는다.</summary>
    public WriteableBitmap? FrameBitmap(int frame, RenderPriority priority = RenderPriority.Background) => FrameView(frame, priority).Bitmap;

    /// <summary>프레임 <paramref name="frame"/>의 최신 합성이 준비되었는지(재생이 기다릴지 정할 때).</summary>
    public bool IsFrameReady(int frame) => Project is { } p && frame >= 0 && frame < p.Frames.Count
        && _renders.IsReady(new RenderKey(RenderKind.Composite, p.Frames[frame]));

    /// <summary>
    /// 프레임 전체 합성 그림(미리보기가 그리는 그대로, 최신 — 없으면 지금 계산). 캐시라서 호출자는 고치지 않는다.
    /// </summary>
    public PixelImage? FrameImage(int frame)
    {
        if (Project is not { } p || frame < 0 || frame >= p.Frames.Count) return null;
        return _renders.GetImage(new RenderKey(RenderKind.Composite, p.Frames[frame]), () => p.PlanFrame(frame));
    }

    /// <summary>
    /// 캔버스에 레이어 효과를 보이는지(기본 켬). 끄면 캔버스·이전 프레임 보기·양파 껍질·부위 화면 주변·스포이드와 채우기의 "보이는 그림" 기준이
    /// 효과 없는 고칠 그림(<see cref="SpriteProject.PlanFrameRaw"/>)이 된다. 미리보기·타임라인·내보내기는 그대로. 바꾸면 <see cref="ViewChanged"/>.
    /// </summary>
    public bool CanvasEffects
    {
        get => _canvasEffects;
        set
        {
            if (_canvasEffects == value) return;
            _canvasEffects = value;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private bool _canvasEffects = true;

    /// <summary>캔버스에 보일 프레임 합성: 효과 보기면 <see cref="FrameView"/>, 끄면 효과 없는 합성.</summary>
    private RenderView CanvasFrameView(int frame, RenderPriority priority, bool live = false)
    {
        if (_canvasEffects) return FrameView(frame, priority, live);
        if (Project is not { } p || frame < 0 || frame >= p.Frames.Count) return RenderView.None;
        return _renders.Get(new RenderKey(RenderKind.Composite, p.Frames[frame], Raw: true), priority, () => p.PlanFrameRaw(frame), live);
    }

    /// <summary>
    /// 캔버스에 보이는 그림: 부위를 골랐으면 그 부위(효과 보기면 <see cref="PartFrameView"/> — 프레임 크기, 끄면 부위 셀 그대로 — 부위 크기, 빈 셀이면 null),
    /// 아니면 프레임 전체 합성(<see cref="CanvasEffects"/>를 따름). 이전 프레임 보기·양파 껍질에 쓴다. 호출자는 Dispose하지 않는다.
    /// </summary>
    public WriteableBitmap? ViewBitmap(int frame, RenderPriority priority = RenderPriority.Soon)
        => CurrentPart is null ? CanvasFrameView(frame, priority).Bitmap
            : PartEffectsShown ? PartFrameView(frame, priority).Bitmap
            : CelView(_layer, frame, priority).Bitmap;

    /// <summary>
    /// 부위 화면에서 효과까지 보이는지(<see cref="CanvasEffects"/> 켬): 그러면 <see cref="DisplayView"/>·<see cref="ViewBitmap"/>은 부위 셀 대신 프레임 크기의
    /// <see cref="PartFrameView"/>이고, 캔버스는 그 그림을 부위 위치만큼 옮겨(부위 화면의 흐린 주변과 같은 자리) 그린다.
    /// </summary>
    public bool PartEffectsShown => _canvasEffects && CurrentPart is not null;

    /// <summary>
    /// 부위 화면의 효과 그림: 지금 부위 하나만 프레임에 보이는 모습 그대로(부위 효과·그룹 효과까지, 옮겨진 자리에, 부위 칸 밖으로 나가도) 그린 프레임 크기 그림
    /// (<see cref="SpriteProject.PlanPartInFrame"/> — 불투명도·숨김은 보지 않음). 부위가 아니면 없음. 호출자는 Dispose하지 않는다.
    /// </summary>
    public RenderView PartFrameView(int frame, RenderPriority priority = RenderPriority.Soon, bool live = false)
    {
        if (Project is not { } p || CurrentPart is not { } part || frame < 0 || frame >= p.Frames.Count) return RenderView.None;
        return _renders.Get(new RenderKey(RenderKind.Part, p.Frames[frame], part), priority, () => p.PlanPartInFrame(part, frame), live);
    }

    /// <summary>
    /// 현재 캔버스 그림: 부위면 그 부위(효과 보기면 <see cref="PartFrameView"/>, 끄면 현재 셀), 아니면 현재 프레임 합성(계산 중이면 중간 그림·진행·덮개 여부).
    /// 부위 셀이 비었으면 투명 비트맵.
    /// </summary>
    public RenderView DisplayView
    {
        get
        {
            if (Project is null) return RenderView.None;
            if (CurrentPart is null) return CanvasFrameView(_frame, RenderPriority.Current, live: true);
            if (PartEffectsShown) return PartFrameView(_frame, RenderPriority.Current, live: true);
            var view = CelView(_layer, _frame, RenderPriority.Current);
            if (view.Bitmap is not null) return view;
            int w = Math.Max(1, CanvasWidth), h = Math.Max(1, CanvasHeight);
            if (_blankBitmap is null || _blankBitmap.PixelSize.Width != w || _blankBitmap.PixelSize.Height != h)
            {
                _blankBitmap?.Dispose();
                _blankBitmap = ImageUtil.NewBitmap(w, h);
            }
            return view with { Bitmap = _blankBitmap };
        }
    }

    /// <summary>레이어·프레임별 효과의 피벗 이동(<see cref="EffectPivotShift"/>) — 그림 결과를 무효로 할 때 같이 비운다.</summary>
    private readonly Dictionary<(Layer Layer, Frame Frame), System.Drawing.PointF> _pivotShifts = new();

    /// <summary>
    /// 캔버스 피벗 표시를 옮길 거리(px): 효과 보기(<see cref="CanvasEffects"/>)가 켜져 있으면 레이어 <paramref name="layer"/>의 효과가 프레임 <paramref name="frame"/>에서
    /// 그림을 옮겨 쌓은 피벗 이동(<see cref="SpriteProject.EffectPivotShift"/> — 부위면 그룹 효과의 이동까지, 링크된 칸에 이어 가는 끝 모습 포함), 끄면 0.
    /// 효과를 그려 재므로 (레이어, 프레임)마다 기억하고 그림 결과를 무효로 할 때 비운다.
    /// </summary>
    public System.Drawing.PointF EffectPivotShift(Layer layer, int frame)
    {
        if (!_canvasEffects || Project is not { } p || frame < 0 || frame >= p.Frames.Count) return System.Drawing.PointF.Empty;
        var key = (layer, p.Frames[frame]);
        if (_pivotShifts.TryGetValue(key, out var shift)) return shift;
        shift = p.EffectPivotShift(layer, frame);
        _pivotShifts[key] = shift;
        return shift;
    }

    /// <summary>
    /// 프레임 합성에서 부위 하나(<paramref name="exclude"/>)를 뺀 비트맵(프레임 크기). 부위 화면의 흐린 주변,
    /// 그룹 화면에서 부위를 끄는 동안의 배경에 쓴다. 호출자는 Dispose하지 않는다.
    /// </summary>
    public WriteableBitmap? ContextBitmap(int frame, Layer exclude)
    {
        if (Project is not { } p || frame < 0 || frame >= p.Frames.Count) return null;
        bool raw = !_canvasEffects;
        return _renders.Get(new RenderKey(RenderKind.Context, p.Frames[frame], exclude, raw), RenderPriority.Current,
            () => raw ? p.PlanFrameRaw(frame, l => !ReferenceEquals(l, exclude)) : p.PlanFrame(frame, layerFilter: l => !ReferenceEquals(l, exclude))).Bitmap;
    }

    /// <summary>
    /// 현재 캔버스에서 보이는 색. 부위 화면이면 부위 셀의 색(투명이면 그 자리의 프레임 합성 색), 아니면 프레임 합성 색.
    /// <paramref name="exact"/>가 false면(상태 표시줄) 계산 중일 때 이전 결과의 색을 쓰고 기다리지 않는다.
    /// </summary>
    public Color32 CompositeColorAt(int x, int y, bool exact = true)
    {
        if (CurrentPart is { } part)
        {
            var c = EditImage is { } cel && cel.Contains(x, y) ? cel.Get(x, y) : Color32.Transparent;
            if (c.A != 0) return c;
            var at = Project?.GetPartOffset(part, _frame) ?? part.PartOffset;
            x += at.X;
            y += at.Y;
        }
        bool raw = !_canvasEffects;
        var image = Project is not { } p || _frame >= p.Frames.Count ? null
            : !exact ? _renders.ShownImage(new RenderKey(RenderKind.Composite, p.Frames[_frame], Raw: raw))
            : raw ? _renders.GetImage(new RenderKey(RenderKind.Composite, p.Frames[_frame], Raw: true), () => p.PlanFrameRaw(_frame))
            : FrameImage(_frame);
        return image is not null && image.Contains(x, y) ? image.Get(x, y) : Color32.Transparent;
    }

    /// <summary>
    /// 현재 캔버스에서 보이는 그림 전체(캔버스 크기, 새 이미지): 각 픽셀이 <see cref="CompositeColorAt"/>와 같다.
    /// 채우기의 "보이는 레이어 전체" 기준에 쓴다.
    /// </summary>
    public PixelImage VisibleImage()
    {
        int w = Math.Max(1, CanvasWidth), h = Math.Max(1, CanvasHeight);
        var img = new PixelImage(w, h);
        if (FrameImage(_frame) is not { } frame) return img;
        if (CurrentPart is not { } part)
        {
            img.CopyFrom(frame);
            return img;
        }
        var cel = EditImage;
        var at = Project!.GetPartOffset(part, _frame);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = cel is not null && cel.Contains(x, y) ? cel.Get(x, y) : Color32.Transparent;
                int fx = x + at.X, fy = y + at.Y;
                if (c.A == 0 && frame.Contains(fx, fy)) c = frame.Get(fx, fy);
                img.Set(x, y, c);
            }
        return img;
    }

    /// <summary>
    /// 셀 하나(레이어 × 프레임)의 그릴 것(원본 크기): 그룹이면 그 그룹의 합성(프레임 크기, 그룹 효과 포함), 일반 레이어면 효과를 입힌 셀(셀 크기로 잘림),
    /// 부위면 셀 그대로(부위 화면에서 고치는 그림이라 효과 없이). 빈 셀(그룹이면 모든 부위가 빈 셀)이면 비트맵이 null.
    /// </summary>
    public RenderView CelView(int layer, int frame, RenderPriority priority = RenderPriority.Background)
    {
        if (Project is not { } p || frame < 0 || frame >= p.Frames.Count || layer < 0 || layer >= p.Layers.Count) return RenderView.None;
        bool raw = !_canvasEffects;
        return _renders.Get(new RenderKey(RenderKind.Cel, p.Frames[frame], p.Layers[layer], raw), priority,
            () => raw ? p.PlanCelThumbnail(layer, frame) : p.PlanCel(layer, frame));
    }

    /// <summary><see cref="CelView"/>의 비트맵. 호출자는 Dispose하지 않는다.</summary>
    public WriteableBitmap? CelBitmap(int layer, int frame) => CelView(layer, frame).Bitmap;

    /// <summary>
    /// 타임라인 칸 썸네일: 고칠 그림(<see cref="SpriteProject.PlanCelThumbnail"/> — 일반 레이어는 효과 없이)을 그 레이어 자리(<see cref="SpriteProject.ThumbnailBounds"/> — 모든 프레임 같음)로 잘라
    /// 긴 변 <see cref="RenderService.ThumbMaxSize"/>px 이하로 줄인 것(<see cref="RenderView.SourceWidth"/>·<see cref="RenderView.SourceHeight"/>는 자른 크기).
    /// 레이어마다 칸에 크게 보이도록 캔버스 전체가 아니라 그 레이어가 칠한 자리만 보인다.
    /// </summary>
    public RenderView CelThumbnail(int layer, int frame)
    {
        if (Project is not { } p || frame < 0 || frame >= p.Frames.Count || layer < 0 || layer >= p.Layers.Count) return RenderView.None;
        return _renders.Get(new RenderKey(RenderKind.Thumb, p.Frames[frame], p.Layers[layer]), RenderPriority.Background, () =>
        {
            if (p.PlanCelThumbnail(layer, frame) is not { } plan) return null;
            var area = ThumbnailBounds(p, layer);
            return area.IsEmpty ? plan : plan.Cropped(area);
        });
    }

    /// <summary>
    /// 레이어마다의 썸네일 자리(모든 프레임 셀을 훑어 셈하므로 기억해 둔다). 프로젝트가 바뀌면(<see cref="InvalidateRenders"/>) 비운다 —
    /// 그리는 중에는 그대로라 칸 썸네일은 그리기를 마친 뒤 새 자리로 맞춰진다.
    /// </summary>
    private readonly Dictionary<Layer, Rectangle> _thumbBounds = new(ReferenceEqualityComparer.Instance);

    private Rectangle ThumbnailBounds(SpriteProject p, int layer)
    {
        var key = p.Layers[layer];
        if (!_thumbBounds.TryGetValue(key, out var area)) _thumbBounds[key] = area = p.ThumbnailBounds(layer);
        return area;
    }
}
