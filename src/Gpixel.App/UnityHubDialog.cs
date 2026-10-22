using System.Globalization;
using Gpixel.Core.IO;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>
/// Unity Hub에 등록된 프로젝트 목록(즐겨찾기 먼저, 최근 수정 순). 고르면 그 프로젝트의 Assets 폴더가 결과가 된다.
/// Hub 목록을 읽을 수 없으면 "폴더 직접 선택"으로 안내한다.
/// </summary>
public sealed class UnityHubDialog : DialogBase
{
    private static readonly double[] Columns = [200, 110, 130, 270];
    private static readonly string[] ColumnTitles = [UiStrings.S_UnityHubDialog_ColumnTitles_B82E425CB9, UiStrings.S_UnityHubDialog_ColumnTitles_9A52A778E0, UiStrings.S_UnityHubDialog_ColumnTitles_76E7750967, UiStrings.S_UnityHubDialog_ColumnTitles_0A77D4532D];

    private readonly ListBox _list;

    /// <summary>고른 폴더(프로젝트의 Assets 또는 직접 고른 폴더).</summary>
    public string? SelectedFolder { get; private set; }

    public UnityHubDialog() : base(UiStrings.S_UnityHubDialog_UnityHubDialog_01D74C119C, UiStrings.S_UnityHubDialog_UnityHubDialog_F72E128AEB)
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
            message.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnityHubDialog_UnityHubDialog_DE17A87853,result.Error);
            message.Foreground = AppTheme.WarningBrush;
        }
        else if (result.Projects.Count == 0)
        {
            message.Text = UiStrings.S_UnityHubDialog_UnityHubDialog_C641F4E17C;
        }
        else
        {
            message.Text = UiStrings.S_UnityHubDialog_UnityHubDialog_73DB9F4625;
        }
        Fill(_list, result.Projects, Columns);

        var browse = Ui.Button(UiStrings.S_UnityHubDialog_UnityHubDialog_F177BFF283, (_, _) =>
        {
            var folder = Ui.PickFolder(this, UiStrings.S_UnityHubDialog_UnityHubDialog_E8C247F967);
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
            ToolTip.SetTip(item, p.Exists ? p.Path : p.Path + UiStrings.S_UnityHubDialog_Fill_E521211C81);
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
            Ui.Info(this, UiStrings.S_UnityHubDialog_TryAccept_B70708BDC1);
            return false;
        }
        if (!p.Exists || !Directory.Exists(p.AssetsPath))
        {
            Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_UnityHubDialog_TryAccept_C901C91A1A,p.AssetsPath));
            return false;
        }
        SelectedFolder = p.AssetsPath;
        return true;
    }
}
