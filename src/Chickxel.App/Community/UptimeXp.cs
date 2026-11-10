using System.Diagnostics;
using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Community;

namespace Chickxel.App;

/// <summary>
/// 앱을 로그인한 채 켜 둔 시간 경험치: <see cref="UptimeXpClock.Interval"/>(10분)마다 한 번 깨어나 <see cref="UptimeXpClock"/>으로 로그인한 시간을 세고, 한 시간이 차면
/// <see cref="CommunityClient.ReportUptimeAsync"/>로 알린다(받은 레벨은 계정 단추·배지가 <see cref="CommunityClient.AccountsChanged"/>로 다시 그림).
/// 레벨이 오르면 상태 표시줄에 알린다. 연결 문제면 다음 틱에 다시, 너무 잦으면(429) 서버가 알려 준 만큼 기다렸다 다시, 예전 서버(404)면 이번 실행 동안 멈춘다.
/// 그 밖에는 아무 일도 하지 않는다.
/// </summary>
internal sealed class UptimeXp
{
    private readonly DispatcherTimer _timer = new() { Interval = UptimeXpClock.Interval };
    private readonly UptimeXpClock _clock = new();
    private readonly Stopwatch _running = Stopwatch.StartNew();
    private readonly Action<string> _message;

    /// <param name="message">레벨이 올랐을 때 보일 상태 표시줄 글.</param>
    public UptimeXp(Action<string> message)
    {
        _message = message;
        // 시작 시각을 기준점으로 둔다(첫 틱까지의 시간도 세게 — 첫 틱이 기준점이면 처음 10분을 잃음)
        _clock.Tick(_running.Elapsed, signedIn: false);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private async void Tick()
    {
        var client = AppCommunity.Client;
        if (!_clock.Tick(_running.Elapsed, client.IsSignedIn)) return;
        int before = (client.User ?? client.Saved.Current?.User)?.Level ?? 0;
        try
        {
            var result = await client.ReportUptimeAsync();
            _clock.Reported(result.Granted, TimeSpan.FromMilliseconds(result.NextInMs));
            if (before > 0 && result.Level > before)
                _message(string.Format(CultureInfo.CurrentCulture, UiStrings.Level_UpStatus, result.Level));
        }
        catch (CommunityException ex) when (ex.Code == CommunityErrorCode.NotFound)
        {
            _clock.Unsupported();
            Stop();
        }
        catch (CommunityException ex) when (ex.Code == CommunityErrorCode.RateLimited)
        {
            // 너무 잦음(429): 서버가 알려 준 만큼(없으면 10분) 기다렸다 다시
            _clock.Reported(granted: false, ex.RetryAfter ?? TimeSpan.FromMinutes(10));
        }
        catch (Exception ex) when (ex is CommunityException or OperationCanceledException)
        {
            _clock.Failed();
        }
    }
}
