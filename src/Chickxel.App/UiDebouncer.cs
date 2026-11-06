namespace Chickxel.App;

/// <summary>
/// UI 스레드에서 한 번만 도는 미루기 타이머: <see cref="Restart"/>할 때마다 처음부터 다시 재고, 조용히 <c>delay</c>가 지나면
/// 타이머를 멈춘 뒤 일을 한 번 한다. <see cref="Cancel"/>은 미뤄 둔 일을 버린다(창을 닫을 때 등).
/// </summary>
public sealed class UiDebouncer
{
    private readonly DispatcherTimer _timer;

    /// <param name="delay">마지막 <see cref="Restart"/> 뒤 기다릴 시간.</param>
    /// <param name="action">시간이 지나면 할 일(UI 스레드).</param>
    public UiDebouncer(TimeSpan delay, Action action)
    {
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            action();
        };
    }

    /// <summary>미뤄 둔 일이 있는지(시간을 재는 중).</summary>
    public bool IsPending => _timer.IsEnabled;

    /// <summary>시간을 처음부터 다시 잰다(미뤄 둔 일이 없으면 새로 미룬다).</summary>
    public void Restart()
    {
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>시간이 아직 안 됐으면 미뤄 둔 일을 버린다.</summary>
    public void Cancel() => _timer.Stop();
}
