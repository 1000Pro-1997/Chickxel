using System.Globalization;
using Chickxel.Core.IO;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 시작 화면: 왼쪽에 즐겨찾기 폴더 · 최근 폴더(줄 왼쪽 별을 누르면 즐겨찾기에 넣고 뺌) · 최근 파일,
/// 오른쪽에 게임 엔진 프로젝트 표(Unity · Godot · Unreal · Aseprite에서 최근에 연 것 — <see cref="EngineProjects"/>, 더블클릭하면 그림 폴더를 엶).
/// </summary>
public sealed class StartPanel : UserControl
{
    /// <summary>프로젝트 표 열 너비: 엔진 · 이름 · 열 폴더 · 마지막 수정.</summary>
    private static readonly double[] ProjectColumns = [130, 170, 210, 130];

    private readonly ListBox _favorites;
    private readonly TextBlock _favoritesEmpty;
    private readonly ListBox _recentFolders;
    private readonly ListBox _recentFiles;
    private readonly ListBox _projects;
    private readonly TextBlock _projectsMessage;
    private IReadOnlyList<string> _favoriteFolders = [], _favoriteFiles = [];

    /// <summary>폴더 · 파일 목록 한 줄이 가리키는 것.</summary>
    public readonly record struct StartEntry(string Path, bool IsFile);

    public event EventHandler<string>? FolderChosen;
    public event EventHandler<string>? FileChosen;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? NewImageRequested;
    /// <summary>폴더·파일 줄의 별을 누름(즐겨찾기에 넣거나 빼기 — 부르는 쪽이 설정을 고치고 <see cref="SetRecent"/>로 다시 그림).</summary>
    public event EventHandler<StartEntry>? FavoriteToggled;

    public StartPanel()
    {
        Background = AppTheme.BackBrush;
        Padding = new Thickness(24);

        var title = new TextBlock
        {
            Text = "Chickxel",
            FontFamily = AppTheme.FontFamily,
            FontSize = 26,
            FontWeight = FontWeight.Bold,
            Foreground = AppTheme.TextBrush,
            Margin = new Thickness(3, 0, 3, 0),
        };
        var subtitle = Ui.Label(StartStrings.Subtitle);
        subtitle.Foreground = AppTheme.DimTextBrush;
        var actions = Ui.Row(
            Ui.Button(StartStrings.OpenFolder, (_, _) => OpenFolderRequested?.Invoke(this, EventArgs.Empty)),
            Ui.Button(StartStrings.NewProject, (_, _) => NewImageRequested?.Invoke(this, EventArgs.Empty)),
            Ui.Button(StartStrings.RefreshHub, (_, _) => LoadHub()));
        actions.Margin = new Thickness(0, 4);

        _favorites = PathList();
        _favoritesEmpty = Ui.Label(StartStrings.FavoritesEmpty);
        _favoritesEmpty.Foreground = AppTheme.DimTextBrush;
        _favoritesEmpty.TextWrapping = TextWrapping.Wrap;
        _favoritesEmpty.Margin = new Thickness(6, 4);
        DockPanel.SetDock(_favoritesEmpty, Dock.Top);
        _recentFolders = PathList();
        _recentFiles = PathList();

        _projects = MakeList();
        ScrollViewer.SetHorizontalScrollBarVisibility(_projects, ScrollBarVisibility.Disabled);
        _projects.DoubleTapped += (_, _) => ChooseProject();
        _projects.KeyDown += (_, e) => { if (e.Key == Key.Enter) ChooseProject(); };
        _projectsMessage = Ui.Label("");
        _projectsMessage.TextWrapping = TextWrapping.Wrap;
        _projectsMessage.IsVisible = false;
        DockPanel.SetDock(_projectsMessage, Dock.Top);

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("4*,6*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,*,*"),
            Background = AppTheme.BackBrush,
        };
        Place(layout, title, 0, 0, columnSpan: 2);
        Place(layout, subtitle, 1, 0, columnSpan: 2);
        Place(layout, actions, 2, 0, columnSpan: 2);
        Place(layout, Section(StartStrings.Favorites, _favorites, _favoritesEmpty), 3, 0);
        Place(layout, Section(StartStrings.RecentFolders, _recentFolders), 4, 0);
        Place(layout, Section(StartStrings.RecentFiles, _recentFiles), 5, 0);
        var projectsTitle = Ui.Row(Ui.Label(StartStrings.HubProjects, bold: true), Ui.InfoBadge(StartStrings.HubInfo));
        Place(layout, Section(projectsTitle, ProjectTable(), _projectsMessage), 3, 1, rowSpan: 3);
        Content = layout;
    }

    private static void Place(Grid grid, Control c, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, column);
        Grid.SetRowSpan(c, rowSpan);
        Grid.SetColumnSpan(c, columnSpan);
        grid.Children.Add(c);
    }

    private static ListBox MakeList() => new()
    {
        SelectionMode = SelectionMode.Single,
        Background = AppTheme.InputBrush,
        Foreground = AppTheme.TextBrush,
    };

    /// <summary>폴더 · 파일 목록(즐겨찾기 · 최근 폴더 · 최근 파일): 더블클릭·Enter로 연다(줄이 폴더면 폴더, 파일이면 파일).</summary>
    private ListBox PathList()
    {
        var list = MakeList();
        void Choose()
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is not StartEntry e) return;
            if (e.IsFile) FileChosen?.Invoke(this, e.Path);
            else FolderChosen?.Invoke(this, e.Path);
        }
        list.DoubleTapped += (_, _) => Choose();
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Choose(); };
        return list;
    }

    private static DockPanel Section(string title, Control content, Control? belowTitle = null) => Section(Ui.Label(title, bold: true), content, belowTitle);

    /// <summary>제목 줄(+ 그 아래 덧붙일 컨트롤) 아래에 내용을 채운 칸.</summary>
    private static DockPanel Section(Control title, Control content, Control? belowTitle = null)
    {
        var p = new DockPanel { LastChildFill = true, Margin = new Thickness(4), Background = AppTheme.BackBrush };
        DockPanel.SetDock(title, Dock.Top);
        p.Children.Add(title);
        if (belowTitle is not null)
        {
            DockPanel.SetDock(belowTitle, Dock.Top);
            p.Children.Add(belowTitle);
        }
        p.Children.Add(content);
        return p;
    }

    /// <summary>폴더·파일 한 줄: 왼쪽 별(즐겨찾기면 채운 노란 별 — 누르면 넣고 뺌) + 경로.</summary>
    private ListBoxItem PathItem(StartEntry entry, bool favorite)
    {
        var icon = new IconView(VectorIcon.Star, favorite ? HelpWindow.Yellow : AppTheme.DimTextBrush) { Width = 13, Height = 13, Filled = favorite };
        var star = new Button
        {
            Content = icon,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(3),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        }.Tip(favorite ? StartStrings.FavoriteRemove : StartStrings.FavoriteAdd);
        star.Click += (_, e) =>
        {
            e.Handled = true;
            FavoriteToggled?.Invoke(this, entry);
        };
        var text = new TextBlock { Text = entry.Path, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var row = new DockPanel { Children = { star, text } };
        DockPanel.SetDock(star, Dock.Left);
        var item = new ListBoxItem { Content = row, Tag = entry, Padding = new Thickness(2, 1, 6, 1), MinHeight = 22 };
        ToolTip.SetTip(item, entry.Path);
        return item;
    }

    private static bool Same(string a, string b)
        => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 즐겨찾기(폴더 먼저, 그다음 파일) · 최근 폴더 · 최근 파일을 다시 채운다(최근 줄의 별은 즐겨찾기에 든 것만 채움).
    /// </summary>
    public void SetRecent(IEnumerable<string> folders, IEnumerable<string> files, IEnumerable<string> favoriteFolders, IEnumerable<string> favoriteFiles)
    {
        _favoriteFolders = [.. favoriteFolders];
        _favoriteFiles = [.. favoriteFiles];
        _favorites.Items.Clear();
        foreach (var f in _favoriteFolders) _favorites.Items.Add(PathItem(new StartEntry(f, false), favorite: true));
        foreach (var f in _favoriteFiles) _favorites.Items.Add(PathItem(new StartEntry(f, true), favorite: true));
        _favoritesEmpty.IsVisible = _favorites.Items.Count == 0;
        _recentFolders.Items.Clear();
        foreach (var f in folders) _recentFolders.Items.Add(PathItem(new StartEntry(f, false), _favoriteFolders.Any(x => Same(x, f))));
        _recentFiles.Items.Clear();
        foreach (var f in files) _recentFiles.Items.Add(PathItem(new StartEntry(f, true), _favoriteFiles.Any(x => Same(x, f))));
    }

    // ───────────── 게임 엔진 프로젝트 ─────────────

    /// <summary>열 제목 줄 + 프로젝트 목록(가로로 넘치면 제목과 함께 가로 스크롤).</summary>
    private Control ProjectTable()
    {
        var header = new Border
        {
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 3),
            Child = ProjectRow(AppTheme.DimTextBrush, true, StartStrings.ColumnEngine, StartStrings.ColumnName, StartStrings.ColumnFolder, StartStrings.ColumnModified),
        };
        DockPanel.SetDock(header, Dock.Top);
        var dock = new DockPanel { LastChildFill = true, Background = AppTheme.InputBrush, Children = { header, _projects } };
        return new ScrollViewer { Content = dock, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private static Grid ProjectRow(IBrush brush, bool bold, params string[] cells)
    {
        var grid = new Grid();
        for (int i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(ProjectColumns[i], GridUnitType.Pixel));
            var text = new TextBlock
            {
                Text = cells[i],
                Foreground = brush,
                FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 4, 0),
            };
            Grid.SetColumn(text, i);
            grid.Children.Add(text);
        }
        return grid;
    }

    private static string EngineName(GameEngine e) => e switch
    {
        GameEngine.Unity => "Unity",
        GameEngine.Godot => "Godot",
        GameEngine.Unreal => "Unreal",
        _ => "Aseprite",
    };

    /// <summary>열 폴더를 프로젝트 루트 기준으로(예: <c>Assets/Sprites</c>, 루트 자신이면 <c>.</c> 대신 폴더 이름).</summary>
    private static string Relative(EngineProject p)
    {
        static string Trim(string s) => s.TrimEnd('/', '\\');
        string root = Trim(p.Root), art = Trim(p.ArtFolder);
        if (art.Equals(root, StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(art.Replace('\\', '/'));
        return art.StartsWith(root, StringComparison.OrdinalIgnoreCase) && art.Length > root.Length && art[root.Length] is '/' or '\\'
            ? art[(root.Length + 1)..].Replace('\\', '/')
            : art;
    }

    /// <summary>프로젝트 표를 채운다(즐겨찾기한 프로젝트는 이름 앞에 채운 별).</summary>
    public void Fill(IReadOnlyList<EngineProject> projects)
    {
        _projects.Items.Clear();
        foreach (var p in projects)
        {
            var engine = p.Version.Length > 0 ? $"{EngineName(p.Engine)} {p.Version}" : EngineName(p.Engine);
            var row = ProjectRow(p.Exists ? AppTheme.TextBrush : AppTheme.DimTextBrush, false,
                engine, p.Title, Relative(p), p.LastModified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "");
            if (p.IsFavorite && row.Children[1] is TextBlock name)
                name.Inlines = [Ui.InlineIcon(VectorIcon.Star, name.Foreground ?? AppTheme.TextBrush, 12, filled: true), new Avalonia.Controls.Documents.Run(" " + p.Title)];
            var item = new ListBoxItem { Content = row, Tag = p, Padding = new Thickness(0, 3), MinHeight = 22 };
            ToolTip.SetTip(item, p.ArtFolder);
            _projects.Items.Add(item);
        }
        _projectsMessage.Foreground = AppTheme.DimTextBrush;
        _projectsMessage.Text = projects.Count == 0 ? StartStrings.HubEmpty : "";
        _projectsMessage.IsVisible = projects.Count == 0;
    }

    /// <summary>엔진 프로젝트 목록을 다시 읽는다(Unity Hub · Godot · Unreal · Aseprite).</summary>
    public void LoadHub() => Fill(EngineProjects.Load());

    private void ChooseProject()
    {
        if ((_projects.SelectedItem as ListBoxItem)?.Tag is not EngineProject p) return;
        if (!Directory.Exists(p.ArtFolder))
        {
            Ui.Error(this, StartStrings.FormatHubFolderMissing(path: p.ArtFolder));
            return;
        }
        FolderChosen?.Invoke(this, p.ArtFolder);
    }
}
