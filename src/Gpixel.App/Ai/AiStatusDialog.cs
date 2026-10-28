using System.ComponentModel;
using Gpixel.App.Localization;

namespace Gpixel.App.Ai;

/// <summary>
/// Claude Code·Codex CLI의 설치 경로, 버전, 로그인과 Gpixel MCP 연결을 한곳에서 점검하고 설치·로그인·로그아웃·경로 변경을 제공한다.
/// 모델 요청은 보내지 않으므로 점검 자체는 AI 사용량을 쓰지 않는다.
/// </summary>
internal sealed class AiStatusDialog : DialogBase
{
    private readonly McpServer _mcp;
    private readonly AppSettings _settings;
    private readonly Dictionary<string, ProviderCard> _cards = new(StringComparer.Ordinal);
    private readonly TextBlock _serverText;
    private readonly IconView _serverIcon;
    private readonly TextBlock _message;
    private readonly ProgressBar _progress;
    private readonly CheckBox _hideUntilChange;
    private readonly Button _recheck;
    private readonly CancellationTokenSource _closed = new();
    private readonly DispatcherTimer _loginPoll;
    private readonly DispatcherTimer _installPoll;
    private readonly Dictionary<AiProvider, (string? Path, DateTime WriteTime, string? Override)> _installPending = new();
    private bool _installChecking;
    private AiProvider? _pollProvider;
    private bool _checking, _polling;

    internal AiStatusDialog(McpServer mcp, AppSettings settings, bool continueToAi) : base(UiStrings.AiStatus_Title, continueToAi ? UiStrings.AiStatus_Continue : UiStrings.AiStatus_Close)
    {
        _mcp = mcp;
        _settings = settings;
        Body.Width = 700;
        Body.MaxHeight = 700;
        // 기본 폭 90에서는 "확인하고 AI 열기"가 잘리므로 글에 맞춘다.
        OkButton.Width = double.NaN;
        OkButton.MinWidth = 90;

        var intro = Ui.Label(UiStrings.AiStatus_Intro);
        intro.TextWrapping = TextWrapping.Wrap;
        intro.Foreground = AppTheme.DimTextBrush;
        intro.Margin = new Thickness(2, 0, 2, 6);

        _progress = new ProgressBar { Height = 4, IsIndeterminate = true, Margin = new Thickness(2, 2, 2, 6) };
        _serverIcon = StatusIcon();
        _serverText = StatusText();
        var serverRow = StatusRow(UiStrings.AiStatus_McpServer, _serverIcon, _serverText);
        var serverGroup = Ui.Group(UiStrings.AiStatus_GpixelConnection, serverRow);

        var cards = new StackPanel { Spacing = 4 };
        foreach (var provider in AiProvider.All)
        {
            var card = new ProviderCard(this, provider);
            _cards[provider.Id] = card;
            cards.Children.Add(card.Root);
        }

        _message = Ui.Label("");
        _message.TextWrapping = TextWrapping.Wrap;
        _message.Foreground = AppTheme.DimTextBrush;
        _message.Margin = new Thickness(4, 4, 4, 0);
        _hideUntilChange = Ui.Check(UiStrings.AiStatus_DontShowUntilChange,
            string.Equals(settings.AiStatusDismissedFingerprint, AiStatusProbe.Fingerprint(settings), StringComparison.Ordinal));
        _hideUntilChange.Margin = new Thickness(4, 8, 4, 0);
        var note = Ui.Label(UiStrings.AiStatus_DontShowNote);
        note.TextWrapping = TextWrapping.Wrap;
        note.Foreground = AppTheme.DimTextBrush;
        note.Margin = new Thickness(25, 0, 4, 2);

        _recheck = Ui.Button(VectorIcon.Refresh, UiStrings.AiStatus_Recheck, async (_, _) => await CheckAllAsync());
        _recheck.HorizontalAlignment = HorizontalAlignment.Left;
        Body.Children.AddRange([intro, _progress, serverGroup, Ui.ScrollHost(cards), _message, _hideUntilChange, note, _recheck]);

        OkButton.IsEnabled = false;
        Opened += async (_, _) => await CheckAllAsync();
        Closing += (_, _) =>
        {
            _settings.AiStatusDismissedFingerprint = _hideUntilChange.On() ? AiStatusProbe.Fingerprint(_settings) : null;
            _closed.Cancel();
        };

        _loginPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _loginPoll.Tick += async (_, _) => await PollLoginAsync();
        _installPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _installPoll.Tick += async (_, _) => await PollInstallAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _loginPoll.Stop();
        _installPoll.Stop();
        _closed.Cancel();
        _closed.Dispose();
        base.OnClosed(e);
    }

    private async Task CheckAllAsync()
    {
        if (_checking || _closed.IsCancellationRequested) return;
        _checking = true;
        _progress.IsVisible = true;
        _progress.IsIndeterminate = true;
        _recheck.IsEnabled = false;
        OkButton.IsEnabled = false;
        _message.Text = UiStrings.AiStatus_Checking;
        foreach (var card in _cards.Values) card.SetPending();
        SetStatus(_serverIcon, _serverText, AiHealthLevel.Pending, UiStrings.AiStatus_CheckingShort);
        try
        {
            var server = await AiStatusProbe.CheckServerAsync(_mcp, _closed.Token);
            if (_closed.IsCancellationRequested) return;
            SetStatus(_serverIcon, _serverText, server.Level,
                server.Level == AiHealthLevel.Ok ? UiStrings.FormatAiStatus_McpServerReady(tools: server.ToolCount) : UiStrings.FormatAiStatus_Failed(reason: server.Error ?? UiStrings.AiStatus_Unknown));

            foreach (var provider in AiProvider.All)
            {
                var health = await AiStatusProbe.CheckProviderAsync(provider, _settings, _mcp, _closed.Token);
                if (_closed.IsCancellationRequested) return;
                _cards[provider.Id].Apply(health);
            }
            _message.Text = UiStrings.AiStatus_CheckComplete;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!_closed.IsCancellationRequested)
            {
                _checking = false;
                _progress.IsVisible = false;
                _recheck.IsEnabled = true;
                OkButton.IsEnabled = true;
            }
        }
    }

    private void StartInstall(AiProvider provider)
    {
        var automaticPath = provider.LocateAutomatically();
        var initialWriteTime = automaticPath is null ? DateTime.MinValue : File.GetLastWriteTimeUtc(automaticPath);
        var originalOverride = provider.GetPathOverride(_settings);
        var steps = AiInstaller.Preview(provider);
        if (Ui.Ask(this, UiStrings.FormatAiStatus_InstallConfirm(product: provider.ProductName, steps: steps), UiStrings.AiStatus_InstallUpdate,
                MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        if (!AiInstaller.OpenTerminal(provider, out var error))
        {
            SetMessage(UiStrings.FormatAiStatus_ActionFailed(reason: error), error: true);
            return;
        }
        SetMessage(UiStrings.FormatAiStatus_InstallerOpened(product: provider.ProductName));
        _installPending[provider] = (automaticPath, initialWriteTime, originalOverride);
        _installPoll.Start();
    }

    private async Task PollInstallAsync()
    {
        if (_installChecking || _checking || _installPending.Count == 0 || _closed.IsCancellationRequested) return;
        _installChecking = true;
        try
        {
            foreach (var (provider, initial) in _installPending)
            {
                var path = provider.LocateAutomatically();
                if (path is null || (path == initial.Path && File.GetLastWriteTimeUtc(path) == initial.WriteTime)) continue;
                var version = await AiCli.RunAsync(path, ["--version"], _closed.Token, timeoutSeconds: 10);
                if (_closed.IsCancellationRequested || version.Error is not null || version.ExitCode != 0 || string.IsNullOrWhiteSpace(version.Stdout)) continue;
                if (provider.GetPathOverride(_settings) == initial.Override) provider.SetPathOverride(_settings, null);
                _installPending.Remove(provider);
                if (_installPending.Count == 0) _installPoll.Stop();
                await CheckAllAsync();
                if (!_closed.IsCancellationRequested) StartLoginPoll(provider);
                break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _installChecking = false;
        }
    }

    private void StartLogin(AiProvider provider)
    {
        if (_cards[provider.Id].Path is not { } path) return;
        if (!AiCli.OpenLoginTerminal(path, provider.LoginArgs, out var error))
        {
            SetMessage(UiStrings.FormatAiStatus_ActionFailed(reason: error), error: true);
            return;
        }
        SetMessage(UiStrings.FormatAiStatus_LoginOpened(product: provider.ProductName));
        StartLoginPoll(provider);
    }

    private async Task LogoutAsync(AiProvider provider)
    {
        if (_checking || _cards[provider.Id].Path is not { } path) return;
        if (Ui.Ask(this, UiStrings.FormatAiStatus_LogoutConfirm(product: provider.ProductName), UiStrings.AiStatus_Logout,
                MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        _checking = true;
        _progress.IsVisible = true;
        _recheck.IsEnabled = false;
        OkButton.IsEnabled = false;
        foreach (var card in _cards.Values) card.SetActionsEnabled(false);
        _message.Text = UiStrings.AiStatus_LoggingOut;
        string? error = null;
        try { error = await AiStatusProbe.LogoutAsync(provider, path, _closed.Token); }
        catch (OperationCanceledException) { }
        finally { _checking = false; }
        if (_closed.IsCancellationRequested) return;
        await CheckAllAsync();
        if (_closed.IsCancellationRequested) return;
        SetMessage(error is null
            ? UiStrings.FormatAiStatus_LoggedOut(product: provider.ProductName)
            : UiStrings.FormatAiStatus_ActionFailed(reason: error), error is not null);
    }

    private void ChangePath(AiProvider provider)
    {
        var current = _cards[provider.Id].Path;
        var patterns = OperatingSystem.IsWindows() ? new[] { provider.Command + ".exe", provider.Command + ".cmd" } : new[] { provider.Command };
        if (Ui.OpenFile(this, UiStrings.FormatAiStatus_ChooseExecutable(product: provider.ProductName), current is null ? null : Path.GetDirectoryName(current),
                new FileFilter(provider.ProductName, patterns), new FileFilter(UiStrings.Ffmpeg_Prefs_AllFiles, "*.*")) is not { } picked) return;
        provider.SetPathOverride(_settings, picked);
        _ = CheckAllAsync();
    }

    private void UseAutomaticPath(AiProvider provider)
    {
        provider.SetPathOverride(_settings, null);
        _ = CheckAllAsync();
    }

    private void StartLoginPoll(AiProvider provider)
    {
        _pollProvider = provider;
        _loginPoll.Stop();
        _loginPoll.Start();
    }

    private async Task PollLoginAsync()
    {
        if (_polling || _pollProvider is not { } provider || _closed.IsCancellationRequested) return;
        _polling = true;
        try
        {
            var path = provider.Locate(_settings);
            if (path is null) return;
            var status = await provider.GetAuthStatusAsync(path, _closed.Token);
            if (!status.LoggedIn) return;
            _loginPoll.Stop();
            _pollProvider = null;
            await CheckAllAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _polling = false;
        }
    }

    private void SetMessage(string text, bool error = false)
    {
        _message.Text = text;
        _message.Foreground = error ? AppTheme.WarningBrush : AppTheme.DimTextBrush;
    }

    private static IconView StatusIcon() => new(VectorIcon.Refresh, AppTheme.DimTextBrush) { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock StatusText() => new()
    {
        Foreground = AppTheme.DimTextBrush,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Grid StatusRow(string label, IconView icon, TextBlock text)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,22,*"), Margin = new Thickness(2, 2) };
        row.Children.Add(new TextBlock { Text = label, Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(icon, 1);
        row.Children.Add(icon);
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        return row;
    }

    private static void SetStatus(IconView icon, TextBlock text, AiHealthLevel level, string value)
    {
        icon.Icon = level switch
        {
            AiHealthLevel.Ok => VectorIcon.Check,
            AiHealthLevel.Info => VectorIcon.Info,
            AiHealthLevel.Warning or AiHealthLevel.Error => VectorIcon.CircleAlert,
            _ => VectorIcon.Refresh,
        };
        var brush = level switch
        {
            AiHealthLevel.Ok or AiHealthLevel.Info => AppTheme.AccentBrush,
            AiHealthLevel.Warning or AiHealthLevel.Error => AppTheme.WarningBrush,
            _ => AppTheme.DimTextBrush,
        };
        icon.Foreground = brush;
        text.Foreground = brush;
        text.Text = value;
    }

    private sealed class ProviderCard
    {
        private readonly AiStatusDialog _owner;
        private readonly AiProvider _provider;
        private readonly IconView _cliIcon = StatusIcon(), _loginIcon = StatusIcon(), _mcpIcon = StatusIcon();
        private readonly TextBlock _cliText = StatusText(), _loginText = StatusText(), _mcpText = StatusText();
        private readonly Button _install, _login, _logout, _changePath, _automaticPath;

        internal ProviderCard(AiStatusDialog owner, AiProvider provider)
        {
            _owner = owner;
            _provider = provider;
            _install = Ui.Button(VectorIcon.Download, UiStrings.AiStatus_InstallUpdate, (_, _) => owner.StartInstall(provider));
            _login = Ui.Button(UiStrings.AiStatus_Login, (_, _) => owner.StartLogin(provider));
            _logout = Ui.Button(UiStrings.AiStatus_Logout, async (_, _) => await owner.LogoutAsync(provider));
            _changePath = Ui.Button(UiStrings.AiStatus_ChangePath, (_, _) => owner.ChangePath(provider));
            _automaticPath = Ui.Button(UiStrings.AiStatus_AutomaticPath, (_, _) => owner.UseAutomaticPath(provider));
            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(StatusRow(UiStrings.AiStatus_CliVersion, _cliIcon, _cliText));
            stack.Children.Add(StatusRow(UiStrings.AiStatus_LoginState, _loginIcon, _loginText));
            stack.Children.Add(StatusRow(UiStrings.AiStatus_McpConnection, _mcpIcon, _mcpText));
            stack.Children.Add(new WrapPanel { Margin = new Thickness(0, 4, 0, 0), Children = { _install, _login, _logout, _changePath, _automaticPath } });
            Root = Ui.Group(provider.ProductName, stack);
            SetPending();
        }

        internal Control Root { get; }
        internal string? Path { get; private set; }

        internal void SetPending()
        {
            SetStatus(_cliIcon, _cliText, AiHealthLevel.Pending, UiStrings.AiStatus_CheckingShort);
            SetStatus(_loginIcon, _loginText, AiHealthLevel.Pending, UiStrings.AiStatus_CheckingShort);
            SetStatus(_mcpIcon, _mcpText, AiHealthLevel.Pending, UiStrings.AiStatus_CheckingShort);
            SetActionsEnabled(false);
        }

        internal void SetActionsEnabled(bool enabled)
        {
            foreach (var button in new[] { _install, _login, _logout, _changePath, _automaticPath }) button.IsEnabled = enabled;
        }

        internal void Apply(AiProviderHealth health)
        {
            Path = health.Path;
            if (health.Path is null)
            {
                SetStatus(_cliIcon, _cliText, AiHealthLevel.Error, UiStrings.FormatAiStatus_NotInstalled(product: _provider.ProductName));
                SetStatus(_loginIcon, _loginText, AiHealthLevel.Warning, UiStrings.AiStatus_LoginUnavailable);
                SetStatus(_mcpIcon, _mcpText, AiHealthLevel.Warning, UiStrings.AiStatus_McpUnavailable);
            }
            else
            {
                var shellPath = AiCli.LocateOnPath(_provider.Command);
                var samePath = AiCli.SamePath(shellPath, health.Path);
                var cliLevel = health.CliError is not null ? AiHealthLevel.Error : samePath ? AiHealthLevel.Ok : AiHealthLevel.Info;
                var versionPath = UiStrings.FormatAiStatus_VersionPath(version: health.Version ?? UiStrings.AiStatus_Unknown, path: health.Path);
                SetStatus(_cliIcon, _cliText, cliLevel, health.CliError is not null
                    ? UiStrings.FormatAiStatus_Failed(reason: health.CliError)
                    : samePath ? versionPath
                    : versionPath + " · " + (shellPath is null ? UiStrings.AiStatus_NotOnPath : UiStrings.FormatAiStatus_DifferentPath(path: shellPath)));
                var auth = health.Auth;
                var loginLevel = auth?.LoggedIn == true ? AiHealthLevel.Ok : auth?.Error is null ? AiHealthLevel.Warning : AiHealthLevel.Error;
                var loginText = auth?.LoggedIn == true
                    ? UiStrings.FormatAiStatus_LoggedIn(account: auth.Describe() is { Length: > 0 } who ? who : _provider.AccountText)
                    : auth?.Error is { } error ? UiStrings.FormatAiStatus_Failed(reason: error) : UiStrings.AiStatus_NotLoggedIn;
                SetStatus(_loginIcon, _loginText, loginLevel, loginText);
                SetStatus(_mcpIcon, _mcpText, health.McpLevel, health.McpLevel switch
                {
                    AiHealthLevel.Ok => UiStrings.AiStatus_McpConnected,
                    AiHealthLevel.Info when _provider.Id == "claude" => UiStrings.AiStatus_McpPreparedClaude,
                    AiHealthLevel.Info => UiStrings.AiStatus_McpPreparedCodex,
                    _ => UiStrings.FormatAiStatus_Failed(reason: health.McpError ?? UiStrings.AiStatus_Unknown),
                });
            }

            _install.IsEnabled = true;
            _changePath.IsEnabled = true;
            _automaticPath.IsEnabled = !string.IsNullOrWhiteSpace(_provider.GetPathOverride(_owner._settings));
            _login.IsEnabled = health.Path is not null && health.Auth?.LoggedIn != true;
            _logout.IsEnabled = health.Path is not null && health.Auth?.LoggedIn == true;
            _login.IsVisible = health.Auth?.LoggedIn != true;
            _logout.IsVisible = health.Auth?.LoggedIn == true;
        }
    }
}
