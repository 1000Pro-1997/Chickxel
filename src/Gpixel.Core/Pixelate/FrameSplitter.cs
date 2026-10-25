using System.Drawing;

namespace Gpixel.Core.Pixelate;

/// <summary>원본에서 뽑은 프레임 하나: 자리(띠·칸 — 원래 위치 유지에 씀)와 그 안 스프라이트 범위(원본 px).</summary>
public sealed record FrameRegion(Rectangle Slot, Rectangle Bounds);

/// <summary>프레임 나누기 결과: 프레임들(읽는 순서)과 픽셀마다 속한 프레임 번호(−1 = 없음·버린 잡티).</summary>
public sealed record FrameSplit(IReadOnlyList<FrameRegion> Frames, int[] Labels);

/// <summary>
/// 배경을 지운 마스크를 프레임으로 나눈다. 자동: 완전히 빈 행으로 띠를 나누고, 띠마다 빈 열로, 조각마다 다시 빈 행으로 나눈 뒤
/// 작은 조각(가장 큰 조각의 <see cref="FrameSettings.MergeSmallFraction"/> 미만)은 가장 가까운 큰 조각에 합친다
/// (<see cref="FrameSettings.MergeDistance"/>보다 멀면 잡티로 버림). 격자: 같은 크기 칸마다 한 프레임(빈 칸은 뺌).
/// </summary>
public static class FrameSplitter
{
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
        var frames = new List<FrameRegion>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int x0 = c * w / cols, x1 = (c + 1) * w / cols, y0 = r * h / rows, y1 = (r + 1) * h / rows;
                var cell = Rectangle.FromLTRB(x0, y0, x1, y1);
                var b = Bounds(fg, w, cell);
                if (b.IsEmpty) continue;
                Label(fg, w, b, labels, frames.Count);
                frames.Add(new FrameRegion(cell, b));
            }
        return new FrameSplit(frames, labels);
    }

    private sealed class Piece(Rectangle bounds, Rectangle band, int count)
    {
        public Rectangle Bounds = bounds;
        public Rectangle Band = band;
        public int Count = count;
        public readonly List<Rectangle> Parts = [bounds];
    }

    private static FrameSplit SplitAuto(bool[] fg, int w, int h, FrameSettings s, int[] labels)
    {
        var pieces = new List<Piece>();
        foreach (var (y0, y1) in Runs(y => RowHas(fg, w, 0, w, y), 0, h))
        {
            var band = Rectangle.FromLTRB(0, y0, w, y1);
            foreach (var (x0, x1) in Runs(x => ColumnHas(fg, w, x, y0, y1), 0, w))
                foreach (var (sy0, sy1) in Runs(y => RowHas(fg, w, x0, x1, y), y0, y1))
                {
                    var r = Rectangle.FromLTRB(x0, sy0, x1, sy1);
                    var b = Bounds(fg, w, r);
                    if (b.IsEmpty) continue;
                    pieces.Add(new Piece(b, Rectangle.FromLTRB(x0, y0, x1, y1), Count(fg, w, b)));
                }
        }
        if (pieces.Count == 0) return new FrameSplit([], labels);

        int max = pieces.Max(p => p.Count);
        var big = pieces.Where(p => p.Count >= max * Math.Clamp(s.MergeSmallFraction, 0, 1)).ToList();
        foreach (var small in pieces.Except(big))
        {
            var target = big.MinBy(b => Gap(b.Bounds, small.Bounds))!;
            if (Gap(target.Bounds, small.Bounds) > Math.Max(0, s.MergeDistance)) continue;
            target.Bounds = Rectangle.Union(target.Bounds, small.Bounds);
            target.Band = Rectangle.Union(target.Band, small.Band);
            target.Parts.Add(small.Bounds);
            target.Count += small.Count;
        }

        // 읽는 순서: 위 띠부터(띠 위끝), 같은 띠면 왼쪽부터.
        var ordered = big.OrderBy(p => p.Band.Top).ThenBy(p => p.Bounds.Left).ToList();
        var frames = new List<FrameRegion>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            foreach (var part in ordered[i].Parts) Label(fg, w, part, labels, i);
            frames.Add(new FrameRegion(ordered[i].Band, ordered[i].Bounds));
        }
        return new FrameSplit(frames, labels);
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

    private static int Count(bool[] fg, int w, Rectangle r)
    {
        int n = 0;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
                if (fg[y * w + x]) n++;
        return n;
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

    private static void Label(bool[] fg, int w, Rectangle r, int[] labels, int index)
    {
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
                if (fg[y * w + x]) labels[y * w + x] = index;
    }
}
