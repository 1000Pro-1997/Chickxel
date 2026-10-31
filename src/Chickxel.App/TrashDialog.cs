using Chickxel.App.Localization;
namespace Chickxel.App;

/// <summary>앱 휴지통 보기: 지운 항목 목록(최근 것부터), 되살리기·영구 삭제·비우기, 보관 정책(기간·용량, 각각 끌 수 있음).</summary>
public sealed class TrashDialog : DialogBase
{
    private readonly AppTrash _trash;
    private readonly ListBox _list;
    private readonly TrashPolicyEditor _policy;
    private readonly TextBlock _usage;
    private readonly Action<IReadOnlyList<string>> _restored;
    private List<AppTrash.Entry> _entries = new();
    private readonly Image _preview;
    private readonly TextBlock _previewInfo;
    private const int ThumbSize = 40, PreviewSize = 220;

    /// <param name="policy">보관 정책.</param>
    /// <param name="policyChanged">정책을 바꿨을 때(설정 저장).</param>
    /// <param name="restored">되살린 파일 경로들.</param>
    public TrashDialog(AppTrash trash, TrashPolicy policy, Action<TrashPolicy> policyChanged, Action<IReadOnlyList<string>> restored) : base(UiStrings.S_TrashDialog_TrashDialog_5DAE31D03A)
    {
        _trash = trash;
        _restored = restored;
        CloseOnly();
        _list = new ListBox
        {
            Width = 520,
            Height = 320,
            SelectionMode = SelectionMode.Multiple,
            Background = AppTheme.InputBrush,
            Foreground = AppTheme.TextBrush,
        };
        _policy = new TrashPolicyEditor(policy);
        _policy.Changed += (_, _) =>
        {
            policyChanged(_policy.Policy);
            Reload();
        };
        _usage = Ui.Label("");
        _usage.Foreground = AppTheme.DimTextBrush;
        var restore = Ui.Button(UiStrings.S_TrashDialog_TrashDialog_2C94038691, (_, _) => RestoreSelected(), 100);
        var delete = Ui.Button(UiStrings.S_TrashDialog_TrashDialog_C67DF450F5, (_, _) => DeleteSelected(), 100);
        var empty = Ui.Button(UiStrings.S_TrashDialog_TrashDialog_9D1463258E, (_, _) => EmptyAll(), 100);
        Body.Children.Add(Ui.Label(UiStrings.S_TrashDialog_TrashDialog_A74F61165C));
        Body.Children.Add(_policy);
        Body.Children.Add(_usage);
        // 왼쪽 목록(이미지면 작은 미리보기), 오른쪽 고른 항목의 큰 미리보기.
        _preview = new Image { Width = PreviewSize, Height = PreviewSize, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(_preview, BitmapInterpolationMode.None);
        _previewInfo = new TextBlock { Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextWrapping = TextWrapping.Wrap, Width = PreviewSize, Margin = new Thickness(0, 4, 0, 0) };
        var previewBox = new StackPanel
        {
            Margin = new Thickness(8, 0, 0, 0),
            Children =
            {
                new Border { Width = PreviewSize + 2, Height = PreviewSize + 2, Background = AppTheme.InputBrush, BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), Child = _preview },
                _previewInfo,
            },
        };
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _list, previewBox } });
        _list.SelectionChanged += (_, _) => ShowPreview();
        Body.Children.Add(Ui.Row(restore, delete, empty));
        _list.DoubleTapped += (_, _) => RestoreSelected();
        Reload();
    }

    private TrashPolicy Policy => _policy.Policy;

    private void Reload()
    {
        _trash.Purge(Policy);
        _entries = _trash.Entries();
        long total = _entries.Sum(_trash.SizeOf);
        _usage.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_TrashDialog_Reload_FAE72AD0B4,_entries.Count,AppTrash.FormatSize(total)) + (Policy.MaxBytes > 0 ? $" / {AppTrash.FormatSize(Policy.MaxBytes)}" : "");
        _list.Items.Clear();
        foreach (var e in _entries)
        {
            var files = string.Join(", ", e.Files.Select(f => Path.GetFileName(f.OriginalPath)));
            var thumb = new Image { Width = ThumbSize, Height = ThumbSize, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapInterpolationMode(thumb, BitmapInterpolationMode.None);
            thumb.Source = LoadThumb(e, ThumbSize);
            var item = new ListBoxItem
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        thumb,
                        new TextBlock
                        {
                            Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_TrashDialog_Reload_A1E6202FC4,e.DeletedUtc.ToLocalTime(),e.Label,(Policy.Days > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_TrashDialog_Reload_00DDCF5548,AppTrash.DaysLeft(e, Policy.Days)) : UiStrings.S_TrashDialog_Reload_734995695F),AppTrash.FormatSize(_trash.SizeOf(e)),files),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                },
                Tag = e,
            };
            ToolTip.SetTip(item, string.Join("\n", e.Files.Select(f => f.OriginalPath)));
            _list.Items.Add(item);
        }
        if (_entries.Count == 0) _list.Items.Add(new ListBoxItem { Content = UiStrings.S_TrashDialog_Reload_929AEA2693, IsEnabled = false });
        ShowPreview();
    }

    /// <summary>항목의 첫 이미지(.chickxel이면 첫 프레임 합성, 아니면 PNG)를 읽는다. 이미지가 없거나 못 읽으면 null.</summary>
    private (Chickxel.Core.Model.PixelImage Image, AppTrash.TrashFile File)? FirstImage(AppTrash.Entry e)
    {
        foreach (var f in e.Files)
        {
            var ext = Path.GetExtension(f.OriginalPath).ToLowerInvariant();
            if (ext != ".png" && !Chickxel.Core.Model.SpriteProject.IsProjectFile(f.OriginalPath)) continue;
            var path = _trash.StoredPath(e, f);
            if (!File.Exists(path)) continue;
            try
            {
                var img = ext == ".png" ? Chickxel.Core.IO.PngIO.Load(path) : Chickxel.Core.IO.ProjectIO.LoadThumbnail(path);
                return (img, f);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException
                                           or NotSupportedException or System.Text.Json.JsonException)
            {
            }
        }
        return null;
    }

    private WriteableBitmap? LoadThumb(AppTrash.Entry e, int size)
        => FirstImage(e) is { } hit ? ImageUtil.MakeThumbnail(hit.Image, size) : null;

    /// <summary>고른 항목(여럿이면 첫 항목)의 큰 미리보기와 크기·파일 이름.</summary>
    private void ShowPreview()
    {
        var e = Selected().FirstOrDefault();
        if (e is not null && FirstImage(e) is { } hit)
        {
            _preview.Source = ImageUtil.MakeThumbnail(hit.Image, PreviewSize);
            _previewInfo.Text = $"{Path.GetFileName(hit.File.OriginalPath)} · {hit.Image.Width}×{hit.Image.Height}px\n{Path.GetDirectoryName(hit.File.OriginalPath)}";
        }
        else
        {
            _preview.Source = null;
            _previewInfo.Text = e is null ? UiStrings.S_TrashDialog_ShowPreview_8408F83804 : UiStrings.S_TrashDialog_ShowPreview_4BE4E0AA4F;
        }
    }

    private List<AppTrash.Entry> Selected()
        => (_list.SelectedItems?.OfType<ListBoxItem>() ?? []).Select(i => i.Tag).OfType<AppTrash.Entry>().ToList();

    private void RestoreSelected()
    {
        var restored = new List<string>();
        var errors = new List<string>();
        foreach (var e in Selected())
        {
            try { restored.AddRange(_trash.Restore(e)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{e.Label}: {ex.Message}"); }
        }
        if (restored.Count > 0) _restored(restored);
        if (errors.Count > 0) Ui.Error(this, string.Join("\n", errors));
        Reload();
    }

    private void DeleteSelected()
    {
        var sel = Selected();
        if (sel.Count == 0) return;
        if (Ui.Ask(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_TrashDialog_DeleteSelected_24E807F59E,sel.Count), UiStrings.S_TrashDialog_DeleteSelected_2C75877A47, MessageButtons.OKCancel, MessageIcon.Warning) != DialogResult.OK) return;
        Remove(sel);
    }

    private void EmptyAll()
    {
        if (_entries.Count == 0) return;
        if (Ui.Ask(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_TrashDialog_EmptyAll_C72A684107,_entries.Count), UiStrings.S_TrashDialog_EmptyAll_DF79DF30A5, MessageButtons.OKCancel, MessageIcon.Warning) != DialogResult.OK) return;
        Remove(_entries);
    }

    private void Remove(IEnumerable<AppTrash.Entry> entries)
    {
        try
        {
            foreach (var e in entries.ToList()) _trash.Delete(e);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_TrashDialog_Remove_35FEE27075,ex.Message));
        }
        Reload();
    }
}
