using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Gpixel.App;

namespace GuideShots;

/// <summary>
/// 샷 하나의 도구 상자: 깨끗한 설정 폴더로 메인 창 띄우기, 창·대화상자·팝업(메뉴) 찍기, 영역 자르기, 강조 테두리, 키·마우스 입력, 비공개 멤버 접근.
/// 화면 없이 그리므로 OS 제목 표시줄이 없다 — 창을 찍을 때 제목 줄을 그려 넣는다(<c>titleBar</c>).
/// </summary>
public sealed class ShotKit
{
    private static readonly List<Window> s_windows = [];
    private static readonly List<Popup> s_popups = [];

    public static string Root { get; } = FindRoot();
    /// <summary>화면 언어("ko" 기본, <c>--lang en</c>이면 영어 — 그림은 docs/assets/guide/en/).</summary>
    public static string Language { get; set; } = "ko";
    /// <summary>찍은 그림이 가는 곳(홈페이지·F1 가이드 그림, 영어는 en 하위 폴더).</summary>
    public static string OutDir => Language == "ko"
        ? Path.Combine(Root, "docs", "assets", "guide")
        : Path.Combine(Root, "docs", "assets", "guide", Language);
    /// <summary>예시 그림(늑대귀 모험가, 200×400 — 열면 49×98 픽셀 그림으로 가져와짐).</summary>
    public static string SamplePng => Path.Combine(Root, "tools", "GuideShots", "Sample", "wolf.png");

    public string Id { get; }
    /// <summary>
    /// 이 샷의 예시 파일 폴더(<c>C:\GpixelGuide\MyGame</c> — 그림에 보이는 경로에 사용자 이름이 나오지 않게, 샷마다 비우고 다시 만듦). 설정 폴더는
    /// 그 옆 appdata라 파일 브라우저에 보이지 않는다. 샷은 한 프로세스에서 차례로 돌므로 폴더 하나를 같이 쓴다.
    /// </summary>
    public string WorkDir { get; }
    public List<string> Saved { get; } = [];
    public MainForm? MainWindow { get; private set; }

    public ShotKit(string id)
    {
        Id = id;
        var shotRoot = OperatingSystem.IsWindows() ? @"C:\GpixelGuide" : Path.Combine(Path.GetTempPath(), "GpixelGuide");
        if (Directory.Exists(shotRoot)) Directory.Delete(shotRoot, recursive: true);
        WorkDir = Path.Combine(shotRoot, "MyGame");
        Directory.CreateDirectory(WorkDir);
        var data = Path.Combine(shotRoot, "appdata");
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable(Platform.DataDirVariable, data);
        App.Settings = AppSettings.Load();
        App.Settings.Language = Language;
        Gpixel.App.Localization.UiLanguage.Apply(Language);
    }

    public static void InstallTracking()
    {
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (!s_windows.Contains(w)) s_windows.Add(w); });
        Window.WindowClosedEvent.AddClassHandler<Window>((w, _) => s_windows.Remove(w));
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((p, _) =>
        {
            s_popups.Remove(p);
            if (p.IsOpen) s_popups.Add(p);
        });
    }

    /// <summary>열려 있는 창(연 순서).</summary>
    public IReadOnlyList<Window> Windows => s_windows.Where(w => w.IsVisible).ToList();

    // ───────────── 준비 ─────────────

    /// <summary>
    /// 메인 창을 띄운다. <paramref name="open"/> = 열 파일(.gpixel·이미지), <paramref name="folder"/> = 파일 브라우저 폴더.
    /// 기본 크기 1600×900(가이드 그림 기준 크기).
    /// </summary>
    public MainForm Main(string? open = null, string? folder = null, int width = 1600, int height = 900)
    {
        var main = new MainForm(new StartupArgs(folder, open), App.Settings) { Width = width, Height = height };
        main.Show();
        MainWindow = main;
        Settle(400);
        return main;
    }

    /// <summary>예시 그림을 <see cref="WorkDir"/>에 <paramref name="name"/>으로 복사해 경로를 돌려준다(파일 브라우저·열기용).</summary>
    public string CopySample(string name = "늑대귀 모험가.png", string? subFolder = null)
    {
        var dir = subFolder is null ? WorkDir : Path.Combine(WorkDir, subFolder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.Copy(SamplePng, path, overwrite: true);
        return path;
    }

    /// <summary>대기열을 돌리고 그리기를 진행하며 <paramref name="ms"/>만큼 기다린다(타이머·애니메이션·늦은 배치).</summary>
    public void Settle(int ms = 150)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (sw.ElapsedMilliseconds < ms) Thread.Sleep(10);
        } while (sw.ElapsedMilliseconds < ms);
    }

    /// <summary>모든 창을 닫는다(메인 창은 저장 묻기를 건너뜀 — 수정한 문서가 있어도 "저장할까요?" 모달에서 멈추지 않게).</summary>
    public void CloseAll()
    {
        foreach (var w in s_windows.ToList())
        {
            if (w is MainForm) Set(w, "_closeConfirmed", true);
            try { w.Close(); } catch { /* 닫힐 때 묻는 창 등은 무시 */ }
        }
        Settle(50);
        s_windows.Clear();
        s_popups.Clear();
    }

    // ───────────── 대화상자 ─────────────

    /// <summary>
    /// <paramref name="open"/>이 띄우는 새 창(모달이면 그 안에서)이 뜨면 <paramref name="settleMs"/> 뒤 <paramref name="withWindow"/>를 부르고,
    /// 끝나면 창을 닫는다(<paramref name="keepOpen"/>이면 그대로). 모달 루프 안에서 돌므로 <paramref name="withWindow"/>에서
    /// <see cref="Settle"/> 대신 <see cref="Later"/>을 쓰지 않아도 되게 이미 기다린 뒤 부른다.
    /// </summary>
    public Window Dialog(Action open, Action<Window> withWindow, int settleMs = 500, bool keepOpen = false)
    {
        var before = Windows.ToHashSet();
        Window? found = null;
        Exception? error = null;
        bool done = false, stopped = false;
        void Poll()
        {
            if (stopped) return;
            var w = Windows.LastOrDefault(x => !before.Contains(x));
            if (w is null)
            {
                DispatcherTimer.RunOnce(Poll, TimeSpan.FromMilliseconds(30));
                return;
            }
            found = w;
            DispatcherTimer.RunOnce(() =>
            {
                try { withWindow(w); }
                catch (Exception ex) { error = ex; }
                finally
                {
                    done = true;
                    if (!keepOpen && w.IsVisible) w.Close();
                }
            }, TimeSpan.FromMilliseconds(settleMs));
        }
        DispatcherTimer.RunOnce(Poll, TimeSpan.FromMilliseconds(30));
        try
        {
            open();
            var sw = Stopwatch.StartNew();
            while (!done && sw.ElapsedMilliseconds < 15000) Settle(30);
        }
        finally
        {
            // 열기가 실패했거나 창이 안 떴으면 기다리던 타이머를 멈춘다(다음 샷의 창을 잘못 잡지 않게).
            stopped = true;
        }
        if (error is not null) throw new InvalidOperationException("dialog step failed: " + error.Message, error);
        if (!done) throw new TimeoutException("no new window appeared");
        return found!;
    }

    /// <summary>모달 루프 안 등에서 <paramref name="ms"/> 뒤 한 번 실행(그 사이 화면이 그려짐).</summary>
    public static void Later(int ms, Action action) => DispatcherTimer.RunOnce(action, TimeSpan.FromMilliseconds(ms));

    /// <summary>모달 창 안에서 기다리기(중첩 루프를 쓰지 않고 시간만 보냄 — 이미 돌고 있는 모달 루프가 그린다).</summary>
    public static void Pump(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(10);
        }
    }

    // ───────────── 찍기 ─────────────

    /// <summary>
    /// 창(또는 팝업 포함 화면)을 찍어 <paramref name="name"/>.png로 저장한다(없으면 샷 id). <paramref name="crop"/>은 창 좌표(제목 줄 빼고),
    /// <paramref name="marks"/>는 강조 테두리를 그릴 컨트롤, <paramref name="titleBar"/>는 창 제목 줄을 그려 넣을지(기본: 자르지 않을 때).
    /// </summary>
    public string Save(TopLevel top, string? name = null, Rect? crop = null, IEnumerable<Control>? marks = null, bool? titleBar = null,
        IEnumerable<Rect>? markRects = null)
    {
        var frame = Capture(top);
        var rects = (marks ?? []).Select(c => Bounds(c, top)).Concat(markRects ?? []).ToList();
        bool title = titleBar ?? crop is null;
        var src = crop ?? new Rect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height);
        src = src.Intersect(new Rect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height));
        int bar = title && top is Window ? TitleHeight : 0;
        var size = new PixelSize((int)Math.Round(src.Width), (int)Math.Round(src.Height) + bar);
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            if (bar > 0) DrawTitleBar(ctx, ((Window)top).Title ?? "Gpixel", size.Width);
            ctx.DrawImage(frame, src, new Rect(0, bar, src.Width, src.Height));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 140, 40)), 3);
            foreach (var r in rects)
            {
                var shifted = new Rect(r.X - src.X - 3, r.Y - src.Y + bar - 3, r.Width + 6, r.Height + 6);
                ctx.DrawRectangle(null, pen, shifted, 4, 4);
            }
        }
        var file = Path.Combine(OutDir, (name ?? Id) + ".png");
        Directory.CreateDirectory(OutDir);
        target.Save(file);
        Saved.Add(Path.GetRelativePath(Root, file));
        return file;
    }

    /// <summary>컨트롤 둘레(+<paramref name="pad"/>)만 잘라 저장한다.</summary>
    public string SaveControl(Control control, string? name = null, double pad = 8, IEnumerable<Control>? marks = null)
    {
        var top = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("control is not in a window");
        var b = Bounds(control, top).Inflate(pad);
        return Save(top, name, b, marks, titleBar: false);
    }

    /// <summary>여러 컨트롤을 모두 담는 영역을 저장한다.</summary>
    public string SaveArea(IEnumerable<Control> controls, string? name = null, double pad = 8, IEnumerable<Control>? marks = null)
    {
        var list = controls.ToList();
        var top = TopLevel.GetTopLevel(list[0]) ?? throw new InvalidOperationException("control is not in a window");
        var b = list.Select(c => Bounds(c, top)).Aggregate((a, c) => a.Union(c)).Inflate(pad);
        return Save(top, name, b, marks, titleBar: false);
    }

    /// <summary>창 화면 + 그 창에서 열린 팝업(메뉴·우클릭 메뉴·툴팁 등)을 제자리에 합친 그림.</summary>
    public Bitmap Capture(TopLevel top)
    {
        var frame = top.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        var popups = s_popups.Where(p => p.IsOpen && p.Host is TopLevel && IsFrom(p, top)).ToList();
        if (popups.Count == 0) return frame;
        var size = frame.PixelSize;
        var target = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            ctx.DrawImage(frame, new Rect(0, 0, size.Width, size.Height));
            var origin = Position(top);
            foreach (var p in popups)
            {
                var host = (TopLevel)p.Host!;
                var img = host.CaptureRenderedFrame();
                if (img is null) continue;
                var pos = Position(host) - origin;
                ctx.DrawImage(img, new Rect(pos.X, pos.Y, img.PixelSize.Width, img.PixelSize.Height));
            }
        }
        return target;
    }

    private static bool IsFrom(Popup p, TopLevel top)
    {
        if (TopLevel.GetTopLevel(p) is { } own && ReferenceEquals(own, top)) return true;
        return p.PlacementTarget is { } t && ReferenceEquals(TopLevel.GetTopLevel(t), top);
    }

    /// <summary>창·팝업의 화면 위치(팝업 위치는 플랫폼 구현의 Position — 공개 API가 아니어서 리플렉션).</summary>
    private static PixelPoint Position(TopLevel top) => top is Window w ? w.Position
        : top.PlatformImpl is { } impl && impl.GetType().GetProperty("Position", Any)?.GetValue(impl) is PixelPoint p ? p : default;

    public const int TitleHeight = 32;

    private static void DrawTitleBar(DrawingContext ctx, string title, int width)
    {
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 36)), new Rect(0, 0, width, TitleHeight));
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(60, 60, 66)), new Rect(0, TitleHeight - 1, width, 1));
        using (var stream = new MemoryStream(Gpixel.App.Icons.AppIconPng()))
        {
            using var icon = new Bitmap(stream);
            ctx.DrawImage(icon, new Rect(10, 8, 16, 16));
        }
        var text = new FormattedText(title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Malgun Gothic, Segoe UI"), 13, Brushes.Gainsboro);
        ctx.DrawText(text, new Point(34, (TitleHeight - text.Height) / 2));
        // 최소화·최대화·닫기 단추 자리(선 아이콘).
        var pen = new Pen(Brushes.Gainsboro, 1);
        double x = width - 128;
        ctx.DrawLine(pen, new Point(x + 15, 16.5), new Point(x + 25, 16.5));
        x += 46;
        ctx.DrawRectangle(null, pen, new Rect(x + 15.5, 11.5, 9, 9));
        x += 46;
        ctx.DrawLine(pen, new Point(x + 15, 11), new Point(x + 25, 21));
        ctx.DrawLine(pen, new Point(x + 25, 11), new Point(x + 15, 21));
    }

    // ───────────── 찾기 ─────────────

    /// <summary>컨트롤의 창 좌표 사각형.</summary>
    public static Rect Bounds(Control c, Visual top)
    {
        var p = c.TranslatePoint(new Point(0, 0), top) ?? throw new InvalidOperationException("control not attached to " + top);
        return new Rect(p, c.Bounds.Size);
    }

    public static IEnumerable<T> All<T>(Visual root) where T : Visual => root.GetVisualDescendants().OfType<T>();

    public static T Find<T>(Visual root, Func<T, bool>? match = null) where T : Visual
        => All<T>(root).FirstOrDefault(c => match?.Invoke(c) ?? true)
           ?? throw new InvalidOperationException($"no {typeof(T).Name} found");

    /// <summary>글자가 정확히 <paramref name="text"/>인 TextBlock(또는 그 글자를 담은 단추·메뉴 항목 등 가장 가까운 Control 조상).</summary>
    public static Control FindText(Visual root, string text, bool ancestorControl = false)
    {
        var tb = All<TextBlock>(root).FirstOrDefault(t => t.Text == text)
                 ?? throw new InvalidOperationException($"no text '{text}'");
        if (!ancestorControl) return tb;
        return tb.GetVisualAncestors().OfType<Button>().Cast<Control>().FirstOrDefault()
               ?? tb.GetVisualAncestors().OfType<MenuItem>().Cast<Control>().FirstOrDefault()
               ?? tb.GetVisualAncestors().OfType<TabItem>().Cast<Control>().FirstOrDefault()
               ?? tb;
    }

    // ───────────── 입력 ─────────────

    public void Key(TopLevel top, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        top.KeyPress(key, mods, PhysicalKey.None, null);
        top.KeyRelease(key, mods, PhysicalKey.None, null);
        Settle(100);
    }

    public void Type(TopLevel top, string text)
    {
        top.KeyTextInput(text);
        Settle(50);
    }

    public static Point Center(Control c, Visual top)
    {
        var b = Bounds(c, top);
        return b.Center;
    }
    // ───────────── 메뉴 명령 ─────────────

    /// <summary>메뉴 명령 id 목록(단축키 표의 "분류/이름" — 메뉴 순서).</summary>
    public IReadOnlyList<string> CommandIds
        => Get<System.Collections.IEnumerable>(MainWindow!, "_commands").Cast<object>().Select(c => Get<ShortcutCommand>(c, "Info").Id).ToList();

    /// <summary>
    /// 메뉴 명령을 실행한다(<paramref name="id"/> = 단축키 표의 "분류/이름" 또는 이름만, 끝의 "…"는 있어도 없어도 됨 — 전체 목록은 <see cref="CommandIds"/>).
    /// 모달 창을 띄우는 명령은 <see cref="Dialog"/> 안에서 부른다.
    /// </summary>
    public void Command(string id)
    {
        static string Plain(string s) => s.TrimEnd('…');
        var want = Plain(id);
        var entry = Get<System.Collections.IEnumerable>(MainWindow!, "_commands").Cast<object>()
            .FirstOrDefault(c => Plain(Get<ShortcutCommand>(c, "Info").Id) is var cid && (cid == want || cid.EndsWith("/" + want, StringComparison.Ordinal)))
            ?? throw new InvalidOperationException("no command " + id);
        Get<Func<bool>>(entry, "Action")();
    }


    public void Click(TopLevel top, Point p, MouseButton button = MouseButton.Left, RawInputModifiers mods = RawInputModifiers.None, int settleMs = 150)
    {
        top.MouseMove(p, mods);
        top.MouseDown(p, button, mods);
        top.MouseUp(p, button, mods);
        Settle(settleMs);
    }

    public void Click(Control c, MouseButton button = MouseButton.Left, RawInputModifiers mods = RawInputModifiers.None, int settleMs = 150)
    {
        var top = TopLevel.GetTopLevel(c) ?? throw new InvalidOperationException("control is not in a window");
        Click(top, Center(c, top), button, mods, settleMs);
    }

    public void DoubleClick(Control c)
    {
        Click(c, settleMs: 0);
        Click(c);
    }

    public void Hover(TopLevel top, Point p, int settleMs = 300)
    {
        top.MouseMove(p);
        Settle(settleMs);
    }

    public void Drag(TopLevel top, Point from, Point to, int steps = 12, MouseButton button = MouseButton.Left, RawInputModifiers mods = RawInputModifiers.None)
    {
        top.MouseMove(from, mods);
        top.MouseDown(from, button, mods);
        for (int i = 1; i <= steps; i++)
        {
            var p = from + (to - from) * (i / (double)steps);
            top.MouseMove(p, mods);
            Settle(10);
        }
        top.MouseUp(to, button, mods);
        Settle(150);
    }

    // ───────────── 비공개 멤버 ─────────────

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static T Get<T>(object target, string name)
    {
        for (var t = target.GetType(); t is not null; t = t.BaseType)
        {
            if (t.GetField(name, Any) is { } f) return (T)f.GetValue(target)!;
            if (t.GetProperty(name, Any) is { } p) return (T)p.GetValue(target)!;
        }
        throw new MissingMemberException(target.GetType().Name, name);
    }

    public static void Set(object target, string name, object? value)
    {
        for (var t = target.GetType(); t is not null; t = t.BaseType)
        {
            if (t.GetField(name, Any) is { } f) { f.SetValue(target, value); return; }
            if (t.GetProperty(name, Any) is { } p) { p.SetValue(target, value); return; }
        }
        throw new MissingMemberException(target.GetType().Name, name);
    }

    public static object? Call(object target, string name, params object?[] args)
    {
        for (var t = target.GetType(); t is not null; t = t.BaseType)
        {
            var m = t.GetMethods(Any).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
            if (m is not null) return m.Invoke(m.IsStatic ? null : target, args);
        }
        throw new MissingMethodException(target.GetType().Name, name);
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Gpixel.sln"))) return dir.FullName;
        throw new InvalidOperationException("Gpixel.sln not found above " + AppContext.BaseDirectory);
    }
}
