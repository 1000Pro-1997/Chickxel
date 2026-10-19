using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 파일 브라우저: 루트 폴더 아래 폴더 트리 + 선택 폴더의 .gpixel 프로젝트와 PNG 썸네일 목록(하위 포함 토글,
/// 프로젝트가 내보내는 이미지는 프로젝트 하나로 묶어 보여 줌), 이름 검색, 파일 변경 감시, 우클릭 메뉴(새 프로젝트·이름 바꾸기·탐색기에서 열기·휴지통으로 삭제).
/// </summary>
public sealed class FileBrowserPanel : UserControl
{
    private const int ThumbSize = 56;
    private const int ItemWidth = 84;

    private readonly TextBlock _rootLabel;
    private readonly TextBox _search;
    private readonly CheckBox _includeSub;
    private readonly TreeView _tree;
    private readonly ListBox _list;
    private readonly Grid _split;
    private readonly ContextMenu _menu;
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly HashSet<string> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Stamp, WriteableBitmap Thumb)> _thumbCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>지금 목록에 있는 항목(경로 → 항목).</summary>
    private readonly Dictionary<string, FileItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _thumbCts;
    private bool _structureChanged;
    private bool _suppressActivate;
    private bool _rebuildingTree;
    /// <summary>이름 바꾸기 중이면 끝내는 동작(true = 적용, false = 취소).</summary>
    private Action<bool>? _endRename;

    /// <summary>목록 한 칸: 경로와 그 칸의 컨트롤들.</summary>
    private sealed record FileItem(string Path, ListBoxItem Container, Image Thumb, TextBlock Label, StackPanel Content);

    public FileBrowserPanel()
    {
        Background = AppTheme.PanelBrush;
        _rootLabel = new TextBlock
        {
            Height = 22,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = AppTheme.DimTextBrush,
            Padding = new Thickness(4, 4, 0, 0),
            Text = "폴더를 여세요",
        };
        DockPanel.SetDock(_rootLabel, Dock.Top);

        _search = new TextBox { Watermark = "파일 이름 검색", Margin = new Thickness(2) };
        _search.TextChanged += (_, _) => RefreshList();
        DockPanel.SetDock(_search, Dock.Top);
        _includeSub = Ui.Check("하위 폴더 포함", true);
        _includeSub.Margin = new Thickness(6, 0, 3, 2);
        _includeSub.IsCheckedChanged += (_, _) => RefreshList();
        DockPanel.SetDock(_includeSub, Dock.Top);

        _tree = new TreeView
        {
            SelectionMode = SelectionMode.Single,
            Background = AppTheme.InputBrush,
            Foreground = AppTheme.TextBrush,
        };
        _tree.SelectionChanged += (_, _) => { if (!_rebuildingTree) RefreshList(); };

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Background = AppTheme.InputBrush,
            Foreground = AppTheme.TextBrush,
            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal }),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += (_, e) =>
        {
            if (_suppressActivate) return;
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ListBoxItem { Tag: FileItem f })
                FileActivated?.Invoke(this, f.Path);
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F2 && SelectedItem is { } item) { BeginRename(item); e.Handled = true; }
            else if (IsDeleteKey(e) && SelectedPath is not null) { DeleteSelected(); e.Handled = true; }
        };
        // 빈 곳을 오른쪽 클릭하면 선택을 지운다(메뉴는 폴더 대상).
        _list.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

        // 메뉴 동작은 메뉴가 닫힌 뒤에 한다(대화상자·이름 바꾸기 입력란 포커스).
        var miNew = new MenuItem { Header = "새 프로젝트…" };
        miNew.Click += (_, _) => Dispatcher.UIThread.Post(() => NewImageRequested?.Invoke(this, CurrentFolder ?? RootFolder ?? ""));
        var miRename = new MenuItem { Header = "이름 바꾸기", InputGesture = new KeyGesture(Key.F2) };
        miRename.Click += (_, _) => Dispatcher.UIThread.Post(() => { if (SelectedItem is { } item) BeginRename(item); }, DispatcherPriority.Background);
        var miExplorer = new MenuItem { Header = $"{Platform.FileManagerName}에서 열기" };
        miExplorer.Click += (_, _) => Dispatcher.UIThread.Post(ShowInExplorer);
        var miDelete = new MenuItem
        {
            Header = "삭제(휴지통)",
            InputGesture = Platform.IsMac ? new KeyGesture(Key.Back, KeyModifiers.Meta) : new KeyGesture(Key.Delete),
        };
        miDelete.Click += (_, _) => Dispatcher.UIThread.Post(DeleteSelected);
        _menu = new ContextMenu();
        _menu.Items.Add(miNew);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(miRename);
        _menu.Items.Add(miExplorer);
        _menu.Items.Add(miDelete);
        _menu.Opening += (_, _) =>
        {
            miNew.IsEnabled = RootFolder is not null;
            bool hasFile = SelectedPath is not null;
            miRename.IsEnabled = miDelete.IsEnabled = hasFile;
            miExplorer.IsEnabled = hasFile || CurrentFolder is not null;
        };
        _list.ContextMenu = _menu;

        var lower = new DockPanel { LastChildFill = true };
        lower.Children.Add(_search);
        lower.Children.Add(_includeSub);
        lower.Children.Add(_list);
        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Rows,
            Background = AppTheme.BorderBrush,
            Height = 4,
            MinHeight = 4,
        };
        _split = new Grid { RowDefinitions = new RowDefinitions("160,4,*") };
        _split.RowDefinitions[0].MinHeight = 40;
        _split.RowDefinitions[2].MinHeight = 60;
        Grid.SetRow(_tree, 0);
        Grid.SetRow(splitter, 1);
        Grid.SetRow(lower, 2);
        _split.Children.Add(_tree);
        _split.Children.Add(splitter);
        _split.Children.Add(lower);

        var root = new DockPanel { LastChildFill = true };
        root.Children.Add(BuildHeader());
        root.Children.Add(_rootLabel);
        root.Children.Add(_split);
        Content = root;

        _watchTimer.Tick += (_, _) => FlushWatcherChanges();
    }

    /// <summary>"파일" 제목 줄 + 오른쪽 끝 새로 고침 아이콘(누르면 한 바퀴 돌며 새로 고침). 폴더 열기는 파일 메뉴(Ctrl+Shift+O·Ctrl+Shift+U).</summary>
    private Border BuildHeader()
    {
        var header = Ui.Header("파일");
        var title = (TextBlock)header.Child!;
        header.Child = null;
        var icon = new SpinningIcon(VectorIcon.Refresh) { Width = 14, Height = 14 };
        var refresh = new Button
        {
            Content = icon,
            Width = 22,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 3, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        }.Tip("새로 고침");
        refresh.Click += (_, _) =>
        {
            icon.Spin();
            RefreshAll();
        };
        DockPanel.SetDock(refresh, Dock.Right);
        header.Child = new DockPanel { LastChildFill = true, Children = { refresh, title } };
        return header;
    }

    /// <summary>선 아이콘. <see cref="Spin"/>하면 한 바퀴(감속) 돈다.</summary>
    private sealed class SpinningIcon : IconView
    {
        private readonly RotateTransform _rotate = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
        private DateTime _start;
        private const double DurationMs = 550;

        public SpinningIcon(VectorIcon icon) : base(icon)
        {
            RenderTransform = _rotate;
            RenderTransformOrigin = RelativePoint.Center;
            _timer.Tick += (_, _) =>
            {
                double t = Math.Min(1, (DateTime.UtcNow - _start).TotalMilliseconds / DurationMs);
                _rotate.Angle = 360 * (1 - Math.Pow(1 - t, 3));
                if (t >= 1)
                {
                    _timer.Stop();
                    _rotate.Angle = 0;
                }
            };
        }

        public void Spin()
        {
            _start = DateTime.UtcNow;
            _timer.Start();
        }
    }

    public string? RootFolder { get; private set; }

    /// <summary>트리에서 고른 폴더.</summary>
    public string? CurrentFolder => (_tree.SelectedItem as TreeViewItem)?.Tag as string ?? RootFolder;

    public string? SelectedPath => SelectedItem?.Path;

    private FileItem? SelectedItem => _list.SelectedItem is ListBoxItem { Tag: FileItem f } ? f : null;

    public bool IncludeSubfolders
    {
        get => _includeSub.IsChecked == true;
        set => _includeSub.IsChecked = value;
    }

    /// <summary>폴더 트리 높이(화면 단위).</summary>
    public int TreeHeight
    {
        get
        {
            var row = _split.RowDefinitions[0];
            return row.Height.IsAbsolute ? (int)row.Height.Value : (int)row.ActualHeight;
        }
        set
        {
            // 아직 배치 전이면 높이를 모르므로 아래쪽 한계는 나중에 행 최소 높이가 지킨다.
            if (value > 40 && (_split.Bounds.Height <= 0 || value < _split.Bounds.Height - 60))
                _split.RowDefinitions[0].Height = new GridLength(value, GridUnitType.Pixel);
        }
    }

    public event EventHandler<string>? FileActivated;
    /// <summary>새 프로젝트를 만들 폴더.</summary>
    public event EventHandler<string>? NewImageRequested;
    /// <summary>이름이 바뀐 파일(이전, 새 경로).</summary>
    public event EventHandler<(string OldPath, string NewPath)>? FileRenamed;
    /// <summary>휴지통으로 보낸 파일.</summary>
    public event EventHandler<string>? FileDeleted;
    /// <summary>외부(또는 앱)에서 바뀐 파일 경로들(감시 결과, 묶어서 전달).</summary>
    public event EventHandler<IReadOnlyCollection<string>>? FilesChanged;

    // ───────────── 폴더 ─────────────

    public void SetRoot(string folder)
    {
        RootFolder = Path.GetFullPath(folder);
        _rootLabel.Text = RootFolder;
        ToolTip.SetTip(_rootLabel, RootFolder);
        StartWatcher();
        RebuildTree(RootFolder);
    }

    private void RebuildTree(string? selectFolder)
    {
        _rebuildingTree = true;
        try
        {
            _tree.Items.Clear();
            if (RootFolder is not null && Directory.Exists(RootFolder))
            {
                var root = MakeNode(RootFolder);
                root.Header = Path.GetFileName(Path.TrimEndingDirectorySeparator(RootFolder)) is { Length: > 0 } n ? n : RootFolder;
                _tree.Items.Add(root);
                PopulateChildren(root);
                root.IsExpanded = true;
                var selected = FindOrExpand(root, selectFolder) ?? root;
                selected.IsSelected = true;
                _tree.SelectedItem = selected;
                Dispatcher.UIThread.Post(() => selected.BringIntoView(), DispatcherPriority.Background);
            }
        }
        finally
        {
            _rebuildingTree = false;
        }
        RefreshList();
    }

    private static TreeViewItem MakeNode(string dir)
    {
        var node = new TreeViewItem { Header = Path.GetFileName(dir), Tag = dir, MinHeight = 20 };
        if (HasSubdirectories(dir)) node.Items.Add(new TreeViewItem { Header = "…", MinHeight = 20 });
        // 펼칠 때 하위 폴더를 읽는다.
        node.PropertyChanged += (_, e) =>
        {
            if (e.Property == TreeViewItem.IsExpandedProperty && node.IsExpanded) PopulateChildren(node);
        };
        return node;
    }

    private static bool HasSubdirectories(string dir)
    {
        try { return EnumerateDirs(dir).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static IEnumerable<string> EnumerateDirs(string dir)
        => Directory.EnumerateDirectories(dir, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System })
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Order(StringComparer.OrdinalIgnoreCase);

    private static void PopulateChildren(TreeViewItem node)
    {
        if (node.Items.Count != 1 || node.Items[0] is not TreeViewItem { Tag: null }) return;
        node.Items.Clear();
        try
        {
            foreach (var d in EnumerateDirs((string)node.Tag!)) node.Items.Add(MakeNode(d));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>트리에서 폴더 노드를 찾는다(필요한 만큼 펼침).</summary>
    private static TreeViewItem? FindOrExpand(TreeViewItem root, string? folder)
    {
        if (folder is null) return null;
        var node = root;
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        while (true)
        {
            var tag = Path.TrimEndingDirectorySeparator((string)node.Tag!);
            if (string.Equals(tag, target, StringComparison.OrdinalIgnoreCase)) return node;
            if (!target.StartsWith(tag + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            PopulateChildren(node);
            var next = node.Items.OfType<TreeViewItem>().FirstOrDefault(n => n.Tag is string t
                && (string.Equals(t, target, StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith(t + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
            if (next is null) return null;
            node.IsExpanded = true;
            node = next;
        }
    }

    public void RefreshAll()
    {
        if (RootFolder is null) return;
        RebuildTree(CurrentFolder);
    }

    // ───────────── 파일 목록 ─────────────

    private void RefreshList()
    {
        var folder = CurrentFolder;
        var keepSelected = SelectedPath;
        _thumbCts?.Cancel();
        _endRename?.Invoke(false);
        _suppressActivate = true;
        _list.Items.Clear();
        _items.Clear();
        _suppressActivate = false;
        if (folder is null || !Directory.Exists(folder)) return;

        List<string> files;
        var exportsOf = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = _includeSub.IsChecked == true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            };
            var projects = Directory.EnumerateFiles(folder, "*" + SpriteProject.FileExtension, options).ToList();
            var projectKeys = projects.Select(p => Path.ChangeExtension(p, null)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // 프로젝트가 내보내는 이미지(같은 이름 PNG + 등록된 프레임 시트·부위 아틀라스)는 목록에서 빼고 프로젝트 하나로 보여 준다.
            var exported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in projects)
            {
                var outputs = ProjectIO.GetRegisteredExportPaths(p);
                exportsOf[p] = outputs;
                exported.UnionWith(outputs);
            }
            files = projects
                .Concat(Directory.EnumerateFiles(folder, "*.png", options)
                    .Where(p => !projectKeys.Contains(Path.ChangeExtension(p, null)) && !exported.Contains(Path.GetFullPath(p))))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = new();
        }
        var query = (_search.Text ?? "").Trim();
        if (query.Length > 0)
            files = files.Where(f => Path.GetFileName(f).Contains(query, StringComparison.OrdinalIgnoreCase)
                                     || exportsOf.TryGetValue(f, out var outs) && outs.Any(o => Path.GetFileName(o).Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        var pending = new List<(string Path, DateTime Stamp)>();
        FileItem? kept = null;
        _suppressActivate = true;
        try
        {
            foreach (var f in files)
            {
                var tip = Path.GetRelativePath(RootFolder ?? folder, f);
                if (exportsOf.TryGetValue(f, out var outputs) && outputs.Count > 0)
                    tip += "\n내보내기: " + string.Join(", ", outputs.Select(o => Path.GetRelativePath(Path.GetDirectoryName(f)!, o)));
                var item = MakeItem(f, tip);
                var stamp = SafeStamp(f);
                if (_thumbCache.TryGetValue(f, out var cached) && cached.Stamp == stamp) item.Thumb.Source = cached.Thumb;
                else pending.Add((f, stamp));
                _list.Items.Add(item.Container);
            }
            if (keepSelected is not null && _items.TryGetValue(keepSelected, out kept)) _list.SelectedItem = kept.Container;
        }
        finally
        {
            _suppressActivate = false;
        }
        if (kept is not null) _list.ScrollIntoView(kept.Container);
        if (pending.Count > 0) LoadThumbnails(pending);
    }

    /// <summary>
    /// <c>.gpixel</c> 프로젝트(레이어·프레임을 보관하는 편집 원본) 표시: 썸네일 오른쪽 아래 파란 레이어 배지.
    /// 그냥 PNG(내보낸 결과물이나 프로젝트 없는 이미지)는 배지가 없다.
    /// </summary>
    private static Control WithProjectBadge(Image thumb)
    {
        var badge = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            Background = AppTheme.AccentBrush,
            BorderBrush = AppTheme.PanelBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -4, -2),
            Child = new IconView(VectorIcon.Layers, Brushes.White, 2.4),
        };
        ToolTip.SetTip(badge, "Gpixel 프로젝트(.gpixel) — 레이어·프레임을 보관하는 편집 원본");
        return new Grid { Width = ThumbSize, Height = ThumbSize, HorizontalAlignment = HorizontalAlignment.Center, Children = { thumb, badge } };
    }

    private FileItem MakeItem(string path, string tip)
    {
        var thumb = new Image { Width = ThumbSize, Height = ThumbSize, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(thumb, BitmapInterpolationMode.None);
        var label = new TextBlock
        {
            Text = Path.GetFileName(path),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = AppTheme.TextBrush,
        };
        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        content.Children.Add(IsProject(path) ? WithProjectBadge(thumb) : thumb);
        content.Children.Add(label);
        var container = new ListBoxItem
        {
            Content = content,
            Width = ItemWidth,
            Padding = new Thickness(2, 4),
            Margin = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(container, tip);
        var item = new FileItem(path, container, thumb, label, content);
        container.Tag = item;
        _items[path] = item;
        return item;
    }

    private static DateTime SafeStamp(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    private void LoadThumbnails(List<(string Path, DateTime Stamp)> files)
    {
        var cts = new CancellationTokenSource();
        _thumbCts = cts;
        var token = cts.Token;
        Task.Run(() =>
        {
            foreach (var (path, stamp) in files)
            {
                if (token.IsCancellationRequested) return;
                WriteableBitmap thumb;
                try
                {
                    var img = IsProject(path) ? ProjectIO.LoadThumbnail(path) : PngIO.Load(path);
                    thumb = ImageUtil.MakeThumbnail(img, ThumbSize);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                {
                    continue;
                }
                Dispatcher.UIThread.Post(() =>
                {
                    _thumbCache.TryGetValue(path, out var old);
                    _thumbCache[path] = (stamp, thumb);
                    if (_items.TryGetValue(path, out var item)) item.Thumb.Source = thumb;
                    old.Thumb?.Dispose();
                });
            }
        }, token);
    }

    /// <summary>파일 하나의 썸네일을 다시 만들고(저장 후) 목록을 갱신한다.</summary>
    public void RefreshFile(string path)
    {
        _thumbCache.Remove(path, out var old);
        RefreshList();
        old.Thumb?.Dispose();
    }

    /// <summary>목록에서 파일을 선택 표시한다(열기 이벤트 없이).</summary>
    public void SelectFile(string? path)
    {
        _suppressActivate = true;
        try
        {
            _list.SelectedItem = null;
            if (path is not null && _items.TryGetValue(path, out var item))
            {
                _list.SelectedItem = item.Container;
                _list.ScrollIntoView(item.Container);
            }
        }
        finally
        {
            _suppressActivate = false;
        }
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_list).Properties.IsRightButtonPressed) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
            _list.SelectedItem = null;
    }

    private static bool IsDeleteKey(KeyEventArgs e)
        => e.Key == Key.Delete || (Platform.IsMac && e.Key == Key.Back && Ui.HasCommand(e.KeyModifiers));

    // ───────────── 우클릭 동작 ─────────────

    /// <summary>목록 칸의 이름을 그 자리에서 고친다(Enter·포커스 이동 = 적용, Esc = 취소).</summary>
    private void BeginRename(FileItem item)
    {
        if (_endRename is not null || !_items.ContainsKey(item.Path)) return;
        var box = new TextBox
        {
            Text = item.Label.Text,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 0,
            Padding = new Thickness(2, 1),
            Width = ItemWidth - 8,
        };
        bool done = false;
        void End(bool commit)
        {
            if (done) return;
            done = true;
            _endRename = null;
            var text = box.Text;
            if (box.IsKeyboardFocusWithin) item.Container.Focus(); // 다른 곳을 눌러 끝났으면 포커스를 뺏지 않는다
            item.Content.Children.Remove(box);
            item.Label.IsVisible = true;
            if (commit && text is not null) Rename(item.Path, text);
        }
        box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    End(true);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    End(false);
                    e.Handled = true;
                    break;
                case Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Delete or Key.Back or Key.F2:
                    e.Handled = true; // 입력란이 처리한 뒤 목록 이동·삭제로 번지지 않게
                    break;
            }
        };
        box.LostFocus += (_, _) => End(true);
        _endRename = End;
        item.Label.IsVisible = false;
        item.Content.Children.Add(box);
        Dispatcher.UIThread.Post(() =>
        {
            if (done) return;
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void Rename(string oldPath, string label)
    {
        var newName = label.Trim();
        var ext = Path.GetExtension(oldPath);
        if (!newName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) newName += ext;
        if (newName.Length <= ext.Length || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Ui.Error(this, "쓸 수 없는 파일 이름입니다.");
            return;
        }
        var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newName);
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) return;
        try
        {
            if (IsProject(oldPath))
                ProjectIO.RenameWithOwnedDefaultExport(oldPath, newPath);
            else if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                // 대소문자만 바꿀 때는 임시 이름을 거친다.
                var temp = oldPath + ".rename-tmp.png";
                DocumentIO.RenameWithCompanions(oldPath, temp);
                DocumentIO.RenameWithCompanions(temp, newPath);
            }
            else DocumentIO.RenameWithCompanions(oldPath, newPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Ui.Error(this, $"이름을 바꾸지 못했습니다.\n{ex.Message}");
            return;
        }
        FileRenamed?.Invoke(this, (oldPath, newPath));
        Dispatcher.UIThread.Post(() =>
        {
            RefreshList();
            SelectFile(newPath);
        });
    }

    private void ShowInExplorer()
    {
        var target = SelectedPath;
        var folder = CurrentFolder;
        try
        {
            if (target is not null) Platform.RevealInFileManager(target);
            else if (folder is not null) Platform.OpenFolder(folder);
        }
        catch (IOException ex)
        {
            Ui.Error(this, $"{Platform.FileManagerName}를 열지 못했습니다.\n{ex.Message}");
        }
    }

    private void DeleteSelected()
    {
        var path = SelectedPath;
        if (path is null) return;
        List<string> companions;
        try
        {
            companions = IsProject(path)
                ? ProjectIO.GetOwnedDefaultExportPaths(path).ToList()
                : DocumentIO.GetCompanionPaths(path).Where(File.Exists).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Ui.Error(this, $"삭제할 파일을 확인하지 못했습니다.\n{ex.Message}");
            return;
        }
        var extra = companions.Count > 0 ? $"\n함께 삭제: {string.Join(", ", companions.Select(Path.GetFileName))}" : "";
        if (Ui.Ask(this, $"{Path.GetFileName(path)}을(를) 휴지통으로 보낼까요?{extra}", "삭제",
                MessageButtons.OKCancel, MessageIcon.Warning) != DialogResult.OK) return;
        try
        {
            foreach (var p in companions.Append(path)) Platform.MoveToTrash(p);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Ui.Error(this, $"삭제를 완료하지 못했습니다. 일부 파일은 이미 휴지통으로 이동했을 수 있습니다.\n{ex.Message}");
            RefreshList();
            return;
        }
        FileDeleted?.Invoke(this, path);
        RefreshList();
    }

    private static bool IsProject(string path) => path.EndsWith(SpriteProject.FileExtension, StringComparison.OrdinalIgnoreCase);

    // ───────────── 변경 감시 ─────────────

    private void StartWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        var root = RootFolder;
        if (root is null || !Directory.Exists(root)) return;
        try
        {
            _watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += (s, e) => { OnFsEvent(s, e); Queue(e.OldFullPath, true); };
            _watcher.Error += (_, _) => Queue(root, true);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    /// <summary>감시 스레드에서 불린다.</summary>
    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        bool isPng = e.FullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || IsProject(e.FullPath);
        bool isDir = e.ChangeType != WatcherChangeTypes.Changed && !Path.HasExtension(e.FullPath);
        if (!isPng && !isDir && !e.FullPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            && !e.FullPath.EndsWith(SidecarIO.Extension, StringComparison.OrdinalIgnoreCase)) return;
        Queue(e.FullPath, isDir);
    }

    /// <summary>바뀐 경로를 UI 스레드에 모으고, 조용해지면(400ms) 한 번에 반영한다.</summary>
    private void Queue(string path, bool structure)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _pendingChanges.Add(path);
            _structureChanged |= structure;
            _watchTimer.Stop();
            _watchTimer.Start();
        });
    }

    private void FlushWatcherChanges()
    {
        _watchTimer.Stop();
        var changed = _pendingChanges.ToList();
        _pendingChanges.Clear();
        var stale = new List<WriteableBitmap>();
        foreach (var p in changed)
            if (_thumbCache.Remove(p, out var old)) stale.Add(old.Thumb);
        if (_structureChanged) RebuildTree(CurrentFolder);
        else RefreshList();
        _structureChanged = false;
        // 목록을 새로 만든 뒤라 더는 화면에 쓰이지 않는다.
        foreach (var b in stale) b.Dispose();
        FilesChanged?.Invoke(this, changed);
    }
}
