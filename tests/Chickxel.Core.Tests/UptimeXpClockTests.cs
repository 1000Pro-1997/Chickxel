using Chickxel.Core.Community;
using Xunit;

namespace Chickxel.Core.Tests;

public class UptimeXpClockTests
{
    /// <summary>1분마다 <paramref name="minutes"/>번 잰다(시각은 <paramref name="start"/>분부터). 알릴 차례였던 횟수.</summary>
    private static int Run(UptimeXpClock clock, ref int start, int minutes, bool signedIn = true)
    {
        int due = 0;
        for (int i = 0; i < minutes; i++)
            if (clock.Tick(TimeSpan.FromMinutes(start++), signedIn)) due++;
        return due;
    }

    [Fact]
    public void ReportsOnceAfterAFullSignedInHour_AndCountsTheNextHourFromThere()
    {
        var clock = new UptimeXpClock();
        int t = 0;
        Assert.Equal(0, Run(clock, ref t, 60)); // 첫 틱은 기준점이라 59분만 셈
        Assert.True(clock.Tick(TimeSpan.FromMinutes(t++), true));
        Assert.True(clock.Reporting);
        Assert.Equal(0, Run(clock, ref t, 5)); // 답을 기다리는 동안은 다시 보내지 않음
        clock.Reported(granted: true, TimeSpan.FromMinutes(60));
        Assert.False(clock.Reporting);
        Assert.Equal(TimeSpan.FromMinutes(5), clock.Counted);
        Assert.Equal(0, Run(clock, ref t, 54));
        Assert.Equal(1, Run(clock, ref t, 1));
    }

    [Fact]
    public void NotSignedIn_DoesNotCount()
    {
        var clock = new UptimeXpClock();
        int t = 0;
        Assert.Equal(0, Run(clock, ref t, 120, signedIn: false));
        Assert.Equal(TimeSpan.Zero, clock.Counted);
        Assert.Equal(0, Run(clock, ref t, 30)); // 로그인한 첫 틱부터 1분씩
        Assert.Equal(0, Run(clock, ref t, 30, signedIn: false));
        Assert.Equal(TimeSpan.FromMinutes(30), clock.Counted);
    }

    [Fact]
    public void LongGaps_CountOnlyTheMaxStep()
    {
        var clock = new UptimeXpClock();
        Assert.False(clock.Tick(TimeSpan.Zero, true));
        Assert.False(clock.Tick(TimeSpan.FromHours(5), true)); // 잠든 사이
        Assert.Equal(UptimeXpClock.MaxStep, clock.Counted);
        Assert.False(clock.Tick(TimeSpan.FromHours(4), true)); // 거꾸로 가는 시계는 세지 않음
        Assert.Equal(UptimeXpClock.MaxStep, clock.Counted);
    }

    [Fact]
    public void Failure_KeepsTheHourPending_AndRetriesOnTheNextTick()
    {
        var clock = new UptimeXpClock();
        int t = 0;
        Assert.Equal(1, Run(clock, ref t, 61));
        clock.Failed();
        Assert.False(clock.Reporting);
        Assert.True(clock.Tick(TimeSpan.FromMinutes(t++), true));
        clock.Failed();
        // 로그아웃한 동안은 보내지 않고, 다시 로그인하면 바로
        Assert.False(clock.Tick(TimeSpan.FromMinutes(t++), false));
        Assert.True(clock.Tick(TimeSpan.FromMinutes(t++), true));
    }

    [Fact]
    public void Refused_WaitsForTheServersNextIn()
    {
        var clock = new UptimeXpClock();
        int t = 0;
        Assert.Equal(1, Run(clock, ref t, 61));
        clock.Reported(granted: false, TimeSpan.FromMinutes(20));
        Assert.Equal(TimeSpan.FromMinutes(40), clock.Counted);
        Assert.Equal(0, Run(clock, ref t, 19));
        Assert.Equal(1, Run(clock, ref t, 1));

        // 남은 시간이 0이어도 1분은 기다리고, 한 시간보다 길면 한 시간
        clock.Reported(granted: false, TimeSpan.Zero);
        Assert.Equal(UptimeXpClock.Hour - UptimeXpClock.MinRetry, clock.Counted);
        clock.Reported(granted: false, TimeSpan.FromHours(3));
        Assert.Equal(TimeSpan.Zero, clock.Counted);
    }

    [Fact]
    public void Granted_WithABacklog_KeepsTheRest()
    {
        var clock = new UptimeXpClock();
        int t = 0;
        Assert.Equal(1, Run(clock, ref t, 61));
        Assert.Equal(0, Run(clock, ref t, 70)); // 답이 늦은 사이에도 셈
        clock.Reported(granted: true, TimeSpan.FromMinutes(60));
        Assert.Equal(TimeSpan.FromMinutes(70), clock.Counted);
        Assert.True(clock.Tick(TimeSpan.FromMinutes(t++), true));
    }

    [Fact]
    public void Unsupported_StopsForTheSession()
    {
        var clock = new UptimeXpClock();
        int t = 0;
        Assert.Equal(1, Run(clock, ref t, 61));
        clock.Unsupported();
        Assert.True(clock.Stopped);
        Assert.Equal(0, Run(clock, ref t, 300));
    }
}
