using System.Text.RegularExpressions;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>대화상자 결과(WinForms와 같은 이름).</summary>
public enum DialogResult
{
    None,
    OK,
    Cancel,
    Yes,
    No,
}

public enum MessageButtons
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel,
}

public enum MessageIcon
{
    None,
    Info,
    Warning,
    Error,
    Question,
}

/// <summary>파일 대화상자 필터 한 줄. <paramref name="Patterns"/>는 "*.png" 형식.</summary>
public sealed record FileFilter(string Name, params string[] Patterns);

/// <summary>코드로 UI를 만들 때 쓰는 작은 도우미들.</summary>
public static class Ui
{
    public static TextBlock Label(string text, bool bold = false) => new()
    {
        Text = text,
        FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
        Foreground = AppTheme.TextBrush,
        Margin = new Thickness(3, 3, 3, 3),
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
    };

    /// <summary>긴 설명을 담는 정보 표시: 모서리가 둥근 작은 네모 안의 "i" 아이콘(마우스를 올리면 풍선).</summary>
    public static Border InfoBadge(string tip)
    {
        var b = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            BorderBrush = AppTheme.DimTextBrush,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            Margin = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Help),
            Child = new IconView(VectorIcon.InfoMark, AppTheme.DimTextBrush, 2.6) { Width = 11, Height = 11 },
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    /// <summary>패널 위쪽 제목 줄(DockPanel 위에 붙임).</summary>
    public static Border Header(string text)
    {
        var b = new Border
        {
            Height = 24,
            Background = AppTheme.BackBrush,
            Padding = new Thickness(6, 0, 0, 0),
            Child = new TextBlock { Text = text, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center },
        };
        DockPanel.SetDock(b, Dock.Top);
        return b;
    }

    public static NumericUpDown Num(decimal min, decimal max, decimal value, int width = 60, int decimals = 0, decimal increment = 1)
        => new()
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = width,
            Increment = increment,
            FormatString = decimals > 0 ? "F" + decimals : "0",
            Margin = new Thickness(3),
            ClipValueToMinMax = true,
            ShowButtonSpinner = false, // 좌우로 끌어 바꾼다(NumberScrub)
        };

    /// <summary>숫자 상자 값(비었으면 최소값).</summary>
    public static decimal NumValue(this NumericUpDown n) => n.Value ?? n.Minimum;

    // 숫자 상자 우클릭 예시 값(NumberPresets.Presets)에 여러 곳에서 쓰는 목록.
    /// <summary>스프라이트·캔버스 가로·세로(px).</summary>
    public static readonly decimal[] SpriteSizes = [8, 16, 24, 32, 48, 64, 96, 128, 256, 512, 1024];
    /// <summary>부위(머리·몸·팔·다리) 가로·세로(px).</summary>
    public static readonly decimal[] PartSizes = [4, 6, 8, 10, 12, 16, 20, 24, 32, 48, 64];
    /// <summary>타일 격자 칸(px).</summary>
    public static readonly decimal[] TileSizes = [8, 12, 16, 24, 32, 48, 64, 128];
    /// <summary>위치 옮김·오프셋(px, 음수 포함).</summary>
    public static readonly decimal[] Offsets = [-16, -8, -4, -2, -1, 0, 1, 2, 4, 8, 16];
    /// <summary>프레임 번호(1부터) 예시: 12개 이하면 전부, 많으면 앞쪽 몇 개·고른 간격·마지막 프레임.</summary>
    public static decimal[] FrameNumbers(int count)
        => count <= 12
            ? [.. Enumerable.Range(1, Math.Max(1, count)).Select(i => (decimal)i)]
            : [.. new decimal[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48 }.Where(v => v < count).Take(11).Append(count)];
    /// <summary>여백·간격 같은 짧은 거리(px).</summary>
    public static readonly decimal[] Spacings = [0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32];
    /// <summary>비율·세기·불투명도(%).</summary>
    public static readonly decimal[] Percents = [0, 10, 25, 33, 50, 66, 75, 90, 100];

    public static Button Button(string text, EventHandler<RoutedEventArgs>? onClick = null, int width = 0)
    {
        var b = new Button { Content = text, Margin = new Thickness(2) };
        if (width > 0) b.Width = width;
        if (onClick is not null) b.Click += onClick;
        return b;
    }

    /// <summary>선 아이콘 하나(붓을 주지 않으면 둘레 글자색을 물려받음). <paramref name="filled"/>면 도형 안을 채운다.</summary>
    public static IconView Icon(VectorIcon icon, double size = 13, bool filled = false) => new(icon)
    {
        Width = size,
        Height = size,
        Filled = filled,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>글 줄(TextBlock.Inlines) 안에 넣는 선 아이콘(글자 가운데 높이).</summary>
    public static Avalonia.Controls.Documents.InlineUIContainer InlineIcon(VectorIcon icon, IBrush brush, double size, bool filled = false)
        => new(new IconView(icon, brush) { Width = size, Height = size, Filled = filled }) { BaselineAlignment = BaselineAlignment.Center };

    /// <summary>
    /// 선 아이콘 + 글자 한 줄(단추·체크 상자·메뉴 내용). 아이콘은 글자색을 물려받아 꺼짐·올림 색을 따른다.
    /// <paramref name="iconAfter"/>면 아이콘이 글자 뒤(메뉴를 여는 단추의 아래 화살표처럼).
    /// </summary>
    public static StackPanel IconLabel(VectorIcon icon, string text, double size = 13, bool iconAfter = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        if (!iconAfter) row.Children.Add(Icon(icon, size));
        row.Children.Add(label);
        if (iconAfter) row.Children.Add(Icon(icon, size - 2));
        return row;
    }

    /// <summary>아이콘 + 글자 단추.</summary>
    public static Button Button(VectorIcon icon, string text, EventHandler<RoutedEventArgs>? onClick = null, int width = 0, bool iconAfter = false)
    {
        var b = Button(text, onClick, width);
        b.Content = IconLabel(icon, text, iconAfter: iconAfter);
        return b;
    }

    /// <summary>Discord 로고(브랜드색) + 글자 단추. 풍선 도움말은 초대 링크.</summary>
    public static Button DiscordButton(string text, EventHandler<RoutedEventArgs> onClick)
    {
        var b = Button(text, onClick);
        var row = IconLabel(VectorIcon.Discord, text);
        ((IconView)row.Children[0]).Foreground = AppTheme.DiscordBrush;
        b.Content = row;
        return b.Tip(AppInfo.DiscordUrl);
    }

    /// <summary>아이콘만 있는 단추(뜻은 풍선 <paramref name="tip"/>으로).</summary>
    public static Button IconButton(VectorIcon icon, string? tip, EventHandler<RoutedEventArgs>? onClick = null, int width = 0, double size = 14)
    {
        var b = Button("", onClick, width);
        var iv = Icon(icon, size);
        iv.Margin = new Thickness(0, Math.Max(0, (16 - size) / 2));
        b.Content = iv;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.VerticalContentAlignment = VerticalAlignment.Center;
        return b.Tip(tip);
    }

    public static CheckBox Check(string text, bool value = false) => new()
    {
        Content = text,
        IsChecked = value,
        Margin = new Thickness(3, 2, 3, 2),
        MinHeight = 22,
    };

    /// <summary>체크 상자가 켜졌는지.</summary>
    public static bool On(this ToggleButton t) => t.IsChecked == true;

    public static TextBox Text(string value = "", int width = 120) => new()
    {
        Text = value,
        Width = width,
        Margin = new Thickness(3),
    };

    public static ComboBox Combo(int width, params string[] items)
    {
        var c = new ComboBox { Width = width, Margin = new Thickness(3) };
        foreach (var it in items) c.Items.Add(it);
        if (items.Length > 0) c.SelectedIndex = 0;
        return c;
    }

    /// <summary>자식들을 한 줄로 늘어놓는 패널.</summary>
    public static StackPanel Row(params Control[] controls)
    {
        var f = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var c in controls)
        {
            c.VerticalAlignment = VerticalAlignment.Center;
            f.Children.Add(c);
        }
        return f;
    }

    /// <summary>자식들을 위에서 아래로 쌓는 패널.</summary>
    public static StackPanel Stack(params Control[] controls)
    {
        var f = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4), Spacing = 2 };
        f.Children.AddRange(controls);
        return f;
    }

    /// <summary>제목 줄이 있는 묶음 상자.</summary>
    public static Border Group(string title, Control content)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, Margin = new Thickness(2, 0, 0, 2) });
        stack.Children.Add(content);
        return new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4, 6, 6),
            Margin = new Thickness(2, 4, 2, 2),
            Child = stack,
        };
    }

    /// <summary>내용을 세로로 스크롤해 보여 주는 패널.</summary>
    public static ScrollViewer ScrollHost(Control content) => new()
    {
        Content = content,
        Background = AppTheme.PanelBrush,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    /// <summary>도움말 풍선.</summary>
    public static T Tip<T>(this T control, string? tip) where T : Control
    {
        ToolTip.SetTip(control, tip is null ? null : Shortcut(tip));
        return control;
    }

    // ───────────── 단축키 표기 ─────────────

    private static readonly Regex ModifierToken = new(@"\b(Ctrl|Shift|Alt)\+", RegexOptions.Compiled);

    /// <summary>
    /// 단축키 문구("Ctrl+Shift+S")를 이 OS 표기로 바꾼다. macOS는 ⌘⇧⌥ 기호(Ctrl 자리 = ⌘), 그 밖은 그대로.
    /// 문장 안에 섞인 단축키도 바꾼다.
    /// </summary>
    public static string Shortcut(string text)
    {
        if (!Platform.IsMac) return text;
        return ModifierToken.Replace(text, m => m.Groups[1].Value switch
        {
            "Ctrl" => "⌘",
            "Shift" => "⇧",
            _ => "⌥",
        });
    }

    /// <summary>"Ctrl" 자리 수정자(macOS = ⌘)가 눌렸는지.</summary>
    public static bool HasCommand(KeyModifiers m) => (m & Platform.CommandModifier) != 0;

    // ───────────── 창 찾기 ─────────────

    public static Window? MainWindow
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>컨트롤이 들어 있는 창(없으면 활성 창, 그다음 메인 창).</summary>
    public static Window? Owner(Visual? visual)
    {
        if (visual is Window w) return w;
        if (visual is not null && TopLevel.GetTopLevel(visual) is Window top) return top;
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        return lifetime?.Windows.FirstOrDefault(x => x.IsActive) ?? lifetime?.MainWindow;
    }

    // ───────────── 동기 대기(모달) ─────────────

    /// <summary>
    /// 작업이 끝날 때까지 UI 메시지를 돌리며 기다린다(중첩 디스패처 프레임). 대화상자를 WinForms처럼 동기로 쓰기 위한 것.
    /// UI 스레드에서만 부른다. 프레임 밖으로 던져진 예외(macOS 창 이벤트 예외)는 <see cref="CrashGuard.Recover"/>가 알린 뒤 계속 기다린다.
    /// </summary>
    public static T Wait<T>(Task<T> task)
    {
        while (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            try
            {
                Dispatcher.UIThread.PushFrame(frame);
                break;
            }
            catch (Exception e) when (CrashGuard.Recover(e, UiStrings.S_Ui_Wait_69BB057E0C))
            {
                // 알렸으니 대화상자를 계속 돌린다.
            }
        }
        return task.GetAwaiter().GetResult();
    }

    public static void Wait(Task task) => Wait(task.ContinueWith(t => { t.GetAwaiter().GetResult(); return 0; }, TaskScheduler.Default));

    /// <summary>
    /// 창을 모달로 띄우고 닫힐 때까지 기다린다(주인 창이 없으면 그냥 띄움). <see cref="DialogBase"/>는 닫힘 처리기(Closed)가 예외를 내
    /// 결과가 오지 않아도(<see cref="CrashGuard"/>가 알림 — 그 뒤 처리기와 결과 알림이 건너뛰어짐) 창이 닫혔으면 더 기다리지 않는다(기본값).
    /// </summary>
    public static T? ShowModal<T>(Window dialog, Visual? owner)
    {
        var o = Owner(owner);
        Task<T?> result;
        if (o is { IsVisible: true } && !ReferenceEquals(o, dialog)) result = dialog.ShowDialog<T?>(o);
        else
        {
            var tcs = new TaskCompletionSource<T?>();
            T? value = default;
            dialog.Closed += (_, _) => tcs.TrySetResult(value);
            dialog.Closing += (_, _) => { if (dialog is DialogBase db && db.DialogResult is var r && r is T t) value = t; };
            dialog.Show();
            result = tcs.Task;
        }
        if (dialog is not DialogBase closing) return Wait(result);
        Wait(Task.WhenAny(result, closing.ClosedTask));
        return result.IsCompleted ? result.GetAwaiter().GetResult() : default;
    }

    // ───────────── 알림 ─────────────

    /// <summary>오류 대화상자.</summary>
    public static void Error(Visual? owner, string message, string? title = null)
        => Ask(owner, message, title ?? UiStrings.S_Ui_Error_D70BCF2886, MessageButtons.OK, MessageIcon.Error);

    public static void Info(Visual? owner, string message, string title = "Chickxel")
        => Ask(owner, message, title, MessageButtons.OK, MessageIcon.Info);

    public static void Warn(Visual? owner, string message, string? title = null)
        => Ask(owner, message, title ?? UiStrings.S_Ui_Warn_A8228BB7D9, MessageButtons.OK, MessageIcon.Warning);

    /// <summary>
    /// 메시지 상자(WinForms MessageBox.Show 대신). 창 닫기 단추는 취소(YesNo는 아니요)다.
    /// <paramref name="okText"/>·<paramref name="cancelText"/> = 확인(예)·취소(아니요) 단추 글자를 바꿀 때.
    /// <paramref name="noText"/> = <see cref="MessageButtons.YesNoCancel"/>의 아니요 단추 글자를 바꿀 때.
    /// </summary>
    public static DialogResult Ask(Visual? owner, string message, string title, MessageButtons buttons, MessageIcon icon = MessageIcon.None,
        string? okText = null, string? cancelText = null, string? noText = null)
    {
        var dialog = new MessageDialog(message, title, buttons, icon);
        if (okText is not null) dialog.SetOkText(okText);
        if (cancelText is not null) dialog.SetCancelText(cancelText);
        if (noText is not null) dialog.SetNoText(noText);
        return dialog.ShowDialog(owner);
    }

    // ───────────── 파일 대화상자 ─────────────

    private static IStorageProvider? Storage(Visual? owner) => Owner(owner)?.StorageProvider;

    private static IStorageFolder? Folder(IStorageProvider sp, string? dir)
        => string.IsNullOrEmpty(dir) || !Directory.Exists(dir) ? null : Wait(sp.TryGetFolderFromPathAsync(dir));

    private static List<FilePickerFileType> Types(FileFilter[] filters)
        => filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns }).ToList();

    /// <summary>파일 하나 열기. 취소하면 null.</summary>
    public static string? OpenFile(Visual? owner, string title, string? initialDir, params FileFilter[] filters)
    {
        if (Storage(owner) is not { } sp) return null;
        var files = Wait(sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters.Length > 0 ? Types(filters) : null,
            SuggestedStartLocation = Folder(sp, initialDir),
        }));
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>파일 여러 개 열기. 취소하면 빈 목록.</summary>
    public static IReadOnlyList<string> OpenFiles(Visual? owner, string title, string? initialDir, params FileFilter[] filters)
    {
        if (Storage(owner) is not { } sp) return [];
        var files = Wait(sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = filters.Length > 0 ? Types(filters) : null,
            SuggestedStartLocation = Folder(sp, initialDir),
        }));
        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    /// <summary>저장할 파일 고르기(덮어쓰기 확인은 OS 대화상자가 함). 취소하면 null.</summary>
    public static string? SaveFile(Visual? owner, string title, string? initialDir, string? suggestedName, string defaultExtension,
        params FileFilter[] filters)
    {
        if (Storage(owner) is not { } sp) return null;
        var file = Wait(sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExtension.TrimStart('.'),
            FileTypeChoices = filters.Length > 0 ? Types(filters) : null,
            SuggestedStartLocation = Folder(sp, initialDir),
            ShowOverwritePrompt = true,
        }));
        var path = file?.TryGetLocalPath();
        if (path is not null && Path.GetExtension(path).Length == 0 && defaultExtension.Length > 0)
            path += "." + defaultExtension.TrimStart('.');
        return path;
    }

    /// <summary>폴더 고르기. 취소하면 null.</summary>
    public static string? PickFolder(Visual? owner, string title, string? initialDir = null)
    {
        if (Storage(owner) is not { } sp) return null;
        var folders = Wait(sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = Folder(sp, initialDir),
        }));
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>색 고르기 대화상자(알파 포함). 취소하면 null. 미리보기 콜백은 색을 고르는 동안에도 호출된다.</summary>
    public static Color32? PickColor(Visual? owner, Color32 initial, string? title = null, Action<Color32>? onPreview = null)
    {
        var dlg = new ColorPickDialog(title ?? UiStrings.S_Ui_PickColor_83B9DBA5AF, initial, onPreview);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Value : null;
    }
}

/// <summary>
/// 어두운 테마의 확인/취소 대화상자 바탕. <see cref="ShowDialog"/>는 닫힐 때까지 기다리는 동기 모달이다.
/// 결과가 정해진 단추(<see cref="OkButton"/>·<see cref="CancelButtonControl"/>·<see cref="AddButton"/>)를 누르면 그 결과로 닫히는데,
/// 그 단추의 Click 처리기에서 <c>DialogResult = DialogResult.None</c>으로 하면 닫지 않는다(입력 검사용).
/// 코드에서 <see cref="DialogResult"/>를 None 아닌 값으로 정하면 바로 그 결과로 닫힌다.
/// </summary>
public class DialogBase : Window
{
    protected readonly StackPanel Body;
    protected readonly Button OkButton;
    protected readonly Button CancelButtonControl;
    private readonly StackPanel _buttons;
    private readonly Dictionary<Button, DialogResult> _results = new();
    private DialogResult _result;
    private bool _vetoed;

    public DialogBase(string title, string? okText = null)
    {
        okText ??= UiStrings.S_Ui_DialogBase_3713D24337;
        Title = title;
        CanResize = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        OkButton = Ui.Button(okText, width: 90);
        OkButton.IsDefault = true;
        CancelButtonControl = Ui.Button(UiStrings.S_Ui_DialogBase_A9649D1FA1, width: 90);
        CancelButtonControl.IsCancel = true;
        _results[OkButton] = DialogResult.OK;
        _results[CancelButtonControl] = DialogResult.Cancel;
        _buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 4,
            Margin = new Thickness(0, 12, 0, 0),
        };
        _buttons.Children.Add(OkButton);
        _buttons.Children.Add(CancelButtonControl);
        var root = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        DockPanel.SetDock(_buttons, Dock.Bottom);
        root.Children.Add(_buttons);
        root.Children.Add(Body);
        Content = root;
        // 단추 자신의 Click 처리기가 모두 끝난 뒤(창까지 올라온 뒤) 결과를 정한다.
        AddHandler(Button.ClickEvent, OnAnyButtonClick, RoutingStrategies.Bubble);
    }

    /// <summary>결과. None 아닌 값을 넣으면 그 결과로 닫히고, 단추 Click 처리기 안에서 None을 넣으면 닫기를 취소한다.</summary>
    public DialogResult DialogResult
    {
        get => _result;
        set
        {
            if (value == DialogResult.None)
            {
                _vetoed = true;
                _result = DialogResult.None;
                return;
            }
            _result = value;
            Close(value);
        }
    }

    private void OnAnyButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button b || !_results.TryGetValue(b, out var r)) return;
        bool vetoed = _vetoed;
        _vetoed = false;
        if (vetoed) return;
        _result = r;
        Close(r);
    }

    /// <summary>닫힐 때까지 기다리는 모달 대화상자. 창 닫기 단추로 닫으면 Cancel.</summary>
    public DialogResult ShowDialog(Visual? owner)
    {
        _vetoed = false;
        var r = Ui.ShowModal<DialogResult>(this, owner);
        return r == DialogResult.None ? (_result == DialogResult.None ? DialogResult.Cancel : _result) : r;
    }

    /// <summary>단추를 누르면 이 결과로 닫히게 한다(WinForms Button.DialogResult 대신).</summary>
    protected void SetButtonResult(Button button, DialogResult result) => _results[button] = result;

    /// <summary>확인 버튼 없이 닫기 버튼만 둔다.</summary>
    protected void CloseOnly()
    {
        OkButton.IsVisible = false;
        OkButton.IsDefault = false;
        CancelButtonControl.Content = UiStrings.S_Ui_CloseOnly_11312F9A81;
        CancelButtonControl.IsDefault = true;
    }

    /// <summary>
    /// 결과 단추를 하나 더 둔다(누르면 그 결과로 닫힘): 기본은 확인·취소 사이, <paramref name="beforeOk"/>면 확인 앞.
    /// </summary>
    protected Button AddButton(string text, DialogResult result, bool beforeOk = false)
    {
        var b = Ui.Button(text, width: 90);
        _results[b] = result;
        _buttons.Children.Insert(_buttons.Children.IndexOf(beforeOk ? OkButton : CancelButtonControl), b);
        return b;
    }

    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>창이 닫히면 끝나는 작업 — Closed 처리기보다 먼저 끝나므로 처리기가 예외를 내도 끝난다(<see cref="Ui.ShowModal{T}"/>).</summary>
    internal Task ClosedTask => _closed.Task;

    protected override void OnClosed(EventArgs e)
    {
        _closed.TrySetResult();
        base.OnClosed(e);
    }
}

/// <summary>메시지 상자.</summary>
internal sealed class MessageDialog : DialogBase
{
    public MessageDialog(string message, string title, MessageButtons buttons, MessageIcon icon) : base(title)
    {
        Body.Children.Add(MessageRow(message, icon));
        Body.MinWidth = 280;
        switch (buttons)
        {
            case MessageButtons.OK:
                OkButton.IsVisible = true;
                OkButton.IsCancel = true; // Esc로도 닫힌다(Windows MessageBox와 같게).
                CancelButtonControl.IsVisible = false;
                break;
            case MessageButtons.OKCancel:
                break;
            case MessageButtons.YesNo:
                OkButton.Content = UiStrings.S_Ui_MessageDialog_76C4BD0F9B;
                SetButtonResult(OkButton, DialogResult.Yes);
                CancelButtonControl.Content = UiStrings.S_Ui_MessageDialog_6922C2357A;
                SetButtonResult(CancelButtonControl, DialogResult.No);
                break;
            case MessageButtons.YesNoCancel:
                OkButton.Content = UiStrings.S_Ui_MessageDialog_76C4BD0F9B;
                SetButtonResult(OkButton, DialogResult.Yes);
                _noButton = AddButton(UiStrings.S_Ui_MessageDialog_6922C2357A, DialogResult.No);
                break;
        }
        _noOnClose = buttons == MessageButtons.YesNo;
        Opened += (_, _) => (OkButton.IsVisible ? OkButton : CancelButtonControl).Focus();
    }

    public void SetOkText(string text) => OkButton.Content = text;

    public void SetCancelText(string text) => CancelButtonControl.Content = text;

    /// <summary><see cref="MessageButtons.YesNoCancel"/>의 아니요 단추 글자(다른 단추 구성에서는 아무것도 하지 않음).</summary>
    public void SetNoText(string text)
    {
        if (_noButton is not null) _noButton.Content = text;
    }

    private Button? _noButton;

    /// <summary>메시지 상자 본문: 왼쪽 아이콘 동그라미(아이콘 없으면 생략) + 고를 수 있는 메시지 글.</summary>
    internal static Control MessageRow(string message, MessageIcon icon)
    {
        VectorIcon? mark = icon switch
        {
            MessageIcon.Error => VectorIcon.X,
            MessageIcon.Warning => VectorIcon.ExclamationMark,
            MessageIcon.Question => VectorIcon.QuestionMark,
            MessageIcon.Info => VectorIcon.InfoMark,
            _ => null,
        };
        var color = icon switch
        {
            MessageIcon.Error => Color.FromRgb(230, 90, 90),
            MessageIcon.Warning => AppTheme.WarningColor,
            _ => AppTheme.AccentColor,
        };
        var row = new DockPanel();
        if (mark is { } m)
        {
            var badge = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = AppTheme.Brush(color),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new IconView(m, Brushes.White, 3) { Width = 18, Height = 18 },
            };
            DockPanel.SetDock(badge, Dock.Left);
            row.Children.Add(badge);
        }
        row.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private readonly bool _noOnClose;

    public new DialogResult ShowDialog(Visual? owner)
    {
        var r = base.ShowDialog(owner);
        return r == DialogResult.Cancel && _noOnClose ? DialogResult.No : r;
    }
}

/// <summary>확인·취소가 필요한 곳에서 재사용 색 편집기를 감싸는 대화상자.</summary>
internal sealed class ColorPickDialog : DialogBase
{
    private readonly ColorPickerEditor _editor;

    public ColorPickDialog(string title, Color32 initial, Action<Color32>? onPreview = null) : base(title)
    {
        _editor = new ColorPickerEditor(initial);
        if (onPreview is not null) _editor.ColorChanged += (_, _) => onPreview(_editor.Color);
        Body.Children.Add(_editor);
    }

    public Color32 Value => _editor.Color;
}
