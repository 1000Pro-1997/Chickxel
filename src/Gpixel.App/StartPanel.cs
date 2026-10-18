using Gpixel.Core.IO;

namespace Gpixel.App;

/// <summary>시작 화면: 최근 폴더·최근 파일 + Unity Hub 프로젝트에서 바로 고르기.</summary>
public sealed class StartPanel : UserControl
{
    private readonly ListBox _recentFolders;
    private readonly ListBox _recentFiles;
    private readonly ListView _hub;
    private readonly Label _hubMessage;

    public event EventHandler<string>? FolderChosen;
    public event EventHandler<string>? FileChosen;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? NewImageRequested;

    public StartPanel()
    {
        BackColor = Theme.BackColor;
        Padding = new Padding(24);

        var title = new Label
        {
            Text = "Gpixel",
            Font = new Font("Malgun Gothic", 20f, FontStyle.Bold),
            ForeColor = Theme.TextColor,
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 0),
        };
        var subtitle = Ui.Label("게임용 픽셀 아트 편집기 — 폴더나 Unity 프로젝트를 고르세요.");
        subtitle.ForeColor = Theme.DimTextColor;
        var actions = Ui.Row(
            Ui.Button("폴더 열기…", (_, _) => OpenFolderRequested?.Invoke(this, EventArgs.Empty)),
            Ui.Button("새 프로젝트…", (_, _) => NewImageRequested?.Invoke(this, EventArgs.Empty)),
            Ui.Button("Hub 목록 새로 고침", (_, _) => LoadHub()));

        _recentFolders = MakeList();
        _recentFolders.DoubleClick += (_, _) => { if (_recentFolders.SelectedItem is string s) FolderChosen?.Invoke(this, s); };
        _recentFolders.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && _recentFolders.SelectedItem is string s) FolderChosen?.Invoke(this, s); };
        _recentFiles = MakeList();
        _recentFiles.DoubleClick += (_, _) => { if (_recentFiles.SelectedItem is string s) FileChosen?.Invoke(this, s); };
        _recentFiles.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && _recentFiles.SelectedItem is string s) FileChosen?.Invoke(this, s); };

        _hub = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Dock = DockStyle.Fill,
            BackColor = Theme.InputColor,
            ForeColor = Theme.TextColor,
            BorderStyle = BorderStyle.None,
            ShowItemToolTips = true,
        };
        _hub.Columns.Add("이름", 180);
        _hub.Columns.Add("Unity 버전", 100);
        _hub.Columns.Add("마지막 수정", 120);
        _hub.Columns.Add("경로", 300);
        _hub.DoubleClick += (_, _) => ChooseHub();
        _hub.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) ChooseHub(); };
        _hubMessage = Ui.Label("");
        _hubMessage.Dock = DockStyle.Top;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            BackColor = Theme.BackColor,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);
        layout.Controls.Add(subtitle, 0, 1);
        layout.SetColumnSpan(subtitle, 2);
        layout.Controls.Add(actions, 0, 2);
        layout.SetColumnSpan(actions, 2);
        layout.Controls.Add(Section("최근 폴더 (더블클릭)", _recentFolders), 0, 3);
        layout.Controls.Add(Section("최근 파일 (더블클릭)", _recentFiles), 0, 4);
        var hubBox = Section("Unity Hub 프로젝트 (더블클릭하면 Assets 폴더를 엽니다)", _hub);
        hubBox.Controls.Add(_hubMessage);
        _hubMessage.BringToFront();
        _hub.BringToFront();
        layout.Controls.Add(hubBox, 1, 3);
        layout.SetRowSpan(hubBox, 2);
        Controls.Add(layout);
    }

    private static ListBox MakeList() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Theme.InputColor,
        ForeColor = Theme.TextColor,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        ItemHeight = 20,
    };

    private static Panel Section(string title, Control content)
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4), BackColor = Theme.BackColor };
        content.Dock = DockStyle.Fill;
        p.Controls.Add(content);
        var label = Ui.Label(title, bold: true);
        label.Dock = DockStyle.Top;
        p.Controls.Add(label);
        return p;
    }

    public void SetRecent(IEnumerable<string> folders, IEnumerable<string> files)
    {
        _recentFolders.Items.Clear();
        _recentFolders.Items.AddRange(folders.Cast<object>().ToArray());
        _recentFiles.Items.Clear();
        _recentFiles.Items.AddRange(files.Cast<object>().ToArray());
    }

    /// <summary>Unity Hub 목록을 다시 읽는다.</summary>
    public void LoadHub()
    {
        var result = UnityHubProjects.Load();
        UnityHubDialog.Fill(_hub, result.Projects);
        _hubMessage.ForeColor = result.Success ? Theme.DimTextColor : Theme.WarningColor;
        _hubMessage.Text = !result.Success
            ? $"Unity Hub 목록을 읽지 못했습니다({result.Error}). \"폴더 열기\"로 직접 고르세요."
            : result.Projects.Count == 0 ? "등록된 프로젝트가 없습니다. \"폴더 열기\"로 직접 고르세요." : "";
    }

    private void ChooseHub()
    {
        if (_hub.SelectedItems.Count == 0 || _hub.SelectedItems[0].Tag is not UnityHubProject p) return;
        if (!p.Exists || !Directory.Exists(p.AssetsPath))
        {
            Ui.Error(FindForm(), $"프로젝트 폴더를 찾을 수 없습니다:\n{p.AssetsPath}");
            return;
        }
        FolderChosen?.Invoke(this, p.AssetsPath);
    }
}
