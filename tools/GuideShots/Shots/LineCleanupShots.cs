using Avalonia;
using Avalonia.Controls;
using Chickxel.App;
using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace GuideShots;

/// <summary>
/// 선 정리 창(편집 메뉴 "선 정리…")과 그 비교 그림. 예시 그림은 손으로 그은 듯 흔들리고 1~2px로 두께가 들쭉날쭉한 검은 선으로 그린
/// 집·해·덤불(<see cref="RoughDoodle"/> — 직선·원호·날카로운 모서리가 다 있고 면 색이 칠해져 있음)이다.
/// </summary>
internal static class LineCleanupShots
{
    private static readonly Color32 Back = new(43, 43, 43);
    private static readonly Color32 CheckerLight = new(204, 204, 204);
    private static readonly Color32 CheckerDark = new(170, 170, 170);
    private const int Margin = 16, Gap = 24, Checker = 8, Zoom = 6;
    private const int Width = 64, Height = 48;

    /// <summary>
    /// 창: ① 지금 그림(찾은 획 겹침) ② 정리한 그림 ③ 범례 ④ 획·바뀐 픽셀 수, 선 찾기 ⑤ 자동 어두운 색·어두움 ⑥ 선 색 ⑦ 색 허용 범위 ⑧ 최대 굵기 ⑨ 최소 길이,
    /// 모양 ⑩ 곧게 펴기 허용치 ⑪ 모서리 각도, 새 선 색 ⑫ 방식 ⑬ 진하게, ⑭ 모든 프레임 ⑮ 적용 ⑯ 기본값. 프레임을 하나 복제해 두어 "모든 프레임(2장)"이 보인다.
    /// </summary>
    [Shot("tools-line-cleanup")]
    public static void Window(ShotKit k)
    {
        var path = Path.Combine(k.WorkDir, "Doodle.png");
        PngIO.Save(RoughDoodle(), path);
        k.Main(path);
        k.Command("프레임 복제");
        k.Settle(300);
        k.Dialog(() => k.Command("선 정리…"), w =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (ShotKit.Get<object?>(w, "_state") is null && sw.ElapsedMilliseconds < 20000) ShotKit.Pump(100);
            var count = ShotKit.Get<TextBlock>(w, "_count");
            var left = (StackPanel)count.Parent!;
            // 번호 동그라미가 범례·개수·설정 줄 머리 글자를 가리지 않게 두 칸 왼쪽을 띄운다(창은 내용에 맞춰 넓어짐).
            left.Margin = new Thickness(14, 0, 0, 0);
            ((StackPanel)left.Parent!).Children[1].Margin = new Thickness(14, 0, 0, 0);
            ShotKit.Pump(400);
            Rect In(Control c) => ShotKit.Bounds(c, w);
            Rect Lead(Rect r, double by = 14) => new(r.X - by, r.Y + 2, r.Width + by, r.Height - 4);
            Rect Row(string field) => Lead(In((Control)ShotKit.Get<Control>(w, field).Parent!));
            // 두 그림 칸(머리 글자 아래) — 동그라미가 "지금 그림 ×5" 글자를 가리지 않게.
            var view = In(ShotKit.Get<Control>(w, "_view"));
            double half = (view.Width - 8) / 2;
            var mode = ShotKit.Get<Control>(w, "_colorMode");
            var modeRow = (StackPanel)mode.Parent!;
            var reset = ShotKit.All<Button>(w).First(b => b.IsEffectivelyVisible && b.Content is string s && s == UiStrings.LineArt_Reset);
            k.Save(w, marks:
            [
                new Rect(view.X, view.Y + 28, half, view.Height - 28),
                new Rect(view.X + half + 8, view.Y + 28, half, view.Height - 28),
                Lead(In(left.Children[1])),
                Lead(In(count)),
                Row("_darkness"),
                Row("_picker").Union(Lead(In(ShotKit.Get<Control>(w, "_chips")))),
                Row("_colorTolerance"),
                Row("_maxWidth"),
                Row("_minLength"),
                Row("_tolerance"),
                Row("_cornerAngle"),
                Lead(In(mode)),
                Lead(In(modeRow.Children[1]).Union(In(ShotKit.Get<Control>(w, "_darken"))), 6),
                Lead(In(ShotKit.Get<Control>(w, "_allFrames"))),
                ShotKit.Get<Control>(w, "OkButton"),
                reset,
            ]);
        });
    }

    /// <summary>
    /// 비교 그림: 정리 전 ↔ 정리 후(기본 설정), 선 색 "그대로" ↔ "안쪽 색을 진하게"(기본 50%) — 실제 <see cref="LineArt.Trace"/>·<see cref="LineArt.Apply"/> 결과를 6배로.
    /// </summary>
    [Shot("tools-line-cleanup-compare")]
    public static void Compare(ShotKit k)
    {
        var src = RoughDoodle();
        PixelImage Clean(LineArtOptions o) => LineArt.Apply(src, LineArt.Trace(src, o), o);
        var keep = Clean(new LineArtOptions());
        var darken = Clean(new LineArtOptions { ColorMode = LineColorMode.Darken });
        Save(k, Row(Scale(src), Scale(keep)), "tools-line-cleanup-compare");
        Save(k, Row(Scale(keep), Scale(darken)), "tools-line-cleanup-compare-2");
    }

    // ───────────── 예시 그림 ─────────────

    private static readonly Color32 Ink = new(34, 28, 40);
    private static readonly Color32 Roof = new(206, 84, 70);
    private static readonly Color32 Wall = new(242, 222, 182);
    private static readonly Color32 Door = new(156, 100, 62);
    private static readonly Color32 Glass = new(150, 200, 240);
    private static readonly Color32 Sun = new(250, 204, 70);
    private static readonly Color32 Bush = new(96, 174, 84);

    /// <summary>
    /// 64×48 낙서: 면을 먼저 칠하고 그 위에 검은 선을 손으로 그은 듯(자리가 ±0.4px 흔들리고 굵기가 1~2px로 바뀜) 긋는다.
    /// 지붕 삼각형·벽(곧은 선과 날카로운 모서리), 아치 문(직선 + 원호), 둥근 창·해·덤불(원호), 땅(긴 직선).
    /// </summary>
    private static PixelImage RoughDoodle()
    {
        var img = new PixelImage(Width, Height);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                Color32? c = null;
                if (Inside((px, py), (21, 8), (5, 23), (37, 23))) c = Roof;
                if (px >= 8 && px <= 34 && py >= 23 && py <= 44) c = Wall;
                if (px >= 17 && px <= 25 && py <= 44 && (py >= 37 || Dist(px, py, 21, 37) <= 4)) c = Door;
                if (Dist(px, py, 21, 17) <= 3.4) c = Glass;
                if (Dist(px, py, 50, 12) <= 7) c = Sun;
                if (py <= 44 && Dist(px, py, 49, 44) <= 9) c = Bush;
                if (c is { } color) img[x, y] = color;
            }

        int seed = 0;
        void Pen(IEnumerable<(double X, double Y)> pts) => Stroke(img, Dense(pts.ToList()), seed++);
        // 땅·벽·지붕·아치 문
        Pen([(2, 44.5), (62, 44.5)]);
        Pen([(8, 23.5), (8, 44)]);
        Pen([(34, 23.5), (34, 44)]);
        Pen([(4.5, 23.5), (21, 7.5), (37.5, 23.5), (4.5, 23.5)]);
        Pen([(17, 44), (17, 37), .. Arc(21, 37, 4, 180, 360), (25, 44)]);
        // 둥근 창·해·덤불(위 반원)
        Pen(Arc(21, 17, 3.9, 0, 360));
        Pen(Arc(50, 12, 7.4, 0, 360));
        Pen(Arc(49, 44, 9.4, 180, 360));
        return img;
    }

    private static IEnumerable<(double X, double Y)> Arc(double cx, double cy, double r, double from, double to)
    {
        for (double a = from; a <= to + 1e-9; a += 4)
            yield return (cx + r * Math.Cos(a * Math.PI / 180), cy + r * Math.Sin(a * Math.PI / 180));
    }

    /// <summary>꺾은선을 0.2px 간격 점으로.</summary>
    private static List<(double X, double Y)> Dense(List<(double X, double Y)> pts)
    {
        var o = new List<(double X, double Y)>();
        for (int i = 1; i < pts.Count; i++)
        {
            var (ax, ay) = pts[i - 1];
            var (bx, by) = pts[i];
            int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) / 0.2));
            for (int s = 0; s < n; s++) o.Add((ax + (bx - ax) * s / n, ay + (by - ay) * s / n));
        }
        o.Add(pts[^1]);
        return o;
    }

    /// <summary>점마다 진행 방향의 옆으로 천천히 흔들고(±0.4px) 반지름 0.45~1.05px 원을 찍는다.</summary>
    private static void Stroke(PixelImage img, List<(double X, double Y)> pts, int seed)
    {
        double p1 = seed * 1.7, p2 = seed * 2.9 + 1, p3 = seed * 0.8 + 2;
        double t = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var (x, y) = pts[i];
            var (nx, ny) = pts[Math.Min(i + 1, pts.Count - 1)];
            var (qx, qy) = pts[Math.Max(i - 1, 0)];
            double dx = nx - qx, dy = ny - qy, len = Math.Sqrt(dx * dx + dy * dy);
            if (i > 0) t += Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
            double side = 0.27 * Math.Sin(t * 0.3 + p1) + 0.12 * Math.Sin(t * 1.1 + p2);
            double r = 0.75 + 0.3 * Math.Sin(t * 0.6 + p3);
            if (len > 0) { x += -dy / len * side; y += dx / len * side; }
            for (int py = (int)Math.Floor(y - r); py <= (int)Math.Ceiling(y + r); py++)
                for (int px = (int)Math.Floor(x - r); px <= (int)Math.Ceiling(x + r); px++)
                    if (px >= 0 && py >= 0 && px < img.Width && py < img.Height && Dist(px + 0.5, py + 0.5, x, y) <= r) img[px, py] = Ink;
        }
    }

    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    private static bool Inside((double X, double Y) p, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        static double Cross((double X, double Y) o, (double X, double Y) u, (double X, double Y) v) => (u.X - o.X) * (v.Y - o.Y) - (u.Y - o.Y) * (v.X - o.X);
        double d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
        return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
    }

    // ───────────── 비교 그림 ─────────────

    private static PixelImage Scale(PixelImage img)
    {
        var o = new PixelImage(img.Width * Zoom, img.Height * Zoom);
        for (int y = 0; y < o.Height; y++)
            for (int x = 0; x < o.Width; x++)
                o[x, y] = img[x / Zoom, y / Zoom];
        return o;
    }

    /// <summary>그림들을 체커 칸 위에 나란히 놓은 한 장(어두운 바탕·여백 — <c>GuidePixelateCompareShots</c>와 같은 모양).</summary>
    private static PixelImage Row(params PixelImage[] panels)
    {
        int height = panels.Max(p => p.Height);
        var img = new PixelImage(Margin * 2 + panels.Sum(p => p.Width) + Gap * (panels.Length - 1), Margin * 2 + height);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                img[x, y] = Back;
        int left = Margin;
        foreach (var p in panels)
        {
            for (int y = 0; y < p.Height; y++)
                for (int x = 0; x < p.Width; x++)
                    img[left + x, Margin + y] = Over(p[x, y], (x / Checker + y / Checker) % 2 == 0 ? CheckerLight : CheckerDark);
            left += p.Width + Gap;
        }
        return img;
    }

    private static Color32 Over(Color32 c, Color32 under)
    {
        if (c.A == 255) return c;
        double a = c.A / 255.0;
        byte Mix(byte top, byte bottom) => (byte)Math.Round(top * a + bottom * (1 - a));
        return new Color32(Mix(c.R, under.R), Mix(c.G, under.G), Mix(c.B, under.B));
    }

    private static void Save(ShotKit k, PixelImage image, string name)
    {
        var file = Path.Combine(ShotKit.OutDir, name + ".png");
        Directory.CreateDirectory(ShotKit.OutDir);
        PngIO.Save(image, file);
        k.Saved.Add(Path.GetRelativePath(ShotKit.Root, file));
    }
}
