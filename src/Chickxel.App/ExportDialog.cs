using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>내보내기 창의 형식 종류(마지막으로 고른 것은 <see cref="AppSettings.ExportFormat"/>, 이미지의 파일 형식은 프로젝트의 <see cref="ExportSettings.ImageFormat"/>).</summary>
public enum ExportFormat
{
    /// <summary>GIF 애니메이션.</summary>
    Gif,
    /// <summary>움직이는 PNG(APNG).</summary>
    Apng,
    /// <summary>이미지 시트(PNG·JPG 등, Unity .meta 선택) — 프로젝트의 내보내기 설정대로.</summary>
    Image,
    /// <summary>프레임마다 PNG(폴더로).</summary>
    Frames,
}

/// <summary>
/// 내보내기 창(Ctrl+E, 파일 > 내보내기…, 미리보기 "GIF로 저장…"). 왼쪽 맨 위 "형식" 묶음의 콤보에서 GIF 애니메이션 · 움직이는 PNG · PNG · JPG · TGA · BMP · 프레임마다 PNG를 고른다.
/// 왼쪽 설정 칸(세로 스크롤) | 끌어서 너비를 바꾸는 나눔줄 | 오른쪽 미리보기 — 형식이 바뀌어도 칸 너비는 그대로.
/// <list type="bullet">
/// <item>이미지 형식(<see cref="SheetExportPages"/>): 콤보 아래 "Unity용으로 내보내기(.meta)" 체크, 그 아래 시트 설정 묶음(+ Unity Import Settings), 오른쪽 결과 시트 미리보기.
/// 고른 형식은 프로젝트의 내보내기 이미지 형식이 된다(실행취소 1단계). 파란 "내보내기"는 OK로 닫혀 호출한 쪽이 프로젝트 설정대로 내보내고,
/// "다른 이름으로 저장…"은 같은 시트를 독립 파일로 쓴다(창은 열린 채 결과 줄).</item>
/// <item>GIF·APNG·프레임마다 PNG(<see cref="AnimationExportPart"/>): 범위·배율·속도·반복·배경 묶음과 재생 미리보기. "내보내기…"는 저장 창을 띄워 쓴다(창은 열린 채).</item>
/// </list>
/// 형식·창 크기·위치·나눔 비율은 <see cref="AppSettings"/>에 기억한다. Enter로 닫히지 않는다(Esc = 닫기).
/// </summary>
public sealed class ExportDialog : DialogBase
{
    private const int DefaultWidth = 1100, DefaultHeight = 760, MinW = 760, MinH = 480;

    private readonly AppSettings _settings;
    private readonly SheetExportPages _sheet;
    private readonly AnimationExportPart _anim;
    /// <summary>왼쪽 설정 | 나눔줄 | 오른쪽 미리보기.</summary>
    private readonly Grid _columns;
    private readonly ComboBox _formatCombo;
    /// <summary>형식 콤보 항목 순서: GIF, APNG, 이미지 형식들(<see cref="ExportImageFormat"/> 순서), 프레임마다 PNG.</summary>
    private readonly (ExportFormat Kind, ExportImageFormat Image)[] _choices;
    private readonly Button _saveCopy;
    private readonly Control _copyResultHost;
    private readonly List<string> _exported = new();
    private ExportFormat _format;
    private bool _canExportAnimation, _syncingCombo;

    /// <param name="ctx">편집기 상태(현재 프로젝트가 있어야 함).</param>
    /// <param name="settings">형식·창 크기·위치·나눔 비율·접은 묶음·애니메이션 설정을 기억할 앱 설정.</param>
    /// <param name="format">처음 고를 형식 종류(이미지면 프로젝트의 이미지 형식).</param>
    /// <param name="ppuChanged">사용자가 PPU를 바꿨을 때(마지막 PPU 기억용).</param>
    public ExportDialog(EditorContext ctx, AppSettings settings, ExportFormat format, Action<float>? ppuChanged = null)
        : base(UiStrings.S_UnityExportDialog_UnityExportDialog_C53EAC78FD)
    {
        _settings = settings;
        // 시트 설정은 바로 적용되고 다른 이름으로 저장·애니메이션은 내보낸 뒤에도 창이 열려 있으므로 취소가 아니라 닫기.
        CancelButtonControl.Content = UiStrings.S_Ui_CloseOnly_11312F9A81;
        OkButton.Background = AppTheme.AccentBrush;
        OkButton.Foreground = Brushes.White;
        OkButton.FontWeight = FontWeight.Bold;
        OkButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        MinWidth = MinW;
        MinHeight = MinH;
        RestorePlacement();

        _sheet = new SheetExportPages(ctx, settings, this, Exported, ppuChanged);
        _anim = new AnimationExportPart(ctx, settings, this, can =>
        {
            _canExportAnimation = can;
            UpdateOkButton();
        }, Exported);

        // ── 형식 묶음: 제목 줄(오른쪽 끝 프리셋) + 콤보 + 이미지 형식의 Unity 체크·JPG 품질 ──
        _choices =
        [
            (ExportFormat.Gif, default), (ExportFormat.Apng, default),
            .. Enum.GetValues<ExportImageFormat>().Select(f => (ExportFormat.Image, f)),
            (ExportFormat.Frames, default),
        ];
        _formatCombo = Ui.Combo(0, [.. _choices.Select(c => ChoiceName(c.Kind, c.Image))]);
        _formatCombo.Width = double.NaN;
        _formatCombo.HorizontalAlignment = HorizontalAlignment.Stretch;
        var title = new TextBlock { Text = UiStrings.Export_Format, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        DockPanel.SetDock(_sheet.PresetButton, Dock.Right);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2), Children = { _sheet.PresetButton, title } };
        var formatGroup = new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4, 6, 6),
            Margin = new Thickness(2, 4, 2, 2),
            Background = Brushes.Transparent,
            Child = Ui.Stack(header, _formatCombo, _sheet.FormatOptions),
        }.HelpAt(SheetExportPages.GuidePage);

        // ── 왼쪽 설정 | 나눔줄 | 오른쪽 미리보기 ──
        var left = Ui.ScrollHost(Ui.Stack(formatGroup, _sheet.SettingsColumn, _anim.Groups));
        var right = new Panel { Children = { _sheet.Preview, _anim.PreviewColumn } };
        double split = double.IsFinite(settings.ExportSplit) ? Math.Clamp(settings.ExportSplit, 0.2, 0.8) : 0.46;
        _columns = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(split, GridUnitType.Star) { MinWidth = 340 },
                new ColumnDefinition(6, GridUnitType.Pixel),
                new ColumnDefinition(1 - split, GridUnitType.Star) { MinWidth = 260 },
            },
        };
        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Columns,
            Background = AppTheme.BorderBrush,
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        }.Tip(UiStrings.S_UnityExportDialog_UnityExportDialog_22A1711719).HelpAt(SheetExportPages.GuidePage, anchor: "Drag ⑦ the divider");
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(right, 2);
        _columns.Children.Add(left);
        _columns.Children.Add(splitter);
        _columns.Children.Add(right);
        // 대화상자 본문(StackPanel)은 높이를 주지 않으므로 칸을 바깥 DockPanel의 채우기 칸에 바로 둔다.
        var root = (DockPanel)Content!;
        root.Children.Remove((Control)Body.Parent!);
        root.Children.Add(_columns);

        // ── 단추: [내보내기(…)] [다른 이름으로 저장…(이미지)] [닫기], 왼쪽 끝 = 다른 이름으로 저장 결과 줄 ──
        _saveCopy = Ui.Button(UiStrings.Export_SaveCopy, (_, _) => Dispatcher.UIThread.Post(_sheet.SaveCopy))
            .Tip(UiStrings.Export_SaveCopyTip).HelpAt(SheetExportPages.GuidePage);
        _saveCopy.MinWidth = 90;
        _saveCopy.Width = double.NaN;
        _saveCopy.Padding = new Thickness(10, 3);
        AddFooter(_saveCopy, CancelButtonControl);
        _copyResultHost = new Panel { Children = { _sheet.CopyResult } };
        AddFooterLeft(_copyResultHost);

        format = Enum.IsDefined(format) ? format : ExportFormat.Image;
        _formatCombo.SelectedIndex = IndexOf(format, _sheet.ImageFormat);
        _formatCombo.SelectionChanged += (_, _) =>
        {
            if (_syncingCombo || _formatCombo.SelectedIndex < 0) return;
            var (kind, image) = _choices[_formatCombo.SelectedIndex];
            if (kind == ExportFormat.Image) _sheet.SetImageFormat(image);
            ApplyFormat(kind);
        };
        // 실행취소·프리셋 등으로 프로젝트의 이미지 형식이 바뀌면 콤보도 따라간다.
        _sheet.Refreshed += SyncImageChoice;
        // 이미지 형식은 OK로 닫혀 호출한 쪽이 내보내고, 다른 형식은 창을 열어 둔 채 저장 창을 띄운다(결과 보기).
        OkButton.Click += (_, _) =>
        {
            if (_format == ExportFormat.Image) return;
            DialogResult = DialogResult.None;
            Dispatcher.UIThread.Post(_anim.Export);
        };
        ApplyFormat(format);
    }

    /// <summary>이 창에서 다른 이름으로 저장·애니메이션·프레임으로 쓴 파일(파일 목록 새로 고침용).</summary>
    public IReadOnlyList<string> ExportedPaths => _exported;
    /// <summary>마지막으로 쓴 결과 문구(상태 표시줄용, 없으면 null).</summary>
    public string? ResultMessage { get; private set; }

    private void Exported(IReadOnlyList<string> paths, string message)
    {
        _exported.AddRange(paths);
        ResultMessage = message;
    }

    private static string ChoiceName(ExportFormat kind, ExportImageFormat image) => kind switch
    {
        ExportFormat.Gif => UiStrings.S_GifExportDialog_ChooseAndExport_AC2A7BE712,
        ExportFormat.Apng => UiStrings.Export_FormatApng,
        ExportFormat.Image => SheetExportPages.FormatName(image),
        _ => UiStrings.Export_FormatFrames,
    };

    private int IndexOf(ExportFormat kind, ExportImageFormat image)
        => Math.Max(0, Array.FindIndex(_choices, c => c.Kind == kind && (kind != ExportFormat.Image || c.Image == image)));

    /// <summary>이미지 형식을 보는 중이면 콤보를 프로젝트의 이미지 형식에 맞춘다.</summary>
    private void SyncImageChoice()
    {
        if (_format != ExportFormat.Image) return;
        int index = IndexOf(ExportFormat.Image, _sheet.ImageFormat);
        if (_formatCombo.SelectedIndex == index) return;
        _syncingCombo = true;
        _formatCombo.SelectedIndex = index;
        _syncingCombo = false;
    }

    /// <summary>형식 종류를 바꾼다: 묶음·미리보기·단추와 대상 도움말을 그 형식에 맞추고 기억한다.</summary>
    private void ApplyFormat(ExportFormat format)
    {
        _format = format;
        _settings.ExportFormat = format;
        bool image = format == ExportFormat.Image;
        _sheet.FormatOptions.IsVisible = _sheet.SettingsColumn.IsVisible = _sheet.Preview.IsVisible = _sheet.PresetButton.IsVisible = image;
        _saveCopy.IsVisible = _copyResultHost.IsVisible = image;
        _anim.Groups.IsVisible = _anim.PreviewColumn.IsVisible = !image;
        _sheet.PreviewShown = image;
        if (image) _anim.Hide();
        else _anim.Show(format);
        // 대상 도움말(F1): 이미지 = "내보내기 창" 쪽, 다른 형식 = "GIF · APNG · 프레임 PNG로 내보내기" 쪽.
        if (image)
        {
            this.HelpAt(SheetExportPages.GuidePage);
            OkButton.Content = UiStrings.S_UnityExportDialog_UnityExportDialog_C53EAC78FD;
            OkButton.Tip(UiStrings.S_UnityExportDialog_UnityExportDialog_C7A33C7D9E).HelpAt(SheetExportPages.GuidePage, anchor: "①");
        }
        else
        {
            this.HelpAt(AnimationExportPart.GuidePage, shot: "screen-gif");
            OkButton.Content = UiStrings.S_GifExportDialog_GifExportDialog_AEA1CD8AEB;
            OkButton.Tip(null).HelpAt(AnimationExportPart.GuidePage, anchor: "Export… opens a save dialog");
        }
        UpdateOkButton();
    }

    /// <summary>애니메이션 형식은 내보낼 프레임이 있을 때만 내보낼 수 있다.</summary>
    private void UpdateOkButton() => OkButton.IsEnabled = _format == ExportFormat.Image || _canExportAnimation;

    /// <summary>기억한 창 크기·위치(지금 어느 화면에도 걸치지 않으면 주인 창 가운데, 크기는 화면 작업 영역 안으로).</summary>
    private void RestorePlacement()
    {
        var s = _settings;
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        double maxW = screen is null ? 3000 : screen.WorkingArea.Width / screen.Scaling;
        double maxH = screen is null ? 3000 : screen.WorkingArea.Height / screen.Scaling;
        Width = Math.Clamp(s.ExportWidth > 0 ? s.ExportWidth : DefaultWidth, MinW, Math.Max(MinW, maxW));
        Height = Math.Clamp(s.ExportHeight > 0 ? s.ExportHeight : DefaultHeight, MinH, Math.Max(MinH, maxH));
        var bounds = new PixelRect(s.ExportX, s.ExportY, (int)Width, (int)Height);
        if (s.ExportX >= 0 && s.ExportY >= 0 && Screens.All.Any(sc => sc.WorkingArea.Intersects(bounds)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(s.ExportX, s.ExportY);
        }
    }

    /// <summary>나눔 비율과 창 크기·위치를 설정에 적는다(최대화·최소화 중이면 크기·위치는 그대로).</summary>
    private void SavePlacement()
    {
        double left = _columns.ColumnDefinitions[0].ActualWidth, right = _columns.ColumnDefinitions[2].ActualWidth;
        if (left > 0 && right > 0) _settings.ExportSplit = Math.Round(left / (left + right), 3);
        if (WindowState != WindowState.Normal) return;
        _settings.ExportX = Math.Max(0, Position.X);
        _settings.ExportY = Math.Max(0, Position.Y);
        if (ClientSize.Width > 0) _settings.ExportWidth = (int)Math.Round(ClientSize.Width);
        if (ClientSize.Height > 0) _settings.ExportHeight = (int)Math.Round(ClientSize.Height);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _sheet.Attach();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        // 닫기·Esc·창 닫기 단추로 닫아도 입력 중이던 이름·폴더가 반영되게(이미지 내보내기 전에).
        _sheet.Detach();
        _anim.Close();
        SavePlacement();
    }
}
