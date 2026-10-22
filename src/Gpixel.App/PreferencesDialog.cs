using System.Text.Json;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>
/// 환경 설정: 일반(실행취소 횟수, 실행취소 기록 남기기·보관 기간·용량, 자동 저장, 앱 휴지통 보관 기간·용량, 업데이트 자동 확인) + 단축키(명령마다 바꾸기·추가·없음·기본값, 모두 기본값, JSON 내보내기·불러오기).
/// 단축키는 서로 겹치지 않게 한다(이미 쓰는 키를 지정하면 그 명령에서 빼도 되는지 묻는다).
/// </summary>
public sealed class PreferencesDialog : DialogBase
{
    private readonly NumericUpDown _undo;
    private readonly CheckBox _historyOn;
    private readonly NumericUpDown _historyDays, _historyMb;
    private readonly TrashPolicyEditor _trash;
    private readonly CheckBox _autoSave;
    private readonly NumericUpDown _autoSaveDelay;
    private readonly CheckBox _updateCheck;
    private readonly IReadOnlyList<ShortcutCommand> _commands;
    private readonly Dictionary<string, List<KeyChord>> _keys;
    private readonly TextBox _search;
    private readonly StackPanel _rows = new() { Spacing = 1 };
    private readonly TextBlock _status;
    /// <summary>키를 기다리는 명령과 방식(true = 바꾸기, false = 추가). 없으면 null.</summary>
    private (string Id, bool Replace)? _capture;

    /// <param name="keys">명령 Id → 지금 단축키.</param>
    /// <param name="history">남긴 실행취소 기록(지금 용량 표시·모두 지우기).</param>
    public PreferencesDialog(int undoSteps, bool historyEnabled, UndoHistoryPolicy historyPolicy, UndoHistoryKeeper history,
        bool autoSave, int autoSaveDelay, TrashPolicy trashPolicy, bool updateCheck,
        IReadOnlyList<ShortcutCommand> commands, IReadOnlyDictionary<string, List<KeyChord>> keys)
        : base(UiStrings.S_PreferencesDialog_PreferencesDialog_0B1C3F663F)
    {
        _commands = commands;
        _keys = commands.ToDictionary(c => c.Id, c => keys.TryGetValue(c.Id, out var k) ? k.ToList() : c.Defaults.ToList());

        _undo = Ui.Num(1, UndoStack.MaxCapacity, Math.Clamp(undoSteps, 1, UndoStack.MaxCapacity), 90)
            .Presets(50, 100, 200, 500, 1000, 2000, 5000, 10000).Unit(UiStrings.S_PreferencesDialog_PreferencesDialog_B8D956911B).RecentKey("undo.steps");
        _trash = new TrashPolicyEditor(trashPolicy);
        _historyOn = Ui.Check(UiStrings.S_PreferencesDialog_PreferencesDialog_3AC3D9A229, historyEnabled);
        _historyDays = Ui.Num(0, 3650, Math.Clamp(historyPolicy.Days, 0, 3650), 70)
            .Presets(0, 1, 3, 7, 14, 30, 60, 90, 180, 365).Unit(UiStrings.S_PreferencesDialog_PreferencesDialog_9D2E2327DF).RecentKey("history.days");
        _historyMb = Ui.Num(10, 100_000, Math.Clamp(historyPolicy.MaxMegabytes, 10, 100_000), 80)
            .Presets(50, 100, 200, 500, 1000, 2000, 5000, 10000).Unit("MB").RecentKey("history.mb");
        void SyncHistory() => _historyDays.IsEnabled = _historyMb.IsEnabled = _historyOn.On();
        _historyOn.IsCheckedChanged += (_, _) => SyncHistory();
        SyncHistory();
        var historyUsage = Ui.Label(history.DescribeUsage());
        historyUsage.Foreground = AppTheme.DimTextBrush;
        var historyClear = Ui.Button(UiStrings.S_PreferencesDialog_PreferencesDialog_A127858673, (_, _) =>
        {
            if (Ui.Ask(this, UiStrings.S_PreferencesDialog_PreferencesDialog_10A5B7FEA9, UiStrings.S_PreferencesDialog_PreferencesDialog_02113E25F5,
                    MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
            history.Clear();
            historyUsage.Text = history.DescribeUsage();
        });
        var historyNote = Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_BF42374CBC);
        historyNote.Foreground = AppTheme.DimTextBrush;
        _autoSave = Ui.Check(UiStrings.S_PreferencesDialog_PreferencesDialog_D3AF99B338, autoSave);
        _autoSaveDelay = Ui.Num(AppSettings.MinAutoSaveDelay, AppSettings.MaxAutoSaveDelay,
                Math.Clamp(autoSaveDelay, AppSettings.MinAutoSaveDelay, AppSettings.MaxAutoSaveDelay), 70)
            .Presets(1, 2, 3, 5, 10, 15, 30, 60, 120, 300, 600).Unit(UiStrings.S_PreferencesDialog_PreferencesDialog_681608AF43).RecentKey("autosave.delay");
        _autoSaveDelay.IsEnabled = autoSave;
        _autoSave.IsCheckedChanged += (_, _) => _autoSaveDelay.IsEnabled = _autoSave.On();
        var autoSaveNote = Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_0D594ECE55);
        autoSaveNote.Foreground = AppTheme.DimTextBrush;
        _updateCheck = Ui.Check(UiStrings.S_PreferencesDialog_PreferencesDialog_20E707DCCA, updateCheck);
        var updateNote = Ui.Label(AppInfo.IsDevBuild
            ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_PreferencesDialog_4B32B49831,AppInfo.VersionText)
            : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_PreferencesDialog_80B1F80264,AppInfo.VersionText));
        updateNote.Foreground = AppTheme.DimTextBrush;
        var general = Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_1F7416F7D9), _undo, Ui.Label(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_PreferencesDialog_20DF2ED64F,UndoStack.DefaultCapacity,UndoStack.MaxCapacity))),
            Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_D87D2E02E0),
            Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_02113E25F5, bold: true),
            _historyOn,
            Ui.Row(Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_566552BAB9), _historyDays, Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_8FA707928D))
                .Tip(UiStrings.S_PreferencesDialog_PreferencesDialog_F8AA857BED),
            Ui.Row(Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_AFE766F9E0), _historyMb, Ui.Label(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_PreferencesDialog_F7A34332AC,UndoHistoryPolicy.DefaultMaxMegabytes))),
            Ui.Row(historyUsage, historyClear),
            historyNote,
            Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_06B0DD01B8, bold: true),
            Ui.Row(_autoSave, _autoSaveDelay,
                Ui.Label(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_PreferencesDialog_19CDB23584,AppSettings.DefaultAutoSaveDelay,AppSettings.MinAutoSaveDelay,AppSettings.MaxAutoSaveDelay))),
            autoSaveNote,
            Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_B57D05987F, bold: true),
            _trash,
            Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_B4D135E19E, bold: true),
            _updateCheck,
            updateNote);

        _search = new TextBox { Watermark = UiStrings.S_PreferencesDialog_PreferencesDialog_3384322E72, Width = 260, Margin = new Thickness(2) };
        _search.TextChanged += (_, _) => RebuildRows();
        _status = Ui.Label("");
        _status.Foreground = AppTheme.DimTextBrush;
        var shortcuts = new DockPanel { LastChildFill = true };
        var top = Ui.Row(_search, Ui.Label(UiStrings.S_PreferencesDialog_PreferencesDialog_DAAB44E61A));
        DockPanel.SetDock(top, Dock.Top);
        var bottom = Ui.Stack(
            Ui.Row(
                Ui.Button(UiStrings.S_PreferencesDialog_PreferencesDialog_CAB27CF286, (_, _) => ResetAll()),
                Ui.Button(UiStrings.S_PreferencesDialog_PreferencesDialog_CC8999072A, (_, _) => Export()),
                Ui.Button(UiStrings.S_PreferencesDialog_PreferencesDialog_1B59D72F85, (_, _) => Import())),
            _status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        shortcuts.Children.Add(top);
        shortcuts.Children.Add(bottom);
        shortcuts.Children.Add(new ScrollViewer { Content = _rows, Height = 420, Width = 720, Background = AppTheme.InputBrush });

        var tabs = new TabView();
        tabs.AddPage(UiStrings.S_PreferencesDialog_PreferencesDialog_2626F2C3C7, general);
        tabs.AddPage(UiStrings.S_PreferencesDialog_PreferencesDialog_DA9395C360, shortcuts);
        Body.Children.Add(tabs);
        RebuildRows();
        // 키를 기다리는 중이면 어떤 키든(Enter·Tab 포함) 단축키로 받는다.
        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
    }

    public int UndoSteps => (int)_undo.NumValue();
    public bool AutoSave => _autoSave.On();
    public bool UndoHistoryEnabled => _historyOn.On();
    public UndoHistoryPolicy UndoHistoryPolicy => new((int)_historyDays.NumValue(), (int)_historyMb.NumValue());
    public int AutoSaveIdleSeconds => (int)_autoSaveDelay.NumValue();
    public TrashPolicy TrashPolicy => _trash.Policy;
    public bool UpdateCheck => _updateCheck.On();
    /// <summary>명령 Id → 정한 단축키(모든 명령).</summary>
    public IReadOnlyDictionary<string, List<KeyChord>> Keys => _keys;

    private void RebuildRows()
    {
        _rows.Children.Clear();
        var q = (_search.Text ?? "").Trim();
        foreach (var c in _commands)
        {
            var keys = _keys[c.Id];
            var keyText = string.Join(", ", keys.Select(k => Ui.Shortcut(Shortcuts.Format(k))));
            if (q.Length > 0 && !c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && !c.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !keyText.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            bool changed = !keys.SequenceEqual(c.Defaults);
            bool capturing = _capture?.Id == c.Id;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("70,250,170,Auto"), Margin = new Thickness(4, 0) };
            var cat = Ui.Label(c.Category);
            cat.Foreground = AppTheme.DimTextBrush;
            var name = Ui.Label(c.Name, bold: changed);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(name, $"{c.Category}/{c.Name}");
            var key = Ui.Label(capturing ? UiStrings.S_PreferencesDialog_RebuildRows_57377CDB79 : keyText.Length > 0 ? keyText : "—");
            if (capturing) key.Foreground = AppTheme.AccentBrush;
            key.TextTrimming = TextTrimming.CharacterEllipsis;
            var id = c.Id;
            var buttons = Ui.Row(
                SmallButton(UiStrings.S_PreferencesDialog_RebuildRows_6C524CA777, () => StartCapture(id, replace: true)),
                SmallButton(UiStrings.S_PreferencesDialog_RebuildRows_9004EC4503, () => StartCapture(id, replace: false)),
                SmallButton(UiStrings.S_PreferencesDialog_RebuildRows_688679199B, () => { _keys[id] = new(); RebuildRows(); }),
                SmallButton(UiStrings.S_PreferencesDialog_RebuildRows_49F151E4F2, () => SetKeys(id, c.Defaults.ToList())));
            Grid.SetColumn(name, 1);
            Grid.SetColumn(key, 2);
            Grid.SetColumn(buttons, 3);
            row.Children.Add(cat);
            row.Children.Add(name);
            row.Children.Add(key);
            row.Children.Add(buttons);
            _rows.Children.Add(row);
        }
    }

    private static Button SmallButton(string text, Action onClick)
    {
        var b = Ui.Button(text, (_, _) => onClick());
        b.Padding = new Thickness(6, 1);
        b.MinHeight = 0;
        b.Focusable = false;
        return b;
    }

    private void StartCapture(string id, bool replace)
    {
        _capture = (id, replace);
        RebuildRows();
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capture is not { } cap) return;
        e.Handled = true;
        if (Shortcuts.IsModifierKey(e.Key)) return;
        _capture = null;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            RebuildRows();
            return;
        }
        var chord = new KeyChord(e.Key, e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta));
        var keys = cap.Replace ? new List<KeyChord>() : _keys[cap.Id].ToList();
        if (!keys.Contains(chord)) keys.Add(chord);
        SetKeys(cap.Id, keys);
    }

    /// <summary>명령의 단축키를 정한다. 다른 명령이 쓰는 키면 그쪽에서 빼도 되는지 묻는다(아니요면 그대로 둠).</summary>
    private void SetKeys(string id, List<KeyChord> keys)
    {
        var clashes = _commands.Where(c => c.Id != id && _keys[c.Id].Any(keys.Contains)).ToList();
        if (clashes.Count > 0)
        {
            var list = string.Join("\n", clashes.Select(c => $"· {c.Category}/{c.Name} ({string.Join(", ", _keys[c.Id].Where(keys.Contains).Select(k => Ui.Shortcut(Shortcuts.Format(k))))})"));
            if (Ui.Ask(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_SetKeys_B4AB7E77B7,list), UiStrings.S_PreferencesDialog_SetKeys_26698A6D77, MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes)
            {
                RebuildRows();
                return;
            }
            foreach (var c in clashes) _keys[c.Id] = _keys[c.Id].Where(k => !keys.Contains(k)).ToList();
        }
        _keys[id] = keys;
        _status.Text = "";
        RebuildRows();
    }

    private void ResetAll()
    {
        foreach (var c in _commands) _keys[c.Id] = c.Defaults.ToList();
        _status.Text = UiStrings.S_PreferencesDialog_ResetAll_28F401ED03;
        RebuildRows();
    }

    private void Export()
    {
        var path = Ui.SaveFile(this, UiStrings.S_PreferencesDialog_Export_545A631C51, null, "gpixel-shortcuts.json", ".json", new FileFilter("JSON", "*.json"));
        if (path is null) return;
        try
        {
            File.WriteAllText(path, Shortcuts.ToJson(_commands, _keys));
            _status.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_Export_5710461F4B,path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_Export_CA2C8FC7D6,ex.Message));
        }
    }

    /// <summary>JSON의 명령은 그 단축키로 바꾸고(파일에 없는 명령은 그대로), 겹치는 키가 생기면 알린다.</summary>
    private void Import()
    {
        var path = Ui.OpenFile(this, UiStrings.S_PreferencesDialog_Import_C177CCAFE9, null, new FileFilter("JSON", "*.json"));
        if (path is null) return;
        var unknown = new List<string>();
        var invalid = new List<string>();
        Dictionary<string, List<KeyChord>> loaded;
        try
        {
            loaded = Shortcuts.FromJson(File.ReadAllText(path), _commands, unknown, invalid);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_Import_08B3AAE84F,ex.Message));
            return;
        }
        foreach (var (id, keys) in loaded) _keys[id] = keys;
        var dupes = _commands.SelectMany(c => _keys[c.Id].Select(k => (k, Name: $"{c.Category}/{c.Name}"))).GroupBy(x => x.k).Where(g => g.Count() > 1)
            .Select(g => $"{Ui.Shortcut(Shortcuts.Format(g.Key))}: {string.Join(", ", g.Select(x => x.Name))}").ToList();
        var notes = new List<string> { string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_Import_180B01420C,loaded.Count) };
        if (unknown.Count > 0) notes.Add(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_Import_B3C5D28F5F,unknown.Count));
        if (invalid.Count > 0) notes.Add(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PreferencesDialog_Import_8BE2475A32,invalid.Count));
        _status.Text = string.Join(" · ", notes) + UiStrings.S_PreferencesDialog_Import_E21914C069;
        if (dupes.Count > 0 || invalid.Count > 0)
            Ui.Info(this, string.Join("\n", (dupes.Count > 0 ? [UiStrings.S_PreferencesDialog_Import_509BBD7051, .. dupes] : Array.Empty<string>())
                .Concat(invalid.Count > 0 ? [UiStrings.S_PreferencesDialog_Import_BD2AAB15B7, .. invalid] : Array.Empty<string>())), UiStrings.S_PreferencesDialog_Import_C177CCAFE9);
        RebuildRows();
    }
}
