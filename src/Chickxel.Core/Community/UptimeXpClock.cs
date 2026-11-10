namespace Chickxel.Core.Community;

/// <summary>
/// 앱을 로그인한 채 켜 둔 시간 경험치(+1 XP/시간)를 언제 서버에 알릴지 정한다(시각은 밖에서 넣음 — 화면·네트워크 없음).
/// 앱은 <see cref="Interval"/>마다 <see cref="Tick"/>을 부르고, true면 <c>POST /api/me/uptime</c>을 보낸 뒤 결과를 <see cref="Reported"/>·<see cref="Failed"/>·
/// <see cref="Unsupported"/>로 알린다. 로그인한 동안의 시간만 세고, 한 번에 <see cref="MaxStep"/>보다 긴 틈(컴퓨터가 잠든 사이 등)은 그만큼만 센다.
/// 실패하면 그 한 시간을 남겨 다음 틱에 다시 보내고(서버가 58분 안에 두 번 주지 않음), 예전 서버(404)면 이번 실행 동안 멈춘다.
/// </summary>
public sealed class UptimeXpClock
{
    /// <summary>경험치 1을 받는 시간.</summary>
    public static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    /// <summary>앱이 <see cref="Tick"/>을 부르는 간격.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    /// <summary>틱 한 번에 더하는 가장 긴 시간(늦은 틱 하나는 다 세고, 컴퓨터가 잠든 사이 같은 긴 틈은 자름).</summary>
    public static readonly TimeSpan MaxStep = Interval * 2;
    /// <summary>서버가 주지 않았을 때 다시 보내기까지 가장 짧게 기다리는 시간.</summary>
    public static readonly TimeSpan MinRetry = TimeSpan.FromMinutes(1);

    private TimeSpan? _last;

    /// <summary>아직 알리지 않은, 로그인한 채 켜 둔 시간.</summary>
    public TimeSpan Counted { get; private set; }

    /// <summary>알리는 중(<see cref="Tick"/>이 true를 돌려준 뒤 결과를 받기 전).</summary>
    public bool Reporting { get; private set; }

    /// <summary>예전 서버라 이번 실행에서는 더 알리지 않는다.</summary>
    public bool Stopped { get; private set; }

    /// <summary>
    /// 시각 <paramref name="now"/>(늘 늘어나는 시계)에 잰다. 로그인했으면 지난 틱부터의 시간(최대 <see cref="MaxStep"/>)을 센다.
    /// 한 시간이 찼고 알리는 중이 아니면 true(그때부터 <see cref="Reporting"/>).
    /// </summary>
    public bool Tick(TimeSpan now, bool signedIn)
    {
        if (Stopped) return false;
        if (signedIn && _last is { } last && now > last) Counted += now - last < MaxStep ? now - last : MaxStep;
        _last = now;
        if (!signedIn || Reporting || Counted < Hour) return false;
        Reporting = true;
        return true;
    }

    /// <summary>
    /// 서버가 답했다. 받았으면(<paramref name="granted"/>) 한 시간을 덜고, 아니면(마지막으로 준 지 58분이 안 됨 — 다른 기기 등) 서버가 알려 준
    /// 남은 시간 <paramref name="nextIn"/>(<see cref="MinRetry"/>~한 시간으로 맞춤)이 지나면 다시 알리게 센 시간을 맞춘다.
    /// </summary>
    public void Reported(bool granted, TimeSpan nextIn)
    {
        Reporting = false;
        if (granted)
        {
            Counted = Counted > Hour ? Counted - Hour : TimeSpan.Zero;
            return;
        }
        var wait = nextIn < MinRetry ? MinRetry : nextIn > Hour ? Hour : nextIn;
        Counted = Hour - wait;
    }

    /// <summary>보내지 못했다(연결 문제 등): 센 시간을 그대로 두어 다음 틱에 다시 알린다.</summary>
    public void Failed() => Reporting = false;

    /// <summary>예전 서버(그 주소가 없음): 이번 실행에서는 멈춘다.</summary>
    public void Unsupported()
    {
        Reporting = false;
        Stopped = true;
    }
}
