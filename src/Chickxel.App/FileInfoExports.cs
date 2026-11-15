using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>상세 정보 창이 파일 목록·메인 창에 맡기는 일(프로젝트의 내보내기 목록).</summary>
/// <param name="FindOpen">그 프로젝트가 탭에 열려 있으면 그 프로젝트.</param>
/// <param name="Trash">파일들(+딸린 .meta 등)을 앱 휴지통으로(이름표). 성공하면 true.</param>
/// <param name="OpenExportSettings">그 프로젝트를 열고(열려 있으면 그 탭으로) 내보내기 창을 띄워 목록 줄(−1 = 이미지 시트, 0 이상 = 애니메이션 줄)을 고른다.</param>
public sealed record FileInfoHost(Func<string, SpriteProject?> FindOpen, Func<IReadOnlyList<string>, string, bool> Trash, Action<string, int> OpenExportSettings);

/// <summary>
/// 상세 정보 창(<see cref="FileInfoDialog"/>)의 프로젝트 내보내기 목록: 내보내기 창의 목록과 같은 줄(이미지 시트, GIF·APNG·프레임마다 PNG 줄)마다
/// [저장할 때] 종류·범위 [설정…], 그 아래 만들어질 파일(있으면 크기, 없으면 "저장하면 만들어짐" — 위치 보기·휴지통), 그리고 지난번에 내보냈지만
/// 지금 목록으로는 만들지 않는 파일.
/// 체크를 바꾸면 탭에 열린 프로젝트는 그 프로젝트에(실행취소 한 단계, 저장해야 파일에 들어감), 닫힌 프로젝트는 디스크의 최신 파일을 읽어 그 값만 바꿔
/// 바로 저장한다(내보내기는 하지 않음 — 읽을 때 바뀐 것(경고)이 있으면 저장하지 않음).
/// </summary>
public sealed class FileInfoExports
{
    private readonly string _path;
    private readonly FileInfoHost _host;
    private readonly StackPanel _panel = new() { Spacing = 2, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _status = new() { Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, IsVisible = false };
    private SpriteProject? _project;
    private bool _open;

    public FileInfoExports(string path, FileInfoHost host)
    {
        _path = path;
        _host = host;
    }

    /// <summary>창에 넣을 칸(읽기 전에는 비어 있음).</summary>
    public Control View => _panel;

    /// <summary>프로젝트를 받아 칸을 채운다(UI 스레드). 탭에 열려 있으면 그 프로젝트를 쓴다.</summary>
    public void Show(SpriteProject loaded)
    {
        var open = _host.FindOpen(_path);
        _open = open is not null;
        _project = open ?? loaded;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_project is not { } p) return;
        var x = p.Export;
        _panel.Children.Clear();
        _panel.Children.Add(Ui.Header(UiStrings.ExportList_Title));
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void SheetRows(ExportSettings sheet, Action<ExportSettings, bool> set, int entry)
        {
            _panel.Children.Add(EntryRow(SheetLabel(sheet), sheet.ExportOnSave, set, entry));
            using var _ = ProjectExport.UseSheet(sheet);
            var (frames, parts) = ProjectExport.GetOutputPaths(p, _path, assumePartContent: true);
            foreach (var f in frames.Concat(parts).Concat(ProjectExport.GetLibraryPaths(p, _path, assumePartContent: true))
                         .Concat(ProjectExport.GetUnityAssetPaths(p, _path, assumePartContent: true)))
                AddFiles([f], shown, sheet.ExportOnSave);
        }
        if (x.SheetListed) SheetRows(x, (e, v) => e.ExportOnSave = v, -1);
        for (int i = 0; i < x.Entries.Count; i++)
        {
            int index = i;
            void Set(ExportSettings e, bool v)
            {
                if (index < e.Entries.Count) e.Entries[index].OnSave = v;
            }
            if (x.Entries[i].Sheet is { } sheet)
            {
                SheetRows(sheet, Set, index);
                continue;
            }
            if (x.Entries[i].Animation is not { } a) continue;
            var label = $"{Path.GetFileName(a.Path.TrimEnd('/', '\\'))} — {KindName(a.Format)} · {a.TagName ?? UiStrings.ExportList_AllFrames} · {a.Scale}×";
            _panel.Children.Add(EntryRow(label, a.OnSave, Set, index));
            var outputs = AnimationExportWriter.OutputPaths(p, a, _path);
            if (outputs.Count > 0) AddFiles(outputs, shown, a.OnSave);
        }
        if (_panel.Children.Count == 1)
            _panel.Children.Add(new TextBlock { Text = UiStrings.ExportList_Empty, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });

        // 지난 기록에 있지만 지금 목록으로는 만들지 않는(아직 디스크에 있는) 결과(.meta는 그 이미지와 함께)
        var leftovers = (p.ExportedFiles ?? [])
            .Where(f => !shown.Contains(f) && !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && File.Exists(f))
            .ToList();
        if (leftovers.Count > 0)
        {
            _panel.Children.Add(new TextBlock { Text = UiStrings.FileInfo_ExportLeftovers, Foreground = AppTheme.DimTextBrush, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
            foreach (var f in leftovers) AddFiles([f], shown, onSave: false);
        }
        _panel.Children.Add(_status);
    }

    /// <summary>시트 줄 이름: "hero.png — 이미지 시트" (+ Unity).</summary>
    private string SheetLabel(ExportSettings sheet)
        => $"{sheet.FileName ?? Path.GetFileNameWithoutExtension(_path)}{sheet.ImageFormat.Extension()} — {UiStrings.ExportList_Sheet}" + SheetExportPages.EngineSuffix(sheet.Engine);

    private static string KindName(AnimationExportFormat f) => f switch
    {
        AnimationExportFormat.Apng => UiStrings.Export_FormatApng,
        AnimationExportFormat.FramePngs => UiStrings.Export_FormatFrames,
        _ => UiStrings.S_GifExportDialog_ChooseAndExport_AC2A7BE712,
    };

    /// <summary>목록 줄: [저장할 때] 종류·범위 [설정…].</summary>
    private Control EntryRow(string label, bool onSave, Action<ExportSettings, bool> set, int entry)
    {
        // 이름의 _가 단축키 표시로 먹히지 않게 글자 칸으로 넣는다
        var check = new CheckBox { IsChecked = onSave, Content = new TextBlock { Text = label }, VerticalAlignment = VerticalAlignment.Center }.Tip(UiStrings.ExportList_OnSaveTip);
        check.IsCheckedChanged += (_, _) =>
        {
            bool value = check.On();
            Apply(e => set(e, value));
        };
        var settings = Ui.Button(UiStrings.ExportList_Settings, (_, _) =>
        {
            // 상세 정보 창을 닫고(모달 위에 모달을 띄우지 않게) 프로젝트를 열어 내보내기 창의 그 줄로
            (TopLevel.GetTopLevel(_panel) as Window)?.Close();
            Dispatcher.UIThread.Post(() => _host.OpenExportSettings(_path, entry));
        });
        settings.Margin = new Thickness(8, 0, 0, 0);
        var row = Ui.Row(check, settings);
        row.Margin = new Thickness(0, 6, 0, 0);
        return row;
    }

    /// <summary>
    /// 결과 파일 줄: 하나면 이름·크기(+.meta), 여러 개(프레임마다 PNG)면 첫 파일 이름과 나머지 개수 — 위치 보기·휴지통(모두).
    /// 아직 없으면 저장할 때 만드는 줄은 "저장하면 만들어짐", 아니면 "아직 없음".
    /// </summary>
    private void AddFiles(IReadOnlyList<string> files, HashSet<string> shown, bool onSave)
    {
        foreach (var f in files) shown.Add(f);
        var existing = files.Where(File.Exists).ToList();
        string detail;
        if (existing.Count == 0) detail = onSave ? UiStrings.FileInfo_ExportNotYet : UiStrings.FileInfo_ExportNone;
        else
        {
            detail = AppTrash.FormatSize(existing.Sum(f => new FileInfo(f).Length));
            if (existing.Any(f => File.Exists(UnityMeta.GetMetaPath(f)))) detail += " + .meta";
        }
        var nameText = files.Count == 1 ? Path.GetFileName(files[0]) : UiStrings.FormatFileInfo_ExportMany(first: Path.GetFileName(files[0]), count: files.Count - 1);
        var name = new SelectableTextBlock { Text = nameText, Foreground = existing.Count > 0 ? AppTheme.TextBrush : AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 300, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, files.Count == 1 ? files[0] : Path.GetDirectoryName(files[0]));
        var row = Ui.Row(name, new TextBlock { Text = detail, Foreground = AppTheme.DimTextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) });
        row.Margin = new Thickness(26, 0, 0, 0);
        if (existing.Count > 0)
        {
            row.Children.Add(Ui.IconButton(VectorIcon.FolderOpen, UiStrings.FileInfo_ExportReveal, (_, _) => Reveal(existing[0])));
            row.Children.Add(Ui.IconButton(VectorIcon.Trash, UiStrings.FileInfo_ExportTrash, (_, _) => TrashFiles(existing, nameText)));
        }
        _panel.Children.Add(row);
    }

    private void Reveal(string file)
    {
        try
        {
            Platform.RevealInFileManager(file);
        }
        catch (IOException ex)
        {
            Say(ex.Message);
        }
    }

    /// <summary>결과 파일들과 딸린 .meta·사이드카를 앱 휴지통으로(파일 목록의 Ctrl+Z로 되살림). 저장할 때 만드는 줄은 다음 저장 때 다시 만들어진다.</summary>
    private void TrashFiles(IReadOnlyList<string> files, string label)
    {
        var companions = files.SelectMany(DocumentIO.GetCompanionPaths).Where(File.Exists).ToList();
        if (!_host.Trash([.. files, .. companions], label)) return;
        Rebuild();
        Say(UiStrings.FormatFileInfo_ExportTrashed(name: label));
    }

    /// <summary>
    /// 체크 하나를 프로젝트에 넣는다: 열린 프로젝트는 실행취소 한 단계로(저장해야 파일에 들어감), 닫힌 프로젝트는 디스크의 최신 파일에 바꿔 바로 저장.
    /// </summary>
    private void Apply(Action<ExportSettings> change)
    {
        if (_project is not { } p) return;
        if (_open)
        {
            p.Edit(UiStrings.ExportList_Undo, ProjectChange.Settings, () => change(p.Export));
            Rebuild();
            Say(UiStrings.FileInfo_ExportChangedOpen);
            return;
        }
        try
        {
            // 디스크의 최신 내용에 바꾼다(창을 띄운 뒤 다른 곳에서 고쳤어도 그 내용을 지우지 않게). 읽으며 바꾼 것(경고)이 있으면 쓰지 않는다.
            var warnings = new List<string>();
            var fresh = ProjectIO.Load(_path, warnings);
            if (warnings.Count > 0)
            {
                Say(UiStrings.FileInfo_ExportNeedsOpen);
                Rebuild();
                return;
            }
            change(fresh.Export);
            ProjectIO.Save(fresh, _path);
            _project = fresh;
            Rebuild();
            Say(UiStrings.FileInfo_ExportChangedSaved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        {
            Say(ex.Message);
            Rebuild();
        }
    }

    private void Say(string text)
    {
        _status.Text = text;
        _status.IsVisible = true;
    }
}
