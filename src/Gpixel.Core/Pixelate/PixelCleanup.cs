using Gpixel.Core.Model;

namespace Gpixel.Core.Pixelate;

/// <summary>줄인 그림 정리: 외톨이 픽셀, 1px 구멍, 외곽선 1px로 얇게, 외곽선(하나로·닫기), L자 겹 픽셀. 모두 그 자리에서 바꾼다.</summary>
public static class PixelCleanup
{
    private static readonly (int X, int Y)[] N8 = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];
    private static readonly (int X, int Y)[] N4 = [(0, -1), (-1, 0), (1, 0), (0, 1)];

    /// <summary>
    /// 설정대로 모두(순서: 외톨이 → 구멍 → 얇게 → 외곽선 → L자). <paramref name="outline"/>은 외곽선 색(가장 어두운 색).
    /// 얇게: 외곽선만 얇게는 최대 3겹, 크기를 줄여서·벡터로 다시 그리기는 남은 한 겹만(벡터 선은 정리 전에 <see cref="VectorOutline.Apply"/>가 그림 —
    /// 새 선 안쪽에 붙은 어두운 그늘 칸을 밝은 이웃 색으로). 벡터로 다시 그리기는 끝에 바깥 둘레의 L자 겹 픽셀도 뺀다(<see cref="RemoveRingCorners"/>).
    /// </summary>
    public static void Run(PixelImage img, CleanupSettings s, Color32 outline, double outlineDarkness)
    {
        if (s.RemoveOrphans) RemoveOrphans(img, s.OrphanMaxContrast);
        if (s.FillHoles) FillHoles(img);
        if (s.OutlineFit != OutlineFit.Off) ThinOutline(img, outlineDarkness, s.OutlineFit == OutlineFit.Thin ? 3 : 1);
        if (s.Outline != OutlineMode.None) Outline(img, s.Outline, outline, outlineDarkness);
        if (s.ReduceJaggies) ReduceJaggies(img, outline);
        if (s.OutlineFit == OutlineFit.Vector) RemoveRingCorners(img, outlineDarkness);
    }

    /// <summary>
    /// 바깥 둘레(투명과 상하좌우로 맞닿은 어두운 픽셀)의 볼록한 L자 모서리 — 둘레 이웃이 가로 하나·세로 하나뿐(둘은 대각선으로 이어짐)이고 나머지 두 상하좌우가
    /// 투명한 픽셀 — 를 투명으로(픽셀 퍼펙트 선의 계단). 정리 단계에서 다시 생긴 것까지 없어질 때까지. 뺀 수를 돌려준다.
    /// </summary>
    public static int RemoveRingCorners(PixelImage img, double darkness)
    {
        var dark = DarkTest(darkness);
        bool Ring(int x, int y) => dark(img.Get(x, y)) && IsEdge(img, x, y);
        int removed = 0;
        bool any = true;
        while (any)
        {
            any = false;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                {
                    if (!Ring(x, y)) continue;
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((dx | dy) != 0 && Ring(x + dx, y + dy)) n++;
                    bool l = Ring(x - 1, y), r = Ring(x + 1, y), u = Ring(x, y - 1), d = Ring(x, y + 1);
                    if (n != 2 || !(l ^ r) || !(u ^ d)) continue;
                    if (img.Get(l ? x + 1 : x - 1, y).A != 0 || img.Get(x, u ? y + 1 : y - 1).A != 0) continue;
                    img[x, y] = Color32.Transparent;
                    removed++;
                    any = true;
                }
        }
        return removed;
    }

    /// <summary>외곽선 밝기(<paramref name="darkness"/> — 팔레트의 외곽선 색 모으기와 같은 기준) 이하인 불투명 색인지.</summary>
    private static Func<Color32, bool> DarkTest(double darkness)
    {
        var cache = new Dictionary<uint, bool>();
        return c =>
        {
            if (c.A == 0) return false;
            if (!cache.TryGetValue(c.Argb, out bool d)) cache[c.Argb] = d = OkLab.FromColor(c).L <= darkness;
            return d;
        };
    }

    /// <summary>
    /// 바깥 외곽선 두께 재기: 투명(또는 그림 밖)과 상하좌우로 맞닿은 어두운 픽셀 수와, 그중 투명 반대쪽(안쪽) 이웃도 바깥이 아닌 어두운 픽셀이면서
    /// 그 안쪽 픽셀 옆(외곽선을 따라가는 방향)에도 어두운 픽셀이 있어 외곽선 띠가 2px 이상으로 겹친 픽셀 수. 외곽선에서 안쪽으로 뻗은 1px 선(띠가 아님 —
    /// <see cref="ThinOutline"/>도 남김: 허리띠·옷 주름이 외곽선에 닿는 T자 자리)은 겹침이 아니다.
    /// </summary>
    public static (int Outline, int Thick) OutlineThickness(PixelImage img, double darkness)
    {
        var dark = DarkTest(darkness);
        int outline = 0, thick = 0;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                if (!dark(img[x, y]) || !IsEdge(img, x, y)) continue;
                outline++;
                foreach (var (dx, dy) in N4)
                {
                    if (img.Get(x + dx, y + dy).A != 0) continue;
                    int ix = x - dx, iy = y - dy;
                    if (!dark(img.Get(ix, iy)) || IsEdge(img, ix, iy)) continue;
                    // 안쪽 픽셀의 옆(방향에 수직) 이웃 중 어두운 것이 있으면 띠.
                    if (dark(img.Get(ix + dy, iy + dx)) || dark(img.Get(ix - dy, iy - dx))) { thick++; break; }
                }
            }
        return (outline, thick);
    }

    /// <summary>
    /// 바깥 외곽선을 1px로: 바깥 외곽선(투명과 맞닿은 어두운 픽셀)에 상하좌우로 붙은 안쪽 어두운 픽셀을 둘레의 밝은(어둡지 않은) 색 중 가장 많은 색으로 바꾼다
    /// (3×3에 밝은 색이 없으면 5×5에서). 바꾼 픽셀 안쪽으로 다시 붙은 어두운 픽셀도 최대 <paramref name="maxLayers"/>겹까지 벗겨 낸다(두꺼운 외곽선).
    /// 바깥 외곽선은 건드리지 않아 끊기지 않고, 외곽선에서 안쪽으로 뻗은 가는 선(둘레에서 2px 이하로 이어진 어두운 픽셀)은 잇는 자리를 남겨 떨어지지 않는다.
    /// 외곽선에 닿지 않은 안쪽 어두운 덩어리(눈 등)는 건드리지 않는다. 바꾼 수를 돌려준다.
    /// </summary>
    public static int ThinOutline(PixelImage img, double darkness, int maxLayers = 3)
    {
        var dark = DarkTest(darkness);
        int w = img.Width, h = img.Height;
        var outer = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                outer[y * w + x] = dark(img[x, y]) && IsEdge(img, x, y);
        var peeled = new bool[w * h];
        var front = outer;
        bool At(bool[] a, int x, int y) => img.Contains(x, y) && a[y * w + x];
        int changed = 0;
        var counts = new Dictionary<Color32, int>();
        var ring = new List<(int X, int Y)>(8);
        var outerRing = new List<(int X, int Y)>(8);
        for (int layer = 0; layer < maxLayers; layer++)
        {
            // 이번 겹: 바깥이 아닌 어두운 픽셀 중 앞 겹(바깥 외곽선 또는 앞에서 바꾼 픽셀)에 상하좌우로 붙은 것.
            var candidate = new bool[w * h];
            int any = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (outer[y * w + x] || !dark(img[x, y])) continue;
                    if (!(At(front, x - 1, y) || At(front, x + 1, y) || At(front, x, y - 1) || At(front, x, y + 1))) continue;
                    candidate[y * w + x] = true;
                    any++;
                }
            if (any == 0) break;
            var next = new bool[w * h];
            int before = changed;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!candidate[y * w + x]) continue;
                    ring.Clear();
                    outerRing.Clear();
                    counts.Clear();
                    foreach (var (dx, dy) in N8)
                    {
                        int nx = x + dx, ny = y + dy;
                        var n = img.Get(nx, ny);
                        if (dark(n))
                        {
                            if (outer[ny * w + nx]) outerRing.Add((dx, dy));
                            else if (!candidate[ny * w + nx]) ring.Add((dx, dy));
                        }
                        else if (n.A != 0 && !peeled[ny * w + nx]) counts[n] = counts.GetValueOrDefault(n) + 1;
                    }
                    if (layer == 0 && IsLineJoint(ring, outerRing)) continue;
                    if (counts.Count == 0)
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < 2) continue;
                                var n = img.Get(x + dx, y + dy);
                                if (n.A != 0 && !dark(n)) counts[n] = counts.GetValueOrDefault(n) + 1;
                            }
                    if (counts.Count == 0) continue;
                    img[x, y] = counts.MaxBy(kv => kv.Value).Key;
                    peeled[y * w + x] = next[y * w + x] = true;
                    changed++;
                }
            if (changed == before) break;
            front = next;
        }
        return changed;
    }

    /// <summary>
    /// 외곽선과 안쪽 가는 선을 잇는 자리인지: 둘레의 안쪽 어두운 픽셀(바깥 외곽선·후보 아님) 중 2px 이하로 이어진 덩어리가 둘레의 바깥 외곽선과 닿지 않으면
    /// (이 픽셀을 바꾸면 그 선이 외곽선에서 떨어짐) true. 넓은 덩어리는 두꺼운 외곽선의 안쪽 겹으로 보고 벗겨 낸다.
    /// </summary>
    private static bool IsLineJoint(List<(int X, int Y)> inner, List<(int X, int Y)> outer)
    {
        if (inner.Count == 0) return false;
        static bool Adjacent((int X, int Y) a, (int X, int Y) b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) == 1;
        var seen = new bool[inner.Count];
        var group = new List<int>();
        for (int s = 0; s < inner.Count; s++)
        {
            if (seen[s]) continue;
            group.Clear();
            group.Add(s);
            seen[s] = true;
            for (int g = 0; g < group.Count; g++)
                for (int b = 0; b < inner.Count; b++)
                    if (!seen[b] && Adjacent(inner[group[g]], inner[b])) { seen[b] = true; group.Add(b); }
            if (group.Count <= 2 && !group.Any(i => outer.Any(o => Adjacent(inner[i], o)))) return true;
        }
        return false;
    }

    /// <summary>8이웃 어디에도 같은 색이 없는 픽셀 수(점수용).</summary>
    public static int CountOrphans(PixelImage img)
    {
        int n = 0;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                var c = img[x, y];
                if (c.A == 0) continue;
                bool same = false;
                foreach (var (dx, dy) in N8)
                    if (img.Get(x + dx, y + dy) == c) { same = true; break; }
                if (!same) n++;
            }
        return n;
    }

    /// <summary>
    /// 외톨이(8이웃에 같은 색 없음)를 이웃에 가장 많은 색으로 — 그 색과의 대비가 <paramref name="maxContrast"/> 이하일 때만(눈동자 같은 점은 남김).
    /// 이웃이 모두 투명한 한 점은 지운다. 바꾼 수를 돌려준다.
    /// </summary>
    public static int RemoveOrphans(PixelImage img, double maxContrast)
    {
        var src = img.Clone();
        int changed = 0;
        var counts = new Dictionary<Color32, int>();
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                var c = src[x, y];
                if (c.A == 0) continue;
                counts.Clear();
                bool same = false;
                int opaque = 0;
                foreach (var (dx, dy) in N8)
                {
                    var n = src.Get(x + dx, y + dy);
                    if (n == c) { same = true; break; }
                    if (n.A == 0) continue;
                    opaque++;
                    counts[n] = counts.GetValueOrDefault(n) + 1;
                }
                if (same) continue;
                if (opaque == 0)
                {
                    img[x, y] = Color32.Transparent;
                    changed++;
                    continue;
                }
                var to = counts.MaxBy(kv => kv.Value).Key;
                if (Lab.Distance(OkLab.FromColor(c), OkLab.FromColor(to)) > maxContrast) continue;
                img[x, y] = to;
                changed++;
            }
        return changed;
    }

    /// <summary>상하좌우가 모두 칠해진 투명 픽셀을 그 넷 중 가장 많은 색으로.</summary>
    public static int FillHoles(PixelImage img)
    {
        var src = img.Clone();
        int changed = 0;
        var counts = new Dictionary<Color32, int>();
        for (int y = 1; y < img.Height - 1; y++)
            for (int x = 1; x < img.Width - 1; x++)
            {
                if (src[x, y].A != 0) continue;
                counts.Clear();
                bool hole = true;
                foreach (var (dx, dy) in N4)
                {
                    var n = src[x + dx, y + dy];
                    if (n.A == 0) { hole = false; break; }
                    counts[n] = counts.GetValueOrDefault(n) + 1;
                }
                if (!hole) continue;
                img[x, y] = counts.MaxBy(kv => kv.Value).Key;
                changed++;
            }
        return changed;
    }

    private static bool IsEdge(PixelImage img, int x, int y)
    {
        foreach (var (dx, dy) in N4)
            if (img.Get(x + dx, y + dy).A == 0) return true;
        return false;
    }

    /// <summary>
    /// 외곽선: 하나로 = 가장자리의 어두운 픽셀(L ≤ <paramref name="darkness"/> + 0.1)을 외곽선 색으로.
    /// 닫기 = 그 위에 외곽선이 아닌 가장자리 픽셀과 맞닿은 투명 칸(상하좌우)에 외곽선 색을 더한다.
    /// </summary>
    public static void Outline(PixelImage img, OutlineMode mode, Color32 outline, double darkness)
    {
        if (mode == OutlineMode.None) return;
        var src = img.Clone();
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                var c = src[x, y];
                if (c.A == 0 || c == outline || !IsEdge(src, x, y)) continue;
                if (OkLab.FromColor(c).L <= darkness + 0.1) img[x, y] = outline;
            }
        if (mode != OutlineMode.EnsureClosed) return;
        var unified = img.Clone();
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                if (unified[x, y].A != 0) continue;
                foreach (var (dx, dy) in N4)
                {
                    var n = unified.Get(x + dx, y + dy);
                    if (n.A == 0 || n == outline) continue;
                    img[x, y] = outline;
                    break;
                }
            }
    }

    /// <summary>
    /// 외곽선 색의 L자 모서리(가로 이웃 하나·세로 이웃 하나만 외곽선이고 그 사이 대각선은 외곽선이 아님)를 뺀다 —
    /// 계단 선이 1px 대각선이 된다. 빠진 자리는 안쪽(외곽선이 아닌 칠해진 이웃) 색, 없으면 투명.
    /// </summary>
    public static int ReduceJaggies(PixelImage img, Color32 outline)
    {
        int changed = 0;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                if (img[x, y] != outline) continue;
                bool l = img.Get(x - 1, y) == outline, r = img.Get(x + 1, y) == outline;
                bool u = img.Get(x, y - 1) == outline, d = img.Get(x, y + 1) == outline;
                if ((l ? 1 : 0) + (r ? 1 : 0) != 1 || (u ? 1 : 0) + (d ? 1 : 0) != 1) continue;
                int hx = l ? -1 : 1, vy = u ? -1 : 1;
                if (img.Get(x + hx, y + vy) == outline) continue;
                // 반대쪽 두 이웃(외곽선 아님) 중 칠해진 색.
                var a = img.Get(x - hx, y);
                var b = img.Get(x, y - vy);
                img[x, y] = a.A != 0 ? a : b;
                changed++;
            }
        return changed;
    }

    /// <summary>
    /// 프레임 떨림 줄이기: 앞뒤 프레임(처음·끝은 서로 이어짐)이 같은 불투명 색인데 이 프레임만 대비 <paramref name="maxContrast"/> 이하로 다른 픽셀을 그 색으로.
    /// 프레임 크기가 모두 같을 때만.
    /// </summary>
    public static int TemporalMajority(IReadOnlyList<PixelImage> frames, double maxContrast)
    {
        if (frames.Count < 3 || frames.Any(f => f.Size != frames[0].Size)) return 0;
        var src = frames.Select(f => f.Clone()).ToList();
        int n = src.Count, changed = 0;
        for (int i = 0; i < n; i++)
        {
            var prev = src[(i + n - 1) % n].Pixels;
            var next = src[(i + 1) % n].Pixels;
            var cur = src[i].Pixels;
            var dst = frames[i].Pixels;
            for (int p = 0; p < cur.Length; p++)
            {
                if (prev[p] != next[p] || prev[p].A == 0 || cur[p].A == 0 || cur[p] == prev[p]) continue;
                if (Lab.Distance(OkLab.FromColor(cur[p]), OkLab.FromColor(prev[p])) > maxContrast) continue;
                dst[p] = prev[p];
                changed++;
            }
        }
        return changed;
    }
}
