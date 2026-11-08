using Avalonia.VisualTree;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 파일 목록의 프로젝트 묶음(Unity 하위 에셋처럼): 프로젝트가 내보낸 그림·GIF·저장한 프레임
/// (<see cref="Chickxel.Core.IO.ProjectFamily"/>)은 따로 칸을 만들지 않고, 프로젝트 칸 오른쪽 화살표를 누르면 프로젝트 바로 뒤에 펼친다.
/// 펼친 칸들은 같은 띠 색으로 묶여 보이고, 그 안의 파일은 다른 파일과 똑같이 열기·고르기·끌기·이름 바꾸기·삭제가 된다.
/// 펼침 상태는 이 세션 동안 프로젝트 경로별로 기억한다(목록을 새로 만들어도 유지).
/// </summary>
public sealed partial class FileBrowserPanel
{
    /// <summary>펼친 묶음(프로젝트 + 딸린 파일) 띠 색(검정을 옅게 — 목록 바탕보다 조금 어둡고, 마우스 올림·고름 색 위에 겹쳐도 비침).</summary>
    private static readonly IBrush GroupBandBrush = new SolidColorBrush(Colors.Black, 0.18);
    /// <summary>펼친 묶음 테두리(검정, 칸 바탕과 상관없이 늘 보임).</summary>
    private static readonly IBrush GroupLineBrush = Brushes.Black;
    private const double GroupLine = 2;

    /// <summary>펼친 프로젝트(이 세션 동안 기억).</summary>
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>검색어에 맞는 딸린 파일이 있어 펼쳐 보이는 프로젝트(검색이 바뀌면 다시 셈, 기억하지 않음).</summary>
    private readonly HashSet<string> _searchExpanded = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>지금 목록의 프로젝트 → 펼치면 보일 딸린 파일(검색 중이면 맞는 것만, 이름 순).</summary>
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>딸린 파일 → 프로젝트.</summary>
    private readonly Dictionary<string, string> _parentOf = new(StringComparer.OrdinalIgnoreCase);

    private bool IsExpanded(string project) => _expanded.Contains(project) || _searchExpanded.Contains(project);

    /// <summary>
    /// 이번 목록의 묶음을 정하고 칸을 만들 파일(프로젝트와 묶이지 않은 파일)을 돌려준다.
    /// 검색 중이면: 이름이 맞는 파일, 이름이 맞는 프로젝트(딸린 파일 모두), 딸린 파일만 맞는 프로젝트(맞는 파일만) — 딸린 파일이 맞으면 묶음을 펼친다.
    /// </summary>
    private List<string> SetGroups(IEnumerable<string> files, Dictionary<string, List<string>> family, string query)
    {
        _children.Clear();
        _parentOf.Clear();
        _searchExpanded.Clear();
        bool Matches(string path) => Path.GetFileName(path).Contains(query, StringComparison.OrdinalIgnoreCase);
        var shown = new List<string>();
        foreach (var f in files)
        {
            var children = family.GetValueOrDefault(f) ?? [];
            if (query.Length > 0)
            {
                var hits = children.Where(Matches).ToList();
                if (hits.Count > 0) _searchExpanded.Add(f);
                if (!Matches(f))
                {
                    if (hits.Count == 0) continue;
                    children = hits;
                }
            }
            shown.Add(f);
            if (children.Count == 0) continue;
            _children[f] = children;
            foreach (var c in children) _parentOf[c] = f;
        }
        return shown;
    }

    /// <summary>프로젝트 칸 오른쪽(썸네일 옆)의 묶음 펼치기/접기 화살표. 눌러도 프로젝트를 고르거나 열지 않는다.</summary>
    private Button GroupArrow(string project)
    {
        const double height = 24;
        var button = new Button
        {
            Content = new IconView(VectorIcon.ChevronRight, AppTheme.TextBrush, 2.6) { Width = 11, Height = 11 },
            Width = 14,
            Height = height,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            Background = AppTheme.HoverBrush,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, (ThumbSize - height) / 2, -1, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = false,
        };
        button.Click += (_, e) =>
        {
            SetGroupExpanded(project, !IsExpanded(project));
            e.Handled = true;
        };
        return button;
    }

    /// <summary>화살표 방향(접힘 = 오른쪽, 펼침 = 왼쪽)과 도움말(딸린 파일 수·단축키)을 맞춘다.</summary>
    private void UpdateGroupArrow(FileItem item, int count)
    {
        if (item.Arrow is not { Content: IconView icon } arrow) return;
        bool expanded = IsExpanded(item.Path);
        icon.Icon = expanded ? VectorIcon.ChevronLeft : VectorIcon.ChevronRight;
        arrow.Tip(expanded ? UiStrings.FormatFileBrowser_GroupCollapse(count: count) : UiStrings.FormatFileBrowser_GroupExpand(count: count));
    }

    /// <summary>프로젝트 묶음을 펼치거나 접는다(목록 전체를 다시 만들지 않고 프로젝트 칸 뒤에 넣고 뺌). 상태는 기억한다.</summary>
    private void SetGroupExpanded(string project, bool expand)
    {
        if (expand) _expanded.Add(project);
        else
        {
            _expanded.Remove(project);
            _searchExpanded.Remove(project);
        }
        if (!_items.TryGetValue(project, out var head) || !_children.TryGetValue(project, out var children)) return;
        if (children.Any(_items.ContainsKey) != expand)
        {
            bool suppress = _suppressActivate;
            _suppressActivate = true;
            try
            {
                if (expand)
                {
                    var pending = new List<(string Path, DateTime Stamp)>();
                    int at = _list.Items.IndexOf(head.Container) + 1;
                    foreach (var child in children) AddItem(child, project, pending, at++);
                    if (pending.Count > 0) LoadThumbnails(pending);
                }
                else
                {
                    _endRename?.Invoke(false); // 접혀 사라질 칸의 이름 바꾸기
                    foreach (var child in children)
                        if (_items.Remove(child, out var item)) _list.Items.Remove(item.Container);
                }
            }
            finally
            {
                _suppressActivate = suppress;
            }
            ApplyGroupStyle();
        }
        UpdateGroupArrow(head, children.Count);
    }

    /// <summary>목록의 파일 칸(접힌 묶음 안이면 묶음을 펼쳐서). 지금 목록에 없으면 null.</summary>
    private FileItem? RevealItem(string path)
    {
        if (_items.TryGetValue(path, out var item)) return item;
        if (!_parentOf.TryGetValue(path, out var project)) return null;
        SetGroupExpanded(project, true);
        return _items.GetValueOrDefault(path);
    }

    /// <summary>
    /// 펼친 묶음을 강조색 테두리 띠로 잇는다: 프로젝트 칸부터 마지막 딸린 칸까지 위아래 선이 이어지고, 양 끝(프로젝트 왼쪽·마지막 파일 오른쪽)에만
    /// 옆 선과 둥근 모서리, 칸 사이 틈 없음 — 줄이 넘어가면 줄 끝이 열려 있어 다음 줄로 이어짐이 보인다.
    /// 테두리는 칸 내용을 감싼 <see cref="FileItem.Frame"/>에 그려 마우스를 올리거나 골라도(칸 바탕이 바뀌어도) 가려지지 않는다. 나머지 칸은 기본 모양.
    /// </summary>
    private void ApplyGroupStyle()
    {
        var items = _list.Items;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not ListBoxItem c || c.Tag is not FileItem item) continue;
            var next = i + 1 < items.Count ? (items[i + 1] as ListBoxItem)?.Tag as FileItem : null;
            bool head = item.Parent is null && next?.Parent is { } p && string.Equals(p, item.Path, StringComparison.OrdinalIgnoreCase);
            var frame = item.Frame;
            if (!head && item.Parent is null)
            {
                frame.ClearValue(Border.BackgroundProperty);
                frame.ClearValue(Border.BorderBrushProperty);
                frame.ClearValue(Border.BorderThicknessProperty);
                frame.ClearValue(Border.CornerRadiusProperty);
                c.ClearValue(ListBoxItem.CornerRadiusProperty);
                c.Margin = new Thickness(1);
                c.VerticalAlignment = VerticalAlignment.Top;
                continue;
            }
            bool last = item.Parent is not null && !string.Equals(next?.Parent, item.Parent, StringComparison.OrdinalIgnoreCase);
            var corners = new CornerRadius(head ? 5 : 0, last ? 5 : 0, last ? 5 : 0, head ? 5 : 0);
            frame.Background = GroupBandBrush;
            frame.BorderBrush = GroupLineBrush;
            frame.BorderThickness = new Thickness(head ? GroupLine : 0, GroupLine, last ? GroupLine : 0, GroupLine);
            frame.CornerRadius = corners;
            // 마우스 올림·고름 바탕도 테두리 모양에 맞춰 둥글게.
            c.CornerRadius = corners;
            c.Margin = new Thickness(head ? 1 : 0, 1, last ? 1 : 0, 1);
            c.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    /// <summary>
    /// Ctrl+→ = 고른 프로젝트(또는 고른 딸린 파일의 프로젝트) 묶음 펼치기, Ctrl+← = 접기(딸린 파일을 골랐으면 프로젝트를 고름, 열지는 않음).
    /// 그냥 ←→는 목록 칸 이동 그대로. 이름 바꾸기 입력란의 Ctrl+←→(단어 이동)는 건드리지 않는다.
    /// </summary>
    private void OnGroupKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right) || !Ui.HasCommand(e.KeyModifiers)
            || (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Alt)) != 0) return;
        if (e.Source is TextBox || (e.Source as Visual)?.FindAncestorOfType<TextBox>() is not null) return;
        if (SelectedPaths.Count != 1 || SelectedItem is not { } item) return;
        var project = item.Parent ?? item.Path;
        if (!_children.ContainsKey(project)) return;
        bool expand = e.Key == Key.Right;
        if (!expand && item.Parent is not null)
        {
            SelectFile(project);
            _items.GetValueOrDefault(project)?.Container.Focus();
        }
        SetGroupExpanded(project, expand);
        e.Handled = true;
    }
}
