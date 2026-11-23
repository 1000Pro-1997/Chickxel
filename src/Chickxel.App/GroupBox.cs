namespace Chickxel.App;

/// <summary>
/// 제목이 테두리 위에 걸린 묶음 상자(<see cref="Ui.Group"/>): 안쪽 칸은 <c>StackPanel[제목 줄, 내용]</c>이고(<see cref="Ui.Foldable"/>·제목 줄에 단추를 붙이는 곳이 이 모양을 씀),
/// 테두리는 직접 그린다 — 윗변은 제목 줄 가운데 높이를 지나고, 제목 줄의 글자·정보박스·접기 화살표·단추 자리마다 끊긴다.
/// </summary>
public sealed class GroupBox : Decorator
{
    /// <summary>윗변을 끊을 때 제목 줄 항목 양옆에 남기는 빈자리(px).</summary>
    private const double GapPad = 4;
    private const double Radius = 3;

    /// <summary>상자 안 바탕(없으면 칠하지 않음 — 투명이면 빈 곳도 눌림·대상 도움말이 잡음).</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<GroupBox>();

    static GroupBox() => AffectsRender<GroupBox>(BackgroundProperty);

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }
    private static readonly IPen Line = new Pen(AppTheme.BorderBrush, 1);

    protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        InvalidateVisual(); // 제목 줄이 바뀌거나 접히면 끊는 자리·아랫변이 달라짐
        return size;
    }

    public override void Render(DrawingContext g)
    {
        var b = new Rect(Bounds.Size);
        if (Background is { } fill) g.FillRectangle(fill, b, (float)Radius);
        if (Child is not Panel { Children.Count: > 0 } stack)
        {
            g.DrawRectangle(null, Line, b.Deflate(0.5), Radius, Radius);
            return;
        }
        var head = stack.Children[0];
        double line = Math.Round(head.TranslatePoint(new Avalonia.Point(0, head.Bounds.Height / 2), this)?.Y ?? 0) + 0.5;
        var box = new Rect(0.5, line, Math.Max(0, b.Width - 1), Math.Max(0, b.Height - 0.5 - line));
        var gaps = new GeometryGroup();
        foreach (var item in HeadItems(head))
        {
            // 늘어난 칸(도킹 칸을 채운 제목 글자 등)도 실제로 그린 폭만 — 원하는 폭에서 바깥 여백을 뺀 것
            double width = Math.Min(item.Bounds.Width, item.DesiredSize.Width - item.Margin.Left - item.Margin.Right);
            if (width > 0 && item.TranslatePoint(default, this) is { } p)
                gaps.Children.Add(new RectangleGeometry(new Rect(p.X - GapPad, line - 2, width + GapPad * 2, 4)));
        }
        using (g.PushGeometryClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(b.Inflate(1)), gaps)))
            g.DrawRectangle(null, Line, box, Radius, Radius);
    }

    /// <summary>제목 줄에서 테두리를 끊을 항목: 줄이 칸(가로 줄·도킹 칸 등)이면 그 안의 보이는 항목들, 아니면 그 자신.</summary>
    private static IEnumerable<Control> HeadItems(Control head)
    {
        if (!head.IsVisible) yield break;
        if (head is Panel panel)
        {
            foreach (var child in panel.Children)
                foreach (var item in HeadItems(child))
                    yield return item;
        }
        else yield return head;
    }
}
