using System.Globalization;
using Gpixel.Core.IO;

namespace Gpixel.App;

/// <summary>
/// Unity Hub에 등록된 프로젝트 목록(즐겨찾기 먼저, 최근 수정 순). 고르면 그 프로젝트의 Assets 폴더가 결과가 된다.
/// Hub 목록을 읽을 수 없으면 "폴더 직접 선택"으로 안내한다.
/// </summary>
public sealed class UnityHubDialog : DialogBase
{
    private readonly ListView _list;

    /// <summary>고른 폴더(프로젝트의 Assets 또는 직접 고른 폴더).</summary>
    public string? SelectedFolder { get; private set; }

    public UnityHubDialog() : base("Unity 프로젝트 열기", "열기")
    {
        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Width = 720,
            Height = 360,
        };
        _list.Columns.Add("이름", 200);
        _list.Columns.Add("Unity 버전", 110);
        _list.Columns.Add("마지막 수정", 130);
        _list.Columns.Add("경로", 270);
        _list.DoubleClick += (_, _) => { if (TryAccept()) DialogResult = DialogResult.OK; };

        var result = UnityHubProjects.Load();
        var message = Ui.Label("");
        if (!result.Success)
        {
            message.Text = $"Unity Hub 프로젝트 목록을 읽지 못했습니다: {result.Error}\n아래 \"폴더 직접 선택\"으로 프로젝트의 Assets 폴더를 고르세요.";
            message.ForeColor = Theme.WarningColor;
        }
        else if (result.Projects.Count == 0)
        {
            message.Text = "Unity Hub에 등록된 프로젝트가 없습니다. \"폴더 직접 선택\"을 쓰세요.";
        }
        else
        {
            message.Text = "★ 즐겨찾기 먼저, 최근 수정 순. 회색은 경로가 없어진 프로젝트입니다.";
        }
        Fill(_list, result.Projects);

        var browse = Ui.Button("폴더 직접 선택…", (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "열 폴더(Unity 프로젝트의 Assets 등)를 고르세요.", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            SelectedFolder = ResolveAssets(dlg.SelectedPath);
            DialogResult = DialogResult.OK;
        });
        Body.Controls.Add(message);
        Body.Controls.Add(_list);
        Body.Controls.Add(browse);
        OkButton.Click += (_, _) => { if (!TryAccept()) DialogResult = DialogResult.None; };
    }

    /// <summary>프로젝트 목록을 리스트뷰에 채운다(시작 화면과 공유).</summary>
    public static void Fill(ListView list, IReadOnlyList<UnityHubProject> projects)
    {
        list.Items.Clear();
        foreach (var p in projects)
        {
            var item = new ListViewItem([
                (p.IsFavorite ? "★ " : "") + p.Title,
                p.Version,
                p.LastModified?.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                p.Path,
            ])
            {
                Tag = p,
                ForeColor = p.Exists ? Theme.TextColor : Theme.DimTextColor,
                ToolTipText = p.Exists ? p.Path : p.Path + " (경로 없음)",
            };
            list.Items.Add(item);
        }
    }

    /// <summary>프로젝트 루트를 고르면 그 안의 Assets 폴더로 바꾼다.</summary>
    public static string ResolveAssets(string folder)
        => UnityProjectLocator.IsProjectRoot(folder) ? Path.Combine(folder, "Assets") : folder;

    private bool TryAccept()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not UnityHubProject p)
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
