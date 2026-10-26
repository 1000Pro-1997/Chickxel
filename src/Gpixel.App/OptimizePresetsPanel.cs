using Gpixel.App.Localization;
using Gpixel.Core.Pixelate;

namespace Gpixel.App;

/// <summary>
/// 자동 최적화 프리셋 목록과 편집(픽셀로 정리 창의 최적화 설정 창·프리셋 창이 함께 씀). 왼쪽은 프리셋 목록(체크 = 자동 최적화를 누르면 함께 돌림,
/// 즐겨찾기는 별 표시와 함께 맨 위)과 추가·복제·이름 바꾸기·즐겨찾기·삭제(마지막 하나는 지울 수 없음), 오른쪽은 고른 프리셋의 바꿔 볼 항목·예산·고정 점수 기준.
/// 고친 값은 <see cref="PixelateOptions.OptimizePresets"/>의 프리셋에 바로 쓰고 <see cref="Changed"/>를 알린다(저장은 부른 쪽).
/// <c>currentSettings</c>를 주면 그 설정에서 쓰지 않는 항목(<see cref="PixelateOptimizer.Applicable"/>)을 흐리게, 없으면 모두 켠다.
/// </summary>
public sealed class OptimizePresetsPanel : Grid
{
    private static readonly OptimizeParam[] Order =
    [
        OptimizeParam.Scale, OptimizeParam.OffsetX, OptimizeParam.OffsetY, OptimizeParam.Colors, OptimizeParam.DownsampleMode,
        OptimizeParam.BorderFraction, OptimizeParam.AlphaThreshold, OptimizeParam.SnapStep, OptimizeParam.OutlineDarkness,
        OptimizeParam.Cleanup, OptimizeParam.Dither, OptimizeParam.OutlineFit, OptimizeParam.VectorTolerance, OptimizeParam.VectorBlob,
        OptimizeParam.PaletteColorSource, OptimizeParam.PaletteMethod, OptimizeParam.EdgeWeight, OptimizeParam.ColorDistance, OptimizeParam.ShadeFamily, OptimizeParam.DistinctHues,
        OptimizeParam.SnapOutline, OptimizeParam.StickerBorder, OptimizeParam.LocalGrid,
    ];

    /// <summary>점수 항목별 비중 칸: 이름, 풍선, 읽기, 바꾼 사본.</summary>
    private static readonly (Func<string> Label, Func<string?> Tip, Func<ScoreWeights, double> Get, Func<ScoreWeights, double, ScoreWeights> With)[] WeightFields =
    [
        (() => UiStrings.Pixelate_Score_Fidelity, () => null, w => w.Fidelity, (w, v) => w with { Fidelity = v }),
        (() => UiStrings.Pixelate_Score_Orphans, () => null, w => w.Orphans, (w, v) => w with { Orphans = v }),
        (() => UiStrings.Pixelate_Score_OutlineGaps, () => null, w => w.OutlineGaps, (w, v) => w with { OutlineGaps = v }),
        (() => UiStrings.Pixelate_Score_Colors, () => null, w => w.Colors, (w, v) => w with { Colors = v }),
        (() => UiStrings.Pixelate_Score_Consistency, () => null, w => w.Consistency, (w, v) => w with { Consistency = v }),
        (() => UiStrings.Pixelate_Score_Grid, () => null, w => w.Grid, (w, v) => w with { Grid = v }),
        (() => UiStrings.Pixelate_Score_OutlineThin, () => null, w => w.OutlineThin, (w, v) => w with { OutlineThin = v }),
        (() => UiStrings.Pixelate_Score_ColorShift, () => UiStrings.Pixelate_Score_ColorShift_Tip, w => w.ColorShift, (w, v) => w with { ColorShift = v }),
        (() => UiStrings.Pixelate_Score_BlockRepetition, () => UiStrings.Pixelate_Score_BlockRepetition_Tip, w => w.BlockRepetition, (w, v) => w with { BlockRepetition = v }),
    ];

    private readonly PixelateOptions _options;
    private readonly Func<PixelateSettings?>? _currentSettings;
    private readonly ListBox _list;
    private readonly Button _duplicate, _rename, _favorite, _delete;
    private readonly Control _editor;
    private readonly TextBlock _title;
    private readonly (OptimizeParam Param, CheckBox Box)[] _checks;
    /// <summary>켤 수 있는 항목 모두 선택 ↔ 모두 해제(글자는 지금 상태를 따라감).</summary>
    private readonly Button _all;
    private readonly NumericUpDown _trials, _seconds, _hueWeight;
    private readonly ComboBox _distance;
    private readonly NumericUpDown[] _weights;
    /// <summary>고른 프리셋을 칸에 불러오는 중(칸 바뀜을 프리셋에 쓰지 않음).</summary>
    private bool _loading;
    /// <summary>여러 항목 체크를 한꺼번에 바꾸는 중.</summary>
    private bool _syncing;
    /// <summary>목록을 다시 채우는 중(고름 바뀜을 알리지 않음).</summary>
    private bool _refreshing;

    public OptimizePresetsPanel(PixelateOptions options, Func<PixelateSettings?>? currentSettings = null)
    {
        _options = options;
        _currentSettings = currentSettings;
        ColumnDefinitions = new ColumnDefinitions("260,*");

        // ── 왼쪽: 프리셋 목록
        _list = new ListBox { Background = AppTheme.InputBrush, Foreground = AppTheme.TextBrush };
        _list.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            OnSelected();
        };
        var add = Ui.IconButton(VectorIcon.Plus, UiStrings.OptimizePreset_Add, (_, _) => Add());
        _duplicate = Ui.IconButton(VectorIcon.Copy, UiStrings.OptimizePreset_Duplicate, (_, _) => Duplicate());
        _rename = Ui.IconButton(VectorIcon.Pencil, UiStrings.OptimizePreset_Rename, (_, _) => Rename());
        _favorite = Ui.IconButton(VectorIcon.Star, UiStrings.OptimizePreset_AddFavorite, (_, _) => ToggleFavorite());
        _delete = Ui.IconButton(VectorIcon.Trash, UiStrings.OptimizePreset_Delete, (_, _) => Delete());
        ToolTip.SetShowOnDisabled(_delete, true);
        var header = Ui.Label(UiStrings.OptimizePreset_List, bold: true);
        header.TextWrapping = TextWrapping.Wrap;
        var buttons = Ui.Row(add, _duplicate, _rename, _favorite, _delete);
        var left = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 8, 4, 8) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(header);
        left.Children.Add(buttons);
        left.Children.Add(_list);

        // ── 오른쪽: 고른 프리셋의 항목·예산·점수 기준
        _title = Ui.Label("", bold: true);
        _checks = [.. Order.Select(p => (p, WithTip(p, Ui.Check(ParamName(p)))))];
        foreach (var (_, box) in _checks)
            box.IsCheckedChanged += (_, _) =>
            {
                if (_loading || _syncing) return;
                Store();
                UpdateChecks();
            };
        var checks = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (_, box) in _checks) checks.Children.Add(box);
        _all = Ui.Button(UiStrings.Pixelate_Opt_SelectAll, (_, _) => ToggleAll()).Tip(UiStrings.Pixelate_Opt_SelectAllTip);
        _trials = Ui.Num(OptimizePreset.MinBudget, OptimizePreset.MaxTrials, OptimizePreset.DefaultTrials, 64, 0, 10).Presets(100, 200, 500, 1000, 2000, 5000);
        _seconds = Ui.Num(OptimizePreset.MinBudget, OptimizePreset.MaxSeconds, OptimizePreset.DefaultSeconds, 64, 0, 5).Presets(15, 30, 60, 120, 300, 600);
        _distance = Ui.Combo(170, UiStrings.Pixelate_Pal_Distance_OkLab, UiStrings.Pixelate_Pal_Distance_Ciede2000, UiStrings.Pixelate_Pal_Distance_OkLchHue);
        _hueWeight = Ui.Num((decimal)PixelateRanges.HueWeight.Min, (decimal)PixelateRanges.HueWeight.Max, 2, 64, 2, 0.25m);
        _weights = [.. WeightFields.Select(f => Ui.Num(0, 10, 1, 64, 1, 0.5m).Tip(f.Tip()))];
        var weightGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", (WeightFields.Length + 1) / 2))),
            Margin = new Thickness(4),
        };
        for (int i = 0; i < _weights.Length; i++)
        {
            var label = Ui.Label(WeightFields[i].Label());
            SetRow(label, i / 2);
            SetColumn(label, i % 2 * 2);
            SetRow(_weights[i], i / 2);
            SetColumn(_weights[i], i % 2 * 2 + 1);
            weightGrid.Children.Add(label);
            weightGrid.Children.Add(_weights[i]);
        }
        _distance.SelectionChanged += (_, _) =>
        {
            _hueWeight.IsEnabled = SelectedDistance() == ColorDistanceMode.OkLchHue;
            Store();
        };
        foreach (var n in new[] { _trials, _seconds, _hueWeight }.Concat(_weights))
            n.ValueChanged += (_, _) => Store();
        var note = new TextBlock { Text = UiStrings.Pixelate_Opt_Note, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, Margin = new Thickness(3, 0), TextWrapping = TextWrapping.Wrap };
        _editor = new Border
        {
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = Ui.Stack(
                    _title,
                    Ui.Row(_all, Ui.Label(UiStrings.Pixelate_Opt_Trials), _trials, Ui.Label(UiStrings.Pixelate_Opt_Seconds), _seconds),
                    checks,
                    Ui.Label(UiStrings.Pixelate_Opt_ScoreCriteria, bold: true),
                    note,
                    Ui.Row(Ui.Label(UiStrings.Pixelate_Opt_ScoreDistance), _distance, _hueWeight),
                    Ui.Label(UiStrings.Pixelate_Opt_ScoreWeights, bold: true),
                    weightGrid),
            },
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(4, 8, 8, 8),
        };
        SetColumn(_editor, 1);
        Children.Add(left);
        Children.Add(_editor);
        Refresh(_options.OptimizePresets.FirstOrDefault());
    }

    /// <summary>프리셋을 고치거나(값·체크·이름·즐겨찾기) 더하거나 지웠을 때.</summary>
    public event EventHandler? Changed;

    /// <summary>목록에서 고른 프리셋이 바뀌었을 때.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>목록에서 고른 프리셋(없으면 null).</summary>
    public OptimizePreset? SelectedPreset => (_list.SelectedItem as ListBoxItem)?.Tag as OptimizePreset;

    /// <summary>지금 설정(<c>currentSettings</c>)이 바뀌었을 때 쓸 수 있는 항목을 다시 맞춘다.</summary>
    public void RefreshApplicable() => UpdateChecks();

    private static string ParamName(OptimizeParam p) => p switch
    {
        OptimizeParam.Scale => UiStrings.Pixelate_Opt_Scale,
        OptimizeParam.OffsetX => UiStrings.Pixelate_Opt_OffsetX,
        OptimizeParam.OffsetY => UiStrings.Pixelate_Opt_OffsetY,
        OptimizeParam.Colors => UiStrings.Pixelate_Opt_Colors,
        OptimizeParam.ColorDistance => UiStrings.Pixelate_Pal_Distance,
        OptimizeParam.DistinctHues => UiStrings.Pixelate_Pal_Distinct,
        OptimizeParam.ShadeFamily => UiStrings.Pixelate_Pal_ShadeFamily,
        OptimizeParam.DownsampleMode => UiStrings.Pixelate_Opt_Downsample,
        OptimizeParam.BorderFraction => UiStrings.Pixelate_Opt_Border,
        OptimizeParam.AlphaThreshold => UiStrings.Pixelate_Opt_Alpha,
        OptimizeParam.SnapStep => UiStrings.Pixelate_Opt_Snap,
        OptimizeParam.OutlineDarkness => UiStrings.Pixelate_Opt_Darkness,
        OptimizeParam.Cleanup => UiStrings.Pixelate_Opt_Cleanup,
        OptimizeParam.Dither => UiStrings.Pixelate_Opt_Dither,
        OptimizeParam.PaletteColorSource => UiStrings.Pixelate_Opt_ColorSource,
        OptimizeParam.PaletteMethod => UiStrings.Pixelate_Opt_PaletteMethod,
        OptimizeParam.EdgeWeight => UiStrings.Pixelate_Opt_EdgeWeight,
        OptimizeParam.SnapOutline => UiStrings.Pixelate_Opt_SnapOutline,
        OptimizeParam.StickerBorder => UiStrings.Pixelate_Bg_Sticker,
        OptimizeParam.LocalGrid => UiStrings.Pixelate_Opt_LocalGrid,
        OptimizeParam.VectorTolerance => UiStrings.Pixelate_Clean_VectorTolerance,
        OptimizeParam.VectorBlob => UiStrings.Pixelate_Clean_VectorBlob,
        _ => UiStrings.Pixelate_Opt_OutlineFit,
    };

    /// <summary>항목 체크의 도움말 풍선(쓸 수 있는 조건).</summary>
    private static CheckBox WithTip(OptimizeParam p, CheckBox box) => p switch
    {
        OptimizeParam.OffsetX or OptimizeParam.OffsetY => box.Tip(UiStrings.Pixelate_Opt_OffsetTip),
        OptimizeParam.OutlineFit => box.Tip(UiStrings.Pixelate_Opt_FitTip),
        OptimizeParam.LocalGrid => box.Tip(UiStrings.Pixelate_Opt_LocalGridTip),
        OptimizeParam.VectorTolerance or OptimizeParam.VectorBlob => box.Tip(UiStrings.Pixelate_Opt_VectorTip),
        OptimizeParam.PaletteColorSource or OptimizeParam.PaletteMethod or OptimizeParam.EdgeWeight => box.Tip(UiStrings.Pixelate_Opt_PaletteTip),
        OptimizeParam.ColorDistance => box.Tip(UiStrings.Pixelate_Opt_ColorDistanceTip),
        OptimizeParam.ShadeFamily => box.Tip(UiStrings.Pixelate_Pal_ShadeFamily_Tip),
        OptimizeParam.DistinctHues => box.Tip(UiStrings.Pixelate_Pal_Distinct_Tip),
        _ => box,
    };

    private static int DistanceIndex(ColorDistanceMode mode) => Math.Max(0, Array.IndexOf(Enum.GetValues<ColorDistanceMode>(), mode));

    private ColorDistanceMode SelectedDistance()
    {
        var values = Enum.GetValues<ColorDistanceMode>();
        return values[Math.Clamp(_distance.SelectedIndex, 0, values.Length - 1)];
    }

    // ───────────── 목록 ─────────────

    /// <summary>목록을 다시 채우고 <paramref name="select"/>(없으면 첫 프리셋)를 고른다.</summary>
    private void Refresh(OptimizePreset? select)
    {
        _refreshing = true;
        _list.Items.Clear();
        foreach (var preset in _options.OptimizePresets)
        {
            var enabled = new CheckBox { IsChecked = preset.Enabled, VerticalAlignment = VerticalAlignment.Center }.Tip(UiStrings.OptimizePreset_EnabledTip);
            enabled.IsCheckedChanged += (_, _) =>
            {
                preset.Enabled = enabled.On();
                Changed?.Invoke(this, EventArgs.Empty);
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(enabled);
            if (preset.Favorite)
            {
                var star = Ui.Icon(VectorIcon.Star, 13, filled: true);
                star.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(star);
            }
            row.Children.Add(new TextBlock { Text = preset.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            _list.Items.Add(new ListBoxItem { Content = row, Tag = preset });
        }
        int index = select is null ? -1 : _options.OptimizePresets.IndexOf(select);
        _list.SelectedIndex = _options.OptimizePresets.Count == 0 ? -1 : Math.Max(0, index);
        _refreshing = false;
        OnSelected();
    }

    private void OnSelected()
    {
        LoadEditor();
        UpdateButtons();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateButtons()
    {
        var selected = SelectedPreset;
        bool has = selected is not null;
        _duplicate.IsEnabled = _rename.IsEnabled = _favorite.IsEnabled = has;
        _delete.IsEnabled = has && _options.OptimizePresets.Count > 1;
        _favorite.Tip(selected?.Favorite == true ? UiStrings.OptimizePreset_RemoveFavorite : UiStrings.OptimizePreset_AddFavorite);
    }

    /// <summary>즐겨찾기를 앞으로(같은 쪽 안에서는 순서 그대로) — 목록 순서가 도는 순서·후보 순서다.</summary>
    private void SortFavoritesFirst()
    {
        var sorted = _options.OptimizePresets.OrderByDescending(p => p.Favorite).ToList();
        _options.OptimizePresets.Clear();
        _options.OptimizePresets.AddRange(sorted);
    }

    /// <summary>이름을 묻는다(빈 이름·취소 = null). 다른 프리셋과 같은 이름(대소문자 무시)이면 알리고 다시 묻는다.</summary>
    private string? AskName(string title, string initial, OptimizePreset? renaming)
    {
        while (true)
        {
            string? name = InputDialog.Ask(this, title, UiStrings.OptimizePreset_NamePrompt, initial)?.Trim();
            if (string.IsNullOrEmpty(name)) return null;
            if (!_options.OptimizePresets.Any(p => !ReferenceEquals(p, renaming) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return name;
            Ui.Warn(this, UiStrings.FormatOptimizePreset_DuplicateName(name: name), title);
            initial = name;
        }
    }

    /// <summary>다른 프리셋과 겹치지 않는 이름: <paramref name="name"/>, 겹치면 "<paramref name="name"/> 2", "… 3"….</summary>
    private string UniqueName(string name)
    {
        bool Taken(string n) => _options.OptimizePresets.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase));
        if (!Taken(name)) return name;
        for (int i = 2; ; i++)
            if ($"{name} {i}" is var candidate && !Taken(candidate)) return candidate;
    }

    private void Add()
    {
        if (AskName(UiStrings.OptimizePreset_Add, UniqueName(UiStrings.OptimizePreset_NewName), null) is not { } name) return;
        var preset = new OptimizePreset { Name = name };
        _options.OptimizePresets.Add(preset);
        Refresh(preset);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Duplicate()
    {
        if (SelectedPreset is not { } source) return;
        if (AskName(UiStrings.OptimizePreset_Duplicate, UniqueName(UiStrings.FormatOptimizePreset_CopyName(name: source.Name)), null) is not { } name) return;
        var copy = source.Clone(name);
        _options.OptimizePresets.Insert(_options.OptimizePresets.IndexOf(source) + 1, copy);
        Refresh(copy);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Rename()
    {
        if (SelectedPreset is not { } preset) return;
        if (AskName(UiStrings.OptimizePreset_Rename, preset.Name, preset) is not { } name || name == preset.Name) return;
        preset.Name = name;
        preset.BuiltIn = null; // 직접 붙인 이름은 화면 언어를 바꿔도 그대로 둔다.
        Refresh(preset);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleFavorite()
    {
        if (SelectedPreset is not { } preset) return;
        preset.Favorite = !preset.Favorite;
        SortFavoritesFirst();
        Refresh(preset);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Delete()
    {
        if (SelectedPreset is not { } preset || _options.OptimizePresets.Count <= 1) return;
        if (Ui.Ask(this, UiStrings.FormatOptimizePreset_DeleteQuestion(name: preset.Name), UiStrings.OptimizePreset_Delete,
                MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        int index = _options.OptimizePresets.IndexOf(preset);
        _options.OptimizePresets.RemoveAt(index);
        Refresh(_options.OptimizePresets[Math.Min(index, _options.OptimizePresets.Count - 1)]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ───────────── 편집 ─────────────

    /// <summary>고른 프리셋의 값을 칸에 불러온다(없으면 편집 칸을 끔).</summary>
    private void LoadEditor()
    {
        var preset = SelectedPreset;
        _editor.IsEnabled = preset is not null;
        if (preset is null) return;
        _loading = true;
        _title.Text = preset.Name;
        var chosen = (OptimizeParam)preset.Params;
        foreach (var (p, box) in _checks) box.IsChecked = chosen.HasFlag(p);
        _trials.Value = Math.Clamp(preset.Trials, OptimizePreset.MinBudget, OptimizePreset.MaxTrials);
        _seconds.Value = Math.Clamp(preset.Seconds, OptimizePreset.MinBudget, OptimizePreset.MaxSeconds);
        _distance.SelectedIndex = DistanceIndex(preset.ScoreColorDistance);
        _hueWeight.Value = (decimal)Math.Round(PixelateRanges.HueWeight.Clamp(preset.ScoreHueWeight), 2);
        for (int i = 0; i < _weights.Length; i++)
            _weights[i].Value = (decimal)Math.Clamp(WeightFields[i].Get(preset.Weights), 0, 10);
        _hueWeight.IsEnabled = preset.ScoreColorDistance == ColorDistanceMode.OkLchHue;
        _loading = false;
        UpdateChecks();
    }

    private OptimizeParam ReadParams()
        => _checks.Where(c => c.Box.On()).Aggregate(OptimizeParam.None, (a, c) => a | c.Param);

    /// <summary>칸 값을 고른 프리셋에 쓴다(불러오는 중이 아니면).</summary>
    private void Store()
    {
        if (_loading || SelectedPreset is not { } preset) return;
        preset.Params = (int)ReadParams();
        preset.Trials = (int)_trials.NumValue();
        preset.Seconds = (int)_seconds.NumValue();
        preset.ScoreColorDistance = SelectedDistance();
        preset.ScoreHueWeight = (double)_hueWeight.NumValue();
        var w = new ScoreWeights();
        for (int i = 0; i < _weights.Length; i++) w = WeightFields[i].With(w, (double)_weights[i].NumValue());
        preset.Weights = w;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 지금 설정에서 쓰지 않는 항목(<see cref="PixelateOptimizer.Applicable"/>)은 끄고(체크는 그대로 — 지금 설정이 없으면 모두 켬), "모두 선택/모두 해제" 글자를 맞춘다
    /// (외곽선 맞추기 방식은 '모두 선택'이 건드리지 않으므로 셈에서 뺀다).
    /// </summary>
    private void UpdateChecks()
    {
        var settings = _currentSettings?.Invoke();
        var applicable = settings is null ? OptimizeParam.All : PixelateOptimizer.Applicable(settings, ReadParams());
        foreach (var (p, box) in _checks) box.IsEnabled = applicable.HasFlag(p);
        var enabled = _checks.Where(c => c.Box.IsEnabled && OptimizeParam.SelectAll.HasFlag(c.Param)).ToList();
        bool all = enabled.Count > 0 && enabled.All(c => c.Box.On());
        _all.Content = all ? UiStrings.Pixelate_Opt_ClearAll : UiStrings.Pixelate_Opt_SelectAll;
    }

    /// <summary>
    /// 켤 수 있는 항목(<see cref="OptimizeParam.SelectAll"/> — 외곽선 맞추기 방식은 빼고)이 모두 켜져 있으면 모두 끄고, 아니면 모두 켠다(꺼진 항목은 그대로 —
    /// 단, 팔레트 색을 켜면 켤 수 있게 되는 가장자리 칸 가중은 함께 켠다). 외곽선 맞추기 방식은 사용자가 고른 방식을 지키도록 사용자가 직접 켜고 끌 때만 바뀐다.
    /// </summary>
    private void ToggleAll()
    {
        var enabled = _checks.Where(c => c.Box.IsEnabled && OptimizeParam.SelectAll.HasFlag(c.Param)).ToList();
        bool on = !(enabled.Count > 0 && enabled.All(c => c.Box.On()));
        var chosen = ReadParams();
        var settings = _currentSettings?.Invoke();
        var reachable = !on ? OptimizeParam.None
            : settings is null ? OptimizeParam.All
            : PixelateOptimizer.Applicable(settings, OptimizeParam.SelectAll | (chosen & OptimizeParam.OutlineFit));
        _syncing = true;
        foreach (var (p, box) in _checks)
            if (OptimizeParam.SelectAll.HasFlag(p) && (box.IsEnabled || reachable.HasFlag(p))) box.IsChecked = on;
        _syncing = false;
        Store();
        UpdateChecks();
    }
}
