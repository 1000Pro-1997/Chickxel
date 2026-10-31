using System.Drawing;

namespace Chickxel.Core.Model;

/// <summary>그룹 자동 배치 방향.</summary>
public enum GroupLayoutDirection
{
    /// <summary>가로 한 줄(왼쪽 → 오른쪽).</summary>
    Row,
    /// <summary>세로 한 줄(위 → 아래).</summary>
    Column,
    /// <summary>격자(<see cref="GroupLayout.Columns"/>열, 왼쪽 위부터 줄마다).</summary>
    Grid,
}

/// <summary>교차축 정렬(가로 줄이면 세로 위치, 세로 줄이면 가로 위치, 격자면 칸 안 위치).</summary>
public enum LayoutAlign
{
    Start,
    Center,
    End,
}

/// <summary>주축 배분: 앞·가운데·끝으로 모으기, 또는 고르게 펼치기(양 끝에 붙이고 사이를 같게).</summary>
public enum LayoutDistribution
{
    Start,
    Center,
    End,
    /// <summary>첫 부위는 영역 시작, 끝 부위는 영역 끝에 붙이고 남는 폭을 사이에 고르게(간격보다 좁아지면 간격으로 앞에서 모음).</summary>
    SpaceBetween,
}

/// <summary>자동 배치에 넣는 부위 순서.</summary>
public enum GroupLayoutOrder
{
    /// <summary>타임라인 순서(맨 위 부위가 먼저).</summary>
    TopFirst,
    /// <summary>거꾸로(맨 아래 부위가 먼저).</summary>
    BottomFirst,
}

/// <summary>
/// 그룹 자동 배치 설정(<see cref="Layer.AutoLayout"/>): 그룹 부위들의 위치(<see cref="Layer.PartOffset"/>)를 크기(<see cref="Layer.PartSize"/>)로
/// 늘 다시 계산한다(부위 추가·삭제·순서·크기·캔버스 크기·설정이 바뀌는 편집과 같은 실행취소 단계). 테두리 상자 기준이라 피벗은 그대로다.
/// 캔버스 밖으로 넘쳐도 자르지 않는다. 바꿀 때는 <see cref="SpriteProject.SetGroupLayout"/>로.
/// </summary>
public sealed record GroupLayout
{
    /// <summary>간격·여백 최댓값(px).</summary>
    public const int MaxSpacing = 4096;
    /// <summary>격자 열 수 최댓값.</summary>
    public const int MaxColumns = 256;

    public GroupLayoutDirection Direction { get; init; } = GroupLayoutDirection.Row;
    /// <summary>격자 열 수(격자일 때만, 1 이상).</summary>
    public int Columns { get; init; } = 2;
    /// <summary>교차축 정렬.</summary>
    public LayoutAlign Align { get; init; } = LayoutAlign.Center;
    /// <summary>주축 배분(격자는 가로·세로 모두).</summary>
    public LayoutDistribution Distribution { get; init; } = LayoutDistribution.Center;
    /// <summary>부위 사이 간격(px, 0 이상).</summary>
    public int Gap { get; init; } = 1;
    /// <summary>배치 영역 가장자리에서 띄울 여백(px, 0 이상).</summary>
    public int Margin { get; init; }
    public GroupLayoutOrder Order { get; init; } = GroupLayoutOrder.TopFirst;
    /// <summary>숨긴 부위도 자리를 차지하는지(기본 예 — 보이기를 켜고 꺼도 다른 부위가 움직이지 않음). 아니면 숨긴 부위는 제자리에 둔다.</summary>
    public bool IncludeHidden { get; init; } = true;
    /// <summary>배치 영역(캔버스 좌표). null = 캔버스 전체.</summary>
    public Rectangle? Area { get; init; }

    /// <summary>값을 허용 범위로 맞춘 사본(파일 읽기·대화상자 입력).</summary>
    public GroupLayout Normalized() => this with
    {
        Direction = Enum.IsDefined(Direction) ? Direction : GroupLayoutDirection.Row,
        Columns = Math.Clamp(Columns, 1, MaxColumns),
        Align = Enum.IsDefined(Align) ? Align : LayoutAlign.Center,
        Distribution = Enum.IsDefined(Distribution) ? Distribution : LayoutDistribution.Center,
        Gap = Math.Clamp(Gap, 0, MaxSpacing),
        Margin = Math.Clamp(Margin, 0, MaxSpacing),
        Order = Enum.IsDefined(Order) ? Order : GroupLayoutOrder.TopFirst,
        Area = Area is { Width: > 0, Height: > 0 } a ? a : null,
    };

    /// <summary>
    /// 배치 계산(순수 함수): <paramref name="sizes"/>(넣는 순서대로)의 왼쪽 위 위치를 <paramref name="canvas"/>(<see cref="Area"/>가 없을 때 쓰는 영역) 안에 돌려준다.
    /// 가로·세로는 한 줄로 이어 붙이고 교차축은 영역 기준으로 정렬한다. 격자는 열 폭 = 그 열에서 가장 넓은 부위, 줄 높이 = 그 줄에서 가장 높은 부위인 표로 놓고,
    /// 칸 안에서 <see cref="Align"/>으로 정렬하며 표 전체를 가로·세로 모두 <see cref="Distribution"/>으로 놓는다.
    /// </summary>
    public Point[] Arrange(IReadOnlyList<Size> sizes, Rectangle canvas)
    {
        var area = Area ?? canvas;
        int m = Math.Max(0, Margin);
        var inner = new Rectangle(area.X + m, area.Y + m, area.Width - 2 * m, area.Height - 2 * m);
        int gap = Math.Max(0, Gap);
        var result = new Point[sizes.Count];
        if (sizes.Count == 0) return result;
        switch (Direction)
        {
            case GroupLayoutDirection.Row:
            {
                var xs = Distribute(sizes.Select(s => s.Width).ToArray(), inner.X, inner.Width, gap);
                for (int i = 0; i < sizes.Count; i++) result[i] = new Point(xs[i], AlignIn(sizes[i].Height, inner.Y, inner.Height));
                break;
            }
            case GroupLayoutDirection.Column:
            {
                var ys = Distribute(sizes.Select(s => s.Height).ToArray(), inner.Y, inner.Height, gap);
                for (int i = 0; i < sizes.Count; i++) result[i] = new Point(AlignIn(sizes[i].Width, inner.X, inner.Width), ys[i]);
                break;
            }
            default:
            {
                int cols = Math.Clamp(Columns, 1, sizes.Count);
                int rows = (sizes.Count + cols - 1) / cols;
                var colW = new int[cols];
                var rowH = new int[rows];
                for (int i = 0; i < sizes.Count; i++)
                {
                    colW[i % cols] = Math.Max(colW[i % cols], sizes[i].Width);
                    rowH[i / cols] = Math.Max(rowH[i / cols], sizes[i].Height);
                }
                var xs = Distribute(colW, inner.X, inner.Width, gap);
                var ys = Distribute(rowH, inner.Y, inner.Height, gap);
                for (int i = 0; i < sizes.Count; i++)
                {
                    int c = i % cols, r = i / cols;
                    result[i] = new Point(AlignIn(sizes[i].Width, xs[c], colW[c]), AlignIn(sizes[i].Height, ys[r], rowH[r]));
                }
                break;
            }
        }
        return result;
    }

    /// <summary>길이 <paramref name="lengths"/>를 [start, start+extent) 안에 주축 배분대로 놓은 시작 위치들.</summary>
    private int[] Distribute(int[] lengths, int start, int extent, int gap)
    {
        int n = lengths.Length;
        long sum = lengths.Sum(l => (long)l);
        var pos = new int[n];
        if (Distribution == LayoutDistribution.SpaceBetween && n > 1 && extent - sum >= (long)gap * (n - 1))
        {
            // 남는 폭을 사이 n-1곳에 나누고, 나머지 1px씩은 앞쪽 사이부터 준다.
            long free = extent - sum;
            long each = free / (n - 1), rest = free % (n - 1);
            long x = start;
            for (int i = 0; i < n; i++)
            {
                pos[i] = (int)x;
                x += lengths[i] + each + (i < rest ? 1 : 0);
            }
            return pos;
        }
        long total = sum + (long)gap * (n - 1);
        long at = Distribution switch
        {
            LayoutDistribution.Center => start + FloorHalf(extent - total),
            LayoutDistribution.End => start + extent - total,
            _ => start,
        };
        for (int i = 0; i < n; i++)
        {
            pos[i] = (int)at;
            at += lengths[i] + gap;
        }
        return pos;
    }

    private int AlignIn(int length, int start, int extent) => Align switch
    {
        LayoutAlign.Center => start + (int)FloorHalf(extent - length),
        LayoutAlign.End => start + extent - length,
        _ => start,
    };

    private static long FloorHalf(long v) => v >= 0 ? v / 2 : -((-v + 1) / 2);
}
