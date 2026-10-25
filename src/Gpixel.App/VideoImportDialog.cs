using System.Globalization;
using Gpixel.App.Localization;
using Gpixel.Core.IO;

namespace Gpixel.App;

/// <summary>
/// 영상 가져오기 설정(ffmpeg로 풀기 전): 구간(시작·끝 초), 뽑을 초당 프레임, 최대 프레임 수, 크기(원래 크기 / 긴 변 맞춤 — 최근접·부드럽게)와
/// 나올 프레임 수·크기·프레임 길이·메모리 예상치.
/// </summary>
public sealed class VideoImportDialog : DialogBase
{
    /// <summary>예상 메모리가 이보다 크면 경고를 보인다.</summary>
    private const long WarnBytes = 1L << 30;

    private readonly VideoInfo _info;
    private readonly NumericUpDown _start, _end, _fps, _maxFrames, _longSide;
    private readonly ComboBox _scale;
    private readonly CheckBox _smooth;
    private readonly TextBlock _estimate, _warning;

    public VideoImportDialog(string fileName, VideoInfo info, VideoImportOptions? initial = null)
        : base(UiStrings.Video_Title, UiStrings.Video_Import)
    {
        _info = info;
        var o = initial ?? VideoImportPlan.Defaults(info);
        var s = info.Best;
        decimal duration = (decimal)(info.DurationSec ?? 0);
        var summary = Ui.Label(info.DurationSec is { } d
            ? UiStrings.FormatVideo_Summary(name: fileName, width: s.Width, height: s.Height, fps: Num(s.Fps), seconds: Num(d))
            : UiStrings.FormatVideo_SummaryNoDuration(name: fileName, width: s.Width, height: s.Height, fps: Num(s.Fps)));
        summary.Foreground = AppTheme.DimTextBrush;

        _start = Ui.Num(0, Math.Max(0, duration), (decimal)o.StartSec, 80, decimals: 2, increment: 0.5m).Unit(UiStrings.Video_SecondsUnit);
        _end = Ui.Num(0, Math.Max(0, duration), (decimal)(o.EndSec ?? (double)duration), 80, decimals: 2, increment: 0.5m).Unit(UiStrings.Video_SecondsUnit);
        _fps = Ui.Num((decimal)VideoImportPlan.MinFps, (decimal)VideoImportPlan.MaxFps, (decimal)o.Fps, 70, decimals: 2)
            .Presets(1, 4, 6, 8, 10, 12, 15, 24, 30, 60).RecentKey("video.fps");
        _maxFrames = Ui.Num(1, VideoImportPlan.MaxFramesLimit, o.MaxFrames, 70)
            .Presets(16, 24, 32, 48, 60, 120, 240, 500, 1000).RecentKey("video.maxFrames");
        _scale = Ui.Combo(170, UiStrings.Video_ScaleKeep, UiStrings.Video_ScaleFit);
        _scale.SelectedIndex = o.Scale == VideoScaleMode.FitLongSide ? 1 : 0;
        _longSide = Ui.Num(1, Gpixel.Core.Model.PixelImage.MaxSize, o.LongSide, 70)
            .Presets(Ui.SpriteSizes).Unit("px").RecentKey("video.longSide");
        _smooth = Ui.Check(UiStrings.Video_Smooth, o.Smooth).Tip(UiStrings.Video_SmoothTip);
        _estimate = Ui.Label("");
        _estimate.FontWeight = FontWeight.SemiBold;
        _warning = Ui.Label(UiStrings.Video_MemoryWarning);
        _warning.Foreground = AppTheme.WarningBrush;
        _warning.TextWrapping = TextWrapping.Wrap;
        _warning.MaxWidth = 460;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        void AddRow(string label, Control content)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Ui.Label(label);
            Grid.SetRow(l, row);
            Grid.SetRow(content, row);
            Grid.SetColumn(content, 1);
            grid.Children.Add(l);
            grid.Children.Add(content);
        }
        bool timed = !info.IsStill && duration > 0;
        if (timed)
            AddRow(UiStrings.Video_Range, Ui.Row(_start, Ui.Label("–"), _end,
                Ui.Button(UiStrings.Video_WholeClip, (_, _) => { _start.Value = 0; _end.Value = duration; })));
        AddRow(UiStrings.Video_Fps, Ui.Row(_fps, Ui.Label(UiStrings.FormatVideo_FpsNote(fps: Num(s.Fps)))));
        AddRow(UiStrings.Video_MaxFrames, _maxFrames);
        AddRow(UiStrings.Video_Scale, Ui.Row(_scale, _longSide, _smooth));

        Body.Children.Add(summary);
        Body.Children.Add(grid);
        Body.Children.Add(_estimate);
        Body.Children.Add(_warning);

        foreach (var n in new[] { _start, _end, _fps, _maxFrames, _longSide }) n.ValueChanged += (_, _) => Sync();
        _scale.SelectionChanged += (_, _) => Sync();
        _smooth.IsCheckedChanged += (_, _) => Sync();
        Sync();
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>고른 설정.</summary>
    public VideoImportOptions Options
    {
        get
        {
            double start = (double)_start.NumValue(), end = (double)_end.NumValue();
            bool whole = _info.DurationSec is not { } d || end >= d - 0.0005;
            return new VideoImportOptions
            {
                StartSec = start,
                EndSec = whole ? null : Math.Max(start, end),
                Fps = (double)_fps.NumValue(),
                MaxFrames = (int)_maxFrames.NumValue(),
                Scale = _scale.SelectedIndex == 1 ? VideoScaleMode.FitLongSide : VideoScaleMode.Keep,
                LongSide = (int)_longSide.NumValue(),
                Smooth = _smooth.On(),
            };
        }
    }

    private void Sync()
    {
        bool fit = _scale.SelectedIndex == 1;
        _longSide.IsEnabled = _smooth.IsEnabled = fit;
        var o = Options;
        int frames = VideoImportPlan.EstimateFrames(_info, o);
        var (w, h) = VideoImportPlan.OutputSize(_info.Best, o);
        long bytes = VideoImportPlan.EstimateBytes(_info, o);
        _estimate.Text = UiStrings.FormatVideo_Estimate(frames: frames, width: w, height: h, ms: VideoImportPlan.FrameDurationMs(o),
            mb: Math.Max(1, (bytes + (1 << 20) - 1) >> 20));
        _warning.IsVisible = bytes > WarnBytes;
    }
}

/// <summary>오래 걸리는 작업(ffmpeg로 프레임 풀기)의 진행 막대와 취소 단추. 작업은 다른 스레드에서 돌고, 끝나면 저절로 닫힌다.</summary>
public sealed class ProgressDialog<T> : DialogBase where T : class
{
    private readonly ProgressBar _bar;
    private readonly TextBlock _text;
    private readonly CancellationTokenSource _cts = new();
    private readonly int _total;
    private T? _result;
    private Exception? _error;
    private volatile bool _done;

    private ProgressDialog(string title, string message, int total) : base(title)
    {
        _total = Math.Max(1, total);
        OkButton.IsVisible = false;
        OkButton.IsDefault = false;
        _bar = new ProgressBar
        {
            Minimum = 0, Maximum = _total, IsIndeterminate = total <= 0, Width = 380, Height = 8, Margin = new Thickness(3, 6, 3, 0),
        };
        _text = Ui.Label("");
        _text.Foreground = AppTheme.DimTextBrush;
        Body.Children.Add(Ui.Label(message, bold: true));
        Body.Children.Add(_bar);
        Body.Children.Add(_text);
        CancelButtonControl.Click += (_, _) => _cts.Cancel();
        Closing += (_, _) => { if (!_done) _cts.Cancel(); };
    }

    /// <summary>
    /// 작업을 돌리며 진행을 보인다(<paramref name="total"/> = 예상 단계 수 — 0이면 진행을 모름, 진행 보고 = 끝낸 단계 수).
    /// 끝나면 결과, 취소하면 null. 작업이 던진 예외는 그대로 다시 던진다.
    /// </summary>
    public static T? Run(Visual? owner, string title, string message, int total, Func<IProgress<int>, CancellationToken, T> work)
    {
        var dlg = new ProgressDialog<T>(title, message, total);
        var progress = new Progress<int>(n =>
        {
            dlg._bar.Value = Math.Min(n, dlg._total);
            dlg._text.Text = UiStrings.FormatVideo_Progress(done: n, total: dlg._total);
        });
        dlg.Opened += (_, _) => Task.Run(() =>
        {
            try
            {
                dlg._result = work(progress, dlg._cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                dlg._error = ex;
            }
            dlg._done = true;
            Dispatcher.UIThread.Post(() => { if (dlg.IsVisible) dlg.DialogResult = DialogResult.OK; });
        });
        dlg.ShowDialog(owner);
        if (dlg._error is { } error) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        return dlg._cts.IsCancellationRequested ? null : dlg._result;
    }
}
