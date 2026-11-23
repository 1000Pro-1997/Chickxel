using Avalonia.Platform.Storage;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scene3D;

namespace Chickxel.App;

/// <summary>
/// 3D 모델 가져오기 설정(그려서 프레임으로 — <see cref="ModelFrames"/>): 왼쪽 미리보기(나올 크기 그대로 그려 크게 보임, 끌기 = 돌리기, 휠 = 확대, 아래 프레임 막대·재생),
/// 오른쪽 위 프리셋(기본 제공 + 저장한 것 — 고르면 아래 값을 모두 바꿈, 지금 값 저장·지우기), 크기·보는 각도·보기 방식·빛·명암 단계·가장자리·텍스처,
/// 픽셀 다듬기(반투명 없애기·색 수)·외곽선(겉·안쪽 겹침·각진 곳, 두께·색), 프레임(한 장 / 한 바퀴 돌려 보기 / 애니메이션).
/// 고친 설정은 앱을 끌 때까지 다음 모델의 처음 값이 된다.
/// 모델 없이 열 수도 있다(파일 > 3D 모델 열기…): 모델 파일을 창에 끌어 놓거나 '모델 열기…'로 고르면 읽어 들이고, 그 전에는 가져오기 단추가 꺼져 있다.
/// 이미 모델이 있어도 다른 모델을 끌어 놓거나 골라 바꿀 수 있다(설정은 그대로).
/// </summary>
public sealed class ModelImportDialog : DialogBase
{
    /// <summary>마지막으로 쓴 설정(다음에 여는 모델의 처음 값 — 애니메이션 번호는 빼고). 처음은 기본 프리셋(<see cref="ModelPresets.Standard"/>).</summary>
    private static ModelRenderSettings _lastRender = ModelPresets.Standard.Render;
    private static ModelFrameOptions _lastFrames = ModelPresets.Standard.Frames;

    /// <summary>사용자 프리셋 파일(설정 파일과 같은 폴더).</summary>
    public static string PresetPath => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, ModelPresets.FileName);

    /// <summary>그릴 장면(애니메이션을 불러오면 그 애니메이션을 붙인 새 장면으로 바뀜 — <see cref="Scene"/>). 아직 모델을 열지 않았으면 null.</summary>
    private ModelScene? _scene;
    private string _fileName;
    /// <summary>애니메이션 불러오기·모델 열기 창의 처음 폴더(모델 파일의 폴더).</summary>
    private string? _folder;
    private readonly TextBlock _summary;
    /// <summary>모델을 읽을 때 난 주의 사항(없으면 숨김).</summary>
    private readonly TextBlock _warnings;
    /// <summary>미리보기 위 안내: 모델이 없으면 끌어 놓기·열기 안내, 끌어 오는 중이면 강조.</summary>
    private readonly Border _dropHint;
    private readonly Button _importAnim;
    private readonly NumericUpDown _yaw, _pitch, _roll, _zoom, _fov, _lightYaw, _lightPitch, _ambient, _light, _steps, _turnFrames, _fps, _maxFrames;
    private readonly SizeField _size;
    private readonly NumericUpDown _colors, _crease, _outlineWidth, _darken;
    private readonly ComboBox _projection, _smooth, _mode, _clip, _outlineColorMode, _preset;
    private readonly CheckBox _textures, _hardAlpha, _outer, _outside, _inner, _creaseOn;
    /// <summary>돌려 보기 옆 '자유 회전': 켜면 미리보기 막대가 프레임 대신 0~359° 회전(1°씩) — 가져오는 프레임은 그대로.</summary>
    private readonly CheckBox _freeTurn;
    /// <summary>자유 회전 중 막대 옆 각도.</summary>
    private readonly TextBlock _turnAngle = Ui.Label("");
    /// <summary>자유 회전을 재생할 때 1°씩 넘기는 간격.</summary>
    private const int FreeTurnTickMs = 30;
    /// <summary>빛 앞뒤(정면에서 잰 각도: 0° = 카메라 쪽 정면, 90° = 옆, 180° = 뒤) — 빛 가로·세로 값과 뒤쪽 여부를 함께 바꾼다.</summary>
    private readonly Slider _lightDepth;
    private readonly TextBlock _lightDepthValue;
    /// <summary>빛이 카메라 뒤쪽 반구에 있는지(<see cref="ModelRenderSettings.LightBack"/>).</summary>
    private bool _lightBehind;
    /// <summary>앞뒤를 바꿀 때 지킬 화면 안 빛 방향(라디안, 오른쪽 = 0 · 위 = π/2). 빛이 정면·정뒤라 방향이 없을 때 쓰려고 마지막 값을 둔다.</summary>
    private double _lightTurn = Math.PI / 2;
    private readonly Button _outlineColor, _deletePreset;
    private Color32 _outlineColorValue;
    private List<ModelPreset> _userPresets = [];
    private readonly ModelPreview _preview;
    private readonly Slider _frame;
    private readonly Button _play;
    private readonly TextBlock _estimate;
    private readonly DispatcherTimer _player;
    /// <summary>미리보기를 그리는 중 / 그리는 동안 값이 또 바뀌어 끝나면 다시 그려야 함 / 다시 그리기를 이미 부탁해 둠(같은 틱의 변경을 한 번으로).</summary>
    private bool _rendering, _renderAgain, _renderQueued;
    /// <summary>픽셀 결과 칸 아래: 지금 프레임에 쓴 색 수와 색 칸(많이 쓴 색부터).</summary>
    private readonly TextBlock _usedLabel = Ui.Label("");
    private readonly ColorSwatches _usedColors = new() { Width = 332 };
    private readonly StackPanel _usedRow;
    /// <summary>색 칸에 보일 최대 수(넘으면 앞쪽만 — 수는 그대로 다 셈).</summary>
    private const int MaxShownColors = 256;
    private CancellationTokenSource? _renderCts;
    /// <summary>지금 모델·프레임 설정의 자세와 맞춤 범위(모델이나 프레임 설정이 바뀔 때만 다시 — 그리기 설정과는 상관없음).</summary>
    private (ModelScene Scene, ModelFrameOptions Options, IReadOnlyList<ModelFrame> Frames, PosedMesh[] Poses, ModelBounds Fit)? _posed;
    /// <summary>값을 한꺼번에 바꾸는 중(프리셋 불러오기·맞추기) — 칸마다 다시 그리지 않게.</summary>
    private bool _syncing;

    /// <summary>가장자리 다듬기 단계(콤보 순서): 끔 = 1, 2×2, 4×4, 8×8 표본.</summary>
    private static readonly int[] SmoothLevels = [1, 2, 4, 8];

    /// <param name="path">모델 파일 경로(<paramref name="scene"/>을 읽어 온 파일, 모델 없이 열면 null).</param>
    /// <param name="scene">읽은 장면(null이면 모델 없이 열고 끌어 놓기·'모델 열기…'를 기다림).</param>
    /// <param name="folder">모델이 없을 때 '모델 열기…' 창의 처음 폴더.</param>
    public ModelImportDialog(string? path, ModelScene? scene, string? folder = null) : base(UiStrings.Model_Title, UiStrings.Model_Import)
    {
        _scene = scene;
        ModelPath = path;
        _fileName = path is null ? "" : Path.GetFileName(path);
        _folder = path is null ? folder : Path.GetDirectoryName(path);
        _size = new SizeField(1, 2048, 128, 128, 70).Presets(Ui.SpriteSizes).RecentKey("model.size");
        _yaw = Ui.Num(-360, 360, 0, 70, decimals: 1, increment: 15).Unit("°");
        _pitch = Ui.Num(-90, 90, 0, 70, decimals: 1, increment: 5).Unit("°");
        _roll = Ui.Num(-180, 180, 0, 70, decimals: 1, increment: 15).Unit("°");
        _projection = Ui.Combo(120, UiStrings.Model_Orthographic, UiStrings.Model_Perspective);
        _fov = Ui.Num(5, 120, 35, 60, increment: 5).Unit("°").Tip(UiStrings.Model_FovTip);
        _zoom = Ui.Num(10, 1000, 100, 70, increment: 10).Unit("%").Tip(UiStrings.Model_ZoomTip);
        _lightYaw = Ui.Num(-90, 90, 0, 70, increment: 15).Unit("°");
        _lightPitch = Ui.Num(-90, 90, 0, 70, increment: 15).Unit("°");
        _lightDepth = new Slider { Minimum = 0, Maximum = 180, Width = 180, VerticalAlignment = VerticalAlignment.Center };
        _lightDepth.Tip(UiStrings.Model_LightDepthTip);
        _lightDepthValue = Ui.Label("");
        _lightDepthValue.MinWidth = 40;
        _ambient = Ui.Num(0, 100, 40, 60, increment: 5).Unit("%");
        _light = Ui.Num(0, 200, 80, 60, increment: 10).Unit("%");
        _steps = Ui.Num(0, 16, 0, 60).Tip(UiStrings.Model_StepsTip);
        _smooth = Ui.Combo(120, UiStrings.Model_SmoothOff, "2×2", "4×4", "8×8").Tip(UiStrings.Model_SmoothTip);
        _textures = Ui.Check(UiStrings.Model_Textures, true);

        _hardAlpha = Ui.Check(UiStrings.Model_HardAlpha).Tip(UiStrings.Model_HardAlphaTip);
        _colors = Ui.Num(0, 64, 0, 60).Presets(0, 4, 8, 12, 16, 24, 32).Tip(UiStrings.Model_ColorsTip);
        _outer = Ui.Check(UiStrings.Model_OutlineOuter).Tip(UiStrings.Model_OutlineOuterTip);
        _outside = Ui.Check(UiStrings.Model_OutlineOutside, true).Tip(UiStrings.Model_OutlineOutsideTip);
        _inner = Ui.Check(UiStrings.Model_OutlineInner).Tip(UiStrings.Model_OutlineInnerTip);
        _creaseOn = Ui.Check(UiStrings.Model_OutlineCrease).Tip(UiStrings.Model_OutlineCreaseTip);
        _crease = Ui.Num(1, 179, 60, 60, increment: 5).Unit("°").Tip(UiStrings.Model_OutlineCreaseTip);
        _outlineWidth = Ui.Num(1, 3, 1, 50).Unit("px");
        _outlineColorMode = Ui.Combo(165, UiStrings.Model_OutlineFixed, UiStrings.Model_OutlineDarken).Tip(UiStrings.Model_OutlineColorTip);
        _outlineColor = Ui.Button("", (_, _) => PickOutlineColor()).Tip(UiStrings.Model_OutlineColorPick);
        _darken = Ui.Num(0, 100, 50, 60, increment: 5).Unit("%").Tip(UiStrings.Model_OutlineDarkenTip);

        // 애니메이션이 없으면 그 방식은 목록에 넣지 않는다(한 장·돌려 보기만 — 애니메이션을 불러오면 더함, FillClips).
        _mode = Ui.Combo(170, UiStrings.Model_ModeStill, UiStrings.Model_ModeTurntable);
        _turnFrames = Ui.Num(1, ModelFrameOptions.MaxTurntableFrames, 8, 60).Presets(4, 8, 16, 32).Tip(UiStrings.Model_TurntableTip);
        _freeTurn = Ui.Check(UiStrings.Model_FreeTurn).Tip(UiStrings.Model_FreeTurnTip);
        _clip = Ui.Combo(200);
        _importAnim = Ui.Button(UiStrings.Model_ImportAnimation, (_, _) => Dispatcher.UIThread.Post(ImportAnimations)).Tip(UiStrings.Model_ImportAnimationTip);
        FillClips(0);
        _fps = Ui.Num((decimal)ModelFrameOptions.MinFps, (decimal)ModelFrameOptions.MaxFps, 12, 60, decimals: 1).Presets(6, 8, 10, 12, 15, 24, 30);
        _maxFrames = Ui.Num(1, ModelFrameOptions.MaxFramesLimit, 120, 70).Presets(16, 32, 60, 120, 240);

        _preset = new ComboBox { Width = 230, Margin = new Thickness(3), PlaceholderText = UiStrings.Model_PresetPick };
        _preset.Tip(UiStrings.Model_PresetTip);
        var savePreset = Ui.Button(UiStrings.Model_PresetSave, (_, _) => Dispatcher.UIThread.Post(SavePreset)).Tip(UiStrings.Model_PresetSaveTip);
        _deletePreset = Ui.Button(UiStrings.Model_PresetDelete, (_, _) => DeletePreset()).Tip(UiStrings.Model_PresetDeleteTip);

        _preview = new ModelPreview { Width = 664, Height = 330 };
        _dropHint = new Border
        {
            Background = AppTheme.Brush(Color.FromArgb(200, 32, 32, 36)),
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new IconView(VectorIcon.Cube, AppTheme.DimTextBrush) { Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock
                    {
                        Text = UiStrings.Model_DropHint, Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center, MaxWidth = 420,
                    },
                },
            },
        };
        // 끄는 쪽으로 모델이 돈다: 오른쪽으로 끌면 모델 앞이 오른쪽으로(카메라 가로 각도는 줄어듦), 아래로 끌면 앞이 아래로(위에서 보게)
        _preview.Rotated += (dx, dy) =>
        {
            _yaw.Value = Wrap((double)_yaw.NumValue() - dx * 0.5);
            _pitch.Value = (decimal)Math.Clamp((double)_pitch.NumValue() + dy * 0.5, -90, 90);
        };
        _preview.Rolled += degrees => _roll.Value = Wrap((double)_roll.NumValue() + degrees);
        _preview.Faced += (yaw, pitch) =>
        {
            _yaw.Value = (decimal)yaw;
            _pitch.Value = (decimal)pitch;
            _roll.Value = 0;
        };
        _preview.LightMoved += (yaw, pitch) =>
        {
            _lightYaw.Value = (decimal)Math.Round(yaw, 1);
            _lightPitch.Value = (decimal)Math.Round(pitch, 1);
        };
        // 끄는 동안은 원본 칸을 가볍게 그리고, 놓으면 제대로 다시 그린다
        _preview.DragEnded += Schedule;
        _frame = new Slider { Minimum = 0, Maximum = 0, Width = 300, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        _play = Ui.IconButton(VectorIcon.Play, UiStrings.Model_PlayTip, (_, _) => TogglePlay());
        var previewHint = Dim(Ui.Label(UiStrings.Model_PreviewHint));
        previewHint.TextWrapping = TextWrapping.Wrap;
        previewHint.MaxWidth = 664;
        var pixelLabel = Ui.Label(UiStrings.Model_PixelResult);
        Grid.SetColumn(pixelLabel, 1);
        var previewBox = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("332,332"),
                    Children =
                    {
                        Ui.Label(UiStrings.Model_Original),
                        pixelLabel,
                    },
                },
                new Border { BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), Child = new Panel { Children = { _preview, _dropHint } } },
                (_usedRow = new StackPanel { Spacing = 2, Margin = new Thickness(333, 0, 0, 0), IsVisible = false, Children = { _usedLabel, _usedColors } }),
                previewHint,
                Ui.Row(_play, _frame, _turnAngle),
            },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        void AddRow(string label, Control content)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Ui.Label(label);
            l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(l, row);
            Grid.SetRow(content, row);
            Grid.SetColumn(content, 1);
            content.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(l);
            grid.Children.Add(content);
        }
        AddRow(UiStrings.Model_Preset, Ui.Row(_preset, savePreset, _deletePreset));
        AddRow(UiStrings.Model_Size, _size);
        AddRow(UiStrings.Model_Angle, Ui.Row(Ui.Label(UiStrings.Model_Yaw), _yaw, Ui.Label(UiStrings.Model_Pitch), _pitch, Ui.Label(UiStrings.Model_Roll), _roll));
        AddRow(UiStrings.Model_Projection, Ui.Row(_projection, Ui.Label(UiStrings.Model_Fov), _fov));
        AddRow(UiStrings.Model_Zoom, Ui.Row(_zoom, Ui.Button(UiStrings.Model_Fit, (_, _) => _zoom.Value = 100)));
        AddRow(UiStrings.Model_Light, Ui.Row(Ui.Label(UiStrings.Model_Yaw), _lightYaw, Ui.Label(UiStrings.Model_Pitch), _lightPitch));
        AddRow(UiStrings.Model_LightDepth, Ui.Row(Ui.Label(UiStrings.Model_LightFront), _lightDepth, Ui.Label(UiStrings.Model_LightBehind), _lightDepthValue));
        AddRow(UiStrings.Model_Brightness, Ui.Row(Ui.Label(UiStrings.Model_Ambient), _ambient, Ui.Label(UiStrings.Model_LightStrength), _light));
        AddRow(UiStrings.Model_Steps, _steps);
        AddRow(UiStrings.Model_Smooth, Ui.Row(_smooth, _textures));
        AddRow(UiStrings.Model_PixelTidy, Ui.Row(_hardAlpha, Ui.Label(UiStrings.Model_Colors), _colors));
        AddRow(UiStrings.Model_Outline, Ui.Row(_outer, _outside, _inner, _creaseOn, _crease));
        AddRow(UiStrings.Model_OutlineLook, Ui.Row(Ui.Label(UiStrings.Model_OutlineWidth), _outlineWidth, _outlineColorMode, _outlineColor, _darken));
        AddRow(UiStrings.Model_Frames, _mode);
        AddRow(UiStrings.Model_Turntable, Ui.Row(_turnFrames, _freeTurn));
        AddRow(UiStrings.Model_Clip, Ui.Row(_clip, _importAnim));
        AddRow(UiStrings.Model_Fps, Ui.Row(_fps, Ui.Label(UiStrings.Model_MaxFrames), _maxFrames));

        _summary = Dim(Ui.Label(""));
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.MaxWidth = 480;
        var openModel = Ui.Button(VectorIcon.FolderOpen, UiStrings.Model_OpenModel, (_, _) => Dispatcher.UIThread.Post(PickModel)).Tip(UiStrings.Model_OpenModelTip);
        _estimate = Ui.Label("");
        _estimate.FontWeight = FontWeight.SemiBold;
        _warnings = Ui.Label("");
        _warnings.Foreground = AppTheme.WarningBrush;
        _warnings.TextWrapping = TextWrapping.Wrap;
        _warnings.MaxWidth = 480;
        var settings = new StackPanel { Spacing = 6, Width = 490, Children = { Ui.Row(openModel), _summary, grid, _estimate, _warnings } };
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { previewBox, settings } });
        ShowModelState();

        // 모델 파일 끌어 놓기(창 어디든): 놓으면 그 모델을 읽어 바꾼다(여러 개면 첫 것)
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnModelDragOver);
        AddHandler(DragDrop.DragOverEvent, OnModelDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => ShowDropTarget(false));
        AddHandler(DragDrop.DropEvent, OnModelDrop);

        _player = new DispatcherTimer();
        _player.Tick += (_, _) => _frame.Value = _frame.Value >= _frame.Maximum ? 0 : _frame.Value + 1;

        Load(_lastRender, _lastFrames);
        try
        {
            _userPresets = ModelPresets.Load(PresetPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Opened += (_, _) => Ui.Warn(this, ex.Message);
        }
        FillPresets(null);
        _preset.SelectionChanged += (_, _) =>
        {
            if (_syncing || _preset.SelectedItem is not ComboBoxItem { Tag: ModelPreset p }) return;
            Load(p.Render, p.Frames);
            _deletePreset.IsEnabled = !p.BuiltIn;
        };

        _size.Changed += (_, _) => Sync();
        foreach (var n in new[] { _yaw, _pitch, _roll, _zoom, _fov, _lightYaw, _lightPitch, _ambient, _light, _steps, _turnFrames, _fps, _maxFrames,
                     _colors, _crease, _outlineWidth, _darken })
            n.ValueChanged += (_, _) => Sync();
        foreach (var c in new[] { _projection, _smooth, _mode, _clip, _outlineColorMode }) c.SelectionChanged += (_, _) => Sync();
        foreach (var c in new[] { _textures, _hardAlpha, _outer, _outside, _inner, _creaseOn }) c.IsCheckedChanged += (_, _) => Sync();
        _freeTurn.IsCheckedChanged += (_, _) =>
        {
            // 막대 자리를 같은 각도로 옮긴다(프레임 k ↔ k × 360 / N°)
            int n = Math.Max(1, (int)_turnFrames.NumValue());
            double target = _freeTurn.On() ? Math.Round(_frame.Value * 360.0 / n) % 360 : Math.Round(_frame.Value * n / 360.0) % n;
            Sync();
            _frame.Value = Math.Min(target, _frame.Maximum);
        };
        _lightDepth.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_syncing) SetLightDepth(_lightDepth.Value);
        };
        _frame.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            _turnAngle.Text = $"{_frame.Value:0}°";
            Schedule();
        };
        Closed += (_, _) =>
        {
            _player.Stop();
            _renderCts?.Cancel();
        };
        Sync();
    }

    /// <summary>자유 회전 중인지(돌려 보기이고 '자유 회전'을 켬).</summary>
    private bool FreeTurn => _freeTurn.On() && (ModelFrameMode)Math.Clamp(_mode.SelectedIndex, 0, 2) == ModelFrameMode.Turntable;

    /// <summary>고른 그리기 설정.</summary>
    public ModelRenderSettings RenderSettings => new()
    {
        Width = _size.Size.Width,
        Height = _size.Size.Height,
        Yaw = (float)_yaw.NumValue(),
        Pitch = (float)_pitch.NumValue(),
        Roll = (float)_roll.NumValue(),
        Projection = _projection.SelectedIndex == 1 ? ModelProjection.Perspective : ModelProjection.Orthographic,
        FieldOfView = (float)_fov.NumValue(),
        Zoom = (float)_zoom.NumValue() / 100f,
        LightYaw = (float)_lightYaw.NumValue(),
        LightPitch = (float)_lightPitch.NumValue(),
        LightBack = _lightBehind,
        Ambient = (float)_ambient.NumValue() / 100f,
        LightStrength = (float)_light.NumValue() / 100f,
        ShadeSteps = (int)_steps.NumValue() == 1 ? 0 : (int)_steps.NumValue(),
        Supersample = SmoothLevels[Math.Clamp(_smooth.SelectedIndex, 0, SmoothLevels.Length - 1)],
        Textures = _textures.On(),
        HardAlpha = _hardAlpha.On(),
        Colors = (int)_colors.NumValue() == 1 ? 2 : (int)_colors.NumValue(),
        OutlineOuter = _outer.On(),
        OutlineOutside = _outside.On(),
        OutlineInner = _inner.On(),
        OutlineCrease = _creaseOn.On() ? (float)_crease.NumValue() : 0,
        OutlineWidth = (int)_outlineWidth.NumValue(),
        OutlineColorMode = _outlineColorMode.SelectedIndex == 1 ? ModelOutlineColor.Darken : ModelOutlineColor.Fixed,
        OutlineColor = _outlineColorValue,
        OutlineDarken = (float)_darken.NumValue() / 100f,
    };

    /// <summary>고른 프레임 설정.</summary>
    public ModelFrameOptions FrameOptions => new()
    {
        Mode = (ModelFrameMode)Math.Clamp(_mode.SelectedIndex, 0, 2),
        TurntableFrames = (int)_turnFrames.NumValue(),
        Animation = Math.Max(0, _clip.SelectedIndex),
        Fps = (double)_fps.NumValue(),
        MaxFrames = (int)_maxFrames.NumValue(),
    };

    /// <summary>창을 닫을 때(가져오기) 고른 설정을 다음 모델의 처음 값으로 기억한다.</summary>
    public void Remember()
    {
        _lastRender = RenderSettings;
        _lastFrames = FrameOptions;
    }

    /// <summary>그릴 장면(불러온 애니메이션까지 붙은 것 — 가져오기는 이 장면으로 그린다). 모델을 연 뒤에만(가져오기 단추가 그때 켜짐).</summary>
    public ModelScene Scene => _scene ?? throw new InvalidOperationException("no model");

    /// <summary>지금 연 모델 파일(모델 없이 열고 아직 고르지 않았으면 null).</summary>
    public string? ModelPath { get; private set; }

    // ───────────── 모델 열기·끌어 놓기 ─────────────

    /// <summary>끌어 온 것 중 첫 3D 모델 파일.</summary>
    private static string? DroppedModel(DragEventArgs e)
        => e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().FirstOrDefault(ModelLoader.IsModelFile);

    private void OnModelDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        bool ok = DroppedModel(e) is not null;
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        ShowDropTarget(ok);
    }

    private void OnModelDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        ShowDropTarget(false);
        if (DroppedModel(e) is not { } path) return;
        e.DragEffects = DragDropEffects.Copy;
        // 끌어 놓기가 끝난 뒤에 읽는다(진행 창이 끌기 도중에 뜨지 않게)
        Dispatcher.UIThread.Post(() => LoadModel(path));
    }

    /// <summary>끌어 오는 중이면 미리보기 위에 안내를 강조 테두리로 보인다(모델이 있으면 그 위에 잠깐 덮음).</summary>
    private void ShowDropTarget(bool on)
    {
        _dropHint.BorderBrush = on ? AppTheme.AccentBrush : null;
        _dropHint.IsVisible = on || _scene is null;
    }

    /// <summary>'모델 열기…': 3D 모델 파일을 골라 읽는다.</summary>
    private void PickModel()
    {
        var paths = Ui.OpenFiles(this, UiStrings.Model_OpenModel, _folder,
            new FileFilter(MainStrings.Open_FilterModels, MediaFormats.Patterns(MediaFormats.ModelExtensions)));
        if (paths.Count > 0) LoadModel(paths[0]);
    }

    /// <summary>모델 파일을 읽어(진행 창) 지금 모델로 바꾼다 — 설정은 그대로, 애니메이션 목록·요약·주의 사항·미리보기만 새 모델로. 읽지 못하면 알리고 그대로.</summary>
    private void LoadModel(string path)
    {
        var name = Path.GetFileName(path);
        ModelScene? scene;
        try
        {
            scene = ProgressDialog<ModelScene>.Run(this, UiStrings.Model_LoadingTitle, UiStrings.FormatModel_Loading(name: name), 0, (_, _) => ModelLoader.Load(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Ui.Error(this, MainStrings.FormatMessage_358(full: path, message: ex.Message));
            return;
        }
        if (scene is null) return;
        StopPlay();
        _scene = scene;
        ModelPath = path;
        _fileName = name;
        _folder = Path.GetDirectoryName(path);
        _posed = null;
        FillClips(0);
        ShowModelState();
        Sync();
    }

    /// <summary>모델 유무에 맞춰 요약·주의 사항·끌어 놓기 안내·가져오기·애니메이션 불러오기 단추를 맞춘다.</summary>
    private void ShowModelState()
    {
        UpdateSummary();
        var warnings = _scene?.Warnings.Distinct().ToList() ?? [];
        _warnings.Text = warnings.Count > 0 ? UiStrings.FormatModel_Warnings(list: string.Join(" · ", warnings)) : "";
        _warnings.IsVisible = warnings.Count > 0;
        ShowDropTarget(false);
        OkButton.IsEnabled = _importAnim.IsEnabled = _scene is not null;
    }

    private void UpdateSummary() => _summary.Text = _scene is not { } scene ? UiStrings.Model_NoModel
        : UiStrings.FormatModel_Summary(name: _fileName, triangles: CountTriangles(scene), materials: scene.Materials.Count, animations: scene.Animations.Count);

    /// <summary>애니메이션 목록을 지금 장면으로 다시 채우고 <paramref name="select"/>번을 고른다. 애니메이션이 있으면 프레임 방식에 '애니메이션'을 더한다.</summary>
    private void FillClips(int select)
    {
        bool was = _syncing;
        _syncing = true;
        _clip.Items.Clear();
        int count = _scene?.Animations.Count ?? 0;
        for (int i = 0; i < count; i++)
        {
            var a = _scene!.Animations[i];
            var seconds = a.Duration.ToString("0.##");
            _clip.Items.Add(string.IsNullOrWhiteSpace(a.Name)
                ? UiStrings.FormatModel_ClipUnnamed(number: i + 1, seconds: seconds)
                : UiStrings.FormatModel_ClipNamed(name: a.Name, seconds: seconds));
        }
        if (count > 0)
        {
            _clip.SelectedIndex = Math.Clamp(select, 0, count - 1);
            if (_mode.ItemCount < 3) _mode.Items.Add(UiStrings.Model_ModeAnimation);
        }
        else if (_mode.ItemCount >= 3)
        {
            // 애니메이션 없는 모델로 바꿈: 그 방식을 빼고(골라 두었으면 한 장으로)
            if (_mode.SelectedIndex == (int)ModelFrameMode.Animation) _mode.SelectedIndex = (int)ModelFrameMode.Still;
            _mode.Items.RemoveAt((int)ModelFrameMode.Animation);
        }
        _syncing = was;
    }

    /// <summary>
    /// '불러오기…': 고른 FBX·glTF 파일들의 애니메이션을 뼈 이름이 같은 뼈에 붙인다(<see cref="ModelAnimationImport.Attach"/> — 믹사모의 Without Skin FBX 등).
    /// 하나라도 붙으면 프레임 방식을 애니메이션으로 바꾸고 처음 붙인 애니메이션을 고른다. 못 붙인 파일과 주의 사항은 한 번에 알린다.
    /// </summary>
    private void ImportAnimations()
    {
        var paths = Ui.OpenFiles(this, UiStrings.Model_ImportAnimation, _folder,
            new FileFilter(MainStrings.Open_FilterModels, MediaFormats.Patterns(MediaFormats.ModelExtensions)));
        if (paths.Count == 0) return;
        if (_scene is not { } scene) return;
        var notes = new List<string>();
        int added = 0, first = scene.Animations.Count;
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            try
            {
                var source = ModelLoader.LoadAnimations(path);
                var result = ModelAnimationImport.Attach(scene, source, Path.GetFileNameWithoutExtension(path));
                scene = result.Scene;
                added += result.Added;
                foreach (var w in result.Warnings.Distinct()) notes.Add(name + ": " + w);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                notes.Add(name + ": " + ex.Message);
            }
        }
        if (added > 0)
        {
            _scene = scene;
            _posed = null;
            FillClips(first);
            _mode.SelectedIndex = (int)ModelFrameMode.Animation;
            UpdateSummary();
            Sync();
        }
        if (notes.Count > 0 || added == 0)
            Ui.Warn(this, UiStrings.FormatModel_ImportAnimationResult(added: added, files: paths.Count) + (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : ""));
    }

    /// <summary>값을 모두 칸에 넣는다(프리셋·처음 값 — 애니메이션 방식인데 이 모델에 애니메이션이 없으면 한 장).</summary>
    private void Load(ModelRenderSettings r, ModelFrameOptions f)
    {
        _syncing = true;
        static decimal Clamp(NumericUpDown n, double v) => Math.Clamp((decimal)v, n.Minimum, n.Maximum);
        _size.Size = new PixelSize((int)r.Width, (int)r.Height);
        _yaw.Value = Clamp(_yaw, r.Yaw);
        _pitch.Value = Clamp(_pitch, r.Pitch);
        _roll.Value = Clamp(_roll, r.Roll);
        _projection.SelectedIndex = r.Projection == ModelProjection.Perspective ? 1 : 0;
        _fov.Value = Clamp(_fov, r.FieldOfView);
        _zoom.Value = Clamp(_zoom, Math.Round(r.Zoom * 100));
        double lightYaw = r.LightYaw;
        bool lightBack = r.LightBack;
        if (lightYaw > 90) { lightYaw = 180 - lightYaw; lightBack = !lightBack; }
        if (lightYaw < -90) { lightYaw = -180 - lightYaw; lightBack = !lightBack; }
        _lightYaw.Value = Clamp(_lightYaw, lightYaw);
        _lightPitch.Value = Clamp(_lightPitch, r.LightPitch);
        _lightBehind = lightBack;
        _ambient.Value = Clamp(_ambient, Math.Round(r.Ambient * 100));
        _light.Value = Clamp(_light, Math.Round(r.LightStrength * 100));
        _steps.Value = Clamp(_steps, r.ShadeSteps);
        _smooth.SelectedIndex = Math.Max(0, Array.IndexOf(SmoothLevels, r.Supersample));
        _textures.IsChecked = r.Textures;
        _hardAlpha.IsChecked = r.HardAlpha;
        _colors.Value = Clamp(_colors, r.Colors);
        _outer.IsChecked = r.OutlineOuter;
        _outside.IsChecked = r.OutlineOutside;
        _inner.IsChecked = r.OutlineInner;
        _creaseOn.IsChecked = r.OutlineCrease > 0;
        if (r.OutlineCrease > 0) _crease.Value = Clamp(_crease, r.OutlineCrease);
        _outlineWidth.Value = Clamp(_outlineWidth, r.OutlineWidth);
        _outlineColorMode.SelectedIndex = r.OutlineColorMode == ModelOutlineColor.Darken ? 1 : 0;
        SetOutlineColor(r.OutlineColor);
        _darken.Value = Clamp(_darken, Math.Round(r.OutlineDarken * 100));
        var mode = f.Mode == ModelFrameMode.Animation && (_scene?.Animations.Count ?? 0) == 0 ? ModelFrameMode.Still : f.Mode;
        _mode.SelectedIndex = (int)mode;
        _turnFrames.Value = Clamp(_turnFrames, f.TurntableFrames);
        _fps.Value = Clamp(_fps, f.Fps);
        _maxFrames.Value = Clamp(_maxFrames, f.MaxFrames);
        _syncing = false;
        Sync();
    }

    /// <summary>프리셋 목록: 기본 제공 뒤에 저장한 것. <paramref name="select"/> 이름이 있으면 그것을 고른 채로(값은 바꾸지 않음).</summary>
    private void FillPresets(string? select)
    {
        _syncing = true;
        _preset.Items.Clear();
        foreach (var p in ModelPresets.BuiltIns.Concat(_userPresets))
        {
            var item = new ComboBoxItem { Content = p.Name, Tag = p };
            _preset.Items.Add(item);
            if (p.Name == select) _preset.SelectedItem = item;
        }
        _deletePreset.IsEnabled = _preset.SelectedItem is ComboBoxItem { Tag: ModelPreset { BuiltIn: false } };
        _syncing = false;
    }

    /// <summary>지금 값을 이름 붙여 저장한다(같은 이름의 저장한 프리셋은 덮어씀, 기본 제공 이름은 쓸 수 없음).</summary>
    private void SavePreset()
    {
        string suggestion = _preset.SelectedItem is ComboBoxItem { Tag: ModelPreset { BuiltIn: false } current } ? current.Name : "";
        if (InputDialog.Ask(this, UiStrings.Model_PresetSave, UiStrings.Model_PresetName, suggestion, _userPresets.Select(p => p.Name)) is not { } name) return;
        if (ModelPresets.BuiltIns.Any(b => b.Name == name))
        {
            Ui.Warn(this, UiStrings.Model_PresetBuiltInName);
            return;
        }
        var preset = new ModelPreset { Name = name, Render = RenderSettings, Frames = FrameOptions with { Animation = 0 } };
        var list = _userPresets.Where(p => p.Name != name).Append(preset).ToList();
        try
        {
            ModelPresets.Save(PresetPath, list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
            return;
        }
        _userPresets = list;
        FillPresets(name);
    }

    private void DeletePreset()
    {
        if (_preset.SelectedItem is not ComboBoxItem { Tag: ModelPreset { BuiltIn: false } current }) return;
        if (Ui.Ask(this, UiStrings.FormatModel_PresetDeleteAsk(name: current.Name), UiStrings.Model_PresetDelete, MessageButtons.YesNo, MessageIcon.Question)
            != DialogResult.Yes) return;
        var list = _userPresets.Where(p => p.Name != current.Name).ToList();
        try
        {
            ModelPresets.Save(PresetPath, list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
            return;
        }
        _userPresets = list;
        FillPresets(null);
    }

    private void PickOutlineColor()
    {
        if (Ui.PickColor(this, _outlineColorValue, UiStrings.Model_OutlineColorPick) is { } c)
        {
            SetOutlineColor(c.WithAlpha(255));
            Sync();
        }
    }

    private void SetOutlineColor(Color32 c)
    {
        _outlineColorValue = c;
        _outlineColor.Content = new Border
        {
            Width = 28,
            Height = 14,
            CornerRadius = new CornerRadius(2),
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Avalonia.Media.Color.FromArgb(c.A, c.R, c.G, c.B)),
        };
    }

    private static decimal Wrap(double degrees)
    {
        degrees %= 360;
        if (degrees > 180) degrees -= 360;
        if (degrees < -180) degrees += 360;
        return (decimal)Math.Round(degrees, 1);
    }

    private static int CountTriangles(ModelScene scene)
    {
        long n = 0;
        foreach (var node in scene.Nodes)
            if (node.Mesh >= 0 && node.Mesh < scene.Meshes.Count)
                foreach (var p in scene.Meshes[node.Mesh].Primitives) n += p.Indices.Length / 3;
        return (int)Math.Min(n, int.MaxValue);
    }

    private void Sync()
    {
        if (_syncing) return;
        _syncing = true;
        var mode = (ModelFrameMode)Math.Clamp(_mode.SelectedIndex, 0, 2);
        _turnFrames.IsEnabled = _freeTurn.IsEnabled = mode == ModelFrameMode.Turntable;
        _clip.IsEnabled = _fps.IsEnabled = _maxFrames.IsEnabled = mode == ModelFrameMode.Animation;
        _fov.IsEnabled = _projection.SelectedIndex == 1;
        bool outline = _outer.On() || _inner.On() || _creaseOn.On();
        _outside.IsEnabled = _outer.On();
        _crease.IsEnabled = _creaseOn.On();
        _outlineWidth.IsEnabled = _outlineColorMode.IsEnabled = outline;
        _outlineColor.IsEnabled = outline && _outlineColorMode.SelectedIndex == 0;
        _darken.IsEnabled = outline && _outlineColorMode.SelectedIndex == 1;
        var o = FrameOptions;
        IReadOnlyList<ModelFrame> frames = _scene is null ? [] : ModelFrames.Plan(_scene, o);
        bool free = FreeTurn;
        _frame.Maximum = free ? 359 : Math.Max(0, frames.Count - 1);
        if (_frame.Value > _frame.Maximum) _frame.Value = _frame.Maximum;
        _frame.IsEnabled = _play.IsEnabled = free || frames.Count > 1;
        if (!free && frames.Count <= 1) StopPlay();
        _player.Interval = TimeSpan.FromMilliseconds(free ? FreeTurnTickMs : ModelFrames.DurationMs(o));
        _turnAngle.IsVisible = free;
        _turnAngle.Text = $"{_frame.Value:0}°";
        var r = RenderSettings;
        _preview.SetOrientation(r);
        _lightDepth.Value = LightDepthOf(r);
        _lightDepthValue.Text = $"{_lightDepth.Value:0}°";
        _estimate.Text = _scene is null ? "" : UiStrings.FormatModel_Estimate(frames: frames.Count, width: r.Width, height: r.Height, ms: ModelFrames.DurationMs(o));
        _syncing = false;
        Schedule();
    }

    /// <summary>빛이 정면(카메라 쪽)에서 몇 도 돌아가 있는지: 0° = 정면, 90° = 옆, 180° = 정뒤(빛 가로·세로·뒤쪽 여부로 잼).</summary>
    private static double LightDepthOf(ModelRenderSettings r)
    {
        double ly = r.LightYaw * Math.PI / 180, lp = r.LightPitch * Math.PI / 180;
        double z = (r.LightBack ? -1 : 1) * Math.Cos(ly) * Math.Cos(lp);
        return Math.Acos(Math.Clamp(z, -1, 1)) * 180 / Math.PI;
    }

    /// <summary>
    /// 앞뒤 막대: 빛을 정면에서 <paramref name="degrees"/>도(0 = 정면, 180 = 정뒤)로 옮긴다 — 화면 안 방향(해 손잡이가 가리키는 쪽)은 그대로 두고
    /// 빛 가로·세로 값과 뒤쪽 여부를 다시 맞춘다.
    /// </summary>
    private void SetLightDepth(double degrees)
    {
        double ly = (double)_lightYaw.NumValue() * Math.PI / 180, lp = (double)_lightPitch.NumValue() * Math.PI / 180;
        double x = Math.Sin(ly) * Math.Cos(lp), y = Math.Sin(lp);
        if (x * x + y * y > 1e-6) _lightTurn = Math.Atan2(y, x);
        double v = degrees * Math.PI / 180, ring = Math.Sin(v), z = Math.Cos(v);
        double nx = ring * Math.Cos(_lightTurn), ny = ring * Math.Sin(_lightTurn);
        _syncing = true;
        _lightYaw.Value = Math.Clamp((decimal)Math.Round(Math.Atan2(nx, Math.Abs(z)) * 180 / Math.PI, 1), _lightYaw.Minimum, _lightYaw.Maximum);
        _lightPitch.Value = Math.Clamp((decimal)Math.Round(Math.Asin(Math.Clamp(ny, -1, 1)) * 180 / Math.PI, 1), _lightPitch.Minimum, _lightPitch.Maximum);
        _lightBehind = z < 0;
        _syncing = false;
        Sync();
    }

    /// <summary>
    /// 미리보기를 다시 그리게 한다. 같은 틱의 변경은 한 번으로 모으고, 그리는 중이면 끝난 뒤 지금 값으로 한 번 더 그린다
    /// (그리던 것을 버리지 않아 끄는 동안에도 끝난 그림이 계속 나온다 — 끄기마다 처음부터 다시 그리면 손을 멈출 때까지 아무것도 안 보임).
    /// </summary>
    private void Schedule()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _renderQueued = false;
            if (_rendering) _renderAgain = true;
            else RenderPreview();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 지금 설정·프레임 막대 자리의 프레임을 다른 스레드에서 그려 미리보기에 보인다. 원본 칸은 미리보기를 끄는 동안 2×2 표본(가볍게),
    /// 아니면 4×4 표본으로 그린다(놓으면 <see cref="ModelPreview.DragEnded"/>로 다시 그림).
    /// </summary>
    private void RenderPreview()
    {
        if (_scene is not { } scene) return;
        _renderCts ??= new CancellationTokenSource();
        var cts = _renderCts;
        var options = FrameOptions;
        var settings = RenderSettings;
        int index = (int)_frame.Value;
        bool free = FreeTurn;
        int originalSamples = _preview.Dragging ? 2 : 4;
        var posed = _posed is { } p && p.Options == options && ReferenceEquals(p.Scene, scene) ? p : ((ModelScene, ModelFrameOptions, IReadOnlyList<ModelFrame>, PosedMesh[], ModelBounds)?)null;
        _rendering = true;
        Task.Run(() =>
        {
            string? error = null;
            (ModelScene, ModelFrameOptions, IReadOnlyList<ModelFrame>, PosedMesh[], ModelBounds)? done = null;
            PixelImage? original = null, image = null;
            ModelRenderSettings view = settings;
            IReadOnlyList<Color32> used = [];
            int usedCount = 0;
            try
            {
                var (s, o, frames, poses, fit) = posed ?? Pose(scene, options, cts.Token);
                // 자유 회전: 첫 자세를 막대 각도(°)만큼 돌려 본다(돌려 보기 자세는 모두 같음)
                int i = free ? 0 : Math.Clamp(index, 0, frames.Count - 1);
                view = settings with { Yaw = settings.Yaw + (free ? index : frames[i].YawOffset) };
                image = ModelRenderer.Render(poses[i], view, fit, cts.Token);
                (used, usedCount) = UsedColors(image);
                // 원본은 같은 자세·카메라·빛으로 출력 픽셀 다듬기 전에 독립적으로 부드럽게 그린다.
                // 긴 변을 제한해 큰 내보내기 크기에서도 미리보기 비용은 일정하다.
                double scale = 330d / Math.Max(settings.Width, settings.Height);
                int smoothWidth = Math.Max(1, (int)Math.Round(settings.Width * scale));
                int smoothHeight = Math.Max(1, (int)Math.Round(settings.Height * scale));
                original = ModelRenderer.RenderLayers(poses[i], view with
                {
                    Width = smoothWidth, Height = smoothHeight, Supersample = originalSamples, ShadeSteps = 0,
                    HardAlpha = false, Colors = 0, OutlineOuter = false, OutlineInner = false, OutlineCrease = 0,
                }, fit, cts.Token).Image;
                done = (s, o, frames, poses, fit);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
            {
                error = ex.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                _rendering = false;
                if (cts.IsCancellationRequested) return;
                if (error is not null) _estimate.Text = error;
                if (done is { } d && original is not null && image is not null)
                {
                    _posed = d;
                    _preview.SetOrientation(view);
                    _preview.Show(original, image);
                    _usedColors.SetColors(used);
                    _usedLabel.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Model_UsedColors, usedCount);
                    _usedRow.IsVisible = true;
                }
                if (_renderAgain)
                {
                    _renderAgain = false;
                    RenderPreview();
                }
            });
        });
    }

    private static (ModelScene, ModelFrameOptions, IReadOnlyList<ModelFrame>, PosedMesh[], ModelBounds) Pose(ModelScene scene, ModelFrameOptions options, CancellationToken ct)
    {
        var frames = ModelFrames.Plan(scene, options);
        var poses = ModelFrames.Pose(scene, frames, ct);
        return (scene, options, frames, poses, ModelFrames.Fit(poses));
    }

    /// <summary>그림에 쓴 색(완전히 투명한 픽셀은 빼고): 많이 쓴 색부터 앞 <see cref="MaxShownColors"/>개와 전체 색 수.</summary>
    private static (IReadOnlyList<Color32> Colors, int Count) UsedColors(PixelImage image)
    {
        var counts = new Dictionary<Color32, int>();
        foreach (var c in image.Pixels)
            if (c.A > 0) counts[c] = counts.GetValueOrDefault(c) + 1;
        return ([.. counts.OrderByDescending(p => p.Value).Take(MaxShownColors).Select(p => p.Key)], counts.Count);
    }

    private void TogglePlay()
    {
        if (_player.IsEnabled) StopPlay();
        else
        {
            _player.Start();
            SetPlayIcon(VectorIcon.Pause);
        }
    }

    private void StopPlay()
    {
        _player.Stop();
        SetPlayIcon(VectorIcon.Play);
    }

    private void SetPlayIcon(VectorIcon icon)
    {
        var iv = Ui.Icon(icon, 14);
        iv.Margin = new Thickness(0, 1);
        _play.Content = iv;
    }

    private static TextBlock Dim(TextBlock label)
    {
        label.Foreground = AppTheme.DimTextBrush;
        return label;
    }

    /// <summary>
    /// 두 결과가 같은 프레임을 보며, 왼쪽에 카메라 고리·축·빛 핸들을 겹쳐 그린다. X·Y 고리는 모델 공간의 원(X축·Y축 둘레)을 지금 보는 방향으로 비춰 그려
    /// 모델을 돌리면 함께 돌고(카메라 쪽 반은 진하게, 뒤쪽 반은 흐리게), 바깥 Z 고리는 화면 회전이라 화면에 붙어 있다.
    /// </summary>
    private sealed class ModelPreview : Control
    {
        private const double Pane = 330;
        private static readonly Avalonia.Point Center = new(Pane / 2, Pane / 2);
        private static readonly Avalonia.Point AxisCenter = new(43, Pane - 44);
        private static readonly Avalonia.Point SunCenter = new(Pane - 43, 44);
        private static readonly Pen RingPen = new(new SolidColorBrush(Color.FromArgb(105, 210, 220, 230)), 1.5);
        private static readonly Pen RearRingPen = new(new SolidColorBrush(Color.FromArgb(40, 210, 220, 230)), 1.5);
        private static readonly Pen RearHoverPen = new(new SolidColorBrush(Color.FromArgb(110, 112, 189, 255)), 2);
        /// <summary>X·Y 고리 반지름과 그릴 때 나누는 토막 수.</summary>
        private const double RingRadius = 112;
        private const int RingSegments = 72;
        private static readonly Pen HoverPen = new(new SolidColorBrush(Color.FromArgb(240, 112, 189, 255)), 3);
        private static readonly Pen AxisPen = new(new SolidColorBrush(Color.FromArgb(210, 220, 230, 240)), 1.5);
        private static readonly Pen SunPen = new(new SolidColorBrush(Color.FromRgb(255, 211, 91)), 2);
        private static readonly IBrush SunBrush = new SolidColorBrush(Color.FromRgb(255, 211, 91));
        private static readonly Cursor OrbitCursor = new(StandardCursorType.SizeAll);
        private static readonly Cursor HandleCursor = new(StandardCursorType.Hand);
        private static readonly System.Numerics.Vector3[] Axes =
            [System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitZ];
        private static readonly string[] AxisLabels = ["X", "Y", "Z"];
        private static readonly string[,] AxisHits =
        {
            { "axis:0:-1", "axis:1:-1", "axis:2:-1" },
            { "axis:0:1", "axis:1:1", "axis:2:1" },
        };
        private static readonly IBrush[] FrontAxisBrushes =
        [
            new SolidColorBrush(Color.FromRgb(251, 113, 110)),
            new SolidColorBrush(Color.FromRgb(117, 214, 139)),
            new SolidColorBrush(Color.FromRgb(104, 165, 255)),
        ];
        private static readonly IBrush[] BackAxisBrushes =
        [
            new SolidColorBrush(Color.FromArgb(140, 251, 113, 110)),
            new SolidColorBrush(Color.FromArgb(140, 117, 214, 139)),
            new SolidColorBrush(Color.FromArgb(140, 104, 165, 255)),
        ];
        private WriteableBitmap? _original, _pixel;
        private Avalonia.Point? _drag;
        private string _active = "", _hover = "";
        private double _lastRollAngle;
        /// <summary>보기 확대(1 = 칸에 맞춤)와 옮김(칸 좌표) — 두 칸이 같이 쓴다. 휠로 바꾸고, 확대 중 오른쪽·가운데 끌기로 옮긴다.</summary>
        private double _viewScale = 1;
        private Vector _viewOffset;
        private Avalonia.Point? _pan;
        private ModelRenderSettings _orientation = new();
        private System.Numerics.Vector3 _right, _up, _back;

        public event Action<double, double>? Rotated;
        public event Action<double>? Rolled;
        public event Action<double, double>? Faced;
        public event Action<double, double>? LightMoved;
        /// <summary>끌기(돌리기·고리·해)를 마침 — 가볍게 그리던 미리보기를 제대로 다시 그릴 때.</summary>
        public event Action? DragEnded;

        /// <summary>지금 미리보기를 끌고 있는지.</summary>
        public bool Dragging => _drag is not null;

        public ModelPreview()
        {
            ClipToBounds = true;
            Cursor = OrbitCursor;
            SetOrientation(_orientation);
        }

        public void SetOrientation(ModelRenderSettings settings)
        {
            _orientation = settings;
            float yaw = settings.Yaw * MathF.PI / 180, pitch = settings.Pitch * MathF.PI / 180;
            _back = new System.Numerics.Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
            var right = new System.Numerics.Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
            var up = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(_back, right));
            float roll = settings.Roll * MathF.PI / 180;
            _right = right * MathF.Cos(roll) + up * MathF.Sin(roll);
            _up = up * MathF.Cos(roll) - right * MathF.Sin(roll);
            InvalidateVisual();
        }

        private static WriteableBitmap Copy(PixelImage image, WriteableBitmap? bitmap)
        {
            if (bitmap is { } old && old.PixelSize.Width == image.Width && old.PixelSize.Height == image.Height)
            {
                ImageUtil.CopyToBitmap(image, old);
                return old;
            }
            bitmap?.Dispose();
            return ImageUtil.ToBitmap(image);
        }

        public void Show(PixelImage original, PixelImage pixel)
        {
            _original = Copy(original, _original);
            _pixel = Copy(pixel, _pixel);
            InvalidateVisual();
        }

        /// <summary>칸 하나에 그림을 맞춰 그리고 보기 확대·옮김을 입힌다(칸 밖은 잘림). 픽셀 결과는 계단 그대로.</summary>
        private void DrawImage(DrawingContext g, WriteableBitmap? bmp, double left, bool pixel)
        {
            if (bmp is null) return;
            var size = bmp.PixelSize;
            double fit = Math.Min(Pane / size.Width, Pane / size.Height) * _viewScale;
            double baseX = (Pane - size.Width * fit / _viewScale) / 2 * _viewScale, baseY = (Pane - size.Height * fit / _viewScale) / 2 * _viewScale;
            var dest = new Rect(left + baseX + _viewOffset.X, baseY + _viewOffset.Y, size.Width * fit, size.Height * fit);
            using (g.PushClip(new Rect(left, 0, Pane, Pane)))
            {
                AppTheme.DrawChecker(g, dest, 8);
                if (pixel)
                {
                    using (ImageUtil.Pixelated(g))
                        g.DrawImage(bmp, new Rect(0, 0, size.Width, size.Height), dest);
                }
                else g.DrawImage(bmp, new Rect(0, 0, size.Width, size.Height), dest);
            }
        }

        public override void Render(DrawingContext g)
        {
            g.FillRectangle(AppTheme.BackBrush, new Rect(Bounds.Size));
            DrawImage(g, _original, 0, false);
            DrawImage(g, _pixel, Pane + 4, true);
            g.DrawLine(AxisPen, new Avalonia.Point(Pane + 2, 0), new Avalonia.Point(Pane + 2, Pane));
            if (_hover.Length != 0 || _active.Length != 0)
            {
                // X = pitch, Y = yaw, Z = roll. X·Y 고리는 모델과 함께 돌고, 바깥 Z 고리는 화면에 붙어 있다.
                g.DrawEllipse(null, _hover == "roll" || _active == "roll" ? HoverPen : RingPen, Center, 137, 137);
                DrawRing(g, 1, "yaw", "Y");
                DrawRing(g, 0, "pitch", "X");
                AppTheme.DrawText(g, "Z", new Avalonia.Point(Center.X - 4, Center.Y - 148), Brushes.White, 11, bold: true);
            }
            DrawAxes(g);
            DrawSun(g);
        }

        /// <summary>
        /// 축 <paramref name="axis"/>(0 = X, 1 = Y) 둘레 고리의 <paramref name="t"/>(라디안) 자리: 모델 공간 원 위 점을 지금 보는 방향으로 비춘 화면 자리와,
        /// 그 점이 카메라 쪽 반에 있는지.
        /// </summary>
        private (Avalonia.Point Point, bool Front) RingPoint(int axis, double t)
        {
            var (u, w) = axis == 0
                ? (System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitZ)
                : (System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitX);
            var v = u * (float)Math.Cos(t) + w * (float)Math.Sin(t);
            return (new Avalonia.Point(Center.X + RingRadius * System.Numerics.Vector3.Dot(v, _right), Center.Y - RingRadius * System.Numerics.Vector3.Dot(v, _up)),
                System.Numerics.Vector3.Dot(v, _back) >= 0);
        }

        /// <summary>X·Y 고리 하나: 토막마다 카메라 쪽이면 진하게, 뒤쪽이면 흐리게(가리키거나 끄는 중이면 파랗게). 이름은 Y 고리 오른쪽 끝·X 고리 위쪽 끝에.</summary>
        private void DrawRing(DrawingContext g, int axis, string id, string label)
        {
            bool lit = _hover == id || _active == id;
            var prev = RingPoint(axis, 0);
            var labelAt = prev.Point;
            for (int i = 1; i <= RingSegments; i++)
            {
                var next = RingPoint(axis, i * 2 * Math.PI / RingSegments);
                bool front = prev.Front || next.Front;
                g.DrawLine(front ? lit ? HoverPen : RingPen : lit ? RearHoverPen : RearRingPen, prev.Point, next.Point);
                if (axis == 1 ? next.Point.X > labelAt.X : next.Point.Y < labelAt.Y) labelAt = next.Point;
                prev = next;
            }
            var at = axis == 1 ? new Avalonia.Point(labelAt.X + 4, labelAt.Y - 8) : new Avalonia.Point(labelAt.X - 4, labelAt.Y - 18);
            AppTheme.DrawText(g, label, at, Brushes.White, 11, bold: true);
        }

        /// <summary>점이 X·Y 고리(비춘 모양) 가까이인지.</summary>
        private bool NearRing(int axis, Avalonia.Point p)
        {
            var prev = RingPoint(axis, 0).Point;
            for (int i = 1; i <= RingSegments; i++)
            {
                var next = RingPoint(axis, i * 2 * Math.PI / RingSegments).Point;
                if (SegmentDistance(p, prev, next) < 7) return true;
                prev = next;
            }
            return false;
        }

        private static double SegmentDistance(Avalonia.Point p, Avalonia.Point a, Avalonia.Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
            double t = len <= 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len, 0, 1);
            return Distance(p, new Avalonia.Point(a.X + t * dx, a.Y + t * dy));
        }

        private Avalonia.Point AxisPoint(System.Numerics.Vector3 axis, double length)
        {
            return new Avalonia.Point(AxisCenter.X + length * System.Numerics.Vector3.Dot(axis, _right),
                AxisCenter.Y - length * System.Numerics.Vector3.Dot(axis, _up));
        }

        private void DrawAxes(DrawingContext g)
        {
            var axes = Axes;
            var back = _back;
            // 뒤쪽 끝을 먼저 칠해 앞쪽 끝이 눌러 보기에도 앞에 오게 한다.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < axes.Length; i++)
                {
                    var axis = axes[i] * side;
                    var tip = AxisPoint(axis, side > 0 ? 27 : 18);
                    if (System.Numerics.Vector3.Dot(axis, back) > 0)
                        g.DrawLine(AxisPen, AxisCenter, tip);
                    bool rear = System.Numerics.Vector3.Dot(axis, back) < 0;
                    var brush = rear ? null : side > 0 ? FrontAxisBrushes[i] : BackAxisBrushes[i];
                    g.DrawEllipse(brush, _hover == AxisHits[side > 0 ? 1 : 0, i] ? HoverPen : AxisPen,
                        tip, side > 0 ? 9 : 6, side > 0 ? 9 : 6);
                    if (side > 0) AppTheme.DrawText(g, AxisLabels[i], new Avalonia.Point(tip.X - 4, tip.Y - 8), Brushes.White, 11, bold: true);
                }
        }

        private Avalonia.Point SunPoint()
        {
            double yaw = _orientation.LightYaw * Math.PI / 180, pitch = _orientation.LightPitch * Math.PI / 180;
            return new Avalonia.Point(SunCenter.X + 24 * Math.Sin(yaw) * Math.Cos(pitch), SunCenter.Y - 24 * Math.Sin(pitch));
        }

        private void DrawSun(DrawingContext g)
        {
            g.DrawEllipse(null, AxisPen, SunCenter, 27, 27);
            var p = SunPoint();
            g.DrawLine(SunPen, SunCenter, p);
            for (int n = 0; n < 8; n++)
            {
                double a = n * Math.PI / 4;
                g.DrawLine(SunPen, new Avalonia.Point(p.X + 10 * Math.Cos(a), p.Y + 10 * Math.Sin(a)),
                    new Avalonia.Point(p.X + 14 * Math.Cos(a), p.Y + 14 * Math.Sin(a)));
            }
            g.DrawEllipse(_orientation.LightBack ? AppTheme.BackBrush : SunBrush,
                _hover == "sun" || _active == "sun" ? HoverPen : SunPen, p, 8, 8);
        }

        private static double Distance(Avalonia.Point a, Avalonia.Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private string Hit(Avalonia.Point p)
        {
            if (p.X < 0 || p.X >= Pane || p.Y < 0 || p.Y >= Pane) return "";
            if (Distance(p, SunPoint()) <= 17) return "sun";
            var axes = Axes;
            var back = _back;
            // When opposite endpoints overlap, the one toward the camera wins.
            for (int facing = 1; facing >= -1; facing -= 2)
                for (int side = 1; side >= -1; side -= 2)
                    for (int i = 0; i < axes.Length; i++)
                        if ((System.Numerics.Vector3.Dot(axes[i] * side, back) >= 0 ? 1 : -1) == facing
                            && Distance(p, AxisPoint(axes[i] * side, side > 0 ? 27 : 18)) < 11)
                            return AxisHits[side > 0 ? 1 : 0, i];
            double dx = p.X - Center.X, dy = p.Y - Center.Y;
            if (Math.Abs(Math.Sqrt(dx * dx + dy * dy) - 137) <= 8) return "roll";
            if (NearRing(1, p)) return "yaw";
            if (NearRing(0, p)) return "pitch";
            return "orbit";
        }

        private void MoveSun(Avalonia.Point p)
        {
            double x = (p.X - SunCenter.X) / 24, y = (SunCenter.Y - p.Y) / 24;
            double radius = Math.Sqrt(x * x + y * y);
            if (radius > 0.999) { x *= 0.999 / radius; y *= 0.999 / radius; }
            double pitch = Math.Asin(y), yaw = Math.Atan2(x, Math.Sqrt(Math.Max(0, 1 - x * x - y * y)));
            LightMoved?.Invoke(yaw * 180 / Math.PI, pitch * 180 / Math.PI);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var props = e.GetCurrentPoint(this).Properties;
            // 확대해 본 뒤 오른쪽·가운데 끌기 = 보기 옮기기
            if ((props.IsRightButtonPressed || props.IsMiddleButtonPressed) && _viewScale > 1)
            {
                _pan = e.GetPosition(this);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
            if (!props.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            var hit = Hit(p);
            if (hit.Length == 0) return;
            if (hit.StartsWith("axis:", StringComparison.Ordinal))
            {
                int axis = hit[5] - '0', side = hit[7] == '-' ? -1 : 1;
                Faced?.Invoke(axis == 0 ? 90 * side : axis == 2 && side < 0 ? 180 : 0,
                    axis == 1 ? 90 * side : 0);
            }
            else
            {
                _active = hit;
                _drag = p;
                _lastRollAngle = Math.Atan2(p.Y - Center.Y, p.X - Center.X);
                if (hit == "sun") MoveSun(p);
                e.Pointer.Capture(this);
            }
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            if (_pan is { } from)
            {
                _viewOffset = ClampOffset(_viewOffset + (p - from), _viewScale);
                _pan = p;
                InvalidateVisual();
                return;
            }
            if (_drag is { } last)
            {
                if (_active == "sun") MoveSun(p);
                else if (_active == "roll")
                {
                    double angle = Math.Atan2(p.Y - Center.Y, p.X - Center.X);
                    double delta = (angle - _lastRollAngle) * 180 / Math.PI;
                    if (delta > 180) delta -= 360;
                    if (delta < -180) delta += 360;
                    Rolled?.Invoke(delta);
                    _lastRollAngle = angle;
                }
                else if (_active == "yaw") Rotated?.Invoke(p.X - last.X, 0);
                else if (_active == "pitch") Rotated?.Invoke(0, p.Y - last.Y);
                else Rotated?.Invoke(p.X - last.X, p.Y - last.Y);
                _drag = p;
            }
            else
            {
                string hover = Hit(p);
                if (_hover != hover)
                {
                    _hover = hover;
                    Cursor = hover is "roll" or "yaw" or "pitch" or "sun" || hover.StartsWith("axis:", StringComparison.Ordinal)
                        ? HandleCursor : OrbitCursor;
                    InvalidateVisual();
                }
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            bool dragged = _drag is not null;
            _drag = null;
            _pan = null;
            _active = "";
            e.Pointer.Capture(null);
            InvalidateVisual();
            if (dragged) DragEnded?.Invoke();
        }

        /// <summary>휠 = 보기 확대·축소(캔버스처럼 커서 자리 기준, 두 칸이 같이 — 1배에서 16배). 출력 확대 설정은 바꾸지 않는다.</summary>
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            var p = e.GetPosition(this);
            var at = new Vector(p.X >= Pane + 4 ? p.X - (Pane + 4) : Math.Min(p.X, Pane), p.Y);
            double scale = Math.Clamp(_viewScale * (e.Delta.Y > 0 ? 1.25 : 0.8), 1, 16);
            if (scale <= 1.001)
            {
                _viewScale = 1;
                _viewOffset = default;
            }
            else
            {
                // 커서 아래 자리가 그대로 있도록: 화면 = 칸 맞춤 자리 × 배율 + 옮김
                _viewOffset = ClampOffset(at - (at - _viewOffset) * (scale / _viewScale), scale);
                _viewScale = scale;
            }
            InvalidateVisual();
            e.Handled = true;
        }

        /// <summary>확대한 그림이 칸 밖으로 빠져 빈 곳이 보이지 않게 옮김을 가둔다.</summary>
        private static Vector ClampOffset(Vector offset, double scale)
            => new(Math.Clamp(offset.X, Pane * (1 - scale), 0), Math.Clamp(offset.Y, Pane * (1 - scale), 0));

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _original?.Dispose();
            _pixel?.Dispose();
            _original = _pixel = null;
        }
    }
}
