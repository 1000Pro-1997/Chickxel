using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 프레임 길이 창(타임라인 프레임 머리글 더블클릭·우클릭 "길이…"): 고른 프레임들의 길이를 ms·FPS·초·60fps 게임 프레임 수로 같게 맞추거나,
/// 배속·전체 길이로 지금 길이 비율을 지킨 채 바꾼다(<see cref="FrameLength"/>). 결과 묶음에 새 길이와 전체 길이가 바로 보인다.
/// 확인하면 새 길이(고른 프레임 차례)를 돌려줄 뿐이고 프로젝트에 넣는 것은 부르는 쪽.
/// </summary>
internal sealed class FrameDurationDialog : DialogBase
{
    // 고른 방법은 앱을 켜 둔 동안 기억한다.
    private static FrameLengthMode s_mode = FrameLengthMode.Milliseconds;

    private readonly IReadOnlyList<int> _current;
    private readonly ComboBox _mode;
    private readonly NumericUpDown[] _values;
    private readonly TextBlock[] _units;
    private readonly TextBlock _each, _total;

    /// <summary>확인했을 때 넣을 새 길이(ms, 고른 프레임 차례).</summary>
    public int[] Durations { get; private set; } = [];

    /// <summary>고른 프레임의 지금 길이 <paramref name="current"/>로 창을 띄운다. 확인하면 새 길이, 취소하면 null.</summary>
    public static int[]? Ask(Visual owner, IReadOnlyList<int> current)
    {
        var dialog = new FrameDurationDialog(current);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Durations : null;
    }

    private FrameDurationDialog(IReadOnlyList<int> current) : base(UiStrings.S_TimelinePanel_SetDuration_6EBBF2FC7D)
    {
        _current = current;
        int first = current[0];
        int sum = current.Sum();

        _mode = Ui.Combo(170, UiStrings.FrameLength_Ms, UiStrings.FrameLength_Fps, UiStrings.FrameLength_Seconds,
            UiStrings.FrameLength_GameFrames, UiStrings.FrameLength_Speed, UiStrings.FrameLength_Total);
        _values =
        [
            Ui.Num(FrameLength.MinMs, FrameLength.MaxMs, first, 80).Presets(50, 80, 100, 120, 150, 200).Unit("ms"),
            Ui.Num(0.02m, 1000, (decimal)Math.Round(1000.0 / first, 2), 80, decimals: 2).Presets(6, 8, 10, 12, 15, 20, 24, 30, 60).Unit("fps"),
            Ui.Num(0.001m, 60, first / 1000m, 80, decimals: 3, increment: 0.05m).Presets(0.05m, 0.1m, 0.15m, 0.2m, 0.25m, 0.5m, 1).Unit(UiStrings.FrameLength_SecondsUnit),
            Ui.Num(1, FrameLength.MaxMs * FrameLength.GameFps / 1000, Math.Max(1, (int)Math.Round(first * FrameLength.GameFps / 1000.0)), 80)
                .Presets(1, 2, 3, 4, 5, 6, 8, 10, 12).Unit(UiStrings.FrameLength_GameFramesUnit),
            Ui.Num(0.05m, 20, 1, 80, decimals: 2, increment: 0.25m).Presets(0.25m, 0.5m, 0.75m, 1, 1.25m, 1.5m, 2, 3, 4).Unit("×"),
            Ui.Num(current.Count * FrameLength.MinMs, (decimal)current.Count * FrameLength.MaxMs, sum, 90).Unit("ms"),
        ];
        _units =
        [
            Ui.Label("ms"), Ui.Label("fps"), Ui.Label(UiStrings.FrameLength_SecondsUnit),
            Ui.Label(UiStrings.FrameLength_GameFramesUnit), Ui.Label("×"), Ui.Label("ms"),
        ];
        var row = Ui.Row(_mode);
        for (int i = 0; i < _values.Length; i++)
        {
            _values[i].VerticalAlignment = _units[i].VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(_values[i]);
            row.Children.Add(_units[i]);
            _values[i].ValueChanged += (_, _) => Refresh();
        }
        var tip = Ui.InfoBadge(UiStrings.FrameLength_Tip);
        tip.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(tip);
        _mode.SelectedIndex = (int)s_mode;
        _mode.SelectionChanged += (_, _) =>
        {
            CarryOver();
            Refresh();
        };

        _each = Ui.Label("");
        _total = Ui.Label("");
        _total.Foreground = AppTheme.DimTextBrush;

        Body.Children.Add(Ui.Label(UiStrings.FormatFrameLength_Count(count: current.Count)));
        Body.Children.Add(Ui.Group(UiStrings.FrameLength_Method, row));
        Body.Children.Add(Ui.Group(UiStrings.FrameLength_Result, Ui.Stack(_each, _total)));
        this.HelpAt("screen-timeline");
        Refresh();
    }

    private FrameLengthMode Mode => _mode.SelectedIndex is >= 0 and <= (int)FrameLengthMode.Total ? (FrameLengthMode)_mode.SelectedIndex : FrameLengthMode.Milliseconds;

    /// <summary>
    /// 방법을 바꾸면 지금 결과를 새 방법의 값으로 옮겨 적는다(80ms를 넣고 FPS로 바꾸면 12.5): 같은 길이 방법은 결과 첫 프레임 길이를, 전체 길이는 결과 합을.
    /// 배속은 지금 길이 기준이라 그대로 둔다.
    /// </summary>
    private void CarryOver()
    {
        if (Durations.Length == 0) return;
        double ms = Durations[0];
        decimal? value = Mode switch
        {
            FrameLengthMode.Milliseconds => (decimal)ms,
            FrameLengthMode.Fps => (decimal)Math.Round(1000.0 / ms, 2),
            FrameLengthMode.Seconds => (decimal)(ms / 1000.0),
            FrameLengthMode.GameFrames => (decimal)Math.Max(1, Math.Round(ms * FrameLength.GameFps / 1000.0)),
            FrameLengthMode.Total => Durations.Sum(),
            _ => null,
        };
        if (value is { } v)
        {
            var box = _values[(int)Mode];
            box.Value = Math.Clamp(v, box.Minimum, box.Maximum);
        }
    }

    /// <summary>고른 방법의 값 칸만 보이고, 새 길이와 결과 글을 다시 계산한다.</summary>
    private void Refresh()
    {
        if (_each is null) return;
        s_mode = Mode;
        for (int i = 0; i < _values.Length; i++) _values[i].IsVisible = _units[i].IsVisible = i == (int)s_mode;
        Durations = FrameLength.Apply(_current, s_mode, (double)_values[(int)s_mode].NumValue());

        var culture = CultureInfo.CurrentCulture;
        int min = Durations.Min(), max = Durations.Max();
        _each.Text = min == max
            ? UiStrings.FormatFrameLength_Each(ms: min.ToString(culture), fps: (1000.0 / min).ToString("0.##", culture),
                game: (min * FrameLength.GameFps / 1000.0).ToString("0.##", culture))
            : UiStrings.FormatFrameLength_EachRange(min: min.ToString(culture), max: max.ToString(culture));
        _total.Text = UiStrings.FormatFrameLength_TotalLine(from: (_current.Sum() / 1000.0).ToString("0.###", culture),
            to: (Durations.Sum() / 1000.0).ToString("0.###", culture));
    }
}
