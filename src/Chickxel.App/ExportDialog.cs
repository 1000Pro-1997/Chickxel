using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>내보내기 창의 형식 종류(이미지의 파일 형식은 그 시트의 <see cref="ExportSettings.ImageFormat"/>).</summary>
public enum ExportFormat
{
    /// <summary>GIF 애니메이션.</summary>
    Gif,
    /// <summary>움직이는 PNG(APNG).</summary>
    Apng,
    /// <summary>이미지 시트(PNG·JPG 등, Unity .meta 선택) — 프로젝트의 내보내기 설정대로.</summary>
    Image,
    /// <summary>프레임마다 PNG(폴더로).</summary>
    Frames,
}

/// <summary>
/// 내보내기 창(Ctrl+E, 파일 > 내보내기…, 미리보기 "GIF로 저장…"). 맨 왼쪽 칸은 이 프로젝트의 <b>내보내기 목록</b>(프로젝트의 시트 + <see cref="ExportSettings.Entries"/>):
/// 줄마다 [저장할 때] 이름·종류 [빼기], 제목 줄의 [더하기]는 고른 줄을 다른 이름으로 복사한다. 줄을 고르면 가운데 칸(세로 스크롤)이 그 줄의 것이 된다 —
/// 맨 위 "형식" 묶음의 콤보(GIF · 움직이는 PNG · PNG · JPG · TGA · BMP · 프레임마다 PNG, 종류를 바꾸면 그 줄이 그 종류가 됨)와 이름·위치, 아래 설정 묶음.
/// 가운데 칸 | 끌어서 너비를 바꾸는 나눔줄 | 오른쪽 미리보기.
/// <list type="bullet">
/// <item>시트 줄(<see cref="SheetExportPages"/>): 콤보 아래 "Unity용으로 내보내기(.meta)" 체크, 그 아래 시트 설정 묶음(+ Unity Import Settings), 오른쪽 결과 시트 미리보기.
/// 파란 "내보내기"는 OK로 닫혀 호출한 쪽이 그 줄을 내보내고(<see cref="SheetEntry"/>), "다른 이름으로 저장…"은 같은 시트를 독립 파일로 쓴다(창은 열린 채 결과 줄).</item>
/// <item>애니메이션 줄(<see cref="AnimationExportPart"/>): 범위·배율·속도·반복·배경 묶음과 재생 미리보기. "내보내기"는 그 줄의 대상에 바로 쓴다(창은 열린 채).</item>
/// </list>
/// 창 크기·위치·나눔 비율은 <see cref="AppSettings"/>에 기억한다. Enter로 닫히지 않는다(Esc = 닫기).
/// </summary>
public sealed class ExportDialog : DialogBase
{
    private const int DefaultWidth = 1360, DefaultHeight = 760, MinW = 1020, MinH = 480, ListWidth = 260;

    private readonly AppSettings _settings;
    private readonly SheetExportPages _sheet;
    private readonly AnimationExportPart _anim;
    /// <summary>목록 | 가운데 설정 | 나눔줄 | 오른쪽 미리보기.</summary>
    private readonly Grid _columns;
    private readonly ComboBox _formatCombo;
    /// <summary>형식 콤보 항목 순서: GIF, APNG, 이미지 형식들(<see cref="ExportImageFormat"/> 순서), 프레임마다 PNG.</summary>
    private readonly (ExportFormat Kind, ExportImageFormat Image)[] _choices;
    private readonly Button _saveCopy;
    private readonly Control _copyResultHost;
    private readonly List<string> _exported = new();
    private ExportFormat _format;
    private bool _canExportAnimation, _syncingCombo;
    private readonly EditorContext _ctx;
    private readonly SpriteProject _project;
    /// <summary>내보내기 목록 줄들(프로젝트의 시트 + 목록의 줄).</summary>
    private readonly StackPanel _list = new() { Spacing = 1 };
    /// <summary>고른 줄: −1 = 프로젝트의 시트, 0 이상 = <see cref="ExportSettings.Entries"/>의 그 줄.</summary>
    private int _selected = -1;
    private readonly EventHandler<ProjectChangedEventArgs> _onProjectChanged;
    private readonly TextBox _nameBox = new() { MinWidth = 120 };
    private readonly TextBlock _extLabel = new() { Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _folderLabel = new() { Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.LeadingCharacterEllipsis };

    /// <param name="ctx">편집기 상태(현재 프로젝트가 있어야 함).</param>
    /// <param name="settings">형식·창 크기·위치·나눔 비율·접은 묶음·애니메이션 설정을 기억할 앱 설정.</param>
    /// <param name="format">처음 고를 형식 종류(이미지면 프로젝트의 이미지 형식).</param>
    /// <param name="ppuChanged">사용자가 PPU를 바꿨을 때(마지막 PPU 기억용).</param>
    /// <param name="entry">처음 고를 목록 줄(−1 = 프로젝트의 시트, 0 이상 = 목록의 그 줄). null이면 <paramref name="format"/>에 맞는 줄.</param>
    public ExportDialog(EditorContext ctx, AppSettings settings, ExportFormat format, Action<float>? ppuChanged = null, int? entry = null)
        : base(UiStrings.S_UnityExportDialog_UnityExportDialog_C53EAC78FD)
    {
        _settings = settings;
        _ctx = ctx;
        _project = ctx.Project ?? throw new InvalidOperationException(nameof(ctx.Project));
        // 시트 설정은 바로 적용되고 다른 이름으로 저장·애니메이션은 내보낸 뒤에도 창이 열려 있으므로 취소가 아니라 닫기.
        CancelButtonControl.Content = UiStrings.S_Ui_CloseOnly_11312F9A81;
        OkButton.Background = AppTheme.AccentBrush;
        OkButton.Foreground = Brushes.White;
        OkButton.FontWeight = FontWeight.Bold;
        OkButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        MinWidth = MinW;
        MinHeight = MinH;
        RestorePlacement();

        _sheet = new SheetExportPages(ctx, settings, this, Exported, ppuChanged);
        _anim = new AnimationExportPart(ctx, settings, this, can =>
        {
            _canExportAnimation = can;
            UpdateOkButton();
        }, Exported);

        // ── 형식 묶음: 제목 줄(오른쪽 끝 프리셋) + 콤보 + 이미지 형식의 Unity 체크·JPG 품질 ──
        _choices =
        [
            (ExportFormat.Gif, default), (ExportFormat.Apng, default),
            .. Enum.GetValues<ExportImageFormat>().Select(f => (ExportFormat.Image, f)),
            (ExportFormat.Frames, default),
        ];
        _formatCombo = Ui.Combo(0, [.. _choices.Select(c => ChoiceName(c.Kind, c.Image))]);
        _formatCombo.Width = double.NaN;
        _formatCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
        var title = new TextBlock { Text = UiStrings.Export_Format, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        DockPanel.SetDock(_sheet.PresetButton, Dock.Right);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2), Children = { _sheet.PresetButton, title } };
        var formatGroup = new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4, 6, 6),
            Margin = new Thickness(2, 4, 2, 2),
            Background = Brushes.Transparent,
            Child = Ui.Stack(header, _formatCombo, NameRows(), _sheet.FormatOptions),
        }.HelpAt(SheetExportPages.GuidePage);

        // ── 왼쪽 설정 | 나눔줄 | 오른쪽 미리보기 ──
        var left = Ui.ScrollHost(Ui.Stack(formatGroup, _sheet.SettingsColumn, _anim.Groups));
        var listPane = ListPane();
        var right = new Panel { Children = { _sheet.Preview, _anim.PreviewColumn } };
        double split = double.IsFinite(settings.ExportSplit) ? Math.Clamp(settings.ExportSplit, 0.2, 0.8) : 0.46;
        _columns = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(ListWidth, GridUnitType.Pixel),
                new ColumnDefinition(split, GridUnitType.Star) { MinWidth = 340 },
                new ColumnDefinition(6, GridUnitType.Pixel),
                new ColumnDefinition(1 - split, GridUnitType.Star) { MinWidth = 260 },
            },
        };
        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Columns,
            Background = AppTheme.BorderBrush,
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        }.Tip(UiStrings.S_UnityExportDialog_UnityExportDialog_22A1711719).HelpAt(SheetExportPages.GuidePage, anchor: "drag it to split the width");
        Grid.SetColumn(left, 1);
        Grid.SetColumn(splitter, 2);
        Grid.SetColumn(right, 3);
        _columns.Children.Add(listPane);
        _columns.Children.Add(left);
        _columns.Children.Add(splitter);
        _columns.Children.Add(right);
        // 대화상자 본문(StackPanel)은 높이를 주지 않으므로 칸을 바깥 DockPanel의 채우기 칸에 바로 둔다.
        var root = (DockPanel)Content!;
        root.Children.Remove((Control)Body.Parent!);
        root.Children.Add(_columns);

        // ── 단추: [내보내기(…)] [다른 이름으로 저장…(이미지)] [닫기], 왼쪽 끝 = 다른 이름으로 저장 결과 줄 ──
        _saveCopy = Ui.Button(UiStrings.Export_SaveCopy, (_, _) => Dispatcher.UIThread.Post(_sheet.SaveCopy))
            .Tip(UiStrings.Export_SaveCopyTip).HelpAt(SheetExportPages.GuidePage);
        _saveCopy.MinWidth = 90;
        _saveCopy.Width = double.NaN;
        _saveCopy.Padding = new Thickness(10, 3);
        AddFooter(_saveCopy, CancelButtonControl);
        _copyResultHost = new Panel { Children = { _sheet.CopyResult } };
        AddFooterLeft(_copyResultHost);

        format = Enum.IsDefined(format) ? format : ExportFormat.Image;
        _formatCombo.SelectionChanged += (_, _) =>
        {
            if (_syncingCombo || _formatCombo.SelectedIndex < 0) return;
            var (kind, image) = _choices[_formatCombo.SelectedIndex];
            ChangeFormat(kind, image);
        };
        _onProjectChanged = (_, _) => Dispatcher.UIThread.Post(OnProjectChanged);
        _project.Changed += _onProjectChanged;
        // 실행취소·프리셋 등으로 고친 시트의 이미지 형식이 바뀌면 콤보도 따라간다.
        _sheet.Refreshed += SyncImageChoice;
        _anim.EntryChanged += RebuildList;
        // 프로젝트의 시트는 OK로 닫혀 호출한 쪽이 내보내고(목록의 다른 시트 줄은 그 줄만), 애니메이션 줄은 창을 열어 둔 채 그 대상에 쓴다(결과 보기).
        OkButton.Click += (_, _) =>
        {
            if (_format == ExportFormat.Image) return;
            DialogResult = DialogResult.None;
            Dispatcher.UIThread.Post(_anim.Export);
        };
        var entries = _project.Export.Entries;
        if (entry is { } wanted && (wanted == -1 && _project.Export.SheetListed || wanted >= 0 && wanted < entries.Count)) Select(wanted);
        else if (format == ExportFormat.Image) Select(_project.Export.SheetListed ? -1 : Math.Max(-1, entries.FindIndex(e => e.Sheet is not null)));
        else
        {
            // 애니메이션 형식으로 열면(미리보기 "GIF로 저장…" 등) 그 형식의 첫 줄, 없으면 새 줄
            var f = ToAnimation(format);
            int i = entries.FindIndex(e => e.Animation?.Format == f);
            Select(i >= 0 ? i : AddAnimation(f, null));
        }
    }

    // ───────────── 내보내기 목록 ─────────────

    private static AnimationExportFormat ToAnimation(ExportFormat f) => f switch
    {
        ExportFormat.Apng => AnimationExportFormat.Apng,
        ExportFormat.Frames => AnimationExportFormat.FramePngs,
        _ => AnimationExportFormat.Gif,
    };

    private static ExportFormat FromAnimation(AnimationExportFormat f) => f switch
    {
        AnimationExportFormat.Apng => ExportFormat.Apng,
        AnimationExportFormat.FramePngs => ExportFormat.Frames,
        _ => ExportFormat.Gif,
    };

    private static string KindName(AnimationExportFormat f) => f switch
    {
        AnimationExportFormat.Apng => UiStrings.Export_FormatApng,
        AnimationExportFormat.FramePngs => UiStrings.Export_FormatFrames,
        _ => UiStrings.S_GifExportDialog_ChooseAndExport_AC2A7BE712,
    };

    private static string Extension(AnimationExportFormat f) => f switch
    {
        AnimationExportFormat.Gif => ".gif",
        AnimationExportFormat.Apng => ".png",
        _ => "",
    };

    /// <summary>고른 줄(목록의 다른 줄이면 그 줄, 프로젝트의 시트면 null).</summary>
    private ExportEntry? SelectedEntry => _selected >= 0 && _selected < _project.Export.Entries.Count ? _project.Export.Entries[_selected] : null;

    /// <summary>고른 시트 줄의 설정(애니메이션 줄이면 null).</summary>
    private ExportSettings? SelectedSheet => _selected < 0 ? _project.Export : SelectedEntry?.Sheet;

    /// <summary>프로젝트 파일 폴더(저장 전이면 마지막 폴더나 그림 폴더).</summary>
    private string ProjectDir => _project.Path is { } p ? Path.GetDirectoryName(Path.GetFullPath(p))!
        : _settings.LastFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    /// <summary>왼쪽 칸 "내보내기 목록": 제목 줄(오른쪽 끝 [더하기]) + 줄마다 [저장할 때] 이름·종류 [빼기].</summary>
    private Control ListPane()
    {
        var title = new TextBlock { Text = UiStrings.ExportList_Title, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        var add = new Button { Content = Ui.IconLabel(VectorIcon.Plus, UiStrings.ExportList_Add), Padding = new Thickness(8, 1) }.Tip(UiStrings.ExportList_AddTip);
        add.Click += (_, _) => Dispatcher.UIThread.Post(AddCopy);
        var presets = Ui.Button(VectorIcon.ChevronDown, UiStrings.ExportList_Presets, null, iconAfter: true).Tip(UiStrings.ExportList_PresetsTip);
        presets.Padding = new Thickness(8, 1);
        presets.Margin = new Thickness(4, 0, 0, 0);
        presets.Click += (_, _) => PopupMenus.Show(presets, ListPresetMenu(), PlacementMode.BottomEdgeAlignedRight);
        // 제목 줄 아래 단추 줄: [더하기] [목록 프리셋 ▾]
        var header = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 6),
            Children = { Ui.Row(title, Ui.InfoBadge(UiStrings.ExportList_Info)), Ui.Row(add, presets) },
        };
        DockPanel.SetDock(header, Dock.Top);
        var hint = new TextBlock { Text = UiStrings.ExportList_SaveHint, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 2, 0) };
        DockPanel.SetDock(hint, Dock.Bottom);
        return new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4, 6, 6),
            Margin = new Thickness(2, 4, 6, 2),
            Child = new DockPanel { Children = { header, hint, new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } } },
        };
    }

    // ───────────── 목록 프리셋 ─────────────

    /// <summary>목록 프리셋 파일(앱 설정 폴더).</summary>
    private static string ListPresetPath => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, ExportListPresets.FileName);

    /// <summary>목록 프리셋을 읽는다. 읽지 못하면 알리고 null.</summary>
    private List<ExportListPreset>? LoadListPresets()
    {
        try
        {
            return ExportListPresets.Load(ListPresetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Ui.Error(this, ex.Message, UiStrings.ExportList_Presets);
            return null;
        }
    }

    private bool SaveListPresets(List<ExportListPreset> presets)
    {
        try
        {
            ExportListPresets.Save(ListPresetPath, presets);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message, UiStrings.ExportList_Presets);
            return false;
        }
    }

    /// <summary>[목록 프리셋 ▾] 메뉴: 지금 목록 저장…, 저장한 프리셋(누르면 목록을 그것으로 바꿈), 지우기 ▸.</summary>
    private IEnumerable<object> ListPresetMenu()
    {
        MenuItem Item(string text, Action act)
        {
            var m = new MenuItem { Header = new TextBlock { Text = text } };
            m.Click += (_, _) => Dispatcher.UIThread.Post(act);
            return m;
        }
        var presets = LoadListPresets() ?? [];
        var items = new List<object> { Item(UiStrings.ExportList_PresetSave, SaveListPreset) };
        if (presets.Count == 0) return items;
        items.Add(new Separator());
        foreach (var p in presets)
        {
            var item = Item(p.Name, () => ApplyListPreset(p));
            ToolTip.SetTip(item, UiStrings.FormatExport_ListPresetRows(count: (p.Sheet.ExportFrames || p.Sheet.ExportParts ? 1 : 0) + p.Rows.Count));
            items.Add(item);
        }
        items.Add(new Separator());
        var delete = new MenuItem { Header = UiStrings.ExportList_PresetDelete };
        foreach (var p in presets)
            delete.Items.Add(Item(p.Name, () => DeleteListPreset(p.Name)));
        items.Add(delete);
        return items;
    }

    /// <summary>지금 목록을 이름 붙여 저장한다(같은 이름이면 덮어쓸지 묻기).</summary>
    private void SaveListPreset()
    {
        if (LoadListPresets() is not { } presets) return;
        CommitName();
        var name = InputDialog.Ask(this, UiStrings.ExportList_PresetSave.TrimEnd('…'), UiStrings.ExportList_PresetName, "", presets.Select(x => x.Name))?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        int at = presets.FindIndex(x => x.Name == name);
        if (at >= 0 && Ui.Ask(this, UiStrings.FormatExport_ListPresetOverwrite(name: name), UiStrings.ExportList_Presets, MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes)
            return;
        var preset = ExportListPreset.Capture(_project.Export, _project.BaseName, name);
        if (at >= 0) presets[at] = preset;
        else presets.Add(preset);
        SaveListPresets(presets);
    }

    /// <summary>목록을 프리셋으로 바꾼다(실행취소 1단계) — 파일 이름의 프로젝트 이름 자리는 이 프로젝트 이름.</summary>
    private void ApplyListPreset(ExportListPreset preset)
    {
        _project.Edit(UiStrings.FormatExport_ListPresetApplied(name: preset.Name), ProjectChange.Settings, () => preset.ApplyTo(_project.Export, _project.BaseName));
        Select(_project.Export.SheetListed ? -1 : 0);
    }

    private void DeleteListPreset(string name)
    {
        if (LoadListPresets() is not { } presets) return;
        if (Ui.Ask(this, UiStrings.FormatExport_ListPresetDeleteAsk(name: name), UiStrings.ExportList_Presets, MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        presets.RemoveAll(p => p.Name == name);
        SaveListPresets(presets);
    }

    /// <summary>형식 묶음 아래 이름·위치 줄: 고른 줄이 만드는 파일 이름(확장자 앞)과 폴더.</summary>
    private Control NameRows()
    {
        _nameBox.LostFocus += (_, _) => CommitName();
        _nameBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitName();
            e.Handled = true;
        };
        var change = Ui.Button(UiStrings.ExportList_ChangeTarget, (_, _) => Dispatcher.UIThread.Post(ChooseLocation));
        Grid Line(string label, Control a, Control b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
            var l = new TextBlock { Text = label, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Width = 44 };
            Grid.SetColumn(a, 1);
            Grid.SetColumn(b, 2);
            g.Children.Add(l);
            g.Children.Add(a);
            g.Children.Add(b);
            return g;
        }
        _extLabel.Margin = new Thickness(4, 0, 0, 0);
        change.Margin = new Thickness(6, 0, 0, 0);
        return Ui.Stack(Line(UiStrings.ExportList_Name, _nameBox, _extLabel), Line(UiStrings.ExportList_Folder, _folderLabel, change));
    }

    /// <summary>이름·위치 줄을 고른 줄에 맞춘다.</summary>
    private void ShowName()
    {
        if (SelectedSheet is { } sheet)
        {
            var path = _project.Path is { } pp ? ProjectExport.GetExportImagePath(sheet, pp) : null;
            _nameBox.Text = sheet.FileName ?? _project.BaseName;
            _extLabel.Text = sheet.ImageFormat.Extension();
            _folderLabel.Text = path is null ? sheet.Folder ?? "" : Path.GetDirectoryName(path);
        }
        else if (SelectedEntry?.Animation is { } a)
        {
            var full = AnimationPath(a);
            _nameBox.Text = Path.GetFileNameWithoutExtension(full.TrimEnd('/', '\\'));
            _extLabel.Text = Extension(a.Format);
            _folderLabel.Text = Path.GetDirectoryName(full.TrimEnd('/', '\\'));
        }
        ToolTip.SetTip(_folderLabel, _folderLabel.Text);
    }

    private string AnimationPath(AnimationExport a) => _project.Path is { } pp ? AnimationExportWriter.ResolvePath(a, pp) : a.Path;

    /// <summary>이름 칸을 고른 줄에 넣는다(실행취소 1단계, 같으면 그대로). 비우면 시트는 프로젝트 이름.</summary>
    private void CommitName()
    {
        var name = string.Join("_", (_nameBox.Text ?? "").Trim().Split(Path.GetInvalidFileNameChars()));
        if (SelectedSheet is { } sheet)
        {
            string? value = name.Length == 0 || name == _project.BaseName && sheet.FileName is null ? null : name;
            if (value != sheet.FileName) _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => sheet.FileName = value);
        }
        else if (SelectedEntry?.Animation is { } a && name.Length > 0)
        {
            var full = AnimationPath(a).TrimEnd('/', '\\');
            var target = Path.Combine(Path.GetDirectoryName(full) ?? ProjectDir, name + Extension(a.Format));
            var stored = AnimationExportWriter.StoredPath(target, _project.Path);
            if (stored != a.Path) _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => a.Path = stored);
        }
        ShowName();
    }

    /// <summary>위치 바꾸기: 시트는 폴더 고르기(프로젝트 폴더 기준 상대 경로로), 애니메이션은 형식에 맞는 저장 창.</summary>
    private void ChooseLocation()
    {
        if (SelectedSheet is { } sheet)
        {
            var current = string.IsNullOrWhiteSpace(sheet.Folder) ? ProjectDir : Path.GetFullPath(Path.Combine(ProjectDir, sheet.Folder));
            if (Ui.PickFolder(this, UiStrings.ExportList_Folder, current) is not { } folder) return;
            var relative = Path.GetRelativePath(ProjectDir, folder);
            string? value = relative == "." ? null : Path.IsPathRooted(relative) ? folder : relative;
            if (value != sheet.Folder) _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => sheet.Folder = value);
        }
        else _anim.ChooseTarget();
        ShowName();
    }

    /// <summary>[더하기]: 고른 줄을 복사해 다른 이름(뒤에 _2 …)으로 목록 끝에 넣고 고른다(저장할 때는 꺼짐). 목록이 비었으면 프로젝트의 시트를 다시 넣는다.</summary>
    private void AddCopy()
    {
        var x = _project.Export;
        if (!x.SheetListed && x.Entries.Count == 0)
        {
            _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => x.ExportFrames = true);
            Select(-1);
            return;
        }
        if (SelectedEntry?.Animation is { } a)
        {
            Select(AddAnimation(a.Format, a));
            return;
        }
        var source = SelectedSheet ?? x;
        var copy = source.CloneSheet();
        copy.ExportOnSave = false;
        copy.FileName = UniqueSheetName(source.FileName ?? _project.BaseName, copy);
        if (!copy.SheetListed) copy.ExportFrames = true;
        _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => x.Entries.Add(ExportEntry.ForSheet(copy)));
        Select(x.Entries.Count - 1);
    }

    /// <summary>시트 이름 <paramref name="baseName"/>에 _2, _3 …을 붙여 목록의 다른 시트와 같은 폴더·이름·형식이 되지 않게 한다.</summary>
    private string UniqueSheetName(string baseName, ExportSettings sheet)
    {
        var stem = System.Text.RegularExpressions.Regex.Replace(baseName, @"_\d+$", "");
        var taken = _project.Export.AllSheets
            .Select(s => (Folder: s.Folder ?? "", Name: s.FileName ?? _project.BaseName, s.ImageFormat))
            .ToHashSet();
        for (int n = 2; ; n++)
        {
            var name = $"{stem}_{n}";
            if (!taken.Contains((sheet.Folder ?? "", name, sheet.ImageFormat))) return name;
        }
    }

    /// <summary>
    /// 애니메이션 줄을 목록 끝에 넣는다: <paramref name="source"/>가 있으면 그 사본(다른 이름), 없으면 지금 칸의 범위·배율 등과 프로젝트 이름의 기본 대상.
    /// 넣은 줄 번호.
    /// </summary>
    private int AddAnimation(AnimationExportFormat format, AnimationExport? source)
    {
        var entry = source?.Clone() ?? new AnimationExport
        {
            Format = format,
            TagName = _anim.RangeTag?.Name,
            Scale = _anim.Scale,
            PlayCount = _anim.PlayCount,
            Background = _anim.BackgroundColor,
            Timing = _anim.Timing,
        };
        entry.OnSave = false;
        var dir = source is null ? ProjectDir : Path.GetDirectoryName(AnimationPath(source).TrimEnd('/', '\\')) ?? ProjectDir;
        var stem = source is null
            ? AnimationExportWriter.FramesBaseName(_project, _anim.RangeTag, _project.Path) + (format == AnimationExportFormat.FramePngs ? "_frames" : "")
            : System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(AnimationPath(source).TrimEnd('/', '\\')), @"_\d+$", "");
        var taken = _project.Export.Animations.Select(a => AnimationPath(a).TrimEnd('/', '\\')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string Candidate(int n) => Path.Combine(dir, (n == 1 ? stem : $"{stem}_{n}") + Extension(format));
        int n = 1;
        while (taken.Contains(Candidate(n))) n++;
        entry.Path = AnimationExportWriter.StoredPath(Candidate(n), _project.Path);
        _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => _project.Export.Entries.Add(ExportEntry.ForAnimation(entry)));
        return _project.Export.Entries.Count - 1;
    }

    /// <summary>
    /// 형식 콤보: 시트 줄에서 이미지 형식이면 그 시트의 형식만 바꾸고, 종류가 바뀌면(시트 ↔ 애니메이션) 그 줄을 같은 이름·폴더의 다른 종류로 바꾼다
    /// (프로젝트의 시트를 애니메이션으로 바꾸면 그 시트를 목록에서 빼고 맨 앞에 애니메이션 줄). 애니메이션끼리는 형식과 대상 확장자만.
    /// </summary>
    private void ChangeFormat(ExportFormat kind, ExportImageFormat image)
    {
        var x = _project.Export;
        if (SelectedSheet is { } sheet)
        {
            if (kind == ExportFormat.Image)
            {
                _sheet.SetImageFormat(image);
                ShowName();
                RebuildList();
                return;
            }
            var path = Path.Combine(string.IsNullOrWhiteSpace(sheet.Folder) ? ProjectDir : Path.GetFullPath(Path.Combine(ProjectDir, sheet.Folder)),
                (sheet.FileName ?? _project.BaseName) + Extension(ToAnimation(kind)));
            var anim = new AnimationExport
            {
                Format = ToAnimation(kind),
                Path = AnimationExportWriter.StoredPath(path, _project.Path),
                TagName = _anim.RangeTag?.Name,
                Scale = _anim.Scale,
                PlayCount = _anim.PlayCount,
                Background = _anim.BackgroundColor,
                Timing = _anim.Timing,
                OnSave = sheet.ExportOnSave,
            };
            int index = Math.Max(0, _selected);
            _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () =>
            {
                if (_selected < 0)
                {
                    x.ExportFrames = x.ExportParts = false;
                    x.Entries.Insert(0, ExportEntry.ForAnimation(anim));
                }
                else x.Entries[index] = ExportEntry.ForAnimation(anim);
            });
            Select(index);
            return;
        }
        if (SelectedEntry?.Animation is not { } a) return;
        if (kind != ExportFormat.Image)
        {
            var format = ToAnimation(kind);
            if (a.Format != format) _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => Retarget(a, format));
            Select(_selected);
            return;
        }
        // 애니메이션 → 이미지 시트: 프로젝트 시트 설정의 사본에 같은 이름·폴더
        var full = AnimationPath(a).TrimEnd('/', '\\');
        var stem = Path.GetFileNameWithoutExtension(full);
        if (a.Format == AnimationExportFormat.FramePngs && stem.EndsWith("_frames", StringComparison.Ordinal)) stem = stem[..^"_frames".Length];
        var newSheet = x.CloneSheet();
        newSheet.FileName = stem;
        var folder = Path.GetRelativePath(ProjectDir, Path.GetDirectoryName(full) ?? ProjectDir);
        newSheet.Folder = folder == "." ? null : Path.IsPathRooted(folder) ? Path.GetDirectoryName(full) : folder;
        newSheet.ImageFormat = image;
        newSheet.ExportOnSave = a.OnSave;
        if (!newSheet.SheetListed) newSheet.ExportFrames = true;
        int at = _selected;
        _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => x.Entries[at] = ExportEntry.ForSheet(newSheet));
        Select(at);
    }

    /// <summary>형식을 바꾸며 대상 이름의 확장자(프레임마다 PNG는 폴더 _frames)도 맞춘다.</summary>
    private static void Retarget(AnimationExport e, AnimationExportFormat format)
    {
        var stem = e.Format == AnimationExportFormat.FramePngs ? e.Path.TrimEnd('/', '\\') : Path.ChangeExtension(e.Path, null);
        if (e.Format == AnimationExportFormat.FramePngs && stem.EndsWith("_frames", StringComparison.Ordinal)) stem = stem[..^"_frames".Length];
        e.Path = format switch
        {
            AnimationExportFormat.FramePngs => stem + "_frames",
            _ => stem + Extension(format),
        };
        e.Format = format;
    }

    /// <summary>
    /// 줄을 고른다(−1 = 프로젝트의 시트, 0 이상 = 목록의 그 줄). 없는 줄이면 남은 첫 줄. 아래 형식·설정·미리보기가 그 줄 것이 된다.
    /// </summary>
    private void Select(int index)
    {
        var x = _project.Export;
        if (index >= x.Entries.Count) index = x.Entries.Count - 1;
        if (index < 0 && !x.SheetListed) index = x.Entries.Count > 0 ? 0 : -1;
        _selected = index;
        if (SelectedEntry?.Animation is { } a)
        {
            _anim.Load(a);
            var kind = FromAnimation(a.Format);
            SyncCombo(kind, default);
            ApplyFormat(kind);
        }
        else
        {
            if (!ReferenceEquals(_sheet.Sheet, SelectedEntry?.Sheet)) _sheet.SetSheet(SelectedEntry?.Sheet);
            SyncCombo(ExportFormat.Image, _sheet.ImageFormat);
            ApplyFormat(ExportFormat.Image);
        }
        ShowName();
        RebuildList();
    }

    private void SyncCombo(ExportFormat kind, ExportImageFormat image)
    {
        _syncingCombo = true;
        _formatCombo.SelectedIndex = IndexOf(kind, image);
        _syncingCombo = false;
    }

    /// <summary>프로젝트가 바뀜(실행취소 등): 줄 객체가 바뀌었으면(되돌리기는 사본으로 바꿈) 고른 줄을 다시 넣고, 아니면 목록·이름만 다시 그린다.</summary>
    private void OnProjectChanged()
    {
        var entry = SelectedEntry;
        bool stale = _selected >= _project.Export.Entries.Count
                     || _selected < 0 && (_sheet.Sheet is not null || !_project.Export.SheetListed)
                     || entry?.Sheet is { } sheet && !ReferenceEquals(_sheet.Sheet, sheet)
                     || entry?.Animation is { } a && !ReferenceEquals(_anim.Entry, a)
                     || entry is not null && SelectedSheet is null && _format == ExportFormat.Image
                     || SelectedSheet is not null && _format != ExportFormat.Image;
        if (stale) Select(_selected);
        else
        {
            if (!_nameBox.IsFocused) ShowName();
            RebuildList();
        }
    }

    /// <summary>목록 줄을 다시 그린다: 프로젝트의 시트(있으면) + 목록의 줄들.</summary>
    private void RebuildList()
    {
        _list.Children.Clear();
        var x = _project.Export;
        if (x.SheetListed)
            _list.Children.Add(Row(SheetLabel(x), x.ExportOnSave, _selected < 0,
                v => _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => x.ExportOnSave = v),
                () => Select(-1),
                () =>
                {
                    _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => x.ExportFrames = x.ExportParts = false);
                    Select(_selected < 0 ? 0 : _selected);
                }));
        for (int i = 0; i < x.Entries.Count; i++)
        {
            int index = i;
            var e = x.Entries[i];
            var label = e.Sheet is { } sheet ? SheetLabel(sheet)
                : e.Animation is { } a ? $"{Path.GetFileName(a.Path.TrimEnd('/', '\\'))} — {KindName(a.Format)} · {a.TagName ?? UiStrings.ExportList_AllFrames} · {a.Scale}×"
                : "";
            _list.Children.Add(Row(label, e.OnSave, _selected == index,
                v => _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => e.OnSave = v),
                () => Select(index),
                () =>
                {
                    _project.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => x.Entries.RemoveAt(index));
                    Select(_selected > index ? _selected - 1 : _selected == index ? Math.Min(index, x.Entries.Count - 1) : _selected);
                }));
        }
        if (_list.Children.Count == 0)
            _list.Children.Add(new TextBlock { Text = UiStrings.ExportList_Empty, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap });
    }

    /// <summary>시트 줄 이름: "hero.png — 이미지 시트" (+ Unity).</summary>
    private string SheetLabel(ExportSettings sheet)
        => $"{sheet.FileName ?? _project.BaseName}{sheet.ImageFormat.Extension()} — {UiStrings.ExportList_Sheet}" + SheetExportPages.EngineSuffix(sheet.Engine);

    /// <summary>목록 줄 하나: [저장할 때] 이름(누르면 고름) [빼기]. 고른 줄은 배경색.</summary>
    private Control Row(string label, bool onSave, bool selected, Action<bool> setOnSave, Action select, Action remove)
    {
        var check = new CheckBox { IsChecked = onSave, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) }.Tip(UiStrings.ExportList_OnSaveTip);
        check.IsCheckedChanged += (_, _) => setOnSave(check.On());
        var text = new TextBlock { Text = label, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(text, label);
        var del = Ui.IconButton(VectorIcon.X, UiStrings.ExportList_Remove, (_, _) => Dispatcher.UIThread.Post(remove));
        DockPanel.SetDock(check, Dock.Left);
        DockPanel.SetDock(del, Dock.Right);
        var row = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1),
            Background = selected ? AppTheme.HoverBrush : Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new DockPanel { Children = { check, del, text } },
        };
        // 체크·빼기 단추는 누르기를 처리해(Handled) 여기 오지 않는다
        row.PointerPressed += (_, _) => Dispatcher.UIThread.Post(select);
        return row;
    }

    /// <summary>파란 "내보내기"로 닫혔을 때 내보낼 목록의 시트 줄(null = 프로젝트의 시트).</summary>
    public int? SheetEntry => _selected >= 0 ? _selected : null;

    /// <summary>이 창에서 다른 이름으로 저장·애니메이션·프레임으로 쓴 파일(파일 목록 새로 고침용).</summary>
    public IReadOnlyList<string> ExportedPaths => _exported;
    /// <summary>마지막으로 쓴 결과 문구(상태 표시줄용, 없으면 null).</summary>
    public string? ResultMessage { get; private set; }

    private void Exported(IReadOnlyList<string> paths, string message)
    {
        _exported.AddRange(paths);
        ResultMessage = message;
    }

    private static string ChoiceName(ExportFormat kind, ExportImageFormat image) => kind switch
    {
        ExportFormat.Gif => UiStrings.S_GifExportDialog_ChooseAndExport_AC2A7BE712,
        ExportFormat.Apng => UiStrings.Export_FormatApng,
        ExportFormat.Image => SheetExportPages.FormatName(image),
        _ => UiStrings.Export_FormatFrames,
    };

    private int IndexOf(ExportFormat kind, ExportImageFormat image)
        => Math.Max(0, Array.FindIndex(_choices, c => c.Kind == kind && (kind != ExportFormat.Image || c.Image == image)));

    /// <summary>이미지 형식을 보는 중이면 콤보를 고친 시트의 이미지 형식에 맞춘다.</summary>
    private void SyncImageChoice()
    {
        if (_format != ExportFormat.Image) return;
        int index = IndexOf(ExportFormat.Image, _sheet.ImageFormat);
        if (_formatCombo.SelectedIndex == index) return;
        _syncingCombo = true;
        _formatCombo.SelectedIndex = index;
        _syncingCombo = false;
    }

    /// <summary>형식 종류를 바꾼다: 묶음·미리보기·단추와 대상 도움말을 그 형식에 맞추고 기억한다.</summary>
    private void ApplyFormat(ExportFormat format)
    {
        _format = format;
        bool image = format == ExportFormat.Image;
        _sheet.FormatOptions.IsVisible = _sheet.SettingsColumn.IsVisible = _sheet.Preview.IsVisible = _sheet.PresetButton.IsVisible = image;
        _saveCopy.IsVisible = _copyResultHost.IsVisible = image;
        _anim.Groups.IsVisible = _anim.PreviewColumn.IsVisible = !image;
        _sheet.PreviewShown = image;
        if (image) _anim.Hide();
        else _anim.Show(format);
        // 대상 도움말(F1): 이미지 = "내보내기 창" 쪽, 다른 형식 = "GIF · APNG · 프레임 PNG로 내보내기" 쪽.
        if (image)
        {
            this.HelpAt(SheetExportPages.GuidePage);
            OkButton.Content = UiStrings.S_UnityExportDialog_UnityExportDialog_C53EAC78FD;
            OkButton.Tip(UiStrings.S_UnityExportDialog_UnityExportDialog_C7A33C7D9E).HelpAt(SheetExportPages.GuidePage, anchor: "①");
        }
        else
        {
            this.HelpAt(AnimationExportPart.GuidePage, shot: "screen-gif");
            OkButton.Content = UiStrings.S_UnityExportDialog_UnityExportDialog_C53EAC78FD;
            OkButton.Tip(null).HelpAt(AnimationExportPart.GuidePage, anchor: "writes the file straight to");
        }
        UpdateOkButton();
    }

    /// <summary>애니메이션 형식은 내보낼 프레임이 있을 때만 내보낼 수 있다.</summary>
    private void UpdateOkButton() => OkButton.IsEnabled = _format == ExportFormat.Image || _canExportAnimation;

    /// <summary>기억한 창 크기·위치(지금 어느 화면에도 걸치지 않으면 주인 창 가운데, 크기는 화면 작업 영역 안으로).</summary>
    private void RestorePlacement()
    {
        var s = _settings;
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        double maxW = screen is null ? 3000 : screen.WorkingArea.Width / screen.Scaling;
        double maxH = screen is null ? 3000 : screen.WorkingArea.Height / screen.Scaling;
        Width = Math.Clamp(s.ExportWidth > 0 ? s.ExportWidth : DefaultWidth, MinW, Math.Max(MinW, maxW));
        Height = Math.Clamp(s.ExportHeight > 0 ? s.ExportHeight : DefaultHeight, MinH, Math.Max(MinH, maxH));
        var bounds = new PixelRect(s.ExportX, s.ExportY, (int)Width, (int)Height);
        if (s.ExportX >= 0 && s.ExportY >= 0 && Screens.All.Any(sc => sc.WorkingArea.Intersects(bounds)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(s.ExportX, s.ExportY);
        }
    }

    /// <summary>나눔 비율과 창 크기·위치를 설정에 적는다(최대화·최소화 중이면 크기·위치는 그대로).</summary>
    private void SavePlacement()
    {
        double left = _columns.ColumnDefinitions[1].ActualWidth, right = _columns.ColumnDefinitions[3].ActualWidth;
        if (left > 0 && right > 0) _settings.ExportSplit = Math.Round(left / (left + right), 3);
        if (WindowState != WindowState.Normal) return;
        _settings.ExportX = Math.Max(0, Position.X);
        _settings.ExportY = Math.Max(0, Position.Y);
        if (ClientSize.Width > 0) _settings.ExportWidth = (int)Math.Round(ClientSize.Width);
        if (ClientSize.Height > 0) _settings.ExportHeight = (int)Math.Round(ClientSize.Height);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _sheet.Attach();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        // 닫기·Esc·창 닫기 단추로 닫아도 입력 중이던 이름·폴더가 반영되게(이미지 내보내기 전에).
        _sheet.Detach();
        _anim.Close();
        _project.Changed -= _onProjectChanged;
        SavePlacement();
    }
}
