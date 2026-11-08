using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>
/// Discord 활동 상태(Rich Presence): <see cref="Show"/>로 정한 활동을 뒤에서 실행 중인 Discord 앱에 보낸다(<see cref="DiscordPresenceClient"/>).
/// Discord가 없으면 <see cref="RetryDelay"/>마다 다시 찾고, 활동은 Discord 한도(20초에 5번)를 넘지 않게 <see cref="MinSendGap"/> 간격으로만 보낸다.
/// 연결이 끊긴 것은 보낼 때 알게 되므로 같은 활동도 <see cref="RefreshDelay"/>마다 다시 보낸다(Discord를 다시 켜도 돌아옴).
/// <see cref="Show"/>(null)이나 <see cref="Dispose"/>는 연결을 닫아 활동을 지운다.
/// </summary>
internal sealed class DiscordPresence : IDisposable
{
    /// <summary>Discord Developer Portal의 애플리케이션 "Chickxel" ID(공개 값 — 활동 이름과 아이콘이 여기서 옴).</summary>
    public const string ApplicationId = "1566150610720530622";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MinSendGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private DiscordActivity? _desired;

    public DiscordPresence() => _loop = Task.Run(RunAsync);

    /// <summary>보일 활동(null = 보이지 않음). 같은 활동이면 아무것도 하지 않는다. 어느 스레드에서 불러도 된다.</summary>
    public void Show(DiscordActivity? activity)
    {
        lock (_gate)
        {
            if (activity == _desired) return;
            _desired = activity;
        }
        _wake.Release();
    }

    private async Task RunAsync()
    {
        DiscordPresenceClient? client = null;
        DiscordActivity? sent = null;
        DateTime lastAttempt = DateTime.MinValue, lastSend = DateTime.MinValue;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                DiscordActivity? want;
                lock (_gate) want = _desired;
                var now = DateTime.UtcNow;
                var wait = TimeSpan.FromMinutes(1);
                if (want is null)
                {
                    if (client is not null) await client.DisposeAsync();
                    client = null;
                    sent = null;
                }
                else
                {
                    if (client is null && now - lastAttempt >= RetryDelay)
                    {
                        lastAttempt = now;
                        client = await ConnectAsync();
                        sent = null;
                    }
                    if (client is null) wait = RetryDelay;
                    else if (want != sent || now - lastSend >= RefreshDelay)
                    {
                        if (now - lastSend < MinSendGap) wait = MinSendGap - (now - lastSend);
                        else
                        {
                            try
                            {
                                await client.SetActivityAsync(want, _stop.Token);
                                sent = want;
                                lastSend = DateTime.UtcNow;
                            }
                            catch (IOException)
                            {
                                // Discord를 껐거나 거절함: 이 연결은 버리고 다음에 다시 찾는다.
                                await client.DisposeAsync();
                                client = null;
                                wait = RetryDelay;
                            }
                        }
                    }
                }
                await _wake.WaitAsync(wait, _stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (client is not null) await client.DisposeAsync();
        }
    }

    private async Task<DiscordPresenceClient?> ConnectAsync()
    {
        try
        {
            return await DiscordPresenceClient.ConnectAsync(ApplicationId, _stop.Token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }
}
