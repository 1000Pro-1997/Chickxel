using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

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
        var save = Ui.Button(UiStrings.S_ExportPresetBar_ExportPresetBar_9E8DD13E00, (_, _) => Dispatcher.UIThread.Post(SaveCurrent))
            .Tip(UiStrings.S_ExportPresetBar_ExportPresetBar_72F5D68487);
        var load = Ui.Button(VectorIcon.ChevronDown, UiStrings.S_ExportPresetBar_ExportPresetBar_BEC700FE09, iconAfter: true);
        load.Tip(UiStrings.S_ExportPresetBar_ExportPresetBar_DDF907B1A3);
        load.Click += (_, _) => PopupMenus.Show(load, LoadMenu(), PlacementMode.BottomEdgeAlignedLeft);
        var manage = Ui.Button(VectorIcon.ChevronDown, UiStrings.S_ExportPresetBar_ExportPresetBar_50C0B7CDEB, iconAfter: true);
        manage.Tip(UiStrings.S_ExportPresetBar_ExportPresetBar_C683C9E2EF);
        manage.Click += (_, _) => PopupMenus.Show(manage, ManageMenu(), PlacementMode.BottomEdgeAlignedLeft);
        _status = new TextBlock
        {
            Foreground = AppTheme.DimTextBrush,
            TextWrapping = TextWrapping.Wrap,
            FontSize = AppTheme.SmallFontSize,
            Margin = new Thickness(3, 0),
        };
        var reset = Ui.Button(UiStrings.S_ExportPresetBar_ExportPresetBar_DE19EC40C8, (_, _) => Apply(ExportPreset.Capture(new ExportSettings(), UiStrings.S_ExportPresetBar_ExportPresetBar_E8C9321AD2)))
            .Tip(UiStrings.S_ExportPresetBar_ExportPresetBar_6D9C82EDAC);
        Children.Add(new TextBlock { Text = UiStrings.S_ExportPresetBar_ExportPresetBar_F85EC90A49, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, Margin = new Thickness(3, 0, 0, 2) });
        Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, Children = { save, load, manage, reset } });
        Children.Add(_status);
        try
        {
            _user = ExportPresets.Load(FilePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _loadError = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_ExportPresetBar_C497FB9B10,FilePath,ex.Message);
        }
        owner.Opened += (_, _) =>
        {
            if (_loadError is not { } error) return;
            _loadError = null;
            Dispatcher.UIThread.Post(() => Ui.Error(_owner, error, UiStrings.S_ExportPresetBar_ExportPresetBar_F85EC90A49));
        };
        UpdateStatus();
    }

    private void UpdateStatus()
        => _status.Text = _user.Count == 0 ? UiStrings.S_ExportPresetBar_UpdateStatus_EFC93D73C4 : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_UpdateStatus_93769D0977,_user.Count);

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
        string output = p.ExportFrames && p.ExportParts ? UiStrings.S_ExportPresetBar_Describe_70797FAAFD : p.ExportParts ? UiStrings.S_ExportPresetBar_Describe_D33CCA9022 : UiStrings.S_ExportPresetBar_Describe_CFA916E467;
        var parts = new List<string> { output, p.Sheet.Layout == SheetLayout.Packed ? UiStrings.S_ExportPresetBar_Describe_447C8E9BD3 : UiStrings.S_ExportPresetBar_Describe_12A18F746C, p.Sheet.Skins == SkinSource.Selected ? UiStrings.S_ExportPresetBar_Describe_DB36FF4EB4 : UiStrings.S_ExportPresetBar_Describe_3967EC3179 };
        if (p.Sheet.SplitSkins) parts.Add(UiStrings.S_ExportPresetBar_Describe_4D11265826);
        if (p.WriteSpriteLibrary) parts.Add("Sprite Library");
        if (p.PartsSheet is not null) parts.Add(UiStrings.S_ExportPresetBar_Describe_01183ED0E2);
        parts.Add(p.ImageFormat.ToString().ToUpperInvariant());
        return string.Join(" · ", parts);
    }

    // ───────────── 불러오기 ─────────────

    private static string DisplayName(ExportPreset preset)
    {
        var builtIns = ExportPresets.BuiltIns;
        return ReferenceEquals(preset, builtIns[0]) ? UiStrings.ExportPreset_BuiltInFrame
            : ReferenceEquals(preset, builtIns[1]) ? UiStrings.ExportPreset_BuiltInParts
            : ReferenceEquals(preset, builtIns[2]) ? UiStrings.ExportPreset_BuiltInSplitSkins
            : preset.Name;
    }

    private List<Control> LoadMenu()
    {
        var items = new List<Control>();
        foreach (var b in ExportPresets.BuiltIns) items.Add(Item(DisplayName(b) + UiStrings.S_ExportPresetBar_LoadMenu_1E058AEC83, () => Apply(b), tip: Describe(b)));
        items.Add(new Separator());
        if (_user.Count == 0) items.Add(Item(UiStrings.S_ExportPresetBar_LoadMenu_29443BE920, () => { }, enabled: false));
        foreach (var u in _user) items.Add(Item(u.Name, () => Apply(u), tip: Describe(u)));
        return items;
    }

    /// <summary>프리셋을 지금 프로젝트 내보내기 설정에 적용(실행취소 1단계).</summary>
    private void Apply(ExportPreset preset)
    {
        if (_ctx.Project is not { } p) return;
        string displayName = DisplayName(preset);
        _ctx.Structural(() => p.Edit(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_ExportPresetBar_Apply_48C0CBCB6F, displayName), ProjectChange.Settings, () => preset.ApplyTo(p.Export)));
        _status.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_ExportPresetBar_Apply_4F08B4EC31, displayName);
    }

    // ───────────── 저장 ─────────────

    private void SaveCurrent()
    {
        if (_ctx.Project is not { } p) return;
        var name = InputDialog.Ask(_owner, UiStrings.S_ExportPresetBar_SaveCurrent_EB9337B412,
            UiStrings.S_ExportPresetBar_SaveCurrent_7205E396E6, _user.Count == 0 ? UiStrings.S_ExportPresetBar_SaveCurrent_09D4C29E84 : "", _user.Select(x => x.Name))?.Trim();
        if (string.IsNullOrEmpty(name) || !ReferenceEquals(_ctx.Project, p)) return;
        if (ExportPresets.BuiltIns.Any(b => b.Name == name))
        {
            Ui.Warn(_owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_SaveCurrent_34724D1873,name), UiStrings.S_ExportPresetBar_SaveCurrent_41CDF33021);
            return;
        }
        var preset = ExportPreset.Capture(p.Export, name);
        int at = _user.FindIndex(x => x.Name == name);
        if (at >= 0)
        {
            if (Ui.Ask(_owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_SaveCurrent_00B001C1F1,name), UiStrings.S_ExportPresetBar_SaveCurrent_41CDF33021, MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes)
                return;
            _user[at] = preset;
        }
        else _user.Add(preset);
        if (Persist()) _status.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_SaveCurrent_7282BD96A5,name);
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
            Ui.Error(_owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_Persist_C1A8D2BB59,FilePath,ex.Message));
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
            Sub(UiStrings.S_ExportPresetBar_ManageMenu_DF594A3D0A, Rename),
            Sub(UiStrings.S_ExportPresetBar_ManageMenu_0EF9F7D98F, Overwrite),
            Sub(UiStrings.S_ExportPresetBar_ManageMenu_ECEE38B4C3, Delete),
            new Separator(),
            Item(UiStrings.S_ExportPresetBar_ManageMenu_8E68D5ADAC, ExportJson, any, UiStrings.S_ExportPresetBar_ManageMenu_65698FF933),
            Item(UiStrings.S_ExportPresetBar_ManageMenu_84D223F154, ImportJson, tip: UiStrings.S_ExportPresetBar_ManageMenu_F1E8D20766),
            new Separator(),
            Item(UiStrings.S_ExportPresetBar_ManageMenu_1F33244D99, () => { }, enabled: false),
        ];
    }

    private void Rename(ExportPreset preset)
    {
        var name = InputDialog.Ask(_owner, UiStrings.S_ExportPresetBar_Rename_43FB8EF9FF, UiStrings.S_ExportPresetBar_Rename_0D429B5902, preset.Name)?.Trim();
        if (string.IsNullOrEmpty(name) || name == preset.Name || !_user.Contains(preset)) return;
        if (ExportPresets.BuiltIns.Any(b => b.Name == name) || _user.Any(x => x.Name == name))
        {
            Ui.Warn(_owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_Rename_0F719FCE1B,name), UiStrings.S_ExportPresetBar_Rename_5E8BA0D2B6);
            return;
        }
        preset.Name = name;
        Persist();
    }

    private void Overwrite(ExportPreset preset)
    {
        if (_ctx.Project is not { } p || !_user.Contains(preset)) return;
        if (Ui.Ask(_owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_Overwrite_847DE141D4,preset.Name), UiStrings.S_ExportPresetBar_Overwrite_6EB3C78D24, MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes)
            return;
        int at = _user.IndexOf(preset);
        if (at < 0) return;
        _user[at] = ExportPreset.Capture(p.Export, preset.Name);
        if (Persist()) _status.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_Overwrite_97D7EA03EC,preset.Name);
    }

    private void Delete(ExportPreset preset)
    {
        if (!_user.Contains(preset)) return;
        if (Ui.Ask(_owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_Delete_E1E1A98A56,preset.Name), UiStrings.S_ExportPresetBar_Delete_126C54B154, MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        _user.Remove(preset);
        Persist();
    }

    private void ExportJson()
    {
        var path = Ui.SaveFile(_owner, UiStrings.S_ExportPresetBar_ExportJson_0C3C461CF9, null, "gpixel-export-presets.json", ".json", new FileFilter("JSON", "*.json"));
        if (path is null) return;
        try
        {
            File.WriteAllText(path, ExportPresets.Serialize(_user), new System.Text.UTF8Encoding(false));
            _status.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_ExportJson_E9BBE0ACD2,path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(_owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_ExportJson_FE6C9A75DF,ex.Message));
        }
    }

    private void ImportJson()
    {
        var path = Ui.OpenFile(_owner, UiStrings.S_ExportPresetBar_ImportJson_A66E4FE95C, null, new FileFilter("JSON", "*.json"));
        if (path is null) return;
        List<ExportPreset> imported;
        try
        {
            imported = ExportPresets.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Ui.Error(_owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_ImportJson_8910F1240F,ex.Message));
            return;
        }
        if (imported.Count == 0)
        {
            Ui.Info(_owner, UiStrings.S_ExportPresetBar_ImportJson_5848A12BAD, UiStrings.S_ExportPresetBar_ImportJson_D4756C4B02);
            return;
        }
        _user = ExportPresets.Merge(_user, imported);
        if (Persist()) _status.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_ExportPresetBar_ImportJson_FF70FD1897,imported.Count);
    }
}
