using System.Diagnostics;
using Avalonia.Controls.Documents;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Chickxel.Core.Community;
using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.App;

/// <summary>
/// 이름 효과(<see cref="NameStyle"/>)로 꾸민 닉네임 한 줄. 효과 규칙은 <see cref="NameEffect"/> 값의 설명(홈페이지 <c>rewards.js</c>와 같음).
/// 크기는 같은 글꼴의 평범한 글과 똑같고(효과는 그리기만 — 빛·물결·불티·반짝이는 칸 밖으로 번져도 레이아웃을 밀지 않음, 마우스는 통과),
/// 글자마다 모양을 한 번 만들어 두고(<see cref="GlyphRun"/>) 매 프레임 색·위치만 바꿔 그린다.
/// 움직이는 효과는 모두 타이머 하나로 같은 시각을 그리고(화면에 붙어 있고 보이는 것만), 마지막 것이 떨어지면 타이머가 선다. 주기가 0이면 멈춘 그림.
/// </summary>
public sealed class NameEffectText : Control
{
    public static readonly StyledProperty<FontFamily> FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner<NameEffectText>();
    public static readonly StyledProperty<double> FontSizeProperty = TextElement.FontSizeProperty.AddOwner<NameEffectText>();
    public static readonly StyledProperty<FontWeight> FontWeightProperty = TextElement.FontWeightProperty.AddOwner<NameEffectText>();

    /// <summary>움직임 한 프레임(ms) — 글자 물결이 매끄럽게 보이는 정도.</summary>
    private const int TickMs = 40;
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(TickMs) };
    private static readonly List<NameEffectText> Running = [];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>네 갈래 별(반지름 1, 가운데 0,0).</summary>
    private static readonly Geometry StarShape = Geometry.Parse("M 0,-1 L 0.22,-0.22 L 1,0 L 0.22,0.22 L 0,1 L -0.22,0.22 L -1,0 L -0.22,-0.22 Z");

    /// <summary>번짐 빛 겹(선 굵기 = 글자 크기 배수, 진하기).</summary>
    private static readonly (double Width, double Alpha)[] GlowLayers = [(0.5, 0.10), (0.32, 0.16), (0.16, 0.26)];

    private readonly string _text;
    private readonly NameStyle _style;
    private readonly int _seed;
    private TextLayout? _layout;
    private Letter[] _letters = [];
    private double _width, _height, _baseline;
    /// <summary>순간이동: 사라지는 차례(차례 → 글자)와 글자마다 몇 번째로 사라지는지.</summary>
    private int[] _order = [], _rank = [];

    /// <summary>
    /// 글자 하나(글꼴이 한 덩어리로 그리는 단위 — 한글 음절·결합 문자 포함): 기준선 위치에 놓인 모양, 왼쪽 끝, 폭, 빈칸인지(반짝이를 남기지 않음),
    /// 빛용 윤곽(처음 쓸 때 만듦).
    /// </summary>
    private sealed class Letter(GlyphRun run, double x, double width, bool blank)
    {
        public GlyphRun Run { get; } = run;
        public double X { get; } = x;
        public double Width { get; } = width;
        public bool Blank { get; } = blank;
        public Geometry? Outline { get; set; }
    }

    static NameEffectText()
    {
        AffectsMeasure<NameEffectText>(FontFamilyProperty, FontSizeProperty, FontWeightProperty);
        Timer.Tick += (_, _) =>
        {
            foreach (var view in Running)
                if (view.IsEffectivelyVisible) view.InvalidateVisual();
        };
    }

    public NameEffectText(string text, NameStyle style)
    {
        _text = text;
        _style = style;
        _seed = Seed(text);
        IsHitTestVisible = false;
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FontFamilyProperty || change.Property == FontSizeProperty || change.Property == FontWeightProperty) Reset();
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        Build();
        return new Avalonia.Size(_width, _height);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_style.PeriodMs <= 0) return;
        Running.Add(this);
        Timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Running.Remove(this);
        if (Running.Count == 0) Timer.Stop();
        Reset();
    }

    public override void Render(DrawingContext context) => Paint(context, _style.PeriodMs > 0 ? Clock.ElapsedMilliseconds : 0);

    /// <summary>글자 모양을 버린다(글꼴이 바뀌었거나 화면에서 떨어짐 — 다음에 그릴 때 다시 만듦).</summary>
    private void Reset()
    {
        foreach (var letter in _letters) letter.Run.Dispose();
        _letters = [];
        _layout?.Dispose();
        _layout = null;
    }

    /// <summary>평범한 글과 같은 줄 배치(<see cref="TextLayout"/>)에서 글자마다 기준선 위치의 모양을 떼어 둔다.</summary>
    private void Build()
    {
        if (_layout is not null) return;
        _layout = new TextLayout(_text, new Typeface(FontFamily, FontStyle.Normal, FontWeight), FontSize, Brushes.Black);
        _width = _layout.WidthIncludingTrailingWhitespace;
        _height = _layout.Height;
        var letters = new List<Letter>();
        foreach (var line in _layout.TextLines)
        {
            double x = line.Start;
            _baseline = line.Baseline;
            foreach (var run in line.TextRuns)
            {
                if (run is not ShapedTextRun shaped)
                {
                    if (run is DrawableTextRun drawable) x += drawable.Size.Width;
                    continue;
                }
                var buffer = shaped.ShapedBuffer;
                IReadOnlyList<GlyphInfo> glyphs = buffer;
                var chars = shaped.Text.Span;
                // 글자 묶음 번호가 조각 안 위치인지 문단 위치인지와 상관없이 조각의 첫 묶음을 0으로
                int firstCluster = int.MaxValue;
                foreach (var glyph in glyphs) firstCluster = Math.Min(firstCluster, glyph.GlyphCluster);
                // 오른쪽 → 왼쪽 글(아랍어 등)은 글자를 떼지 않고 한 덩어리로
                for (int g = 0; g < glyphs.Count;)
                {
                    int end = g + 1;
                    if (!buffer.IsLeftToRight) end = glyphs.Count;
                    else while (end < glyphs.Count && glyphs[end].GlyphCluster == glyphs[g].GlyphCluster) end++;
                    var infos = new GlyphInfo[end - g];
                    double advance = 0;
                    for (int k = 0; k < infos.Length; k++)
                    {
                        infos[k] = glyphs[g + k];
                        advance += infos[k].GlyphAdvance;
                    }
                    int from = Math.Clamp(glyphs[g].GlyphCluster - firstCluster, 0, chars.Length);
                    int to = end < glyphs.Count ? Math.Clamp(glyphs[end].GlyphCluster - firstCluster, from, chars.Length) : chars.Length;
                    bool blank = buffer.IsLeftToRight && chars[from..to].IsWhiteSpace();
                    var glyphRun = new GlyphRun(buffer.GlyphTypeface, buffer.FontRenderingEmSize, shaped.Text, infos, new Avalonia.Point(x, _baseline), buffer.BidiLevel);
                    letters.Add(new Letter(glyphRun, x, advance, blank));
                    x += advance;
                    g = end;
                }
            }
        }
        _letters = [.. letters];
        if (_style.Effect == NameEffect.Teleport) Shuffle();
    }

    /// <summary>순간이동에서 글자가 사라지는 차례(이름마다 정해진 무작위 — 주기마다 같음).</summary>
    private void Shuffle()
    {
        int n = _letters.Length;
        _order = new int[n];
        _rank = new int[n];
        for (int i = 0; i < n; i++) _order[i] = i;
        for (int i = n - 1; i > 0; i--)
        {
            int j = HashInt(_seed, i, 17, i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }
        for (int k = 0; k < n; k++) _rank[_order[k]] = k;
    }

    /// <summary><paramref name="ms"/> 시각의 모습을 그린다(주기 안 위치 t = 0~1).</summary>
    private void Paint(DrawingContext g, long ms)
    {
        Build();
        if (_letters.Length == 0) return;
        int period = _style.PeriodMs;
        double t = period > 0 ? ms % period / (double)period : 0;
        using (g.PushTransform(Matrix.CreateTranslation(0, (Bounds.Height - _height) / 2)))
        {
            switch (_style.Effect)
            {
                case NameEffect.Gradient: PaintGradient(g, t); break;
                case NameEffect.Neon: PaintNeon(g, t); break;
                case NameEffect.Wave: PaintWave(g, t); break;
                case NameEffect.Glitch: PaintGlitch(g, t); break;
                case NameEffect.Teleport: PaintTeleport(g, t); break;
                case NameEffect.Hallucination: PaintHallucination(g, t); break;
                case NameEffect.Fire: PaintFire(g, t); break;
                case NameEffect.Galaxy: PaintGalaxy(g, t); break;
                case NameEffect.Rainbow: PaintRainbow(g, t); break;
            }
        }
    }

    private Color32 Pick(int index) => _style.Colors[index % _style.Colors.Length];

    private double Em => FontSize;

    /// <summary>글자 윗부분(대문자 높이쯤)의 y.</summary>
    private double InkTop => _baseline - 0.72 * Em;

    // ───────────── 효과 ─────────────

    private void PaintGradient(DrawingContext g, double t)
    {
        var brush = Band(_style.Colors, t, flowing: _style.PeriodMs > 0);
        foreach (var letter in _letters) Fill(g, letter, brush);
    }

    private void PaintNeon(DrawingContext g, double t)
    {
        var color = Pick(0);
        // 주기의 70%쯤 두 번 꺼졌다 켜짐
        if (t is >= 0.68 and < 0.71 or >= 0.74 and < 0.77)
        {
            var off = Solid(Darken(color, 0.55));
            foreach (var letter in _letters) Fill(g, letter, off);
            return;
        }
        double strength = 0.7 - 0.3 * Math.Cos(2 * Math.PI * t);
        foreach (var letter in _letters) Glow(g, letter, color, strength);
        var fill = Solid(color);
        foreach (var letter in _letters) Fill(g, letter, fill);
    }

    private void PaintWave(DrawingContext g, double t)
    {
        var brush = Band(_style.Colors, 0, flowing: false);
        for (int i = 0; i < _letters.Length; i++) Fill(g, _letters[i], brush, 0, WaveY(t, i, 0.12));
    }

    /// <summary>글자 <paramref name="i"/>의 물결 높이(위가 −).</summary>
    private double WaveY(double t, int i, double amplitude) => -amplitude * Em * Math.Sin(2 * Math.PI * (t + 0.12 * i));

    private void PaintGlitch(DrawingContext g, double t)
    {
        const double burstAt = 0.6;
        double burst = Math.Min(0.4, 240.0 / _style.PeriodMs);
        var main = Solid(Pick(0));
        if (t < burstAt || t >= burstAt + burst)
        {
            // 평소: 아주 옅은 색 어긋남
            var left = Solid(Pick(1), 0.5);
            var right = Solid(Pick(2), 0.5);
            foreach (var letter in _letters)
            {
                Fill(g, letter, left, -0.5);
                Fill(g, letter, right, 0.5);
            }
            foreach (var letter in _letters) Fill(g, letter, main);
            return;
        }
        // 흔들림: 프레임마다 다른 무작위(주기 안 위치로 정해져 주기마다 같음)
        int step = (int)(t * _style.PeriodMs / TickMs);
        double jitter1 = (HashUnit(_seed, step, 1) - 0.5) * 2, jitter2 = (HashUnit(_seed, step, 2) - 0.5) * 2;
        var copy1 = Solid(Pick(1), 0.85);
        var copy2 = Solid(Pick(2), 0.85);
        foreach (var letter in _letters)
        {
            Fill(g, letter, copy1, -2 + jitter1);
            Fill(g, letter, copy2, 2 + jitter2);
        }
        // 글자 높이를 세 칸으로 나눠 1~3칸에서 가로 띠 하나씩을 옆으로 민다
        double top = InkTop, zone = (_baseline + 0.1 * Em - top) / 3;
        int count = 1 + HashInt(_seed, step, 3, 3), first = HashInt(_seed, step, 4, 3);
        Span<(double Top, double Bottom, double Shift)> slices = stackalloc (double, double, double)[3];
        int used = 0;
        for (int z = 0; z < 3; z++)
        {
            if (Mod(z - first, 3) >= count) continue;
            double height = zone * (0.5 + 0.4 * HashUnit(_seed, step, 10 + z));
            double y = top + zone * z + (zone - height) * HashUnit(_seed, step, 20 + z);
            double shift = (1 + HashInt(_seed, step, 30 + z, 3)) * (HashUnit(_seed, step, 40 + z) < 0.5 ? -1 : 1);
            slices[used++] = (y, y + height, shift);
        }
        double cursor = -_height;
        for (int s = 0; s <= used; s++)
        {
            var (y0, y1, dx) = s < used ? slices[s] : (_height * 2, _height * 2, 0d);
            Strip(cursor, y0, 0);
            if (s < used) Strip(y0, y1, dx);
            cursor = y1;
        }

        void Strip(double y0, double y1, double dx)
        {
            if (y1 <= y0) return;
            using (g.PushClip(new Rect(-16, y0, _width + 32, y1 - y0)))
                foreach (var letter in _letters) Fill(g, letter, main, dx);
        }
    }

    private void PaintTeleport(DrawingContext g, double t)
    {
        var main = Solid(Pick(0));
        var spark = Pick(1);
        int n = _letters.Length;
        double slot = 0.12 / n;
        for (int i = 0; i < n; i++)
        {
            var letter = _letters[i];
            if (letter.Blank) continue;
            double vanish = 0.70 + 0.10 * _rank[i] / n, appear = 0.88 + slot * i;
            if (t < vanish)
            {
                Fill(g, letter, main);
                continue;
            }
            if (t >= appear)
            {
                // 왼쪽부터 1px 위에서 내려앉으며 나타남
                double p = Math.Min(1, (t - appear) / slot);
                Fill(g, letter, p < 1 ? Solid(Pick(0), 0.4 + 0.6 * p) : main, 0, p < 1 ? -1 : 0);
                continue;
            }
            double x = letter.X + letter.Width / 2, y = _baseline - 0.36 * Em;
            double age = (t - vanish) * _style.PeriodMs;
            if (age < 160) Star(g, x, y, Em * (0.34 - 0.18 * age / 160), spark, 1);
            else
            {
                // 빈자리에 남은 반짝 점
                double twinkle = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (age / 320 + HashUnit(_seed, i, 5)));
                double r = Em * 0.08;
                g.DrawEllipse(Solid(spark, 0.45 + 0.55 * twinkle), null, new Avalonia.Point(x, y), r, r);
            }
        }
    }

    private void PaintHallucination(DrawingContext g, double t)
    {
        var colors = new Color32[_style.Colors.Length];
        for (int i = 0; i < colors.Length; i++) colors[i] = RotateHue(_style.Colors[i], t);
        var brush = Band(colors, 0, flowing: false);
        double a = 2 * Math.PI * t, dx = 2 * Math.Cos(a), dy = 2 * Math.Sin(a);
        var ghost1 = Solid(Pick(1), 0.4);
        var ghost2 = Solid(Pick(2), 0.4);
        foreach (var letter in _letters)
        {
            Fill(g, letter, ghost1, dx, dy);
            Fill(g, letter, ghost2, -dx, -dy);
        }
        foreach (var letter in _letters) Fill(g, letter, brush);
    }

    private void PaintFire(DrawingContext g, double t)
    {
        Color32 yellow = Pick(0), orange = Pick(1), red = Pick(2);
        double top = InkTop, bottom = _baseline + 0.05 * Em;
        for (int i = 0; i < _letters.Length; i++)
        {
            // 글자마다 밝기가 일렁임(−1 어둡게 ~ 1 밝게)
            double f = Flicker(t, i);
            Color32 Adjust(Color32 c) => f >= 0 ? Lighten(c, 0.35 * f) : Darken(c, -0.3 * f);
            ImmutableGradientStop[] stops =
                [new(0, Rgb(Adjust(red))), new(0.5, Rgb(Adjust(orange))), new(1, Rgb(Adjust(yellow)))];
            var brush = new ImmutableLinearGradientBrush(stops, 1, null, null, GradientSpreadMethod.Pad,
                new RelativePoint(0, top, RelativeUnit.Absolute), new RelativePoint(0, bottom, RelativeUnit.Absolute));
            Fill(g, _letters[i], brush);
        }
        // 글자 위로 떠오르는 불티 셋(주기의 45% 동안 살고 차례로 어긋남)
        for (int k = 0; k < 3; k++)
        {
            double u = Frac(t - k / 3.0 - 0.07 * HashUnit(_seed, k, 6)) / 0.45;
            if (u >= 1) continue;
            double x = (0.1 + 0.8 * HashUnit(_seed, k, 7)) * _width + Math.Sin(u * 2 * Math.PI) * 1.2;
            double y = top + 0.1 * Em - u * 0.9 * Em;
            g.DrawRectangle(Solid(Mix(yellow, red, u), 1.3 * (1 - u)), null, new Rect(x - 1, y - 1, 2, 2));
        }
    }

    /// <summary>불꽃 글자 <paramref name="i"/>의 밝기 일렁임(−1~1, 약 90ms마다 새 값으로 부드럽게 — 주기마다 같음).</summary>
    private double Flicker(double t, int i)
    {
        int steps = Math.Max(2, (int)Math.Round(_style.PeriodMs / 90.0));
        double s = t * steps;
        int k = (int)Math.Floor(s);
        double fr = s - k, smooth = fr * fr * (3 - 2 * fr);
        double a = HashUnit(_seed, i, Mod(k, steps)), b = HashUnit(_seed, i, Mod(k + 1, steps));
        return (a + (b - a) * smooth) * 2 - 1;
    }

    private void PaintGalaxy(DrawingContext g, double t)
    {
        var brush = Band([Pick(0), Pick(1)], 0, flowing: false);
        foreach (var letter in _letters) Glow(g, letter, Pick(2), 0.6);
        foreach (var letter in _letters) Fill(g, letter, brush);
        Sparkles(g, t, 3, Lighten(Pick(2), 0.6));
    }

    private void PaintRainbow(DrawingContext g, double t)
    {
        var brush = Band(_style.Colors, t, flowing: true);
        for (int i = 0; i < _letters.Length; i++) Glow(g, _letters[i], Color32.White, 0.35, 0, WaveY(t, i, 0.08));
        for (int i = 0; i < _letters.Length; i++) Fill(g, _letters[i], brush, 0, WaveY(t, i, 0.08));
        Sparkles(g, t, 2, Color32.White);
    }

    /// <summary>글자 위에 반짝이는 네 갈래 별 <paramref name="count"/>개(하나가 주기에 두 번 — 자리를 바꿔 — 피었다 짐).</summary>
    private void Sparkles(DrawingContext g, double t, int count, Color32 color)
    {
        const int lives = 2;
        double top = InkTop, height = _baseline - top;
        for (int k = 0; k < count; k++)
        {
            double w = t * lives + k / (double)count;
            int life = Mod((int)Math.Floor(w), lives);
            double u = Frac(w), scale = Math.Sin(Math.PI * u);
            double x = HashUnit(_seed, k * 7 + life, 1) * _width;
            double y = top + HashUnit(_seed, k * 7 + life, 2) * height;
            Star(g, x, y, Em * 0.3 * scale, color, 0.4 + 0.6 * scale);
        }
    }

    // ───────────── 그리기 도구 ─────────────

    private static void Fill(DrawingContext g, Letter letter, IBrush brush, double dx = 0, double dy = 0)
    {
        using (g.PushTransform(Matrix.CreateTranslation(dx, dy))) g.DrawGlyphRun(brush, letter.Run);
    }

    /// <summary>글자 둘레의 부드러운 빛(윤곽을 굵기가 다른 반투명 선으로 겹쳐 그림 — 번짐 ≈ 글자 크기의 절반).</summary>
    private void Glow(DrawingContext g, Letter letter, Color32 color, double strength, double dx = 0, double dy = 0)
    {
        var outline = letter.Outline ??= letter.Run.BuildGeometry();
        using (g.PushTransform(Matrix.CreateTranslation(dx, dy)))
            foreach (var (width, alpha) in GlowLayers)
                g.DrawGeometry(null, new ImmutablePen(Solid(color, alpha * strength), width * Em, null, PenLineCap.Round, PenLineJoin.Round), outline);
    }

    private static void Star(DrawingContext g, double x, double y, double radius, Color32 color, double alpha)
    {
        if (radius < 0.3) return;
        using (g.PushTransform(Matrix.CreateScale(radius, radius) * Matrix.CreateTranslation(x, y)))
            g.DrawGeometry(Solid(color, alpha), null, StarShape);
    }

    /// <summary>
    /// 왼쪽 → 오른쪽 색 띠. <paramref name="flowing"/>이면 되풀이되는 띠(글자 크기 3배 이상 길이)가 <paramref name="shift"/>(0~1)만큼 오른쪽으로 밀린 것 —
    /// 처음 색과 끝 색이 다르면 처음 색을 끝에 한 번 더 둬서 이음매가 없게.
    /// </summary>
    private IImmutableBrush Band(IReadOnlyList<Color32> colors, double shift, bool flowing)
    {
        int n = colors.Count;
        int count = n + (flowing && colors[0].Argb != colors[n - 1].Argb ? 1 : 0);
        var stops = new ImmutableGradientStop[count];
        for (int i = 0; i < count; i++) stops[i] = new ImmutableGradientStop(count == 1 ? 0 : i / (double)(count - 1), Rgb(colors[i % n]));
        double length = flowing ? Math.Max(_width, 3 * Em) : Math.Max(_width, 1);
        double start = flowing ? Frac(shift) * length : 0;
        return new ImmutableLinearGradientBrush(stops, 1, null, null, flowing ? GradientSpreadMethod.Repeat : GradientSpreadMethod.Pad,
            new RelativePoint(start, 0, RelativeUnit.Absolute), new RelativePoint(start + length, 0, RelativeUnit.Absolute));
    }

    private static Color Rgb(Color32 c, double alpha = 1)
        => Avalonia.Media.Color.FromArgb((byte)Math.Round(c.A * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B);

    private static IImmutableBrush Solid(Color32 c, double alpha = 1) => new ImmutableSolidColorBrush(Rgb(c, alpha));

    /// <summary>색상(hue)을 <paramref name="turns"/>바퀴만큼 돌린 색(채도·밝기는 그대로).</summary>
    private static Color32 RotateHue(Color32 c, double turns)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? Frac((g - b) / d / 6) : max == g ? ((b - r) / d + 2) / 6 : ((r - g) / d + 4) / 6;
        double s = max == 0 ? 0 : d / max;
        h = Frac(h + turns);
        byte F(double n)
        {
            double k = (n + h * 6) % 6;
            return (byte)Math.Round((max - max * s * Math.Max(0, Math.Min(k, Math.Min(4 - k, 1)))) * 255);
        }
        return new Color32(F(5), F(3), F(1), c.A);
    }
}
