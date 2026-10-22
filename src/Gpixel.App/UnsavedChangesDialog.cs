using Avalonia.VisualTree;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>저장 안 한 변경이 있는 문서 한 줄: 프로젝트, 보일 이름·폴더, 썸네일 그림, 덧붙일 설명(없으면 null).</summary>
internal sealed record UnsavedFile(SpriteProject Project, string Name, string? Folder, PixelImage Thumbnail, string? Note);

/// <summary>
/// 끝낼 때 저장 안 한 변경이 있는 문서(지금 문서 + 메모리에 남겨 둔 문서)를 한 번에 묻는다.
/// 결과: Yes = 체크한 파일 저장 후 끝내기(<see cref="Checked"/>), No = 저장하지 않고 끝내기, Cancel = 끝내지 않기.
/// </summary>
internal sealed class UnsavedChangesDialog : DialogBase
{
    private const int ThumbSize = 40;

    private readonly List<(UnsavedFile File, CheckBox Box)> _rows = new();
    private readonly List<WriteableBitmap> _thumbs = new();

    public UnsavedChangesDialog(IReadOnlyList<UnsavedFile> files) : base(UiStrings.S_UnsavedChangesDialog_UnsavedChangesDialog_CDE0AC4DCE, UiStrings.S_UnsavedChangesDialog_UnsavedChangesDialog_2541BFCB80)
    {
        Body.Children.Add(Ui.Label(files.Count == 1
            ? UiStrings.S_UnsavedChangesDialog_UnsavedChangesDialog_10BDA86213
            : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnsavedChangesDialog_UnsavedChangesDialog_8786C69C9A,files.Count), bold: true));
        var list = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var file in files) list.Children.Add(MakeRow(file));
        Body.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 360,
            MinWidth = 380,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        SetButtonResult(OkButton, DialogResult.Yes);
        OkButton.Width = 130;
        AddButton(UiStrings.S_UnsavedChangesDialog_UnsavedChangesDialog_A5BC13DA85, DialogResult.No);
        UpdateOk();
        Opened += (_, _) => OkButton.Focus();
        Closed += (_, _) => { foreach (var t in _thumbs) t.Dispose(); };
    }

    /// <summary>체크한(저장할) 문서들(보인 순서대로).</summary>
    public IReadOnlyList<SpriteProject> Checked => _rows.Where(r => r.Box.On()).Select(r => r.File.Project).ToList();

    private Control MakeRow(UnsavedFile file)
    {
        var box = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        box.IsCheckedChanged += (_, _) => UpdateOk();
        _rows.Add((file, box));
        var bitmap = ImageUtil.MakeThumbnail(file.Thumbnail, ThumbSize);
        _thumbs.Add(bitmap);
        var thumb = new Image
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Source = bitmap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        RenderOptions.SetBitmapInterpolationMode(thumb, BitmapInterpolationMode.None);
        var text = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = file.Name, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush });
        if (file.Folder is not null)
            text.Children.Add(new TextBlock { Text = file.Folder, Foreground = AppTheme.DimTextBrush, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 });
        if (file.Note is not null)
            text.Children.Add(new TextBlock { Text = file.Note, Foreground = AppTheme.DimTextBrush, FontSize = 11 });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { box, thumb, text } };
        // 줄 아무 곳이나 누르면 체크를 바꾼다.
        var host = new Border { Child = row, Background = Brushes.Transparent, Padding = new Thickness(2) };
        host.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<CheckBox>(includeSelf: true) is not null) return;
            box.IsChecked = !box.On();
        };
        ToolTip.SetTip(host, file.Project.Path ?? file.Name);
        return host;
    }

    private void UpdateOk() => OkButton.IsEnabled = _rows.Any(r => r.Box.On());
}
