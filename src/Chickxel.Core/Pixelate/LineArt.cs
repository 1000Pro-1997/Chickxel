using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Pixelate;

/// <summary>정리한 선의 색: 획 색 그대로, 또는 안쪽 이웃 색을 어둡게.</summary>
public enum LineColorMode { Keep, Darken }

/// <summary>선 정리(<see cref="LineArt"/>) 설정.</summary>
public sealed record LineArtOptions
{
    /// <summary>OKLab L ≤ <see cref="Darkness"/>인 색을 선 색으로 본다.</summary>
    public bool AutoDark { get; init; } = true;
    public double Darkness { get; init; } = 0.35;
    /// <summary>더 고른 선 색(스포이트) — 서로 다른 색은 맞닿아도 다른 획.</summary>
    public IReadOnlyList<Color32> Colors { get; init; } = [];
    /// <summary>고른 선 색과 같은 색으로 볼 OKLab 거리.</summary>
    public double ColorTolerance { get; init; } = 0.06;
    /// <summary>덩어리 평균 굵기(넓이 ÷ 뼈대 길이)가 이 이하인 것만 선 — 넓은 어두운 면·눈은 그대로.</summary>
    public double MaxWidth { get; init; } = 2.5;
    /// <summary>뼈대 길이(px)가 이보다 짧은 조각은 그대로.</summary>
    public int MinLength { get; init; } = 3;
    /// <summary>직선·원호로 맞출 때 벗어나도 되는 거리(px, <see cref="CleanLines.Fit"/>의 straightTolerance).</summary>
    public double Tolerance { get; init; } = 0.6;
    /// <summary>이 각도(도) 이상 꺾인 자리는 모서리로 남긴다.</summary>
    public double CornerAngle { get; init; } = 40;
    public LineColorMode ColorMode { get; init; } = LineColorMode.Keep;
    /// <summary><see cref="LineColorMode.Darken"/>: 새 선 픽셀마다 둘레의 선 아닌 색(가장 많은 것)의 OKLab L × (1 − Darken), 둘레에 그런 색이 없으면 획 색.</summary>
    public double Darken { get; init; } = 0.5;
}

/// <summary>딴 획 하나: 직선·원호 조각, 고리인지, 획 색(가장 많은 픽셀 색), 이 획에 든 옛 선 픽셀.</summary>
public sealed record LineStroke(int Id, IReadOnlyList<LineShape> Shapes, bool Closed, Color32 Color, IReadOnlyList<Point> Pixels);

/// <summary>한 그림에서 딴 획들과 옛 선 픽셀(획에 든 것) 표시.</summary>
public sealed record LineArtTrace(int Width, int Height, IReadOnlyList<LineStroke> Strokes, bool[] LineMask)
{
    /// <summary>바꿔도 되는 픽셀(<see cref="LineArt.Trace"/>에 준 선택 영역, null = 전체) — <see cref="LineArt.Apply"/>는 이 밖을 바꾸지 않는다.</summary>
    public bool[]? Area { get; init; }
}

/// <summary>
/// 픽셀 그림의 선 정리: 선 색 픽셀(어두운 색 또는 고른 색)을 같은 색끼리 8방향 덩어리로 묶어 가는 것만 뼈대로 줄이고, 갈림점에서 나눈 선마다
/// 직선·원호로 맞춘 뒤(<see cref="Trace"/>) 1px 픽셀 퍼펙트 선으로 다시 그리고 옛 두꺼운 선 자리를 메운다(<see cref="Apply"/>).
/// </summary>
public static class LineArt
{
    /// <summary>
    /// 선 픽셀 = 영역(<paramref name="area"/>, null = 전체) 안의 불투명 픽셀 중 고른 색(<see cref="LineArtOptions.Colors"/>, 가장 가까운 것)에서
    /// <see cref="LineArtOptions.ColorTolerance"/> 안이거나, <see cref="LineArtOptions.AutoDark"/>면 OKLab L ≤ <see cref="LineArtOptions.Darkness"/>인 것.
    /// 같은 선 색끼리 8방향으로 이어진 덩어리마다 Zhang–Suen 뼈대를 따서 평균 굵기가 <see cref="LineArtOptions.MaxWidth"/>보다 굵으면 건너뛰고,
    /// 뼈대 선 중 짧은 것(홀로 떨어진 선은 <see cref="LineArtOptions.MinLength"/> 미만, 한쪽만 갈림점에 붙은 잔가지는 그것과 굵기 × 2 + 1 미만)은 뺀다.
    /// 남은 선마다 픽셀 중심을 <see cref="CleanLines.Fit"/>으로 맞춰 획 하나로 만들고, 덩어리 픽셀은 덩어리 안에서 가장 가까운 획에 나눠 준다.
    /// </summary>
    public static LineArtTrace Trace(PixelImage img, LineArtOptions o, bool[]? area = null)
    {
        int w = img.Width, h = img.Height, n = w * h;
        if (area is not null && area.Length != n) throw new ArgumentException(null, nameof(area));
        var lineColor = LineColorTest(o);
        var key = new int[n];
        for (int i = 0; i < n; i++)
        {
            var c = img.Pixels[i];
            key[i] = area is not null && !area[i] ? -1 : lineColor(c);
        }

        var strokes = new List<LineStroke>();
        var mask = new bool[n];
        var component = new int[n];
        Array.Fill(component, -1);
        var owner = new int[n];
        Array.Fill(owner, -1);
        var stack = new Stack<int>();
        var pixels = new List<int>();
        int components = 0;
        var labs = new Dictionary<uint, Lab>();
        Lab LabOf(Color32 c)
        {
            if (!labs.TryGetValue(c.Argb, out var l)) labs[c.Argb] = l = OkLab.FromColor(c);
            return l;
        }
        for (int start = 0; start < n; start++)
        {
            if (key[start] < 0 || component[start] >= 0) continue;
            int id = components++;
            pixels.Clear();
            component[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                pixels.Add(i);
                int cx = i % w, cy = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if ((dx | dy) == 0 || nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (component[j] >= 0 || key[j] != key[start]) continue;
                        // 어두운 선(자동)은 서로 비슷한 색끼리만 잇는다 — 검은 외곽선과 거기 붙은 남색 치마·신발 같은 어두운 면이 한 덩어리가 되지 않게.
                        if (key[start] == 0 && Lab.Distance(LabOf(img.Pixels[i]), LabOf(img.Pixels[j])) > DarkSplit) continue;
                        component[j] = id;
                        stack.Push(j);
                    }
            }
            foreach (var part in ThinParts(pixels, id, w, h, component, ref components))
                AddStrokes(img, o, part.Pixels, part.Id, component, owner, strokes, mask);
        }
        return new LineArtTrace(w, h, strokes, mask) { Area = area is null ? null : (bool[])area.Clone() };
    }

    /// <summary>자동 어두운 선 덩어리에서 이웃 색끼리 이을 OKLab 거리 상한.</summary>
    private const float DarkSplit = 0.1f;

    /// <summary>
    /// 덩어리에서 굵은 곳(8방향 이웃이 모두 덩어리인 칸 — 3px 이상 굵음 — 과 그 둘레 한 칸)을 빼고 남은 가는 부분들(8방향으로 이어진 것마다 새 번호).
    /// 굵은 곳 칸은 번호를 지워(−2) 선으로 보지 않는다 — 굵은 덩어리(검은 신발)에 붙은 1px 외곽선도 따로 정리된다.
    /// </summary>
    private static List<(int Id, int[] Pixels)> ThinParts(List<int> pixels, int id, int w, int h, int[] component, ref int components)
    {
        bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && component[y * w + x] == id;
        var core = new List<int>();
        foreach (int i in pixels)
        {
            int x = i % w, y = i / w;
            bool full = true;
            for (int dy = -1; dy <= 1 && full; dy++)
                for (int dx = -1; dx <= 1 && full; dx++)
                    if ((dx | dy) != 0 && !In(x + dx, y + dy)) full = false;
            if (full) core.Add(i);
        }
        if (core.Count == 0) return [(id, [.. pixels])];
        var thick = new HashSet<int>();
        foreach (int i in core)
        {
            int x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (In(x + dx, y + dy)) thick.Add((y + dy) * w + x + dx);
        }
        foreach (int i in thick) component[i] = -2;
        var parts = new List<(int, int[])>();
        var stack = new Stack<int>();
        var part = new List<int>();
        foreach (int start in pixels)
        {
            if (component[start] != id) continue;
            int pid = components++;
            part.Clear();
            component[start] = pid;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                part.Add(i);
                int cx = i % w, cy = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if ((dx | dy) == 0 || !In(nx, ny)) continue;
                        component[ny * w + nx] = pid;
                        stack.Push(ny * w + nx);
                    }
            }
            parts.Add((pid, [.. part]));
        }
        return parts;
    }

    /// <summary>덩어리 하나의 뼈대 선들을 획으로.</summary>
    private static void AddStrokes(PixelImage img, LineArtOptions o, int[] pixels, int id, int[] component, int[] owner,
        List<LineStroke> strokes, bool[] mask)
    {
        int w = img.Width, h = img.Height;
        var (chains, length) = VectorOutline.Skeleton(pixels, w, mixedLinks: true);
        if (length == 0) return;
        double width = pixels.Length / (double)length;
        if (width > o.MaxWidth) return;
        int minLength = Math.Max(1, o.MinLength);
        double minSpur = Math.Max(minLength, Math.Ceiling(2 * width) + 1);
        var ends = new Dictionary<int, int>();
        foreach (var chain in chains)
        {
            ends[chain.Points[0]] = ends.GetValueOrDefault(chain.Points[0]) + 1;
            ends[chain.Points[^1]] = ends.GetValueOrDefault(chain.Points[^1]) + 1;
        }
        var kept = new List<VectorOutline.Chain>();
        foreach (var chain in chains)
        {
            int count = chain.Points.Length;
            if (count < 2) continue;
            bool free = ends[chain.Points[0]] == 1 && ends[chain.Points[^1]] == 1;
            bool loop = chain.Points[0] == chain.Points[^1];
            if (chain.Spur ? count < minSpur : (free || loop) && count < minLength) continue;
            kept.Add(chain);
        }
        if (kept.Count == 0) return;

        // 덩어리 픽셀을 가장 가까운(덩어리 안 8방향 걸음) 획 뼈대에 나눠 준다.
        var queue = new Queue<int>();
        for (int k = 0; k < kept.Count; k++)
            foreach (int i in kept[k].Points)
                if (owner[i] < 0)
                {
                    owner[i] = k;
                    queue.Enqueue(i);
                }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int cx = i % w, cy = i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if ((dx | dy) == 0 || nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (component[j] != id || owner[j] >= 0) continue;
                    owner[j] = owner[i];
                    queue.Enqueue(j);
                }
        }
        var assigned = new List<Point>[kept.Count];
        for (int k = 0; k < kept.Count; k++) assigned[k] = [];
        foreach (int i in pixels)
            if (owner[i] >= 0) assigned[owner[i]].Add(new Point(i % w, i / w));

        var counts = new Dictionary<Color32, int>();
        for (int k = 0; k < kept.Count; k++)
        {
            // 뼈대 칸을 모두 앞 획이 가져간 선(갈림점 사이 한두 칸)은 그 획들이 이미 잇는다.
            if (assigned[k].Count == 0) continue;
            var chain = kept[k];
            var pts = new List<(double X, double Y)>(chain.Points.Length + 2);
            foreach (int i in chain.Points) pts.Add((i % w + 0.5, i / w + 0.5));
            bool closed = pts.Count > 2 && chain.Points[0] == chain.Points[^1];
            if (!closed)
            {
                if (ends[chain.Points[0]] == 1) Extend(pts, true, assigned[k], width);
                if (ends[chain.Points[^1]] == 1) Extend(pts, false, assigned[k], width);
            }
            var shapes = CleanLines.Fit(pts, o.Tolerance, o.CornerAngle, closed);
            counts.Clear();
            foreach (var p in assigned[k])
            {
                var c = img[p.X, p.Y];
                counts[c] = counts.GetValueOrDefault(c) + 1;
                mask[p.Y * w + p.X] = true;
            }
            strokes.Add(new LineStroke(strokes.Count, shapes, closed, counts.MaxBy(kv => kv.Value).Key, assigned[k]));
        }
    }

    /// <summary>
    /// 갈래 없이 끝나는 뼈대 끝을 그 획 픽셀 중 끝 방향으로 가장 멀리 나간 것(옆으로 굵기 절반 안, 굵기 + 1 거리 안)의 중심까지 늘인다 —
    /// 세선화가 깎아 먹은 선 끝을 되살린다.
    /// </summary>
    private static void Extend(List<(double X, double Y)> pts, bool atStart, List<Point> own, double width)
    {
        var end = atStart ? pts[0] : pts[^1];
        var back = atStart ? pts[Math.Min(3, pts.Count - 1)] : pts[Math.Max(0, pts.Count - 4)];
        double dx = end.X - back.X, dy = end.Y - back.Y, length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) return;
        dx /= length;
        dy /= length;
        double best = 0.5, side = width / 2 + 0.25, reach = width + 1;
        (double X, double Y)? far = null;
        foreach (var p in own)
        {
            double vx = p.X + 0.5 - end.X, vy = p.Y + 0.5 - end.Y;
            double along = vx * dx + vy * dy, across = Math.Abs(vx * dy - vy * dx);
            if (along > best && along <= reach && across <= side) { best = along; far = (p.X + 0.5, p.Y + 0.5); }
        }
        if (far is not { } f) return;
        if (atStart) pts.Insert(0, f);
        else pts.Add(f);
    }

    /// <summary>
    /// 획들을 다시 그린 새 그림(<paramref name="img"/>는 그대로): 뺀 획(<paramref name="excluded"/>)이 아닌 획마다 조각을 1px 선으로 획 색
    /// (<see cref="LineColorMode.Darken"/>이면 안쪽 이웃 색을 어둡게)으로 칠하고(뺀 획의 픽셀 위에는 칠하지 않음), 새 선이 덮지 않은 옛 선 픽셀은
    /// 투명·그림 밖에서 새 선을 넘지 않고(상하좌우) 닿으면 투명, 아니면 선 색이 아닌 이웃 색 중 가장 많은 것으로 안쪽까지 한 겹씩 메운다.
    /// <see cref="LineArtTrace.Area"/> 밖은 바꾸지 않는다.
    /// </summary>
    public static PixelImage Apply(PixelImage img, LineArtTrace t, LineArtOptions o, IReadOnlySet<int>? excluded = null)
    {
        if (img.Width != t.Width || img.Height != t.Height) throw new ArgumentException(null, nameof(t));
        var result = img.Clone();
        int w = img.Width, h = img.Height, n = w * h;
        var area = t.Area;
        var old = new bool[n];
        var kept = new bool[n];
        var active = new List<LineStroke>();
        foreach (var stroke in t.Strokes)
        {
            bool skip = excluded is not null && excluded.Contains(stroke.Id);
            if (!skip) active.Add(stroke);
            foreach (var p in stroke.Pixels)
                if (img.Contains(p.X, p.Y)) (skip ? kept : old)[p.Y * w + p.X] = true;
        }
        if (active.Count == 0) return result;

        var test = LineColorTest(o);
        var isLine = new Dictionary<uint, bool>();
        bool LineColored(Color32 c)
        {
            if (!isLine.TryGetValue(c.Argb, out bool v)) isLine[c.Argb] = v = test(c) >= 0;
            return v;
        }
        var px = img.Pixels;
        var line = new bool[n];
        var color = new Color32[n];
        var counts = new Dictionary<Color32, int>();
        // 안쪽 색을 진하게: 새 선 픽셀마다 둘레(3×3, 없으면 5×5)의 선 아닌 색 중 가장 많은 것을 어둡게 — 한 획이 머리카락·피부처럼 여러 면에 닿으면
        // 닿은 면마다 그 면의 진한 색이 된다(셀아웃). 둘레에 그런 색이 없으면 획 색.
        var darkCache = new Dictionary<Color32, Color32>();
        float darken = -(float)Math.Clamp(o.Darken, 0, 1);
        Color32? InnerDark(int x, int y)
        {
            for (int r = 1; r <= 2; r++)
            {
                counts.Clear();
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx | dy) == 0 || !img.Contains(nx, ny)) continue;
                        int j = ny * w + nx;
                        var c = px[j];
                        if (c.A == 0 || t.LineMask[j] || LineColored(c)) continue;
                        counts[c] = counts.GetValueOrDefault(c) + 1;
                    }
                if (counts.Count == 0) continue;
                var fill = counts.MaxBy(kv => kv.Value).Key;
                if (!darkCache.TryGetValue(fill, out var d)) darkCache[fill] = d = OkLab.ShiftLightness(fill, darken);
                return d;
            }
            return null;
        }
        foreach (var stroke in active)
        {
            foreach (var p in CleanLines.Rasterize(stroke.Shapes, stroke.Closed))
            {
                if (!img.Contains(p.X, p.Y)) continue;
                int i = p.Y * w + p.X;
                if (kept[i] || (area is not null && !area[i])) continue;
                line[i] = true;
                color[i] = o.ColorMode == LineColorMode.Darken ? InnerDark(p.X, p.Y) ?? stroke.Color : stroke.Color;
            }
        }

        var stale = new bool[n];
        for (int i = 0; i < n; i++) stale[i] = old[i] && !line[i];
        var outside = Outside(img, line, stale, fromTransparent: true);
        var target = new bool[n];
        for (int i = 0; i < n; i++) target[i] = stale[i] && !outside[i];
        var fill = new Color32[n];
        var done = new bool[n];
        // 선 색이 아닌 이웃부터, 그래도 못 메운 칸은 남은 선 색 픽셀(눈·넓은 면 등)로도.
        Spread(w, h, target, fill, done, i => px[i].A != 0 && !old[i] && !line[i] && !LineColored(px[i]), px);
        Spread(w, h, target, fill, done, i => px[i].A != 0 && !old[i] && !line[i], px);
        var dst = result.Pixels;
        for (int i = 0; i < n; i++)
        {
            if (line[i]) dst[i] = color[i];
            else if (stale[i])
            {
                if (outside[i]) dst[i] = Color32.Transparent;
                else if (done[i]) dst[i] = fill[i];
            }
        }
        return result;
    }

    /// <summary>색 → 선 색 번호(고른 색 k는 k + 1, 어두운 색 0, 선 색 아님 −1).</summary>
    private static Func<Color32, int> LineColorTest(LineArtOptions o)
    {
        var picked = o.Colors.Select(OkLab.FromColor).ToArray();
        var cache = new Dictionary<uint, int>();
        return c =>
        {
            if (c.A == 0) return -1;
            if (cache.TryGetValue(c.Argb, out int k)) return k;
            var lab = OkLab.FromColor(c);
            k = -1;
            double best = o.ColorTolerance;
            for (int j = 0; j < picked.Length; j++)
            {
                double d = Lab.Distance(lab, picked[j]);
                if (d <= best) { best = d; k = j + 1; }
            }
            if (k < 0 && o.AutoDark && lab.L <= o.Darkness) k = 0;
            cache[c.Argb] = k;
            return k;
        };
    }

    /// <summary>
    /// 메울 칸(<paramref name="target"/>)을 이미 정한 칸(<paramref name="done"/>)과 <paramref name="source"/> 픽셀에서 상하좌우로 한 겹씩 메운다:
    /// 칸마다 정해진 상하좌우 이웃 색 중 가장 많은 것.
    /// </summary>
    private static void Spread(int w, int h, bool[] target, Color32[] fill, bool[] done, Func<int, bool> source, Color32[] px)
    {
        int n = w * h;
        var known = new bool[n];
        var color = new Color32[n];
        for (int i = 0; i < n; i++)
        {
            if (done[i]) { known[i] = true; color[i] = fill[i]; }
            else if (!target[i] && source(i)) { known[i] = true; color[i] = px[i]; }
        }
        var front = new List<int>();
        var queued = new bool[n];
        void Neighbours(int i, Action<int> visit)
        {
            int x = i % w, y = i / w;
            if (x > 0) visit(i - 1);
            if (x < w - 1) visit(i + 1);
            if (y > 0) visit(i - w);
            if (y < h - 1) visit(i + w);
        }
        for (int i = 0; i < n; i++)
        {
            if (!target[i] || known[i]) continue;
            bool touches = false;
            Neighbours(i, j => touches |= known[j]);
            if (touches) { front.Add(i); queued[i] = true; }
        }
        var counts = new Dictionary<Color32, int>();
        var picked = new List<(int Index, Color32 Color)>();
        while (front.Count > 0)
        {
            picked.Clear();
            foreach (int i in front)
            {
                counts.Clear();
                Neighbours(i, j =>
                {
                    if (known[j]) counts[color[j]] = counts.GetValueOrDefault(color[j]) + 1;
                });
                if (counts.Count > 0) picked.Add((i, counts.MaxBy(kv => kv.Value).Key));
            }
            var next = new List<int>();
            foreach (var (i, c) in picked)
            {
                known[i] = done[i] = true;
                color[i] = fill[i] = c;
            }
            foreach (var (i, _) in picked)
                Neighbours(i, j =>
                {
                    if (target[j] && !known[j] && !queued[j]) { queued[j] = true; next.Add(j); }
                });
            front = next;
        }
    }

    /// <summary>
    /// 바깥 칸: 그림 둘레(<paramref name="fromTransparent"/>면 모든 투명 픽셀도)에서 투명·<paramref name="old"/> 칸을 따라 상하좌우로 닿는 칸 —
    /// 8방향으로 이어진 새 선(<paramref name="line"/>)은 넘지 못한다.
    /// </summary>
    internal static bool[] Outside(PixelImage img, bool[] line, bool[] old, bool fromTransparent)
    {
        int w = img.Width, h = img.Height;
        var outside = new bool[w * h];
        var queue = new Queue<int>();
        void Seed(int i)
        {
            if (outside[i] || line[i] || !(img.Pixels[i].A == 0 || old[i])) return;
            outside[i] = true;
            queue.Enqueue(i);
        }
        for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
        if (fromTransparent)
            for (int i = 0; i < w * h; i++)
                if (img.Pixels[i].A == 0) Seed(i);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Seed(i - 1);
            if (x < w - 1) Seed(i + 1);
            if (y > 0) Seed(i - w);
            if (y < h - 1) Seed(i + w);
        }
        return outside;
    }
}
