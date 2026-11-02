using System.Globalization;
using System.Net;
using Chickxel.App.Localization;
using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>
/// Chickxel 계정 창(메뉴 줄 오른쪽 계정 단추, 도움말 메뉴 > Chickxel 계정…, 로그인이 필요한 올리기에서도 열림).
/// 로그인하지 않았으면 Google·GitHub·Discord 단추: 누르면 브라우저에 로그인 쪽을 열고(그 제공자를 골라 둠) 127.0.0.1 되돌아오기를 기다린다.
/// 로그인했으면 프로필 그림·이름·핸들, 이름·핸들 고치기(<c>PATCH /api/me</c>), 내 갤러리 쪽 열기, 로그아웃, 다른 계정 추가.
/// 이 컴퓨터에 로그인해 둔 다른 계정은 아래 목록에서 바꾸거나 로그아웃한다. 창을 닫으면 기다리던 로그인은 취소된다.
/// </summary>
public sealed class AccountDialog : DialogBase
{
    private readonly StackPanel _view = new() { Spacing = 6, Width = 380 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush, IsVisible = false };
    private readonly string? _reason;
    private CancellationTokenSource? _work;
    private Uri? _loginUrl;
    private bool _busy;
    /// <summary>지금 계정을 두고 다른 계정으로 로그인하는 중(로그인 단추 화면).</summary>
    private bool _adding;

    /// <summary>
    /// 창을 띄운다(닫힐 때까지 기다림). <paramref name="reason"/>은 맨 위에 보일 안내(로그인이 필요한 까닭).
    /// <paramref name="addAccount"/>면 처음부터 다른 계정 추가 화면. 닫을 때 로그인돼 있으면 true.
    /// </summary>
    public static bool Show(Window owner, string? reason = null, bool addAccount = false)
    {
        new AccountDialog(reason, addAccount).ShowDialog(owner);
        return AppCommunity.Client.User is not null;
    }

    private AccountDialog(string? reason, bool addAccount) : base(UiStrings.Account_Title)
    {
        _reason = reason;
        _adding = addAccount;
        CloseOnly();
        Body.Children.Add(_view);
        Body.Children.Add(_status);
        Closed += (_, _) => _work?.Cancel();
        Opened += (_, _) => Refresh();
        Build();
    }

    private static CommunityClient Client => AppCommunity.Client;

    private static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private void SetStatus(string? text, bool error = false)
    {
        _status.Text = text ?? "";
        _status.IsVisible = !string.IsNullOrEmpty(text);
        _status.Foreground = error ? AppTheme.UnsavedBrush : AppTheme.DimTextBrush;
    }

    /// <summary>
    /// 로그인해 있으면 서버에 내 정보를 다시 물어 그린다(이름·핸들 잠김이 새것이 되게; 끝난 세션이면 안내하고 로그인 단추로).
    /// 로그인이 필요해서 열린 창이면 확인되자마자 닫는다.
    /// </summary>
    private async void Refresh()
    {
        if (!Client.IsSignedIn || _busy) return;
        _busy = true;
        SetStatus(UiStrings.Account_Loading);
        Build();
        try
        {
            var user = await Client.GetMeAsync(Token());
            SetStatus(user is null ? UiStrings.Account_SessionEnded : null, error: user is null);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (CommunityException ex)
        {
            SetStatus(ex.DisplayText, error: true);
        }
        finally
        {
            _busy = false;
        }
        Build();
        CloseIfDone();
    }

    /// <summary>로그인이 필요해서 열린 창이면 확인된 계정이 생기자마자 닫고 하던 일로 돌아간다.</summary>
    private void CloseIfDone()
    {
        if (_reason is not null && Client.User is not null && IsVisible) DialogResult = DialogResult.OK;
    }

    private CancellationToken Token()
    {
        _work?.Dispose();
        _work = new CancellationTokenSource();
        return _work.Token;
    }

    // ───────────── 화면 ─────────────

    private void Build()
    {
        _view.Children.Clear();
        if (_reason is not null && Client.User is null)
            _view.Children.Add(new TextBlock { Text = _reason, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.WarningBrush });
        if (_loginUrl is not null) BuildWaiting();
        else if (_adding) BuildSignIn(adding: true);
        else if (Client.User is { } user) BuildSignedIn(user);
        else if (Client.IsSignedIn) BuildChecking();
        else BuildSignIn(adding: false);
        if (_loginUrl is null && !_adding) BuildOtherAccounts();
    }

    private void BuildSignIn(bool adding)
    {
        _view.Children.Add(Text(adding ? UiStrings.Account_AddIntro : UiStrings.Account_Intro));
        foreach (var (provider, icon, name) in new[]
                 {
                     ("google", VectorIcon.Google, "Google"),
                     ("github", VectorIcon.GitHub, "GitHub"),
                     ("discord", VectorIcon.Discord, "Discord"),
                 })
        {
            var button = Ui.Button(icon, F(UiStrings.Account_SignInWith, name), (_, _) => SignIn(provider));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(12, 6);
            if (provider == "discord") button.Foreground = AppTheme.DiscordBrush;
            _view.Children.Add(button);
        }
        _view.Children.Add(Dim(UiStrings.Account_BrowserNote));
        if (adding && Client.Saved.List.Count > 0)
        {
            var back = Ui.Button(VectorIcon.ChevronLeft, UiStrings.Account_Back, (_, _) =>
            {
                _adding = false;
                SetStatus(null);
                Build();
                Refresh();
            });
            back.HorizontalAlignment = HorizontalAlignment.Left;
            _view.Children.Add(back);
        }
    }

    private void BuildWaiting()
    {
        _view.Children.Add(Text(UiStrings.Account_Waiting));
        var reopen = Ui.Button(VectorIcon.ExternalLink, UiStrings.Account_ReopenBrowser, (_, _) =>
        {
            if (_loginUrl is { } url) AppCommunity.Open(this, url);
        });
        var cancel = Ui.Button(VectorIcon.X, UiStrings.Account_CancelSignIn, (_, _) => _work?.Cancel());
        _view.Children.Add(Ui.Row(reopen, cancel));
    }

    private void BuildChecking() => _view.Children.Add(Dim(UiStrings.Account_Loading));

    private void BuildSignedIn(CommunityUser user)
    {
        var avatar = AppCommunity.Avatar(user, 56);
        avatar.Background = AppTheme.PanelBrush;
        avatar.VerticalAlignment = VerticalAlignment.Top;
        var names = new StackPanel
        {
            Spacing = 2, Margin = new Thickness(10, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = user.Name, FontWeight = FontWeight.Bold, FontSize = 16, Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "@" + user.Handle, Foreground = AppTheme.DimTextBrush },
            },
        };
        _view.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { avatar, names } });

        var name = new TextBox { Text = user.Name, Width = 240, MaxLength = CommunityRules.MaxNameLength };
        // 에셋 패키지를 올린 뒤에는 서버가 핸들을 바꾸지 않으므로(패키지 ID·작성자 ID가 핸들) 고칠 수 없는 칸으로 보인다.
        var handle = new TextBox { Text = user.Handle, Width = 240, MaxLength = CommunityRules.MaxHandleLength, IsEnabled = !user.HandleLocked };
        var save = Ui.Button(VectorIcon.Save, UiStrings.Account_SaveProfile, (_, _) => SaveProfile(name.Text, handle.Text));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        AddRow(grid, 0, UiStrings.Account_Name, name);
        AddRow(grid, 1, UiStrings.Account_Handle, handle, UiStrings.Account_HandleTip);
        var profile = Ui.Stack(grid);
        if (user.HandleLocked) profile.Children.Add(Dim(UiStrings.Account_HandleLocked));
        profile.Children.Add(save);
        _view.Children.Add(Ui.Group(UiStrings.Account_Profile, profile));

        var page = Ui.Button(VectorIcon.ExternalLink, UiStrings.Account_OpenPage, (_, _) => AppCommunity.Open(this, Client.UserPageUrl(user.Handle)));
        var logout = Ui.Button(VectorIcon.LogOut, UiStrings.Account_SignOut, (_, _) => SignOut(null));
        var add = Ui.Button(VectorIcon.UserPlus, UiStrings.Account_AddAccount, (_, _) =>
        {
            _adding = true;
            SetStatus(null);
            Build();
        });
        _view.Children.Add(Ui.Row(page, logout));
        _view.Children.Add(Ui.Row(add));
    }

    /// <summary>이 컴퓨터에 로그인해 둔 다른 계정(지금 계정 빼고): 프로필 그림·이름·핸들, 전환, 로그아웃.</summary>
    private void BuildOtherAccounts()
    {
        var saved = Client.Saved;
        var rows = new StackPanel { Spacing = 4 };
        for (int i = 0; i < saved.List.Count; i++)
        {
            if (i == saved.Active) continue;
            var account = saved.List[i];
            var names = new StackPanel { Spacing = 0, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = AppCommunity.DisplayName(account.User), Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis });
            if (account.User is { } user) names.Children.Add(new TextBlock { Text = "@" + user.Handle, Foreground = AppTheme.DimTextBrush, FontSize = 11 });
            var switchButton = Ui.Button(UiStrings.Account_Switch, (_, _) => SwitchTo(account));
            var signOut = Ui.IconButton(VectorIcon.LogOut, F(UiStrings.Account_SignOutOf, AppCommunity.DisplayName(account.User)), (_, _) => SignOut(account));
            var buttons = Ui.Row(switchButton, signOut);
            buttons.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { AppCommunity.Avatar(account.User, 28), names } });
            rows.Children.Add(row);
        }
        if (rows.Children.Count > 0) _view.Children.Add(Ui.Group(UiStrings.Account_SavedAccounts, rows));
    }

    /// <summary>이름표 | 칸 한 줄(이름표 옆 풍선 도움말 선택).</summary>
    private static void AddRow(Grid grid, int row, string label, Control field, string? tip = null)
    {
        var text = new TextBlock { Text = label, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center };
        Control head = tip is null ? text : Ui.Row(text, Ui.InfoBadge(tip));
        head.VerticalAlignment = VerticalAlignment.Center;
        head.Margin = new Thickness(0, 3, 8, 3);
        field.Margin = new Thickness(0, 3);
        Grid.SetRow(head, row);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(head);
        grid.Children.Add(field);
    }

    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.TextBrush };

    private static TextBlock Dim(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush };

    // ───────────── 동작 ─────────────

    private async void SignIn(string provider)
    {
        if (_busy) return;
        _busy = true;
        SetStatus(null);
        try
        {
            await Client.SignInAsync(provider, url =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _loginUrl = url;
                    AppCommunity.Open(this, url);
                    Build();
                });
            }, Token());
            _adding = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (CommunityException ex)
        {
            SetStatus(ex.DisplayText, error: true);
        }
        catch (HttpListenerException ex)
        {
            SetStatus(F(UiStrings.Account_ListenError, ex.Message), error: true);
        }
        finally
        {
            _busy = false;
            _loginUrl = null;
        }
        Build();
        // 로그인이 필요해서 열린 창이면 로그인하자마자 닫고 하던 일로 돌아간다.
        if (_reason is not null && Client.User is not null && IsVisible) DialogResult = DialogResult.OK;
        else Activate();
    }

    /// <summary>저장한 계정 <paramref name="account"/>로 바꾸고 서버에 확인한다(끝난 세션이면 목록에서 빠지고 안내).</summary>
    private void SwitchTo(CommunityAccount account)
    {
        if (_busy) return;
        Client.SwitchAccount(account);
        SetStatus(null);
        Build();
        Refresh();
    }

    private async void SaveProfile(string? name, string? handle)
    {
        if (_busy || Client.User is not { } user) return;
        name = name?.Trim() ?? "";
        handle = handle?.Trim() ?? "";
        if (!CommunityRules.IsValidName(name))
        {
            SetStatus(UiStrings.Account_NameInvalid, error: true);
            return;
        }
        if (!CommunityRules.IsValidHandle(handle))
        {
            SetStatus(UiStrings.Account_HandleInvalid, error: true);
            return;
        }
        if (name == user.Name && handle == user.Handle) return;
        _busy = true;
        SetStatus(UiStrings.Account_Saving);
        try
        {
            await Client.UpdateMeAsync(name == user.Name ? null : name, handle == user.Handle ? null : handle, Token());
            SetStatus(UiStrings.Account_Saved);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (CommunityException ex)
        {
            SetStatus(ex.DisplayText, error: true);
        }
        finally
        {
            _busy = false;
        }
        Build();
    }

    /// <summary>계정 <paramref name="account"/>(null = 지금 계정)에서 로그아웃한다. 다른 계정이 남아도 저절로 바꾸지 않는다.</summary>
    private async void SignOut(CommunityAccount? account)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (account is null) await Client.SignOutAsync(Token());
            else await Client.SignOutAsync(account, Token());
            SetStatus(UiStrings.Account_SignedOut);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _busy = false;
        }
        Build();
    }
}
