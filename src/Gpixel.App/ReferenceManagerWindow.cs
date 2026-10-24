using Avalonia.Input;
using Avalonia.Platform.Storage;
using Gpixel.App.Localization;
using Gpixel.Core.Model;
using Gpixel.Core.References;

namespace Gpixel.App;

/// <summary>
/// AI 참고 자료 창(모드리스, 하나만): 위 = 단추 줄(파일 추가·현재 캔버스 추가·폴더 열기·규칙 편집·AI로 설명 붙이기)과 설명 없는 자료 안내,
/// 왼쪽 = 검색 칸(AI와 같은 검색) + 종류 거르기 + 자료 목록(점 확대 썸네일·제목·태그, 기본 예시는 "기본" 칩),
/// 오른쪽 = 고른 자료(미리보기 — 여러 프레임이면 움직임, 크기·프레임·색 수, 제목·설명·태그·종류 고치기, 팔레트, 삭제). 파일을 끌어다 놓아도 추가된다.
/// 고친 칸은 포커스를 잃을 때 저장한다. AI 도구가 보관소를 바꾸면(<see cref="ReferenceLibrary.Changed"/>) 목록을 다시 만든다.
/// </summary>
public sealed class ReferenceManagerWindow : Window
{
    private const int ThumbSize = 52;

    private readonly ReferenceLibrary _lib;
    private readonly EditorContext _ctx;
    private readonly Action<string> _captionWithAi;
    private readonly TextBox _search;
    private readonly ComboBox _kindFilter;
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly Border _detail = new() { Padding = new Thickness(12, 10) };
    private readonly Border _notice;
    private readonly TextBlock _noticeText;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    /// <summary>썸네일(id → 내용 해시와 비트맵). 내용이 바뀌면 다시 만든다.</summary>
    private readonly Dictionary<string, (string Hash, WriteableBitmap Bitmap)> _thumbs = new();
    private ReferencePreview? _preview;
    private string? _selectedId;
    /// <summary>오른쪽 칸에 보이는 자료(고친 칸을 저장할 대상 — 목록에서 다른 것을 고르기 전 자료일 수 있음).</summary>
    private string? _shownId;
    private bool _reloadPosted;
    /// <summary>오른쪽 고치기 칸(고른 자료가 사용자 자료일 때만).</summary>
    private TextBox? _title, _description, _tags;
    private ComboBox? _kind;

    public ReferenceManagerWindow(ReferenceLibrary lib, EditorContext ctx, Action<string> captionWithAi)
    {
        _lib = lib;
        _ctx = ctx;
        _captionWithAi = captionWithAi;
        Title = UiStrings.Refs_Window_Title;
        Width = 1000;
        Height = 680;
        MinWidth = 640;
        MinHeight = 400;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = AppTheme.BackBrush;

        // 위: 단추 줄
        var toolbar = new WrapPanel { Margin = new Thickness(8, 6) };
        toolbar.Children.Add(Ui.Button(VectorIcon.FilePlus, UiStrings.Refs_AddFiles, (_, _) => AddFiles()).Tip(UiStrings.Refs_AddFiles_Tip));
        toolbar.Children.Add(Ui.Button(VectorIcon.Image, UiStrings.Refs_AddCanvas, (_, _) => AddCanvas()).Tip(UiStrings.Refs_AddCanvas_Tip));
        toolbar.Children.Add(Ui.Button(VectorIcon.FolderOpen, UiStrings.Refs_OpenFolder, (_, _) => OpenFolder()).Tip(_lib.Root));
        toolbar.Children.Add(Ui.Button(VectorIcon.Pencil, UiStrings.Refs_EditRules, (_, _) => EditRules()).Tip(UiStrings.Refs_EditRules_Tip));
        toolbar.Children.Add(Ui.Button(VectorIcon.Sparkles, UiStrings.Refs_CaptionWithAi, (_, _) => CaptionWithAi()).Tip(UiStrings.Refs_CaptionWithAi_Tip));
        foreach (var c in toolbar.Children) c.Margin = new Thickness(0, 0, 4, 0);

        _noticeText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center };
        var noticeButton = Ui.Button(VectorIcon.Sparkles, UiStrings.Refs_CaptionWithAi, (_, _) => CaptionWithAi());
        var noticeRow = new DockPanel();
        DockPanel.SetDock(noticeButton, Dock.Right);
        noticeRow.Children.Add(noticeButton);
        noticeRow.Children.Add(_noticeText);
        _notice = new Border
        {
            Background = AppTheme.InputBrush,
            BorderBrush = AppTheme.AccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(8, 0, 8, 6),
            Child = noticeRow,
            IsVisible = false,
        };

        // 왼쪽: 검색 + 종류 + 목록
        _search = new TextBox { Watermark = UiStrings.Refs_SearchWatermark, Margin = new Thickness(8, 6, 8, 4) };
        _search.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Reload();
        };
        _kindFilter = Ui.Combo(160, [UiStrings.Refs_KindAll, .. ReferenceKinds.All.Select(AppReferences.KindLabel)]);
        _kindFilter.Margin = new Thickness(8, 0, 8, 6);
        _kindFilter.SelectionChanged += (_, _) => Reload();
        var side = new DockPanel { Background = AppTheme.PanelBrush };
        var sideTop = new StackPanel { Children = { _search, _kindFilter } };
        DockPanel.SetDock(sideTop, Dock.Top);
        side.Children.Add(sideTop);
        side.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border { Padding = new Thickness(6, 0, 6, 6), Child = _list },
        });

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("320,Auto,*") };
        var splitter = new GridSplitter { Width = 4, Background = AppTheme.BorderBrush, ResizeDirection = GridResizeDirection.Columns };
        var detailScroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = _detail };
        Grid.SetColumn(side, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(detailScroll, 2);
        body.Children.Add(side);
        body.Children.Add(splitter);
        body.Children.Add(detailScroll);

        _status = new TextBlock { Foreground = AppTheme.DimTextBrush, Margin = new Thickness(8, 4), TextTrimming = TextTrimming.CharacterEllipsis };
        var top = new StackPanel { Children = { toolbar, _notice } };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(_status);
        root.Children.Add(body);
        Content = root;

        // 파일을 끌어다 놓으면 추가.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
            if (paths.Count > 0) Import(paths);
        });

        _lib.Changed += OnLibraryChanged;
        // 다른 앱에서 폴더에 넣은 파일: 창으로 돌아올 때 다시 살펴본다(바뀐 것이 있으면 Changed로 다시 그림).
        Activated += (_, _) => _lib.Rescan();
        Closed += (_, _) =>
        {
            _lib.Changed -= OnLibraryChanged;
            _debounce.Stop();
            _preview?.Stop();
            foreach (var (_, t) in _thumbs) t.Bitmap.Dispose();
            _thumbs.Clear();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            e.Handled = true;
            if (!string.IsNullOrEmpty(_search.Text)) _search.Text = "";
            else Close();
        };
        Reload();
    }

    /// <summary>목록과 고른 자료를 다시 만든다(설정에서 기본 예시를 숨기거나 보일 때 메인 창도 부름).</summary>
    public void Reload()
    {
        _reloadPosted = false;
        Commit();
        var items = Visible();
        _list.Children.Clear();
        foreach (var item in items) _list.Children.Add(Row(item));
        if (items.Count == 0)
            _list.Children.Add(new TextBlock { Text = UiStrings.Refs_Empty, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush, Margin = new Thickness(4, 8) });
        int missing = _lib.UncaptionedCount;
        _notice.IsVisible = missing > 0;
        _noticeText.Text = UiStrings.FormatRefs_Uncaptioned(count: missing);
        if (_selectedId is null || items.All(i => i.Id != _selectedId)) _selectedId = items.FirstOrDefault()?.Id;
        ShowDetail(_selectedId is { } id ? _lib.Find(id) : null);
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        // AI 도구가 바꾸는 중일 수 있으니 한 번에 모아 다시 그린다.
        if (_reloadPosted) return;
        _reloadPosted = true;
        Dispatcher.UIThread.Post(Reload, DispatcherPriority.Background);
    }

    /// <summary>검색 칸·종류 거르기에 맞는 자료(검색이 없으면 모두, 최근 것부터 · 기본 예시는 끝).</summary>
    private List<ReferenceItem> Visible()
    {
        var query = (_search.Text ?? "").Trim();
        string? kind = _kindFilter.SelectedIndex > 0 ? ReferenceKinds.All[_kindFilter.SelectedIndex - 1] : null;
        if (query.Length == 0 && kind is null) return [.. _lib.AllItems];
        return [.. _lib.Search(new ReferenceQuery { Text = query, Kind = kind, IncludeRules = false, Limit = 0 }).Select(h => h.Item)];
    }

    // ───────────── 목록 ─────────────

    private Control Row(ReferenceItem item)
    {
        Control thumb = item.IsImage && Thumb(item) is { } bmp
            ? new Image { Source = bmp, Width = ThumbSize, Height = ThumbSize }
            : new Border { Width = ThumbSize, Height = ThumbSize, Background = AppTheme.InputBrush, Child = new IconView(item.IsImage ? VectorIcon.Image : VectorIcon.BookImage, AppTheme.DimTextBrush) { Width = 24, Height = 24 } };
        thumb.VerticalAlignment = VerticalAlignment.Top;

        var title = new TextBlock { Text = item.Title, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis };
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (item.BuiltIn) titleLine.Children.Add(Chip(UiStrings.Refs_BuiltInChip, UiStrings.Refs_BuiltIn_Tip));
        if (!item.IsImage) titleLine.Children.Add(Chip(UiStrings.Refs_NoteChip, null));
        titleLine.Children.Add(title);
        var meta = item.IsImage
            ? $"{AppReferences.KindLabel(item.Kind)} · {item.Width}×{item.Height}{(item.Frames > 1 ? $" · {item.Frames}f" : "")}"
            : item.Id;
        var text = new StackPanel
        {
            Margin = new Thickness(8, 0, 0, 0),
            Children =
            {
                titleLine,
                new TextBlock { Text = meta, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock
                {
                    Text = string.Join(", ", item.Tags), Foreground = AppTheme.AccentBrush, FontSize = AppTheme.SmallFontSize,
                    TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = item.Tags.Count > 0,
                },
            },
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(text, 1);
        grid.Children.Add(thumb);
        grid.Children.Add(text);
        bool selected = item.Id == _selectedId;
        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = selected ? AppTheme.AccentBrush : Brushes.Transparent,
            Background = selected ? AppTheme.HoverBrush : Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            Commit();
            _selectedId = item.Id;
            foreach (var child in _list.Children.OfType<Border>())
            {
                bool on = ReferenceEquals(child, row);
                child.BorderBrush = on ? AppTheme.AccentBrush : Brushes.Transparent;
                child.Background = on ? AppTheme.HoverBrush : Brushes.Transparent;
            }
            ShowDetail(item);
        };
        return row;
    }

    private static Border Chip(string text, string? tip) => new Border
    {
        Background = AppTheme.AccentBrush,
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(4, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = AppTheme.SmallFontSize, Foreground = Brushes.White },
    }.Tip(tip);

    /// <summary>첫 프레임 썸네일(체커 위 정수 배율). 읽을 수 없으면 null.</summary>
    private WriteableBitmap? Thumb(ReferenceItem item)
    {
        var key = item.BuiltIn ? item.Id : item.Id + "|" + item.Hash;
        if (_thumbs.TryGetValue(item.Id, out var t) && t.Hash == key) return t.Bitmap;
        if (LoadFrames(item) is not { } frames) return null;
        if (_thumbs.Remove(item.Id, out var old)) old.Bitmap.Dispose();
        var bmp = ImageUtil.MakeThumbnail(frames.Images[0], ThumbSize);
        _thumbs[item.Id] = (key, bmp);
        return bmp;
    }

    private ReferenceFrames? LoadFrames(ReferenceItem item)
    {
        try { return _lib.LoadFrames(item); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    // ───────────── 고른 자료 ─────────────

    private void ShowDetail(ReferenceItem? item)
    {
        _preview?.Stop();
        _preview = null;
        _title = _description = _tags = null;
        _kind = null;
        _shownId = item?.Id;
        if (item is null)
        {
            _detail.Child = new TextBlock { Text = UiStrings.Refs_NoSelection, Foreground = AppTheme.DimTextBrush };
            return;
        }
        var stack = new StackPanel { Spacing = 6 };
        bool editable = !item.BuiltIn;

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (item.BuiltIn) head.Children.Add(Chip(UiStrings.Refs_BuiltInChip, UiStrings.Refs_BuiltIn_Tip));
        head.Children.Add(new SelectableTextBlock { Text = item.Id, Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(head);

        if (item.IsImage)
        {
            if (LoadFrames(item) is { } frames)
            {
                _preview = new ReferencePreview(frames) { Height = 260, HorizontalAlignment = HorizontalAlignment.Stretch };
                stack.Children.Add(new Border { BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), Child = _preview });
            }
            stack.Children.Add(Ui.Label(UiStrings.FormatRefs_Info(width: item.Width, height: item.Height, frames: item.Frames, colors: item.Colors)));
        }

        _title = new TextBox { Text = item.Title, IsReadOnly = !editable };
        _title.LostFocus += (_, _) => Commit();
        _title.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        stack.Children.Add(Ui.Label(UiStrings.Refs_Field_Title, bold: true));
        stack.Children.Add(_title);

        if (item.IsImage)
        {
            _description = new TextBox { Text = item.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, IsReadOnly = !editable };
            _description.LostFocus += (_, _) => Commit();
            _tags = new TextBox { Text = string.Join(", ", item.Tags), IsReadOnly = !editable };
            _tags.LostFocus += (_, _) => Commit();
            _tags.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
            _kind = Ui.Combo(180, [.. ReferenceKinds.All.Select(AppReferences.KindLabel)]);
            _kind.SelectedIndex = Math.Max(0, ReferenceKinds.All.ToList().IndexOf(item.Kind));
            _kind.IsEnabled = editable;
            _kind.SelectionChanged += (_, _) => Commit();
            stack.Children.Add(Ui.Label(UiStrings.Refs_Field_Description, bold: true));
            stack.Children.Add(_description);
            stack.Children.Add(Ui.Label(UiStrings.Refs_Field_Tags, bold: true));
            stack.Children.Add(_tags);
            stack.Children.Add(Ui.Row(Ui.Label(UiStrings.Refs_Field_Kind, bold: true), _kind));

            if (item.Palette.Count > 0)
            {
                stack.Children.Add(Ui.Label(UiStrings.Refs_Field_Palette, bold: true));
                var swatches = new WrapPanel();
                foreach (var hex in item.Palette)
                {
                    if (!Color32.TryParseHex(hex, out var c)) continue;
                    swatches.Children.Add(new Border
                    {
                        Width = 18, Height = 18, Margin = new Thickness(0, 0, 2, 2),
                        BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1),
                        Background = AppTheme.Brush(c.ToColor()),
                    }.Tip(hex));
                }
                stack.Children.Add(swatches);
            }
        }
        else
        {
            var text = _lib.ReadText(item) ?? "";
            stack.Children.Add(new Border
            {
                Background = AppTheme.InputBrush,
                Padding = new Thickness(8),
                Child = new SelectableTextBlock { Text = text.Length > 8000 ? text[..8000] + "…" : text, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.TextBrush },
            });
        }

        if (editable)
        {
            var delete = Ui.Button(VectorIcon.Trash, UiStrings.Refs_Delete, (_, _) => Delete(item)).Tip(UiStrings.Refs_Delete_Tip);
            delete.HorizontalAlignment = HorizontalAlignment.Left;
            delete.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(delete);
        }
        _detail.Child = stack;
    }

    /// <summary>오른쪽 칸에서 고친 제목·설명·태그·종류를 저장한다(바뀐 것이 없으면 그대로).</summary>
    private void Commit()
    {
        if (_shownId is not { } id || _title is null || _lib.Find(id) is not { BuiltIn: false } item || item.Type == ReferenceTypes.Rules) return;
        var title = (_title.Text ?? "").Trim();
        var description = _description is null ? null : (_description.Text ?? "").Trim();
        var tags = _tags is null ? null : BuiltInReferences.SplitTags(_tags.Text);
        var kind = _kind is { SelectedIndex: >= 0 } k ? ReferenceKinds.All[k.SelectedIndex] : null;
        bool changed = (title.Length > 0 && title != item.Title)
            || (description is not null && description != item.Description)
            || (tags is not null && !tags.SequenceEqual(item.Tags))
            || (kind is not null && kind != item.Kind);
        if (!changed) return;
        try
        {
            _lib.Update(id, title, description, tags, kind, CaptionSources.User);
            _status.Text = UiStrings.Refs_Status_Saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
    }

    // ───────────── 단추 ─────────────

    private void AddFiles()
    {
        var paths = Ui.OpenFiles(this, UiStrings.Refs_AddFiles, null,
            new FileFilter(UiStrings.Refs_FileFilter, "*.png", "*.gif", "*.gpixel", "*.md", "*.txt"));
        if (paths.Count > 0) Import(paths);
    }

    private void Import(IReadOnlyList<string> paths)
    {
        Commit();
        try
        {
            var added = _lib.ImportFiles(paths);
            _status.Text = added.Count > 0 ? UiStrings.FormatRefs_Status_Added(count: added.Count) : UiStrings.Refs_Status_NothingAdded;
            if (added.Count > 0) Select(added[0].Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
    }

    /// <summary>지금 문서(보이는 레이어 합성, 모든 프레임 — 여러 프레임이면 애니메이션 그대로)를 자료로 넣는다.</summary>
    private void AddCanvas()
    {
        if (_ctx.Project is not { } p)
        {
            _status.Text = UiStrings.Refs_Status_NoProject;
            return;
        }
        Commit();
        _ctx.CommitFloating();
        var images = Enumerable.Range(0, p.Frames.Count).Select(i => p.Flatten(i)).ToList();
        try
        {
            var item = _lib.AddImage(new ReferenceFrames(images, [.. p.Frames.Select(f => f.DurationMs)]), p.BaseName, null, null, null, null, CaptionSources.User);
            _status.Text = UiStrings.FormatRefs_Status_Added(count: 1);
            Select(item.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
    }

    private void Select(string id)
    {
        _search.Text = "";
        _kindFilter.SelectedIndex = 0;
        _selectedId = id;
        Reload();
    }

    /// <summary>자료 파일을 Gpixel 휴지통으로 옮긴다(휴지통에서 되살리면 제목·설명도 돌아옴).</summary>
    private void Delete(ReferenceItem item)
    {
        if (item.BuiltIn || _lib.FullPath(item) is not { } path) return;
        try
        {
            new AppTrash(AppTrash.DefaultRoot).Add([path], UiStrings.FormatRefs_TrashLabel(title: item.Title));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
            return;
        }
        _selectedId = null;
        _lib.Rescan();
        _status.Text = UiStrings.FormatRefs_Status_Deleted(title: item.Title);
        Reload();
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_lib.Root);
            Platform.OpenFolder(_lib.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
    }

    private void EditRules()
    {
        try { Platform.OpenFile(_lib.EnsureUserRules()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
    }

    /// <summary>설명 없는 자료(없으면 고른 자료)에 설명을 붙여 달라는 부탁을 채팅 입력란에 넣는다. 보내지는 않는다.</summary>
    private void CaptionWithAi()
    {
        Commit();
        var targets = _lib.Items.Where(i => i.NeedsCaption).ToList();
        if (targets.Count == 0 && _selectedId is { } id && _lib.Find(id) is { BuiltIn: false, IsImage: true } selected) targets.Add(selected);
        if (targets.Count == 0)
        {
            _status.Text = UiStrings.Refs_Status_NothingToCaption;
            return;
        }
        _captionWithAi(AppReferences.CaptionRequest(targets));
        _status.Text = UiStrings.Refs_Status_CaptionPrefilled;
    }
}

/// <summary>참고 그림 미리보기: 체커 위 정수 배율(최근접)로 가운데에, 여러 프레임이면 프레임 길이대로 되풀이한다.</summary>
internal sealed class ReferencePreview : Control
{
    private readonly ReferenceFrames _frames;
    private readonly WriteableBitmap[] _bitmaps;
    private readonly DispatcherTimer _timer = new();
    private int _index;

    public ReferencePreview(ReferenceFrames frames)
    {
        _frames = frames;
        _bitmaps = [.. frames.Images.Select(ImageUtil.ToBitmap)];
        _timer.Tick += (_, _) =>
        {
            _index = (_index + 1) % _bitmaps.Length;
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _frames.DurationsMs[_index]));
            InvalidateVisual();
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (_bitmaps.Length < 2) return;
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _frames.DurationsMs[0]));
            _timer.Start();
        };
        DetachedFromVisualTree += (_, _) => Stop();
    }

    /// <summary>타이머를 멈추고 비트맵을 버린다(여러 번 불러도 됨). 그 뒤로는 그리지 않는다.</summary>
    public void Stop()
    {
        _timer.Stop();
        if (_stopped) return;
        _stopped = true;
        foreach (var b in _bitmaps) b.Dispose();
    }

    private bool _stopped;

    public override void Render(DrawingContext context)
    {
        if (_stopped || _bitmaps.Length == 0) return;
        var img = _frames.Images[_index];
        ImageUtil.DrawFitted(context, _bitmaps[_index], new Rectangle(0, 0, img.Width, img.Height), new Rect(Bounds.Size).Deflate(8), checker: true);
    }
}
