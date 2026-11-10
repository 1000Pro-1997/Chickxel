using System.Buffers.Binary;
using System.Globalization;
using BitOperations = System.Numerics.BitOperations;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 색 고르기 팝업의 "그림에서 집기": 현재 프레임을 따로 창에 크게(정수 배율 최근접, 투명은 체커) 띄우고 누른 픽셀 색을 집는다.
/// 휠 = 커서 자리 기준 확대·축소, 끌기 = 이동(움직이지 않고 떼면 집기), 아래 줄에 올린 픽셀 색·HEX·좌표. 투명한 곳은 집지 않는다. Esc = 닫기.
/// </summary>
internal sealed class ImagePickWindow : DialogBase
{
    private readonly ImagePickView _view;
    private readonly SwatchView _swatch = new() { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _info = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private Color32? _picked;

    private ImagePickWindow(PixelImage image) : base(UiStrings.ColorPick_ImageTitle)
    {
        this.HelpAt("screen-color-picker", anchor: "Color pickers in other windows");
        CloseOnly();
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        MinWidth = 360;
        MinHeight = 300;
        // 처음 크기: 긴 변이 약 640 DIP가 되는 정수 배율(창은 띄울 때 작업 영역에 맞춰 줄어듦).
        double scale = Math.Max(1, Math.Floor(640.0 / Math.Max(image.Width, image.Height)));
        Width = Math.Clamp(image.Width * scale + 40, 480, 1100);
        Height = Math.Clamp(image.Height * scale + 120, 400, 900);

        _view = new ImagePickView(image);
        _view.Hovered += OnHovered;
        _view.Picked += color =>
        {
            _picked = color;
            DialogResult = DialogResult.OK;
        };
        var status = new DockPanel { Margin = new Thickness(0, 6, 0, 0), Children = { _swatch, _info } };
        DockPanel.SetDock(_swatch, Dock.Left);
        DockPanel.SetDock(status, Dock.Bottom);
        var frame = new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Child = _view,
        };
        // 본문(StackPanel)은 높이를 주지 않으므로 바깥 DockPanel의 채움 자리에 바로 넣는다.
        ((DockPanel)Content!).Children.Add(new DockPanel { Children = { status, frame } });
        AddFooterLeft(new TextBlock
        {
            Text = UiStrings.ColorPick_ImageHint,
            Foreground = AppTheme.DimTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        OnHovered(-1, -1);
    }

    /// <summary><paramref name="image"/>를 띄우고, 픽셀을 누르면 그 색으로 <paramref name="pick"/>을 부른다(닫기·Esc면 부르지 않음).</summary>
    public static void Open(Visual? owner, PixelImage image, Action<Color32> pick)
    {
        var window = new ImagePickWindow(image);
        if (window.ShowDialog(owner) == DialogResult.OK && window._picked is { } color) pick(color);
    }

    private void OnHovered(int x, int y)
    {
        var image = _view.Image;
        if (!image.Contains(x, y))
        {
            _swatch.Color = null;
            _info.Text = UiStrings.ColorPick_ImageNoPixel;
            _info.Foreground = AppTheme.DimTextBrush;
            return;
        }
        var color = image.Get(x, y);
        _swatch.Color = color;
        _info.Text = string.Create(CultureInfo.InvariantCulture,
            $"{(color.A == 0 ? UiStrings.S_ColorPickerModules_Swatches_3FF5B6C54B : color.ToHex())} · ({x}, {y})");
        _info.Foreground = AppTheme.TextBrush;
    }

    /// <summary>그림을 정수 배율로 맞춰 그리고 휠 확대·끌기 이동·누르기 집기를 받는 칸.</summary>
    private sealed class ImagePickView : Control
    {
        private const double MaxScale = 64, Pad = 8, DragDistance = 3;
        private readonly WriteableBitmap _bitmap;
        /// <summary>배율(0 = 칸에 맞춤 — 창 크기를 바꾸면 다시 맞춤).</summary>
        private double _scale;
        /// <summary>그림 가운데가 칸 가운데에서 벗어난 거리(DIP).</summary>
        private Vector _pan;
        private Avalonia.Point _press;
        private Vector _pressPan;
        private bool _pressed, _dragged;
        private int _hoverX = -1, _hoverY = -1;

        public ImagePickView(PixelImage image)
        {
            Image = image;
            _bitmap = ImageUtil.ToBitmap(image);
            Cursor = new Cursor(StandardCursorType.Cross);
            ClipToBounds = true;
        }

        public PixelImage Image { get; }

        /// <summary>올린 픽셀이 바뀜(그림 밖이면 -1, -1).</summary>
        public event Action<int, int>? Hovered;

        /// <summary>투명하지 않은 픽셀을 눌렀다(끌지 않고 떼었을 때).</summary>
        public event Action<Color32>? Picked;

        /// <summary>칸에 다 들어가는 배율(1 이상이면 정수, 그림이 칸보다 크면 줄임).</summary>
        private double FitScale
        {
            get
            {
                double fit = Math.Min((Bounds.Width - Pad * 2) / Image.Width, (Bounds.Height - Pad * 2) / Image.Height);
                return fit >= 1 ? Math.Floor(fit) : Math.Max(fit, 1e-3);
            }
        }

        private double Scale => _scale > 0 ? _scale : FitScale;

        private Rect ImageRect
        {
            get
            {
                double s = Scale, w = Image.Width * s, h = Image.Height * s;
                return new Rect(Math.Floor((Bounds.Width - w) / 2 + _pan.X), Math.Floor((Bounds.Height - h) / 2 + _pan.Y), w, h);
            }
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(AppTheme.InputBrush, new Rect(Bounds.Size));
            var rect = ImageRect;
            double s = Scale;
            AppTheme.DrawChecker(context, rect, Math.Max(2, Math.Min(8, (int)s * 2)));
            using (ImageUtil.Pixelated(context))
                context.DrawImage(_bitmap, new Rect(0, 0, Image.Width, Image.Height), rect);
            // 크게 볼 때 올린 픽셀 테두리(흰 선 + 검은 바깥선).
            if (s >= 4 && Image.Contains(_hoverX, _hoverY))
            {
                var cell = new Rect(rect.X + _hoverX * s, rect.Y + _hoverY * s, s, s);
                context.DrawRectangle(null, OutlineDark, cell.Inflate(0.5));
                context.DrawRectangle(null, OutlineLight, cell.Deflate(0.5));
            }
        }

        private static readonly IPen OutlineDark = AppTheme.Pen(Colors.Black);
        private static readonly IPen OutlineLight = AppTheme.Pen(Colors.White);

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (e.Delta.Y == 0) return;
            e.Handled = true;
            double old = Scale, fit = FitScale;
            double next = e.Delta.Y > 0
                ? old < 1 ? Math.Min(1, old * 2) : Math.Min(MaxScale, Math.Floor(old) + Math.Max(1, Math.Floor(old / 4)))
                : old <= 1 ? old / 2 : Math.Max(1, Math.Floor(old) - Math.Max(1, Math.Floor(old / 5)));
            // 맞춤보다 작아지면 맞춤으로 돌아간다(가운데로).
            if (next <= fit)
            {
                _scale = 0;
                _pan = default;
            }
            else
            {
                // 커서 아래 그림 자리가 그대로 있게 옮긴다.
                var p = e.GetPosition(this);
                var rect = ImageRect;
                var at = new Vector((p.X - rect.X) / old, (p.Y - rect.Y) / old);
                _scale = next;
                _pan = new Vector(p.X - at.X * next - (Bounds.Width - Image.Width * next) / 2, p.Y - at.Y * next - (Bounds.Height - Image.Height * next) / 2);
            }
            UpdateHover(e.GetPosition(this));
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            e.Handled = true;
            e.Pointer.Capture(this);
            _press = e.GetPosition(this);
            _pressPan = _pan;
            _pressed = true;
            _dragged = false;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            if (_pressed)
            {
                var d = p - _press;
                if (!_dragged && Math.Abs(d.X) + Math.Abs(d.Y) >= DragDistance) _dragged = true;
                if (_dragged)
                {
                    _pan = _pressPan + d;
                    InvalidateVisual();
                }
            }
            UpdateHover(p);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Pointer.Capture(null);
            bool click = _pressed && !_dragged && e.InitialPressMouseButton == MouseButton.Left;
            _pressed = _dragged = false;
            if (!click) return;
            var (x, y) = PixelAt(e.GetPosition(this));
            if (Image.Contains(x, y) && Image.Get(x, y) is { A: > 0 } color) Picked?.Invoke(color);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (!_pressed) SetHover(-1, -1);
        }

        private (int X, int Y) PixelAt(Avalonia.Point p)
        {
            var rect = ImageRect;
            double s = Scale;
            return ((int)Math.Floor((p.X - rect.X) / s), (int)Math.Floor((p.Y - rect.Y) / s));
        }

        private void UpdateHover(Avalonia.Point p)
        {
            var (x, y) = PixelAt(p);
            if (!Image.Contains(x, y)) (x, y) = (-1, -1);
            SetHover(x, y);
        }

        private void SetHover(int x, int y)
        {
            if (x == _hoverX && y == _hoverY) return;
            _hoverX = x;
            _hoverY = y;
            Hovered?.Invoke(x, y);
            InvalidateVisual();
        }
    }

    /// <summary>바뀌는 색 칸(없으면 빈 테두리).</summary>
    private sealed class SwatchView : Control
    {
        private Color32? _color;

        public Color32? Color
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            var r = new Rect(Bounds.Size);
            if (_color is { } c) ImageUtil.DrawSwatch(context, r, c);
            context.DrawRectangle(null, AppTheme.Pen(AppTheme.BorderColor), r.Deflate(0.5));
        }
    }
}

/// <summary>
/// 색 고르기 팝업의 "화면에서 집기": 누른 때의 모든 화면을 찍어 화면마다 테두리 없는 맨 위 전체 화면 창에 1:1로 띄우고,
/// 누른 곳의 색을 집는다(커서 옆 11×11 돋보기·HEX). Esc·우클릭·다른 앱으로 전환 = 그만.
/// 찍기: Windows GDI(BitBlt, 화면별 DPI는 Avalonia가 켠 화면별 DPI 인식으로 실제 픽셀), macOS CoreGraphics(화면 기록 권한 필요), Linux X11(XGetImage).
/// </summary>
internal static class ScreenColorPicker
{
    private static readonly List<Window> OpenWindows = [];
    private static bool _wasActive;

    /// <summary>이 환경에서 화면을 찍을 수 없는 까닭(단추를 끄고 풍선 도움말로 보임). 찍을 수 있으면 null.</summary>
    public static string? UnavailableReason { get; } = FindUnavailableReason();

    private static string? FindUnavailableReason()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) return null;
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD()) return UiStrings.ColorPick_ScreenUnsupported;
        // XWayland로는 다른 Wayland 앱 화면이 검게 찍힌다.
        if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 }
            || string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
            return UiStrings.ColorPick_ScreenWayland;
        if (Environment.GetEnvironmentVariable("DISPLAY") is not { Length: > 0 }) return UiStrings.ColorPick_ScreenNoDisplay;
        return null;
    }

    /// <summary>화면 집기를 시작한다. 색을 누르면 <paramref name="pick"/>(그만두면 부르지 않음).</summary>
    public static void Start(Visual? owner, Action<Color32> pick)
    {
        if (OpenWindows.Count > 0 || UnavailableReason is not null || Ui.Owner(owner) is not { } window) return;
        if (OperatingSystem.IsMacOS() && !MacCapture.HasPermission())
        {
            Ui.Info(window, UiStrings.ColorPick_ScreenMacPermission);
            return;
        }
        // 색 팝업이 화면에서 사라진 뒤에 찍는다.
        PopupMenus.CloseAll();
        DispatcherTimer.RunOnce(() => Capture(window, pick), TimeSpan.FromMilliseconds(150));
    }

    private static void Capture(Window owner, Action<Color32> pick)
    {
        List<(Screen Screen, PixelImage Image)> shots;
        try
        {
            shots = CaptureScreens(owner.Screens.All);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or ArgumentException)
        {
            Ui.Warn(owner, UiStrings.FormatColorPick_ScreenFailed(ex.Message));
            return;
        }
        if (shots.Count == 0)
        {
            Ui.Warn(owner, UiStrings.FormatColorPick_ScreenFailed("-"));
            return;
        }
        _wasActive = false;
        void Finish(Color32? color)
        {
            if (OpenWindows.Count == 0) return;
            var windows = OpenWindows.ToArray();
            OpenWindows.Clear();
            foreach (var w in windows) w.Close();
            if (owner.IsVisible) owner.Activate();
            if (color is { } c) pick(c);
        }
        foreach (var (screen, image) in shots)
        {
            var w = new ScreenPickWindow(screen, image, Finish);
            w.Activated += (_, _) => _wasActive = true;
            // 다른 앱으로 넘어가면(Alt+Tab 등) 맨 위 창이 남지 않게 그만둔다 — 화면끼리 옮겨 갈 때는 다른 집기 창이 활성.
            w.Deactivated += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (_wasActive && OpenWindows.Count > 0 && !OpenWindows.Any(x => x.IsActive)) Finish(null);
            }, DispatcherPriority.Background);
            OpenWindows.Add(w);
            w.Show();
        }
        OpenWindows[^1].Activate();
    }

    /// <summary>화면마다 찍은 그림(실제 픽셀, 불투명). 찍지 못한 화면은 빠진다.</summary>
    private static List<(Screen Screen, PixelImage Image)> CaptureScreens(IReadOnlyList<Screen> screens)
    {
        if (OperatingSystem.IsWindows())
            return [.. screens.Select(s => (s, WindowsCapture.Capture(s.Bounds)))];
        if (OperatingSystem.IsMacOS()) return MacCapture.Capture(screens);
        return X11Capture.Capture(screens);
    }

    /// <summary>찍은 픽셀의 알파를 255로(화면은 불투명 — 빈 알파 바이트를 채움).</summary>
    private static void MakeOpaque(Color32[] pixels)
    {
        foreach (ref uint p in MemoryMarshal.Cast<Color32, uint>(pixels.AsSpan())) p |= 0xFF000000u;
    }

    /// <summary>화면 하나를 덮는 테두리 없는 맨 위 창.</summary>
    private sealed class ScreenPickWindow : Window
    {
        public ScreenPickWindow(Screen screen, PixelImage shot, Action<Color32?> finish)
        {
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = screen.Bounds.Position;
            Width = screen.Bounds.Width / screen.Scaling;
            Height = screen.Bounds.Height / screen.Scaling;
            // Windows는 전체 화면 상태가 그 모니터 크기에 정확히 맞춘다(macOS는 새 Space로 넘어가므로 크기만).
            if (OperatingSystem.IsWindows()) WindowState = WindowState.FullScreen;
            Background = Brushes.Black;
            Content = new ScreenPickView(shot, finish);
            KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                finish(null);
            };
        }
    }

    /// <summary>찍은 화면을 창 가득(1:1) 그리고 커서 옆에 돋보기·HEX, 위에 안내를 그린다.</summary>
    private sealed class ScreenPickView : Control
    {
        private const int Cells = 11;
        private const double Cell = 10, Gap = 20, LabelHeight = 24;
        private static readonly IPen DarkPen = AppTheme.Pen(Colors.Black, 2);
        private static readonly IPen LightPen = AppTheme.Pen(Colors.White);
        private static readonly IPen BorderPen = AppTheme.Pen(AppTheme.BorderColor);

        private readonly PixelImage _shot;
        private readonly Action<Color32?> _finish;
        private readonly WriteableBitmap _bitmap;
        private readonly FormattedText _hint;
        private Avalonia.Point _pointer;
        private int _x = -1, _y = -1;
        private FormattedText? _hex;

        public ScreenPickView(PixelImage shot, Action<Color32?> finish)
        {
            _shot = shot;
            _finish = finish;
            _bitmap = ImageUtil.ToBitmap(shot);
            _hint = Text(UiStrings.ColorPick_ScreenHint);
            Cursor = new Cursor(StandardCursorType.Cross);
        }

        private static FormattedText Text(string text)
            => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AppTheme.Typeface, AppTheme.FontSize, AppTheme.TextBrush);

        public override void Render(DrawingContext context)
        {
            var area = new Rect(Bounds.Size);
            using (ImageUtil.Pixelated(context))
                context.DrawImage(_bitmap, new Rect(0, 0, _shot.Width, _shot.Height), area);
            // 안내: 위 가운데(커서가 그 자리에 오면 아래로 비킴).
            var hint = new Rect(Math.Floor((area.Width - _hint.Width) / 2) - 12, 16, _hint.Width + 24, _hint.Height + 12);
            if (hint.Inflate(Gap).Contains(_pointer)) hint = hint.WithY(area.Height - 16 - hint.Height);
            context.FillRectangle(AppTheme.PanelBrush, hint);
            context.DrawRectangle(null, BorderPen, hint.Deflate(0.5));
            context.DrawText(_hint, new Avalonia.Point(hint.X + 12, hint.Y + 6));
            if (_shot.Contains(_x, _y)) DrawLoupe(context, area);
        }

        private void DrawLoupe(DrawingContext context, Rect area)
        {
            const double size = Cells * Cell;
            var box = new Rect(_pointer.X + Gap, _pointer.Y + Gap, size, size + LabelHeight);
            if (box.Right > area.Right) box = box.WithX(_pointer.X - Gap - size);
            if (box.Bottom > area.Bottom) box = box.WithY(_pointer.Y - Gap - box.Height);
            var grid = new Rect(box.X, box.Y, size, size);
            context.FillRectangle(Brushes.Black, grid);
            // 화면 가장자리에서는 그림 밖 칸을 검게 둔다.
            int sx = _x - Cells / 2, sy = _y - Cells / 2;
            int x0 = Math.Max(0, sx), y0 = Math.Max(0, sy), x1 = Math.Min(_shot.Width, sx + Cells), y1 = Math.Min(_shot.Height, sy + Cells);
            using (ImageUtil.Pixelated(context))
                context.DrawImage(_bitmap, new Rect(x0, y0, x1 - x0, y1 - y0),
                    new Rect(grid.X + (x0 - sx) * Cell, grid.Y + (y0 - sy) * Cell, (x1 - x0) * Cell, (y1 - y0) * Cell));
            var center = new Rect(grid.X + Cells / 2 * Cell, grid.Y + Cells / 2 * Cell, Cell, Cell);
            context.DrawRectangle(null, DarkPen, center.Inflate(1));
            context.DrawRectangle(null, LightPen, center.Deflate(0.5));
            var label = new Rect(box.X, grid.Bottom, size, LabelHeight);
            context.FillRectangle(AppTheme.PanelBrush, label);
            ImageUtil.DrawSwatch(context, new Rect(label.X + 5, label.Y + 5, 14, 14), _shot.Get(_x, _y));
            if (_hex is not null) context.DrawText(_hex, new Avalonia.Point(label.X + 25, label.Y + (LabelHeight - _hex.Height) / 2));
            context.DrawRectangle(null, BorderPen, box.Deflate(-0.5));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            // 여러 화면이면 커서가 있는 화면의 창이 키(Esc)를 받게 한다.
            if (TopLevel.GetTopLevel(this) is Window { IsActive: false } window) window.Activate();
            _pointer = e.GetPosition(this);
            var (x, y) = PixelAt(_pointer);
            if (x != _x || y != _y)
            {
                _x = x;
                _y = y;
                _hex = _shot.Contains(x, y) ? Text(_shot.Get(x, y).ToHex()) : null;
            }
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _x = _y = -1;
            _hex = null;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            e.Handled = true;
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
            {
                _finish(null);
                return;
            }
            var (x, y) = PixelAt(point.Position);
            if (_shot.Contains(x, y)) _finish(_shot.Get(x, y));
        }

        /// <summary>창 좌표 → 찍은 그림 픽셀(창 크기에 비례 — 그림이 창 가득이므로 OS마다 좌표 단위가 달라도 맞음).</summary>
        private (int X, int Y) PixelAt(Avalonia.Point p)
            => Bounds.Width <= 0 || Bounds.Height <= 0 ? (-1, -1)
                : ((int)Math.Floor(p.X / Bounds.Width * _shot.Width), (int)Math.Floor(p.Y / Bounds.Height * _shot.Height));
    }

    // ───────────── Windows: GDI ─────────────

    private static class WindowsCapture
    {
        private const uint SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, SizeImage;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ClrUsed, ClrImportant;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool BitBlt(IntPtr dst, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, uint rop);

        [DllImport("gdi32.dll")]
        private static extern bool GdiFlush();

        /// <summary>가상 화면 좌표(실제 픽셀) <paramref name="r"/>을 찍는다.</summary>
        public static unsafe PixelImage Capture(PixelRect r)
        {
            var screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new InvalidOperationException("GetDC failed");
            IntPtr memory = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                memory = CreateCompatibleDC(screen);
                // 위에서 아래로 쌓는 32비트 BGRA(높이 음수).
                var header = new BitmapInfoHeader { Size = (uint)sizeof(BitmapInfoHeader), Width = r.Width, Height = -r.Height, Planes = 1, BitCount = 32 };
                dib = CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
                if (memory == IntPtr.Zero || dib == IntPtr.Zero || bits == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
                old = SelectObject(memory, dib);
                if (!BitBlt(memory, 0, 0, r.Width, r.Height, screen, r.X, r.Y, SrcCopy | CaptureBlt))
                    throw new InvalidOperationException("BitBlt failed (" + Marshal.GetLastPInvokeError() + ")");
                GdiFlush();
                var pixels = new Color32[r.Width * r.Height];
                new ReadOnlySpan<Color32>((void*)bits, pixels.Length).CopyTo(pixels);
                MakeOpaque(pixels);
                return new PixelImage(r.Width, r.Height, pixels);
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(memory, old);
                if (dib != IntPtr.Zero) DeleteObject(dib);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }

    // ───────────── macOS: CoreGraphics ─────────────

    private static class MacCapture
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [StructLayout(LayoutKind.Sequential)]
        private struct CGRect
        {
            public double X, Y, Width, Height;
        }

        [DllImport(CoreGraphics)]
        private static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] displays, out uint count);

        [DllImport(CoreGraphics)]
        private static extern CGRect CGDisplayBounds(uint display);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGDisplayCreateImage(uint display);

        [DllImport(CoreGraphics)]
        private static extern nuint CGImageGetWidth(IntPtr image);

        [DllImport(CoreGraphics)]
        private static extern nuint CGImageGetHeight(IntPtr image);

        [DllImport(CoreGraphics)]
        private static extern nuint CGImageGetBytesPerRow(IntPtr image);

        [DllImport(CoreGraphics)]
        private static extern nuint CGImageGetBitsPerPixel(IntPtr image);

        [DllImport(CoreGraphics)]
        private static extern uint CGImageGetBitmapInfo(IntPtr image);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGImageGetDataProvider(IntPtr image);

        [DllImport(CoreGraphics)]
        private static extern IntPtr CGDataProviderCopyData(IntPtr provider);

        [DllImport(CoreGraphics)]
        private static extern void CGImageRelease(IntPtr image);

        [DllImport(CoreGraphics)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool CGPreflightScreenCaptureAccess();

        [DllImport(CoreGraphics)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool CGRequestScreenCaptureAccess();

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFDataGetBytePtr(IntPtr data);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr obj);

        /// <summary>화면 기록 권한이 있는가(없으면 처음 한 번 시스템 묻기 창을 띄운다 — 허용해도 앱을 다시 켜야 적용). macOS 10.15 전은 권한이 없다.</summary>
        public static bool HasPermission()
        {
            try
            {
                if (CGPreflightScreenCaptureAccess()) return true;
                CGRequestScreenCaptureAccess();
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return true;
            }
        }

        public static List<(Screen Screen, PixelImage Image)> Capture(IReadOnlyList<Screen> screens)
        {
            var ids = new uint[32];
            if (CGGetActiveDisplayList((uint)ids.Length, ids, out uint count) != 0 || count == 0)
                throw new InvalidOperationException("CGGetActiveDisplayList failed");
            var displays = ids.Take((int)count).Select(id => (Id: id, Bounds: CGDisplayBounds(id))).ToList();
            var result = new List<(Screen, PixelImage)>();
            foreach (var screen in screens)
            {
                if (displays.Count == 0) break;
                // Avalonia 화면과 가장 가까운 CoreGraphics 화면(포인트 — 배율을 곱한 것과 그대로 둘 다 견줌).
                var best = displays.MinBy(d => Distance(d.Bounds, screen));
                displays.Remove(best);
                if (CaptureDisplay(best.Id) is { } image) result.Add((screen, image));
            }
            return result;
        }

        private static double Distance(CGRect cg, Screen screen)
        {
            var b = screen.Bounds;
            double Diff(double s) => Math.Abs(cg.X * s - b.X) + Math.Abs(cg.Y * s - b.Y) + Math.Abs(cg.Width * s - b.Width) + Math.Abs(cg.Height * s - b.Height);
            return Math.Min(Diff(screen.Scaling), Diff(1));
        }

        private static unsafe PixelImage? CaptureDisplay(uint display)
        {
            var image = CGDisplayCreateImage(display);
            if (image == IntPtr.Zero) return null;
            try
            {
                int w = (int)CGImageGetWidth(image), h = (int)CGImageGetHeight(image), stride = (int)CGImageGetBytesPerRow(image);
                if (w <= 0 || h <= 0 || CGImageGetBitsPerPixel(image) != 32) return null;
                uint info = CGImageGetBitmapInfo(image);
                // 바이트 차례: 32비트 리틀 엔디언(0x2000)이면 A가 앞(First)일 때 B G R A, 뒤(Last)일 때 A B G R; 아니면 A R G B / R G B A.
                bool little = (info & 0x7000) == 0x2000, first = (info & 0x1F) is 2 or 4 or 6;
                var (ro, go, bo) = little ? first ? (2, 1, 0) : (3, 2, 1) : first ? (1, 2, 3) : (0, 1, 2);
                var data = CGDataProviderCopyData(CGImageGetDataProvider(image));
                if (data == IntPtr.Zero) return null;
                try
                {
                    var src = (byte*)CFDataGetBytePtr(data);
                    var pixels = new Color32[w * h];
                    for (int y = 0; y < h; y++)
                    {
                        byte* row = src + (long)y * stride;
                        for (int x = 0; x < w; x++, row += 4) pixels[y * w + x] = new Color32(row[ro], row[go], row[bo], 255);
                    }
                    return new PixelImage(w, h, pixels);
                }
                finally
                {
                    CFRelease(data);
                }
            }
            finally
            {
                CGImageRelease(image);
            }
        }
    }

    // ───────────── Linux: X11 ─────────────

    private static class X11Capture
    {
        private const string X11 = "libX11.so.6";
        private const int ZPixmap = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct XImage
        {
            public int Width, Height, XOffset, Format;
            public IntPtr Data;
            public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
            public nuint RedMask, GreenMask, BlueMask;
        }

        [DllImport(X11)]
        private static extern IntPtr XOpenDisplay(IntPtr name);

        [DllImport(X11)]
        private static extern int XCloseDisplay(IntPtr display);

        [DllImport(X11)]
        private static extern IntPtr XDefaultRootWindow(IntPtr display);

        [DllImport(X11)]
        private static extern int XDefaultScreen(IntPtr display);

        [DllImport(X11)]
        private static extern int XDisplayWidth(IntPtr display, int screen);

        [DllImport(X11)]
        private static extern int XDisplayHeight(IntPtr display, int screen);

        [DllImport(X11)]
        private static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, nuint planeMask, int format);

        [DllImport(X11)]
        private static extern int XDestroyImage(IntPtr image);

        public static List<(Screen Screen, PixelImage Image)> Capture(IReadOnlyList<Screen> screens)
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) throw new InvalidOperationException("XOpenDisplay failed");
            try
            {
                var root = XDefaultRootWindow(display);
                int screen = XDefaultScreen(display), rootW = XDisplayWidth(display, screen), rootH = XDisplayHeight(display, screen);
                var result = new List<(Screen, PixelImage)>();
                foreach (var s in screens)
                {
                    // 루트 창 밖으로 나간 부분은 잘라 찍는다.
                    int x0 = Math.Max(0, s.Bounds.X), y0 = Math.Max(0, s.Bounds.Y);
                    int x1 = Math.Min(rootW, s.Bounds.Right), y1 = Math.Min(rootH, s.Bounds.Bottom);
                    if (x1 <= x0 || y1 <= y0) continue;
                    if (CaptureRect(display, root, x0, y0, x1 - x0, y1 - y0) is { } image) result.Add((s, image));
                }
                return result;
            }
            finally
            {
                XCloseDisplay(display);
            }
        }

        private static unsafe PixelImage? CaptureRect(IntPtr display, IntPtr root, int x, int y, int w, int h)
        {
            var ptr = XGetImage(display, root, x, y, (uint)w, (uint)h, nuint.MaxValue, ZPixmap);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                var img = *(XImage*)ptr;
                if (img.BitsPerPixel != 32 || img.Data == IntPtr.Zero) return null;
                uint red = (uint)img.RedMask, green = (uint)img.GreenMask, blue = (uint)img.BlueMask;
                int rs = BitOperations.TrailingZeroCount(red), gs = BitOperations.TrailingZeroCount(green), bs = BitOperations.TrailingZeroCount(blue);
                bool msb = img.ByteOrder != 0;
                var pixels = new Color32[w * h];
                for (int row = 0; row < h; row++)
                {
                    var line = new ReadOnlySpan<byte>((byte*)img.Data + (long)row * img.BytesPerLine, w * 4);
                    for (int col = 0; col < w; col++)
                    {
                        var bytes = line.Slice(col * 4, 4);
                        uint v = msb ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                        pixels[row * w + col] = new Color32((byte)((v & red) >> rs), (byte)((v & green) >> gs), (byte)((v & blue) >> bs), 255);
                    }
                }
                return new PixelImage(w, h, pixels);
            }
            finally
            {
                XDestroyImage(ptr);
            }
        }
    }
}
