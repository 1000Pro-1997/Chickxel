namespace Gpixel.App.PlayTest;

/// <summary>
/// 움직임 테스트 화면. 이중 버퍼로 그리며, 포커스를 받으면 방향키·스페이스를 포함한 모든 키를 입력으로 받는다.
/// 그리기는 <see cref="Control.Paint"/> 이벤트에서 한다.
/// </summary>
internal sealed class PlayView : Control
{
    public PlayView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.Opaque, true);
        TabStop = true;
        BackColor = Theme.BackColor;
    }

    /// <summary>방향키도 일반 입력으로 받는다(포커스 이동에 쓰지 않음).</summary>
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Space => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}
