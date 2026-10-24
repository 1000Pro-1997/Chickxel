namespace Gpixel.App;

/// <summary>선으로 그리는 작은 아이콘(24×24 격자 SVG 경로, Lucide 아이콘 모양 — ISC 라이선스).</summary>
public enum VectorIcon
{
    Eye,
    EyeOff,
    Lock,
    LockOpen,
    Copy,
    Plus,
    Minus,
    Refresh,
    Layers,
    FilePlus,
    FolderPlus,
    Play,
    Pause,
    Dice,
    ExternalLink,
    /// <summary>두 화살표 맞바꾸기(⇄).</summary>
    Swap,
    /// <summary>양쪽 화살표 하나(↔).</summary>
    MoveHorizontal,
    /// <summary>위아래 화살표 하나(↕).</summary>
    MoveVertical,
    Check,
    X,
    /// <summary>작은 점(채워 그리면 ●).</summary>
    Dot,
    Circle,
    /// <summary>가운데 점이 있는 원(라디오 단추 켜짐).</summary>
    CircleDot,
    Square,
    Diamond,
    Triangle,
    /// <summary>끌기 손잡이(점 2×3).</summary>
    Grip,
    ChevronDown,
    ChevronRight,
    ChevronUp,
    Menu,
    Star,
    Heart,
    Download,
    Trash,
    /// <summary>연필(고치기).</summary>
    Pencil,
    ArrowUpLeft,
    ArrowUp,
    ArrowUpRight,
    ArrowLeft,
    ArrowRight,
    ArrowDownLeft,
    ArrowDown,
    ArrowDownRight,
    /// <summary>아래로 꺾여 오른쪽으로(↳).</summary>
    CornerDownRight,
    /// <summary>아래로 꺾여 왼쪽으로(↵ 줄바꿈).</summary>
    CornerDownLeft,
    /// <summary>둥근 바탕 위에 올리는 굵은 느낌표(경고 표시 안쪽).</summary>
    ExclamationMark,
    /// <summary>둥근 바탕 위에 올리는 물음표.</summary>
    QuestionMark,
    /// <summary>둥근 바탕·네모 테 안에 올리는 "i".</summary>
    InfoMark,
}

public static class VectorIcons
{
    private static readonly Dictionary<VectorIcon, Geometry[]> Cache = new();
    private static readonly Dictionary<(IBrush, double), IPen> Pens = new();

    /// <summary>원 하나의 경로(반원 두 개).</summary>
    private static string Circle(double cx, double cy, double r)
        => FormattableString.Invariant($"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0z");

    private static string[] Paths(VectorIcon icon) => icon switch
    {
        VectorIcon.Eye =>
        [
            "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0",
            "M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0",
        ],
        VectorIcon.EyeOff =>
        [
            "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49",
            "M14.084 14.158a3 3 0 0 1-4.242-4.242",
            "M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143",
            "M2 2L22 22",
        ],
        VectorIcon.Lock =>
        [
            "M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z",
            "M7 11V7a5 5 0 0 1 10 0v4",
        ],
        VectorIcon.LockOpen =>
        [
            "M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z",
            "M7 11V7a5 5 0 0 1 9.9-1",
        ],
        VectorIcon.Copy =>
        [
            "M10 8h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H10a2 2 0 0 1-2-2V10a2 2 0 0 1 2-2z",
            "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2",
        ],
        VectorIcon.Plus => ["M5 12h14", "M12 5v14"],
        VectorIcon.Minus => ["M5 12h14"],
        VectorIcon.Refresh =>
        [
            "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8",
            "M21 3v5h-5",
            "M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16",
            "M8 16H3v5",
        ],
        VectorIcon.Layers =>
        [
            "M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z",
            "M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12",
            "M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17",
        ],
        VectorIcon.FilePlus =>
        [
            "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z",
            "M14 2v4a2 2 0 0 0 2 2h4",
            "M9 15h6",
            "M12 18v-6",
        ],
        VectorIcon.FolderPlus =>
        [
            "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z",
            "M12 10v6",
            "M9 13h6",
        ],
        VectorIcon.Play => ["M6 3l14 9-14 9z"],
        VectorIcon.Pause =>
        [
            "M7 4h2a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z",
            "M15 4h2a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-2a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z",
        ],
        VectorIcon.Dice =>
        [
            "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z",
            "M16 8h.01", "M8 8h.01", "M8 16h.01", "M16 16h.01", "M12 12h.01",
        ],
        VectorIcon.ExternalLink => ["M15 3h6v6", "M10 14 21 3", "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"],
        VectorIcon.Swap => ["M8 3 4 7l4 4", "M4 7h16", "m16 21 4-4-4-4", "M20 17H4"],
        VectorIcon.MoveHorizontal => ["m18 8 4 4-4 4", "M2 12h20", "m6 8-4 4 4 4"],
        VectorIcon.MoveVertical => ["M12 2v20", "m8 18 4 4 4-4", "m8 6 4-4 4 4"],
        VectorIcon.Check => ["M20 6 9 17l-5-5"],
        VectorIcon.X => ["M18 6 6 18", "m6 6 12 12"],
        VectorIcon.Dot => [Circle(12, 12, 4)],
        VectorIcon.Circle => [Circle(12, 12, 10)],
        VectorIcon.CircleDot => [Circle(12, 12, 10), Circle(12, 12, 3)],
        VectorIcon.Square => ["M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"],
        VectorIcon.Diamond =>
        [
            "M2.7 10.3a2.41 2.41 0 0 0 0 3.41l7.59 7.59a2.41 2.41 0 0 0 3.41 0l7.59-7.59a2.41 2.41 0 0 0 0-3.41l-7.59-7.59a2.41 2.41 0 0 0-3.41 0Z",
        ],
        VectorIcon.Triangle => ["M13.73 4a2 2 0 0 0-3.46 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"],
        VectorIcon.Grip => [Circle(9, 5, 1), Circle(9, 12, 1), Circle(9, 19, 1), Circle(15, 5, 1), Circle(15, 12, 1), Circle(15, 19, 1)],
        VectorIcon.ChevronDown => ["m6 9 6 6 6-6"],
        VectorIcon.ChevronRight => ["m9 18 6-6-6-6"],
        VectorIcon.ChevronUp => ["m18 15-6-6-6 6"],
        VectorIcon.Menu => ["M4 6h16", "M4 12h16", "M4 18h16"],
        VectorIcon.Star => ["M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01z"],
        VectorIcon.Heart =>
        [
            "M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z",
        ],
        VectorIcon.Download => ["M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "m7 10 5 5 5-5", "M12 15V3"],
        VectorIcon.Trash =>
        [
            "M3 6h18",
            "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6",
            "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2",
            "M10 11v6",
            "M14 11v6",
        ],
        VectorIcon.Pencil =>
        [
            "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z",
            "m15 5 4 4",
        ],
        VectorIcon.ArrowUpLeft => ["M7 17V7h10", "M17 17 7 7"],
        VectorIcon.ArrowUp => ["m5 12 7-7 7 7", "M12 19V5"],
        VectorIcon.ArrowUpRight => ["M7 7h10v10", "M7 17 17 7"],
        VectorIcon.ArrowLeft => ["m12 19-7-7 7-7", "M19 12H5"],
        VectorIcon.ArrowRight => ["M5 12h14", "m12 5 7 7-7 7"],
        VectorIcon.ArrowDownLeft => ["M17 7 7 17", "M17 17H7V7"],
        VectorIcon.ArrowDown => ["M12 5v14", "m19 12-7 7-7-7"],
        VectorIcon.ArrowDownRight => ["m7 7 10 10", "M17 7v10H7"],
        VectorIcon.CornerDownRight => ["m15 10 5 5-5 5", "M4 4v7a4 4 0 0 0 4 4h12"],
        VectorIcon.CornerDownLeft => ["m9 10-5 5 5 5", "M20 4v7a4 4 0 0 1-4 4H4"],
        VectorIcon.ExclamationMark => ["M12 4v10", "M12 19.5h.01"],
        VectorIcon.QuestionMark => ["M8 8.5a4 4 0 1 1 5.5 3.7c-.9.4-1.5 1.2-1.5 2.2v.6", "M12 19.5h.01"],
        VectorIcon.InfoMark => ["M12 10.5v9", "M12 4.5h.01"],
        _ => [],
    };

    private static Geometry[] Geometries(VectorIcon icon)
    {
        if (Cache.TryGetValue(icon, out var g)) return g;
        g = Paths(icon).Select(p => (Geometry)StreamGeometry.Parse(p)).ToArray();
        Cache[icon] = g;
        return g;
    }

    /// <summary>
    /// <paramref name="area"/> 가운데에 <paramref name="size"/> 크기로 아이콘을 그린다.
    /// <paramref name="filled"/>면 닫힌 도형 안을 같은 색으로 채운다(●·◆·★처럼).
    /// </summary>
    public static void Draw(DrawingContext g, VectorIcon icon, Rect area, double size, IBrush brush, double strokeWidth = 2, bool filled = false)
    {
        double scale = size / 24.0;
        double x = Math.Round(area.X + (area.Width - size) / 2), y = Math.Round(area.Y + (area.Height - size) / 2);
        using (g.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y)))
        {
            var pen = Pen(brush, strokeWidth);
            foreach (var geo in Geometries(icon))
                g.DrawGeometry(filled ? brush : null, pen, geo);
        }
    }

    /// <summary>둥근 끝 펜. 바뀌지 않는 붓만 모아 둔다(테마·전환이 만드는 붓은 매번 새로).</summary>
    private static IPen Pen(IBrush brush, double strokeWidth)
    {
        if (brush is not IImmutableBrush) return new Pen(brush, strokeWidth) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (!Pens.TryGetValue((brush, strokeWidth), out var pen))
            Pens[(brush, strokeWidth)] = pen = new Pen(brush, strokeWidth) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }.ToImmutable();
        return pen;
    }
}

/// <summary>
/// 선 아이콘 하나를 크기에 맞춰 그리는 컨트롤. 붓을 주지 않으면 글자색(<see cref="Avalonia.Controls.Documents.TextElement.ForegroundProperty"/>)을
/// 물려받아, 단추 안에서는 단추 글자처럼 꺼짐·올림 색을 따른다.
/// </summary>
public class IconView : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        Avalonia.Controls.Documents.TextElement.ForegroundProperty.AddOwner<IconView>();

    public static readonly StyledProperty<VectorIcon> IconProperty =
        AvaloniaProperty.Register<IconView, VectorIcon>(nameof(Icon));

    static IconView() => AffectsRender<IconView>(ForegroundProperty, IconProperty);

    private readonly double _strokeWidth;

    public IconView(VectorIcon icon, IBrush? brush = null, double strokeWidth = 2.2)
    {
        Icon = icon;
        if (brush is not null) Foreground = brush;
        _strokeWidth = strokeWidth;
    }

    public VectorIcon Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>닫힌 도형 안을 채운다(●·◆·★ 모양).</summary>
    public bool Filled { get; init; }

    public override void Render(DrawingContext g)
    {
        // 투명 바탕: 글자처럼 칸 전체가 마우스(풍선 도움말)를 받게
        g.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        VectorIcons.Draw(g, Icon, new Rect(Bounds.Size), Math.Min(Bounds.Width, Bounds.Height), Foreground ?? AppTheme.TextBrush, _strokeWidth, Filled);
    }
}
