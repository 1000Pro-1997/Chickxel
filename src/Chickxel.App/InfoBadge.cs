namespace Chickxel.App;

/// <summary>
/// 정보박스: 모서리가 둥근 작은 네모 안의 "i" 아이콘. 마우스를 올리면 긴 설명 풍선이 뜨고, 누르면 기다리지 않고 바로 뜬다(다시 누르면 닫힘).
/// 화면에 다 들어가지 않는 안내 글은 모두 이것으로 단다(<see cref="Ui.InfoBadge"/>).
/// </summary>
public sealed class InfoBadge : Border
{
    public InfoBadge(string tip)
    {
        Width = 16;
        Height = 16;
        CornerRadius = new CornerRadius(4);
        BorderBrush = AppTheme.DimTextBrush;
        BorderThickness = new Thickness(1);
        Background = Brushes.Transparent;
        Margin = new Thickness(4, 0);
        VerticalAlignment = VerticalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Help);
        Child = new IconView(VectorIcon.InfoMark, AppTheme.DimTextBrush, 2.6) { Width = 11, Height = 11 };
        Tip = tip;
    }

    /// <summary>설명 글(단축키 표기는 이 OS에 맞춤).</summary>
    public string Tip
    {
        get => ToolTip.GetTip(this) as string ?? "";
        set => ToolTip.SetTip(this, Ui.Shortcut(value));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // 누르기는 여기서 끝낸다(부모 줄의 고르기·끌기가 시작되지 않게)
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        // 풍선 관리자가 누르기·떼기 때 풍선을 닫으므로 그 뒤에 연다
        bool open = !ToolTip.GetIsOpen(this);
        Dispatcher.UIThread.Post(() => ToolTip.SetIsOpen(this, open));
    }
}
