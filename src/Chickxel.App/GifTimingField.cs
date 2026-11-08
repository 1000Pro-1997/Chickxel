using Chickxel.App.Localization;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>
/// GIF 재생 속도 고르기 한 줄: 방식(원래 속도 / 배속 / 같은 FPS) + 그 방식의 값 + 설명 정보박스.
/// GIF 내보내기 창과 갤러리에 올리기 창이 같이 쓰고, 마지막 값은 앱 설정(<see cref="AppSettings.GifTimingMode"/>·<see cref="AppSettings.GifSpeed"/>·
/// <see cref="AppSettings.GifFps"/>)에 기억한다.
/// </summary>
public sealed class GifTimingField : StackPanel
{
    private readonly ComboBox _mode;
    private readonly NumericUpDown _speed, _fps;
    private readonly TextBlock _speedUnit, _fpsUnit;

    /// <summary>방식이나 값이 바뀜.</summary>
    public event Action? Changed;

    public GifTimingField(AppSettings settings)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        _mode = Ui.Combo(150, UiStrings.GifTiming_Original, UiStrings.GifTiming_Speed, UiStrings.GifTiming_Fps);
        _mode.SelectedIndex = Enum.IsDefined(settings.GifTimingMode) ? (int)settings.GifTimingMode : 0;
        _speed = Ui.Num((decimal)GifTiming.MinSpeed, (decimal)GifTiming.MaxSpeed, (decimal)settings.GifSpeed, width: 64, decimals: 2, increment: 0.25m)
            .Presets(0.25m, 0.5m, 0.75m, 1, 1.25m, 1.5m, 2, 3, 4).Unit("×").RecentKey("gif.speed");
        _fps = Ui.Num(GifTiming.MinFps, GifTiming.MaxFps, settings.GifFps, width: 60)
            .Presets(5, 8, 10, 12, 15, 20, 24, 30, 50).Unit("fps").RecentKey("gif.fps");
        _speedUnit = Ui.Label("×");
        _fpsUnit = Ui.Label("fps");
        foreach (var c in new Control[] { _mode, _speed, _speedUnit, _fps, _fpsUnit, Ui.InfoBadge(UiStrings.GifTiming_Tip) })
        {
            c.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(c);
        }
        _mode.SelectionChanged += (_, _) =>
        {
            ShowValue();
            Changed?.Invoke();
        };
        _speed.ValueChanged += (_, _) => Changed?.Invoke();
        _fps.ValueChanged += (_, _) => Changed?.Invoke();
        ShowValue();
    }

    /// <summary>고른 속도.</summary>
    public GifTiming Timing => Mode == GifTimingMode.Original
        ? GifTiming.Original
        : new GifTiming { Mode = Mode, Speed = (double)_speed.NumValue(), Fps = (int)_fps.NumValue() };

    private GifTimingMode Mode => _mode.SelectedIndex is >= 0 and <= (int)GifTimingMode.Fps ? (GifTimingMode)_mode.SelectedIndex : GifTimingMode.Original;

    /// <summary>고른 방식의 값 상자만 보인다.</summary>
    private void ShowValue()
    {
        _speed.IsVisible = _speedUnit.IsVisible = Mode == GifTimingMode.Speed;
        _fps.IsVisible = _fpsUnit.IsVisible = Mode == GifTimingMode.Fps;
    }

    /// <summary>지금 값을 앱 설정에 적는다(다음에 여는 GIF 내보내기·갤러리에 올리기 창이 이어 씀).</summary>
    public void Save(AppSettings settings)
    {
        settings.GifTimingMode = Mode;
        settings.GifSpeed = (double)_speed.NumValue();
        settings.GifFps = (int)_fps.NumValue();
    }
}
