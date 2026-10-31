using Chickxel.Core.Model;

namespace Chickxel.Core.Pixelate;

/// <summary>배경 지우기 결과: 전경 마스크(true = 스프라이트), 배경색(색으로 지웠으면), 쓴 허용 거리, 기존 알파를 썼는지.</summary>
public sealed record BackgroundResult(bool[] Foreground, Color32? Color, double Tolerance, bool UsedAlpha)
{
    public int ForegroundCount => Foreground.Count(f => f);
}

/// <summary>
/// 배경 지우기. 자동이면 테두리가 대부분 투명할 때 기존 알파를, 아니면 테두리에서 가장 많은 색을 배경으로 본다.
/// 색으로 지울 때 기본은 테두리와 이어진 픽셀만(4방향 채우기) — 스프라이트 안쪽의 같은 색이나 배경에 닿은 거의 검은 외곽선은
/// 배경색과 허용 거리보다 멀기만 하면 남는다. 허용 거리 자동은 테두리 잡음(배경색과의 거리 상위 분위)에서 정한다.
/// </summary>
public static class BackgroundRemoval
{
    public static BackgroundResult Run(PixelImage image, BackgroundSettings s)
    {
        var result = Mask(image, s);
        if (s.Mode != BackgroundMode.None && s.RemoveStickerBorder) RemoveStickerBorder(image, result.Foreground);
        if (s.RemoveSpecks > 0) RemoveSpecks(result.Foreground, image.Width, image.Height, s.RemoveSpecks);
        return result;
    }

    /// <summary>스티커 테두리로 볼 밝기(OKLab L)와 최대 채도.</summary>
    private const float StickerLight = 0.82f, StickerChroma = 0.045f;

    /// <summary>
    /// 스티커 테두리 지우기(저절로 찾음): 전경 가장자리(투명과 맞닿은 픽셀)의 <c>60%</c> 이상이 밝은 무채색(흰 띠)이면, 가장자리에서 시작해
    /// 밝은 무채색 픽셀을 따라 안쪽으로(4방향) 띠 두께의 3배 깊이까지(팔과 창 사이처럼 띠로 막힌 틈도) 지우고, 띠와 외곽선 사이에 번진 밝은 회색 한 겹도 지운다.
    /// 지운 뒤 새 가장자리가 절반 이상 어둡지 않으면(외곽선이 없는 흰 그림) 되돌린다. 지운 픽셀 수.
    /// </summary>
    public static int RemoveStickerBorder(PixelImage image, bool[] fg)
    {
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        var labCache = new Dictionary<uint, Lab>();
        Lab LabOf(int i)
        {
            uint key = px[i].Argb | 0xFF000000u;
            if (!labCache.TryGetValue(key, out var l)) labCache[key] = l = OkLab.FromColor(px[i]);
            return l;
        }
        bool Light(int i) { var l = LabOf(i); return l.L >= StickerLight && MathF.Sqrt(l.A * l.A + l.B * l.B) <= StickerChroma; }
        bool Edge(int i)
        {
            int x = i % w, y = i / w;
            return x == 0 || y == 0 || x == w - 1 || y == h - 1 || !fg[i - 1] || !fg[i + 1] || !fg[i - w] || !fg[i + w];
        }
        int edges = 0, lightEdges = 0;
        var starts = new List<int>();
        for (int i = 0; i < fg.Length; i++)
        {
            if (!fg[i] || !Edge(i)) continue;
            edges++;
            if (Light(i)) { lightEdges++; starts.Add(i); }
        }
        if (edges == 0 || lightEdges < edges * 0.6) return 0;

        // 띠 두께: 가로·세로 줄마다 투명 → 전경으로 들어서며 밝은 무채색이 이어지는 길이의 75% 분위.
        var runs = new List<int>();
        for (int y = 0; y < h; y += 2)
            for (int x = 1; x < w; x++)
            {
                int i = y * w + x;
                if (!fg[i] || fg[i - 1]) continue;
                int k = 0;
                while (x + k < w && fg[i + k] && Light(i + k)) k++;
                if (k > 0 && x + k < w && fg[i + k]) runs.Add(k);
            }
        if (runs.Count < 10) return 0;
        runs.Sort();
        int depth = (int)Math.Ceiling(runs[runs.Count * 3 / 4] * 3.0) + 1;

        var dist = new int[fg.Length];
        Array.Fill(dist, -1);
        var queue = new Queue<int>();
        foreach (int i in starts) { dist[i] = 0; queue.Enqueue(i); }
        var removed = new List<int>();
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            removed.Add(i);
            if (dist[i] >= depth) continue;
            int x = i % w, y = i / w;
            foreach (int j in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1 })
            {
                if (j < 0 || dist[j] >= 0 || !fg[j] || !Light(j)) continue;
                dist[j] = dist[i] + 1;
                queue.Enqueue(j);
            }
        }
        foreach (int i in removed) fg[i] = false;
        // 띠와 외곽선 사이의 번진 밝은 회색 한 겹.
        var fringe = new List<int>();
        foreach (int i in removed)
        {
            int x = i % w, y = i / w;
            foreach (int j in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1 })
            {
                if (j < 0 || !fg[j]) continue;
                var l = LabOf(j);
                if (l.L >= 0.6f && MathF.Sqrt(l.A * l.A + l.B * l.B) <= StickerChroma) fringe.Add(j);
            }
        }
        foreach (int i in fringe) fg[i] = false;

        int newEdges = 0, dark = 0;
        for (int i = 0; i < fg.Length; i++)
        {
            if (!fg[i] || !Edge(i)) continue;
            newEdges++;
            if (LabOf(i).L <= 0.5f) dark++;
        }
        if (newEdges == 0 || dark < newEdges * 0.5)
        {
            foreach (int i in removed) fg[i] = true;
            foreach (int i in fringe) fg[i] = true;
            return 0;
        }
        return removed.Count + fringe.Count;
    }

    /// <summary>8방향으로 이어진 전경 조각 중 <paramref name="minSize"/> 픽셀보다 작은 것을 배경으로.</summary>
    public static void RemoveSpecks(bool[] fg, int w, int h, int minSize)
    {
        var seen = new bool[fg.Length];
        var stack = new Stack<int>();
        var piece = new List<int>();
        for (int start = 0; start < fg.Length; start++)
        {
            if (!fg[start] || seen[start]) continue;
            piece.Clear();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                piece.Add(i);
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                        int j = ny * w + nx;
                        if (!fg[j] || seen[j]) continue;
                        seen[j] = true;
                        stack.Push(j);
                    }
            }
            if (piece.Count < minSize)
                foreach (int i in piece) fg[i] = false;
        }
    }

    private static BackgroundResult Mask(PixelImage image, BackgroundSettings s)
    {
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        var fg = new bool[px.Length];
        int alphaCut = Math.Clamp(s.AlphaThreshold, 1, 255);
        for (int i = 0; i < px.Length; i++) fg[i] = px[i].A >= alphaCut;

        if (s.Mode == BackgroundMode.None) return new BackgroundResult(fg, null, 0, true);

        var border = BorderPixels(image);
        if (s.Mode == BackgroundMode.Auto && s.KeepExistingAlpha)
        {
            // 테두리의 1/4 이상이 투명하거나, 테두리에 투명이 있고 그림의 1% 이상이 투명하면 이미 배경이 지워진 그림(스프라이트가 가장자리에 닿아도).
            int transparentBorder = border.Count(c => c.A < alphaCut);
            int transparent = fg.Count(f => !f);
            if (transparentBorder >= border.Count * 0.25 || (transparentBorder > 0 && transparent >= px.Length * 0.01))
                return new BackgroundResult(fg, null, 0, true);
        }

        var bg = s.Mode == BackgroundMode.PickColor
            ? PixelateSettings.ParseColor(s.Color, Color32.Black)
            : DominantColor(border.Where(c => c.A >= alphaCut));
        double tol = s.AutoTolerance ? AutoTolerance(border.Where(c => c.A >= alphaCut), bg) : Math.Max(0, s.Tolerance);
        double tol2 = tol * tol;
        bool IsBg(Color32 c)
        {
            if (c.A < alphaCut) return true;
            int dr = c.R - bg.R, dg = c.G - bg.G, db = c.B - bg.B;
            return dr * dr + dg * dg + db * db <= tol2;
        }

        if (!s.ConnectedOnly)
        {
            for (int i = 0; i < px.Length; i++)
                if (fg[i] && IsBg(px[i])) fg[i] = false;
            return new BackgroundResult(fg, bg, tol, false);
        }

        // 테두리에서 시작하는 4방향 채우기.
        var visited = new bool[px.Length];
        var stack = new Stack<int>();
        void Seed(int x, int y)
        {
            int i = y * w + x;
            if (visited[i] || !IsBg(px[i])) return;
            visited[i] = true;
            stack.Push(i);
        }
        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            fg[i] = false;
            int x = i % w, y = i / w;
            if (x > 0) Seed(x - 1, y);
            if (x < w - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < h - 1) Seed(x, y + 1);
        }
        RemoveFringe(image, fg, bg);
        return new BackgroundResult(fg, bg, tol, false);
    }

    /// <summary>
    /// 번진 가장자리 한 겹: 배경과 맞닿은 전경 픽셀 중 배경색에 (그 반대쪽 2px 안쪽 전경 색보다) 더 가까운 것(배경과 그림 색이 섞인 픽셀)을 배경으로.
    /// </summary>
    private static void RemoveFringe(PixelImage image, bool[] fg, Color32 bg)
    {
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        static int D2(Color32 a, Color32 b) { int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B; return dr * dr + dg * dg + db * db; }
        var drop = new List<int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!fg[i]) continue;
                foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    int bx = x + dx, by = y + dy, ix = x - 2 * dx, iy = y - 2 * dy;
                    if ((uint)bx >= (uint)w || (uint)by >= (uint)h || fg[by * w + bx]) continue;
                    if ((uint)ix >= (uint)w || (uint)iy >= (uint)h || !fg[iy * w + ix]) continue;
                    if (D2(px[i], bg) < D2(px[i], px[iy * w + ix])) { drop.Add(i); break; }
                }
            }
        foreach (int i in drop) fg[i] = false;
    }

    /// <summary>배경을 지운 사본(배경 픽셀은 완전 투명, 나머지는 불투명).</summary>
    public static PixelImage Apply(PixelImage image, bool[] foreground)
    {
        var copy = new PixelImage(image.Width, image.Height);
        var src = image.Pixels;
        var dst = copy.Pixels;
        for (int i = 0; i < src.Length; i++)
            if (foreground[i]) dst[i] = src[i].WithAlpha(255);
        return copy;
    }

    /// <summary>배경을 남기되 한 색으로 고르게 한 사본(배경 픽셀 = <paramref name="background"/>, 모두 불투명).</summary>
    public static PixelImage Flatten(PixelImage image, bool[] foreground, Color32 background)
    {
        var copy = new PixelImage(image.Width, image.Height);
        var src = image.Pixels;
        var dst = copy.Pixels;
        for (int i = 0; i < src.Length; i++)
            dst[i] = foreground[i] ? src[i].WithAlpha(255) : background.WithAlpha(255);
        return copy;
    }

    internal static List<Color32> BorderPixels(PixelImage image)
    {
        int w = image.Width, h = image.Height;
        var list = new List<Color32>(2 * (w + h));
        for (int x = 0; x < w; x++)
        {
            list.Add(image[x, 0]);
            if (h > 1) list.Add(image[x, h - 1]);
        }
        for (int y = 1; y < h - 1; y++)
        {
            list.Add(image[0, y]);
            if (w > 1) list.Add(image[w - 1, y]);
        }
        return list;
    }

    /// <summary>가장 많은 색: 채널마다 16단계로 묶어 가장 큰 묶음의 평균(잡음이 있어도 한 색).</summary>
    internal static Color32 DominantColor(IEnumerable<Color32> colors)
    {
        var bins = new Dictionary<int, (int N, long R, long G, long B)>();
        foreach (var c in colors)
        {
            int key = (c.R >> 4) << 8 | (c.G >> 4) << 4 | c.B >> 4;
            var b = bins.GetValueOrDefault(key);
            bins[key] = (b.N + 1, b.R + c.R, b.G + c.G, b.B + c.B);
        }
        if (bins.Count == 0) return Color32.Black;
        var best = bins.Values.MaxBy(b => b.N);
        return new Color32((byte)(best.R / best.N), (byte)(best.G / best.N), (byte)(best.B / best.N));
    }

    /// <summary>테두리 픽셀 중 배경색에 가까운 것(거리 80 미만 — 테두리에 닿은 스프라이트는 뺌)의 90% 거리 × 1.5 + 8 (8~64).</summary>
    internal static double AutoTolerance(IEnumerable<Color32> border, Color32 bg)
    {
        var d = border.Select(c =>
        {
            int dr = c.R - bg.R, dg = c.G - bg.G, db = c.B - bg.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }).Where(v => v < 80).OrderBy(v => v).ToList();
        if (d.Count == 0) return 24;
        double p90 = d[(int)((d.Count - 1) * 0.9)];
        return Math.Clamp(p90 * 1.5 + 8, 8, 64);
    }
}
