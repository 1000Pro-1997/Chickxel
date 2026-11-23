using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 색 칸 목록(공용): 편집기 팔레트 칸과 같은 크기(14px + 2px 간격)로 너비에 맞춰 줄을 바꿔 늘어놓는다. 마우스를 올린 색은 흰 테두리와
/// 풍선 도움말(#RRGGBB)로 보이고 <see cref="HoverChanged"/>로 알린다. 팔레트 웹의 고른 팔레트, 3D 모델 가져오기의 쓴 색이 쓴다.
/// </summary>
public sealed class ColorSwatches : Control
{
    /// <summary>편집기 팔레트 칸과 같은 크기(14px + 2px 간격).</summary>
    private const double Cell = 14, GapPx = 2;
    private IReadOnlyList<Color32> _colors = [];
    private int _hover = -1;

    public ColorSwatches() => TipPlacement.FollowPointer(this);

    /// <summary>한 줄에 <paramref name="columns"/>칸이 들어가는 너비(오른쪽 스크롤바 자리 14px 포함).</summary>
    public static double WidthFor(int columns) => GapPx + columns * (Cell + GapPx) + 14;

    /// <summary>마우스를 올린 색이 바뀜(null = 칸 밖).</summary>
    public event EventHandler<Color32?>? HoverChanged;

    public IReadOnlyList<Color32> Colors => _colors;

    public void SetColors(IReadOnlyList<Color32> colors)
    {
        _colors = colors;
        _hover = -1;
        ToolTip.SetTip(this, null);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private int Columns(double width) => Math.Max(1, (int)((width - GapPx) / (Cell + GapPx)));

    protected override Avalonia.Size MeasureOverride(Avalonia.Size available)
    {
        double width = double.IsInfinity(available.Width) ? 600 : available.Width;
        int rows = (_colors.Count + Columns(width) - 1) / Columns(width);
        return new Avalonia.Size(width, Math.Max(Cell + 2 * GapPx, rows * (Cell + GapPx) + GapPx));
    }

    private Rect CellRect(int i, int cols)
        => new(GapPx + i % cols * (Cell + GapPx), GapPx + i / cols * (Cell + GapPx), Cell, Cell);

    public override void Render(DrawingContext g)
    {
        g.FillRectangle(AppTheme.InputBrush, new Rect(Bounds.Size));
        int cols = Columns(Bounds.Width);
        for (int i = 0; i < _colors.Count; i++)
        {
            var r = CellRect(i, cols);
            ImageUtil.DrawSwatch(g, r, _colors[i]);
            g.DrawRectangle(null, i == _hover ? AppTheme.Pen(Avalonia.Media.Colors.White, 2) : AppTheme.Pen(AppTheme.BorderColor), r.Deflate(i == _hover ? 1 : 0.5));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        int cols = Columns(Bounds.Width);
        int col = (int)((pos.X - GapPx) / (Cell + GapPx)), row = (int)((pos.Y - GapPx) / (Cell + GapPx));
        int i = pos.X >= GapPx && pos.Y >= GapPx && col < cols ? row * cols + col : -1;
        if (i >= _colors.Count || (i >= 0 && !CellRect(i, cols).Contains(pos))) i = -1;
        SetHover(i);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(-1);
    }

    private void SetHover(int i)
    {
        if (i == _hover) return;
        _hover = i;
        Color32? c = i >= 0 ? _colors[i] : null;
        ToolTip.SetTip(this, c?.ToHex());
        HoverChanged?.Invoke(this, c);
        InvalidateVisual();
    }
}
