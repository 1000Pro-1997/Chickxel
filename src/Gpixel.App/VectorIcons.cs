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
    Refresh,
    Layers,
    FilePlus,
    FolderPlus,
}

public static class VectorIcons
{
    private static readonly Dictionary<VectorIcon, Geometry[]> Cache = new();
    private static readonly Dictionary<(IBrush, double), IPen> Pens = new();

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
        _ => [],
    };

    private static Geometry[] Geometries(VectorIcon icon)
    {
        if (Cache.TryGetValue(icon, out var g)) return g;
        g = Paths(icon).Select(p => (Geometry)StreamGeometry.Parse(p)).ToArray();
        Cache[icon] = g;
        return g;
    }

    /// <summary><paramref name="area"/> 가운데에 <paramref name="size"/> 크기로 아이콘을 그린다.</summary>
    public static void Draw(DrawingContext g, VectorIcon icon, Rect area, double size, IBrush brush, double strokeWidth = 2)
    {
        double scale = size / 24.0;
        double x = Math.Round(area.X + (area.Width - size) / 2), y = Math.Round(area.Y + (area.Height - size) / 2);
        if (!Pens.TryGetValue((brush, strokeWidth), out var pen))
            Pens[(brush, strokeWidth)] = pen = new Pen(brush, strokeWidth) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }.ToImmutable();
        using (g.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y)))
            foreach (var geo in Geometries(icon))
                g.DrawGeometry(null, pen, geo);
    }
}

/// <summary>선 아이콘 하나를 크기에 맞춰 그리는 컨트롤.</summary>
public class IconView(VectorIcon icon, IBrush? brush = null, double strokeWidth = 2.2) : Control
{
    public override void Render(DrawingContext g)
        => VectorIcons.Draw(g, icon, new Rect(Bounds.Size), Math.Min(Bounds.Width, Bounds.Height), brush ?? AppTheme.TextBrush, strokeWidth);
}
