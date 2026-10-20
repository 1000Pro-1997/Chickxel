using System.Text.Json;
using Gpixel.Core.Edit;

namespace Gpixel.App;

/// <summary>
/// 환경 설정: 일반(실행취소 횟수, 자동 저장, 앱 휴지통 보관 기간·용량, 업데이트 자동 확인) + 단축키(명령마다 바꾸기·추가·없음·기본값, 모두 기본값, JSON 내보내기·불러오기).
/// 단축키는 서로 겹치지 않게 한다(이미 쓰는 키를 지정하면 그 명령에서 빼도 되는지 묻는다).
/// </summary>
public sealed class PreferencesDialog : DialogBase
{
    private readonly NumericUpDown _undo;
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
    public PreferencesDialog(int undoSteps, bool autoSave, int autoSaveDelay, TrashPolicy trashPolicy, bool updateCheck,
        IReadOnlyList<ShortcutCommand> commands, IReadOnlyDictionary<string, List<KeyChord>> keys)
        : base("환경 설정")
    {
        _commands = commands;
        _keys = commands.ToDictionary(c => c.Id, c => keys.TryGetValue(c.Id, out var k) ? k.ToList() : c.Defaults.ToList());

        _undo = Ui.Num(1, UndoStack.MaxCapacity, Math.Clamp(undoSteps, 1, UndoStack.MaxCapacity), 90);
        _trash = new TrashPolicyEditor(trashPolicy);
        _autoSave = Ui.Check("자동 저장", autoSave);
        _autoSaveDelay = Ui.Num(AppSettings.MinAutoSaveDelay, AppSettings.MaxAutoSaveDelay,
            Math.Clamp(autoSaveDelay, AppSettings.MinAutoSaveDelay, AppSettings.MaxAutoSaveDelay), 70);
        _autoSaveDelay.IsEnabled = autoSave;
        _autoSave.IsCheckedChanged += (_, _) => _autoSaveDelay.IsEnabled = _autoSave.On();
        var autoSaveNote = Ui.Label("저장한 적 있는 파일만, Ctrl+S와 같이(켜져 있으면 시트 내보내기 포함) 저장합니다. 새 프로젝트는 저장하지 않습니다.");
        autoSaveNote.Foreground = AppTheme.DimTextBrush;
        _updateCheck = Ui.Check("시작할 때 새 버전 확인(있으면 업데이트 창)", updateCheck);
        var updateNote = Ui.Label(AppInfo.IsDevBuild
            ? $"지금은 개발 빌드({AppInfo.VersionText})라 자동으로 확인하지 않습니다. 도움말 > 업데이트 확인…은 언제든 쓸 수 있습니다."
            : $"지금 버전 {AppInfo.VersionText} · 도움말 > 업데이트 확인…으로 언제든 확인할 수 있습니다.");
        updateNote.Foreground = AppTheme.DimTextBrush;
        var general = Ui.Stack(
            Ui.Row(Ui.Label("실행취소 횟수"), _undo, Ui.Label($"단계 (기본 {UndoStack.DefaultCapacity}, 최대 {UndoStack.MaxCapacity})")),
            Ui.Label("줄이면 열린 프로젝트의 오래된 기록부터 버립니다. 많을수록 메모리를 더 씁니다."),
            Ui.Label("저장", bold: true),
            Ui.Row(_autoSave, _autoSaveDelay,
                Ui.Label($"초 동안 편집이 없으면 저장 (기본 {AppSettings.DefaultAutoSaveDelay}, {AppSettings.MinAutoSaveDelay}~{AppSettings.MaxAutoSaveDelay})")),
            autoSaveNote,
            Ui.Label("앱 휴지통(파일 브라우저에서 지운 파일) 보관", bold: true),
            _trash,
            Ui.Label("업데이트", bold: true),
            _updateCheck,
            updateNote);

        _search = new TextBox { Watermark = "명령·키 검색", Width = 260, Margin = new Thickness(2) };
        _search.TextChanged += (_, _) => RebuildRows();
        _status = Ui.Label("");
        _status.Foreground = AppTheme.DimTextBrush;
        var shortcuts = new DockPanel { LastChildFill = true };
        var top = Ui.Row(_search, Ui.Label("\"바꾸기\"·\"추가\"를 누른 뒤 키를 누르세요(Esc = 취소)."));
        DockPanel.SetDock(top, Dock.Top);
        var bottom = Ui.Stack(
            Ui.Row(
                Ui.Button("모두 기본값", (_, _) => ResetAll()),
                Ui.Button("내보내기(JSON)…", (_, _) => Export()),
                Ui.Button("불러오기(JSON)…", (_, _) => Import())),
            _status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        shortcuts.Children.Add(top);
        shortcuts.Children.Add(bottom);
        shortcuts.Children.Add(new ScrollViewer { Content = _rows, Height = 420, Width = 720, Background = AppTheme.InputBrush });

        var tabs = new TabView();
        tabs.AddPage("일반", general);
        tabs.AddPage("단축키", shortcuts);
        Body.Children.Add(tabs);
        RebuildRows();
        // 키를 기다리는 중이면 어떤 키든(Enter·Tab 포함) 단축키로 받는다.
        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
    }

    public int UndoSteps => (int)_undo.NumValue();
    public bool AutoSave => _autoSave.On();
    public int AutoSaveDelaySeconds => (int)_autoSaveDelay.NumValue();
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
            ToolTip.SetTip(name, c.Id);
            var key = Ui.Label(capturing ? "키를 누르세요… (Esc 취소)" : keyText.Length > 0 ? keyText : "—");
            if (capturing) key.Foreground = AppTheme.AccentBrush;
            key.TextTrimming = TextTrimming.CharacterEllipsis;
            var id = c.Id;
            var buttons = Ui.Row(
                SmallButton("바꾸기", () => StartCapture(id, replace: true)),
                SmallButton("추가", () => StartCapture(id, replace: false)),
                SmallButton("없음", () => { _keys[id] = new(); RebuildRows(); }),
                SmallButton("기본값", () => SetKeys(id, c.Defaults.ToList())));
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
            var list = string.Join("\n", clashes.Select(c => $"· {c.Id} ({string.Join(", ", _keys[c.Id].Where(keys.Contains).Select(k => Ui.Shortcut(Shortcuts.Format(k))))})"));
            if (Ui.Ask(this, $"이미 쓰는 키입니다:\n{list}\n\n그 명령에서 빼고 지정할까요?", "단축키 겹침", MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes)
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
        _status.Text = "모든 단축키를 기본값으로 되돌렸습니다(확인을 눌러야 적용).";
        RebuildRows();
    }

    private void Export()
    {
        var path = Ui.SaveFile(this, "단축키 내보내기", null, "gpixel-shortcuts.json", ".json", new FileFilter("JSON", "*.json"));
        if (path is null) return;
        try
        {
            File.WriteAllText(path, Shortcuts.ToJson(_commands, _keys));
            _status.Text = $"내보냄: {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, $"내보내지 못했습니다.\n{ex.Message}");
        }
    }

    /// <summary>JSON의 명령은 그 단축키로 바꾸고(파일에 없는 명령은 그대로), 겹치는 키가 생기면 알린다.</summary>
    private void Import()
    {
        var path = Ui.OpenFile(this, "단축키 불러오기", null, new FileFilter("JSON", "*.json"));
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
            Ui.Error(this, $"불러오지 못했습니다.\n{ex.Message}");
            return;
        }
        foreach (var (id, keys) in loaded) _keys[id] = keys;
        var dupes = _commands.SelectMany(c => _keys[c.Id].Select(k => (k, c.Id))).GroupBy(x => x.k).Where(g => g.Count() > 1)
            .Select(g => $"{Ui.Shortcut(Shortcuts.Format(g.Key))}: {string.Join(", ", g.Select(x => x.Id))}").ToList();
        var notes = new List<string> { $"불러옴: 명령 {loaded.Count}개" };
        if (unknown.Count > 0) notes.Add($"모르는 명령 {unknown.Count}개 건너뜀");
        if (invalid.Count > 0) notes.Add($"읽을 수 없는 키 {invalid.Count}개 건너뜀");
        _status.Text = string.Join(" · ", notes) + " (확인을 눌러야 적용)";
        if (dupes.Count > 0 || invalid.Count > 0)
            Ui.Info(this, string.Join("\n", (dupes.Count > 0 ? ["겹치는 키(나중 명령이 이김 — 고쳐 주세요):", .. dupes] : Array.Empty<string>())
                .Concat(invalid.Count > 0 ? ["읽을 수 없는 키:", .. invalid] : Array.Empty<string>())), "단축키 불러오기");
        RebuildRows();
    }
}
