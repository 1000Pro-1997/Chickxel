using Avalonia.Media.Immutable;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>색 고르기 방식(색 패널 "색" 머리글 오른쪽 탭).</summary>
public enum ColorPickerMode
{
    /// <summary>채도·명도 사각형 + 세로 색상 막대.</summary>
    Square,
    /// <summary>벌집: 육각형 칸(가운데 흰색 → 바깥으로 갈수록 진하고 어두운 색, 아래에 회색 줄).</summary>
    Honeycomb,
    /// <summary>색상환: 색상 고리 + 안쪽 채도·명도 사각형.</summary>
    Wheel,
    /// <summary>R·G·B·H·S·V 막대(막대마다 지금 색에서 그 값만 바꾼 그라데이션).</summary>
    Sliders,
}

/// <summary>색 고르기 컨트롤이 함께 쓰는 붓·펜.</summary>
internal static class PickerPaint
{
    /// <summary>왼쪽 흰색 → 오른쪽 투명(채도).</summary>
    public static readonly IBrush WhiteFade = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Colors.White), new ImmutableGradientStop(1, Color.FromArgb(0, 255, 255, 255))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(1, 0, RelativeUnit.Relative));

    /// <summary>위 투명 → 아래 검정(명도).</summary>
    public static readonly IBrush BlackFade = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.FromArgb(0, 0, 0, 0)), new ImmutableGradientStop(1, Colors.Black)],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    public static readonly IPen BlackRing = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Black), 1.5);
    public static readonly IPen WhiteRing = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White), 1.5);
    public static readonly IPen WhiteMarker = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White), 2);
    public static readonly IPen BlackOutline = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(20, 20, 24)), 1);

    /// <summary>채도·명도 사각형(지금 색상의 순색 위에 흰색·검정 덮기)과 표시 고리.</summary>
    public static void DrawSvSquare(DrawingContext g, Rect rect, float hue, float s, float v)
    {
        g.FillRectangle(AppTheme.Brush(Color32.FromHsv(hue, 1, 1).ToColor()), rect);
        g.FillRectangle(WhiteFade, rect);
        g.FillRectangle(BlackFade, rect);
        var p = new Avalonia.Point(rect.X + s * (rect.Width - 1), rect.Y + (1 - v) * (rect.Height - 1));
        DrawRing(g, p);
    }

    /// <summary>컨트롤 전체를 투명하게 칠한다(그린 곳만 눌리는 히트 테스트에서 빈 곳·틈도 눌리게).</summary>
    public static void FillHitArea(DrawingContext g, Rect bounds) => g.FillRectangle(Brushes.Transparent, new Rect(bounds.Size));

    /// <summary>검정·흰 겹고리 표시.</summary>
    public static void DrawRing(DrawingContext g, Avalonia.Point p)
    {
        g.DrawEllipse(null, BlackRing, p, 5, 5);
        g.DrawEllipse(null, WhiteRing, p, 4, 4);
    }

    /// <summary>점 끌기(왼쪽 단추 누르면 잡고, 놓거나 잃으면 끝)를 하는 컨트롤 바탕.</summary>
    public abstract class DragControl : Control
    {
        public bool Dragging { get; private set; }

        protected DragControl()
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            ClipToBounds = true;
        }

        /// <summary>누른 점에서 끌기를 시작할지(시작하면 true, <see cref="Drag"/>가 바로 불린다).</summary>
        protected abstract bool Begin(Avalonia.Point p);

        protected abstract void Drag(Avalonia.Point p);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            if (!pt.Properties.IsLeftButtonPressed || !Begin(pt.Position)) return;
            Dragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            Drag(pt.Position);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (Dragging) Drag(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            Dragging = false;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            Dragging = false;
        }
    }
}

/// <summary>
/// 벌집 색 고르기(그림판의 육각형 색 고르기처럼): 반지름 6고리의 육각형 칸 127개 — 가운데 흰색, 고리마다 채도가 오르고 바깥 두 고리는 어두워지며
/// 색상은 위(빨강)부터 시계 방향. 아래에 흰색 → 검정 회색 13칸. 칸을 누르면(끌면 지나는 칸마다) 그 색, 마우스를 올린 칸은 흰 테두리,
/// 지금 색과 같은 칸은 굵은 테두리.
/// </summary>
internal sealed class HoneycombPicker : PickerPaint.DragControl
{
    private const int Rings = 6;
    /// <summary>육각형 외접원 반지름(꼭짓점이 위아래인 육각형).</summary>
    private const double Hex = 7;
    private static readonly double HexW = Math.Sqrt(3) * Hex;
    private static readonly float[] RingS = [0f, 0.2f, 0.4f, 0.65f, 0.9f, 1f, 1f];
    private static readonly float[] RingV = [1f, 1f, 1f, 1f, 1f, 0.8f, 0.6f];
    private static readonly (Avalonia.Point Center, Color32 Color, IBrush Brush)[] Cells = BuildCells();
    private static readonly double ContentHeight = Cells.Max(c => c.Center.Y) + Hex + 1;

    private int _hover = -1;
    private Color32 _current;

    public HoneycombPicker()
    {
        Height = ContentHeight + 2;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public event EventHandler<Color32>? ColorPicked;

    /// <summary>지금 색(같은 RGB 칸에 굵은 테두리).</summary>
    public Color32 Current
    {
        get => _current;
        set { if (_current == value) return; _current = value; InvalidateVisual(); }
    }

    /// <summary>칸 중심(왼쪽 위 = 큰 육각형 가운데 위, 화면에서는 가로 가운데로 옮김)·색.</summary>
    private static (Avalonia.Point, Color32, IBrush)[] BuildCells()
    {
        var cells = new List<(Avalonia.Point, Color32, IBrush)>();
        double rowStep = 1.5 * Hex;
        double cy = Rings * rowStep + Hex;
        for (int r = -Rings; r <= Rings; r++)
            for (int q = -Rings; q <= Rings; q++)
            {
                int ring = Math.Max(Math.Abs(q), Math.Max(Math.Abs(r), Math.Abs(q + r)));
                if (ring > Rings) continue;
                double x = HexW * (q + r / 2.0), y = rowStep * r;
                float hue = (float)((Math.Atan2(x, -y) * 180 / Math.PI + 360) % 360);
                var c = ring == 0 ? Color32.White : Color32.FromHsv(hue, RingS[ring], RingV[ring]);
                cells.Add((new Avalonia.Point(x, cy + y), c, AppTheme.Brush(c.ToColor())));
            }
        // 회색 줄: 흰색 → 검정 13칸.
        double gy = cy + Rings * rowStep + Hex + 5 + Hex;
        for (int i = 0; i <= 12; i++)
        {
            byte l = (byte)Math.Round(255 * (1 - i / 12.0));
            var c = new Color32(l, l, l);
            cells.Add((new Avalonia.Point(HexW * (i - 6), gy), c, AppTheme.Brush(c.ToColor())));
        }
        return [.. cells];
    }

    private double OffsetX => Math.Floor(Bounds.Width / 2);

    private static StreamGeometry HexGeometry(Avalonia.Point c, double r)
    {
        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        for (int i = 0; i < 6; i++)
        {
            double a = Math.PI / 180 * (60 * i - 90);
            var p = new Avalonia.Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
            if (i == 0) ctx.BeginFigure(p, true);
            else ctx.LineTo(p);
        }
        ctx.EndFigure(true);
        return geo;
    }

    public override void Render(DrawingContext g)
    {
        PickerPaint.FillHitArea(g, Bounds);
        double ox = OffsetX;
        int current = -1;
        for (int i = 0; i < Cells.Length; i++)
        {
            var (center, color, brush) = Cells[i];
            g.DrawGeometry(brush, null, HexGeometry(center + new Avalonia.Point(ox, 0), Hex - 0.6));
            if (current < 0 && color == _current.WithAlpha(255)) current = i;
        }
        if (_hover >= 0 && _hover != current)
            g.DrawGeometry(null, PickerPaint.WhiteRing, HexGeometry(Cells[_hover].Center + new Avalonia.Point(ox, 0), Hex - 1));
        if (current >= 0)
        {
            var c = Cells[current].Center + new Avalonia.Point(ox, 0);
            g.DrawGeometry(null, PickerPaint.BlackOutline, HexGeometry(c, Hex + 0.5));
            g.DrawGeometry(null, PickerPaint.WhiteMarker, HexGeometry(c, Hex - 1));
        }
    }

    private int IndexAt(Avalonia.Point p)
    {
        var local = p - new Avalonia.Point(OffsetX, 0);
        int best = -1;
        double bestD = Hex * Hex;
        for (int i = 0; i < Cells.Length; i++)
        {
            var d = Cells[i].Center - local;
            double d2 = d.X * d.X + d.Y * d.Y;
            if (d2 < bestD) { bestD = d2; best = i; }
        }
        return best;
    }

    protected override bool Begin(Avalonia.Point p) => IndexAt(p) >= 0;

    protected override void Drag(Avalonia.Point p)
    {
        int i = IndexAt(p);
        if (i < 0 || Cells[i].Color == _current.WithAlpha(255)) return;
        ColorPicked?.Invoke(this, Cells[i].Color);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int i = IndexAt(e.GetPosition(this));
        if (i == _hover) return;
        _hover = i;
        ToolTip.SetTip(this, i < 0 ? null : Cells[i].Color.ToHex());
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }
}

/// <summary>색상환: 바깥 고리 = 색상(위 빨강부터 시계 방향), 안쪽 사각형 = 채도(가로)·명도(세로). 고리나 사각형을 눌러 끈다.</summary>
internal sealed class WheelPicker : PickerPaint.DragControl
{
    private const double RingWidth = 14;
    private const int Segments = 180;
    private static readonly IBrush[] SegmentBrushes =
        [.. Enumerable.Range(0, Segments).Select(i => AppTheme.Brush(Color32.FromHsv((i + 0.5f) * 360f / Segments, 1, 1).ToColor()))];

    private float _h, _s, _v;
    private bool _ringDrag;
    private (Avalonia.Point Center, double Radius, StreamGeometry[] Wedges)? _ring;

    public WheelPicker()
    {
        Height = 150;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public event EventHandler? ValueChanged;
    public float Hue => _h;
    public float S => _s;
    public float V => _v;

    public void SetHsv(float h, float s, float v)
    {
        if (_h == h && _s == s && _v == v) return;
        (_h, _s, _v) = (h, s, v);
        InvalidateVisual();
    }

    private Avalonia.Point Center => new(Math.Floor(Bounds.Width / 2), Math.Floor(Bounds.Height / 2));
    private double Outer => Math.Max(RingWidth + 8, Math.Floor(Math.Min(Bounds.Width, Bounds.Height) / 2) - 2);
    private double Inner => Outer - RingWidth;

    private Rect SquareRect
    {
        get
        {
            double half = Math.Floor((Inner - 4) / Math.Sqrt(2));
            var c = Center;
            return new Rect(c.X - half, c.Y - half, 2 * half, 2 * half);
        }
    }

    /// <summary>색상 고리 조각(크기가 바뀔 때만 다시 만듦).</summary>
    private StreamGeometry[] Wedges()
    {
        var c = Center;
        double ro = Outer;
        if (_ring is { } cached && cached.Center == c && cached.Radius == ro) return cached.Wedges;
        double ri = Inner;
        var wedges = new StreamGeometry[Segments];
        for (int i = 0; i < Segments; i++)
        {
            // 이웃 조각과 틈이 보이지 않게 조금 겹친다.
            double a0 = (i * 360.0 / Segments - 0.3 - 90) * Math.PI / 180, a1 = ((i + 1) * 360.0 / Segments + 0.3 - 90) * Math.PI / 180;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Avalonia.Point(c.X + ri * Math.Cos(a0), c.Y + ri * Math.Sin(a0)), true);
                ctx.LineTo(new Avalonia.Point(c.X + ro * Math.Cos(a0), c.Y + ro * Math.Sin(a0)));
                ctx.LineTo(new Avalonia.Point(c.X + ro * Math.Cos(a1), c.Y + ro * Math.Sin(a1)));
                ctx.LineTo(new Avalonia.Point(c.X + ri * Math.Cos(a1), c.Y + ri * Math.Sin(a1)));
                ctx.EndFigure(true);
            }
            wedges[i] = geo;
        }
        _ring = (c, ro, wedges);
        return wedges;
    }

    public override void Render(DrawingContext g)
    {
        PickerPaint.FillHitArea(g, Bounds);
        var wedges = Wedges();
        for (int i = 0; i < Segments; i++) g.DrawGeometry(SegmentBrushes[i], null, wedges[i]);
        var c = Center;
        double mid = Outer - RingWidth / 2, a = (_h - 90) * Math.PI / 180;
        PickerPaint.DrawRing(g, new Avalonia.Point(c.X + mid * Math.Cos(a), c.Y + mid * Math.Sin(a)));
        PickerPaint.DrawSvSquare(g, SquareRect, _h, _s, _v);
    }

    protected override bool Begin(Avalonia.Point p)
    {
        var d = p - Center;
        double r = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        if (r >= Inner - 2 && r <= Outer + 2) _ringDrag = true;
        else if (SquareRect.Inflate(4).Contains(p)) _ringDrag = false;
        else return false;
        return true;
    }

    protected override void Drag(Avalonia.Point p)
    {
        if (_ringDrag)
        {
            var d = p - Center;
            _h = (float)((Math.Atan2(d.X, -d.Y) * 180 / Math.PI + 360) % 360);
        }
        else
        {
            var sq = SquareRect;
            _s = (float)Math.Clamp((p.X - sq.X) / Math.Max(1, sq.Width - 1), 0, 1);
            _v = 1 - (float)Math.Clamp((p.Y - sq.Y) / Math.Max(1, sq.Height - 1), 0, 1);
        }
        InvalidateVisual();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// R·G·B·H·S·V 막대 여섯 줄. 막대마다 지금 색에서 그 값만 0 → 최대로 바꾼 그라데이션을 보이고, 눌러 끌면 그 값을 바꾼다
/// (R·G·B는 <see cref="RgbPicked"/>, H·S·V는 <see cref="HsvPicked"/>).
/// </summary>
internal sealed class SliderPicker : PickerPaint.DragControl
{
    private const double RowH = 18, BarH = 12, LabelW = 16, Pad = 4;
    private static readonly string[] Labels = ["R", "G", "B", "H", "S", "V"];

    private Color32 _color = Color32.Black;
    private float _h, _s, _v;
    private int _row = -1;

    public SliderPicker()
    {
        Height = RowH * Labels.Length + 2;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public event EventHandler<Color32>? RgbPicked;
    public event EventHandler<(float H, float S, float V)>? HsvPicked;

    public void SetColor(Color32 color, float h, float s, float v)
    {
        if (_color == color && _h == h && _s == s && _v == v) return;
        (_color, _h, _s, _v) = (color, h, s, v);
        InvalidateVisual();
    }

    private Rect BarRect(int row) => new(LabelW + Pad, 1 + row * RowH + (RowH - BarH) / 2, Math.Max(1, Bounds.Width - LabelW - 2 * Pad), BarH);

    private static IBrush Horizontal(params Color32[] stops) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = [.. stops.Select((c, i) => new Avalonia.Media.GradientStop(c.ToColor(), stops.Length == 1 ? 0 : i / (double)(stops.Length - 1)))],
    };

    private IBrush BarBrush(int row) => row switch
    {
        0 => Horizontal(new Color32(0, _color.G, _color.B), new Color32(255, _color.G, _color.B)),
        1 => Horizontal(new Color32(_color.R, 0, _color.B), new Color32(_color.R, 255, _color.B)),
        2 => Horizontal(new Color32(_color.R, _color.G, 0), new Color32(_color.R, _color.G, 255)),
        3 => Horizontal([.. Enumerable.Range(0, 7).Select(i => Color32.FromHsv(i * 60 % 360, _s, _v))]),
        4 => Horizontal(Color32.FromHsv(_h, 0, _v), Color32.FromHsv(_h, 1, _v)),
        _ => Horizontal(Color32.Black, Color32.FromHsv(_h, _s, 1)),
    };

    private double Value(int row) => row switch
    {
        0 => _color.R / 255.0,
        1 => _color.G / 255.0,
        2 => _color.B / 255.0,
        3 => _h / 360.0,
        4 => _s,
        _ => _v,
    };

    public override void Render(DrawingContext g)
    {
        PickerPaint.FillHitArea(g, Bounds);
        for (int row = 0; row < Labels.Length; row++)
        {
            var bar = BarRect(row);
            AppTheme.DrawTextIn(g, Labels[row], new Rect(2, bar.Y - 3, LabelW, BarH + 6), AppTheme.DimTextBrush, AppTheme.SmallFontSize);
            g.FillRectangle(BarBrush(row), bar);
            g.DrawRectangle(null, PickerPaint.BlackOutline, bar.Inflate(0.5));
            double x = Math.Round(bar.X + Value(row) * (bar.Width - 1));
            g.DrawRectangle(null, PickerPaint.BlackOutline, new Rect(x - 3, bar.Y - 2, 6, bar.Height + 4));
            g.DrawRectangle(null, PickerPaint.WhiteMarker, new Rect(x - 2, bar.Y - 1, 4, bar.Height + 2));
        }
    }

    protected override bool Begin(Avalonia.Point p)
    {
        _row = Math.Clamp((int)((p.Y - 1) / RowH), 0, Labels.Length - 1);
        return true;
    }

    protected override void Drag(Avalonia.Point p)
    {
        var bar = BarRect(_row);
        double t = Math.Clamp((p.X - bar.X) / Math.Max(1, bar.Width - 1), 0, 1);
        byte b = (byte)Math.Round(t * 255);
        switch (_row)
        {
            case 0: RgbPicked?.Invoke(this, new Color32(b, _color.G, _color.B, _color.A)); break;
            case 1: RgbPicked?.Invoke(this, new Color32(_color.R, b, _color.B, _color.A)); break;
            case 2: RgbPicked?.Invoke(this, new Color32(_color.R, _color.G, b, _color.A)); break;
            case 3: HsvPicked?.Invoke(this, ((float)Math.Min(t * 360, 359.9), _s, _v)); break;
            case 4: HsvPicked?.Invoke(this, (_h, (float)t, _v)); break;
            default: HsvPicked?.Invoke(this, (_h, _s, (float)t)); break;
        }
    }
}

/// <summary>색 고르기 방식 목록·이름·탭 그림(색 패널 "색" 머리글 오른쪽 작은 <see cref="TabStrip"/>: 그림만, 풍선 도움말에 이름).</summary>
internal static class ColorPickerModes
{
    /// <summary>탭 순서.</summary>
    public static readonly ColorPickerMode[] All = [ColorPickerMode.Square, ColorPickerMode.Honeycomb, ColorPickerMode.Wheel, ColorPickerMode.Sliders];

    public static string Name(ColorPickerMode mode) => mode switch
    {
        ColorPickerMode.Square => UiStrings.S_ColorPickers_Name_B6557C1430,
        ColorPickerMode.Honeycomb => UiStrings.S_ColorPickers_Name_C21FB98CC0,
        ColorPickerMode.Wheel => UiStrings.S_ColorPickers_Name_5E5B391CB2,
        _ => UiStrings.S_ColorPickers_Name_73C3C4C63D,
    };

    /// <summary>방식을 나타내는 작은 그림(가운데 기준 약 14×12).</summary>
    public static TabIconPainter Icon(ColorPickerMode mode) => (g, r, brush) =>
    {
        var pen = AppTheme.Pen(brush.Color, 1.2);
        var c = r.Center;
        switch (mode)
        {
            case ColorPickerMode.Square:
                g.DrawRectangle(null, pen, new Rect(c.X - 7, c.Y - 5, 10, 10));
                g.DrawRectangle(null, pen, new Rect(c.X + 5, c.Y - 5, 2, 10));
                break;
            case ColorPickerMode.Honeycomb:
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    for (int k = 0; k < 6; k++)
                    {
                        double a = Math.PI / 180 * (60 * k - 90);
                        var p = new Avalonia.Point(c.X + 6 * Math.Cos(a), c.Y + 6 * Math.Sin(a));
                        if (k == 0) ctx.BeginFigure(p, false);
                        else ctx.LineTo(p);
                    }
                    ctx.EndFigure(true);
                }
                g.DrawGeometry(null, pen, geo);
                break;
            case ColorPickerMode.Wheel:
                g.DrawEllipse(null, AppTheme.Pen(brush.Color, 2.2), c, 5.5, 5.5);
                g.DrawRectangle(null, pen, new Rect(c.X - 2, c.Y - 2, 4, 4));
                break;
            default:
                for (int k = -1; k <= 1; k++)
                {
                    double y = c.Y + k * 4;
                    g.DrawLine(pen, new Avalonia.Point(c.X - 7, y), new Avalonia.Point(c.X + 7, y));
                    double kx = c.X + (k == 0 ? 3 : k < 0 ? -3 : 0);
                    g.FillRectangle(brush, new Rect(kx - 1.5, y - 1.5, 3, 3));
                }
                break;
        }
    };
}
