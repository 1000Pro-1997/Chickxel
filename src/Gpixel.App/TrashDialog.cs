namespace Gpixel.App;

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
    public TrashDialog(AppTrash trash, TrashPolicy policy, Action<TrashPolicy> policyChanged, Action<IReadOnlyList<string>> restored) : base("앱 휴지통")
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
        var restore = Ui.Button("되살리기", (_, _) => RestoreSelected(), 100);
        var delete = Ui.Button("영구 삭제", (_, _) => DeleteSelected(), 100);
        var empty = Ui.Button("비우기", (_, _) => EmptyAll(), 100);
        Body.Children.Add(Ui.Label("파일 브라우저에서 지운 파일(합치기로 지운 원본 포함)을 보관합니다. 파일 목록에서 Ctrl+Z로 마지막 삭제를 되살립니다."));
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
        _usage.Text = $"지금 {_entries.Count}개 · {AppTrash.FormatSize(total)}" + (Policy.MaxBytes > 0 ? $" / {AppTrash.FormatSize(Policy.MaxBytes)}" : "");
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
                            Text = $"{e.DeletedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {e.Label} · {(Policy.Days > 0 ? $"{AppTrash.DaysLeft(e, Policy.Days)}일 남음" : "기간 제한 없음")} · {AppTrash.FormatSize(_trash.SizeOf(e))}\n   {files}",
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
        if (_entries.Count == 0) _list.Items.Add(new ListBoxItem { Content = "비어 있음", IsEnabled = false });
        ShowPreview();
    }

    /// <summary>항목의 첫 이미지(.gpixel이면 첫 프레임 합성, 아니면 PNG)를 읽는다. 이미지가 없거나 못 읽으면 null.</summary>
    private (Gpixel.Core.Model.PixelImage Image, AppTrash.TrashFile File)? FirstImage(AppTrash.Entry e)
    {
        foreach (var f in e.Files)
        {
            var ext = Path.GetExtension(f.OriginalPath).ToLowerInvariant();
            if (ext is not (".png" or ".gpixel")) continue;
            var path = _trash.StoredPath(e, f);
            if (!File.Exists(path)) continue;
            try
            {
                var img = ext == ".gpixel" ? Gpixel.Core.IO.ProjectIO.LoadThumbnail(path) : Gpixel.Core.IO.PngIO.Load(path);
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
            _previewInfo.Text = e is null ? "항목을 고르면 미리보기가 보입니다." : "미리볼 이미지가 없습니다.";
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
        if (Ui.Ask(this, $"항목 {sel.Count}개를 영구 삭제할까요? 되돌릴 수 없습니다.", "영구 삭제", MessageButtons.OKCancel, MessageIcon.Warning) != DialogResult.OK) return;
        Remove(sel);
    }

    private void EmptyAll()
    {
        if (_entries.Count == 0) return;
        if (Ui.Ask(this, $"휴지통의 항목 {_entries.Count}개를 모두 영구 삭제할까요? 되돌릴 수 없습니다.", "비우기", MessageButtons.OKCancel, MessageIcon.Warning) != DialogResult.OK) return;
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
            Ui.Error(this, $"일부 항목을 지우지 못했습니다.\n{ex.Message}");
        }
        Reload();
    }
}
