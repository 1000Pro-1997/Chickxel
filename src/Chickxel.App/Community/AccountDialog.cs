using System.Globalization;
using System.Net;
using Chickxel.App.Localization;
using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>
/// Chickxel 계정 창(도움말 메뉴 > Chickxel 계정…, 로그인이 필요한 올리기에서도 열림).
/// 로그인하지 않았으면 Google·GitHub·Discord 단추: 누르면 브라우저에 로그인 쪽을 열고(그 제공자를 골라 둠) 127.0.0.1 되돌아오기를 기다린다.
/// 로그인했으면 프로필 그림·이름·핸들, 이름·핸들 고치기(<c>PATCH /api/me</c>), 내 갤러리 쪽 열기, 로그아웃.
/// 창을 닫으면 기다리던 로그인은 취소된다.
/// </summary>
public sealed class AccountDialog : DialogBase
{
    private const int MaxAvatarBytes = 2 << 20;

    private readonly StackPanel _view = new() { Spacing = 6, Width = 380 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush, IsVisible = false };
    private readonly string? _reason;
    private CancellationTokenSource? _work;
    private Uri? _loginUrl;
    private bool _busy;

    /// <summary>창을 띄운다(닫힐 때까지 기다림). <paramref name="reason"/>은 맨 위에 보일 안내(로그인이 필요한 까닭). 닫을 때 로그인돼 있으면 true.</summary>
    public static bool Show(Window owner, string? reason = null)
    {
        new AccountDialog(reason).ShowDialog(owner);
        return AppCommunity.Client.User is not null;
    }

    private AccountDialog(string? reason) : base(UiStrings.Account_Title)
    {
        _reason = reason;
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

    /// <summary>저장한 세션이 있으면 서버에 내 정보를 물어 다시 그린다(끝난 세션이면 로그인 단추로).</summary>
    private async void Refresh()
    {
        if (!Client.IsSignedIn || _busy) return;
        _busy = true;
        SetStatus(UiStrings.Account_Loading);
        Build();
        try
        {
            await Client.GetMeAsync(Token());
            SetStatus(null);
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
        else if (Client.User is { } user) BuildSignedIn(user);
        else if (Client.IsSignedIn) BuildChecking();
        else BuildSignedOut();
    }

    private void BuildSignedOut()
    {
        _view.Children.Add(Text(UiStrings.Account_Intro));
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
        var avatar = new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(28), ClipToBounds = true, Background = AppTheme.PanelBrush,
            Child = new IconView(VectorIcon.CircleUser, AppTheme.DimTextBrush) { Width = 40, Height = 40 },
            VerticalAlignment = VerticalAlignment.Top,
        };
        LoadAvatar(avatar, user.AvatarUrl);
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
        var handle = new TextBox { Text = user.Handle, Width = 240, MaxLength = CommunityRules.MaxHandleLength };
        var save = Ui.Button(VectorIcon.Save, UiStrings.Account_SaveProfile, (_, _) => SaveProfile(name.Text, handle.Text));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        AddRow(grid, 0, UiStrings.Account_Name, name);
        AddRow(grid, 1, UiStrings.Account_Handle, handle, UiStrings.Account_HandleTip);
        _view.Children.Add(Ui.Group(UiStrings.Account_Profile, Ui.Stack(grid, save)));

        var page = Ui.Button(VectorIcon.ExternalLink, UiStrings.Account_OpenPage, (_, _) => AppCommunity.Open(this, Client.UserPageUrl(user.Handle)));
        var logout = Ui.Button(VectorIcon.LogOut, UiStrings.Account_SignOut, (_, _) => SignOut());
        _view.Children.Add(Ui.Row(page, logout));
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

    /// <summary>프로필 그림을 받아 둥근 칸에 넣는다(HTTPS·2 MiB 이하, 못 받으면 사람 아이콘 그대로).</summary>
    private async void LoadAvatar(Border host, string? url)
    {
        if (Client.Resolve(url) is not { } uri) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await AvatarHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxAvatarBytes) return;
            byte[] data = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (data.Length > MaxAvatarBytes) return;
            using var stream = new MemoryStream(data, writable: false);
            host.Child = new Image { Source = new Bitmap(stream), Stretch = Stretch.UniformToFill, Width = host.Width, Height = host.Height };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or ArgumentException or InvalidOperationException
                                       or NotSupportedException)
        {
            // 그림이 없어도 계정 창은 그대로 쓴다.
        }
    }

    private static readonly HttpClient AvatarHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

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

    private async void SignOut()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await Client.SignOutAsync(Token());
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
