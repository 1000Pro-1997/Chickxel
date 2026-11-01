using System.Diagnostics;
using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>
/// 효과 만들기(설계 <c>효과-만들기-설계.md</c>): 개인 레시피(기본 효과 조합)와 스크립트 효과(JS)를 한 창에서 만들고, 올릴 때 굽는 것과 같은
/// 미리보기(<see cref="AssetPreview.Render"/>, 병아리 견본, <see cref="AssetPreview.PackageFrames"/>칸)로 확인하고, 개인 에셋으로 저장·지금 레이어에 넣기·
/// 올리기(게시 창으로 이어짐)를 한다. 레시피 효과는 병아리를 그린 임시 프로젝트에 효과 창을 띄워 고친다(효과 창 컨트롤을 그대로 씀).
/// 창은 하나만 열리고(모달이 아님) 메인 편집기와 함께 쓴다.
/// </summary>
public sealed class EffectMakerWindow : Window
{
    private enum Kind
    {
        None,
        Recipe,
        Script,
    }

    private const int PreviewBox = 300;
    private static EffectMakerWindow? s_window;

    private readonly EditorContext _ctx;
    private readonly ListBox _list = new() { Margin = new Thickness(3) };
    private readonly ContentControl _center = new();
    private readonly TextBox _name, _description;
    private readonly ListBox _effectList = new() { Height = 200, Margin = new Thickness(3) };
    private readonly ComboBox _frameRange;
    private readonly TextBlock _scriptName = Ui.Label("", bold: true);
    private readonly ScriptCodeEditor _code = new();
    private readonly StackPanel _scriptSettings = new() { Spacing = 2 };
    private readonly Control _recipePanel, _scriptPanel, _emptyPanel;
    private readonly MakerPreview _preview = new() { Width = PreviewBox, Height = PreviewBox };
    private readonly ComboBox _chick = Ui.Combo(160);
    private readonly Button _play;
    private readonly CheckBox _original;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush, MaxWidth = PreviewBox };
    private readonly StackPanel _errors = new() { Spacing = 2 };
    private readonly Button _save, _insert, _upload;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(AssetPreview.PackageFrameMs) };
    private readonly Action _onAssetsChanged;

    private Kind _kind;
    /// <summary>고치는 개인 에셋(아직 저장하지 않은 새 효과면 null).</summary>
    private string? _entryId;
    private EffectRecipe _recipe = new();
    private string _source = "";
    private ScriptEffectProgram? _program;
    private readonly Dictionary<string, object> _previewValues = new(StringComparer.Ordinal);
    private bool _loading, _syncingList;
    /// <summary>
    /// 마지막으로 불러오거나 저장한 상태(새 효과면 null). 고친 것이 있는지는 이것과 견줘 정한다 — 글 상자의 TextChanged는 값을 넣은 뒤에 늦게 오므로
    /// "불러오는 중" 표시로는 막을 수 없다.
    /// </summary>
    private EffectRecipe? _savedRecipe;
    private string? _savedSource;

    private bool Dirty => _kind switch
    {
        Kind.Recipe => _savedRecipe is null || !_recipe.Equals(_savedRecipe),
        Kind.Script => _savedSource is null || _source != _savedSource,
        _ => false,
    };
    private CancellationTokenSource? _render;

    /// <summary>메인 창에 딸린 효과 만들기를 연다(효과 창 커스텀 탭·에셋 관리자처럼 메인 창 밖에서).</summary>
    public static void OpenFromAnywhere(string? assetId)
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: MainForm main })
            Open(main, ((Ai.IAiHost)main).Context, assetId);
    }

    /// <summary>효과 만들기를 연다(이미 열려 있으면 앞으로). <paramref name="assetId"/>가 개인 레시피·스크립트면 그것을 연다.</summary>
    public static void Open(Window owner, EditorContext ctx, string? assetId = null)
    {
        if (s_window is { } open)
        {
            open.Activate();
            if (assetId is not null) open.TryOpen(assetId);
            return;
        }
        AppAssets.ShowProblems(owner);
        var window = new EffectMakerWindow(ctx);
        s_window = window;
        window.Show(owner);
        if (assetId is not null) window.TryOpen(assetId);
        else if (window.Entries().FirstOrDefault() is { } first) window.TryOpen(first.Id);
    }

    private EffectMakerWindow(EditorContext ctx)
    {
        _ctx = ctx;
        Title = UiStrings.EffectMaker_Title;
        Width = 1240;
        Height = 780;
        MinWidth = 980;
        MinHeight = 600;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // ── 왼쪽: 내 효과 목록
        _list.SelectionChanged += (_, _) => OnListSelection();
        _list.ContextMenu = ListMenu();
        var newRecipe = Ui.Button(VectorIcon.Layers, UiStrings.EffectMaker_NewRecipe, (_, _) => NewRecipe(fromLayer: false));
        var fromLayer = Ui.Button(VectorIcon.Copy, UiStrings.EffectMaker_FromLayer, (_, _) => NewRecipe(fromLayer: true))
            .Tip(UiStrings.EffectMaker_FromLayerTip);
        var newScript = Ui.Button(VectorIcon.Code, UiStrings.EffectMaker_NewScript, (_, _) => NewScript(ScriptTemplates.Shine));
        var openJs = Ui.Button(VectorIcon.FolderOpen, UiStrings.ScriptEffect_OpenFile, (_, _) => OpenScriptFile());
        var left = new DockPanel { Width = 240, LastChildFill = true };
        var leftTop = Ui.Stack(Ui.Label(UiStrings.EffectMaker_MyEffects, bold: true), Ui.Row(newRecipe, fromLayer), Ui.Row(newScript, openJs));
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.Add(leftTop);
        left.Children.Add(_list);

        // ── 가운데: 레시피
        _name = new TextBox { Width = 320, Margin = new Thickness(3), MaxLength = 100 };
        _name.TextChanged += (_, _) => Edited(() => _recipe = _recipe with { Name = _name.Text ?? "" }, rerender: false);
        _description = new TextBox { Width = 460, Height = 64, Margin = new Thickness(3), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 2000 };
        _description.TextChanged += (_, _) => Edited(() => _recipe = _recipe with { Description = string.IsNullOrWhiteSpace(_description.Text) ? null : _description.Text }, rerender: false);
        _frameRange = Ui.Combo(220, UiStrings.EffectMaker_RangeAdd, UiStrings.EffectMaker_RangeAll);
        _frameRange.SelectionChanged += (_, _) => Edited(() => _recipe = _recipe with
        {
            FrameRange = _frameRange.SelectedIndex == 1 ? RecipeFrameRange.AllFrames : RecipeFrameRange.AddRange,
        }, rerender: false);
        var editEffects = Ui.Button(VectorIcon.SlidersHorizontal, UiStrings.EffectMaker_EditEffects, (_, _) => EditRecipeEffects())
            .Tip(UiStrings.EffectMaker_EditEffectsTip);
        var parameters = Ui.Button(UiStrings.RecipeParams_Edit, (_, _) => EditParameters());
        _recipePanel = Ui.Stack(
            Rows((UiStrings.EffectMaker_Name, _name), (UiStrings.EffectMaker_Description, _description), (UiStrings.EffectMaker_Range, _frameRange)),
            Ui.Group(UiStrings.EffectMaker_Effects, Ui.Stack(_effectList, Ui.Row(editEffects, parameters))),
            Hint(UiStrings.EffectMaker_RecipeHint));

        // ── 가운데: 스크립트
        _code.TextChanged += (_, _) => Edited(() => _source = _code.Text, rerender: true);
        var scriptTop = Ui.Stack(Ui.Row(_scriptName), Hint(UiStrings.ScriptEditor_Hint));
        DockPanel.SetDock(scriptTop, Dock.Top);
        var settingsBox = Ui.Group(UiStrings.EffectMaker_PreviewSettings, new ScrollViewer { Content = _scriptSettings, MaxHeight = 170 });
        DockPanel.SetDock(settingsBox, Dock.Bottom);
        var script = new DockPanel { LastChildFill = true };
        script.Children.Add(scriptTop);
        script.Children.Add(settingsBox);
        script.Children.Add(_code);
        _scriptPanel = script;
        _emptyPanel = Ui.Stack(Hint(UiStrings.EffectMaker_Empty));
        _center.Content = _emptyPanel;
        _center.Margin = new Thickness(8, 0);

        // ── 오른쪽: 미리보기·상태
        for (int i = 0; i < PreviewChicks.Ids.Count; i++)
            _chick.Items.Add(string.Format(CultureInfo.CurrentCulture, UiStrings.EffectPreset_PreviewChickTip, i + 1));
        _chick.SelectionChanged += (_, _) => ChickChanged();
        _play = Ui.Button(VectorIcon.Pause, UiStrings.EffectMaker_Pause, (_, _) => TogglePlay(), 90);
        _original = Ui.Check(UiStrings.EffectMaker_ShowOriginal);
        _original.IsCheckedChanged += (_, _) => _preview.ShowOriginal = _original.On();
        _timer.Tick += (_, _) => _preview.Step();
        var right = Ui.Stack(
            Ui.Label(UiStrings.EffectMaker_Preview, bold: true),
            _preview,
            Ui.Row(Ui.Label(UiStrings.EffectPreset_PreviewChick), _chick),
            Ui.Row(_play, _original),
            Hint(UiStrings.EffectMaker_PreviewHint),
            Ui.Group(UiStrings.EffectMaker_Status, Ui.Stack(_status, _errors)));
        right.Width = PreviewBox + 24;

        // ── 아래 줄
        _save = Ui.Button(VectorIcon.Save, UiStrings.EffectMaker_Save, (_, _) => Save()).Tip(UiStrings.EffectMaker_SaveTip);
        _insert = Ui.Button(VectorIcon.Plus, UiStrings.EffectMaker_Insert, (_, _) => InsertIntoLayer()).Tip(UiStrings.EffectMaker_InsertTip);
        _upload = Ui.Button(VectorIcon.Upload, UiStrings.EffectMaker_Upload, (_, _) => Upload()).Tip(UiStrings.EffectMaker_UploadTip);
        _upload.Background = AppTheme.AccentBrush;
        _upload.Foreground = Brushes.White;
        var close = Ui.Button(UiStrings.S_Ui_CloseOnly_11312F9A81, (_, _) => Close(), 90);
        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var rightButtons = Ui.Row(_upload, close);
        DockPanel.SetDock(rightButtons, Dock.Right);
        bottom.Children.Add(rightButtons);
        bottom.Children.Add(Ui.Row(_save, _insert));

        var columns = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        columns.Children.Add(left);
        columns.Children.Add(right);
        columns.Children.Add(_center);
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(columns);
        Content = root;

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Rerender();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                Save();
            }
        };
        _onAssetsChanged = () => Dispatcher.UIThread.Post(RebuildList);
        AppAssets.Changed += _onAssetsChanged;
        Closing += (_, e) =>
        {
            if (!ConfirmDiscard()) e.Cancel = true;
        };
        Closed += (_, _) =>
        {
            AppAssets.Changed -= _onAssetsChanged;
            _render?.Cancel();
            _timer.Stop();
            _debounce.Stop();
            s_window = null;
        };
        RebuildList();
        UpdateButtons();
    }

    // ───────────── 목록 ─────────────

    private IEnumerable<AssetEntry> Entries()
        => AppAssets.Catalog.Personal.Where(e => e.Kind is AssetKinds.EffectRecipe or AssetKinds.EffectScript);

    private void RebuildList()
    {
        _syncingList = true;
        _list.Items.Clear();
        foreach (var entry in Entries())
        {
            var label = Ui.IconLabel(entry.Kind == AssetKinds.EffectScript ? VectorIcon.Code : VectorIcon.Layers, entry.Name);
            _list.Items.Add(new ListBoxItem { Content = label, Tag = entry.Id });
        }
        SelectInList(_entryId);
        _syncingList = false;
    }

    private void SelectInList(string? id)
    {
        bool was = _syncingList;
        _syncingList = true;
        _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(item => (string?)item.Tag == id);
        _syncingList = was;
    }

    private void OnListSelection()
    {
        if (_syncingList || _list.SelectedItem is not ListBoxItem { Tag: string id } || id == _entryId) return;
        if (!ConfirmDiscard())
        {
            SelectInList(_entryId);
            return;
        }
        TryOpen(id);
    }

    private ContextMenu ListMenu()
    {
        var delete = new MenuItem { Header = UiStrings.EffectMaker_Delete, Icon = Ui.Icon(VectorIcon.Trash, 14) };
        delete.Click += (_, _) => DeleteSelected();
        var manager = new MenuItem { Header = UiStrings.EffectPreset_ShowInManager, Icon = Ui.Icon(VectorIcon.Package, 14) };
        manager.Click += (_, _) =>
        {
            if (_list.SelectedItem is ListBoxItem { Tag: string id }) AssetManagerWindow.Open(this, null, id);
        };
        return new ContextMenu { Items = { delete, manager } };
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: string id } || AppAssets.Catalog.Find(id) is not { } entry) return;
        if (Ui.Ask(this, string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_DeleteQuestion, entry.Name), UiStrings.EffectMaker_Title,
                MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        if (!AppAssets.Try(this, () => AppAssets.Store.RemovePersonal(id), UiStrings.EffectMaker_SaveError)) return;
        if (id == _entryId)
        {
            MarkSaved();
            Show(Kind.None);
        }
    }

    /// <summary>개인 레시피·스크립트를 연다(저장하지 않은 고침은 부르는 쪽이 이미 확인함).</summary>
    private void TryOpen(string id)
    {
        if (AppAssets.Catalog.Find(id) is not { Origin: AssetOrigin.Personal } entry) return;
        _entryId = id;
        switch (entry.Data)
        {
            case EffectRecipe recipe:
                LoadRecipe(recipe);
                break;
            case ScriptAsset script:
                LoadScript(script.Source);
                break;
        }
        SelectInList(id);
    }

    // ───────────── 새로 만들기 ─────────────

    private void NewRecipe(bool fromLayer)
    {
        if (!ConfirmDiscard()) return;
        var recipe = new EffectRecipe { Name = UiStrings.EffectMaker_NewRecipeName };
        if (fromLayer)
        {
            if (_ctx.Project is not { } project || _ctx.CurrentLayerObject is not { Effects.Count: > 0 } layer)
            {
                Ui.Info(this, UiStrings.EffectMaker_NoLayerEffects, UiStrings.EffectMaker_Title);
                return;
            }
            recipe = EffectRecipe.Capture("", layer.Name, layer.Effects, project);
        }
        _entryId = null;
        LoadRecipe(recipe);
        SelectInList(null);
        UpdateButtons();
    }

    private void NewScript(string source)
    {
        if (!ConfirmDiscard()) return;
        _entryId = null;
        LoadScript(source);
        SelectInList(null);
        UpdateButtons();
    }

    private void OpenScriptFile()
    {
        var path = Ui.OpenFile(this, UiStrings.ScriptEffect_OpenFileTitle, null, new FileFilter(UiStrings.ScriptEffect_FileFilter, "*.js"));
        if (path is null) return;
        try
        {
            NewScript(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, UiStrings.FormatScriptEffect_ReadError(message: ex.Message));
        }
    }

    // ───────────── 불러오기·보이기 ─────────────

    private void LoadRecipe(EffectRecipe recipe)
    {
        _loading = true;
        _recipe = recipe;
        _name.Text = recipe.Name;
        _description.Text = recipe.Description ?? "";
        _frameRange.SelectedIndex = recipe.FrameRange == RecipeFrameRange.AllFrames ? 1 : 0;
        _chick.SelectedIndex = Math.Max(0, PreviewChicks.Ids.ToList().IndexOf(recipe.PreviewChick));
        RefreshEffectList();
        _loading = false;
        _savedRecipe = _entryId is null ? null : recipe;
        Show(Kind.Recipe);
    }

    private void LoadScript(string source)
    {
        _loading = true;
        _source = ScriptEffectProgram.NormalizeSource(source);
        _code.Text = _source;
        _previewValues.Clear();
        _program = null;
        _loading = false;
        _savedSource = _entryId is null ? null : _source;
        Show(Kind.Script);
    }

    private void Show(Kind kind)
    {
        _kind = kind;
        _center.Content = kind switch { Kind.Recipe => _recipePanel, Kind.Script => _scriptPanel, _ => _emptyPanel };
        if (kind == Kind.None)
        {
            _entryId = null;
            _preview.SetFrames([], null);
            _status.Text = "";
            _errors.Children.Clear();
            SelectInList(null);
        }
        UpdateButtons();
        Rerender();
    }

    private void RefreshEffectList()
    {
        _effectList.Items.Clear();
        foreach (var effect in _recipe.Effects)
        {
            var text = effect.DisplayName + (effect.Enabled ? "" : " " + UiStrings.EffectMaker_Off);
            _effectList.Items.Add(new ListBoxItem { Content = effect.ClipToPrevious ? "    " + text : text });
        }
        if (_recipe.Effects.Count == 0) _effectList.Items.Add(new ListBoxItem { Content = UiStrings.EffectMaker_NoEffects, IsEnabled = false });
    }

    private void UpdateButtons()
    {
        bool any = _kind != Kind.None;
        _save.IsEnabled = any;
        _insert.IsEnabled = any && _ctx.Project is not null;
        _upload.IsEnabled = any;
        Title = any ? string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_TitleWith, CurrentName() + (Dirty ? " *" : "")) : UiStrings.EffectMaker_Title;
    }

    private string CurrentName() => _kind == Kind.Script ? _program?.Name ?? UiStrings.ScriptEffect_Name : _recipe.Name;

    private void Edited(Action change, bool rerender)
    {
        if (_loading) return;
        change();
        UpdateButtons();
        if (rerender || _kind == Kind.Recipe) _debounce.Start();
    }

    // ───────────── 레시피 고치기 ─────────────

    /// <summary>
    /// 병아리를 그린 임시 프로젝트(<see cref="AssetPreview.PackageFrames"/>프레임)에 레시피 효과를 넣고 효과 창을 띄운다. 확인하면 그 효과 목록을 레시피로 잡고,
    /// 이름·설명·범위·병아리는 그대로, 매개변수는 고친 목록에서도 맞으면 그대로 둔다(맞지 않으면 빼고 알림).
    /// </summary>
    private void EditRecipeEffects()
    {
        var chick = PreviewChicks.Load(_recipe.PreviewChick);
        const int Margin = 12;
        var padded = new PixelImage(chick.Width + Margin * 2, chick.Height + Margin * 2);
        padded.Blit(chick, Margin, Margin);
        var frames = Enumerable.Range(0, AssetPreview.PackageFrames).Select(_ => padded.Clone()).ToArray();
        var project = SpriteProject.FromFrames(frames, Enumerable.Repeat(AssetPreview.PackageFrameMs, frames.Length).ToArray(), _recipe.Name);
        var effects = _recipe.Instantiate(project, null).Select(effect => effect with { Recipe = null }).ToList();
        project.SetLayerEffects(0, effects);
        var scratch = new EditorContext();
        scratch.SetProject(project);
        var dialog = new LayerEffectsDialog(scratch, project.Layers[0]);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var captured = EffectRecipe.Capture(_recipe.Id, _recipe.Name, project.Layers[0].Effects, project) with
        {
            Description = _recipe.Description,
            FrameRange = _recipe.FrameRange,
            PreviewFrames = _recipe.PreviewFrames,
            PreviewChick = _recipe.PreviewChick,
        };
        var bad = RecipeParameters.Check(captured.Effects, _recipe.Parameters, (_, _) => { });
        if (bad.Count > 0) Ui.Info(this, UiStrings.EffectMaker_ParametersDropped, UiStrings.EffectMaker_Title);
        _recipe = captured.WithParameters(_recipe.Parameters.Where((_, i) => !bad.Contains(i)).ToList());
        foreach (var source in _recipe.Scripts.Values) ScriptTrustStore.TrustLocal(ScriptLibrary.Add(source));
        RefreshEffectList();
        UpdateButtons();
        Rerender();
    }

    private void EditParameters()
    {
        var dialog = new RecipeParametersDialog(_recipe);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _recipe = _recipe.WithParameters(dialog.Parameters);
        UpdateButtons();
        Rerender();
    }

    // ───────────── 미리보기 ─────────────

    /// <summary>미리보기에 그릴 효과 목록(스크립트는 해석이 되면 미리보기 설정 값의 효과 하나). 그릴 것이 없으면 빈 목록.</summary>
    private IReadOnlyList<LayerEffect> PreviewEffects()
    {
        if (_kind == Kind.Recipe) return _recipe.Effects;
        if (_kind != Kind.Script || _program is null) return [];
        // 미리보기는 이 창 안에서만: 실행 허락은 메모리에만 두고(설정 파일에 남기지 않음), 저장·넣기 때 TrustLocal로 남긴다
        string hash = ScriptLibrary.Add(_source);
        if (ScriptLibrary.TrustOf(hash) != ScriptTrust.Trusted) ScriptLibrary.SetTrust(hash, true);
        return [new ScriptEffect { ScriptHash = hash, Settings = ScriptSettingValues.Normalized(_program, _previewValues) }];
    }

    private string PreviewChickId() => _kind == Kind.Script ? _program?.PreviewChick ?? PreviewChicks.Default : _recipe.PreviewChick;

    /// <summary>원문을 해석하고(스크립트) 미리보기 칸들을 작업 스레드에서 다시 그린다. 앞 계산은 취소한다.</summary>
    private void Rerender()
    {
        _render?.Cancel();
        _errors.Children.Clear();
        if (_kind == Kind.Script) ParseScript();
        var effects = PreviewEffects();
        if (effects.Count == 0)
        {
            _preview.SetFrames([], null);
            if (_kind == Kind.Recipe) _status.Text = UiStrings.EffectMaker_NoEffects;
            return;
        }
        var chickId = PreviewChickId();
        var program = _kind == Kind.Script ? _program : null;
        var values = program is null ? null : new Dictionary<string, object>(_previewValues, StringComparer.Ordinal);
        var cancel = new CancellationTokenSource();
        _render = cancel;
        _status.Text = UiStrings.EffectMaker_Rendering;
        Task.Run(() =>
        {
            var chick = PreviewChicks.Load(chickId);
            ScriptError? runError = null;
            double? msPerFrame = null;
            ScriptExecutionPath? path = null;
            if (program is not null)
            {
                // 빠른 경로 판정(첫 읽기에 변환·컴파일)을 먼저 해 두어 아래 시간에 컴파일이 섞이지 않게 한다
                path = program.ExecutionPath;
                // 실행 오류·시간은 효과를 거치지 않고 직접 한 번 돌려 본다(효과 렌더러는 오류를 삼키고 효과를 건너뛴다)
                var probe = chick.Clone();
                var watch = Stopwatch.StartNew();
                try
                {
                    program.Render(probe, new Rectangle(0, 0, probe.Width, probe.Height),
                        new ScriptFrameContext { Time = 0.5, Progress = 0.5, FrameIndex = 5, FrameStep = 5, FrameSpan = 9 }, values, cancel.Token);
                    msPerFrame = watch.Elapsed.TotalMilliseconds;
                }
                catch (ScriptEffectException ex)
                {
                    runError = ex.Error;
                }
            }
            var frames = new List<PixelImage>();
            for (int step = 0; step < AssetPreview.PackageFrames && !cancel.IsCancellationRequested; step++)
                frames.Add(AssetPreview.Render(effects, chick, step, AssetPreview.PackageFrames));
            var original = AssetPreview.Original(effects, chick, 0, AssetPreview.PackageFrames);
            return (frames, original, runError, msPerFrame, path);
        }, cancel.Token).ContinueWith(task =>
        {
            if (cancel.IsCancellationRequested || task.IsFaulted || task.IsCanceled || !ReferenceEquals(_render, cancel)) return;
            var (frames, original, runError, ms, path) = task.Result;
            _preview.SetFrames(frames, original);
            _status.Text = runError is not null ? UiStrings.EffectMaker_RunFailed
                : ms is { } t ? string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_FrameTime, t.ToString("0.00", CultureInfo.CurrentCulture))
                : UiStrings.EffectMaker_Ready;
            if (path is not null) AddPath(path);
            if (runError is not null)
            {
                AddError(runError);
                _code.SetErrors([runError]);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// 실행 경로 줄: 빠른 경로(변환해 컴파일한 코드)인지, Jint로 도는지와 그 까닭(누르면 그 줄로). 작성자가 빠른 경로에 맞게 고칠 수 있게 보인다(설계 8장).
    /// </summary>
    private void AddPath(ScriptExecutionPath path)
    {
        var text = new SelectableTextBlock
        {
            Text = path.Compiled ? UiStrings.EffectMaker_PathFast
                : path.Line > 0 ? string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_PathJintAt, path.Line, path.Reason)
                : string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_PathJint, path.Reason),
            TextWrapping = TextWrapping.Wrap,
            Foreground = path.Compiled ? AppTheme.DimTextBrush : AppTheme.WarningBrush,
            MaxWidth = PreviewBox,
        };
        ToolTip.SetTip(text, UiStrings.EffectMaker_PathTip);
        if (!path.Compiled && path.Line > 0)
        {
            text.Cursor = new Cursor(StandardCursorType.Hand);
            text.PointerPressed += (_, _) => _code.GoTo(path.Line, Math.Max(1, path.Column));
        }
        _errors.Children.Add(text);
    }

    /// <summary>원문을 해석해 오류를 보이고, 되면 이름·설정 칸을 맞춘다.</summary>
    private void ParseScript()
    {
        var parsed = ScriptEffectProgram.Parse(_source);
        _code.SetErrors(parsed.Errors);
        foreach (var error in parsed.Errors) AddError(error);
        if (!parsed.Success)
        {
            _status.Text = UiStrings.EffectMaker_ParseFailed;
            _scriptName.Text = UiStrings.ScriptEffect_Name;
            _program = null;
            return;
        }
        bool schemaChanged = _program is null || !_program.Settings.SequenceEqual(parsed.Program.Settings);
        _program = parsed.Program;
        _scriptName.Text = _program.Author is { } author ? $"{_program.Name}  ·  {author}" : _program.Name;
        _loading = true;
        _chick.SelectedIndex = Math.Max(0, PreviewChicks.Ids.ToList().IndexOf(_program.PreviewChick));
        _loading = false;
        if (schemaChanged) BuildScriptSettings();
        UpdateButtons();
    }

    /// <summary>미리보기 설정 칸: 선언의 settings로 만든 칸(값은 미리보기에만 쓰고 원문의 default는 바꾸지 않음).</summary>
    private void BuildScriptSettings()
    {
        _scriptSettings.Children.Clear();
        if (_program is null) return;
        var values = _program.NormalizeSettings(_previewValues);
        if (_program.Settings.Count == 0) _scriptSettings.Children.Add(Hint(UiStrings.ScriptEffect_NoSettings));
        foreach (var setting in _program.Settings)
        {
            string key = setting.Key;
            void Set(object value)
            {
                _previewValues[key] = value;
                _debounce.Stop();
                _debounce.Start();
            }
            Control editor = setting.Type switch
            {
                ScriptSettingType.Int or ScriptSettingType.Number
                    => LayerEffectsDialog.ScriptNumberEditor(setting, Convert.ToDouble(values[key], CultureInfo.InvariantCulture), v => Set(v)),
                ScriptSettingType.Bool => CheckEditor((bool)values[key], v => Set(v)),
                ScriptSettingType.Color => ColorEditor((Color32)values[key], v => Set(v)),
                _ => ChoiceEditor(setting, (string)values[key], v => Set(v)),
            };
            var label = Ui.Label(setting.Label);
            label.Width = 110;
            label.VerticalAlignment = VerticalAlignment.Center;
            _scriptSettings.Children.Add(Ui.Row(label, editor));
        }
    }

    private static Control CheckEditor(bool value, Action<bool> changed)
    {
        var box = Ui.Check("", value);
        box.IsCheckedChanged += (_, _) => changed(box.On());
        return box;
    }

    private static Control ColorEditor(Color32 value, Action<Color32> changed)
    {
        var field = new ColorField(value);
        field.ColorChanged += (_, _) => changed(field.Color);
        return field;
    }

    private static Control ChoiceEditor(ScriptSetting setting, string value, Action<string> changed)
    {
        var combo = Ui.Combo(160, setting.Choices.Select(c => c.Label).ToArray());
        combo.SelectedIndex = Math.Max(0, setting.Choices.ToList().FindIndex(c => c.Value == value));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) changed(setting.Choices[combo.SelectedIndex].Value);
        };
        return combo;
    }

    private void AddError(ScriptError error)
    {
        var text = new SelectableTextBlock { Text = ScriptStatusText.Error(error), TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.UnsavedBrush, MaxWidth = PreviewBox };
        if (error.Line > 0)
        {
            text.Cursor = new Cursor(StandardCursorType.Hand);
            text.PointerPressed += (_, _) => _code.GoTo(error.Line, error.Column);
        }
        _errors.Children.Add(text);
    }

    private void ChickChanged()
    {
        if (_loading || _chick.SelectedIndex < 0 || _kind == Kind.None) return;
        string id = PreviewChicks.Ids[_chick.SelectedIndex];
        if (_kind == Kind.Recipe)
        {
            if (_recipe.PreviewChick == id) return;
            _recipe = _recipe with { PreviewChick = id };
            UpdateButtons();
            Rerender();
            return;
        }
        if (_program?.PreviewChick == id) return;
        // 스크립트는 원문 선언의 previewChick이 병아리를 정한다: 원문을 고친다
        if (ScriptDeclarationEdit.SetPreviewChick(_source, id) is not { } edited)
        {
            Ui.Info(this, UiStrings.EffectMaker_FixSourceFirst, UiStrings.EffectMaker_Title);
            _loading = true;
            _chick.SelectedIndex = Math.Max(0, PreviewChicks.Ids.ToList().IndexOf(PreviewChickId()));
            _loading = false;
            return;
        }
        _code.Text = edited; // TextChanged → Edited → 다시 그리기
    }

    private void TogglePlay()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _play.Content = Ui.IconLabel(VectorIcon.Play, UiStrings.EffectMaker_Play);
        }
        else
        {
            _timer.Start();
            _play.Content = Ui.IconLabel(VectorIcon.Pause, UiStrings.EffectMaker_Pause);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _timer.Start();
    }

    // ───────────── 저장·넣기·올리기 ─────────────

    /// <summary>개인 에셋으로 저장한다. 스크립트는 오류 없이 해석될 때만. 되면 true.</summary>
    private bool Save()
    {
        if (_kind == Kind.None) return false;
        AssetEntry? saved = null;
        bool ok;
        if (_kind == Kind.Recipe)
        {
            if (string.IsNullOrWhiteSpace(_recipe.Name))
            {
                Ui.Info(this, UiStrings.EffectMaker_NeedName, UiStrings.EffectMaker_Title);
                return false;
            }
            var recipe = _recipe with { Id = _entryId ?? "" };
            ok = AppAssets.Try(this, () => saved = AppAssets.Store.SavePersonalRecipe(recipe), UiStrings.EffectMaker_SaveError);
        }
        else
        {
            if (_program is null)
            {
                Ui.Info(this, UiStrings.EffectMaker_FixSourceFirst, UiStrings.EffectMaker_Title);
                return false;
            }
            ScriptTrustStore.TrustLocal(ScriptLibrary.Add(_source));
            ok = AppAssets.Try(this, () => saved = AppAssets.Store.SavePersonalScript(_source, _entryId), UiStrings.EffectMaker_SaveError);
        }
        if (!ok || saved is null) return false;
        _entryId = saved.Id;
        if (saved.Data is EffectRecipe stored) _recipe = stored;
        MarkSaved();
        RebuildList();
        UpdateButtons();
        return true;
    }

    /// <summary>지금 메인 편집기의 레이어 끝에 효과를 넣는다(실행취소 한 단계, 레시피 범위가 "추가 범위"면 모든 프레임).</summary>
    private void InsertIntoLayer()
    {
        if (_ctx.Project is not { } project) return;
        int index = _ctx.CurrentLayer;
        if (index < 0 || index >= project.Layers.Count) return;
        if (LayerEffectsDialog.OpenCount > 0)
        {
            Ui.Info(this, UiStrings.EffectMaker_CloseEffectsFirst, UiStrings.EffectMaker_Title);
            return;
        }
        if (project.IsLayerLocked(index))
        {
            Ui.Info(this, UiStrings.EffectMaker_LayerLocked, UiStrings.EffectMaker_Title);
            return;
        }
        EffectRecipe recipe;
        if (_kind == Kind.Recipe) recipe = _recipe;
        else if (_program is not null)
        {
            ScriptTrustStore.TrustLocal(ScriptLibrary.Add(_source));
            recipe = ScriptAsset.From("", _program).ToRecipe();
        }
        else
        {
            Ui.Info(this, UiStrings.EffectMaker_FixSourceFirst, UiStrings.EffectMaker_Title);
            return;
        }
        var added = recipe.Instantiate(project, null);
        if (_kind == Kind.Script && added is [ScriptEffect script] && _program is not null)
            added = [script with { Settings = ScriptSettingValues.Normalized(_program, _previewValues) }];
        var list = project.Layers[index].Effects.Concat(added).ToList();
        project.Edit(string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_InsertUndo, CurrentName()), ProjectChange.Layers,
            () => project.SetLayerEffects(index, list));
        _ctx.SetCurrentLayer(index);
    }

    /// <summary>저장한 뒤 이 효과 하나를 고른 게시 창을 연다(이름·설명·태그·라이선스·버전을 거기서 적고 올림 — 미리보기는 저절로 구움).</summary>
    private async void Upload()
    {
        if (!Save() || _entryId is not { } id) return;
        await AssetPublishDialog.RunAsync(this, [id]);
    }

    /// <summary>저장하지 않은 고침이 있으면 저장·버림·취소를 묻는다. 계속해도 되면 true.</summary>
    private bool ConfirmDiscard()
    {
        if (!Dirty) return true;
        var answer = Ui.Ask(this, string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_UnsavedQuestion, CurrentName()), UiStrings.EffectMaker_Title,
            MessageButtons.YesNoCancel, MessageIcon.Question, okText: UiStrings.EffectMaker_Save, noText: UiStrings.EffectMaker_Discard);
        if (answer == DialogResult.Cancel || answer == DialogResult.None) return false;
        if (answer == DialogResult.Yes) return Save();
        MarkSaved(); // 버림: 다음 확인(창 닫기)에서 다시 묻지 않게
        return true;
    }

    /// <summary>지금 상태를 저장한(또는 버리기로 한) 상태로 기억한다.</summary>
    private void MarkSaved()
    {
        _savedRecipe = _recipe;
        _savedSource = _source;
    }

    // ───────────── 조각 ─────────────

    private static Control Rows(params (string Label, Control Field)[] rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = Ui.Label(rows[i].Label);
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(3, 7, 8, 3);
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            rows[i].Field.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(label);
            grid.Children.Add(rows[i].Field);
        }
        return grid;
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, Margin = new Thickness(3, 2, 3, 4),
    };

    /// <summary>미리보기: 체크무늬 위에 정수 배로 키운 칸(재생은 창의 타이머가 넘김), 원본 보기면 효과 없는 견본.</summary>
    private sealed class MakerPreview : Control
    {
        private WriteableBitmap?[] _frames = [];
        private WriteableBitmap? _original;
        private int _index;
        private bool _showOriginal;

        public bool ShowOriginal
        {
            get => _showOriginal;
            set
            {
                _showOriginal = value;
                InvalidateVisual();
            }
        }

        public void SetFrames(IReadOnlyList<PixelImage> frames, PixelImage? original)
        {
            foreach (var frame in _frames) frame?.Dispose();
            _original?.Dispose();
            _frames = frames.Select(ImageUtil.ToBitmap).ToArray<WriteableBitmap?>();
            _original = original is null ? null : ImageUtil.ToBitmap(original);
            _index = 0;
            InvalidateVisual();
        }

        public void Step()
        {
            if (_frames.Length <= 1 || _showOriginal) return;
            _index = (_index + 1) % _frames.Length;
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            var b = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.PanelBrush, b);
            g.DrawRectangle(new Pen(AppTheme.BorderBrush, 1), b.Deflate(0.5));
            var bmp = _showOriginal ? _original : _frames.Length > 0 ? _frames[Math.Min(_index, _frames.Length - 1)] : null;
            if (bmp is null) return;
            var area = b.Deflate(10);
            var src = new Rectangle(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
            double fit = Math.Min(area.Width / src.Width, area.Height / src.Height);
            ImageUtil.DrawFitted(g, bmp, src, area, fit >= 1 ? (int)Math.Floor(fit) : 0, checker: true);
        }
    }
}
