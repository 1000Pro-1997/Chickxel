using System.ComponentModel;
using Gpixel.Core.IO;

namespace Gpixel.App;

/// <summary>업데이트 창에서 고른 것.</summary>
public enum UpdateChoice
{
    Later,
    Skip,
    Install,
}

/// <summary>
/// 새 버전 알림: 버전, 릴리스 설명(원문 그대로), 지금 업데이트 / 다음 업데이트까지 띄우지 않기 / 릴리스 페이지 열기 / 나중에.
/// "지금 업데이트"는 창 안에서 내려받고(진행 막대·취소) 검증·압축 해제까지 마치면 <see cref="UpdateChoice.Install"/>로 닫힌다.
/// 자동 설치를 할 수 없으면(<paramref name="reason"/>) 그 이유를 보이고 릴리스 페이지를 권한다.
/// </summary>
public sealed class UpdateDialog : DialogBase
{
    private readonly ReleaseInfo _release;
    private readonly InstallPlan? _plan;
    private readonly Updater _updater;
    private readonly Button _skip, _page;
    private readonly StackPanel _progressRow;
    private readonly ProgressBar _progress;
    private readonly TextBlock _progressText, _note;
    private CancellationTokenSource? _cts;

    public UpdateDialog(ReleaseInfo release, InstallPlan? plan, string? reason, Updater updater)
        : base("Gpixel 업데이트", "지금 업데이트")
    {
        _release = release;
        _plan = plan;
        _updater = updater;

        string published = release.PublishedAt is { } at ? $"  ·  {at.ToLocalTime():yyyy-MM-dd} 공개" : "";
        Body.Children.Add(new TextBlock
        {
            Text = "새 버전이 나왔습니다",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = AppTheme.TextBrush,
            Margin = new Thickness(3, 0, 3, 2),
        });
        Body.Children.Add(Ui.Label($"Gpixel {release.Version}  ·  지금 버전 {AppInfo.VersionText}{published}"));
        string pageUrl = release.PageUrl.ToString();
        Body.Children.Add(new TextBox
        {
            Text = release.Notes.Trim().Length > 0
                ? release.Notes.Trim()
                : $"바뀐 점을 불러오지 못했습니다.\n\nGpixel {release.Version}의 바뀐 점은 아래 링크(릴리스 페이지)에서 볼 수 있습니다:\n{pageUrl}",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Width = 580,
            Height = 280,
            Margin = new Thickness(3, 6, 3, 3),
        });
        // 버전별 릴리스 페이지 링크(설명을 읽었든 못 읽었든 늘 보임)
        var link = new Button
        {
            Content = new TextBlock { Text = $"Gpixel {release.Version} 바뀐 점 — 릴리스 페이지 열기 ↗", TextDecorations = TextDecorations.Underline },
            Foreground = AppTheme.AccentBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(3, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
        }.Tip(pageUrl);
        link.Click += (_, _) => Updater.OpenUrl(this, pageUrl);
        Body.Children.Add(link);
        _note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 580,
            Margin = new Thickness(3, 4, 3, 0),
            Foreground = AppTheme.WarningBrush,
            IsVisible = false,
        };
        Body.Children.Add(_note);
        if (reason is not null) ShowNote(reason, warning: true);

        _progress = new ProgressBar { Minimum = 0, Maximum = 1, Width = 580, Height = 8, Margin = new Thickness(3, 6, 3, 0) };
        _progressText = Ui.Label("");
        _progressText.Foreground = AppTheme.DimTextBrush;
        _progressRow = new StackPanel { Spacing = 2, IsVisible = false };
        _progressRow.Children.Add(_progress);
        _progressRow.Children.Add(_progressText);
        Body.Children.Add(_progressRow);

        OkButton.IsEnabled = plan is not null;
        OkButton.Click += OnInstallClick;
        _skip = AddButton("다음 업데이트까지 띄우지 않기", DialogResult.No);
        _skip.Tip($"Gpixel {release.Version}은(는) 다시 알리지 않고, 그보다 새 버전이 나오면 다시 알립니다.");
        _skip.Click += (_, _) => Choice = UpdateChoice.Skip;
        _page = AddButton("릴리스 페이지 열기", DialogResult.Yes);
        _page.Click += (_, _) =>
        {
            DialogResult = DialogResult.None; // 창은 그대로 둔다
            Updater.OpenUrl(this, _release.PageUrl.ToString());
        };
        if (plan is null)
        {
            OkButton.IsDefault = false;
            _page.IsDefault = true;
        }
        CancelButtonControl.Content = "나중에";
        CancelButtonControl.Tip("다음에 실행할 때 다시 알립니다.");
        CancelButtonControl.Click += (_, _) =>
        {
            if (_cts is not { } cts) return; // 나중에 → 닫기
            DialogResult = DialogResult.None;
            cts.Cancel();
        };
        foreach (var b in new[] { OkButton, _skip, _page, CancelButtonControl })
        {
            b.Width = double.NaN;
            b.MinWidth = 90;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        if (plan is not null)
        {
            OkButton.Background = AppTheme.AccentBrush;
            OkButton.Foreground = Brushes.White;
            OkButton.FontWeight = FontWeight.Bold;
        }
        // 내려받는 중에 창을 닫으면 내려받기를 멈춘다(준비 폴더는 Updater가 지운다).
        Closing += (_, _) => _cts?.Cancel();
    }

    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    /// <summary>내려받아 검증·압축 해제까지 마친 새 버전(<see cref="UpdateChoice.Install"/>일 때).</summary>
    public StagedUpdate? Staged { get; private set; }

    private void ShowNote(string text, bool warning)
    {
        _note.Text = text;
        _note.Foreground = warning ? AppTheme.WarningBrush : AppTheme.DimTextBrush;
        _note.IsVisible = true;
    }

    private void SetBusy(bool busy)
    {
        OkButton.IsVisible = !busy;
        _skip.IsVisible = !busy;
        _page.IsVisible = !busy;
        CancelButtonControl.Content = busy ? "취소" : "나중에";
        _progressRow.IsVisible = busy;
        if (!busy) ShowProgress(new UpdateProgress("", 0, 0));
    }

    private void ShowProgress(UpdateProgress p)
    {
        const double mb = 1024 * 1024;
        _progress.IsIndeterminate = p.Total <= 0 && p.Stage.Length > 0;
        _progress.Value = p.Total > 0 ? Math.Clamp((double)p.Done / p.Total, 0, 1) : 0;
        _progressText.Text = p.Total > 0
            ? $"{p.Stage} {p.Done / mb:0.0} / {p.Total / mb:0.0} MB ({100.0 * p.Done / p.Total:0}%)"
            : p.Done > 0 ? $"{p.Stage} {p.Done / mb:0.0} MB" : p.Stage;
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        DialogResult = DialogResult.None; // 닫지 않고 창 안에서 내려받는다
        if (_plan is null || _cts is not null) return;
        var cts = _cts = new CancellationTokenSource();
        _note.IsVisible = false;
        SetBusy(true);
        try
        {
            var staged = await _updater.DownloadAndStageAsync(_plan, new Progress<UpdateProgress>(ShowProgress), cts.Token);
            if (cts.IsCancellationRequested)
            {
                Updater.TryDeleteDirectory(staged.Directory);
                ShowNote("내려받기를 취소했습니다.", warning: false);
                return;
            }
            Staged = staged;
            Choice = UpdateChoice.Install;
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            ShowNote("내려받기를 취소했습니다.", warning: false);
        }
        catch (Exception ex) when (ex is UpdateException or HttpRequestException or IOException or UnauthorizedAccessException
            or OperationCanceledException or InvalidDataException or Win32Exception)
        {
            ShowNote(ex switch
            {
                UpdateException => ex.Message,
                OperationCanceledException => "서버가 응답하지 않습니다. 잠시 뒤 다시 시도하세요.",
                HttpRequestException => $"내려받지 못했습니다. 인터넷 연결을 확인하세요.\n({ex.Message})",
                _ => $"업데이트를 준비하지 못했습니다.\n({ex.Message})",
            } + "\n지금 버전은 그대로입니다.", warning: true);
        }
        finally
        {
            _cts = null;
            cts.Dispose();
            if (Choice != UpdateChoice.Install) SetBusy(false);
        }
    }
}
