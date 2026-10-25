using System.Drawing;

namespace Gpixel.Core.Pixelate;

/// <summary>
/// 원본에서 뽑은 프레임 하나: 자리(띠·칸 — 원래 위치 유지에 씀)와 그 안 스프라이트 범위(원본 px). 격자 나누기에서는 칸 선을 넘은 손끝·발끝도
/// 그 스프라이트에 붙으므로 범위가 자리(칸) 밖으로 나갈 수 있다(원래 위치 유지는 그만큼 자리째 밀어 자르지 않음 — <see cref="Pixelator"/>).
/// </summary>
public sealed record FrameRegion(Rectangle Slot, Rectangle Bounds);

/// <summary>프레임 나누기 결과: 프레임들(읽는 순서)과 픽셀마다 속한 프레임 번호(−1 = 없음·버린 잡티).</summary>
public sealed record FrameSplit(IReadOnlyList<FrameRegion> Frames, int[] Labels);

/// <summary>
/// 배경을 지운 마스크를 프레임으로 나눈다.
/// <para>
/// 자동: 완전히 빈 행으로 띠를 나누고, 띠마다 빈 열로, 조각마다 다시 빈 행으로 나눈다. 그다음 너무 넓은 조각(빈 열 없이 맞닿거나 가로로 겹친
/// 스프라이트 둘 이상)을 가장 얇은 세로 줄에서 가른다: 기대 프레임 수(<see cref="FrameSettings.ExpectedFrames"/>)가 있으면 큰 조각이 그보다 적을 때만,
/// 기대 너비(큰 조각 너비 합 ÷ 기대 수)의 <see cref="WideExpected"/>배 이상인 조각을; 없으면 다른 큰 조각이 둘 이상일 때 그 너비 중앙값의
/// <see cref="WideMedian"/>배 이상인 조각을 — (너비 ÷ 기준 너비, 반올림)조각으로. 자를 곳은 고른 경계 ±<see cref="CutWindow"/>×기준 너비 안에서
/// 전경 픽셀이 가장 적은 열이고, 그 열이 조각 열 중앙값의 <see cref="ThinCut"/> 이하일 때만 자른다(한 스프라이트 몸통은 가르지 않음).
/// 가른 조각 안 픽셀은 격자와 같이 덩어리째 나눈다(<see cref="AssignCells"/>). 마지막으로 작은 조각(가장 큰 조각의
/// <see cref="FrameSettings.MergeSmallFraction"/> 미만)은 가장 가까운 큰 조각에 합친다(<see cref="FrameSettings.MergeDistance"/>보다 멀면 잡티로 버림).
/// </para>
/// <para>
/// 격자: 같은 크기 칸마다 한 프레임(빈 칸은 뺌). 칸 선으로 픽셀을 자르지 않고 8방향으로 이어진 덩어리째 칸에 준다(<see cref="AssignCells"/>) —
/// 칸 선을 넘은 손끝·발끝이 이웃 프레임 가장자리에 조각으로 남지 않는다. 프레임 범위는 받은 덩어리를 합친 사각형, 자리는 칸 그대로.
/// </para>
/// </summary>
public static class FrameSplitter
{
    /// <summary>큰 덩어리를 한 칸에 통째로 줄 때 가장 많이 든 칸의 최소 비율.</summary>
    private const double MajorityShare = 0.5;
    /// <summary>두 번째로 많이 든 칸이 이 비율 이상이면 큰 덩어리를 통째로 주지 않고 자른다 — 6:4로 맞닿은 두 스프라이트.</summary>
    private const double SecondShare = 0.25;
    /// <summary>가장 큰 덩어리의 이 비율 이상이면 큰 덩어리(프레임을 이루는 스프라이트), 미만이면 작은 조각(잡티·떨어진 손끝·발끝).</summary>
    private const double LargeShare = 0.25;
    /// <summary>자를 열·행을 찾는 범위: 고른 경계 ± 이 비율 × 칸(기준) 너비.</summary>
    private const double CutWindow = 0.25;
    /// <summary>자동, 기대 프레임 수가 있을 때: 기대 너비의 이 배 이상인 조각을 가른다.</summary>
    private const double WideExpected = 1.5;
    /// <summary>자동, 기대 프레임 수가 없을 때: 다른 큰 조각 너비 중앙값의 이 배 이상인 조각을 가른다.</summary>
    private const double WideMedian = 1.8;
    /// <summary>자동: 자를 열의 전경 픽셀 수가 조각 열(빈 열 제외) 중앙값의 이 비율 이하일 때만 가른다.</summary>
    private const double ThinCut = 1.0 / 3;

    public static FrameSplit Run(bool[] fg, int width, int height, FrameSettings s)
    {
        var labels = new int[fg.Length];
        Array.Fill(labels, -1);
        return s.Mode switch
        {
            FrameSplitMode.Grid => SplitGrid(fg, width, height, Math.Max(1, s.Columns), Math.Max(1, s.Rows), labels),
            FrameSplitMode.Auto => SplitAuto(fg, width, height, s, labels),
            _ => SplitSingle(fg, width, height, labels),
        };
    }

    private static FrameSplit SplitSingle(bool[] fg, int w, int h, int[] labels)
    {
        var all = new Rectangle(0, 0, w, h);
        var b = Bounds(fg, w, all);
        if (b.IsEmpty) return new FrameSplit([], labels);
        Label(fg, w, b, labels, 0);
        return new FrameSplit([new FrameRegion(all, b)], labels);
    }

    private static FrameSplit SplitGrid(bool[] fg, int w, int h, int cols, int rows, int[] labels)
    {
        var xs = new int[cols + 1];
        var ys = new int[rows + 1];
        for (int c = 0; c <= cols; c++) xs[c] = c * w / cols;
        for (int r = 0; r <= rows; r++) ys[r] = r * h / rows;
        // 칸 번호(행 우선)를 먼저 적고, 빈 칸을 뺀 프레임 번호로 바꾼다.
        var (bounds, _) = AssignCells(fg, null, 0, w, xs, ys, labels);
        var index = new int[bounds.Length];
        var frames = new List<FrameRegion>();
        for (int k = 0; k < bounds.Length; k++)
        {
            if (bounds[k].IsEmpty) { index[k] = -1; continue; }
            index[k] = frames.Count;
            int c = k % cols, r = k / cols;
            frames.Add(new FrameRegion(Rectangle.FromLTRB(xs[c], ys[r], xs[c + 1], ys[r + 1]), bounds[k]));
        }
        for (int i = 0; i < labels.Length; i++)
            if (labels[i] >= 0) labels[i] = index[labels[i]];
        return new FrameSplit(frames, labels);
    }

    /// <summary>자동 나누기 조각: 번호(나누는 동안 픽셀마다 적는 주인), 스프라이트 범위, 띠(자리), 픽셀 수.</summary>
    private sealed class Piece(int id, Rectangle bounds, Rectangle band, int count)
    {
        public readonly int Id = id;
        public Rectangle Bounds = bounds;
        public Rectangle Band = band;
        public int Count = count;
    }

    private static FrameSplit SplitAuto(bool[] fg, int w, int h, FrameSettings s, int[] labels)
    {
        // 나누는 동안 labels에는 조각 번호를 적고 마지막에 프레임 번호로 바꾼다.
        var pieces = new List<Piece>();
        foreach (var (y0, y1) in Runs(y => RowHas(fg, w, 0, w, y), 0, h))
            foreach (var (x0, x1) in Runs(x => ColumnHas(fg, w, x, y0, y1), 0, w))
                foreach (var (sy0, sy1) in Runs(y => RowHas(fg, w, x0, x1, y), y0, y1))
                {
                    var b = Bounds(fg, w, Rectangle.FromLTRB(x0, sy0, x1, sy1));
                    if (b.IsEmpty) continue;
                    var p = new Piece(pieces.Count, b, Rectangle.FromLTRB(x0, y0, x1, y1), 0);
                    p.Count = Label(fg, w, b, labels, p.Id);
                    pieces.Add(p);
                }
        if (pieces.Count == 0) return new FrameSplit([], labels);

        double fraction = Math.Clamp(s.MergeSmallFraction, 0, 1);
        int nextId = pieces.Count;
        SplitWide(fg, w, pieces, labels, Math.Max(0, s.ExpectedFrames), fraction, ref nextId);

        int max = pieces.Max(p => p.Count);
        var big = pieces.Where(p => p.Count >= max * fraction).ToList();
        foreach (var small in pieces.Except(big))
        {
            var target = big.MinBy(b => Gap(b.Bounds, small.Bounds))!;
            if (Gap(target.Bounds, small.Bounds) > Math.Max(0, s.MergeDistance))
            {
                Relabel(labels, w, small.Bounds, small.Id, -1);
                continue;
            }
            Relabel(labels, w, small.Bounds, small.Id, target.Id);
            target.Bounds = Rectangle.Union(target.Bounds, small.Bounds);
            target.Band = Rectangle.Union(target.Band, small.Band);
            target.Count += small.Count;
        }

        // 읽는 순서: 위 띠부터(띠 위끝), 같은 띠면 왼쪽부터.
        var ordered = big.OrderBy(p => p.Band.Top).ThenBy(p => p.Bounds.Left).ToList();
        var map = new int[nextId];
        Array.Fill(map, -1);
        var frames = new List<FrameRegion>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            map[ordered[i].Id] = i;
            frames.Add(new FrameRegion(ordered[i].Band, ordered[i].Bounds));
        }
        for (int i = 0; i < labels.Length; i++)
            if (labels[i] >= 0) labels[i] = map[labels[i]];
        return new FrameSplit(frames, labels);
    }

    /// <summary>
    /// 너무 넓은 큰 조각을 가른다(클래스 설명): 기대 수가 있으면 큰 조각이 그보다 적은 동안 가장 넓은 조각을, 없으면 중앙값 규칙에 맞는 조각을.
    /// 가를 수 없는 조각(얇은 열이 없음·한 덩어리가 대부분)은 그대로 두고 다음으로.
    /// </summary>
    private static void SplitWide(bool[] fg, int w, List<Piece> pieces, int[] owner, int expected, double fraction, ref int nextId)
    {
        var kept = new HashSet<Piece>();
        while (true)
        {
            int max = pieces.Max(p => p.Count);
            var big = pieces.Where(p => p.Count >= max * fraction).ToList();
            if (expected > 0 ? big.Count >= expected : big.Count < 3) return;
            Piece? wide = null;
            double unit = 0;
            foreach (var p in big.Where(p => !kept.Contains(p)).OrderByDescending(p => p.Bounds.Width))
            {
                if (expected > 0)
                {
                    unit = big.Sum(q => q.Bounds.Width) / (double)expected;
                    if (p.Bounds.Width >= WideExpected * unit) wide = p;
                    break;
                }
                var others = big.Where(q => q != p).Select(q => q.Bounds.Width).Order().ToList();
                unit = others.Count % 2 == 1 ? others[others.Count / 2] : (others[others.Count / 2 - 1] + others[others.Count / 2]) / 2.0;
                if (p.Bounds.Width >= WideMedian * unit) { wide = p; break; }
            }
            if (wide is null || unit <= 0) return;
            int parts = Math.Max(2, (int)Math.Round(wide.Bounds.Width / unit, MidpointRounding.AwayFromZero));
            if (TrySplit(fg, w, owner, wide, parts, unit, ref nextId) is not { } split)
            {
                kept.Add(wide);
                continue;
            }
            int at = pieces.IndexOf(wide);
            pieces.RemoveAt(at);
            pieces.InsertRange(at, split);
        }
    }

    /// <summary>
    /// 조각 <paramref name="p"/>를 <paramref name="parts"/>개로: 고른 경계마다 ±<see cref="CutWindow"/>×<paramref name="unit"/> 안에서 전경이 가장 적은 열
    /// (같으면 고른 경계에 가까운 열)을 고르고, 그 열이 얇지 않으면(<see cref="ThinCut"/>) 가르지 않는다. 그 열들을 칸 선으로 덩어리째 나눈다.
    /// 나눈 조각이 둘 미만이면 null(바꾸지 않음).
    /// </summary>
    private static List<Piece>? TrySplit(bool[] fg, int w, int[] owner, Piece p, int parts, double unit, ref int nextId)
    {
        var r = p.Bounds;
        var hist = new int[r.Width];
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int i = y * w + x;
                if (fg[i] && owner[i] == p.Id) hist[x - r.Left]++;
            }
        var filled = hist.Where(v => v > 0).Order().ToArray();
        if (filled.Length == 0) return null;
        double thin = filled[filled.Length / 2] * ThinCut;

        var xs = new int[parts + 1];
        xs[0] = r.Left;
        xs[parts] = r.Right;
        for (int j = 1; j < parts; j++)
        {
            double target = r.Left + j * (double)r.Width / parts;
            int lo = Math.Max(xs[j - 1] + 1, (int)Math.Ceiling(target - CutWindow * unit));
            int hi = Math.Min(r.Right - 1, (int)Math.Floor(target + CutWindow * unit));
            if (lo > hi) return null;
            int best = lo;
            for (int x = lo + 1; x <= hi; x++)
            {
                int a = hist[x - r.Left], b = hist[best - r.Left];
                if (a < b || (a == b && Math.Abs(x - target) < Math.Abs(best - target))) best = x;
            }
            if (hist[best - r.Left] > thin) return null;
            xs[j] = best;
        }

        var cellOf = new int[fg.Length];
        var (bounds, counts) = AssignCells(fg, owner, p.Id, w, xs, [r.Top, r.Bottom], cellOf);
        if (bounds.Count(b => !b.IsEmpty) < 2) return null;
        var ids = new int[parts];
        var split = new List<Piece>(parts);
        for (int k = 0; k < parts; k++)
        {
            if (bounds[k].IsEmpty) continue;
            ids[k] = nextId++;
            split.Add(new Piece(ids[k], bounds[k], Rectangle.FromLTRB(bounds[k].Left, p.Band.Top, bounds[k].Right, p.Band.Bottom), counts[k]));
        }
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int i = y * w + x;
                if (fg[i] && owner[i] == p.Id) owner[i] = ids[cellOf[i]];
            }
        return split;
    }

    /// <summary>
    /// 칸 선 <paramref name="xs"/>×<paramref name="ys"/>(바깥 경계 포함) 안의 마스크 픽셀(전경이고, <paramref name="owner"/>가 있으면 그 값이 <paramref name="ownerId"/>)에
    /// 칸 번호(행 우선)를 <paramref name="cellOf"/>에 적는다 — 8방향으로 이어진 덩어리째:
    /// <list type="bullet">
    /// <item>큰 덩어리(가장 큰 덩어리의 <see cref="LargeShare"/> 이상 — 스프라이트): 가장 많이 든 칸이 <see cref="MajorityShare"/> 이상이고 두 번째 칸이
    /// <see cref="SecondShare"/> 미만이면 그 칸에 통째로(칸 선을 넘은 손끝·발끝도 함께). 아니면(맞닿은 두 스프라이트·칸보다 큰 배경 찌꺼기) 칸 선을 그 덩어리의
    /// 가장 얇은 열·행(선 ±<see cref="CutWindow"/>칸 안)으로 옮겨 픽셀마다 나눈다.</item>
    /// <item>작은 덩어리(잡티·떨어진 손끝·발끝·효과): 가장 많이 든 칸이 큰 덩어리를 받지 못했으면 그 칸에(작은 스프라이트 자체), 아니면 범위(사각형)가 가장 가까운
    /// 칸에 — 같으면 가장 많이 든 칸.</item>
    /// </list>
    /// 마스크 밖 픽셀은 건드리지 않는다. 칸마다 받은 픽셀의 범위(없으면 빈 사각형)와 픽셀 수를 돌려준다.
    /// </summary>
    private static (Rectangle[] Bounds, int[] Counts) AssignCells(bool[] fg, int[]? owner, int ownerId, int w, int[] xs, int[] ys, int[] cellOf)
    {
        int cols = xs.Length - 1, rows = ys.Length - 1, cells = cols * rows;
        var region = Rectangle.FromLTRB(xs[0], ys[0], xs[^1], ys[^1]);
        var bounds = new Rectangle[cells];
        var counts = new int[cells];
        var (id, size, box) = Blobs(fg, owner, ownerId, w, region);
        int nb = size.Count;
        if (nb == 0) return (bounds, counts);

        var colOf = new int[region.Width];
        var rowOf = new int[region.Height];
        for (int x = region.Left; x < region.Right; x++) colOf[x - region.Left] = Index(xs, x);
        for (int y = region.Top; y < region.Bottom; y++) rowOf[y - region.Top] = Index(ys, y);

        // 덩어리마다 그 범위가 걸친 칸들에 든 픽셀 수.
        var c0 = new int[nb];
        var r0 = new int[nb];
        var span = new int[nb];
        var off = new int[nb + 1];
        for (int b = 0; b < nb; b++)
        {
            var bb = box[b];
            c0[b] = colOf[bb.Left - region.Left];
            r0[b] = rowOf[bb.Top - region.Top];
            span[b] = colOf[bb.Right - 1 - region.Left] - c0[b] + 1;
            off[b + 1] = off[b] + span[b] * (rowOf[bb.Bottom - 1 - region.Top] - r0[b] + 1);
        }
        var hits = new int[off[nb]];
        for (int y = region.Top; y < region.Bottom; y++)
            for (int x = region.Left; x < region.Right; x++)
            {
                int b = id[y * w + x];
                if (b >= 0) hits[off[b] + (rowOf[y - region.Top] - r0[b]) * span[b] + colOf[x - region.Left] - c0[b]]++;
            }

        const int Cut = -1, Small = -2;
        int largest = size.Max();
        var top = new int[nb];
        var whole = new int[nb];
        for (int b = 0; b < nb; b++)
        {
            int best = 0, second = 0, at = 0;
            for (int k = off[b]; k < off[b + 1]; k++)
            {
                int v = hits[k];
                if (v > best) { second = best; best = v; at = k - off[b]; }
                else if (v > second) second = v;
            }
            top[b] = (r0[b] + at / span[b]) * cols + c0[b] + at % span[b];
            whole[b] = size[b] < largest * LargeShare ? Small
                : best >= size[b] * MajorityShare && second < size[b] * SecondShare ? top[b]
                : Cut;
        }

        // 뚜렷한 주인 칸이 없는 큰 덩어리: 그 덩어리 안에서 칸 선마다 가장 얇은 열·행으로 옮긴 선.
        var cuts = new Dictionary<int, (int[] Xs, int[] Ys)>();
        for (int b = 0; b < nb; b++)
        {
            if (whole[b] != Cut) continue;
            var bb = box[b];
            var colHist = new int[bb.Width];
            var rowHist = new int[bb.Height];
            for (int y = bb.Top; y < bb.Bottom; y++)
                for (int x = bb.Left; x < bb.Right; x++)
                    if (id[y * w + x] == b) { colHist[x - bb.Left]++; rowHist[y - bb.Top]++; }
            cuts[b] = (Thinnest(xs, colHist, bb.Left), Thinnest(ys, rowHist, bb.Top));
        }

        var minX = new int[cells];
        var minY = new int[cells];
        var maxX = new int[cells];
        var maxY = new int[cells];
        Array.Fill(minX, int.MaxValue);
        Array.Fill(minY, int.MaxValue);
        Array.Fill(maxX, -1);
        Array.Fill(maxY, -1);
        for (int y = region.Top; y < region.Bottom; y++)
            for (int x = region.Left; x < region.Right; x++)
            {
                int i = y * w + x, b = id[i];
                if (b < 0 || whole[b] == Small) continue;
                int k = whole[b] >= 0 ? whole[b] : Index(cuts[b].Ys, y) * cols + Index(cuts[b].Xs, x);
                cellOf[i] = k;
                counts[k]++;
                if (x < minX[k]) minX[k] = x;
                if (x > maxX[k]) maxX[k] = x;
                if (y < minY[k]) minY[k] = y;
                if (y > maxY[k]) maxY[k] = y;
            }
        for (int k = 0; k < cells; k++)
            if (maxX[k] >= 0) bounds[k] = Rectangle.FromLTRB(minX[k], minY[k], maxX[k] + 1, maxY[k] + 1);

        // 작은 덩어리는 큰 덩어리로 정한 범위를 보고 고른다(서로의 범위는 보지 않음 — 순서와 상관없게).
        var had = bounds.Select(b => !b.IsEmpty).ToArray();
        var target = new int[nb];
        for (int b = 0; b < nb; b++)
        {
            if (whole[b] != Small) continue;
            int best = top[b];
            if (had[best])
            {
                int gap = Gap(bounds[best], box[b]);
                for (int k = 0; k < cells; k++)
                {
                    if (!had[k]) continue;
                    int g = Gap(bounds[k], box[b]);
                    if (g < gap) { gap = g; best = k; }
                }
            }
            target[b] = best;
        }
        for (int b = 0; b < nb; b++)
        {
            if (whole[b] != Small) continue;
            int k = target[b];
            var bb = box[b];
            for (int y = bb.Top; y < bb.Bottom; y++)
                for (int x = bb.Left; x < bb.Right; x++)
                    if (id[y * w + x] == b) cellOf[y * w + x] = k;
            counts[k] += size[b];
            bounds[k] = bounds[k].IsEmpty ? bb : Rectangle.Union(bounds[k], bb);
        }
        return (bounds, counts);
    }

    /// <summary>
    /// 덩어리 하나의 칸 선: 안쪽 선마다 그 덩어리가 걸쳐 있으면 선 ±<see cref="CutWindow"/>×(이웃 칸 평균 너비) 안에서 덩어리 픽셀이 가장 적은 줄
    /// (같으면 원래 선에 가까운 줄)로 옮긴다. <paramref name="hist"/>는 <paramref name="start"/>부터의 줄마다 덩어리 픽셀 수.
    /// </summary>
    private static int[] Thinnest(int[] lines, int[] hist, int start)
    {
        var moved = (int[])lines.Clone();
        int end = start + hist.Length;
        for (int i = 1; i < lines.Length - 1; i++)
        {
            int line = lines[i];
            if (line <= start || line >= end) continue;
            int window = (int)(CutWindow * (lines[i + 1] - lines[i - 1]) / 2);
            int lo = Math.Max(Math.Max(start + 1, moved[i - 1] + 1), line - window);
            int hi = Math.Min(Math.Min(end - 1, lines[i + 1] - 1), line + window);
            int best = line;
            for (int v = lo; v <= hi; v++)
            {
                int a = hist[v - start], b = hist[best - start];
                if (a < b || (a == b && Math.Abs(v - line) < Math.Abs(best - line))) best = v;
            }
            moved[i] = best;
        }
        return moved;
    }

    /// <summary>8방향으로 이어진 마스크 픽셀 덩어리(<paramref name="within"/> 안): 픽셀마다 덩어리 번호(−1 = 없음), 덩어리마다 픽셀 수·범위.</summary>
    private static (int[] Id, List<int> Size, List<Rectangle> Box) Blobs(bool[] fg, int[]? owner, int ownerId, int w, Rectangle within)
    {
        var id = new int[fg.Length];
        Array.Fill(id, -1);
        var size = new List<int>();
        var box = new List<Rectangle>();
        var stack = new Stack<int>();
        bool In(int i) => fg[i] && (owner is null || owner[i] == ownerId);
        for (int y = within.Top; y < within.Bottom; y++)
            for (int x = within.Left; x < within.Right; x++)
            {
                int start = y * w + x;
                if (id[start] >= 0 || !In(start)) continue;
                int b = size.Count, n = 0, x0 = x, x1 = x, y0 = y, y1 = y;
                id[start] = b;
                stack.Push(start);
                while (stack.TryPop(out int i))
                {
                    n++;
                    int px = i % w, py = i / w;
                    if (px < x0) x0 = px;
                    if (px > x1) x1 = px;
                    if (py < y0) y0 = py;
                    if (py > y1) y1 = py;
                    for (int ny = Math.Max(within.Top, py - 1); ny <= Math.Min(within.Bottom - 1, py + 1); ny++)
                        for (int nx = Math.Max(within.Left, px - 1); nx <= Math.Min(within.Right - 1, px + 1); nx++)
                        {
                            int j = ny * w + nx;
                            if (id[j] < 0 && In(j)) { id[j] = b; stack.Push(j); }
                        }
                }
                size.Add(n);
                box.Add(Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1));
            }
        return (id, size, box);
    }

    /// <summary><paramref name="lines"/>(바깥 경계 포함, 오름차순)로 나눈 칸 중 <paramref name="v"/>가 든 칸 번호.</summary>
    private static int Index(int[] lines, int v)
    {
        int i = 0;
        while (i < lines.Length - 2 && v >= lines[i + 1]) i++;
        return i;
    }

    /// <summary>두 사각형 사이 빈 거리(겹치면 0) — 가로·세로 중 큰 쪽.</summary>
    private static int Gap(Rectangle a, Rectangle b)
    {
        int dx = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right));
        int dy = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));
        return Math.Max(dx, dy);
    }

    private static IEnumerable<(int Start, int End)> Runs(Func<int, bool> filled, int from, int to)
    {
        int start = -1;
        for (int i = from; i < to; i++)
        {
            if (filled(i)) { if (start < 0) start = i; }
            else if (start >= 0) { yield return (start, i); start = -1; }
        }
        if (start >= 0) yield return (start, to);
    }

    private static bool RowHas(bool[] fg, int w, int x0, int x1, int y)
    {
        for (int x = x0; x < x1; x++) if (fg[y * w + x]) return true;
        return false;
    }

    private static bool ColumnHas(bool[] fg, int w, int x, int y0, int y1)
    {
        for (int y = y0; y < y1; y++) if (fg[y * w + x]) return true;
        return false;
    }

    internal static Rectangle Bounds(bool[] fg, int w, Rectangle within)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = within.Top; y < within.Bottom; y++)
            for (int x = within.Left; x < within.Right; x++)
            {
                if (!fg[y * w + x]) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
        return x1 < 0 ? Rectangle.Empty : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
    }

    /// <summary><paramref name="r"/> 안 전경 픽셀에 <paramref name="index"/>를 적고 그 수를 돌려준다.</summary>
    private static int Label(bool[] fg, int w, Rectangle r, int[] labels, int index)
    {
        int n = 0;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
                if (fg[y * w + x]) { labels[y * w + x] = index; n++; }
        return n;
    }

    /// <summary><paramref name="r"/> 안에서 <paramref name="from"/>이 적힌 픽셀을 <paramref name="to"/>로.</summary>
    private static void Relabel(int[] labels, int w, Rectangle r, int from, int to)
    {
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
                if (labels[y * w + x] == from) labels[y * w + x] = to;
    }
}
