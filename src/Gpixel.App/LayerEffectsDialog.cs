using Gpixel.Core.Edit;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

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
        (UiStrings.S_LayerEffectsDialog_KindTable_5C190C1C74, UiStrings.S_LayerEffectsDialog_KindTable_45261BC59C, () => new OutlineEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_10CD6603BC, UiStrings.S_LayerEffectsDialog_KindTable_2576225185, () => new ShadowEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_126136E630, UiStrings.S_LayerEffectsDialog_KindTable_4230F44F0C, () => new RimLightEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_C29C82BC8E, UiStrings.S_LayerEffectsDialog_KindTable_03AE924348, () => new ColorOverlayEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_25FE384908, UiStrings.S_LayerEffectsDialog_KindTable_6134E7833F, () => new InnerGlowEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_20C2A2DCF6, UiStrings.S_LayerEffectsDialog_KindTable_4E3A3D50CE, () => new PaletteSwapEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_CA9E953421, UiStrings.S_LayerEffectsDialog_KindTable_02A04E0B04, () => new ColorAdjustEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_5627BD3D9D, UiStrings.S_LayerEffectsDialog_KindTable_6A331276D8, () => new ReflectionEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_AC8A98B707, UiStrings.S_LayerEffectsDialog_KindTable_91F153F61A, () => new ExplodeEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_174D9850CC, UiStrings.S_LayerEffectsDialog_KindTable_E97AB47F1D, () => new ShineEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_792E7BADCE, UiStrings.S_LayerEffectsDialog_KindTable_7C6C1B6117, () => new SquashEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_1725C3F28A, UiStrings.S_LayerEffectsDialog_KindTable_993BDE3846, () => new HitFlashEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_510993AD9E, UiStrings.S_LayerEffectsDialog_KindTable_4E942C831D, () => new BlinkEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_2C5AB3754D, UiStrings.S_LayerEffectsDialog_KindTable_043CDEE3B8, () => new ShakeEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_F5A6D8375C, UiStrings.S_LayerEffectsDialog_KindTable_361470DFA9, () => new BobEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_F6C9786974, UiStrings.S_LayerEffectsDialog_KindTable_FFF387B9B9, () => new AppearEffect()),
        (UiStrings.Fade_Name, UiStrings.Fade_Description, () => new FadeEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_4A3575A660, UiStrings.S_LayerEffectsDialog_KindTable_1B49B1765C, () => new MeltEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_B65A656A1B, UiStrings.S_LayerEffectsDialog_KindTable_34F6760645, () => new RotateEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_C41906B546, UiStrings.S_LayerEffectsDialog_KindTable_69C80DA119, () => new AfterimageEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_23A9E57A2F, UiStrings.S_LayerEffectsDialog_KindTable_1ABE2A05EE, () => new ColorCycleEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_71D60FCC7E, UiStrings.S_LayerEffectsDialog_KindTable_CB2F9D0913, () => new ShockwaveEffect()),
        (UiStrings.S_LayerEffectsDialog_KindTable_2B322D7A38, UiStrings.S_LayerEffectsDialog_KindTable_EF5C5A3497, () => new HologramEffect()),
    ];

    /// <summary>효과 종류의 한 줄 설명(번호 = <see cref="Create"/>의 kind).</summary>
    internal static string DescriptionOf(int kind) => KindTable[kind].Description;

    /// <summary>효과 종류 이름(번호 = <see cref="Create"/>의 kind).</summary>
    internal static readonly string[] Kinds = KindTable.Select(k => k.Name).ToArray();

    /// <summary>종류 번호의 새 효과(기본 설정, 범위 밖이면 외곽선).</summary>
    internal static LayerEffect Create(int kind) => (kind >= 0 && kind < KindTable.Length ? KindTable[kind] : KindTable[0]).New();

    /// <summary>종류가 움직이는 효과(프레임 범위에 따라 모습이 바뀜)인지.</summary>
    internal static bool IsAnimated(int kind) => Create(kind) is AnimatedEffect;

    private static readonly (LightDirection Dir, string Name)[] Directions =
    [
        (LightDirection.TopLeft, UiStrings.S_LayerEffectsDialog_Directions_38A213C040), (LightDirection.Top, UiStrings.S_LayerEffectsDialog_Directions_53073620AC), (LightDirection.TopRight, UiStrings.S_LayerEffectsDialog_Directions_E2ADEEE3AD),
        (LightDirection.Left, UiStrings.S_LayerEffectsDialog_Directions_87DE11F0BD), (LightDirection.Right, UiStrings.S_LayerEffectsDialog_Directions_A573091255),
        (LightDirection.BottomLeft, UiStrings.S_LayerEffectsDialog_Directions_2D3F13D6AD), (LightDirection.Bottom, UiStrings.S_LayerEffectsDialog_Directions_B1DECE0B86), (LightDirection.BottomRight, UiStrings.S_LayerEffectsDialog_Directions_73A07AC9E0),
    ];

    private static readonly string[] Easings = [UiStrings.S_LayerEffectsDialog_Easings_4FA414FBAC, UiStrings.S_LayerEffectsDialog_Easings_BDEEAEDE46, UiStrings.S_LayerEffectsDialog_Easings_503369007B, UiStrings.S_LayerEffectsDialog_Easings_7665ED231C];
    private static readonly string[] Repeats = [UiStrings.S_LayerEffectsDialog_Repeats_3E39AFFB4A, UiStrings.S_LayerEffectsDialog_Repeats_753C57FE3B, UiStrings.S_LayerEffectsDialog_Repeats_466235A991];
    private static readonly string[] Centers = [UiStrings.S_LayerEffectsDialog_Centers_17A9293CA6, UiStrings.S_LayerEffectsDialog_Centers_7CA53FB898, UiStrings.S_LayerEffectsDialog_Centers_4EEB98A21E, UiStrings.S_LayerEffectsDialog_Centers_8D791DFBBE];
    private static readonly string[] Anchors = [UiStrings.S_LayerEffectsDialog_Anchors_0EC3ED31B9, UiStrings.S_LayerEffectsDialog_Anchors_1F3A9869CE, UiStrings.S_LayerEffectsDialog_Anchors_6466116779];
    private static readonly string[] ShadowDithers = [UiStrings.S_LayerEffectsDialog_ShadowDithers_85A6B2AD26, "25%", UiStrings.S_LayerEffectsDialog_ShadowDithers_BC77BAD2B0, "75%"];
    private static readonly string[] Blends = [UiStrings.S_LayerEffectsDialog_Blends_69D924C609, UiStrings.S_LayerEffectsDialog_Blends_1725732230, UiStrings.S_LayerEffectsDialog_Blends_5260B3BD7E];
    private static readonly string[] Dithers = [UiStrings.S_LayerEffectsDialog_Dithers_19C7C4899C, "2×2 Bayer", "4×4 Bayer"];

    /// <summary>숫자 칸 종류: 우클릭 예시 값(칸 범위 밖 값은 빠짐), 단위(메뉴 글자), 최근 값 묶음 이름. <paramref name="Format"/>가 있으면 단위 대신 그 글자.</summary>
    private sealed record NumKind(string Key, string Unit, decimal[] Values, Func<decimal, string>? Format = null);

    /// <summary>효과 설정의 숫자 칸 종류(같은 종류끼리 최근 값을 나눔).</summary>
    private static class NumKinds
    {
        /// <summary>지나가는·바뀌는 방향(0~359°).</summary>
        public static readonly NumKind Angle = new("effect.angle", "°", [0, 15, 30, 45, 60, 90, 120, 135, 180, 225, 270, 315]);
        /// <summary>범위 동안 도는 전체 각도(양수 시계 방향, 음수 반시계 방향).</summary>
        public static readonly NumKind Rotation = new("effect.rotation", "°", [-1440, -720, -540, -360, -270, -180, -90, -45, -30, -15, 0, 15, 30, 45, 90, 180, 270, 360, 540, 720, 1080, 1440]);
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
        /// <summary>무작위 변화·불규칙함(0~100%).</summary>
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
        public static readonly NumKind Distance = new("effect.distance", "px", [0, 4, 8, 12, 16, 24, 32, 48, 64, 96, 128]);
        /// <summary>중력(음수 = 위로).</summary>
        public static readonly NumKind Gravity = new("effect.gravity", "px", [-16, -8, -4, -2, 0, 2, 4, 6, 8, 16, 32]);
        /// <summary>터지기 조각 크기(1~4px).</summary>
        public static readonly NumKind Piece = new("effect.piece", "px", [1, 2, 3, 4]);
        /// <summary>범위 안 횟수(튕김·번쩍임·깜빡임·흔들림·오르내림).</summary>
        public static readonly NumKind Count = new("effect.count", UiStrings.S_LayerEffectsDialog_Count_A61CE50C3B, [1, 2, 3, 4, 5, 6, 8, 10, 12, 16]);
        /// <summary>움직임 반복 횟수.</summary>
        public static readonly NumKind Repeat = new("effect.repeat", UiStrings.S_LayerEffectsDialog_Repeat_ABB1AFE73B, [1, 2, 3, 4, 5, 6, 8]);
        /// <summary>프레임 수(보임·숨김·번쩍임·잔상 간격).</summary>
        public static readonly NumKind Frames = new("effect.frames", UiStrings.S_LayerEffectsDialog_Frames_656538CC7F, [0, 1, 2, 3, 4, 5, 6, 8, 10, 12, 16]);
        /// <summary>잔상 개수.</summary>
        public static readonly NumKind Copies = new("effect.copies", UiStrings.S_LayerEffectsDialog_Copies_54ACB79D5D, [1, 2, 3, 4, 5, 6, 8]);
        /// <summary>주사선 간격(줄).</summary>
        public static readonly NumKind ScanGap = new("effect.scanGap", UiStrings.S_LayerEffectsDialog_ScanGap_5A028BD5A6, [2, 3, 4, 5, 6]);
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
    private readonly CheckBox _effectOnly, _expandCanvas, _loop;
    /// <summary>크게 보기 창의 재생 단추·프레임 설명·재생 범위 체크(창이 닫혀 있으면 null).</summary>
    private Button? _largePlay;
    private TextBlock? _largeCaption;
    private CheckBox? _largeEffectOnly, _largeLoop;
    private readonly ComboBox _expandMode;
    private readonly TextBlock _expandNote;
    private readonly TextBlock _caption;
    private readonly PreviewView _preview = new();
    private Window? _previewWindow;
    private PreviewView? _largePreview;
    private readonly DispatcherTimer _timer = new();
    private int _previewFrame;
    /// <summary>재생 범위: true = 고른 효과 범위, false = 늘린 뒤 전체 프레임. 앱을 켜 둔 동안 유지.</summary>
    private static bool s_effectOnly = true;
    /// <summary>효과에 맞춘 캔버스 확대 방식. 앱을 켜 둔 동안 유지.</summary>
    private static EffectCanvasExpandMode s_canvasExpandMode = EffectCanvasExpandMode.KeepCenter;
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
    public LayerEffectsDialog(EditorContext ctx, Layer layer, int focus = -1, FrameRange? newRange = null, FrameRange? motionRange = null) : base(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_7E8F1A60E2,layer.Name))
    {
        // 숫자·색 값 입력에서 Enter는 입력만 확정한다. 효과 적용은 확인 단추로만 한다.
        OkButton.IsDefault = false;
        _motionRange = motionRange ?? newRange;
        _project = ctx.Project ?? throw new InvalidOperationException(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_42089F3C1D);
        _layer = layer;
        _frame = Math.Clamp(ctx.CurrentFrame, 0, _project.Frames.Count - 1);
        _previewFrame = _frame;
        _original = layer.Effects;
        _effects = [.. layer.Effects];

        int n = _project.Frames.Count;
        _addAll = Ui.Check(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_562B8ABA6D, newRange is null);
        _addFrom = WithKind(Ui.Num(1, n, (newRange?.From ?? 0) + 1, 56), FrameNumberKind(end: false));
        _addTo = WithKind(Ui.Num(1, n, (newRange?.To ?? n - 1) + 1, 56), FrameNumberKind(end: true));
        _addFrom.IsEnabled = _addTo.IsEnabled = newRange is not null;
        ToolTip.SetTip(_addAll, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_58CE85AE10);
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
        _remove = Ui.Button(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_DEC8D5CC64, (_, _) => Remove(), 60);
        _up = Ui.Button(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_4E920AEA13, (_, _) => Move(-1), 60);
        _down = Ui.Button(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_FE8FEDFCFF, (_, _) => Move(1), 60);
        _bake = Ui.Button(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_769C41A711, (_, _) => Bake(), 80);
        _list = new ListBox { Width = SettingsWidth, Height = 170, Margin = new Thickness(3) };
        ToolTip.SetTip(_list, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_A48FBAD60B);
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
            if (!effect.ClipToPrevious) DetachClippedFollowers(from);
            if (to == 0 && effect.ClipToPrevious)
            {
                ReplaceEffect(from, effect with { ClipToPrevious = false });
                effect = _effects[from];
            }
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

        _expandCanvas = Ui.Check(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_420801482E, true);
        _expandMode = Ui.Combo(110, [UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_E64B4CB375, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_F7968CE40F, UiStrings.EffectCanvasExpand_KeepCenter]);
        _expandMode.SelectedIndex = (int)s_canvasExpandMode;
        _expandMode.IsEnabled = true;
        _expandNote = Ui.Label("");
        _expandNote.Foreground = AppTheme.DimTextBrush;
        _expandNote.Margin = new Thickness(3, 0, 0, 2);
        _expandNote.TextWrapping = TextWrapping.Wrap;
        _expandNote.MaxWidth = SettingsWidth;
        _expandNote.IsVisible = true;
        ToolTip.SetTip(_expandCanvas, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_98461A52E4);
        ToolTip.SetTip(_expandMode, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_4DF95CBC10);
        _expandCanvas.IsCheckedChanged += (_, _) =>
        {
            _expandMode.IsEnabled = _expandCanvas.On();
            _expandNote.IsVisible = _expandCanvas.On();
            SyncCanvasExpansion();
            RefreshPreview();
        };
        _expandMode.SelectionChanged += (_, _) =>
        {
            if (_expandMode.SelectedIndex < 0) return;
            s_canvasExpandMode = (EffectCanvasExpandMode)_expandMode.SelectedIndex;
            SyncCanvasExpansion();
            RefreshPreview();
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
            ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_F60FBD674C,mr.From + 1,mr.To + 1)
            : null;
        var rangeLabel = Ui.Label(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_CE6B076915);
        if (motionTip is not null) ToolTip.SetTip(rangeLabel, motionTip);
        var canvasExpand = Ui.Stack(Ui.Row(_expandCanvas, _expandMode), _expandNote);
        var shared = Ui.Group(UiStrings.EffectShared_Title, Ui.Stack(canvasExpand, _extendPanel));
        var left = Ui.Stack(
            Ui.Row(rangeLabel, _addAll, Ui.Label(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_4782D0C688), _addFrom, Ui.Label("~"), _addTo),
            _list,
            Ui.Row(_remove, _up, _down, _bake),
            shared,
            Ui.Group(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_4D4CF63BB1, new ScrollViewer { Content = _settings, MaxHeight = 320, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }));
        left.Width = SettingsWidth + 30;

        _preview.Width = PreviewBox * 2 + 12;
        _preview.Height = PreviewBox + 20;
        _preview.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(_preview, UiStrings.EffectPreview_OpenLarge);
        _preview.PointerPressed += (_, e) =>
        {
            if (!_preview.HasImages || !e.GetCurrentPoint(_preview).Properties.IsLeftButtonPressed) return;
            OpenLargePreview();
            e.Handled = true;
        };
        _caption = Ui.Label("");
        _caption.Foreground = AppTheme.DimTextBrush;
        _play = Ui.Button(VectorIcon.Play, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_3DC0585476, (_, _) => TogglePlay(), 80);
        ToolTip.SetTip(_play, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_46C6465E13);
        _effectOnly = Ui.Check(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_BEC5ADB6D1, s_effectOnly);
        _effectOnly.VerticalAlignment = VerticalAlignment.Center;
        _effectOnly.IsCheckedChanged += (_, _) =>
        {
            s_effectOnly = _effectOnly.On();
            if (_largeEffectOnly is { } large) large.IsChecked = s_effectOnly;
            SyncPlayback(restart: true);
        };
        ToolTip.SetTip(_effectOnly, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_2138B1E743);
        _loop = Ui.Check(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_96BC2CE00E, s_seamlessLoop);
        _loop.VerticalAlignment = VerticalAlignment.Center;
        _loop.IsCheckedChanged += (_, _) =>
        {
            s_seamlessLoop = _loop.On();
            if (_largeLoop is { } large) large.IsChecked = s_seamlessLoop;
        };
        ToolTip.SetTip(_loop, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_0500474F59);
        _timer.Tick += (_, _) => NextPreviewFrame();
        var right = Ui.Stack(
            Ui.Row(_play, _effectOnly, _loop, Ui.InfoBadge(Intro(layer))),
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
            _previewWindow?.Close();
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
        => UiStrings.S_LayerEffectsDialog_Intro_AA476847D8
           + UiStrings.S_LayerEffectsDialog_Intro_3052848546
           + UiStrings.S_LayerEffectsDialog_Intro_C71F0948BE
           + (layer.IsGroup ? UiStrings.S_LayerEffectsDialog_Intro_5F6126CBFA : "")
           + (layer.IsPart ? UiStrings.S_LayerEffectsDialog_Intro_03C1A71EB9 : "");

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
            if (_effects[i].ClipToPrevious)
                row.Children.Add(Ui.Icon(VectorIcon.CornerDownRight, 12));
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
        if (_savePreset is not null) _savePreset.IsEnabled = Selected >= 0;
        var (canBake, why) = CanBake(_project, _layer);
        _bake.IsEnabled = canBake && Selected >= 0;
        int extra = Selected >= 0 ? LayerEffects.BakeSet(_effects, [Selected]).Count - 1 : 0;
        ToolTip.SetTip(_bake, why ?? (UiStrings.S_LayerEffectsDialog_UpdateButtons_ADC8E0220B
                                      + (extra > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_UpdateButtons_DFC3F498B4,extra) : "")));
    }

    /// <summary>효과 하나 또는 사용자 정의 효과 묶음을 추가 범위로 목록 <paramref name="at"/>번째에 넣고 첫 효과를 고른다.</summary>
    private void Add(IReadOnlyList<LayerEffect> templates, int at)
    {
        if (templates.Count == 0) return;
        int n = _project.Frames.Count;
        bool animated = templates.Any(effect => effect is AnimatedEffect);
        var range = !_addRangeTouched && animated ? _motionRange
            : _addAll.On() ? null : FrameRange.Of((int)_addFrom.NumValue() - 1, (int)_addTo.NumValue() - 1, n);
        var added = templates.Select((effect, index) => effect with
        {
            Frames = range,
            ClipToPrevious = index > 0 && effect.ClipToPrevious,
        }).ToArray();
        at = Math.Clamp(at, 0, _effects.Count);
        _effects.InsertRange(at, added);
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
        int clippedFollowers = 0;
        if (!removed.ClipToPrevious)
            while (i + clippedFollowers + 1 < _effects.Count && _effects[i + clippedFollowers + 1].ClipToPrevious)
                clippedFollowers++;
        _effects.RemoveAt(i);
        for (int child = 0; child < clippedFollowers; child++)
            ReplaceEffect(i + child, _effects[i + child] with { ClipToPrevious = false });
        RebuildList(Math.Min(i, _effects.Count - 1));
    }

    private void Move(int delta)
    {
        int i = Selected, j = i + delta;
        if (i < 0 || j < 0 || j >= _effects.Count) return;
        var effect = _effects[i];
        if (!effect.ClipToPrevious) DetachClippedFollowers(i);
        if (j == 0 && effect.ClipToPrevious)
        {
            ReplaceEffect(i, effect with { ClipToPrevious = false });
            effect = _effects[i];
        }
        _effects.RemoveAt(i);
        _effects.Insert(j, effect);
        RebuildList(j);
    }

    private readonly DragReorder _drag = new();

    /// <summary>효과 참조를 새 불변 사본으로 바꾸고, 프레임 늘리기·가상 범위가 그 사본을 따라가게 한다.</summary>
    private void ReplaceEffect(int index, LayerEffect effect)
    {
        var old = _effects[index];
        _effects[index] = effect;
        if (_virtualRangeOverrides.Remove(old, out var virtualRange))
            _virtualRangeOverrides[effect] = virtualRange;
        if (ReferenceEquals(old, _extensionOwner)) _extensionOwner = effect;
        if (ReferenceEquals(old, _preferExistingFor)) _preferExistingFor = effect;
    }

    private void DetachClippedFollowers(int anchor)
    {
        for (int i = anchor + 1; i < _effects.Count && _effects[i].ClipToPrevious; i++)
            ReplaceEffect(i, _effects[i] with { ClipToPrevious = false });
    }

    /// <summary>효과 하나를 바꾸고(목록 글자 갱신) 캔버스·미리보기에 반영한다.</summary>
    private void Update(int index, LayerEffect effect)
    {
        if (index < 0 || index >= _effects.Count || _effects[index] == effect) return;
        ReplaceEffect(index, effect);
        if (_list.Items[index] is ListBoxItem { Content: StackPanel row })
        {
            _syncing = true;
            if (row.Children.OfType<CheckBox>().FirstOrDefault() is { } check) check.IsChecked = effect.Enabled;
            if (row.Children.OfType<TextBlock>().FirstOrDefault() is { } text) text.Text = EffectSummary(effect);
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
        SyncCanvasExpansion();
        RefreshPreview();
    }

    private string EffectSummary(LayerEffect e)
        => Summary(_virtualRangeOverrides.TryGetValue(e, out var range) ? e with { Frames = range } : e)
           + (e.ClipToPrevious ? UiStrings.EffectPreset_ClippedSuffix : "");

    private void RefreshEffectSummary(int index)
    {
        if (index >= 0 && index < _effects.Count
            && _list.Items[index] is ListBoxItem { Content: StackPanel row }
            && row.Children.OfType<TextBlock>().FirstOrDefault() is { } text)
            text.Text = EffectSummary(_effects[index]);
    }

    private static string Summary(LayerEffect e) => (e switch
    {
        OutlineEffect o => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_E7A8C83171,(o.Inside ? UiStrings.S_LayerEffectsDialog_Summary_192DBA1F7C : UiStrings.S_LayerEffectsDialog_Summary_78B20FF1F4),o.Thickness,(o.Diagonal ? UiStrings.S_LayerEffectsDialog_Summary_009BCE5A4C : UiStrings.S_LayerEffectsDialog_Summary_2629FC9A9D),
                               o.ColorMode == OutlineColorMode.Shade ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.EffectOutline_ShadeSummary, o.ShadeAmount.ToString("+0;−0;0", global::System.Globalization.CultureInfo.CurrentCulture)) : o.Color.ToHex())
                             + (o.HideOriginal ? UiStrings.EffectOutline_HideOriginalSummary : ""),
        ShadowEffect s => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_E233F2401C,s.OffsetX,s.OffsetY,s.Color.ToHex()) + (s.Dither != ShadowDither.None ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_FDDC95CFEA,ShadowDithers[(int)s.Dither]) : ""),
        RimLightEffect r => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_0B62ABBEE2,Directions.First(d => d.Dir == r.Direction).Name,r.Size),
        ColorOverlayEffect c => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_F824955A0D,Blends[(int)c.Blend],c.Amount,c.Color.ToHex()),
        ExplodeEffect x => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_DE0B23D063,x.DistanceX,x.DistanceY,Centers[(int)x.Center].Split(' ')[0]) + (x.KeepOriginal ? UiStrings.S_LayerEffectsDialog_Summary_30757AF9AF : ""),
        ShineEffect s => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_F451DAAC0A,s.Angle,s.Width,s.Strength),
        SquashEffect q => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_963828451F,q.WidthAmount,q.HeightAmount,q.Bounces),
        InnerGlowEffect g => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_B8FE86B3A0,g.Size,g.Strength,g.Color.ToHex()),
        PaletteSwapEffect p => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_53B3F1206A,p.Pairs.Count) + (p.Tolerance > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_E77A16477B,p.Tolerance) : ""),
        ColorAdjustEffect c => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_62017EE627,AdjustSummary(c)),
        ReflectionEffect m => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_54368CD4DD,m.Direction switch
        {
            ReflectionDirection.Up => UiStrings.S_LayerEffectsDialog_Summary_99CAC4A07E,
            ReflectionDirection.Left => UiStrings.Reflection_SummaryLeft,
            ReflectionDirection.Right => UiStrings.Reflection_SummaryRight,
            _ => UiStrings.S_LayerEffectsDialog_Summary_F724879C11,
        },m.Gap,m.Height,m.OpacityStart,m.OpacityEnd) + (m.DitherFade ? UiStrings.S_LayerEffectsDialog_Summary_823544BC03 : ""),
        HitFlashEffect hit => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_A9AB403D93,hit.Count,hit.Strength,hit.Color.ToHex()) + (hit.Shape == HitFlashShape.Fade ? UiStrings.S_LayerEffectsDialog_Summary_1BF53EA7E5 : ""),
        BlinkEffect blink => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_0080B38202) + (blink.Timing == BlinkTiming.Frames ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_855D00FD34,blink.OnFrames,blink.OffFrames) : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_7D1229802B,blink.Count))
                             + $" · {BlinkHides[(int)blink.Hide]}",
        ShakeEffect shake => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_92D1EF3203,Axes[(int)shake.Axis],shake.Amount,shake.Count) + (shake.Decay ? UiStrings.S_LayerEffectsDialog_Summary_1D40117C02 : "") + (shake.Regular ? UiStrings.S_LayerEffectsDialog_Summary_4DA5BACD5A : ""),
        BobEffect bob => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_BBAD57A076,(bob.Axis == EffectAxis.Horizontal ? UiStrings.S_LayerEffectsDialog_Summary_3722634F0B : UiStrings.S_LayerEffectsDialog_Summary_EFCA029C51),bob.Height,bob.Cycles),
        AppearEffect appear => $"{appear.DisplayName} · {AppearStyles[(int)appear.Style]}" + (appear.Style == AppearStyle.Wipe ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_C36AC46BAE,WipeFroms[(int)appear.From]) : ""),
        FadeEffect fade => $"{fade.DisplayName} · {(fade.FadeOut ? UiStrings.Fade_Out : UiStrings.Fade_In)}",
        MeltEffect melt => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_DA48E085AF,melt.Distance,melt.Irregularity),
        RotateEffect rotate => string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_LayerEffectsDialog_Summary_4AD4C2E7D2, rotate.Angle, Anchors[(int)rotate.Anchor].Split(' ')[0]),
        AfterimageEffect after => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_4BF321B781,after.Count,after.Interval,after.Opacity) + (after.Tint ? $" · {after.TintColor.ToHex()}" : ""),
        ColorCycleEffect cycle => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_055B04E8E0,cycle.Colors.Count,cycle.Steps) + (cycle.Reverse ? UiStrings.S_LayerEffectsDialog_Summary_D6E8941524 : ""),
        ShockwaveEffect wave => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_D07B4E5446,wave.Radius,wave.Thickness,(wave.Behind ? UiStrings.S_LayerEffectsDialog_Summary_E3B52B00EB : UiStrings.S_LayerEffectsDialog_Summary_BC26FFFDAA)),
        HologramEffect holo => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_8FC3C16840,holo.Color.ToHex(),holo.TintStrength,holo.Opacity) + (holo.Dither ? UiStrings.S_LayerEffectsDialog_Summary_823544BC03 : "")
                               + (holo.GlitchRate > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_7B535BB782,holo.GlitchRate) : ""),
        _ => e.DisplayName,
    }) + (e.Frames is { } f ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Summary_58CF9C9156,f) : "");

    // ───────────── 영구 적용 ─────────────

    /// <summary>레이어에 효과를 영구 적용할 수 있는지와, 안 되면 그 까닭.</summary>
    internal static (bool Ok, string? Why) CanBake(SpriteProject project, Layer layer)
    {
        if (layer.IsGroup) return (false, UiStrings.S_LayerEffectsDialog_CanBake_F0B223C43A);
        if (project.IsLayerLocked(project.IndexOf(layer))) return (false, UiStrings.S_LayerEffectsDialog_CanBake_235F1CD0DC);
        return (true, null);
    }

    /// <summary>
    /// 효과 영구 적용(<see cref="SpriteProject.BakeLayerEffects"/>)한 결과 알림 — "효과 영구 적용: 외곽선 (프레임 1~4)".
    /// </summary>
    internal static string BakeSummary((IReadOnlyList<LayerEffect> Baked, FrameRange? Frames, int Cels, bool Grown) r, Layer layer)
        => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_BakeSummary_5A9A6172BE,string.Join(", ", r.Baked.Select(e => e.DisplayName)))
           + (r.Frames is { } f ? f.From == f.To ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_BakeSummary_E38062A45D,f.From + 1) : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_BakeSummary_1887A237E0,f.From + 1,f.To + 1) : UiStrings.S_LayerEffectsDialog_BakeSummary_5A6EC98D59)
           + string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_BakeSummary_76F41482A1,r.Cels)
           + (r.Grown ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_BakeSummary_E39243C108,layer.PartSize.Width,layer.PartSize.Height) : "")
           + UiStrings.S_LayerEffectsDialog_BakeSummary_49B857E682;

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
            string label = extension is not null && expansion is not null ? UiStrings.S_LayerEffectsDialog_Bake_E014268BBB
                : extension is not null ? UiStrings.S_LayerEffectsDialog_Bake_E079C5BFE1
                : expansion is not null ? UiStrings.S_LayerEffectsDialog_Bake_D0F7807D90 : UiStrings.S_LayerEffectsDialog_Bake_D15392AB9F;
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
        _extendTarget = null;
        _extendFor = -1;
        FillSettings();
        ShowSharedExtendSettings();
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
            _settings.Children.Add(Ui.Label(_effects.Count == 0 ? UiStrings.S_LayerEffectsDialog_FillSettings_24136B66D0 : UiStrings.S_LayerEffectsDialog_FillSettings_12D0C7E74E));
            return;
        }
        var e = _effects[i];
        void Set(Func<LayerEffect, LayerEffect> change) => Update(i, change(_effects[i]));
        AddRangeEditor(i);
        AddClipEditor(i);

        switch (e)
        {
            case OutlineEffect o:
                SettingsRow(UiStrings.EffectOutline_ColorMode, ChoiceEditor([UiStrings.EffectOutline_ModeFixed, UiStrings.EffectOutline_ModeShade], (int)o.ColorMode, k =>
                {
                    Set(x => (OutlineEffect)x with { ColorMode = (OutlineColorMode)k });
                    ShowSettings(); // 그 방식의 색·명암 칸으로
                }).Tip(UiStrings.EffectOutline_ModeTip));
                if (o.ColorMode == OutlineColorMode.Shade)
                    SettingsSubRow(UiStrings.EffectOutline_ShadeAmount, NumEditor(-100, 100, o.ShadeAmount, NumKinds.Adjust, v => Set(x => (OutlineEffect)x with { ShadeAmount = v })).Tip(UiStrings.EffectOutline_ShadeAmountTip), Ui.Label("%"));
                else
                    SettingsSubRow(UiStrings.S_LayerEffectsDialog_FillSettings_F6DB7C3B67, EffectColorEditor(o, nameof(OutlineEffect.Color), ColorEditor(o.Color, c => Set(x => (OutlineEffect)x with { Color = c })), Set));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_A81FAFD1EE, NumEditor(1, OutlineEffect.MaxThickness, o.Thickness, NumKinds.Thickness, v => Set(x => (OutlineEffect)x with { Thickness = v })), Ui.Label("px"));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_7AEF607F6B, ChoiceEditor([UiStrings.S_LayerEffectsDialog_FillSettings_85BBD645E0, UiStrings.S_LayerEffectsDialog_FillSettings_D2ECBA6FD8], o.Diagonal ? 1 : 0, k => Set(x => (OutlineEffect)x with { Diagonal = k == 1 })));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_E78408846D, ChoiceEditor([UiStrings.S_LayerEffectsDialog_FillSettings_62FD728E7C, UiStrings.S_LayerEffectsDialog_FillSettings_4331838592], o.Inside ? 1 : 0, k => Set(x => (OutlineEffect)x with { Inside = k == 1 })));
                SettingsRow("", CheckEditor(UiStrings.EffectOutline_HideOriginal, o.HideOriginal, v => Set(x => (OutlineEffect)x with { HideOriginal = v })).Tip(UiStrings.EffectOutline_HideOriginalTip));
                break;
            case ShadowEffect s:
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_F6DB7C3B67, EffectColorEditor(s, nameof(ShadowEffect.Color), ColorEditor(s.Color, c => Set(x => (ShadowEffect)x with { Color = c })), Set));
                SettingsRow("X",
                    NumEditor(-ShadowEffect.MaxOffset, ShadowEffect.MaxOffset, s.OffsetX, NumKinds.Offset, v => Set(x => (ShadowEffect)x with { OffsetX = v })),
                    Ui.Label("Y"),
                    NumEditor(-ShadowEffect.MaxOffset, ShadowEffect.MaxOffset, s.OffsetY, NumKinds.Offset, v => Set(x => (ShadowEffect)x with { OffsetY = v })),
                    Ui.Label("px"));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_FEA25AE8B2, ChoiceEditor(ShadowDithers, (int)s.Dither, k => Set(x => (ShadowEffect)x with { Dither = (ShadowDither)k })));
                _settings.Children.Add(Hint(UiStrings.S_LayerEffectsDialog_FillSettings_9577438EC7));
                break;
            case RimLightEffect r:
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_66BE69E8AB,
                    ChoiceEditor(Directions.Select(d => d.Name).ToArray(), Array.FindIndex(Directions, d => d.Dir == r.Direction),
                        k => Set(x => (RimLightEffect)x with { Direction = Directions[k].Dir })));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_3CA6157165, NumEditor(1, RimLightEffect.MaxSize, r.Size, NumKinds.Thickness,
                    v => Set(x => (RimLightEffect)x with { Size = v })), Ui.Label("px"));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_1381B5CF89, EffectColorEditor(r, nameof(RimLightEffect.Light), ColorEditor(r.Light, c => Set(x => (RimLightEffect)x with { Light = c })), Set));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_6E13AD4474, EffectColorEditor(r, nameof(RimLightEffect.Shade), ColorEditor(r.Shade, c => Set(x => (RimLightEffect)x with { Shade = c })), Set));
                _settings.Children.Add(Hint(UiStrings.S_LayerEffectsDialog_FillSettings_0AB9179C30));
                break;
            case ColorOverlayEffect c:
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_F6DB7C3B67, EffectColorEditor(c, nameof(ColorOverlayEffect.Color), ColorEditor(c.Color, v => Set(x => (ColorOverlayEffect)x with { Color = v })), Set));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_5522C8DF65, NumEditor(0, 100, c.Amount, NumKinds.Strength, v => Set(x => (ColorOverlayEffect)x with { Amount = v })), Ui.Label("%"));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_5DD23B47AC, ChoiceEditor(Blends, (int)c.Blend, k => Set(x => (ColorOverlayEffect)x with { Blend = (EffectBlend)k })));
                _settings.Children.Add(Hint(UiStrings.S_LayerEffectsDialog_FillSettings_BD67E1B5C4));
                break;
            case ExplodeEffect x:
            {
                ExplodeEffect X(LayerEffect v) => (ExplodeEffect)v;
                var distanceY = NumEditor(0, ExplodeEffect.MaxDistance, x.DistanceY, NumKinds.Distance, v => Set(o => X(o) with { DistanceY = v }));
                ToolTip.SetTip(distanceY, UiStrings.Explode_DistanceYTip);
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_98D374602B,
                    Ui.Label("X"), NumEditor(0, ExplodeEffect.MaxDistance, x.DistanceX, NumKinds.Distance, v => Set(o => X(o) with { DistanceX = v })), Ui.Label("px"),
                    Ui.Label("Y"), distanceY, Ui.Label("px"));
                var cx = NumEditor(-PixelImage.MaxSize, PixelImage.MaxSize, x.CenterX, CenterKind(vertical: false), v => Set(o => X(o) with { CenterX = v }));
                var cy = NumEditor(-PixelImage.MaxSize, PixelImage.MaxSize, x.CenterY, CenterKind(vertical: true), v => Set(o => X(o) with { CenterY = v }));
                cx.IsEnabled = cy.IsEnabled = x.Center == ExplodeCenter.Custom;
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_5C5C39442A, ChoiceEditor(Centers, (int)x.Center, k =>
                {
                    cx.IsEnabled = cy.IsEnabled = (ExplodeCenter)k == ExplodeCenter.Custom;
                    Set(o => X(o) with { Center = (ExplodeCenter)k });
                }));
                SettingsSubRow("X", cx, Ui.Label("Y"), cy, Ui.Label(UiStrings.S_LayerEffectsDialog_FillSettings_A89FB839CA));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_9E6E8E6EFE,
                    Ui.Label("X"), NumEditor(0, 100, x.SpreadX, NumKinds.Random, v => Set(o => X(o) with { SpreadX = v })), Ui.Label("%"),
                    Ui.Label("Y"), NumEditor(0, 100, x.SpreadY, NumKinds.Random, v => Set(o => X(o) with { SpreadY = v })), Ui.Label("%"));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_1C82F0E839,
                    NumEditor(-ExplodeEffect.MaxGravity, ExplodeEffect.MaxGravity, x.Gravity, NumKinds.Gravity, v => Set(o => X(o) with { Gravity = v })), Ui.Label("px"));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_E1CC351651, NumEditor(1, ExplodeEffect.MaxPieceSize, x.PieceSize, NumKinds.Piece, v => Set(o => X(o) with { PieceSize = v })), Ui.Label("px"),
                    CheckEditor(UiStrings.S_LayerEffectsDialog_FillSettings_880B57A4CE, x.Shrink, v => Set(o => X(o) with { Shrink = v })));
                var fadeStart = NumEditor(0, 100, x.FadeStart, NumKinds.Progress, v => Set(o => X(o) with { FadeStart = v }));
                fadeStart.IsEnabled = x.Fade;
                SettingsCheckRow(CheckEditor(UiStrings.S_LayerEffectsDialog_FillSettings_08EF532DFB, x.Fade, v => { fadeStart.IsEnabled = v; Set(o => X(o) with { Fade = v }); }).Tip(UiStrings.S_LayerEffectsDialog_FillSettings_E831CF80BB),
                    Ui.Label(UiStrings.S_LayerEffectsDialog_FillSettings_E8E9684F4B), fadeStart, Ui.Label("%"));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_288AED0ECC, NumEditor(0, ExplodeEffect.MaxFlashFrames, x.FlashFrames, NumKinds.Frames, v => Set(o => X(o) with { FlashFrames = v })), Ui.Label(UiStrings.S_LayerEffectsDialog_FillSettings_5F8BA1F369));
                SettingsSubRow(UiStrings.S_LayerEffectsDialog_FillSettings_6D8B67BF59, EffectColorEditor(x, nameof(ExplodeEffect.FlashColor), ColorEditor(x.FlashColor, v => Set(o => X(o) with { FlashColor = v })), Set));
                SettingsRow("", CheckEditor(UiStrings.S_LayerEffectsDialog_FillSettings_7049E2D1FE, x.KeepOriginal, v => Set(o => X(o) with { KeepOriginal = v })));
                var seed = NumEditor(0, ExplodeEffect.MaxSeed, x.Seed, NumKinds.Seed, v => Set(o => X(o) with { Seed = v }));
                var reroll = Ui.IconButton(VectorIcon.Dice, null, (_, _) => seed.Value = System.Random.Shared.Next(0, ExplodeEffect.MaxSeed + 1), 36);
                ToolTip.SetTip(reroll, UiStrings.S_LayerEffectsDialog_FillSettings_BE76D99E55);
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_E37116317A, seed, reroll);
                AddMotionEditors(i, Set);
                _settings.Children.Add(Hint(UiStrings.S_LayerEffectsDialog_FillSettings_171EBAB542
                                            + UiStrings.S_LayerEffectsDialog_FillSettings_CD2E150E76));
                break;
            }
            case ShineEffect s:
            {
                ShineEffect S(LayerEffect v) => (ShineEffect)v;
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_1192DC8149, NumEditor(0, 359, s.Angle, NumKinds.Angle, v => Set(o => S(o) with { Angle = v })), Ui.Label(UiStrings.S_LayerEffectsDialog_FillSettings_C4DC26AC14));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_D370965ED0, NumEditor(1, ShineEffect.MaxWidth, s.Width, NumKinds.Px, v => Set(o => S(o) with { Width = v })), Ui.Label("px"),
                    Ui.Label(UiStrings.S_LayerEffectsDialog_FillSettings_6C80368CF6), NumEditor(0, 100, s.Strength, NumKinds.Strength, v => Set(o => S(o) with { Strength = v })), Ui.Label("%"));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_F6DB7C3B67, EffectColorEditor(s, nameof(ShineEffect.Color), ColorEditor(s.Color, v => Set(o => S(o) with { Color = v })), Set));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_48FAB42CF8, ChoiceEditor([UiStrings.S_LayerEffectsDialog_FillSettings_28DDBC6B0B, UiStrings.S_LayerEffectsDialog_FillSettings_AB233EA745, UiStrings.S_LayerEffectsDialog_FillSettings_541ACE652F], s.Softness, k => Set(o => S(o) with { Softness = k })));
                AddMotionEditors(i, Set);
                _settings.Children.Add(Hint(UiStrings.S_LayerEffectsDialog_FillSettings_2118984CA5));
                break;
            }
            case SquashEffect q:
            {
                SquashEffect Q(LayerEffect v) => (SquashEffect)v;
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_3029688617, NumEditor(0, SquashEffect.MaxAmount, q.WidthAmount, NumKinds.Squash, v => Set(o => Q(o) with { WidthAmount = v })), Ui.Label("%"),
                    Ui.Label(UiStrings.S_LayerEffectsDialog_FillSettings_DA003AD5F5), NumEditor(0, SquashEffect.MaxAmount, q.HeightAmount, NumKinds.Squash, v => Set(o => Q(o) with { HeightAmount = v })), Ui.Label("%"));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_7D4825E02A, NumEditor(1, SquashEffect.MaxBounces, q.Bounces, NumKinds.Count, v => Set(o => Q(o) with { Bounces = v })), Ui.Label(UiStrings.S_LayerEffectsDialog_FillSettings_9926E8F548));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_4165F7C464, ChoiceEditor(Anchors, (int)q.Anchor, k => Set(o => Q(o) with { Anchor = (SquashAnchor)k })));
                SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_4D51C71D99, ChoiceEditor([UiStrings.S_LayerEffectsDialog_FillSettings_4147FCFCA9, UiStrings.S_LayerEffectsDialog_FillSettings_B8F02D5B5D], q.StretchFirst ? 1 : 0, k => Set(o => Q(o) with { StretchFirst = k == 1 })));
                AddMotionEditors(i, Set);
                _settings.Children.Add(Hint(UiStrings.S_LayerEffectsDialog_FillSettings_9691881EA3));
                break;
            }
            case InnerGlowEffect g: InnerGlowPanel(g, Set); break;
            case PaletteSwapEffect p: PaletteSwapPanel(p, Set); break;
            case ColorAdjustEffect c: ColorAdjustPanel(c, Set); break;
            case ReflectionEffect m: ReflectionPanel(m, Set); break;
            case HitFlashEffect hit: HitFlashPanel(hit, Set); break;
            case BlinkEffect blink: BlinkPanel(blink, Set); break;
            case ShakeEffect shake: ShakePanel(shake, Set); break;
            case BobEffect bob: BobPanel(bob, Set); break;
            case AppearEffect appear: AppearPanel(appear, Set); break;
            case FadeEffect fade:
                SettingsRow(UiStrings.Fade_Direction, ChoiceEditor([UiStrings.Fade_In, UiStrings.Fade_Out], fade.FadeOut ? 1 : 0,
                    k => Set(x => (FadeEffect)x with { FadeOut = k == 1 })));
                AddMotionEditors(i, Set);
                break;
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
        _settings.Children.Add(Hint(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_AddMotionEditors_610C3B2627,to - from + 1,from + 1,to + 1)
                                    + (from == to ? UiStrings.S_LayerEffectsDialog_AddMotionEditors_9BD880B938 : "")));
    }

    private void AddTimingEditors(AnimatedEffect a, Action<Func<LayerEffect, LayerEffect>> set)
    {
        SettingsRow(UiStrings.S_LayerEffectsDialog_AddTimingEditors_E1B19358AF, ChoiceEditor(Easings, (int)a.Easing, k => set(o => (AnimatedEffect)o with { Easing = (EffectEasing)k })));
        var count = NumEditor(1, AnimatedEffect.MaxRepeatCount, a.RepeatCount, NumKinds.Repeat, v => set(o => (AnimatedEffect)o with { RepeatCount = v }));
        count.IsEnabled = a.Repeat != EffectRepeat.Once;
        SettingsRow(UiStrings.S_LayerEffectsDialog_AddTimingEditors_E8E7F0E20B, ChoiceEditor(Repeats, (int)a.Repeat, k =>
        {
            count.IsEnabled = (EffectRepeat)k != EffectRepeat.Once;
            set(o => (AnimatedEffect)o with { Repeat = (EffectRepeat)k });
        }), count, Ui.Label(UiStrings.S_LayerEffectsDialog_AddTimingEditors_433F81EB07));
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
        var all = Ui.Check(UiStrings.S_LayerEffectsDialog_AddRangeEditor_1629B10BDB, range is null);
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
        _rangeRow = SettingsRow(UiStrings.S_LayerEffectsDialog_AddRangeEditor_C773F4F9DC, all, from, Ui.Label("~"), to);
    }

    /// <summary>고른 효과를 앞의 비클리핑 효과가 만든 영역에만 적용한다.</summary>
    private void AddClipEditor(int i)
    {
        var effect = _effects[i];
        int anchor = i - 1;
        while (anchor >= 0 && _effects[anchor].ClipToPrevious) anchor--;
        bool canClip = anchor >= 0 && _effects[anchor] is not AfterimageEffect;
        var clip = Ui.Check(UiStrings.EffectPreset_ClipToPrevious, effect.ClipToPrevious);
        clip.IsEnabled = canClip;
        ToolTip.SetTip(clip, canClip ? UiStrings.EffectPreset_ClipTip : UiStrings.EffectPreset_ClipUnavailableTip);
        clip.IsCheckedChanged += (_, _) =>
        {
            if (Selected != i || !canClip || _effects[i].ClipToPrevious == clip.On()) return;
            ReplaceEffect(i, _effects[i] with { ClipToPrevious = clip.On() });
            RebuildList(i);
        };
        SettingsRow(UiStrings.EffectPreset_ApplyTarget, clip);
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
        var field = new ColorField(value);
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
            if ((end ? t.To : t.From) + 1 is var f && f >= 1 && f <= n) names.TryAdd(f, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_FrameNumberKind_D50B47CDF3,t.Name,(end ? UiStrings.S_LayerEffectsDialog_FrameNumberKind_FC90F137FF : UiStrings.S_LayerEffectsDialog_FrameNumberKind_5E664EA7C3)));
        names[now] = names.TryGetValue(now, out var tag) ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_FrameNumberKind_F07F8BC693,tag) : UiStrings.S_LayerEffectsDialog_FrameNumberKind_36C0AD1206;
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
        string edge = vertical ? UiStrings.S_LayerEffectsDialog_CenterKind_7529E535E3 : UiStrings.S_LayerEffectsDialog_CenterKind_DE4F2D8B9E;
        return new(vertical ? "effect.center.y" : "effect.center.x", "px", [.. values],
            v => v == mid ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_CenterKind_6FC4EF0158,v) : v == last && v > 0 ? $"{v} px ({edge})" : $"{v} px");
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
        if (_largePlay is { } largePlay) largePlay.IsEnabled = _play.IsEnabled;
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
        UpdatePlayButtons();
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
        UpdatePlayButtons();
    }

    /// <summary>창과 크게 보기 창의 재생 단추 글을 재생 상태에 맞춘다.</summary>
    private void UpdatePlayButtons()
    {
        foreach (var button in new[] { _play, _largePlay })
            if (button is not null)
                button.Content = _timer.IsEnabled ? Ui.IconLabel(VectorIcon.Pause, UiStrings.S_LayerEffectsDialog_SyncPlayback_61EB6DFDA0) : Ui.IconLabel(VectorIcon.Play, UiStrings.S_LayerEffectsDialog_SyncPlayback_1100994439);
    }

    /// <summary>재생을 멈추고 한 프레임 앞(+1)이나 뒤(−1)로 옮긴다(재생 범위 안에서 돌아감, 범위가 없으면 모든 프레임).</summary>
    private void StepPreview(int delta)
    {
        _timer.Stop();
        var (from, to) = PlayRange() ?? (0, VirtualCount - 1);
        if (to < from) return;
        int f = _previewFrame < from || _previewFrame > to ? from : _previewFrame + delta;
        _previewFrame = f > to ? from : f < from ? to : f;
        RefreshPreview();
        UpdatePlayButtons();
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
    /// 전·후를 모든 프레임에 공통인 확인 후 캔버스 자리와 크기로 그린다.
    /// 확장을 끈 경우 캔버스 밖 효과 픽셀은 흐리게 표시한다.
    /// </summary>
    private void RefreshPreview()
    {
        int f = _previewFrame;
        _strip.Current = f;
        var all = VirtualEffects();
        var (source, effects) = AnimateAt(f, all);
        string where = _layer.IsGroup ? UiStrings.S_LayerEffectsDialog_RefreshPreview_BDAFE80FE8 : _layer.IsPart ? UiStrings.S_LayerEffectsDialog_RefreshPreview_BD6BE5C32E : "";
        if (_extension is not null)
            where = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_RefreshPreview_6A782EF4D8,VirtualCount,where,OriginalOf(f) + 1,(IsCopyFrame(f) ? UiStrings.S_LayerEffectsDialog_RefreshPreview_E18A9F9C35 : ""));
        var bounds = _previewBounds;
        var canvas = new Rectangle(_previewCanvas.X - bounds.X, _previewCanvas.Y - bounds.Y, _previewCanvas.Width, _previewCanvas.Height);
        if (source is null)
        {
            _caption.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_RefreshPreview_A0B1282CEF,f + 1,where);
            var empty = new PixelImage(bounds.Width, bounds.Height);
            _preview.SetImages(empty, empty, canvas);
            _largePreview?.InvalidateVisual();
            if (_largeCaption is not null) _largeCaption.Text = _caption.Text;
            return;
        }
        var (after, dx, dy) = LayerEffects.Render(source, effects, LayerEffects.Padding(all, source.Width, source.Height));
        int ox = _layer.IsPart ? _layer.PartOffset.X : 0;
        int oy = _layer.IsPart ? _layer.PartOffset.Y : 0;
        PixelImage before;
        if (bounds.X == ox && bounds.Y == oy && bounds.Width == source.Width && bounds.Height == source.Height)
            before = source;
        else
        {
            before = new PixelImage(bounds.Width, bounds.Height);
            before.Blit(source, new Rectangle(bounds.X - ox, bounds.Y - oy, bounds.Width, bounds.Height), 0, 0);
        }
        var afterRegion = new Rectangle(bounds.X - ox - dx, bounds.Y - oy - dy, bounds.Width, bounds.Height);
        var croppedAfter = afterRegion == after.Bounds ? after : after.Crop(afterRegion);
        _preview.SetImages(before, croppedAfter, canvas);
        _largePreview?.InvalidateVisual();
        string progress = Selected >= 0 && Selected < all.Count && all[Selected] is AnimatedEffect a && a.AppliesTo(f)
            ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_RefreshPreview_19220A5382,a.AtFrame(f, VirtualCount, _project.EffectPivot(_layer, OriginalOf(f))).Time * 100) : "";
        _caption.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_RefreshPreview_64659703DD,f + 1,where,progress);
        if (_largeCaption is not null) _largeCaption.Text = _caption.Text;
    }

    private void OpenLargePreview()
    {
        if (_previewWindow is { } window)
        {
            window.Activate();
            return;
        }
        var view = new PreviewView(_preview) { Margin = new Thickness(12, 12, 12, 4) };
        // 아래 줄: 재생·이전·다음 프레임, 지금 프레임 설명, 재생 범위 체크 두 개(창의 체크와 같이 바뀜).
        var play = Ui.Button(VectorIcon.Play, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_3DC0585476, (_, _) => TogglePlay(), 80);
        ToolTip.SetTip(play, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_46C6465E13);
        var prev = Ui.IconButton(VectorIcon.ChevronLeft, UiStrings.EffectPreview_PrevFrame, (_, _) => StepPreview(-1));
        var next = Ui.IconButton(VectorIcon.ChevronRight, UiStrings.EffectPreview_NextFrame, (_, _) => StepPreview(1));
        var effectOnly = Ui.Check(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_BEC5ADB6D1, _effectOnly.On());
        effectOnly.VerticalAlignment = VerticalAlignment.Center;
        effectOnly.IsCheckedChanged += (_, _) => _effectOnly.IsChecked = effectOnly.On();
        ToolTip.SetTip(effectOnly, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_2138B1E743);
        var loop = Ui.Check(UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_96BC2CE00E, _loop.On());
        loop.VerticalAlignment = VerticalAlignment.Center;
        loop.IsCheckedChanged += (_, _) => _loop.IsChecked = loop.On();
        ToolTip.SetTip(loop, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_0500474F59);
        var caption = Ui.Label(_caption.Text ?? "");
        caption.Foreground = AppTheme.DimTextBrush;
        caption.VerticalAlignment = VerticalAlignment.Center;
        var bar = Ui.Row(play, prev, next, effectOnly, loop, caption);
        bar.Margin = new Thickness(12, 0, 12, 10);
        DockPanel.SetDock(bar, Dock.Bottom);
        var large = new Window
        {
            Title = Title,
            Width = 900,
            Height = 620,
            MinWidth = 520,
            MinHeight = 300,
            CanResize = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = AppTheme.BackBrush,
            Content = new DockPanel { Children = { bar, view } },
        };
        large.KeyDown += (_, e) =>
        {
            // 단추·체크에 포커스가 있으면 Space는 그 단추를 누른다 — 창에서 또 재생을 바꾸지 않는다.
            if (e.Key == Key.Space && e.Source is Avalonia.Controls.Primitives.ToggleButton or Button) return;
            switch (e.Key)
            {
                case Key.Escape: large.Close(); break;
                case Key.Space: TogglePlay(); break;
                case Key.Left: StepPreview(-1); break;
                case Key.Right: StepPreview(1); break;
                default: return;
            }
            e.Handled = true;
        };
        large.Closed += (_, _) =>
        {
            _previewWindow = null;
            _largePreview = null;
            _largePlay = null;
            _largeCaption = null;
            _largeEffectOnly = null;
            _largeLoop = null;
        };
        _previewWindow = large;
        _largePreview = view;
        _largePlay = play;
        _largeCaption = caption;
        _largeEffectOnly = effectOnly;
        _largeLoop = loop;
        play.IsEnabled = _play.IsEnabled;
        UpdatePlayButtons();
        large.Show(this);
    }

    /// <summary>전·후를 같은 캔버스 위치와 배율로 표시하고, 잘려 나가는 픽셀은 흐리게 표시한다.</summary>
    private sealed class PreviewView : Control, IDisposable
    {
        private readonly PreviewView? _source;
        private WriteableBitmap? _before, _after;
        private Rectangle _canvas;

        public PreviewView() { }
        public PreviewView(PreviewView source) => _source = source;

        public bool HasImages => (_source ?? this)._before is not null;

        public void SetImages(PixelImage? before, PixelImage? after, Rectangle canvas)
        {
            Dispose();
            _canvas = canvas;
            if (before is not null && after is not null)
            {
                try
                {
                    _before = ImageUtil.ToBitmap(before);
                    _after = ImageUtil.ToBitmap(after);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            var b = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.BackBrush, b);
            var data = _source ?? this;
            if (data._before is not { } before || data._after is not { } after)
            {
                AppTheme.DrawTextIn(g, UiStrings.S_LayerEffectsDialog_Render_BF9D824663, b, AppTheme.DimTextBrush, center: true);
                return;
            }
            double half = (b.Width - 12) / 2, top = 18;
            var src = new Rectangle(0, 0, before.PixelSize.Width, before.PixelSize.Height);
            AppTheme.DrawTextIn(g, UiStrings.S_LayerEffectsDialog_Render_71A8D08947, new Rect(0, 0, half, 16), AppTheme.DimTextBrush, AppTheme.SmallFontSize, center: true);
            AppTheme.DrawTextIn(g, UiStrings.S_LayerEffectsDialog_Render_894FF3899A, new Rect(half + 12, 0, half, 16), AppTheme.DimTextBrush, AppTheme.SmallFontSize, center: true);
            var area = new Rect(0, top, half, b.Height - top);
            double fit = Math.Min(area.Width / src.Width, area.Height / src.Height);
            int scale = fit >= 1 ? (int)Math.Floor(fit) : 0;
            DrawImage(g, before, src, area, scale, data._canvas);
            DrawImage(g, after, src, new Rect(half + 12, top, half, b.Height - top), scale, data._canvas);
        }

        private static void DrawImage(DrawingContext g, WriteableBitmap bitmap, Rectangle src, Rect area, int scale, Rectangle canvas)
        {
            // The faint spill shows where the effect continues beyond the actual project canvas.
            Rect dest;
            using (g.PushOpacity(0.25))
                dest = ImageUtil.DrawFitted(g, bitmap, src, area, scale);
            // DrawFitted returns the exact pixel-snapped destination, including downscaled images.
            var canvasRect = new Rect(dest.X + canvas.X * dest.Width / src.Width, dest.Y + canvas.Y * dest.Height / src.Height,
                canvas.Width * dest.Width / src.Width, canvas.Height * dest.Height / src.Height);
            using (g.PushClip(canvasRect))
                ImageUtil.DrawFitted(g, bitmap, src, area, scale, checker: true);
            using (g.PushClip(dest))
                g.DrawRectangle(null, AppTheme.Pen(AppTheme.BorderColor), canvasRect);
        }

        public void Dispose()
        {
            _before?.Dispose();
            _after?.Dispose();
            _before = _after = null;
        }
    }
}
