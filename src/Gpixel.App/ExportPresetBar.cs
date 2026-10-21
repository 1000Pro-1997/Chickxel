using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 내보내기 설정 창 탭 줄 오른쪽 "프리셋 ▾" 창의 내용: [지금 설정 저장…] [불러오기 ▾] [관리 ▾] [기본 설정으로].
/// 프리셋 = 프로젝트에 매이지 않는 내보내기 설정 묶음(<see cref="ExportPreset"/>). 불러오면 지금 프로젝트에 적용(실행취소 1단계),
/// 사용자 프리셋은 앱 설정 폴더의 <see cref="ExportPresets.FileName"/>에 저장한다(기본 제공 프리셋은 이름 바꾸기·덮어쓰기·삭제 불가).
/// </summary>
public sealed class ExportPresetBar : StackPanel
{
    private readonly EditorContext _ctx;
    private readonly Window _owner;
    private readonly TextBlock _status;
    private List<ExportPreset> _user = new();
    /// <summary>창을 열 때 프리셋 파일을 읽지 못한 이유(창이 열리면 한 번 알림).</summary>
    private string? _loadError;

    /// <summary>사용자 프리셋 파일 경로(설정 파일과 같은 폴더).</summary>
    public static string FilePath => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, ExportPresets.FileName);

    public ExportPresetBar(EditorContext ctx, Window owner)
    {
        _ctx = ctx;
        _owner = owner;
        Spacing = 2;
        var save = Ui.Button("지금 설정 저장…", (_, _) => Dispatcher.UIThread.Post(SaveCurrent))
            .Tip("지금 내보내기 설정을 이름 붙여 저장합니다(파일 이름·폴더·고른 스킨·착장·뺀 레이어·Single 프레임·원래 위치 유지는 담지 않음)");
        var load = Ui.Button(VectorIcon.ChevronDown, "불러오기", iconAfter: true);
        load.Tip("프리셋을 지금 프로젝트에 적용(실행취소 1단계)");
        load.Click += (_, _) => PopupMenus.Show(load, LoadMenu(), PlacementMode.BottomEdgeAlignedLeft);
        var manage = Ui.Button(VectorIcon.ChevronDown, "관리", iconAfter: true);
        manage.Tip("사용자 프리셋 이름 바꾸기·덮어쓰기·삭제, JSON 내보내기/가져오기");
        manage.Click += (_, _) => PopupMenus.Show(manage, ManageMenu(), PlacementMode.BottomEdgeAlignedLeft);
        _status = new TextBlock
        {
            Foreground = AppTheme.DimTextBrush,
            TextWrapping = TextWrapping.Wrap,
            FontSize = AppTheme.SmallFontSize,
            Margin = new Thickness(3, 0),
        };
        var reset = Ui.Button("기본 설정으로", (_, _) => Apply(ExportPreset.Capture(new ExportSettings(), "기본 설정")))
            .Tip("내보내기 설정을 처음 값으로 되돌립니다(파일 이름·폴더는 그대로, 실행취소 1단계)");
        Children.Add(new TextBlock { Text = "내보내기 설정 프리셋", FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, Margin = new Thickness(3, 0, 0, 2) });
        Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, Children = { save, load, manage, reset } });
        Children.Add(_status);
        try
        {
            _user = ExportPresets.Load(FilePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _loadError = $"내보내기 설정 프리셋 파일을 읽지 못했습니다 — 저장한 프리셋이 보이지 않습니다(새로 저장하면 이 파일을 덮어씁니다).\n{FilePath}\n{ex.Message}";
        }
        owner.Opened += (_, _) =>
        {
            if (_loadError is not { } error) return;
            _loadError = null;
            Dispatcher.UIThread.Post(() => Ui.Error(_owner, error, "내보내기 설정 프리셋"));
        };
        UpdateStatus();
    }

    private void UpdateStatus()
        => _status.Text = _user.Count == 0 ? "저장한 프리셋 없음 — 기본 제공 프리셋 3개를 불러올 수 있습니다." : $"저장한 프리셋 {_user.Count}개";

    private static TextBlock Plain(string text) => new() { Text = text };

    private static MenuItem Item(string text, Action action, bool enabled = true, string? tip = null)
    {
        var item = new MenuItem { Header = Plain(text), IsEnabled = enabled };
        if (tip is not null) item.Tip(tip);
        // 메뉴가 닫힌 뒤 실행해 대화상자가 메뉴와 겹치지 않게 한다.
        item.Click += (_, _) => Dispatcher.UIThread.Post(action);
        return item;
    }

    private static string Describe(ExportPreset p)
    {
        string output = p.ExportFrames && p.ExportParts ? "프레임 시트 + 부위 아틀라스" : p.ExportParts ? "부위 아틀라스" : "프레임 시트";
        var parts = new List<string> { output, p.Sheet.Layout == SheetLayout.Packed ? "자동 배치" : "격자", p.Sheet.Skins == SkinSource.Selected ? "고른 스킨" : "지금 착장" };
        if (p.Sheet.SplitSkins) parts.Add("스킨마다 따로 파일");
        if (p.WriteSpriteLibrary) parts.Add("Sprite Library");
        if (p.PartsSheet is not null) parts.Add("시트별 다르게");
        parts.Add(p.ImageFormat.ToString().ToUpperInvariant());
        return string.Join(" · ", parts);
    }

    // ───────────── 불러오기 ─────────────

    private List<Control> LoadMenu()
    {
        var items = new List<Control>();
        foreach (var b in ExportPresets.BuiltIns) items.Add(Item(b.Name + " (기본 제공)", () => Apply(b), tip: Describe(b)));
        items.Add(new Separator());
        if (_user.Count == 0) items.Add(Item("(저장한 프리셋 없음)", () => { }, enabled: false));
        foreach (var u in _user) items.Add(Item(u.Name, () => Apply(u), tip: Describe(u)));
        return items;
    }

    /// <summary>프리셋을 지금 프로젝트 내보내기 설정에 적용(실행취소 1단계).</summary>
    private void Apply(ExportPreset preset)
    {
        if (_ctx.Project is not { } p) return;
        _ctx.Structural(() => p.Edit($"내보내기 프리셋: {preset.Name}", ProjectChange.Settings, () => preset.ApplyTo(p.Export)));
        _status.Text = $"\"{preset.Name}\" 프리셋을 적용했습니다(실행취소 Ctrl+Z).";
    }

    // ───────────── 저장 ─────────────

    private void SaveCurrent()
    {
        if (_ctx.Project is not { } p) return;
        var name = InputDialog.Ask(_owner, "내보내기 설정 프리셋 저장",
            "프리셋 이름(같은 이름이 있으면 덮어쓸지 묻습니다)", _user.Count == 0 ? "내 설정" : "", _user.Select(x => x.Name))?.Trim();
        if (string.IsNullOrEmpty(name) || !ReferenceEquals(_ctx.Project, p)) return;
        if (ExportPresets.BuiltIns.Any(b => b.Name == name))
        {
            Ui.Warn(_owner, $"\"{name}\"은(는) 기본 제공 프리셋 이름입니다 — 다른 이름을 쓰세요.", "내보내기 설정 프리셋");
            return;
        }
        var preset = ExportPreset.Capture(p.Export, name);
        int at = _user.FindIndex(x => x.Name == name);
        if (at >= 0)
        {
            if (Ui.Ask(_owner, $"\"{name}\" 프리셋이 이미 있습니다. 지금 설정으로 덮어쓸까요?", "내보내기 설정 프리셋", MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes)
                return;
            _user[at] = preset;
        }
        else _user.Add(preset);
        if (Persist()) _status.Text = $"\"{name}\" 프리셋을 저장했습니다.";
    }

    /// <summary>사용자 프리셋을 파일에 쓴다. 실패하면 알리고 false.</summary>
    private bool Persist()
    {
        try
        {
            ExportPresets.Save(FilePath, _user);
            UpdateStatus();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(_owner, $"프리셋을 저장하지 못했습니다.\n{FilePath}\n{ex.Message}");
            return false;
        }
    }

    // ───────────── 관리 ─────────────

    private List<Control> ManageMenu()
    {
        bool any = _user.Count > 0;
        MenuItem Sub(string text, Action<ExportPreset> action)
        {
            var sub = new MenuItem { Header = Plain(text), IsEnabled = any };
            if (any) sub.ItemsSource = _user.Select(u => (Control)Item(u.Name, () => action(u))).ToList();
            return sub;
        }
        return
        [
            Sub("이름 바꾸기…", Rename),
            Sub("지금 설정으로 덮어쓰기", Overwrite),
            Sub("삭제", Delete),
            new Separator(),
            Item("JSON 내보내기…", ExportJson, any, "저장한 프리셋을 JSON 파일로(다른 PC·팀과 나누기)"),
            Item("JSON 가져오기…", ImportJson, tip: "JSON 파일의 프리셋을 더합니다(같은 이름은 덮어씀)"),
            new Separator(),
            Item("기본 제공 프리셋은 바꿀 수 없습니다", () => { }, enabled: false),
        ];
    }

    private void Rename(ExportPreset preset)
    {
        var name = InputDialog.Ask(_owner, "프리셋 이름 바꾸기", "프리셋 이름", preset.Name)?.Trim();
        if (string.IsNullOrEmpty(name) || name == preset.Name || !_user.Contains(preset)) return;
        if (ExportPresets.BuiltIns.Any(b => b.Name == name) || _user.Any(x => x.Name == name))
        {
            Ui.Warn(_owner, $"같은 이름의 프리셋이 이미 있습니다: {name}", "내보내기 설정 프리셋");
            return;
        }
        preset.Name = name;
        Persist();
    }

    private void Overwrite(ExportPreset preset)
    {
        if (_ctx.Project is not { } p || !_user.Contains(preset)) return;
        if (Ui.Ask(_owner, $"\"{preset.Name}\" 프리셋을 지금 내보내기 설정으로 덮어쓸까요?", "내보내기 설정 프리셋", MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes)
            return;
        int at = _user.IndexOf(preset);
        if (at < 0) return;
        _user[at] = ExportPreset.Capture(p.Export, preset.Name);
        if (Persist()) _status.Text = $"\"{preset.Name}\" 프리셋을 덮어썼습니다.";
    }

    private void Delete(ExportPreset preset)
    {
        if (!_user.Contains(preset)) return;
        if (Ui.Ask(_owner, $"\"{preset.Name}\" 프리셋을 지울까요?", "내보내기 설정 프리셋", MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        _user.Remove(preset);
        Persist();
    }

    private void ExportJson()
    {
        var path = Ui.SaveFile(_owner, "내보내기 설정 프리셋 내보내기", null, "gpixel-export-presets.json", ".json", new FileFilter("JSON", "*.json"));
        if (path is null) return;
        try
        {
            File.WriteAllText(path, ExportPresets.Serialize(_user), new System.Text.UTF8Encoding(false));
            _status.Text = $"내보냄: {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(_owner, $"내보내지 못했습니다.\n{ex.Message}");
        }
    }

    private void ImportJson()
    {
        var path = Ui.OpenFile(_owner, "내보내기 설정 프리셋 가져오기", null, new FileFilter("JSON", "*.json"));
        if (path is null) return;
        List<ExportPreset> imported;
        try
        {
            imported = ExportPresets.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Ui.Error(_owner, $"가져오지 못했습니다.\n{ex.Message}");
            return;
        }
        if (imported.Count == 0)
        {
            Ui.Info(_owner, "파일에 프리셋이 없습니다.", "내보내기 설정 프리셋");
            return;
        }
        _user = ExportPresets.Merge(_user, imported);
        if (Persist()) _status.Text = $"프리셋 {imported.Count}개를 가져왔습니다(같은 이름은 덮어씀).";
    }
}
