using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Slicing;

/// <summary>격자 잘라내기 – 칸 크기 기준(여백 offset, 간격 padding, 빈 칸 건너뛰기).</summary>
public static class GridSlicer
{
    /// <summary>
    /// 이미지 안에 완전히 들어가는 칸만 왼쪽 위부터 행 순서로 만든다.
    /// <paramref name="skipEmpty"/>면 모두 투명한 칸은 뺀다.
    /// </summary>
    public static List<Rectangle> Slice(PixelImage image, GridSettings grid, bool skipEmpty = true)
    {
        var rects = Slice(image.Width, image.Height, grid);
        if (skipEmpty) rects.RemoveAll(image.IsRegionEmpty);
        return rects;
    }

    /// <summary>이미지 없이 크기만으로 칸을 만든다(빈 칸 판단 없음).</summary>
    public static List<Rectangle> Slice(int width, int height, GridSettings grid)
    {
        int cw = Math.Max(1, grid.CellW), ch = Math.Max(1, grid.CellH);
        int stepX = cw + Math.Max(0, grid.PadX), stepY = ch + Math.Max(0, grid.PadY);
        var rects = new List<Rectangle>();
        for (int y = Math.Max(0, grid.OffsetY); y + ch <= height; y += stepY)
            for (int x = Math.Max(0, grid.OffsetX); x + cw <= width; x += stepX)
                rects.Add(new Rectangle(x, y, cw, ch));
        return rects;
    }
}

/// <summary>격자 잘라내기 – 열·행 개수 기준(칸 크기 = 이미지 크기 / 개수, 나머지는 버림).</summary>
public static class CountSlicer
{
    public static List<Rectangle> Slice(PixelImage image, int columns, int rows, bool skipEmpty = false)
    {
        var grid = ToGrid(image.Width, image.Height, columns, rows);
        return GridSlicer.Slice(image, grid, skipEmpty);
    }

    /// <summary>열·행 개수를 같은 결과의 칸 크기 격자 설정으로 바꾼다.</summary>
    public static GridSettings ToGrid(int width, int height, int columns, int rows)
    {
        columns = Math.Clamp(columns, 1, width);
        rows = Math.Clamp(rows, 1, height);
        return new GridSettings { CellW = width / columns, CellH = height / rows };
    }
}

/// <summary>
/// 자동 잘라내기: 투명이 아닌 픽셀의 8방향 연결 덩어리마다 칸 하나. 위→아래 행, 행 안에서 왼쪽→오른쪽 순.
/// </summary>
public static class AutoSlicer
{
    /// <param name="minSize">가로·세로 중 긴 쪽이 이보다 작은 덩어리(티끌)는 버린다.</param>
    /// <param name="mergeOverlapping">감싸는 사각형이 겹치는 덩어리는 하나로 합친다(떨어진 눈·장식 등).</param>
    public static List<Rectangle> Slice(PixelImage image, int minSize = 1, bool mergeOverlapping = true)
    {
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        var visited = new bool[px.Length];
        var rects = new List<Rectangle>();
        var stack = new Stack<int>();
        for (int start = 0; start < px.Length; start++)
        {
            if (visited[start] || px[start].A == 0) continue;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            visited[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % w, y = i / w;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                for (int ny = Math.Max(0, y - 1); ny <= Math.Min(h - 1, y + 1); ny++)
                    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(w - 1, x + 1); nx++)
                    {
                        int j = ny * w + nx;
                        if (visited[j] || px[j].A == 0) continue;
                        visited[j] = true;
                        stack.Push(j);
                    }
            }
            rects.Add(Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1));
        }

        if (mergeOverlapping) MergeOverlapping(rects);
        rects.RemoveAll(r => Math.Max(r.Width, r.Height) < Math.Max(1, minSize));
        return SliceOrder.Sort(rects);
    }

    private static void MergeOverlapping(List<Rectangle> rects)
    {
        bool merged;
        do
        {
            merged = false;
            for (int i = 0; i < rects.Count && !merged; i++)
                for (int j = i + 1; j < rects.Count; j++)
                {
                    if (!rects[i].IntersectsWith(rects[j])) continue;
                    rects[i] = Rectangle.Union(rects[i], rects[j]);
                    rects.RemoveAt(j);
                    merged = true;
                    break;
                }
        } while (merged);
    }
}

/// <summary>칸 정렬·행 나누기.</summary>
public static class SliceOrder
{
    /// <summary>
    /// 사각형을 행으로 묶는다: 위쪽부터, 칸의 세로 중심이 현재 행 범위 안이거나 행의 세로 중심이
    /// 칸 안에 있으면 같은 행. 각 행은 왼쪽→오른쪽으로 정렬된 원래 목록의 인덱스들이다.
    /// </summary>
    public static List<List<int>> GroupRows(IReadOnlyList<Rectangle> rects)
    {
        var order = Enumerable.Range(0, rects.Count).OrderBy(i => rects[i].Y).ThenBy(i => rects[i].X).ToList();
        var rows = new List<List<int>>();
        int rowTop = 0, rowBottom = 0;
        foreach (var i in order)
        {
            var r = rects[i];
            int centerY = r.Y + r.Height / 2;
            int rowCenter = (rowTop + rowBottom) / 2;
            bool sameRow = rows.Count > 0
                           && ((centerY >= rowTop && centerY < rowBottom) || (rowCenter >= r.Top && rowCenter < r.Bottom));
            if (sameRow)
            {
                rows[^1].Add(i);
                rowTop = Math.Min(rowTop, r.Top);
                rowBottom = Math.Max(rowBottom, r.Bottom);
                continue;
            }
            rows.Add([i]);
            rowTop = r.Top;
            rowBottom = r.Bottom;
        }
        foreach (var row in rows) row.Sort((a, b) => rects[a].X.CompareTo(rects[b].X));
        return rows;
    }

    /// <summary>위→아래 행, 행 안에서 왼쪽→오른쪽 순으로 정렬한 새 목록.</summary>
    public static List<Rectangle> Sort(IReadOnlyList<Rectangle> rects)
        => GroupRows(rects).SelectMany(row => row.Select(i => rects[i])).ToList();
}
