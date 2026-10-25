using System.Drawing;
using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.Core.Pixelate;

/// <summary>
/// 외곽선을 벡터로 다시 그리기(<see cref="OutlineFit.Vector"/>): 배경을 지운 원본에서 어두운 외곽선 띠를 뼈대(1px 중심선)로 줄여 선들로 따고,
/// 결과 칸 좌표로 옮겨 단순하게 한 뒤 1px 픽셀 퍼펙트 선으로 다시 그린다 — 원본 외곽선이 2~3칸 두께여도 결과 외곽선은 1px이고 끊기지 않는다.
/// </summary>
public static class VectorOutline
{
    /// <summary>뼈대 선 하나(원본 픽셀 번호 = y × 폭 + x). 한쪽 끝만 갈래 없이 끝나면(다른 끝은 갈림점) 잔가지 후보.</summary>
    public sealed record Chain(int[] Points, bool Spur);
    /// <summary>
    /// 어두운 덩어리(8방향으로 이어진 픽셀) 하나: 넓이(원본 px), 바깥 외곽선 띠인지(<see cref="Trace"/>), 평균 굵기(넓이 ÷ 뼈대 길이 — 선이면 선 굵기,
    /// 넓은 어두운 면이면 크다), 픽셀, 뼈대 선들.
    /// </summary>
    public sealed record Blob(int Area, bool Band, double Width, int[] Pixels, IReadOnlyList<Chain> Chains);

    /// <summary>한 프레임의 어두운 덩어리들과 잰 바깥 외곽선 두께(원본 px, 못 재면 0).</summary>
    public sealed record Tracing(IReadOnlyList<Blob> Blobs, double Thickness);

    /// <summary>바깥 외곽선 띠로 볼 깊이: 투명에서 어두운 픽셀을 따라 들어간 거리가 외곽선 두께의 이 배 이하.</summary>
    private const double BandDepth = 1.5;

    /// <summary>
    /// 한 프레임의 어두운 픽셀(OKLab L ≤ <paramref name="darkness"/>)을 나눈다: 바깥 외곽선 띠 = 투명(또는 프레임 밖)에서 어두운 픽셀만 따라 상하좌우로
    /// 들어간 거리가 바깥 외곽선 두께(<see cref="GridDetector.OutlineThickness"/>)의 <see cref="BandDepth"/>배 이하인 픽셀(외곽선에 붙은 어두운 옷 같은
    /// 넓은 면은 띠 깊이에서 잘림), 나머지 어두운 픽셀은 안쪽 덩어리. 덩어리마다 Zhang–Suen 세선화로 뼈대를 따 둔다 —
    /// 칸 크기·설정과 상관없으므로 한 번만 하면 된다(<see cref="PixelateSource.VectorTrace"/>가 기억해 둔다).
    /// </summary>
    public static Tracing Trace(PixelImage cleaned, Lab[] lab, Rectangle area, int[]? labels, int label, double darkness)
    {
        int w = cleaned.Width;
        var px = cleaned.Pixels;
        bool On(int x, int y) => area.Contains(x, y) && px[y * w + x].A != 0 && (labels is null || labels[y * w + x] == label);
        bool Dark(int x, int y) => On(x, y) && lab[y * w + x].L <= darkness;
        var (thickness, samples) = GridDetector.OutlineThickness(cleaned, labels, label, area, darkness);
        if (samples == 0) thickness = 0;
        double depth = thickness > 0 ? Math.Max(1, thickness * BandDepth) : double.PositiveInfinity;

        // 외곽선 잉크 색: 투명과 맞닿은 어두운 픽셀의 OKLab 중앙값, 허용 거리 = 그 둘레 퍼짐(중앙값 거리)의 3배(0.04~0.1).
        // 외곽선 안쪽에 붙은 어두운 그늘(짙은 초록 등)은 띠에 넣지 않는다 — 넣으면 뼈대가 그늘 쪽으로 밀려 바깥 외곽선이 사라진다.
        var seeds = new List<int>();
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                if (Dark(x, y) && (!On(x - 1, y) || !On(x + 1, y) || !On(x, y - 1) || !On(x, y + 1))) seeds.Add(y * w + x);
        Lab ink = default;
        double tolerance = double.PositiveInfinity;
        if (seeds.Count > 0)
        {
            static float Median(List<float> v) { v.Sort(); return v[v.Count / 2]; }
            ink = new Lab(Median(seeds.Select(i => lab[i].L).ToList()), Median(seeds.Select(i => lab[i].A).ToList()), Median(seeds.Select(i => lab[i].B).ToList()));
            var spread = seeds.Select(i => Lab.Distance(lab[i], ink)).ToList();
            tolerance = Math.Clamp(3 * Median(spread), 0.04, 0.1);
        }
        bool Ink(int i) => Lab.Distance(lab[i], ink) <= tolerance;

        // 바깥 외곽선 띠: 투명과 맞닿은 잉크 색 픽셀(깊이 1)에서 잉크 색 픽셀만 따라 너비 우선.
        var dist = new int[px.Length];
        var queue = new Queue<int>();
        foreach (int i in seeds)
            if (Ink(i))
            {
                dist[i] = 1;
                queue.Enqueue(i);
            }
        (int, int)[] n4 = [(-1, 0), (1, 0), (0, -1), (0, 1)];
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            if (dist[i] + 1 > depth) continue;
            int cx = i % w, cy = i / w;
            foreach (var (dx, dy) in n4)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!Dark(nx, ny) || dist[ny * w + nx] != 0 || !Ink(ny * w + nx)) continue;
                dist[ny * w + nx] = dist[i] + 1;
                queue.Enqueue(ny * w + nx);
            }
        }

        var seen = new bool[px.Length];
        var blobs = new List<Blob>();
        var stack = new Stack<int>();
        var pixels = new List<int>();
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                int start = y * w + x;
                if (seen[start] || !Dark(x, y)) continue;
                bool band = dist[start] != 0;
                pixels.Clear();
                seen[start] = true;
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
                            if ((dx | dy) == 0 || !Dark(nx, ny)) continue;
                            int j = ny * w + nx;
                            if (seen[j] || (dist[j] != 0) != band) continue;
                            seen[j] = true;
                            stack.Push(j);
                        }
                }
                var all = pixels.ToArray();
                var (chains, length) = Skeleton(all, w);
                blobs.Add(new Blob(all.Length, band, all.Length / (double)Math.Max(1, length), all, chains));
            }
        return new Tracing(blobs, thickness);
    }

    /// <summary>덩어리 픽셀들을 Zhang–Suen 세선화로 1px 뼈대로 줄이고 선들로 딴다(뼈대 픽셀 수도).</summary>
    private static (List<Chain> Chains, int Length) Skeleton(int[] pixels, int imageWidth)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (int i in pixels)
        {
            int x = i % imageWidth, y = i / imageWidth;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        // 둘레 1칸 여백을 둔 격자.
        int gw = maxX - minX + 3, gh = maxY - minY + 3;
        var g = new byte[gw * gh];
        var live = new List<int>(pixels.Length);
        foreach (int i in pixels)
        {
            int k = (i / imageWidth - minY + 1) * gw + (i % imageWidth - minX + 1);
            g[k] = 1;
            live.Add(k);
        }
        int[] off = [-gw, -gw + 1, 1, gw + 1, gw, gw - 1, -1, -gw - 1]; // P2..P9 (N, NE, E, SE, S, SW, W, NW)
        var remove = new List<int>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int sub = 0; sub < 2; sub++)
            {
                remove.Clear();
                foreach (int k in live)
                {
                    if (g[k] == 0) continue;
                    int b = 0, a = 0;
                    for (int n = 0; n < 8; n++)
                    {
                        b += g[k + off[n]];
                        if (g[k + off[n]] == 0 && g[k + off[(n + 1) % 8]] == 1) a++;
                    }
                    if (b < 2 || b > 6 || a != 1) continue;
                    int p2 = g[k + off[0]], p4 = g[k + off[2]], p6 = g[k + off[4]], p8 = g[k + off[6]];
                    if (sub == 0 ? (p2 * p4 * p6 != 0 || p4 * p6 * p8 != 0) : (p2 * p4 * p8 != 0 || p2 * p6 * p8 != 0)) continue;
                    remove.Add(k);
                }
                foreach (int k in remove) g[k] = 0;
                if (remove.Count > 0) changed = true;
            }
            live.RemoveAll(k => g[k] == 0);
        }

        int Degree(int k)
        {
            int d = 0;
            foreach (int o in off) d += g[k + o];
            return d;
        }
        int ToImage(int k) => (k / gw - 1 + minY) * imageWidth + (k % gw - 1 + minX);
        var chains = new List<Chain>();
        var visited = new HashSet<int>();
        var usedEdges = new HashSet<long>();
        static long Edge(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        var path = new List<int>();
        foreach (int node in live)
        {
            int deg = Degree(node);
            if (deg == 2) continue;
            if (deg == 0) { chains.Add(new Chain([ToImage(node)], false)); continue; }
            foreach (int o in off)
            {
                int q = node + o;
                if (g[q] == 0 || usedEdges.Contains(Edge(node, q)) || (Degree(q) == 2 && visited.Contains(q))) continue;
                usedEdges.Add(Edge(node, q));
                path.Clear();
                path.Add(ToImage(node));
                int prev = node, cur = q;
                while (Degree(cur) == 2 && visited.Add(cur))
                {
                    path.Add(ToImage(cur));
                    int next = -1;
                    foreach (int o2 in off)
                        if (g[cur + o2] == 1 && cur + o2 != prev && (Degree(cur + o2) != 2 || !visited.Contains(cur + o2))) { next = cur + o2; break; }
                    if (next < 0) { cur = -1; break; }
                    prev = cur;
                    cur = next;
                }
                if (cur >= 0)
                {
                    path.Add(ToImage(cur));
                    usedEdges.Add(Edge(prev, cur));
                }
                int endDeg = cur >= 0 ? Degree(cur) : 2;
                chains.Add(new Chain([.. path], (deg == 1) != (endDeg == 1) && (deg >= 3 || endDeg >= 3)));
            }
        }
        // 갈림·끝이 없는 고리.
        foreach (int k in live)
        {
            if (Degree(k) != 2 || visited.Contains(k)) continue;
            path.Clear();
            int prev = -1, cur = k;
            while (cur >= 0 && visited.Add(cur))
            {
                path.Add(ToImage(cur));
                int next = -1;
                foreach (int o in off)
                    if (g[cur + o] == 1 && cur + o != prev && !visited.Contains(cur + o)) { next = cur + o; break; }
                prev = cur;
                cur = next;
            }
            path.Add(path[0]);
            chains.Add(new Chain([.. path], false));
        }
        return (chains, live.Count);
    }

    /// <summary>
    /// 줄인 칸 그림 <paramref name="img"/>(칸 배치 <paramref name="layout"/> — 지역 보정이면 띠마다 다른 경계)의 외곽선을 다시 그린다:
    /// 바깥 외곽선 띠와, 바깥과 닿지 않았어도 넓이가 <see cref="CleanupSettings.VectorBlobCells"/>칸 이상이고 선처럼 가는(평균 굵기 ≤ 외곽선 두께의
    /// <see cref="BandDepth"/>배) 안쪽 덩어리의 뼈대 선(한쪽 끝이 갈래 없이 끝나는 짧은 잔가지 — 한 칸 또는 띠 깊이보다 짧음 — 은 뺌)을
    /// 칸 경계로 결과 좌표(칸 안 위치까지 실수)로 옮겨 더글러스–포이커로 단순하게(허용 오차 <see cref="CleanupSettings.VectorTolerance"/> 결과 px) 하고
    /// 브레젠험 선 + L자 모서리 빼기(<see cref="PixelPerfect"/>)로 1px 선을 외곽선 색으로 칠한다. 새 선에 놓이지 않은 옛 외곽선 칸(다시 그린 덩어리에서 온
    /// 어두운 칸)은 바깥(투명에서 새 선을 넘지 않고 닿는 칸)이면 투명, 안쪽이면 가장 가까운 안쪽의 어둡지 않은 색으로 — 외곽선이 두 겹 남지 않는다.
    /// 다시 그리지 않는 덩어리(눈 같은 작은 점, 넓은 어두운 면)가 든 칸은 그대로 둔다. 바꾼 칸 수를 돌려준다.
    /// </summary>
    public static int Apply(PixelImage img, Tracing trace, Rectangle bounds, int imageWidth, CellLayout layout,
        Color32 outline, double darkness, CleanupSettings s)
    {
        int w = img.Width, h = img.Height;
        var blobs = trace.Blobs;
        if (w == 0 || h == 0 || blobs.Count == 0) return 0;
        var xc = layout.XCuts;
        var yc = layout.YCuts;
        double cell = ((xc[^1] - xc[0]) + (yc[^1] - yc[0])) / (double)Math.Max(1, w + h);
        double minArea = PixelateRanges.VectorBlobCells.Clamp(s.VectorBlobCells) * cell * cell;
        double band = trace.Thickness > 0 ? trace.Thickness * BandDepth : cell * BandDepth;
        double minSpur = Math.Max(cell, band);
        double tolerance = PixelateRanges.VectorTolerance.Clamp(s.VectorTolerance);
        int CellOf(int i)
        {
            int x = i % imageWidth, y = i / imageWidth;
            if (x < bounds.Left || y < bounds.Top || x >= bounds.Right || y >= bounds.Bottom) return -1;
            layout.Locate(x, y, out int c, out int r);
            return c < w && r < h ? r * w + c : -1;
        }

        var line = new bool[w * h];
        // 칸마다 다시 그린 덩어리 픽셀 수와 그대로 둘 덩어리 픽셀 수.
        var kept = new int[w * h];
        var keep = new int[w * h];
        var pts = new List<(double X, double Y)>();
        foreach (var blob in blobs)
        {
            bool redraw = blob.Band || (blob.Area >= minArea && blob.Width <= band);
            foreach (int i in blob.Pixels)
            {
                int c = CellOf(i);
                if (c < 0) continue;
                if (redraw) kept[c]++; else keep[c]++;
            }
            if (!redraw) continue;
            foreach (var chain in blob.Chains)
            {
                if (chain.Spur && chain.Points.Length < minSpur) continue;
                pts.Clear();
                foreach (int i in chain.Points)
                    pts.Add(layout.ToCell(i % imageWidth + 0.5, i / imageWidth + 0.5));
                var simple = Simplify(pts, tolerance);
                var path = new List<Point>();
                for (int k = 0; k < simple.Count; k++)
                {
                    var p = new Point((int)Math.Floor(simple[k].X), (int)Math.Floor(simple[k].Y));
                    if (k == 0) { path.Add(p); continue; }
                    var seg = Raster.Line(path[^1], p);
                    for (int t = 1; t < seg.Count; t++) path.Add(seg[t]);
                }
                foreach (var p in PixelPerfect.Filter(path))
                    if (p.X >= 0 && p.Y >= 0 && p.X < w && p.Y < h) line[p.Y * w + p.X] = true;
            }
        }
        RemoveCorners(line, w, h);

        // 옛 외곽선 칸: 다시 그린 덩어리에서 온 어두운 칸(작은 덩어리가 든 칸·새 선은 뺌).
        var dark = new Dictionary<uint, bool>();
        bool IsDark(Color32 c)
        {
            if (c.A == 0) return false;
            if (!dark.TryGetValue(c.Argb, out bool d)) dark[c.Argb] = d = OkLab.FromColor(c).L <= darkness;
            return d;
        }
        var old = new bool[w * h];
        for (int i = 0; i < w * h; i++) old[i] = !line[i] && kept[i] > keep[i] && IsDark(img.Pixels[i]);

        // 바깥: 둘레에서 투명·옛 외곽선 칸을 따라(상하좌우 — 8방향으로 이어진 새 선은 넘지 못함) 닿는 칸.
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
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Seed(i - 1);
            if (x < w - 1) Seed(i + 1);
            if (y > 0) Seed(i - w);
            if (y < h - 1) Seed(i + w);
        }

        int changed = 0;
        var px = img.Pixels;
        var pending = new List<int>();
        for (int i = 0; i < w * h; i++)
        {
            if (line[i])
            {
                if (px[i] != outline) { px[i] = outline; changed++; }
            }
            else if (old[i])
            {
                if (outside[i]) { px[i] = Color32.Transparent; changed++; }
                else pending.Add(i);
            }
        }
        // 안쪽 옛 외곽선 칸: 둘레에 이미 정해진 어둡지 않은 색이 있는 칸부터 그 중 가장 많은 색으로(바깥쪽으로 번져 나감).
        var counts = new Dictionary<Color32, int>();
        while (pending.Count > 0)
        {
            var fill = new List<(int I, Color32 C)>();
            foreach (int i in pending)
            {
                counts.Clear();
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx | dy) == 0 || nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (old[j] || line[j] || px[j].A == 0 || IsDark(px[j])) continue;
                        counts[px[j]] = counts.GetValueOrDefault(px[j]) + 1;
                    }
                if (counts.Count > 0) fill.Add((i, counts.MaxBy(kv => kv.Value).Key));
            }
            if (fill.Count == 0) break; // 둘레가 모두 외곽선(가는 틈) — 그대로 둔다.
            foreach (var (i, c) in fill)
            {
                px[i] = c;
                old[i] = false;
                changed++;
            }
            pending.RemoveAll(i => !old[i]);
        }
        return changed;
    }

    /// <summary>선 칸 중 가로 이웃 하나·세로 이웃 하나만 선이고 그 둘이 대각선으로 이어지는(L자 모서리) 칸을 뺀다 — 이어짐은 그대로.</summary>
    private static void RemoveCorners(bool[] line, int w, int h)
    {
        bool At(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && line[y * w + x];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!line[y * w + x]) continue;
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx | dy) != 0 && At(x + dx, y + dy)) n++;
                if (n != 2) continue;
                bool l = At(x - 1, y), r = At(x + 1, y), u = At(x, y - 1), d = At(x, y + 1);
                if ((l ^ r) && (u ^ d)) line[y * w + x] = false;
            }
    }

    /// <summary>더글러스–포이커 단순화(끝점은 남김, 닫힌 선은 가장 먼 점에서 둘로 나눠서).</summary>
    public static List<(double X, double Y)> Simplify(IReadOnlyList<(double X, double Y)> pts, double tolerance)
    {
        if (pts.Count <= 2 || tolerance <= 0) return [.. pts];
        var keepPt = new bool[pts.Count];
        keepPt[0] = keepPt[^1] = true;
        bool closed = pts[0] == pts[^1];
        if (closed)
        {
            int far = 0;
            double best = -1;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                double dx = pts[i].X - pts[0].X, dy = pts[i].Y - pts[0].Y, d = dx * dx + dy * dy;
                if (d > best) { best = d; far = i; }
            }
            keepPt[far] = true;
            Reduce(pts, 0, far, tolerance, keepPt);
            Reduce(pts, far, pts.Count - 1, tolerance, keepPt);
        }
        else Reduce(pts, 0, pts.Count - 1, tolerance, keepPt);
        var r = new List<(double, double)>();
        for (int i = 0; i < pts.Count; i++) if (keepPt[i]) r.Add(pts[i]);
        return r;
    }

    private static void Reduce(IReadOnlyList<(double X, double Y)> pts, int a, int b, double tol, bool[] keep)
    {
        var stack = new Stack<(int, int)>();
        stack.Push((a, b));
        while (stack.Count > 0)
        {
            var (i0, i1) = stack.Pop();
            if (i1 - i0 < 2) continue;
            double ax = pts[i0].X, ay = pts[i0].Y, bx = pts[i1].X, by = pts[i1].Y;
            double vx = bx - ax, vy = by - ay, len = Math.Sqrt(vx * vx + vy * vy);
            int far = -1;
            double best = tol;
            for (int i = i0 + 1; i < i1; i++)
            {
                double d = len < 1e-9
                    ? Math.Sqrt((pts[i].X - ax) * (pts[i].X - ax) + (pts[i].Y - ay) * (pts[i].Y - ay))
                    : Math.Abs(vx * (pts[i].Y - ay) - vy * (pts[i].X - ax)) / len;
                if (d > best) { best = d; far = i; }
            }
            if (far < 0) continue;
            keep[far] = true;
            stack.Push((i0, far));
            stack.Push((far, i1));
        }
    }
}
