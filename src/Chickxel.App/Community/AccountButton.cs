using Chickxel.App.Localization;
using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>
/// 메인 창 메뉴 줄 오른쪽 끝의 계정 단추: 지금 계정의 프로필 그림과 이름(쓰는 계정이 없으면 사람 아이콘과 "로그인").
/// 이 컴퓨터에 로그인해 둔 계정이 없으면 누를 때 바로 계정 창을 열고, 있으면 계정 메뉴를 연다:
/// 로그인해 둔 계정들(누르면 그 계정으로 전환, 지금 계정에 체크 표시), 다른 계정 추가, 계정 관리(또는 로그인), 로그아웃.
/// chickxel.com에는 사용자가 계정 메뉴를 처음 열 때(지금 계정을 조용히 확인해 이름·프로필 그림을 새것으로)와 계정을 바꿀 때만 접속한다 —
/// 그 전에는 저장해 둔 이름과 사람 아이콘만 보인다(프로필 그림도 받지 않음).
/// </summary>
internal sealed class AccountButton : Button
{
    private const double AvatarSize = 18;

    private readonly Window _owner;
    private readonly StackPanel _content = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly TextBlock _name = new() { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis };
    private bool _verified;

    public AccountButton(Window owner)
    {
        _owner = owner;
        Content = _content;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(8, 3);
        Margin = new Thickness(0, 0, 4, 0);
        MinHeight = 0;
        Focusable = false;
        VerticalAlignment = VerticalAlignment.Center;
        this.Tip(UiStrings.Account_ButtonTip);
        Click += (_, _) => Open();
        Update();
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppCommunity.Client.AccountsChanged += OnAccountsChanged;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AppCommunity.Client.AccountsChanged -= OnAccountsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    // 요청을 마친 스레드에서 올 수 있다.
    private void OnAccountsChanged() => Dispatcher.UIThread.Post(Update);

    /// <summary>지금 계정으로 그림과 이름을 다시 그린다(이름은 확인한 정보, 없으면 저장해 둔 정보; 프로필 그림은 이번 실행에서 확인한 뒤에만).</summary>
    private void Update()
    {
        var client = AppCommunity.Client;
        var saved = client.Saved;
        _content.Children.Clear();
        if (saved.Current is { } current)
        {
            _content.Children.Add(AppCommunity.Avatar(client.User, AvatarSize));
            _name.Text = AppCommunity.DisplayName(client.User ?? current.User);
        }
        else
        {
            _content.Children.Add(Ui.Icon(VectorIcon.CircleUser, 16));
            _name.Text = UiStrings.Account_SignInButton;
        }
        _content.Children.Add(_name);
        if (saved.List.Count > 0) _content.Children.Add(Ui.Icon(VectorIcon.ChevronDown, 10));
    }

    private void Open()
    {
        var client = AppCommunity.Client;
        var saved = client.Saved;
        if (saved.List.Count == 0)
        {
            AccountDialog.Show(_owner);
            return;
        }
        if (!_verified)
        {
            _verified = true;
            _ = AppCommunity.VerifyQuietlyAsync();
        }
        var items = new List<object>();
        for (int i = 0; i < saved.List.Count; i++)
        {
            var account = saved.List[i];
            bool current = i == saved.Active;
            var user = current ? client.User ?? account.User : account.User;
            items.Add(Item(AccountHeader(user, current), AppCommunity.Avatar(user, 16), () => SwitchTo(account)));
        }
        items.Add(new Separator());
        items.Add(Item(UiStrings.Account_AddAccount, Ui.Icon(VectorIcon.UserPlus, 14), () => AccountDialog.Show(_owner, addAccount: true)));
        if (saved.Current is not null)
        {
            items.Add(Item(UiStrings.Account_Manage, Ui.Icon(VectorIcon.CircleUser, 14), () => AccountDialog.Show(_owner)));
            items.Add(Item(UiStrings.Account_SignOut, Ui.Icon(VectorIcon.LogOut, 14), () => _ = client.SignOutAsync()));
        }
        else
        {
            items.Add(Item(UiStrings.Account_SignInMenu, Ui.Icon(VectorIcon.LogIn, 14), () => AccountDialog.Show(_owner)));
        }
        PopupMenus.Show(this, items, PlacementMode.BottomEdgeAlignedRight);
    }

    /// <summary>계정 메뉴 한 줄: 이름(지금 계정은 굵게)과 @핸들, 지금 계정이면 오른쪽 체크 아이콘.</summary>
    private static Control AccountHeader(CommunityUser? user, bool current)
    {
        var names = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = AppCommunity.DisplayName(user), FontWeight = current ? FontWeight.Bold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center });
        if (user is not null) names.Children.Add(new TextBlock { Text = "@" + user.Handle, Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(names);
        if (current)
        {
            var check = Ui.Icon(VectorIcon.Check, 13);
            check.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(check, 1);
            row.Children.Add(check);
        }
        return row;
    }

    private static MenuItem Item(object header, Control icon, Action action)
    {
        var item = new MenuItem { Header = header, Icon = icon };
        // 메뉴가 닫힌 뒤에 한다(계정 창이 메뉴 위에 뜨지 않게).
        item.Click += (_, _) => Dispatcher.UIThread.Post(action);
        return item;
    }

    /// <summary>
    /// 계정 <paramref name="account"/>로 바꾸고 서버에 확인한다. 그 계정의 로그인이 끝났으면(목록에서 빠짐) 안내와 함께 계정 창을 연다.
    /// </summary>
    private async void SwitchTo(CommunityAccount account)
    {
        var client = AppCommunity.Client;
        if (client.Saved.Current?.Token == account.Token) return;
        client.SwitchAccount(account);
        if (client.Saved.Current?.Token != account.Token) return;
        if (await AppCommunity.VerifyQuietlyAsync() == false) AccountDialog.Show(_owner, UiStrings.Account_SessionEnded);
    }
}
