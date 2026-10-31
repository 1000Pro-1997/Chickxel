namespace Chickxel.App.PlayTest;

/// <summary>
/// 움직임 테스트 화면. 포커스를 받으면 방향키·스페이스를 포함한 모든 키를 입력으로 받는다.
/// 그리기는 <see cref="Paint"/>에 맡긴다(화면 전체를 채워야 한다).
/// </summary>
internal sealed class PlayView : Control
{
    public PlayView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>화면을 그리는 함수(그리기 대상, 화면 크기).</summary>
    public Action<DrawingContext, Avalonia.Size>? Paint { get; set; }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (Paint is null) context.FillRectangle(AppTheme.BackBrush, new Rect(size));
        else Paint(context, size);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        base.OnPointerPressed(e);
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }
}
