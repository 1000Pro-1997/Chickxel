using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace GuideShots;

/// <summary>녹화에 그려 넣는 마우스 커서 모양(앱 커서와 같은 종류를 벡터로 그림).</summary>
public enum ClipCursor
{
    None,
    Arrow,
    Hand,
    IBeam,
    /// <summary>위아래 화살표(숫자 칸 위/아래 단추 위).</summary>
    SizeNS,
    /// <summary>십자(캔버스의 그리기 도구).</summary>
    Cross,
}

/// <summary>저장할 형식(여럿을 함께 고름). <see cref="Mp4"/>는 JPG 포스터(<see cref="ClipRecorder.MarkPoster"/>)도 함께 쓴다.</summary>
[Flags]
public enum ClipFormat
{
    WebP = 1,
    Gif = 2,
    Mp4 = 4,
}

/// <summary>
/// 움짤·영상 녹화: 샷 안에서 실제 앱에 마우스·키 입력을 보내며 그때마다 메인 창(그 위에 뜬 대화상자·팝업 포함)의 한 영역을 프레임으로 찍고,
/// 각 프레임에 정해 둔 길이만큼 머문다. 포인터 자리에는 벡터로 그린 커서를 얹고, 버튼을 누르고 있는 동안은 빨간 고리를 두른다.
/// 저장은 애니메이션 WebP(Pillow — 프레임 길이 그대로), GIF(ffmpeg palettegen/paletteuse), MP4 H.264(ffmpeg, yuv420p, faststart) + JPG 포스터.
/// 좌표는 모두 메인 창 좌표다(대화상자·팝업 안 컨트롤은 <see cref="At"/>·<see cref="BoundsOf"/>가 바꿔 줌).
/// </summary>
public sealed class ClipRecorder : IDisposable
{
    private readonly ShotKit _kit;
    private readonly Window _main;
    private readonly Rect _crop;
    private readonly bool _titleBar;
    private readonly string _dir;
    private readonly List<(string File, int Ms)> _frames = [];
    /// <summary>메인 창 위에 겹쳐 그리는 대화상자 창과 그 창 바탕(제목 줄 포함)의 왼쪽 위(메인 창 좌표).</summary>
    private readonly Dictionary<Window, Point> _dialogs = [];
    private byte[]? _last;
    private int _poster = -1;
    private MouseButton? _button;
    private TopLevel? _pressTarget;

    /// <summary>뗀 뒤 첫 프레임에도 고리를 그린다(누른 사이를 찍지 않은 짧은 클릭이 보이게).</summary>
    private bool _ringAfterUp;

    /// <param name="crop">찍을 영역(메인 창 좌표).</param>
    /// <param name="titleBar">영역 위에 창 제목 줄(<see cref="ShotKit.TitleHeight"/>)을 그려 넣을지(창 맨 위부터 자를 때 — OS 창처럼 보이게).</param>
    /// <param name="frameMs">움직이는 동안 한 프레임 길이(ms).</param>
    public ClipRecorder(ShotKit kit, Window main, Rect crop, bool titleBar = false, int frameMs = 50)
    {
        _kit = kit;
        _main = main;
        _crop = new Rect(Math.Round(crop.X), Math.Round(crop.Y), Math.Round(crop.Width), Math.Round(crop.Height));
        _titleBar = titleBar;
        FrameMs = frameMs;
        _dir = Path.Combine(Path.GetTempPath(), "guideshots-clip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        Pointer = _crop.Center;
    }

    /// <summary>움직이는 동안 한 프레임 길이(ms).</summary>
    public int FrameMs { get; set; }
    /// <summary>지금 그릴 커서 모양.</summary>
    public ClipCursor Cursor { get; set; } = ClipCursor.Arrow;
    /// <summary>포인터 자리(메인 창 좌표).</summary>
    public Point Pointer { get; private set; }
    /// <summary>누르고 있는 보조 키(이동·누르기·떼기에 함께 보냄 — 예: Shift를 누른 채 클릭).</summary>
    public RawInputModifiers Modifiers { get; set; }
    /// <summary>제목 줄에 쓸 글로 바꾸기(예: 샷 작업 폴더 경로를 예시 경로로). 없으면 창 제목 그대로.</summary>
    public Func<string, string>? TitleFilter { get; set; }
    /// <summary>찍은 프레임 수(같은 그림이 이어지면 한 프레임으로 합침).</summary>
    public int FrameCount => _frames.Count;
    /// <summary>찍은 길이(ms).</summary>
    public int DurationMs => _frames.Sum(f => f.Ms);

    // ───────────── 좌표 ─────────────

    /// <summary>컨트롤의 메인 창 좌표 사각형(메인 창·대화상자·팝업 안 어디든).</summary>
    public Rect BoundsOf(Control c)
    {
        var top = TopLevel.GetTopLevel(c) ?? throw new InvalidOperationException("control is not in a window");
        return ShotKit.Bounds(c, top).Translate(Offset(top));
    }

    /// <summary>컨트롤 가운데의 메인 창 좌표.</summary>
    public Point At(Control c) => BoundsOf(c).Center;

    /// <summary>창·팝업 안 (0,0)의 메인 창 좌표.</summary>
    private Vector Offset(TopLevel top)
    {
        if (ReferenceEquals(top, _main)) return default;
        if (top is Window w) return (Vector)DialogOrigin(w) + new Vector(0, ShotKit.TitleHeight);
        // 팝업: 띄운 창 기준 위치 차이(플랫폼이 창 위치로 팝업 자리를 정함).
        var popup = ShotKit.OpenPopups.FirstOrDefault(p => ReferenceEquals(p.Host, top));
        var owner = popup?.PlacementTarget is { } t ? TopLevel.GetTopLevel(t) : popup is not null ? TopLevel.GetTopLevel(popup) : null;
        owner ??= _main;
        var d = ShotKit.Position(top) - ShotKit.Position(owner);
        return Offset(owner) + new Vector(d.X, d.Y);
    }

    /// <summary>대화상자 바탕(제목 줄 포함)의 왼쪽 위: 처음 볼 때 메인 창 가운데(찍을 영역 안)에 놓고 그대로 둔다.</summary>
    private Point DialogOrigin(Window w)
    {
        if (_dialogs.TryGetValue(w, out var p)) return p;
        var size = w.ClientSize;
        double h = size.Height + ShotKit.TitleHeight;
        var c = _main.ClientSize;
        p = new Point(Math.Round((c.Width - size.Width) / 2), Math.Round(Math.Max(0, (c.Height - h) / 2)));
        _dialogs[w] = p;
        return p;
    }

    /// <summary>지금 메인 창 위에 떠 있는 대화상자(연 순서).</summary>
    private List<Window> Dialogs() => _kit.Windows.Where(w => !ReferenceEquals(w, _main) && w.IsVisible).ToList();

    /// <summary>점 <paramref name="p"/>(메인 창 좌표)를 받을 창·팝업과 그 안의 좌표: 맨 위 팝업 → 대화상자 → 메인 창.</summary>
    private (TopLevel Top, Point Local) Hit(Point p)
    {
        foreach (var popup in ShotKit.OpenPopups.AsEnumerable().Reverse())
        {
            if (popup.Host is not TopLevel host) continue;
            var o = Offset(host);
            if (new Rect(host.ClientSize).Contains(p - o)) return (host, p - o);
        }
        foreach (var w in Dialogs().AsEnumerable().Reverse())
        {
            var o = Offset(w);
            if (new Rect(w.ClientSize).Contains(p - o)) return (w, p - o);
        }
        return (_main, p);
    }

    // ───────────── 입력 ─────────────

    private RawInputModifiers ButtonFlag => _button switch
    {
        MouseButton.Left => RawInputModifiers.LeftMouseButton,
        MouseButton.Right => RawInputModifiers.RightMouseButton,
        MouseButton.Middle => RawInputModifiers.MiddleMouseButton,
        _ => RawInputModifiers.None,
    };

    /// <summary>포인터를 <paramref name="p"/>로 보낸다(누르고 있으면 누른 창에 — 끌기).</summary>
    private void SendMove(Point p)
    {
        Pointer = p;
        if (_pressTarget is { } t) t.MouseMove(p - Offset(t), Modifiers | ButtonFlag);
        else
        {
            var (top, local) = Hit(p);
            top.MouseMove(local, Modifiers);
        }
    }

    /// <summary>프레임 없이 포인터만 옮긴다.</summary>
    public void Place(Point p)
    {
        SendMove(p);
        ShotKit.Pump(20);
    }

    /// <summary>지금 화면을 한 프레임으로 찍어 <paramref name="ms"/> 동안 둔다(그 사이 앱 타이머도 돌림).</summary>
    public void Hold(int ms)
    {
        ShotKit.Pump(Math.Clamp(ms, 15, 120));
        Snap(ms);
    }

    /// <summary>
    /// 포인터를 <paramref name="to"/>까지 <paramref name="ms"/> 동안 옮기며 프레임마다 찍는다(누르고 있으면 끌기).
    /// <paramref name="ease"/>면 천천히 출발·도착(손으로 옮기는 것처럼), 아니면 일정한 빠르기(획 긋기).
    /// </summary>
    public void Move(Point to, int ms = 450, bool ease = true)
    {
        var from = Pointer;
        int steps = Math.Max(1, ms / FrameMs);
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            if (ease) t = t * t * (3 - 2 * t);
            SendMove(from + (to - from) * t);
            ShotKit.Pump(12);
            Snap(FrameMs);
        }
    }

    /// <summary>점들을 차례로 이으며 <paramref name="ms"/> 동안 일정한 빠르기로 옮긴다(곡선 획).</summary>
    public void Trace(IReadOnlyList<Point> points, int ms)
    {
        if (points.Count == 0) return;
        double total = 0;
        var prev = Pointer;
        foreach (var p in points)
        {
            total += Distance(prev, p);
            prev = p;
        }
        int steps = Math.Max(1, ms / FrameMs);
        // 프레임 사이에도 촘촘히 이동 이벤트를 보내 앱이 실제 손놀림처럼 받게(한 프레임에 여러 번).
        const int sub = 4;
        var start = Pointer;
        for (int i = 1; i <= steps * sub; i++)
        {
            SendMove(PointAlong(start, points, total * i / (steps * sub)));
            if (i % sub == 0)
            {
                ShotKit.Pump(12);
                Snap(FrameMs);
            }
            else Dispatcher.UIThread.RunJobs();
        }
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Point PointAlong(Point start, IReadOnlyList<Point> points, double distance)
    {
        var prev = start;
        foreach (var p in points)
        {
            double d = Distance(prev, p);
            if (distance <= d && d > 0) return prev + (p - prev) * (distance / d);
            distance -= d;
            prev = p;
        }
        return points[^1];
    }

    /// <summary>버튼을 누른다(지금 포인터 자리).</summary>
    public void Down(MouseButton button = MouseButton.Left)
    {
        var (top, local) = Hit(Pointer);
        top.MouseMove(local, Modifiers);
        _button = button;
        _pressTarget = top;
        top.MouseDown(local, button, Modifiers);
        ShotKit.Pump(20);
    }

    /// <summary>누른 버튼을 뗀다.</summary>
    public void Up()
    {
        if (_button is not { } button || _pressTarget is not { } top) return;
        var local = Pointer - Offset(top);
        _button = null;
        _pressTarget = null;
        _ringAfterUp = true;
        top.MouseUp(local, button, Modifiers);
        ShotKit.Pump(30);
    }

    /// <summary>
    /// <paramref name="to"/>로 옮겨 가 클릭한다(누른 모습 <paramref name="pressMs"/>, 뗀 뒤 <paramref name="afterMs"/>).
    /// 길게 누르기 타이머가 있는 단추(도구 막대 묶음 칸 등)는 <paramref name="pressMs"/> = 0: 누른 사이에 찍지 않고(찍는 동안 실제 시간이 흘러 길게 누르기가 됨) 뗀 뒤 첫 프레임에 고리를 그린다.
    /// </summary>
    public void Click(Point to, int moveMs = 450, MouseButton button = MouseButton.Left, int pressMs = 150, int afterMs = 300)
    {
        PressAndRelease(to, moveMs, button, pressMs);
        Hold(afterMs);
    }

    /// <summary>창이 뜨는 클릭처럼 <see cref="Up"/>이 오래 걸리는 곳에서: 누른 모습을 찍은 뒤 뗀다(뗀 뒤는 찍지 않음).</summary>
    public void PressAndRelease(Point to, int moveMs = 450, MouseButton button = MouseButton.Left, int pressMs = 150)
    {
        if (moveMs > 0) Move(to, moveMs);
        else Place(to);
        Down(button);
        if (pressMs > 0) Hold(pressMs);
        Up();
    }

    /// <summary><paramref name="from"/>에서 눌러 <paramref name="to"/>까지 끈다.</summary>
    public void Drag(Point from, Point to, int ms = 700, MouseButton button = MouseButton.Left)
    {
        Move(from);
        Down(button);
        Hold(120);
        Move(to, ms, ease: false);
        Hold(120);
        Up();
    }

    /// <summary>키를 누르고 뗀다(포커스가 있는 창에).</summary>
    public void Key(Key key, RawInputModifiers mods = RawInputModifiers.None, int afterMs = 250)
    {
        var top = FocusedTop();
        top.KeyPress(key, mods, PhysicalKey.None, null);
        top.KeyRelease(key, mods, PhysicalKey.None, null);
        Hold(afterMs);
    }

    /// <summary>키를 누른 채로 둔다(예: Shift를 누르고 있는 동안의 미리보기). <see cref="KeyUp"/>으로 뗀다.</summary>
    public void KeyDown(Key key)
    {
        FocusedTop().KeyPress(key, Modifiers, PhysicalKey.None, null);
        ShotKit.Pump(20);
    }

    public void KeyUp(Key key)
    {
        FocusedTop().KeyRelease(key, Modifiers, PhysicalKey.None, null);
        ShotKit.Pump(20);
    }

    /// <summary>글자를 한 자씩 친다.</summary>
    public void Type(string text, int perCharMs = 160)
    {
        var top = FocusedTop();
        foreach (var ch in text)
        {
            top.KeyTextInput(ch.ToString());
            Hold(perCharMs);
        }
    }

    private TopLevel FocusedTop() => Dialogs().LastOrDefault(w => w.IsActive) ?? (TopLevel?)Dialogs().LastOrDefault() ?? _main;

    /// <summary>바로 다음에 찍는 프레임(없으면 마지막 프레임)을 MP4 포스터(JPG)로 쓴다.</summary>
    public void MarkPoster() => _poster = _frames.Count;

    // ───────────── 찍기 ─────────────

    private void Snap(int ms)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = Render();
        _ringAfterUp = false;
        using var stream = new MemoryStream();
        frame.Save(stream);
        var bytes = stream.ToArray();
        if (_last is not null && _poster != _frames.Count && bytes.AsSpan().SequenceEqual(_last))
        {
            var (file, prev) = _frames[^1];
            _frames[^1] = (file, prev + ms);
            return;
        }
        _last = bytes;
        var path = Path.Combine(_dir, $"f{_frames.Count:D5}.png");
        File.WriteAllBytes(path, bytes);
        _frames.Add((path, ms));
    }

    /// <summary>메인 창 + 대화상자(제목 줄 포함)를 합쳐 찍을 영역만 자르고 커서를 얹은 한 프레임.</summary>
    private RenderTargetBitmap Render()
    {
        int bar = _titleBar ? ShotKit.TitleHeight : 0;
        var size = new PixelSize((int)_crop.Width, (int)_crop.Height + bar);
        var target = new RenderTargetBitmap(size, new Vector(96, 96));
        var captures = new List<Bitmap>();
        using (var ctx = target.CreateDrawingContext())
        {
            if (bar > 0)
            {
                // 창 전체 폭의 제목 줄을 그려 잘라 낸다(오른쪽만 자르면 OS 창처럼 단추만 보임).
                var title = _main.Title ?? "Chickxel";
                using (ctx.PushTransform(Matrix.CreateTranslation(-_crop.X, 0)))
                    ShotKit.DrawTitleBar(ctx, TitleFilter?.Invoke(title) ?? title, (int)_main.ClientSize.Width);
            }
            using (ctx.PushClip(new Rect(0, bar, size.Width, size.Height - bar)))
            using (ctx.PushTransform(Matrix.CreateTranslation(-_crop.X, bar - _crop.Y)))
            {
                var main = _kit.Capture(_main);
                captures.Add(main);
                ctx.DrawImage(main, new Rect(0, 0, main.PixelSize.Width, main.PixelSize.Height));
                foreach (var w in Dialogs())
                {
                    var origin = DialogOrigin(w);
                    var img = _kit.Capture(w);
                    captures.Add(img);
                    // 화면 없이 띄운 창에는 OS 그림자·테두리가 없다: 어두운 그림자 띠와 테두리 한 줄로 창이 떠 보이게.
                    var whole = new Rect(origin.X, origin.Y, img.PixelSize.Width, img.PixelSize.Height + ShotKit.TitleHeight);
                    for (int i = 1; i <= 8; i++)
                        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(40 - i * 4), 0, 0, 0)), 2), whole.Inflate(i * 2 - 1));
                    using (ctx.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
                        ShotKit.DrawTitleBar(ctx, w.Title ?? "", img.PixelSize.Width);
                    ctx.DrawImage(img, new Rect(origin.X, origin.Y + ShotKit.TitleHeight, img.PixelSize.Width, img.PixelSize.Height));
                    ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(70, 70, 78)), 1), whole.Inflate(0.5));
                }
                DrawCursor(ctx, Pointer, Cursor, _button is not null || _ringAfterUp);
            }
        }
        foreach (var c in captures) c.Dispose();
        return target;
    }

    private static readonly Color Ring = Color.FromArgb(220, 229, 57, 53);

    /// <summary>커서(끝이 <paramref name="p"/>)와 누르고 있으면 그 둘레 빨간 고리.</summary>
    private static void DrawCursor(DrawingContext ctx, Point p, ClipCursor kind, bool pressed)
    {
        if (kind == ClipCursor.None) return;
        if (pressed)
        {
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(50, 229, 57, 53)), new Pen(new SolidColorBrush(Ring), 2), p, 11, 11);
        }
        var black = new Pen(Brushes.Black, 1, lineJoin: PenLineJoin.Round);
        using var _ = ctx.PushTransform(Matrix.CreateTranslation(Math.Round(p.X) + 0.5, Math.Round(p.Y) + 0.5));
        switch (kind)
        {
            case ClipCursor.Arrow:
                ctx.DrawGeometry(Brushes.White, black, Geometry.Parse("M0,0 L0,16 L3.6,12.6 L6.2,18.6 L8.8,17.5 L6.3,11.6 L11.3,11.6 Z"));
                break;
            case ClipCursor.Hand:
                // 검지를 편 손(끝 = 손가락 끝).
                ctx.DrawGeometry(Brushes.White, black, Geometry.Parse(
                    "M-1.5,1.5 Q-1.5,-0.2 0,-0.2 Q1.5,-0.2 1.5,1.5 L1.5,7.2 Q1.6,6 3,6 Q4.5,6 4.5,7.5 L4.5,8.2 Q4.6,7 6,7 Q7.5,7 7.5,8.5 L7.5,9.2 " +
                    "Q7.6,8 9,8 Q10.5,8 10.5,9.5 L10.5,14 Q10.5,18.5 7,19.6 L2.5,19.6 Q0,19.6 -1.5,17.2 L-5,11.6 Q-5.8,10.2 -4.6,9.6 Q-3.5,9.1 -2.5,10.3 L-1.5,11.6 Z"));
                ctx.DrawLine(black, new Point(4.5, 8.2), new Point(4.5, 12));
                ctx.DrawLine(black, new Point(7.5, 9.2), new Point(7.5, 12.5));
                break;
            case ClipCursor.IBeam:
            {
                var white = new Pen(Brushes.White, 3, lineCap: PenLineCap.Round);
                var geo = Geometry.Parse("M-3,-9 L-1,-9 Q0,-9 0,-8 L0,8 Q0,9 -1,9 L-3,9 M3,-9 L1,-9 Q0,-9 0,-8 M3,9 L1,9 Q0,9 0,8");
                ctx.DrawGeometry(null, white, geo);
                ctx.DrawGeometry(null, new Pen(Brushes.Black, 1.2), geo);
                break;
            }
            case ClipCursor.SizeNS:
                ctx.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 1, lineJoin: PenLineJoin.Round),
                    Geometry.Parse("M0,-11 L5,-6 L2,-6 L2,6 L5,6 L0,11 L-5,6 L-2,6 L-2,-6 L-5,-6 Z"));
                break;
            case ClipCursor.Cross:
            {
                var geo = Geometry.Parse("M-10,0 L-2,0 M2,0 L10,0 M0,-10 L0,-2 M0,2 L0,10");
                ctx.DrawGeometry(null, new Pen(Brushes.White, 3), geo);
                ctx.DrawGeometry(null, new Pen(Brushes.Black, 1), geo);
                break;
            }
        }
    }

    // ───────────── 저장 ─────────────

    /// <summary>
    /// 찍은 프레임을 <paramref name="name"/>(저장소 루트 기준 경로, 확장자 없이 — 예: <c>docs/assets/feature-pivot</c>)으로 저장한다.
    /// <paramref name="webpLossless"/>가 아니면 WebP 품질 <paramref name="webpQuality"/>. MP4는 <paramref name="fps"/> 고정 프레임.
    /// </summary>
    public void Save(string name, ClipFormat formats, bool webpLossless = true, int webpQuality = 90, int fps = 24, int crf = 20)
    {
        if (_frames.Count == 0) throw new InvalidOperationException("no frames recorded");
        var basePath = Path.Combine(ShotKit.Root, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
        if (formats.HasFlag(ClipFormat.WebP)) SaveWebP(basePath + ".webp", webpLossless, webpQuality);
        if (formats.HasFlag(ClipFormat.Gif) || formats.HasFlag(ClipFormat.Mp4))
        {
            var ffmpeg = Chickxel.App.FfmpegTool.Locate((string?)null) ?? throw new InvalidOperationException("ffmpeg not found");
            var list = WriteConcatList();
            if (formats.HasFlag(ClipFormat.Gif))
            {
                Run(ffmpeg, "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", list,
                    "-filter_complex", "[0:v]split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=none:diff_mode=rectangle",
                    "-fps_mode", "passthrough", "-loop", "0", basePath + ".gif");
                Record(basePath + ".gif");
            }
            if (formats.HasFlag(ClipFormat.Mp4))
            {
                Run(ffmpeg, "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", list,
                    "-vf", $"fps={fps},format=yuv420p", "-c:v", "libx264", "-preset", "slow", "-crf", crf.ToString(CultureInfo.InvariantCulture),
                    // fps 필터가 마지막 프레임을 그 길이만큼 한 번 더 늘이므로 찍은 길이로 자른다.
                    "-t", (DurationMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture),
                    "-movflags", "+faststart", "-an", basePath + ".mp4");
                Record(basePath + ".mp4");
                var poster = _frames[_poster is >= 0 && _poster < _frames.Count ? _poster : _frames.Count - 1].File;
                Run(ffmpeg, "-y", "-v", "error", "-i", poster, "-q:v", "8", basePath + ".jpg");
                Record(basePath + ".jpg");
            }
        }
    }

    private void Record(string file)
    {
        var rel = Path.GetRelativePath(ShotKit.Root, file);
        _kit.Saved.Add($"{rel} ({_frames.Count} frames, {DurationMs / 1000.0:0.0}s, {new FileInfo(file).Length / 1024.0:0.0} KB)");
    }

    /// <summary>ffmpeg concat 목록(프레임마다 길이, 마지막 프레임은 길이가 쓰이도록 한 번 더).</summary>
    private string WriteConcatList()
    {
        var sb = new StringBuilder("ffconcat version 1.0\n");
        foreach (var (file, ms) in _frames)
        {
            sb.Append("file '").Append(file.Replace('\\', '/')).Append("'\n");
            sb.Append("duration ").Append((ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
        }
        // 마지막 프레임의 길이가 쓰이도록 한 번 더 넣는다(MP4는 찍은 길이로 자름).
        sb.Append("file '").Append(_frames[^1].File.Replace('\\', '/')).Append("'\n");
        var path = Path.Combine(_dir, "frames.txt");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    /// <summary>애니메이션 WebP: Pillow로 프레임마다 길이를 그대로(ffmpeg의 WebP는 길이를 반올림해서).</summary>
    private void SaveWebP(string file, bool lossless, int quality)
    {
        var python = FindPython() ?? throw new InvalidOperationException("python (with Pillow) not found on PATH — needed for animated WebP");
        var spec = Path.Combine(_dir, "webp.json");
        File.WriteAllText(spec, JsonSerializer.Serialize(new
        {
            files = _frames.Select(f => f.File).ToArray(),
            durations = _frames.Select(f => f.Ms).ToArray(),
            output = file,
            lossless,
            quality,
        }));
        var script = Path.Combine(_dir, "webp.py");
        File.WriteAllText(script, """
            import json, sys
            from PIL import Image
            spec = json.load(open(sys.argv[1], encoding="utf-8"))
            frames = [Image.open(f).convert("RGB") for f in spec["files"]]
            frames[0].save(spec["output"], save_all=True, append_images=frames[1:], duration=spec["durations"], loop=0,
                           lossless=spec["lossless"], quality=spec["quality"], method=6)
            """);
        Run(python, script, spec);
        Record(file);
    }

    private static string? FindPython()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "python.exe", "python3.exe" } : ["python3", "python"];
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var n in names)
            {
                var p = Path.Combine(dir.Trim(), n);
                // Windows 스토어 바로 가기(WindowsApps의 python.exe)는 설치 안내만 띄우므로 건너뛴다.
                if (File.Exists(p) && !p.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) return p;
            }
        return null;
    }

    private static void Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start " + exe);
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} failed ({p.ExitCode}): {err.Result}{output.Result}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
