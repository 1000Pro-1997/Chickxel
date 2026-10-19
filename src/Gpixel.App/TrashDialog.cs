namespace Gpixel.App;

/// <summary>앱 휴지통 보기: 지운 항목 목록(최근 것부터), 되살리기·영구 삭제·비우기, 보관 기간.</summary>
public sealed class TrashDialog : DialogBase
{
    private readonly AppTrash _trash;
    private readonly ListBox _list;
    private readonly NumericUpDown _days;
    private readonly Action<IReadOnlyList<string>> _restored;
    private List<AppTrash.Entry> _entries = new();

    /// <param name="days">보관 기간(일).</param>
    /// <param name="daysChanged">보관 기간을 바꿨을 때(설정 저장).</param>
    /// <param name="restored">되살린 파일 경로들.</param>
    public TrashDialog(AppTrash trash, int days, Action<int> daysChanged, Action<IReadOnlyList<string>> restored) : base("앱 휴지통")
    {
        _trash = trash;
        _restored = restored;
        CloseOnly();
        _list = new ListBox
        {
            Width = 520,
            Height = 300,
            SelectionMode = SelectionMode.Multiple,
            Background = AppTheme.InputBrush,
            Foreground = AppTheme.TextBrush,
        };
        _days = Ui.Num(1, 365, days, 70);
        _days.ValueChanged += (_, _) =>
        {
            daysChanged((int)_days.NumValue());
            Reload();
        };
        var restore = Ui.Button("되살리기", (_, _) => RestoreSelected(), 100);
        var delete = Ui.Button("영구 삭제", (_, _) => DeleteSelected(), 100);
        var empty = Ui.Button("비우기", (_, _) => EmptyAll(), 100);
        Body.Children.Add(Ui.Label("파일 브라우저에서 지운 파일(합치기로 지운 원본 포함)을 보관합니다. 파일 목록에서 Ctrl+Z로 마지막 삭제를 되살립니다."));
        Body.Children.Add(Ui.Row(Ui.Label("보관 기간"), _days, Ui.Label("일 — 지나면 영구 삭제")));
        Body.Children.Add(_list);
        Body.Children.Add(Ui.Row(restore, delete, empty));
        _list.DoubleTapped += (_, _) => RestoreSelected();
        Reload();
    }

    private int Days => (int)_days.NumValue();

    private void Reload()
    {
        _trash.Purge(Days);
        _entries = _trash.Entries();
        _list.Items.Clear();
        foreach (var e in _entries)
        {
            var files = string.Join(", ", e.Files.Select(f => Path.GetFileName(f.OriginalPath)));
            var item = new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = $"{e.DeletedUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {e.Label} · {AppTrash.DaysLeft(e, Days)}일 남음\n   {files}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                Tag = e,
            };
            ToolTip.SetTip(item, string.Join("\n", e.Files.Select(f => f.OriginalPath)));
            _list.Items.Add(item);
        }
        if (_entries.Count == 0) _list.Items.Add(new ListBoxItem { Content = "비어 있음", IsEnabled = false });
    }

    private List<AppTrash.Entry> Selected()
        => (_list.SelectedItems?.OfType<ListBoxItem>() ?? []).Select(i => i.Tag).OfType<AppTrash.Entry>().ToList();

    private void RestoreSelected()
    {
        var restored = new List<string>();
        var errors = new List<string>();
        foreach (var e in Selected())
        {
            try { restored.AddRange(_trash.Restore(e)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{e.Label}: {ex.Message}"); }
        }
        if (restored.Count > 0) _restored(restored);
        if (errors.Count > 0) Ui.Error(this, string.Join("\n", errors));
        Reload();
    }

    private void DeleteSelected()
    {
        var sel = Selected();
        if (sel.Count == 0) return;
        if (Ui.Ask(this, $"항목 {sel.Count}개를 영구 삭제할까요? 되돌릴 수 없습니다.", "영구 삭제", MessageButtons.OKCancel, MessageIcon.Warning) != DialogResult.OK) return;
        Remove(sel);
    }

    private void EmptyAll()
    {
        if (_entries.Count == 0) return;
        if (Ui.Ask(this, $"휴지통의 항목 {_entries.Count}개를 모두 영구 삭제할까요? 되돌릴 수 없습니다.", "비우기", MessageButtons.OKCancel, MessageIcon.Warning) != DialogResult.OK) return;
        Remove(_entries);
    }

    private void Remove(IEnumerable<AppTrash.Entry> entries)
    {
        try
        {
            foreach (var e in entries.ToList()) _trash.Delete(e);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, $"일부 항목을 지우지 못했습니다.\n{ex.Message}");
        }
        Reload();
    }
}
