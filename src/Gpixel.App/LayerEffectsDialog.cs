using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 레이어 효과(비파괴): 효과 목록(추가·삭제·순서·켜기·영구 적용) + 고른 효과 설정(프레임 범위 포함) + 전/후 미리보기(움직이는 효과는 그 범위를 돌려 봄).
/// 고치는 동안 캔버스에도 바로 보이고(<see cref="SpriteProject.PreviewLayerEffects"/>), 확인이면 대화상자 한 번이 실행취소 한 단계,
/// 취소·창 닫기면 원래대로 돌아간다(<see cref="SpriteProject.EndLayerEffectsPreview"/>).
/// </summary>
public sealed partial class LayerEffectsDialog : DialogBase
{
    private const int PreviewBox = 220;
    /// <summary>오른쪽(미리보기) 칸 폭 — 고정(넉넉히): 설명 글이 바뀌어도 창 폭이 그대로.</summary>
    private const int RightWidth = PreviewBox * 2 + 72;
    /// <summary>가운데(효과 목록·설정) 칸 폭.</summary>
    private const int SettingsWidth = 350;

    /// <summary>
    /// 추가할 수 있는 효과 종류(이름, 한 줄 설명, 새 효과 만들기) — 번호 = <see cref="Create"/>의 kind. 고정 효과 먼저, 움직이는 효과는 뒤에 모아 둔다.
    /// 설명은 효과 종류 상자의 도움말에 보인다.
    /// </summary>
    private static readonly (string Name, string Description, Func<LayerEffect> New)[] KindTable =
    [
        ("외곽선", "그림 둘레에 테두리 선을 두릅니다(바깥 = 그림 밖에, 안쪽 = 가장자리 픽셀에).", () => new OutlineEffect()),
        ("그림자", "그림 모양 그대로의 그림자를 비스듬히 옮겨 뒤에 깝니다.", () => new ShadowEffect()),
        ("가장자리 빛·그늘", "빛이 오는 쪽 가장자리를 밝게, 반대쪽을 어둡게 덧칠해 입체감을 줍니다.", () => new RimLightEffect()),
        ("색 덮기", "보이는 픽셀을 한 색으로 물들입니다(100% = 실루엣).", () => new ColorOverlayEffect()),
        ("그라데이션 덮기", "보이는 픽셀에 두 색이 차츰 바뀌는 그라데이션을 섞습니다(디더 무늬도 됨).", () => new GradientOverlayEffect()),
        ("안쪽 빛", "가장자리에서 안쪽으로 색을 번지듯 섞어 빛나는 테두리를 만듭니다.", () => new InnerGlowEffect()),
        ("색 바꾸기", "고른 색을 다른 색으로 바꿉니다(팔레트 스왑 — 같은 그림의 색만 다른 버전).", () => new PaletteSwapEffect()),
        ("색조 보정", "흑백·세피아·색조 이동·밝기·대비·채도로 색을 바꿉니다(그림은 그대로).", () => new ColorAdjustEffect()),
        ("반사", "그림을 위아래로 뒤집어 아래에 점점 옅게 비춥니다(물·바닥 반사).", () => new ReflectionEffect()),
        ("터지기", "픽셀이 조각나 바깥으로 튀어 날아가며 사라집니다.", () => new ExplodeEffect()),
        ("빛 지나가기", "빛 띠가 그림을 한쪽에서 반대쪽으로 훑고 지나갑니다(반짝임).", () => new ShineEffect()),
        ("늘었다 줄기", "젤리처럼 가로·세로로 출렁이다 원래 모양으로 돌아옵니다.", () => new SquashEffect()),
        ("피격 번쩍임", "맞았을 때처럼 그림이 한 색으로 번쩍입니다.", () => new HitFlashEffect()),
        ("깜빡이기", "보였다 숨었다를 되풀이합니다(무적 시간 표시 등).", () => new BlinkEffect()),
        ("흔들기", "그림 전체가 픽셀 단위로 덜덜 떱니다.", () => new ShakeEffect()),
        ("둥실둥실", "위아래로 부드럽게 떠오르고 가라앉습니다(대기 동작).", () => new BobEffect()),
        ("나타나기 / 사라지기", "디더·흩어짐·쓸기·투명도로 그림이 나타나거나 사라집니다.", () => new AppearEffect()),
        ("녹아내리기", "픽셀이 열마다 다른 빠르기로 아래로 흘러내립니다.", () => new MeltEffect()),
        ("회전", "그림을 돌립니다(RotSprite — 새 색·반투명 없이).", () => new RotateEffect()),
        ("잔상", "앞 프레임 그림을 뒤에 점점 옅게 남겨 빠르게 움직이는 느낌을 줍니다.", () => new AfterimageEffect()),
        ("색 순환", "정한 색들을 차례로 돌려 바꿉니다(물·불·네온처럼 흐르는 색).", () => new ColorCycleEffect()),
        ("충격파 고리", "중심에서 픽셀 원 고리가 퍼져 나갑니다.", () => new ShockwaveEffect()),
        ("홀로그램", "한 색의 투사 영상처럼 물들이고 주사선·밝은 띠·깜빡임·글리치를 더합니다(SF 홀로그램).", () => new HologramEffect()),
    ];

    /// <summary>효과 종류의 한 줄 설명(번호 = <see cref="Create"/>의 kind).</summary>
    private static string DescriptionOf(int kind) => KindTable[kind].Description;

    /// <summary>효과 종류 이름(번호 = <see cref="Create"/>의 kind).</summary>
    internal static readonly string[] Kinds = KindTable.Select(k => k.Name).ToArray();

    /// <summary>종류 번호의 새 효과(기본 설정, 범위 밖이면 외곽선).</summary>
    internal static LayerEffect Create(int kind) => (kind >= 0 && kind < KindTable.Length ? KindTable[kind] : KindTable[0]).New();

    /// <summary>종류가 움직이는 효과(프레임 범위에 따라 모습이 바뀜)인지.</summary>
    internal static bool IsAnimated(int kind) => Create(kind) is AnimatedEffect;

    private static readonly (LightDirection Dir, string Name)[] Directions =
    [
        (LightDirection.TopLeft, "왼쪽 위"), (LightDirection.Top, "위"), (LightDirection.TopRight, "오른쪽 위"),
        (LightDirection.Left, "왼쪽"), (LightDirection.Right, "오른쪽"),
        (LightDirection.BottomLeft, "왼쪽 아래"), (LightDirection.Bottom, "아래"), (LightDirection.BottomRight, "오른쪽 아래"),
    ];

    private static readonly string[] Easings = ["처음 빠르게", "일정하게", "천천히 시작", "천천히 시작·끝"];
    private static readonly string[] Repeats = ["한 번", "반복", "왕복"];
    private static readonly string[] Centers = ["가운데", "피벗", "아래 가운데", "직접 (X, Y)"];
    private static readonly string[] Anchors = ["아래 가운데 (발 고정)", "가운데", "피벗"];
    private static readonly string[] ShadowDithers = ["없음 (반투명)", "25%", "50% (바둑판)", "75%"];
    private static readonly string[] Blends = ["덮기", "곱하기", "스크린"];
    private static readonly string[] Dithers = ["없음 (부드럽게)", "2×2 Bayer", "4×4 Bayer"];

    /// <summary>숫자 칸 종류: 우클릭 예시 값(칸 범위 밖 값은 빠짐), 단위(메뉴 글자), 최근 값 묶음 이름. <paramref name="Format"/>가 있으면 단위 대신 그 글자.</summary>
    private sealed record NumKind(string Key, string Unit, decimal[] Values, Func<decimal, string>? Format = null);

    /// <summary>효과 설정의 숫자 칸 종류(같은 종류끼리 최근 값을 나눔).</summary>
    private static class NumKinds
    {
        /// <summary>지나가는·바뀌는 방향(0~359°).</summary>
        public static readonly NumKind Angle = new("effect.angle", "°", [0, 15, 30, 45, 60, 90, 120, 135, 180, 225, 270, 315]);
        /// <summary>범위 동안 도는 전체 각도(360 = 한 바퀴).</summary>
        public static readonly NumKind Rotation = new("effect.rotation", "°", [15, 30, 45, 90, 180, 270, 360, 540, 720, 1080, 1440]);
        /// <summary>색상 돌리기(−180~180°).</summary>
        public static readonly NumKind Hue = new("effect.hue", "°", [-180, -120, -90, -60, -30, 0, 30, 60, 90, 120, 180]);
        /// <summary>세기·양(0~100%).</summary>
        public static readonly NumKind Strength = new("effect.strength", "%", [0, 10, 25, 33, 50, 66, 75, 90, 100]);
        /// <summary>불투명도(0~100%).</summary>
        public static readonly NumKind Opacity = new("effect.opacity", "%", [0, 10, 20, 25, 33, 40, 50, 60, 75, 90, 100]);
        /// <summary>밝기·대비·채도(−100~100%).</summary>
        public static readonly NumKind Adjust = new("effect.adjust", "%", [-100, -75, -50, -25, -10, 0, 10, 25, 50, 75, 100]);
        /// <summary>빈도(프레임 중 몇 %).</summary>
        public static readonly NumKind Rate = new("effect.rate", "%", [0, 5, 10, 15, 20, 25, 33, 50, 75, 100]);
        /// <summary>흩어짐·불규칙함(0~100%).</summary>
        public static readonly NumKind Random = new("effect.random", "%", [0, 10, 20, 25, 33, 50, 66, 75, 90, 100]);
        /// <summary>진행 중 시작 시점(0~100%).</summary>
        public static readonly NumKind Progress = new("effect.progress", "%", [0, 10, 20, 25, 33, 40, 50, 60, 66, 75, 90]);
        /// <summary>그림 크기의 몇 %(반사 높이).</summary>
        public static readonly NumKind Portion = new("effect.portion", "%", [10, 20, 25, 33, 40, 50, 60, 66, 75, 100]);
        /// <summary>늘었다 줄기 세기(1~50%).</summary>
        public static readonly NumKind Squash = new("effect.squash", "%", [5, 10, 15, 20, 25, 30, 40, 50]);
        /// <summary>작은 픽셀 거리(폭·높이·흔들림·간격·밀림).</summary>
        public static readonly NumKind Px = new("effect.px", "px", [0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32]);
        /// <summary>선·띠·고리 두께.</summary>
        public static readonly NumKind Thickness = new("effect.thickness", "px", [1, 2, 3, 4, 5, 6, 8, 12, 16]);
        /// <summary>그림자 X·Y 옮김(음수 = 왼쪽·위).</summary>
        public static readonly NumKind Offset = new("effect.offset", "px", [-8, -4, -3, -2, -1, 0, 1, 2, 3, 4, 8]);
        /// <summary>날아가는·흘러내리는 거리, 고리 반지름.</summary>
        public static readonly NumKind Distance = new("effect.distance", "px", [4, 8, 12, 16, 24, 32, 48, 64, 96, 128]);
        /// <summary>중력(음수 = 위로).</summary>
        public static readonly NumKind Gravity = new("effect.gravity", "px", [-16, -8, -4, -2, 0, 2, 4, 6, 8, 16, 32]);
        /// <summary>터지기 조각 크기(1~4px).</summary>
        public static readonly NumKind Piece = new("effect.piece", "px", [1, 2, 3, 4]);
        /// <summary>범위 안 횟수(튕김·번쩍임·깜빡임·흔들림·오르내림).</summary>
        public static readonly NumKind Count = new("effect.count", "번", [1, 2, 3, 4, 5, 6, 8, 10, 12, 16]);
        /// <summary>움직임 반복 횟수.</summary>
        public static readonly NumKind Repeat = new("effect.repeat", "번", [1, 2, 3, 4, 5, 6, 8]);
        /// <summary>프레임 수(보임·숨김·번쩍임·잔상 간격).</summary>
        public static readonly NumKind Frames = new("effect.frames", "프레임", [0, 1, 2, 3, 4, 5, 6, 8, 10, 12, 16]);
        /// <summary>잔상 개수.</summary>
        public static readonly NumKind Copies = new("effect.copies", "개", [1, 2, 3, 4, 5, 6, 8]);
        /// <summary>주사선 간격(줄).</summary>
        public static readonly NumKind ScanGap = new("effect.scanGap", "줄", [2, 3, 4, 5, 6]);
        /// <summary>무작위 시드.</summary>
        public static readonly NumKind Seed = new("effect.seed", "", [1, 2, 3, 7, 42, 1234]);
        /// <summary>색 허용 차이(R·G·B 0~255).</summary>
        public static readonly NumKind Tolerance = new("effect.tolerance", "", [0, 4, 8, 16, 24, 32, 48, 64, 96, 128]);
    }

    private readonly SpriteProject _project;
    private readonly Layer _layer;
    private readonly int _frame;
    private readonly IReadOnlyList<LayerEffect> _original;
    private readonly List<LayerEffect> _effects;
    private readonly Dictionary<int, PixelImage?> _sources = new();

    private readonly ListBox _list;
    private readonly StackPanel _settings;
    private readonly Button _remove, _up, _down, _bake, _play;
    private readonly CheckBox _effectOnly, _expandCanvas;
    private readonly ComboBox _expandMode;
    private readonly TextBlock _expandNote;
    private readonly TextBlock _caption;
    private readonly PreviewView _preview = new();
    private readonly DispatcherTimer _timer = new();
    private int _previewFrame;
    /// <summary>재생 범위: true = 고른 효과 범위, false = 늘린 뒤 전체 프레임. 앱을 켜 둔 동안 유지.</summary>
    private static bool s_effectOnly = true;
    /// <summary>효과에 맞춘 캔버스 확대 방식. 앱을 켜 둔 동안 유지.</summary>
    private static EffectCanvasExpandMode s_canvasExpandMode;
    /// <summary>확인 단추에서 검증·계산한 캔버스 확대(닫힌 뒤 같은 결과를 적용).</summary>
    private EffectCanvasExpansion? _confirmedCanvasExpansion;
    /// <summary>재생 반복: 끝 프레임 다음 바로 처음으로(끝에서 멈칫하지 않음). 앱을 켜 둔 동안 창을 다시 열어도 유지.</summary>
    private static bool s_seamlessLoop;
    private bool _syncing, _baked;

    /// <summary>확인(프레임을 늘렸을 때)·영구 적용으로 닫혔으면 상태 표시줄에 보일 알림.</summary>
    public string? ResultMessage { get; private set; }

    /// <param name="focus">처음 고를 효과 번호(범위 밖이면 첫 효과).</param>
    /// <param name="newRange">"추가 범위"의 처음 값(타임라인에서 칸을 고르고 "효과 추가…"로 열었을 때 그 프레임들). null = 모든 프레임.</param>
    /// <param name="motionRange">추가 범위를 그대로 두고 움직이는 효과를 추가할 때 쓸 범위(한 칸만 골랐으면 태그 끝까지 넓힌 것). null = newRange.</param>
    public LayerEffectsDialog(EditorContext ctx, Layer layer, int focus = -1, FrameRange? newRange = null, FrameRange? motionRange = null) : base($"레이어 효과 — {layer.Name}")
    {
        _motionRange = motionRange ?? newRange;
        _project = ctx.Project ?? throw new InvalidOperationException("열린 프로젝트가 없습니다.");
        _layer = layer;
        _frame = Math.Clamp(ctx.CurrentFrame, 0, _project.Frames.Count - 1);
        _previewFrame = _frame;
        _original = layer.Effects;
        _effects = [.. layer.Effects];

        int n = _project.Frames.Count;
        _addAll = Ui.Check("모든 프레임", newRange is null);
        _addFrom = WithKind(Ui.Num(1, n, (newRange?.From ?? 0) + 1, 56), FrameNumberKind(end: false));
        _addTo = WithKind(Ui.Num(1, n, (newRange?.To ?? n - 1) + 1, 56), FrameNumberKind(end: true));
        _addFrom.IsEnabled = _addTo.IsEnabled = newRange is not null;
        ToolTip.SetTip(_addAll, "추가하는 효과가 입혀질 프레임(추가한 뒤에도 아래 설정에서 바꿀 수 있음).");
        _addAll.IsCheckedChanged += (_, _) =>
        {
            if (!_addAll.On() && (int)_addFrom.NumValue() == 1 && (int)_addTo.NumValue() == n)
                _addFrom.Value = _addTo.Value = _frame + 1; // 모든 프레임을 덮는 범위 = "모든 프레임"이라 지금 프레임 하나로 시작
            _addFrom.IsEnabled = _addTo.IsEnabled = !_addAll.On();
            _addRangeTouched = true;
            SyncStripRange();
        };
        _addFrom.ValueChanged += (_, _) => { _addRangeTouched = true; SyncStripRange(); };
        _addTo.ValueChanged += (_, _) => { _addRangeTouched = true; SyncStripRange(); };
        _remove = Ui.Button("삭제", (_, _) => Remove(), 60);
        _up = Ui.Button("위로", (_, _) => Move(-1), 60);
        _down = Ui.Button("아래로", (_, _) => Move(1), 60);
        _bake = Ui.Button("영구 적용", (_, _) => Bake(), 80);
        _list = new ListBox { Width = SettingsWidth, Height = 170, Margin = new Thickness(3) };
        ToolTip.SetTip(_list, "줄을 끌어 순서를 바꿉니다(위에서부터 차례로 적용). Delete = 고른 효과 삭제.");
        _list.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Delete or Key.Back) || Selected < 0) return;
            e.Handled = true;
            Remove();
            _list.Focus(); // 이어서 Delete를 누를 수 있게
        };
        _drag.Moved += (from, to) =>
        {
            var effect = _effects[from];
            _effects.RemoveAt(from);
            _effects.Insert(to, effect);
            RebuildList(to);
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            ShowSettings();
            UpdateButtons();
            SyncPlayback(restart: true);
            SyncStripRange();
        };
        _settings = new StackPanel { Spacing = 2, MinHeight = 120, Width = SettingsWidth };
        Grid.SetIsSharedSizeScope(_settings, true); // 설정 줄마다 이름 열 폭을 맞춘다(SettingsRow)

        _expandCanvas = Ui.Check("효과만큼 캔버스 확대", false);
        _expandMode = Ui.Combo(110, ["최소한으로", "비율 유지"]);
        _expandMode.SelectedIndex = (int)s_canvasExpandMode;
        _expandMode.IsEnabled = false;
        _expandNote = Ui.Label("");
        _expandNote.Foreground = AppTheme.DimTextBrush;
        _expandNote.Margin = new Thickness(3, 0, 0, 2);
        _expandNote.TextWrapping = TextWrapping.Wrap;
        _expandNote.MaxWidth = SettingsWidth;
        _expandNote.IsVisible = false;
        ToolTip.SetTip(_expandCanvas, "확인할 때 이 레이어의 켜진 효과가 캔버스 밖에서 잘리지 않도록 캔버스를 늘립니다. 그림 크기는 바꾸지 않고 모든 레이어·부위·피벗을 함께 옮깁니다.");
        ToolTip.SetTip(_expandMode, "최소한으로: 효과가 나간 방향만 필요한 만큼 · 비율 유지: 효과를 모두 담으면서 원래 캔버스 가로세로 비율을 유지");
        _expandCanvas.IsCheckedChanged += (_, _) =>
        {
            _expandMode.IsEnabled = _expandCanvas.On();
            _expandNote.IsVisible = _expandCanvas.On();
            SyncCanvasExpansion();
        };
        _expandMode.SelectionChanged += (_, _) =>
        {
            if (_expandMode.SelectedIndex < 0) return;
            s_canvasExpandMode = (EffectCanvasExpandMode)_expandMode.SelectedIndex;
            SyncCanvasExpansion();
        };
        OkButton.Click += (_, _) =>
        {
            _confirmedCanvasExpansion = null;
            if (!_expandCanvas.On()) return;
            try
            {
                _confirmedCanvasExpansion = CanvasExpansion(VirtualEffects());
            }
            catch (InvalidOperationException ex)
            {
                DialogResult = DialogResult.None;
                Ui.Error(this, ex.Message);
            }
        };

        string? motionTip = _motionRange != newRange && _motionRange is { } mr
            ? $"추가 범위를 그대로 두고 움직이는 효과(터지기 ~ 충격파 고리)를 추가하면 여러 프레임에 걸치도록 프레임 {mr.From + 1}~{mr.To + 1}로 넓혀 추가합니다."
            : null;
        var rangeLabel = Ui.Label("추가 범위");
        if (motionTip is not null) ToolTip.SetTip(rangeLabel, motionTip);
        var canvasExpand = Ui.Stack(Ui.Row(_expandCanvas, _expandMode), _expandNote);
        var left = Ui.Stack(
            Ui.Row(rangeLabel, _addAll, Ui.Label("프레임"), _addFrom, Ui.Label("~"), _addTo),
            _list,
            Ui.Row(_remove, _up, _down, _bake),
            canvasExpand,
            Ui.Group("설정", new ScrollViewer { Content = _settings, MaxHeight = 390, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }));
        left.Width = SettingsWidth + 30;

        _preview.Width = PreviewBox * 2 + 12;
        _preview.Height = PreviewBox + 20;
        _caption = Ui.Label("");
        _caption.Foreground = AppTheme.DimTextBrush;
        _play = Ui.Button(VectorIcon.Play, "재생", (_, _) => TogglePlay(), 80);
        ToolTip.SetTip(_play, "고른 효과의 범위 또는 늘린 뒤 전체 프레임을 돌려 봅니다.");
        _effectOnly = Ui.Check("고른 효과 범위만", s_effectOnly);
        _effectOnly.VerticalAlignment = VerticalAlignment.Center;
        _effectOnly.IsCheckedChanged += (_, _) =>
        {
            s_effectOnly = _effectOnly.On();
            SyncPlayback(restart: true);
        };
        ToolTip.SetTip(_effectOnly, "켜면 고른 효과가 입혀지는 프레임만, 끄면 늘리기로 더해질 프레임까지 전체를 재생합니다.");
        var loop = Ui.Check("끝에서 바로 처음으로", s_seamlessLoop);
        loop.VerticalAlignment = VerticalAlignment.Center;
        loop.IsCheckedChanged += (_, _) => s_seamlessLoop = loop.On();
        ToolTip.SetTip(loop, "재생할 때 마지막 프레임을 더 보여 주지 않고 곧바로 첫 프레임으로 이어서 반복합니다(게임에서 반복 재생하는 모습).");
        _timer.Tick += (_, _) => NextPreviewFrame();
        var right = Ui.Stack(
            Ui.Row(_play, _effectOnly, loop, Ui.InfoBadge(Intro(layer))),
            _caption,
            _preview,
            BuildFrameStrip());
        right.Width = RightWidth; // 고정 — 진행 % 등 글 길이에 따라 창 폭이 흔들리지 않게

        var root = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        root.Children.Add(BuildPalette(motionTip));
        root.Children.Add(left);
        root.Children.Add(right);
        Body.Children.Add(root);

        RebuildList(_effects.Count == 0 ? -1 : focus >= 0 && focus < _effects.Count ? focus : 0);
        Closed += (_, _) =>
        {
            _timer.Stop();
            _stripTimer.Stop();
            DisposePalette();
            _strip.Dispose();
            int index = _project.IndexOf(_layer);
            if (index >= 0 && !_baked) EndPreview(index, DialogResult == DialogResult.OK);
            _preview.Dispose();
        };
    }

    private static string Intro(Layer layer)
        => "효과는 그림을 고치지 않고 보일 때만 입혀집니다(나중에 그린 것에도). 위에서부터 차례로 적용됩니다.\n"
           + "움직이는 효과는 프레임 범위의 처음에서 시작해 끝에서 끝납니다(범위가 길수록 느림).\n"
           + "미리보기는 고른 프레임의 범위 안에 있는 효과만 보입니다. 효과를 고르면 그 범위를 바로 재생하고, '고른 효과 범위만'을 끄면 늘린 뒤 전체 프레임을 재생합니다. 아래 프레임 줄: 누르면 그 프레임, 끌면 고른 효과의 프레임 범위."
           + (layer.IsGroup ? "\n그룹 효과는 부위들을 합친 모양에 입혀지며 프레임 시트에만 들어갑니다." : "")
           + (layer.IsPart ? "\n부위 아틀라스에서는 번지는 만큼 스프라이트가 커지고 피벗은 같은 자리를 가리킵니다." : "");

    // ───────────── 목록 ─────────────

    private readonly CheckBox _addAll;
    private readonly NumericUpDown _addFrom, _addTo;
    /// <summary>추가 범위를 손대지 않았을 때 움직이는 효과에 쓸 범위.</summary>
    private readonly FrameRange? _motionRange;
    private bool _addRangeTouched;

    private int Selected => _list.SelectedIndex;

    private void RebuildList(int select)
    {
        _syncing = true;
        _drag.Clear();
        _list.Items.Clear();
        for (int i = 0; i < _effects.Count; i++)
        {
            int at = i;
            var check = new CheckBox { IsChecked = _effects[i].Enabled, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            check.IsCheckedChanged += (_, _) =>
            {
                if (_syncing || at >= _effects.Count) return;
                Update(at, _effects[at] with { Enabled = check.IsChecked == true });
            };
            var text = new TextBlock { Text = EffectSummary(_effects[i]), VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
            row.Children.Add(check);
            row.Children.Add(text);
            var item = new ListBoxItem { Content = row };
            _list.Items.Add(item);
            _drag.Attach(item);
        }
        _list.SelectedIndex = Math.Clamp(select, -1, _effects.Count - 1);
        _syncing = false;
        ShowSettings();
        Changed();
        SyncPlayback(restart: Selected >= 0);
    }

    private void UpdateButtons()
    {
        _remove.IsEnabled = Selected >= 0;
        _up.IsEnabled = Selected > 0;
        _down.IsEnabled = Selected >= 0 && Selected < _effects.Count - 1;
        var (canBake, why) = CanBake(_project, _layer);
        _bake.IsEnabled = canBake && Selected >= 0;
        int extra = Selected >= 0 ? LayerEffects.BakeSet(_effects, [Selected]).Count - 1 : 0;
        ToolTip.SetTip(_bake, why ?? ("고른 효과를 셀 픽셀에 구워 넣고 효과에서 뺍니다(실행취소 한 단계, 창이 닫힘)."
                                      + (extra > 0 ? $" 위의 켜진 효과 {extra}개도 함께 구워집니다(보이던 그대로가 되도록)." : "")));
    }

    /// <summary>종류 <paramref name="kind"/>의 새 효과를 추가 범위로 목록 <paramref name="at"/>번째에 넣고 고른다.</summary>
    private void Add(int kind, int at)
    {
        var effect = Create(kind);
        int n = _project.Frames.Count;
        var range = !_addRangeTouched && IsAnimated(kind) ? _motionRange
            : _addAll.On() ? null : FrameRange.Of((int)_addFrom.NumValue() - 1, (int)_addTo.NumValue() - 1, n);
        if (range is not null) effect = effect with { Frames = range };
        at = Math.Clamp(at, 0, _effects.Count);
        _effects.Insert(at, effect);
        RebuildList(at);
    }

    private void Remove()
    {
        if (Selected is var i && i < 0) return;
        var removed = _effects[i];
        _virtualRangeOverrides.Remove(removed);
        if (ReferenceEquals(removed, _extensionOwner)) SetExtension(null, null);
        if (ReferenceEquals(removed, _preferExistingFor))
        {
            _preferExistingFor = null;
            _preferExisting = null;
        }
        _effects.RemoveAt(i);
        RebuildList(Math.Min(i, _effects.Count - 1));
    }

    private void Move(int delta)
    {
        int i = Selected, j = i + delta;
        if (i < 0 || j < 0 || j >= _effects.Count) return;
        (_effects[i], _effects[j]) = (_effects[j], _effects[i]);
        RebuildList(j);
    }

    private readonly DragReorder _drag = new();

    /// <summary>효과 하나를 바꾸고(목록 글자 갱신) 캔버스·미리보기에 반영한다. 늘리기 소유 효과도 새 불변 사본을 따라간다.</summary>
    private void Update(int index, LayerEffect effect)
    {
        if (index < 0 || index >= _effects.Count || _effects[index] == effect) return;
        var old = _effects[index];
        _effects[index] = effect;
        if (_virtualRangeOverrides.Remove(old, out var virtualRange))
            _virtualRangeOverrides[effect] = virtualRange;
        if (ReferenceEquals(old, _extensionOwner)) _extensionOwner = effect;
        if (ReferenceEquals(old, _preferExistingFor)) _preferExistingFor = effect;
        if (_list.Items[index] is ListBoxItem { Content: StackPanel { Children: [CheckBox check, TextBlock text] } })
        {
            _syncing = true;
            check.IsChecked = effect.Enabled;
            text.Text = EffectSummary(effect);
            _syncing = false;
        }
        Changed();
        SyncPlayback();
    }

    /// <summary>목록이 바뀜: 단추 상태, 캔버스(미리보기 효과)·전/후 미리보기를 갱신한다.</summary>
    private void Changed()
    {
        InvalidateStrip();
        SyncExtendEditor();
        UpdateButtons();
        int index = _project.IndexOf(_layer);
        if (index >= 0) _project.PreviewLayerEffects(index, _effects);
        RefreshPreview();
        SyncCanvasExpansion();
    }

    private string EffectSummary(LayerEffect e)
        => Summary(_virtualRangeOverrides.TryGetValue(e, out var range) ? e with { Frames = range } : e);

    private void RefreshEffectSummary(int index)
    {
        if (index >= 0 && index < _effects.Count
            && _list.Items[index] is ListBoxItem { Content: StackPanel { Children: [_, TextBlock text] } })
            text.Text = EffectSummary(_effects[index]);
    }

    private static string Summary(LayerEffect e) => (e switch
    {
        OutlineEffect o => $"외곽선 · {(o.Inside ? "안쪽" : "바깥")} {o.Thickness}px · {(o.Diagonal ? "8방향" : "4방향")} · {o.Color.ToHex()}",
        ShadowEffect s => $"그림자 · ({s.OffsetX}, {s.OffsetY}) · {s.Color.ToHex()}" + (s.Dither != ShadowDither.None ? $" · 디더 {ShadowDithers[(int)s.Dither]}" : ""),
        RimLightEffect r => $"가장자리 빛·그늘 · {Directions.First(d => d.Dir == r.Direction).Name} · {r.Size}px",
        ColorOverlayEffect c => $"색 덮기 · {c.Amount}% · {c.Color.ToHex()}",
        ExplodeEffect x => $"터지기 · {x.Distance}px · {Centers[(int)x.Center].Split(' ')[0]}" + (x.KeepOriginal ? " · 원본 남김" : ""),
        ShineEffect s => $"빛 지나가기 · {s.Angle}° · {s.Width}px · {s.Strength}%",
        SquashEffect q => $"늘었다 줄기 · 가로 {q.WidthAmount}% · 세로 {q.HeightAmount}% · {q.Bounces}번",
        GradientOverlayEffect g => $"그라데이션 덮기 · {g.Angle}° · {Blends[(int)g.Blend]} · {g.Strength}%" + (g.Dither != EffectDither.None ? $" · {Dithers[(int)g.Dither]}" : ""),
        InnerGlowEffect g => $"안쪽 빛 · {g.Size}px · {g.Strength}% · {g.Color.ToHex()}",
        PaletteSwapEffect p => $"색 바꾸기 · {p.Pairs.Count}쌍" + (p.Tolerance > 0 ? $" · 허용 {p.Tolerance}" : ""),
        ColorAdjustEffect c => $"색조 보정 · {AdjustSummary(c)}",
        ReflectionEffect m => $"반사 · {(m.Above ? "위" : "아래")} · 간격 {m.Gap}px · {m.Height}% · {m.OpacityStart}→{m.OpacityEnd}%" + (m.DitherFade ? " · 디더" : ""),
        HitFlashEffect hit => $"피격 번쩍임 · {hit.Count}번 · {hit.Strength}% · {hit.Color.ToHex()}" + (hit.Shape == HitFlashShape.Fade ? " · 엷어짐" : ""),
        BlinkEffect blink => $"깜빡이기 · " + (blink.Timing == BlinkTiming.Frames ? $"보임 {blink.OnFrames} / 숨김 {blink.OffFrames}프레임" : $"{blink.Count}번")
                             + $" · {BlinkHides[(int)blink.Hide]}",
        ShakeEffect shake => $"흔들기 · {Axes[(int)shake.Axis]} · {shake.Amount}px · {shake.Count}번" + (shake.Decay ? " · 줄어듦" : "") + (shake.Regular ? " · 규칙적" : ""),
        BobEffect bob => $"둥실둥실 · {(bob.Axis == EffectAxis.Horizontal ? "좌우" : "위아래")} · {bob.Height}px · {bob.Cycles}번",
        AppearEffect appear => $"{appear.DisplayName} · {AppearStyles[(int)appear.Style]}" + (appear.Style == AppearStyle.Wipe ? $" · {WipeFroms[(int)appear.From]}부터" : ""),
        MeltEffect melt => $"녹아내리기 · {melt.Distance}px · 불규칙 {melt.Irregularity}%",
        RotateEffect rotate => $"회전 · {rotate.Angle}° · {(rotate.Clockwise ? "시계" : "반시계")} 방향 · {Anchors[(int)rotate.Anchor].Split(' ')[0]}",
        AfterimageEffect after => $"잔상 · {after.Count}개 · 간격 {after.Interval} · {after.Opacity}%" + (after.Tint ? $" · {after.TintColor.ToHex()}" : ""),
        ColorCycleEffect cycle => $"색 순환 · {cycle.Colors.Count}색 · {cycle.Steps}칸" + (cycle.Reverse ? " · 거꾸로" : ""),
        ShockwaveEffect wave => $"충격파 고리 · {wave.Radius}px · 두께 {wave.Thickness} · {(wave.Behind ? "뒤" : "앞")}",
        HologramEffect holo => $"홀로그램 · {holo.Color.ToHex()} · {holo.TintStrength}% · 투명도 {holo.Opacity}%" + (holo.Dither ? " · 디더" : "")
                               + (holo.GlitchRate > 0 ? $" · 글리치 {holo.GlitchRate}%" : ""),
        _ => e.DisplayName,
    }) + (e.Frames is { } f ? $" · 프레임 {f}" : "");

    // ───────────── 영구 적용 ─────────────

    /// <summary>레이어에 효과를 영구 적용할 수 있는지와, 안 되면 그 까닭.</summary>
    internal static (bool Ok, string? Why) CanBake(SpriteProject project, Layer layer)
    {
        if (layer.IsGroup) return (false, "그룹 효과는 영구 적용할 수 없습니다 — 부위에 적용하세요.");
        if (project.IsLayerLocked(project.IndexOf(layer))) return (false, "잠긴 레이어에는 영구 적용할 수 없습니다.");
        return (true, null);
    }

    /// <summary>
    /// 효과 영구 적용(<see cref="SpriteProject.BakeLayerEffects"/>)한 결과 알림 — "효과 영구 적용: 외곽선 (프레임 1~4)".
    /// </summary>
    internal static string BakeSummary((IReadOnlyList<LayerEffect> Baked, FrameRange? Frames, int Cels, bool Grown) r, Layer layer)
        => $"효과 영구 적용: {string.Join(", ", r.Baked.Select(e => e.DisplayName))} ("
           + (r.Frames is { } f ? f.From == f.To ? $"프레임 {f.From + 1}" : $"프레임 {f.From + 1}~{f.To + 1}" : "모든 프레임")
           + $", 셀 {r.Cels}개)"
           + (r.Grown ? $" · 부위 크기 {layer.PartSize.Width}×{layer.PartSize.Height}로 넓힘" : "")
           + " — Ctrl+Z로 되돌립니다.";

    /// <summary>
    /// 고른 효과 영구 적용: 창에서 고친 효과 목록을 확정하고 곧바로 구워 넣는다(둘을 합쳐 실행취소 한 단계). 창은 닫힌다.
    /// </summary>
    private void Bake()
    {
        int index = _project.IndexOf(_layer), selected = Selected;
        if (index < 0 || selected < 0) return;
        var list = VirtualEffects().ToList();
        var extension = _extension;
        EffectCanvasExpansion? expansion = null;
        try
        {
            if (_expandCanvas.On() && CanvasExpansion(list) is { } plan && !plan.IsNone(_project.Width, _project.Height))
                expansion = plan;
        }
        catch (InvalidOperationException ex)
        {
            Ui.Error(this, ex.Message);
            return;
        }
        int oldWidth = _project.Width, oldHeight = _project.Height;
        _project.EndLayerEffectsPreview(index, _original, commit: false);
        try
        {
            (IReadOnlyList<LayerEffect>, FrameRange?, int, bool) result = default;
            var change = ProjectChange.Layers | ProjectChange.Pixels
                         | (extension is null ? 0 : ProjectChange.Frames | ProjectChange.Tags)
                         | (expansion is null ? 0 : ProjectChange.Canvas | ProjectChange.Frames | ProjectChange.Settings);
            string label = extension is not null && expansion is not null ? "프레임·캔버스 늘리기 · 효과 영구 적용"
                : extension is not null ? "프레임 늘리기 · 효과 영구 적용"
                : expansion is not null ? "캔버스 확대 · 효과 영구 적용" : "효과 영구 적용";
            _project.Edit(label, change, () =>
            {
                if (extension is { } ext) _project.ExtendFrames(ext);
                _project.SetLayerEffects(index, list);
                if (expansion is { } grow) ApplyCanvasExpansion(grow);
                result = _project.BakeLayerEffects(index, [selected]);
            });
            ResultMessage = (extension is { } x ? ExtendSummary(x) + " · " : "")
                            + (expansion is { } grow ? CanvasExpansionSummary(oldWidth, oldHeight, grow) + " · " : "")
                            + BakeSummary(result, _layer);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _project.PreviewLayerEffects(index, _effects);
            Ui.Error(this, ex.Message);
            return;
        }
        _baked = true;
        DialogResult = DialogResult.OK;
    }

    // ───────────── 설정 ─────────────

    /// <summary>고른 효과의 설정 칸을 다시 만든다. 설명 글은 칸에 늘어놓지 않고 프레임 범위 줄 끝의 정보 표시(i) 풍선에 모은다.</summary>
    private void ShowSettings()
    {
        _rangeRow = null;
        _extendCount = null;
        _extendModeBox = null;
        _extendFor = -1;
        FillSettings();
        var hints = _settings.Children.OfType<TextBlock>().Where(t => t.Classes.Contains(HintClass)).ToList();
        if (hints.Count == 0 || _rangeRow is null) return;
        foreach (var h in hints) _settings.Children.Remove(h);
        _rangeRow.Children.Add(Ui.InfoBadge(string.Join("\n\n", hints.Select(h => h.Text))));
    }

    /// <summary>설정 칸의 프레임 범위 줄(설명 정보 표시를 붙일 곳).</summary>
    private StackPanel? _rangeRow;

    private void FillSettings()
    {
        _settings.Children.Clear();
        int i = Selected;
        if (i < 0 || i >= _effects.Count)
        {
            _settings.Children.Add(Ui.Label(_effects.Count == 0 ? "효과가 없습니다 — 왼쪽에서 골라 추가하세요." : "효과를 고르세요."));
            return;
        }
        var e = _effects[i];
        void Set(Func<LayerEffect, LayerEffect> change) => Update(i, change(_effects[i]));
        AddRangeEditor(i);

        switch (e)
        {
            case OutlineEffect o:
                SettingsRow("색", EffectColorEditor(o, nameof(OutlineEffect.Color), ColorEditor(o.Color, c => Set(x => (OutlineEffect)x with { Color = c })), Set));
                SettingsRow("두께", NumEditor(1, OutlineEffect.MaxThickness, o.Thickness, NumKinds.Thickness, v => Set(x => (OutlineEffect)x with { Thickness = v })), Ui.Label("px"));
                SettingsRow("방향", ChoiceEditor(["4방향 (십자)", "8방향 (모서리 포함)"], o.Diagonal ? 1 : 0, k => Set(x => (OutlineEffect)x with { Diagonal = k == 1 })));
                SettingsRow("위치", ChoiceEditor(["바깥 (그림 밖으로)", "안쪽 (가장자리 픽셀)"], o.Inside ? 1 : 0, k => Set(x => (OutlineEffect)x with { Inside = k == 1 })));
                break;
            case ShadowEffect s:
                SettingsRow("색", EffectColorEditor(s, nameof(ShadowEffect.Color), ColorEditor(s.Color, c => Set(x => (ShadowEffect)x with { Color = c })), Set));
                SettingsRow("X",
                    NumEditor(-ShadowEffect.MaxOffset, ShadowEffect.MaxOffset, s.OffsetX, NumKinds.Offset, v => Set(x => (ShadowEffect)x with { OffsetX = v })),
                    Ui.Label("Y"),
                    NumEditor(-ShadowEffect.MaxOffset, ShadowEffect.MaxOffset, s.OffsetY, NumKinds.Offset, v => Set(x => (ShadowEffect)x with { OffsetY = v })),
                    Ui.Label("px"));
                SettingsRow("디더", ChoiceEditor(ShadowDithers, (int)s.Dither, k => Set(x => (ShadowEffect)x with { Dither = (ShadowDither)k })));
                _settings.Children.Add(Hint("그림 모양 그대로 옮긴 단단한 그림자(알파 = 진하기). 디더를 고르면 반투명 대신 불투명한 색을 그 밀도의 무늬(50% = 바둑판)로 깝니다."));
                break;
            case RimLightEffect r:
                SettingsRow("빛이 오는 쪽",
                    ChoiceEditor(Directions.Select(d => d.Name).ToArray(), Array.FindIndex(Directions, d => d.Dir == r.Direction),
                        k => Set(x => (RimLightEffect)x with { Direction = Directions[k].Dir })));
                SettingsRow("범위", NumEditor(1, RimLightEffect.MaxSize, r.Size, NumKinds.Thickness,
                    v => Set(x => (RimLightEffect)x with { Size = v })), Ui.Label("px"));
                SettingsRow("빛 색", EffectColorEditor(r, nameof(RimLightEffect.Light), ColorEditor(r.Light, c => Set(x => (RimLightEffect)x with { Light = c })), Set));
                SettingsRow("그늘 색", EffectColorEditor(r, nameof(RimLightEffect.Shade), ColorEditor(r.Shade, c => Set(x => (RimLightEffect)x with { Shade = c })), Set));
                _settings.Children.Add(Hint("빛 쪽 가장자리에서 지정한 칸 수만큼 빛 색, 반대쪽에 그늘 색을 덧칠합니다(알파 = 세기, 0 = 안 칠함)."));
                break;
            case ColorOverlayEffect c:
                SettingsRow("색", EffectColorEditor(c, nameof(ColorOverlayEffect.Color), ColorEditor(c.Color, v => Set(x => (ColorOverlayEffect)x with { Color = v })), Set));
                SettingsRow("양", NumEditor(0, 100, c.Amount, NumKinds.Strength, v => Set(x => (ColorOverlayEffect)x with { Amount = v })), Ui.Label("%"));
                _settings.Children.Add(Hint("보이는 픽셀을 그 색 쪽으로 물들입니다(100% = 실루엣, 투명도는 그대로)."));
                break;
            case ExplodeEffect x:
            {
                ExplodeEffect X(LayerEffect v) => (ExplodeEffect)v;
                SettingsRow("거리", NumEditor(1, ExplodeEffect.MaxDistance, x.Distance, NumKinds.Distance, v => Set(o => X(o) with { Distance = v })), Ui.Label("px (끝까지 날아가는 거리)"));
                var cx = NumEditor(-PixelImage.MaxSize, PixelImage.MaxSize, x.CenterX, CenterKind(vertical: false), v => Set(o => X(o) with { CenterX = v }));
                var cy = NumEditor(-PixelImage.MaxSize, PixelImage.MaxSize, x.CenterY, CenterKind(vertical: true), v => Set(o => X(o) with { CenterY = v }));
                cx.IsEnabled = cy.IsEnabled = x.Center == ExplodeCenter.Custom;
                SettingsRow("중심", ChoiceEditor(Centers, (int)x.Center, k =>
                {
                    cx.IsEnabled = cy.IsEnabled = (ExplodeCenter)k == ExplodeCenter.Custom;
                    Set(o => X(o) with { Center = (ExplodeCenter)k });
                }));
                SettingsSubRow("X", cx, Ui.Label("Y"), cy, Ui.Label("px (원본 왼쪽 위 기준)"));
                SettingsRow("흩어짐", NumEditor(0, 100, x.Spread, NumKinds.Random, v => Set(o => X(o) with { Spread = v })), Ui.Label("%"),
                    Ui.Label("  중력"), NumEditor(-ExplodeEffect.MaxGravity, ExplodeEffect.MaxGravity, x.Gravity, NumKinds.Gravity, v => Set(o => X(o) with { Gravity = v })), Ui.Label("px"));
                SettingsRow("조각 크기", NumEditor(1, ExplodeEffect.MaxPieceSize, x.PieceSize, NumKinds.Piece, v => Set(o => X(o) with { PieceSize = v })), Ui.Label("px"),
                    CheckEditor("끝으로 갈수록 작게", x.Shrink, v => Set(o => X(o) with { Shrink = v })));
                var fadeStart = NumEditor(0, 100, x.FadeStart, NumKinds.Progress, v => Set(o => X(o) with { FadeStart = v }));
                fadeStart.IsEnabled = x.Fade;
                SettingsCheckRow(CheckEditor("사라짐", x.Fade, v => { fadeStart.IsEnabled = v; Set(o => X(o) with { Fade = v }); }).Tip("끝으로 갈수록 흐려지며 사라집니다(시작 = 흐려지기 시작하는 진행 %)."),
                    Ui.Label("시작"), fadeStart, Ui.Label("%"));
                SettingsRow("번쩍임", NumEditor(0, ExplodeEffect.MaxFlashFrames, x.FlashFrames, NumKinds.Frames, v => Set(o => X(o) with { FlashFrames = v })), Ui.Label("프레임"));
                SettingsSubRow("번쩍임 색", EffectColorEditor(x, nameof(ExplodeEffect.FlashColor), ColorEditor(x.FlashColor, v => Set(o => X(o) with { FlashColor = v })), Set));
                SettingsRow("", CheckEditor("원본 남기기 (복사본이 날아감)", x.KeepOriginal, v => Set(o => X(o) with { KeepOriginal = v })));
                var seed = NumEditor(0, ExplodeEffect.MaxSeed, x.Seed, NumKinds.Seed, v => Set(o => X(o) with { Seed = v }));
                var reroll = Ui.IconButton(VectorIcon.Dice, null, (_, _) => seed.Value = System.Random.Shared.Next(0, ExplodeEffect.MaxSeed + 1), 36);
                ToolTip.SetTip(reroll, "다른 흩어짐(시드를 무작위로)");
                SettingsRow("시드", seed, reroll);
                AddMotionEditors(i, Set);
                _settings.Children.Add(Hint("범위 첫 프레임은 원래 그림 그대로, 그 뒤로 보이는 픽셀이 중심에서 바깥으로 튀어 나가며 사라집니다. 셀 그림은 그대로입니다."
                                            + " 범위의 프레임마다 그 프레임 그림이 조각이 됩니다(그림이 없는 프레임은 터질 것도 없음)."));
                break;
            }
            case ShineEffect s:
            {
                ShineEffect S(LayerEffect v) => (ShineEffect)v;
                SettingsRow("각도", NumEditor(0, 359, s.Angle, NumKinds.Angle, v => Set(o => S(o) with { Angle = v })), Ui.Label("° (지나가는 방향, 45 = ↗)"));
                SettingsRow("폭", NumEditor(1, ShineEffect.MaxWidth, s.Width, NumKinds.Px, v => Set(o => S(o) with { Width = v })), Ui.Label("px"),
                    Ui.Label("  세기"), NumEditor(0, 100, s.Strength, NumKinds.Strength, v => Set(o => S(o) with { Strength = v })), Ui.Label("%"));
                SettingsRow("색", EffectColorEditor(s, nameof(ShineEffect.Color), ColorEditor(s.Color, v => Set(o => S(o) with { Color = v })), Set));
                SettingsRow("부드러움", ChoiceEditor(["딱딱하게", "가장자리 1px", "가장자리 2px"], s.Softness, k => Set(o => S(o) with { Softness = k })));
                AddMotionEditors(i, Set);
                _settings.Children.Add(Hint("빛 띠가 그림 한쪽 바로 밖에서 들어와 반대쪽으로 빠져나갑니다. 보이는 픽셀만 밝아지고(스크린), 투명한 곳·투명도는 그대로입니다."));
                break;
            }
            case SquashEffect q:
            {
                SquashEffect Q(LayerEffect v) => (SquashEffect)v;
                SettingsRow("가로 세기", NumEditor(0, SquashEffect.MaxAmount, q.WidthAmount, NumKinds.Squash, v => Set(o => Q(o) with { WidthAmount = v })), Ui.Label("%"),
                    Ui.Label("  세로 세기"), NumEditor(0, SquashEffect.MaxAmount, q.HeightAmount, NumKinds.Squash, v => Set(o => Q(o) with { HeightAmount = v })), Ui.Label("%"));
                SettingsRow("튕김", NumEditor(1, SquashEffect.MaxBounces, q.Bounces, NumKinds.Count, v => Set(o => Q(o) with { Bounces = v })), Ui.Label("번"));
                SettingsRow("기준점", ChoiceEditor(Anchors, (int)q.Anchor, k => Set(o => Q(o) with { Anchor = (SquashAnchor)k })));
                SettingsRow("순서", ChoiceEditor(["옆으로 먼저 (납작 → 길쭉)", "위로 먼저 (길쭉 → 납작)"], q.StretchFirst ? 1 : 0, k => Set(o => Q(o) with { StretchFirst = k == 1 })));
                AddMotionEditors(i, Set);
                _settings.Children.Add(Hint("가로·세로 세기를 따로 정해 출렁입니다. 0%인 축은 고정되고 픽셀은 늘이거나 줄여도 또렷합니다."));
                break;
            }
            case GradientOverlayEffect g: GradientPanel(g, Set); break;
            case InnerGlowEffect g: InnerGlowPanel(g, Set); break;
            case PaletteSwapEffect p: PaletteSwapPanel(p, Set); break;
            case ColorAdjustEffect c: ColorAdjustPanel(c, Set); break;
            case ReflectionEffect m: ReflectionPanel(m, Set); break;
            case HitFlashEffect hit: HitFlashPanel(hit, Set); break;
            case BlinkEffect blink: BlinkPanel(blink, Set); break;
            case ShakeEffect shake: ShakePanel(shake, Set); break;
            case BobEffect bob: BobPanel(bob, Set); break;
            case AppearEffect appear: AppearPanel(appear, Set); break;
            case MeltEffect melt: MeltPanel(melt, Set); break;
            case RotateEffect rotate: RotatePanel(rotate, Set); break;
            case AfterimageEffect after: AfterimagePanel(after, Set); break;
            case ColorCycleEffect cycle: ColorCyclePanel(cycle, Set); break;
            case ShockwaveEffect wave: ShockwavePanel(wave, Set); break;
            case HologramEffect holo: HologramPanel(holo, Set); break;
        }
    }

    /// <summary>
    /// 움직이는 효과 공통: 빠르기 곡선, 반복(한 번/반복/왕복 + 횟수), 범위 길이 안내. <paramref name="timing"/>이 false면(진행 곡선을 쓰지 않는 효과 — 깜빡이기 프레임 간격·잔상)
    /// 범위 안내만.
    /// </summary>
    private void AddMotionEditors(int i, Action<Func<LayerEffect, LayerEffect>> set, bool timing = true)
    {
        var a = (AnimatedEffect)_effects[i];
        if (timing) AddTimingEditors(a, set);
        var (from, to) = VRangeOf(a);
        _settings.Children.Add(Hint($"지금 {to - from + 1}프레임(프레임 {from + 1}~{to + 1}) 동안 움직입니다 — 범위를 늘리면 느려지고 줄이면 빨라집니다."
                                    + (from == to ? " 한 프레임이면 움직이지 않습니다." : "")));
    }

    private void AddTimingEditors(AnimatedEffect a, Action<Func<LayerEffect, LayerEffect>> set)
    {
        SettingsRow("빠르기", ChoiceEditor(Easings, (int)a.Easing, k => set(o => (AnimatedEffect)o with { Easing = (EffectEasing)k })));
        var count = NumEditor(1, AnimatedEffect.MaxRepeatCount, a.RepeatCount, NumKinds.Repeat, v => set(o => (AnimatedEffect)o with { RepeatCount = v }));
        count.IsEnabled = a.Repeat != EffectRepeat.Once;
        SettingsRow("반복", ChoiceEditor(Repeats, (int)a.Repeat, k =>
        {
            count.IsEnabled = (EffectRepeat)k != EffectRepeat.Once;
            set(o => (AnimatedEffect)o with { Repeat = (EffectRepeat)k });
        }), count, Ui.Label("번"));
    }

    /// <summary>
    /// 프레임 범위: "모든 프레임"(나중에 늘어난 프레임까지) 또는 시작~끝 프레임(1부터). 체크를 풀면 현재 프레임 하나로 시작한다.
    /// 타임라인의 효과 줄에서 상자 끝을 끌어도 바뀐다.
    /// </summary>
    private void AddRangeEditor(int i)
    {
        var effect = _effects[i];
        bool virtualRange = UsesVirtualTimeline(effect);
        int n = virtualRange ? VirtualCount : _project.Frames.Count;
        FrameRange? range = virtualRange
            ? _virtualRangeOverrides.TryGetValue(effect, out var direct)
                ? direct
                : effect.Frames is { } stored && _extension is { } extension ? extension.MapRange(stored) : null
            : effect.Frames;
        var all = Ui.Check("모든 프레임", range is null);
        var from = WithKind(Ui.Num(1, n, (range?.From ?? 0) + 1, 56), FrameNumberKind(end: false));
        var to = WithKind(Ui.Num(1, n, (range?.To ?? n - 1) + 1, 56), FrameNumberKind(end: true));
        from.IsEnabled = to.IsEnabled = range is not null;
        bool syncing = false;
        void Apply()
        {
            // 선택을 바꾸며 없어진 범위 칸의 지연 이벤트는 이전 효과에도, 새 효과에도 적용하지 않는다.
            if (syncing || Selected != i || _extendFor != i) return;
            var next = all.On() ? null : FrameRange.Of((int)from.NumValue() - 1, (int)to.NumValue() - 1, n);
            if (next is null && !all.On())
            {
                // 모든 프레임을 덮는 범위 = "모든 프레임"
                syncing = true;
                all.IsChecked = true;
                syncing = false;
            }
            from.IsEnabled = to.IsEnabled = next is not null;
            if (virtualRange)
            {
                SetVirtualRange(i, next);
            }
            else
            {
                FollowRange(_effects[i], next);
                Update(i, _effects[i] with { Frames = next });
            }
            SyncExtendEditor();
            if (_effects[i] is AnimatedEffect) ShowSettings(); // 범위 길이 안내 갱신
        }
        all.IsCheckedChanged += (_, _) =>
        {
            if (!all.On() && (int)from.NumValue() == 1 && (int)to.NumValue() == n)
            {
                // 모든 프레임을 덮는 범위는 "모든 프레임"과 같으므로 현재 프레임 하나로 시작한다.
                syncing = true;
                from.Value = to.Value = _frame + 1;
                syncing = false;
            }
            Apply();
        };
        from.ValueChanged += (_, _) => Apply();
        to.ValueChanged += (_, _) => Apply();
        _rangeRow = SettingsRow("범위", all, from, Ui.Label("~"), to);
        AddExtendEditor(i);
    }

    /// <summary>설정 칸 이름 열의 크기 공유 묶음 — 모든 줄의 이름 열이 가장 긴 이름에 맞춰 같은 폭이 된다.</summary>
    private const string LabelColumn = "EffectSettingsLabel";
    /// <summary>위 줄에 딸린 줄(<see cref="SettingsSubRow"/>)의 이름 들여쓰기.</summary>
    private const double SubIndent = 12;

    /// <summary>
    /// 설정 칸에 한 줄: 왼쪽 이름 열(모든 줄이 같은 폭, 왼쪽 맞춤)에 <paramref name="label"/>, 오른쪽 열에 컨트롤들(모든 줄이 같은 x에서 시작).
    /// 이름이 ""면 오른쪽 열만. 오른쪽 열 줄을 돌려준다.
    /// </summary>
    private StackPanel SettingsRow(string label, params Control[] controls) => FormRow(label.Length == 0 ? null : Ui.Label(label), 0, controls);

    /// <summary>위 줄에 딸린 줄(위 줄 고름에 따라 쓰이는 값): 이름을 조금 들여 쓴다.</summary>
    private StackPanel SettingsSubRow(string label, params Control[] controls) => FormRow(Ui.Label(label), SubIndent, controls);

    /// <summary>체크 상자가 이름인 줄(켜야 오른쪽 값을 씀): 체크 상자가 이름 열에 들어간다.</summary>
    private StackPanel SettingsCheckRow(CheckBox head, params Control[] controls) => FormRow(head, 0, controls);

    private StackPanel FormRow(Control? head, double indent, Control[] controls)
    {
        if (head is not null)
        {
            head.VerticalAlignment = VerticalAlignment.Center;
            head.Margin = new Thickness(head.Margin.Left + indent, head.Margin.Top, head.Margin.Right + 6, head.Margin.Bottom);
        }
        var row = Ui.Row(controls);
        AddFormRow(head, row);
        return row;
    }

    /// <summary>오른쪽 열에 한 덩어리(줄 바꿈 패널 등 — 열 폭에 맞춰 늘어남). 이름은 첫 줄 높이에 맞춘다.</summary>
    private void SettingsBlock(string label, Control body)
    {
        var head = Ui.Label(label);
        head.VerticalAlignment = VerticalAlignment.Top;
        head.Margin = new Thickness(3, 6, 9, 3);
        AddFormRow(head, body);
    }

    /// <summary>이름 열(<see cref="LabelColumn"/> 공유 폭, 왼쪽 맞춤) + 값 열 한 줄을 설정 칸에 넣는다.</summary>
    private void AddFormRow(Control? head, Control body)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = LabelColumn });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (head is not null)
        {
            head.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(head);
        }
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        _settings.Children.Add(grid);
    }

    private const string HintClass = "effect-hint";

    /// <summary>설명 글. 설정 칸에 넣으면 <see cref="ShowSettings"/>가 정보 표시 풍선으로 옮긴다(펼침 상자 안에서는 그대로 보임).</summary>
    private static TextBlock Hint(string text, double maxWidth = 290)
    {
        var t = Ui.Label(text);
        t.TextWrapping = TextWrapping.Wrap;
        t.MaxWidth = maxWidth;
        t.Foreground = AppTheme.DimTextBrush;
        t.Classes.Add(HintClass);
        return t;
    }

    private static ColorField ColorEditor(Color32 value, Action<Color32> changed)
    {
        var field = new ColorField(value) { LivePreview = true };
        field.ColorChanged += (_, _) => changed(field.Color);
        return field;
    }

    /// <summary>효과 값 숫자 칸: <paramref name="kind"/>의 우클릭 예시 값·단위·최근 값 묶음을 붙인다.</summary>
    private static NumericUpDown NumEditor(int min, int max, int value, NumKind kind, Action<int> changed)
    {
        var n = WithKind(Ui.Num(min, max, value, 56), kind);
        n.ValueChanged += (_, _) => changed((int)n.NumValue());
        return n;
    }

    private static NumericUpDown WithKind(NumericUpDown n, NumKind kind) => n.Presets(kind.Values, [], kind.Format).Unit(kind.Unit).RecentKey(kind.Key);

    /// <summary>
    /// 프레임 번호 칸(1부터, 범위 시작·끝) 예시: 처음·지금·마지막 프레임과 태그 시작(끝) 프레임에 이름을 붙이고, 남는 자리는 고른 간격.
    /// </summary>
    private NumKind FrameNumberKind(bool end)
    {
        int n = _project.Frames.Count, now = _frame + 1;
        var names = new Dictionary<decimal, string>();
        foreach (var t in _project.Tags)
            if ((end ? t.To : t.From) + 1 is var f && f >= 1 && f <= n) names.TryAdd(f, $"{t.Name} {(end ? "끝" : "시작")}");
        names[now] = names.TryGetValue(now, out var tag) ? $"지금 · {tag}" : "지금";
        var values = new SortedSet<decimal>(names.Keys) { 1, n };
        foreach (var v in Ui.FrameNumbers(n))
        {
            if (values.Count >= 12) break;
            values.Add(v);
        }
        return new(end ? "effect.frame.to" : "effect.frame.from", "", [.. values],
            v => names.TryGetValue(v, out var name) ? $"{v} ({name})" : $"{v}");
    }

    /// <summary>중심 X·Y 칸(원본 왼쪽 위 기준 px) 예시: 0, 그림 가운데·끝(이름 붙임)과 흔한 거리.</summary>
    private NumKind CenterKind(bool vertical)
    {
        var source = SourceAt(_frame);
        int size = source is null ? vertical ? _project.Height : _project.Width : vertical ? source.Height : source.Width;
        decimal mid = size / 2, last = Math.Max(0, size - 1);
        var values = new SortedSet<decimal> { 0, mid, last };
        foreach (var v in new decimal[] { 4, 8, 16, 24, 32, 48, 64 })
            if (v < last && values.Count < 10) values.Add(v);
        string edge = vertical ? "아래 끝" : "오른쪽 끝";
        return new(vertical ? "effect.center.y" : "effect.center.x", "px", [.. values],
            v => v == mid ? $"{v} px (가운데)" : v == last && v > 0 ? $"{v} px ({edge})" : $"{v} px");
    }

    private static ComboBox ChoiceEditor(string[] items, int selected, Action<int> changed)
    {
        var c = Ui.Combo(170, items);
        c.SelectedIndex = Math.Max(0, selected);
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) changed(c.SelectedIndex); };
        return c;
    }

    private static CheckBox CheckEditor(string text, bool value, Action<bool> changed)
    {
        var c = Ui.Check(text, value);
        c.IsCheckedChanged += (_, _) => changed(c.On());
        return c;
    }

    // ───────────── 미리보기 ─────────────

    /// <summary>움직이는 효과의 프레임 범위(null = 모든 프레임).</summary>
    private (int From, int To) RangeOf(LayerEffect e)
        => e.Frames is { } r ? (r.From, Math.Min(r.To, _project.Frames.Count - 1)) : (0, _project.Frames.Count - 1);

    /// <summary>재생할 범위: "고른 효과 범위만"이면 고른 효과 범위, 체크를 끄면 늘린 뒤 전체 프레임.</summary>
    private (int From, int To)? PlayRange()
    {
        if (_effectOnly.On())
            return Selected >= 0 && Selected < _effects.Count ? VRangeOf(_effects[Selected]) : null;
        return VirtualCount > 0 ? (0, VirtualCount - 1) : null;
    }

    /// <summary>재생 범위를 현재 상태에 맞춘다. 효과를 새로 골랐거나 범위 방식을 바꾸면 처음부터 곧바로 재생한다.</summary>
    private void SyncPlayback(bool restart = false)
    {
        var range = PlayRange();
        _play.IsEnabled = range is not null;
        if (range is null)
        {
            _timer.Stop();
            int frame = Math.Clamp(VirtualOf(_frame), 0, VirtualCount - 1);
            if (_previewFrame != frame)
            {
                _previewFrame = frame;
                RefreshPreview();
            }
        }
        else
        {
            bool moved = restart || _previewFrame < range.Value.From || _previewFrame > range.Value.To;
            if (moved)
            {
                _previewFrame = range.Value.From;
                RefreshPreview();
            }
            if (restart) StartTimer();
        }
        _play.Content = _timer.IsEnabled ? Ui.IconLabel(VectorIcon.Pause, "멈춤") : Ui.IconLabel(VectorIcon.Play, "재생");
    }

    private void TogglePlay()
    {
        if (_timer.IsEnabled) _timer.Stop();
        else if (PlayRange() is { } range)
        {
            if (_previewFrame < range.From || _previewFrame >= range.To) _previewFrame = range.From;
            StartTimer();
            RefreshPreview();
        }
        _play.Content = _timer.IsEnabled ? Ui.IconLabel(VectorIcon.Pause, "멈춤") : Ui.IconLabel(VectorIcon.Play, "재생");
    }

    private void StartTimer()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(40, _project.Frames[OriginalOf(_previewFrame)].DurationMs));
        _timer.Start();
    }

    /// <summary>재생 중 다음 프레임(범위 끝 다음은 처음 — "끝에서 바로 처음으로"가 꺼져 있으면 끝 프레임은 조금 더 보여 줌).</summary>
    private void NextPreviewFrame()
    {
        if (PlayRange() is not { } range)
        {
            SyncPlayback();
            return;
        }
        _previewFrame = _previewFrame >= range.To || _previewFrame < range.From ? range.From : _previewFrame + 1;
        int ms = Math.Max(40, _project.Frames[OriginalOf(_previewFrame)].DurationMs);
        _timer.Interval = TimeSpan.FromMilliseconds(_previewFrame == range.To && !s_seamlessLoop ? ms * 3 : ms);
        RefreshPreview();
    }

    /// <summary>효과 원본(그 프레임, 창이 열린 동안 바뀌지 않으므로 프레임마다 한 번만 만든다).</summary>
    private PixelImage? SourceAt(int frame)
    {
        if (!_sources.TryGetValue(frame, out var src)) _sources[frame] = src = _project.EffectSource(_layer, frame);
        return src;
    }

    /// <summary>
    /// 미리보기 프레임의 전(효과 없이)·후(그 프레임 범위 안의 효과만)를 그린다. 크기는 모든 프레임 같게(효과 여백 전체).
    /// 프레임을 늘리는 중이면 늘린 뒤 차례의 프레임(<see cref="AnimateAt"/>).
    /// </summary>
    private void RefreshPreview()
    {
        int f = _previewFrame;
        _strip.Current = f;
        var all = VirtualEffects();
        var (source, effects) = AnimateAt(f, all);
        string where = _layer.IsGroup ? " (그룹 합성)" : _layer.IsPart ? " (부위)" : "";
        if (_extension is not null)
            where = $"/{VirtualCount}{where} · 원래 프레임 {OriginalOf(f) + 1}{(IsCopyFrame(f) ? " 사본" : "")}";
        if (source is null)
        {
            _caption.Text = $"프레임 {f + 1}{where}";
            _preview.SetImages(null, null);
            return;
        }
        var (after, dx, dy) = LayerEffects.Render(source, effects, LayerEffects.Padding(all, source.Width, source.Height));
        var before = new PixelImage(after.Width, after.Height);
        before.Blit(source, -dx, -dy);
        _preview.SetImages(before, after);
        string progress = Selected >= 0 && Selected < all.Count && all[Selected] is AnimatedEffect a && a.AppliesTo(f)
            ? $" · 진행 {a.AtFrame(f, VirtualCount, _project.EffectPivot(_layer, OriginalOf(f))).Time * 100:0}%" : "";
        _caption.Text = $"프레임 {f + 1}{where}{progress}";
    }

    /// <summary>전·후를 같은 크기·같은 정수 배율로 나란히 그린다(후 그림이 번진 만큼 둘 다 넓힘).</summary>
    private sealed class PreviewView : Control, IDisposable
    {
        private WriteableBitmap? _before, _after;

        public void SetImages(PixelImage? before, PixelImage? after)
        {
            Dispose();
            if (before is not null && after is not null)
            {
                _before = ImageUtil.ToBitmap(before);
                _after = ImageUtil.ToBitmap(after);
            }
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            var b = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.BackBrush, b);
            if (_before is null || _after is null)
            {
                AppTheme.DrawTextIn(g, "이 프레임에 그림이 없습니다", b, AppTheme.DimTextBrush, center: true);
                return;
            }
            double half = (b.Width - 12) / 2, top = 18;
            var src = new Rectangle(0, 0, _before.PixelSize.Width, _before.PixelSize.Height);
            AppTheme.DrawTextIn(g, "전", new Rect(0, 0, half, 16), AppTheme.DimTextBrush, AppTheme.SmallFontSize, center: true);
            AppTheme.DrawTextIn(g, "후", new Rect(half + 12, 0, half, 16), AppTheme.DimTextBrush, AppTheme.SmallFontSize, center: true);
            var area = new Rect(0, top, half, b.Height - top);
            double fit = Math.Min(area.Width / src.Width, area.Height / src.Height);
            int scale = fit >= 1 ? (int)Math.Floor(fit) : 0; // 같은 배율로(작으면 맞춤 축소)
            ImageUtil.DrawFitted(g, _before, src, area, scale, checker: true);
            ImageUtil.DrawFitted(g, _after, src, new Rect(half + 12, top, half, b.Height - top), scale, checker: true);
        }

        public void Dispose()
        {
            _before?.Dispose();
            _after?.Dispose();
            _before = _after = null;
        }
    }
}
