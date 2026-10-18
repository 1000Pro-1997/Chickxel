using System.Diagnostics;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 파일 브라우저: 루트 폴더 아래 폴더 트리 + 선택 폴더의 .gpixel 프로젝트와 PNG 썸네일 목록(하위 포함 토글,
/// 같은 이름이면 .gpixel만), 이름 검색, 파일 변경 감시, 우클릭 메뉴(새 프로젝트·이름 바꾸기·탐색기에서 열기·휴지통으로 삭제).
/// </summary>
public sealed class FileBrowserPanel : UserControl
{
    private const int ThumbSize = 56;

    private readonly Label _rootLabel;
    private readonly TextBox _search;
    private readonly CheckBox _includeSub;
    private readonly TreeView _tree;
    private readonly ListView _list;
    private readonly ImageList _thumbs;
    private readonly SplitContainer _split;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _watchTimer = new() { Interval = 400 };
    private readonly HashSet<string> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Stamp, Bitmap Thumb)> _thumbCache = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _thumbCts;
    private bool _structureChanged;
    private bool _suppressActivate;

    public FileBrowserPanel()
    {
        BackColor = Theme.PanelColor;
        _rootLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            AutoEllipsis = true,
            ForeColor = Theme.DimTextColor,
            Padding = new Padding(4, 4, 0, 0),
            Text = "폴더를 여세요",
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false, BackColor = Theme.PanelColor };
        buttons.Controls.Add(Ui.Button("폴더 열기", (_, _) => OpenFolderRequested?.Invoke(this, EventArgs.Empty)));
        buttons.Controls.Add(Ui.Button("Unity", (_, _) => OpenUnityRequested?.Invoke(this, EventArgs.Empty)));
        buttons.Controls.Add(Ui.Button("새로 고침", (_, _) => RefreshAll()));

        _search = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = "파일 이름 검색",
            BackColor = Theme.InputColor,
            ForeColor = Theme.TextColor,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _search.TextChanged += (_, _) => RefreshList();
        _includeSub = Ui.Check("하위 폴더 포함", true);
        _includeSub.Dock = DockStyle.Top;
        _includeSub.Padding = new Padding(4, 0, 0, 0);
        _includeSub.CheckedChanged += (_, _) => RefreshList();

        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            BackColor = Theme.InputColor,
            ForeColor = Theme.TextColor,
            BorderStyle = BorderStyle.None,
            LineColor = Theme.BorderColor,
            ItemHeight = 20,
        };
        _tree.BeforeExpand += (_, e) => PopulateChildren(e.Node!);
        _tree.AfterSelect += (_, _) => RefreshList();

        _thumbs = new ImageList { ImageSize = new Size(ThumbSize, ThumbSize), ColorDepth = ColorDepth.Depth32Bit };
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.LargeIcon,
            LargeImageList = _thumbs,
            MultiSelect = false,
            LabelEdit = true,
            HideSelection = false,
            ShowItemToolTips = true,
            BackColor = Theme.InputColor,
            ForeColor = Theme.TextColor,
            BorderStyle = BorderStyle.None,
        };
        _list.ItemSelectionChanged += (_, e) =>
        {
            if (e.IsSelected && !_suppressActivate && e.Item?.Name is { Length: > 0 } path)
                FileActivated?.Invoke(this, path);
        };
        _list.BeforeLabelEdit += (_, e) => { if (_list.Items[e.Item].Name.Length == 0) e.CancelEdit = true; };
        _list.AfterLabelEdit += OnAfterLabelEdit;
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F2 && _list.SelectedItems.Count == 1) { _list.SelectedItems[0].BeginEdit(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && SelectedPath is not null) { DeleteSelected(); e.Handled = true; }
        };

        _menu = new ContextMenuStrip { Renderer = Theme.Renderer };
        var miNew = new ToolStripMenuItem("새 프로젝트…", null, (_, _) => NewImageRequested?.Invoke(this, CurrentFolder ?? RootFolder ?? ""));
        var miRename = new ToolStripMenuItem("이름 바꾸기", null, (_, _) => _list.SelectedItems.Cast<ListViewItem>().FirstOrDefault()?.BeginEdit()) { ShortcutKeyDisplayString = "F2" };
        var miExplorer = new ToolStripMenuItem("탐색기에서 열기", null, (_, _) => ShowInExplorer());
        var miDelete = new ToolStripMenuItem("삭제(휴지통)", null, (_, _) => DeleteSelected()) { ShortcutKeyDisplayString = "Del" };
        _menu.Items.AddRange([miNew, new ToolStripSeparator(), miRename, miExplorer, miDelete]);
        foreach (ToolStripItem it in _menu.Items) it.ForeColor = Theme.TextColor;
        _menu.Opening += (_, e) =>
        {
            miNew.Enabled = RootFolder is not null;
            bool hasFile = SelectedPath is not null;
            miRename.Enabled = miDelete.Enabled = hasFile;
            miExplorer.Enabled = hasFile || CurrentFolder is not null;
        };
        _list.ContextMenuStrip = _menu;
        _list.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = _list.HitTest(e.Location).Item;
            if (hit is null) _list.SelectedItems.Clear();
        };

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 4,
            BackColor = Theme.BorderColor,
        };
        _split.Panel1.Controls.Add(_tree);
        _split.Panel2.Controls.Add(_list);
        _split.Panel2.Controls.Add(_includeSub);
        _split.Panel2.Controls.Add(_search);

        Controls.Add(_split);
        Controls.Add(buttons);
        Controls.Add(_rootLabel);
        Controls.Add(Ui.Header("파일"));

        _watchTimer.Tick += (_, _) => FlushWatcherChanges();
    }

    public string? RootFolder { get; private set; }

    /// <summary>트리에서 고른 폴더.</summary>
    public string? CurrentFolder => _tree.SelectedNode?.Tag as string ?? RootFolder;

    public string? SelectedPath => _list.SelectedItems.Count == 1 && _list.SelectedItems[0].Name.Length > 0 ? _list.SelectedItems[0].Name : null;

    public bool IncludeSubfolders
    {
        get => _includeSub.Checked;
        set => _includeSub.Checked = value;
    }

    public int TreeHeight
    {
        get => _split.SplitterDistance;
        set { if (value > 40 && value < _split.Height - 60) _split.SplitterDistance = value; }
    }

    public event EventHandler<string>? FileActivated;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? OpenUnityRequested;
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
        new ToolTip().SetToolTip(_rootLabel, RootFolder);
        StartWatcher();
        RebuildTree(RootFolder);
    }

    private void RebuildTree(string? selectFolder)
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        if (RootFolder is not null && Directory.Exists(RootFolder))
        {
            var root = MakeNode(RootFolder);
            root.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(RootFolder)) is { Length: > 0 } n ? n : RootFolder;
            _tree.Nodes.Add(root);
            PopulateChildren(root);
            root.Expand();
            _tree.SelectedNode = FindOrExpand(root, selectFolder) ?? root;
        }
        _tree.EndUpdate();
        RefreshList();
    }

    private static TreeNode MakeNode(string dir)
    {
        var node = new TreeNode(Path.GetFileName(dir)) { Tag = dir };
        if (HasSubdirectories(dir)) node.Nodes.Add(new TreeNode("…"));
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

    private static void PopulateChildren(TreeNode node)
    {
        if (node.Nodes.Count != 1 || node.Nodes[0].Tag is not null) return;
        node.Nodes.Clear();
        try
        {
            foreach (var d in EnumerateDirs((string)node.Tag!)) node.Nodes.Add(MakeNode(d));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>트리에서 폴더 노드를 찾는다(필요한 만큼 펼침).</summary>
    private static TreeNode? FindOrExpand(TreeNode root, string? folder)
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
            var next = node.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag is string t
                && (string.Equals(t, target, StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith(t + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
            if (next is null) return null;
            node.Expand();
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
        _list.BeginUpdate();
        _list.Items.Clear();
        if (folder is null || !Directory.Exists(folder))
        {
            _list.EndUpdate();
            return;
        }
        List<string> files;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = _includeSub.Checked,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            };
            var projects = Directory.EnumerateFiles(folder, "*" + SpriteProject.FileExtension, options).ToList();
            var projectKeys = projects.Select(p => Path.ChangeExtension(p, null)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // 같은 이름의 .gpixel이 있으면 PNG(내보낸 시트)는 목록에서 뺀다.
            files = projects
                .Concat(Directory.EnumerateFiles(folder, "*.png", options).Where(p => !projectKeys.Contains(Path.ChangeExtension(p, null))))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = new();
        }
        var query = _search.Text.Trim();
        if (query.Length > 0)
            files = files.Where(f => Path.GetFileName(f).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        var pending = new List<(string Path, DateTime Stamp)>();
        foreach (var f in files)
        {
            var item = new ListViewItem(Path.GetFileName(f)) { Name = f, ToolTipText = Path.GetRelativePath(RootFolder ?? folder, f) };
            var stamp = SafeStamp(f);
            if (_thumbCache.TryGetValue(f, out var cached) && cached.Stamp == stamp)
            {
                if (!_thumbs.Images.ContainsKey(f)) _thumbs.Images.Add(f, cached.Thumb);
                item.ImageKey = f;
            }
            else pending.Add((f, stamp));
            _list.Items.Add(item);
        }
        _suppressActivate = true;
        if (keepSelected is not null && _list.Items[keepSelected] is { } sel) sel.Selected = true;
        _suppressActivate = false;
        _list.EndUpdate();
        if (keepSelected is not null && _list.Items[keepSelected] is { } kept) kept.EnsureVisible();
        if (pending.Count > 0) LoadThumbnails(pending);
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
                Bitmap thumb;
                try
                {
                    var img = IsProject(path) ? ProjectIO.LoadThumbnail(path) : PngIO.Load(path);
                    thumb = ImageUtil.MakeThumbnail(img, ThumbSize);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                {
                    continue;
                }
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(() =>
                {
                    if (_thumbCache.TryGetValue(path, out var old)) old.Thumb.Dispose();
                    _thumbCache[path] = (stamp, thumb);
                    if (_thumbs.Images.ContainsKey(path)) _thumbs.Images.RemoveByKey(path);
                    _thumbs.Images.Add(path, thumb);
                    if (_list.Items[path] is { } item)
                    {
                        item.ImageKey = "";
                        item.ImageKey = path;
                    }
                });
            }
        }, token);
    }

    /// <summary>파일 하나의 썸네일을 다시 만들고(저장 후) 목록을 갱신한다.</summary>
    public void RefreshFile(string path)
    {
        if (_thumbCache.Remove(path, out var old)) old.Thumb.Dispose();
        if (_thumbs.Images.ContainsKey(path)) _thumbs.Images.RemoveByKey(path);
        RefreshList();
    }

    /// <summary>목록에서 파일을 선택 표시한다(열기 이벤트 없이).</summary>
    public void SelectFile(string? path)
    {
        _suppressActivate = true;
        foreach (ListViewItem it in _list.SelectedItems) it.Selected = false;
        if (path is not null && _list.Items[path] is { } item)
        {
            item.Selected = true;
            item.EnsureVisible();
        }
        _suppressActivate = false;
    }

    // ───────────── 우클릭 동작 ─────────────

    private void OnAfterLabelEdit(object? sender, LabelEditEventArgs e)
    {
        var item = _list.Items[e.Item];
        if (e.Label is null) return;
        e.CancelEdit = true; // 목록은 새로 고침으로 갱신한다.
        var oldPath = item.Name;
        var newName = e.Label.Trim();
        var ext = Path.GetExtension(oldPath);
        if (!newName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) newName += ext;
        if (newName.Length <= ext.Length || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Ui.Error(FindForm(), "쓸 수 없는 파일 이름입니다.");
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
            Ui.Error(FindForm(), $"이름을 바꾸지 못했습니다.\n{ex.Message}");
            return;
        }
        FileRenamed?.Invoke(this, (oldPath, newPath));
        BeginInvoke(() =>
        {
            RefreshList();
            SelectFile(newPath);
        });
    }

    private void ShowInExplorer()
    {
        var target = SelectedPath;
        var psi = target is not null
            ? new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"")
            : new ProcessStartInfo("explorer.exe", $"\"{CurrentFolder}\"");
        try { Process.Start(psi); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Ui.Error(FindForm(), $"탐색기를 열지 못했습니다.\n{ex.Message}");
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
            Ui.Error(FindForm(), $"삭제할 파일을 확인하지 못했습니다.\n{ex.Message}");
            return;
        }
        var extra = companions.Count > 0 ? $"\n함께 삭제: {string.Join(", ", companions.Select(Path.GetFileName))}" : "";
        if (MessageBox.Show(FindForm(), $"{Path.GetFileName(path)}을(를) 휴지통으로 보낼까요?{extra}", "삭제",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            foreach (var p in companions.Append(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Ui.Error(FindForm(), $"삭제를 완료하지 못했습니다. 일부 파일은 이미 휴지통으로 이동했을 수 있습니다.\n{ex.Message}");
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
        if (RootFolder is null || !Directory.Exists(RootFolder)) return;
        try
        {
            _watcher = new FileSystemWatcher(RootFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += (s, e) => { OnFsEvent(s, e); Queue(e.OldFullPath, true); };
            _watcher.Error += (_, _) => Queue(RootFolder, true);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _watcher = null;
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        bool isPng = e.FullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || IsProject(e.FullPath);
        bool isDir = e.ChangeType != WatcherChangeTypes.Changed && !Path.HasExtension(e.FullPath);
        if (!isPng && !isDir && !e.FullPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            && !e.FullPath.EndsWith(SidecarIO.Extension, StringComparison.OrdinalIgnoreCase)) return;
        Queue(e.FullPath, isDir);
    }

    private void Queue(string path, bool structure)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
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
        foreach (var p in changed)
            if (_thumbCache.Remove(p, out var old))
            {
                if (_thumbs.Images.ContainsKey(p)) _thumbs.Images.RemoveByKey(p);
                old.Thumb.Dispose();
            }
        if (_structureChanged) RebuildTree(CurrentFolder);
        else RefreshList();
        _structureChanged = false;
        FilesChanged?.Invoke(this, changed);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _thumbCts?.Cancel();
            _watcher?.Dispose();
            _watchTimer.Dispose();
            foreach (var (_, t) in _thumbCache.Values) t.Dispose();
        }
        base.Dispose(disposing);
    }
}
