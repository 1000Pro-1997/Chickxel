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
/// </summary>
public sealed class ModelImportDialog : DialogBase
{
    /// <summary>마지막으로 쓴 설정(다음에 여는 모델의 처음 값 — 애니메이션 번호는 빼고).</summary>
    private static ModelRenderSettings _lastRender = new();
    private static ModelFrameOptions _lastFrames = new();

    /// <summary>사용자 프리셋 파일(설정 파일과 같은 폴더).</summary>
    public static string PresetPath => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, ModelPresets.FileName);

    /// <summary>그릴 장면(애니메이션을 불러오면 그 애니메이션을 붙인 새 장면으로 바뀜 — <see cref="Scene"/>).</summary>
    private ModelScene _scene;
    private readonly string _fileName;
    /// <summary>애니메이션 불러오기의 처음 폴더(모델 파일의 폴더).</summary>
    private readonly string? _folder;
    private readonly TextBlock _summary;
    private readonly Button _importAnim;
    private readonly NumericUpDown _width, _height, _yaw, _pitch, _zoom, _fov, _lightYaw, _lightPitch, _ambient, _light, _steps, _turnFrames, _fps, _maxFrames;
    private readonly NumericUpDown _colors, _crease, _outlineWidth, _darken;
    private readonly ComboBox _projection, _smooth, _mode, _clip, _outlineColorMode, _preset;
    private readonly CheckBox _textures, _hardAlpha, _outer, _outside, _inner, _creaseOn;
    private readonly Button _outlineColor, _deletePreset;
    private Color32 _outlineColorValue;
    private List<ModelPreset> _userPresets = [];
    private readonly ModelPreview _preview;
    private readonly Slider _frame;
    private readonly Button _play;
    private readonly TextBlock _estimate;
    private readonly DispatcherTimer _debounce, _player;
    private CancellationTokenSource? _renderCts;
    /// <summary>지금 프레임 설정의 자세와 맞춤 범위(프레임 설정이 바뀔 때만 다시 — 그리기 설정과는 상관없음).</summary>
    private (ModelFrameOptions Options, IReadOnlyList<ModelFrame> Frames, PosedMesh[] Poses, ModelBounds Fit)? _posed;
    /// <summary>값을 한꺼번에 바꾸는 중(프리셋 불러오기·맞추기) — 칸마다 다시 그리지 않게.</summary>
    private bool _syncing;

    /// <summary>가장자리 다듬기 단계(콤보 순서): 끔 = 1, 2×2, 4×4, 8×8 표본.</summary>
    private static readonly int[] SmoothLevels = [1, 2, 4, 8];

    /// <param name="folder">애니메이션 불러오기 창의 처음 폴더(모델 파일의 폴더, 없으면 null).</param>
    public ModelImportDialog(string fileName, ModelScene scene, string? folder = null) : base(UiStrings.Model_Title, UiStrings.Model_Import)
    {
        _scene = scene;
        _fileName = fileName;
        _folder = folder;

        _width = Ui.Num(1, 2048, 128, 70).Presets(Ui.SpriteSizes).Unit("px").RecentKey("model.width");
        _height = Ui.Num(1, 2048, 128, 70).Presets(Ui.SpriteSizes).Unit("px").RecentKey("model.height");
        _yaw = Ui.Num(-360, 360, 0, 70, decimals: 1, increment: 15).Unit("°");
        _pitch = Ui.Num(-90, 90, 0, 70, decimals: 1, increment: 5).Unit("°");
        _projection = Ui.Combo(120, UiStrings.Model_Orthographic, UiStrings.Model_Perspective);
        _fov = Ui.Num(5, 120, 35, 60, increment: 5).Unit("°").Tip(UiStrings.Model_FovTip);
        _zoom = Ui.Num(10, 1000, 100, 70, increment: 10).Unit("%").Tip(UiStrings.Model_ZoomTip);
        _lightYaw = Ui.Num(-180, 180, 0, 70, increment: 15).Unit("°");
        _lightPitch = Ui.Num(-90, 90, 0, 70, increment: 15).Unit("°");
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
        _clip = Ui.Combo(200);
        _importAnim = Ui.Button(UiStrings.Model_ImportAnimation, (_, _) => Dispatcher.UIThread.Post(ImportAnimations)).Tip(UiStrings.Model_ImportAnimationTip);
        FillClips(0);
        _fps = Ui.Num((decimal)ModelFrameOptions.MinFps, (decimal)ModelFrameOptions.MaxFps, 12, 60, decimals: 1).Presets(6, 8, 10, 12, 15, 24, 30);
        _maxFrames = Ui.Num(1, ModelFrameOptions.MaxFramesLimit, 120, 70).Presets(16, 32, 60, 120, 240);

        _preset = new ComboBox { Width = 230, Margin = new Thickness(3), PlaceholderText = UiStrings.Model_PresetPick };
        _preset.Tip(UiStrings.Model_PresetTip);
        var savePreset = Ui.Button(UiStrings.Model_PresetSave, (_, _) => Dispatcher.UIThread.Post(SavePreset)).Tip(UiStrings.Model_PresetSaveTip);
        _deletePreset = Ui.Button(UiStrings.Model_PresetDelete, (_, _) => DeletePreset()).Tip(UiStrings.Model_PresetDeleteTip);

        _preview = new ModelPreview { Width = 360, Height = 360 };
        _preview.Rotated += (dx, dy) =>
        {
            _yaw.Value = Wrap((double)_yaw.NumValue() + dx * 0.5);
            _pitch.Value = (decimal)Math.Clamp((double)_pitch.NumValue() + dy * 0.5, -90, 90);
        };
        _preview.Zoomed += factor => _zoom.Value = Math.Clamp(Math.Round(_zoom.NumValue() * (decimal)factor), _zoom.Minimum, _zoom.Maximum);
        _frame = new Slider { Minimum = 0, Maximum = 0, Width = 300, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        _play = Ui.IconButton(VectorIcon.Play, UiStrings.Model_PlayTip, (_, _) => TogglePlay());
        var previewBox = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new Border { BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), Child = _preview },
                Dim(Ui.Label(UiStrings.Model_PreviewHint)),
                Ui.Row(_play, _frame),
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
        AddRow(UiStrings.Model_Size, Ui.Row(_width, Ui.Label("×"), _height));
        AddRow(UiStrings.Model_Angle, Ui.Row(Ui.Label(UiStrings.Model_Yaw), _yaw, Ui.Label(UiStrings.Model_Pitch), _pitch));
        AddRow(UiStrings.Model_Projection, Ui.Row(_projection, Ui.Label(UiStrings.Model_Fov), _fov));
        AddRow(UiStrings.Model_Zoom, Ui.Row(_zoom, Ui.Button(UiStrings.Model_Fit, (_, _) => _zoom.Value = 100)));
        AddRow(UiStrings.Model_Light, Ui.Row(Ui.Label(UiStrings.Model_Yaw), _lightYaw, Ui.Label(UiStrings.Model_Pitch), _lightPitch));
        AddRow(UiStrings.Model_Brightness, Ui.Row(Ui.Label(UiStrings.Model_Ambient), _ambient, Ui.Label(UiStrings.Model_LightStrength), _light));
        AddRow(UiStrings.Model_Steps, _steps);
        AddRow(UiStrings.Model_Smooth, Ui.Row(_smooth, _textures));
        AddRow(UiStrings.Model_PixelTidy, Ui.Row(_hardAlpha, Ui.Label(UiStrings.Model_Colors), _colors));
        AddRow(UiStrings.Model_Outline, Ui.Row(_outer, _outside, _inner, _creaseOn, _crease));
        AddRow(UiStrings.Model_OutlineLook, Ui.Row(Ui.Label(UiStrings.Model_OutlineWidth), _outlineWidth, _outlineColorMode, _outlineColor, _darken));
        AddRow(UiStrings.Model_Frames, _mode);
        AddRow(UiStrings.Model_Turntable, _turnFrames);
        AddRow(UiStrings.Model_Clip, Ui.Row(_clip, _importAnim));
        AddRow(UiStrings.Model_Fps, Ui.Row(_fps, Ui.Label(UiStrings.Model_MaxFrames), _maxFrames));

        _summary = Dim(Ui.Label(""));
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.MaxWidth = 480;
        UpdateSummary();
        _estimate = Ui.Label("");
        _estimate.FontWeight = FontWeight.SemiBold;
        var settings = new StackPanel { Spacing = 6, Width = 490, Children = { _summary, grid, _estimate } };
        if (scene.Warnings.Count > 0)
        {
            var warn = Ui.Label(UiStrings.FormatModel_Warnings(list: string.Join(" · ", scene.Warnings.Distinct())));
            warn.Foreground = AppTheme.WarningBrush;
            warn.TextWrapping = TextWrapping.Wrap;
            warn.MaxWidth = 480;
            settings.Children.Add(warn);
        }
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { previewBox, settings } });

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RenderPreview();
        };
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

        foreach (var n in new[] { _width, _height, _yaw, _pitch, _zoom, _fov, _lightYaw, _lightPitch, _ambient, _light, _steps, _turnFrames, _fps, _maxFrames,
                     _colors, _crease, _outlineWidth, _darken })
            n.ValueChanged += (_, _) => Sync();
        foreach (var c in new[] { _projection, _smooth, _mode, _clip, _outlineColorMode }) c.SelectionChanged += (_, _) => Sync();
        foreach (var c in new[] { _textures, _hardAlpha, _outer, _outside, _inner, _creaseOn }) c.IsCheckedChanged += (_, _) => Sync();
        _frame.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) Schedule();
        };
        Closed += (_, _) =>
        {
            _player.Stop();
            _debounce.Stop();
            _renderCts?.Cancel();
        };
        Sync();
    }

    /// <summary>고른 그리기 설정.</summary>
    public ModelRenderSettings RenderSettings => new()
    {
        Width = (int)_width.NumValue(),
        Height = (int)_height.NumValue(),
        Yaw = (float)_yaw.NumValue(),
        Pitch = (float)_pitch.NumValue(),
        Projection = _projection.SelectedIndex == 1 ? ModelProjection.Perspective : ModelProjection.Orthographic,
        FieldOfView = (float)_fov.NumValue(),
        Zoom = (float)_zoom.NumValue() / 100f,
        LightYaw = (float)_lightYaw.NumValue(),
        LightPitch = (float)_lightPitch.NumValue(),
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

    /// <summary>그릴 장면(불러온 애니메이션까지 붙은 것 — 가져오기는 이 장면으로 그린다).</summary>
    public ModelScene Scene => _scene;

    private void UpdateSummary() => _summary.Text = UiStrings.FormatModel_Summary(name: _fileName, triangles: CountTriangles(_scene),
        materials: _scene.Materials.Count, animations: _scene.Animations.Count);

    /// <summary>애니메이션 목록을 지금 장면으로 다시 채우고 <paramref name="select"/>번을 고른다. 애니메이션이 있으면 프레임 방식에 '애니메이션'을 더한다.</summary>
    private void FillClips(int select)
    {
        bool was = _syncing;
        _syncing = true;
        _clip.Items.Clear();
        for (int i = 0; i < _scene.Animations.Count; i++)
        {
            var a = _scene.Animations[i];
            var seconds = a.Duration.ToString("0.##");
            _clip.Items.Add(string.IsNullOrWhiteSpace(a.Name)
                ? UiStrings.FormatModel_ClipUnnamed(number: i + 1, seconds: seconds)
                : UiStrings.FormatModel_ClipNamed(name: a.Name, seconds: seconds));
        }
        if (_scene.Animations.Count > 0)
        {
            _clip.SelectedIndex = Math.Clamp(select, 0, _scene.Animations.Count - 1);
            if (_mode.ItemCount < 3) _mode.Items.Add(UiStrings.Model_ModeAnimation);
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
        var scene = _scene;
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
        _width.Value = Clamp(_width, r.Width);
        _height.Value = Clamp(_height, r.Height);
        _yaw.Value = Clamp(_yaw, r.Yaw);
        _pitch.Value = Clamp(_pitch, r.Pitch);
        _projection.SelectedIndex = r.Projection == ModelProjection.Perspective ? 1 : 0;
        _fov.Value = Clamp(_fov, r.FieldOfView);
        _zoom.Value = Clamp(_zoom, Math.Round(r.Zoom * 100));
        _lightYaw.Value = Clamp(_lightYaw, r.LightYaw);
        _lightPitch.Value = Clamp(_lightPitch, r.LightPitch);
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
        var mode = f.Mode == ModelFrameMode.Animation && _scene.Animations.Count == 0 ? ModelFrameMode.Still : f.Mode;
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
        _turnFrames.IsEnabled = mode == ModelFrameMode.Turntable;
        _clip.IsEnabled = _fps.IsEnabled = _maxFrames.IsEnabled = mode == ModelFrameMode.Animation;
        _fov.IsEnabled = _projection.SelectedIndex == 1;
        bool outline = _outer.On() || _inner.On() || _creaseOn.On();
        _outside.IsEnabled = _outer.On();
        _crease.IsEnabled = _creaseOn.On();
        _outlineWidth.IsEnabled = _outlineColorMode.IsEnabled = outline;
        _outlineColor.IsEnabled = outline && _outlineColorMode.SelectedIndex == 0;
        _darken.IsEnabled = outline && _outlineColorMode.SelectedIndex == 1;
        var o = FrameOptions;
        var frames = ModelFrames.Plan(_scene, o);
        _frame.Maximum = Math.Max(0, frames.Count - 1);
        if (_frame.Value > _frame.Maximum) _frame.Value = _frame.Maximum;
        _frame.IsEnabled = _play.IsEnabled = frames.Count > 1;
        if (frames.Count <= 1) StopPlay();
        _player.Interval = TimeSpan.FromMilliseconds(ModelFrames.DurationMs(o));
        var r = RenderSettings;
        _estimate.Text = UiStrings.FormatModel_Estimate(frames: frames.Count, width: r.Width, height: r.Height, ms: ModelFrames.DurationMs(o));
        _syncing = false;
        Schedule();
    }

    private void Schedule()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>지금 설정·프레임 막대 자리의 프레임을 다른 스레드에서 그려 미리보기에 보인다(앞서 그리던 것은 멈춤).</summary>
    private void RenderPreview()
    {
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        var options = FrameOptions;
        var settings = RenderSettings;
        int index = (int)_frame.Value;
        var posed = _posed is { } p && p.Options == options ? p : ((ModelFrameOptions, IReadOnlyList<ModelFrame>, PosedMesh[], ModelBounds)?)null;
        var scene = _scene;
        Task.Run(() =>
        {
            try
            {
                var (o, frames, poses, fit) = posed ?? Pose(scene, options, cts.Token);
                int i = Math.Clamp(index, 0, frames.Count - 1);
                var image = ModelRenderer.Render(poses[i], settings with { Yaw = settings.Yaw + frames[i].YawOffset }, fit, cts.Token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    _posed = (o, frames, poses, fit);
                    _preview.Show(image);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
            {
                Dispatcher.UIThread.Post(() => { if (!cts.IsCancellationRequested) _estimate.Text = ex.Message; });
            }
        });
    }

    private static (ModelFrameOptions, IReadOnlyList<ModelFrame>, PosedMesh[], ModelBounds) Pose(ModelScene scene, ModelFrameOptions options, CancellationToken ct)
    {
        var frames = ModelFrames.Plan(scene, options);
        var poses = ModelFrames.Pose(scene, frames, ct);
        return (options, frames, poses, ModelFrames.Fit(poses));
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

    /// <summary>미리보기: 그린 그림을 칸에 맞는 가장 큰 배율로(최근접) 바둑판 바탕 위에. 끌면 돌리고, 휠로 확대한다.</summary>
    private sealed class ModelPreview : Control
    {
        private WriteableBitmap? _bitmap;
        private Avalonia.Point? _drag;

        /// <summary>끌어서 돌림(가로·세로 화면 px).</summary>
        public event Action<double, double>? Rotated;
        /// <summary>휠 확대 배율(1보다 크면 크게).</summary>
        public event Action<double>? Zoomed;

        public ModelPreview()
        {
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        public void Show(PixelImage image)
        {
            if (_bitmap is { } old && old.PixelSize.Width == image.Width && old.PixelSize.Height == image.Height) ImageUtil.CopyToBitmap(image, old);
            else
            {
                _bitmap?.Dispose();
                _bitmap = ImageUtil.ToBitmap(image);
            }
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            var bounds = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.BackBrush, bounds);
            if (_bitmap is not { } bmp) return;
            var size = bmp.PixelSize;
            double scale = Math.Min(bounds.Width / size.Width, bounds.Height / size.Height);
            // 1배보다 크면 정수 배로(픽셀이 고르게 보이게).
            if (scale >= 1) scale = Math.Floor(scale);
            var dest = new Rect((bounds.Width - size.Width * scale) / 2, (bounds.Height - size.Height * scale) / 2, size.Width * scale, size.Height * scale);
            AppTheme.DrawChecker(g, dest, 8);
            using (ImageUtil.Pixelated(g))
                g.DrawImage(bmp, new Rect(0, 0, size.Width, size.Height), dest);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _drag = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_drag is not { } last) return;
            var p = e.GetPosition(this);
            _drag = p;
            Rotated?.Invoke(p.X - last.X, p.Y - last.Y);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _drag = null;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            Zoomed?.Invoke(e.Delta.Y > 0 ? 1.1 : 1 / 1.1);
            e.Handled = true;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}
