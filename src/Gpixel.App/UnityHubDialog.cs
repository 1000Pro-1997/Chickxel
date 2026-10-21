using System.Globalization;
using Gpixel.Core.IO;

namespace Gpixel.App;

/// <summary>
/// Unity Hub에 등록된 프로젝트 목록(즐겨찾기 먼저, 최근 수정 순). 고르면 그 프로젝트의 Assets 폴더가 결과가 된다.
/// Hub 목록을 읽을 수 없으면 "폴더 직접 선택"으로 안내한다.
/// </summary>
public sealed class UnityHubDialog : DialogBase
{
    private static readonly double[] Columns = [200, 110, 130, 270];
    private static readonly string[] ColumnTitles = ["이름", "Unity 버전", "마지막 수정", "경로"];

    private readonly ListBox _list;

    /// <summary>고른 폴더(프로젝트의 Assets 또는 직접 고른 폴더).</summary>
    public string? SelectedFolder { get; private set; }

    public UnityHubDialog() : base("Unity 프로젝트 열기", "열기")
    {
        _list = new ListBox();
        _list.DoubleTapped += (_, _) => { if (TryAccept()) DialogResult = DialogResult.OK; };
        var table = Table(_list, Columns);
        table.Width = 720;
        table.Height = 360;

        var result = UnityHubProjects.Load();
        var message = Ui.Label("");
        if (!result.Success)
        {
            message.Text = $"Unity Hub 프로젝트 목록을 읽지 못했습니다: {result.Error}\n아래 \"폴더 직접 선택\"으로 프로젝트의 Assets 폴더를 고르세요.";
            message.Foreground = AppTheme.WarningBrush;
        }
        else if (result.Projects.Count == 0)
        {
            message.Text = "Unity Hub에 등록된 프로젝트가 없습니다. \"폴더 직접 선택\"을 쓰세요.";
        }
        else
        {
            message.Text = "즐겨찾기(별 표시) 먼저, 최근 수정 순. 회색은 경로가 없어진 프로젝트입니다.";
        }
        Fill(_list, result.Projects, Columns);

        var browse = Ui.Button("폴더 직접 선택…", (_, _) =>
        {
            var folder = Ui.PickFolder(this, "열 폴더(Unity 프로젝트의 Assets 등)를 고르세요.");
            if (folder is null) return;
            SelectedFolder = ResolveAssets(folder);
            DialogResult = DialogResult.OK;
        });
        browse.HorizontalAlignment = HorizontalAlignment.Left;
        Body.Children.Add(message);
        Body.Children.Add(table);
        Body.Children.Add(browse);
        OkButton.Click += (_, _) => { if (!TryAccept()) DialogResult = DialogResult.None; };
    }

    /// <summary>
    /// 프로젝트 목록을 리스트에 채운다(시작 화면과 공유). 한 줄은 <paramref name="columnWidths"/> 너비의
    /// 이름·Unity 버전·마지막 수정·경로 칸이고, 항목 Tag는 <see cref="UnityHubProject"/>다.
    /// </summary>
    public static void Fill(ListBox list, IReadOnlyList<UnityHubProject> projects, IReadOnlyList<double> columnWidths)
    {
        list.Items.Clear();
        foreach (var p in projects)
        {
            var row = Row(columnWidths, p.Exists ? AppTheme.TextBrush : AppTheme.DimTextBrush, false,
                p.Title,
                p.Version,
                p.LastModified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                p.Path);
            if (p.IsFavorite && row.Children[0] is TextBlock title)
                title.Inlines = [Ui.InlineIcon(VectorIcon.Star, title.Foreground ?? AppTheme.TextBrush, 12, filled: true), new Avalonia.Controls.Documents.Run(" " + p.Title)];
            var item = new ListBoxItem { Content = row, Tag = p, Padding = new Thickness(0, 3), MinHeight = 22 };
            ToolTip.SetTip(item, p.Exists ? p.Path : p.Path + " (경로 없음)");
            list.Items.Add(item);
        }
    }

    /// <summary>
    /// 열 제목 줄 + 리스트(가로로 넘치면 제목과 함께 가로 스크롤). <see cref="Fill"/>과 같은 열 너비를 준다.
    /// </summary>
    internal static Control Table(ListBox list, IReadOnlyList<double> columnWidths)
    {
        list.SelectionMode = SelectionMode.Single;
        list.Background = AppTheme.InputBrush;
        list.Foreground = AppTheme.TextBrush;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var header = new Border
        {
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 3),
            Child = Row(columnWidths, AppTheme.DimTextBrush, true, ColumnTitles),
        };
        DockPanel.SetDock(header, Dock.Top);
        var dock = new DockPanel { LastChildFill = true, Background = AppTheme.InputBrush };
        dock.Children.Add(header);
        dock.Children.Add(list);
        return new ScrollViewer
        {
            Content = dock,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>리스트에서 고른 Hub 프로젝트.</summary>
    internal static UnityHubProject? SelectedProject(ListBox list) => (list.SelectedItem as ListBoxItem)?.Tag as UnityHubProject;

    private static Grid Row(IReadOnlyList<double> widths, IBrush brush, bool bold, params string[] cells)
    {
        var grid = new Grid();
        for (int i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(i < widths.Count ? widths[i] : 100, GridUnitType.Pixel));
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

    /// <summary>프로젝트 루트를 고르면 그 안의 Assets 폴더로 바꾼다.</summary>
    public static string ResolveAssets(string folder)
        => UnityProjectLocator.IsProjectRoot(folder) ? Path.Combine(folder, "Assets") : folder;

    private bool TryAccept()
    {
        if (SelectedProject(_list) is not { } p)
        {
            Ui.Info(this, "목록에서 프로젝트를 고르세요.");
            return false;
        }
        if (!p.Exists || !Directory.Exists(p.AssetsPath))
        {
            Ui.Error(this, $"프로젝트 폴더를 찾을 수 없습니다:\n{p.AssetsPath}");
            return false;
        }
        SelectedFolder = p.AssetsPath;
        return true;
    }
}
