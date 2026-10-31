using System.Drawing;
using System.Numerics;
using Chickxel.Core.Model;

namespace Chickxel.Core.Slicing;

/// <summary>아틀라스 배치 옵션.</summary>
/// <param name="MaxWidth">최대 가로(px). 0이면 자동(전체 넓이의 제곱근, 가장 넓은 스프라이트 이상).</param>
/// <param name="Spacing">스프라이트 사이 최소 간격(px).</param>
/// <param name="PowerOfTwo">결과 가로·세로를 2의 거듭제곱으로.</param>
/// <param name="Order">배치 순서.</param>
/// <param name="MinSize">결과 시트의 최소 크기(원래 위치 유지 시 원래 시트 크기).</param>
public sealed record AtlasPackOptions(
    int MaxWidth = 0,
    int Spacing = 0,
    bool PowerOfTwo = false,
    AtlasPackOrder Order = AtlasPackOrder.KeepOrder,
    Size MinSize = default);

/// <summary>배치 결과: 입력 순서대로의 사각형(왼쪽 위 원점)과 시트 크기.</summary>
public sealed record AtlasPackResult(IReadOnlyList<Rectangle> Rects, Size Size);

/// <summary>
/// 아틀라스 배치기. 고정 사각형(원래 위치 유지)을 먼저 놓고, 나머지는 순서대로 "가장 위, 그다음 가장 왼쪽" 빈 자리
/// (기존 사각형의 오른쪽·아래 모서리에서 만든 후보 점)에 최대 가로 안에서 놓는다. 같은 높이면 행 채우기와 같은 결과다.
/// </summary>
public static class AtlasPacker
{
    /// <param name="sizes">스프라이트 크기들.</param>
    /// <param name="options">옵션.</param>
    /// <param name="fixedRects">
    /// 스프라이트별 원래 칸(null = 없음). 스프라이트가 그 칸 안에 들어가고(가로·세로 ≤) 앞서 고정된 칸과 겹치지 않으면
    /// 그 왼쪽 위에 스프라이트 크기로 고정한다. 아니면 빈 곳에 새로 배치.
    /// </param>
    public static AtlasPackResult Pack(IReadOnlyList<Size> sizes, AtlasPackOptions options, IReadOnlyList<Rectangle?>? fixedRects = null)
    {
        int n = sizes.Count;
        int spacing = Math.Max(0, options.Spacing);
        var rects = new Rectangle[n];
        var placed = new List<Rectangle>(n);
        var free = new List<int>(n);

        for (int i = 0; i < n; i++)
        {
            if (sizes[i].Width < 1 || sizes[i].Height < 1)
                throw new ArgumentException(Chickxel.Core.Localization.DomainStrings.FormatAtlas_InvalidSpriteSize(size: sizes[i]), nameof(sizes));
            if (fixedRects is not null && i < fixedRects.Count && fixedRects[i] is { } r
                && r.X >= 0 && r.Y >= 0 && sizes[i].Width <= r.Width && sizes[i].Height <= r.Height)
            {
                var candidate = new Rectangle(r.Location, sizes[i]);
                if (!placed.Any(p => p.IntersectsWith(candidate)))
                {
                    rects[i] = candidate;
                    placed.Add(candidate);
                    continue;
                }
            }
            free.Add(i);
        }

        if (options.Order == AtlasPackOrder.LargestFirst)
            free = free.OrderByDescending(i => (long)sizes[i].Width * sizes[i].Height)
                .ThenByDescending(i => sizes[i].Height)
                .ThenBy(i => i)
                .ToList();

        int width = ChooseWidth(sizes, free, placed, options, spacing);
        foreach (var i in free)
        {
            rects[i] = PlaceOne(sizes[i], placed, width, spacing);
            placed.Add(rects[i]);
        }

        int w = Math.Max(options.MinSize.Width, placed.Count == 0 ? 1 : placed.Max(r => r.Right));
        int h = Math.Max(options.MinSize.Height, placed.Count == 0 ? 1 : placed.Max(r => r.Bottom));
        if (options.PowerOfTwo)
        {
            w = (int)BitOperations.RoundUpToPowerOf2((uint)w);
            h = (int)BitOperations.RoundUpToPowerOf2((uint)h);
        }
        return new AtlasPackResult(rects, new Size(w, h));
    }

    private static int ChooseWidth(IReadOnlyList<Size> sizes, List<int> free, List<Rectangle> placed, AtlasPackOptions options, int spacing)
    {
        int widest = free.Count == 0 ? 1 : free.Max(i => sizes[i].Width);
        int fixedRight = placed.Count == 0 ? 0 : placed.Max(r => r.Right);
        int width;
        if (options.MaxWidth > 0)
        {
            width = options.MaxWidth;
        }
        else
        {
            long area = free.Sum(i => (long)(sizes[i].Width + spacing) * (sizes[i].Height + spacing))
                        + placed.Sum(r => (long)r.Width * r.Height);
            width = Math.Max(options.MinSize.Width, (int)Math.Ceiling(Math.Sqrt(area)));
            if (options.PowerOfTwo) width = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(1, width));
        }
        return Math.Max(width, Math.Max(widest, fixedRight));
    }

    /// <summary>가장 위, 그다음 가장 왼쪽의 빈 자리(다른 사각형과 간격 이상 떨어짐).</summary>
    private static Rectangle PlaceOne(Size size, List<Rectangle> placed, int maxWidth, int spacing)
    {
        var xs = new SortedSet<int> { 0 };
        var ys = new SortedSet<int> { 0 };
        foreach (var p in placed)
        {
            xs.Add(p.Right + spacing);
            ys.Add(p.Bottom + spacing);
        }
        foreach (var y in ys)
            foreach (var x in xs)
            {
                if (x + size.Width > maxWidth) break; // xs는 오름차순
                var candidate = new Rectangle(x, y, size.Width, size.Height);
                var guard = Rectangle.Inflate(candidate, spacing, spacing);
                if (!placed.Any(p => p.IntersectsWith(guard))) return candidate;
            }
        // 가장 아래 줄 다음(항상 비어 있음)
        int bottom = placed.Count == 0 ? 0 : placed.Max(p => p.Bottom) + spacing;
        return new Rectangle(0, bottom, size.Width, size.Height);
    }
}
