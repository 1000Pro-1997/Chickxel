using System.Text.RegularExpressions;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Gpixel.Core.Model;

namespace Gpixel.App;

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

    public static Button Button(string text, EventHandler<RoutedEventArgs>? onClick = null, int width = 0)
    {
        var b = new Button { Content = text, Margin = new Thickness(2) };
        if (width > 0) b.Width = width;
        if (onClick is not null) b.Click += onClick;
        return b;
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
    /// UI 스레드에서만 부른다.
    /// </summary>
    public static T Wait<T>(Task<T> task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }

    public static void Wait(Task task) => Wait(task.ContinueWith(t => { t.GetAwaiter().GetResult(); return 0; }, TaskScheduler.Default));

    /// <summary>창을 모달로 띄우고 닫힐 때까지 기다린다(주인 창이 없으면 그냥 띄움).</summary>
    public static T? ShowModal<T>(Window dialog, Visual? owner)
    {
        var o = Owner(owner);
        if (o is { IsVisible: true } && !ReferenceEquals(o, dialog)) return Wait(dialog.ShowDialog<T>(o));
        var tcs = new TaskCompletionSource<T?>();
        T? result = default;
        dialog.Closed += (_, _) => tcs.TrySetResult(result);
        dialog.Closing += (_, _) => { if (dialog is DialogBase db && db.DialogResult is var r && r is T t) result = t; };
        dialog.Show();
        return Wait(tcs.Task);
    }

    // ───────────── 알림 ─────────────

    /// <summary>오류 대화상자.</summary>
    public static void Error(Visual? owner, string message, string title = "오류")
        => Ask(owner, message, title, MessageButtons.OK, MessageIcon.Error);

    public static void Info(Visual? owner, string message, string title = "Gpixel")
        => Ask(owner, message, title, MessageButtons.OK, MessageIcon.Info);

    public static void Warn(Visual? owner, string message, string title = "알림")
        => Ask(owner, message, title, MessageButtons.OK, MessageIcon.Warning);

    /// <summary>메시지 상자(WinForms MessageBox.Show 대신). 창 닫기 단추는 취소(YesNo는 아니요)다.</summary>
    public static DialogResult Ask(Visual? owner, string message, string title, MessageButtons buttons, MessageIcon icon = MessageIcon.None)
        => new MessageDialog(message, title, buttons, icon).ShowDialog(owner);

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

    /// <summary>색 고르기 대화상자(알파 포함). 취소하면 null.</summary>
    public static Color32? PickColor(Visual? owner, Color32 initial, string title = "색 고르기")
    {
        var dlg = new ColorPickDialog(title, initial);
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

    public DialogBase(string title, string okText = "확인")
    {
        Title = title;
        CanResize = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        OkButton = Ui.Button(okText, width: 90);
        OkButton.IsDefault = true;
        CancelButtonControl = Ui.Button("취소", width: 90);
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
        CancelButtonControl.Content = "닫기";
        CancelButtonControl.IsDefault = true;
    }

    /// <summary>확인·취소 사이에 결과 단추를 하나 더 둔다(누르면 그 결과로 닫힘).</summary>
    protected Button AddButton(string text, DialogResult result)
    {
        var b = Ui.Button(text, width: 90);
        _results[b] = result;
        _buttons.Children.Insert(_buttons.Children.IndexOf(CancelButtonControl), b);
        return b;
    }
}

/// <summary>메시지 상자.</summary>
internal sealed class MessageDialog : DialogBase
{
    public MessageDialog(string message, string title, MessageButtons buttons, MessageIcon icon) : base(title)
    {
        var (glyph, color) = icon switch
        {
            MessageIcon.Error => ("✕", Color.FromRgb(230, 90, 90)),
            MessageIcon.Warning => ("!", AppTheme.WarningColor),
            MessageIcon.Question => ("?", AppTheme.AccentColor),
            MessageIcon.Info => ("i", AppTheme.AccentColor),
            _ => ("", AppTheme.TextColor),
        };
        var row = new DockPanel();
        if (glyph.Length > 0)
        {
            var badge = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = AppTheme.Brush(color),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontWeight = FontWeight.Bold,
                    FontSize = 16,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            DockPanel.SetDock(badge, Dock.Left);
            row.Children.Add(badge);
        }
        row.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, VerticalAlignment = VerticalAlignment.Center });
        Body.Children.Add(row);
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
                OkButton.Content = "예";
                SetButtonResult(OkButton, DialogResult.Yes);
                CancelButtonControl.Content = "아니요";
                SetButtonResult(CancelButtonControl, DialogResult.No);
                break;
            case MessageButtons.YesNoCancel:
                OkButton.Content = "예";
                SetButtonResult(OkButton, DialogResult.Yes);
                AddButton("아니요", DialogResult.No);
                break;
        }
        _noOnClose = buttons == MessageButtons.YesNo;
        Opened += (_, _) => (OkButton.IsVisible ? OkButton : CancelButtonControl).Focus();
    }

    private readonly bool _noOnClose;

    public new DialogResult ShowDialog(Visual? owner)
    {
        var r = base.ShowDialog(owner);
        return r == DialogResult.Cancel && _noOnClose ? DialogResult.No : r;
    }
}

/// <summary>색 고르기 창에 함께 보일 것: 앱 팔레트들, 최근 색, 스포이드로 찍을 현재 그림(없으면 null).</summary>
public sealed record ColorPickSources(IReadOnlyList<NamedPalette> Palettes, IReadOnlyList<Color32> Recent, Func<PixelImage?> Image);

/// <summary>
/// 색 고르기(알파 포함). 오른쪽에 앱의 팔레트들과 최근 색(누르면 그 색, 알파는 그 색 그대로)과
/// <b>스포이드</b>(현재 프레임 그림을 크게 보여 주고 누른 픽셀의 색을 집음)가 있다.
/// </summary>
internal sealed class ColorPickDialog : DialogBase
{
    /// <summary>MainForm이 연결한다(팔레트·최근 색·현재 그림). 없으면 색상 상자만.</summary>
    public static Func<ColorPickSources?>? Sources { get; set; }

    private readonly ColorView _view;

    public ColorPickDialog(string title, Color32 initial) : base(title)
    {
        _view = new ColorView
        {
            Color = initial.ToColor(),
            IsAlphaEnabled = true,
            IsAlphaVisible = true,
            IsColorPaletteVisible = false,
            Width = 340,
        };
        if (Sources?.Invoke() is not { } src)
        {
            Body.Children.Add(_view);
            return;
        }
        var side = new StackPanel { Spacing = 4, Width = 230, Margin = new Thickness(10, 0, 0, 0) };
        void Pick(Color32 c) => _view.Color = c.ToColor();
        foreach (var p in src.Palettes.Where(p => p.Colors.Count > 0))
        {
            side.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush });
            side.Children.Add(Swatches(p.Colors, Pick));
        }
        if (src.Recent.Count > 0)
        {
            side.Children.Add(new TextBlock { Text = "최근 색", FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush });
            side.Children.Add(Swatches(src.Recent, Pick));
        }
        var image = src.Image();
        if (image is not null)
        {
            var picker = new ImagePicker(image, Pick) { IsVisible = false };
            var dropper = new ToggleButton { Content = "스포이드 — 그림에서 집기", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center }
                .Tip("현재 프레임 그림이 보이면 원하는 픽셀을 누르세요(투명한 곳은 집지 않음)");
            dropper.IsCheckedChanged += (_, _) => picker.IsVisible = dropper.IsChecked == true;
            side.Children.Add(dropper);
            side.Children.Add(picker);
        }
        Body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _view, new ScrollViewer { Content = side, MaxHeight = 460, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } },
        });
    }

    public Color32 Value => _view.Color.ToColor32();

    /// <summary>색 칸들(누르면 <paramref name="pick"/>) — 색 고르기 창과 효과 창의 "그림에서 색 가져오기"가 같이 쓴다.</summary>
    internal static WrapPanel Swatches(IEnumerable<Color32> colors, Action<Color32> pick)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in colors.Distinct())
        {
            var b = new Border
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(1),
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new SwatchCell(c),
            };
            ToolTip.SetTip(b, c.A == 0 ? "투명" : c.ToHex());
            b.PointerPressed += (_, e) => { pick(c); e.Handled = true; };
            wrap.Children.Add(b);
        }
        return wrap;
    }

    private sealed class SwatchCell(Color32 color) : Control
    {
        public override void Render(DrawingContext g) => ImageUtil.DrawSwatch(g, new Rect(Bounds.Size), color);
    }

    /// <summary>스포이드: 그림을 칸에 맞는 정수 배율로 보여 주고, 누르거나 끄는 픽셀의 색을 집는다.</summary>
    private sealed class ImagePicker : Control
    {
        private readonly PixelImage _image;
        private readonly WriteableBitmap _bitmap;
        private readonly Action<Color32> _pick;
        private const double Box = 220;

        public ImagePicker(PixelImage image, Action<Color32> pick)
        {
            _image = image;
            _bitmap = ImageUtil.ToBitmap(image);
            _pick = pick;
            Width = Box;
            Height = Box;
            Cursor = new Cursor(StandardCursorType.Cross);
            ClipToBounds = true;
        }

        private Rect ImageRect
        {
            get
            {
                double fit = Math.Min(Box / _image.Width, Box / _image.Height);
                double s = fit >= 1 ? Math.Floor(fit) : fit;
                double w = _image.Width * s, h = _image.Height * s;
                return new Rect((Box - w) / 2, (Box - h) / 2, w, h);
            }
        }

        public override void Render(DrawingContext g)
        {
            var r = ImageRect;
            g.FillRectangle(AppTheme.InputBrush, new Rect(Bounds.Size));
            AppTheme.DrawChecker(g, r, 6);
            using (ImageUtil.Pixelated(g))
                g.DrawImage(_bitmap, new Rect(0, 0, _image.Width, _image.Height), r);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            e.Pointer.Capture(this);
            PickAt(e.GetPosition(this));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) PickAt(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Pointer.Capture(null);
        }

        private void PickAt(Avalonia.Point p)
        {
            var r = ImageRect;
            int x = (int)Math.Floor((p.X - r.X) / r.Width * _image.Width), y = (int)Math.Floor((p.Y - r.Y) / r.Height * _image.Height);
            if (!_image.Contains(x, y)) return;
            var c = _image.Get(x, y);
            if (c.A > 0) _pick(c);
        }
    }
}
