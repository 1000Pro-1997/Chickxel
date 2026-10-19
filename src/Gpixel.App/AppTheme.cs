using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Gpixel.App;

/// <summary>모든 창이 함께 쓰는 어두운 평면 테마 색·글꼴과 그리기 도우미.</summary>
public static class AppTheme
{
    public static readonly Color BackColor = Color.FromRgb(30, 30, 33);
    public static readonly Color PanelColor = Color.FromRgb(43, 43, 48);
    public static readonly Color InputColor = Color.FromRgb(55, 55, 61);
    public static readonly Color HoverColor = Color.FromRgb(64, 64, 72);
    public static readonly Color TextColor = Color.FromRgb(222, 222, 226);
    public static readonly Color DimTextColor = Color.FromRgb(140, 140, 148);
    public static readonly Color AccentColor = Color.FromRgb(86, 156, 214);
    public static readonly Color BorderColor = Color.FromRgb(70, 70, 78);
    public static readonly Color WarningColor = Color.FromRgb(230, 160, 70);
    public static readonly Color CheckerA = Color.FromRgb(68, 68, 72);
    public static readonly Color CheckerB = Color.FromRgb(92, 92, 98);

    public static readonly IImmutableSolidColorBrush BackBrush = new ImmutableSolidColorBrush(BackColor);
    public static readonly IImmutableSolidColorBrush PanelBrush = new ImmutableSolidColorBrush(PanelColor);
    public static readonly IImmutableSolidColorBrush InputBrush = new ImmutableSolidColorBrush(InputColor);
    public static readonly IImmutableSolidColorBrush HoverBrush = new ImmutableSolidColorBrush(HoverColor);
    public static readonly IImmutableSolidColorBrush TextBrush = new ImmutableSolidColorBrush(TextColor);
    public static readonly IImmutableSolidColorBrush DimTextBrush = new ImmutableSolidColorBrush(DimTextColor);
    public static readonly IImmutableSolidColorBrush AccentBrush = new ImmutableSolidColorBrush(AccentColor);
    public static readonly IImmutableSolidColorBrush BorderBrush = new ImmutableSolidColorBrush(BorderColor);
    public static readonly IImmutableSolidColorBrush WarningBrush = new ImmutableSolidColorBrush(WarningColor);

    /// <summary>한글이 고르게 보이는 OS 기본 글꼴(Windows: 맑은 고딕, macOS: Apple SD 산돌고딕 Neo).</summary>
    public static readonly FontFamily FontFamily = new(
        OperatingSystem.IsWindows() ? "Malgun Gothic" :
        OperatingSystem.IsMacOS() ? "Apple SD Gothic Neo" :
        FontFamily.DefaultFontFamilyName);

    public const double FontSize = 12;
    public const double SmallFontSize = 11;
    public static readonly Typeface Typeface = new(FontFamily);
    public static readonly Typeface BoldTypeface = new(FontFamily, FontStyle.Normal, FontWeight.Bold);

    private static readonly Dictionary<Color, IImmutableSolidColorBrush> Brushes = new();

    /// <summary>색 하나의 불변 브러시(캐시됨).</summary>
    public static IImmutableSolidColorBrush Brush(Color c)
    {
        if (Brushes.TryGetValue(c, out var b)) return b;
        b = new ImmutableSolidColorBrush(c);
        if (Brushes.Count < 4096) Brushes[c] = b;
        return b;
    }

    /// <summary>1 화면 픽셀 너비의 단색 펜.</summary>
    public static ImmutablePen Pen(Color c, double thickness = 1, ImmutableDashStyle? dash = null) => new(Brush(c), thickness, dash);

    // ───────────── 앱 스타일 ─────────────

    /// <summary>Gpixel 색을 쓴 어두운 Fluent 테마(촘촘한 크기).</summary>
    public static FluentTheme CreateFluentTheme()
    {
        var fluent = new FluentTheme { DensityStyle = DensityStyle.Compact };
        fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources
        {
            Accent = AccentColor,
            RegionColor = PanelColor,
            AltHigh = BackColor,
            AltLow = BackColor,
            AltMedium = BackColor,
            AltMediumHigh = BackColor,
            AltMediumLow = BackColor,
            BaseHigh = TextColor,
            BaseLow = BorderColor,
            BaseMedium = DimTextColor,
            BaseMediumHigh = Color.FromRgb(190, 190, 196),
            BaseMediumLow = Color.FromRgb(110, 110, 118),
            ChromeAltLow = Color.FromRgb(190, 190, 196),
            ChromeBlackHigh = Colors.Black,
            ChromeBlackLow = Color.FromRgb(190, 190, 196),
            ChromeBlackMedium = BackColor,
            ChromeBlackMediumLow = BackColor,
            ChromeDisabledHigh = BorderColor,
            ChromeDisabledLow = DimTextColor,
            ChromeGray = Color.FromRgb(110, 110, 118),
            ChromeHigh = Color.FromRgb(110, 110, 118),
            ChromeLow = PanelColor,
            ChromeMedium = InputColor,
            ChromeMediumLow = PanelColor,
            ChromeWhite = Colors.White,
            ListLow = HoverColor,
            ListMedium = BorderColor,
        };
        return fluent;
    }

    /// <summary>앱 전체 스타일: 창 배경·글꼴, 입력란 배경, 촘촘한 단추.</summary>
    public static void Install(Application app)
    {
        app.Styles.Add(new Style(x => x.Is<Window>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, PanelBrush),
                new Setter(TemplatedControl.ForegroundProperty, TextBrush),
                new Setter(TemplatedControl.FontFamilyProperty, FontFamily),
                new Setter(TemplatedControl.FontSizeProperty, FontSize),
                new Setter(Window.IconProperty, Icons.AppIcon()),
            },
        });
        app.Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, InputBrush),
                new Setter(TemplatedControl.BorderBrushProperty, BorderBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 3)),
                new Setter(Layoutable.MinHeightProperty, 24d),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(2)),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        });
        app.Styles.Add(new Style(x => x.OfType<ToggleButton>().Class(":checked"))
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, AccentBrush) },
        });
        foreach (var type in new[] { typeof(TextBox), typeof(ComboBox), typeof(NumericUpDown) })
        {
            app.Styles.Add(new Style(x => x.OfType(type))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, InputBrush),
                    new Setter(TemplatedControl.BorderBrushProperty, BorderBrush),
                    new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(2)),
                    new Setter(Layoutable.MinHeightProperty, 24d),
                },
            });
        }
        app.Styles.Add(new Style(x => x.OfType<MenuItem>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 3)) },
        });
        // 숫자 상자: 위/아래 단추 없이 좌우로 끌어 값 바꾸기.
        NumberScrub.Install();
        app.Styles.Add(new Style(x => x.OfType<NumericUpDown>().Template().OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 2)),
                new Setter(Layoutable.MinWidthProperty, 0d),
            },
        });
    }

    // ───────────── 글자 ─────────────

    /// <summary>사용자 그리기용 글자 모양.</summary>
    public static FormattedText Text(string text, IBrush? brush = null, double size = FontSize, bool bold = false)
        => new(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            bold ? BoldTypeface : Typeface, size, brush ?? TextBrush);

    /// <summary>글자를 그리고 그 크기를 돌려준다.</summary>
    public static Avalonia.Size DrawText(DrawingContext g, string text, Avalonia.Point origin, IBrush? brush = null, double size = FontSize, bool bold = false)
    {
        var ft = Text(text, brush, size, bold);
        g.DrawText(ft, origin);
        return new Avalonia.Size(ft.WidthIncludingTrailingWhitespace, ft.Height);
    }

    /// <summary>글자를 사각형 안 세로 가운데(가로는 왼쪽 + <paramref name="padLeft"/>)에 잘라 그린다.</summary>
    public static void DrawTextIn(DrawingContext g, string text, Rect r, IBrush? brush = null, double size = FontSize, bool bold = false,
        double padLeft = 0, bool center = false)
    {
        var ft = Text(text, brush, size, bold);
        ft.MaxTextWidth = Math.Max(1, r.Width - padLeft);
        ft.MaxLineCount = 1;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        double x = center ? r.X + (r.Width - ft.Width) / 2 : r.X + padLeft;
        using (g.PushClip(r))
            g.DrawText(ft, new Avalonia.Point(x, r.Y + (r.Height - ft.Height) / 2));
    }

    public static Avalonia.Size MeasureText(string text, double size = FontSize, bool bold = false)
    {
        var ft = Text(text, null, size, bold);
        return new Avalonia.Size(ft.WidthIncludingTrailingWhitespace, ft.Height);
    }

    // ───────────── 체커 ─────────────

    private static readonly Dictionary<int, WriteableBitmap> CheckerBitmaps = new();

    /// <summary>한 칸이 <paramref name="cell"/> 픽셀인 2×2 칸 체커 비트맵(캐시됨, Dispose 금지).</summary>
    private static unsafe WriteableBitmap CheckerBitmap(int cell)
    {
        if (CheckerBitmaps.TryGetValue(cell, out var bmp)) return bmp;
        int n = cell * 2;
        uint a = 0xFF000000 | (uint)(CheckerA.R << 16 | CheckerA.G << 8 | CheckerA.B);
        uint b = 0xFF000000 | (uint)(CheckerB.R << 16 | CheckerB.G << 8 | CheckerB.B);
        bmp = new WriteableBitmap(new PixelSize(n, n), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var fb = bmp.Lock())
        {
            for (int y = 0; y < n; y++)
            {
                var row = new Span<uint>((byte*)fb.Address + (long)y * fb.RowBytes, n);
                for (int x = 0; x < n; x++) row[x] = (x < cell) == (y < cell) ? a : b;
            }
        }
        CheckerBitmaps[cell] = bmp;
        return bmp;
    }

    /// <summary>투명 체커 배경을 그린다. cell은 화면 단위, 무늬는 area 왼쪽 위에 맞춘다.</summary>
    public static void DrawChecker(DrawingContext g, Rect area, double cell)
    {
        if (area.Width <= 0 || area.Height <= 0) return;
        int c = Math.Clamp((int)Math.Round(cell), 1, 64);
        var brush = new ImageBrush(CheckerBitmap(c))
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            DestinationRect = new RelativeRect(area.X, area.Y, c * 2, c * 2, RelativeUnit.Absolute),
        };
        using (g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            g.FillRectangle(brush, area);
    }
}
