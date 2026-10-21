namespace Gpixel.App;

/// <summary>
/// 앱 휴지통 정책 편집 줄 두 개: [v] 기간 제한 [N]일, [v] 용량 제한 [N]GB. 끄면 제한 없음(0).
/// 환경 설정과 휴지통 창이 같이 쓴다.
/// </summary>
public sealed class TrashPolicyEditor : StackPanel
{
    private readonly CheckBox _daysOn, _sizeOn;
    private readonly NumericUpDown _days, _gb;
    private bool _updating;

    public event EventHandler? Changed;

    public TrashPolicyEditor(TrashPolicy policy)
    {
        Spacing = 2;
        _daysOn = Ui.Check("기간 제한");
        _days = Ui.Num(1, 3650, policy.Days > 0 ? policy.Days : 30, 70).Presets(1, 3, 7, 14, 30, 60, 90, 180, 365).Unit("일").RecentKey("trash.days");
        _sizeOn = Ui.Check("용량 제한");
        _gb = Ui.Num(0.1m, 1000, policy.MaxMegabytes > 0 ? Math.Round(policy.MaxMegabytes / 1024m, 1) : 1, 70, 1, 0.1m)
            .Presets(0.5m, 1, 2, 5, 10, 20, 50, 100).Unit("GB").RecentKey("trash.gb");
        _gb.FormatString = "0.#";
        _daysOn.IsChecked = policy.Days > 0;
        _sizeOn.IsChecked = policy.MaxMegabytes > 0;
        Children.Add(Ui.Row(_daysOn, _days, Ui.Label("일 — 지나면 영구 삭제")).Tip("끄면 기간과 관계없이 보관합니다."));
        Children.Add(Ui.Row(_sizeOn, _gb, Ui.Label("GB — 넘치면 오래된 것부터 영구 삭제")).Tip("끄면 용량과 관계없이 보관합니다."));
        foreach (var c in new Control[] { _daysOn, _sizeOn })
            ((CheckBox)c).IsCheckedChanged += (_, _) => OnChanged();
        _days.ValueChanged += (_, _) => OnChanged();
        _gb.ValueChanged += (_, _) => OnChanged();
        Sync();
    }

    public TrashPolicy Policy => new(
        _daysOn.On() ? (int)_days.NumValue() : 0,
        _sizeOn.On() ? Math.Max(1, (int)Math.Round(_gb.NumValue() * 1024)) : 0);

    private void Sync()
    {
        _days.IsEnabled = _daysOn.On();
        _gb.IsEnabled = _sizeOn.On();
    }

    private void OnChanged()
    {
        if (_updating) return;
        _updating = true;
        Sync();
        _updating = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
