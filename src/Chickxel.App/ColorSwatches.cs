using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 색 칸 목록(공용): 편집기 팔레트 칸과 같은 크기(14px + 2px 간격)로 너비에 맞춰 줄을 바꿔 늘어놓는다. 마우스를 올린 색은 흰 테두리와
/// 풍선 도움말(#RRGGBB, 또는 <see cref="TipFor"/>)로 보이고 <see cref="HoverChanged"/>로 알린다. 고른 칸(<see cref="SelectedIndex"/>)은 강조색 테두리.
/// 누르기는 <see cref="CellClicked"/>(왼쪽)·<see cref="CellRightClicked"/>(오른쪽)로 알린다. 팔레트 웹의 고른 팔레트, 3D 모델 가져오기의 쓴 색·원본 색이 쓴다.
/// </summary>
public sealed class ColorSwatches : Control
{
    /// <summary>편집기 팔레트 칸과 같은 크기(14px + 2px 간격).</summary>
    private const double Cell = 14, GapPx = 2;
    private IReadOnlyList<Color32> _colors = [];
    private int _hover = -1, _selected = -1;

    public ColorSwatches() => TipPlacement.FollowPointer(this);

    /// <summary>한 줄에 <paramref name="columns"/>칸이 들어가는 너비(오른쪽 스크롤바 자리 14px 포함).</summary>
    public static double WidthFor(int columns) => GapPx + columns * (Cell + GapPx) + 14;

    /// <summary>마우스를 올린 색이 바뀜(null = 칸 밖).</summary>
    public event EventHandler<Color32?>? HoverChanged;
    /// <summary>칸을 왼쪽 단추로 누름(칸 번호).</summary>
    public event EventHandler<int>? CellClicked;
    /// <summary>칸을 오른쪽 단추로 누름(칸 번호, 누른 이벤트 — 메뉴를 그 자리에 열 때).</summary>
    public event Action<int, PointerPressedEventArgs>? CellRightClicked;

    /// <summary>칸 번호 → 풍선 도움말(없으면 #RRGGBB).</summary>
    public Func<int, string>? TipFor { get; set; }

    /// <summary>고른 칸(−1 = 없음).</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            InvalidateVisual();
        }
    }

    public IReadOnlyList<Color32> Colors => _colors;

    public void SetColors(IReadOnlyList<Color32> colors)
    {
        _colors = colors;
        _hover = -1;
        if (_selected >= colors.Count) _selected = -1;
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
            if (i == _selected)
            {
                g.DrawRectangle(null, AppTheme.Pen(AppTheme.AccentColor, 2), r.Inflate(1));
                g.DrawRectangle(null, AppTheme.Pen(Avalonia.Media.Colors.White, 1), r.Deflate(0.5));
            }
            else g.DrawRectangle(null, i == _hover ? AppTheme.Pen(Avalonia.Media.Colors.White, 2) : AppTheme.Pen(AppTheme.BorderColor), r.Deflate(i == _hover ? 1 : 0.5));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHover(IndexAt(e.GetPosition(this)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        int i = IndexAt(e.GetPosition(this));
        if (i < 0) return;
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.LeftButtonPressed && CellClicked is { } click)
        {
            click(this, i);
            e.Handled = true;
        }
        else if (kind == PointerUpdateKind.RightButtonPressed && CellRightClicked is { } right)
        {
            right(i, e);
            e.Handled = true;
        }
    }

    private int IndexAt(Avalonia.Point pos)
    {
        int cols = Columns(Bounds.Width);
        int col = (int)((pos.X - GapPx) / (Cell + GapPx)), row = (int)((pos.Y - GapPx) / (Cell + GapPx));
        int i = pos.X >= GapPx && pos.Y >= GapPx && col < cols ? row * cols + col : -1;
        if (i >= _colors.Count || (i >= 0 && !CellRect(i, cols).Contains(pos))) i = -1;
        return i;
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
        ToolTip.SetTip(this, c is null ? null : TipFor?.Invoke(i) ?? c.Value.ToHex());
        HoverChanged?.Invoke(this, c);
        InvalidateVisual();
    }
}
