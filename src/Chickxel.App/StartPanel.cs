using Chickxel.Core.IO;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>시작 화면: 최근 폴더·최근 파일 + Unity Hub 프로젝트에서 바로 고르기.</summary>
public sealed class StartPanel : UserControl
{
    private static readonly double[] HubColumns = [180, 100, 120, 300];

    private readonly ListBox _recentFolders;
    private readonly ListBox _recentFiles;
    private readonly ListBox _hub;
    private readonly TextBlock _hubMessage;

    public event EventHandler<string>? FolderChosen;
    public event EventHandler<string>? FileChosen;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? NewImageRequested;

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

        _recentFolders = MakeList();
        _recentFolders.DoubleTapped += (_, _) => { if (Selected(_recentFolders) is { } s) FolderChosen?.Invoke(this, s); };
        _recentFolders.KeyDown += (_, e) => { if (e.Key == Key.Enter && Selected(_recentFolders) is { } s) FolderChosen?.Invoke(this, s); };
        _recentFiles = MakeList();
        _recentFiles.DoubleTapped += (_, _) => { if (Selected(_recentFiles) is { } s) FileChosen?.Invoke(this, s); };
        _recentFiles.KeyDown += (_, e) => { if (e.Key == Key.Enter && Selected(_recentFiles) is { } s) FileChosen?.Invoke(this, s); };

        _hub = new ListBox();
        _hub.DoubleTapped += (_, _) => ChooseHub();
        _hub.KeyDown += (_, e) => { if (e.Key == Key.Enter) ChooseHub(); };
        var hubTable = UnityHubDialog.Table(_hub, HubColumns);
        _hubMessage = Ui.Label("");
        _hubMessage.TextWrapping = TextWrapping.Wrap;
        _hubMessage.IsVisible = false;
        DockPanel.SetDock(_hubMessage, Dock.Top);

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("4*,6*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,*"),
            Background = AppTheme.BackBrush,
        };
        Place(layout, title, 0, 0, columnSpan: 2);
        Place(layout, subtitle, 1, 0, columnSpan: 2);
        Place(layout, actions, 2, 0, columnSpan: 2);
        Place(layout, Section(StartStrings.RecentFolders, _recentFolders), 3, 0);
        Place(layout, Section(StartStrings.RecentFiles, _recentFiles), 4, 0);
        Place(layout, Section(StartStrings.HubProjects, hubTable, _hubMessage), 3, 1, rowSpan: 2);
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

    /// <summary>제목 줄(+ 그 아래 덧붙일 컨트롤) 아래에 내용을 채운 칸.</summary>
    private static DockPanel Section(string title, Control content, Control? belowTitle = null)
    {
        var p = new DockPanel { LastChildFill = true, Margin = new Thickness(4), Background = AppTheme.BackBrush };
        var label = Ui.Label(title, bold: true);
        DockPanel.SetDock(label, Dock.Top);
        p.Children.Add(label);
        if (belowTitle is not null)
        {
            DockPanel.SetDock(belowTitle, Dock.Top);
            p.Children.Add(belowTitle);
        }
        p.Children.Add(content);
        return p;
    }

    private static void FillStrings(ListBox list, IEnumerable<string> items)
    {
        list.Items.Clear();
        foreach (var s in items)
            list.Items.Add(new ListBoxItem { Content = s, Tag = s, Padding = new Thickness(6, 2), MinHeight = 20 });
    }

    private static string? Selected(ListBox list) => (list.SelectedItem as ListBoxItem)?.Tag as string;

    public void SetRecent(IEnumerable<string> folders, IEnumerable<string> files)
    {
        FillStrings(_recentFolders, folders);
        FillStrings(_recentFiles, files);
    }

    /// <summary>Unity Hub 목록을 다시 읽는다.</summary>
    public void LoadHub()
    {
        var result = UnityHubProjects.Load();
        UnityHubDialog.Fill(_hub, result.Projects, HubColumns);
        _hubMessage.Foreground = result.Success ? AppTheme.DimTextBrush : AppTheme.WarningBrush;
        _hubMessage.Text = !result.Success
            ? StartStrings.FormatHubLoadFailed(error: result.Error)
            : result.Projects.Count == 0 ? StartStrings.HubEmpty : "";
        _hubMessage.IsVisible = !string.IsNullOrEmpty(_hubMessage.Text);
    }

    private void ChooseHub()
    {
        if (UnityHubDialog.SelectedProject(_hub) is not { } p) return;
        if (!p.Exists || !Directory.Exists(p.AssetsPath))
        {
            Ui.Error(this, StartStrings.FormatHubFolderMissing(path: p.AssetsPath));
            return;
        }
        FolderChosen?.Invoke(this, p.AssetsPath);
    }
}
