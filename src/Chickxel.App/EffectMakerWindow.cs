using System.Diagnostics;
using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>
/// 효과 만들기(설계 <c>효과-만들기-설계.md</c>): 스크립트 효과(JS)를 만들고, 올릴 때 굽는 것과 같은 미리보기(<see cref="AssetPreview.Render"/>,
/// 병아리 견본, 선언의 <c>previewFrames</c>칸 — 미리보기 아래 칸 띠에서 칸마다 볼 수 있음)로 확인하고, 개인 스크립트 에셋으로 저장·지금 레이어에 넣기·검증·올리기(게시 창으로 이어짐)를 한다.
/// 세 칸: 왼쪽은 내가 만든 효과 목록(모두 보기를 켜면 복사본·설치한 패키지의 스크립트도)과 제목·설명 칸, 가운데는 원문, 오른쪽은 미리보기·상태.
/// 제목·설명·병아리·프레임 수 칸은 원문 선언의 <c>name</c>·<c>description</c>·<c>previewChick</c>·<c>previewFrames</c>를 고친다(원문 하나가 효과 하나 — 어느 앱에서나 같은 효과).
/// 미리보기 설정 값의 "기본값으로"는 지금 값을 선언 settings의 <c>default</c>로 쓰고, "초기화"는 값을 원문의 기본값으로 되돌린다.
/// 기본 효과를 합친 레시피는 여기서 만들지 않는다(효과 창의 "전체 효과 저장" — 내 컴퓨터에서만 쓰고 온라인에 올리지 않음).
/// 창은 하나만 열리고(모달이 아님) 메인 편집기와 함께 쓴다.
/// "정보" 옆 규칙 단추는 효과 규칙·API 창(<see cref="Ai.ScriptGuideWindow"/> — 복사해 다른 AI에 붙여 넣음)을, "미리보기" 옆 AI 단추는 창 오른쪽에
/// AI 칸(<see cref="Ai.EffectMakerAiPanel"/>)을 연다. AI는 효과 만들기 도구(<c>get_effect_maker_source</c> 등)로 이 창의 원문만 고친다.
/// </summary>
public sealed class EffectMakerWindow : Window
{
    private const int PreviewBox = 300;
    private const int ChickScale = 2;
    /// <summary>AI 칸 너비(창 오른쪽에 한 칸 더 — 여는 만큼 창이 넓어짐).</summary>
    private const int AiPanelWidth = 380;
    /// <summary>AI 칸을 닫았을 때의 창 최소 너비(열면 칸 너비만큼 더함).</summary>
    private const int BaseMinWidth = 980;
    private static EffectMakerWindow? s_window;

    private readonly EditorContext _ctx;
    private readonly ListBox _list = new() { Margin = new Thickness(3) };
    private readonly CheckBox _showAll;
    private readonly TextBox _title, _description;
    /// <summary>제목·설명 칸이 고치는 언어 이름(기본 선언 값이 아닌 번역을 고칠 때, 또는 선언에 <c>language</c>가 있을 때).</summary>
    private readonly TextBlock _titleLanguage = LanguageCaption(), _descriptionLanguage = LanguageCaption();
    /// <summary>선언 <c>tags</c>(올릴 때 태그) 칩 편집기.</summary>
    private readonly TagChipEditor _tags = new(double.NaN);
    /// <summary>제목·설명 칸이 고치는 번역 언어(<see cref="ScriptTranslations.EditLanguage"/>). null이면 선언의 기본 name·description.</summary>
    private string? _infoLanguage;
    private readonly ContentControl _center = new();
    private readonly ScriptCodeEditor _code = new();
    private readonly StackPanel _scriptSettings = new() { Spacing = 2 };
    private readonly Control _scriptPanel, _emptyPanel;
    private readonly MakerPreview _preview = new() { Width = PreviewBox, Height = PreviewBox };
    private readonly ComboBox _chick = new() { Width = 180, Margin = new Thickness(3) };
    private readonly Button _play, _resetValues, _setDefaults, _languages;
    private readonly CheckBox _original;
    /// <summary>미리보기 칸 수(선언의 <c>previewFrames</c>를 고침).</summary>
    private readonly NumericUpDown _frameCount;
    /// <summary>미리보기 아래 칸 띠: 그려 둔 칸마다 작은 그림과 번호(누르면 멈추고 그 칸을 봄).</summary>
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly ScrollViewer _stripScroll;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush, MaxWidth = PreviewBox };
    private readonly StackPanel _errors = new() { Spacing = 2 };
    private readonly Button _save, _insert, _validate, _upload;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(AssetPreview.PackageFrameMs) };
    private readonly Action _onAssetsChanged;
    /// <summary>"AI" 단추(켜짐 = AI 칸 열림)와 AI 칸 자리. 칸은 처음 열 때 만들고 창을 닫을 때까지 둔다(대화·세션 유지).</summary>
    private readonly ToggleButton _aiToggle;
    private readonly Border _aiHost;
    private Ai.EffectMakerAiPanel? _aiPanel;
    /// <summary>AI 칸을 열며 창을 넓힌 너비(닫을 때 그만큼 줄임, 최대화 중이면 0).</summary>
    private double _aiGrowth;
    private Ai.ScriptGuideWindow? _rules;

    /// <summary>스크립트를 열어 두었는지(목록에서 고르거나 새로 만듦).</summary>
    private bool _open;
    /// <summary>목록에서 고른 항목(개인 에셋·설치한 패키지 에셋 ID, 새 스크립트면 null).</summary>
    private string? _shownId;
    /// <summary>저장할 때 덮어쓸 개인 스크립트(새 스크립트·설치한 패키지에서 연 것이면 null — 저장하면 새 개인 에셋).</summary>
    private string? _entryId;
    private string _source = "";
    private ScriptEffectProgram? _program;
    private readonly Dictionary<string, object> _previewValues = new(StringComparer.Ordinal);
    /// <summary>미리보기 효과의 색 그라데이션(슬롯 = <see cref="ScriptColorSlots"/>). 미리보기·지금 레이어에 넣기에만 쓰고 원문에는 쓰지 않는다.</summary>
    private readonly Dictionary<string, EffectColorGradient> _previewGradients = new(StringComparer.Ordinal);
    private bool _loading, _syncingList, _syncingInfo, _syncingFrames;
    /// <summary>
    /// 마지막으로 불러오거나 저장한 원문(새 스크립트면 null). 고친 것이 있는지는 이것과 견줘 정한다 — 편집기의 TextChanged는 값을 넣은 뒤에 늦게 올 수 있어
    /// "불러오는 중" 표시로는 막을 수 없다.
    /// </summary>
    private string? _savedSource;
    private CancellationTokenSource? _render, _check;

    private bool Dirty => _open && (_savedSource is null || _source != _savedSource);

    /// <summary>메인 창에 딸린 효과 만들기를 연다(효과 창 커스텀 탭·에셋 관리자처럼 메인 창 밖에서).</summary>
    public static void OpenFromAnywhere(string? assetId)
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: MainForm main })
            Open(main, ((Ai.IAiHost)main).Context, assetId);
    }

    /// <summary>효과 만들기를 연다(이미 열려 있으면 앞으로). <paramref name="assetId"/>가 스크립트 효과면 그것을 연다.</summary>
    public static void Open(Window owner, EditorContext ctx, string? assetId = null)
    {
        if (s_window is { } open)
        {
            open.Activate();
            if (assetId is not null && open.ConfirmDiscard()) open.TryOpen(assetId);
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
        MinWidth = BaseMinWidth;
        MinHeight = 600;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // ── 왼쪽: 내 효과 목록, 아래 제목·설명
        _list.SelectionChanged += (_, _) => OnListSelection();
        _list.ContextMenu = ListMenu();
        _showAll = Ui.Check(UiStrings.EffectMaker_ShowAll);
        ToolTip.SetTip(_showAll, UiStrings.EffectMaker_ShowAllTip);
        _showAll.IsCheckedChanged += (_, _) => RebuildList();
        var newScript = Ui.Button(VectorIcon.Plus, UiStrings.EffectMaker_NewScript, (_, _) => NewScript(ScriptTemplates.Starter));
        var openJs = Ui.Button(VectorIcon.FolderOpen, UiStrings.ScriptEffect_OpenFile, (_, _) => OpenScriptFile());
        var header = new DockPanel();
        DockPanel.SetDock(_showAll, Dock.Right);
        header.Children.Add(_showAll);
        header.Children.Add(Ui.Label(UiStrings.EffectMaker_MyEffects, bold: true));
        _title = new TextBox { Margin = new Thickness(3), MaxLength = 100, Watermark = UiStrings.EffectMaker_TitleHint };
        _title.TextChanged += (_, _) => InfoEdited("name", _title.Text?.Trim() is { Length: > 0 } t ? t : _infoLanguage is null ? "" : null);
        _description = new TextBox
        {
            Height = 72, Margin = new Thickness(3), AcceptsReturn = false, TextWrapping = TextWrapping.Wrap,
            MaxLength = ScriptEffectProgram.MaxDescriptionLength, Watermark = UiStrings.EffectMaker_DescriptionHint,
        };
        _description.TextChanged += (_, _) => InfoEdited("description", _description.Text?.Trim() is { Length: > 0 } d ? d : null);
        _tags.TagsChanged += (_, _) => TagsEdited();
        // 제목·설명 아래: 다른 언어의 제목·설명·설정 이름(선언의 translations)
        _languages = Ui.Button(VectorIcon.Languages, UiStrings.EffectMaker_Languages, (_, _) => EditTranslations()).Tip(UiStrings.EffectMaker_LanguagesTip);
        var info = Ui.Group(UiStrings.EffectMaker_Info, Ui.Stack(
            InfoLabel(UiStrings.EffectMaker_TitleLabel, _titleLanguage), _title,
            InfoLabel(UiStrings.EffectMaker_DescriptionLabel, _descriptionLanguage), _description,
            Ui.Row(Ui.Label(UiStrings.EffectMaker_TagsLabel), Ui.InfoBadge(UiStrings.EffectMaker_TagsTip)), _tags,
            _languages));
        // "정보" 제목 줄 오른쪽에 규칙 단추(효과를 만들 때 지킬 규칙·API — 복사해 다른 AI에 붙여 넣음)
        var rules = Ui.Button(VectorIcon.BookOpen, UiStrings.EffectMaker_Rules, (_, _) => ShowRules()).Tip(UiStrings.EffectMaker_RulesTip);
        rules.Padding = new Thickness(6, 1);
        HeaderButtons(info, rules);
        var left = new DockPanel { Width = 260, LastChildFill = true };
        var leftTop = Ui.Stack(header, Ui.Row(newScript, openJs));
        DockPanel.SetDock(leftTop, Dock.Top);
        DockPanel.SetDock(info, Dock.Bottom);
        left.Children.Add(leftTop);
        left.Children.Add(info);
        left.Children.Add(_list);

        // ── 가운데: 원문과 미리보기 설정 값
        _code.TextChanged += (_, _) => Edited();
        // 색 그라데이션·색 목록 칸이 들어가도록 넉넉히(넘치면 스크롤)
        var settingsBox = Ui.Group(UiStrings.EffectMaker_PreviewSettings, new ScrollViewer { Content = _scriptSettings, MaxHeight = 220 });
        // 제목 줄 오른쪽: 미리보기 값을 원문 기본값으로 되돌리기 / 지금 값을 원문 기본값으로 쓰기
        _resetValues = Ui.Button(VectorIcon.Undo2, UiStrings.EffectMaker_ResetValues, (_, _) => ResetValues()).Tip(UiStrings.EffectMaker_ResetValuesTip);
        _setDefaults = Ui.Button(VectorIcon.Pencil, UiStrings.EffectMaker_SetDefaults, (_, _) => SetDefaults()).Tip(UiStrings.EffectMaker_SetDefaultsTip);
        _resetValues.Padding = _setDefaults.Padding = new Thickness(6, 1);
        HeaderButtons(settingsBox, Ui.Row(_resetValues, _setDefaults));
        DockPanel.SetDock(settingsBox, Dock.Bottom);
        var script = new DockPanel { LastChildFill = true };
        script.Children.Add(settingsBox);
        script.Children.Add(_code);
        _scriptPanel = script;
        _emptyPanel = Ui.Stack(Hint(UiStrings.EffectMaker_Empty));
        _center.Content = _emptyPanel;
        _center.Margin = new Thickness(8, 0);

        // ── 오른쪽: 미리보기·상태
        for (int i = 0; i < PreviewChicks.Ids.Count; i++) _chick.Items.Add(ChickItem(PreviewChicks.Ids[i], i + 1));
        _chick.SelectionChanged += (_, _) => ChickChanged();
        ToolTip.SetTip(_preview, UiStrings.EffectMaker_PreviewHint);
        _play = Ui.Button(VectorIcon.Pause, UiStrings.EffectMaker_Pause, (_, _) => TogglePlay(), 90);
        _original = Ui.Check(UiStrings.EffectMaker_ShowOriginal);
        _original.IsCheckedChanged += (_, _) => _preview.ShowOriginal = _original.On();
        _timer.Tick += (_, _) => _preview.Step();
        _preview.FramesChanged += RebuildStrip;
        _preview.IndexChanged += HighlightStrip;
        _stripScroll = new ScrollViewer
        {
            Content = _strip, Width = PreviewBox, Margin = new Thickness(0, 2),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, IsVisible = false,
        };
        _strip.Margin = new Thickness(0, 0, 0, 10); // 아래 여백: 겹쳐 뜨는 가로 스크롤 막대가 번호를 가리지 않게
        _frameCount = Ui.Num(ScriptEffectProgram.MinPreviewFrames, ScriptEffectProgram.MaxPreviewFrames, AssetPreview.PackageFrames, 64);
        _frameCount.ValueChanged += (_, _) => FrameCountChanged();
        var frameLabel = Ui.Label(UiStrings.EffectMaker_FrameCount);
        frameLabel.VerticalAlignment = VerticalAlignment.Center;
        var frames = Ui.Row(frameLabel, _frameCount).Tip(UiStrings.EffectMaker_FrameCountTip);
        // 재생 단추·효과 없이 보기 옆에 프레임 수(좁으면 다음 줄로)
        var playRow = new WrapPanel { Orientation = Orientation.Horizontal, Width = PreviewBox };
        playRow.Children.Add(Ui.Row(_play, _original));
        playRow.Children.Add(frames);
        // "미리보기" 제목 줄 오른쪽에 AI 단추: 누르면 창 오른쪽에 AI 칸이 열린다
        _aiToggle = new ToggleButton
        {
            Content = Ui.IconLabel(VectorIcon.Sparkles, UiStrings.EffectMakerAi_Button),
            Padding = new Thickness(8, 1),
            VerticalAlignment = VerticalAlignment.Center,
        }.Tip(UiStrings.EffectMakerAi_ButtonTip);
        _aiToggle.IsCheckedChanged += (_, _) => ShowAiPanel(_aiToggle.On());
        var previewHeader = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_aiToggle, Dock.Right);
        previewHeader.Children.Add(_aiToggle);
        var previewTitle = Ui.Label(UiStrings.EffectMaker_Preview, bold: true);
        previewTitle.VerticalAlignment = VerticalAlignment.Center;
        previewHeader.Children.Add(previewTitle);
        var right = Ui.Stack(
            previewHeader,
            _preview,
            _stripScroll,
            Ui.Row(Ui.Label(UiStrings.EffectPreset_PreviewChick), _chick),
            playRow,
            Ui.Group(UiStrings.EffectMaker_Status, Ui.Stack(_status, _errors)));
        right.Width = PreviewBox + 24;

        // ── 아래 줄
        _save = Ui.Button(VectorIcon.Save, UiStrings.EffectMaker_Save, (_, _) => Save()).Tip(UiStrings.EffectMaker_SaveTip);
        _insert = Ui.Button(VectorIcon.Plus, UiStrings.EffectMaker_Insert, (_, _) => InsertIntoLayer()).Tip(UiStrings.EffectMaker_InsertTip);
        _validate = Ui.Button(VectorIcon.Check, UiStrings.EffectMaker_Validate, async (_, _) => await ValidateAsync()).Tip(UiStrings.EffectMaker_ValidateTip);
        _upload = Ui.Button(VectorIcon.Upload, UiStrings.EffectMaker_Upload, (_, _) => Upload()).Tip(UiStrings.EffectMaker_UploadTip);
        _upload.Background = AppTheme.AccentBrush;
        _upload.Foreground = Brushes.White;
        var close = Ui.Button(UiStrings.S_Ui_CloseOnly_11312F9A81, (_, _) => Close(), 90);
        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var rightButtons = Ui.Row(_validate, _upload, close);
        DockPanel.SetDock(rightButtons, Dock.Right);
        bottom.Children.Add(rightButtons);
        bottom.Children.Add(Ui.Row(_save, _insert));

        _aiHost = new Border { Width = AiPanelWidth, Margin = new Thickness(8, 0, 0, 0), IsVisible = false };
        var columns = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(_aiHost, Dock.Right);
        DockPanel.SetDock(right, Dock.Right);
        columns.Children.Add(left);
        columns.Children.Add(_aiHost);
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
            _check?.Cancel();
            _timer.Stop();
            _debounce.Stop();
            _aiPanel?.Shutdown();
            _rules?.Close();
            s_window = null;
        };
        RebuildList();
        UpdateButtons();
    }

    // ───────────── 목록 ─────────────

    /// <summary>
    /// 목록 항목: 기본은 내가 만든 개인 스크립트(설치한 패키지의 원문을 그대로 복사한 것은 뺌), 모두 보기면 그 복사본과 설치한 패키지의 스크립트 효과까지.
    /// </summary>
    private IEnumerable<AssetEntry> Entries()
    {
        var catalog = AppAssets.Catalog;
        var installed = catalog.Packages.SelectMany(p => p.Entries).Where(e => e.Kind == AssetKinds.EffectScript).ToList();
        var installedHashes = installed.Select(e => e.Script!.Hash).ToHashSet(StringComparer.Ordinal);
        var personal = catalog.Personal.Where(e => e.Kind == AssetKinds.EffectScript);
        if (_showAll.On() != true) return personal.Where(e => !installedHashes.Contains(e.Script!.Hash));
        return personal.Concat(installed);
    }

    private void RebuildList()
    {
        _syncingList = true;
        _list.Items.Clear();
        foreach (var entry in Entries())
        {
            Control content = entry.Origin == AssetOrigin.Personal
                ? Ui.IconLabel(VectorIcon.Code, entry.DisplayName)
                : Ui.Row(Ui.IconLabel(VectorIcon.Package, entry.DisplayName), new TextBlock { Text = entry.PackageName ?? entry.PackageId ?? "", Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center });
            _list.Items.Add(new ListBoxItem { Content = content, Tag = entry.Id });
        }
        SelectInList(_shownId);
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
        if (_syncingList || _list.SelectedItem is not ListBoxItem { Tag: string id } || id == _shownId) return;
        if (!ConfirmDiscard())
        {
            SelectInList(_shownId);
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
        var menu = new ContextMenu { Items = { delete, manager } };
        // 설치한 패키지의 효과는 여기서 지우지 않는다(에셋 관리자에서 패키지째로)
        menu.Opening += (_, _) => delete.IsEnabled = _list.SelectedItem is ListBoxItem { Tag: string id } && AppAssets.Catalog.Find(id)?.Origin == AssetOrigin.Personal;
        return menu;
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: string id } || AppAssets.Catalog.Find(id) is not { Origin: AssetOrigin.Personal } entry) return;
        if (Ui.Ask(this, string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_DeleteQuestion, entry.DisplayName), UiStrings.EffectMaker_Title,
                MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        if (!AppAssets.Try(this, () => AppAssets.Store.RemovePersonal(id), UiStrings.EffectMaker_SaveError)) return;
        if (id == _shownId) CloseScript();
    }

    /// <summary>
    /// 스크립트 효과를 연다(저장하지 않은 고침은 부르는 쪽이 이미 확인함). 개인 에셋이면 저장할 때 그 자리를 고치고, 설치한 패키지의 것이면 저장할 때 개인 사본을 만든다.
    /// 목록에 보이지 않는 항목(모두 보기를 끈 채 복사본·패키지 효과를 열 때)이면 모두 보기를 켠다.
    /// </summary>
    private void TryOpen(string id)
    {
        if (AppAssets.Catalog.Find(id) is not { Data: ScriptAsset script } entry) return;
        _shownId = id;
        _entryId = entry.Origin == AssetOrigin.Personal ? id : null;
        Load(script.Source, saved: true);
        if (!_list.Items.OfType<ListBoxItem>().Any(item => (string?)item.Tag == id)) _showAll.IsChecked = true;
        SelectInList(id);
    }

    // ───────────── 새로 만들기·불러오기 ─────────────

    private void NewScript(string source)
    {
        if (!ConfirmDiscard()) return;
        _shownId = null;
        _entryId = null;
        Load(source, saved: false);
        SelectInList(null);
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

    /// <summary>원문을 연다. <paramref name="saved"/>면 그 원문을 저장한 상태로 본다(목록에서 연 것), 아니면 새 효과(저장 전).</summary>
    private void Load(string source, bool saved)
    {
        _loading = true;
        DropCheck();
        _source = ScriptEffectProgram.NormalizeSource(source);
        _code.Text = _source;
        _previewValues.Clear();
        _previewGradients.Clear();
        _program = null;
        _loading = false;
        _savedSource = saved ? _source : null;
        _open = true;
        _center.Content = _scriptPanel;
        UpdateButtons();
        Rerender();
    }

    /// <summary>열어 둔 스크립트를 닫고 빈 화면으로(지운 스크립트를 보고 있었을 때).</summary>
    private void CloseScript()
    {
        _open = false;
        _shownId = null;
        _entryId = null;
        _savedSource = null;
        _render?.Cancel();
        DropCheck();
        _center.Content = _emptyPanel;
        _preview.SetFrames([], null);
        _status.Text = "";
        _errors.Children.Clear();
        SetInfo(null);
        SelectInList(null);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _save.IsEnabled = _open;
        _insert.IsEnabled = _open && _ctx.Project is not null;
        _validate.IsEnabled = _open;
        _upload.IsEnabled = _open;
        _title.IsEnabled = _description.IsEnabled = _tags.IsEnabled = _open;
        _frameCount.IsEnabled = _languages.IsEnabled = _open && _program is not null;
        UpdateValueButtons();
        Title = _open ? string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_TitleWith, CurrentName() + (Dirty ? " *" : "")) : UiStrings.EffectMaker_Title;
    }

    /// <summary>"초기화"는 미리보기 설정 값이 원문의 기본값과 하나라도 다르거나 그라데이션이 있을 때, "기본값으로"는 값이 다를 때만 켠다.</summary>
    private void UpdateValueButtons()
    {
        bool differs = _open && _program is { } program && program.NormalizeSettings(_previewValues) is var values
                       && program.Settings.Any(s => !ScriptSetting.SameValue(values[s.Key], s.Default));
        _setDefaults.IsEnabled = differs;
        _resetValues.IsEnabled = differs || (_open && _previewGradients.Count > 0);
    }

    /// <summary>창 제목·묻는 글에 보일 이름: 화면 언어의 번역(없으면 선언의 <c>name</c>).</summary>
    private string CurrentName() => _program?.NameFor(CultureInfo.CurrentUICulture) ?? UiStrings.ScriptEffect_Name;

    private void Edited()
    {
        if (_loading) return;
        _source = _code.Text;
        DropCheck(); // 원문이 바뀌면 검증 결과는 낡음
        UpdateButtons();
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>검증(진행 중이거나 보이는 결과)을 버린다. 다음 다시 그리기가 상태 칸을 채운다.</summary>
    private void DropCheck()
    {
        _check?.Cancel();
        _check = null;
    }

    // ───────────── 제목·설명 ─────────────

    /// <summary>
    /// 제목·설명 칸을 고치면 원문 선언의 그 값을 고친다(편집기는 달라진 곳만 바뀜). 선언을 찾지 못하는 원문이면 원문 오류를 보인다.
    /// 화면 언어가 선언의 기본 언어(<c>language</c>)와 다르면 기본 값 대신 그 언어의 번역(<c>translations[화면 언어]</c>)을 고친다 — 비우면 그 번역 값을 뺀다.
    /// 칸 값이 지금 선언과 같으면 아무것도 하지 않는다 — 글 상자의 TextChanged는 값을 넣은 뒤 늦게 올 수 있어(<see cref="SetInfo"/>가 넣은 값),
    /// 그때 원문을 다시 쓰면 따옴표 모양만 바뀌어도 고친 것으로 보인다.
    /// </summary>
    private void InfoEdited(string key, string? value)
    {
        if (_syncingInfo || _loading || !_open) return;
        if (_program is not null && value == InfoValue(_program, _infoLanguage, key)) return;
        string? edited = _infoLanguage is { } language
            ? ScriptDeclarationEdit.SetTranslationField(_source, language, key, value)
            : ScriptDeclarationEdit.SetString(_source, key, value);
        if (edited is null)
        {
            _status.Text = UiStrings.EffectMaker_FixSourceFirst;
            return;
        }
        _code.Replace(edited); // TextChanged → Edited → 다시 그리기
    }

    /// <summary>선언에서 제목·설명 칸이 보이는 값: <paramref name="language"/>가 null이면 기본 name·description, 아니면 그 언어 번역(없으면 null).</summary>
    private static string? InfoValue(ScriptEffectProgram program, string? language, string key)
    {
        if (language is null) return key == "name" ? program.Name.Trim() : program.Description;
        if (!program.Translations.TryGetValue(language, out var translation)) return null;
        return key == "name" ? translation.Name : translation.Description;
    }

    /// <summary>
    /// 해석한 선언 값을 제목·설명·태그 칸에 보인다(그 칸에서 치는 중이면 건드리지 않음 — 커서가 튀지 않게). 화면 언어가 기본 언어와 다르면
    /// 그 언어 번역을 보이고(없으면 빈 칸 — 흐린 글로 기본 값), 칸 이름 옆에 고치는 언어를 적는다. <paramref name="program"/>이 null이면 비운다.
    /// </summary>
    private void SetInfo(ScriptEffectProgram? program)
    {
        _syncingInfo = true;
        _infoLanguage = program is null ? null : ScriptTranslations.EditLanguage(program.Language, CultureInfo.CurrentUICulture);
        string? name = program is null ? null : InfoValue(program, _infoLanguage, "name");
        string? description = program is null ? null : InfoValue(program, _infoLanguage, "description");
        if (!_title.IsFocused && (_title.Text ?? "") != (name ?? "")) _title.Text = name ?? "";
        if (!_description.IsFocused && (_description.Text ?? "") != (description ?? "")) _description.Text = description ?? "";
        bool translating = _infoLanguage is not null;
        _title.Watermark = translating ? program!.Name : UiStrings.EffectMaker_TitleHint;
        _description.Watermark = translating && program!.Description is { Length: > 0 } baseDescription ? baseDescription : UiStrings.EffectMaker_DescriptionHint;
        string? caption = (_infoLanguage ?? program?.Language) is { } code ? LanguagePicker.NativeName(code) : null;
        foreach (var label in new[] { _titleLanguage, _descriptionLanguage })
        {
            label.Text = caption ?? "";
            label.IsVisible = caption is not null;
        }
        if (!_tags.IsKeyboardFocusWithin) _tags.SetTags(program?.Tags ?? []);
        _syncingInfo = false;
    }

    /// <summary>태그 칩을 바꾸면 선언의 <c>tags</c>를 고친다(규칙에 맞지 않는 태그가 있으면 칩 아래 까닭만 보이고 원문은 그대로).</summary>
    private void TagsEdited()
    {
        if (_syncingInfo || _loading || !_open || !_tags.IsValid) return;
        if (_program is not null && _program.Tags.SequenceEqual(_tags.Tags)) return;
        if (ScriptDeclarationEdit.SetTags(_source, _tags.Tags) is not { } edited)
        {
            _status.Text = UiStrings.EffectMaker_FixSourceFirst;
            return;
        }
        _code.Replace(edited);
    }

    /// <summary>정보 칸 이름 줄: 이름 + 고치는 언어(작은 흐린 글).</summary>
    private static Control InfoLabel(string text, TextBlock language)
    {
        var label = Ui.Label(text);
        label.VerticalAlignment = language.VerticalAlignment = VerticalAlignment.Center;
        return Ui.Row(label, language);
    }

    private static TextBlock LanguageCaption() => new()
    {
        Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, Margin = new Thickness(4, 0, 0, 0), IsVisible = false,
    };

    // ───────────── 미리보기 ─────────────

    /// <summary>미리보기에 그릴 효과(해석이 되면 미리보기 설정 값·그라데이션의 스크립트 효과 하나). 그릴 것이 없으면 빈 목록.</summary>
    internal IReadOnlyList<LayerEffect> PreviewEffects()
    {
        if (!_open || _program is null) return [];
        // 미리보기는 이 창 안에서만: 실행 허락은 메모리에만 두고(설정 파일에 중간 원문이 쌓이지 않게), 저장·넣기 때 TrustLocal로 남긴다
        string hash = ScriptLibrary.Add(_source);
        if (ScriptLibrary.TrustOf(hash) != ScriptTrust.Trusted) ScriptLibrary.SetTrust(hash, true);
        return [PreviewEffect(_program) with { ScriptHash = hash }];
    }

    /// <summary><paramref name="program"/>으로 만든 미리보기 설정 값·그라데이션의 새 효과(선언의 빠르기 곡선).</summary>
    private ScriptEffect PreviewEffect(ScriptEffectProgram program)
        => program.CreateEffect(_previewValues) with { ColorGradients = new Dictionary<string, EffectColorGradient>(_previewGradients, StringComparer.Ordinal) };

    /// <summary>
    /// 미리보기 설정 칸의 고치기(효과 창 설정 칸과 같은 모양 — <see cref="LayerEffectsDialog.AddScriptSettingRows"/>): 미리보기 효과에 <paramref name="change"/>를 입혀
    /// 바뀐 설정 값과 그라데이션을 미리보기 값으로 옮기고 잠시 뒤 다시 그린다.
    /// </summary>
    private void ChangePreview(Func<LayerEffect, LayerEffect> change)
    {
        if (_program is not { } program || change(PreviewEffect(program)) is not ScriptEffect after) return;
        var before = program.NormalizeSettings(_previewValues);
        var now = program.NormalizeSettings(after.Settings);
        // 고친 키만 넣는다(나머지는 원문 기본값을 그대로 따름)
        foreach (var setting in program.Settings)
            if (!ScriptSetting.SameValue(before[setting.Key], now[setting.Key])) _previewValues[setting.Key] = now[setting.Key];
        _previewGradients.Clear();
        foreach (var (slot, gradient) in after.ColorGradients) _previewGradients[slot] = gradient;
        UpdateValueButtons();
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>원문을 해석하고 미리보기 칸들을 작업 스레드에서 다시 그린다. 앞 계산은 취소한다.</summary>
    private void Rerender()
    {
        _render?.Cancel();
        // 검증 결과를 보이는 중이면(원문은 그대로 — 원문이 바뀌면 DropCheck) 상태 칸은 두고 그림만 다시 그린다
        bool report = _check is null;
        if (report) _errors.Children.Clear();
        if (!_open) return;
        ParseScript(report);
        var effects = PreviewEffects();
        if (effects.Count == 0)
        {
            _preview.SetFrames([], null);
            return;
        }
        var chickId = _program!.PreviewChick;
        var program = _program;
        int steps = program.PreviewFrames;
        var values = new Dictionary<string, object>(_previewValues, StringComparer.Ordinal);
        var cancel = new CancellationTokenSource();
        _render = cancel;
        _status.Text = UiStrings.EffectMaker_Rendering;
        Task.Run(() =>
        {
            var chick = PreviewChicks.Load(chickId);
            ScriptError? runError = null;
            double? msPerFrame = null;
            // 빠른 경로 판정(첫 읽기에 변환·컴파일)을 먼저 해 두어 아래 시간에 컴파일이 섞이지 않게 한다
            var path = program.ExecutionPath;
            // 실행 오류·시간은 효과를 거치지 않고 직접 한 번 돌려 본다(효과 렌더러는 오류를 삼키고 효과를 건너뛴다)
            var probe = chick.Clone();
            var watch = Stopwatch.StartNew();
            try
            {
                program.Render(probe, new Rectangle(0, 0, probe.Width, probe.Height), Frame(steps / 2, steps), values, cancel.Token);
                msPerFrame = watch.Elapsed.TotalMilliseconds;
            }
            catch (ScriptEffectException ex)
            {
                runError = ex.Error;
            }
            var frames = new List<PixelImage>();
            for (int step = 0; step < steps && !cancel.IsCancellationRequested; step++)
                frames.Add(AssetPreview.Render(effects, chick, step, steps));
            var original = AssetPreview.Original(effects, chick, 0, steps);
            return (frames, original, runError, msPerFrame, path);
        }, cancel.Token).ContinueWith(task =>
        {
            if (cancel.IsCancellationRequested || task.IsFaulted || task.IsCanceled || !ReferenceEquals(_render, cancel)) return;
            var (frames, original, runError, ms, path) = task.Result;
            _preview.SetFrames(frames, original);
            if (_check is not null) return;
            _status.Text = runError is not null ? UiStrings.EffectMaker_RunFailed
                : ms is { } t ? string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_FrameTime, t.ToString("0.00", CultureInfo.CurrentCulture))
                : UiStrings.EffectMaker_Ready;
            AddPath(path);
            if (runError is not null)
            {
                AddError(runError, null);
                _code.SetErrors([runError]);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>올릴 때 미리보기의 <paramref name="step"/>번째 칸과 같은 진행(선형, <paramref name="steps"/>칸 — 선언의 <c>previewFrames</c>).</summary>
    private static ScriptFrameContext Frame(int step, int steps)
    {
        int span = steps - 1;
        double t = span == 0 ? 0 : step / (double)span;
        return new ScriptFrameContext { Time = t, Progress = t, FrameIndex = step, FrameStep = step, FrameSpan = span };
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

    /// <summary>원문을 해석해(<paramref name="report"/>면 오류를 상태 칸에 보이고) 되면 제목·설명·병아리·프레임 수·설정 칸을 맞춘다.</summary>
    private void ParseScript(bool report)
    {
        var parsed = ScriptEffectProgram.Parse(_source);
        if (report)
        {
            _code.SetErrors(parsed.Errors);
            foreach (var error in parsed.Errors) AddError(error, null);
        }
        if (!parsed.Success)
        {
            if (report) _status.Text = UiStrings.EffectMaker_ParseFailed;
            _program = null;
            _preview.SetFrames([], null);
            UpdateButtons();
            return;
        }
        // 설정이나 번역(설정 이름)이 바뀌면 칸을 다시 만든다
        bool schemaChanged = _program is null || !_program.Settings.SequenceEqual(parsed.Program.Settings)
                             || _program.Translations.Count != parsed.Program.Translations.Count
                             || _program.Translations.Any(t => !parsed.Program.Translations.TryGetValue(t.Key, out var other) || !t.Value.SameAs(other));
        _program = parsed.Program;
        // 선언에 없는 색 슬롯(지운 설정·줄어든 최대 항목 수)의 그라데이션은 버린다
        foreach (var slot in _previewGradients.Keys.ToList())
            if (!ScriptColorSlots.TryParse(_program.Settings, slot, out _, out _, out _)) _previewGradients.Remove(slot);
        SetInfo(_program);
        _loading = true;
        _chick.SelectedIndex = Math.Max(0, PreviewChicks.Ids.ToList().IndexOf(_program.PreviewChick));
        _loading = false;
        _syncingFrames = true;
        _frameCount.Value = _program.PreviewFrames;
        _syncingFrames = false;
        if (schemaChanged) BuildScriptSettings();
        UpdateButtons();
    }

    /// <summary>
    /// 미리보기 설정 칸: 선언의 settings로 만든 칸(효과 창 설정 칸과 같은 편집기 — 색 그라데이션, 색 목록·색 쌍 목록, 각도·점 포함).
    /// 값은 미리보기·지금 레이어에 넣기에만 쓰고 원문의 default는 "기본값으로"를 눌러야 바뀐다.
    /// </summary>
    private void BuildScriptSettings()
    {
        _scriptSettings.Children.Clear();
        if (_program is not { } program) return;
        if (program.Settings.Count == 0) _scriptSettings.Children.Add(Hint(UiStrings.ScriptEffect_NoSettings));
        LayerEffectsDialog.AddScriptSettingRows(program, PreviewEffect(program), ChangePreview, BuildScriptSettings, (name, editor, block) =>
        {
            var label = Ui.Label(name);
            label.Width = 110;
            var line = Ui.Row(label, editor);
            // 여러 줄 덩어리(색 목록 등)는 이름과 편집기를 위에 맞춤
            if (block)
            {
                label.VerticalAlignment = editor.VerticalAlignment = VerticalAlignment.Top;
                label.Margin = new Thickness(label.Margin.Left, 6, label.Margin.Right, label.Margin.Bottom);
            }
            _scriptSettings.Children.Add(line);
        });
    }

    /// <summary>미리보기 설정 값과 그라데이션을 원문의 기본값(그라데이션 없음)으로 되돌린다(원문은 그대로).</summary>
    private void ResetValues()
    {
        if (_program is null) return;
        _previewValues.Clear();
        _previewGradients.Clear();
        BuildScriptSettings();
        UpdateValueButtons();
        _debounce.Stop();
        Rerender();
    }

    /// <summary>
    /// 지금 미리보기 설정 값 중 기본값과 다른 것을 원문 선언의 각 설정 <c>default</c>로 쓴다(편집기 실행취소 한 단계). 쓴 뒤 미리보기 값은 새 기본값과 같으므로
    /// 칸은 그대로 보인다. 선언을 고칠 수 없는 원문이면 원문 오류를 안내한다.
    /// </summary>
    private void SetDefaults()
    {
        if (!_open || _program is not { } program) return;
        var values = program.NormalizeSettings(_previewValues);
        var changed = program.Settings.Where(s => !ScriptSetting.SameValue(values[s.Key], s.Default))
            .Select(s => new KeyValuePair<string, object>(s.Key, values[s.Key])).ToList();
        if (changed.Count == 0) return;
        if (ScriptDeclarationEdit.SetSettingDefaults(_source, changed) is not { } edited)
        {
            _status.Text = UiStrings.EffectMaker_FixSourceFirst;
            return;
        }
        _previewValues.Clear(); // 새 기본값 = 지금 값
        ApplyDeclarationEdit(edited);
    }

    /// <summary>프레임 수 칸을 바꾸면 선언의 <c>previewFrames</c>를 고치고 곧바로 다시 그린다(원문 오류면 칸을 되돌리고 안내).</summary>
    private void FrameCountChanged()
    {
        if (_syncingFrames || _loading || !_open) return;
        int frames = (int)_frameCount.NumValue();
        if (_program is null || _program.PreviewFrames == frames) return;
        if (ScriptDeclarationEdit.SetPreviewFrames(_source, frames) is not { } edited)
        {
            _status.Text = UiStrings.EffectMaker_FixSourceFirst;
            _syncingFrames = true;
            _frameCount.Value = _program.PreviewFrames;
            _syncingFrames = false;
            return;
        }
        ApplyDeclarationEdit(edited);
    }

    /// <summary>
    /// "언어 설정": 다른 언어의 제목·설명·설정 이름을 고치는 창을 열고, 확인하면 선언의 <c>translations</c>를 바꾼다(달라진 언어만 고침 — 실행취소 한 단계).
    /// </summary>
    private void EditTranslations()
    {
        if (!_open || _program is not { } program) return;
        if (ScriptTranslationsDialog.Ask(this, program) is not { } translations) return;
        if (ScriptDeclarationEdit.SetTranslations(_source, translations) is not { } edited)
        {
            Ui.Info(this, UiStrings.EffectMaker_FixSourceFirst, UiStrings.EffectMaker_Title);
            return;
        }
        if (edited != _source) ApplyDeclarationEdit(edited);
    }

    /// <summary>창이 고친 선언을 편집기에 넣고(달라진 곳만, 실행취소 한 단계) 입력 기다림 없이 다시 그린다.</summary>
    private void ApplyDeclarationEdit(string edited)
    {
        _code.Replace(edited);
        if (_source != _code.Text) Edited(); // 편집기의 TextChanged가 늦게 오는 경우
        _debounce.Stop();
        Rerender();
    }

    /// <summary>묶음 상자(<see cref="Ui.Group"/>)의 제목 줄 오른쪽에 <paramref name="buttons"/>를 둔다.</summary>
    private static void HeaderButtons(Border group, Control buttons)
    {
        var stack = (StackPanel)group.Child!;
        var title = stack.Children[0];
        stack.Children.RemoveAt(0);
        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        stack.Children.Insert(0, header);
    }

    /// <summary>오류 줄(누르면 원문의 그 자리로). <paramref name="where"/>는 앞에 붙일 곳(검증에서 병아리·칸).</summary>
    private void AddError(ScriptError error, string? where)
    {
        var text = new SelectableTextBlock
        {
            Text = where is null ? ScriptStatusText.Error(error) : where + " " + ScriptStatusText.Error(error),
            TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.UnsavedBrush, MaxWidth = PreviewBox,
        };
        if (error.Line > 0)
        {
            text.Cursor = new Cursor(StandardCursorType.Hand);
            text.PointerPressed += (_, _) => _code.GoTo(error.Line, error.Column);
        }
        _errors.Children.Add(text);
    }

    private void AddNote(string message, IBrush brush)
        => _errors.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = brush, MaxWidth = PreviewBox });

    /// <summary>병아리는 원문 선언의 <c>previewChick</c>이 정한다: 고르면 원문을 고친다(원문에 오류가 있으면 되돌리고 안내).</summary>
    private void ChickChanged()
    {
        if (_loading || _chick.SelectedIndex < 0 || !_open) return;
        string id = PreviewChicks.Ids[_chick.SelectedIndex];
        if (_program?.PreviewChick == id) return;
        if (ScriptDeclarationEdit.SetPreviewChick(_source, id) is not { } edited)
        {
            Ui.Info(this, UiStrings.EffectMaker_FixSourceFirst, UiStrings.EffectMaker_Title);
            _loading = true;
            _chick.SelectedIndex = Math.Max(0, PreviewChicks.Ids.ToList().IndexOf(_program?.PreviewChick ?? PreviewChicks.Default));
            _loading = false;
            return;
        }
        _code.Replace(edited); // TextChanged → Edited → 다시 그리기
    }

    /// <summary>병아리 고르기 칸의 항목: 병아리 그림(2배)과 번호.</summary>
    private static ComboBoxItem ChickItem(string id, int number)
    {
        var image = PreviewChicks.Load(id);
        var view = new Image
        {
            Source = ImageUtil.ToBitmap(image), Width = image.Width * ChickScale, Height = image.Height * ChickScale, Stretch = Stretch.Fill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapInterpolationMode(view, BitmapInterpolationMode.None);
        string label = string.Format(CultureInfo.CurrentCulture, UiStrings.EffectPreset_PreviewChickTip, number);
        return new ComboBoxItem
        {
            Content = Ui.Row(view, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }),
        };
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

    // ───────────── 검증 ─────────────

    /// <summary>검증을 돌려 통과하면 true(취소·새로 고침이면 false). 검증 단추와 올리기가 쓴다.</summary>
    private async Task<bool> ValidateAsync() => await CheckAsync() is { Problems.Count: 0 };

    /// <summary>
    /// 올리기 전 검사: 설치와 같은 원문 검사(크기·문법·선언, 맨 위 실행 한도)와 제목, 그리고 선언의 기본 설정으로 모든 병아리 견본 × 올릴 때 미리보기 칸마다
    /// 직접 돌려 실행 오류(같은 오류는 처음 한 번만)와 가장 느린 칸을 본다. 결과는 상태 칸에 보이고 돌려준다(열린 스크립트가 없거나, 끝나기 전에 원문이 바뀌어 취소되면 null).
    /// 검증 단추와 AI 도구 <c>check_effect_maker</c>가 같이 쓴다.
    /// </summary>
    internal async Task<ValidationResult?> CheckAsync()
    {
        if (!_open) return null;
        // 미뤄 둔 다시 그리기가 있으면 먼저(미리보기를 지금 원문에 맞춤 — 검증 중이라 상태 칸은 건드리지 않음)
        bool pending = _debounce.IsEnabled;
        _debounce.Stop();
        _check?.Cancel();
        var cancel = new CancellationTokenSource();
        _check = cancel;
        if (pending) Rerender();
        _errors.Children.Clear();
        _status.Text = UiStrings.EffectMaker_Validating;
        string source = _source;
        var result = await Task.Run(() => Validate(source, cancel.Token), cancel.Token).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
        if (result is null || cancel.IsCancellationRequested || !ReferenceEquals(_check, cancel)) return null;

        _errors.Children.Clear();
        foreach (var problem in result.Problems) AddError(problem.Error, problem.Where);
        if (result.Path is { } path) AddPath(path);
        int errors = result.Problems.Count;
        _status.Text = errors > 0 ? string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_ValidateFailed, errors)
            : string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_ValidateOk, result.Runs,
                result.SlowestMs.ToString("0.00", CultureInfo.CurrentCulture), result.SlowestChick);
        if (errors == 0) AddNote(UiStrings.EffectMaker_ValidateOkNote, AppTheme.DimTextBrush);
        _code.SetErrors(result.Problems.Select(p => p.Error));
        return result;
    }

    internal sealed record ValidationProblem(ScriptError Error, string? Where);

    internal sealed record ValidationResult(IReadOnlyList<ValidationProblem> Problems, ScriptExecutionPath? Path, int Runs, double SlowestMs, int SlowestChick);

    private static ValidationResult Validate(string source, CancellationToken cancel)
    {
        var problems = new List<ValidationProblem>();
        var issues = new List<AssetIssue>();
        var context = new AssetReadContext(AssetKinds.EffectScript, strict: true, AssetPackageLimits.Default, issues);
        if (ScriptAssetFormat.Check(source, "source", context) is null)
        {
            // 원문 검사 실패: 해석 오류는 줄·칸이 있는 오류로 다시 받아 보인다
            var parsed = ScriptEffectProgram.Parse(source, ScriptAssetFormat.CheckLimits);
            problems.AddRange(parsed.Errors.Select(e => new ValidationProblem(e, null)));
            if (problems.Count == 0) problems.AddRange(issues.Select(i => new ValidationProblem(new ScriptError(ScriptErrorKind.Declaration, i.Message, 0, 0), null)));
            return new ValidationResult(problems, null, 0, 0, 0);
        }
        var program = ScriptEffectProgram.Parse(source).Program!;
        if (!AssetManifest.IsValidName(program.Name))
            problems.Add(new ValidationProblem(new ScriptError(ScriptErrorKind.Declaration, UiStrings.EffectMaker_ValidateName, 0, 0), null));

        var seen = new HashSet<(string, int, int)>();
        int runs = 0, slowestChick = 1;
        double slowest = 0;
        for (int c = 0; c < PreviewChicks.Ids.Count; c++)
        {
            var chick = PreviewChicks.Load(PreviewChicks.Ids[c]);
            for (int step = 0; step < program.PreviewFrames; step++)
            {
                cancel.ThrowIfCancellationRequested();
                var image = chick.Clone();
                var watch = Stopwatch.StartNew();
                try
                {
                    program.Render(image, new Rectangle(0, 0, image.Width, image.Height), Frame(step, program.PreviewFrames), null, cancel);
                    double ms = watch.Elapsed.TotalMilliseconds;
                    if (ms > slowest)
                    {
                        slowest = ms;
                        slowestChick = c + 1;
                    }
                }
                catch (ScriptEffectException ex)
                {
                    if (seen.Add((ex.Error.Message, ex.Error.Line, ex.Error.Column)))
                        problems.Add(new ValidationProblem(ex.Error, string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_ValidateWhere, c + 1, step + 1)));
                }
                runs++;
            }
        }
        return new ValidationResult(problems, program.ExecutionPath, runs, slowest, slowestChick);
    }

    // ───────────── 저장·넣기·올리기 ─────────────

    /// <summary>개인 스크립트 에셋으로 저장한다(오류 없이 해석될 때만). 설치한 패키지에서 연 것이면 개인 사본이 된다. 되면 true.</summary>
    private bool Save()
    {
        if (!_open) return false;
        if (_program is null)
        {
            Ui.Info(this, UiStrings.EffectMaker_FixSourceFirst, UiStrings.EffectMaker_Title);
            return false;
        }
        ScriptTrustStore.TrustLocal(ScriptLibrary.Add(_source));
        AssetEntry? saved = null;
        if (!AppAssets.Try(this, () => saved = AppAssets.Store.SavePersonalScript(_source, _entryId), UiStrings.EffectMaker_SaveError) || saved is null)
            return false;
        _entryId = _shownId = saved.Id;
        _savedSource = _source;
        RebuildList();
        if (!_list.Items.OfType<ListBoxItem>().Any(item => (string?)item.Tag == saved.Id)) _showAll.IsChecked = true;
        SelectInList(saved.Id);
        UpdateButtons();
        return true;
    }

    /// <summary>지금 메인 편집기의 레이어 끝에 미리보기 설정 값·그라데이션의 스크립트 효과를 넣는다(모든 프레임, 실행취소 한 단계).</summary>
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
        if (_program is null)
        {
            Ui.Info(this, UiStrings.EffectMaker_FixSourceFirst, UiStrings.EffectMaker_Title);
            return;
        }
        var hash = project.AddScript(_source);
        ScriptTrustStore.TrustLocal(hash);
        var effect = PreviewEffect(_program) with { ScriptHash = hash };
        var list = project.Layers[index].Effects.Append(effect).ToList();
        project.Edit(string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_InsertUndo, CurrentName()), ProjectChange.Layers,
            () => project.SetLayerEffects(index, list));
        _ctx.SetCurrentLayer(index);
    }

    /// <summary>저장하고 검증을 통과하면 이 스크립트 하나를 고른 게시 창을 연다(태그·라이선스·버전을 거기서 적고 올림 — 미리보기는 저절로 구움).</summary>
    private async void Upload()
    {
        if (!Save() || _entryId is not { } id) return;
        if (!await ValidateAsync())
        {
            Ui.Info(this, UiStrings.EffectMaker_ValidateFirst, UiStrings.EffectMaker_Title);
            return;
        }
        await AssetPublishDialog.RunAsync(this, id);
    }

    /// <summary>저장하지 않은 고침이 있으면 저장·버림·취소를 묻는다. 계속해도 되면 true.</summary>
    private bool ConfirmDiscard()
    {
        if (!Dirty) return true;
        var answer = Ui.Ask(this, string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_UnsavedQuestion, CurrentName()), UiStrings.EffectMaker_Title,
            MessageButtons.YesNoCancel, MessageIcon.Question, okText: UiStrings.EffectMaker_Save, noText: UiStrings.EffectMaker_Discard);
        if (answer == DialogResult.Cancel || answer == DialogResult.None) return false;
        if (answer == DialogResult.Yes) return Save();
        _savedSource = _source; // 버림: 다음 확인(창 닫기)에서 다시 묻지 않게
        return true;
    }

    // ───────────── 규칙·AI 칸 ─────────────

    /// <summary>규칙 창을 연다(이미 열려 있으면 앞으로). 열린 스크립트가 있으면 그 원문을 붙여 복사할 수 있다.</summary>
    private void ShowRules()
    {
        if (_rules is { } open)
        {
            open.Activate();
            return;
        }
        var window = new Ai.ScriptGuideWindow(() => _open ? _source : null);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_rules, window)) _rules = null;
        };
        _rules = window;
        window.Show(this);
    }

    /// <summary>
    /// AI 칸을 열거나 닫는다. 보통 창이면 칸 너비만큼 창을 넓히고(화면 오른쪽을 넘으면 왼쪽으로 옮김) 닫으면 그만큼 줄인다.
    /// 최대화·전체 화면이면 창 크기는 그대로 두고 가운데 원문 칸이 좁아진다. 칸은 처음 열 때 만든다(편집기 MCP 서버를 시작하지 못하면 단추를 끔).
    /// </summary>
    private void ShowAiPanel(bool open)
    {
        if (open == _aiHost.IsVisible) return;
        if (open && _aiPanel is null)
        {
            // 메인 창: 효과 만들기를 연 주인(보통 메인 창), 아니면 앱의 메인 창
            var main = Owner as MainForm
                       ?? (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainForm;
            if (main is null || main.EnsureAiServer(this) is not { } mcp)
            {
                _aiToggle.IsChecked = false;
                return;
            }
            _aiPanel = new Ai.EffectMakerAiPanel(mcp, ((Ai.IAiHost)main).Settings, main.ShowAiChat, () => _aiToggle.IsChecked = false, CanvasImage, PreviewFrame);
            _aiHost.Child = _aiPanel;
        }
        double step = AiPanelWidth + _aiHost.Margin.Left;
        if (open)
        {
            _aiGrowth = WindowState == WindowState.Normal ? step : 0;
            MinWidth = BaseMinWidth + step;
            if (_aiGrowth > 0)
            {
                Width += _aiGrowth;
                if (Screens.ScreenFromWindow(this) is { } screen)
                {
                    int over = Position.X + (int)Math.Ceiling(Width * screen.Scaling) - screen.WorkingArea.Right;
                    if (over > 0) Position = new PixelPoint(Math.Max(screen.WorkingArea.X, Position.X - over), Position.Y);
                }
            }
        }
        else
        {
            MinWidth = BaseMinWidth;
            if (_aiGrowth > 0 && WindowState == WindowState.Normal) Width = Math.Max(BaseMinWidth, Width - _aiGrowth);
            _aiGrowth = 0;
        }
        _aiHost.IsVisible = open;
        if (open) _aiPanel!.Activate();
    }

    // ───────────── AI 도구(PixelTools의 효과 만들기 도구가 부름) ─────────────

    /// <summary>열려 있는 효과 만들기(없으면 null).</summary>
    internal static EffectMakerWindow? Current => s_window;

    /// <summary>열린 스크립트가 있는지(목록에서 골랐거나 새로 만듦).</summary>
    internal bool HasScript => _open;

    /// <summary>열어 둔 스크립트의 에셋 ID(목록에서 연 것, 새 스크립트·열린 것이 없으면 null).</summary>
    internal string? ShownId => _open ? _shownId : null;

    /// <summary>열린 문서의 지금 프레임 합성(없으면 null) — AI 칸의 "캔버스 첨부".</summary>
    private PixelImage? CanvasImage()
        => _ctx.Project is { Frames.Count: > 0 } p ? p.Flatten(Math.Clamp(_ctx.CurrentFrame, 0, p.Frames.Count - 1)) : null;

    /// <summary>미리보기에 지금 보이는 칸(원본 보기면 효과 없는 견본, 그릴 것이 없으면 null) — AI 칸의 "미리보기 첨부".</summary>
    private PixelImage? PreviewFrame()
    {
        if (PreviewEffects() is not { Count: > 0 } effects || _program is null) return null;
        var chick = PreviewChicks.Load(_program.PreviewChick);
        int steps = _program.PreviewFrames;
        return _original.On()
            ? AssetPreview.Original(effects, chick, 0, steps)
            : AssetPreview.Render(effects, chick, Math.Clamp(_preview.Index, 0, steps - 1), steps);
    }

    /// <summary>편집기의 지금 원문(줄바꿈을 정리한 것).</summary>
    internal string Source => _source;

    /// <summary>마지막으로 불러오거나 저장한 뒤 고친 것이 있는지.</summary>
    internal bool IsDirty => Dirty;

    /// <summary><paramref name="program"/>의 선언에 맞춘 미리보기 설정 값(빠진 키는 기본값).</summary>
    internal IReadOnlyDictionary<string, object> PreviewValues(ScriptEffectProgram program) => program.NormalizeSettings(_previewValues);

    /// <summary>
    /// AI가 쓴 원문을 편집기에 넣는다: 열린 스크립트가 있으면 달라진 곳만 바꾸고(편집기 실행취소 한 단계, 커서·스크롤 유지 — 저장 전 상태가 됨),
    /// 없으면 새 스크립트(저장 전)로 연다. 미리보기·상태 칸은 입력 기다림 없이 곧바로 다시 그린다. 문서(프로젝트)는 건드리지 않는다.
    /// </summary>
    internal void SetSourceFromAi(string source)
    {
        source = ScriptEffectProgram.NormalizeSource(source);
        if (!_open)
        {
            _shownId = null;
            _entryId = null;
            Load(source, saved: false);
            SelectInList(null);
            return;
        }
        _code.Replace(source);
        if (_source != _code.Text) Edited(); // 편집기의 TextChanged가 늦게 오는 경우
        _debounce.Stop();
        Rerender();
    }

    // ───────────── 조각 ─────────────

    private static TextBlock Hint(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, Margin = new Thickness(3, 2, 3, 4),
    };

    /// <summary>
    /// 칸 띠를 다시 만든다: 미리보기가 그려 둔 칸마다 작은 그림(같은 비트맵을 함께 씀 — 따로 그리지 않음)과 번호. 누르면 재생을 멈추고 그 칸을 본다.
    /// </summary>
    private void RebuildStrip()
    {
        _strip.Children.Clear();
        var frames = _preview.Frames;
        for (int i = 0; i < frames.Count; i++)
        {
            int index = i;
            var number = new TextBlock
            {
                Text = (i + 1).ToString(CultureInfo.CurrentCulture), FontSize = AppTheme.SmallFontSize, Foreground = AppTheme.DimTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var thumb = new Border
            {
                Child = new StackPanel { Children = { new FrameThumb(frames[i]) { Width = ThumbSize, Height = ThumbSize }, number } },
                BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), Padding = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
            }.Tip(string.Format(CultureInfo.CurrentCulture, UiStrings.EffectMaker_FrameThumbTip, i + 1));
            thumb.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                ShowStep(index);
            };
            _strip.Children.Add(thumb);
        }
        _stripScroll.IsVisible = frames.Count > 0;
        HighlightStrip();
    }

    /// <summary>
    /// 보이는 칸의 작은 그림에 테두리를 두른다(재생 중이면 따라감). 띠는 저절로 밀지 않는다 — 재생 중에 띠가 움직이면 누르려던 칸이 달아난다.
    /// </summary>
    private void HighlightStrip()
    {
        for (int i = 0; i < _strip.Children.Count; i++)
        {
            var thumb = (Border)_strip.Children[i];
            bool shown = i == _preview.Index;
            thumb.BorderBrush = shown ? AppTheme.AccentBrush : Brushes.Transparent;
        }
    }

    /// <summary>칸 띠에서 고른 칸: 재생을 멈추고(효과 없이 보기도 끔) 그 칸을 보인다.</summary>
    private void ShowStep(int step)
    {
        if (_timer.IsEnabled) TogglePlay();
        if (_original.On()) _original.IsChecked = false;
        _preview.ShowStep(step);
    }

    private const int ThumbSize = 40;

    /// <summary>미리보기: 체크무늬 위에 정수 배로 키운 칸(재생은 창의 타이머가 넘김), 원본 보기면 효과 없는 견본.</summary>
    private sealed class MakerPreview : Control
    {
        private WriteableBitmap?[] _frames = [];
        private WriteableBitmap? _original;
        private int _index;

        /// <summary>그린 칸들이 바뀜(칸 띠를 다시 만듦).</summary>
        public event Action? FramesChanged;

        /// <summary>보이는 칸 번호가 바뀜(재생으로 넘어가거나 고름).</summary>
        public event Action? IndexChanged;

        /// <summary>보이는 칸 번호(재생 중이면 넘어가는 칸).</summary>
        public int Index => _index;

        /// <summary>그려 둔 칸들(칸 띠가 같은 비트맵을 보임 — 다음 <see cref="SetFrames"/>에서 버림).</summary>
        public IReadOnlyList<WriteableBitmap?> Frames => _frames;

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

        /// <summary>새 칸들로 바꾼다. 보던 칸 번호는 새 칸 수 안에서 그대로 둔다(멈춰 둔 칸에서 설정을 바꿔 볼 수 있게).</summary>
        public void SetFrames(IReadOnlyList<PixelImage> frames, PixelImage? original)
        {
            var old = _frames;
            _original?.Dispose();
            _frames = frames.Select(ImageUtil.ToBitmap).ToArray<WriteableBitmap?>();
            _original = original is null ? null : ImageUtil.ToBitmap(original);
            _index = Math.Clamp(_index, 0, Math.Max(0, _frames.Length - 1));
            FramesChanged?.Invoke();
            foreach (var frame in old) frame?.Dispose(); // 칸 띠가 새 비트맵으로 바뀐 뒤에 버린다
            InvalidateVisual();
        }

        public void Step()
        {
            if (_frames.Length <= 1 || _showOriginal) return;
            _index = (_index + 1) % _frames.Length;
            IndexChanged?.Invoke();
            InvalidateVisual();
        }

        /// <summary><paramref name="step"/>번째 칸을 보인다(칸 수 밖이면 무시).</summary>
        public void ShowStep(int step)
        {
            if (step < 0 || step >= _frames.Length) return;
            _index = step;
            IndexChanged?.Invoke();
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

    /// <summary>칸 띠의 작은 그림: 미리보기가 그려 둔 비트맵을 체크무늬 위에 맞춰 그린다.</summary>
    private sealed class FrameThumb(WriteableBitmap? bitmap) : Control
    {
        public override void Render(DrawingContext g)
        {
            var b = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.PanelBrush, b);
            if (bitmap is null) return;
            var src = new Rectangle(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            double fit = Math.Min(b.Width / src.Width, b.Height / src.Height);
            ImageUtil.DrawFitted(g, bitmap, src, b, fit >= 1 ? (int)Math.Floor(fit) : 0, checker: true);
        }
    }
}
